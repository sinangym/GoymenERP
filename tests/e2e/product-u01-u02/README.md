# U-01 / U-02 — Ürün ekranları için gerçek-tarayıcı kabul hazırlığı

İki dil, `URUN_LANE` ile seçilir:

- **T** (varsayılan): kontrollü API yanıtları tarayıcıda `route.fulfill` ile verilir; hiçbir API'ye ulaşılmaz. Bileşen kanıtıdır.
- **L**: Backend'in **ENV_READY** tuple'ındaki canlı Product API'si, Vite proxy üzerinden. Hiçbir yanıt test tarafından üretilmez.

Her ikisi de Cari C-DESKTOP-010/009 desenini **tüketir**: aynı koşum sahipliği modülü, aynı yanıt yardımcıları, aynı canlı kayıt/probe/kanıt yazıcıları, aynı Vite profili şekli. İkinci bir shell, router, query altyapısı, sahiplik sözleşmesi, rapor altyapısı veya test sistemi kurulmadı.

**Hiçbir koşum kendi başına ERP kabulü değildir.** T dilinde canlı API/SQL/login/capability yayını yoktur. L dili yalnız aynı Backend'in gerçek ENV_READY tuple'ıyla koşar; `PASS`/kabul hükmü ortam yayımcısının ve orkestratörün alanıdır. Fixture verisi hiçbir yerde canlı sonuç yerine kullanılmaz.

## T dili — ne ölçüyor

| Spec | Ölçülen |
| --- | --- |
| `u-01-list-card` | liste → satır → kart; hiçbir Product isteğinde `X-Company-Id` yok; etiket/eksiklik karttan; "okunamadı" ≠ "atanmadı" |
| `u-01-authority` | capability yoksa nötr blok + sıfır Product okuması; liste/kart 403 → route kapanır, kayıt açığa çıkmaz; liste grant'i kart açmaz |
| `u-02-create-edit-journey` | liste → yeni → Draft (sunucu kodu) → kart → düzenle + gerekçe → taze sürüm → karta dön; tek POST/tek PATCH, farklı key'ler, key alfabesi; DIFF + `expectedVersion` okunan karttan |
| `u-02-unknown-same-key` | 503 → aynı key/aynı gövde ile resubmit ve retry → `isReplay` kabul; unknown → retry 403 → route kapanır → **gen-2** ile taze giriş girişimi bulur, otomatik POST yok, insan retry'ı ilk key |
| `u-02-context-change` | dirty create + şirket değişimi: Kal tutar; onaylı bırak düşürür, diğer şirket boş, geri dönüşte gelmez, resend yok |
| `u-02-edit-conflicts` | `version_conflict`: iki sürüm, girdi korunur, yalnız "Güncel hâli yükle"; `lifecycle_conflict` adıyla |

Son yerel T koşumu: 14/14, gerçek Chromium (`ba67665`). T kampanyası kabul için tekrar koşulmaz.

## L dili — kaynak hazır, koşum ENV_READY ile

| Spec | Ölçülen (hepsi canlı, hiçbiri mock değil) |
| --- | --- |
| `l-01-journey` | create → liste (aramayla bulunur) → kart → düzenle + gerekçe → taze sürüm → karta dön → listeden yeniden aç. **İki komut da gerçek kayıp yanıt + gerçek aynı-key replay'den geçer:** ilk POST ve ilk PATCH test tarafından `route.fetch` ile gerçek API'ye iletilir; gerçek 201/200 ve gerçek ürün kimliği (id/code/version, `lifecycle: Draft`) **görüldükten sonra** tarayıcıya teslimi kesilir (`upstream_success_then_delivery_abort`). Kullanıcı aynı key ile retry eder; sıradan tarayıcı isteği gerçek `isReplay: true` ile döner. Kod, id ve sürümler yalnız gerçek yanıtlardan; PATCH `expectedVersion` karttan okunan sürüm; hiçbir Product isteğinde şirket başlığı yok. |
| `l-02-authority` | Product yetkisi olmayan actor (`unauthorizedActor`) doğrudan probe ile liste/kart/create → gerçek 403 + `forbidden`, reddedilen ad açığa çıkmaz; yetkili actor'ün aramasında o ad yok (hiçbir şey oluşmadı); var olmayan id → nötr blok, id doğrulanmaz. |

Ölçülmeyen ve açık kalan: gerçek login (dev identity başlığı), SQL kalıcılığının veritabanı tarafı (ortamın kendi snapshot/verify aracı), sayfa yenilemesi sonrası (bellek-içi girişim tasarım gereği biter), klavye/zoom erişilebilirliği, `references` ve aktivasyon (sonraki dilim).

