namespace GameLanguageExplorer.Core.Entities;

public class CatalogSource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty; // Steam, Epic Games Store, etc.
    public string? BaseUrl { get; set; }
    public bool IsApiAvailable { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public string? SyncError { get; set; }

    public ICollection<GamePlatform> Platforms { get; set; } = new List<GamePlatform>();
}