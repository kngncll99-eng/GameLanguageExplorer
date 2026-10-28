namespace GameLanguageExplorer.Core.Services.Steam;

using GameLanguageExplorer.Core.Interfaces;

/// <summary>
/// Yönetici izni gerektirmeyen, kayıt defteri kullanmayan varsayılan Steam kök bulucu:
/// <c>STEAM_ROOT</c> ortam değişkeni + yaygın kurulum yolları.
/// </summary>
public sealed class DefaultSteamRootLocator : ISteamRootLocator
{
    public const string SteamRootEnvironmentVariable = "STEAM_ROOT";

    public IReadOnlyList<string> GetCandidateSteamRoots()
    {
        var roots = new List<string>();

        var fromEnvironment = Environment.GetEnvironmentVariable(SteamRootEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            roots.Add(fromEnvironment.Trim());
        }

        AddIfNotBlank(roots, SafeFolderPath(Environment.SpecialFolder.ProgramFilesX86, "Steam"));
        AddIfNotBlank(roots, SafeFolderPath(Environment.SpecialFolder.ProgramFiles, "Steam"));
        AddIfNotBlank(roots, @"C:\Steam");

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? SafeFolderPath(Environment.SpecialFolder folder, string child)
    {
        try
        {
            var basePath = Environment.GetFolderPath(folder);

            return string.IsNullOrWhiteSpace(basePath) ? null : Path.Combine(basePath, child);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static void AddIfNotBlank(ICollection<string> list, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            list.Add(value);
        }
    }
}
