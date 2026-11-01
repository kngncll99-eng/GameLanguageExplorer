using System.IO;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Ea;
using Microsoft.Win32;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>
/// EA App/Origin kurulumlarını registry, InstallData/config klasörleri ve bilinen
/// oyun klasörlerinden salt okunur olarak toplar.
/// </summary>
public sealed class RegistryEaInstallationLocator : IEaInstallationLocator
{
    private static readonly string[] RegistryGamePaths =
    {
        @"SOFTWARE\WOW6432Node\EA Games",
        @"SOFTWARE\Electronic Arts\EA Games",
        @"SOFTWARE\Origin",
        @"SOFTWARE\WOW6432Node\Origin"
    };

    private readonly IEaInstallationLocator _fallback = new DefaultEaInstallationLocator();

    public IReadOnlyList<EaInstallationCandidate> GetInstalledCandidates()
    {
        var candidates = new List<EaInstallationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var path in RegistryGamePaths)
            {
                ReadRegistryGames(hive, path, candidates, seen);
            }
        }

        foreach (var configRoot in GetConfigDirectories())
        {
            foreach (var candidate in EaConfigManifestReader.ReadFromDirectory(configRoot))
            {
                if (seen.Add(NormalizePath(candidate.InstallPath)))
                {
                    candidates.Add(candidate);
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

    private static void ReadRegistryGames(
        RegistryKey hive,
        string subKeyPath,
        ICollection<EaInstallationCandidate> candidates,
        ISet<string> seen)
    {
        try
        {
            using var rootKey = hive.OpenSubKey(subKeyPath);

            if (rootKey is null)
            {
                return;
            }

            foreach (var keyName in rootKey.GetSubKeyNames())
            {
                try
                {
                    using var gameKey = rootKey.OpenSubKey(keyName);

                    if (gameKey is null)
                    {
                        continue;
                    }

                    var installPath = GetString(gameKey, "Install Dir", "InstallDir", "InstallLocation", "InstallPath", "Path");

                    if (string.IsNullOrWhiteSpace(installPath))
                    {
                        continue;
                    }

                    var normalized = NormalizePath(installPath);

                    if (!seen.Add(normalized))
                    {
                        continue;
                    }

                    candidates.Add(new EaInstallationCandidate(
                        GetString(gameKey, "ContentID", "ContentId", "content_id") ?? keyName,
                        GetString(gameKey, "DisplayName", "Name", "GameName"),
                        installPath,
                        GetString(gameKey, "Executable", "Exe", "exe"),
                        GetString(gameKey, "OfferID", "OfferId", "offer_id")));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // Tek oyun anahtarı okunamazsa diğerleri taranmaya devam eder.
                }
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or System.Security.SecurityException
                                          or PlatformNotSupportedException)
        {
            // Registry okunamıyor; config ve fallback klasörler yine denenir.
        }
    }

    private static IReadOnlyList<string> GetConfigDirectories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programData = Environment.GetEnvironmentVariable("PROGRAMDATA")
                          ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var directories = new List<string>();

        if (!string.IsNullOrWhiteSpace(programData))
        {
            directories.Add(Path.Combine(programData, "EA Desktop", "InstallData"));
            directories.Add(Path.Combine(programData, "Origin", "LocalContent"));
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            directories.Add(Path.Combine(localAppData, "Electronic Arts", "EA Desktop"));
            directories.Add(Path.Combine(localAppData, "Origin"));
        }

        return directories.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
