namespace GameLanguageExplorer.Tests;

using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Translation;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.Services;
using GameLanguageExplorer.WPF.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Seçim değişikliği sırasında geç gelen önizleme/çeviri/yazma sonuçlarının
/// yeni dosyaya uygulanmadığını doğrular.
/// </summary>
public class AsyncSelectionIntegrityTests
{
    [Fact]
    public async Task Editor_LatePreviewForA_IsNotAppliedToB()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var (_, fileAId, fileBId, _, _) = await CreateTwoFilesAsync(database, tree);
        var preview = new GatedPreviewService(
            PreviewTestHelper.CreatePreviewService(database),
            fileAId);
        var editor = CreateEditor(database, tree.Root, preview);

        editor.SelectedFile = editor.Files.Single(row => row.Id == fileAId);
        var loadA = editor.LoadEntriesCommand.ExecuteAsync(null);
        await preview.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        editor.SelectedFile = editor.Files.Single(row => row.Id == fileBId);
        await editor.LoadEntriesCommand.ExecuteAsync(null);

        preview.Release.TrySetResult();
        await loadA.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(fileBId, editor.SelectedFile?.Id);
        Assert.Single(editor.Entries);
        Assert.Equal("/b", editor.Entries[0].Key);
        Assert.True(editor.LoadEntriesCommand.CanExecute(null));
    }

    [Fact]
    public async Task Editor_SaveStartedForA_DoesNotWriteOrReportToB()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var (_, fileAId, fileBId, _, _) = await CreateTwoFilesAsync(database, tree);
        var writeService = new DelayedWriteService();
        var confirmation = new AlwaysConfirmService();
        var editor = CreateEditor(
            database,
            tree.Root,
            PreviewTestHelper.CreatePreviewService(database),
            writeService,
            confirmation);

        editor.SelectedFile = editor.Files.Single(row => row.Id == fileAId);
        await editor.LoadEntriesCommand.ExecuteAsync(null);
        editor.Entries[0].TranslationText = "Changed A";
        Assert.True(editor.SaveCommand.CanExecute(null));

        var save = editor.SaveCommand.ExecuteAsync(null);
        await writeService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        editor.SelectedFile = editor.Files.Single(row => row.Id == fileBId);
        writeService.Release.TrySetResult();
        await save.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, writeService.Calls);
        Assert.Equal(fileAId, writeService.LastDiscoveredFileId);
        Assert.NotEqual(fileBId, writeService.LastDiscoveredFileId);
        Assert.Null(editor.SaveStatusMessage);
        Assert.Null(editor.SaveErrorMessage);
    }

    [Fact]
    public async Task Translation_LateRunForA_IsNotAppliedToB_AndLateProgressIgnored()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var (_, fileAId, fileBId, _, _) = await CreateTwoFilesAsync(database, tree);
        var engine = new GatedTranslationEngine();
        var writeService = new DelayedWriteService();
        var journalDirectory = Path.Combine(
            Path.GetTempPath(),
            "gle_selection_journal_" + Guid.NewGuid().ToString("N"));

        try
        {
            var viewModel = CreateTranslationViewModel(
                database,
                engine,
                writeService,
                journalDirectory);

            viewModel.SelectedFile = viewModel.Files.Single(row => row.Id == fileAId);
            var runA = viewModel.StartCommand.ExecuteAsync(null);
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            viewModel.SelectedFile = viewModel.Files.Single(row => row.Id == fileBId);
            engine.Release.TrySetResult();
            await runA.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200);

            Assert.Equal(fileBId, viewModel.SelectedFile?.Id);
            Assert.False(viewModel.HasResults);
            Assert.Empty(viewModel.Results);
            Assert.Equal(0, viewModel.ProgressPercent);
            Assert.False(viewModel.WriteToFileCommand.CanExecute(null));
            Assert.Equal(0, writeService.Calls);
        }
        finally
        {
            try { Directory.Delete(journalDirectory, recursive: true); } catch { }
        }
    }

    private static async Task<(int InstallId, int FileAId, int FileBId, string PathA, string PathB)>
        CreateTwoFilesAsync(TempDatabase database, TempTree tree)
    {
        var pathA = tree.Write(@"Localization\a.json", """{"a":"A"}""");
        var pathB = tree.Write(@"Localization\b.json", """{"b":"B"}""");
        var (installId, fileAId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, pathA, "JSON");
        var fileBId = await PreviewTestHelper.AddFileAsync(database, installId, pathB, "JSON");

        using var context = database.CreateContext();

        foreach (var (id, path) in new[] { (fileAId, pathA), (fileBId, pathB) })
        {
            var record = context.DiscoveredFiles.Single(file => file.Id == id);
            record.FileSize = new FileInfo(path).Length;
            record.Sha256 = ComputeSha256(path);
            record.IsWritable = true;
        }

        await context.SaveChangesAsync();
        return (installId, fileAId, fileBId, pathA, pathB);
    }


    private static EditorViewModel CreateEditor(
        TempDatabase database,
        string root,
        ILocalizationPreviewService previewService,
        ILocalizationWriteService? writeService = null,
        IConfirmationService? confirmationService = null)
    {
        var installedService = new InstalledGameScanService(
            database.CreateFactory(),
            Array.Empty<IInstalledGameProvider>(),
            NullLogger<InstalledGameScanService>.Instance);
        var fileService = new LocalizationFileScanService(
            database.CreateFactory(),
            NullLogger<LocalizationFileScanService>.Instance);

        return new EditorViewModel(
            installedService,
            fileService,
            previewService,
            writeService,
            confirmationService);
    }

    private static TranslationViewModel CreateTranslationViewModel(
        TempDatabase database,
        ITranslationEngine engine,
        ILocalizationWriteService writeService,
        string journalDirectory)
    {
        var installedService = new InstalledGameScanService(
            database.CreateFactory(),
            Array.Empty<IInstalledGameProvider>(),
            NullLogger<InstalledGameScanService>.Instance);
        var fileService = new LocalizationFileScanService(
            database.CreateFactory(),
            NullLogger<LocalizationFileScanService>.Instance);

        return new TranslationViewModel(
            installedService,
            fileService,
            PreviewTestHelper.CreatePreviewService(database),
            engine,
            new InfiniteHttpClientFactory(),
            new TranslationRunJournalStore(journalDirectory),
            writeService,
            new AlwaysConfirmService());
    }

    private static string ComputeSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed class GatedPreviewService : ILocalizationPreviewService
    {
        private readonly ILocalizationPreviewService _inner;
        private readonly int _gatedFileId;
        private int _gated;

        public GatedPreviewService(ILocalizationPreviewService inner, int gatedFileId)
        {
            _inner = inner;
            _gatedFileId = gatedFileId;
        }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<LocalizationPreviewResult> PreviewAsync(
            int installedGameId,
            int discoveredFileId,
            CancellationToken cancellationToken = default)
        {
            if (discoveredFileId == _gatedFileId && Interlocked.Exchange(ref _gated, 1) == 0)
            {
                Started.TrySetResult();
                await Release.Task.ConfigureAwait(false);
            }

            return await _inner
                .PreviewAsync(installedGameId, discoveredFileId, cancellationToken)
                .ConfigureAwait(false);
        }

        public IReadOnlyList<LocalizationFormatAdapterInfo> GetAdapterInfo()
            => _inner.GetAdapterInfo();
    }

    private sealed class GatedTranslationEngine : ITranslationEngine
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TranslationRunResult> RunAsync(
            TranslationEngineRequest request,
            ITranslationProvider provider,
            IProgress<TranslationRunProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.ConfigureAwait(false);

            progress?.Report(new TranslationRunProgress(
                1, 1, 1, 1, "late progress", TimeSpan.FromMilliseconds(1)));

            var units = request.Entries
                .Where(entry => entry.Status != LocalizationEntryStatus.Skipped)
                .Select(entry => new TranslationUnitResult(
                    entry.Key,
                    entry.Key,
                    entry.SourceText,
                    new string('c', 64),
                    TranslationUnitStatus.Candidate)
                {
                    Translation = "T:" + entry.SourceText
                })
                .ToList();

            return new TranslationRunResult
            {
                RunId = "late_run",
                Status = TranslationRunStatus.Completed,
                Units = units,
                Metrics = new TranslationRunMetrics
                {
                    TotalRecords = units.Count,
                    TranslatableRecords = units.Count,
                    CandidateRecords = units.Count
                }
            };
        }
    }



    private sealed class DelayedWriteService : ILocalizationWriteService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }
        public int LastDiscoveredFileId { get; private set; }

        public async Task<LocalizationWriteResult> WriteAsync(
            int installedGameId,
            int discoveredFileId,
            IReadOnlyDictionary<string, string> translations,
            CancellationToken cancellationToken = default,
            IProgress<LocalizationWriteProgress>? progress = null,
            string? expectedSourceSha256 = null)
        {
            Calls++;
            LastDiscoveredFileId = discoveredFileId;
            Started.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            progress?.Report(new LocalizationWriteProgress(
                LocalizationWriteStage.Completed, "late write progress", 100));

            return new LocalizationWriteResult(
                true,
                LocalizationWriteStatus.Success,
                "Test yazma başarılı.");
        }

        public Task<LocalizationWriteResult> RestoreAsync(
            int backupRecordId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(LocalizationWriteResult.Fail(
                LocalizationWriteStatus.RestoreFailed,
                "Testte geri yükleme kullanılmıyor."));
    }

    private sealed class AlwaysConfirmService : IConfirmationService
    {
        public bool Confirm(string title, string message) => true;
    }

    private sealed class InfiniteHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new() { Timeout = Timeout.InfiniteTimeSpan };
    }
}

