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
