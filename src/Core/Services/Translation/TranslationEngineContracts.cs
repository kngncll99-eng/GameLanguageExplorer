namespace GameLanguageExplorer.Core.Services.Translation;

using GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>Çalışma sonucunun kullanıcıya gösterilebilir durumu.</summary>
public enum TranslationRunStatus
{
    /// <summary>Tüm çevrilebilir kayıtlar otomatik kabul edildi.</summary>
    Completed = 0,

    /// <summary>Çalışma bitti ancak inceleme gerektiren veya başarısız kayıtlar var.</summary>
    CompletedWithReview = 1,

    /// <summary>Kullanıcı iptal etti; tamamlanan partiler korunur.</summary>
    Cancelled = 2,

    /// <summary>Çalışma başlatılamadı veya sürdürülemedi.</summary>
    Failed = 3
}

/// <summary>Tek bir kaydın çalışma sonucu.</summary>
public enum TranslationUnitStatus
{
    /// <summary>Kaynak kayıt çeviriye uygun değil (atlandı, belirsiz veya boş).</summary>
    Skipped = 0,

    /// <summary>Dosyada bulunan güvenilir mevcut çeviri yeniden kullanıldı.</summary>
    Reused = 1,

    /// <summary>Otomatik kontrolleri geçti; kullanıcı incelemesine sunulabilir.</summary>
    Candidate = 2,

    /// <summary>Şüpheli veya güvenli doğrulanamayan çeviri; insan incelemesi gerekir.</summary>
    NeedsReview = 3,

    /// <summary>Kullanılabilir çeviri üretilemedi.</summary>
    Failed = 4
}

/// <summary>Çalışmanın neden başlatılamadığını/bitmediğini sınıflandırır.</summary>
public enum TranslationRunFailureKind
{
    None = 0,
    InvalidRequest = 1,
    SourceTruncated = 2,
    SourceChanged = 3,
    JournalCorrupt = 4,
    Provider = 5,
    InvalidResponse = 6,
    Unexpected = 7
}

/// <summary>
/// Motorun parti ve retry sınırları. Varsayılanlar küçük ve güvenlidir; gerçek model
/// ölçümlerine göre yükseltilebilir. Tek bir devasa istek hiçbir zaman üretilmez.
/// </summary>
public sealed record TranslationEngineOptions
{
    public const int DefaultMaxUnitsPerBatch = 20;
    public const int DefaultMaxSourceCharactersPerBatch = 6000;

    /// <summary>Bir istekte yer alacak en fazla benzersiz kayıt (varsayılan 20).</summary>
    public int MaxUnitsPerBatch { get; init; } = DefaultMaxUnitsPerBatch;

    /// <summary>Bir istekte yer alacak maskelenmiş kaynak metnin en fazla karakteri (varsayılan 6000).</summary>
    public int MaxSourceCharactersPerBatch { get; init; } = DefaultMaxSourceCharactersPerBatch;

    /// <summary>Tek bir parti için en fazla deneme sayısı; ilk deneme dahil (varsayılan 2).</summary>
    public int MaxAttemptsPerBatch { get; init; } = 2;

    /// <summary>Başarısız partinin ikiye bölünerek yeniden denenebileceği en fazla derinlik.</summary>
    public int MaxSplitDepth { get; init; } = 2;

    /// <summary>Yeniden denemeler arasındaki taban bekleme.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Sunucunun önerdiği bekleme dahil en fazla retry gecikmesi.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(10);

    public void Validate()
    {
        if (MaxUnitsPerBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxUnitsPerBatch), MaxUnitsPerBatch, "Parti kayıt sınırı pozitif olmalıdır.");
        }

        if (MaxSourceCharactersPerBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxSourceCharactersPerBatch),
                MaxSourceCharactersPerBatch,
                "Parti karakter sınırı pozitif olmalıdır.");
        }

        if (MaxAttemptsPerBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxAttemptsPerBatch), MaxAttemptsPerBatch, "Deneme sayısı pozitif olmalıdır.");
        }

        if (MaxSplitDepth < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxSplitDepth), MaxSplitDepth, "Bölme derinliği negatif olamaz.");
        }

        if (RetryBaseDelay < TimeSpan.Zero || RetryBaseDelay == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RetryBaseDelay), RetryBaseDelay, "Retry gecikmesi negatif veya sonsuz olamaz.");
        }

        if (MaxRetryDelay < RetryBaseDelay || MaxRetryDelay == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxRetryDelay), MaxRetryDelay, "En fazla retry gecikmesi taban gecikmeden küçük olamaz.");
        }
    }
}

/// <summary>
/// Motorun tek bir çalışma için girdisi. API anahtarı içermez; kimlik bilgisi sağlayıcı
/// oluşturulurken ayrı ve güvenli kaynaktan çözülür.
/// </summary>
public sealed record TranslationEngineRequest
{
    public required int InstalledGameId { get; init; }

    public required int DiscoveredFileId { get; init; }

    /// <summary>Salt okunur doğrulama için kullanılan kaynak dosya yolu.</summary>
    public required string FilePath { get; init; }

