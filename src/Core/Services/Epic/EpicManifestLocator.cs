namespace GameLanguageExplorer.Core.Services.Epic;

using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// Epic Games Launcher manifest klasörlerini bilinen Windows konumlarından toplar.
/// Hiçbir dosya okumaz veya yazmaz.
/// </summary>
public sealed class EpicManifestLocator : IEpicManifestLocator
{
    private const string RelativeManifestPath = @"Epic\EpicGamesLauncher\Data\Manifests";

    public IReadOnlyList<string> GetCandidateManifestDirectories()
    {
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("PROGRAMDATA"),
            Environment.GetEnvironmentVariable("LOCALAPPDATA"),
            Environment.GetEnvironmentVariable("APPDATA"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        var directories = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string path;

            try
            {
                path = Path.GetFullPath(Path.Combine(root, RelativeManifestPath));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (seen.Add(path))
            {
                directories.Add(path);
            }
        }

        return directories;
    }
}
