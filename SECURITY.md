# Security - Game Language Explorer

## Güvenlik İlkeleri

### Dosya İşlemleri

1. **Yedekleme Zorunluluğu** - Herhangi bir dosya değişikliğinden önce orijinal dosyanın yedeği alınmalıdır.
2. **SHA-256 Bütünlük Doğrulaması** - Orijinal ve değiştirilen dosyalar SHA-256 ile doğrulanmalıdır.
3. **Geçici Dosya Modeli** - Değişiklikler önce geçici dosyaya yazılır, doğrulandıktan sonra orijinal yerleştirilir.
4. **Başarısız İşlemlerde Orijinali Koruma** - Herhangi bir hata durumunda orijinal dosya bozulmamalıdır.
5. **Geri Yükleme** - Yedeklemeler üzerinden geri yükleme imkanı sağlanmalıdır.
6. **Değişiklik Geçmişi** - Tüm dosya değişiklikleri kaydedilmelidir.

### AI Çeviri

1. **Kullanıcı Onayı** - Kullanıcının açık onayı olmadan oyun dosyaları harici AI servislerine gönderilmemelidir.
2. **Değişken Koruma** - `{playerName}`, `{0}`, `%s` gibi değişkenler, HTML/XML etiketleri ve biçimlendirme kodları çeviri sırasında korunmalıdır.
3. **Bozuk Çeviri Reddi** - Otomatik olarak bozuk çeviriler kabul edilmemelidir, kullanıcı onayı gerekli.
4. **Yerel LLM** - İsteğe bağlı yerel LLM bağlantısı desteklenmelidir, varsayılan olarak harici servis bağımlılığı olmamalıdır.

### Katalog Verileri

1. **Gerçek Veriler** - Erişilemeyen katalog verileri uydurulamaz.
2. **Doğrulama Durumu** - Her bilginin kaynağı ve doğrulama durumu (Verified/Predicted/Unknown) belirtilmelidir.
3. **Tahmin Ayırt Ediciliği** - Kesin bilgi olmayan alanlar tahmin olarak işaretlenmeli, gerçek olarak gösterilmemelidir.

### Eklenti Güvenliği

1. **Bilinmeyen Eklentiler** - Otomatik olarak çalıştırılmamalıdır.
2. **Yazma Desteği** - Sadece doğrulanmış adaptörlerde dosya yazma desteği etkinleştirilmelidir.
3. **Sürüm Uyumluluğu** - Eklentiler destekledikleri sürümleri bildirmelidir.

## Veritabanı Güvenliği

- SQLite veritabanı yerel olarak saklanır
- Hassas veriler (kullanıcı kimlik bilgileri) düz metin olarak saklanmamalıdır
- Veritabanı dosyası erişim kısıtlamaları ile korunmalıdır