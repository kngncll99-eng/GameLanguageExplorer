using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLanguageExplorer.WPF.Services;

namespace GameLanguageExplorer.WPF.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    private object _currentViewModel = null!;

    public object CurrentViewModel
    {
        get => _currentViewModel;
        set => SetProperty(ref _currentViewModel, value);
    }

    /// <summary>İçerik alanındaki bölüm başlığı.</summary>
    [ObservableProperty]
    private string _currentSectionTitle = "Global Oyun Kataloğu";

    /// <summary>Koyu tema etkin mi?</summary>
    [ObservableProperty]
    private bool _isDarkTheme = ThemeManager.IsDark;

    public string ThemeToggleText => IsDarkTheme ? "Açık temaya geç" : "Koyu temaya geç";

    public string ThemeStateText => IsDarkTheme ? "Koyu tema" : "Açık tema";

    public IRelayCommand NavigateToCatalogCommand { get; }
    public IRelayCommand NavigateToInstalledCommand { get; }
    public IRelayCommand NavigateToFileDiscoveryCommand { get; }
    public IRelayCommand NavigateToFormatsCommand { get; }
    public IRelayCommand NavigateToEditorCommand { get; }
    public IRelayCommand NavigateToTranslationCommand { get; }
    public IRelayCommand NavigateToBackupCommand { get; }
    public IRelayCommand NavigateToSettingsCommand { get; }
    public IRelayCommand ToggleThemeCommand { get; }

    private readonly TranslationViewModel _translationViewModel;
    private readonly EditorViewModel _editorViewModel;
    private readonly CatalogViewModel _catalogViewModel;
    private readonly InstalledGamesViewModel _installedGamesViewModel;
    private readonly FileDiscoveryViewModel _fileDiscoveryViewModel;

    public ShellViewModel(
        CatalogViewModel catalogViewModel,
        InstalledGamesViewModel installedGamesViewModel,
        FileDiscoveryViewModel fileDiscoveryViewModel,
        TranslationViewModel translationViewModel,
        EditorViewModel editorViewModel)
    {
        _catalogViewModel = catalogViewModel;
        _installedGamesViewModel = installedGamesViewModel;
        _fileDiscoveryViewModel = fileDiscoveryViewModel;
        _translationViewModel = translationViewModel;
        _editorViewModel = editorViewModel;

        NavigateToCatalogCommand = new RelayCommand(() =>
        {
            CurrentViewModel = _catalogViewModel;
            CurrentSectionTitle = "Global Oyun Kataloğu";
        });

        NavigateToInstalledCommand = new RelayCommand(() =>
        {
            CurrentViewModel = _installedGamesViewModel;
            CurrentSectionTitle = "Kurulu Oyunlar";
        });

        NavigateToFileDiscoveryCommand = new RelayCommand(() =>
        {
            CurrentViewModel = _fileDiscoveryViewModel;
            CurrentSectionTitle = "Dil Dosyası Keşfi";
        });

        NavigateToFormatsCommand = new RelayCommand(() => { /* yer tutucu */ });
        NavigateToEditorCommand = new RelayCommand(() =>
        {
            CurrentViewModel = _editorViewModel;
            CurrentSectionTitle = "Yerelleştirme Editörü";
        });
        NavigateToTranslationCommand = new RelayCommand(() =>
        {
            CurrentViewModel = _translationViewModel;
            CurrentSectionTitle = "AI Çeviri";
        });
        NavigateToBackupCommand = new RelayCommand(() => { /* yer tutucu */ });
        NavigateToSettingsCommand = new RelayCommand(() => { /* yer tutucu */ });

        ToggleThemeCommand = new RelayCommand(ToggleTheme);

        CurrentViewModel = _catalogViewModel;
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        OnPropertyChanged(nameof(ThemeToggleText));
        OnPropertyChanged(nameof(ThemeStateText));
    }

    private void ToggleTheme()
    {
        var theme = ThemeManager.Toggle();
        IsDarkTheme = theme == AppTheme.Dark;
    }
}
