namespace GameLanguageExplorer.Core.Entities;

public class GameVersion
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string VersionNumber { get; set; } = string.Empty;
    public string? BuildNumber { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? PlatformSpecificInfo { get; set; }
    public bool IsCurrent { get; set; }

    public Game Game { get; set; } = null!;
}