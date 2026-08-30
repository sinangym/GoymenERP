# Göymen ERP — Codex / Claude / ChatGPT Otonom Teslim Planı v2.1

- Durum: **K3 GÜVENLİK DÜZELTMESİ** — bu exact head merge edildiğinde bağlayıcı
- Hedef konum: `docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md`
- İlişki: `CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md` protokolünü daraltır ve
  tamamlar; yerine geçmez. Merge edilmemiş PR #115 ve #116 önerilerini birlikte
  süperse eder. Çelişkide kaynak önceliği ile daha sıkı güvenlik/tek-yazar kapısı
  uygulanır.
- Kaynak önceliği değişmez: son kullanıcı kararı > `docs/decisions/KARAR_SICILI.md` > `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` > feature spec/ADR > StokERP yalnız referans.
- `AGENTS.md` mimari değişmezleri ve PR kabul kapısı aynen geçerlidir.

Bu belge, üç yapay zekâ ajanının (Codex, Claude, ChatGPT) tek GitHub gerçeği
üzerinden güvenli işleri sürekli ilerletmesini sağlar. Kullanıcı 31 Ağustos 2026'da
implementation merge koordinasyonu için standing onay vermiştir. Bu onay açık iş
politikasını tahmin etme, karar sicilini değiştirme veya production aktivasyonu yetkisi
değildir.

---

## 1. Yetki devri (autonomy grant)

Sinan Göymen, bu belgeyi onaylayarak aşağıdaki **duran yetkiyi** verir:

1. Lane ajanı kendi PR'ini merge etmez. Bölüm 10 kapıları geçince ayrı
   `CODEX-ORCHESTRATOR` expected-head squash merge yapabilir.
2. Karara bağlanmamış noktada bölüm 6 uygulanır. Yalnız geri alınabilir,
   işletme anlamı taşımayan teknik/UX ayarı `ASSUMPTION-*` olabilir. Para, stok,
   üretim, formül, yetki, onay, güvenlik, migration ve saklama politikası tahminle
   etkinleştirilmez.
3. Codex `.github/**` için build/test/contract-drift/real-SQL/security kapısı önerisi
   hazırlayabilir. Merge; iki bağımsız exact-head review ve coordinator kapısı ister.
   Deployment, secret ve production environment kapsam dışıdır.
4. Bu yetki `main` → production **aktivasyonunu kapsamaz**. Production activation, cutover ve gerçek hukuki/mali değer girişi Sinan'a aittir (bölüm 18).

Yetki geri alınabilir: Sinan herhangi bir anda "otonom dur" der; ajanlar mevcut PR'ları Draft'a çeker ve bekler.

---

## 2. "Teslim noktası" tanımı — dürüst kapsam

Ajanlar aşağıdaki duruma **insan kararı olmadan** ulaşır:

- Dalga 0–5 kapsamında 159 kanonik ekran ve kabul edilmiş `DEC-*` kararları
  uygulanır. Açık karara bağlı kapalı feature, "tamamlandı" sayılmaz.
- `AT-001…AT-070` senaryolarının uygulanabilir olanları ilgili kanıt türüyle
  yeşil olur. Feature flag kapalı veya karar bekleyen yolun AT kaydı yeşil sayılmaz;
  `BLOCKED_BY_DECISION` olarak raporlanır.
- CI tamamen yeşil; contract-drift sıfır; P0/P1 açık hata yok.
- Staging ortamına deploy edilebilir durumda (build-once artefakt + migration + rollback manifesti).
- Gerçek dış bilgi gerektiren yol **default-deny feature-flag kapalı + placeholder**
  kalır; etkin davranış, acceptance veya production-ready iddiası üretmez ve
  `DECISION_PACK.md`'de listelenir.

Ajanların **ulaşamayacağı** ve insana kalan:

- Production activation ve tek-gecelik cutover (`DEC-GOV-005`).
- `DECISION_PACK.md`'deki gerçek değerlerin girilmesi (VKN ile fatura kesilemez; banka oranı uydurulamaz).
- `OPEN-013` (SSO sağlayıcı/MFA), `OPEN-020` (bulut AI/KVKK sözleşmesi) production seçimi.
- Nihai kabul imzası (`DEC-TST-002`).

Yani hedef: **"staging-tam + DECISION_PACK hazır"** — Sinan tek oturumda paketi çözer, bayrakları açar, cutover yapar.

---

## 3. Dört rol — üç kod/analiz şeridi

### 3.1 Codex — Backend, platform ve veri şeridi

Tek-yazar alanı:

- `src/backend/**` (endpoint dahil), `Modules/**`, `BuildingBlocks/**`
- EF Core DbContext, mapping, migration, persistence
- backend unit/architecture/HTTP/gerçek SQL acceptance testleri
- geçici `artifacts/openapi.generated.json`, normalize edilmiş kanonik
  `contracts/openapi/goymen-erp-v1.json` ve backend-contract doğrulama tooling'i
- platform runtime: Identity, CompanyContext, Audit, Idempotency, Outbox/Inbox, Observability, Belge kasası, Global arama
- `.github/**` kalite kapısı proposal'ları (bölüm 1.3 kapsamı; coordinator merge)
- deterministik formül hesaplayıcıları (`Modules/**/Calculation`) — golden testli

Branch öneki: `codex/`

### 3.2 Claude — Frontend, UX ve tarayıcı kabul şeridi

Tek-yazar alanı:

- `src/frontend/**` — React shell, router, layout, 159 kanonik ekran, page composition, feature-mount consumer
- generated TypeScript API client'in tüketimi (`src/frontend/goymen-erp-web/src/api/generated/`)
- ortak list/360/form/workspace/drawer/modal bileşenleri ve durum katmanları
- frontend unit/component testleri
- browser E2E, visual regression, accessibility testleri (test paketi seçimi bölüm 9)
- `docs/ui/assets/**`, `docs/ui/prototypes/**` ve exact handoff verilmiş ekran kartları

Branch öneki: `claude/`

Claude backend endpoint, DbContext, migration, permission veya server authorization kodu yazmaz. Eksik contract için Codex'e `HANDOFF-*` issue açar ve o dilimi park edip sıradaki ekrana geçer.

### 3.3 ChatGPT — İş analizi, sözleşme ve orkestrasyon şeridi

Tek-yazar alanı:

