namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

// ─────────────────────────────────────────────────────────────────────────────
// Aşama 5B — LocresCSV arayüz duman testi
//
// Not: Gerçek uygulamanın veritabanı yolu %LOCALAPPDATA% altında sabittir (ortam
// değişkeniyle yönlendirilemez) ve önizleme yalnızca kayıtlı bir kurulum kökünün
// İÇİNDEKİ dosyaları kabul eder. Kullanıcı DB'sine sentetik kayıt eklemek ve oyun
// klasörlerine dosya yazmak yasak olduğundan gerçek pencereye sentetik dosya
// enjekte edilemez. Bu testler, arayüzün bağlandığı GERÇEK ViewModel'i ve GERÇEK
// önizleme servisini geçici DB/dosya ile sürer.
// ─────────────────────────────────────────────────────────────────────────────

public class LocresCsvUiSmokeTests
{
    private const string SyntheticFile = "sample.locres.csv";

    /// <summary>
    /// Tamamen sentetik içerik (gerçek NTE metni/anahtarı DEĞİL). Kapsanan durumlar:
    /// normal kayıt, boş çeviri, virgüllü metin, tırnaklı metin, çok satırlı metin,
    /// yinelenen anahtar, boş anahtar, boş kaynak.
    /// </summary>
    private const string SyntheticCsv =
        "key,source,Translation\n"
        + "AAAA1111BBBB2222CCCC3333DDDD4444,Start Game,Oyunu Başlat\n"
        + "AAAA1111BBBB2222CCCC3333DDDD5555,Quit Game,\n"
        + "AAAA1111BBBB2222CCCC3333DDDD6666,\"Hello, adventurer!\",\"Merhaba, maceracı!\"\n"
        + "AAAA1111BBBB2222CCCC3333DDDD7777,\"He said \"\"welcome\"\" back\",\"\"\"hoş geldin\"\" dedi\"\n"
        + "AAAA1111BBBB2222CCCC3333DDDD8888,\"First line\nSecond line\",\"Birinci satır\nİkinci satır\"\n"
        + "AAAA1111BBBB2222CCCC3333DDDD4444,Duplicate row,Tekrar\n"
        + ",Keyless row,\n"
        + "AAAA1111BBBB2222CCCC3333DDDD9999,,\n";

    private static FileDiscoveryViewModel CreateViewModel(TempDatabase database)
    {
        var installedService = new InstalledGameScanService(
            database.CreateFactory(),
            Array.Empty<GameLanguageExplorer.Core.Interfaces.IInstalledGameProvider>(),
            NullLogger<InstalledGameScanService>.Instance);

        var fileService = new LocalizationFileScanService(
            database.CreateFactory(),
            NullLogger<LocalizationFileScanService>.Instance);

        return new FileDiscoveryViewModel(
            installedService,
            fileService,
            PreviewTestHelper.CreatePreviewService(database));
    }

    private static DiscoveredFileRow Row(int id, string path, string format = "CSV")
        => new(new DiscoveredFile
        {
            Id = id,
            FilePath = path,
            FileName = System.IO.Path.GetFileName(path),
            DetectedFormat = format
        });

