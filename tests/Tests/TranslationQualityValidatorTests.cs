namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Services.Localization.Preview;

public class TranslationQualityValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyTranslation_IsReportedAsWarning(string? translation)
    {
        var result = TranslationQualityValidator.Validate("Kaynak metin", translation);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationQualityIssueCode.EmptyTranslation, issue.Code);
        Assert.Equal(TranslationQualityIssueSeverity.Warning, issue.Severity);
        Assert.False(string.IsNullOrWhiteSpace(issue.Description));
    }

    [Fact]
    public void WhitespaceOnlyTranslation_IsDistinctFromEmptyTranslation()
    {
        var result = TranslationQualityValidator.Validate("Kaynak metin", " \t\r\n");

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationQualityIssueCode.WhitespaceOnlyTranslation, issue.Code);
        Assert.Equal(TranslationQualityIssueSeverity.Warning, issue.Severity);
    }

    [Fact]
    public void OrdinaryTranslatedText_ProducesNoFindings()
    {
        var result = TranslationQualityValidator.Validate("Open inventory", "Envanteri aç");

        Assert.Empty(result.Issues);
        Assert.False(result.HasIssues);
        Assert.True(result.PlaceholderComparison.IsMatch);
    }

    [Fact]
    public void SameAsSource_IsInformationalBecauseItCanBeIntentional()
    {
        var result = TranslationQualityValidator.Validate("Ciri", "Ciri");

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationQualityIssueCode.SameAsSource, issue.Code);
        Assert.Equal(TranslationQualityIssueSeverity.Info, issue.Severity);
    }

    [Fact]
    public void TranslationDifferentFromSource_DoesNotProduceSameAsSourceFinding()
    {
        var result = TranslationQualityValidator.Validate("Settings", "Ayarlar");

        Assert.DoesNotContain(result.Issues, i => i.Code == TranslationQualityIssueCode.SameAsSource);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("OK", "Bu çeviri kısa kaynak için bilerek çok daha uzun yazılmıştır.")]
    [InlineData("12345", "1")]
    [InlineData("Ankara", "İstanbul")]
    public void ShortNumbersAndProperNames_DoNotProduceLengthFindings(
        string source,
        string translation)
    {
        var result = TranslationQualityValidator.Validate(source, translation);

        Assert.Empty(result.Issues);
    }

    [Fact]
    public void DifferentWritingSystemAtConfiguredBoundary_DoesNotProduceLengthFinding()
    {
        var result = TranslationQualityValidator.Validate(new string('a', 80), new string('界', 20));

        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ExtremelyShortTranslation_ProducesInformationalLengthFindingWithSafeDetails()
    {
        var result = TranslationQualityValidator.Validate(new string('a', 100), new string('b', 10));

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationQualityIssueCode.UnusualLengthDifference, issue.Code);
        Assert.Equal(TranslationQualityIssueSeverity.Info, issue.Severity);
        Assert.Equal(100, issue.SourceLength);
        Assert.Equal(10, issue.TranslationLength);
        Assert.NotNull(issue.LengthRatio);
        Assert.Equal(0.1, issue.LengthRatio.Value, 10);
        Assert.Null(issue.SourceToken);
        Assert.Null(issue.TranslationToken);
    }

    [Fact]
    public void ExtremelyLongTranslation_ProducesInformationalLengthFinding()
    {
        var result = TranslationQualityValidator.Validate(new string('a', 20), new string('b', 100));

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationQualityIssueCode.UnusualLengthDifference, issue.Code);
        Assert.Equal(TranslationQualityIssueSeverity.Info, issue.Severity);
        Assert.Equal(5.0, issue.LengthRatio);
    }

    [Fact]
    public void LengthThresholds_AreConfigurableAndValuesWithinThemAreAccepted()
    {
        var options = new TranslationQualityOptions
        {
            MinimumSourceLengthForLengthCheck = 4,
            MinimumAbsoluteLengthDifference = 2,
            MinimumLengthRatio = 0.5,
            MaximumLengthRatio = 2.0
        };

        var outside = TranslationQualityValidator.Validate("abcdefgh", "abc", options);
        var inside = TranslationQualityValidator.Validate("abcdefgh", "abcdef", options);

        Assert.Equal(
            TranslationQualityIssueCode.UnusualLengthDifference,
            Assert.Single(outside.Issues).Code);
        Assert.Empty(inside.Issues);
    }

    [Theory]
    [InlineData(
        "A {PlayerName}", "B",
        PlaceholderComparisonIssueType.MissingToken,
        TranslationQualityIssueCode.MissingPlaceholder)]
    [InlineData(
        "A", "B {0}",
        PlaceholderComparisonIssueType.ExtraToken,
        TranslationQualityIssueCode.ExtraPlaceholder)]
    [InlineData(
        "A {PlayerName}", "B {0}",
        PlaceholderComparisonIssueType.TokenKindChanged,
        TranslationQualityIssueCode.PlaceholderKindChanged)]
    [InlineData(
        "A %s", "B %d",
        PlaceholderComparisonIssueType.TokenFormatChanged,
        TranslationQualityIssueCode.PlaceholderFormatChanged)]
    [InlineData(
        "A {0} {0}", "B {0}",
        PlaceholderComparisonIssueType.TokenCountMismatch,
        TranslationQualityIssueCode.PlaceholderCountMismatch)]
    [InlineData(
        "A {PlayerName} {0}", "B {0} {PlayerName}",
        PlaceholderComparisonIssueType.TokenOrderChanged,
        TranslationQualityIssueCode.PlaceholderOrderChanged)]
    public void PlaceholderIssues_AreMappedWithCodesSeverityAndDetails(
        string source,
        string translation,
        PlaceholderComparisonIssueType expectedComparisonType,
        TranslationQualityIssueCode expectedQualityCode)
    {
        var result = TranslationQualityValidator.Validate(source, translation);

        var comparisonIssue = Assert.Single(result.PlaceholderComparison.Issues);
        var qualityIssue = Assert.Single(result.Issues);
        Assert.Equal(expectedComparisonType, comparisonIssue.Type);
        Assert.Equal(expectedQualityCode, qualityIssue.Code);
        Assert.Equal(TranslationQualityIssueSeverity.Warning, qualityIssue.Severity);
        Assert.False(string.IsNullOrWhiteSpace(qualityIssue.Description));
        Assert.Equal(comparisonIssue.SourceToken, qualityIssue.SourceToken);
        Assert.Equal(comparisonIssue.TranslationToken, qualityIssue.TranslationToken);
        Assert.Equal(comparisonIssue.SourceCount, qualityIssue.SourceCount);
        Assert.Equal(comparisonIssue.TranslationCount, qualityIssue.TranslationCount);
        Assert.Null(qualityIssue.SourceLength);
        Assert.Null(qualityIssue.TranslationLength);
        Assert.Null(qualityIssue.LengthRatio);
    }

    [Fact]
    public void MatchingPlaceholders_DoNotProduceQualityFindings()
    {
        var result = TranslationQualityValidator.Validate(
            "Merhaba {PlayerName}",
            "Selam {PlayerName}");

        Assert.Empty(result.Issues);
        Assert.True(result.PlaceholderComparison.IsMatch);
    }

    [Fact]
    public void DefaultContext_DoesNotAssumeEmptyTranslationIsAnExpectedTemplateValue()
    {
        var result = TranslationQualityValidator.Validate("Merhaba {PlayerName}", string.Empty);

        Assert.Equal(
            new[]
            {
                TranslationQualityIssueCode.EmptyTranslation,
                TranslationQualityIssueCode.MissingPlaceholder
            },
            result.Issues.Select(i => i.Code));
    }

    [Fact]
    public void ExplicitTemplateContext_AllowsEmptyTranslationWithoutHidingRawComparison()
    {
        var options = new TranslationQualityOptions
        {
            Context = TranslationQualityContext.UntranslatedTemplate
        };

        var result = TranslationQualityValidator.Validate("Merhaba {PlayerName}", string.Empty, options);

        Assert.Empty(result.Issues);
        Assert.False(result.PlaceholderComparison.IsMatch);
        Assert.Equal(
            PlaceholderComparisonIssueType.MissingToken,
            Assert.Single(result.PlaceholderComparison.Issues).Type);
    }

    [Fact]
    public void ExplicitTemplateContext_DoesNotTreatWhitespaceOnlyTranslationAsEmpty()
    {
        var options = new TranslationQualityOptions
        {
            Context = TranslationQualityContext.UntranslatedTemplate
        };

        var result = TranslationQualityValidator.Validate("Kaynak metin", "  ", options);

        Assert.Equal(
            TranslationQualityIssueCode.WhitespaceOnlyTranslation,
            Assert.Single(result.Issues).Code);
    }
}
