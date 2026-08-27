# Yetki Modeli

RBAC rol şablonları, ABAC veri kapsamı ve sınırlı kişi istisnaları birlikte kullanılır.

## Başlangıç rol şablonları

- Admin / Sistem Yöneticisi
- Genel Yönetici
- Finans
- Muhasebe
- Satış
- Satın Alma ve Satış Operasyonu
- Stok/Depo
- Üretim Planlama ve Sevkiyat
- Üretim Operatörü
- Sevkiyat/Lojistik/Kantar
- Temel Kalite
- Denetçi/Okuma

## Yetki boyutları

- Şirket, tesis, depo/lokasyon
- Modül, sayfa, işlem
- Alan ve hassas veri
- Kayıt sahibi/durum
- Tutar, para birimi ve marj eşiği
- Taslak, onaya gönderme, onay/red
- Dışa aktarma/paylaşım
- Geçerlilik başlangıç/bitiş

Varsayılan `deny` ve asgari yetki uygulanır. Buton gizleme güvenlik değildir; backend aynı yetkiyi tekrar kontrol eder.
