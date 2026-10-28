namespace GameLanguageExplorer.Core.Services.Translation;

using GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>Sağlayıcı çağrısının başarısızlık türü. HTTP durum kodlarından bağımsızdır.</summary>
public enum TranslationProviderFailureKind
{
    Configuration = 0,
    Authentication = 1,
    RateLimited = 2,
    Timeout = 3,
    Network = 4,
    InvalidRequest = 5,
    InvalidResponse = 6,
    ContentRejected = 7,
    Cancelled = 8,
    Unknown = 9
}

/// <summary>
/// Sağlayıcı hatasının taşınabilir açıklaması. Mesajda API anahtarı, kaynak metin veya
/// başka bir gizli değer bulunmamalıdır; sağlayıcı uygulaması bu kuraldan sorumludur.
/// </summary>
public sealed record TranslationProviderError(
    TranslationProviderFailureKind Kind,
    string Message,
    bool IsTransient = false,
    TimeSpan? RetryAfter = null,
    string? ProviderCode = null);

/// <summary>Sağlayıcının taşıma veya yanıt sözleşmesini ihlal ettiğini bildiren istisna.</summary>
public sealed class TranslationProviderException : Exception
{
    public TranslationProviderException(TranslationProviderError error)
        : base((error ?? throw new ArgumentNullException(nameof(error))).Message)
    {
        Error = error;
    }

    public TranslationProviderException(TranslationProviderError error, Exception? innerException)
        : base((error ?? throw new ArgumentNullException(nameof(error))).Message, innerException)
    {
        Error = error;
    }

    public TranslationProviderError Error { get; }
}

/// <summary>Sağlayıcının yeteneklerini ve sınırlarını tanımlar.</summary>
public sealed record TranslationProviderDescriptor
{
    public required string ProviderId { get; init; }

    public required string DisplayName { get; init; }

    public bool SupportsBatching { get; init; }

    public bool SupportsJsonResponseFormat { get; init; } = true;

    /// <summary>Sağlayıcının tek istekte kabul ettiği en fazla kayıt sayısı (bilinmiyorsa <c>null</c>).</summary>
    public int? MaximumBatchSize { get; init; }

    public string? Description { get; init; }
}

/// <summary>
/// Sağlayıcıdan bağımsız üretim ayarları. Sırlar (API anahtarı, erişim belirteci) burada
/// veya <see cref="TranslationProviderProfile"/> içinde tutulmaz.
/// </summary>
public sealed record TranslationGenerationOptions
{
    /// <summary>Örnekleme sıcaklığı (0–2). Varsayılan 0,2.</summary>
    public double Temperature { get; init; } = 0.2;

    /// <summary>Yanıt için üst sınır (varsayılan 2048).</summary>
    public int MaxOutputTokens { get; init; } = 2048;

    /// <summary>Tek sağlayıcı çağrısı için zaman aşımı (varsayılan 60 saniye).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Sağlayıcı destekliyorsa tekrarlanabilir örnekleme tohumu.</summary>
    public int? Seed { get; init; }

    public void Validate()
    {
        if (!double.IsFinite(Temperature) || Temperature < 0 || Temperature > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Temperature), Temperature, "Sıcaklık 0 ile 2 arasında sonlu bir değer olmalıdır.");
        }

        if (MaxOutputTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxOutputTokens), MaxOutputTokens, "En fazla çıktı belirteci pozitif olmalıdır.");
        }

        if (Timeout <= TimeSpan.Zero || Timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Timeout), Timeout, "Zaman aşımı pozitif ve sonlu olmalıdır.");
        }

        if (Seed is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Seed), Seed, "Tohum negatif olamaz.");
        }
    }
}

/// <summary>
/// Sağlayıcı bağlantı profilidir. <b>API anahtarı veya erişim belirteci içermez.</b>
/// Kimlik bilgisi, sağlayıcı uygulaması tarafından çalışma zamanında ortam değişkeninden
/// veya işletim sistemi kimlik bilgisi deposundan okunmalı; profile, URL'ye veya günlüğe
/// yazılmamalıdır.
/// </summary>
public sealed record TranslationProviderProfile
{
    public required string ProviderId { get; init; }

    public required Uri Endpoint { get; init; }

    public required string Model { get; init; }

    public string? DisplayName { get; init; }

    public TranslationGenerationOptions GenerationOptions { get; init; } = new();

