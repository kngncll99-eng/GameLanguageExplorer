# Game Language Explorer

Windows 10/11 için C# ve WPF kullanarak geliştirilen evrensel oyun kataloğu ve dil dosyası analiz aracı.

## Amaç

Farklı platformlardaki (Steam, Epic Games Store, Ubisoft Connect, GOG, EA app, Xbox) oyunları ortak bir kataloğa eklemek; her oyuna özel bilgi paneli oluşturmak ve dil dosyası formatlarını araştırıp görüntülemek.

## Teknoloji Yığını

- C# 12 / .NET 10 LTS
- WPF ve MVVM (CommunityToolkit.Mvvm)
- SQLite (Entity Framework Core)
- Dependency Injection (Microsoft.Extensions.DependencyInjection)
- Yapılandırılmış loglama (Microsoft.Extensions.Logging)
- xUnit testleri

## Proje Yapısı

```
GameLanguageExplorer/
├── src/
│   ├── Core/              # Varlıklar, arayüzler, sözleşmeler
│   ├── Data/              # SQLite DbContext, migrationlar
│   └── WPF/               # WPF arayüzü, ViewModel'ler, Görünümler
└── tests/
    └── Tests/             # xUnit testleri
```

## Kurulum

1. .NET 10 SDK'nın yüklü olduğundan emin olun (`dotnet --version` → 10.x).
2. Depoyu klonlayın ve proje kök dizinine girin.

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

Uygulama ilk açılışta veritabanı şemasını otomatik oluşturur (EF Core migration'ları
başlangıçta uygulanır). Veritabanı dosyası:

```
%LOCALAPPDATA%\GameLanguageExplorer\gamelanguageexplorer.db
```

## Arayüz: tema ve oyun paleti

- **Koyu tema varsayılandır.** Sol kenar çubuğundaki düğme ile açık temaya geçilir. Renkler
  `src/WPF/Themes/DarkTheme.xaml` ve `LightTheme.xaml` içinde **aynı anahtar setiyle** tanımlıdır;
  görünümler renkleri `DynamicResource` ile alır ve `ThemeManager` yalnızca tema sözlüğünü değiştirir.
  Bu nedenle tema değişikliği tüm ekranlara anında yansır.
- **Tercih kalıcıdır ve veritabanına yazılmaz:** `%LOCALAPPDATA%\GameLanguageExplorer\ui-preferences.json`.
  Dosya okunamaz/bozulursa uygulama sessizce koyu temaya döner (şema değişikliği yok).
- **Oyun paleti** seçili oyunun **adına göre** *tahmini* bir vurgu rengi seçer
  (`src/WPF/Services/GamePalette.cs`): NTE → turkuaz, Bannerlord / Mount & Blade → altın,
  karanlık fantezi adlı oyunlar → bordo, tanınmayanlar → nötr mavi. Tahmin olduğu arayüzde
  "tahmini" etiketi ve gerekçe metniyle belirtilir; kesin bilgi gibi sunulmaz.
- Palet yalnızca vurgu kenarlığı, sayaç şeridi, seçili satır tonu ve rozet gibi öğeleri etkiler.
  **Metin rengi her zaman tema renklerinden gelir** ve durumlar renkle birlikte metinle de ifade edilir.

## Veri Kaynakları ve Kapsam

- **Steam — isimle arama ve içe aktarma (anahtarsız, önerilen):** `store.steampowered.com/api/storesearch`
  ile arama yapılır, `store.steampowered.com/api/appdetails` ile ad, geliştirici, yayıncı,
  kısa açıklama, çıkış tarihi ve kapak görseli çekilir. Arama kutusuna bir ad yazıp
  **"Steam'den Ekle"** düğmesine basın (ör. `portal`). AppID kalıcı olarak saklanır.
