# U-01 / U-02 — Ürün ekranları için gerçek-tarayıcı kabul hazırlığı

İki dil, `URUN_LANE` ile seçilir:

- **T** (varsayılan): kontrollü API yanıtları tarayıcıda `route.fulfill` ile verilir; hiçbir API'ye ulaşılmaz. Bileşen kanıtıdır.
- **L**: yayımcı tarafından doğrulanmış, tüketici tarafından geçerlenmiş bir Ürün ortamının canlı Product API'si, aynı-origin Vite proxy üzerinden. Hiçbir yanıt test tarafından üretilmez.

Her ikisi de mevcut Cari desenlerini **tüketir**: C010 koşum sahipliği modülü, C009 canlı kayıt/probe/kanıt yazıcıları, aynı Vite profili şekli. L dilinin ortam sözleşmesi Product Backend'in yayımladığı **tek** kaynaktır: `tools/product-u01-u02/ENVIRONMENT_CONTRACT.md` (`7c5d64f`). Bu dizin ikinci bir shell, router, query altyapısı, sahiplik sözleşmesi, manifest şeması, rapor altyapısı veya test sistemi kurmaz.

**Hiçbir koşum kendi başına ERP kabulü değildir.** T dilinde canlı API/SQL/login/capability yayını yoktur. L dili yalnız Backend yayımcısının `environment-ready.json`'ı ve Orkestratör'ün kaydettiği accepted-contract hash'i ile koşar; `PASS`/kabul hükmü yayımcının ve orkestratörün alanıdır. Fixture verisi hiçbir yerde canlı sonuç yerine kullanılmaz. **Şu an gerçek run/instance/fixture/ENV_READY yoktur; L koşulmamıştır (NOT_RUN).**

## T dili — ne ölçüyor

| Spec | Ölçülen |
| --- | --- |
| `u-01-list-card` | liste → satır → kart; her Product isteğinde committed şirket doğrulanmış origin başlığı (`X-Company-Id`) olarak, payload'da asla; etiket/eksiklik karttan; "okunamadı" ≠ "atanmadı" |
| `u-01-authority` | capability yoksa nötr blok + sıfır Product okuması; liste/kart 403 → route kapanır, kayıt açığa çıkmaz; liste grant'i kart açmaz |
| `u-02-create-edit-journey` | liste → yeni → Draft (sunucu kodu) → kart → düzenle + gerekçe → taze sürüm → karta dön; tek POST/tek PATCH, farklı key'ler, key alfabesi, origin başlığı var payload'da companyId yok; DIFF + `expectedVersion` okunan karttan |
| `u-02-unknown-same-key` | 503 → aynı key/aynı gövde ile resubmit ve retry → `isReplay` kabul; unknown → retry 403 → route kapanır → **gen-2** ile taze giriş girişimi bulur, otomatik POST yok, insan retry'ı ilk key |
| `u-02-context-change` | dirty create + şirket değişimi: Kal tutar; onaylı bırak düşürür, diğer şirket boş, geri dönüşte gelmez, resend yok |
| `u-02-edit-conflicts` | `version_conflict`: iki sürüm, girdi korunur, yalnız "Güncel hâli yükle"; `lifecycle_conflict` adıyla |

Son yerel T koşumu: 14/14, gerçek Chromium. T kampanyası kabul için tekrar koşulmaz.

## L dili — sözleşmenin tüketici tarafı

### Girdiler (ENVIRONMENT_CONTRACT.md "Consumer validation")

| Değişken | Anlam |
| --- | --- |
| `URUN_ENVIRONMENT_MANIFEST` | `environment-ready.json`'ın **mutlak** yolu: `C:\Users\goyme\.codex\worktrees\dd1f\GoymenERP-codex\artifacts\product-u01-u02\acceptance\<runId>\environment-ready.json`. Kardeşleri `accepted-contract.json` ve `source-binding.json` sabittir, manifest'ten alınmaz. |
| `URUN_ACCEPTED_CONTRACT_SHA256` | Orkestratör'ün #245 devrinde kaydettiği accepted-contract hash'i. Bir manifest kendi hash'ini onay sayamaz. |
| `CARI010_EVIDENCE_DIR` (+ isteğe bağlı `CARI010_RUN_ID`, `CARI010_VITE_CACHE_DIR`) | C010 **tarayıcı** koşum sahipliği; ortamın kaynak `runId`'sinden ayrıdır ve kanıtta ikisi birlikte yazılır. |

