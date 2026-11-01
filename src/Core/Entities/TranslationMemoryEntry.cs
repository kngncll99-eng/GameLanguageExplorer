namespace GameLanguageExplorer.Core.Entities;

public class TranslationMemoryEntry
{
    public int Id { get; set; }
    public string SourceText { get; set; } = string.Empty;
    public string TargetText { get; set; } = string.Empty;

    /// <summary>Kaynak metnin dili (ör. English).</summary>
    public string SourceLanguage { get; set; } = string.Empty;

    /// <summary>Hedef çeviri dili (ör. Turkish).</summary>
    public string TargetLanguage { get; set; } = string.Empty;

    public int UsageCount { get; set; } = 1;
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}