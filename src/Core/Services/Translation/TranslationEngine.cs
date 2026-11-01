namespace GameLanguageExplorer.Core.Services.Translation;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>
/// Önizleme kayıtlarını güvenli partiler halinde çevirir. Sağlayıcı dışarıdan verilir;
/// yer tutucular maskelenir, yanıt kimlik üzerinden eşleştirilir, kalite politikası uygulanır
/// ve her parti sonunda JSON çalışma günlüğü atomik olarak güncellenir.
/// </summary>
public sealed class TranslationEngine : ITranslationEngine
{
    private readonly TranslationRunJournalStore _journalStore;
    private readonly ITranslationMemory? _translationMemory;

    public TranslationEngine(
        TranslationRunJournalStore? journalStore = null,
        ITranslationMemory? translationMemory = null)
    {
        _journalStore = journalStore ?? new TranslationRunJournalStore();
        _translationMemory = translationMemory;
    }

    public async Task<TranslationRunResult> RunAsync(
        TranslationEngineRequest request,
        ITranslationProvider provider,
        IProgress<TranslationRunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);

        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();

        try
        {
            ValidateRequest(request);
            var sourceFileSha = await VerifySourceFileAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return await RunCoreAsync(
                    request,
                    provider,
                    sourceFileSha,
                    progress,
                    warnings,
                    stopwatch,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateFailureResult(
                request,
                TranslationRunStatus.Cancelled,
                TranslationRunFailureKind.None,
                "Çalışma kullanıcı tarafından iptal edildi.",
                warnings,
                stopwatch.Elapsed);
        }
        catch (TranslationRunJournalCorruptException exception)
        {
            return CreateFailureResult(
                request,
                TranslationRunStatus.Failed,
                TranslationRunFailureKind.JournalCorrupt,
                exception.Message,
                warnings,
                stopwatch.Elapsed);
        }
        catch (SourceFileChangedException exception)
        {
            return CreateFailureResult(
                request,
                TranslationRunStatus.Failed,
                TranslationRunFailureKind.SourceChanged,
                exception.Message,
                warnings,
                stopwatch.Elapsed);
        }
        catch (ArgumentException exception)
        {
            return CreateFailureResult(
                request,
                TranslationRunStatus.Failed,
                TranslationRunFailureKind.InvalidRequest,
                exception.Message,
                warnings,
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CreateFailureResult(
                request,
                TranslationRunStatus.Failed,
                TranslationRunFailureKind.Unexpected,
                "Çalışma günlüğü yazılamadı veya dosya erişimi başarısız: " + exception.Message,
                warnings,
                stopwatch.Elapsed);
        }
        catch (Exception exception)
        {
            return CreateFailureResult(
                request,
                TranslationRunStatus.Failed,
                TranslationRunFailureKind.Unexpected,
                "Çeviri çalışması beklenmeyen bir hatayla durdu: " + exception.Message,
                warnings,
                stopwatch.Elapsed);
        }
    }

    private async Task<TranslationRunResult> RunCoreAsync(
        TranslationEngineRequest request,
        ITranslationProvider provider,
        string sourceFileSha,
        IProgress<TranslationRunProgress>? progress,
        List<string> warnings,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (request.SourceTruncated)
        {
            return CreateFailureResult(
                request,
                TranslationRunStatus.Failed,
                TranslationRunFailureKind.SourceTruncated,
                "Önizleme kayıt sınırı nedeniyle kesildi; eksik dosya çevrilmez. " +
                "Önce dosya okuma sınırını artırın veya daha küçük bir dosya seçin.",
                warnings,
                stopwatch.Elapsed);
        }

        var store = string.IsNullOrWhiteSpace(request.JournalDirectory)
            ? _journalStore
            : new TranslationRunJournalStore(request.JournalDirectory);

        EnsureJournalIsOutsideSourceTree(store.DirectoryPath, request.FilePath);

        var normalizedRequest = request with { SourceFileSha256 = sourceFileSha };
        var runId = TranslationRunIdentity.ComputeRunId(normalizedRequest);
        var plans = BuildPlans(normalizedRequest, warnings);
        var groups = BuildGroups(plans);
        var journal = await store.TryLoadAsync(runId, cancellationToken).ConfigureAwait(false);
        var resumed = false;

        if (journal is not null)
        {
            ValidateJournalMetadata(journal, normalizedRequest, sourceFileSha);
            resumed = ApplyJournalResults(journal, plans, warnings);
        }
        else
        {
            journal = new TranslationRunJournal
            {
                RunId = runId,
                InstalledGameId = request.InstalledGameId,
                DiscoveredFileId = request.DiscoveredFileId,
                SourceFilePath = Path.GetFullPath(request.FilePath),
                SourceFileSha256 = sourceFileSha,
                FileFormat = request.FileFormat,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ProviderId = request.Provider.ProviderId,
                Model = request.Provider.Model,
                PromptVersion = TranslationPromptBuilder.PromptVersion,
                GlossaryVersion = GameGlossaryCatalog.Version,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Status = "Running"
            };
        }

        PropagateGroupResults(groups, warnings);
        await ApplyTranslationMemoryAsync(plans, normalizedRequest, warnings, cancellationToken)
            .ConfigureAwait(false);

        var workItems = BuildWorkItems(groups, request, warnings);
        var batchOptions = request.Options;
        if (provider.Descriptor.MaximumBatchSize is int providerLimit
            && providerLimit > 0
            && providerLimit < batchOptions.MaxUnitsPerBatch)
        {
            batchOptions = batchOptions with { MaxUnitsPerBatch = providerLimit };
        }

        var batches = BuildBatches(workItems, batchOptions);
        var translateCount = plans.Count(plan => plan.Result?.Status != TranslationUnitStatus.Skipped);
        var completedCount = plans.Count(plan =>
            plan.Result is not null && plan.Result.Status != TranslationUnitStatus.Skipped);
        var requestMetrics = new List<TranslationRequestMetric>();

        try
        {
            await SaveJournalAsync(store, journal, plans, "Running", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CreateFailureResult(
                request,
                TranslationRunStatus.Failed,
                TranslationRunFailureKind.Unexpected,
                "Çalışma günlüğü yazılamadı; güvenli devam sağlanamadığı için çeviri başlatılmadı.",
                warnings,
                stopwatch.Elapsed);
        }

        progress?.Report(new TranslationRunProgress(
            completedCount,
            translateCount,
            0,
            batches.Count,
            resumed ? "Önceki çalışma geri yüklendi; kalan partiler hazırlanıyor…" : "Partiler hazırlanıyor…",
            stopwatch.Elapsed));

        var cancelled = false;
        string? runError = null;
        var failureKind = TranslationRunFailureKind.None;
        var batchNumber = 0;

        try
        {
            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                batchNumber++;

                progress?.Report(new TranslationRunProgress(
                    completedCount,
                    translateCount,
                    batchNumber,
                    batches.Count,
                    $"Parti {batchNumber}/{batches.Count} çevriliyor…",
                    stopwatch.Elapsed));

                await ProcessBatchAsync(
                        batch,
                        batchNumber,
                        0,
                        request,
                        provider,
                        requestMetrics,
                        warnings,
                        cancellationToken)
                    .ConfigureAwait(false);

                completedCount = plans.Count(plan =>
                    plan.Result is not null && plan.Result.Status != TranslationUnitStatus.Skipped);

                try
                {
                    await SaveJournalAsync(store, journal, plans, "Running", CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new JournalWriteException(
                        "Parti tamamlandı ancak çalışma günlüğü yazılamadı; güvenli devam için durduruldu.",
                        exception);
                }

                progress?.Report(new TranslationRunProgress(
                    completedCount,
                    translateCount,
                    batchNumber,
                    batches.Count,
                    $"Parti {batchNumber}/{batches.Count} tamamlandı.",
                    stopwatch.Elapsed));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            runError = "Çalışma kullanıcı tarafından iptal edildi; tamamlanan partiler korundu.";
        }
        catch (JournalWriteException exception)
        {
            failureKind = TranslationRunFailureKind.Unexpected;
            runError = exception.Message;
        }
        catch (Exception exception)
        {
            failureKind = TranslationRunFailureKind.Unexpected;
            runError = "Çeviri çalışması beklenmeyen bir hatayla durdu: " + exception.Message;
        }

        if (!cancelled && failureKind == TranslationRunFailureKind.None)
        {
            try
            {
                var finalHash = await FileFingerprint
                    .ComputeSha256Async(request.FilePath, CancellationToken.None)
                    .ConfigureAwait(false);
                if (!string.Equals(finalHash, sourceFileSha, StringComparison.Ordinal))
                {
                    failureKind = TranslationRunFailureKind.SourceChanged;
                    runError = "Kaynak dosya çalışma sırasında değişti; sonuçlar güvenilir kabul edilmedi.";
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failureKind = TranslationRunFailureKind.SourceChanged;
                runError = "Kaynak dosya çalışma sonunda doğrulanamadı: " + exception.Message;
            }
        }

        var finalUnits = plans
            .Select(plan => plan.Result ?? CreatePendingResult(plan))
            .ToArray();

        if (!cancelled
            && failureKind == TranslationRunFailureKind.None
            && batches.Count > 0
            && requestMetrics.Count > 0
            && requestMetrics.All(metric => !metric.Success))
        {
            failureKind = TranslationRunFailureKind.Provider;
            runError =
                "Hiçbir çeviri isteği başarılı olmadı. Yerel sunucu, model veya ağ bağlantısını " +
                "kontrol edin; kayıt ayrıntılarında sağlayıcı hatası bulunabilir.";
        }

        var hasReview = finalUnits.Any(unit => unit.Status is
            TranslationUnitStatus.NeedsReview or TranslationUnitStatus.Failed);
        var status = cancelled
            ? TranslationRunStatus.Cancelled
            : failureKind != TranslationRunFailureKind.None
                ? TranslationRunStatus.Failed
                : hasReview
                    ? TranslationRunStatus.CompletedWithReview
                    : TranslationRunStatus.Completed;

        journal.Status = status.ToString();
        try
        {
            await SaveJournalAsync(store, journal, plans, status.ToString(), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add("Son çalışma durumu günlüğe yazılamadı: " + exception.Message);
        }

        if (!cancelled && failureKind == TranslationRunFailureKind.None)
        {
            await SaveTranslationMemoryAsync(finalUnits, normalizedRequest, warnings)
                .ConfigureAwait(false);
        }

        var metrics = BuildMetrics(
            plans.Count,
            finalUnits,
            requestMetrics,
            normalizedRequest,
            stopwatch.Elapsed);
        var journalPath = store.GetJournalPath(runId);

        return new TranslationRunResult
        {
            RunId = runId,
            Status = status,
            FailureKind = failureKind,
            ErrorMessage = runError,
            JournalPath = journalPath,
            Resumed = resumed,
            Units = finalUnits,
            Warnings = warnings,
            Metrics = metrics
        };
    }

    private static void ValidateRequest(TranslationEngineRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceFileSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetLanguage);

        if (string.Equals(
                request.SourceLanguage.Trim(),
                request.TargetLanguage.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Kaynak ve hedef dil aynı olamaz.");
        }

        if (request.Entries is null)
        {
            throw new ArgumentException("Önizleme kayıtları boş olamaz.");
        }

        if (!FileFingerprint.TryNormalizeSha256(request.SourceFileSha256, out _))
        {
            throw new ArgumentException("Kaynak dosya SHA-256 değeri 64 onaltılık karakter olmalıdır.");
        }

        request.Provider.Validate();
        request.Options.Validate();
    }

    private static async Task<string> VerifySourceFileAsync(
        TranslationEngineRequest request,
        CancellationToken cancellationToken)
    {
        var expected = request.SourceFileSha256.Trim().ToUpperInvariant();
        var actual = await FileFingerprint.ComputeSha256Async(request.FilePath, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new SourceFileChangedException(
                "Kaynak dosya keşif sırasında kaydedilen SHA-256 ile eşleşmiyor. " +
                "Dosya değişmiş olabilir; eski günlük otomatik sürdürülmedi.");
        }

        return actual;
    }

    private static TranslationRunResult CreateFailureResult(
        TranslationEngineRequest request,
        TranslationRunStatus status,
        TranslationRunFailureKind failureKind,
        string message,
        IReadOnlyList<string> warnings,
        TimeSpan duration)
    {
        var entries = request.Entries ?? Array.Empty<LocalizationTextEntry>();
        var units = entries
            .Where(entry => entry is not null)
            .Select((entry, index) => CreateFailureUnit(entry, index, message))
            .ToArray();

        var metrics = BuildMetrics(
            entries.Count,
            units,
            Array.Empty<TranslationRequestMetric>(),
            request,
            duration);

        return new TranslationRunResult
        {
            RunId = TryComputeRunId(request),
            Status = status,
            FailureKind = failureKind,
            ErrorMessage = message,
            Units = units,
            Warnings = warnings,
            Metrics = metrics
        };
    }

    private static TranslationUnitResult CreateFailureUnit(
        LocalizationTextEntry entry,
        int index,
        string reason)
    {
        var unitId = BuildUnitId("unavailable", entry, index);
        return new TranslationUnitResult(
            unitId,
            entry.Key,
            entry.SourceText,
            HashText(entry.SourceText),
            TranslationUnitStatus.Failed)
        {
            DecisionReason = reason
        };
    }

    private static TranslationUnitResult CreatePendingResult(EntryPlan plan) =>
        new(
            plan.UnitId,
            plan.Entry.Key,
            plan.Entry.SourceText,
            plan.SourceSha256,
            TranslationUnitStatus.Failed)
        {
            DecisionReason = "Çalışma tamamlanmadı; bu kayıt için çeviri üretilmedi."
        };

    private static void EnsureJournalIsOutsideSourceTree(string journalDirectory, string sourceFilePath)
    {
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceFilePath));
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return;
        }

        var journal = Path.GetFullPath(journalDirectory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(sourceDirectory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        if (string.Equals(journal, root, StringComparison.OrdinalIgnoreCase)
            || journal.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || journal.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Çalışma günlüğü oyun dosyasının bulunduğu klasöre veya alt klasörüne yazılamaz.");
        }
    }

    private static TranslationRunMetrics BuildMetrics(
        int totalRecords,
        IReadOnlyList<TranslationUnitResult> units,
        IReadOnlyList<TranslationRequestMetric> requests,
        TranslationEngineRequest request,
        TimeSpan duration)
    {
        var inputTokens = requests
            .Where(metric => metric.InputTokens.HasValue)
            .Select(metric => metric.InputTokens!.Value)
            .ToArray();
        var outputTokens = requests
            .Where(metric => metric.OutputTokens.HasValue)
            .Select(metric => metric.OutputTokens!.Value)
            .ToArray();

        return new TranslationRunMetrics
        {
            TotalRecords = totalRecords,
            TranslatableRecords = units.Count(unit => unit.Status != TranslationUnitStatus.Skipped),
            CandidateRecords = units.Count(unit => unit.Status == TranslationUnitStatus.Candidate),
            ReusedRecords = units.Count(unit => unit.Status == TranslationUnitStatus.Reused),
            NeedsReviewRecords = units.Count(unit => unit.Status == TranslationUnitStatus.NeedsReview),
            FailedRecords = units.Count(unit => unit.Status == TranslationUnitStatus.Failed),
            SkippedRecords = units.Count(unit => unit.Status == TranslationUnitStatus.Skipped),
            RequestCount = requests.Count,
            RetryCount = requests.Count(metric => metric.IsRetry),
            BatchCount = requests.Select(metric => metric.BatchIndex).Distinct().Count(),
            TotalDuration = duration,
            TotalRequestDuration = TimeSpan.FromTicks(
                requests.Sum(metric => metric.Duration.Ticks)),
            InputTokens = inputTokens.Length == 0 ? null : inputTokens.Sum(),
            OutputTokens = outputTokens.Length == 0 ? null : outputTokens.Sum(),
            TranslatedSourceCharacters = units
                .Where(unit => unit.Status is
                    TranslationUnitStatus.Candidate
                    or TranslationUnitStatus.Reused
                    or TranslationUnitStatus.NeedsReview)
                .Sum(unit => unit.SourceText.Length),
            ProviderId = request.Provider.ProviderId,
            Model = request.Provider.Model,
            PromptVersion = TranslationPromptBuilder.PromptVersion
        };
    }

    private static string HashText(string? text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)));

    private static string BuildUnitId(string sourceFileSha, LocalizationTextEntry entry, int index)
    {
        var material = string.Join(
            "\n",
            "GLE-UNIT-1",
            sourceFileSha,
            entry.Key,
            HashText(entry.SourceText),
            entry.Context ?? string.Empty,
            entry.PluralIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            entry.PluralCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "u_" + Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    private static string BuildGroupKey(LocalizationTextEntry entry)
    {
        var material = string.Join(
            "\u001F",
            entry.SourceText,
            entry.Context ?? string.Empty,
            entry.FormatKey,
            entry.PluralIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            entry.PluralCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            string.Join("\u001E", entry.Flags.OrderBy(flag => flag, StringComparer.Ordinal)));

        return HashText(material);
    }

    /// <summary>
    /// AI çağrısından önce tam eşleşmeli çeviri belleğini uygular. Sonuçlar yalnızca
    /// kalite kontrolünden geçen kayıtlar için <see cref="TranslationUnitStatus.Reused"/> olur.
    /// </summary>
    private async Task ApplyTranslationMemoryAsync(
        IReadOnlyList<EntryPlan> plans,
        TranslationEngineRequest request,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (_translationMemory is null)
        {
            return;
        }

        var matches = new Dictionary<string, string?>(StringComparer.Ordinal);

        try
        {
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!plan.IsEligible
                    || plan.Result is not null
                    || string.IsNullOrWhiteSpace(plan.Entry.SourceText))
                {
                    continue;
                }

                var source = plan.Entry.SourceText;

                if (!matches.TryGetValue(source, out var translation))
                {
                    translation = await _translationMemory.FindExactAsync(
                            source,
                            request.SourceLanguage,
                            request.TargetLanguage,
                            cancellationToken)
                        .ConfigureAwait(false);
                    matches[source] = translation;
                }

                if (string.IsNullOrWhiteSpace(translation))
                {
                    continue;
                }

                var acceptance = TranslationAcceptancePolicy.Evaluate(source, translation);

                if (acceptance.Status != TranslationAcceptanceStatus.Candidate)
                {
                    continue;
                }

                plan.Result = CreatePlanResult(
                    plan,
                    TranslationUnitStatus.Reused,
                    "Çeviri belleğindeki tam eşleşme kullanıldı; sağlayıcı çağrılmadı.",
                    attempts: 0,
                    acceptance.TranslationText,
                    acceptance.Findings);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            warnings.Add("Çeviri belleği okunamadı; sağlayıcı ile devam edildi: " + exception.Message);
        }
    }

    /// <summary>Otomatik kabul edilen yeni çevirileri, yinelenen kayıt oluşturmadan belleğe yazar.</summary>
    private async Task SaveTranslationMemoryAsync(
        IReadOnlyList<TranslationUnitResult> units,
        TranslationEngineRequest request,
        List<string> warnings)
    {
        if (_translationMemory is null)
        {
            return;
        }

        var savedSources = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            foreach (var unit in units)
            {
                if (unit.Status != TranslationUnitStatus.Candidate
                    || string.IsNullOrWhiteSpace(unit.Translation)
                    || !savedSources.Add(unit.SourceText))
                {
                    continue;
                }

                await _translationMemory.SaveAsync(
                        unit.SourceText,
                        unit.Translation,
                        request.SourceLanguage,
                        request.TargetLanguage,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            warnings.Add("Yeni çeviri belleğe yazılamadı: " + exception.Message);
        }
    }

    private static List<EntryPlan> BuildPlans(
        TranslationEngineRequest request,
        List<string> warnings)
    {
        var plans = new List<EntryPlan>(request.Entries.Count);
        var keyCounts = request.Entries
            .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Key))
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var rejectedExisting = 0;

        for (var index = 0; index < request.Entries.Count; index++)
        {
            var entry = request.Entries[index];
            if (entry is null)
            {
                continue;
            }

            var baseId = BuildUnitId(request.SourceFileSha256, entry, index);
            var unitId = baseId;
            var suffix = 2;
            while (!usedIds.Add(unitId))
            {
                unitId = baseId + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                suffix++;
            }

            var plan = new EntryPlan(entry, unitId, HashText(entry.SourceText), BuildGroupKey(entry));

            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                plan.Result = CreatePlanResult(
                    plan,
                    TranslationUnitStatus.Failed,
                    "Kaynak anahtarı boş; güvenli eşleştirme yapılamadı.",
                    attempts: 0);
            }
            else if (keyCounts.TryGetValue(entry.Key, out var count) && count > 1)
            {
                plan.Result = CreatePlanResult(
                    plan,
                    TranslationUnitStatus.Failed,
                    "Yinelenen kaynak anahtarı; güvenli eşleştirme yapılamadı.",
                    attempts: 0);
            }
            else if (entry.Status == LocalizationEntryStatus.Skipped)
            {
                plan.Result = CreatePlanResult(
                    plan,
                    TranslationUnitStatus.Skipped,
                    entry.SkipReason ?? "Kayıt çeviri adayı değil.",
                    attempts: 0);
            }
            else if (entry.Status == LocalizationEntryStatus.Ambiguous)
            {
                plan.Result = CreatePlanResult(
                    plan,
                    TranslationUnitStatus.Skipped,
                    "Belirsiz kayıt; varsayılan olarak çevrilmedi. " + (entry.SkipReason ?? string.Empty),
                    attempts: 0);
            }
            else if (string.IsNullOrWhiteSpace(entry.SourceText))
            {
                plan.Result = CreatePlanResult(
                    plan,
                    TranslationUnitStatus.Skipped,
                    "Boş kaynak metin; çeviri üretilmedi.",
                    attempts: 0);
            }
            else
            {
                plan.IsEligible = true;

                if (!entry.IsFuzzy && !string.IsNullOrWhiteSpace(entry.ExistingTranslation))
                {
                    var acceptance = TranslationAcceptancePolicy.Evaluate(
                        entry.SourceText,
                        entry.ExistingTranslation);

                    if (acceptance.Status == TranslationAcceptanceStatus.Candidate)
                    {
                        plan.Result = CreatePlanResult(
                            plan,
                            TranslationUnitStatus.Reused,
                            "Dosyadaki mevcut çeviri kalite kontrolünden geçti; yeniden çevrilmedi.",
                            attempts: 0,
                            acceptance.TranslationText,
                            acceptance.Findings);
                    }
                    else
                    {
                        rejectedExisting++;
                    }
                }

                if (plan.Result is null
                    && GameGlossaryCatalog.TryGetExactTranslation(
                        entry.SourceText,
                        request.TargetLanguage,
                        out var exactTranslation))
                {
                    var exactAcceptance = TranslationAcceptancePolicy.Evaluate(
                        entry.SourceText,
                        exactTranslation);

                    if (exactAcceptance.Status == TranslationAcceptanceStatus.Candidate)
                    {
                        plan.Result = CreatePlanResult(
                            plan,
                            TranslationUnitStatus.Candidate,
                            "Sözlükteki tam terim eşleşmesi kullanıldı; insan incelemesi önerilir.",
                            attempts: 0,
                            exactAcceptance.TranslationText,
                            exactAcceptance.Findings);
                    }
                }
            }

            plans.Add(plan);
        }

        if (rejectedExisting > 0)
        {
            warnings.Add(
                $"{rejectedExisting} mevcut çeviri kalite kontrolünden geçmediği için yeniden çevrilecek.");
        }

        return plans;
    }

    private static TranslationUnitResult CreatePlanResult(
        EntryPlan plan,
        TranslationUnitStatus status,
        string reason,
        int attempts,
        string? translation = null,
        IReadOnlyList<TranslationQualityIssue>? issues = null) =>
        new(plan.UnitId, plan.Entry.Key, plan.Entry.SourceText, plan.SourceSha256, status)
        {
            Translation = translation,
            DecisionReason = reason,
            Attempts = attempts,
            QualityIssues = issues ?? Array.Empty<TranslationQualityIssue>()
        };

    private static List<GroupPlan> BuildGroups(IReadOnlyList<EntryPlan> plans)
    {
        var groups = new List<GroupPlan>();
        var byKey = new Dictionary<string, GroupPlan>(StringComparer.Ordinal);

        foreach (var plan in plans.Where(plan => plan.IsEligible))
        {
            if (!byKey.TryGetValue(plan.GroupKey, out var group))
            {
                group = new GroupPlan(plan.GroupKey);
                byKey.Add(plan.GroupKey, group);
                groups.Add(group);
            }

            group.Members.Add(plan);
        }

        return groups;
    }

    private static void ValidateJournalMetadata(
        TranslationRunJournal journal,
        TranslationEngineRequest request,
        string sourceFileSha)
    {
        var mismatch =
            !string.Equals(journal.SourceFileSha256, sourceFileSha, StringComparison.Ordinal)
            || journal.InstalledGameId != request.InstalledGameId
            || journal.DiscoveredFileId != request.DiscoveredFileId
            || (request.FileFormat is not null
                && !string.Equals(journal.FileFormat, request.FileFormat, StringComparison.Ordinal))
            || !string.Equals(journal.SourceLanguage, request.SourceLanguage, StringComparison.Ordinal)
            || !string.Equals(journal.TargetLanguage, request.TargetLanguage, StringComparison.Ordinal)
            || !string.Equals(journal.ProviderId, request.Provider.ProviderId, StringComparison.Ordinal)
            || !string.Equals(journal.Model, request.Provider.Model, StringComparison.Ordinal)
            || !string.Equals(
                journal.PromptVersion,
                TranslationPromptBuilder.PromptVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                journal.GlossaryVersion,
                GameGlossaryCatalog.Version,
                StringComparison.Ordinal);

        if (mismatch)
        {
            throw new TranslationRunJournalCorruptException(
                "Çalışma günlüğü kaynak dosya, dil, sağlayıcı, model veya istem sürümüyle eşleşmiyor; " +
                "eski sonuçlar güvenli biçimde yeniden kullanılamaz.");
        }
    }

    private static bool ApplyJournalResults(
        TranslationRunJournal journal,
        IReadOnlyList<EntryPlan> plans,
        List<string> warnings)
    {
        var byId = journal.Units.ToDictionary(unit => unit.UnitId, StringComparer.Ordinal);
        var resumed = 0;

        foreach (var plan in plans)
        {
            if (plan.Result is not null
                || !byId.TryGetValue(plan.UnitId, out var saved)
                || !string.Equals(saved.SourceSha256, plan.SourceSha256, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(saved.Translation)
                || !Enum.TryParse<TranslationUnitStatus>(saved.Status, out var savedStatus)
                || savedStatus is not (
                    TranslationUnitStatus.Candidate
                    or TranslationUnitStatus.NeedsReview
                    or TranslationUnitStatus.Reused))
            {
                continue;
            }

            if (savedStatus == TranslationUnitStatus.Reused
                && !string.Equals(
                    saved.Translation,
                    plan.Entry.ExistingTranslation,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var acceptance = TranslationAcceptancePolicy.Evaluate(plan.Entry.SourceText, saved.Translation);
            if (acceptance.Status == TranslationAcceptanceStatus.Failed)
            {
                continue;
            }

            plan.Result = CreatePlanResult(
                plan,
                acceptance.Status == TranslationAcceptanceStatus.Candidate
                    ? TranslationUnitStatus.Candidate
                    : TranslationUnitStatus.NeedsReview,
                "Önceki çalışma günlüğünden geri yüklendi; yeniden çevrilmedi.",
                saved.Attempts,
                acceptance.TranslationText,
                acceptance.Findings);
            resumed++;
        }

        if (resumed > 0)
        {
            warnings.Add($"{resumed} kayıt önceki çalışma günlüğünden geri yüklendi.");
        }

        return resumed > 0;
    }

    private static void PropagateGroupResults(List<GroupPlan> groups, List<string> warnings)
    {
        foreach (var group in groups)
        {
            var completed = group.Members
                .Where(plan => plan.Result?.Translation is not null
                               && plan.Result.Status is
                                   TranslationUnitStatus.Candidate
                                   or TranslationUnitStatus.NeedsReview
                                   or TranslationUnitStatus.Reused)
                .Select(plan => plan.Result!)
                .ToArray();

            if (completed.Length == 0)
            {
                continue;
            }

            var distinct = completed
                .Select(result => result.Translation!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (distinct.Length > 1)
            {
                warnings.Add(
                    "Aynı kaynak ve bağlam grubunda farklı çeviriler var; " +
                    "çelişkili sonuçlar otomatik olarak birleştirilmedi.");
                continue;
            }

            var template = completed[0];
            foreach (var pending in group.Members.Where(plan => plan.Result is null))
            {
                var acceptance = TranslationAcceptancePolicy.Evaluate(
                    pending.Entry.SourceText,
                    template.Translation!);

                if (acceptance.Status == TranslationAcceptanceStatus.Failed)
                {
                    continue;
                }

                pending.Result = CreatePlanResult(
                    pending,
                    acceptance.Status == TranslationAcceptanceStatus.Candidate
                        ? TranslationUnitStatus.Candidate
                        : TranslationUnitStatus.NeedsReview,
                    "Aynı kaynak ve bağlam grubundaki doğrulanmış sonuç kullanıldı.",
                    template.Attempts,
                    acceptance.TranslationText,
                    acceptance.Findings);
            }
        }
    }

    private static List<WorkItem> BuildWorkItems(
        List<GroupPlan> groups,
        TranslationEngineRequest request,
        List<string> warnings)
    {
        var items = new List<WorkItem>();

        foreach (var group in groups)
        {
            var pending = group.Members.Where(plan => plan.Result is null).ToArray();
            if (pending.Length == 0)
            {
                continue;
            }

            var representative = pending[0];
            try
            {
                var protection = PlaceholderProtector.Protect(
                    representative.Entry.SourceText,
                    representative.UnitId);
                var promptUnit = new TranslationUnit
                {
                    UnitId = representative.UnitId,
                    EntryKey = representative.Entry.Key,
                    SourceText = protection.MaskedText,
                    Context = representative.Entry.Context,
                    Reference = representative.Entry.Reference,
                    FormatKey = representative.Entry.FormatKey,
                    PluralIndex = representative.Entry.PluralIndex,
                    PluralCount = representative.Entry.PluralCount,
                    Flags = representative.Entry.Flags
                };

                items.Add(new WorkItem(representative, pending, protection, promptUnit));
            }
            catch (Exception exception)
            {
                var reason = "Yer tutucu koruması hazırlanamadı: " + exception.Message;
                warnings.Add(reason);
                MarkFailed(pending, reason, attempts: 0);
            }
        }

        return items;
    }

    private static List<List<WorkItem>> BuildBatches(
        IReadOnlyList<WorkItem> workItems,
        TranslationEngineOptions options)
    {
        var batches = new List<List<WorkItem>>();
        var current = new List<WorkItem>();
        var characters = 0;

        foreach (var item in workItems)
        {
            var wouldExceedCharacters = current.Count > 0
                && characters + item.SourceCharacters > options.MaxSourceCharactersPerBatch;
            var wouldExceedCount = current.Count >= options.MaxUnitsPerBatch;

            if (wouldExceedCharacters || wouldExceedCount)
            {
                batches.Add(current);
                current = new List<WorkItem>();
                characters = 0;
            }

            current.Add(item);
            characters += item.SourceCharacters;
        }

        if (current.Count > 0)
        {
            batches.Add(current);
        }

        return batches;
    }

    private static IReadOnlyList<GlossaryEntry> MergeGlossary(
        TranslationEngineRequest request,
        IEnumerable<WorkItem> items)
    {
        var merged = new List<GlossaryEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in request.Glossary)
        {
            if (entry is null
                || string.IsNullOrWhiteSpace(entry.SourceTerm)
                || string.IsNullOrWhiteSpace(entry.TargetTerm)
                || !seen.Add(entry.SourceTerm))
            {
                continue;
            }

            merged.Add(entry);
        }

        var sources = items.Select(item => item.PromptUnit.SourceText);
        foreach (var entry in GameGlossaryCatalog.SelectFor(sources))
        {
            if (seen.Add(entry.SourceTerm))
            {
                merged.Add(entry);
            }
        }

        return merged;
    }

    private static async Task SaveJournalAsync(
        TranslationRunJournalStore store,
        TranslationRunJournal journal,
        IReadOnlyList<EntryPlan> plans,
        string status,
        CancellationToken cancellationToken)
    {
        journal.Status = status;
        journal.UpdatedAtUtc = DateTime.UtcNow;
        journal.Units = plans
            .Where(plan => plan.Result is not null)
            .Select(plan => new TranslationJournalUnit
            {
                UnitId = plan.UnitId,
                EntryKey = plan.Entry.Key,
                SourceSha256 = plan.SourceSha256,
                Status = plan.Result!.Status.ToString(),
                Translation = plan.Result.Translation,
                DecisionReason = plan.Result.DecisionReason,
                Attempts = plan.Result.Attempts,
                QualityCodes = plan.Result.QualityIssues
                    .Select(issue => issue.Code.ToString())
                    .ToList()
            })
            .ToList();

        await store.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ProcessBatchAsync(
        IReadOnlyList<WorkItem> workItems,
        int batchIndex,
        int depth,
        TranslationEngineRequest request,
        ITranslationProvider provider,
        List<TranslationRequestMetric> requestMetrics,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (workItems.Count == 0)
        {
            return;
        }

        var promptUnits = workItems.Select(item => item.PromptUnit).ToArray();
        TranslationPrompt prompt;
        IReadOnlyList<GlossaryEntry> glossary;
        try
        {
            glossary = MergeGlossary(request, workItems);
            prompt = TranslationPromptBuilder.Build(new TranslationPromptRequest
            {
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                Units = promptUnits,
                Glossary = glossary
            });
        }
        catch (Exception exception)
        {
            MarkFailed(
                workItems.SelectMany(item => item.Members),
                "İstem hazırlanamadı: " + exception.Message,
                attempts: 0);
            return;
        }

        var expectedIds = workItems
            .Select(item => item.Representative.UnitId)
            .ToHashSet(StringComparer.Ordinal);
        var maxAttempts = Math.Max(1, request.Options.MaxAttemptsPerBatch);
        string? lastError = null;
        string? lastErrorKind = null;
        TimeSpan? retryAfter = null;
        var attemptsUsed = 0;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attemptsUsed = attempt;
            var stopwatch = Stopwatch.StartNew();
            var canRetry = false;

            try
            {
                var response = await provider.TranslateAsync(
                        new TranslationProviderRequest
                        {
                            RequestId = $"batch-{batchIndex}-{depth}-{attempt}",
                            SourceLanguage = request.SourceLanguage,
                            TargetLanguage = request.TargetLanguage,
                            Units = promptUnits,
                            Glossary = glossary,
                            Prompt = prompt,
                            GenerationOptions = request.Provider.GenerationOptions
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                stopwatch.Stop();
                if (!TryMapCandidates(response, expectedIds, out var candidates, out var mappingError))
                {
                    lastError = mappingError;
                    lastErrorKind = nameof(TranslationRunFailureKind.InvalidResponse);
                    canRetry = true;
                    requestMetrics.Add(new TranslationRequestMetric(
                        batchIndex,
                        attempt,
                        promptUnits.Length,
                        promptUnits.Sum(unit => unit.SourceText.Length),
                        stopwatch.Elapsed,
                        false,
                        response?.InputTokens,
                        response?.OutputTokens,
                        lastErrorKind));
                }
                else
                {
                    requestMetrics.Add(new TranslationRequestMetric(
                        batchIndex,
                        attempt,
                        promptUnits.Length,
                        promptUnits.Sum(unit => unit.SourceText.Length),
                        stopwatch.Elapsed,
                        true,
                        response?.InputTokens,
                        response?.OutputTokens,
                        null));
                    ApplyCandidates(workItems, candidates, attempt);
                    return;
                }
            }
            catch (TranslationProviderException exception)
            {
                stopwatch.Stop();
                lastError = exception.Error.Message;
                lastErrorKind = exception.Error.Kind.ToString();
                retryAfter = exception.Error.RetryAfter;
                canRetry = IsRetryable(exception.Error, invalidResponse: false);
                requestMetrics.Add(new TranslationRequestMetric(
                    batchIndex,
                    attempt,
                    promptUnits.Length,
                    promptUnits.Sum(unit => unit.SourceText.Length),
                    stopwatch.Elapsed,
                    false,
                    null,
                    null,
                    lastErrorKind));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                lastError = "Çeviri isteği zaman aşımına uğradı.";
                lastErrorKind = nameof(TranslationProviderFailureKind.Timeout);
                retryAfter = null;
                canRetry = true;
                requestMetrics.Add(new TranslationRequestMetric(
                    batchIndex,
                    attempt,
                    promptUnits.Length,
                    promptUnits.Sum(unit => unit.SourceText.Length),
                    stopwatch.Elapsed,
                    false,
                    null,
                    null,
                    lastErrorKind));
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                lastError = "Sağlayıcı isteği başarısız: " + exception.Message;
                lastErrorKind = nameof(TranslationProviderFailureKind.Unknown);
                retryAfter = null;
                canRetry = false;
                requestMetrics.Add(new TranslationRequestMetric(
                    batchIndex,
                    attempt,
                    promptUnits.Length,
                    promptUnits.Sum(unit => unit.SourceText.Length),
                    stopwatch.Elapsed,
                    false,
                    null,
                    null,
                    lastErrorKind));
            }

            if (attempt < maxAttempts && canRetry)
            {
                await DelayBeforeRetryAsync(attempt, retryAfter, request.Options, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            break;
        }

        if (workItems.Count > 1
            && depth < request.Options.MaxSplitDepth
            && ShouldSplit(lastErrorKind))
        {
            warnings.Add(
                $"Parti {batchIndex} küçültülerek yeniden denendi ({lastErrorKind ?? "bilinmeyen hata"}).");

            var midpoint = workItems.Count / 2;
            var left = workItems.Take(midpoint).ToArray();
            var right = workItems.Skip(midpoint).ToArray();

            await ProcessBatchAsync(
                    left,
                    batchIndex,
                    depth + 1,
                    request,
                    provider,
                    requestMetrics,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
            await ProcessBatchAsync(
                    right,
                    batchIndex,
                    depth + 1,
                    request,
                    provider,
                    requestMetrics,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        MarkFailed(
            workItems.SelectMany(item => item.Members),
            "Çeviri üretilemedi" + (lastError is null ? "." : ": " + lastError),
            attemptsUsed);
    }

    private static bool TryMapCandidates(
        TranslationProviderResponse? response,
        IReadOnlySet<string> expectedIds,
        out IReadOnlyDictionary<string, string?> candidates,
        out string? error)
    {
        candidates = new Dictionary<string, string?>(StringComparer.Ordinal);
        error = null;

        if (response?.Candidates is null)
        {
            error = "Sağlayıcı yanıtında çeviri listesi yok.";
            return false;
        }

        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var candidate in response.Candidates)
        {
            if (candidate is null
                || string.IsNullOrWhiteSpace(candidate.UnitId)
                || !expectedIds.Contains(candidate.UnitId))
            {
                error = "Yanıt bilinmeyen veya boş bir unitId içeriyor.";
                return false;
            }

            if (!map.TryAdd(candidate.UnitId, candidate.Text))
            {
                error = "Yanıtta yinelenen unitId var.";
                return false;
            }
        }

        var missing = expectedIds.Count(id => !map.ContainsKey(id));
        if (missing > 0)
        {
            error = $"Yanıtta {missing} beklenen unitId eksik.";
            return false;
        }

        if (map.Count != expectedIds.Count)
        {
            error = "Yanıt beklenen kayıt kümesiyle tam eşleşmiyor.";
            return false;
        }

        candidates = map;
        return true;
    }

    private static bool IsRetryable(TranslationProviderError error, bool invalidResponse) =>
        invalidResponse
        || error.IsTransient
        || error.Kind is
            TranslationProviderFailureKind.RateLimited
            or TranslationProviderFailureKind.Timeout
            or TranslationProviderFailureKind.Network
            or TranslationProviderFailureKind.InvalidResponse;

    private static bool ShouldSplit(string? errorKind) =>
        !string.Equals(errorKind, nameof(TranslationProviderFailureKind.Authentication), StringComparison.Ordinal)
        && !string.Equals(errorKind, nameof(TranslationProviderFailureKind.Configuration), StringComparison.Ordinal)
        && !string.Equals(errorKind, nameof(TranslationProviderFailureKind.Network), StringComparison.Ordinal)
        && !string.Equals(errorKind, nameof(TranslationProviderFailureKind.RateLimited), StringComparison.Ordinal)
        && !string.Equals(errorKind, nameof(TranslationProviderFailureKind.Timeout), StringComparison.Ordinal);

    private static async Task DelayBeforeRetryAsync(
        int attempt,
        TimeSpan? retryAfter,
        TranslationEngineOptions options,
        CancellationToken cancellationToken)
    {
        var backoffMilliseconds = options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var delay = retryAfter ?? TimeSpan.FromMilliseconds(backoffMilliseconds);

        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        if (delay > options.MaxRetryDelay)
        {
            delay = options.MaxRetryDelay;
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ApplyCandidates(
        IReadOnlyList<WorkItem> workItems,
        IReadOnlyDictionary<string, string?> candidates,
        int attempts)
    {
        foreach (var item in workItems)
        {
            if (!candidates.TryGetValue(item.Representative.UnitId, out var candidateText))
            {
                MarkFailed(item.Members, "Yanıt bu kayıt için çeviri içermiyor.", attempts);
                continue;
            }

            var restoration = PlaceholderProtector.Restore(candidateText, item.Protection);
            if (!restoration.IsValid)
            {
                var protectionReason = string.Join(
                    ", ",
                    restoration.Issues.Select(issue => issue.Type.ToString()));
                MarkFailed(
                    item.Members,
                    "Yer tutucu geri yüklemesi doğrulanamadı: " + protectionReason,
                    attempts);
                continue;
            }

            foreach (var member in item.Members)
            {
                var acceptance = TranslationAcceptancePolicy.Evaluate(
                    member.Entry.SourceText,
                    restoration);
                var status = acceptance.Status switch
                {
                    TranslationAcceptanceStatus.Candidate => TranslationUnitStatus.Candidate,
                    TranslationAcceptanceStatus.NeedsReview => TranslationUnitStatus.NeedsReview,
                    _ => TranslationUnitStatus.Failed
                };

                member.Result = CreatePlanResult(
                    member,
                    status,
                    acceptance.DecisionReason,
                    attempts,
                    status == TranslationUnitStatus.Failed ? null : acceptance.TranslationText,
                    acceptance.Findings);
            }
        }
    }

    private static void MarkFailed(
        IEnumerable<EntryPlan> plans,
        string reason,
        int attempts)
    {
        foreach (var plan in plans)
        {
            if (plan.Result is null)
            {
                plan.Result = CreatePlanResult(
                    plan,
                    TranslationUnitStatus.Failed,
                    reason,
                    attempts);
            }
        }
    }

    private static string TryComputeRunId(TranslationEngineRequest request)
    {
        try
        {
            return TranslationRunIdentity.ComputeRunId(request);
        }
        catch
        {
            return "run_unavailable";
        }
    }

    private sealed class EntryPlan
    {
        public EntryPlan(
            LocalizationTextEntry entry,
            string unitId,
            string sourceSha256,
            string groupKey)
        {
            Entry = entry;
            UnitId = unitId;
            SourceSha256 = sourceSha256;
            GroupKey = groupKey;
        }

        public LocalizationTextEntry Entry { get; }

        public string UnitId { get; }

        public string SourceSha256 { get; }

        public string GroupKey { get; }

        public bool IsEligible { get; set; }

        public TranslationUnitResult? Result { get; set; }
    }

    private sealed class GroupPlan(string key)
    {
        public string Key { get; } = key;

        public List<EntryPlan> Members { get; } = new();
    }

    private sealed class WorkItem(
        EntryPlan representative,
        IReadOnlyList<EntryPlan> members,
        PlaceholderProtectionResult protection,
        TranslationUnit promptUnit)
    {
        public EntryPlan Representative { get; } = representative;

        public IReadOnlyList<EntryPlan> Members { get; } = members;

        public PlaceholderProtectionResult Protection { get; } = protection;

        public TranslationUnit PromptUnit { get; } = promptUnit;

        public int SourceCharacters => PromptUnit.SourceText.Length;
    }

    private sealed class JournalWriteException(string message, Exception innerException)
        : Exception(message, innerException);

    private sealed class SourceFileChangedException(string message) : Exception(message);
}
