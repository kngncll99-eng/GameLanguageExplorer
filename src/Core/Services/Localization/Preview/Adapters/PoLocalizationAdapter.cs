namespace GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;

using System.Text;

/// <summary>PO dosyasındaki tek bir mesaj kaydı (ham hâli).</summary>
internal sealed class PoMessage
{
    public int Line { get; set; }
    public string? Context { get; set; }
    public string? Id { get; set; }
    public string? IdPlural { get; set; }
    public string? Str { get; set; }

    /// <summary>Tekil <c>msgstr</c> satır numarası (1 tabanlı); yoksa -1.</summary>
    public int StrLine { get; set; } = -1;

    public List<PoPluralForm> PluralForms { get; } = new();
    public List<int> FlagLines { get; } = new();
    public List<string> Flags { get; } = new();
    public List<string> References { get; } = new();
    public bool IsObsolete { get; set; }
    public bool IsMalformed { get; set; }
    public string? MalformedReason { get; set; }

    public bool HasContent =>
        Id is not null || Str is not null || IdPlural is not null ||
        Context is not null || PluralForms.Count > 0;
}

/// <summary>Tek bir çoğul çeviri biçimi (<c>msgstr[n]</c>).</summary>
internal sealed record PoPluralForm(int Index, string Text, int Line);

/// <summary>PO ayrıştırma sonucu.</summary>
internal sealed class PoParseResult
{
    public List<PoMessage> Messages { get; } = new();
    public List<string> Warnings { get; } = new();
    public int SyntaxErrorCount { get; set; }
    public bool Truncated { get; set; }
}

/// <summary>
/// GNU gettext PO/POT ayrıştırıcısı (satır tabanlı durum makinesi; regex yok).
///
/// <para>Desteklenenler: <c>msgctxt</c>, <c>msgid</c>, <c>msgid_plural</c>, <c>msgstr</c>,
/// <c>msgstr[n]</c>, çok satırlı tırnaklı dizeler, kaçış dizileri, <c>#</c> yorumları,
/// <c>#,</c> işaretleri, <c>#:</c> referansları, <c>#~</c> eski (obsolete) kayıtlar ve
/// boş satırla ayrılan kayıtlar.</para>
/// </summary>
internal static class PoDocumentParser
{
    private enum PoField
    {
        None = 0,
        Context,
        Id,
        IdPlural,
        Str,
        PluralStr
    }

