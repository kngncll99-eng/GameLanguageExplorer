using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services;
using GameLanguageExplorer.Core.Services.Localization;
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

/// <summary>
/// "AI Çeviri" ekranı: keşfedilmiş dosya seçimi, salt okunur önizleme, yerel LM Studio
/// bağlantı sınaması, partili çeviri, iptal, devam ve sonuç inceleme.
/// </summary>
public partial class TranslationViewModel : ViewModelBase
{
    private readonly IInstalledGameScanService _installedGameScanService;
    private readonly ILocalizationFileScanService _fileScanService;
    private readonly ILocalizationPreviewService _previewService;
    private readonly ITranslationEngine _translationEngine;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TranslationRunJournalStore _journalStore;
    private readonly IReadOnlyCollection<string> _previewableExtensions;
    private readonly IReadOnlyCollection<string> _writableExtensions;
    private readonly ILocalizationWriteService? _writeService;
    private readonly IConfirmationService? _confirmationService;

    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _writeCts;
    private LocalizationPreviewResult? _preview;
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

    public TranslationViewModel(
        IInstalledGameScanService installedGameScanService,
        ILocalizationFileScanService fileScanService,
        ILocalizationPreviewService previewService,
        ITranslationEngine translationEngine,
        IHttpClientFactory httpClientFactory,
        TranslationRunJournalStore? journalStore = null,
        ILocalizationWriteService? writeService = null,
        IConfirmationService? confirmationService = null)
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

        var adapterInfo = previewService.GetAdapterInfo();
        _previewableExtensions = adapterInfo
            .SelectMany(adapter => adapter.Extensions)
            .ToArray();
        _writableExtensions = adapterInfo
            .Where(adapter => adapter.SupportsWrite)
            .SelectMany(adapter => adapter.Extensions)
            .ToArray();

        PreviewCommand = new AsyncRelayCommand(PreviewAsync, CanPreview);
        StartCommand = new AsyncRelayCommand(StartAsync, CanStart);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        ToggleGameListCommand = new RelayCommand(() => IsGameListOpen = !IsGameListOpen);
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
        WriteToFileCommand = new AsyncRelayCommand(WriteToFileAsync, CanWriteToFile);
        CancelWriteCommand = new RelayCommand(CancelWrite, () => IsWriting);

