namespace GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>Kaydın yapısal türü.</summary>
public enum LocalizationTextKind
{
    JsonString = 0,
    XmlElementText = 1,
    XmlAttribute = 2,
    DelimitedCell = 3,

    /// <summary>gettext PO/POT mesajı (msgid / msgstr).</summary>
    PoMessage = 4,

    /// <summary>INI anahtar değeri.</summary>
    IniValue = 5,

    /// <summary>LocresCSV satırı (<c>key,source,Translation</c>): anahtar + kaynak metin + mevcut çeviri.</summary>
    LocresCsvEntry = 6
}

/// <summary>Kaydın çevrilebilirlik durumu.</summary>
public enum LocalizationEntryStatus
{
    /// <summary>Metin; çevrilebilir aday.</summary>
    Translatable = 0,

    /// <summary>Metin olabilir ama bağlam belirsiz (ör. metin sütunundaki sayı).</summary>
    Ambiguous = 1,

    /// <summary>Çeviri adayı değil (sayı, boolean, boş, yapısal attribute...).</summary>
    Skipped = 2
}

/// <summary>
/// Ayrıştırılan tek bir metin kaydı. <b>Kalıcı değildir</b>: yalnızca önizleme içindir.
/// Aynı metnin farklı konumlarda bulunması ayrı kayıtlar üretir (<see cref="Key"/> benzersizdir).
/// </summary>
public sealed record LocalizationTextEntry
{
    /// <summary>Yapısal konum: JSON Pointer, XPath benzeri yol veya <c>satır:2 sütun:3</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Dosyadaki orijinal metin (biçimlendirme/değişkenler korunmuş hâliyle).</summary>
    public required string SourceText { get; init; }

    public required LocalizationTextKind Kind { get; init; }

    public required LocalizationEntryStatus Status { get; init; }

    /// <summary>Çevrilebilir değilse nedeni.</summary>
    public string? SkipReason { get; init; }

    /// <summary>Üst yapı / element / CSV başlığı gibi ek bağlam.</summary>
    public string? Context { get; init; }

    /// <summary>Kaynak satır numarası (mümkünse, 1 tabanlı).</summary>
    public int? Line { get; init; }

    /// <summary>Metinde bulunan değişken/biçimlendirme işaretleri.</summary>
    public PlaceholderScanResult Placeholders { get; init; } = PlaceholderScanResult.Empty;

    /// <summary>Kaydın gerçekten metin olduğuna dair kanıt (ör. "JSON string value at /a/b").</summary>
    public required string Evidence { get; init; }

    /// <summary>Kaynağın format anahtarı (<c>FileFormat.Name</c> ile uyumlu: JSON, XML, CSV, TSV, PO, POT, INI).</summary>
    public required string FormatKey { get; init; }

    /// <summary>
    /// Dosyada zaten var olan çeviri (PO <c>msgstr</c>). Yalnızca okuma amaçlı gösterilir;
    /// hiçbir yere yazılmaz. Çeviri yoksa <c>null</c> veya boş dize.
    /// </summary>
    public string? ExistingTranslation { get; init; }

    /// <summary>Kaydın işaretleri (ör. gettext <c>fuzzy</c>).</summary>
    public IReadOnlyList<string> Flags { get; init; } = Array.Empty<string>();

    /// <summary><c>fuzzy</c> işaretli mi? (gözden geçirilmesi gereken çeviri)</summary>
    public bool IsFuzzy => Flags.Any(f => string.Equals(f, "fuzzy", StringComparison.OrdinalIgnoreCase));

    /// <summary>Çoğul kayıtta bu kaydın biçim indeksi (0 tabanlı).</summary>
    public int? PluralIndex { get; init; }

    /// <summary>Çoğul kayıttaki toplam biçim sayısı.</summary>
    public int? PluralCount { get; init; }

    /// <summary>Çoğul kaydın kaynak (İngilizce) çoğul metni (<c>msgid_plural</c>).</summary>
    public string? PluralSourceText { get; init; }

    /// <summary>Kaynak referansı (ör. PO <c>#: src/main.c:42</c>).</summary>
    public string? Reference { get; init; }

    /// <summary>Çeviri sonrası doğrulama için işaret imzası.</summary>
    public string PlaceholderSignature => Placeholders.Signature;
}

/// <summary>Ayrıştırma sonucunun genel durumu.</summary>
public enum LocalizationReadStatus
{
    /// <summary>Ayrıştırma tamamlandı (boş sonuç da olabilir).</summary>
    Parsed = 0,

    /// <summary>Bu format için ayrıştırıcı yok (veya kapalı).</summary>
    UnsupportedFormat = 1,

    /// <summary>Dosya boyut sınırını aşıyor.</summary>
    FileTooLarge = 2,

    /// <summary>Dosya boş veya yalnızca boşluk içeriyor.</summary>
    EmptyFile = 3,

    /// <summary>Sözdizimi/schema hatası.</summary>
    InvalidSyntax = 4,

    /// <summary>Kodlama çözülemedi.</summary>
    EncodingError = 5,

    /// <summary>Dosya okunamadı (erişim/yol hatası).</summary>
    ReadError = 6
}

/// <summary>Ayrıştırma ayarları.</summary>
public sealed class LocalizationFormatReadOptions
{
    /// <summary>Bu boyuttan büyük dosyalar ayrıştırılmaz (varsayılan 32 MB).</summary>
    public long MaxFileBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>En fazla kayıt sayısı (varsayılan 100.000).</summary>
    public int MaxEntries { get; init; } = 100_000;

    /// <summary>En fazla yapısal derinlik (JSON/XML).</summary>
    public int MaxDepth { get; init; } = 64;
}

/// <summary>Bir dosyanın ayrıştırma sonucu.</summary>
public sealed class LocalizationTextReadResult
{
    public LocalizationReadStatus Status { get; set; } = LocalizationReadStatus.Parsed;

    public string? ErrorMessage { get; set; }

    public List<LocalizationTextEntry> Entries { get; } = new();

    public List<string> Warnings { get; } = new();

    /// <summary>Kayıt limiti nedeniyle kesildi mi?</summary>
    public bool Truncated { get; set; }

    /// <summary>Çevrilebilir aday sayısı.</summary>
    public int TranslatableCount => Entries.Count(e => e.Status == LocalizationEntryStatus.Translatable);

    /// <summary>Bağlamı belirsiz kayıt sayısı.</summary>
    public int AmbiguousCount => Entries.Count(e => e.Status == LocalizationEntryStatus.Ambiguous);

    /// <summary>Çeviri adayı olmayan (atlanan) kayıt sayısı.</summary>
    public int SkippedCount => Entries.Count(e => e.Status == LocalizationEntryStatus.Skipped);

    public TimeSpan Duration { get; set; }

    public bool Success => Status == LocalizationReadStatus.Parsed;

    public static LocalizationTextReadResult Fail(LocalizationReadStatus status, string message) => new()
    {
        Status = status,
        ErrorMessage = message
    };
}

