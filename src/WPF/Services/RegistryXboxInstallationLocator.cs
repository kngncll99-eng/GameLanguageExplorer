using System.IO;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Xbox;
using Microsoft.Win32;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>
/// Xbox App / Microsoft Store paketlerini AppModel registry kayıtlarından ve
/// C:\XboxGames klasöründen salt okunur olarak toplar.
/// </summary>
public sealed class RegistryXboxInstallationLocator : IXboxInstallationLocator
{
    private const string PackageRepositoryPath =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    private static readonly string[] InfrastructurePackagePrefixes =
    {
        "Microsoft.Windows",
        "Microsoft.UI",
        "Microsoft.VCLibs",
        "Microsoft.NET",
        "Microsoft.Services",
        "Microsoft.GamingApp"
    };

    private readonly IXboxInstallationLocator _fallback = new DefaultXboxInstallationLocator();

    public IReadOnlyList<XboxInstallationCandidate> GetInstalledCandidates()
    {
        var candidates = new List<XboxInstallationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ReadRegistryPackages(candidates, seen);

        foreach (var fallback in _fallback.GetInstalledCandidates())
        {
            if (seen.Add(NormalizePath(fallback.InstallPath)))
            {
                candidates.Add(fallback);
            }
        }

        return candidates;
    }

    private static void ReadRegistryPackages(
        ICollection<XboxInstallationCandidate> candidates,
        ISet<string> seen)
    {
        try
        {
            using var packagesKey = Registry.CurrentUser.OpenSubKey(PackageRepositoryPath);

            if (packagesKey is null)
            {
                return;
            }

            foreach (var packageFullName in packagesKey.GetSubKeyNames())
            {
                try
                {
                    using var packageKey = packagesKey.OpenSubKey(packageFullName);

                    if (packageKey is null)
                    {
                        continue;
                    }

                    var installPath = GetString(packageKey, "PackageRootFolder", "InstallLocation", "InstallPath");
                    var packageFamilyName = GetString(packageKey, "PackageFamilyName");
                    var displayName = GetString(packageKey, "DisplayName", "Name");

                    if (string.IsNullOrWhiteSpace(installPath)
                        || !IsLikelyGamePackage(packageFullName, packageFamilyName)
                        || !SafeDirectoryExists(installPath)
                        || !seen.Add(NormalizePath(installPath)))
                    {
                        continue;
                    }

                    candidates.Add(new XboxInstallationCandidate(
                        packageFamilyName,
                        IsResourceName(displayName) ? null : displayName,
                        installPath,
                        packageFullName,
                        GetString(packageKey, "ApplicationId", "AppUserModelId")));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // Tek paket anahtarı okunamazsa diğerleri taranmaya devam eder.
                }
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or System.Security.SecurityException
                                          or PlatformNotSupportedException)
        {
            // Registry okunamıyor; C:\XboxGames fallback'i yine denenir.
        }
    }

    private static bool IsLikelyGamePackage(string packageFullName, string? packageFamilyName)
    {
        var name = packageFamilyName ?? packageFullName;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return !InfrastructurePackagePrefixes.Any(prefix =>
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsResourceName(string? value)
        => value is not null && value.StartsWith("@{", StringComparison.Ordinal);

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

    private static bool SafeDirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string NormalizePath(string? path)
        => (path ?? string.Empty).Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
