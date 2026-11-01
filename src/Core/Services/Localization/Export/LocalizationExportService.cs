namespace GameLanguageExplorer.Core.Services.Localization.Export;

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>JSON, CSV ve PO formatlarında çeviri dışa aktarımı yapar.</summary>
public sealed class LocalizationExportService : ILocalizationExportService
{
    public async Task<LocalizationExportResult> ExportAsync(
        string filePath,
        LocalizationExportFormat format,
        IReadOnlyList<LocalizationExportEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return LocalizationExportResult.Fail("Dışa aktarma dosya yolu boş olamaz.");
        }

        if (entries is null || entries.Count == 0)
        {
            return LocalizationExportResult.Fail("Dışa aktarılacak çeviri kaydı yok.");
        }

        try
        {
            var warnings = ValidatePlaceholders(entries);
            var content = format switch
            {
                LocalizationExportFormat.Json => BuildJson(entries),
                LocalizationExportFormat.Csv => BuildCsv(entries),
                LocalizationExportFormat.Po => BuildPo(entries),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Desteklenmeyen format.")
            };

            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                    filePath,
                    content,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);

            return new LocalizationExportResult(
                true,
                filePath,
                entries.Count,
                warnings,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return LocalizationExportResult.Fail("Dışa aktarma iptal edildi.");
        }
        catch (Exception exception)
        {
            return LocalizationExportResult.Fail($"Dışa aktarma başarısız: {exception.Message}");
        }
    }

    private static List<string> ValidatePlaceholders(IReadOnlyList<LocalizationExportEntry> entries)
    {
        var warnings = new List<string>();

        foreach (var entry in entries)
        {
            var comparison = PlaceholderComparer.Compare(entry.SourceText, entry.Translation);
            if (!comparison.IsMatch)
            {
                warnings.Add(
                    $"'{entry.Key}' yer tutucuları kaynakla eşleşmiyor: "
                    + string.Join(", ", comparison.Issues.Select(issue => issue.Type)));
            }
        }

        return warnings;
    }

    private static string BuildJson(IReadOnlyList<LocalizationExportEntry> entries)
    {
        var payload = entries.Select(entry => new
        {
            key = entry.Key,
            source = entry.SourceText,
            translation = entry.Translation,
            context = entry.Context
        });

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static string BuildCsv(IReadOnlyList<LocalizationExportEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("key,source,translation");

        foreach (var entry in entries)
        {
            builder.Append(EscapeCsv(entry.Key)).Append(',')
                .Append(EscapeCsv(entry.SourceText)).Append(',')
                .Append(EscapeCsv(entry.Translation)).AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildPo(IReadOnlyList<LocalizationExportEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("msgid \"\"");
        builder.AppendLine("msgstr \"\"");
        builder.AppendLine("\"Content-Type: text/plain; charset=UTF-8\\n\"");
        builder.AppendLine();

        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Context))
            {
                builder.AppendLine("#. context: " + entry.Context.ReplaceLineEndings(" "));
            }

            builder.AppendLine("msgctxt " + EscapePo(entry.Key));
            builder.AppendLine("msgid " + EscapePo(entry.SourceText));
            builder.AppendLine("msgstr " + EscapePo(entry.Translation));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string value)
        => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string EscapePo(string value)
        => "\""
           + value
               .Replace("\\", "\\\\", StringComparison.Ordinal)
               .Replace("\"", "\\\"", StringComparison.Ordinal)
               .Replace("\r\n", "\\n", StringComparison.Ordinal)
               .Replace("\n", "\\n", StringComparison.Ordinal)
               .Replace("\r", "\\n", StringComparison.Ordinal)
           + "\"";
}
