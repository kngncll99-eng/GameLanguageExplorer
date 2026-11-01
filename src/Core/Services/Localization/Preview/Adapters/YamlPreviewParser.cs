namespace GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;

using System.Globalization;
using System.Text;

/// <summary>YAML ayrıştırma hatası; satır numarası kullanıcıya raporlanır.</summary>
internal sealed class YamlParseException : Exception
{
    public YamlParseException(string message, int line = 0)
        : base(message)
    {
        Line = line;
    }

    public int Line { get; }
}

/// <summary>
/// YAML belgesini güvenli biçimde gezip skaler değerleri çıkarır. Tam YAML 1.2 değildir;
/// yaygın blok eşleme/liste, tırnaklı skaler, blok metin ve basit flow koleksiyonlarını destekler.
/// </summary>
internal static class YamlPreviewParser
{
    private enum ContainerKind
    {
        Unknown = 0,
        Mapping = 1,
        Sequence = 2
    }

    private enum ScalarValueKind
    {
        String = 0,
        Null = 1,
        Boolean = 2,
        Number = 3
    }

    private sealed record ScalarValue(ScalarValueKind Kind, string Text);

    private sealed class Frame
    {
        public Frame(int indent, string path, ContainerKind kind)
        {
            Indent = indent;
            Path = path;
            Kind = kind;
        }

        public int Indent { get; }
        public string Path { get; }
        public ContainerKind Kind { get; set; }
        public int NextIndex { get; set; }
    }

    private sealed record YamlToken(int Indent, string Content, int RawIndex, int LineNumber);

    private sealed class ParseContext
    {
        public ParseContext(
            string[] rawLines,
            List<YamlToken> tokens,
            LocalizationFormatReadOptions options,
            LocalizationTextReadResult result,
            CancellationToken cancellationToken)
        {
            RawLines = rawLines;
            Tokens = tokens;
            Options = options;
            Result = result;
            CancellationToken = cancellationToken;
        }

        public string[] RawLines { get; }
        public List<YamlToken> Tokens { get; }
        public LocalizationFormatReadOptions Options { get; }
        public LocalizationTextReadResult Result { get; }
        public CancellationToken CancellationToken { get; }
        public HashSet<string> UsedKeys { get; } = new(StringComparer.Ordinal);
        public Stack<Frame> Frames { get; } = new();
        public int? PreviousTokenIndent { get; set; }
        public int? PreviousBlockChildIndent { get; set; }
        public int? LastOpenedFrameIndent { get; set; }
    }

    public static LocalizationTextReadResult Parse(
        string text,
        LocalizationFormatReadOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var rawLines = normalized.Split('\n');
        var tokens = Tokenize(rawLines);
        var result = new LocalizationTextReadResult();
        var context = new ParseContext(rawLines, tokens, options, result, cancellationToken);

        context.Frames.Push(new Frame(-1, string.Empty, ContainerKind.Unknown));

        for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            if (result.Truncated)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (result.Entries.Count >= options.MaxEntries)
            {
                MarkTruncated(
                    context,
                    $"Kayıt sınırı aşıldı ({options.MaxEntries}); kalan düğümler okunmadı.");
                break;
            }

            var currentIndent = tokens[tokenIndex].Indent;
            ProcessLine(context, tokens[tokenIndex], ref tokenIndex);
            context.PreviousTokenIndent = currentIndent;
            context.PreviousBlockChildIndent = context.LastOpenedFrameIndent;
            context.LastOpenedFrameIndent = null;
        }

