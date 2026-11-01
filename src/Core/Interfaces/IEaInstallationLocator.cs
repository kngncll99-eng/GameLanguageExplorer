namespace GameLanguageExplorer.Core.Interfaces;

/// <summary>EA App / Origin kurulum kaynağından okunan tek bir oyun adayı.</summary>
public sealed record EaInstallationCandidate(
    string? ContentId,
    string? GameName,
    string InstallPath,
    string? ExecutableName = null,
    string? OfferId = null);

/// <summary>
/// EA App/Origin kurulumlarını config, catalog ve registry kaynaklarından toplar.
/// Tüm erişim salt okunurdur.
/// </summary>
public interface IEaInstallationLocator
{
    IReadOnlyList<EaInstallationCandidate> GetInstalledCandidates();
}
