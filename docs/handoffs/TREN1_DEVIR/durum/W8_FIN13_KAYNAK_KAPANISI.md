# W8 — FIN-S13 özgün B-04 kaynak kapanışı

**DONE (kaynak), runtime kabulü NOT_RUN.** Fatura → 120/320 yevmiye host adaptörü ve `sales.invoice.finalized.v1` / `purchasing.invoice.finalized.v1` tüketicileri, plandaki özgün B-04 sözleşmesine göre incelendi. İlk SMM, Mixed veya üretim maliyet atfı bu dilimin kapsamına eklenmedi.

- Gerçek Purchasing incoming invoice kesinleşmesi outbox'ı aynı işlemde yazar; son yetki kaybı rollback kaynağı `87fac44cd`. Sales gerçek kesinleşme kaynağı `fdc417b38`. Accounting source admission/voucher ve onaylı fiş kaynakları `3044b80fb`, `d2c99cd81`, `118adcab2`; tamamı merkezi head'in atası, `git merge-base --is-ancestor` exit0.
- Program `AddAccountingInvoiceJournal`, `MapAccountingSourceVouchersV1` ve `AddSalesFinanceCancellationHost` bağlarını taşır. TrainCompletionHostComposition iki gerçek finalized event kaynağını, iki consumer ve cancellation consumer'ı mevcut Work registry'ye bağlar. Dispatcher/production açılmadı.
- Gerçek source voucher onayı güncel yetkili, farklı hazırlayan/onaylayan ve açık dönem ile gerçek Accounting posting owner'ını kullanır. Seri/idempotency/version/audit ve iptal kanıtı çitleri mevcut kaynakta korunur; somut hesap veya rol uydurulmaz.
- `SourceVouchersPage`, `sourceVoucherApi`, mevcut Accounting route/capability/nav üzerinden gerçek liste/onay/ters kayıt kaynağına bağlıdır. Ayrı kullanıcı oturumu veya gerçek sağlayıcı kabulü iddia edilmez.
- Son birleşik `65618ac1ec70b97d2a5d05bc6f6cf05e2f2dcea9` kaynağında API, Accounting.Unit, Sales.Unit, Integration ve TSC kaynak derlemeleri exit0; runtime Unit/SQL/HTTP/Vitest/browser/CI ve pin üretimi ayrı bekler. Bu kaynaklarda özgün FIN-S13 için somut kod eksiği bulunmadı; eski header/rapor bekleyişleri güncel kayıtların yokluğu sayılmadı.
- Plan 166/174 (%95,4) yerel kaynak DONE; 8 açık. Yayımlanmış tren135/174 (%77,6), FF henüz yapılmadı. W8 son pakettir; yeni dalga açılmadı. Final exact-head K3 ve insan/sağlayıcı kapıları değişmez.
