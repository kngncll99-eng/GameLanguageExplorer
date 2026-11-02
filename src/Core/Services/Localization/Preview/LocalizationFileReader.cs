namespace GameLanguageExplorer.Core.Services.Localization.Preview;

using System.Text;

/// <summary>
/// Kaynak dosyanın algılanan kodlaması ve BOM baytları. Yazma sırasında metin
/// yeniden kodlanırken bu bilgi korunur; sessiz UTF-8 dönüşümü yapılmaz.
/// </summary>
public sealed record LocalizationFileEncoding(
    string Name,
    Encoding Encoding,
    byte[] Preamble)
{
    /// <summary>Metni, algılanan kodlama ve BOM ile baytlara çevirir.</summary>
    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var body = Encoding.GetBytes(text);

        if (Preamble.Length == 0)
        {
            return body;
        }

        var bytes = new byte[Preamble.Length + body.Length];
        Buffer.BlockCopy(Preamble, 0, bytes, 0, Preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, Preamble.Length, body.Length);
        return bytes;
    }
}

/// <summary>Dosya metninin okunma sonucu.</summary>
public sealed record TextFileContent(
    bool Success,
    string? Text,
    LocalizationReadStatus Status,
    string? ErrorMessage)
{
    /// <summary>Okunan dosyanın kodlaması/BOM bilgisi; belirlenemediyse <c>null</c>.</summary>
    public LocalizationFileEncoding? Encoding { get; init; }
}

/// <summary>
/// Dosyayı <b>salt okunur</b> biçimde metne çevirir. Boyut/kodlama/erişim sorunlarını
/// istisna yerine durum olarak bildirir.
/// </summary>
public static class LocalizationFileReader
{
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly byte[] Utf16LeBom = { 0xFF, 0xFE };
    private static readonly byte[] Utf16BeBom = { 0xFE, 0xFF };
    private static readonly byte[] Utf32LeBom = { 0xFF, 0xFE, 0x00, 0x00 };
    private static readonly byte[] Utf32BeBom = { 0x00, 0x00, 0xFE, 0xFF };

    /// <summary>
    /// Dosyanın BOM'unu ve kodlamasını algılar. BOM yoksa geçerli UTF-8 varsayılır;
    /// BOM'lu UTF-16/UTF-32 korunur. Okunamayan dosyada <c>null</c> döner.
    /// </summary>
    public static LocalizationFileEncoding? DetectEncoding(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4,
                FileOptions.SequentialScan);

            Span<byte> header = stackalloc byte[4];
            var read = stream.Read(header);

            if (read >= 4)
            {
                if (header[..4].SequenceEqual(Utf32LeBom))
                {
                    return CreateUtf32(littleEndian: true);
                }

                if (header[..4].SequenceEqual(Utf32BeBom))
                {
                    return CreateUtf32(littleEndian: false);
                }
            }

            if (read >= 3 && header[..3].SequenceEqual(Utf8Bom))
            {
                return new LocalizationFileEncoding(
                    "UTF-8 BOM",
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
                    Utf8Bom);
            }

            if (read >= 2)
            {
                if (header[..2].SequenceEqual(Utf16LeBom))
                {
                    return new LocalizationFileEncoding(
                        "UTF-16 LE BOM",
                        new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true),
                        Utf16LeBom);
                }

                if (header[..2].SequenceEqual(Utf16BeBom))
                {
                    return new LocalizationFileEncoding(
                        "UTF-16 BE BOM",
                        new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true),
                        Utf16BeBom);
                }
            }

            return new LocalizationFileEncoding(
                "UTF-8",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                Array.Empty<byte>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static LocalizationFileEncoding CreateUtf32(bool littleEndian)
        => new(
            littleEndian ? "UTF-32 LE BOM" : "UTF-32 BE BOM",
            new UTF32Encoding(
                bigEndian: !littleEndian,
                byteOrderMark: true,
                throwOnInvalidCharacters: true),
            littleEndian ? Utf32LeBom : Utf32BeBom);

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

        var encoding = DetectEncoding(filePath);

        if (encoding is null)
        {
            return new TextFileContent(false, null, LocalizationReadStatus.EncodingError,
                "Dosya kodlaması güvenilir biçimde belirlenemedi; sessiz dönüşüm yapılmadı.");
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
                encoding.Encoding,
                detectEncodingFromByteOrderMarks: true);

            var text = reader.ReadToEnd();

            if (string.IsNullOrWhiteSpace(text))
            {
                return new TextFileContent(false, null, LocalizationReadStatus.EmptyFile,
                    "Dosya yalnızca boşluk/boş satır içeriyor.");
            }

            return new TextFileContent(true, text, LocalizationReadStatus.Parsed, null)
            {
                Encoding = encoding
            };
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
