# Supported Formats - Game Language Explorer

## Okuma (ayrıştırma) ve yazma desteği

**JSON, XML, CSV/TSV, PO/POT, INI, YAML ve LocresCSV** dosyaları ayrıştırılabilir.
**JSON, CSV, PO ve INI için güvenli yazma altyapısı** vardır; diğer formatlarda yazma desteği yoktur.
JSON, CSV, PO ve INI adaptörlerinde `SupportsWrite=true`, diğer adaptörlerde `false` değerindedir.
Yazma sırasında kaynak kodlama/BOM korunur, kaynak SHA-256 sabitlenir ve yer tutucu/etiket doğrulaması tüm yazma yollarında zorunludur.

| Format | Uzantılar | Keşif | Okuma (önizleme) | Yazma | Notlar |
|--------|-----------|-------|------------------|-------|--------|
| JSON | .json | ✅ | ✅ | ✅* | JSON Pointer konumu; sayı/boolean/null aday değil. *Güvenli yazma altyapısı |
| XML | .xml | ✅ | ✅ | ❌ | Element metni + metin attribute; DTD kapalı (XXE yok) |
| CSV | .csv | ✅ | ✅ | ✅* | Ayraç otomatik (virgül, noktalı virgül, sekme, pipe), RFC 4180 tırnakları. *Yalnızca çeviri sütunu |
| TSV | .tsv | ✅ | ✅ | ❌ | CSV ile aynı motor, ayraç sabit sekme |
| PO | .po | ✅ | ✅ | ✅* | msgctxt/çoğul/fuzzy/mevcut çeviri. *Yalnızca msgstr |
| POT | .pot | ✅ | ✅ | ❌ | Şablon; boş msgstr normaldir |
| INI | .ini | ✅ | ✅ | ✅* | [Bölüm] + ilk `=`; bölüm bağlamı korunur. *Yalnızca değer |
| LocresCSV | .csv | ✅ | ✅ | ❌ | `key,source,Translation` başlık şeması; sütun sırası serbest, boş `Translation` = çeviri yok |
| YAML | .yaml, .yml | ✅ | ✅ | ❌ | Blok/flow eşleme ve listeler; `a.b`, `a[0].b`; `\|`/`>` blok metinleri; salt okunur |
| TXT | .txt | ✅ | ❌ | ❌ | Ayrıştırıcı yok |
| STRINGS | .strings | ✅ | ❌ | ❌ | Ayrıştırıcı yok |
| LOC | .loc | ✅ | ❌ | ❌ | Ayrıştırıcı yok |
| MO | .mo | ✅ (ikili) | ❌ | ❌ | Ayrıştırıcı yok |

### LocresCSV ile sıradan CSV ayrımı

`.csv` uzantısını iki ayrıştırıcı paylaşır. Seçim **dosyanın başlığına** bakılarak yapılır:

- Başlıkta `key`, `source` ve `Translation` sütunlarının **üçü birden** varsa (büyük/küçük harf
  duyarsız, çevresindeki boşluk/tırnak temizlenir) → **LocresCSV**.
- Aksi hâlde dosya sıradan **CSV** olarak okunur (önceki davranış korunur).
- Başlık okuma sınırlıdır (ilk 4 KB) ve salt okunurdur; dosyaya hiçbir şey yazılmaz.
- LocresCSV'de `key` **anahtardır**, kaynak metin değildir; `source` orijinal metin,
  `Translation` ise mevcut çeviridir (boşsa "çeviri yok").
- Yinelenen anahtarlar birleştirilmez/silinmez; korunur ve uyarı olarak bildirilir.
- UEExtractor dokümantasyonundaki "32 karakterlik anahtar" kuralı **engelleyici değildir**:
  uymayan satırlar atılmaz, yalnızca `key-format` işareti + uyarı üretilir.

> **NTE doğrulama durumu:** LocresCSV adapter'ı sentetik testlerle doğrulanmıştır; gerçek NTE
> LocresCSV çıktısı elde edilmediği için gerçek NTE verisiyle doğrulanmamıştır. Araştırma
> kanıtları ve ön koşul engeli: [NTE-LOCALIZATION-RESEARCH.md](NTE-LOCALIZATION-RESEARCH.md).

