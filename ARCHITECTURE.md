# Architecture - Game Language Explorer

## Genel Mimari

Uygulama, Modüler Monolitik mimari desenini izler. WPF masaüstü uygulaması, Katmanlı Mimari (Layered Architecture) prensipleriyle tasarlanmıştır.

## Katmanlar

### 1. Core (GameLanguageExplorer.Core)

Varlık (Entity), Arayüz (Interface) ve Sözleşme (Contract) tanımlarını içerir. Herhangi bir harici bağımlılığa sahip değildir.

**Önemli Bileşenler:**
- `Entities/` - Veritabanı varlıkları (Game, GamePlatform, GameProfile, vb.)
- `Interfaces/` - Sözleşmeler (IGameCatalogProvider, IInstalledGameProvider, ILocalizationFormatAdapter, IGameFormatPlugin)

### 2. Data (GameLanguageExplorer.Data)

Veri erişim katmanı. Entity Framework Core ve SQLite kullanır.

**Önemli Bileşenler:**
- `AppDbContext.cs` - EF Core DbContext, tüm DbSet ve Fluent API konfigürasyonları
- `AppDbContextFactory.cs` - Design-time DbContext factory (migrationler için)
- `Migrations/` - Veritabanı şema migrationları

### 3. WPF (GameLanguageExplorer.WPF)

Kullanıcı arayüzü katmanı. MVVM deseni kullanır.

**Önemli Bileşenler:**
- `App.xaml` - Uygulama kaynakları: `DataTemplate` (ViewModel → View eşlemesi), stiller,
  görünürlük dönüştürücüleri
- `App.xaml.cs` - DI konteyner yapılandırması, migration uygulama, pencere oluşturma
- `Views/` - `Shell.xaml` (ana pencere, navigasyon), `CatalogView.xaml` (katalog ekranı),
  `InstalledGamesView.xaml` (kurulu oyunlar ekranı), `EditorView.xaml` (yan yana editör)
- `ViewModels/` - `ViewModelBase`, `ShellViewModel`, `CatalogViewModel`, `InstalledGamesViewModel`,
  `EditorViewModel`
- `Converters/` - `BoolToVisibilityConverter` (Invert destekli), `StringToVisibilityConverter`,
  `IntToVisibilityConverter`
- `Services/` - `RegistrySteamRootLocator` (Windows kayıt defterinden Steam kökü; `ISteamRootLocator`)

### 4. Tests (GameLanguageExplorer.Tests)

xUnit tabanlı birim testleri.

## Tasarım Prensipleri

1. **MVVM Pattern** - ViewModelBase -> ObservableObject (CommunityToolkit.Mvvm)
2. **Dependency Injection** - `Microsoft.Extensions.Hosting` ile servis kayıtları. `App.xaml.cs`
   içinde `Host.CreateDefaultBuilder()` kullanılır; tüm UI bileşenleri singleton'dır (tek pencereli
   masaüstü uygulaması).
3. **DbContext Factory** - `AddDbContextFactory<AppDbContext>` kullanılır. ViewModel ve servisler
   `IDbContextFactory<AppDbContext>` üzerinden **her işlem için kısa ömürlü** bir context oluşturur.
   Bu, uzun ömürlü ViewModel'lerle scoped DbContext'i karıştırmayı ve change tracker şişmesini önler.
4. **Servis katmanı** - Katalog senkronizasyonu `ICatalogSyncService` / `CatalogSyncService`
   üzerinden yürür; ayrı bir Repository katmanı yoktur (DbContext doğrudan servis içinde kullanılır).
5. **Ayrıştırma katmanı** - `ILocalizationFormatAdapter` sözleşmesi ve JSON/XML/CSV/TSV/PO/POT/INI/LocresCSV
   uygulamaları `Core/Services/Localization/Preview/Adapters` altındadır. JSON ayrıca
   `ILocalizationWritableFormatAdapter` ile güvenli yazma içeriği üretir. `IGameFormatPlugin`
   hâlâ yalnızca arayüzdür.
6. **Önizleme servisi** - `LocalizationPreviewService` (Data) dosyayı açmadan önce kurulum kökü
   sınırını doğrular, adaptörü seçer ve sonucu döndürür; veritabanına yazmaz.

## Uygulama Başlangıç Akışı