### ENV_READY tuple — bu dilin Backend'den beklediği tek dar girdi

`URUN_ENVIRONMENT_MANIFEST` ile verilen JSON; eksik/boş alan koşumu Vite ve tarayıcı başlamadan reddeder:

| Alan | Anlam |
| --- | --- |
| `readyForBrowser: true` | Yayımcı ortamı tarayıcıya hazır ilan etti. |
| `runId`, `instanceId` | Ortamın kimliği; her kanıt kaydına yazılır. |
| `apiBaseUrl` | Canlı Product API tabanı (rezerve `http://127.0.0.1:26080`); Vite `/api` proxy hedefi. |
| `actor` | **Mevcut global Product yetkisi** olan dev kimliği; bundle `X-Development-User` olarak gönderir (`VITE_DEV_USER`). |
| `unauthorizedActor` | Product yetkisi olmayan, yalnız şirket üyesi dev kimliği; yalnız L-02 probe'u kullanır. |
| `companyId` | Shell'in bağlanacağı şirket; Product global olduğu için hiçbir Product isteğine ulaşmaz (kayıtla doğrulanır). |
| `browserPrefix` | Bu koşumun oluşturduğu her kaydın adında; yabancı satır atfedilebilir. |
| `erpAccepted: false`, `productionActivated: false` | `productionActivated: true` koşumu reddeder. |

Ayrıca ortamda en az bir gerçek kod grubu (`GET /products/code-groups` boş değil) ve UI origin'i `http://127.0.0.1:26173`'e izin veren CORS gerekir; L dili rezerve 26173'e bağlanır ve farklı bir port isteğini reddeder. Bu alanlar dışında yeni endpoint/field talebi yoktur; bir tanesi sağlanamıyorsa tek dar kaynak devri bu tabloya işlenir.

## Giriş ve probe

`browser-entry.tsx` üretim `ShellProvider` + `AppShell` + `shellRoutes` kullanır. Product route'ları henüz `src/shell/router.tsx`'e spread edilmediği için (shell sahibinin deltası, bkz. `src/product/SHARED_MOUNT_HANDOFF.md`) giriş, `productRoutes`'u aynı layout route'un çocuklarına, catch-all'dan önce ekler; `urun` yolu zaten varsa eklemez — delta main'e girince aynı giriş gerçek router'ı ölçmeye devam eder.

`window.urun`: `read()` (faz, actor, şirket, epoch, revizyon, generation, yayımlı product capability'leri), `navigate(path)` (uygulama içi gezinme; yenileme değil), `pending(scope)` (bekleyen girişimin yalnız **fazı ve key'i**; payload asla).

## Koşum

Sahiplik ve değişkenler C010 ile ortaktır (`tests/e2e/cari-acceptance-010/README.md`): `CARI010_EVIDENCE_DIR` (zorunlu, depo dışı mutlak yol), isteğe bağlı `CARI010_RUN_ID`, `CARI010_VITE_CACHE_DIR`. Her koşum kendi `<kanıt>/runs/<id>` dizinini ve depo dışı taze Vite önbelleğini sahiplenir; hiçbir şey temizlenmez veya üzerine yazılmaz. L dilinde 009 canlı kanıt yazıcıları aynı koşum dizinine (`<koşum>/L/**`) yönlendirilir.

```
cd tests/e2e
# T
CARI010_EVIDENCE_DIR=<mutlak-kanıt-kökü> npx playwright test --config product-u01-u02/playwright.config.ts
# L — yalnız Backend ENV_READY tuple'ı ile
URUN_LANE=L URUN_ENVIRONMENT_MANIFEST=<manifest.json> CARI010_EVIDENCE_DIR=<mutlak-kanıt-kökü> npx playwright test --config product-u01-u02/playwright.config.ts
```

Kanıt: `run-owner.json`, `diagnostics/warm-up.json`, `tests.json` (alan beyaz listesi), T: `T/<slug>-<zaman>.json` (yöntem, yol, sorgu **adları**, iki kimlik-dışı başlık, payload anahtarları + SHA, durum); L: `L/network/*.json` (009 `LiveRecorder`: gerçek durum/başlıklar, `authorization`/`cookie` asla kopyalanmaz, sorgu değerleri düşürülür), `L/withheld/*.json`, `L/authority/*.json`, `L/screenshots/*.png`, Playwright trace. T dev sunucusu loopback `15175`; L dili rezerve `26173`. Cari'nin 25xxx süreçlerine dokunulmaz.
