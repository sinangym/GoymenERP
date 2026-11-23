# Alış-Cari — Dalga 6 kaynak raporu

## Değişiklikler

| Dilim / karar | Durum | Commit ve kapsam |
|---|---|---|
| ALC-S5 / W5 şirket izolasyonu | PARTIAL | `f63872565`: mal kabul ve tedarikçi iade kimliği probe'larından IgnoreQueryFilters kaldırıldı. Başka şirketin kimliği okunmaz; global kimlik çakışmasını DB unique constraint'i concurrent-change olarak reddeder. Gerçek SQL kaynak testi iki şirketi ve aynı kimlikli yarış reddini kapsar. İade maliyet kaynağı ve kullanıcı akışının kalanını tamamlamaz. |
| F17 teslimat takvimi owner portu | SOURCE_READY | `a59c69bdc`: `IPurchaseFutureSupplyScheduleReadPortV1`. Güncel revizyon takvimi ve kabul düşüldükten sonraki kalanlar saklanan SliceNo sırasında döner (ALS-D81). Takvimsiz satır okunabilir ve tarihsizdir; kaynak hatası Unreadable olur. Yazma veya yeni authority yoktur. Stok talep azaltma tarih sırasına host köprüsü entegrasyonda bağlanır. |
| ALS-D116 / W5-5.1 | KAYIT TAMAM | `ce3f72498`: `purchasing.goods-receipts.approve-overdelivery`, şirket geneli, Critical değil. OrdersApprove ve ApproveVariance'dan ayrıdır. Yalnız kayıt; fazla teslim onay iş akışı henüz bu izinle uygulanmadı. Grant/rol yoktur. |

`f63872565` commit mesajındaki DEC-SEC-001 atfı sicilde bulunmayan bir etiket hatasıdır;
dayanak AGENTS CompanyId/modül sınırı ve ADR-002 şirket izolasyonudur. Bu kayıt yeni karar
uydurmaz; geçmiş commit değiştirilmedi. Source/test gövdeleri ilgili şirket sınırını korur.

## Yazılan gerçek SQL test kaynakları

- `GoymenERP.Api.IntegrationTests.PurchasingSqlServerRelationalAcceptanceTests.Receipt_and_return_identity_probes_do_not_disclose_other_companies_and_global_collisions_still_refuse`.
- `GoymenERP.Api.IntegrationTests.PurchasingSqlServerRelationalAcceptanceTests.Future_supply_schedule_uses_latest_revision_and_subtracts_receipts_in_slice_order_without_writing`.
- `GoymenERP.Api.IntegrationTests.PurchasingSqlServerRelationalAcceptanceTests.A_future_supply_line_without_a_schedule_is_readable_and_undated_but_an_unresolved_company_is_not`.

İlişkisel envanter ve fingerprint/pin'ler merkezi entegrasyonda üretilir. Yeni migration yoktur.
Api.IntegrationTests ön derlemesi `a59c69bdc` kaynağında exit 0, sıfır hata/uyarı verdi;
sonraki izin kaydı birleşik head'de yeniden derlenmelidir. Test/gerçek SQL/CI/Vitest/tarayıcı
koşumu **NOT_RUN**; kaynak testi kabul kanıtı değildir.

## Kalanlar

- ALC-S7 fason hizmet siparişi ve ALC-S8 ek alış masrafı kaydı: NOT_STARTED. Ayarlar listesi
  owner portu hazırdır; TK-43 işlem türü listesini kapalı tutar, ALS-D79 ortak masraf yapısı
  NEEDS-SPEC olmaya devam eder. Ek masraf ortalama maliyete bağlanmaz (ALS-D80).
- ALC-S14 Cari v2, ALC-SC47 alış siparişinde cari revizyonu, ALC-S5'in kalan maliyet/arayüz
  kabulü ve W7 ALC-S6 canlanma kuralları: tamamlanmadı.
- FNS-D105 purchasing.invoice.finalized olayının üreticisi ve alış faturası çekirdeği eksiktir.
  Boş olay veya başarılıymış gibi bir fiş üretilmedi.
- W5'in Purchasing shell unit beklentisi merkezi provider/pin üretiminden sonra hizalanmalı.
- Bypass/context construction için önceki “K3 review bekliyor” listesi korunur.
- Yasak yüzey, grant/rol, OPEN, production ve Finance STOP değiştirilmedi.
