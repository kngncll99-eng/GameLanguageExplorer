namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Translation;

public class PlaceholderProtectorTests
{
    [Theory]
    [InlineData("{0}")]
    [InlineData("{PlayerName}")]
    [InlineData("%s")]
    [InlineData("%1$s")]
    [InlineData("%%")]
    [InlineData(@"\n")]
    [InlineData(@"\t")]
    [InlineData("<b>")]
    [InlineData("</b>")]
    [InlineData("[color=red]")]
    [InlineData("{0:N2}")]
    public void ProtectAndRestore_SupportedPlaceholderKinds(string token)
    {
        var source = $"Önce {token} sonra";
        var protection = PlaceholderProtector.Protect(source);

        var placeholder = Assert.Single(protection.Placeholders);
        Assert.Equal(token, placeholder.Token.Value);
        Assert.False(protection.MaskedText.Contains(token, StringComparison.Ordinal));
        Assert.True(protection.MaskedText.Contains(placeholder.Sentinel, StringComparison.Ordinal));

        var restoration = PlaceholderProtector.Restore(protection.MaskedText, protection);

        Assert.True(restoration.IsValid);
        Assert.Empty(restoration.Issues);
        Assert.Equal(source, restoration.Text);
        Assert.NotNull(restoration.Comparison);
        Assert.True(restoration.Comparison!.IsMatch);
    }

    [Fact]
    public void RepeatedPlaceholder_GetsDistinctSentinels_AndRestoresEveryOccurrence()
    {
        const string source = "{0} ve {0}";
        var protection = PlaceholderProtector.Protect(source);

        Assert.Equal(2, protection.Placeholders.Count);
        Assert.NotEqual(protection.Placeholders[0].Sentinel, protection.Placeholders[1].Sentinel);

        var restoration = PlaceholderProtector.Restore(protection.MaskedText, protection);

        Assert.True(restoration.IsValid);
        Assert.Equal(source, restoration.Text);
    }

    [Fact]
    public void DifferentScopes_ProduceDifferentSentinels_ForSameSource()
    {
        var first = PlaceholderProtector.Protect("Merhaba {0}", "unit-1");
        var sameScope = PlaceholderProtector.Protect("Merhaba {0}", "unit-1");
        var second = PlaceholderProtector.Protect("Merhaba {0}", "unit-2");

        Assert.Equal(first.Placeholders[0].Sentinel, sameScope.Placeholders[0].Sentinel);
        Assert.NotEqual(first.Placeholders[0].Sentinel, second.Placeholders[0].Sentinel);
    }

    [Fact]
    public void SourceContainingSentinelLikeText_DoesNotCollideWithGeneratedSentinels()
    {
        const string literal = "__GLE_PH_00000000_0000__";
        var source = $"{literal} ve {{0}}";
        var protection = PlaceholderProtector.Protect(source);

        Assert.NotEqual("__GLE_PH_", protection.SentinelPrefix);
        Assert.True(protection.MaskedText.Contains(literal, StringComparison.Ordinal));

        var restoration = PlaceholderProtector.Restore(protection.MaskedText, protection);

        Assert.True(restoration.IsValid);
        Assert.Equal(source, restoration.Text);
    }

    [Fact]
    public void MissingSentinel_IsReported_AndUnverifiedResponseIsNotRestored()
    {
        var protection = PlaceholderProtector.Protect("Merhaba {PlayerName}");
        var restoration = PlaceholderProtector.Restore("Merhaba", protection);

        var issue = Assert.Single(restoration.Issues);
        Assert.Equal(PlaceholderRestorationIssueType.MissingSentinel, issue.Type);
        Assert.Equal(protection.Placeholders[0].Sentinel, issue.Sentinel);
        Assert.Equal(1, issue.ExpectedCount);
        Assert.Equal(0, issue.ActualCount);
        Assert.False(restoration.IsValid);
        Assert.Null(restoration.Text);
    }

    [Fact]
    public void ExtraSentinel_IsReported()
    {
        var protection = PlaceholderProtector.Protect("Merhaba {0}");
        var unknown = protection.SentinelPrefix + "deadbeef_9999__";

        var restoration = PlaceholderProtector.Restore(
            protection.MaskedText + " " + unknown,
            protection);

        var issue = Assert.Single(restoration.Issues);
        Assert.Equal(PlaceholderRestorationIssueType.ExtraSentinel, issue.Type);
        Assert.Equal(unknown, issue.Sentinel);
        Assert.Equal(0, issue.ExpectedCount);
        Assert.Equal(1, issue.ActualCount);
        Assert.False(restoration.IsValid);
        Assert.Null(restoration.Text);
    }

