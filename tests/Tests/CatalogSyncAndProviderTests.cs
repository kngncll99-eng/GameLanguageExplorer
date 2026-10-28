namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Data;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http;
using System.Text;

// ─────────────────────────────────────────────────────────────────────────────
// Test altyapısı
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Testlerde <see cref="IDbContextFactory{TContext}"/> sağlamak için basit uygulama.</summary>
public sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

    public AppDbContext CreateDbContext() => new(_options);
}

/// <summary>Geçici SQLite veritabanı; gerçek kullanıcı verisine dokunmaz.</summary>
public sealed class TempDatabase : IDisposable
{
    public string FilePath { get; }
    public DbContextOptions<AppDbContext> Options { get; }

    public TempDatabase(bool ensureCreated = true)
    {
        FilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gle_test_{Guid.NewGuid():N}.db");
        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={FilePath}")
            .Options;

        if (ensureCreated)
        {
            using var context = new AppDbContext(Options);
            context.Database.EnsureCreated();
        }
    }

    public AppDbContext CreateContext() => new(Options);

    public TestDbContextFactory CreateFactory() => new(Options);

    public CatalogSyncService CreateSyncService()
        => new(CreateFactory(), Array.Empty<IGameCatalogProvider>(), NullLogger<CatalogSyncService>.Instance);

    public void Dispose()
    {
        try { System.IO.File.Delete(FilePath); } catch { /* en iyi çaba */ }
    }
}

/// <summary>URL içeriğine göre sabit JSON döndüren handler (gerçek ağ erişimi yok).</summary>
public sealed class RoutingHttpMessageHandler : HttpMessageHandler
{
    private readonly IReadOnlyDictionary<string, string> _routes;

    public RoutingHttpMessageHandler(IReadOnlyDictionary<string, string> routes) => _routes = routes;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;

