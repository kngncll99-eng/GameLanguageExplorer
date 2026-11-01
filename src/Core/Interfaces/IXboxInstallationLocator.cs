namespace GameLanguageExplorer.Core.Interfaces;

/// <summary>Xbox / Microsoft Store kurulum kaynağından okunan tek bir oyun adayı.</summary>
public sealed record XboxInstallationCandidate(
    string? PackageFamilyName,
    string? GameName,
    string InstallPath,
    string? PackageFullName = null,
    string? ApplicationId = null);

/// <summary>
/// Xbox App / Microsoft Store kurulumlarını registry, bilinen klasörler ve paket
/// metadata dosyalarından toplar. Tüm erişim salt okunurdur.
/// </summary>
public interface IXboxInstallationLocator
{
    IReadOnlyList<XboxInstallationCandidate> GetInstalledCandidates();
}
