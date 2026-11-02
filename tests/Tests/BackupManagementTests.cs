namespace GameLanguageExplorer.Tests;

using System.IO;
using System.Security.Cryptography;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.Services;
using GameLanguageExplorer.WPF.ViewModels;

/// <summary>Yedek listeleme, silme ve geri yükleme akışını doğrular.</summary>
public class BackupManagementTests
{
    [Fact]
    public async Task GetBackupsAsync_ListsNewestFirst_WithSizeAndFilter()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var backupDirectory = Path.Combine(tree.Root, "backups");
        Directory.CreateDirectory(backupDirectory);

        var originalOne = tree.Write(@"Localization\a.json", """{"x":"1"}""");
        var originalTwo = tree.Write(@"Localization\b.json", """{"x":"2"}""");
        var backupOne = WriteFile(backupDirectory, "one.bak", "one");
        var backupTwo = WriteFile(backupDirectory, "two.bak", "second");

        using (var context = database.CreateContext())
        {
            context.BackupRecords.AddRange(
                CreateRecord(originalOne, backupOne, DateTime.UtcNow.AddMinutes(-5)),
                CreateRecord(originalTwo, backupTwo, DateTime.UtcNow));
            context.SaveChanges();
        }

        var service = CreateManagementService(database, backupDirectory);

        var all = await service.GetBackupsAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal("two.bak", all[0].BackupFileName);
        Assert.Equal("b.json", all[0].OriginalFileName);
        Assert.Equal(6, all[0].BackupSizeBytes);
        Assert.True(all[0].IsBackupFileAvailable);

