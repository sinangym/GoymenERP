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
- Kullanıcı ekranı: gerçek Documents listesi/temiz sürüm seçimi, XML doğrudan okuma,
  CSV/XLSX açık sütun eşleştirmesi, belge üzerindeki tüm satır/toplam/vergi/kur/ödeme
  bilgilerinin ondalık metinlerini koruyan önizlemesi yazıldı. React ekranında para
  hesabı yapılmaz. Bilinmeyen kimlik ayırma sonucu aynı anahtarla doğrulanır;
  bağlam/sürüm değişince eski önizleme kapanır. Yetkisiz/bulunamadı/çakışma/503,
  çevrimdışı ve bozuk yanıt durumları ayrıdır. Ekran mevcut import capability'sine
  bağlıdır; bu kontrolün merkez owner bildirimi ve tam workflow yayını tamamlanmadı.
- Frontend `npx tsc -b`: exit 0. Strict decoder, transport ve DOM test kaynakları
  yazıldı; Vitest/browser çalıştırılmadı. Mevcut Documents owner sözleşmesine
  `purchasing.invoice-import` eklendi; yeni izin/grant yoktur.
- Kayıtlı dosya işlemlerini tekrar açma: yetkili şirketin gerçek rezervasyonları
  25 kayıtlık sayfalardan seçilir; teknik kimlik girişi veya yeni bir yazma işlemi
  gerekmez. API no-store, şirket/fresh yetki ve taşma kontrollü sayfa sorgusu;
  decoder şirket, sayfa, kimlik tekilliği ve sürümü doğrular. Gerçek SQL sayfalama/
  şirket yalıtımı, HTTP ve geç bağlam yanıtı test kaynakları eklendi.
- Bu takipte API, Purchasing.UnitTests, Api.IntegrationTests ve frontend `tsc -b`
  derlemeleri exit 0, hata 0. Yeni migration yok; runtime testler **NOT_RUN**.
- Kalan: gerçek cari/ürün/birim/vergi/ödeme planı
  eşlemesi; mevcut fatura kayıt komutuna bağlama. Kaynakta olmayan resmî TL satır,
  taksit tutarı veya başlık masraf/yuvarlama dağılımı tahmin edilmez.
- 174 paydası ve DONE sayısı değişmedi; bu kayıt tamamlanmış FIN-S13 iddiası değildir.
