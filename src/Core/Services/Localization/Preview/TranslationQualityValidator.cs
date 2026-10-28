namespace GameLanguageExplorer.Core.Services.Localization.Preview;

using System.Globalization;

/// <summary>Kalite bulgusunun bilgilendirme düzeyi; doğrulama sonucu engelleme kararı değildir.</summary>
public enum TranslationQualityIssueSeverity
{
    Info = 0,
    Warning = 1
}

/// <summary>Temel çeviri kalitesi bulgusunun kodu.</summary>
public enum TranslationQualityIssueCode
{
    EmptyTranslation = 0,
    WhitespaceOnlyTranslation = 1,
    SameAsSource = 2,
    UnusualLengthDifference = 3,
    MissingPlaceholder = 4,
    ExtraPlaceholder = 5,
    PlaceholderKindChanged = 6,
    PlaceholderFormatChanged = 7,
    PlaceholderCountMismatch = 8,
    PlaceholderOrderChanged = 9
}

/// <summary>Boş çevirinin anlamını belirleyen, çağıran tarafından verilen açık bağlam.</summary>
public enum TranslationQualityContext
{
    /// <summary>Çeviri beklenir; boş çeviri bulgu üretir. Varsayılan budur.</summary>
    TranslationExpected = 0,

    /// <summary>
    /// POT gibi çevirinin henüz boş bırakılması beklenen şablon. Validator bu bağlamı
    /// metinden, uzantıdan veya PO/POT biçiminden kendiliğinden tahmin etmez.
    /// </summary>
    UntranslatedTemplate = 1
}

/// <summary>Temel çeviri kalitesi denetimlerinin yapılandırılabilir sınırları.</summary>
public sealed class TranslationQualityOptions
{
    public TranslationQualityContext Context { get; init; } = TranslationQualityContext.TranslationExpected;

    /// <summary>Uzunluk oranı denetimine katılmak için gereken en az kaynak metin öğesi (varsayılan 20).</summary>
    public int MinimumSourceLengthForLengthCheck { get; init; } = 20;

    /// <summary>
    /// Oran sınırı aşılsa bile bulgu için gereken en az mutlak metin öğesi farkı (varsayılan 20).
    /// Kısa metinlerde birkaç karakterlik doğal farkların oran nedeniyle büyümesini engeller.
    /// </summary>
    public int MinimumAbsoluteLengthDifference { get; init; } = 20;

    /// <summary>
    /// Kabul edilen en küçük çeviri/kaynak uzunluk oranı (varsayılan 0,25).
    /// 0,25–4,0 aralığı ve 20 öğelik alt sınırlar bilinçli olarak geniştir; farklı yazı
    /// sistemlerinde yalnızca aşırı farklar bilgi bulgusu üretir. Bu sınırlar hata kanıtı değildir.
    /// </summary>
    public double MinimumLengthRatio { get; init; } = 0.25;

    /// <summary>Kabul edilen en büyük çeviri/kaynak uzunluk oranı (varsayılan 4,0).</summary>
    public double MaximumLengthRatio { get; init; } = 4.0;
}

/// <summary>
/// Tek bir salt okunur kalite bulgusu. Tam kaynak veya çeviri metnini kopyalamaz;
/// yalnızca ilgili tokenları ve sayısal uzunluk/adet ayrıntılarını taşır.
/// </summary>
public sealed record TranslationQualityIssue(
    TranslationQualityIssueCode Code,
    TranslationQualityIssueSeverity Severity,
    string Description)
{
    public PlaceholderToken? SourceToken { get; init; }
    public PlaceholderToken? TranslationToken { get; init; }
    public int? SourceCount { get; init; }
    public int? TranslationCount { get; init; }
    public int? SourceLength { get; init; }
    public int? TranslationLength { get; init; }
    public double? LengthRatio { get; init; }
}

/// <summary>Bir metin çiftinin kalite bulguları ve değiştirilmemiş placeholder karşılaştırması.</summary>
public sealed record TranslationQualityResult(
    PlaceholderComparisonResult PlaceholderComparison,
    IReadOnlyList<TranslationQualityIssue> Issues)
{
    public bool HasIssues => Issues.Count > 0;
}

