# Göymen ERP — Hızlı Teslim Planı v3.0

- Durum: **KULLANICI ONAYI BEKLİYOR**
- İlişki: `GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md` (v2.1) belgesinin **rol dağılımını, dilim sırasını ve tören seviyesini** günceller. Güvenlik değişmezleri (`AGENTS.md` mimari kuralları, `KARAR_SICILI.md` önceliği, CompanyId/audit/idempotency, production aktivasyonunun insanda kalması) **aynen korunur**. Çelişkide bu belge süreç/hız tarafında, v2.1 güvenlik tarafında üstündür.
- Yazan: `CLAUDE-FRONTEND` — kullanıcı isteği (2026-08-31): "projeyi çok hızlı bitir, görev dağılımı yap, soruları sor, Claude de kod yazsın".

---

## 1. Proje ne amaçlıyor (tek paragraf)

Göймen ERP; Göymen'in çelik/metal işleme operasyonunu (Osmaniye Hol-0 Boya, Hol-1 Boru/Profil, Hol-3 Dilme/Soğuk Hadde, Hol-6 Boy Kesme + Gaziantep + harici depolar) ve birden fazla hukuki şirketini tek veri modelinde yürüten **bağımsız web ERP**'dir. StokERP'nin yerini alır. Zincir: satış talebi → sipariş → tahsis → stok → üretim (5 motor) → sevkiyat/kantar → irsaliye → fatura → açık kalem → tahsilat; paralelinde satın alma, cari, belge kasası, muhasebe taslak, finans/hazine, raporlama. Her sayfada açıklayan/taslak hazırlayan AI (yetki değil). Bir olay bir kez kaydedilir. .NET 10 + React + SQL Server, modüler monolit.

---

## 2. Neredeyiz (2026-08-31, dürüst)

| Ölçü | Değer |
|---|---|
| Backend production kod | ~13.500 satır — 5 platform modülü: Organization, Identity, Audit, Idempotency, Counterparty (domain+app+endpoint) + Shell capability endpoint |
| Backend test | ~19.900 satır |
| **Frontend kod** | **~360 satır** (Organization bootstrap görünümü; hiçbir ekran yok) |
| Docs (markdown) | ~23.900 satır (≈ backend production kodun 1.8 katı) |
| Commit dağılımı | 139 docs · 80 feat · 73 test · 71 fix · 21 refactor |
| Merged PR | 30 |
| **Kullanıcının kullanabileceği ekran** | **0 / 159** |
| Kilitli karar | 107 `DEC-*` KILITLI + 2 GECICI_VARSAYIM |
| Açık karar | 21 `OPEN-*` |
| Kabul senaryosu | `AT-001…AT-070` (taslak; çoğu "TBD" modül) |

**Platform temeli sağlam. Çıktı yok.** İlk Cari ekranı bile başlamadı.

---

## 3. Neden yavaş — kök nedenler

1. **Tören çıktının kat kat üstünde.** Ajan-ayrık `CRITICAL_ANALYSIS` dizinleri, `manifest_task` parity alanlı backlog, revision/supersedes kurallı `DECISION_PACK`, 14-maddelik generator PoC profili, K1/K2/K3 + 2× exact-head review + coordinator merge. 360 satır frontend için kurumsal-ölçek yönetişim.
2. **Her şey maksimalist adversarial review'dan geçiyor.** Var olmayan bir özelliğin analiz dokümanı 4 revizyon + 5 review turu tüketiyor. Bar "production'a çıkıyoruz" için ayarlanmış, keşfe uygulanıyor.
3. **3 ajan aynı anda çalışmıyor.** Codex/ChatGPT saatlerce sustuğunda 3-ajanlı pipeline throughput'u sıfırlanıyor.
4. **`OPEN-*` sona ertelenmiş ama dalgaları şimdi bloke ediyor.** Ajanlar 30 saniyelik soruyu sormamak için devasa placeholder-flag makinesi kuruyor.
5. **Fiiliyatta platform-first, vertical-slice değil.** Plan "dikey dilim" diyor; iş: platform hardening + contract + readiness docs.

---

## 4. v3.0 ilkesi

> **Bir çalışan dikey dilim, sonuna kadar. Tören riskle orantılı. İnsan soruyu gerçek zamanlı cevaplar. Dört rol de aynı dilimin farklı katmanında çalışır — üç ayrı hatta değil.**

---

## 5. Governance kesintileri (bunları ARTIK yapmıyoruz)

