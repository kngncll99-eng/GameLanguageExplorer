# Game Language Explorer

Game Language Explorer, Windows 10/11 üzerinde çalışan; oyunları tek bir katalogda toplamayı, kurulu oyunları salt okunur biçimde keşfetmeyi ve oyunların yerelleştirme dosyalarını güvenli biçimde incelemeyi amaçlayan bir WPF masaüstü uygulamasıdır.

> **Mevcut durum:** Erken geliştirme aşaması. Katalog, Steam/Epic/GOG/Ubisoft/manuel kurulu oyun keşfi, dil dosyası keşfi ve önizlemesi, JSON içerik kontrolü, JSON/CSV/PO/INI için güvenli yazma altyapısı, AI Çeviri ekranında onaylı “Dosyaya Yaz” ve JSON/CSV/PO dışa aktarma komutları, yan yana yerelleştirme editörü ve OpenAI uyumlu yerel LLM ile çeviri denemeleri çalışıyor. JSON/CSV/PO/INI dışı formatlarda yazma, yedekleme ekranı ve eklenti sistemi henüz yok.

## Amaç ve Hedef

Projenin amacı, farklı oyun kaynaklarını ortak bir veri modelinde birleştirmek ve yerelleştirme süreçlerini daha görünür, güvenli ve izlenebilir hale getirmektir.

Temel hedefler:

- Steam başta olmak üzere oyunları aramak, kataloğa eklemek ve yerel SQLite veritabanında saklamak.
- Kurulu oyunları mağaza manifestlerinden veya kullanıcının seçtiği klasörlerden salt okunur biçimde tespit etmek.
- Oyun klasörlerindeki dil dosyası adaylarını keşfetmek; format, güven düzeyi ve kanıtlarıyla birlikte raporlamak.
- JSON, XML, CSV, TSV, PO, POT, INI ve LocresCSV dosyalarını ayrıştırıp metin kayıtlarını önizlemek; JSON/CSV/PO/INI için güvenli yazma.
- Yerel veya OpenAI uyumlu bir LLM ile çeviri adayları üretmek; yer tutucuları, kaliteyi ve çalışma günlüğünü yönetmek.
- Uzun vadede güvenli düzenleme, yedekleme, geri yükleme ve motor özelinde format eklentileri sunmak.

Keşif ve önizleme varsayılan olarak **salt okunurdur**. JSON/CSV/PO/INI dosyaları için açıkça çağrılan güvenli yazma servisi (yedek → geçici dosya → SHA-256 → atomik taşıma → gerekirse geri yükleme) vardır. AI Çeviri ekranındaki **Dosyaya Yaz** komutu yalnızca kullanıcı onayından sonra çalışır; otomatik yazma yapmaz.

## Tamamlanan Özellikler ve Modüller

### 1. Masaüstü Uygulama ve Tema

- WPF tabanlı tek pencereli `Shell` ve MVVM navigasyonu.
- Çalışan ekranlar:
  - **Global Oyun Kataloğu**
  - **Kurulu Oyunlar**
  - **Dil Dosyası Keşfi**
  - **AI Çeviri**
- **Editör** ekranı aktiftir; Formatlar, Yedekleme ve Ayarlar menü düğmeleri şu an yer tutucudur.
- Koyu tema varsayılandır; açık/koyu tema arasında geçiş yapılabilir.
- Tema tercihi `%LOCALAPPDATA%\GameLanguageExplorer\ui-preferences.json` dosyasında saklanır.
- Seçili oyun adına göre tahmini bir vurgu rengi üreten oyun paleti vardır; bu renk kesin bilgi olarak sunulmaz.

### 2. Oyun Kataloğu

- Steam mağazasında isimle arama ve sonuçları kataloğa aktarma.
- Steam arama sonuçlarında ad, geliştirici, yayıncı, kısa açıklama, çıkış tarihi ve kapak görseli gibi alanların zenginleştirilmesi.
- `STEAM_API_KEY` ortam değişkeni varsa Steam tam katalog senkronizasyonu.
- Katalog kayıtlarının SQLite üzerinde tutulması, arama ve listeleme.
- Katalog listesinde ilk 100 kaydın gösterilmesi; toplam katalog sayısının ayrıca raporlanması.
- Hata, boş sonuç ve iptal durumlarının arayüzde bildirilmesi.

