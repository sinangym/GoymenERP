# ERP-000L — Gözlemlenebilirlik Hazırlık ve Kodlama Kapısı

- İlk hazırlık tarihi: 2026-08-28
- Current-main yeniden doğrulama tarihi: 2026-08-29
- İncelenen taban: `main` / `9d884b789bc20442686567e50de243feb6496d42`
- Görev: `ERP-000L — Gözlemlenebilirlik`
- Kapsam: yalnız readiness analizi, güven sınırı ve kabul planı
- Uygulama durumu: **NO-GO / BLOCKED**
- Readiness belge durumu: **GO**
- Production activation: **NO-GO**

## 1. Yönetici özeti

`DEC-TEC-009`, merkezî log, metrik, trace, korelasyon, yavaş sorgu, kuyruk,
entegrasyon, backup ve güvenlik alarmlarının platformla birlikte kurulmasını bağlayıcı
olarak ister. Bu yön nettir; ancak hangi telemetry sözleşmesinin, sağlık semantiğinin,
saklama politikasının ve işletim modelinin güvenli kabul edileceği henüz fiziksel bir
karar paketinde kilitli değildir.

Kodlamayı durduran dört kapı vardır:

1. `OPEN-014`: gerçek sunucu, işletim sistemi, ağ, disk, yedek ve kapasite envanteri
   yoktur; collector/sink topolojisi veya kaynak bütçesi seçilemez.
2. `OPEN-015`: üç yıllık hacim, eşzamanlı kullanıcı ve yoğun saat bilgisi yoktur;
   cardinality bütçesi, sampling, SLO ve alarm eşiği kesinleştirilemez.
3. `OPEN-017`: kesin RPO/RTO ve felaket topolojisi açık karardır. Geçici 15 dakika/
   4 saat değerleri production SLO'su veya alarm eşiği olarak kilitlenemez.
4. Telemetry şeması, iş audit'i ve kalıcı güvenlik olayı ayrımı, operator erişimi,
   redaction, probe semantiği, sampling, SLO/alert sahipliği ve runbook kapanışlarının
   task-local toplu adı `ERP-000L-DECISION-PACK`tır. Bu ad kanonik `OPEN-*`, `ADR-*`
   veya `AT-*` kimliği değildir.

Bu readiness işi herhangi bir paket, provider, collector, sink, exporter, dashboard,
threshold, secret, config veya CI davranışı seçmez ve eklemez. Yalnız bu belge,
manifestteki ERP-000L satırı ve append-only otonom ilerleme kaydı değişir; yeni kanonik
karar/kabul kimliği üretilmez.

## 2. Bağlayıcı repo gerçeği

| Kayıt | ERP-000L için bağlayıcı sonuç |
|---|---|
| `DEC-GOV-002` | Kritik veri şirket sınırını korur; telemetry tenant sızıntısı veya grup görünümü yetkisi üretemez. |
| `DEC-TEC-003` | SQL Server birincil PoC adayıdır; performans, audit ve restore kanıtı olmadan nihai seçim değildir. |
| `DEC-TEC-004` | On-prem/hibrid, VPN ve tesis dışı şifreli yedek yönü kilitlidir; kesin topoloji açık kalır. |
| `DEC-TEC-007` | 15 dakika RPO / 4 saat RTO yalnız geçici hedeftir; işletme onayı ve restore provası gerekir. |
| `DEC-TEC-009` | Log, metric, trace, korelasyon, yavaş sorgu, kuyruk, entegrasyon, backup ve güvenlik alarmı platformla birlikte kurulur. |
| `ADR-009` | Business audit, teknik log değildir; başarılı/no-change kritik iş etkisiyle aynı transaction'da append-only kanıttır. |
| `OPEN-014` | Donanım, OS, ağ, UPS ve yedek envanteri dağıtım PoC'sinden önce kapanmalıdır. |
| `OPEN-015` | Hacim ve yoğunluk verisi performans SLO'su kilitlenmeden önce kapanmalıdır. |
| `OPEN-017` | Kesin RPO/RTO ve felaket topolojisi production açılmadan kapanmalıdır. |
| `AT-070` | Gerçek nokta-zaman DB restore'u ile nesne bütünlüğü mutabakatı ister; mevcut satır `Taslak`, kanıt alanı boştur. |
| `SY-07` | “Audit ve Sistem Sağlığı” kanonik ekrandır; UI kataloğunda `DECISION_ONLY_REFERENCE_MISSING` durumundadır. |

Bu belge `ACCEPTANCE_MATRIX.csv` dosyasını değiştirmez. Mevcut taslak kabul satırları,
özellikle `AT-070`, `Taslak` ve kanıtı boş kalır. ERP-000D kapsamında otomatik kanıtı
bulunan `AT-003` de yeniden sınıflandırılmaz; audit kanıtı telemetry kabulünün yerine
geçmez.

## 3. Mevcut kod sınırı ve kanıt açığı

İncelenen `main@9d884b7` snapshot'ında:

