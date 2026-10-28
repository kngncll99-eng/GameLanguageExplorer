namespace GameLanguageExplorer.Core.Services.Translation;

using System.Text;
using System.Text.Json;

/// <summary>
/// Katı çeviri yanıtı ayrıştırma sonucu. Hata mesajı ham yanıt gövdesini, kaynak metni veya
/// kimlik bilgisini içermez; yalnızca güvenli bir açıklama taşır.
/// </summary>
public sealed record TranslationResponseParseResult(
    bool IsSuccess,
    IReadOnlyList<TranslationCandidate> Candidates,
    string? Error)
{
    public static TranslationResponseParseResult Success(IReadOnlyList<TranslationCandidate> candidates) =>
        new(true, candidates, null);

    public static TranslationResponseParseResult Failure(string error) =>
        new(false, Array.Empty<TranslationCandidate>(), error);
}

/// <summary>
/// Modelin çeviri yanıtını yalnızca beklenen
/// <c>{"translations":[{"unitId":"string","text":"string"}]}</c> şemasıyla kabul eder.
/// Markdown kod bloklarını, JSON dışındaki metinleri, bilinmeyen alanları ve yinelenen JSON
/// alanlarını temizlemez veya tolere etmez.
///
/// <para>Boş veya yalnızca boşluk içeren <c>text</c> değerleri bu katmanda geçerli kabul edilir.
/// Kalite kararı <see cref="TranslationAcceptancePolicy"/> katmanına aittir; böylece boş çeviri
/// <see cref="TranslationAcceptanceStatus.NeedsReview"/> olarak raporlanabilir.</para>
/// </summary>
public static class TranslationResponseParser
{
    /// <summary>Kabul edilen en büyük UTF-8 yanıt boyutu (1 MiB).</summary>
    public const int MaximumResponseBytes = 1024 * 1024;

