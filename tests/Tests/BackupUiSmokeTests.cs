namespace GameLanguageExplorer.Tests;

using System.IO;
using System.Threading;
using System.Windows;
using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Interfaces;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Core.Services.Localization.Preview;
using GameLanguageExplorer.Core.Services.Localization.Preview.Adapters;
using GameLanguageExplorer.Data.Services;
using GameLanguageExplorer.WPF.Services;
using GameLanguageExplorer.WPF.ViewModels;
using GameLanguageExplorer.WPF.Views;

/// <summary>Yedek ekranının XAML kaynaklarıyla birlikte yüklenebildiğini doğrular.</summary>
[Collection("WpfUi")]
public class BackupUiSmokeTests
{
    [Fact]
    public void BackupView_LoadsRecordsAndRenders()
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
                using var tree = new TempTree();
                var backupDirectory = Path.Combine(tree.Root, "backups");
                Directory.CreateDirectory(backupDirectory);

                var original = tree.Write(@"Localization\menu.json", """{"x":"new"}""");
                var backup = Path.Combine(backupDirectory, "menu.bak");
                File.WriteAllText(backup, """{"x":"old"}""");

                using (var context = database.CreateContext())
                {
                    context.BackupRecords.Add(new BackupRecord
                    {
                        OriginalFilePath = original,
                        BackupFilePath = backup,
                        OriginalSha256 = new string('a', 64),
                        CreatedAt = DateTime.UtcNow
                    });
                    context.SaveChanges();
                }

                var options = new LocalizationWriteOptions { BackupDirectory = backupDirectory };
                var viewModel = new BackupViewModel(
                    new BackupManagementService(database.CreateFactory(), options),
                    new LocalizationWriteService(
                        database.CreateFactory(),
                        new LocalizationFormatAdapterRegistry(
                            Array.Empty<ILocalizationFormatAdapter>()),
                        options),
                    new NoConfirmationService());

                viewModel.RefreshCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Assert.Single(viewModel.Entries);

                var view = new BackupView
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
