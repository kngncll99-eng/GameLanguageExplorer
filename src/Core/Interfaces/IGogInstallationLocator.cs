namespace GameLanguageExplorer.Core.Interfaces;

/// <summary>GOG kurulum kaynağından okunan tek bir oyun adayı.</summary>
public sealed record GogInstallationCandidate(
    string? GameId,
    string? GameName,
    string InstallPath,
    string? ExecutableName = null);

/// <summary>
/// GOG kurulumlarını kayıt defteri, bilinen klasörler ve yerel metadata dosyalarından
/// toplar. Uygulama yalnızca okur; hiçbir dosya veya kayıt defteri değeri değiştirilmez.
/// </summary>
public interface IGogInstallationLocator
{
    IReadOnlyList<GogInstallationCandidate> GetInstalledCandidates();
}