Varsayılan yoktur; ortam değişkeni ile hedef/kullanıcı yönlendirmesi yoktur.

### Doğrulama — `manifest-validator.ts` (yan etkiden önce)

Runner, C010 sahipliği alınmadan **önce**, üç dosyayı bayt olarak bir kez okur ve reddeder ancak hepsi tutarsa bağlar:

- **accepted-contract.json**, her alan tam: `contractId=PRODUCT-U0102-L-v1`, `taskId=U-01-U-02`, `environmentName=Testing`; `runId=product-u0102-<12hex>`, nil olmayan `instanceId`; `acceptanceRoot` = sabit kök + runId **ve** manifest o dizinde; `apiBaseUrl` tam `http://127.0.0.1:26080`, `sqlEndpoint` tam `127.0.0.1,26433`, `uiOrigin` tam `http://127.0.0.1:26173` (userinfo/yol/sorgu/fragment, başka host/port/şema yok); `databaseName=GoymenERP_Product_U0102_<suffix>`; `composeProject/containerName/volumeName/networkName` = runId / `-sql` / `-data` / `-network`; `actor`≠`unauthorizedActor`, iki farklı nil-olmayan `actorId/unauthorizedActorId`; `companyId`, `codeGroupId` UUID; `browserPrefix=PRODUCT-U0102-<suffix>`; `source` nesnesi (mutlak `worktree`/`frontendWorktree`; 40-hex `acceptanceHead=compiledHead`, `backendDependency`, `productDependency`, `backendTree`, `frontendHead`, `frontendDiffBase`; 64-hex `openApiSha256`, `migrationSourcesSha256`, `frontendDiffSha256`, `fixtureSha256`; boş olmayan **sıralı** göreli `fixturePaths`; `sourcesClean === true`; boş olmayan sıralı `GoymenERP*.dll` → SHA-256 haritası); `erpAccepted` ve `productionActivated` **JSON boolean `false`**.
- Bayt hash'i `URUN_ACCEPTED_CONTRACT_SHA256` ile aynı olmalı (`ACCEPTED_HASH`).
- **source-binding.json**: `contractId/runId/instanceId/acceptedContractSha256` accepted ile aynı; `source` nesnesi accepted'ınkiyle **kanonik JSON eşitliğinde** (artefakt haritası dahil — beklenen-vs-gözlenen); `ownership` accepted'ın DB/SQL/API/compose/container/volume/network adlarını ve `resourceRunId/resourceInstanceId`'yi tekrar eder, `containerId/networkId` hex kimlik, `hostPid` > 0, `hostStartTimeUtc` zaman damgası; `verifiedAtUtc` zaman damgası. Tüketici SQL/konteyner/PID gözlemi **yapmaz**; bu yayımcının doğrulamasıdır ve burada yalnız tutarlılığı sınanır — kendi beyan ettiği bir "temiz" bayrağı tek başına hiçbir şeydir.
- **environment-ready.json**: tekrarlanan 11 alan accepted ile aynı; `readyForBrowser === true`; `acceptedContractSha256` = accepted baytları, `sourceBindingSha256` = binding baytları; `verifiedAtUtc` = binding'inki; iki nihai bayrak `false`. `readyForBrowser` tek başına asla geçmez.
- **Servis edilen frontend:** bu worktree `source.frontendWorktree`, HEAD `source.frontendHead`, `src/frontend/goymen-erp-web` + `tests/e2e` temiz (Git ile gözlenir).

Sonra **dondurulur**: üç hash + kimlikler + `apiBaseUrl/actor/unauthorizedActor/companyId/codeGroupId/browserPrefix` `URUN_LIVE_BINDING` ile Vite ve worker'lara verilir; her üye üç dosyayı yeniden hash'ler, uyuşmazsa reddeder. Proxy hedefi ve `VITE_DEV_USER` yalnız bu snapshot'tan gelir.

