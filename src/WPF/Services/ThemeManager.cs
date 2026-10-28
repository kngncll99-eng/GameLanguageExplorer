using System;
using System.Windows;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>Uygulama teması.</summary>
public enum AppTheme
{
    Dark = 0,
    Light = 1
}

/// <summary>
/// Tema değiştirmeyi tek noktadan yönetir: <c>Application.Resources.MergedDictionaries</c> içindeki
/// ilk sözlüğü (tema sözlüğü) değiştirir. Tüm view'lar renkleri <c>DynamicResource</c> ile aldığı
/// için değişiklik anında her ekrana yansır.
/// </summary>
public static class ThemeManager
{
    private const string DarkThemePath = "Themes/DarkTheme.xaml";
    private const string LightThemePath = "Themes/LightTheme.xaml";

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static bool IsDark => Current == AppTheme.Dark;

    /// <summary>Tema değiştiğinde tetiklenir (tercihi kaydetmek için).</summary>
    public static event EventHandler? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        Current = theme;

        var resources = Application.Current?.Resources;

        if (resources is null)
        {
            return;
        }

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(theme == AppTheme.Light ? LightThemePath : DarkThemePath, UriKind.Relative)
        };

        var merged = resources.MergedDictionaries;

        if (merged.Count > 0)
        {
            merged[0] = dictionary;
        }
        else
        {
            merged.Add(dictionary);
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static AppTheme Toggle()
    {
        Apply(IsDark ? AppTheme.Light : AppTheme.Dark);
        return Current;
    }

    public static string Describe(AppTheme theme) => theme == AppTheme.Light ? "Açık" : "Koyu";
}
