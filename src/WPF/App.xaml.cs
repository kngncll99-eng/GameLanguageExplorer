using System;
using System.Windows;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Core.Services.Ea;
using GameLanguageExplorer.Core.Services.Epic;
using GameLanguageExplorer.Core.Services.Gog;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Ubisoft;
using GameLanguageExplorer.Core.Services.Localization.Export;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;
using GameLanguageExplorer.Core.Services.Steam;
using GameLanguageExplorer.Core.Services.Translation;
using GameLanguageExplorer.WPF.Services;
using GameLanguageExplorer.Data;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.ViewModels;
using GameLanguageExplorer.WPF.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameLanguageExplorer.WPF;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            DatabaseConfiguration.EnsureDirectoryExists();

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    // DbContext, WPF'in uzun ömürlü (singleton) ViewModel'leriyle uyumlu olsun diye
                    // factory olarak kaydedilir. Böylece her işlem kendi kısa ömürlü context'ini
                    // oluşturur; scoped/singleton yaşam süresi çakışması ve change tracker şişmesi önlenir.
                    services.AddDbContextFactory<AppDbContext>(options =>
                        options.UseSqlite(DatabaseConfiguration.ConnectionString));

                    services.AddHttpClient<SteamCatalogProvider>(client =>
                    {
                        client.BaseAddress = new Uri(SteamCatalogProvider.BaseUrl);
                        client.DefaultRequestHeaders.Add("User-Agent", "GameLanguageExplorer/1.0");
                        client.Timeout = TimeSpan.FromSeconds(60);
                    });

                    // Çeviri istemcisi: istek başına zaman aşımı yalnızca TranslationGenerationOptions
                    // tarafından yönetilir. API anahtarı burada tutulmaz.
                    services.AddHttpClient("translation", client =>
                    {
                        client.Timeout = Timeout.InfiniteTimeSpan;
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("GameLanguageExplorer/1.0");
                    });

                    services.AddSingleton<IGameCatalogProvider>(sp => sp.GetRequiredService<SteamCatalogProvider>());
                    services.AddSingleton<IGameSearchProvider>(sp => sp.GetRequiredService<SteamCatalogProvider>());
                    services.AddSingleton<ICatalogSyncService, CatalogSyncService>();

                    // Kurulu oyun tespiti (salt okunur). Her kaynak bağımsız bir sağlayıcıdır.
                    services.AddSingleton<ISteamRootLocator, RegistrySteamRootLocator>();
                    services.AddSingleton<IInstalledGameProvider, SteamInstalledGameProvider>();
                    services.AddSingleton<IEpicManifestLocator, EpicManifestLocator>();
                    services.AddSingleton<IInstalledGameProvider, EpicInstalledGameProvider>();
                    services.AddSingleton<IGogInstallationLocator, RegistryGogInstallationLocator>();
                    services.AddSingleton<IInstalledGameProvider, GogInstalledGameProvider>();
                    services.AddSingleton<IUbisoftInstallationLocator, RegistryUbisoftInstallationLocator>();
                    services.AddSingleton<IInstalledGameProvider, UbisoftInstalledGameProvider>();
                    services.AddSingleton<IEaInstallationLocator, RegistryEaInstallationLocator>();
                    services.AddSingleton<IInstalledGameProvider, EaInstalledGameProvider>();
                    services.AddSingleton<IInstalledGameProvider, ManualGameFolderProvider>();

                    services.AddSingleton<IFolderPickerService, FolderPickerService>();
                    services.AddSingleton<IConfirmationService, MessageBoxConfirmationService>();
                    services.AddSingleton<ISaveFileDialogService, SaveFileDialogService>();
                    services.AddSingleton<ILocalizationExportService, LocalizationExportService>();
                    services.AddSingleton<IInstalledGameScanService, InstalledGameScanService>();

                    // Dil dosyası keşfi (salt okunur).
                    services.AddSingleton<ILocalizationFileScanService, LocalizationFileScanService>();

                    // Metin ayrıştırıcıları (yalnızca okuma; yazma yok).
                    services.AddSingleton<LocalizationFormatReadOptions>(_ => new LocalizationFormatReadOptions());
                    services.AddSingleton<ILocalizationFormatAdapter, JsonLocalizationAdapter>();
                    services.AddSingleton<ILocalizationFormatAdapter, XmlLocalizationAdapter>();
                    services.AddSingleton<ILocalizationFormatAdapter, CsvLocalizationAdapter>();
                    services.AddSingleton<ILocalizationFormatAdapter, TsvLocalizationAdapter>();
                    services.AddSingleton<ILocalizationFormatAdapter, PoLocalizationAdapter>();
                    services.AddSingleton<ILocalizationFormatAdapter, PotLocalizationAdapter>();
                    services.AddSingleton<ILocalizationFormatAdapter, IniLocalizationAdapter>();
                    // .csv uzantısını CSV ile paylaşır; başlık şeması eşleşmezse CSV kullanılır.
                    services.AddSingleton<ILocalizationFormatAdapter, LocresCsvLocalizationAdapter>();
                    services.AddSingleton<LocalizationFormatAdapterRegistry>();
                    services.AddSingleton<ILocalizationPreviewService, LocalizationPreviewService>();

                    // Güvenli yazma: JSON adaptörü yeni içeriği üretir, servis yedek/doğrulama/taşıma yapar.
                    services.AddSingleton<ILocalizationFileHashProvider, Sha256LocalizationFileHashProvider>();
                    services.AddSingleton<ILocalizationFileReplacer, AtomicLocalizationFileReplacer>();
                    services.AddSingleton<LocalizationWriteOptions>(_ => new LocalizationWriteOptions());
                    services.AddSingleton<ILocalizationWriteService, LocalizationWriteService>();

                    // Çeviri motoru ve güvenli JSON çalışma günlüğü.
                    services.AddSingleton<TranslationRunJournalStore>();
                    services.AddSingleton<ITranslationMemory, TranslationMemoryService>();
                    services.AddSingleton<ITranslationEngine, TranslationEngine>();

                    services.AddSingleton<CatalogViewModel>();
                    services.AddSingleton<InstalledGamesViewModel>();
                    services.AddSingleton<FileDiscoveryViewModel>();
                    services.AddSingleton<TranslationViewModel>();
                    services.AddSingleton<EditorViewModel>();
                    services.AddSingleton<ShellViewModel>();

                    services.AddSingleton<Shell>(sp => new Shell
                    {
                        DataContext = sp.GetRequiredService<ShellViewModel>()
                    });
                })
                .Build();

            Services = _host.Services;

            // Şema, pencereden önce hazır olmalı: CatalogViewModel oluşturulurken veritabanını okur.
            ApplyDatabaseMigrations();

            // Kayıtlı tema tercihini uygula (varsayılan: koyu). Tercih dosyası DB'ye dokunmaz.
            ApplyUiTheme();

            var mainWindow = Services.GetRequiredService<Shell>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            ReportStartupFailure(ex);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.Dispose();
            _host = null;
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Bekleyen EF Core migration'larını uygular. Veritabanı dosyası veya şeması yoksa oluşturur;
    /// var olan veriyi silmez.
    /// </summary>
    private void ApplyDatabaseMigrations()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var context = factory.CreateDbContext();
        context.Database.Migrate();
    }

    /// <summary>
    /// Kayıtlı tema tercihini uygular ve değişiklikleri kalıcılaştırır.
    /// Tercih yalnızca <c>%LOCALAPPDATA%\GameLanguageExplorer\ui-preferences.json</c> dosyasında
    /// tutulur; veritabanına veya oyun dosyalarına yazılmaz.
    /// </summary>
    private static void ApplyUiTheme()
    {
        ThemeManager.Apply(UiPreferencesStore.LoadTheme());

        ThemeManager.ThemeChanged += (_, _) => UiPreferencesStore.SaveTheme(ThemeManager.Current);
    }

    private void ReportStartupFailure(Exception ex)
    {
        try
        {
            var logger = _host?.Services.GetService<ILogger<App>>();
            logger?.LogCritical(ex, "Uygulama başlatılırken ölümcül hata oluştu.");
        }
        catch
        {
            // Loglama altyapısı da hazır değilse kullanıcıya gösterilecek mesajı engellemeyelim.
        }

        MessageBox.Show(
            $"Uygulama başlatılırken hata oluştu.\n\n" +
            $"Hata türü: {ex.GetType().FullName}\n\n" +
            $"Hata mesajı: {ex.Message}\n\n" +
            $"StackTrace: {ex.StackTrace}",
            "Uygulama Hatası",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}