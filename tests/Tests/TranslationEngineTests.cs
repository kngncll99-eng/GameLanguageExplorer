namespace GameLanguageExplorer.Tests;

using System.IO;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Translation;
using GameLanguageExplorer.Data.Services;

public class TranslationEngineTests
{
    private const string FileSha = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    private static TranslationProviderProfile Profile(string model = "test-model") => new()
    {
        ProviderId = "test-provider",
        Endpoint = new Uri("http://127.0.0.1:1234/v1"),
        Model = model,
        GenerationOptions = new TranslationGenerationOptions
        {
            Timeout = TimeSpan.FromSeconds(5),
            MaxOutputTokens = 512
        }
    };

    private static LocalizationTextEntry Entry(
        string key,
        string source,
        LocalizationEntryStatus status = LocalizationEntryStatus.Translatable,
        string? existing = null,
        string? context = null,
        string format = "XML")
        => new()
        {
            Key = key,
            SourceText = source,
            Kind = LocalizationTextKind.XmlElementText,
            Status = status,
            Context = context,
            ExistingTranslation = existing,
            Evidence = "test",
            FormatKey = format
        };

    private static async Task<(TempTree Tree, string Path, string Sha)> CreateSourceAsync(
        string content = "<root/>")
    {
        var tree = new TempTree();
        var path = tree.Write("source.xml", content);
        var sha = await FileFingerprint.ComputeSha256Async(path);
        return (tree, path, sha);
    }

    private static TranslationEngineRequest Request(
        string path,
        string sha,
        IReadOnlyList<LocalizationTextEntry> entries,
        TranslationEngineOptions? options = null,
        bool truncated = false)
        => new()
        {
            InstalledGameId = 10,
            DiscoveredFileId = 20,
            FilePath = path,
            FileFormat = "XML",
            SourceFileSha256 = sha,
            Entries = entries,
            SourceTruncated = truncated,
            SourceLanguage = "English",
            TargetLanguage = "Turkish",
            Provider = Profile(),
            Options = options ?? new TranslationEngineOptions
            {
                MaxAttemptsPerBatch = 1,
                RetryBaseDelay = TimeSpan.Zero,
                MaxRetryDelay = TimeSpan.Zero
            }
        };

    private sealed class StubProvider : ITranslationProvider
    {
        private readonly Func<int, TranslationProviderRequest, CancellationToken, Task<TranslationProviderResponse>> _handler;

        public StubProvider(
            Func<int, TranslationProviderRequest, CancellationToken, Task<TranslationProviderResponse>> handler)
        {
            _handler = handler;
        }

        public int Calls { get; private set; }

        public List<TranslationProviderRequest> Requests { get; } = new();

        public TranslationProviderDescriptor Descriptor { get; } = new()
        {
            ProviderId = "test-provider",
            DisplayName = "Test",
            SupportsBatching = true
        };

        public async Task<TranslationProviderResponse> TranslateAsync(
            TranslationProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Requests.Add(request);
            return await _handler(Calls, request, cancellationToken);
        }
    }

    private static StubProvider SuccessProvider(Func<string, string>? translate = null)
    {
        var translator = translate ?? (source => "Çeviri: " + source);
        return new StubProvider((_, request, _) => Task.FromResult(new TranslationProviderResponse
        {
            Candidates = request.Units
                .Select(unit => new TranslationCandidate(unit.UnitId, translator(unit.SourceText)))
                .ToArray(),
            Duration = TimeSpan.FromMilliseconds(5)
        }));
    }

    [Fact]
    public async Task TranslationMemory_ExactMatch_IsReusedWithoutProviderCall()
    {
        using var database = new TempDatabase();
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();

        using (var context = database.CreateContext())
        {
            context.TranslationMemoryEntries.Add(new TranslationMemoryEntry
            {
                SourceText = "Play",
                TargetText = "Oyna",
                SourceLanguage = "English",
                TargetLanguage = "Turkish",
                UsageCount = 1
            });
            await context.SaveChangesAsync();
        }

        var provider = new StubProvider((_, _, _) =>
            throw new InvalidOperationException("Bellek eşleşmesinde sağlayıcı çağrılmamalıydı."));

        var engine = new TranslationEngine(
            journal.Store,
            new TranslationMemoryService(database.CreateFactory()));

        var result = await engine.RunAsync(
            Request(path, sha, new[] { Entry("ui.play", "Play") }),
            provider);

        Assert.Equal(TranslationRunStatus.Completed, result.Status);
        Assert.Equal(0, provider.Calls);
        Assert.Single(result.Units);
        Assert.Equal(TranslationUnitStatus.Reused, result.Units[0].Status);
        Assert.Equal("Oyna", result.Units[0].Translation);

        using var readContext = database.CreateContext();
        var memory = readContext.TranslationMemoryEntries.Single();
        Assert.Equal(2, memory.UsageCount);
        Assert.NotNull(memory.LastUsedAt);
    }

