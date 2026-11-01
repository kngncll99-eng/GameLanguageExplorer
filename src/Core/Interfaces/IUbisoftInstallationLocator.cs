namespace GameLanguageExplorer.Core.Interfaces;

/// <summary>Ubisoft Connect kurulum kaynağından okunan tek bir oyun adayı.</summary>
public sealed record UbisoftInstallationCandidate(
    string? GameId,
    string? GameName,
    string InstallPath,
    string? ExecutableName = null);

/// <summary>
/// Ubisoft Connect kurulumlarını registry, bilinen klasörler ve yerel config/metadata
/// dosyalarından toplar. Tüm erişim salt okunurdur.
/// </summary>
public interface IUbisoftInstallationLocator
{
    IReadOnlyList<UbisoftInstallationCandidate> GetInstalledCandidates();
}
