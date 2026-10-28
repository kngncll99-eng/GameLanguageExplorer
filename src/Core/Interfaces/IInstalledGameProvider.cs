namespace GameLanguageExplorer.Core.Interfaces;

using GameLanguageExplorer.Core.Entities;

/// <summary>Bir kurulu oyun kaynağının tarama sonucunun durumu.</summary>
public enum InstalledGameScanStatus
{
    /// <summary>Tarama tamamlandı; sonuç güvenilir (boş sonuç da olabilir).</summary>
    Completed = 0,

    /// <summary>Kaynak bu makinede bulunamadı/erişilemedi (ör. launcher kurulu değil).</summary>
    Unavailable = 1,

    /// <summary>Tarama sırasında hata oluştu.</summary>
    Failed = 2
}

/// <summary>
/// Bir sağlayıcının tarama çıktısı. <b>Sonuç ile güvenilirliği ayırır:</b>
/// boş ama başarılı bir tarama ile başarısız bir tarama aynı şey değildir.
/// </summary>
public sealed class InstalledGameScanBatch
{
    public string ProviderName { get; init; } = string.Empty;

    public InstalledGameScanStatus Status { get; init; } = InstalledGameScanStatus.Completed;

    /// <summary>
    /// Tarama kısmen tamamlandı (bazı kütüphaneler/manifestler okunamadı). Kayıt eklemek için
    /// yeterli, ancak "artık kurulu değil" işaretlemesi için yeterince eksiksiz değil.
    /// </summary>
    public bool IsPartial { get; init; }

    public IReadOnlyList<InstalledGame> Games { get; init; } = Array.Empty<InstalledGame>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string? ErrorMessage { get; init; }

    /// <summary>Bulunan kayıtlar veritabanına yazılabilir mi?</summary>
    public bool IsTrustworthy => Status == InstalledGameScanStatus.Completed;

    /// <summary>
    /// Bu tarama, sağlayıcının sorumlu olduğu önceki kayıtları "kurulu değil" işaretlemek için
    /// yeterince eksiksiz mi? (Başarısız/erişilemeyen/kısmi taramalarda <c>false</c>.)
    /// </summary>
    public bool IsAuthoritative => Status == InstalledGameScanStatus.Completed && !IsPartial;

    public static InstalledGameScanBatch Completed(
        string providerName,
        IReadOnlyList<InstalledGame> games,
        IReadOnlyList<string>? warnings = null,
        bool isPartial = false) => new()
        {
            ProviderName = providerName,
            Status = InstalledGameScanStatus.Completed,
            Games = games ?? Array.Empty<InstalledGame>(),
            Warnings = warnings ?? Array.Empty<string>(),
            IsPartial = isPartial
        };

    public static InstalledGameScanBatch Unavailable(string providerName, string reason) => new()
    {
        ProviderName = providerName,
        Status = InstalledGameScanStatus.Unavailable,
        ErrorMessage = reason
    };

    public static InstalledGameScanBatch Failed(string providerName, string error) => new()
    {
        ProviderName = providerName,
        Status = InstalledGameScanStatus.Failed,
        ErrorMessage = error
    };
}

/// <summary>
/// Kurulu oyun kaynağı sağlayıcısı. Her kaynak (Steam, manuel klasör, ileride Epic/GOG/...)
/// bağımsız bir uygulama olarak eklenir ve <b>salt okunur</b> tespit yapar.
/// </summary>
public interface IInstalledGameProvider
{
    string ProviderName { get; }

    /// <summary>
    /// Kurulu oyunları tespit eder. Sonuç, taramanın güvenilirliğini de bildirir.
    /// Launcher kurulu değilse <see cref="InstalledGameScanBatch.Unavailable"/> döner.
    /// </summary>
    Task<InstalledGameScanBatch> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>Belirli bir kurulumu (sağlayıcıya özgü kimlikle) arar.</summary>
    Task<InstalledGame?> DetectInstallationAsync(string gameIdentifier, CancellationToken cancellationToken = default);
}
