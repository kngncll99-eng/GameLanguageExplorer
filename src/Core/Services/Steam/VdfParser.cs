namespace GameLanguageExplorer.Core.Services.Steam;

using System.Text;

/// <summary>
/// Valve KeyValues (VDF) biçimi için küçük, toleranslı bir okuyucu.
/// <c>libraryfolders.vdf</c> ve <c>appmanifest_*.acf</c> dosyalarını ayrıştırmak için kullanılır.
/// </summary>
public static class VdfParser
{
    /// <summary>
    /// Metni iç içe sözlüklere ayrıştırır. Değerler ya <see cref="string"/> ya da
    /// <c>Dictionary&lt;string, object&gt;</c> olur.
    /// </summary>
    /// <exception cref="FormatException">Girdi bozuk/bitmemiş blok içeriyorsa.</exception>
    public static Dictionary<string, object> Parse(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var tokens = Tokenize(text);
        var index = 0;
        var result = ParseBlock(tokens, ref index, expectClosingBrace: false);

        if (index != tokens.Count)
        {
            throw new FormatException("VDF içeriğinde beklenmeyen fazladan simge var.");
        }

        return result;
    }

    /// <summary>Dosyayı okur; dosya yoksa veya bozuksa <see cref="FormatException"/> fırlatır.</summary>
    public static Dictionary<string, object> ParseFile(string filePath)
        => Parse(File.ReadAllText(filePath, Encoding.UTF8));

    private static Dictionary<string, object> ParseBlock(List<string> tokens, ref int index, bool expectClosingBrace)
    {
        var block = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        while (index < tokens.Count)
        {
            var token = tokens[index];

            if (token == "}")
            {
                index++;

                if (!expectClosingBrace)
                {
                    throw new FormatException("VDF içeriğinde beklenmeyen '}' simgesi.");
                }

                return block;
            }

            if (token == "{")
            {
                throw new FormatException("VDF içeriğinde anahtarsız '{' simgesi.");
            }

            var key = token;
            index++;

            if (index >= tokens.Count)
            {
                throw new FormatException($"VDF içeriği '{key}' anahtarı için değer içermiyor.");
            }

            var next = tokens[index];

            if (next == "{")
            {
                index++;
                block[key] = ParseBlock(tokens, ref index, expectClosingBrace: true);
                continue;
            }

            if (next == "}")
            {
                throw new FormatException($"VDF içeriğinde '{key}' anahtarının değeri eksik.");
            }

            block[key] = next;
            index++;
        }

        if (expectClosingBrace)
        {
            throw new FormatException("VDF içeriği beklenen '}' ile bitmiyor.");
        }

        return block;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var index = 0;

        void Flush()
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        while (index < text.Length)
        {
            var ch = text[index];

            // Yorumlar: // satır sonuna kadar
            if (ch == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                while (index < text.Length && text[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                Flush();
                index++;
                continue;
            }

            if (ch == '{' || ch == '}')
            {
                Flush();
                tokens.Add(ch.ToString());
                index++;
                continue;
            }

            if (ch == '"')
            {
                Flush();
                index++;
                var value = new StringBuilder();

                while (index < text.Length)
                {
                    var c = text[index];

                    if (c == '\\' && index + 1 < text.Length)
                    {
                        var escaped = text[index + 1];
                        value.Append(escaped switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            '\\' => '\\',
                            '"' => '"',
                            _ => escaped
                        });

                        index += 2;
                        continue;
                    }

                    if (c == '"')
                    {
                        index++;
                        break;
                    }

                    value.Append(c);
                    index++;
                }

                tokens.Add(value.ToString());
                continue;
            }

            // Tırnaksız değer (eski VDF biçimlerinde görülebilir)
            current.Append(ch);
            index++;
        }

        Flush();
        return tokens;
    }
}
