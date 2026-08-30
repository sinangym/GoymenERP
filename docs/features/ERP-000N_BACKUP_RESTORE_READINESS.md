# ERP-000N — Backup/Restore Runbook Hazırlık ve Kodlama Kapısı

- Tarih: 2026-08-29
- İncelenen taban: main / 434b83ce49f02a2034c23c75690b791c38425874
- Görev: ERP-000N — Backup/restore runbook
- Bu belgenin kapsamı: yalnız readiness analizi ve recovery sözleşmesi hazırlığı
- Uygulama, script, CI, altyapı ve production davranışı: **NO-GO**
- Readiness/specification review: **GO**

## 1. Yönetici özeti

ERP-000N bugün backup komutu, restore scripti, provider yapılandırması veya production
felaket kurtarma uygulaması geliştirmeye hazır değildir. Repository; SQL Server
point-in-time restore, S3-compatible nesne sürümleri, şirket ayrıştırması, tesis dışı
şifreli yedek, geçici RPO/RTO ve gerçek restore tatbikatı yönünü açıkça ister. Buna
karşılık ortak recovery point, backup-set manifesti, DB ile nesne deposunun birlikte
uzlaştırılması, dış etkilerin restore sonrası tekrarını önleme, anahtar kurtarma ve
gerçek izole tatbikat sözleşmesi henüz yoktur.

İncelenen main snapshot'ında manifest ERP-000N'yi yalnız ERP-000A'ya bağlı ve ready
gösterir. Bu durum aşağıdaki zorunlu temelleri temsil etmez:

- CompanyId izolasyonu ve şirket veri paketi,
- yetkili recovery operatörü ve görev ayrılığı,
- append-only audit ile restore kanıtı,
- idempotency ve concurrency kayıtlarının geri dönüş davranışı,
- outbox/inbox high-water ve dış etki uzlaştırması,
- gerçek Belge Kasası ile S3-compatible object version sözleşmesi,
- backup/restore alarm ve gözlemlenebilirliği,
- SQL Server PITR ve migration uyumluluğu,
- release artifact/configuration/rollback manifesti.

Bu dal bu nedenle yalnız ERP-000N manifest satırını blocked yapar ve bu readiness
belgesini ekler. Yeni kanonik OPEN-* veya AT-* kimliği üretmez. Mevcut AT-070 Taslak
ve kanıt alanı boş olarak kalır.

## 2. Bağlayıcı kaynaklar ve kanıtlanan repo gerçeği

Kaynak önceliği kök AGENTS.md sırasına göre uygulanmıştır.

### 2.1 Kilitli kararlar

- DEC-GOV-003: bir hukuki şirket geçmişi, bakiyesi, stoku, belgesi ve auditiyle ayrı
  sunucu/veritabanına taşınabilir olmalıdır; şirket veri paketi export/restore testi
  zorunludur.
- DEC-GOV-005: big-bang cutover en az iki prova, final delta, rollback ve mutabakat
  gerektirir.
- DEC-GOV-006: bağımsız kabul açığı restore kanıtıyla kapatılır.
- DEC-TEC-003: SQL Server ancak restore dahil PoC kapılarını geçerse birincil teknik
  seçim olarak doğrulanabilir.
- DEC-TEC-004: on-prem/hibrid yönünde tesis dışı şifreli yedek ve felaket senaryosu
  zorunludur; kesin topoloji açık karardır.
- DEC-TEC-007: RPO 15 dakika ve RTO 4 saat yalnız geçici hedeftir; production öncesi
  işletme onayı ve restore tatbikatı gerekir.
- DEC-TEC-008: ortamlar ayrıdır; migration, rollback ve production yayın kanıtı
  sürümlü olmalıdır.
- DEC-TEC-009: backup, disk, kuyruk, entegrasyon, log, metric ve trace alarmları
  platformla birlikte kurulmalıdır.
- DEC-DOC-001, DEC-DOC-002 ve DEC-DOC-003: physical blob, logical document, version
  ve record link ayrıdır; fiziksel dedup erişim bağlamını birleştiremez; eski version
  sessizce ezilemez.
- DEC-DOC-008: ticari düzeltme ile retention/imha ayrı politikadır.
- DEC-TST-001 ve DEC-TST-002: restore, güvenlik, performans ve açık P0/P1 bulunmaması
  teknik kabulün parçasıdır.

### 2.2 Mevcut açık kararlar

- OPEN-014: sunucu, disk, RAID, ağ, UPS ve yedek envanteri bilinmiyor.
- OPEN-015: üç yıllık veri, belge/OCR hacmi, büyüme ve yoğun saat profili bilinmiyor.
- OPEN-017: kesin RPO/RTO ve felaket topolojisi açık.
- OPEN-018: belge türü bazlı retention, imha ve legal hold açık.

Bu açık kararlar geçici değerleri production gerçeğine dönüştürmez. Bu belge topology,
saklama süresi, backup sıklığı, bant genişliği, kopya sayısı, provider, ürün edition'ı
veya sayısal eşik seçmez.

### 2.3 ADR ve fiziksel dosya drift'i

ADR_REGISTER.md aşağıdaki kayıtları gösterir; ancak incelenen main üzerinde fiziksel
ADR dosyaları yoktur:

- ADR-003 — SQL Server birincil PoC,
- ADR-005 — nesne deposu ve DB metadata/hash,
- ADR-010 — ortam, CI/CD ve rollback manifesti.

Register satırı tek başına uygulanabilir backup/restore sözleşmesi değildir. Üç fiziksel
ADR onaylanmadan SQL backup modu, object versioning, recovery modeli, release
compatibility veya rollback davranışı üretilemez.

### 2.4 Mevcut kabul gerçeği

