namespace GameLanguageExplorer.Data.Services;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public class CatalogSyncService : ICatalogSyncService
{
    /// <summary>
    /// Kaç kayıtta bir <c>SaveChanges</c> çağrılacağını belirler. Her oyun için ayrı transaction
    /// açmak (Steam kataloğu ~250.000 kayıt) uygulamayı pratikte kilitler; bu yüzden yazma
    /// işlemleri toplu hale getirilmiştir.
    /// </summary>
    private const int SaveBatchSize = 1000;

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IEnumerable<IGameCatalogProvider> _providers;
    private readonly ILogger<CatalogSyncService> _logger;

    public CatalogSyncService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IEnumerable<IGameCatalogProvider> providers,
        ILogger<CatalogSyncService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CatalogSyncResult> SyncProviderAsync(
        string providerName,
        CancellationToken cancellationToken = default)
    {
        var provider = _providers.FirstOrDefault(p =>
            p.ProviderName.Equals(providerName, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            return Failure(providerName, $"Sağlayıcı bulunamadı: {providerName}");
        }

        _logger.LogInformation("Katalog senkronizasyonu başlatılıyor: {Provider}", providerName);

        List<Game> games;

        try
        {
            games = (await provider.FetchGamesAsync(cancellationToken).ConfigureAwait(false))?.ToList() ?? new List<Game>();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Senkronizasyon iptal edildi: {Provider}", providerName);
            return Cancelled(providerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Katalog indirilemedi: {Provider}", providerName);
            await TryRecordSyncErrorAsync(providerName, ex.Message).ConfigureAwait(false);
            return Failure(providerName, ex.Message);
        }

        _logger.LogInformation("{Provider} sağlayıcısından {Count} oyun alındı.", providerName, games.Count);

        return await UpsertGamesAsync(providerName, provider.IsApiAvailable, games, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CatalogSyncResult> ImportGamesAsync(
        string providerName,
        IReadOnlyList<Game> games,
        CancellationToken cancellationToken = default)
    {
        if (games is null || games.Count == 0)
        {
            _logger.LogWarning("{Provider} için içe aktarılacak oyun yok.", providerName);

            return new CatalogSyncResult
            {
                ProviderName = providerName,
                TotalFetched = 0,
                Success = true,
                SyncCompletedAt = DateTime.UtcNow
            };
        }

        _logger.LogInformation("{Provider} için {Count} oyun içe aktarılıyor.", providerName, games.Count);

        return await UpsertGamesAsync(providerName, isApiAvailable: true, games, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Verilen oyun listesini veritabanına ekler/günceller. Ağ erişimi yapmaz.</summary>
    private async Task<CatalogSyncResult> UpsertGamesAsync(
        string providerName,
        bool isApiAvailable,
        IReadOnlyList<Game> games,
        CancellationToken cancellationToken)
    {
        try
        {
            if (games.Count == 0)
            {
                return new CatalogSyncResult
                {
                    ProviderName = providerName,
                    TotalFetched = 0,
                    Success = true,
                    SyncCompletedAt = DateTime.UtcNow
                };
            }

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var catalogSource = await GetOrCreateCatalogSourceAsync(db, providerName, isApiAvailable, cancellationToken)
                .ConfigureAwait(false);

            var now = DateTime.UtcNow;
            int added = 0;
            int updated = 0;
            int unchanged = 0;
            int pendingChanges = 0;

            // Aynı fetch içinde tekrar eden kayıtların iki kez eklenmesini engeller
            // (toplu SaveChanges kullanıldığı için henüz veritabanında görünmezler).
            var createdByName = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
            var createdByExternalId = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);

            foreach (var gameDto in games)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = gameDto.Name?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var externalId = string.IsNullOrWhiteSpace(gameDto.ExternalId) ? null : gameDto.ExternalId.Trim();

                var existingGame = ResolveCreated(createdByExternalId, createdByName, externalId, name)
                    ?? await FindExistingGameAsync(db, providerName, externalId, name, cancellationToken).ConfigureAwait(false);

                if (existingGame is null)
                {
                    var newGame = CreateGame(gameDto, name, now);
                    newGame.Platforms.Add(CreatePlatform(providerName, catalogSource, externalId, now));
                    db.Games.Add(newGame);

                    createdByName[name] = newGame;
                    if (externalId is not null)
                    {
                        createdByExternalId[externalId] = newGame;
                    }

                    added++;
                }
                else
                {
                    if (ApplyUpdates(existingGame, gameDto, now))
                    {
                        updated++;
                    }
                    else
                    {
                        unchanged++;
                    }

                    UpsertPlatform(existingGame, providerName, catalogSource, externalId, now);
                }

                if (++pendingChanges >= SaveBatchSize)
                {
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    pendingChanges = 0;
                }
            }

            catalogSource.LastSyncAt = DateTime.UtcNow;
            catalogSource.SyncError = null;
            catalogSource.IsApiAvailable = isApiAvailable;

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Senkronizasyon tamamlandı: {Provider}, Toplam: {Total}, Eklenen: {Added}, Güncellenen: {Updated}, Değişmeyen: {Unchanged}",
                providerName, games.Count, added, updated, unchanged);

            return new CatalogSyncResult
            {
                ProviderName = providerName,
                TotalFetched = games.Count,
                Added = added,
                Updated = updated,
                Unchanged = unchanged,
                Success = true,
                SyncCompletedAt = DateTime.UtcNow
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Senkronizasyon iptal edildi: {Provider}", providerName);
            return Cancelled(providerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Senkronizasyon hatası: {Provider}", providerName);
            await TryRecordSyncErrorAsync(providerName, ex.Message).ConfigureAwait(false);
            return Failure(providerName, ex.Message);
        }
    }

    public async Task<IEnumerable<Game>> SearchGamesAsync(
        string? searchText,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = db.Games
            .AsNoTracking()
            .Include(g => g.Platforms)
            .ThenInclude(p => p.CatalogSource)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            // Contains() SQLite'ta büyük/küçük harfe duyarlıdır; LIKE (ASCII için) duyarsızdır.
            // Kullanıcı girdisindeki joker karakterler kaçırılır.
            var pattern = "%" + EscapeLikePattern(searchText.Trim()) + "%";
            query = query.Where(g => EF.Functions.Like(g.Name, pattern, "\\"));
        }

        return await query
            .OrderBy(g => g.Name)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Game?> GetGameByIdAsync(int gameId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Games
            .AsNoTracking()
            .Include(g => g.Platforms)
            .ThenInclude(p => p.CatalogSource)
            .FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountGamesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Games.CountAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<CatalogSource> GetOrCreateCatalogSourceAsync(
        AppDbContext db,
        string providerName,
        bool isApiAvailable,
        CancellationToken cancellationToken)
    {
        var catalogSource = await db.CatalogSources
            .FirstOrDefaultAsync(c => c.Name.ToLower() == providerName.ToLower(), cancellationToken)
            .ConfigureAwait(false);

        if (catalogSource is not null)
        {
            return catalogSource;
        }

        catalogSource = new CatalogSource
        {
            Name = providerName,
            BaseUrl = SteamCatalogProvider.BaseUrl,
            IsApiAvailable = isApiAvailable,
            LastSyncAt = DateTime.UtcNow
        };

        db.CatalogSources.Add(catalogSource);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return catalogSource;
    }

    private static CatalogSyncResult Failure(string providerName, string message) => new()
    {
        ProviderName = providerName,
        Success = false,
        ErrorMessage = message,
        SyncCompletedAt = DateTime.UtcNow
    };

    /// <summary>LIKE deseninde özel anlam taşıyan karakterleri kaçırır.</summary>
    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    private static CatalogSyncResult Cancelled(string providerName) => new()
    {
        ProviderName = providerName,
        Success = false,
        ErrorMessage = "İşlem iptal edildi",
        SyncCompletedAt = DateTime.UtcNow
    };

    private static Game? ResolveCreated(
        IReadOnlyDictionary<string, Game> createdByExternalId,
        IReadOnlyDictionary<string, Game> createdByName,
        string? externalId,
        string name)
    {
        if (externalId is not null && createdByExternalId.TryGetValue(externalId, out var byExternalId))
        {
            return byExternalId;
        }

        return createdByName.TryGetValue(name, out var byName) ? byName : null;
    }

    /// <summary>
    /// Önce sağlayıcıya özgü oyun kimliğiyle (ör. Steam AppID) eşleşme arar; bulamazsa
    /// oyun adına göre geri düşer. Kimlikle eşleşme, aynı adı taşıyan farklı oyunların
    /// yanlışlıkla birleştirilmesini önler.
    /// </summary>
    private static async Task<Game?> FindExistingGameAsync(
        AppDbContext db,
        string providerName,
        string? externalId,
        string name,
        CancellationToken cancellationToken)
    {
        if (externalId is not null)
        {
            var byExternalId = await db.Games
                .Include(g => g.Platforms)
                .FirstOrDefaultAsync(
                    g => g.Platforms.Any(p => p.PlatformName == providerName && p.PlatformGameId == externalId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (byExternalId is not null)
            {
                return byExternalId;
            }
        }

        return await db.Games
            .Include(g => g.Platforms)
            .FirstOrDefaultAsync(g => g.Name == name, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Game CreateGame(Game source, string name, DateTime now) => new()
    {
        Name = name,
        Description = source.Description,
        Developer = source.Developer,
        Publisher = source.Publisher,
        ReleaseDate = source.ReleaseDate,
        GameEngine = source.GameEngine,
        CoverImageUrl = source.CoverImageUrl,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static bool ApplyUpdates(Game target, Game source, DateTime now)
    {
        bool changed = false;

        if (target.Description != source.Description) { target.Description = source.Description; changed = true; }
        if (target.Developer != source.Developer) { target.Developer = source.Developer; changed = true; }
        if (target.Publisher != source.Publisher) { target.Publisher = source.Publisher; changed = true; }
        if (target.ReleaseDate != source.ReleaseDate) { target.ReleaseDate = source.ReleaseDate; changed = true; }
        if (target.GameEngine != source.GameEngine) { target.GameEngine = source.GameEngine; changed = true; }
        if (target.CoverImageUrl != source.CoverImageUrl) { target.CoverImageUrl = source.CoverImageUrl; changed = true; }

        if (changed)
        {
            target.UpdatedAt = now;
        }

        return changed;
    }

    private static void UpsertPlatform(
        Game game,
        string providerName,
        CatalogSource catalogSource,
        string? externalId,
        DateTime now)
    {
        var platform = game.Platforms.FirstOrDefault(p =>
            p.PlatformName == providerName &&
            (externalId is null || p.PlatformGameId == externalId));

        if (platform is null)
        {
            game.Platforms.Add(CreatePlatform(providerName, catalogSource, externalId, now));
            return;
        }

        platform.LastSyncedAt = now;

        if (externalId is not null && platform.PlatformGameId != externalId)
        {
            platform.PlatformGameId = externalId;
        }

        platform.StoreUrl ??= BuildStoreUrl(providerName, externalId);
    }

    private static GamePlatform CreatePlatform(
        string providerName,
        CatalogSource catalogSource,
        string? externalId,
        DateTime now) => new()
        {
            CatalogSourceId = catalogSource.Id,
            PlatformName = providerName,
            PlatformGameId = externalId ?? string.Empty,
            StoreUrl = BuildStoreUrl(providerName, externalId),
            LastSyncedAt = now
        };

    /// <summary>
    /// Mağaza bağlantısını yalnızca gerçek platform kimliği (ör. Steam AppID) biliniyorsa üretir.
    /// Yerel veritabanı Id'si mağaza URL'si için kullanılmaz.
    /// </summary>
    private static string? BuildStoreUrl(string providerName, string? externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        return providerName.Equals(SteamCatalogProvider.ProviderNameValue, StringComparison.OrdinalIgnoreCase)
            ? $"https://store.steampowered.com/app/{externalId}"
            : null;
    }

    /// <summary>Hata bilgisini kaynağa yazmayı dener; bu sırada oluşan hatalar asıl hatayı maskelemez.</summary>
    private async Task TryRecordSyncErrorAsync(string providerName, string errorMessage)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);

            var catalogSource = await db.CatalogSources
                .FirstOrDefaultAsync(c => c.Name.ToLower() == providerName.ToLower())
                .ConfigureAwait(false);

            if (catalogSource is null)
            {
                return;
            }

            catalogSource.SyncError = errorMessage;
            catalogSource.LastSyncAt = DateTime.UtcNow;

            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception persistenceError)
        {
            _logger.LogWarning(persistenceError, "Senkronizasyon hatası kaydedilemedi: {Provider}", providerName);
        }
    }
}






