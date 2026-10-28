using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.WPF.Services;

namespace GameLanguageExplorer.WPF.ViewModels;

/// <summary>
/// "Kurulu Oyunlar" ekranı: tüm kaynakları tarar, kaynağa göre filtreler ve manuel klasör eklemeyi yönetir.
/// </summary>
public partial class InstalledGamesViewModel : ViewModelBase
{
    public const string AllSourcesFilter = "Tümü";

    private readonly IInstalledGameScanService _scanService;
    private readonly IFolderPickerService? _folderPicker;

    private CancellationTokenSource? _operationCts;
    private List<InstalledGame> _allGames = new();
    private List<InstalledGame> _games = new();

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
    private int _gameCount;

    [ObservableProperty]
    private int _scanFound;

    [ObservableProperty]
    private int _scanAdded;

    [ObservableProperty]
    private int _scanUpdated;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _emptyMessage = "Henüz kurulu oyun taranmadı. 'Tüm Kaynakları Tara' düğmesine basın veya bir oyun klasörü ekleyin.";

    [ObservableProperty]
    private string? _lastScanText;

    [ObservableProperty]
    private string _selectedSourceFilter = AllSourcesFilter;

    [ObservableProperty]
    private string? _manualFolderPath;

    [ObservableProperty]
    private string _manualGameName = string.Empty;

    [ObservableProperty]
    private string? _manualStatusMessage;

    public IAsyncRelayCommand ScanCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand PickFolderCommand { get; }
    public IAsyncRelayCommand AddManualFolderCommand { get; }
    public RelayCommand ToggleSourceFilterCommand { get; }

    /// <summary>Kaynak filtresi listesi açık mı? (tema uyumlu seçici)</summary>
    [ObservableProperty]
    private bool _isSourceFilterOpen;

    /// <summary>Kaynak filtresi seçenekleri (yalnızca gerçekten uygulanmış kaynaklar + "Tümü").</summary>
    public IReadOnlyList<string> SourceFilters { get; }

    /// <summary>Desteklenen/planlanan kaynakların dürüst listesi (UI bilgilendirmesi için).</summary>
    public IReadOnlyList<GameSourceDescriptor> Sources => GameSourceCatalog.All;

    public IEnumerable<InstalledGame> Games => _games;

    public InstalledGamesViewModel(
        IInstalledGameScanService scanService,
        IFolderPickerService? folderPicker = null,
        IReadOnlyList<string>? sourceFilters = null)
    {
        _scanService = scanService ?? throw new ArgumentNullException(nameof(scanService));
        _folderPicker = folderPicker;

        SourceFilters = sourceFilters ?? new[] { AllSourcesFilter }
            .Concat(GameSourceCatalog.Implemented.Select(s => s.Key))
            .ToList();

        ScanCommand = new AsyncRelayCommand(ExecuteScanAsync);
        RefreshCommand = new AsyncRelayCommand(ExecuteRefreshAsync);
        CancelCommand = new RelayCommand(CancelOperation, () => IsLoading);
        PickFolderCommand = new RelayCommand(PickFolder);
        AddManualFolderCommand = new AsyncRelayCommand(ExecuteAddManualFolderAsync);
        ToggleSourceFilterCommand = new RelayCommand(() => IsSourceFilterOpen = !IsSourceFilterOpen);

        LoadInstalledGames();
    }

    partial void OnIsLoadingChanged(bool value) => CancelCommand.NotifyCanExecuteChanged();

    partial void OnSelectedSourceFilterChanged(string value)
    {
        IsSourceFilterOpen = false; // seçim yapılınca liste kapanır
        ApplyFilter();
    }

