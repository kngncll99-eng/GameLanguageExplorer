namespace GameLanguageExplorer.Tests;

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;
using GameLanguageExplorer.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Güvenli yazma akışının dosya ve veritabanı davranışlarını doğrular.</summary>
public class LocalizationWriteTests
{
    [Fact]
    public async Task WriteAsync_ValidJson_BacksUpAndWritesAtomically()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var original = """
        {
          "menu": {
            "play": "Play",
            "quit": "Quit"
          }
        }
        """;
        var path = tree.Write(@"Localization\menu.json", original);
        var originalSha = ComputeSha256(path);
        var (installId, fileId) = await CreateInstallAndFileAsync(database, tree.Root, path);
        var service = CreateService(database, tree.Root);

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string>
            {
                ["/menu/play"] = "Oyna",
                ["/menu/quit"] = "Çıkış"
            });

        Assert.True(result.Success);
        Assert.Equal(LocalizationWriteStatus.Success, result.Status);
        Assert.NotNull(result.BackupRecordId);
        Assert.Equal(originalSha, result.OriginalSha256);
        Assert.Equal(ComputeSha256(path), result.NewSha256);

        using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
        {
            Assert.Equal("Oyna", document.RootElement.GetProperty("menu").GetProperty("play").GetString());
            Assert.Equal("Çıkış", document.RootElement.GetProperty("menu").GetProperty("quit").GetString());
        }

        using var context = database.CreateContext();
        var backup = context.BackupRecords.Single();
        var savedFile = context.DiscoveredFiles.Single(f => f.Id == fileId);

        Assert.False(backup.IsRestored);
        Assert.True(File.Exists(backup.BackupFilePath));
        Assert.Equal(originalSha, backup.OriginalSha256);
        Assert.Equal(result.NewSha256, savedFile.Sha256);
        Assert.Contains("SHA-256", savedFile.LastValidationResult);
    }

    [Fact]
    public async Task WriteAsync_VerificationFailure_LeavesOriginalUntouched()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var original = """{"menu":{"play":"Play"}}""";
        var path = tree.Write(@"Localization\menu.json", original);
        var originalSha = ComputeSha256(path);
        var (installId, fileId) = await CreateInstallAndFileAsync(database, tree.Root, path);

        var service = CreateService(
            database,
            tree.Root,
            new BrokenTemporaryHashProvider());

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["/menu/play"] = "Oyna" });

        Assert.False(result.Success);
        Assert.Equal(LocalizationWriteStatus.VerificationFailed, result.Status);
        Assert.Equal(originalSha, ComputeSha256(path));
        Assert.Equal(original, await File.ReadAllTextAsync(path));

        using var context = database.CreateContext();
        var backup = context.BackupRecords.Single();
        Assert.False(backup.IsRestored);
        Assert.True(File.Exists(backup.BackupFilePath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.gle-tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task WriteAsync_ReplaceFailure_RestoresOriginalFromBackup()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var original = """{"menu":{"play":"Play"}}""";
        var path = tree.Write(@"Localization\menu.json", original);
        var originalSha = ComputeSha256(path);
        var (installId, fileId) = await CreateInstallAndFileAsync(database, tree.Root, path);

        var service = CreateService(
            database,
            tree.Root,
            replacer: new ThrowOnceReplacer());

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["/menu/play"] = "Oyna" });

        Assert.False(result.Success);
        Assert.Equal(LocalizationWriteStatus.Restored, result.Status);
        Assert.True(result.RestoredFromBackup);
        Assert.Equal(originalSha, ComputeSha256(path));
        Assert.Equal(original, await File.ReadAllTextAsync(path));

        using var context = database.CreateContext();
        var backup = context.BackupRecords.Single();
        Assert.True(backup.IsRestored);
        Assert.NotNull(backup.RestoredAt);
        Assert.Equal(originalSha, context.DiscoveredFiles.Single(f => f.Id == fileId).Sha256);
    }

    private static LocalizationWriteService CreateService(
        TempDatabase database,
        string root,
        ILocalizationFileHashProvider? hashProvider = null,
        ILocalizationFileReplacer? replacer = null)
    {
        var registry = new LocalizationFormatAdapterRegistry(new ILocalizationFormatAdapter[]
        {
            new JsonLocalizationAdapter()
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
            DetectedFormat = "JSON",
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

    /// <summary>Geçici dosya için kasıtlı olarak yanlış SHA-256 döndürür.</summary>
    private sealed class BrokenTemporaryHashProvider : ILocalizationFileHashProvider
    {
        private readonly Sha256LocalizationFileHashProvider _inner = new();

        public Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
            => filePath.EndsWith(".gle-tmp", StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult(new string('0', 64))
                : _inner.ComputeSha256Async(filePath, cancellationToken);
    }

    /// <summary>İlk taşımayı yapar ve hata fırlatır; geri yükleme çağrısında normal davranır.</summary>
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
