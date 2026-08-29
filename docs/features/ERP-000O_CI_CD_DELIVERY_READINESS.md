# ERP-000O — CI/CD ve Teslimat Güvencesi Hazırlık Kapısı

- İlk hazırlık tarihi: 2026-08-28
- Current-main yeniden doğrulama tarihi: 2026-08-29
- İncelenen taban: `main` / `c1d28cc44e775cfea3a528cfd755cbe85b5ee290`
- Görev: `ERP-000O — CI/CD ve kalite kapıları`
- Kapsam: yalnız readiness analizi, güven sınırı ve kabul planı
- Readiness/specification durumu: **GO**
- Workflow, kod, altyapı, repository ayarı ve deployment durumu: **NO-GO / BLOCKED**

## 1. Yönetici özeti

Göymen ERP'nin mevcut CI akışı faydalı ve gerçek bir başlangıç kanıtıdır. Pull request ve
`main` push'larında backend, frontend, gerçek SQL Server ve container build işleri
çalışmaktadır. Bununla birlikte yeşil CI bugün doğrulanabilir, değişmez ve geri alınabilir
bir yayın zinciri değildir:

- private repository'nin güncel GitHub planında branch protection/ruleset zorlaması
  etkinleştirilemediği için başarısız kontrol merge veya doğrudan push'u teknik olarak
  engellemez;
- migration SQL dosyaları ve container image'ları job içinde üretilir fakat kalıcı
  artifact olarak yüklenmez, hashlenmez, imzalanmaz ve promote edilmez;
- release/tag/deployment/environment, artifact registry, config manifesti, SBOM,
  provenance, attestation, production migration veya rollback provası yoktur;
- workflow action'ları ve container tabanları mutable tag kullanır; repository düzeyinde
  action SHA pin zorlaması ve allowlist yoktur;
- mevcut compose tabanlı web build'i Development bootstrap özelliğini build-time olarak
  açar ve production release candidate sayılamaz.

Bu nedenle ERP-000O yalnız ERP-000A'ya bağlı `ready` bir iş değildir. Fiziksel ADR-010,
gerçek altyapı envanteri, ERP-000M migration/SQL Server sözleşmesi ve task-local teslimat
karar paketi kapanmadan `.github`, Docker, deployment, registry, secret veya environment
değişikliği yapmak güvenli değildir.

Bu belge provider, registry, deployment strategy, action/paket, threshold, signing
algoritması, secret manager veya production davranışı seçmez. Yeni kanonik `OPEN-*`,
`AT-*` veya `ADR-*` kimliği üretmez.

## 2. Bağlayıcı repository gerçeği

### 2.1 Kilitli kararlar

| Kayıt | ERP-000O için bağlayıcı sonuç |
|---|---|
| `DEC-GOV-004` | Çekirdek modüller bağımlılık sırasıyla geliştirilir; eksik modüllü erken production yoktur. |
| `DEC-GOV-005` | Canlıya geçiş en az iki prova, final delta, rollback ve mutabakat ister. |
| `DEC-GOV-006` | Tek geliştiricili yapıda bağımsız kabul açığı otomatik test, yazılı senaryo ve restore kanıtıyla kapatılır. |
| `DEC-TEC-002` | .NET/React sürüm güvenlik güncellemeleri yayın başlangıcında doğrulanır. |
| `DEC-TEC-003` | SQL Server henüz PoC adayıdır; performans, arama, audit, restore ve maliyet kapıları geçmelidir. |
| `DEC-TEC-004` | On-prem/hibrid yönü vardır; kesin topoloji ve sunucu envanteri açık karardır. |
| `DEC-TEC-008` | Dev/test/staging/prod ayrıdır; CI/CD, migration/rollback manifesti ve onaylı production yayını zorunludur. |
| `DEC-TEC-009` | Log, metric, trace, korelasyon ve alarm yayın zincirinin sonradan eklenen bir fazı değildir. |
| `DEC-MIG-001`–`DEC-MIG-004` | Migration batch, prova, final delta, SoT tarihi, rollback sahibi ve kaynak snapshot kanıtı korunur. |
| `DEC-TST-001`–`DEC-TST-002` | Kritik kabul, gerçek veri örneği, güvenlik/performans/restore kanıtı ve açık P0/P1 bulunmaması gerekir. |

Master v3 ayrıca bağımlılık/SBOM, secret, statik/dinamik güvenlik taraması; aynı kaynaktan
üretilmiş release artifact, migration ve sürüm manifesti; düzenli restore; ayrı ortamlar
ve insan onaylı production yayınını zorunlu tutar.

### 2.2 ADR drift'i

`docs/architecture/ADR_REGISTER.md`, `ADR-010 — Ayrı ortam, CI/CD, rollback manifesti`
kaydını `Kabul` gösterir. İncelenen `main` üzerinde buna karşılık gelen fiziksel
`docs/adr/ADR-010-*.md` dosyası yoktur.

Register satırı tek başına şu uygulanabilir kararların yerine geçmez:

- artifact sınırı, kimliği, sürümü, saklama ve registry sahipliği;
- build-once/promote davranışı;
- source, dependency ve base-image provenance'ı;
- config schema, secret reference ve environment drift davranışı;
- DbContext migration envanteri, sıra, deployment identity ve hata/geri dönüş modeli;
- branch protection, required check, reviewer, bypass ve hotfix yetkisi;
- staging/production approval ve rollback/forward-fix akışı.

Fiziksel ADR-010 insan review'uyla oluşturulup register ile uzlaştırılmadan ERP-000O
implementation başlatılamaz. Bu readiness belgesi ADR-010'u kendiliğinden oluşturmaz veya
kabul edilmiş saymaz.

### 2.3 Açık kararlar

- `OPEN-014`: sunucu, CPU/RAM/disk/RAID, ağ, UPS, yedek ve gerçek environment envanteri
  bilinmediği için deployment target, runner/topology ve kapasite seçilemez.
- `OPEN-013`: production SSO, MFA ve dış erişim politikası kapanmadan production kimlik
  kapısı açılamaz.
- `OPEN-015`: gerçek hacim ve yoğun saat verisi kapanmadan performans/release SLO eşikleri
  kesinleştirilemez.
- `OPEN-017`: kesin RPO/RTO ve felaket topolojisi kapanmadan production release/rollback
  başarı ölçütü kilitlenemez.

