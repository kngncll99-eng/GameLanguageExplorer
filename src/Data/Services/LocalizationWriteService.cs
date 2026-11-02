namespace GameLanguageExplorer.Data.Services;

using System.Security.Cryptography;
using System.Text;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Dil dosyasına yazmayı güvenli bir akışla gerçekleştirir:
/// yedek → geçici dosya → SHA-256 doğrulama → atomik taşıma → gerekirse geri yükleme.
/// </summary>
public sealed class LocalizationWriteService : ILocalizationWriteService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly LocalizationFormatAdapterRegistry _registry;
    private readonly LocalizationWriteOptions _options;
    private readonly ILocalizationFileHashProvider _hashProvider;
    private readonly ILocalizationFileReplacer _replacer;
    private readonly ILogger<LocalizationWriteService> _logger;

    public LocalizationWriteService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        LocalizationFormatAdapterRegistry registry,
        LocalizationWriteOptions? options = null,
        ILocalizationFileHashProvider? hashProvider = null,
        ILocalizationFileReplacer? replacer = null,
        ILogger<LocalizationWriteService>? logger = null)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options ?? new LocalizationWriteOptions();
        _hashProvider = hashProvider ?? new Sha256LocalizationFileHashProvider();
        _replacer = replacer ?? new AtomicLocalizationFileReplacer();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalizationWriteService>.Instance;
    }

    public async Task<LocalizationWriteResult> WriteAsync(
        int installedGameId,
        int discoveredFileId,
        IReadOnlyDictionary<string, string> translations,
        CancellationToken cancellationToken = default,
        IProgress<LocalizationWriteProgress>? progress = null,
        string? expectedSourceSha256 = null)
    {
        try
        {
            return await WriteCoreAsync(
                    installedGameId,
                    discoveredFileId,
                    translations,
                    cancellationToken,
                    progress,
                    expectedSourceSha256)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.Cancelled,
                "Yazma işlemi kullanıcı tarafından iptal edildi.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Güvenli dil dosyası yazma işlemi başarısız: oyun={GameId} dosya={FileId}",
                installedGameId, discoveredFileId);

            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.WriteFailed,
                $"Güvenli yazma başarısız: {ex.Message}");
        }
    }

    private async Task<LocalizationWriteResult> WriteCoreAsync(
        int installedGameId,
        int discoveredFileId,
        IReadOnlyDictionary<string, string> translations,
        CancellationToken cancellationToken,
        IProgress<LocalizationWriteProgress>? progress,
        string? expectedSourceSha256)
    {
        if (translations is null || translations.Count == 0)
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.NoTranslations,
                "Yazılacak çeviri kaydı yok.");
        }

        progress?.Report(new LocalizationWriteProgress(
            LocalizationWriteStage.Validating, "Dosya ve yol doğrulanıyor…", 5));

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var install = await db.InstalledGames
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == installedGameId, cancellationToken)
            .ConfigureAwait(false);

        if (install is null)
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.NotFound,
                $"Kurulum kaydı bulunamadı (Id={installedGameId}).");
        }

        // Yazma sırasında SHA-256 alanını güncelleyebilmek için kayıt tracked tutulur.
        var file = await db.DiscoveredFiles
            .FirstOrDefaultAsync(f => f.Id == discoveredFileId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.NotFound,
                $"Keşfedilmiş dosya kaydı bulunamadı (Id={discoveredFileId}).");
        }

        if (file.InstalledGameId != installedGameId)
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.PathRejected,
                "Dosya kaydı seçili kuruluma ait değil; yazma reddedildi.",
                file.FilePath);
        }

        var root = PathSafety.TryNormalize(install.InstallPath);
        var path = PathSafety.TryNormalize(file.FilePath);

        if (root is null || path is null || !PathSafety.IsUnderRootResolved(path, root))
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.PathRejected,
                "Dosya yolu kurulum kökünün içinde değil veya bağlantılar güvenilir biçimde " +
                "çözümlenemedi; yazma reddedildi.",
                file.FilePath);
        }

        if (!File.Exists(path))
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.NotFound,
                $"Dosya diskte bulunamadı: '{path}'.",
                path);
        }

        var sourceEncoding = LocalizationFileReader.DetectEncoding(path);

        if (sourceEncoding is null)
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.UnsupportedEncoding,
                "Kaynak dosyanın kodlaması güvenilir biçimde belirlenemedi; sessiz dönüşüm yapılmadı.",
                path);
        }

        // Sembolik bağlantı hedefi kurulum kökü dışına çıkıyorsa yazma yapılmaz.
        var fileInfo = new FileInfo(path);

        if (fileInfo.LinkTarget is not null)
        {
            var target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);

            if (target is null || !PathSafety.IsUnderRoot(target.FullName, root))
            {
                return LocalizationWriteResult.Fail(
                    LocalizationWriteStatus.PathRejected,
                    "Dosya kök dizin dışına işaret ediyor; yazma reddedildi.",
                    path);
            }
        }

        if (File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.PathRejected,
                "Dosya salt okunur; yazma reddedildi.",
                path);
        }

        var extension = Path.GetExtension(path);
        var adapter = ResolveAdapter(path, extension);

        if (adapter is null
            || !adapter.SupportsWrite
            || adapter is not ILocalizationWritableFormatAdapter writableAdapter)
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.UnsupportedFormat,
                $"'{extension}' uzantısı için yazılabilir adaptör yok.",
                path);
        }

        var originalSha256 = await _hashProvider.ComputeSha256Async(path, cancellationToken)
            .ConfigureAwait(false);

        var expected = expectedSourceSha256?.Trim();

        if ((!string.IsNullOrWhiteSpace(file.Sha256)
                && !string.Equals(file.Sha256, originalSha256, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(expected)
                && !string.Equals(expected, originalSha256, StringComparison.OrdinalIgnoreCase)))
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.SourceChanged,
                "Kaynak dosya önizlemeden/son taramadan sonra değişmiş; yazma reddedildi.",
                path);
        }

        var read = await adapter.ReadAsync(
                path,
                new LocalizationFormatReadOptions
                {
                    MaxFileBytes = _options.MaxFileBytes
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!read.Success)
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.WriteFailed,
                read.ErrorMessage ?? "Kaynak dosya yazma için okunamadı.",
                path);
        }

        var entriesByKey = read.Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var writeTranslations = new List<LocalizationWriteTranslation>();

        foreach (var pair in translations)
        {
            if (!entriesByKey.TryGetValue(pair.Key, out var entry))
            {
                return LocalizationWriteResult.Fail(
                    LocalizationWriteStatus.WriteFailed,
                    $"Çeviri anahtarı kaynak dosyada bulunamadı: '{pair.Key}'.",
                    path);
            }

            if (entry.Status == LocalizationEntryStatus.Skipped)
            {
                return LocalizationWriteResult.Fail(
                    LocalizationWriteStatus.WriteFailed,
                    $"'{pair.Key}' çevrilebilir bir kayıt değil; yazma reddedildi.",
                    path);
            }

            var validationSource = entry.SourceTextForTranslation ?? entry.SourceText;
            var validationError = LocalizationWriteValidator.Validate(
                entry.Key,
                validationSource,
                pair.Value);

            if (validationError is not null)
            {
                return LocalizationWriteResult.Fail(
                    LocalizationWriteStatus.WriteFailed,
                    validationError,
                    path);
            }

            writeTranslations.Add(new LocalizationWriteTranslation(
                entry.Key,
                entry.SourceText,
                pair.Value));
        }


        var content = await writableAdapter.BuildWriteContentAsync(
                path,
                writeTranslations,
                new LocalizationFormatWriteOptions
                {
                    MaxFileBytes = _options.MaxFileBytes
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!content.Success || content.Content is null)
        {
            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.WriteFailed,
                content.ErrorMessage ?? "Yeni dosya içeriği üretilemedi.",
                path);
        }

        var newContent = content.Content;
        var newBytes = sourceEncoding.Encode(newContent);
        var expectedNewSha256 = Convert
            .ToHexString(SHA256.HashData(newBytes))
            .ToLowerInvariant();
        var backupDirectory = ResolveBackupDirectory();
        Directory.CreateDirectory(backupDirectory);

        var backupPath = Path.Combine(backupDirectory, $"{Guid.NewGuid():N}.bak");
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.gle-tmp");

        BackupRecord? backupRecord = null;
        var originalFileSize = new FileInfo(path).Length;

        try
        {
            progress?.Report(new LocalizationWriteProgress(
                LocalizationWriteStage.BackingUp, "Orijinal dosya yedekleniyor…", 20));
            cancellationToken.ThrowIfCancellationRequested();

            File.Copy(path, backupPath, overwrite: false);
            var backupSha256 = await _hashProvider.ComputeSha256Async(backupPath, cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(backupSha256, originalSha256, StringComparison.OrdinalIgnoreCase))
            {
                SafeDelete(backupPath);
                return LocalizationWriteResult.Fail(
                    LocalizationWriteStatus.BackupFailed,
                    "Yedek dosyasının SHA-256 değeri orijinalle eşleşmiyor.",
                    path);
            }

            backupRecord = new BackupRecord
            {
                OriginalFilePath = path,
                BackupFilePath = backupPath,
                OriginalSha256 = originalSha256,
                CreatedAt = DateTime.UtcNow
            };

            db.BackupRecords.Add(backupRecord);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            progress?.Report(new LocalizationWriteProgress(
                LocalizationWriteStage.WritingTemporary, "Yeni içerik geçici dosyaya yazılıyor…", 45));
            cancellationToken.ThrowIfCancellationRequested();

            await WriteAllBytesDurableAsync(temporaryPath, newBytes, cancellationToken).ConfigureAwait(false);

            var temporarySha256 = await _hashProvider.ComputeSha256Async(temporaryPath, cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(temporarySha256, expectedNewSha256, StringComparison.OrdinalIgnoreCase))
            {
                SafeDelete(temporaryPath);
                return LocalizationWriteResult.Fail(
                    LocalizationWriteStatus.VerificationFailed,
                    "Geçici dosyanın SHA-256 doğrulaması başarısız; orijinal dosya değiştirilmedi.",
                    path);
            }

            progress?.Report(new LocalizationWriteProgress(
                LocalizationWriteStage.Verifying, "Geçici dosyanın SHA-256 değeri doğrulandı.", 70));

            var beforeReplaceSha256 = await _hashProvider.ComputeSha256Async(path, cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(beforeReplaceSha256, originalSha256, StringComparison.OrdinalIgnoreCase))
            {
                SafeDelete(temporaryPath);
                return LocalizationWriteResult.Fail(
                    LocalizationWriteStatus.SourceChanged,
                    "Dosya yazma sırasında değişti; orijinal dosya değiştirilmedi.",
                    path);
            }


            try
            {
                progress?.Report(new LocalizationWriteProgress(
                    LocalizationWriteStage.Replacing, "Dosya atomik olarak değiştiriliyor…", 80));
                cancellationToken.ThrowIfCancellationRequested();

                _replacer.Replace(temporaryPath, path);
                temporaryPath = string.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Kritik noktadan önce iptal: orijinal dosya değiştirilmedi.
                SafeDelete(temporaryPath);
                throw;
            }
            catch (Exception ex)
            {
                SafeDelete(temporaryPath);
                _logger.LogError(ex, "Dosya değiştirilemedi: {Path}", path);

                using var recoveryCts = CreateRecoveryCancellationSource();
                var currentSha256 = await TryComputeSha256Async(path, recoveryCts.Token)
                    .ConfigureAwait(false);

                if (string.Equals(currentSha256, originalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return LocalizationWriteResult.Fail(
                        LocalizationWriteStatus.WriteFailed,
                        $"Dosya değiştirilemedi: {ex.Message}",
                        path);
                }

                var restore = await TryRestoreAsync(
                        backupRecord, path, originalSha256, originalFileSize, recoveryCts.Token)
                    .ConfigureAwait(false);

                return BuildRestoreResult(
                    restore, path, backupRecord, originalSha256, expectedNewSha256,
                    $"Dosya değiştirilemedi: {ex.Message}");
            }

            // Kritik noktadan sonra kullanıcı iptali yok sayılır; doğrulama, DB ve
            // gerekirse kurtarma ayrı bir süre sınırıyla tamamlanır.
            return await CompleteReplacedWriteAsync(
                    db,
                    file,
                    backupRecord,
                    path,
                    originalSha256,
                    originalFileSize,
                    expectedNewSha256,
                    newBytes.Length,
                    progress)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SafeDelete(temporaryPath);
            throw;
        }
        catch (Exception ex)
        {
            SafeDelete(temporaryPath);
            _logger.LogError(ex, "Güvenli yazma hazırlığı başarısız: {Path}", path);

            if (backupRecord is null)
            {
                return LocalizationWriteResult.Fail(
                    LocalizationWriteStatus.WriteFailed,
                    $"Yazma başarısız: {ex.Message}",
                    path);
            }

            using var recoveryCts = CreateRecoveryCancellationSource();
            var currentSha256 = await TryComputeSha256Async(path, recoveryCts.Token)
                .ConfigureAwait(false);

            if (string.Equals(currentSha256, originalSha256, StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationWriteResult.Fail(
                    LocalizationWriteStatus.WriteFailed,
                    $"Yazma başarısız; orijinal dosya değiştirilmedi: {ex.Message}",
                    path);
            }

            var restore = await TryRestoreAsync(
                    backupRecord, path, originalSha256, originalFileSize, recoveryCts.Token)
                .ConfigureAwait(false);

            return BuildRestoreResult(
                restore, path, backupRecord, originalSha256, expectedNewSha256,
                $"Yazma başarısız: {ex.Message}");
        }
    }


    /// <summary>
    /// Dosya değiştirildikten sonraki kritik bölüm. Kullanıcı iptal token'ı yerine
    /// ayrı bir kurtarma süresi kullanır; dosya ile DB aynı sonuca bağlanır.
    /// </summary>
    private async Task<LocalizationWriteResult> CompleteReplacedWriteAsync(
        AppDbContext db,
        DiscoveredFile file,
        BackupRecord backupRecord,
        string path,
        string originalSha256,
        long originalFileSize,
        string expectedNewSha256,
        long newFileSize,
        IProgress<LocalizationWriteProgress>? progress)
    {
        using var recoveryCts = CreateRecoveryCancellationSource();
        var recoveryToken = recoveryCts.Token;

        try
        {
            var finalSha256 = await _hashProvider.ComputeSha256Async(path, recoveryToken)
                .ConfigureAwait(false);

            if (!string.Equals(finalSha256, expectedNewSha256, StringComparison.OrdinalIgnoreCase))
            {
                var restore = await TryRestoreAsync(
                        backupRecord, path, originalSha256, originalFileSize, recoveryToken)
                    .ConfigureAwait(false);

                return BuildRestoreResult(
                    restore, path, backupRecord, originalSha256, expectedNewSha256,
                    "Yazma sonrası SHA-256 doğrulaması başarısız.");
            }

            file.Sha256 = expectedNewSha256;
            file.FileSize = newFileSize;
            file.LastValidationResult = "Güvenli yazma tamamlandı; SHA-256 doğrulandı.";
            file.LastValidatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(recoveryToken).ConfigureAwait(false);

            progress?.Report(new LocalizationWriteProgress(
                LocalizationWriteStage.Completed, "Güvenli yazma tamamlandı.", 100));

            return new LocalizationWriteResult(
                true,
                LocalizationWriteStatus.Success,
                "Çeviriler güvenli biçimde dosyaya yazıldı.",
                path,
                backupRecord.BackupFilePath,
                backupRecord.Id,
                originalSha256,
                expectedNewSha256);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yazma sonrası doğrulama/DB güncellemesi başarısız: {Path}", path);

            progress?.Report(new LocalizationWriteProgress(
                LocalizationWriteStage.Restoring, "Hata oluştu; yedekten geri yükleniyor…", 90));

            using var restoreCts = CreateRecoveryCancellationSource();
            var restore = await TryRestoreAsync(
                    backupRecord, path, originalSha256, originalFileSize, restoreCts.Token)
                .ConfigureAwait(false);

            return BuildRestoreResult(
                restore, path, backupRecord, originalSha256, expectedNewSha256,
                $"Yazma sonrası doğrulama/DB güncellemesi başarısız: {ex.Message}");
        }
    }

    private CancellationTokenSource CreateRecoveryCancellationSource()
    {
        var timeout = _options.RecoveryTimeout;

        return timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan
            ? new CancellationTokenSource()
            : new CancellationTokenSource(timeout);
    }

    private async Task<string?> TryComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _hashProvider.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<LocalizationWriteResult> RestoreAsync(
        int backupRecordId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var record = await db.BackupRecords
                .FirstOrDefaultAsync(b => b.Id == backupRecordId, cancellationToken)
                .ConfigureAwait(false);

            if (record is null)
            {
                return LocalizationWriteResult.Fail(
                    LocalizationWriteStatus.NotFound,
                    $"Yedek kaydı bulunamadı (Id={backupRecordId}).");
            }

            var originalPath = PathSafety.TryNormalize(record.OriginalFilePath);

            if (originalPath is null)
            {
                return new LocalizationWriteResult(
                    false,
                    LocalizationWriteStatus.PathRejected,
                    "Geri yükleme reddedildi: yedek kaydındaki orijinal dosya yolu geçersiz. "
                    + $"Yedek bozuk olduğu için değil, hedef yol doğrulanamadığı için işlem yapılmadı. "
                    + $"Yedek korunuyor: '{record.BackupFilePath}'.",
                    record.BackupFilePath);
            }

            if (!File.Exists(record.BackupFilePath))
            {
                return LocalizationWriteResult.Fail(
                    LocalizationWriteStatus.NotFound,
                    $"Yedek dosyası diskte bulunamadı: '{record.BackupFilePath}'. "
                    + "Yedek kaydı korunuyor.",
                    originalPath);
            }

            // Geri yükleme hedefi de yazma ile aynı güven sınırından geçer: kayıtlı
            // kurulum kökü bulunmalı ve bağlantılar çözüldüğünde hedef kök altında kalmalı.
            var installPaths = await db.DiscoveredFiles
                .AsNoTracking()
                .Where(file => file.FilePath == record.OriginalFilePath
                               || file.FilePath == originalPath)
                .Select(file => file.InstalledGame.InstallPath)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var root = installPaths
                .Select(PathSafety.TryNormalize)
                .FirstOrDefault(candidate => candidate is not null
                    && PathSafety.IsUnderRootResolved(originalPath, candidate));

            if (root is null)
            {
                var reason = installPaths.Count == 0
                    ? "bu dosya için kurulum/dosya kaydı bulunamadı"
                    : "hedef, kayıtlı kurulum kökü altında güvenilir biçimde çözümlenemedi";

                return new LocalizationWriteResult(
                    false,
                    LocalizationWriteStatus.PathRejected,
                    $"Geri yükleme reddedildi: {reason}. Yedek bozuk olduğu için değil, "
                    + "hedef kök doğrulanamadığı için işlem yapılmadı. "
                    + $"Yedek korunuyor: '{record.BackupFilePath}'.",
                    originalPath,
                    record.BackupFilePath,
                    record.Id,
                    record.OriginalSha256);
            }

            var originalFileSize = new FileInfo(record.BackupFilePath).Length;
            var restore = await TryRestoreAsync(
                    record, originalPath, record.OriginalSha256, originalFileSize, cancellationToken)
                .ConfigureAwait(false);

            if (!restore.FileRestored)
            {
                return LocalizationWriteResult.Fail(
                    LocalizationWriteStatus.RestoreFailed,
                    "Yedekten geri yükleme başarısız oldu; yedek korunuyor."
                    + (restore.ErrorMessage is null ? string.Empty : " " + restore.ErrorMessage),
                    originalPath);
            }

            if (!restore.DatabaseUpdated)
            {
                return new LocalizationWriteResult(
                    false,
                    LocalizationWriteStatus.RestoreFailed,
                    "Dosya yedekten geri yüklendi ancak veritabanı güncellenemedi; " +
                    "yedek korunuyor. " + (restore.ErrorMessage ?? string.Empty),
                    originalPath,
                    record.BackupFilePath,
                    record.Id,
                    record.OriginalSha256,
                    RestoredFromBackup: true);
            }

            return new LocalizationWriteResult(
                true,
                LocalizationWriteStatus.Restored,
                "Yedek başarıyla geri yüklendi.",
                originalPath,
                record.BackupFilePath,
                record.Id,
                record.OriginalSha256,
                RestoredFromBackup: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yedekten geri yükleme başarısız: yedek={BackupId}", backupRecordId);

            return LocalizationWriteResult.Fail(
                LocalizationWriteStatus.RestoreFailed,
                $"Yedekten geri yükleme başarısız: {ex.Message}");
        }
    }

    private sealed record RestoreAttemptResult(
        bool FileRestored,
        bool DatabaseUpdated,
        string? ErrorMessage);

    /// <summary>
    /// Yedeği doğrulayıp orijinal konuma geri yükler. Dosya değiştikten sonra DB
    /// güncellemesi için yeni bir context kullanılır; başarısız olsa da yedek silinmez.
    /// </summary>
    private async Task<RestoreAttemptResult> TryRestoreAsync(
        BackupRecord record,
        string originalPath,
        string expectedOriginalSha256,
        long originalFileSize,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(record.BackupFilePath))
        {
            return new RestoreAttemptResult(
                false, false, "Yedek dosyası diskte bulunamadı.");
        }

        var restoreTemporaryPath = Path.Combine(
            Path.GetDirectoryName(originalPath)!,
            $".{Path.GetFileName(originalPath)}.{Guid.NewGuid():N}.restore-tmp");
        var fileRestored = false;

        try
        {
            var currentSha256 = await TryComputeSha256Async(originalPath, cancellationToken)
                .ConfigureAwait(false);

            if (string.Equals(currentSha256, expectedOriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                fileRestored = true;
            }
            else
            {
                var backupSha256 = await _hashProvider
                    .ComputeSha256Async(record.BackupFilePath, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.Equals(backupSha256, expectedOriginalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new RestoreAttemptResult(
                        false, false, "Yedek dosyasının SHA-256 değeri orijinalle eşleşmiyor.");
                }

                File.Copy(record.BackupFilePath, restoreTemporaryPath, overwrite: true);
                _replacer.Replace(restoreTemporaryPath, originalPath);
                restoreTemporaryPath = string.Empty;
                fileRestored = true;

                var restoredSha256 = await _hashProvider
                    .ComputeSha256Async(originalPath, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.Equals(restoredSha256, expectedOriginalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new RestoreAttemptResult(
                        false, false, "Geri yüklenen dosyanın SHA-256 doğrulaması başarısız.");
                }
            }

            await using var recoveryDb = await _dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            var backupRecord = await recoveryDb.BackupRecords
                .FirstOrDefaultAsync(item => item.Id == record.Id, cancellationToken)
                .ConfigureAwait(false);

            if (backupRecord is not null)
            {
                backupRecord.IsRestored = true;
                backupRecord.RestoredAt = DateTime.UtcNow;
            }

            var file = await recoveryDb.DiscoveredFiles
                .FirstOrDefaultAsync(item => item.FilePath == originalPath, cancellationToken)
                .ConfigureAwait(false);

            if (file is not null)
            {
                file.Sha256 = expectedOriginalSha256;
                file.FileSize = originalFileSize;
                file.LastValidationResult = "Yedekten geri yükleme tamamlandı.";
                file.LastValidatedAt = DateTime.UtcNow;
            }

            await recoveryDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new RestoreAttemptResult(true, true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yedekten geri yükleme denemesi başarısız: {Path}", originalPath);
            return new RestoreAttemptResult(fileRestored, false, ex.Message);
        }
        finally
        {
            SafeDelete(restoreTemporaryPath);
        }
    }


    /// <summary>
    /// Aynı uzantıyı birden fazla adaptör destekliyorsa (`.csv` → CSV/LocresCSV) başlıkla seçim yapar.
    /// Böylece LocresCSV dosyası yanlışlıkla CSV olarak yazılmaz.
    /// </summary>
    private ILocalizationFormatAdapter? ResolveAdapter(string path, string extension)
    {
        var candidates = _registry.FindAllByExtension(extension);

        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var head = LocalizationFileReader.ReadHead(path);

        return (head is null ? null : _registry.FindByHeader(path, head)) ?? candidates[0];
    }

    private static LocalizationWriteResult BuildRestoreResult(
        RestoreAttemptResult restore,
        string path,
        BackupRecord? backupRecord,
        string originalSha256,
        string expectedNewSha256,
        string reason)
    {
        var message = restore switch
        {
            { FileRestored: true, DatabaseUpdated: true } =>
                $"{reason} Orijinal dosya yedekten geri yüklendi.",
            { FileRestored: true, DatabaseUpdated: false } =>
                $"{reason} Dosya yedeğe döndürüldü ancak veritabanı güncellenemedi; "
                + "yedek korunuyor."
                + (restore.ErrorMessage is null ? string.Empty : " " + restore.ErrorMessage),
            _ =>
                $"{reason} Yedekten geri yükleme de başarısız oldu; yedek korunuyor."
                + (restore.ErrorMessage is null ? string.Empty : " " + restore.ErrorMessage)
        };

        var restored = restore.FileRestored && restore.DatabaseUpdated;

        return new LocalizationWriteResult(
            false,
            restored ? LocalizationWriteStatus.Restored : LocalizationWriteStatus.RestoreFailed,
            message,
            path,
            backupRecord?.BackupFilePath,
            backupRecord?.Id,
            originalSha256,
            expectedNewSha256,
            restore.FileRestored);
    }

    private string ResolveBackupDirectory()
        => string.IsNullOrWhiteSpace(_options.BackupDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameLanguageExplorer",
                "backups")
            : Path.GetFullPath(_options.BackupDirectory);

    /// <summary>Geçici dosyayı, kaynak kodlama/BOM ile yazar ve disk tamponunu boşaltır.</summary>
    private static async Task WriteAllBytesDurableAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.WriteThrough);

        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void SafeDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Geçici dosya silinemezse asıl hata maskelenmez.
        }
    }

}
