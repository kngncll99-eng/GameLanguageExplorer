namespace GameLanguageExplorer.Data;

/// <summary>
/// SQLite veritabanı dosyasının konumunu ve bağlantı dizesini tek bir yerden yönetir.
/// Veritabanı, uygulama çıktısının (bin) içine değil, kullanıcının yerel uygulama verisi
/// klasörüne yazılır; böylece <c>dotnet clean</c> / yeniden derleme veriyi silmez.
/// </summary>
public static class DatabaseConfiguration
{
    public const string FileName = "gamelanguageexplorer.db";

    /// <summary>%LOCALAPPDATA%\GameLanguageExplorer\gamelanguageexplorer.db</summary>
    public static string DatabasePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameLanguageExplorer",
        FileName);

    public static string ConnectionString => $"Data Source={DatabasePath}";

    /// <summary>Veritabanı klasörünü (yoksa) oluşturur.</summary>
    public static void EnsureDirectoryExists()
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
