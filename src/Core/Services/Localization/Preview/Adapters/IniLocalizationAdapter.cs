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
public sealed class IniLocalizationAdapter : ILocalizationFormatAdapter, ILocalizationWritableFormatAdapter
{
    public const string FormatKey = "INI";

    public string FormatName => FormatKey;

    public string DisplayName => "INI";

    public string[] SupportedExtensions => new[] { ".ini" };

    public bool SupportsRead => true;

    public bool SupportsWrite => true;

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

    public Task<LocalizationWriteContentResult> BuildWriteContentAsync(
        string sourceFilePath,
        IReadOnlyList<LocalizationWriteTranslation> translations,
        LocalizationFormatWriteOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => BuildWriteContent(
                sourceFilePath,
                translations,
                options ?? new LocalizationFormatWriteOptions()),
            cancellationToken);

    private static LocalizationWriteContentResult BuildWriteContent(
        string sourceFilePath,
        IReadOnlyList<LocalizationWriteTranslation> translations,
        LocalizationFormatWriteOptions options)
    {
        if (translations.Count == 0)
        {
            return LocalizationWriteContentResult.Fail("Yazılacak çeviri kaydı yok.");
        }

        var content = LocalizationFileReader.ReadText(sourceFilePath, new LocalizationFormatReadOptions
        {
            MaxFileBytes = options.MaxFileBytes,
            MaxDepth = options.MaxDepth
        });

        if (!content.Success)
        {
            return LocalizationWriteContentResult.Fail(content.ErrorMessage ?? "INI dosyası okunamadı.");
        }

        var lines = content.Text!.Split('\n').ToList();
        var targets = BuildTargets(lines);
        var applied = 0;

        foreach (var translation in translations)
        {
            if (string.IsNullOrWhiteSpace(translation.Key))
            {
                return LocalizationWriteContentResult.Fail("INI kayıt anahtarı boş olamaz.");
            }

            if (!targets.TryGetValue(translation.Key, out var target))
            {
                return LocalizationWriteContentResult.Fail(
                    $"INI kaydı bulunamadı: '{translation.Key}'.");
            }

            if (!string.Equals(target.SourceText, translation.SourceText, StringComparison.Ordinal))
            {
                return LocalizationWriteContentResult.Fail(
                    $"'{translation.Key}' kaynak değeri değişmiş; yazma reddedildi.");
            }

            if (string.Equals(target.SourceText, translation.Translation, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrEmpty(translation.Translation)
                || translation.Translation.Contains('\r')
                || translation.Translation.Contains('\n'))
            {
                return LocalizationWriteContentResult.Fail(
                    $"'{translation.Key}' için tek satırlı, boş olmayan bir değer gerekli.");
            }

            lines[target.LineIndex] = ReplaceValue(lines[target.LineIndex], target, translation.Translation);
            applied++;
        }

        return applied == 0
            ? LocalizationWriteContentResult.Ok(content.Text!)
            : LocalizationWriteContentResult.Ok(string.Join("\n", lines));
    }

    /// <summary>
    /// Okuma sırasındaki bölüm/anahtar ve yinelenen anahtar numaralandırmasını birebir
    /// kullanarak yalnızca değer aralığını kaydeder.
    /// </summary>
    private static Dictionary<string, IniWriteTarget> BuildTargets(IReadOnlyList<string> lines)
    {
        var targets = new Dictionary<string, IniWriteTarget>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? section = null;

        for (var index = 0; index < lines.Count; index++)
        {
            var raw = lines[index];
            var ending = raw.EndsWith('\r') ? "\r" : string.Empty;
            var content = ending.Length == 0 ? raw : raw[..^1];
            var trimmed = content.Trim();

            if (trimmed.Length == 0 || trimmed[0] is ';' or '#')
            {
                continue;
            }

            if (trimmed[0] == '[')
            {
                var close = trimmed.IndexOf(']');

                if (close >= 0)
                {
                    var name = trimmed[1..close].Trim();
                    section = name.Length == 0 ? null : name;
                }

                continue;
            }

            var separator = trimmed.IndexOf('=');

            if (separator <= 0)
            {
                continue;
            }

            var key = trimmed[..separator].Trim();

            if (key.Length == 0)
            {
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

            var coreStart = content.Length - content.TrimStart().Length;
            var coreEnd = content.Length - content.TrimEnd().Length;
            var core = content[coreStart..(content.Length - coreEnd)];
            var equals = core.IndexOf('=');

            if (equals <= 0)
            {
                continue;
            }

            var valueRaw = core[(equals + 1)..];
            var leftWhitespace = valueRaw.Length - valueRaw.TrimStart(' ', '\t').Length;
            var rightWhitespace = valueRaw.Length - valueRaw.TrimEnd(' ', '\t').Length;
            var valueStart = coreStart + equals + 1 + leftWhitespace;
            var valueEnd = coreStart + core.Length - rightWhitespace;

            if (valueEnd <= valueStart)
            {
                continue;
            }

            targets[entryKey] = new IniWriteTarget(
                index,
                valueStart,
                valueEnd,
                content[valueStart..valueEnd]);
        }

        return targets;
    }

    private static string ReplaceValue(string raw, IniWriteTarget target, string translation)
        => raw[..target.ValueStart] + translation + raw[target.ValueEnd..];

    private sealed record IniWriteTarget(
        int LineIndex,
        int ValueStart,
        int ValueEnd,
        string SourceText);

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


