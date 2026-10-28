namespace GameLanguageExplorer.Core.Entities;

public class LocalizationEntry
{
    public int Id { get; set; }
    public int LocalizationProjectId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public string? TranslatedText { get; set; }
    public string? Context { get; set; }
    public string? FilePath { get; set; }
    public int LineNumber { get; set; }
    public bool IsTranslated { get; set; }
    public bool IsReviewed { get; set; }
    public bool NeedsReview { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public LocalizationProject Project { get; set; } = null!;
}