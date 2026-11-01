namespace GameLanguageExplorer.Tests;

using System.IO;
using GameLanguageExplorer.Core.Services.Localization.Export;

/// <summary>JSON, CSV ve PO dışa aktarma çıktılarını doğrular.</summary>
public class LocalizationExportServiceTests
{
    [Theory]
    [InlineData(LocalizationExportFormat.Json, ".json")]
    [InlineData(LocalizationExportFormat.Csv, ".csv")]
    [InlineData(LocalizationExportFormat.Po, ".po")]
    public async Task Export_WritesSelectedFormat_AndPreservesPlaceholder(
        LocalizationExportFormat format,
        string extension)
    {
        using var tree = new TempTree();
        var target = Path.Combine(tree.Root, "exports", "menu" + extension);
        var service = new LocalizationExportService();
        var entry = new LocalizationExportEntry(
            "/menu/greeting",
            "Hello {playerName}",
            "Merhaba {playerName}");

        var result = await service.ExportAsync(target, format, new[] { entry });

        Assert.True(result.Success);
        Assert.Equal(1, result.EntryCount);
        Assert.True(File.Exists(target));

        var content = await File.ReadAllTextAsync(target);
        Assert.Contains("Merhaba {playerName}", content);
        Assert.Contains("Hello {playerName}", content);

        if (format == LocalizationExportFormat.Csv)
        {
            Assert.StartsWith("key,source,translation", content);
        }
    }

    [Fact]
    public async Task Export_PlaceholderMismatch_ReturnsWarning()
    {
        using var tree = new TempTree();
        var target = Path.Combine(tree.Root, "menu.json");
        var service = new LocalizationExportService();
        var entry = new LocalizationExportEntry(
            "/menu/greeting",
            "Hello {playerName}",
            "Merhaba");

        var result = await service.ExportAsync(target, LocalizationExportFormat.Json, new[] { entry });

        Assert.True(result.Success);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains("yer tutucuları", result.Warnings[0], StringComparison.OrdinalIgnoreCase);
    }
}
