namespace GameLanguageExplorer.Core.Services.Localization;

/// <summary>Bir formatın içerik türü. Paket/ikili dosyalar asla düz metin sayılmaz.</summary>
public enum LocalizationFormatKind
{
    /// <summary>Düz metin tabanlı yerelleştirme formatı.</summary>
    Text = 0,

    /// <summary>İkili yerelleştirme kataloğu (ör. derlenmiş MO).</summary>
    Binary = 1,

    /// <summary>Paket/arşiv: içine bakmadan hiçbir zaman metin olarak sınıflandırılmaz.</summary>
    Package = 2
}

/// <summary>Bir adayın uygulama tarafından gerçekten okunup yazılabildiğini gösterir.</summary>
public enum LocalizationSupportStatus
{
    /// <summary>Aday, ancak henüz ayrıştırıcı (adapter) yok.</summary>
    CandidateNoAdapter = 0,

    /// <summary>Paket/arşiv; ayrıştırıcı yok, metin olarak sunulmaz.</summary>
    UnsupportedPackage = 1,

    /// <summary>İkili katalog; ayrıştırıcı yok.</summary>
    UnsupportedBinary = 2
}

/// <summary>Bilinen bir yerelleştirme/paket formatının tanımı.</summary>
public sealed record LocalizationFormatInfo(
    string Key,
    string DisplayName,
    string[] Extensions,
    LocalizationFormatKind Kind,
    string? Note);

/// <summary>
/// Tanınan formatların kataloğu. Uzantı eşleşmesi tek başına "destekleniyor" anlamına gelmez;
/// gerçek okuma desteği kayıtlı adaptörlerle belirlenir.
/// </summary>
public static class LocalizationFormatCatalog
{
    // Güven düzeyleri (DiscoveredFile.DetectionConfidence alanına yazılır).
    public const string ConfidenceHigh = "High";
    public const string ConfidenceMedium = "Medium";
    public const string ConfidenceLow = "Low";

    private static readonly LocalizationFormatInfo[] _all =
    {
        new("JSON", "JSON", new[] { ".json" }, LocalizationFormatKind.Text, "Anahtar/değer metin dosyası"),
        new("XML", "XML", new[] { ".xml" }, LocalizationFormatKind.Text, "Etiket tabanlı metin dosyası"),
        new("CSV", "CSV", new[] { ".csv" }, LocalizationFormatKind.Text, "Virgülle ayrılmış metin"),
        new("TSV", "TSV", new[] { ".tsv" }, LocalizationFormatKind.Text, "Sekmeyle ayrılmış metin"),
        new("PO", "PO", new[] { ".po" }, LocalizationFormatKind.Text, "gettext metin kataloğu"),
        new("POT", "POT", new[] { ".pot" }, LocalizationFormatKind.Text, "gettext şablon kataloğu"),
        new("INI", "INI", new[] { ".ini" }, LocalizationFormatKind.Text, "Bölüm/anahtar metin dosyası"),
        new("YAML", "YAML", new[] { ".yaml", ".yml" }, LocalizationFormatKind.Text, "Girintili metin dosyası"),
        new("TXT", "TXT", new[] { ".txt" }, LocalizationFormatKind.Text, "Düz metin"),
        new("STRINGS", "STRINGS", new[] { ".strings" }, LocalizationFormatKind.Text, "Unity/Apple metin kataloğu"),
        new("LOC", "LOC", new[] { ".loc" }, LocalizationFormatKind.Text, "Motora özgü düz metin kataloğu"),
        new("MO", "MO", new[] { ".mo" }, LocalizationFormatKind.Binary, "Derlenmiş gettext kataloğu (ikili)"),

        // Paket/arşiv: asla düz metin sayılmaz.
        new("PAK", "PAK", new[] { ".pak" }, LocalizationFormatKind.Package, "Paket arşivi; ayrıştırıcı yok"),
        new("BUNDLE", "BUNDLE", new[] { ".bundle" }, LocalizationFormatKind.Package, "Paket arşivi; ayrıştırıcı yok"),
        new("ARCHIVE", "ARCHIVE", new[] { ".archive" }, LocalizationFormatKind.Package, "Paket arşivi; ayrıştırıcı yok"),
        new("ASSETS", "ASSETS", new[] { ".assets" }, LocalizationFormatKind.Package, "Varlık paketi; ayrıştırıcı yok"),
        new("UASSET", "UASSET", new[] { ".uasset" }, LocalizationFormatKind.Package, "Unreal varlık paketi; ayrıştırıcı yok"),
        new("BANK", "BANK", new[] { ".bank" }, LocalizationFormatKind.Package, "Ses bankası; ayrıştırıcı yok")
    };

    public static IReadOnlyList<LocalizationFormatInfo> All => _all;

    public static IEnumerable<LocalizationFormatInfo> TextFormats
        => _all.Where(f => f.Kind == LocalizationFormatKind.Text);

    public static IEnumerable<LocalizationFormatInfo> PackageFormats
        => _all.Where(f => f.Kind == LocalizationFormatKind.Package);

    /// <summary>Uzantıya göre format bulur (baştaki nokta olmadan da çalışır).</summary>
    public static LocalizationFormatInfo? FindByExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var ext = extension.Trim();

        if (!ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        return _all.FirstOrDefault(f =>
            f.Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)));
    }

    public static LocalizationFormatInfo? Find(string? key)
        => string.IsNullOrWhiteSpace(key)
            ? null
            : _all.FirstOrDefault(f => string.Equals(f.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adayın destek durumu. Adapter yoksa metin formatları yalnızca "aday"dır;
    /// paket/ikili formatlar hiçbir zaman destekleniyor gibi gösterilmez.
    /// </summary>
    public static LocalizationSupportStatus GetSupportStatus(string? formatKey, string? adapterUsed)
    {
        var format = Find(formatKey);

        if (!string.IsNullOrWhiteSpace(adapterUsed))
        {
            return LocalizationSupportStatus.CandidateNoAdapter; // adapter varsa gerçekten okunabilir
        }

        return format?.Kind switch
        {
            LocalizationFormatKind.Package => LocalizationSupportStatus.UnsupportedPackage,
            LocalizationFormatKind.Binary => LocalizationSupportStatus.UnsupportedBinary,
            _ => LocalizationSupportStatus.CandidateNoAdapter
        };
    }

    /// <summary>UI'da gösterilecek kısa destek metni.</summary>
    public static string DescribeSupport(string? formatKey, string? adapterUsed)
    {
        if (!string.IsNullOrWhiteSpace(adapterUsed))
        {
            return $"Okunabilir ({adapterUsed})";
        }

        return GetSupportStatus(formatKey, null) switch
        {
            LocalizationSupportStatus.UnsupportedPackage => "Desteklenmiyor — paket (ayrıştırıcı yok)",
            LocalizationSupportStatus.UnsupportedBinary => "Desteklenmiyor — ikili (ayrıştırıcı yok)",
            _ => "Aday — ayrıştırıcı yok"
        };
    }
}

