namespace GameLanguageExplorer.Core.Services.Translation;

using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// OpenAI uyumlu <c>/v1/chat/completions</c> uç noktasıyla konuşan sağlayıcı.
///
/// <para><b>Endpoint kuralı:</b> <see cref="TranslationProviderProfile.Endpoint"/> kök adres
/// (<c>https://host</c>) ise <c>/v1/chat/completions</c>, <c>/v1</c> ile biten taban adres ise
/// <c>/chat/completions</c> eklenir. Zaten <c>/chat/completions</c> ile biten adres tam uç nokta
/// olarak kullanılır. Diğer yollar belirsiz olduğu için sessizce birleştirilmez; yapılandırma
/// hatası olarak reddedilir.</para>
///
/// <para><b>Kimlik bilgisi:</b> API anahtarı profile, URL'ye, isteme veya hata mesajına yazılmaz.
/// Yalnızca yapıcıya açıkça verilen değer <c>Authorization: Bearer</c> başlığında taşınır. Bu
/// sınıf ortam değişkeni veya kimlik bilgisi deposu okumaz; anahtar
/// <see cref="ITranslationCredentialProvider"/> üzerinden
/// <see cref="OpenAiCompatibleTranslationProviderFactory"/> ile çözülmelidir.</para>
///
/// <para><b>Zaman aşımı:</b> Tek yetkili istek zaman aşımı
/// <see cref="TranslationGenerationOptions.Timeout"/> değeridir. <see cref="HttpClient.Timeout"/>
/// sonsuz değilse istek zaman aşımından kısa olamaz; aksi halde istek yapılandırma hatasıyla
/// reddedilir. Çağıranın iptal belirteci ayrı tutulur ve
/// <see cref="OperationCanceledException"/> olarak korunur.</para>
///
/// <para>Retry, backoff, rate limiting ve batching bu sınıfın kapsamında değildir.</para>
/// </summary>
public sealed class OpenAiCompatibleTranslationProvider : ITranslationProvider
{
    /// <summary>OpenAI uyumlu sohbet tamamlama yolu.</summary>
    public const string ChatCompletionsPath = "/v1/chat/completions";

    private const string ChatCompletionsSuffix = "/chat/completions";

    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private readonly HttpClient _httpClient;
    private readonly TranslationProviderProfile _profile;
    private readonly Uri _endpoint;
    private readonly string? _apiKey;

    /// <summary>
    /// Sağlayıcıyı oluşturur. <paramref name="apiKey"/> <c>null</c> veya boşsa Authorization
    /// başlığı gönderilmez (yerel LM Studio/Ollama gibi anahtarsız sunucular için).
    /// </summary>
    public OpenAiCompatibleTranslationProvider(
        HttpClient httpClient,
        TranslationProviderProfile profile,
        string? apiKey = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _profile.Validate();

        _endpoint = ResolveChatCompletionsEndpoint(_profile.Endpoint);

        if (string.Equals(_profile.Endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !_profile.Endpoint.IsLoopback)
        {
            throw new ArgumentException(
                "Uzak HTTP uç noktaları desteklenmez; HTTPS kullanın. " +
                "HTTP yalnızca yerel (loopback) LM Studio/Ollama uç noktaları için kabul edilir.",
                nameof(profile));
        }

        if (apiKey is not null)
        {
            foreach (var character in apiKey)
            {
                if (character is < (char)0x21 or > (char)0x7E)
                {
                    throw new ArgumentException(
                        "API anahtarı yalnızca yazdırılabilir ASCII karakterler içerebilir.",
                        nameof(apiKey));
                }
            }
        }

        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;

        Descriptor = new TranslationProviderDescriptor
        {
            ProviderId = _profile.ProviderId,
            DisplayName = _profile.DisplayName ?? "OpenAI uyumlu sağlayıcı",
            SupportsBatching = false,
            SupportsJsonResponseFormat = true,
            MaximumBatchSize = null,
            Description = "OpenAI uyumlu sohbet tamamlama API'si."
        };
    }

    public TranslationProviderDescriptor Descriptor { get; }

    public async Task<TranslationProviderResponse> TranslateAsync(
        TranslationProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var options = request.GenerationOptions;
        try
        {
            options.Validate();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new TranslationProviderException(
                new TranslationProviderError(
                    TranslationProviderFailureKind.Configuration,
                    "Üretim seçenekleri geçersiz."),
                exception);
        }

        if (_httpClient.Timeout != Timeout.InfiniteTimeSpan
            && _httpClient.Timeout < options.Timeout)
        {
            throw new TranslationProviderException(new TranslationProviderError(
                TranslationProviderFailureKind.Configuration,
                "İstek zaman aşımı HttpClient.Timeout değerinden uzun olamaz. " +
                "HttpClient.Timeout değerini Timeout.InfiniteTimeSpan yapın veya en az istek " +
                "zaman aşımı kadar seçin."));
        }

        var unitIds = request.Units.Select(unit => unit.UnitId).ToArray();
        var requestJson = JsonSerializer.Serialize(
            new
            {
                model = _profile.Model,
                messages = new object[]
                {
                    new { role = "system", content = request.Prompt.SystemPrompt },
                    new { role = "user", content = request.Prompt.UserPrompt }
                },
                temperature = options.Temperature,
                max_tokens = options.MaxOutputTokens,
                seed = options.Seed,
                stream = false
            },
            RequestJsonOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (_apiKey is not null)
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        // İstek başına zaman aşımı yalnızca bu bağlı kaynakla uygulanır. Çağıranın iptal belirteci
        // ayrı tutulur; aşağıdaki catch blokları kullanıcı iptalini OperationCanceledException
        // olarak korur, yalnızca zaman aşımını TranslationProviderException'a dönüştürür.
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.Timeout);

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw CreateTimeoutException();
        }
        catch (HttpRequestException)
        {
            throw CreateNetworkException();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpStatusException(response);
            }

            string body;
            try
            {
                body = await ReadBodyWithLimitAsync(response.Content, timeoutSource.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw CreateTimeoutException();
            }
            catch (HttpRequestException)
            {
                throw CreateNetworkException();
            }
            catch (IOException)
            {
                throw CreateNetworkException();
            }

            var envelope = ParseEnvelope(body);
            var parsed = TranslationResponseParser.Parse(envelope.Content, unitIds);

            // Bazı yerel modeller unitId/text dışında zararsız alanlar (ör. note) ekleyebilir.
            // Yalnızca bu durumda ikinci, toleranslı ayrıştırma denenir; bilinmeyen, eksik
            // veya yinelenen unitId yine reddedilir. Varsayılan parser katı kalır.
            if (!parsed.IsSuccess
                && parsed.Error?.Contains("beklenmeyen bir alan", StringComparison.Ordinal) == true)
            {
                parsed = TranslationResponseParser.Parse(
                    envelope.Content,
                    unitIds,
                    allowUnknownItemFields: true);
            }

            if (!parsed.IsSuccess)
            {
                throw new TranslationProviderException(
                    new TranslationProviderError(
                        TranslationProviderFailureKind.InvalidResponse,
                        "Model yanıtı beklenen çeviri JSON sözleşmesine uymuyor: "
                            + parsed.Error));
            }

            return new TranslationProviderResponse
            {
                Candidates = parsed.Candidates,
                Model = envelope.Model,
                ProviderRequestId = envelope.ProviderRequestId,
                InputTokens = envelope.InputTokens,
                OutputTokens = envelope.OutputTokens,
                Duration = stopwatch.Elapsed
            };
        }
    }

