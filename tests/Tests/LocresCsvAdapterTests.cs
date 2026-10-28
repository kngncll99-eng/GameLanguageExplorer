namespace GameLanguageExplorer.Tests;

using System.Text;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;

// ─────────────────────────────────────────────────────────────────────────────
// LocresCSV (key,source,Translation) salt okunur ayrıştırıcısı
// ─────────────────────────────────────────────────────────────────────────────

public class LocresCsvLocalizationAdapterTests
{
    private const string Key1 = "4A6FDB1549E45F6C5D8D739129686E2F";
    private const string Key2 = "0B1C2D3E4F5061728394A5B6C7D8E9F0";

    private static async Task<LocalizationTextReadResult> ReadAsync(
        TempTree tree, string content, string name = "game_locres.csv")
        => await new LocresCsvLocalizationAdapter().ReadAsync(tree.Write(name, content));

    [Fact]
    public async Task BasicFile_MapsKeySourceAndTranslationSeparately()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        key,source,Translation
        {Key1},Welcome back,
        {Key2},"Quest completed",Görev tamamlandı
        """);

        Assert.True(result.Success, result.ErrorMessage);

        var first = result.Entries[0];
        Assert.Equal(Key1, first.Key);                       // anahtar, kaynak metin DEĞİL
        Assert.Equal("Welcome back", first.SourceText);      // kaynak metin
        Assert.Null(first.ExistingTranslation);              // boş çeviri = çeviri yok
        Assert.Equal(LocalizationTextKind.LocresCsvEntry, first.Kind);
        Assert.Equal(LocresCsvLocalizationAdapter.FormatKey, first.FormatKey);
        Assert.Equal(LocalizationEntryStatus.Translatable, first.Status);

        var second = result.Entries[1];
        Assert.Equal(Key2, second.Key);
        Assert.Equal("Quest completed", second.SourceText);
        Assert.Equal("Görev tamamlandı", second.ExistingTranslation);
    }

    [Fact]
    public async Task ColumnOrder_IsResolvedByName_NotByPosition()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        source,Translation,key
        "Play",Oyna,{Key1}
        """);

        var entry = Assert.Single(result.Entries);

        Assert.Equal(Key1, entry.Key);
        Assert.Equal("Play", entry.SourceText);
        Assert.Equal("Oyna", entry.ExistingTranslation);
    }

    [Fact]
    public async Task Headers_AreCaseInsensitive_AndWhitespaceTolerant()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
         " KEY " , Source ,translation
        {Key1},Settings,Ayarlar
        """);

        Assert.True(result.Success, result.ErrorMessage);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(Key1, entry.Key);
        Assert.Equal("Settings", entry.SourceText);
        Assert.Equal("Ayarlar", entry.ExistingTranslation);
    }

    [Fact]
    public async Task EmptyTranslation_MeansNoTranslation_NotAnError()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        key,source,Translation
        {Key1},Inventory,
        """);

        Assert.True(result.Success);

        var entry = Assert.Single(result.Entries);
        Assert.Null(entry.ExistingTranslation);
        Assert.Equal(LocalizationEntryStatus.Translatable, entry.Status);
    }

    [Fact]
    public async Task SourceContainingComma_IsReadFromQuotedField()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        key,source,Translation
        {Key1},"Hello, traveler, welcome","Merhaba, yolcu, hoş geldin"
        """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Hello, traveler, welcome", entry.SourceText);
        Assert.Equal("Merhaba, yolcu, hoş geldin", entry.ExistingTranslation);
    }

    [Fact]
    public async Task EscapedQuotes_AreUnescaped()
    {
        using var tree = new TempTree();

        // Ham dize sınırlayıcısı """ ile çakışmaması için normal dize kullanıldı.
        var content = "key,source,Translation\n"
                      + $"{Key1},\"He said \"\"hello\"\"\",O \"merhaba\" dedi\n";

        var result = await ReadAsync(tree, content);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("He said \"hello\"", entry.SourceText);
        Assert.Equal("O \"merhaba\" dedi", entry.ExistingTranslation);
    }

    [Fact]
    public async Task MultiLineFields_AreKeptIntact()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        key,source,Translation
        {Key1},"Line one
        Line two","Satır bir
        Satır iki"
        """);

        var entry = Assert.Single(result.Entries);

        Assert.Contains("Line one", entry.SourceText);
        Assert.Contains("Line two", entry.SourceText);
        Assert.Contains("Satır iki", entry.ExistingTranslation!);
        Assert.Equal(2, entry.Line); // başlık 1. satır, kayıt 2. satırda başlar
    }

    [Fact]
    public async Task MeaningfulWhitespace_IsPreservedInSourceAndTranslation()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        key,source,Translation
        {Key1},"  padded source  ","  padded translation  "
        """);

        var entry = Assert.Single(result.Entries);

        Assert.Equal("  padded source  ", entry.SourceText);
        Assert.Equal("  padded translation  ", entry.ExistingTranslation);
    }

    [Fact]
    public async Task LiteralQuotes_InsideText_ArePreserved()
    {
        using var tree = new TempTree();

        // CSV'de """quoted""" alanı, çözüldükten sonra "quoted" değerini verir.
        var content = "key,source,Translation\n"
                      + $"{Key1},\"\"\"quoted source\"\"\",\"\"\"alıntı\"\"\"\n";

        var result = await ReadAsync(tree, content);

        var entry = Assert.Single(result.Entries);

        Assert.Equal("\"quoted source\"", entry.SourceText);
        Assert.Equal("\"alıntı\"", entry.ExistingTranslation);
    }

    [Fact]
    public async Task WhitespaceOnlyTranslation_IsKeptDistinctFromEmptyTranslation()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        key,source,Translation
        {Key1},Inventory,"   "
        """);

        var entry = Assert.Single(result.Entries);

        Assert.Equal("   ", entry.ExistingTranslation);

        var quality = TranslationQualityValidator.Validate(entry.SourceText, entry.ExistingTranslation);

        Assert.Contains(quality.Issues,
            issue => issue.Code == TranslationQualityIssueCode.WhitespaceOnlyTranslation);
        Assert.DoesNotContain(quality.Issues,
            issue => issue.Code == TranslationQualityIssueCode.EmptyTranslation);
    }

    [Fact]
    public async Task CrlfInsideQuotedFields_IsPreserved()
    {
        using var tree = new TempTree();

        var content = "key,source,Translation\r\n"
                      + $"{Key1},\"Line one\r\nLine two\",\"Satır bir\r\nSatır iki\"\r\n";

        var result = await ReadAsync(tree, content);

        var entry = Assert.Single(result.Entries);

        Assert.Equal("Line one\r\nLine two", entry.SourceText);
        Assert.Equal("Satır bir\r\nSatır iki", entry.ExistingTranslation);
    }

    [Fact]
    public async Task EmptyKey_IsSkippedWithReason_AndNotSilentlyDropped()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        key,source,Translation
        ,Orphan text,
        """);

        var entry = Assert.Single(result.Entries);

        Assert.Equal(LocalizationEntryStatus.Skipped, entry.Status);
        Assert.Contains("Anahtar boş", entry.SkipReason!);
        Assert.Contains("satır", entry.Key);           // konum bilgisi korunur
        Assert.Equal("Orphan text", entry.SourceText); // metin kaybolmaz
    }

    [Fact]
    public async Task EmptySource_IsSkippedWithReason()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        key,source,Translation
        {Key1},,
        """);

        var entry = Assert.Single(result.Entries);

        Assert.Equal(LocalizationEntryStatus.Skipped, entry.Status);
        Assert.Contains("Kaynak metin boş", entry.SkipReason!);
        Assert.Equal(Key1, entry.Key);
    }

    [Fact]
    public async Task DuplicateKeys_ArePreserved_AndReported()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        key,source,Translation
        {Key1},First,
        {Key1},Second,İkinci
        """);

        Assert.Equal(2, result.Entries.Count); // birleştirilmedi, silinmedi
        Assert.All(result.Entries, e => Assert.Equal(Key1, e.Key));
        Assert.Equal("First", result.Entries[0].SourceText);
        Assert.Equal("Second", result.Entries[1].SourceText);
        Assert.DoesNotContain("duplicate-key", result.Entries[0].Flags);
        Assert.Contains("duplicate-key", result.Entries[1].Flags);
        Assert.Contains(result.Warnings, w => w.Contains("yinelenen", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OrdinaryCsv_WithOtherHeaders_IsRejected()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, """
        name,value
        player_name,Player
        """);

        Assert.False(result.Success);
        Assert.Equal(LocalizationReadStatus.InvalidSyntax, result.Status);
        Assert.Contains("LocresCSV", result.ErrorMessage!);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void OrdinaryCsvHeader_DoesNotMatchSniffer()
    {
        var adapter = new LocresCsvLocalizationAdapter();

        Assert.False(adapter.MatchesHeader("plain.csv", "name,value\nA,B"));
        Assert.False(adapter.MatchesHeader("plain.csv", "id,text,comment\n1,Hello,note"));
        Assert.False(adapter.MatchesHeader("plain.csv", ""));
        Assert.True(adapter.MatchesHeader("locres.csv", "key,source,Translation"));
        Assert.True(adapter.MatchesHeader("locres.csv", "source,Translation,key"));
    }

    [Fact]
    public async Task BrokenCsv_IsReportedWithoutCrashing()
    {
        using var tree = new TempTree();

        // Kapanmayan tırnak: sessizce yutulmaz, uyarı üretilir.
        var result = await ReadAsync(tree, $"""
        key,source,Translation
        {Key1},"Unterminated text,
        """);

        Assert.Contains(result.Warnings, w =>
            w.Contains("tırnak", StringComparison.OrdinalIgnoreCase) ||
            w.Contains("bozuk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Utf8Bom_IsHandled()
    {
        using var tree = new TempTree();

        var path = tree.Write("bom_locres.csv", "placeholder");

        // İçeriği BOM ile yeniden yaz: BOM başlığa/anahtara sızmamalı.
        System.IO.File.WriteAllText(
            path,
            $"key,source,Translation\n{Key1},Settings,Ayarlar\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = await new LocresCsvLocalizationAdapter().ReadAsync(path);

        Assert.True(result.Success, result.ErrorMessage);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(Key1, entry.Key);
        Assert.Equal("Settings", entry.SourceText);

        var head = LocalizationFileReader.ReadHead(path);
        Assert.NotNull(head);
        Assert.True(new LocresCsvLocalizationAdapter().MatchesHeader(path, head!));
    }

    [Fact]
    public async Task MissingTranslationColumn_IsRejected()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        key,source
        {Key1},Settings
        """);

        Assert.False(result.Success);
        Assert.Equal(LocalizationReadStatus.InvalidSyntax, result.Status);
    }

    [Fact]
    public async Task ExtraColumns_AreIgnoredWithWarning()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        key,source,Translation,hash
        {Key1},Settings,Ayarlar,ABC123
        """);

        Assert.True(result.Success);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Settings", entry.SourceText);
        Assert.Contains(result.Warnings, w => w.Contains("fazladan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NumericSource_IsAmbiguous()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, $"""
        key,source,Translation
        {Key1},120,
        """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(LocalizationEntryStatus.Ambiguous, entry.Status);
    }

    [Fact]
    public async Task UndocumentedKeyFormat_IsFlaggedButKept()
    {
        using var tree = new TempTree();

        var result = await ReadAsync(tree, """
        key,source,Translation
        short_key,Settings,Ayarlar
        """);

        var entry = Assert.Single(result.Entries);

        Assert.Equal(LocalizationEntryStatus.Translatable, entry.Status); // kayıt atılmaz
        Assert.Equal("short_key", entry.Key);
        Assert.Contains("key-format", entry.Flags);
        Assert.Contains(result.Warnings, w => w.Contains("32"));
    }

    [Theory]
    [InlineData(';')]
    [InlineData('|')]
    public async Task AlternativeDelimiters_AreSupported(char delimiter)
    {
        using var tree = new TempTree();

        var content = $"key{delimiter}source{delimiter}Translation\n"
                      + $"{Key1}{delimiter}Settings{delimiter}Ayarlar\n";

        var result = await ReadAsync(tree, content);

        Assert.True(result.Success, result.ErrorMessage);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(Key1, entry.Key);
        Assert.Equal("Settings", entry.SourceText);
        Assert.Equal("Ayarlar", entry.ExistingTranslation);
    }

    [Fact]
    public void Adapter_IsReadOnly_AndClaimsOnlyCsv()
    {
        var adapter = new LocresCsvLocalizationAdapter();

        Assert.True(adapter.SupportsRead);
        Assert.False(adapter.SupportsWrite);
        Assert.Equal(new[] { ".csv" }, adapter.SupportedExtensions);
        Assert.Equal(LocresCsvLocalizationAdapter.FormatKey, adapter.FormatName);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Kayıt defteri: başlıkla ayırt etme + mevcut CSV/TSV davranışının korunması
// ─────────────────────────────────────────────────────────────────────────────

public class LocresCsvRegistryTests
{
    private const string Key1 = "4A6FDB1549E45F6C5D8D739129686E2F";

    [Fact]
    public void FindByHeader_SelectsLocresCsv_WhenSchemaMatches()
    {
        var registry = PreviewTestHelper.CreateRegistry();

        var adapter = registry.FindByHeader("x.csv", $"key,source,Translation\n{Key1},Settings,Ayarlar\n");

        Assert.NotNull(adapter);
        Assert.Equal(LocresCsvLocalizationAdapter.FormatKey, adapter!.FormatName);
    }

    [Fact]
    public void FindByHeader_ReturnsNull_ForOrdinaryCsv()
    {
        var registry = PreviewTestHelper.CreateRegistry();

        Assert.Null(registry.FindByHeader("x.csv", "name,value\nA,B\n"));
    }

    [Fact]
    public void ExtensionDefault_IsStillCsv()
    {
        var registry = PreviewTestHelper.CreateRegistry();

        // Uzantı önceliği CSV'de kalır: mevcut davranış bozulmaz.
        Assert.Equal("CSV", registry.FindByExtension(".csv")!.FormatName);

        var csvAdapters = registry.FindAllByExtension(".csv");
        Assert.Equal(2, csvAdapters.Count);
        Assert.True(registry.IsExtensionAmbiguous(".csv"));
        Assert.False(registry.IsExtensionAmbiguous(".tsv"));
        Assert.False(registry.IsExtensionAmbiguous(".po"));
    }

    [Fact]
    public async Task PreviewService_SelectsLocresCsv_EndToEnd()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var filePath = tree.Write(
            "game_locres.csv",
            $"key,source,Translation\n{Key1},Settings,Ayarlar\n");

        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, filePath, "CSV");

        var service = PreviewTestHelper.CreatePreviewService(database);
        var result = await service.PreviewAsync(installId, fileId);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(LocresCsvLocalizationAdapter.FormatKey, result.FormatKey);
        Assert.Equal("LocresCSV", result.AdapterName);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(Key1, entry.Key);              // anahtar
        Assert.Equal("Settings", entry.SourceText); // kaynak metin
        Assert.Equal("Ayarlar", entry.ExistingTranslation);
    }

    [Fact]
    public async Task PreviewService_StillUsesPlainCsv_ForOrdinaryCsv()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var filePath = tree.Write("plain.csv", "name,value\nplayer,Player\n");

        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, filePath, "CSV");

        var service = PreviewTestHelper.CreatePreviewService(database);
        var result = await service.PreviewAsync(installId, fileId);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("CSV", result.FormatKey);
        Assert.Equal("CSV", result.AdapterName);
        Assert.DoesNotContain(result.Entries, e => e.Kind == LocalizationTextKind.LocresCsvEntry);
    }
}
