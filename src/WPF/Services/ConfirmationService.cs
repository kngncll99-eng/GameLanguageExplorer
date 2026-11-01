using System.Windows;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>ViewModel'i doğrudan MessageBox API'sine bağlamadan kullanıcı onayı alır.</summary>
public interface IConfirmationService
{
    /// <summary>Kullanıcı açıkça onaylarsa <c>true</c> döner.</summary>
    bool Confirm(string title, string message);
}

/// <summary>Varsayılan WPF onay penceresi; varsayılan seçenek "Hayır"dır.</summary>
public sealed class MessageBoxConfirmationService : IConfirmationService
{
    public bool Confirm(string title, string message)
        => MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
}
