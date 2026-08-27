# AI Veri ve Güvenlik Politikası

## Sınıflar

- Genel: onaylı bulut kullanılabilir
- Şirket içi: minimum veri, onaylı kurumsal ortam
- Gizli: fiyat, marj, cari, sözleşme — yerel model tercih
- Çok gizli: IBAN, kimlik, bordro, erişim anahtarı — yerel, buluta gönderilmez

## Değişmez kurallar

- AI doğrudan DB’ye bağlanmaz.
- Yetkisiz satır modele gönderilmez.
- Görev için gereken minimum veri kullanılır.
- Her çağrıda veri kapsamı, model/prompt/tool sürümü ve sonuç auditlenir.
- Bulut fallback veri sınıfı izin vermiyorsa otomatik çalışmaz.
- Kullanıcı davranışı kendi kendine kalıcı iş kuralı oluşturmaz.