Steam dışında katalog sağlayıcısı henüz yoktur.

### 3. Kurulu Oyun Keşfi

- **Steam (salt okunur):**
  - Steam kökünü kayıt defterinden ve bilinen yollardan bulma.
  - `libraryfolders.vdf` ile ek kütüphaneleri okuma.
  - `appmanifest_*.acf` dosyalarından AppID, oyun adı, kurulum klasörü ve boyut çıkarma.
- **Epic Games (salt okunur):**
  - `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests` ve kullanıcı profilindeki bilinen manifest klasörlerini tarama.
  - `.item` manifestlerinden `DisplayName`, `InstallLocation`, `AppName`, `CatalogItemId`, `AppVersion` bilgilerini okuma.
  - Bozuk/eksik manifestleri atlama; hiç yol bulunamazsa mevcut kayıtları silmeme.
- **GOG (salt okunur):**
  - `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games` ve `HKLM\SOFTWARE\GOG.com\Games` kayıt defteri anahtarlarını okuma.
  - Bilinen GOG klasörlerindeki oyunları ve `gameInfo.igd` / `goggame-*.info` metadata dosyalarını tarama.
  - Oyun adı, kurulum klasörü ve mümkünse GOG oyun ID’sini çıkarma.
- **Ubisoft Connect (salt okunur):**
  - `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs` ve `HKLM\SOFTWARE\Ubisoft\Launcher\Installs` anahtarlarını okuma.
  - `%LOCALAPPDATA%\Ubisoft Game Launcher` ve `%PROGRAMDATA%\Ubisoft` config/cache JSON dosyalarını tarama.
  - Oyun klasörlerindeki `uplay_install.manifest` / `uplay_install.state` metadata dosyalarından ad, ID ve çalıştırılabilir dosya çıkarma.
- **Manuel klasör:** Kullanıcının seçtiği oyun klasörünü `Manual` kaynağıyla ekleme.
- Kaynağa göre filtreleme ve tarama sonucu özeti.
- Eksik/erişilemeyen klasörlerde mevcut kayıtları koruma; başarısız veya kısmi taramada kayıt silmeme.
- Steam, Epic, GOG ve Ubisoft dışındaki mağazalar (`EA`, `Xbox`) yalnızca “planlandı” olarak listelenir; taramaya katılmaz.

### 4. Dil Dosyası Keşfi

- Seçilen kurulum klasöründe özyinelemeli, salt okunur tarama.
- İlerleme raporu ve iptal desteği.
- Kök dizin sınırı, sembolik bağlantı döngüleri ve kök dışına işaret eden bağlantılar için güvenlik kontrolleri.
- Aday formatlar:
  - Düz metin: `JSON`, `XML`, `CSV`, `TSV`, `PO`, `POT`, `INI`, `YAML/YML`, `TXT`, `STRINGS`, `LOC`
  - İkili: `MO`
  - Paket/arşiv: `PAK`, `BUNDLE`, `ARCHIVE`, `ASSETS`, `UASSET`, `BANK`
- Paketler metin olarak sınıflandırılmaz; yalnızca dosya/klasör adında yerelleştirme ipucu varsa listelenir.
- Her aday için uzantı, boyut, format, güven düzeyi, destek durumu ve gerekçe kaydı.
- `FormatEvidence` ile uzantı, dosya adı, klasör adı ve içerik kanıtlarının saklanması.
- JSON adaylarında sınırlı içerik incelemesi:
  - Varsayılan üst sınır 256 KB.
  - Geçerli JSON ve yeterli metin değeri güveni yükseltir.
  - Geçersiz, boş, okunamayan, aşırı büyük veya belirsiz JSON güveni düşürür; tarama durmaz.
- SHA-256 yalnızca adaylar için hesaplanır; 64 MB üzerindeki dosyalarda atlanır.
- Tekrar tarama mükerrer kayıt oluşturmaz; bulunamayan dosyalar silinmez, “artık bulunamadı” olarak işaretlenir.

### 5. Dil Dosyası Önizlemesi

Salt okunur önizleme için kayıtlı adaptörler:

