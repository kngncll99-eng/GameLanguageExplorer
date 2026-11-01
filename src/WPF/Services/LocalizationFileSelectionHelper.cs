using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.WPF.ViewModels;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>Editör/çeviri ekranlarının ortak dosya yetenekleri.</summary>
public sealed record LocalizationAdapterCapabilities(
    IReadOnlyCollection<string> PreviewableExtensions,
    IReadOnlyCollection<string> WritableExtensions,
    IReadOnlyCollection<string> WritableFormatKeys)
{
    public static LocalizationAdapterCapabilities From(IReadOnlyList<LocalizationFormatAdapterInfo> adapters)
    {
        var previewable = adapters
            .SelectMany(adapter => adapter.Extensions)
            .ToArray();
        var writableAdapters = adapters.Where(adapter => adapter.SupportsWrite).ToArray();

        return new LocalizationAdapterCapabilities(
            previewable,
            writableAdapters.SelectMany(adapter => adapter.Extensions).ToArray(),
            writableAdapters.Select(adapter => adapter.FormatName).ToArray());
    }
}

/// <summary>Kurulu oyun ve keşfedilmiş dosya satırlarının ortak hazırlanması.</summary>
public static class LocalizationFileSelectionHelper
{
    public sealed record InstalledGamesLoadResult(
        IReadOnlyList<InstalledGame> Games,
        string? ErrorMessage);

    public static InstalledGamesLoadResult LoadInstalledGames(
        IInstalledGameScanService installedGameScanService)
    {
        try
        {
            var games = installedGameScanService
                .GetInstalledGamesAsync()
                .GetAwaiter()
                .GetResult()
                .ToList();

            return new InstalledGamesLoadResult(
                games,
                games.Count == 0
                    ? "Kurulu oyun bulunamadı. Önce 'Kurulu Oyunlar' ekranından tarama yapın veya klasör ekleyin."
                    : null);
        }
        catch (Exception exception)
        {
            return new InstalledGamesLoadResult(
                Array.Empty<InstalledGame>(),
                "Kurulu oyunlar yüklenemedi: " + exception.Message);
        }
    }

    public static IReadOnlyList<DiscoveredFileRow> CreateRows(
        IEnumerable<DiscoveredFile> files,
        LocalizationAdapterCapabilities capabilities)
        => files
            .Select(file => new DiscoveredFileRow(
                file,
                capabilities.PreviewableExtensions,
                capabilities.WritableExtensions,
                capabilities.WritableFormatKeys))
            .ToList();
}
