# TREN-1 W8 Finans

Sabit kapsam: FIN-S13, FIN-S14, FIN-S17, FNS-SC32, FIN-SC36, FIN-SC47-KREDI.
Bu rapor kaynak geliştirmesini, derlemeyi ve çalıştırılmış kabul kanıtını ayrı tutar.

## FIN-SC36 — PARTIAL, resmî belge kaynak tarihi ve TL karşılığı

Kararlar: FNS-D76/D89/D90, KLN-D104 ve resmî belge kaynağını esas alma cevapları.

- Yeni elle kayıtta gerçek fatura tarihi zorunludur. Eski tarihsiz kayıtlar nullable kalır; kayıt saati fatura tarihi yerine geçirilmez. `AddOfficialSalesInvoiceSourceDate` EF ile üretildi; gerçek tarihli kayıt varken Down ilk işlemde `THROW 51965` ile reddeder.
- CSV v3 gerçek fatura tarihini ve kaynakta varsa TL karşılığını taşır. Eksik TL karşılığı null kalır; kurla veya sıfırla tamamlanmaz. V1/V2 sözleşmeleri korunur. Güncel onaylı ithal kaynağın sahibi, tam resmî numara/ETTN çifti, belge tarihi ve immutable dosya özeti owner okumasında korunur; sahte PDF kimliği üretilmez.
- Bekleyen elle kayıt ithali engellemez. Ayrı yetkili elle kaydı onaylayınca o kaynak esas alınır; kaynak farkları alan adıyla detayda ve onay yanıtında görünür, bir kez denetlenir. İthal belge ve eski hareket değiştirilmez. Bozuk kaynak veya sonradan yetki kaybı başarılı sonuç sayılmaz.
- İthal kaynak ekranı belge tarihi, bilinmeyen TL, bekleyen elle kayıt ve esas alınan elle kaynak farklarını gösterir. Para değerleri string doğrulanır; sayıya dönüşmüş para veya başka kaynak belge kimliği reddedilir.
- API, Finance.UnitTests ve IntegrationTests kaynak derlemeleri exit 0; son kaynak-fark testi eklemesi kredi hareketi checkpoint'indeki derlemeyle tekrar doğrulandı. TypeScript app/node derlemeleri exit 0. Birim, HTTP, sözleşme, Vitest ve gerçek SQL fact kaynakları yazıldı; runtime test/SQL/CI NOT_RUN.
- SQL fact: `GoymenERP.Api.IntegrationTests.FinanceOfficialSalesInvoiceSqlServerRelationalAcceptanceTests.Official_source_date_persists_and_its_Down_refuses_loss_of_known_dates`.
- Kalan: Sales owner tarih alanı merkez entegrasyonunda taşınacak; elle resmî fatura kullanıcı ekranı tamamlanacak. Bu checkpoint tüm FIN-SC36 dilimini DONE yapmaz.

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

## FIN-SC47-KREDI — PARTIAL, kaynak öneri/onay ekranı

- Dört grubun kaynak alanları, gerçek Cari şirket hesabı seçimi, Settings para adayları, banka planının CSV ile aktarımı ve elle kaynak satır girişi yazıldı. Masrafın takside dahil veya ayrı olduğu kullanıcı tarafından açıkça seçilir; seçili varsayılan yoktur. Tutar metni korunur, ekran hesaplama yapmaz.
- Öneri, farklı yetkilinin onayı ve gerekçeli ret aynı backend sözleşmesini kullanır. Eski profili eksik kayıt başarı varsayılmaz. Bilinmeyen yazma sonucu formu kilitler; aynı oturum/şirket bağlamında sayfa yeniden açılınca aynı anahtar ve gövdeyle sorgulanır.
- Para tutarı sayı olarak gelirse, kaynak profili başka gruba aitse veya şirket farklıysa yanıt reddedilir. Loading/empty/not-found/403/offline/503/malformed/conflict durumları ortak shell durumlarıdır. Kaynak testleri bu sınırlar, planın byte-değerli tutarları, self-approval ve remount sonrası aynı komut için yazıldı.
- Finance shell sözleşmesi 1.14; `finance.credits.workspace` yolu `/finans/krediler`, öneri/onay kontrolleri mevcut kredi izinlerini ister. Yeni üç capability `ImplementationAvailable=false`; aktivasyon veya grant yapılmadı. Merkez React mount/menü/pin koordinatörde.
- TypeScript app kaynak derlemesi exit 0. Shell kaynak testi güncellendi; .NET derleme slotu bekleniyor. Runtime Vitest/browser NOT_RUN. Kalan gerçek hareket, limit uyarısı ve fiş önerisi işleri nedeniyle FIN-SC47-KREDI hâlen PARTIAL.

