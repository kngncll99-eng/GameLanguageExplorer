namespace GameLanguageExplorer.Core.Services.Translation;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Çalışma günlüğündeki tek bir kayıt. Kaynak metnin kendisi saklanmaz.</summary>
public sealed class TranslationJournalUnit
{
    public string UnitId { get; set; } = string.Empty;

    public string EntryKey { get; set; } = string.Empty;

    /// <summary>Kaynak metnin SHA-256 değeri; kaynak değişirse eski sonuç eşleşmez.</summary>
    public string SourceSha256 { get; set; } = string.Empty;

    public string Status { get; set; } = nameof(TranslationUnitStatus.Failed);

    public string? Translation { get; set; }

    public string? DecisionReason { get; set; }

    public int Attempts { get; set; }

    public List<string> QualityCodes { get; set; } = new();
}

/// <summary>
/// Sürümlenmiş, JSON tabanlı çalışma günlüğü. API anahtarı, tam istem veya kaynak metin
/// içermez. Her parti sonunda atomik olarak yazılır.
/// </summary>
public sealed class TranslationRunJournal
{
    public int SchemaVersion { get; set; } = TranslationRunJournalStore.CurrentSchemaVersion;

    public string RunId { get; set; } = string.Empty;

    public int InstalledGameId { get; set; }

    public int DiscoveredFileId { get; set; }

    public string SourceFilePath { get; set; } = string.Empty;

    public string SourceFileSha256 { get; set; } = string.Empty;

    public string? FileFormat { get; set; }

    public string SourceLanguage { get; set; } = string.Empty;

    public string TargetLanguage { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string PromptVersion { get; set; } = string.Empty;

    public string GlossaryVersion { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Running, Completed, CompletedWithReview, Cancelled veya Failed.</summary>
    public string Status { get; set; } = "Running";

    public List<TranslationJournalUnit> Units { get; set; } = new();
}

/// <summary>Bozuk veya şema dışı günlüğü sessizce sıfırlamak yerine bildirir.</summary>
public sealed class TranslationRunJournalCorruptException : Exception
{
    public TranslationRunJournalCorruptException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Günlük dosyalarını <c>%LOCALAPPDATA%\GameLanguageExplorer\translation-runs</c> altında
/// yönetir. Yazma geçici dosya + atomik taşıma ile yapılır; bozuk dosya sessizce silinmez.
/// </summary>
public sealed class TranslationRunJournalStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public TranslationRunJournalStore(string? directoryPath = null)
    {
        DirectoryPath = string.IsNullOrWhiteSpace(directoryPath)
            ? DefaultDirectoryPath
            : Path.GetFullPath(directoryPath);
    }

    public string DirectoryPath { get; }

    public static string DefaultDirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameLanguageExplorer",
        "translation-runs");

    public string GetJournalPath(string runId)
    {
        if (!IsSafeRunId(runId))
        {
            throw new ArgumentException("Çalışma kimliği güvenli değil.", nameof(runId));
        }

        return Path.Combine(DirectoryPath, runId + ".json");
    }

    public async Task<TranslationRunJournal?> TryLoadAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        var path = GetJournalPath(runId);

        if (!File.Exists(path))
        {
            return null;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new TranslationRunJournalCorruptException(
                "Çalışma günlüğü okunamadı. Dosya bozuk veya erişilemiyor.", exception);
        }

        TranslationRunJournal? journal;
        try
        {
            journal = JsonSerializer.Deserialize<TranslationRunJournal>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new TranslationRunJournalCorruptException(
                "Çalışma günlüğü geçerli JSON değil; sessizce sıfırdan başlatılmadı.", exception);
        }

        Validate(journal, runId);
        return journal;
    }

    public async Task SaveAsync(
        TranslationRunJournal journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);

        if (!IsSafeRunId(journal.RunId))
        {
            throw new ArgumentException("Çalışma kimliği güvenli değil.", nameof(journal));
        }

        Directory.CreateDirectory(DirectoryPath);
        var path = GetJournalPath(journal.RunId);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(journal, JsonOptions);

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 8192,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(json), cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // Geçici dosya temizliği başarısız olsa da kalıcı günlük yazımı etkilenmez.
            }
        }
    }

    private static void Validate(TranslationRunJournal? journal, string expectedRunId)
    {
        if (journal is null)
        {
            throw new TranslationRunJournalCorruptException(
                "Çalışma günlüğü boş; sessizce sıfırdan başlatılmadı.");
        }

        if (journal.SchemaVersion != CurrentSchemaVersion)
        {
            throw new TranslationRunJournalCorruptException(
                $"Desteklenmeyen çalışma günlüğü şema sürümü: {journal.SchemaVersion}.");
        }

        if (!string.Equals(journal.RunId, expectedRunId, StringComparison.Ordinal))
        {
            throw new TranslationRunJournalCorruptException(
                "Çalışma günlüğü kimliği dosya adıyla eşleşmiyor.");
        }

        if (journal.Units is null
            || string.IsNullOrWhiteSpace(journal.SourceFileSha256)
            || journal.SourceFileSha256.Length != 64)
        {
            throw new TranslationRunJournalCorruptException(
                "Çalışma günlüğü zorunlu alanları eksik.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in journal.Units)
        {
            if (unit is null
                || string.IsNullOrWhiteSpace(unit.UnitId)
                || !ids.Add(unit.UnitId)
                || string.IsNullOrWhiteSpace(unit.EntryKey)
                || unit.SourceSha256.Length != 64
                || !Enum.TryParse<TranslationUnitStatus>(unit.Status, ignoreCase: false, out _))
            {
                throw new TranslationRunJournalCorruptException(
                    "Çalışma günlüğünde geçersiz veya yinelenen kayıt var.");
            }
        }
    }

    private static bool IsSafeRunId(string runId)
        => !string.IsNullOrWhiteSpace(runId)
           && runId.Length <= 80
           && runId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}

/// <summary>
/// Kaynak dosya, dil çifti, sağlayıcı, model ve istem sürümünden kararlı çalışma kimliği
/// üretir. Ayarlardan biri değişirse farklı günlük yolu oluşur ve eski sonuçlar yanlışlıkla
/// yeniden kullanılmaz.
/// </summary>
public static class TranslationRunIdentity
{
    public static string ComputeRunId(TranslationEngineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Provider);

        var glossary = string.Join(
            "\u001F",
            request.Glossary
                .Select(entry => string.Join(
                    "\u001E",
                    entry.SourceTerm,
                    entry.TargetTerm,
                    entry.Note ?? string.Empty,
                    entry.CaseSensitive ? "1" : "0"))
                .OrderBy(value => value, StringComparer.Ordinal));

        var material = string.Join(
            "\n",
            "GLE-TRANSLATION-RUN-1",
            request.InstalledGameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.DiscoveredFileId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.SourceFileSha256,
            request.FileFormat ?? string.Empty,
            request.SourceLanguage,
            request.TargetLanguage,
            request.Provider.ProviderId,
            request.Provider.Model,
            request.Provider.Endpoint.GetLeftPart(UriPartial.Path),
            TranslationPromptBuilder.PromptVersion,
            GameGlossaryCatalog.Version,
            glossary);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "run_" + Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }
}
