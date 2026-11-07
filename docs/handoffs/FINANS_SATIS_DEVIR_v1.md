# Finans → Satış görev notu v1

Tarih: 2026-11-02 · Yazan: Finans şeridi (Claude) · Alıcı: Satış modülünü yürüten konuşma
Kararlar: FNS-D2, FNS-D7, FNS-D14 (`docs/decisions/SINAN_KARARLARI_2026-11-02_FINANS.md`); SAT-D85/D86/D96/D129/D172/D208;
DEC-FIN-005/006. Bağlam: `docs/delivery/FINANS_HAZIRLIK_PLANI_v1.md` §6 MOD-04/MOD-05, §18; Satış notu
`docs/handoffs/SATIS_MODUL_BAGLANTILARI_v1.md`. Finans şeridi Satış dosyalarını değiştirmez. Bu not iş tanımıdır; Satış'ın
kendi karar, review ve merge kapıları geçerlidir.

## FS-1 · Kesinleşen satış faturası → alacak kalemi (MOD-04, API-10)

- **Finans tarafı:** `IFinanceInvoiceOpenItemAdmissionPortV1.AdmitAsync(FinanceContext, CommandMetadata,
  FinanceInvoiceOpenItemAdmissionV1)` (F2a). Host kaydı, Satış'ın kesinleştirme portu geldiğinde yapılır.
- **Alanlar:** `AdmissionId` (Satış'ın bu kabul için ürettiği id), `Producer = Sales`, `InvoiceId`, `InvoiceVersion`
  (token), `PartyId`, `CompanyAccountId`, `CurrencyId`, `ExchangeRate` (SAT-D172 dondurulmuş kur; TL faturada null),
  `Installments[]`: `OpenItemId`, `InstallmentId`, `Sequence` (1…n), `DueOn`, `Amount` (ödenecek tutar payı, exact
  decimal text), `FunctionalAmount` (TL karşılığı, Satış calculator'ı hesaplar; TL faturada null). Taksit yoksa tek
  taksit.
- **Idempotency:** `CommandMetadata.IdempotencyKey`, öneri `sales-invoice:{invoiceId}:{invoiceVersion}`. Aynı anahtar ve
  payload aynı kullanıcıyla tekrar oynatılır (başka payload ya da kullanıcı → `IdempotencyConflict`); aynı fatura aynı
  sürüm ve aynı satırlarla yeni bir kabulle gelirse ikinci kalem açılmaz; farklı satır **veya farklı sürümle**
  `finance_invoice_already_admitted` döner. Yorum: değişen fatura iptal (ters kayıt, SAT-D96; FS-1b) ve yeniden kesimle düzeltilir.
- **İşlem sınırı:** Finans kendi işleminde yazar (tek `SaveChanges`); Satış'ın veritabanı işlemine katılmaz. Satış
  kesinleşmeyi "Finans kabulü bekliyor" durumuyla commit eder, sonra portu aynı anahtarla çağırır; hata ya da zaman
  aşımında aynı anahtarla yeniden dener.
- **Yeniden gönderim:** aynı kullanıcı aynı anahtarla tekrar dener; başka bir yetkili kullanıcı ya da arka plan taraması
  (SAT-D208) **yeni anahtar ve aynı `AdmissionId`** ile gönderir → aynı payload replay olur, farklı payload
  `finance_invoice_admission_identity_conflict` ile reddedilir. Satış `AdmissionId`, `OpenItemId`'ler ve payload'u
  "Finans kabulü bekliyor" durumuyla birlikte kalıcı saklar (payload hash bu id'leri içerir).
- **Yetki:** Satış kendi kesinleştirme yetkisini ölçer; Finans ayrıca host'un sağladığı
  `IFinanceInvoiceOpenItemAdmissionAuthorityV1` ile her kabulü iki kez çitler (eşlenmemiş kompozisyon = unavailable).
  Hangi yetkinin cevap vereceği ve çağıranın `Producer` ile bağlanması S5 bağlantısında belirlenir. Bağlamdaki şirket ve kullanıcı
  sunucu bağlamına eşit olmalıdır.
- **Taşıma:** ERP-000F dağıtıcısı gelene kadar senkron port (SINAN 10-21 No 55). Dağıtıcıdan sonra aynı payload
  `sales.invoice.finalized.v1` olur; Finans inbox/dedup ile aynı servisi çağırır. Matrah/KDV/tevkifat, belge no/tarih ve
  irsaliye bağları olay payload'unda Accounting için kalır; açık kalem için gerekmez.

## FS-1b · Resmî belge iptali → açık kalemin ters kaydı (SAT-D96, API-11)

- **Finans tarafı:** `IFinanceInvoiceOpenItemCancellationPortV1.CancelAsync(FinanceContext, CommandMetadata,
  FinanceInvoiceOpenItemCancellationV1)` (F2b). Host kaydı, Satış'ın "resmî belge iptal" komutu geldiğinde yapılır.
- **Alanlar:** `CancellationId` (Satış'ın bu iptal kaydı için ürettiği id), `Producer = Sales`, `InvoiceId`,
  `InvoiceVersion` (kabul edilen sürüm), `Reason` (kısa metin, ≤ 512; RED / iptal gerekçesi).
- **Sonuç:** faturanın bütün açık kalemleri silinmez; ilk tutarları kalır, durumları `Cancelled`, kalanları `0` olur ve
  cari ekstrede iptal gününde ayrı bir ters satır ("Satış faturası iptali (ters kayıt)", alacak sütunu) görünür.
- **Ret:** kabul edilmemiş fatura, başka sürüm, başka bir iptal id'siyle zaten iptal edilmiş fatura, ya da kalemlerinden
  birinde aktif dağıtım (tahsilat bağlanmış) varsa `finance_invoice_cancellation_has_allocations`. Dağıtımlı faturanın ne
  olacağı karara bağlı değil (Issue #66 NEEDS-SINAN); o zamana kadar dağıtım önce kendi işlemiyle serbest bırakılır.
- **Idempotency ve işlem sınırı:** FS-1 ile aynı. Öneri anahtar `sales-invoice-cancel:{invoiceId}:{invoiceVersion}`;
  Satış iptalini "Finans iptali bekliyor" durumuyla commit eder, sonra portu aynı anahtarla çağırır (ALS-D64 "ayrı adım");
  başka yetkili kullanıcı yeni anahtar + aynı `CancellationId` ile tekrar gönderir (SAT-D208).
- **Yetki:** host'un sağladığı `IFinanceInvoiceOpenItemCancellationAuthorityV1` ile iki kez çitlenir; hangi yetkinin
  cevap vereceği Satış bağlantısında belirlenir.

## FS-2 · Risk portu (MOD-05)

`ISalesPartyRiskReadPortV1` main'de. Yön filtresi (yalnız alacak kalemleri) #902'de, açık taslak PR; merge'e kadar main
bütün açık resmî taksitleri sayar (bugün borç kaynağı olmadığı için sonuç aynı). Kredi limiti ve teminat
`NoSource` kalır (OPEN-010, Finans API-85).

## FS-3 · Finans → Satış olayları

`finance.open-item.settled.v1`, `finance.payment.recorded.v1`: Finans outbox dilimi (F2-OB) ERP-000F'den sonra.
İptal önizlemesindeki eşleşmemiş avans (API-21) o zamana kadar `NoSource`.
