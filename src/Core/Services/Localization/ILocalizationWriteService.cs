namespace GameLanguageExplorer.Core.Services.Localization;

/// <summary>Güvenli yazma akışının ilerleme aşamaları.</summary>
public enum LocalizationWriteStage
{
    Validating = 0,
    BackingUp = 1,
    WritingTemporary = 2,
    Verifying = 3,
    Replacing = 4,
    Restoring = 5,
    Completed = 6
}

/// <summary>Yazma akışının UI'ya bildirilen ilerlemesi.</summary>
public sealed record LocalizationWriteProgress(
    LocalizationWriteStage Stage,
    string Message,
    int Percent);

/// <summary>Güvenli yazma/geri yükleme işleminin sonucu.</summary>
public enum LocalizationWriteStatus
{
    Success = 0,
    NotFound = 1,
    PathRejected = 2,
    UnsupportedFormat = 3,
    NoTranslations = 4,
    SourceChanged = 5,
    BackupFailed = 6,
    WriteFailed = 7,
    VerificationFailed = 8,
    Restored = 9,
    RestoreFailed = 10,
    Cancelled = 11
}

/// <summary>Güvenli yazma sonucu. Başarısız olsa bile yedek bilgisi taşır.</summary>
public sealed record LocalizationWriteResult(
    bool Success,
    LocalizationWriteStatus Status,
    string Message,
    string? OriginalFilePath = null,
    string? BackupFilePath = null,
    int? BackupRecordId = null,
    string? OriginalSha256 = null,
    string? NewSha256 = null,
    bool RestoredFromBackup = false)
{
    public static LocalizationWriteResult Fail(
        LocalizationWriteStatus status,
        string message,
        string? originalFilePath = null) => new(false, status, message, originalFilePath);
}

/// <summary>Güvenli yazma akışının ayarları.</summary>
public sealed class LocalizationWriteOptions
{
    /// <summary>Yedeklerin tutulacağı klasör. Boşsa yerel uygulama verisi kullanılır.</summary>
    public string? BackupDirectory { get; init; }

    /// <summary>Yedekleme ve doğrulama için en büyük dosya boyutu (varsayılan 32 MB).</summary>
    public long MaxFileBytes { get; init; } = 32L * 1024 * 1024;
}

/// <summary>
/// Seçilen dil dosyasına çeviri sonuçlarını güvenli biçimde yazar.
/// Akış: yedek → geçici dosya → SHA-256 doğrulama → atomik taşıma → gerekirse geri yükleme.
/// </summary>
public interface ILocalizationWriteService
{
    Task<LocalizationWriteResult> WriteAsync(
        int installedGameId,
        int discoveredFileId,
        IReadOnlyDictionary<string, string> translations,
        CancellationToken cancellationToken = default,
        IProgress<LocalizationWriteProgress>? progress = null);

    /// <summary>Daha önce oluşturulan bir yedeği açıkça geri yükler.</summary>
    Task<LocalizationWriteResult> RestoreAsync(
        int backupRecordId,
        CancellationToken cancellationToken = default);
}
