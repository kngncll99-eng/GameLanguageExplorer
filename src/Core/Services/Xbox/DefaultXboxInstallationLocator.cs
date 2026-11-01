namespace GameLanguageExplorer.Core.Services.Xbox;

using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// Kayıt defteri gerektirmeyen varsayılan Xbox aday bulucu: Xbox App oyunlarının
/// yaygın kurulum kökü olan <c>C:\XboxGames</c> altındaki oyun klasörlerini listeler.
/// </summary>
public sealed class DefaultXboxInstallationLocator : IXboxInstallationLocator
{
    private const string XboxGamesRoot = @"C:\XboxGames";

    public IReadOnlyList<XboxInstallationCandidate> GetInstalledCandidates()
    {
        var candidates = new List<XboxInstallationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!SafeDirectoryExists(XboxGamesRoot))
        {
            return candidates;
        }

        string[] children;

        try
        {
            children = Directory.GetDirectories(XboxGamesRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return candidates;
        }

        foreach (var child in children)
        {
            var installPath = ResolveContentPath(child);

            if (seen.Add(installPath))
            {
                candidates.Add(new XboxInstallationCandidate(null, null, installPath));
            }
        }

        return candidates;
    }

    private static string ResolveContentPath(string gameRoot)
    {
        var content = Path.Combine(gameRoot, "Content");
        return SafeDirectoryExists(content) ? content : gameRoot;
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
