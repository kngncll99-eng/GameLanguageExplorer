using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.WPF.Services;

namespace GameLanguageExplorer.WPF.ViewModels;

/// <summary>Listede gösterilen tek bir keşfedilmiş dosya.</summary>
public sealed class DiscoveredFileRow
{
    public DiscoveredFileRow(
        DiscoveredFile file,
        IReadOnlyCollection<string>? previewableExtensions = null,
        IReadOnlyCollection<string>? writableExtensions = null,
        IReadOnlyCollection<string>? writableFormatKeys = null)
    {
        Id = file.Id;
        FileName = string.IsNullOrWhiteSpace(file.FileName)
            ? Path.GetFileName(file.FilePath)
            : file.FileName;

        FilePath = file.FilePath;
        Extension = Path.GetExtension(file.FilePath);
        SizeText = FormatSize(file.FileSize);
        FormatKey = file.DetectedFormat;
        ConfidenceText = DescribeConfidence(file.DetectionConfidence);

        // Destek metni, kayıtlı adapter'lara göre belirlenir (DB'de AdapterUsed tutulmuyor).
        var previewable = previewableExtensions is not null
                          && previewableExtensions.Any(e => string.Equals(e, Extension, StringComparison.OrdinalIgnoreCase));

        SupportText = previewable
            ? "Ayrıştırılabilir (önizleme)"
            : LocalizationFormatCatalog.DescribeSupport(file.DetectedFormat, file.AdapterUsed);

        SupportsPreview = previewable;
        SupportsWrite = writableExtensions is not null
                        && writableExtensions.Any(e => string.Equals(e, Extension, StringComparison.OrdinalIgnoreCase))
                        && (writableFormatKeys is null
                            || writableFormatKeys.Any(key => string.Equals(key, FormatKey, StringComparison.OrdinalIgnoreCase)));

        IsPackage = !previewable
                    && LocalizationFormatCatalog.GetSupportStatus(file.DetectedFormat, file.AdapterUsed)
                        == LocalizationSupportStatus.UnsupportedPackage;

        IsMissing = !file.IsReadable;
        Reason = IsMissing
            ? "Dosya artık bulunamadı (kayıt korunuyor)."
            : file.LastValidationResult ?? string.Empty;

        Sha256 = file.Sha256;
        LastValidatedText = file.LastValidatedAt is null
            ? "—"
            : file.LastValidatedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }

    public int Id { get; }
    public string FileName { get; }
    public string FilePath { get; }
    public string Extension { get; }
    public string SizeText { get; }
    public string FormatKey { get; }
    public string ConfidenceText { get; }
    public string SupportText { get; }
    public string Reason { get; }
    public string Sha256 { get; }
    public string LastValidatedText { get; }
    public bool IsMissing { get; }
    public bool IsPackage { get; }

    /// <summary>Bu dosya kayıtlı bir adapter ile önizlenebilir mi?</summary>
    public bool SupportsPreview { get; }

    /// <summary>Bu dosya kayıtlı bir adapter ile güvenli biçimde yazılabilir mi?</summary>
    public bool SupportsWrite { get; }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    private static string DescribeConfidence(string? confidence)
        => confidence switch
        {
            LocalizationFormatCatalog.ConfidenceHigh => "Yüksek",
            LocalizationFormatCatalog.ConfidenceMedium => "Orta",
            LocalizationFormatCatalog.ConfidenceLow => "Düşük",
            _ => confidence ?? "—"
        };
}

