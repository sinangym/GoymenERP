# TREN-1 W8 — devam eden son kaynak paketi

W8 sürüyor; yeni dalga açılmadı. Son yayımlanmış ana plan **135/174 DONE (%77,6)**,
39 açık dilimdir. Aşağıdaki birleşimler kaynak checkpoint'idir; eksik gerçek owner
bağlantısı veya dilim kapsamı yalnız derleme geçtiği için DONE yapılmaz.

## Birleştirilen ekip kaynakları

| Ekip | Alınan kaynak | Entegrasyon |
|---|---|---|
| satis | `a27517bd5` | `06825b5bb` |
| alis_cari | `82d31f300` | `e77f3d8bd` |
| uretim | `4d9b8bfca` | `d8a2f100d` |
| finans | `9e9b8eebe`, ardından B-28 kaynak takibi `0f9d4bc71` | `2294f9763`, `167a4a4ac` |
| kimlik / Settings / Work | `f2781f6eb`, hesap güvenliği `244783f03` | `9ce344a0b`, `a840b380e` |
| stok_lojistik / Product | `b6ba56727` | `04ee1f625` |

Platformun bağımsız yeni ekip dalı yoktur; kişisel bildirim deposu Kimlik/Work
checkpoint'indedir. Finans B-28 tamamlayıcı kaydı stok birleşiminden önce alındı.
Birleşimler `--no-ff` ile yapıldı; rebase, force veya eski commit değişikliği yoktur.

## Çatışma ve merkezi bağlantı

- Manufacturing'in taşıdığı eski Purchasing kopyasında güncel numaralama ve cari
  revizyonu korundu. Ortak Inventory sözleşmelerinde exact oran ve ilk giriş kuru
  birlikte kaldı. Purchasing batch tersleme/tedarikçi iade ilaveleri korundu.
- Kimlik birleşiminde Inventory geçmiş satış bağlantısı okuyucusunun ortak
  transaction'a katılan güncel sürümü ve bu sürümün SQL testi korundu.
- Stok birleşiminde owner'ın strict ilk giriş kur doğrulaması ve Product boya
  üyeliği sözleşmesi alındı. Güncel Purchasing onaylı sipariş kuru kanıtı,
  Settings numara serisi kayıtları ve Kimlik raporu korundu.
- Belge owner sözlüğüne hem lojistik irsaliye hem resmî satış faturası eklendi.
- Gerçek Product birim dönüşümü mevcut runtime koşuluna bağlandı. Inventory
  consumer'ı aynı owner calculator'ını kullanır; yeni katsayı veya aktivasyon yoktur.
- Tekli/toplu mal kabul ve kabul düzeltmesi, yeni Purchasing kabulünü aynı açık
  transaction üzerinden okuyan maliyet kaynağına bağlandı. Bilinmeyen TL/USD
  maliyet sıfır olmaz; ödünç kaynak commit/rollback sahibi değildir. Bunun SQL
  kaynak testi `Receipt_cost_host_reads_the_uncommitted_acceptance_and_keeps_unknown_prices_null_without_owning_the_transaction` adındadır.
- Alış kabul geri alma/batch factory'leri, Cari V2 portu, üretim boya seçimi,
  gerçek Settings teminat listesi ve kredi Cari owner'ı merkezi DI'a bağlandı.
- Inventory/Purchasing/Finance outbox kaynakları ile bildirim, üretim SMM ve kredi
  muhasebe tüketicileri kaydedildi. Purchasing fatura tüketicisi kayıtlıdır;
  gerçek gelen fatura producer'ı hâlâ açık iştir. Dispatcher etkinleştirilmedi.
- Kredi, resmî satış faturası, üretim maliyet farkı, kullanıcı yönetimi ve boya
  ürünleri route/menüleri bağlandı. Hesap güvenliği sayfasına ana sayfadan giriş
  eklendi. Mevcut capability/permission kapıları korunur; staged Finance ve
  Accounting bildirimleri runtime kabulü olarak yükseltilmedi.

## Kaynak doğrulaması ve kalan

- Birleşik API ve Api.IntegrationTests kaynakları exit 0, .NET uyarı/hata 0.
  TypeScript `npx tsc -b` exit 0. Son merkezi DI ilavesinden sonra API tekrar
  derlemesi de exit 0, uyarı/hata 0 verdi.
  Derlemede OpenAPI üretimi kapalıdır; otomatik uygulama veya DB başlatılmadı.
- Bütün UnitTests/ArchitectureTests ortak derlemesi ve pin üretimi bu son merkezi
  bağlantılardan sonra yapılacaktır. Pin sırası Shell → OpenAPI → recovery/release
  → mimari guard → ilişkisel envanter/test gruplarıdır.
- Runtime Unit/SQL/HTTP/Vitest/browser/CI ve bağımsız K3 kabulü **NOT_RUN**.
  Yeni context construction ve sabit SQL horizon okumaları için K3 gerekçeleri
  korunur; guard izin listesine sessiz ekleme yapılmadı.
- Açık geliştirme: üretim maliyet/reopen gerçek provider bileşimi; Product açık
  ticari mal/mamul sınıfı; gelen alış faturası/muhasebe zinciri; Finans dönem/devir
  ve diğer B-28 kaynakları; Sales maliyet pini/son resmî kaynak provenance'ı;
  W5 kaynak hizaları ve son tamamlanma denetimi.
- Cevabı beklenen kararlar: KIM-D27 kurtarma kapsamı (`6933574705`), SAT-S12 pin
  yenileme sınırı (`6934716192`), FIN-S17 yeni dönem yokken devir ve yeniden açma
  yetkisi (`6935152814`). Yanıt gelmeden bu politikalar uygulanmaz; diğer işler sürer.

Bu rapor W8'in bittiği, trene push edildiği veya ürünün kullanılabilir/kabul
edilmiş olduğu anlamına gelmez. Ana plan sayısı tam dilim denetiminden sonra güncellenir.

## Son owner bağlantıları — 17 Kasım kaynak devamı

- Üretim yeniden açma işleminde gerçek Accounting dönem okuyucusu aynı Serializable
  transaction'a katılır (`d93914f54`); bağlantıyı kapatma/commit etme sahibi değildir.
  Yeni context construction K3 inceleme listesindedir, guard'a sessiz eklenmedi.
- FIN-S14 gerçek gider/nakit transferi olayları immutable muhasebe kaynak kuyruğuna
  bağlandı (`6ee4c7a75`). `20261117102540_AddFinanceJournalSources`, Down **51989**;
  onaylı eşleme ve aynı tutar kanıtı olmadan muhasebe önerisi oluşmaz. Kur değişimi
  muhasebe satır politikası açık kalır. Dispatcher ve Finance STOP değişmedi.
- Product boya üyeliği için gerçek ürün adayı (`6c710040d`) ve Inventory ortalama
  maliyetin para birimi kanıtı (`f9b175a42`) alındı. Eski/bilinmeyen maliyet para
  birimi TL varsayılmaz. Üretim USD gösterimi resmî TL tutarı/kabul günü USD kuru
  çapraz dönüşümüne bağlandı (`d6f24c79d`); TL ana maliyet değişmez.
- SAT-S9 termin uyarısı (`b7e959a2f`) ve SAT-S13 gerçek kantar farkı görüntüsü
  (`b06da2eee`) alındı. Geçmiş termin ayırmayı kendiliğinden bırakmaz; ekran hesap
  yapmaz. Resmî irsaliyeden faturaya kg aktarımının önceki kaynakları korunur.