    private static void ValidateRequest(TranslationProviderRequest request)
    {
        if (request.GenerationOptions is null)
        {
            throw new TranslationProviderException(
                new TranslationProviderError(
                    TranslationProviderFailureKind.InvalidRequest,
                    "Üretim seçenekleri eksik."));
        }

        if (request.Prompt is null
            || string.IsNullOrWhiteSpace(request.Prompt.SystemPrompt)
            || string.IsNullOrWhiteSpace(request.Prompt.UserPrompt))
        {
            throw new TranslationProviderException(
                new TranslationProviderError(
                    TranslationProviderFailureKind.InvalidRequest,
                    "Sistem ve kullanıcı istemi zorunludur."));
        }

        if (request.Units is null || request.Units.Count == 0)
        {
            throw new TranslationProviderException(
                new TranslationProviderError(
                    TranslationProviderFailureKind.InvalidRequest,
                    "En az bir çeviri birimi gerekir."));
        }

        var unitIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in request.Units)
        {
            if (unit is null || string.IsNullOrWhiteSpace(unit.UnitId))
            {
                throw new TranslationProviderException(
                    new TranslationProviderError(
                        TranslationProviderFailureKind.InvalidRequest,
                        "Çeviri birimlerinin unitId değerleri boş olamaz."));
            }

            if (!unitIds.Add(unit.UnitId))
            {
                throw new TranslationProviderException(
                    new TranslationProviderError(
                        TranslationProviderFailureKind.InvalidRequest,
                        "Çeviri birimlerinde yinelenen unitId var."));
            }
        }

