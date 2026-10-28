namespace GameLanguageExplorer.Core.Entities;

public class LocalizationProject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int InstalledGameId { get; set; }
    public string SourceLanguage { get; set; } = "en";
    public string TargetLanguage { get; set; } = "tr";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "Active"; // Active, Completed, Paused

    public InstalledGame InstalledGame { get; set; } = null!;
    public ICollection<LocalizationEntry> Entries { get; set; } = new List<LocalizationEntry>();
}