- yeni iş alanı feature spec'leri ve açıkça `owner: CHATGPT-*` olan
  `docs/features/**` dosyaları (canlı sicilde 107 kilitli + 2 geçici karar)
- `docs/acceptance/**` — `AT-001…AT-070` senaryolarının gerçek veri/rol/adım/beklenen kayıt/audit ayrıntısı
- `docs/formulas/**` — formül kataloğu + golden test veri setleri (yalnız
  `DEC-PRD-009`/Master §10 veya kabul edilmiş formül sözleşmesindeki değerlerle;
  işletme semantiği belirsizse `OPEN-*` + default-deny kapalı yol)
- `docs/backlog/DELIVERY_BACKLOG_v1.json` — dalga/dilim backlog'unun canlı tutulması
- PR kuyruğu önceliklendirmesi
- **kritik nokta analiz hakemliği** (bölüm 6): bir ajan karara bağlanmamış noktaya çarptığında ChatGPT "güvenli varsayım" mı yoksa "gerçek `OPEN-*` blocker" mı olduğunu belirler
- `docs/decisions/KARAR_SICILI.md` / `ACIK_KARARLAR.md` senkron **öneri** PR'ları (merge Sinan)

Branch öneki: `chatgpt/`

ChatGPT production source (backend/frontend) yazmaz.

### 3.4 Sinan — Repository tercihi + terminal geçiş

Kalan insan yüzeyi:

1. **Repository koruma tercihi** (bölüm 14): private repository için GitHub planı
   yükseltilirse teknik branch protection açılır; aksi halde coordinator procedural
   kapısı kullanılır. Visibility otomasyon tarafından değiştirilmez.
2. **Terminal geçiş** (bölüm 18): `DECISION_PACK.md` çözümü + gerçek değer girişi + feature-flag açma + cutover.

`AGENTS.md`, `docs/governance/**`, `docs/decisions/**`, `docs/master/**`, `docs/backlog/task_manifest.json` semantic sahibi Sinan'dır; ajanlar yalnız öneri PR açar.

---

## 4. Kesin dosya sahipliği

| Yol / yüzey | Yazar | Diğer ajan |
|---|---|---|
| `src/backend/**` (endpoint dahil) | Codex | read-only exact-head reviewer |
| `tests/GoymenERP.*.UnitTests/**`, `*.ArchitectureTests/**`, `*.Api.IntegrationTests/**` | Codex | reviewer |
| `.github/**` (bölüm 1.3 kapsamı) | Codex proposal | iki reviewer + coordinator |
| `src/frontend/**` | Claude | reviewer |
| kabul edilmiş browser/E2E test kökü | Claude | reviewer |
| `docs/ui/assets/**`, `docs/ui/prototypes/**`, handoff'lu ekran kartı | Claude | reviewer |
| kanonik UI ID/statü/route/katalog/golden-gate semantiği | Claude proposal | Codex + coordinator K3 |
| `docs/features/**`, `docs/acceptance/**`, `docs/formulas/**` | dosyadaki mevcut owner; yeni business spec ChatGPT | reviewer |
| `docs/backlog/DELIVERY_BACKLOG_v1.json` | ChatGPT | reviewer |
| `docs/delivery/AUTONOMOUS_PROGRESS.md`, `docs/delivery/DECISION_PACK.md` | tek coordinator: ChatGPT-BUSINESS | Codex/Claude Issue #66 girdisi yollar |
| `docs/delivery/CRITICAL_ANALYSIS/<konu>/<ajan>.md` | dosya adındaki tek ajan | diğer ajan ayrı dosya yazar |
| `AGENTS.md`, `docs/governance/**`, `docs/decisions/**`, `docs/master/**`, `task_manifest.json` | Sinan onaylı proposal-only | öneri PR |

Bir dosyanın tek yazarı vardır. Bir PR sahibinin dışındaki production dosyasını içeriyorsa (`git diff --name-only origin/main` karşı yüzeyden dosya gösterirse) PR otomatik bölünür. Karşı ajan aynı PR'a commit atmaz; review sonucu + `HANDOFF-*` issue üretir.

Geçiş kuralı:

- Bu plan merge edilmeden açılmış PR'lar eski sahibiyle tamamlanır veya sahibi
  tarafından kapatılır; ortasında yazar değiştirilmez.
- `CHATGPT-CARI` Counterparty module/domain/API yüzeyi Codex'e, Cari frontend/UI
  yüzeyi Claude'a ancak iki ayrı durable exact-surface handoff sonrası geçer.
- Shared shell/App/router/query-cache/CompanyContext fence'i mevcut
  `CODEX-PLATFORM` sahibinde kalır; Claude ancak exact handoff sonrası yazar.
- Owner etiketi olmayan `docs/features/**` fail-closed read-only'dir.
- PR #115 ve #116 bu v2.1 düzeltmesi merge edilince superseded olarak kapatılır;
  cherry-pick veya birlikte merge edilmez.
- `codex/autonomous-delivery-v2-safety-correction` dalı, PR #117 sonrası P0/P1
  onarımı için Issue #66 coordination lease'i ve kullanıcının exact talimatıyla verilen
  tek seferlik `AGENTS.md`, bu governance dosyası,
  `docs/backlog/DELIVERY_BACKLOG_v1.json` ve
  `docs/delivery/DECISION_PACK.md` owner istisnasıdır. Merge sonrasında backlog ve
  Decision Pack tek yazarı yeniden `CHATGPT-BUSINESS` olur; istisna başka dala
  devredilemez.

---

## 5. Otonom döngü — her şerit için

Her ajan, kendi şeridinde **durmadan** şu döngüyü çalıştırır:

1. `git fetch --all --prune`. Canlı `main`, açık PR'lar, worktree durumu doğrula.
2. Şu kaynakları oku: `AGENTS.md`, bu belge, `CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`, `KARAR_SICILI.md`, `GOYMEN_ERP_MASTER_RAPOR_V3.md`, kanonik `task_manifest.json`, `DELIVERY_BACKLOG_v1.json`, ilgili feature spec/ADR/acceptance/formül/UI kartları, `DECISION_PACK.md`, `AUTONOMOUS_PROGRESS.md` son 3 döngü.
3. `DELIVERY_BACKLOG_v1.json` içinden bağımlılıkları karşılanmış
   en yüksek öncelikli dilimi seç. `manifest_task` varsa kanonik manifestte status
   `ready` olmalı ve `blocked_by` kapıları fiziksel olarak kapanmış olmalıdır.
   Backlog, manifesti gevşetemez. Öncelik, JSON'daki `waves` sırası ve aynı wave
   içindeki `slices` sırasıdır; validator iki sırayı da semantic hash ile kilitler.
