namespace GameLanguageExplorer.Core.Services.Translation;

/// <summary>
/// API anahtarını güvenli bir kaynaktan (ortam değişkeni, işletim sistemi kimlik bilgisi deposu)
/// çözer. Dönen değer yalnızca sağlayıcı yapıcısına aktarılmalı; profile, URL'ye, isteme, HTTP
/// gövdesine, günlüğe veya hata mesajına yazılmamalıdır.
/// </summary>
public interface ITranslationCredentialProvider
{
    /// <summary>
    /// Sağlayıcı için API anahtarını döndürür. Anahtar tanımlı değilse <c>null</c> döner.
    /// Boş veya yalnızca boşluk içeren değerler anahtarsız kabul edilir.
    /// </summary>
    /// <param name="providerId">Kimlik bilgisinin çözüleceği sağlayıcı kimliği.</param>
    string? GetApiKey(string providerId);
}
