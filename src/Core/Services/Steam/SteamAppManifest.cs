namespace GameLanguageExplorer.Core.Services.Steam;

/// <summary>
/// <c>steamapps/appmanifest_&lt;appid&gt;.acf</c> dosyasından okunan doğrulanmış kurulum bilgisi.
/// </summary>
public sealed record SteamAppManifest(
    string AppId,
    string Name,
    string InstallDir,
    long? SizeOnDisk,
    DateTime? LastUpdatedUtc,
    string ManifestPath)
{
    /// <summary>Oyunun gerçek kurulum klasörü (<c>&lt;library&gt;/steamapps/common/&lt;installdir&gt;</c>).</summary>
    public string? ResolveInstallPath(string steamAppsPath)
    {
        if (string.IsNullOrWhiteSpace(steamAppsPath) || string.IsNullOrWhiteSpace(InstallDir))
        {
            return null;
        }

        return Path.Combine(steamAppsPath, "common", InstallDir);
    }
}
