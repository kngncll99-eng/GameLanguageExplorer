using GameLanguageExplorer.Core.Entities;

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

    /// <summary>
    /// Bellekteki kayıtları en son kullanılan önce olacak şekilde listeler.
    /// <paramref name="sourceTextFilter"/> boş değilse yalnızca kaynak metni bu değeri içeren
    /// kayıtlar döndürülür.
    /// </summary>
    Task<IReadOnlyList<TranslationMemoryEntry>> GetEntriesAsync(
        string? sourceTextFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>Tek bir çeviri belleği kaydını siler. Kayıt bulunamazsa <c>false</c> döner.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Tüm çeviri belleği kayıtlarını siler ve silinen kayıt sayısını döndürür.</summary>
    Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);
}
