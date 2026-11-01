namespace GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;

using System.Diagnostics;
using System.Globalization;
using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// CSV/TSV ortak mantığı. Ayraç türü alt sınıfta belirlenir; geri kalan her şey (başlık tespiti,
/// hücre sınıflandırması, konum üretimi) paylaşılır. Böylece iki format için ayrı ayrıştırıcı
/// yazılmaz.
/// </summary>
public abstract class DelimitedLocalizationAdapterBase : ILocalizationFormatAdapter
{
    private static readonly string[] TextColumnHints =
    {
        "text", "name", "desc", "title", "label", "message", "msg", "string",
        "caption", "subtitle", "dialog", "line", "content", "translation"
    };

    private static readonly string[] HeaderColumnHints =
    {
        "id", "key", "value", "source", "target", "original", "translated",
        "language", "locale", "context", "comment", "file", "path", "index"
    };

    protected abstract char? ForcedDelimiter { get; }

    protected abstract string RecordWord { get; }

    public abstract string FormatName { get; }

    public abstract string DisplayName { get; }

    public abstract string[] SupportedExtensions { get; }

    public bool SupportsRead => true;

    public virtual bool SupportsWrite => false;

    /// <summary>
    /// Boş çeviri hücreleri yazılabilir hedef sayılsın mı? Yalnızca CSV'de açıktır; salt okunur
    /// TSV davranışı değişmez.
    /// </summary>
    protected virtual bool TreatEmptyTranslationAsTranslatable => false;

    public Task<LocalizationTextReadResult> ReadAsync(
        string filePath,
        LocalizationFormatReadOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => Read(filePath, options ?? new LocalizationFormatReadOptions(), cancellationToken),
            cancellationToken);

