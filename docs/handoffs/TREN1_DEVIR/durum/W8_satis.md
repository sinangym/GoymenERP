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

## SAT-S9 — ALS-D90 / F17-5 termin uyarısı

- Aktif gelecek ayırmaların terminleri gerçek Purchasing schedule owner köprüsünden okunur.
  İstanbul iş günüyle yalnız okuma anında karşılaştırılır. Geçmiş termin ayırmayı bırakmaz,
  sürümünü değiştirmez, olay veya zamanlanmış iş üretmez. Aynı kaynak satırı bir kez okunur.
- Tarihsiz, okunamayan ve aktif olmayan kaynaklar ayrı gösterilir; okunamayan termin
  "geçmedi" sayılmaz. Son şirket/yetki çiti kaynak okumasından sonra yeniden çalışır.
  Ekran backend'in gününü, tarihi ve uyarısını gösterir; kendi tarihiyle hesaplamaz.
- Birim, gerçek SQL ve strict istemci/ekran test kaynakları eklendi. TypeScript derlemesi
  exit 0; backend kaynak derlemesi merkez entegrasyonda sıradadır. Runtime Unit/SQL/HTTP/
  Vitest/browser/CI **NOT_RUN**. Migration/izin yok; plan durumu ve yüzde artırılmadı.

### SAT-S13 — irsaliye listesinde gerçek kantar farkı

- Kullanıcı satırı açınca mevcut Sales owner endpointinden teorik/kantar/fark/fazla kg,
  kayıt/onay/bant ve fatura bekleme nedeni okunur. Ekran dağılım, fark veya tolerans
  hesaplamaz. Satır açılmadan arka planda her irsaliyeye ayrı istek gönderilmez.
- Şirket ve irsaliye kimliği, miktar metinleri ve onay tutarlılığı istemcide doğrulanır.
  Eksik kayıt, yetkisiz, bulunamadı, çakışma, çevrimdışı, 503 ve bozuk yanıt ayrı kalır.
  İstek şirket bağlamına ve iptal sinyaline bağlıdır; `no-store` kullanır.
- Sözleşme/HTTP ve gerçek shell üzerinde ekran test kaynakları eklendi. `npx tsc -b`
  exit 0. Runtime Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**; yeni izin/migration yok.
  Merkezi pin üretimi ve trene entegrasyon öncesi plan yüzdesi artırılmadı.

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

### SAT-SC34 kullanıcı akışı eki — siparişin gerçek faturalarını seçme

- Serbest teknik fatura kimliği girişi kaldırıldı. Şirket ve satış siparişiyle sınırlı,
  sayfalı gerçek liste belge numarası, tarih ve durum gösterir; kullanıcı bu listeden
  faturayı açar. Liste yalnız mevcut `sales.invoices.read` yetkisini ister; yazma veya
  fiyat okuma yetkisi istemez ve parasal alan taşımaz. Ayrıntı/komutların kendi yetki
  çitleri korunur. Cursor başka şirket veya siparişte kullanılamaz.
- Boş, yetkisiz, bozuk ve yabancı sipariş yanıtları işlem açmaz. Şirket/oturum/yetki
  nesli değişiminde seçim sınırı yenilenir; belirsiz komutun saklanan anahtarı korunur.
  Kesinleştirmenin ilk yanıtı, eşzamanlı otomatik Finans aktarımı replay olsa bile
  kesinleştirme komutunun kendi correlation/replay makbuzunu korur.
- Unit, HTTP sözleşmesi, gerçek SQL sayfalama/şirket-cursor sınırı ve Vitest kaynakları
  eklendi. Yeni migration, izin, grant veya aktivasyon yok. Api, Sales.UnitTests ve
  Api.IntegrationTests kaynak derlemeleri exit 0; 0 uyarı/hata. OpenAPI üretimi kapalıdır.
  İlk TypeScript derlemesindeki test matcher türleri mevcut doğal DOM kontrolleriyle
  düzeltildi; tekrar `npx --no-install tsc -b` exit 0. `git diff --check` exit 0.
  Runtime Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**. Merkezi panel yerleştirmesi kök yazardadır.
