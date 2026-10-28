namespace GameLanguageExplorer.Core.Services;

using GameLanguageExplorer.Core.Entities;

/// <summary>Kurulu oyun taramasının özeti.</summary>
public class InstalledGameScanResult
{
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public int TotalFound { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int MarkedNotInstalled { get; set; }
    public int CatalogGamesCreated { get; set; }
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Erişilemediği/başarısız olduğu için <b>hiçbir kaydı güncellenmeyen</b> sağlayıcılar.
    /// Bu sağlayıcıların mevcut kurulumları korunur, "kurulu değil" işaretlenmez.
    /// </summary>
    public List<string> SkippedProviders { get; set; } = new();

    /// <summary>
    /// Kayıtları işlenen ama kısmi olduğu için "kurulu değil" işaretlemesi yapılmayan sağlayıcılar.
    /// </summary>
    public List<string> PartialProviders { get; set; } = new();

    public DateTime ScanCompletedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Manuel oyun klasörü ekleme sonucu.</summary>
public class ManualGameAddResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Aynı klasör daha önce eklenmişse <c>true</c> (mükerrer kayıt oluşturulmadı).</summary>
    public bool AlreadyExisted { get; set; }

    public int InstalledGameId { get; set; }
    public int GameId { get; set; }
    public string? GameName { get; set; }
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Kurulu oyunları tespit edip veritabanına yazan servis. Sağlayıcılar yalnızca okuma yapar;
/// kalıcılaştırma ve katalog eşleştirmesi burada yürür.
/// </summary>
public interface IInstalledGameScanService
{
    Task<InstalledGameScanResult> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Kullanıcının seçtiği klasörü <c>Manual</c> kaynağı olarak kaydeder. Mağaza ataması yapılmaz,
    /// oyun adı doğrulanmamış kabul edilir ve aynı klasör ikinci kez eklenirse mükerrer kayıt oluşmaz.
    /// </summary>
    Task<ManualGameAddResult> AddManualGameAsync(
        string folderPath,
        string? gameName,
        CancellationToken cancellationToken = default);

    /// <summary>Veritabanındaki kurulu oyun kayıtlarını (oyun bilgisiyle) döndürür.</summary>
    Task<IReadOnlyList<InstalledGame>> GetInstalledGamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Kurulu (IsInstalled = true) kayıt sayısı.</summary>
    Task<int> CountInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>Kayıtlı en son tarama zamanı (yoksa null).</summary>
    Task<DateTime?> GetLastScanAtAsync(CancellationToken cancellationToken = default);
}

