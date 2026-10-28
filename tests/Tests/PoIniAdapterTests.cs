namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;

// ─────────────────────────────────────────────────────────────────────────────
// PO / POT ayrıştırıcısı
// ─────────────────────────────────────────────────────────────────────────────

public class PoLocalizationAdapterTests
{
    private static async Task<LocalizationTextReadResult> ReadPoAsync(TempTree tree, string content)
        => await new PoLocalizationAdapter().ReadAsync(tree.Write("strings.po", content));

    private static async Task<LocalizationTextReadResult> ReadPotAsync(TempTree tree, string content)
        => await new PotLocalizationAdapter().ReadAsync(tree.Write("strings.pot", content));

    [Fact]
    public async Task BasicMsgIdMsgStr_ProducesEntryWithExistingTranslation()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgid "Welcome, traveler!"
        msgstr "Hoş geldin, yolcu!"
        """);

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);

        Assert.Equal("Welcome, traveler!", entry.SourceText);
        Assert.Equal("Hoş geldin, yolcu!", entry.ExistingTranslation);
        Assert.Equal(LocalizationEntryStatus.Translatable, entry.Status);
        Assert.Equal(LocalizationTextKind.PoMessage, entry.Kind);
        Assert.Equal("PO", entry.FormatKey);
        Assert.Equal(1, entry.Line);
    }

    [Fact]
    public async Task MsgCtxt_IsKeptAndUsedInKey()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgctxt "menu"
        msgid "Open"
        msgstr "Aç"
        """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("menu", entry.Context);
        Assert.Contains("[menu]", entry.Key);
        Assert.Equal("Open", entry.SourceText);
    }

    [Fact]
    public async Task MultiLineQuotedStrings_AreConcatenated()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgid ""
        "Bu çok "
        "uzun bir "
        "cümledir."
        msgstr ""
        "This is a "
        "long sentence."
        """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Bu çok uzun bir cümledir.", entry.SourceText);
        Assert.Equal("This is a long sentence.", entry.ExistingTranslation);
    }

    [Fact]
    public async Task EscapeSequences_AreDecoded()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgid "Line1\nLine2\t\"quoted\""
        msgstr "Satır1\nSatır2\t\"tırnak\""
        """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Line1\nLine2\t\"quoted\"", entry.SourceText);
        Assert.Equal("Satır1\nSatır2\t\"tırnak\"", entry.ExistingTranslation);
    }

    [Fact]
    public async Task DoubleBackslashEscape_IsDecodedAsSingleBackslash()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgid "Path C:\\Game"
        msgstr "Yol C:\\Oyun"
        """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(@"Path C:\Game", entry.SourceText);
        Assert.Equal(@"Yol C:\Oyun", entry.ExistingTranslation);
    }


    [Fact]
    public async Task PluralForms_AreEmittedSeparately_NotMerged()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgid "%d file"
        msgid_plural "%d files"
        msgstr[0] "%d dosya"
        msgstr[1] "%d dosya"
        """);

        var entries = result.Entries.OrderBy(e => e.PluralIndex).ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal(new int?[] { 0, 1 }, entries.Select(e => e.PluralIndex).ToArray());
        Assert.All(entries, e => Assert.Equal(2, e.PluralCount));
        Assert.All(entries, e => Assert.Equal("%d files", e.PluralSourceText));

        // İlk biçim tekil kaynağı, ikincisi çoğul kaynağı taşır; birleştirilmez.
        Assert.Equal("%d file", entries[0].SourceText);
        Assert.Equal("%d files", entries[1].SourceText);

        Assert.Equal("%d dosya", entries[0].ExistingTranslation);
        Assert.Equal("%d dosya", entries[1].ExistingTranslation);

        // Anahtarlar benzersiz.
        Assert.Equal(2, entries.Select(e => e.Key).Distinct().Count());
        Assert.Contains("[0]", entries[0].Key);
        Assert.Contains("[1]", entries[1].Key);
    }

    [Fact]
    public async Task PluralWithoutMsgStrForms_KeepsPluralSourceInfo()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgid "%d item"
        msgid_plural "%d items"
        """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("%d item", entry.SourceText);
        Assert.Equal("%d items", entry.PluralSourceText);
        Assert.Equal(0, entry.PluralCount);
        Assert.Null(entry.PluralIndex);
        Assert.Equal("", entry.ExistingTranslation);
    }

    [Fact]
    public async Task FuzzyFlag_MarksEntryAmbiguous()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        #, fuzzy
        msgid "Health"
        msgstr "Sağlık"
        """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(LocalizationEntryStatus.Ambiguous, entry.Status);
        Assert.True(entry.IsFuzzy);
        Assert.Contains("fuzzy", entry.Flags);
        Assert.Contains("fuzzy", entry.SkipReason);
    }

    [Fact]
    public async Task HeaderEntry_IsSkippedAsMetadata()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgid ""
        msgstr ""
        "Project-Id-Version: Test 1.0\n"
        "Language: tr\n"

        msgid "Start"
        msgstr "Başla"
        """);

        Assert.Equal(2, result.Entries.Count);

        var header = result.Entries.Single(e => e.Key.Contains("başlık"));
        Assert.Equal(LocalizationEntryStatus.Skipped, header.Status);
        Assert.Contains("meta veri", header.SkipReason);

        var entry = result.Entries.Single(e => e.SourceText == "Start");
        Assert.Equal(LocalizationEntryStatus.Translatable, entry.Status);
    }

    [Fact]
    public async Task PotWithEmptyMsgStr_IsNotAnError()
    {
        using var tree = new TempTree();

        var result = await ReadPotAsync(tree, """
        msgid "Start game"
        msgstr ""

        msgid "Quit"
        msgstr ""
        """);

        Assert.True(result.Success);
        Assert.Equal(2, result.Entries.Count);
        Assert.All(result.Entries, e => Assert.Equal(LocalizationEntryStatus.Translatable, e.Status));
        Assert.All(result.Entries, e => Assert.Equal("", e.ExistingTranslation));
        Assert.All(result.Entries, e => Assert.Equal("POT", e.FormatKey));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("ayrıştırılamadı", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ObsoleteEntries_AreSkippedNotDropped()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        #~ msgid "Old text"
        #~ msgstr "Eski metin"

        msgid "New text"
        msgstr "Yeni metin"
        """);

        Assert.Equal(2, result.Entries.Count);

        var obsolete = result.Entries.Single(e => e.SourceText == "Old text");
        Assert.Equal(LocalizationEntryStatus.Skipped, obsolete.Status);
        Assert.Contains("obsolete", obsolete.SkipReason);

        Assert.Equal(LocalizationEntryStatus.Translatable,
            result.Entries.Single(e => e.SourceText == "New text").Status);
    }

    [Fact]
    public async Task CommentsAndReferences_AreHandled()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        # çevirmen notu
        #. extracted comment
        #: src/main.c:42
        #| msgid "Previous"
        #, c-format
        msgid "Hello %s"
        msgstr "Merhaba %s"
        """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Hello %s", entry.SourceText);
        Assert.Equal("Merhaba %s", entry.ExistingTranslation);
        Assert.Equal("src/main.c:42", entry.Reference);
        Assert.Contains("c-format", entry.Flags);
        Assert.Equal(LocalizationEntryStatus.Translatable, entry.Status);
    }

    [Fact]
    public async Task MalformedEntry_IsReportedAndMarkedSkipped()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgid "Sağlam"
        msgstr "Intact"

        msgid "Bozuk
        msgstr "Broken"
        """);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("kapanmayan tırnak", StringComparison.OrdinalIgnoreCase));

        var malformed = result.Entries.Single(e => e.Status == LocalizationEntryStatus.Skipped);
        Assert.Contains("Ayrıştırılamayan", malformed.SkipReason);

        // Sağlam kayıt hâlâ geçerli.
        Assert.Contains(result.Entries, e =>
            e.SourceText == "Sağlam" && e.Status == LocalizationEntryStatus.Translatable);
    }

    [Fact]
    public async Task GarbageFile_ReturnsInvalidSyntax()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        this is definitely not a po file
        neither is this line
        """);

        Assert.False(result.Success);
        Assert.Equal(LocalizationReadStatus.InvalidSyntax, result.Status);
        Assert.Contains("PO dosyası ayrıştırılamadı", result.ErrorMessage);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task EmptyFile_IsReportedAsEmpty()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, string.Empty);

        Assert.Equal(LocalizationReadStatus.EmptyFile, result.Status);
    }

    [Fact]
    public async Task Placeholders_ArePreservedInPo()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgid "{playerName} dealt %d damage"
        msgstr "{playerName} %d hasar verdi"
        """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("{playerName}|%d", entry.Placeholders.Signature);
        Assert.Equal("{playerName}|%d", entry.PlaceholderSignature);
    }

    [Fact]
    public async Task DuplicateMsgIds_GetUniqueKeys()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgid "Same"
        msgstr "Bir"

        msgid "Same"
        msgstr "İki"
        """);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(2, result.Entries.Select(e => e.Key).Distinct().Count());
        Assert.Contains(result.Entries, e => e.ExistingTranslation == "Bir");
        Assert.Contains(result.Entries, e => e.ExistingTranslation == "İki");
    }

    [Fact]
    public async Task MissingBlankLineBetweenEntries_IsReportedAndBothEntriesPreserved()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgid "A"
        msgstr "Bir"
        msgid "B"
        msgstr "İki"
        """);

        Assert.True(result.Success);
        Assert.Equal(new[] { "A", "B" }, result.Entries.Select(e => e.SourceText).ToArray());
        Assert.Equal(new[] { "Bir", "İki" }, result.Entries.Select(e => e.ExistingTranslation).ToArray());
        Assert.Contains(result.Warnings,
            w => w.Contains("boş satırla ayrılmamış", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MissingBlankLineBeforeMsgCtxt_IsReportedAndBothEntriesPreserved()
    {
        using var tree = new TempTree();

        var result = await ReadPoAsync(tree, """
        msgid "A"
        msgstr "Bir"
        msgctxt "menu"
        msgid "B"
        msgstr "İki"
        """);

        Assert.True(result.Success);
        Assert.Equal(new[] { "A", "B" }, result.Entries.Select(e => e.SourceText).ToArray());
        Assert.Equal("menu", result.Entries[1].Context);
        Assert.Contains(result.Warnings,
            w => w.Contains("boş satırla ayrılmamış", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnknownEscape_IsReportedAsMalformed_NotSilentlyRewritten()
    {
        using var tree = new TempTree();

        // Gerçek PO içeriğinde tek ters bölü vardır: msgid "C:\q"
        var result = await ReadPoAsync(tree, """
        msgid "Safe"
        msgstr "Güvenli"

        msgid "C:\q"
        msgstr "D:\q"
        """);

        Assert.True(result.Success);
        Assert.Equal("Safe", Assert.Single(result.Entries).SourceText);
        Assert.Contains(result.Warnings,
            w => w.Contains("kaçış", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("\r", "CR-only")]
    [InlineData("\n", "LF")]
    [InlineData("\r\n", "CRLF")]
    public async Task LineEndings_PreserveRecordKeysContextsAndLineNumbers(string newline, string lineEndingName)
    {
        using var tree = new TempTree();

        var content = string.Join(newline, new[]
        {
            "msgctxt \"menu\"",
            "msgid \"Open\"",
            "msgstr \"Aç\"",
            "",
            "msgid \"Close\"",
            "msgstr \"Kapat\""
        });

        var result = await ReadPoAsync(tree, content);

        Assert.True(result.Success, $"{lineEndingName} satır sonları okunamadı.");
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(new[] { "[menu] Open", "Close" },
            result.Entries.Select(e => e.Key).ToArray());
        Assert.Equal(new string?[] { "menu", null },
            result.Entries.Select(e => e.Context).ToArray());
        Assert.Equal(new int?[] { 1, 5 },
            result.Entries.Select(e => e.Line).ToArray());
        Assert.Equal(new[] { "Aç", "Kapat" },
            result.Entries.Select(e => e.ExistingTranslation).ToArray());
    }

    [Fact]
    public async Task PluralForms_EntryLimit_StopsWithinMessageAndWarnsOnce()
    {
        using var tree = new TempTree();

        var result = await new PoLocalizationAdapter().ReadAsync(
            tree.Write("plural.po", """
            msgid "One file"
            msgid_plural "%d files"
            msgstr[0] "Bir dosya"
            msgstr[1] "%d dosya"
            msgstr[2] "%d dosya (diğer)"
            """),
            new LocalizationFormatReadOptions { MaxEntries = 2 });

        Assert.True(result.Success);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(new[] { "One file", "%d files" },
            result.Entries.Select(e => e.SourceText).ToArray());
        Assert.Equal(new int?[] { 0, 1 },
            result.Entries.Select(e => e.PluralIndex).ToArray());
        Assert.True(result.Truncated);
        Assert.Single(result.Warnings, w => w.Contains("Kayıt sınırı", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultipleMessages_EntryLimit_StopsAtLimitAndWarnsOnce()
    {
        using var tree = new TempTree();

        var result = await new PoLocalizationAdapter().ReadAsync(
            tree.Write("strings.po", """
            msgid "A"
            msgstr "Bir"

            msgid "B"
            msgstr "İki"

            msgid "C"
            msgstr "Üç"
            """),
            new LocalizationFormatReadOptions { MaxEntries = 2 });

        Assert.True(result.Success);
        Assert.Equal(new[] { "A", "B" },
            result.Entries.Select(e => e.SourceText).ToArray());
        Assert.DoesNotContain(result.Entries, e => e.SourceText == "C");
        Assert.True(result.Truncated);
        Assert.Single(result.Warnings, w => w.Contains("Kayıt sınırı", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultipleMessages_AtExactEntryLimit_AreNotTruncated()
    {
        using var tree = new TempTree();

        var result = await new PoLocalizationAdapter().ReadAsync(
            tree.Write("strings.po", """
            msgid "A"
            msgstr "Bir"

            msgid "B"
            msgstr "İki"
            """),
            new LocalizationFormatReadOptions { MaxEntries = 2 });

        Assert.True(result.Success);
        Assert.Equal(2, result.Entries.Count);
        Assert.False(result.Truncated);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Kayıt sınırı", StringComparison.Ordinal));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// INI ayrıştırıcısı
// ─────────────────────────────────────────────────────────────────────────────

public class IniLocalizationAdapterTests
{
    private static async Task<LocalizationTextReadResult> ReadIniAsync(TempTree tree, string content)
        => await new IniLocalizationAdapter().ReadAsync(tree.Write("strings.ini", content));

    [Fact]
    public async Task SectionsAndKeys_AreParsedWithContext()
    {
        using var tree = new TempTree();

        var result = await ReadIniAsync(tree, """
        [Menu]
        Title=Ana Menü
        StartGame=Oyuna Başla

        [Dialog]
        Greeting=Merhaba yolcu
        """);

        Assert.True(result.Success);
        Assert.Equal(3, result.Entries.Count);

        Assert.Equal("Ana Menü", result.Entries[0].SourceText);
        Assert.Equal("Menu", result.Entries[0].Context);
        Assert.Equal("Menu / Title", result.Entries[0].Key);
        Assert.Equal(LocalizationTextKind.IniValue, result.Entries[0].Kind);
        Assert.Equal("INI", result.Entries[0].FormatKey);

        Assert.Equal("Dialog", result.Entries[2].Context);
        Assert.Equal("Dialog / Greeting", result.Entries[2].Key);
        Assert.All(result.Entries, e => Assert.Equal(LocalizationEntryStatus.Translatable, e.Status));
    }

    [Fact]
    public async Task ValueContainingEquals_IsPreserved()
    {
        using var tree = new TempTree();

        var result = await ReadIniAsync(tree, """
        [Net]
        Url=http://example.com/?a=1&b=2
        Formula=x=y+z
        """);

        var url = result.Entries.Single(e => e.Key == "Net / Url");
        Assert.Equal("http://example.com/?a=1&b=2", url.SourceText);

        var formula = result.Entries.Single(e => e.Key == "Net / Formula");
        Assert.Equal("x=y+z", formula.SourceText);
    }

    [Fact]
    public async Task CommentsAndBlankLines_AreIgnored()
    {
        using var tree = new TempTree();

        var result = await ReadIniAsync(tree, """
        ; eski usul yorum
        # diyez yorum

        [Main]
        ; bölüm içi yorum
        Title=Merhaba
        """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Merhaba", entry.SourceText);
        Assert.Equal(6, entry.Line);
    }

    [Fact]
    public async Task EmptyAndNumericValues_AreSkipped()
    {
        using var tree = new TempTree();

        var result = await ReadIniAsync(tree, """
        [Video]
        Empty=
        Width=1920
        Fullscreen=true
        Path=C:\Game\bin
        Title=Video Ayarları
        """);

        Assert.Equal(LocalizationEntryStatus.Skipped, result.Entries.Single(e => e.Key == "Video / Empty").Status);
        Assert.Equal(LocalizationEntryStatus.Skipped, result.Entries.Single(e => e.Key == "Video / Width").Status);
        Assert.Equal(LocalizationEntryStatus.Skipped, result.Entries.Single(e => e.Key == "Video / Fullscreen").Status);
        Assert.Equal(LocalizationEntryStatus.Skipped, result.Entries.Single(e => e.Key == "Video / Path").Status);
        Assert.Equal(LocalizationEntryStatus.Translatable, result.Entries.Single(e => e.Key == "Video / Title").Status);

        Assert.Equal(4, result.SkippedCount);
        Assert.Equal(1, result.TranslatableCount);
    }

    [Fact]
    public async Task MalformedLines_AreReportedNotSilentlyIgnored()
    {
        using var tree = new TempTree();

        var result = await ReadIniAsync(tree, """
        [Main]
        BuSatirdaEsittirYok
        Title=Merhaba
        """);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("ayrıştırılamadı", StringComparison.OrdinalIgnoreCase));

        var broken = result.Entries.Single(e => e.SourceText == "BuSatirdaEsittirYok");
        Assert.Equal(LocalizationEntryStatus.Skipped, broken.Status);
        Assert.Contains("'=' ayırıcısı yok", broken.SkipReason);

        Assert.Equal(LocalizationEntryStatus.Translatable,
            result.Entries.Single(e => e.SourceText == "Merhaba").Status);
    }

    [Fact]
    public async Task DuplicateKeys_GetUniqueKeys()
    {
        using var tree = new TempTree();

        var result = await ReadIniAsync(tree, """
        [Main]
        Title=Birinci
        Title=İkinci
        """);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(2, result.Entries.Select(e => e.Key).Distinct().Count());
        Assert.Equal("Birinci", result.Entries[0].SourceText);
        Assert.Equal("İkinci", result.Entries[1].SourceText);
        Assert.Contains("#2", result.Entries[1].Key);
    }

    [Fact]
    public async Task OrderIsPreserved()
    {
        using var tree = new TempTree();

        var result = await ReadIniAsync(tree, """
        First=Bir
        [S]
        A=İki
        B=Üç
        """);

        Assert.Equal(new[] { "Bir", "İki", "Üç" }, result.Entries.Select(e => e.SourceText).ToArray());
        Assert.Null(result.Entries[0].Context);
        Assert.Equal("S", result.Entries[1].Context);
    }

    [Fact]
    public async Task TechnicalConfigFile_ProducesWarning()
    {
        using var tree = new TempTree();

        var result = await ReadIniAsync(tree, """
        [Video]
        Width=1920
        Height=1080
        Fullscreen=true
        Vsync=0
        """);

        Assert.True(result.Success);
        Assert.Equal(0, result.TranslatableCount);
        Assert.Contains(result.Warnings,
            w => w.Contains("teknik bir ayar dosyası olabilir", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LocalizationHintedFile_DoesNotProduceConfigWarning()
    {
        using var tree = new TempTree();

        var result = await new IniLocalizationAdapter().ReadAsync(tree.Write(@"localization\menu.ini", """
        [Video]
        Width=1920
        Height=1080
        Fullscreen=true
        Vsync=0
        Title=Ana Menü
        """));

        Assert.Equal(1, result.TranslatableCount);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("yapılandırma dosyası olabilir"));
    }

    [Fact]
    public async Task UnhintedIni_ProducesContextWarning()
    {
        using var tree = new TempTree();

        // "app.ini": dosya adı/klasörü yerelleştirme ipucu içermiyor.
        var result = await new IniLocalizationAdapter().ReadAsync(tree.Write("app.ini", """
        [General]
        Title=Merhaba dünya
        """));

        Assert.Equal(1, result.TranslatableCount);
        Assert.Contains(result.Warnings,
            w => w.Contains("yerelleştirme ipucu içermiyor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Placeholders_ArePreservedInIni()
    {
        using var tree = new TempTree();

        var result = await ReadIniAsync(tree, """
        [Game]
        Welcome=Hoş geldin {playerName}
        """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("{playerName}", entry.Placeholders.Signature);
        Assert.Equal("{playerName}", entry.PlaceholderSignature);
    }

    [Theory]
    [InlineData("\r", "CR-only")]
    [InlineData("\n", "LF")]
    [InlineData("\r\n", "CRLF")]
    public async Task LineEndings_PreserveRecordAndLineBoundaries(string newline, string lineEndingName)
    {
        using var tree = new TempTree();

        var content = string.Join(newline, new[]
        {
            "[Menu]",
            "Title=Ana Menü",
            "StartGame=Oyuna Başla",
            "",
            "[Dialog]",
            "Greeting=Merhaba yolcu"
        });

        var result = await ReadIniAsync(tree, content);

        Assert.True(result.Success, $"{lineEndingName} satır sonları okunamadı.");
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(new[] { "Menu / Title", "Menu / StartGame", "Dialog / Greeting" },
            result.Entries.Select(e => e.Key).ToArray());
        Assert.Equal(new int?[] { 2, 3, 6 }, result.Entries.Select(e => e.Line).ToArray());
        Assert.Equal(new[] { "Ana Menü", "Oyuna Başla", "Merhaba yolcu" },
            result.Entries.Select(e => e.SourceText).ToArray());
        Assert.Equal(new[] { "Menu", "Menu", "Dialog" },
            result.Entries.Select(e => e.Context).ToArray());
    }

    [Fact]
    public async Task EmptyFile_IsReportedAsEmpty()
    {
        using var tree = new TempTree();

        var result = await ReadIniAsync(tree, "   \n\n  ");

        Assert.Equal(LocalizationReadStatus.EmptyFile, result.Status);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Önizleme servisi ile uçtan uca (PO + INI)
// ─────────────────────────────────────────────────────────────────────────────

public class PoIniPreviewIntegrationTests
{
    [Fact]
    public async Task PoFile_IsPreviewedThroughService()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var file = tree.Write(@"Localization\tr.po", """
        msgid "Start"
        msgstr "Başla"
        """);

        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, file, detectedFormat: "PO");

        var result = await PreviewTestHelper.CreatePreviewService(database).PreviewAsync(installId, fileId);

        Assert.True(result.Success);
        Assert.True(result.FormatSupported);
        Assert.Equal("PO", result.AdapterName);
        Assert.Equal(1, result.TranslatableCount);
        Assert.Equal("Başla", Assert.Single(result.Entries).ExistingTranslation);
    }

    [Fact]
    public async Task IniFile_IsPreviewedThroughService()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var file = tree.Write(@"Localization\menu.ini", """
        [Menu]
        Title=Ana Menü
        """);

        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, file, detectedFormat: "INI");

        var result = await PreviewTestHelper.CreatePreviewService(database).PreviewAsync(installId, fileId);

        Assert.True(result.Success);
        Assert.Equal("INI", result.AdapterName);
        Assert.Equal("Ana Menü", Assert.Single(result.Entries).SourceText);
    }
}




