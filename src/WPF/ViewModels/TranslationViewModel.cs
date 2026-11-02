using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Export;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Translation;
using GameLanguageExplorer.WPF.Services;

namespace GameLanguageExplorer.WPF.ViewModels;

/// <summary>Sonuç tablosunda gösterilen tek bir çeviri kaydı.</summary>
public sealed class TranslationResultRow
{
    private static readonly Brush CandidateBrush = CreateBrush(0x4A, 0xDE, 0x80);
    private static readonly Brush ReviewBrush = CreateBrush(0xF5, 0xC4, 0x51);
    private static readonly Brush FailedBrush = CreateBrush(0xFF, 0x8F, 0x8F);
    private static readonly Brush ReusedBrush = CreateBrush(0x7F, 0xB8, 0xFF);
    private static readonly Brush SkippedBrush = CreateBrush(0x94, 0xA0, 0xB0);

    public TranslationResultRow(TranslationUnitResult result)
    {
        UnitId = result.UnitId;
        EntryKey = result.EntryKey;
        SourceText = result.SourceText;
        TranslationText = result.Translation ?? string.Empty;
        DecisionReason = result.DecisionReason ?? string.Empty;
        AttemptsText = result.Attempts == 0 ? "—" : result.Attempts.ToString();
        QualityText = result.QualityIssues.Count == 0
            ? "—"
            : string.Join(
                " | ",
                result.QualityIssues.Select(issue => $"{issue.Code}: {issue.Description}"));
        PlaceholderText = result.SourceText.Length == 0
            ? "—"
            : DescribePlaceholders(result.SourceText);

        Status = result.Status;
        (StatusText, StatusBrush) = result.Status switch
        {
            TranslationUnitStatus.Candidate => ("Aday", CandidateBrush),
            TranslationUnitStatus.NeedsReview => ("İnceleme", ReviewBrush),
            TranslationUnitStatus.Failed => ("Başarısız", FailedBrush),
            TranslationUnitStatus.Reused => ("Yeniden kullanıldı", ReusedBrush),
            _ => ("Atlandı", SkippedBrush)
        };
    }

    public string UnitId { get; }
    public string EntryKey { get; }
    public string SourceText { get; }
    public string TranslationText { get; }
    public string DecisionReason { get; }
    public string AttemptsText { get; }
    public string QualityText { get; }
    public string PlaceholderText { get; }
    public TranslationUnitStatus Status { get; }
    public string StatusText { get; }
    public Brush StatusBrush { get; }
    public bool NeedsAttention =>
        Status is TranslationUnitStatus.NeedsReview or TranslationUnitStatus.Failed;

    private static string DescribePlaceholders(string sourceText)
    {
        var scan = PlaceholderScanner.Scan(sourceText);
        return scan.HasPlaceholders ? scan.Display : "—";
    }

    private static Brush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Dışa aktarma formatı seçeneği.</summary>
public sealed record TranslationExportFormatOption(
    LocalizationExportFormat Format,
    string DisplayName,
    string Extension,
    string Filter);

/// <summary>
/// "AI Çeviri" ekranı: keşfedilmiş dosya seçimi, salt okunur önizleme, yerel LM Studio
/// bağlantı sınaması, partili çeviri, iptal, devam ve sonuç inceleme.
/// </summary>
public partial class TranslationViewModel : ViewModelBase
{
    public static IReadOnlyList<TranslationExportFormatOption> AvailableExportFormats { get; } =
        new[]
        {
            new TranslationExportFormatOption(
                LocalizationExportFormat.Json,
                "JSON",
                ".json",
                "JSON dosyası (*.json)|*.json|Tüm dosyalar (*.*)|*.*"),
            new TranslationExportFormatOption(
                LocalizationExportFormat.Csv,
                "CSV",
                ".csv",
                "CSV dosyası (*.csv)|*.csv|Tüm dosyalar (*.*)|*.*"),
            new TranslationExportFormatOption(
                LocalizationExportFormat.Po,
                "PO",
                ".po",
                "PO dosyası (*.po)|*.po|Tüm dosyalar (*.*)|*.*")
        };

