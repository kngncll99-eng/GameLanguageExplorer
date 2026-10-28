namespace GameLanguageExplorer.Core.Entities;

public class BackupRecord
{
    public int Id { get; set; }
    public string OriginalFilePath { get; set; } = string.Empty;
    public string BackupFilePath { get; set; } = string.Empty;
    public string OriginalSha256 { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRestored { get; set; }
    public DateTime? RestoredAt { get; set; }
}