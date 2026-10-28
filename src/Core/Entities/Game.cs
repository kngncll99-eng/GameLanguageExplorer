using System.ComponentModel.DataAnnotations.Schema;

namespace GameLanguageExplorer.Core.Entities;

public class Game
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? GameEngine { get; set; }
    public string? CoverImageUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Sağlayıcıya özgü oyun kimliği (ör. Steam AppID). Bu alan veritabanına yazılmaz;
    /// katalog sağlayıcısından senkronizasyon servisine kimliği taşımak için kullanılır ve
    /// kalıcı karşılığı <see cref="GamePlatform.PlatformGameId"/> alanıdır.
    /// </summary>
    [NotMapped]
    public string? ExternalId { get; set; }

    public ICollection<GamePlatform> Platforms { get; set; } = new List<GamePlatform>();
    public ICollection<GameProfile> Profiles { get; set; } = new List<GameProfile>();
    public ICollection<GameVersion> Versions { get; set; } = new List<GameVersion>();
    public ICollection<InstalledGame> Installations { get; set; } = new List<InstalledGame>();
}