- AT-070: point-in-time DB restore ile nesne dosyası bütünlüğünün gerçek provada
  mutabık olması gerekir.
- AT-070 güncel ACCEPTANCE_MATRIX.csv içinde Taslak durumundadır ve kanıtı boştur.
- AT-001 şirket izolasyonunun restore/export sonrasında da korunması için yeniden
  çalıştırılmalıdır.
- AT-003 restore edilen her canonical audit event'inin bağımsız `IntegrityHash` yeniden
  hesabı ve append-only erişim/trigger koruması açısından regresyon kanıtıdır. Mevcut
  ERP-000D sözleşmesi olaylar arası hash chain tanımlamaz.
- AT-065 Belge Kasası kapsamına girdiğinde tek dosya ile çok kayıt bağlantısının
  restore sonrası bozulmadığını kanıtlamaya yardım eder; kendisi bugün Taslaktır.

Bu belge bu senaryoların durumunu veya kanıt alanını değiştirmez.

### 2.5 Mevcut teknik boşluk

- DEPLOY_BACKUP_RPO_RTO.md yalnız geçici hedef ve üst seviye production kapılarıdır;
  uygulanabilir runbook değildir.
- CUTOVER_RUNBOOK.md big-bang geçiş için üst seviye listedir; disaster recovery
  restore runbook'u değildir.
- SQL_SERVER_POC.md point-in-time restore ve checksum testini ister; yöntem ve kanıt
  formatını tanımlamaz.
- docker-compose yalnız SQL Server volume'u içerir; gerçek S3-compatible object store
  veya restore drill ortamı yoktur.
- Object storage ortam değişkenleri yalnız placeholder'dır; adapter veya provider
  seçimi kanıtı değildir.
- CI içinde gerçek backup/restore drill job'ı bulunmaz.
- Bu readiness belgesi eklenmeden önce ERP-000N feature spec'i yoktur; backup scripti,
  restore scripti ve recovery acceptance fixture'ı bugün de bulunmaz.
- PR #24 ile task-manifest structural validator'ı `main`e alınmıştır; bu araç bu dalda
  JSON/duplicate/dependency/DAG denetimi için çalıştırılır. Güncel workflow'lara bağlı
  zorunlu bir CI kapısı olduğu iddia edilmez.
- PR #19 merge sonrasındaki exact `main` için standart CI `33266146969` ve UI golden
  `33266146965` tamamen yeşildir. Bu baseline kanıtı ERP-000N branch head CI'ının,
  backup/restore drill'inin, artifact üretiminin veya production recovery kabulünün
  yerine geçmez.

## 3. Amaç, kapsam ve kapsam dışı

### 3.1 Hedef

ERP-000N'nin hedefi tek tek yedek komutları toplamak değil; belirli bir recovery
point için SQL Server verisi, nesne sürümleri, sürüm manifestleri, anahtar erişimi ve
dış etki durumunu güvenli biçimde yeniden çalışır hale getiren, tekrar edilebilir ve
kanıt üreten operasyon sözleşmesidir.

Hazırlık paketinin tanımlaması gereken temel sonuçlar:

1. Bir backup setinin hangi recovery point'i temsil ettiği.
2. SQL PITR zinciri ile S3-compatible object version envanterinin nasıl bağlandığı.
3. DB, object, audit, idempotency, outbox ve inbox kayıtlarının nasıl uzlaştırıldığı.
4. Restore sırasında dış connector ve dispatcher'ların neden kapalı kaldığı.
5. Şirket bazlı export/restore'un shared blob veya başka tenant bilgisi sızdırmadığı.
6. Şifreli backup'ın anahtar kaybı halinde anlamsız artifact'e dönüşmediğinin nasıl
   kanıtlandığı.
7. Schema, migration, config ve application artifact uyumunun nasıl doğrulandığı.
8. RPO/RTO'nun tahmin değil gerçek gözlenen tatbikat kanıtı olduğu.

### 3.2 Bu readiness çalışmasının dışında kalanlar

- SQL Server backup/restore komutu veya scripti yazmak.
- Object storage CLI, SDK, lifecycle policy veya bucket oluşturmak.
- CI workflow, Docker Compose, Kubernetes, VM, DNS, VPN veya network topolojisi
  değiştirmek.
- Backup provider, object provider, cloud, tape, appliance veya replication ürünü
  seçmek.
- Production backup sıklığı, retention süresi, kopya sayısı veya sayısal alarm eşiği
  belirlemek.
- Secret, gerçek anahtar, sertifika, parola, erişim anahtarı veya recovery credential
  üretmek ya da belgelemek.
- Migration, DB şeması, module contract, API veya worker eklemek.
- AT-070'i geçti, otomatik veya kanıtlı saymak.
- ERP-000F, ERP-000G, ERP-000L, ERP-000M veya ERP-000O davranışını bu task içinde
  uydurmak.

## 4. Bağımlılıklar ve sahiplik sınırı

ERP-000N implementation aşağıdaki platform görevleri main üzerinde kabul edilmeden
başlayamaz:

| Bağımlılık | Recovery açısından zorunluluk |
|---|---|
| ERP-000A | Repository, environment ve test omurgası |
| ERP-000B | CompanyId izolasyonu ve şirket veri paketi sınırı |
| ERP-000C | Yetki, ABAC, görev ayrılığı ve hassas operasyon kontrolü |
| ERP-000D | Append-only audit/correlation ve recovery kanıt zinciri |
| ERP-000E | Idempotency/concurrency kayıtlarının restore davranışı |
| ERP-000F | Outbox/inbox high-water, replay, poison ve dış etki uzlaştırması |
| ERP-000G | Logical document/version/link ile S3-compatible object sözleşmesi |
| ERP-000L, sonraki tüketici/entegrasyon | N, tekrarlanabilir drill ölçümü ve backup/restore sinyallerini üretir; L bunları production log/metric/trace/alert/health kanalına bağlar. N ile L arasında hard dependency döngüsü kurulmaz. |
| ERP-000M | SQL Server PITR, migration ve gerçek restore PoC'si |
| ERP-000O | Artifact, config, migration manifesti ve release/rollback kapısı |

