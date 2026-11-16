# W8 Kimlik — KIM-S5 explicit şirket açma ve ilk Admin seçimi

## Kaynak durumu

- **KIM-S5: SOURCE_READY — kaynak kullanıcı akışı ve derleme tamam; entegrasyon ve kabul koşumu bekliyor.** Bu kayıt global DONE, gerçek kullanıcı kabulü,
  gerçek SQL veya production kullanılabilirliği iddiası değildir.
- Dilim commit başlığı: `KIM-S5: explicit company opening and first Admin (KIM-D56 KIM-D20 KIM-D21 KIM-D28 KIM-D40)`.
  Kaynak commit `ca8121d4f90aaccda23f33a594d076fc9cc74d8e`. Önceki dar model assertion hizalaması ayrı `fd90499c9` commit'idir.
- Dayanak: [EK27 / KIM-D56](https://github.com/sinangym/GoymenERP/pull/1196) ve doğrudan kullanıcı A cevabı:
  yönetim veya genel müdür yetkililerinden biri açar; ilk Admin'i açıkça seçer; açana otomatik yetki verilmez;
  açma için ikinci kişi onayı aranmaz. KIM-D20 Pending yaşam döngüsü ve mevcut şirket bootstrap'ı korunur.

## Tamamlanan kaynak

- Ayrı typed `ICompanySetupGroupAuthorityV1`: şirket açma ve ilk Admin seçimi iki ayrı operation'dır.
  Identity'nin gerçek SQL sağlayıcısı yalnız açık insan kararına dayalı global operation kaydını, aktif kullanıcıyı,
  geçerlilik penceresini ve rowversion'ı okur. Şirket rolü, Product group yetkisi, rol/kişi adı veya opener kimliği
  otomatik grup yetkisi sağlamaz. Aynı request'te yetki cache edilmez; son owner lookup/replay'den sonra da yenilenir.
- `identity.company-setup.first-admin.assign` **yalnız kayıt** eklendi. Yeni Critical sınıfı seçilmedi;
  mevcut `organization.companies.open` Critical kaydı korundu. Gerçek grant, seed, rol ataması veya grant endpoint'i yoktur.
- Organization gerçek açma owner command'i: doğrulanmış VKN, expectedVersion `"0"`, açık gerekçe, global idempotency anahtarı,
  correlation/causation, aynı Serializable transaction'da Pending şirket + açılış outbox + terminal + Audit owner yazımı.
  Aktör/hedef değiştirerek anahtar bölünmez; replay aynı özgün cevabı ve correlation'ı verir.
  İş kuralı ve idempotency redleri mevcut F-023 portunu kullanır: red audit'ine serbest gerekçe/alan değeri/IP/UA taşınmaz;
  terminal red replay'i yeni audit satırı üretmez. Başarı audit'i gerçek oluşturulan alanları içerir.
- Identity açılış event consumer'ı yalnız immutable inbox/dedup kaydeder. Geç veya yinelenen eski olay da opener'a
  request, assignment, permission event veya first-Admin event üretmez. Tarihsel CompanyOpener satırları kalır;
  o modla yeni store yazımı reddedilir.
- Açık kullanıcı seçimi mevcut şirket Admin applock'ı altında no-Admin sayımı, aktif hedef kullanıcı, beklenen şirket sürümü
  ve yapılandırılmış Admin rolüyle yapılır. Yeni CompanySetupAuthority approval mode tek yetkili kararı gösterir.
  Aynı tx'deki owner proof olmadan doğrudan approval yazımı reddedilir. Deterministik first-Admin olayı değişmez;
  şirketi yalnız Organization consumer Active yapar.
- İlk Admin endpoint'i üyelik olmadan ancak kendi taze grup operation'ıyla çağrılabilir. Middleware owner şirket durumu
  okunmadan önce yetkiyi, okuma sonrasında özgün aktör/revision'ı doğrular. Active'te yalnız aynı komutun replay yolu
  erişilebilirdir; yeni ikinci Admin ve bütün normal şirket işlemleri mevcut çitlerde kalır.
- Gerçek UI `/kurulum/sirketler`: operasyonel şirket üyeliği oluşmadan server grup capabilities ile açılır.
  Şirket ve kullanıcı select'leri boş başlar; otomatik opener seçimi yoktur. Sunucu aktif adayları aranır.
  Yükleniyor/boş/yetkisiz/bulunamadı/çakışma/çevrimdışı/503/bozuk yanıt ayrı durumdur. Türkçe metin ayrı dosyadadır.
  Bilinmeyen yazma sonucunda özgün hedef/body/key server session binding'e bağlı sessionStorage'da korunur;
  aynı key ile retry yapılır. Bozuk yerel komut yeni key/varsayılan hedefe çevrilmez. Aktör/session değişimi eski cevabı reddeder.

## EF migration

- EF CLI 10.0.8 ile üretilen Identity migration: `20261116205256_AddExplicitCompanySetupAuthority`.
  Yeni global operation tablosu, check/unique/restrict-FK/rowversion ve tarihsel approval mode'ları koruyan CHECK genişlemesi.
  Down ilk işlemi **THROW 51947**: global yetki veya CompanySetupAuthority geçmişi varken kaybı reddeder.
- EF CLI ile üretilen Organization migration: `20261116205304_AddAuthorizedCompanyOpening`.
  Global-key terminal tablosu ve company/time index'i. Down ilk işlemi **THROW 51948**: herhangi terminal geçmişi varken reddeder.
- İki Up yalnız DDL'dir; seed/backfill/gerçek grant yoktur. Designer/snapshot üretici çıktısıdır.
  Migration DB'ye uygulanmadı; Down guard kaynakları yazıldı, gerçek SQL **NOT_RUN**.

## Test kaynağı ve doğrulama

- Identity birim/model/contract kaynakları: operation ayrımı; gerçek provider'ın revoke/expired/inactive/wrong actor reddi;
  şirket rolündeki aynı izin adlarının grup yetkisi olmaması; açık hedef/tek decider; proof ve immutable guard;
  global model CHECK/index/FK/rowversion; tarihsel mode ve Down koruması.
- Organization birim/model kaynakları: gerçek Pending/invoice-disabled factory; owner proof olmadan company/journal reddi;
  global terminal anahtarı, immutable ve migration DDL/Down sınırı.
- SQL fact kaynakları: çift event teslimde sıfır otomatik grant; seçilmiş farklı hedef; yalnız owner consumer aktivasyonu;
  Audit hatasında assignment/event veya açılış company/outbox/terminal rollback ve aynı key retry;
  iki yetkilinin explicit seçim yarışı; aynı global açma anahtarında tek commit; son gerçek terminal lookup sırasında
  grup yetkisi geri çekilince replay açıklanmaması; harmless/protected veride iki yeni Down guard;
  F-023 red alanları ve red replay'de tek audit.
- Middleware kaynak testleri: foreign company durum/owner hata bilgisinin grup yetkisinden önce okunmaması;
  mid-await yetki/membership kaybı, Pending/Active explicit seçim ve normal lifecycle 503 güvenli platform cevabı.
- UI/decoder/transport kaynakları: no automatic selection; exact DTO/required/extra/duplicate/empty-id/unsafe-version reddi;
  foreign success ve bozuk replay provenance bilinmeyen yazma sayılır; unknown 409/503 özgün key'i korur;
  ayrı durumlar ve aynı seçilmiş hedef/body/key retry.
- İlk checkpoint: API, Api.IntegrationTests, Identity.UnitTests ve Organization.UnitTests derlemeleri **exit 0, 0 hata/uyarı**.
  İlk Identity.UnitTests derlemesindeki nullable database-index assertion compile sorunu non-null DB adıyla düzeltildi;
  tekrar derleme 0. Son test/audit/endpoint ilaveleri sonrası Api.IntegrationTests, Identity.UnitTests ve Organization.UnitTests derlemeleri de exit 0 verdi.
- Son frontend `npx tsc -b` **exit 0**. İlk geçiş yeni ekranın generic identity tek-kayıt/liste tip ayrımını buldu;
  tek-kayıt okuyucusu ve liste biçimini reddeden UI kaynak testi ile düzeltildi. Yeni paket/sürüm/lockfile yoktur.
  Unit/Vitest/SQL/host/browser/CI ve DB apply **NOT_RUN**; hiçbir test silinmedi/skip edilmedi.

## Merkezi entegrasyon ve insan kapıları

- Yeni endpointler: GetCompanySetupCapabilitiesV1, GetPendingCompanySetupsV1, OpenCompanyV1,
  GetFirstCompanyAdminCandidatesV1; mevcut AssignFirstCompanyAdminV1 devam eder. Yeni DTO/status/replay ve
  grubun company-context-free sözleşmeleri OpenAPI üretiminde kök tarafından pinlenecek.
- SQL fact adlarının yeni kullanıcı kararı nedeniyle değişen beş eski girdisi ve ek fact'ler ilişkisel/test-group envanterine
  kökte yansıtılacak. Merkezi OpenAPI JSON/expectation, Shell authority, recovery/release, architecture/test-group pin,
  KALAN_PLAN ve production/Work dispatcher aktivasyonu bu dalda değiştirilmedi.
- Recovery'de W7 organization.company-setup-outbox producer source gap'ı bu gerçek opening writer ile kaynakta kapanır;
  envanter writer evidence yeni method üzerinden kökte yeniden üretilmelidir. Üretim kabulü anlamına gelmez.
- **K3 review bekliyor:** Identity CompanySetupGroupAssignments bilinçli CompanyId'siz global authorization kaydıdır;
  şirket kurulmadan önce yönetim/GM operation yetkisi ölçülür. Organization CompanyOpeningExecutions bilinçli global-key
  terminaldir; CompanyId taşır ama company query filter yoktur. Sadece group-authorized owner command okur, son lookup sonrası
  özgün aktör/revision çiti vardır; write proof target/company/actor/tx'yi bağlar. Bu gerekçeler guard'ı geçirmek için
  otomatik allowlist'e eklenmedi. Önceki W7 outbox horizon SQL inceleme kapıları aynen kalır.
- Gerçek yönetim/GM eligibility mapping/grant ve roller insan kapısındadır; kod rol adı veya persona'dan hak seçmez.
  İki bağımsız K3 inceleme, gerçek SQL/failure/browser/CI, dispatcher aktivasyonu ve production onayı ayrı kalır.


## KIM-S20 / STK-S11 typed birim kaynağı takip işi

- Kaynak durumu: SOURCE_READY; API ve Settings.UnitTests derlemeleri exit 0, test koşumu NOT_RUN. Kaynak commit `540ce04b6d91455120d03017789dcfed7b704002`. Master §28.1 ağırlık kg/ton ve ALS-D43/D51
  metre/adet kararları uygulanır. Yeni bir fiziksel veya ticari çevrim formülü seçilmedi.
- Ayrı `ISettingsPhysicalUnitSemanticsV1.ReadAsync(UnitId)` portu: Found/NotFound/Unavailable, istenen UnitId,
  açık Kilogram/Tonne/Metre/Piece sınıfı, yalnız kütle birimlerinde exact KgPerUnit (1/1000), kaynak sürümü.
  Metre/adet kilogram katsayısı null'dır; Product sahibi sürümlü çevrim olmadan mass varsayımı yapılamaz.
- Canonical UnitId'ler aynı mevcut UUIDv5 namespace/name yöntemiyle atanmıştır; code/name/dimension'dan tür türetilmez.
  Owner kaynağı kimliği veya meta verisi uyuşmazsa Unavailable, bilinmeyen kimlikte NotFound.
- Settings gerçek provider DI kaydı vardır. Yeni global kimlikler şirket profili oluşturmaz/aktifleştirmez;
  mevcut üç şirketin yalnız kg onaylı profilleri aynen kalır. Tüketici şirketin uygun tarihli profilini ayrıca doğrular.
- Unit/contract kaynakları dört exact identity ve source revision, kg/ton mass katsayıları, metre/adet null mass,
  kg adını taşıyan yabancı identity, bozuk owner cevabı, owner failure/cancellation ve unchanged company profillerini kapsar.
- Migration/DB/rol/grant/seed/production/pin yoktur. STK-S11 kullanımı kendi consumer diliminde bağlanır.

## KIM-S19 — numara serileri ve kayıtlı operasyonel politikalar

- **PARTIAL — bu owner/UI checkpoint'i SOURCE_READY; bütün belge tüketicileri ve bildirim deposu bağlantısı ayrı takiptedir.** Dayanak KIM-D53/N-12, SAT-D305, KLN-D114/D115 ve satın almayı da kapsayan yeni kullanıcı cevabıdır. Yeni varsayılan şirket formatı veya seed yoktur.
- Kontrat checkpoint'i `4e068eacac51347d85baba142a351e4d377787fe`: `ISettingsDocumentNumberSeriesV1`, `ISettingsOperationalPolicyAdministrationV1` ve typed politika reader'ları. Gerçek scoped provider/DI, endpoint ve üç Ayarlar ekranı bu checkpoint'te tamamlandı.
- Numara rezervasyonu aynı açık Serializable owner transaction/connection'a katılır; port commit etmez. Şirket/yıl sayacı, SQL rowversion, format sürümü, aynı komut anahtarı replay'i, iptal defteri ve boşluk listesi vardır. Her exception'da caller tam rollback yapmalıdır. Seri ve owner command kapsamı farklı belge sahiplerinin aynı opaque anahtarını birbirine karıştırmaz. Kullanıcı numara biçimi girmeden işlem fail-closed kalır.
- Hurda maliyete katılım başlangıcı yalnız sağlıklı boş-store okumasında true; bildirim etkinlik süresi başlangıcı aynı koşulda 90 gündür. Her değişim expectedVersion/idempotency/audit taşır. `settings.document-number-series.manage`, `settings.manufacturing-scrap-cost.manage`, `settings.notification-retention.manage` yalnız kayıt edildi; grant, rol veya Critical sınıfı seçilmedi.
- EF CLI 10.0.8 gerçek üretimi: `20261116220424_AddDocumentNumberSeriesAndOperationalPolicies`. Up yalnız dört tablo ve iki policy CHECK genişlemesidir. Down ilk adım THROW **51954** ile format/sayaç/rezervasyon/iptal veya yeni politika geçmişinde veri kaybını reddeder. DB apply NOT_RUN.
- Gerçek SQL kaynakları caller rollback/commit/replay, iptal sonrası numarayı tekrar kullanmama, yıl değişimi, şirket çiti, son owner lookup'ta yetki kaybı, aynı opaque key'in ayrı owner serileri, Down guard ve operasyonel politika tarihçesini kapsar. Model kaynakları filtre/FK/unique/rowversion/immutable ve şirket guard'larını; UI kaynakları açık giriş, tüm ekran durumları, strict DTO ve aynı key/body retry'ı kapsar. Runtime koşumu NOT_RUN.
- API startup, Api.IntegrationTests, Settings.UnitTests, Identity.UnitTests, Audit.UnitTests ve son `npx tsc -b` derlemeleri exit 0, 0 hata/uyarı. Unit/Vitest/SQL/host/browser/CI koşulmadı. Paket, sürüm veya lockfile değişmedi.
- **K3 review bekliyor:** Settings numara owner'ı yalnız kendisine ait yeni scoped context'i caller'ın açık SQL transaction'ına katıyor. Audit owner portu aynı transaction'ı kullanıyor; yeni context-construction yüzeyleri gerekçesi olmadan guard allowlist'ine eklenmedi. Merkezi pin/JSON üretimi kökte yapılacak.

## Dar dependency merge kaydı

- `af76b13e3848ac354ff82df50c91d96ca3260189` normal no-ff merge'i gerçek Sales satır sorumlusu owner reader'ını aldı. İki kaynak çatışmasında kök incelemesi ve açık talimatıyla `CompanyContextMiddleware.cs` ile `CompanyLifecycleScopeFenceTests.cs` mevcut HEAD içerikleri byte-exact korundu. Bunlar ordinary foreign-company/failure çitlerini ve daha yeni KIM-D56 group-operation/fresh-revision istisnasını taşır; eski opener otomatiği geri alınmadı. Diğer dependency değişiklikleri ve kirli Settings/Audit kaynakları korundu. Bu dar çözüm bağımsız K3 inceleme listesine dahildir; runtime test NOT_RUN.

## KIM-S7 — kurucu kurtarma System denetimi

- **SOURCE_READY; kabul koşumu NOT_RUN.** TK-44 / KIM-D22 kapsamındaki TTY kurucu kurtarması OS operatörü ve makine beyanını parola okunmadan önce ister; beyan onay metninde gösterilir. Kaynak, belge ve commit'te gerçek cihaz/operatör bilgisi yoktur. Parola, hash veya security stamp audit'e yazılmaz.
- `IFounderRecoverySystemAuditV1` yalnız kapalı typed kurucu reactivate/password-reset verisini kabul eder. Identity account write'ı ve gerçek Audit owner append'i aynı açık Serializable transaction'a katılır; Audit hatası her ikisini geri alır, başarı SecurityEvent'i üretilmez. Mevcut SecurityEvents red/başarı anlamı ve kurucunun grant/rol/2FA durumu değiştirilmez. Reactivate audit'i gerçek yeniden erişilebilir assignment kimliklerini kaydeder, yeni assignment oluşturmaz.
- PermissionCode null yalnız bu successful System event çiftinde ve doğrulanmış operatör beyanında mümkündür. Bütün normal User/AI/Integration/System draft'ları nonempty permission ister; GroupShared Product event'i null kabul etmez. Owner CHECK typed actor/event/operation/entity/source/outcome/role/AI/JSON/version sınırlarını korur. Eski permission-bearing satırların canonical hash'i değişmez. DDL sahibi olmayan diğer modül context'lerinin required mapping'i korunur.
- EF CLI 10.0.8 üretimi `20261116220434_AddTypedFounderRecoverySystemAudit`: nullable owner kolonu ve dar CHECK; Up'ta seed/geçmiş yeniden yazımı yok. Down ilk işlem THROW **51952** ile typed null-permission geçmişi kaybını reddeder. DB apply NOT_RUN.
- Unit kaynakları normal draft null reddi, typed beyan/operation doğruluğu ve migration/model'i; gerçek SQL kaynakları aynı Identity transaction'ında çok şirket commit/rollback, şirket filtresi, hash ve hatalı User/AI/operator satırı CHECK reddini kapsar. Identity kaynakları audit failure, eksik beyan ve gerçek assignment kapsamını doğrular. Organization normal User SQL assertion'ı nonnull permission şartını açıkça koruyacak biçimde hizalandı.
- Audit UI strict decoder yalnız bu kapalı typed null varyantını kabul eder ve Türkçe sistem işlemi metni gösterir; normal null-permission cevabı bozuk sayılır. Son Api.IntegrationTests, Identity.UnitTests, Audit.UnitTests ve TypeScript derlemeleri exit 0, 0 hata/uyarı; runtime Unit/Vitest/SQL/browser/CI NOT_RUN.
- **K3 review bekliyor:** çok şirketli tek kurtarma transaction'ına Audit owner fresh context'leri katılır; context construction yeni gerekçeli yüzeydir. Guard inventory'ye sessiz kayıt/izin genişletme yapılmadı. Gerçek SQL SecurityEvents snapshot fact'i ve atomiklik kabulü kapanışta koşulacaktır.

## KIM-S13 — ilk döviz para profili ve kredi planı kaynağı

- **SOURCE_READY; entegrasyon/kabul bekliyor.** URN-D2/D3/D4 ve kredi planında para biriminin Ayarlar'daki en küçük birimini kullanma kararı uygulanır. Bu takip ana planın paydasını veya biten sayısını artırmaz.
- TRY/USD/EUR için mevcut şirket operasyonlarıyla sınırlı boş yönetim hedefleri sunulur. `finance.credit-plan` yalnız önceden Finans para operasyonu olan şirkette hedef olur. Hedef kayıt değildir: ölçek, aktiflik, yürürlük tarihi ve gerekçe yetkili tarafından açıkça girilmeden değer, aday para birimi veya otomatik aktivasyon üretilmez.
- İlk kayıt expectedVersion `0` ile mevcut tek SaveChanges/audit/idempotency biriminde marker + sürüm 1 oluşturur. Aynı anahtar replay'i, çakışan anahtar/gövde ve iki ilk kayıt yarışı korunur. Eski onaylı kataloglar ilk değişimde mevcut aktarım yolunu sürdürür; geçmiş belge anlık görüntüleri değişmez.
- Operasyona özel V2 aday okuyucusu aynı gerçek kalıcı zaman çizgisini kullanır. Pasif/gelecek tarihli profil aday olmaz; eksik veya okunamayan depodan etkin profil tahmin edilmez. Eski operasyon belirtmeyen aday sözleşmesi genişletilmez. Aktör ve şirket await sonrasında yeniden doğrulanır.
- UI ilk değerleri boş gösterir; version `0` yalnız boş geçmişte kabul edilir. SQL kaynak fact'i sıfır otomatik kayıt, açık ilk kayıt, replay/çatışma, audit, tarih, şirket ayrımı ve pasifleştirme sonrası adayın kalkmasını kapsar. Mevcut decoder testleri korunmuştur.
- API, Settings.UnitTests, Api.IntegrationTests ve TypeScript derlemeleri exit 0, hata/uyarı 0. Runtime Unit/SQL/Vitest/browser/CI **NOT_RUN**. Yeni migration, seed, gerçek grant/rol veya Finance STOP değişikliği yoktur.
