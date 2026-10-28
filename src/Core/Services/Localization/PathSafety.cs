namespace GameLanguageExplorer.Core.Services.Localization;

/// <summary>Dosya sistemi yolları için güvenlik yardımcıları (kök dizin dışına çıkmayı engeller).</summary>
public static class PathSafety
{
    /// <summary>
    /// Yolu mutlak biçime getirir ve sondaki ayırıcıları temizler. Geçersizse <c>null</c>.
    /// </summary>
    public static string? TryNormalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path.Trim());
            var root = Path.GetPathRoot(full);

            if (!string.IsNullOrEmpty(root) && full.Length > root.Length)
            {
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// <paramref name="candidatePath"/> gerçekten <paramref name="rootPath"/> altında mı?
    /// <c>C:\Game2</c>, <c>C:\Game</c> altında sayılmaz (ayırıcı sınırı kontrol edilir).
    /// </summary>
    public static bool IsUnderRoot(string candidatePath, string rootPath)
    {
        var root = TryNormalize(rootPath);
        var candidate = TryNormalize(candidatePath);

        if (root is null || candidate is null)
        {
            return false;
        }

        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Kök klasöre göre göreli klasör yolunu döndürür (dosya adı dahil değil).</summary>
    public static string? GetRelativeDirectory(string filePath, string rootPath)
    {
        var root = TryNormalize(rootPath);
        var candidate = TryNormalize(filePath);

        if (root is null || candidate is null || !IsUnderRoot(candidate, root))
        {
            return null;
        }

        var relative = candidate[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directory = Path.GetDirectoryName(relative);

        return string.IsNullOrEmpty(directory) ? null : directory;
    }
}