- **Steam — tam katalog (API anahtarı gerekir):** Valve, anahtarsız
  `ISteamApps/GetAppList` uç noktasını **kaldırdı** (HTTP 404). Tam katalog artık yalnızca
  `IStoreService/GetAppList/v1` üzerinden alınabilir ve bir Web API anahtarı gerektirir.
  `STEAM_API_KEY` ortam değişkenini tanımlayın (https://steamcommunity.com/dev/apikey),
  ardından **"Tam Katalog Senkronize Et"** düğmesini kullanın.
  Anahtar yoksa uygulama sessizce boş sonuç vermez; nedeni açıklayan bir hata gösterir.
- **Kurulu oyun algılama — genel ilke:** Katalogda bir oyunun bulunması, o oyunun bilgisayarda
  kurulu olduğu veya dil dosyalarının doğrulandığı anlamına **gelmez**. Kurulum kaydı yalnızca
  bir sağlayıcının doğrulamasıyla veya kullanıcının açıkça eklediği klasörle oluşur.
- **Kurulu oyun algılama (Steam, salt okunur):** Sol menüden **"Kurulu Oyunlar"** ekranına geçip
  **"Oyunları Tara"** düğmesine basın. Tarama:
  - Steam kök klasörünü kayıt defterinden (`HKCU\Software\Valve\Steam\SteamPath`,
    `HKLM\...\Valve\Steam\InstallPath`) ve bilinen yollardan bulur (`STEAM_ROOT` ortam
    değişkeniyle de geçersiz kılınabilir).
  - `steamapps\libraryfolders.vdf` ile ek kütüphaneleri okur (yeni iç içe biçim + eski düz biçim).
  - Her kütüphanedeki `appmanifest_*.acf` dosyalarından **AppID, oyun adı, kurulum klasörü ve boyut**
    bilgisini çıkarır.
  - Kurulum, klasörün varlığına değil **doğrulanmış Steam manifestine** dayanır.
  - Hiçbir oyun dosyası değiştirilmez, taşınmaz veya silinmez (yalnızca okuma).
- **Manuel oyun klasörü ekleme:** "Kurulu Oyunlar" ekranındadır. **"Klasör Seç"** ile oyunun
  kurulu olduğu klasörü seçin, isterseniz oyun adını düzeltip **"Klasörü Ekle"** deyin.
  - Kaynak `Manual` olarak yazılır; **mağaza (Steam/Epic/...) atanmaz** — kaynak doğrulanmamıştır.
  - Oyun adı boş bırakılırsa klasör adı kullanılır ve ad **doğrulanmamış** sayılır
    (listede "ad doğrulanmadı" etiketi görünür).
  - Aynı klasör ikinci kez eklenirse **mükerrer kayıt oluşmaz**; farklı dizinlerdeki kurulumlar
    ayrı kayıt olarak kalır. Mevcut katalog oyunuyla **ada göre otomatik birleştirme yapılmaz**.
  - Var olmayan/erişilemeyen klasör reddedilir; boş klasör uyarıyla kabul edilir.
  - Klasör sonradan silinirse kayıt **silinmez**, "kurulu değil" olarak işaretlenir.
- **Dil dosyası keşfi (salt okunur):** Sol menüden **"Dil Dosyası Keşfi"** ekranına geçin,
  kurulu oyunu seçin ve **"Dil Dosyalarını Tara"** düğmesine basın.
  - Seçilen kurulum klasörü **salt okunur** taranır: hiçbir dosya değiştirilmez, silinmez,
    yeniden adlandırılmaz (içerik ve yazma zamanı doğrulanır).
  - Alt klasörler keşfedilir; ilerleme gösterilir ve tarama **iptal edilebilir**.
  - Kök dizin dışına çıkılmaz; sembolik bağlantı döngüleri ve kök dışına işaret eden bağlar
    atlanır. Erişilemeyen klasörler taramayı durdurmaz, uyarı olarak raporlanır.
  - Aday formatlar: `JSON, XML, CSV, TSV, PO, POT, INI, YAML/YML, TXT, STRINGS, LOC`
    (düz metin) ve `MO` (ikili). Ayrıca `PAK, BUNDLE, ARCHIVE, ASSETS, UASSET, BANK`
    paketleri **metin olarak sınıflandırılmaz**.
  - Her aday için **uzantı, boyut, format, güven düzeyi, destek durumu ve aday olma gerekçesi**
    gösterilir. Uzantı eşleşmesi tek başına en fazla `Düşük` güven verir.
  - Tekrar tarama **mükerrer kayıt oluşturmaz** (kurulum + yol benzersizdir).
  - Silinmiş dosyaların kayıtları **silinmez**; yalnızca "artık bulunamadı" olarak işaretlenir
    ve bu işaretleme **yalnızca eksiksiz** (hatasız ve iptal edilmemiş) taramada yapılır.
- **Metin önizleme (salt okunur ayrıştırma):** "Dil Dosyası Keşfi" ekranında bir aday dosya seçip
  **"Metinleri Önizle"** düğmesine basın.
  - **Desteklenen ayrıştırıcılar:** `JSON`, `XML`, `CSV`, `TSV`, `PO`, `POT`, `INI`, `LocresCSV`
    (`key,source,Translation`). Diğer formatlar ve tüm paketler önizlenemez ("Bu format için ayrıştırıcı yok").
  - Dosya açılmadan **önce** yolun seçili kurulum dizini içinde olduğu doğrulanır; kök dışındaki
    yollar reddedilir.
  - Dosya **salt okunur** okunur; hiçbir şey yazılmaz, yeniden serileştirilmez.
  - Her kayıt için **yapısal konum** (JSON Pointer / XPath benzeri yol / satır–sütun), **orijinal
    metin**, **değişken/biçimlendirme işaretleri**, **durum** (çevrilebilir / belirsiz / atlandı) ve
    **kanıt** gösterilir.
  - JSON'da sayı/boolean/null çeviri adayı olarak sunulmaz; XML'de etiket ve attribute **adları**
    metin sayılmaz; CSV'de sayısal hücreler bağlama göre "atlandı" veya "belirsiz" işaretlenir.
  - Aynı metin iki farklı konumda geçse kayıtlar **birleştirilmez**.
  - XML ayrıştırmasında DTD kapalı ve harici çözümleyici yoktur (**XXE engellenir**).
  - **PO/POT:** `msgctxt`, çok satırlı dizeler ve kaçışlar çözülür; **her çoğul biçim ayrı kayıt**
    olarak gösterilir (birleştirilmez), `#, fuzzy` kayıtları "Belirsiz" işaretlenir, mevcut
    `msgstr` çevirisi ikinci satırda görünür. Başlık kaydı meta veri olarak "Atlandı" listelenir.
  - **INI:** `[Bölüm]` bağlamı ve dosya sırası korunur; ilk `=` ayırıcıdır; boş/sayısal/yol
    değerleri atlanır. Dosya teknik ayar dosyasına benziyorsa ayrıca **uyarı** gösterilir.
  - Bozuk sözdizimi, çözülemeyen kodlama ve aşırı büyük dosya (varsayılan 32 MB) **hata mesajıyla**
    bildirilir; uygulama çökmez.
  - Bu aşamada **düzenleme, çeviri ve dosyaya yazma yoktur**; ayrıştırma sırasında ilerleme/iptal
    sunulmaz (tek dosya işlemi olduğu arayüzde belirtilir).
- **Dil dosyası formatları**: Okuma (ayrıştırma) JSON/XML/CSV/TSV için var; **yazma yoktur**.
  Ayrıntılar: [SUPPORTED-FORMATS.md](SUPPORTED-FORMATS.md).

### Kaynak destek durumu

| Kaynak | Durum | Not |
|--------|-------|-----|
| Steam | ✅ Uygulandı | `appmanifest_*.acf` + `libraryfolders.vdf`, salt okunur |
| Manuel klasör | ✅ Uygulandı | Kullanıcı seçimi; mağaza doğrulanmaz |
| Epic Games | ⏳ Planlandı | `.item` manifestleri henüz okunmuyor |
| GOG | ⏳ Planlandı | Galaxy kayıtları henüz okunmuyor |
| Ubisoft Connect | ⏳ Planlandı | Yerel kurulum kayıtları henüz okunmuyor |
| EA App | ⏳ Planlandı | Yerel kurulum kayıtları henüz okunmuyor |
| Xbox / Microsoft Store | ⏳ Planlandı | MSIX paket metadata'sı henüz okunmuyor |

Planlanan kaynaklar yalnızca **bilgi olarak** listelenir (arayüzdeki "Kaynaklar ve destek durumu"
bölümü); destekleniyormuş gibi davranmaz ve tarama sonucuna kayıt eklemez.

### Tarama güvenilirliği (R1)

Bir sağlayıcının taraması üç durumdan biriyle döner: `Completed`, `Unavailable`, `Failed`;
ayrıca **kısmi** olabilir (bazı kütüphaneler/manifestler okunamadı).

- Yalnızca `Completed` tarama kayıt **ekler/günceller**.
- Yalnızca **eksiksiz** `Completed` tarama, o kaynağın önceki kayıtlarını "kurulu değil"
  işaretleyebilir.
- `Unavailable` (ör. Steam kurulu değil), `Failed` veya **kısmi** tarama mevcut kayıtları
  **korur**: hiçbir kayıt silinmez, "kurulu değil" yapılmaz. Arayüz bunu uyarı şeridinde bildirir.
- Boş ama başarılı bir tarama ile başarısız bir tarama **aynı şey değildir**.

## Geliştirme Aşamaları

Proje 9 aşamada geliştirilmektedir. Mevcut durum: **Aşama 1 tamamlandı, Aşama 2 kısmen tamamlandı.**

| Aşama | Açıklama | Durum |
|-------|----------|-------|
| 1 | Proje iskeleti, WPF arayüzü, SQLite ve dokümantasyon | ✅ Tamamlandı |
| 2 | Global Game Catalog, ilk katalog sağlayıcısı ve oyun arama ekranı | 🟡 Kısmen tamamlandı — Steam kataloğu senkronizasyonu ve arama çalışıyor; oyun profili / kaynak-doğrulama ekranı yok |
| 3 | Her oyun için ayrıntılı profil ve kaynak/doğrulama altyapısı | ⏳ Beklemede (yalnızca veri modeli hazır) |
| 4 | Steam, Epic ve Ubisoft kurulu oyun keşfi | 🟡 Kısmen tamamlandı — Steam taraması ve manuel klasör ekleme çalışıyor (salt okunur); Epic/Ubisoft/GOG/EA/Xbox yok |
| 5 | Dil dosyası tarama, format tanıma ve ilk editör | 🟡 Kısmen tamamlandı — keşif + JSON/XML/CSV/TSV okuma ve önizleme çalışıyor; editör, çeviri ve yazma yok |
| 6 | Yedekleme, güvenli dışa aktarma ve geri yükleme | ⏳ Beklemede (yalnızca `BackupRecord` tablosu var) |
| 7 | Eklenti sistemi ve motor özelindeki formatlar | ⏳ Beklemede (yalnızca arayüzler tanımlı) |
| 8 | AI ile Türkçe çeviri | ⏳ Beklemede |
| 9 | Diğer mağazalar, performans, testler ve Windows dağıtımı | ⏳ Beklemede |

## Bilinen Sınırlamalar

- **Steam senkronizasyonu tam katalog üzerinde çalışır.** `GetAppList/v2` yaklaşık 250.000
  kayıt döndürür; ilk senkronizasyon uzun sürebilir ve UI işlem sırasında yanıt vermeyebilir.
  Sonraki aşamada ilerleme raporlama ve iptal düğmesi eklenmelidir.
- Steam yalnızca AppID + ad verir; geliştirici/yayıncı/açıklama alanları boş kalır.
- Steam dışında katalog sağlayıcısı yoktur.
- Kurulu oyun tarama, format tanıma, editör, yedekleme ve AI çeviri ekranları henüz yoktur;
  navigasyon menüsündeki düğmelerin bir kısmı yer tutucudur.
- Katalog eşleştirmesi AppID yoksa oyun adına göre yapılır (aynı adlı farklı oyunlar
  birleşebilir).
- **Kurulu oyun taraması yalnızca Steam + manuel klasörü destekler.** Epic/GOG/Ubisoft/EA/Xbox
  için `IInstalledGameProvider` uygulaması yoktur; `GameSourceCatalog`'da `Planned` olarak
  işaretlidirler ve arayüzde "destekleniyor" gibi gösterilmez.
- Manuel eklenen kayıtların oyun adı kullanıcı tarafından verilir; **doğrulanmamış** kabul edilir.
  Bu kayıtlar için mağaza/platform bilgisi oluşturulmaz ve ada göre otomatik katalog eşleştirmesi
  yapılmaz (yanlış birleştirmeyi önlemek için bilinçli tercih).
- Bu aşamada **şema değişikliği yapılmadı** (yeni migration yok): doğrulanmamış kaynak bilgisi
  mevcut `InstallSource = "Manual"` alanıyla, doğrulanmamış ad da arayüzdeki etiketle temsil edilir.
- Steam manifesti oyun/araç ayrımı yapmaz; bu yüzden `Steamworks Common Redistributables`
  gibi dağıtım paketleri de listelenir.
- Aynı klasörü paylaşan iki farklı Steam uygulaması (ör. oyun + modlama kiti) iki ayrı
  kurulum kaydı olarak görünür — bu bilinçli bir davranıştır.
- Tarama dosya **içeriğini** analiz etmez; yalnızca uzantı, dosya/klasör adı ve boyut gibi
  **metadata** kullanılır. İçerik doğrulaması (magic bytes, header) ve ayrıştırma henüz yoktur.
- **Hiçbir format için yazma desteği yoktur.** Okuma yalnızca `JSON`, `XML`, `CSV`, `TSV`, `PO`,
  `POT`, `INI`, `LocresCSV` ile sınırlıdır; `YAML`, `TXT`, `STRINGS`, `LOC`, `MO` hâlâ yalnızca keşfedilir,
  ayrıştırılamaz. `ILocalizationFormatAdapter.SupportsWrite` daima `false`'tur.
- **Önizleme veritabanına yazmaz.** Ayrıştırılan metinler geçici (in-memory) modeldir;
  `LocalizationProject`/`LocalizationEntry` tabloları bu aşamada kullanılmaz.
- Önizleme tek dosya içindir: **ilerleme ve iptal yoktur**; büyük dosyalarda düğme birkaç saniye
  yanıt vermeyebilir.
- UTF-8/UTF-16 dışı kodlamalar (ör. Windows-1254) **reddedilir** (hata mesajı gösterilir); otomatik
  kodlama tahmini yoktur. `.json` uzantılı ikili dosyalar (ör. macOS `__MACOSX/._*`) bu sayede
  güvenle reddedilir.
- Ayrıştırıcılar içeriğin gerçekten yerelleştirme olduğunu **doğrulamaz**: örneğin bir oyunun
  yama/konfig JSON'ları da başarıyla ayrıştırılır. Uygunluk değerlendirmesi kullanıcıya aittir.
- CSV'de ayraç otomatik algılanır (` , ; \t |`); algılanamazsa uyarı verilir. Başlık satırı
  sezgisel tespit edilir; yanlış tespit olasılığı "başlık algılanmadı" uyarısıyla sınırlanır.
- Paketlerin (`.pak`, `.uasset`, `.bundle`, `.archive`, `.assets`, `.bank`) içine bakılmaz;
  yalnızca dosya/klasör adında ipucu varsa listeye alınır ve **desteklenmiyor** olarak işaretlenir.
- SHA-256 yalnızca adaylar için hesaplanır ve 64 MB'tan büyük dosyalarda atlanır
  (`Sha256` boş bırakılır, nedeni kayıtta yazılır).
- Sembolik bağlantı/junction **döngüleri** gerçek yol karşılaştırmasıyla engellenir; ancak test
  ortamında bağ oluşturmak yönetici izni gerektirdiği için bu senaryo yalnızca birim testleriyle
  (yol karşılaştırma kuralları) doğrulanmıştır.

## Dokümantasyon

- [ARCHITECTURE.md](ARCHITECTURE.md) - Mimari ve tasarım
- [ROADMAP.md](ROADMAP.md) - Geliştirme yol haritası
- [SUPPORTED-FORMATS.md](SUPPORTED-FORMATS.md) - Desteklenen dosya formatları
- [NTE-LOCALIZATION-RESEARCH.md](NTE-LOCALIZATION-RESEARCH.md) - NTE yerelleştirme araştırması ve doğrulama sınırları
- [CATALOG-SOURCES.md](CATALOG-SOURCES.md) - Katalog kaynakları
- [SECURITY.md](SECURITY.md) - Güvenlik politikası

## Lisans

MIT