- `CorrelationContextMiddleware`, correlation yoksa 32 karakterlik server GUID'i üretir.
  İstemciden correlation/causation gelirse her header exact tek raw değer, 1-128 karakter
  ve yalnız güvenli ASCII harf/rakam ile `-_.:` karakterlerinden oluşmalıdır. Boş,
  whitespace, padded, çoklu, comma-joined, Unicode, kontrol karakterli veya uzun değer
  downstream endpoint, audit ve idempotency etkisinden önce `400` ile reddedilir.
- Middleware doğrulanan correlation/causation ve server-side `Web` source channel'ını
  request scope'a kurar, correlation'ı response header'da döndürür. Correlation bir
  authorization veya CompanyContext kaynağı değildir.
- `ILogger` üzerinden tam üç business structured-log çağrısı vardır. Bunların biri
  `OrganizationService` location command'ında raw `CompanyId`, `LocationId` ve `ActorId`;
  ikisi `CounterpartyCommandService` create ve account-active-state command'larında raw
  `CompanyId`, `PartyId` ve `ActorId` alanlarını loglar. Bu mevcut source-level PII/tenant
  redaction boşluğudur; production erişim veya retention politikasının kabul edildiği
  anlamına gelmez ve bu docs-only dalda kod değiştirilmez.
- `Program.cs`, boş genel `AddHealthChecks()` kaydıyla yalnız anonymous `/health/live`
  endpoint'ini açar. Kayıtlı dependency check, ayrı startup/readiness endpoint'i,
  process-specific zorunlu dependency veya capability-degradation kanıtı yoktur.
- OpenTelemetry/Serilog/Prometheus benzeri telemetry paketi, `Meter`/`ActivitySource`,
  merkezî metric/trace pipeline'ı, collector/exporter, telemetry sink'i, sampling
  politikası, SLO/alert sahipliği veya dashboard kanıtı yoktur.
- ERP-000D append-only, Company-scoped ve tenant-filtered business audit'i Organization
  ile Counterparty kritik command akışlarında vardır. Başarılı/no-change iş etkisi audit
  ile aynı transaction'dadır; audit teknik log veya kalıcı genel security-event değildir.
- Reddedilen/yetkisiz denemeler için kabul edilmiş genel kalıcı güvenlik olayı sözleşmesi
  yoktur. Issue `#63` ve `IDENTITY_GLOBAL_SECURITY_EVIDENCE_v1.md` yalnız global
  Identity/control-plane değişiklikleri için **DRAFT / NO-GO** bir hazırlık sınırıdır;
  genel security-event channel'ı veya tamamlanmış L bağımlılığı sayılmaz.
- ERP-000F kuyruk/outbox implementation'ı, ERP-000M SQL PoC implementation'ı,
  ERP-000O delivery implementation'ı ve ERP-000N gerçek backup/restore provası `main`de
  kabul edilmiş değildir. M/O/N readiness belgelerinin merge edilmiş olması runtime veya
  production kabulü değildir.

Bu nedenle mevcut log ve health satırları ERP-000L'yi implementation-ready veya
tamamlanmış yapmaz.

## 4. Amaç ve kapsam dışı

ERP-000L'nin ilerideki dar amacı:

1. Uygulama, worker ve zorunlu dependency'ler için provider-agnostic telemetry zarfını
   tanımlamak.
2. Server tarafından üretilen correlation/causation ile log, metric ve trace'i birbiriyle
   ilişkilendirmek.
3. Business audit, kalıcı güvenlik olayı ve kayıp yaşayabilen tanısal telemetry'yi
   kesin biçimde ayırmak.
4. Tenant/veri sınıfı güvenli redaction, KVKK minimizasyonu ve operator RBAC/ABAC
   sınırını kurmak.
5. Liveness, readiness ve startup probe'larını ayrı ve test edilebilir semantiklerle
   tanımlamak.
6. SLI/SLO, sampling, alert owner, escalation ve runbook kapılarını kanıta bağlamak.
7. Gerçek API, SQL Server, kuyruk ve backup/restore yolunda arızanın tanılanabildiğini
   ve alarm kapanışının doğrulanabildiğini kanıtlamak.

Bu readiness çalışmasının dışında kalanlar:

- logging/metrics/tracing paketi veya protokolü seçmek;
- SaaS, on-prem ürün, collector, agent, sink, exporter ya da storage seçmek;
- endpoint, connection string, API key, certificate veya secret eklemek;
- `appsettings*`, environment, container, reverse-proxy veya network config'i değiştirmek;
- dashboard veya `SY-07` UI tasarlamak/uygulamak;
- SLO, sampling oranı, retention süresi veya alarm threshold'u uydurmak;
- health endpoint'i, middleware, instrumentation, DB şeması veya background worker kodlamak;
- CI workflow veya deployment davranışı değiştirmek;
- business audit şemasını telemetry tablosuna dönüştürmek;
- reddedilen güvenlik denemesini yalnız log/trace ile geçici kılmak.

## 5. Bağımlılıklar ve sahiplik

