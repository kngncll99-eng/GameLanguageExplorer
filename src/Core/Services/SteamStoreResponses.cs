namespace GameLanguageExplorer.Core.Services;

using System.Text.Json.Serialization;

/// <summary>
/// <c>store.steampowered.com/api/storesearch</c> yanıtı. API anahtarı gerektirmez.
/// </summary>
public class SteamStoreSearchResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("items")]
    public List<SteamStoreSearchItem> Items { get; set; } = new();
}

public class SteamStoreSearchItem
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("tiny_image")]
    public string? TinyImage { get; set; }
}

/// <summary>
/// <c>api.steampowered.com/IStoreService/GetAppList/v1</c> yanıtı (API anahtarı gerekir).
/// </summary>
public class SteamStoreAppListResponse
{
    [JsonPropertyName("response")]
    public SteamStoreAppListBody Response { get; set; } = new();
}

public class SteamStoreAppListBody
{
    [JsonPropertyName("apps")]
    public List<SteamStoreAppListItem> Apps { get; set; } = new();

    [JsonPropertyName("have_more")]
    public bool HaveMore { get; set; }

    [JsonPropertyName("last_appid")]
    public long LastAppId { get; set; }
}

public class SteamStoreAppListItem
{
    [JsonPropertyName("appid")]
    public long AppId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