| Format | Özellik |
|--------|---------|
| JSON | JSON Pointer konumu; string değerleri aday, sayı/boolean/null atlanır |
| XML | Element metni ve metin attribute; DTD kapalı, XXE engellenir |
| CSV | Otomatik ayraç (virgül, noktalı virgül, sekme, pipe), RFC 4180 tırnakları |
| TSV | CSV motoru, sabit sekme ayracı |
| PO | `msgctxt`, çok satırlı dizeler, `#, fuzzy`, mevcut çeviri |
| POT | Gettext şablonu; boş `msgstr` normal kabul edilir |
| INI | `[Bölüm]` bağlamı, ilk `=`, yorumlar ve sıra korunur |
| LocresCSV | `key,source,Translation` başlık şeması; başlıkla CSV’den ayrılır |

- Yer tutucular (`{ad}`, `{0}`, `%s`, `%d`, `\n`, `<b>`, `[color=red]` gibi) taranır.
- Çeviri kalitesi için yer tutucu/karakter karşılaştırması ve kalite doğrulayıcıları vardır.
- Dosya boyutu varsayılan 32 MB ile sınırlıdır.
- UTF-8 ve UTF-16 desteklenir; diğer kodlamalar okunabilir hata ile reddedilir.
- Önizleme veritabanına yazmaz; sonuçlar geçici modeldir.
- Şu an **JSON, CSV, PO ve INI** için güvenli yazma altyapısı vardır; bu adaptörlerde `SupportsWrite=true`, diğerlerinde `false`. CSV'de yalnızca başlıkta tanınan çeviri sütunu (`translation`, `target`, `translated` vb.), PO'da yalnızca `msgstr` / `msgstr[n]` alanları, INI'de yalnızca `key=value` değer tarafı güncellenir; yorumlar, bölümler, `msgid`, `msgctxt` ve yapı korunur. Yazma servisi orijinali doğrudan değiştirmez; yedek, geçici dosya ve SHA-256 doğrulaması kullanır.

### 6. AI Çeviri

- AI Çeviri ekranında kurulu oyun ve keşfedilmiş dosya seçimi.
- Çeviri öncesi salt okunur önizleme ve çevrilebilir kayıt sayısı.
- Varsayılan olarak yerel LM Studio uç noktası (`http://127.0.0.1:1234/v1`) ve yerel model kullanılır.
- OpenAI uyumlu herhangi bir uç nokta yapılandırılabilir.
- API anahtarı isteğe bağlıdır; varsayılan olarak `GLE_LLM_API_KEY` ortam değişkeninden okunur.
- Partili çeviri, ilerleme, iptal ve önceki çalışmayı sürdürme desteği.
- Sağlayıcı yanıtı JSON sözleşmesiyle doğrulanır; eksik/bozuk yanıtlar güvenli biçimde raporlanır.
- Yer tutucular maskelenir ve çeviri sonrası geri yüklenir.
- Kalite/terminoloji politikası ve yerleşik oyun sözlüğü uygulanır; kullanıcı sözlüğü için motor sözleşmesi hazırdır ancak arayüze bağlı değildir.
- AI çağrısından önce `TranslationMemoryEntry` tablosunda kaynak metin + kaynak dil + hedef dil tam eşleşmesi aranır; eşleşme `Yeniden kullanıldı` durumuyla ve sağlayıcı çağrılmadan kullanılır. Başarılı yeni çeviriler belleğe yazılır.
- Sonuçlar `Aday`, `İnceleme`, `Başarısız`, `Yeniden kullanıldı` ve `Atlandı` durumlarıyla gösterilir.
- Çalışma günlüğü JSON olarak `%LOCALAPPDATA%\GameLanguageExplorer\translation-runs` altında tutulur.
- Günlük atomik yazılır; bozuk günlük sessizce sıfırlanmaz.
- **Dosyaya Yaz:** Seçili JSON/CSV/PO/INI dosyası için `Aday` ve `Yeniden kullanıldı` durumundaki çevirileri, kullanıcı onayı ve yedekleme sonrası güvenli yazma servisiyle dosyaya yazar. CSV'de yalnızca çeviri sütunu, PO'da yalnızca `msgstr` alanları, INI'de yalnızca değer tarafı güncellenir; değişen PO kaydındaki `fuzzy` işareti kaldırılır.
- **Dışa Aktar:** Seçili çevirileri yeni bir **JSON, CSV veya PO** dosyasına kaydeder. `SaveFileDialog` kullanılır; oyun dosyası değiştirilmez.
- Dışa aktarmada yer tutucular korunur; kaynak/çeviri yer tutucuları uyuşmazsa sonuçta uyarı gösterilir.
- Yazma sırasında ilerleme gösterilir; işlem iptal edilebilir ve başarı/hata/geri yükleme mesajı arayüzde gösterilir.
- Diğer formatlarda oyun dosyasına yazma komutu pasif kalır.

