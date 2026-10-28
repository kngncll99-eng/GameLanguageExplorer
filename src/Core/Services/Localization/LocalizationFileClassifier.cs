namespace GameLanguageExplorer.Core.Services.Localization;

/// <summary>Bir adayın neden seçildiğini açıklayan tek kanıt.</summary>
public sealed record LocalizationEvidenceItem(
    string EvidenceType,
    string EvidenceValue,
    double Confidence,
    string? MatchedPattern);

/// <summary>Bir dosyanın yerelleştirme adayı olarak sınıflandırma sonucu.</summary>
public sealed record LocalizationCandidateClassification(
    LocalizationFormatInfo Format,
    string Extension,
    string Confidence,
    string Reason,
    IReadOnlyList<LocalizationEvidenceItem> Evidence)
{
    /// <summary>Paket/arşiv mi? (asla düz metin sayılmaz)</summary>
    public bool IsPackage => Format.Kind == LocalizationFormatKind.Package;

    /// <summary>Uzantı dışında bir bağlam ipucu eşleşti mi?</summary>
    public bool HasContextHint => Evidence.Any(e =>
        e.EvidenceType is LocalizationEvidenceTypes.FileName or LocalizationEvidenceTypes.FolderName);

    /// <summary>Gerçekten ayrıştırılabilir mi? (bu aşamada hiçbir format için "evet" değil)</summary>
    public LocalizationSupportStatus SupportStatus
        => LocalizationFormatCatalog.GetSupportStatus(Format.Key, adapterUsed: null);
}

/// <summary>Kanıt türleri (<c>FormatEvidence.EvidenceType</c> alanına yazılır).</summary>
public static class LocalizationEvidenceTypes
{
    public const string Extension = "Extension";
    public const string FileName = "FileName";
    public const string FolderName = "FolderName";
}

/// <summary>
/// Dosyaları yerelleştirme adayı olarak sınıflandırır.
///
/// <list type="bullet">
/// <item>Uzantı eşleşmesi <b>kesin destek değildir</b>; tek başına güven en fazla <c>Low</c>'dur.</item>
/// <item>Dosya adı ve/veya klasör yolundaki ipuçları güveni yükseltir (Medium/High).</item>
/// <item>PAK/BUNDLE/ASSETS/UASSET/BANK gibi paketler <b>hiçbir zaman</b> metin sayılmaz.</item>
/// <item>Bilinmeyen uzantılar aday değildir (<c>null</c>).</item>
/// </list>
/// </summary>
public static class LocalizationFileClassifier
{
    private enum HintMatchMode
    {
        /// <summary>Yalnızca tam eşleşme.</summary>
        Exact,

        /// <summary>Tam eşleşme veya önek.</summary>
        ExactOrPrefix,

        /// <summary>Tam eşleşme, önek veya sonek.</summary>
        Any
    }

    private sealed record Hint(string Token, HintMatchMode Mode);

    // "text"/"string" yalnızca TAM eşleşir; aksi halde "texture"/"substring" gibi
    // klasör adları yanlış pozitif üretirdi.
    private static readonly Hint[] _hints =
    {
        new("localization", HintMatchMode.Any),
        new("localisation", HintMatchMode.Any),
        new("localized", HintMatchMode.Any),
        new("localised", HintMatchMode.Any),
        new("language", HintMatchMode.Any),
        new("languages", HintMatchMode.Any),
        new("locale", HintMatchMode.Any),
        new("locales", HintMatchMode.Any),
        new("translation", HintMatchMode.Any),
        new("translations", HintMatchMode.Any),
        new("stringtable", HintMatchMode.Any),
        new("stringtables", HintMatchMode.Any),
        new("text", HintMatchMode.Exact),
        new("texts", HintMatchMode.Exact),
        new("string", HintMatchMode.Exact),
        new("strings", HintMatchMode.Exact),
        new("i18n", HintMatchMode.Any),
        new("l10n", HintMatchMode.Any)
    };

    /// <summary>Aranan ipucu sözcükleri.</summary>
    public static IReadOnlyList<string> HintTokens => _hints.Select(h => h.Token).ToArray();