/// <summary>Önizleme listesinde gösterilen tek bir metin kaydı.</summary>
public sealed class PreviewEntryRow
{
    public PreviewEntryRow(LocalizationTextEntry entry)
    {
        Key = entry.Key;
        SourceText = entry.SourceText;
        StatusText = entry.Status switch
        {
            LocalizationEntryStatus.Translatable => "Çevrilebilir",
            LocalizationEntryStatus.Ambiguous => "Belirsiz",
            _ => "Atlandı"
        };

        IsTranslatable = entry.Status == LocalizationEntryStatus.Translatable;
        IsAmbiguous = entry.Status == LocalizationEntryStatus.Ambiguous;
        IsSkipped = entry.Status == LocalizationEntryStatus.Skipped;

        PlaceholderText = entry.Placeholders.HasPlaceholders ? entry.Placeholders.Display : "—";
        ContextText = entry.Context ?? string.Empty;
        SkipReason = entry.SkipReason ?? string.Empty;
        Evidence = entry.Evidence;
        LineText = entry.Line is null ? string.Empty : $"satır {entry.Line}";
        KindText = entry.Kind switch
        {
            LocalizationTextKind.JsonString => "JSON değeri",
            LocalizationTextKind.XmlElementText => "XML element metni",
            LocalizationTextKind.XmlAttribute => "XML attribute",
            LocalizationTextKind.PoMessage => "PO mesajı",
            LocalizationTextKind.IniValue => "INI değeri",
            LocalizationTextKind.LocresCsvEntry => "LocresCSV kaydı",
            _ => "Hücre"
        };

        ExistingTranslation = entry.ExistingTranslation;
        HasExistingTranslation = !string.IsNullOrEmpty(entry.ExistingTranslation);
        MetaText = BuildMeta(entry);
    }

    /// <summary>Çoğul bilgisi, işaretler ve kaynak referansı tek satırda.</summary>
    private static string BuildMeta(LocalizationTextEntry entry)
    {
        var parts = new List<string>();

        if (entry.PluralIndex is { } index)
        {
            parts.Add(entry.PluralCount is > 0 ? $"çoğul {index + 1}/{entry.PluralCount}" : "çoğul");
        }

        if (!string.IsNullOrWhiteSpace(entry.PluralSourceText))
        {
            var plural = entry.PluralSourceText.Trim();
            parts.Add("çoğul kaynak: " + (plural.Length > 60 ? plural[..57] + "…" : plural));
        }

        if (entry.Flags.Count > 0)
        {
            parts.Add("işaret: " + string.Join(", ", entry.Flags));
        }

        if (!string.IsNullOrWhiteSpace(entry.Reference))
        {
            parts.Add("#: " + entry.Reference);
        }

        return string.Join(" · ", parts);
    }

    public string Key { get; }
    public string SourceText { get; }
    public string StatusText { get; }
    public string PlaceholderText { get; }
    public string ContextText { get; }
    public string SkipReason { get; }
    public string Evidence { get; }
    public string LineText { get; }
    public string KindText { get; }

    /// <summary>Dosyada var olan çeviri (PO <c>msgstr</c>); yoksa <c>null</c>.</summary>
    public string? ExistingTranslation { get; }

    public bool HasExistingTranslation { get; }

    /// <summary>Çoğul/işaret/referans özeti (boş olabilir).</summary>
    public string MetaText { get; }

    public bool HasMeta => !string.IsNullOrWhiteSpace(MetaText);

    public bool IsTranslatable { get; }
    public bool IsAmbiguous { get; }
    public bool IsSkipped { get; }
}

/// <summary>
/// "Dil Dosyası Keşfi" ekranı: seçilen kurulum klasörünü salt okunur tarar,
/// ilerleme/iptal destekler ve adayları gerekçesiyle listeler.
/// Seçilen dosya ayrıca <b>salt okunur</b> önizlenebilir.
/// </summary>
public partial class FileDiscoveryViewModel : ViewModelBase
{
    private readonly IInstalledGameScanService _installedGameScanService;
    private readonly ILocalizationFileScanService _fileScanService;
    private readonly ILocalizationPreviewService? _previewService;

    private CancellationTokenSource? _operationCts;
    private List<PreviewEntryRow> _allPreviewEntries = new();

    /// <summary>Kayıtlı adapter'ların uzantıları (keşif listesindeki destek metni için).</summary>
    private readonly IReadOnlyCollection<string>? _previewableExtensions;

    [ObservableProperty]
    private InstalledGame? _selectedGame;

    [ObservableProperty]
    private DiscoveredFileRow? _selectedFile;

