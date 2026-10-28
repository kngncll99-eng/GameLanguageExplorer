namespace GameLanguageExplorer.Core.Entities;

public class GameProfile
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string? KnownFileExtensions { get; set; } // Comma-separated
    public string? PossibleFileLocations { get; set; } // Comma-separated
    public string? ArchiveFormat { get; set; }
    public string? LocalizationFormat { get; set; }
    public string? AvailableTools { get; set; }
    public string? FileReadabilityStatus { get; set; }
    public string? FileEditabilityStatus { get; set; }
    public string? ExportStatus { get; set; }
    public string? SourceOfInformation { get; set; }
    public string? VerificationStatus { get; set; } // Verified, Predicted, Unknown
    public DateTime? LastVerifiedAt { get; set; }
    public double? ConfidenceScore { get; set; } // 0-1

    public Game Game { get; set; } = null!;
}