4. Dilimi kendi şeridinde uçtan uca üret (bölüm 12 teslim modeli).
5. Karara bağlanmamış noktaya çarparsan → **bölüm 6 kritik nokta protokolü**. Durma; ilerle veya sıradaki dilime geç.
6. PR aç: tek-writer, exact-head, `[K1|K2|K3]` başlık, full CI, gerektiğinde gerçek SQL, contract-drift yeşil.
7. Karşı ajandan **adversarial exact-head review** al (bölüm 8 şablonu).
8. Bölüm 10 kapıları geçince coordinator'a expected-head merge adayı olarak
   ilet. Lane yazarı kendi PR'ini merge etmez.
9. Issue #66'ya yapılandırılmış döngü kaydı yaz; ChatGPT-BUSINESS tek-yazar
   olarak `AUTONOMOUS_PROGRESS.md` ve `DECISION_PACK.md` derlemesini yapar.
10. Sıradaki dilim. Bağımlılıkları karşılanmış dilim varken **durma**.

**Paralellik:** üç şerit aynı anda çalışır. Codex Dalga N backend'i yaparken Claude Dalga N-1 ekranlarını, ChatGPT Dalga N+1 spec'lerini üretir. Buluşma yüzeyi yalnız OpenAPI (bölüm 9).

`DELIVERY_BACKLOG_v1.json` yalnız `status=ACTIVE_GATED` ve
`tools/validate_delivery_backlog.py` yeşilken yürütülebilir. Kapı; kanonik manifestteki
aktif görevleri exactly-once snapshot eder, 109 karar/21 OPEN/70 AT/159 ekran
baseline'ını ve dilim bazlı mapping hash'ini kilitler. `production_gate.default` yalnız
`false` olabilir. ERP-000F manifestte `ready` görünse bile eski orkestratör §11
fiziksel sözleşmeleri kapanana kadar `execution_state=SPEC_ONLY` kalır.

---

## 6. Kritik nokta analiz protokolü — "sor ve dur"un yerine

Bir ajan karara bağlanmamış bir noktaya çarptığında, **şu sırayla** değerlendirir ve **ilerler**:

### Adım 1 — Kilitli karar var mı?

Canlı sicilde 107 `KILITLI`, 2 `GECICI_VARSAYIM` karar vardır. Sayı her
döngüde yeniden hesaplanır; Master Rapor ve kabul edilmiş ADR/feature spec ile birlikte
taranır. Kapsıyorsa karara uyulur.

### Adım 2 — Değer / eşik / tolerans mı?

Örn. kantar toleransı, rezerv süresi, fiyat marj eşiği, debounce ms, yuvarlama basamağı.

- Yalnız işletme sonucu, yetki, para, stok, üretim, formül, saklama veya
  güvenlik semantiği taşımayan teknik/UX ayarı yapılandırılabilir varsayım olabilir.
- Master Rapor'daki kilitli/geçici varsayım varsa aynen kullan; yoksa işletme
  varsayılanı seçme. Örneğin debounce aralığı teknik varsayım olabilir; risk haircut,
  onay eşiği veya üretim toleransı olamaz.
- Issue #66'ya `DECISION-PACK-CANDIDATE` olarak `ASSUMPTION-NNN` adayı ekle:
  ne varsayıldı, hangi `DEC-*`/Master bölümünden, hangi config anahtarı, yanlışsa
  ne kırılır. Yalnız `CHATGPT-BUSINESS` derleyicisi bunu `DECISION_PACK.md`ye alır.
- Teknik/UX ayarında ilerle. İşletme semantiğinde Adım 3'e geç ve davranışı
  default-deny kapalı tut.

### Adım 3 — Gerçek dış bilinmeyen mi?

Örn. gerçek VKN, banka teminat haircut oranı, SSO sağlayıcı adı, bulut AI endpoint'i, sunucu RAID yapısı.

- Placeholder + `OPEN-*` referansı kullan (`OPEN-001` VKN, `OPEN-010` haircut, `OPEN-013` SSO, `OPEN-020` bulut AI...).
- Bu değeri production'da kullanacak her yol **feature-flag KAPALI** açılır.
- Issue #66'ya `DECISION-PACK-CANDIDATE` blocker satırı: Sinan ne sağlamalı,
  hangi bayrak açılacak, hangi ekran/akış bekliyor. Tek-yazar derleyici bunu pakete alır.
- Diğer bağımsız altyapıyı inşa etmeye devam et. Kapalı yol hesaplama,
  onay, muhasebe, stok veya production etkisi üretmez; acceptance'ı yeşil sayılmaz.

### Adım 4 — Mimari / güvenlik çatalı, güvenli varsayılan yok mu?

Örn. event-sourced stok defterinin partition stratejisi, tahsis motoru concurrency modeli, çok-şirket okuma cache mimarisi.

- **Çift-ajan adversarial analiz**: iki ajan (Codex + Claude, veya konuya göre
  Codex + ChatGPT) bağımsız analiz yapar. Her ajan yalnız kendi
  `docs/delivery/CRITICAL_ANALYSIS/<konu>/<ajan>.md` dosyasına yazar: seçenekler,
  `DEC-*`/`AGENTS.md` uyumu, güvenlik/performans/geri-alınabilirlik etkisi ve öneri.
- **Yakınsarsa**: geri alınabilir teknik PoC ve ADR önerisiyle ilerle. Security,
  CompanyId, migration, formül, para, stok veya üretim politikası kabul edilmiş karar
  olmadan production davranışına dönüşmez.
- **Iraksarsa**: `OPEN-*` öner (ChatGPT), bu dilimi park et, **sıradaki bağımsız dilime geç**. Asla boşta bekleme.

### Adım 5 — İş kuralı uydurma yasağı

Kaynaklarda olmayan rol adı, onay eşiği, oran veya politika hiçbir zaman tahminle kodlanmaz. Adım 2 veya 3'e düşer. Bu kural `AGENTS.md`'den devralınır ve gevşetilmez.

### Hakemlik

