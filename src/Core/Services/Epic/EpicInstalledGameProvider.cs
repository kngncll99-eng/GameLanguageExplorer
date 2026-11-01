namespace GameLanguageExplorer.Core.Services.Epic;

using System.Text.Json;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.InstalledGames;
using Microsoft.Extensions.Logging;

/// <summary>
/// Epic Games Launcher'ın <c>.item</c> manifestlerinden kurulu oyunları <b>salt okunur</b>
/// biçimde tespit eder. Manifest dosyaları yalnızca okunur; hiçbir dosya değiştirilmez.
/// </summary>
public sealed class EpicInstalledGameProvider : InstalledGameProviderBase
{
    public const string ProviderNameValue = "Epic";

    private readonly IEpicManifestLocator _locator;

    public EpicInstalledGameProvider(
        IEpicManifestLocator? locator = null,
        ILogger<EpicInstalledGameProvider>? logger = null)
        : base(ProviderNameValue, "Epic", logger)
    {
        _locator = locator ?? new EpicManifestLocator();
    }

    protected override string UnavailableReason =>
        "Epic Games Launcher manifest klasörü bulunamadı (Epic kurulu olmayabilir).";

    protected override ProviderCandidateSet LoadCandidates(CancellationToken cancellationToken)
    {
        var candidateDirectories = _locator.GetCandidateManifestDirectories();
        var existingDirectories = candidateDirectories
            .Where(SafeDirectoryExists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (existingDirectories.Count == 0)
        {
            return new ProviderCandidateSet(Array.Empty<ProviderInstallCandidate>(), SourceAvailable: false);
        }

        var results = new List<ProviderInstallCandidate>();
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

                var installation = ReadInstallation(file, ref partial, cancellationToken);

                if (installation is not null)
                {
                    results.Add(installation);
                }
            }
        }

        return new ProviderCandidateSet(results, SourceAvailable: true, IsPartial: partial);
    }

    private ProviderInstallCandidate? ReadInstallation(
        string manifestPath,
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

            if (GetBool(root, "bIsIncompleteInstall") == true)
            {
                Warn($"Epic oyunu tamamlanmamış kurulum olarak işaretlenmiş: '{sourceId}'.");
            }

            var gameName = !string.IsNullOrWhiteSpace(displayName)
                ? displayName.Trim()
                : !string.IsNullOrWhiteSpace(appName)
                    ? appName.Trim()
                    : Path.GetFileName(NormalizePath(installPath));

            return new ProviderInstallCandidate(
                installPath,
                sourceId,
                gameName,
                string.IsNullOrWhiteSpace(launchExecutable) ? null : launchExecutable.Trim(),
                GetLong(root, "InstallSize"));
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

}