| Bırakılan | Yerine |
|---|---|
| Protokol üzerinde meta-iş | v2.1 + bu belge dondu. Yeni governance PR'ı yok (Sinan istemedikçe). |
| Pre-production'da 2× exact-head review + coordinator | **Tek review** (karşı ajan) + CI yeşil → coordinator squash-merge. Production yokken "K3 deployment riski" yok. |
| K3 kapsamı: "CompanyId/CompanyContext'e dokunan her şey" | K3 yalnız: **DB migration · para/formül/tolerans · permission/rol · cross-module contract · `.github/**` · production activation.** Shell/ekran/component/analiz = K1/K2. |
| Standalone readiness/contract dokümanları | Analiz yalnız **aynı veya bir sonraki PR'da koda dönüşüyorsa** yazılır. |
| Paket seçimi için `CRITICAL_ANALYSIS` + PoC profili | Seç + 10 satırlık ADR. React Router + TanStack Query + CSS Modules zaten kabul adayı (#118/#135). |
| `ASSUMPTION-*` / `OPEN-*` için ağır kayıt makinesi | Ajan noktaya çarpar → Issue #66'ya tek satır `NEEDS-SINAN: <soru>` yazar → sıradaki dilime geçer. Sinan günlük check-in'de toplu cevaplar. |
| Her döngüde uzun `AUTONOMOUS_PROGRESS` girişi | Issue #66'ya 3 satır: ne yapıldı / davranış değişikliği evet-hayır / sıradaki. |

**Korunan güvenlik:** `AGENTS.md` mimari değişmezleri, CompanyId her kritik kayıtta, audit/idempotency/concurrency testleri, gerçek SQL kanıtı (migration/para/formül dilimlerinde), formülün tek deterministik calculator'da olması, production activation'ın Sinan'da kalması, açık kararın sessiz production'a alınmaması.

---

## 6. Rol dağılımı — hızlı

### 6.1 Sinan — karar + terminal (aktif katılım: günde ~15 dk)

- **Tek sefer (bölüm 9 soruları):** ~12 bloke edici soruyu cevapla, geçici değerleri resmen kabul et, branch-protection kararı ver.
- **Günlük ~15 dk check-in:** Issue #66'daki `NEEDS-SINAN:` satırlarını topluca cevapla. "Şu 3 yaklaşımdan hangisi?" → anında.
- **Terminal:** production activation + cutover + `DECISION_PACK` final.

### 6.2 ChatGPT — iş spec + acceptance, **tam-zamanında** (bir dilim önde, dalga değil)

Sadece Claude/Codex'in şimdi yapacağı dilim için:
- somut acceptance senaryoları (gerçek veri, rol, adım, beklenen kayıt, audit) — `AT-*` satırlarını "TBD"den çıkar
- `KARAR_SICILI`'den o dilimin iş kuralları + alan listesi + durum geçişleri
- `DELIVERY_BACKLOG_v1.json` öncelik sırası (canlı)
- kritik nokta hakemliği: `NEEDS-SINAN` mı yoksa güvenli teknik varsayım mı
- **Yapmaz:** readiness docs, contract bundle, ileri dalga meta-analizi, çözülmüş kararı yeniden analiz.

### 6.3 Codex — backend, **dilim başına**, endpoint dahil

Şimdiki dilim için:
- domain (yeniyse) → application → **endpoint** → gerçek-SQL acceptance testi → OpenAPI güncelle
- migration, platform runtime, formül calculator'ları
- **tek sefer:** kanonik OpenAPI baseline + `contract-drift` CI job'ı; sonra sadece bakım
- Claude'un frontend PR'ını **hızlı** review eder (güvenlik merceği; maksimalist değil)
- **Yapmaz:** readiness docs, standalone contract, çözülmüş kararı yeniden analiz, frontend dosyası.

### 6.4 Claude — frontend, **EKRAN YAZAR** (artık sadece analiz değil)

- **Shell (SHELL-PILOT-01B):** router + layout + CompanyContext seçici + fence core (PR #133) + UI states (PR #136) → `App.tsx`'e wire, gerçek Organization API. React Router + TanStack Query + CSS Modules kurar.
- **Sonra ekranlar, dalga dalga:** C-01 → C-02 → C-03 → U-01/U-02 → S-01…S-04 → ilk uçtan uca.
- Codex'in generated client'ini tüketir (generator gelene kadar ince elle yazılmış client — pragmatik, `HANDOFF-*` ile işaretli).
- Ekran başına browser + a11y testi.
- Codex'in backend PR'ını **hızlı** review eder (frontend-contract merceği).
- **Yapmaz:** backend endpoint/permission/migration, `docs/ui` golden katalog semantiği (Codex+coordinator K3).

---

## 7. Paralel dilim deseni — hız kilidini açan şey

Her dikey dilim `N` için, **üç ajan aynı dilimin farklı katmanında**:

```
ChatGPT: dilim N spec (½ gün)  ──┐
                                 ├─▶  Codex: dilim N backend (domain→endpoint→SQL test→OpenAPI)
Claude: shell / dilim N-1 UI  ───┘        Claude: dilim N UI (Codex contract'ına karşı; gerekirse fixture)
                                             │
                                   karşı ajan hızlı review → coordinator merge → dilim N+1
```

Üç ajan üç alakasız hatta değil — **aynı dilimde, farklı katman.** Codex backend'i biterken Claude UI'ı bitirmiş olur; buluşma OpenAPI.

**Dur koşulları (yalnız 3):** P0 güvenlik/veri-bütünlüğü flag'le azaltılamıyor · dilimin tamamı `NEEDS-SINAN`'a bağlı · bir şeritte arka arkaya 2 davranışsız tur.

---

## 8. İlk 6 dilim (somut)

| # | Dilim | Kim | Çıktı |
|---|---|---|---|
| 1 | **SHELL** | Claude (lead) + Codex (#132 done) | Uygulama gerçek şirket dizini + capability gating ile açılıyor; mock ERP menüsü yok; company-switch stale veri göstermiyor |
| 2 | **C-01 + C-03** | 3 ajan | Cari listesi + Cari 360 gerçek API verisiyle render; yabancı şirket verisi sızmıyor (`AT-001`) |
| 3 | **C-02 + CARI-LINK** | 3 ajan | Yeni Cari oluşturma + mevcut Party'yi şirkete bağlama; idempotency (`AT-005`), çift kayıt yok |
| 4 | **U-01 + U-02** | 3 ajan | Ürün listesi + Ürün kartı (görüntüle + temel oluştur); tek kart çok rol (`DEC-PRD-001`) |
| 5 | **S-01…S-04** | 3 ajan | Satış talebi/sipariş + sipariş 360 + revizyon (`DEC-SAL-001/002`) |
| 6 | **İlk uçtan uca** | 3 ajan | Sipariş → tahsis iskeleti → satış irsaliyesi taslağı; zincir görünür |

`DELIVERY_BACKLOG_v1.json` Dalga 1-5 bunun devamı. Her dilim `≤ 5` aktif merge-queue PR.

---

## 9. Sinan'a sorular

### 9.1 Şimdi bloke eden (ilk dilimler başlamadan cevap gerekli)

1. **Şirketler (`OPEN-001`):** İki (veya kaç) hukuki Göymen şirketinin **tam unvanı, VKN'si ve CompanyCode'u** nedir? Gerçek değerleri veremiyorsan: "şu 2 placeholder ile başlayın, go-live öncesi düzeltirim" der misin — ve placeholder isimleri sen mi verirsin?
2. **Kimlik (`OPEN-013`):** Dev/staging için **yalnız yerel admin bootstrap hesabı**, OIDC/SSO sağlayıcısı sonra seçilecek — bu şekilde kod yazmamız onaylı mı?
3. **Depolar (`OPEN-002`):** `DEC-ORG-004` depo adlarını veriyor (Gaziantep, Osmaniye Hol-0…7, Baştuğ, Selimoğlu, Alya, Şadiler, Tosçelik, Doğruluk). Her birini **hangi şirket işletiyor / sahibi kim / harici mi**? Bilmiyorsan hepsi "birinci şirket, kendi malı" varsayımıyla başlayalım mı?
4. **Frontend paketler:** `react-router` + `@tanstack/react-query` + CSS Modules + React context/reducer — Codex supply-chain review'undan geçti (MIT, temiz). Bunları pinleyip 10 satırlık ADR ile geçmek onaylı mı? (Alternatif: TanStack Router — daha genç, daha çok tip güvenliği.)
5. **Branch protection:** Repo private ve `main` korumasız. GitHub planını private-repo branch protection için yükseltecek misin, yoksa coordinator prosedürel kapısıyla mı devam edelim?
6. **`dummy` commit:** `main`'de `f60540b` — içeriği "dummy" olan bir dosya var. Bilinçli mi? Değilse revert edelim.
7. **v3.0 governance:** Bu belgedeki tören kesintilerini (pre-prod'da tek review, Claude kod yazar, JIT spec, `OPEN-*` için Issue #66 tek-satır + günlük check-in) onaylıyor musun?
8. **Ajan loop'ları:** Codex + ChatGPT loop'larını sürekli çalışır tutabilir misin? Tutamazsan: "Claude kritik hattı sürer, diğer ikisi müsait oldukça yardım eder" moduna mı geçelim?

### 9.2 İlgili dalga başlamadan (birkaç hafta ertelenebilir)

9. **Üretim formülleri (`OPEN-007`):** Yoğunluk, hız, kerf, kaplama, yuvarlama, tolerans **kesin değerlerini kim verecek** ve ne zaman? (Üretim dalgasını bloke eder — Dalga 3.)
10. **Kantar toleransları (`OPEN-003/004`):** Ürün/işlem bazlı uyarı/onay/blok eşiklerini kim belirleyecek? (Lojistik/Kantar dalgası — Dalga 3.)
11. **Finans eşikleri (`OPEN-009/010/012`):** Onay tutarları/rolleri, DBS/teminat haircut oranları, kur alma saati/tatil politikası. (Finans dalgası — Dalga 4.)
12. **Muhasebe entegrasyonu (`OPEN-016`):** Resmî muhasebe/e-belge sistemine hangi sırayla, hangi erişim yöntemiyle bağlanılacak? (Muhasebe/e-belge dalgası — Dalga 4-5.)

### 9.3 Bloke etmeyen (kabul ederek geçebiliriz)

13. `OPEN-014/015/017` — sunucu envanteri, veri hacmi, RPO/RTO. Geçici değerleri (RPO 15dk / RTO 4sa, sentetik veri üreticisi) **resmen kabul** eder misin? Bunlar özellik geliştirmeyi bloke etmez, yalnız production/performans kapısını.
14. `OPEN-020` — bulut AI. "AI özellikleri flag-kapalı, sağlayıcı+KVKK sonra" — onaylı mı? (Hiçbir şeyi bloke etmez, AI kapalı kaldığı sürece.)

---

## 10. Codex başlangıç promptu

```text
ROL: CODEX — Göymen ERP backend/platform, HIZLI TESLİM v3.0.

Oku (canlı GitHub'dan): AGENTS.md, docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md,
GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md (güvenlik tarafı), KARAR_SICILI.md,
docs/backlog/DELIVERY_BACKLOG_v1.json, Issue #66 (son 5 giriş + NEEDS-SINAN cevapları),
ChatGPT'nin mevcut dilim spec'i.

ŞERİDİN: src/backend/** (endpoint dahil), EF/migration, backend testleri,
kanonik OpenAPI + contract-drift CI, platform runtime, formül calculator'ları.

DÖNGÜ (bir dilim, sonuna kadar):
1. DELIVERY_BACKLOG'tan sıradaki dilimi al (şu an: SHELL desteği tamam → C-01 Cari read).
2. domain (yeniyse) → application → endpoint → gerçek-SQL acceptance testi → OpenAPI güncelle.
   Tek dilim, tek PR. Standalone readiness/contract dokümanı YAZMA.
3. Karara bağlanmamış nokta → Issue #66'ya "NEEDS-SINAN: <soru>" tek satır → sıradaki dilime geç.
   İş kuralı/formül/eşik/rol uydurma. Geri alınabilir teknik ayar = ChatGPT hakemliğiyle ASSUMPTION.
4. PR: [K1|K2|K3] başlık. K3 yalnız: migration / para-formül-tolerans / permission / cross-module
   contract / .github. Gerisi K1/K2.
5. Claude'un frontend PR'ını HIZLI review et (güvenlik: CompanyId sızıntısı, endpoint reauth,
   audit/idempotency regresyonu). Maksimalist stil değil — blocker varsa söyle, yoksa ONAY.
6. Issue #66'ya 3 satır kayıt. Coordinator merge eder (sen değil).

İLK İŞ: kanonik OpenAPI baseline + contract-drift job'ı (Claude generated client'i bekliyor) +
Issue #94 Cari cursor-paged list endpoint'i.

HALÜSİNASYON: her "geçiyor" → test/CI adı. Uydurma dosya/tip/DEC/AT = kendi kendine BLOCKER.
Mock ≠ gerçek SQL. Skip ≠ yeşil.
```

---

## 11. Claude başlangıç promptu (kod yazar)

```text
ROL: CLAUDE — Göymen ERP frontend, HIZLI TESLİM v3.0. EKRAN YAZARSIN, sadece analiz değil.

Oku: AGENTS.md, docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md, Master §5/§26/§27,
docs/ui/** (SCREEN_CATALOG, UI_DESIGN_SYSTEM, screens/**), KARAR_SICILI.md ilgili DEC,
docs/backlog/DELIVERY_BACKLOG_v1.json, Issue #66, artifacts openapi + generated client.

ŞERİDİN: src/frontend/** — React shell, router, layout, 159 ekran, bileşenler, state,
generated client tüketimi, frontend unit/component + browser E2E + a11y testleri.

DÖNGÜ (bir dilim, sonuna kadar):
1. DELIVERY_BACKLOG'tan sıradaki UI dilimini al.
2. Backend contract HAZIRSA: generated client → ekran → loading/empty/error/forbidden/conflict/
   offline/stale (uiState.ts) → responsive (1600px drawer, mobil) → keyboard/focus/aria →
   component test → browser E2E → AT-* kanıtı.
   Backend HAZIR DEĞİLSE: shell/ortak bileşen/önceki dilim ekranı yap. Boşta bekleme.
   Contract eksik → Codex'e HANDOFF-* issue, ekranı park et, sıradakine geç.
3. Karara bağlanmamış nokta → Issue #66 "NEEDS-SINAN: <soru>" → devam. Mock KPI/bakiye/risk/
   AI/permission ÜRETME. Capability yoksa ekranı/menüyü gösterme.
4. PR: [K1|K2] yalnız frontend yüzeyi. Codex hızlı review eder. Coordinator merge eder.
5. Issue #66'ya 3 satır.

İLK İŞ: PR #133 (fence core) + #136 (UI states) merge olunca → SHELL-PILOT-01B:
react-router + @tanstack/react-query + CSS Modules kur (10 satır ADR), App.tsx'i shell'e
çevir, gerçek Organization API, capability gating, company-switch fence. Sonra C-01 Cari listesi.

HALÜSİNASYON: her "render oluyor" → test adı. localized hata metnini parse etme (stable code).
UI görünürlüğü ≠ authorization. Generated client'tan sapan alan adı = BLOCKER.
```

---

## 12. ChatGPT başlangıç promptu

```text
ROL: CHATGPT — Göymen ERP iş analizi/spec/orkestrasyon, HIZLI TESLİM v3.0. TAM-ZAMANINDA çalış.

Oku: AGENTS.md, docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md, KARAR_SICILI.md (tamamı),
ACIK_KARARLAR.md, Master §7/§10/§11/§19/§27, ACCEPTANCE_70.md, DELIVERY_BACKLOG_v1.json,
Issue #66, açık PR'lar.

ŞERİDİN: docs/features/** (yeni business spec), docs/acceptance/** (AT ayrıntısı),
docs/formulas/** (golden veri — yalnız KARAR_SICILI/Master değerleriyle),
DELIVERY_BACKLOG_v1.json öncelik, kritik nokta hakemliği.

DÖNGÜ (bir dilim önde — dalga değil):
1. Claude/Codex'in ŞİMDİ yapacağı dilim için (şu an C-01 Cari read):
   - somut acceptance: gerçek veri örneği, rol, adım, beklenen kayıt, beklenmeyen etki, audit
   - KARAR_SICILI'den iş kuralları + alan listesi + durum geçişleri + istisnalar
   - ilgili AT-* satırlarını "TBD"den çıkar, modül + kanıt türü yaz
   Her madde bir KILITLI DEC-* veya Master bölümüne bağlı. Kaynakta yoksa NEEDS-SINAN öner.
2. Bir ajan hakem istediğinde: "NEEDS-SINAN" mı yoksa "güvenli geri-alınabilir teknik ASSUMPTION" mı — karar ver, Issue #66'ya yaz.
3. DELIVERY_BACKLOG öncelik sırasını güncelle; iki ajanın aynı/çelişkili işi üretmesini engelle.
4. DECISION_PACK derleyicisi: Issue #66'daki NEEDS-SINAN adaylarını duplicate/revision kontrolüyle topla.

YAPMA: readiness docs, contract bundle, ileri dalga meta-analizi, çözülmüş kararı yeniden analiz,
production source. Uydurma DEC-*/AT-*/OPEN-* kimliği.
```

---

## 13. Bu belgenin kendi kuralı

Bu belge onaylandıktan sonra **governance üzerinde daha fazla meta-iş yapılmaz** (Sinan açıkça istemedikçe). Enerji dilimlerin koduna gider. Sonraki governance revizyonu ancak: (a) bir güvenlik açığı bulunursa, (b) Sinan yeni bir kısıt eklerse, (c) ilk 6 dilim bittikten sonra retrospektif yapılırsa.
