# AGENTS.md — Göymen ERP Repository Kuralları

## Kaynakların önceliği

1. `docs/decisions/KARAR_SICILI.md`
2. `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`
3. İlgili feature spec ve ADR
4. Eski StokERP yalnız referans/golden kaynak

Çelişkide daha yeni kullanıcı kararı geçerlidir. Açık işletme politikasını tahmin ederek kilitleme; yapılandırılabilir bırak ve `OPEN-*` kimliğiyle işaretle.

## Çalışma orkestrasyonu

Her yeni çalışma döngüsünde `docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`
varsa okunur ve görev/branch/PR/doğrulama akışında uygulanır. Bu protokol yukarıdaki kaynak
önceliğini veya iş kurallarını değiştirmez ve kendi başına dış yazma ya da merge yetkisi
vermez.

**Governance/süreç dosyalarının merge'i (22 Ekim 2026 kullanıcı kararı; önceki "insan review
kapısında kalır" kuralının yerine geçer):** `AGENTS.md`, `docs/governance/**`,
`docs/decisions/**`, `.claude/**` ve `.github/**` kalite kapısı değişiklikleri, aşağıdaki
şartlarla **`CODEX-ORCHESTRATOR` tarafından merge edilebilir**; Sinan'ın tıkı beklenmez
(25 Ekim 2026 istisnası: Claude şeridi kendi açtığı `claude/*` governance PR'ını aynı iki
review ve kanıt şartıyla merge edebilir — `docs/decisions/SINAN_KARARLARI_2026-10-25_MERGE_YETKISI.md`):

- Yazar ≠ koordinatör; iki bağımsız exact-head review; açık P0/P1 yok.
- Yalnız doküman/governance/karar/ayar dosyası (`docs/**`, `AGENTS.md`, `.claude/**`)
  değiştiren PR'da **CI koşumu şartı aranmaz**; `git diff --check` + iki review yeter.
- `docs/decisions/**`, `docs/backlog/**` veya `.github/**` değiştiren adaylarda merge
  öncesi `tools/validate_delivery_backlog.py`, `tests/delivery_backlog_validator/`
  (bağımsız sicil MD/CSV eşliği dahil) ve `tests/dotnet_bootstrap_integrity/`
  hafif kontrolleri yerelde çalıştırılır; komut, exit code ve kaynak commit'i PR'a
  kaydedilir. Kaynak değişirse bu kanıt yenilenir. Sicil hash'i yalnız mevcut
  `tests/delivery_backlog_validator/recompute_delivery_backlog_provenance.py`
  üreticisinin çıktısından ve onaylı kaynak değişikliği incelendikten sonra güncellenir.
  Bu daraltma test veya uygulama dosyası içeren adayın normal fresh CI şartını kaldırmaz.
- `.github/**` değişikliğinde iki review ayrıca şunu doğrular: her `runs-on` GitHub-hosted
  (`ubuntu-latest`/`windows-latest`) etikettir, self-hosted etiket yoktur, artifact yalnız
  secret içermeyen CI kanıtıdır (saklama ≤ 7 gün); cache/Packages yüklemesi, secret,
  environment veya deploy yüzeyi açılmamıştır (`SINAN_KARARLARI_2026-10-26_PUBLIC_CI.md`).
- Merge sonrası Issue #66'ya tek satır makbuz (PR, merge SHA, iki review bağlantısı).
- Sinan bu yetkiyi Issue #66'ya yazacağı tek satırla geri alabilir.

**Değişmeyen insan kapıları:** deployment, environment, secret, workflow'ların Settings →
Actions'ta etkinleştirilmesi, grant/rol/Critical, production aktivasyonu ve `OPEN-*`
kapatma her zaman somut kullanıcı kararı ister.

`docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md` merge edilmişse ayrıca okunur.

`docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md` merge edilmişse **önce** okunur ve
uygulanır. v3, v2'nin **rol dağılımını, dilim sırasını ve tören seviyesini** günceller;
güvenlik değişmezleri (bu dosyanın "Mimari değişmezler" ve "Yasaklar" bölümleri,
`KARAR_SICILI.md` önceliği, production aktivasyonunun insanda kalması) aynen korunur.
Çelişkide v3 süreç tarafında, v2 güvenlik tarafında üstündür.

`docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v4.md` merge edilmişse v3'ten **önce** okunur ve
uygulanır. v4 (2026-10-21 kullanıcı kararı §2), v3'ün tören seviyesini ve karar akışını
günceller (dalga START-GO, review dondurma + K1/K2 delta review `DELTA-OK`, P8 `DELEGE-*`
delegasyonu, yorum disiplini); güvenlik değişmezleri, K3 tanımı ve K3'teki iki bağımsız
exact-head review aynen korunur. K1/K2'de dondurulmuş `REVIEW-READY <sha>` üzerindeki tam
karşı-ajan review + merge edilecek son head için `DELTA-OK <sha>` imzası "exact-head
bağımsız review" şartını sağlar. Çelişkide v4 süreç tarafında, v2/v3 güvenlik tarafında
üstündür.

- Pre-production PR'ı tek karşı-ajan review + fresh zorunlu CI ile coordinator merge edilir
  (Claude şeridinin kendi PR'ları için 25 Ekim 2026 istisnası aşağıda; review yorumu
  ASSUMPTION-MERGE-1 için bkz. `docs/decisions/SINAN_KARARLARI_2026-10-25_MERGE_YETKISI.md`).
  Yalnız doküman/governance/ayar dosyası değiştiren PR'da fresh CI şartı yoktur (yukarıdaki
  22 Ekim kuralı).
- İki bağımsız exact-head review yalnız **K3**'te gerekir; K3 kapsamı dardır: DB
  migration/constraint · para/formül/tolerans · permission/rol · cross-module contract ·
  `.github/**` · production activation. Diğer her şey K1/K2.
- Karara bağlanmamış nokta: ajan **durmaz**; Issue #66'ya `NEEDS-SINAN: <soru>` tek satır
  yazar ve sıradaki dilime geçer. Geri alınabilir teknik/UX ayarı ChatGPT hakemliğiyle
  `ASSUMPTION-*` olabilir; para/stok/üretim/formül/yetki/güvenlik/migration/saklama
  politikası tahminle etkinleştirilemez.
