namespace GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>Ayrıştırılmış tek bir CSV/TSV satırı.</summary>
public sealed record DelimitedRow(int LineNumber, IReadOnlyList<string> Fields);

/// <summary>CSV/TSV ayrıştırma sonucu.</summary>
public sealed class DelimitedParseResult
{
    public List<DelimitedRow> Rows { get; } = new();

    public char Delimiter { get; set; }

    /// <summary>Ayraç güvenilir şekilde belirlenebildi mi?</summary>
    public bool DelimiterDetermined { get; set; } = true;

    /// <summary>Tırnak yapısı dengeli mi?</summary>
    public bool IsWellFormed { get; set; } = true;

    public List<string> Warnings { get; } = new();
}

/// <summary>
/// RFC 4180 uyumlu ayraçlı metin (CSV/TSV) çözümleyicisi.
///
/// <list type="bullet">
/// <item>Tırnak içindeki ayraçlar ve kaçışlı tırnaklar (<c>""</c>) doğru işlenir.</item>
/// <item>Ayraç tahmin edilemezse <see cref="DelimitedParseResult.DelimiterDetermined"/> false olur
/// ve uyarı üretilir; sessizce yanlış sonuç verilmez.</item>
/// <item>Yapı hiçbir şekilde yeniden yazılmaz; yalnızca okunur.</item>
/// </list>
/// </summary>
public static class DelimitedTextParser
{
    public static readonly char[] CandidateDelimiters = { ',', ';', '\t', '|' };

    public static DelimitedParseResult Parse(string text, char? forcedDelimiter = null)
    {
        var result = new DelimitedParseResult();

        if (forcedDelimiter is { } forced)
        {
            result.Delimiter = forced;
        }
        else
        {
            var detection = DetectDelimiter(text);
            result.Delimiter = detection.Delimiter;
            result.DelimiterDetermined = detection.Determined;

            if (detection.Warning is not null)
            {
                result.Warnings.Add(detection.Warning);
            }
        }

        Tokenize(text, result.Delimiter, result);

        return result;
    }

    /// <summary>
    /// Örneklemin ilk 8 KB'ında tırnak dışındaki ayraçları sayarak en tutarlı ayracı seçer.
    /// </summary>
    private static (char Delimiter, bool Determined, string? Warning) DetectDelimiter(string text)
    {
        var sampleLength = Math.Min(text.Length, 8192);
        var sample = text[..sampleLength];

        var best = '\0';
        var bestLines = -1;
        var bestCount = -1;
        var tieCount = 0;

        foreach (var candidate in CandidateDelimiters)
        {
            var (lines, count) = CountOutsideQuotes(sample, candidate);

            if (count == 0)
            {
                continue;
            }

            if (lines > bestLines || (lines == bestLines && count > bestCount))
            {
                best = candidate;
                bestLines = lines;
                bestCount = count;
                tieCount = 1;
            }
            else if (lines == bestLines && count == bestCount)
            {
                tieCount++;
            }
        }

        if (best == '\0')
        {
            return (',', false,
                "Ayraç belirlenemedi (tek sütunlu dosya olabilir); virgül varsayıldı ve sütunlar tek tek okundu.");
        }

        if (tieCount > 1)
        {
            return (best, true,
                $"Birden fazla ayraç olası ({DisplayDelimiter(best)} seçildi); sütunlar beklenenden farklı olabilir.");
        }

        return (best, true, null);
    }

    private static (int Lines, int Count) CountOutsideQuotes(string text, char delimiter)
    {
        var inQuotes = false;
        var lines = 0;
        var count = 0;
        var lineHasDelimiter = false;
        var seenAny = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
            {
                continue;
            }

            if (ch == delimiter)
            {
                count++;
                lineHasDelimiter = true;
                seenAny = true;
            }
            else if (ch is '\n' or '\r' && lineHasDelimiter)
            {
                lines++;
                lineHasDelimiter = false;
            }
        }

        if (lineHasDelimiter)
        {
            lines++;
        }

        return seenAny ? (lines, count) : (0, 0);
    }

    /// <summary>Gösterim için ayracı okunabilir biçime çevirir.</summary>
    public static string DisplayDelimiter(char delimiter) => delimiter switch
    {
        '\t' => "sekme (\\t)",
        ',' => "virgül (,)",
        ';' => "noktalı virgül (;)",
        '|' => "dikey çizgi (|)",
        _ => $"'{delimiter}'"
    };

    /// <summary>RFC 4180 uyumlu tokenizer: tırnak, kaçışlı tırnak ve satır sonlarını işler.</summary>
    private static void Tokenize(string text, char delimiter, DelimitedParseResult result)
    {
        var fields = new List<string>();
        var builder = new System.Text.StringBuilder();
        var inQuotes = false;
        var line = 1;
        var rowLine = 1;
        var rowStarted = false;

        void EndField()
        {
            fields.Add(builder.ToString());
            builder.Clear();
        }

        void EndRow()
        {
            EndField();
            result.Rows.Add(new DelimitedRow(rowLine, fields.ToArray()));
            fields.Clear();
            rowStarted = false;
            rowLine = line;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }

                    continue;
                }

                if (ch == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        // Tırnak içindeki satır sonu alan verisidir; CRLF olduğu gibi korunur.
                        builder.Append("\r\n");
                        i++;
                        line++;
                        continue;
                    }

                    builder.Append('\r');
                    line++;
                    continue;
                }

                if (ch == '\n')
                {
                    line++;
                }

                builder.Append(ch);
                continue;
            }

            if (ch == '"' && builder.Length == 0)
            {
                // Alan başındaki tırnak, tırnaklı alanı başlatır (RFC 4180).
                inQuotes = true;
                rowStarted = true;
                continue;
            }

            if (ch == delimiter)
            {
                EndField();
                rowStarted = true;
                continue;
            }

            if (ch == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    continue;
                }

                EndRow();
                line++;
                rowLine = line;
                continue;
            }

            if (ch == '\n')
            {
                EndRow();
                line++;
                rowLine = line;
                continue;
            }

            builder.Append(ch);
            rowStarted = true;
        }

        if (inQuotes)
        {
            result.IsWellFormed = false;
            result.Warnings.Add("Kapanmayan tırnak bulundu; dosya yapısı beklenenden farklı olabilir.");
        }

        if (builder.Length > 0 || fields.Count > 0 || rowStarted)
        {
            EndRow();
        }
    }
}


