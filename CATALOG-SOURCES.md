# Catalog Sources - Game Language Explorer

## Mevcut Durum

Steam sağlayıcısı uygulanmıştır ve uygulama içinden çalıştırılabilir. Diğer sağlayıcılar
henüz uygulanmamıştır.

| Platform | API Mevcut | Sağlayıcı | Durum | Notlar |
|----------|-----------|-----------|-------|--------|
| Steam (arama + içe aktarma) | ✅ Evet | `SteamCatalogProvider.SearchGamesAsync` | ✅ Uygulandı ve gerçek API ile doğrulandı | `storesearch` + `appdetails`; **API anahtarı gerekmez** |
| Steam (tam katalog) | ⚠️ Anahtar gerekir | `SteamCatalogProvider.FetchGamesAsync` | ✅ Kodlandı, ⚠️ anahtarsız doğrulanamadı | `IStoreService/GetAppList/v1`; `STEAM_API_KEY` gerekir |
| Epic Games Store | ✅ Evet | EpicCatalogProvider | ❌ Planlanıyor | Store API |
| Ubisoft Connect | ❌ Sınırlı | UbisoftCatalogProvider | ❌ Planlanıyor | Alternatif kaynak gerekli |
| GOG | ✅ Evet | GOGCatalogProvider | ❌ Planlanıyor | GOG API |
| EA App | ❌ Bilinmiyor | EACatalogProvider | ❌ Planlanıyor | Araştırma gerekiyor |
| Xbox | ✅ Evet | XboxCatalogProvider | ❌ Planlanıyor | Xbox API |

### ⚠️ Önemli: `ISteamApps/GetAppList` kaldırıldı

Valve, anahtarsız `ISteamApps/GetAppList` metodunu **API yüzeyinden kaldırdı**.
`v1`, `v2`, `v0001`, `v0002` ve `?format=json` varyantlarının tamamı **HTTP 404**
döndürür:

```
Method 'GetAppList' not found in interface 'ISteamApps'
```

Bu nedenle eski tam katalog senkronizasyonu hiçbir kayıt üretmiyordu (ve hata mesajı
UI'da görünmediği için sebep anlaşılmıyordu). Güncel davranış:

- Aynı host üzerindeki diğer uç noktalar çalışıyor (`ISteamWebAPIUtil/GetServerInfo/v1` → 200).
- `IStoreService/GetAppList/v1` **403** döndürür: `Please verify your key= parameter`.
- `store.steampowered.com/api/storesearch` ve `/api/appdetails` anahtarsız **200** döndürür.

### Steam sağlayıcısının gerçek sınırları

- **Arama yolu (anahtarsız):** `storesearch` yalnızca ada göre arama yapar; tüm kataloğu
  listeleyemez. Dönen ilk 10 sonuç `appdetails` ile zenginleştirilir.
- **Tam katalog yolu:** ~250.000 kayıt; `max_results`/`last_appid` ile sayfalanır.
  Yazma işlemleri 1.000'lik gruplar hâlinde toplu yapılır.
- AppID, `Game.ExternalId` (kalıcı değil, `[NotMapped]`) üzerinden taşınır ve
  `GamePlatform.PlatformGameId` alanına yazılır. `StoreUrl` bu AppID'den üretilir
  (`https://store.steampowered.com/app/{appid}`).
- Tekrarlanan senkronizasyon/içe aktarma **duplike kayıt oluşturmaz**: eşleştirme önce
  AppID, sonra oyun adı üzerinden yapılır.
- Çıkış tarihleri istek diline göre gelir (ör. `10 Eki 2007`); `tr-TR` ve sabit kültürle
  ayrıştırılır.

## Tasarım İlkeleri

1. Her platform için ayrı `IGameCatalogProvider` uygulaması
2. API bulunmayan platformlarda alternatif kaynaklar değerlendirilir
3. Erişilemeyen veriler uydurulmamalıdır
4. Tüm sağlayıcılar bağımsız test edilebilir modül olarak tasarlanmalıdır
5. Katalog verileri yerel SQLite veritabanında saklanır
6. Güncellemelerde sadece değişen kayıtlar işlenir

## Kısıtlamalar

- Kullanıcının oyun hesabına sahip olması katalogda yer almak için zorunlu değildir
- Resmi API olmayan platformlar için erişilemeyen veriler tahminle doldurulamaz
- Tüm bilginin kaynağı ve doğrulama durumu belirtilmelidir