- SAT-D378 kalan kaynak incelemesi: SAT-D96 dışarıdaki resmî iptalin ERP'de kaydını ve
  Finans ters açık kalemini tanımlar. Gerçek Finance F2b iptal portu vardır; üretici
  bağlantısı eksiktir. KLN-D16, onaysız fiş taslağını iptal etmeyi ve onaylı fiş için
  onaylanacak ters taslak açmayı seçer; Accounting kaynak iptal tüketicisi eksiktir.
  Aynı karar belgesinin Sınırlar bölümü ters fiş dönemini seçmez. FNS-D112 yalnız
  satır sınıfı/SMM düzeltmesi kapsamında olduğundan tam iptale sessizce genişletilmedi;
  kalan dönem/tarih sorusu koordinatöre iletildi. Bu ek dilimi DONE yapmaz.

### SAT-SC34 / STK-S3 işlem içi tartı kanıtı

- Kaynak `f8f39bbc9`, gerçek Lojistik borrowed-reader bağımlılığı `486158802`
  (seçici alım `42d9a9e81`). Satış fiyat taşımayan invoice-claim okuması gerçek
  şirket/talimat/tartı aralığındaki bütün durumları okur; boş aralık da
  Serializable işlem bitene kadar korunur. İptal/ret geçmişi kaynak sürümünden
  düşmez; unavailable boş veya aktif fatura yok cevabı sayılmaz.
- Satış create ve lifecycle yazması aynı claim aralığını önce okur. Create gerçek
  Lojistik sahibini aynı connection/transaction içinde tekrar okuyup talimat
  sürümü, tartı revizyonu, net kg ve kaynak satır kimliklerini karşılaştırır.
  Eski veya eksik kaynakta kayıt, sayaç, attempt ve denetim commit edilmez.
- EF üretimi `20261117135232_AddSubcontractInvoiceClaimRange`: yalnız üç alanlı,
  tüm durumları kapsayan index. Up create-index, Down drop-index; veri silinmediği
  için yeni THROW gerekmez. Snapshot elle yazılmadı, DB'ye uygulanmadı.
- API, Sales.UnitTests ve Api.IntegrationTests kaynak derlemeleri exit 0,
  uyarı/hata 0. Sonradan eklenen index Down round-trip SQL fact kaynağı merkez
  derlemesini bekler. Altı SQL fact adı sıralı ilişkisel envanterlere eklendi;
  yürütme **NOT_RUN**. İlk iki compile hatası mevcut hata sabiti ve sentetik
  ICompanyContext arayüz üyeleriyle düzeltildi; son derlemeler yukarıdadır.
- İki read-only borrowed context construction kaynağı **K3 review bekliyor**:
  SalesSubcontractInvoiceClaimReadSession, LogisticsSubcontractDeliveryReadSession.
  Context şirket filtresi korunur; yeni bypass/ham SQL guard allowlist eklenmedi.
  Dispose yalnız read context'ini bırakır, ödünç SQL handle'larını yönetmez.
- STK-S3 gerçek müşteri malı teslimi/tartı düzeltme host'u bu portu bağlamalıdır;
  yalnız bu seam o akışın tamamlandığı anlamına gelmez. Finalized iptal üreticisi
  ve Finans/Muhasebe dönem-tarih kararı halen açık; Accounting iptal tüketicisi
  `3044b80fb` ile vardır, Work kaydı gerçek Sales üreticisini bekler. DONE artışı yok.

### STK-S3 bağımlılığı — işlem içi gerçek satış kaynağı ve ayrı ürün kimlikleri

- Kaynak `e8ed7373ebc170d27f050f5017a6788e9ba7262e`. Yeni read-only Sales owner
  session gerçek aggregate ve kabul edilmiş revizyon kurallarını caller'ın açık
  Serializable transaction'ında okur. Şirket/işlem çiti await öncesi ve sonrasıdır;
  dispose yalnız kendisine ait context'i kapatır. Ham SQL/bypass yoktur.
