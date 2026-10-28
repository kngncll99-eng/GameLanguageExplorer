namespace GameLanguageExplorer.Core.Interfaces;

using GameLanguageExplorer.Core.Entities;

public interface IGameCatalogProvider
{
    string ProviderName { get; }
    bool IsApiAvailable { get; }
    Task<IEnumerable<Game>> FetchGamesAsync(CancellationToken cancellationToken = default);
    Task<Game?> GetGameDetailsAsync(string platformGameId, CancellationToken cancellationToken = default);
    Task SyncCatalogAsync(CancellationToken cancellationToken = default);
}