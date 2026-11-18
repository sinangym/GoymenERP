# W8 recovery / release kapanışı

Kaynak tabanı `3e85cbbb1bbee5efc5ec4dbe9afcfa6669677034`.
Bu çalışma yalnız recovery generator/schema/test pinlerini hizalar; mimari guard,
ilişkisel envanter, backend kaynakları ve K3 onay durumunu değiştirmez.

## İlk pin checkpoint'i

- Gerçek üretici actuals çıktısı: risky source 1456,
  `daf57a94adb4b8a4f015181621f31e7ae141ac32da461441140d52ae8ddd4301`.
- Migration 339,
  `4ed13eaa1ea5498d053a4d9cfecdcbe8e447bcf7ffaf7893e42ab51815d739ab`.
- Audit live source 42,
  `4616565dfe715d5cdd27d0675af49f0cbf110158cac9d143151ec51378f87b2a`.
- DbContext 19 ve declaration fingerprint değişmedi. Aynı çıktıda drift gösteren
  16 mevcut operative method ve 31 mevcut live-blob hash'i güncellendi; dictionary
  anahtarları, writer kapsamları, required token veya durable-store sınıfları değişmedi.
- Schema ve iki Python test paketindeki migration sayıları 258→339 hizalandı.
  Durable store'un kayıtlı 56 değeri gerçek üreticinin discovery kontrolünden önce
  yeni bir gerçek toplam olarak sunulmaz; eksik sınıflandırma veya kaynak önkoşulu
  çıkarsa ayrıca raporlanacaktır.
- İlk pin diff kontrolü exit 0. Temiz committed HEAD üzerinde gerçek generator ve
  iki ilgili Python suite sonucu aşağıdaki doğrulama devamında kaydedilecektir.

Yeni bypass/SQL/context izni, skip, grant/rol, provider veya production aktivasyonu
yoktur. Mevcut K3 pending açıklamaları korunur; pin yenilemek review veya gerçek SQL
kabulü değildir. .NET/Node, SQL, browser, Vitest, CI veya DB apply çalıştırılmaz.

## İlk gerçek üretici sonucu ve dar Finance outbox sınıflandırması

- Temiz `9701a7dd0` üzerinde gerçek generator native **exit 2**:
  `Durable outbox contract surface drifted.` İlk stdout/stderr ve exit kaydı korunur.
  İlgili Python suite'leri bu önkoşul bilinirken çalıştırılmadı; test atlanmadı.
- Keşifte beklenen altı `*OutboxV1.cs` dosyasına karşı yedi gerçek dosya vardır.
  Tek eksik kayıt mevcut `FinanceFinancialMovementOutboxV1.cs` owner yazıcısıdır.
  FIN-S11/FIN-S14 kaynağı, ayrı mali olay veya yeni provider değildir.
- `FinanceDbContext.Credits.cs` gerçek `finance.OutboxMessages` tablosunu composite
  CompanyId/Id anahtarı, server-company filtresi, rowversion cursor ve şirket/olay/
  kaynak/sürüm unique indeksiyle eşler. Gerçek DDL
  `20261117005509_AddCreditActualMovements` içindedir. İki gerçek append metodu
  `FinanceFinancialMovementOutboxV1.Add` ve `FinanceCreditMovementServiceV1.AddEvent`
  exact blob/method hash'leriyle bağlanmıştır; yeni inventory kaydı **K3 pending** kalır.
- Expense/transfer/book-value ve credit komutları gerçek owner source ve sürümü doğrular;
  özgün command replay çıkışı writer'dan öncedir ve outbox aynı owner save/transaction'a
  eklenir. Sürümlü payload CompanyId/EventId/source kimliklerini taşır. Gerçek Work
  source kaydı `TrainCompletionHostComposition` içindedir; Finance reader şirketli
  committed rowversion aralığını okur. Accounting kabulü envelope/source kimliği ve
  payload hash'iyle tekrar teslimi doğrular; inventory bir dispatcher aktivasyonu değildir.
- Bu somut physical outbox eklenince kayıtlı store sayısı 57 olur. Diğer henüz
  keşfedilmemiş W8 tabloları bu sayıdan çıkarımla sınıflandırılmaz. Yeni required
  evidence ve registry girdisi dışında validator semantiği gevşetilmedi. Yeni evidence
  yolunun risky-surface kapsamına etkisi temiz commit'te gerçek yardımcıyla ölçülecektir.

Temiz `d154f7fea` üzerinde gerçek actuals helper native **exit 0** verdi. Yeni Finance
contract evidence dosyaları risky kümeye katıldığı için gerçek count **1458**, fingerprint
`50213dd8efedb28fa55eac6425dcce8e3cd2c384e09ee28731ed5773038a1b6f` oldu. Diğer
declaration/Audit/method/live-blob pinlerinde drift yoktur. Bu değer sonraki temiz
producer denemesi için hizalandı; helper sonucu durable discovery'nin geçtiği anlamına gelmez.

## Gerçek key discovery devamı

- Temiz `8ce76bd90` gerçek generator native **exit 2**:
  `Durable idempotency-key table surface drifted.` İlk hata kaydı korunur.
- Aynı gerçek discovery fonksiyonu 42 kayıtlı key tablosuna karşı 49 gerçek tablo
  buldu. Yedi ek tablo da gerçek PK veya unique indekste `IdempotencyKeyHash` taşır;
  hiçbiri metadata-only dışlamasına alınmadı. İncelenen source zinciri:

