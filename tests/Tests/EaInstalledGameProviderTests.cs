namespace GameLanguageExplorer.Tests;

using System.IO;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Ea;
using GameLanguageExplorer.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>EA App/Origin provider'ın salt okunur kurulum tespitini doğrular.</summary>
public class EaInstalledGameProviderTests
{
    [Fact]
    public async Task ScanAsync_Manifest_ReturnsEaInstallation()
    {
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "EA Games", "ExampleGame");
        Directory.CreateDirectory(installPath);
        await File.WriteAllTextAsync(
            Path.Combine(installPath, "manifest.json"),
            """
            {
              "contentId": "ea-content-123",
              "offerId": "offer-456",
              "displayName": "Example EA Game",
              "executable": "Game.exe"
            }
            """);

        var provider = new EaInstalledGameProvider(
            new FixedEaInstallationLocator(
                new EaInstallationCandidate(null, null, installPath)));

        var batch = await provider.ScanAsync();

        Assert.Equal(InstalledGameScanStatus.Completed, batch.Status);
        var installation = Assert.Single(batch.Games);
        Assert.Equal("EA", installation.InstallSource);
        Assert.Equal("ea-content-123", installation.InstallSourceId);
        Assert.Equal("Example EA Game", installation.DetectedGameName);
        Assert.Equal(Path.GetFullPath(installPath), installation.InstallPath);
        Assert.Equal("Game.exe", installation.ExecutableName);
        Assert.True(installation.IsInstalled);
    }

    [Fact]
    public async Task ScanAsync_NoCandidates_ReturnsUnavailable()
    {
        var provider = new EaInstalledGameProvider(new FixedEaInstallationLocator());
        var batch = await provider.ScanAsync();

        Assert.Equal(InstalledGameScanStatus.Unavailable, batch.Status);
        Assert.Empty(batch.Games);
        Assert.NotEmpty(provider.LastScanWarnings);
    }

    [Fact]
    public async Task ScanService_PersistsEaInstallationWithEaSource()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "EA Games", "PersistedGame");
        Directory.CreateDirectory(installPath);

        var provider = new EaInstalledGameProvider(
            new FixedEaInstallationLocator(
                new EaInstallationCandidate("ea-42", "Persisted EA Game", installPath)));
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
        Assert.Equal("EA", saved.InstallSource);
        Assert.Equal("ea-42", saved.InstallSourceId);
        Assert.True(saved.IsInstalled);
    }

    [Fact]
    public void ConfigManifestReader_ReadsInstallerDataJson()
    {
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "EA Games", "ConfigGame");
        Directory.CreateDirectory(installPath);
        var installData = Path.Combine(tree.Root, "EA Desktop", "InstallData");
        Directory.CreateDirectory(installData);

        File.WriteAllText(
            Path.Combine(installData, "catalog.json"),
            $$"""
            {
              "offers": [
                {
                  "contentId": "config-9",
                  "offerId": "offer-9",
                  "displayName": "Config Game",
                  "installPath": "{{installPath.Replace("\\", "\\\\")}}"
                }
              ]
            }
            """);

        var candidates = EaConfigManifestReader.ReadFromDirectory(installData);

        var candidate = Assert.Single(candidates);
        Assert.Equal("config-9", candidate.ContentId);
        Assert.Equal("offer-9", candidate.OfferId);
        Assert.Equal("Config Game", candidate.GameName);
        Assert.Equal(installPath, candidate.InstallPath);
    }
}

/// <summary>Test için sabit EA adayları.</summary>
public sealed class FixedEaInstallationLocator : IEaInstallationLocator
{
    private readonly IReadOnlyList<EaInstallationCandidate> _candidates;

    public FixedEaInstallationLocator(params EaInstallationCandidate[] candidates)
        => _candidates = candidates;

    public IReadOnlyList<EaInstallationCandidate> GetInstalledCandidates() => _candidates;
}
