namespace GameLanguageExplorer.Tests;

using System.IO;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Ubisoft;
using GameLanguageExplorer.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Ubisoft Connect provider'ın salt okunur kurulum tespitini doğrular.</summary>
public class UbisoftInstalledGameProviderTests
{
    [Fact]
    public async Task ScanAsync_UplayManifest_ReturnsUbisoftInstallation()
    {
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "Ubisoft", "ExampleGame");
        Directory.CreateDirectory(installPath);
        await File.WriteAllTextAsync(
            Path.Combine(installPath, "uplay_install.manifest"),
            "game_code=u-123\ngame_name=Example Ubisoft Game\nexe=Game.exe\n");

        var provider = new UbisoftInstalledGameProvider(
            new FixedUbisoftInstallationLocator(
                new UbisoftInstallationCandidate(null, null, installPath)));

        var batch = await provider.ScanAsync();

        Assert.Equal(InstalledGameScanStatus.Completed, batch.Status);
        var installation = Assert.Single(batch.Games);
        Assert.Equal("Ubisoft", installation.InstallSource);
        Assert.Equal("u-123", installation.InstallSourceId);
        Assert.Equal("Example Ubisoft Game", installation.DetectedGameName);
        Assert.Equal(Path.GetFullPath(installPath), installation.InstallPath);
        Assert.Equal("Game.exe", installation.ExecutableName);
        Assert.True(installation.IsInstalled);
    }

    [Fact]
    public async Task ScanAsync_NoCandidates_ReturnsUnavailable()
    {
        var provider = new UbisoftInstalledGameProvider(new FixedUbisoftInstallationLocator());
        var batch = await provider.ScanAsync();

        Assert.Equal(InstalledGameScanStatus.Unavailable, batch.Status);
        Assert.Empty(batch.Games);
        Assert.NotEmpty(provider.LastScanWarnings);
    }

    [Fact]
    public async Task ScanService_PersistsUbisoftInstallationWithUbisoftSource()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "Ubisoft", "PersistedGame");
        Directory.CreateDirectory(installPath);

        var provider = new UbisoftInstalledGameProvider(
            new FixedUbisoftInstallationLocator(
                new UbisoftInstallationCandidate("u-42", "Persisted Ubisoft Game", installPath)));
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
        Assert.Equal("Ubisoft", saved.InstallSource);
        Assert.Equal("u-42", saved.InstallSourceId);
        Assert.True(saved.IsInstalled);
    }

    [Fact]
    public void ConfigManifestReader_ReadsNestedGameConfig()
    {
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "Ubisoft", "ConfigGame");
        Directory.CreateDirectory(installPath);
        var configPath = Path.Combine(tree.Root, "Ubisoft Game Launcher");
        Directory.CreateDirectory(configPath);

        File.WriteAllText(
            Path.Combine(configPath, "settings.json"),
            $$"""
            {
              "games": [
                {
                  "game_code": "config-9",
                  "game_name": "Config Game",
                  "installDir": "{{installPath.Replace("\\", "\\\\")}}"
                }
              ]
            }
            """);

        var candidates = UbisoftConfigManifestReader.ReadFromDirectory(configPath);

        var candidate = Assert.Single(candidates);
        Assert.Equal("config-9", candidate.GameId);
        Assert.Equal("Config Game", candidate.GameName);
        Assert.Equal(installPath, candidate.InstallPath);
    }
}

/// <summary>Test için sabit Ubisoft adayları.</summary>
public sealed class FixedUbisoftInstallationLocator : IUbisoftInstallationLocator
{
    private readonly IReadOnlyList<UbisoftInstallationCandidate> _candidates;

    public FixedUbisoftInstallationLocator(params UbisoftInstallationCandidate[] candidates)
        => _candidates = candidates;

    public IReadOnlyList<UbisoftInstallationCandidate> GetInstalledCandidates() => _candidates;
}
