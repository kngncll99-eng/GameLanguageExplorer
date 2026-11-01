namespace GameLanguageExplorer.Core.Services.Gog;

using System.Text.Json;
using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// GOG Galaxy storage klasöründeki JSON tabanlı config dosyalarından oyun adayları
/// çıkarır. Bilinmeyen/bozuk dosyalar sessizce atlanır; dosyalar yalnızca okunur.
/// </summary>
public static class GogStorageManifestReader
{
    private const long MaxConfigBytes = 2L * 1024 * 1024;

    public static IReadOnlyList<GogInstallationCandidate> ReadFromStorage(string storageDirectory)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory) || !Directory.Exists(storageDirectory))
        {
            return Array.Empty<GogInstallationCandidate>();
        }

        var candidates = new List<GogInstallationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> files;

        try
        {
            files = Directory.EnumerateFiles(storageDirectory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return candidates;
        }

        foreach (var file in files)
        {
            try
            {
                if (new FileInfo(file).Length > MaxConfigBytes)
                {
                    continue;
                }

                using var document = JsonDocument.Parse(
                    File.ReadAllText(file),
                    new JsonDocumentOptions { MaxDepth = 12 });

                ReadObjects(document.RootElement, candidates, seen, depth: 0);
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or JsonException
                                              or ArgumentException)
            {
                // Bir config dosyası okunamazsa diğer dosyalar taranmaya devam eder.
            }
        }

        return candidates;
    }

    private static void ReadObjects(
        JsonElement element,
        ICollection<GogInstallationCandidate> candidates,
        ISet<string> seen,
        int depth)
    {
        if (depth > 8)
        {
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ReadObjects(item, candidates, seen, depth + 1);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var gameId = GetString(element, "gameId", "gameID", "id", "rootGameID");
        var gameName = GetString(element, "gameName", "name", "title", "displayName");
        var installPath = GetString(element, "installPath", "installLocation", "path");

        if (!string.IsNullOrWhiteSpace(installPath)
            && Path.IsPathFullyQualified(installPath.Trim())
            && (!string.IsNullOrWhiteSpace(gameId) || !string.IsNullOrWhiteSpace(gameName))
            && seen.Add(NormalizePath(installPath)))
        {
            candidates.Add(new GogInstallationCandidate(
                gameId,
                gameName,
                installPath.Trim(),
                GetString(element, "exe", "executable", "launchCommand")));
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                ReadObjects(property.Value, candidates, seen, depth + 1);
            }
        }
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static string NormalizePath(string path)
        => path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
