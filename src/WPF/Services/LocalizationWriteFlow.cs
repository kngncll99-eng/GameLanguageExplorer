using GameLanguageExplorer.Core.Services.Localization;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>Onaylı güvenli yazma akışının ViewModel'den bağımsız sonucu.</summary>
public sealed record LocalizationWriteOutcome(
    bool Confirmed,
    bool Success,
    LocalizationWriteResult? Result,
    string? StatusMessage,
    string? ErrorMessage,
    int ProgressPercent);

/// <summary>
/// Translation ve Editor ekranlarının ortak onay + yazma akışını yürütür.
/// ViewModel'e özgü progress/command durumları çağıran tarafta kalır.
/// </summary>
public static class LocalizationWriteFlow
{
    public static async Task<LocalizationWriteOutcome> ExecuteAsync(
        IConfirmationService confirmationService,
        ILocalizationWriteService writeService,
        int installedGameId,
        int discoveredFileId,
        IReadOnlyDictionary<string, string> translations,
        string fileName,
        string confirmationTitle,
        string confirmationAction,
        string userCanceledMessage,
        string operationCanceledMessage,
        string failurePrefix,
        Action? onConfirmed,
        IProgress<LocalizationWriteProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmationService);
        ArgumentNullException.ThrowIfNull(writeService);
        ArgumentNullException.ThrowIfNull(translations);

        var confirmed = confirmationService.Confirm(
            confirmationTitle,
            $"'{fileName}' dosyasındaki {translations.Count} {confirmationAction}\n\n"
            + "Orijinal dosya değişecek ve işlem öncesi yedek alınacak. Devam etmek istiyor musunuz?");

        if (!confirmed)
        {
            return new LocalizationWriteOutcome(
                Confirmed: false,
                Success: false,
                Result: null,
                StatusMessage: userCanceledMessage,
                ErrorMessage: null,
                ProgressPercent: 0);
        }

        onConfirmed?.Invoke();

        try
        {
            var result = await writeService.WriteAsync(
                    installedGameId,
                    discoveredFileId,
                    translations,
                    cancellationToken,
                    progress)
                .ConfigureAwait(true);

            return result.Success
                ? new LocalizationWriteOutcome(
                    Confirmed: true,
                    Success: true,
                    Result: result,
                    StatusMessage: FormatSuccessMessage(result),
                    ErrorMessage: null,
                    ProgressPercent: 100)
                : new LocalizationWriteOutcome(
                    Confirmed: true,
                    Success: false,
                    Result: result,
                    StatusMessage: result.Status == LocalizationWriteStatus.Restored
                        ? "Orijinal dosya yedekten geri yüklendi."
                        : null,
                    ErrorMessage: result.Message,
                    ProgressPercent: 0);
        }
        catch (OperationCanceledException)
        {
            return new LocalizationWriteOutcome(
                Confirmed: true,
                Success: false,
                Result: null,
                StatusMessage: operationCanceledMessage,
                ErrorMessage: null,
                ProgressPercent: 0);
        }
        catch (Exception exception)
        {
            return new LocalizationWriteOutcome(
                Confirmed: true,
                Success: false,
                Result: null,
                StatusMessage: null,
                ErrorMessage: failurePrefix + exception.Message,
                ProgressPercent: 0);
        }
    }

    private static string FormatSuccessMessage(LocalizationWriteResult result)
        => result.BackupFilePath is null
            ? result.Message
            : $"{result.Message} Yedek: {result.BackupFilePath}";
}
