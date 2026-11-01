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

/// <summary>CSV adaptörünün güvenli yazma akışını doğrular.</summary>
public class CsvLocalizationWriteTests
{
    [Fact]
    public async Task WriteAsync_ValidCsv_UpdatesOnlyTranslationColumn()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var original = "key,text,translation\n1,Play,\n2,Quit,\n";
        var path = tree.Write(@"Localization\menu.csv", original);
        var originalSha = ComputeSha256(path);
        var (installId, fileId) = await CreateInstallAndFileAsync(database, tree.Root, path);
        var service = CreateService(database, tree.Root);

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string>
            {
                ["satır 2 / translation"] = "Oyna",
                ["satır 2 / text"] = "Bu sütun yazılmamalı"
            });

        Assert.True(result.Success);
        Assert.Equal(LocalizationWriteStatus.Success, result.Status);
        Assert.Equal(originalSha, result.OriginalSha256);
        Assert.Equal(ComputeSha256(path), result.NewSha256);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("1,Play,Oyna", content);
        Assert.Contains("2,Quit,", content);
        Assert.DoesNotContain("Bu sütun yazılmamalı", content);

        using var context = database.CreateContext();
        var backup = context.BackupRecords.Single();
        Assert.False(backup.IsRestored);
        Assert.True(File.Exists(backup.BackupFilePath));
    }

    [Fact]
    public async Task WriteAsync_CsvHashFailure_LeavesOriginalUntouched()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var original = "key,text,translation\n1,Play,\n";
        var path = tree.Write(@"Localization\menu.csv", original);
        var originalSha = ComputeSha256(path);
        var (installId, fileId) = await CreateInstallAndFileAsync(database, tree.Root, path);
        var service = CreateService(database, tree.Root, new BrokenTemporaryHashProvider());

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["satır 2 / translation"] = "Oyna" });

        Assert.False(result.Success);
        Assert.Equal(LocalizationWriteStatus.VerificationFailed, result.Status);
        Assert.Equal(originalSha, ComputeSha256(path));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteAsync_CsvReplaceFailure_RestoresOriginal()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var original = "key,text,translation\n1,Play,\n";
        var path = tree.Write(@"Localization\menu.csv", original);
        var originalSha = ComputeSha256(path);
        var (installId, fileId) = await CreateInstallAndFileAsync(database, tree.Root, path);
        var service = CreateService(database, tree.Root, replacer: new ThrowOnceReplacer());

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["satır 2 / translation"] = "Oyna" });

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
            new CsvLocalizationAdapter()
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
            DetectedFormat = "CSV",
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