    private LocalizationTextReadResult Read(
        string filePath,
        LocalizationFormatReadOptions options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var content = LocalizationFileReader.ReadText(filePath, options);

        if (!content.Success)
        {
            return LocalizationTextReadResult.Fail(content.Status, content.ErrorMessage ?? "Dosya okunamadı.");
        }

        try
        {
            var parse = DelimitedTextParser.Parse(content.Text!, ForcedDelimiter);
            var result = new LocalizationTextReadResult();

            foreach (var warning in parse.Warnings)
            {
                result.Warnings.Add(warning);
            }

            if (parse.Rows.Count == 0)
            {
                result.Warnings.Add("Dosyada okunabilir satır bulunamadı.");
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            var fieldCounts = parse.Rows.Select(r => r.Fields.Count).Distinct().OrderBy(c => c).ToList();

            if (fieldCounts.Count > 1)
            {
                result.Warnings.Add(
                    $"Satırlarda farklı sütun sayıları var ({string.Join(", ", fieldCounts)}); dosya düzensiz olabilir.");
            }

            var columnNames = DetectHeaderColumnNames(
                parse,
                out var hasHeader,
                out var headerCandidateRejected,
                out var headerInferred);

            if (!hasHeader)
            {
                result.Warnings.Add(headerCandidateRejected
                    ? "Başlık satırı algılanmadı; ilk satır başlık benzeri görünse de güvenilir tespit edilemedi ve veri olarak korundu."
                    : "Başlık satırı algılanmadı; ilk satır dahil tüm satırlar veri olarak okundu.");
            }
            else if (headerInferred)
            {
                result.Warnings.Add(
                    "Başlık satırı yalnızca tek sütun adı veya veri türü farkıyla sezgisel algılandı; ilk satır veri olabilir.");
            }

            var startRow = hasHeader ? 1 : 0;

            for (var rowIndex = startRow; rowIndex < parse.Rows.Count; rowIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (result.Entries.Count >= options.MaxEntries)
                {
                    result.Truncated = true;
                    result.Warnings.Add($"Kayıt sınırı aşıldı ({options.MaxEntries}); kalan satırlar okunmadı.");
                    break;
                }

                var row = parse.Rows[rowIndex];

                for (var columnIndex = 0; columnIndex < row.Fields.Count; columnIndex++)
                {
                    // Satır içindeki hücreler tek tek eklendiği için sınırı burada da kontrol et.
                    if (result.Entries.Count >= options.MaxEntries)
                    {
                        result.Truncated = true;
                        result.Warnings.Add($"Kayıt sınırı aşıldı ({options.MaxEntries}); kalan satırlar okunmadı.");
                        break;
                    }

                    var value = row.Fields[columnIndex];
                    var columnName = columnIndex < columnNames.Count ? columnNames[columnIndex] : null;
                    var key = columnName is null
                        ? $"satır {row.LineNumber} / sütun {columnIndex + 1}"
                        : $"satır {row.LineNumber} / {columnName}";

                    var (status, skipReason) = ClassifyCell(value, columnName);

                    result.Entries.Add(new LocalizationTextEntry
                    {
                        Key = key,
                        SourceText = value,
                        Kind = LocalizationTextKind.DelimitedCell,
                        Status = status,
                        SkipReason = skipReason,
                        Context = columnName ?? $"sütun {columnIndex + 1}",
                        Line = row.LineNumber,
                        Placeholders = status == LocalizationEntryStatus.Skipped
                            ? PlaceholderScanResult.Empty
                            : PlaceholderScanner.Scan(value),
                        Evidence = $"{RecordWord} hücresi: satır {row.LineNumber}, sütun {columnIndex + 1}"
                                   + $" (ayraç: {DelimitedTextParser.DisplayDelimiter(parse.Delimiter)})",
                        FormatKey = FormatName
                    });
                }

                if (result.Truncated)
                {
                    break;
                }
            }

            result.Duration = stopwatch.Elapsed;
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return LocalizationTextReadResult.Fail(
                LocalizationReadStatus.InvalidSyntax,
                $"{(string.IsNullOrEmpty(DisplayName) ? FormatName : DisplayName)} ayrıştırılamadı: {ex.Message}");
        }
    }

    /// <summary>
    /// İlk satırı başlık olarak kabul edip etmeyeceğini belirler. Kimlik benzeri hücreler tek başına
    /// yeterli değildir: tanınan bir sütun adı veya veri hücreleriyle tür farkı yoksa ilk satır
    /// başlık sayılmaz ve veri olarak korunur.
    /// </summary>
    protected static List<string> DetectHeaderColumnNames(
        DelimitedParseResult parse,
        out bool hasHeader,
        out bool headerCandidateRejected,
        out bool headerInferred)
    {
        hasHeader = false;
        headerCandidateRejected = false;
        headerInferred = false;

        if (parse.Rows.Count < 2)
        {
            return new List<string>();
        }

        var first = parse.Rows[0];

        if (first.Fields.Count < 2)
        {
            return new List<string>();
        }

        var names = first.Fields.Select(f => f.Trim()).ToList();

        if (names.Any(string.IsNullOrEmpty))
        {
            return new List<string>();
        }

        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
        {
            return new List<string>();
        }

        if (!names.All(IsHeaderLike))
        {
            return new List<string>();
        }

        var knownColumnNameCount = names.Count(IsKnownHeaderColumn);

        if (knownColumnNameCount == 0 && !HasHeaderValueContrast(first, parse.Rows))
        {
            headerCandidateRejected = true;
            return new List<string>();
        }

        hasHeader = true;
        headerInferred = knownColumnNameCount < 2;
        return names;
    }

    /// <summary>
    /// Sütun adı ipucu bulunmayan başlıklarda, veri satırlarındaki en az bir hücrenin başlık
    /// hücresiyle aynı şekle sahip olmaması (sayı, uzun metin, cümle noktalaması) başlık lehine kanıt sayılır.
    /// </summary>
    private static bool HasHeaderValueContrast(DelimitedRow header, IReadOnlyList<DelimitedRow> rows)
    {
        for (var columnIndex = 0; columnIndex < header.Fields.Count; columnIndex++)
        {
            for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];

                if (columnIndex >= row.Fields.Count)
                {
                    continue;
                }

                var value = row.Fields[columnIndex].Trim();

                if (value.Length > 0 && !IsHeaderLike(value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsKnownHeaderColumn(string value)
    {
        if (IsTextColumn(value))
        {
            return true;
        }

        var trimmed = value.Trim();

        return HeaderColumnHints.Any(hint =>
            string.Equals(trimmed, hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHeaderLike(string value)
    {
        if (value.Length == 0 || value.Length > 64)
        {
            return false;
        }

        if (value.IndexOfAny(new[] { '.', '!', '?', ',', ':', ';' }) >= 0)
        {
            return false;
        }

        var firstChar = value[0];

        if (!char.IsLetter(firstChar) && firstChar != '_')
        {
            return false;
        }

        return value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-' or '(' or ')' or '#');
    }

    /// <summary>Boş / sayısal / metin hücrelerini ayırır.</summary>
    private (LocalizationEntryStatus Status, string? SkipReason) ClassifyCell(string value, string? columnName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // Boş çeviri hücresi yazma için geçerli bir hedeftir; kaynak/id sütunları atlanır.
            if (TreatEmptyTranslationAsTranslatable
                && !string.IsNullOrWhiteSpace(columnName)
                && CsvLocalizationAdapter.IsTranslationColumnName(columnName))
            {
                return (LocalizationEntryStatus.Translatable, null);
            }

            return (LocalizationEntryStatus.Skipped, "Boş hücre");
        }

        if (!IsNumeric(value))
        {
            return (LocalizationEntryStatus.Translatable, null);
        }

        // Bağlam belirsizse aday olarak işaretle: metin sütunundaki sayı gerçekten metin olabilir.
        if (columnName is not null && IsTextColumn(columnName))
        {
            return (LocalizationEntryStatus.Ambiguous,
                $"Sayısal görünüyor ancak '{columnName}' sütunu metin olabilir");
        }

        return (LocalizationEntryStatus.Skipped, "Sayısal değer çeviri adayı değil");
    }

    private static bool IsNumeric(string value)
    {
        var trimmed = value.Trim();

        return double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out _);
    }

    private static bool IsTextColumn(string columnName)
    {
        var lowered = columnName.ToLowerInvariant();

        return TextColumnHints.Any(hint => lowered.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Virgül/noktalı virgül gibi ayraçları otomatik algılayan CSV ayrıştırıcısı.
/// Yazma desteği yalnızca başlıkta tanınan bir çeviri sütunu varsa kullanılır.
/// </summary>
public sealed class CsvLocalizationAdapter : DelimitedLocalizationAdapterBase, ILocalizationWritableFormatAdapter
{
    public const string FormatKey = "CSV";

    protected override char? ForcedDelimiter => null;

    protected override string RecordWord => "CSV";

    public override string FormatName => FormatKey;

    public override string DisplayName => "CSV";

    public override string[] SupportedExtensions => new[] { ".csv" };

    public override bool SupportsWrite => true;

    protected override bool TreatEmptyTranslationAsTranslatable => true;

    /// <summary>Başlık adı çeviri sütunu olarak kullanılabilir mi?</summary>
    public static bool IsTranslationColumnName(string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return false;
        }

        var normalized = new string(columnName
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

        return normalized is "translation"
            or "translations"
            or "target"
            or "targettext"
            or "translated"
            or "translationtext"
            or "ceviri"
            or "hedef";
    }

    public Task<LocalizationWriteContentResult> BuildWriteContentAsync(
        string sourceFilePath,
        IReadOnlyList<LocalizationWriteTranslation> translations,
        LocalizationFormatWriteOptions? options = null,
        CancellationToken cancellationToken = default)
        => WritableLocalizationWriteSupport.BuildFromSourceAsync(
            sourceFilePath,
            translations,
            options,
            "CSV",
            (sourceText, writeOptions) => BuildWriteContent(sourceText, translations, writeOptions),
            cancellationToken);

    private static LocalizationWriteContentResult BuildWriteContent(
        string sourceText,
        IReadOnlyList<LocalizationWriteTranslation> translations,
        LocalizationFormatWriteOptions options)
    {
        var parse = DelimitedTextParser.Parse(sourceText, null);

        if (!parse.IsWellFormed || parse.Rows.Count == 0)
        {
            return LocalizationWriteContentResult.Fail("CSV yapısı güvenli biçimde çözülemedi.");
        }

        var headerNames = DetectHeaderColumnNames(
            parse,
            out var hasHeader,
            out _,
            out _);

        if (!hasHeader || headerNames.Count == 0)
        {
            return LocalizationWriteContentResult.Fail(
                "CSV dosyasında güvenilir bir başlık satırı bulunamadı; yazma reddedildi.");
        }

        var translationColumnIndex = -1;
        for (var index = 0; index < headerNames.Count; index++)
        {
            if (IsTranslationColumnName(headerNames[index]))
            {
                translationColumnIndex = index;
                break;
            }
        }

        if (translationColumnIndex < 0)
        {
            return LocalizationWriteContentResult.Fail(
                "CSV başlığında translation/target/translated sütunu bulunamadı; yazma reddedildi.");
        }



        var rowsByLine = parse.Rows.ToDictionary(row => row.LineNumber);
        var mutableRows = parse.Rows.Select(row => row.Fields.ToList()).ToList();
        var applied = 0;
        var translationHeader = headerNames[translationColumnIndex];

        foreach (var translation in translations)
        {
            if (!TryParseEntryKey(translation.Key, out var lineNumber, out var columnName))
            {
                return LocalizationWriteContentResult.Fail(
                    $"CSV kayıt anahtarı çözülemedi: '{translation.Key}'.");
            }

            // Kaynak/id gibi diğer sütunlara yazılmasına izin verme.
            if (!IsTranslationColumnName(columnName)
                || !string.Equals(columnName, translationHeader, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!rowsByLine.TryGetValue(lineNumber, out var row)
                || row.Fields.Count <= translationColumnIndex)
            {
                return LocalizationWriteContentResult.Fail(
                    $"CSV satırı bulunamadı veya çeviri sütunu eksik: '{translation.Key}'.");
            }

            var current = row.Fields[translationColumnIndex];
            if (!string.Equals(current, translation.SourceText, StringComparison.Ordinal))
            {
                return LocalizationWriteContentResult.Fail(
                    $"'{translation.Key}' kaynak metni değişmiş; yazma reddedildi.");
            }

            var rowIndex = parse.Rows.IndexOf(row);
            mutableRows[rowIndex][translationColumnIndex] = translation.Translation;
            applied++;
        }

        if (applied == 0)
        {
            return LocalizationWriteContentResult.Fail(
                "CSV çeviri sütununda güncellenecek kayıt bulunamadı.");
        }

        var newline = sourceText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var keepTrailingNewline = sourceText.EndsWith("\n", StringComparison.Ordinal)
                                  || sourceText.EndsWith("\r", StringComparison.Ordinal);
        var builder = new System.Text.StringBuilder();

        for (var rowIndex = 0; rowIndex < mutableRows.Count; rowIndex++)
        {
            var fields = mutableRows[rowIndex];
            for (var columnIndex = 0; columnIndex < fields.Count; columnIndex++)
            {
                if (columnIndex > 0)
                {
                    builder.Append(parse.Delimiter);
                }

                builder.Append(EscapeCsvField(fields[columnIndex], parse.Delimiter));
            }

            if (rowIndex < mutableRows.Count - 1 || keepTrailingNewline)
            {
                builder.Append(newline);
            }
        }

        return LocalizationWriteContentResult.Ok(builder.ToString());
    }

    private static bool TryParseEntryKey(string key, out int lineNumber, out string columnName)
    {
        lineNumber = 0;
        columnName = string.Empty;

        const string prefix = "satır ";
        var separator = key.IndexOf(" / ", StringComparison.Ordinal);

        if (!key.StartsWith(prefix, StringComparison.Ordinal) || separator <= prefix.Length)
        {
            return false;
        }

        if (!int.TryParse(
                key[prefix.Length..separator],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out lineNumber))
        {
            return false;
        }

        columnName = key[(separator + 3)..];
        return !string.IsNullOrWhiteSpace(columnName);
    }

    private static string EscapeCsvField(string value, char delimiter)
    {
        var needsQuotes = value.Contains(delimiter)
                          || value.Contains('"')
                          || value.Contains('\r')
                          || value.Contains('\n');

        return needsQuotes
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}

/// <summary>
/// TSV ayrıştırıcısı: CSV ile <b>aynı</b> çözümleyiciyi kullanır, yalnızca ayraç sekmeye sabittir.
/// Ayrı bir motor yazılmadı.
/// </summary>
public sealed class TsvLocalizationAdapter : DelimitedLocalizationAdapterBase
{
    public const string FormatKey = "TSV";

    protected override char? ForcedDelimiter => '\t';

    protected override string RecordWord => "TSV";

    public override string FormatName => FormatKey;

    public override string DisplayName => "TSV";

    public override string[] SupportedExtensions => new[] { ".tsv" };
}