    public static PoParseResult Parse(string text, int maxMessages, CancellationToken cancellationToken)
    {
        var result = new PoParseResult();
        var normalizedText = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var pendingFlags = new List<string>();
        var pendingReferences = new List<string>();
        var pendingFlagLines = new List<int>();

        PoMessage? current = null;
        var lastField = PoField.None;
        var lastPluralIndex = -1;
        var lineNumber = 0;

        void EnsureCurrent()
        {
            if (current is not null)
            {
                return;
            }

            current = new PoMessage { Line = lineNumber };

            foreach (var flag in pendingFlags)
            {
                if (!current.Flags.Contains(flag, StringComparer.OrdinalIgnoreCase))
                {
                    current.Flags.Add(flag);
                }
            }

            current.References.AddRange(pendingReferences);
            current.FlagLines.AddRange(pendingFlagLines);
            pendingFlags.Clear();
            pendingReferences.Clear();
            pendingFlagLines.Clear();
        }

        void MarkError(string reason)
        {
            result.SyntaxErrorCount++;

            if (result.Warnings.Count < 30)
            {
                result.Warnings.Add($"Satır {lineNumber}: {reason}");
            }

            EnsureCurrent();
            current!.IsMalformed = true;
            current.MalformedReason ??= reason;
        }

        void ReportWarning(string reason)
        {
            if (result.Warnings.Count < 30)
            {
                result.Warnings.Add($"Satır {lineNumber}: {reason}");
            }
        }

        void AppendToCurrent(string value)
        {
            switch (lastField)
            {
                case PoField.Context:
                    current!.Context += value;
                    break;
                case PoField.Id:
                    current!.Id += value;
                    break;
                case PoField.IdPlural:
                    current!.IdPlural += value;
                    break;
                case PoField.Str:
                    current!.Str += value;
                    break;
                case PoField.PluralStr:
                    var position = current!.PluralForms.FindIndex(f => f.Index == lastPluralIndex);

                    if (position >= 0)
                    {
                        var form = current.PluralForms[position];
                        current.PluralForms[position] = form with { Text = form.Text + value };
                    }

                    break;
            }
        }

        void Flush()
        {
            if (current is null)
            {
                return;
            }

            if (current.HasContent)
            {
                if (result.Messages.Count >= maxMessages)
                {
                    result.Truncated = true;
                }
                else
                {
                    result.Messages.Add(current);
                }
            }

            current = null;
            lastField = PoField.None;
            lastPluralIndex = -1;
        }

        bool StartsNewRecord(string directiveName)
        {
            if (current is null)
            {
                return false;
            }

            var hasMessageContent = current.Id is not null ||
                                    current.Str is not null ||
                                    current.PluralForms.Count > 0;

            return directiveName switch
            {
                "msgid" => hasMessageContent,
                "msgctxt" => hasMessageContent || current.IdPlural is not null,
                _ => false
            };
        }

        void ClosePreviousRecordIfNeeded(string directiveName)
        {
            if (!StartsNewRecord(directiveName))
            {
                return;
            }

            var previousLine = current!.Line;
            ReportWarning(
                $"'{directiveName}' direktifi önceki kayıttan boş satırla ayrılmamış; " +
                $"satır {previousLine} kaydı kapatıldı.");
            Flush();
        }

        foreach (var rawLine in normalizedText.Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            var trimmed = rawLine.Trim();

            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }

            var work = trimmed;
            var obsolete = false;

            if (work.StartsWith("#~", StringComparison.Ordinal))
            {
                obsolete = true;
                work = work[2..].TrimStart();

                if (work.Length == 0)
                {
                    continue;
                }
            }
            else if (work[0] == '#')
            {
                HandleComment(work, pendingFlags, pendingReferences, pendingFlagLines, lineNumber);
                continue;
            }

            if (work[0] == '"')
            {
                if (obsolete)
                {
                    EnsureCurrent();
                    current!.IsObsolete = true;
                }

                if (!TryParseQuoted(work, out var continuation, out var continuationError))
                {
                    MarkError($"Devam satırı okunamadı: {continuationError}");
                    continue;
                }

                if (current is null || lastField == PoField.None)
                {
                    MarkError("Sahipsiz devam satırı (önceki direktif yok).");
                    continue;
                }

                AppendToCurrent(continuation);
                continue;
            }

            if (!TryParseDirective(work, out var directiveName, out var pluralIndex, out var value, out var error))
            {
                var head = work.Split(new[] { ' ', '\t' }, 2)[0];
                ClosePreviousRecordIfNeeded(head);
                MarkError($"Tanınmayan satır: {error}");
                continue;
            }

            ClosePreviousRecordIfNeeded(directiveName);
            EnsureCurrent();

            if (obsolete)
            {
                current!.IsObsolete = true;
            }

            switch (directiveName)
            {
                case "msgctxt":
                    current!.Context = value;
                    lastField = PoField.Context;
                    break;

                case "msgid":
                    current!.Id = value;
                    lastField = PoField.Id;
                    break;

                case "msgid_plural":
                    current!.IdPlural = value;
                    lastField = PoField.IdPlural;
                    break;

                case "msgstr":
                    current!.Str = value;
                    current.StrLine = lineNumber;
                    lastField = PoField.Str;
                    break;

                case "msgstr_plural":
                    current!.PluralForms.Add(new PoPluralForm(pluralIndex, value, lineNumber));
                    lastField = PoField.PluralStr;
                    lastPluralIndex = pluralIndex;
                    break;

                default:
                    MarkError($"Bilinmeyen direktif: '{directiveName}'.");
                    break;
            }
        }

