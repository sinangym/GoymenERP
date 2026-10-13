# Sinan Kararları — Transfer Belge ve Stok Yaşam Döngüsü — 2026-10-14

## Kaynak ve kapsam

Bu kayıt, Sinan Göymen'in 14 Ekim 2026 tarihli açık kullanıcı kararını kalıcılaştırır.
Transfer sahipliği ayrımını değiştirmez; transfer belge ve stok yaşam döngüsünün aşağıdaki
altı kararını ve kısmi kabul değişmezini kilitler.

Mevcut kararlarla uyumu:

- `DEC-STK-008`: transfer iki bacaklı çıkış/yolda/hedef kabul akışıdır; mülkiyet
  değişmez, konum ve sorumluluk değişir.
- Logistics, transfer çıkış belgesinin iş sahibidir.
- Inventory; kaynak stok düşümünün, hedef kabulünün, lot/etiket bağlarının ve iki bacaklı
  stok hareketlerinin sahibidir.
- Organization; depo, dış lokasyon ve işletici ilişkisinin sahibidir.
- Documents; `DEC-DOC-001` ve `DEC-DOC-003` uyarınca belge saklama ve sürümleme
  altyapısının sahibidir. `DEC-DOC-008` uyarınca kesin ticari kayıt silinmez; düzeltme ve
  ters kayıt saklama/imha politikasından ayrıdır.

## Kilitli transfer kararları

1. Logistics transfer çıkış belgesi ile Inventory hedef kabul belgesi ayrı kimlikli ve
   sürümlü belge türleridir.
2. Yalnız kesinleşmiş/etkin belge dispatch veya kabul işlemini açabilir. Taslak belge stok
   hareketi üretmez.
3. İlk dilimde bir transfer bir çıkış belgesine bağlıdır. Kısmi kabul desteklenecek şekilde
   tasarlanacaktır.
4. e-İrsaliye kimliği ilk dilimde opsiyonel belge bağlantısıdır. Mevzuatın gerektirdiği
   düzenleme ve provider aktivasyonu ayrıca ele alınır.
5. Kesin belgeler silinmez; düzeltme veya ters kayıt zinciri kullanılır.
6. Kaynak stok hareketi, kesin Logistics belgesini doğrulayan idempotent Inventory
   dispatch komutunda oluşur. Hedef stok hareketi Inventory kabulünde oluşur.

## Kısmi kabul değişmezi

Veri modeli, 25 tonluk bir transferin 15 tonunun kabul edildiği ve 10 tonunun hâlâ
beklediği durumu temsil edebilmelidir.

Sevk edilen, yolda olan, kabul edilen ve fark/uyuşmazlık miktarları birbirine
karıştırılmaz; ayrı gerçekler olarak temsil edilir ve sunulur.

Etiket/lot kimliği ile mevcut bölme ve soy ağacı kararları değişmeden korunur.

## Sınırlar

- Bu kayıt yeni permission, role/grant veya provider aktivasyonu oluşturmaz.
- Documents transfer iş politikasının, Organization stok hareketinin sahibi olmaz.
- Modüller birbirinin tablolarına doğrudan yazmaz; owner sözleşmeleri üzerinden bağlanır.
- Deployment veya production aktivasyonu yetkisi vermez.

## Durum

- Altı transfer yaşam döngüsü kararı: **KİLİTLİ — son kullanıcı kararı**.
- Kısmi kabul ve ayrı miktar değişmezi: **KİLİTLİ — son kullanıcı kararı**.
- Uygulama, gerçek SQL/browser kabulü ve pilot/UAT: **ayrı teslim kapılarıdır**.
