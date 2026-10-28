namespace GameLanguageExplorer.Core.Entities;

public class GlossaryTerm
{
    public int Id { get; set; }
    public string SourceTerm { get; set; } = string.Empty;
    public string TargetTerm { get; set; } = string.Empty; // Turkish translation
    public string? Context { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}