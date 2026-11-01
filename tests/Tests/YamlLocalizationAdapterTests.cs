namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;

/// <summary>YAML adaptörünün salt okunur önizleme davranışını doğrular.</summary>
public class YamlLocalizationAdapterTests
{
    [Fact]
    public async Task SimpleYaml_ExtractsStrings_AndSkipsNonStringScalars()
    {
        using var tree = new TempTree();
        var path = tree.Write(
            "strings.yaml",
            """
            title: My Game
            menu:
              play: Play
              quit: "Quit & Save"
            empty: ""
            answer: 42
            enabled: true
            nothing: null
            """);

        var adapter = new YamlLocalizationAdapter();
        var result = await adapter.ReadAsync(path);

        Assert.True(result.Success);
        Assert.False(adapter.SupportsWrite);
        Assert.Contains(".yaml", adapter.SupportedExtensions);
        Assert.Contains(".yml", adapter.SupportedExtensions);

        Assert.Equal("My Game", Entry(result, "title").SourceText);
        Assert.Equal("Play", Entry(result, "menu.play").SourceText);
        Assert.Equal("Quit & Save", Entry(result, "menu.quit").SourceText);
        Assert.Equal(LocalizationTextKind.YamlScalar, Entry(result, "title").Kind);

        Assert.Equal(LocalizationEntryStatus.Skipped, Entry(result, "empty").Status);
        Assert.Equal(LocalizationEntryStatus.Skipped, Entry(result, "answer").Status);
        Assert.Equal(LocalizationEntryStatus.Skipped, Entry(result, "enabled").Status);
        Assert.Equal(LocalizationEntryStatus.Skipped, Entry(result, "nothing").Status);
        Assert.Contains("sayı", Entry(result, "answer").SkipReason);
        Assert.Contains("boolean", Entry(result, "enabled").SkipReason);
    }

    [Fact]
    public async Task NestedMappingsAndLists_ProduceDotAndIndexPaths()
    {
        using var tree = new TempTree();
        var path = tree.Write(
            "game.yml",
            """
            game:
              title: My Game
              chapters:
                - name: Intro
                  lines:
                    - Hello
                    - "Welcome, {player}"
                - name: Finale
                  lines: [The end, "Thanks"]
            """);

        var result = await new YamlLocalizationAdapter().ReadAsync(path);

        Assert.True(result.Success);
        Assert.Equal("My Game", Entry(result, "game.title").SourceText);
        Assert.Equal("Intro", Entry(result, "game.chapters[0].name").SourceText);
        Assert.Equal("Hello", Entry(result, "game.chapters[0].lines[0]").SourceText);
        Assert.Equal("Welcome, {player}", Entry(result, "game.chapters[0].lines[1]").SourceText);
        Assert.Equal("Finale", Entry(result, "game.chapters[1].name").SourceText);
        Assert.Equal("The end", Entry(result, "game.chapters[1].lines[0]").SourceText);
        Assert.Equal("Thanks", Entry(result, "game.chapters[1].lines[1]").SourceText);

        var placeholder = Entry(result, "game.chapters[0].lines[1]");
        Assert.Contains(placeholder.Placeholders.Tokens, token => token.Value == "{player}");
    }

    [Fact]
    public async Task BlockScalars_QuotesAndComments_AreHandled()
    {
        using var tree = new TempTree();
        var path = tree.Write(
            "text.yaml",
            """
            description: |
              Line one
              Line two
            folded: >-
              Hello
              world
            commented: value # açıklama
            hash: "a # b"
            """);

        var result = await new YamlLocalizationAdapter().ReadAsync(path);

        Assert.True(result.Success);
        Assert.Equal("Line one\nLine two\n", Entry(result, "description").SourceText);
        Assert.Equal("Hello world", Entry(result, "folded").SourceText);
        Assert.Equal("value", Entry(result, "commented").SourceText);
        Assert.Equal("a # b", Entry(result, "hash").SourceText);
    }

    [Fact]
    public async Task FlowCollections_ProduceIndexedAndNestedPaths()
    {
        using var tree = new TempTree();
        var path = tree.Write(
            "flow.yaml",
            """
            items: [one, "two, three"]
            map: {a: Alpha, nested: {b: Beta}}
            """);

        var result = await new YamlLocalizationAdapter().ReadAsync(path);

        Assert.True(result.Success);
        Assert.Equal("one", Entry(result, "items[0]").SourceText);
        Assert.Equal("two, three", Entry(result, "items[1]").SourceText);
        Assert.Equal("Alpha", Entry(result, "map.a").SourceText);
        Assert.Equal("Beta", Entry(result, "map.nested.b").SourceText);
    }

    [Fact]
    public async Task InvalidYaml_ReturnsControlledSyntaxError()
    {
        using var tree = new TempTree();
        var path = tree.Write(
            "broken.yaml",
            "menu:\n\tplay: Play");

        var result = await new YamlLocalizationAdapter().ReadAsync(path);

        Assert.False(result.Success);
        Assert.Equal(LocalizationReadStatus.InvalidSyntax, result.Status);
        Assert.Contains("satır 2", result.ErrorMessage);
        Assert.Contains("sekme", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Entries);

        var indentationPath = tree.Write(
            "indent.yaml",
            "title: A\n  child: B");

        var indentationResult = await new YamlLocalizationAdapter().ReadAsync(indentationPath);

        Assert.False(indentationResult.Success);
        Assert.Equal(LocalizationReadStatus.InvalidSyntax, indentationResult.Status);
        Assert.Contains("girinti", indentationResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MaxEntriesAndMaxFileBytes_AreEnforced()
    {
        using var tree = new TempTree();
        var entriesPath = tree.Write(
            "entries.yaml",
            """
            a: one
            b: two
            c: three
            """);

        var limited = await new YamlLocalizationAdapter().ReadAsync(
            entriesPath,
            new LocalizationFormatReadOptions { MaxEntries = 2 });

        Assert.True(limited.Success);
        Assert.True(limited.Truncated);
        Assert.Equal(2, limited.Entries.Count);
        Assert.NotEmpty(limited.Warnings);

        var tooLarge = await new YamlLocalizationAdapter().ReadAsync(
            entriesPath,
            new LocalizationFormatReadOptions { MaxFileBytes = 4 });

        Assert.False(tooLarge.Success);
        Assert.Equal(LocalizationReadStatus.FileTooLarge, tooLarge.Status);
    }

    [Fact]
    public async Task PreviewService_ResolvesYamlAdapter()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var path = tree.Write(
            "Localization/strings.yaml",
            """
            menu:
              play: Play
              quit: Quit
            """);

        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database,
            tree.Root,
            path,
            "YAML");

        var service = PreviewTestHelper.CreatePreviewService(database);
        var result = await service.PreviewAsync(installId, fileId);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("YAML", result.FormatKey);
        Assert.Equal("YAML", result.AdapterName);
        Assert.Equal(2, result.TranslatableCount);
        Assert.Equal("Play", Entry(result.Entries, "menu.play").SourceText);
    }

    private static LocalizationTextEntry Entry(
        LocalizationTextReadResult result,
        string key)
        => result.Entries.Single(entry => entry.Key == key);

    private static LocalizationTextEntry Entry(
        IReadOnlyList<LocalizationTextEntry> entries,
        string key)
        => entries.Single(entry => entry.Key == key);
}
