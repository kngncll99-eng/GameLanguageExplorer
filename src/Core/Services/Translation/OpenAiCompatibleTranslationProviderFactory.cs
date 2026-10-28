namespace GameLanguageExplorer.Core.Services.Translation;

/// <summary>
/// OpenAI uyumlu sağlayıcı için profil ve örnek oluşturma katmanı.
///
/// <para>Sır tutmaz. API anahtarı yalnızca <see cref="ITranslationCredentialProvider"/>
/// üzerinden çözülür ve doğrudan sağlayıcı yapıcısına aktarılır; profile, URL'ye, isteme veya
/// hata mesajına yazılmaz.</para>
///
/// <para><b>Zaman aşımı:</b> Tek yetkili istek zaman aşımı
/// <see cref="TranslationGenerationOptions.Timeout"/> değeridir. Sağlayıcı her istek için bağlı
/// bir <see cref="CancellationTokenSource"/> kullanır. <see cref="HttpClient.Timeout"/> sonsuz
/// değilse istek zaman aşımından kısa olamaz; aksi halde hangi sınırın önce uygulandığı belirsiz
/// olacağı için yapılandırma reddedilir.</para>
/// </summary>
public static class OpenAiCompatibleTranslationProviderFactory
{
    /// <summary>
    /// Metin girdilerinden doğrulanmış bir <see cref="TranslationProviderProfile"/> oluşturur.
    /// Hata mesajları uç noktanın ham değerini veya sorgu dizesindeki sırları yansıtmaz.
    /// </summary>
    public static TranslationProviderProfile CreateProfile(
        string providerId,
        string endpoint,
        string model,
        string? displayName = null,
        TranslationGenerationOptions? generationOptions = null)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Sağlayıcı kimliği boş olamaz.", nameof(providerId));
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Uç nokta boş olamaz.", nameof(endpoint));
        }

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var endpointUri))
        {
            throw new ArgumentException(
                "Uç nokta geçerli bir mutlak adres olmalıdır.",
                nameof(endpoint));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model adı boş olamaz.", nameof(model));
        }

        var profile = new TranslationProviderProfile
        {
            ProviderId = providerId.Trim(),
            Endpoint = endpointUri,
            Model = model.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            GenerationOptions = generationOptions ?? new TranslationGenerationOptions()
        };

        profile.Validate();
        return profile;
    }

    /// <summary>
    /// Güvenli kaynaktan API anahtarını çözer, zaman aşımı ilişkisini doğrular ve sağlayıcıyı
    /// oluşturur. Anahtar yoksa (<c>null</c> veya boş) sağlayıcı Authorization başlığı göndermez;
    /// bu, yerel LM Studio/Ollama gibi kimlik doğrulamasız sunucular için gereklidir.
    /// </summary>
    public static OpenAiCompatibleTranslationProvider Create(
        HttpClient httpClient,
        TranslationProviderProfile profile,
        ITranslationCredentialProvider credentialProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(credentialProvider);

        profile.Validate();
        EnsureHttpClientTimeoutIsCompatible(
            httpClient,
            profile.GenerationOptions.Timeout);

        var apiKey = ResolveApiKey(credentialProvider, profile.ProviderId);
        return new OpenAiCompatibleTranslationProvider(httpClient, profile, apiKey);
    }

    /// <summary>
    /// Varsayılan olarak <see cref="EnvironmentTranslationCredentialProvider.DefaultEnvironmentVariableName"/>
    /// değişkeninden API anahtarını okuyarak sağlayıcıyı oluşturur.
    /// </summary>
    public static OpenAiCompatibleTranslationProvider CreateFromEnvironment(
        HttpClient httpClient,
        TranslationProviderProfile profile,
        string? environmentVariableName = null) =>
        Create(
            httpClient,
            profile,
            new EnvironmentTranslationCredentialProvider(environmentVariableName));

    private static void EnsureHttpClientTimeoutIsCompatible(
        HttpClient httpClient,
        TimeSpan requestTimeout)
    {
        if (httpClient.Timeout != Timeout.InfiniteTimeSpan
            && httpClient.Timeout < requestTimeout)
        {
            throw new ArgumentException(
                "HttpClient.Timeout, TranslationGenerationOptions.Timeout değerinden kısa " +
                "olamaz. İstek başına zaman aşımını tek yetkili sınır yapmak için " +
                "HttpClient.Timeout değerini Timeout.InfiniteTimeSpan olarak ayarlayın veya " +
                "en az istek zaman aşımı kadar seçin.",
                nameof(httpClient));
        }
    }

    private static string? ResolveApiKey(
        ITranslationCredentialProvider credentialProvider,
        string providerId)
    {
        string? apiKey;
        try
        {
            apiKey = credentialProvider.GetApiKey(providerId);
        }
        catch (Exception)
        {
            // Kimlik bilgisi kaynağının hata metni sır içerebilir; dışarı taşınmaz.
            throw new TranslationProviderException(new TranslationProviderError(
                TranslationProviderFailureKind.Configuration,
                "API anahtarı güvenli kaynaktan çözülemedi."));
        }

        return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }
}