- Fason Inventory kanıtında fiziksel etiket ürünü ve satış hizmet ürünü ayrı tutulur.
  Manufacturing plan sürümünden bağımsız SalesLinkRevision da immutable JSON/hash'e
  girer; geriye giden kaynak reddedilir. Bu iki additive provenance alanı yeni bir
  maliyet/çevrim/sahiplik politikası değildir; yeni tablo veya migration gerekmez.
- İki gerçek SQL fact kaynağı uncommitted gerçek Sales sürümünü, caller rollback'ini,
  başka şirketi, zayıf/bitmiş transaction'ı, aynı kayda paralel gerçek SQL yazımının
  bloklanmasını ve handle sahipliğini kapsar. İlişkisel satış envanteri sıralıdır.
- Inventory.UnitTests, Sales.UnitTests ve Api.IntegrationTests kaynak derlemeleri
  exit 0, hata/uyarı 0. İlk Inventory derlemesinde eksik assets bulundu; yalnız
  mevcut proje restore edildi, yeni paket yoktur. Runtime Unit/SQL/HTTP/CI NOT_RUN.
- K3 review bekliyor: yeni borrowed Sales read-context construction. Guard izin
  listesi genişletilmedi. Gerçek fason Logistics host ve ekran zinciri halen açıktır;
  bu bağımlılık bütün STK-S3 diliminin DONE veya runtime kabulü değildir.

### SAT-S12 ilk onay marj maliyeti koruma

- Oturumdaki doğrudan insan cevabının kaynak sınırı açık aktarımı:
  [6951607190](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6951607190).
  Müşteri reddi sonrası aynı ürün/miktarla yeniden etkinleşen satır ilk onayın
  birim maliyetini korur; aynı revizyon içindeki miktar değişimi yeni maliyet
  okuması gerektirmez. Gerçek yeni revizyonda değişen miktar tabanı için mevcut
  yeniden sabitleme ve eski kayıtları geriye doldurmama kuralları korunur.
- Yeni bir miktar değiştirme endpointi veya stok/muhasebe hareketi eklenmedi.
  Golden kabul kapısı açık tutuldu; marjın kullanılabilir olduğu iddia edilmez.
- Yeniden etkinleşme kaynak testi ilk sabiti ve maliyet okuma sayısını korur.
  Sales.UnitTests ve Api.IntegrationTests derlemeleri exit 0, hata/uyarı 0;
  `git diff --check` exit 0. Runtime testler ve SQL **NOT_RUN**.


### SAT-SC34 / ADR-011 — özgün komut cevabının kalıcı tekrarı

- Başarılı lifecycle denemesine nullable `LifecycleSequence` bağlandı. Sonraki onay,
  kesinleştirme veya Finans yeniden gönderimi ilk komut cevabını değiştirmez. Finans
  denemeleri aynı fatura sürümünü taşıyabildiğinden özgün ledger sıra numarası kullanılır.
  Oluşturma tekrarı immutable ilk ledger kaydını okur; güncel fatura yazılmaz/geri sarılmaz.
- Eski null bağlar korunur; güncel başlıktan cevap veya geçmiş sürüm tahmin edilmez.
  Şirket filtreleri ve aktör/gövde/anahtar bağı aynen sürer. Yeni grant veya izin yoktur.
- EF üretimi `20261117155825_AddSubcontractInvoiceReplaySequence`; nullable kolon ve
  CHECK DDL'i. Down ilk adımda özgün cevap bağı varsa **51937** ile veri kaybını reddeder.
  Ana dal/tren ve aktif kaynaklarda numara çakışması görülmedi. DB apply **NOT_RUN**.
- Üç gerçek SQL fact kaynağı ilk oluşturma/komut cevabını, aynı fatura sürümündeki iki
  Finans denemesini ve boş/dolu Down korumasını kapsar. Model/ilişkisel envanter güncel.
- API, Sales.UnitTests ve son Api.IntegrationTests kaynak derlemeleri exit 0, uyarı/hata 0.
  İlk Integration denemesinde mevcut fixture'da olmayan yardımcı çağrısı üç hata verdi;
  gerçek ScalarAsync helper'ıyla düzeltildi, son Integration yeniden derlendi. EF üretimi
  ve `git diff --check` exit 0. Runtime Unit/SQL/HTTP/CI **NOT_RUN**.