Kanonik readiness/acceptance sırası `ERP-000M → ERP-000O → ERP-000N → ERP-000L`,
ardından bağımsız `ERP-000J/ERP-000K` şeklindedir. ERP-000O readiness belgesinin
`main`e merge edilmiş olması ERP-000O'yu tamamlamaz; görev güncel manifestte
`blocked` kalır ve ERP-000N implementation için kabul edilmiş bağımlılık sayılamaz.
Bu branch manifesti hedef sıranın yalnız `M → O → N` dependency prefix'ini kurar;
ERP-000L güncel `main`deki A-only satırıyla değişmeden kalır. `N → L` dependency'si
yalnız sonraki ERP-000L current-main rebuild/review'unda kurulacaktır. ERP-000L,
N'nin sinyallerini sonraki production telemetry/alert kanalına bağlar; N, L'ye ters
hard dependency ekleyerek döngü oluşturmaz.

Backup/restore bir domain modülünün tablosuna doğrudan yazan alternatif uygulama yolu
değildir. Recovery işlemi teknik katmanda çalışsa bile, sonuç açılmadan önce tüm
module schema/version ve business invariant doğrulamaları geçmelidir.

## 5. Recovery point ve backup-set manifesti

### 5.1 Ortak recovery point

SQL transaction commit zamanı ile object upload/finalize zamanı aynı atomik transaction
değildir. Bu nedenle yalnız aynı dakika damgasına sahip iki yedeği beraber almak
tutarlı restore kanıtı değildir.

Kabul edilmiş recovery sözleşmesi en az şu ayrımı yapmalıdır:

- DB içinde commit edilmiş ve object referansı kalıcılaşmış kayıt,
- object storage'a staged olarak yazılmış fakat DB tarafından sahiplenilmemiş bytes,
- DB metadata'sı var fakat object finalize tamamlanmamış kayıt,
- object mevcut fakat güvenlik taraması veya business lifecycle tamamlanmamış version,
- outbox'ta commit edilmiş fakat dışarı yayımlanmamış mesaj,
- dışarı yayımlanmış fakat inbox/ack durumu recovery point'te farklı görünen mesaj.

Bu durumlar bilinmeden DB backup zamanı ile object version zamanı birleştirilemez.

### 5.2 Kavramsal backup-set manifesti

Kesin dosya formatı ve storage provider kararı ayrı review ister. Manifest en az
aşağıdaki semantik alanları taşıyabilmelidir:

- değişmez BackupSetId ve manifest schema version,
- environment kimliği ve amaç; production verisini başka ortama sessizce açmaz,
- backup başlangıç/bitiş ve seçilen recovery point zamanları,
- SQL Server instance/database kimliği ile backup chain ve PITR sınırları,
- ilgili migration history, schema version ve database compatibility bilgisi,
- object namespace, versioning durumu, kapsanan object version/high-water bilgisi,
- logical document/version/link reconciliation özeti,
- audit, idempotency, outbox ve inbox high-water/checkpoint referansları,
- application artifact, deployment, config schema ve migration manifest referansı,
- her artifact için boyut, hash/checksum algoritma sürümü ve doğrulama sonucu,
- encryption/key reference; anahtarın kendisi manifestte bulunmaz,
- retention class, legal hold ve immutable-copy policy referansı,
- oluşturan sistem/operatör, onay ve görev ayrılığı kanıtı,
- source correlation, run kimliği ve gözlemlenebilirlik referansı,
- manifest issuer/run/environment/freshness bağlamı; ayrı failure-domain'deki trust
  anchor ile doğrulanan authenticated/tamper-evident immutable manifest-version referansı,
- audit ve outbox/inbox high-water checkpoint'lerinin aynı trust anchor'a bağlanan
  authenticated referansı; ham iş verisi değildir,
- başarı, kısmi, reddedilmiş veya doğrulanmamış sonuç.

Eksik, doğrulanamayan, forged, replay edilmiş, downgrade edilmiş veya freshness bağlamı
uyuşmayan manifest restore edilebilir varsayılmaz. Manifest algoritması/provider'ı bu
belgede seçilmez; artifact/checksum ile manifestin birlikte değiştirilmesi ayrı trust
root doğrulamasını geçemez. Manifestte
provider secret, ham credential, müşteri verisi veya gereksiz object key listesi
bulunmaz.

## 6. SQL Server PITR sözleşmesi

Fiziksel ADR-003 aşağıdaki konuları kilitlemeden implementation başlatılmaz:

- SQL Server version/edition ve hedef backup/PITR kabiliyeti,
- recovery model ve backup-chain sahipliği,
- full/differential/log veya eşdeğer zincirin kanonik sırası,
- backup chain bütünlüğü ve gap tespiti,
- transactionally consistent recovery point seçimi,
- encrypted backup ve key/certificate restore sırası,
- system database, login, job ve server-level configuration kapsamı,
- module migration history tablolarının birlikte korunması,
- restore sonrası DB integrity, constraint ve migration doğrulaması,
- standby/isolated restore ortamı ile production cutover ayrımı,
- bozuk veya eksik zincirde fail-closed davranış,
- rollback yerine forward-fix gereken schema durumları.