    [Fact]
    public async Task TranslationMemory_SuccessfulTranslation_IsSaved()
    {
        using var database = new TempDatabase();
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();

        var engine = new TranslationEngine(
            journal.Store,
            new TranslationMemoryService(database.CreateFactory()));

        var result = await engine.RunAsync(
            Request(path, sha, new[] { Entry("ui.play", "Play") }),
            SuccessProvider());

        Assert.Equal(TranslationRunStatus.Completed, result.Status);
        Assert.Equal(TranslationUnitStatus.Candidate, result.Units[0].Status);

        using var readContext = database.CreateContext();
        var memory = readContext.TranslationMemoryEntries.Single();
        Assert.Equal("Play", memory.SourceText);
        Assert.Equal("Çeviri: Play", memory.TargetText);
        Assert.Equal("English", memory.SourceLanguage);
        Assert.Equal("Turkish", memory.TargetLanguage);
        Assert.Equal(1, memory.UsageCount);
    }

    [Fact]
    public async Task SimpleTranslatableEntries_ProduceCandidateResults()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider();
        var entries = new[]
        {
            Entry("ui.inventory", "Open inventory"),
            Entry("ui.settings", "Open settings panel")
        };

        var result = await new TranslationEngine(journal.Store).RunAsync(Request(path, sha, entries), provider);

        Assert.Equal(TranslationRunStatus.Completed, result.Status);
        Assert.Equal(2, result.Units.Count);
        Assert.All(result.Units, unit => Assert.Equal(TranslationUnitStatus.Candidate, unit.Status));
        Assert.Equal(2, result.Metrics.CandidateRecords);
        Assert.Equal(1, result.Metrics.RequestCount);
        Assert.Equal(1, provider.Calls);
        Assert.True(File.Exists(result.JournalPath));
        var journalText = await File.ReadAllTextAsync(result.JournalPath!);
        Assert.Contains("\"fileFormat\": \"XML\"", journalText, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", journalText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(journal.Root, "*.tmp"));
        Assert.Equal(sha, await FileFingerprint.ComputeSha256Async(path));
    }

    [Fact]
    public async Task CompletedJournal_IsResumedWithoutSecondProviderCall()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var entries = new[]
        {
            Entry("ui.start", "Start"),
            Entry("ui.stop", "Stop")
        };

        var first = await new TranslationEngine(journal.Store).RunAsync(
            Request(path, sha, entries),
            SuccessProvider());

        Assert.Equal(2, first.Metrics.CandidateRecords);
        Assert.False(first.Resumed);

        var secondProvider = new StubProvider((_, _, _) =>
            throw new InvalidOperationException("Günlükten geri yüklenen kayıt için sağlayıcı çağrılmamalıydı."));

        var second = await new TranslationEngine(journal.Store).RunAsync(
            Request(path, sha, entries),
            secondProvider);

