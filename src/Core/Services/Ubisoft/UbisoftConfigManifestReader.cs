namespace GameLanguageExplorer.Core.Services.Ubisoft;

using System.Text.Json;
using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// Ubisoft launcher config/cache klasörlerindeki JSON dosyalarından oyun adayları çıkarır.
/// Dosyalar yalnızca okunur; bilinmeyen/bozuk dosyalar atlanır.
/// </summary>
public static class UbisoftConfigManifestReader
{
    private const long MaxConfigBytes = 2L * 1024 * 1024;

    public static IReadOnlyList<UbisoftInstallationCandidate> ReadFromDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<UbisoftInstallationCandidate>();
        }

        var candidates = new List<UbisoftInstallationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (new FileInfo(file).Length > MaxConfigBytes)
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(
                        File.ReadAllText(file),
                        new JsonDocumentOptions { MaxDepth = 12 });

                    ReadObjects(document.RootElement, candidates, seen, 0);
                }
                catch (Exception exception) when (exception is IOException
                                                  or UnauthorizedAccessException
                                                  or JsonException
                                                  or ArgumentException)
                {
                    // Bir config dosyası okunamazsa diğerleri taranmaya devam eder.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return candidates;
        }

        return candidates;
    }

    private static void ReadObjects(
        JsonElement element,
        ICollection<UbisoftInstallationCandidate> candidates,
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

        var gameId = GetString(element, "game_code", "gameId", "gameID", "id", "uplay_id");
        var gameName = GetString(element, "gameName", "game_name", "name", "title");
        var installPath = GetString(element, "installDir", "install_dir", "installLocation", "path");

        if (!string.IsNullOrWhiteSpace(installPath)
            && Path.IsPathFullyQualified(installPath.Trim())
            && (!string.IsNullOrWhiteSpace(gameId) || !string.IsNullOrWhiteSpace(gameName))
            && seen.Add(NormalizePath(installPath)))
        {
            candidates.Add(new UbisoftInstallationCandidate(
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

            return property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : null;
        }

        return null;
    }

    private static string NormalizePath(string path)
        => path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
