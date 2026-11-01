using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Core.Services.Ea;
using GameLanguageExplorer.Core.Services.Epic;
using GameLanguageExplorer.Core.Services.Gog;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Export;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;
using GameLanguageExplorer.Core.Services.Steam;
using GameLanguageExplorer.Core.Services.Translation;
using GameLanguageExplorer.Core.Services.Ubisoft;
using GameLanguageExplorer.Core.Services.Xbox;
using GameLanguageExplorer.Data;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.Services;
using GameLanguageExplorer.WPF.ViewModels;
using GameLanguageExplorer.WPF.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameLanguageExplorer.WPF;

/// <summary>
/// Uygulama servislerinin mantıksal gruplar halinde kaydedilmesi. Yaşam süreleri
/// App.xaml.cs içindeki özgün kayıtlarla aynı tutulur.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationDataServices(this IServiceCollection services)
    {
        // Uzun ömürlü ViewModel'lerle scoped DbContext çakışmasını önlemek için factory.
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(DatabaseConfiguration.ConnectionString));

        return services;
    }

    public static IServiceCollection AddCatalogServices(this IServiceCollection services)
    {
        services.AddHttpClient<SteamCatalogProvider>(client =>
        {
            client.BaseAddress = new Uri(SteamCatalogProvider.BaseUrl);
            client.DefaultRequestHeaders.Add("User-Agent", "GameLanguageExplorer/1.0");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddSingleton<IGameCatalogProvider>(sp => sp.GetRequiredService<SteamCatalogProvider>());
        services.AddSingleton<IGameSearchProvider>(sp => sp.GetRequiredService<SteamCatalogProvider>());
        services.AddSingleton<ICatalogSyncService, CatalogSyncService>();

        return services;
    }

    public static IServiceCollection AddStoreProviders(this IServiceCollection services)
    {
        // Kayıt sırası IInstalledGameProvider numaralandırmasında korunmalıdır.
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

        services.AddSingleton<IXboxInstallationLocator, RegistryXboxInstallationLocator>();
        services.AddSingleton<IInstalledGameProvider, XboxInstalledGameProvider>();

        services.AddSingleton<IInstalledGameProvider, ManualGameFolderProvider>();

        return services;
    }

    public static IServiceCollection AddLocalizationServices(this IServiceCollection services)
    {
        services.AddSingleton<IInstalledGameScanService, InstalledGameScanService>();
        services.AddSingleton<ILocalizationFileScanService, LocalizationFileScanService>();
        services.AddSingleton<ILocalizationExportService, LocalizationExportService>();

        services.AddSingleton<LocalizationFormatReadOptions>(_ => new LocalizationFormatReadOptions());
        services.AddSingleton<ILocalizationFormatAdapter, JsonLocalizationAdapter>();
        services.AddSingleton<ILocalizationFormatAdapter, YamlLocalizationAdapter>();
        services.AddSingleton<ILocalizationFormatAdapter, XmlLocalizationAdapter>();
        services.AddSingleton<ILocalizationFormatAdapter, CsvLocalizationAdapter>();
        services.AddSingleton<ILocalizationFormatAdapter, TsvLocalizationAdapter>();
        services.AddSingleton<ILocalizationFormatAdapter, PoLocalizationAdapter>();
        services.AddSingleton<ILocalizationFormatAdapter, PotLocalizationAdapter>();
        services.AddSingleton<ILocalizationFormatAdapter, IniLocalizationAdapter>();
        services.AddSingleton<ILocalizationFormatAdapter, LocresCsvLocalizationAdapter>();
        services.AddSingleton<LocalizationFormatAdapterRegistry>();
        services.AddSingleton<ILocalizationPreviewService, LocalizationPreviewService>();

        services.AddSingleton<ILocalizationFileHashProvider, Sha256LocalizationFileHashProvider>();
        services.AddSingleton<ILocalizationFileReplacer, AtomicLocalizationFileReplacer>();
        services.AddSingleton<LocalizationWriteOptions>(_ => new LocalizationWriteOptions());
        services.AddSingleton<ILocalizationWriteService, LocalizationWriteService>();
        services.AddSingleton<IBackupManagementService, BackupManagementService>();

        return services;
    }

    public static IServiceCollection AddTranslationServices(this IServiceCollection services)
    {
        services.AddHttpClient("translation", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GameLanguageExplorer/1.0");
        });

        services.AddSingleton<TranslationRunJournalStore>();
        services.AddSingleton<ITranslationMemory, TranslationMemoryService>();
        services.AddSingleton<ITranslationEngine, TranslationEngine>();

        return services;
    }

    public static IServiceCollection AddUiServices(this IServiceCollection services)
    {
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<IConfirmationService, MessageBoxConfirmationService>();
        services.AddSingleton<ISaveFileDialogService, SaveFileDialogService>();

        services.AddSingleton<CatalogViewModel>();
        services.AddSingleton<InstalledGamesViewModel>();
        services.AddSingleton<FileDiscoveryViewModel>();
        services.AddSingleton<TranslationViewModel>();
        services.AddSingleton<EditorViewModel>();
        services.AddSingleton<TranslationMemoryViewModel>();
        services.AddSingleton<BackupViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton(sp => new Shell
        {
            DataContext = sp.GetRequiredService<ShellViewModel>()
        });

        return services;
    }
}

