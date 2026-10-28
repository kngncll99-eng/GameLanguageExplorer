using System;

namespace GameLanguageExplorer.WPF.Services;

/// <summary>
/// Bir oyun için vurgu paleti. <b>Yalnızca görsel vurgu</b> sağlar; oyun verisini, tarama
/// davranışını veya çeviri mantığını etkilemez.
/// </summary>
/// <param name="Key">Palet anahtarı (test/telemetri için sabit).</param>
/// <param name="DisplayName">UI'da gösterilen kısa ad.</param>
/// <param name="AccentHex">Vurgu rengi (kenarlık, işaret, seçim tonu).</param>
/// <param name="IsEstimated">Palet yalnızca oyun adına bakılarak <b>tahmin</b> edildi mi?</param>
/// <param name="Rationale">Tahminin gerekçesi (UI'da "tahmini" etiketiyle birlikte gösterilir).</param>
public sealed record GamePalette(
    string Key,
    string DisplayName,
    string AccentHex,
    bool IsEstimated,
    string Rationale);

/// <summary>
/// Oyun adından vurgu paleti seçer. Oyun kimliği yalnızca adla tahmin edildiği için sonuç
/// <b>kesin bilgi olarak sunulmaz</b> (<see cref="GamePalette.IsEstimated"/>).
/// </summary>
public static class GamePaletteCatalog
{
    public const string NeutralAccentHex = "#4C8DFF";

    public static GamePalette Neutral { get; } = new(
        "Neutral",
        "Nötr",
        NeutralAccentHex,
        true,
        "Oyun kimliği tanınmadı; nötr ve profesyonel vurgu kullanılıyor.");

    public static GamePalette SciFi { get; } = new(
        "SciFi",
        "Bilimkurgu — turkuaz",
        "#22D3EE",
        true,
        "Oyun adı bilimkurgu temasına işaret ediyor; turkuaz vurgu tahmini olarak seçildi.");

    public static GamePalette Medieval { get; } = new(
        "Medieval",
        "Ortaçağ — altın",
        "#D9A441",
        true,
        "Oyun adı ortaçağ/kılıç temasına işaret ediyor; altın vurgu tahmini olarak seçildi.");

    public static GamePalette DarkFantasy { get; } = new(
        "DarkFantasy",
        "Karanlık fantezi — bordo",
        "#C2557A",
        true,
        "Oyun adı karanlık fantezi temasına işaret ediyor; bordo vurgu tahmini olarak seçildi.");

    public static GamePalette Resolve(string? gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return Neutral;
        }

        var name = gameName.Trim().ToLowerInvariant();

        if (Contains(name, "neverness") || Contains(name, "neverness to everness"))
        {
            return SciFi;
        }

        if (Contains(name, "bannerlord") || Contains(name, "mount & blade") || Contains(name, "mount and blade"))
        {
            return Medieval;
        }

        if (Contains(name, "lust and magic") || Contains(name, "dark fantasy") || Contains(name, "grim"))
        {
            return DarkFantasy;
        }

        return Neutral;
    }

    private static bool Contains(string value, string token)
        => value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
