namespace GameLanguageExplorer.Data.Services;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Kullanıcının manuel eklediği oyun klasörlerini doğrular.
///
/// <para>Manuel kayıtların tek doğruluk kaynağı veritabanıdır (<c>InstallSource = "Manual"</c>);
/// bu sağlayıcı her taramada kayıtlı klasörlerin hâlâ var olup olmadığını kontrol eder.
/// Klasör kaybolduysa kayıt <c>IsInstalled = false</c> olarak bildirilir (silinmez).</para>
///
/// <para>Salt okunur: yalnızca <see cref="Directory.Exists(string)"/> çağrılır.</para>
/// </summary>
public sealed class ManualGameFolderProvider : IInstalledGameProvider
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<ManualGameFolderProvider>? _logger;

    public ManualGameFolderProvider(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<ManualGameFolderProvider>? logger = null)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _logger = logger;
    }

    public string ProviderName => InstalledGameScanService.ManualProviderName;

    public async Task<InstalledGameScanBatch> ScanAsync(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var games = new List<InstalledGame>();

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var rows = await db.InstalledGames
                .AsNoTracking()
                .Where(i => i.InstallSource == InstalledGameScanService.ManualProviderName)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var now = DateTime.UtcNow;

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var exists = SafeDirectoryExists(row.InstallPath);

                if (!exists)
                {
                    warnings.Add($"Manuel klasör bulunamadı: '{row.InstallPath}'.");
                }

                games.Add(new InstalledGame
                {
                    GameId = row.GameId,
                    InstallPath = row.InstallPath,
                    InstallSource = InstalledGameScanService.ManualProviderName,
                    InstallSourceId = row.InstallSourceId ?? string.Empty,
                    SizeInBytes = row.SizeInBytes,
                    LastScanAt = now,
                    IsInstalled = exists,
                    DetectedGameName = null // ad doğrulanmamış; mevcut oyun bağlantısı korunur
                });
            }

            _logger?.LogInformation("Manuel klasör taraması: {Count} kayıt.", games.Count);

            return InstalledGameScanBatch.Completed(ProviderName, games, warnings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Manuel klasör taraması başarısız.");
            return InstalledGameScanBatch.Failed(ProviderName, ex.Message);
        }
    }

    public Task<InstalledGame?> DetectInstallationAsync(string gameIdentifier, CancellationToken cancellationToken = default)
        => Task.FromResult<InstalledGame?>(null);

    private static bool SafeDirectoryExists(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
