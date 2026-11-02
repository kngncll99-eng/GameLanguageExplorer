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

    /// <summary>
    /// Yolun kendisi ve tüm ara klasörlerindeki junction/symbolic link hedeflerini çözer.
    /// Çözülemeyen bağlantı veya okunamayan yol için <c>null</c> döner.
    /// </summary>
    public static string? ResolveFinalPath(string path)
    {
        var full = TryNormalize(path);

        if (full is null)
        {
            return null;
        }

        try
        {
            var root = Path.GetPathRoot(full) ?? string.Empty;
            var current = root;

            foreach (var segment in full[root.Length..]
                         .Split(
                             new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                             StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);

                if (Directory.Exists(current))
                {
                    var directory = new DirectoryInfo(current);

                    if (directory.LinkTarget is not null)
                    {
                        var target = directory.ResolveLinkTarget(returnFinalTarget: true);

                        if (target is null)
                        {
                            return null;
                        }

                        current = Path.GetFullPath(target.FullName);
                    }
                }
                else if (File.Exists(current))
                {
                    var file = new FileInfo(current);

                    if (file.LinkTarget is not null)
                    {
                        var target = file.ResolveLinkTarget(returnFinalTarget: true);

                        if (target is null)
                        {
                            return null;
                        }

                        current = Path.GetFullPath(target.FullName);
                    }
                }
            }

            return TryNormalize(current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Metinsel kök kontrolünden sonra bağlantıları da çözerek hedefin gerçekten
    /// kökün içinde kaldığını doğrular. Ara klasör junction/symlink'leri kapsanır.
    /// </summary>
    public static bool IsUnderRootResolved(string candidatePath, string rootPath)
    {
        if (!IsUnderRoot(candidatePath, rootPath))
        {
            return false;
        }

        var resolvedRoot = ResolveFinalPath(rootPath);
        var resolvedCandidate = ResolveFinalPath(candidatePath);

        return resolvedRoot is not null
               && resolvedCandidate is not null
               && IsUnderRoot(resolvedCandidate, resolvedRoot);
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