`OPEN-013`, `OPEN-015`, `OPEN-017`, ERP-000N ve ERP-000L; ERP-000O temel delivery
mekanizmasını tasarlamak için hard dependency yapılmaz. Bunlar production activation'ın
fail-closed kapılarıdır. Aksi halde O → N → L yönü geri dönüp dependency döngüsü yaratabilir.

Identity production reconciliation runner Issue `#62` ve global security evidence Issue
`#63` de current repository'de **DRAFT / NO-GO** durumundadır. Bunlar fiziksel ADR-010 ve
delivery/evidence sınırıyla uzlaştırılması gereken güncel üretim kapılarıdır; bu readiness
sync'i onları ERP-000O manifest `blocked_by` listesine sessizce eklemez veya kapatmaz.

## 3. Mevcut CI ve dış kontrol yüzeyi kanıtı

### 3.1 Repository workflow'u

`main` üzerindeki `.github/workflows/ci.yml` şu dört işi çalıştırır:

1. **Backend build, test and migration**
   - .NET bootstrap integrity preflight; .NET 10 setup, restore, Release build ve solution
     testleri;
   - architecture dependency guard ile exact platform tenant-bypass source inventory
     guard'ı;
   - transitif NuGet vulnerability envanteri;
   - Organization, Identity, Audit ve Idempotency için dört idempotent migration SQL
     dosyası üretimi.
2. **Frontend lint, test and build**
   - Node 24, `npm ci`, `npm audit --audit-level=high`, lint, test ve build.
3. **SQL Server relational acceptance**
   - gerçek SQL Server 2022 service container;
   - migration/seed, CompanyId izolasyonu, RBAC/ABAC, audit, idempotency/concurrency ve
     relational constraint kabulü.
4. **Container build**
   - compose model doğrulaması;
   - API ve web image build'i.

Workflow pull request ve `main` push'larında çalışır. Job token'ı repository içeriğinde
read-only'dir. Bunlar korunması gereken olumlu başlangıç kanıtlarıdır.

Çözümde Audit, Counterparty, Idempotency, Identity ve Organization olmak üzere beş
design-time migration DbContext'i vardır. Workflow yalnız dört script üretir ve
Counterparty migration scriptini üretmez. Tenant-bypass inventory guard'ı incelenmiş source
fingerprintlerini korur; her tenant query'nin güvenli olduğunu veya bütün migration'ların
delivery kapsamına alındığını kanıtlayan bir migration discovery gate'i değildir.

`main` üzerinde ayrıca `.github/workflows/ui-catalog-validation.yml` bulunur. Bu ayrı
workflow pull request ve `main` push'larında UI katalog validator negatif testlerini ve
kanonik katalog golden-state doğrulamasını çalıştırır. Bu, dokümantasyon/UI referans
bütünlüğü için olumlu bir kapıdır; branch enforcement, release artifactı veya deployment
kanıtı değildir.

### 3.2 Doğrulanmış son çalışma

İlk readiness analizinin 2026-08-28 tarihli read-only GitHub kontrolünde
`main@9f08ff1` için CI run
`33127982533` dört işte başarılıdır. Aynı run'ın artifact API sonucu
`total_count = 0`dır. Bu nedenle:

- CI logu testlerin çalıştığını kanıtlar;
- job çalışma alanında migration dosyası ve image üretildiğini kanıtlar;
- fakat kalıcı, indirilebilir, hashli ve tekrar promote edilebilir bir release artifactı
  kanıtlamaz.

Bu ilk snapshot ve aşağıdaki `main@caff02d3` senkronizasyon snapshot'ı yalnız tarihsel
CI/artifact boşluğunu kanıtlar; güncel ana dal gerçeği veya yeni PR head kabul kanıtı olarak
kullanılmaz. ERP-000E `done`, ERP-000M readiness ise `blocked` olarak `main`dedir. Canlı run
ID ve dış repository ayarları zamanla değişebilir. ERP-000O review'unda bu değerler yeniden
salt-okunur doğrulanmalı; tarihsel snapshot kalıcı ayar varsayımı yapılmamalıdır.

Tarihsel senkronizasyon tabanı `main@caff02d3` için standart CI run `33147071294` dört işte,
UI katalog golden validation run `33147071296` ise kendi doğrulama işinde başarılıdır.
Standart CI run'ının artifact API sonucu yine `total_count = 0`dır. Böylece test omurgası
ve UI katalog koruması o tarihsel tabanda doğrulanmış, release artifact boşluğu ise
kapanmamış olarak kalır.

2026-08-29 current-main yeniden doğrulamasında `main@c1d28cc4` için standart CI run
`33265386298` dört işte, UI katalog golden validation run `33265386300` kendi işinde
başarılıdır. Standart CI artifact API sonucu `total_count = 0`dır. Exact durum ve artifact
API sonucu PR #19 final head review'unda yeniden doğrulanmalıdır; yeni PR head kendi fresh
CI kanıtını üretmeden bu main snapshot'ı merge kabul kanıtı sayılamaz.

### 3.3 GitHub enforcement gerçeği

2026-08-29 tarihli current-main read-only GitHub API kontrolünde:

- repository private ve default branch `main`dir;
- branch protection ve repository ruleset sorguları mevcut plan için upgrade/public
  repository gerektiren `403` sonucu vermiştir;
- repository environment sayısı `0`dır;
- repository Actions secret sayısı `0`dır;
- repository Actions variable sayısı `0`dır;
- Actions etkin, `allowed_actions = all`, `sha_pinning_required = false`tır;
- default workflow permission `read`, PR approve yetkisi `false`tır;
- code scanning etkin değildir;
- secret scanning kapalıdır;
- Dependabot alerts kapalıdır;
- `CODEOWNERS` yoktur;
- release, tag ve deployment kanıtı yoktur.

Manuel disiplin önemlidir fakat “başarısız kapı merge'i engeller” kabulünün teknik kanıtı
değildir. Kullanıcı private repository'yi public yapma, ücretli plan, başka korumalı git
platformu veya eşdeğer teknik enforcement seçeneklerinden hangisini kullanacağını açıkça
kararlaştırmalıdır. Bu belge seçim yapmaz.

## 4. Mevcut boşluklar

### 4.1 Merge ve review enforcement

- Başarısız veya henüz bitmemiş CI sonucu merge/direct push'u teknik olarak engellemiyor.
- Force-push, branch deletion, stale approval, required conversation ve admin bypass
  politikaları kilitli değil.
