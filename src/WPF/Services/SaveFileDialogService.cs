using Microsoft.Win32;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>Dışa aktarma hedef dosyasını seçmek için MVVM dostu dosya kaydetme servisi.</summary>
public interface ISaveFileDialogService
{
    /// <summary>Dosya seçilirse tam yolu, iptal edilirse <c>null</c> döner.</summary>
    string? PickSaveFile(
        string title,
        string defaultFileName,
        string filter,
        string defaultExtension);
}

/// <summary>Windows SaveFileDialog tabanlı varsayılan uygulama.</summary>
public sealed class SaveFileDialogService : ISaveFileDialogService
{
    public string? PickSaveFile(
        string title,
        string defaultFileName,
        string filter,
        string defaultExtension)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = filter,
            DefaultExt = defaultExtension,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
