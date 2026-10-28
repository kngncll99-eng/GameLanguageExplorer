namespace GameLanguageExplorer.Data.Services;

using System.Diagnostics;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Keşfedilmiş bir dosyayı <b>salt okunur</b> ayrıştırıp metin kayıtlarını önizler.
///
/// <para>Güvenlik sırası:</para>
/// <list type="number">
/// <item>Kurulum ve dosya kaydı veritabanından okunur.</item>
/// <item>Dosyanın gerçekten bu kuruluma ait olduğu doğrulanır.</item>
/// <item>Yol normalleştirilir ve <b>kurulum kökünün içinde</b> olduğu doğrulanır.</item>
/// <item>Ancak bundan sonra dosya açılır (salt okunur, paylaşımlı erişim).</item>
/// </list>
///
/// <para>Bu servis veritabanına veya oyun dosyalarına <b>yazmaz</b>.</para>
/// </summary>
public sealed class LocalizationPreviewService : ILocalizationPreviewService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly LocalizationFormatAdapterRegistry _registry;
    private readonly LocalizationFormatReadOptions _options;
    private readonly ILogger<LocalizationPreviewService> _logger;

    public LocalizationPreviewService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        LocalizationFormatAdapterRegistry registry,
        LocalizationFormatReadOptions? options = null,
        ILogger<LocalizationPreviewService>? logger = null)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options ?? new LocalizationFormatReadOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalizationPreviewService>.Instance;
    }

    public IReadOnlyList<LocalizationFormatAdapterInfo> GetAdapterInfo() => _registry.Describe();

    public async Task<LocalizationPreviewResult> PreviewAsync(
        int installedGameId,
        int discoveredFileId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        LocalizationPreviewResult result;

        try
        {
            result = await PreviewCoreAsync(installedGameId, discoveredFileId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Önizleme sırasında beklenmeyen hata: kurulum={GameId} dosya={FileId}",
                installedGameId, discoveredFileId);

            result = LocalizationPreviewResult.Reject(
                LocalizationPreviewFailureKind.UnexpectedError,
                $"Önizleme başarısız: {ex.Message}");
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        result.InstalledGameId = installedGameId;
        result.DiscoveredFileId = discoveredFileId;

        return result;
    }

    /// <summary>
    /// Uzantı tek bir ayrıştırıcıya aitse doğrudan onu seçer (mevcut CSV/TSV davranışı korunur).
    /// Uzantıyı birden çok ayrıştırıcı destekliyorsa (ör. <c>.csv</c> → CSV ve LocresCSV) dosyanın
    /// <b>başlığına</b> bakılır; eşleşme yoksa uzantı sırasındaki ilk adaptör kullanılır.
    /// Başlık okuma sınırlıdır ve salt okunurdur.
    /// </summary>
    private ILocalizationFormatAdapter? ResolveAdapter(string path, string extension)
    {
        var candidates = _registry.FindAllByExtension(extension);

        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var head = LocalizationFileReader.ReadHead(path);

        return (head is null ? null : _registry.FindByHeader(path, head)) ?? candidates[0];
    }

    private async Task<LocalizationPreviewResult> PreviewCoreAsync(
        int installedGameId,
        int discoveredFileId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var install = await db.InstalledGames
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == installedGameId, cancellationToken)
            .ConfigureAwait(false);

        if (install is null)
        {
            return LocalizationPreviewResult.Reject(
                LocalizationPreviewFailureKind.NotFound,
                $"Kurulum kaydı bulunamadı (Id={installedGameId}).");
        }

        var file = await db.DiscoveredFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == discoveredFileId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            return LocalizationPreviewResult.Reject(
                LocalizationPreviewFailureKind.NotFound,
                $"Dosya kaydı bulunamadı (Id={discoveredFileId}).");
        }

        var fileName = file.FileName;

        if (file.InstalledGameId != installedGameId)
        {
            return LocalizationPreviewResult.Reject(
                LocalizationPreviewFailureKind.PathRejected,
                "Dosya kaydı bu kuruluma ait değil; önizleme reddedildi.",
                file.FilePath, fileName);
        }

        var root = PathSafety.TryNormalize(install.InstallPath);
        var path = PathSafety.TryNormalize(file.FilePath);

        if (root is null || path is null)
        {
            return LocalizationPreviewResult.Reject(
                LocalizationPreviewFailureKind.PathRejected,
                "Kurulum veya dosya yolu geçersiz.",
                file.FilePath, fileName);
        }

        // Dosya AÇILMADAN önce kök sınırı kontrolü.
        if (!PathSafety.IsUnderRoot(path, root))
        {
            return LocalizationPreviewResult.Reject(
                LocalizationPreviewFailureKind.PathRejected,
                $"Dosya seçili kurulum dizininin dışında; önizleme reddedildi. Kök: '{root}'.",
                path, fileName);
        }

        var extension = Path.GetExtension(path);
        var adapter = ResolveAdapter(path, extension);

        var result = new LocalizationPreviewResult
        {
            FilePath = path,
            FileName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(path) : fileName,
            FormatKey = adapter?.FormatName ?? file.DetectedFormat,
            AdapterName = adapter?.DisplayName,
            FormatSupported = adapter is not null
        };

        if (adapter is null)
        {
            result.Success = false;
            result.Failure = LocalizationPreviewFailureKind.UnsupportedFormat;
            result.ReadStatus = LocalizationReadStatus.UnsupportedFormat;
            result.ErrorMessage =
                $"'{extension}' uzantısı için ayrıştırıcı yok. Tanınan bir uzantı olması, içeriğin "
                + "desteklendiği anlamına gelmez.";
            return result;
        }

        if (!File.Exists(path))
        {
            result.Success = false;
            result.Failure = LocalizationPreviewFailureKind.FileMissing;
            result.ReadStatus = LocalizationReadStatus.ReadError;
            result.ErrorMessage = $"Dosya diskte bulunamadı: '{path}'.";
            return result;
        }

        var read = await adapter.ReadAsync(path, _options, cancellationToken).ConfigureAwait(false);

        result.ReadStatus = read.Status;
        result.Entries = read.Entries;
        result.TranslatableCount = read.TranslatableCount;
        result.AmbiguousCount = read.AmbiguousCount;
        result.SkippedCount = read.SkippedCount;
        result.Warnings = read.Warnings;
        result.Truncated = read.Truncated;
        result.Success = read.Success;

        if (!read.Success)
        {
            result.Failure = LocalizationPreviewFailureKind.ParseError;
            result.ErrorMessage = read.ErrorMessage ?? "Dosya ayrıştırılamadı.";
        }

        return result;
    }
}

