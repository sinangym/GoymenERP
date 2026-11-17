# W8 FIN-S13 dosya kaynağı — PARTIAL

Dayanak: ALS-D7/D44, mevcut FIN-S13; biçimler için kullanıcı cevabının
[ajan aktarımı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6945631103).

- XML/UBL, açık sütun eşlemeli CSV/XLSX kaynak okuyucuları tamamlandı. Dosya ve
  açılmış arşiv boyutu, satır/hücre sayısı, XML derinliği, formül/dış bağlantı,
  yinelenen alan, kimlikte kaybolan baştaki sıfır ve hassasiyet kaybı denetlenir.
- Gerçek Documents sahibi: yetkili şirkette idempotent kimlik ayırma → dosya yükleme
  → temiz değişmez sürüm/hash üzerinden önizleme. Önizleme para etkisi üretmez.
- EF üretimi: `AddPurchaseInvoiceXmlContentKind` (51995),
  `AddPurchaseInvoiceImportReservations` (51996). Down veri varken reddeder.
- API, Purchasing.UnitTests, Documents.UnitTests, Api.IntegrationTests derlemeleri:
  exit 0, 0 uyarı/hata. İlk API derlemesindeki referans türü kısıtı düzeltildi.
  Documents projesinin eksik yerel restore çıktısı standart restore ile üretildi.
- Birim/HTTP/model/gerçek SQL test kaynakları yazıldı; çalışma zamanı testleri,
  SQL, Vitest/browser ve CI **NOT_RUN**. Pin ve ilişkisel/grup üretimi entegrasyonda.
- Kalan: kullanıcı ekranı ve sütun seçimi; gerçek cari/ürün/birim/vergi/ödeme planı
  eşlemesi; mevcut fatura kayıt komutuna bağlama. Kaynakta olmayan resmî TL satır,
  taksit tutarı veya başlık masraf/yuvarlama dağılımı tahmin edilmez.
- 174 paydası ve DONE sayısı değişmedi; bu kayıt tamamlanmış FIN-S13 iddiası değildir.
