namespace GameLanguageExplorer.Core.Services.Localization;

using System.Security.Cryptography;

/// <summary>Dosya SHA-256 değerini hesaplar; testlerde hata taklidi için değiştirilebilir.</summary>
public interface ILocalizationFileHashProvider
{
    Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>Varsayılan SHA-256 sağlayıcısı.</summary>
public sealed class Sha256LocalizationFileHashProvider : ILocalizationFileHashProvider
{
    public Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 64 * 1024,
                    FileOptions.SequentialScan);

                return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            },
            cancellationToken);
}

/// <summary>Geçici dosyayı hedefe taşır; testlerde kısmi hata taklidi için değiştirilebilir.</summary>
public interface ILocalizationFileReplacer
{
    void Replace(string temporaryFilePath, string destinationFilePath);
}

/// <summary>
/// Aynı klasördeki geçici dosyayı hedefe atomik olarak taşır. Windows'ta <see cref="File.Replace(string,string,string?)"/>
/// kullanılır; platform desteklemiyorsa aynı birimdeki <see cref="File.Move(string,string,bool)"/> yedeğine düşer.
/// </summary>
public sealed class AtomicLocalizationFileReplacer : ILocalizationFileReplacer
{
    public void Replace(string temporaryFilePath, string destinationFilePath)
    {
        try
        {
            File.Replace(temporaryFilePath, destinationFilePath, destinationBackupFileName: null,
                ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(temporaryFilePath, destinationFilePath, overwrite: true);
        }
    }
}