        Flush();
        return result;
    }

    private static void HandleComment(
        string comment,
        List<string> flags,
        List<string> references,
        List<int> flagLines,
        int lineNumber)
    {
        if (comment.StartsWith("#,", StringComparison.Ordinal))
        {
            foreach (var part in comment[2..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Length > 0 && !flags.Contains(part, StringComparer.OrdinalIgnoreCase))
                {
                    flags.Add(part);
                }
            }

            if (!flagLines.Contains(lineNumber))
            {
                flagLines.Add(lineNumber);
            }

            return;
        }

        if (comment.StartsWith("#:", StringComparison.Ordinal))
        {
            var reference = comment[2..].Trim();

            if (reference.Length > 0)
            {
                references.Add(reference);
            }
        }

        // "#." (çıkarılan yorum), "#|" (önceki değer) ve "#" (çevirmen yorumu) yok sayılır.
    }

    private static bool TryParseDirective(
        string line,
        out string name,
        out int pluralIndex,
        out string value,
        out string error)
    {
        name = string.Empty;
        pluralIndex = -1;
        value = string.Empty;
        error = string.Empty;

        var space = line.IndexOfAny(new[] { ' ', '\t' });

        if (space <= 0)
        {
            error = "direktif ve tırnaklı değer bekleniyordu";
            return false;
        }

        var head = line[..space];
        var rest = line[space..].TrimStart();

        if (head.StartsWith("msgstr[", StringComparison.Ordinal))
        {
            if (!head.EndsWith(']'))
            {
                error = $"geçersiz çoğul biçim: '{head}'";
                return false;
            }

            var digits = head[7..^1];

            if (!int.TryParse(digits, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out pluralIndex))
            {
                error = $"geçersiz çoğul indeksi: '{digits}'";
                return false;
            }

            name = "msgstr_plural";
        }
        else
        {
            name = head;
        }

        if (name is not ("msgctxt" or "msgid" or "msgid_plural" or "msgstr" or "msgstr_plural"))
        {
            error = $"bilinmeyen direktif '{head}'";
            return false;
        }

        if (!TryParseQuoted(rest, out value, out var quoteError))
        {
            error = quoteError;
            return false;
        }

        return true;
    }

    /// <summary>Bir veya daha fazla tırnaklı parçayı çözer (PO kaçışları desteklenir).</summary>
    private static bool TryParseQuoted(string text, out string value, out string error)
    {
        var builder = new StringBuilder();
        var index = 0;
        var any = false;

        value = string.Empty;
        error = string.Empty;

        while (index < text.Length)
        {
            while (index < text.Length && (text[index] == ' ' || text[index] == '\t'))
            {
                index++;
            }

            if (index >= text.Length)
            {
                break;
            }

            if (text[index] != '"')
            {
                error = $"tırnak bekleniyordu ('{text[index]}')";
                return false;
            }

            index++;
            var closed = false;

            while (index < text.Length)
            {
                var ch = text[index];

                if (ch == '\\')
                {
                    index++;

                    if (index >= text.Length)
                    {
                        break;
                    }

                    if (!TryAppendEscape(text, ref index, builder, out var escapeError))
                    {
                        error = escapeError;
                        return false;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    index++;
                    closed = true;
                    break;
                }

                builder.Append(ch);
                index++;
            }

            if (!closed)
            {
                error = "kapanmayan tırnak";
                return false;
            }

            any = true;
        }

        if (!any)
        {
            error = "tırnaklı dize bekleniyordu";
            return false;
        }

        value = builder.ToString();
        return true;
    }

    /// <summary>PO kaçış dizilerini çözer; bilinmeyen kaçışta hata döndürür.</summary>
    private static bool TryAppendEscape(
        string text,
        ref int index,
        StringBuilder builder,
        out string error)
    {
        error = string.Empty;
        var ch = text[index];

        switch (ch)
        {
            case 'n': builder.Append('\n'); index++; return true;
            case 't': builder.Append('\t'); index++; return true;
            case 'r': builder.Append('\r'); index++; return true;
            case 'a': builder.Append('\a'); index++; return true;
            case 'b': builder.Append('\b'); index++; return true;
            case 'f': builder.Append('\f'); index++; return true;
            case 'v': builder.Append('\v'); index++; return true;
            case '\\': builder.Append('\\'); index++; return true;
            case '"': builder.Append('"'); index++; return true;
            case '\'': builder.Append('\''); index++; return true;
            case '?': builder.Append('?'); index++; return true;
            case 'x':
            {
                index++;
                var start = index;

                while (index < text.Length && index - start < 2 && Uri.IsHexDigit(text[index]))
                {
                    index++;
                }

                if (index == start)
                {
                    error = "geçersiz \\x kaçışı: onaltılık basamak bekleniyordu";
                    return false;
                }

                builder.Append((char)Convert.ToInt32(text[start..index], 16));
                return true;
            }
        }

        if (ch >= '0' && ch <= '7')
        {
            var start = index;

            while (index < text.Length && index - start < 3 && text[index] >= '0' && text[index] <= '7')
            {
                index++;
            }

            builder.Append((char)Convert.ToInt32(text[start..index], 8));
            return true;
        }

        error = $"bilinmeyen kaçış dizisi '\\{ch}'";
        return false;
    }
}

