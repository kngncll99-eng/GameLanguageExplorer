namespace GameLanguageExplorer.Tests;

using System.IO;
using System.Security.Cryptography;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Yer tutucu doğrulamasının UI'dan bağımsız olarak servis sınırında uygulandığını doğrular.
/// </summary>
public class LocalizationWriteValidationTests
{
    [Theory]
    [InlineData("menu.json", "JSON", """{"greeting":"Hello {playerName}"}""", "/greeting")]
    [InlineData("menu.ini", "INI", "[Menu]\ngreeting = Hello {playerName}\n", "Menu / greeting")]
    [InlineData("menu.po", "PO", "msgid \"Hello {playerName}\"\nmsgstr \"\"\n", "Hello {playerName}")]
    [InlineData("menu.csv", "CSV", "key,text,translation\n1,Hello {playerName},\n", "satır 2 / translation")]
    public async Task MissingPlaceholder_IsRejectedWithoutChangingFile(
        string fileName,
        string format,
        string original,
        string key)
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var path = tree.Write(Path.Combine("Localization", fileName), original);
        var originalSha = ComputeSha256(path);
        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, path, format);
        await SetFileHashAsync(database, fileId, path);

        var service = CreateService(database, tree.Root);
        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { [key] = "Merhaba" });

        Assert.False(result.Success);
        Assert.Equal(LocalizationWriteStatus.WriteFailed, result.Status);
        Assert.Contains(key, result.Message, StringComparison.Ordinal);
        Assert.Contains("yer tutucu", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.Equal(originalSha, ComputeSha256(path));

        using var context = database.CreateContext();
        Assert.Empty(context.BackupRecords);
    }

    [Fact]
    public async Task PoPluralForm_UsesItsOwnSourceTextForPlaceholderCheck()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var original =
            "msgctxt \"menu\"\n" +
            "msgid \"Item {count}\"\n" +
            "msgid_plural \"Items {count}\"\n" +
            "msgstr[0] \"Öğe {count}\"\n" +
            "msgstr[1] \"Öğeler\"\n";
        var path = tree.Write(@"Localization\items.po", original);
        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, path, "PO");
        await SetFileHashAsync(database, fileId, path);

        var service = CreateService(database, tree.Root);
        var preview = await new GameLanguageExplorer.Core.Services.Localization.Preview.Adapters
            .PoLocalizationAdapter()
            .ReadAsync(path, new GameLanguageExplorer.Core.Services.Localization.Preview
                .LocalizationFormatReadOptions());
        var pluralEntry = preview.Entries.Single(entry => entry.PluralIndex == 1);
        Assert.Equal("Items {count}", pluralEntry.SourceText);
        var key = pluralEntry.Key;

        var rejected = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { [key] = "Öğeler" });

        Assert.Equal(LocalizationWriteStatus.WriteFailed, rejected.Status);
        Assert.Contains(key, rejected.Message, StringComparison.Ordinal);
        Assert.Contains("yer tutucu", rejected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, await File.ReadAllTextAsync(path));

        var accepted = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { [key] = "Öğeler {count}" });

        Assert.True(accepted.Success, accepted.Message);
        Assert.Contains("msgstr[1] \"Öğeler {count}\"", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ValidPlaceholderTranslation_IsWritten()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var original = """{"greeting":"Hello {playerName}"}""";
        var path = tree.Write(@"Localization\menu.json", original);
        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, path, "JSON");
        await SetFileHashAsync(database, fileId, path);

        var service = CreateService(database, tree.Root);
        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["/greeting"] = "Merhaba {playerName}" });

        Assert.True(result.Success, result.Message);
        Assert.Contains("Merhaba {playerName}", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task StalePreviewSourceHash_IsRejectedBeforeAnyBackup()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var original = """{"greeting":"Hello"}""";
        var path = tree.Write(@"Localization\menu.json", original);
        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, path, "JSON");
        await SetFileHashAsync(database, fileId, path);

        var service = CreateService(database, tree.Root);
        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["/greeting"] = "Merhaba" },
            expectedSourceSha256: new string('a', 64));

        Assert.Equal(LocalizationWriteStatus.SourceChanged, result.Status);
        Assert.Equal(original, await File.ReadAllTextAsync(path));

        using var context = database.CreateContext();
        Assert.Empty(context.BackupRecords);
    }

    [Fact]
    public async Task CsvEmptyTranslationCell_UsesSourceColumnForPlaceholderCheck()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var original = "key,text,translation\n1,Hello {playerName},\n";
        var path = tree.Write(@"Localization\menu.csv", original);
        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, path, "CSV");
        await SetFileHashAsync(database, fileId, path);

        var service = CreateService(database, tree.Root);
        var rejected = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["satır 2 / translation"] = "Merhaba" });

        Assert.Equal(LocalizationWriteStatus.WriteFailed, rejected.Status);
        Assert.Equal(original, await File.ReadAllTextAsync(path));

        var accepted = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["satır 2 / translation"] = "Merhaba {playerName}" });

        Assert.True(accepted.Success);
        Assert.Contains("1,Hello {playerName},Merhaba {playerName}", await File.ReadAllTextAsync(path));
    }

    private static LocalizationWriteService CreateService(TempDatabase database, string root)
        => new(
            database.CreateFactory(),
            PreviewTestHelper.CreateRegistry(),
            new LocalizationWriteOptions { BackupDirectory = Path.Combine(root, "backups") },
            logger: NullLogger<LocalizationWriteService>.Instance);

    private static async Task SetFileHashAsync(TempDatabase database, int fileId, string path)
    {
        using var context = database.CreateContext();
        var record = context.DiscoveredFiles.Single(file => file.Id == fileId);
        record.FileSize = new FileInfo(path).Length;
        record.Sha256 = ComputeSha256(path);
        record.IsWritable = true;
        await context.SaveChangesAsync();
    }

    private static string ComputeSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
