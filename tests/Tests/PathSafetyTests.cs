namespace GameLanguageExplorer.Tests;

using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Ara klasör junction/symlink'lerinin kök sınırını aşamadığını doğrular.
/// Ortam bağlantı oluşturmaya izin vermiyorsa testler açıkça skipped raporlanır.
/// </summary>
public class PathSafetyResolvedTests
{
    [Fact]
    public void NormalFileAndSimilarRoot_AreEvaluatedTextually()
    {
        using var tree = new TempTree();
        var game = Path.Combine(tree.Root, "Game");
        var file = tree.Write(@"Game\Localization\menu.json", "{}");
        var similar = tree.Write(@"Game2\menu.json", "{}");

        Assert.True(PathSafety.IsUnderRoot(file, game));
        Assert.True(PathSafety.IsUnderRootResolved(file, game));
        Assert.False(PathSafety.IsUnderRoot(similar, game));
        Assert.False(PathSafety.IsUnderRootResolved(similar, game));
    }

    [JunctionFact]
    public void IntermediateJunctionToOutside_IsRejected()
    {
        using var tree = new TempTree();
        var game = Path.Combine(tree.Root, "Game");
        var outside = Path.Combine(tree.Root, "Outside");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "secret.json");
        File.WriteAllText(outsideFile, "{}");

        var junction = Path.Combine(game, "link");
        Assert.True(CreateJunction(junction, outside), "Test junction'ı oluşturulamadı.");
        var candidate = Path.Combine(junction, "secret.json");

        Assert.True(PathSafety.IsUnderRoot(candidate, game));
        Assert.False(PathSafety.IsUnderRootResolved(candidate, game));
        Assert.True(File.Exists(outsideFile));
    }

    [JunctionFact]
    public async Task BackupDelete_JunctionEscape_IsRejectedAndFileKept()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var backupDirectory = Path.Combine(tree.Root, "backups");
        var outside = Path.Combine(tree.Root, "outside");
        Directory.CreateDirectory(backupDirectory);
        Directory.CreateDirectory(outside);
        var secret = Path.Combine(outside, "keep.bak");
        await File.WriteAllTextAsync(secret, "outside");

        var junction = Path.Combine(backupDirectory, "link");
        Assert.True(CreateJunction(junction, outside), "Test junction'ı oluşturulamadı.");

        int recordId;
        using (var context = database.CreateContext())
        {
            var record = new BackupRecord
            {
                OriginalFilePath = Path.Combine(tree.Root, "Game", "menu.json"),
                BackupFilePath = Path.Combine(junction, "keep.bak"),
                OriginalSha256 = new string('a', 64),
                CreatedAt = DateTime.UtcNow
            };
            context.BackupRecords.Add(record);
            await context.SaveChangesAsync();
            recordId = record.Id;
        }

        var service = new BackupManagementService(
            database.CreateFactory(),
            new LocalizationWriteOptions { BackupDirectory = backupDirectory });

        var result = await service.DeleteAsync(recordId);

        Assert.Equal(BackupDeleteStatus.PathRejected, result.Status);
        Assert.True(File.Exists(secret));

        using var readContext = database.CreateContext();
        Assert.Single(readContext.BackupRecords);
    }

    [JunctionFact]
    public async Task Preview_JunctionEscape_IsPathRejected()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var game = Path.Combine(tree.Root, "Game");
        var outside = Path.Combine(tree.Root, "Outside");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "menu.json");
        await File.WriteAllTextAsync(outsideFile, """{"greeting":"Hello"}""");

        var junction = Path.Combine(game, "link");
        Assert.True(CreateJunction(junction, outside), "Test junction'ı oluşturulamadı.");
        var candidate = Path.Combine(junction, "menu.json");

        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, game, candidate, "JSON");
        var service = PreviewTestHelper.CreatePreviewService(database);

        var result = await service.PreviewAsync(installId, fileId);

        Assert.False(result.Success);
        Assert.Equal(LocalizationPreviewFailureKind.PathRejected, result.Failure);
        _ = outsideFile;
    }

    [JunctionFact]
    public async Task Restore_JunctionEscape_IsPathRejected()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var game = Path.Combine(tree.Root, "Game");
        var outside = Path.Combine(tree.Root, "Outside");
        var backupDirectory = Path.Combine(tree.Root, "backups");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(backupDirectory);

        var outsideFile = Path.Combine(outside, "menu.json");
        await File.WriteAllTextAsync(outsideFile, """{"x":"new"}""");
        var backup = Path.Combine(backupDirectory, "menu.bak");
        await File.WriteAllTextAsync(backup, """{"x":"old"}""");

        var junction = Path.Combine(game, "link");
        Assert.True(CreateJunction(junction, outside), "Test junction'ı oluşturulamadı.");
        var candidate = Path.Combine(junction, "menu.json");

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, game);
        int fileId;
        using (var context = database.CreateContext())
        {
            var file = new DiscoveredFile
            {
                InstalledGameId = installId,
                FilePath = candidate,
                FileName = "menu.json",
                FileSize = new FileInfo(outsideFile).Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(outsideFile)))
                    .ToLowerInvariant(),
                DetectedFormat = "JSON",
                DetectionConfidence = "High",
                IsReadable = true,
                IsWritable = true
            };
            context.DiscoveredFiles.Add(file);
            await context.SaveChangesAsync();
            fileId = file.Id;

            context.BackupRecords.Add(new BackupRecord
            {
                OriginalFilePath = candidate,
                BackupFilePath = backup,
                OriginalSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(backup)))
                    .ToLowerInvariant(),
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var service = new LocalizationWriteService(
            database.CreateFactory(),
            PreviewTestHelper.CreateRegistry(),
            new LocalizationWriteOptions { BackupDirectory = backupDirectory },
            logger: NullLogger<LocalizationWriteService>.Instance);

        int backupId;
        using (var context = database.CreateContext())
        {
            backupId = context.BackupRecords.Single().Id;
        }

        var result = await service.RestoreAsync(backupId);

        Assert.Equal(LocalizationWriteStatus.PathRejected, result.Status);
        Assert.Contains("new", await File.ReadAllTextAsync(outsideFile));
        _ = fileId;
    }



    private static bool CreateJunction(string linkPath, string targetPath)
    {
        try
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(10_000);
            return process.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Junction oluşturulamayan ortamlarda testleri xUnit v2 ile açıkça atlar.</summary>
    public sealed class JunctionFactAttribute : FactAttribute
    {
        private static readonly string? UnsupportedReason = DetectSupport();

        public JunctionFactAttribute()
        {
            if (UnsupportedReason is not null)
            {
                Skip = UnsupportedReason;
            }
        }

        private static string? DetectSupport()
        {
            if (!OperatingSystem.IsWindows())
            {
                return "Ara klasör junction testi yalnızca Windows'ta çalışır.";
            }

            var root = Path.Combine(Path.GetTempPath(), "gle_junction_probe_" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(root, "target");
            var link = Path.Combine(root, "link");

            try
            {
                Directory.CreateDirectory(target);

                if (!CreateJunction(link, target))
                {
                    return "Test ortamı junction oluşturmaya izin vermiyor.";
                }

                return null;
            }
            catch (Exception exception)
            {
                return "Junction desteği belirlenemedi: " + exception.Message;
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }
}
