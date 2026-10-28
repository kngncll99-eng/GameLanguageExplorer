namespace GameLanguageExplorer.Core.Services.Translation;

/// <summary>
/// Küçük, sürümlenmiş İngilizce → Türkçe oyun terminolojisi sözlüğü. Yalnızca istemde
/// geçen terimler gönderilir; çeviri zorlaması modelin yanıtında ayrıca kalite kontrolünden
/// geçirilir. Sözlük anlamsal doğruluğun garantisi değildir.
/// </summary>
public static class GameGlossaryCatalog
{
    public const string Version = "1.5.0";

    private static readonly GlossaryEntry[] Terms =
    {
        Term("Attack", "Saldır"),
        Term("Defend", "Savun"),
        Term("Inventory", "Envanter"),
        Term("Quest", "Görev"),
        Term("Health", "Sağlık"),
        Term("Stamina", "Dayanıklılık"),
        Term("Mana", "Mana"),
        Term("Settings", "Ayarlar"),
        Term("Continue", "Devam Et"),
        Term("Save Game", "Oyunu Kaydet"),
        Term("Load Game", "Oyunu Yükle"),
        Term("New Game", "Yeni Oyun"),
        Term("Main Menu", "Ana Menü"),
        Term("Pause", "Duraklat"),
        Term("Resume", "Devam Et"),
        Term("Exit", "Çıkış"),
        Term("Cancel", "İptal"),
        Term("Back", "Geri"),
        Term("Confirm", "Onayla"),
        Term("Close", "Kapat"),
        Term("Equip", "Kuşan"),
        Term("Unequip", "Çıkar"),
        Term("Craft", "Üret"),
        Term("Trade", "Ticaret"),
        Term("Experience", "Deneyim"),
        Term("Level Up", "Seviye Atla"),
        Term("Achievement", "Başarım"),
        Term("Multiplayer", "Çok Oyunculu"),
        Term("Single Player", "Tek Oyunculu"),
        Term("Current", "Mevcut"),
        Term("Flip Hair", "Saçı Çevir"),
        Term("Deployment", "Konuşlandırma"),
        Term("Reset Deployment", "Konuşlandırmayı Sıfırla"),
        Term("Eyebrow Type", "Kaş Türü"),
        Term("Eyebrow", "Kaş"),
        Term("Mounted", "At Üstünde"),
        Term("another message", "başka bir mesaj"),
        Term("Take Control of Character", "Karakterin Kontrolünü Al"),
        Term("In-Game Purchases", "Oyun İçi Satın Alımlar"),
        Term("ESRB rating", "ESRB derecelendirmesi"),
        Term("rating information", "derecelendirme bilgileri"),
        Term("No banner equipped", "Bayrak Kuşanılmamış"),
        Term("Keybind", "Tuş Ataması"),
        Term("Keybinds", "Tuş Atamaları"),
        Term("Outdated", "Güncel Değil"),
        Term("Mission", "Görev"),
        Term("Formation", "Düzen"),
        Term("Properties", "Özellikler"),
        Term("Default", "Varsayılan"),
        Term("Reset", "Sıfırla"),
        Term("Battle Morale", "Savaş Morali"),
        Term("Hero Troops", "Kahraman Birlikleri"),
        Term("Banner Bearers", "Bayrak Taşıyıcıları"),
        Term("Banner Effect", "Bayrak Etkisi"),
        Term("Field of View", "Görüş Alanı"),
        Term("Weight", "Ağırlık"),
        Term("Controller", "Kontrolcü"),
        Term("Are you sure want to reset all properties? Your changes will be lost.",
            "Tüm özellikleri sıfırlamak istediğinizden emin misiniz? Değişiklikleriniz kaybolacak."),
        Term("You need to have at least one more formation of this type to change this formation's type.",
            "Bu düzenin türünü değiştirmek için bu türden en az bir düzeniniz daha olmalıdır."),
        Term("From {WHISPER_TARGET}", "Kimden {WHISPER_TARGET}"),
        Term("{PERCENTAGE}% of troop type", "{PERCENTAGE}% birlik türü"),
        Term("'{KEY}' to cancel", "'{KEY}' tuşuyla iptal et"),
        Term("Fast Cursor Move Left", "Hızlı İmleç Sola Hareket"),
        Term("Fast Cursor Move Right", "Hızlı İmleç Sağa Hareket"),
        Term("Fast Cursor Move Up", "Hızlı İmleç Yukarı Hareket"),
        Term("Fast Cursor Move Down", "Hızlı İmleç Aşağı Hareket"),
        Term("Map Camera Rotate", "Harita Kamerasını Döndür"),
        Term("Select all heroes", "Tüm Kahramanları Seç"),
        Term("Toggle scoreboard", "Skor Tablosunu Aç/Kapat")
    };

    /// <summary>Tüm varsayılan sözlük kayıtları (kopya döndürür).</summary>
    public static IReadOnlyList<GlossaryEntry> All => Terms.ToArray();

    /// <summary>
    /// Verilen kaynak metinlerde geçen terimleri döndürür. Uzun ifadeler önce gelir;
    /// böylece "Load Game" varken "Game" gibi kısa bir terim gereksiz yere eklenmez.
    /// </summary>
    public static IReadOnlyList<GlossaryEntry> SelectFor(IEnumerable<string> sourceTexts)
    {
        ArgumentNullException.ThrowIfNull(sourceTexts);

        var texts = sourceTexts.Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
        if (texts.Length == 0)
        {
            return Array.Empty<GlossaryEntry>();
        }

        return Terms
            .Where(term => texts.Any(text => text.Contains(term.SourceTerm, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(term => term.SourceTerm.Length)
            .ThenBy(term => term.SourceTerm, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Yalnızca hedef dil Türkçe ve kaynak metin bir sözlük terimiyle birebir eşleşiyorsa
    /// (isteğe bağlı sondaki iki nokta hariç) güvenilir sözlük karşılığını döndürür.
    /// Bu yol LLM çağrısını atlar; sonuç yine <c>Candidate</c> olarak işaretlenir.
    /// </summary>
    public static bool TryGetExactTranslation(
        string? sourceText,
        string? targetLanguage,
        out string translation)
    {
        translation = string.Empty;

        if (string.IsNullOrWhiteSpace(sourceText)
            || string.IsNullOrWhiteSpace(targetLanguage)
            || !IsTurkish(targetLanguage))
        {
            return false;
        }

        var text = sourceText.Trim();
        var punctuation = string.Empty;

        if (text.EndsWith(':'))
        {
            punctuation = ":";
            text = text[..^1].TrimEnd();
        }

        var term = Terms.FirstOrDefault(entry =>
            string.Equals(entry.SourceTerm, text, StringComparison.OrdinalIgnoreCase));

        if (term is null)
        {
            return false;
        }

        translation = term.TargetTerm + punctuation;
        return true;
    }

    private static bool IsTurkish(string language) =>
        language.Trim().StartsWith("tr", StringComparison.OrdinalIgnoreCase)
        || language.Contains("Turkish", StringComparison.OrdinalIgnoreCase);

    private static GlossaryEntry Term(string source, string target) => new()
    {
        SourceTerm = source,
        TargetTerm = target,
        Note = "Oyun arayüzü terimi",
        CaseSensitive = false
    };
}
