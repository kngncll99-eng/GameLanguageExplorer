namespace GameLanguageExplorer.Core.Services.Localization;

using System.Text;
using System.Text.Json;

/// <summary>JSON içeriğinin yerelleştirme adayı olarak değerlendirilme durumu.</summary>
public enum JsonContentStatus
{
    /// <summary>İçerik incelenmedi (ör. çağıran örnek sağlamadı).</summary>
    NotInspected = 0,

    /// <summary>İçerik yerelleştirme metinleri içeriyor.</summary>
    LocalizationLike = 1,

    /// <summary>İçerikte metin var ancak yerelleştirme olduğu kesin değil.</summary>
    Ambiguous = 2,

    /// <summary>Geçerli JSON, ancak çevrilebilir metin değeri yok.</summary>
    NoText = 3,

    /// <summary>Geçersiz JSON (bozuk/yarım/derinlik aşımı).</summary>
    Invalid = 4,

    /// <summary>Dosya inceleme boyutu sınırını aşıyor; okunmadı.</summary>
    Oversized = 5,

    /// <summary>Dosya boş veya yalnızca boşluk içeriyor.</summary>
    Empty = 6,

    /// <summary>İçerik okunamadı (kodlama/erişim sorunu).</summary>
    ReadError = 7
}

/// <summary>
/// JSON içeriğinden çıkarılan sınırlı ve salt okunur sinyaller. Hiçbir zaman dosya
/// içeriğini saklamaz; yalnızca sayılar ve kısa bir açıklama taşır.
/// </summary>
public sealed record JsonContentAnalysis(
    JsonContentStatus Status,
    int TextValueCount = 0,
    int StringValueCount = 0,
    int ScalarValueCount = 0,
    string? Detail = null)
{
    /// <summary>Metin değerlerinin bulunduğu nesnelerde eşleşen ipucu anahtarları.</summary>
    public IReadOnlyList<string> PropertyHints { get; init; } = Array.Empty<string>();

    /// <summary>İçerik güçlü bir yerelleştirme sinyali veriyor mu?</summary>
    public bool HasLocalizationSignal => Status == JsonContentStatus.LocalizationLike;

    /// <summary>Metin değerlerinin tüm skaler değerlere oranı (0-1).</summary>
    public double TextRatio => ScalarValueCount == 0
        ? 0
        : (double)TextValueCount / ScalarValueCount;

    /// <summary>Kanıt/rapor metinlerinde kullanılacak kısa özet.</summary>
    public string Summary => Status switch
    {
        JsonContentStatus.LocalizationLike => $"{TextValueCount} metin değeri / {ScalarValueCount} skaler",
        JsonContentStatus.Ambiguous => $"{TextValueCount} metin değeri / {ScalarValueCount} skaler (belirsiz)",
        JsonContentStatus.NoText => "çevrilebilir metin değeri yok",
        JsonContentStatus.Invalid => "geçersiz JSON",
        JsonContentStatus.Oversized => "boyut sınırı nedeniyle incelenmedi",
        JsonContentStatus.Empty => "boş içerik",
        JsonContentStatus.ReadError => "içerik okunamadı",
        _ => "incelenmedi"
    };
}

/// <summary>
/// JSON dosyasının başındaki sınırlı içeriği inceleyerek yerelleştirme sinyali arar.
/// Yalnızca geçerli JSON kabul edilir; sayı/boolean/null değerleri metin sinyali sayılmaz.
/// Boyut sınırı aşılırsa içerik okunmaz ve çağıran güveni düşürmelidir.
/// </summary>
public static class JsonContentInspector
{
    /// <summary>İçerik incelemesi için varsayılan üst sınır (256 KB).</summary>
    public const long DefaultMaxBytes = 256 * 1024;

    private const int MaxDepth = 64;

