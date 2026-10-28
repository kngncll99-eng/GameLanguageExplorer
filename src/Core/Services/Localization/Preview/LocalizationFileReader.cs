namespace GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>Dosya metninin okunma sonucu.</summary>
public sealed record TextFileContent(
    bool Success,
    string? Text,
    LocalizationReadStatus Status,
    string? ErrorMessage);

/// <summary>
/// Dosyayı <b>salt okunur</b> biçimde metne çevirir. Boyut/kodlama/erişim sorunlarını
/// istisna yerine durum olarak bildirir.
/// </summary>
public static class LocalizationFileReader
{
    /// <summary>
    /// Dosyanın yalnızca <b>başındaki</b> sınırlı metni okur (başlık/şema tespiti için).
    /// Hiçbir yere yazmaz; hata durumunda istisna fırlatmaz, <c>null</c> döner.
    /// </summary>
    public static string? ReadHead(string filePath, int maxChars = 4096)
    {
        if (maxChars <= 0)
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.SequentialScan);

            using var reader = new StreamReader(
                stream,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);

            var buffer = new char[maxChars];
            var read = reader.ReadBlock(buffer, 0, maxChars);

            return read <= 0 ? null : new string(buffer, 0, read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or System.Text.DecoderFallbackException)
        {
            return null;
        }
    }

    public static TextFileContent ReadText(string filePath, LocalizationFormatReadOptions options)
    {
        FileInfo info;

        try
        {
            info = new FileInfo(filePath);

            if (!info.Exists)
            {
                return new TextFileContent(false, null, LocalizationReadStatus.ReadError,
                    $"Dosya bulunamadı: '{filePath}'.");
            }

            if (info.Length == 0)
            {
                return new TextFileContent(false, null, LocalizationReadStatus.EmptyFile,
                    "Dosya boş (0 bayt).");
            }

            if (info.Length > options.MaxFileBytes)
            {
                return new TextFileContent(false, null, LocalizationReadStatus.FileTooLarge,
                    $"Dosya çok büyük ({info.Length:N0} bayt); sınır {options.MaxFileBytes:N0} bayt.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new TextFileContent(false, null, LocalizationReadStatus.ReadError,
                $"Dosya bilgisi okunamadı: {ex.Message}");
        }

        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            // BOM algılama açık; geçersiz baytlar sessizce değiştirilmez, hata verilir.
            using var reader = new StreamReader(
                stream,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);

            var text = reader.ReadToEnd();

            if (string.IsNullOrWhiteSpace(text))
            {
                return new TextFileContent(false, null, LocalizationReadStatus.EmptyFile,
                    "Dosya yalnızca boşluk/boş satır içeriyor.");
            }

            return new TextFileContent(true, text, LocalizationReadStatus.Parsed, null);
        }
        catch (System.Text.DecoderFallbackException)
        {
            return new TextFileContent(false, null, LocalizationReadStatus.EncodingError,
                "Dosya kodlaması çözülemedi (geçersiz UTF-8 baytları). UTF-8/UTF-16 bekleniyordu.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new TextFileContent(false, null, LocalizationReadStatus.ReadError,
                $"Dosya okunamadı: {ex.Message}");
        }
    }
}
