namespace GameLanguageExplorer.Tests;

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;
using GameLanguageExplorer.Core.Services.Translation;

/// <summary>
/// Gerçek kurulu oyun dosyası + gerçek yerel LM Studio testi. Varsayılan test paketinde
/// çalışmaz; <c>GLE_REAL_LOCAL_TEST=1</c> verilerek açıkça istenmelidir. Sentetik test
/// sonucu ile karıştırılmaması için ayrı dosyada tutulur.
/// </summary>
/// <summary>
/// Yalnızca <c>GLE_REAL_LOCAL_TEST=1</c> verildiğinde keşfedilen gerçek entegrasyon
/// testi. Koşul sağlanmazsa xUnit testi açıkça <c>Skipped</c> olarak raporlar.
/// </summary>
public sealed class RealIntegrationFactAttribute : FactAttribute
{
    public RealIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GLE_REAL_LOCAL_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Gerçek dosya/LLM gerektirir. GLE_REAL_LOCAL_TEST=1 ve GLE_REAL_FILE, "
                   + "GLE_REAL_ENDPOINT, GLE_REAL_MODEL ortam değişkenleriyle ayrı çalıştırın.";
        }
    }
}

public class RealLocalTranslationIntegrationTests
{
    private static string RealFile => RequiredEnvironment("GLE_REAL_FILE");

    private static string Endpoint => RequiredEnvironment("GLE_REAL_ENDPOINT");

    private static string Model => RequiredEnvironment("GLE_REAL_MODEL");

