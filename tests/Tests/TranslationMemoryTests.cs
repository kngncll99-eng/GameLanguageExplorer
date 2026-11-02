namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services.Translation;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.Services;
using GameLanguageExplorer.WPF.ViewModels;
using Microsoft.EntityFrameworkCore;

/// <summary>Çeviri belleği listeleme/silme servisi ve ekran ViewModel'i.</summary>
public class TranslationMemoryTests
{
    [Fact]
    public async Task GetEntriesAsync_FiltersSourceText_AndOrdersByLastUsed()
    {
        using var database = new TempDatabase();
        Seed(database);

        var service = new TranslationMemoryService(database.CreateFactory());

        var filtered = await service.GetEntriesAsync("Play");
        Assert.Single(filtered);
        Assert.Equal("Play", filtered[0].SourceText);

        var all = await service.GetEntriesAsync();
        Assert.Equal(3, all.Count);
        Assert.Equal("Newest", all[0].SourceText);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTargetRecord()
    {
        using var database = new TempDatabase();
        Seed(database);

        var service = new TranslationMemoryService(database.CreateFactory());
        var entries = await service.GetEntriesAsync();
        var target = entries.Single(entry => entry.SourceText == "Play");

        Assert.True(await service.DeleteAsync(target.Id));
        Assert.False(await service.DeleteAsync(target.Id));

        var remaining = await service.GetEntriesAsync();
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, entry => entry.Id == target.Id);
    }

    [Fact]
    public async Task DeleteAllAsync_RemovesEveryRecord()
    {
        using var database = new TempDatabase();
        Seed(database);

        var service = new TranslationMemoryService(database.CreateFactory());

        Assert.Equal(3, await service.DeleteAllAsync());
        Assert.Empty(await service.GetEntriesAsync());
    }

    [Fact]
    public async Task SameSource_DifferentContexts_AreStoredAndReturnedSeparately()
    {
        using var database = new TempDatabase();
        var service = new TranslationMemoryService(database.CreateFactory());

        var menu = new TranslationMemoryContext("game:1", "menu");
        var toolbar = new TranslationMemoryContext("game:1", "toolbar");

        await service.SaveAsync("Play", "Oyna", "English", "Turkish", menu);
        await service.SaveAsync("Play", "Başlat", "English", "Turkish", toolbar);

        Assert.Equal("Oyna", await service.FindExactAsync("Play", "English", "Turkish", menu));
        Assert.Equal("Başlat", await service.FindExactAsync("Play", "English", "Turkish", toolbar));

        var entries = await service.GetEntriesAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(2, entries.Select(entry => entry.Context).Distinct().Count());
    }

    [Fact]
    public async Task LegacyRecord_WithUnknownContext_IsNotReusedForGameScope()
    {
        using var database = new TempDatabase();

        using (var context = database.CreateContext())
        {
            context.TranslationMemoryEntries.Add(new TranslationMemoryEntry
            {
                SourceText = "Play",
                TargetText = "Eski çeviri",
                SourceLanguage = "English",
                TargetLanguage = "Turkish",
                ScopeKey = "legacy:1",
                Context = string.Empty,
                PluralIndex = -1,
                PluralCount = -1
            });
            await context.SaveChangesAsync();
        }

        var service = new TranslationMemoryService(database.CreateFactory());
        var found = await service.FindExactAsync(
            "Play",
            "English",
            "Turkish",
            new TranslationMemoryContext("game:1", string.Empty));

        Assert.Null(found);
    }

    [Fact]
    public async Task ConcurrentSaves_DoNotCreateDuplicateIdentityRows()
    {
        using var database = new TempDatabase();
        var service = new TranslationMemoryService(database.CreateFactory());
        var context = new TranslationMemoryContext("game:7", "menu");

        await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(index => service.SaveAsync(
                "Play",
                "Çeviri " + index,
                "English",
                "Turkish",
                context)));

