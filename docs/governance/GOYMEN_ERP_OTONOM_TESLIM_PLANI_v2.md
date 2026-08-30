# Göymen ERP — Codex / Claude / ChatGPT Otonom Teslim Planı v2.0

- Durum: **KULLANICI ONAYI BEKLİYOR** — onaylandığında bağlayıcı çalışma protokolü
- Hedef konum: `docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md`
- İlişki: `CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md` ve `CODEX_CLAUDE_IS_BOLUMU_v1.md` (PR #116) belgelerini **birleştirir ve süperse eder**. Çelişkide bu belge geçerlidir.
- Kaynak önceliği değişmez: son kullanıcı kararı > `docs/decisions/KARAR_SICILI.md` > `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` > feature spec/ADR > StokERP yalnız referans.
- `AGENTS.md` mimari değişmezleri ve PR kabul kapısı aynen geçerlidir.

Bu belge, üç yapay zekâ ajanının (Codex, Claude, ChatGPT) **her PR'da insan kararı beklemeden** Göymen ERP'yi teslim noktasına taşımasını sağlayan otonom çalışma protokolüdür. İnsan katılımı iki noktaya iner: (1) tek seferlik kurulum, (2) sonda tek `DECISION_PACK` geçişi + production aktivasyonu.

---

## 1. Yetki devri (autonomy grant)

Sinan Göймен, bu belgeyi onaylayarak aşağıdaki **duran yetkiyi** verir:

1. Ajanlar, bölüm 10'daki tüm makine kapıları + çift çapraz review geçtiğinde `main` dalına **kendileri merge edebilir**. Her PR için ayrı insan onayı gerekmez.
2. Ajanlar, karara bağlanmamış noktalarda bölüm 6'daki **kritik nokta analiz protokolünü** uygular; durmak yerine güvenli varsayım + `ASSUMPTION-*` kaydı + feature-flag ile ilerler.
3. Ajanlar `.github/**` CI/CD workflow'larını bölüm 12 Dalga 0 kapsamında düzenleyebilir (kapsam: build/test/contract-drift/real-SQL/security tarama; deployment/secret/production environment hariç).
4. Bu yetki `main` → production **aktivasyonunu kapsamaz**. Production activation, cutover ve gerçek hukuki/mali değer girişi Sinan'a aittir (bölüm 18).

Yetki geri alınabilir: Sinan herhangi bir anda "otonom dur" der; ajanlar mevcut PR'ları Draft'a çeker ve bekler.

---

## 2. "Teslim noktası" tanımı — dürüst kapsam

Ajanlar aşağıdaki duruma **insan kararı olmadan** ulaşır:

- Dalga 0–5 (bölüm 12) özellik-tam: 12 modül, ~160 kanonik ekran, kabul edilmiş `DEC-*` kararları uygulanmış.
- 70 kabul senaryosu (`AT-001…AT-070`) gerçek SQL Server + tarayıcı kanıtıyla yeşil.
- CI tamamen yeşil; contract-drift sıfır; P0/P1 açık hata yok.
- Staging ortamına deploy edilebilir durumda (build-once artefakt + migration + rollback manifesti).
- Gerçek dış bilgi (hukuki unvan/VKN, banka haircut oranı, SSO sözleşmesi, bulut AI sağlayıcısı, kesin RPO/RTO, sunucu envanteri, saha tolerans değerleri) gerektiren her şey **feature-flag kapalı + placeholder** ile hazır; `DECISION_PACK.md`'de listeli.

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
- `artifacts/openapi.json` üretimi ve backend-contract doğrulama tooling'i
- platform runtime: Identity, CompanyContext, Audit, Idempotency, Outbox/Inbox, Observability, Belge kasası, Global arama
- `.github/**` CI/CD workflow'ları (bölüm 1.3 kapsamı)
- deterministik formül hesaplayıcıları (`Modules/**/Calculation`) — golden testli

Branch öneki: `codex/`

### 3.2 Claude — Frontend, UX ve tarayıcı kabul şeridi

Tek-yazar alanı:

- `src/frontend/**` — React shell, router, layout, tüm ~160 ekran, page composition, feature-mount consumer
- generated TypeScript API client'in tüketimi (`src/frontend/goymen-erp-web/src/api/generated/`)
- ortak list/360/form/workspace/drawer/modal bileşenleri ve durum katmanları
- frontend unit/component testleri
- browser E2E, visual regression, accessibility testleri (test paketi seçimi bölüm 9)
- `docs/ui/**`

Branch öneki: `claude/`

Claude backend endpoint, DbContext, migration, permission veya server authorization kodu yazmaz. Eksik contract için Codex'e `HANDOFF-*` issue açar ve o dilimi park edip sıradaki ekrana geçer.

### 3.3 ChatGPT — İş analizi, sözleşme ve orkestrasyon şeridi

Tek-yazar alanı:

- `docs/features/**` — modül bazlı feature spec'ler (127 kilitli `DEC-*` + Master Rapor §7/§27'den türetilir)
- `docs/acceptance/**` — `AT-001…AT-070` senaryolarının gerçek veri/rol/adım/beklenen kayıt/audit ayrıntısı
- `docs/formulas/**` — formül kataloğu + golden test veri setleri (değerler `DEC-PRD-009`/master §10; belirsizse `ASSUMPTION-*`)
- `docs/backlog/DELIVERY_BACKLOG_v1.json` — dalga/dilim backlog'unun canlı tutulması
- PR kuyruğu önceliklendirmesi
- **kritik nokta analiz hakemliği** (bölüm 6): bir ajan karara bağlanmamış noktaya çarptığında ChatGPT "güvenli varsayım" mı yoksa "gerçek `OPEN-*` blocker" mı olduğunu belirler
- `docs/decisions/KARAR_SICILI.md` / `ACIK_KARARLAR.md` senkron **öneri** PR'ları (merge Sinan)

