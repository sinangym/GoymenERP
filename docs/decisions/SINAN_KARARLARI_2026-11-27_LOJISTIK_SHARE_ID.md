# Sinan Kararları — 27 Kasım 2026 (lojistik hareket payı kimliği)

Kaynak: Bu oturumdaki ShareId tekillik sorusuna Sinan'ın doğrudan cevabı.
Mevcut `T2/SMALL-GAPS` kapsamındaki açık soru cevaplanmıştır; yeni paket, rol/grant veya canlı aktivasyon kararı değildir.

## Karar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| LOJ-D21 | ShareId şirket genelinde tekillik | ShareId aynı şirket içerisinde yalnızca bir talimatın tek hareket payını tanımlar. Farklı talimatlarda yeniden kullanım engellenir; farklı şirketler bağımsız kalır. Aynı hareketin yükleme, sevk, teslim, faturalama ve maliyet aşamalarında kaynak kimliği korunur. İşlem tekrarlarında mükerrer kayıt oluşmaz. İptal edilen payın kimliği başka bir talimatta yeniden kullanılamaz. | Logistics hareket payı/SQL tekilliği, Inventory/Sales/maliyet kaynak bağlantıları ve replay |

## Geçiş ve kabul sınırları

- Migration öncesinde mevcut kayıtlar kontrol edilir. Çakışma varsa geçmiş kayıtlar kendiliğinden değiştirilmez; migration açık hata ile durur ve çakışma raporlanır.
- Mevcut testli altyapı kullanılır; SQL tekillik ve eşzamanlılık kontrolleri gerçek SQL kabulünde doğrulanır.
- Bu karar miktar, maliyet veya değerleme formülünü değiştirmez; yeni fiziksel/finansal hareket üretmez. Ayrı gerçekleşme aşamalarının mevcut kaynak bağlantıları korunur.
- Mevcut kayıtlar silinmez veya yeniden kimliklendirilmez. Şirket izolasyonu, idempotency, beklenen sürüm, audit ve outbox kuralları sürer.
- Diğer bağımsız paketler bu işin kabulünü beklemeden devam eder.