Bu belge belirli SQL komutu, edition özelliği veya backup frekansı seçmez. Gerçek ortam
envanteri OPEN-014 ile, kesin RPO/RTO OPEN-017 ile kapatılmalıdır.

## 7. S3-compatible object version recovery

Fiziksel ADR-005 ve kabul edilmiş ERP-000G sözleşmesi en az şunları belirlemelidir:

- object versioning ve immutable identity modeli,
- staged/quarantine/ready gibi güvenlik durumlarının kesin anlamı,
- logical document/version/link ile physical object version ilişkisi,
- checksum kaynağı ve yeniden doğrulama yöntemi,
- incomplete multipart/staged/orphan bytes davranışı,
- delete marker, lifecycle transition ve legal hold etkisi,
- shared physical blob varsa tenant-safe reference ownership,
- son reference silme ve fiziksel imha yetkisi,
- restore edilen object'in malware/security durumunun yeniden doğrulanması,
- backup set içindeki object inventory/high-water üretimi,
- object provider metadata'sının taşınabilir ve doğrulanabilir alt kümesi.

DB metadata'sı restore edildi diye object var kabul edilmez. Object bulundu diye logical
document erişimi veya CompanyId yetkisi oluşmaz. Fiziksel dedup başka şirketin belge,
hash, varlık veya kullanım bilgisini açıklayamaz.

## 8. DB–object–audit–idempotency–outbox/inbox uzlaştırması

### 8.1 Restore sonrası recovery mode

Restore edilen sistem normal kullanıcı trafiğine ve dış connector'lara hemen açılmaz.
Önce recovery mode içinde aşağıdaki kapılar tamamlanır:

1. Backup-set manifest ve tüm artifact checksum'ları doğrulanır.
2. SQL chain ve seçilen recovery point doğrulanır.
3. Object inventory/version ile logical metadata uzlaştırılır.
4. Eksik object, orphan object, staged object ve delete marker farkları sınıflandırılır.
5. Her kalıcı audit event'inin canonical alanlarından bağımsız `IntegrityHash` yeniden
   hesaplanır; append-only trigger/erişim koruması regresyonu çalıştırılır. Olaylar arası
   completeness/continuity, mevcut per-event hash'in kanıtlamadığı ayrı recovery kanıtıdır.
6. Idempotency sonuç kayıtları ile business kayıtları karşılaştırılır.
7. Outbox/inbox checkpoint, lease, retry ve poison durumları uzlaştırılır.
8. Schema, migration, config ve application artifact uyumu doğrulanır.
9. CompanyId, permission ve tenant isolation negatif testleri geçer.
10. Yetkili recovery kararıyla connector/dispatcher ve kullanıcı trafiği kontrollü
    açılır.

Bu sıralama production komutu değildir; bağlayıcı recovery contract'ın kanıtlaması
gereken fail-closed evrelerdir.

### 8.2 Dış etki ve replay riski

DB point-in-time olarak geçmişe dönerken e-posta, banka talimatı, e-belge, webhook veya
başka dış sistem etkisi geri alınmayabilir. Restore edilen outbox satırı yeniden
işlenirse aynı dış etki ikinci kez oluşabilir.

ERP-000F ve recovery sözleşmesi en az şunları kilitlemelidir:

- restore başlangıcında bütün dispatcher ve connector'ların kapalı olması,
- son güvenilir external dispatch/inbox high-water referansı,
- provider/external-system receipt veya idempotency referansının korunması,
- recovery point sonrası gerçekleşmiş dış etkilerin ayrı reconciliation listesi,
- yeniden yayınlanabilir, manuel karar isteyen ve asla otomatik replay edilmeyecek
  mesaj sınıfları,
- stale lease sahibi ve worker fencing davranışı,
- duplicate delivery olsa bile consumer tarafında ikinci iş etkisi oluşmaması,
- connector açma kararının actor, reason, approval ve audit kanıtı.

Restore runbook'u external effect hiç olmamış gibi davranamaz. Dış sistem durumunu
kanıtlamadan otomatik replay veya otomatik gönderim açılması yasaktır.

### 8.3 Audit ve idempotency

- Restore, append-only audit satırlarını sessizce yeniden yazmaz veya olaylar arası hash
  chain varmış gibi davranmaz.
- Her event'in canonical kalıcı alanlarından bağımsız `IntegrityHash` yeniden hesaplanır;
  append-only trigger/erişim koruması ayrıca doğrulanır.
- Recovery işleminin kendisi, primary DB kullanılamazken de ayrı failure-domain'de
  dayanabilen out-of-band operasyon/security kanıtı üretmelidir; backup artifact içine
  sonradan business audit eklenmez. DB açıldıktan sonra bu kanıt correlation ile bağlanır.
- Aynı idempotency key'in recovery point öncesi ve dış etki sonrası durumları
  uzlaştırılmadan critical write açılmaz.
- Restored terminal result, current authorization ve CompanyContext kontrolünü
  atlamaz.
- Recovery tooling ham idempotency key'i loglamaz veya export etmez.

## 9. Tenant-safe şirket export/restore

DEC-GOV-003 yalnız tam environment restore'u değil, bir şirketin geçmişiyle
ayrıştırılabilmesini de zorunlu kılar.

Şirket veri paketi sözleşmesi en az şunları kapsamalıdır:

- seçili CompanyId için owned ve referenced kayıtların eksiksiz dependency graph'i,
- ortak masterların yalnız kullanılan ve taşınmasına izin verilen alt kümesi,
- şirket kapsamlı audit, belge linki, sipariş, stok, finans ve metadata,
- shared physical blob varsa yetkili object copy/reference stratejisi,
- source identity, export batch, schema/version ve checksum manifesti,
- hedefte foreign key, unique key, number series ve permission eşlemesi,
- authentication secret/token/session/key ve aktif external-identity binding'lerinin
  pakete girmemesi; gerekli principal/role/scope metadata'sının inert ve default-deny
  ithal edilmesi,
