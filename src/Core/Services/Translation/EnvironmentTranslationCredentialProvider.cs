namespace GameLanguageExplorer.Core.Services.Translation;

/// <summary>
/// API anahtarını ortam değişkeninden okuyan kimlik bilgisi sağlayıcısı.
///
/// <para>Varsayılan ortam değişkeni <see cref="DefaultEnvironmentVariableName"/> değeridir.
/// Değişken adı yapıcıda değiştirilebilir; ad yalnızca ASCII harf, rakam ve alt çizgi
/// içerebilir ve harf veya alt çizgiyle başlamalıdır. Anahtar değeri hiçbir hata mesajına,
/// günlüğe veya açıklamaya yazılmaz.</para>
/// </summary>
public sealed class EnvironmentTranslationCredentialProvider : ITranslationCredentialProvider
{
    /// <summary>Anahtarın okunacağı varsayılan ortam değişkeni.</summary>
    public const string DefaultEnvironmentVariableName = "GLE_LLM_API_KEY";

    private const int MaximumEnvironmentVariableNameLength = 128;

    /// <summary>
    /// Sunucu tarafından kullanılan, sır içermeyen ortam değişkeni adı.
    /// </summary>
    public string EnvironmentVariableName { get; }

    /// <summary>
    /// Kimlik bilgisi sağlayıcısını oluşturur. <paramref name="environmentVariableName"/>
    /// <c>null</c> ise <see cref="DefaultEnvironmentVariableName"/> kullanılır.
    /// </summary>
    public EnvironmentTranslationCredentialProvider(string? environmentVariableName = null)
    {
        var name = environmentVariableName ?? DefaultEnvironmentVariableName;
        if (!IsValidEnvironmentVariableName(name))
        {
            throw new ArgumentException(
                "Ortam değişkeni adı 1-128 karakter olmalı; yalnızca ASCII harf, rakam ve " +
                "alt çizgi içermeli ve harf veya alt çizgiyle başlamalıdır.",
                nameof(environmentVariableName));
        }

        EnvironmentVariableName = name;
    }

    /// <inheritdoc />
    public string? GetApiKey(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException(
                "Sağlayıcı kimliği boş olamaz.",
                nameof(providerId));
        }

        var value = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsValidEnvironmentVariableName(string name)
    {
        if (name.Length is 0 or > MaximumEnvironmentVariableNameLength)
        {
            return false;
        }

        if (!IsAsciiLetter(name[0]) && name[0] != '_')
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!IsAsciiLetter(character) && !char.IsAsciiDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