    /// <summary>Önizlemede tespit edilen dosya biçimi (ör. XML, JSON, PO). Günlüğe yazılır.</summary>
    public string? FileFormat { get; init; }

    /// <summary>Keşif/önizleme sırasında kaydedilen SHA-256. Çalışma başlamadan önce doğrulanır.</summary>
    public required string SourceFileSha256 { get; init; }

    public required IReadOnlyList<LocalizationTextEntry> Entries { get; init; }

    /// <summary>Önizleme kayıt sınırı nedeniyle kesildiyse motor çalışmayı reddeder.</summary>
    public bool SourceTruncated { get; init; }

    public required string SourceLanguage { get; init; }

    public required string TargetLanguage { get; init; }

    public required TranslationProviderProfile Provider { get; init; }

    public IReadOnlyList<GlossaryEntry> Glossary { get; init; } = Array.Empty<GlossaryEntry>();

    /// <summary>Günlük dizini. Boşsa <c>%LOCALAPPDATA%</c> altındaki varsayılan kullanılır.</summary>
    public string? JournalDirectory { get; init; }

    public TranslationEngineOptions Options { get; init; } = new();
}

/// <summary>Tek bir HTTP/parti denemesinin güvenli ölçümü.</summary>
public sealed record TranslationRequestMetric(
    int BatchIndex,
    int Attempt,
    int UnitCount,
    int SourceCharacters,
    TimeSpan Duration,
    bool Success,
    int? InputTokens,
    int? OutputTokens,
    string? ErrorKind)
{
    public bool IsRetry => Attempt > 1;
}

/// <summary>
/// Tek bir çeviri kaydının sonucu. <see cref="QualityIssues"/> ham istemi veya API anahtarını
/// taşımaz; yalnızca doğrulayıcının güvenli bulgularını taşır.
/// </summary>
public sealed record TranslationUnitResult(
    string UnitId,
    string EntryKey,
    string SourceText,
    string SourceSha256,
    TranslationUnitStatus Status)
{
    public string? Translation { get; init; }

    public string? DecisionReason { get; init; }

    public int Attempts { get; init; }

    public IReadOnlyList<TranslationQualityIssue> QualityIssues { get; init; } =
        Array.Empty<TranslationQualityIssue>();

    public bool IsReused => Status == TranslationUnitStatus.Reused;
}

/// <summary>Çalışma özeti. Hız değerleri gerçek süre ve gerçek kayıt sayılarından hesaplanır.</summary>
public sealed record TranslationRunMetrics
{
    public int TotalRecords { get; init; }
    public int TranslatableRecords { get; init; }
    public int CandidateRecords { get; init; }
    public int ReusedRecords { get; init; }
    public int NeedsReviewRecords { get; init; }
    public int FailedRecords { get; init; }
    public int SkippedRecords { get; init; }

    public int RequestCount { get; init; }
    public int RetryCount { get; init; }
    public int BatchCount { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public TimeSpan TotalRequestDuration { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }

    public string ProviderId { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = string.Empty;

    public int ProducedRecords =>
        CandidateRecords + ReusedRecords + NeedsReviewRecords;

    public double RecordsPerSecond => TotalDuration.TotalSeconds <= 0
        ? 0
        : ProducedRecords / TotalDuration.TotalSeconds;

    public double CharactersPerSecond => TotalDuration.TotalSeconds <= 0
        ? 0
        : TranslatedSourceCharacters / TotalDuration.TotalSeconds;

    public int TranslatedSourceCharacters { get; init; }

    public TimeSpan AverageRequestDuration => RequestCount <= 0
        ? TimeSpan.Zero
        : TimeSpan.FromTicks(TotalRequestDuration.Ticks / RequestCount);
}

/// <summary>Motorun sonucu. Başarısızlıkta bile üretilmiş kayıtlar korunur.</summary>
public sealed record TranslationRunResult
{
    public required string RunId { get; init; }

    public required TranslationRunStatus Status { get; init; }

    public TranslationRunFailureKind FailureKind { get; init; }

    public string? ErrorMessage { get; init; }

    public string? JournalPath { get; init; }

    /// <summary>Önceki bir günlükten kayıt geri yüklendi mi?</summary>
    public bool Resumed { get; init; }

    public IReadOnlyList<TranslationUnitResult> Units { get; init; } =
        Array.Empty<TranslationUnitResult>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public required TranslationRunMetrics Metrics { get; init; }
}

/// <summary>İlerleme bildirimi; UI iş parçacığına <see cref="IProgress{T}"/> ile taşınır.</summary>
public sealed record TranslationRunProgress(
    int Completed,
    int Total,
    int BatchIndex,
    int BatchCount,
    string Message,
    TimeSpan Elapsed);

/// <summary>Çeviri motoru sözleşmesi. Sağlayıcı dışarıdan verilir; motor HTTP'yi kendisi kurmaz.</summary>
public interface ITranslationEngine
{
    Task<TranslationRunResult> RunAsync(
        TranslationEngineRequest request,
        ITranslationProvider provider,
        IProgress<TranslationRunProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