- hedefte issuer/subject çözümleme, local reprovision, role/scope farkı, görev ayrılığı ve
  çift onay tamamlanmadan hiçbir imported principal/assignment'ın etkinleşmemesi,
- başka şirketin row, count, hash, object key, blob existence veya audit bilgisinin
  pakete sızmaması,
- hedefte restore sonrası CompanyId isolation saldırı testleri,
- cross-company iş ilişkilerinin açık, maskeli veya dış referans olarak taşınma kararı.

Tam DB backup'ı alıp başka şirket kayıtlarını uygulama filtresiyle gizlemek company
export değildir. Şirket paketi normal uygulama endpoint'inin IgnoreQueryFilters ile
genişletilmiş sürümü de değildir; ayrı, yetkili ve auditli system operation ister.

Kaynak ortamın subject/rol bilgisi hedefte kendiliğinden oturum veya erişim üretemez.
Shared blob ve cross-company reference kararı ERP-000G tarafından kabul edilmeden
şirket export/restore scripti yazılamaz.

## 10. Güvenlik, anahtar ve görev ayrılığı

### 10.1 Şifreleme ve anahtar kurtarma

- Backup aktarımda ve depoda şifrelenmelidir.
- Encryption key/certificate backup artifact ile aynı erişim alanında tutulamaz.
- Key escrow, rotation, revocation, expiry ve disaster access prosedürü ayrı onaylı
  politika olmalıdır.
- Eski backup setlerinin eski key sürümleriyle restore edilebildiği gerçek tatbikatta
  kanıtlanmalıdır.
- Key erişimi yoksa backup başarılı sayılmaz; checksum geçmesi decrypt edilebilirlik
  kanıtı değildir.
- Secret değeri manifest, log, ticket, source control veya runbook metnine yazılmaz.

### 10.2 Yetki ve dual control

- Backup okuma, restore başlatma, key açma, connector açma ve production cutover ayrı
  permission/operational role'lara ayrılabilmelidir.
- Tek kişi production backup'ını indirip izole olmayan ortama restore ederek bütün
  şirket verisine erişememelidir.
- Yüksek etkili restore ve production cutover ikinci onay/dual control gerektirir;
  kesin rol/eşik bu belge tarafından seçilmez.
- Break-glass erişimi süreli, gerekçeli, en az yetkili ve auditlidir.
- Restore operatörünün application içi admin olması otomatik business-data erişimi
  sağlamaz.

### 10.3 İzole restore ağı

- Restore drill production ağına ve dış connector'lara kapalı bir ortamda başlar.
- DNS, webhook, e-posta, banka, e-belge, object callback ve başka egress varsayılan
  olarak kapalıdır.
- Test ortamına taşınan production verisi onaylı güvenlik ve erişim kontrolüne tabidir;
  restore kopyası sıradan developer verisi değildir.
- Restore tamamlanmadan public endpoint, scheduled worker veya message dispatcher
  başlamaz.
- Ortam kimliği response, log ve operator ekranında açıkça görünür; production ile
  karıştırılmaz.

Kesin network topology, lokasyon ve provider OPEN-014/OPEN-017 kapanmadan seçilemez.

## 11. Retention, legal hold, immutability ve ransomware

OPEN-018 belge türü retention/imha/legal hold kararıdır ve aşağıdaki konuların yalnız belge
alt kümesine doğrudan girdi sağlar. DB satırları, audit/security evidence, backup-set
manifesti, key metadata'sı ve restore kopyaları için backup-wide retention/legal-hold/
immutable-copy/imha sözleşmesi ayrıca `ERP-000N-RECOVERY-CONTRACT` kapanışında kabul
edilmelidir:

- veri/belge türü bazlı backup retention,
- operational retention ile legal hold önceliği,
- delete/expiry yetkisi ve görev ayrılığı,
- immutable veya write-protected off-site kopya,
- ransomware/credential compromise halinde bağımsız recovery yolu,
- lifecycle değişikliğinin mevcut backup setlere etkisi,
- yasal imha ile eski backup'ta kalan kişisel/ticari verinin yönetimi,
- restore edildiğinde süresi dolmuş veya legal hold altındaki verinin davranışı,
- restore kopyalarının güvenli imhası ve kanıtı.

Backup tarihçeyi süresiz saklamak için sessiz bir istisna, retention ise backup
bütünlüğünü bozacak otomatik deletion değildir. İki politika aynı karar paketi ve
tatbikatla uzlaştırılmalıdır.

Ransomware kanıtı yalnız backup dosyasının varlığı değildir. Ayrı credential sınırı,
immutable copy, checksum, restore edilebilir key ve temiz/izole recovery tatbikatı
birlikte gerekir.

## 12. Schema, config ve artifact compatibility

Bir backup set yalnız bytes değil, çalıştırılabilir uyumluluk bağlamı taşımalıdır:

- application artifact/image identity ve hash,
- deployed source/commit ve release manifest referansı,
- module migration history ve schema version,
- configuration schema version; secret değerleri hariç,
- object metadata/schema version,
- event, outbox/inbox ve public contract versionları,
- feature flag ve worker/connector activation state,
- restore sırasında kullanılacak compatible binary veya forward-fix planı.

Güncel binary eski DB'yi otomatik ve geri dönüşsüz migrate ederek restore doğrulamasını
bozamaz. Önce compatible validation, sonra onaylı migration/forward-fix yapılmalıdır.
Production'da veri oluştuktan sonra audit/idempotency/outbox tablolarını düşüren Down
yolu recovery stratejisi sayılamaz.

