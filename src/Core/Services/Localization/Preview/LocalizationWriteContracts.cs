namespace GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>Yazılacak tek bir çeviri kaydı.</summary>
/// <param name="Key">Adaptörün ürettiği yapısal anahtar (JSON Pointer).</param>
/// <param name="SourceText">Yazma sırasında dosyada beklenen kaynak metin.</param>
/// <param name="Translation">Kaynak metnin yerine yazılacak çeviri.</param>
public sealed record LocalizationWriteTranslation(
    string Key,
    string SourceText,
    string Translation);

/// <summary>Adaptörün ürettiği yeni dosya içeriği.</summary>
public sealed record LocalizationWriteContentResult(
    bool Success,
    string? Content,
    string? ErrorMessage)
{
    public static LocalizationWriteContentResult Ok(string content)
        => new(true, content, null);

    public static LocalizationWriteContentResult Fail(string message)
        => new(false, null, message);
}

/// <summary>Yazma sırasında kullanılan güvenlik sınırları.</summary>
public sealed class LocalizationFormatWriteOptions
{
    /// <summary>Bu boyuttan büyük dosyalar yazılmaz (varsayılan 32 MB).</summary>
    public long MaxFileBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>En fazla yapısal derinlik (varsayılan 64).</summary>
    public int MaxDepth { get; init; } = 64;
}

/// <summary>
/// Yeni dosya içeriğini <b>bellekte</b> üreten yazılabilir adaptör sözleşmesi.
/// Adaptör orijinal dosyaya yazmaz; güvenli yazma servisi çıktıyı geçici dosyaya yazar.
/// </summary>
public interface ILocalizationWritableFormatAdapter
{
    Task<LocalizationWriteContentResult> BuildWriteContentAsync(
        string sourceFilePath,
        IReadOnlyList<LocalizationWriteTranslation> translations,
        LocalizationFormatWriteOptions? options = null,
        CancellationToken cancellationToken = default);
}