| Bağımlılık | Gerekçe | ERP-000L sınırı |
|---|---|---|
| ERP-000A | API, test ve solution omurgası | Ortak yapı ve sürüm bilgisini tüketir. |
| ERP-000B | CompanyContext ve tenant izolasyonu | CompanyId'yi istemci/log metninden türetmez; tenant görünürlüğü backend'de korunur. |
| ERP-000C | Kimlik, RBAC ve ABAC | Operator/detay erişimi default-deny yetki kararı tüketir; yeni rol adı uydurmaz. |
| ERP-000D | Business audit ve correlation | Append-only audit'i değiştirmez; correlation üzerinden ilişki kurar. |
| ERP-000E | İdempotency/concurrency sonucu | Replay/conflict/no-change semantiğini doğru outcome olarak gözlemler; ikinci iş doğrusu kurmaz. |
| ERP-000F | Outbox/inbox ve dispatcher | Gerçek kuyruk yaşı, retry, poison/dead-letter ve lease sinyallerini sahip sözleşmeden alır. |
| ERP-000M | SQL Server PoC/migration | Gerçek bağlantı, pool, query, deadlock/timeout ve hacim kanıtını sağlar. |
| ERP-000N | Backup/restore runbook | Backup yaşı, başarısızlık ve gerçek restore mutabakatı kanıtını sağlar. |

Manifest bağımlılıkları A/B/C/D/E/F/M/N olarak fail-closed kaydedilir. Bunların tamamı
kabul edilip `main`e alınmadan ERP-000L implementation başlatılmaz. Bir dependency'nin
readiness/spec PR'ı veya Draft PR'ı, kabul edilmiş runtime contract sayılmaz.

ERP-000O ve ERP-000G, ERP-000N'nin kabul edilmiş doğrudan bağımlılıklarıdır; dolayısıyla
L'ye ayrıca tekrarlı edge eklenmeden transitif olarak kapanmak zorundadır. Current-main
readiness yönü `M → O → N → L`, ardından bağımsız `J/K`dır. Bu sıra readiness
belgelerinin merge sırasıdır; M/O/N/L implementation'larının kabul edildiği anlamına
gelmez.

## 6. Üç ayrı kanıt kanalı

### 6.1 Business audit

Business audit, `ADR-009` sahibidir. Başarılı veya business açısından no-change kritik
ERP etkisini actor, CompanyId, permission, before/after, neden ve correlation ile aynı
transaction'da saklar. Sampling uygulanmaz; teknik sink kesintisi yüzünden audit
başarısı uydurulmaz. Telemetry, audit satırının kopyası veya system-of-record'u değildir.

### 6.2 Kalıcı güvenlik olayı

Yetkisiz erişim, tenant aşma, şüpheli kimlik/policy denemesi, kötüye kullanım ve güvenlik
kontrolü ihlali; kabul edilmiş, dayanıklı ve erişimi kısıtlı bir security-event kanalına
gidebilmelidir. Bu kanal:

- normal başarısız business audit olayı gibi gösterilmez;
- trace sampling veya geçici log buffer'ına bağlı kalmaz;
- tenant, aktör, policy sonucu, güvenli correlation, event schema version ve sınıflandırma
  kanıtını taşıyabilir;
- ham credential, token, request body veya hassas business payload saklamaz;
- append/immutability, retention, legal hold, erişim, dış alarm ve incident bağlantısı
  kararlarını açıkça taşır.

Kalıcı security-event deposunun sahibi, exact şeması, transaction/failure semantiği,
retention ve sink'i mevcut kaynaklarda kilitli değildir. Bunlar
`ERP-000L-DECISION-PACK` kapanmadan kodlanmaz.

### 6.3 Tanısal telemetry

Log, metric ve trace; sistem sağlığı, gecikme, hata, kapasite ve dependency davranışını
tanılamak içindir. Bounded buffer, sampling veya pipeline arızası nedeniyle kontrollü
kayıp yaşayabilir. Bu nedenle business gerçeği, onay, para/stok hareketi, audit veya
kalıcı security-event yerine geçmez. Üç kanal ortak correlation ile bağlanır; payload
kopyalayarak birleştirilmez.

## 7. Ortak telemetry zarfı ve korelasyon

Kabul edilecek ortak zarf en az şu düşük-cardinality veya kontrollü alan sınıflarını
tanımlamalıdır:

- UTC zaman, environment, service/module/component ve build/deployment version;
- sürümlü event/template adı; serbest mesaj event identity değildir;
- route template veya operation class; gerçek URL/path/query string değildir;
- outcome/error-code/status-family; exception message label değildir;
- server doğrulamalı correlation, causation, trace ve span kimlikleri;
- dependency kind ve operation class; host, connection string veya object key değildir;
- güvenli duration/size sınıfı ve gerektiğinde restricted numeric measurement;
- veri sınıfı/redaction policy version ve sampling kararı.

Correlation yalnız ilişkilendirme anahtarıdır; authorization, CompanyContext veya iş
başarısı kanıtı değildir. İstemciden gelen correlation güvenli uzunluk/karakter kontrolünü
geçse bile actor/tenant seçemez. Async sınırda ERP-000F'nin kabul edilmiş event zarfındaki
correlation/causation taşınır; yeni rastgele ilişki veya ham message payload loglanmaz.

