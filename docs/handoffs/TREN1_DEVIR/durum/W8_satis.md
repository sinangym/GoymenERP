# W8 Satış — kaynak tamamlama

W8 son çalışma paketidir; yeni dalga açılmadı. Bu rapor kaynak durumudur, çalışma zamanı kabulü değildir.

## SAT-S13 — PARTIAL (entegrasyon bekliyor)

Kararlar: SAT-D343, SAT-D421, SAT-D424, SAT-D425. İrsaliye kg kaydı ve ekranı, gerçek Documents sürüm/hash kanıtı, teorik kg oranlı backend dağıtımı, en ağır ilk satıra artık ve faturaya kaynak satır kg'sının aynen aktarımı yazıldı. Birleşen faturada yeniden dağıtım yapılmaz. Fiziksel stok miktarı bu aktarımda değişmez. Belgesiz eski kayıtlara kg kaynağı uydurulmaz.

- Migration: `20261117010812_AddDispatchKilogramAllocations`, EF üretimi; nullable eski kayıt uyumu; Down ilk işlemde kaynak dağılımı varken **51973** ile reddeder.
- Yeni izin adı yok; mevcut Logistics record/read/approve sınırları kullanılır. Doküman erişimi actual owner ve company çitindedir. Belge seçimi, GİB doğrulaması veya dış sisteme gönderim değildir.
- Kaynak testleri: kesin dağıtım/precision, sahte/eksik/başka şirkete ait belge, source sürümü, replay, lost authority, HTTP wire/status, SQL immutable kayıt+attempt+audit/rollback/Down, Vitest ekran durumları ve aynı anahtar retry.
- Derleme: Api, Logistics.UnitTests, Sales.UnitTests, Api.IntegrationTests exit 0, 0 uyarı/hata. Api için `OpenApiGenerateDocumentsOnBuild=false` kullanıldı; OpenAPI üretimi merkezi entegrasyona kaldı. Frontend `npx --no-install tsc -b` exit 0. `git diff --check` exit 0.
- İlk Api denemesi yeni worktree'de restore bulunmadığı için NETSDK1004 verdi; restore içeren sonraki derleme başarılı oldu. Bu bir test sonucu değildir.
- Unit/SQL/HTTP/Vitest/browser/CI: **NOT_RUN** (GOV-D9). Veritabanı kurulmadı veya migration uygulanmadı.
- Entegrasyon: Shell/OpenAPI/recovery/architecture/ilişkisel envanter pinleri merkezi sırayla üretilecek. `KALAN_PLAN.json` DONE sayısı bu kaynak commit'iyle değişmedi.
- K3 review listesi: immutable JSON kaynak+strict decoder, gerçek Documents owner bağlama/sürüm sabitleme ve commit öncesi yeniden okuma, Sales kg taşıma ve physical quantity ayrımı, 51973 Down. Yeni tenant bypass allowlist girdisi eklenmedi.

SAT-S9, SAT-S12 ve SAT-SC34 ile sipariş kurunun gerçek üretim tüketicisine bağlanması mevcut açık W8 kapsamı olarak sürer.

### SAT-S13 — tek fatura kuru ve USD sevk günü sınırı

- 17 Kasım kullanıcı cevabı, Issue #1189 `6943483644` ajan aktarımıdır; doğrudan insan
  GitHub yorumu olarak sunulmaz. USD fiyatlı farklı İstanbul iş günlerinin sevkleri aynı
  fatura taslağına alınmaz; TCMB yayını veya kur aynı çıksa da gün sınırı korunur.
  Aynı İstanbul gününün UTC gece sınırındaki sevkleri birlikte kalabilir. TL sevklerine
  bu cevaptan yeni gün kısıtı çıkarılmadı; mevcut para birimi/vade/tek kur kontrolleri sürer.
- Üç birim test kaynağı ve Türkçe ret metni eklendi. API, Sales.UnitTests ve
  Api.IntegrationTests kaynak derlemeleri exit 0, uyarı/hata 0; TypeScript exit 0.
  Sales.UnitTests ilk no-restore denemesinde assets eksikti; yalnız ilgili proje restore
  edildikten sonra derlendi. Runtime Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**.
- Bu kayıt genel vergi mevzuatı yorumu değildir. Merkezi pinler ve trene entegrasyon
  tamamlanmadan SAT-S13 için DONE veya yüzde artışı yapılmadı.

## URT-SC36 bağlantısı — gerçek satış siparişi kur kaynağı