- Gelen alış faturası listesi/eşleştirme/kesinleştirme mevcut owner'a bağlandı;
  dosya içe aktarma hâlâ açık FIN-S13 kapsamıdır. Product ticari mal/mamul kontrolü,
  Ayarlar alış fatura fark toleransı, Finans cari bakiye devri ve Muhasebe satır
  sınıfı/SMM düzeltme route/menü bağlantıları eklendi. Mevcut staged Finance/
  Accounting kapıları ve gerçek yetki sınırları korunur; grant yapılmadı.
- Sales invoice finalized ve Finance gerçek hareket kaynakları, Work registry'de
  ilgili Accounting consumers ile eşlendi. Registry validator/dispatch kapalı
  olma test kaynağı eklendi. Shell provider sürümleri Settings **1.13**, Finance
  **1.16**; authority hashleri merkezi üretici aşamasında güncellenecek.
- Birleşik API, Api.IntegrationTests, Finance.UnitTests, Inventory.UnitTests,
  Product.UnitTests kaynak derlemeleri exit 0, uyarı/hata 0. TypeScript derlemesi
  exit 0; son mount parity test kaynaklarının derlemesi bir sonraki TSC'ye dahildir.
  Bütün runtime Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**. Plan **135/174 (%77,6)**
  kalır; tamamlanmış dilim denetimi/pin/FF push öncesi yüzde artırılmadı.

## Dosya ve fiziksel belge sahipleri — devam checkpointi

- FIN-S13 dosya kaynağı `929f6e57a`, kullanıcı önizlemesi `fd6c33fb1`, kayıtlı
  kaynağı yeniden açma `85c2a4769` entegrasyona alındı. XML/UBL, CSV ve XLSX;
  gerçek Documents owner, immutable sürüm/hash ve temiz dosya denetimi vardır.
  Gerçek cari/ürün/birim/vergi/vade eşlemesi ve fatura kayıt admission'ı hâlâ açık;
  import capability list parent'ı ile birlikte staged kalır. Dosya önizlemesi
  finansal kayıt veya resmî belge doğrulaması sayılmaz.
- ALC-S7 fiyat/actual vergi kaynağı `7e3aad912` / rapor `9c2812891`; ALC-S8 kayıt
  `60a2e3110` / rapor `93e1f98c3`, additive ExpenseTypeId `a3d1512a5` alındı.
  Ek masraf mevcut OrdersRevise ile kaydedilir; ilgili API/route merkezde bağlandı.
  Parçalı fatura eşleştirmesi `368216c5d` / rapor `555503e1b` ile alındı;
  fiziksel kabul, stok hareketi veya Inventory ortalama maliyeti üretilmez.
- ALS-D105 güncel insan cevabı [6947126872](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6947126872):
  satış fason vergi varsayılanı/satır kodu mevcut sales.orders.approve, alış tarafı
  mevcut purchasing.orders.approve ile değişir. Önceki iki yeni izin cevabı geri
  alınmıştır. Yeni izin/grant yok; bu yetki düzeltmesinin kod takibi owner'dadır.
- STK-S8 belge/ayrılış/Settings owner'ları `06a4e2953`, gerçek müşteri rolü kanıtı
  `c86f24b46` alındı. Settings tartı ve Logistics transfer çıkışı route'ları bağlandı;
  üç mevcut owner yeni izin kaydı merkez kataloğuna eklendi, grant verilmedi.
  Transfer planlama mevcut stock-transfer.write ile ayrı route'tur; genel stok
  raporu izinleri bu komuta eklenmez. Plan/kabul workspace kaynak takibi sürer.
- SAT-SC34 lifecycle `f8cf8b24b`, siparişe bağlı gerçek belge seçimi `924ddd6d0`
  alındı. Okuyucu/onaylayan write izni olmadan lifecycle paneline ulaşabilir.
  Kesinleşmiş iptalin Finans/Muhasebe bağı ve B-04 fiş onayı hâlâ açık;
  [6947269325](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6947269325)
  dönem/tarih sorusu yanıt bekler. Tamamlanmış iptal iddiası yapılmaz.
- Bu owner checkpointlerinin raporlarında API/ilgili Unit/Integration/TypeScript
  derlemeleri exit 0 kayıtlıdır. Merkezde ortak provider/route değişiklikleriyle
  API, Purchasing, Inventory, Logistics, Counterparty, Accounting, Settings,
  Identity UnitTests ve Api.IntegrationTests derlemeleri exit 0 alındı.
  Counterparty test kaynağındaki ConfigurationBuilder bağımlılığı ve xUnit
  predicate kullanımı derleme hataları düzeltildi; proje yeniden exit 0 verdi.
  Son merkez TypeScript derlemesi, bu owner birleşimleriyle birlikte exit 0 aldı.
  Son parçalı masraf eşleştirmesi kendi owner'ında C# exit 0 aldı; bu son deltanın
  merkez C# derlemesi sonraki entegrasyon derlemesine dahildir.
  Test çalışma zamanı, gerçek SQL, Vitest/browser ve CI **NOT_RUN**.
  Shell/OpenAPI/recovery/mimari/ilişkisel pin üretimi ve tren FF push henüz yapılmadı.
  Sabit ölçüm **135/174 (%77,6)**, W8 son pakettir; yeni dalga açılmadı.

## Transfer kabul owner'ı ve fatura commit çiti — devam

- STK-S8 gerçek plan/kabul kaynağı `1127e5993` alındı. Inventory kabul belgesi
  Draft/Effective değişmez geçmişi ve typed belge kanıtı, action-purpose kuyruk
  okumaları, gerçek plan adayları ve komut ekranı vardır. EF üretimi
  `20261117122205_AddInventoryTransferReceiptDocuments`, Down **51999**; DB'ye
  uygulanmadı. Owner API, Inventory/Counterparty/Organization UnitTests,
  Api.IntegrationTests ve TypeScript derlemeleri exit 0, uyarı/hata 0 aldı.
- İki yeni Inventory endpoint map'i gerçek host'a bağlandı. Fazla kabul onayı,
  kayıp talebi ve kayıp onayı mevcut ayrı yetkileriyle bağımsız gateway'e açıldı;
  genel stok raporu veya Product gösterim yetkisi komuta eklenmedi. Inventory
  shell provider **1.12**, 20 bildirimdir. Merkez birleşik TypeScript exit 0;
  bu son mount/provider C# derlemesi sıradaki merkez derlemesini bekler.
- FIN-S13 `87fac44cd`: fatura kayıt ve terminal ret işlemleri kendi transaction'ında
  SQL Save sonrası fresh yetki çitinden geçer. Ret halinde belge/geçmiş/denetim/
  attempt commit edilmez. İki SQL fact kaynağı, Purchasing.UnitTests ve
  Api.IntegrationTests derlemeleri exit 0; runtime **NOT_RUN**. Alış fason vergi
  snapshot'ının immutable eşleştirme metadata'sı `bc0524b5f` de alındı; resmî
  fatura tutarları değiştirilmez. D105 yetki düzeltmesinin kalan owner kaynağı sürer.
- Kaynakta TL satır/toplam olmayan dövizli alış faturası yuvarlama dağılımı için
  [6947890400](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6947890400)
  karar bekler. Linked GİB gerçek provider'ı 503 kalır; mock veya unavailable
  kullanılabilirlik sayılmaz. Pin/son tren FF ve runtime kabulü henüz alınmadı.

## Kaynak fişi onayı ve resmî fatura kimliği — devam

