using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.WPF.Services;

namespace GameLanguageExplorer.WPF.ViewModels;

/// <summary>Editör tablosunda gösterilen ve düzenlenebilen tek bir metin kaydı.</summary>
public sealed partial class EditorEntryRow : ObservableObject
{
    public EditorEntryRow(LocalizationTextEntry entry, bool canEdit)
    {
        Key = entry.Key;
        SourceText = entry.SourceText;
        StatusText = entry.Status switch
        {
            LocalizationEntryStatus.Translatable => "Çevrilebilir",
            LocalizationEntryStatus.Ambiguous => "Belirsiz",
            _ => "Atlandı"
        };

        IsSkipped = entry.Status == LocalizationEntryStatus.Skipped;
        IsEditable = canEdit && !IsSkipped;
        ContextText = entry.Context ?? string.Empty;
        SkipReason = entry.SkipReason ?? string.Empty;
        PlaceholderText = entry.Placeholders.HasPlaceholders ? entry.Placeholders.Display : "—";
        _translationText = entry.ExistingTranslation ?? string.Empty;
    }

    [ObservableProperty]
    private string _translationText;

    public string Key { get; }
    public string SourceText { get; }
    public string StatusText { get; }
    public string ContextText { get; }
    public string SkipReason { get; }
    public string PlaceholderText { get; }
    public bool IsSkipped { get; }
    public bool IsEditable { get; }

    /// <summary>Kaynak metinden farklı, boş olmayan bir çeviri girildi mi?</summary>
    public bool IsChanged =>
        IsEditable
        && !string.IsNullOrWhiteSpace(TranslationText)
        && !string.Equals(TranslationText, SourceText, StringComparison.Ordinal);
}

/// <summary>
/// Yan yana yerelleştirme editörü: seçili dosyanın metinlerini kaynak/çeviri olarak gösterir.
/// Düzenleme yalnızca yazma destekli dosyalarda açıktır; kaydetme güvenli yazma servisine gider.
/// </summary>
public partial class EditorViewModel : ViewModelBase
{
    private readonly IInstalledGameScanService _installedGameScanService;
    private readonly ILocalizationFileScanService _fileScanService;
    private readonly ILocalizationPreviewService _previewService;
    private readonly ILocalizationWriteService? _writeService;
    private readonly IConfirmationService? _confirmationService;
    private readonly IReadOnlyCollection<string> _previewableExtensions;
    private readonly IReadOnlyCollection<string> _writableExtensions;

    private CancellationTokenSource? _saveCts;

    [ObservableProperty]
    private InstalledGame? _selectedGame;

    [ObservableProperty]
    private DiscoveredFileRow? _selectedFile;

    [ObservableProperty]
    private EditorEntryRow? _selectedEntry;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isEditingEnabled;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _warningMessage;

    [ObservableProperty]
    private string? _saveStatusMessage;

    [ObservableProperty]
    private string? _saveErrorMessage;

    [ObservableProperty]
    private double _saveProgressPercent;

    [ObservableProperty]
    private int _entryCount;

    public ObservableCollection<InstalledGame> Games { get; } = new();
    public ObservableCollection<DiscoveredFileRow> Files { get; } = new();
    public ObservableCollection<EditorEntryRow> Entries { get; } = new();

    public int ChangedCount => Entries.Count(entry => entry.IsChanged);
    public bool HasEntries => Entries.Count > 0;
    public bool IsEditableFile => SelectedFile?.SupportsWrite == true;

    public IAsyncRelayCommand LoadEntriesCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelSaveCommand { get; }
    public IAsyncRelayCommand ReloadCommand { get; }

    public EditorViewModel(
        IInstalledGameScanService installedGameScanService,
        ILocalizationFileScanService fileScanService,
        ILocalizationPreviewService previewService,
        ILocalizationWriteService? writeService = null,
        IConfirmationService? confirmationService = null)
    {
        _installedGameScanService = installedGameScanService
            ?? throw new ArgumentNullException(nameof(installedGameScanService));
        _fileScanService = fileScanService
            ?? throw new ArgumentNullException(nameof(fileScanService));
        _previewService = previewService
            ?? throw new ArgumentNullException(nameof(previewService));
        _writeService = writeService;
        _confirmationService = confirmationService;

        var adapterInfo = previewService.GetAdapterInfo();
        _previewableExtensions = adapterInfo
            .SelectMany(adapter => adapter.Extensions)
            .ToArray();
        _writableExtensions = adapterInfo
            .Where(adapter => adapter.SupportsWrite)
            .SelectMany(adapter => adapter.Extensions)
            .ToArray();

        LoadEntriesCommand = new AsyncRelayCommand(LoadEntriesAsync, CanLoadEntries);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        CancelSaveCommand = new RelayCommand(CancelSave, () => IsSaving);
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);