    /// <summary>Önizleme tablosunda seçili kayıt (ayrıntı paneli bunu gösterir).</summary>
    [ObservableProperty]
    private PreviewEntryRow? _selectedPreviewEntry;

    public bool HasSelectedPreviewEntry => SelectedPreviewEntry is not null;

    /// <summary>Seçili oyunun vurgu rengi (yalnızca görsel vurgu).</summary>
    [ObservableProperty]
    private Brush _accentBrush = CreateBrush(GamePaletteCatalog.NeutralAccentHex, 0xFF);

    /// <summary>Seçili oyunun yumuşak vurgu tonu (satır/kart arka planı).</summary>
    [ObservableProperty]
    private Brush _accentTintBrush = CreateBrush(GamePaletteCatalog.NeutralAccentHex, 0x2E);

    /// <summary>Rozette gösterilen kısa tema adı (ör. "Bilimkurgu").</summary>
    [ObservableProperty]
    private string _paletteShortName = ShortPaletteName(GamePaletteCatalog.Neutral.DisplayName);

    [ObservableProperty]
    private bool _isPreviewing;

    [ObservableProperty]
    private bool _hasPreview;

    [ObservableProperty]
    private bool _showSkippedPreviewEntries;

    [ObservableProperty]
    private string? _previewTitle;

    [ObservableProperty]
    private string? _previewPath;

    [ObservableProperty]
    private string? _previewAdapterText;

    [ObservableProperty]
    private string? _previewSupportText;

    [ObservableProperty]
    private string? _previewStatusMessage;

    [ObservableProperty]
    private string? _previewErrorMessage;

    [ObservableProperty]
    private string? _previewWarningMessage;

    [ObservableProperty]
    private int _previewTranslatableCount;

    [ObservableProperty]
    private int _previewAmbiguousCount;

    [ObservableProperty]
    private int _previewSkippedCount;

    [ObservableProperty]
    private string? _previewDurationText;

    public ObservableCollection<PreviewEntryRow> PreviewEntries { get; } = new();

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _warningMessage;

    [ObservableProperty]
    private bool _isStatusVisible;

    [ObservableProperty]
    private string? _currentDirectory;

    [ObservableProperty]
    private int _filesSeen;

    [ObservableProperty]
    private int _candidatesFound;

    [ObservableProperty]
    private int _addedCount;

    [ObservableProperty]
    private int _updatedCount;

    [ObservableProperty]
    private int _missingCount;

    [ObservableProperty]
    private int _skippedPackages;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _emptyMessage = "Önce bir kurulu oyun seçin, sonra 'Dil Dosyalarını Tara' düğmesine basın.";

    [ObservableProperty]
    private string? _lastScanText;

    public ObservableCollection<InstalledGame> Games { get; } = new();

    /// <summary>Oyun listesi paneli açık mı? (tema uyumlu seçici)</summary>
    [ObservableProperty]
    private bool _isGameListOpen;

    public ObservableCollection<DiscoveredFileRow> Files { get; } = new();

    public IAsyncRelayCommand ScanCommand { get; }
    public IAsyncRelayCommand ReloadCommand { get; }
    public RelayCommand CancelCommand { get; }
    public IAsyncRelayCommand PreviewCommand { get; }
    public RelayCommand ToggleGameListCommand { get; }

    public FileDiscoveryViewModel(
        IInstalledGameScanService installedGameScanService,
        ILocalizationFileScanService fileScanService,
        ILocalizationPreviewService? previewService = null)
    {
        _installedGameScanService = installedGameScanService
            ?? throw new ArgumentNullException(nameof(installedGameScanService));

        _fileScanService = fileScanService
            ?? throw new ArgumentNullException(nameof(fileScanService));

        _previewService = previewService;

        _previewableExtensions = previewService?
            .GetAdapterInfo()
            .SelectMany(a => a.Extensions)
            .ToArray();

        ScanCommand = new AsyncRelayCommand(ExecuteScanAsync, () => SelectedGame is not null && !IsScanning);
        ReloadCommand = new AsyncRelayCommand(ExecuteReloadAsync);
        CancelCommand = new RelayCommand(CancelOperation, () => IsScanning);
        PreviewCommand = new AsyncRelayCommand(ExecutePreviewAsync, () => SelectedFile is not null && !IsPreviewing);
        ToggleGameListCommand = new RelayCommand(() => IsGameListOpen = !IsGameListOpen);

        LoadGames();
    }

