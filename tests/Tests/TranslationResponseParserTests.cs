namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Services.Translation;

public class TranslationResponseParserTests
{
    [Fact]
    public void ValidJson_ReturnsCandidatesInOrder()
    {
        const string json = """
            {"translations":[{"unitId":"u1","text":"Merhaba"},{"unitId":"u2","text":"Dünya"}]}
            """;

        var result = TranslationResponseParser.Parse(json, new[] { "u1", "u2" });

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Collection(
            result.Candidates,
            first =>
            {
                Assert.Equal("u1", first.UnitId);
                Assert.Equal("Merhaba", first.Text);
            },
            second =>
            {
                Assert.Equal("u2", second.UnitId);
                Assert.Equal("Dünya", second.Text);
            });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyResponse_IsRejected(string? json)
    {
        var result = TranslationResponseParser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Candidates);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void MalformedJson_IsRejected()
    {
        var result = TranslationResponseParser.Parse("""{"translations":[{"unitId":"u1"}]""");

        Assert.False(result.IsSuccess);
        Assert.Contains("geçerli JSON", result.Error);
    }

    [Fact]
    public void MarkdownCodeBlock_IsRejected()
    {
        const string json = """
            ```json
            {"translations":[{"unitId":"u1","text":"Merhaba"}]}
            ```
            """;

        var result = TranslationResponseParser.Parse(json, new[] { "u1" });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void SurroundingText_IsRejected()
    {
        const string json = """
            İşte çeviri:
            {"translations":[{"unitId":"u1","text":"Merhaba"}]}
            Umarım yardımcı olur.
            """;

        var result = TranslationResponseParser.Parse(json, new[] { "u1" });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void MissingTranslations_IsRejected()
    {
        var result = TranslationResponseParser.Parse("""{}""");

        Assert.False(result.IsSuccess);
        Assert.Contains("eksik", result.Error);
    }

    [Fact]
    public void UnknownRootField_IsRejected()
    {
        var result = TranslationResponseParser.Parse("""{"items":[]}""");

        Assert.False(result.IsSuccess);
        Assert.Contains("beklenmeyen", result.Error);
    }

    [Fact]
    public void DuplicateRootJsonField_IsRejected()
    {
        var result = TranslationResponseParser.Parse("""{"translations":[],"translations":[]}""");

        Assert.False(result.IsSuccess);
        Assert.Contains("yinelenen JSON alanı", result.Error);
    }

    [Fact]
    public void WrongTranslationsType_IsRejected()
    {
        var result = TranslationResponseParser.Parse("""{"translations":{}}""");

        Assert.False(result.IsSuccess);
        Assert.Contains("dizi", result.Error);
    }

    [Fact]
    public void RootArray_IsRejected()
    {
        var result = TranslationResponseParser.Parse("""[]""");

        Assert.False(result.IsSuccess);
        Assert.Contains("nesnesi", result.Error);
    }

    [Theory]
    [InlineData("""{"translations":[{"unitId":1,"text":"x"}]}""")]
    [InlineData("""{"translations":[{"unitId":"u1","text":null}]}""")]
    [InlineData("""{"translations":[{"unitId":null,"text":"x"}]}""")]
    [InlineData("""{"translations":["x"]}""")]
    public void WrongItemShapeOrType_IsRejected(string json)
    {
        var result = TranslationResponseParser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Candidates);
    }

    [Theory]
    [InlineData("""{"translations":[{"unitId":"","text":"x"}]}""")]
    [InlineData("""{"translations":[{"unitId":"   ","text":"x"}]}""")]
    public void EmptyUnitId_IsRejected(string json)
    {
        var result = TranslationResponseParser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Contains("unitId", result.Error);
    }

    [Fact]
    public void DuplicateUnitId_IsRejected()
    {
        const string json = """
            {"translations":[{"unitId":"u1","text":"a"},{"unitId":"u1","text":"b"}]}
            """;

        var result = TranslationResponseParser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Contains("yinelenen unitId", result.Error);
    }

    [Fact]
    public void DuplicateJsonField_IsRejected()
    {
        const string json = """
            {"translations":[{"unitId":"u1","unitId":"u1","text":"x"}]}
            """;

        var result = TranslationResponseParser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Contains("yinelenen JSON alanı", result.Error);
    }

    [Fact]
    public void UnknownItemField_IsRejected()
    {
        const string json = """
            {"translations":[{"unitId":"u1","text":"x","note":"extra"}]}
            """;

        var result = TranslationResponseParser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Contains("beklenmeyen", result.Error);
    }

    [Fact]
    public void ExpectedUnitIdMissing_IsRejected()
    {
        const string json = """
            {"translations":[{"unitId":"u1","text":"x"}]}
            """;

        var result = TranslationResponseParser.Parse(json, new[] { "u1", "u2" });

        Assert.False(result.IsSuccess);
        Assert.Contains("eksik", result.Error);
    }

    [Fact]
    public void UnknownUnitId_IsRejected()
    {
        const string json = """
            {"translations":[{"unitId":"u2","text":"x"}]}
            """;

        var result = TranslationResponseParser.Parse(json, new[] { "u1" });

        Assert.False(result.IsSuccess);
        Assert.Contains("beklenmeyen", result.Error);
    }

    [Fact]
    public void ExpectedUnitIdComparison_IsCaseSensitive()
    {
        const string json = """
            {"translations":[{"unitId":"U1","text":"x"}]}
            """;

        var result = TranslationResponseParser.Parse(json, new[] { "u1" });

        Assert.False(result.IsSuccess);
        Assert.Contains("beklenmeyen", result.Error);
    }

    [Fact]
    public void TolerantMode_IgnoresUnknownItemFieldsButKeepsIdValidation()
    {
        const string json = """
            {"translations":[{"unitId":"u1","text":"Merhaba","note":"model notu"}]}
            """;

        var tolerant = TranslationResponseParser.Parse(
            json,
            new[] { "u1" },
            allowUnknownItemFields: true);

        Assert.True(tolerant.IsSuccess);
        Assert.Equal("Merhaba", Assert.Single(tolerant.Candidates).Text);

        var strict = TranslationResponseParser.Parse(json, new[] { "u1" });
        Assert.False(strict.IsSuccess);

        var unknownId = TranslationResponseParser.Parse(
            """{"translations":[{"unitId":"u2","text":"Merhaba","note":"x"}]}""",
            new[] { "u1" },
            allowUnknownItemFields: true);
        Assert.False(unknownId.IsSuccess);
    }

    [Fact]
    public void EmptyTranslationText_IsAcceptedForQualityPolicy()
    {
        const string json = """
            {"translations":[{"unitId":"u1","text":""}]}
            """;

        var result = TranslationResponseParser.Parse(json, new[] { "u1" });

        Assert.True(result.IsSuccess);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(string.Empty, candidate.Text);
    }

    [Fact]
    public void WhitespaceOnlyTranslationText_IsAcceptedForQualityPolicy()
    {
        const string json = """
            {"translations":[{"unitId":"u1","text":"   "}]}
            """;

        var result = TranslationResponseParser.Parse(json, new[] { "u1" });

        Assert.True(result.IsSuccess);
        Assert.Equal("   ", Assert.Single(result.Candidates).Text);
    }

    [Fact]
    public void ResponseExceedingSizeLimit_IsRejected()
    {
        var json = new string('x', TranslationResponseParser.MaximumResponseBytes + 1);

        var result = TranslationResponseParser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Contains("sınır", result.Error);
    }
}