Branch öneki: `chatgpt/`

ChatGPT production source (backend/frontend) yazmaz.

### 3.4 Sinan — Kurulum + terminal geçiş

Yalnız iki dokunuş:

1. **Tek seferlik kurulum** (bölüm 14): repo private + branch protection + required checks + bu belgeyi onay.
2. **Terminal geçiş** (bölüm 18): `DECISION_PACK.md` çözümü + gerçek değer girişi + feature-flag açma + cutover.

`AGENTS.md`, `docs/governance/**`, `docs/decisions/**`, `docs/master/**`, `docs/backlog/task_manifest.json` semantic sahibi Sinan'dır; ajanlar yalnız öneri PR açar.

---

## 4. Kesin dosya sahipliği

| Yol / yüzey | Yazar | Diğer ajan |
|---|---|---|
| `src/backend/**` (endpoint dahil) | Codex | read-only exact-head reviewer |
| `tests/GoymenERP.*.UnitTests/**`, `*.ArchitectureTests/**`, `*.Api.IntegrationTests/**` | Codex | reviewer |
| `.github/**` (bölüm 1.3 kapsamı) | Codex | reviewer |
| `src/frontend/**` | Claude | reviewer |
| kabul edilmiş browser/E2E test kökü | Claude | reviewer |
| `docs/ui/**` | Claude | reviewer |
| `docs/features/**`, `docs/acceptance/**`, `docs/formulas/**` | ChatGPT | reviewer |
| `docs/backlog/DELIVERY_BACKLOG_v1.json` | ChatGPT | reviewer |
| `docs/delivery/AUTONOMOUS_PROGRESS.md`, `docs/delivery/DECISION_PACK.md` | append-only, üç ajan da yazar | — |
| `docs/delivery/CRITICAL_ANALYSIS/**` | analizi yapan ajan(lar) | — |
| `AGENTS.md`, `docs/governance/**`, `docs/decisions/**`, `docs/master/**`, `task_manifest.json` | Sinan onaylı proposal-only | öneri PR |