- Bu düzeltme SAT-SC34'ün resmî iptal üreticisi ve bağımsız muhasebe onayı kalanını
  tamamlamaz; tüm dilim PARTIAL kalır. Kaynak ve rapor aynı dilim commit'indedir.


### SAT-S9 / F17-5 — kaynak planında gerçek alış ayırmaları

- Satırın kaynak planından gerçek Inventory ayırma okuyucusuna ve mevcut açık alış
  satırı seçme/yönetme ekranına bağ kuruldu. Kaynak planını kaydetmek ayırma oluşturmaz;
  gelecek tedarik fiziksel stok kısmına veya frontend hesaplamasına eklenmez.
- Ayrı Inventory read capability olmadan çağrı yoktur. Kullanıcı tek satırı açınca
  yalnız o satır okunur; siparişin bütün satırları için istek patlaması yapılmaz.
  Şirket/aktör/route-entry query anahtarı ve ortak auth boundary korunur.
- Kalan miktar, gerçek değerlendirme tarihi ve termin-geçti bayrağı aynen gösterilir.
  Alış/Stok bırakma nedenleri ve kayıtlı gerekçe birlikte görünür; bilinmeyen neden
  için yeni işletme kuralı türetilmez. Sınırlı owner penceresi eksiksiz geçmiş sanılmaz.
- Kaynak testleri bağımsız yetki, lazy okuma, gerçek miktar/termin/gerekçe aktarımı,
  loading/empty, 401/403/404/409/503, offline/ağ/bozuk yanıt ve okunamayan termin
  ayrımını kapsar. Yeni API, migration, izin veya grant yoktur.
- `npx tsc -b` son kaynakta exit 0. İlk denemede iç PlanView sınırında eksik props
  bulundu; typed render prop ile düzeltildi. `git diff --check` exit 0. Runtime
  Vitest/browser/HTTP/SQL/CI **NOT_RUN**. Merkezi entegrasyon/pin/tren FF beklenir.

- SAT-S9 dar takip: sipariş sürümü de satır görüntüsünün query/mount anahtarına girdi.
  Aynı siparişte kaydedilen revizyon eski ayırma görünümünü yeni sürüme taşımaz.
  Bu salt istemci bağlam çiti sonrası TypeScript kaynak derlemesi exit 0; runtime NOT_RUN.

### SAT-S13 / STK-SC36 — kesinleşen faturanın değişmez sevk kaynağı

- Dar Satış üretici bağı **SOURCE_COMPLETE**; kaynak commit'i
  `fdc417b38620920439cbbc08b689008709e6e084`. SAT-D94 / AT-055 ve SAT-D424 / SAT-D425
  dayanağıyla normal `sales.invoice.finalized.v1` olayının her satırına additive
  `ShipmentSource` eklendi. Gerçek saklı paydan sevk, irsaliye ve satır numarası,
  satış siparişi/satırı, fiyat tabanı revizyonu, ürün, birim ve fatura miktar metni taşınır.
- `SalesInvoiceShipmentSourceV1.InvoicedQuantity` saklı `QuantityText` değeridir.
  Fiziksel Inventory Issue kg'sı veya maliyet tutarı olarak kullanılmaz. Mevcut API
  yalnız bütün `ShipmentId` payını ister; miktar/parça alanı kabul etmez. Gerçek SQL
  kaynağında aktif sevk unique index'i ve yükleme sonrası değişmez pay eşitliği korunur.
- Yeni olay yalnız kesinleştirmenin mevcut tek kayıt işlemiyle üretilir. Eski outbox
  kayıtları zenginleştirilmez; mevcut tutar/vergi alanları değişmez. Tekrar komutunun
  ikinci kaynak olayı üretmemesi, Finans cevabından sonra JSON'ın aynen kalması ve
  eski Muhasebe okuyucusunun additive alanla uyumu test kaynaklarına bağlandı.
