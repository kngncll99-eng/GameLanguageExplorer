namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Translation;

public class TranslationAcceptancePolicyTests
{
    [Fact]
    public void OrdinaryTranslation_IsCandidate()
    {
        var result = TranslationAcceptancePolicy.Evaluate("Open inventory", "Envanteri aç");

        Assert.Equal(TranslationAcceptanceStatus.Candidate, result.Status);
        Assert.True(result.IsAccepted);
        Assert.Equal("Envanteri aç", result.TranslationText);
        Assert.Empty(result.Findings);
        Assert.Empty(result.ProtectionIssues);
        Assert.False(string.IsNullOrWhiteSpace(result.DecisionReason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void EmptyOrWhitespaceTranslation_IsNeedsReview(string? translation)
    {
        var result = TranslationAcceptancePolicy.Evaluate("Open inventory", translation);

        Assert.Equal(TranslationAcceptanceStatus.NeedsReview, result.Status);
        Assert.False(result.IsAccepted);
        Assert.False(string.IsNullOrWhiteSpace(result.DecisionReason));
        Assert.Contains(
            result.Findings,
            i => i.Code is TranslationQualityIssueCode.EmptyTranslation
                or TranslationQualityIssueCode.WhitespaceOnlyTranslation);
    }

    [Theory]
    [InlineData(
        "Merhaba {PlayerName}", "Merhaba",
        TranslationQualityIssueCode.MissingPlaceholder)]
    [InlineData(
        "Merhaba", "Merhaba {0}",
        TranslationQualityIssueCode.ExtraPlaceholder)]
    [InlineData(
        "{PlayerName}", "{0}",
        TranslationQualityIssueCode.PlaceholderKindChanged)]
    [InlineData(
        "%s", "%d",
        TranslationQualityIssueCode.PlaceholderFormatChanged)]
    [InlineData(
        "{0} ve {0}", "{0}",
        TranslationQualityIssueCode.PlaceholderCountMismatch)]
    [InlineData(
        "{0} {1}", "{1} {0}",
        TranslationQualityIssueCode.PlaceholderOrderChanged)]
    public void PlaceholderFailures_AreFailedAndNeverAccepted(
        string source,
        string translation,
        TranslationQualityIssueCode expectedCode)
    {
        var result = TranslationAcceptancePolicy.Evaluate(source, translation);

        Assert.Equal(TranslationAcceptanceStatus.Failed, result.Status);
        Assert.False(result.IsAccepted);
        Assert.Null(result.TranslationText);
        Assert.Contains(result.Findings, i => i.Code == expectedCode);
        Assert.False(string.IsNullOrWhiteSpace(result.DecisionReason));
    }

    [Fact]
    public void SameAsSource_IsCandidateWithInformationalFinding()
    {
        var result = TranslationAcceptancePolicy.Evaluate("Ciri", "Ciri");

        Assert.Equal(TranslationAcceptanceStatus.Candidate, result.Status);
        Assert.Equal("Ciri", result.TranslationText);
        var issue = Assert.Single(result.Findings);
        Assert.Equal(TranslationQualityIssueCode.SameAsSource, issue.Code);
        Assert.Equal(TranslationQualityIssueSeverity.Info, issue.Severity);
    }

    [Fact]
    public void InformationalLengthFinding_IsPreservedAndStillCandidate()
    {
        var result = TranslationAcceptancePolicy.Evaluate(
            new string('a', 100),
            new string('b', 10));

        Assert.Equal(TranslationAcceptanceStatus.Candidate, result.Status);
        var issue = Assert.Single(result.Findings);
        Assert.Equal(TranslationQualityIssueCode.UnusualLengthDifference, issue.Code);
        Assert.Equal(TranslationQualityIssueSeverity.Info, issue.Severity);
    }

    [Fact]
    public void TemplateContextEmptyTranslation_StillNeedsReview()
    {
        var options = new TranslationQualityOptions
        {
            Context = TranslationQualityContext.UntranslatedTemplate
        };

        var result = TranslationAcceptancePolicy.Evaluate(
            "Merhaba {0}",
            string.Empty,
            options);

        Assert.Equal(TranslationAcceptanceStatus.NeedsReview, result.Status);
        Assert.False(result.IsAccepted);
        Assert.Empty(result.Findings);
        Assert.False(string.IsNullOrWhiteSpace(result.DecisionReason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptySource_IsNeedsReview(string? source)
    {
        var result = TranslationAcceptancePolicy.Evaluate(source, "Çeviri");

        Assert.Equal(TranslationAcceptanceStatus.NeedsReview, result.Status);
        Assert.False(result.IsAccepted);
    }

    [Fact]
    public void InvalidPlaceholderProtection_IsFailed_AndProtectionIssuesArePreserved()
    {
        const string source = "Merhaba {0}";
        var protection = PlaceholderProtector.Protect(source);
        var restoration = PlaceholderProtector.Restore("Merhaba", protection);

        Assert.False(restoration.IsValid);

        var result = TranslationAcceptancePolicy.Evaluate(source, restoration);

        Assert.Equal(TranslationAcceptanceStatus.Failed, result.Status);
        Assert.Null(result.TranslationText);
        Assert.NotEmpty(result.ProtectionIssues);
        Assert.Contains(
            result.ProtectionIssues,
            i => i.Type == PlaceholderRestorationIssueType.MissingSentinel);
        Assert.Contains(
            result.Findings,
            i => i.Code == TranslationQualityIssueCode.MissingPlaceholder);
    }

    [Fact]
    public void ValidProtectedTranslation_IsCandidateWithRestoredPlaceholder()
    {
        const string source = "Open {0}";
        var protection = PlaceholderProtector.Protect(source);
        var translatedMasked = "Aç " + protection.Placeholders[0].Sentinel;
        var restoration = PlaceholderProtector.Restore(translatedMasked, protection);

        Assert.True(restoration.IsValid);

        var result = TranslationAcceptancePolicy.Evaluate(source, restoration);

        Assert.Equal(TranslationAcceptanceStatus.Candidate, result.Status);
        Assert.Equal("Aç {0}", result.TranslationText);
        Assert.Empty(result.ProtectionIssues);
        Assert.True(result.Quality.PlaceholderComparison.IsMatch);
    }

    [Fact]
    public void ProtectionForDifferentSource_IsRejected()
    {
        var protection = PlaceholderProtector.Protect("Merhaba {0}");
        var restoration = PlaceholderProtector.Restore(protection.MaskedText, protection);
        Assert.True(restoration.IsValid);

        Assert.Throws<ArgumentException>(
            () => TranslationAcceptancePolicy.Evaluate("Başka {0}", restoration));
    }

    [Fact]
    public void AllQualityFindings_ArePreservedInTheAcceptanceResult()
    {
        var result = TranslationAcceptancePolicy.Evaluate(
            "Merhaba {PlayerName}",
            "Merhaba");

        Assert.Equal(TranslationAcceptanceStatus.Failed, result.Status);
        Assert.Equal(result.Quality.Issues, result.Findings);
        Assert.Contains(
            result.Findings,
            i => i.Code == TranslationQualityIssueCode.MissingPlaceholder
                && i.Severity == TranslationQualityIssueSeverity.Warning);
    }
}