/// <summary>
/// PO/POT ortak adaptörü: aynı ayrıştırıcıyı kullanır, yalnızca format anahtarı ve
/// şablon anlamı farklıdır. <b>PO yazma desteği alt sınıfta açılır; POT salt okunurdur.</b>
/// </summary>
public abstract class PoLocalizationAdapterBase : GameLanguageExplorer.Core.Interfaces.ILocalizationFormatAdapter
{
    public abstract string FormatName { get; }
    public abstract string DisplayName { get; }
    public abstract string[] SupportedExtensions { get; }

    /// <summary>POT şablonu mu? (msgstr boş olması normaldir.)</summary>
    protected abstract bool IsTemplate { get; }

    public bool SupportsRead => true;

    public virtual bool SupportsWrite => false;

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

        var parse = PoDocumentParser.Parse(content.Text!, options.MaxEntries, cancellationToken);
        var result = new LocalizationTextReadResult();
        var truncationWarningReported = false;

        void MarkTruncated()
        {
            result.Truncated = true;

            if (truncationWarningReported)
            {
                return;
            }

            result.Warnings.Add($"Kayıt sınırı aşıldı ({options.MaxEntries}); kalan kayıtlar okunmadı.");
            truncationWarningReported = true;
        }

        if (parse.Truncated)
        {
            MarkTruncated();
        }

        foreach (var warning in parse.Warnings)
        {
            result.Warnings.Add(warning);
        }

        if (parse.Messages.Count == 0 && parse.SyntaxErrorCount > 0)
        {
            return LocalizationTextReadResult.Fail(
                LocalizationReadStatus.InvalidSyntax,
                $"PO dosyası ayrıştırılamadı: {result.Warnings.FirstOrDefault() ?? "bilinmeyen sözdizimi hatası"}");
        }

        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        var headerEmitted = false;

        foreach (var message in parse.Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (result.Entries.Count >= options.MaxEntries)
            {
                MarkTruncated();
                break;
            }

            // Başlık kaydı: açıkça boş msgid ("") olan ilk, sağlam, bağlamsız kayıt.
            // Bozuk kayıtlar (msgid hiç yok) başlık sayılmaz.
            var isHeader = !headerEmitted &&
                           !message.IsMalformed &&
                           !message.IsObsolete &&
                           message.Context is null &&
                           message.Id is { Length: 0 };

            if (isHeader)
            {
                headerEmitted = true;
            }

            if (!Emit(message, isHeader, result, usedKeys, options.MaxEntries))
            {
                MarkTruncated();
                break;
            }
        }

        if (parse.SyntaxErrorCount > 0)
        {
            result.Warnings.Add(
                $"{parse.SyntaxErrorCount} sözdizimi hatası bulundu; etkilenen kayıtlar 'atlandı' olarak işaretlendi.");
        }