    private const int MaximumJsonDepth = 16;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumJsonDepth
    };

    /// <summary>
    /// Yanıtı ayrıştırır. <paramref name="expectedUnitIds"/> verilirse yanıt kümesi büyük/küçük
    /// harfe duyarlı olarak tam eşleşmelidir; eksik, bilinmeyen veya yinelenen kimlikler reddedilir.
    /// </summary>
    public static TranslationResponseParseResult Parse(
        string? json,
        IReadOnlyCollection<string>? expectedUnitIds = null,
        bool allowUnknownItemFields = false)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return TranslationResponseParseResult.Failure(
                "Yanıt boş; çeviri JSON'u bekleniyordu.");
        }

        if (Encoding.UTF8.GetByteCount(json) > MaximumResponseBytes)
        {
            return TranslationResponseParseResult.Failure(
                $"Yanıt {MaximumResponseBytes} baytlık boyut sınırını aşıyor.");
        }

        HashSet<string>? expected = null;
        if (expectedUnitIds is not null)
        {
            expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var unitId in expectedUnitIds)
            {
                if (string.IsNullOrWhiteSpace(unitId))
                {
                    throw new ArgumentException(
                        "Beklenen unitId değerleri boş olamaz.",
                        nameof(expectedUnitIds));
                }

                expected.Add(unitId);
            }
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException exception)
        {
            return TranslationResponseParseResult.Failure(
                "Yanıt geçerli JSON değil" +
                (exception.LineNumber is long line
                    ? $" (satır {line + 1}, konum {exception.BytePositionInLine + 1})."
                    : "."));
        }

        using (document)
        {
            return ParseDocument(document.RootElement, expected, allowUnknownItemFields);
        }
    }

    private static TranslationResponseParseResult ParseDocument(
        JsonElement root,
        HashSet<string>? expected,
        bool allowUnknownItemFields)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return TranslationResponseParseResult.Failure(
                "Yanıtın kökü bir JSON nesnesi olmalıdır.");
        }

        JsonElement translations = default;
        var hasTranslations = false;
        var rootProperties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in root.EnumerateObject())
        {
            if (!rootProperties.Add(property.Name))
            {
                return TranslationResponseParseResult.Failure(
                    "Kök nesnede yinelenen JSON alanı var.");
            }

            if (!property.NameEquals("translations"))
            {
                return TranslationResponseParseResult.Failure(
                    "Kök nesnede beklenmeyen bir alan var.");
            }

            translations = property.Value;
            hasTranslations = true;
        }

        if (!hasTranslations)
        {
            return TranslationResponseParseResult.Failure(
                "'translations' alanı eksik.");
        }

        if (translations.ValueKind != JsonValueKind.Array)
        {
            return TranslationResponseParseResult.Failure(
                "'translations' alanı bir dizi olmalıdır.");
        }

        var candidates = new List<TranslationCandidate>();
        var responseIds = new HashSet<string>(StringComparer.Ordinal);
        var itemIndex = 0;

        foreach (var item in translations.EnumerateArray())
        {
            var itemResult = ParseItem(
                item,
                itemIndex,
                expected,
                responseIds,
                allowUnknownItemFields);
            if (itemResult.Error is not null)
            {
                return itemResult.Error;
            }

            candidates.Add(itemResult.Candidate!);
            itemIndex++;
        }

        if (expected is not null)
        {
            var missingCount = expected.Count(unitId => !responseIds.Contains(unitId));
            if (missingCount > 0)
            {
                return TranslationResponseParseResult.Failure(
                    $"Yanıtta {missingCount} beklenen unitId eksik.");
            }
        }

        return TranslationResponseParseResult.Success(candidates);
    }

    private static (TranslationCandidate? Candidate, TranslationResponseParseResult Error) ParseItem(
        JsonElement item,
        int itemIndex,
        HashSet<string>? expected,
        HashSet<string> responseIds,
        bool allowUnknownItemFields)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return (null, TranslationResponseParseResult.Failure(
                $"'translations[{itemIndex}]' bir nesne olmalıdır."));
        }

        string? unitId = null;
        string? text = null;
        var hasUnitId = false;
        var hasText = false;
        var itemProperties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in item.EnumerateObject())
        {
            if (!itemProperties.Add(property.Name))
            {
                return (null, TranslationResponseParseResult.Failure(
                    $"'translations[{itemIndex}]' nesnesinde yinelenen JSON alanı var."));
            }

            if (property.NameEquals("unitId"))
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return (null, TranslationResponseParseResult.Failure(
                        $"'translations[{itemIndex}].unitId' bir metin olmalıdır."));
                }

                unitId = property.Value.GetString();
                hasUnitId = true;
            }
            else if (property.NameEquals("text"))
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return (null, TranslationResponseParseResult.Failure(
                        $"'translations[{itemIndex}].text' bir metin olmalıdır."));
                }

                text = property.Value.GetString();
                hasText = true;
            }
            else if (!allowUnknownItemFields)
            {
                return (null, TranslationResponseParseResult.Failure(
                    $"'translations[{itemIndex}]' nesnesinde beklenmeyen bir alan var."));
            }
        }

        if (!hasUnitId || !hasText || unitId is null || text is null)
        {
            return (null, TranslationResponseParseResult.Failure(
                $"'translations[{itemIndex}]' nesnesinde unitId ve text zorunludur."));
        }

        if (string.IsNullOrWhiteSpace(unitId))
        {
            return (null, TranslationResponseParseResult.Failure(
                $"'translations[{itemIndex}].unitId' boş olamaz."));
        }

        if (!responseIds.Add(unitId))
        {
            return (null, TranslationResponseParseResult.Failure(
                "Yanıtta yinelenen unitId var."));
        }

        if (expected is not null && !expected.Contains(unitId))
        {
            return (null, TranslationResponseParseResult.Failure(
                "Yanıt beklenmeyen bir unitId içeriyor."));
        }

        return (new TranslationCandidate(unitId, text), null!);
    }
}
