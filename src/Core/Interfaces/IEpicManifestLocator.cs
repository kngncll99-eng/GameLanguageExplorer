namespace GameLanguageExplorer.Core.Interfaces;

/// <summary>Epic Games Launcher'ın bilinen manifest klasörlerini sağlar.</summary>
public interface IEpicManifestLocator
{
    /// <summary>
    /// Aday manifest klasörleri. Var olmayan yollar da dönebilir; çağıran taraf varlık
    /// kontrolünü kendisi yapar.
    /// </summary>
    IReadOnlyList<string> GetCandidateManifestDirectories();
}