    [RealIntegrationFact]
    public async Task RealBannerlordFile_TranslatesThroughLocalLmStudio()
    {
        Assert.True(File.Exists(RealFile), "Gerçek test dosyası bulunamadı: " + RealFile);

        var limitText = Environment.GetEnvironmentVariable("GLE_REAL_LIMIT");
        var limit = int.TryParse(limitText, out var parsed) && parsed > 0 ? parsed : 10;

        var adapter = new XmlLocalizationAdapter();
        var readStopwatch = Stopwatch.StartNew();
        var read = await adapter.ReadAsync(
            RealFile,
            new LocalizationFormatReadOptions { MaxFileBytes = 4 * 1024 * 1024 });
        readStopwatch.Stop();

        Assert.True(read.Success, read.ErrorMessage);
        Assert.False(read.Truncated);
        var selected = read.Entries
            .Where(entry => entry.Status == LocalizationEntryStatus.Translatable)
            .Take(limit)
            .ToArray();
        Assert.NotEmpty(selected);

        var hashStopwatch = Stopwatch.StartNew();
        var hash = await FileFingerprint.ComputeSha256Async(RealFile);
        hashStopwatch.Stop();
        var journalDirectory = Path.Combine(
            Path.GetTempPath(),
            "gle-real-journal-" + Guid.NewGuid().ToString("N"));

        var profile = OpenAiCompatibleTranslationProviderFactory.CreateProfile(
            "lmstudio-local",
            Endpoint,
            Model,
            "Yerel LM Studio",
            new TranslationGenerationOptions
            {
                Temperature = 0.2,
                MaxOutputTokens = 4096,
                Timeout = TimeSpan.FromMinutes(3)
            });

        using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var innerProvider = OpenAiCompatibleTranslationProviderFactory.Create(
            httpClient,
            profile,
            new EnvironmentTranslationCredentialProvider());
        var runStopwatch = new Stopwatch();
        var provider = new RecordingTranslationProvider(innerProvider, runStopwatch);

        var request = new TranslationEngineRequest
        {
            InstalledGameId = 0,
            DiscoveredFileId = 0,
            FilePath = RealFile,
            FileFormat = "XML",
            SourceFileSha256 = hash,
            Entries = selected,
            SourceLanguage = "English",
            TargetLanguage = "Turkish",
            Provider = profile,
            JournalDirectory = journalDirectory,
            Options = new TranslationEngineOptions
            {
                MaxUnitsPerBatch = 10,
                MaxSourceCharactersPerBatch = 6000,
                MaxAttemptsPerBatch = 2,
                MaxSplitDepth = 1
            }
        };

        var progress = new Progress<TranslationRunProgress>(_ => { });
        runStopwatch.Start();
        var result = await new TranslationEngine(new TranslationRunJournalStore(journalDirectory))
            .RunAsync(request, provider, progress);
        runStopwatch.Stop();

        var firstRequest = provider.Requests.FirstOrDefault();
        var firstRequestText = firstRequest is null
            ? "—"
            : $"{firstRequest.StartOffset.TotalSeconds + firstRequest.Duration.TotalSeconds:0.000}s " +
              $"(start {firstRequest.StartOffset.TotalSeconds:0.000}s + call {firstRequest.Duration.TotalSeconds:0.000}s)";
        var outputTokensPerSecond = result.Metrics.OutputTokens is int outputTokens
            && result.Metrics.TotalDuration.TotalSeconds > 0
                ? outputTokens / result.Metrics.TotalDuration.TotalSeconds
                : (double?)null;

        var summary = new StringBuilder()
            .AppendLine($"File: {RealFile}")
            .AppendLine($"SHA256: {hash}")
            .AppendLine($"Adapter records: {read.Entries.Count}, test records: {selected.Length}")
            .AppendLine($"Status: {result.Status}, failure: {result.FailureKind}")
            .AppendLine($"Error: {result.ErrorMessage ?? "—"}")
            .AppendLine($"RunId: {result.RunId}")
            .AppendLine($"Journal: {result.JournalPath}")
            .AppendLine($"Requests: {result.Metrics.RequestCount}, retries: {result.Metrics.RetryCount}, batches: {result.Metrics.BatchCount}")
            .AppendLine($"Duration: {result.Metrics.TotalDuration.TotalSeconds:0.00}s, avg request: {result.Metrics.AverageRequestDuration.TotalMilliseconds:0}ms")
            .AppendLine($"First request: {firstRequestText}")
            .AppendLine($"Read: {readStopwatch.Elapsed.TotalMilliseconds:0.0}ms, hash: {hashStopwatch.Elapsed.TotalMilliseconds:0.0}ms")
            .AppendLine($"Total request time: {result.Metrics.TotalRequestDuration.TotalSeconds:0.000}s, non-request engine time: {(result.Metrics.TotalDuration - result.Metrics.TotalRequestDuration).TotalSeconds:0.000}s")
            .AppendLine($"Records/s: {result.Metrics.RecordsPerSecond:0.000}, chars/s: {result.Metrics.CharactersPerSecond:0.0}")
            .AppendLine($"Tokens: in={result.Metrics.InputTokens?.ToString() ?? "—"}, out={result.Metrics.OutputTokens?.ToString() ?? "—"}")
            .AppendLine($"Output tokens/s (wall clock, not decode-only): {outputTokensPerSecond?.ToString("0.00") ?? "—"}")
            .AppendLine($"Candidate: {result.Metrics.CandidateRecords}, review: {result.Metrics.NeedsReviewRecords}, failed: {result.Metrics.FailedRecords}, skipped: {result.Metrics.SkippedRecords}")
            .AppendLine("Requests detail:");
        foreach (var exchange in provider.Requests)
        {
            summary.AppendLine(
                $"  #{exchange.Index} offset={exchange.StartOffset.TotalSeconds:0.000}s " +
                $"duration={exchange.Duration.TotalSeconds:0.000}s units={exchange.UnitCount} " +
                $"success={exchange.Success} in={exchange.InputTokens?.ToString() ?? "—"} " +
                $"out={exchange.OutputTokens?.ToString() ?? "—"} error={exchange.ErrorKind ?? "—"}");
        }

        summary.AppendLine("Results:");

        foreach (var unit in result.Units)
        {
            summary.AppendLine(
                $"[{unit.Status}] {unit.EntryKey} | {unit.SourceText.ReplaceLineEndings(" ")} => " +
                $"{(unit.Translation ?? "—").ReplaceLineEndings(" ")} | {unit.DecisionReason}");
        }

        var output = Path.Combine(
            Path.GetTempPath(),
            "gle-real-run-result-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(output, summary.ToString(), Encoding.UTF8);
        Assert.True(File.Exists(output));

        var export = new
        {
            result.RunId,
            Status = result.Status.ToString(),
            FailureKind = result.FailureKind.ToString(),
            result.ErrorMessage,
            result.JournalPath,
            SourceFileSha256 = hash,
            ReadMilliseconds = readStopwatch.Elapsed.TotalMilliseconds,
            HashMilliseconds = hashStopwatch.Elapsed.TotalMilliseconds,
            RunWallClockSeconds = runStopwatch.Elapsed.TotalSeconds,
            FirstRequestSeconds = firstRequest is null
                ? (double?)null
                : (firstRequest.StartOffset + firstRequest.Duration).TotalSeconds,
            Metrics = new
            {
                result.Metrics.TotalRecords,
                result.Metrics.TranslatableRecords,
                result.Metrics.CandidateRecords,
                result.Metrics.ReusedRecords,
                result.Metrics.NeedsReviewRecords,
                result.Metrics.FailedRecords,
                result.Metrics.SkippedRecords,
                result.Metrics.RequestCount,
                result.Metrics.RetryCount,
                result.Metrics.BatchCount,
                TotalDurationSeconds = result.Metrics.TotalDuration.TotalSeconds,
                TotalRequestDurationSeconds = result.Metrics.TotalRequestDuration.TotalSeconds,
                result.Metrics.InputTokens,
                result.Metrics.OutputTokens,
                result.Metrics.ProviderId,
                result.Metrics.Model,
                result.Metrics.PromptVersion,
                result.Metrics.TranslatedSourceCharacters
            },
            Requests = provider.Requests.Select(exchange => new
            {
                exchange.Index,
                StartOffsetSeconds = exchange.StartOffset.TotalSeconds,
                DurationSeconds = exchange.Duration.TotalSeconds,
                exchange.Success,
                exchange.UnitCount,
                exchange.InputTokens,
                exchange.OutputTokens,
                exchange.ErrorKind
            }).ToArray(),
            Units = result.Units.Select(unit => new
            {
                unit.EntryKey,
                Status = unit.Status.ToString(),
                unit.Attempts,
                unit.SourceText,
                unit.Translation,
                unit.DecisionReason,
                QualityCodes = unit.QualityIssues.Select(issue => issue.Code.ToString()).ToArray()
            }).ToArray()
        };
        var exportJson = JsonSerializer.Serialize(export, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        var exportPath = Path.Combine(
            Path.GetTempPath(),
            "gle-real-run-units-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(exportPath, exportJson, new UTF8Encoding(false));
        Assert.True(File.Exists(exportPath));

        Assert.True(
            result.Status is TranslationRunStatus.Completed or TranslationRunStatus.CompletedWithReview,
            result.ErrorMessage);
        Assert.True(result.Metrics.CandidateRecords + result.Metrics.NeedsReviewRecords > 0);
    }

    private sealed class RecordingTranslationProvider : ITranslationProvider
    {
        private readonly ITranslationProvider _inner;
        private readonly Stopwatch _clock;

        public RecordingTranslationProvider(ITranslationProvider inner, Stopwatch clock)
        {
            _inner = inner;
            _clock = clock;
        }

        public List<RecordedRequest> Requests { get; } = new();

        public TranslationProviderDescriptor Descriptor => _inner.Descriptor;

        public async Task<TranslationProviderResponse> TranslateAsync(
            TranslationProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            var index = Requests.Count + 1;
            var startOffset = _clock.Elapsed;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = await _inner.TranslateAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                stopwatch.Stop();
                Requests.Add(new RecordedRequest(
                    index,
                    startOffset,
                    stopwatch.Elapsed,
                    true,
                    request.Units.Count,
                    response.InputTokens,
                    response.OutputTokens,
                    null));
                return response;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                Requests.Add(new RecordedRequest(
                    index,
                    startOffset,
                    stopwatch.Elapsed,
                    false,
                    request.Units.Count,
                    null,
                    null,
                    exception.GetType().Name));
                throw;
            }
        }
    }

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Gerçek entegrasyon testi için '{name}' ortam değişkeni zorunludur.");
        }

        return value.Trim();
    }

    private sealed record RecordedRequest(
        int Index,
        TimeSpan StartOffset,
        TimeSpan Duration,
        bool Success,
        int UnitCount,
        int? InputTokens,
        int? OutputTokens,
        string? ErrorKind);
}
