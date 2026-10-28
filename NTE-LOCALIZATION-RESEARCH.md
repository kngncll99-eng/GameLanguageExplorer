# NTE Yerelleştirme Araştırması (Aşama 6)

## Kapsam ve sonuç

Bu doküman, **Neverness to Everness (NTE)** yerelleştirme metinlerine erişim için yapılan
araştırmanın kanıtlarını ve durma nedenini kaydeder. Çalışma araştırma ve salt okunur
incelemeyle sınırlıdır; UEExtractor uygulamaya entegre edilmemiştir.

Araştırılan mevcut yöntemle, bu makinede **güvenli ve tamamlanabilir bir NTE metin çıkarma
yolu doğrulanamadı**. Bu ifade gelecekte hiçbir yöntemin mümkün olmayacağı anlamına gelmez;
yalnızca aşağıdaki ön koşullar ve güvenlik sınırları altında doğrulama yapılamadığını belirtir.

## Resmî kaynak ve sürüm

- Araştırılan araç: [SolicenTEAM/UEExtractor](https://github.com/SolicenTEAM/UEExtractor).
- İncelenen sürüm: **1.0.8.7** (`v.1.0.8.7`); resmî sürüm varlığı `UEExtractor.zip` olarak
  doğrulandı. Araç MIT lisansıyla yayımlanıyor.
- [Resmî README](https://github.com/SolicenTEAM/UEExtractor/blob/main/README.md), NTE için
  özel paket dizini davranışı ve **şifreli `.locres` dosyaları** desteğini belirtir.
- Aynı README, ZenLoader/IoStore oyunları için `.usmap` ve arşivleri açmak için AES anahtarı
  ön koşullarını açıklar. Bu dosyaların veya anahtarın temini bu çalışmanın kapsamı dışındadır.

## Yerel kanıtlar

Önceki çalışma günlükleri `%TEMP%\ueextractor-audit` altında incelendi. Geçici klasör olduğu
için bu dosyalar kalıcı proje varlığı değildir.

Günlüklerde görülen başlıca satırlar:

```text
Provider initialized. Found 6967 virtual files.
RequiredKeys (encryption GUIDs needed): 1
Encrypted=True
Path filter active: "HT/Content/Localization"
Found 0 .locres file(s) to process
```

- `HT/Content/Localization` hedef yolunda **üç ayrı koşuda** sıfır `.locres` bulundu.
- Kurulum yolu hâlâ geçerliydi; `Paks` ve `PatchPaks` klasörleri mevcuttu.
- Kurulumda `aes.txt`, `.usmap` ve PAK dışı gevşek `.locres` dosyası bulunamadı.
- PAK dışındaki metin benzeri dosyaların incelemesinde yerelleştirme metni yerine teknik
  yapılandırma, CDN/sunucu bilgileri ve benzeri içerikler görüldü; doğrudan okunabilir bir
  yerelleştirme kaynağı doğrulanamadı.
- **Gerçek LocresCSV elde edilmedi.** Ön koşullar sağlanmadığı için yeni bir çıkarma denemesi
  yapılmadı; aynı başarısız işlem tekrarlanmadı.

## Mevcut LocresCSV desteğinin doğrulama sınırı

Projedeki LocresCSV salt okunur adapter'ı `key,source,Translation` başlık şemasını destekler.
Bu destek **sentetik test verileri ve sentetik önizleme testleriyle** doğrulanmıştır.
Gerçek NTE LocresCSV çıktısı bulunmadığından adapter **gerçek NTE verisiyle doğrulanmış
değildir**; sentetik test sonuçları gerçek oyun metni çıkarıldığı şeklinde yorumlanmamalıdır.

Format ayrıntıları için [SUPPORTED-FORMATS.md](SUPPORTED-FORMATS.md) dosyasına bakın.

## Güvenlik ve bütünlük

Araştırma sırasında aşağıdaki sınırlar korundu:

- AES anahtarı aranmadı, türetilmedi veya kullanılmadı; `.usmap` temin edilmedi.
- Oyun paketleri değiştirilmedi, yeniden adlandırılmadı, silinmedi veya yamalanmadı.
- Oyun dizinine çıktı yazılmadı; anti-cheat veya bütünlük kontrolleri atlatılmadı.
- Kullanıcı veritabanı, migration'lar ve uygulama kaynak kodu değiştirilmedi.
- UEExtractor uygulamaya entegre edilmedi; kalıcı içe aktarma veya çeviri yapılmadı.

Aşama 6'daki salt okunur ölçümde oyun dizini **1508 dosya** ve **106091115606 bayt**
(yaklaşık **98,81 GB**) olarak önceki `game-before.txt` kaydıyla aynıydı. Bu ölçüm, araştırma
kapsamında oyun dosyalarının değiştirilmediğine dair yerel kanıttır.

## Çözülemeyen engel

NTE paketlerinde `Encrypted=True` ve `RequiredKeys: 1` görüldüğünden, ayrıca gerekli
`.usmap`/AES ön koşulları bu makinede bulunmadığından, resmî araçla hedef localization yolunda
okunabilir `.locres` kaydı elde edilemedi. Bu engel aşılmadan gerçek NTE LocresCSV doğrulaması
yapılamaz. Yeni bir değerlendirme, ancak yetkili ve güvenlik sınırlarına uygun bir veri/ön koşul
kaynağı sağlanırsa ayrıca ele alınmalıdır.
