namespace GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;

using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// XML ayrıştırıcısı: element metinlerini ve metin taşıyan attribute değerlerini çıkarır.
///
/// <list type="bullet">
/// <item>DTD işleme <b>kapalı</b> (<see cref="DtdProcessing.Prohibit"/>) ve XML çözümleyici
/// <c>null</c>: XXE, harici entity ve ağ kaynağı çözümlemesi yapılmaz.</item>
/// <item>Etiket adları, attribute adları ve yapı çeviri metni olarak sunulmaz.</item>
/// <item>Ad alanları (namespace) desteklenir; yol yerel adla, namespace bağlam olarak kaydedilir.</item>
/// <item>Metin, dosyadaki hâliyle (boşluklar korunarak) kaydedilir.</item>
/// </list>
/// </summary>
public sealed class XmlLocalizationAdapter : ILocalizationFormatAdapter
{
    public const string FormatKey = "XML";

    private static readonly HashSet<string> StructuralAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "key", "x", "y", "w", "h", "width", "height", "size", "count", "index",
        "visible", "enabled", "resource", "source", "ref", "href", "type", "format",
        "version", "lang", "language", "culture", "encoding"
    };

    public string FormatName => FormatKey;
    public string DisplayName => "XML";
    public string[] SupportedExtensions => new[] { ".xml" };
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
            return LocalizationTextReadResult.Fail(content.Status, content.ErrorMessage ?? "Dosya okunamadı.");
        }

        try
        {
            var settings = new XmlReaderSettings
            {
                // Güvenlik: DTD yok, harici kaynak yok → XXE / entity bombası engellenir.
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = false,
                CloseInput = true,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = options.MaxFileBytes
            };

            using var stringReader = new StringReader(content.Text!);
            using var reader = XmlReader.Create(stringReader, settings);
            var document = XDocument.Load(reader, LoadOptions.SetLineInfo);

            var result = new LocalizationTextReadResult();
            var usedKeys = new HashSet<string>(StringComparer.Ordinal);

            if (document.Root is not null)
            {
                Walk(document.Root, "/" + document.Root.Name.LocalName, 0, options, result, usedKeys, cancellationToken);
            }

            result.Duration = stopwatch.Elapsed;
            return result;
        }
        catch (XmlException ex)
        {
            return LocalizationTextReadResult.Fail(
                LocalizationReadStatus.InvalidSyntax,
                $"Geçersiz XML (satır {ex.LineNumber}, sütun {ex.LinePosition}): {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return LocalizationTextReadResult.Fail(
                LocalizationReadStatus.InvalidSyntax,
                $"XML ayrıştırılamadı: {ex.Message}");
        }
    }

    private static void Walk(
        XElement element,
        string path,
        int depth,
        LocalizationFormatReadOptions options,
        LocalizationTextReadResult result,
        HashSet<string> usedKeys,
        CancellationToken cancellationToken)
    {
        if (result.Truncated)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (depth > options.MaxDepth)
        {
            result.Truncated = true;
            result.Warnings.Add($"XML derinlik sınırı aşıldı ({options.MaxDepth}); kalan düğümler okunmadı.");
            return;
        }

        var line = GetLineNumber(element);
        var namespaceUri = element.Name.NamespaceName;
        var elementContext = string.IsNullOrEmpty(namespaceUri) ? path : $"{path} (ns: {namespaceUri})";

        // 1) Attribute değerleri
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue; // xmlns bildirimleri metin değildir
            }

            var attributePath = $"{path}/@{attribute.Name.LocalName}";
            var value = attribute.Value;
            var skipReason = DescribeSkipReason(attribute.Name.LocalName, value);

            AddEntry(options, result, usedKeys, new LocalizationTextEntry
            {
                Key = UniqueKey(attributePath, usedKeys),
                SourceText = value,
                Kind = LocalizationTextKind.XmlAttribute,
                Status = skipReason is null
                    ? LocalizationEntryStatus.Translatable
                    : LocalizationEntryStatus.Skipped,
                SkipReason = skipReason,
                Context = path,
                Line = line,
                Placeholders = skipReason is null ? PlaceholderScanner.Scan(value) : PlaceholderScanResult.Empty,
                Evidence = $"XML attribute değeri: {attributePath}",
                FormatKey = FormatKey
            });
        }

        // 2) Alt elementler veya element metni
        if (element.HasElements)
        {
            var textIndex = 0;

            foreach (var node in element.Nodes())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // XCData da XText'ten türediği için CDATA içeriği bu dalda korunur.
                if (node is XText textNode)
                {
                    textIndex++;
                    AddTextNodeEntry(
                        textNode,
                        $"{path}/text()[{textIndex}]",
                        elementContext,
                        options,
                        result,
                        usedKeys);
                }
                else if (node is XElement child)
                {
                    Walk(child, path + "/" + ChildSegment(element, child), depth + 1, options, result, usedKeys, cancellationToken);
                }

                if (result.Truncated)
                {
                    return;
                }
            }

            return;
        }

        // Yaprak element: metin düğümü (CDATA dahil), dosyadaki hâliyle.
        var text = element.Value;
        var blank = string.IsNullOrWhiteSpace(text);

        AddEntry(options, result, usedKeys, new LocalizationTextEntry
        {
            Key = UniqueKey(path, usedKeys),
            SourceText = text,
            Kind = LocalizationTextKind.XmlElementText,
            Status = blank ? LocalizationEntryStatus.Skipped : LocalizationEntryStatus.Translatable,
            SkipReason = blank ? "Boş veya yalnızca boşluk içeren element" : null,
            Context = elementContext,
            Line = line,
            Placeholders = blank ? PlaceholderScanResult.Empty : PlaceholderScanner.Scan(text),
            Evidence = $"XML element metni: {path}",
            FormatKey = FormatKey
        });
    }

    private static void AddTextNodeEntry(
        XText textNode,
        string path,
        string elementContext,
        LocalizationFormatReadOptions options,
        LocalizationTextReadResult result,
        HashSet<string> usedKeys)
    {
        var text = textNode.Value;

        if (string.IsNullOrEmpty(text) || IsFormattingWhitespace(text))
        {
            return;
        }

        var blank = string.IsNullOrWhiteSpace(text);

        AddEntry(options, result, usedKeys, new LocalizationTextEntry
        {
            Key = UniqueKey(path, usedKeys),
            SourceText = text,
            Kind = LocalizationTextKind.XmlElementText,
            Status = blank ? LocalizationEntryStatus.Skipped : LocalizationEntryStatus.Translatable,
            SkipReason = blank ? "Boş veya yalnızca boşluk içeren element" : null,
            Context = elementContext,
            Line = GetLineNumber(textNode),
            Placeholders = blank ? PlaceholderScanResult.Empty : PlaceholderScanner.Scan(text),
            Evidence = $"XML metin düğümü: {path}",
            FormatKey = FormatKey
        });
    }

    /// <summary>
    /// Satır sonu içeren salt-whitespace düğümlerini XML girintisi sayar.
    /// Aynı satırdaki boşluklar anlamlı olabileceğinden atlanmaz; Skipped kayıt olarak korunur.
    /// </summary>
    private static bool IsFormattingWhitespace(string text)
        => string.IsNullOrWhiteSpace(text) && (text.Contains('\n') || text.Contains('\r'));

    /// <summary>Yapısal veya sayısal attribute değerlerini çeviri adayı saymaz.</summary>
    private static string? DescribeSkipReason(string attributeName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Boş attribute değeri";
        }

        if (StructuralAttributeNames.Contains(attributeName))
        {
            return $"Yapısal attribute ('{attributeName}') çeviri metni değil";
        }

        if (IsNumericOrBoolean(value))
        {
            return "Sayısal/boolean değer çeviri adayı değil";
        }

        return null;
    }

    private static bool IsNumericOrBoolean(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    /// <summary>Aynı adlı kardeşler arasında 1 tabanlı index ile benzersiz yol üretir.</summary>
    private static string ChildSegment(XElement parent, XElement child)
    {
        var sameName = parent.Elements(child.Name).ToList();

        if (sameName.Count <= 1)
        {
            return child.Name.LocalName;
        }

        var index = sameName.IndexOf(child) + 1;

        return $"{child.Name.LocalName}[{index}]";
    }

    private static int? GetLineNumber(XObject node)
    {
        if (node is IXmlLineInfo info && info.HasLineInfo())
        {
            return info.LineNumber;
        }

        return null;
    }

    private static void AddEntry(
        LocalizationFormatReadOptions options,
        LocalizationTextReadResult result,
        HashSet<string> usedKeys,
        LocalizationTextEntry entry)
    {
        _ = usedKeys;

        if (result.Entries.Count >= options.MaxEntries)
        {
            result.Truncated = true;
            result.Warnings.Add($"Kayıt sınırı aşıldı ({options.MaxEntries}); kalan düğümler okunmadı.");
            return;
        }

        result.Entries.Add(entry);
    }

    private static string UniqueKey(string baseKey, HashSet<string> usedKeys)
    {
        if (usedKeys.Add(baseKey))
        {
            return baseKey;
        }

        var suffix = 2;

        while (!usedKeys.Add($"{baseKey}#{suffix}"))
        {
            suffix++;
        }

        return $"{baseKey}#{suffix}";
    }
}

