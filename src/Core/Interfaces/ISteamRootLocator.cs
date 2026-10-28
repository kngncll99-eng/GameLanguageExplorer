namespace GameLanguageExplorer.Core.Interfaces;

/// <summary>Aday Steam kurulum kök klasörlerini (ör. <c>C:\Program Files (x86)\Steam</c>) sağlar.</summary>
public interface ISteamRootLocator
{
    /// <summary>
    /// Bilinen konumlardan toplanan aday kök klasörler. Var olmayan yollar da dönebilir;
    /// çağıran taraf varlık kontrolünü kendisi yapar.
    /// </summary>
    IReadOnlyList<string> GetCandidateSteamRoots();
}

/// <summary>
/// Tarama sırasında oluşan, kullanıcıya gösterilebilecek uyarıları taşır
/// (erişilemeyen klasör, bozuk manifest vb.). Hatalar sessizce yutulmaz.
/// </summary>
public interface IScanDiagnostics
{
    IReadOnlyList<string> LastScanWarnings { get; }
}