- FIN-S13 kaynak fiş owner'ı `3044b80fb` alındı. Gerçek hesap planı ve açık dönemle
  ayrı hazırlayan/onaylayan fiş akışı, iptal olayı önce gelirse tombstone ve
  onaylanmamış kaynak fişin iptal kaydı vardır. Kesinleşmiş yevmiyenin ters kayıt
  dönem/tarihi yanıtı bekler; kendiliğinden ters kayıt veya dönem açma yapılmaz.
- Kaynak fişi endpoint'i host'a, okuma ekranı tek menü/gateway'e bağlandı.
  Onay aynı ekranın ayrı kontrolüdür: mevcut JournalsRead ve normal fiş onay
  yetkileri gerekir. Accounting provider **1.7**, 20 bildirimdir; yeni izin/grant
  yoktur. Yeni finans ekranları SQL/browser kabulüne kadar staged kalır.
- Muhasebe iptal tüketicisinin host kimliği mevcut kapalı başlangıç anahtarını
  korur. Gerçek Sales iptal üreticisi tamamlanmadan Work aboneliği kaydedilmez;
  DI kaydı olayın işlendiği veya iptalin uçtan uca tamamlandığı anlamına gelmez.
- `987759ebd`: alış faturasının resmî numarası additive/null-omit olay alanıyla
  Muhasebe kaynağına taşınır. Eski olaylar için numara tahmin edilmez. Purchasing
  UnitTests ve Api.IntegrationTests derlemeleri exit 0, uyarı/hata 0 aldı.
- Bu birleşim ve host/provider kaynaklarıyla merkez API, Inventory.UnitTests,
  Accounting.UnitTests, Api.IntegrationTests ve TypeScript derlemeleri exit 0,
  uyarı/hata 0 aldı. Çalışma zamanı testleri, SQL, HTTP, Vitest/browser ve CI
  **NOT_RUN**. Sabit ölçüm **135/174 (%77,6)**; pinler ve tren FF henüz yoktur.

## ALS-D105 son yetki cevabının entegrasyonu

- `e67f53fc7` / rapor `46eeef92f` alındı. Satış ve alış fason varsayılanları ve
  satır vergi kodu değişiklikleri mevcut ayrı sipariş onay yetkilerini kullanır.
  Önceki “iki yeni izin” yanıtı geri alınmıştır; yeni ERP permission, grant,
  rol veya Critical sınıflaması yoktur. Vergi revizyonu normal yeniden onaya gider.
- İki ayrı Ayarlar route'u gerçek `SubcontractTaxDefaultPage` bileşenine bağlandı.
  Settings provider **1.15**, 31 bildirimdir; yeni adlar yalnız teknik ekran
  capability kimliğidir. Satış ekranı yalnız sales.orders.approve, alış ekranı
  yalnız purchasing.orders.approve gerektirir. Yeni iki ekran runtime kabulüne
  kadar staged kalır. Mevcut genel vergi ana verisi yetkisi değiştirilmez.
- Owner API, Purchasing/Sales/Settings UnitTests ve Api.IntegrationTests
  derlemeleri exit 0, uyarı/hata 0 aldı. Birleşik merkez TypeScript exit 0;
  son merkez provider/authority-test kaynaklarının C# derlemesi sıradadır.
  Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**; kaynak DONE sayısı artırılmadı.

## Sıralı devam ve tartı/fatura claim kaynağı

- ALS-D105 merkez birleşimiyle API, Settings/Purchasing/Sales UnitTests ve
  Api.IntegrationTests kaynak derlemeleri exit 0, uyarı/hata 0 aldı. İki Ayarlar
  ekranının önceki merkez TypeScript derlemesi exit 0'dır.
- `f8f39bbc9` / rapor `4c6aaa3e1` ile fiyat taşımayan Sales claim reader ve
  gerçek Lojistik işlem içi yeniden okuması alındı. Merkez Sales.UnitTests ve
  Api.IntegrationTests derlemeleri exit 0; index Down round-trip fact kaynağı da
  bu derlemeye dahildir. Runtime kabulü **NOT_RUN**; tartı düzeltme host'unun
  claim bağlaması ve kesinleşmiş iptal zinciri halen açıktır.
- EF `20261117135232_AddSubcontractInvoiceClaimRange` yalnız index ekler/kaldırır;
  veri kaybı yok, yeni THROW yok, DB uygulaması yok. İki yeni borrowed read-context
  construction kaynağı K3 incelemesine bırakıldı; guard allowlist genişletilmedi.
- Paralel yürütücüler sonlandı; kalan kaynak işi tek yazarla sırayla sürer.
  STK-S3 / STK-S29 yarım kaynakları korunur; migration/host/PDF bağlantısı
  tamamlanmış sayılmaz. Üretim completion V2 kanıtı henüz yazılmadı.
- Ana plan ALC-S7 ve ALC-S8 için PLANNED yerine gerçek PARTIAL durumuna geçirildi.
  **135 DONE (%77,6), 25 PARTIAL, 14 diğer açık; toplam 174, açık 39**.
  Kaynak kodun ilerlemesi tam dilim DONE veya runtime kabulü diye sayılmadı.
  W8 son pakettir; yeni dalga yok. Pinler ve tren FF henüz tamamlanmadı.

## W8 paralel devam ve yeni insan cevapları

- Daha önce kota hatası veren mevcut yürütücüler yeniden yanıt verdi; üretim,
  stok/lojistik ve finans işleri ayrı worktree/dosya sahipliğiyle yeniden başladı.
  Tek .NET/EF ve tek Node derleme slotu, açık lease devri ve her yeni araç
  sürecinde en az 2 GiB boş RAM koşulu korunur. Yeni dalga açılmadı.
- `93bfba4ac` / `ba38bc6da` / `cea19b720` kaynakları merkezde alındı. Inventory
  fason müşteri malı rezervasyon/sevk participant'ı ve Documents marka/şablon
  depoları PARTIAL'dır; gerçek host/renderer eksikliği başarı sayılmaz.
  Merkez API, Documents.UnitTests, Inventory.UnitTests, Api.IntegrationTests
  derlemeleri exit 0, hata/uyarı 0; birleşik TypeScript derlemesi exit 0.
- Oturumdaki doğrudan insan cevapları, kaynak sınırı açık ajan aktarımlarıyla
  Issue #1189'a kaydedildi: [6951273717](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6951273717)
  yalnız faturalanan maliyet payının kesinleşmesi ve kalanın beklemesi;
  [6951274243](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6951274243)
  ilk SMM önerisinin kesinleşen faturanın gerçek sevk maliyeti payından oluşması;
  [6951274771](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6951274771)
  resmen iptal edilen fason faturanın ters kaydında seçilen açık dönem/tarih ve
  farklı muhasebe onaylayanı. Bu aktarımlar bağımsız DOCS incelemesi değildir.
- KIM-S8 ve STK-S29 ana planda gerçek mevcut kaynakları nedeniyle PARTIAL'a
  hizalandı. **135 DONE (%77,6), 27 PARTIAL, 12 diğer açık**; toplam 174 ve
  açık 39 değişmedi. Runtime Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**.

### STK-S29 gerçek üretici ve merkezi bağlantılar

- `e53576af9` gerçek PDF üretimi, yayımlı şablon/varlık kaynağı, Inventory
  sabit yayımla tekrar üretim ve yalnız miktarlı Logistics irsaliye PDF okumasını
  ekledi. Product görünümü gerçek sahibinden fiyat içermeyen dar sözleşmeyle
  okunur; mevcut Product runtime kapısı korunur.
