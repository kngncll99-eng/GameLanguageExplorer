namespace GameLanguageExplorer.Core.Services.Localization;

using GameLanguageExplorer.Core.Entities;

/// <summary>Dil dosyası taramasının ayarları.</summary>
public sealed class LocalizationScanOptions
{
    /// <summary>En fazla alt klasör derinliği.</summary>
    public int MaxDepth { get; init; } = 24;

    /// <summary>En fazla taranacak klasör sayısı (sonsuz döngü/çok büyük ağaçlara karşı emniyet).</summary>
    public int MaxDirectories { get; init; } = 20_000;

    /// <summary>En fazla görülecek dosya sayısı.</summary>
    public int MaxFiles { get; init; } = 200_000;

    /// <summary>Kaydedilecek en fazla aday sayısı.</summary>
    public int MaxCandidates { get; init; } = 20_000;

    /// <summary>SHA-256 hesaplanacak en büyük dosya boyutu (bayt). Daha büyükleri karma hesaplanmadan atlanır.</summary>
    public long MaxHashBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>İlerleme bildirimi sıklığı (görülen dosya sayısı).</summary>
    public int ProgressIntervalFiles { get; init; } = 200;

    /// <summary>Kayıt (SaveChanges) aralığı.</summary>
    public int SaveBatchSize { get; init; } = 500;

    /// <summary>Hiç taranmayacak sistem klasörleri.</summary>
    public IReadOnlyCollection<string> ExcludedDirectoryNames { get; init; } = new[]
    {
        "$RECYCLE.BIN",
        "System Volume Information",
        "$WinREAgent"
    };
}

/// <summary>Tarama ilerlemesi.</summary>
public sealed record LocalizationScanProgress(
    int FilesSeen,
    int CandidatesFound,
    string CurrentDirectory);

/// <summary>Dil dosyası taramasının özeti.</summary>
public class LocalizationFileScanResult
{
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }

    public int InstalledGameId { get; set; }
    public string? RootPath { get; set; }

    public int FilesSeen { get; set; }
    public int CandidatesFound { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }

    /// <summary>Kaydı korunup "artık bulunamadı" olarak işaretlenen dosya sayısı.</summary>
    public int MarkedMissing { get; set; }

    public int IgnoredFiles { get; set; }

    /// <summary>İpucu eşleşmediği için listeye alınmayan paket/arşiv sayısı.</summary>
    public int SkippedPackages { get; set; }

    /// <summary>Erişim hataları, limit aşımı vb. nedeniyle tarama eksik kaldı mı?</summary>
    public bool WasPartial { get; set; }

    public List<string> Warnings { get; set; } = new();

    public DateTime ScanCompletedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Seçilen kurulum klasöründe dil dosyası adaylarını <b>salt okunur</b> biçimde keşfeder.
/// Dosyalar değiştirilmez, silinmez, yeniden adlandırılmaz.
/// </summary>
public interface ILocalizationFileScanService
{
    Task<LocalizationFileScanResult> ScanAsync(
        int installedGameId,
        IProgress<LocalizationScanProgress>? progress = null,
        LocalizationScanOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiscoveredFile>> GetDiscoveredFilesAsync(
        int installedGameId,
        CancellationToken cancellationToken = default);

    /// <summary>Bilinen formatları (FileFormat tablosunu) eksikse ekler; mevcut kayıtları değiştirmez.</summary>
    Task<int> EnsureFormatsAsync(CancellationToken cancellationToken = default);
}