        if (IsTemplate && result.Entries.All(e => e.Status == LocalizationEntryStatus.Skipped))
        {
            result.Warnings.Add("POT şablonunda çevrilebilir kayıt bulunamadı.");
        }

        return result;
    }

    private bool Emit(
        PoMessage message,
        bool isHeader,
        LocalizationTextReadResult result,
        HashSet<string> usedKeys,
        int maxEntries)
    {
        if (result.Entries.Count >= maxEntries)
        {
            result.Truncated = true;
            return false;
        }

        var keyBase = BuildKeyBase(message);
        var reference = message.References.Count == 0 ? null : string.Join(" ", message.References);
        var isFuzzy = message.Flags.Contains("fuzzy", StringComparer.OrdinalIgnoreCase);

        if (isHeader && !message.IsMalformed)
        {
            Add(result, usedKeys, new LocalizationTextEntry
            {
                Key = UniqueKey("PO başlık kaydı", usedKeys),
                SourceText = message.Str ?? string.Empty,
                Kind = LocalizationTextKind.PoMessage,
                Status = LocalizationEntryStatus.Skipped,
                SkipReason = "PO başlık kaydı (meta veri); çeviri metni değil",
                Line = message.Line,
                Placeholders = PlaceholderScanResult.Empty,
                Evidence = $"PO başlık kaydı (satır {message.Line})",
                FormatKey = FormatName,
                Flags = message.Flags,
                Reference = reference
            });

            return true;
        }

        if (message.IsMalformed)
        {
            Add(result, usedKeys, new LocalizationTextEntry
            {
                Key = UniqueKey(keyBase, usedKeys),
                SourceText = message.Id ?? message.Str ?? string.Empty,
                Kind = LocalizationTextKind.PoMessage,
                Status = LocalizationEntryStatus.Skipped,
                SkipReason = $"Ayrıştırılamayan kayıt: {message.MalformedReason}",
                Context = message.Context,
                Line = message.Line,
                Placeholders = PlaceholderScanResult.Empty,
                Evidence = $"PO kaydı (satır {message.Line})",
                FormatKey = FormatName,
                Flags = message.Flags,
                Reference = reference
            });

            return true;
        }

        if (message.IsObsolete)
        {
            Add(result, usedKeys, new LocalizationTextEntry
            {
                Key = UniqueKey(keyBase, usedKeys),
                SourceText = message.Id ?? string.Empty,
                Kind = LocalizationTextKind.PoMessage,
                Status = LocalizationEntryStatus.Skipped,
                SkipReason = "Eski (obsolete) kayıt (#~); çeviri adayı değil",
                Context = message.Context,
                Line = message.Line,
                Placeholders = PlaceholderScanResult.Empty,
                Evidence = $"PO eski kayıt (satır {message.Line})",
                FormatKey = FormatName,
                ExistingTranslation = message.Str,
                Flags = message.Flags,
                Reference = reference
            });

            return true;
        }

        if (string.IsNullOrEmpty(message.Id))
        {
            Add(result, usedKeys, new LocalizationTextEntry
            {
                Key = UniqueKey(keyBase, usedKeys),
                SourceText = message.Str ?? string.Empty,
                Kind = LocalizationTextKind.PoMessage,
                Status = LocalizationEntryStatus.Skipped,
                SkipReason = "Boş msgid; çeviri kaynağı yok",
                Context = message.Context,
                Line = message.Line,
                Placeholders = PlaceholderScanResult.Empty,
                Evidence = $"PO kaydı (satır {message.Line})",
                FormatKey = FormatName,
                Flags = message.Flags,
                Reference = reference
            });

            return true;
        }

        // Çoğul kayıt: her biçim AYRI kayıt olarak yayınlanır (sessizce birleştirilmez).
        if (message.PluralForms.Count > 0)
        {
            var forms = message.PluralForms.OrderBy(f => f.Index).ToList();
            var count = forms.Count;

            foreach (var form in forms)
            {
                if (result.Entries.Count >= maxEntries)
                {
                    result.Truncated = true;
                    return false;
                }

                var source = form.Index == 0 ? message.Id! : (message.IdPlural ?? message.Id!);

                Add(result, usedKeys, new LocalizationTextEntry
                {
                    Key = UniqueKey($"{keyBase} [{form.Index}]", usedKeys),
                    SourceText = source,
                    Kind = LocalizationTextKind.PoMessage,
                    Status = isFuzzy ? LocalizationEntryStatus.Ambiguous : LocalizationEntryStatus.Translatable,
                    SkipReason = isFuzzy ? "fuzzy işaretli (gözden geçirilmeli)" : null,
                    Context = message.Context,
                    Line = form.Line,
                    Placeholders = PlaceholderScanner.Scan(source),
                    Evidence = $"PO çoğul biçim {form.Index}/{count} (msgstr[{form.Index}], satır {form.Line})",
                    FormatKey = FormatName,
                    ExistingTranslation = form.Text,
                    Flags = message.Flags,
                    PluralIndex = form.Index,
                    PluralCount = count,
                    PluralSourceText = message.IdPlural,
                    Reference = reference
                });
            }

            return true;
        }

        // Tekil kayıt (msgid_plural var ama msgstr[n] yoksa bu bilgi kaybolmaz).
        Add(result, usedKeys, new LocalizationTextEntry
        {
            Key = UniqueKey(keyBase, usedKeys),
            SourceText = message.Id!,
            Kind = LocalizationTextKind.PoMessage,
            Status = isFuzzy ? LocalizationEntryStatus.Ambiguous : LocalizationEntryStatus.Translatable,
            SkipReason = isFuzzy ? "fuzzy işaretli (gözden geçirilmeli)" : null,
            Context = message.Context,
            Line = message.Line,
            Placeholders = PlaceholderScanner.Scan(message.Id),
            Evidence = $"PO kaydı (msgid, satır {message.Line})",
            FormatKey = FormatName,
            ExistingTranslation = message.Str ?? string.Empty,
            Flags = message.Flags,
            PluralCount = message.IdPlural is null ? null : 0,
            PluralSourceText = message.IdPlural,
            Reference = reference
        });

        return true;
    }

    private static void Add(
        LocalizationTextReadResult result,
        HashSet<string> usedKeys,
        LocalizationTextEntry entry)
    {
        _ = usedKeys;
        result.Entries.Add(entry);
    }

    /// <summary>Anahtar: <c>msgctxt + msgid</c>; satır sonları tek boşluğa indirilir.</summary>
    internal static string BuildKeyBase(PoMessage message)
    {
        var id = NormalizeInline(message.Id);

        if (id.Length == 0)
        {
            id = "(boş msgid)";
        }

        var key = message.Context is null ? id : $"[{NormalizeInline(message.Context)}] {id}";

        return key.Length > 120 ? key[..117] + "…" : key;
    }

    private static string NormalizeInline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
            }
            else
            {
                builder.Append(ch);
                lastWasSpace = false;
            }
        }

        return builder.ToString();
    }

    internal static string UniqueKey(string baseKey, HashSet<string> usedKeys)
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