Artifact veya migration manifesti eksikse restore tamamlanmış kabul edilmez. ERP-000O
bu kanıtların build/release zincirinde üretilebilir olmasını sağlamalıdır.

## 13. RPO/RTO ve kapasite kanıtı

### 13.1 Geçici hedefler production SLO değildir

RPO 15 dakika ve RTO 4 saat bugün yalnız geçici hedeflerdir. Kesin değerler:

- iş etki analizi,
- OPEN-014 altyapı envanteri,
- OPEN-015 gerçek hacim/büyüme/yoğun saat profili,
- OPEN-017 felaket topolojisi,
- SQL log ve object version davranışı,
- network/bant genişliği ve key erişim süresi,
- reconciliation ve business validation süresi

ile işletme tarafından onaylanmalıdır.

### 13.2 Ölçüm başlangıç ve bitişi

Her tatbikat raporu RPO/RTO ölçümünün başlangıç/bitiş olaylarını açıkça tanımlar.
Örneğin yalnız DB restore komutunun bitmesi RTO değildir. RTO en az:

- olayın tespiti,
- recovery kararı ve yetkili erişim,
- backup/key bulma,
- DB ve object restore,
- reconciliation,
- schema/config/artifact validation,
- security ve tenant acceptance,
- connector/user traffic açma kararı

süresini kapsamalıdır.

Gözlenen veri kaybı recovery point ile son doğrulanmış business event arasından
ölçülür. SQL ve object ayrı RPO iddiası taşıyorsa ortak business recovery point ayrıca
raporlanmalıdır.

Tek başarılı prova yeterli değildir. Farklı hata sınıfları, veri hacimleri ve key
rotation durumları planlı periyodik tatbikata alınmalıdır; kesin sıklık karar sahibi
tarafından belirlenir.

## 14. Gözlemlenebilirlik, alarm ve runbook

ERP-000N, L tamamlanmadan da gerçek drill süresi/sonucu ve aşağıdaki sinyalleri
tekrarlanabilir test harness'iyle üretmelidir. Bu, production telemetry/alert kabulü
değildir; ERP-000L daha sonra aynı sinyalleri işletim kanalına bağlar:

- son başarılı backup-set yaşı ve sonucu,
- SQL backup chain gap veya log backup failure,
- object version/inventory/checksum failure,
- manifest eksikliği veya unknown schema version,
- backup boyutu, süre ve beklenmeyen büyüme,
- encryption/key erişim veya expiry riski,
- off-site/immutable copy başarısı,
- restore drill son sonucu ve gözlenen RPO/RTO,
- reconciliation fark sayıları,
- orphan/missing object ve unresolved external-effect sayıları,
- dispatcher/connector recovery lock durumu,
- kapasite, disk ve backup target erişilebilirliği.

Metric label'larında müşteri adı, object key, belge no, secret veya yüksek cardinality
iş verisi bulunmaz. Log ve trace correlation taşır; backup content'i loglamaz.

Runbook en az şu evreleri kapsamalıdır:

1. Olayı sınıflandır ve recovery owner'ı ata.
2. Connector/dispatcher ve gerekli write trafiğini fail-closed kapat.
3. Hedef recovery point ve backup setini dual control ile seç.
4. Artifact, checksum, chain ve key kullanılabilirliğini doğrula.
5. İzole ortamı hazırla; dış egress'i kapalı tut.
6. SQL ve object recovery işlemlerini onaylı provider-specific eklerden yürüt.
7. Ortak reconciliation ve compatibility kontrollerini çalıştır.
8. Security, CompanyId, audit, idempotency ve event acceptance'larını çalıştır.
9. İş sahibiyle miktar/tutar/belge/checksum örneklerini doğrula.
10. RPO/RTO ve açık farkları kaydet.
11. Production cutover veya geri çekilme kararını yetkili onayla ver.
12. Connector'ları sınıflı uzlaştırma sonrası kontrollü aç.
13. Tatbikat/olay kanıtını ve takip aksiyonlarını kapat.

Provider-specific komutlar ancak fiziksel ADR'ler ve gerçek altyapı seçimi onaylandıktan
sonra ayrı, erişimi kontrollü eklerde yer alabilir.

## 15. Gerçek izole kabul ve restore drill planı

Aşağıdaki maddeler yeni kanonik AT-* kimliği değildir. AT-070'i ayrıntılandıracak
task-local aday kanıt alanlarıdır.

### 15.1 Backup-set ve bütünlük

- Gerçek SQL Server ve gerçek S3-compatible object store üzerinde backup set üretilir.
- Manifest, SQL chain, object inventory/version ve artifact/config referanslarını
  birbirine bağlar.
- Tek byte bozulma, eksik log segmenti, eksik object version ve yanlış key sürümü
  ayrı fail-closed hata üretir.
- Forged, alanı eksiltilmiş, artifact/checksum ile birlikte değiştirilmiş, eski/replayed
  veya downgrade manifest; ayrı trust root, issuer/run/environment/freshness ve immutable
  version doğrulamasında fail-closed reddedilir.
- Checksum doğrulaması backup sırasında ve restore sonrasında tekrarlanır.

### 15.2 Point-in-time ve DB–object tutarlılığı

- Seçili recovery point öncesi commit edilmiş kayıtlar ve objects vardır.
- Recovery point sonrası kayıtlar başarıyla geri dönmüş gibi görünmez.
- DB metadata var/object yok; object var/DB link yok; staged/orphan/delete marker
  senaryoları sınıflandırılır ve sessiz başarı olmaz.
- Logical document/version/link toplamları ve hashes mutabık kalır.

### 15.3 Audit, idempotency ve event recovery

