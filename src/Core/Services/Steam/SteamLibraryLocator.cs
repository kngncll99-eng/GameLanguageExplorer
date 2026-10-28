namespace GameLanguageExplorer.Core.Services.Steam;

using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// Steam kök klasörlerinden oyun kütüphanelerini (<c>steamapps</c>) bulur.
/// <c>steamapps/libraryfolders.vdf</c> ek kütüphaneleri listeler.
/// </summary>
public static class SteamLibraryLocator
{
    public const string LibraryFoldersFileName = "libraryfolders.vdf";

    /// <summary>
    /// Verilen köklerin kendi <c>steamapps</c> klasörünü ve <c>libraryfolders.vdf</c> içinde
    /// tanımlı ek kütüphaneleri döndürür. Yalnızca gerçekten var olan yollar döner.
    /// </summary>
    /// <param name="steamRoots">Aday Steam kök klasörleri.</param>
    /// <param name="warnings">Erişim/biçim hatalarının ekleneceği liste (opsiyonel).</param>
    public static IReadOnlyList<string> FindSteamAppsPaths(
        IEnumerable<string> steamRoots,
        ICollection<string>? warnings = null)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? steamAppsPath)
        {
            if (string.IsNullOrWhiteSpace(steamAppsPath))
            {
                return;
            }

            var normalized = NormalizePath(steamAppsPath);

            if (seen.Add(normalized))
            {
                results.Add(normalized);
            }
        }

        foreach (var root in steamRoots ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string steamApps;

            try
            {
                steamApps = Path.Combine(NormalizePath(root), "steamapps");
            }
            catch (ArgumentException ex)
            {
                warnings?.Add($"Geçersiz Steam yolu '{root}': {ex.Message}");
                continue;
            }

            if (!SafeDirectoryExists(steamApps, warnings))
            {
                continue;
            }

            Add(steamApps);
            AddRangeFromLibraryFolders(steamApps, Add, warnings);
        }

        return results;
    }

    private static void AddRangeFromLibraryFolders(
        string steamAppsPath,
        Action<string?> add,
        ICollection<string>? warnings)
    {
        var vdfPath = Path.Combine(steamAppsPath, LibraryFoldersFileName);

        if (!SafeFileExists(vdfPath))
        {
            return;
        }

        Dictionary<string, object> parsed;

        try
        {
            parsed = VdfParser.ParseFile(vdfPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            warnings?.Add($"'{vdfPath}' okunamadı: {ex.Message}");
            return;
        }

        if (!parsed.TryGetValue("libraryfolders", out var foldersValue) ||
            foldersValue is not Dictionary<string, object> folders)
        {
            warnings?.Add($"'{vdfPath}' içinde 'libraryfolders' bloğu yok.");
            return;
        }

        foreach (var entry in folders)
        {
            // Yeni biçim: "0" { "path" "D:\\SteamLibrary" ... }
            if (entry.Value is Dictionary<string, object> libraryBlock)
            {
                if (libraryBlock.TryGetValue("path", out var pathValue) && pathValue is string path && !string.IsNullOrWhiteSpace(path))
                {
                    var librarySteamApps = Path.Combine(NormalizePath(path), "steamapps");

                    if (SafeDirectoryExists(librarySteamApps, warnings))
                    {
                        add(librarySteamApps);
                    }
                    else
                    {
                        warnings?.Add($"Kütüphane yolu bulunamadı, atlandı: '{librarySteamApps}'");
                    }
                }

                continue;
            }

            // Eski biçim: "1" "D:\\SteamLibrary"
            if (entry.Value is string directPath && !string.IsNullOrWhiteSpace(directPath) && directPath.Contains(Path.DirectorySeparatorChar))
            {
                var librarySteamApps = Path.Combine(NormalizePath(directPath), "steamapps");

                if (SafeDirectoryExists(librarySteamApps, warnings))
                {
                    add(librarySteamApps);
                }
            }
        }
    }

    private static string NormalizePath(string path)
        => path.Trim().TrimEnd('"').Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);

    private static bool SafeDirectoryExists(string path, ICollection<string>? warnings)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            warnings?.Add($"Klasöre erişilemedi '{path}': {ex.Message}");
            return false;
        }
    }

    private static bool SafeFileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