/// <summary>GNU gettext <c>.po</c> ayrıştırıcısı (mevcut çeviriler dahil).</summary>
public sealed class PoLocalizationAdapter : PoLocalizationAdapterBase, ILocalizationWritableFormatAdapter
{
    public const string FormatKey = "PO";

    public override string FormatName => FormatKey;

    public override string DisplayName => "PO";

    public override string[] SupportedExtensions => new[] { ".po" };

    public override bool SupportsWrite => true;

    protected override bool IsTemplate => false;

    public Task<LocalizationWriteContentResult> BuildWriteContentAsync(
        string sourceFilePath,
        IReadOnlyList<LocalizationWriteTranslation> translations,
        LocalizationFormatWriteOptions? options = null,
        CancellationToken cancellationToken = default)
        => WritableLocalizationWriteSupport.BuildFromSourceAsync(
            sourceFilePath,
            translations,
            options,
            "PO",
            (sourceText, writeOptions) => BuildWriteContent(sourceText, translations, writeOptions),
            cancellationToken);

    private static LocalizationWriteContentResult BuildWriteContent(
        string sourceText,
        IReadOnlyList<LocalizationWriteTranslation> translations,
        LocalizationFormatWriteOptions options)
    {
        var parse = PoDocumentParser.Parse(sourceText, int.MaxValue, CancellationToken.None);

        if (parse.Messages.Count == 0)
        {
            return LocalizationWriteContentResult.Fail(
                parse.SyntaxErrorCount > 0
                    ? "PO dosyası ayrıştırılamadı; yazma reddedildi."
                    : "PO dosyasında yazılabilir kayıt bulunamadı.");
        }

        var targets = BuildTargets(parse);
        var lines = sourceText.Split('\n').ToList();
        var removedLines = new HashSet<int>();
        var changedMessages = new HashSet<PoMessage>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var applied = 0;

        foreach (var translation in translations)
        {
            if (string.IsNullOrEmpty(translation.Key))
            {
                return LocalizationWriteContentResult.Fail("PO kayıt anahtarı boş olamaz.");
            }

            if (!seenKeys.Add(translation.Key))
            {
                return LocalizationWriteContentResult.Fail(
                    $"Aynı PO kaydı birden fazla kez yazılmak isteniyor: '{translation.Key}'.");
            }

            if (!targets.TryGetValue(translation.Key, out var target))
            {
                return LocalizationWriteContentResult.Fail(
                    $"PO kaydı bulunamadı: '{translation.Key}'.");
            }

            if (!string.Equals(target.SourceText, translation.SourceText, StringComparison.Ordinal))
            {
                return LocalizationWriteContentResult.Fail(
                    $"'{translation.Key}' kaynak metni değişmiş; yazma reddedildi.");
            }

            var current = target.Form is null ? target.Message.Str ?? string.Empty : target.Form.Text;

            if (string.Equals(current, translation.Translation, StringComparison.Ordinal))
            {
                continue;
            }

            if (translation.Translation.Length == 0)
            {
                return LocalizationWriteContentResult.Fail(
                    $"'{translation.Key}' için boş çeviri yazılamaz.");
            }

            if (target.StrLine <= 0 || target.StrLine > lines.Count)
            {
                return LocalizationWriteContentResult.Fail(
                    $"'{translation.Key}' için msgstr satırı bulunamadı.");
            }

            var lineIndex = target.StrLine - 1;
            lines[lineIndex] = ReplaceMsgstrLine(lines[lineIndex], target, translation.Translation);

            var continuation = lineIndex + 1;
            while (continuation < lines.Count && IsQuotedContinuation(lines[continuation]))
            {
                removedLines.Add(continuation);
                continuation++;
            }

            changedMessages.Add(target.Message);
            applied++;
        }

        if (applied == 0)
        {
            return LocalizationWriteContentResult.Ok(sourceText);
        }

        // Yeni çeviri incelenmiş sayılır: fuzzy işaretini yalnızca değişen kayıtlardan kaldır.
        foreach (var message in changedMessages)
        {
            foreach (var flagLine in message.FlagLines)
            {
                RemoveFuzzyFlag(lines, flagLine - 1, removedLines);
            }
        }

        var output = new StringBuilder(sourceText.Length);
        var firstWritten = true;

        for (var index = 0; index < lines.Count; index++)
        {
            if (removedLines.Contains(index))
            {
                continue;
            }

            if (!firstWritten)
            {
                output.Append('\n');
            }

            output.Append(lines[index]);
            firstWritten = false;
        }

        return LocalizationWriteContentResult.Ok(output.ToString());
    }