        using var readContext = database.CreateContext();
        var entry = Assert.Single(readContext.TranslationMemoryEntries);
        Assert.Equal("game:7", entry.ScopeKey);
        Assert.Equal("menu", entry.Context);
        Assert.StartsWith("Çeviri ", entry.TargetText);
    }

    [Fact]
    public async Task LegacyMigration_PreservesRows_AndMarksScopeUnknown()
    {
        using var database = new TempDatabase(ensureCreated: false);

        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync("20261101191417_AddTranslationMemoryLanguages");
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "TranslationMemoryEntries"
                    ("SourceText", "TargetText", "SourceLanguage", "TargetLanguage",
                     "UsageCount", "LastUsedAt", "CreatedAt")
                VALUES
                    ('Play', 'Oyna', 'English', 'Turkish', 1, NULL, CURRENT_TIMESTAMP),
                    ('Play', 'Başla', 'English', 'Turkish', 1, NULL, CURRENT_TIMESTAMP);
                """);

            await context.Database.MigrateAsync();

            var rows = await context.TranslationMemoryEntries
                .AsNoTracking()
                .OrderBy(entry => entry.Id)
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.StartsWith("legacy:", row.ScopeKey));
            Assert.All(rows, row => Assert.Equal(-1, row.PluralIndex));
        }

        var service = new TranslationMemoryService(database.CreateFactory());
        Assert.Null(await service.FindExactAsync(
            "Play",
            "English",
            "Turkish",
            new TranslationMemoryContext("game:1", string.Empty)));
    }

    [Fact]
    public async Task ViewModel_RefreshAndSearch_LoadFilteredEntries()
    {
        using var database = new TempDatabase();
        Seed(database);

        var viewModel = CreateViewModel(database, confirmationResult: true);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(3, viewModel.EntryCount);
        Assert.True(viewModel.HasEntries);
        Assert.False(viewModel.IsEmpty);
        Assert.True(viewModel.DeleteAllCommand.CanExecute(null));

        // Arama metni değişince debounce başlar; hemen yenilemek onu iptal eder ve
        // güncel filtreyle tek yükleme yapar.
        viewModel.SearchText = "Play";
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.EntryCount);
        Assert.Equal("Play", viewModel.Entries[0].SourceText);
        Assert.Equal("Oyna", viewModel.Entries[0].TargetText);
    }

    [Fact]
    public async Task ViewModel_DeleteSelected_AsksConfirmationAndReloads()
    {
        using var database = new TempDatabase();
        Seed(database);

        var confirmation = new RecordingConfirmationService(true);
        var viewModel = CreateViewModel(database, confirmation);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SelectedEntry = viewModel.Entries.Single(entry => entry.SourceText == "Play");
        Assert.True(viewModel.DeleteSelectedCommand.CanExecute(null));

        await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.True(confirmation.WasCalled);
        Assert.Contains("silinsin mi", confirmation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, viewModel.EntryCount);
        Assert.Null(viewModel.SelectedEntry);
        Assert.Contains("silindi", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ViewModel_DeleteAll_Declined_KeepsRecords()
    {
        using var database = new TempDatabase();
        Seed(database);

        var confirmation = new RecordingConfirmationService(false);
        var viewModel = CreateViewModel(database, confirmation);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.DeleteAllCommand.ExecuteAsync(null);

        Assert.True(confirmation.WasCalled);
        Assert.Equal(3, viewModel.EntryCount);

        using var context = database.CreateContext();
        Assert.Equal(3, context.TranslationMemoryEntries.Count());
    }

    private static TranslationMemoryViewModel CreateViewModel(
        TempDatabase database,
        bool confirmationResult)
        => CreateViewModel(database, new RecordingConfirmationService(confirmationResult));

    private static TranslationMemoryViewModel CreateViewModel(
        TempDatabase database,
        IConfirmationService confirmation)
        => new(new TranslationMemoryService(database.CreateFactory()), confirmation);

    private static void Seed(TempDatabase database)
    {
        using var context = database.CreateContext();
        var now = DateTime.UtcNow;

        context.TranslationMemoryEntries.AddRange(
            new TranslationMemoryEntry
            {
                SourceText = "Play",
                TargetText = "Oyna",
                SourceLanguage = "English",
                TargetLanguage = "Turkish",
                UsageCount = 2,
                LastUsedAt = now.AddMinutes(-10),
                CreatedAt = now.AddHours(-2)
            },
            new TranslationMemoryEntry
            {
                SourceText = "Settings",
                TargetText = "Ayarlar",
                SourceLanguage = "English",
                TargetLanguage = "Turkish",
                UsageCount = 1,
                LastUsedAt = now.AddMinutes(-5),
                CreatedAt = now.AddHours(-2)
            },
            new TranslationMemoryEntry
            {
                SourceText = "Newest",
                TargetText = "En yeni",
                SourceLanguage = "English",
                TargetLanguage = "Turkish",
                UsageCount = 1,
                LastUsedAt = now,
                CreatedAt = now.AddHours(-1)
            });

        context.SaveChanges();
    }

    private sealed class RecordingConfirmationService : IConfirmationService
    {
        private readonly bool _result;

        public RecordingConfirmationService(bool result) => _result = result;

        public bool WasCalled { get; private set; }
        public string Message { get; private set; } = string.Empty;

        public bool Confirm(string title, string message)
        {
            WasCalled = true;
            Message = message;
            return _result;
        }
    }
}

