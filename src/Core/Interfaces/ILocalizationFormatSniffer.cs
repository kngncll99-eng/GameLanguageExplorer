namespace GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// Uzantısı başka bir ayrıştırıcıyla <b>çakışan</b> biçimleri (ör. <c>.csv</c> → CSV / LocresCSV)
/// içerik başlığına bakarak ayırt eder.
///
/// <para>Yalnızca okuma amaçlıdır: dosya değiştirilmez, hiçbir yere yazılmaz. Uygulamalar
/// yanlış pozitif vermemek için yalnızca <b>belgelenmiş</b> başlık şemasını kabul etmelidir.</para>
/// </summary>
public interface ILocalizationFormatSniffer
{
    /// <summary>
    /// Verilen başlık örneği (dosyanın başı) bu ayrıştırıcının belgelenmiş şemasına uyuyor mu?
    /// </summary>
    /// <param name="filePath">Karar verilen dosya (yalnızca tanılama için).</param>
    /// <param name="headerSample">Dosyanın başından okunmuş sınırlı metin örneği.</param>
    bool MatchesHeader(string filePath, string headerSample);
}
