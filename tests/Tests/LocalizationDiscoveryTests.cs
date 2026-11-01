namespace GameLanguageExplorer.Tests;

using GameLanguageExplorer.Core.Entities;
using GameLanguageExplorer.Core.Services.Localization;
using GameLanguageExplorer.Data;
using GameLanguageExplorer.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

// ─────────────────────────────────────────────────────────────────────────────
// Test altyapısı
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Geçici dosya ağacı oluşturur; test sonunda siler.</summary>
public sealed class TempTree : IDisposable
{
    public string Root { get; }

    public TempTree()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gle_disc_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(Root);
    }

    /// <summary>Kök altına dosya yazar (klasörleri oluşturur) ve tam yolu döndürür.</summary>
    public string Write(string relativePath, string content = "key=value")
    {
        var full = System.IO.Path.Combine(Root, relativePath);
        var dir = System.IO.Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
        }

        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Delete(string relativePath) => System.IO.File.Delete(System.IO.Path.Combine(Root, relativePath));

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Root, recursive: true); } catch { /* en iyi çaba */ }
    }
}

/// <summary>Dil dosyası testleri için ortak yardımcılar.</summary>
public static class DiscoveryTestHelper
{
    public const string GameName = "Test Oyunu";

    public static LocalizationFileScanService CreateService(TempDatabase database)
        => new(database.CreateFactory(), NullLogger<LocalizationFileScanService>.Instance);

