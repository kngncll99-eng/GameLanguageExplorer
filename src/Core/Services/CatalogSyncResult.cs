namespace GameLanguageExplorer.Core.Services;

public class CatalogSyncResult
{
    public string ProviderName { get; set; } = string.Empty;
    public int TotalFetched { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime SyncCompletedAt { get; set; } = DateTime.UtcNow;
}