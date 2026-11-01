namespace GameLanguageExplorer.Core.Services.Localization;

/// <summary>
/// Yedek yönetim ekranı için dosya sistemi ayrıntılarıyla zenginleştirilmiş yedek kaydı.
/// </summary>
public sealed record BackupRecordInfo(
    int Id,
    string BackupFileName,
    string OriginalFileName,
    string OriginalFilePath,
    string BackupFilePath,
    long? BackupSizeBytes,
    string OriginalSha256,
    DateTime CreatedAt,
    bool IsRestored,
    DateTime? RestoredAt,
    bool IsBackupFileAvailable);

/// <summary>Yedek silme işleminin sonucu.</summary>
public enum BackupDeleteStatus
{
    Deleted = 0,
    NotFound = 1,
    PathRejected = 2,
    DeleteFailed = 3
}

public sealed record BackupDeleteResult(
    BackupDeleteStatus Status,
    string Message,
    bool RecordDeleted = false,
    bool FileDeleted = false)
{
    public bool Succeeded => Status == BackupDeleteStatus.Deleted;
}

/// <summary>
/// <c>BackupRecord</c> tablosunu listeleyen ve güvenli biçimde temizleyen servis.
/// Geri yükleme mevcut <see cref="ILocalizationWriteService.RestoreAsync"/> akışını kullanır.
/// </summary>
public interface IBackupManagementService
{
    /// <summary>
    /// Yedekleri en yeni önce olacak şekilde listeler. Arama metni verilirse yedek dosyası
    /// veya orijinal yol içinde arar.
    /// </summary>
    Task<IReadOnlyList<BackupRecordInfo>> GetBackupsAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kaydı ve yedek dosyasını siler. Yedek dosyası yapılandırılmış yedek klasörünün
    /// dışındaysa güvenlik nedeniyle işlem reddedilir.
    /// </summary>
    Task<BackupDeleteResult> DeleteAsync(
        int backupRecordId,
        CancellationToken cancellationToken = default);
}