Clock skew, UTC kaynağı, host/worker zaman senkronizasyonu ve farklı sistemlerden gelen
timestamp güven seviyesi decision pack'te belirlenmelidir. Sıralama yalnız timestamp'ten
çıkarılmaz; event/queue sequence sözleşmesi varsa sahibi modülden tüketilir.

## 8. CompanyId, cardinality ve bilgi sızıntısı

### 8.1 Varsayılan yasak alanlar

Aşağıdakiler varsayılan/genel structured-log alanı, global metric label, trace attribute,
span adı, dashboard dimension, alarm etiketi veya exception metni olamaz:

- raw CompanyId, ActorId/UserId, role listesi ve session/token kimliği;
- record/document/blob/object/message/idempotency key ve provider kimliği;
- müşteri/tedarikçi adı, VKN, IBAN, telefon, e-posta, belge numarası;
- route içindeki gerçek ID, query string, SQL parametresi, request/response body;
- secret, connection string, signed URL, authorization/cookie header;
- serbest kullanıcı metni, dosya içeriği, AI prompt/tool argümanı veya stack'in hassas
  local değerleri.

Metric label sözlüğü environment, service, route template, operation class, outcome
class, status family ve dependency kind gibi bounded değerlerden oluşur. Yeni label veya
değer uzayı cardinality bütçesi, privacy review ve contract test olmadan eklenmez.

### 8.2 Tenant tanılama

Şirket/kullanıcı bazlı maliyet veya sağlık görünümü gerekiyorsa raw kimliği global time
series label'ına koymak yerine güvenli tenant-scoped aggregate/store veya access-controlled
restricted query kullanılır. Pseudonymous token gerekiyorsa tenantlar arası korelasyonu
gereksiz büyütmeyen, anahtarı secret store'da olan ve rotasyon davranışı kabul edilmiş
bir keyed mapping tasarlanmalıdır; algoritma bu belgede seçilmez.

Raw kimliğe gerçekten ihtiyaç duyan dar incident araştırması ancak ayrı restricted alan,
açık veri sınıfı, sınırlı retention ve auditli operator erişimi kararından sonra mümkün
olabilir; genel sink'e raw ID yazmak geçici çözüm değildir.

Yetkisiz operator, dashboard toplamı, boş sonuç, alarm adı, gecikme farkı veya health
detayından başka şirketin varlığını öğrenemez. Grup/genel görünüm ayrı açık permission ve
gerekli ABAC kapsamı ister; UI'da gizlemek tek kontrol değildir.

## 9. Redaction, KVKK ve operator erişimi

### 9.1 Redaction ve minimizasyon

- Telemetry source'ta allowlist alanlarla üretilir; exporter/sink filtresi ikinci savunma
  katmanıdır, ilk kontrol değildir.
- Header, body, SQL parameter, exception data, object metadata ve user-entered text
  varsayılan deny'dır.
- Exception type ve kabul edilmiş stable error code korunabilir; mesaj/stack ayrı veri
  sınıfı ve redaction görür.
- Log template ile argument ayrılır; string interpolation veya nesneyi bütünüyle serialize
  etmek hassas veri kaçış yolu olamaz.
- Redaction failure fail-closed davranır; hassas alanı “tanı için” sessizce export etmez.
- Veri sınıfı, erişim, retention, silme/anonimleştirme, legal hold ve incident kullanım
  amacı KVKK minimizasyonuyla decision pack'te kilitlenir.

### 9.2 Operator RBAC/ABAC

Public health cevabı yalnız gerekli minimum statüyü verir. Detailed health, log arama,
trace açma, security-event okuma, export ve retention/config değişikliği birbirinden ayrı
server-side permission ve ABAC kararlarıdır. Permission kodları veya rol adları bu
readiness belgesinde uydurulmaz.

Operator erişimi:

- default-deny ve least-privilege olmalı;
- CompanyContext veya açık platform-wide scope olmadan tenant detayı göstermemeli;
- hassas görüntüleme/export için gerekçe, zaman sınırlı elevation ve business audit veya
  kabul edilmiş security evidence üretmeli;
- kendi erişim logunu silmeye/değiştirmeye izin vermemeli;
- normal uygulama kullanıcılarının diagnostic endpoint/sink'e doğrudan erişimini
  engellemeli;
- support/incident kullanımında kaynak tenant sınırını tekrar doğrulamalıdır.

Exact erişim matrisi, break-glass prosedürü ve hangi operator eyleminin business audit ya
da security-event üreteceği `ERP-000L-DECISION-PACK` kapanışıdır.

## 10. Health probe semantiği

### 10.1 Liveness

Liveness yalnız process'in çalışabildiğini ve kendi event loop'unun ilerlediğini ölçer.
SQL, queue, object store, dış entegrasyon veya telemetry sink arızası liveness'ı doğrudan
başarısız yapıp restart fırtınası oluşturmamalıdır. Probe hızlı, bounded ve yan etkisizdir.

### 10.2 Startup

Startup; zorunlu configuration/schema compatibility ve uygulamanın trafik almadan önce
tamamlaması gereken bootstrap durumunu temsil eder. Production migration'ı probe içinde
çalıştırmaz. Başlatma bitmeden readiness başarı vermez; timeout ve başarısızlık nedeni
restricted operator kanıtında görünür olur.

