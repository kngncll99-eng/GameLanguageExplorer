namespace GameLanguageExplorer.Core.Entities;

public class TranslationMemoryEntry
{
    public int Id { get; set; }
    public string SourceText { get; set; } = string.Empty;
    public string TargetText { get; set; } = string.Empty;
    public int UsageCount { get; set; } = 1;
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}