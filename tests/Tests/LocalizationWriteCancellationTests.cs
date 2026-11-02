namespace GameLanguageExplorer.Tests;

using System.IO;
using System.Security.Cryptography;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;
using GameLanguageExplorer.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Kritik replace noktası, iptal, doğrulama ve geri yükleme senaryolarında
/// dosya/DB/yedek tutarlılığını doğrular.
/// </summary>
public class LocalizationWriteCancellationTests
{
    private const string OriginalJson = """{"menu":{"play":"Play"}}""";

    [Fact]
    public async Task CancelImmediatelyAfterReplace_CompletesVerificationAndDatabaseUpdate()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var (path, installId, fileId, originalSha) = await CreateFileAsync(database, tree);

        using var cts = new CancellationTokenSource();
        var replacer = new ActionReplacer((temporary, destination) =>
        {
            File.Move(temporary, destination, overwrite: true);
            cts.Cancel();
        });

        var service = CreateService(database, tree.Root, replacer: replacer);
        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["/menu/play"] = "Oyna" },
            cts.Token);

        Assert.True(result.Success);
        Assert.Equal(LocalizationWriteStatus.Success, result.Status);
        Assert.Contains("Oyna", await File.ReadAllTextAsync(path));
        Assert.Equal(result.NewSha256, ComputeSha256(path));

        using var context = database.CreateContext();
        Assert.Equal(result.NewSha256, context.DiscoveredFiles.Single(f => f.Id == fileId).Sha256);
        Assert.False(context.BackupRecords.Single().IsRestored);
        Assert.NotEqual(originalSha, result.NewSha256);
    }

    [Fact]
    public async Task CancelBeforeReplace_LeavesOriginalWithoutUnnecessaryRestore()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var (path, installId, fileId, originalSha) = await CreateFileAsync(database, tree);

        using var cts = new CancellationTokenSource();
        var hashProvider = new CancelAfterTemporaryHashProvider(cts);
        var service = CreateService(database, tree.Root, hashProvider);

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["/menu/play"] = "Oyna" },
            cts.Token);

        Assert.Equal(LocalizationWriteStatus.Cancelled, result.Status);
        Assert.Equal(OriginalJson, await File.ReadAllTextAsync(path));
        Assert.Equal(originalSha, ComputeSha256(path));

        using var context = database.CreateContext();
        var backup = context.BackupRecords.Single();
        Assert.False(backup.IsRestored);
        Assert.True(File.Exists(backup.BackupFilePath));
        Assert.Equal(originalSha, context.DiscoveredFiles.Single(f => f.Id == fileId).Sha256);
    }

    [Fact]
    public async Task FinalHashMismatch_RestoresOriginalAndMarksBackup()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var (path, installId, fileId, originalSha) = await CreateFileAsync(database, tree);

        var hashProvider = new BreakFinalHashProvider(path);
        var firstReplace = true;
        var replacer = new ActionReplacer((temporary, destination) =>
        {
            File.Move(temporary, destination, overwrite: true);

            if (firstReplace)
            {
                firstReplace = false;
                hashProvider.BreakNextDestinationHash = true;
            }
        });
        var service = CreateService(database, tree.Root, hashProvider, replacer);

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["/menu/play"] = "Oyna" });

        Assert.Equal(LocalizationWriteStatus.Restored, result.Status);
        Assert.True(result.RestoredFromBackup);
        Assert.Equal(OriginalJson, await File.ReadAllTextAsync(path));
        Assert.Equal(originalSha, ComputeSha256(path));

        using var context = database.CreateContext();
        Assert.True(context.BackupRecords.Single().IsRestored);
        Assert.Equal(originalSha, context.DiscoveredFiles.Single(f => f.Id == fileId).Sha256);
    }


    [Fact]
    public async Task DatabaseFailureAfterReplace_RestoresFileAndKeepsBackup()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var (path, installId, fileId, originalSha) = await CreateFileAsync(database, tree);

        using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER FailDiscoveredFileUpdate
                BEFORE UPDATE ON "DiscoveredFiles"
                BEGIN
                    SELECT RAISE(ABORT, 'forced test failure');
                END;
                """);
        }

        var service = CreateService(database, tree.Root);
        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["/menu/play"] = "Oyna" });

        Assert.Equal(LocalizationWriteStatus.RestoreFailed, result.Status);
        Assert.True(result.RestoredFromBackup);
        Assert.Contains("veritabanı", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OriginalJson, await File.ReadAllTextAsync(path));
        Assert.Equal(originalSha, ComputeSha256(path));

        using var readContext = database.CreateContext();
        Assert.Equal(originalSha, readContext.DiscoveredFiles.Single(f => f.Id == fileId).Sha256);
        var backup = readContext.BackupRecords.Single();
        Assert.True(File.Exists(backup.BackupFilePath));
    }

    [Fact]
    public async Task RestoreFailure_KeepsBackupAndReportsUnrecoveredState()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var (path, installId, fileId, originalSha) = await CreateFileAsync(database, tree);

        var service = CreateService(database, tree.Root, replacer: new FailRestoreReplacer());
        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["/menu/play"] = "Oyna" });

        Assert.Equal(LocalizationWriteStatus.RestoreFailed, result.Status);
        Assert.False(result.RestoredFromBackup);
        Assert.Contains("yedek korunuyor", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Oyna", await File.ReadAllTextAsync(path));
        Assert.NotEqual(originalSha, ComputeSha256(path));

        using var context = database.CreateContext();
        Assert.True(File.Exists(context.BackupRecords.Single().BackupFilePath));
        Assert.Equal(originalSha, context.DiscoveredFiles.Single(f => f.Id == fileId).Sha256);
    }

    private static LocalizationWriteService CreateService(
        TempDatabase database,
        string root,
        ILocalizationFileHashProvider? hashProvider = null,
        ILocalizationFileReplacer? replacer = null)
        => new(
            database.CreateFactory(),
            new LocalizationFormatAdapterRegistry(new ILocalizationFormatAdapter[]
            {
                new JsonLocalizationAdapter()
            }),
            new LocalizationWriteOptions
            {
                BackupDirectory = Path.Combine(root, "backups")
            },
            hashProvider,
            replacer,
            NullLogger<LocalizationWriteService>.Instance);

    private static async Task<(string Path, int InstallId, int FileId, string OriginalSha)>
        CreateFileAsync(TempDatabase database, TempTree tree)
    {
        var path = tree.Write(@"Localization\menu.json", OriginalJson);
        var originalSha = ComputeSha256(path);
        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);

        using var context = database.CreateContext();
        var file = new DiscoveredFile
        {
            InstalledGameId = installId,
            FilePath = path,
            FileName = Path.GetFileName(path),
            FileSize = new FileInfo(path).Length,
            Sha256 = originalSha,
            DetectedFormat = "JSON",
            DetectionConfidence = "High",
            IsReadable = true,
            IsWritable = true
        };

        context.DiscoveredFiles.Add(file);
        await context.SaveChangesAsync();
        return (path, installId, file.Id, originalSha);
    }

    private sealed class ActionReplacer : ILocalizationFileReplacer
    {
        private readonly Action<string, string> _replace;

        public ActionReplacer(Action<string, string> replace) => _replace = replace;

        public void Replace(string temporaryFilePath, string destinationFilePath)
            => _replace(temporaryFilePath, destinationFilePath);
    }

    private sealed class CancelAfterTemporaryHashProvider : ILocalizationFileHashProvider
    {
        private readonly Sha256LocalizationFileHashProvider _inner = new();
        private readonly CancellationTokenSource _cts;

        public CancelAfterTemporaryHashProvider(CancellationTokenSource cts) => _cts = cts;

        public async Task<string> ComputeSha256Async(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            var hash = await _inner.ComputeSha256Async(filePath, cancellationToken);

            if (filePath.EndsWith(".gle-tmp", StringComparison.OrdinalIgnoreCase))
            {
                _cts.Cancel();
            }

            return hash;
        }
    }

    private sealed class BreakFinalHashProvider : ILocalizationFileHashProvider
    {
        private readonly Sha256LocalizationFileHashProvider _inner = new();
        private readonly string _destinationPath;

        public BreakFinalHashProvider(string destinationPath) => _destinationPath = destinationPath;

        public bool BreakNextDestinationHash { get; set; }

        public async Task<string> ComputeSha256Async(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (BreakNextDestinationHash
                && string.Equals(filePath, _destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                BreakNextDestinationHash = false;
                return new string('0', 64);
            }

            return await _inner.ComputeSha256Async(filePath, cancellationToken);
        }
    }

    private sealed class FailRestoreReplacer : ILocalizationFileReplacer
    {
        private int _calls;

        public void Replace(string temporaryFilePath, string destinationFilePath)
        {
            if (_calls++ == 0)
            {
                File.Move(temporaryFilePath, destinationFilePath, overwrite: true);
            }

            throw new IOException("Test için geri yükleme taşıması başarısız.");
        }
    }

    private static string ComputeSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}

