namespace GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;

using System.Diagnostics;
using System.Globalization;
using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// UEExtractor'ın ürettiği <b>LocresCSV</b> biçimini (<c>key,source,Translation</c>) salt okunur
/// olarak ayrıştırır.
///
/// <para>CSV/TSV adaptörlerinden farkı: her hücre ayrı bir metin değildir. Bir satır tek bir
/// kayıttır ve alanlar anlam taşır:</para>
/// <list type="bullet">
/// <item><c>key</c> → <see cref="LocalizationTextEntry.Key"/> (anahtar; <b>kaynak metin değildir</b>)</item>
/// <item><c>source</c> → <see cref="LocalizationTextEntry.SourceText"/> (çevrilecek orijinal metin)</item>
/// <item><c>Translation</c> → <see cref="LocalizationTextEntry.ExistingTranslation"/> (boşsa çeviri yok)</item>
/// </list>
///
/// <para>Sütun sırası sabit değildir; başlık adları büyük/küçük harf duyarsız ve boşluklardan
/// arındırılmış olarak eşlenir. Virgül/tırnak/kaçışlı tırnak/çok satırlı alanlar mevcut
/// <see cref="DelimitedTextParser"/> (RFC 4180) ile çözülür — ayrı bir CSV motoru yoktur.</para>
///
/// <para><b>Yazma yoktur:</b> <see cref="SupportsWrite"/> daima <c>false</c>; dosya yalnızca
/// paylaşımlı okuma ile açılır.</para>
/// </summary>
public sealed class LocresCsvLocalizationAdapter : ILocalizationFormatAdapter, ILocalizationFormatSniffer
{
    public const string FormatKey = "LOCRESCSV";

    public const string KeyHeader = "key";
    public const string SourceHeader = "source";
    public const string TranslationHeader = "translation";

    /// <summary>
    /// UEExtractor dokümantasyonu: "key = unique string of 32 characters". Bu kural
    /// <b>engelleyici değildir</b>; uymayan satırlar atılmaz, yalnızca uyarı + işaret alır.
    /// </summary>
    public const int DocumentedKeyLength = 32;

    /// <summary>Başlık tespiti için okunan en fazla karakter.</summary>
    public const int HeaderSampleChars = 4096;

    /// <summary>Başlık ayrıştırmasında denenen ayraçlar (sırayla).</summary>
    private static readonly char[] CandidateDelimiters = { ',', ';', '|' };

    public string FormatName => FormatKey;

    public string DisplayName => "LocresCSV";

    public string[] SupportedExtensions => new[] { ".csv" };

    public bool SupportsRead => true;

    public bool SupportsWrite => false;

    public Task<LocalizationTextReadResult> ReadAsync(
        string filePath,
        LocalizationFormatReadOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => Read(filePath, options ?? new LocalizationFormatReadOptions(), cancellationToken),
            cancellationToken);

    /// <summary>
    /// Yalnızca <c>key</c>, <c>source</c> ve <c>Translation</c> başlıklarının <b>üçü birden</b>
    /// bulunduğunda eşleşir. Sıradan CSV (<c>name,value</c> gibi) eşleşmez.
    /// </summary>
    public bool MatchesHeader(string filePath, string headerSample) => TryMapColumns(headerSample, out _);

    /// <summary>Başlık satırından sütun indekslerini ve ayracı belirler.</summary>
    private static bool TryMapColumns(string? sample, out LocresColumns columns)
    {
        columns = default;

        if (string.IsNullOrWhiteSpace(sample))
        {
            return false;
        }

        foreach (var delimiter in CandidateDelimiters)
        {
            var parse = DelimitedTextParser.Parse(sample, delimiter);

            if (parse.Rows.Count == 0)
            {
                continue;
            }

            var header = parse.Rows[0].Fields;

            if (header.Count < 3)
            {
                continue;
            }

            var keyIndex = IndexOfHeader(header, KeyHeader);
            var sourceIndex = IndexOfHeader(header, SourceHeader);
            var translationIndex = IndexOfHeader(header, TranslationHeader);

            if (keyIndex < 0 || sourceIndex < 0 || translationIndex < 0)
            {
                continue;
            }

            columns = new LocresColumns(delimiter, keyIndex, sourceIndex, translationIndex);
            return true;
        }

        return false;
    }

