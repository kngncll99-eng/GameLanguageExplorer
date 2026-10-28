namespace GameLanguageExplorer.Tests;

using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Translation;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.ViewModels;
using GameLanguageExplorer.WPF.Views;
using Microsoft.Extensions.Logging.Abstractions;

[Collection("WpfUi")]
public class TranslationUiSmokeTests
{
    private const string SyntheticXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
        + "<root>\n"
        + "  <string id=\"a\">Start</string>\n"
        + "  <string id=\"b\">Settings</string>\n"
        + "</root>\n";

    [Fact]
    public void TranslationView_LoadsPreviewAndRenders()
    {
        Exception? failure = null;
        var output = Path.Combine(Path.GetTempPath(), "gle_translation_ui.png");

        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new GameLanguageExplorer.WPF.App();
                    app.InitializeComponent();
                }

                using var database = new TempDatabase();
                using var tree = new TempTree();

                var file = tree.Write("sample.xml", SyntheticXml);
                var (_, fileId) = PreviewTestHelper
                    .CreateInstallAndFileAsync(database, tree.Root, file, "XML")
                    .GetAwaiter().GetResult();

                using (var context = database.CreateContext())
                {
                    var record = context.DiscoveredFiles.Single(record => record.Id == fileId);
                    record.FileSize = new FileInfo(file).Length;
                    record.Sha256 = FileFingerprint.ComputeSha256Async(file).GetAwaiter().GetResult();
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
                    "gle_translation_ui_journal_" + Guid.NewGuid().ToString("N"));

                try
                {
                    var viewModel = new TranslationViewModel(
                        installedService,
                        fileService,
                        previewService,
                        new TranslationEngine(new TranslationRunJournalStore(journalDirectory)),
                        new DummyHttpClientFactory(),
                        new TranslationRunJournalStore(journalDirectory));

                    viewModel.SelectedFile = viewModel.Files.Single(row => row.Id == fileId);
                    viewModel.PreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();

                    Assert.True(viewModel.HasPreview);
                    Assert.Equal(2, viewModel.PreviewTranslatableCount);
                    Assert.Equal(0, viewModel.PreviewAmbiguousCount);

                    var view = new TranslationView
                    {
                        DataContext = viewModel,
                        Width = 1200,
                        Height = 820
                    };

                    view.Measure(new Size(1200, 820));
                    view.Arrange(new Rect(0, 0, 1200, 820));
                    view.UpdateLayout();

                    var bitmap = new RenderTargetBitmap(
                        1200, 820, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(view);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(output);
                    encoder.Save(stream);
                }
                finally
                {
                    try { Directory.Delete(journalDirectory, recursive: true); } catch { }
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(180));

        Assert.Null(failure);
        Assert.True(File.Exists(output));
    }

    private sealed class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
