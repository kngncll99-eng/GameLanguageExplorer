using System;
using System.IO;
using System.Text.Json;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>
/// Tema tercihini <b>veritabanına dokunmadan</b> küçük bir JSON dosyasında saklar:
/// <c>%LOCALAPPDATA%\GameLanguageExplorer\ui-preferences.json</c>.
///
/// <para>Dosya okunamaz/yazılamazsa sessizce varsayılana (koyu) döner; uygulama çökmez.
/// Veritabanı, migration ve oyun dosyaları bu sınıftan etkilenmez.</para>
/// </summary>
public static class UiPreferencesStore
{
    private const string FileName = "ui-preferences.json";

    private sealed class PreferenceModel
    {
        public string? Theme { get; set; }
    }

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameLanguageExplorer",
        FileName);

    public static AppTheme LoadTheme()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return AppTheme.Dark;
            }

            var model = JsonSerializer.Deserialize<PreferenceModel>(File.ReadAllText(FilePath));

            if (string.Equals(model?.Theme, "Light", StringComparison.OrdinalIgnoreCase))
            {
                return AppTheme.Light;
            }

            return AppTheme.Dark;
        }
        catch
        {
            // Bozuk/erişilemeyen tercih dosyası uygulamayı engellemez.
            return AppTheme.Dark;
        }
    }

    public static void SaveTheme(AppTheme theme)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(new PreferenceModel
            {
                Theme = theme == AppTheme.Light ? "Light" : "Dark"
            });

            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Tercih kaydedilemezse sessizce yoksay (salt kozmetik).
        }
    }
}