    private static readonly HashSet<string> SensitiveQueryParameterNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "api_key",
            "apikey",
            "api-key",
            "key",
            "token",
            "access_token",
            "authorization"
        };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProviderId))
        {
            throw new ArgumentException("Sağlayıcı kimliği boş olamaz.", nameof(ProviderId));
        }

        ArgumentNullException.ThrowIfNull(Endpoint);
        if (!Endpoint.IsAbsoluteUri
            || (!string.Equals(Endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Uç nokta mutlak bir HTTP veya HTTPS adresi olmalıdır.",
                nameof(Endpoint));
        }

        if (!string.IsNullOrEmpty(Endpoint.UserInfo))
        {
            throw new ArgumentException(
                "Uç nokta kullanıcı bilgisi içinde kimlik bilgisi bulunmamalıdır. " +
                "Kimlik bilgisi çalışma zamanında ayrıca sağlanmalıdır.",
                nameof(Endpoint));
        }

        if (HasSensitiveQueryParameter(Endpoint))
        {
            throw new ArgumentException(
                "Uç nokta sorgu dizesinde API anahtarı veya erişim belirteci bulunmamalıdır. " +
                "Kimlik bilgisi çalışma zamanında ayrıca sağlanmalıdır.",
                nameof(Endpoint));
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new ArgumentException("Model adı boş olamaz.", nameof(Model));
        }

        GenerationOptions.Validate();
    }

    private static bool HasSensitiveQueryParameter(Uri endpoint)
    {
        var query = endpoint.Query;
        if (string.IsNullOrEmpty(query))
        {
            return false;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = separator >= 0 ? pair[..separator] : pair;
            name = Uri.UnescapeDataString(name.Replace('+', ' '));

            if (SensitiveQueryParameterNames.Contains(name))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Sağlayıcıya gönderilecek tek bir çeviri kaydı. <see cref="SourceText"/> çağrıdan önce
/// <see cref="PlaceholderProtector"/> ile maskelenmiş olmalıdır; ham yer tutucu içeren metin
/// <see cref="TranslationPromptBuilder"/> tarafından reddedilir.
/// </summary>
public sealed record TranslationUnit
{
    /// <summary>Çalıştırma içinde benzersiz, kararlı kayıt kimliği.</summary>
    public required string UnitId { get; init; }

    /// <summary>Kaynak dosyadaki yapısal anahtar (ör. JSON Pointer, XPath, PO msgid konumu).</summary>
    public required string EntryKey { get; init; }

    public required string SourceText { get; init; }

    public string? Context { get; init; }

    public string? Reference { get; init; }

    public string? FormatKey { get; init; }

    public int? PluralIndex { get; init; }

    public int? PluralCount { get; init; }

    public IReadOnlyList<string> Flags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Mevcut önizleme kaydını değiştirmeden çeviri birimine dönüştürür. <paramref name="unitId"/>
    /// çağıran tarafından üretilir; bu yöntem kalıcılık veya dosya erişimi yapmaz.
    /// </summary>
    public static TranslationUnit FromPreviewEntry(LocalizationTextEntry entry, string unitId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(unitId);

        return new TranslationUnit
        {
            UnitId = unitId,
            EntryKey = entry.Key,
            SourceText = entry.SourceText,
            Context = entry.Context,
            Reference = entry.Reference,
            FormatKey = entry.FormatKey,
            PluralIndex = entry.PluralIndex,
            PluralCount = entry.PluralCount,
            Flags = entry.Flags
        };
    }
}

/// <summary>Kaynak terimden hedef terime sözlük kaydı. Sır içermez.</summary>
public sealed record GlossaryEntry
{
    public required string SourceTerm { get; init; }

    public required string TargetTerm { get; init; }

    public string? Note { get; init; }

    public bool CaseSensitive { get; init; }
}

/// <summary>İstem oluşturmak için gereken, sağlayıcıdan bağımsız girdi.</summary>
public sealed record TranslationPromptRequest
{
    public required string SourceLanguage { get; init; }

    public required string TargetLanguage { get; init; }

    public required IReadOnlyList<TranslationUnit> Units { get; init; }

    public IReadOnlyList<GlossaryEntry> Glossary { get; init; } = Array.Empty<GlossaryEntry>();
}

/// <summary>
/// Üretilen sistem/kullanıcı istemi ve makinenin doğrulayabileceği JSON yanıt sözleşmesi.
/// Kaynak metin yalnızca <see cref="PayloadJson"/> içinde veri olarak yer alır.
/// </summary>
public sealed record TranslationPrompt(
    string PromptVersion,
    string SystemPrompt,
    string UserPrompt,
    string ResponseFormat,
    string PayloadJson,
    IReadOnlyList<string> UnitIds);

/// <summary>Sağlayıcıya gönderilecek istek. Kimlik bilgisi içermez.</summary>
public sealed record TranslationProviderRequest
{
    public required string RequestId { get; init; }

    public required string SourceLanguage { get; init; }

    public required string TargetLanguage { get; init; }

    public required IReadOnlyList<TranslationUnit> Units { get; init; }

    public IReadOnlyList<GlossaryEntry> Glossary { get; init; } = Array.Empty<GlossaryEntry>();

    public required TranslationPrompt Prompt { get; init; }

    public TranslationGenerationOptions GenerationOptions { get; init; } = new();
}

/// <summary>Modelin tek bir kayıt için döndürdüğü çeviri adayı.</summary>
public sealed record TranslationCandidate(
    string UnitId,
    string? Text)
{
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Sağlayıcı yanıtı. Ham HTTP gövdesi veya sırlar taşınmaz; yalnızca doğrulanabilir
/// adaylar ve kullanım bilgileri bulunur.
/// </summary>
public sealed record TranslationProviderResponse
{
    public required IReadOnlyList<TranslationCandidate> Candidates { get; init; }

    public string? Model { get; init; }

    public string? ProviderRequestId { get; init; }

    public int? InputTokens { get; init; }

    public int? OutputTokens { get; init; }

    public TimeSpan Duration { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Sağlayıcıdan bağımsız çeviri taşıma sözleşmesi. Uygulamalar API anahtarını yapıcıda
/// çalışma zamanında almalı; profile, isteğe veya günlüğe yazmamalıdır. Taşıma ve yanıt
/// hataları <see cref="TranslationProviderException"/> ile, iptal ise
/// <see cref="OperationCanceledException"/> ile bildirilir.
/// </summary>
public interface ITranslationProvider
{
    TranslationProviderDescriptor Descriptor { get; }

    Task<TranslationProviderResponse> TranslateAsync(
        TranslationProviderRequest request,
        CancellationToken cancellationToken = default);
}