    /// <summary>
    /// Okuma sırasındaki anahtar üretimini birebir yineler; böylece yinelenen msgid'lerde
    /// <c>#2</c> sonekleri de doğru kayda bağlanır.
    /// </summary>
    private static Dictionary<string, PoWriteTarget> BuildTargets(PoParseResult parse)
    {
        var targets = new Dictionary<string, PoWriteTarget>(StringComparer.Ordinal);
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        var headerEmitted = false;

        foreach (var message in parse.Messages)
        {
            var isHeader = !headerEmitted
                           && !message.IsMalformed
                           && !message.IsObsolete
                           && message.Context is null
                           && message.Id is { Length: 0 };

            if (isHeader)
            {
                headerEmitted = true;
                UniqueKey("PO başlık kaydı", usedKeys);
                continue;
            }

            var keyBase = BuildKeyBase(message);

            if (message.IsMalformed
                || message.IsObsolete
                || string.IsNullOrEmpty(message.Id))
            {
                UniqueKey(keyBase, usedKeys);
                continue;
            }

            if (message.PluralForms.Count > 0)
            {
                foreach (var form in message.PluralForms.OrderBy(f => f.Index))
                {
                    var key = UniqueKey($"{keyBase} [{form.Index}]", usedKeys);
                    var source = form.Index == 0
                        ? message.Id
                        : message.IdPlural ?? message.Id;

                    targets[key] = new PoWriteTarget(message, form, form.Line, source);
                }

                continue;
            }

            var singularKey = UniqueKey(keyBase, usedKeys);
            targets[singularKey] = new PoWriteTarget(message, null, message.StrLine, message.Id);
        }

        return targets;
    }




