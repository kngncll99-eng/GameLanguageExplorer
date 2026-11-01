namespace GameLanguageExplorer.Core.Services.InstalledGames;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>Ortak mağaza tarayıcılarının ürettiği, sağlayıcıdan bağımsız aday.</summary>
public sealed record ProviderInstallCandidate(
    string InstallPath,
    string? GameId = null,
    string? GameName = null,
    string? ExecutableName = null,
    long? SizeInBytes = null);

/// <summary>Bir sağlayıcının aday yükleme sonucu ve kaynak durumu.</summary>
public sealed record ProviderCandidateSet(
    IReadOnlyList<ProviderInstallCandidate> Candidates,
    bool SourceAvailable,
    bool IsPartial = false);

/// <summary>
/// Manifest/config tabanlı mağaza sağlayıcılarının ortak yürütme iskeleti.
/// Uyarı toplama, hata durumları, yol doğrulama, dedupe ve InstalledGame üretimi
/// tek yerde tutulur; format/registry ayrıntıları alt sınıflarda kalır.
/// </summary>
public abstract class InstalledGameProviderBase : IInstalledGameProvider, IScanDiagnostics
{
    private readonly List<string> _warnings = new();
    private readonly object _warningsLock = new();

    protected InstalledGameProviderBase(
        string providerName,
        string displayName,
        ILogger? logger = null)
    {
        ProviderName = providerName;
        DisplayName = displayName;
        Logger = logger;
    }

    public string ProviderName { get; }

    protected string DisplayName { get; }

    protected ILogger? Logger { get; }

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

    protected abstract string UnavailableReason { get; }

    protected abstract ProviderCandidateSet LoadCandidates(CancellationToken cancellationToken);

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
        var identifier = gameIdentifier.Trim();

        return batch.Games.FirstOrDefault(game =>
            string.Equals(game.InstallSourceId, identifier, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizePath(game.InstallPath), NormalizePath(identifier), StringComparison.OrdinalIgnoreCase));
    }

    private InstalledGameScanBatch ScanCore(CancellationToken cancellationToken)
    {
        lock (_warningsLock)
        {
            _warnings.Clear();
        }

        ProviderCandidateSet candidateSet;

        try
        {
            candidateSet = LoadCandidates(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var message = $"{DisplayName} kurulum kaynakları alınamadı: {exception.Message}";
            Warn(message);
            return InstalledGameScanBatch.Failed(ProviderName, message);
        }

        if (!candidateSet.SourceAvailable)
        {
            Warn(UnavailableReason);
            return InstalledGameScanBatch.Unavailable(ProviderName, UnavailableReason);
        }

        var now = DateTime.UtcNow;
        var results = new List<InstalledGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var partial = candidateSet.IsPartial;

        foreach (var candidate in candidateSet.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(candidate.InstallPath)
                || !Path.IsPathFullyQualified(candidate.InstallPath.Trim()))
            {
                partial = true;
                Warn($"{DisplayName} adayında geçerli kurulum yolu yok; aday atlandı.");
                continue;
            }

            if (!TryGetFullPath(candidate.InstallPath, out var installPath))
            {
                partial = true;
                Warn($"{DisplayName} kurulum yolu çözülemedi '{candidate.InstallPath}'.");
                continue;
            }

            var folderExists = SafeDirectoryExists(installPath);

            if (!folderExists)
            {
                Warn($"{DisplayName} kaydı var ama oyun klasörü bulunamadı: '{installPath}'.");
            }

            var installation = CreateInstallation(candidate, installPath, folderExists, now);

            if (string.IsNullOrWhiteSpace(installation.InstallSourceId))
            {
                partial = true;
                Warn($"{DisplayName} adayının kimliği belirlenemedi: '{installPath}'.");
                continue;
            }

            if (seen.Add($"{installation.InstallSourceId}|{NormalizePath(installation.InstallPath)}"))
            {
                results.Add(installation);
            }
        }

        return InstalledGameScanBatch.Completed(
            ProviderName,
            results,
            LastScanWarnings,
            isPartial: partial);
    }

    protected virtual InstalledGame CreateInstallation(
        ProviderInstallCandidate candidate,
        string installPath,
        bool folderExists,
        DateTime now)
    {
        var folderName = Path.GetFileName(NormalizePath(installPath));
        var gameName = FirstNonEmpty(candidate.GameName, folderName);
        var gameId = FirstNonEmpty(candidate.GameId, gameName);
        var sourceId = string.IsNullOrWhiteSpace(gameId) ? NormalizePath(installPath) : gameId;

        return new InstalledGame
        {
            GameId = 0,
            InstallPath = installPath,
            ExecutableName = FirstNonEmpty(candidate.ExecutableName),
            SizeInBytes = candidate.SizeInBytes,
            InstalledAt = null,
            LastScanAt = now,
            IsInstalled = folderExists,
            InstallSource = ProviderName,
            InstallSourceId = sourceId,
            DetectedGameName = string.IsNullOrWhiteSpace(gameName) ? sourceId : gameName
        };
    }

    protected void Warn(string message)
    {
        lock (_warningsLock)
        {
            if (!_warnings.Contains(message, StringComparer.Ordinal))
            {
                _warnings.Add(message);
            }
        }

        Logger?.LogWarning("{Message}", message);
    }

    protected static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    protected static bool SafeDirectoryExists(string path)
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

    protected static string NormalizePath(string? path)
        => (path ?? string.Empty).Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    protected static bool TryGetFullPath(string path, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = string.Empty;
            return false;
        }
    }
}