- Tek geliştiricili projede bağımsız review kanıtının GitHub üzerinde hangi kimlikle ve
  hangi kural ile sağlanacağı açık değil.
- `.github`, governance, migration, security ve deployment değişiklikleri için ayrı
  ownership/reviewer matrisi yok.

### 4.2 Artifact ve provenance

- Backend publish çıktısı ve frontend dist kalıcı artifact olarak yüklenmiyor.
- Container image build edilir fakat registry'ye push edilmez; digest kaydı yoktur.
- Migration SQL dosyaları upload edilmez, hashlenmez ve release'e bağlanmaz.
- Artifact manifesti, SBOM, source/build provenance, signing veya attestation yoktur.
- Aynı immutable artifactın staging'den production'a promote edildiği kanıtlanamaz.
- Test sonucu, coverage, security scan ve migration kanıtının retention politikası yoktur.

### 4.3 Supply chain

- Workflow action'ları major tag kullanır; immutable commit SHA zorlaması yoktur.
- Dört workflow işi mutable `ubuntu-latest` runner etiketi kullanır; trusted release'i
  üreten executor/runner image ve OS release kimliği, kritik ön-yüklü build-tool sürümleri,
  trust/isolation sınıfı ve update/reproducibility politikası kanıt zarfında yoktur.
- SQL Server, .NET, Node ve Nginx base image'ları digest ile kilitli değildir.
- Fail-closed .NET bootstrap integrity preflight; `global.json` politikasını ve
  `Directory.Build.props`, `NuGet.Config`, `dotnet-tools.json` byte/hash yüzeyini kilitler,
  yasak inherited bootstrap dosyalarını reddeder. Bu olumlu kontrol SDK politikasındaki
  `latestFeature` roll-forward'ı, Actions/runner/base-image kimliğini veya release
  artifact/provenance eksiklerini ortadan kaldırmaz.
- NuGet için restore lock dosyası yoktur; npm lock dosyası vardır.
- Action allowlist, dependency update review, SBOM/license policy ve exception yaşam
  döngüsü yoktur.
- NuGet vulnerability komutunun bulgu olduğunda gerçekten fail-closed davrandığına dair
  pozitif/negatif gate testi yoktur.
- SAST, secret, code, container image ve staging DAST kapıları yoktur.

### 4.4 Migration, config ve rollback

- Çözümde beş design-time migration DbContext'i vardır; bunların dördü workflow'a elle
  yazılmıştır. Counterparty migration scripti üretilmez. Yeni veya eksik context'in
  delivery listesinden sessizce çıkmasını önleyen fail-closed discovery/ownership kontrolü
  yoktur.
- Scriptler üretilir fakat script artifactının boş ve önceki sürüm DB'ye uygulanması test
  edilmez.
- Migration history/schema sahipliği vardır; modüller arası uygulanma sırası ve release
  manifesti yoktur.
- CI SQL testi `sa` kullanır; production migration ve application least-privilege
  kimliklerini kanıtlamaz.
- Production uygulaması startup'ta migration çalıştırmaz; ayrı migration job/promotion
  yolu henüz yoktur.
- Config schema version, required/forbidden keys, secret reference, environment drift ve
  feature compatibility manifesti yoktur.
- Binary rollback ile schema rollback/forward-fix ayrımı ve tekrar başlatma/fencing
  davranışı kanıtlanmamıştır.

### 4.5 Production candidate ayrımı

`infra/docker-compose.yml` geliştirme ortamıdır:

- API `ASPNETCORE_ENVIRONMENT=Development` ile çalışır;
- web build'i `VITE_BOOTSTRAP_IDENTITY_ENABLED=true` build argümanını alır;
- SQL erişimi `sa` ile yapılır.

Bu compose build'inden çıkan web image'ı production artifactı veya production'a promote
edilecek build-once çıktı sayılamaz. Development bootstrap header/UI yolu production
candidate içinde bulunmamalı veya etkinleşememelidir. Production artifact/config sınırı
ve negatif kabul testi karar paketi kapanmadan yeni image/release workflow'u yazılmaz.

## 5. Amaç, kapsam ve kapsam dışı

### 5.1 ERP-000O'nun ilerideki dar amacı

ERP-000O aşağıdaki delivery foundation'ı kurmalıdır:

1. PR ve `main` için teknik olarak enforce edilen fail-closed quality gate.
2. Onaylı source SHA'dan bir kez üretilen immutable application artifactları.
3. Source, toolchain, dependency, base image, test, SBOM ve artifact digest provenance'ı.
4. Bütün DbContext'ler için sahipli, sıralı, hashli ve gerçek SQL'de doğrulanmış migration
   manifesti.
5. Secret içermeyen config schema ve environment-specific reference manifesti.
6. Binary, config, schema, feature flag, rollback ve forward-fix compatibility kanıtı.
7. Aynı digest'in staging'e ve daha sonra yetkili onayla production'a promote edilmesi.
8. Environment, identity, approval, audit, retention ve evidence sınırları.

### 5.2 Bu readiness çalışmasının dışında kalanlar

- `.github/**`, Dockerfile, compose, source, test, environment veya repository setting
  değiştirmek;
- CI/CD action, package, scanner, registry, signing/attestation aracı seçmek;
- cloud/on-prem runner, deployment target veya network topology seçmek;
- blue/green, canary, rolling, recreate veya başka deployment strategy seçmek;
- vulnerability, coverage, performance veya alarm threshold'u belirlemek;
- secret manager, OIDC issuer, signing key, certificate veya credential üretmek;
- branch protection planı satın almak, repository'yi public yapmak veya dış git platformu
  seçmek;
- release/tag/version formatını kilitlemek;
- production migration, deploy, rollback, backup veya restore çalıştırmak;
- mevcut acceptance satırını geçti/otomatik yapmak;
- yeni kanonik `OPEN-*`, `AT-*` veya `ADR-*` kimliği oluşturmak.

## 6. Bağımlılıklar ve DAG

| Bağımlılık | ERP-000O için zorunluluk |
|---|---|
| `ERP-000A` | Repository, solution, temel CI ve container omurgası |
| `ERP-000B` | CompanyId izolasyon regresyonunun yayın kapısı olması |
| `ERP-000C` | Identity/RBAC/ABAC ve production bootstrap yasağı |
| `ERP-000D` | Release/deployment operasyonunun audit/correlation sınırı |
| `ERP-000E` | Idempotency/concurrency ve migration/rollback güvenliği |
| `ERP-000M` | SQL Server PoC, DbContext migration ownership/order ve gerçek SQL kanıtı |