### 7. Yerelleştirme Editörü

- Kurulu oyun ve keşfedilmiş dosya seçimi.
- Sol sütunda orijinal metin, sağ sütunda düzenlenebilir çeviri.
- Kayıtlar `ILocalizationPreviewService` ve `LocalizationTextEntry` üzerinden yüklenir.
- Düzenleme yalnızca `SupportsWrite=true` dosyalarda açıktır; şu an JSON, CSV, PO ve INI.
- `Skipped` kayıtlar salt okunur gösterilir.
- Değişiklikler `LocalizationWriteService.WriteAsync` ile kaydedilir; önce yedekleme/onay akışı çalışır.
- DataGrid satır/kolon sanallaştırması ve koyu tema kaynakları kullanılır.
- JSON/CSV/PO/INI dışındaki dosyalar önizlenebilir fakat salt okunur kalır.

### 8. Veri ve Kalıcılık

- Entity Framework Core + SQLite.
- Uygulama başlangıcında bekleyen migration’lar otomatik uygulanır.
- Veritabanı: `%LOCALAPPDATA%\GameLanguageExplorer\gamelanguageexplorer.db`
- Başlıca tablolar/entity’ler:
  - Oyun kataloğu: `Game`, `GamePlatform`, `GameVersion`, `GameProfile`, `CatalogSource`
  - Kurulumlar: `InstalledGame`
  - Dil dosyaları: `DiscoveredFile`, `FileFormat`, `FormatEvidence`
  - Yerelleştirme: `LocalizationProject`, `LocalizationEntry`, `GlossaryTerm`, `TranslationMemoryEntry`
  - Diğer: `BackupRecord`, `PluginRegistration`
- `BackupRecord` güvenli yazma servisi tarafından, `TranslationMemoryEntry` ise çeviri motoru tarafından kullanılır. `GlossaryTerm` ve `PluginRegistration` tabloları şema olarak hazırdır; ilgili iş akışlarının bir kısmı henüz bağlı değildir.

### 9. Test ve Doğrulama

- xUnit tabanlı birim ve entegrasyon testleri.
- Son doğrulanan durum: **629 test başarılı**, 0 başarısız, 0 atlanan.
- `scripts/validate-project.ps1` salt okunur doğrulama yapar:
  - `dotnet build`
  - `dotnet test`
  - Git durum özeti
  - JSON ve Markdown raporu
- Raporlar: `%LOCALAPPDATA%\GameLanguageExplorer\validation-runs`

## Kullanılan Teknolojiler ve Kütüphaneler

| Alan | Teknoloji |
|------|-----------|
| Dil / çalışma zamanı | C#, .NET 10 |
| Masaüstü UI | WPF |
| MVVM | CommunityToolkit.Mvvm 8.4.2 |
| Veri erişimi | Entity Framework Core 10.0.13 |
| Veritabanı | SQLite |
| DI / hosting | Microsoft.Extensions.DependencyInjection, Hosting |
| HTTP | Microsoft.Extensions.Http |
| Loglama | Microsoft.Extensions.Logging |
| JSON | `System.Text.Json` |
| XML | `System.Xml` / güvenli XML ayarları |
| Test | xUnit 2.9.3, Microsoft.NET.Test.Sdk, coverlet |
| Hedef platform | Windows 10/11, `net10.0-windows` |

Projede JSON, XML, CSV/TSV, PO/POT, INI ve LocresCSV için özel yazılmış okuma adaptörleri kullanılır; JSON/CSV/PO/INI yazma desteği aynı adaptörlerdedir ve harici bir yerelleştirme ayrıştırma kütüphanesi yoktur.

## Klasör Yapısı

