namespace GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>Metin içinde bulunan değişken/biçimlendirme işaretinin türü.</summary>
public enum PlaceholderKind
{
    /// <summary><c>{playerName}</c> gibi adlandırılmış süslü parantez.</summary>
    Named = 0,

    /// <summary><c>{0}</c> gibi konumsal süslü parantez.</summary>
    Positional = 1,

    /// <summary><c>%s</c>, <c>%d</c>, <c>%1$s</c>, <c>%%</c> gibi printf işaretleri.</summary>
    Printf = 2,

    /// <summary><c>\n</c>, <c>\t</c>, <c>\\</c> gibi kaçış dizileri.</summary>
    Escape = 3,

    /// <summary><c>&lt;b&gt;</c>, <c>&lt;/b&gt;</c>, <c>&lt;color=#fff&gt;</c> gibi etiketler.</summary>
    Tag = 4,

    /// <summary><c>[b]</c>, <c>[/b]</c>, <c>[color=red]</c> gibi köşeli parantezli etiketler.</summary>
    BracketTag = 5
}

/// <summary>Metinde bulunan tek bir işaret ve konumu.</summary>
public sealed record PlaceholderToken(PlaceholderKind Kind, string Value, int Start, int Length);

/// <summary>
/// Bir metindeki korunması gereken işaretler. <see cref="Signature"/> ileride çeviri sonrası
/// metinle karşılaştırma yapmak için kullanılabilir (aynı imza ⇒ aynı işaretler).
/// </summary>
public sealed record PlaceholderScanResult(IReadOnlyList<PlaceholderToken> Tokens)
{
    public static PlaceholderScanResult Empty { get; } = new(Array.Empty<PlaceholderToken>());

    public bool HasPlaceholders => Tokens.Count > 0;

    /// <summary>Sıra korumalı imza: <c>{playerName}|{0}|%s</c>.</summary>
    public string Signature => string.Join("|", Tokens.Select(t => t.Value));

    /// <summary>Tür bilgisiyle imza (daha katı karşılaştırma).</summary>
    public string TypedSignature => string.Join("|", Tokens.Select(t => $"{t.Kind}:{t.Value}"));

    /// <summary>UI'da gösterilecek kısa liste.</summary>
    public string Display => string.Join(" ", Tokens.Select(t => t.Value));
}

/// <summary>
/// Metinlerdeki değişkenleri ve biçimlendirme etiketlerini <b>tanır</b>; anlamını tahmin etmez.
/// Konumları ve imzayı kaydeder, böylece ileride çeviri sonrası karşılaştırma yapılabilir.
/// </summary>
public static class PlaceholderScanner
{
    /// <summary>Tek geçişte, çakışmayan işaretler üretir.</summary>
    public static PlaceholderScanResult Scan(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return PlaceholderScanResult.Empty;
        }

        var tokens = new List<PlaceholderToken>();
        var index = 0;

        while (index < text.Length)
        {
            var ch = text[index];
            var length = 0;

            if (ch == '<')
            {
                length = MatchTag(text, index);

                if (length > 0)
                {
                    tokens.Add(new PlaceholderToken(PlaceholderKind.Tag, text.Substring(index, length), index, length));
                }
            }
            else if (ch == '[')
            {
                length = MatchBracketTag(text, index);

                if (length > 0)
                {
                    tokens.Add(new PlaceholderToken(PlaceholderKind.BracketTag, text.Substring(index, length), index, length));
                }
            }
            else if (ch == '\\')
            {
                length = MatchEscape(text, index);

                if (length > 0)
                {
                    tokens.Add(new PlaceholderToken(PlaceholderKind.Escape, text.Substring(index, length), index, length));
                }
            }
            else if (ch == '{')
            {
                length = MatchBrace(text, index, out var kind);

                if (length > 0)
                {
                    tokens.Add(new PlaceholderToken(kind, text.Substring(index, length), index, length));
                }
            }
            else if (ch == '%')
            {
                length = MatchPrintf(text, index);

                if (length > 0)
                {
                    tokens.Add(new PlaceholderToken(PlaceholderKind.Printf, text.Substring(index, length), index, length));
                }
            }

            index += length > 0 ? length : 1;
        }

