# Finans → Satış görev notu v1

Tarih: 2026-11-02 · Yazan: Finans şeridi (Claude) · Alıcı: Satış modülünü yürüten konuşma
Kararlar: FNS-D2, FNS-D7, FNS-D14 (`docs/decisions/SINAN_KARARLARI_2026-11-02_FINANS.md`); SAT-D85/D86/D96/D172;
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
  payload tekrar oynatılır; aynı fatura aynı satırlarla yeniden gelirse ikinci kalem açılmaz; farklı satırla
  `finance_invoice_already_admitted` döner. Değişen fatura iptal + yeniden kesimdir (SAT-D96, Finans F2b).
- **Yetki:** port kendi yetkisini taşımaz; Satış kendi kesinleştirme yetkisini ölçer. Bağlamdaki şirket ve kullanıcı
  sunucu bağlamına eşit olmalıdır.
- **Taşıma:** ERP-000F dağıtıcısı gelene kadar senkron port (SINAN 10-21 No 55). Dağıtıcıdan sonra aynı payload
  `sales.invoice.finalized.v1` olur; Finans inbox/dedup ile aynı servisi çağırır. Matrah/KDV/tevkifat, belge no/tarih ve
  irsaliye bağları olay payload'unda Accounting için kalır; açık kalem için gerekmez.

## FS-2 · Risk portu (MOD-05)

`ISalesPartyRiskReadPortV1` main'de; F1 (#902) ile yalnız alacak kalemlerini sayar. Kredi limiti ve teminat
`NoSource` kalır (OPEN-010, Finans API-85).

## FS-3 · Finans → Satış olayları

`finance.open-item.settled.v1`, `finance.payment.recorded.v1`: Finans outbox dilimi (F2-OB) ERP-000F'den sonra.
İptal önizlemesindeki eşleşmemiş avans (API-21) o zamana kadar `NoSource`.
