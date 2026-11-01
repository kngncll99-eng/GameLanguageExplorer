namespace GameLanguageExplorer.Core.Services.Ubisoft;

using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// Kayıt defteri gerektirmeyen varsayılan Ubisoft aday bulucu. Bilinen Ubisoft oyun
/// klasörlerini ve doğrudan alt oyun dizinlerini listeler.
/// </summary>
public sealed class DefaultUbisoftInstallationLocator : IUbisoftInstallationLocator
{
    private static readonly string[] GameFolderNames =
    {
        @"Ubisoft\Ubisoft Game Launcher\games",
        @"Ubisoft Game Launcher\games",
        @"Ubisoft\games"
    };

    public IReadOnlyList<UbisoftInstallationCandidate> GetInstalledCandidates()
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

            if (!string.IsNullOrWhiteSpace(basePath))
            {
                roots.AddRange(GameFolderNames.Select(folder => Path.Combine(basePath, folder)));
            }
        }

        var candidates = new List<UbisoftInstallationCandidate>();
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
                    candidates.Add(new UbisoftInstallationCandidate(null, null, child));
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
