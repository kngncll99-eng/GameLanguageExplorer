namespace GameLanguageExplorer.Core.Services.Ea;

using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// Kayıt defteri gerektirmeyen varsayılan EA/Origin aday bulucu. Bilinen oyun
/// klasörlerini ve doğrudan alt oyun dizinlerini listeler.
/// </summary>
public sealed class DefaultEaInstallationLocator : IEaInstallationLocator
{
    private static readonly string[] GameFolderNames =
    {
        "EA Games",
        "Origin Games",
        "Electronic Arts"
    };

    public IReadOnlyList<EaInstallationCandidate> GetInstalledCandidates()
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

        var candidates = new List<EaInstallationCandidate>();
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
                    candidates.Add(new EaInstallationCandidate(null, null, child));
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