    /// <summary>Verilen klasörle ilişkili bir Game + InstalledGame kaydı oluşturur.</summary>
    public static async Task<int> CreateInstallAsync(TempDatabase database, string installPath)
    {
        using var context = database.CreateContext();

        var game = new Game { Name = GameName };
        context.Games.Add(game);
        await context.SaveChangesAsync();

        var install = new InstalledGame
        {
            GameId = game.Id,
            InstallPath = installPath,
            InstallSource = "Manual",
            InstallSourceId = string.Empty,
            IsInstalled = true
        };

        context.InstalledGames.Add(install);
        await context.SaveChangesAsync();

        return install.Id;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Format kataloğu
// ─────────────────────────────────────────────────────────────────────────────

public class LocalizationFormatCatalogTests
{
    [Theory]
    [InlineData(".json")]
    [InlineData(".xml")]
    [InlineData(".csv")]
    [InlineData(".tsv")]
    [InlineData(".po")]
    [InlineData(".pot")]
    [InlineData(".ini")]
    [InlineData(".yaml")]
    [InlineData(".yml")]
    [InlineData(".txt")]
    [InlineData(".strings")]
    [InlineData(".loc")]
    public void RequestedTextExtensions_AreClassifiedAsText(string extension)
    {
        var format = LocalizationFormatCatalog.FindByExtension(extension);

        Assert.NotNull(format);
        Assert.Equal(LocalizationFormatKind.Text, format!.Kind);
    }

    [Theory]
    [InlineData(".pak")]
    [InlineData(".bundle")]
    [InlineData(".archive")]
    [InlineData(".assets")]
    [InlineData(".uasset")]
    [InlineData(".bank")]
    public void PackageExtensions_AreNeverText(string extension)
    {
        var format = LocalizationFormatCatalog.FindByExtension(extension);

        Assert.NotNull(format);
        Assert.Equal(LocalizationFormatKind.Package, format!.Kind);
        Assert.Equal(LocalizationSupportStatus.UnsupportedPackage,
            LocalizationFormatCatalog.GetSupportStatus(format.Key, null));
    }

    [Fact]
    public void Mo_IsBinary_AndNotMarkedAsSupported()
    {
        var format = LocalizationFormatCatalog.FindByExtension(".mo");

        Assert.NotNull(format);
        Assert.Equal(LocalizationFormatKind.Binary, format!.Kind);
        Assert.Equal(LocalizationSupportStatus.UnsupportedBinary,
            LocalizationFormatCatalog.GetSupportStatus(format.Key, null));
    }

    [Fact]
    public void NoFormatIsReportedAsReadableWithoutAdapter()
    {
        foreach (var format in LocalizationFormatCatalog.All)
        {
            var text = LocalizationFormatCatalog.DescribeSupport(format.Key, adapterUsed: null);

            Assert.DoesNotContain("Okunabilir", text);
        }
    }

    [Fact]
    public void FindByExtension_WorksWithoutLeadingDot()
    {
        var format = LocalizationFormatCatalog.FindByExtension("json");

        Assert.NotNull(format);
        Assert.Equal("JSON", format!.Key);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Sınıflandırıcı (uzantı vs. bağlam ipuçları)
// ─────────────────────────────────────────────────────────────────────────────

public class LocalizationFileClassifierTests
{
    [Theory]
    [InlineData("game.exe")]
    [InlineData("data.dll")]
    [InlineData("logo.png")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    public void UnknownExtensions_AreNotCandidates(string fileName)
    {
        Assert.Null(LocalizationFileClassifier.Classify(fileName));
    }

    [Fact]
    public void ExtensionOnly_IsLowConfidence_AndNotPresentedAsSupport()
    {
        var result = LocalizationFileClassifier.Classify(@"C:\Game\data\settings.json");

        Assert.NotNull(result);
        Assert.Equal("JSON", result!.Format.Key);
        Assert.Equal(LocalizationFormatCatalog.ConfidenceLow, result.Confidence);
        Assert.False(result.HasContextHint);
        Assert.Contains("Yalnızca uzantı", result.Reason);
        Assert.Equal(LocalizationSupportStatus.CandidateNoAdapter, result.SupportStatus);
        Assert.Contains("ayrıştırıcı yok", LocalizationFormatCatalog.DescribeSupport(result.Format.Key, null));
    }

    [Fact]
    public void FileNameHint_RaisesConfidenceToMedium()
    {
        var result = LocalizationFileClassifier.Classify(@"C:\Game\data\ui_translation.json");

        Assert.NotNull(result);
        Assert.Equal(LocalizationFormatCatalog.ConfidenceMedium, result!.Confidence);
        Assert.True(result.HasContextHint);
        Assert.Contains("Dosya adı", result.Reason);
    }

    [Fact]
    public void FolderHint_FromRelativePath_IsDetected()
    {
        var result = LocalizationFileClassifier.Classify(@"C:\Game\Localization\Data\menu.json", @"Localization\Data");

        Assert.NotNull(result);
        Assert.Equal(LocalizationFormatCatalog.ConfidenceMedium, result!.Confidence);
        Assert.Contains("Klasör yolu", result.Reason);
    }

    [Fact]
    public void FileNameAndFolderHints_GiveHighConfidence()
    {
        var result = LocalizationFileClassifier.Classify(
            @"C:\Game\Localization\languages\menu_text.json",
            @"Localization\languages");

        Assert.NotNull(result);
        Assert.Equal(LocalizationFormatCatalog.ConfidenceHigh, result!.Confidence);
    }

    [Theory]
    [InlineData("Texture")]
    [InlineData("Textures")]
    [InlineData("Context")]
    public void MisleadingFolderNames_AreNotHints(string folder)
    {
        var result = LocalizationFileClassifier.Classify(
            $@"C:\Game\{folder}\config.json",
            folder);

        Assert.NotNull(result);
        Assert.False(result!.HasContextHint);
        Assert.Equal(LocalizationFormatCatalog.ConfidenceLow, result.Confidence);
    }

    [Fact]
    public void PackageFile_IsNeverClassifiedAsText_EvenWithHint()
    {
        var result = LocalizationFileClassifier.Classify(
            @"C:\Game\Content\Localization\localization_data.pak",
            @"Content\Localization");

        Assert.NotNull(result);
        Assert.True(result!.IsPackage);
        Assert.Equal(LocalizationFormatKind.Package, result.Format.Kind);
        Assert.Equal(LocalizationSupportStatus.UnsupportedPackage, result.SupportStatus);
        Assert.Contains("metin olarak ayrıştırılmaz", result.Reason);
    }

    [Fact]
    public void Evidence_AlwaysContainsExtension()
    {
        var result = LocalizationFileClassifier.Classify(@"C:\Game\language\menu_translation.csv", "language");

        Assert.NotNull(result);
        Assert.Contains(result!.Evidence, e => e.EvidenceType == LocalizationEvidenceTypes.Extension);
        Assert.Contains(result.Evidence, e => e.EvidenceType == LocalizationEvidenceTypes.FileName);
        Assert.Contains(result.Evidence, e => e.EvidenceType == LocalizationEvidenceTypes.FolderName);
    }

    [Fact]
    public void JsonContentSignal_RaisesLowConfidenceToMedium()
    {
        var content = JsonContentInspector.Inspect(
            """{"menu":{"play":"Oyna","settings":"Ayarlar","quit":"Çıkış"}}""");

        var result = LocalizationFileClassifier.Classify(
            @"C:\Game\data\ui.json",
            jsonContent: content);

        Assert.NotNull(result);
        Assert.Equal(LocalizationFormatCatalog.ConfidenceMedium, result!.Confidence);
        Assert.True(result.HasContentSignal);
        Assert.Contains(result.Evidence, e => e.EvidenceType == LocalizationEvidenceTypes.Content);
    }

    [Fact]
    public void JsonContentWithoutText_CapsHighConfidence()
    {
        var content = JsonContentInspector.Inspect("""{"width":1920,"height":1080}""");

        var result = LocalizationFileClassifier.Classify(
            @"C:\Game\Localization\languages\menu_text.json",
            @"Localization\languages",
            content);

        Assert.Equal(LocalizationFormatCatalog.ConfidenceLow, result!.Confidence);
        Assert.Contains("çevrilebilir metin", result.Reason);
    }

    [Fact]
    public void InvalidJsonContent_CapsHighConfidence()
    {
        var content = JsonContentInspector.Inspect("""{"menu":""");

        var result = LocalizationFileClassifier.Classify(
            @"C:\Game\Localization\languages\menu_text.json",
            @"Localization\languages",
            content);

        Assert.Equal(LocalizationFormatCatalog.ConfidenceLow, result!.Confidence);
        Assert.Contains("Geçersiz JSON", result.Reason);
    }

    [Fact]
    public void AmbiguousJsonContent_CapsHighAtMedium()
    {
        var content = JsonContentInspector.Inspect("""{"name":"My Game","version":"1.0.0"}""");

        var result = LocalizationFileClassifier.Classify(
            @"C:\Game\Localization\languages\menu_text.json",
            @"Localization\languages",
            content);

        Assert.Equal(LocalizationFormatCatalog.ConfidenceMedium, result!.Confidence);
        Assert.Contains("belirsiz", result.Reason);
    }

    [Fact]
    public void OversizedJsonContent_CapsHighAtMedium()
    {
        var content = new JsonContentAnalysis(
            JsonContentStatus.Oversized,
            Detail: "boyut sınırı nedeniyle incelenmedi");

        var result = LocalizationFileClassifier.Classify(
            @"C:\Game\Localization\languages\menu_text.json",
            @"Localization\languages",
            content);

        Assert.Equal(LocalizationFormatCatalog.ConfidenceMedium, result!.Confidence);
        Assert.Contains("boyut sınırı", result.Reason);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Yol güvenliği
// ─────────────────────────────────────────────────────────────────────────────

public class JsonContentInspectorTests
{
    [Fact]
    public void LocalizationLikeJson_IsDetected()
    {
        var result = JsonContentInspector.Inspect(
            """{"menu":{"play":"Oyna","settings":"Ayarlar","quit":"Çıkış"}}""");

        Assert.Equal(JsonContentStatus.LocalizationLike, result.Status);
        Assert.Equal(3, result.TextValueCount);
        Assert.True(result.HasLocalizationSignal);
    }

    [Fact]
    public void GameConfigJson_WithoutText_IsNotLocalizationLike()
    {
        var result = JsonContentInspector.Inspect(
            """{"width":1920,"height":1080,"fullscreen":true,"vsync":false}""");

        Assert.Equal(JsonContentStatus.NoText, result.Status);
        Assert.False(result.HasLocalizationSignal);
    }

    [Fact]
    public void MetadataStrings_AreAmbiguous()
    {
        var result = JsonContentInspector.Inspect(
            """{"name":"My Game","version":"1.0.0"}""");

        Assert.Equal(JsonContentStatus.Ambiguous, result.Status);
        Assert.False(result.HasLocalizationSignal);
    }

    [Fact]
    public void FlatTextObject_IsLocalizationLike()
    {
        var result = JsonContentInspector.Inspect(
            """{"hello":"Hello","world":"World"}""");

        Assert.Equal(JsonContentStatus.LocalizationLike, result.Status);
        Assert.True(result.HasLocalizationSignal);
    }

    [Fact]
    public void InvalidJson_IsReportedAsInvalid()
    {
        var result = JsonContentInspector.Inspect("""{"menu":""");

        Assert.Equal(JsonContentStatus.Invalid, result.Status);
        Assert.Contains("Geçersiz JSON", result.Detail);
    }

    [Fact]
    public void OversizedJson_IsNotParsed()
    {
        var result = JsonContentInspector.Inspect("""{"menu":"Oyna"}""", maxBytes: 4);

        Assert.Equal(JsonContentStatus.Oversized, result.Status);
    }

    [Fact]
    public void EmptyJson_IsReportedAsEmpty()
    {
        var result = JsonContentInspector.Inspect("   ");

        Assert.Equal(JsonContentStatus.Empty, result.Status);
    }
}

public class PathSafetyTests
{
    [Fact]
    public void IsUnderRoot_RejectsSiblingFolderWithSamePrefix()
    {
        Assert.False(PathSafety.IsUnderRoot(@"C:\Game2\file.json", @"C:\Game"));
        Assert.True(PathSafety.IsUnderRoot(@"C:\Game\file.json", @"C:\Game"));
        Assert.True(PathSafety.IsUnderRoot(@"C:\Game\Sub\file.json", @"C:\Game"));
    }

    [Fact]
    public void IsUnderRoot_RejectsParentTraversal()
    {
        Assert.False(PathSafety.IsUnderRoot(@"C:\Game\..\Other\file.json", @"C:\Game"));
    }

    [Fact]
    public void GetRelativeDirectory_ReturnsFolderPart()
    {
        var relative = PathSafety.GetRelativeDirectory(@"C:\Game\Localization\Text\a.json", @"C:\Game\");

        Assert.Equal(@"Localization\Text", relative);
    }

    [Fact]
    public void GetRelativeDirectory_ReturnsNullOutsideRoot()
    {
        Assert.Null(PathSafety.GetRelativeDirectory(@"C:\Other\a.json", @"C:\Game"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Tarama servisi (salt okunur, mükerrer kayıt yok)
// ─────────────────────────────────────────────────────────────────────────────

public class LocalizationFileScanServiceTests
{
    private sealed class CollectingProgress : IProgress<LocalizationScanProgress>
    {
        public List<LocalizationScanProgress> Items { get; } = new();
        public void Report(LocalizationScanProgress value) => Items.Add(value);
    }

    [Fact]
    public async Task ScanAsync_FindsCandidatesRecursively()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"root.json");
        tree.Write(@"Localization\menu.xml");
        tree.Write(@"Data\lang\strings.csv");
        tree.Write(@"bin\game.exe");

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var result = await DiscoveryTestHelper.CreateService(database).ScanAsync(installId);

        Assert.True(result.Success);
        Assert.Equal(3, result.CandidatesFound);
        Assert.Equal(3, result.Added);
        Assert.Equal(1, result.IgnoredFiles);
        Assert.False(result.WasPartial);

        var files = await DiscoveryTestHelper.CreateService(database).GetDiscoveredFilesAsync(installId);
        Assert.Equal(3, files.Count);
        Assert.Contains(files, f => f.FilePath.EndsWith(@"Localization\menu.xml", StringComparison.OrdinalIgnoreCase));
        Assert.All(files, f => Assert.True(f.IsReadable));
    }

    [Fact]
    public async Task ScanAsync_StoresReasonAndSupportStatus_NotJustExtension()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\ui_text.json",
            """{"menu":{"play":"Oyna","settings":"Ayarlar","quit":"Çıkış"}}""");
        tree.Write(@"Data\settings.json",
            """{"width":1920,"height":1080,"fullscreen":true}""");

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        await DiscoveryTestHelper.CreateService(database).ScanAsync(installId);

        var files = await DiscoveryTestHelper.CreateService(database).GetDiscoveredFilesAsync(installId);

        var hinted = files.Single(f => f.FileName == "ui_text.json");
        var plain = files.Single(f => f.FileName == "settings.json");

        Assert.Equal(LocalizationFormatCatalog.ConfidenceHigh, hinted.DetectionConfidence);
        Assert.Equal(LocalizationFormatCatalog.ConfidenceLow, plain.DetectionConfidence);
        Assert.Contains("ipucu", hinted.LastValidationResult);
        Assert.Contains("Yalnızca uzantı", plain.LastValidationResult);

        // Hiçbir kayıt "okunabilir" olarak sunulmaz.
        Assert.All(files, f => Assert.Null(f.AdapterUsed));
    }

    [Fact]
    public async Task ScanAsync_JsonContentSignal_AddsContentEvidence()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Data\ui.json",
            """{"menu":{"play":"Oyna","settings":"Ayarlar","quit":"Çıkış"}}""");

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var result = await DiscoveryTestHelper.CreateService(database).ScanAsync(installId);

        Assert.True(result.Success);

        var file = Assert.Single(
            await DiscoveryTestHelper.CreateService(database).GetDiscoveredFilesAsync(installId));

        Assert.Equal(LocalizationFormatCatalog.ConfidenceMedium, file.DetectionConfidence);

        using var context = database.CreateContext();
        Assert.Contains(context.FormatEvidences, e =>
            e.DiscoveredFileId == file.Id && e.EvidenceType == LocalizationEvidenceTypes.Content);
    }

    [Fact]
    public async Task ScanAsync_CorruptJson_StoresLowConfidenceWithoutThrowing()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var path = tree.Write(@"Localization\broken_text.json", """{"menu":""");

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var result = await DiscoveryTestHelper.CreateService(database).ScanAsync(installId);

        Assert.True(result.Success);

        var file = Assert.Single(
            await DiscoveryTestHelper.CreateService(database).GetDiscoveredFilesAsync(installId));

        Assert.Equal(LocalizationFormatCatalog.ConfidenceLow, file.DetectionConfidence);
        Assert.Contains("Geçersiz JSON", file.LastValidationResult);
        Assert.True(System.IO.File.Exists(path));
    }

    [Fact]
    public async Task ScanAsync_OversizedJson_IsNotParsedAndCappedAtMedium()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\huge_text.json", new string('x', 64));

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var result = await DiscoveryTestHelper.CreateService(database).ScanAsync(
            installId,
            progress: null,
            options: new LocalizationScanOptions { MaxJsonContentBytes = 32 });

        Assert.True(result.Success);

        var file = Assert.Single(
            await DiscoveryTestHelper.CreateService(database).GetDiscoveredFilesAsync(installId));

        Assert.Equal(LocalizationFormatCatalog.ConfidenceMedium, file.DetectionConfidence);
        Assert.Contains("sınır", file.LastValidationResult);
    }

    [Fact]
    public async Task ScanAsync_GenericJsonConfig_IsNotPromotedByPathHint()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\ui_text.json",
            """{"width":1920,"height":1080,"fullscreen":true}""");

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var result = await DiscoveryTestHelper.CreateService(database).ScanAsync(installId);

        Assert.True(result.Success);

        var file = Assert.Single(
            await DiscoveryTestHelper.CreateService(database).GetDiscoveredFilesAsync(installId));

        Assert.Equal(LocalizationFormatCatalog.ConfidenceLow, file.DetectionConfidence);
        Assert.Contains("çevrilebilir metin", file.LastValidationResult);
    }

    [Fact]
    public async Task ScanAsync_AmbiguousJson_IsCappedAtMedium()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\ui_text.json",
            """{"name":"My Game","version":"1.0.0"}""");

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var result = await DiscoveryTestHelper.CreateService(database).ScanAsync(installId);

        Assert.True(result.Success);

        var file = Assert.Single(
            await DiscoveryTestHelper.CreateService(database).GetDiscoveredFilesAsync(installId));

        Assert.Equal(LocalizationFormatCatalog.ConfidenceMedium, file.DetectionConfidence);
        Assert.Contains("belirsiz", file.LastValidationResult);
    }

