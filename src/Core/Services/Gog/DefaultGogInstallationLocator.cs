namespace GameLanguageExplorer.Core.Services.Gog;

using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// Kayıt defteri gerektirmeyen varsayılan GOG aday bulucu: yaygın GOG oyun klasörlerini
/// ve doğrudan alt oyun dizinlerini listeler. Asıl zenginleştirme <c>gameInfo.igd</c>
/// dosyasından provider tarafından yapılır.
/// </summary>
public sealed class DefaultGogInstallationLocator : IGogInstallationLocator
{
    private static readonly string[] GameFolderNames =
    {
        @"GOG Galaxy\Games",
        @"GOG Games",
        @"GOG.com\Galaxy\Games"
    };

    public IReadOnlyList<GogInstallationCandidate> GetInstalledCandidates()
    {
        var roots = new List<string>();

        foreach (var specialFolder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86
                 })
        {
            string basePath;

            try
            {
                basePath = Environment.GetFolderPath(specialFolder);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or PlatformNotSupportedException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(basePath))
            {
                continue;
            }

            roots.AddRange(GameFolderNames.Select(folder => Path.Combine(basePath, folder)));
        }

        var candidates = new List<GogInstallationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!SafeDirectoryExists(root))
            {
                continue;
            }

            string[] children;

            try
            {
                children = Directory.GetDirectories(root);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (seen.Add(child))
                {
                    candidates.Add(new GogInstallationCandidate(null, null, child));
                }
            }
        }

        return candidates;
    }

    private static bool SafeDirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
