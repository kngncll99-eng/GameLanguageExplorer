namespace GameLanguageExplorer.Core.Services.Localization.Export;

/// <summary>Dışa aktarma hedef formatı.</summary>
public enum LocalizationExportFormat
{
    Json = 0,
    Csv = 1,
    Po = 2
}

/// <summary>Dışa aktarılacak tek bir çeviri kaydı.</summary>
public sealed record LocalizationExportEntry(
    string Key,
    string SourceText,
    string Translation,
    string? Context = null);

/// <summary>Dışa aktarma sonucu. Yer tutucu uyuşmazlıkları uyarı olarak taşınır.</summary>
public sealed record LocalizationExportResult(
    bool Success,
    string? FilePath,
    int EntryCount,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage)
{
    public static LocalizationExportResult Fail(string message) =>
        new(false, null, 0, Array.Empty<string>(), message);
}

/// <summary>
/// Seçilen çevirileri yeni bir JSON, CSV veya PO dosyasına yazar.
/// Orijinal oyun dosyasına dokunmaz; yalnızca dışa aktarma hedefini oluşturur.
/// </summary>
public interface ILocalizationExportService
{
    Task<LocalizationExportResult> ExportAsync(
        string filePath,
        LocalizationExportFormat format,
        IReadOnlyList<LocalizationExportEntry> entries,
        CancellationToken cancellationToken = default);
}