| Fiziksel tablo | Gerçek operative writer / replay kanıtı |
|---|---|
| documents.DocumentBrandingAttempts | DocumentBrandingService.ExecuteAsync; ayrı terminal kaydı da aynı full-blob pinindedir. Company/actor/request hash ve özgün ResultJson replay'i. |
| logistics.TransferDepartureAttempts | LogisticsTransferDepartureOwner.OwnedAsync; şirket/transfer/actor/hash, özgün cevap ve aynı Serializable owner transaction. |
| organization.CompanyOpeningExecutions | OrganizationCompanyOpeningV1.OpenAsync; açık group-operation authority, hedef company+actor+request hash ve immutable ResponseJson. Global key genel company bypass'ı değildir. |
| product.PaintMembershipAttempts | ProductPaintMembershipService.ExecuteAsync; actor/company/request hash, özgün başarı/ret cevabı ve Audit ile aynı işlem. |
| purchasing.IncomingInvoiceAttempts | PurchasingStore.AddInvoiceAttempt; FindIncomingInvoiceAttemptAsync actor/company/request hash ile özgün snapshot/refusal'ı çözer. |
| purchasing.InvoiceImportReservations | PurchasingStore.ReserveAsync; gerçek unique company/key, özgün actor ve ayrılmış kimlik replay'i. Payload'sız kimlik ayırma olması metadata-only olduğu anlamına gelmez. |
| sales.SubcontractInvoiceCommandAttempts | SalesStore.UpdateLifecycleCoreAsync; özgün lifecycle sequence üzerinden exact cevap, actor/company/hash ve immutable terminal ret kaydı. |

Her kayıt gerçek owner mapping, migration, operative write token ve exact method/blob
hash'iyle bağlanır; validator kuralları değişmedi. Kayıtlı durable-store sayısı bu dar
devamda 64'tür; tam discovery geçmeden nihai gerçek toplam diye sunulmaz.

Organization'ın mevcut CompanySetupOutboxMessages kaydı için W7'de bırakılan
“producer yok” notu artık kaynağa uymuyordu: OpenAsync aynı işlemde gerçek enqueue
yapar. Mevcut spec'e bu writer ve exact hash bağı eklendi; eski gap testi gerçek
writer'ın zorunlu olduğu kaynak assertion'ına hizalandı. Bu source doğrulaması
production veya dispatcher aktivasyonu değildir; K3 pending durumu korunur.

## Message discovery ve açık source-shape önkoşulları

- Temiz `c3d1bac1a` gerçek generator native **exit 2**:
  `Durable message/delivery source surface drifted.` Gerçek keşif yalnız iki ek
  kayıt gösterdi: `sales.OfficialInvoiceCancellationDeliveries` ve `work.NotificationInbox`.
- WorkNotificationStoreV1.DeliverAsync, gerçek recipient owner okuması ardından aynı
  Serializable işlemde NotificationInbox ve kişisel bildirimleri kaydeder. PK
  CompanyId/SourceName/EventId'dir; olay tipi/sürümü ve PayloadSha256 tekrar teslimde
  değişemez. Gerçek Notifications model/DDL ve live writer/hash bağıyla inbox olarak
  envantere alındı; bildirim retention politikası veya dispatcher değiştirilmedi.
- Sales iptal teslim geçmişi business miktar kaydı değildir: gerçek Finance teslim
  cevabının immutable ViewJson/Sequence geçmişidir. CompanyId/CancellationId/Sequence
  key'i, önceki delivery version, command replay, aynı işlem audit ve fresh fence
  korunarak delivery olarak sınıflandırıldı. Kaynak `RecordDeliveryAsync` callback
  içinde yazdığı için mevcut generator deferred-callable koruması bunu doğru biçimde
  operative anchor saymıyordu. Koruma gevşetilmedi.
- Koordinatörün dar ek yetkisiyle `1e78622ad` ayrı source commit'i, aynı transaction
  wrapper/argüman/await/fence/sıra/sonuçları koruyup yalnız lambda gövdesini gerçek
  `RecordDeliveryCoreAsync` metoduna çıkardı. Envanter bu gerçek core'u bağlar.
- Aynı kaynak kontrolünde iki Purchasing lexical önkoşulu bulundu. `4c77e73cd`
  ayrı source commit'i AddInvoiceAttempt'ın aynı expression body’sini block body yaptı;
  ReserveAsync'in nested Prior okuyucusunu aynı captured parametreleri açık alan private
  metoda taşıdı. Key hesaplama, ilk replay, write, concurrency recovery ve son cevap
  sırası değişmedi. Mevcut generator token/fingerprint fonksiyonları iki dönüşümü
  dosya yazılmadan önce doğruladı. Yeni behavior, SQL, model veya migration yoktur.
- Yeni iki message kaydıyla kayıtlı durable-store sayısı 66'dır. Üç runtime dosyasının
  kaynak derlemesi **QUEUED**; .NET/Node lease alınmadı ve süreç başlatılmadı. Generator
  validator ve metadata-only/non-message istisna kuralları genişletilmedi. Yeni source
  şekliyle exact hash ve son risky surface yeniden ölçülecek; tam suite ancak gerçek
  üretici geçtiğinde çalıştırılacaktır.

Temiz `ee3f7bdde` üzerinde 47 yeni/etkilenen evidence ve operative token/fingerprint
kontrolü geçti. Gerçek actuals helper native **exit 0**: risky count 1458 kaldı,
üç source refactor sonrası fingerprint
`1dda5bbe2bda2284b176dab393241b5598226551f36741a5941e21b8b60d0b98` oldu. DbContext19,
migration339, Audit42 ve diğer method/blob pinlerinde drift yoktur. Bu bounded
owner tabanının pinidir; coordinator'ın sonraki kaynak birleşimi ayrıca yeniden ölçülür.
