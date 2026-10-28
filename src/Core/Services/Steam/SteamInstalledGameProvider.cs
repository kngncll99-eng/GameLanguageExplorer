namespace GameLanguageExplorer.Core.Services.Steam;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>
/// Steam kurulu oyunlarını <b>salt okunur</b> biçimde tespit eder.
///
/// <list type="bullet">
/// <item>Yalnızca <c>libraryfolders.vdf</c> ve <c>appmanifest_*.acf</c> dosyaları okunur.</item>
/// <item>Hiçbir oyun dosyası değiştirilmez, silinmez veya yeniden adlandırılmaz.</item>
/// <item>Erişilemeyen klasör/bozuk manifest tüm taramayı durdurmaz; uyarı olarak raporlanır.</item>
/// <item>Kurulum, klasör varlığına değil <b>doğrulanmış manifeste</b> dayanır.</item>
/// <item>Steam bulunamazsa <see cref="InstalledGameScanStatus.Unavailable"/> döner; bu durumda
/// çağıran taraf mevcut kayıtları "kurulu değil" işaretlememelidir.</item>
/// </list>
/// </summary>
public class SteamInstalledGameProvider : IInstalledGameProvider, IScanDiagnostics
{
    public const string ProviderNameValue = "Steam";

    private readonly ISteamRootLocator _rootLocator;
    private readonly ILogger<SteamInstalledGameProvider>? _logger;
    private readonly List<string> _warnings = new();
    private readonly object _warningsLock = new();

    public SteamInstalledGameProvider(
        ISteamRootLocator rootLocator,
        ILogger<SteamInstalledGameProvider>? logger = null)
    {
        _rootLocator = rootLocator ?? throw new ArgumentNullException(nameof(rootLocator));
        _logger = logger;
    }

    public string ProviderName => ProviderNameValue;

    public IReadOnlyList<string> LastScanWarnings
    {
        get
        {
            lock (_warningsLock)
            {
                return _warnings.ToArray();
            }
        }
    }

    public Task<InstalledGameScanBatch> ScanAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => ScanCore(cancellationToken), cancellationToken);

    public async Task<InstalledGame?> DetectInstallationAsync(
        string gameIdentifier,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameIdentifier))
        {
            return null;
        }

        var batch = await ScanAsync(cancellationToken).ConfigureAwait(false);

        return batch.Games.FirstOrDefault(i =>
            string.Equals(i.InstallSourceId, gameIdentifier.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private InstalledGameScanBatch ScanCore(CancellationToken cancellationToken)
    {
        lock (_warningsLock)
        {
            _warnings.Clear();
        }

        var results = new List<InstalledGame>();
        var partial = false;

        try
        {
            IReadOnlyList<string> roots;

            try
            {
                roots = _rootLocator.GetCandidateSteamRoots();
            }
            catch (Exception ex)
            {
                var message = $"Steam kurulum konumları alınamadı: {ex.Message}";
                Warn(message);
                return InstalledGameScanBatch.Failed(ProviderNameValue, message);
            }

            if (roots.Count == 0)
            {
                const string reason = "Steam kurulumu bulunamadı (bilinen konumlarda 'steamapps' klasörü yok).";
                Warn(reason);
                return InstalledGameScanBatch.Unavailable(ProviderNameValue, reason);
            }

            var libraryWarnings = new List<string>();
            var steamAppsPaths = SteamLibraryLocator.FindSteamAppsPaths(roots, libraryWarnings);

            foreach (var warning in libraryWarnings)
            {
                // Bir kütüphane okunamadıysa tarama kısmi: eksik kayıtlar "kurulu değil" sayılmamalı.
                partial = true;
                Warn(warning);
            }

            if (steamAppsPaths.Count == 0)
            {
                const string reason = "Steam kök klasörü bulundu ancak hiçbir oyun kütüphanesi (steamapps) okunamadı.";
                Warn(reason);
                return InstalledGameScanBatch.Unavailable(ProviderNameValue, reason);
            }

            ScanLibraries(steamAppsPaths, results, ref partial, cancellationToken);

            _logger?.LogInformation(
                "Steam taraması tamamlandı: {Count} kurulum, {Libraries} kütüphane, kısmi={Partial}.",
                results.Count, steamAppsPaths.Count, partial);

            return InstalledGameScanBatch.Completed(ProviderNameValue, results, LastScanWarnings, isPartial: partial);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Steam taraması beklenmeyen hata ile durdu.");
            return InstalledGameScanBatch.Failed(ProviderNameValue, ex.Message);
        }
    }

    /// <summary>Kütüphaneleri ve manifestleri tarar; okunamayan her öğe taramayı "kısmi" yapar.</summary>
    private void ScanLibraries(
        IReadOnlyList<string> steamAppsPaths,
        List<InstalledGame> results,
        ref bool partial,
        CancellationToken cancellationToken)
    {
        var seenManifests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        foreach (var steamApps in steamAppsPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string[] manifestFiles;

            try
            {
                manifestFiles = Directory.GetFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                partial = true;
                Warn($"Kütüphane okunamadı, atlandı '{steamApps}': {ex.Message}");
                continue;
            }
            catch (Exception ex)
            {
                partial = true;
                Warn($"Kütüphane taranırken beklenmeyen hata '{steamApps}': {ex.Message}");
                continue;
            }

            foreach (var manifestPath in manifestFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var install = ReadInstall(manifestPath, steamApps, now, ref partial, cancellationToken);

                if (install is not null && seenManifests.Add($"{install.InstallSourceId}|{install.InstallPath}"))
                {
                    results.Add(install);
                }
            }
        }
    }

    private InstalledGame? ReadInstall(
        string manifestPath,
        string steamApps,
        DateTime now,
        ref bool partial,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SteamAppManifest manifest;

        try
        {
            manifest = SteamManifestParser.ParseFile(manifestPath);
        }
        catch (FormatException ex)
        {
            partial = true;
            Warn($"Bozuk manifest atlandı '{manifestPath}': {ex.Message}");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            partial = true;
            Warn($"Manifest okunamadı '{manifestPath}': {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            partial = true;
            Warn($"Manifest işlenirken beklenmeyen hata '{manifestPath}': {ex.Message}");
            return null;
        }

        var installPath = manifest.ResolveInstallPath(steamApps);

        if (installPath is null)
        {
            partial = true;
            Warn($"Kurulum yolu hesaplanamadı (AppID {manifest.AppId}).");
            return null;
        }

        // Kurulum, klasör varlığına değil doğrulanmış manifeste dayanır.
        // Klasör yoksa kayıt yine üretilir ama "kurulu değil" olarak işaretlenir.
        var folderExists = SafeDirectoryExists(installPath);

        if (!folderExists)
        {
            Warn($"Manifest var ama oyun klasörü bulunamadı: '{installPath}' (AppID {manifest.AppId}).");
        }

        return new InstalledGame
        {
            GameId = 0,
            InstallPath = installPath,
            InstallSource = ProviderNameValue,
            InstallSourceId = manifest.AppId,
            SizeInBytes = manifest.SizeOnDisk,
            InstalledAt = manifest.LastUpdatedUtc,
            LastScanAt = now,
            IsInstalled = folderExists,
            DetectedGameName = manifest.Name
        };
    }

    private static bool SafeDirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private void Warn(string message)
    {
        lock (_warningsLock)
        {
            _warnings.Add(message);
        }

        _logger?.LogWarning("{Message}", message);
    }
}



