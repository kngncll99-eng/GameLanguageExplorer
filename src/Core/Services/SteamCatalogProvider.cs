namespace GameLanguageExplorer.Core.Services;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

/// <summary>
/// Steam katalog sağlayıcısı.
///
/// <para><b>Önemli:</b> Valve, <c>ISteamApps/GetAppList</c> uç noktasını kaldırdı (HTTP 404,
/// "Method 'GetAppList' not found in interface 'ISteamApps'"). Tam katalog artık yalnızca
/// API anahtarı gerektiren <c>IStoreService/GetAppList/v1</c> üzerinden alınabilir.</para>
///
/// <para>Anahtar gerektirmeyen yol <see cref="SearchGamesAsync"/>'tır:
/// <c>store.steampowered.com/api/storesearch</c> ile isimle arama + 
/// <c>store.steampowered.com/api/appdetails</c> ile zenginleştirme.</para>
/// </summary>
public class SteamCatalogProvider : IGameCatalogProvider, IGameSearchProvider
{
    public const string ProviderNameValue = "Steam";
    public const string BaseUrl = "https://api.steampowered.com";
    public const string ApiKeyEnvironmentVariable = "STEAM_API_KEY";

    private const string StoreAppListUrl = BaseUrl + "/IStoreService/GetAppList/v1/";
    private const string StoreSearchUrl = "https://store.steampowered.com/api/storesearch/";
    private const string StoreAppDetailsUrl = "https://store.steampowered.com/api/appdetails";
    private const int CatalogPageSize = 50000;
    private const int MaxCatalogPages = 40;

    private readonly HttpClient _httpClient;
    private readonly ILogger<SteamCatalogProvider>? _logger;
    private readonly Func<string?> _apiKeyAccessor;

    public string ProviderName => ProviderNameValue;
    public bool IsApiAvailable => true;

    /// <summary>
    /// Tek kurucu: <c>AddHttpClient&lt;SteamCatalogProvider&gt;</c> typed-client fabrikası
    /// <see cref="HttpClient"/> alan bir kurucu bekler. Ek bağımlılıklar opsiyoneldir;
    /// böylece DI bu kurucuyu çözebilir (testler <paramref name="apiKeyAccessor"/> verir).
    /// </summary>
    public SteamCatalogProvider(
        HttpClient httpClient,
        ILogger<SteamCatalogProvider>? logger = null,
        Func<string?>? apiKeyAccessor = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        _apiKeyAccessor = apiKeyAccessor ?? (() => Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable));

