namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Core.Services.Steam;
using GameLanguageExplorer.Data;
using GameLanguageExplorer.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

// ─────────────────────────────────────────────────────────────────────────────
// Kurulu oyun test altyapısı (geçici klasörler, gerçek Steam kurulumu gerekmez)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Geçici bir Steam kurulum ağacı oluşturur ve test sonunda siler.</summary>
public sealed class TempSteamInstall : IDisposable
{
    public string RootPath { get; }

    public TempSteamInstall()
    {
        RootPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"gle_steam_{Guid.NewGuid():N}");

        System.IO.Directory.CreateDirectory(SteamApps);
    }

    public string SteamApps => System.IO.Path.Combine(RootPath, "steamapps");

    /// <summary>Ek bir kütüphane kökü oluşturur ve kök yolunu döndürür.</summary>
    public string CreateLibrary(string name)
    {
        var libraryRoot = System.IO.Path.Combine(RootPath, "libraries", name);
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(libraryRoot, "steamapps"));
        return libraryRoot;
    }

    /// <summary><c>libraryfolders.vdf</c> dosyasını yeni (v2, iç içe) biçimde yazar.</summary>
    public void WriteLibraryFolders(params string[] libraryRoots)
    {
        var lines = new List<string> { "\"libraryfolders\"", "{" };

        for (var i = 0; i < libraryRoots.Length; i++)
        {
            lines.Add($"    \"{i}\"");
            lines.Add("    {");
            lines.Add($"        \"path\"        \"{libraryRoots[i].Replace("\\", "\\\\")}\"");
            lines.Add("    }");
        }

        lines.Add("}");
        WriteFile(System.IO.Path.Combine(SteamApps, "libraryfolders.vdf"), string.Join("\n", lines));
    }

    /// <summary>Eski (düz yol) biçimde <c>libraryfolders.vdf</c> yazar.</summary>
    public void WriteLegacyLibraryFolders(params string[] libraryRoots)
    {
        var lines = new List<string> { "\"libraryfolders\"", "{" };

        for (var i = 0; i < libraryRoots.Length; i++)
        {
            lines.Add($"    \"{i + 1}\"        \"{libraryRoots[i].Replace("\\", "\\\\")}\"");
        }

        lines.Add("}");
        WriteFile(System.IO.Path.Combine(SteamApps, "libraryfolders.vdf"), string.Join("\n", lines));
    }

    /// <summary>Geçerli bir <c>appmanifest_&lt;appid&gt;.acf</c> yazar.</summary>
    public string WriteManifest(
        string steamAppsPath,
        string appId,
        string name,
        string installDir,
        long sizeOnDisk = 123456,
        bool createInstallFolder = true)
    {
        var content = string.Join("\n", new[]
        {
            "\"AppState\"",
            "{",
            $"\t\"appid\"\t\t\"{appId}\"",
            $"\t\"name\"\t\t\"{name}\"",
            $"\t\"installdir\"\t\t\"{installDir}\"",
            $"\t\"SizeOnDisk\"\t\t\"{sizeOnDisk}\"",
            "\t\"LastUpdated\"\t\t\"1700000000\"",
            "}"
        });

        var manifestPath = System.IO.Path.Combine(steamAppsPath, $"appmanifest_{appId}.acf");
        WriteFile(manifestPath, content);

        if (createInstallFolder)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(steamAppsPath, "common", installDir));
        }

        return manifestPath;
    }

    /// <summary>Bozuk içerikli manifest yazar.</summary>
    public string WriteBrokenManifest(string steamAppsPath, string appId, string content)
    {
        var manifestPath = System.IO.Path.Combine(steamAppsPath, $"appmanifest_{appId}.acf");
        WriteFile(manifestPath, content);
        return manifestPath;
    }

    private static void WriteFile(string path, string content)
    {
        var directory = System.IO.Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        System.IO.File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
            // Test temizliği en iyi çaba.
        }
    }
}

/// <summary>Sabit Steam kök listesi döndüren locator.</summary>
public sealed class FixedSteamRootLocator : ISteamRootLocator
{
    private readonly IReadOnlyList<string> _roots;

    public FixedSteamRootLocator(params string[] roots) => _roots = roots;

    public IReadOnlyList<string> GetCandidateSteamRoots() => _roots;
}

/// <summary>Bellekteki kurulum listesini döndüren (veya hata fırlatan) sağlayıcı.</summary>
public sealed class StubInstalledGameProvider : IInstalledGameProvider, IScanDiagnostics
{
    private readonly Func<CancellationToken, Task<IEnumerable<InstalledGame>>> _scan;
    private readonly List<string> _warnings = new();
    private readonly InstalledGameScanStatus _status;
    private readonly bool _isPartial;
    private readonly string? _errorMessage;

    public StubInstalledGameProvider(
        string name,
        Func<CancellationToken, Task<IEnumerable<InstalledGame>>> scan,
        params string[] warnings)
        : this(name, scan, InstalledGameScanStatus.Completed, isPartial: false, errorMessage: null, warnings)
    {
    }

    private StubInstalledGameProvider(
        string name,
        Func<CancellationToken, Task<IEnumerable<InstalledGame>>> scan,
        InstalledGameScanStatus status,
        bool isPartial,
        string? errorMessage,
        string[] warnings)
    {
        ProviderName = name;
        _scan = scan;
        _status = status;
        _isPartial = isPartial;
        _errorMessage = errorMessage;
        _warnings.AddRange(warnings);
    }

    /// <summary>Kaynak bu makinede yok (ör. launcher kurulu değil).</summary>
    public static StubInstalledGameProvider Unavailable(string name, string reason)
        => new(name, _ => Task.FromResult<IEnumerable<InstalledGame>>(Array.Empty<InstalledGame>()),
            InstalledGameScanStatus.Unavailable, isPartial: false, reason, Array.Empty<string>());

