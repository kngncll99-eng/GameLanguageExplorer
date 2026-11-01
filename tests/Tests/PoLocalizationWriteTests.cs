namespace GameLanguageExplorer.Tests;

using System.IO;
using System.Security.Cryptography;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;
using GameLanguageExplorer.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>PO adaptörünün güvenli yazma ve yapı koruma davranışını doğrular.</summary>
public class PoLocalizationWriteTests
{
    [Fact]
    public async Task WriteAsync_ValidPo_UpdatesOnlyMsgstrAndRemovesFuzzy()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var original =
            "# Translator comment\n" +
            "#: menu.play\n" +
            "#, fuzzy\n" +
            "msgid \"Play\"\n" +
            "msgstr \"Oy\"\n" +
            "\n" +
            "#: menu.quit\n" +
            "msgid \"Quit\"\n" +
            "msgstr \"Çık\"\n";
        var expected =
            "# Translator comment\n" +
            "#: menu.play\n" +
            "msgid \"Play\"\n" +
            "msgstr \"Oyna\"\n" +
            "\n" +
            "#: menu.quit\n" +
            "msgid \"Quit\"\n" +
            "msgstr \"Çık\"\n";

        var path = tree.Write(@"Localization\menu.po", original);
        var originalSha = ComputeSha256(path);
        var (installId, fileId) = await CreateInstallAndFileAsync(database, tree.Root, path);
        var service = CreateService(database, tree.Root);

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["Play"] = "Oyna" });

        Assert.True(result.Success);
        Assert.Equal(LocalizationWriteStatus.Success, result.Status);
        Assert.Equal(originalSha, result.OriginalSha256);
        Assert.Equal(ComputeSha256(path), result.NewSha256);
        Assert.Equal(expected, await File.ReadAllTextAsync(path));

        using var context = database.CreateContext();
        Assert.False(context.BackupRecords.Single().IsRestored);
    }

    [Fact]
    public async Task WriteAsync_PluralPo_PreservesContextAndOtherForms()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var original =
            "#: menu.item\n" +
            "msgctxt \"menu\"\n" +
            "msgid \"Item\"\n" +
            "msgid_plural \"Items\"\n" +
            "msgstr[0] \"Öğe\"\n" +
            "msgstr[1] \"Öğeler\"\n";
        var expected =
            "#: menu.item\n" +
            "msgctxt \"menu\"\n" +
            "msgid \"Item\"\n" +
            "msgid_plural \"Items\"\n" +
            "msgstr[0] \"Öğe\"\n" +
            "msgstr[1] \"Yeni Öğeler\"\n";

        var path = tree.Write(@"Localization\items.po", original);
        var (installId, fileId) = await CreateInstallAndFileAsync(database, tree.Root, path);
        var service = CreateService(database, tree.Root);

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["[menu] Item [1]"] = "Yeni Öğeler" });

        Assert.True(result.Success);
        Assert.Equal(expected, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteAsync_PoHashFailure_LeavesOriginalUntouched()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var original = "msgid \"Play\"\nmsgstr \"Oy\"\n";
        var path = tree.Write(@"Localization\menu.po", original);
        var originalSha = ComputeSha256(path);
        var (installId, fileId) = await CreateInstallAndFileAsync(database, tree.Root, path);
        var service = CreateService(database, tree.Root, new BrokenTemporaryHashProvider());

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["Play"] = "Oyna" });

        Assert.False(result.Success);
        Assert.Equal(LocalizationWriteStatus.VerificationFailed, result.Status);
        Assert.Equal(originalSha, ComputeSha256(path));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteAsync_PoReplaceFailure_RestoresOriginal()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var original = "msgid \"Play\"\nmsgstr \"Oy\"\n";
        var path = tree.Write(@"Localization\menu.po", original);
        var originalSha = ComputeSha256(path);
        var (installId, fileId) = await CreateInstallAndFileAsync(database, tree.Root, path);
        var service = CreateService(database, tree.Root, replacer: new ThrowOnceReplacer());

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["Play"] = "Oyna" });

        Assert.False(result.Success);
        Assert.Equal(LocalizationWriteStatus.Restored, result.Status);
        Assert.True(result.RestoredFromBackup);
        Assert.Equal(originalSha, ComputeSha256(path));
        Assert.Equal(original, await File.ReadAllTextAsync(path));

        using var context = database.CreateContext();
        Assert.True(context.BackupRecords.Single().IsRestored);
    }

    private static LocalizationWriteService CreateService(
        TempDatabase database,
        string root,
        ILocalizationFileHashProvider? hashProvider = null,
        ILocalizationFileReplacer? replacer = null)
    {
        var registry = new LocalizationFormatAdapterRegistry(new ILocalizationFormatAdapter[]
        {
            new PoLocalizationAdapter()
        });

        return new LocalizationWriteService(
            database.CreateFactory(),
            registry,
            new LocalizationWriteOptions
            {
                BackupDirectory = Path.Combine(root, "backups")
            },
            hashProvider,
            replacer,
            NullLogger<LocalizationWriteService>.Instance);
    }

    private static async Task<(int InstallId, int FileId)> CreateInstallAndFileAsync(
        TempDatabase database,
        string root,
        string path)
    {
        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, root);

        using var context = database.CreateContext();
        var file = new DiscoveredFile
        {
            InstalledGameId = installId,
            FilePath = path,
            FileName = Path.GetFileName(path),
            FileSize = new FileInfo(path).Length,
            Sha256 = ComputeSha256(path),
            DetectedFormat = "PO",
            DetectionConfidence = "High",
            IsReadable = true,
            IsWritable = true
        };

        context.DiscoveredFiles.Add(file);
        await context.SaveChangesAsync();

        return (installId, file.Id);
    }

    private static string ComputeSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed class BrokenTemporaryHashProvider : ILocalizationFileHashProvider
    {
        private readonly Sha256LocalizationFileHashProvider _inner = new();

        public Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
            => filePath.EndsWith(".gle-tmp", StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult(new string('0', 64))
                : _inner.ComputeSha256Async(filePath, cancellationToken);
    }

    private sealed class ThrowOnceReplacer : ILocalizationFileReplacer
    {
        private int _calls;

        public void Replace(string temporaryFilePath, string destinationFilePath)
        {
            var shouldThrow = _calls++ == 0;

            File.Move(temporaryFilePath, destinationFilePath, overwrite: true);

            if (shouldThrow)
            {
                throw new IOException("Test için taşıma sonrası hata üretildi.");
            }
        }
    }
}

