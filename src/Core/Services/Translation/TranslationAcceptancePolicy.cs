namespace GameLanguageExplorer.Core.Services.Translation;

using GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>Çeviri adayının işlem kararı.</summary>
public enum TranslationAcceptanceStatus
{
    /// <summary>Yalnızca bilgilendirici bulgular var; aday kullanılabilir.</summary>
    Candidate = 0,

    /// <summary>Boş çeviri veya engelleyici olmayan uyarı; insan incelemesi gerekir.</summary>
    NeedsReview = 1,

    /// <summary>Yapısal yer tutucu doğrulaması başarısız; otomatik kabul edilemez.</summary>
    Failed = 2
}

/// <summary>
/// Kabul kararı. Mevcut <see cref="TranslationQualityResult"/> bulguları olduğu gibi korunur;
/// koruma sorunları ayrıca taşınır. <see cref="TranslationText"/> yalnızca aday veya inceleme
/// durumunda doludur, başarısız çeviri metni kullanıma sunulmaz.
/// </summary>
public sealed record TranslationAcceptanceResult(
    TranslationAcceptanceStatus Status,
    string? TranslationText,
    TranslationQualityResult Quality,
    IReadOnlyList<PlaceholderRestorationIssue> ProtectionIssues,
    string DecisionReason)
{
    public bool IsAccepted => Status == TranslationAcceptanceStatus.Candidate;

    public IReadOnlyList<TranslationQualityIssue> Findings => Quality.Issues;
}

/// <summary>
/// <see cref="TranslationQualityValidator"/> ve <see cref="PlaceholderComparer"/> sonuçlarını
/// yorumlayarak adayı <see cref="TranslationAcceptanceStatus.Candidate"/>,
/// <see cref="TranslationAcceptanceStatus.NeedsReview"/> veya
/// <see cref="TranslationAcceptanceStatus.Failed"/> olarak sınıflandırır. Yer tutucu eksikliği,
/// fazlalığı, biçim/tür/adet değişimi veya sıra değişimi hiçbir zaman otomatik kabul edilmez.
/// </summary>
public static class TranslationAcceptancePolicy
{
    public static TranslationAcceptanceResult Evaluate(
        string? sourceText,
        string? translationText,
        TranslationQualityOptions? options = null)
    {
        var quality = TranslationQualityValidator.Validate(sourceText, translationText, options);

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return new TranslationAcceptanceResult(
                TranslationAcceptanceStatus.NeedsReview,
                translationText,
                quality,
                Array.Empty<PlaceholderRestorationIssue>(),
                "Kaynak metin boş; çeviri doğrulanamaz ve insan incelemesi gerekir.");
        }

        if (string.IsNullOrWhiteSpace(translationText))
        {
            return new TranslationAcceptanceResult(
                TranslationAcceptanceStatus.NeedsReview,
                translationText,
                quality,
                Array.Empty<PlaceholderRestorationIssue>(),
                "Çeviri boş veya yalnızca boşluk içeriyor; insan incelemesi gerekir.");
        }

        if (quality.Issues.Any(issue => IsPlaceholderFailure(issue.Code)))
        {
            return new TranslationAcceptanceResult(
                TranslationAcceptanceStatus.Failed,
                null,
                quality,
                Array.Empty<PlaceholderRestorationIssue>(),
                "Yer tutucu doğrulaması başarısız; çeviri otomatik kabul edilemez.");
        }

        if (quality.Issues.Any(issue => issue.Severity == TranslationQualityIssueSeverity.Warning))
        {
            return new TranslationAcceptanceResult(
                TranslationAcceptanceStatus.NeedsReview,
                translationText,
                quality,
                Array.Empty<PlaceholderRestorationIssue>(),
                "Kalite uyarıları var; insan incelemesi gerekir.");
        }

        var reason = quality.Issues.Count == 0
            ? "Engelleyici bulgu yok."
            : "Yalnızca bilgilendirici bulgular var; aday kullanılabilir.";

        return new TranslationAcceptanceResult(
            TranslationAcceptanceStatus.Candidate,
            translationText,
            quality,
            Array.Empty<PlaceholderRestorationIssue>(),
            reason);
    }

    /// <summary>
    /// Koruma sonucu doğrulanmamışsa çeviriyi <see cref="TranslationAcceptanceStatus.Failed"/>
    /// olarak döndürür ve koruma sorunlarını kaybetmez. Doğrulanmışsa normal kalite politikasına
    /// devreder.
    /// </summary>
    public static TranslationAcceptanceResult Evaluate(
        string? sourceText,
        PlaceholderRestorationResult restoration,
        TranslationQualityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(restoration);

        if (!string.Equals(sourceText, restoration.Protection.SourceText, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Kaynak metin, yer tutucu koruma sonucundaki kaynakla eşleşmiyor.",
                nameof(sourceText));
        }

        if (!restoration.IsValid)
        {
            var quality = TranslationQualityValidator.Validate(sourceText, null, options);
            return new TranslationAcceptanceResult(
                TranslationAcceptanceStatus.Failed,
                null,
                quality,
                restoration.Issues,
                "Yer tutucu koruması doğrulanamadı; çeviri otomatik kabul edilemez.");
        }

        var result = Evaluate(sourceText, restoration.Text, options);
        return result with { ProtectionIssues = restoration.Issues };
    }

    private static bool IsPlaceholderFailure(TranslationQualityIssueCode code) => code is
        TranslationQualityIssueCode.MissingPlaceholder
        or TranslationQualityIssueCode.ExtraPlaceholder
        or TranslationQualityIssueCode.PlaceholderKindChanged
        or TranslationQualityIssueCode.PlaceholderFormatChanged
        or TranslationQualityIssueCode.PlaceholderCountMismatch
        or TranslationQualityIssueCode.PlaceholderOrderChanged;
}