Tam test, gerçek SQL, arayüz kabulü, CI ve bağımsız K3 incelemesi: NOT_RUN.

## FIN-SC47-KREDI — PARTIAL, gerçekleşmiş hareket ve muhasebe önerisi

- Kullanım, geri ödeme, faiz tahakkuku ve açılış kaynakları planın yanında ayrı kaydedilir. Kaynak belge/revizyon, tarihler, bileşen tutarları, varsa gerçek banka hesabı ve belgede açık net tutar korunur. Plan onayı para hareketi yaratmaz. Eksik TL sıfır veya hesaplanmış kurla tamamlanmaz; belge tutarları yuvarlanmaz.
- Öneri ve ayrı kişinin kararı şirket/yetki, sürüm, idempotency, Settings profil sürümü ve açık muhasebe dönemi çitlerinden geçer. Onaylı gerçek banka bacağı iç defterde bir kez okunur; ayrıca kopya tahsilat/ödeme üretilmez. Geri ödeme aynı SERIALIZABLE işlem içinde gerçek iç defter bakiyesini aşamaz.
- Kapalı açılış dönemi yeniden açılmaz. Kullanıcının seçtiği açık muhasebe tarihindeki düzeltme, orijinal kaynağı koruyan ters olay ve yeni kaynak olayını aynı işlemde kaydeder. Giren onaylayamaz; değişen kaynak/sürüm/profil kabul edilmez.
- Finance transactional outbox kaynağı `finance.outbox.v1`, olay `finance.credit-movement.recorded.v1` schema 1'dir. Kalıcı rowversion imleci daha erken henüz commit edilmemiş kaydı atlamaz. Sabit `MIN_ACTIVE_ROWVERSION()` metadata sorgusu için **K3 review bekliyor**; tenant filtresi bypass edilmez ve guard izin listesi değiştirilmedi.
- Accounting kendi immutable kaynak/de-dup kaydını tutar. TK-30 uyarınca eşleme boşsa `MappingMissing` bekler; kaynak ekranında eksik eşleme ve bilinmeyen TL görünür. Henüz seçilmemiş karşı hesap, bileşen eşlemesi, oran veya fiş yazma yetkisi türetilmez. Bekleyen öneri yevmiye/iş başarısı sayılmaz.
- Hareket öneri/onay/ret ve açılış düzeltme ekranları gerçek API'leri kullanır. Bozuk/başka şirket yanıtı reddedilir; bilinmeyen yazma sonucunda aynı gövde ve anahtar sayfa yeniden açıldığında korunur. Muhasebe ekranı ayrı okuma izni reddini boş liste veya fişleşmiş kayıt gibi göstermez.
- Birim, model, HTTP, strict istemci ve gerçek SQL kabul fact kaynakları eklendi: kaynak/komut/audit/outbox tekliği, self-approval, kapalı açılış düzeltmesi, gerçek banka net tutarı, bakiyeyi aşan geri ödeme, committed outbox imleci, Accounting kaynak kimliği ve şirket çiti. Runtime test/SQL/Vitest/browser/CI NOT_RUN.
- EF üreticisi `20261117005509_AddCreditActualMovements` ve `20261117005516_AddCreditMovementProposals` migration'larını üretti; designer/snapshot üreticiden gelir. Down ilk işlemde kaynak/komut/outbox verisi için `THROW 51969`, muhasebe kaynak önerisi için `THROW 51971` ile veri kaybını reddeder. Veritabanına uygulanmadı.
- API, Finance.UnitTests, Accounting.UnitTests ve IntegrationTests kaynak derlemeleri exit 0, 0 uyarı/0 hata. Hepsi tek .NET slotunda `-m:1 -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false` ile koşuldu. Bu OpenAPI üretimi veya test PASS kanıtı değildir; merkezi pin üretimi ayrıdır. TypeScript app son kaynak derlemesi ve `git diff --check` exit 0.
- Merkez bağlama: `AddFinanceCreditLenderOwnerRead` krediye özgü açık dönem adapterini de kaydeder; `AddWorkOutboxEventSource<FinanceCreditOutboxEventSourceV1>` kaynağı yukarıdaki tek olay/schema ile tanıtılmalı, `AddWorkInboxConsumer<CreditMovementProposalInboxConsumerV1>(CreditMovementProposalInboxV1.ConsumerName, CreditMovementProposalInboxV1.Subscriptions)` eklenmelidir. Dispatcher etkinleştirilmez.
- Kalan: banka+tür limit uyarısı ve merkezi gerçek Settings/host bağlantıları. Tüm kredi dilimi DONE değildir; TK-30 eşleme ayrıntısı insan kararında kalır.