Adım 2 mi Adım 3 mü belirsizse: **ChatGPT hakem**dir. ChatGPT `DECISION_PACK.md`'ye sınıflandırmayı yazar; kod yazan ajan ona uyar.

---

## 7. DECISION_PACK.md mekanizması

Tek derleme dosyası `docs/delivery/DECISION_PACK.md`'dir ve tek yazarlıdır:
`CHATGPT-BUSINESS`. Codex/Claude yeni girdiyi Issue #66'ya `DECISION-PACK-CANDIDATE`
olarak yazar. ChatGPT güncel `main` üzerinde duplicate `OPEN-*`, ID ve revision kontrolü
yaptıktan sonra ayrı PR ile derler.

`ASSUMPTION-NNN` kimliği mevcut en büyük numaranın bir fazlası olarak yalnız derleyici
tarafından ayrılır. Silme yoktur; düzeltme yeni revision ve `SUPERSEDES` alanıyla yapılır.

Her giriş sabit formatta:

```text
### ASSUMPTION-042  (veya)  OPEN-010 blocker
- Alan: <modül / ekran / formül>
- Nokta: <karara bağlanmamış ne>
- Uygulanan varsayım: <ne yapıldı>
- Dayanak: <DEC-XXX | Master §N | "dış bilinmeyen, güvenli placeholder">
- Config anahtarı / feature-flag: <ad, mevcut değer, açık/kapalı>
- Yanlışsa etki: <ne kırılır, hangi ekran/akış>
- Sinan'dan gereken: <somut değer veya karar>
- Bekleyen dilim: <BACKLOG id, veya "yok — flag açılınca canlı">
```

`AUTONOMOUS_PROGRESS.md` derleyicisi her döngüde `DECISION_PACK` açık giriş
sayısını raporlar. Dosya başka açık PR tarafından değiştiriliyorsa yalnız Issue #66
kaydı kullanılır; aynı dosyada paralel append yapılmaz.

---

## 8. Adversarial çapraz review

Katman 2 ve 3 PR'larında karşı ajan review eder. Codex↔Claude birbirini; iş-kuralı/spec PR'larında ChatGPT de reviewer.

Çıktı sabit şablon:

```text
REVIEWED HEAD: <40-char SHA>
BASE SHA: <40-char SHA>
KATMAN: K1 | K2 | K3 — doğru | yanlış
SOURCE OF TRUTH: <DEC-* / spec / issue / AT-* kimlikleri>
DEĞİŞEN DAVRANIŞ: <endpoint / ekran / test — kırmızıdan yeşile dönen>
YÜZEY İHLALİ: yok | <dosyalar>
CONTRACT DRIFT: yok | <alan/tip farkı>
KANIT: <test/CI job adı ve sonucu> ; SKIP sayısı: <n>
DOĞRULANMAYAN İDDİA: yok | <PR açıklamasında test karşılığı olmayan iddia>
UYDURMA SEMBOL/YOL: yok | <repoda bulunmayan dosya/tip adı>
COMPANYID/YETKI/AUDIT/IDEMPOTENCY REGRESYONU: yok | <bulgu>
ASSUMPTION KAYDI: tam | eksik  (Issue #66 adayı/tek-yazar DECISION_PACK revision'ı + default-false production_gate doğru mu)
UI DURUMLARI: loading/empty/error/forbidden/conflict/offline/stale — tam | eksik | uygulanamaz
ACCESSIBILITY/BROWSER: tam | uygulanamaz | eksik
KARAR: HANDOFF-ACCEPTED | BLOCKER
```

Kurallar:

- Review yalnız exact head içindir; head değişirse review stale.
- Review eden ajan aynı PR'a kod commit'i atarsa bağımsız review geçersizdir.
- `DOĞRULANMAYAN İDDİA` veya `UYDURMA SEMBOL/YOL` tek başına BLOCKER'dır.
- İki ajan birbirini blocker'larsa: `CRITICAL_ANALYSIS` dosyası açılır, çift analiz yapılır; yine ıraksarsa `OPEN-*` + Sinan. Üçüncü tur yok.

---

## 9. API sözleşmesi ve contract drift

İki kod şeridinin tek buluşma yüzeyi sürümlü OpenAPI'dir.

- **Codex**: exact backend SHA'dan DB, seed, secret ve OIDC metadata erişimi olmadan
  deterministik `artifacts/openapi.generated.json` üretir; normalize ve commit edilen
  kanonik baseline `contracts/openapi/goymen-erp-v1.json` olur.
- **Claude**: TS tiplerini elle yazmaz; pinlenmiş generator ile
  `src/frontend/goymen-erp-web/src/api/generated/` üretir ve commit eder; generated
  dosyayı elle düzenlemez; UI branching'i localized metin değil stable `code` üzerinden
  yapar.
- **CI `contract-drift` job'ı**: backend build → OpenAPI'yı iki kez üret ve normalize
  hash'leri eşitle → kanonik baseline'ı karşılaştır → TS client'i iki kez üret ve
  committed çıktıyla karşılaştır → fark = kırmızı.
- PoC; typed response, bearer security, `X-Company-Id`, `Idempotency-Key`,
  correlation/replay header'ları ve ProblemDetails stable-code metadata matrisini
  kaybetmediğini kanıtlar. Generator/codegen sürümleri pinlenir.
- Tek taraflı endpoint veya elle DTO yasak. Contract değişikliği isteyen taraf karşıya `HANDOFF-*` issue açar.

`contract-drift` henüz mevcut değilse yalnız onu kuran bootstrap K3 PR'ı bu kapıdan
muaf olabilir; mevcut beş CI işi + iki exact review zorunludur. Bootstrap merge edildikten
sonra istisna kapanır. PR #91'in `.github/workflows/ci.yml` tek-yazar sırası korunur.

**Frontend paket seçimi** (router, query/cache, form, grid, browser-E2E):
`ERP-000K_SHELL_CONTRACT_v1.md` §19 gereği K3. Claude analizini
`CRITICAL_ANALYSIS/frontend-packages/claude.md`, Codex bağımsız review'unu
`frontend-packages/codex.md` dosyasına yazar. Yakınsama yalnız ADR önerisi üretir.
Playwright veya herhangi bir paket bu analizden önce varsayılan değildir.

---

## 9.1 Risk katmanı

