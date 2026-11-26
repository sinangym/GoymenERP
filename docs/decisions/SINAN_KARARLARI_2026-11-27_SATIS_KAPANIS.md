# Sinan Kararları — 27 Kasım 2026 (normal satış kapanışı)

Kaynak: Sinan'ın bu oturumdaki iki satış kapanışı sorusuna doğrudan cevabı.
Bu kayıt yeni rol/grant, otomatik kapanış veya canlı aktivasyon kararı değildir.
SAT-D32 ve SAT-D221'in bütün eksenlerin tamamlanması ve kullanıcının kapatma
işlemini ayrıca yapması kuralları korunur.

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D478 | Kaynak ekseninin tamamlanması | Gerçekleşen sevklerin kaynak bağlantıları doğrulanmış, siparişe ait açık tedarik, üretim ve tahsis yükümlülükleri tamamlanmış veya usulüne uygun kapatılmış olmalıdır. Kaynak planı oluşturulması tek başına yeterli değildir. Ortak satınalma ve üretim emirlerinde yalnız ilgili siparişin kaynak payı esas alınır; başka siparişlerin paylarının veya ortak belgenin tamamının kapanması beklenmez. | Sales normal kapanış, Purchasing/Manufacturing/Inventory/Logistics sahip okuma portları |
| SAT-D479 | Finansal eksenin tamamlanması | İlgili kesinleşmiş faturaların bütün açık kalemlerinin geçerli mahsuplarla kapanması yeterlidir. Banka mutabakatı Finance içinde bağımsız izlenir ve satış kapanışının ek koşulu değildir. Henüz gerçekleşmemiş ödeme veya tahsilat mevcut finans kurallarına aykırı biçimde kapanmış sayılmaz. | Sales normal kapanış, Finance açık kalem ve mahsup sahibi okuma portu |

## Ortak sınırlar

- Ticari tamamlanma, fiziksel teslim, faturalama ve finansal kapanış ayrı görünür.
- Bütün mevcut eksenlerin tamamlanması şartı korunur; zorla kapatma normal
  kapanışın yerine geçirilmez ve otomatik kapatma eklenmez.
- Eksik, okunamayan, başka şirkete ait veya çelişen sahip kanıtı tamamlanmış
  sayılmaz. Banka mutabakatını ayırmak geçersiz mahsup veya beklenen tahsilatı
  gerçekleşmiş kabul etmek değildir.
- Mevcut yetki, görev ayrılığı, beklenen sürüm, idempotency, denetim ve şirket
  izolasyonu kuralları korunur; yeni bir yetki ataması yapılmaz.