```text
GameLanguageExplorer.slnx
├── src/
│   ├── Core/
│   │   ├── Entities/              # EF Core varlıkları
│   │   ├── Interfaces/            # Katalog, kurulum ve format sözleşmeleri
│   │   └── Services/
│   │       ├── Steam/             # Steam katalog ve manifest okuma
│   │       ├── Localization/      # Format kataloğu, sınıflandırıcı, JSON içerik incelemesi
│   │       │   └── Preview/       # Dosya okuyucu, adaptörler, yer tutucu/kalite araçları
│   │       └── Translation/       # OpenAI uyumlu sağlayıcı, çeviri motoru, günlük
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── Migrations/
│   │   └── Services/              # Katalog senkronizasyonu, kurulum/dil dosyası tarama, önizleme
│   └── WPF/
│       ├── App.xaml / App.xaml.cs # DI, migration, tema başlangıcı
│       ├── Views/                 # Shell, katalog, kurulu oyunlar, keşif, çeviri
│       ├── ViewModels/            # MVVM ekran mantığı
│       ├── Services/              # Tema, klasör seçici, Steam kök bulucu
│       └── Themes/                # Koyu/açık tema sözlükleri
├── tests/Tests/                   # xUnit testleri
├── scripts/validate-project.ps1   # Salt okunur build/test/Git doğrulaması
├── ARCHITECTURE.md
├── ROADMAP.md
├── SUPPORTED-FORMATS.md
├── CATALOG-SOURCES.md
├── NTE-LOCALIZATION-RESEARCH.md
└── SECURITY.md
```


## Önemli Dosyalar

| Dosya | Görev |
|-------|-------|
| `src/WPF/App.xaml.cs` | DI kapsayıcısı, migration uygulama, tema başlangıcı |
| `src/WPF/ViewModels/ShellViewModel.cs` | Ana pencere navigasyonu ve tema geçişi |
| `src/WPF/ViewModels/CatalogViewModel.cs` | Katalog arama, listeleme ve Steam içe aktarma |
| `src/WPF/ViewModels/InstalledGamesViewModel.cs` | Kurulu oyun tarama, filtreleme ve manuel klasör ekleme |
| `src/WPF/ViewModels/FileDiscoveryViewModel.cs` | Dil dosyası tarama ve salt okunur önizleme |
| `src/WPF/ViewModels/TranslationViewModel.cs` | AI çeviri akışı, LM Studio bağlantısı, yazma ve dışa aktarma |
| `src/Core/Services/Localization/Export/LocalizationExportService.cs` | JSON/CSV/PO dışa aktarma |
| `src/WPF/Services/SaveFileDialogService.cs` | MVVM dostu hedef dosya seçimi |
| `src/WPF/ViewModels/EditorViewModel.cs` | Yan yana düzenleme, değişiklik takibi ve güvenli kaydetme |
| `src/WPF/Views/EditorView.xaml` | Sanallaştırılmış kaynak/çeviri editörü |
| `src/Data/AppDbContext.cs` | EF Core DbSet’leri ve model yapılandırması |
| `src/Data/Services/CatalogSyncService.cs` | Katalog kayıtlarının eklenmesi/güncellenmesi |
| `src/Data/Services/InstalledGameScanService.cs` | Steam/Epic/GOG/Ubisoft/manuel kurulum kayıtlarının taranması |
| `src/Data/Services/LocalizationFileScanService.cs` | Dil dosyası adaylarının salt okunur keşfi |
| `src/Data/Services/LocalizationPreviewService.cs` | Yol güvenliği, adaptör seçimi ve önizleme |
| `src/Data/Services/LocalizationWriteService.cs` | Yedek, geçici dosya, SHA-256 doğrulama ve atomik yazma |
| `src/Core/Services/Localization/Preview/Adapters/JsonLocalizationAdapter.cs` | JSON okuma ve güvenli yazma içeriği üretimi |
| `src/Core/Services/Localization/LocalizationFormatCatalog.cs` | Formatlar, güven düzeyleri ve destek durumu |
| `src/Core/Services/Localization/JsonContentInspector.cs` | JSON içerik/güven sinyali |
| `src/Core/Services/Localization/Preview/Adapters/` | JSON, XML, CSV, TSV, PO, POT, INI, LocresCSV adaptörleri |
| `src/Core/Services/Translation/TranslationEngine.cs` | Partili çeviri, iptal/devam, kalite ve günlük akışı |
| `src/Core/Services/Translation/OpenAiCompatibleTranslationProvider.cs` | OpenAI uyumlu sağlayıcı istemcisi |
| `tests/Tests/` | Core, Data ve WPF iş mantığı testleri |
| `scripts/validate-project.ps1` | Build/test/Git doğrulaması ve rapor üretimi |