### FNS-SC32 / FNS-D31-D70: gerçek teminat türü bağlantısı

- Finance teminat türü okuyucusu Settings'in `ISettingsCollateralTypeCatalogV1` gerçek sözleşmesine bağlandı; güncel İstanbul iş günü ve çağrı öncesi/sonrası şirket/kişi çiti korunur. Pasif türün mevcut kayıtlardaki oranı korunur, boş katalog boş kalır. Eksik/bozuk/yabancı şirket cevabı kullanılabilir sonuç veya yüzde 100 olarak gösterilmez.
- Settings sözleşmesi owner kaynağından byte-exact alındı. Owner persistence ve gerçek host DI kabulü merkezi entegrasyonda doğrulanacak. Yeni persistence/migration yok.
- Dört birim kaynak testi: oran/sürüm, boş/pasif, yabancı şirket/geç yetki kaybı, bozuk cevap/iptal. Derleme sırada; runtime test/SQL/CI NOT_RUN. FNS-SC32 cari bakiye transferi bu bağlantıyla tamamlanmış sayılmaz.

## FIN-SC47-KREDI — PARTIAL, banka ve kredi grubu limiti

- FNS-D95/D100: gerçek Cari şirket hesabı, kredi grubu ve para birimi anahtarında kaynak belgeye dayalı limit önerisi, ayrı kişinin onayı ve gerekçeli ret yazıldı. Eski onaylı limit değişmez; yeni onay append-only revizyondur. Bekleyen öneri ve onaylı revizyon tekliği şirketli SQL constraint ile korunur.
- Gerçekleşmiş, onaylı ve ters kaydedilmemiş kredi hareketlerinin yalnız anapara bileşeni değerlendirilir. Plan ve bekleyen öneri mevcut kullanım değildir; önerinin etkisi ayrıca gösterilir. Tüm banka geçmişi veya kullanılabilir banka limiti iddiası yapılmaz. Kaynakta limit yoksa aşım bilinmiyor kalır; sıfıra çevrilmez. Limit aşımı kayıt/onay önünde engel değildir.
- Uygulama ekranı kaynak limitin tarihçesini, kaydedilmiş anapara netini ve backend uyarısını gösterir. Cari risk limiti ekranı ve `/finance/credit-limits` değişmedi; banka/grup işlemleri `/finance/credit-bank-limits` yüzeyindedir. Tutar hesabı UI'da yapılmaz. Bilinmeyen yazma sonucu remount ve yetki reddinden sonra aynı komutla sorgulanır.
- EF `20261117012305_AddCreditBankGroupLimits` üretildi; veri varken Down ilk işlemde `THROW 51974` ile reddeder. Snapshot/designer üreticiden gelir. Veritabanına uygulanmadı.
- Birim/model, üç HTTP sözleşmesi, iki gerçek SQL fact ve üç istemci davranışı kaynakları eklendi. API, Finance.UnitTests ve IntegrationTests kaynak derlemeleri exit 0, 0 uyarı/0 hata; `-m:1 -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false`. Önceki gerçek teminat türü adapteri de bu kaynak derlemelerine dahildir. TypeScript kaynak derlemesi exit 0; `git diff --check` exit 0. Runtime test/SQL/Vitest/browser/CI NOT_RUN, OpenAPI üretimi merkezi sıradadır.
- Yeni izin/grant yok; mevcut kredi öneri ve onay izinleri kullanılır. Kredi muhasebe önerilerinin TK-30 eşleme ayrıntısı ve merkezi host/Settings entegrasyonu açıkken tüm kredi dilimi DONE sayılmaz.

