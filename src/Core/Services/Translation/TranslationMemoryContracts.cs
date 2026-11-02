using GameLanguageExplorer.Core.Entities;

namespace GameLanguageExplorer.Core.Services.Translation;

/// <summary>
/// Çeviri belleği kimliğinin bağlam bileşenleri. Aynı kaynak metin farklı oyun,
/// bağlam veya çoğul biçimde farklı çeviriye sahip olabilir.
/// </summary>
public sealed record TranslationMemoryContext(
    string ScopeKey,
    string Context = "",
    int? PluralIndex = null,
    int? PluralCount = null)
{
    /// <summary>Eski/bağlamsız çağrılar için boş kimlik; yeni kayıtlarla eşleşmez.</summary>
    public static TranslationMemoryContext Empty { get; } = new(string.Empty);
}

/// <summary>
/// Çeviri belleği sözleşmesi. Motor yalnızca bu arayüze bağlıdır; gerçek tablo erişimi
/// veri katmanındaki uygulamada yapılır.
/// </summary>
public interface ITranslationMemory
{
    /// <summary>Bağlamsız eski API; kapsam/bağlam bilinmediği için yeni kayıtlarla eşleşmez.</summary>
    Task<string?> FindExactAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
        => FindExactAsync(
            sourceText,
            sourceLanguage,
            targetLanguage,
            TranslationMemoryContext.Empty,
            cancellationToken);

    /// <summary>Kaynak metin, dil çifti ve bağlam kimliği için tam eşleşme arar.</summary>
    Task<string?> FindExactAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        TranslationMemoryContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Bağlamsız eski API; yeni kayıtlar boş kapsamla yazılmaz.</summary>
    Task SaveAsync(
        string sourceText,
        string targetText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
        => SaveAsync(
            sourceText,
            targetText,
            sourceLanguage,
            targetLanguage,
            TranslationMemoryContext.Empty,
            cancellationToken);

    /// <summary>Başarılı bir çeviriyi bağlam kimliğiyle belleğe ekler/günceller.</summary>
    Task SaveAsync(
        string sourceText,
        string targetText,
        string sourceLanguage,
        string targetLanguage,
        TranslationMemoryContext context,
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