    private readonly IInstalledGameScanService _installedGameScanService;
    private readonly ILocalizationFileScanService _fileScanService;
    private readonly ILocalizationPreviewService _previewService;
    private readonly ITranslationEngine _translationEngine;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TranslationRunJournalStore _journalStore;
    private readonly IReadOnlyCollection<string> _previewableExtensions;
    private readonly IReadOnlyCollection<string> _writableExtensions;
    private readonly IReadOnlyCollection<string> _writableFormatKeys;
    private readonly ILocalizationWriteService? _writeService;
    private readonly IConfirmationService? _confirmationService;
    private readonly ILocalizationExportService? _exportService;
    private readonly ISaveFileDialogService? _saveFileDialogService;

    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _writeCts;
    private LocalizationPreviewResult? _preview;
    private int _previewRequestId;
    private int _runRequestId;
    private int _writeRequestId;
    private SelectionSnapshot? _resultsSelection;

    private sealed record SelectionSnapshot(
        int InstalledGameId,
        int DiscoveredFileId,
        string FilePath,
        string FileName,
        string? SourceSha256);
    private List<TranslationResultRow> _allResults = new();

    [ObservableProperty]
    private InstalledGame? _selectedGame;

    [ObservableProperty]
    private DiscoveredFileRow? _selectedFile;

    [ObservableProperty]
    private bool _isGameListOpen;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _hasPreview;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _showOnlyAttention;

    [ObservableProperty]
    private string _sourceLanguage = "English";

    [ObservableProperty]
    private string _targetLanguage = "Turkish";

    [ObservableProperty]
    private string _endpoint = "http://127.0.0.1:1234/v1";

    [ObservableProperty]
    private string _model = "huihui-qwen3-8b-abliterated-v2";

    [ObservableProperty]
    private int _requestTimeoutSeconds = 120;

    [ObservableProperty]
    private int _maxOutputTokens = 4096;

    [ObservableProperty]
    private int _maxUnitsPerBatch = TranslationEngineOptions.DefaultMaxUnitsPerBatch;

    [ObservableProperty]
    private int _maxSourceCharactersPerBatch = TranslationEngineOptions.DefaultMaxSourceCharactersPerBatch;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _warningMessage;

    [ObservableProperty]
    private string? _connectionMessage;

    [ObservableProperty]
    private string? _previewTitle;

    [ObservableProperty]
    private string? _previewPath;

    [ObservableProperty]
    private int _previewTranslatableCount;

    [ObservableProperty]
    private int _previewAmbiguousCount;

    [ObservableProperty]
    private int _previewSkippedCount;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _batchIndex;

    [ObservableProperty]
    private int _batchCount;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _isWriting;

    [ObservableProperty]
    private double _writeProgressPercent;

    [ObservableProperty]
    private string? _writeStatusMessage;

    [ObservableProperty]
    private string? _writeErrorMessage;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private TranslationExportFormatOption _selectedExportFormat = AvailableExportFormats[0];

    [ObservableProperty]
    private string? _exportStatusMessage;

    [ObservableProperty]
    private string? _exportErrorMessage;

    [ObservableProperty]
    private string? _elapsedText;

    [ObservableProperty]
    private string? _metricsText;

    [ObservableProperty]
    private string? _journalPath;

    [ObservableProperty]
    private TranslationResultRow? _selectedResult;

    public bool HasSelectedResult => SelectedResult is not null;

    public ObservableCollection<InstalledGame> Games { get; } = new();

    public ObservableCollection<DiscoveredFileRow> Files { get; } = new();

    public ObservableCollection<TranslationResultRow> Results { get; } = new();

    public IReadOnlyList<TranslationExportFormatOption> ExportFormats => AvailableExportFormats;

    public IReadOnlyList<string> Languages { get; } = new[]
    {
        "English", "Turkish", "German", "French", "Spanish", "Italian",
        "Portuguese", "Russian", "Polish", "Japanese", "Korean", "Chinese"
    };

    public IAsyncRelayCommand PreviewCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ToggleGameListCommand { get; }
    public IAsyncRelayCommand ReloadCommand { get; }
    public IAsyncRelayCommand WriteToFileCommand { get; }
    public RelayCommand CancelWriteCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }

    public TranslationViewModel(
        IInstalledGameScanService installedGameScanService,
        ILocalizationFileScanService fileScanService,
        ILocalizationPreviewService previewService,
        ITranslationEngine translationEngine,
        IHttpClientFactory httpClientFactory,
        TranslationRunJournalStore? journalStore = null,
        ILocalizationWriteService? writeService = null,
        IConfirmationService? confirmationService = null,
        ILocalizationExportService? exportService = null,
        ISaveFileDialogService? saveFileDialogService = null)
    {
        _installedGameScanService = installedGameScanService
            ?? throw new ArgumentNullException(nameof(installedGameScanService));
        _fileScanService = fileScanService
            ?? throw new ArgumentNullException(nameof(fileScanService));
        _previewService = previewService
            ?? throw new ArgumentNullException(nameof(previewService));
        _translationEngine = translationEngine
            ?? throw new ArgumentNullException(nameof(translationEngine));
        _httpClientFactory = httpClientFactory
            ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _journalStore = journalStore ?? new TranslationRunJournalStore();
        _writeService = writeService;
        _confirmationService = confirmationService;
        _exportService = exportService;
        _saveFileDialogService = saveFileDialogService;

        var capabilities = LocalizationAdapterCapabilities.From(previewService.GetAdapterInfo());
        _previewableExtensions = capabilities.PreviewableExtensions;
        _writableExtensions = capabilities.WritableExtensions;
        _writableFormatKeys = capabilities.WritableFormatKeys;

        PreviewCommand = new AsyncRelayCommand(PreviewAsync, CanPreview);
        StartCommand = new AsyncRelayCommand(StartAsync, CanStart);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        ToggleGameListCommand = new RelayCommand(() => IsGameListOpen = !IsGameListOpen);
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
        WriteToFileCommand = new AsyncRelayCommand(WriteToFileAsync, CanWriteToFile);
        CancelWriteCommand = new RelayCommand(CancelWrite, () => IsWriting);
        ExportCommand = new AsyncRelayCommand(ExportAsync, CanExport);

        LoadGames();
    }

    partial void OnSelectedGameChanged(InstalledGame? value)
    {
        IsGameListOpen = false;
        InvalidateSelectionRequests();
        ResetPreview();
        LoadFiles();
    }

    partial void OnSelectedFileChanged(DiscoveredFileRow? value)
    {
        InvalidateSelectionRequests();
        ResetPreview();
        PreviewCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        WriteToFileCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        WriteStatusMessage = null;
        WriteErrorMessage = null;
        WriteProgressPercent = 0;
        ExportStatusMessage = null;
        ExportErrorMessage = null;
    }

    /// <summary>
    /// Seçim değiştiğinde eski önizleme/çeviri isteğini iptal eder ve istek kimliğini artırır.
    /// İptal gecikse bile eski sonuç geçerli seçime uygulanmaz.
    /// </summary>
    private void InvalidateSelectionRequests()
    {
        _previewRequestId++;
        _previewCts?.Cancel();
        _previewCts = null;

        _runRequestId++;
        _operationCts?.Cancel();

        _resultsSelection = null;
    }

    private bool IsCurrentSelection(int installedGameId, int discoveredFileId) =>
        SelectedGame?.Id == installedGameId && SelectedFile?.Id == discoveredFileId;

    private SelectionSnapshot? CaptureSelection(string? sourceSha256 = null) =>
        SelectedGame is { } game && SelectedFile is { } file
            ? new SelectionSnapshot(
                game.Id,
                file.Id,
                file.FilePath,
                file.FileName,
                sourceSha256)
            : null;