Hedef negatifler (ortam gerektirmez, sentetik dosyalar geçici dizinde): `node --test product-u01-u02/manifest-validator.test.mjs` — 33 vaka: devir hash'i eksik/yanlış, her yanlış `apiBaseUrl` biçimi, başka SQL/UI portu, C006 sabitleri (`contractId`, `taskId`, `runId`, DB, container, prefix) Ürün değeri olarak, string/undefined nihai bayraklar, nil/aynı actor kimlikleri, yanlış kök, bozuk `source`, beklenen≠gözlenen `source`, eksik/yanlış `ownership`, kardeş hash/tekrar/zaman uyuşmazlığı, başka worktree/HEAD/kirli ağaç, bağlandıktan sonra düzenlenen dosya (runner ve member), eksik kardeş/JSON-dışı/yanlış dosya adı.

### Ne ölçüyor (hepsi canlı, hiçbiri mock değil)

| Spec | Ölçülen |
| --- | --- |
| `l-01-journey` | create → liste (aramayla bulunur) → kart → düzenle + gerekçe → taze sürüm → karta dön → listeden yeniden aç. Kod grubu **sözleşmenin `codeGroupId`'si**, yalnız canlı yetkili katalog sunuyorsa (adım 4). **İki komut da gerçek kayıp yanıt + gerçek aynı-key replay'den geçer:** ilk POST ve ilk PATCH `route.fetch` ile gerçek API'ye iletilir; gerçek 201/200 ve gerçek ürün kimliği (`lifecycle: Draft`) **görüldükten sonra** tarayıcıya teslimi kesilir (`upstream_success_then_delivery_abort`). Aynı key ile retry sıradan tarayıcı isteğidir ve gerçek `isReplay: true` döner. Kod/id/sürüm yalnız gerçek yanıttan; PATCH `expectedVersion` karttan okunan sürüm; her Product isteği bağlı actor ve bağlı şirket (doğrulanmış origin başlığı) ile, payload'da companyId yok; retry aynı origin/key/payload (shell şirketi Ürün sahibi değildir). |
| `l-02-authority` | `unauthorizedActor` doğrudan probe ile liste/kart/create → gerçek 403 (sözleşme `code` vaat etmez; gözlenen kaydedilir, zorunlu değil), reddedilen ad açığa çıkmaz; yetkili actor'ün aramasında o ad yok; var olmayan id → nötr blok, id doğrulanmaz. |

Her L kanıt kaydı: C010 **tarayıcı** `runId` + ortam `resourceRunId/resourceInstanceId` + üç dosya hash'i + `frontendHead` + yayımcı `verifiedAtUtc` (adım 3).

### Aşama devri — Backend'in gerçek quiescent snapshot'ı (`src/checkpoint.ts`)

Product owner'ın `Created` assertion'ı gerçek **after-create v1**'i ister; final v2'den türetilemez. `l-01-journey` bu yüzden üç kez durur ve aynı Backend'e devreder: **`baseline`** (ilk mutation tarayıcıdan çıkmadan), **`after-create`** (create gerçek replay ile tamamlandı, edit başlamadı), **`final`** (edit tamamlandı, kart yeniden açıldı). Her aşamada tarayıcı C009 `LiveRecorder.quiesce` ile sessizleşir (uçan istek/`aria-busy` yok), `<koşum>/L/checkpoints/<aşama>.request.json` yazar (aşama, tarayıcı runId + resource run/instance + manifest hash, gerçek yanıttan okunan ürün kimliği, sessizlik beyanı, o ana kadarki gerçek HTTP exchange metadata'sı — gövde yok) ve **yalnız** `<aşama>.ack.json` gelince devam eder. Ack, aynı Backend'in `control.ps1 -Product -Action snapshot` ile aldığı snapshot'ın kaydıdır ve şunları taşımalıdır: `stage`, `resourceRunId` (bağlı run), `requestSha256` (tam istek baytları), `snapshotSha256`, `takenAtUtc`; isteğe bağlı `snapshotPath` okunabiliyorsa baytları hash'e uymalı (salt-okunur; asla parse edilmez). Yanlış/eksik ack aşamayı hemen reddeder; ack yoksa bekleme `URUN_CHECKPOINT_TIMEOUT_MS` (varsayılan 15 dk) tavanına kadar **artefakt beklemesidir** (süre tahmini sleep değil), tavanda `TIMEOUT` ile aşama reddedilir. Tarayıcı SQL açmaz, snapshot çalıştırmaz, JSON'dan durum türetmez.