## Çalışan Kısımlar

Aşağıdaki modüller mevcut kodda uygulanmıştır ve build/test ile doğrulanmıştır:

| Modül | Durum | Not |
|-------|-------|-----|
| WPF Shell ve navigasyon | ✅ Çalışıyor | Katalog, Kurulu Oyunlar, Dil Keşfi, AI Çeviri |
| Koyu/açık tema | ✅ Çalışıyor | Tercih JSON dosyasında kalıcı |
| Steam arama ve içe aktarma | ✅ Çalışıyor | API anahtarı gerektirmeyen arama |
| Steam tam katalog senkronizasyonu | ✅ Çalışıyor | `STEAM_API_KEY` gerekir |
| Steam kurulu oyun taraması | ✅ Çalışıyor | Salt okunur manifest taraması |
| Epic kurulu oyun taraması | ✅ Çalışıyor | Salt okunur `.item` manifest taraması |
| GOG kurulu oyun taraması | ✅ Çalışıyor | Registry + `gameInfo.igd` (salt okunur) |
| Ubisoft kurulu oyun taraması | ✅ Çalışıyor | Registry + config/cache + `uplay_install` (salt okunur) |
| Manuel oyun klasörü ekleme | ✅ Çalışıyor | `Manual` kaynağı, doğrulanmamış ad |
| Dil dosyası keşfi | ✅ Çalışıyor | İlerleme, iptal, yol güvenliği, SHA-256 |
| JSON içerik/güven kontrolü | ✅ Çalışıyor | Sınırlı, salt okunur, güvenli hata durumları |
| Güvenli JSON/CSV/PO/INI yazma | ✅ Çalışıyor | `BackupRecord`, SHA-256, atomik taşıma, geri yükleme; AI Çeviri/Editör |
| JSON/XML/CSV/TSV/PO/POT/INI/LocresCSV önizleme | ✅ Çalışıyor | Salt okunur |
| Yer tutucu ve kalite kontrolleri | ✅ Çalışıyor | Çeviri ve önizleme akışında |
| OpenAI uyumlu AI çeviri | ✅ Çalışıyor | Varsayılan yerel LM Studio; JSON için onaylı Dosyaya Yaz |
| Çeviri dışa aktarma | ✅ Çalışıyor | JSON/CSV/PO; yeni dosya, yer tutucu uyarıları |
| SQLite şeması ve migration’lar | ✅ Çalışıyor | Başlangıçta otomatik migration |
| Yan yana editör | ✅ Çalışıyor | JSON düzenleme; diğer formatlar salt okunur |
| Doğrulama script’i ve testler | ✅ Çalışıyor | Son durum: 629/629 test |

> WPF ekranlarının tamamı otomatik UI testleriyle kapsanmaz; build/test sonuçları iş mantığını doğrular, görsel akış manuel kontrol gerektirir.

## Eksik Kalan veya Henüz Başlamayan Kısımlar

### Dosya yazma ve güvenli düzenleme

- JSON için güvenli yazma servisi vardır; diğer formatlarda geri yazma yoktur.
- Yan yana editör JSON için vardır; diğer formatlarda düzenleme ve yazma UI'ı yoktur.
- JSON/CSV/PO/INI dışındaki formatlarda yedekleme, atomik yazma, SHA-256 bütünlük doğrulaması ve geri yükleme akışı yoktur.
- `BackupRecord` tablosu JSON güvenli yazma servisinde kullanılır.

### Format kapsamı

- `YAML`, `TXT`, `STRINGS`, `LOC` ve `MO` yalnızca keşfedilir; ayrıştırıcıları yoktur.
- Unreal `.locres`, `.uasset` ve şifreli `.pak` içerikleri okunmuyor.
- NTE için gerçek `.locres`/PAK doğrulaması; şifreleme ve gerekli ön koşullar nedeniyle henüz yapılamıyor.
- UTF-8/UTF-16 dışı kodlamalar (ör. Windows-1254) reddedilir.

### Mağaza ve platform kapsamı