    /// <summary>Kaynak başarısız oldu.</summary>
    public static StubInstalledGameProvider Failing(string name, string error)
        => new(name, _ => throw new InvalidOperationException(error),
            InstalledGameScanStatus.Failed, isPartial: false, error, Array.Empty<string>());

    /// <summary>Kısmi tarama: kayıtlar döner ama "kurulu değil" işaretlemesi yapılmamalı.</summary>
    public static StubInstalledGameProvider Partial(string name, params InstalledGame[] games)
        => new(name, _ => Task.FromResult<IEnumerable<InstalledGame>>(games),
            InstalledGameScanStatus.Completed, isPartial: true, errorMessage: null, Array.Empty<string>());

    public string ProviderName { get; }

    public IReadOnlyList<string> LastScanWarnings => _warnings;

    public async Task<InstalledGameScanBatch> ScanAsync(CancellationToken cancellationToken = default)
    {
        var games = (await _scan(cancellationToken))?.ToList() ?? new List<InstalledGame>();

        return _status switch
        {
            InstalledGameScanStatus.Unavailable => InstalledGameScanBatch.Unavailable(ProviderName, _errorMessage ?? "kaynak yok"),
            InstalledGameScanStatus.Failed => InstalledGameScanBatch.Failed(ProviderName, _errorMessage ?? "hata"),
            _ => InstalledGameScanBatch.Completed(ProviderName, games, _warnings, _isPartial)
        };
    }

    public Task<InstalledGame?> DetectInstallationAsync(string gameIdentifier, CancellationToken cancellationToken = default)
        => Task.FromResult<InstalledGame?>(null);

    /// <summary>Hazır bir tespit kaydı üretir.</summary>
    public static InstalledGame Install(string appId, string name, string path, bool isInstalled = true)
        => new()
        {
            InstallPath = path,
            InstallSource = "Steam",
            InstallSourceId = appId,
            DetectedGameName = name,
            IsInstalled = isInstalled,
            SizeInBytes = 1000
        };
}


// ─────────────────────────────────────────────────────────────────────────────
// VDF ayrıştırıcı
// ─────────────────────────────────────────────────────────────────────────────

public class VdfParserTests
{
    [Fact]
    public void Parse_SimpleNestedBlock_ReturnsNestedDictionary()
    {
        var vdf = "\"root\"\n{\n  \"key\" \"value\"\n  \"child\"\n  {\n    \"inner\" \"42\"\n  }\n}";

        var parsed = VdfParser.Parse(vdf);

        var root = Assert.IsType<Dictionary<string, object>>(parsed["root"]);
        Assert.Equal("value", root["key"]);

        var child = Assert.IsType<Dictionary<string, object>>(root["child"]);
        Assert.Equal("42", child["inner"]);
    }

    [Fact]
    public void Parse_IgnoresComments()
    {
        var vdf = "\"root\"\n{\n  // yorum satırı\n  \"key\" \"value\" // satır sonu\n}";

        var parsed = VdfParser.Parse(vdf);
        var root = Assert.IsType<Dictionary<string, object>>(parsed["root"]);

        Assert.Equal("value", root["key"]);
    }

    [Fact]
    public void Parse_HandlesEscapedQuotesAndBackslashes()
    {
        var vdf = "\"root\"\n{\n  \"path\" \"C:\\\\Games\\\\\\\"Test\\\"\"\n}";

        var parsed = VdfParser.Parse(vdf);
        var root = Assert.IsType<Dictionary<string, object>>(parsed["root"]);

        Assert.Equal("C:\\Games\\\"Test\"", root["path"]);
    }