Tek devir: `L/checkpoints/l01-handover.json` — üç aşamanın istek/ack yolları ve hash'leri, snapshot hash'leri, gerçek create/edit/replay exchange metadata'sı, withheld teslimler; hepsi aynı bağlı koşumdan. Regresyon: `node --test product-u01-u02/checkpoint.test.mjs` (20 vaka: doğru ack devam ettirir, geç gelen ack beklenir, tavanda red, okunamayan ack tavanda red, geçici okuma hatası sonrası geçerli ack devam, NaN/Infinity/0/negatif tavan veya poll aralığı ilk beklemeden önce red, başka aşama/run/istek-hash/snapshot-hash/zaman red, uyuşmayan snapshot dosyası red, aynı aşama ikinci istek red).

Ölçülmeyen ve açık kalan: gerçek login (dev identity başlığı), SQL tarafı (yayımcının koşum öncesi/sonrası doğrulaması ve ortamın kendi araçları), sayfa yenilemesi sonrası (bellek-içi girişim tasarım gereği biter), klavye/zoom erişilebilirliği, `references` ve aktivasyon (sonraki dilim).

**CORS yok:** tarayıcı yalnız `http://127.0.0.1:26173` loopback origin'iyle konuşur; `/api` Vite tarafından sunucu tarafında iletilir. Cross-origin istek ve dolayısıyla merkezi bir CORS değişikliği gerekmez (sözleşme adım 4 ile uyumlu).

## Giriş ve probe

`browser-entry.tsx` üretim `ShellProvider` + `AppShell` + `shellRoutes` kullanır. Product route'ları henüz `src/shell/router.tsx`'e spread edilmediği için (shell sahibinin deltası, bkz. `src/product/SHARED_MOUNT_HANDOFF.md`) giriş, `productRoutes`'u aynı layout route'un çocuklarına, catch-all'dan önce ekler; `urun` yolu zaten varsa eklemez — delta main'e girince aynı giriş gerçek router'ı ölçmeye devam eder.

`window.urun`: `read()` (faz, actor, şirket, epoch, revizyon, generation, yayımlı product capability'leri), `navigate(path)` (uygulama içi gezinme; yenileme değil), `pending(scope)` (bekleyen girişimin yalnız **fazı ve key'i**; payload asla).

## Koşum

```
cd tests/e2e
# T
CARI010_EVIDENCE_DIR=<kanıt-kökü> npx playwright test --config product-u01-u02/playwright.config.ts
# L — yalnız yayımlanmış ENV_READY ve Orkestratör'ün accepted hash'i ile (bugün yok → koşulmaz)
URUN_LANE=L URUN_ENVIRONMENT_MANIFEST=<...\acceptance\<runId>\environment-ready.json> URUN_ACCEPTED_CONTRACT_SHA256=<devir hash'i> CARI010_EVIDENCE_DIR=<kanıt-kökü> npx playwright test --config product-u01-u02/playwright.config.ts
```

Sahiplik C010 ile ortaktır (`tests/e2e/cari-acceptance-010/README.md`): her tarayıcı koşumu kendi `<kanıt>/runs/<id>` dizinini ve depo dışı taze Vite önbelleğini sahiplenir; hiçbir şey temizlenmez veya üzerine yazılmaz. L dilinde C009 canlı kanıt yazıcıları aynı koşum dizinine (`<koşum>/L/**`) yönlendirilir.

Kanıt: `run-owner.json`, `diagnostics/warm-up.json` ve `service/ui-*.json` (bağlama hash'leriyle), `tests.json` (alan beyaz listesi); T: `T/<slug>-<zaman>.json`; L: `L/checkpoints/*.request.json` + `*.ack.json` + `l01-handover.json`, `L/network/*.json` (C009 `LiveRecorder`: `authorization`/`cookie` asla kopyalanmaz, sorgu değerleri düşürülür), `L/withheld/*.json`, `L/authority/*.json`, `L/screenshots/*.png`, Playwright trace. T dev sunucusu loopback `15175`; L dili rezerve `26173`. Cari'nin 25xxx süreçlerine dokunulmaz.