Bir dosyanın tek yazarı vardır. Bir PR sahibinin dışındaki production dosyasını içeriyorsa (`git diff --name-only origin/main` karşı yüzeyden dosya gösterirse) PR otomatik bölünür. Karşı ajan aynı PR'a commit atmaz; review sonucu + `HANDOFF-*` issue üretir.

---

## 5. Otonom döngü — her şerit için

Her ajan, kendi şeridinde **durmadan** şu döngüyü çalıştırır:

1. `git fetch --all --prune`. Canlı `main`, açık PR'lar, worktree durumu doğrula.
2. Şu kaynakları oku: `AGENTS.md`, bu belge, `CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`, `KARAR_SICILI.md`, `GOYMEN_ERP_MASTER_RAPOR_V3.md`, `DELIVERY_BACKLOG_v1.json`, ilgili feature spec/ADR/acceptance/formül/UI kartları, `DECISION_PACK.md`, `AUTONOMOUS_PROGRESS.md` son 3 döngü.
3. `DELIVERY_BACKLOG_v1.json` içinden **bağımlılıkları karşılanmış en yüksek öncelikli** dikey dilimi kendi şeridin için seç.
4. Dilimi kendi şeridinde uçtan uca üret (bölüm 12 teslim modeli).
5. Karara bağlanmamış noktaya çarparsan → **bölüm 6 kritik nokta protokolü**. Durma; ilerle veya sıradaki dilime geç.
6. PR aç: tek-writer, exact-head, `[K1|K2|K3]` başlık, full CI, gerektiğinde gerçek SQL, contract-drift yeşil.
7. Karşı ajandan **adversarial exact-head review** al (bölüm 8 şablonu).
8. Bölüm 10 kapıları geçince **kendin merge et** (expected-head squash).
9. `AUTONOMOUS_PROGRESS.md`'ye döngü kaydı + `DAVRANIŞ DEĞİŞİKLİĞİ` satırı yaz.
10. Sıradaki dilim. Bağımlılıkları karşılanmış dilim varken **durma**.

**Paralellik:** üç şerit aynı anda çalışır. Codex Dalga N backend'i yaparken Claude Dalga N-1 ekranlarını, ChatGPT Dalga N+1 spec'lerini üretir. Buluşma yüzeyi yalnız OpenAPI (bölüm 9).

---

## 6. Kritik nokta analiz protokolü — "sor ve dur"un yerine

Bir ajan karara bağlanmamış bir noktaya çarptığında, **şu sırayla** değerlendirir ve **ilerler**:

### Adım 1 — Kilitli karar var mı?

127 `KILITLI` `DEC-*` + Master Rapor §7/§10/§11 + kabul edilmiş ADR/feature spec taranır. Kapsıyorsa: **karara uy, ilerle.** Kayıt gerekmez.

### Adım 2 — Değer / eşik / tolerans mı?

Örn. kantar toleransı, rezerv süresi, fiyat marj eşiği, debounce ms, yuvarlama basamağı.

- **Yapılandırılabilir politika tablosu** kur (kod içine sabit sayı gömme — `AGENTS.md` yasağı).
- Varsayılanı Master Rapor'daki geçici varsayımdan al (örn. `DEC-STK-005` rezerv 3 gün, `DEC-PRDCT-005` debounce 300–500 ms, `DEC-FIN-010` önceki iş günü TCMB satış).
- `DECISION_PACK.md`'ye `ASSUMPTION-NNN` ekle: ne varsayıldı, hangi `DEC-*`/master bölümünden, hangi config anahtarı, yanlışsa ne kırılır.
- **İlerle.** Feature-flag gerekmez; değer config'ten gelir.

### Adım 3 — Gerçek dış bilinmeyen mi?

Örn. gerçek VKN, banka teminat haircut oranı, SSO sağlayıcı adı, bulut AI endpoint'i, sunucu RAID yapısı.

