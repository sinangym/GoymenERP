# TREN-1 W8 Finans

Sabit kapsam: FIN-S13, FIN-S14, FIN-S17, FNS-SC32, FIN-SC36, FIN-SC47-KREDI.
Bu rapor kaynak geliştirmesini, derlemeyi ve çalıştırılmış kabul kanıtını ayrı tutar.

## FIN-S13 — PARTIAL

Kararlar: FNS-D105, KLN-D6/D7/D8/D10/D11/D13. Dilim commit'i bu raporla aynı kaynak checkpoint'indedir.

- Accounting alış faturası kabulünü kendi portu ve işlem sınırı içinde uygular. Açık satır amacı üretim girdisi / ticari mal / hizmet eşlemesini seçer; hesap kodu tahmin edilmez.
- Alış yevmiye taslağı net tutarları stok/gider borcuna, tam KDV'yi indirilecek KDV borcuna, tevkifatı ayrı tevkifat borcu alacağına ve ödenecek tutarı satıcı alacağına taşır. Eksik eşleme aynı kaynak anahtarıyla bekler.
- `purchasing.invoice.finalized.v1` tüketicisi belge tarihi, gerçek kesinleştiren kullanıcı, kaynak/fonksiyonel tutarları ve şirketi doğrular. Aynı olay/sürüm ikinci fiş üretmez. Muhasebe host yetkisi ve dispatcher etkinleştirilmedi.
- Kaynak testleri: birim kabul/consumer, host bağlama ve gerçek SQL kabul fact'i yazıldı. SQL fact: `GoymenERP.Api.IntegrationTests.AccountingSourceDocumentAdmissionSqlServerRelationalAcceptanceTests.Purchase_invoice_waits_for_its_own_mapping_then_commits_one_balanced_draft_and_replays_without_duplicate`.
- Migration yok; yeni THROW yok. API, Accounting.UnitTests ve IntegrationTests kaynak derlemeleri exit 0. Tevkifat eşleme seçicisi kaynak incelemesinde düzeltildikten sonra Accounting.UnitTests tekrar derlemesi exit 0.
- Kalan: Purchasing gerçek kesinleştirme producer'ı ve host Work aboneliği koordinatör entegrasyonunda; kaynak-kullanıcı akışı ile kapanış testleri henüz NOT_RUN. Bu nedenle DONE değildir.

## Diğer finans dilimleri

Mevcut dilimler üzerinde çalışma sürüyor. Kredi grupları araştırmasında resmî ERP belgeleri kaynak belge/plan/gerçek hareket ayrımını destekler; veri yokluğu sıfır sayılmaz. Kullanıcının son iki cevabı, para biriminin Settings profilindeki en küçük birim toleransını ve komisyon/masrafın kaynak belgede takside dahil ya da ayrı gösterilmesine uyulmasını kilitler. Yeni oran, hesap eşlemesi veya grant türetilmez.

Tam test, gerçek SQL, arayüz kabulü, CI ve bağımsız K3 incelemesi: NOT_RUN.