- Yedi Documents izni yalnız kayıtlıdır; grant, rol veya Critical değişikliği
  yoktur. Mevcut Settings shell sahibi altında dört ekran ve bağımsız
  hazırlama/yayımlama kontrolleri bağlandı; yalnız yayımlama yetkilisi de tek
  menü öğesinden erişebilir. Teknik kaplar runtime kabulüne kadar staged kalır.
- API, Identity.UnitTests, Api.IntegrationTests ve Accounting.UnitTests
  kaynak derlemeleri exit 0, uyarı/hata 0; birleşik TypeScript derlemesi exit 0;
  `git diff --check` exit 0. SQL/HTTP/Vitest/browser/CI **NOT_RUN**.
- MIME alt kümesi yapılandırma devamı, merkezi pinler ve yayımlanmış trene
  fast-forward beklediğinden ana plan STK-S29 halen PARTIAL'dır.


## STK-S3 — gerçek fason teslim merkezi bağlantısı

- Owner kaynakları `b6774f25c` / `415f46df4` merkeziye alındı. Gerçek Sales, Manufacturing,
  Inventory ve Logistics zincirinin host/runtime endpoint kayıtları bağlandı; hizmet
  ürünü ile fiziksel etiket ürünü ayrı kalır. İşlem içinde tek owner yazımı korunur.
- Logistics provider 1.5, sekiz yeni S3 screen/action bildirimi: tamamı kabul kanıtına
  kadar staged. Mevcut ordinary-delivery bildirimleri yayımlanmış durumunu korur.
  `logistics.weighings.correct` yalnız All/CompanyWideOnly kayıtlarına eklendi;
  Critical/grant/rol ataması yapılmadı.
- Üç ekran, sekiz capability ve tek Fason teslimler menü girdisi gerçek owner sayfalarına
  bağlandı. Normal Logistics mount-drift envanteri eski ve yeni owner kümelerini birlikte
  korur. Yetki/route/menü karşı örnekleri ve kayıt/model test kaynakları hizalandı.
- API, Logistics.UnitTests, Accounting.UnitTests, Identity.UnitTests ve Api.IntegrationTests
  kaynak derlemeleri exit 0, 0 uyarı/hata. Merkezi TypeScript derlemesi exit 0; hafif
  backlog/bootstrap doğrulayıcıları exit 0. Runtime Unit/SQL/HTTP/Vitest/browser/CI NOT_RUN.
- 100 ayrılıp 80 yüklenen kısmi sevkte kalan ayırma politikası insan cevabı bekliyor.
  Yeni S3 host'un negatif fiziksel ret günlüğü/detail takip düzeltmesi mevcut S21
  diliminde sürer. Bu nedenle STK-S3 bütünü PARTIAL; yeni dalga açılmadı.
- Registry/OpenAPI/recovery/architecture/relational/test-group pin üretimi ve tren FF
  henüz yapılmadı. Kaynak derlemesi runtime veya kullanılabilirlik kabulü değildir.


## FIN-S17 — gerçek devir owner'ı ve transaction boyunca dönem çiti

- `5edcd80b0` ve `dd46601ae` owner kaynakları merkeziye alındı. Accounting kapanışı
  ile Finance para birimi bazlı gerçek devir fişi tek Serializable host transaction'ına
  bağlandı. Fiziksel/finansal olay ikinci kez üretilmez; cari ekstre açılış kanıtı ve
  aralık köprüsü toplamı çift saymaz. İki kişili yeniden açma mevcut izinlerle kalır.
- Gerçek `FinancePeriodCarryForwardPortV1` kaydedildi. Finance kayıt dönem kuralı,
  gerçek borrowed Accounting owner okumasıyla işlem bitimine kadar kilidi koruyan
  `TransactionalOccurredOnFinanceRecordPeriodRule` oldu; AddFinanceServices öncesinde
  tek scoped descriptor kaydı korunur. Tarih seçimi occurredOn olarak kalır.
- Composition kaynak testleri tek descriptor/fallback ayrımını ve occurredOn'un
  valueOn/recordedAt'ten bağımsız gerçek port girişine taşındığını doğrular.
- Son merkezi API, Accounting.UnitTests, Identity.UnitTests, Logistics.UnitTests ve
  Api.IntegrationTests derlemeleri exit 0, 0 uyarı/hata. Owner Finance.Unit/TSC de exit 0.
  Runtime Unit/SQL/HTTP/Vitest/browser/CI NOT_RUN.
- Banka işlem tarihi/muhasebe tarihi devir ayrımı ve sonraki kapalı devir zinciri
  insan cevabı bekler; kaynak fail-closed kalır. FIN-S17 PARTIAL; pinler/tren FF bekler.


## FIN-S13 / ALS-D7 — gerçek ürün araması ve tür sahibi merkezi bağlantısı

- `AddPurchaseInvoiceImportProductSearch()` gerçek host kaydı eklendi. Aynı scoped adapter gerçek Product aday okuyucusunu ve bağımsız tür/sürüm portunu sağlar; `General` varsayımı kullanılmaz. Service ürünü mal satırına çevrilmez; hizmet/masraf için açık Ayarlar seçimi sürer.
- Owner kaynakları `c5570b816` ve raporu `cdb3a521e` merkezi entegrasyona alındı. Yetki/şirket değişimi ve eski nullable snapshot/hash uyumluluğu owner test kaynaklarında korunur.
- Birleşik kaynakta API, Purchasing.UnitTests, Inventory.UnitTests ve Api.IntegrationTests derlemeleri exit 0, 0 uyarı/hata. Standart tek süreç parametreleri ve her süreç öncesi bellek kapısı kullanıldı. Owner TSC exit 0; merkezi yeni frontend farkı için son birleşik TSC ayrıca alınacak.
- Runtime birim/SQL/HTTP/Vitest/browser/CI ve merkezi pinler NOT_RUN. FIN-S13 bütünü için bu kayıt tek başına DONE/kabul değildir.


## STK-SC36-a / SAT-D385/D404/D413 — gerçek geç alış faturası maliyet bağlantısı

- Owner checkpoint `0501f9abd` merkezi entegrasyona alındı. `AddInventoryPurchaseInvoiceCostsV1()` gerçek Inventory store'unu scoped kaydeder; gerçek Purchasing finalized olayı Work manifestinde Inventory maliyet tüketicisine bağlanır. Inventory maliyet farkı olayı da kaynak bildirimine eklendi. Dispatcher/production ayarı değiştirilmedi.
- Composition test kaynağı gerçek registry doğrulayıcısı, tüketici/source event eşleşmesi, gerçek scoped store türü ve dispatcher başlatılmamasını kontrol eder. Yeni tablo/migration/immutable guard kaynakları owner raporundadır; sessiz guard izin listesi eklenmedi.
- Birleşik kaynakta API, Purchasing.UnitTests, Inventory.UnitTests ve Api.IntegrationTests derlemeleri exit 0, 0 uyarı/hata; `git diff --check` exit 0. Runtime birim/SQL/Vitest/browser/CI ve pin üretimi NOT_RUN.
- İlk maliyet tanımasının kısmi stok/sevk payı ve parasal artık politikası için #1189 `6954342313` ve `6954297311` cevapları bekleniyor. Accounting gerçek SMM önerisi/eşleme kaynağı ilgili owner'da sürüyor. Dilim PARTIAL; bu bağlantı maliyet kabulü veya production aktivasyonu değildir.


## ALC-S5 / SAT-D306,D392,D393 — gerçek alış iade maliyet sağlayıcısı

