# Göymen ERP Otonom İlerleme Kaydı

Bu dosya `docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md` uyarınca yürütülen
döngülerin doğrulanabilir durum günlüğüdür. Tamamlanmamış veya CI ile kanıtlanmamış çalışma
`done` olarak yazılmaz.

## 2026-08-28 00:35 +03:00 — Döngü 1

- Main: `5f577892e9eeb51034cb8906e8a5e01a31d8680c`
- Aktif kritik görev: `ERP-000E — Kalıcı Idempotency + Concurrency`
- Kritik branch/PR: `codex/erp-000e-idempotency-concurrency` / Draft PR #6
- İncelenen head: `7723ccdf6c7b72db8cbf9f01016cfbd41312135d`
- Uzak CI: run `33119029839` devam ediyor
- Ayrı açık PR: Draft PR #2, Faz 0 v1.1 raporlama deltası

### Tamamlananlar

- PR #6 güncel `main` ile senkronize edildi.
- Replay öncesi kaynak kapsamı yeniden doğrulandı; scope daraltıldığında kalıcı sonuç ve
  original correlation açıklanmıyor.
- `VersionConflict` beklenen/mevcut sürüm farkını kalıcı ve replay edilebilir taşıyor.
- Cross-module Domain/Infrastructure hedeflerine default-deny architecture guard ile
  ADR-009 ve ADR-011'e bağlı iki exact atomiklik istisnası eklendi; Application contract
  referansları bu istisna mekanizmasının dışında bırakıldı.
- Global `AT-005`, backend conflict contract'ı otomatik kanıtlanmış olsa da kullanıcı
  arayüzü/E2E beklediği için gerçeğe uygun biçimde `Taslak` tutuldu.
- İki bağımsız self-review sonunda yeni P0/P1 bulunmadı.
- ERP-000F kodlaması başlatılmadı.

### Yerel kanıt

- Release build: 0 uyarı, 0 hata.
- Çözüm testleri: 59 başarılı, SQL env gerektiren 1 test bu koşuda atlandı.
- Ayrı gerçek SQL Server acceptance: 1/1 başarılı; toplam doğrulanmış senaryo 60/60.
- Architecture dependency guard: 6/6.
- HTTP integration: 26/26.
- `dotnet format --verify-no-changes` ve `git diff --check`: başarılı.
- NuGet/npm vulnerability taraması: bilinen bulgu yok.
- Dört idempotent migration scripti üretildi.
- Frontend lint/test/build ve API/web container build başarılı.
- Test için oluşturulan geçici SQL container'ı doğrulanarak kaldırıldı.

### Kapı ve sonraki iş

- In review: ERP-000E Draft PR #6; güncel uzak CI ve insan mimari review'u bekliyor.
- Blocked: ERP-000F, ERP-000E `main`e merge edilene kadar kapalı.
- Open decision: `OPEN-021` idempotency retention/partition/privileged purge; çözülmedi.
- Safe next: repo governance kaydını ayrı docs-only PR olarak doğrulamak; ardından bağımsız
  UI katalog golden validator veya UI-MAP karar sicili uzlaştırmasını ayrı işte ele almak.
- Auto-merge: yapılmadı.

```text
DONE: PR #6 main sync; üç review blocker düzeltmesi; yerel kabul kanıtı
IN REVIEW: ERP-000E Draft PR #6, head 7723ccdf, CI 33119029839
BLOCKED: ERP-000F (ERP-000E merge kapısı)
SAFE NEXT: governance docs PR; UI katalog golden validator
OPEN DECISIONS: OPEN-021
MAIN SHA: 5f577892e9eeb51034cb8906e8a5e01a31d8680c
ACTIVE PRS: #6 Draft; #2 Draft
```

## 2026-08-28 00:41 +03:00 — Döngü 2

- ERP-000E head `7723ccdf6c7b72db8cbf9f01016cfbd41312135d` için CI run
  `33119029839` dört işte tamamen başarılı oldu: backend build/test/migration ve
  architecture guard, frontend lint/test/build, gerçek SQL Server relational acceptance ve
  container build.
- Nihai kabul kanıtı Draft PR #6'ya yorum olarak kaydedildi; PR kritik insan review kapısı
  nedeniyle Draft bırakıldı ve merge edilmedi.
- Governance protokolü ile bu ilerleme günlüğü Draft PR #8 altında ayrıştırıldı. İlk CI run
  `33119090777` dört işte tamamen başarılı oldu.
- ERP-000F başlatılmadı; `OPEN-021` açık kaldı.
- UI katalog golden validator bağımsız docs/tooling işi olarak ayrı dalda yürütülüyor.