    partial void OnSelectedGameChanged(InstalledGame? value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        UpdatePalette();
        IsGameListOpen = false; // seçim yapılınca liste kapanır
        LoadDiscoveredFiles();
    }

    partial void OnSelectedPreviewEntryChanged(PreviewEntryRow? value)
        => OnPropertyChanged(nameof(HasSelectedPreviewEntry));

    /// <summary>
    /// Seçili oyunun adına göre vurgu paletini günceller. Palet <b>yalnızca görsel</b> vurgudur;
    /// oyun verisini, tarama davranışını veya çeviri mantığını etkilemez. Ad bazlı tahmin olduğu
    /// için arayüzde "tahmini" olarak etiketlenir.
    /// </summary>
    private void UpdatePalette()
    {
        var palette = GamePaletteCatalog.Resolve(SelectedGame?.Game?.Name);

        AccentBrush = CreateBrush(palette.AccentHex, 0xFF);
        AccentTintBrush = CreateBrush(palette.AccentHex, 0x2E);
        PaletteShortName = ShortPaletteName(palette.DisplayName);
    }

    /// <summary>"Bilimkurgu — turkuaz" → "Bilimkurgu": rozet için kısa tema adı.</summary>
    private static string ShortPaletteName(string displayName)
    {
        var separator = displayName.IndexOf('\u2014'); // em dash

        return separator <= 0 ? displayName : displayName[..separator].Trim();
    }