- Owner `c67daf71f` merkezi entegrasyona alındı; `IPurchaseSupplierReturnInvoiceCostReadPortV1` gerçek `PurchaseSupplierReturnInvoiceCostReadPort` scoped kaydıyla bağlandı. Fiziksel kabul okuması mevcut gerçek `PurchaseGoodsReceiptHost` üzerinden sürer.
- Normal komutun işlem öncesi sorgusunda tek kesin fatura satırının tek tam kabule bağlı net TL/orijinal kg kanıtı kullanılır. Kaynak yokluğu mevcut manuel/onay yolundan ayrıdır: bozuk kanıt, tam dört ondalıkta temsil edilemeyen kesin maliyet ve desteklenmeyen aktif borrowed transaction açık 503 verir. Başka bağlantı aynı işlem diye gösterilmez.
- Owner altı gerçek SQL fact kaynağı yazdı; merkezi composition testi gerçek scoped sağlayıcının varlığını kontrol eder. Birleşik API, Accounting.UnitTests, Logistics.UnitTests ve IntegrationTests derlemeleri exit 0, 0 uyarı/hata. Owner Purchasing.UnitTests de exit 0; ilk eksik assets/dar restore kanıtı owner raporundadır.
- Runtime testler, merkezi pinler ve tren fast-forward henüz NOT_RUN; kabul/production değildir.


## STK-SC36-a / SAT-D419,D420 — gerçek SMM hesap eşleme merkezi akışı

- Owner `c51d83d86` gerçek eşleme workspace'i, yalnız onaylı plandaki 621/620 seçenekleri, Product sınıfı/eşleme sürümlü resolver ve gerçek hazırlama/yayımlama ekranıyla entegrasyona alındı. Mevcut diğer eşlemeler hazırlanırken korunur; hesap planı veya eşleme kendiliğinden kurulmaz.
- Gerçek workspace endpoint'i ve scoped resolver bağlandı. Muhasebe altında tek hesap eşleme rotası/menü girişi ve ayrı hazırlama/etkinleştirme kontrolleri eklendi. Üç yetenek mevcut muhasebe eşleme izinlerini kullanır, şirket bazlıdır ve `ImplementationAvailable:false` kalır. Provider 1.8; yeni izin, grant veya Critical sınıf değişikliği yoktur.
- Shell/route drift ve owner capability test kaynakları yeni kaydı ve mevcut yayımlanmış kümenin korunmasını doğrular. Birleşik API, Accounting.UnitTests, Logistics.UnitTests, IntegrationTests derlemeleri exit 0, 0 uyarı/hata; merkezi TSC exit 0. Owner gerçek EF `20261117171929_WidenPostingMappingsToCogs`, Down 51929 ve SQL fact kaynakları raporundadır.
- Runtime SQL/Vitest/browser/CI, merkezi pin üretimi ve tren FF NOT_RUN. Bu kayıt ilk SMM veya geç fatura farkı önerisinin tamamlandığı anlamına gelmez; ilgili Accounting sahibi kaynak akışını sürdürür.


## STK-SC36-a / SAT-D419 — ilk SMM için gerçek sevk kimlik zinciri

- Sales owner `fdc417b38` / rapor `c0a799e09` normal kesinleşmiş fatura satırına değişmez `ShipmentSource` ekler; bu yeni bir stok/para olayı değildir. Ticari fatura kg'sı fiziksel çıkış kg'sı sayılmaz ve geçmiş outbox yeniden yazılmaz.
- Inventory owner `0eedb43ba` gerçek shipment kimliğinden kendi tek komut/sonuç/hareket/outbox izini çözerek mevcut orijinal TL damgası okuyucusuna bağlanır. Available yalnız fiziksel iz kanıtıdır, Known TL iddiası değildir. Yeni model/EF yoktur.
- Logistics owner iç `ILogisticsInvoicedShipmentProofReadPortV1` okuması aynı şirket ve dayanıklı sürümde onaylı irsaliye satırı, gerçek sevk payı, talimat satırı, sipariş/ürün/birim kimliklerini çapraz doğrular. Fiyat, maliyet ve oran üretmez. Kullanıcı yetkili irsaliye seçicisi değiştirilmez; yeni browser route veya grant yoktur. Bu sistem amaçlı çapraz modül okuma yüzeyi K3 review bekler; guard izin listesine sessiz eklenmedi.
- Altı unit fact kaynağı ve mevcut gerçek SQL source fact'inde çoklu sipariş, yabancı şirket, yanlış sevk ve iptal kanıtları eklendi. API, Accounting.UnitTests, Logistics.UnitTests, IntegrationTests birleşik kaynak derlemeleri exit 0, 0 uyarı/hata; runtime NOT_RUN. Yeni ilişkisel FQN yok; Inventory owner üç yeni SQL fact'i final envanter üretiminde alınacak.
- Accounting gerçek hazırlama/onay ve iptal zincirini diğer kaynaklarla birleştirir; ilk SMM bütünü henüz DONE değildir. Merkezi pinler, tren FF ve gerçek kabul NOT_RUN.


## STK-SC36-a / SAT-D419 — sevk kanıtı değişirken fail-closed kaynak testi

- Logistics owner kaynak anlık görüntüsü alındıktan sonra irsaliyenin gerçek domain iptalini araya alan deterministik bir unit fact kaynağı eklendi. İkinci owner okuması değişen sürüm/iptali görür; maliyet tüketicisine eski onaylı kanıt verilmez. Mevcut fake-store hook varsayılan boş kalır.
- Birleşik API, Accounting.UnitTests, Logistics.UnitTests, Finance.UnitTests, IntegrationTests derlemeleri exit0; runtime unit/SQL ve eşzamanlı gerçek SQL kanıtı NOT_RUN. Bu kaynak testi gerçek SQL yarış kabulü diye sunulmaz.


## STK-SC36-a / FIN-S13 / FIN-S14 — gerçek maliyet önerileri ve kur kaynaklarının merkezi montajı

- Accounting owner `b5dbcca00` ve P2 düzeltmesi `5e459ceff` merkeze alındı. İlk Sales sevk SMM ve geç Inventory alış farkı, gerçek owner source reader/store/service ile bağlandı; `accounting.inventory-cost-proposals` Work tüketicisi iki gerçek yayımlanmış olaya kayıtlıdır. Dispatch kendiliğinden açılmaz. Gerçek endpoint grubu, mevcut prepare/approve/reversal izinlerine ayrı beş ekran rotası ve menüyle bağlandı; tüm yeni declarations staged kalır. Accounting provider1.9/28declaration; yeni permission/grant/Critical yoktur.
- Owner ilk SMM kaydı mevcut invoice-line/version trace ve iki satırlı gerçek fiş akışını korur. Kaynak/fatura iptali, açık dönem, profil, sınıf ve hesap eşleme güncelliği hazırlama/onayda tekrar ölçülür; eski stok hareketi yeniden yazılmaz. Kaynak seçenek okumasının hata yolundaki fresh authority kontrolü ve on test vaka kaynağı korunur.
- FIN-S14 owner `92235738f` / rapor `da6397da6` gerçek Settings para profili/TCMB quote kaynağı, değişmez kur snapshot'ı ve existing transfer UI ile merkeze alındı. `AddFinanceCashExchangeSources()` merkezi DI'ya bağlandı. İki finansal TL değerinin kararı ve muhasebe eşleme sınırı halen açıktır; benchmark farkı otomatik kâr/zarar değildir.
- Birleşik API, Accounting.UnitTests, Logistics.UnitTests, Finance.UnitTests ve IntegrationTests derlemeleri exit0, sıfır uyarı/hata; merkezi `tsc -b` exit0. Work registry ve DI kaynak testleri gerçek iki event ve owner implementasyonlarını kapsar. Yeni shell beyanları mevcut published kümeyi değiştirmez; route drift kaynakları eklendi. Güncel parent-control sayısı mevcut altı kayıtla hizalandı; guard fingerprint üretici aşamasındadır, elle hash değiştirilmedi.
- EF kaynakları owner raporlarındadır: Accounting inventory proposals51930/51931/51932 ve Finance cash snapshot51933. İki Finance raporu append çatışması her iki kayıt korunarak çözüldü; uygulama kaynak/test çatışması olmadı.
- Kalan: para ölçeğine sığmayan exact ilk SMM/fark ve BS1 pay kararları; original-Unavailable sevkin sonradan generic üretim SMM'sine gerçek fatura satırı bağlantısı; gerçek iptal producer yetkisi; kur değerleme kararı. Bunlar tamamlanmadan ilgili dilimler DONE/usable sayılmaz. Runtime birim/SQL/Vitest/browser/CI, pin üretimi ve tren FF NOT_RUN.


