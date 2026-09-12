# U-01 / U-02 — Ürün ekranları için gerçek-tarayıcı kabul hazırlığı (T dili)

Kontrollü API yanıtlarıyla, değiştirilmemiş frontend'i gerçek Chromium içinde sürer. Cari C-DESKTOP-010 desenini **tüketir**: aynı koşum sahipliği modülü, aynı yanıt yardımcıları, aynı Vite profili şekli. İkinci bir shell, router, query altyapısı, sahiplik sözleşmesi veya test sistemi kurulmadı.

**Bu bir ERP kabulü değildir.** Canlı Product API'si, SQL, gerçek login veya sunucunun `product.*` capability yayını yoktur; hepsi kontrollü `route.fulfill` yanıtıdır. Gerçek ortam (26173 UI / 26080 API / 26433 SQL) hazır olduğunda L dili ayrıca yazılır; buradaki hiçbir sonuç `PASS`/kabul sayılmaz.

## Ne ölçüyor

| Spec | Ölçülen |
| --- | --- |
| `u-01-list-card` | liste → satır → kart; hiçbir Product isteğinde `X-Company-Id` yok; etiket/eksiklik karttan; "okunamadı" ≠ "atanmadı" |
| `u-01-authority` | capability yoksa nötr blok + sıfır Product okuması; liste/kart 403 → route kapanır, kayıt açığa çıkmaz; liste grant'i kart açmaz |
| `u-02-create-edit-journey` | liste → yeni → Draft (sunucu kodu) → kart → düzenle + gerekçe → taze sürüm → karta dön; tek POST/tek PATCH, farklı key'ler, key alfabesi; DIFF + `expectedVersion` okunan karttan |
| `u-02-unknown-same-key` | 503 → aynı key/aynı gövde ile retry → `isReplay` kabul; unknown → retry 403 → route kapanır → **gen-2** ile taze giriş girişimi bulur, otomatik POST yok, insan retry'ı ilk key |
| `u-02-context-change` | dirty create + şirket değişimi: Kal tutar; onaylı bırak düşürür, diğer şirket boş, geri dönüşte gelmez, resend yok |
| `u-02-edit-conflicts` | `version_conflict`: iki sürüm, girdi korunur, yalnız "Güncel hâli yükle"; `lifecycle_conflict` adıyla |

Ölçülmeyen ve açık kalan: gerçek yetki/capability yayını, canlı idempotency replay, SQL kalıcılığı, sayfa yenilemesi sonrası (bellek-içi girişim tasarım gereği biter), klavye/zoom erişilebilirliği, `references` ve aktivasyon (sonraki dilim).

## Giriş ve probe

`browser-entry.tsx` üretim `ShellProvider` + `AppShell` + `shellRoutes` kullanır. Product route'ları henüz `src/shell/router.tsx`'e spread edilmediği için (shell sahibinin deltası, bkz. `src/product/SHARED_MOUNT_HANDOFF.md`) giriş, `productRoutes`'u aynı layout route'un çocuklarına, catch-all'dan önce ekler; `urun` yolu zaten varsa eklemez — delta main'e girince aynı giriş gerçek router'ı ölçmeye devam eder.

`window.urun`: `read()` (faz, actor, şirket, epoch, revizyon, generation, yayımlı product capability'leri), `navigate(path)` (uygulama içi gezinme; yenileme değil), `pending(scope)` (bekleyen girişimin yalnız **fazı ve key'i**; payload asla).

## Koşum

Sahiplik ve değişkenler C010 ile ortaktır (`tests/e2e/cari-acceptance-010/README.md`): `CARI010_EVIDENCE_DIR` (zorunlu, depo dışı mutlak yol), isteğe bağlı `CARI010_RUN_ID`, `CARI010_VITE_CACHE_DIR`. Her koşum kendi `<kanıt>/runs/<id>` dizinini ve depo dışı taze Vite önbelleğini sahiplenir; hiçbir şey temizlenmez veya üzerine yazılmaz.

```
cd tests/e2e
CARI010_EVIDENCE_DIR=<mutlak-kanıt-kökü> npx playwright test --config product-u01-u02/playwright.config.ts
```

Kanıt: `run-owner.json`, `diagnostics/warm-up.json`, `tests.json` (alan beyaz listesi), `T/<slug>-<zaman>.json` (istek kayıtları: yöntem, yol, sorgu **adları**, iki kimlik-dışı başlık, payload anahtarları + SHA, durum; gövde/çerez/ortam yok). Dev sunucusu loopback `127.0.0.1:15175`; rezerve 26173 kullanılmaz.