- **K1:** yalnız yazım/bozuk link, normatif olmayan açıklama, davranışı
  değiştirmeyen fixture/tooling veya accepted contractı değiştirmeyen generated çıktı.
- **K2:** mevcut şemayı ve kilitli kararı kullanan normal domain/endpoint/frontend
  davranışı. Yeni permission, migration, cross-module contract veya açık politika yoktur.
- **K3:** DB schema/migration/constraint; Identity/RBAC/ABAC/CompanyId; audit,
  idempotency, outbox/inbox; cross-module contract; finans/stok/üretim/formül; package
  veya supply-chain; `.github/**`, governance, decision, master, task state; secret,
  deployment, backup/restore ve production yüzeyi.

Docs-only olmak riski düşürmez. Yanlış katman blocker'dır. Açık politika seçen K3
merge edilemez; ilgili `OPEN-*` owner kararını beklerken bağımsız dilime geçilir.

---

## 10. Otonom merge kapıları

Bir PR `main`e lane yazarından ayrı coordinator tarafından merge edilebilir **ancak ve
ancak** hepsi sağlanırsa:

1. Fresh standart CI tamamen yeşil (build, format/diff, unit, integration, architecture guard, frontend lint/test/build, container build).
2. Gereken yerde **gerçek SQL Server relational acceptance** yeşil (mock/InMemory yeterli değil).
3. `contract-drift` job'ı yeşil. Yalnız contract-drift bootstrap PR'ı bölüm 9'daki
   tek seferlik istisnayı kullanabilir.
4. Karşı ajan review'u `HANDOFF-ACCEPTED` (bölüm 8 şablonu, exact head).
5. Yeni davranışın **kırmızıdan yeşile dönen** testi var; skip sayısı PR'da yazılı.
6. P0/P1 açık bulgu yok.
7. PR bir `ASSUMPTION-*` veya `OPEN-*`'a dokunuyorsa: `DECISION_PACK.md` girişi tam **ve** ilgili feature-flag'in durumu doğru (dış bilinmeyen → kapalı).
8. Issue #66'da döngü kaydı vardır; tek-yazar derleme PR'ı ayrıdır ve feature PR'ını
   shared progress dosyasıyla çakıştırmaz.
9. K3 ise: yazar dışında **iki bağımsız ajan** exact-head imzası, gereken
   `CRITICAL_ANALYSIS` dosyaları ve ilgili gerçek SQL/browser/failure kanıtı vardır.
10. Coordinator merge öncesi PR head'ini yeniden okur; beklenen SHA değişmişse merge
    reddedilir. Lane yazarı kendi PR'ini merge edemez.

Kapılardan biri eksikse merge yok; PR Draft'a çekilir veya düzeltilir. GitHub teknik
koruması eksikse lane self-merge kapalıdır; coordinator prosedürel kanıtı GitHub'a
yazıp expected-head squash merge yapar. Bu, teknik koruma varmış gibi raporlanamaz.

---

## 11. Durma koşulları — yalnız üç

1. **P0 güvenlik / veri bütünlüğü bulgusu**, feature-flag arkasında güvenle azaltılamıyor → o şeridi durdur, Issue #66'ya `P0-BLOCKER` adayı yaz, **diğer iki şerit devam eder**.
2. **Kalan backlog'un tamamı `OPEN-*`'a bağlı** → `DECISION_PACK.md` derlenir, Sinan'a "şu N karar kilit" tek mesajı gider. Ajanlar bekler.
3. **Bir şeritte arka arkaya iki davranışsız döngü** → o şerit, hangi `OPEN-*`'ın kilit olduğunu Issue #66'ya tek paragrafta yazar, sonraki dalganın hazırlık işine (spec okuma, fixture) geçer. Üçüncü davranışsız döngü başlamaz.

Bunun dışında ajanlar durmaz, "onaylar mısın" diye sormaz, insan beklemez.

---

## 12. Teslim backlog'u — Dalga 0–5

Kaynak: Master Rapor §16 (fazlar) + §27 (ekran kataloğu) + Issue #66/#78/#100. Makine-okunur hâli `docs/backlog/DELIVERY_BACKLOG_v1.json` (ChatGPT sahibi, canlı tutar).

Her dilim teslim modeli:

```text
kullanıcı sonucu → veri modeli/migration → domain/application contract → API →
permission/scope/CompanyContext → audit/idempotency/concurrency → OpenAPI →
generated TS client → React UI → gerçek SQL kabul → browser E2E/accessibility →
AT-* kanıtı → exact-head çapraz review → merge
```

### Dalga 0 — Platform tamamlama (Codex ağırlıklı)

| Dilim | Kapsam |
|---|---|
| ERP-000F | Transactional outbox / inbox / dedup / retry-backoff; manifest `ready` olsa da eski orkestratör §11 fiziksel sözleşmeleri kapanana kadar `SPEC_ONLY` |
| ERP-000G | Belge kasası çekirdeği: blob/belge/sürüm/bağlantı, hash dedup, erişim politikası |
| ERP-000H | Türkçe PDF + Unicode dosya adı + gömülü font stratejisi (font dosyası `OPEN-019`, flag) |
| ERP-000I | Company/permission-scoped exact + full-text arama, keyset pagination |
| ERP-000M | SQL Server PoC + çok-DbContext migration sahiplik/sıra/rollback (RPO/RTO geçici `DEC-TEC-007`) |
| ERP-000O | CI/CD fail-closed kalite kapıları, build-once artefakt, contract-drift job'ı, OpenAPI export |
| ERP-000L | Gözlemlenebilirlik: log/metrik/trace/health/correlation/security-event (OPEN-014/15/17 geçici) |
| SHELL-PILOT-01 | React kabuğu: router, layout, CompanyContext seçici, capability gating, ortak durum bileşenleri (Claude; paket seçimi bölüm 9) |

Çıkış: uygulama gerçek API ile açılır; OpenAPI→generated client zinciri canlı; CI otonom merge'e güvenilir.

### Dalga 1 — Ana veriler (Faz 1)

| Dilim | Ekranlar | Kilitli karar |
|---|---|---|
| CARI | C-01…C-12 | DEC-CAR-001..003, AT-009..014 |
| URUN-HIZMET | U-01…U-10, IA-01/02 | DEC-PRD-001..011, AT-015..020, Issue #100 |
| ORG-MASTER | AV-01…AV-03 | DEC-ORG-001..004 |
| ONAY-YETKI | SY-01…SY-08 | DEC-GOV, §12 yetki modeli, AT-004 |

