namespace GameLanguageExplorer.Tests;

using System.Net;
using System.Net.Http;
using System.Text;
using GameLanguageExplorer.Core.Services.Translation;

public class OpenAiCompatibleConnectionProbeTests
{
    private static TranslationProviderProfile Profile() => new()
    {
        ProviderId = "local",
        Endpoint = new Uri("http://127.0.0.1:1234/v1"),
        Model = "test-model",
        GenerationOptions = new TranslationGenerationOptions
        {
            Timeout = TimeSpan.FromSeconds(5)
        }
    };

    [Theory]
    [InlineData("http://127.0.0.1:1234", "http://127.0.0.1:1234/v1/models")]
    [InlineData("http://127.0.0.1:1234/v1", "http://127.0.0.1:1234/v1/models")]
    [InlineData("http://127.0.0.1:1234/v1/chat/completions", "http://127.0.0.1:1234/v1/models")]
    public void ResolveModelsEndpoint_HandlesSupportedEndpointForms(string endpoint, string expected)
    {
        var result = OpenAiCompatibleConnectionProbe.ResolveModelsEndpoint(new Uri(endpoint));

        Assert.Equal(expected, result.ToString());
    }

    [Fact]
    public async Task Probe_ReportsModelAvailability()
    {
        using var handler = new StubHandler(
            HttpStatusCode.OK,
            """{"data":[{"id":"other-model"},{"id":"test-model"}]}""");
        using var client = new HttpClient(handler);

        var result = await OpenAiCompatibleConnectionProbe.ProbeAsync(client, Profile());

        Assert.True(result.Success);
        Assert.True(result.ModelAvailable);
        Assert.Contains("test-model", result.Models);
    }

    [Fact]
    public async Task Probe_ReportsMissingModelWithoutFailingServerConnection()
    {
        using var handler = new StubHandler(HttpStatusCode.OK, """{"data":[{"id":"other"}]}""");
        using var client = new HttpClient(handler);

        var result = await OpenAiCompatibleConnectionProbe.ProbeAsync(client, Profile());

        Assert.True(result.Success);
        Assert.False(result.ModelAvailable);
    }

    [Fact]
    public async Task Probe_DoesNotLeakApiKeyOnAuthenticationFailure()
    {
        const string key = "super-secret-key";
        using var handler = new StubHandler(HttpStatusCode.Unauthorized, "{}");
        using var client = new HttpClient(handler);

        var result = await OpenAiCompatibleConnectionProbe.ProbeAsync(client, Profile(), key);

        Assert.False(result.Success);
        Assert.DoesNotContain(key, result.ErrorMessage ?? string.Empty);
        Assert.Equal("Bearer", handler.LastAuthorizationScheme);
    }

    [Fact]
    public async Task Probe_RejectsMalformedModelList()
    {
        using var handler = new StubHandler(HttpStatusCode.OK, "{not-json");
        using var client = new HttpClient(handler);

        var result = await OpenAiCompatibleConnectionProbe.ProbeAsync(client, Profile());

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public string? LastAuthorizationScheme { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
