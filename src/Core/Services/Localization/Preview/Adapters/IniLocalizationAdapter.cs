namespace GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;

using System.Globalization;
using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// INI ayrıştırıcısı: <c>[Bölüm]</c> başlıkları ve <c>anahtar=değer</c> satırlarını sırayla okur.
///
/// <list type="bullet">
/// <item>Ayırıcı olarak <b>ilk</b> <c>=</c> kullanılır; değerin içindeki <c>=</c> korunur.</item>
/// <item><c>;</c> ve <c>#</c> ile başlayan yorumlar ile boş satırlar atlanır.</item>
/// <item>Bölüm bağlamı her kayda yazılır; dosya sırası korunur.</item>
/// <item>Değerler yalnızca baş/son boşluklarından temizlenir; içerik değiştirilmez.</item>
/// <item>Her INI dosyası çeviri dosyası değildir: sayısal/yol değerleri atlanır ve dosya
/// teknik ayar dosyasına benziyorsa uyarı üretilir.</item>
/// </list>
/// </summary>
public sealed class IniLocalizationAdapter : ILocalizationFormatAdapter
{
    public const string FormatKey = "INI";

    public string FormatName => FormatKey;

    public string DisplayName => "INI";

    public string[] SupportedExtensions => new[] { ".ini" };

    public bool SupportsRead => true;

    public bool SupportsWrite => false;

    public Task<LocalizationTextReadResult> ReadAsync(
        string filePath,
        LocalizationFormatReadOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Read(filePath, options ?? new LocalizationFormatReadOptions(), cancellationToken), cancellationToken);

    private LocalizationTextReadResult Read(
        string filePath,
        LocalizationFormatReadOptions options,
        CancellationToken cancellationToken)
    {
        var content = LocalizationFileReader.ReadText(filePath, options);

        if (!content.Success)
        {
            return LocalizationTextReadResult.Fail(content.Status, content.ErrorMessage ?? "Dosya okunamadı.");
        }

        var result = new LocalizationTextReadResult();
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        string? section = null;
        var lineNumber = 0;
        var malformed = 0;
        var translatable = 0;
        var skipped = 0;

        // CRLF, LF ve yalnızca CR kullanan dosyaları aynı satır modeline getir.
        var normalizedText = content.Text!.Replace("\r\n", "\n").Replace('\r', '\n');

        foreach (var rawLine in normalizedText.Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            if (result.Entries.Count >= options.MaxEntries)
            {
                result.Truncated = true;
                result.Warnings.Add($"Kayıt sınırı aşıldı ({options.MaxEntries}); kalan satırlar okunmadı.");
                break;
            }

            var trimmed = rawLine.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed[0] is ';' or '#')
            {
                continue; // yorum satırı
            }

            if (trimmed[0] == '[')
            {
                var close = trimmed.IndexOf(']');

                if (close < 0)
                {
                    malformed++;
                    result.Entries.Add(BuildSkipped(
                        $"satır {lineNumber}", trimmed, "Bölüm başlığı kapatılmamış (']' yok)", section, lineNumber));
                    skipped++;
                    continue;
                }

                var name = trimmed[1..close].Trim();
                section = name.Length == 0 ? null : name;

                if (name.Length == 0)
                {
                    malformed++;
                    result.Warnings.Add($"Satır {lineNumber}: boş bölüm adı.");
                }

                continue;
            }

            var separator = trimmed.IndexOf('=');

            if (separator <= 0)
            {
                malformed++;
                result.Entries.Add(BuildSkipped(
                    $"satır {lineNumber}", trimmed, "Tanınmayan satır ('=' ayırıcısı yok)", section, lineNumber));
                skipped++;
                continue;
            }

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();

            if (key.Length == 0)
            {
                malformed++;
                result.Entries.Add(BuildSkipped(
                    $"satır {lineNumber}", trimmed, "Anahtar adı boş", section, lineNumber));
                skipped++;
                continue;
            }

            var occurrenceKey = section is null ? key : $"{section} / {key}";
            var entryKey = occurrenceKey;

            if (occurrences.TryGetValue(occurrenceKey, out var seen))
            {
                occurrences[occurrenceKey] = seen + 1;
                entryKey = $"{occurrenceKey} #{seen + 1}";
            }
            else
            {
                occurrences[occurrenceKey] = 1;
            }

            var (status, skipReason) = ClassifyValue(value);

            if (status == LocalizationEntryStatus.Translatable)
            {
                translatable++;
            }
            else
            {
                skipped++;
            }

            result.Entries.Add(new LocalizationTextEntry
            {
                Key = entryKey,
                SourceText = value,
                Kind = LocalizationTextKind.IniValue,
                Status = status,
                SkipReason = skipReason,
                Context = section,
                Line = lineNumber,
                Placeholders = status == LocalizationEntryStatus.Skipped
                    ? PlaceholderScanResult.Empty
                    : PlaceholderScanner.Scan(value),
                Evidence = $"INI değeri: {entryKey} (satır {lineNumber})",
                FormatKey = FormatKey
            });
        }

