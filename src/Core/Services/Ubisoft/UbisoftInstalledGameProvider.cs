namespace GameLanguageExplorer.Core.Services.Ubisoft;

using System.Text.Json;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>
/// Ubisoft Connect kurulu oyunlarını registry/klasör adaylarından ve oyun içi
/// <c>uplay_install.manifest</c> / benzeri metadata dosyalarından <b>salt okunur</b>
/// biçimde tespit eder.
/// </summary>
public sealed class UbisoftInstalledGameProvider : IInstalledGameProvider, IScanDiagnostics
{
    public const string ProviderNameValue = "Ubisoft";

    private readonly IUbisoftInstallationLocator _locator;
    private readonly ILogger<UbisoftInstalledGameProvider>? _logger;
    private readonly List<string> _warnings = new();
    private readonly object _warningsLock = new();

    public UbisoftInstalledGameProvider(
        IUbisoftInstallationLocator? locator = null,
        ILogger<UbisoftInstalledGameProvider>? logger = null)
    {
        _locator = locator ?? new DefaultUbisoftInstallationLocator();
        _logger = logger;
    }

    public string ProviderName => ProviderNameValue;

    public IReadOnlyList<string> LastScanWarnings
    {
        get
        {
            lock (_warningsLock)
            {
                return _warnings.ToArray();
            }
        }
    }

    public Task<InstalledGameScanBatch> ScanAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => ScanCore(cancellationToken), cancellationToken);

    public async Task<InstalledGame?> DetectInstallationAsync(
        string gameIdentifier,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameIdentifier))
        {
            return null;
        }

        var batch = await ScanAsync(cancellationToken).ConfigureAwait(false);
        var identifier = gameIdentifier.Trim();

        return batch.Games.FirstOrDefault(game =>
            string.Equals(game.InstallSourceId, identifier, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizePath(game.InstallPath), NormalizePath(identifier), StringComparison.OrdinalIgnoreCase));
    }

    private InstalledGameScanBatch ScanCore(CancellationToken cancellationToken)
    {
        lock (_warningsLock)
        {
            _warnings.Clear();
        }

        IReadOnlyList<UbisoftInstallationCandidate> candidates;

        try
        {
            candidates = _locator.GetInstalledCandidates();
        }
        catch (Exception exception)
        {
            var message = $"Ubisoft kurulum kaynakları alınamadı: {exception.Message}";
            Warn(message);
            return InstalledGameScanBatch.Failed(ProviderNameValue, message);
        }

        if (candidates.Count == 0)
        {
            const string reason =
                "Ubisoft Connect kurulumu bulunamadı (registry veya bilinen Ubisoft klasörleri yok).";
            Warn(reason);
            return InstalledGameScanBatch.Unavailable(ProviderNameValue, reason);
        }

        var now = DateTime.UtcNow;
        var results = new List<InstalledGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var partial = false;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(candidate.InstallPath)
                || !Path.IsPathFullyQualified(candidate.InstallPath.Trim()))
            {
                partial = true;
                Warn("Ubisoft adayında geçerli kurulum yolu yok; aday atlandı.");
                continue;
            }

            string installPath;

            try
            {
                installPath = Path.GetFullPath(candidate.InstallPath.Trim());
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                partial = true;
                Warn($"Ubisoft kurulum yolu çözülemedi '{candidate.InstallPath}': {exception.Message}");
                continue;
            }

            var folderExists = SafeDirectoryExists(installPath);

            if (!folderExists)
            {
                Warn($"Ubisoft kaydı var ama oyun klasörü bulunamadı: '{installPath}'.");
            }

            var info = folderExists ? TryReadGameInfo(installPath) : null;
            var gameName = FirstNonEmpty(
                candidate.GameName,
                info?.GameName,
                Path.GetFileName(NormalizePath(installPath)));
            var gameId = FirstNonEmpty(candidate.GameId, info?.GameId, gameName);
            var sourceId = string.IsNullOrWhiteSpace(gameId) ? NormalizePath(installPath) : gameId;

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                partial = true;
                Warn($"Ubisoft adayının kimliği belirlenemedi: '{installPath}'.");
                continue;
            }

            var installation = new InstalledGame
            {
                GameId = 0,
                InstallPath = installPath,
                ExecutableName = FirstNonEmpty(candidate.ExecutableName, info?.ExecutableName),
                SizeInBytes = null,
                InstalledAt = null,
                LastScanAt = now,
                IsInstalled = folderExists,
                InstallSource = ProviderNameValue,
                InstallSourceId = sourceId,
                DetectedGameName = string.IsNullOrWhiteSpace(gameName) ? sourceId : gameName
            };

            if (seen.Add($"{installation.InstallSourceId}|{NormalizePath(installation.InstallPath)}"))
            {
                results.Add(installation);
            }
        }

        return InstalledGameScanBatch.Completed(
            ProviderNameValue,
            results,
            LastScanWarnings,
            isPartial: partial);
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

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

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

    private static string NormalizePath(string? path)
        => (path ?? string.Empty).Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private void Warn(string message)
    {
        lock (_warningsLock)
        {
            if (!_warnings.Contains(message, StringComparer.Ordinal))
            {
                _warnings.Add(message);
            }
        }

        _logger?.LogWarning("{Message}", message);
    }

    private sealed record UbisoftGameInfo(
        string? GameId,
        string? GameName,
        string? ExecutableName);
}

