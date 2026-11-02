# Roadmap - Game Language Explorer

Bu dosya, uygulanmış özellikler ile açık işleri ayırır. Tamamlanmış maddeler kodda ve testlerde doğrulanmıştır; gerçek oyun dosyası/LLM gerektiren entegrasyon testi ayrı ve varsayılan olarak skipped çalışır.

## Tamamlananlar

### 1. Proje iskeleti ve masaüstü uygulama
- [x] .NET 10 solution: Core, Data, WPF, Tests
- [x] EF Core SQLite, CommunityToolkit.Mvvm, DI ve logging
- [x] WPF Shell, koyu/açık tema ve MVVM navigasyon
- [x] Katalog, Kurulu Oyunlar, Dil Keşfi, AI Çeviri, Editör, Çeviri Belleği ve Yedekler ekranları

### 2. Katalog ve kurulu oyun keşfi
- [x] Steam katalog arama/senkronizasyon temeli
- [x] Steam, Epic, GOG, Ubisoft, EA App ve Xbox için salt okunur kurulu oyun keşfi
- [x] Manuel oyun klasörü ekleme
- [x] Kurulum kayıtlarının SQLite'ta tutulması

### 3. Dil dosyası keşfi ve önizleme
- [x] JSON, XML, CSV, TSV, PO, POT, INI, YAML ve LocresCSV okuma/önizleme
- [x] Format, güven düzeyi ve kanıt raporlama
- [x] SHA-256 ve boyut sınırları
- [x] YAML'de blok/flow yapılar, `a.b` ve `a[0].b` yolları (salt okunur)

### 4. Güvenli yazma
- [x] JSON, CSV, PO ve INI için yedek → geçici dosya → SHA-256 → atomik taşıma → geri yükleme
- [x] Kaynak kodlama/BOM koruma (UTF-8, UTF-8 BOM, UTF-16 LE/BE BOM)
- [x] Kaynak sürüm/hash kontrolü; önizlemeden sonra değişen dosyayı reddetme
- [x] Yazma sınırında yer tutucu/etiket doğrulaması
- [x] Replace sonrası iptalden bağımsız doğrulama/DB/kurtarma
- [x] Metinsel kök + ara junction/symlink çözümlü yol güvenliği
- [x] Yedek listeleme, onaylı geri yükleme ve güvenli silme

### 5. Editör ve AI çeviri
- [x] Yan yana kaynak/hedef editörü; değişiklik takibi başlangıç çevirisine göre
- [x] OpenAI uyumlu yerel LLM istemcisi
- [x] Parti/retry, kalite kontrolü, çalışma günlüğü ve iptal
- [x] Seçim değişikliğinde geç gelen önizleme/çeviri/progress sonuçlarını yalnızca doğru dosyaya uygulama
- [x] JSON/CSV/PO dışa aktarma

### 6. Çeviri belleği
- [x] Tam eşleşmeli yeniden kullanım
- [x] Oyun kapsamı + bağlam + çoğul biçim kimliği
- [x] Eski kayıtları silmeden `legacy:<id>` kapsamına taşıyan migration
- [x] Benzersiz indeks ve eşzamanlı kayıt koruması
- [x] Listeleme, arama ve onaylı silme ekranı

### 7. Test ve CI
- [x] xUnit birim/entegrasyon testleri
- [x] Gerçek LLM/oyun dosyası testi ortam değişkenleriyle açık skip
- [x] Windows CI: .NET 10 restore, Release build, test ve TRX artifact
- [x] `scripts/validate-project.ps1` ile salt okunur doğrulama raporu

## Açık İşler

- [ ] Unreal `.locres`/`.uasset` ve şifreli `.pak` içerikleri
- [ ] `MO`, `TXT`, `STRINGS`, `LOC` ayrıştırıcıları
- [ ] `.locres`/PAK için gerçek NTE doğrulaması
- [ ] `GlossaryTerm` tablosunun çeviri akışına tam bağlanması
- [ ] Sağlayıcı profili, güvenli API anahtarı saklama ve yönetim ekranı
- [ ] Editörde arama/filtreleme, toplu düzenleme ve değişiklik geçmişi
- [ ] XLIFF/XML dışa aktarma ve şablonlar
- [ ] `IGameFormatPlugin` yükleme/sürüm/güvenlik altyapısı
- [ ] Windows installer, sürümleme ve kod imzalama
- [ ] Core/Data testlerini WPF test projesinden ayırma (mevcut keşfi bozmadan değerlendirilecek)