### 10.3 Readiness

Readiness, ilgili process'in o anda yeni trafik/iş kabul edip güvenli tamamlayabilmesini
ölçer. API için zorunlu DB/identity/config; worker için zorunlu DB/lease/queue sözleşmesi
gibi process-specific dependency'ler test edilir. Opsiyonel AI, arama, belge veya dış
entegrasyon arızası temel ERP'yi gereksiz yere unready yapmaz; capability degradation
ayrı ve görünürdür.

Readiness kontrolü:

- kısa timeout/cancellation ve bounded concurrency kullanır;
- business kaydı, audit, outbox veya object yazmaz;
- cache'deki eski başarıyı güncel sağlık diye sınırsız sürdürmez;
- dependency adı, host, connection detail, stack veya tenant verisini public cevaba
  sızdırmaz;
- yük altında kendisi ek incident veya thundering-herd üretmez.

Mevcut `/health/live` yalnız başlangıç noktasıdır. Endpoint adları, orchestrator probe
ayarları ve detay formatı karar paketi kabul edilmeden çoğaltılmaz.

## 11. Metric, trace ve sampling sınırı

### 11.1 SLI aday sınıfları

Kabul kararı için ölçülebilir sınıflar:

- API/worker availability ve request/job outcome oranı;
- route template/operation class latency dağılımı;
- hata, timeout, cancellation, retry ve circuit state;
- CPU/memory/thread/connection pool/disk/network saturation;
- SQL query class latency, timeout/deadlock ve pool wait;
- outbox backlog depth, oldest age, dispatch latency, retry ve poison/dead-letter;
- integration availability ve bounded retry/backpressure;
- backup age/result, restore süresi ve checksum/mutabakat sonucu;
- telemetry pipeline queue/drop/export failure ve saat sapması.

Bu liste eşik veya ürün seçimi değildir. SQL query text/parameter, message payload,
tenant ID ve exception text metric label olmaz.

### 11.2 Sampling

- Metric aggregation ve trace sampling politikası environment/workload bazında sürümlü
  ve geri alınabilir olmalıdır.
- Error/slow/dependency trace örneklemesi tanıya yeterli olmalı; oranlar OPEN-015 hacmi
  olmadan sabitlenmez.
- Sampling kararı trace üzerinde görünür olmalı ve correlation ile log araması yanlış
  “trace yoksa olay yok” sonucuna varmamalıdır.
- Business audit ile kalıcı security-event sampling'e tabi değildir.
- Tail/head sampling, collector buffer ve export retry seçimi gerçek yük ve kaynak
  bütçesiyle kanıtlanmadan yapılmaz.
- Telemetry kendi kaynak tüketimi için CPU/memory/queue/drop bütçesi taşır; ERP işini
  sınırsız buffer veya retry ile durduramaz.

## 12. SLO, alarm, sahiplik ve runbook

Master rapordaki arama/yazma gecikmesi ile aylık `%99,9` kullanılabilirlik değerleri
başlangıç önerisidir. `OPEN-014` ve `OPEN-015` kapanıp gerçek hacim kanıtı alınmadan
production SLO'su veya alarm threshold'u sayılmaz. `OPEN-017` içindeki geçici RPO/RTO da
aynı şekilde kesin alarm değeri değildir.

Her kabul edilmiş SLO şunları taşır:

- açık kullanıcı/capability ve ölçüm sınırı;
- SLI formülü, iyi/kötü olay tanımı, veri kaynağı ve eksik veri davranışı;
- pencere, hedef, error budget ve maintenance istisnası;
- Company/region/environment kapsamı ve tenant sızıntısız aggregation;
- owner, reviewer, yürürlük tarihi, sürüm ve rollback;
- sentetik ve gerçek yük kanıtı.

Her alarm:

- sinyal, severity, pencere, dedup/suppression ve recovery koşulu;
- ilk müdahale sahibi, yedek/escalation sahibi ve ulaşılabilirlik kapısı;
- yanlış pozitif/negatif ve missing-data davranışı;
- ilgili runbook, dashboard/query ve son deployment bağlantısı;
- kullanıcı etkisi, tenant güvenliği ve incident/audit bağını

taşımadan production'a açılmaz. Alarm “kuruldu” diye kapanmaz; kontrollü hata enjeksiyonu
ile tetiklenme, bildirim, sahiplenme, düzeltme ve recovery kapanışı kanıtlanır.

Runbook en az detection, ilk güvenlik kontrolü, triage, bağımlılık teşhisi, containment,
rollback/failover, veri bütünlüğü, recovery, kullanıcı iletişimi, post-incident ve kanıt
saklama adımlarını içerir. Çalışmayan veya sahibi olmayan runbook release kapısı olamaz.

## 13. Retention, dayanıklılık ve pipeline arızası

- Log, metric, trace, business audit ve security-event ayrı retention/erişim/failure
  politikalarına sahiptir.
- Telemetry export arızası business transaction'ı “başarısız audit” gibi göstermez ve
  sınırsız local disk/memory büyümesine yol açmaz.
