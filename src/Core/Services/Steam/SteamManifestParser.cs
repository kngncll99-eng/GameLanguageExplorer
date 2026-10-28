namespace GameLanguageExplorer.Core.Services.Steam;

using System.Globalization;

/// <summary>
/// <c>appmanifest_*.acf</c> dosyalarını okuyup doğrular. Bozuk/eksik dosyalar
/// <see cref="FormatException"/> veya <see cref="IOException"/> ile bildirilir; çağıran taraf
/// bu hataları tek tek yakalayıp taramaya devam eder.
/// </summary>
public static class SteamManifestParser
{
    /// <summary>Manifest içeriğini ayrıştırır ve zorunlu alanları doğrular.</summary>
    /// <exception cref="FormatException">Manifest geçerli VDF değilse veya zorunlu alanlar eksikse.</exception>
    public static SteamAppManifest Parse(string content, string manifestPath)
    {
        var root = VdfParser.Parse(content);

        if (!root.TryGetValue("AppState", out var appStateValue) || appStateValue is not Dictionary<string, object> appState)
        {
            throw new FormatException("Manifest içinde 'AppState' bloğu yok.");
        }

        var appId = GetString(appState, "appid");
        var name = GetString(appState, "name");
        var installDir = GetString(appState, "installdir");

        if (string.IsNullOrWhiteSpace(appId))
        {
            throw new FormatException("Manifest içinde 'appid' alanı yok.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new FormatException($"Manifest '{appId}' için 'name' alanı yok.");
        }

        if (string.IsNullOrWhiteSpace(installDir))
        {
            throw new FormatException($"Manifest '{appId}' için 'installdir' alanı yok.");
        }

        var sizeOnDisk = TryParseLong(GetString(appState, "SizeOnDisk"));
        var lastUpdated = TryParseUnixSeconds(GetString(appState, "LastUpdated"));

        return new SteamAppManifest(appId.Trim(), name.Trim(), installDir.Trim(), sizeOnDisk, lastUpdated, manifestPath);
    }

    public static SteamAppManifest ParseFile(string manifestPath)
        => Parse(File.ReadAllText(manifestPath), manifestPath);

    private static string? GetString(Dictionary<string, object> block, string key)
        => block.TryGetValue(key, out var value) && value is string text ? text : null;

    private static long? TryParseLong(string? value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTime? TryParseUnixSeconds(string? value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
