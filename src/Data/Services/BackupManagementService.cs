namespace GameLanguageExplorer.Data.Services;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services.Localization;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// <see cref="BackupRecord"/> tablosunu yedek yönetim ekranı için listeler ve temizler.
/// Yedek dosyası silme işlemi yapılandırılmış yedek klasörüyle sınırlıdır.
/// </summary>
public sealed class BackupManagementService : IBackupManagementService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly LocalizationWriteOptions _options;

    public BackupManagementService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        LocalizationWriteOptions? options = null)
    {
        _dbContextFactory = dbContextFactory
            ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _options = options ?? new LocalizationWriteOptions();
    }

    public async Task<IReadOnlyList<BackupRecordInfo>> GetBackupsAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        IQueryable<BackupRecord> query = db.BackupRecords.AsNoTracking();
        var filter = searchText?.Trim();

        if (!string.IsNullOrEmpty(filter))
        {
            query = query.Where(record =>
                record.OriginalFilePath.Contains(filter)
                || record.BackupFilePath.Contains(filter));
        }

        var records = await query
            .OrderByDescending(record => record.CreatedAt)
            .ThenByDescending(record => record.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records.Select(ToInfo).ToArray();
    }

    public async Task<BackupDeleteResult> DeleteAsync(
        int backupRecordId,
        CancellationToken cancellationToken = default)
    {
        if (backupRecordId <= 0)
        {
            return new BackupDeleteResult(
                BackupDeleteStatus.NotFound,
                "Yedek kaydı bulunamadı.");
        }

        await using var db = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var record = await db.BackupRecords
            .FirstOrDefaultAsync(item => item.Id == backupRecordId, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return new BackupDeleteResult(
                BackupDeleteStatus.NotFound,
                "Yedek kaydı bulunamadı.");
        }

        var backupDirectory = ResolveBackupDirectory();
        var backupPath = PathSafety.TryNormalize(record.BackupFilePath);

        if (backupPath is null || !PathSafety.IsUnderRootResolved(backupPath, backupDirectory))
        {
            return new BackupDeleteResult(
                BackupDeleteStatus.PathRejected,
                "Yedek dosyası yapılandırılmış yedek klasörünün dışında; güvenlik nedeniyle silinmedi.");
        }

        try
        {
            var fileDeleted = false;

            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
                fileDeleted = true;
            }

            db.BackupRecords.Remove(record);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new BackupDeleteResult(
                BackupDeleteStatus.Deleted,
                fileDeleted
                    ? "Yedek dosyası ve kaydı silindi."
                    : "Yedek dosyası diskte bulunamadı; kayıt silindi.",
                RecordDeleted: true,
                FileDeleted: fileDeleted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new BackupDeleteResult(
                BackupDeleteStatus.DeleteFailed,
                $"Yedek silinemedi: {ex.Message}");
        }
    }

    private static BackupRecordInfo ToInfo(BackupRecord record)
    {
        long? size = null;
        var available = false;

        try
        {
            var file = new FileInfo(record.BackupFilePath);
            available = file.Exists;
            size = available ? file.Length : null;
        }
        catch
        {
            // Geçersiz yol kaydı listede "dosya yok" olarak gösterilir.
        }

        return new BackupRecordInfo(
            record.Id,
            SafeFileName(record.BackupFilePath),
            SafeFileName(record.OriginalFilePath),
            record.OriginalFilePath,
            record.BackupFilePath,
            size,
            record.OriginalSha256,
            record.CreatedAt,
            record.IsRestored,
            record.RestoredAt,
            available);
    }

    private static string SafeFileName(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(name) ? "—" : name;
        }
        catch
        {
            return "—";
        }
    }

    private string ResolveBackupDirectory()
        => string.IsNullOrWhiteSpace(_options.BackupDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameLanguageExplorer",
                "backups")
            : Path.GetFullPath(_options.BackupDirectory);
}
