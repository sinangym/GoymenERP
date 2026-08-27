# Göymen ERP Finans AI ve Kullanıcı Odaklı Çalışma Alanı v4 — Doğrulama Raporu

Doğrulama tarihi: 24.08.2026

## Sonuç

Çalışma alanının statik HTML prototipi; ekran geçişleri, işlem workspaceleri, bağlamsal AI, görünüm modları ve mobil yerleşim açısından otomatik tarayıcı testlerinden geçirilmiştir.

| Kontrol | Sonuç |
|---|---:|
| Ana finans ekranları | 13 / 13 başarılı |
| İşlem workspaceleri | 73 / 73 başarılı |
| Finans menü grupları | 5 / 5 başarılı |
| Her sayfada AI dikkat noktası | 13 / 13 başarılı |
| Her sayfada bağlamsal AI aksiyonu | 13 / 13 başarılı |
| Her workspace'te tek AI işlem kontrolü | 73 / 73 başarılı |
| AI kaynak/kanıt satırları | Başarılı |
| AI güven ve kullanıcı onayı sınırı | Başarılı |
| Sade / Ayrıntılı görünüm | Başarılı |
| Benim İşlerim / Yönetim Özeti | Başarılı |
| Mobil yatay taşma | Yok |
| JavaScript çalışma hatası | 0 |
| Tarayıcı konsol hatası | 0 |
| Yinelenen statik HTML kimliği | 0 |

## Ekran doğrulaması

Aşağıdaki ekranların tamamı açılmış, doğru sayfa başlığı üretilmiş ve her ekranda tam olarak bir bağlamsal AI dikkat alanı ile iki ekran-özel AI işlemi ve bir “AI’a Sor” erişimi bulunduğu doğrulanmıştır:

1. Finans Genel Bakış
2. Tahsilat / Ödeme
3. Açık Kalemler ve Finansal Dağıtım
4. Tahsilat Takibi ve Cari Risk
5. Banka / Kasa
6. Ödeme Yönetimi
7. Ödeme Araçları
8. Nakit Planı
9. Krediler ve Limitler
10. Teminatlar ve Yükümlülükler
11. Nakit Bütçesi
12. Finans Kontrol Merkezi
13. Finans Raporları

## AI doğrulaması

Bağlamsal AI çekmecesinde aşağıdaki unsurlar test edilmiştir:

- Bulunulan sayfanın amacı ve dikkat noktası
- Sayfaya özgü iki önerilen AI işlemi
- Önerinin hangi veri kaynaklarına dayandığını gösteren üç kaynak satırı
- Güven oranı ve önerinin gerekçesi
- Kaynak ekrana veya ilgili işlem workspace'ine geçiş
- “AI mali sonucu kullanıcı onayı olmadan kesinleştirmez” kontrol sınırı

Her işlem workspace'inde yalnızca bir adet standart **AI İşlem Kontrolü** yer aldığı; zorunlu alan, ilişki, mükerrerlik, finansal etki ve kullanıcı kararı kontrollerinin gösterildiği doğrulanmıştır.

## Kullanıcı kolaylığı doğrulaması

- İlk açılışta **Sade** görünüm kullanılır.
- Genel Bakış ilk açılışta **Benim İşlerim** görev görünümünü gösterir.
- Yönetim Özeti tek seçimle açılır.
- Ayrıntılı görünüm veri modelini değiştirmeden ek tablo ve alanları açar.
- Finans menüsü beş görev grubunda düzenlenmiştir.
- Ana sayfalarda tek birincil işlem öne çıkarılmıştır.
- Yoğun işlemler tam ekran workspace'e alınmıştır.
- Sayfa kılavuzu ve klavye kısayolları erişilebilirdir.
- Mobil 390 px görünümünde sayfa yatay taşma üretmemiştir.

## Sınırlar

Bu çalışma alanı etkileşimli UI/UX prototipidir. Aşağıdaki canlı servisler bağlı değildir:

- Gerçek veritabanı ve API
- Banka API'leri ve canlı ekstre dosyaları
- Kimlik doğrulama ve gerçek rol/yetki servisi
- Kalıcı kayıt ve onay motoru
- Bildirim/e-posta servisleri

Göymen ERP–Mikro mutabakatı ile resmî muhasebe ve vergi beyannameleri bilinçli olarak kapsam dışındadır.

Ham test sonuçları `validation_results.json` dosyasındadır.