    private static string ReplaceMsgstrLine(string line, PoWriteTarget target, string translation)
    {
        var ending = line.EndsWith('\r') ? "\r" : string.Empty;
        var content = ending.Length == 0 ? line : line[..^1];
        var trimmed = content.TrimStart();
        var leading = content[..(content.Length - trimmed.Length)];
        var directive = target.Form is null ? "msgstr" : $"msgstr[{target.Form.Index}]";

        return leading + directive + " " + QuotePo(translation) + ending;
    }

    /// <summary>Bir önceki direktife ait tırnaklı devam satırı mı?</summary>
    private static bool IsQuotedContinuation(string line)
        => line.TrimStart().StartsWith('"');

    /// <summary>
    /// Değişen çevirinin fuzzy işaretini kaldırır; aynı satırdaki diğer flag'ler korunur.
    /// </summary>
    private static void RemoveFuzzyFlag(List<string> lines, int index, HashSet<int> removedLines)
    {
        if (index < 0 || index >= lines.Count || removedLines.Contains(index))
        {
            return;
        }

        var line = lines[index];
        var ending = line.EndsWith('\r') ? "\r" : string.Empty;
        var content = ending.Length == 0 ? line : line[..^1];
        var trimmed = content.TrimStart();

        if (!trimmed.StartsWith("#,", StringComparison.Ordinal))
        {
            return;
        }

        var marker = content.IndexOf("#,", StringComparison.Ordinal);
        var parts = content[(marker + 2)..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!parts.Any(part => string.Equals(part, "fuzzy", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var remaining = parts
            .Where(part => !string.Equals(part, "fuzzy", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (remaining.Length == 0)
        {
            removedLines.Add(index);
            return;
        }

        lines[index] = content[..(marker + 2)] + " " + string.Join(", ", remaining) + ending;
    }

    private static string QuotePo(string value)
        => "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) + "\"";

    private sealed record PoWriteTarget(
        PoMessage Message,
        PoPluralForm? Form,
        int StrLine,
        string SourceText);
}

/// <summary>
/// GNU gettext <c>.pot</c> şablon ayrıştırıcısı. Aynı motoru kullanır; boş <c>msgstr</c>
/// alanları hata değildir.
/// </summary>
public sealed class PotLocalizationAdapter : PoLocalizationAdapterBase
{
    public const string FormatKey = "POT";

    public override string FormatName => FormatKey;

    public override string DisplayName => "POT";

    public override string[] SupportedExtensions => new[] { ".pot" };

    protected override bool IsTemplate => true;
}