FNS-D73/D83/D107 için Sales owner okuma portu eklendi. Mevcut onaylı revizyon ve arşivlenmiş onaylı revizyon kendi satır kimliğiyle doğrulanır. Siparişte ayrı kur türü/tarihi alanı bulunmadığından FNS-D83'ün sipariş tarihi ve TCMB döviz satış varsayılanı, mevcut Ayarlar SAT-D54/D170 çözümleyicisine aktarılır. Tarih algoritması burada tekrarlanmaz. Üretimin talep ettiği kaynak para birimi açıkça iletilir; okunamayan sipariş veya kur için mal kabul kuruna geçilmez. TL stok maliyeti değiştirilmez.

- Üretim Application sözleşmesi bağımlılığı: `64d0e3495` (bu dalda `9d256f2e3`); kesin oran türü üretim yazarının `f65b9026b` kaynağıyla aynıdır.
- Kaynak testleri: gerçek Sales owner, ilk revizyonun sıfır olması, arşivlenmiş onay, yanlış şirket/satır/revizyon, değişen kaynak sürümü, aktör/şirket kaybı, kur hassasiyeti ve iptal. Gerçek SQL fact'i actual Sales store üzerinden kalıcı kayıt okumasını ve yazma oluşmamasını kapsar.
- Api, Sales.UnitTests ve Api.IntegrationTests derlemeleri exit 0; 0 uyarı/hata. İlk Integration derlemesindeki sentetik şirket bağlamının eksik arayüz üyesi tamamlandı; yeniden derleme exit 0. OpenAPI üretimi kapalı, merkezi entegrasyon bekliyor.
- Test çalıştırılması, SQL ve CI: **NOT_RUN**. Yeni migration, izin veya grant yok. Üretimin gerçek tüketicisiyle birleştirme ve pin üretimi tamamlanmadan dilim DONE sayılmaz.

## SAT-S12 — PARTIAL: gerçek maliyet kaynağı ve iç onay anlık görüntüsü