    [Fact]
    public async Task ScanAsync_PackageWithoutHint_IsNotStored_ButCounted()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Content\level01.uasset");
        tree.Write(@"Content\audio.bank");
        tree.Write(@"Content\patch.pak");
        tree.Write(@"Localization\localization_data.pak");

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var result = await DiscoveryTestHelper.CreateService(database).ScanAsync(installId);

        // Aday listesine yalnızca ipucu eşleşen paket girer; diğer paketler sayılır ama kaydedilmez.
        Assert.Equal(1, result.CandidatesFound);
        Assert.Equal(3, result.SkippedPackages);

        var files = await DiscoveryTestHelper.CreateService(database).GetDiscoveredFilesAsync(installId);

        var stored = Assert.Single(files);
        Assert.Equal("PAK", stored.DetectedFormat);
        Assert.Contains("metin olarak ayrıştırılmaz", stored.LastValidationResult);
    }

    [Fact]
    public async Task ScanAsync_SecondScan_DoesNotDuplicate()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\a.json");
        tree.Write(@"Localization\b.xml");

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var service = DiscoveryTestHelper.CreateService(database);

        var first = await service.ScanAsync(installId);
        var second = await service.ScanAsync(installId);

        Assert.Equal(2, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(2, second.Unchanged);

        using var context = database.CreateContext();
        Assert.Equal(2, context.DiscoveredFiles.Count());
    }

    [Fact]
    public async Task ScanAsync_ComputesSha256_ForCandidates()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var path = tree.Write(@"Localization\a.json", "merhaba");
        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        await DiscoveryTestHelper.CreateService(database).ScanAsync(installId);

        using var context = database.CreateContext();
        var row = context.DiscoveredFiles.Single();

        Assert.False(string.IsNullOrWhiteSpace(row.Sha256));
        Assert.Equal(64, row.Sha256.Length);
        Assert.Equal(7, row.FileSize);
        Assert.True(System.IO.File.Exists(path));
    }

    [Fact]
    public async Task ScanAsync_RemovedFile_IsMarkedMissing_NotDeleted()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\a.json");
        tree.Write(@"Localization\b.json");

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var service = DiscoveryTestHelper.CreateService(database);

        await service.ScanAsync(installId);
        tree.Delete(@"Localization\b.json");

        var second = await service.ScanAsync(installId);

        Assert.Equal(1, second.MarkedMissing);

        using var context = database.CreateContext();
        Assert.Equal(2, context.DiscoveredFiles.Count()); // silinmedi

        var missing = context.DiscoveredFiles.Single(f => f.FileName == "b.json");
        Assert.False(missing.IsReadable);
        Assert.Equal(LocalizationFileScanService.MissingFileMessage, missing.LastValidationResult);
    }

    [Fact]
    public async Task ScanAsync_ReappearingFile_ClearsMissingFlag()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\a.json");
        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var service = DiscoveryTestHelper.CreateService(database);

        await service.ScanAsync(installId);
        tree.Delete(@"Localization\a.json");
        await service.ScanAsync(installId);

        tree.Write(@"Localization\a.json");
        await service.ScanAsync(installId);

        using var context = database.CreateContext();
        var row = context.DiscoveredFiles.Single();

        Assert.True(row.IsReadable);
        Assert.NotEqual(LocalizationFileScanService.MissingFileMessage, row.LastValidationResult);
        Assert.Equal(1, context.DiscoveredFiles.Count());
    }

    [Fact]
    public async Task ScanAsync_PartialScan_DoesNotMarkMissing()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\a.json");
        tree.Write(@"Deep\Inner\b.json");

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var service = DiscoveryTestHelper.CreateService(database);

        await service.ScanAsync(installId);

        // Derinlik limiti: iç klasörler atlanır → tarama kısmi.
        var partial = await service.ScanAsync(
            installId,
            progress: null,
            options: new LocalizationScanOptions { MaxDepth = 0 });

        Assert.True(partial.WasPartial);
        Assert.Equal(0, partial.MarkedMissing);

        using var context = database.CreateContext();
        Assert.Equal(2, context.DiscoveredFiles.Count(f => f.IsReadable));
    }

    [Fact]
    public async Task ScanAsync_DoesNotModifyFilesOnDisk()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        var path = tree.Write(@"Localization\a.json", "icerik-degisMemeli");
        var before = System.IO.File.GetLastWriteTimeUtc(path);
        var beforeText = System.IO.File.ReadAllText(path);

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        await DiscoveryTestHelper.CreateService(database).ScanAsync(installId);

        Assert.Equal(beforeText, System.IO.File.ReadAllText(path));
        Assert.Equal(before, System.IO.File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task ScanAsync_Cancelled_ThrowsAndDoesNotMarkMissing()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\a.json");
        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var service = DiscoveryTestHelper.CreateService(database);

        await service.ScanAsync(installId);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await service.ScanAsync(installId, progress: null, options: null, cts.Token));

        using var context = database.CreateContext();
        Assert.True(context.DiscoveredFiles.Single().IsReadable);
    }

    [Fact]
    public async Task ScanAsync_SeedsFileFormats_Once()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\a.json");
        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var service = DiscoveryTestHelper.CreateService(database);

        var firstRunAdded = await service.EnsureFormatsAsync();
        var secondRunAdded = await service.EnsureFormatsAsync();

        await service.ScanAsync(installId);

        Assert.Equal(LocalizationFormatCatalog.All.Count, firstRunAdded);
        Assert.Equal(0, secondRunAdded);

        using var context = database.CreateContext();
        Assert.Equal(LocalizationFormatCatalog.All.Count, context.FileFormats.Count());
        Assert.All(context.FileFormats, f => Assert.False(f.SupportsRead));
        Assert.All(context.FileFormats, f => Assert.False(f.SupportsWrite));
    }

    [Fact]
    public async Task ScanAsync_CreatesFormatEvidence_OnFirstDiscoveryOnly()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\ui_text.json");
        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var service = DiscoveryTestHelper.CreateService(database);

        await service.ScanAsync(installId);

        int afterFirst;
        using (var context = database.CreateContext())
        {
            afterFirst = context.FormatEvidences.Count();
            Assert.True(afterFirst >= 3); // uzantı + dosya adı + klasör
        }

        await service.ScanAsync(installId);

        using var verify = database.CreateContext();
        Assert.Equal(afterFirst, verify.FormatEvidences.Count());
    }

    [Fact]
    public async Task ScanAsync_LargeFile_SkipsHashButStillDiscovered()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\big.txt", new string('a', 64));
        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);

        var result = await DiscoveryTestHelper.CreateService(database)
            .ScanAsync(installId, progress: null, options: new LocalizationScanOptions { MaxHashBytes = 10 });

        Assert.Equal(1, result.Added);

        using var context = database.CreateContext();
        var row = context.DiscoveredFiles.Single();

        Assert.Equal(string.Empty, row.Sha256);
        Assert.Contains("SHA-256", row.LastValidationResult);
    }

    [Fact]
    public async Task ScanAsync_ReportsProgress()
    {
        using var database = new TempDatabase();
        using var tree = new TempTree();

        tree.Write(@"Localization\a.json");
        tree.Write(@"Localization\b.json");

        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, tree.Root);
        var progress = new CollectingProgress();

        await DiscoveryTestHelper.CreateService(database)
            .ScanAsync(installId, progress, new LocalizationScanOptions { ProgressIntervalFiles = 1 });

        Assert.NotEmpty(progress.Items);
        Assert.Equal(2, progress.Items.Max(p => p.FilesSeen));
    }

    [Fact]
    public async Task ScanAsync_UnknownInstallation_ReturnsFailure()
    {
        using var database = new TempDatabase();
        var result = await DiscoveryTestHelper.CreateService(database).ScanAsync(9999);

        Assert.False(result.Success);
        Assert.Contains("bulunamadı", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_MissingRootFolder_ReturnsFailure()
    {
        using var database = new TempDatabase();
        var missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gle_missing_{Guid.NewGuid():N}");
        var installId = await DiscoveryTestHelper.CreateInstallAsync(database, missing);

        var result = await DiscoveryTestHelper.CreateService(database).ScanAsync(installId);

        Assert.False(result.Success);
        Assert.Contains("Kurulum klasörü", result.ErrorMessage!);
    }
}




