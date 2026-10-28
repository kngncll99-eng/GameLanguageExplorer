using System.ComponentModel.DataAnnotations.Schema;

namespace GameLanguageExplorer.Core.Entities;

public class InstalledGame
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string InstallPath { get; set; } = string.Empty;
    public string? ExecutableName { get; set; }
    public long? SizeInBytes { get; set; }
    public DateTime? LastPlayedAt { get; set; }
    public DateTime? InstalledAt { get; set; }
    public DateTime? LastScanAt { get; set; }
    public bool IsInstalled { get; set; } = true;
    public string? InstallSource { get; set; } // Steam, Epic, Manual, etc.
    public string? InstallSourceId { get; set; } // Platform-specific install ID

    /// <summary>
    /// Tespit sırasında manifestten okunan oyun adı. Veritabanına yazılmaz; kalıcı karşılığı
    /// <see cref="Game.Name"/> alanıdır. Tarayıcı bu adı kullanarak katalog kaydını eşleştirir/oluşturur.
    /// </summary>
    [NotMapped]
    public string? DetectedGameName { get; set; }

    public Game Game { get; set; } = null!;
    public ICollection<DiscoveredFile> DiscoveredFiles { get; set; } = new List<DiscoveredFile>();
}