        var filtered = await service.GetBackupsAsync("a.json");
        Assert.Single(filtered);
        Assert.Equal("one.bak", filtered[0].BackupFileName);
    }

    [Fact]
    public async Task DeleteAsync_RemovesBackupFileAndRecord()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var backupDirectory = Path.Combine(tree.Root, "backups");
        Directory.CreateDirectory(backupDirectory);

        var original = tree.Write(@"Localization\menu.json", """{"x":"1"}""");
        var backup = WriteFile(backupDirectory, "menu.bak", "original");
        var recordId = AddRecord(database, original, backup);
        var service = CreateManagementService(database, backupDirectory);

        var result = await service.DeleteAsync(recordId);

        Assert.True(result.Succeeded);
        Assert.True(result.RecordDeleted);
        Assert.True(result.FileDeleted);
        Assert.False(File.Exists(backup));

        using var context = database.CreateContext();
        Assert.Empty(context.BackupRecords);
        Assert.True(File.Exists(original));
    }

    [Fact]
    public async Task DeleteAsync_PathOutsideBackupDirectory_IsRejected()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var backupDirectory = Path.Combine(tree.Root, "backups");
        var outsideDirectory = Path.Combine(tree.Root, "outside");
        Directory.CreateDirectory(backupDirectory);

        var original = tree.Write(@"Localization\menu.json", """{"x":"1"}""");
        var outsideBackup = WriteFile(outsideDirectory, "keep.bak", "outside");
        var recordId = AddRecord(database, original, outsideBackup);
        var service = CreateManagementService(database, backupDirectory);

        var result = await service.DeleteAsync(recordId);

        Assert.Equal(BackupDeleteStatus.PathRejected, result.Status);
        Assert.True(File.Exists(outsideBackup));

        using var context = database.CreateContext();
        Assert.Single(context.BackupRecords);
    }

    [Fact]
    public async Task ViewModel_RestoreSelected_ConfirmsAndRestoresOriginalFile()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var backupDirectory = Path.Combine(tree.Root, "backups");
        Directory.CreateDirectory(backupDirectory);

        var original = tree.Write(@"Localization\menu.json", """{"x":"new"}""");
        var backup = WriteFile(backupDirectory, "menu.bak", """{"x":"old"}""");
        await PreviewTestHelper.CreateInstallAndFileAsync(database, tree.Root, original, "JSON");
        AddRecord(database, original, backup);

        var confirmation = new RecordingConfirmationService(true);
        var viewModel = CreateViewModel(database, backupDirectory, confirmation);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Single(viewModel.Entries);

        viewModel.SelectedEntry = viewModel.Entries[0];
        Assert.True(viewModel.RestoreSelectedCommand.CanExecute(null));

        await viewModel.RestoreSelectedCommand.ExecuteAsync(null);

        Assert.True(confirmation.WasCalled);
        Assert.Contains("değiştirilecek", confirmation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("""{"x":"old"}""", await File.ReadAllTextAsync(original));
        Assert.Contains("geri yüklendi", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.Entries[0].IsRestored);

        using var context = database.CreateContext();
        Assert.True(context.BackupRecords.Single().IsRestored);
        Assert.NotNull(context.BackupRecords.Single().RestoredAt);
    }

    [Fact]
    public async Task Restore_WithoutInstallRecord_ExplainsReasonAndKeepsBackup()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var backupDirectory = Path.Combine(tree.Root, "backups");
        Directory.CreateDirectory(backupDirectory);

        var original = tree.Write(@"Localization\menu.json", """{"x":"new"}""");
        var backup = WriteFile(backupDirectory, "menu.bak", """{"x":"old"}""");
        var recordId = AddRecord(database, original, backup);

        var service = new LocalizationWriteService(
            database.CreateFactory(),
            new LocalizationFormatAdapterRegistry(Array.Empty<ILocalizationFormatAdapter>()),
            new LocalizationWriteOptions { BackupDirectory = backupDirectory });

        var result = await service.RestoreAsync(recordId);

        Assert.False(result.Success);
        Assert.Equal(LocalizationWriteStatus.PathRejected, result.Status);
        Assert.Contains("kurulum/dosya kaydı bulunamadı", result.Message, StringComparison.Ordinal);
        Assert.Contains("Yedek bozuk olduğu için değil", result.Message, StringComparison.Ordinal);
        Assert.Contains(backup, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(backup, result.BackupFilePath);
        Assert.True(File.Exists(backup));
        Assert.Equal("""{"x":"new"}""", await File.ReadAllTextAsync(original));

        using var context = database.CreateContext();
        Assert.Single(context.BackupRecords);
    }

    [Fact]
    public async Task ViewModel_DeleteSelected_RequiresConfirmation()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var backupDirectory = Path.Combine(tree.Root, "backups");
        Directory.CreateDirectory(backupDirectory);

        var original = tree.Write(@"Localization\menu.json", """{"x":"new"}""");
        var backup = WriteFile(backupDirectory, "menu.bak", """{"x":"old"}""");
        AddRecord(database, original, backup);

        var confirmation = new RecordingConfirmationService(false);
        var viewModel = CreateViewModel(database, backupDirectory, confirmation);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedEntry = viewModel.Entries[0];

        await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.True(confirmation.WasCalled);
        Assert.Contains("silinsin mi", confirmation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(backup));
        Assert.Single(viewModel.Entries);

        confirmation.Result = true;
        await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.False(File.Exists(backup));
        Assert.Empty(viewModel.Entries);
        Assert.Contains("silindi", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        using var context = database.CreateContext();
        Assert.Empty(context.BackupRecords);
        Assert.True(File.Exists(original));
    }

    private static IBackupManagementService CreateManagementService(
        TempDatabase database,
        string backupDirectory)
        => new BackupManagementService(
            database.CreateFactory(),
            new LocalizationWriteOptions { BackupDirectory = backupDirectory });

    private static BackupViewModel CreateViewModel(
        TempDatabase database,
        string backupDirectory,
        IConfirmationService confirmation)
    {
        var options = new LocalizationWriteOptions { BackupDirectory = backupDirectory };
        var backupService = new BackupManagementService(database.CreateFactory(), options);
        var writeService = new LocalizationWriteService(
            database.CreateFactory(),
            new LocalizationFormatAdapterRegistry(Array.Empty<ILocalizationFormatAdapter>()),
            options);

        return new BackupViewModel(backupService, writeService, confirmation);
    }

    private static int AddRecord(
        TempDatabase database,
        string originalPath,
        string backupPath)
    {
        using var context = database.CreateContext();
        var record = CreateRecord(originalPath, backupPath, DateTime.UtcNow);
        context.BackupRecords.Add(record);
        context.SaveChanges();
        return record.Id;
    }

    private static BackupRecord CreateRecord(
        string originalPath,
        string backupPath,
        DateTime createdAt)
        => new()
        {
            OriginalFilePath = originalPath,
            BackupFilePath = backupPath,
            OriginalSha256 = ComputeSha256(backupPath),
            CreatedAt = createdAt
        };

    private static string WriteFile(string directory, string fileName, string content)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static string ComputeSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed class RecordingConfirmationService : IConfirmationService
    {
        public RecordingConfirmationService(bool result) => Result = result;

        public bool Result { get; set; }
        public bool WasCalled { get; private set; }
        public string Message { get; private set; } = string.Empty;

        public bool Confirm(string title, string message)
        {
            WasCalled = true;
            Message = message;
            return Result;
        }
    }
}