        return tokens.Count == 0 ? PlaceholderScanResult.Empty : new PlaceholderScanResult(tokens);
    }

    /// <summary><c>&lt;b&gt;</c>, <c>&lt;/b&gt;</c>, <c>&lt;br/&gt;</c>, <c>&lt;color=#fff&gt;</c>.</summary>
    private static int MatchTag(string text, int start)
    {
        var i = start + 1;

        if (i < text.Length && text[i] == '/')
        {
            i++;
        }

        if (i >= text.Length || !char.IsLetter(text[i]))
        {
            return 0;
        }

        while (i < text.Length && text[i] != '>' && text[i] != '<')
        {
            i++;
        }

        if (i >= text.Length || text[i] != '>')
        {
            return 0;
        }

        var length = i - start + 1;

        return length > 100 ? 0 : length;
    }

    /// <summary><c>[b]</c>, <c>[/b]</c>, <c>[color=red]</c>.</summary>
    private static int MatchBracketTag(string text, int start)
    {
        var i = start + 1;

        if (i < text.Length && text[i] == '/')
        {
            i++;
        }

        if (i >= text.Length || !char.IsLetter(text[i]))
        {
            return 0;
        }

        while (i < text.Length && text[i] != ']' && text[i] != '[' && text[i] != '\n')
        {
            i++;
        }

        if (i >= text.Length || text[i] != ']')
        {
            return 0;
        }

        var length = i - start + 1;

        return length > 100 ? 0 : length;
    }

    /// <summary><c>\n</c>, <c>\t</c>, <c>\r</c>, <c>\\</c>, <c>\"</c>, <c>\uXXXX</c>.</summary>
    private static int MatchEscape(string text, int start)
    {
        if (start + 1 >= text.Length)
        {
            return 0;
        }

        var next = text[start + 1];

        if (next is 'n' or 't' or 'r' or '\\' or '"' or '\'' or '0' or 'a' or 'b' or 'f' or 'v')
        {
            return 2;
        }

        if (next == 'u' && start + 6 <= text.Length)
        {
            for (var i = start + 2; i < start + 6; i++)
            {
                if (!Uri.IsHexDigit(text[i]))
                {
                    return 0;
                }
            }

            return 6;
        }

        return 0;
    }

    /// <summary><c>{playerName}</c> veya <c>{0}</c> (iç içe süslü parantez yok).</summary>
    private static int MatchBrace(string text, int start, out PlaceholderKind kind)
    {
        kind = PlaceholderKind.Named;

        var end = text.IndexOf('}', start + 1);

        if (end < 0)
        {
            return 0;
        }

        var length = end - start + 1;

        if (length < 3 || length > 64)
        {
            return 0;
        }

        var inner = text.Substring(start + 1, length - 2);

        if (inner.Length == 0 || inner.Contains('{') || inner.Contains('\n') || inner[0] == '{')
        {
            return 0;
        }

        kind = inner.All(char.IsDigit) || IsPositionalFormat(inner)
            ? PlaceholderKind.Positional
            : PlaceholderKind.Named;

        return length;
    }

    private static bool IsPositionalFormat(string inner)
    {
        // {0:N2} gibi biçim belirteçli konumsal alanlar.
        var separator = inner.IndexOf(':');

        return separator > 0
               && inner[..separator].All(char.IsDigit)
               && inner[(separator + 1)..].Length > 0;
    }

    /// <summary><c>%s</c>, <c>%d</c>, <c>%1$s</c>, <c>%02d</c>, <c>%%</c>.</summary>
    private static int MatchPrintf(string text, int start)
    {
        var i = start + 1;

        if (i >= text.Length)
        {
            return 0;
        }

        // %% kaçışı
        if (text[i] == '%')
        {
            return 2;
        }

        // %1$s biçimi
        var digitStart = i;

        while (i < text.Length && char.IsDigit(text[i]))
        {
            i++;
        }

        if (i > digitStart && i < text.Length && text[i] == '$')
        {
            i++;
        }
        else
        {
            i = digitStart;
        }

        // Bayraklar. Boşluk bilinçli olarak bayrak sayılmaz: "% emin" gibi doğal metinler
        // yanlışlıkla printf sanılmasın.
        while (i < text.Length && "-+0#'".Contains(text[i]))
        {
            i++;
        }

        // Genişlik
        while (i < text.Length && char.IsDigit(text[i]))
        {
            i++;
        }

        // Hassasiyet
        if (i + 1 < text.Length && text[i] == '.' && char.IsDigit(text[i + 1]))
        {
            i++;

            while (i < text.Length && char.IsDigit(text[i]))
            {
                i++;
            }
        }

        if (i >= text.Length || !"sdifxucXeEgGopq".Contains(text[i]))
        {
            return 0;
        }

        return i - start + 1;
    }
}