    [Fact]
    public async Task Preview_ShowsKeySourceTranslation_Separately_OnUiSurface()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var file = tree.Write(SyntheticFile, SyntheticCsv);
        var (_, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(database, tree.Root, file, "CSV");

        var viewModel = CreateViewModel(database);

        // Keşif listesi: uzantıya göre CSV, önizlenebilir.
        var discoveryRow = Assert.Single(viewModel.Files);
        Assert.Equal("CSV", discoveryRow.FormatKey);
        Assert.Contains("Ayrıştırılabilir", discoveryRow.SupportText);

        viewModel.SelectedFile = Row(fileId, file);
        await viewModel.PreviewCommand.ExecuteAsync(null);

        // Önizleme başlığı / adaptör: LocresCSV olarak tanındı.
        Assert.True(viewModel.HasPreview);
        Assert.Null(viewModel.PreviewErrorMessage);
        Assert.Equal(SyntheticFile, viewModel.PreviewTitle);
        Assert.Equal("LocresCSV ayrıştırıcısı", viewModel.PreviewAdapterText);

        // Sayımlar: 8 veri satırı → 6 çevrilebilir, 2 atlandı.
        Assert.Equal(6, viewModel.PreviewTranslatableCount);
        Assert.Equal(0, viewModel.PreviewAmbiguousCount);
        Assert.Equal(2, viewModel.PreviewSkippedCount);

        // Atlananlar varsayılan olarak gizli, filtre açılınca görünür.
        Assert.Equal(6, viewModel.PreviewEntries.Count);
        viewModel.ShowSkippedPreviewEntries = true;
        Assert.Equal(8, viewModel.PreviewEntries.Count);

        // Tür etiketi: her satır LocresCSV kaydı.
        Assert.All(viewModel.PreviewEntries, e => Assert.Equal("LocresCSV kaydı", e.KindText));

        // 1) Normal kayıt: anahtar / kaynak / çeviri ayrı alanlarda.
        var normal = viewModel.PreviewEntries[0];
        Assert.Equal("AAAA1111BBBB2222CCCC3333DDDD4444", normal.Key);
        Assert.Equal("Start Game", normal.SourceText);
        Assert.Equal("Oyunu Başlat", normal.ExistingTranslation);
        Assert.True(normal.HasExistingTranslation);
        Assert.Equal("Çevrilebilir", normal.StatusText);
        Assert.NotEqual(normal.Key, normal.SourceText);

        // 2) Boş çeviri: hata değil, "mevcut çeviri yok".
        var noTranslation = viewModel.PreviewEntries[1];
        Assert.Null(noTranslation.ExistingTranslation);
        Assert.False(noTranslation.HasExistingTranslation);
        Assert.Equal("Çevrilebilir", noTranslation.StatusText);

        // 3) Virgüllü ve 4) tırnaklı kaynak metinler bozulmadan.
        Assert.Equal("Hello, adventurer!", viewModel.PreviewEntries[2].SourceText);
        Assert.Equal("Merhaba, maceracı!", viewModel.PreviewEntries[2].ExistingTranslation);
        Assert.Equal("He said \"welcome\" back", viewModel.PreviewEntries[3].SourceText);

        // 5) Çok satırlı metin: ayrıntı panelinde tam okunur.
        var multiLine = viewModel.PreviewEntries[4];
        Assert.Contains("First line", multiLine.SourceText);
        Assert.Contains("Second line", multiLine.SourceText);
        Assert.Contains("\n", multiLine.SourceText);

        // 6) Yinelenen anahtar: korunur + meta + uyarı.
        var duplicate = viewModel.PreviewEntries[5];
        Assert.Equal("AAAA1111BBBB2222CCCC3333DDDD4444", duplicate.Key);
        Assert.Equal("Duplicate row", duplicate.SourceText);
        Assert.Contains("duplicate-key", duplicate.MetaText);
        Assert.NotNull(viewModel.PreviewWarningMessage);
        Assert.Contains("uyarı", viewModel.PreviewWarningMessage);

        // 7) Hatalı kayıtlar sessizce kaybolmuyor.
        var keyless = viewModel.PreviewEntries.Single(e => e.SkipReason.Contains("Anahtar boş"));
        Assert.Equal("Atlandı", keyless.StatusText);
        Assert.Contains("satır", keyless.Key);
        Assert.Equal("Keyless row", keyless.SourceText);

        var emptySource = viewModel.PreviewEntries.Single(e => e.SkipReason.Contains("Kaynak metin boş"));
        Assert.Equal("AAAA1111BBBB2222CCCC3333DDDD9999", emptySource.Key);
    }