## STK-SC36-a / SAT-D419,D422 — değişmez üretim/sevk izi, fatura claim okuması ve ekran kanıtı

- Sales owner `216bf74ce` gerçek finalized invoice/outbox/immutable shipment paylaşımını company-filtered, en fazla128 kimlikli typed iç portla okur. Her asenkron sınırda gerçek tüketici çiti gerekir; çift snapshot yalnız point-in-time kanıttır, son yazım kilidi değildir. Actual ilk PriceBasisRevision=0 kabulü maliyet adapter'ında korundu. Merkezi scoped Sales reader kaydı ve DI kaynak testi eklendi; browser endpoint veya yeni permission yoktur.
- Inventory owner `870695de7` mevcut generation TX ve FenceLineage içindeki gerçek shipment/movement/line/root paylarını/iade bağlarını değişmez history JSON'unda tutar. Outbox küçük generation/version/history-SHA referansı taşır; gerçek typed reader kayıtlıdır. Geçmişte eksik kanıt bugünkü grafikten üretilmez. RecordedAtUtc gerçek hareket kanıtıdır; sonradan eklenen rowversion ilk creation sırası sayılmaz. Kesin eşit zaman sırası kanıtsızsa açık unresolved kalır. Yeni model/migration yoktur; altı yeni SQL fact kaynağı final envanter üretimini bekler.
- Owner `a83142368` iki bozuk yanıt kapısını ve unknown-replay sürekliliğini düzeltir. Hazırlanmış tutar gösterilen canonical tutarla uyuşmalıdır; exact oranlı bekleyen kaynaklar backend'in gerçek metin biçimini korur. Orijinal JournalId ile actual CurrentPostedJournalId ayrıdır; SAT-D422 replacement sonrası ters öneri gerçek güncel fiş kimliğine bağlanır. Capability/context revision yenilendiğinde query çiti değişir, fakat aynı session/actor/company/kind için bilinmeyen isteğin anahtar/gövdesi korunur. Başka kimliğe taşınmaz.
- Birleşik API, Inventory.UnitTests, Sales.UnitTests, Accounting.UnitTests ve IntegrationTests derlemeleri exit0; merkezi `tsc -b` exit0. Runtime birim/SQL/Vitest/browser/CI, OpenAPI/pin üretimi ve tren FF NOT_RUN. Owner kaynak incelemeleri K3 exact-head imzası değildir.
- Kullanıcının doğrudan attribution dağıtım cevabı #1189 aktarımları6956807403 ve6957337880'dedir: her gerçek output'un kayıtlı SMM toplamı kg paylarına bölünür; artık en ağır/önce kaydedilen sevkten başlanarak toplam korunacak şekilde uygulanır, pay sıfıra ulaşınca kalan aynı sıradaki sonraki paya geçer. Bu yalnız mevcut fişin pay bağlantısıdır; ikinci SMM yaratılmaz. Ayrı ilk BS1 ve fiş ölçeği kararları bununla seçilmez. Accounting gerçek attribution/tek ekonomik claim/SAT-D422 component bağlantısı halen WIP olduğundan enclosing dilim DONE değildir.


## STK-SC36-a / BS-1 — ilk alış maliyeti kaynak checkpointi PARTIAL

- Doğrudan kullanıcı cevaplarının açık ajan aktarımları6958072219 (5/6) ve6958102286 (11) uygulanır. Gerçek Purchasing kaynak/acceptance/version/document çitleri korunur. Bilinmeyen orijinal maliyet sıfır veya bugünkü ortalama yapılmaz; ilk tanıma geç farktan ayrı, immutable InitialRecognitions kanıtıdır.
- İlk kısmi fatura mevcut fiziksel stok/net sevk kök oranını kullanır; mevcut producer yalnız acceptance bağı taşır, bulunmayan daha dar fiziksel bağ uydurulmaz. Faturalanmayan oran pending kalır. Orijinal receipt/issue stamp ve miktar hareketi değişmez. Şirketli aynı Serializable işleminde gerçek lineage label sürümleri çitlenir; exact miktarlı özel priced pool yalnız faturalanan stok payını alır. Tamamlanmamış ortalama bütün stok maliyeti diye yayımlanmaz.
- SMM payı gerçek şirket TL journal profile ölçeğiyle bir kez yuvarlanır; exact pay/signed artık/profil-kimlik/sürüm/ölçek korunur. Stok payları authoritative kalanı korur; artık kg/ilk kaynak satırı/ordinal lokasyon kodu önceliğindedir, pozitif pay eksiye düşmez. Tam sevkte alt-kuruş farkı sahte stok yaratmaz. Çok küçük kaynak bir yuvarlanmış SMM payını finanse edemiyorsa negatif stok değeri üretilmez; açık refusal kalır.
- Mevcut Settings journal profile timeline için dar internal şirketli reader ve Organization gerçek lokasyon kodu host adapterı eklendi. Kullanıcı taklidi, yeni profil/ölçek varsayımı, grant veya HTTP capability yok. Bu yeni automation yüzeyi K3 review bekliyor; filtre bypass/ham SQL/allowlist eklenmedi. Model/migration değişmedi.
- Altı birim test kaynağı, iki gerçek Inventory SQL fact kaynağı ve gerçek Settings journal timeline/şirket/aktiflik SQL fact kaynağı yazıldı. Actual owner portları SQL fixture sınırlarında sentetik olduğu yerde açık gösterilir. İlk Unit compile CS8752 üç params/target-new hatası verdi; açık türle düzeltilip ilk log/exit korundu. API kaynak derlemesi0; Unit tekrar0; Integration ve API bağımlılık derlemesi0, .NET0 uyarı/hata. Sonraki typed physical DTO/Settings fact/owner-fence ekleri yeni birleşik derlemeyi bekler; exact son head PASS denmez.
- Gerçek onaylı ilk purchase SMM payının Sales fatura claim'ine ikinci fiş yaratmadan bağlanması ve Accounting parser/RecordedRounding entegrasyonu halen kaynak işi olarak sürer. Bu checkpoint enclosing dilimi DONE yapmaz. Runtime unit/SQL/HTTP/Vitest/browser/CI ve pin üretimi NOT_RUN; tren135/174 (%77,6), W8 son pakettir, W9 açılmaz.


## STK-SC36-a / BS-1 — tek fatura satırı, birden çok gerçek kabul

