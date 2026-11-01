namespace GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>
/// Yazılabilir adaptörlerin ortak giriş noktası: boş istek kontrolü, kaynak dosyayı
/// güvenli okuma ve format-özel içerik üretimini tek akışta birleştirir.
/// </summary>
public static class WritableLocalizationWriteSupport
{
    public static Task<LocalizationWriteContentResult> BuildFromSourceAsync(
        string sourceFilePath,
        IReadOnlyList<LocalizationWriteTranslation> translations,
        LocalizationFormatWriteOptions? options,
        string formatName,
        Func<string, LocalizationFormatWriteOptions, LocalizationWriteContentResult> buildContent,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                if (translations.Count == 0)
                {
                    return LocalizationWriteContentResult.Fail("Yazılacak çeviri kaydı yok.");
                }

                var writeOptions = options ?? new LocalizationFormatWriteOptions();
                var readOptions = new LocalizationFormatReadOptions
                {
                    MaxFileBytes = writeOptions.MaxFileBytes,
                    MaxDepth = writeOptions.MaxDepth
                };

                var content = LocalizationFileReader.ReadText(sourceFilePath, readOptions);

                if (!content.Success || content.Text is null)
                {
                    return LocalizationWriteContentResult.Fail(
                        content.ErrorMessage ?? $"{formatName} dosyası okunamadı.");
                }

                return buildContent(content.Text, writeOptions);
            },
            cancellationToken);
}
