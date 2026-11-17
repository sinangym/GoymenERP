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

## FIN-SC47-KREDI — PARTIAL

Kararlar: FNS-D88/D93/D94/D99/D100/D106/D109; 17 Kasım kullanıcı araştırma/uygulama talimatı ve para biriminin en küçük birimi ile kaynak belgedeki dahil/ayrı masraf cevapları.

- Dört grubun ayrı belge profilleri, kaynak referansı/tarihi/revizyonu, gerçek Cari `PartyId` + `CompanyPartyAccountId` kimliği ve owner sürümleri yazıldı. Yetkili owner okuması şirketi/aktörü her await sonrasında tekrar doğrular; banka rolü ve hayali master oluşturmaz.
- Taksitli plan ile kaynak hareketleri ayrıdır. Gayrinakdi/plansız kayda sahte taksit veya sıfır plan toplamı eklenmez. Çek/senet bağlantısı Finance sahibinden doğrulanır; rücu seçimi evrak riskini kaldırmaz.
- Settings'in gerçek para profili ve sürümü saklanır; satır toleransı bu profilin en küçük birimidir. Komisyon/masraf yalnız kaynakta takside dahil denmişse bileşen toplamına girer; ayrı tahsilat ayrı kalır. Kaynak metinler yuvarlanmaz, faiz/vergi/kur hesaplanmaz. Eksik para profili veya Cari owner cevabı başarılı sayılmaz.
- `AddCreditSourceProfiles` EF ile üretildi; source proof veya masraf kaydı varken Down ilk işlemde `THROW 51961` ile durur. Designer/snapshot üreticiden gelir. Yeni permission/grant yok.
- Kaynak testleri: minimum birim ölçekleri, ayrı/dahil masraf, gayrinakdi plansız öneri/onay, eksik owner/profil, değişen profil, pasif lender, model/snapshot/Down ve gerçek SQL constraint/kalıcılık fact'i. API ön kaynak derlemesi, migration/V2 bağlantısı sonrası Finance.UnitTests ve IntegrationTests kaynak derlemeleri exit 0; uyarı/hata 0. Çalıştırılmış test/SQL kanıtı yoktur.
- Merkez entegrasyon: `AddFinanceCreditLenderOwnerRead`; Settings V2 para adayları ve gerçek profil okuması `finance.credit-plan` işlemine bağlıdır. Profil yoksa fallback yoktur.
- Yeni SQL fact: `GoymenERP.Api.IntegrationTests.FinanceCreditAccountSqlServerRelationalAcceptanceTests.Credit_source_profile_Down_refuses_protected_rows_and_database_checks_keep_lender_proof_and_charge_json_complete`. Migration SQL mutation sayısı 1; mimari, recovery ve ilişkisel envanter üretimi merkezi entegrasyondadır.
- Kalan: gruba ait gerçek kullanım/ödeme/tahakkuk fiş önerileri, banka+tür limit uyarısı, kapalı dönem düzeltmesi, ekranlar ve kullanıcı akışı. Bu checkpoint bu dilimi DONE yapmaz.

## Diğer finans dilimleri

### FIN-S14 bağlantısı — üretimde önceden sevk edilmiş bölümün SMM önerisi

Kararlar: URT-D67/D78/D79/D80 ve 17 Kasım tüketilen bölüm için ayrı SMM önerisi, açık dönem/tarih seçimi ve farklı muhasebe onaylayanı cevapları. Dilim bütünü PARTIAL'dır; bu kayıt bir kaynak checkpoint'idir.

- Gerçek `inventory.outbox.v1` / `inventory.manufacturing-cost-difference.proposed.v1` schema 1 olayını şirket, olay kimliği, maliyet nesli, miktarlar ve kaynak tutarlar ile kabul eden Accounting inbox yazıldı. Replay yeni öneri/fiş üretmez; eksik tutar sıfır sayılmaz, USD muhasebe tutarına katılmaz.
- Kaynak olay kimliğiyle kalıcı öneri oluşur. Muhasebe hazırlayanı açık dönem/tarih, mevcut onaylı hesap planından iki hesap ve gerekçe seçer. Farklı muhasebe yetkilisi onaylar; hesap planı ve şirket/yetki tekrar okunur. Onayda tek Accounting işlemi yevmiye, delivery, öneri kararı, audit ve idempotency makbuzunu birlikte kaydeder. Son yetki kaybında hepsi geri alınır.
- Stok sahibi tüketilen payı bir kez yuvarlar: 100,01 TL / 3 kg örneğinde 33,34 TL tüketilen ve 66,67 TL kalan. Accounting kaynakta gelen 33,34 TL'yi aynen kullanır; saklanan kg maliyetinden yeniden türetmez ve ikinci yuvarlama yapmaz. Negatif ters fark da kaynak işaretiyle korunur; karşı hesap tahmin edilmez.
- Hazırlama ve onay ekranları, kaynak tutarları ve dondurulmuş hesap planı kod/ad/sürüm/hash kanıtını gösterir. Aynı kişi onaylayamaz; çevrimdışı/503/bozuk yanıt sonrası aynı istek anahtarı korunur. Yeni yetki/grant yok; mevcut muhasebe düzeltme izinleri kullanılır.
- EF migration: `20261117000534_AddManufacturingCostDifferenceProposals`; herhangi bir kaynak/karar varken Down ilk işlemde `THROW 51962`. Designer/snapshot üreticiden gelir.
- Birim, model, Vitest ve dört gerçek SQL fact kaynağı eklendi; onay yarışı, geç yetki kaybı, kapalı dönem, source yuvarlama ve Down korumasını kapsar. API, Accounting.UnitTests, Finance.UnitTests, IntegrationTests derlemeleri exit 0, uyarı/hata 0; TypeScript app/node ayrı `tsc -p` derlemeleri exit 0. Runtime test/SQL/Vitest/browser/CI: NOT_RUN.
- Merkez entegrasyon: `AddWorkInboxConsumer<ManufacturingCostDifferenceInboxConsumerV1>(ManufacturingCostDifferenceInboxV1.ConsumerName, ManufacturingCostDifferenceInboxV1.Subscriptions)`. Accounting shell sözleşmesi 1.5; `accounting.manufacturing-cost-differences.prepare` ve `.approve` merkezi pin/menü kaydı bekler. Stock gerçek producer/dispatcher bağlantısı koordinatör entegrasyonundadır. Henüz kullanılabilirlik/kabul iddiası yoktur.

Mevcut dilimler üzerinde çalışma sürüyor. Kredi grupları araştırmasında resmî ERP belgeleri kaynak belge/plan/gerçek hareket ayrımını destekler; veri yokluğu sıfır sayılmaz. Kullanıcının son iki cevabı, para biriminin Settings profilindeki en küçük birim toleransını ve komisyon/masrafın kaynak belgede takside dahil ya da ayrı gösterilmesine uyulmasını kilitler. Yeni oran, hesap eşlemesi veya grant türetilmez.

Tam test, gerçek SQL, arayüz kabulü, CI ve bağımsız K3 incelemesi: NOT_RUN.