- Placeholder + `OPEN-*` referansı kullan (`OPEN-001` VKN, `OPEN-010` haircut, `OPEN-013` SSO, `OPEN-020` bulut AI...).
- Bu değeri production'da kullanacak her yol **feature-flag KAPALI** açılır.
- `DECISION_PACK.md`'ye `OPEN-*` blocker satırı: Sinan ne sağlamalı, hangi bayrak açılacak, hangi ekran/akış bekliyor.
- **Diğer her şeyi inşa etmeye devam et.** Örn. risk motoru: haircut oranı 0 varsayımıyla tam çalışır; gerçek oran girilince config güncellenir.

### Adım 4 — Mimari / güvenlik çatalı, güvenli varsayılan yok mu?

Örn. event-sourced stok defterinin partition stratejisi, tahsis motoru concurrency modeli, çok-şirket okuma cache mimarisi.

- **Çift-ajan adversarial analiz**: iki ajan (Codex + Claude, veya konuya göre Codex + ChatGPT) bağımsız analiz yapar. Bulgular `docs/delivery/CRITICAL_ANALYSIS/<konu>-<tarih>.md` içine yazılır: seçenekler, her birinin `DEC-*`/`AGENTS.md` uyumu, güvenlik/performans/geri-alınabilirlik etkisi, öneri.
- **Yakınsarsa** (iki analiz + kaynak taraması aynı sonuca): dokümante edilmiş gerekçeyle **ilerle**. `AUTONOMOUS_PROGRESS.md`'ye `KRİTİK ANALİZ: <konu> → <karar>` satırı.
- **Iraksarsa**: `OPEN-*` öner (ChatGPT), bu dilimi park et, **sıradaki bağımsız dilime geç**. Asla boşta bekleme.

### Adım 5 — İş kuralı uydurma yasağı

Kaynaklarda olmayan rol adı, onay eşiği, oran veya politika hiçbir zaman tahminle kodlanmaz. Adım 2 veya 3'e düşer. Bu kural `AGENTS.md`'den devralınır ve gevşetilmez.

### Hakemlik

Adım 2 mi Adım 3 mü belirsizse: **ChatGPT hakem**dir. ChatGPT `DECISION_PACK.md`'ye sınıflandırmayı yazar; kod yazan ajan ona uyar.

---

## 7. DECISION_PACK.md mekanizması

Tek dosya (`docs/delivery/DECISION_PACK.md`). Ajanlar çalışırken buraya **append** eder. Sonda Sinan tek oturumda çözer.

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

