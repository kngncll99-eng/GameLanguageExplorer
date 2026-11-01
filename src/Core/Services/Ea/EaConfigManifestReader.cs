namespace GameLanguageExplorer.Core.Services.Ea;

using System.Text.Json;
using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// EA Desktop/Origin config, cache ve InstallData klasörlerindeki JSON/metin
/// dosyalarından oyun adayları çıkarır. Yalnızca okuma yapar.
/// </summary>
public static class EaConfigManifestReader
{
    private const long MaxConfigBytes = 2L * 1024 * 1024;

    public static IReadOnlyList<EaInstallationCandidate> ReadFromDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<EaInstallationCandidate>();
        }

        var candidates = new List<EaInstallationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
            {
                ReadFile(file, candidates, seen);
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.dat", SearchOption.AllDirectories))
            {
                ReadFile(file, candidates, seen);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return candidates;
        }

        return candidates;
    }

    private static void ReadFile(
        string file,
        ICollection<EaInstallationCandidate> candidates,
        ISet<string> seen)
    {
        try
        {
            if (new FileInfo(file).Length > MaxConfigBytes)
            {
                return;
            }

            var text = File.ReadAllText(file);

            if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var document = JsonDocument.Parse(
                    text,
                    new JsonDocumentOptions { MaxDepth = 12 });

                ReadObjects(document.RootElement, candidates, seen, 0);
            }
            else
            {
                ReadKeyValue(text, candidates, seen);
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or JsonException
                                          or ArgumentException)
        {
            // Bir config dosyası okunamazsa diğerleri taranmaya devam eder.
        }
    }

    private static void ReadObjects(
        JsonElement element,
        ICollection<EaInstallationCandidate> candidates,
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

        AddCandidate(
            GetString(element, "contentId", "contentID", "content_id", "gameId", "id"),
            GetString(element, "offerId", "offerID"),
            GetString(element, "displayName", "gameName", "name", "title"),
            GetString(element, "installPath", "installDir", "installLocation", "path"),
            GetString(element, "exe", "executable", "launchCommand"),
            candidates,
            seen);

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                ReadObjects(property.Value, candidates, seen, depth + 1);
            }
        }
    }

    private static void ReadKeyValue(
        string text,
        ICollection<EaInstallationCandidate> candidates,
        ISet<string> seen)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            var separator = line.IndexOfAny(new[] { ':', '=' });

            if (separator > 0)
            {
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"', '\'');
            }
        }

        AddCandidate(
            First(values, "contentId", "contentID", "content_id", "gameId", "id"),
            First(values, "offerId", "offerID"),
            First(values, "displayName", "gameName", "name", "title"),
            First(values, "installPath", "installDir", "installLocation", "path"),
            First(values, "exe", "executable", "launchCommand"),
            candidates,
            seen);
    }

    private static void AddCandidate(
        string? contentId,
        string? offerId,
        string? gameName,
        string? installPath,
        string? executable,
        ICollection<EaInstallationCandidate> candidates,
        ISet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(installPath)
            || !Path.IsPathFullyQualified(installPath.Trim())
            || (string.IsNullOrWhiteSpace(contentId)
                && string.IsNullOrWhiteSpace(offerId)
                && string.IsNullOrWhiteSpace(gameName))
            || !seen.Add(NormalizePath(installPath)))
        {
            return;
        }

        candidates.Add(new EaInstallationCandidate(
            contentId,
            gameName,
            installPath.Trim(),
            executable,
            offerId));
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static string? First(IReadOnlyDictionary<string, string> values, params string[] names)
        => names.Select(name => values.TryGetValue(name, out var value) ? value : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string NormalizePath(string path)
        => path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

