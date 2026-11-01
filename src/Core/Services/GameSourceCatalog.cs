namespace GameLanguageExplorer.Core.Services;

/// <summary>Bir oyun kaynağının uygulama içindeki destek düzeyi.</summary>
public enum GameSourceSupportLevel
{
    /// <summary>Sağlayıcı uygulandı ve kullanılabilir.</summary>
    Implemented = 0,

    /// <summary>Sağlayıcı henüz uygulanmadı (yalnızca mimari hazırlık).</summary>
    Planned = 1
}

/// <summary>Bir oyun kaynağının tanımı (UI'da destek durumunu göstermek için).</summary>
public sealed record GameSourceDescriptor(
    string Key,
    string DisplayName,
    GameSourceSupportLevel SupportLevel,
    string Notes)
{
    /// <summary>UI'da gösterilecek destek etiketi.</summary>
    public string SupportText => SupportLevel == GameSourceSupportLevel.Implemented
        ? "✅ Uygulandı"
        : "⏳ Planlandı";
}

/// <summary>
/// Bilinen oyun kaynaklarının listesi. Uygulanmamış kaynaklar <see cref="GameSourceSupportLevel.Planned"/>
/// olarak işaretlenir; UI bunları "destekleniyor" gibi göstermez.
/// </summary>
public static class GameSourceCatalog
{
    public const string SteamKey = "Steam";
    public const string EpicKey = "Epic";
    public const string GogKey = "GOG";
    public const string UbisoftKey = "Ubisoft";
    public const string EaKey = "EA";
    public const string XboxKey = "Xbox";
    public const string ManualKey = "Manual";

    public static IReadOnlyList<GameSourceDescriptor> All { get; } = new List<GameSourceDescriptor>
    {
        new(SteamKey, "Steam", GameSourceSupportLevel.Implemented,
            "appmanifest_*.acf + libraryfolders.vdf (salt okunur)"),
        new(EpicKey, "Epic Games", GameSourceSupportLevel.Implemented,
            "Launcher manifestleri (.item; salt okunur)"),
        new(GogKey, "GOG", GameSourceSupportLevel.Implemented,
            "Registry + GOG klasörleri + gameInfo.igd (salt okunur)"),
        new(UbisoftKey, "Ubisoft Connect", GameSourceSupportLevel.Implemented,
            "Registry + config/cache + uplay_install manifestleri (salt okunur)"),
        new(EaKey, "EA App", GameSourceSupportLevel.Implemented,
            "Registry + EA Desktop/Origin InstallData + manifestleri (salt okunur)"),
        new(XboxKey, "Xbox / Microsoft Store", GameSourceSupportLevel.Implemented,
            "AppModel paketleri + C:\\XboxGames + AppxManifest (salt okunur)"),
        new(ManualKey, "Manuel Klasör", GameSourceSupportLevel.Implemented,
            "Kullanıcının seçtiği klasör; oyun adı kullanıcı tarafından verilir ve doğrulanmamıştır")
    };

    public static GameSourceDescriptor? Find(string? key)
        => key is null ? null : All.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Yalnızca gerçekten uygulanmış kaynaklar.</summary>
    public static IEnumerable<GameSourceDescriptor> Implemented
        => All.Where(s => s.SupportLevel == GameSourceSupportLevel.Implemented);
}
