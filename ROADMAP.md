# Roadmap - Game Language Explorer

## Aşama 1: Proje Iskeleti, WPF Arayüzü, SQLite ve Dokümantasyon (TAMAMLANDI)

### Tamamlananlar
- [x] .NET 10 solution ve 4 proje oluşturuldu (Core, Data, WPF, Tests)
- [x] NuGet paketleri yapılandırıldı (EF Core SQLite, CommunityToolkit.Mvvm, DI, Logging)
- [x] 14 veritabanı varlığı tanımlandı (Game, GamePlatform, CatalogSource, vb.)
- [x] EF Core migration (InitialCreate) oluşturuldu
- [x] WPF koyu tema arayüzü oluşturuldu (Shell, CatalogView)
- [x] Temel navigasyon (ShellViewModel, Katalog navigasyonu)
- [x] Dependency Injection konteynerı yapılandırıldı
- [x] 4 xUnit birim testi yazıldı ve geçti
- [x] Dokümantasyon dosyaları oluşturuldu (README, ARCHITECTURE, ROADMAP, vb.)

### Test Sonuçları
- `dotnet build`: ✅ Başarılı (tüm projeler)
- `dotnet test`: ✅ Başarılı (4/4 test)

## Aşama 2: Global Game Catalog, İlk Katalog Sağlayıcısı ve Oyun Arama Ekranı

### Planlananlar
- [ ] IGameCatalogProvider arayüzünün Steam uygulaması
- [ ] Steam Web API entegrasyonu (api.steampowered.com)
- [ ] Katalog senkronizasyonu (periyodik güncelleme)
- [ ] Oyun arama, filtreleme, sıralama
- [ ] Yerel SQLite veritabanında katalog saklama
- [ ] Değişiklik bazlı güncelleme (sadece değişen kayıtlar)

## Aşama 3: Her Oyun İçin Ayrıntılı Profil ve Kaynak/Doğrulama Altyapısı

### Planlananlar
- [ ] GameProfile detaylı görünüm
- [ ] Kaynak ve doğrulama durumu takibi (Verified / Predicted / Unknown)
- [ ] Kapak görseli, açıklama, geliştirici, yayıcı bilgileri
- [ ] Desteklenen diller (arayüz, ses, altyazı)
- [ ] Bilinen dil dosyası uzantıları ve konumları

## Aşama 4: Steam, Epic ve Ubisoft Kurulu Oyun Keşfi

### Planlananlar
- [ ] IInstalledGameProvider arayüzü
- [ ] Steam kütüphane taraması (libraryfolders.vdf, appmanifest_*.acf)
- [ ] Epic Games kurulum kayıtları
- [ ] Ubisoft Connect kurulum kayıtları
- [ ] Elle oyun klasörü ekleme
- [ ] Asenkron ve iptal edilebilir tarama

## Aşama 5: Dil Dosyası Tarama, Format Tanıma ve İlk Editör

### Planlananlar
- [ ] XML, JSON, CSV, TSV, TXT, INI, YAML, PO format adaptörleri
- [ ] Dosya yapısına göre format doğrulama (uzantı sadece ipucu)
- [ ] SHA-256 hesaplama
- [ ] Metin kayıtları keşfi
- [ ] Yan yana kaynak/hedef editör

## Aşama 6: Yedekleme, Güvenli Dışa Aktarım ve Geri Yükleme

### Planlananlar
- [ ] Orijinal dosya yedekleme (değişiklik öncesi)
- [ ] SHA-256 bütünlük doğrulama
- [ ] Geçici dosyaya yaz ve doğrula modeli
- [ ] Geri yükleme ve değişiklik geçmişi
- [ ] Ayrı çıktı klasörüne dışa aktarma (varsayılan)

## Aşama 7: Eklenti Sistemi ve Motor Özelindeki Formatlar

### Planlananlar
- [ ] ILocalizationFormatAdapter ve IGameFormatPlugin sözleşmeleri
- [ ] Unity, Unreal Engine adaptörleri
- [ ] Arşiv biçimi adaptörleri
- [ ] Eklenti kaydı ve sürüm uyumluluğu

## Aşama 8: AI ile Türkçe Çeviri

### Planlananlar
- [ ] AI sağlayıcı arayüzü (OpenAI uyumlu yerel uç nokta)
- [ ] Değişken, etiket ve biçimlendirme kodunu koruma
- [ ] Çeviri belleği ve terim sözlüğü
- [ ] Yerel LLM bağlantısı (isteğe bağlı)

## Aşama 9: Diğer Mağazalar, Performans, Testler ve Windows Dağıtımı

### Planlananlar
- [ ] GOG, EA app, Xbox katalog sağlayıcıları
- [ ] Performans optimizasyonu
- [ ] Kapsamlı testler
- [ ] Windows MSI/EXE dağıtım paketi