    private static SolidColorBrush CreateBrush(string hex, byte alpha)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            var fallback = new SolidColorBrush(Color.FromArgb(alpha, 0x4C, 0x8D, 0xFF));
            fallback.Freeze();
            return fallback;
        }
    }

    partial void OnSelectedFileChanged(DiscoveredFileRow? value)
    {
        PreviewCommand.NotifyCanExecuteChanged();
        ResetPreview();
    }

    partial void OnIsPreviewingChanged(bool value) => PreviewCommand.NotifyCanExecuteChanged();

    partial void OnShowSkippedPreviewEntriesChanged(bool value) => ApplyPreviewFilter();

    partial void OnIsScanningChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void LoadGames()
    {
        try
        {
            Games.Clear();

            foreach (var game in _installedGameScanService.GetInstalledGamesAsync().GetAwaiter().GetResult())
            {
                Games.Add(game);
            }

            ErrorMessage = Games.Count == 0
                ? "Kurulu oyun bulunamadı. Önce 'Kurulu Oyunlar' ekranından tarama yapın veya klasör ekleyin."
                : null;

            SelectedGame = Games.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Kurulu oyunlar yüklenemedi: {ex.Message}";
        }
    }

    private void LoadDiscoveredFiles()
    {
        Files.Clear();

        if (SelectedGame is null)
        {
            TotalCount = 0;
            IsEmpty = true;
            EmptyMessage = "Önce bir kurulu oyun seçin, sonra 'Dil Dosyalarını Tara' düğmesine basın.";
            LastScanText = null;
            return;
        }

        try
        {
            var files = _fileScanService
                .GetDiscoveredFilesAsync(SelectedGame.Id)
                .GetAwaiter().GetResult();

            // Görüntüleme sırası: önizlenebilir adaylar önce, kayıp dosyalar en sonda.
            // (Yalnızca listedeki gösterim sırasıdır; tarama/veri davranışı değişmez.)
            foreach (var file in files
                         .Select(f => new DiscoveredFileRow(f, _previewableExtensions))
                         .OrderByDescending(r => r.SupportsPreview)
                         .ThenBy(r => r.IsMissing)
                         .ThenBy(r => r.FileName, StringComparer.OrdinalIgnoreCase))
            {
                Files.Add(file);
            }

            TotalCount = Files.Count;
            IsEmpty = Files.Count == 0;

            // Tarama sırasında sayaçlar canlı ilerlemeden gelir; tarama yokken "bulunan aday"
            // sayacı kayıtlı aday sayısını göstersin ki üst kartlar listeyle çelişmesin.
            if (!IsScanning)
            {
                CandidatesFound = Files.Count;
            }

            if (IsEmpty)
            {
                EmptyMessage = $"'{SelectedGame.Game?.Name ?? SelectedGame.InstallPath}' için henüz dil dosyası taranmadı.";
            }

            var lastValidated = Files
                .Select(f => f.LastValidatedText)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) && t != "—");

            LastScanText = lastValidated is null ? "Son tarama: henüz yapılmadı" : $"Son tarama: {lastValidated}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Keşfedilmiş dosyalar yüklenemedi: {ex.Message}";
        }
    }

    private async Task ExecuteReloadAsync()
    {
        await Task.Yield();
        LoadGames();
    }

    private async Task ExecuteScanAsync()
    {
        if (SelectedGame is null || IsScanning)
        {
            return;
        }

        var gameId = SelectedGame.Id;

        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();

        IsScanning = true;
        IsStatusVisible = true;
        ErrorMessage = null;
        WarningMessage = null;
        FilesSeen = 0;
        CandidatesFound = 0;
        AddedCount = 0;
        UpdatedCount = 0;
        MissingCount = 0;
        SkippedPackages = 0;
        CurrentDirectory = null;
        StatusMessage = "Dil dosyaları taranıyor (salt okunur)...";

        var progress = new Progress<LocalizationScanProgress>(p =>
        {
            FilesSeen = p.FilesSeen;
            CandidatesFound = p.CandidatesFound;
            CurrentDirectory = p.CurrentDirectory;
            StatusMessage = $"Taranıyor… {p.FilesSeen} dosya görüldü, {p.CandidatesFound} aday.";
        });

        try
        {
            var result = await _fileScanService
                .ScanAsync(gameId, progress, options: null, _operationCts.Token)
                .ConfigureAwait(true);

            FilesSeen = result.FilesSeen;
            CandidatesFound = result.CandidatesFound;
            AddedCount = result.Added;
            UpdatedCount = result.Updated;
            MissingCount = result.MarkedMissing;
            SkippedPackages = result.SkippedPackages;

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Tarama başarısız oldu.";
                StatusMessage = "Tarama tamamlanamadı.";
                return;
            }

            LoadDiscoveredFiles();

            WarningMessage = BuildWarnings(result);

            StatusMessage =
                $"Tarama tamamlandı: {result.FilesSeen} dosya görüldü, {result.CandidatesFound} aday bulundu, " +
                $"{result.Added} eklendi, {result.Updated} güncellendi, {result.Unchanged} değişmedi" +
                (result.MarkedMissing > 0 ? $", {result.MarkedMissing} kayıt 'artık bulunamadı' işaretlendi" : string.Empty) +
                (result.SkippedPackages > 0 ? $", {result.SkippedPackages} paket listeye alınmadı" : string.Empty) +
                $" ({result.Duration.TotalSeconds:0.0} sn).";

            if (result.WasPartial)
            {
                WarningMessage = (WarningMessage is null ? string.Empty : WarningMessage + " | ")
                                 + "Tarama kısmi tamamlandı; mevcut kayıtlar 'artık bulunamadı' işaretlenmedi.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Tarama iptal edildi. Kısmi sonuçlar korunuyor; hiçbir kayıt 'artık bulunamadı' işaretlenmedi.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Tarama başarısız: {ex.Message}";
            StatusMessage = null;
        }
        finally
        {
            IsScanning = false;
            _operationCts?.Dispose();
            _operationCts = null;
        }
    }

    private static string? BuildWarnings(LocalizationFileScanResult result)
    {
        if (result.Warnings.Count == 0)
        {
            return null;
        }

        var preview = string.Join(" | ", result.Warnings.Take(3));

        return $"{result.Warnings.Count} uyarı: {preview}" + (result.Warnings.Count > 3 ? " | …" : string.Empty);
    }

    // ── Önizleme (salt okunur ayrıştırma) ──────────────────────────────────────

    /// <summary>
    /// Seçili dosyayı ayrıştırıp metin kayıtlarını önizler. Tek dosya ayrıştırması olduğu için
    /// <b>ilerleme ve iptal sunulmaz</b>; bu durum arayüzde açıkça belirtilir.
    /// </summary>
    private async Task ExecutePreviewAsync()
    {
        if (SelectedFile is null || IsPreviewing)
        {
            return;
        }

        if (_previewService is null)
        {
            HasPreview = true;
            PreviewErrorMessage = "Önizleme servisi kullanılamıyor.";
            return;
        }

        var gameId = SelectedGame?.Id ?? 0;

        IsPreviewing = true;
        HasPreview = false;
        PreviewErrorMessage = null;
        PreviewWarningMessage = null;
        PreviewStatusMessage = "Ayrıştırılıyor (salt okunur)…";
        _allPreviewEntries = new List<PreviewEntryRow>();
        PreviewEntries.Clear();

        try
        {
            var result = await _previewService
                .PreviewAsync(gameId, SelectedFile.Id, CancellationToken.None)
                .ConfigureAwait(true);

            ApplyPreview(result);
        }
        catch (Exception ex)
        {
            HasPreview = true;
            PreviewErrorMessage = $"Önizleme başarısız: {ex.Message}";
            PreviewStatusMessage = null;
        }
        finally
        {
            IsPreviewing = false;
        }
    }

    private void ApplyPreview(LocalizationPreviewResult result)
    {
        HasPreview = true;
        PreviewTitle = result.FileName;
        PreviewPath = result.FilePath;
        PreviewAdapterText = result.AdapterName is null
            ? "Ayrıştırıcı yok"
            : $"{result.AdapterName} ayrıştırıcısı";
        PreviewSupportText = result.FormatSupported ? "Format destekleniyor" : "Format desteklenmiyor";
        PreviewTranslatableCount = result.TranslatableCount;
        PreviewAmbiguousCount = result.AmbiguousCount;
        PreviewSkippedCount = result.SkippedCount;
        PreviewDurationText = $"{result.Duration.TotalMilliseconds:0} ms";
        PreviewErrorMessage = result.Success ? null : result.ErrorMessage;

        PreviewWarningMessage = result.Warnings.Count == 0
            ? null
            : $"{result.Warnings.Count} uyarı: {string.Join(" | ", result.Warnings.Take(3))}"
              + (result.Warnings.Count > 3 ? " | …" : string.Empty);

        PreviewStatusMessage = result.Success
            ? $"Ayrıştırma tamamlandı: {result.TranslatableCount} çevrilebilir, "
              + $"{result.AmbiguousCount} belirsiz, {result.SkippedCount} atlandı."
              + (result.Truncated ? " (Kayıt sınırı nedeniyle kesildi.)" : string.Empty)
            : null;

        _allPreviewEntries = result.Entries.Select(e => new PreviewEntryRow(e)).ToList();
        ApplyPreviewFilter();
    }

    private void ApplyPreviewFilter()
    {
        SelectedPreviewEntry = null;
        PreviewEntries.Clear();

        foreach (var row in _allPreviewEntries)
        {
            if (row.IsSkipped && !ShowSkippedPreviewEntries)
            {
                continue;
            }

            PreviewEntries.Add(row);
        }
    }

    private void ResetPreview()
    {
        HasPreview = false;
        PreviewTitle = null;
        PreviewPath = null;
        PreviewAdapterText = null;
        PreviewSupportText = null;
        PreviewStatusMessage = null;
        PreviewErrorMessage = null;
        PreviewWarningMessage = null;
        PreviewTranslatableCount = 0;
        PreviewAmbiguousCount = 0;
        PreviewSkippedCount = 0;
        PreviewDurationText = null;

        _allPreviewEntries = new List<PreviewEntryRow>();
        PreviewEntries.Clear();
        SelectedPreviewEntry = null;
    }

    private void CancelOperation() => _operationCts?.Cancel();
}