Manifest bağımlılıkları exact olarak `A/B/C/D/E/M`dir. Bu görev:

- ERP-000N'ye hard-depend etmez; O'nun artifact/config/migration/rollback kanıtı N'nin
  backup/restore ve compatibility girdisidir;
- ERP-000L'ye hard-depend etmez; L, N sonrası production telemetry/alert zincirini kurar;
- ERP-000F/G ve sonraki modülleri kendi içinde yeniden uygulamaz; onların PR'ları O'nun
  kabul edilmiş discovery/gate sözleşmesine yeni migration, contract ve test ekler.

Hedef yön `M → O → N → L` olarak döngüsüz korunur. ERP-000M readiness güncel `main`e
alınmıştır; ERP-000M implementation ve kabulü açık kapıları nedeniyle hâlâ `blocked`dır.
Bu PR yalnız ERP-000O satırının exact `A/B/C/D/E/M` bağımlılıklarını kurar. ERP-000N ve
ERP-000L satırları, kendi Draft PR'ları sırasıyla O ve N sonrasında güncel `main` ile
senkronize edilip insan review'undan geçene kadar değiştirilmez. Bu nedenle bütün zincirin
manifestte tamamlandığı henüz iddia edilmez. M ayrıca N'ye doğrudan recovery girdisi
sağlayabilir. Her adımda exact DAG yeniden doğrulanmalı; O'nun `depends_on` listesine N
veya L eklenmesi yasaktır.

## 7. `ERP-000O-DELIVERY-CONTRACT` karar paketi

Bu ad yeni kanonik decision/open/acceptance kimliği değildir. Aşağıdaki açık delivery
kararlarının tamamı insan review'uyla kapanana kadar task manifestin O'yu implementation
için seçmesini engelleyen task-local aggregate blocker'dır.

### 7.1 Repository enforcement ve review

Karar sahibi en az şunları belirlemelidir:

- private repository için teknik branch/ruleset enforcement çözümü;
- required check isimleri, exact commit/merge-ref doğrulaması ve stale run davranışı;
- direct push, force-push, branch deletion ve bypass politikası;
- review sayısı, code ownership, stale approval ve resolved conversation politikası;
- tek geliştiricili yapıda bağımsız insan/ChatGPT review kanıtının GitHub enforcement ile
  nasıl uzlaştırılacağı;
- governance, workflow, security, migration ve deployment dosyalarının sahipleri;
- emergency/hotfix yolu, süreli bypass, ikinci onay ve sonradan audit.

### 7.2 Artifact ve registry sınırı

Karar sahibi:

- application artifact türlerini ve ownership'ini;
- registry/artifact store, erişim ve retention modelini;
- version/tag/release kimliği ile commit/digest bağını;
- build-once ve environment'lar arası promote davranışını;
- artifact deletion/immutability ve legal hold sınırını;
- release evidence saklama ve erişim politikasını

seçmelidir. Bu belge provider veya format seçmez.

### 7.3 Supply-chain ve trust

Karar paketi:

- action allowlist ve immutable SHA pin politikasını;
- trusted release runner/executor kimliğini; image/OS release veya desteklenen immutable
  referansı; kritik build-tool envanterini; hosted/self-hosted trust, izolasyon, ephemeral
  cleanup ve cache sınırını;
- runner image/OS/tooling patch ve update review'unu, yeniden üretilebilirlik toleransını
  ve doğrulanamayan ya da onaysız drift halinde validation-only/fail-closed davranışını;
- SDK/tool/package/base-image lock ve güncelleme review'unu;
- SBOM ve license envanter formatını;
- source/build provenance ile signing/attestation trust root'unu;
- signing identity, key custody, rotation, revocation ve offline/disaster doğrulamasını;
- vulnerability severity, exception owner, gerekçe, expiry ve yeniden değerlendirmeyi;
- dependency, SAST, secret, code, container ve staging DAST gate'lerini

kilitlemelidir. Araç ve threshold bu readiness belgesinde seçilmez.

### 7.4 Migration, configuration ve rollback

Karar paketi:

- DbContext discovery, module owner, history schema/table ve canonical apply order'ı;
- migration artifact identity, hash, manifest ve compatibility aralığını;
- boş DB, önceki release DB ve production-benzeri hacimde gerçek SQL doğrulamasını;
- migration/application least-privilege identity ayrımını;
- lock, timeout, retry, resume, fencing ve partial-failure semantiğini;
- preflight, backup/recovery checkpoint ve post-migration verification'ı;
- destructive `Down` yerine binary rollback/expand-contract/forward-fix sınırını;
- config schema version, secret reference, drift ve forbidden-key politikasını;
- feature flag, old/new binary ve old/new schema compatibility matrisini

onaylamalıdır.

### 7.5 Environment ve production approval

Karar paketi:

- dev/test/staging/prod owner ve network sınırlarını;
- environment-specific service identity ve kısa ömürlü credential/federation modelini;
- deployment approval, görev ayrılığı ve break-glass davranışını;
- bakım penceresi, kullanıcı iletişimi ve rollback karar sahibini;
- staging smoke/security/performance/restore kanıtının production'a girişini;
- production activation öncesi `OPEN-013/015/017`, ERP-000N/L ve proje genel kabul
  kapılarını

tanımlamalıdır.

## 8. Güven sınırları

### 8.1 Untrusted pull request

PR kodu ve workflow değişikliği güvenilir kabul edilmez. PR validation:

- production/staging secret, signing key, registry write credential veya deployment token
  alamaz;
- write-capable environment'a deploy edemez;
- `pull_request_target` veya eşdeğer privileged context'te PR kaynak kodunu açıkça review
  edilmiş güvenli model olmadan çalıştıramaz;
- token permission'larını job bazında en az yetkiyle sınırlar;
- cache/artifact poisoning ve fork/branch identity karışmasına karşı source/ref/digest
  bağını doğrular;
- log, annotation veya test artifactına secret/PII yazmaz.

Bir PR'ın kendi required check tanımını silmesi veya yumuşatması, aynı PR'ın yeşil sonucu
ile meşrulaştırılamaz. Workflow/governance değişikliği ayrı insan review'u ister.

### 8.2 Trusted release

Release candidate yalnız onaylı `main` commit'inden veya kabul edilmiş immutable source
ref'inden üretilir. Build job:

- source SHA/tree ve clean checkout'u kaydeder;
- release artifactı bir kez üretir;
- her output için cryptographic digest üretir;
- dependency/base-image/toolchain kimliğini ve SBOM'u bağlar;
- test/migration/config/provenance manifestlerini aynı release kimliğine bağlar;
- signing/attestation işlemini PR secret'ından değil onaylı trusted identity'den yürütür;
- daha sonra environment için yeniden build etmez.

### 8.3 Deployment identity

Build, artifact publish, migration ve application runtime aynı credential/role değildir.
Migration identity:

- yalnız onaylı migration seti ve hedef environment için süreli yetki alır;
- application runtime'ın sahip olmaması gereken DDL yetkisini kalıcı taşımaz;
- başka environment veya tenant verisine erişim üretmez;
- actor, reason, source release, approval ve correlation kanıtı üretir.

## 9. Build-once immutable release zarfı

Kesin format ayrı karardır. Release zarfı en az şu semantik alanları taşıyabilmelidir:

- release/build kimliği ve schema version;
- source repository, commit SHA, tree/ref ve clean-checkout sonucu;
- workflow/run/job identity ve trusted/untrusted build sınıfı;
- runner/executor trust sınıfı ve kimliği; image/OS release veya immutable referansı;
  kritik ön-yüklü build-tool sürümleri ve substrate policy version;
- UTC build zamanı ve tekrar üretim girdileri;
- SDK, tool, package lock ve base-image digest referansları;
- backend/frontend/container veya diğer outputların ad, boyut ve digestleri;
- SBOM ve license inventory referansı;
- source/build provenance ve signing/attestation verification sonucu;
- test suite, gerçek SQL, security scan ve exception kanıt referansları;
- DbContext migration manifesti ve script digestleri;
- config schema ve secret içermeyen environment requirement referansı;
- feature/binary/schema compatibility ve rollback/forward-fix referansı;
- artifact store/registry immutable identity ve retention class;
- producer, reviewer, approver ve promotion audit/correlation referansı;
- başarı, kısmi, reddedilmiş veya doğrulanmamış sonuç.

Eksik, unknown schema'lı, digest'i tutmayan, unsigned/untrusted, revoked, downgrade edilmiş
veya başka source/environment'a ait zarf deploy edilebilir varsayılmaz. Manifestte secret,
token, connection string, müşteri/şirket verisi veya imza private key'i bulunmaz.

## 10. Migration delivery sözleşmesi

### 10.1 DbContext discovery ve sahiplik

Statik workflow satırları tek gerçeklik olamaz. Kabul edilen envanter:

- çözümde migration sahibi bütün DbContext'leri keşfeder veya exact allowlist ile doğrular;
- her context'in module owner, project, history schema/table ve migration dizinini taşır;
- yeni context/migration envantere eklenmediyse CI fail eder;
- başka modül history/table'ını sahiplenmeyi reddeder;
- cross-context dependency ve apply order'ı sürümlü manifestte açıklar.

### 10.2 Üretim ve doğrulama

Her release için migration artifactı:

- onaylı source SHA'dan idempotent/uygulanabilir biçimde üretilir;
- script/migration ID/order/hash ile release manifestine bağlanır;
- boş DB ve desteklenen önceki release DB üzerinde gerçek SQL Server'da uygulanır;
- ikinci uygulamada beklenen idempotent sonucu verir;
- pending/unexpected migration ve schema drift'i fail-closed reddeder;
- constraint, index, rowversion, trigger, permission ve tenant invariant regresyonlarını
  çalıştırır;
- production-benzeri veri/hacim ve lock riskini ERP-000M/OPEN-015 kanıtına bağlar.

### 10.3 Production uygulama ve hata

Production application startup otomatik DDL/migration çalıştırmaz. Ayrı migration adımı:

- preflight ve compatibility kontrolü yapar;
- onaylı backup/recovery checkpoint referansı alır;
- least-privilege migration identity kullanır;
- tek release/target için concurrency/fencing uygular;
- timeout/cancel/partial failure sonucunu açık kaydeder;
- tekrar başlatma ve forward-fix davranışını kanıtlar;
- post-migration history/schema/data/security smoke testini geçmeden application promote
  etmez.

Production verisi oluştuktan sonra audit, idempotency, outbox/inbox veya business tabloyu
düşüren `Down` scripti normal rollback sayılamaz. Binary rollback ancak eski binary yeni
schema ile uyumluysa yapılır; aksi durumda onaylı forward-fix/feature-disable uygulanır.

## 11. Configuration ve secret sözleşmesi

Artifact environment-neutral olmalı; secret artifact içine gömülmemelidir. Config zarfı:

- schema version ve application compatibility aralığı;
- required, optional ve forbidden key sınıfları;
- secret value yerine secret reference/identity;
- non-secret config'in canonical digest'i;
- environment, company/genel kapsam ve owner;
- feature flag version/default/fail-safe davranışı;
- config drift, unknown key ve stale secret sonucu;
- rotation/revocation ve rollback uyumluluğu

taşımalıdır.

Production candidate:

- `DevelopmentBootstrap` authentication ile başlatılamaz;
- frontend bootstrap identity flag/header yolunu etkin taşıyamaz;
- Development seed veya placeholder secret kullanamaz;
- production SSO/config eksikse fail-closed başlamaz;
- CORS/host/TLS/reverse-proxy ve environment kimliğini onaylı config'ten alır;
- config/secret'i log, artifact, SBOM veya client bundle'a yazmaz.

## 12. Supply-chain ve güvenlik kapıları

Kabul edilecek policy en az şunları kanıtlamalıdır:

1. Action/reusable workflow kaynakları exact immutable revision'a pinlidir ve allowlist
   dışı kaynak fail eder.
2. SDK, tool, NuGet/npm ve base image girdileri lock/digest ile tekrar doğrulanabilir.
3. Dependency graph ve base image için SBOM üretilir; release digest'ine bağlanır.
4. Dependency/license/vulnerability bulgusu policy'ye göre fail, istisna veya review
   sonucuna gider; sessiz yeşil olmaz.
5. İstisna owner, reason, scope, expiry ve yeniden değerlendirme taşır.
6. Secret scanning source, history, generated artifact ve container layer'larını kapsar.
7. SAST/code scan build source'unu; container scan final runtime image'ını tarar.
8. Staging DAST/integration güvenlik testi yalnız izinli hedef ve sentetik veride çalışır.
9. Scanner/policy servisi kullanılamazsa missing-data davranışı fail-closed veya açık
   insan review'udur; otomatik yeşil değildir.
