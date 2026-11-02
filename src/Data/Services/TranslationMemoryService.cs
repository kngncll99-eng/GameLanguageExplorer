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
        TranslationMemoryContext context,
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
        var (scope, contextText, pluralIndex, pluralCount) = Normalize(context);

        await using var db = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var entry = await db.TranslationMemoryEntries
            .FirstOrDefaultAsync(
                item => item.SourceText == sourceText
                        && item.SourceLanguage == source
                        && item.TargetLanguage == target
                        && item.ScopeKey == scope
                        && item.Context == contextText
                        && item.PluralIndex == pluralIndex
                        && item.PluralCount == pluralCount,
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
        TranslationMemoryContext context,
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
        var (scope, contextText, pluralIndex, pluralCount) = Normalize(context);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var db = await _dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            var entry = await db.TranslationMemoryEntries
                .FirstOrDefaultAsync(
                    item => item.SourceText == sourceText
                            && item.SourceLanguage == source
                            && item.TargetLanguage == target
                            && item.ScopeKey == scope
                            && item.Context == contextText
                            && item.PluralIndex == pluralIndex
                            && item.PluralCount == pluralCount,
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
                    ScopeKey = scope,
                    Context = contextText,
                    PluralIndex = pluralIndex,
                    PluralCount = pluralCount,
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

            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // Benzersiz indeks yarışı: yeni context ile mevcut kaydı güncelle.
            }
        }
    }

    private static (string ScopeKey, string Context, int PluralIndex, int PluralCount)
        Normalize(TranslationMemoryContext context) =>
        (
            context.ScopeKey?.Trim() ?? string.Empty,
            context.Context?.Trim() ?? string.Empty,
            context.PluralIndex ?? -1,
            context.PluralCount ?? -1
        );

    public async Task<IReadOnlyList<TranslationMemoryEntry>> GetEntriesAsync(
        string? sourceTextFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        IQueryable<TranslationMemoryEntry> query = db.TranslationMemoryEntries
            .AsNoTracking();

        var filter = sourceTextFilter?.Trim();
        if (!string.IsNullOrEmpty(filter))
        {
            query = query.Where(entry => entry.SourceText.Contains(filter));
        }

        return await query
            .OrderByDescending(entry => entry.LastUsedAt ?? entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return false;
        }

        await using var db = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var entry = await db.TranslationMemoryEntries
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return false;
        }

        db.TranslationMemoryEntries.Remove(entry);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await db.TranslationMemoryEntries
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
