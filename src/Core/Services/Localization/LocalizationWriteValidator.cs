namespace GameLanguageExplorer.Core.Services.Localization;

using GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>
/// Tüm yazma yollarında (AI, editör, doğrudan servis çağrısı) uygulanan ortak
/// yapısal doğrulama. Yer tutucu/etiket kaybı yazmayı engeller; uzunluk veya
/// "kaynakla aynı" gibi stil uyarıları engellemez.
/// </summary>
public static class LocalizationWriteValidator
{
    public static string? Validate(string key, string sourceText, string translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
        {
            return $"'{key}' için boş çeviri yazılamaz.";
        }

        if (string.IsNullOrEmpty(sourceText))
        {
            return null;
        }

        var comparison = PlaceholderComparer.Compare(sourceText, translation);

        if (comparison.IsMatch)
        {
            return null;
        }

        var details = string.Join(
            "; ",
            comparison.Issues.Select(Describe));

        return $"'{key}' çevirisi yapısal yer tutucu doğrulamasını geçemedi: {details}.";
    }

    private static string Describe(PlaceholderComparisonIssue issue)
        => issue.Type switch
        {
            PlaceholderComparisonIssueType.MissingToken =>
                $"eksik yer tutucu '{issue.SourceToken?.Value}'",
            PlaceholderComparisonIssueType.ExtraToken =>
                $"kaynakta olmayan yer tutucu '{issue.TranslationToken?.Value}'",
            PlaceholderComparisonIssueType.TokenKindChanged =>
                $"yer tutucu türü değişti ('{issue.SourceToken?.Value}' → '{issue.TranslationToken?.Value}')",
            PlaceholderComparisonIssueType.TokenFormatChanged =>
                $"yer tutucu biçimi değişti ('{issue.SourceToken?.Value}' → '{issue.TranslationToken?.Value}')",
            PlaceholderComparisonIssueType.TokenCountMismatch =>
                $"yer tutucu adedi uyuşmuyor ('{issue.SourceToken?.Value}')",
            PlaceholderComparisonIssueType.TokenOrderChanged =>
                "yer tutucu sırası değişti",
            _ => "bilinmeyen yer tutucu sorunu"
        };
}
