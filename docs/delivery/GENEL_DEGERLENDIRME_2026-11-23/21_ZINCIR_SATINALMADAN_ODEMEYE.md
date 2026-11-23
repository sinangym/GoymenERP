# Zincir: Satın almadan ödemeye — halkalar arası geçişlerin durumu ve yapılacaklar

> Kapsam: master rapor §8.2 zinciri ve yan akışları (stok girişi ve maliyet, muhasebe fişi, ters akışlar, dövizli alım,
> hizmet ve fason alımı). HEAD `ddc6cba`, 23 Kasım 2026. Yalnız kod okunarak hazırlandı; derleme ve test koşulmadı.
> Modüllerin iç ayrıntısı ayrı belgelerdedir; burada halkalar arasındaki geçişlere bakıldı.
> Bu belge karar veya onay değildir.

Kısaltmalar: **ortak işlem** = iki modülün kaydı tek veritabanı işleminde birlikte yazılır, biri olmazsa diğeri de olmaz.
**Port** = bir modülün diğerinden veri okuduğu ya da ona kayıt sunduğu tanımlı kapı. **Outbox dağıtıcısı** = bir modülün
"şu oldu" bildirimini diğer modüllere taşıyan arka plan servisi.

## 1. Kısa sonuç

Zincir bugün baştan sona **yürümüyor**. Arka uçta talepten açık kaleme ve ödeme dağıtımına kadar olan parçalar yazılmış
durumda; ama bir kullanıcı ekrandan ilerlediğinde zincir üç yerde kopuyor ve ödeme tarafının ortası hiç yok.

- **Ekrandan ilk kopuş 3. halkada (sipariş):** sipariş açma ekranı talebe bağlanmıyor ve yalnız tek satır gönderiyor
  (`PurchaseOrderCreatePage.tsx` satır 238–244: `sourceRequestId: null`, tek elemanlı `lines`). Arka uç ikisini de
  destekliyor; onaylı talep ekranda siparişe dönüşmüyor, kullanıcı baştan elle giriyor.
- **Zincirin durduğu yer 6–7. halka (gelen fatura ve üçlü kontrol):** arka uç ve ekran kodu var, ama fatura ekranlarının
  altısı da sunucuda "henüz yayımlanmadı" olarak işaretli (`PurchasingShellCapabilityProvider.cs` satır 110–116:
  `ImplementationAvailable: false`). Sunucu bu ekranları hiçbir kullanıcıya açmıyor
  (`ShellCapabilityService.cs` satır 76). Fatura girilemeyince açık kalem doğmuyor, ödeme faturaya bağlanamıyor.