10. Tool download ve update süreci kendisi provenance/hash doğrulamasına tabidir.

Kesin ürün, paket, threshold ve lisans istisnası bu belgede seçilmez.

## 13. Environment, promotion ve approval

### 13.1 Build once, promote same digest

- PR build'i doğrulama kanıtıdır; production release değildir.
- Onaylı `main` source'u trusted release artifactını bir kez üretir.
- Aynı immutable digest önce test/staging'e, sonra onayla production'a promote edilir.
- Environment'a göre source'u veya image'ı yeniden build etmek yasaktır.
- Promotion öncesi artifact/provenance/signature/config/migration compatibility yeniden
  doğrulanır.
- Yanlış environment, stale approval veya başka digest fail-closed reddedilir.

### 13.2 Staging

Staging kanıtı en az:

- clean deploy ve supported-upgrade deploy;
- config/secret reference validation;
- migration ve post-migration verification;
- API/web startup, liveness/readiness ve dependency health;
- CompanyId/RBAC/ABAC/audit/idempotency/concurrency regresyonu;
- browser/E2E/accessibility ve gerekli performance/security testleri;
- worker/connector disabled/degraded davranışı;
- rollback/forward-fix game-day

içermelidir. Hangi testlerin ERP-000L/N veya sonraki feature tasklarından geleceği açık
evidence matrix'te gösterilir; eksik task kanıtı uydurulmaz.

### 13.3 Production approval

Production environment:

- PR validation secret'larından ayrıdır;
- actor, approver, source release/digest, reason, window ve correlation taşır;
- least-privilege, süreli identity/federation kullanır;
- self-approval/bypass davranışını kabul edilmiş SoD politikasına göre reddeder;
- deploy ve migration sonuçlarını silinemez operasyon/security evidence'a bağlar;
- başarısız doğrulamada connector/user traffic açmaz.

ERP-000O delivery foundation tamamlanmış olsa bile production activation ancak:

- `OPEN-013` production SSO/MFA/dış erişim;
- `OPEN-015` onaylı hacim ve performance/SLO kapısı;
- `OPEN-017` kesin RPO/RTO/felaket topolojisi;
- ERP-000N gerçek backup/restore ve recovery kanıtı;
- ERP-000L production health/telemetry/alert/runbook kanıtı;
- bütün hedef modül ve kritik acceptance'lar;
- insan güvenlik/mimari/işletme onayı

kapanınca mümkün olabilir.

## 14. Rollback, forward-fix ve hotfix

Release sözleşmesi “önceki image'ı çalıştır” cümlesinden daha geniştir:

- old/new binary ile old/new schema/config/event compatibility matrisi;
- destructive veya irreversible migration sınıflandırması;
- expand/contract evreleri ve feature flag sırası;
- worker/connector/outbox replay ve duplicate external-effect güvenliği;
- rollback için artifact, config ve migration manifest digestleri;
- recovery checkpoint/backup-set referansı;
- partial deployment ve node version skew davranışı;
- başarısız rollback halinde forward-fix ve containment;
- kullanıcı trafiği/connector açma kapısı;
- owner, approval, elapsed time ve post-incident kanıtı

taşımalıdır.

Hotfix normal gate'i sessizce atlamaz. Daha kısa yol seçilecekse kapsam, süre, ikinci onay,
minimum test, security scan, rollback ve izleyen tam reconciliation açıkça tanımlanır.

## 15. Task-local kabul senaryoları

Aşağıdakiler yeni kanonik `AT-*` değildir. ERP-000O implementation kararları kabul
edildiğinde merkezi acceptance satırlarına insan review'uyla eşlenebilecek task-local
senaryolardır; bugün geçmiş sayılmaz.

### 15.1 Enforcement ve untrusted PR

- `ERP-000O-AC-001`: Required bir CI işi fail/cancel/in-progress iken merge teknik olarak
  reddedilir.
- `ERP-000O-AC-002`: Direct push, force-push, branch deletion veya yetkisiz bypass main'i
  değiştiremez.
- `ERP-000O-AC-003`: Eski commit'in yeşil sonucu yeni head/merge ref için kullanılamaz.
- `ERP-000O-AC-004`: Workflow/governance/gate'i silen PR kendi zayıflatılmış sonucu ile
  merge edilemez; ayrı sahipli review gerekir.
- `ERP-000O-AC-005`: Untrusted PR production/staging secret, signing key, registry write
  veya deployment token okuyamaz.
- `ERP-000O-AC-006`: Malicious PR log, cache, artifact veya workflow output üzerinden
  secret/external credential elde edemez.
- `ERP-000O-AC-007`: Job token permissions exact görev dışında write/bypass sağlamaz.

### 15.2 Build, artifact ve provenance

- `ERP-000O-AC-008`: Trusted release artifactı onaylı source SHA/tree ile birebir bağlıdır.
- `ERP-000O-AC-009`: Staging ve production aynı immutable digest'i kullanır; environment
  için rebuild reddedilir.
- `ERP-000O-AC-010`: Production candidate Development bootstrap auth/header/seed veya
  placeholder secret ile build/start edilemez.
- `ERP-000O-AC-011`: Backend, frontend, image, migration, config schema, SBOM ve test
  evidence tek versioned release manifestinde hashli referans taşır.
- `ERP-000O-AC-012`: Artifact, manifest, SBOM, provenance veya attestation içindeki tek
  byte değişiklik verification'ı fail eder.
- `ERP-000O-AC-013`: Missing, revoked, wrong-repository, wrong-run veya downgrade provenance
  deploy edilebilir sayılmaz.
- `ERP-000O-AC-014`: Artifactlar onaylı store'a yüklenir ve retention süresince digest ile
  yeniden indirilebilir/doğrulanabilir.

### 15.3 Supply chain ve security

- `ERP-000O-AC-015`: Allowlist dışı veya mutable/unpinned action/reusable workflow CI'ı
  fail eder.
- `ERP-000O-AC-016`: Runner/executor kimliği, image/OS release'i, trust/isolation veya
  kritik build-tool envanteri doğrulanamıyor ya da onaylı substrate'tan drift ediyorsa;
  ayrıca lock/digest dışı SDK, package veya base image varsa trusted release fail eder.
