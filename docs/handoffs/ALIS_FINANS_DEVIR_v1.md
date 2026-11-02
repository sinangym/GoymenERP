# Alış → Finans görev notu v1

Tarih: 2026-11-02 · Yazan: Alış şeridi (Claude) · Alıcı: Finans modülünü yürüten konuşma
Kararlar: `docs/decisions/SINAN_KARARLARI_2026-11-02_ALIS_EK3.md` ALS-D41 (TL borç tedarikçi faturasındaki kurla),
ALS-D42 (tedarikçi borcu satış riskinden düşülmez); `_ALIS.md` ALS-D6 (kur); `_ALIS_EK4.md` ALS-D44 (borç/alacak
ayrımı Finans konuşmasında); `_ALIS_EK7.md` ALS-D50 (avans Finans'ta ödeme olarak); `_ALIS_EK9.md` ALS-D59 (borç Finans'ta tek kayıt; Cari
kartına hareket yok; 320 fişi aynı olaydan Muhasebe'de), ALS-D62 (dağıtıcı gelene kadar doğrudan kabul portu, sonra
olay), ALS-D64 (önce kaydet, sonra idempotent çağrı; tek DB işlemi yok; **açık çelişki:** koordinatör aktarımı "tek DB
işlemi" — NEEDS-SINAN https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6590568009). Alış şeridi Finans dosyalarını
değiştirmez. Bu not iş tanımıdır; Finans'ın kendi karar, review ve merge kapıları geçerlidir. Arayüz ve olay adları
öneridir. Bağlam: `docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md` §6 MOD-04/MOD-15, §14; Finans planı
`docs/delivery/FINANS_HAZIRLIK_PLANI_v1.md` F1/F2/F3.

## F-A1 · Açık kalemde yön (MOD-15) — #902'nin merge'i

- **Bugün (main `e7a980c3`):** açık kalemde yön yok; kaynak türü yalnız `official.invoice-installment.v1`
  (`FinanceOpenItemSourceV1.cs:10`). `FinanceOpenItemDirectionV1` ve rezerve tür
  `official.incoming-invoice-installment.v1` yalnız açık taslak PR #902'de.
- **İstenen (Finans):** #902'nin kendi kapılarıyla (K3, iki bağımsız exact-head review) merge edilmesi. Alış A5
  (gelen fatura kesinleştirme) bu olmadan açılmaz; Alış portu o zamana kadar fail-closed kalır.

## F-A2 · Gelen fatura kabul portu (MOD-04; Finans planı F2, API-10 `IFinanceInvoiceOpenItemAdmissionPortV1`)

- **Bugün:** Alış'ta gelen fatura çekirdeği, olayı ve outbox'ı yok (Alış A5-0 ve A5'te gelir). Finans'ta gelen
  yönlü kabul portu yok.
- **İstenen (Finans):** Finans planının adlandırdığı `IFinanceInvoiceOpenItemAdmissionPortV1` (API-10, Alış ve Satış
  ortak; ERP-000F yokken senkron port, dağıtıcı gelince aynı içerik `purchasing.invoice.finalized.v1` olayıyla) her
  taksit için bir **borç** açık kalemi (`official.incoming-invoice-installment.v1`) açar. Alış'ın göndereceği alanlar (öneri):
  1. şirket (`CompanyId`), tedarikçi taraf/hesap kimliği (Cari);
  2. fatura kimliği + fatura sürümü;
  3. para birimi ve **ödenecek tutar** (KDV dahil, tevkifat düşülmüş);
  4. TL karşılığı (`FunctionalAmount`, Alış calculator'ından) + **tedarikçi faturasındaki kur** (`ExchangeRate`;
     ALS-D41; TL borç bu kurla). ERP kontrol kuru (ALS-D6/D14) açık kaleme **alan olarak gönderilmez**; Alış'ın
     fatura snapshot'ında (A5-KUR) ve olay payload'unda kalır (EK9 ALS-D62 yorumu);
  5. taksitler: sıra no, vade tarihi, tutar (Σ taksit = ödenecek tutar).
- **Taşıma ve işlem sınırı (ALS-D62, ALS-D64):** ERP-000F gelene kadar **doğrudan port**: Alış faturayı önce kendi
  işleminde **"Finans kabulü bekliyor"** durumuyla kaydeder, sonra `IFinanceInvoiceOpenItemAdmissionPortV1.AdmitAsync`'i
  hemen çağırır; Finans kendi işleminde (ayrı veritabanı bağlamı) yazar. Hata/zaman aşımında aynı anahtarla yeniden
  denenir. İki modül tek DB işleminde bağlanmaz (ADR-CARI-001). Dağıtıcı gelince aynı içerik
  `purchasing.invoice.finalized.v1` olayıyla gider (bire bir eşleme). Ayrıntılar A5 sözleşmesinde Finans ile yazılır.
  **Açık çelişki (NEEDS-SINAN):** koordinatör Sinan adına "tek veritabanı işlemi; fatura kesinleşmesi ve borç
  kalemi birlikte commit" aktardı (https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6590513683 madde 2); Sinan'ın doğrudan cevabı ALS-D64. Soru: https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6590568009. Cevaba kadar
  ALS-D64 geçerli (ALS-D57); A5 kabul sözleşmesi kilitlenmez.
- **Idempotency:** fatura kimliği + sürüm üzerinden; anahtar önerisi `purchasing-invoice:{invoiceId}:{invoiceVersion}`
  (Finans FS-1 `sales-invoice:` deseni; teknik ayrıntı). Aynı anahtar ikinci kez gelirse ikinci kalem açılmaz
  (inbox/dedup); bu yüzden yeniden denemede çifte borç oluşmaz.
  Faturanın iptali/ters kaydı ayrı sürümlü olayla gelir (Alış A5'te ayrıca önerilir).
- **Alış tarafı:** dağıtıcıya kadar doğrudan port çağrısı, sonra olay Alış outbox'ından çıkar (MOD-12, plan §14.1
  A5-0); Finans tablosuna Alış yazmaz (işlem sınırı yukarıdaki açık çelişkiye bağlı). Borç Finans'ta tek kayıttır; Cari kartına ayrıca hareket yazılmaz; 320 fişi
  aynı kaynak olaydan Muhasebe'de üretilir (ALS-D59).

## F-A3 · Ödeme amacı (Finans planı TEK-02, F1) ve tedarikçi ödemesinin borca dağıtımı (API-20, F3)

- **Bugün:** `ManualBankPaymentV1.cs` içindeki `ManualBankPaymentPreparation` taraf (`PartyId`) taşıyor ama borç açık kalemine dağıtım ve ödeme amacı
  (tedarikçi ödemesi / müşteriye iade) yok; Finans planı D-7'ye göre risk portu aynı hesaba yapılan çıkan ödemeyi
  müşteriye iade sayıyor.
- **İstenen (Finans):** çıkan ödemeye amaç alanı (Finans planı TEK-02, F1) ve tedarikçi ödemesinin borç kalemine
  dağıtılması (Finans planı API-20, F3),
  böylece ALS-D42'ye uygun olarak tedarikçi ödemesi müşteri riskine karışmaz. Alış "ödendi / kısmi ödendi"
  görünümünü Finans'ın olayından okur (Finans planı MOD-03, `finance.open-item.settled.v1` önerisi).

## F-A4 · Avans (ALS-D50)

Tedarikçiye avans Alış'ta kayıt türü değildir; Finans'ta ödeme olarak yapılır ve fatura gelince kaleme aktarılır
(Finans planı API-21). Alış yalnız siparişte avans bilgisini Finans'tan okuyarak gösterebilir; ayrı tasarım Finans'ındır.
