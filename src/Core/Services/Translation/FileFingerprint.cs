namespace GameLanguageExplorer.Core.Services.Translation;

using System.Security.Cryptography;

/// <summary>
/// Kaynak dosyanın salt okunur SHA-256 parmak izini üretir. Dosyayı değiştirmez,
/// kilitlemez ve içeriğini başka bir yere kopyalamaz.
/// </summary>
public static class FileFingerprint
{
    /// <summary>Dosya yolunu doğrular ve SHA-256 değerini büyük harfli onaltılık olarak döndürür.</summary>
    public static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Kaynak dosya bulunamadı.", fullPath);
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    /// <summary>SHA-256 metnini 64 onaltılık karakter olarak doğrular ve büyük harfe çevirir.</summary>
    public static bool TryNormalizeSha256(string? value, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();

        if (candidate.Length != 64 || !candidate.All(Uri.IsHexDigit))
        {
            return false;
        }

        normalized = candidate.ToUpperInvariant();
        return true;
    }
}
