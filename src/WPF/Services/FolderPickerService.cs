using Microsoft.Win32;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>Kullanıcıdan klasör seçimi ister (MVVM'de ViewModel'i UI API'sine bağlamamak için).</summary>
public interface IFolderPickerService
{
    /// <summary>Klasör seçilirse tam yolu, iptal edilirse <c>null</c> döner.</summary>
    string? PickFolder(string title);
}

/// <summary>Windows klasör seçim diyaloğu (<c>OpenFolderDialog</c>; yönetici izni gerektirmez).</summary>
public sealed class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Oyun klasörünü seçin" : title,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
