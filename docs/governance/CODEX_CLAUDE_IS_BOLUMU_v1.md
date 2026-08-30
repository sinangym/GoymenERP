# Göymen ERP — Codex / Claude Çift Ajan İş Bölümü Protokolü v1.1

- Durum: **KULLANICI ONAYI BEKLİYOR (proposal-only)**
- Hedef konum: `docs/governance/CODEX_CLAUDE_IS_BOLUMU_v1.md`
- İlişki: `docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md` protokolünü **daraltır; yerine geçmez**. Her iki ajan iki dosyayı da okur.
- Kaynak önceliği değişmez: `docs/decisions/KARAR_SICILI.md` > `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` > feature spec/ADR > StokERP yalnız referans.
- Bu belge yeni iş kuralı, permission, production aktivasyonu veya merge yetkisi üretmez. `AGENTS.md` mimari değişmezleri ve PR kabul kapısı aynen geçerlidir.

Bu sürüm, v1.0 taslağının iki yapısal hatasını düzeltir:

1. API endpoint sahipliği Claude'a verilmez; endpoint backend sahibiyle atomik kalır.
2. `ERP-000K` doğrudan `blocked → ready` yapılmaz; yerine `SHELL-PILOT-01` geliştirme dilimi açılır.

Ek olarak üç küçük düzeltme içerir (bkz. bölüm 8 K3 listesi — browser test paketi; bölüm 9 — ChatGPT-BUSINESS review yükü; bölüm 11.1 — repo sertleştirme önkoşulu).

---

## 0. Canlı durum notu — normatif değildir

Bu bölüm yalnız protokol hazırlanırken görülen snapshot'tır; her döngü başında canlı GitHub gerçeği `git fetch --all --prune` ile yeniden çözülür. Snapshot değerleri governance içine kalıcı gerçek olarak gömülmez.

- İncelenen `main`: `871101455dc2ba343d1c94982bfe9d1fa547411b` (fast-forward sonrası doğrulandı, 2026-08-31)
- `main` üzerindeki son görünür sözleşme taşıyıcısı: PR `#109`, Cari UI contract (iki dosyalık kabul taşıyıcısı; üretim arayüzü değil).
- Açık PR sayısı: **14** (çoğu Draft ve eski `main` tabanına bağlı).
- `ERP-000A…E`: `done`
- `ERP-000F/G/H/I`: `ready`
- `ERP-000J/K/L/M/N/O`: `blocked`
- Mevcut React uygulaması gerçek Organization API verisini kullanan bootstrap görünümüdür (`App.tsx` ~209 satır + `organizationApi.ts` ~77 satır). Kanonik Cari/Satış/Alış/Stok/Ürün/Üretim/Finans ekranı yoktur.
- Repository görünürlüğü **PUBLIC**, `main` **korumasız**, ruleset listesi **boş**. Her ajan çalışmasından önce bu ayrıca doğrulanır (bkz. bölüm 11.1).
- Repoda "Claude" referansı yok; mevcut kabul edilmiş sahiplik `CODEX-PLATFORM` + `CHATGPT-BUSINESS/CARI` + Sinan üzerine kurulu (`ERP-000K_SHELL_CONTRACT_v1.md` §17, `CARI_UI_VERTICAL_SLICE_v1.md` v1.5).

---

## 1. Ana hedef

İki kod yazan ajan, aynı dosya veya aggregate üzerinde yarışmadan çalışan dikey dilimler üretir.

Yeni başarı tanımı:

> Bir çalışma döngüsü yalnız belge veya hazırlık kanıtı ürettiğinde ürün döngüsü sayılmaz. Döngü; çalışan endpoint, gerçek API kullanan ekran, kırmızıdan yeşile geçen davranış testi veya uygulanmış kullanıcı kararı üretince tamamlanır.

Güvenlik kapıları korunur; fakat düşük riskli iş ile kritik security/migration işi aynı maliyetli süreçten geçirilmez.

---

## 2. Dört rol

### 2.1 Sinan Göymen — Ürün ve merge otoritesi

Sinan:

- son iş kararını verir;
- `OPEN-*` kayıtlarını kapatır;
- `AGENTS.md`, governance, karar sicili, master rapor, task manifest ve `.github/**` değişikliklerinin semantic sahibidir;
- Katman 3 PR'larında insan onayı verir;
- production activation, migration, permission ve deployment kararını verir.

