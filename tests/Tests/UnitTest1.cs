namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Data;
using GameLanguageExplorer.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class GameTests
{
    [Fact]
    public void Game_CanBeCreated_WithRequiredProperties()
    {
        var game = new Game
        {
            Name = "Test Game",
            Developer = "Test Developer",
            Publisher = "Test Publisher"
        };

        Assert.Equal("Test Game", game.Name);
        Assert.Equal("Test Developer", game.Developer);
        Assert.Equal("Test Publisher", game.Publisher);
    }

    [Fact]
    public void GamePlatform_StoresPlatformInfo()
    {
        var platform = new GamePlatform
        {
            PlatformName = "Steam",
            PlatformGameId = "12345",
            StoreUrl = "https://store.steampowered.com/app/12345/"
        };

        Assert.Equal("Steam", platform.PlatformName);
        Assert.Equal("12345", platform.PlatformGameId);
    }

    [Fact]
    public void GameProfile_TracksVerificationStatus()
    {
        var profile = new GameProfile
        {
            SourceOfInformation = "Steam API",
            VerificationStatus = "Predicted",
            ConfidenceScore = 0.75
        };

        Assert.Equal("Steam API", profile.SourceOfInformation);
        Assert.Equal("Predicted", profile.VerificationStatus);
        Assert.Equal(0.75, profile.ConfidenceScore);
    }

    [Fact]
    public void DiscoveredFile_StoresFileInfo()
    {
        var file = new DiscoveredFile
        {
            FilePath = @"C:\Games\TestGame\localization\strings.json",
            FileName = "strings.json",
            Sha256 = "abc123",
            DetectedFormat = "JSON",
            IsReadable = true,
            IsWritable = true
        };

        Assert.Equal("strings.json", file.FileName);
        Assert.True(file.IsReadable);
        Assert.True(file.IsWritable);
        Assert.Equal("JSON", file.DetectedFormat);
    }

// ─────────────────────────────────────────────────────────────────────────────
// Mock provider for testing CatalogSyncService without network access
// ─────────────────────────────────────────────────────────────────────────────
public class FakeCatalogProvider : IGameCatalogProvider
{
    public string ProviderName { get; }
    public bool IsApiAvailable { get; set; } = true;
    private readonly Func<CancellationToken, Task<IEnumerable<Game>>> _fetchFunc;

    public FakeCatalogProvider(string name, Func<CancellationToken, Task<IEnumerable<Game>>> fetchFunc)
    {
        ProviderName = name;
        _fetchFunc = fetchFunc;
    }

    public Task<IEnumerable<Game>> FetchGamesAsync(CancellationToken cancellationToken = default) => _fetchFunc(cancellationToken);
    public Task<Game?> GetGameDetailsAsync(string platformGameId, CancellationToken cancellationToken = default) => Task.FromResult<Game?>(null);
    public Task SyncCatalogAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

// ─────────────────────────────────────────────────────────────────────────────
// CatalogSyncService Tests
// ─────────────────────────────────────────────────────────────────────────────
public class CatalogSyncServiceTests : IDisposable
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly string _dbPath;

    public CatalogSyncServiceTests()
    {
        _dbPath = $"test_catalog_{Guid.NewGuid():N}.db";
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using (var ctx = new AppDbContext(_dbOptions))
        {
            ctx.Database.EnsureCreated();
            if (!ctx.CatalogSources.Any(c => c.Name == "Steam"))
            {
                ctx.CatalogSources.Add(new CatalogSource
                {
                    Name = "Steam",
                    BaseUrl = "https://api.steampowered.com",
                    IsApiAvailable = true,
                    LastSyncAt = DateTime.UtcNow
                });
                ctx.SaveChanges();
            }
        }
    }

    public void Dispose()
    {
        try { System.IO.File.Delete(_dbPath); } catch { }
    }

    private CatalogSyncService CreateService(IEnumerable<IGameCatalogProvider> providers)
    {
        var logger = NullLogger<CatalogSyncService>.Instance;
        return new CatalogSyncService(new TestDbContextFactory(_dbOptions), providers, logger);
    }
    // ── Provider not found ────────────────────────────────────────────────
    [Fact]
    public async Task SyncProviderAsync_UnknownProvider_ReturnsFailure()
    {
        var service = CreateService(Array.Empty<IGameCatalogProvider>());
        var result = await service.SyncProviderAsync("Nonexistent", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Sağlayıcı bulunamadı: Nonexistent", result.ErrorMessage);
        Assert.Equal("Nonexistent", result.ProviderName);
    }

    // ── Successful sync with new games ────────────────────────────────────
    [Fact]
    public async Task SyncProviderAsync_NewGames_AddsAll()
    {
        var games = new List<Game>
        {
            new Game { Name = "Game A", Developer = "Dev A", Publisher = "Pub A" },
            new Game { Name = "Game B", Developer = "Dev B", Publisher = "Pub B" },
        };
        var provider = new FakeCatalogProvider("Steam", ct => Task.FromResult<IEnumerable<Game>>(games));
        var service = CreateService(new[] { provider });

        var result = await service.SyncProviderAsync("Steam", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalFetched);
        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Unchanged);

        using var ctx = new AppDbContext(_dbOptions);
        Assert.Equal(2, ctx.Games.Count());
        Assert.Contains(ctx.Games, g => g.Name == "Game A");
        Assert.Contains(ctx.Games, g => g.Name == "Game B");
    }

    // ── Duplicate games are updated, not duplicated ───────────────────────
    [Fact]
    public async Task SyncProviderAsync_DuplicateNames_UpdatesExisting()
    {
        using (var ctx = new AppDbContext(_dbOptions))
        {
            ctx.Games.Add(new Game { Name = "Game A", Developer = "OldDev", Publisher = "OldPub" });
            await ctx.SaveChangesAsync();
        }

        var games = new List<Game>
        {
            new Game { Name = "Game A", Developer = "NewDev", Publisher = "NewPub" },
        };
        var provider = new FakeCatalogProvider("Steam", ct => Task.FromResult<IEnumerable<Game>>(games));
        var service = CreateService(new[] { provider });

        var result = await service.SyncProviderAsync("Steam", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.TotalFetched);
        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Updated);

        using var ctx2 = new AppDbContext(_dbOptions);
        Assert.Equal(1, ctx2.Games.Count(g => g.Name == "Game A"));
        var game = ctx2.Games.First(g => g.Name == "Game A");
        Assert.Equal("NewDev", game.Developer);
        Assert.Equal("NewPub", game.Publisher);
    }
}
public class SqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    public SqliteRepositoryTests()
    {
        _dbPath = $"test_sqlite_{Guid.NewGuid():N}.db";
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using var ctx = new AppDbContext(_dbOptions);
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        try { System.IO.File.Delete(_dbPath); } catch { }
    }
    [Fact]
    public async Task AddGame_PersistsToDatabase()
    {
        using var ctx = new AppDbContext(_dbOptions);
        var game = new Game { Name = "Persisted Game", Developer = "Dev" };
        ctx.Games.Add(game);
        await ctx.SaveChangesAsync();

        Assert.True(game.Id > 0);

        using var ctx2 = new AppDbContext(_dbOptions);
        var loaded = await ctx2.Games.FindAsync(game.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Persisted Game", loaded!.Name);
    }

    [Fact]
    public async Task UniqueCatalogSourceName_EnforcedByIndex()
    {
        using var ctx = new AppDbContext(_dbOptions);
        ctx.CatalogSources.Add(new CatalogSource { Name = "Steam", BaseUrl = "url1" });
        await ctx.SaveChangesAsync();

        ctx.CatalogSources.Add(new CatalogSource { Name = "Steam", BaseUrl = "url2" });
        await Assert.ThrowsAsync<DbUpdateException>(async () => await ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task UniquePlatformIndex_EnforcedByDatabase()
    {
        using var ctx = new AppDbContext(_dbOptions);
        var game = new Game { Name = "G" };
        ctx.Games.Add(game);
        await ctx.SaveChangesAsync();

        var source = new CatalogSource { Name = "Steam" };
        ctx.CatalogSources.Add(source);
        await ctx.SaveChangesAsync();

        ctx.GamePlatforms.Add(new GamePlatform { GameId = game.Id, CatalogSourceId = source.Id, PlatformName = "Steam", PlatformGameId = "123" });
        await ctx.SaveChangesAsync();

        ctx.GamePlatforms.Add(new GamePlatform { GameId = game.Id, CatalogSourceId = source.Id, PlatformName = "Steam", PlatformGameId = "123" });
        await Assert.ThrowsAsync<DbUpdateException>(async () => await ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task SearchByName_ContainsMatch()
    {
        using var ctx = new AppDbContext(_dbOptions);
        ctx.Games.Add(new Game { Name = "The Witcher 3" });
        ctx.Games.Add(new Game { Name = "Cyberpunk 2077" });
        await ctx.SaveChangesAsync();

        var results = await ctx.Games
            .Where(g => g.Name.Contains("Witcher"))
            .ToListAsync();

        Assert.Single(results);
        Assert.Equal("The Witcher 3", results[0].Name);
    }

    [Fact]
    public async Task OrderByName_ReturnsSorted()
    {
        using var ctx = new AppDbContext(_dbOptions);
        ctx.Games.Add(new Game { Name = "Zelda" });
        ctx.Games.Add(new Game { Name = "Mario" });
        ctx.Games.Add(new Game { Name = "Halo" });
        await ctx.SaveChangesAsync();

        var results = await ctx.Games.OrderBy(g => g.Name).ToListAsync();

        Assert.Equal("Halo", results[0].Name);
        Assert.Equal("Mario", results[1].Name);
        Assert.Equal("Zelda", results[2].Name);
    }
}

public class SteamCatalogProviderMockedTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }

    private static HttpClient CreateMockClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        return new HttpClient(new MockHttpMessageHandler(handler)) { BaseAddress = new Uri("https://api.steampowered.com") };
    }

    /// <summary>API anahtarı tanımlı sağlayıcı: tam katalog (IStoreService) yolu testleri için.</summary>
    private static SteamCatalogProvider CreateProviderWithKey(HttpClient client)
        => new(client, null, () => "TEST-API-KEY");

    [Fact]
    public async Task FetchGamesAsync_WithoutApiKey_ThrowsInvalidOperationException()
    {
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        var provider = new SteamCatalogProvider(client);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.FetchGamesAsync());

        Assert.Contains("STEAM_API_KEY", ex.Message);
    }

    [Fact]
    public async Task FetchGamesAsync_ValidResponse_ReturnsGames()
    {
        var json = "{\"response\":{\"apps\":[{\"appid\":400,\"name\":\"Portal\"},{\"appid\":620,\"name\":\"Portal 2\"}],\"have_more\":false}}";
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            }));
        var provider = CreateProviderWithKey(client);

        var games = (await provider.FetchGamesAsync()).ToList();

        Assert.Equal(2, games.Count);
        Assert.Equal("Portal", games[0].Name);
        Assert.Equal("400", games[0].ExternalId);
        Assert.Equal("620", games[1].ExternalId);
    }

    [Fact]
    public async Task FetchGamesAsync_EmptyApps_ReturnsEmpty()
    {
        var json = "{\"response\":{\"apps\":[],\"have_more\":false}}";
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            }));
        var provider = CreateProviderWithKey(client);

        var games = (await provider.FetchGamesAsync()).ToList();

        Assert.Empty(games);
    }

    [Fact]
    public async Task FetchGamesAsync_MissingApps_ReturnsEmpty()
    {
        var json = "{\"response\":{}}";
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            }));
        var provider = CreateProviderWithKey(client);

        var games = (await provider.FetchGamesAsync()).ToList();

        Assert.Empty(games);
    }

    [Fact]
    public async Task FetchGamesAsync_HttpError_ThrowsHttpRequestException()
    {
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)));
        var provider = CreateProviderWithKey(client);

        await Assert.ThrowsAsync<HttpRequestException>(async () => await provider.FetchGamesAsync());
    }

    [Fact]
    public async Task FetchGamesAsync_NotFound_ThrowsHttpRequestException()
    {
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)));
        var provider = CreateProviderWithKey(client);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () => await provider.FetchGamesAsync());

        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task FetchGamesAsync_FiltersEmptyNames()
    {
        var json = "{\"response\":{\"apps\":[{\"appid\":1,\"name\":\"\"},{\"appid\":2,\"name\":\"Valid Game\"},{\"appid\":3,\"name\":null}],\"have_more\":false}}";
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            }));
        var provider = CreateProviderWithKey(client);

        var games = (await provider.FetchGamesAsync()).ToList();

        Assert.Single(games);
        Assert.Equal("Valid Game", games[0].Name);
    }

    [Fact]
    public async Task FetchGamesAsync_Cancellation_ThrowsOperationCanceled()
    {
        var client = CreateMockClient(async (req, ct) =>
        {
            await Task.Delay(1000, ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });
        var provider = CreateProviderWithKey(client);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.FetchGamesAsync(cts.Token));
    }


    [Fact]
    public async Task FetchGamesAsync_LegacyAppListPayload_IsIgnored()
    {
        // Eski ISteamApps/GetAppList biçimi artık kullanılmıyor; bilinmeyen gövde boş sonuç üretir.
        var json = "{\"applist\":{\"apps\":[{\"appid\":1,\"name\":\"Game One\"}]}}";
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            }));
        var provider = CreateProviderWithKey(client);

        var games = (await provider.FetchGamesAsync()).ToList();

        Assert.Empty(games);
    }


    [Fact]
    public async Task FetchGamesAsync_EmptyResponse_ReturnsEmpty_Legacy()
    {
        var json = "{\"applist\":{\"apps\":[]}}";
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            }));
        var provider = CreateProviderWithKey(client);

        var games = (await provider.FetchGamesAsync()).ToList();

        Assert.Empty(games);
    }

    [Fact]
    public async Task FetchGamesAsync_NullApps_ReturnsEmpty_Legacy()
    {
        var json = "{\"applist\":{}}";
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            }));
        var provider = CreateProviderWithKey(client);

        var games = (await provider.FetchGamesAsync()).ToList();

        Assert.Empty(games);
    }


    [Fact]
    public async Task FetchGamesAsync_HttpError_ThrowsHttpRequestException_Legacy()
    {
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)));
        var provider = CreateProviderWithKey(client);

        await Assert.ThrowsAsync<HttpRequestException>(async () => await provider.FetchGamesAsync());
    }

    [Fact]
    public async Task FetchGamesAsync_NotFound_ThrowsHttpRequestException_Legacy()
    {
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)));
        var provider = CreateProviderWithKey(client);

        await Assert.ThrowsAsync<HttpRequestException>(async () => await provider.FetchGamesAsync());
    }

    [Fact]
    public async Task FetchGamesAsync_FiltersEmptyNames_Legacy()
    {
        var json = "{\"applist\":{\"apps\":[{\"appid\":1,\"name\":\"\"},{\"appid\":2,\"name\":\"Valid Game\"},{\"appid\":3,\"name\":null}]}}";
        var client = CreateMockClient((req, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            }));
        var provider = CreateProviderWithKey(client);

        var games = (await provider.FetchGamesAsync()).ToList();

        Assert.Empty(games);
    }


    [Fact]
    public async Task FetchGamesAsync_Cancellation_ThrowsOperationCanceled_Legacy()
    {
        var client = CreateMockClient(async (req, ct) =>
        {
            await Task.Delay(1000, ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });
        var provider = CreateProviderWithKey(client);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.FetchGamesAsync(cts.Token));
    }