    [Fact]
    public void Parse_UnclosedBlock_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => VdfParser.Parse("\"root\"\n{\n  \"key\" \"value\"\n"));
    }

    [Fact]
    public void Parse_KeyWithoutValue_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => VdfParser.Parse("\"root\"\n{\n  \"key\"\n}"));
    }

    [Fact]
    public void Parse_UnterminatedQuote_ThrowsFormatException()
    {
        // Kesilmemiş tırnak = bozuk VDF. Sessizce kabul etmek yerine hata verilir;
        // böylece bozuk manifestler yanlış kurulum kaydı üretmez.
        Assert.Throws<FormatException>(() => VdfParser.Parse("\"root\"\n{\n  \"key\" \"value\n}"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// appmanifest (*.acf) ayrıştırıcı
// ─────────────────────────────────────────────────────────────────────────────

public class SteamManifestParserTests
{
    private const string ValidManifest = @"""AppState""
{
	""appid""		""400""
	""name""		""Portal""
	""installdir""		""Portal""
	""SizeOnDisk""		""1234567""
	""LastUpdated""		""1700000000""
}";

    [Fact]
    public void Parse_ValidManifest_ReturnsFields()
    {
        var manifest = SteamManifestParser.Parse(ValidManifest, @"C:\x\appmanifest_400.acf");

        Assert.Equal("400", manifest.AppId);
        Assert.Equal("Portal", manifest.Name);
        Assert.Equal("Portal", manifest.InstallDir);
        Assert.Equal(1234567, manifest.SizeOnDisk);
        Assert.NotNull(manifest.LastUpdatedUtc);
        Assert.Equal(DateTimeKind.Utc, manifest.LastUpdatedUtc!.Value.Kind);
    }

    [Fact]
    public void ResolveInstallPath_CombinesCommonFolder()
    {
        var manifest = SteamManifestParser.Parse(ValidManifest, "x");

        var path = manifest.ResolveInstallPath(@"D:\SteamLibrary\steamapps");

        Assert.Equal(System.IO.Path.Combine(@"D:\SteamLibrary\steamapps", "common", "Portal"), path);
    }

    [Fact]
    public void Parse_MissingAppState_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => SteamManifestParser.Parse(@"""Other"" { ""appid"" ""1"" }", "x"));
    }

    [Theory]
    [InlineData(@"""AppState"" { ""name"" ""Portal"" ""installdir"" ""Portal"" }")]
    [InlineData(@"""AppState"" { ""appid"" ""400"" ""installdir"" ""Portal"" }")]
    [InlineData(@"""AppState"" { ""appid"" ""400"" ""name"" ""Portal"" }")]
    [InlineData(@"""AppState"" { ""appid"" ""400"" ""name"" ""Portal"" ""installdir"" """" }")]
    public void Parse_MissingRequiredField_ThrowsFormatException(string content)
    {
        Assert.Throws<FormatException>(() => SteamManifestParser.Parse(content, "x"));
    }

    [Fact]
    public void Parse_TruncatedManifest_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => SteamManifestParser.Parse("\"AppState\"\n{\n  \"appid\" \"400\"\n", "x"));
    }

    [Fact]
    public void Parse_InvalidSizeAndTimestamp_AreNull()
    {
        var content = @"""AppState"" { ""appid"" ""1"" ""name"" ""A"" ""installdir"" ""B"" ""SizeOnDisk"" ""abc"" ""LastUpdated"" ""0"" }";

        var manifest = SteamManifestParser.Parse(content, "x");

        Assert.Null(manifest.SizeOnDisk);
        Assert.Null(manifest.LastUpdatedUtc);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Kütüphane bulucu (libraryfolders.vdf)
// ─────────────────────────────────────────────────────────────────────────────

public class SteamLibraryLocatorTests
{
    [Fact]
    public void FindSteamAppsPaths_IncludesRootSteamApps()
    {
        using var steam = new TempSteamInstall();

        var paths = SteamLibraryLocator.FindSteamAppsPaths(new[] { steam.RootPath });

        Assert.Contains(steam.SteamApps, paths);
    }

    [Fact]
    public void FindSteamAppsPaths_AdditionalLibrariesFromVdf_AreDetected()
    {
        using var steam = new TempSteamInstall();
        var libraryRoot = steam.CreateLibrary("D1");
        steam.WriteLibraryFolders(steam.RootPath, libraryRoot);

        var paths = SteamLibraryLocator.FindSteamAppsPaths(new[] { steam.RootPath });

        Assert.Equal(2, paths.Count);
        Assert.Contains(System.IO.Path.Combine(libraryRoot, "steamapps"), paths);
    }

    [Fact]
    public void FindSteamAppsPaths_LegacyFlatFormat_IsSupported()
    {
        using var steam = new TempSteamInstall();
        var libraryRoot = steam.CreateLibrary("Legacy");
        steam.WriteLegacyLibraryFolders(libraryRoot);

        var paths = SteamLibraryLocator.FindSteamAppsPaths(new[] { steam.RootPath });

        Assert.Contains(System.IO.Path.Combine(libraryRoot, "steamapps"), paths);
    }

    [Fact]
    public void FindSteamAppsPaths_MissingVdf_ReturnsRootOnly()
    {
        using var steam = new TempSteamInstall();

        var paths = SteamLibraryLocator.FindSteamAppsPaths(new[] { steam.RootPath });

        Assert.Single(paths);
    }

    [Fact]
    public void FindSteamAppsPaths_BrokenVdf_AddsWarningAndContinues()
    {
        using var steam = new TempSteamInstall();
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(steam.SteamApps, "libraryfolders.vdf"),
            "\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\x\" ");

        var warnings = new List<string>();
        var paths = SteamLibraryLocator.FindSteamAppsPaths(new[] { steam.RootPath }, warnings);

        Assert.Contains(steam.SteamApps, paths);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void FindSteamAppsPaths_NonExistentLibrary_AddsWarning()
    {
        using var steam = new TempSteamInstall();
        var missingLibrary = System.IO.Path.Combine(steam.RootPath, "missing-lib");
        steam.WriteLibraryFolders(steam.RootPath, missingLibrary);

        var warnings = new List<string>();
        var paths = SteamLibraryLocator.FindSteamAppsPaths(new[] { steam.RootPath }, warnings);

        Assert.Single(paths);
        Assert.Contains(warnings, w => w.Contains("bulunamadı", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindSteamAppsPaths_DuplicateRoots_AreDeduplicated()
    {
        using var steam = new TempSteamInstall();

        var paths = SteamLibraryLocator.FindSteamAppsPaths(new[] { steam.RootPath, steam.RootPath });

        Assert.Single(paths);
    }

    [Fact]
    public void FindSteamAppsPaths_NonExistentRoot_IsIgnored()
    {
        var paths = SteamLibraryLocator.FindSteamAppsPaths(new[] { @"Z:\does-not-exist\Steam" });

        Assert.Empty(paths);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Steam kurulu oyun sağlayıcısı (dosya sistemi, salt okunur)
// ─────────────────────────────────────────────────────────────────────────────

public class SteamInstalledGameProviderTests
{
    private static SteamInstalledGameProvider CreateProvider(params string[] roots)
        => new(new FixedSteamRootLocator(roots));

    [Fact]
    public async Task ScanInstalledGamesAsync_ReadsManifestFromRootLibrary()
    {
        using var steam = new TempSteamInstall();
        steam.WriteManifest(steam.SteamApps, "400", "Portal", "Portal");
        var provider = CreateProvider(steam.RootPath);

        var installs = ((await provider.ScanAsync()).Games).ToList();

        var install = Assert.Single(installs);
        Assert.Equal("400", install.InstallSourceId);
        Assert.Equal("Steam", install.InstallSource);
        Assert.Equal("Portal", install.DetectedGameName);
        Assert.True(install.IsInstalled);
        Assert.Equal(
            System.IO.Path.Combine(steam.SteamApps, "common", "Portal"),
            install.InstallPath);
        Assert.Empty(provider.LastScanWarnings);
    }

    [Fact]
    public async Task ScanInstalledGamesAsync_ReadsAdditionalLibraries()
    {
        using var steam = new TempSteamInstall();
        steam.WriteManifest(steam.SteamApps, "400", "Portal", "Portal");

        var libraryRoot = steam.CreateLibrary("Extra");
        var librarySteamApps = System.IO.Path.Combine(libraryRoot, "steamapps");
        steam.WriteManifest(librarySteamApps, "620", "Portal 2", "Portal 2");
        steam.WriteLibraryFolders(steam.RootPath, libraryRoot);

        var provider = CreateProvider(steam.RootPath);
        var installs = ((await provider.ScanAsync()).Games).ToList();

        Assert.Equal(2, installs.Count);
        Assert.Contains(installs, i => i.InstallSourceId == "400");
        Assert.Contains(installs, i => i.InstallSourceId == "620");
    }

    [Fact]
    public async Task ScanInstalledGamesAsync_BrokenManifest_IsSkippedWithWarning()
    {
        using var steam = new TempSteamInstall();
        steam.WriteManifest(steam.SteamApps, "400", "Portal", "Portal");
        steam.WriteBrokenManifest(steam.SteamApps, "999", "\"AppState\" { \"appid\" \"999\" ");

        var provider = CreateProvider(steam.RootPath);
        var installs = ((await provider.ScanAsync()).Games).ToList();

        Assert.Single(installs);
        Assert.Contains(provider.LastScanWarnings, w => w.Contains("Bozuk manifest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanInstalledGamesAsync_MissingInstallFolder_MarksNotInstalled()
    {
        using var steam = new TempSteamInstall();
        steam.WriteManifest(steam.SteamApps, "400", "Portal", "Portal", createInstallFolder: false);

        var provider = CreateProvider(steam.RootPath);
        var installs = ((await provider.ScanAsync()).Games).ToList();

        var install = Assert.Single(installs);
        Assert.False(install.IsInstalled);
        Assert.Contains(provider.LastScanWarnings, w => w.Contains("oyun klasörü bulunamadı", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanInstalledGamesAsync_NoRoots_ReturnsEmptyWithWarning()
    {
        var provider = new SteamInstalledGameProvider(new FixedSteamRootLocator());

        var installs = ((await provider.ScanAsync()).Games).ToList();

        Assert.Empty(installs);
        Assert.NotEmpty(provider.LastScanWarnings);
    }

    [Fact]
    public async Task ScanInstalledGamesAsync_NoSteamApps_ReturnsEmptyWithWarning()
    {
        using var steam = new TempSteamInstall();
        var emptyRoot = System.IO.Path.Combine(steam.RootPath, "empty-root");
        System.IO.Directory.CreateDirectory(emptyRoot);

        var provider = CreateProvider(emptyRoot);
        var installs = ((await provider.ScanAsync()).Games).ToList();

        Assert.Empty(installs);
        Assert.NotEmpty(provider.LastScanWarnings);
    }

    [Fact]
    public async Task ScanInstalledGamesAsync_Cancelled_Throws()
    {
        using var steam = new TempSteamInstall();
        steam.WriteManifest(steam.SteamApps, "400", "Portal", "Portal");
        var provider = CreateProvider(steam.RootPath);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await provider.ScanAsync(cts.Token));
    }

    [Fact]
    public async Task DetectInstallationAsync_FindsByAppId()
    {
        using var steam = new TempSteamInstall();
        steam.WriteManifest(steam.SteamApps, "400", "Portal", "Portal");
        var provider = CreateProvider(steam.RootPath);

        var found = await provider.DetectInstallationAsync("400");

        Assert.NotNull(found);
        Assert.Equal("Portal", found!.DetectedGameName);
        Assert.Null(await provider.DetectInstallationAsync("does-not-exist"));
    }

    [Fact]
    public async Task ScanInstalledGamesAsync_DoesNotModifyFilesOnDisk()
    {
        using var steam = new TempSteamInstall();
        var manifestPath = steam.WriteManifest(steam.SteamApps, "400", "Portal", "Portal");
        var before = System.IO.File.ReadAllText(manifestPath);
        var installFolder = System.IO.Path.Combine(steam.SteamApps, "common", "Portal");
        var beforeEntries = System.IO.Directory.GetFileSystemEntries(installFolder).Length;

        var provider = CreateProvider(steam.RootPath);
        await provider.ScanAsync();

        Assert.Equal(before, System.IO.File.ReadAllText(manifestPath));
        Assert.Equal(beforeEntries, System.IO.Directory.GetFileSystemEntries(installFolder).Length);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Kurulu oyun tarama servisi (kalıcılaştırma, dedupe, katalog eşleştirme)
// ─────────────────────────────────────────────────────────────────────────────

public class InstalledGameScanServiceTests
{
    private static InstalledGameScanService CreateService(TempDatabase database, params IInstalledGameProvider[] providers)
        => new(database.CreateFactory(), providers, NullLogger<InstalledGameScanService>.Instance);

    private static StubInstalledGameProvider ProviderReturning(params InstalledGame[] installs)
        => new("Steam", _ => Task.FromResult<IEnumerable<InstalledGame>>(installs));

    [Fact]
    public async Task ScanAsync_PersistsInstalledGamesAndCreatesCatalogGames()
    {
        using var database = new TempDatabase();
        var service = CreateService(database, ProviderReturning(
            StubInstalledGameProvider.Install("400", "Portal", @"C:\Steam\steamapps\common\Portal"),
            StubInstalledGameProvider.Install("620", "Portal 2", @"C:\Steam\steamapps\common\Portal 2")));

        var result = await service.ScanAsync();

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalFound);
        Assert.Equal(2, result.Added);
        Assert.Equal(2, result.CatalogGamesCreated);
        Assert.Empty(result.Warnings);

        using var context = database.CreateContext();
        Assert.Equal(2, context.InstalledGames.Count());
        Assert.Equal(2, context.Games.Count());
        Assert.Equal(2, context.GamePlatforms.Count());
        Assert.All(context.GamePlatforms, p =>
        {
            Assert.Equal("Steam", p.PlatformName);
            Assert.False(string.IsNullOrEmpty(p.PlatformGameId));
            Assert.StartsWith("https://store.steampowered.com/app/", p.StoreUrl);
        });
    }

    [Fact]
    public async Task ScanAsync_SecondRun_DoesNotDuplicate()
    {
        using var database = new TempDatabase();
        var installs = new[]
        {
            StubInstalledGameProvider.Install("400", "Portal", @"C:\Steam\steamapps\common\Portal")
        };
        var service = CreateService(database, ProviderReturning(installs));

        await service.ScanAsync();
        var second = await service.ScanAsync();

        Assert.True(second.Success);
        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(1, second.Unchanged);
        Assert.Equal(0, second.CatalogGamesCreated);

        using var context = database.CreateContext();
        Assert.Equal(1, context.InstalledGames.Count());
        Assert.Equal(1, context.Games.Count());
    }

    [Fact]
    public async Task ScanAsync_LinksToExistingCatalogGameByAppId()
    {
        using var database = new TempDatabase();

        int existingGameId;

        using (var context = database.CreateContext())
        {
            var game = new Game { Name = "Portal (katalog)" };
            game.Platforms.Add(new GamePlatform { PlatformName = "Steam", PlatformGameId = "400" });
            context.Games.Add(game);
            await context.SaveChangesAsync();
            existingGameId = game.Id;
        }

        var service = CreateService(database, ProviderReturning(
            StubInstalledGameProvider.Install("400", "Portal", @"C:\Steam\steamapps\common\Portal")));

        var result = await service.ScanAsync();

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.CatalogGamesCreated);

        using var verify = database.CreateContext();
        var install = verify.InstalledGames.Single();
        Assert.Equal(existingGameId, install.GameId);
        Assert.Equal("Portal (katalog)", verify.Games.Single().Name);
    }

    [Fact]
    public async Task ScanAsync_MissingInstallation_IsMarkedNotInstalledNotDeleted()
    {
        using var database = new TempDatabase();
        var both = new[]
        {
            StubInstalledGameProvider.Install("400", "Portal", @"C:\Steam\steamapps\common\Portal"),
            StubInstalledGameProvider.Install("620", "Portal 2", @"C:\Steam\steamapps\common\Portal 2")
        };

        await CreateService(database, ProviderReturning(both)).ScanAsync();

        var onlyOne = new[]
        {
            StubInstalledGameProvider.Install("400", "Portal", @"C:\Steam\steamapps\common\Portal")
        };
        var result = await CreateService(database, ProviderReturning(onlyOne)).ScanAsync();

        Assert.True(result.Success);
        Assert.Equal(1, result.MarkedNotInstalled);

        using var context = database.CreateContext();
        Assert.Equal(2, context.InstalledGames.Count()); // silinmedi
        Assert.Equal(1, context.InstalledGames.Count(i => i.IsInstalled));
        Assert.False(context.InstalledGames.Single(i => i.InstallSourceId == "620").IsInstalled);
    }

    [Fact]
    public async Task ScanAsync_ProviderThrows_ContinuesWithOtherProvidersAndWarns()
    {
        using var database = new TempDatabase();

        var failing = new StubInstalledGameProvider("Steam", _ => throw new InvalidOperationException("disk okunamadı"));
        var working = ProviderReturning(
            StubInstalledGameProvider.Install("400", "Portal", @"C:\Steam\steamapps\common\Portal"));

        var service = CreateService(database, failing, working);
        var result = await service.ScanAsync();

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("taranamadı", StringComparison.OrdinalIgnoreCase));

        using var context = database.CreateContext();
        Assert.Equal(1, context.InstalledGames.Count());
    }

    [Fact]
    public async Task ScanAsync_ProviderWarnings_AreSurfaced()
    {
        using var database = new TempDatabase();
        var provider = new StubInstalledGameProvider(
            "Steam",
            _ => Task.FromResult<IEnumerable<InstalledGame>>(Array.Empty<InstalledGame>()),
            "Kütüphane okunamadı: erişim reddedildi");

        var result = await CreateService(database, provider).ScanAsync();

        Assert.True(result.Success);
        Assert.Equal(0, result.TotalFound);
        Assert.Contains(result.Warnings, w => w.Contains("erişim reddedildi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanAsync_NoProviders_ReturnsSuccessZero()
    {
        using var database = new TempDatabase();

        var result = await CreateService(database).ScanAsync();

        Assert.True(result.Success);
        Assert.Equal(0, result.TotalFound);
        Assert.Equal(0, result.Added);
    }

    [Fact]
    public async Task ScanAsync_SameInstallReportedTwice_IsDeduplicatedWithinRun()
    {
        using var database = new TempDatabase();
        var duplicate = StubInstalledGameProvider.Install("400", "Portal", @"C:\Steam\steamapps\common\Portal");

        var service = CreateService(database, ProviderReturning(duplicate, duplicate));
        var result = await service.ScanAsync();

        Assert.Equal(1, result.TotalFound);
        Assert.Equal(1, result.Added);

        using var context = database.CreateContext();
        Assert.Equal(1, context.InstalledGames.Count());
    }

    [Fact]
    public async Task ScanAsync_InstallWithoutName_IsSkippedWithWarning()
    {
        using var database = new TempDatabase();
        var nameless = new InstalledGame
        {
            InstallPath = @"C:\Steam\steamapps\common\Unknown",
            InstallSource = "Steam",
            InstallSourceId = "999",
            DetectedGameName = null
        };

        var result = await CreateService(database, ProviderReturning(nameless)).ScanAsync();

        Assert.Equal(0, result.Added);
        Assert.Contains(result.Warnings, w => w.Contains("oyun adı belirlenemedi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanAsync_EmptyPath_IsSkippedWithWarning()
    {
        using var database = new TempDatabase();
        var empty = new InstalledGame { InstallPath = "  ", InstallSource = "Steam", InstallSourceId = "1", DetectedGameName = "X" };

        var result = await CreateService(database, ProviderReturning(empty)).ScanAsync();

        Assert.Equal(0, result.Added);
        Assert.Contains(result.Warnings, w => w.Contains("boş kurulum yolu", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetInstalledGamesAsync_ReturnsOnlyInstalledWithGameData()
    {
        using var database = new TempDatabase();
        var service = CreateService(database, ProviderReturning(
            StubInstalledGameProvider.Install("400", "Portal", @"C:\Steam\steamapps\common\Portal"),
            StubInstalledGameProvider.Install("620", "Portal 2", @"C:\Steam\steamapps\common\Portal 2", isInstalled: false)));

        await service.ScanAsync();

        var installed = await service.GetInstalledGamesAsync();

        var row = Assert.Single(installed);
        Assert.Equal("Portal", row.Game.Name);
        Assert.Equal("Steam", row.InstallSource);
        Assert.Equal(1, await service.CountInstalledAsync());
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// R1: tarama güvenilirliği — başarısız/erişilemeyen/kısmi tarama mevcut kayıtları bozmamalı
// ─────────────────────────────────────────────────────────────────────────────

public class InstalledGameScanReliabilityTests
{
    private static InstalledGameScanService CreateService(TempDatabase database, params IInstalledGameProvider[] providers)
        => new(database.CreateFactory(), providers, NullLogger<InstalledGameScanService>.Instance);

    private static InstalledGame[] TwoSteamInstalls() => new[]
    {
        StubInstalledGameProvider.Install("400", "Portal", @"C:\Steam\steamapps\common\Portal"),
        StubInstalledGameProvider.Install("620", "Portal 2", @"C:\Steam\steamapps\common\Portal 2")
    };

    private static StubInstalledGameProvider Completed(params InstalledGame[] games)
        => new("Steam", _ => Task.FromResult<IEnumerable<InstalledGame>>(games));

    [Fact]
    public void Batch_StatusSemantics_AreDistinct()
    {
        var completed = InstalledGameScanBatch.Completed("Steam", Array.Empty<InstalledGame>());
        var partial = InstalledGameScanBatch.Completed("Steam", Array.Empty<InstalledGame>(), isPartial: true);
        var unavailable = InstalledGameScanBatch.Unavailable("Steam", "yok");
        var failed = InstalledGameScanBatch.Failed("Steam", "hata");

        Assert.True(completed.IsTrustworthy);
        Assert.True(completed.IsAuthoritative);

        Assert.True(partial.IsTrustworthy);
        Assert.False(partial.IsAuthoritative);

        Assert.False(unavailable.IsTrustworthy);
        Assert.False(unavailable.IsAuthoritative);

        Assert.False(failed.IsTrustworthy);
        Assert.False(failed.IsAuthoritative);
    }

    [Fact]
    public async Task ScanAsync_TrustworthyEmptyScan_MarksExistingAsNotInstalled()
    {
        using var database = new TempDatabase();
        await CreateService(database, Completed(TwoSteamInstalls())).ScanAsync();

        var result = await CreateService(database, Completed()).ScanAsync();

        Assert.True(result.Success);
        Assert.Equal(2, result.MarkedNotInstalled);

        using var context = database.CreateContext();
        Assert.Equal(2, context.InstalledGames.Count()); // silinmedi
        Assert.Equal(0, context.InstalledGames.Count(i => i.IsInstalled));
    }

    [Fact]
    public async Task ScanAsync_UnavailableProvider_KeepsExistingInstalls()
    {
        using var database = new TempDatabase();
        await CreateService(database, Completed(TwoSteamInstalls())).ScanAsync();

        var unavailable = StubInstalledGameProvider.Unavailable("Steam", "Steam kurulumu bulunamadı");
        var result = await CreateService(database, unavailable).ScanAsync();

        Assert.True(result.Success);
        Assert.Equal(0, result.MarkedNotInstalled);
        Assert.Equal(0, result.Updated);
        Assert.Contains("Steam", result.SkippedProviders);

        using var context = database.CreateContext();
        Assert.Equal(2, context.InstalledGames.Count(i => i.IsInstalled));
    }

    [Fact]
    public async Task ScanAsync_FailingProvider_KeepsExistingInstalls()
    {
        using var database = new TempDatabase();
        await CreateService(database, Completed(TwoSteamInstalls())).ScanAsync();

        var failing = StubInstalledGameProvider.Failing("Steam", "disk okunamadı");
        var result = await CreateService(database, failing).ScanAsync();

        Assert.True(result.Success);
        Assert.Equal(0, result.MarkedNotInstalled);
        Assert.Contains("Steam", result.SkippedProviders);
        Assert.Contains(result.Warnings, w => w.Contains("taranamadı", StringComparison.OrdinalIgnoreCase));

        using var context = database.CreateContext();
        Assert.Equal(2, context.InstalledGames.Count(i => i.IsInstalled));
    }

    [Fact]
    public async Task ScanAsync_PartialProvider_UpdatesButDoesNotMarkMissing()
    {
        using var database = new TempDatabase();
        await CreateService(database, Completed(TwoSteamInstalls())).ScanAsync();

        var partial = StubInstalledGameProvider.Partial("Steam",
            StubInstalledGameProvider.Install("400", "Portal", @"C:\Steam\steamapps\common\Portal"));

        var result = await CreateService(database, partial).ScanAsync();

        Assert.True(result.Success);
        Assert.Equal(0, result.MarkedNotInstalled);
        Assert.Contains("Steam", result.PartialProviders);

        using var context = database.CreateContext();
        Assert.Equal(2, context.InstalledGames.Count(i => i.IsInstalled));
    }

    [Fact]
    public async Task ScanAsync_SkippedProvider_DoesNotAffectOtherProviders()
    {
        using var database = new TempDatabase();

        await CreateService(database, new StubInstalledGameProvider("GOG", _ =>
            Task.FromResult<IEnumerable<InstalledGame>>(new[]
            {
                new InstalledGame
                {
                    InstallPath = @"C:\GOG\Game",
                    InstallSource = "GOG",
                    InstallSourceId = "g1",
                    DetectedGameName = "GOG Game",
                    IsInstalled = true
                }
            }))).ScanAsync();

        // Steam erişilemez, GOG taranmıyor → GOG kaydı korunmalı
        var result = await CreateService(database,
            StubInstalledGameProvider.Unavailable("Steam", "Steam yok")).ScanAsync();

        Assert.Equal(0, result.MarkedNotInstalled);

        using var context = database.CreateContext();
        Assert.Equal(1, context.InstalledGames.Count(i => i.IsInstalled));
    }

    [Fact]
    public async Task ScanAsync_TwoAppIdsSharingInstallFolder_CreateSeparateRows()
    {
        using var database = new TempDatabase();
        const string shared = @"C:\Steam\steamapps\common\Bannerlord";

        var provider = new StubInstalledGameProvider("Steam", _ =>
            Task.FromResult<IEnumerable<InstalledGame>>(new[]
            {
                StubInstalledGameProvider.Install("261550", "Bannerlord", shared),
                StubInstalledGameProvider.Install("1393600", "Bannerlord Modding Kit", shared)
            }));

        var service = CreateService(database, provider);
        var result = await service.ScanAsync();

        Assert.Equal(2, result.Added);

        using var context = database.CreateContext();
        Assert.Equal(2, context.InstalledGames.Count());

        var ids = context.InstalledGames.Select(i => i.InstallSourceId).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "1393600", "261550" }, ids);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Manuel oyun klasörü ekleme
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Geçici klasör oluşturur ve test sonunda siler.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory(string? subfolder = null)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"gle_manual_{Guid.NewGuid():N}",
            subfolder ?? "GameFolder");

        System.IO.Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            var root = System.IO.Path.GetDirectoryName(Path);

            if (!string.IsNullOrEmpty(root))
            {
                System.IO.Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // en iyi çaba
        }
    }
}

public class ManualGameFolderTests
{
    private static InstalledGameScanService CreateService(TempDatabase database)
        => new(database.CreateFactory(), Array.Empty<IInstalledGameProvider>(), NullLogger<InstalledGameScanService>.Instance);

    [Fact]
    public async Task AddManualGameAsync_ValidFolder_CreatesManualInstallWithoutStorePlatform()
    {
        using var database = new TempDatabase();
        using var folder = new TempDirectory();
        var service = CreateService(database);

        var result = await service.AddManualGameAsync(folder.Path, "Benim Oyunum");

        Assert.True(result.Success);
        Assert.False(result.AlreadyExisted);
        Assert.Equal("Benim Oyunum", result.GameName);

        using var context = database.CreateContext();
        var install = context.InstalledGames.Single();

        Assert.Equal("Manual", install.InstallSource);
        Assert.Equal(string.Empty, install.InstallSourceId);
        Assert.True(install.IsInstalled);
        Assert.Equal(1, context.Games.Count());

        // Kaynak doğrulanmadı → hiçbir mağaza/platform kaydı oluşturulmamalı.
        Assert.Equal(0, context.GamePlatforms.Count());
        Assert.Equal(0, context.CatalogSources.Count());
    }

    [Fact]
    public async Task AddManualGameAsync_EmptyName_UsesFolderName()
    {
        using var database = new TempDatabase();
        using var folder = new TempDirectory(subfolder: "MyGameDir");
        var service = CreateService(database);

        var result = await service.AddManualGameAsync(folder.Path, "   ");

        Assert.True(result.Success);
        Assert.Equal("MyGameDir", result.GameName);
    }

    [Fact]
    public async Task AddManualGameAsync_SameFolderTwice_NoDuplicate()
    {
        using var database = new TempDatabase();
        using var folder = new TempDirectory();
        var service = CreateService(database);

        var first = await service.AddManualGameAsync(folder.Path, "Oyun");
        var second = await service.AddManualGameAsync(folder.Path, "Oyun");

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.InstalledGameId, second.InstalledGameId);

        using var context = database.CreateContext();
        Assert.Equal(1, context.InstalledGames.Count());
        Assert.Equal(1, context.Games.Count());
    }

    [Fact]
    public async Task AddManualGameAsync_TrailingSeparator_IsSameFolder()
    {
        using var database = new TempDatabase();
        using var folder = new TempDirectory();
        var service = CreateService(database);

        await service.AddManualGameAsync(folder.Path, "Oyun");
        var second = await service.AddManualGameAsync(folder.Path + System.IO.Path.DirectorySeparatorChar, "Oyun");

        Assert.True(second.AlreadyExisted);

        using var context = database.CreateContext();
        Assert.Equal(1, context.InstalledGames.Count());
    }

    [Fact]
    public async Task AddManualGameAsync_TwoDifferentFoldersWithSameName_StaySeparate()
    {
        using var database = new TempDatabase();
        using var folderA = new TempDirectory(subfolder: "Kopya");
        using var folderB = new TempDirectory(subfolder: "Kopya");
        var service = CreateService(database);

        var a = await service.AddManualGameAsync(folderA.Path, "Ayni Isim");
        var b = await service.AddManualGameAsync(folderB.Path, "Ayni Isim");

        Assert.True(a.Success);
        Assert.True(b.Success);
        Assert.False(b.AlreadyExisted);
        Assert.NotEqual(a.InstalledGameId, b.InstalledGameId);

        using var context = database.CreateContext();
        Assert.Equal(2, context.InstalledGames.Count());
        Assert.Equal(2, context.Games.Count()); // ada göre otomatik birleştirme yapılmaz
    }

    [Fact]
    public async Task AddManualGameAsync_NonExistentFolder_ReturnsFailure()
    {
        using var database = new TempDatabase();
        var service = CreateService(database);
        var missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gle_missing_{Guid.NewGuid():N}");

        var result = await service.AddManualGameAsync(missing, "Yok");

        Assert.False(result.Success);
        Assert.Contains("bulunamadı", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);

        using var context = database.CreateContext();
        Assert.Equal(0, context.InstalledGames.Count());
    }

    [Fact]
    public async Task AddManualGameAsync_BlankPath_ReturnsFailure()
    {
        using var database = new TempDatabase();
        var service = CreateService(database);

        var result = await service.AddManualGameAsync("   ", "X");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AddManualGameAsync_EmptyFolder_IsAcceptedWithWarning()
    {
        using var database = new TempDatabase();
        using var folder = new TempDirectory();
        var service = CreateService(database);

        var result = await service.AddManualGameAsync(folder.Path, "Bos");

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("boş", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddManualGameAsync_DoesNotDeleteExistingCatalogData()
    {
        using var database = new TempDatabase();

        using (var context = database.CreateContext())
        {
            var game = new Game { Name = "Katalog Oyunu" };
            game.Platforms.Add(new GamePlatform { PlatformName = "Steam", PlatformGameId = "400" });
            context.Games.Add(game);
            await context.SaveChangesAsync();
        }

        using var folder = new TempDirectory();
        var service = CreateService(database);
        var result = await service.AddManualGameAsync(folder.Path, "Manuel Oyun");

        Assert.True(result.Success);

        using var verify = database.CreateContext();
        Assert.Equal(2, verify.Games.Count());
        Assert.Equal(1, verify.GamePlatforms.Count());
        Assert.Contains(verify.Games, g => g.Name == "Katalog Oyunu");
    }

    [Fact]
    public async Task ManualGameFolderProvider_MissingFolder_IsReportedNotInstalled()
    {
        using var database = new TempDatabase();
        var service = CreateService(database);

        var folderRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gle_manual_{Guid.NewGuid():N}");
        var gameDir = System.IO.Path.Combine(folderRoot, "GoingAway");
        System.IO.Directory.CreateDirectory(gameDir);

        var added = await service.AddManualGameAsync(gameDir, "Giden Oyun");
        Assert.True(added.Success);

        // Klasörü sil: kayıt kalmalı, "kurulu değil" işaretlenmeli.
        System.IO.Directory.Delete(gameDir, recursive: true);

        var provider = new ManualGameFolderProvider(database.CreateFactory(), NullLogger<ManualGameFolderProvider>.Instance);
        var scanService = new InstalledGameScanService(
            database.CreateFactory(),
            new IInstalledGameProvider[] { provider },
            NullLogger<InstalledGameScanService>.Instance);

        var result = await scanService.ScanAsync();

        Assert.True(result.Success);

        using var context = database.CreateContext();
        var install = context.InstalledGames.Single();
        Assert.False(install.IsInstalled);
        Assert.Equal(1, context.InstalledGames.Count()); // silinmedi

        try { System.IO.Directory.Delete(folderRoot, recursive: true); } catch { }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Kaynak kataloğu (dürüst destek durumu)
// ─────────────────────────────────────────────────────────────────────────────

public class GameSourceCatalogTests
{
    [Fact]
    public void OnlySteamAndManualAreImplemented()
    {
        var implemented = GameSourceCatalog.Implemented.Select(s => s.Key).ToList();

        Assert.Equal(2, implemented.Count);
        Assert.Contains(GameSourceCatalog.SteamKey, implemented);
        Assert.Contains(GameSourceCatalog.ManualKey, implemented);
    }

    [Theory]
    [InlineData("Epic")]
    [InlineData("GOG")]
    [InlineData("Ubisoft")]
    [InlineData("EA")]
    [InlineData("Xbox")]
    public void OtherLaunchersAreMarkedAsPlannedNotImplemented(string key)
    {
        var source = GameSourceCatalog.Find(key);

        Assert.NotNull(source);
        Assert.Equal(GameSourceSupportLevel.Planned, source!.SupportLevel);
        Assert.Contains("Planlandı", source.SupportText);
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        Assert.NotNull(GameSourceCatalog.Find("steam"));
        Assert.NotNull(GameSourceCatalog.Find("MANUAL"));
        Assert.Null(GameSourceCatalog.Find("bilinmeyen"));
    }
}