- Her audit event'inin canonical kalıcı alanlarından bağımsız `IntegrityHash` yeniden
  hesaplanır ve append-only trigger/erişim koruması doğrulanır; olaylar arası hash chain
  varmış gibi davranılmaz.
- Aynı idempotency key/payload restore sonrası ikinci business event üretmez.
- Aynı key farklı payload kesin reddedilir.
- Outbox/inbox high-water ve duplicate-delivery senaryoları ikinci dış etki üretmez.
- Dışarı daha önce ulaşmış fakat DB point-in-time'da görünmeyen etki reconciliation
  kuyruğuna düşer; otomatik replay edilmez.
- Stale worker/lease connector lock'ı aşamaz.

### 15.4 Tenant ve şirket paketi

- A şirketi full restore ve company export yollarıyla B şirketi row, count, hash,
  object key, blob existence, audit veya manifest bilgisini öğrenemez.
- Shared object senaryosunda A paketi B'nin logical linkini veya business metadata'sını
  taşımaz.
- Export edilen A şirketi hedefte constraint, CompanyId, permission ve document link
  testlerini geçer.
- Tam environment restore sonrasında mevcut CompanyId ve RBAC/ABAC regresyonları
  yeniden çalıştırılır.
- Source external identity binding, token/session/secret/key veya aktif role assignment
  hedefte otomatik etkinleşmez; imported principal default-deny kalır ve yalnız hedef-local
  reprovision + issuer/subject çözümleme + fark/görev ayrılığı + çift onayla etkinleşir.

### 15.5 Güvenlik ve anahtar

- Eski key sürümüne bağlı onaylı backup gerçek escrow prosedürüyle açılır.
- Yanlış/eksik key, secret veya certificate açık ve veri sızıntısız fail eder.
- Restore ortamında e-posta, webhook, banka/e-belge ve başka dış egress kapalıdır.
- Yetkisiz operator backup content, key veya restore sonucu okuyamaz.
- Dual-control ve break-glass olayları audit/correlation kanıtı üretir.

### 15.6 Compatibility ve geri dönüş

- Backup setiyle eşleşen application artifact ve migration manifesti bulunur.
- Yanlış binary/schema/config birleşimi trafiğe açılmaz.
- Onaylı forward-fix migration varsa önce isolated ortamda test edilir.
- Restore drill başarısızsa temiz tekrar ve artifact imha prosedürü kanıtlanır.

### 15.7 RPO/RTO ve operasyon

- Olay tespitinden güvenli trafik açılışına kadar gerçek elapsed time ölçülür.
- Ortak business recovery point ile gerçek veri kaybı ölçülür.
- OPEN-017 kapandıktan sonra onaylı RPO/RTO eşikleriyle karşılaştırılır.
- Task-local drill alarm/probe'ları tatbikatta gerçekten tetiklenir ve sahipli aksiyona
  dönüşür; production alert entegrasyonu ERP-000L kabulünde ayrıca kanıtlanır.
- Tatbikat raporu tarih, ortam, backup set, actor/approver, hata/fark ve kanıt
  referanslarını taşır.

Mock, yalnız dosya kopyalama, yalnız SQL restore veya yalnız object checksum AT-070'in
yerine geçmez. Gerçek SQL Server ve gerçek S3-compatible object version davranışı
birlikte test edilmelidir.

## 16. Implementation öncesi karar ve kabul kapısı

ERP-000N implementation ancak aşağıdakilerin tamamı ayrı insan review'uyla kabul
edildiğinde GO olabilir:

1. Fiziksel ADR-003, ADR-005 ve ADR-010 oluşturulmuş ve register durumlarıyla
   tutarlı olmalıdır.
2. OPEN-014 altyapı/yedek envanteri sahipli ve doğrulanmış olmalıdır.
3. OPEN-015 veri/belge hacmi ve büyüme profili kapasite ve drill girdisine
   dönüştürülmelidir.
4. OPEN-017 kesin RPO/RTO ile felaket topolojisini kapatmalıdır.
5. OPEN-018 belge alt kümesi için retention/imha/legal hold kararını kapatmalıdır. Diğer
   recovery data class'larının retention, legal hold, immutable copy ve restore-copy
   destruction sözleşmesi ayrıca `ERP-000N-RECOVERY-CONTRACT` içinde kabul edilmelidir.
6. ERP-000F outbox/inbox replay ve external-effect recovery sınırı kabul edilmelidir.
7. ERP-000G DB–object lifecycle, shared blob ve tenant-safe export sınırı kabul
   edilmelidir.
8. ERP-000N'nin ölçüm/sinyal sözleşmesi ve task-local drill probe'ları kabul edilmelidir;
   ERP-000L bunların sonraki production telemetry/alert tüketicisidir ve N için hard
   dependency değildir.
9. ERP-000M gerçek SQL Server PITR ve migration compatibility kanıtını sağlamalıdır.
10. ERP-000O artifact/config/migration/rollback manifest sözleşmesini sağlamalıdır.
11. Common recovery point, authenticated/tamper-evident backup-set manifest, ayrı trust
    root/anti-rollback, connector lock, high-water ve reconciliation sözleşmesi review
    edilmelidir.
12. AT-070 ve task-local aday senaryolar gerçek izole drill planına eşlenmelidir.
13. Permission, inert imported identity/role activation, dual control, key escrow,
    restore data access ve out-of-band recovery evidence planı güvenlik review'undan
    geçmelidir.

Bu kapılar kapanmadan:

- backup/restore scripti yazılmaz,
- provider/ürün/paket seçilmez,
- CI veya infra değiştirilmez,
- gerçek secret/key eklenmez,
- production backup veya restore otomasyonu açılmaz,
- görev ready, done veya accepted yapılmaz.