- Drop, retry, backpressure ve local buffering davranışı ölçülür; exact kapasite ve süre
  gerçek envanter olmadan seçilmez.
- Sink geri geldiğinde duplicate/reordered telemetry business duplicate event sayılmaz.
- Security-event dayanıklılığı normal trace buffer'ına indirgenemez; kayıp/failure
  semantiği karar paketinde açıkça seçilir.
- Retention süresi yalnız storage maliyetine göre değil veri sınıfı, KVKK amacı, incident,
  legal hold ve erişim gereksinimine göre onaylanır.
- Backup telemetry'si backup'ın kendisi değildir; gerçek artefakt, checksum ve restore
  mutabakatına bağlanır.

## 14. Task-local kabul planı

Aşağıdaki senaryolar readiness planıdır; yeni kanonik `AT-*` değildir ve geçmiş
sayılmaz. Decision pack kabul edildiğinde gerekirse mevcut merkezi acceptance kayıtlarına
insan review'uyla eşlenir.

### 14.1 Kanal ve korelasyon

1. Başarılı/no-change kritik iş etkisi transactional business audit'te kalır; aynı olayın
   telemetry kopyası audit SoT'u olmaz.
2. Yetkisiz tenant erişimi kabul edilmiş durable security-event üretir; trace sampling
   veya sink kesintisi bu kanıtı sessizce yok etmez.
3. Transient API/provider/dependency hatası telemetry'de tanılanır; business audit veya
   security-event gibi yanlış sınıflandırılmaz.
4. HTTP ve ERP-000F async sınırında correlation/causation güvenli biçimde taşınır;
   istemci kimliği CompanyId/authorization üretmez.
5. Replay, no-change, conflict, retry ve duplicate dispatch outcome'ları sahip E/F
   sözleşmesiyle tutarlı görünür; ikinci iş olayı icat edilmez.

### 14.2 Tenant, redaction ve erişim

6. A şirketi operatorü veya normal kullanıcısı B şirketinin varlığını log, metric, trace,
   alarm, health detayı, sayı veya süre farkından öğrenemez.
7. Raw CompanyId/ActorId/record/message/document/provider kimlikleri metric label, span adı,
   dashboard dimension veya public health cevabında bulunmaz.
8. VKN, IBAN, ad, e-posta, telefon, belge no, request body, SQL parametresi, token, secret,
   signed URL ve serbest kullanıcı metni adversarial fixture'da source redaction'ı geçemez.
9. Detailed health/log/trace/security-event/export erişimi server-side RBAC+ABAC olmadan
   reddedilir; UI gizleme tek kontrol değildir.
10. Platform-wide veya break-glass erişim açık gerekçe/süre/evidence olmadan tenant
    sınırını aşamaz; operator eylemi sonradan silinemez.
11. Yeni bir label/value uzayı cardinality ve privacy contract testinden geçmeden kabul
    edilmez; saldırgan ID/path/header ile series patlaması yaratamaz.

### 14.3 Health ve degradation

12. SQL/queue/sink kesintisi liveness restart fırtınası üretmez.
13. Zorunlu startup tamamlanmadan readiness başarılı olmaz; probe migration veya business
    write çalıştırmaz.
14. API'nin zorunlu SQL bağımlılığı kullanılamazsa readiness fail olur ve public cevap
    connection/topology/tenant detayı sızdırmaz.
15. Opsiyonel AI/arama/entegrasyon arızası temel ERP'yi unready yapmaz; capability
    degradation ve operator alarmı görünür olur.
16. Probe timeout/cancellation, paralel çağrı ve dependency gecikmesi bounded kalır;
    probe kendi incident'ını büyütmez.

### 14.4 Gerçek API ve SQL kanıtı

17. Gerçek API çağrısında route template, outcome, duration ve correlation ilişkilidir;
    gerçek path/query/body veya başka tenant verisi export edilmez.
18. Unhandled exception güvenli stable error code üretir; public/telemetry çıktısında
    secret, SQL parametresi ve hassas stack local'i bulunmaz.
19. Gerçek SQL Server'da normal, yavaş, timeout, deadlock/concurrency ve pool pressure
    senaryoları güvenli query class ile tanılanır; raw SQL parametre/payload sızmaz.
20. ERP-000D audit atomik rollback ve ERP-000E replay/conflict senaryolarında telemetry
    outcome gerçek DB sonucu ile mutabıktır.

### 14.5 Gerçek kuyruk ve backup kanıtı

21. Gerçek ERP-000F dispatcher'ında backlog depth, oldest age, lease, retry/backoff,
    poison/dead-letter ve recovery güvenli bounded label'larla ölçülür.
22. Duplicate/reordered telemetry, inbox dedup sonrası ikinci business effect veya ikinci
    audit gerçeği gibi yorumlanmaz.
23. Gerçek backup başarısızlığı alarmı tetikler; owner runbook ile teşhis/iyileştirme ve
    alarm recovery kapanışını kanıtlar.
24. Gerçek nokta-zaman SQL restore'u ve nesne deposu checksum/mutabakatı ölçülür; yalnız
    “backup job başarılı” metriği `AT-070`u geçirmez.
