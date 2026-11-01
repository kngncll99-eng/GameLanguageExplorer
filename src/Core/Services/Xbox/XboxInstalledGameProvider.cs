namespace GameLanguageExplorer.Core.Services.Xbox;

using System.Xml.Linq;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.InstalledGames;
using Microsoft.Extensions.Logging;

/// <summary>
/// Xbox App / Microsoft Store oyunlarını paket adaylarından ve paket içi
/// <c>AppxManifest.xml</c> / <c>MicrosoftGame.config</c> dosyalarından salt okunur
/// biçimde tespit eder.
/// </summary>
public sealed class XboxInstalledGameProvider : InstalledGameProviderBase
{
    public const string ProviderNameValue = "Xbox";

    private readonly IXboxInstallationLocator _locator;

    public XboxInstalledGameProvider(
        IXboxInstallationLocator? locator = null,
        ILogger<XboxInstalledGameProvider>? logger = null)
        : base(ProviderNameValue, "Xbox", logger)
    {
        _locator = locator ?? new DefaultXboxInstallationLocator();
    }

    protected override string UnavailableReason =>
        "Xbox oyun kurulumu bulunamadı (XboxGames veya AppModel paketleri yok).";

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
                Warn($"Xbox kurulum yolu çözülemedi '{candidate.InstallPath}'.");
                continue;
            }

            var folderExists = SafeDirectoryExists(installPath);
            var info = folderExists ? TryReadPackageInfo(installPath) : null;

            results.Add(new ProviderInstallCandidate(
                installPath,
                FirstNonEmpty(
                    candidate.PackageFamilyName,
                    candidate.PackageFullName,
                    candidate.ApplicationId,
                    info?.IdentityName),
                FirstNonEmpty(candidate.GameName, info?.DisplayName, Path.GetFileName(NormalizePath(installPath))),
                FirstNonEmpty(info?.ExecutableName)));
        }

        return new ProviderCandidateSet(results, SourceAvailable: true, IsPartial: partial);
    }

    private XboxPackageInfo? TryReadPackageInfo(string installPath)
    {
        string? metadataPath = null;

        try
        {
            foreach (var fileName in new[] { "AppxManifest.xml", "appxmanifest.xml", "MicrosoftGame.config" })
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
            Warn($"Xbox metadata dosyası aranamadı '{installPath}': {exception.Message}");
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
                Warn($"Xbox metadata dosyası çok büyük, atlandı '{metadataPath}'.");
                return null;
            }

            var document = XDocument.Parse(File.ReadAllText(metadataPath));
            var displayName = GetAttribute(document, "DisplayName")
                              ?? GetElementValue(document, "DisplayName");
            var identityName = GetAttribute(document, "Name")
                               ?? GetElementValue(document, "IdentityName");
            var executable = GetAttribute(document, "Executable")
                             ?? GetElementValue(document, "Executable");

            return displayName is null && identityName is null && executable is null
                ? null
                : new XboxPackageInfo(identityName, displayName, executable);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Warn($"Xbox metadata dosyası okunamadı '{metadataPath}': {exception.Message}");
            return null;
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        {
            Warn($"Bozuk Xbox metadata dosyası atlandı '{metadataPath}': {exception.Message}");
            return null;
        }
    }

    private static string? GetAttribute(XDocument document, string name)
    {
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                if (string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(attribute.Value))
                {
                    return attribute.Value.Trim();
                }
            }
        }

        return null;
    }

    private static string? GetElementValue(XDocument document, string name)
    {
        var element = document
            .Descendants()
            .FirstOrDefault(item =>
                string.Equals(item.Name.LocalName, name, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.Value));

        return element?.Value.Trim();
    }


    private sealed record XboxPackageInfo(
        string? IdentityName,
        string? DisplayName,
        string? ExecutableName);
}

