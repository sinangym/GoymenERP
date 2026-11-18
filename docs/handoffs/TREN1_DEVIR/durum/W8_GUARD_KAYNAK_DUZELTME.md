# W8 mimari guard kaynak düzeltmesi

Durum: SOURCE_DONE; derleme ve çalışma zamanı doğrulaması entegrasyonda bekliyor.
Kapsam mevcut W8 kapanışıdır; yeni dilim veya dalga açılmadı. Kaynak tabanı
`3e85cbbb1bbee5efc5ec4dbe9afcfa6669677034`; GOV-D9 ve mimari şirket/işlem değişmezleri korunur.
Bu çalışma yazara teknik destektir; bağımsız exact-head inceleme veya K3 onayı değildir.

## Değişen kaynaklar

- Inventory alt yüklenici ayırma bırakma ve Sales resmi iptal endpointleri, alış tedarik
  bildirim tüketicisi, Inventory ilk giriş kur kanıtı, Manufacturing maliyet nesli görünümü,
  Product sınıflandırma tarihçesi ve Accounting SMM iptal kanıtı JSON alanlarını açık tipli
  `TryGetProperty` üzerinden okur. Zorunlu alan yoksa `KeyNotFoundException`, JSON nesnesi
  yanlış türdeyse mevcut `InvalidOperationException` davranışı sürer. Önceki alan sayısı,
  yinelenen/bilinmeyen alan, tür ve kapsam kontrolleri kaldırılmadı; opsiyonel alanlar değişmedi.
- İki Inventory maliyet okuyucusunda etiket sürümü `Property(row => row.Version).OriginalValue`
  üzerinden alınır. Değiştirilmiş güncel sürüm yerine EF'nin yüklediği özgün sürümle fence
  yapılması korunur; şirket sorgusu, transaction ve maliyet hesabı değişmedi.
- Finance Treasury işlem işaretçisi açık kilit içinde `TryGetValue` ve `Add` ile tek kez
  kaydedilir. İç içe/yarışan deneme mevcut işaretçiyi değiştiremez; yalnız kabul edilen
  denemenin eski cleanup akışı işaretçiyi kaldırır. Kilit içinde async veya DB işlemi yoktur.
- Product alış birim dönüştürücüsü, aynı yedi kayıtlı bağımlılıkla açık constructor üzerinden
  kurulur. Mevcut çalışma zamanı ve dört önkoşul kapısı ile unavailable yanıtları korunur.
- Purchasing Finance aktarım uyarısında bilinen hata sınıfları açık tipli kategorilere
  ayrılır; bilinmeyen alt tür yalnız `Exception` olarak yazılır. Warning mesaj şablonu,
  şirket/fatura/faz alanları ve dışarıdaki unavailable davranışı korunur. Bilinmeyen alt
  türün tam adı artık yazılmaz; bu dar tanılama ayrıntısı kaybı bilinçlidir. Hata mesajı,
  stack, yeni hassas veri veya reflection tabanlı kaçış eklenmedi.

## Yazılan testler

- `PurchaseIncomingInvoiceDiagnosticsTests`: bilinen kategori ve bilinmeyen güvenli
  fallback, aynı Warning alanları, exception nesnesi/mesajı/alt tür adı sızmaması.
- `FinanceDocumentNumberingTests`: iç içe deneme reddedilirken dış rollback işaretçisinin
  korunması ve işlem tamamlandıktan sonra aynı context'in yeni denemede kullanılabilmesi.
- Mevcut JSON, DI ve gerçek SQL testleri korunur; test silme veya skip yoktur.

## Kanıt ve sınır

- `git diff --check`: exit 0.
- Dar kaynak taraması: görevdeki 12 dosyada yasaklanan dört reflection API adı kalmadı.
- .NET derleme/test: NOT_RUN; küresel ağır işlem hakkı entegrasyon sahibindedir.
- Node, gerçek SQL, HTTP, tarayıcı, CI ve bağımsız review: NOT_RUN.
- Guard/allowlist, WorkDbContext, migration/model, HTTP sözleşmesi, grant/rol/Critical,
  production ve Finance STOP değiştirilmedi. Entegrasyon sahibi değişen backend
  fingerprint/recovery pinlerini gerçek üreticilerle yeniden üretmelidir.

## Komut adı tanımlayıcısı düzeltmesi

`InventoryTransferReceiptDocuments.CreateCommand` SQL API çağrısı değildir; mevcut
`inventory.transfer.receipt-document.create.v1` iş komutu adının sabitidir. Sabit ve aynı
kaynak dosyadaki dört kullanım `CreateDocumentCommand` olarak açıkça adlandırıldı.
Kaynak/test ağaçlarında sınıf üzerinden dış referans bulunmadı. Komut metni, çağrı sırası,
idempotency, audit ve HTTP sözleşmesi değişmedi; SQL allowlist izni eklenmedi.
`git diff --check` exit 0; .NET/Node/runtime/SQL NOT_RUN, entegrasyon doğrulaması bekliyor.