## 17. ERP-000N-RECOVERY-CONTRACT etiketi

ERP-000N-RECOVERY-CONTRACT yeni kanonik decision, OPEN veya acceptance kimliği değildir.
Manifest tabanlı orkestrasyonun; ortak recovery point, backup-set manifesti, DB–object
uzlaştırması, audit/idempotency/outbox high-water, connector lock, tenant-safe company
restore, key/security, compatibility, observability ve gerçek drill sözleşmeleri
kapanmadan ERP-000N'yi implementation için seçmesini engelleyen task-local aggregate
blocker etiketidir.

Bu aggregate blocker; yalnız OPEN-018 belge kararına indirgenemeyen backup-wide
retention/legal-hold/immutable-copy/restore-copy destruction; manifest authenticity ve
anti-rollback trust root; inert identity import/reprovision; out-of-band recovery kanıtı
kapanmadan çözüldü sayılamaz.

Merkezî karar sicili bu konuları mevcut kararlara bağladığında veya yeni kanonik
kimlikleri insan review'uyla atadığında manifestte onların yerini almalıdır. Bu belge
kendiliğinden OPEN-014/015/017/018'i kapatmaz.

## 18. Paralel Draft PR ve shared manifest kapısı

Bu dal `docs/backlog/task_manifest.json` dosyasını değiştirir. İncelenen güncel `main`
üzerinde ERP-000E `done`, ERP-000M `blocked` ve ERP-000O `blocked` gerçeği korunur.
PR #19 ile ERP-000O readiness'i `main`e alınmıştır; readiness merge'i ERP-000O
implementation veya acceptance kapısını açmamıştır.

Bu çalışma:

- yalnız exact ERP-000N task objesini değiştirir; manifest kök metadata'sı ve diğer on
  dört task objesi güncel `main` ile byte-for-byte eşit bırakılır,
- ERP-000N'yi exact `A/B/C/D/E/F/G/M/O` bağımlılıkları ve
  `ADR-003/005/010`, `OPEN-014/015/017/018`,
  `ERP-000N-RECOVERY-CONTRACT` blocker'larıyla `blocked` yapar,
- PR #24 validator'ını yerel structural/DAG kanıtı olarak çalıştırır; workflow'a bağlı
  required gate veya branch-protection kanıtı üretmiş saymaz,
- current branch DAG'ında 31 edge ve exact `M → O → N` dependency prefix'ini
  doğrular; ERP-000L satırını A-only current-main gerçeğiyle değiştirmez ve ters
  M/O/N dependency'si eklemez,
- hedef kabul sırasını `ERP-000M → ERP-000O → ERP-000N → ERP-000L`, ardından bağımsız
  `ERP-000J/ERP-000K` olarak korur; `N → L` edge'ini sonraki ERP-000L PR'ına bırakır,
- yalnız ERP-000N satırındaki kasıtlı delta yeniden üretilir,
- conflict veya semantik farkı otomatik ya da sessiz çözmez,
- task manifest kontrol yüzeyi ve backup/restore kapsamı nedeniyle Draft ve insan
  review'unda kalır.

Bu belge eski CI kanıtını yeni head'e taşımaz. Exact head için JSON/DAG, diff, tam yerel
testler, standart CI ve UI golden kanıtı yeniden üretilir; PR Draft ve insan review
kapısında bırakılır.

## 19. Definition of Ready ve Definition of Done

### 19.1 Kodlama için Ready

ERP-000N ancak:

- bütün manifest bağımlılıkları main üzerinde accepted/done,
- fiziksel ADR-003/005/010 kabul edilmiş,
- OPEN-014/015/017/018 kapanmış,
- ERP-000N-RECOVERY-CONTRACT merkezî kararlarla çözülmüş,
- gerçek izole SQL Server + S3-compatible drill tasarımı yürütülebilir,
- güvenlik, key, dual-control ve connector-lock planı kabul edilmiş,
- AT-070 kanıt planı review edilmiş

ise implementation için ready olabilir.

Bugünkü durum: karşılanmıyor; implementation NO-GO.

### 19.2 Tamamlandı sayılmak için Done

- Onaylı backup/restore automation ve provider-specific runbook ayrı kritik PR'da
  uygulanmıştır.
- Gerçek backup seti, manifesti, checksums ve keys ile izole ortamda restore edilmiştir.
- DB, object, audit, idempotency, outbox/inbox ve external-effect reconciliation
  geçmiştir.
- CompanyId/full restore ve tenant-safe company export/restore testleri geçmiştir.
- Schema/config/artifact compatibility ve rollback/forward-fix kanıtı vardır.
- Task-local drill alarm/probe kanıtı ve observed RPO/RTO raporu gerçek tatbikattan
  üretilmiştir; production telemetry/alert entegrasyonu ERP-000L kabulünde kalır.
- AT-070 merkezî acceptance sürecinde kanıtla güncellenmiştir.
- P0/P1 açık kusur yoktur.
- Production activation insan güvenlik/mimari review'undan geçmiştir.

## 20. Sonuç

ERP-000N hedefi repo kararlarıyla uyumludur; fakat mevcut tek satırlık ready tanımı
uygulanabilir recovery güvencesi sağlamaz. SQL ve object bytes'ın bulunması, şirket
izolasyonu, dış etki güvenliği, key kullanılabilirliği ve schema compatibility
kanıtlanmadan çalışan ERP geri yüklenmiş sayılmaz.

Sonuç:

- **Readiness/specification: GO**
- **Script, code, CI, infra, provider ve production davranışı: NO-GO**
- **AT-070: Taslak; kanıt alanı boş ve değiştirilmedi**
- **Yeni kanonik OPEN/AT: oluşturulmadı**
- **ERP-000N durumu: blocked**
