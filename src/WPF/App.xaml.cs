using System;
using System.Windows;
using GameLanguageExplorer.Data;
using GameLanguageExplorer.WPF.Services;
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
                    services
                        .AddApplicationDataServices()
                        .AddCatalogServices()
                        .AddStoreProviders()
                        .AddLocalizationServices()
                        .AddTranslationServices()
                        .AddUiServices();
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