    [Fact]
    public async Task SelectedEntry_ShowsFullKeyAndMeta_InDetailPanel()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var file = tree.Write(SyntheticFile, SyntheticCsv);
        var (_, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(database, tree.Root, file, "CSV");

        var viewModel = CreateViewModel(database);
        viewModel.SelectedFile = Row(fileId, file);
        await viewModel.PreviewCommand.ExecuteAsync(null);

        var multiline = viewModel.PreviewEntries.Single(e => e.SourceText.Contains("Second line"));
        viewModel.SelectedPreviewEntry = multiline;

        Assert.True(viewModel.HasSelectedPreviewEntry);
        var detail = viewModel.SelectedPreviewEntry!;

        Assert.Equal(32, detail.Key.Length);              // anahtar kısaltılmadan
        Assert.Contains("LocresCSV", detail.ContextText); // bağlam
        Assert.Contains("First line", detail.SourceText); // tam metin
        Assert.Contains("İkinci satır", detail.ExistingTranslation!);

        Assert.StartsWith("AAAA1111", detail.Key);
        Assert.DoesNotContain("AAAA1111", detail.SourceText);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Görsel duman testi: gerçek FileDiscoveryView'i sentetik veriyle, iki temada
// çevrimdışı render eder ve PNG üretir. Gerçek pencere açılmaz; kullanıcı DB'si
// ve oyun klasörleri kullanılmaz.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("WpfUi")]
public class LocresCsvRenderSmokeTests
{
    private const string SyntheticCsv =
        "key,source,Translation\n"
        + "AAAA1111BBBB2222CCCC3333DDDD4444,Start Game,Oyunu Başlat\n"
        + "AAAA1111BBBB2222CCCC3333DDDD5555,Quit Game,\n"
        + "AAAA1111BBBB2222CCCC3333DDDD6666,\"Hello, adventurer!\",\"Merhaba, maceracı!\"\n"
        + "AAAA1111BBBB2222CCCC3333DDDD8888,\"First line\nSecond line\",\"Birinci satır\nİkinci satır\"\n"
        + "AAAA1111BBBB2222CCCC3333DDDD4444,Duplicate row,Tekrar\n"
        + ",Keyless row,\n"
        + "AAAA1111BBBB2222CCCC3333DDDD9999,,\n";

    [Fact]
    public void PreviewView_RendersInDarkAndLightThemes()
    {
        var outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gle_locres_render");
        System.IO.Directory.CreateDirectory(outDir);

        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current is null)
                {
                    var app = new GameLanguageExplorer.WPF.App();
                    app.InitializeComponent(); // App.xaml: dönüştürücüler + stiller + varsayılan tema
                }

                using var database = new TempDatabase();
                using var tree = new TempTree();

                var file = tree.Write("sample.locres.csv", SyntheticCsv);
                var (_, fileId) = PreviewTestHelper
                    .CreateInstallAndFileAsync(database, tree.Root, file, "CSV")
                    .GetAwaiter().GetResult();

                // Test tohumu dosya boyutunu yazmaz; gerçekçi liste görünümü için tamamla.
                using (var context = database.CreateContext())
                {
                    var record = context.DiscoveredFiles.Single(f => f.Id == fileId);
                    record.FileSize = new System.IO.FileInfo(file).Length;
                    context.SaveChanges();
                }

                var viewModel = CreateViewModel(database);
                viewModel.SelectedFile = new DiscoveredFileRow(new DiscoveredFile
                {
                    Id = fileId,
                    FilePath = file,
                    FileName = "sample.locres.csv",
                    FileSize = new System.IO.FileInfo(file).Length,
                    DetectedFormat = "CSV"
                });

                viewModel.PreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                viewModel.ShowSkippedPreviewEntries = true;

                // Ayrıntı panelinde çok satırlı kaydın tam okunabildiğini göstermek için onu seç.
                viewModel.SelectedPreviewEntry = viewModel.PreviewEntries
                    .FirstOrDefault(e => e.SourceText.Contains("Second line"));

                Assert.True(viewModel.HasPreview);
                Assert.NotEmpty(viewModel.PreviewEntries);
                Assert.NotNull(viewModel.SelectedPreviewEntry);

                foreach (var theme in new[] { "Dark", "Light" })
                {
                    ApplyTheme(theme);

                    var view = new GameLanguageExplorer.WPF.Views.FileDiscoveryView
                    {
                        DataContext = viewModel,
                        Width = 1360,
                        Height = 820
                    };

                    view.Measure(new System.Windows.Size(1360, 820));
                    view.Arrange(new System.Windows.Rect(0, 0, 1360, 820));
                    view.UpdateLayout();

                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        1360, 820, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(view);

                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

                    using var stream = System.IO.File.Create(
                        System.IO.Path.Combine(outDir, $"locres_ui_{theme.ToLowerInvariant()}.png"));
                    encoder.Save(stream);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(180));

        Assert.Null(failure);
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(outDir, "locres_ui_dark.png")));
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(outDir, "locres_ui_light.png")));
    }

    /// <summary>Tema sözlüğünü mutlak pack URI ile değiştirir (test ana bilgisayarında güvenli).</summary>
    private static void ApplyTheme(string theme)
    {
        var resources = System.Windows.Application.Current!.Resources;
        var dictionary = new System.Windows.ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/GameLanguageExplorer.WPF;component/Themes/{theme}Theme.xaml",
                UriKind.Absolute)
        };

        if (resources.MergedDictionaries.Count > 0)
        {
            resources.MergedDictionaries[0] = dictionary;
        }
        else
        {
            resources.MergedDictionaries.Add(dictionary);
        }
    }

    private static FileDiscoveryViewModel CreateViewModel(TempDatabase database)
    {
        var installedService = new InstalledGameScanService(
            database.CreateFactory(),
            Array.Empty<GameLanguageExplorer.Core.Interfaces.IInstalledGameProvider>(),
            NullLogger<InstalledGameScanService>.Instance);

        var fileService = new LocalizationFileScanService(
            database.CreateFactory(),
            NullLogger<LocalizationFileScanService>.Instance);

        return new FileDiscoveryViewModel(
            installedService,
            fileService,
            PreviewTestHelper.CreatePreviewService(database));
    }
}