- `ERP-000O-AC-017`: SBOM final artifact/image içeriği ve dependency graph ile mutabıktır.
- `ERP-000O-AC-018`: Dependency, license, SAST, secret, code ve container bulguları onaylı
  policy sonucuna bağlanmadan yeşil sayılmaz.
- `ERP-000O-AC-019`: Security istisnası owner/reason/expiry taşır; scope dışı veya süresi
  dolmuş istisna uygulanmaz.
- `ERP-000O-AC-020`: Scanner/policy servisi unavailable/missing-data olduğunda sonuç sessiz
  yeşil olmaz.

### 15.4 Migration ve configuration

- `ERP-000O-AC-021`: Yeni DbContext/migration exact inventory'de yoksa workflow fail eder.
- `ERP-000O-AC-022`: Her context'in owner, history schema/table, apply order, migration ID
  ve script hash'i release manifestinde bulunur.
- `ERP-000O-AC-023`: Onaylı migration artifactı gerçek SQL Server'da empty ve supported
  previous-version DB'ye uygulanır; ikinci uygulama beklenen idempotent sonucu verir.
- `ERP-000O-AC-024`: Migration identity application runtime'dan ayrıdır; fazla DDL/data
  permission ile çalışma security testinde reddedilir.
- `ERP-000O-AC-025`: Timeout/partial failure sonrası history/schema drift görünürdür;
  tekrar/forward-fix kanıtlanmadan application promote edilmez.
- `ERP-000O-AC-026`: Production application startup otomatik migration/DDL çalıştıramaz.
- `ERP-000O-AC-027`: Config schema unknown/missing/forbidden key, wrong environment veya
  stale secret reference ile fail-closed davranır.
- `ERP-000O-AC-028`: Secret value source, client bundle, config digest, artifact, SBOM veya
  log içinde bulunmaz.

### 15.5 Promotion, approval ve recovery

- `ERP-000O-AC-029`: Staging clean/upgrade deploy, smoke, security ve tenant regresyonları
  aynı release digest'i üzerinde geçer.
- `ERP-000O-AC-030`: Yetkisiz/self-approved/stale approval production promote edemez.
- `ERP-000O-AC-031`: Binary rollback yalnız schema/config compatibility kanıtı varsa
  çalışır; destructive Down normal rollback olarak kullanılmaz.
- `ERP-000O-AC-032`: Migration sonrası eski binary uyumsuzsa onaylı forward-fix veya feature
  containment çalışır ve veri kaybı üretmez.
- `ERP-000O-AC-033`: Rollback/forward-fix game-day artifact, config, migration, owner,
  elapsed time ve evidence ile tekrar edilebilir.
- `ERP-000O-AC-034`: `OPEN-013/015/017`, ERP-000N/L veya insan approval açıkken production
  activation fail-closed kalır.
- `ERP-000O-AC-035`: Release/deploy/migration operasyonu actor, approver, reason, source
  digest ve correlation ile dayanıklı evidence üretir.
- `ERP-000O-AC-036`: Missing artifact/evidence, yalnız başarılı workflow loguyla telafi
  edilmez ve görev accepted/done yapılmaz.

## 16. Test ve kanıt stratejisi

### 16.1 Static/contract testleri

- workflow schema, permission, event ve dangerous-trigger denetimi;
- action/reusable workflow allowlist ve immutable revision denetimi;
- runner/executor image-OS-tool identity, trust/isolation, ephemeral cleanup, cache ve
  onaylı substrate drift denetimi;
- Docker/base-image digest ve package/tool lock denetimi;
- DbContext/migration inventory ve module ownership denetimi;
- release/config/migration/SBOM/provenance schema contract testleri;
- DAG ve task manifest dependency testi;
- forbidden Development bootstrap ve secret pattern testleri.

### 16.2 Adversarial CI testleri

- required job fail/cancel/timeout ve stale result;
- malicious PR secret/log/cache/artifact exfiltration fixture'ı;
- workflow gate silme/yumuşatma ve unauthorized action ekleme;
- artifact/manifest/SBOM/provenance mutation, omission ve downgrade;
- wrong repository/ref/run/environment/digest promotion;
- expired security exception ve missing scanner result;
- direct push/force-push/bypass ve self-approval denemesi.

Gerçek production secret kullanılmaz. Fixture'lar sentetik ve etkisiz credential kullanır.

### 16.3 Gerçek SQL ve migration

- empty DB ile supported previous release DB;
- bütün context/history/order doğrulaması;
- exact generated artifact scriptinin uygulanması;
- least-privilege migration identity ve application identity negative testleri;
- lock/timeout/parallel deploy/partial failure/fencing;
- idempotent retry, pending migration ve schema drift;
- CompanyId/RBAC/ABAC/audit/idempotency/concurrency post-migration regresyonu;
- rollback compatibility ve forward-fix provası.

EF `MigrateAsync()` ile test DB oluşturmak değerlidir fakat release'te yüklenen exact SQL
artifactının uygulanması kanıtının yerine geçmez.

### 16.4 Artifact ve environment

- build output digest ve SBOM reconciliation;
- attestation/trust verification;
- artifact upload/download/retention ve permission;
- staging deploy/smoke/health/security;
- same-digest promotion;
- config/secret reference ve drift;
- rollback/forward-fix game-day;
- production gate'in açık blocker'larda kapalı kalması.

## 17. Evidence matrisi

Her kabul sonucu en az şu bağları taşımalıdır:

- source SHA/tree ve PR/main event;
- runner/executor identity, image/OS release, kritik build-tool envanteri, trust/isolation
  ve substrate policy version;
- workflow/run/job ve exact test suite;
- immutable artifact/release digest;
- migration/config/SBOM/provenance manifest digestleri;
- environment ve deployment/migration identity;
- reviewer/approver ve karar gerekçesi;
- test zamanı, tool/policy version ve sonuç;
- exception varsa owner/expiry;
- rollback/forward-fix/restore referansı;
- kalıcı evidence location ve retention class.

Yalnız ekran görüntüsü, terminal özeti, PR yorumu veya geçici workflow logu release artifact
kanıtı değildir. Evidence store erişimi least-privilege olmalı; silme/retention ayrı
yetkili ve auditli olmalıdır.

## 18. Implementation giriş kapısı

### 18.1 Şimdi izinli

