# Sıfır maliyetli CI modeli — 2026-10-21 (r2)

Karar: Sinan, 21 Ekim 2026 ("GitHub Actions bütçesini açmayalım; maliyetsiz en mantıklı çözümü
bul ve uygula"). Makbuz: #66/6313909467; bu belge `SINAN_KARARLARI_2026-10-21.md` §2'nin
teknik ekidir. `.github/**` değişikliği K3'tür: iki bağımsız exact-head review **ve Sinan'ın
Issue #66'da açık `MERGE-OK <sha>` makbuzu** olmadan merge edilmez. Bu belge workflow'u
kendisi etkinleştirmez.

## 1. Neden ücretsiz

GitHub Actions faturası yalnız **GitHub-hosted runner dakikaları** ve **depolama**
(artifact + cache + Packages) için kesilir. **Self-hosted runner'da koşan job ücretsizdir.**

Harcama limiti `$0` yalnız plana dahil kota aşıldıktan sonra devreye girer (özel repo, Free
plan: ayda 2.000 hosted dakika, 500 MB depolama; Windows dakikası ×2). Kota tükenene kadar
hosted'a düşen bir job `$0` limitine rağmen koşar ve dahil kotayı tüketir; kota bitince hosted
job başlamaz ve artifact/cache/Packages yüklemesi reddedilir. Self-hosted job'lar her durumda
ücretsiz çalışır. **Bu yüzden `$0` limiti hosted fallback'e karşı sigorta değildir; hosted
dakikayı sıfırda tutan tek mekanizma `runs-on` etiketleridir.** Hesabın plan kotası ve mevcut
tüketimi bağımsız teyit edilmemiştir (bkz. #765 `docs/development/LOCAL_CI_20261016.md`).

Model: **hosted dakika yok, artifact/cache yok, kanıt yerel diskte.** Bütçe `$0` kalır. Ek
hizmet, hesap, lisans yok.

## 2. Seçenekler ve seçim

| Seçenek | Maliyet | Artı | Eksi | Karar |
|---|---|---|---|---|
| A. Actions + self-hosted runner, artifact/cache kaldırılmış | 0 | #765 zaten bu yolda; PR'da check sonucu görünür. (Zorunlu-check/ruleset özel repoda plan yükseltmesi veya public görünürlük ister — LOCAL_CI_20261016 kaydı API 403; ikisi de açık karar. Merge kapısı AGENTS.md gereği koordinatör tarafından prosedürel doğrulanır.) | Workflow'lar Sinan tarafından tek tek yeniden etkinleştirilmeli; runner ayakta olmalı | **SEÇİLDİ** |
| B. Actions tamamen kapalı, yerel `local_check.py` + commit status API | 0 | Runner gerekmez | Status API için token gerekir; sonuç check sekmesinde değil commit status olarak görünür; iki ayrı kanıt yolu | Yedek (A çalışmazsa) |
| C. Hosted runner + bütçe | ücretli | Sıfır kurulum | Sinan'ın kararına aykırı | Ret |
| D. Başka CI hizmeti (ücretsiz katman) | 0 başlangıç, sonra ücretli | — | Yeni hesap/secret, limit riski, governance dışı | Ret |

## 3. #765 üzerinde yapılacak somut değişiklik (CI sahibi; K3)

1. **Artifact adımlarını kaldır:** main'de `actions/upload-artifact@v7` kullanan **12 adım**
   (`ci.yml` 77, 174, 194, 471, 491, 560; `browser-acceptance.yml` 499;
   `c01-real-browser-e2e.yml` 367, 388, 398; `cari-c02-link-acceptance.yml` 50;
   `openapi-generate.yml` 153). Yerine: aynı dosyalar runner'ın yerel kanıt klasörüne
   kopyalanır: `E:\GoymenERP-CI\evidence\gh-<workflow>-<run_id>-<sha7>\` (Windows; mevcut
   `launcher-*` kökü) veya `/home/ci/local-ci-evidence/gh-<workflow>-<run_id>-<sha7>/`
   (Linux; #765'teki `--evidence-root` kökü; runner hesabının yazma izni K3 review'unda
   doğrulanır). Adım sonunda klasörün sha256 manifesti job log'una yazılır.
   **İkisi job-arası taşımadır, yalnız silinmez:** `ci.yml:174` (backend-group proof) ve
   `ci.yml:471` (relational-shard proof) dosyalarının tüketicileri `ci.yml:214` ve `:580`
   `actions/download-artifact@v8` adımlarıdır (`backend` ve `relational` aggregate job'ları,
   `tools/verify_backend_test_groups.py --aggregate --proof-directory` ve
   `tools/verify_rcsi_relational_acceptance.py --aggregate --proof-directory`). Worker
   job'lar `proof-*.json`'u aynı runner üzerindeki kalıcı yerel proof dizinine (`${{ runner.temp }}`
   dışı) mevcut dizilimle yazar; aggregate job'daki download adımı bu dizinden okuyan kopya
   adımıyla değiştirilir; verifier'lar ve proof şemaları **değişmez** (#765 LOCAL_FULL
   blokeri 2). Worker ve aggregate job'ların aynı makinede koşması `runs-on` etiketiyle
   garanti edilir. #765'teki `*.yml.disabled` dosyaları kayıtlı workflow değildir; `.yml`'e
   çevrilmeden önce aynı temizlik uygulanır.
2. **Cache'i kaldır:** `ci.yml:405-406` `cache: npm` ve `cache-dependency-path` satırları
   birlikte silinir; runner'daki kalıcı `~/.npm` zaten önbellektir.
3. **`runs-on`:** main'deki 18 `ubuntu-latest`/`windows-latest` satırının (ci.yml ×11,
   browser-acceptance ×2, openapi-generate ×2, c01-real-browser-e2e, cari-c02-link-acceptance,
   ui-catalog-validation) tamamı açık self-hosted etikete çevrilmiş olmalı; hosted fallback
   **yok**. K3 review'unda her satır tek tek doğrulanır; yeni hosted etiket girişi P0 sayılır.
4. **Zorunlu job'lar** (main `ci.yml` job id'leriyle; GitHub check eşleşmesi job *adıyla*):
   `backend-build`, `backend-test-group` (matrix: architecture / api / modules — üçü de),
   `backend` (toplayıcı, "Backend build, test and migration"), `frontend`, `relational-shard`
   (A/B/C), `relational-rcsi`, `relational` (toplayıcı, "SQL Server relational acceptance"),
   `contract-drift` ("OpenAPI contract drift"), `release-manifest`,
   `recovery-surface-inventory` + `validate-ui-catalog` (`ui-catalog-validation.yml`).
   Ayrı bir `architecture` job'u yoktur. Aggregate job'lar `if: always()` olduğundan kabul
   kanıtında alt job'ların her biri ayrıca `success` olmalıdır. `containers`, browser E2E,
   C01 ve `cari-c006-backend` (Windows) saat bağımsız her zaman, mevcut tek ağır-iş kilidi
   altında sırayla/merge treninde koşar; PR'da zorunlu
   değil. #765 job id'lerini değiştiriyorsa liste #765 head'inden alınır ve PR'a yazılır.
5. **Her zaman, sıralı koşu:** [22 Ekim kullanıcı kararı](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6342843156)
   uyarınca ağır koşular saat bağımsız her zaman, mevcut tek ağır-iş kilidi altında
   sırayla yürür; gece saatine bırakılmaz. Anlık 2 GiB boş RAM kapısı her koşuda korunur.
   Eşik tutmazsa koşu reddedilir, neden kaydedilir ve ilk uygun anda aynı kapılarla yeniden
   denenir. Scheduler, servis, config veya yeni watcher eklenmez; Windows Görev Zamanlayıcı
   22:00 işi ya da yeni `schedule` tetikleyicisi kurulmaz. LOCAL_FULL kilidinin üç blokeri
   kabul edildikten sonra açılır; öncesinde `--plan`.
6. **Kabul kanıtı:** aday head'de bütün zorunlu job'lar tamam (cancelled/skipped/sıfır test
   yok), ham log + TRX, head SHA. TRX özetleri mevcut `tools/ci/summarize_trx.py` ile
   `artifacts/ci-safe-test-summary/*.json` olarak üretilir (ci.yml:67/184/481/550), yerel
   kanıt klasörüne kopyalanır ve job log'una basılır; `tests/ci_safe_trx_summary` bu aracın
   unittest paketidir (ci.yml:27) ve aynen koşar.
7. **Tetikleyiciler:** `c01-real-browser-e2e.yml` `on:` bloğundan `pull_request` ve `push`
   kaldırılır; yalnız `workflow_dispatch` (istenirse `browser-acceptance.yml` desenindeki
   `pull_request: types: [labeled]`) kalır. `cari-c02-link-acceptance.yml` (C006, Windows, ~30
   dk) için CI sahibi aynı seçimi yapar ya da hafif olduğunu kanıtlayıp PR'da kalmasını 4.
   maddeye açıkça yazar. Bu değişiklik olmadan workflow'lar açılınca C01 her PR push'unda 60
   dk'lık compose yığınını runner'da başlatır ve Actions tarafında LOCAL_FULL'ün 2 GiB
   admission kapısı yoktur.

## 4. Sinan'ın yapacağı (ücretsiz)

1. #765 (veya onu taşıyan PR) için Issue #66'ya `MERGE-OK <sha>` tek satırı (iki bağımsız
   review tamamlandıktan sonra).
2. Merge sonrası: Repo → Settings → Actions → `disabled_manually` durumundaki **altı kanonik
   workflow'u** (`ci.yml`, `c01-real-browser-e2e.yml`, `browser-acceptance.yml`,
   `cari-c02-link-acceptance.yml`, `openapi-generate.yml`, `ui-catalog-validation.yml`) **tek
   tek** Enable yapmak. Yedinci kayıt `c01-normal-provider-attestation.yml` (eski dal) kapalı
   kalır; toplu "enable all" yapılmaz. Bu adım §3.7 tetikleyici değişikliği merge edildikten
   sonra yapılır.
3. Zorunlu check / ruleset etkinleştirmek bu adımın parçası değildir (plan/görünürlük kararı
   ayrı `NEEDS-SINAN`).

Harcama limiti `$0`'da kalır. Runner'lar (`DESKTOP-11CDRHD` Linux servis + Windows kullanıcı
oturumu) açık olduğu sürece PR'da yalnız hafif check'ler otomatik koşar.

## 5. Kaynak sorunu (RAM < 2 GiB)

Anlık 2 GiB boş RAM eşiği değişmez; bu bir saat kapısı değildir. Ağır koşular her zaman,
mevcut tek ağır-iş kilidi altında sırayla yürür. Eşik tutmazsa koşu reddedilir, neden
kaydedilir ve ilk uygun anda aynı kapılarla yeniden denenir; eşik aşılmaz veya test
başarılı sayılmaz. Sonuç koşu tamamlandığında kanıt klasörüne yazılır. Bu düzenleme
scheduler, servis, config veya yeni watcher eklemez.

## 6. Geri alma

Model başarısız olursa (runner sürekli çevrimdışı, depolama kotası beklenmedik dolu):
Seçenek B'ye geçilir; workflow'lar yeniden devre dışı bırakılır. Hiçbir adım hosted dakikaya
düşmez.
