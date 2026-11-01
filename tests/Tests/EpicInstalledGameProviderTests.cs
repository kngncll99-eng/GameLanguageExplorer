namespace GameLanguageExplorer.Tests;

using System.IO;
using System.Text.Json;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Epic;
using GameLanguageExplorer.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Epic launcher manifestlerinden salt okunur kurulu oyun tespitini doğrular.</summary>
public class EpicInstalledGameProviderTests
{
    [Fact]
    public async Task ScanAsync_ItemManifest_ReturnsEpicInstallation()
    {
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "EpicGames", "ExampleGame");
        Directory.CreateDirectory(installPath);

        var manifestDirectory = Path.Combine(tree.Root, "manifests");
        Directory.CreateDirectory(manifestDirectory);

        var manifest = JsonSerializer.Serialize(new
        {
            FormatVersion = 0,
            AppName = "ExampleGame",
            DisplayName = "Example Game",
            CatalogItemId = "catalog-example",
            InstallLocation = installPath,
            InstallSize = 123456L,
            LaunchExecutable = "Game.exe"
        });

        await File.WriteAllTextAsync(Path.Combine(manifestDirectory, "ExampleGame.item"), manifest);

        var provider = new EpicInstalledGameProvider(new FixedEpicManifestLocator(manifestDirectory));
        var batch = await provider.ScanAsync();

        Assert.Equal(InstalledGameScanStatus.Completed, batch.Status);
        var installation = Assert.Single(batch.Games);
        Assert.Equal("Epic", installation.InstallSource);
        Assert.Equal("ExampleGame", installation.InstallSourceId);
        Assert.Equal("Example Game", installation.DetectedGameName);
        Assert.Equal(Path.GetFullPath(installPath), installation.InstallPath);
        Assert.Equal(123456L, installation.SizeInBytes);
        Assert.Equal("Game.exe", installation.ExecutableName);
        Assert.True(installation.IsInstalled);
        Assert.Empty(batch.Warnings);
    }

    [Fact]
    public async Task ScanAsync_MissingManifestDirectory_ReturnsUnavailable()
    {
        using var tree = new TempTree();
        var missing = Path.Combine(tree.Root, "does-not-exist");

        var provider = new EpicInstalledGameProvider(new FixedEpicManifestLocator(missing));
        var batch = await provider.ScanAsync();

        Assert.Equal(InstalledGameScanStatus.Unavailable, batch.Status);
        Assert.Empty(batch.Games);
        Assert.NotEmpty(provider.LastScanWarnings);
    }

    [Fact]
    public async Task ScanService_PersistsEpicInstallationWithEpicSource()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "EpicGames", "PersistedGame");
        Directory.CreateDirectory(installPath);

        var manifestDirectory = Path.Combine(tree.Root, "manifests");
        Directory.CreateDirectory(manifestDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(manifestDirectory, "PersistedGame.item"),
            JsonSerializer.Serialize(new
            {
                AppName = "PersistedGame",
                DisplayName = "Persisted Game",
                CatalogItemId = "catalog-persisted",
                InstallLocation = installPath
            }));

        var provider = new EpicInstalledGameProvider(new FixedEpicManifestLocator(manifestDirectory));
        var service = new InstalledGameScanService(
            database.CreateFactory(),
            new IInstalledGameProvider[] { provider },
            NullLogger<InstalledGameScanService>.Instance);

        var result = await service.ScanAsync();

        Assert.True(result.Success);
        Assert.Equal(1, result.TotalFound);
        Assert.Equal(1, result.Added);

        using var context = database.CreateContext();
        var saved = Assert.Single(context.InstalledGames);
        Assert.Equal("Epic", saved.InstallSource);
        Assert.Equal("PersistedGame", saved.InstallSourceId);
        Assert.Equal(Path.GetFullPath(installPath), saved.InstallPath);
        Assert.True(saved.IsInstalled);
    }

    [Fact]
    public async Task ScanAsync_BrokenManifest_IsSkippedWithoutFailingScan()
    {
        using var tree = new TempTree();
        var manifestDirectory = Path.Combine(tree.Root, "manifests");
        Directory.CreateDirectory(manifestDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(manifestDirectory, "broken.item"),
            "{ \"AppName\": \"Broken\", ");

        var provider = new EpicInstalledGameProvider(new FixedEpicManifestLocator(manifestDirectory));
        var batch = await provider.ScanAsync();

        Assert.Equal(InstalledGameScanStatus.Completed, batch.Status);
        Assert.True(batch.IsPartial);
        Assert.Empty(batch.Games);
        Assert.Contains(batch.Warnings, warning => warning.Contains("Bozuk Epic manifesti"));
    }
}

/// <summary>Test için sabit Epic manifest klasörleri.</summary>
public sealed class FixedEpicManifestLocator : IEpicManifestLocator
{
    private readonly IReadOnlyList<string> _directories;

    public FixedEpicManifestLocator(params string[] directories) => _directories = directories;

    public IReadOnlyList<string> GetCandidateManifestDirectories() => _directories;
}