### Paket / arşiv formatları (asla düz metin sayılmaz, ayrıştırılmaz)

| Format | Uzantı | Keşif | Okuma |
|--------|--------|-------|-------|
| PAK | .pak | ✅ ipucu varsa listelenir | ❌ Desteklenmiyor |
| BUNDLE | .bundle | ✅ ipucu varsa | ❌ Desteklenmiyor |
| ARCHIVE | .archive | ✅ ipucu varsa | ❌ Desteklenmiyor |
| ASSETS | .assets | ✅ ipucu varsa | ❌ Desteklenmiyor |
| UASSET | .uasset | ✅ ipucu varsa | ❌ Desteklenmiyor |
| BANK | .bank | ✅ ipucu varsa | ❌ Desteklenmiyor |

> Dosya/klasör adında yerelleştirme ipucu **olmayan** paketler listeye alınmaz; yalnızca sayılır.

## Güven düzeyleri (keşif)

| Güven | Nasıl oluşur |
|-------|--------------|
| `Low` (Düşük) | Yalnızca uzantı eşleşmesi; bağlam yok |
| `Medium` (Orta) | Dosya adı **veya** klasör yolunda ipucu |
| `High` (Yüksek) | Dosya adı **ve** klasör yolunda ipucu |

Aranan ipuçları: `localization`, `localisation`, `localized`, `localised`, `language(s)`,
`locale(s)`, `translation(s)`, `stringtable(s)`, `text`, `texts`, `string`, `strings`,
`i18n`, `l10n`. `text`/`string` yalnızca **tam** eşleşir (`Texture`, `Context` yanlış pozitif vermez).

### JSON içerik kontrolü

JSON adaylarında uzantı ve dosya/klasör ipuçlarına ek olarak dosyanın başındaki sınırlı
içerik **salt okunur** incelenir (varsayılan üst sınır 256 KB). Bu inceleme `FormatEvidence`
tarafında `Content` türüyle raporlanır ve yalnızca güven düzeyini ayarlar; dosya hiçbir zaman
değiştirilmez veya yeniden serileştirilmez.

- Geçerli JSON ve yeterli sayıda çevrilebilir metin değeri → güven bir seviye yükselir
  (ör. `Low` → `Medium`); yol ipuçları zaten yüksekse `High` korunur.
- Sayı/boolean/null ağırlıklı veya çevrilebilir metin içermeyen JSON → güven `Low`.
- Geçersiz, boş veya okunamayan JSON → güven `Low`; tarama durmaz, dosya aday olarak kalır.
- Belirsiz içerik (tek tük metin, teknik dizeler) → güven en fazla `Medium`.
- 256 KB üstündeki JSON → içerik okunmaz, güven en fazla `Medium`; yalnızca yol/uzantı
  sinyalleri kullanılır.
- Yol, URL, GUID ve teknik anahtar benzeri dizeler çevrilebilir metin sayılmaz.

## Güvenli yazma (JSON/CSV/PO/INI)

JSON, CSV, PO ve INI için yazma akışı şu sırayla çalışır:

1. Kaynak kodlama/BOM belirlenir; güvenilir değilse yazma reddedilir.
2. Orijinal dosyanın SHA-256 değeri, DB kaydı ve önizleme sürümüyle karşılaştırılır.
3. Her çeviri için yer tutucu/etiket yapısı doğrulanır; eksik/fazla/değişmiş işaret varsa dosya değiştirilmez.
4. Dosya yedeklenir ve `BackupRecord` tablosuna kaydedilir.
5. Yeni içerik, kaynak kodlama/BOM ile aynı klasörde geçici dosyaya yazılır.
6. Beklenen SHA-256 ve boyut gerçek baytlar üzerinden hesaplanır; geçici dosya doğrulanır.
7. Doğrulama başarılıysa dosya atomik olarak orijinalin üzerine taşınır.
8. Replace sonrası son doğrulama, DB güncellemesi ve gerekli kurtarma kullanıcı iptalinden bağımsız tamamlanır.
9. Hata olursa yedekten geri yükleme denenir; geri yükleme başarısızsa yedek korunur ve sonuç açıkça bildirilir.

