namespace GameLanguageExplorer.Core.Services.Translation;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

/// <summary>Yerel/uzak OpenAI uyumlu sunucunun erişilebilirliğini ve model listesini bildirir.</summary>
public sealed record TranslationConnectionProbeResult(
    bool Success,
    bool ModelAvailable,
    IReadOnlyList<string> Models,
    string? ErrorMessage,
    TimeSpan Duration)
{
    public static TranslationConnectionProbeResult Failure(string message, TimeSpan duration) =>
        new(false, false, Array.Empty<string>(), message, duration);
}

/// <summary>
/// <c>/v1/models</c> uç noktasını salt okunur biçimde yoklar. API anahtarı yalnızca
/// Authorization başlığında taşınır; URL'ye, sonuca veya hata mesajına yazılmaz.
/// </summary>
public static class OpenAiCompatibleConnectionProbe
{
    public static async Task<TranslationConnectionProbeResult> ProbeAsync(
        HttpClient httpClient,
        TranslationProviderProfile profile,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(profile);

        var stopwatch = Stopwatch.StartNew();
        var endpoint = ResolveModelsEndpoint(profile.Endpoint);

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (apiKey.Any(char.IsControl))
            {
                return TranslationConnectionProbeResult.Failure(
                    "API anahtarı kontrol karakteri içeriyor.", stopwatch.Elapsed);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(profile.GenerationOptions.Timeout);

        try
        {
            using var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                        "Sunucu kimlik doğrulamayı reddetti.",
                    HttpStatusCode.NotFound => "Model listesi uç noktası bulunamadı (/v1/models).",
                    _ => $"Sunucu HTTP {(int)response.StatusCode} döndürdü."
                };
                return TranslationConnectionProbeResult.Failure(message, stopwatch.Elapsed);
            }

            var body = await ReadBodyWithLimitAsync(response.Content, timeoutSource.Token)
                .ConfigureAwait(false);
            var models = ParseModels(body);

            if (models is null)
            {
                return TranslationConnectionProbeResult.Failure(
                    "Sunucu geçerli bir model listesi döndürmedi.", stopwatch.Elapsed);
            }

            var available = models.Any(model =>
                string.Equals(model, profile.Model, StringComparison.OrdinalIgnoreCase));
            return new TranslationConnectionProbeResult(
                true,
                available,
                models,
                null,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return TranslationConnectionProbeResult.Failure(
                "Bağlantı sınaması zaman aşımına uğradı.", stopwatch.Elapsed);
        }
        catch (HttpRequestException)
        {
            stopwatch.Stop();
            return TranslationConnectionProbeResult.Failure(
                "Çeviri sunucusuna ulaşılamadı.", stopwatch.Elapsed);
        }
        catch (Exception)
        {
            stopwatch.Stop();
            return TranslationConnectionProbeResult.Failure(
                "Bağlantı sınaması tamamlanamadı.", stopwatch.Elapsed);
        }
    }

    public static Uri ResolveModelsEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.IsAbsoluteUri
            || (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Uç nokta mutlak bir HTTP/HTTPS adresi olmalıdır.", nameof(endpoint));
        }

        if (!string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException("Uç nokta fragment içeremez.", nameof(endpoint));
        }

        var builder = new UriBuilder(endpoint);
        var path = builder.Path.TrimEnd('/');

        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^"/chat/completions".Length];
        }

        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = path + "/models";
        }
        else if (path.Length == 0)
        {
            builder.Path = "/v1/models";
        }
        else
        {
            throw new ArgumentException(
                "Belirsiz uç nokta yolu. Kök adres, '/v1' ile biten taban adres veya " +
                "'/chat/completions' ile biten tam adres kullanın.",
                nameof(endpoint));
        }

        return builder.Uri;
    }

    private static async Task<string> ReadBodyWithLimitAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength
            && contentLength > TranslationResponseParser.MaximumResponseBytes)
        {
            throw new InvalidOperationException("Model listesi boyut sınırını aşıyor.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > TranslationResponseParser.MaximumResponseBytes)
            {
                throw new InvalidOperationException("Model listesi boyut sınırını aşıyor.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
    }

    private static IReadOnlyList<string>? ParseModels(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var models = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(id.GetString()))
                {
                    models.Add(id.GetString()!);
                }
            }

            return models;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
