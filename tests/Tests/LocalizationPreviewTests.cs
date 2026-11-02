namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

// ─────────────────────────────────────────────────────────────────────────────
// Test altyapısı
// ─────────────────────────────────────────────────────────────────────────────

public static class PreviewTestHelper
{
    public static LocalizationFormatAdapterRegistry CreateRegistry()
        => new(new ILocalizationFormatAdapter[]
        {
            new JsonLocalizationAdapter(),
            new YamlLocalizationAdapter(),
            new XmlLocalizationAdapter(),
            new CsvLocalizationAdapter(),
            new TsvLocalizationAdapter(),
            new PoLocalizationAdapter(),
            new PotLocalizationAdapter(),
            new IniLocalizationAdapter(),
            // CSV'den sonra: `.csv` uzantısı için varsayılan hâlâ CSV'dir; LocresCSV başlıkla seçilir.
            new LocresCsvLocalizationAdapter()
        });

    public static LocalizationPreviewService CreatePreviewService(TempDatabase database)
        => new(database.CreateFactory(), CreateRegistry(), new LocalizationFormatReadOptions(),
            NullLogger<LocalizationPreviewService>.Instance);

    /// <summary>Geçici DB'de Game + InstalledGame + DiscoveredFile kaydı oluşturur.</summary>
    public static async Task<(int InstallId, int FileId)> CreateInstallAndFileAsync(
        TempDatabase database,
        string rootPath,
        string filePath,
        string detectedFormat = "JSON")
    {
        using var context = database.CreateContext();

        var game = new Game { Name = "Ayrıştırma Testi" };
        context.Games.Add(game);
        await context.SaveChangesAsync();

        var install = new InstalledGame
        {
            GameId = game.Id,
            InstallPath = rootPath,
            InstallSource = "Manual",
            InstallSourceId = string.Empty,
            IsInstalled = true
        };

        context.InstalledGames.Add(install);
        await context.SaveChangesAsync();

        var file = new DiscoveredFile
        {
            InstalledGameId = install.Id,
            FilePath = filePath,
            FileName = System.IO.Path.GetFileName(filePath),
            Sha256 = string.Empty,
            DetectedFormat = detectedFormat,
            DetectionConfidence = "Low",
            IsReadable = true
        };

        context.DiscoveredFiles.Add(file);
        await context.SaveChangesAsync();

        return (install.Id, file.Id);
    }