        if (result.Entries.Count == 0)
        {
            result.Warnings.Add("Dosyada anahtar/değer satırı bulunamadı.");
        }

        if (malformed > 0)
        {
            result.Warnings.Add($"{malformed} satır ayrıştırılamadı; 'atlandı' olarak işaretlendi.");
        }

        AddConfigFileWarning(result, filePath, translatable, skipped);

        return result;
    }

    /// <summary>Boş / sayısal / yol değerlerini ayırır; metni çeviri adayı sayar.</summary>
    private static (LocalizationEntryStatus Status, string? SkipReason) ClassifyValue(string value)
    {
        if (value.Length == 0)
        {
            return (LocalizationEntryStatus.Skipped, "Boş değer");
        }

        if (IsNumericOrBoolean(value))
        {
            return (LocalizationEntryStatus.Skipped, "Sayısal/boolean değer çeviri adayı değil");
        }

        if (LooksLikePath(value))
        {
            return (LocalizationEntryStatus.Skipped, "Dosya yolu gibi görünüyor (teknik değer)");
        }

        return (LocalizationEntryStatus.Translatable, null);
    }

    private static bool IsNumericOrBoolean(string value)
    {
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("none", StringComparison.OrdinalIgnoreCase)
            || value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>Sürücü harfli veya UNC yol; çeviri metni olması beklenmez.</summary>
    private static bool LooksLikePath(string value)
    {
        if (value.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        if (value.Length < 3)
        {
            return false;
        }

        return char.IsLetter(value[0])
               && value[1] == ':'
               && (value[2] == '\\' || value[2] == '/');
    }

    private static LocalizationTextEntry BuildSkipped(
        string key,
        string sourceText,
        string reason,
        string? section,
        int line)
        => new()
        {
            Key = key,
            SourceText = sourceText,
            Kind = LocalizationTextKind.IniValue,
            Status = LocalizationEntryStatus.Skipped,
            SkipReason = reason,
            Context = section,
            Line = line,
            Placeholders = PlaceholderScanResult.Empty,
            Evidence = $"INI satırı (satır {line})",
            FormatKey = FormatKey
        };

    /// <summary>
    /// Dosya teknik ayar dosyasına benziyorsa uyarı ekler. Yol/klasör adında yerelleştirme
    /// ipucu varsa (ör. <c>localization/text.ini</c>) uyarı verilmez.
    /// </summary>
    private static void AddConfigFileWarning(
        LocalizationTextReadResult result,
        string filePath,
        int translatable,
        int skipped)
    {
        if (translatable == 0)
        {
            result.Warnings.Add(
                "Bu INI dosyasında çevrilebilir metin bulunamadı; teknik bir ayar dosyası olabilir.");
            return;
        }

        var hasContextHint = LocalizationFileClassifier.Classify(filePath)?.HasContextHint == true;

        if (hasContextHint)
        {
            return;
        }

        var total = translatable + skipped;

        if (total > 0 && translatable * 10 < total * 3)
        {
            result.Warnings.Add(
                $"Değerlerin çoğu sayısal/yol gibi görünüyor (%{translatable * 100 / total} metin); "
                + "bu dosya bir yapılandırma dosyası olabilir.");
            return;
        }

        // Bağlam belirsiz: teknik anahtarlar da metin gibi görünebilir, bunu açıkça belirt.
        result.Warnings.Add(
            "Dosya adı/klasörü yerelleştirme ipucu içermiyor; bu bir yapılandırma dosyası olabilir "
            + "ve değerlerin çeviri metni olduğu doğrulanmadı.");
    }
}


