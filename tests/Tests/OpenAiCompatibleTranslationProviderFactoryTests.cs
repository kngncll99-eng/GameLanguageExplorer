namespace GameLanguageExplorer.Tests;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GameLanguageExplorer.Core.Services.Translation;

public class OpenAiCompatibleTranslationProviderFactoryTests
{
    [Fact]
    public void EnvironmentCredentialProvider_UsesDefaultVariableName()
    {
        var provider = new EnvironmentTranslationCredentialProvider();

        Assert.Equal("GLE_LLM_API_KEY", provider.EnvironmentVariableName);
        Assert.Equal(
            EnvironmentTranslationCredentialProvider.DefaultEnvironmentVariableName,
            provider.EnvironmentVariableName);
    }

    [Fact]
    public void EnvironmentCredentialProvider_ReadsConfiguredVariableAndTrimsValue()
    {
        var variableName = CreateEnvironmentVariableName();
        try
        {
            Environment.SetEnvironmentVariable(variableName, "  test-secret-key \r\n");
            var provider = new EnvironmentTranslationCredentialProvider(variableName);

            Assert.Equal("test-secret-key", provider.GetApiKey("test-provider"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EnvironmentCredentialProvider_MissingValue_ReturnsNull(string value)
    {
        var variableName = CreateEnvironmentVariableName();
        try
        {
            Environment.SetEnvironmentVariable(variableName, value);
            var provider = new EnvironmentTranslationCredentialProvider(variableName);

            Assert.Null(provider.GetApiKey("test-provider"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("1GLE_KEY")]
    [InlineData("GLE KEY")]
    [InlineData("GLE-KEY")]
    [InlineData("GLE_KEY=value")]
    [InlineData("GLE_KEY\nINJECTED")]
    public void EnvironmentCredentialProvider_InvalidVariableName_IsRejected(string variableName)
    {
        Assert.Throws<ArgumentException>(
            () => new EnvironmentTranslationCredentialProvider(variableName));
    }

    [Fact]
    public void EnvironmentCredentialProvider_TooLongVariableName_IsRejected()
    {
        var variableName = new string('A', 129);

        Assert.Throws<ArgumentException>(
            () => new EnvironmentTranslationCredentialProvider(variableName));
    }

    [Fact]
    public void EnvironmentCredentialProvider_EmptyProviderId_IsRejected()
    {
        var provider = new EnvironmentTranslationCredentialProvider();

        Assert.Throws<ArgumentException>(() => provider.GetApiKey(" "));
    }

    [Fact]
    public void CreateProfile_ValidValues_ReturnsValidatedProfile()
    {
        var options = new TranslationGenerationOptions
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var profile = OpenAiCompatibleTranslationProviderFactory.CreateProfile(
            " test-provider ",
            " https://api.example.com/v1 ",
            " test-model ",
            " Test Provider ",
            options);

        Assert.Equal("test-provider", profile.ProviderId);
        Assert.Equal("https://api.example.com/v1", profile.Endpoint.ToString());
        Assert.Equal("test-model", profile.Model);
        Assert.Equal("Test Provider", profile.DisplayName);
        Assert.Same(options, profile.GenerationOptions);
    }

    [Fact]
    public void CreateProfile_InvalidEndpoint_IsRejectedWithoutEchoingValue()
    {
        const string secret = "must-not-appear";
        var exception = Assert.Throws<ArgumentException>(
            () => OpenAiCompatibleTranslationProviderFactory.CreateProfile(
                "test-provider",
                "not a valid endpoint " + secret,
                "test-model"));

        Assert.DoesNotContain(secret, exception.ToString());
    }

    [Fact]
    public void CreateProfile_EndpointWithUserInfo_IsRejectedWithoutLeakingSecret()
    {
        const string secret = "super-secret";
        var exception = Assert.Throws<ArgumentException>(
            () => OpenAiCompatibleTranslationProviderFactory.CreateProfile(
                "test-provider",
                $"https://user:{secret}@api.example.com/v1",
                "test-model"));

        Assert.DoesNotContain(secret, exception.ToString());
    }

    [Fact]
    public void CreateProfile_EndpointWithSensitiveQuery_IsRejectedWithoutLeakingSecret()
    {
        const string secret = "super-secret";
        var exception = Assert.Throws<ArgumentException>(
            () => OpenAiCompatibleTranslationProviderFactory.CreateProfile(
                "test-provider",
                $"https://api.example.com/v1?api_key={secret}",
                "test-model"));

        Assert.DoesNotContain(secret, exception.ToString());
    }

    [Fact]
    public async Task Create_SendsResolvedBearerKeyOnlyInAuthorizationHeader()
    {
        const string apiKey = "factory-secret-key";
        using var handler = new StubHttpMessageHandler
        {
            ResponseBody = SuccessfulResponse(TranslationContent(("u1", "Merhaba")))
        };
        using var client = new HttpClient(handler);
        var profile = CreateProfile();
        var credentials = new StubCredentialProvider(apiKey);
        var provider = OpenAiCompatibleTranslationProviderFactory.Create(
            client,
            profile,
            credentials);

        var response = await provider.TranslateAsync(CreateRequest("u1"));

        Assert.Equal("Bearer factory-secret-key", handler.LastAuthorization);
        Assert.DoesNotContain(apiKey, handler.LastRequestUri!.ToString());
        Assert.DoesNotContain(apiKey, handler.LastRequestBody!);
        Assert.DoesNotContain(apiKey, profile.ToString());
        Assert.Equal("test-provider", credentials.LastProviderId);
        Assert.Equal("Merhaba", Assert.Single(response.Candidates).Text);
    }

    [Fact]
    public async Task Create_WithoutKeyForLoopback_AllowsUnauthenticatedUse()
    {
        using var handler = new StubHttpMessageHandler
        {
            ResponseBody = SuccessfulResponse(TranslationContent(("u1", "Merhaba")))
        };
        using var client = new HttpClient(handler);
        var profile = CreateProfile("http://127.0.0.1:1234/v1");
        var provider = OpenAiCompatibleTranslationProviderFactory.Create(
            client,
            profile,
            new StubCredentialProvider(null));

        await provider.TranslateAsync(CreateRequest("u1"));

        Assert.Null(handler.LastAuthorization);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Create_RemoteWithoutKey_AuthenticationFailureIsClearAndSafe()
    {
        const string secretInBody = "must-not-appear";
        using var handler = new StubHttpMessageHandler
        {
            StatusCode = HttpStatusCode.Unauthorized,
            ResponseBody = $$"""{"error":"{{secretInBody}}"}"""
        };
        using var client = new HttpClient(handler);
        var provider = OpenAiCompatibleTranslationProviderFactory.Create(
            client,
            CreateProfile(),
            new StubCredentialProvider(null));

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(CreateRequest("u1")));

        Assert.Equal(TranslationProviderFailureKind.Authentication, exception.Error.Kind);
        Assert.Equal("401", exception.Error.ProviderCode);
        Assert.Contains("API anahtarı sağlanmamış", exception.Message);
        Assert.DoesNotContain(secretInBody, exception.ToString());
    }

    [Fact]
    public void Create_RejectsHttpClientTimeoutShorterThanProfileTimeout()
    {
        const string apiKey = "factory-secret-key";
        using var handler = new StubHttpMessageHandler();
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(1)
        };
        var profile = CreateProfile(timeout: TimeSpan.FromSeconds(5));

        var exception = Assert.Throws<ArgumentException>(
            () => OpenAiCompatibleTranslationProviderFactory.Create(
                client,
                profile,
                new StubCredentialProvider(apiKey)));

        Assert.Contains("HttpClient.Timeout", exception.Message);
        Assert.DoesNotContain(apiKey, exception.ToString());
        Assert.Equal(TimeSpan.FromSeconds(1), client.Timeout);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Create_AllowsInfiniteHttpClientTimeout()
    {
        using var handler = new StubHttpMessageHandler
        {
            ResponseBody = SuccessfulResponse(TranslationContent(("u1", "Merhaba")))
        };
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var provider = OpenAiCompatibleTranslationProviderFactory.Create(
            client,
            CreateProfile(timeout: TimeSpan.FromSeconds(5)),
            new StubCredentialProvider("factory-secret-key"));

        await provider.TranslateAsync(CreateRequest("u1"));

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Translate_RequestTimeoutLongerThanHttpClientTimeout_IsConfigurationError()
    {
        using var handler = new StubHttpMessageHandler
        {
            ResponseBody = SuccessfulResponse(TranslationContent(("u1", "Merhaba")))
        };
        using var client = new HttpClient(handler);
        var provider = OpenAiCompatibleTranslationProviderFactory.Create(
            client,
            CreateProfile(timeout: TimeSpan.FromSeconds(5)),
            new StubCredentialProvider("factory-secret-key"));
        var request = CreateRequest("u1") with
        {
            GenerationOptions = new TranslationGenerationOptions
            {
                Timeout = TimeSpan.FromSeconds(200)
            }
        };

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(
            () => provider.TranslateAsync(request));

        Assert.Equal(TranslationProviderFailureKind.Configuration, exception.Error.Kind);
        Assert.Contains("HttpClient.Timeout", exception.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Translate_RequestTimeout_IsReportedAsTransientTimeout()
    {
        using var handler = new StubHttpMessageHandler
        {
            WaitUntilCancelled = true
        };
        using var client = new HttpClient(handler);
        var provider = OpenAiCompatibleTranslationProviderFactory.Create(
            client,
            CreateProfile(timeout: TimeSpan.FromSeconds(5)),
            new StubCredentialProvider("factory-secret-key"));
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
    public async Task Translate_CallerCancellation_StaysOperationCanceledException()
    {
        using var handler = new StubHttpMessageHandler
        {
            WaitUntilCancelled = true
        };
        using var client = new HttpClient(handler);
        var provider = OpenAiCompatibleTranslationProviderFactory.Create(
            client,
            CreateProfile(timeout: TimeSpan.FromSeconds(5)),
            new StubCredentialProvider("factory-secret-key"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.TranslateAsync(CreateRequest("u1"), cancellation.Token));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public void Create_CredentialProviderFailure_DoesNotLeakItsMessage()
    {
        const string secret = "factory-secret-key";
        using var handler = new StubHttpMessageHandler();
        using var client = new HttpClient(handler);

        var exception = Assert.Throws<TranslationProviderException>(
            () => OpenAiCompatibleTranslationProviderFactory.Create(
                client,
                CreateProfile(),
                new ThrowingCredentialProvider(
                    new InvalidOperationException("credential failed with " + secret))));

        Assert.Equal(TranslationProviderFailureKind.Configuration, exception.Error.Kind);
        Assert.DoesNotContain(secret, exception.ToString());
        Assert.Null(exception.InnerException);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public void Create_InvalidApiKeyCharacter_DoesNotLeakKey()
    {
        const string apiKey = "factory-secret-key\r\nInjected: value";
        using var handler = new StubHttpMessageHandler();
        using var client = new HttpClient(handler);

        var exception = Assert.Throws<ArgumentException>(
            () => OpenAiCompatibleTranslationProviderFactory.Create(
                client,
                CreateProfile(),
                new StubCredentialProvider(apiKey)));

        Assert.DoesNotContain("factory-secret-key", exception.ToString());
    }

    [Fact]
    public async Task CreateFromEnvironment_ResolvesConfiguredVariable()
    {
        const string apiKey = "environment-secret-key";
        var variableName = CreateEnvironmentVariableName();
        try
        {
            Environment.SetEnvironmentVariable(variableName, apiKey);
            using var handler = new StubHttpMessageHandler
            {
                ResponseBody = SuccessfulResponse(TranslationContent(("u1", "Merhaba")))
            };
            using var client = new HttpClient(handler);
            var provider = OpenAiCompatibleTranslationProviderFactory.CreateFromEnvironment(
                client,
                CreateProfile(),
                variableName);

            await provider.TranslateAsync(CreateRequest("u1"));

            Assert.Equal("Bearer environment-secret-key", handler.LastAuthorization);
            Assert.DoesNotContain(apiKey, handler.LastRequestBody!);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    private static TranslationProviderProfile CreateProfile(
        string endpoint = "https://api.example.com",
        TimeSpan? timeout = null) =>
        OpenAiCompatibleTranslationProviderFactory.CreateProfile(
            "test-provider",
            endpoint,
            "test-model",
            "Test Provider",
            new TranslationGenerationOptions
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(5)
            });

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

    private static string CreateEnvironmentVariableName() =>
        "GLE_TEST_" + Guid.NewGuid().ToString("N");

    private sealed class StubCredentialProvider(string? apiKey) : ITranslationCredentialProvider
    {
        public string? LastProviderId { get; private set; }

        public string? GetApiKey(string providerId)
        {
            LastProviderId = providerId;
            return apiKey;
        }
    }

    private sealed class ThrowingCredentialProvider(Exception exception) : ITranslationCredentialProvider
    {
        public string? GetApiKey(string providerId) => throw exception;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public string ResponseBody { get; set; } = "{}";

        public bool WaitUntilCancelled { get; set; }

        public int RequestCount { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public string? LastRequestBody { get; private set; }

        public string? LastAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (WaitUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