## FIN-SC36 — PARTIAL, PDF ile resmî satış faturası kullanıcı akışı

- FNS-D17–D21/D39/D89/D90, KLN-D102/D104: resmî fatura numarası, ETTN, kaynak tarih ve beş tutar alanı PDF ile kaydedilir. Kimlik ayırma, aynı PDF'nin yüklenmesi, gerçek belge tarama sonucu ve kesin kayıt ayrı sabit idempotency anahtarlarıyla sürer. Belgede olmayan kur/vergi/tutar hesaplanmaz; bilinmeyen TL sıfır sayılmaz.
- Kayıt ve onay gerçek Cari owner okumasıyla şirket üyeliğini doğrular. Şirket/kişi her owner await'inden sonra tekrar kontrol edilir. Geçmiş resmî belgeye yeni aktif/cari rolü politikası eklenmez. Gerçek Settings para adayları kayıt ve ekran için aynı kaynaktır; yetkisiz/bozuk owner cevabı başarılı sayılmaz.
- Liste, bekleyen filtre, kaynak detay, gerçek carinin güncel kod/unvanı ve para birimi, faturaya sabitlenen PDF sürümü, import farkları, ayrı kişinin onayı/reddi ve kaydedenin geri çekmesi yazıldı. Belirsiz yazma sonucu sayfa yeniden açıldığında aynı gövde/anahtarla sorgulanır; tekrar denemede 403 eski işlemin başarısız olduğunu kanıtlamaz. Fatura miktarı veya para hesabı arayüzde yapılmaz.
- Birim, HTTP/host, gerçek SQL fact ve strict istemci kaynakları yazıldı. Müşteri owner kaybı hem kayıttan önce hem onayda kalıcılık/makbuz oluşmasını engeller; SQL fact'i runtime çalıştırılmadı. TSC, API, Finance.UnitTests ve IntegrationTests kaynak derlemeleri exit 0; .NET uyarı/hata 0. Runtime test/SQL/Vitest/browser/CI NOT_RUN.
- Yeni migration/THROW/izin yok. Finance shell sözleşmesi 1.15: `finance.official-sales-invoices.record`, `.approve`, `.withdraw`; ekran yolları `/finans/resmi-satis-faturalari` ve `/finans/resmi-satis-faturasi-onaylari`. Capability'ler merkez mount/pin tamamlanana kadar `ImplementationAvailable=false` kalır; gerçek grant veya aktivasyon yapılmadı.
- Gelen alış faturası çekirdeği ALS-EK4 §2 uyarınca Purchasing sahibindedir; bu checkpoint gelen fatura için ikinci Finance kaydı oluşturmaz. Tüm dilimin kabulü veya kullanıcıya açılmışlığı iddia edilmez.
