namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.WPF.ViewModels;

/// <summary>Editör değişiklik takibinin başlangıç çevirisine göre çalıştığını doğrular.</summary>
public class EditorChangeTrackingTests
{
    [Fact]
    public void ExistingTranslation_StartsUnchanged_AndSourceEquivalentTargetIsAChange()
    {
        var row = CreateRow("Play", "Oyna");

        Assert.False(row.IsChanged);
        Assert.Equal("Oyna", row.OriginalTranslation);

        row.TranslationText = "Play";

        Assert.True(row.IsChanged);

        row.AcceptChanges();

        Assert.False(row.IsChanged);
        Assert.Equal("Play", row.OriginalTranslation);

        row.TranslationText = "Oyna";

        Assert.True(row.IsChanged);
    }

    [Fact]
    public void RevertingToInitialValue_ClearsChange()
    {
        var row = CreateRow("Play", "Oyna");

        row.TranslationText = "Başla";
        Assert.True(row.IsChanged);

        row.TranslationText = "Oyna";
        Assert.False(row.IsChanged);

        row.TranslationText = string.Empty;
        Assert.True(row.IsChanged);
    }

    private static EditorEntryRow CreateRow(string source, string existing) =>
        new(
            new LocalizationTextEntry
            {
                Key = "msgid",
                SourceText = source,
                Kind = LocalizationTextKind.PoMessage,
                Status = LocalizationEntryStatus.Translatable,
                Evidence = "test",
                FormatKey = "PO",
                ExistingTranslation = existing,
                Placeholders = PlaceholderScanner.Scan(source)
            },
            canEdit: true);
}
