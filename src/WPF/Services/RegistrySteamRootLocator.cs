using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Steam;
using Microsoft.Win32;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>
/// Steam kurulum köklerini önce kayıt defterinden (yönetici izni gerektirmez), sonra yaygın
/// yollardan bulur. Erişilemeyen anahtarlar taramayı durdurmaz.
/// </summary>
public sealed class RegistrySteamRootLocator : ISteamRootLocator
{
    private readonly ISteamRootLocator _fallback = new DefaultSteamRootLocator();

    public IReadOnlyList<string> GetCandidateSteamRoots()
    {
        var roots = new List<string>();

        // HKCU: mevcut kullanıcının Steam kurulumu (yönetici izni gerekmez)
        TryAdd(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");

        // HKLM: makine geneli kurulum (okuma izni normalde yeterlidir)
        TryAdd(roots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        TryAdd(roots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        foreach (var fallbackRoot in _fallback.GetCandidateSteamRoots())
        {
            roots.Add(fallbackRoot);
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void TryAdd(List<string> roots, RegistryKey hive, string subKeyPath, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(subKeyPath);

            if (key?.GetValue(valueName) is string path && !string.IsNullOrWhiteSpace(path))
            {
                roots.Add(path);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            // Kayıt defteri anahtarına erişilemedi: sessizce devam et, diğer konumlar denenecek.
        }
    }
}