- Şeritler (v3 §6): **Codex** = backend + endpoint + gerçek-SQL + OpenAPI + formül
  calculator. **Claude** = tüm frontend / React shell / ekranlar / generated-client
  tüketimi / browser-a11y. **ChatGPT** = iş spec + acceptance + formül golden + backlog +
  hakemlik (tam-zamanında, bir dilim önde). **`CODEX-ORCHESTRATOR`** = merge (Claude
  şeridi kendi PR'larını da merge edebilir; 25 Ekim 2026 kararı). Üç ajan
  aynı dikey dilimin farklı katmanında çalışır.

En güncel kullanıcı kararları için `docs/decisions/` içindeki tarihli `SINAN_KARARLARI_*.md`
dosyaları `KARAR_SICILI.md`'den önce, "son kullanıcı kararı" seviyesinde geçerlidir.

31 Ağustos 2026 tarihli kullanıcı kararı, geliştirme PR'ları için aşağıdaki dar ve geri
alınabilir standing merge yetkisini verir:

- Şerit yazarı kendi PR'ını merge etmez; merge'i ayrı `CODEX-ORCHESTRATOR` koordinatörü
  expected-head ile yapar. **İstisna (25 Ekim 2026 kullanıcı kararı,
  `docs/decisions/SINAN_KARARLARI_2026-10-25_MERGE_YETKISI.md`):** Claude şeridi yalnız
  kendi açtığı `claude/*` PR'larını, bu bölümdeki ve 22 Ekim kuralındaki bütün kapılarla
  (fresh CI, exact-head bağımsız review, yeni davranış testi, K3 ve governance'ta iki
  review, açık P0/P1 yok, `.claude/settings.json` için Sinan'ın #66 onayı)
  `CODEX-ORCHESTRATOR`'ı beklemeden squash merge edebilir ve merge sonrası Issue #66
  makbuzuyla `CODEX-ORCHESTRATOR`'ı bilgilendirir. İnsan kapıları değişmez.
- K1/K2 implementation PR'ı ancak fresh zorunlu kontroller, exact-head bağımsız review,
  yeni davranış testi ve açık P0/P1 olmaması halinde coordinator tarafından squash merge
  edilebilir.
- K3 implementation PR'ı ancak kilitli kararları uygularsa, açık işletme politikasını
  seçmiyorsa, iki bağımsız exact-head review ve ilgili gerçek SQL/browser/failure kanıtı
  varsa coordinator tarafından merge edilebilir.
- Bu yetki `OPEN-*` kapatma, yeni rol/formül/eşik/para/stok/üretim politikası seçme,
  karar/master/task durumunu sessiz değiştirme, secret/deployment veya production
  aktivasyonu yetkisi değildir.
- `.github/**` içinde yalnız build/test/contract/security kalite kapısı değişikliği aynı
  K3 kanıtıyla merge edilebilir (22 Ekim 2026 kuralı: CI koşumu olmadan da, iki review'un
  GitHub-hosted/secret/deploy doğrulamasıyla). Deployment, environment, secret ve production
  yüzeyi her zaman somut kullanıcı kararı ister.

GitHub teknik koruması kullanılamıyorsa lane self-merge kapalı kalır (tek istisna: yukarıdaki
25 Ekim 2026 Claude şeridi kararı); coordinator — ve bu istisnada Claude şeridi — aynı
kapıları prosedürel olarak doğrular ve kanıtı GitHub'a yazmadan merge yapmaz.

**Merge öncesi main tazelik kontrolü (Faz 0; 7 Kasım 2026 kullanıcı kararı GOV-D2/GOV-D3'e dayanır, kapsam ve Claude şeridine uygulanması uygulama ayrıntısıdır,
`docs/decisions/SINAN_KARARLARI_2026-11-07_MUHASEBE_FISI_VE_RECOVERY.md`):** `src/backend/`,
bir migration'a, kök build-control dosyalarına (`tools/generate_recovery_surface_inventory.py` içindeki `REPOSITORY_BUILD_CONTROL_PATHS`: `Directory.Build.*`, `Directory.Packages.*`, `NuGet.Config`, `global.json`, `.editorconfig`/`.globalconfig`, `src/.editorconfig` vb.) veya recovery envanterine (`tools/generate_recovery_surface_inventory.py`,
`contracts/recovery/**`, `tests/recovery_surface_inventory/**`, `tests/release_manifest/**`)
dokunan PR'ı merge eden herkes (`CODEX-ORCHESTRATOR` ve Claude şeridi) expected-head squash'tan
hemen önce `git fetch origin main <pr-branch> && git merge-base --is-ancestor origin/main <expected-head>`
çalıştırır; kullandığı main SHA'sını ve exit code'u merge makbuzuna yazar. Fresh zorunlu CI aynı
head'de yeşil olmalıdır. Exit ≠ 0 ise üç kademeden biri uygulanır; önce (c) denenir (GOV-D3 kapsam
mantığı, #976 ölçümü; (c) için GOV-D5, `docs/decisions/SINAN_KARARLARI_2026-11-12_CI_KUYRUK.md`).
**(c)** `main` yalnız CI'ın hiç başlamadığı düzyazıda (CIH-D5 listesi) ilerlediyse merge edilebilir:
mekanik merge commit'i, yeni CI koşusu ve `DELTA-OK` aranmaz; aynı head'deki fresh yeşil CI ve
review'lar geçerli kalır. Depo kökünde çalıştırılır; iki komut da exit 0 vermelidir:
```
SKIPPED=(':(exclude,glob)docs/decisions/**/*.md' ':(exclude,glob)docs/delivery/**/*.md' \
  ':(exclude,glob)docs/handoffs/**/*.md' ':(exclude,glob)docs/operations/**/*.md')
READ_BY_CI=(docs/decisions/KARAR_SICILI.md docs/decisions/ACIK_KARARLAR.md \
  docs/decisions/SINAN_KARARLARI_2026-08-31.md docs/delivery/DECISION_PACK.md)
MB="$(git merge-base origin/main <expected-head>)"
git diff --quiet "$MB" origin/main -- . "${SKIPPED[@]}" && git diff --quiet "$MB" origin/main -- "${READ_BY_CI[@]}"
```
İki liste `ci.yml` yol filtresiyle aynıdır (`tests/ci_safe_trx_summary/test_workflow_path_filters.py`
eşliği sabitler). Makbuza main SHA'sı, `(c)`, iki komut ve exit code'ları yazılır. Komutlardan biri
exit ≠ 0 verirse (c) uygulanmaz, (a)/(b)'ye geçilir. (a)/(b) kapsamı, yeniden üretilen pin'lerin
girdileridir:
```
SCOPE=(src/backend/ .editorconfig .globalconfig Directory.Build.props Directory.Build.targets Directory.Build.rsp \
  Directory.Packages.props Directory.Packages.targets Directory.Solution.props Directory.Solution.targets \
  MSBuild.rsp NuGet.Config global.json src/.editorconfig src/.globalconfig src/Directory.Build.props \
  src/Directory.Build.targets src/Directory.Packages.props src/Directory.Packages.targets \
  src/Directory.Solution.props src/Directory.Solution.targets src/MSBuild.rsp src/NuGet.Config src/global.json \
  tools/generate_recovery_surface_inventory.py contracts/recovery/ tests/recovery_surface_inventory/ \
  tests/release_manifest/ contracts/openapi/ 'tests/GoymenERP.*' tools/relational_inventory/)
git diff --quiet "$(git merge-base origin/main <expected-head>)" origin/main -- "${SCOPE[@]}"
```
**(a)** exit ≠ 0 ise main kapsamın içinde ilerlemiştir: main PR'a merge edilir, pin'ler yeniden
üretilir, CI yeniden koşar (K3'te GOV-D1'e göre iki review). **(b)** exit 0 ise main yalnız kapsam
dışında (docs, frontend…) ilerlemiştir: re-pin beklenmez; CI kırmızı çıkarsa (a)'ya (`main-merge-resync`)
geçilir. CI birleşik ağacı test ettiğinden taze merge ref gerekir. Taze merge ref'in tek yolu **boş
olmayan mekanik merge commit'idir**: origin/main PR dalına çakışmasız merge edilir; boş commit,
"re-run jobs" ve kapat-aç yasaktır. Yeni head'de zorunlu CI yeşil olmadan merge yoktur. `DELTA-OK`
kanıtı: `git rev-parse <yeni-head>^1` = `<eski-head>` ve `git diff <eski-head> <yeni-head>` çıktısı
`git diff $(git merge-base origin/main <eski-head>) origin/main` çıktısıyla aynıdır (çakışma
çözümü gerekirse eşitlik bozulur, (b) dışına çıkılır). Bağımsız reviewer imzalar ve `--stat` ile
sınıfı yazar; yazar veya merge eden kendi merge'üne `DELTA-OK` veremez. K3'te iki exact-head review
şartı aynen sürer, (b) yalnız mekanik delta'yı kapsar. Makbuza main SHA'sı, (a)/(b) kararı ve yeni
yeşil CI run kimliği yazılır ((c)'de yeni koşu yoktur; head'deki yeşil koşunun kimliği yazılır). Squash'taki `sha=` parametresi yalnız
head'i doğrular, base'i doğrulamaz; kontrol ile merge çağrısı arasında saniyelik bir yarış kalır.
Merge eden, çağrıdan önce main ilerlediyse kontrolü yineler. GitHub "Require branches to be up to
date" ayarı şimdilik kapalıdır (GOV-D3); açılması insan kapısıdır.

## Mimari değişmezler

- Modüler monolit; modül sınırları kod, şema ve servis sözleşmesinde korunur.
- Her kritik kayıtta `CompanyId`; şirket filtresi yalnız UI değildir.
- Bir modül başka modül tablosuna doğrudan yazmaz.
- Kritik commands: authorization, expected version, idempotency key, correlation/causation, audit.
- Dış etkiler transactional outbox; consumers inbox/dedup kullanır.
- Fiziksel/ticari/finansal olay bir kez kaydedilir; kopya hareket üretme.
- Silme yerine pasif, iptal veya ters kayıt; retention/imha ayrı politikadır.
- AI doğrudan DB veya ham SQL kullanamaz; yalnız şemalı backend tools.
- Formula/UI/report aynı deterministic backend calculator’ı kullanır.

## Stack başlangıcı

- Backend: C# / ASP.NET Core / .NET 10
- Frontend: React + TypeScript
- DB PoC: SQL Server; EF Core
- Files: S3-compatible object storage abstraction
- Tests: unit, integration, contract, E2E, security and restore

## Kodlama kuralları

- Kod ve teknik isimler İngilizce; kullanıcı metinleri Türkçe ve kaynak dosyalı/localizable.
- Nullable/units/money/date semantics açık türlerle temsil edilir.
- Money, Quantity, Unit, ExchangeRate, BusinessDate gibi value objects kullan.
- Magic number, hard-coded tolerance, role name veya CompanyId kullanma.
- Migration geri alınabilir veya açık forward-fix planlı olmalı.
- Secrets commit edilmez; `.env.example` yalnız placeholder içerir.
- Her public API ve event sürümlü contract testine sahip olmalıdır.

## PR kabul kapısı

- İlgili karar/feature spec referansı
- CompanyId ve yetki testleri
- Audit/idempotency/concurrency etkisi
- Error/empty/loading/conflict UI durumları
- Migration ve rollback etkisi
- Log/metric/trace
- Unit + integration + gerektiğinde E2E
- P0/P1 açık hata yok

## Yasaklar

- StokERP kodunu kör kopyalama
- Ham SQL’i AI aracına açma
- UI doğrulamasını tek güvenlik olarak kullanma
- Fiyat/formül/toleransı ekran içinde yeniden yazma
- Aynı para/stok olayını ikinci hareket olarak üretme
- Açık kararları sessiz varsayımla production’a alma
