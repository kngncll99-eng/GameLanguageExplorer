namespace GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;

using System.Diagnostics;
using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// YAML ayrıştırıcısı: eşleme ve listelerdeki skaler <b>string</b> değerleri çıkarır.
///
/// <para>Desteklenen yaygın YAML biçimi: blok eşlemeleri, blok listeleri, tırnaklı/kaçışlı
/// skalerler, yorumlar, <c>|</c>/<c>&gt;</c> blok metinleri ve basit akış (flow) listeleri/eşlemeleri.
/// Çapa/alias, etiket ve karmaşık anahtar gibi gelişmiş YAML özellikleri desteklenmez.</para>
///
/// <para>Sayı, boolean ve null skalerler çeviri adayı olarak sunulmaz. Dosya asla yazılmaz;
/// <see cref="SupportsWrite"/> her zaman <c>false</c>'tur.</para>
/// </summary>
public sealed class YamlLocalizationAdapter : ILocalizationFormatAdapter
{
    public const string FormatKey = "YAML";

    public string FormatName => FormatKey;
    public string DisplayName => "YAML";
    public string[] SupportedExtensions => new[] { ".yaml", ".yml" };
    public bool SupportsRead => true;
    public bool SupportsWrite => false;

    public Task<LocalizationTextReadResult> ReadAsync(
        string filePath,
        LocalizationFormatReadOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => Read(filePath, options ?? new LocalizationFormatReadOptions(), cancellationToken),
            cancellationToken);

    private static LocalizationTextReadResult Read(
        string filePath,
        LocalizationFormatReadOptions options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var content = LocalizationFileReader.ReadText(filePath, options);

        if (!content.Success)
        {
            return LocalizationTextReadResult.Fail(
                content.Status,
                content.ErrorMessage ?? "Dosya okunamadı.");
        }

        try
        {
            var result = YamlPreviewParser.Parse(content.Text!, options, cancellationToken);
            result.Duration = stopwatch.Elapsed;
            return result;
        }
        catch (YamlParseException ex)
        {
            return LocalizationTextReadResult.Fail(
                LocalizationReadStatus.InvalidSyntax,
                ex.Line > 0
                    ? $"Geçersiz YAML (satır {ex.Line}): {ex.Message}"
                    : $"Geçersiz YAML: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return LocalizationTextReadResult.Fail(
                LocalizationReadStatus.InvalidSyntax,
                $"YAML ayrıştırılamadı: {ex.Message}");
        }
    }
}