```text
DONE: PR #6 final CI 33119029839; PR #8 ilk CI 33119090777
IN REVIEW: ERP-000E Draft PR #6; governance Draft PR #8
BLOCKED: ERP-000F (ERP-000E insan review ve merge kapısı)
SAFE NEXT: UI katalog golden validator
OPEN DECISIONS: OPEN-021
MAIN SHA: 5f577892e9eeb51034cb8906e8a5e01a31d8680c
ACTIVE PRS: #8 Draft; #6 Draft; #2 Draft
```

## 2026-08-28 01:02 +03:00 — Döngü 3

- Governance Draft PR #8 içinde bulunan self-authority-escalation açığı kapatıldı. Repo
  protokolünün dış yazma/merge yetkisi üretmediği; governance, CI, acceptance durumu ve test
  kapısı değişikliklerinin otomatik merge edilemeyeceği kesinleştirildi. `AGENTS.md` protokolü
  yeni döngüler için keşfedilebilir yapıyor. Head `b8ae919a0b8809802b4f1f60cc2c56f0bcaec7ad`
  için CI run `33119693982` dört işte başarılı oldu; PR Draft bırakıldı.
- UI katalog golden validator Draft PR #9 olarak açıldı. Head
  `0e253bf0f684a42113200f611a2725db07a484be` üzerinde 24/24 fixture testi ile gerçek
  katalogda 72 kaynak kayıt, 159 kanonik ekran, 72 ekran kartı, 77 özgün varlık, 5 prototip,
  19 seçilmiş kaynak ve exact 101 manifest girdisi doğrulandı. Symlink/sınıf-kökü ve yanlış
  karar kabulü blockerları kapatıldı; bağımsız yeniden incelemede yeni P0/P1 bulunmadı.
  UI run `33120492323` ve ana CI run `33120492302` tamamen başarılı; PR Draft bırakıldı.
- Raporlama deltası Draft PR #2 güncel `main` ile salt-okunur incelendi. Merge conflict,
  mevcut kabul kanıtının gerilemesi, task manifest drift, canonical karar keşfi, `OPEN-020`
  veri güvenliği bağı, RPT-001/RPT-003 kabul döngüsü ve R-06/R-07 UI katalog uyumsuzluğu
  olmak üzere yedi blocker PR yorumuna kaydedildi. PR merge edilmedi.
- ERP-000E Draft PR #6 değişmedi; final CI yeşil ve insan review kapısında. ERP-000F
  başlatılmadı, `OPEN-021` açık kaldı ve `main` değiştirilmedi.

```text
DONE: PR #6 final kanıt; PR #8 governance guard; PR #9 UI golden validator; PR #2 blocker review
IN REVIEW: ERP-000E Draft #6; governance Draft #8; UI validator Draft #9
BLOCKED: ERP-000F (ERP-000E insan review ve merge kapısı); PR #2 (yedi blocker)
SAFE NEXT: insan review sonrası PR #6 merge kararı; ardından bağımlı dalları güncel main ile senkronize et
OPEN DECISIONS: OPEN-021; OPEN-020 raporlama AI sağlayıcı/veri sınıfı bağı
MAIN SHA: 5f577892e9eeb51034cb8906e8a5e01a31d8680c
ACTIVE PRS: #9 Draft; #8 Draft; #6 Draft; #2 Draft
```

## 2026-08-28 03:04 +03:00 — Döngü 4

- ERP-000E kullanıcı tarafından kabul edildi ve PR #6, `main` dalına
  `9f08ff16447eca89682d0a084e2341b9c2b433c2` commit'iyle squash merge edildi.
- Governance Draft PR #8 güncel `main` ile conflict olmadan senkronize edildi. Head
  `f8703278c186ae4f2b25462880604e6084fc5ee0` için CI run `33128061915`; backend,
  frontend, gerçek SQL Server relational acceptance ve container build işlerinde tamamen
  başarılı oldu. Self-review yeni kod/güvenlik blocker'ı bulmadı.
- UI katalog golden validator Draft PR #9 güncel `main` ile conflict olmadan senkronize
  edildi. Head `8e7d41a5ff0f972007a70b43a53573a490ff23a3` için UI run `33128067501`
  ve ana CI run `33128067513` tamamen başarılı oldu; 24/24 yerel fixture testi ve gerçek
  katalog doğrulaması da geçti. Self-review yeni P0/P1 bulmadı.
- PR #8 `AGENTS.md` ve governance politikasına, PR #9 ise `.github` CI workflow'una
  dokunduğu için protokolün kontrol yüzeyi istisnası uygulandı. İki PR da başarılı
  doğrulamaya rağmen otomatik merge edilmedi ve insan/ChatGPT review'u için Draft kaldı.
