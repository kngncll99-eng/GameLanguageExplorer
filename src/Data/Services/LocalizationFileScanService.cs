namespace GameLanguageExplorer.Data.Services;

using System.Diagnostics;
using System.Security.Cryptography;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Core.Services.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Seçilen kurulum klasöründe dil dosyası adaylarını <b>salt okunur</b> biçimde keşfeder.
///
/// <list type="bullet">
/// <item>Yalnızca okuma yapılır: dosyalar değiştirilmez, silinmez, yeniden adlandırılmaz.</item>
/// <item>Kök dizin dışına çıkılmaz; sembolik bağlantı hedefleri ve döngüler kontrol edilir.</item>
/// <item>Tekrar tarama mükerrer <see cref="DiscoveredFile"/> oluşturmaz (yol bazlı eşleştirme).</item>
/// <item>Bulunamayan dosyalar <b>silinmez</b>; yalnızca "artık bulunamadı" olarak işaretlenir ve
/// bu işaretleme yalnızca eksiksiz (hatasız, iptal edilmemiş) taramada yapılır.</item>
/// </list>
/// </summary>
public sealed class LocalizationFileScanService : ILocalizationFileScanService
{
    /// <summary>Artık bulunamayan dosyalar için <c>LastValidationResult</c> metni.</summary>
    public const string MissingFileMessage = "Dosya artık bulunamadı; kayıt korunuyor.";

    /// <summary>Çok büyük dosyalarda SHA-256 atlandığında eklenecek not.</summary>
    public const string HashSkippedMessage = "SHA-256 hesaplanmadı (dosya boyutu sınırını aşıyor).";

    private const int MaxWarnings = 40;

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<LocalizationFileScanService> _logger;

