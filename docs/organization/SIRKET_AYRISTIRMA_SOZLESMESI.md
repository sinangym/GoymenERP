# Şirket Ayrıştırma Sözleşmesi

## Amaç

Bir hukuki şirket ileride bütün geçmişiyle yeni sunucuya veya yeni veritabanına ayrılabilmelidir.

## Zorunlu tasarım kuralları

1. Her kritik transaction kaydında `CompanyId` zorunludur.
2. Paylaşılan masterlarda şirket kullanım/hesap ilişkisi ayrı tablodur.
3. Bir başka şirketin stoğu yalnız açık grup içi satış/transfer/emanet olayıyla kullanılabilir.
4. Numara serisi, cari hesap, banka, finans, muhasebe, stok, sipariş, üretim, belge ve audit şirket kapsamındadır.
5. Dosya blob’u fiziksel olarak ortak tutulsa bile belge erişim ve şirket bağlantıları ayrıdır.
6. Şirket dışa aktarımında ortak masterların yalnız kullanılan alt kümesi ve bütün referans bağı taşınır.
7. Dışa aktarılan veri paketi checksum, kaynak kimlik, batch ve manifest taşır.
8. Ayrıştırma testi yedek/restore kabul setine eklenir.

## Önerilen ilk fiziksel model

Tek SQL Server veritabanı + katı CompanyId izolasyonu + şirket veri paketi dışa aktarma/geri yükleme. Şirket başına ayrı veritabanı, gerçek ayrıştırma ihtiyacı doğduğunda geçiş seçeneğidir.
