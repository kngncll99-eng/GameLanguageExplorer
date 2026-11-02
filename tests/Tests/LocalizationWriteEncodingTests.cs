namespace GameLanguageExplorer.Tests;

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Yazma sırasında kaynak kodlama ve BOM'un korunduğunu doğrular.</summary>
public class LocalizationWriteEncodingTests
{
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public async Task WriteAsync_PreservesEncodingBomAndBackupBytes(string encodingName)
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        const string originalJson = """{"greeting":"Merhaba dünya"}""";
        var originalBytes = Encode(originalJson, encodingName);
        var path = Path.Combine(tree.Root, "Localization", "menu.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, originalBytes);

        var originalSha = ComputeSha256(originalBytes);
        var (installId, fileId) = await PreviewTestHelper.CreateInstallAndFileAsync(
            database, tree.Root, path, "JSON");
        await SetFileHashAsync(database, fileId, path, originalSha, originalBytes.Length);

        var service = new LocalizationWriteService(
            database.CreateFactory(),
            PreviewTestHelper.CreateRegistry(),
            new LocalizationWriteOptions { BackupDirectory = Path.Combine(tree.Root, "backups") },
            logger: NullLogger<LocalizationWriteService>.Instance);

        var result = await service.WriteAsync(
            installId,
            fileId,
            new Dictionary<string, string> { ["/greeting"] = "Selam dünya" });

        Assert.True(result.Success, result.Message);

        var newBytes = await File.ReadAllBytesAsync(path);
        var expectedPreamble = GetPreamble(encodingName);
        Assert.True(newBytes.AsSpan().StartsWith(expectedPreamble));
        Assert.Equal(expectedPreamble.Length, GetPreamble(newBytes).Length);

        var decoded = Decode(newBytes, encodingName);
        using var document = JsonDocument.Parse(decoded);
        Assert.Equal("Selam dünya", document.RootElement.GetProperty("greeting").GetString());
        Assert.Equal(ComputeSha256(newBytes), result.NewSha256);

        using var context = database.CreateContext();
        var savedFile = context.DiscoveredFiles.Single(file => file.Id == fileId);
        Assert.Equal(newBytes.Length, savedFile.FileSize);
        Assert.Equal(result.NewSha256, savedFile.Sha256);

        var backup = context.BackupRecords.Single();
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(backup.BackupFilePath));
        Assert.Equal(originalSha, backup.OriginalSha256);
    }

    private static byte[] Encode(string text, string encodingName)
    {
        var body = encodingName switch
        {
            "utf8bom" or "utf8" => new UTF8Encoding(false).GetBytes(text),
            "utf16le" => Encoding.Unicode.GetBytes(text),
            "utf16be" => Encoding.BigEndianUnicode.GetBytes(text),
            "utf32le" => new UTF32Encoding(bigEndian: false, byteOrderMark: false).GetBytes(text),
            "utf32be" => new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetBytes(text),
            _ => throw new ArgumentOutOfRangeException(nameof(encodingName))
        };

        var preamble = GetPreamble(encodingName);
        return preamble.Concat(body).ToArray();
    }

    private static string Decode(byte[] bytes, string encodingName)
    {
        var preambleLength = GetPreamble(encodingName).Length;
        var body = bytes.AsSpan(preambleLength).ToArray();

        return encodingName switch
        {
            "utf8bom" or "utf8" => new UTF8Encoding(false).GetString(body),
            "utf16le" => Encoding.Unicode.GetString(body),
            "utf16be" => Encoding.BigEndianUnicode.GetString(body),
            "utf32le" => new UTF32Encoding(bigEndian: false, byteOrderMark: false).GetString(body),
            "utf32be" => new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetString(body),
            _ => throw new ArgumentOutOfRangeException(nameof(encodingName))
        };
    }

    private static byte[] GetPreamble(string encodingName) => encodingName switch
    {
        "utf8" => Array.Empty<byte>(),
        "utf8bom" => new byte[] { 0xEF, 0xBB, 0xBF },
        "utf16le" => new byte[] { 0xFF, 0xFE },
        "utf16be" => new byte[] { 0xFE, 0xFF },
        "utf32le" => new byte[] { 0xFF, 0xFE, 0x00, 0x00 },
        "utf32be" => new byte[] { 0x00, 0x00, 0xFE, 0xFF },
        _ => throw new ArgumentOutOfRangeException(nameof(encodingName))
    };

    private static byte[] GetPreamble(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return bytes[..4];
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return bytes[..4];
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return bytes[..3];
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return bytes[..2];
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return bytes[..2];
        }

        return Array.Empty<byte>();
    }

    private static async Task SetFileHashAsync(
        TempDatabase database,
        int fileId,
        string path,
        string sha256,
        long size)
    {
        using var context = database.CreateContext();
        var record = context.DiscoveredFiles.Single(file => file.Id == fileId);
        record.Sha256 = sha256;
        record.FileSize = size;
        record.IsWritable = true;
        await context.SaveChangesAsync();
        _ = path;
    }

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
