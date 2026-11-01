namespace GameLanguageExplorer.Tests;

using System.IO;
using System.Net.Http;
using System.Threading;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Translation;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.Services;
using GameLanguageExplorer.WPF.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>AI Çeviri ekranındaki güvenli dosyaya yazma komutunu doğrular.</summary>
public class TranslationWriteViewModelTests
{
    [Fact]
    public async Task WriteToFileCommand_ConfirmedWrite_UsesWriteService()
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

        var installedService = new InstalledGameScanService(
            database.CreateFactory(),
            Array.Empty<IInstalledGameProvider>(),
            NullLogger<InstalledGameScanService>.Instance);
        var fileService = new LocalizationFileScanService(
            database.CreateFactory(),
            NullLogger<LocalizationFileScanService>.Instance);
        var previewService = PreviewTestHelper.CreatePreviewService(database);
        var journalDirectory = Path.Combine(
            Path.GetTempPath(),
            "gle_write_vm_journal_" + Guid.NewGuid().ToString("N"));
        var writeService = new RecordingWriteService();
        var confirmation = new RecordingConfirmationService(true);

        try
        {
            var viewModel = new TranslationViewModel(
                installedService,
                fileService,
                previewService,
                new StubTranslationEngine(),
                new InfiniteHttpClientFactory(),
                new TranslationRunJournalStore(journalDirectory),
                writeService,
                confirmation);

            viewModel.SelectedFile = viewModel.Files.Single(row => row.Id == fileId);
            await viewModel.StartCommand.ExecuteAsync(null);

            Assert.True(viewModel.HasResults);
            Assert.True(viewModel.WriteToFileCommand.CanExecute(null));

            await viewModel.WriteToFileCommand.ExecuteAsync(null);

            Assert.True(confirmation.WasCalled);
            Assert.Contains("orijinal", confirmation.Message, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(writeService.Translations);
            Assert.Equal(2, writeService.Translations!.Count);
            Assert.Contains("başarı", viewModel.WriteStatusMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(100, viewModel.WriteProgressPercent);
            Assert.Null(viewModel.WriteErrorMessage);
        }
        finally
        {
            try { Directory.Delete(journalDirectory, recursive: true); } catch { }
        }
    }

    private static string ComputeSha256(string path)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();

    private sealed class RecordingConfirmationService : IConfirmationService
    {
        private readonly bool _answer;

        public RecordingConfirmationService(bool answer) => _answer = answer;

        public bool WasCalled { get; private set; }
        public string Message { get; private set; } = string.Empty;

        public bool Confirm(string title, string message)
        {
            WasCalled = true;
            Message = message;
            return _answer;
        }
    }

    private sealed class RecordingWriteService : ILocalizationWriteService
    {
        public IReadOnlyDictionary<string, string>? Translations { get; private set; }

        public Task<LocalizationWriteResult> WriteAsync(
            int installedGameId,
            int discoveredFileId,
            IReadOnlyDictionary<string, string> translations,
            CancellationToken cancellationToken = default,
            IProgress<LocalizationWriteProgress>? progress = null)
        {
            Translations = translations;

            return Task.FromResult(new LocalizationWriteResult(
                true,
                LocalizationWriteStatus.Success,
                "Test yazma başarılı.",
                BackupRecordId: 1,
                BackupFilePath: @"C:\temp\backup.bak",
                NewSha256: new string('a', 64)));
        }

        public Task<LocalizationWriteResult> RestoreAsync(
            int backupRecordId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(LocalizationWriteResult.Fail(
                LocalizationWriteStatus.RestoreFailed,
                "Testte geri yükleme kullanılmıyor."));
    }

    private sealed class InfiniteHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new() { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class StubTranslationEngine : ITranslationEngine
    {
        public Task<TranslationRunResult> RunAsync(
            TranslationEngineRequest request,
            ITranslationProvider provider,
            IProgress<TranslationRunProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var units = request.Entries
                .Where(entry => entry.Status != LocalizationEntryStatus.Skipped)
                .Select(entry => new TranslationUnitResult(
                    entry.Key,
                    entry.Key,
                    entry.SourceText,
                    new string('b', 64),
                    TranslationUnitStatus.Candidate)
                {
                    Translation = "T:" + entry.SourceText
                })
                .ToList();

            return Task.FromResult(new TranslationRunResult
            {
                RunId = "test_run",
                Status = TranslationRunStatus.Completed,
                Units = units,
                Metrics = new TranslationRunMetrics
                {
                    TotalRecords = units.Count,
                    TranslatableRecords = units.Count,
                    CandidateRecords = units.Count
                }
            });
        }
    }
}
