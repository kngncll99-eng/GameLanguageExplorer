namespace GameLanguageExplorer.Core.Entities;

public class TranslationMemoryEntry
{
    public int Id { get; set; }
    public string SourceText { get; set; } = string.Empty;
    public string TargetText { get; set; } = string.Empty;

    /// <summary>Kaynak metnin dili (ör. English).</summary>
    public string SourceLanguage { get; set; } = string.Empty;

    /// <summary>Hedef çeviri dili (ör. Turkish).</summary>
    public string TargetLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Oyun/proje kapsamı (ör. <c>game:42</c>). Eski kayıtlarda boş kalır ve
    /// otomatik yeniden kullanım için eşleşmez.
    /// </summary>
    public string ScopeKey { get; set; } = string.Empty;

    /// <summary>Kayıt bağlamı (PO msgctxt, CSV sütunu, yapısal yol). Boş = bağlamsız.</summary>
    public string Context { get; set; } = string.Empty;

    /// <summary>PO çoğul biçim indeksi; tekil kayıtlarda -1.</summary>
    public int PluralIndex { get; set; } = -1;

    /// <summary>Çoğul biçim sayısı; tekil kayıtlarda -1.</summary>
    public int PluralCount { get; set; } = -1;

    public int UsageCount { get; set; } = 1;
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}