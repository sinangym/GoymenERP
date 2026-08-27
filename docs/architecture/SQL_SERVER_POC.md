# SQL Server PoC Planı

## Amaç

Mevcut lisans ve sunucu avantajını doğrulamak; geri dönülmez seçimden önce kritik yükleri test etmek.

## Testler

1. Türkçe kesin ve tam metin arama
2. CompanyId izolasyonu ve şirket veri paketi
3. Aynı stokta paralel tahsis yarışı
4. Büyük audit/stock event tabloları ve keyset pagination
5. Outbox/inbox tekrar işleme
6. Nokta-zaman veya eşdeğer restore prosedürü
7. Büyük belge metadata ve hash sorguları
8. Üç yıllık tahminin 3 katı sentetik veri
9. Büyük XLSX/PDF dışa aktarım kuyruğu
10. Yedek, restore ve bütünlük checksum

## Kabul

Sık arama p95 ≤ 300 ms, karmaşık arama/listeler p95 ≤ 1 sn hedefi; temel yazma p95 ≤ 1 sn; sıfır aşırı tahsis; başarılı restore. Kesin eşikler gerçek envanter sonrası onaylanır.