25. Ölçülen restore süresi/veri kaybı geçici RPO/RTO'dan ayrı raporlanır; OPEN-017 insan
    kararı olmadan kesin production kabulü ilan edilmez.

### 14.6 SLO, sampling ve operasyon

26. Temsilî üç yıllık hacim ve yoğun saat yükünde telemetry overhead, series cardinality,
    export drop/backpressure ve query maliyeti ölçülür.
27. Sampling altında seçili trace yokken log/metric bunu olay yokmuş gibi göstermez;
    durable security-event ve audit kaybolmaz.
28. SLO veri kaynağı eksik veya exporter arızalıysa pencere sessiz yeşil sayılmaz;
    missing-data politikası uygulanır.
29. Kontrollü hata enjeksiyonu alarmın tetiklenme, dedup, bildirim, owner acknowledgment,
    runbook, recovery ve kapanış zincirini kanıtlar.
30. Telemetry pipeline tamamen kapalıyken temel ERP güvenli davranır; bounded buffer/drop
    görünür olur ve secret açarak debug fallback'i uygulanmaz.

## 15. Test ve kanıt stratejisi

### 15.1 Unit/contract

- telemetry schema/version ve allowlist/redaction testleri;
- metric label cardinality bütçesi ve bounded enum testleri;
- correlation/causation propagation ve invalid input testleri;
- audit/security-event/telemetry routing state-machine testleri;
- probe classification, timeout, cancellation ve side-effect negative testleri;
- sampling/missing-data/SLO calculation ve alarm transition testleri;
- operator permission/ABAC policy unit testleri.

### 15.2 Integration ve gerçek altyapı

- gerçek API + CompanyContext + Identity ile A/B izolasyonu ve restricted operator erişimi;
- gerçek SQL Server dependency, slow query, timeout/deadlock/pool ve redaction kanıtı;
- gerçek ERP-000F outbox/dispatcher/inbox üzerinde queue yaş/retry/poison/recovery kanıtı;
- gerçek ERP-000N DB + object-store backup/restore, checksum, RPO/RTO ölçümü ve alarm
  lifecycle kanıtı;
- telemetry sink/collector yerine implementation kararında seçilen gerçek bileşenle
  export failure, bounded buffer, backpressure ve recovery testi;
- gerçek deployment/startup/readiness/liveness orkestrator davranışı.

Fake dependency testleri hata matrisi için kullanılabilir; gerçek SQL, queue ve restore
kanıtının yerine geçmez. CI'ya hangi testin nasıl ekleneceği implementation kararından
sonra ayrı diff'tir; bu docs-only dal CI değiştirmez.

### 15.3 Güvenlik ve insan doğrulaması

- log injection, PII/secret corpus, high-cardinality ve tenant-oracle adversarial testleri;
- operator/support/break-glass akışının insan review'u;
- alarm owner/escalation ve runbook game-day tatbikatı;
- `SY-07` için responsive/keyboard/accessibility, empty/loading/degraded/error durumları;
- KVKK/veri sahibi tarafından veri sınıfı, retention ve erişim review'u.

`SY-07` görseli/referansı eksik olduğundan dashboard veya sistem sağlığı UI'sı bu dalda
tasarlanmaz. UI implementation, katalog uzlaştırması ve insan onayı olmadan GO değildir.

## 16. `ERP-000L-DECISION-PACK` kapanış koşulları

Task-local aggregate blocker ancak aşağıdakilerin tamamıyla kaldırılabilir:

- OPEN-014 sunucu/ağ/depolama/env envanteri kabul edildi;
- OPEN-015 hacim, eşzamanlılık ve yoğun saat verisi kabul edildi;
- OPEN-017 kesin RPO/RTO ve felaket topolojisi kabul edildi;
- A/B/C/D/E/F/M/N görevleri kabul edilip `main`e alındı ve contract'ları doğrulandı;
- business audit, durable security-event ve telemetry ownership/failure/retention ayrımı
  kabul edildi;
- sürümlü telemetry zarfı, low-cardinality sözlüğü ve redaction/data-class policy kabul
  edildi;
- operator RBAC/ABAC, platform-wide scope, export ve break-glass matrisi kabul edildi;
- liveness/readiness/startup ve capability degradation semantiği kabul edildi;
- SLI/SLO/sampling/missing-data/error-budget politikası gerçek hacimle kabul edildi;
- alert severity, owner, escalation, maintenance ve runbook matrisi tamamlandı;
- implementation için paket/provider/collector/sink/exporter seçimi güncel resmi
  dokümantasyon, lisans, güvenlik ve kapasite review'uyla ayrıca kabul edildi;
- gerçek API/SQL/queue/backup test planı ve kanıt formatı onaylandı;
- `SY-07` UI referans açığı ayrı katalog/insan review kapısında ele alındı;
- task-local senaryolar gerekirse merkezi acceptance satırlarına review ile eşlendi;
- P0/P1 güvenlik, privacy ve mimari bulgusu kalmadı.

Bu kapanışların hiçbiri mevcut `OPEN-*` veya `AT-*` satırını bu docs-only PR'da
tamamlanmış yapmaz.

