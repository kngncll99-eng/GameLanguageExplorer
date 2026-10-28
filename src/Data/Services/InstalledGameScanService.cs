namespace GameLanguageExplorer.Data.Services;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Kurulu oyun taramasını yürütür ve sonuçları veritabanına yazar.
///
/// <para>Güvenlik/veri bütünlüğü kuralları:</para>
/// <list type="bullet">
/// <item>Oyun dosyalarına yazılmaz, taşınmaz, silinmez (sağlayıcılar salt okunur).</item>
/// <item>Kaybolan kurulumlar <b>silinmez</b>, <c>IsInstalled = false</c> olarak işaretlenir.</item>
/// <item>Aynı kurulum tekrar tarandığında yinelenen kayıt oluşmaz.</item>
/// <item>Katalog kaydı olmayan kurulumlar için oyun kaydı oluşturulur; katalog senkronizasyonu
/// AppID üzerinden aynı kaydı bulup güncelleyebilir.</item>
/// </list>
/// </summary>
public class InstalledGameScanService : IInstalledGameScanService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IEnumerable<IInstalledGameProvider> _providers;
    private readonly ILogger<InstalledGameScanService> _logger;

    public InstalledGameScanService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IEnumerable<IInstalledGameProvider> providers,
        ILogger<InstalledGameScanService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<InstalledGameScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        var result = new InstalledGameScanResult();

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var now = DateTime.UtcNow;
            var scannedProviders = new List<string>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var catalogSourceId = await GetOrCreateCatalogSourceIdAsync(db, cancellationToken).ConfigureAwait(false);

            foreach (var provider in _providers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                InstalledGameScanBatch batch;

                try
                {
                    batch = await provider.ScanAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Kurulu oyun sağlayıcısı başarısız: {Provider}", provider.ProviderName);
                    result.Warnings.Add($"{provider.ProviderName} taranamadı: {ex.Message}");
                    result.SkippedProviders.Add(provider.ProviderName);
                    continue;
                }

                if (batch is null)
                {
                    result.Warnings.Add($"{provider.ProviderName} taranamadı: sağlayıcı sonuç döndürmedi.");
                    result.SkippedProviders.Add(provider.ProviderName);
                    continue;
                }

                result.Warnings.AddRange(batch.Warnings);

                if (!batch.IsTrustworthy)
                {
                    // R1: kaynak erişilemedi/başarısız → bu sağlayıcının MEVCUT kayıtları korunur.
                    // Hiçbir kayıt eklenmez, güncellenmez ve "kurulu değil" işaretlenmez.
                    var reason = batch.ErrorMessage ?? "kaynak bu makinede bulunamadı";
                    result.SkippedProviders.Add(provider.ProviderName);
                    result.Warnings.Add($"{provider.ProviderName}: tarama güvenilir değil, mevcut kayıtlar korundu ({reason}).");
                    continue;
                }

                if (batch.IsAuthoritative)
                {
                    scannedProviders.Add(provider.ProviderName);
                }
                else
                {
                    // Kısmi tarama: bulunanlar yazılır ama "kurulu değil" işaretlemesi yapılmaz.
                    result.PartialProviders.Add(provider.ProviderName);
                    result.Warnings.Add($"{provider.ProviderName}: tarama kısmi tamamlandı; eksik kayıtlar 'kurulu değil' işaretlenmedi.");
                }

                foreach (var install in batch.Games)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await UpsertInstallationAsync(db, install, provider.ProviderName, catalogSourceId, seenKeys, now, result, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await MarkMissingAsNotInstalledAsync(db, scannedProviders, seenKeys, now, result, cancellationToken)
                .ConfigureAwait(false);

            result.Success = true;
            result.ScanCompletedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Kurulu oyun taraması tamamlandı: bulunan={Found}, eklenen={Added}, güncellenen={Updated}, değişmeyen={Unchanged}, kurulu-değil={NotInstalled}, yeni katalog kaydı={Created}",
                result.TotalFound, result.Added, result.Updated, result.Unchanged, result.MarkedNotInstalled, result.CatalogGamesCreated);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Kurulu oyun taraması iptal edildi.");
            result.Success = false;
            result.ErrorMessage = "Tarama iptal edildi";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kurulu oyun taraması başarısız.");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task<IReadOnlyList<InstalledGame>> GetInstalledGamesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.InstalledGames
            .AsNoTracking()
            .Include(i => i.Game)
            .Where(i => i.IsInstalled)
            .OrderBy(i => i.Game.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DateTime?> GetLastScanAtAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.InstalledGames
            .MaxAsync(i => (DateTime?)i.LastScanAt, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountInstalledAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.InstalledGames.CountAsync(i => i.IsInstalled, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Kullanıcının seçtiği klasörü <c>Manual</c> kaynağı olarak ekler.
    ///
    /// <list type="bullet">
    /// <item>Mağaza kimliği/StoreUrl <b>atanmaz</b> (kaynak doğrulanmamıştır).</item>
    /// <item>Oyun adı kullanıcıdan gelir; boşsa klasör adı kullanılır ve ad doğrulanmamış sayılır.</item>
    /// <item>Aynı klasör ikinci kez eklenirse mükerrer kayıt oluşturulmaz.</item>
    /// <item>Farklı klasörler ayrı kurulum kaydı olarak kalır.</item>
    /// <item>Mevcut katalog kaydıyla ada göre otomatik birleştirme yapılmaz.</item>
    /// </list>
    /// </summary>
    public async Task<ManualGameAddResult> AddManualGameAsync(
        string folderPath,
        string? gameName,
        CancellationToken cancellationToken = default)
    {
        var result = new ManualGameAddResult();

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            result.ErrorMessage = "Klasör yolu boş olamaz.";
            return result;
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(folderPath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            result.ErrorMessage = $"Geçersiz klasör yolu: {ex.Message}";
            return result;
        }

        if (!Directory.Exists(fullPath))
        {
            result.ErrorMessage = $"Klasör bulunamadı veya erişilemiyor: {fullPath}";
            return result;
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(fullPath).Any())
            {
                result.Warnings.Add("Klasör boş görünüyor; dil dosyası içermeyebilir.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Warnings.Add($"Klasör içeriği okunamadı: {ex.Message}");
        }

        var name = string.IsNullOrWhiteSpace(gameName) ? SafeFolderName(fullPath) : gameName.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            result.ErrorMessage = "Oyun adı belirlenemedi; lütfen bir ad girin.";
            return result;
        }

        return await SaveManualGameAsync(fullPath, name!, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManualGameAddResult> SaveManualGameAsync(
        string fullPath,
        string name,
        ManualGameAddResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var normalized = NormalizePath(fullPath);
            var lowered = normalized.ToLowerInvariant();

            var existing = await db.InstalledGames
                .Include(i => i.Game)
                .FirstOrDefaultAsync(
                    i => i.InstallSource == ManualProviderName && i.InstallPath.ToLower() == lowered,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                // Mükerrer kayıt oluşturma; isim verildiyse güncelle.
                if (!string.Equals(existing.Game.Name, name, StringComparison.Ordinal))
                {
                    existing.Game.Name = name;
                    existing.Game.UpdatedAt = DateTime.UtcNow;
                }

                existing.IsInstalled = true;
                existing.LastScanAt = DateTime.UtcNow;

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                result.Success = true;
                result.AlreadyExisted = true;
                result.InstalledGameId = existing.Id;
                result.GameId = existing.GameId;
                result.GameName = existing.Game.Name;
                return result;
            }

            var now = DateTime.UtcNow;

            // Mağaza/platform kaydı OLUŞTURULMAZ: kaynak doğrulanmamıştır.
            var game = new Game
            {
                Name = name,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Games.Add(game);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var install = new InstalledGame
            {
                GameId = game.Id,
                InstallPath = normalized,
                InstallSource = ManualProviderName,
                InstallSourceId = string.Empty,
                LastScanAt = now,
                IsInstalled = true
            };

            db.InstalledGames.Add(install);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            result.Success = true;
            result.InstalledGameId = install.Id;
            result.GameId = game.Id;
            result.GameName = game.Name;

            _logger.LogInformation("Manuel oyun klasörü eklendi: {Path} ({Game})", normalized, name);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manuel oyun klasörü eklenemedi: {Path}", fullPath);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private static string? SafeFolderName(string fullPath)
    {
        try
        {
            return new DirectoryInfo(fullPath).Name;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Mevcut kurulum kaydını bulur (önce kimlik+ yol, sonra kaynak + yol).</summary>
    private static async Task<InstalledGame?> FindExistingInstallAsync(
        AppDbContext db,
        string source,
        string sourceId,
        string installPath,
        CancellationToken cancellationToken)
    {
        var byKey = await db.InstalledGames
            .FirstOrDefaultAsync(
                i => i.InstallSource == source && i.InstallSourceId == sourceId && i.InstallPath == installPath,
                cancellationToken)
            .ConfigureAwait(false);

        if (byKey is not null)
        {
            return byKey;
        }

        // Kimlik boş olabilir (manuel kayıtlar). Kimlik taşıyan kaynaklarda (ör. Steam AppID)
        // yol tek başına kimlik değildir: aynı klasörü paylaşan iki AppID ayrı kayıt olmalıdır.
        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        return await db.InstalledGames
            .FirstOrDefaultAsync(i => i.InstallSource == source && i.InstallPath == installPath, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Manuel eklenen kurulumların kaynak adı (bkz. <see cref="GameSourceCatalog.ManualKey"/>).</summary>
    public const string ManualProviderName = GameSourceCatalog.ManualKey;





    private async Task UpsertInstallationAsync(
        AppDbContext db,
        InstalledGame install,
        string providerName,
        int? catalogSourceId,
        HashSet<string> seenKeys,
        DateTime now,
        InstalledGameScanResult result,
        CancellationToken cancellationToken)
    {
        var installPath = install.InstallPath?.Trim();

        if (string.IsNullOrWhiteSpace(installPath))
        {
            result.Warnings.Add($"{providerName}: boş kurulum yolu olan kayıt atlandı.");
            return;
        }

        var source = string.IsNullOrWhiteSpace(install.InstallSource) ? providerName : install.InstallSource!;
        var sourceId = install.InstallSourceId?.Trim() ?? string.Empty;
        var normalizedPath = NormalizePath(installPath);
        var dedupeKey = $"{source}|{sourceId}|{normalizedPath}";

        if (!seenKeys.Add(dedupeKey))
        {
            // Aynı taramada aynı kurulum iki kez bildirildi (ör. iki kök aynı kütüphaneyi gösteriyor).
            return;
        }

        result.TotalFound++;

        var existing = await FindExistingInstallAsync(db, source, sourceId, installPath, cancellationToken)
            .ConfigureAwait(false);

        int? resolvedGameId;

        if (existing is not null && string.IsNullOrWhiteSpace(install.DetectedGameName))
        {
            // Ad taşınmıyor (ör. manuel kayıt): mevcut oyun bağlantısını koru, yeni oyun üretme.
            resolvedGameId = existing.GameId;
        }
        else
        {
            resolvedGameId = await ResolveGameIdAsync(db, source, sourceId, install.DetectedGameName, catalogSourceId, result, cancellationToken)
                .ConfigureAwait(false);

            resolvedGameId ??= existing?.GameId;

            if (resolvedGameId is not null && existing is null)
            {
                // Aynı oyun + aynı klasör ama yol biçimi farklı olabilir (ör. sürücü harfi büyük/küçük).
                var id = resolvedGameId.Value;

                existing = await db.InstalledGames
                    .FirstOrDefaultAsync(i => i.GameId == id && i.InstallPath == installPath, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (resolvedGameId is null)
        {
            result.Warnings.Add($"{providerName}: '{installPath}' için oyun adı belirlenemedi, kayıt atlandı.");
            return;
        }

        var gameId = resolvedGameId.Value;

        if (existing is null)
        {
            db.InstalledGames.Add(new InstalledGame
            {
                GameId = gameId,
                InstallPath = installPath,
                InstallSource = source,
                InstallSourceId = sourceId,
                SizeInBytes = install.SizeInBytes,
                InstalledAt = install.InstalledAt,
                LastScanAt = now,
                IsInstalled = install.IsInstalled
            });

            result.Added++;
            return;
        }

        var changed = false;

        if (existing.GameId != gameId) { existing.GameId = gameId; changed = true; }
        if (existing.InstallSource != source) { existing.InstallSource = source; changed = true; }
        if (existing.InstallSourceId != sourceId) { existing.InstallSourceId = sourceId; changed = true; }
        if (existing.SizeInBytes != install.SizeInBytes) { existing.SizeInBytes = install.SizeInBytes; changed = true; }
        if (existing.InstalledAt != install.InstalledAt) { existing.InstalledAt = install.InstalledAt; changed = true; }
        if (existing.IsInstalled != install.IsInstalled) { existing.IsInstalled = install.IsInstalled; changed = true; }

        existing.LastScanAt = now;

        if (changed)
        {
            result.Updated++;
        }
        else
        {
            result.Unchanged++;
        }
    }

    /// <summary>
    /// Kurulumu bir katalog oyununa bağlar. Sırasıyla: platform kimliği (Steam AppID) → oyun adı
    /// → yeni kayıt. Böylece katalog senkronizasyonu ile aynı oyun iki kez oluşmaz.
    /// </summary>
    private static async Task<int?> ResolveGameIdAsync(
        AppDbContext db,
        string source,
        string sourceId,
        string? detectedName,
        int? catalogSourceId,
        InstalledGameScanResult result,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            var bySourceId = await db.GamePlatforms
                .AsNoTracking()
                .Where(p => p.PlatformName == source && p.PlatformGameId == sourceId)
                .Select(p => (int?)p.GameId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (bySourceId is not null)
            {
                return bySourceId;
            }
        }

        var name = detectedName?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var byName = await db.Games
            .Where(g => g.Name == name)
            .Select(g => (int?)g.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (byName is not null)
        {
            await EnsurePlatformAsync(db, byName.Value, source, sourceId, catalogSourceId, cancellationToken).ConfigureAwait(false);
            return byName;
        }

        var now = DateTime.UtcNow;
        var game = new Game
        {
            Name = name,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            game.Platforms.Add(new GamePlatform
            {
                PlatformName = source,
                PlatformGameId = sourceId,
                CatalogSourceId = catalogSourceId,
                StoreUrl = BuildStoreUrl(source, sourceId),
                LastSyncedAt = now
            });
        }

        db.Games.Add(game);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        result.CatalogGamesCreated++;

        return game.Id;
    }

    /// <summary>
    /// Oyunun ilgili platform kaydı yoksa ekler. <c>(GameId, PlatformName)</c> benzersiz indeksi
    /// ihlal edilmemesi için önce varlık kontrolü yapılır.
    /// </summary>
    private static async Task EnsurePlatformAsync(
        AppDbContext db,
        int gameId,
        string source,
        string sourceId,
        int? catalogSourceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }

        var exists = await db.GamePlatforms
            .AnyAsync(p => p.GameId == gameId && p.PlatformName == source, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        db.GamePlatforms.Add(new GamePlatform
        {
            GameId = gameId,
            PlatformName = source,
            PlatformGameId = sourceId,
            CatalogSourceId = catalogSourceId,
            StoreUrl = BuildStoreUrl(source, sourceId),
            LastSyncedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sağlayıcısı başarıyla taranan ama bu taramada görülmeyen kayıtları <c>IsInstalled = false</c>
    /// yapar. Kayıtlar <b>silinmez</b>; kullanıcı verisi ve geçmiş korunur.
    /// </summary>
    private static async Task MarkMissingAsNotInstalledAsync(
        AppDbContext db,
        IReadOnlyCollection<string> scannedProviders,
        HashSet<string> seenKeys,
        DateTime now,
        InstalledGameScanResult result,
        CancellationToken cancellationToken)
    {
        if (scannedProviders.Count == 0)
        {
            return;
        }

        var providerSet = new HashSet<string>(scannedProviders, StringComparer.OrdinalIgnoreCase);

        var installedRows = await db.InstalledGames
            .Where(i => i.IsInstalled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var changed = false;

        foreach (var row in installedRows)
        {
            if (row.InstallSource is null || !providerSet.Contains(row.InstallSource))
            {
                // Bu sağlayıcı taranmadı (veya bilinmiyor) → dokunma.
                continue;
            }

            var key = $"{row.InstallSource}|{row.InstallSourceId}|{NormalizePath(row.InstallPath)}";

            if (seenKeys.Contains(key))
            {
                continue;
            }

            row.IsInstalled = false;
            row.LastScanAt = now;
            result.MarkedNotInstalled++;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int?> GetOrCreateCatalogSourceIdAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var source = await db.CatalogSources
            .FirstOrDefaultAsync(c => c.Name == SteamCatalogProvider.ProviderNameValue, cancellationToken)
            .ConfigureAwait(false);

        if (source is not null)
        {
            return source.Id;
        }

        source = new CatalogSource
        {
            Name = SteamCatalogProvider.ProviderNameValue,
            BaseUrl = SteamCatalogProvider.BaseUrl,
            IsApiAvailable = true
        };

        db.CatalogSources.Add(source);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return source.Id;
    }

    private static string NormalizePath(string? path)
        => (path ?? string.Empty).Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? BuildStoreUrl(string source, string sourceId)
        => source.Equals(SteamCatalogProvider.ProviderNameValue, StringComparison.OrdinalIgnoreCase)
            ? $"https://store.steampowered.com/app/{sourceId}"
            : null;
}



