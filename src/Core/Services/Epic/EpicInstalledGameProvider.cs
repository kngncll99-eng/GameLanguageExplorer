namespace GameLanguageExplorer.Core.Services.Epic;

using System.Text.Json;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>
/// Epic Games Launcher'ın <c>.item</c> manifestlerinden kurulu oyunları <b>salt okunur</b>
/// biçimde tespit eder. Manifest dosyaları yalnızca okunur; hiçbir dosya değiştirilmez.
/// </summary>
public sealed class EpicInstalledGameProvider : IInstalledGameProvider, IScanDiagnostics
{
    public const string ProviderNameValue = "Epic";

    private readonly IEpicManifestLocator _locator;
    private readonly ILogger<EpicInstalledGameProvider>? _logger;
    private readonly List<string> _warnings = new();
    private readonly object _warningsLock = new();

    public EpicInstalledGameProvider(
        IEpicManifestLocator? locator = null,
        ILogger<EpicInstalledGameProvider>? logger = null)
    {
        _locator = locator ?? new EpicManifestLocator();
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

        return batch.Games.FirstOrDefault(game =>
            string.Equals(game.InstallSourceId, gameIdentifier.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private InstalledGameScanBatch ScanCore(CancellationToken cancellationToken)
    {
        lock (_warningsLock)
        {
            _warnings.Clear();
        }

        IReadOnlyList<string> candidateDirectories;

        try
        {
            candidateDirectories = _locator.GetCandidateManifestDirectories();
        }
        catch (Exception exception)
        {
            var message = $"Epic manifest konumları alınamadı: {exception.Message}";
            Warn(message);
            return InstalledGameScanBatch.Failed(ProviderNameValue, message);
        }

        var existingDirectories = candidateDirectories
            .Where(SafeDirectoryExists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (existingDirectories.Count == 0)
        {
            const string reason =
                "Epic Games Launcher manifest klasörü bulunamadı (Epic kurulu olmayabilir).";
            Warn(reason);
            return InstalledGameScanBatch.Unavailable(ProviderNameValue, reason);
        }

        var now = DateTime.UtcNow;
        var results = new List<InstalledGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var partial = false;

        foreach (var directory in existingDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string[] files;

            try
            {
                files = Directory.GetFiles(directory, "*.item", SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                partial = true;
                Warn($"Epic manifest klasörü okunamadı '{directory}': {exception.Message}");
                continue;
            }
            catch (Exception exception)
            {
                partial = true;
                Warn($"Epic manifest klasörü taranırken beklenmeyen hata '{directory}': {exception.Message}");
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var installation = ReadInstallation(file, now, ref partial, cancellationToken);

                if (installation is not null
                    && seen.Add($"{installation.InstallSourceId}|{NormalizePath(installation.InstallPath)}"))
                {
                    results.Add(installation);
                }
            }
        }

        return InstalledGameScanBatch.Completed(
            ProviderNameValue,
            results,
            LastScanWarnings,
            isPartial: partial);
    }

    private InstalledGame? ReadInstallation(
        string manifestPath,
        DateTime now,
        ref bool partial,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(manifestPath),
                new JsonDocumentOptions { MaxDepth = 16 });

            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                partial = true;
                Warn($"Epic manifesti nesne değil, atlandı '{manifestPath}'.");
                return null;
            }

            var appName = GetString(root, "AppName");
            var catalogItemId = GetString(root, "CatalogItemId");
            var displayName = GetString(root, "DisplayName");
            var installLocation = GetString(root, "InstallLocation");
            var launchExecutable = GetString(root, "LaunchExecutable");

            var sourceId = !string.IsNullOrWhiteSpace(appName) ? appName.Trim() : catalogItemId?.Trim();

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                partial = true;
                Warn($"Epic manifestinde AppName/CatalogItemId yok, atlandı '{manifestPath}'.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(installLocation)
                || !Path.IsPathFullyQualified(installLocation.Trim()))
            {
                partial = true;
                Warn($"Epic manifestinde geçerli InstallLocation yok, atlandı '{manifestPath}'.");
                return null;
            }

            string installPath;

            try
            {
                installPath = Path.GetFullPath(installLocation.Trim());
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                partial = true;
                Warn($"Epic InstallLocation çözülemedi '{manifestPath}': {exception.Message}");
                return null;
            }

            var folderExists = SafeDirectoryExists(installPath);

            if (!folderExists)
            {
                Warn($"Epic manifesti var ama oyun klasörü bulunamadı: '{installPath}'.");
            }

            if (GetBool(root, "bIsIncompleteInstall") == true)
            {
                Warn($"Epic oyunu tamamlanmamış kurulum olarak işaretlenmiş: '{sourceId}'.");
            }

            var gameName = !string.IsNullOrWhiteSpace(displayName)
                ? displayName.Trim()
                : !string.IsNullOrWhiteSpace(appName)
                    ? appName.Trim()
                    : Path.GetFileName(NormalizePath(installPath));

            return new InstalledGame
            {
                GameId = 0,
                InstallPath = installPath,
                ExecutableName = string.IsNullOrWhiteSpace(launchExecutable)
                    ? null
                    : launchExecutable.Trim(),
                SizeInBytes = GetLong(root, "InstallSize"),
                InstalledAt = null,
                LastScanAt = now,
                IsInstalled = folderExists,
                InstallSource = ProviderNameValue,
                InstallSourceId = sourceId,
                DetectedGameName = string.IsNullOrWhiteSpace(gameName) ? sourceId : gameName
            };
        }
        catch (JsonException exception)
        {
            partial = true;
            Warn($"Bozuk Epic manifesti atlandı '{manifestPath}': {exception.Message}");
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            partial = true;
            Warn($"Epic manifesti okunamadı '{manifestPath}': {exception.Message}");
            return null;
        }
        catch (Exception exception)
        {
            partial = true;
            Warn($"Epic manifesti işlenirken beklenmeyen hata '{manifestPath}': {exception.Message}");
            return null;
        }
    }


    private static string? GetString(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : null;
        }

        return null;
    }

    private static long? GetLong(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetInt64(out var number))
            {
                return number;
            }

            if (property.Value.ValueKind == JsonValueKind.String
                && long.TryParse(
                    property.Value.GetString(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out number))
            {
                return number;
            }
        }

        return null;
    }

    private static bool? GetBool(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(property.Value.GetString(), out var parsed) => parsed,
                _ => null
            };
        }

        return null;
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

    private static string NormalizePath(string path)
        => path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
}