/// <summary>
/// Kaynak ve çeviriyi değiştirmeden temel kalite bulguları üretir. Bulgular engelleyici değildir;
/// çeviriyi reddetme, düzeltme, yazma veya kabul etme kararı vermez.
/// </summary>
public static class TranslationQualityValidator
{
    /// <summary>
    /// Uzunluklar kullanıcıya görünen metin öğeleriyle ölçülür. Şablon bağlamında yalnızca
    /// <c>null</c>/boş çeviri için çeviriye bağlı bulgular atlanır; ham placeholder karşılaştırması
    /// yine sonuçta taşınır. Whitespace-only bir şablon çevirisi boş çeviri sayılmaz.
    /// </summary>
    public static TranslationQualityResult Validate(
        string? sourceText,
        string? translationText,
        TranslationQualityOptions? options = null)
    {
        options ??= new TranslationQualityOptions();
        ValidateOptions(options);

        var comparison = PlaceholderComparer.Compare(sourceText, translationText);
        if (options.Context == TranslationQualityContext.UntranslatedTemplate
            && string.IsNullOrEmpty(translationText))
        {
            return new TranslationQualityResult(comparison, Array.Empty<TranslationQualityIssue>());
        }

        var issues = new List<TranslationQualityIssue>();
        if (string.IsNullOrEmpty(translationText))
        {
            issues.Add(new TranslationQualityIssue(
                TranslationQualityIssueCode.EmptyTranslation,
                TranslationQualityIssueSeverity.Warning,
                "Çeviri boş."));
        }
        else if (string.IsNullOrWhiteSpace(translationText))
        {
            issues.Add(new TranslationQualityIssue(
                TranslationQualityIssueCode.WhitespaceOnlyTranslation,
                TranslationQualityIssueSeverity.Warning,
                "Çeviri yalnızca boşluk karakterlerinden oluşuyor."));
        }
        else
        {
            if (string.Equals(sourceText, translationText, StringComparison.Ordinal))
            {
                issues.Add(new TranslationQualityIssue(
                    TranslationQualityIssueCode.SameAsSource,
                    TranslationQualityIssueSeverity.Info,
                    "Çeviri kaynak metinle aynı; özel ad, sayı veya bilinçli kullanım olabilir."));
            }

            AddLengthIssue(sourceText, translationText, options, issues);
        }

        foreach (var comparisonIssue in comparison.Issues)
        {
            issues.Add(MapPlaceholderIssue(comparisonIssue));
        }

        return new TranslationQualityResult(comparison, issues);
    }

    private static void AddLengthIssue(
        string? sourceText,
        string translationText,
        TranslationQualityOptions options,
        ICollection<TranslationQualityIssue> issues)
    {
        if (sourceText is null)
        {
            return;
        }

        var sourceLength = new StringInfo(sourceText).LengthInTextElements;
        var translationLength = new StringInfo(translationText).LengthInTextElements;
        if (sourceLength == 0 || sourceLength < options.MinimumSourceLengthForLengthCheck)
        {
            return;
        }

        var absoluteDifference = Math.Abs(sourceLength - translationLength);
        if (absoluteDifference < options.MinimumAbsoluteLengthDifference)
        {
            return;
        }

        var ratio = translationLength / (double)sourceLength;
        if (ratio >= options.MinimumLengthRatio && ratio <= options.MaximumLengthRatio)
        {
            return;
        }

        issues.Add(new TranslationQualityIssue(
            TranslationQualityIssueCode.UnusualLengthDifference,
            TranslationQualityIssueSeverity.Info,
            "Çeviri/kaynak uzunluk oranı yapılandırılan sınırların dışında; dil farkı olabilir.")
        {
            SourceLength = sourceLength,
            TranslationLength = translationLength,
            LengthRatio = ratio
        });
    }

    private static TranslationQualityIssue MapPlaceholderIssue(PlaceholderComparisonIssue issue)
    {
        var (code, description) = issue.Type switch
        {
            PlaceholderComparisonIssueType.MissingToken => (
                TranslationQualityIssueCode.MissingPlaceholder,
                "Kaynak yer tutucu çeviride eksik."),
            PlaceholderComparisonIssueType.ExtraToken => (
                TranslationQualityIssueCode.ExtraPlaceholder,
                "Çeviride kaynakta olmayan yer tutucu var."),
            PlaceholderComparisonIssueType.TokenKindChanged => (
                TranslationQualityIssueCode.PlaceholderKindChanged,
                "Yer tutucu türü çeviride değişmiş."),
            PlaceholderComparisonIssueType.TokenFormatChanged => (
                TranslationQualityIssueCode.PlaceholderFormatChanged,
                "Yer tutucu biçimi çeviride değişmiş."),
            PlaceholderComparisonIssueType.TokenCountMismatch => (
                TranslationQualityIssueCode.PlaceholderCountMismatch,
                "Yer tutucu adedi kaynak ve çeviride farklı."),
            PlaceholderComparisonIssueType.TokenOrderChanged => (
                TranslationQualityIssueCode.PlaceholderOrderChanged,
                "Yer tutucu sırası strict-order politikasına göre değişmiş."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(issue), issue.Type, "Desteklenmeyen placeholder karşılaştırma sorunu.")
        };

        return new TranslationQualityIssue(
            code,
            TranslationQualityIssueSeverity.Warning,
            description)
        {
            SourceToken = issue.SourceToken,
            TranslationToken = issue.TranslationToken,
            SourceCount = issue.SourceCount,
            TranslationCount = issue.TranslationCount
        };
    }

    private static void ValidateOptions(TranslationQualityOptions options)
    {
        if (options.Context is not (TranslationQualityContext.TranslationExpected
            or TranslationQualityContext.UntranslatedTemplate))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Desteklenmeyen çeviri kalitesi bağlamı.");
        }

        if (options.MinimumSourceLengthForLengthCheck < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "En az kaynak uzunluğu negatif olamaz.");
        }

        if (options.MinimumAbsoluteLengthDifference < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "En az mutlak uzunluk farkı negatif olamaz.");
        }

        if (!double.IsFinite(options.MinimumLengthRatio) || options.MinimumLengthRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "En küçük uzunluk oranı sonlu ve pozitif olmalıdır.");
        }

        if (!double.IsFinite(options.MaximumLengthRatio)
            || options.MaximumLengthRatio < options.MinimumLengthRatio)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "En büyük uzunluk oranı sonlu ve en küçük orandan az olmamalıdır.");
        }
    }
}