`OPEN-018`, ERP-000N üzerinden audit/belge retention ve legal-hold kapısına transitif
girdidir; L'nin log/metric/trace ve security-event saklama, erişim, imha ve legal-hold
semantiğini kendiliğinden belirlemez. Bu kanal-bazlı PII/retention kararı decision pack'te
açık kalır. Issue `#63` de yalnız Identity/control-plane evidence hazırlığıdır; L'nin
genel security-event owner/provider/sink/failure contract'ını kapatmaz.

## 17. Kodlama giriş kapısı

### Şimdi izinli

- telemetry event/field inventory ve veri sınıfı taslağı hazırlamak;
- mevcut log/health/cardinality/secret risklerini read-only envanterlemek;
- OPEN-014/015/017 için işletme girdisi ve kanıt şablonu hazırlamak;
- SLI, alarm-owner, runbook ve test matrisi taslağını insan review'una sunmak;
- gerçek veri içermeyen redaction/cardinality/adversarial fixture planlamak;
- yalnız dokümantasyon farklarını güncel `main` ile conflict-safe uzlaştırmak.

### Şimdi yasak

- package/provider/collector/sink/exporter veya dashboard seçmek/eklemek;
- secret, endpoint, connection, certificate, network egress veya config eklemek;
- health endpoint/middleware/instrumentation/DB/security-event store kodlamak;
- metric name/label, sampling oranı, retention, SLO veya alarm threshold'unu production
  davranışı olarak kilitlemek;
- raw CompanyId/ActorId/record ID veya hassas payload'ı “geçici” telemetry'ye açmak;
- audit'i loga, security-event'i trace'e veya backup metriğini restore kanıtına eşitlemek;
- CI/deployment/container/reverse proxy davranışı değiştirmek;
- `SY-07` ekranını görsel referans ve UI kabulü olmadan uygulamak;
- bütün manifest bağımlılıkları kabul edilip `main`e alınmadan implementation başlatmak.

## 18. Ortak manifest ve birleştirme kapısı

Bu current-main clean rebuild `main@9d884b789bc20442686567e50de243feb6496d42`
tabanından ayrılmıştır. ERP-000M readiness `blocked`, ERP-000O readiness `blocked` ve
ERP-000N readiness `blocked` olarak sırasıyla main'e alınmıştır. Eski PR head'i
`47e2732c043e121d51861f5fcc9c18ce6bf64631` ile CI `33134372310` tarihsel kanıttır;
bu branch'in kabul kanıtı olarak yeniden kullanılamaz.

Güvenli kural:

1. Bu docs-only L readiness diff'i bağımlı implementation başlamadan review edilebilir.
2. Manifest root metadata'sı ve L dışındaki on dört task objesi exact current-main
   parity taşır; başka status, dependency, blocker, scope veya acceptance değiştirilmez.
3. L direct dependency listesi A/B/C/D/E/F/M/N'dir. N'nin A/B/C/D/E/F/G/M/O listesi
   korunur ve birleşik graph 15 task / 38 edge ile `M → O → N → L` yönünde döngüsüzdür.
4. Bütün görev statüleri GitHub merge gerçeği ve ilgili delivery kanıtıyla yeniden
   doğrulanır; readiness/spec merge'i `done` veya implementation kabulü sayılmaz.
5. Current-main progress blob'u byte-for-byte prefix olarak korunur ve yalnız tek yeni
   Döngü 16 kaydı eklenir.
6. L implementation dalı ancak A/B/C/D/E/F/M/N'nin tamamı kabul edilip `main`e alındıktan,
   OPEN-014/015/017 ile decision pack kapandıktan sonra güncel `main`den açılabilir.

Bu PR task manifesti, observability/security ve production activation sınırına
dokunduğundan Draft ve exact-SHA bağımsız review kapısında kalır. Fresh standard CI,
gerçek SQL Server job'ı ve UI golden başarılı olmadan kabul edilmez; head veya base
değişirse önceki review/CI kanıtı stale olur.

Bu sync kapısı implementation izni vermez ve merge sırasını iş kuralı tahminiyle
değiştirmez.

## 19. Sonuç

ERP-000L için sonuç **readiness belgesi GO, implementation NO-GO**'dur. Merkezî
gözlemlenebilirlik yönü kilitlidir; fakat telemetry/business-audit/security-event ayrımı,
tenant-safe redaction/operator erişimi, probe semantiği, SLO/sampling/alarm sahipliği ve
gerçek API/SQL/queue/backup kanıtı kabul edilmeden kodlama güvenli değildir.

ERP-000L ancak A/B/C/D/E/F/M/N `main`de kabul edildikten, `OPEN-014`, `OPEN-015`,
`OPEN-017` ile `ERP-000L-DECISION-PACK` kapandıktan sonra implementation için yeniden
değerlendirilebilir. O zamana kadar paket, provider, sink, exporter, dashboard, threshold,
secret veya production davranışı tahmin edilmeyecektir. Readiness belgesinin merge'i
production activation izni değildir; PR #2, ERP-000F/M, Identity #62/#63 ve Cari
sahiplik/acceptance kapıları kapalı kalır.