    [Fact]
    public void ModifiedSentinel_IsReported()
    {
        var protection = PlaceholderProtector.Protect("Merhaba {0}");
        var malformed = protection.SentinelPrefix + "bozuk";

        var restoration = PlaceholderProtector.Restore(
            protection.MaskedText + " " + malformed,
            protection);

        var issue = Assert.Single(restoration.Issues);
        Assert.Equal(PlaceholderRestorationIssueType.ModifiedSentinel, issue.Type);
        Assert.Equal(malformed, issue.Sentinel);
        Assert.False(restoration.IsValid);
        Assert.Null(restoration.Text);
    }

    [Fact]
    public void DuplicateSentinel_IsReported()
    {
        var protection = PlaceholderProtector.Protect("Merhaba {0}");
        var sentinel = protection.Placeholders[0].Sentinel;

        var restoration = PlaceholderProtector.Restore(
            protection.MaskedText + " " + sentinel,
            protection);

        var issue = Assert.Single(restoration.Issues);
        Assert.Equal(PlaceholderRestorationIssueType.DuplicateSentinel, issue.Type);
        Assert.Equal(sentinel, issue.Sentinel);
        Assert.Equal(1, issue.ExpectedCount);
        Assert.Equal(2, issue.ActualCount);
        Assert.False(restoration.IsValid);
        Assert.Null(restoration.Text);
    }

    [Fact]
    public void ReorderedSentinels_AreRestoredButRejectedByStrictPlaceholderOrder()
    {
        var protection = PlaceholderProtector.Protect("{0} {1}");
        Assert.Equal(2, protection.Placeholders.Count);

        var reordered = protection.Placeholders[1].Sentinel
            + " "
            + protection.Placeholders[0].Sentinel;

        var restoration = PlaceholderProtector.Restore(reordered, protection);

        var issue = Assert.Single(restoration.Issues);
        Assert.Equal(PlaceholderRestorationIssueType.PlaceholderMismatch, issue.Type);
        Assert.Null(restoration.Text);
        Assert.NotNull(restoration.Comparison);
        Assert.False(restoration.Comparison!.IsMatch);
        Assert.Contains(
            restoration.Comparison.Issues,
            i => i.Type == PlaceholderComparisonIssueType.TokenOrderChanged);
    }

    [Fact]
    public void TranslationWithSourcePlaceholderInsteadOfSentinel_IsRejected()
    {
        var protection = PlaceholderProtector.Protect("Merhaba {0}");

        var restoration = PlaceholderProtector.Restore("Merhaba {0}", protection);

        var issue = Assert.Single(restoration.Issues);
        Assert.Equal(PlaceholderRestorationIssueType.MissingSentinel, issue.Type);
        Assert.False(restoration.IsValid);
        Assert.Null(restoration.Text);
    }

    [Fact]
    public void PlainTextWithoutPlaceholders_RestoresNormally()
    {
        var protection = PlaceholderProtector.Protect("Düz metin");
        var restoration = PlaceholderProtector.Restore("Çeviri", protection);

        Assert.False(protection.HasPlaceholders);
        Assert.Equal("Düz metin", protection.MaskedText);
        Assert.True(restoration.IsValid);
        Assert.Equal("Çeviri", restoration.Text);
    }

    [Fact]
    public void PlainSourceWithInjectedPlaceholder_IsRejected()
    {
        var protection = PlaceholderProtector.Protect("Düz metin");

        var restoration = PlaceholderProtector.Restore("Çeviri {0}", protection);

        var issue = Assert.Single(restoration.Issues);
        Assert.Equal(PlaceholderRestorationIssueType.PlaceholderMismatch, issue.Type);
        Assert.Null(restoration.Text);
    }

    [Fact]
    public void NullSource_ProducesEmptyProtection()
    {
        var protection = PlaceholderProtector.Protect(null);

        Assert.Equal(string.Empty, protection.SourceText);
        Assert.Equal(string.Empty, protection.MaskedText);
        Assert.False(protection.HasPlaceholders);

        var restoration = PlaceholderProtector.Restore(null, protection);

        Assert.True(restoration.IsValid);
        Assert.Null(restoration.Text);
    }

    [Fact]
    public void Restore_RejectsNullProtection()
    {
        Assert.Throws<ArgumentNullException>(() => PlaceholderProtector.Restore("x", null!));
    }
}
