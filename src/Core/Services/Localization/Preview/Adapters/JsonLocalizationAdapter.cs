namespace GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;

using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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
public sealed class JsonLocalizationAdapter : ILocalizationFormatAdapter, ILocalizationWritableFormatAdapter
{
    public const string FormatKey = "JSON";

    public string FormatName => FormatKey;
    public string DisplayName => "JSON";
    public string[] SupportedExtensions => new[] { ".json" };
    public bool SupportsRead => true;
    public bool SupportsWrite => true;

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

    public Task<LocalizationWriteContentResult> BuildWriteContentAsync(
        string sourceFilePath,
        IReadOnlyList<LocalizationWriteTranslation> translations,
        LocalizationFormatWriteOptions? options = null,
        CancellationToken cancellationToken = default)
        => WritableLocalizationWriteSupport.BuildFromSourceAsync(
            sourceFilePath,
            translations,
            options,
            "JSON",
            (sourceText, writeOptions) => BuildWriteContent(sourceText, translations, writeOptions),
            cancellationToken);

    private static LocalizationWriteContentResult BuildWriteContent(
        string sourceText,
        IReadOnlyList<LocalizationWriteTranslation> translations,
        LocalizationFormatWriteOptions options)
    {
        try
        {
            var root = JsonNode.Parse(
                sourceText,
                nodeOptions: null,
                documentOptions: new JsonDocumentOptions
                {
                    MaxDepth = options.MaxDepth,
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false
                });

            if (root is null)
            {
                return LocalizationWriteContentResult.Fail("JSON kök değeri boş; yazma yapılmadı.");
            }

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var translation in translations)
            {
                if (string.IsNullOrWhiteSpace(translation.Key))
                {
                    return LocalizationWriteContentResult.Fail("Çeviri anahtarı boş olamaz.");
                }

                if (!seenKeys.Add(translation.Key))
                {
                    return LocalizationWriteContentResult.Fail(
                        $"Aynı JSON Pointer birden fazla kez yazılmak isteniyor: '{translation.Key}'.");
                }

                if (!TryResolvePointer(root, translation.Key, out var node, out var resolveError))
                {
                    return LocalizationWriteContentResult.Fail(resolveError!);
                }

                if (node is not JsonValue value || !value.TryGetValue<string>(out var currentText))
                {
                    return LocalizationWriteContentResult.Fail(
                        $"'{translation.Key}' konumu string değer değil; yazma reddedildi.");
                }

                // Kaynak metin önizlemeden sonra değişmişse yanlış yere yazmayı engelle.
                if (!string.Equals(currentText, translation.SourceText, StringComparison.Ordinal))
                {
                    return LocalizationWriteContentResult.Fail(
                        $"'{translation.Key}' kaynak metni değişmiş; yazma reddedildi.");
                }

                if (node.Parent is null)
                {
                    return LocalizationWriteContentResult.Fail("Kök JSON değeri yazılamaz.");
                }

                node.ReplaceWith(JsonValue.Create(translation.Translation));
            }

            // Girdi dosyası çok satırlıysa okunabilir biçimi korumaya çalış.
            var writeIndented = sourceText.Contains('\n', StringComparison.Ordinal);
            var json = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = writeIndented,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            return LocalizationWriteContentResult.Ok(json);
        }
        catch (JsonException ex)
        {
            return LocalizationWriteContentResult.Fail($"JSON yazma içeriği üretilemedi: {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return LocalizationWriteContentResult.Fail($"JSON yazma içeriği üretilemedi: {ex.Message}");
        }
    }

    /// <summary>RFC 6901 JSON Pointer'ı çözer; yalnızca mevcut string düğümlerine yazmaya izin verir.</summary>
    private static bool TryResolvePointer(
        JsonNode root,
        string pointer,
        out JsonNode? node,
        out string? error)
    {
        node = root;
        error = null;

        if (string.Equals(pointer, "(kök)", StringComparison.Ordinal) || pointer.Length == 0)
        {
            error = "Kök JSON değeri yazılamaz.";
            return false;
        }

        if (pointer[0] != '/')
        {
            error = $"Geçersiz JSON Pointer: '{pointer}'.";
            return false;
        }

        foreach (var rawSegment in pointer.Split('/').Skip(1))
        {
            var segment = rawSegment.Replace("~1", "/").Replace("~0", "~");

            if (node is JsonObject jsonObject)
            {
                if (!jsonObject.TryGetPropertyValue(segment, out node) || node is null)
                {
                    error = $"JSON Pointer bulunamadı: '{pointer}'.";
                    return false;
                }

                continue;
            }

            if (node is JsonArray jsonArray)
            {
                if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                    || index < 0
                    || index >= jsonArray.Count)
                {
                    error = $"JSON Pointer dizi indeksi geçersiz: '{pointer}'.";
                    return false;
                }

                node = jsonArray[index];

                if (node is null)
                {
                    error = $"JSON Pointer null değere işaret ediyor: '{pointer}'.";
                    return false;
                }

                continue;
            }

            error = $"JSON Pointer bir nesne/dizi üzerinden geçmiyor: '{pointer}'.";
            return false;
        }

        return true;
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


