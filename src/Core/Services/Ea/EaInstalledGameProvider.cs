namespace GameLanguageExplorer.Core.Services.Ea;

using System.Text.Json;
using System.Xml.Linq;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>
/// EA App (eski Origin) kurulu oyunlarını config/catalog adaylarından ve oyun içi
/// manifest/metadata dosyalarından <b>salt okunur</b> biçimde tespit eder.
/// </summary>
public sealed class EaInstalledGameProvider : IInstalledGameProvider, IScanDiagnostics
{
    public const string ProviderNameValue = "EA";

    private readonly IEaInstallationLocator _locator;
    private readonly ILogger<EaInstalledGameProvider>? _logger;
    private readonly List<string> _warnings = new();
    private readonly object _warningsLock = new();

    public EaInstalledGameProvider(
        IEaInstallationLocator? locator = null,
        ILogger<EaInstalledGameProvider>? logger = null)
    {
        _locator = locator ?? new DefaultEaInstallationLocator();
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

        IReadOnlyList<EaInstallationCandidate> candidates;

        try
        {
            candidates = _locator.GetInstalledCandidates();
        }
        catch (Exception exception)
        {
            var message = $"EA kurulum kaynakları alınamadı: {exception.Message}";
            Warn(message);
            return InstalledGameScanBatch.Failed(ProviderNameValue, message);
        }

        if (candidates.Count == 0)
        {
            const string reason =
                "EA App/Origin kurulumu bulunamadı (EA Desktop/Origin verisi veya bilinen klasör yok).";
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
                Warn("EA adayında geçerli kurulum yolu yok; aday atlandı.");
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
                Warn($"EA kurulum yolu çözülemedi '{candidate.InstallPath}': {exception.Message}");
                continue;
            }

            var folderExists = SafeDirectoryExists(installPath);

            if (!folderExists)
            {
                Warn($"EA kaydı var ama oyun klasörü bulunamadı: '{installPath}'.");
            }

            var info = folderExists ? TryReadGameInfo(installPath) : null;
            var gameName = FirstNonEmpty(
                candidate.GameName,
                info?.GameName,
                Path.GetFileName(NormalizePath(installPath)));
            var gameId = FirstNonEmpty(
                candidate.ContentId,
                info?.ContentId,
                candidate.OfferId,
                info?.OfferId,
                gameName);
            var sourceId = string.IsNullOrWhiteSpace(gameId) ? NormalizePath(installPath) : gameId;

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                partial = true;
                Warn($"EA adayının kimliği belirlenemedi: '{installPath}'.");
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
    /// Oyun klasöründeki bilinen EA/Origin metadata dosyalarını JSON, XML veya basit
    /// anahtar=değer biçiminde okur.
    /// </summary>
    private EaGameInfo? TryReadGameInfo(string installPath)
    {
        string? metadataPath = null;

        try
        {
            foreach (var relativePath in new[]
                     {
                         "installerdata.xml",
                         Path.Combine("__Installer", "installerdata.xml"),
                         "manifest.json",
                         "content.json",
                         "installerdata.json",
                         "Default.dat"
                     })
            {
                var path = Path.Combine(installPath, relativePath);

                if (File.Exists(path))
                {
                    metadataPath = path;
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Warn($"EA metadata dosyası aranamadı '{installPath}': {exception.Message}");
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
                Warn($"EA metadata dosyası çok büyük, atlandı '{metadataPath}'.");
                return null;
            }

            var text = File.ReadAllText(metadataPath);

            return TryParseJsonInfo(text)
                   ?? TryParseXmlInfo(text)
                   ?? TryParseKeyValueInfo(text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Warn($"EA metadata dosyası okunamadı '{metadataPath}': {exception.Message}");
            return null;
        }
        catch (Exception exception)
        {
            Warn($"EA metadata dosyası işlenemedi '{metadataPath}': {exception.Message}");
            return null;
        }
    }

    private static EaGameInfo? TryParseJsonInfo(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = document.RootElement;
            var contentId = FirstNonEmpty(
                GetString(root, "contentId"),
                GetString(root, "contentID"),
                GetString(root, "content_id"),
                GetString(root, "gameId"),
                GetString(root, "id"));
            var offerId = FirstNonEmpty(GetString(root, "offerId"), GetString(root, "offerID"));
            var gameName = FirstNonEmpty(
                GetString(root, "displayName"),
                GetString(root, "gameName"),
                GetString(root, "name"),
                GetString(root, "title"));
            var executable = FirstNonEmpty(
                GetString(root, "exe"),
                GetString(root, "executable"),
                GetString(root, "launchCommand"));

            return contentId is null && offerId is null && gameName is null && executable is null
                ? null
                : new EaGameInfo(contentId, offerId, gameName, executable);
        }
        catch (JsonException)
        {
            return null;
        }
    }


    private static EaGameInfo? TryParseXmlInfo(string text)
    {
        try
        {
            var document = XDocument.Parse(text);
            var contentId = GetXmlValue(document, "contentId", "contentID", "content_id", "gameId");
            var offerId = GetXmlValue(document, "offerId", "offerID");
            var gameName = GetXmlValue(document, "displayName", "gameName", "name", "title");
            var executable = GetXmlValue(document, "exe", "executable", "launchCommand");

            return contentId is null && offerId is null && gameName is null && executable is null
                ? null
                : new EaGameInfo(contentId, offerId, gameName, executable);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        {
            return null;
        }
    }

    private static EaGameInfo? TryParseKeyValueInfo(string text)
    {
        string? contentId = null;
        string? offerId = null;
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

            if (key.Equals("contentId", StringComparison.OrdinalIgnoreCase)
                || key.Equals("contentID", StringComparison.OrdinalIgnoreCase)
                || key.Equals("content_id", StringComparison.OrdinalIgnoreCase)
                || key.Equals("gameId", StringComparison.OrdinalIgnoreCase))
            {
                contentId ??= value;
            }
            else if (key.Equals("offerId", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("offerID", StringComparison.OrdinalIgnoreCase))
            {
                offerId ??= value;
            }
            else if (key.Equals("displayName", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("gameName", StringComparison.OrdinalIgnoreCase)
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

        return contentId is null && offerId is null && gameName is null && executable is null
            ? null
            : new EaGameInfo(contentId, offerId, gameName, executable);
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

    private static string? GetXmlValue(XDocument document, params string[] names)
    {
        foreach (var element in document.Descendants())
        {
            if (!names.Any(name => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(element.Value))
            {
                return element.Value.Trim();
            }

            var attribute = element.Attributes()
                .FirstOrDefault(item => names.Any(name =>
                    string.Equals(item.Name.LocalName, name, StringComparison.OrdinalIgnoreCase)));

            if (attribute is not null && !string.IsNullOrWhiteSpace(attribute.Value))
            {
                return attribute.Value.Trim();
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

    private sealed record EaGameInfo(
        string? ContentId,
        string? OfferId,
        string? GameName,
        string? ExecutableName);
}