        return result;
    }

    private static List<YamlToken> Tokenize(string[] rawLines)
    {
        var tokens = new List<YamlToken>(Math.Min(rawLines.Length, 4096));

        for (var index = 0; index < rawLines.Length; index++)
        {
            var line = rawLines[index];
            var indent = 0;

            while (indent < line.Length && line[indent] == ' ')
            {
                indent++;
            }

            if (indent < line.Length && line[indent] == '\t')
            {
                throw new YamlParseException(
                    "Girintide sekme kullanılamaz; boşluk kullanın.",
                    index + 1);
            }

            var content = line[indent..].TrimEnd();

            if (content.Length == 0 || content[0] == '#')
            {
                continue;
            }

            var marker = StripInlineComment(content).Trim();

            if (marker is "---" or "...")
            {
                continue;
            }

            if (marker.StartsWith("%YAML", StringComparison.OrdinalIgnoreCase)
                || marker.StartsWith("%TAG", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            tokens.Add(new YamlToken(indent, content, index, index + 1));
        }

        return tokens;
    }

    private static void ProcessLine(
        ParseContext context,
        YamlToken token,
        ref int tokenIndex)
    {
        var previousIndent = context.PreviousTokenIndent;

        while (context.Frames.Count > 1
               && token.Indent <= context.Frames.Peek().Indent)
        {
            context.Frames.Pop();
        }

        var parent = context.Frames.Peek();

        if (previousIndent is not null
            && token.Indent > previousIndent.Value
            && parent.Indent != previousIndent.Value
            && (context.PreviousBlockChildIndent is null
                || token.Indent <= context.PreviousBlockChildIndent.Value))
        {
            throw new YamlParseException(
                "Beklenmeyen girinti; üst öğe yeni bir blok açmıyor.",
                token.LineNumber);
        }

        var isSequence = IsSequenceLine(token.Content);

        if (parent.Kind == ContainerKind.Unknown)
        {
            parent.Kind = isSequence ? ContainerKind.Sequence : ContainerKind.Mapping;
        }

        if (parent.Kind == ContainerKind.Sequence)
        {
            ProcessSequenceLine(context, token, parent, ref tokenIndex);
            return;
        }

        ProcessMappingLine(context, token, parent, ref tokenIndex);
    }

    private static void ProcessSequenceLine(
        ParseContext context,
        YamlToken token,
        Frame parent,
        ref int tokenIndex)
    {
        if (!IsSequenceLine(token.Content))
        {
            throw new YamlParseException(
                "Liste bağlamında '-' ile başlayan bir öğe bekleniyordu.",
                token.LineNumber);
        }

        var itemPath = AppendIndex(parent.Path, parent.NextIndex++);
        var itemContent = token.Content[1..].TrimStart();

        if (itemContent.Length == 0)
        {
            PushFrame(context, new Frame(token.Indent, itemPath, ContainerKind.Unknown));
            return;
        }

        if (IsSequenceLine(itemContent))
        {
            throw new YamlParseException(
                "Aynı satırda iç içe liste ('- - ...') desteklenmiyor; öğeyi ayrı satıra taşıyın.",
                token.LineNumber);
        }

        if (TrySplitMapping(itemContent, flow: false, token.LineNumber, out var key, out var value))
        {
            if (!PushFrame(
                    context,
                    new Frame(token.Indent, itemPath, ContainerKind.Mapping)))
            {
                return;
            }

            ProcessValue(
                context,
                value,
                JoinPath(itemPath, key),
                itemPath,
                token,
                ref tokenIndex,
                childIndent: token.Indent + 2);
            return;
        }

        ProcessValue(
            context,
            itemContent,
            itemPath,
            parent.Path,
            token,
            ref tokenIndex,
            childIndent: token.Indent);
    }

    private static void ProcessMappingLine(
        ParseContext context,
        YamlToken token,
        Frame parent,
        ref int tokenIndex)
    {
        if (IsSequenceLine(token.Content))
        {
            throw new YamlParseException(
                "Eşleme bağlamında beklenmeyen liste öğesi.",
                token.LineNumber);
        }

        if (!TrySplitMapping(token.Content, flow: false, token.LineNumber, out var key, out var value))
        {
            if (context.Frames.Count == 1)
            {
                // Kök belge tek bir skaler olabilir.
                EmitScalarValue(
                    context,
                    token.Content,
                    "(kök)",
                    string.Empty,
                    token.LineNumber);
                return;
            }

            throw new YamlParseException(
                "Anahtar/değer eşlemesi bekleniyordu.",
                token.LineNumber);
        }

        ProcessValue(
            context,
            value,
            JoinPath(parent.Path, key),
            parent.Path,
            token,
            ref tokenIndex,
            childIndent: token.Indent);
    }

    private static void ProcessValue(
        ParseContext context,
        string rawValue,
        string path,
        string parentPath,
        YamlToken token,
        ref int tokenIndex,
        int childIndent)
    {
        var value = StripInlineComment(rawValue).Trim();

        if (value.Length == 0)
        {
            PushFrame(context, new Frame(childIndent, path, ContainerKind.Unknown));
            return;
        }

        if (IsBlockScalarIndicator(value))
        {
            var blockText = ReadBlockScalar(
                context,
                ref tokenIndex,
                childIndent,
                value,
                token.LineNumber);

            EmitTextValue(context, blockText, path, parentPath, token.LineNumber);
            return;
        }

        if (IsFlowCollectionStart(value))
        {
            EmitFlowNode(context, value, path, parentPath, token.LineNumber, depth: 0);
            return;
        }

        EmitScalarValue(context, value, path, parentPath, token.LineNumber);
    }

    private static bool PushFrame(ParseContext context, Frame frame)
    {
        if (context.Frames.Count >= context.Options.MaxDepth + 1)
        {
            MarkTruncated(
                context,
                $"YAML derinlik sınırı aşıldı ({context.Options.MaxDepth}); kalan düğümler okunmadı.");
            return false;
        }

        context.Frames.Push(frame);
        context.LastOpenedFrameIndent = frame.Indent;
        return true;
    }

    private static void MarkTruncated(ParseContext context, string message)
    {
        if (!context.Result.Truncated)
        {
            context.Result.Truncated = true;
            context.Result.Warnings.Add(message);
        }
    }

    private static void EmitFlowNode(
        ParseContext context,
        string rawValue,
        string path,
        string parentPath,
        int line,
        int depth)
    {
        if (depth > context.Options.MaxDepth)
        {
            MarkTruncated(
                context,
                $"YAML akış derinlik sınırı aşıldı ({context.Options.MaxDepth}); kalan düğümler okunmadı.");
            return;
        }

        var raw = StripInlineComment(rawValue).Trim();

        if (raw.Length == 0)
        {
            return;
        }

        if (raw[0] == '[')
        {
            if (raw[^1] != ']')
            {
                throw new YamlParseException("Akış listesi kapatılmamış.", line);
            }

            var parts = SplitFlow(raw[1..^1], line);
            var index = 0;

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                EmitFlowNode(
                    context,
                    part,
                    AppendIndex(path, index),
                    path,
                    line,
                    depth + 1);

                index++;
            }

            return;
        }

        if (raw[0] == '{')
        {
            if (raw[^1] != '}')
            {
                throw new YamlParseException("Akış eşlemesi kapatılmamış.", line);
            }

            var parts = SplitFlow(raw[1..^1], line);

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                if (!TrySplitMapping(part, flow: true, line, out var key, out var value))
                {
                    throw new YamlParseException(
                        "Akış eşlemesinde 'anahtar: değer' bekleniyordu.",
                        line);
                }

                EmitFlowNode(
                    context,
                    value,
                    JoinPath(path, key),
                    path,
                    line,
                    depth + 1);
            }

            return;
        }

        if (raw[0] is ']' or '}')
        {
            throw new YamlParseException("Beklenmeyen akış kapanış karakteri.", line);
        }

        EmitScalarValue(context, raw, path, parentPath, line);
    }

    private static List<string> SplitFlow(string content, int line)
    {
        var parts = new List<string>();
        var start = 0;
        var depth = 0;
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inDouble)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inDouble = false;
                }

                continue;
            }

            if (inSingle)
            {
                if (c == '\'')
                {
                    if (i + 1 < content.Length && content[i + 1] == '\'')
                    {
                        i++;
                    }
                    else
                    {
                        inSingle = false;
                    }
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inDouble = true;
                    break;
                case '\'':
                    inSingle = true;
                    break;
                case '[':
                case '{':
                    depth++;
                    break;
                case ']':
                case '}':
                    depth--;

                    if (depth < 0)
                    {
                        throw new YamlParseException("Akış koleksiyonu dengesiz.", line);
                    }

                    break;
                case ',' when depth == 0:
                    parts.Add(content[start..i]);
                    start = i + 1;
                    break;
            }
        }

        if (inSingle || inDouble || depth != 0)
        {
            throw new YamlParseException("Akış koleksiyonu veya tırnak kapatılmamış.", line);
        }

        parts.Add(content[start..]);
        return parts;
    }

    private static void EmitScalarValue(
        ParseContext context,
        string rawValue,
        string path,
        string parentPath,
        int line)
    {
        var raw = StripInlineComment(rawValue).Trim();

        if (raw.Length == 0)
        {
            return;
        }

        var scalar = ParseScalarValue(raw, line);

        if (scalar.Kind != ScalarValueKind.String)
        {
            AddEntry(
                context,
                new LocalizationTextEntry
                {
                    Key = UniqueKey(path, context.UsedKeys),
                    SourceText = raw,
                    Kind = LocalizationTextKind.YamlScalar,
                    Status = LocalizationEntryStatus.Skipped,
                    SkipReason = $"YAML {DescribeKind(scalar.Kind)} değeri çeviri adayı değil",
                    Context = DescribeContext(parentPath),
                    Line = line,
                    Placeholders = PlaceholderScanResult.Empty,
                    Evidence = $"YAML skaler değeri: {path}",
                    FormatKey = YamlLocalizationAdapter.FormatKey
                });

            return;
        }

        EmitTextValue(context, scalar.Text, path, parentPath, line);
    }

    private static void EmitTextValue(
        ParseContext context,
        string text,
        string path,
        string parentPath,
        int line)
    {
        var isBlank = string.IsNullOrWhiteSpace(text);

        AddEntry(
            context,
            new LocalizationTextEntry
            {
                Key = UniqueKey(path, context.UsedKeys),
                SourceText = text,
                Kind = LocalizationTextKind.YamlScalar,
                Status = isBlank
                    ? LocalizationEntryStatus.Skipped
                    : LocalizationEntryStatus.Translatable,
                SkipReason = isBlank
                    ? "Boş veya yalnızca boşluk içeren değer"
                    : null,
                Context = DescribeContext(parentPath),
                Line = line,
                Placeholders = isBlank
                    ? PlaceholderScanResult.Empty
                    : PlaceholderScanner.Scan(text),
                Evidence = $"YAML skaler değeri: {path}",
                FormatKey = YamlLocalizationAdapter.FormatKey
            });
    }

    private static void AddEntry(ParseContext context, LocalizationTextEntry entry)
    {
        if (context.Result.Entries.Count >= context.Options.MaxEntries)
        {
            MarkTruncated(
                context,
                $"Kayıt sınırı aşıldı ({context.Options.MaxEntries}); kalan düğümler okunmadı.");
            return;
        }

        context.Result.Entries.Add(entry);
    }

    private static ScalarValue ParseScalarValue(string raw, int line)
    {
        if (raw[0] == '"')
        {
            return new ScalarValue(ScalarValueKind.String, ParseDoubleQuoted(raw, line));
        }

        if (raw[0] == '\'')
        {
            return new ScalarValue(ScalarValueKind.String, ParseSingleQuoted(raw, line));
        }

        if (IsNullLiteral(raw))
        {
            return new ScalarValue(ScalarValueKind.Null, raw);
        }

        if (IsBooleanLiteral(raw))
        {
            return new ScalarValue(ScalarValueKind.Boolean, raw);
        }

        if (IsNumberLiteral(raw))
        {
            return new ScalarValue(ScalarValueKind.Number, raw);
        }

        return new ScalarValue(ScalarValueKind.String, raw);
    }

    private static string ParseDoubleQuoted(string raw, int line)
    {
        if (raw.Length < 2 || raw[^1] != '"')
        {
            throw new YamlParseException("Çift tırnaklı skaler kapatılmamış.", line);
        }

        var end = raw.Length - 1;
        var builder = new StringBuilder(raw.Length - 2);

        for (var i = 1; i < end; i++)
        {
            var c = raw[i];

            if (c == '"')
            {
                throw new YamlParseException(
                    "Çift tırnaklı skalerden sonra beklenmeyen içerik var.",
                    line);
            }

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            i++;

            if (i >= end)
            {
                throw new YamlParseException("Kaçış karakteri eksik.", line);
            }

            switch (raw[i])
            {
                case '0': builder.Append('\0'); break;
                case 'a': builder.Append('\a'); break;
                case 'b': builder.Append('\b'); break;
                case 't': builder.Append('\t'); break;
                case 'n': builder.Append('\n'); break;
                case 'v': builder.Append('\v'); break;
                case 'f': builder.Append('\f'); break;
                case 'r': builder.Append('\r'); break;
                case 'e': builder.Append('\u001b'); break;
                case ' ': builder.Append(' '); break;
                case '"': builder.Append('"'); break;
                case '/': builder.Append('/'); break;
                case '\\': builder.Append('\\'); break;
                case 'N': builder.Append('\u0085'); break;
                case '_': builder.Append('\u00a0'); break;
                case 'L': builder.Append('\u2028'); break;
                case 'P': builder.Append('\u2029'); break;
                case 'x': AppendHexEscape(builder, raw, ref i, 2, end, line); break;
                case 'u': AppendHexEscape(builder, raw, ref i, 4, end, line); break;
                case 'U': AppendHexEscape(builder, raw, ref i, 8, end, line); break;
                default: builder.Append(raw[i]); break;
            }
        }

        return builder.ToString();
    }

    private static void AppendHexEscape(
        StringBuilder builder,
        string raw,
        ref int index,
        int digits,
        int end,
        int line)
    {
        var start = index + 1;

        if (start + digits > end)
        {
            throw new YamlParseException("Unicode kaçış dizisi eksik.", line);
        }

        if (!int.TryParse(
                raw.AsSpan(start, digits),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var code))
        {
            throw new YamlParseException("Geçersiz Unicode kaçış dizisi.", line);
        }

        if (digits <= 4)
        {
            builder.Append((char)code);
        }
        else
        {
            if (code is < 0 or > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
            {
                throw new YamlParseException("Geçersiz Unicode kod noktası.", line);
            }

            builder.Append(char.ConvertFromUtf32(code));
        }

        index += digits;
    }

    private static string ParseSingleQuoted(string raw, int line)
    {
        if (raw.Length < 2 || raw[^1] != '\'')
        {
            throw new YamlParseException("Tek tırnaklı skaler kapatılmamış.", line);
        }

        var end = raw.Length - 1;
        var builder = new StringBuilder(raw.Length - 2);

        for (var i = 1; i < end; i++)
        {
            var c = raw[i];

            if (c != '\'')
            {
                builder.Append(c);
                continue;
            }

            if (i + 1 < end && raw[i + 1] == '\'')
            {
                builder.Append('\'');
                i++;
                continue;
            }

            throw new YamlParseException(
                "Tek tırnaklı skalerden sonra beklenmeyen içerik var.",
                line);
        }

        return builder.ToString();
    }

    private static bool IsNullLiteral(string raw)
        => raw == "~" || string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase);

    private static bool IsBooleanLiteral(string raw)
        => string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumberLiteral(string raw)
    {
        var candidate = raw.Replace("_", string.Empty, StringComparison.Ordinal);

        if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("+0x", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("-0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = candidate[(candidate.IndexOf("x", StringComparison.OrdinalIgnoreCase) + 1)..];
            return long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        }

        if (candidate.StartsWith("0o", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            var offset = candidate[0] is '+' or '-' ? 1 : 0;
            var digits = candidate[(offset + 2)..];

            return digits.Length > 0
                   && (candidate[offset] == '0')
                   && (candidate[offset + 1] is 'o' or 'O'
                       ? digits.All(c => c is >= '0' and <= '7')
                       : digits.All(c => c is '0' or '1'));
        }

        if (candidate is ".inf" or "+.inf" or "-.inf" or ".nan"
            or ".Inf" or "+.Inf" or "-.Inf" or ".NaN"
            or ".INF" or "+.INF" or "-.INF" or ".NAN")
        {
            return true;
        }

        return double.TryParse(
            candidate,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out _);
    }

    private static string DescribeKind(ScalarValueKind kind) => kind switch
    {
        ScalarValueKind.Null => "null",
        ScalarValueKind.Boolean => "boolean",
        ScalarValueKind.Number => "sayı",
        _ => "metin"
    };

    private static bool IsBlockScalarIndicator(string raw)
    {
        if (raw.Length == 0 || raw[0] is not ('|' or '>'))
        {
            return false;
        }

        for (var i = 1; i < raw.Length; i++)
        {
            if (raw[i] is not ('+' or '-' or >= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }

    private static string ReadBlockScalar(
        ParseContext context,
        ref int tokenIndex,
        int parentIndent,
        string indicator,
        int line)
    {
        var start = context.Tokens[tokenIndex].RawIndex + 1;
        var collected = new List<(int Indent, string Content)>();
        var rawIndex = start;

        for (; rawIndex < context.RawLines.Length; rawIndex++)
        {
            var rawLine = context.RawLines[rawIndex];

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                collected.Add((0, string.Empty));
                continue;
            }

            var indent = 0;

            while (indent < rawLine.Length && rawLine[indent] == ' ')
            {
                indent++;
            }

            if (indent <= parentIndent)
            {
                break;
            }

            collected.Add((indent, rawLine[indent..]));
        }

        var endRaw = rawIndex - 1;
        var firstNonBlank = collected.FirstOrDefault(item => item.Content.Length > 0);
        var blockIndent = firstNonBlank.Content.Length == 0
            ? parentIndent + 1
            : collected
                .Where(item => item.Content.Length > 0)
                .Min(item => item.Indent);

        var explicitIndent = indicator.FirstOrDefault(char.IsDigit);

        if (explicitIndent != default)
        {
            blockIndent = Math.Max(0, parentIndent) + (explicitIndent - '0');
        }

        var lines = new List<string>(collected.Count);

        foreach (var item in collected)
        {
            if (item.Content.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            lines.Add(item.Indent >= blockIndent
                ? new string(' ', item.Indent - blockIndent) + item.Content
                : item.Content);
        }

        var body = indicator[0] == '>'
            ? FoldBlockScalar(lines)
            : string.Join("\n", lines);

        if (indicator.Contains('+'))
        {
            body += "\n";
        }
        else
        {
            body = body.TrimEnd('\n');

            if (!indicator.Contains('-') && body.Length > 0)
            {
                body += "\n";
            }
        }

        while (tokenIndex + 1 < context.Tokens.Count
               && context.Tokens[tokenIndex + 1].RawIndex <= endRaw)
        {
            tokenIndex++;
        }

        _ = line;
        return body;
    }

    private static string FoldBlockScalar(List<string> lines)
    {
        var builder = new StringBuilder();
        var pendingBlankLines = 0;

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                pendingBlankLines++;
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(pendingBlankLines > 0
                    ? new string('\n', pendingBlankLines)
                    : " ");
            }

            builder.Append(line);
            pendingBlankLines = 0;
        }

        return builder.ToString();
    }

    private static bool IsSequenceLine(string content)
        => content == "-"
           || content.StartsWith("- ", StringComparison.Ordinal)
           || content.StartsWith("-\t", StringComparison.Ordinal);

    private static bool IsFlowCollectionStart(string value)
        => value.Length > 0 && value[0] is '[' or '{';

    private static string JoinPath(string parent, string key)
        => string.IsNullOrEmpty(parent) ? key : $"{parent}.{key}";

    private static string AppendIndex(string parent, int index)
        => string.IsNullOrEmpty(parent) ? $"[{index}]" : $"{parent}[{index}]";

    private static string DescribeContext(string path)
        => string.IsNullOrEmpty(path) ? "(kök)" : path;

    private static bool TrySplitMapping(
        string content,
        bool flow,
        int line,
        out string key,
        out string value)
    {
        key = string.Empty;
        value = string.Empty;

        var depth = 0;
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inDouble)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inDouble = false;
                }

                continue;
            }

            if (inSingle)
            {
                if (c == '\'')
                {
                    if (i + 1 < content.Length && content[i + 1] == '\'')
                    {
                        i++;
                    }
                    else
                    {
                        inSingle = false;
                    }
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inDouble = true;
                    break;
                case '\'':
                    inSingle = true;
                    break;
                case '[':
                case '{':
                    depth++;
                    break;
                case ']':
                case '}':
                    depth--;
                    break;
                case ':' when depth == 0
                               && (flow
                                   || i == content.Length - 1
                                   || char.IsWhiteSpace(content[i + 1])):
                {
                    var rawKey = content[..i].Trim();

                    if (rawKey.Length == 0)
                    {
                        return false;
                    }

                    key = NormalizeKey(rawKey, line);
                    value = content[(i + 1)..].Trim();
                    return key.Length > 0;
                }
            }
        }

        return false;
    }

    private static string NormalizeKey(string rawKey, int line)
    {
        if (rawKey.Length > 0 && rawKey[0] == '"')
        {
            return ParseDoubleQuoted(rawKey, line);
        }

        if (rawKey.Length > 0 && rawKey[0] == '\'')
        {
            return ParseSingleQuoted(rawKey, line);
        }

        return StripInlineComment(rawKey).Trim();
    }

    private static string StripInlineComment(string content)
    {
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inDouble)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inDouble = false;
                }

                continue;
            }

            if (inSingle)
            {
                if (c == '\'')
                {
                    if (i + 1 < content.Length && content[i + 1] == '\'')
                    {
                        i++;
                    }
                    else
                    {
                        inSingle = false;
                    }
                }

                continue;
            }

            if (c == '"')
            {
                inDouble = true;
                continue;
            }

            if (c == '\'')
            {
                inSingle = true;
                continue;
            }

            if (c == '#' && (i == 0 || char.IsWhiteSpace(content[i - 1])))
            {
                return content[..i].TrimEnd();
            }
        }

        return content;
    }

    private static string UniqueKey(string baseKey, HashSet<string> usedKeys)
    {
        if (usedKeys.Add(baseKey))
        {
            return baseKey;
        }

        var suffix = 2;

        while (!usedKeys.Add($"{baseKey}#{suffix}"))
        {
            suffix++;
        }

        return $"{baseKey}#{suffix}";
    }
}

