namespace GameLanguageExplorer.Core.Services.Localization.Preview;

using GameLanguageExplorer.Core.Interfaces;

/// <summary>UI'da gösterilecek adaptör bilgisi.</summary>
public sealed record LocalizationFormatAdapterInfo(
    string FormatName,
    string DisplayName,
    string[] Extensions,
    bool SupportsRead,
    bool SupportsWrite)
{
    public string ExtensionsText => string.Join(", ", Extensions);
}

/// <summary>
/// Uzantıya göre ayrıştırıcı seçer. Yalnızca gerçekten uygulanmış adaptörler kayıtlıdır;
/// kayıtlı olmayan format "desteklenmiyor" olarak raporlanır.
/// </summary>
public sealed class LocalizationFormatAdapterRegistry
{
    private readonly IReadOnlyList<ILocalizationFormatAdapter> _adapters;

    public LocalizationFormatAdapterRegistry(IEnumerable<ILocalizationFormatAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        // Aynı format anahtarı iki kez kaydedilirse ilk kayıt geçerlidir.
        var unique = new List<ILocalizationFormatAdapter>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var adapter in adapters)
        {
            if (seen.Add(adapter.FormatName))
            {
                unique.Add(adapter);
            }
        }

        _adapters = unique;
    }

    public IReadOnlyList<ILocalizationFormatAdapter> Adapters => _adapters;

    public ILocalizationFormatAdapter? FindByExtension(string? extension)
    {
        var normalized = NormalizeExtension(extension);

        if (normalized is null)
        {
            return null;
        }

        return _adapters.FirstOrDefault(a =>
            a.SupportsRead &&
            a.SupportedExtensions.Any(e => string.Equals(e, normalized, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Uzantıyı destekleyen <b>tüm</b> ayrıştırıcılar (kayıt sırasıyla). Aynı uzantıyı birden çok
    /// adaptör destekleyebilir (ör. <c>.csv</c> → CSV ve LocresCSV); bu durumda başlığa bakılır.
    /// </summary>
    public IReadOnlyList<ILocalizationFormatAdapter> FindAllByExtension(string? extension)
    {
        var normalized = NormalizeExtension(extension);

        if (normalized is null)
        {
            return Array.Empty<ILocalizationFormatAdapter>();
        }

        return _adapters
            .Where(a =>
                a.SupportsRead &&
                a.SupportedExtensions.Any(e => string.Equals(e, normalized, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>Bu uzantıyı birden çok ayrıştırıcı mı destekliyor?</summary>
    public bool IsExtensionAmbiguous(string? extension) => FindAllByExtension(extension).Count > 1;

    /// <summary>
    /// Başlık örneğine göre ayrıştırıcı seçer (<see cref="ILocalizationFormatSniffer"/> uygulayanlar).
    /// Hiçbiri eşleşmezse <c>null</c> döner; bu durumda çağıran uzantı sırasına geri düşmelidir.
    /// </summary>
    public ILocalizationFormatAdapter? FindByHeader(string? filePath, string? headerSample)
    {
        if (string.IsNullOrWhiteSpace(headerSample))
        {
            return null;
        }

        var path = filePath ?? string.Empty;

        foreach (var adapter in _adapters)
        {
            if (adapter.SupportsRead
                && adapter is ILocalizationFormatSniffer sniffer
                && sniffer.MatchesHeader(path, headerSample))
            {
                return adapter;
            }
        }

        return null;
    }

    private static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var ext = extension.Trim();

        return ext.StartsWith('.') ? ext : "." + ext;
    }

    public ILocalizationFormatAdapter? FindByFormatName(string? formatName)
        => string.IsNullOrWhiteSpace(formatName)
            ? null
            : _adapters.FirstOrDefault(a =>
                string.Equals(a.FormatName, formatName.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Uzantı için gerçekten okunabilen bir ayrıştırıcı var mı?</summary>
    public bool Supports(string? extension) => FindByExtension(extension) is not null;

    public IReadOnlyList<LocalizationFormatAdapterInfo> Describe()
        => _adapters
            .Select(a => new LocalizationFormatAdapterInfo(
                a.FormatName, a.DisplayName, a.SupportedExtensions, a.SupportsRead, a.SupportsWrite))
            .ToList();
}