        LoadGames();
    }

    partial void OnSelectedGameChanged(InstalledGame? value)
    {
        LoadFiles();
    }

    partial void OnSelectedFileChanged(DiscoveredFileRow? value)
    {
        Entries.Clear();
        SelectedEntry = null;
        EntryCount = 0;
        IsEditingEnabled = value?.SupportsWrite == true;
        SaveStatusMessage = null;
        SaveErrorMessage = null;
        SaveProgressPercent = 0;
        LoadEntriesCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(ChangedCount));
        OnPropertyChanged(nameof(IsEditableFile));
    }

    partial void OnIsEditingEnabledChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsEditableFile));
    }

    partial void OnIsLoadingChanged(bool value)
    {
        LoadEntriesCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSavingChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        CancelSaveCommand.NotifyCanExecuteChanged();
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
        catch (Exception exception)
        {
            ErrorMessage = "Kurulu oyunlar yüklenemedi: " + exception.Message;
        }
    }

    private void LoadFiles()
    {
        Files.Clear();

        if (SelectedGame is null)
        {
            SelectedFile = null;
            return;
        }

        try
        {
            var files = _fileScanService
                .GetDiscoveredFilesAsync(SelectedGame.Id)
                .GetAwaiter().GetResult();

            foreach (var file in files
                         .Select(file => new DiscoveredFileRow(file, _previewableExtensions, _writableExtensions))
                         .OrderByDescending(row => row.SupportsWrite)
                         .ThenByDescending(row => row.SupportsPreview)
                         .ThenBy(row => row.IsMissing)
                         .ThenBy(row => row.FileName, StringComparer.OrdinalIgnoreCase))
            {
                Files.Add(file);
            }

            SelectedFile = Files.FirstOrDefault(row => row.SupportsWrite)
                           ?? Files.FirstOrDefault(row => row.SupportsPreview)
                           ?? Files.FirstOrDefault();
        }
        catch (Exception exception)
        {
            ErrorMessage = "Keşfedilmiş dosyalar yüklenemedi: " + exception.Message;
        }
    }

    private bool CanLoadEntries() =>
        SelectedGame is not null
        && SelectedFile?.SupportsPreview == true
        && !IsLoading
        && !IsSaving;

    private async Task LoadEntriesAsync() => await LoadEntriesCoreAsync(preserveStatus: false).ConfigureAwait(true);

    private async Task LoadEntriesCoreAsync(bool preserveStatus)
    {
        if (!CanLoadEntries() || SelectedGame is null || SelectedFile is null)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        WarningMessage = null;
        Entries.Clear();
        SelectedEntry = null;
        EntryCount = 0;
        SaveProgressPercent = 0;

        if (!preserveStatus)
        {
            SaveStatusMessage = null;
            SaveErrorMessage = null;
        }

        try
        {
            StatusMessage = "Metinler yükleniyor…";
            var preview = await _previewService
                .PreviewAsync(SelectedGame.Id, SelectedFile.Id)
                .ConfigureAwait(true);

            if (!preview.Success)
            {
                ErrorMessage = preview.ErrorMessage ?? "Dosya önizlenemedi.";
                StatusMessage = null;
                return;
            }

            foreach (var entry in preview.Entries)
            {
                var row = new EditorEntryRow(entry, SelectedFile.SupportsWrite);
                row.PropertyChanged += OnEntryPropertyChanged;
                Entries.Add(row);
            }

            EntryCount = Entries.Count;
            IsEditingEnabled = SelectedFile.SupportsWrite;
            WarningMessage = preview.Warnings.Count == 0
                ? null
                : string.Join(" | ", preview.Warnings.Take(4));
            StatusMessage = SelectedFile.SupportsWrite
                ? $"{EntryCount} kayıt yüklendi; {ChangedCount} değişiklik var."
                : $"{EntryCount} kayıt salt okunur yüklendi. Bu dosya türünde düzenleme kapalı.";
            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(ChangedCount));
        }
        catch (Exception exception)
        {
            ErrorMessage = "Metinler yüklenemedi: " + exception.Message;
            StatusMessage = null;
        }
        finally
        {
            IsLoading = false;
            LoadEntriesCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(EditorEntryRow.TranslationText))
        {
            return;
        }

        OnPropertyChanged(nameof(ChangedCount));
        SaveCommand.NotifyCanExecuteChanged();
    }


    private bool CanSave() =>
        _writeService is not null
        && _confirmationService is not null
        && SelectedGame is not null
        && SelectedFile?.SupportsWrite == true
        && IsEditingEnabled
        && ChangedCount > 0
        && !IsLoading
        && !IsSaving;

    private async Task SaveAsync()
    {
        if (!CanSave() || SelectedGame is null || SelectedFile is null || _writeService is null)
        {
            return;
        }

        var changes = Entries
            .Where(entry => entry.IsEditable && entry.IsChanged)
            .ToDictionary(entry => entry.Key, entry => entry.TranslationText, StringComparer.Ordinal);

        if (changes.Count == 0)
        {
            SaveErrorMessage = "Kaydedilecek değişiklik yok.";
            SaveStatusMessage = null;
            return;
        }

        var confirmed = _confirmationService?.Confirm(
            "Değişiklikleri Kaydet",
            $"'{SelectedFile.FileName}' dosyasındaki {changes.Count} kayıt değiştirilecek.\n\n"
            + "Orijinal dosya değişecek ve işlem öncesi yedek alınacak. Devam etmek istiyor musunuz?");

        if (confirmed != true)
        {
            SaveStatusMessage = "Kaydetme işlemi kullanıcı tarafından iptal edildi.";
            SaveErrorMessage = null;
            return;
        }

        IsSaving = true;
        SaveErrorMessage = null;
        SaveProgressPercent = 0;
        SaveStatusMessage = "Güvenli kaydetme hazırlanıyor…";
        _saveCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<LocalizationWriteProgress>(ApplySaveProgress);
            var result = await _writeService.WriteAsync(
                    SelectedGame.Id,
                    SelectedFile.Id,
                    changes,
                    _saveCts.Token,
                    progress)
                .ConfigureAwait(true);

            ApplySaveResult(result);

            if (result.Success)
            {
                // Başarılı yazmadan sonra kaynak metinler değiştiği için tabloyu yenile.
                IsSaving = false;
                await LoadEntriesCoreAsync(preserveStatus: true).ConfigureAwait(true);
                SaveStatusMessage = result.BackupFilePath is null
                    ? result.Message
                    : $"{result.Message} Yedek: {result.BackupFilePath}";
            }
        }
        catch (OperationCanceledException)
        {
            SaveStatusMessage = "Kaydetme iptal edildi; orijinal dosya korunmalıdır.";
            SaveProgressPercent = 0;
        }
        catch (Exception exception)
        {
            SaveErrorMessage = "Kaydetme başarısız: " + exception.Message;
            SaveStatusMessage = null;
            SaveProgressPercent = 0;
        }
        finally
        {
            _saveCts?.Dispose();
            _saveCts = null;
            IsSaving = false;
        }
    }

    private void CancelSave()
    {
        if (!IsSaving)
        {
            return;
        }

        SaveStatusMessage = "Kaydetme iptal ediliyor…";
        _saveCts?.Cancel();
    }

    private void ApplySaveProgress(LocalizationWriteProgress progress)
    {
        SaveProgressPercent = Math.Clamp(progress.Percent, 0, 100);
        SaveStatusMessage = progress.Message;
    }

    private void ApplySaveResult(LocalizationWriteResult result)
    {
        if (result.Success)
        {
            SaveProgressPercent = 100;
            SaveErrorMessage = null;
            return;
        }

        SaveProgressPercent = 0;
        SaveErrorMessage = result.Message;
        SaveStatusMessage = result.Status == LocalizationWriteStatus.Restored
            ? "Orijinal dosya yedekten geri yüklendi."
            : null;
    }

    private async Task ReloadAsync()
    {
        LoadGames();

        if (SelectedFile?.SupportsPreview == true)
        {
            await LoadEntriesCoreAsync(preserveStatus: false).ConfigureAwait(true);
        }
    }
}