JSON'da yalnızca mevcut string değerleri JSON Pointer ile değiştirilir. CSV'de yalnızca
başlıkta tanınan çeviri sütunu (`translation`, `target`, `translated` vb.) güncellenir;
diğer sütunlar, satır sırası ve ayraç yapısı korunur. PO'da yalnızca `msgstr` /
`msgstr[n]` satırları yamanır; yorumlar, `msgid`, `msgctxt`, boş satırlar ve kayıt sırası
korunur. Değişen PO kaydının `fuzzy` işareti kaldırılır. INI'de yalnızca `key=value`
satırının değer tarafı yamanır; bölümler, yorumlar, boş satırlar ve satır sırası korunur.
Sayı/boolean/null değerlerine, bilinmeyen yollara veya önizlemeden sonra değişmiş kaynak
metinlere yazılmaz.
AI Çeviri ekranındaki “Dosyaya Yaz” komutu yalnızca kullanıcı onayından sonra çalışır;
diğer formatlarda yazma komutu pasiftir.

## Ayrıştırma kuralları

| Konu | Davranış |
|------|----------|
| Konum | JSON Pointer (`/a/b/0`), XPath benzeri (`/root/item[2]/@name`), YAML (`a.b` / `a[0].b`), CSV `satır 3 / text` |
| Çevrilebilirlik | `Translatable`, `Ambiguous` (bağlam belirsiz), `Skipped` (neden yazılır) |
| Yapı korunumu | Okuma salt okunur; JSON/CSV/PO/INI güvenli yazma geçici dosya + atomik taşıma kullanır |
| Değişkenler | `{ad}`, `{0}`, `%s`, `%d`, `\n`, `<b>`, `[color=red]` tanınır ve imza üretilir |
| PO çoğul | `msgid_plural` + her `msgstr[n]` **ayrı kayıt** (`PluralIndex/PluralCount/PluralSourceText`); birleştirilmez |
| PO fuzzy | `#, fuzzy` → durum `Belirsiz` + `Flags=["fuzzy"]`; kayıt atlanmaz |
| PO başlık | `msgid ""` başlık kaydı `Atlandı (meta veri)` olarak görünür, içeriği korunur |
| PO obsolete | `#~` kayıtları `Atlandı` olarak görünür, silinmez |
| INI bağlam | `[Bölüm]` her kayda `Context` olarak yazılır; dosya sırası korunur |
| INI değerleri | Boş/sayısal/boolean/yol değerleri `Atlandı`; metinler `Çevrilebilir` |
| INI teknik dosya | Çevrilebilir oran düşükse ve yol/klasörde ipucu yoksa uyarı verilir |
| YAML kapsamı | Blok eşleme/liste, tırnaklı skaler, yorum, `\|`/`>` blok metni ve basit flow `[]`/`{}` desteklenir; çapa/alias, etiket ve karmaşık anahtarlar desteklenmez |
| Kodlama | UTF-8, UTF-8 BOM, UTF-16 LE/BE BOM ve UTF-32 BOM okunur; yazmada kaynak kodlama/BOM korunur. Güvenilir belirlenemeyen kodlamalar sessiz dönüştürülmeden reddedilir |
| Boyut | Varsayılan 32 MB üstü reddedilir |
| Güvenlik | XML'de DTD kapalı, harici çözümleyici yok (XXE engellenir) |

## Gelecek Aşamalar (Planlanmış)

| Format | Durum |
|--------|-------|
| MO okuma | ❌ Planlanmadı |
| STRINGS/LOC okuma | ❌ Planlanmadı |
| Unreal `.locres` / `.uasset` ayrıştırma | ❌ Planlanmadı |
| Paket açma (.pak, .bundle, .archive) | ❌ Planlanmadı |
| **Yazma / geri yazma** | ✅ JSON, CSV, PO ve INI güvenli yazma altyapısı; diğer formatlar ❌ |

