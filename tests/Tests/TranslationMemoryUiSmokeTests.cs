namespace GameLanguageExplorer.Tests;

using System.Threading;
using System.Windows;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.Services;
using GameLanguageExplorer.WPF.ViewModels;
using GameLanguageExplorer.WPF.Views;

/// <summary>Çeviri belleği ekranının XAML kaynaklarıyla birlikte yüklenebildiğini doğrular.</summary>
[Collection("WpfUi")]
public class TranslationMemoryUiSmokeTests
{
    [Fact]
    public void TranslationMemoryView_LoadsEntriesAndRenders()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new GameLanguageExplorer.WPF.App();
                    app.InitializeComponent();
                }

                using var database = new TempDatabase();
                using (var context = database.CreateContext())
                {
                    context.TranslationMemoryEntries.AddRange(
                        new TranslationMemoryEntry
                        {
                            SourceText = "Play",
                            TargetText = "Oyna",
                            SourceLanguage = "English",
                            TargetLanguage = "Turkish",
                            UsageCount = 3,
                            LastUsedAt = DateTime.UtcNow
                        },
                        new TranslationMemoryEntry
                        {
                            SourceText = "Settings",
                            TargetText = "Ayarlar",
                            SourceLanguage = "English",
                            TargetLanguage = "Turkish",
                            UsageCount = 1,
                            LastUsedAt = DateTime.UtcNow.AddMinutes(-5)
                        });
                    context.SaveChanges();
                }

                var viewModel = new TranslationMemoryViewModel(
                    new TranslationMemoryService(database.CreateFactory()),
                    new NoConfirmationService());

                viewModel.RefreshCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Assert.Equal(2, viewModel.EntryCount);

                var view = new TranslationMemoryView
                {
                    DataContext = viewModel,
                    Width = 1200,
                    Height = 820
                };

                view.Measure(new Size(1200, 820));
                view.Arrange(new Rect(0, 0, 1200, 820));
                view.UpdateLayout();

                Assert.NotNull(view.Content);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(120));

        Assert.Null(failure);
    }

    private sealed class NoConfirmationService : IConfirmationService
    {
        public bool Confirm(string title, string message) => false;
    }
}