Ajanlar bu yüzeyler için proposal PR hazırlayabilir; tek başına kabul edilmiş karar veya merge yetkisi kazanmaz.

### 2.2 ChatGPT-BUSINESS — İş anlamı ve orkestrasyon sahibi

ChatGPT-BUSINESS:

- kullanıcı kararlarını acceptance kriterlerine çevirir;
- Cari/Ürün/Hizmet/Finans/Stok iş anlamını korur;
- açık PR ve issue kuyruğunu önceliklendirir;
- iki ajanın aynı işi veya çelişkili sözleşmeyi üretmesini engeller;
- PR açıklamasındaki iddiaları kaynak ve test kanıtıyla karşılaştırır;
- production kodunun varsayımla yeni iş kuralı üretmesini engeller.

Normal durumda production source yazarı değildir.

### 2.3 Codex — Backend, platform ve veri sahibi

Codex her döngü başında canlı `main`, açık PR'lar, `AGENTS.md`, karar sicili, master rapor, task manifest, ilgili accepted contract'lar ve **`docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`** ile bu protokolü okur.

Codex'in tek-yazar alanı:

- `src/backend/**`
- `src/backend/GoymenERP.Api/**`
- `src/backend/Modules/**`
- `src/backend/BuildingBlocks/**`
- EF Core DbContext, mapping, migration ve persistence
- backend unit, architecture, HTTP ve gerçek SQL acceptance testleri
- OpenAPI üretimi ve backend-contract doğrulama tooling'i
- platform security, CompanyContext, Identity, Audit, Idempotency ve observability runtime'ı

Branch öneki: `codex/`

**Önemli:** API endpointleri Claude'a ayrılmaz. Endpoint; authorization, CompanyContext, ProblemDetails, audit ve idempotency sınırının parçası olduğu için backend sahibiyle atomik kalır. Domain/application Codex'te, endpoint Claude'da olursa tek davranış iki bağımlı PR, iki head ve iki review gerektirir; bu koordinasyonu yavaşlatır.

### 2.4 Claude — Frontend, UX ve browser kabul sahibi

Claude her döngü başında canlı `main`, `AGENTS.md`, merged Cari UI contract, shell contract, `docs/ui` ekran kartları, OpenAPI/generated-client akışı ve **`docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`** ile bu protokolü okur. Prompt içindeki SHA ve PR durumlarını güvenilir saymaz; GitHub'dan çözer.

Claude'un tek-yazar alanı:

- `src/frontend/**`
- React shell implementation'ı
- router, page composition, feature mount consumer, query/cache consumer
- generated TypeScript API client'in tüketimi
- form/list/360/workspace/drawer/modal bileşenleri
- frontend unit/component testleri
- browser E2E, visual regression ve accessibility testleri (test paketi seçimi bölüm 8 K3'e tabidir)
- `docs/ui/**`

Branch öneki: `claude/`

Claude backend endpoint, DbContext, migration, permission veya server authorization kodu yazmaz. Eksik contract için Codex'e issue/handoff açar.

---

## 3. Semantic owner, implementation writer ve reviewer ayrımı

Tek bir "owner" alanı üç farklı şeyi karıştırmamalıdır.

| Yüzey | Semantic owner | Implementation writer | Zorunlu reviewer |
|---|---|---|---|
| Ortak shell güvenlik sözleşmesi | `CODEX-PLATFORM` | `CLAUDE-FRONTEND` | Codex + ChatGPT-BUSINESS |
| Cari iş davranışı | `CHATGPT-BUSINESS/CARI` | Codex backend / Claude frontend | karşı ajan + ChatGPT-BUSINESS |
| Product/Service iş davranışı | `CHATGPT-BUSINESS/PRODUCT-SERVICE` | Codex backend / Claude frontend | karşı ajan + ChatGPT-BUSINESS |
| Identity/CompanyContext/Audit/Idempotency | `CODEX-PLATFORM` | Codex | ChatGPT-BUSINESS + Sinan, riskine göre |
| UI görsel ve erişilebilirlik davranışı | `CLAUDE-FRONTEND/UX` | Claude | Codex security review |
| Governance ve karar | Sinan | proposal hazırlayan ajan olabilir | Sinan |

Mevcut shell ve Cari UI sözleşmeleri Claude'a yazarlık vermiyorsa, Claude `App.tsx`, global CSS, router ve ortak shell dosyalarına dokunmadan önce açık bir **implementation handoff amendment** merge edilmelidir:

```text
SEMANTIC OWNER: CODEX-PLATFORM
FRONTEND IMPLEMENTATION WRITER: CLAUDE-FRONTEND
SECURITY REVIEWER: CODEX-PLATFORM
BUSINESS REVIEWER: CHATGPT-BUSINESS
```

Bu handoff iş kuralı veya permission üretmez; yalnız dosya yazarlığını aktarır. `ERP-000K_SHELL_CONTRACT_v1.md` §17 ve `CARI_UI_VERTICAL_SLICE_v1.md`'ye handoff satırı olarak eklenir (Sinan/ilgili owner onayıyla).

---

## 4. Kesin dosya sahipliği

| Yol / yüzey | Yazar |
|---|---|
| `src/backend/**` | Codex |
| `tests/GoymenERP.*.UnitTests/**` | Codex |
| `tests/GoymenERP.ArchitectureTests/**` | Codex |
| `tests/GoymenERP.Api.IntegrationTests/**` | Codex |
| `src/frontend/**` | Claude |
| kabul edilmiş browser/E2E test kökü | Claude |
| `docs/ui/**` | Claude |
| `docs/features/**` | dosyanın semantic owner'ı |
| `docs/security/**`, `docs/adr/**` | Codex; business anlamı varsa ChatGPT review |
| `AGENTS.md`, `docs/governance/**`, `docs/decisions/**`, `docs/master/**`, `docs/backlog/task_manifest.json`, `.github/**` | Sinan onaylı proposal-only |

"Proposal-only" şu demektir: ajan ayrı branch'te değişiklik hazırlayabilir; değişiklik Sinan onayı olmadan merge edilemez.

Bir PR, sahibinin dışındaki production dosyasını içeriyorsa otomatik olarak bölünür. Karşı ajan aynı PR'a düzeltme commit'i atmaz; review sonucu ve handoff issue üretir.

Çakışma tespiti: her PR açılışında `git diff --name-only origin/main` çıktısı karşı yüzeyden dosya içeriyorsa PR reddedilir ve bölünür.

---

## 5. API sözleşmesi ve contract drift

İki ajanın tek buluşma yüzeyi sürümlü HTTP/OpenAPI contract'ıdır.

### 5.1 Üretim

Codex:

1. exact backend SHA'dan deterministik OpenAPI çıktısı üretir (`artifacts/openapi.json`);
2. üretim komutunu repository-visible tooling altında tutar;
3. authorization veya CompanyContext bilgisini istemciye yetki kaynağı olarak açmaz;
4. endpoint/DTO/error değişikliğini contract testine bağlar.

### 5.2 Tüketim

Claude:

1. TypeScript request/response tiplerini elle çoğaltmaz;
2. kabul edilmiş ve pinlenmiş generator ile client üretir;
3. generated klasörü elle düzenlemez;
4. UI branching'i localized `title/detail` yerine stable `code` üzerinden yapar.

Önerilen generated yol:

```text
src/frontend/goymen-erp-web/src/api/generated/
```

### 5.3 Drift kapısı

CI:

1. backend'i build eder;
2. OpenAPI'yı yeniden üretir;
3. TypeScript client'i yeniden üretir;
4. committed generated çıktı ile karşılaştırır;
5. fark varsa `contract-drift` job'ını kırmızı yapar.

`.github/**` değişikliği Katman 3'tür. Codex teknik PR'ı hazırlar; Sinan onaylar ve merge eder.

Tek taraflı endpoint veya elle yazılmış DTO yasaktır. Bu mekanizma iki ajanın birbirinin varsaydığı alan adını uydurmasını (bu projedeki en olası halüsinasyon biçimi) derleyici seviyesinde bitirir.

---

## 6. Dikey dilim kuralı

Öncelik yatay platform belgesi değil, kullanıcıya ulaşan dikey dilimdir.

Bir dilim, uygulanabilir katmanları kapsar:

```text
domain/application
→ HTTP contract ve endpoint
→ gerçek SQL/HTTP acceptance
→ generated TS client
→ gerçek API kullanan ekran
→ component/browser/accessibility acceptance
```

Her dilim yeni DB şeması gerektirmez. Mevcut şema yeterliyse migration üretilmez. (Örn. Counterparty domain/application/infrastructure zaten `main`de.)

Platform işi yalnız aktif bir dikey dilimin adlandırılmış ihtiyacını karşılıyorsa açılır. "İleride lazım olabilir" gerekçesi tek başına yeni yatay PR açmak için yeterli değildir.

Yalnız `docs/**` değiştiren çalışma:

- yapılabilir;
- fakat `DÖNGÜ` değil `NOT` olarak kaydedilir;
- bir aktif feature'ın named blocker'ını kapatmalıdır;
- art arda iki behavior üretmeyen turun ardından üçüncü hazırlık turu başlamaz. Ajan durur ve Sinan'a hangi `OPEN-*` kararının kilit olduğunu tek paragrafta bildirir.

İlk dikey dilim **Cari (Counterparty)** olmalıdır: modül zaten `main`de, `CARI_UI_VERTICAL_SLICE_v1` ve `CARI_MODULE_ENTRYPOINT_CONTRACT_v1` yazılmış, `CARI-ROUTE-01..10` ekran kartları katalogda. Eksik olan tek şey üst katman.

---

## 7. ERP-000K için doğru unblock modeli

`ERP-000K` doğrudan `blocked → ready` yapılmaz. `task_manifest.json`: `depends_on = [A,B,C,D,E,L,O]`, `blocked_by = [ERP-000K-SHELL-CONTRACT]`. `ERP-000K_SHELL_CONTRACT_v1.md`: `implementation_status: BLOCKED`, `production_activation: NO-GO`; server-backed capability yok, route registry yok, `/api/v1/identity/me` permission/scope döndürmüyor. Manifesti `ready` yapmak `AGENTS.md`'nin "açık kararları sessiz varsayımla production'a alma" yasağını çiğner.

Bunun yerine kullanıcı kararıyla bir geliştirme dilimi açılır:

```text
SHELL-PILOT-01
```

### Başlama kapıları

- mevcut React bootstrap ve Organization şirket API'si;
- merge edilmiş capability/route/mount contract girdileri;
- açık implementation handoff;
- CompanyContext generation/stale-response fence acceptance;
- OpenAPI/generated client ve contract-drift kapısı;
- shell'de mock business menü/sonuç üretmeme.

### Tamamlama kapıları (ERP-000K `done` için ayrıca gerekir)

- server-backed capability runtime;
- gerçek route registry ve feature mount;
- ERP-000L error/telemetry sınırları;
- ERP-000O browser/accessibility/quality enforcement;
- responsive ve keyboard acceptance;
- Sinan UAT;
- production activation kararı.

Bu ayrımla geliştirme başlayabilir; `ERP-000K done`, production-ready veya tüm 159 ekran uygulanmış sayılmaz.

---

## 8. Risk katmanları

Katman yol adına değil **semantic etkiye** göre seçilir. Katman ataması PR başlığında belirtilir: `[K1]`, `[K2]`, `[K3]`. Yanlış katman ataması karşı ajanın review'unda blocker'dır.

### K1 — düşük risk / hızlı merge

Yalnız:

- yazım, biçim, bozuk link;
- normatif olmayan açıklama;
- mevcut contractı değiştirmeyen generated katalog/manifest;
- davranış değiştirmeyen test fixture/tooling.

Şunlar docs-only olsa bile K1 değildir: security/permission contractı; CompanyId veya authorization anlamı; task/acceptance durumu; cross-module contract; governance veya merge politikası; formula, money, stock, production veya audit/idempotency semantiği.

Kapı: CI + exact diff allowlist. Branch protection aktif olmadan otomatik merge yoktur.

### K2 — normal feature

- normal domain davranışı;
- endpoint;
- frontend ekranı;
- component;
- browser akışı;
- mevcut şemayı kullanan dikey dilim.

Kapı: standard CI; contract drift; ilgili unit/integration/browser testleri; karşı ajan exact-head review'u; P0/P1 yok; `AGENTS.md` uygulanabilir kapıları. İnsan review'u opsiyoneldir.

### K3 — kritik

- DB schema/migration/constraint;
- Identity, RBAC/ABAC, permission;
- CompanyId ve tenant isolation;
- Audit, idempotency, outbox/inbox;
- shared ProblemDetails/correlation;
- finans/stok/üretim formülü;
- cross-module contract;
- `.github/**`, governance, decision, task manifest;
- secrets, deployment, backup/restore;
- **frontend router, query/cache, form, grid, icon, accessibility ve browser/E2E paketi seçimi** — `ERP-000K_SHELL_CONTRACT_v1.md` §19 gereği güncel resmî destek, lisans, security/supply-chain, üç yıllık TCO ve React/TypeScript/Vite uyumluluğu review'undan geçer. Playwright veya herhangi bir paket bu review'dan önce varsayılan kabul edilmez.

Kapı: full CI; gerçek SQL; rollback/restore veya ilgili failure injection; exact-head karşı review; Sinan'ın açık insan onayı; expected-head squash merge; auto-merge yok.

---

## 9. Çapraz review protokolü

Katman 2 ve 3 PR'larında karşı ajan review eder. Codex'in yazdığını Claude, Claude'un yazdığını Codex inceler.

**ChatGPT-BUSINESS review yükü (düzeltme):** ChatGPT-BUSINESS iş-anlamı taşıyan her K2/K3 için zorunlu reviewer'dır. Ancak **yalnız görsel/erişilebilirlik K2** PR'larında (yeni business alanı, KPI, formül, permission veya API mapping içermeyen; salt layout/token/focus/responsive) ChatGPT-BUSINESS review'u **bloklayıcı değildir**: Codex security review'u + `AGENTS.md` "mock KPI / sahte veri yok" kuralı yeterli kapıdır, ChatGPT notu asenkron gelir. Bu, en yüksek hacimli PR türünü ChatGPT müsaitliğine bağlamaz.

Review çıktısı serbest metin değil, sabit şablondur:

```text
REVIEWED HEAD: <40-char SHA>
BASE SHA: <40-char SHA>
KATMAN: K1 | K2 | K3 — doğru | yanlış
SOURCE OF TRUTH: <karar/spec/issue kimlikleri>
DEĞİŞEN DAVRANIŞ: <endpoint/ekran/test>
YÜZEY İHLALİ: yok | <dosyalar>
CONTRACT DRIFT: yok | <fark>
KANIT:
- <test/job adı ve sonucu>
- <skip sayısı>
DOĞRULANMAYAN İDDİA: yok | <iddia>
COMPANYID/YETKI/AUDIT/IDEMPOTENCY REGRESYONU: yok | <bulgu>
UI DURUMLARI: loading/empty/error/forbidden/conflict/offline/stale — tam | eksik
ACCESSIBILITY/BROWSER: tam | uygulanamaz | eksik
KARAR: HANDOFF-ACCEPTED | BLOCKER
```

### 9.1 Halüsinasyon karşıtı kurallar

Çapraz review halüsinasyonu azaltır, sıfırlamaz. Asıl güvence çalıştırılabilir doğrulamadır.

1. Her "çalışıyor / geçiyor / doğrulandı" cümlesinin yanında test adı veya CI job linki bulunur. Kanıtsız iddia blocker'dır.
2. Dosya ve sembol adları uydurulmaz. Review eden ajan her dosya yolunu ve tip adını repoda fiilen doğrular. Var olmayan yola atıf tek başına blocker'dır.
3. Yeşil CI "test yazıldı"nın yerine geçmez. Yeni davranışın kırmızıdan yeşile dönen testi yoksa davranış eklenmemiş sayılır.
4. Mock/InMemory kanıtı relational veya browser kanıtı değildir.
5. Skip edilmiş test yeşil sayılmaz. Skip sayısı PR açıklamasında yazılır.
6. İş kuralı uydurulmaz. Kaynaklarda olmayan eşik, oran, rol adı veya politika `OPEN-*` olarak işaretlenir.

Kurallar: review yalnız exact head içindir; head değişirse review stale olur; AI ajan review'u insan onayı yerine geçmez; review eden ajan aynı PR'a kod commit'i atarsa bağımsız review geçersizdir.

---

## 10. WIP ve PR hijyeni

- Her ajan en fazla **2 açık implementation PR** taşır.
- Aynı anda toplam en fazla **5 aktif merge-queue PR** bulunur.
- Backlog işi PR olarak aylarca açık tutulmaz; issue olarak tutulur.
- Bir PR yeni `main` nedeniyle stale olduğunda aynı çalışma döngüsünde: sync edilip fresh CI/review alır, clean rebuild edilir, veya kapatılıp issue'ya geri taşınır.
- PR açıklamasındaki exact head SHA canlı head ile eşleşmek zorundadır.
- "Hazır", "kanıtlandı", "yeşil" iddiası test/job kimliği olmadan yazılamaz.
- Eski green run yeni head için kullanılamaz.
- Draft PR sayısı ilerleme metriği değildir.

---

## 11. İlk stabilizasyon turu

Bu protokol devreye alınmadan önce.

### 11.1 Repository güvenliği — Sinan'ın bloklayıcı önkoşulu

Repo **PUBLIC**, `main` **korumasız**, ruleset **boş**. Branch protection yokken "K1 oto-merge", "zorunlu CI" veya "insan onayı" yalnız metinsel protokoldür; GitHub bunları teknik olarak zorlamaz. İkinci bir kod-yazan ajan (Claude) yazma erişimi almadan önce Sinan:

1. repository görünürlüğünün bilinçli olup olmadığını doğrular; gerekirse private yapar;
2. `main` için branch protection/ruleset açar;
3. direct push, force-push ve deletion'ı kapatır;
4. required checks'i tanımlar;
5. K3'te stale review dismissal ve insan merge kapısını etkinleştirir.

Bu kapılar yokken K1 dahil otomatik merge kullanılmaz.

### 11.2 Açık PR temizliği

Canlı snapshot'a göre ilk kuyruk (numaralar her döngü GitHub'dan yeniden doğrulanır):