        LoadGames();
    }

    partial void OnSelectedGameChanged(InstalledGame? value)
    {
        IsGameListOpen = false;
        ResetPreview();
        LoadFiles();
    }

    partial void OnSelectedFileChanged(DiscoveredFileRow? value)
    {
        ResetPreview();
        PreviewCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        WriteToFileCommand.NotifyCanExecuteChanged();
        WriteStatusMessage = null;
        WriteErrorMessage = null;
        WriteProgressPercent = 0;
    }

    partial void OnIsRunningChanged(bool value)
    {
        IsBusy = value;
        StartCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        WriteToFileCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        PreviewCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        WriteToFileCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsWritingChanged(bool value)
    {
        WriteToFileCommand.NotifyCanExecuteChanged();
        CancelWriteCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasResultsChanged(bool value) => WriteToFileCommand.NotifyCanExecuteChanged();

    partial void OnShowOnlyAttentionChanged(bool value) => ApplyResultFilter();

    partial void OnSelectedResultChanged(TranslationResultRow? value) =>
        OnPropertyChanged(nameof(HasSelectedResult));

    partial void OnSourceLanguageChanged(string value) => StartCommand.NotifyCanExecuteChanged();

    partial void OnTargetLanguageChanged(string value) => StartCommand.NotifyCanExecuteChanged();

    partial void OnEndpointChanged(string value) => StartCommand.NotifyCanExecuteChanged();

    partial void OnModelChanged(string value) => StartCommand.NotifyCanExecuteChanged();

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
            return;
        }

        try
        {
            var files = _fileScanService
                .GetDiscoveredFilesAsync(SelectedGame.Id)
                .GetAwaiter().GetResult();

            foreach (var file in files
                         .Select(file => new DiscoveredFileRow(file, _previewableExtensions, _writableExtensions))
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
        StartCommand.NotifyCanExecuteChanged();
        WriteToFileCommand.NotifyCanExecuteChanged();
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

    private bool CanWriteToFile() =>
        _writeService is not null
        && _confirmationService is not null
        && SelectedGame is not null
        && SelectedFile?.SupportsWrite == true
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
        if (!CanWriteToFile() || SelectedGame is null || SelectedFile is null || _writeService is null)
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

        var confirmed = _confirmationService?.Confirm(
            "Dosyaya Yaz",
            $"'{SelectedFile.FileName}' dosyasındaki {translations.Count} çeviri yazılacak.\n\n"
            + "Orijinal dosya değişecek ve işlem öncesi yedek alınacak. Devam etmek istiyor musunuz?");

        if (confirmed != true)
        {
            WriteStatusMessage = "Yazma işlemi kullanıcı tarafından iptal edildi.";
            WriteErrorMessage = null;
            return;
        }

        IsWriting = true;
        IsBusy = true;
        WriteErrorMessage = null;
        WriteProgressPercent = 0;
        WriteStatusMessage = "Güvenli yazma hazırlanıyor…";
        _writeCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<LocalizationWriteProgress>(ApplyWriteProgress);
            var result = await _writeService.WriteAsync(
                    SelectedGame.Id,
                    SelectedFile.Id,
                    translations,
                    _writeCts.Token,
                    progress)
                .ConfigureAwait(true);

            ApplyWriteResult(result);
        }
        catch (OperationCanceledException)
        {
            WriteStatusMessage = "Yazma işlemi iptal edildi; orijinal dosya korunmalıdır.";
            WriteProgressPercent = 0;
        }
        catch (Exception exception)
        {
            WriteErrorMessage = "Yazma başarısız: " + exception.Message;
            WriteStatusMessage = null;
            WriteProgressPercent = 0;
        }
        finally
        {
            _writeCts?.Dispose();
            _writeCts = null;
            IsWriting = false;
            IsBusy = false;
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

    private void ApplyWriteProgress(LocalizationWriteProgress progress)
    {
        WriteProgressPercent = Math.Clamp(progress.Percent, 0, 100);
        WriteStatusMessage = progress.Message;
    }

    private void ApplyWriteResult(LocalizationWriteResult result)
    {
        if (result.Success)
        {
            WriteProgressPercent = 100;
            WriteErrorMessage = null;
            WriteStatusMessage = result.BackupFilePath is null
                ? result.Message
                : $"{result.Message} Yedek: {result.BackupFilePath}";
            return;
        }

        WriteProgressPercent = 0;
        WriteErrorMessage = result.Message;
        WriteStatusMessage = result.Status == LocalizationWriteStatus.Restored
            ? "Orijinal dosya yedekten geri yüklendi."
            : null;
    }

    private async Task StartAsync()
    {
        if (SelectedGame is null || SelectedFile is null)
        {
            return;
        }

        IsRunning = true;
        IsBusy = true;
        ErrorMessage = null;
        WarningMessage = null;
        MetricsText = null;
        HasResults = false;
        ProgressPercent = 0;
        CompletedCount = 0;
        TotalCount = 0;
        _operationCts = new CancellationTokenSource();

        try
        {
            StatusMessage = "Dosya önizlemesi hazırlanıyor…";
            if (!await EnsurePreviewAsync().ConfigureAwait(true))
            {
                return;
            }

            if (_preview!.Truncated)
            {
                ErrorMessage =
                    "Önizleme kayıt sınırı nedeniyle kesildi; eksik dosya çevrilmez. " +
                    "Daha küçük bir dosya seçin veya okuma sınırını artırın.";
                StatusMessage = null;
                return;
            }

            var profile = BuildProfile(TimeSpan.FromSeconds(Clamp(RequestTimeoutSeconds, 5, 600)));
            var sourceSha = FileFingerprint.TryNormalizeSha256(SelectedFile.Sha256, out var normalized)
                ? normalized
                : await FileFingerprint.ComputeSha256Async(SelectedFile.FilePath, _operationCts.Token)
                    .ConfigureAwait(true);

            var options = new TranslationEngineOptions
            {
                MaxUnitsPerBatch = Clamp(MaxUnitsPerBatch, 1, 100),
                MaxSourceCharactersPerBatch = Clamp(MaxSourceCharactersPerBatch, 500, 100_000)
            };

            var request = new TranslationEngineRequest
            {
                InstalledGameId = SelectedGame.Id,
                DiscoveredFileId = SelectedFile.Id,
                FilePath = SelectedFile.FilePath,
                FileFormat = _preview.FormatKey,
                SourceFileSha256 = sourceSha,
                Entries = _preview.Entries,
                SourceTruncated = _preview.Truncated,
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

            var progress = new Progress<TranslationRunProgress>(ApplyProgress);
            StatusMessage = "Çeviri başlatıldı; ilk parti hazırlanıyor…";

            var result = await _translationEngine
                .RunAsync(request, provider, progress, _operationCts.Token)
                .ConfigureAwait(true);

            ApplyRunResult(result);
        }
        catch (Exception exception)
        {
            ErrorMessage = "Çeviri başlatılamadı: " + exception.Message;
            StatusMessage = null;
        }
        finally
        {
            _operationCts?.Dispose();
            _operationCts = null;
            IsRunning = false;
            IsBusy = false;
        }
    }

    private async Task<bool> EnsurePreviewAsync()
    {
        if (_preview is not null
            && _preview.Success
            && _preview.DiscoveredFileId == SelectedFile?.Id)
        {
            return true;
        }

        var result = await _previewService
            .PreviewAsync(SelectedGame!.Id, SelectedFile!.Id, _operationCts?.Token ?? CancellationToken.None)
            .ConfigureAwait(true);

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Dosya önizlenemedi.";
            StatusMessage = null;
            return false;
        }

        _preview = result;
        HasPreview = true;
        PreviewTitle = result.FileName;
        PreviewPath = result.FilePath;
        PreviewTranslatableCount = result.TranslatableCount;
        PreviewAmbiguousCount = result.AmbiguousCount;
        PreviewSkippedCount = result.SkippedCount;
        return true;
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
        if (SelectedGame is null || SelectedFile is null)
        {
            return;
        }

        IsBusy = true;
        HasPreview = false;
        ErrorMessage = null;
        WarningMessage = null;
        StatusMessage = "Önizleme salt okunur hazırlanıyor…";

        try
        {
            var result = await _previewService
                .PreviewAsync(SelectedGame.Id, SelectedFile.Id, CancellationToken.None)
                .ConfigureAwait(true);

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
        catch (Exception exception)
        {
            ErrorMessage = "Önizleme başarısız: " + exception.Message;
            StatusMessage = null;
        }
        finally
        {
            IsBusy = false;
            PreviewCommand.NotifyCanExecuteChanged();
            StartCommand.NotifyCanExecuteChanged();
        }
    }

    private void ApplyProgress(TranslationRunProgress progress)
    {
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

    private void ApplyRunResult(TranslationRunResult result)
    {
        _allResults = result.Units
            .Select(unit => new TranslationResultRow(unit))
            .ToList();
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