    /// <summary>Var olan bir kuruluma ikinci bir keşfedilmiş dosya kaydı ekler.</summary>
    public static async Task<int> AddFileAsync(
        TempDatabase database,
        int installId,
        string filePath,
        string detectedFormat = "JSON")
    {
        using var context = database.CreateContext();

        var file = new DiscoveredFile
        {
            InstalledGameId = installId,
            FilePath = filePath,
            FileName = System.IO.Path.GetFileName(filePath),
            Sha256 = string.Empty,
            DetectedFormat = detectedFormat,
            DetectionConfidence = "Low",
            IsReadable = true
        };

        context.DiscoveredFiles.Add(file);
        await context.SaveChangesAsync();
        return file.Id;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Değişken / biçimlendirme koruma
// ─────────────────────────────────────────────────────────────────────────────

public class PlaceholderScannerTests
{
    [Fact]
    public void NamedAndPositionalBraces_AreDetected()
    {
        var scan = PlaceholderScanner.Scan("Merhaba {playerName}, {0} yeni mesajın var.");

        Assert.Equal(2, scan.Tokens.Count);
        Assert.Equal(PlaceholderKind.Named, scan.Tokens[0].Kind);
        Assert.Equal("{playerName}", scan.Tokens[0].Value);
        Assert.Equal(PlaceholderKind.Positional, scan.Tokens[1].Kind);
        Assert.Equal("{0}", scan.Tokens[1].Value);
        Assert.Equal("{playerName}|{0}", scan.Signature);
    }

    [Fact]
    public void PrintfAndEscapeTokens_AreDetected()
    {
        var scan = PlaceholderScanner.Scan("Ad: %s, sayı: %d, satır:\\n");

        Assert.Contains(scan.Tokens, t => t.Kind == PlaceholderKind.Printf && t.Value == "%s");
        Assert.Contains(scan.Tokens, t => t.Kind == PlaceholderKind.Printf && t.Value == "%d");
        Assert.Contains(scan.Tokens, t => t.Kind == PlaceholderKind.Escape && t.Value == "\\n");
    }

    [Fact]
    public void AngleAndBracketTags_AreDetected()
    {
        var scan = PlaceholderScanner.Scan("<b>Önemli</b> [color=red]uyarı[/color]");

        Assert.Contains(scan.Tokens, t => t.Kind == PlaceholderKind.Tag && t.Value == "<b>");
        Assert.Contains(scan.Tokens, t => t.Kind == PlaceholderKind.Tag && t.Value == "</b>");
        Assert.Contains(scan.Tokens, t => t.Kind == PlaceholderKind.BracketTag && t.Value == "[color=red]");
        Assert.Contains(scan.Tokens, t => t.Kind == PlaceholderKind.BracketTag && t.Value == "[/color]");
    }

    [Fact]
    public void PlainText_HasNoTokens()
    {
        var scan = PlaceholderScanner.Scan("Sadece düz bir cümle. 100% emin değilim.");

        Assert.False(scan.HasPlaceholders);
        Assert.Equal(string.Empty, scan.Signature);
    }

    [Fact]
    public void PercentAtEnd_IsNotTreatedAsPrintf()
    {
        var scan = PlaceholderScanner.Scan("İndirim %50");

        Assert.DoesNotContain(scan.Tokens, t => t.Kind == PlaceholderKind.Printf);
    }

    [Fact]
    public void Signature_IsComparableBetweenSourceAndTranslation()
    {
        var source = PlaceholderScanner.Scan("{playerName} saldırdı: %d hasar, <b>{0}</b>");
        var translated = PlaceholderScanner.Scan("{playerName} attacked: %d damage, <b>{0}</b>");

        Assert.Equal(source.Signature, translated.Signature);
        Assert.Equal(source.TypedSignature, translated.TypedSignature);
    }

    [Fact]
    public void UnterminatedBrace_IsIgnored()
    {
        var scan = PlaceholderScanner.Scan("Bozuk {playerName");

        Assert.False(scan.HasPlaceholders);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// JSON ayrıştırıcısı
// ─────────────────────────────────────────────────────────────────────────────

public class JsonLocalizationAdapterTests
{
    private const string SampleJson = """
    {
      "dialogue": {
        "greeting": "Welcome, traveler!",
        "farewell": "See you again."
      },
      "version": 3,
      "enabled": true,
      "nothing": null
    }
    """;

    [Fact]
    public async Task NestedObjectsAndArrays_AreWalked()
    {
        using var tree = new TempTree();

        var result = await new JsonLocalizationAdapter().ReadAsync(tree.Write("loc.json", """
        {
          "menu": { "start": "Başla" },
          "items": [ "Kılıç", "Kalkan" ],
          "nested": [ { "deep": { "text": "Derin metin" } } ]
        }
        """));

        Assert.True(result.Success);
        Assert.Equal("JSON", result.Entries[0].FormatKey);

        var keys = result.Entries.Select(e => e.Key).ToList();

        Assert.Contains("/menu/start", keys);
        Assert.Contains("/items/0", keys);
        Assert.Contains("/items/1", keys);
        Assert.Contains("/nested/0/deep/text", keys);
    }

    [Fact]
    public async Task NonTextValues_AreNotPresentedAsTranslatable()
    {
        using var tree = new TempTree();
        var result = await new JsonLocalizationAdapter().ReadAsync(tree.Write("loc.json", SampleJson));

        Assert.True(result.Success);

        var translatable = result.Entries
            .Where(e => e.Status == LocalizationEntryStatus.Translatable)
            .Select(e => e.SourceText)
            .ToList();

        Assert.Equal(new[] { "Welcome, traveler!", "See you again." }, translatable);

        var skipped = result.Entries.Where(e => e.Status == LocalizationEntryStatus.Skipped).ToList();
        Assert.Equal(3, skipped.Count); // 3, true, null
        Assert.All(skipped, e => Assert.NotNull(e.SkipReason));
        Assert.DoesNotContain(result.Entries, e =>
            e.Status == LocalizationEntryStatus.Translatable &&
            (e.SourceText == "3" || e.SourceText == "true" || e.SourceText == "null"));
    }

    [Fact]
    public async Task SameTextAtTwoLocations_StaysAsTwoEntries()
    {
        using var tree = new TempTree();

        var result = await new JsonLocalizationAdapter().ReadAsync(tree.Write("loc.json", """
        { "a": "Aynı metin", "b": "Aynı metin" }
        """));

        var entries = result.Entries.Where(e => e.SourceText == "Aynı metin").ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal(2, entries.Select(e => e.Key).Distinct().Count());
        Assert.Contains(entries, e => e.Key == "/a");
        Assert.Contains(entries, e => e.Key == "/b");
        Assert.Equal("JSON string değeri: /a", entries.Single(e => e.Key == "/a").Evidence);
    }

    [Fact]
    public async Task BlankStrings_AreSkippedWithReason()
    {
        using var tree = new TempTree();

        var result = await new JsonLocalizationAdapter().ReadAsync(tree.Write("loc.json", """
        { "empty": "", "spaces": "   " }
        """));

        Assert.All(result.Entries, e => Assert.Equal(LocalizationEntryStatus.Skipped, e.Status));
        Assert.All(result.Entries, e => Assert.Contains("Boş", e.SkipReason));
    }

    [Fact]
    public async Task InvalidJson_ReturnsReadableError()
    {
        using var tree = new TempTree();

        var result = await new JsonLocalizationAdapter().ReadAsync(tree.Write("loc.json", "{ \"a\": \"b\", }"));

        Assert.False(result.Success);
        Assert.Equal(LocalizationReadStatus.InvalidSyntax, result.Status);
        Assert.Contains("Geçersiz JSON", result.ErrorMessage);
    }

    [Fact]
    public async Task Placeholders_ArePreservedOnJsonEntries()
    {
        using var tree = new TempTree();

        var result = await new JsonLocalizationAdapter().ReadAsync(tree.Write("loc.json", """
        { "hit": "{playerName} {0} hasar aldı: %d" }
        """));

        var entry = Assert.Single(result.Entries);
        Assert.Equal("{playerName}|{0}|%d", entry.Placeholders.Signature);
        Assert.Equal("{playerName} {0} hasar aldı: %d", entry.SourceText);
        Assert.Equal("{playerName}|{0}|%d", entry.PlaceholderSignature);
    }

    [Fact]
    public async Task EmptyAndWhitespaceOnlyFiles_AreReported()
    {
        using var tree = new TempTree();

        var empty = await new JsonLocalizationAdapter().ReadAsync(tree.Write("empty.json", string.Empty));
        var blank = await new JsonLocalizationAdapter().ReadAsync(tree.Write("blank.json", "   \n\t \n"));

        Assert.Equal(LocalizationReadStatus.EmptyFile, empty.Status);
        Assert.Equal(LocalizationReadStatus.EmptyFile, blank.Status);
        Assert.False(empty.Success);
    }

    [Fact]
    public async Task OversizedFile_IsRejectedWithoutParsing()
    {
        using var tree = new TempTree();
        var path = tree.Write("big.json", "{ \"a\": \"uzun bir metin\" }");

        var result = await new JsonLocalizationAdapter()
            .ReadAsync(path, new LocalizationFormatReadOptions { MaxFileBytes = 5 });

        Assert.Equal(LocalizationReadStatus.FileTooLarge, result.Status);
        Assert.Contains("çok büyük", result.ErrorMessage);
    }

    [Fact]
    public async Task EntryLimit_MarksResultAsTruncated()
    {
        using var tree = new TempTree();
        var path = tree.Write("loc.json", "{ \"a\": \"1\", \"b\": \"2\", \"c\": \"3\" }");

        var result = await new JsonLocalizationAdapter()
            .ReadAsync(path, new LocalizationFormatReadOptions { MaxEntries = 2 });

        Assert.True(result.Success);
        Assert.Equal(2, result.Entries.Count);
        Assert.True(result.Truncated);
        Assert.Contains(result.Warnings, w => w.Contains("Kayıt sınırı"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// XML ayrıştırıcısı
// ─────────────────────────────────────────────────────────────────────────────

public class XmlLocalizationAdapterTests
{
    [Fact]
    public async Task ElementTextAndAttribute_AreBothCollected()
    {
        using var tree = new TempTree();

        var result = await new XmlLocalizationAdapter().ReadAsync(tree.Write("loc.xml", """
        <root>
          <greeting>Merhaba yolcu!</greeting>
          <item name="Kılıç" id="5">Keskin bir kılıç.</item>
        </root>
        """));

        Assert.True(result.Success);

        var element = result.Entries.Single(e => e.Key == "/root/greeting");
        Assert.Equal("Merhaba yolcu!", element.SourceText);
        Assert.Equal(LocalizationTextKind.XmlElementText, element.Kind);
        Assert.Equal(LocalizationEntryStatus.Translatable, element.Status);

        var attribute = result.Entries.Single(e => e.Key == "/root/item/@name");
        Assert.Equal("Kılıç", attribute.SourceText);
        Assert.Equal(LocalizationTextKind.XmlAttribute, attribute.Kind);
        Assert.Equal(LocalizationEntryStatus.Translatable, attribute.Status);

        // Yapısal attribute metin olarak sunulmaz.
        var structural = result.Entries.Single(e => e.Key == "/root/item/@id");
        Assert.Equal(LocalizationEntryStatus.Skipped, structural.Status);
        Assert.Contains("Yapısal", structural.SkipReason);
    }

    [Fact]
    public async Task TagNamesAndStructure_AreNotPresentedAsText()
    {
        using var tree = new TempTree();

        var result = await new XmlLocalizationAdapter().ReadAsync(
            tree.Write("loc.xml", "<root><child><inner>Metin</inner></child></root>"));

        var texts = result.Entries
            .Where(e => e.Status == LocalizationEntryStatus.Translatable)
            .Select(e => e.SourceText)
            .ToList();

        Assert.Equal(new[] { "Metin" }, texts);
        Assert.DoesNotContain(result.Entries, e => e.SourceText.Contains("root") || e.SourceText.Contains("child"));
    }

    [Fact]
    public async Task NamespacedElements_AreSupported()
    {
        using var tree = new TempTree();

        var result = await new XmlLocalizationAdapter().ReadAsync(tree.Write("loc.xml", """
        <root xmlns:ui="urn:test:ui">
          <ui:item name="Selam">Dünya</ui:item>
        </root>
        """));

        Assert.True(result.Success);

        var element = result.Entries.Single(e => e.Key == "/root/item");
        Assert.Equal("Dünya", element.SourceText);
        Assert.Contains("urn:test:ui", element.Context);

        Assert.Contains(result.Entries, e => e.Key == "/root/item/@name" && e.SourceText == "Selam");

        // xmlns bildirimi metin olarak alınmamalı.
        Assert.DoesNotContain(result.Entries, e => e.SourceText.Contains("urn:test:ui"));
    }

    [Fact]
    public async Task ExternalEntities_AreBlocked()
    {
        using var tree = new TempTree();

        var payload = """
        <?xml version="1.0"?>
        <!DOCTYPE root [ <!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini"> ]>
        <root><text>&xxe;</text></root>
        """;

        var result = await new XmlLocalizationAdapter().ReadAsync(tree.Write("xxe.xml", payload));

        Assert.False(result.Success);
        Assert.Equal(LocalizationReadStatus.InvalidSyntax, result.Status);
        Assert.Empty(result.Entries);
        Assert.DoesNotContain(result.Entries, e => e.SourceText.Contains("[fonts]", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BrokenXml_ReturnsReadableError()
    {
        using var tree = new TempTree();

        var result = await new XmlLocalizationAdapter().ReadAsync(tree.Write("bad.xml", "<root><a>metin</root>"));

        Assert.False(result.Success);
        Assert.Equal(LocalizationReadStatus.InvalidSyntax, result.Status);
        Assert.Contains("Geçersiz XML", result.ErrorMessage);
    }

    [Fact]
    public async Task NumericAttributes_AreSkipped()
    {
        using var tree = new TempTree();

        var result = await new XmlLocalizationAdapter().ReadAsync(
            tree.Write("loc.xml", """<item x="10" count="3" visible="true" text="Görünür metin" />"""));

        var text = result.Entries.Single(e => e.Key == "/item/@text");
        Assert.Equal(LocalizationEntryStatus.Translatable, text.Status);

        Assert.All(
            result.Entries.Where(e => e.Key is "/item/@x" or "/item/@count" or "/item/@visible"),
            e => Assert.Equal(LocalizationEntryStatus.Skipped, e.Status));
    }

    [Fact]
    public async Task RepeatedSiblingElements_GetUniqueKeys()
    {
        using var tree = new TempTree();

        var result = await new XmlLocalizationAdapter().ReadAsync(tree.Write("loc.xml", """
        <root><line>Bir</line><line>İki</line></root>
        """));

        var lines = result.Entries.Where(e => e.SourceText is "Bir" or "İki").ToList();

        Assert.Equal(2, lines.Count);
        Assert.Equal(new[] { "/root/line[1]", "/root/line[2]" }, lines.Select(e => e.Key).ToArray());
    }

    [Fact]
    public async Task MixedContent_DirectTextAndChildElement_AreOrderedWithStableKeys()
    {
        using var tree = new TempTree();

        var result = await ReadXmlAsync(tree, "<text>Merhaba <b>dünya</b>!</text>");

        Assert.True(result.Success);
        Assert.Equal(
            new[] { "/text/text()[1]", "/text/b", "/text/text()[2]" },
            result.Entries.Select(e => e.Key).ToArray());
        Assert.Equal(
            new[] { "Merhaba ", "dünya", "!" },
            result.Entries.Select(e => e.SourceText).ToArray());
        Assert.All(result.Entries, entry =>
        {
            Assert.Equal(LocalizationTextKind.XmlElementText, entry.Kind);
            Assert.Equal(LocalizationEntryStatus.Translatable, entry.Status);
        });
    }

    [Fact]
    public async Task MixedContentMultipleChildren_PreserveOuterWhitespaceAndDocumentOrder()
    {
        using var tree = new TempTree();

        var result = await ReadXmlAsync(
            tree,
            "<text>Ön <b>kalın</b> orta <i>italik</i> son</text>");

        Assert.Equal(
            new[] { "/text/text()[1]", "/text/b", "/text/text()[2]", "/text/i", "/text/text()[3]" },
            result.Entries.Select(e => e.Key).ToArray());
        Assert.Equal(
            new[] { "Ön ", "kalın", " orta ", "italik", " son" },
            result.Entries.Select(e => e.SourceText).ToArray());
        Assert.DoesNotContain(result.Entries, e => e.Key == "/text");
    }

    [Fact]
    public async Task NestedOnlyAndPlainTextElements_KeepExistingElementKeys()
    {
        using var tree = new TempTree();

        var nested = await ReadXmlAsync(
            tree,
            "<text><b>Tamamen iç içe</b></text>",
            "nested.xml");
        var nestedEntry = Assert.Single(nested.Entries);
        Assert.Equal("/text/b", nestedEntry.Key);
        Assert.Equal("Tamamen iç içe", nestedEntry.SourceText);
        Assert.DoesNotContain(nested.Entries, e => e.Key.Contains("/text()", StringComparison.Ordinal));

        var plain = await ReadXmlAsync(
            tree,
            "<text>Yalnızca düz metin</text>",
            "plain.xml");
        var plainEntry = Assert.Single(plain.Entries);
        Assert.Equal("/text", plainEntry.Key);
        Assert.Equal("Yalnızca düz metin", plainEntry.SourceText);
        Assert.Equal(LocalizationEntryStatus.Translatable, plainEntry.Status);
    }

    [Fact]
    public async Task MixedContentWhitespace_PreservesMeaningfulSpacesAndOmitsFormattingIndentation()
    {
        using var tree = new TempTree();

        var mixed = await ReadXmlAsync(
            tree,
            "<text> <b>kalın</b> <i>italik</i> </text>",
            "whitespace.xml");

        Assert.Equal(
            new[] { "/text/text()[1]", "/text/b", "/text/text()[2]", "/text/i", "/text/text()[3]" },
            mixed.Entries.Select(e => e.Key).ToArray());
        Assert.Equal(
            new[] { " ", "kalın", " ", "italik", " " },
            mixed.Entries.Select(e => e.SourceText).ToArray());
        Assert.Equal(
            new[]
            {
                LocalizationEntryStatus.Skipped,
                LocalizationEntryStatus.Translatable,
                LocalizationEntryStatus.Skipped,
                LocalizationEntryStatus.Translatable,
                LocalizationEntryStatus.Skipped
            },
            mixed.Entries.Select(e => e.Status).ToArray());
        Assert.All(mixed.Entries.Where(e => e.Key.Contains("/text()", StringComparison.Ordinal)), entry =>
        {
            Assert.Equal("Boş veya yalnızca boşluk içeren element", entry.SkipReason);
            Assert.Empty(entry.Placeholders.Tokens);
        });

        var formatted = await ReadXmlAsync(
            tree,
            "<root>\n  <item>Bir</item>\n  <item>İki</item>\n</root>",
            "formatted.xml");

        Assert.Equal(new[] { "/root/item[1]", "/root/item[2]" }, formatted.Entries.Select(e => e.Key).ToArray());
        Assert.Equal(new[] { "Bir", "İki" }, formatted.Entries.Select(e => e.SourceText).ToArray());
        Assert.DoesNotContain(formatted.Entries, e => e.Key.Contains("/text()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdjacentTextAndCData_PreserveNodeOrderContentAndLeafBehavior()
    {
        using var tree = new TempTree();

        var mixed = await ReadXmlAsync(
            tree,
            "<text>önce<![CDATA[CDATA & ham metin]]>sonra<b>kalın</b><![CDATA[son]]></text>",
            "cdata.xml");

        Assert.Equal(
            new[] { "/text/text()[1]", "/text/text()[2]", "/text/text()[3]", "/text/b", "/text/text()[4]" },
            mixed.Entries.Select(e => e.Key).ToArray());
        Assert.Equal(
            new[] { "önce", "CDATA & ham metin", "sonra", "kalın", "son" },
            mixed.Entries.Select(e => e.SourceText).ToArray());
        Assert.All(mixed.Entries, e => Assert.Equal(LocalizationEntryStatus.Translatable, e.Status));

        var leaf = await ReadXmlAsync(
            tree,
            "<root><leaf>A<![CDATA[B]]>C</leaf></root>",
            "leaf-cdata.xml");
        var leafEntry = Assert.Single(leaf.Entries);
        Assert.Equal("/root/leaf", leafEntry.Key);
        Assert.Equal("ABC", leafEntry.SourceText);
    }

    [Fact]
    public async Task EmptyAndWhitespaceOnlyElements_KeepSkippedSourceTextContract()
    {
        using var tree = new TempTree();

        var result = await ReadXmlAsync(
            tree,
            "<root><empty></empty><blank>   </blank><selfClosing /></root>");

        Assert.Equal(
            new[] { "/root/empty", "/root/blank", "/root/selfClosing" },
            result.Entries.Select(e => e.Key).ToArray());
        Assert.Equal(new[] { "", "   ", "" }, result.Entries.Select(e => e.SourceText).ToArray());
        Assert.All(result.Entries, entry =>
        {
            Assert.Equal(LocalizationEntryStatus.Skipped, entry.Status);
            Assert.Equal("Boş veya yalnızca boşluk içeren element", entry.SkipReason);
            Assert.Empty(entry.Placeholders.Tokens);
        });
        Assert.Equal(0, result.TranslatableCount);
        Assert.Equal(3, result.SkippedCount);
    }


    [Fact]
    public async Task MixedContentRepeatedSiblings_KeepIndexedChildKeysWithoutTextKeyCollisions()
    {
        using var tree = new TempTree();

        var result = await ReadXmlAsync(
            tree,
            "<text>Ön <b>Tekrar</b> orta <b>Tekrar</b> son</text>");

        Assert.Equal(
            new[] { "/text/text()[1]", "/text/b[1]", "/text/text()[2]", "/text/b[2]", "/text/text()[3]" },
            result.Entries.Select(e => e.Key).ToArray());
        Assert.Equal(
            new[] { "Ön ", "Tekrar", " orta ", "Tekrar", " son" },
            result.Entries.Select(e => e.SourceText).ToArray());
        Assert.Equal(result.Entries.Count, result.Entries.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task MixedContentStructuralAttributes_KeepExistingClassification()
    {
        using var tree = new TempTree();

        var result = await ReadXmlAsync(
            tree,
            "<text id=\"42\" key=\"greeting\">Merhaba <b>dünya</b>!</text>");

        Assert.Equal(
            new[] { "/text/@id", "/text/@key", "/text/text()[1]", "/text/b", "/text/text()[2]" },
            result.Entries.Select(e => e.Key).ToArray());

        var id = result.Entries[0];
        var key = result.Entries[1];
        Assert.Equal("42", id.SourceText);
        Assert.Equal("greeting", key.SourceText);
        Assert.All(new[] { id, key }, entry =>
        {
            Assert.Equal(LocalizationTextKind.XmlAttribute, entry.Kind);
            Assert.Equal(LocalizationEntryStatus.Skipped, entry.Status);
            Assert.Contains("Yapısal", entry.SkipReason);
        });
        Assert.Equal(3, result.TranslatableCount);
        Assert.Equal(2, result.SkippedCount);
    }

    [Fact]
    public async Task MixedContentOuterPlaceholders_AreScannedAndValidatedPerSegmentWithoutXmlTags()
    {
        using var tree = new TempTree();

        var result = await ReadXmlAsync(
            tree,
            "<text>Merhaba {playerName} <b>%s</b>! {0}</text>");

        Assert.Equal(
            new[] { "Merhaba {playerName} ", "%s", "! {0}" },
            result.Entries.Select(e => e.SourceText).ToArray());

        var tokens = result.Entries.SelectMany(e => e.Placeholders.Tokens).ToList();
        Assert.Equal(
            new[] { PlaceholderKind.Named, PlaceholderKind.Printf, PlaceholderKind.Positional },
            tokens.Select(t => t.Kind).ToArray());
        Assert.Equal(new[] { "{playerName}", "%s", "{0}" }, tokens.Select(t => t.Value).ToArray());
        Assert.DoesNotContain(tokens, t => t.Kind == PlaceholderKind.Tag);
        Assert.DoesNotContain(result.Entries, e => e.SourceText.Contains("<text>") || e.SourceText.Contains("<b>"));

        var outerValidation = TranslationQualityValidator.Validate(
            result.Entries[0].SourceText,
            "Hello {playerName} ");
        var childValidation = TranslationQualityValidator.Validate(result.Entries[1].SourceText, "Değer: %s");
        var trailingValidation = TranslationQualityValidator.Validate(result.Entries[2].SourceText, "Tamam {0}!");
        Assert.Empty(outerValidation.Issues);
        Assert.Empty(childValidation.Issues);
        Assert.Empty(trailingValidation.Issues);
        Assert.True(outerValidation.PlaceholderComparison.IsMatch);
        Assert.True(childValidation.PlaceholderComparison.IsMatch);
        Assert.True(trailingValidation.PlaceholderComparison.IsMatch);

        var missingPlaceholder = TranslationQualityValidator.Validate(result.Entries[0].SourceText, "Hello ");
        Assert.Equal(
            TranslationQualityIssueCode.MissingPlaceholder,
            Assert.Single(missingPlaceholder.Issues).Code);
    }

    private static Task<LocalizationTextReadResult> ReadXmlAsync(
        TempTree tree,
        string xml,
        string fileName = "loc.xml")
        => new XmlLocalizationAdapter().ReadAsync(tree.Write(fileName, xml));
}

// ─────────────────────────────────────────────────────────────────────────────
// CSV / TSV ayrıştırıcısı
// ─────────────────────────────────────────────────────────────────────────────

public class DelimitedLocalizationAdapterTests
{
    [Fact]
    public void Parser_HandlesQuotedDelimitersAndEscapedQuotes()
    {
        var parsed = DelimitedTextParser.Parse("key,text\n1,\"Hello, world\"\n2,\"She said \"\"hi\"\"\"\n");

        Assert.Equal(',', parsed.Delimiter);
        Assert.True(parsed.IsWellFormed);
        Assert.Equal(3, parsed.Rows.Count);

        Assert.Equal(new[] { "1", "Hello, world" }, parsed.Rows[1].Fields);
        Assert.Equal(new[] { "2", "She said \"hi\"" }, parsed.Rows[2].Fields);
    }

    [Fact]
    public void Parser_DetectsSemicolonDelimiter()
    {
        var parsed = DelimitedTextParser.Parse("id;metin\n1;Merhaba\n2;Dünya\n");

        Assert.Equal(';', parsed.Delimiter);
        Assert.True(parsed.DelimiterDetermined);
        Assert.Equal(3, parsed.Rows.Count);
        Assert.Equal(new[] { "1", "Merhaba" }, parsed.Rows[1].Fields);
    }

    [Fact]
    public void Parser_WarnsWhenDelimiterCannotBeDetermined()
    {
        var parsed = DelimitedTextParser.Parse("tek sutun\nikinci satir\n");

        Assert.False(parsed.DelimiterDetermined);
        Assert.Contains(parsed.Warnings, w => w.Contains("Ayraç belirlenemedi"));
        Assert.Equal(2, parsed.Rows.Count);
        Assert.All(parsed.Rows, r => Assert.Single(r.Fields));
    }

    [Fact]
    public void Parser_ReportsUnbalancedQuotes()
    {
        var parsed = DelimitedTextParser.Parse("key,text\n1,\"kapanmayan\n");

        Assert.False(parsed.IsWellFormed);
        Assert.Contains(parsed.Warnings, w => w.Contains("Kapanmayan tırnak"));
    }

    [Fact]
    public async Task Csv_WithHeader_ProducesRowsAndColumns()
    {
        using var tree = new TempTree();

        var result = await new CsvLocalizationAdapter().ReadAsync(tree.Write("loc.csv",
            "id,text\n1,\"Merhaba, dünya\"\n2,Veda\n"));

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Başlık satırı algılanmadı"));

        var entries = result.Entries.Where(e => e.Status != LocalizationEntryStatus.Skipped).ToList();

        Assert.Contains(entries, e => e.SourceText == "Merhaba, dünya" && e.Key.Contains("text"));
        Assert.Contains(entries, e => e.SourceText == "Veda" && e.Key.Contains("text"));
    }

    [Fact]
    public async Task Csv_WithoutHeader_UsesRowAndColumnPositions()
    {
        using var tree = new TempTree();

        var result = await new CsvLocalizationAdapter().ReadAsync(tree.Write("loc.csv",
            "\"Merhaba dünya, nasılsın?\",\"İkinci hücre\"\n"));

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("Başlık satırı algılanmadı"));
        Assert.Contains(result.Entries, e => e.Key.Contains("sütun 1"));
        Assert.Contains(result.Entries, e => e.SourceText.Contains("Merhaba dünya"));
    }

    [Fact]
    public async Task Csv_HeaderlessTextRows_KeepFirstRowAsData()
    {
        using var tree = new TempTree();

        var result = await new CsvLocalizationAdapter().ReadAsync(tree.Write("loc.csv",
            "Hello,World\nfoo bar,baz\n"));

        Assert.True(result.Success);
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "Hello");
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "World");
        Assert.Contains(result.Entries, e => e.Line == 2 && e.SourceText == "foo bar");
        Assert.Contains(result.Entries, e => e.Line == 2 && e.SourceText == "baz");
        Assert.All(result.Entries, e => Assert.StartsWith("sütun ", e.Context));
        Assert.Contains(result.Warnings, w => w.Contains("güvenilir tespit edilemedi"));
    }

    [Fact]
    public async Task Csv_MalformedHeaderLikeRow_IsKeptAsData()
    {
        using var tree = new TempTree();

        var result = await new CsvLocalizationAdapter().ReadAsync(tree.Write("loc.csv",
            "Hello,World,Extra\nfoo bar,baz\n"));

        Assert.True(result.Success);
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "Hello");
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "World");
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "Extra");
        Assert.All(result.Entries, e => Assert.StartsWith("sütun ", e.Context));
        Assert.Contains(result.Warnings, w => w.Contains("güvenilir tespit edilemedi"));
    }

    [Fact]
    public async Task Csv_EmptyFile_ProducesWarningAndNoEntries()
    {
        using var tree = new TempTree();

        var result = await new CsvLocalizationAdapter().ReadAsync(tree.Write("loc.csv", string.Empty));

        Assert.False(result.Success);
        Assert.Equal(LocalizationReadStatus.EmptyFile, result.Status);
        Assert.Equal("Dosya boş (0 bayt).", result.ErrorMessage);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task Csv_SingleRow_IsReadAsData()
    {
        using var tree = new TempTree();

        var result = await new CsvLocalizationAdapter().ReadAsync(tree.Write("loc.csv",
            "Hello,World\n"));

        Assert.True(result.Success);
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "Hello");
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "World");
        Assert.Contains(result.Warnings, w => w.Contains("Başlık satırı algılanmadı"));
    }

    [Fact]
    public async Task Csv_UnknownHeaderNames_StillDetectedWithNumericData()
    {
        using var tree = new TempTree();

        var result = await new CsvLocalizationAdapter().ReadAsync(tree.Write("loc.csv",
            "foo,bar\n1,2\n"));

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Entries, e => e.SourceText == "foo");
        Assert.Contains(result.Entries, e => e.SourceText == "1" && e.Key.Contains("/ foo"));
        Assert.Contains(result.Warnings, w => w.Contains("sezgisel"));
    }

    [Fact]
    public async Task Csv_SingleKnownHeaderColumn_IsKeptWithWarning()
    {
        using var tree = new TempTree();

        var result = await new CsvLocalizationAdapter().ReadAsync(tree.Write("loc.csv",
            "key,en\nplayer,Player\n"));

        Assert.True(result.Success);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("player", result.Entries[0].SourceText);
        Assert.Equal("key", result.Entries[0].Context);
        Assert.Equal("Player", result.Entries[1].SourceText);
        Assert.Equal("en", result.Entries[1].Context);
        Assert.Contains(result.Warnings, w => w.Contains("tek sütun adı"));
    }

    [Fact]
    public async Task Tsv_HeaderlessTextRows_KeepFirstRowAsData()
    {
        using var tree = new TempTree();

        var result = await new TsvLocalizationAdapter().ReadAsync(tree.Write("loc.tsv",
            "Hello\tWorld\nfoo bar\tbaz\n"));

        Assert.True(result.Success);
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "Hello");
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "World");
        Assert.Contains(result.Entries, e => e.Line == 2 && e.SourceText == "foo bar");
        Assert.Contains(result.Entries, e => e.Line == 2 && e.SourceText == "baz");
        Assert.All(result.Entries, e => Assert.StartsWith("sütun ", e.Context));
        Assert.Contains(result.Warnings, w => w.Contains("güvenilir tespit edilemedi"));
    }

    [Fact]
    public async Task Tsv_MalformedHeaderLikeRow_IsKeptAsData()
    {
        using var tree = new TempTree();

        var result = await new TsvLocalizationAdapter().ReadAsync(tree.Write("loc.tsv",
            "Hello\tWorld\tExtra\nfoo bar\tbaz\n"));

        Assert.True(result.Success);
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "Hello");
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "World");
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "Extra");
        Assert.All(result.Entries, e => Assert.StartsWith("sütun ", e.Context));
        Assert.Contains(result.Warnings, w => w.Contains("güvenilir tespit edilemedi"));
    }

    [Fact]
    public async Task Tsv_EmptyFile_ProducesWarningAndNoEntries()
    {
        using var tree = new TempTree();

        var result = await new TsvLocalizationAdapter().ReadAsync(tree.Write("loc.tsv", string.Empty));

        Assert.False(result.Success);
        Assert.Equal(LocalizationReadStatus.EmptyFile, result.Status);
        Assert.Equal("Dosya boş (0 bayt).", result.ErrorMessage);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task Tsv_SingleRow_IsReadAsData()
    {
        using var tree = new TempTree();

        var result = await new TsvLocalizationAdapter().ReadAsync(tree.Write("loc.tsv",
            "Hello\tWorld\n"));

        Assert.True(result.Success);
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "Hello");
        Assert.Contains(result.Entries, e => e.Line == 1 && e.SourceText == "World");
        Assert.Contains(result.Warnings, w => w.Contains("Başlık satırı algılanmadı"));
    }

    [Fact]
    public async Task Tsv_UnknownHeaderNames_StillDetectedWithNumericData()
    {
        using var tree = new TempTree();

        var result = await new TsvLocalizationAdapter().ReadAsync(tree.Write("loc.tsv",
            "foo\tbar\n1\t2\n"));

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Entries, e => e.SourceText == "foo");
        Assert.Contains(result.Entries, e => e.SourceText == "1" && e.Key.Contains("/ foo"));
        Assert.Contains(result.Warnings, w => w.Contains("sezgisel"));
    }

    [Fact]
    public async Task Tsv_SingleKnownHeaderColumn_IsKeptWithWarning()
    {
        using var tree = new TempTree();

        var result = await new TsvLocalizationAdapter().ReadAsync(tree.Write("loc.tsv",
            "key\ten\nplayer\tPlayer\n"));

        Assert.True(result.Success);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("player", result.Entries[0].SourceText);
        Assert.Equal("key", result.Entries[0].Context);
        Assert.Equal("Player", result.Entries[1].SourceText);
        Assert.Equal("en", result.Entries[1].Context);
        Assert.Contains(result.Warnings, w => w.Contains("tek sütun adı"));
    }

    [Fact]
    public async Task Csv_NumericCells_AreClassifiedConservatively()
    {
        using var tree = new TempTree();

        var result = await new CsvLocalizationAdapter().ReadAsync(tree.Write("loc.csv",
            "id,text\n1,42\n2,Merhaba\n"));

        // "id" sütunundaki 1 → sayısal, metin sütunu değil → atlandı
        var idCell = result.Entries.Single(e => e.Key.Contains("/ id") && e.SourceText == "1");
        Assert.Equal(LocalizationEntryStatus.Skipped, idCell.Status);

        // "text" sütunundaki 42 → bağlam belirsiz → aday
        var ambiguous = result.Entries.Single(e => e.Key.Contains("/ text") && e.SourceText == "42");
        Assert.Equal(LocalizationEntryStatus.Ambiguous, ambiguous.Status);
        Assert.Contains("metin olabilir", ambiguous.SkipReason);

        var text = result.Entries.Single(e => e.SourceText == "Merhaba");
        Assert.Equal(LocalizationEntryStatus.Translatable, text.Status);
    }

    [Fact]
    public async Task Tsv_UsesTabDelimiter()
    {
        using var tree = new TempTree();

        var result = await new TsvLocalizationAdapter().ReadAsync(tree.Write("loc.tsv",
            "key\ttext\n1\tMerhaba\n2\tDünya\n"));

        Assert.True(result.Success);
        Assert.Equal("TSV", result.Entries[0].FormatKey);
        Assert.Contains(result.Entries, e => e.SourceText == "Merhaba");
        Assert.Contains(result.Entries, e => e.SourceText == "Dünya");
    }

    [Fact]
    public async Task Csv_EntryLimit_StopsBeforeExceedingLimitWithinRow()
    {
        using var tree = new TempTree();

        var result = await new CsvLocalizationAdapter().ReadAsync(
            tree.Write("loc.csv", "key,text\nSelam,Dünya\n"),
            new LocalizationFormatReadOptions { MaxEntries = 1 });

        Assert.True(result.Success);
        Assert.True(result.Truncated);
        Assert.Single(result.Entries);
        Assert.Equal("Selam", result.Entries[0].SourceText);
        Assert.DoesNotContain(result.Entries, e => e.SourceText == "Dünya");
        Assert.Contains(result.Warnings, w => w.Contains("Kayıt sınırı"));
    }

    [Fact]
    public async Task Tsv_EntryLimit_StopsBeforeExceedingLimitWithinRow()
    {
        using var tree = new TempTree();

        var result = await new TsvLocalizationAdapter().ReadAsync(
            tree.Write("loc.tsv", "key\ttext\nSelam\tDünya\n"),
            new LocalizationFormatReadOptions { MaxEntries = 1 });

        Assert.True(result.Success);
        Assert.True(result.Truncated);
        Assert.Single(result.Entries);
        Assert.Equal("Selam", result.Entries[0].SourceText);
        Assert.DoesNotContain(result.Entries, e => e.SourceText == "Dünya");
        Assert.Contains(result.Warnings, w => w.Contains("Kayıt sınırı"));
    }

    [Fact]
    public async Task Csv_PreservesMultiLineQuotedCell()
    {
        using var tree = new TempTree();

        var result = await new CsvLocalizationAdapter().ReadAsync(tree.Write("loc.csv",
            "key,text\n1,\"Birinci satır\nİkinci satır\"\n"));

        var entry = result.Entries.Single(e => e.SourceText.Contains("İkinci satır"));

        Assert.Equal("Birinci satır\nİkinci satır", entry.SourceText);
        Assert.Equal(LocalizationEntryStatus.Translatable, entry.Status);
    }

    [Fact]
    public async Task Csv_PreservesCrlfInsideQuotedCell()
    {
        using var tree = new TempTree();

        var result = await new CsvLocalizationAdapter().ReadAsync(tree.Write("loc.csv",
            "key,text\r\n1,\"Birinci satır\r\nİkinci satır\"\r\n"));

        var entry = result.Entries.Single(e => e.SourceText.Contains("İkinci satır"));

        Assert.Equal("Birinci satır\r\nİkinci satır", entry.SourceText);
    }

    [Fact]
    public async Task Tsv_PreservesCrlfInsideQuotedCell()
    {
        using var tree = new TempTree();

        var result = await new TsvLocalizationAdapter().ReadAsync(tree.Write("loc.tsv",
            "key\ttext\r\n1\t\"Birinci satır\r\nİkinci satır\"\r\n"));

        var entry = result.Entries.Single(e => e.SourceText.Contains("İkinci satır"));

        Assert.Equal("Birinci satır\r\nİkinci satır", entry.SourceText);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Adaptör kayıt defteri
// ─────────────────────────────────────────────────────────────────────────────

public class LocalizationFormatAdapterRegistryTests
{
    [Theory]
    [InlineData(".json", "JSON")]
    [InlineData(".xml", "XML")]
    [InlineData(".csv", "CSV")]
    [InlineData(".tsv", "TSV")]
    [InlineData(".po", "PO")]
    [InlineData(".pot", "POT")]
    [InlineData(".ini", "INI")]
    [InlineData(".yaml", "YAML")]
    [InlineData(".yml", "YAML")]
    public void KnownExtensions_HaveAdapters(string extension, string expected)
    {
        var registry = PreviewTestHelper.CreateRegistry();
        var adapter = registry.FindByExtension(extension);

        Assert.NotNull(adapter);
        Assert.Equal(expected, adapter!.FormatName);
        Assert.True(adapter.SupportsRead);

        // Güvenli yazma altyapısı JSON, CSV, PO ve INI adaptörlerinde açıktır; POT şablondur.
        Assert.Equal(
            extension is ".json" or ".csv" or ".po" or ".ini",
            adapter.SupportsWrite);
    }

    [Theory]
    [InlineData(".pak")]
    [InlineData(".uasset")]
    [InlineData(".txt")]
    [InlineData(".strings")]
    public void UnimplementedExtensions_HaveNoAdapter(string extension)
    {
        var registry = PreviewTestHelper.CreateRegistry();

        Assert.Null(registry.FindByExtension(extension));
        Assert.False(registry.Supports(extension));
    }

    [Fact]
    public void Describe_ListsOnlyRealAdapters()
    {
        var described = PreviewTestHelper.CreateRegistry().Describe();

        Assert.Equal(9, described.Count);
        Assert.All(described, d =>
            Assert.Equal(
                d.FormatName is JsonLocalizationAdapter.FormatKey
                    or CsvLocalizationAdapter.FormatKey
                    or PoLocalizationAdapter.FormatKey
                    or IniLocalizationAdapter.FormatKey,
                d.SupportsWrite));
        Assert.Contains(described, d => d.FormatName == LocresCsvLocalizationAdapter.FormatKey);
        Assert.Contains(described, d => d.FormatName == YamlLocalizationAdapter.FormatKey);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Önizleme servisi (yol güvenliği, desteklenmeyen format, hata durumları)
// ─────────────────────────────────────────────────────────────────────────────

public class LocalizationPreviewServiceTests
{
    [Fact]
    public async Task ValidFile_OutsideRoot_IsRejectedBeforeOpening()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        // Kök dışında geçerli bir JSON var; yine de reddedilmeli (hiç açılmamalı).
        var outside = tree.Write("outside.json", "{ \"a\": \"dışarıdaki metin\" }");
        var insideRoot = System.IO.Path.Combine(tree.Root, "game");
        System.IO.Directory.CreateDirectory(insideRoot);

        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(database, insideRoot, outside);
        var result = await PreviewTestHelper.CreatePreviewService(database).PreviewAsync(installId, fileId);

        Assert.False(result.Success);
        Assert.Equal(LocalizationPreviewFailureKind.PathRejected, result.Failure);
        Assert.Contains("dışında", result.ErrorMessage);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task FileOutsideRoot_WithInvalidContent_StillReportsPathRejection()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        // İçerik bozuk; eğer dosya açılsaydı InvalidSyntax dönerdi. PathRejected bekleniyor.
        var outside = tree.Write("broken.json", "{ bozuk");
        var insideRoot = System.IO.Path.Combine(tree.Root, "game");
        System.IO.Directory.CreateDirectory(insideRoot);

        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(database, insideRoot, outside);
        var result = await PreviewTestHelper.CreatePreviewService(database).PreviewAsync(installId, fileId);

        Assert.Equal(LocalizationPreviewFailureKind.PathRejected, result.Failure);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task FileFromAnotherInstallation_IsRejected()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var file = tree.Write("loc.json", "{ \"a\": \"metin\" }");
        var (_, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(database, tree.Root, file);
        var (otherInstallId, _) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, tree.Write("other.json", "{ \"a\": \"x\" }"));

        var result = await PreviewTestHelper.CreatePreviewService(database).PreviewAsync(otherInstallId, fileId);

        Assert.Equal(LocalizationPreviewFailureKind.PathRejected, result.Failure);
        Assert.Contains("bu kuruluma ait değil", result.ErrorMessage);
    }

    [Fact]
    public async Task UnsupportedFormat_IsNotParsed()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        // İçerik aslında JSON ama uzantı .pak → ayrıştırıcı yok.
        var pak = tree.Write("localization.pak", "{ \"a\": \"metin\" }");
        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, pak, detectedFormat: "PAK");

        var result = await PreviewTestHelper.CreatePreviewService(database).PreviewAsync(installId, fileId);

        Assert.False(result.Success);
        Assert.Equal(LocalizationPreviewFailureKind.UnsupportedFormat, result.Failure);
        Assert.False(result.FormatSupported);
        Assert.Null(result.AdapterName);
        Assert.Empty(result.Entries);
        Assert.Contains("ayrıştırıcı yok", result.ErrorMessage);
    }

    [Fact]
    public async Task MissingFileOnDisk_IsReported()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var missing = System.IO.Path.Combine(tree.Root, "gone.json");
        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(database, tree.Root, missing);

        var result = await PreviewTestHelper.CreatePreviewService(database).PreviewAsync(installId, fileId);

        Assert.Equal(LocalizationPreviewFailureKind.FileMissing, result.Failure);
        Assert.Contains("diskte bulunamadı", result.ErrorMessage);
    }

    [Fact]
    public async Task UnknownIds_ReturnNotFound()
    {
        using var database = new TempDatabase();
        var service = PreviewTestHelper.CreatePreviewService(database);

        var noInstall = await service.PreviewAsync(4242, 1);
        var noFile = await service.PreviewAsync(1, 4242);

        Assert.Equal(LocalizationPreviewFailureKind.NotFound, noInstall.Failure);
        Assert.Equal(LocalizationPreviewFailureKind.NotFound, noFile.Failure);
    }

    [Fact]
    public async Task ValidJsonFile_IsPreviewedSuccessfully()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var file = tree.Write(@"Localization\menu.json", """
        { "start": "Başla {playerName}", "count": 3 }
        """);

        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(database, tree.Root, file);
        var result = await PreviewTestHelper.CreatePreviewService(database).PreviewAsync(installId, fileId);

        Assert.True(result.Success);
        Assert.True(result.FormatSupported);
        Assert.Equal("JSON", result.AdapterName);
        Assert.Equal(1, result.TranslatableCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Contains(result.Entries, e => e.Placeholders.Signature == "{playerName}");
        Assert.Equal("menu.json", result.FileName);
    }

    [Fact]
    public async Task PreviewDoesNotModifyTheFile()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var file = tree.Write("loc.json", "{ \"a\": \"dokunma\" }");
        var beforeWrite = System.IO.File.GetLastWriteTimeUtc(file);
        var beforeText = System.IO.File.ReadAllText(file);

        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(database, tree.Root, file);
        await PreviewTestHelper.CreatePreviewService(database).PreviewAsync(installId, fileId);

        Assert.Equal(beforeText, System.IO.File.ReadAllText(file));
        Assert.Equal(beforeWrite, System.IO.File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public async Task InvalidEncoding_IsReportedNotThrown()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var file = System.IO.Path.Combine(tree.Root, "bad.json");
        await System.IO.File.WriteAllBytesAsync(file, new byte[] { 0x7B, 0x22, 0xC3, 0x28, 0x22, 0x7D });

        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(database, tree.Root, file);
        var result = await PreviewTestHelper.CreatePreviewService(database).PreviewAsync(installId, fileId);

        Assert.False(result.Success);
        Assert.Equal(LocalizationReadStatus.EncodingError, result.ReadStatus);
        Assert.Contains("kodlaması", result.ErrorMessage);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// WPF önizleme ekranı (ViewModel hata gösterimi)
// ─────────────────────────────────────────────────────────────────────────────

public class FileDiscoveryViewModelPreviewTests
{
    private static FileDiscoveryViewModel CreateViewModel(TempDatabase database)
    {
        var installedService = new InstalledGameScanService(
            database.CreateFactory(),
            Array.Empty<GameLanguageExplorer.Core.Interfaces.IInstalledGameProvider>(),
            NullLogger<InstalledGameScanService>.Instance);

        var fileService = new LocalizationFileScanService(
            database.CreateFactory(),
            NullLogger<LocalizationFileScanService>.Instance);

        return new FileDiscoveryViewModel(
            installedService,
            fileService,
            PreviewTestHelper.CreatePreviewService(database));
    }

    private static DiscoveredFileRow Row(int id, string path, string format = "JSON")
        => new(new DiscoveredFile
        {
            Id = id,
            FilePath = path,
            FileName = System.IO.Path.GetFileName(path),
            DetectedFormat = format
        });

    [Fact]
    public async Task Preview_ShowsError_WhenPathIsRejected()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var outside = tree.Write("outside.json", "{ \"a\": \"metin\" }");
        var insideRoot = System.IO.Path.Combine(tree.Root, "game");
        System.IO.Directory.CreateDirectory(insideRoot);

        var (_, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(database, insideRoot, outside);

        var viewModel = CreateViewModel(database);
        viewModel.SelectedFile = Row(fileId, outside);

        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasPreview);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.PreviewErrorMessage));
        Assert.Contains("dışında", viewModel.PreviewErrorMessage);
        Assert.Empty(viewModel.PreviewEntries);
    }

    [Fact]
    public async Task Preview_ListsEntriesAndHidesSkippedByDefault()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var file = tree.Write(@"Localization\menu.json", """
        { "hello": "Merhaba {playerName}", "count": 7 }
        """);

        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(database, tree.Root, file);

        var viewModel = CreateViewModel(database);
        viewModel.SelectedFile = Row(fileId, file);

        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasPreview);
        Assert.Null(viewModel.PreviewErrorMessage);
        Assert.Equal(1, viewModel.PreviewTranslatableCount);
        Assert.Equal(1, viewModel.PreviewSkippedCount);
        Assert.Single(viewModel.PreviewEntries); // atlananlar varsayılan olarak gizli
        Assert.Equal("{playerName}", viewModel.PreviewEntries[0].PlaceholderText);

        viewModel.ShowSkippedPreviewEntries = true;
        Assert.Equal(2, viewModel.PreviewEntries.Count);
    }

    [Fact]
    public async Task SelectingAnotherFile_ResetsPreview()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var file = tree.Write("loc.json", "{ \"a\": \"metin\" }");
        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(database, tree.Root, file);

        var viewModel = CreateViewModel(database);
        viewModel.SelectedFile = Row(fileId, file);

        await viewModel.PreviewCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasPreview);

        viewModel.SelectedFile = null;

        Assert.False(viewModel.HasPreview);
        Assert.Empty(viewModel.PreviewEntries);
    }
}