        foreach (var route in _routes)
        {
            if (url.Contains(route.Key, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(route.Value, Encoding.UTF8, "application/json")
                });
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>Bellekteki listeyi döndüren katalog sağlayıcısı.</summary>
public sealed class StubCatalogProvider : IGameCatalogProvider
{
    private readonly IReadOnlyList<Game> _games;

    public StubCatalogProvider(string name, params Game[] games)
    {
        ProviderName = name;
        _games = games;
    }

    public string ProviderName { get; }
    public bool IsApiAvailable => true;

    public Task<IEnumerable<Game>> FetchGamesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<Game>>(_games);

    public Task<Game?> GetGameDetailsAsync(string platformGameId, CancellationToken cancellationToken = default)
        => Task.FromResult<Game?>(null);

    public Task SyncCatalogAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

// ─────────────────────────────────────────────────────────────────────────────
// Steam: storesearch + appdetails (anahtar gerektirmeyen yol)
// ─────────────────────────────────────────────────────────────────────────────

public class SteamStoreSearchTests
{
    private const string SearchJson =
        "{\"total\":2,\"items\":[{\"type\":\"app\",\"name\":\"Portal\",\"id\":400," +
        "\"tiny_image\":\"https://cdn/400.jpg\"},{\"type\":\"app\",\"name\":\"Portal 2\",\"id\":620}]}";

    private const string DetailsJson =
        "{\"400\":{\"success\":true,\"data\":{\"type\":\"game\",\"name\":\"Portal\"," +
        "\"developers\":[\"Valve\"],\"publishers\":[\"Valve\"]," +
        "\"short_description\":\"Aperture Science\",\"header_image\":\"https://cdn/header.jpg\"," +
        "\"release_date\":{\"coming_soon\":false,\"date\":\"Oct 10, 2007\"}}}}";

    private static SteamCatalogProvider CreateProvider(bool includeDetails = true)
    {
        var routes = new Dictionary<string, string> { ["storesearch"] = SearchJson };

        if (includeDetails)
        {
            routes["appdetails"] = DetailsJson;
        }

        var client = new HttpClient(new RoutingHttpMessageHandler(routes));
        return new SteamCatalogProvider(client, null, () => null);
    }

    [Fact]
    public async Task SearchGamesAsync_ParsesAppIdAndName()
    {
        var provider = CreateProvider();

        var results = await provider.SearchGamesAsync("portal", maxResults: 10, enrichDetails: false);

        Assert.Equal(2, results.Count);
        Assert.Equal("Portal", results[0].Name);
        Assert.Equal("400", results[0].ExternalId);
        Assert.Equal("620", results[1].ExternalId);
    }

    [Fact]
    public async Task SearchGamesAsync_EnrichesWithAppDetails()
    {
        var provider = CreateProvider();

        var results = await provider.SearchGamesAsync("portal", maxResults: 1, enrichDetails: true);

        var game = Assert.Single(results);
        Assert.Equal("Valve", game.Developer);
        Assert.Equal("Valve", game.Publisher);
        Assert.Equal("Aperture Science", game.Description);
        Assert.Equal("https://cdn/header.jpg", game.CoverImageUrl);
        Assert.NotNull(game.ReleaseDate);
    }

    [Fact]
    public async Task SearchGamesAsync_EmptyTerm_ReturnsEmptyWithoutHttpCall()
    {
        var provider = CreateProvider();

        var results = await provider.SearchGamesAsync("   ");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchGamesAsync_RespectsMaxResults()
    {
        var provider = CreateProvider();

        var results = await provider.SearchGamesAsync("portal", maxResults: 1, enrichDetails: false);

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchGamesAsync_HttpError_ThrowsWithDetails()
    {
        var client = new HttpClient(new RoutingHttpMessageHandler(new Dictionary<string, string>()));

        client.BaseAddress = new Uri("https://store.steampowered.com");

        var provider = new SteamCatalogProvider(client, null, () => null);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            async () => await provider.SearchGamesAsync("portal"));

        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task GetGameDetailsAsync_InvalidId_ReturnsNull()
    {
        var provider = CreateProvider();

        Assert.Null(await provider.GetGameDetailsAsync("not-a-number"));
    }

    [Fact]
    public async Task GetGameDetailsAsync_ValidId_ReturnsEnrichedGame()
    {
        var provider = CreateProvider();

        var game = await provider.GetGameDetailsAsync("400");

        Assert.NotNull(game);
        Assert.Equal("Portal", game!.Name);
        Assert.Equal("400", game.ExternalId);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// İçe aktarma (ImportGamesAsync): kalıcılık, AppID, mağaza URL'si, idempotency
// ─────────────────────────────────────────────────────────────────────────────

public class CatalogImportTests
{
    [Fact]
    public async Task ImportGamesAsync_PersistsGameWithPlatformAppIdAndStoreUrl()
    {
        using var database = new TempDatabase();
        var service = database.CreateSyncService();

        var games = new List<Game>
        {
            new() { Name = "Portal", ExternalId = "400", Developer = "Valve", Publisher = "Valve" }
        };

        var result = await service.ImportGamesAsync("Steam", games);

        Assert.True(result.Success);
        Assert.Equal(1, result.TotalFetched);
        Assert.Equal(1, result.Added);

        using var context = database.CreateContext();
        var platform = context.GamePlatforms.Single();

        Assert.Equal("Steam", platform.PlatformName);
        Assert.Equal("400", platform.PlatformGameId);
        Assert.Equal("https://store.steampowered.com/app/400", platform.StoreUrl);

        // Aynı veritabanından yeniden okunabiliyor mu?
        Assert.Equal("Portal", context.Games.Single().Name);
        Assert.Equal("Valve", context.Games.Single().Developer);
    }

    [Fact]
    public async Task ImportGamesAsync_Twice_DoesNotDuplicate()
    {
        using var database = new TempDatabase();
        var service = database.CreateSyncService();
        var games = new List<Game> { new() { Name = "Portal", ExternalId = "400" } };

        await service.ImportGamesAsync("Steam", games);
        var second = await service.ImportGamesAsync("Steam", games);

        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(1, second.Unchanged);

        using var context = database.CreateContext();
        Assert.Equal(1, context.Games.Count());
        Assert.Equal(1, context.GamePlatforms.Count());
    }

    [Fact]
    public async Task ImportGamesAsync_EmptyList_ReturnsZeroWithoutFailure()
    {
        using var database = new TempDatabase();
        var service = database.CreateSyncService();

        var result = await service.ImportGamesAsync("Steam", Array.Empty<Game>());

        Assert.True(result.Success);
        Assert.Equal(0, result.TotalFetched);
        Assert.Equal(0, result.Added);
    }

    [Fact]
    public async Task SearchGamesAsync_DbQuery_FindsPartialName()
    {
        using var database = new TempDatabase();

        using (var context = database.CreateContext())
        {
            context.Games.AddRange(
                new Game { Name = "Portal" },
                new Game { Name = "Portal 2" },
                new Game { Name = "Half-Life 2" });
            await context.SaveChangesAsync();
        }

        var service = database.CreateSyncService();
        var results = (await service.SearchGamesAsync("Port")).ToList();

        AscendingNames(results);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task CountGamesAsync_ReturnsTotalRows()
    {
        using var database = new TempDatabase();

        using (var context = database.CreateContext())
        {
            context.Games.AddRange(new Game { Name = "A" }, new Game { Name = "B" });
            await context.SaveChangesAsync();
        }

        Assert.Equal(2, await database.CreateSyncService().CountGamesAsync());
    }

    private static void AscendingNames(IReadOnlyList<Game> games)
    {
        Assert.Equal("Portal", games[0].Name);
        Assert.Equal("Portal 2", games[1].Name);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CatalogViewModel: arama davranışı (binding'lerin bağlı olduğu komutlar)
// ─────────────────────────────────────────────────────────────────────────────

public class CatalogViewModelSearchTests
{
    private static CatalogViewModel CreateViewModel(TempDatabase database, IGameSearchProvider? searchProvider = null)
    {
        var sync = new CatalogSyncService(
            database.CreateFactory(),
            Array.Empty<IGameCatalogProvider>(),
            NullLogger<CatalogSyncService>.Instance);

        return new CatalogViewModel(database.CreateFactory(), sync, searchProvider);
    }

    private static TempDatabase SeedDatabase()
    {
        var database = new TempDatabase();

        using var context = database.CreateContext();
        context.Games.AddRange(
            new Game { Name = "Portal" },
            new Game { Name = "Portal 2" },
            new Game { Name = "Half-Life 2" },
            new Game { Name = "Counter-Strike" });
        context.SaveChanges();

        return database;
    }

    [Fact]
    public void Constructor_LoadsGamesAndTotalCount()
    {
        using var database = SeedDatabase();

        var viewModel = CreateViewModel(database);

        Assert.Equal(4, viewModel.GameCount);
        Assert.Equal(4, viewModel.Games.Count());
    }

    [Fact]
    public async Task Search_ExactName_ReturnsSingleMatch()
    {
        using var database = SeedDatabase();
        var viewModel = CreateViewModel(database);

        viewModel.SearchText = "Half-Life 2";
        await viewModel.SearchCommand.ExecuteAsync(null);

        var result = Assert.Single(viewModel.Games);
        Assert.Equal("Half-Life 2", result.Name);
    }

    [Fact]
    public async Task Search_PartialName_ReturnsAllMatches()
    {
        using var database = SeedDatabase();
        var viewModel = CreateViewModel(database);

        viewModel.SearchText = "Portal";
        await viewModel.SearchCommand.ExecuteAsync(null);

        var names = viewModel.Games.Select(g => g.Name).ToList();
        Assert.Equal(2, names.Count);
        Assert.Contains("Portal", names);
        Assert.Contains("Portal 2", names);
    }

    [Fact]
    public async Task Search_IsCaseInsensitive()
    {
        using var database = SeedDatabase();
        var viewModel = CreateViewModel(database);

        viewModel.SearchText = "pOrTaL";
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Games.Count());
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmptyAndReports()
    {
        using var database = SeedDatabase();
        var viewModel = CreateViewModel(database);

        viewModel.SearchText = "zzzzz";
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Games);
        Assert.NotNull(viewModel.StatusMessage);
        Assert.Contains("bulunamadı", viewModel.StatusMessage!);
        Assert.True(viewModel.IsStatusVisible);
    }

    [Fact]
    public async Task Search_Cleared_RestoresAllGames()
    {
        using var database = SeedDatabase();
        var viewModel = CreateViewModel(database);

        viewModel.SearchText = "zzzzz";
        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.Games);

        viewModel.SearchText = string.Empty;
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(4, viewModel.Games.Count());
    }

    [Fact]
    public async Task Search_EmptyCatalog_DoesNotThrow()
    {
        using var database = new TempDatabase();
        var viewModel = CreateViewModel(database);

        viewModel.SearchText = "portal";
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Games);
        Assert.Equal(0, viewModel.GameCount);
        Assert.Null(viewModel.ErrorMessage);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Migration: boş veritabanında şema gerçekten oluşuyor mu?
// ─────────────────────────────────────────────────────────────────────────────

public class MigrationTests
{
    [Fact]
    public void Migrate_OnEmptyDatabase_CreatesAllTables()
    {
        using var database = new TempDatabase(ensureCreated: false);

        using (var context = database.CreateContext())
        {
            context.Database.Migrate();

            var applied = context.Database.GetAppliedMigrations().ToList();
            Assert.NotEmpty(applied);
            Assert.Contains(applied, m => m.Contains("InitialCreate"));
            Assert.Contains(applied, m => m.Contains("AddCatalogSourceIdToGamePlatform"));
            Assert.Contains(applied, m => m.Contains("AlignModelSnapshot"));

            Assert.Equal(0, context.Games.Count());
            Assert.Equal(0, context.CatalogSources.Count());
        }

        using (var context = database.CreateContext())
        {
            context.Database.Migrate();
        }
    }

    [Fact]
    public void Migrate_GamePlatforms_HasModelAlignedUniqueIndex()
    {
        using var database = new TempDatabase(ensureCreated: false);
        using var context = database.CreateContext();
        context.Database.Migrate();

        var indexNames = context.Database
            .SqlQuery<string>(
                $"SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND tbl_name = 'GamePlatforms' AND sql IS NOT NULL")
            .ToList();

        Assert.Contains("IX_GamePlatforms_GameId_PlatformName", indexNames);
        Assert.DoesNotContain("IX_GamePlatforms_PlatformName_PlatformGameId", indexNames);
    }
}




