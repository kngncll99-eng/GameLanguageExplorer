namespace GameLanguageExplorer.Core.Services.Gog;

using System.Text.Json;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.InstalledGames;
using Microsoft.Extensions.Logging;

/// <summary>
/// GOG kurulu oyunlarını kayıt defteri/klasör adaylarından ve yerel <c>gameInfo.igd</c>
/// metadata dosyalarından <b>salt okunur</b> biçimde tespit eder. Dosya ve kayıt defteri
/// değerleri değiştirilmez.
/// </summary>
public sealed class GogInstalledGameProvider : InstalledGameProviderBase
{
    public const string ProviderNameValue = "GOG";

    private readonly IGogInstallationLocator _locator;

    public GogInstalledGameProvider(
        IGogInstallationLocator? locator = null,
        ILogger<GogInstalledGameProvider>? logger = null)
        : base(ProviderNameValue, "GOG", logger)
    {
        _locator = locator ?? new DefaultGogInstallationLocator();
    }

    protected override string UnavailableReason =>
        "GOG kurulumu bulunamadı (registry veya bilinen GOG klasörleri yok).";

    protected override ProviderCandidateSet LoadCandidates(CancellationToken cancellationToken)
    {
        var candidates = _locator.GetInstalledCandidates();

        if (candidates.Count == 0)
        {
            return new ProviderCandidateSet(Array.Empty<ProviderInstallCandidate>(), SourceAvailable: false);
        }

        var results = new List<ProviderInstallCandidate>();
        var partial = false;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetFullPath(candidate.InstallPath, out var installPath))
            {
                partial = true;
                Warn($"GOG kurulum yolu çözülemedi '{candidate.InstallPath}'.");
                continue;
            }

            var folderExists = SafeDirectoryExists(installPath);
            var info = folderExists ? TryReadGameInfo(installPath) : null;

            results.Add(new ProviderInstallCandidate(
                installPath,
                FirstNonEmpty(candidate.GameId, info?.GameId),
                FirstNonEmpty(candidate.GameName, info?.GameName, Path.GetFileName(NormalizePath(installPath))),
                FirstNonEmpty(candidate.ExecutableName, info?.ExecutableName)));
        }

        return new ProviderCandidateSet(results, SourceAvailable: true, IsPartial: partial);
    }

    /// <summary>
    /// Kurulum klasöründeki <c>gameInfo.igd</c> veya <c>goggame-*.info</c> dosyasını
    /// JSON ya da anahtar=değer biçiminde okur. Okunamayan metadata sessizce yok sayılmaz.
    /// </summary>
    private GogGameInfo? TryReadGameInfo(string installPath)
    {
        string? metadataPath = null;

        try
        {
            var gameInfo = Path.Combine(installPath, "gameInfo.igd");

            if (File.Exists(gameInfo))
            {
                metadataPath = gameInfo;
            }
            else
            {
                metadataPath = Directory
                    .GetFiles(installPath, "goggame-*.info", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Warn($"GOG metadata dosyası aranamadı '{installPath}': {exception.Message}");
            return null;
        }

        if (metadataPath is null)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(metadataPath);

            if (info.Length > 2L * 1024 * 1024)
            {
                Warn($"GOG metadata dosyası çok büyük, atlandı '{metadataPath}'.");
                return null;
            }

            var text = File.ReadAllText(metadataPath);

            return TryParseJsonInfo(text) ?? TryParseKeyValueInfo(text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Warn($"GOG metadata dosyası okunamadı '{metadataPath}': {exception.Message}");
            return null;
        }
        catch (Exception exception)
        {
            Warn($"GOG metadata dosyası işlenemedi '{metadataPath}': {exception.Message}");
            return null;
        }
    }

    private static GogGameInfo? TryParseJsonInfo(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = document.RootElement;
            var gameId = FirstNonEmpty(
                GetString(root, "gameId"),
                GetString(root, "gameID"),
                GetString(root, "id"),
                GetString(root, "rootGameID"));
            var gameName = FirstNonEmpty(
                GetString(root, "gameName"),
                GetString(root, "name"),
                GetString(root, "title"),
                GetString(root, "displayName"));
            var executable = FirstNonEmpty(
                GetString(root, "exe"),
                GetString(root, "executable"),
                GetString(root, "launchCommand"));

            return gameId is null && gameName is null && executable is null
                ? null
                : new GogGameInfo(gameId, gameName, executable);
        }
        catch (JsonException)
        {
            return null;
        }
    }


    private static GogGameInfo? TryParseKeyValueInfo(string text)
    {
        string? gameId = null;
        string? gameName = null;
        string? executable = null;

        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            var separator = line.IndexOf('=');

            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');

            if (key.Equals("gameId", StringComparison.OrdinalIgnoreCase)
                || key.Equals("gameID", StringComparison.OrdinalIgnoreCase)
                || key.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                gameId ??= value;
            }
            else if (key.Equals("gameName", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("name", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                gameName ??= value;
            }
            else if (key.Equals("exe", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("executable", StringComparison.OrdinalIgnoreCase))
            {
                executable ??= value;
            }
        }

        return gameId is null && gameName is null && executable is null
            ? null
            : new GogGameInfo(gameId, gameName, executable);
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : null;
            }
        }

        return null;
    }

    private sealed record GogGameInfo(
        string? GameId,
        string? GameName,
        string? ExecutableName);
}