- Bir yeni ASCII unit fact eklendi: `SalesInvoiceShipmentSourceContractTests.`
  `An_invoice_create_claims_whole_shipments_and_refuses_client_quantity_overrides`.
  İki mevcut SQL fact genişletildi; yeni ilişkisel fact kimliği eklenmedi. Mevcut
  header sözleşme beklentisindeki `officialInvoiceNo` eksikliği gerçek üreticiyle hizalandı.
- API, Sales.UnitTests ve Api.IntegrationTests kaynak derlemeleri exit 0, uyarı/hata 0.
  OpenAPI build üretimi kapalıydı; OpenAPI kabulü olarak sayılmaz. `git diff --check`
  exit 0. Unit/SQL/HTTP/Vitest/browser/CI runtime **NOT_RUN**.
- Bu dar üretici bağı, ilk SMM'nin bütün akışını tamamlamaz. Gerçek stok hareketi ve
  kayıtlı TL maliyeti Inventory sahibinden doğrulanmalı, fiş önerisi Accounting sahibi
  üzerinden yürümelidir. Birleşik owner kabulü ve merkezi entegrasyon beklenir;
  STK-SC36'nın tamamına DONE verilmez. Yeni DB/EF veya yetki/grant değişikliği yoktur.

### FIN-S13 / STK-SC36 — sevkten kesinleşmiş fatura payına gerçek ters okuma

- SAT-D94 / SAT-D425 kapsamında, en çok 128 gerçek sevk kimliği için şirket kapsamlı
  `ISalesFinalizedShipmentClaimReadPortV1` eklendi. Fatura satırı, başlık, kesinleştirme
  kaydı ve değişmez outbox JSON/hash birlikte doğrulanır. Fiyat, maliyet, vergi veya
  muhasebe hesabı dışarı taşınmaz; ticari miktar fiziksel maliyet oranı sayılmaz.
- Okuyucu gerçek tüketicinin taze yetki/şirket/aktör çitini zorunlu alır; her veritabanı
  await öncesinde/sonrasında ve sonucu vermeden yeniden ölçer. İki sınırlı kaynak
  okuması değişirse sonuç kullanılamaz. Kaynak sürümü sonraki yazım için işlem kilidi
  veya yetki değildir; yazan tüketici kendi son çitini ve işlemini kurmalıdır.
- Faturalanmamış pay, başka şirket/olmayan kaynak ve bozuk/çelişkili/eski eksik kanıt
  ayrıdır. Gerçek taslak iptali ile bırakılmış payın sürümü/tarihi gösterilir; normal
  kesinleşmiş fatura için henüz olmayan resmî iptal geçişi tahmin edilmez. Aynı sevkin
  birden çok aktif veya aynı faturada yinelenen payı kullanılabilir kabul edilmez.
- Mevcut ilk SMM okuyucusundaki `PriceBasisRevision` alt sınırı gerçek Sales domain ve
  SQL CHECK ile hizalandı: ilk ticari revizyon 0 geçerlidir. Eksik eski ShipmentSource
  halen kullanılamazdır; geçmiş kaynak veya maliyet geriye doldurulmaz.
- Unit kaynakları 12 bozuk kanıt karşıörneğini, boş/tekrarlı/129 kimlikli isteği,
  şirket ve tüketici çiti kaybını kapsar. Üç yeni gerçek SQL fact kaynağı gerçek
  kesinleştirme/outbox bağını, şirket ayrımını, await sonrası çit kaybını ve iki okuma
  arasında gerçek owner iptalini kapsar. Sales ilişkisel envanteri sıralı güncellendi;
  merkezi fingerprint/test grubu pinleri entegrasyon sahibindedir.
- API, Sales.UnitTests ve Api.IntegrationTests kaynak derlemeleri exit 0, uyarı/hata 0.
  OpenAPI build üretimi kapalıdır; OpenAPI kabulü değildir. `git diff --check` exit 0.
  Runtime Unit/SQL/HTTP/CI **NOT_RUN**. Yeni model, migration, endpoint, izin veya
  grant yoktur. Merkezi DI ve gerçek muhasebe attribution tüketicisi takip işidir;
  bu kaynak checkpointi bütün FIN-S13/STK-SC36 dilimine DONE vermez.
