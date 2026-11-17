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
