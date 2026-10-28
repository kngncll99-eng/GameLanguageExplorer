namespace GameLanguageExplorer.Core.Entities;

public class DiscoveredFile
{
    public int Id { get; set; }
    public int InstalledGameId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string DetectedFormat { get; set; } = string.Empty;
    public string DetectionConfidence { get; set; } = string.Empty; // High, Medium, Low
    public bool IsReadable { get; set; }
    public bool IsWritable { get; set; }
    public string? AdapterUsed { get; set; }
    public string? LastValidationResult { get; set; }
    public DateTime? LastValidatedAt { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    public InstalledGame InstalledGame { get; set; } = null!;
}