- İlk yerel kernelin kabul başına maliyet şekli yayımlanmadan düzeltildi: aynı fatura satırının gerçek kabullerindeki exact tüketilmiş TL katkıları toplanır; SMM satır toplamı yalnız bir kez gerçek şirket para profiliyle yuvarlanır. `.005 + .005` için `.01 + .01` üretilemez.
- Yeni immutable kaynakta satır başlığı tek maliyet bileşenidir; her kabulün gerçek acceptance/receipt/root kimliği, kendi fiyatı, faturalanan fiziksel payı, tüketilmiş exact katkısı ve typed fiziksel JSON/hash korunur. Stok konumları satırda gruplanır; farklı kabullerin fiyatları ortalanarak yeniden seçilmez. Kalan stok parasal artığı mevcut kg/kaynak satırı/konum kodu kuralıyla korunur.
- İlk tanıma `purchase-first-line/{invoiceLineId}` Accounting bileşenine bağlandı. Kaynak yuvarlaması/profil sürümü/exact artık aynen taşınır; hayali tek kabul konumu veya Manufacturing generation/output kimliği yoktur. Exact tutar sıfır değil fakat fiş tutarı sıfırsa gerçek ayrı sıfır maliyet onayı için kanıt tutulur; sahte sıfır fiş oluşturulmaz.
- Adapter fiziksel/finansal toplamları, gerçek root/sevk/iade/konum paylarını ve kayıtlı yuvarlamayı doğrular. Kaynağın hash'i, satır/cari/ETTN bağı ve foreign company çiti bozulursa kalıcı ret olur. Fiziksel hareket veya eski unavailable maliyet damgası değiştirilmez.
- Üç yeni calculator, üç adapter ve bir gerçek SQL fact kaynağı eklendi; önceki tek-kabul SQL kaynakları yeni satır başlığıyla korunur. Bunlar test kaynaklarıdır; runtime/SQL koşulmadı.
- Bu checkpoint kaynak derlemesi **QUEUED**: Q11/line-key Accounting bağımlılıkları sonraki no-ff entegrasyonla alınacaktır. Önceki e408 kernel derlemesi bu yeni aggregate/adapter delta için PASS değildir. Migration yoktur. Inventory kaynak fact envanterine gerçek tam ad eklendi; guard hashleri final üretici sırasındadır.
- Dilim halen PARTIAL: actual purchase posted attribution, Mixed/SAT422 ve üretim delta bağlantılarının kalan kısmı Accounting sahibiyle tamamlanıyor. Bu kernel veya derleme, bütün STK-SC36-a'nın bittiği iddiası değildir.


## SAT-SC34 — merkez resmî iptal kaydı bağlantısı

- Owner checkpoint `5455cb849` no-ff ile alındı; gerçek Program'da `AddSalesFinanceCancellationHost`, mevcut Work registry'de Accounting Sales cancellation consumer/subscription bağlandı. Dispatcher/production ayarı açılmadı.
- Sales shell 1.26, yalnız mevcut `sales.invoices.finalize` yetkisini kullanan tek şirketli, fiyat gerektirmeyen bağımsız `/satis/fatura-resmi-iptal` ekranını yayımlar. Yeni izin/grant/rol/Critical yoktur. Ekran Sales route listesine ve merkezi menüye bağlandı; backend yetkisi her istekte tekrar ölçülür.
- Gerçek sağlayıcı yokluğu typed 503 olarak korunur (kullanıcı Q12). Resmî iptal olmuş gibi kanıt üretilmez. Kaynak/route/host hazır olması dış belge iptalinin kullanılabilir kabulü değildir; dilim PARTIAL kalır.
- Provider/host/mount sözleşme test kaynakları güncellendi; owner'ın altı gerçek SQL fact tam adı sıralı Sales envanterine eklendi. Shell registry otoriteleri, OpenAPI, recovery ve guard pinleri finalde üreticiyle yeniden üretilecek; elle hash yazılmadı.
- Owner API/EF/Sales.Unit/Integration/TSC derlemeleri exit0. Bu merkezi delta için derlemeler QUEUED; runtime/SQL/Vitest/browser/CI NOT_RUN.


## STK-S3 — ayrı yetkili kullanılmayan fason ayırma komutu

- Owner kaynak checkpoint'i `372e27303` alındı; gerçek Program DI ve endpoint mount'u bağlandı. Kullanılmış pay veya fiziksel stok/hareket değişmez; yalnız aktif kullanılmayan pay açık komutla bırakılır. Sipariş/label/capacity çitleri owner işleminde kalır.
- Inventory shell 1.13 bağımsız `/stok/fason-ayirma-birak` ekranını yalnız mevcut `inventory.physical-allocation.write` izniyle yayımlar. Teknik route capability `inventory.subcontract-allocations.release-unused` bir ERP izni değildir; eski stok-detail control'ünün ekstra okuma izinleri yeni write-purpose gateway'e yüklenmez. Sayfa, route ve menü aynı teknik capability'ye bağlandı.
- Kayıtlı fresh izin/version/idempotency/Audit/replay ve HTTP/fiziksel rollback kaynakları owner'dadır. Merkezi mount/provider test kaynakları ve üç gerçek SQL fact tam adı güncellendi. Şema/migration, yeni izin/grant/rol veya production aktivasyonu yoktur.
- Kaynak derleme owner zinciri sürüyor; merkez delta için son birleşik derleme QUEUED. Runtime/SQL/Vitest/browser/CI NOT_RUN. Pinler final üretici sırasındadır; elle hash yazılmadı.


## Birleşik kaynak derleme checkpoint'i — 398a55566

- Kaynak bu head'de bütün zincir boyunca donduruldu. API **exit0**, Inventory.UnitTests **exit0**, Sales.UnitTests **exit0**, Api.IntegrationTests **exit0**; tamamı 0 uyarı/0 hata. Parametreler `--no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false -v:q`. Native log/exit kayıtları depodışında korundu.
- Aynı kaynakta `npx tsc -b` **exit0**. .NET ve Node ayrı lease'lerle, her süreç başlangıcında >=2GiB fiziksel boş bellek ölçülerek çalıştı. Sonrasında iki lease bırakıldı; model/SQL çalıştırılmadı.
- Bu kaynak ilk alış invoice-line aggregate + API adapter/Q11 recorded rounding/Mixed tüketimi, Q7 numaralama/gerçek EF, resmî iptal host/subscription ve iki yeni standalone route'u beraber derler. Eski ayrı owner compile sonuçlarından birleşik PASS türetilmedi.
- Hafif kontroller: `validate_delivery_backlog` exit0; `validate_dotnet_bootstrap_integrity` exit0; delivery Python paketi 64 test/skip0/exit0; bootstrap Python paketi 56 test/9 koşullu skip/exit0. Skipler mevcut dosya sistemi symlink/case-variant yetenek sınırlarındandır; skip0 kanıtı değildir, koşumlar değiştirilmedi. `git diff --check` exit0.
- Bunlar kaynak derlemeleri ve izinli hafif doğrulayıcılardır. Tam runtime Unit/mimari/gerçek SQL/HTTP/Vitest/browser/CI, OpenAPI/pin üretimi, tren FF ve K3 kabulü henüz tamamlanmadı. Yeni Accounting purchase attribution ve KIM-S19 kaynakları owner dallarında sürer; bu checkpoint onları kapsamaz.
- STK-S3 sekiz fason shell deklarasyonunun `ImplementationAvailable:false` bekleyişi kendi staged-profile kabul kapısıdır. Kabul koşmadan sunum kapısını true'ya çevirip testi atlamadık; gerçek endpoint/route/maliyet kaynaklarının varlığı kullanılabilirlik kanıtı sayılmaz. Son kabul/publish işi açık kayıtlıdır.