### Dalga 2 — Ticari MVP (Faz 2)

| Dilim | Ekranlar | Kilitli karar |
|---|---|---|
| SATIS | S-01…S-11 | DEC-SAL-001..008, AT-021..025 |
| SATINALMA | P-01…P-12 | DEC-PUR-001, AT-026..028 |
| CARI-360-RISK | C-03/C-08 risk iskeleti | DEC-FIN-007..009 (haircut 0, flag) |
| LOJISTIK-BELGE | S-07/L-08 irsaliye taslak, P-07/L-10 mal kabul, B-01…B-11 | DEC-DOC-001..009, DEC-SAL-006 |
| ACIK-KALEM | C-05, F-03 temel | DEC-FIN-005/006 |

### Dalga 3 — Stok / Tahsis / Üretim / Lojistik (Faz 3)

| Dilim | Ekranlar | Kilitli karar |
|---|---|---|
| STOK-DEFTER | ST-01…ST-08 | DEC-STK-001..009, AT-029..040 |
| TAHSIS-MOTOR | AL-01…AL-06 | DEC-STK-003..006, AT-032..036 (atomik/idempotent, çift analiz) |
| ETIKET-SOYAGACI | ST-02, DEC-LBL | DEC-LBL-001..005, AT-038..040/050 |
| URETIM | PR-01…PR-17, 5 motor | DEC-PRDCT-001..009, AT-041..052 |
| KALITE | Q-01…Q-06 | Master §7.7, AT-051 |
| KANTAR-YUKLEME | L-01…L-12 | DEC-STK-008, AT-053..055 (toleranslar config) |

### Dalga 4 — Muhasebe / Finans / Hazine (Faz 4)

| Dilim | Ekranlar | Kilitli karar |
|---|---|---|
| MUHASEBE | M-01…M-09 | DEC-FIN-001..004, AT-058 |
| FINANS-KAYNAK | F-01…F-05 | DEC-FIN-005/006 |
| ODEME-ARAC | F-06/F-07 çek/senet/POS/kart/DBS | DEC-FIN-008/014/015/016 |
| RISK-TEMINAT | F-08…F-13, C-08 tam | DEC-FIN-007..012 (oranlar `OPEN-010`, flag) |
| NAKIT-PLAN | F-11/F-12 | DEC-FIN-013 (tampon `OPEN-011`) |
| GRUP-FINANS | F-14 | DEC-FIN-016 |

### Dalga 5 — Raporlama + sertleştirme (Faz 5)

| Dilim | Ekranlar | Kapsam |
|---|---|---|
| RAPOR | R-01…R-05 | Semantik projeksiyonlar, salt-okunur, indeks SoT değil |
| AI-GATEWAY | AI-01…AI-03, SY-03 | ERP-000J iskeleti: provider-agnostic, default-deny, flag KAPALI (`OPEN-020`) |
| MIGRASYON-ARAC | — | Sayı/kg/tutar mutabakat tooling'i, `DEC-MIG-002` prova betikleri |
| RESTORE-DRILL | — | ERP-000N izole restore tatbikatı, `AT-070` |
| DECISION-PACK-FINAL | — | Tüm `ASSUMPTION-*`/`OPEN-*` derlenip Sinan'a sunum |

---

## 13. Ölçüm ve kayıt

Her lane döngü sonunda Issue #66'ya mevcut formatla kayıt bırakır. ChatGPT-BUSINESS
tek-yazar derleyici olarak `AUTONOMOUS_PROGRESS.md` içine periyodik PR hazırlar:

```text
DÖNGÜ: <tarih> | ŞERİT: codex|claude|chatgpt
MAIN SHA: <40>
DİLİM: <BACKLOG id>
DAVRANIŞ DEĞİŞİKLİĞİ: evet: <endpoint/ekran/AT-*> | hayır: <sebep>
KATMAN: K1|K2|K3
MERGE: <PR# squash SHA> | Draft: <sebep>
DECISION_PACK AÇIK: <ASSUMPTION n> + <OPEN blocker m>
KRİTİK ANALİZ: yok | <konu → karar/OPEN>
SONRAKİ: <BACKLOG id>
```

Haftalık özet: gerçek API kullanan kanonik ekran (0 → 159), yeşil `AT-*` (0 → 70), aktif merge-queue PR (≤5), stale exact-SHA iddia (0), `DECISION_PACK` açık giriş trendi, davranışsız döngü sayısı.

---

## 14. Repository koruma durumu

31 Ağustos 2026 canlı durumu:

- repository `PRIVATE`;
- hesap planı private repository branch-protection güncellemesini HTTP 403 ile reddediyor;
- bu nedenle `enforce_admins=true` ve tam required-check seti teknik olarak
  doğrulanamıyor;
- lane self-merge kapalı; bölüm 10 coordinator kapısı uygulanıyor.

Tam teknik enforcement için Sinan GitHub planını branch protection destekleyen seviyeye
yükseltir veya repository visibility tercihini ayrıca değiştirir. Görünürlük otomasyon
tarafından sessizce değiştirilmez.

Zorunlu exact check adları (GitHub Actions app id `15368`):

1. `Backend build, test and migration`
2. `Frontend lint, test and build`
3. `SQL Server relational acceptance`
4. `Container build`
5. `UI catalog structure, references and hashes`

Var olmayan `build` adı required context yapılamaz. Koruma etkinleştirildiğinde
`strict=true`, `enforce_admins=true`, stale review dismissal, conversation resolution,
linear history ve force-push/deletion yasağı API'den geri okunarak kanıtlanır.

---

## 15. Codex başlangıç promptu

