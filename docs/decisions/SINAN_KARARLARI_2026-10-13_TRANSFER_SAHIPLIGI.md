# Sinan Kararları — Transfer Sahipliği — 2026-10-13

## Kaynak ve kapsam

Bu kayıt, Sinan Göymen'in 13 Ekim 2026 tarihli açık kullanıcı kararını kalıcılaştırır.
Yalnız modüller arası transfer sahipliği sınırını kilitler; aşağıda `NEEDS-SINAN`
olarak tutulan işletme ayrıntılarını karara bağlamaz ve kaynak uygulaması için tek başına
`START` yetkisi vermez.

Mevcut kararlarla uyumu:

- `DEC-STK-008`: dış fason ve transfer iki bacaklı çıkış/yolda/hedef kabul akışıdır;
  mülkiyet değişmez, konum ve sorumluluk değişir.
- `DEC-ORG-001` ve `DEC-ORG-004`: depo/lokasyon organizasyon modelindedir; harici
  depoda işleten taraf ile mülkiyet ayrı tutulur.
- `DEC-DOC-001` ve `DEC-DOC-003`: belge saklama; blob, mantıksal belge, sürüm ve kayıt
  bağlantısı katmanlarıyla yürür; mevcut belge üzerine yazılmaz.
- `DEC-DOC-008`: ticari düzeltme ile belge saklama/imha politikası birbirinden ayrıdır.

## Kilitli sahiplik ayrımı

1. **Logistics**, kaynak depodan fiziksel çıkışın ve transfer çıkış belgesinin iş
   sahibidir.
2. **Inventory**, kaynak stok düşümünün, hedef kabulünün, lot/etiket bağlarının ve iki
   bacaklı stok hareketlerinin sahibidir.
3. **Organization**, depo, dış lokasyon ve işletici ilişkisinin sahibidir.
4. **Documents**, belge saklama ve sürümleme altyapısının sahibidir.

Bu ayrım modüllerin birbirinin tablolarına doğrudan yazmasına izin vermez. Documents
transfer iş politikasının, Organization stok hareketinin, Inventory transfer çıkış
belgesi yaşam döngüsünün sahibi hâline gelmez.

## NEEDS-SINAN — açık işletme kararları

Aşağıdaki konularda bu kayıt bir varsayılan veya öneri seçmez:

- Transfer için kanonik belge türü veya türleri.
- Transfer belge ve işlem yaşam döngüsü / durum makinesi.
- Transfer, transfer satırı ve belge arasındaki cardinality.
- Transfer belgesinin e-irsaliye ile eşlemesi.
- İptal, düzeltme ve ters kayıt davranışı.
- Kaynak stok düşümü ile hedef stok artışının hangi kesin olay veya durumda
  kesinleştiği.

Bu altı konu açık kaldığı sürece bunlara bağlı yeni policy, permission/grant veya
`INV-R20` uygulama başlangıcı bu kayıt üzerinden türetilemez.

## Durum

- Sahiplik ayrımı: **KİLİTLİ — son kullanıcı kararı**.
- Yukarıdaki altı işletme ayrıntısı: **NEEDS-SINAN**.
- Deployment, production aktivasyonu veya veri değişikliği yetkisi: **YOK**.