    partial void OnIsRunningChanged(bool value)
    {
        IsBusy = value;
        StartCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        WriteToFileCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        PreviewCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        WriteToFileCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsWritingChanged(bool value)
    {
        WriteToFileCommand.NotifyCanExecuteChanged();
        CancelWriteCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsExportingChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();

    partial void OnHasResultsChanged(bool value)
    {
        WriteToFileCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnShowOnlyAttentionChanged(bool value) => ApplyResultFilter();

    partial void OnSelectedResultChanged(TranslationResultRow? value) =>
        OnPropertyChanged(nameof(HasSelectedResult));

    partial void OnSourceLanguageChanged(string value) => StartCommand.NotifyCanExecuteChanged();

    partial void OnTargetLanguageChanged(string value) => StartCommand.NotifyCanExecuteChanged();

    partial void OnEndpointChanged(string value) => StartCommand.NotifyCanExecuteChanged();

    partial void OnModelChanged(string value) => StartCommand.NotifyCanExecuteChanged();

    private void LoadGames()
    {
        var load = LocalizationFileSelectionHelper.LoadInstalledGames(_installedGameScanService);

        Games.Clear();
        foreach (var game in load.Games)
        {
            Games.Add(game);
        }

        ErrorMessage = load.ErrorMessage;
        SelectedGame = Games.FirstOrDefault();
    }

    private void LoadFiles()
    {
        Files.Clear();

        if (SelectedGame is null)
        {
            return;
        }

        try
        {
            var files = _fileScanService
                .GetDiscoveredFilesAsync(SelectedGame.Id)
                .GetAwaiter().GetResult();

            var capabilities = new LocalizationAdapterCapabilities(
                _previewableExtensions,
                _writableExtensions,
                _writableFormatKeys);

            foreach (var file in LocalizationFileSelectionHelper
                         .CreateRows(files, capabilities)
                         .OrderByDescending(row => row.SupportsPreview)
                         .ThenBy(row => row.IsMissing)
                         .ThenBy(row => row.FileName, StringComparer.OrdinalIgnoreCase))
            {
                Files.Add(file);
            }

            SelectedFile = Files.FirstOrDefault(row => row.SupportsPreview) ?? Files.FirstOrDefault();
        }
        catch (Exception exception)
        {
            ErrorMessage = "Keşfedilmiş dosyalar yüklenemedi: " + exception.Message;
        }
    }

    private void ResetPreview()
    {
        _preview = null;
        _resultsSelection = null;
        HasPreview = false;
        PreviewTitle = null;
        PreviewPath = null;
        PreviewTranslatableCount = 0;
        PreviewAmbiguousCount = 0;
        PreviewSkippedCount = 0;
        HasResults = false;
        _allResults = new List<TranslationResultRow>();
        Results.Clear();
        SelectedResult = null;
        StatusMessage = null;
        ErrorMessage = null;
        WarningMessage = null;
        MetricsText = null;
        JournalPath = null;
        ProgressPercent = 0;
        CompletedCount = 0;
        TotalCount = 0;
        WriteStatusMessage = null;
        WriteErrorMessage = null;
        WriteProgressPercent = 0;
        ExportStatusMessage = null;
        ExportErrorMessage = null;
        StartCommand.NotifyCanExecuteChanged();
        WriteToFileCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    private bool CanPreview() =>
        SelectedFile?.SupportsPreview == true && !IsBusy && !IsRunning;

    private bool CanStart() =>
        SelectedFile?.SupportsPreview == true
        && !IsBusy
        && !IsRunning
        && !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(Model)
        && !string.Equals(SourceLanguage, TargetLanguage, StringComparison.OrdinalIgnoreCase);

    private bool CanExport() =>
        _exportService is not null
        && _saveFileDialogService is not null
        && HasResults
        && !IsBusy
        && !IsRunning
        && !IsWriting
        && !IsExporting
        && _allResults.Any(IsWritableTranslation);

    private async Task ExportAsync()
    {
        if (!CanExport() || _exportService is null || _saveFileDialogService is null)
        {
            return;
        }

        var entries = _allResults
            .Where(IsWritableTranslation)
            .Select(row => new LocalizationExportEntry(
                row.EntryKey,
                row.SourceText,
                row.TranslationText))
            .ToList();

        if (entries.Count == 0)
        {
            ExportErrorMessage = "Dışa aktarılacak aday veya yeniden kullanılmış çeviri yok.";
            return;
        }

        var baseName = SelectedFile?.FileName ?? "translations";
        var defaultFileName = Path.GetFileNameWithoutExtension(baseName) + SelectedExportFormat.Extension;
        var targetPath = _saveFileDialogService.PickSaveFile(
            "Çevirileri Dışa Aktar",
            defaultFileName,
            SelectedExportFormat.Filter,
            SelectedExportFormat.Extension);

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            ExportStatusMessage = "Dışa aktarma kullanıcı tarafından iptal edildi.";
            ExportErrorMessage = null;
            return;
        }

        if (SelectedFile is not null
            && string.Equals(
                Path.GetFullPath(targetPath),
                Path.GetFullPath(SelectedFile.FilePath),
                StringComparison.OrdinalIgnoreCase))
        {
            ExportErrorMessage = "Dışa aktarma hedefi orijinal oyun dosyası olamaz; yeni bir dosya seçin.";
            ExportStatusMessage = null;
            return;
        }

        IsExporting = true;
        ExportStatusMessage = "Çeviriler dışa aktarılıyor…";
        ExportErrorMessage = null;

        try
        {
            var result = await _exportService
                .ExportAsync(targetPath, SelectedExportFormat.Format, entries)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                ExportErrorMessage = result.ErrorMessage ?? "Dışa aktarma başarısız.";
                ExportStatusMessage = null;
                return;
            }

            var warningText = result.Warnings.Count == 0
                ? string.Empty
                : " Uyarı: " + string.Join(" | ", result.Warnings.Take(3));
            ExportStatusMessage =
                $"{result.EntryCount} çeviri '{targetPath}' dosyasına aktarıldı.{warningText}";
        }
        catch (Exception exception)
        {
            ExportErrorMessage = "Dışa aktarma başarısız: " + exception.Message;
            ExportStatusMessage = null;
        }
        finally
        {
            IsExporting = false;
        }
    }

    private bool CanWriteToFile() =>
        _writeService is not null
        && _confirmationService is not null
        && SelectedFile?.SupportsWrite == true
        && _resultsSelection is { } resultSelection
        && IsCurrentSelection(resultSelection.InstalledGameId, resultSelection.DiscoveredFileId)
        && HasResults
        && !IsBusy
        && !IsRunning
        && !IsWriting
        && _allResults.Any(IsWritableTranslation);

    private static bool IsWritableTranslation(TranslationResultRow row) =>
        row.Status is TranslationUnitStatus.Candidate or TranslationUnitStatus.Reused
        && !string.IsNullOrWhiteSpace(row.TranslationText);

    private async Task WriteToFileAsync()
    {
        if (!CanWriteToFile()
            || _resultsSelection is not { } selection
            || _writeService is null)
        {
            return;
        }

        var translations = _allResults
            .Where(IsWritableTranslation)
            .ToDictionary(row => row.EntryKey, row => row.TranslationText, StringComparer.Ordinal);

        if (translations.Count == 0)
        {
            WriteErrorMessage = "Yazılabilecek aday veya yeniden kullanılmış çeviri yok.";
            return;
        }

        var requestId = ++_writeRequestId;
        var cts = new CancellationTokenSource();
        _writeCts = cts;

        try
        {
            var progress = new Progress<LocalizationWriteProgress>(
                value => ApplyWriteProgress(requestId, selection, value));
            var outcome = await LocalizationWriteFlow.ExecuteAsync(
                    _confirmationService!,
                    _writeService,
                    selection.InstalledGameId,
                    selection.DiscoveredFileId,
                    translations,
                    selection.FileName,
                    "Dosyaya Yaz",
                    "çeviri yazılacak.",
                    "Yazma işlemi kullanıcı tarafından iptal edildi.",
                    "Yazma işlemi iptal edildi; orijinal dosya korunmalıdır.",
                    "Yazma başarısız: ",
                    () =>
                    {
                        IsWriting = true;
                        IsBusy = true;
                        WriteErrorMessage = null;
                        WriteProgressPercent = 0;
                        WriteStatusMessage = "Güvenli yazma hazırlanıyor…";
                    },
                    progress,
                    selection.SourceSha256,
                    cts.Token)
                .ConfigureAwait(true);

            if (requestId != _writeRequestId
                || !ReferenceEquals(_writeCts, cts)
                || !IsCurrentSelection(selection.InstalledGameId, selection.DiscoveredFileId))
            {
                return;
            }

            WriteProgressPercent = outcome.ProgressPercent;
            WriteStatusMessage = outcome.StatusMessage;
            WriteErrorMessage = outcome.ErrorMessage;
        }
        finally
        {
            cts.Dispose();

            if (ReferenceEquals(_writeCts, cts))
            {
                _writeCts = null;
                IsWriting = false;
                IsBusy = false;
            }
        }
    }

    private void CancelWrite()
    {
        if (!IsWriting)
        {
            return;
        }

        WriteStatusMessage = "Yazma iptal ediliyor…";
        _writeCts?.Cancel();
    }

    private void ApplyWriteProgress(
        int requestId,
        SelectionSnapshot selection,
        LocalizationWriteProgress progress)
    {
        if (!IsWriting
            || requestId != _writeRequestId
            || !IsCurrentSelection(selection.InstalledGameId, selection.DiscoveredFileId))
        {
            return;
        }

        WriteProgressPercent = Math.Clamp(progress.Percent, 0, 100);
        WriteStatusMessage = progress.Message;
    }

    private async Task StartAsync()
    {
        var selection = CaptureSelection();

        if (selection is null)
        {
            return;
        }

        _operationCts?.Cancel();
        var requestId = ++_runRequestId;
        var cts = new CancellationTokenSource();
        _operationCts = cts;

        IsRunning = true;
        IsBusy = true;
        ErrorMessage = null;
        WarningMessage = null;
        MetricsText = null;
        HasResults = false;
        _resultsSelection = null;
        ProgressPercent = 0;
        CompletedCount = 0;
        TotalCount = 0;

        try
        {
            StatusMessage = "Dosya önizlemesi hazırlanıyor…";
            var preview = await EnsurePreviewAsync(selection, cts.Token).ConfigureAwait(true);

            if (!IsCurrentRun(requestId, selection, cts) || preview is null)
            {
                return;
            }

            if (preview.Truncated)
            {
                ErrorMessage =
                    "Önizleme kayıt sınırı nedeniyle kesildi; eksik dosya çevrilmez. " +
                    "Daha küçük bir dosya seçin veya okuma sınırını artırın.";
                StatusMessage = null;
                return;
            }

            var profile = BuildProfile(TimeSpan.FromSeconds(Clamp(RequestTimeoutSeconds, 5, 600)));
            var sourceSha = preview.SourceSha256;

            if (string.IsNullOrWhiteSpace(sourceSha))
            {
                sourceSha = await FileFingerprint
                    .ComputeSha256Async(selection.FilePath, cts.Token)
                    .ConfigureAwait(true);
            }

            selection = selection with { SourceSha256 = sourceSha };

            var options = new TranslationEngineOptions
            {
                MaxUnitsPerBatch = Clamp(MaxUnitsPerBatch, 1, 100),
                MaxSourceCharactersPerBatch = Clamp(MaxSourceCharactersPerBatch, 500, 100_000)
            };

            var request = new TranslationEngineRequest
            {
                InstalledGameId = selection.InstalledGameId,
                DiscoveredFileId = selection.DiscoveredFileId,
                FilePath = selection.FilePath,
                FileFormat = preview.FormatKey,
                SourceFileSha256 = sourceSha,
                Entries = preview.Entries,
                SourceTruncated = preview.Truncated,
                SourceLanguage = SourceLanguage,
                TargetLanguage = TargetLanguage,
                Provider = profile,
                JournalDirectory = _journalStore.DirectoryPath,
                Options = options
            };

            var client = _httpClientFactory.CreateClient("translation");
            var provider = OpenAiCompatibleTranslationProviderFactory.Create(
                client,
                profile,
                new EnvironmentTranslationCredentialProvider());

            var progress = new Progress<TranslationRunProgress>(
                value => ApplyProgress(requestId, selection, value));
            StatusMessage = "Çeviri başlatıldı; ilk parti hazırlanıyor…";

            var result = await _translationEngine
                .RunAsync(request, provider, progress, cts.Token)
                .ConfigureAwait(true);

            if (!IsCurrentRun(requestId, selection, cts))
            {
                return;
            }

            ApplyRunResult(result, selection);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (IsCurrentRun(requestId, selection, cts))
            {
                StatusMessage = "Çeviri iptal edildi; tamamlanan partiler günlükte korundu.";
            }
        }
        catch (Exception exception)
        {
            if (!IsCurrentRun(requestId, selection, cts))
            {
                return;
            }

            ErrorMessage = "Çeviri başlatılamadı: " + exception.Message;
            StatusMessage = null;
        }
        finally
        {
            cts.Dispose();

            if (ReferenceEquals(_operationCts, cts))
            {
                _operationCts = null;
                IsRunning = false;
                IsBusy = false;
            }
        }
    }

    private bool IsCurrentRun(
        int requestId,
        SelectionSnapshot selection,
        CancellationTokenSource cts) =>
        requestId == _runRequestId
        && ReferenceEquals(_operationCts, cts)
        && !cts.IsCancellationRequested
        && IsCurrentSelection(selection.InstalledGameId, selection.DiscoveredFileId);

    private async Task<LocalizationPreviewResult?> EnsurePreviewAsync(
        SelectionSnapshot selection,
        CancellationToken cancellationToken)
    {
        if (_preview is not null
            && _preview.Success
            && _preview.InstalledGameId == selection.InstalledGameId
            && _preview.DiscoveredFileId == selection.DiscoveredFileId)
        {
            return _preview;
        }

        var result = await _previewService
            .PreviewAsync(selection.InstalledGameId, selection.DiscoveredFileId, cancellationToken)
            .ConfigureAwait(true);

        if (!IsCurrentSelection(selection.InstalledGameId, selection.DiscoveredFileId)
            || result.InstalledGameId != selection.InstalledGameId
            || result.DiscoveredFileId != selection.DiscoveredFileId)
        {
            return null;
        }

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Dosya önizlenemedi.";
            StatusMessage = null;
            return null;
        }

        _preview = result;
        HasPreview = true;
        PreviewTitle = result.FileName;
        PreviewPath = result.FilePath;
        PreviewTranslatableCount = result.TranslatableCount;
        PreviewAmbiguousCount = result.AmbiguousCount;
        PreviewSkippedCount = result.SkippedCount;
        return result;
    }

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));

    private TranslationProviderProfile BuildProfile(TimeSpan timeout) =>
        OpenAiCompatibleTranslationProviderFactory.CreateProfile(
            "lmstudio-local",
            Endpoint,
            Model,
            "Yerel LM Studio",
            new TranslationGenerationOptions
            {
                Temperature = 0.2,
                MaxOutputTokens = Clamp(MaxOutputTokens, 256, 16_384),
                Timeout = timeout
            });

    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        ConnectionMessage = "Bağlantı sınanıyor…";
        ErrorMessage = null;

        try
        {
            var profile = BuildProfile(TimeSpan.FromSeconds(30));
            var client = _httpClientFactory.CreateClient("translation");
            string? apiKey = null;
            try
            {
                apiKey = new EnvironmentTranslationCredentialProvider().GetApiKey(profile.ProviderId);
            }
            catch
            {
                // Anahtar yoksa yerel sunucu anahtarsız çalışabilir; sır dışarı taşınmaz.
            }

            var result = await OpenAiCompatibleConnectionProbe
                .ProbeAsync(client, profile, apiKey, CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                ConnectionMessage = "Bağlantı başarısız: " + result.ErrorMessage;
                return;
            }

            if (result.ModelAvailable)
            {
                ConnectionMessage =
                    $"Bağlantı başarılı. Model hazır: {profile.Model} " +
                    $"({result.Duration.TotalMilliseconds:0} ms).";
            }
            else
            {
                var models = result.Models.Count == 0
                    ? "model listesi boş"
                    : string.Join(", ", result.Models.Take(5));
                ConnectionMessage =
                    $"Sunucu erişilebilir ancak '{profile.Model}' listede yok. " +
                    $"Bulunan modeller: {models}";
            }
        }
        catch (Exception exception)
        {
            ConnectionMessage = "Bağlantı sınaması başarısız: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Cancel()
    {
        if (_operationCts is null)
        {
            return;
        }

        StatusMessage = "İptal isteniyor; tamamlanan partiler korunacak…";
        _operationCts.Cancel();
    }

    private async Task ReloadAsync()
    {
        await Task.Yield();
        LoadGames();
        LoadFiles();
    }

    private async Task PreviewAsync()
    {
        if (SelectedGame is not { } game || SelectedFile is not { } file)
        {
            return;
        }

        _previewCts?.Cancel();
        var requestId = ++_previewRequestId;
        var cts = new CancellationTokenSource();
        _previewCts = cts;

        IsBusy = true;
        HasPreview = false;
        ErrorMessage = null;
        WarningMessage = null;
        StatusMessage = "Önizleme salt okunur hazırlanıyor…";

        try
        {
            var result = await _previewService
                .PreviewAsync(game.Id, file.Id, cts.Token)
                .ConfigureAwait(true);

            if (requestId != _previewRequestId
                || !ReferenceEquals(_previewCts, cts)
                || cts.IsCancellationRequested
                || !IsCurrentSelection(game.Id, file.Id)
                || result.InstalledGameId != game.Id
                || result.DiscoveredFileId != file.Id)
            {
                return;
            }

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Dosya önizlenemedi.";
                StatusMessage = null;
                return;
            }

            _preview = result;
            HasPreview = true;
            PreviewTitle = result.FileName;
            PreviewPath = result.FilePath;
            PreviewTranslatableCount = result.TranslatableCount;
            PreviewAmbiguousCount = result.AmbiguousCount;
            PreviewSkippedCount = result.SkippedCount;
            WarningMessage = result.Warnings.Count == 0
                ? null
                : string.Join(" | ", result.Warnings.Take(5));
            StatusMessage =
                $"{result.TranslatableCount} çevrilebilir, {result.AmbiguousCount} belirsiz, " +
                $"{result.SkippedCount} atlandı. Ayrıştırma: {result.Duration.TotalMilliseconds:0} ms."
                + (result.Truncated ? " DİKKAT: kayıt sınırı nedeniyle kesildi." : string.Empty);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Seçim değişti; eski önizleme sonucu uygulanmaz.
        }
        catch (Exception exception)
        {
            if (requestId != _previewRequestId || !IsCurrentSelection(game.Id, file.Id))
            {
                return;
            }

            ErrorMessage = "Önizleme başarısız: " + exception.Message;
            StatusMessage = null;
        }
        finally
        {
            cts.Dispose();

            if (ReferenceEquals(_previewCts, cts))
            {
                _previewCts = null;
                IsBusy = false;
                PreviewCommand.NotifyCanExecuteChanged();
                StartCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private void ApplyProgress(
        int requestId,
        SelectionSnapshot selection,
        TranslationRunProgress progress)
    {
        // Progress<T> callback'i iptal/seçim değişikliğinden sonra gelebilir.
        if (!IsRunning
            || requestId != _runRequestId
            || !IsCurrentSelection(selection.InstalledGameId, selection.DiscoveredFileId))
        {
            return;
        }

        CompletedCount = progress.Completed;
        TotalCount = progress.Total;
        BatchIndex = progress.BatchIndex;
        BatchCount = progress.BatchCount;
        ProgressPercent = progress.Total <= 0
            ? 0
            : Math.Clamp(progress.Completed * 100.0 / progress.Total, 0, 100);
        ElapsedText = FormatElapsed(progress.Elapsed);

        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            StatusMessage = progress.Message;
        }
    }

    private void ApplyRunResult(TranslationRunResult result, SelectionSnapshot selection)
    {
        if (!IsRunning || !IsCurrentSelection(selection.InstalledGameId, selection.DiscoveredFileId))
        {
            return;
        }

        _allResults = result.Units
            .Select(unit => new TranslationResultRow(unit))
            .ToList();
        _resultsSelection = selection;
        ApplyResultFilter();

        HasResults = _allResults.Count > 0;
        CompletedCount = result.Units.Count(unit => unit.Status != TranslationUnitStatus.Skipped);
        TotalCount = result.Metrics.TranslatableRecords;
        ProgressPercent = TotalCount <= 0
            ? 0
            : Math.Clamp(CompletedCount * 100.0 / TotalCount, 0, 100);
        ElapsedText = FormatElapsed(result.Metrics.TotalDuration);
        JournalPath = result.JournalPath;
        ErrorMessage = result.ErrorMessage;
        WarningMessage = result.Warnings.Count == 0
            ? null
            : string.Join(" | ", result.Warnings.Take(6));

        var resumePrefix = result.Resumed ? "Önceki çalışma sürdürüldü. " : string.Empty;
        StatusMessage = resumePrefix + result.Status switch
        {
            TranslationRunStatus.Completed => "Çeviri tamamlandı.",
            TranslationRunStatus.CompletedWithReview =>
                "Çeviri tamamlandı; inceleme gerektiren veya başarısız kayıtlar var.",
            TranslationRunStatus.Cancelled =>
                "Çalışma iptal edildi; tamamlanan partiler günlükte korundu.",
            _ => "Çalışma başarısız."
        };

        var metrics = result.Metrics;
        MetricsText = string.Join(
            Environment.NewLine,
            $"Kayıt: {metrics.TotalRecords} toplam · {metrics.TranslatableRecords} çevrilebilir · " +
            $"{metrics.CandidateRecords} aday · {metrics.ReusedRecords} yeniden kullanıldı · " +
            $"{metrics.NeedsReviewRecords} inceleme · {metrics.FailedRecords} başarısız · " +
            $"{metrics.SkippedRecords} atlandı",
            $"İstek: {metrics.RequestCount} · retry: {metrics.RetryCount} · parti: {metrics.BatchCount} · " +
            $"ortalama {metrics.AverageRequestDuration.TotalMilliseconds:0} ms",
            $"Süre: {metrics.TotalDuration.TotalSeconds:0.0} s · " +
            $"{metrics.RecordsPerSecond:0.00} kayıt/s · {metrics.CharactersPerSecond:0.0} karakter/s",
            $"Token: giriş {metrics.InputTokens?.ToString() ?? "—"} · " +
            $"çıkış {metrics.OutputTokens?.ToString() ?? "—"}",
            $"Model: {metrics.Model} · istem: {metrics.PromptVersion} · sağlayıcı: {metrics.ProviderId}");
    }

    private void ApplyResultFilter()
    {
        Results.Clear();
        foreach (var row in _allResults)
        {
            if (!ShowOnlyAttention || row.NeedsAttention)
            {
                Results.Add(row);
            }
        }

        SelectedResult = Results.FirstOrDefault();
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
}
