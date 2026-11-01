namespace GameLanguageExplorer.Core.Services.Ubisoft;

using System.Text.Json;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.InstalledGames;
using Microsoft.Extensions.Logging;

/// <summary>
/// Ubisoft Connect kurulu oyunlarını registry/klasör adaylarından ve oyun içi
/// <c>uplay_install.manifest</c> / benzeri metadata dosyalarından <b>salt okunur</b>
/// biçimde tespit eder.
/// </summary>
public sealed class UbisoftInstalledGameProvider : InstalledGameProviderBase
{
    public const string ProviderNameValue = "Ubisoft";

    private readonly IUbisoftInstallationLocator _locator;

    public UbisoftInstalledGameProvider(
        IUbisoftInstallationLocator? locator = null,
        ILogger<UbisoftInstalledGameProvider>? logger = null)
        : base(ProviderNameValue, "Ubisoft Connect", logger)
    {
        _locator = locator ?? new DefaultUbisoftInstallationLocator();
    }

    protected override string UnavailableReason =>
        "Ubisoft Connect kurulumu bulunamadı (registry veya bilinen Ubisoft klasörleri yok).";

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
                Warn($"Ubisoft kurulum yolu çözülemedi '{candidate.InstallPath}'.");
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
    /// Oyun klasöründeki bilinen Ubisoft metadata dosyalarını JSON veya basit
    /// <c>key: value</c> / <c>key=value</c> biçiminde okur.
    /// </summary>
    private UbisoftGameInfo? TryReadGameInfo(string installPath)
    {
        string? metadataPath = null;

        try
        {
            foreach (var fileName in new[]
                     {
                         "uplay_install.manifest",
                         "uplay_install.state",
                         "game_info.json",
                         "manifest.json"
                     })
            {
                var path = Path.Combine(installPath, fileName);

                if (File.Exists(path))
                {
                    metadataPath = path;
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Warn($"Ubisoft metadata dosyası aranamadı '{installPath}': {exception.Message}");
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
                Warn($"Ubisoft metadata dosyası çok büyük, atlandı '{metadataPath}'.");
                return null;
            }

            var text = File.ReadAllText(metadataPath);

            return TryParseJsonInfo(text) ?? TryParseKeyValueInfo(text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Warn($"Ubisoft metadata dosyası okunamadı '{metadataPath}': {exception.Message}");
            return null;
        }
        catch (Exception exception)
        {
            Warn($"Ubisoft metadata dosyası işlenemedi '{metadataPath}': {exception.Message}");
            return null;
        }
    }

    private static UbisoftGameInfo? TryParseJsonInfo(string text)
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
                GetString(root, "game_code"),
                GetString(root, "gameId"),
                GetString(root, "gameID"),
                GetString(root, "id"),
                GetString(root, "uplay_id"));
            var gameName = FirstNonEmpty(
                GetString(root, "gameName"),
                GetString(root, "game_name"),
                GetString(root, "name"),
                GetString(root, "title"));
            var executable = FirstNonEmpty(
                GetString(root, "exe"),
                GetString(root, "executable"),
                GetString(root, "launchCommand"));

            return gameId is null && gameName is null && executable is null
                ? null
                : new UbisoftGameInfo(gameId, gameName, executable);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UbisoftGameInfo? TryParseKeyValueInfo(string text)
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

            var separator = line.IndexOfAny(new[] { ':', '=' });

            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');

            if (key.Equals("game_code", StringComparison.OrdinalIgnoreCase)
                || key.Equals("gameId", StringComparison.OrdinalIgnoreCase)
                || key.Equals("gameID", StringComparison.OrdinalIgnoreCase)
                || key.Equals("uplay_id", StringComparison.OrdinalIgnoreCase))
            {
                gameId ??= value;
            }
            else if (key.Equals("gameName", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("game_name", StringComparison.OrdinalIgnoreCase)
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
            : new UbisoftGameInfo(gameId, gameName, executable);
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

    private sealed record UbisoftGameInfo(
        string? GameId,
        string? GameName,
        string? ExecutableName);
}