- mevcut workflow, repository setting ve delivery gap'lerini read-only envanterlemek;
- fiziksel ADR-010 için karar soru seti/evidence şablonu hazırlamak;
- OPEN-014 altyapı/env envanteri toplamak;
- artifact/migration/config/provenance schema taslağını insan review'una sunmak;
- branch enforcement, approval ve single-developer review seçeneklerini karar sahibine
  sunmak;
- task-local acceptance/evidence matrisi hazırlamak;
- güncel main ile docs-only manifest conflict'ini güvenli uzlaştırmak.

### 18.2 Şimdi yasak

- `.github/**`, Dockerfile, compose, source, test veya infra değiştirmek;
- repository setting, branch/ruleset, environment, secret veya permission değiştirmek;
- action, package, scanner, registry, deployment provider veya runner eklemek;
- signing key/certificate/OIDC trust veya production credential oluşturmak;
- threshold, coverage, SLO, retention veya security exception policy uydurmak;
- migration/deploy/rollback scripti çalıştırmak;
- Development compose image'ını production artifactı saymak;
- artifact olmadan workflow logunu release kanıtı ilan etmek;
- task/acceptance/ADR/open decision durumunu kapatmak;
- bütün dependencies ve decision contract kapanmadan implementation başlatmak.

## 19. Definition of Ready

ERP-000O implementation ancak:

- A/B/C/D/E/M kabul edilip `main`e alınmış;
- fiziksel ADR-010 oluşturulmuş, insan review'uyla kabul edilmiş ve register ile tutarlı;
- OPEN-014 gerçek altyapı/environment envanteri kapanmış;
- `ERP-000O-DELIVERY-CONTRACT` içindeki enforcement, artifact, provenance, supply-chain,
  runner/executor trust, migration, config, rollback, environment ve approval kararları
  kapanmış;
- private repository için teknik required-check enforcement yolu yürütülebilir;
- provider/tool/registry/secret/trust kararları güncel resmi dokümantasyon, lisans, güvenlik
  ve TCO review'undan geçmiş;
- task-local acceptance/evidence planı onaylanmış;
- P0/P1 mimari ve güvenlik bulgusu kalmamış

ise yeniden GO değerlendirmesi alabilir.

Bugünkü durum: koşullar karşılanmıyor; implementation **NO-GO**.

## 20. Definition of Done

ERP-000O delivery foundation ancak:

- protected main required check'leri gerçek teknik enforcement ile uyguluyor;
- untrusted PR secret/deployment sınırı adversarial testlerden geçiyor;
- trusted release runner/executor substrate'ı onaylı kimlik, image/OS/tool envanteri,
  isolation/ephemeral sınırı ve drift kontrolüyle doğrulanıyor;
- bütün current DbContext migration'ları discover/order/hash/real-SQL/least-privilege
  kanıtına sahip;
- trusted main build'i immutable artifact, migration/config manifesti, SBOM, provenance ve
  doğrulanmış signing/attestation üretiyor;
- artifactlar onaylı store'a yükleniyor ve retention içinde yeniden doğrulanabiliyor;
- staging aynı digest'i deploy edip smoke/security/tenant/migration ve rollback/forward-fix
  provasını geçiyor;
- production bootstrap yolu negatif testle kapalı;
- environment approval/SoD ve dayanıklı deployment evidence çalışıyor;
- task-local acceptance'lar evidence ile geçmiş;
- P0/P1 açık kusur kalmamış;
- kritik workflow/security/deployment diff'i insan review'uyla kabul edilmiş

ise `done` olabilir.

ERP-000O'nun `done` olması production go-live demek değildir. `OPEN-013/015/017`, ERP-000N,
ERP-000L, hedef modüller ve insan production approval kapanmadan production activation
teknik olarak kapalı kalmalıdır.

## 21. Shared manifest ve merge kapısı

Bu dal `main@c1d28cc44e775cfea3a528cfd755cbe85b5ee290` üzerine temiz olarak yeniden
kurulmuştur. Güncel PR deltası yalnız:

- `docs/features/ERP-000O_CI_CD_DELIVERY_READINESS.md`,
- `docs/backlog/task_manifest.json` içindeki ERP-000O satırı,
- `docs/delivery/AUTONOMOUS_PROGRESS.md` içindeki bu senkronizasyonun kanıt kaydı

değiştirir.

Aynı manifest başka readiness ve teslim dallarında da değişebilir. ERP-000E'nin `done`,
ERP-000M'nin güncel `blocked` readiness satırı ve ERP-000O dışındaki bütün task objeleri bu
dalda güncel `main` ile aynen korunur. Güvenli merge kuralı:

1. Kabul edilmiş sıra korunur: ERP-000M readiness `main`dedir; sıradaki kapılar
   ERP-000O → ERP-000N → ERP-000L, ardından bağımsız ERP-000J/ERP-000K'dir.
2. Her manifest merge'inden sonra bu dal güncel `main` ile sync edilir.
3. Yalnız ERP-000O satırının kasıtlı deltası yeniden üretilir.
4. Başka görev status/dependency/blocker değeri otomatik veya sezgisel değiştirilmez.
5. Draft/spec varlığı `accepted/done` sayılmaz; GitHub merge ve delivery evidence yeniden
   doğrulanır.
6. DAG `M → O → N → L` olarak kontrol edilir; M readiness'in `main`de olması M
   implementation'ını açmaz ve O'ya N/L hard dependency eklenmez.
7. Conflict veya semantik fark sessiz çözülmez; insan review'una çıkarılır.
8. Task manifest, CI/governance ve delivery kontrol yüzeyi olduğu için PR Draft ve insan
   review'unda kalır; otomatik merge edilmez.

Bu docs-only merge implementation veya production izni vermez.

## 22. Sonuç

ERP-000O için sonuç:

- **Readiness/specification: GO**
- **Workflow, source, test, Docker, infra ve repository setting değişikliği: NO-GO**
- **Production deployment/activation: NO-GO**
- **Yeni kanonik OPEN/AT/ADR: oluşturulmadı**
- **Task durumu: blocked**

Mevcut CI korunması gereken gerçek bir test omurgasıdır; fakat branch enforcement,
immutable uploaded artifact, migration/config/rollback manifesti, SBOM/provenance/trust,
supply-chain policy, least-privilege environment ve gerçek promotion/rollback kanıtı
olmadan yayın zinciri sayılmaz. Açık kararlar tahmin edilmeyecek ve production activation
fail-closed kalacaktır.