1. `App.InitializeComponent()` → `App.xaml` kaynakları (stiller, DataTemplate, converter'lar) yüklenir.
2. `App.OnStartup` → `DatabaseConfiguration.EnsureDirectoryExists()`.
3. `Host.CreateDefaultBuilder()` ile DI konteyneri kurulur.
4. `Database.Migrate()` → bekleyen migration'lar uygulanır (şema yoksa oluşturulur, veri silinmez).
5. `Shell` (Window) DI'dan çözülür; `DataContext = ShellViewModel`.
6. `ShellViewModel` ctor'unda `CurrentViewModel = CatalogViewModel` atanır.
7. `Shell.xaml` içindeki `ContentControl`, `App.xaml`'daki `DataTemplate` sayesinde
   `CatalogViewModel` → `CatalogView` eşlemesini kullanır.
8. `Window.Show()` ile ana pencere görünür olur.
9. Başlangıçta istisna oluşursa `ILogger` üzerinden `Critical` seviyede loglanır, kullanıcıya
   `MessageBox` ile gösterilir ve uygulama sıfırdan farklı çıkış koduyla kapanır.

## Veritabanı Şeması

Aşağıdaki varlıklar SQLite veritabanında saklanır:

- **Game** - Oyun temel bilgileri (ad, geliştirici, yayıncı, çıkış tarihi)
- **GamePlatform** - Platform özgü oyun kimlikleri (Steam ID, Epic ID vb.)
- **CatalogSource** - Katalog kaynakları (Steam, Epic vb.)
- **GameProfile** - Oyun profil bilgileri (dil dosyası uzantıları, formatlar, doğrulama durumu)
- **GameVersion** - Oyun sürüm bilgileri
- **InstalledGame** - Kurulu oyun bilgileri (yol, executable, boyut)
- **DiscoveredFile** - Keşfedilen dosyalar (yol, SHA-256, format, okuma/yazma durumu)
- **FileFormat** - Desteklenen dosya formatları
- **FormatEvidence** - Format tespit kanıtları
- **LocalizationProject** - Yerelleştirme projeleri
- **LocalizationEntry** - Yerelleştirme girişleri (anahtar, kaynak metin, çeviri)
- **GlossaryTerm** - Terim sözlüğü
- **TranslationMemoryEntry** - Çeviri belleği
- **BackupRecord** - Yedekleme kayıtları
- **PluginRegistration** - Eklenti kayıtları

## Güvenlik İlkeleri

- Kullanıcı onayı olmadan oyun dosyaları değiştirilmez.
- Değişiklik öncesi orijinal dosyanın yedeği alınır.
- SHA-256 ile dosya bütünlüğü doğrulanır.
- Dışarıdan değişmiş dosyalarda yazma işlemi durdurulur.
- AI çevirisi sırasında kullanıcının onayı olmadan dosyalar harici servislere gönderilmez.

## Kısıtlamalar (Mevcut Durum)

**Çalışan:**
- Ana pencere (`Shell`) + navigasyon menüsü (katalog ve kurulu oyunlar dışındaki düğmeler yer tutucu)
- Steam kataloğu senkronizasyonu (API anahtarı ile) ve Steam aramasından içe aktarma
- Katalog araması (büyük/küçük harf duyarsız, ilk 100 sonuç)
- **Steam kurulu oyun taraması (salt okunur)** — `InstalledGamesView`
- SQLite şeması, migration'lar ve başlangıçta otomatik migration uygulama
- Hata/uyarı gösterimi: senkronizasyon, arama, tarama ve veritabanı hataları UI'da bildirilir

**Uygulanmadı / eksik:**
- Steam dışı kurulu oyun sağlayıcıları (Epic, Ubisoft, GOG, EA, Xbox)
- Dil dosyası adaptörleri: okuma (JSON, XML, CSV, TSV, PO, POT, INI, LocresCSV). MO/YAML/TXT/
  STRINGS/LOC ve paket formatları için ayrıştırıcı yoktur. **Güvenli yazma yalnızca JSON** için
  vardır; diğer formatlarda yazma desteği yoktur.
- Eklenti sistemi (`IGameFormatPlugin` yalnızca arayüz)
- AI çeviri motoru
- JSON güvenli yazma servisi (`BackupRecord` + geçici dosya + SHA-256 + atomik taşıma) ve
  AI Çeviri ekranında onaylı “Dosyaya Yaz” komutu vardır; JSON dışındaki formatlar henüz yoktur.
- Oyun profili ve kaynak/doğrulama ekranı (`GameProfile` tablosu var, kullanılmıyor)
- Tam katalog senkronizasyonu için ilerleme göstergesi (yalnızca belirsiz ilerleme çubuğu var)

## Kurulu Oyun Tespiti (Salt Okunur)

```
ISteamRootLocator ──► SteamLibraryLocator ──► manifest dosyaları
(Registry + yollar)   (libraryfolders.vdf)    (appmanifest_*.acf)
                                                     │
                                                     ▼
                                       SteamInstalledGameProvider
                                       (Core, dosya sistemi; DB yok)
                                                     │ InstalledGame (GameId=0, DetectedGameName)
                                                     ▼
                                       InstalledGameScanService (Data)
                                       • Steam AppID → mevcut katalog oyunu
                                       • yoksa: oyun adı → yeni Game + GamePlatform(Steam, appId)
                                       • InstalledGame upsert (kaynak + AppID + yol)
                                       • görülmeyenler: IsInstalled = false (SİLİNMEZ)
```

**Güvenlik sınırları:** keşif salt okunurdur. JSON için güvenli yazma servisi yedek, geçici dosya,
SHA-256 ve atomik taşıma kullanır; diğer formatlarda oyun dosyalarına yazma/taşıma/silme yoktur. Erişilemeyen
klasör veya bozuk manifest tüm taramayı durdurmaz (`IScanDiagnostics.LastScanWarnings` ile
raporlanır). Yönetici izni gerektiren işlem yapılmaz (HKCU öncelikli).

## Doğrulama

```powershell
dotnet build ".\GameLanguageExplorer.slnx" -v:minimal
dotnet test ".\tests\Tests\GameLanguageExplorer.Tests.csproj" -v:minimal
```

WPF arayüzü birim testleriyle tamamen doğrulanamaz; `dotnet test` yalnızca Core/Data
katmanlarını ve ViewModel dışı iş mantığını kapsar. Pencerenin gerçekten açıldığı manuel
olarak (veya uygulama başlatılıp `MainWindowTitle` kontrol edilerek) doğrulanmalıdır.