    /// <summary>Dosyayı sınıflandırır; aday değilse <c>null</c>.</summary>
    /// <param name="filePath">Dosyanın tam yolu.</param>
    /// <param name="relativePath">Kök klasöre göre göreli klasör yolu (ör. <c>Localization\Text</c>).</param>
    public static LocalizationCandidateClassification? Classify(string filePath, string? relativePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var extension = GetExtension(filePath);
        var format = LocalizationFormatCatalog.FindByExtension(extension);

        if (format is null)
        {
            return null;
        }

        var fileName = GetFileName(filePath);
        var nameStem = StripExtension(fileName);
        var folderSegments = GetFolderSegments(filePath, relativePath);

        var nameMatches = FindHints(nameStem);
        var folderMatches = folderSegments.SelectMany(FindHints).ToList();

        var evidence = new List<LocalizationEvidenceItem>
        {
            new(LocalizationEvidenceTypes.Extension, extension, 0.25, extension)
        };

        foreach (var match in nameMatches.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            evidence.Add(new LocalizationEvidenceItem(LocalizationEvidenceTypes.FileName, fileName, 0.35, match));
        }

        foreach (var match in folderMatches.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            evidence.Add(new LocalizationEvidenceItem(
                LocalizationEvidenceTypes.FolderName,
                string.Join('\\', folderSegments),
                0.35,
                match));
        }

        var score = (nameMatches.Count > 0 ? 1 : 0) + (folderMatches.Count > 0 ? 1 : 0);

        var confidence = score >= 2
            ? LocalizationFormatCatalog.ConfidenceHigh
            : score == 1
                ? LocalizationFormatCatalog.ConfidenceMedium
                : LocalizationFormatCatalog.ConfidenceLow;

        return new LocalizationCandidateClassification(
            format,
            extension,
            confidence,
            BuildReason(format, extension, nameMatches, folderMatches),
            evidence);
    }

    private static string BuildReason(
        LocalizationFormatInfo format,
        string extension,
        IReadOnlyList<string> nameMatches,
        IReadOnlyList<string> folderMatches)
    {
        if (format.Kind == LocalizationFormatKind.Package)
        {
            var hintText = nameMatches.Count > 0
                ? $" Dosya adı ipucu: '{nameMatches[0]}'."
                : folderMatches.Count > 0
                    ? $" Klasör ipucu: '{folderMatches[0]}'."
                    : " Dosya/klasör adında ipucu yok.";

            return $"Paket/arşiv dosyası ({extension}); metin olarak ayrıştırılmaz, ayrıştırıcı yok.{hintText}";
        }

        if (format.Kind == LocalizationFormatKind.Binary)
        {
            var hintText = nameMatches.Count > 0 || folderMatches.Count > 0
                ? " Dosya/klasör adı yerelleştirme ile ilişkili."
                : " Yalnızca uzantı eşleşmesi.";

            return $"İkili yerelleştirme kataloğu ({extension}); ayrıştırıcı yok.{hintText}";
        }

        if (nameMatches.Count > 0 && folderMatches.Count > 0)
        {
            return $"Dosya adı ('{nameMatches[0]}') ve klasör yolu ('{folderMatches[0]}') yerelleştirme ipucu içeriyor; "
                   + $"uzantı {extension}. İçerik doğrulanmadı.";
        }

        if (nameMatches.Count > 0)
        {
            return $"Dosya adı yerelleştirme ipucu içeriyor ('{nameMatches[0]}'); uzantı {extension}. İçerik doğrulanmadı.";
        }

        if (folderMatches.Count > 0)
        {
            return $"Klasör yolu yerelleştirme ipucu içeriyor ('{folderMatches[0]}'); uzantı {extension}. İçerik doğrulanmadı.";
        }

        return $"Yalnızca uzantı eşleşmesi ({extension}); bağlam ve içerik doğrulanmadı.";
    }

    private static List<string> FindHints(string? text)
    {
        var matches = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return matches;
        }

        foreach (var segment in SplitSegments(text))
        {
            foreach (var hint in _hints)
            {
                if (Matches(segment, hint))
                {
                    matches.Add(hint.Token);
                    break;
                }
            }
        }

        return matches;
    }

    private static bool Matches(string segment, Hint hint)
    {
        if (segment.Equals(hint.Token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (hint.Mode == HintMatchMode.Exact)
        {
            return false;
        }

        if (segment.StartsWith(hint.Token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return hint.Mode == HintMatchMode.Any
               && segment.EndsWith(hint.Token, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Metni harf/rakam dışı karakterlerden bölerek sözcüklere ayırır.</summary>
    private static IEnumerable<string> SplitSegments(string text)
    {
        var buffer = new System.Text.StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer.Append(ch);
            }
            else if (buffer.Length > 0)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    private static IReadOnlyList<string> GetFolderSegments(string filePath, string? relativePath)
    {
        var source = relativePath;

        if (string.IsNullOrWhiteSpace(source))
        {
            try
            {
                source = Path.GetFileName(Path.GetDirectoryName(filePath));
            }
            catch (ArgumentException)
            {
                source = null;
            }
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return Array.Empty<string>();
        }

        return source.TrimEnd('\\', '/')
            .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string GetExtension(string path)
    {
        try
        {
            return Path.GetExtension(path) ?? string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static string GetFileName(string path)
    {
        try
        {
            return Path.GetFileName(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static string StripExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');

        return dot > 0 ? fileName[..dot] : fileName;
    }
}



