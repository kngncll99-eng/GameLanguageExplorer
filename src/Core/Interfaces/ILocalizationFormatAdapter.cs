namespace GameLanguageExplorer.Core.Interfaces;

using GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>
/// Yerelleştirme dosyası ayrıştırıcısı (adaptör).
///
/// <para>Yazma desteği yalnızca <see cref="SupportsWrite"/> <c>true</c> olan ve
/// <c>ILocalizationWritableFormatAdapter</c> uygulayan adaptörlerde vardır. Şu an bu destek
/// JSON ile sınırlıdır; diğer adaptörler salt okunurdur.</para>
///
/// <para>Uzantı tanımak içerik desteği anlamına gelmez: adaptör, dosyayı gerçekten çözümleyip
/// <see cref="LocalizationTextReadResult"/> üretmeden başarı bildirmez.</para>
/// </summary>
public interface ILocalizationFormatAdapter
{
    /// <summary><c>FileFormat.Name</c> ile aynı anahtar (ör. <c>JSON</c>).</summary>
    string FormatName { get; }

    /// <summary>UI'da gösterilecek ad.</summary>
    string DisplayName { get; }

    /// <summary>Desteklenen uzantılar (nokta dahil).</summary>
    string[] SupportedExtensions { get; }

    bool SupportsRead { get; }

    /// <summary>Adaptör güvenli yazma akışında kullanılabilir mi? (şu an yalnızca JSON).</summary>
    bool SupportsWrite { get; }

    /// <summary>
    /// Dosyayı salt okunur ayrıştırır. Bozuk/aşırı büyük/çözülemeyen dosyalarda
    /// istisna fırlatmak yerine hata durumu döndürür.
    /// </summary>
    Task<LocalizationTextReadResult> ReadAsync(
        string filePath,
        LocalizationFormatReadOptions? options = null,
        CancellationToken cancellationToken = default);
}
