namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Services.Localization.Preview;

public class PlaceholderComparerTests
{
    [Fact]
    public void IdenticalTokens_InSameOrder_AreCompatible()
    {
        var result = PlaceholderComparer.Compare(
            "Merhaba {PlayerName}: {0:N2}, %s, <b>önemli</b>",
            "Selam {PlayerName}: {0:N2}, %s, <b>önemli</b>");

        Assert.True(result.IsMatch);
        Assert.Empty(result.Issues);
        Assert.Equal(PlaceholderOrderPolicy.Strict, result.OrderPolicy);
        Assert.Equal(result.SourceScan.TypedSignature, result.TranslationScan.TypedSignature);
    }

    [Fact]
    public void MissingToken_IsReported()
    {
        var result = PlaceholderComparer.Compare(
            "Merhaba {PlayerName}, seviye {0}",
            "Merhaba, seviye {0}");

        var issue = Assert.Single(result.Issues);
        Assert.False(result.IsMatch);
        Assert.Equal(PlaceholderComparisonIssueType.MissingToken, issue.Type);
        Assert.Equal("{PlayerName}", issue.SourceToken?.Value);
        Assert.Null(issue.TranslationToken);
        Assert.Equal(1, issue.SourceCount);
        Assert.Equal(0, issue.TranslationCount);
    }

    [Fact]
    public void ExtraToken_IsReported()
    {
        var result = PlaceholderComparer.Compare(
            "Merhaba {PlayerName}",
            "Merhaba {PlayerName}, seviye {0}");

        var issue = Assert.Single(result.Issues);
        Assert.False(result.IsMatch);
        Assert.Equal(PlaceholderComparisonIssueType.ExtraToken, issue.Type);
        Assert.Null(issue.SourceToken);
        Assert.Equal("{0}", issue.TranslationToken?.Value);
        Assert.Equal(0, issue.SourceCount);
        Assert.Equal(1, issue.TranslationCount);
    }

    [Fact]
    public void TokenKindChange_IsReported()
    {
        var result = PlaceholderComparer.Compare("Merhaba {PlayerName}", "Merhaba {0}");

        var issue = Assert.Single(result.Issues);
        Assert.Equal(PlaceholderComparisonIssueType.TokenKindChanged, issue.Type);
        Assert.Equal(PlaceholderKind.Named, issue.SourceToken?.Kind);
        Assert.Equal(PlaceholderKind.Positional, issue.TranslationToken?.Kind);
    }

    [Fact]
    public void TokenFormatChange_IsReported()
    {
        var result = PlaceholderComparer.Compare("Değer: %s", "Değer: %d");

        var issue = Assert.Single(result.Issues);
        Assert.Equal(PlaceholderComparisonIssueType.TokenFormatChanged, issue.Type);
        Assert.Equal("%s", issue.SourceToken?.Value);
        Assert.Equal("%d", issue.TranslationToken?.Value);
        Assert.Equal(PlaceholderKind.Printf, issue.SourceToken?.Kind);
        Assert.Equal(PlaceholderKind.Printf, issue.TranslationToken?.Kind);
    }

    [Theory]
    [InlineData("{0} ve {0}", "{0}", 2, 1)]
    [InlineData("{0}", "{0} ve {0}", 1, 2)]
    public void RepeatedTokenCountMismatch_IsReported(
        string source,
        string translation,
        int expectedSourceCount,
        int expectedTranslationCount)
    {
        var result = PlaceholderComparer.Compare(source, translation);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(PlaceholderComparisonIssueType.TokenCountMismatch, issue.Type);
        Assert.Equal("{0}", issue.SourceToken?.Value);
        Assert.Equal("{0}", issue.TranslationToken?.Value);
        Assert.Equal(expectedSourceCount, issue.SourceCount);
        Assert.Equal(expectedTranslationCount, issue.TranslationCount);
    }

    [Theory]
    [InlineData("{PlayerName} ve {0}", "{0} ve {PlayerName}")]
    [InlineData("%s ardından %d", "%d ardından %s")]
    public void OrderChange_IsReportedUnderDefaultStrictPolicy(string source, string translation)
    {
        var result = PlaceholderComparer.Compare(source, translation);

        var issue = Assert.Single(result.Issues);
        Assert.False(result.IsMatch);
        Assert.Equal(PlaceholderOrderPolicy.Strict, result.OrderPolicy);
        Assert.Equal(PlaceholderComparisonIssueType.TokenOrderChanged, issue.Type);
    }