`AUTONOMOUS_PROGRESS.md` her döngüde `DECISION_PACK` açık giriş sayısını raporlar. Sayı, teslim yakınlığının ölçüsüdür.

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
ASSUMPTION KAYDI: tam | eksik  (ASSUMPTION/OPEN girişi DECISION_PACK'te mi + flag doğru mu)
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

- **Codex**: exact backend SHA'dan deterministik `artifacts/openapi.json`; authorization/CompanyContext bilgisini istemciye yetki kaynağı olarak açmaz; her endpoint/DTO/error değişikliği contract testine bağlı.
- **Claude**: TS tiplerini elle yazmaz; pinlenmiş generator ile `src/frontend/goymen-erp-web/src/api/generated/` üretir ve commit eder; generated dosyayı elle düzenlemez; UI branching'i localized metin değil stable `code` üzerinden yapar.
- **CI `contract-drift` job'ı**: backend build → OpenAPI yeniden üret → TS client yeniden üret → committed çıktı ile karşılaştır → fark = kırmızı.
- Tek taraflı endpoint veya elle DTO yasak. Contract değişikliği isteyen taraf karşıya `HANDOFF-*` issue açar.

Bu mekanizma, iki ajanın birbirinin alan adını uydurmasını (bu projedeki en olası halüsinasyon) derleyici seviyesinde bitirir.

**Frontend paket seçimi** (router, query/cache, form, grid, browser-E2E): `ERP-000K_SHELL_CONTRACT_v1.md` §19 gereği K3. Claude aday paketleri + lisans/security/supply-chain/TCO/React-uyum analizini `CRITICAL_ANALYSIS/frontend-packages-<tarih>.md`'ye yazar; Codex adversarial review eder; yakınsarsa seçim kilitlenir ve ADR önerisi açılır. Playwright veya herhangi bir paket bu analizden önce varsayılan değildir.

---

## 10. Otonom merge kapıları

Bir PR `main`e ajan tarafından merge edilebilir **ancak ve ancak** hepsi sağlanırsa:

1. Fresh standart CI tamamen yeşil (build, format/diff, unit, integration, architecture guard, frontend lint/test/build, container build).
2. Gereken yerde **gerçek SQL Server relational acceptance** yeşil (mock/InMemory yeterli değil).
3. `contract-drift` job'ı yeşil.
4. Karşı ajan review'u `HANDOFF-ACCEPTED` (bölüm 8 şablonu, exact head).
5. Yeni davranışın **kırmızıdan yeşile dönen** testi var; skip sayısı PR'da yazılı.
6. P0/P1 açık bulgu yok.
7. PR bir `ASSUMPTION-*` veya `OPEN-*`'a dokunuyorsa: `DECISION_PACK.md` girişi tam **ve** ilgili feature-flag'in durumu doğru (dış bilinmeyen → kapalı).
8. `AUTONOMOUS_PROGRESS.md` döngü kaydı + `DAVRANIŞ DEĞİŞİKLİĞİ` satırı eklendi.
9. K3 ise: **iki ajan** imzası (Codex + Claude/ChatGPT) + `CRITICAL_ANALYSIS` dosyası (gerekliyse).

Kapılardan biri eksikse merge yok; PR Draft'a çekilir veya düzeltilir. Branch protection kurulmadıysa (bölüm 14) ajanlar bu kapıları **kendileri denetler** ve `gh pr merge --squash` ile ilerler; kurulduysa GitHub zorlar.

---

## 11. Durma koşulları — yalnız üç

1. **P0 güvenlik / veri bütünlüğü bulgusu**, feature-flag arkasında güvenle azaltılamıyor → o şeridi durdur, `DECISION_PACK.md`'ye `P0-BLOCKER` yaz, **diğer iki şerit devam eder**.
2. **Kalan backlog'un tamamı `OPEN-*`'a bağlı** → `DECISION_PACK.md` derlenir, Sinan'a "şu N karar kilit" tek mesajı gider. Ajanlar bekler.
3. **Bir şeritte arka arkaya iki davranışsız döngü** → o şerit, hangi `OPEN-*`'ın kilit olduğunu tek paragrafta `DECISION_PACK.md`'ye yazar, sonraki dalganın hazırlık işine (spec okuma, fixture) geçer. Üçüncü davranışsız döngü başlamaz.

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
| ERP-000F | Transactional outbox / inbox / dedup / retry-backoff |
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

`AUTONOMOUS_PROGRESS.md` her döngü sonunda (mevcut format + ek):

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

Haftalık özet: gerçek API kullanan kanonik ekran (0 → ~160), yeşil `AT-*` (0 → 70), aktif merge-queue PR (≤5), stale exact-SHA iddia (0), `DECISION_PACK` açık giriş trendi, davranışsız döngü sayısı.

---

## 14. Tek seferlik kurulum — Sinan

Otonom döngü başlamadan **bir kez**:

```bash
# 1. Repo'yu bilinçli değilse private yap
gh repo edit sinangym/GoymenERP --visibility private

# 2. main branch protection
gh api -X PUT repos/sinangym/GoymenERP/branches/main/protection \
  -F "required_status_checks[strict]=true" \
  -F "required_status_checks[contexts][]=build" \
  -F "enforce_admins=false" \
  -F "required_pull_request_reviews[required_approving_review_count]=0" \
  -F "restrictions=null" \
  -F "allow_force_pushes=false" -F "allow_deletions=false"
```

3. Bu belgeyi (`GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md`) merge et.
4. `enforce_admins=false` bilinçli: ajanlar `sinangym` hesabıyla, bölüm 10 kapılarını kendileri denetleyerek merge eder. Sinan istediğinde `enforce_admins=true` yapıp tam manuel moda döner.

Kurulum yapılmazsa: ajanlar yine bölüm 10 kapılarını kendileri denetler ve merge eder; tek fark GitHub'ın teknik zorlaması olmaz.

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
- docs/backlog/DELIVERY_BACKLOG_v1.json
- docs/delivery/DECISION_PACK.md + AUTONOMOUS_PROGRESS.md son 3 döngü
- ilgili feature spec / ADR / acceptance / formül

ŞERİDİN: src/backend/** (endpoint dahil), EF/migration, backend testleri,
artifacts/openapi.json üretimi, .github/** CI (build/test/contract-drift/real-SQL/security),
platform runtime (Identity/CompanyContext/Audit/Idempotency/Outbox/Observability/Belge/Arama),
deterministik formül hesaplayıcıları.

YAZMA: src/frontend/**, docs/ui/**, Claude/ChatGPT branch'leri,
AGENTS.md/governance/decisions/task_manifest merge'i (yalnız öneri PR).

DÖNGÜ (durmadan):
1. fetch + kaynakları oku.
2. DELIVERY_BACKLOG'tan bağımlılıkları karşılanmış en yüksek öncelikli backend dilimini seç.
   Sıra: Dalga 0 → 1 → 2 → 3 → 4 → 5.
3. Dilimi uçtan uca: veri modeli → migration → domain/application → endpoint →
   permission/CompanyContext → audit/idempotency/concurrency → OpenAPI → gerçek SQL kabul.
4. Karara bağlanmamış nokta → PROTOKOL BÖLÜM 6:
   - kilitli DEC-* var → uy.
   - değer/eşik → config tablosu + master varsayımı + ASSUMPTION-NNN → ilerle.
   - dış bilinmeyen → placeholder + OPEN-* + feature-flag KAPALI → ilerle, gerisini yap.
   - mimari çatal → CRITICAL_ANALYSIS/<konu>.md + Claude ile çift analiz → yakınsa ilerle, ıraksa OPEN-* + sıradaki dilim.
   - belirsiz sınıflandırma → ChatGPT hakem.
   ASLA "onaylar mısın" diye sorma, insan bekleme.
5. PR: [K1|K2|K3] başlık, tek-writer, exact-head, full CI, gerçek SQL, contract-drift yeşil.
6. Claude'dan adversarial exact-head review al.
7. BÖLÜM 10 kapıları geçince kendin squash-merge et.
8. AUTONOMOUS_PROGRESS.md döngü kaydı.
9. Sıradaki dilim.

DUR yalnızca: P0 güvenlik/veri-bütünlüğü flag'le azaltılamıyor | tüm backlog OPEN-*'a bağlı |
arka arkaya 2 davranışsız döngü (→ DECISION_PACK'e tek paragraf, sonraki dalga hazırlığı).

HALÜSİNASYON: her "çalışıyor/geçiyor" → test/CI job adı. Uydurma dosya/tip/DEC/AT kimliği = kendi kendine BLOCKER.
Yeşil CI ≠ test yazıldı. Mock ≠ gerçek SQL. Skip ≠ yeşil. İş kuralı uydurma → ASSUMPTION/OPEN.
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
- docs/ui/** (SCREEN_CATALOG, CANONICAL_SCREEN_STATUS, UI_DESIGN_SYSTEM, screens/**)
- docs/features/ERP-000K_SHELL_CONTRACT_v1.md + CARI_UI_VERTICAL_SLICE_v1.md
- docs/backlog/DELIVERY_BACKLOG_v1.json
- artifacts/openapi.json + src/frontend/.../api/generated/
- docs/delivery/DECISION_PACK.md + AUTONOMOUS_PROGRESS.md son 3 döngü

ŞERİDİN: src/frontend/** (React shell, router, ~160 ekran, bileşenler, state),
generated TS client TÜKETİMİ, frontend unit/component testleri,
browser E2E + visual regression + accessibility, docs/ui/**.

YAZMA: src/backend/**, GoymenERP.Api/Endpoints/**, DbContext/migration,
permission/CompanyId/server authorization, Codex/ChatGPT branch'leri,
governance/decisions/task_manifest merge'i.
Eksik backend contract → Codex'e HANDOFF-* issue, o ekranı park et, sıradakine geç.

İLK İŞ: bölüm 9 uyarınca frontend paket seçimini (router/query-cache/form/E2E)
CRITICAL_ANALYSIS/frontend-packages-<tarih>.md ile analiz et, Codex review'undan geçir, ADR öner.

DÖNGÜ (durmadan):
1. fetch + kaynakları oku.
2. DELIVERY_BACKLOG'tan backend contract'ı HAZIR olan en yüksek öncelikli ekran dilimini seç.
   Backend hazır değilse: shell/ortak-bileşen/hazır-olan-önceki-dalga ekranı yap. Boşta bekleme.
3. Dilimi uçtan uca: generated client → ekran → loading/empty/error/forbidden/conflict/offline/stale →
   responsive (1600px drawer, mobil) → keyboard/focus/aria → component test → browser E2E → AT-* kanıtı.
4. Karara bağlanmamış nokta → PROTOKOL BÖLÜM 6 (Codex ile aynı: config+ASSUMPTION veya placeholder+OPEN+flag veya çift analiz). İnsan bekleme.
5. Mock bakiye/risk/stok/KPI/AI ÜRETME. Capability yoksa ekranı/menüyü gösterme.
6. PR: [K1|K2|K3], yalnız frontend yüzeyi, contract-drift yeşil.
7. Codex'ten adversarial exact-head review al.
8. BÖLÜM 10 kapıları geçince kendin squash-merge et.
9. AUTONOMOUS_PROGRESS.md döngü kaydı. Sıradaki dilim.

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
- docs/decisions/KARAR_SICILI.md (127 KILITLI DEC-*), ACIK_KARARLAR.md (21 OPEN-*)
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
   bir KILITLI DEC-* veya Master bölümüne bağlı. Kaynakta yoksa ASSUMPTION-NNN veya OPEN-* öner.
3. İlgili AT-* senaryolarını gerçek veri/rol/adım/beklenen kayıt/audit ile ayrıntılandır.
4. Codex/Claude bir karara bağlanmamış noktaya çarpıp hakem istediğinde:
   DECISION_PACK.md'ye sınıflandırma yaz (Adım 2 config-varsayım mı, Adım 3 dış-bilinmeyen mi).
5. PR açıklamalarındaki iddiaları kaynak + test kanıtıyla karşılaştır; DOĞRULANMAYAN İDDİA'yı raporla.
6. DELIVERY_BACKLOG önceliğini güncelle; iki ajanın aynı/çelişkili işi üretmesini engelle.
7. Haftalık ölçüm özetini AUTONOMOUS_PROGRESS.md'ye yaz.

DUR yalnızca: tüm backlog OPEN-*'a bağlı (→ DECISION_PACK derle, Sinan'a tek mesaj).

HALÜSİNASYON: uydurma DEC-*/AT-*/OPEN-* kimliği üretme. Her spec maddesi kaynak referanslı.
İş kuralı tahmin etme — kaynakta yoksa ASSUMPTION/OPEN. Production kodu yazma.
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

- `CODEX_CLAUDE_IS_BOLUMU_v1.md` (PR #116): bu belge **süperse eder**. #116 kapatılabilir veya "v2 ile değiştirildi" notuyla bırakılabilir.
- `CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`: bölüm 2–5 (döngü başlangıcı, doğrulama tabanı, branch/PR standardı) aynen geçerli; bu belge yalnız otonomi kapsamını ve üç-şerit iş bölümünü ekler.
- Issue #66 EPIC: bu belgenin backlog'u (§12) #66'nın teslim modelini üç şeride böler ve otonom merge ekler.
