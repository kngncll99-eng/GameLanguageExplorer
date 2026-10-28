namespace GameLanguageExplorer.Core.Entities;

public class GamePlatform
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int? CatalogSourceId { get; set; } // Kaynak kaynağı (Steam, Epic vb.)
    public string PlatformName { get; set; } = string.Empty; // Steam, Epic, Ubisoft, GOG, etc.
    public string PlatformGameId { get; set; } = string.Empty; // Platform-specific game ID
    public string? StoreUrl { get; set; }
    public DateTime? LastSyncedAt { get; set; }

    public Game Game { get; set; } = null!;
    public CatalogSource? CatalogSource { get; set; }
}