namespace GameLanguageExplorer.Data.Services;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services.Translation;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// <see cref="TranslationMemoryEntry"/> tablosunu kullanan tam eşleşmeli çeviri belleği.
/// </summary>
public sealed class TranslationMemoryService : ITranslationMemory
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public TranslationMemoryService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<string?> FindExactAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceText)
            || string.IsNullOrWhiteSpace(sourceLanguage)
            || string.IsNullOrWhiteSpace(targetLanguage))
        {
            return null;
        }

        var source = sourceLanguage.Trim();
        var target = targetLanguage.Trim();

        await using var db = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var entry = await db.TranslationMemoryEntries
            .FirstOrDefaultAsync(
                item => item.SourceText == sourceText
                        && item.SourceLanguage == source
                        && item.TargetLanguage == target,
                cancellationToken)
            .ConfigureAwait(false);

        if (entry is null || string.IsNullOrWhiteSpace(entry.TargetText))
        {
            return null;
        }

        entry.UsageCount = Math.Max(1, entry.UsageCount + 1);
        entry.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entry.TargetText;
    }

    public async Task SaveAsync(
        string sourceText,
        string targetText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceText)
            || string.IsNullOrWhiteSpace(targetText)
            || string.IsNullOrWhiteSpace(sourceLanguage)
            || string.IsNullOrWhiteSpace(targetLanguage))
        {
            return;
        }

        var source = sourceLanguage.Trim();
        var target = targetLanguage.Trim();

        await using var db = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var entry = await db.TranslationMemoryEntries
            .FirstOrDefaultAsync(
                item => item.SourceText == sourceText
                        && item.SourceLanguage == source
                        && item.TargetLanguage == target,
                cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            db.TranslationMemoryEntries.Add(new TranslationMemoryEntry
            {
                SourceText = sourceText,
                TargetText = targetText,
                SourceLanguage = source,
                TargetLanguage = target,
                UsageCount = 1,
                LastUsedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            entry.TargetText = targetText;
            entry.UsageCount = Math.Max(1, entry.UsageCount);
            entry.LastUsedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