    [Theory]
    [InlineData("%s", PlaceholderKind.Printf)]
    [InlineData("%1$s", PlaceholderKind.Printf)]
    [InlineData("%%", PlaceholderKind.Printf)]
    [InlineData("{0:N2}", PlaceholderKind.Positional)]
    [InlineData("{PlayerName}", PlaceholderKind.Named)]
    [InlineData(@"\n", PlaceholderKind.Escape)]
    [InlineData("<b>", PlaceholderKind.Tag)]
    [InlineData("[color=red]", PlaceholderKind.BracketTag)]
    public void SupportedTokenForms_AreScannedAndCompared(string token, PlaceholderKind expectedKind)
    {
        var result = PlaceholderComparer.Compare(
            $"Kaynak {token} metni",
            $"Çeviri {token} metni");

        Assert.True(result.IsMatch);
        Assert.Empty(result.Issues);
        Assert.Equal(expectedKind, Assert.Single(result.SourceScan.Tokens).Kind);
        Assert.Equal(expectedKind, Assert.Single(result.TranslationScan.Tokens).Kind);
        Assert.Equal(token, result.SourceScan.Tokens[0].Value);
        Assert.Equal(token, result.TranslationScan.Tokens[0].Value);
    }

    [Fact]
    public void EmptySourceAndTranslation_AreCompatibleAndReportNoTokens()
    {
        var result = PlaceholderComparer.Compare(null, string.Empty);

        Assert.True(result.IsMatch);
        Assert.Empty(result.Issues);
        Assert.True(result.SourceHasNoTokens);
        Assert.True(result.TranslationHasNoTokens);
    }

    [Fact]
    public void PlainTextWithoutTokens_IsPlaceholderCompatibleEvenWhenTextDiffers()
    {
        var result = PlaceholderComparer.Compare("Kaynak metin", "Tamamen farklı çeviri");

        Assert.True(result.IsMatch);
        Assert.Empty(result.Issues);
        Assert.True(result.SourceHasNoTokens);
        Assert.True(result.TranslationHasNoTokens);
    }

    [Fact]
    public void EmptyTranslation_ReportsMissingTokensAndTranslationNoTokenState()
    {
        var result = PlaceholderComparer.Compare("Merhaba {PlayerName}", string.Empty);

        Assert.Equal(PlaceholderComparisonIssueType.MissingToken, Assert.Single(result.Issues).Type);
        Assert.False(result.SourceHasNoTokens);
        Assert.True(result.TranslationHasNoTokens);
    }

    [Fact]
    public void EmptySource_ReportsExtraTokensAndSourceNoTokenState()
    {
        var result = PlaceholderComparer.Compare(string.Empty, "Merhaba {PlayerName}");

        Assert.Equal(PlaceholderComparisonIssueType.ExtraToken, Assert.Single(result.Issues).Type);
        Assert.True(result.SourceHasNoTokens);
        Assert.False(result.TranslationHasNoTokens);
    }

    [Fact]
    public void UnsupportedOrIncompleteTokenLikeText_IsNotInventedAsAnIssue()
    {
        var result = PlaceholderComparer.Compare(
            "Tanınmayan %z ve {kapanmayan metin",
            "Tamamen düz çeviri");

        Assert.True(result.IsMatch);
        Assert.Empty(result.Issues);
        Assert.True(result.SourceHasNoTokens);
        Assert.True(result.TranslationHasNoTokens);
    }

    [Fact]
    public void SameTokenTwiceOnBothSides_IsCompatible()
    {
        var result = PlaceholderComparer.Compare(
            "{PlayerName} / {PlayerName}",
            "Bir {PlayerName}, iki {PlayerName}");

        Assert.True(result.IsMatch);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void RepeatedTokenWithOneFormatChange_IsNotReducedToOnlyCountMismatch()
    {
        var result = PlaceholderComparer.Compare("%s / %s", "%s / %d");

        var issue = Assert.Single(result.Issues);
        Assert.Equal(PlaceholderComparisonIssueType.TokenFormatChanged, issue.Type);
        Assert.Equal("%s", issue.SourceToken?.Value);
        Assert.Equal("%d", issue.TranslationToken?.Value);
    }
}
