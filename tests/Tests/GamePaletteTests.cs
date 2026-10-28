namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.WPF.Services;

/// <summary>
/// Oyun paleti yalnızca <b>görsel vurgu</b> üretir. Bu testler paletin ad bazlı tahmin
/// kurallarını ve bilinmeyen oyunların nötr davranışını doğrular.
/// </summary>
public class GamePaletteCatalogTests
{
    [Theory]
    [InlineData("Neverness to Everness")]
    [InlineData("NEVERNESS TO EVERNESS")]
    public void SciFiGame_UsesTurquoiseAccent(string name)
    {
        var palette = GamePaletteCatalog.Resolve(name);

        Assert.Equal("SciFi", palette.Key);
        Assert.Equal("#22D3EE", palette.AccentHex);
        Assert.True(palette.IsEstimated);
    }

    [Theory]
    [InlineData("Mount & Blade II: Bannerlord")]
    [InlineData("Mount and Blade Warband")]
    public void MedievalGame_UsesGoldAccent(string name)
    {
        var palette = GamePaletteCatalog.Resolve(name);

        Assert.Equal("Medieval", palette.Key);
        Assert.Equal("#D9A441", palette.AccentHex);
    }

    [Fact]
    public void DarkFantasyGame_UsesBordeauxAccent()
    {
        var palette = GamePaletteCatalog.Resolve("Heroes of Lust and Magic");

        Assert.Equal("DarkFantasy", palette.Key);
        Assert.Equal("#C2557A", palette.AccentHex);
    }

    [Theory]
    [InlineData("Some Unknown Indie Game")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UnknownGame_FallsBackToNeutralPalette(string? name)
    {
        var palette = GamePaletteCatalog.Resolve(name);

        Assert.Equal("Neutral", palette.Key);
        Assert.Equal(GamePaletteCatalog.NeutralAccentHex, palette.AccentHex);
        Assert.True(palette.IsEstimated);
        Assert.False(string.IsNullOrWhiteSpace(palette.DisplayName));
    }

    [Fact]
    public void EveryPalette_IsMarkedAsEstimated()
    {
        // Paletler yalnızca oyun adına bakılarak seçilir; kesin bilgi olarak sunulmamalı.
        foreach (var name in new[] { "Neverness to Everness", "Bannerlord", "Heroes of Lust and Magic", "X" })
        {
            Assert.True(GamePaletteCatalog.Resolve(name).IsEstimated);
        }

        Assert.True(GamePaletteCatalog.Neutral.IsEstimated);
    }
}