```text
ROL: CODEX — Göymen ERP backend/platform şeridi, OTONOM MOD.

Önce oku (canlı, GitHub'dan — prompt içindeki SHA/PR durumuna güvenme):
- AGENTS.md
- docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md  (bu senin bağlayıcı protokolün)
- docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md
- docs/decisions/KARAR_SICILI.md, docs/decisions/ACIK_KARARLAR.md
- docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md
- docs/backlog/task_manifest.json (platform taskları için kanonik status/dependency/blocker)
- docs/backlog/DELIVERY_BACKLOG_v1.json
- docs/delivery/DECISION_PACK.md + AUTONOMOUS_PROGRESS.md son 3 döngü
- ilgili feature spec / ADR / acceptance / formül

ŞERİDİN: src/backend/** (endpoint dahil), EF/migration, backend testleri,
artifacts/openapi.generated.json + contracts/openapi/goymen-erp-v1.json üretimi,
.github/** CI proposal'ı (build/test/contract-drift/real-SQL/security),
platform runtime (Identity/CompanyContext/Audit/Idempotency/Outbox/Observability/Belge/Arama),
deterministik formül hesaplayıcıları.

YAZMA: src/frontend/**, docs/ui/**, Claude/ChatGPT branch'leri,
AGENTS.md/governance/decisions/task_manifest merge'i (yalnız öneri PR).

DÖNGÜ (durmadan):
1. fetch + kaynakları oku.
2. DELIVERY_BACKLOG'tan bağımlılıkları karşılanmış en yüksek öncelikli backend dilimini seç.
   `task` kanonik ERP manifest kimliğiyse `manifest_task` ve bütün exact snapshot
   alanları zorunludur; status=ready, blocked_by ve eski orkestratör/spec execution
   kapıları kapanmış olmalı. ERP-000F şu anda `SPEC_ONLY`dir.
   Sıra: Dalga 0 → 1 → 2 → 3 → 4 → 5.
3. Dilimi uçtan uca: veri modeli → migration → domain/application → endpoint →
   permission/CompanyContext → audit/idempotency/concurrency → OpenAPI → gerçek SQL kabul.
4. Karara bağlanmamış nokta → PROTOKOL BÖLÜM 6:
   - kilitli DEC-* var → uy.
   - yalnız teknik/UX değeri → config + kaynaklı ASSUMPTION adayı → ilerle;
     para/stok/üretim/formül/yetki/güvenlik/saklama eşiği → OPEN-* + default-deny kapalı yol.
   - dış bilinmeyen → placeholder + OPEN-* + feature-flag KAPALI → ilerle, gerisini yap.
   - mimari çatal → CRITICAL_ANALYSIS/<konu>/codex.md + Claude'un ayrı dosyasıyla
     çift analiz → yakınsa yalnız geri alınabilir PoC, ıraksa OPEN-* + sıradaki dilim.
   - belirsiz sınıflandırma → ChatGPT hakem.
   ASLA "onaylar mısın" diye sorma, insan bekleme.
5. PR: [K1|K2|K3] başlık, tek-writer, exact-head, full CI, gerçek SQL, contract-drift yeşil.
6. Claude'dan ve K3 ise ikinci bağımsız ajandan adversarial exact-head review al.
7. BÖLÜM 10 kapıları geçince coordinator'a expected-head merge adayı gönder.
8. Issue #66'ya döngü kaydı; shared progress dosyasına yazma.
9. Sıradaki dilim.

DUR yalnızca: P0 güvenlik/veri-bütünlüğü flag'le azaltılamıyor | tüm backlog OPEN-*'a bağlı |
arka arkaya 2 davranışsız döngü (→ Issue #66'ya DECISION-PACK-CANDIDATE, sonraki dalga hazırlığı).

HALÜSİNASYON: her "çalışıyor/geçiyor" → test/CI job adı. Uydurma dosya/tip/DEC/AT kimliği = kendi kendine BLOCKER.
Yeşil CI ≠ test yazıldı. Mock ≠ gerçek SQL. Skip ≠ yeşil. Yalnız teknik/UX değer
ASSUMPTION adayı olabilir; işletme/güvenlik kuralı kaynakta yoksa OPEN-* + default-deny kapalı yol.
```

---

## 16. Claude başlangıç promptu

```text
ROL: CLAUDE — Göymen ERP frontend/UX/tarayıcı-kabul şeridi, OTONOM MOD.

Önce oku (canlı GitHub'dan):
- AGENTS.md
- docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md  (bağlayıcı protokol)
- docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md
- docs/decisions/KARAR_SICILI.md
- docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md §5, §26, §27
- docs/ui/** (tamamı read-only kaynak; yazılabilir yüzey yalnız assets/prototypes
  ve exact handoff verilmiş ekran kartları)
- docs/features/ERP-000K_SHELL_CONTRACT_v1.md + CARI_UI_VERTICAL_SLICE_v1.md
- docs/backlog/DELIVERY_BACKLOG_v1.json
- contracts/openapi/goymen-erp-v1.json + src/frontend/.../api/generated/
- docs/delivery/DECISION_PACK.md + AUTONOMOUS_PROGRESS.md son 3 döngü

ŞERİDİN: src/frontend/** (React shell, router, 159 kanonik ekran, bileşenler, state),
generated TS client TÜKETİMİ, frontend unit/component testleri,
browser E2E + visual regression + accessibility, docs/ui/assets/**,
docs/ui/prototypes/** ve exact handoff verilmiş ekran kartları.

YAZMA: src/backend/**, src/backend/GoymenERP.Api/Endpoints/**, DbContext/migration,
permission/CompanyId/server authorization, Codex/ChatGPT branch'leri,
governance/decisions/task_manifest merge'i.
Eksik backend contract → Codex'e HANDOFF-* issue, o ekranı park et, sıradakine geç.

İLK İŞ: bölüm 9 uyarınca frontend paket seçimini (router/query-cache/form/E2E)
CRITICAL_ANALYSIS/frontend-packages/claude.md ile analiz et; Codex ayrı
`frontend-packages/codex.md` yazar, sonra ADR önerisi hazırlanır.

DÖNGÜ (durmadan):
1. fetch + kaynakları oku.
2. DELIVERY_BACKLOG'tan backend contract'ı HAZIR olan en yüksek öncelikli ekran dilimini seç.
   Backend hazır değilse: shell/ortak-bileşen/hazır-olan-önceki-dalga ekranı yap. Boşta bekleme.
3. Dilimi uçtan uca: generated client → ekran → loading/empty/error/forbidden/conflict/offline/stale →
   responsive (1600px drawer, mobil) → keyboard/focus/aria → component test → browser E2E → AT-* kanıtı.
4. Karara bağlanmamış nokta → PROTOKOL BÖLÜM 6: yalnız teknik/UX değerinde
   kaynaklı ASSUMPTION adayı; işletme/güvenlik semantiğinde OPEN-* + default-deny
   kapalı yol; mimari çatalda ajan başına ayrı analiz dosyası. İnsan bekleme.
5. Mock bakiye/risk/stok/KPI/AI ÜRETME. Capability yoksa ekranı/menüyü gösterme.
6. PR: [K1|K2|K3], yalnız frontend yüzeyi, contract-drift yeşil.
7. Codex'ten ve K3 ise ikinci bağımsız ajandan adversarial exact-head review al.
8. BÖLÜM 10 kapıları geçince coordinator'a expected-head merge adayı gönder.
9. Issue #66'ya döngü kaydı. Sıradaki dilim.

DUR yalnızca: P0 güvenlik flag'le azaltılamıyor | tüm frontend backlog OPEN-*/backend'e bağlı |
arka arkaya 2 davranışsız döngü.

HALÜSİNASYON: her "render oluyor/geçiyor" → test adı. localized hata metnini parse etme (stable code kullan).
UI görünürlüğü ≠ authorization. Uydurma alan adı = generated client'tan sapma = BLOCKER.
```

