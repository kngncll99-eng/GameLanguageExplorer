using System.IO;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Gog;
using Microsoft.Win32;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>
/// GOG kurulumlarını önce Windows kayıt defterinden, sonra bilinen GOG klasörlerinden
/// salt okunur olarak toplar. Erişilemeyen anahtarlar taramayı durdurmaz.
/// </summary>
public sealed class RegistryGogInstallationLocator : IGogInstallationLocator
{
    private static readonly string[] RegistryGamePaths =
    {
        @"SOFTWARE\WOW6432Node\GOG.com\Games",
        @"SOFTWARE\GOG.com\Games"
    };

    private readonly IGogInstallationLocator _fallback = new DefaultGogInstallationLocator();

    public IReadOnlyList<GogInstallationCandidate> GetInstalledCandidates()
    {
        var candidates = new List<GogInstallationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var path in RegistryGamePaths)
            {
                ReadRegistryGames(hive, path, candidates, seen);
            }
        }

        foreach (var storageRoot in GetStorageDirectories())
        {
            foreach (var stored in GogStorageManifestReader.ReadFromStorage(storageRoot))
            {
                if (seen.Add(NormalizePath(stored.InstallPath)))
                {
                    candidates.Add(stored);
                }
            }
        }

        foreach (var fallback in _fallback.GetInstalledCandidates())
        {
            if (seen.Add(NormalizePath(fallback.InstallPath)))
            {
                candidates.Add(fallback);
            }
        }

        return candidates;
    }

    private static IReadOnlyList<string> GetStorageDirectories()
    {
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("PROGRAMDATA"),
            Environment.GetEnvironmentVariable("LOCALAPPDATA"),
            Environment.GetEnvironmentVariable("APPDATA"),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        return roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.Combine(root!, @"GOG.com\Galaxy\storage"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ReadRegistryGames(
        RegistryKey hive,
        string subKeyPath,
        ICollection<GogInstallationCandidate> candidates,
        ISet<string> seen)
    {
        try
        {
            using var gamesKey = hive.OpenSubKey(subKeyPath);

            if (gamesKey is null)
            {
                return;
            }

            foreach (var keyName in gamesKey.GetSubKeyNames())
            {
                try
                {
                    using var gameKey = gamesKey.OpenSubKey(keyName);

                    if (gameKey is null)
                    {
                        continue;
                    }

                    var installPath = GetString(gameKey, "path", "installPath", "installLocation");

                    if (string.IsNullOrWhiteSpace(installPath))
                    {
                        continue;
                    }

                    var normalized = NormalizePath(installPath);

                    if (!seen.Add(normalized))
                    {
                        continue;
                    }

                    var gameId = GetString(gameKey, "gameID", "gameId", "id") ?? keyName;
                    var gameName = GetString(gameKey, "gameName", "name", "title");

                    candidates.Add(new GogInstallationCandidate(
                        gameId,
                        gameName,
                        installPath,
                        GetString(gameKey, "exe", "executable", "launchCommand")));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // Tek bir oyun anahtarı okunamazsa diğerleri taranmaya devam eder.
                }
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or System.Security.SecurityException
                                          or PlatformNotSupportedException)
        {
            // Registry bu makinede/izinde okunamıyor; fallback klasörler yine denenir.
        }
    }

    private static string? GetString(RegistryKey key, params string[] valueNames)
    {
        foreach (var valueName in valueNames)
        {
            if (key.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string NormalizePath(string? path)
        => (path ?? string.Empty).Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
