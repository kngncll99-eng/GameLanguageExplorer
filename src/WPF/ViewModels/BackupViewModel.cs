using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.WPF.Services;

namespace GameLanguageExplorer.WPF.ViewModels;

/// <summary>Yedek tablosunda gösterilen salt okunur kayıt.</summary>
public sealed class BackupRow
{
    public BackupRow(BackupRecordInfo info)
    {
        Id = info.Id;
        BackupFileName = info.BackupFileName;
        OriginalFileName = info.OriginalFileName;
        OriginalFilePath = info.OriginalFilePath;
        BackupFilePath = info.BackupFilePath;
        BackupSizeBytes = info.BackupSizeBytes;
        OriginalSha256 = info.OriginalSha256;
        CreatedText = FormatDate(info.CreatedAt);
        IsRestored = info.IsRestored;
        RestoredText = info.RestoredAt is null ? string.Empty : $"Geri yüklendi: {FormatDate(info.RestoredAt.Value)}";
        IsBackupFileAvailable = info.IsBackupFileAvailable;
    }

    public int Id { get; }
    public string BackupFileName { get; }
    public string OriginalFileName { get; }
    public string OriginalFilePath { get; }
    public string BackupFilePath { get; }
    public long? BackupSizeBytes { get; }
    public string OriginalSha256 { get; }
    public string CreatedText { get; }
    public bool IsRestored { get; }
    public string RestoredText { get; }
    public bool IsBackupFileAvailable { get; }

    public string SizeText => FormatSize(BackupSizeBytes);
    public string Sha256Text => string.IsNullOrWhiteSpace(OriginalSha256)
        ? "—"
        : OriginalSha256.ToLowerInvariant();

    public string StatusText => IsRestored
        ? "Geri yüklendi"
        : IsBackupFileAvailable ? "Yedek" : "Dosya yok";

    private static string FormatDate(DateTime value)
        => value == default ? "—" : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private static string FormatSize(long? bytes)
    {
        if (bytes is null)
        {
            return "—";
        }

        double value = bytes.Value;
        string[] units = { "B", "KB", "MB", "GB" };
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.0} {units[unit]}";
    }
}

/// <summary>
/// "Yedekler" ekranı: <c>BackupRecord</c> kayıtlarını listeler, arar, onay alarak
/// seçili yedeği geri yükler veya siler.
/// </summary>
public partial class BackupViewModel : ViewModelBase
{
    private const int SearchDebounceMilliseconds = 300;

    private readonly IBackupManagementService _backupService;
    private readonly ILocalizationWriteService _writeService;
    private readonly IConfirmationService _confirmationService;

    private CancellationTokenSource? _loadCts;
    private int _loadVersion;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private BackupRow? _selectedEntry;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _backupCount;

    public ObservableCollection<BackupRow> Entries { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RestoreSelectedCommand { get; }
    public IAsyncRelayCommand DeleteSelectedCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }

    public bool HasBackups => Entries.Count > 0;
    public bool IsEmpty => Entries.Count == 0;

    public string EmptyMessage => string.IsNullOrWhiteSpace(SearchText)
        ? "Henüz yedek kaydı yok. Güvenli yazma işlemleri sırasında yedekler otomatik oluşturulur."
        : "Arama ölçütüne uyan yedek kaydı bulunamadı.";

    public BackupViewModel(
        IBackupManagementService backupService,
        ILocalizationWriteService writeService,
        IConfirmationService confirmationService)
    {
        _backupService = backupService
            ?? throw new ArgumentNullException(nameof(backupService));
        _writeService = writeService
            ?? throw new ArgumentNullException(nameof(writeService));
        _confirmationService = confirmationService
            ?? throw new ArgumentNullException(nameof(confirmationService));

        RefreshCommand = new AsyncRelayCommand(
            () => LoadAsync(debounce: false),
            () => !IsBusy);
        RestoreSelectedCommand = new AsyncRelayCommand(
            RestoreSelectedAsync,
            () => SelectedEntry is not null && !IsBusy);
        DeleteSelectedCommand = new AsyncRelayCommand(
            DeleteSelectedAsync,
            () => SelectedEntry is not null && !IsBusy);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(EmptyMessage));
        _ = LoadAsync(debounce: true);
    }

    partial void OnSelectedEntryChanged(BackupRow? value)
    {
        RestoreSelectedCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        RestoreSelectedCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }
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

            var entries = await _backupService
                .GetBackupsAsync(SearchText, cts.Token)
                .ConfigureAwait(true);

            if (version != _loadVersion)
            {
                return;
            }

            ReplaceEntries(entries);
            ErrorMessage = null;
            StatusMessage = successMessage ?? $"{entries.Count} yedek listeleniyor.";
        }
        catch (OperationCanceledException)
        {
            // Yeni arama/yenileme devraldı.
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                ReplaceEntries(Array.Empty<BackupRecordInfo>());
                StatusMessage = null;
                ErrorMessage = $"Yedekler yüklenemedi: {ex.Message}";
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

    private void ReplaceEntries(IReadOnlyList<BackupRecordInfo> entries)
    {
        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(new BackupRow(entry));
        }

        BackupCount = Entries.Count;
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private async Task RestoreSelectedAsync()
    {
        var selected = SelectedEntry;
        if (selected is null)
        {
            return;
        }

        if (!_confirmationService.Confirm(
                "Yedeği geri yükle",
                $"{selected.OriginalFilePath}\n\n" +
                "Bu dosya yedekteki sürümle değiştirilecek. Mevcut değişiklikler kaybolabilir.\n\n" +
                "Devam edilsin mi?"))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            var result = await _writeService
                .RestoreAsync(selected.Id)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                ErrorMessage = result.Message;
                return;
            }

            SelectedEntry = null;
            await LoadAsync(
                debounce: false,
                successMessage: $"Yedek geri yüklendi: {selected.OriginalFileName}")
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Yedek geri yüklenemedi: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        var selected = SelectedEntry;
        if (selected is null)
        {
            return;
        }

        if (!_confirmationService.Confirm(
                "Yedeği sil",
                $"{selected.BackupFileName}\n{selected.OriginalFilePath}\n\n" +
                "Yedek dosyası ve veritabanı kaydı silinsin mi?\n\n" +
                "Bu işlem geri alınamaz."))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            var result = await _backupService
                .DeleteAsync(selected.Id)
                .ConfigureAwait(true);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Message;
                return;
            }

            SelectedEntry = null;
            await LoadAsync(debounce: false, successMessage: result.Message)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Yedek silinemedi: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

