namespace GameLanguageExplorer.Tests;

using System.IO;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Xbox;
using GameLanguageExplorer.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Xbox / Microsoft Store provider'ın salt okunur tespitini doğrular.</summary>
public class XboxInstalledGameProviderTests
{
    [Fact]
    public async Task ScanAsync_AppxManifest_ReturnsXboxInstallation()
    {
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "XboxGames", "ExampleGame", "Content");
        Directory.CreateDirectory(installPath);
        await File.WriteAllTextAsync(
            Path.Combine(installPath, "AppxManifest.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
              <Identity Name="Publisher.ExampleGame" Publisher="CN=Test" Version="1.0.0.0" />
              <Applications>
                <Application Id="App" Executable="Game.exe">
                  <uap:VisualElements DisplayName="Example Xbox Game" />
                </Application>
              </Applications>
            </Package>
            """);

        var provider = new XboxInstalledGameProvider(
            new FixedXboxInstallationLocator(
                new XboxInstallationCandidate(null, null, installPath)));

        var batch = await provider.ScanAsync();

        Assert.Equal(InstalledGameScanStatus.Completed, batch.Status);
        var installation = Assert.Single(batch.Games);
        Assert.Equal("Xbox", installation.InstallSource);
        Assert.Equal("Publisher.ExampleGame", installation.InstallSourceId);
        Assert.Equal("Example Xbox Game", installation.DetectedGameName);
        Assert.Equal("Game.exe", installation.ExecutableName);
        Assert.Equal(Path.GetFullPath(installPath), installation.InstallPath);
        Assert.True(installation.IsInstalled);
    }

    [Fact]
    public async Task ScanAsync_NoCandidates_ReturnsUnavailable()
    {
        var provider = new XboxInstalledGameProvider(new FixedXboxInstallationLocator());
        var batch = await provider.ScanAsync();

        Assert.Equal(InstalledGameScanStatus.Unavailable, batch.Status);
        Assert.Empty(batch.Games);
        Assert.NotEmpty(provider.LastScanWarnings);
    }

    [Fact]
    public async Task ScanService_PersistsXboxInstallationWithXboxSource()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();
        var installPath = Path.Combine(tree.Root, "XboxGames", "PersistedGame", "Content");
        Directory.CreateDirectory(installPath);

        var provider = new XboxInstalledGameProvider(
            new FixedXboxInstallationLocator(
                new XboxInstallationCandidate(
                    "Publisher.PersistedGame_8wekyb3d8bbwe",
                    "Persisted Xbox Game",
                    installPath,
                    "Publisher.PersistedGame_1.0.0.0_x64__8wekyb3d8bbwe")));
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
        Assert.Equal("Xbox", saved.InstallSource);
        Assert.Equal("Publisher.PersistedGame_8wekyb3d8bbwe", saved.InstallSourceId);
        Assert.True(saved.IsInstalled);
    }
}

/// <summary>Test için sabit Xbox adayları.</summary>
public sealed class FixedXboxInstallationLocator : IXboxInstallationLocator
{
    private readonly IReadOnlyList<XboxInstallationCandidate> _candidates;

    public FixedXboxInstallationLocator(params XboxInstallationCandidate[] candidates)
        => _candidates = candidates;

    public IReadOnlyList<XboxInstallationCandidate> GetInstalledCandidates() => _candidates;
}
