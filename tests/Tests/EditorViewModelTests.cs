namespace GameLanguageExplorer.Tests;

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.Services;
using GameLanguageExplorer.WPF.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Yerelleştirme editörünün yükleme, düzenleme ve kaydetme davranışlarını doğrular.</summary>
public class EditorViewModelTests
{
    [Fact]
    public async Task JsonFile_LoadsEntries_AndSavesWithConfirmation()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var path = tree.Write(
            @"Localization\menu.json",
            """{"menu":{"play":"Play","quit":"Quit"}}""");

        var (_, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, path, "JSON");

        using (var context = database.CreateContext())
        {
            var record = context.DiscoveredFiles.Single(file => file.Id == fileId);
            record.FileSize = new FileInfo(path).Length;
            record.Sha256 = ComputeSha256(path);
            context.SaveChanges();
        }

        var confirmation = new RecordingConfirmationService();
        var editor = CreateEditor(database, tree.Root, confirmation);

        Assert.True(editor.SelectedFile?.SupportsWrite);
        await editor.LoadEntriesCommand.ExecuteAsync(null);

        Assert.Equal(2, editor.Entries.Count);
        Assert.True(editor.IsEditingEnabled);
        Assert.All(editor.Entries, entry => Assert.True(entry.IsEditable));

        var play = editor.Entries.Single(entry => entry.Key == "/menu/play");
        play.TranslationText = "Oyna";

        Assert.Equal(1, editor.ChangedCount);
        Assert.True(editor.SaveCommand.CanExecute(null));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.True(confirmation.WasCalled);
        Assert.Contains("yedek", confirmation.Message, StringComparison.OrdinalIgnoreCase);

        using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
        {
            Assert.Equal("Oyna", document.RootElement.GetProperty("menu").GetProperty("play").GetString());
            Assert.Equal("Quit", document.RootElement.GetProperty("menu").GetProperty("quit").GetString());
        }

        using (var context = database.CreateContext())
        {
            Assert.Single(context.BackupRecords);
        }

        Assert.Equal(0, editor.ChangedCount);
        Assert.Contains("yazıldı", editor.SaveStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonWritableFile_IsReadOnly()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var path = tree.Write(
            @"Localization\sample.xml",
            "<root><string id=\"a\">Start</string></root>");

        var (_, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, path, "XML");

        using (var context = database.CreateContext())
        {
            var record = context.DiscoveredFiles.Single(file => file.Id == fileId);
            record.FileSize = new FileInfo(path).Length;
            record.Sha256 = ComputeSha256(path);
            context.SaveChanges();
        }

        var editor = CreateEditor(database, tree.Root, new RecordingConfirmationService());

        await editor.LoadEntriesCommand.ExecuteAsync(null);

        Assert.False(editor.IsEditingEnabled);
        Assert.NotEmpty(editor.Entries);
        Assert.All(editor.Entries, entry => Assert.False(entry.IsEditable));
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    private static EditorViewModel CreateEditor(
        TempDatabase database,
        string root,
        IConfirmationService confirmation)
    {
        var installedService = new InstalledGameScanService(
            database.CreateFactory(),
            Array.Empty<IInstalledGameProvider>(),
            NullLogger<InstalledGameScanService>.Instance);
        var fileService = new LocalizationFileScanService(
            database.CreateFactory(),
            NullLogger<LocalizationFileScanService>.Instance);
        var previewService = PreviewTestHelper.CreatePreviewService(database);
        var registry = new LocalizationFormatAdapterRegistry(new ILocalizationFormatAdapter[]
        {
            new JsonLocalizationAdapter()
        });
        var writeService = new LocalizationWriteService(
            database.CreateFactory(),
            registry,
            new LocalizationWriteOptions
            {
                BackupDirectory = Path.Combine(root, "backups")
            });

        return new EditorViewModel(installedService, fileService, previewService, writeService, confirmation);
    }

    private static string ComputeSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed class RecordingConfirmationService : IConfirmationService
    {
        public bool WasCalled { get; private set; }
        public string Message { get; private set; } = string.Empty;

        public bool Confirm(string title, string message)
        {
            WasCalled = true;
            Message = message;
            return true;
        }
    }
}