    public LocalizationFileScanService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<LocalizationFileScanService>? logger = null)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalizationFileScanService>.Instance;
    }

    public async Task<LocalizationFileScanResult> ScanAsync(
        int installedGameId,
        IProgress<LocalizationScanProgress>? progress = null,
        LocalizationScanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LocalizationScanOptions();

        var stopwatch = Stopwatch.StartNew();
        var result = new LocalizationFileScanResult { InstalledGameId = installedGameId };

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var install = await db.InstalledGames
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == installedGameId, cancellationToken)
            .ConfigureAwait(false);

        if (install is null)
        {
            result.Success = false;
            result.ErrorMessage = $"Kurulum kaydı bulunamadı (Id={installedGameId}).";
            return result;
        }

        var root = PathSafety.TryNormalize(install.InstallPath);

        if (root is null || !Directory.Exists(root))
        {
            result.Success = false;
            result.RootPath = root;
            result.ErrorMessage = $"Kurulum klasörü bulunamadı veya erişilemiyor: '{install.InstallPath}'.";
            return result;
        }

        result.RootPath = root;

        await EnsureFormatsAsync(db, cancellationToken).ConfigureAwait(false);

        var collected = await Task.Run(
                () => Collect(root, options, progress, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

        result.Warnings.AddRange(collected.Warnings);
        result.FilesSeen = collected.FilesSeen;
        result.CandidatesFound = collected.Hits.Count;
        result.IgnoredFiles = collected.IgnoredFiles;
        result.SkippedPackages = collected.SkippedPackages;
        result.WasPartial = collected.WasPartial;

        await ApplyAsync(db, installedGameId, collected, result, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        result.ScanCompletedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Dil dosyası taraması: oyun={Id} klasör='{Root}' görülen={Seen} aday={Candidates} eklenen={Added} güncellenen={Updated} eksik={Missing} kısmi={Partial}",
            installedGameId, root, result.FilesSeen, result.CandidatesFound, result.Added, result.Updated,
            result.MarkedMissing, result.WasPartial);

        return result;
    }

    public async Task<IReadOnlyList<DiscoveredFile>> GetDiscoveredFilesAsync(
        int installedGameId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.DiscoveredFiles
            .AsNoTracking()
            .Where(f => f.InstalledGameId == installedGameId)
            .OrderBy(f => f.FilePath)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> EnsureFormatsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await EnsureFormatsAsync(db, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Bilinen formatları ekler (idempotent). Mevcut kayıtları değiştirmez veya silmez.</summary>
    private static async Task<int> EnsureFormatsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existingNames = await db.FileFormats
            .Select(f => f.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lookup = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var format in LocalizationFormatCatalog.All)
        {
            if (lookup.Contains(format.Key))
            {
                continue;
            }

            db.FileFormats.Add(new FileFormat
            {
                Name = format.Key,
                Extensions = format.Extensions,
                // Bu aşamada hiçbir adapter yok: okuma/yazma desteklenmiyor olarak işaretlenir.
                SupportsRead = false,
                SupportsWrite = false,
                Description = format.Note,
                IsBuiltIn = true
            });

            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return added;
    }

    private sealed record CandidateHit(
        string FullPath,
        string FileName,
        string Extension,
        long SizeInBytes,
        bool IsWritable,
        LocalizationCandidateClassification Classification,
        string Sha256);

    private sealed class ScanCollection
    {
        public List<CandidateHit> Hits { get; } = new();
        public List<string> Warnings { get; } = new();
        public int FilesSeen { get; set; }
        public int IgnoredFiles { get; set; }
        public int SkippedPackages { get; set; }
        public bool WasPartial { get; set; }
    }

    private ScanCollection Collect(
        string root,
        LocalizationScanOptions options,
        IProgress<LocalizationScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var collection = new ScanCollection();
        var rootReal = ResolveRealDirectory(root) ?? root;
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(string Directory, int Depth)>();
        var excluded = new HashSet<string>(options.ExcludedDirectoryNames, StringComparer.OrdinalIgnoreCase);

        pending.Push((root, 0));

        var directoriesVisited = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (directory, depth) = pending.Pop();

            if (++directoriesVisited > options.MaxDirectories)
            {
                collection.WasPartial = true;
                AddWarning(collection, $"Klasör limiti aşıldı ({options.MaxDirectories}); tarama kısmi.");
                break;
            }

            if (depth > options.MaxDepth)
            {
                collection.WasPartial = true;
                AddWarning(collection, $"Derinlik limiti aşıldı, atlandı: '{directory}'.");
                continue;
            }

            var realDirectory = ResolveRealDirectory(directory);

            if (realDirectory is null)
            {
                collection.WasPartial = true;
                AddWarning(collection, $"Klasör okunamadı, atlandı: '{directory}'.");
                continue;
            }

            if (!PathSafety.IsUnderRoot(realDirectory, rootReal))
            {
                collection.WasPartial = true;
                AddWarning(collection, $"Klasör kök dizin dışına işaret ediyor, atlandı: '{directory}'.");
                continue;
            }

            // Sembolik bağlantı döngülerini engelle.
            if (!visitedDirectories.Add(realDirectory))
            {
                continue;
            }

            string[] entries;

            try
            {
                entries = Directory.GetFileSystemEntries(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                           or DirectoryNotFoundException or PathTooLongException
                                           or System.Security.SecurityException)
            {
                collection.WasPartial = true;
                AddWarning(collection, $"Klasör erişilemedi ('{directory}'): {ex.Message}");
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileAttributes attributes;

                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or FileNotFoundException)
                {
                    collection.WasPartial = true;
                    AddWarning(collection, $"Öğe okunamadı, atlandı ('{entry}'): {ex.Message}");
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    var name = Path.GetFileName(entry);

                    if (!excluded.Contains(name))
                    {
                        pending.Push((entry, depth + 1));
                    }

                    continue;
                }

                if (!ProcessFile(entry, root, attributes, options, collection))
                {
                    break;
                }

                if (collection.FilesSeen % options.ProgressIntervalFiles == 0)
                {
                    progress?.Report(new LocalizationScanProgress(
                        collection.FilesSeen, collection.Hits.Count, directory));
                }
            }
        }

        progress?.Report(new LocalizationScanProgress(collection.FilesSeen, collection.Hits.Count, root));

        return collection;
    }

    /// <summary>Tek bir dosyayı işler. <c>false</c> dönerse aday limiti aşılmıştır.</summary>
    private bool ProcessFile(
        string entry,
        string root,
        FileAttributes attributes,
        LocalizationScanOptions options,
        ScanCollection collection)
    {
        collection.FilesSeen++;

        if (collection.FilesSeen > options.MaxFiles)
        {
            collection.WasPartial = true;
            AddWarning(collection, $"Dosya limiti aşıldı ({options.MaxFiles}); tarama kısmi.");
            return false;
        }

        var relativeDirectory = PathSafety.GetRelativeDirectory(entry, root);
        var classification = LocalizationFileClassifier.Classify(entry, relativeDirectory);

        if (classification is null)
        {
            collection.IgnoredFiles++;
            return true;
        }

        // Paketler ipucu yoksa listeye alinmaz: aksi halde binlerce .uasset/.pak listeyi doldurur.
        if (classification.IsPackage && !classification.HasContextHint)
        {
            collection.SkippedPackages++;
            return true;
        }

        if (collection.Hits.Count >= options.MaxCandidates)
        {
            collection.WasPartial = true;
            AddWarning(collection, $"Aday limiti aşıldı ({options.MaxCandidates}); tarama kısmi.");
            return false;
        }

        long size;
        string sha;

        try
        {
            // Sembolik bağlantılı dosyalar kök dışına işaret ediyorsa atlanır.
            var fileInfo = new FileInfo(entry);

            if (fileInfo.LinkTarget is not null)
            {
                var target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);

                if (target is not null && !PathSafety.IsUnderRoot(target.FullName, root))
                {
                    collection.WasPartial = true;
                    AddWarning(collection, $"Dosya kök dizin dışına işaret ediyor, atlandı: '{entry}'.");
                    return true;
                }
            }

            size = fileInfo.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            collection.WasPartial = true;
            AddWarning(collection, $"Dosya bilgisi okunamadı, atlandı ('{entry}'): {ex.Message}");
            return true;
        }

        if (size > options.MaxHashBytes)
        {
            // Dosya yine de keşfedildi; yalnızca karma hesaplanmadı (tarama "kısmi" sayılmaz).
            AddWarning(collection,
                $"'{Path.GetFileName(entry)}' dosyası {options.MaxHashBytes / (1024 * 1024)} MB sınırından büyük; SHA-256 atlandı.");
            sha = string.Empty;
        }
        else
        {
            sha = TryComputeSha256(entry, collection);

            if (sha.Length == 0)
            {
                return true;
            }
        }

        var isWritable = !attributes.HasFlag(FileAttributes.ReadOnly);

        collection.Hits.Add(new CandidateHit(
            PathSafety.TryNormalize(entry) ?? entry,
            Path.GetFileName(entry),
            classification.Extension,
            size,
            isWritable,
            classification,
            sha));

        return true;
    }

    private static string TryComputeSha256(string path, ScanCollection collection)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            collection.WasPartial = true;
            AddWarning(collection, $"Dosya okunamadı, atlandı ('{path}'): {ex.Message}");
            return string.Empty;
        }
    }

    private static string? ResolveRealDirectory(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);

            if (info.LinkTarget is not null)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);

                if (target is null)
                {
                    return null;
                }

                return PathSafety.TryNormalize(target.FullName);
            }

            return PathSafety.TryNormalize(info.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or DirectoryNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    private static void AddWarning(ScanCollection collection, string message)
    {
        if (collection.Warnings.Count < MaxWarnings)
        {
            collection.Warnings.Add(message);
        }
    }

    private static async Task ApplyAsync(
        AppDbContext db,
        int installedGameId,
        ScanCollection collected,
        LocalizationFileScanResult result,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var existingRows = await db.DiscoveredFiles
            .Where(f => f.InstalledGameId == installedGameId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byPath = new Dictionary<string, DiscoveredFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in existingRows)
        {
            byPath[row.FilePath] = row;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hit in collected.Hits)
        {
            cancellationToken.ThrowIfCancellationRequested();

            seen.Add(hit.FullPath);

            if (!byPath.TryGetValue(hit.FullPath, out var row))
            {
                row = new DiscoveredFile
                {
                    InstalledGameId = installedGameId,
                    FilePath = hit.FullPath,
                    DiscoveredAt = now
                };

                db.DiscoveredFiles.Add(row);
                byPath[hit.FullPath] = row;

                ApplyValues(row, hit, now);
                AddEvidence(db, row, hit, now);

                result.Added++;
            }
            else if (ApplyValues(row, hit, now))
            {
                result.Updated++;
            }
            else
            {
                result.Unchanged++;
            }
        }

        // "Artık bulunamadı" işaretlemesi YALNIZCA eksiksiz taramada yapılır.
        // Erişim hatası/limit/iptal varsa mevcut kayıtlar olduğu gibi bırakılır.
        if (!collected.WasPartial)
        {
            foreach (var row in existingRows)
            {
                if (seen.Contains(row.FilePath))
                {
                    continue;
                }

                if (!row.IsReadable &&
                    string.Equals(row.LastValidationResult, MissingFileMessage, StringComparison.Ordinal))
                {
                    continue; // zaten eksik olarak işaretli
                }

                row.IsReadable = false;
                row.LastValidatedAt = now;
                row.LastValidationResult = MissingFileMessage;
                result.MarkedMissing++;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>İçerik alanlarını günceller; değişiklik olduysa <c>true</c>.</summary>
    private static bool ApplyValues(DiscoveredFile row, CandidateHit hit, DateTime now)
    {
        var changed = false;

        if (!string.Equals(row.FileName, hit.FileName, StringComparison.Ordinal))
        {
            row.FileName = hit.FileName;
            changed = true;
        }

        if (row.FileSize != hit.SizeInBytes)
        {
            row.FileSize = hit.SizeInBytes;
            changed = true;
        }

        if (!string.Equals(row.Sha256, hit.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            row.Sha256 = hit.Sha256;
            changed = true;
        }

        if (!string.Equals(row.DetectedFormat, hit.Classification.Format.Key, StringComparison.Ordinal))
        {
            row.DetectedFormat = hit.Classification.Format.Key;
            changed = true;
        }

        if (!string.Equals(row.DetectionConfidence, hit.Classification.Confidence, StringComparison.Ordinal))
        {
            row.DetectionConfidence = hit.Classification.Confidence;
            changed = true;
        }

        if (!row.IsReadable)
        {
            row.IsReadable = true;
            changed = true;
        }

        if (row.IsWritable != hit.IsWritable)
        {
            row.IsWritable = hit.IsWritable;
            changed = true;
        }

        var note = hit.Sha256.Length == 0
            ? $"{hit.Classification.Reason} {HashSkippedMessage}"
            : hit.Classification.Reason;

        if (!string.Equals(row.LastValidationResult, note, StringComparison.Ordinal))
        {
            row.LastValidationResult = note;
            changed = true;
        }

        // Zaman damgası her taramada yenilenir; "değişmedi" sayımını etkilemez.
        row.LastValidatedAt = now;

        return changed;
    }

    private static void AddEvidence(AppDbContext db, DiscoveredFile row, CandidateHit hit, DateTime now)
    {
        foreach (var item in hit.Classification.Evidence)
        {
            db.FormatEvidences.Add(new FormatEvidence
            {
                DiscoveredFile = row,
                EvidenceType = item.EvidenceType,
                EvidenceValue = item.EvidenceValue,
                Confidence = item.Confidence,
                MatchedPattern = item.MatchedPattern,
                CreatedAt = now
            });
        }
    }
}





