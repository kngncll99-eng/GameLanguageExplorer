namespace GameLanguageExplorer.Core.Services;

using GameLanguageExplorer.Core.Entities;

public interface ICatalogSyncService
{
    /// <summary>Sağlayıcıdan tam katalog çeker ve veritabanına yazar.</summary>
    Task<CatalogSyncResult> SyncProviderAsync(string providerName, CancellationToken cancellationToken = default);

    /// <summary>Önceden getirilmiş oyun listesini (ör. Steam arama sonuçları) veritabanına yazar.</summary>
    Task<CatalogSyncResult> ImportGamesAsync(
        string providerName,
        IReadOnlyList<Game> games,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Game>> SearchGamesAsync(string? searchText, int take = 100, CancellationToken cancellationToken = default);

    Task<Game?> GetGameByIdAsync(int gameId, CancellationToken cancellationToken = default);

    /// <summary>Veritabanındaki toplam oyun sayısı.</summary>
    Task<int> CountGamesAsync(CancellationToken cancellationToken = default);
}