- Steam dışında katalog sağlayıcısı yok.
- EA App ve Xbox için kurulu oyun sağlayıcısı yok.
- Steam manifesti oyun/araç ayrımı yapmadığı için dağıtım paketleri de listelenebilir.

### Çeviri ve veri

- AI çeviri ekranı sonuçları yalnızca kullanıcı onayıyla JSON dosyasına yazılabilir; diğer formatlarda yazma yoktur.
- `GlossaryTerm` tablosu çeviri akışına tam bağlı değil; çeviri belleği tam eşleşmeli olarak çalışıyor, dosya tabanlı günlük ve yerleşik sözlük de kullanılmaya devam ediyor.
- Sağlayıcı profili/yönetim ekranı ve API anahtarı saklama arayüzü yok; anahtar ortam değişkeninden okunur.
- Tam katalog senkronizasyonu ve büyük dosya önizlemesi için ayrıntılı ilerleme/iptal deneyimi sınırlı.

### Eklenti ve dağıtım

- `IGameFormatPlugin` yalnızca arayüzdür; eklenti yükleme/kayıt sistemi yok.
- `PluginRegistration` tablosu hazır ancak kullanılmıyor.
- Windows MSI/EXE dağıtım paketi ve kod imzalama yok.


## Sonraki Adımlar İçin Öneriler

1. **Yedek yönetimi:** Geçmiş yedekleri listeleyen, geri yükleme ve temizleme işlemlerini sunan ekran.
2. **Editör geliştirmeleri:** Arama/filtreleme, toplu düzenleme ve değişiklik geçmişi eklemek.
3. **Dışa aktarma geliştirmeleri:** XLIFF/XML gibi ek formatlar ve dışa aktarma şablonları eklemek.
4. **Format kapsamını genişletme:** Öncelik sırasıyla `MO`, `YAML`, `TXT`, `STRINGS`, `LOC` ve Unreal `.locres` araştırması.
5. **Mağaza sağlayıcıları:** EA ve Xbox için salt okunur kurulum tespiti.
6. **Çeviri altyapısını tamamlama:** Çeviri belleği ve sözlük tablolarını akışa bağlamak; sağlayıcı profili ve güvenli anahtar yönetimi eklemek.
7. **Eklenti sistemi:** `IGameFormatPlugin` için yükleme, sürüm uyumluluğu ve güvenlik sınırları.
8. **Performans ve UX:** Katalog senkronizasyonu, büyük dosya önizlemesi ve uzun listeler için ilerleme, iptal ve sanallaştırma.
9. **Dağıtım:** Windows installer, sürümleme, kod imzalama ve temiz kurulum doğrulaması.

## Kurulum ve Çalıştırma

Gereksinimler:

- Windows 10/11
- .NET 10 SDK

```powershell
# Bağımlılıkları geri yükle
dotnet restore ".\GameLanguageExplorer.slnx"

# Derle
dotnet build ".\GameLanguageExplorer.slnx" -v:minimal

# Testleri çalıştır
dotnet test ".\tests\Tests\GameLanguageExplorer.Tests.csproj" -v:minimal

# Uygulamayı çalıştır
dotnet run --project ".\src\WPF\GameLanguageExplorer.WPF.csproj"
```

Salt okunur doğrulama ve rapor:

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\validate-project.ps1"
```

AI çeviri için varsayılan yerel LM Studio uç noktası `http://127.0.0.1:1234/v1` olarak ayarlıdır. Kimlik doğrulaması gereken bir sağlayıcı kullanılacaksa `GLE_LLM_API_KEY` ortam değişkeni tanımlanabilir.

## Dokümantasyon

- [ARCHITECTURE.md](ARCHITECTURE.md) — Katmanlar, mimari ve güvenlik sınırları
- [ROADMAP.md](ROADMAP.md) — Geliştirme aşamaları
- [SUPPORTED-FORMATS.md](SUPPORTED-FORMATS.md) — Format destek matrisi ve ayrıştırma kuralları
- [CATALOG-SOURCES.md](CATALOG-SOURCES.md) — Katalog kaynakları
- [NTE-LOCALIZATION-RESEARCH.md](NTE-LOCALIZATION-RESEARCH.md) — NTE araştırması ve doğrulama sınırları
- [SECURITY.md](SECURITY.md) — Güvenlik politikası

## Lisans

MIT