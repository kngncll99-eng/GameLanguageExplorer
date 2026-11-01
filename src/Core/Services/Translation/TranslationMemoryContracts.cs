namespace GameLanguageExplorer.Core.Services.Translation;

/// <summary>
/// Çeviri belleği sözleşmesi. Motor yalnızca bu arayüze bağlıdır; gerçek tablo erişimi
/// veri katmanındaki uygulamada yapılır.
/// </summary>
public interface ITranslationMemory
{
    /// <summary>Kaynak metin ve dil çifti için tam eşleşme arar; bulunursa hedef metni döndürür.</summary>
    Task<string?> FindExactAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default);

    /// <summary>Başarılı bir çeviriyi belleğe ekler veya aynı anahtarın hedef metnini günceller.</summary>
    Task SaveAsync(
        string sourceText,
        string targetText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default);
}