    private static int IndexOfHeader(IReadOnlyList<string> header, string expected)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(NormalizeHeader(header[i]), expected, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Yalnızca başlık hücrelerini karşılaştırma için sadeleştirir: boşluk, BOM ve gereksiz
    /// tırnaklar temizlenir (ör. <c> " KEY " </c> → <c>KEY</c>). Veri alanlarına uygulanmaz;
    /// aksi hâlde anlamlı boşluklar ve literal tırnaklar kaybolur.
    /// </summary>
    private static string NormalizeHeader(string? value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Trim().Trim('\uFEFF').Trim('"').Trim();

    private LocalizationTextReadResult Read(
        string filePath,
        LocalizationFormatReadOptions options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var content = LocalizationFileReader.ReadText(filePath, options);

        if (!content.Success)
        {
            return LocalizationTextReadResult.Fail(
                content.Status,
                content.ErrorMessage ?? "Dosya okunamadı.");
        }

        try
        {
            var text = content.Text!;
            var sample = text.Length > HeaderSampleChars ? text[..HeaderSampleChars] : text;

            if (!TryMapColumns(sample, out var columns))
            {
                // Sessiz tahmin yok: sıradan CSV'yi LocresCSV sanmamak için açık hata.
                return LocalizationTextReadResult.Fail(
                    LocalizationReadStatus.InvalidSyntax,
                    "LocresCSV başlıkları bulunamadı: 'key', 'source' ve 'Translation' sütunlarının "
                    + "üçü birden gerekli. Dosya sıradan CSV olabilir; bu ayrıştırıcı yalnızca "
                    + "belgelenmiş LocresCSV şemasını okur.");
            }

            var parse = DelimitedTextParser.Parse(text, columns.Delimiter);
            var result = new LocalizationTextReadResult();

            foreach (var warning in parse.Warnings)
            {
                result.Warnings.Add(warning);
            }

            if (!parse.IsWellFormed)
            {
                result.Warnings.Add(
                    "CSV yapısı bozuk olabilir (kapanmayan tırnak); satırlar okunabildiği kadar alındı.");
            }

            if (parse.Rows.Count == 0)
            {
                result.Warnings.Add("Dosyada okunabilir satır yok.");
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            var headerNames = parse.Rows[0].Fields.Select(NormalizeHeader).ToArray();
            result.Warnings.Add("LocresCSV algılandı: sütun düzeni " + string.Join(", ", headerNames) + ".");

            if (parse.Rows.Count == 1)
            {
                result.Warnings.Add("Başlık dışında veri satırı yok.");
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            var usedIndexes = new HashSet<int> { columns.KeyIndex, columns.SourceIndex, columns.TranslationIndex };
            var ignoredColumns = headerNames
                .Where((name, index) => !usedIndexes.Contains(index) && name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ignoredColumns.Length > 0)
            {
                result.Warnings.Add(
                    "Kullanılmayan fazladan sütun(lar): " + string.Join(", ", ignoredColumns) + ".");
            }

            var firstLineByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var duplicates = new List<string>();
            var duplicateTotal = 0;
            var invalidKeyFormatCount = 0;

            ReadRows(parse, columns, headerNames, options, cancellationToken, result,
                firstLineByKey, duplicates, ref duplicateTotal, ref invalidKeyFormatCount);

            if (duplicateTotal > 0)
            {
                result.Warnings.Add(
                    $"{duplicateTotal} yinelenen anahtar korundu (birleştirilmedi): "
                    + string.Join(" | ", duplicates)
                    + (duplicateTotal > duplicates.Count ? " | …" : string.Empty));
            }

            if (invalidKeyFormatCount > 0)
            {
                result.Warnings.Add(
                    $"{invalidKeyFormatCount} kayıt belgelenmiş 32 karakterlik anahtar biçimine uymuyor; "
                    + "kayıtlar atılmadı, yalnızca 'key-format' işaretiyle bildirildi.");
            }

            result.Duration = stopwatch.Elapsed;
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return LocalizationTextReadResult.Fail(
                LocalizationReadStatus.InvalidSyntax,
                $"LocresCSV ayrıştırılamadı: {ex.Message}");
        }
    }

    private static void ReadRows(
        DelimitedParseResult parse,
        LocresColumns columns,
        string[] headerNames,
        LocalizationFormatReadOptions options,
        CancellationToken cancellationToken,
        LocalizationTextReadResult result,
        Dictionary<string, int> firstLineByKey,
        List<string> duplicates,
        ref int duplicateTotal,
        ref int invalidKeyFormatCount)
    {
        for (var rowIndex = 1; rowIndex < parse.Rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (result.Entries.Count >= options.MaxEntries)
            {
                result.Truncated = true;
                result.Warnings.Add($"Kayıt sınırı aşıldı ({options.MaxEntries}); kalan satırlar okunmadı.");
                break;
            }

            var row = parse.Rows[rowIndex];
            var fields = row.Fields;

            if (fields.Count == 0 || fields.All(string.IsNullOrWhiteSpace))
            {
                continue; // tamamen boş satır veri değildir
            }

            // CSV alanları burada zaten çözülmüştür. İçerik ikinci kez kırpılmaz veya
            // tırnaklardan arındırılmaz; aksi hâlde anlamlı boşluklar ve literal tırnaklar kaybolur.
            var key = FieldAt(fields, columns.KeyIndex);
            var source = FieldAt(fields, columns.SourceIndex);
            var translation = FieldAt(fields, columns.TranslationIndex);

            var flags = new List<string>();
            LocalizationEntryStatus status;
            string? skipReason = null;

            if (columns.KeyIndex >= fields.Count
                || columns.SourceIndex >= fields.Count
                || columns.TranslationIndex >= fields.Count)
            {
                status = LocalizationEntryStatus.Skipped;
                skipReason = $"Eksik sütun (satırda {fields.Count} alan var)";
            }
            else if (key.Length == 0)
            {
                status = LocalizationEntryStatus.Skipped;
                skipReason = "Anahtar boş";
            }
            else if (string.IsNullOrWhiteSpace(key))
            {
                status = LocalizationEntryStatus.Skipped;
                skipReason = "Anahtar yalnızca boşluk içeriyor";
            }
            else if (source.Length == 0)
            {
                status = LocalizationEntryStatus.Skipped;
                skipReason = "Kaynak metin boş";
            }
            else if (string.IsNullOrWhiteSpace(source))
            {
                status = LocalizationEntryStatus.Skipped;
                skipReason = "Kaynak metin yalnızca boşluk içeriyor";
            }
            else if (IsNumeric(source))
            {
                status = LocalizationEntryStatus.Ambiguous;
                skipReason = "Sayısal görünüyor; kaynak metin olabilir";
            }
            else
            {
                status = LocalizationEntryStatus.Translatable;
            }

            if (key.Length > 0)
            {
                if (firstLineByKey.TryGetValue(key, out var firstLine))
                {
                    // Yinelenen anahtar birleştirilmez, silinmez: her satır korunur.
                    duplicateTotal++;
                    flags.Add("duplicate-key");

                    if (duplicates.Count < 5)
                    {
                        duplicates.Add($"{Shorten(key)} (satır {firstLine} ve {row.LineNumber})");
                    }
                }
                else
                {
                    firstLineByKey[key] = row.LineNumber;
                }

                if (!LooksLikeDocumentedKey(key))
                {
                    invalidKeyFormatCount++;
                    flags.Add("key-format");
                }
            }

            result.Entries.Add(new LocalizationTextEntry
            {
                Key = key.Length == 0 ? $"satır {row.LineNumber} (anahtar boş)" : key,
                SourceText = source,
                Kind = LocalizationTextKind.LocresCsvEntry,
                Status = status,
                SkipReason = skipReason,
                Context = "LocresCSV · sütunlar: " + string.Join(", ", headerNames),
                Line = row.LineNumber,
                Placeholders = status == LocalizationEntryStatus.Skipped
                    ? PlaceholderScanResult.Empty
                    : PlaceholderScanner.Scan(source),
                Evidence = $"LocresCSV satırı {row.LineNumber}: anahtar '{Shorten(key)}', "
                           + $"kaynak {source.Length} karakter, "
                           + (translation.Length > 0 ? "mevcut çeviri var" : "mevcut çeviri yok"),
                FormatKey = FormatKey,
                ExistingTranslation = translation.Length == 0 ? null : translation,
                Flags = flags
            });
        }
    }

    private static string FieldAt(IReadOnlyList<string> fields, int index)
        => index >= 0 && index < fields.Count ? fields[index] : string.Empty;

    /// <summary>
    /// Belgelenmiş anahtar biçimi: UEExtractor README "32 karakterlik benzersiz dize".
    /// Bu kontrol <b>bilgilendiricidir</b>; uymayan satır atılmaz.
    /// </summary>
    private static bool LooksLikeDocumentedKey(string key)
        => key.Length == DocumentedKeyLength
           && key.All(c => char.IsAsciiHexDigit(c));

    private static bool IsNumeric(string value)
        => double.TryParse(
            value.Trim(),
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out _);

    private static string Shorten(string value, int max = 40)
        => value.Length <= max ? value : value[..max] + "…";

    private readonly record struct LocresColumns(
        char Delimiter,
        int KeyIndex,
        int SourceIndex,
        int TranslationIndex);
}
