namespace GameLanguageExplorer.Core.Entities;

public class FormatEvidence
{
    public int Id { get; set; }
    public int DiscoveredFileId { get; set; }
    public string EvidenceType { get; set; } = string.Empty; // Header, Extension, Content, MagicBytes
    public string EvidenceValue { get; set; } = string.Empty;
    public double Confidence { get; set; } // 0-1
    public string? MatchedPattern { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DiscoveredFile DiscoveredFile { get; set; } = null!;
}