## 18 Kasım — ilk alış atfı ve SAT-S12 P2 birleşik kaynak checkpoint'i

- U `938b171e7` Settings teminat okuma çiti ve Finance `3ea0f8300` gerçek ilk alış maliyeti atfı no-ff ile çakışmasız alındı. Actual purchase lineage reader ve posted purchase reader scoped DI kayıtları, mevcut provider zinciri ve kompozisyon test kaynakları bağlandı. Yeni kaynaklar helper olarak kalmadı; gerçek Inbox/öneri/onay/Sales tüketimi aynı owner sözleşmelerini kullanır.
- `65618ac1ec70b97d2a5d05bc6f6cf05e2f2dcea9` kaynağı zincir boyunca sabit kaldı. API, Accounting.Unit, Sales.Unit, Integration ve TSC derlemeleri exit0, backend 0 uyarı/0 hata. En az 2 GiB fiziksel RAM ve tek .NET/ayrı Node lease kapısı korundu. Derlemeler OpenAPI üretimi kapalıydı; runtime Unit/SQL/HTTP/Vitest/browser/CI veya DB apply değildir.
- Dört purchase attribution, dört üretim transfer geri alma, yedi finans numaralama ve iki Settings geç yetki kaybı SQL fact tam adı gerçek kaynaklardan merkezi sıralı envantere eklendi. Hash/fingerprint elle değiştirilmedi; final pin üretimi bekler.
- SAT-S12 P2-1 gerçek Settings TL kimliğine bağlandı; USD/USD snapshot bile TL marjı gibi hesaplanamaz. Golden DOCS adayı #1200 exact `63913e087f850441f51be2136977344b6410102a` ayrı main tabanlı tek dosyalık daldadır. İki bağımsız DOCS ve en az bir ayrı ChatGPT golden kabulü/main koşulu tamamlanmadan kapı açılmaz; son K3 ayrıca sürer.
- Yeni doğrudan sıfır maliyet cevabı `6960913297` üzerine `11e2f6e` kaynağı yazıldı; canlı ve restore sıfır maliyet marjı `CostUnreadable`, sipariş iç onayı engellenmez. Bu ekin Sales.Unit kaynak derlemesi exit0, 0 uyarı/hata; regression runtime koşulmadı. Sonrasında .NET lease Finance SAT-D422 kaynağı için açıkça bırakıldı.
- Yerel kaynak sayımı değişmedi: 165/174 (%94,8), 9 açık; yayımlanmış tren135/174 (%77,6). Yeni dalga/implementation PR/push veya runtime başarı iddiası yoktur. #1200 yalnız gerekli bağımsız golden DOCS adayıdır.

## Sonraki kaynak devamı — 166/174

- FIN-S13 özgün B-04 fatura→120/320 kaynak bağlantısı gerçek kaynak/ancestry/Program/Inbox/route ile kapandı: yerel166/174 (%95,4), 8 açık. İlk SMM/üretim atfı ayrı STK-SC36-a içinde kalır; ayrıntı `W8_FIN13_KAYNAK_KAPANISI.md`.
- Finance `189b335e1` gerçek alış payı SAT-D422 düzeltme checkpoint'i normal no-ff ile çakışmasız alındı; ek `IPostedPurchaseShipmentCostReaderV1` mevcut typed scoped constructor kaydından çözülür. Ayrı factory değişikliği yoktur. Üç gerçek SQL fact adı merkezi sıralı envantere eklendi; owner API/Accounting.Unit/Integration/TSC0, yeni birleşik head derlemesi QUEUED. Runtime kanıtı değildir.
- Stock Q10/FIN14 sözleşme `08b0d6dc9` merkezde no-ff alındı. Finance gerçek belgeli kaynak defter değeri/farklı onay ve Accounting bileşen köprüsü owner dallarında sürer; 51810 Accounting ve51912–15 Finance guard numaraları rezerve. Yeni Settings aileleri51807/08 için union teyidi kaynakta tutulur; gerçek EF üretimi dışında migration icat edilmez.
- SAT-S12 golden #1200 P2 düzeltmesiyle yeni head `dfab7e934fd6e7fc3b0dd5ca40bd9a3643d3ee4f`; iki yeni DOCS imzası bekler. Kullanıcı bu aday için iki ayrı bağımsız Claude oturumuna dar istisna verdi (`6961293621`); genel K3 kuralı değişmez. Banka listesi sorusu da yanıtlandı (`6961276925`); Settings/K6 kaynak çalışması devam eder.
- Entegrasyon ekip dalı normal push ile yedeklendi; `claude/tren-1`135/174 üzerinde kaldı. W8 son pakettir, W9/implementation PR/CI/deployment açılmadı; source build ile runtime kabulü ayrıdır.

## Golden main ve ayrı SAT-S12 kaynak kapanışı — 167/174

- PR #1200 exact `dfab7e934fd6e7fc3b0dd5ca40bd9a3643d3ee4f` iki bağımsız DOCS imzasıyla ayrı koordinatör tarafından main `3c1b47878447570210b933b83d874646f9c52e4b` oldu; #1189 makbuzu6961778667. Bu oturum kendi doküman PR'ını merge etmedi. Yeni main farkı yalnız tek golden dosyasıydı; `fb95e43cb` ile no-ff trene alındı, kaynak/test/migration çatışması yoktu.
- Ayrı uygulama `19ca53a178dc97e86cdc576ba775d9ee89543a4f` golden kabul koşulunu kaynakta karşılar. Gerçek TL para birimi bağı, sıfır maliyet unavailable, canlı önizleme/anlık görüntü ayrımı ve yetki geri alma korumaları korunur. 15 bağımsız golden vektörü gerçek uygulama hesap bağına; G01 ve foreign satış gerçek servis okumasına test kaynağı olarak eklendi. Sales.Unit kaynak derlemesi exit0/0 uyarı/hata/42,95 saniye. SAT-S12 kaynak DONE; yerel **167/174 (%96,0), 7 açık**. K3 ve runtime kabulü değildir.
- FIN14 gerçek belge kanıtı, owner-link DI, endpoint, iki ayrı staged purpose route ve Finance outbox manifesti merkez `6ff5e4f63` kaynağında bağlandı. API/Finance.Unit/Integration/TSC kaynak derlemeleri exit0, backend0 uyarı/hata; ayrıntı `W8_FIN14_MERKEZ_BAGLANTISI.md`. Yeni shell kayıtları ImplementationAvailable:false; kabul öncesi açılmadı.
- Accounting owner `c6555ab24` gerçek onaylı bozdurma kaynak/eşleme köprüsü trene no-ff alındı. Finance5/Accounting4 gerçek SQL fact adı sıralı envantere eklendi. Hedef de dövizken ayrı belgeli hedef TL consumer/owner kaynak checkpointleri ve gerçek sıfır maliyet atıfları ekiplerde sürer; FIN14/STK-SC36-a henüz DONE sayılmaz.
- Kaynaklar ayrı worktree'lerde sürer. Ağır .NET/EF/Node sahibi, açık lease devriyle Settings/K6 sahibine geçti. Runtime Unit/SQL/HTTP/Vitest/browser/CI, pin üretimi ve son tam dalga kabulü NOT_RUN. Yayımlanmış tren135/174; bu ara kayıt tren fast-forward veya yeni implementation PR değildir.
