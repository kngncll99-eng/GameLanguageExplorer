using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Data;
using Microsoft.EntityFrameworkCore;

namespace GameLanguageExplorer.WPF.ViewModels;

/// <summary>
/// Katalog ekranı. Arama, liste yenileme, Steam tam katalog senkronizasyonu ve
/// (API anahtarı gerektirmeyen) Steam aramasından içe aktarma işlemlerini yönetir.
/// </summary>
public partial class CatalogViewModel : ViewModelBase
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ICatalogSyncService _syncService;
    private readonly IGameSearchProvider? _searchProvider;

    private CancellationTokenSource? _operationCts;
    private List<Game> _games = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _gameCount;

    [ObservableProperty]
    private Game? _selectedGame;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _syncAdded;

    [ObservableProperty]
    private int _syncUpdated;

    [ObservableProperty]
    private int _syncTotalFetched;

    /// <summary>Durum/bilgi şeridinin görünür olup olmadığını kontrol eder.</summary>
    [ObservableProperty]
    private bool _isStatusVisible;

    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand SyncCommand { get; }
    public IAsyncRelayCommand ImportFromSteamCommand { get; }
    public RelayCommand CancelCommand { get; }

    public IEnumerable<Game> Games => _games;

    public CatalogViewModel(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ICatalogSyncService syncService,
        IGameSearchProvider? searchProvider = null)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _searchProvider = searchProvider;

        SearchCommand = new AsyncRelayCommand(ExecuteSearchAsync);
        RefreshCommand = new RelayCommand(ExecuteRefresh);
        SyncCommand = new AsyncRelayCommand(ExecuteSyncAsync);
        ImportFromSteamCommand = new AsyncRelayCommand(ExecuteImportFromSteamAsync);
        CancelCommand = new RelayCommand(CancelOperation, () => IsLoading);

        LoadGames();
    }

    partial void OnIsLoadingChanged(bool value) => CancelCommand.NotifyCanExecuteChanged();

    // ── Liste yükleme / arama ────────────────────────────────────────────────

    /// <summary>Veritabanındaki toplam sayıyı ve ilk 100 oyunu yükler.</summary>
    private void LoadGames()
    {
        try
        {
            using var db = _dbContextFactory.CreateDbContext();

            GameCount = db.Games.Count();

            _games = db.Games
                .AsNoTracking()
                .OrderBy(g => g.Name)
                .Take(100)
                .ToList();

            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            _games = new List<Game>();
            GameCount = 0;
            ErrorMessage = $"Oyun listesi yüklenemedi: {ex.Message}";
        }

        OnPropertyChanged(nameof(Games));
    }

    /// <summary>
    /// Arama. Sorgu mantığı tek yerde (<see cref="ICatalogSyncService.SearchGamesAsync"/>) tutulur;
    /// böylece UI ile servis aynı (büyük/küçük harf duyarsız) davranışı paylaşır.
    /// </summary>
    private async Task ExecuteSearchAsync()
    {
        var term = SearchText?.Trim();

        if (string.IsNullOrEmpty(term))
        {
            IsStatusVisible = false;
            LoadGames();
            return;
        }

        try
        {
            var results = (await _syncService.SearchGamesAsync(term, take: 100)).ToList();

            _games = results;
            ErrorMessage = null;

            StatusMessage = results.Count == 0
                ? $"'{term}' ile eşleşen oyun bulunamadı (katalogda {GameCount} oyun var)."
                : $"'{term}' için {results.Count} sonuç gösteriliyor (katalogda {GameCount} oyun).";

            IsStatusVisible = true;
        }
        catch (Exception ex)
        {
            _games = new List<Game>();
            ErrorMessage = $"Arama başarısız: {ex.Message}";
            IsStatusVisible = true;
        }

        OnPropertyChanged(nameof(Games));
    }

    private void ExecuteRefresh()
    {
        IsStatusVisible = false;
        LoadGames();
        StatusMessage = $"Liste yenilendi. Katalogda {GameCount} oyun var.";
        IsStatusVisible = true;
    }

    // ── Uzun süren işlemler ──────────────────────────────────────────────────

    private async Task ExecuteSyncAsync()
    {
        if (IsLoading)
        {
            return;
        }

        BeginOperation("Senkronizasyon başlatıldı. Steam'den veri alınıyor...");

        try
        {
            var result = await _syncService.SyncProviderAsync("Steam", _operationCts!.Token);

            ApplyResult(result, sourceLabel: "Steam kataloğu");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Senkronizasyon iptal edildi.";
            IsStatusVisible = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Senkronizasyon başarısız: {ex.Message}";
            StatusMessage = null;
            IsStatusVisible = true;
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>Katalogdaki arama kutusunu kullanarak Steam'de arama yapar ve sonuçları kataloğa yazar.</summary>
    private async Task ExecuteImportFromSteamAsync()
    {
        if (IsLoading)
        {
            return;
        }

        var term = SearchText?.Trim();

        if (string.IsNullOrWhiteSpace(term))
        {
            ErrorMessage = "Steam'de aramak için önce bir oyun adı yazın (örnek: portal).";
            StatusMessage = null;
            IsStatusVisible = true;
            return;
        }

        if (_searchProvider is null)
        {
            ErrorMessage = "Steam arama sağlayıcısı kullanılamıyor.";
            IsStatusVisible = true;
            return;
        }

        BeginOperation($"Steam'de '{term}' aranıyor...");

        try
        {
            var found = await _searchProvider.SearchGamesAsync(term, maxResults: 10, enrichDetails: true, _operationCts!.Token);

            if (found.Count == 0)
            {
                ErrorMessage = $"Steam'de '{term}' için sonuç bulunamadı.";
                StatusMessage = null;
                IsStatusVisible = true;
                return;
            }

            StatusMessage = $"Steam'de {found.Count} oyun bulundu. Kataloğa yazılıyor...";

            var result = await _syncService.ImportGamesAsync("Steam", found, _operationCts!.Token);

            ApplyResult(result, sourceLabel: $"Steam araması ('{term}')");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "İçe aktarma iptal edildi.";
            IsStatusVisible = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Steam araması/içe aktarma başarısız: {ex.Message}";
            StatusMessage = null;
            IsStatusVisible = true;
        }
        finally
        {
            EndOperation();
        }
    }

    private void ApplyResult(CatalogSyncResult result, string sourceLabel)
    {
        SyncTotalFetched = result.TotalFetched;
        SyncAdded = result.Added;
        SyncUpdated = result.Updated;

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Bilinmeyen hata.";
            StatusMessage = $"{sourceLabel}: işlem başarısız.";
            IsStatusVisible = true;
            LoadGames();
            return;
        }

        // Sıfır sonuç, başarılı senkronizasyon gibi gösterilmez.
        if (result.TotalFetched == 0)
        {
            ErrorMessage = $"{sourceLabel}: 0 oyun alındı. Katalog güncellenmedi.";
            StatusMessage = "İşlem tamamlandı ancak hiç oyun alınmadı.";
            IsStatusVisible = true;
            LoadGames();
            return;
        }

        ErrorMessage = null;

        LoadGames();
        StatusMessage =
            $"{sourceLabel}: {result.TotalFetched} oyun alındı, {result.Added} eklendi, " +
            $"{result.Updated} güncellendi, {result.Unchanged} değişmedi. Katalogda toplam {GameCount} oyun var.";
        IsStatusVisible = true;
    }

    private void BeginOperation(string initialStatus)
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();

        IsLoading = true;
        IsStatusVisible = true;
        ErrorMessage = null;
        StatusMessage = initialStatus;
        SyncTotalFetched = 0;
        SyncAdded = 0;
        SyncUpdated = 0;
    }

    private void EndOperation()
    {
        IsLoading = false;
        _operationCts?.Dispose();
        _operationCts = null;
    }

    private void CancelOperation() => _operationCts?.Cancel();
}


