using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services.Translation;
using GameLanguageExplorer.WPF.Services;

namespace GameLanguageExplorer.WPF.ViewModels;

/// <summary>Çeviri belleği tablosunda gösterilen salt okunur kayıt.</summary>
public sealed class TranslationMemoryRow
{
    public TranslationMemoryRow(TranslationMemoryEntry entry)
    {
        Id = entry.Id;
        SourceText = entry.SourceText;
        TargetText = entry.TargetText;
        SourceLanguage = entry.SourceLanguage;
        TargetLanguage = entry.TargetLanguage;
        UsageCount = entry.UsageCount;
        LastUsedText = FormatDate(entry.LastUsedAt ?? entry.CreatedAt);
        CreatedText = FormatDate(entry.CreatedAt);
    }

    public int Id { get; }
    public string SourceText { get; }
    public string TargetText { get; }
    public string SourceLanguage { get; }
    public string TargetLanguage { get; }
    public int UsageCount { get; }
    public string LastUsedText { get; }
    public string CreatedText { get; }

    private static string FormatDate(DateTime value)
        => value == default ? "—" : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

/// <summary>
/// "Çeviri Belleği" ekranı: kayıtları listeler, kaynak metne göre filtreler ve
/// onay alarak tek kayıt veya tüm belleği siler.
/// </summary>
public partial class TranslationMemoryViewModel : ViewModelBase
{
    private const int SearchDebounceMilliseconds = 300;

    private readonly ITranslationMemory _translationMemory;
    private readonly IConfirmationService _confirmationService;

    private CancellationTokenSource? _loadCts;
    private int _loadVersion;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private TranslationMemoryRow? _selectedEntry;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _entryCount;

    public ObservableCollection<TranslationMemoryRow> Entries { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand DeleteSelectedCommand { get; }
    public IAsyncRelayCommand DeleteAllCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }

    public bool HasEntries => Entries.Count > 0;
    public bool IsEmpty => Entries.Count == 0;

    public string EmptyMessage => string.IsNullOrWhiteSpace(SearchText)
        ? "Çeviri belleğinde henüz kayıt yok. Başarılı AI çevirileri otomatik olarak buraya eklenir."
        : "Arama ölçütüne uyan çeviri belleği kaydı bulunamadı.";

    public TranslationMemoryViewModel(
        ITranslationMemory translationMemory,
        IConfirmationService confirmationService)
    {
        _translationMemory = translationMemory
            ?? throw new ArgumentNullException(nameof(translationMemory));
        _confirmationService = confirmationService
            ?? throw new ArgumentNullException(nameof(confirmationService));

        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(debounce: false));
        DeleteSelectedCommand = new AsyncRelayCommand(
            DeleteSelectedAsync,
            () => SelectedEntry is not null);
        DeleteAllCommand = new AsyncRelayCommand(
            DeleteAllAsync,
            () => HasEntries);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(EmptyMessage));
        _ = LoadAsync(debounce: true);
    }

    partial void OnSelectedEntryChanged(TranslationMemoryRow? value)
        => DeleteSelectedCommand.NotifyCanExecuteChanged();

    private async Task LoadAsync(bool debounce, string? successMessage = null)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, cts);
        previous?.Cancel();

        var version = Interlocked.Increment(ref _loadVersion);

        try
        {
            if (debounce)
            {
                await Task.Delay(SearchDebounceMilliseconds, cts.Token)
                    .ConfigureAwait(true);
            }

            IsBusy = true;

            var entries = await _translationMemory
                .GetEntriesAsync(SearchText, cts.Token)
                .ConfigureAwait(true);

            if (version != _loadVersion)
            {
                return;
            }

            ReplaceEntries(entries);
            ErrorMessage = null;
            StatusMessage = successMessage ?? $"{entries.Count} kayıt listeleniyor.";
        }
        catch (OperationCanceledException)
        {
            // Yeni arama/yenileme devraldı; durum mesajlarına dokunma.
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                ReplaceEntries(Array.Empty<TranslationMemoryEntry>());
                StatusMessage = null;
                ErrorMessage = $"Çeviri belleği yüklenemedi: {ex.Message}";
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsBusy = false;
            }

            Interlocked.CompareExchange(ref _loadCts, null, cts);
            cts.Dispose();
        }
    }

    private void ReplaceEntries(IReadOnlyList<TranslationMemoryEntry> entries)
    {
        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(new TranslationMemoryRow(entry));
        }

        EntryCount = Entries.Count;
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        DeleteAllCommand.NotifyCanExecuteChanged();
    }

    private async Task DeleteSelectedAsync()
    {
        var selected = SelectedEntry;
        if (selected is null)
        {
            return;
        }

        if (!_confirmationService.Confirm(
                "Çeviri belleği kaydını sil",
                $"\"{Shorten(selected.SourceText)}\" kaynağına ait çeviri silinsin mi?\n\n" +
                "Bu işlem geri alınamaz."))
        {
            return;
        }

        try
        {
            var deleted = await _translationMemory
                .DeleteAsync(selected.Id)
                .ConfigureAwait(true);

            SelectedEntry = null;
            await LoadAsync(
                debounce: false,
                successMessage: deleted
                    ? "Seçili çeviri belleği kaydı silindi."
                    : "Seçili kayıt bulunamadı; liste yenilendi.")
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Kayıt silinemedi: {ex.Message}";
        }
    }

    private async Task DeleteAllAsync()
    {
        if (!HasEntries)
        {
            return;
        }

        if (!_confirmationService.Confirm(
                "Çeviri belleğini temizle",
                $"{EntryCount} çeviri belleği kaydının tamamı silinsin mi?\n\n" +
                "Bu işlem geri alınamaz."))
        {
            return;
        }

        try
        {
            var deleted = await _translationMemory
                .DeleteAllAsync()
                .ConfigureAwait(true);

            SelectedEntry = null;
            await LoadAsync(
                debounce: false,
                successMessage: $"{deleted} çeviri belleği kaydı silindi.")
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Çeviri belleği temizlenemedi: {ex.Message}";
        }
    }

    private static string Shorten(string text)
    {
        const int maxLength = 80;
        var singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maxLength
            ? singleLine
            : singleLine[..maxLength] + "…";
    }
}