public class EntityTests
{
    [Fact]
    public void Game_DefaultValues()
    {
        var game = new Game();
        Assert.Equal(string.Empty, game.Name);
        Assert.Null(game.Description);
        Assert.Null(game.Developer);
        Assert.Null(game.Publisher);
        Assert.Null(game.ReleaseDate);
        Assert.Null(game.GameEngine);
        Assert.Null(game.CoverImageUrl);
        Assert.NotEqual(default, game.CreatedAt);
        Assert.NotEqual(default, game.UpdatedAt);
        Assert.Empty(game.Platforms);
        Assert.Empty(game.Profiles);
    }

    [Fact]
    public void GamePlatform_DefaultValues()
    {
        var platform = new GamePlatform();
        Assert.Equal(0, platform.GameId);
        Assert.Null(platform.CatalogSourceId);
        Assert.Equal(string.Empty, platform.PlatformName);
        Assert.Equal(string.Empty, platform.PlatformGameId);
        Assert.Null(platform.StoreUrl);
        Assert.Null(platform.LastSyncedAt);
    }

    [Fact]
    public void CatalogSource_DefaultValues()
    {
        var source = new CatalogSource();
        Assert.Equal(string.Empty, source.Name);
        Assert.Null(source.BaseUrl);
        Assert.False(source.IsApiAvailable);
        Assert.Null(source.LastSyncAt);
        Assert.Null(source.SyncError);
        Assert.Empty(source.Platforms);
    }