    private void LoadInstalledGames()
    {
        try
        {
            _allGames = _scanService.GetInstalledGamesAsync().GetAwaiter().GetResult().ToList();
            ErrorMessage = null;

            var lastScan = _scanService.GetLastScanAtAsync().GetAwaiter().GetResult();
            LastScanText = lastScan is null
                ? "Son tarama: henüz yapılmadı"
                : $"Son tarama: {lastScan.Value.ToLocalTime():dd.MM.yyyy HH:mm}";
        }
        catch (Exception ex)
        {
            _allGames = new List<InstalledGame>();
            ErrorMessage = $"Kurulu oyun listesi yüklenemedi: {ex.Message}";
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _games = string.IsNullOrWhiteSpace(SelectedSourceFilter) || SelectedSourceFilter == AllSourcesFilter
            ? _allGames
            : _allGames
                .Where(g => string.Equals(g.InstallSource, SelectedSourceFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        GameCount = _games.Count;
        IsEmpty = _games.Count == 0;

        if (IsEmpty)
        {
            EmptyMessage = _allGames.Count == 0
                ? "Henüz kurulu oyun yok. 'Tüm Kaynakları Tara' düğmesine basın veya bir oyun klasörü ekleyin."
                : $"'{SelectedSourceFilter}' kaynağında kurulu oyun yok. Farklı bir filtre seçin.";
        }

        OnPropertyChanged(nameof(Games));
    }

    private async Task ExecuteScanAsync()
    {
        if (IsLoading)
        {
            return;
        }

        BeginOperation("Tüm kaynaklar taranıyor (salt okunur)...");

        try
        {
            var result = await _scanService.ScanAsync(_operationCts!.Token).ConfigureAwait(true);

            ScanFound = result.TotalFound;
            ScanAdded = result.Added;
            ScanUpdated = result.Updated;

            LoadInstalledGames();

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Tarama başarısız oldu.";
                StatusMessage = "Tarama tamamlanamadı.";
                IsStatusVisible = true;
                return;
            }

            var notes = new List<string>();

            if (result.SkippedProviders.Count > 0)
            {
                notes.Add($"erişilemeyen kaynak(lar) atlandı: {string.Join(", ", result.SkippedProviders)} — mevcut kayıtları korundu");
            }

            if (result.PartialProviders.Count > 0)
            {
                notes.Add($"kısmi tarama: {string.Join(", ", result.PartialProviders)} — 'kurulu değil' işaretlemesi yapılmadı");
            }

            WarningMessage = BuildWarningMessage(result.Warnings, notes);

            if (result.TotalFound == 0)
            {
                StatusMessage = "Tarama tamamlandı ancak kurulu oyun bulunamadı.";

                if (result.SkippedProviders.Count > 0)
                {
                    EmptyMessage = "Desteklenen kaynaklara erişilemedi; mevcut kayıtlar korundu. Ayrıntılar için uyarılara bakın.";
                }
            }
            else
            {
                StatusMessage =
                    $"Tarama tamamlandı: {result.TotalFound} kurulum bulundu, {result.Added} eklendi, " +
                    $"{result.Updated} güncellendi, {result.Unchanged} değişmedi" +
                    (result.MarkedNotInstalled > 0 ? $", {result.MarkedNotInstalled} kayıt 'kurulu değil' işaretlendi" : string.Empty) +
                    $". Listede {GameCount} oyun var.";
            }

            IsStatusVisible = true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Tarama iptal edildi.";
            IsStatusVisible = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Tarama başarısız: {ex.Message}";
            StatusMessage = null;
            IsStatusVisible = true;
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task ExecuteRefreshAsync()
    {
        await Task.Yield();

        LoadInstalledGames();

        StatusMessage = $"Liste yenilendi. Kurulu oyun sayısı: {GameCount}.";
        IsStatusVisible = true;
    }

    private void PickFolder()
    {
        if (_folderPicker is null)
        {
            ManualStatusMessage = "Klasör seçimi bu ortamda kullanılamıyor; yolu elle girin.";
            return;
        }

        try
        {
            var picked = _folderPicker.PickFolder("Oyun klasörünü seçin");

            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            ManualFolderPath = picked;

            if (string.IsNullOrWhiteSpace(ManualGameName))
            {
                ManualGameName = SafeFolderName(picked) ?? string.Empty;
            }

            ManualStatusMessage = "Klasör seçildi. Oyun adını doğrulayıp 'Klasörü Ekle' düğmesine basın.";
        }
        catch (Exception ex)
        {
            ManualStatusMessage = $"Klasör seçilemedi: {ex.Message}";
        }
    }

    private async Task ExecuteAddManualFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualFolderPath))
        {
            ManualStatusMessage = "Önce bir oyun klasörü seçin.";
            return;
        }

        ManualStatusMessage = "Klasör ekleniyor...";

        try
        {
            var result = await _scanService
                .AddManualGameAsync(ManualFolderPath!, ManualGameName, _operationCts?.Token ?? CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                ManualStatusMessage = $"Eklenemedi: {result.ErrorMessage}";
                return;
            }

            ManualStatusMessage = result.AlreadyExisted
                ? $"Bu klasör zaten ekliydi; mükerrer kayıt oluşturulmadı ('{result.GameName}')."
                : $"Eklendi: '{result.GameName}'. Kaynak 'Manual' olarak kaydedildi (mağaza doğrulanmadı, ad doğrulanmamış).";

            if (result.Warnings.Count > 0)
            {
                WarningMessage = string.Join(" | ", result.Warnings);
            }

            ManualFolderPath = null;
            ManualGameName = string.Empty;

            LoadInstalledGames();

            StatusMessage = $"Manuel oyun listesi güncellendi. Listede {GameCount} oyun var.";
            IsStatusVisible = true;
        }
        catch (OperationCanceledException)
        {
            ManualStatusMessage = "İşlem iptal edildi.";
        }
        catch (Exception ex)
        {
            ManualStatusMessage = $"Eklenemedi: {ex.Message}";
        }
    }

    private static string? BuildWarningMessage(IReadOnlyList<string> warnings, IReadOnlyList<string> notes)
    {
        var parts = new List<string>();

        if (notes.Count > 0)
        {
            parts.AddRange(notes);
        }

        if (warnings.Count > 0)
        {
            parts.Add($"{warnings.Count} uyarı: " + string.Join(" | ", warnings.Take(3)) +
                      (warnings.Count > 3 ? " | …" : string.Empty));
        }

        return parts.Count == 0 ? null : string.Join(" — ", parts);
    }

    private static string? SafeFolderName(string path)
    {
        try
        {
            var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            return System.IO.Path.GetFileName(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException)
        {
            return null;
        }
    }

    private void BeginOperation(string initialStatus)
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();

        IsLoading = true;
        IsStatusVisible = true;
        ErrorMessage = null;
        WarningMessage = null;
        StatusMessage = initialStatus;
        ScanFound = 0;
        ScanAdded = 0;
        ScanUpdated = 0;
    }

    private void EndOperation()
    {
        IsLoading = false;
        _operationCts?.Dispose();
        _operationCts = null;
    }

    private void CancelOperation() => _operationCts?.Cancel();
}