- **Ödeme tarafının ortası yok:** ödeme önerisi, bütçe/nakit kontrolü, banka talimat dosyası, banka ekstresi içe
  aktarımı ve banka mutabakatı için kod yok (arama sonuçları §3.6'da). Ödeme, elle açılan tek tek "giden ödeme"
  kayıtlarıyla yapılıyor.
- **Otomatik geçişlerin hepsi kapalı:** faturadan muhasebe fişine ve faturadan stok maliyet farkına giden yol outbox
  dağıtıcısına bağlı; dağıtıcı ve muhasebe kabul anahtarı varsayılan olarak kapalı, hiçbir ayar dosyasında açılmamış.
- **Hiçbir iş rolü yok:** zincirdeki 19 Alış izninin ve Finans/Muhasebe izinlerinin hiçbiri başlangıç rollerinde yok
  (`identity-seed.json` içinde `purchasing.`, `finance.`, `accounting.` 0 kez geçiyor).

14 ana halkadan **5'i gerçek** (talep, talep onayı, mal/hizmet kabulü, cari açık kalem, dağıtım), **6'sı kısmi**
(sipariş, termin/sevk bildirimi, üçlü kontrol, gelen fatura, bütçe/nakit/onay, banka sonucu), **3'ü yok**
(ödeme önerisi, banka talimatı, mutabakat).

Sağlam olan taraf: mal kabulü ile stok girişi tek ortak işlemde; tersleme ve tedarikçi iadesi de öyle. Hazırlayan ≠
onaylayan kuralı talep, sipariş, toplu kabul, fatura farkı ve ödemede sunucuda zorlanıyor. Aynı fatura iki kez
kaydedilemiyor.

## 2. Halka halka tablo

Geçiş türü: **otomatik** (ortak işlem ya da olay), **port** (sonraki modül öncekinin verisini okur, kullanıcı ayrı işlem
başlatır), **elle** (kullanıcı yeniden girer), **yok**. Durum: 1 = kod var, 1-kısmi, 0 = yok.
Yollar kısaltıldı: `Api/` = `src/backend/GoymenERP.Api/`, `Alış/` = `src/backend/Modules/Purchasing/`,
`Finans/` = `src/backend/Modules/Finance/`, `web/` = `src/frontend/goymen-erp-web/src/`.

| # | Halka | Modül | Komut / uç | Ekran | Sonraki halkaya geçiş | Taşınan veri | Durum | Kanıt |
|---|---|---|---|---|---|---|---|---|
| 1 | Talep | Alış | `POST purchasing/requests`, `/{id}/submission`, `/{id}/withdrawal` | Talep listesi, yeni talep (çok satırlı), detay | Aynı kayıt üzerinde durum geçişi | Ürün, birim, miktar, isteğe bağlı kaynak (stok/satış/üretim) | 1 | `Api/Endpoints/PurchasingEndpoints.cs`; `web/purchasing/PurchaseRequestCreatePage.tsx` |
| 2 | Talep onayı | Alış | `POST requests/{id}/approval`, `/rejection` | Talep detayında karar paneli | **Port (arka uç) / elle (ekran):** sipariş `sourceRequestId` ile onaylı talebe bağlanabiliyor, ama ekran bu alanı hep boş gönderiyor | Talep kimliği ve satır kimliği (yalnız arka uçta) | 1 | `Alış/…Application/PurchasingService.RequestDecisions.cs`; `web/purchasing/PurchaseOrderCreatePage.tsx:238-244` |
| 3 | Satın alma siparişi | Alış | `POST purchasing/orders`, `/{id}/approval`, `/cancellation`, `/exchange-rate`, `/supplier-revisions` | Sipariş listesi, yeni sipariş (**tek satır, talepsiz**), detay | Aynı kayıt; onaylı sipariş satırı kabule açık | Tedarikçi, cari hesap, para birimi, fiyat, kur | 1-kısmi | `Alış/…Domain/Orders/PurchaseOrder.cs`; `web/purchasing/PurchaseOrderCreatePage.tsx` |
| 4 | Termin / sevk bildirimi | Alış | `POST orders/{id}/lines/{lineId}/schedule` (termin dilimleri) | Sipariş detayında termin paneli | **Yok:** termin bilgisi kabule taşınmıyor; tedarikçi sevk bildirimi ve tedarikçi teyidi yok | Termin tarihi ve miktarı (Stok "gelecek tedarik" olarak okuyor) | 1-kısmi | `Alış/…Application/PurchasingService.LineSchedule.cs`; `Asn`, `DispatchNotice`, `SevkBildirim`, `SupplierConfirm`, `teyit` araması 0 sonuç |
| 5a | Mal kabulü | Alış + Stok | `POST orders/{id}/lines/{lineId}/goods-receipts`, `/goods-receipt-batches` (+ onay/ret) | Sipariş detayında kabul panelleri; Lojistik "Operasyon işleri — Satın alma" | **Otomatik (ortak işlem):** kabul payı, stok hareketi, etiket ve ilk maliyet tek işlemde | Sipariş satırı, miktar, tartılan kg, depo/lokasyon, sipariş net birim fiyatı | 1 | `Api/PurchasingRuntime/PurchaseGoodsReceiptHost.cs:41-53` |
| 5b | Hizmet / fason kabulü | Alış | `POST purchasing/service-orders`, `/{id}/approval`, `/{id}/service-acceptances` | "Hizmet ve fason alışları" listesi, yeni, detay | Port: fatura eşleştirme hizmet kabulünü okuyor | Hizmet türü, miktar, fiyat, fason vergi bilgisi | 1 | `Api/Endpoints/PurchasingEndpoints.ServiceOrders.cs`; `Alış/…Persistence/PurchaseIncomingInvoiceMatchSource.Services.cs` |
| 6 | Üçlü kontrol | Alış | `POST purchasing/invoices/{id}/matching`, `/variance-approval` | Fatura detayı — **ekran yayımlanmamış** | Aynı kayıt (Eşleşti → Kesinleşti) | Kabul payları, beklenen net tutar, kontrol kuru, tolerans sürümü | 1-kısmi | `Alış/…Domain/Invoices/PurchaseIncomingInvoice.cs:186-281`; `Alış/…Infrastructure/Shell/PurchasingShellCapabilityProvider.cs:110-116` |
| 7 | Gelen fatura | Alış | `POST purchasing/invoice-imports/reservations`, `/{id}/preview`, `/{id}/records`; `POST invoices/{id}/finalization` | Fatura listesi ve içe aktarma — **ekran yayımlanmamış** | **Otomatik (port, işlem sonrası):** kesinleşen fatura Finans'a sunulur; olmazsa "Finans bekliyor" kalır, `POST invoices/{id}/finance-admission` ile yeniden denenir | Cari, cari hesap, para birimi, fatura kuru, taksitler (vade, tutar, TL karşılığı) | 1-kısmi | `Alış/…Application/PurchaseIncomingInvoiceWorkflow.cs:76-101`; `Api/PurchasingRuntime/PurchaseInvoiceFinanceAdmissionHost.cs` |
| 8 | Cari açık kalem | Finans | Port: `IFinanceInvoiceOpenItemAdmissionPortV1` (HTTP ucu yok); okuma `GET finance/open-items` | "Açık kalemler", cari ekstre | **Elle:** ödeme, açık kalemden başlatılmıyor; kullanıcı ayrı ödeme kaydı açıyor | — | 1 | `Finans/…Infrastructure/FinanceInvoiceOpenItemAdmissionService.cs`; `Api/Endpoints/FinanceOpenItemReadEndpoints.cs` |
| 9 | Ödeme önerisi | — | Yok | Yok | Yok | — | 0 | `PaymentProposal`, `PaymentSuggestion`, `OdemeOnerisi`, `PaymentRun`, `PaymentPlan` araması 0 sonuç |
| 10 | Bütçe / nakit / onay | Finans | Yalnız ödeme onayı: `POST finance/manual-bank-payments` (taslak → onay) | "Giden Ödemeler" listesi, yeni, detay | Aynı kayıt (Taslak → Onaylı → Kayıtlı) | Cari, cari hesap, banka hesabı, tutar, tarih | 1-kısmi | `Finans/…Infrastructure/ManualBankPaymentService.cs:157-213`; `Budget`, `Butce`, `CashFlow`, `NakitAkis` araması Finans'ta 0 sonuç |
| 11 | Banka talimatı | — | Yok (talimat dosyası ya da banka bağlantısı üretilmiyor) | Yok | Yok | — | 0 | `BankInstruction`, `PaymentOrder`, `BankTalimat`, `PaymentFile` araması Finans'ta 0 sonuç |
| 12 | Banka sonucu | Finans | `POST finance/manual-bank-payments` kayıt adımı; kullanıcı "banka sonucu gerçekleşti" kutusunu işaretler | Giden ödeme detayı | Port: kayıtlı ödeme dağıtıma açılır | Ödeme kimliği, tutar | 1-kısmi | `Finans/…Contracts/ManualBankPaymentV1.cs:60-67` (`BankResultConfirmed`) |
| 13 | Mutabakat | — | Yok. Yalnız banka CSV sütun eşleme **ayarı** var; ekstreyi içe alan kod yok | Yok | Yok | — | 0 | `BankStatement`, `StatementLine`, `Reconcil`, `Mutabakat`, `Ekstre` araması Finans'ta 0 sonuç; `Api/Endpoints/SettingsBankCsvMappingEndpoints.cs` yalnız ayar |
| 14 | Dağıtım | Finans | `POST finance/payment-allocations` (+ serbest bırakma) | Giden ödeme detayında dağıtım paneli | Zincirin sonu; kalan sıfırsa kalem kapanır | Ödeme, açık kalem, tutar, gerekçe | 1 | `Finans/…Infrastructure/FinancePaymentAllocationService.cs`; `web/finance/ManualBankPaymentPages.tsx:20` |

Yan akışlar:

| # | Geçiş | Modüller | Tür | Durum | Kanıt |
|---|---|---|---|---|---|
| Y1 | Kabul → stok girişi ve ilk maliyet | Alış → Stok | Otomatik, ortak işlem | 1 | `Api/PurchasingRuntime/PurchaseGoodsReceiptHost.cs`, `PurchaseReceiptCostScope.cs`, `Api/InventoryRuntime/PurchasingOwnerInventoryReceiptCostSource.cs` |
| Y2 | Kesinleşen fatura → stok ortalama maliyetinin fatura fiyatına çekilmesi | Alış → Stok | Otomatik, olay (**dağıtıcı kapalı**) | 1-kısmi | `Api/Shell/TrainCompletionHostComposition.cs:86-97`; `…Inventory…/Persistence/InventoryPurchaseInvoiceCostStoreV1.cs` |
| Y3 | Kesinleşen fatura → muhasebe kaynak fişi (onay bekleyen) | Alış → Muhasebe | Otomatik, olay (**dağıtıcı ve kabul anahtarı kapalı**) | 1-kısmi | `…Accounting.Application/Inbox/AccountingPurchaseInvoiceFinalizedInboxConsumer.cs`; `Api/AccountingRuntime/AccountingInvoiceJournalHost.cs:52-58` |
| Y4 | Stok maliyet farkı → muhasebe maliyet farkı önerisi | Stok → Muhasebe | Otomatik, olay (**dağıtıcı kapalı**); onay elle | 1-kısmi | `Api/Endpoints/AccountingEndpoints.InventoryCosts.cs`; `web/accounting/InventoryCostProposalsPage.tsx` |
| Y5 | Ek maliyet (navlun vb.) → stok maliyeti | Alış → Stok | **Yok** (karar gereği masraf olarak kalıyor) | 0 | `Alış/…Domain/Expenses/` sınıf açıklaması: "No quantity, stock movement, allocation or average-cost input exists" |
| Y6 | Giden ödeme → muhasebe fişi | Finans → Muhasebe | **Yok** | 0 | `NormalJournalSourceKindsV1` listesinde tahsilat, masraf ödemesi, virman, kredi var; giden banka ödemesi yok (`…Accounting.Application/ApprovedNormalJournalSourceBindingV1.cs:9-18`) |
| Y7 | Kur farkı | Finans / Muhasebe | **Yok** | 0 | `ExchangeDifference`, `KurFark`, `FxDifference`, `Revaluation`, `RateDifference` araması 0 sonuç |

## 3. Sorunlu ya da dikkat isteyen geçişler

### 3.1 Talep → sipariş: arka uçta bağlı, ekranda kopuk

Arka uç siparişi onaylı bir talebe bağlıyor, talep onaylı değilse reddediyor ve bir talep satırına açılan siparişlerin
toplamının ihtiyacı aşmasına izin vermiyor (`PurchaseOrder.cs` satır 116 açıklaması). Ekran ise `sourceRequestId` ve
`sourceRequestLineId` alanlarını her zaman boş gönderiyor. Talep detayında "siparişe dönüştür" düğmesi yok
(`PurchaseRequestDetailPage.tsx` ve `purchasingText.ts` içinde arandı). Sonuç: onaylanan talep ile açılan sipariş
arasında kayıt bağı oluşmuyor, "talepsiz sipariş" ile "talepli sipariş" ayırt edilemiyor, kullanıcı ürün ve miktarı
ikinci kez giriyor.

Aynı ekran yalnız **tek satırlı** sipariş açıyor. Talep ve hizmet siparişi ekranları çok satırlı; mal siparişi ekranı
değil. Çok kalemli bir alım için kullanıcı ya ayrı siparişler açmak ya da tedarikçi revizyonu panelini kullanmak
zorunda (revizyon panelinin satır ekleyip eklemediği doğrulanamadı).

### 3.2 Termin var, sevk bildirimi ve tedarikçi teyidi yok

Sipariş satırına termin dilimleri giriliyor ve Stok bunu "gelecek tedarik" olarak okuyor
(`Api/Endpoints/PurchasingOwnerInventoryFutureSupplyScheduleSource.cs`). Tedarikçinin "yola çıktı" bildirimi (irsaliye
ön kaydı) ve 2 Kasım kararındaki isteğe bağlı tedarikçi teyidi (ALS-D13) için kod yok. Kabul ekranı beklenen sevkiyatı
göstermiyor; depo sorumlusu siparişi ve satırı kendisi buluyor.

### 3.3 Fatura ekranları sunucuda kapalı

Bu, zincirin fiilen durduğu yerdir. Fatura içe aktarma, eşleştirme, fark onayı ve kesinleştirme uçları çalışır
durumda yazılmış; ekran dosyaları da var (`web/purchasing/IncomingInvoicePages.tsx`, `InvoiceImportPanel.tsx`,
`InvoiceImportAdmissionPanel.tsx`) ve menü girişi tanımlı (`web/shell/navigation.ts` satır 495, "Alış faturaları").
Ancak sunucu bu altı yeteneği `ImplementationAvailable: false` ile bildiriyor; ön yüzdeki kapı
(`web/purchasing/purchasingRoutes.tsx` satır 16: "staged invoice routes fail closed until published") sayfayı açmıyor.
Açılması tek bir bayrağın değiştirilmesi gibi görünse de, bu bayrak "kabul kanıtı incelenene kadar kapalı" anlamında
bilinçli konmuş; açmadan önce ekranın gerçek sunucuya karşı denenmesi gerekir.

Fatura yalnız dosyadan gelir (e-fatura XML, CSV, Excel; `…Infrastructure/Imports/PurchaseInvoiceFileReader.cs`).
Elle fatura girişi yok; bu 2 Kasım kararıyla uyumlu (ALS-D7: "Mikro'dan içe aktar + eşleştir").

### 3.4 Fatura → açık kalem: otomatik ama iki adımlı

Fatura kesinleşince Alış kendi kaydını yazar, sonra aynı istek içinde Finans'a sunar. Finans yanıt vermezse fatura
"Finans bekliyor" durumunda kalır ve kullanıcı yeniden dener. Aynı anahtarla tekrar sunmak ikinci açık kalem
üretmiyor (Finans tarafı `AdmissionId` ile tekilleştiriyor). Bu geçiş outbox dağıtıcısına bağlı **değil**, dolayısıyla
dağıtıcı kapalıyken de çalışır. Bekleyen faturaları toplu gösteren ya da kendiliğinden yeniden deneyen bir iş yok;
unutulan fatura açık kalem üretmeden bekler.

### 3.5 Açık kalem → ödeme: bağ yok, kullanıcı elle kuruyor

Giden ödeme taslağı cari, cari hesap, banka hesabı ve tutarla açılıyor; hangi faturayı ödediği taslakta yok
(`ManualBankPaymentV1.cs` `ManualBankPaymentPreparation`). Fatura bağı ancak ödeme "kayıtlı" olduktan sonra dağıtım
adımında kuruluyor. Bu şu anlama gelir: onaylayan kişi ödemenin hangi faturalar için olduğunu onay anında sistemde
göremez; vadesi gelen kalemlerden toplu ödeme listesi çıkarılamaz.

### 3.6 Ödeme önerisi, bütçe/nakit, banka talimatı, mutabakat: yok

Arka uçta (`src/backend`, göç dosyaları hariç) yapılan aramalar: ödeme önerisi 0 dosya; bütçe 2 dosya (ikisi de stok
tersleme kodunda geçen başka bir sözcük); nakit akışı 0; banka talimatı için Finans'ta 0; banka ekstresi ve mutabakat
için Finans'ta 0. Banka CSV sütun eşlemesi Ayarlar'da tanımlanabiliyor ama onu kullanan bir içe aktarım yok.

### 3.7 Otomatik olay yolları kapalı

`Program.cs` satır 507'de dağıtıcı kaydı var, ama `Work:OutboxDispatcher:Enabled` true olmadıkça hiçbir şey çalışmıyor
(`WorkModuleExtensions.cs` `AddWorkOutboxDispatcher`). `appsettings.json` ve `appsettings.Development.json` içinde bu
bölüm yok; depoda bu anahtarı açan bir ayar dosyası bulunamadı. Açıldığında beş sayı (parti boyu, deneme sayısı,
bekleme süreleri, tarama aralığı) da verilmek zorunda; verilmezse uygulama açılmıyor
(`WorkOutboxDispatcherOptions.cs`). Değerler seçilmemiş (kod yorumu: NEEDS-SINAN).

Muhasebe tarafında ikinci bir anahtar var: `Accounting:InvoiceJournal:InboxAdmissionEnabled`. Kapalıyken muhasebe her
faturayı "şu an kabul edemiyorum" diye geri çevirir ve olay yeniden denenir
(`AccountingInvoiceJournalHost.cs` satır 52–58). Kod yorumuna göre bu sistem kabulünü hangi iznin ölçeceği karara
bağlanmamış.

Dağıtıcı kapalıyken olmayanlar: fatura → muhasebe kaynak fişi; fatura → stok ortalama maliyetinin güncellenmesi; stok
maliyet farkı → muhasebe önerisi; masraf ödemesi/virman → muhasebe fiş kaynağı; satır yeniden açılma bildirimi.
Dağıtıcı kapalıyken olanlar: kabul → stok, tersleme → stok, iade → stok (ortak işlem); fatura → açık kalem (port).

### 3.8 Giden ödemenin muhasebe fişi yok

Tahsilat, masraf ödemesi, virman ve kredi hareketi muhasebeye fiş kaynağı olarak gidebiliyor; tedarikçiye yapılan
banka ödemesi bu listede yok ve Finans bu ödeme için olay yayınlamıyor (`ManualBankPaymentService.cs` içinde `Outbox`
araması 0 sonuç). Fatura muhasebeleşse bile "320 Satıcılar borç / 102 Bankalar alacak" kaydını üretecek yol yok.

### 3.9 Ek maliyet stok maliyetine girmiyor

Navlun ve benzeri ek masraflar siparişe bağlanıyor ve tedarikçi faturasının masraf satırıyla eşleşiyor
(`Api/Endpoints/PurchasingEndpoints.AdditionalCosts.cs`, `web/purchasing/AdditionalCostPage.tsx`). Stok maliyetine
dağıtım yok. Bu, ALS-D23'ün kendi ifadesiyle uyumlu ("maliyete dağıtım sonraki muhasebe aşamasıdır"); ancak görev
tanımındaki "ek maliyet/navlun dahil stok maliyeti" bugün karşılanmıyor.

### 3.10 Mal kabulünde kg toleransı hiçbir zaman okunmuyor

Toplu mal kabulü, beklenen ile tartılan kg farkını Ayarlar'daki tolerans yüzdesiyle karşılaştırmak üzere yazılmış.
Toleransı okuyan port (`IPurchaseGoodsReceiptPolicyReadPortV1`) için uygulama yok: `src` altında yalnız arayüz tanımı
ve isteğe bağlı kullanım var, uygulayan sınıf yalnız testlerde. Çalışan üründe tolerans hep "ayarlanmamış" sayılıyor;
kg birimli satırda farkı olan her toplu kabul onaya düşüyor (`PurchasingService.GoodsReceiptBatch.cs` satır 110–122,
624). Onay için ayrı izin ve farklı kişi gerekiyor. Rol tanımlanmadan bu, kabulü kilitler.

## 4. Kontroller

| Kontrol | Sunucuda zorlanıyor mu | Eşik / kural nereden | Kanıt | Not |
|---|---|---|---|---|
| Talep: hazırlayan ≠ onaylayan | Evet; ayrıca veritabanı kısıtı | Sabit kural; hazırlayan ve gönderen ikisi de "hazırlayan" sayılır | `…Domain/Requests/PurchaseRequest.cs:250`; README `CK_PurchaseRequests_MakerChecker` | Göndereni kayıtlı olmayan talep hiç onaylanamaz |
| Sipariş: hazırlayan ≠ onaylayan | Evet | Sabit kural | `…Domain/Orders/PurchaseOrder.cs:237` | Tutar eşiği yok |
| Sipariş onay eşiği (tutara göre kademe) | **Yok** | — | Alış'ta `Threshold`, `ApprovalLimit`, `AmountLimit` araması 0 sonuç | Master §12 kademeli onay bekliyor |
| Sipariş iptali | İzin + gerekçe; ikinci onay yok | Karar (ALS-D67) | `PurchasingService.OrderCancellation.cs` | Bilinçli tek adım |
| Fazla teslimat | Evet, **sert ret**: kabul kalan miktarı aşamaz | Tolerans yok | `PurchaseOrder.cs:458` (`AcceptanceExceedsRemaining`) | "Fazla teslimat onayı" izni kayıtlı ama hiçbir komut kullanmıyor |
| Mal kabulü kg farkı | Evet | Ayarlar'dan okunması tasarlanmış, **port bağlı değil** (§3.10) | `…Domain/GoodsReceipts/GoodsReceiptKgVariance.cs` | Fiilen her fark onaya düşer |
| Toplu kabul: hazırlayan ≠ onaylayan | Evet | Sabit kural | `…Domain/GoodsReceipts/PurchaseGoodsReceiptBatch.cs:110` | |
| Üçlü kontrol toleransı (tutar ve kur) | Evet; tolerans okunamazsa eşleştirme reddedilir, sıfır varsayılmaz | Ayarlar: "Alış faturası fark toleransı" (sürümlü) | `PurchaseIncomingInvoice.cs:190, 241, 275`; `Api/Endpoints/SettingsPurchaseInvoiceVariancePolicyEndpoints.cs`; `web/settings/PurchaseInvoiceVariancePolicyPage.tsx` | Miktar farkında tolerans yok: her fark kayda geçer |
| Fiyat/miktar/kur farkı onayı | Evet; fark varken onaysız kesinleştirme reddedilir | Yukarıdaki tolerans | `PurchaseIncomingInvoice.cs:284-301` | Faturayı kaydeden ve eşleştiren onaylayamaz |
| Hizmet siparişi: hazırlayan ≠ onaylayan | Evet | Sabit kural | `…Domain/Services/PurchaseServiceOrder.cs:55` | |
| Tedarikçi iadesi maliyet istisnası | Evet | Politika alanı var, "aynı kişi karar verebilir" kapalı | `…Domain/Returns/PurchaseSupplierReturn.cs:107-130` | |
| Ödeme: hazırlayan ≠ onaylayan | Evet; taslağı düzelten herkes hazırlayan sayılır | Sabit kural | `ManualBankPaymentService.cs:189-200, 592-609` | |
| Ödeme onay eşiği (tutara göre) | **Yok** | — | Finans'ta `Threshold`, `ApprovalLimit`, `AmountLimit` araması 0 sonuç | |
| Ödemede bütçe / nakit kontrolü | **Yok** | — | §3.6 | |
| Açık kalemi aşan dağıtım | Tutar ve taraf sunucuda sahipli | — | `FinancePaymentAllocationV1.cs` sınıf açıklaması | Aşım reddi satır satır okunmadı (bkz. §10) |

Tüm komutlarda izin, şirket bağlamı, beklenen sürüm ve tekrar anahtarı sunucuda denetleniyor; izin her yazmadan hemen
önce yeniden ölçülüyor (ör. `PurchaseIncomingInvoiceWorkflow.cs` `RequireAsync` ve `FenceAsync`).

## 5. Ters akışlar

| Ters akış | Var mı | Stok | Maliyet | Açık kalem | Muhasebe | Kanıt / not |
|---|---|---|---|---|---|---|
| Talep reddi / geri çekme | Evet | — | — | — | — | Onaylı siparişe bağlı talep reddedilemez |
| Sipariş iptali (kalanın tamamı) | Evet | Fiziksel etki yok; gelecek tedarik düşer, bağlı satış tahsisi varsa ayrı uçla serbest bırakılır | — | — | — | `POST orders/{id}/cancellation`, `/cancellation-with-allocation-release`; kısmi/satır bazlı iptal yok (satır kapanışı ayrı: `/lines/{lineId}/closure`, `/reopening`) |
| Kabul tersleme (tam, kısmi, düzeltme, toplu) | Evet | Geri alınıyor, ortak işlem | Stok tarafında izleniyor | **Etki yok** | **Etki yok** | `Api/PurchasingRuntime/PurchaseReceiptReversalHost*.cs`; `…Inventory…/InventoryGoodsReceiptReversalParticipant.cs` |
| Tedarikçi iadesi | Evet | Çıkış, ortak işlem | Özgün satır maliyetiyle ya da fatura birim maliyetiyle | **Etki yok**: iade faturası, alacak dekontu ya da borcun düşülmesi yok | **Etki yok** | `Api/PurchasingRuntime/PurchaseSupplierReturnHost.cs`; `SupplierReturn` araması Finans ve Muhasebe'de 0 sonuç |
| Fatura iptali / düzeltme | **Yok** | — | — | Finans'ta iptal portu hazır ama Alış çağırmıyor | — | Fatura durumları yalnız Kayıtlı, Eşleşti, Finans bekliyor, Finans kabul etti (`PurchaseIncomingInvoice.cs:63`); `IFinanceInvoiceOpenItemCancellationPortV1` kullanan tek yer Satış (`Api/Shell/SalesFinanceCancellationHost.cs`) |
| Ödeme tersleme | Evet (öneri + onay) | — | — | Dağıtım serbest bırakılır | Fiş yok (§3.8) | `finance/manual-bank-payments/reversal-proposals`; `web/finance/PaymentReversalPanel.tsx` |
| Dağıtımın geri alınması | Evet | — | — | Kalem yeniden açılır | — | `FinancePaymentAllocationView` `ReleasedBy`, `ReleaseReason` |

Dikkat isteyen iki nokta:

1. **Yanlış içe aktarılan ya da tedarikçinin iptal ettiği fatura geri alınamıyor.** Kesinleşmiş fatura açık kalem
   doğurduktan sonra onu kapatacak bir Alış komutu yok. Finans tarafındaki iptal servisi yazılmış ve Satış için
   kullanılıyor; Alış bağlantısı eksik (devir notu `docs/handoffs/FINANS_ALIS_DEVIR_v1.md` FA-2b bunu "bağlantı Alış'ın
   iptal komutunda" diye bırakmış).
2. **Tedarikçi iadesi parasal sonuç doğurmuyor.** Mal stoktan çıkıyor ama tedarikçiye olan borç aynı kalıyor. 2 Kasım
   kararları iade faturasını sonraya bırakmış (`SINAN_KARARLARI_2026-11-02_ALIS.md` satır 69); yine de test ortamında
   iade yapan kullanıcı cari bakiyenin değişmediğini görecek.

Faturası kesinleşmiş bir kabulün terslenmesini Alış tarafında engelleyen bir kural bulunamadı (tersleme
dosyalarında `invoice` araması 0 sonuç). Eşleştirme, önceden yapılmış terslemeleri hesaba katıyor (faturalanabilir pay
= kabul − geri alınan; `PurchaseIncomingInvoice.cs:214-222`); sıra tersine olduğunda ne olduğu test edilerek
doğrulanmalı.

## 6. Tek gerçeklik denetimi

| Soru | Sonuç | Kanıt |
|---|---|---|
| Stok girişi ile alış kabulü tek işlemde mi | Evet. Alış kabul payı, Stok hareketi, etiket ve maliyet aynı veritabanı işleminde; biri reddederse hepsi geri alınır | `PurchaseGoodsReceiptHost.cs:41-88` |
| Aynı kabul iki kez kaydedilebilir mi | Hayır. Kabul kimliği ve tekrar anahtarı önce aranıyor; aynı istek ilk sonucu döndürür, farklı içerik reddedilir. Kalan miktarı aşan kabul reddedilir | `PurchaseGoodsReceiptHost.cs:56-64`; `PurchaseOrder.cs:458` |
| Aynı fatura iki kez kaydedilebilir mi | Hayır. Şirket + e-fatura kimliği (ETTN) tekil; şirket + tedarikçi + resmî fatura numarası tekil | `…Persistence/PurchasingDbContext.IncomingInvoices.cs:26-27` |
| Aynı kabul iki faturaya bağlanabilir mi | Bir eşleştirmede her kabul bir kez; faturalar arası kalan pay hesabı var | `PurchaseIncomingInvoice.cs:196-199`; `PurchaseIncomingInvoiceMatchSource.cs` |
| Aynı fatura iki açık kalem doğurabilir mi | Hayır. Finans kabulü `AdmissionId` ile tekil ve tekrar edilebilir | `FinanceInvoiceOpenItemAdmissionV1.cs` açıklaması |
| Aynı ödeme iki kez kaydedilebilir mi | Aynı komut anahtarıyla hayır. **Ancak** ödeme faturaya bağlı açılmadığı için aynı fatura için ikinci bir ödeme taslağı açılmasını engelleyen bir şey yok; fazlalık ancak dağıtımda görünür | `ManualBankPaymentV1.cs` |
| Kopya veri | Sipariş fiyatı kabulde stok maliyetine, fatura tutarı açık kaleme "kopya" değil "dondurulmuş kaynak" olarak geçiyor (kimlik ve sürümle). Dağıtım ayrı para hareketi üretmiyor | `FinancePaymentAllocationV1.cs:5-10` |
| Elle yeniden giriş | Talep → sipariş (ürün, miktar); açık kalem → ödeme (cari, tutar) | §3.1, §3.5 |

## 7. Üç şirket boyutu

- **Şirket ayrımı:** zincirdeki bütün kayıtlar şirket kimliği taşıyor ve her komut sunucunun çözdüğü şirketle
  karşılaştırıyor (ör. `PurchaseIncomingInvoiceWorkflow.cs` `invoice.CompanyId != scope.Company` denetimi). Outbox
  kaynağı da yalnız kendi şirketinin satırlarını okuyor (`PurchasingOutboxEventSource.cs`).
- **Şirketler arası alım:** temsili yok. `Intercompany`, `InterCompany`, `GrupIci` araması arka uçta 0 sonuç. GSP'nin
  GDC'den alımı bugün iki bağımsız kayıtla yapılır: GSP'de bir alış siparişi, GDC'de bir satış siparişi. Aralarında
  bağ, karşılıklı mutabakat ya da tek girişle çift kayıt yok; miktar ve fiyat iki tarafa ayrı ayrı girilir.
- **Ortak tedarikçi:** Alış, tedarikçiyi Cari modülünün portundan okuyor ve siparişe bir "cari hesap" bağlıyor
  (`Program.cs:434`, `IPurchasingPartyAccountPortV1`). Bir tedarikçinin üç şirkette nasıl paylaşıldığı (tek taraf,
  şirket başına ayrı hesap) Cari modülünün konusudur; bu çalışmada doğrulanmadı.
- **Grup düzeyinde ödeme ya da borç görünümü:** yok. Açık kalemler ve giden ödemeler şirket bazında.

## 8. Master §29 kabul senaryolarından bu zincire düşenler

| Senaryo | Bugün koşulabilir mi | Engel |
|---|---|---|
| AT-004 Oluşturan kendi yüksek riskli işlemini onaylayamaz | Arka uçta evet (talep, sipariş, toplu kabul, fatura farkı, ödeme) | Ekrandan denemek için iki ayrı kullanıcı ve rol gerekir; iş rolü tanımlı değil |
| AT-007 İptal ve ters işlem geçmişi silmez | Kısmen | Kabul tersleme, iade, ödeme tersleme, sipariş iptali var; fatura iptali yok |
| AT-024 Kısmi teslimat sipariş kalanını doğru tutar (alış tarafı) | Evet | Rol |
| AT-025 Satış ve satın alma belgeleri operasyon listelerinde karışmaz | Evet (ayrı "Operasyon işleri — Satın alma" girişi) | Rol; ayrıntı Lojistik belgesinde |
| AT-026 Fazla/eksik teslimat toleransı | **Hayır** | Fazla teslimat sert reddediliyor, tolerans bandı yok; eksik teslimde tolerans önerisi yazılmamış (`PurchasingService.LineClosure.cs:19-20`); kabul kg toleransı portu bağlı değil (§3.10); OPEN-004 değerleri geçici |
| AT-027 Tedarikçi fiyat/miktar farkı onaysız faturaya geçmez | Arka uçta evet; ekrandan **hayır** | Fatura ekranları yayımlanmamış (§3.3) |
| AT-028 İade kaynak sipariş, teslimat ve fatura bağını korur | Kısmen | İade kabul ve siparişe bağlı, fatura birim maliyetini okuyor; iadenin kendi faturası ve cari etkisi yok |
| AT-058 Ödeme açık kalem ve banka hareketine çift yönlü bağlanır | Kısmen | Ödeme ↔ açık kalem dağıtımla bağlanıyor; banka hareketi (ekstre satırı) kavramı yok |
| AT-064 Alış siparişi kalanı mal kabulden önce fiziksel stoğu artırmaz | Evet (Stok belgesinde doğrulanmalı) | Rol |
| AT-065 Tek belge bir kez saklanır, tüm ekranlar aynı referansı gösterir | Kısmen | Sipariş detayında belge paneli var, fatura içe aktarma belge kasasından okuyor; ödeme belge bağı var. Uçtan uca aynı dosyanın dört ekranda görünmesi denenmedi |

## 9. Yapılacaklar

Öncelik: P0 = üç firmanın test ortamında bu zinciri uçtan uca yürütebilmesi için şart. Büyüklük: K (gün), O (1–2
hafta), B (daha fazla).

| # | İş | Neden | Öncelik | Büyüklük | İlgili modüller |
|---|---|---|---|---|---|
| 1 | Alış, Finans ve Muhasebe için iş rollerini tanımlamak ve zincirin izinlerini bu rollere vermek (talep açan, satın alma uzmanı, satın alma müdürü, depo/kabul, fatura eşleştiren, finans hazırlayan, finans onaylayan, muhasebe) | Hiçbir izin hiçbir rolde yok; hazırlayan ≠ onaylayan her adımda en az iki kullanıcı ister. Yetki verme insan kapısıdır | P0 | O | Kimlik, Alış, Finans, Muhasebe |
| 2 | Alış faturası ekranlarını (liste, içe aktarma, detay, eşleştirme, fark onayı, kesinleştirme) gerçek sunucuya karşı deneyip yayımlamak | Zincir burada duruyor; arka uç hazır, ekran kapalı (§3.3) | P0 | O | Alış |
| 3 | Sipariş açma ekranını talebe bağlamak ("talepten sipariş aç") ve çok satırlı yapmak | Talep onayı ekranda siparişe taşınmıyor; tek satır kullanılamaz (§3.1) | P0 | O | Alış |
| 4 | Mal kabulü kg toleransı portunu Ayarlar'a bağlamak ve ayar ekranını sağlamak | Bağlı değilken farkı olan her toplu kabul onaya düşüyor (§3.10) | P0 | K–O | Alış, Ayarlar |
| 5 | Test ortamında outbox dağıtıcısını açmak: beş değerin seçilmesi ve muhasebe kabul anahtarının kararı | Fatura → muhasebe fişi ve fatura → stok maliyeti yolu bu olmadan çalışmıyor (§3.7). Değerler ve etkinleştirme kullanıcı kararıdır | P0 | K (karar) + K | Platform (Work), Muhasebe, Stok |
| 6 | Zincir için kurulum verisinin ekrandan girilebildiğini doğrulamak: tedarikçi cari hesabı, ürün, depo/lokasyon, para birimi ve kur, fatura fark toleransı, banka hesabı (onaylı), muhasebe hesap eşlemesi | Bunlardan biri eksikse ilgili adım "kullanılamıyor" yanıtı veriyor | P0 | O | Cari, Ürün, Organizasyon, Ayarlar, Finans, Muhasebe |
| 7 | Talepten dağıtıma tek bir uçtan uca tarayıcı senaryosu yazmak ve koşmak | Halkalar ayrı ayrı test edilmiş; ödeme ile alış faturasını birlikte kullanan test bulunamadı | P0 | O | Hepsi |
| 8 | Giden ödemeyi açık kalemlerden başlatmak (kalem seç → ödeme taslağı) ve taslakta ödenen faturaları göstermek | Onaylayan neyi onayladığını göremiyor; çift ödeme riski (§3.5, §6) | P1 | O | Finans |
| 9 | Alış faturası iptal komutu ve Finans iptal portuna bağlantı | Yanlış ya da iptal edilen fatura geri alınamıyor (§5) | P1 | O | Alış, Finans, Muhasebe |
| 10 | Giden banka ödemesinin muhasebe fiş kaynağı | Ödeme muhasebeye yansımıyor (§3.8) | P1 | O | Finans, Muhasebe |
| 11 | Tedarikçi iadesinin parasal sonucu (iade faturası ya da alacak kaydı, açık kalem etkisi) | İade stoku düşürüyor, borcu düşürmüyor (§5) | P1 | O–B | Alış, Finans, Muhasebe |
| 12 | "Finans bekliyor" durumunda kalan faturalar için liste ve kendiliğinden yeniden deneme | Unutulan fatura açık kalem üretmiyor (§3.4) | P1 | K | Alış |
| 13 | Ödeme önerisi: vadesi gelen kalemlerden öneri listesi, toplu onay | Master §8.2 halkası; bugün yok | P1 | B | Finans |
| 14 | Banka ekstresi içe aktarımı ve banka mutabakatı | Master §8.2 halkası; yalnız CSV eşleme ayarı var | P1 | B | Finans, Ayarlar |
| 15 | Tutara göre onay eşikleri (sipariş ve ödeme) | Master §12; bugün yalnız "farklı kişi" kuralı var. Eşik değerleri kullanıcı kararıdır | P1 | O | Alış, Finans, Ayarlar |
| 16 | Fazla/eksik teslimat tolerans bandı (uyarı/onay/blok) ve fazla teslimat onayı | AT-026; izin kayıtlı ama kullanılmıyor; OPEN-004 değerleri geçici | P1 | O | Alış, Ayarlar |
| 17 | Kur farkı (dövizli faturanın ödeme günündeki farkı) | Dövizli alımda fatura kuru ve TL karşılığı tutuluyor, fark hesabı yok | P1 | B | Finans, Muhasebe |
| 18 | Ek maliyetin (navlun vb.) stok maliyetine dağıtımı | ALS-D23 "sonraki muhasebe aşaması" demiş; bugün yok | P1 | B | Alış, Stok, Muhasebe |
| 19 | Tedarikçi teyidi ve sevk bildirimi (beklenen sevkiyat) | ALS-D13 ve §8.2 halkası; kabul ekranı bekleneni göstermiyor | P2 | O | Alış, Lojistik |
| 20 | Şirketler arası alım-satım bağı | Üç şirket birbirinden alıyorsa iki tarafın tek girişle tutarlı olması | P2 | B | Alış, Satış, Finans |
| 21 | Banka talimat dosyası, bütçe ve nakit kontrolü | §8.2 halkaları; test başlangıcı için şart değil | P2 | B | Finans |

## 10. Doğrulanamayanlar

- Hiçbir test ya da derleme koşulmadı. "Var" denen her şey kod okumasına dayanır; test kaynağı mevcut
  (`tests/GoymenERP.Purchasing.UnitTests` 97 dosya, `tests/GoymenERP.Api.IntegrationTests` içinde adı "Purchas"
  içeren 99 dosya, ön yüzde `purchasing/` altında 69 test dosyası) ama sonuçları koordinatörden gelecek.
- Fatura ekranlarının `ImplementationAvailable: false` olmasının test ortamında başka bir yolla açılıp açılmadığı
  (`Api/Shell/C01E2eShellCapabilityActivation.cs` bir test etkinleştirme katmanı içeriyor) ayrıntılı okunmadı.
- Tedarikçi revizyonu panelinin onaylı siparişe yeni satır ekleyip ekleyemediği okunmadı.
- Faturası kesinleşmiş bir kabul terslendiğinde stok maliyeti, açık kalem ve muhasebe tarafında ne olduğu yalnız
  kısmen izlendi (Stok tarafında `InventoryPurchaseInvoiceCostStoreV1.ReceiptReversals.cs` var; Alış tarafında engel
  bulunamadı).
- Ödeme dağıtımında açık kalem kalanını aşan tutarın reddi satır satır okunmadı; sözleşme açıklaması tutarın sunucuda
  sahipli olduğunu söylüyor.
- İş rolü oluşturma ucu: kimlik uçlarında yalnız rol okuma (`GET access/roles`) görüldü, rol oluşturma ucu görülmedi;
  kesin sonuç Kimlik belgesinde doğrulanmalı.
- Ortak tedarikçinin üç şirkette paylaşımı ve Lojistik "Operasyon işleri — Satın alma" ekranının kabule nasıl
  bağlandığı ilgili modül belgelerine bırakıldı.
- Fason alımda malzemenin fasoncuya çıkışı ve dönüşü (Stok ve Üretim tarafı) izlenmedi; yalnız hizmet siparişi, hizmet
  kabulü ve faturayla eşleşmesi görüldü.
- Belge ile kod çelişkileri: `docs/operations/CANLI_ORTAM_KURULUM_REHBERI_v1.md` satır 128 dağıtıcının `Program.cs`'te
  hiç çağrılmadığını söylüyor; kodda çağrılıyor (satır 507), yalnız anahtar kapalı. `Alış/README.md` "Current state
  (2026-11-02)" bölümünün ilk maddeleri sipariş iptali ve revizyonun olmadığını söylüyor; ikisi de kodda var (README
  aynı bölümde sonradan bunu düzeltiyor). `docs/handoffs/FINANS_ALIS_DEVIR_v1.md` FA-4 dağıtım ekranını "sonraki
  dilimde" diyor; ekran var (`web/finance/PaymentAllocationPanel.tsx`).
