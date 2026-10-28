namespace GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>Önizlemenin başarısızlık nedeni.</summary>
public enum LocalizationPreviewFailureKind
{
    None = 0,

    /// <summary>Kurulum veya dosya kaydı bulunamadı.</summary>
    NotFound = 1,

    /// <summary>Yol, seçili kurulum dizininin dışında (dosya hiç açılmadı).</summary>
    PathRejected = 2,

    /// <summary>Bu format için ayrıştırıcı yok.</summary>
    UnsupportedFormat = 3,

    /// <summary>Kayıt var ama dosya diskte yok.</summary>
    FileMissing = 4,

    /// <summary>Ayrıştırma hatası (bozuk sözdizimi, kodlama vb.).</summary>
    ParseError = 5,

    /// <summary>Beklenmeyen hata.</summary>
    UnexpectedError = 6
}

/// <summary>Bir dosyanın metin önizleme sonucu.</summary>
public sealed class LocalizationPreviewResult
{
    public bool Success { get; set; }
    public LocalizationPreviewFailureKind Failure { get; set; } = LocalizationPreviewFailureKind.None;
    public string? ErrorMessage { get; set; }

    public int InstalledGameId { get; set; }
    public int DiscoveredFileId { get; set; }

    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    public string? FormatKey { get; set; }

    /// <summary>Kullanılan adaptörün görünen adı (yoksa <c>null</c>).</summary>
    public string? AdapterName { get; set; }

    public bool FormatSupported { get; set; }

    public LocalizationReadStatus ReadStatus { get; set; } = LocalizationReadStatus.UnsupportedFormat;

    public List<LocalizationTextEntry> Entries { get; set; } = new();

    public int TranslatableCount { get; set; }
    public int AmbiguousCount { get; set; }
    public int SkippedCount { get; set; }

    public List<string> Warnings { get; set; } = new();

    public bool Truncated { get; set; }

    public TimeSpan Duration { get; set; }

    public static LocalizationPreviewResult Reject(
        LocalizationPreviewFailureKind kind,
        string message,
        string filePath = "",
        string fileName = "") => new()
        {
            Success = false,
            Failure = kind,
            ErrorMessage = message,
            FilePath = filePath,
            FileName = fileName
        };
}

/// <summary>
/// Keşfedilmiş bir dosyayı <b>salt okunur</b> ayrıştırıp metin kayıtlarını önizler.
/// Dosya açılmadan önce yolun seçili kurulum dizini içinde olduğu doğrulanır.
/// </summary>
public interface ILocalizationPreviewService
{
    Task<LocalizationPreviewResult> PreviewAsync(
        int installedGameId,
        int discoveredFileId,
        CancellationToken cancellationToken = default);

    /// <summary>Kayıtlı ayrıştırıcıların listesi (UI bilgilendirmesi).</summary>
    IReadOnlyList<LocalizationFormatAdapterInfo> GetAdapterInfo();
}