    // Yalnızca yerelleştirmeyle güçlü ilişkisi olan anahtarlar. "name"/"title" gibi
    // genel meta veri anahtarları bilinçli olarak dışarıda bırakıldı.
    private static readonly HashSet<string> _propertyHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "texts", "string", "strings",
        "label", "labels", "message", "messages",
        "dialogue", "dialogues", "caption", "captions",
        "tooltip", "tooltips", "translation", "translations",
        "localization", "localisation", "subtitle", "subtitles",
        "prompt", "prompts", "ui", "locale", "language"
    };

    /// <summary>
    /// Verilen JSON metnini inceler. <paramref name="maxBytes"/> aşılırsa metin
    /// ayrıştırılmaz ve <see cref="JsonContentStatus.Oversized"/> döner.
    /// </summary>
    public static JsonContentAnalysis Inspect(string? json, long maxBytes = DefaultMaxBytes)
    {
        if (json is null)
        {
            return new JsonContentAnalysis(JsonContentStatus.NotInspected, Detail: "İçerik örneği yok.");
        }

        if (maxBytes <= 0)
        {
            return new JsonContentAnalysis(JsonContentStatus.Oversized,
                Detail: "İçerik inceleme sınırı geçersiz.");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonContentAnalysis(JsonContentStatus.Empty,
                Detail: "Dosya boş veya yalnızca boşluk içeriyor.");
        }

        if (Encoding.UTF8.GetByteCount(json) > maxBytes)
        {
            return new JsonContentAnalysis(JsonContentStatus.Oversized,
                Detail: $"JSON içeriği {maxBytes:N0} bayt inceleme sınırını aşıyor; okunmadı.");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = MaxDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });

            var stats = new Stats();
            Walk(document.RootElement, stats);
            return Classify(stats);
        }
        catch (JsonException ex)
        {
            return new JsonContentAnalysis(JsonContentStatus.Invalid,
                Detail: $"Geçersiz JSON: {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new JsonContentAnalysis(JsonContentStatus.Invalid,
                Detail: $"JSON incelenemedi: {ex.Message}");
        }
    }

    private static void Walk(JsonElement element, Stats stats)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (IsPropertyHint(property.Name))
                    {
                        stats.PropertyHints.Add(property.Name);
                    }

                    Walk(property.Value, stats);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, stats);
                }

                break;

            case JsonValueKind.String:
                stats.ScalarValueCount++;
                stats.StringValueCount++;

                var value = element.GetString();

                if (IsTextLike(value))
                {
                    stats.TextValueCount++;
                    stats.TotalTextLength += value!.Trim().Length;
                }

                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                stats.ScalarValueCount++;
                break;
        }
    }

    private static JsonContentAnalysis Classify(Stats stats)
    {
        if (stats.TextValueCount == 0)
        {
            return new JsonContentAnalysis(
                JsonContentStatus.NoText,
                stats.TextValueCount,
                stats.StringValueCount,
                stats.ScalarValueCount,
                "JSON içinde çevrilebilir metin değeri bulunamadı.");
        }

        var ratio = stats.ScalarValueCount == 0
            ? 0
            : (double)stats.TextValueCount / stats.ScalarValueCount;

        var averageLength = (double)stats.TotalTextLength / stats.TextValueCount;
        var hasHint = stats.PropertyHints.Count > 0;

        var localizationLike =
            (stats.TextValueCount >= 2 && averageLength >= 3 && ratio >= 0.5)
            || (stats.TextValueCount >= 2 && averageLength >= 2 && ratio >= 0.5 && hasHint)
            || (stats.TextValueCount >= 5 && ratio >= 0.35);

        if (localizationLike)
        {
            return new JsonContentAnalysis(
                JsonContentStatus.LocalizationLike,
                stats.TextValueCount,
                stats.StringValueCount,
                stats.ScalarValueCount,
                "JSON içeriği yerelleştirme metinleri içeriyor.")
            {
                PropertyHints = stats.PropertyHints.ToArray()
            };
        }

        return new JsonContentAnalysis(
            JsonContentStatus.Ambiguous,
            stats.TextValueCount,
            stats.StringValueCount,
            stats.ScalarValueCount,
            "JSON içeriği metin içeriyor ancak yerelleştirme olduğu kesin değil.")
        {
            PropertyHints = stats.PropertyHints.ToArray()
        };
    }
    private static bool IsPropertyHint(string propertyName)
    {
        foreach (var segment in SplitSegments(propertyName))
        {
            if (_propertyHints.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tek başına çevrilebilir metin olma olasılığı düşük dizeleri eler.</summary>
    private static bool IsTextLike(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        if (text.Length < 2 || !text.Any(char.IsLetter))
        {
            return false;
        }

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Contains('\\') || (text.Contains('/') && !text.Contains(' ')))
        {
            return false;
        }

        if (Guid.TryParse(text, out _))
        {
            return false;
        }

        // "{playerName}", "<color=red>" gibi yalnızca biçimlendirme/yer tutucu dizeleri.
        if ((text[0] is '{' or '<' or '[') && (text[^1] is '}' or '>' or ']'))
        {
            return false;
        }

        // Boşluksuz teknik anahtar/yol/sürüm benzeri dizeler ("ui.play", "v1.2.3").
        if (!text.Contains(' ')
            && text.Contains('.')
            && text.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'))
        {
            return false;
        }

        // Uzun, boşluksuz harf/rakam dizileri (kimlik, karma, kod).
        if (!text.Contains(' ')
            && text.Length >= 16
            && text.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<string> SplitSegments(string text)
    {
        var buffer = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer.Append(ch);
            }
            else if (buffer.Length > 0)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    private sealed class Stats
    {
        public int TextValueCount;
        public int StringValueCount;
        public int ScalarValueCount;
        public int TotalTextLength;
        public HashSet<string> PropertyHints { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}