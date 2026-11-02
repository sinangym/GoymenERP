# Finans → Alış görev notu v1

Tarih: 2026-11-02 · Yazan: Finans şeridi (Claude) · Alıcı: Alış modülünü yürüten konuşma
Kararlar: FNS-D2, FNS-D7, FNS-D8, FNS-D14 (`docs/decisions/SINAN_KARARLARI_2026-11-02_FINANS.md`); ALS-D41/D42/D44/D50.
Bağlam: `docs/delivery/FINANS_HAZIRLIK_PLANI_v1.md` §6 MOD-01…03, §18; Alış notu `docs/handoffs/ALIS_FINANS_DEVIR_v1.md`
(F-A1, F-A2). Finans şeridi Alış dosyalarını değiştirmez. Bu not iş tanımıdır; Alış'ın kendi kapıları geçerlidir.

## FA-1 · Açık kalemde yön (F-A1)

#902 (MOD-15): `FinanceOpenItemDirectionV1`; gelen fatura taksiti `official.incoming-invoice-installment.v1` → borç
(Payable). Alış A5 bu merge'den sonra açılır.

## FA-2 · Gelen fatura kabul portu (F-A2, API-10)

Satış ile aynı port: `IFinanceInvoiceOpenItemAdmissionPortV1`, `Producer = Purchasing`. Alanlar, idempotency ve taşıma
`docs/handoffs/FINANS_SATIS_DEVIR_v1.md` FS-1 ile aynıdır. `ExchangeRate` tedarikçi faturasındaki kurdur (ALS-D41);
`FunctionalAmount` Alış calculator'ından gelir. Tedarikçi borcu satış riskinden düşülmez (ALS-D42).

## FA-3 · Sipariş başlığında bakiye (MOD-02)

`GET /api/v1/finance/party-balance?partyId&companyAccountId` (API-04, #912): para birimi bazında Toplam Borç / Toplam
Alacak ve net. Ekranı Alış tüketir.

## FA-4 · Ödendi / avans (MOD-03)

Tedarikçi ödemesinin borç kalemine dağıtımı Finans F3'tedir (API-20); avans ALS-D50 ile Finans'ta ödeme olarak
tutulur. Durum olayı `finance.open-item.settled.v1` Finans outbox dilimi (F2-OB, ERP-000F sonrası).