        if (request.Prompt.UnitIds is null
            || !request.Prompt.UnitIds.SequenceEqual(
                request.Units.Select(unit => unit.UnitId),
                StringComparer.Ordinal))
        {
            throw new TranslationProviderException(
                new TranslationProviderError(
                    TranslationProviderFailureKind.InvalidRequest,
                    "İstemdeki unitId listesi istekle eşleşmiyor."));
        }
    }

    private static async Task<string> ReadBodyWithLimitAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength
            && contentLength > TranslationResponseParser.MaximumResponseBytes)
        {
            throw CreateTooLargeException();
        }

        await using var stream = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > TranslationResponseParser.MaximumResponseBytes)
            {
                throw CreateTooLargeException();
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
    }

    private static TranslationProviderException CreateTooLargeException() =>
        new(new TranslationProviderError(
            TranslationProviderFailureKind.InvalidResponse,
            $"Sağlayıcı yanıtı {TranslationResponseParser.MaximumResponseBytes} baytlık " +
            "boyut sınırını aşıyor."));

    private static TranslationProviderException CreateTimeoutException() =>
        new(new TranslationProviderError(
            TranslationProviderFailureKind.Timeout,
            "Çeviri isteği zaman aşımına uğradı.",
            IsTransient: true));

    private static TranslationProviderException CreateNetworkException() =>
        new(new TranslationProviderError(
            TranslationProviderFailureKind.Network,
            "Çeviri sunucusuna ulaşılamadı.",
            IsTransient: true));

    private TranslationProviderException CreateHttpStatusException(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        var kind = statusCode switch
        {
            401 or 403 => TranslationProviderFailureKind.Authentication,
            429 => TranslationProviderFailureKind.RateLimited,
            408 => TranslationProviderFailureKind.Timeout,
            404 => TranslationProviderFailureKind.Configuration,
            >= 400 and < 500 => TranslationProviderFailureKind.InvalidRequest,
            >= 500 => TranslationProviderFailureKind.Network,
            >= 300 and < 400 => TranslationProviderFailureKind.InvalidResponse,
            _ => TranslationProviderFailureKind.Unknown
        };

        var isTransient = kind is
            TranslationProviderFailureKind.RateLimited
            or TranslationProviderFailureKind.Timeout
            or TranslationProviderFailureKind.Network;

        var message = _apiKey is null && (statusCode is 401 or 403)
            ? "Sağlayıcı kimlik doğrulama istedi ancak API anahtarı sağlanmamış. " +
              "Anahtar gerekiyorsa güvenli bir ortam değişkeninden yapılandırın."
            : $"Sağlayıcı HTTP {statusCode.ToString(CultureInfo.InvariantCulture)} durumu döndürdü.";

        return new TranslationProviderException(new TranslationProviderError(
            kind,
            message,
            isTransient,
            ReadRetryAfter(response),
            statusCode.ToString(CultureInfo.InvariantCulture)));
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is DateTimeOffset date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : null;
        }

        return null;
    }

    private static ChatCompletionEnvelope ParseEnvelope(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw InvalidEnvelope("Sağlayıcı yanıtı geçerli JSON değil.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw InvalidEnvelope("Sağlayıcı yanıtının kökü bir nesne olmalıdır.");
            }

            if (!TryGetUniqueProperty(root, "choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                throw InvalidEnvelope("Sağlayıcı yanıtında geçerli 'choices' dizisi yok.");
            }

            var firstChoice = choices[0];
            if (firstChoice.ValueKind != JsonValueKind.Object
                || !TryGetUniqueProperty(firstChoice, "message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !TryGetUniqueProperty(message, "content", out var content)
                || content.ValueKind != JsonValueKind.String)
            {
                throw InvalidEnvelope(
                    "Sağlayıcı yanıtında choices[0].message.content metni yok.");
            }

            var translationJson = content.GetString();
            if (string.IsNullOrWhiteSpace(translationJson))
            {
                throw InvalidEnvelope("Sağlayıcı yanıtındaki content boş.");
            }

            var (inputTokens, outputTokens) = ReadUsage(root);
            return new ChatCompletionEnvelope(
                translationJson,
                ReadOptionalString(root, "model"),
                ReadOptionalString(root, "id"),
                inputTokens,
                outputTokens);
        }
    }

    private static TranslationProviderException InvalidEnvelope(string message) =>
        new(new TranslationProviderError(
            TranslationProviderFailureKind.InvalidResponse,
            message));

    private static bool TryGetUniqueProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        value = default;
        var found = false;

        foreach (var property in element.EnumerateObject())
        {
            if (!property.NameEquals(propertyName))
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            value = property.Value;
            found = true;
        }

        return found;
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName) =>
        TryGetUniqueProperty(element, propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static (int? InputTokens, int? OutputTokens) ReadUsage(JsonElement root)
    {
        if (!TryGetUniqueProperty(root, "usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        return (ReadOptionalInt32(usage, "prompt_tokens"),
            ReadOptionalInt32(usage, "completion_tokens"));
    }

    private static int? ReadOptionalInt32(JsonElement element, string propertyName) =>
        TryGetUniqueProperty(element, propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var result)
                ? result
                : null;

    private static Uri ResolveChatCompletionsEndpoint(Uri endpoint)
    {
        if (!string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException(
                "Uç nokta fragment içeremez.",
                nameof(endpoint));
        }

        var builder = new UriBuilder(endpoint);
        var path = builder.Path.TrimEnd('/');

        if (path.EndsWith(ChatCompletionsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = path;
        }
        else if (path.Length == 0)
        {
            builder.Path = ChatCompletionsPath;
        }
        else if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = path + ChatCompletionsSuffix;
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

    private sealed record ChatCompletionEnvelope(
        string Content,
        string? Model,
        string? ProviderRequestId,
        int? InputTokens,
        int? OutputTokens);
}
