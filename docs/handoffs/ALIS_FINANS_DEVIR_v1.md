# Alış → Finans görev notu v1

Tarih: 2026-11-02 · Yazan: Alış şeridi (Claude) · Alıcı: Finans modülünü yürüten konuşma
Kararlar: `docs/decisions/SINAN_KARARLARI_2026-11-02_ALIS_EK3.md` ALS-D41 (TL borç tedarikçi faturasındaki kurla),
ALS-D42 (tedarikçi borcu satış riskinden düşülmez); `_ALIS.md` ALS-D6 (kur); `_ALIS_EK4.md` ALS-D44 (borç/alacak
ayrımı Finans konuşmasında); `_ALIS_EK7.md` ALS-D50 (avans Finans'ta ödeme olarak). Alış şeridi Finans dosyalarını
değiştirmez. Bu not iş tanımıdır; Finans'ın kendi karar, review ve merge kapıları geçerlidir. Arayüz ve olay adları
öneridir. Bağlam: `docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md` §6 MOD-04/MOD-15, §14; Finans planı
`docs/delivery/FINANS_HAZIRLIK_PLANI_v1.md` F1/F2/F3.

## F-A1 · Açık kalemde yön (MOD-15) — #902'nin merge'i

- **Bugün (main `732bc1cc`):** açık kalemde yön yok; kaynak türü yalnız `official.invoice-installment.v1`
  (`FinanceOpenItemSourceV1.cs:10`). `FinanceOpenItemDirectionV1` ve rezerve tür
  `official.incoming-invoice-installment.v1` yalnız açık taslak PR #902'de.
- **İstenen (Finans):** #902'nin kendi kapılarıyla (K3, iki bağımsız exact-head review) merge edilmesi. Alış A5
  (gelen fatura kesinleştirme) bu olmadan açılmaz; Alış portu o zamana kadar fail-closed kalır.

## F-A2 · Gelen fatura kabul portu (MOD-04; Finans planı F2, API-10…12)

- **Bugün:** Alış'ta gelen fatura çekirdeği, olayı ve outbox'ı yok (Alış A5-0 ve A5'te gelir). Finans'ta gelen
  yönlü kabul portu yok.
- **İstenen (Finans):** `purchasing.invoice.finalized.v1` olayını tüketen bir kabul portu; her taksit için bir
  **borç** açık kalemi (`official.incoming-invoice-installment.v1`) açar. Alış'ın göndereceği alanlar (öneri):
  1. şirket (`CompanyId`), tedarikçi taraf/hesap kimliği (Cari);
  2. fatura kimliği + fatura sürümü;
  3. para birimi ve **ödenecek tutar** (KDV dahil, tevkifat düşülmüş);
  4. TL karşılığı + kullanılan kur (ALS-D41: tedarikçi faturasındaki kur; kur kaynağı ALS-D6 ile, snapshot olarak);
  5. taksitler: sıra no, vade tarihi, tutar (Σ taksit = ödenecek tutar).
- **Idempotency:** fatura kimliği + sürüm üzerinden; aynı olay ikinci kez gelirse ikinci kalem açılmaz (inbox/dedup).
  Faturanın iptali/ters kaydı ayrı sürümlü olayla gelir (Alış A5'te ayrıca önerilir).
- **Alış tarafı:** olay Alış outbox'ından çıkar (MOD-12, plan §14.1 A5-0); Finans tablosuna Alış yazmaz.

## F-A3 · Tedarikçi ödemesinin borca dağıtımı ve ödeme amacı (Finans planı F3, TEK-02)

- **Bugün:** `ManualBankPaymentV1` taraf (`PartyId`) taşıyor ama borç açık kalemine dağıtım ve ödeme amacı
  (tedarikçi ödemesi / müşteriye iade) yok; Finans planı D-7'ye göre risk portu aynı hesaba yapılan çıkan ödemeyi
  müşteriye iade sayıyor.
- **İstenen (Finans):** tedarikçi ödemesinin borç kalemine dağıtılması ve ödeme amacı alanı (Finans planı TEK-02),
  böylece ALS-D42'ye uygun olarak tedarikçi ödemesi müşteri riskine karışmaz. Alış "ödendi / kısmi ödendi"
  görünümünü Finans'ın olayından okur (Finans planı MOD-03, `finance.open-item.settled.v1` önerisi).

## F-A4 · Avans (ALS-D50)

Tedarikçiye avans Alış'ta kayıt türü değildir; Finans'ta ödeme olarak yapılır ve fatura gelince kaleme aktarılır
(Finans planı API-21). Alış yalnız siparişte avans bilgisini Finans'tan okuyarak gösterebilir; ayrı tasarım Finans'ındır.