**Hızlı karar verilmesi gereken aktif dilimler**

- `#111` — Cari stable ProblemDetails codes;
- `#114` — platform request metadata ProblemDetails;
- `#91` — CI bootstrap preflight (K3 insan güvenlik kararı);
- `#105` — group authorization scope contract (Product hattı Cari UI'yı bloke etmemeli).

**Clean rebuild / supersede / kapatma incelemesi**

- `#65` — eski execution-plan snapshot'ı;
- `#44`, `#85` — route/security metadata tek-gerçeklik zinciri;
- `#52` — eski Counterparty migration artifact taşıyıcısı;
- `#10` — ERP-000F readiness ile current manifest parity;
- diğer eski-base Draft PR'lar.

**Backlog'a geri taşınabilecekler**

- `#28`, `#86`, `#89`, `#90`, `#101` — güncel dikey dilimi bloke etmiyorsa açık PR yerine issue olur.

Hedef: tam temizlik için haftalar harcamadan, **bir iş günü** içinde aktif merge kuyruğunu `≤5` yapmak.

---

## 12. İlk dört davranış dalgası

### Dalga 1 — SHELL-PILOT-01

**Codex**

- mevcut merge edilmiş shell/capability contractını tüketir;
- yalnız gerekli server-backed capability/route declaration yüzeyini uygular;
- Organization şirket dizini contractını sürümler;
- `artifacts/openapi.json` export ve contract testini kurar;
- CompanyContext ve permission negatif testlerini gerçek HTTP/SQL ile kanıtlar.

**Claude**

- explicit handoff sonrası React shell'i modülerleştirir;
- router, layout, breadcrumb ve global state katmanını kurar (router/query-cache paket seçimi bölüm 8 K3);
- şirket değişiminde request-generation/cache/stale-response fence uygular;
- loading/empty/error/401/403/500/offline durumlarını oluşturur;
- capability yoksa business menü göstermemeyi uygular;
- desktop/tablet/mobile ve keyboard temel testlerini yazar.

**Çıkış:** Uygulama açılır; yetkili şirketler gerçek API'den gelir; şirket değişimi stale veriyi göstermez; yalnız server capability ile izinli route mount edilir; mock ERP ekranı yoktur.

### Dalga 2 — CARI-ROUTE-01 / C-01 Cari Kartlar

**Codex**

- Issue `#94` accepted contractına göre server-side arama, filtre ve authenticated keyset cursor uygular;
- CompanyId/authorized multi-company read sınırını korur;
- `invalid_cursor`, non-disclosure ve exact count semantiğini test eder;
- gerçek SQL performans/cardinality acceptance üretir;
- OpenAPI contractını yayınlar.

**Claude**

- generated client ile `/cari` ekranını uygular;
- server-side search/filter/sort/cursor kullanır;
- loading, no-data, filter-empty, error, forbidden, malformed ve stale-company state'lerini gösterir;
- desktop drawer / mobil detay akışını yalnız kabul edilen alanlarla kurar;
- listede mock bakiye/risk/stok KPI göstermez;
- component + browser + accessibility testleri yazar.

**Çıkış:** İlk kanonik business ekran gerçek API verisiyle çalışır ve yabancı şirket verisi sızdırmaz.

### Dalga 3 — C-02 Yeni Cari + şirkete bağlama workspace'i

**Codex**

- mevcut create ve company-link davranışını current stable error contractıyla tamamlar;
- Issue `#95` candidate API'sini uygular;
- idempotency, replay, race, conflict ve rollback acceptance'ını gerçek SQL/HTTP ile kanıtlar;
- platform ve Cari error sahipliğini karıştırmaz.

**Claude**

- C-02 create alt kümesini uygular;
- her logical submit için tek `Idempotency-Key` üretir;
- timeout/unknown outcome'da aynı key/payload retry akışını korur;
- `validation_failed`, `command_metadata_invalid`, `already_linked`, `version_conflict`, `idempotency_conflict` durumlarını stable code ile gösterir;
- replay'de ikinci toast/navigation/business etkisi üretmez;
- şirket link candidate ekranında yalnız yetkili source özetlerini gösterir.

**Çıkış:** Cari oluşturma ve şirkete bağlama gerçek veritabanı üzerinde çalışır; çift kayıt oluşmaz; kullanıcı anlaşılır ve güvenli hata görür.

### Dalga 4 — C-03 Cari 360 çekirdek özeti

**Codex**

- yalnız kabul edilen Party ve CompanyPartyAccount alanlarını veren projection'ı doğrular;
- global Party durumu ile company-account durumunu ayırır;
- foreign/hidden account redaction ve direct-ID non-disclosure testlerini tamamlar.

**Claude**

- C-03 ekranını gerçek projection ile uygular;
- cari kimliği, türü, global durum ve yalnız yetkili şirket hesaplarını gösterir;
- henüz sahibi olmayan finans/risk/stok/CRM/AI sekmelerini veya sahte KPI'ları açmaz;
- deep-link, refresh, back/forward ve company-switch browser testlerini tamamlar.

**Çıkış:** Üçüncü kanonik Cari ekranı gerçek veriyle çalışır; erişilemeyen alanlar inert boş sekme olarak görünmez.

---

## 13. Ölçüm

Her hafta `docs/delivery/AUTONOMOUS_PROGRESS.md` içine raporlanır:

- gerçek API kullanan kanonik ekran sayısı;
- çalışan endpoint/davranış sayısı;
- kırmızıdan yeşile dönen acceptance testi;
- aktif merge-queue PR sayısı;
- stale exact-SHA iddia sayısı;
- docs-only tur sayısı;
- kapanan `OPEN-*` sayısı.

İlk hedef:

```text
KANONİK BUSINESS EKRANI: 0 → en az 3
AKTİF MERGE QUEUE: 14 → en fazla 5
DAVRANIŞ ÜRETMEYEN ARDIŞIK TUR: en fazla 1
STALE EXACT-SHA İDDİASI: 0
```

Her döngü sonunda mevcut formata ek olarak şu satır yazılır:

```text
DAVRANIŞ DEĞİŞİKLİĞİ: <evet: hangi endpoint/ekran/test> | <hayır: sebep>
AJAN: codex | claude
KATMAN: K1 | K2 | K3
```

Arka arkaya iki "hayır" satırı görülürse bölüm 6'daki durma kuralı işler.

---

## 14. Codex başlangıç promptu

```text
ROL: CODEX-BACKEND/PLATFORM

Önce canlı main, açık PR'lar, AGENTS.md, karar sicili, master rapor, task manifest,
ilgili accepted contracts, docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md ve
bu iş bölümü protokolünü oku. Prompt içindeki SHA ve PR durumlarını güvenilir sayma;
GitHub'dan çöz.

YAZABİLECEĞİN ALAN:
- src/backend/** (endpoint dahil)
- backend unit/architecture/API integration/real-SQL testleri
- backend OpenAPI üretim tooling'i
- kendi platform/security docs alanın

YAZAMAYACAĞIN ALAN:
- src/frontend/**
- docs/ui/**
- Claude branch'leri
- kullanıcı onayı olmadan governance/decision/task manifest/.github merge'i

İLK HEDEF:
1. Açık Cari/platform merge kuyruğunda canlı exact-head truth'u düzelt.
2. PR #111 ve #114'ü çakışma, error ownership ve exact-SHA açısından incele.
3. SHELL-PILOT-01 için yalnız gerekli capability/route/company-directory ve OpenAPI contractını uygula.
4. Ardından Issue #94 Cari paged-read backend dilimini tamamla.

Her PR:
- tek writer; exact head; gerçek SQL gerektiğinde gerçek SQL;
- stable ProblemDetails ownership;
- CompanyId/authorization/audit/idempotency regresyonu yok;
- UI için uydurma DTO veya business varsayımı yok;
- auto-merge yok.
```

---

## 15. Claude başlangıç promptu

```text
ROL: CLAUDE-FRONTEND/UX

Önce canlı main, AGENTS.md, merged Cari UI contract, shell contract, docs/ui ekran
kartları, OpenAPI/generated-client akışı,
docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md ve bu iş bölümü protokolünü oku.
Prompt içindeki SHA ve PR durumlarını güvenilir sayma; GitHub'dan çöz.

YAZABİLECEĞİN ALAN:
- src/frontend/**
- frontend component testleri
- kabul edilmiş browser/E2E test kökü
- docs/ui/**

YAZAMAYACAĞIN ALAN:
- src/backend/**
- GoymenERP.Api/Endpoints/**
- DbContext/migration
- permission/CompanyId/server authorization
- Codex branch'leri
- kullanıcı onayı olmadan governance/decision/task manifest/.github merge'i

İLK HEDEF:
1. Explicit shell implementation handoff'u doğrula.
2. SHELL-PILOT-01 React kabuğunu gerçek Organization API ile uygula.
3. CompanyContext switch sırasında stale-response/cache/request fence kur.
4. Backend OpenAPI contractı hazır olduğunda generated client'i tüket.
5. Issue #94 backend'i merge-ready olduğunda C-01 Cari Kartlar ekranını paralel geliştir.
6. Mock bakiye/risk/stok/AI veya sahte permission üretme.

Her PR:
- yalnız frontend yüzeyi; gerçek API contractı;
- loading/empty/error/forbidden/conflict/offline/stale state'leri;
- keyboard, focus, mobile ve accessibility kanıtı;
- localized hata metnini parse etmeme;
- backend authorization yerine UI visibility kullanmama;
- yeni npm paketi için bölüm 8 K3 review'u olmadan varsayılan seçim yok;
- auto-merge yok.
```

---

## 16. Çatışma çözümü

- İki ajan aynı dosyayı değiştirmişse: sahibi olan kazanır, diğeri PR'ını böler.
- İki ajan aynı sözleşme için farklı yorum yapmışsa: `KARAR_SICILI.md` bakılır; orada yoksa `OPEN-*` açılır ve iş durur. Tahminle ilerlenmez.
- İki ajan birbirinin review'unu blocker'lamışsa: Sinan'a taşınır, üçüncü tur yok.
- Bir ajan diğerinin yüzeyine ikinci kez dokunmuşsa: o döngü iptal edilir.

---

## 17. Nihai sıra

```text
0. Repository visibility + main protection (Sinan)
1. Bir günlük PR triage, aktif kuyruk ≤5
2. Shell implementation handoff amendment (Sinan onayı)
3. OpenAPI + generated client + contract drift (Codex)
4. SHELL-PILOT-01
5. C-01 Cari Kartlar
6. C-02 Yeni Cari + company-link workspace
7. C-03 Cari 360
8. Sonraki görünür dikey dilim
```

Açık Product, Reporting, supply-chain veya readiness işleri bu hattı bloke etmiyorsa açık implementation PR olarak tutulmaz. Önce kullanıcıya görünen ve güvenli biçimde çalışan Cari dilimi tamamlanır.
