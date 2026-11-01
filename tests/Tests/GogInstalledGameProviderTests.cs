namespace GameLanguageExplorer.Tests;

using System.IO;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Gog;
using GameLanguageExplorer.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>GOG provider'ın salt okunur kurulum tespitini doğrular.</summary>
public class GogInstalledGameProviderTests
{
    [Fact]
    public async Task ScanAsync_GameInfo_ReturnsGogInstallation()
    {
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "GOG Games", "ExampleGame");
        Directory.CreateDirectory(installPath);
        await File.WriteAllTextAsync(
            Path.Combine(installPath, "gameInfo.igd"),
            "gameId=12345\ngameName=Example GOG Game\nexe=Game.exe\n");

        var provider = new GogInstalledGameProvider(
            new FixedGogInstallationLocator(
                new GogInstallationCandidate(null, null, installPath)));

        var batch = await provider.ScanAsync();

        Assert.Equal(InstalledGameScanStatus.Completed, batch.Status);
        var installation = Assert.Single(batch.Games);
        Assert.Equal("GOG", installation.InstallSource);
        Assert.Equal("12345", installation.InstallSourceId);
        Assert.Equal("Example GOG Game", installation.DetectedGameName);
        Assert.Equal(Path.GetFullPath(installPath), installation.InstallPath);
        Assert.Equal("Game.exe", installation.ExecutableName);
        Assert.True(installation.IsInstalled);
    }

    [Fact]
    public async Task ScanAsync_NoCandidates_ReturnsUnavailable()
    {
        var provider = new GogInstalledGameProvider(new FixedGogInstallationLocator());
        var batch = await provider.ScanAsync();

        Assert.Equal(InstalledGameScanStatus.Unavailable, batch.Status);
        Assert.Empty(batch.Games);
        Assert.NotEmpty(provider.LastScanWarnings);
    }

    [Fact]
    public void StorageManifestReader_ReadsNestedGameConfig()
    {
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "GOG Games", "StorageGame");
        Directory.CreateDirectory(installPath);
        var storagePath = Path.Combine(tree.Root, "storage");
        Directory.CreateDirectory(storagePath);

        File.WriteAllText(
            Path.Combine(storagePath, "config.json"),
            $$"""
            {
              "library": {
                "games": [
                  {
                    "gameId": "storage-7",
                    "gameName": "Storage Game",
                    "installLocation": "{{installPath.Replace("\\", "\\\\")}}"
                  }
                ]
              }
            }
            """);

        var candidates = GogStorageManifestReader.ReadFromStorage(storagePath);

        var candidate = Assert.Single(candidates);
        Assert.Equal("storage-7", candidate.GameId);
        Assert.Equal("Storage Game", candidate.GameName);
        Assert.Equal(installPath, candidate.InstallPath);
    }

    [Fact]
    public async Task ScanService_PersistsGogInstallationWithGogSource()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "GOG Games", "PersistedGame");
        Directory.CreateDirectory(installPath);

        var provider = new GogInstalledGameProvider(
            new FixedGogInstallationLocator(
                new GogInstallationCandidate("gog-42", "Persisted GOG Game", installPath)));
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
        Assert.Equal("GOG", saved.InstallSource);
        Assert.Equal("gog-42", saved.InstallSourceId);
        Assert.True(saved.IsInstalled);
    }
}

/// <summary>Test için sabit GOG adayları.</summary>
public sealed class FixedGogInstallationLocator : IGogInstallationLocator
{
    private readonly IReadOnlyList<GogInstallationCandidate> _candidates;

    public FixedGogInstallationLocator(params GogInstallationCandidate[] candidates)
        => _candidates = candidates;

    public IReadOnlyList<GogInstallationCandidate> GetInstalledCandidates() => _candidates;
}