- Draft PR #2'nin daha önce kaydedilmiş yedi blocker'ı korunuyor; conflict veya mimari
  tutarsızlık sessizce çözülmedi.
- ERP-000F readiness incelemesi implementation için `NO-GO`, yalnız spec/ADR/acceptance
  hazırlığı için `GO` verdi. Gerçek `ADR-006`, event ownership/schema, dispatcher
  lease/crash recovery, poison/dead-letter, inbox atomikliği, ordering, messaging
  retention ve gerçek SQL race kabul sözleşmeleri eksik. Üretim kodu/migration yazılmadı;
  güncel `main`den `codex/erp-000f-spec-gate` spec-only dalı ayrıldı.

```text
DONE: ERP-000E PR #6 merge 9f08ff1; PR #8/#9 main sync ve güncel yeşil CI
IN REVIEW: governance Draft #8; UI validator Draft #9
BLOCKED: ERP-000F implementation (ADR/spec/acceptance ve açık kararlar); PR #2 (yedi blocker)
SAFE NEXT: ERP-000F spec-only ADR/OPEN/acceptance Draft PR; PR #8/#9 insan review'u
OPEN DECISIONS: OPEN-020; OPEN-021; ERP-000F için OPEN-022..OPEN-025 hazırlanacak
MAIN SHA: 9f08ff16447eca89682d0a084e2341b9c2b433c2
ACTIVE PRS: #9 Draft; #8 Draft; #2 Draft
```

## 2026-08-28 03:32 +03:00 — Döngü 5

- ERP-000F readiness için güncel `main`den ayrılan `codex/erp-000f-spec-gate` dalında
  yalnız dokümantasyon kapsamlı Draft PR #10 açıldı. Head
  `fd5d2e905fb5cc3ff27a4b51b77edd8338bf53fa` ve CI run `33129862266`; backend,
  frontend, gerçek SQL Server relational acceptance ve container build işlerinin tamamı
  başarılı oldu.
- PR #10, fiziksel ADR-006 önerisini, `OPEN-022`…`OPEN-025` bloklayıcı kararlarını,
  ERP-000F feature/readiness sözleşmesini ve `AT-079`…`AT-094` arasındaki 16 taslak kabul
  senaryosunu ekledi. Toplam 86 acceptance ID benzersiz; yeni kanıt alanları bilinçli olarak
  boş ve durumlar `Taslak` kaldı. PR #2 için ayrılmış `AT-071`…`AT-078` değiştirilmedi.
- Üç bağımsız self-review; tenant scheduler/context, outbox/inbox unique scope,
  event/envelope hash sürümleme, lease fencing, audit aktörü, harici duplicate sınırı ve
  coding gate üzerinde kalan P0/P1 bulmadı.
- ERP-000F implementation hâlâ `NO-GO / BLOCKED` durumundadır. ADR-006 insan review'uyla
  kabul edilmeden ve `OPEN-022`…`OPEN-025` kapanmadan feature veya test-substrate kodu,
  migration, worker, adapter ya da pilot event başlatılmadı.
- Governance Draft PR #8 head `f8703278c186ae4f2b25462880604e6084fc5ee0`, CI
  `33128061915`; UI validator Draft PR #9 head
  `8e7d41a5ff0f972007a70b43a53573a490ff23a3`, UI/ana CI `33128067501` ve
  `33128067513` ile yeşildir. İkisi de kontrol yüzeyine dokunduğu için manuel review'da
  ve Draft kalır.
- Draft PR #2'nin yedi blocker'ı korunmuştur; conflict ve mimari tutarsızlıklar sessizce
  çözülmemiştir.

```text
DONE: ERP-000E PR #6 merge; PR #8/#9 güncel yeşil CI; ERP-000F spec/readiness PR #10 yeşil CI
IN REVIEW: ERP-000F spec Draft #10; governance Draft #8; UI validator Draft #9
BLOCKED: ERP-000F implementation (ADR-006 + OPEN-022..025); PR #2 (yedi blocker)
SAFE NEXT: PR #10/#8/#9 manuel review; yalnız bağımsız ve kontrol yüzeyi olmayan güvenli işler
OPEN DECISIONS: OPEN-020; OPEN-021; OPEN-022; OPEN-023; OPEN-024; OPEN-025
MAIN SHA: 9f08ff16447eca89682d0a084e2341b9c2b433c2
ACTIVE PRS: #10 Draft; #9 Draft; #8 Draft; #2 Draft
```
