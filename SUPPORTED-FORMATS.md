# Supported Formats - Game Language Explorer

## Aşama 3: Okuma (ayrıştırma) desteği

Bu aşamada **JSON, XML, CSV ve TSV** dosyaları salt okunur olarak ayrıştırılabilir.
**Hiçbir format için yazma desteği yoktur** (`SupportsWrite` daima `false`).

| Format | Uzantılar | Keşif | Okuma (önizleme) | Yazma | Notlar |
|--------|-----------|-------|------------------|-------|--------|
| JSON | .json | ✅ | ✅ | ❌ | JSON Pointer konumu; sayı/boolean/null aday değil |
| XML | .xml | ✅ | ✅ | ❌ | Element metni + metin attribute; DTD kapalı (XXE yok) |
| CSV | .csv | ✅ | ✅ | ❌ | Ayraç otomatik (` , ; \t |`), RFC 4180 tırnakları |
| TSV | .tsv | ✅ | ✅ | ❌ | CSV ile aynı motor, ayraç sabit sekme |
| PO | .po | ✅ | ✅ | ❌ | msgctxt/çoğul/fuzzy/mevcut çeviri |
| POT | .pot | ✅ | ✅ | ❌ | Şablon; boş msgstr normaldir |
| INI | .ini | ✅ | ✅ | ❌ | [Bölüm] + ilk `=`; bölüm bağlamı korunur |
| LocresCSV | .csv | ✅ | ✅ | ❌ | `key,source,Translation` başlık şeması; sütun sırası serbest, boş `Translation` = çeviri yok |
| YAML | .yaml, .yml | ✅ | ❌ | ❌ | Ayrıştırıcı yok |
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

## Ayrıştırma kuralları

| Konu | Davranış |
|------|----------|
| Konum | JSON Pointer (`/a/b/0`), XPath benzeri (`/root/item[2]/@name`), CSV `satır 3 / text` |
| Çevrilebilirlik | `Translatable`, `Ambiguous` (bağlam belirsiz), `Skipped` (neden yazılır) |
| Yapı korunumu | Dosya yalnızca okunur; yeniden serileştirme/yazma yok |
| Değişkenler | `{ad}`, `{0}`, `%s`, `%d`, `\n`, `<b>`, `[color=red]` tanınır ve imza üretilir |
| PO çoğul | `msgid_plural` + her `msgstr[n]` **ayrı kayıt** (`PluralIndex/PluralCount/PluralSourceText`); birleştirilmez |
| PO fuzzy | `#, fuzzy` → durum `Belirsiz` + `Flags=["fuzzy"]`; kayıt atlanmaz |
| PO başlık | `msgid ""` başlık kaydı `Atlandı (meta veri)` olarak görünür, içeriği korunur |
| PO obsolete | `#~` kayıtları `Atlandı` olarak görünür, silinmez |
| INI bağlam | `[Bölüm]` her kayda `Context` olarak yazılır; dosya sırası korunur |
| INI değerleri | Boş/sayısal/boolean/yol değerleri `Atlandı`; metinler `Çevrilebilir` |
| INI teknik dosya | Çevrilebilir oran düşükse ve yol/klasörde ipucu yoksa uyarı verilir |
| Kodlama | UTF-8 / UTF-16 (BOM algılama); diğerleri okunabilir hata ile reddedilir |
| Boyut | Varsayılan 32 MB üstü reddedilir |
| Güvenlik | XML'de DTD kapalı, harici çözümleyici yok (XXE engellenir) |

## Gelecek Aşamalar (Planlanmış)

| Format | Durum |
|--------|-------|
| PO/POT/MO okuma | ❌ Planlanmadı |
| INI/YAML/STRINGS/LOC okuma | ❌ Planlanmadı |
| Unreal `.locres` / `.uasset` ayrıştırma | ❌ Planlanmadı |
| Paket açma (.pak, .bundle, .archive) | ❌ Planlanmadı |
| **Yazma / geri yazma** | ❌ Bu aşamada yok |

