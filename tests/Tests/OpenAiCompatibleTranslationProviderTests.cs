namespace GameLanguageExplorer.Tests;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GameLanguageExplorer.Core.Services.Translation;

public class OpenAiCompatibleTranslationProviderTests
{
    [Fact]
    public async Task RootEndpoint_AppendsV1ChatCompletions()
    {
        using var handler = CreateHandler(SuccessfulResponse(TranslationContent(("u1", "Merhaba"))));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");
        var request = CreateRequest("u1");

        await provider.TranslateAsync(request);

        Assert.Equal(
            "https://api.example.com/v1/chat/completions",
            handler.LastRequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
    }

    [Fact]
    public async Task V1BaseEndpoint_AppendsChatCompletionsOnly()
    {
        using var handler = CreateHandler(SuccessfulResponse(TranslationContent(("u1", "Merhaba"))));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com/v1");
        var request = CreateRequest("u1");

        await provider.TranslateAsync(request);

        Assert.Equal(
            "https://api.example.com/v1/chat/completions",
            handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task FullEndpoint_IsUsedUnchanged()
    {
        using var handler = CreateHandler(SuccessfulResponse(TranslationContent(("u1", "Merhaba"))));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com/custom/chat/completions");
        var request = CreateRequest("u1");

        await provider.TranslateAsync(request);

        Assert.Equal(
            "https://api.example.com/custom/chat/completions",
            handler.LastRequestUri!.ToString());
    }

    [Fact]
    public void AmbiguousEndpointPath_IsRejected()
    {
        using var handler = CreateHandler(SuccessfulResponse(TranslationContent(("u1", "Merhaba"))));
        using var client = new HttpClient(handler);

        var exception = Assert.Throws<ArgumentException>(
            () => CreateProvider(client, "https://api.example.com/custom"));

        Assert.Contains("Belirsiz", exception.Message);
    }

    [Fact]
    public void RemoteHttpEndpoint_IsRejected()
    {
        using var handler = CreateHandler(SuccessfulResponse(TranslationContent(("u1", "Merhaba"))));
        using var client = new HttpClient(handler);

        var exception = Assert.Throws<ArgumentException>(
            () => CreateProvider(client, "http://api.example.com"));

        Assert.Contains("HTTPS", exception.Message);
    }

    [Fact]
    public void LoopbackHttpEndpoint_IsAccepted()
    {
        using var handler = CreateHandler(SuccessfulResponse(TranslationContent(("u1", "Merhaba"))));
        using var client = new HttpClient(handler);

        var provider = CreateProvider(client, "http://localhost:1234/v1");

        Assert.Equal("test-provider", provider.Descriptor.ProviderId);
    }

    [Fact]
    public void ApiKeyWithControlCharacters_IsRejected()
    {
        using var handler = CreateHandler(SuccessfulResponse(TranslationContent(("u1", "Merhaba"))));
        using var client = new HttpClient(handler);

        Assert.Throws<ArgumentException>(
            () => new OpenAiCompatibleTranslationProvider(
                client,
                CreateProfile("https://api.example.com"),
                "secret\r\nInjected: value"));
    }

    [Fact]
    public async Task RequestBody_MapsSystemAndUserPromptsAndOptions()
    {
        using var handler = CreateHandler(SuccessfulResponse(TranslationContent(("u1", "Merhaba"))));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");
        var request = CreateRequest("u1");

        await provider.TranslateAsync(request);

        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        var root = document.RootElement;

        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(0.3, root.GetProperty("temperature").GetDouble());
        Assert.Equal(512, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(7, root.GetProperty("seed").GetInt32());

        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal(request.Prompt.SystemPrompt, messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal(request.Prompt.UserPrompt, messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task SuccessfulResponse_ReturnsValidatedCandidatesAndUsage()
    {
        var content = TranslationContent(("u1", "Merhaba"), ("u2", "Dünya"));
        using var handler = CreateHandler(SuccessfulResponse(content));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");
        var request = CreateRequest("u1", "u2");

        var response = await provider.TranslateAsync(request);

        Assert.Collection(
            response.Candidates,
            first =>
            {
                Assert.Equal("u1", first.UnitId);
                Assert.Equal("Merhaba", first.Text);
            },
            second =>
            {
                Assert.Equal("u2", second.UnitId);
                Assert.Equal("Dünya", second.Text);
            });
        Assert.Equal("test-model", response.Model);
        Assert.Equal("chatcmpl-1", response.ProviderRequestId);
        Assert.Equal(10, response.InputTokens);
        Assert.Equal(5, response.OutputTokens);
        Assert.True(response.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task AuthorizationHeader_IsBearerAndSecretIsNotInUrlOrBody()
    {
        using var handler = CreateHandler(SuccessfulResponse(TranslationContent(("u1", "Merhaba"))));
        using var client = new HttpClient(handler);
        const string apiKey = "test-secret-key";
        var provider = CreateProvider(client, "https://api.example.com", apiKey);
        var request = CreateRequest("u1");

        await provider.TranslateAsync(request);

        Assert.Equal("Bearer test-secret-key", handler.LastAuthorization);
        Assert.DoesNotContain(apiKey, handler.LastRequestUri!.ToString());
        Assert.DoesNotContain(apiKey, handler.LastRequestBody!);
    }

    [Fact]
    public async Task MissingApiKey_SendsNoAuthorizationHeader()
    {
        using var handler = CreateHandler(SuccessfulResponse(TranslationContent(("u1", "Merhaba"))));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "http://127.0.0.1:1234");
        var request = CreateRequest("u1");

        await provider.TranslateAsync(request);

        Assert.Null(handler.LastAuthorization);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{}]}""")]
    [InlineData("""{"choices":[{"message":{}}]}""")]
    [InlineData("""{"choices":[{"message":{"content":null}}]}""")]
    [InlineData("""{"choices":[{"message":{"content":42}}]}""")]
    public async Task MissingOrInvalidChoiceContent_IsInvalidResponse(string responseBody)
    {
        using var handler = CreateHandler(responseBody);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(CreateRequest("u1")));

        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, exception.Error.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, TranslationProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, TranslationProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.BadRequest, TranslationProviderFailureKind.InvalidRequest)]
    [InlineData(HttpStatusCode.NotFound, TranslationProviderFailureKind.Configuration)]
    [InlineData(HttpStatusCode.RequestTimeout, TranslationProviderFailureKind.Timeout)]
    [InlineData(HttpStatusCode.InternalServerError, TranslationProviderFailureKind.Network)]
    public async Task HttpStatusCodes_AreClassified(
        HttpStatusCode statusCode,
        TranslationProviderFailureKind expectedKind)
    {
        using var handler = CreateHandler("""{"error":"raw body"}""");
        handler.StatusCode = statusCode;
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(CreateRequest("u1")));

        Assert.Equal(expectedKind, exception.Error.Kind);
        Assert.DoesNotContain("raw body", exception.Message);
    }

    [Fact]
    public async Task RateLimit_IsTransientAndCarriesRetryAfter()
    {
        using var handler = CreateHandler("""{"error":"raw body"}""");
        handler.StatusCode = HttpStatusCode.TooManyRequests;
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(CreateRequest("u1")));

        Assert.Equal(TranslationProviderFailureKind.RateLimited, exception.Error.Kind);
        Assert.True(exception.Error.IsTransient);
        Assert.Equal(TimeSpan.FromSeconds(12), exception.Error.RetryAfter);
        Assert.DoesNotContain("raw body", exception.Message);
    }

    [Fact]
    public async Task ResponseWithUnexpectedUnitIds_IsRejected()
    {
        using var handler = CreateHandler(SuccessfulResponse(TranslationContent(("u2", "Merhaba"))));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(CreateRequest("u1")));

        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, exception.Error.Kind);
        Assert.DoesNotContain("Merhaba", exception.Message);
    }

    private static OpenAiCompatibleTranslationProvider CreateProvider(
        HttpClient client,
        string endpoint,
        string? apiKey = null) =>
        new(client, CreateProfile(endpoint), apiKey);

    private static TranslationProviderProfile CreateProfile(string endpoint) => new()
    {
        ProviderId = "test-provider",
        DisplayName = "Test Provider",
        Endpoint = new Uri(endpoint),
        Model = "test-model"
    };

    private static TranslationProviderRequest CreateRequest(params string[] unitIds)
    {
        var units = unitIds
            .Select((unitId, index) => new TranslationUnit
            {
                UnitId = unitId,
                EntryKey = $"key-{index}",
                SourceText = $"Source {index}"
            })
            .ToArray();

        var prompt = TranslationPromptBuilder.Build(new TranslationPromptRequest
        {
            SourceLanguage = "en",
            TargetLanguage = "tr",
            Units = units
        });

        return new TranslationProviderRequest
        {
            RequestId = "request-1",
            SourceLanguage = "en",
            TargetLanguage = "tr",
            Units = units,
            Prompt = prompt,
            GenerationOptions = new TranslationGenerationOptions
            {
                Temperature = 0.3,
                MaxOutputTokens = 512,
                Seed = 7,
                Timeout = TimeSpan.FromSeconds(5)
            }
        };
    }

    private static string TranslationContent(params (string UnitId, string Text)[] items) =>
        JsonSerializer.Serialize(new
        {
            translations = items.Select(item => new
            {
                unitId = item.UnitId,
                text = item.Text
            })
        });

    private static string SuccessfulResponse(string translationContent) =>
        JsonSerializer.Serialize(new
        {
            id = "chatcmpl-1",
            model = "test-model",
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        role = "assistant",
                        content = translationContent
                    }
                }
            },
            usage = new
            {
                prompt_tokens = 10,
                completion_tokens = 5
            }
        });

    private static StubHttpMessageHandler CreateHandler(string responseBody) => new()
    {
        ResponseBody = responseBody
    };

    [Fact]
    public async Task ExtraItemFields_AreIgnoredByTolerantFallback()
    {
        const string content =
            """{"translations":[{"unitId":"u1","text":"Merhaba","note":"model notu"}]}""";
        using var handler = CreateHandler(SuccessfulResponse(content));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");

        var response = await provider.TranslateAsync(CreateRequest("u1"));

        Assert.Equal("Merhaba", Assert.Single(response.Candidates).Text);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var handler = CreateHandler("{}");
        handler.WaitUntilCancelled = true;
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.TranslateAsync(CreateRequest("u1"), cancellation.Token));
    }

    [Fact]
    public async Task ProviderTimeout_IsReportedAsTransientTimeout()
    {
        using var handler = CreateHandler("{}");
        handler.WaitUntilCancelled = true;
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");
        var request = CreateRequest("u1") with
        {
            GenerationOptions = new TranslationGenerationOptions
            {
                Timeout = TimeSpan.FromMilliseconds(50)
            }
        };

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(request));

        Assert.Equal(TranslationProviderFailureKind.Timeout, exception.Error.Kind);
        Assert.True(exception.Error.IsTransient);
    }

    [Fact]
    public async Task ResponseOverSizeLimit_IsRejectedWithoutIncludingBody()
    {
        var body = new string('x', TranslationResponseParser.MaximumResponseBytes + 1);
        using var handler = CreateHandler(body);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(CreateRequest("u1")));

        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, exception.Error.Kind);
        Assert.Contains("boyut sınırı", exception.Message);
        Assert.DoesNotContain(body, exception.Message);
    }

    [Fact]
    public async Task MarkdownWrappedTranslationJson_IsRejected()
    {
        var content = "```json\n"
            + TranslationContent(("u1", "Merhaba"))
            + "\n```";
        using var handler = CreateHandler(SuccessfulResponse(content));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(CreateRequest("u1")));

        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, exception.Error.Kind);
        Assert.DoesNotContain("Merhaba", exception.Message);
    }

    [Fact]
    public async Task PromptUnitIdsNotMatchingRequest_IsRejectedBeforeHttpCall()
    {
        using var handler = CreateHandler(SuccessfulResponse(TranslationContent(("u1", "Merhaba"))));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com");
        var request = CreateRequest("u1") with
        {
            Prompt = CreateRequest("u2").Prompt
        };

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(request));

        Assert.Equal(TranslationProviderFailureKind.InvalidRequest, exception.Error.Kind);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task ApiKey_DoesNotLeakIntoErrorMessages()
    {
        const string apiKey = "super-secret-key";
        using var handler = CreateHandler("""{"error":"super-secret-key"}""");
        handler.StatusCode = HttpStatusCode.Unauthorized;
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com", apiKey);

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(CreateRequest("u1")));

        Assert.DoesNotContain(apiKey, exception.Message);
        Assert.DoesNotContain(apiKey, exception.ToString());
    }

    [Fact]
    public async Task MalformedResponseBody_DoesNotLeakBodyOrApiKey()
    {
        const string apiKey = "super-secret-key";
        using var handler = CreateHandler("""{"error":"super-secret-key"}""");
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com", apiKey);

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(CreateRequest("u1")));

        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, exception.Error.Kind);
        Assert.DoesNotContain(apiKey, exception.ToString());
        Assert.DoesNotContain("error", exception.Message);
    }

    [Fact]
    public async Task NetworkFailure_DoesNotLeakInnerExceptionDetails()
    {
        const string apiKey = "super-secret-key";
        using var handler = CreateHandler("{}");
        handler.ExceptionToThrow = new HttpRequestException("failed with " + apiKey);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "https://api.example.com", apiKey);

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(CreateRequest("u1")));

        Assert.Equal(TranslationProviderFailureKind.Network, exception.Error.Kind);
        Assert.True(exception.Error.IsTransient);
        Assert.DoesNotContain(apiKey, exception.ToString());
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public string ResponseBody { get; set; } = "{}";

        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public bool WaitUntilCancelled { get; set; }

        public Exception? ExceptionToThrow { get; set; }

        public Uri? LastRequestUri { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public string? LastRequestBody { get; private set; }

        public string? LastAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            if (WaitUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };

            if (StatusCode == HttpStatusCode.TooManyRequests)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
            }

            return response;
        }
    }
}