        // BaseAddress/User-Agent DI tarafından da ayarlanabilir; iki kez eklenmesini engelle.
        _httpClient.BaseAddress ??= new Uri(BaseUrl);

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any(ua => ua.Product?.Name == "GameLanguageExplorer"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GameLanguageExplorer/1.0");
        }
    }

    /// <summary>
    /// Tam katalog. API anahtarı yoksa anlaşılır bir <see cref="InvalidOperationException"/> fırlatır
    /// (sessizce boş liste döndürmez).
    /// </summary>
    public async Task<IEnumerable<Game>> FetchGamesAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = _apiKeyAccessor();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Steam tam katalog senkronizasyonu için Steam Web API anahtarı gerekiyor. " +
                "Valve, anahtarsız 'ISteamApps/GetAppList' uç noktasını kaldırdı (HTTP 404). " +
                $"'{ApiKeyEnvironmentVariable}' ortam değişkenine bir anahtar tanımlayın " +
                "(https://steamcommunity.com/dev/apikey) veya anahtar gerektirmeyen " +
                "\"Steam'den Ekle\" ile isimle arama yapın.");
        }

        var now = DateTime.UtcNow;
        var games = new List<Game>();
        long lastAppId = 0;

        for (var page = 0; page < MaxCatalogPages; page++)
        {
            var url = $"{StoreAppListUrl}?key={Uri.EscapeDataString(apiKey)}&include_games=true&max_results={CatalogPageSize}";
            if (lastAppId > 0)
            {
                url += $"&last_appid={lastAppId}";
            }

            using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("response", out var body))
            {
                break;
            }

            var pageCount = 0;

            if (body.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Array)
            {
                foreach (var app in apps.EnumerateArray())
                {
                    var appId = app.TryGetProperty("appid", out var idElement) && idElement.TryGetInt64(out var parsedId)
                        ? parsedId
                        : 0;

                    var name = app.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                        ? nameElement.GetString()
                        : null;

                    if (appId <= 0 || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    games.Add(new Game
                    {
                        Name = name!.Trim(),
                        ExternalId = appId.ToString(CultureInfo.InvariantCulture),
                        CreatedAt = now,
                        UpdatedAt = now
                    });

                    pageCount++;
                }
            }

            var haveMore = body.TryGetProperty("have_more", out var moreElement) && moreElement.ValueKind == JsonValueKind.True;
            var nextCursor = body.TryGetProperty("last_appid", out var cursorElement) && cursorElement.TryGetInt64(out var cursor)
                ? cursor
                : 0;

            _logger?.LogInformation("Steam katalog sayfası {Page}: {Count} oyun (have_more={HaveMore})", page + 1, pageCount, haveMore);

            if (!haveMore || pageCount == 0 || nextCursor <= lastAppId)
            {
                break;
            }

            lastAppId = nextCursor;
        }

        _logger?.LogInformation("Steam kataloğundan toplam {Count} oyun alındı.", games.Count);
        return games;
    }

    /// <summary>
    /// Anahtar gerektirmeyen isimle arama: <c>storesearch</c> + (isteğe bağlı) <c>appdetails</c>.
    /// </summary>
    public async Task<IReadOnlyList<Game>> SearchGamesAsync(
        string term,
        int maxResults = 10,
        bool enrichDetails = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Array.Empty<Game>();
        }

        var url = $"{StoreSearchUrl}?term={Uri.EscapeDataString(term.Trim())}&l=turkish&cc=TR";
        using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);

        var results = new List<Game>();

        if (document.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                if (!string.Equals(type, "app", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var appId = item.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var parsedId)
                    ? parsedId
                    : 0;

                var name = item.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;

                if (appId <= 0 || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var now = DateTime.UtcNow;

                results.Add(new Game
                {
                    Name = name!.Trim(),
                    ExternalId = appId.ToString(CultureInfo.InvariantCulture),
                    CoverImageUrl = item.TryGetProperty("tiny_image", out var imageElement) && imageElement.ValueKind == JsonValueKind.String
                        ? imageElement.GetString()
                        : null,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                if (results.Count >= maxResults)
                {
                    break;
                }
            }
        }

        _logger?.LogInformation("Steam araması '{Term}': {Count} sonuç", term, results.Count);

        if (!enrichDetails)
        {
            return results;
        }

        foreach (var game in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnrichAsync(game, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>Tek bir oyunun ayrıntılarını AppID ile getirir.</summary>
    public async Task<Game?> GetGameDetailsAsync(string platformGameId, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(platformGameId, out var appId) || appId <= 0)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var game = new Game
        {
            Name = string.Empty,
            ExternalId = appId.ToString(CultureInfo.InvariantCulture),
            CreatedAt = now,
            UpdatedAt = now
        };

        var enriched = await EnrichAsync(game, cancellationToken).ConfigureAwait(false);
        return enriched ? game : null;
    }

    public async Task SyncCatalogAsync(CancellationToken cancellationToken = default)
    {
        // Gerçek kalıcılık CatalogSyncService tarafından yapılır; burada yalnızca veri çekilir.
        await FetchGamesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>appdetails</c> uç noktasından ad/geliştirici/yayıncı/açıklama/çıkış tarihi/kapak görseli çeker.
    /// Zenginleştirme başarısız olursa yalnızca uyarı loglanır, arama sonucu bozulmaz.
    /// </summary>
    private async Task<bool> EnrichAsync(Game game, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(game.ExternalId))
        {
            return false;
        }

        var url = $"{StoreAppDetailsUrl}?appids={Uri.EscapeDataString(game.ExternalId)}&l=turkish&cc=TR";

        try
        {
            using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty(game.ExternalId, out var entry))
            {
                return false;
            }

            if (!entry.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            {
                return false;
            }

            if (!entry.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var name = ReadString(data, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                game.Name = name!.Trim();
            }

            game.Developer = ReadFirstArrayElement(data, "developers") ?? game.Developer;
            game.Publisher = ReadFirstArrayElement(data, "publishers") ?? game.Publisher;
            game.Description = ReadString(data, "short_description") ?? game.Description;
            game.CoverImageUrl = ReadString(data, "header_image") ?? game.CoverImageUrl;

            var releaseDate = ReadString(data, "release_date");
            game.ReleaseDate = ParseSteamDate(releaseDate) ?? game.ReleaseDate;

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Steam appdetails zenginleştirmesi başarısız: {AppId}", game.ExternalId);
            return false;
        }
    }

    /// <summary>
    /// Steam tarihleri istek diline göre gelir (ör. "10 Eki 2007" veya "Oct 10, 2007").
    /// Hem Türkçe hem de sabit kültürle denenir; ayrıştırılamazsa null döner.
    /// </summary>
    private static DateTime? ParseSteamDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        var cultures = new[] { CultureInfo.GetCultureInfo("tr-TR"), CultureInfo.InvariantCulture };

        foreach (var culture in cultures)
        {
            if (DateTime.TryParse(text, culture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("date", out var nested))
        {
            return nested.ValueKind == JsonValueKind.String ? nested.GetString() : null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? ReadFirstArrayElement(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// JSON yanıtı indirir. HTTP ve JSON hataları ayırt edilebilir mesajlarla fırlatılır.
    /// </summary>
    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var snippet = body.Length > 300 ? body[..300] : body;

            _logger?.LogError("Steam HTTP hatası: {Status} {Url} — {Body}", (int)response.StatusCode, url, snippet);

            throw new HttpRequestException(
                $"Steam isteği başarısız: {(int)response.StatusCode} {response.ReasonPhrase}. " +
                $"Adres: {url}. Yanıt: {snippet}",
                inner: null,
                statusCode: response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Steam yanıtı geçerli JSON değil. Adres: {url}. Ayrıntı: {ex.Message}", ex);
        }
    }
}