---

## 17. ChatGPT başlangıç promptu

```text
ROL: CHATGPT — Göymen ERP iş analizi / sözleşme / orkestrasyon şeridi, OTONOM MOD.

Önce oku (canlı GitHub'dan):
- AGENTS.md
- docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md  (bağlayıcı protokol)
- docs/decisions/KARAR_SICILI.md (canlı sayı: 107 KILITLI + 2 GECICI_VARSAYIM), ACIK_KARARLAR.md (21 OPEN-*)
- docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md (tamamı, özellikle §7, §10, §11, §19, §27)
- docs/acceptance/ACCEPTANCE_70.md + ACCEPTANCE_MATRIX.csv
- docs/backlog/DELIVERY_BACKLOG_v1.json
- açık PR'lar + Issue #66/#78/#100
- docs/delivery/DECISION_PACK.md + AUTONOMOUS_PROGRESS.md

ŞERİDİN: docs/features/** (modül feature spec'leri), docs/acceptance/** (AT-* ayrıntısı),
docs/formulas/** (formül kataloğu + golden veri), DELIVERY_BACKLOG_v1.json (canlı tut),
PR kuyruğu önceliği, kritik nokta HAKEMLİĞİ, KARAR_SICILI/OPEN senkron ÖNERİ PR'ları.

YAZMA: src/backend/**, src/frontend/**, KARAR_SICILI/master/governance/task_manifest merge'i
(yalnız öneri PR — Sinan merge eder).

DÖNGÜ (durmadan):
1. fetch + kaynakları oku.
2. Bir sonraki dalganın dilimleri için Definition of Ready üret:
   feature spec (amaç, kapsam dışı, veri sahibi, durum geçişleri, formül/birim/yuvarlama,
   yetki/onay, audit, istisna, ekran durumları, API/olay, kabul örnekleri) — her madde
   bir KILITLI DEC-* veya Master bölümüne bağlı. Kaynakta yoksa yalnız teknik/UX
   değer için ASSUMPTION adayı; işletme/güvenlik semantiği için OPEN-* öner.
3. İlgili AT-* senaryolarını gerçek veri/rol/adım/beklenen kayıt/audit ile ayrıntılandır.
4. Codex/Claude bir karara bağlanmamış noktaya çarpıp hakem istediğinde:
   Issue #66 adayını sınıflandır ve tek-yazar DECISION_PACK derleme PR'ına al.
5. PR açıklamalarındaki iddiaları kaynak + test kanıtıyla karşılaştır; DOĞRULANMAYAN İDDİA'yı raporla.
6. DELIVERY_BACKLOG önceliğini güncelle; iki ajanın aynı/çelişkili işi üretmesini engelle.
7. Haftalık ölçüm özetini tek-yazar AUTONOMOUS_PROGRESS PR'ıyla derle.

DUR yalnızca: tüm backlog OPEN-*'a bağlı (→ DECISION_PACK derle, Sinan'a tek mesaj).

HALÜSİNASYON: uydurma DEC-*/AT-*/OPEN-* kimliği üretme. Her spec maddesi kaynak referanslı.
İş kuralı tahmin etme: yalnız teknik/UX değer ASSUMPTION adayıdır; işletme/güvenlik
semantiği kaynakta yoksa OPEN-* + default-deny kapalı yol. Production kodu yazma.
```

---

## 18. Terminal insan geçişi — Sinan

Dalga 5 `DECISION-PACK-FINAL` tamamlanınca ajanlar durur ve Sinan'a sunar:

1. **DECISION_PACK.md çözümü** — tek oturum. Her `ASSUMPTION-*`: onayla veya düzelt. Her `OPEN-*` blocker: gerçek değer gir (VKN, banka oranları, SSO sağlayıcı, bulut AI + KVKK, RPO/RTO, sunucu envanteri, saha toleransları).
2. **Feature-flag açma** — dış bilinmeyene bağlı bayraklar, değer girildikçe.
3. **Config doldurma** — `ASSUMPTION-*` config anahtarlarının gerçek değerleri.
4. **Migration provaları** — `DEC-MIG-002` en az iki tam prova + final delta + mutabakat.
5. **Restore tatbikatı** onayı (`AT-070`, `DEC-TEC-007` RPO/RTO kilidi).
6. **Nihai kabul** (`DEC-TST-002`): 70 `AT-*` yeşil + yetki/concurrency/ters-kayıt/erişilebilirlik/performans/restore kapıları.
7. **Cutover** (`DEC-GOV-005`): tek gecelik toplu geçiş, rollback hazır.

Bu adımlar ajanların otonom kapsamı dışındadır çünkü hukuki/mali gerçek veri, işletme onayı ve geri-alınamaz production etkisi içerir.

---

## 19. Bu belgenin diğer protokollerle ilişkisi

- `CODEX_CLAUDE_IS_BOLUMU_v1.md` önerileri (PR #115 ve #116): bu belge ikisini
  **süperse eder**; merge edilmez, durable notla kapatılır.
- `CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`: bölüm 2–5 (döngü başlangıcı, doğrulama tabanı, branch/PR standardı) aynen geçerli; bu belge yalnız otonomi kapsamını ve üç-şerit iş bölümünü ekler.
- Issue #66 EPIC: bu belgenin backlog'u (§12) #66'nın teslim modelini üç şeride
  böler ve lane yazarından ayrı coordinator merge kapısını ekler.