        Assert.True(second.Resumed);
        Assert.Equal(0, secondProvider.Calls);
        Assert.Equal(2, second.Metrics.CandidateRecords);
        Assert.Equal(0, second.Metrics.RequestCount);
        Assert.All(second.Units, unit => Assert.Equal(TranslationUnitStatus.Candidate, unit.Status));
        Assert.Contains(second.Units, unit => unit.Translation == "Çeviri: Start");
    }

    [Fact]
    public async Task AmbiguousAndSkippedEntries_AreNotSentToModel()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider();
        var entries = new[]
        {
            Entry("ui.start", "Start"),
            Entry("ui.number", "123", LocalizationEntryStatus.Ambiguous),
            Entry("ui.structural", "id", LocalizationEntryStatus.Skipped)
        };

        var result = await new TranslationEngine(journal.Store).RunAsync(Request(path, sha, entries), provider);

        Assert.Equal(2, result.Units.Count(u => u.Status == TranslationUnitStatus.Skipped));
        Assert.Single(result.Units, u => u.Status == TranslationUnitStatus.Candidate);
        Assert.Single(provider.Requests);
        Assert.Single(provider.Requests[0].Units);
        Assert.Equal(2, result.Metrics.SkippedRecords);
    }

    [Fact]
    public async Task IdenticalSourceAndContext_AreGroupedIntoSingleModelUnit()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider();
        var entries = new[]
        {
            Entry("ui.attack.1", "Attack now", context: "button"),
            Entry("ui.attack.2", "Attack now", context: "button")
        };

        var result = await new TranslationEngine(journal.Store).RunAsync(Request(path, sha, entries), provider);

        Assert.Equal(2, result.Units.Count(u => u.Status == TranslationUnitStatus.Candidate));
        Assert.Single(provider.Requests);
        Assert.Single(provider.Requests[0].Units);
        Assert.Equal(2, result.Units.Select(u => u.UnitId).Distinct().Count());
    }

    [Fact]
    public async Task Placeholder_IsMaskedAndRestoredOnce()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider(source => "Basın " + source);
        var entries = new[] { Entry("action.attack", "Press {0} to attack") };

        var result = await new TranslationEngine(journal.Store).RunAsync(Request(path, sha, entries), provider);

        var unit = Assert.Single(result.Units);
        Assert.Equal(TranslationUnitStatus.Candidate, unit.Status);
        Assert.NotNull(unit.Translation);
        Assert.Equal(1, CountOccurrences(unit.Translation!, "{0}"));
        Assert.DoesNotContain("__GLE_PH", unit.Translation!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingPlaceholder_IsRejectedAsFailed()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider(_ => "Saldır");
        var entries = new[] { Entry("action.attack", "Press {0} to attack") };

        var result = await new TranslationEngine(journal.Store).RunAsync(Request(path, sha, entries), provider);

        var unit = Assert.Single(result.Units);
        Assert.Equal(TranslationUnitStatus.Failed, unit.Status);
        Assert.Null(unit.Translation);
    }

    [Fact]
    public async Task EmptyModelOutput_IsNeedsReviewNotCandidate()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider(_ => string.Empty);
        var entries = new[] { Entry("ui.save", "Save") };

        var result = await new TranslationEngine(journal.Store).RunAsync(Request(path, sha, entries), provider);

        Assert.Equal(TranslationUnitStatus.NeedsReview, Assert.Single(result.Units).Status);
        Assert.Equal(TranslationRunStatus.CompletedWithReview, result.Status);
    }

    [Fact]
    public async Task TrustedExistingTranslation_IsReusedWithoutModelCall()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider();
        var entries = new[] { Entry("ui.settings", "Settings", existing: "Ayarlar") };

        var result = await new TranslationEngine(journal.Store).RunAsync(Request(path, sha, entries), provider);

        var unit = Assert.Single(result.Units);
        Assert.Equal(TranslationUnitStatus.Reused, unit.Status);
        Assert.Equal("Ayarlar", unit.Translation);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task DuplicateEntryKeys_AreFailedWithoutModelCall()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider();
        var entries = new[]
        {
            Entry("same.key", "First"),
            Entry("same.key", "Second")
        };

        var result = await new TranslationEngine(journal.Store).RunAsync(Request(path, sha, entries), provider);

        Assert.Equal(2, result.Units.Count(u => u.Status == TranslationUnitStatus.Failed));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task TruncatedPreview_IsRejectedBeforeModelCall()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider();

        var result = await new TranslationEngine(journal.Store).RunAsync(
            Request(path, sha, new[] { Entry("ui.a", "Start") }, truncated: true),
            provider);

        Assert.Equal(TranslationRunStatus.Failed, result.Status);
        Assert.Equal(TranslationRunFailureKind.SourceTruncated, result.FailureKind);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ChangedSourceHash_IsRejectedBeforeModelCall()
    {
        var (tree, path, _) = await CreateSourceAsync();
        using var treeScope = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider();

        var result = await new TranslationEngine(journal.Store).RunAsync(
            Request(path, new string('A', 64), new[] { Entry("ui.a", "Start") }),
            provider);

        Assert.Equal(TranslationRunStatus.Failed, result.Status);
        Assert.Equal(TranslationRunFailureKind.SourceChanged, result.FailureKind);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task TransientProviderFailure_IsRetried()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = new StubProvider((call, request, _) =>
        {
            if (call == 1)
            {
                throw new TranslationProviderException(new TranslationProviderError(
                    TranslationProviderFailureKind.Network,
                    "geçici ağ hatası",
                    IsTransient: true));
            }

            return Task.FromResult(ResponseFor(request.Units));
        });

        var options = new TranslationEngineOptions
        {
            MaxAttemptsPerBatch = 2,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero
        };

        var result = await new TranslationEngine(journal.Store).RunAsync(
            Request(path, sha, new[] { Entry("ui.a", "Start") }, options),
            provider);

        Assert.Equal(TranslationRunStatus.Completed, result.Status);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(1, result.Metrics.RetryCount);
    }

    [Fact]
    public async Task CompletedJournal_IsResumedWithoutModelCall()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var entries = new[]
        {
            Entry("ui.a", "Start"),
            Entry("ui.b", "Continue")
        };
        var request = Request(path, sha, entries);

        var first = await new TranslationEngine(journal.Store).RunAsync(request, SuccessProvider());
        var noCallProvider = new StubProvider((_, _, _) =>
            throw new InvalidOperationException("İkinci çalışmada model çağrılmamalıydı."));

        var second = await new TranslationEngine(journal.Store).RunAsync(request, noCallProvider);

        Assert.True(second.Resumed);
        Assert.Equal(TranslationRunStatus.Completed, second.Status);
        Assert.Equal(0, noCallProvider.Calls);
        Assert.Equal(0, second.Metrics.RequestCount);
        Assert.All(second.Units, unit => Assert.Equal(TranslationUnitStatus.Candidate, unit.Status));
        Assert.Equal(first.Units.Select(u => u.Translation), second.Units.Select(u => u.Translation));
    }

    [Fact]
    public async Task CorruptJournal_IsReportedAndNotSilentlyRestarted()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var request = Request(path, sha, new[] { Entry("ui.a", "Start") });
        var first = await new TranslationEngine(journal.Store).RunAsync(request, SuccessProvider());
        await File.WriteAllTextAsync(first.JournalPath!, "{bozuk-json");

        var provider = SuccessProvider();
        var second = await new TranslationEngine(journal.Store).RunAsync(request, provider);

        Assert.Equal(TranslationRunStatus.Failed, second.Status);
        Assert.Equal(TranslationRunFailureKind.JournalCorrupt, second.FailureKind);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task BatchLimits_AreRespected()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider();
        var entries = Enumerable.Range(1, 5)
            .Select(index => Entry($"ui.{index}", "Menu item " + index))
            .ToArray();
        var options = new TranslationEngineOptions
        {
            MaxUnitsPerBatch = 2,
            MaxAttemptsPerBatch = 1,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero
        };

        var result = await new TranslationEngine(journal.Store).RunAsync(
            Request(path, sha, entries, options),
            provider);

        Assert.Equal(TranslationRunStatus.Completed, result.Status);
        Assert.Equal(3, provider.Calls);
        Assert.Equal(new[] { 2, 2, 1 }, provider.Requests.Select(request => request.Units.Count));
        Assert.Equal(3, result.Metrics.BatchCount);
    }

    [Fact]
    public async Task InvalidBatch_IsSplitAndSuccessfulUnitsAreKept()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = new StubProvider((_, request, _) =>
        {
            var candidates = request.Units
                .Take(Math.Max(1, request.Units.Count - 1))
                .Select(unit => new TranslationCandidate(unit.UnitId, "Çeviri: " + unit.SourceText))
                .ToArray();
            return Task.FromResult(new TranslationProviderResponse { Candidates = candidates });
        });
        var entries = new[]
        {
            Entry("ui.a", "Begin battle"),
            Entry("ui.b", "Continue game")
        };
        var options = new TranslationEngineOptions
        {
            MaxUnitsPerBatch = 20,
            MaxAttemptsPerBatch = 1,
            MaxSplitDepth = 2,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero
        };

        var result = await new TranslationEngine(journal.Store).RunAsync(
            Request(path, sha, entries, options),
            provider);

        Assert.Equal(TranslationRunStatus.Completed, result.Status);
        Assert.All(result.Units, unit => Assert.Equal(TranslationUnitStatus.Candidate, unit.Status));
        Assert.True(provider.Calls >= 3);
    }

    [Fact]
    public async Task UserCancellation_PreservesCompletedBatchAndJournal()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        using var cancellation = new CancellationTokenSource();
        var secondCallStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new StubProvider(async (call, request, token) =>
        {
            if (call == 1)
            {
                return ResponseFor(request.Units);
            }

            secondCallStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return ResponseFor(request.Units);
        });
        var entries = new[]
        {
            Entry("ui.a", "Start"),
            Entry("ui.b", "Continue game")
        };
        var options = new TranslationEngineOptions
        {
            MaxUnitsPerBatch = 1,
            MaxAttemptsPerBatch = 1,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero
        };

        var runTask = new TranslationEngine(journal.Store).RunAsync(
            Request(path, sha, entries, options),
            provider,
            null,
            cancellation.Token);
        await secondCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var result = await runTask;

        Assert.Equal(TranslationRunStatus.Cancelled, result.Status);
        Assert.Equal(1, result.Units.Count(u => u.Status == TranslationUnitStatus.Candidate));
        Assert.Equal(2, provider.Calls);

        var loaded = await journal.Store.TryLoadAsync(result.RunId);
        Assert.NotNull(loaded);
        Assert.Single(loaded!.Units);
        Assert.Equal("Cancelled", loaded.Status);
    }

    [Theory]
    [InlineData("Are you sure want to reset all properties? Your changes will be lost.",
        "Tüm özellikleri sıfırlamak istediğinizden emin misiniz? Değişiklikleriniz kaybolacak.")]
    [InlineData("From {WHISPER_TARGET}", "Kimden {WHISPER_TARGET}")]
    [InlineData("Toggle scoreboard", "Skor Tablosunu Aç/Kapat")]
    [InlineData("{PERCENTAGE}% of troop type", "{PERCENTAGE}% birlik türü")]
    public void ExactGlossaryLookup_CoversPlaceholderAndSentenceLabels(
        string source,
        string expected)
    {
        Assert.True(GameGlossaryCatalog.TryGetExactTranslation(source, "Turkish", out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ExactTurkishGlossaryLabel_IsCandidateWithoutModelCall()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider();
        var entries = new[] { Entry("ui.flipHair", "Flip Hair") };

        var result = await new TranslationEngine(journal.Store).RunAsync(Request(path, sha, entries), provider);

        var unit = Assert.Single(result.Units);
        Assert.Equal(TranslationUnitStatus.Candidate, unit.Status);
        Assert.Equal("Saçı Çevir", unit.Translation);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task NoTranslatableEntries_CompletesWithoutRequest()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = SuccessProvider();
        var entries = new[]
        {
            Entry("ui.a", "123", LocalizationEntryStatus.Ambiguous),
            Entry("ui.b", "id", LocalizationEntryStatus.Skipped)
        };

        var result = await new TranslationEngine(journal.Store).RunAsync(Request(path, sha, entries), provider);

        Assert.Equal(TranslationRunStatus.Completed, result.Status);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, result.Metrics.TranslatableRecords);
        Assert.Equal(2, result.Metrics.SkippedRecords);
    }

    [Fact]
    public async Task AllProviderRequestsFail_ReturnsExplicitProviderFailure()
    {
        var (tree, path, sha) = await CreateSourceAsync();
        using var _ = tree;
        using var journal = new JournalScope();
        var provider = new StubProvider((_, _, _) =>
            throw new TranslationProviderException(new TranslationProviderError(
                TranslationProviderFailureKind.Network,
                "sunucu yok",
                IsTransient: true)));

        var options = new TranslationEngineOptions
        {
            MaxAttemptsPerBatch = 1,
            MaxSplitDepth = 0,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero
        };

        var result = await new TranslationEngine(journal.Store).RunAsync(
            Request(path, sha, new[] { Entry("ui.a", "Start") }, options),
            provider);

        Assert.Equal(TranslationRunStatus.Failed, result.Status);
        Assert.Equal(TranslationRunFailureKind.Provider, result.FailureKind);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(TranslationUnitStatus.Failed, Assert.Single(result.Units).Status);
    }

    private static TranslationProviderResponse ResponseFor(
        IReadOnlyList<TranslationUnit> units,
        Func<string, string>? translate = null)
    {
        var translator = translate ?? (source => "Çeviri: " + source);
        return new TranslationProviderResponse
        {
            Candidates = units
                .Select(unit => new TranslationCandidate(unit.UnitId, translator(unit.SourceText)))
                .ToArray()
        };
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class JournalScope : IDisposable
    {
        public JournalScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "gle_journal_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public TranslationRunJournalStore Store => new(Root);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* en iyi çaba */ }
        }
    }
}