    [Fact]
    public void GameProfile_DefaultValues()
    {
        var profile = new GameProfile();
        Assert.Equal(0, profile.GameId);
        Assert.Null(profile.SourceOfInformation);
        Assert.Null(profile.VerificationStatus);
        Assert.Null(profile.ConfidenceScore);
    }

    [Fact]
    public void CatalogSyncResult_DefaultValues()
    {
        var result = new CatalogSyncResult();
        Assert.Equal(string.Empty, result.ProviderName);
        Assert.Equal(0, result.TotalFetched);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Unchanged);
        Assert.False(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotEqual(default, result.SyncCompletedAt);
    }
}
public class SteamCatalogProviderTests
{
    [Fact]
    public void Constructor_NullHttpClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SteamCatalogProvider(null!));
    }

    [Fact]
    public void ProviderName_ReturnsSteam()
    {
        using var client = new HttpClient();
        var provider = new SteamCatalogProvider(client);
        Assert.Equal("Steam", provider.ProviderName);
    }

    [Fact]
    public void IsApiAvailable_ReturnsTrue()
    {
        using var client = new HttpClient();
        var provider = new SteamCatalogProvider(client);
        Assert.True(provider.IsApiAvailable);
    }

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal("Steam", SteamCatalogProvider.ProviderNameValue);
        Assert.Equal("https://api.steampowered.com", SteamCatalogProvider.BaseUrl);
    }

    [Fact]
    public void Constructor_SetsBaseAddress()
    {
        using var client = new HttpClient();
        var provider = new SteamCatalogProvider(client);
        Assert.Equal("https://api.steampowered.com/", client.BaseAddress?.ToString());
    }

    [Fact]
    public void Constructor_AddsUserAgentHeader()
    {
        using var client = new HttpClient();
        var provider = new SteamCatalogProvider(client);
        Assert.Contains(client.DefaultRequestHeaders.UserAgent, ua => ua.Product?.Name == "GameLanguageExplorer");
    }
}
public class CatalogSyncServiceExtraTests : IDisposable
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly string _dbPath;

    public CatalogSyncServiceExtraTests()
    {
        _dbPath = $"test_extra_{Guid.NewGuid():N}.db";
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using (var ctx = new AppDbContext(_dbOptions))
        {
            ctx.Database.EnsureCreated();
            if (!ctx.CatalogSources.Any(c => c.Name == "Steam"))
            {
                ctx.CatalogSources.Add(new CatalogSource { Name = "Steam", BaseUrl = "https://api.steampowered.com", IsApiAvailable = true, LastSyncAt = DateTime.UtcNow });
                ctx.SaveChanges();
            }
        }
    }

    public void Dispose() { try { System.IO.File.Delete(_dbPath); } catch { } }

    private CatalogSyncService CreateService(IEnumerable<IGameCatalogProvider> providers)
    {
        var logger = NullLogger<CatalogSyncService>.Instance;
        return new CatalogSyncService(new TestDbContextFactory(_dbOptions), providers, logger);
    }

    [Fact]
    public async Task EmptyFetch_ReturnsSuccessZero()
    {
        var provider = new FakeCatalogProvider("Steam", ct => Task.FromResult<IEnumerable<Game>>(Array.Empty<Game>()));
        var service = CreateService(new[] { provider });
        var result = await service.SyncProviderAsync("Steam", CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(0, result.TotalFetched);
        Assert.Equal(0, result.Added);
    }

    [Fact]
    public async Task NullFetch_ReturnsSuccessZero()
    {
        var provider = new FakeCatalogProvider("Steam", ct => Task.FromResult<IEnumerable<Game>>(null!));
        var service = CreateService(new[] { provider });
        var result = await service.SyncProviderAsync("Steam", CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(0, result.TotalFetched);
    }

    [Fact]
    public async Task Cancellation_StopsAndReturnsCancelled()
    {
        var games = Enumerable.Range(1, 100).Select(i => new Game { Name = $"Game {i}" }).ToList();
        var provider = new FakeCatalogProvider("Steam", async ct => { await Task.Delay(50, ct); return games; });
        var service = CreateService(new[] { provider });
        var cts = new CancellationTokenSource();
        cts.CancelAfter(1);
        var result = await service.SyncProviderAsync("Steam", cts.Token);
        Assert.False(result.Success);
        Assert.Equal("İşlem iptal edildi", result.ErrorMessage);
    }

    [Fact]
    public async Task ProviderThrows_ReturnsFailureWithError()
    {
        var provider = new FakeCatalogProvider("Steam", ct => throw new HttpRequestException("Network error"));
        var service = CreateService(new[] { provider });
        var result = await service.SyncProviderAsync("Steam", CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("Network error", result.ErrorMessage!);
    }
}
}
}