- Kararlar: SAT-D268–275 ve URT-D67. Stok şirket geneli ortalama maliyet portu, gerçek para birimi ve birim dönüşüm kanıtıyla bağlandı; alış ve kesinleşmiş üretim maliyeti kapsanır. Para birimi kanıtı olmayan eski değer TL sayılmaz. V1 `PurchaseCostOnly` metin anahtarı korunur; kullanıcı açıklaması gerçek kapsamı gösterir.
- İç onayda satır maliyeti, kaynak kanıtı ve kalıcı kullanılamama nedeni aynı transaction/audit/idempotency kaydına eklenir. Fiyat değişikliği maliyeti yeniden yazmaz; ürün/birim/miktar değişen revizyon yeniden kaynak okur. Eski onaylar geriye doldurulmaz. Müşteri reddinden geri getirme (#1189 `6934716192`) ve revizyon dışı miktar değişimi için açık politika tahmin edilmedi.
- SAT-D265(5) bağımsız formül golden kabulü bulunmadığından hesap gösterimi `FormulaGoldenPending` olarak kapalıdır. Sentetik calculator testleri bu onayın yerine geçmez. Stok okuma ve onay kanıtı kaynakları tamamlandı diye dilim DONE sayılmadı.
- EF üretimi: `20261117105309_AddSalesMarginCostSnapshots`, ilk Down işlemi **51993**; kayıt varken delili silmez. CompanyId filtresi, append-only yazma koruması, snapshot/constraint, gerçek SQL rollback ve Down fact kaynakları yazıldı. Yeni tenant bypass veya izin yok.
- Merkezi kayıt: `ISalesMarginCostReadPortV1` → `InventorySalesMarginCostReadPortV1` scoped adapter. Sales modülündeki gerçek servis factory bağlantısı bu değişikliktedir; merkezi Program/host kaydı ve pinler kök entegrasyondadır. Inventory para birimi kanıtı bağımlılığı `f9b175a42`.
- Kaynak derlemeleri: Api, Sales.UnitTests ve Api.IntegrationTests exit 0; 0 uyarı/hata. İlk Sales.Unit derlemesindeki hedef türü belirsiz test kurucusu düzeltildi, tekrar exit 0. `OpenApiGenerateDocumentsOnBuild=false`; OpenAPI üretilmedi. `npx --no-install tsc -b` ve `git diff --check` exit 0.
- SQL, Unit, HTTP, Vitest, browser ve CI: **NOT_RUN**. Veritabanına migration uygulanmadı. Formül golden/insan kararı, merkezi entegrasyon ve kapanış kanıtları ayrı beklemektedir.

## SAT-SC34 — PARTIAL: fason fatura yaşam döngüsü ve resmî tarih kuru

- Kararlar: SAT-D129/D379/D399, SAT-D376/D377/D402, SAT-D380, SAT-D204/D208/D241.
  Dövizli fason hizmet faturası kendi para biriminde hazırlanır; taslak gününden kur
  seçilmez. Ayrı kişinin onayı sonrası gerçek Finance resmî fatura kaynağı okunur;
  belge tarihi, kaynak kimliği/sürümü/checksum veya PDF kanıtı ve o tarihin Settings
  kuru eşleştirmede saklanır. Eksik kur, belge veya şirket kanıtı başarı sayılmaz.
- Vade, onaylı siparişin vade koşulundan ve gerektiğinde gerçek Logistics sevk olayının
  İstanbul tarihinden gelir. Tartım kayıt zamanı veya fatura tarihi yerine konmaz.
  Dondurulmuş satırlar ve mevcut deterministik fatura/taksit hesaplayıcıları kullanılır;
  ekran miktar, kur, vergi veya tutar hesaplamaz. Eski taslaklara vade uydurulmaz.
- Submit/approve/reject/cancel/match/finalize/resend-finance komutları mevcut Satış
  izinleri, şirket, sürüm, idempotency, correlation/causation ve audit sınırındadır.
  Hazırlayan/gönderen onaylayamaz. Kesinleştirme tek outbox olayı ve bekleyen Finance
  kabulü yazar; tekrar aynı AdmissionId/kalem kimliklerini kullanır. Kullanılamayan
  Finance veya muhasebe tüketimi kabul edilmiş gibi gösterilmez.
- Normal ve fason faturalar aynı şirket/resmî numara/ETTN tekillik kaydını kullanır.
  Eski normal eşleşmelerin kimlikleri migration ile korunur; yeni fatura üretilmez.
  EF migration: `20261117113343_AddSubcontractInvoiceLifecycle`; Down ilk işlemde
  kanıt varken **51994** ile reddeder. Başlık geçişi ve append-only tarihçe/komut/kimlik
  trigger'ları **51997** kullanır. Migration/designer/snapshot üreticiyle oluşturuldu.
- Kaynak testleri: gerçek resmî tarih ve yabancı şirket reddi, kur eksikliği, taze yetki
  kaybı, maker/checker, replay, ortak belge kimliği, SQL atomik outbox/audit/attempt
  rollback ve Down; HTTP katı gövde/başlıklar; frontend bozuk yanıt, yetkisizlik ve
  ekran kapanınca aynı anahtarla belirsiz komutu yeniden deneme.
- Kaynak derleme: Api, Sales.UnitTests ve Api.IntegrationTests exit 0; ilk Api
  denemesindeki namespace importu düzeltildi. OpenAPI üretimi derlemede kapalıdır.
  TypeScript ilk denemede ortak istek yardımcısının eksik argümanını yakaladı;
  düzeltme sonrası `npx --no-install tsc -b` exit 0. Runtime Unit/SQL/HTTP/Vitest,
  browser ve CI **NOT_RUN**. Veritabanı oluşturulmadı/migration uygulanmadı.
- Merkezi entegrasyon: yalnız okuma/onay yetkilileri için lifecycle panelinin sipariş
  detayında ayrıca açılması kök yazardadır; Program/Shell/generated pinler bu şeritte
  değiştirilmedi. Sales modül factory ve endpoint kayıtları kaynakta tamamlandı.
- Açık kalan: SAT-D378'in kesinleşmiş faturayı iptal etme/finansal tersleme ve ardından
  tartı düzeltme akışı mevcut normal fatura iptaliyle de tamamlanmış değildir. Bu
  checkpoint yalnız taslak/onay bekleyen iptalini sunar; kesinleşmiş faturaya veya
  eski tartıya sessizce müdahale etmez. Merkezi pinler, bu kalan kaynak bağı ve kapanış
  kabulü tamamlanmadan dilim DONE veya yüzde artışı sayılmaz.
- K3 review: ortak resmî belge kimliği backfill'i, dört trigger ve guarded Down,
  tarihli kur/para snapshotı, tek Finance kabulü/outbox, kaynak kanıtı ve yetki
  kaybı kontrolleri. Yeni bypass allowlist girdisi, rol/grant veya aktivasyon yok.
