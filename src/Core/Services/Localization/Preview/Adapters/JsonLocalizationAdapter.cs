namespace GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// JSON ayrıştırıcısı: nesne/dizi hiyerarşisindeki <b>string</b> değerleri çıkarır.
///
/// <list type="bullet">
/// <item>Sayı, boolean ve null değerleri çeviri adayı olarak sunulmaz (atlanan olarak raporlanır).</item>
/// <item>Konum olarak RFC 6901 JSON Pointer kullanılır (<c>/dialogue/greeting</c>).</item>
/// <item>Aynı metin farklı konumlarda geçse bile kayıtlar birleştirilmez (anahtar konumdur).</item>
/// <item>Dosya asla yeniden serileştirilmez veya yazılmaz.</item>
/// </list>
/// </summary>
public sealed class JsonLocalizationAdapter : ILocalizationFormatAdapter
{
    public const string FormatKey = "JSON";

    public string FormatName => FormatKey;
    public string DisplayName => "JSON";
    public string[] SupportedExtensions => new[] { ".json" };
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
            using var document = JsonDocument.Parse(content.Text!, new JsonDocumentOptions
            {
                MaxDepth = options.MaxDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });

            var result = new LocalizationTextReadResult();
            var usedKeys = new HashSet<string>(StringComparer.Ordinal);

            Walk(document.RootElement, string.Empty, 0, options, result, usedKeys, cancellationToken);

            result.Duration = stopwatch.Elapsed;
            return result;
        }
        catch (JsonException ex)
        {
            var line = (ex.LineNumber ?? 0) + 1;
            var column = (ex.BytePositionInLine ?? 0) + 1;

            return LocalizationTextReadResult.Fail(
                LocalizationReadStatus.InvalidSyntax,
                $"Geçersiz JSON (satır {line}, sütun {column}): {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return LocalizationTextReadResult.Fail(
                LocalizationReadStatus.InvalidSyntax,
                $"JSON ayrıştırılamadı: {ex.Message}");
        }
    }

    private static void Walk(
        JsonElement element,
        string pointer,
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
            result.Warnings.Add($"JSON derinlik sınırı aşıldı ({options.MaxDepth}); kalan düğümler okunmadı.");
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, pointer + "/" + EscapePointerSegment(property.Name),
                        depth + 1, options, result, usedKeys, cancellationToken);

                    if (result.Truncated)
                    {
                        return;
                    }
                }

                break;

            case JsonValueKind.Array:
                var index = 0;

                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, pointer + "/" + index.ToString(CultureInfo.InvariantCulture),
                        depth + 1, options, result, usedKeys, cancellationToken);

                    if (result.Truncated)
                    {
                        return;
                    }

                    index++;
                }

                break;

            case JsonValueKind.String:
                AddStringEntry(element, pointer, options, result, usedKeys);
                break;

            case JsonValueKind.Number:
                AddSkipped(pointer, element.GetRawText(), "sayı", options, result, usedKeys, "JSON sayı değeri");
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                AddSkipped(pointer, element.GetRawText(), "boolean", options, result, usedKeys, "JSON boolean değeri");
                break;

            case JsonValueKind.Null:
                AddSkipped(pointer, "null", "null", options, result, usedKeys, "JSON null değeri");
                break;
        }
    }

    private static void AddStringEntry(
        JsonElement element,
        string pointer,
        LocalizationFormatReadOptions options,
        LocalizationTextReadResult result,
        HashSet<string> usedKeys)
    {
        var text = element.GetString() ?? string.Empty;
        var displayPointer = DescribePointer(pointer);
        var isBlank = string.IsNullOrWhiteSpace(text);

        AddEntry(options, result, usedKeys, new LocalizationTextEntry
        {
            Key = UniqueKey(displayPointer, usedKeys),
            SourceText = text,
            Kind = LocalizationTextKind.JsonString,
            Status = isBlank ? LocalizationEntryStatus.Skipped : LocalizationEntryStatus.Translatable,
            SkipReason = isBlank ? "Boş veya yalnızca boşluk içeren değer" : null,
            Context = DescribePointer(ParentPointer(pointer)),
            Placeholders = isBlank ? PlaceholderScanResult.Empty : PlaceholderScanner.Scan(text),
            Evidence = $"JSON string değeri: {displayPointer}",
            FormatKey = FormatKey
        });
    }

    private static void AddSkipped(
        string pointer,
        string rawValue,
        string valueKind,
        LocalizationFormatReadOptions options,
        LocalizationTextReadResult result,
        HashSet<string> usedKeys,
        string evidencePrefix)
    {
        var displayPointer = DescribePointer(pointer);

        AddEntry(options, result, usedKeys, new LocalizationTextEntry
        {
            Key = UniqueKey(displayPointer, usedKeys),
            SourceText = rawValue,
            Kind = LocalizationTextKind.JsonString,
            Status = LocalizationEntryStatus.Skipped,
            SkipReason = $"{valueKind} değeri çeviri adayı değil",
            Context = DescribePointer(ParentPointer(pointer)),
            Placeholders = PlaceholderScanResult.Empty,
            Evidence = $"{evidencePrefix}: {displayPointer}",
            FormatKey = FormatKey
        });
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

    /// <summary>JSON Pointer kaçışı: <c>~</c> → <c>~0</c>, <c>/</c> → <c>~1</c>.</summary>
    private static string EscapePointerSegment(string segment)
        => segment.Replace("~", "~0").Replace("/", "~1");

    private static string ParentPointer(string pointer)
    {
        var lastSlash = pointer.LastIndexOf('/');

        return lastSlash <= 0 ? string.Empty : pointer[..lastSlash];
    }

    private static string DescribePointer(string pointer)
        => string.IsNullOrEmpty(pointer) ? "(kök)" : pointer;

    /// <summary>
    /// Aynı JSON Pointer iki kez üretilebilir (yinelenen özellik adları). Kayıtlar
    /// birleştirilmesin diye anahtar benzersizleştirilir.
    /// </summary>
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


