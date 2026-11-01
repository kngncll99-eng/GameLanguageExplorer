using System.IO;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Ubisoft;
using Microsoft.Win32;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>
/// Ubisoft Connect kurulumlarını registry, config/cache klasörleri ve bilinen oyun
/// klasörlerinden salt okunur olarak toplar.
/// </summary>
public sealed class RegistryUbisoftInstallationLocator : IUbisoftInstallationLocator
{
    private static readonly string[] RegistryInstallPaths =
    {
        @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs",
        @"SOFTWARE\Ubisoft\Launcher\Installs"
    };

    private readonly IUbisoftInstallationLocator _fallback = new DefaultUbisoftInstallationLocator();

    public IReadOnlyList<UbisoftInstallationCandidate> GetInstalledCandidates()
    {
        var candidates = new List<UbisoftInstallationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var path in RegistryInstallPaths)
            {
                ReadRegistryInstalls(hive, path, candidates, seen);
            }
        }

        foreach (var configRoot in GetConfigDirectories())
        {
            foreach (var candidate in UbisoftConfigManifestReader.ReadFromDirectory(configRoot))
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

    private static void ReadRegistryInstalls(
        RegistryKey hive,
        string subKeyPath,
        ICollection<UbisoftInstallationCandidate> candidates,
        ISet<string> seen)
    {
        try
        {
            using var installsKey = hive.OpenSubKey(subKeyPath);

            if (installsKey is null)
            {
                return;
            }

            foreach (var keyName in installsKey.GetSubKeyNames())
            {
                try
                {
                    using var gameKey = installsKey.OpenSubKey(keyName);

                    if (gameKey is null)
                    {
                        continue;
                    }

                    var installPath = GetString(gameKey, "InstallDir", "InstallPath", "Path", "installDir");

                    if (string.IsNullOrWhiteSpace(installPath))
                    {
                        continue;
                    }

                    var normalized = NormalizePath(installPath);

                    if (!seen.Add(normalized))
                    {
                        continue;
                    }

                    candidates.Add(new UbisoftInstallationCandidate(
                        keyName,
                        GetString(gameKey, "Name", "GameName", "gameName"),
                        installPath,
                        GetString(gameKey, "Executable", "Exe", "exe")));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // Tek oyun anahtarı okunamazsa kalanlar taranmaya devam eder.
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

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            directories.Add(Path.Combine(localAppData, "Ubisoft Game Launcher"));
        }

        if (!string.IsNullOrWhiteSpace(programData))
        {
            directories.Add(Path.Combine(programData, "Ubisoft"));
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
