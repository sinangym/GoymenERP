# Kritik Analiz — Shell CompanyContext / async fence / stale-response

- Tarih: 2026-08-31
- Protokol: `GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md` §6 Adım 4 (mimari çatal) + §9
- Üst sözleşme: `ERP-000K_SHELL_CONTRACT_v1.md` §8, §9, §13, §18.2
- Bağlı: Issue #120 (`ORGANIZATION-COMPANY-DIRECTORY-V1`), Issue #122 (`W0-CICD-001`)
- İncelenen main: `e9c48605260e52a2905609dc377e14e9dbb68582`
- Analizi yapan: `CLAUDE-FRONTEND`
- Durum: **REV.1 — ChatGPT-BUSINESS BLOCKER (4×P1) ele alındı; CODEX-PLATFORM fresh exact-head review bekliyor**
- Karar sınıfı: `SHELL-PILOT-01` implementation'ını bağlayan mimari; kod eklemez.

---

## Revizyon günlüğü

| Rev | Tarih | Değişiklik |
|---|---|---|
| 0 | 2026-08-31 | İlk analiz (head `676abb9`) |
| 1 | 2026-08-31 | ChatGPT-BUSINESS security-boundary review (PR #119) BLOCKER yanıtı: P1-1 (fabricated revision kaldırıldı → fail-closed disabled shell), P1-2 (iki fazlı geçiş, `activeContext + pendingTarget`), P1-3 (opak capability snapshot, `/identity/me` alan zorunluluğu kaldırıldı), P1-4 (grup-okuma yetkisi server route registry'den, OpenAPI yalnız projeksiyon). Hardening: namespace invariant testi, context-free query sahipliği, write-registry payload-hash + reauth-before-replay + insecure storage yasağı, başarısız-reauth browser testi. |

---

## 0. Problem

`ERP-000K_SHELL_CONTRACT` §13, şirket değişimi (ve login/logout, permission daraltması) sırasında 10 zorunluluk sıralar. Bunların çoğu **yalnız `AbortController` ile karşılanmaz** (§13 son paragraf açıkça söyler). Gecikmiş resolve, iptal edilemeyen write, cache sızıntısı ve kısa süreli eski-veri gösterimi senaryoları browser testinde kanıtlanmalıdır (`AT-008`, task-local senaryo 1/6/7/9).

Bu analiz, shell'in bu 10 maddeyi hangi mekanizmayla karşılayacağını sabitler. `SHELL-PILOT-01` bu tasarıma göre kodlanır. **Bu doküman capability endpoint şekli, permission alanı veya route yetkisi SEÇMEZ — o kararlar Codex-owned platform contract'larına aittir.**

## 1. §13 zorunluluklarının mekanizmaya eşlenmesi

| § | Zorunluluk | Mekanizma |
|---|---|---|
| 13.1 | Dirty form/workspace/onay preview sessizce taşınmaz | **Dirty-gate**: switch isteği açık `dirtyScopes` seti varsa engellenir |
| 13.2 | Kullanıcı kal/vazgeç veya güvenli save tamamlamadan context commit edilmez | Dirty-gate kullanıcı kararına kadar `pendingTarget` tutar; `activeContext` değişmez |
| 13.3 | Pending write sonucu bilinmiyorsa switch duplicate write tetiklemez | **Write-registry**: aktif mutation'lar `contextEpoch` ile etiketli; unknown-outcome write payload-hash + command identity + context binding ile tutulur, switch onu tekrar göndermez |
| 13.4 | Eski şirket request'i iptal + geç response atılır | **Context epoch fence**: her request `epoch` taşır; `epoch !== currentEpoch` olan response reducer/cache'e yazılmadan düşer |
| 13.5 | Query/cache/optimistic/total/suggestion contextler arası paylaşılmaz | **Doğrulanmış namespace**: `queryKey[0]` = context-owned key; invariant testi (§2.4) her feature query'sini denetler |
| 13.6 | Yeni context server tarafından yeniden yetkilendirilmeden route/data açılmaz | Switch akışında `pendingTarget` için server reauthorization; başarıdan önce hiçbir X verisi/route'u açılmaz |
| 13.7 | Eski şirket verisi skeleton/animation/error fallback/history'de görünmez | Epoch değişiminde feature subtree `key={ctxKey}` ile unmount → nötr switching state → yeni context mount |
| 13.8 | Açık modal/drawer kapanır veya yeniden yetkilendirilir; focus güvenli yere döner | **Overlay-registry**: switch'te tüm açık overlay'ler `onContextChange` ile kapatılır, focus `main`'e |
| 13.9 | Logout/kullanıcı değişiminde hassas view/form state yeni session'a taşınmaz | `ctxKey` `userId` içerir → kullanıcı değişimi tüm subtree'yi remount eder; memory temizlenir; hassas veri insecure storage'a hiç yazılmaz |
| 13.10 | Permission daraltması açık page/favorite/recent/background refresh'i yeniden doğrular | Server capability snapshot revizyon değişimi → `ctxKey` değişir → subtree remount + route reauth |

## 2. Context epoch fence — çekirdek mekanizma

### 2.1 `contextEpoch`

Shell store'da monotonik artan tamsayı. Şu olaylarda `++` **(yalnız atomik commit anında, bkz. §3)**:

- şirket değişiminin **başarılı** commit'i (tek şirket ↔ tek şirket, veya grup-okuma moduna geçiş)
- login / logout / kullanıcı değişimi
- server capability snapshot revizyon değişimi (server bildirimi veya 401/403 sonrası snapshot yenileme)

Başarısız switch epoch'u artırmaz; `activeContext` değişmez.

### 2.2 Request etiketleme

Transport wrapper her isteğe iliştirir:

- header `X-Company-Id` — **yalnız tek şirket modunda** ve **yalnız `activeContext` bağlıyken**; grup-okuma route'unda server-owned route registry beyanına göre bilinçli atlanır (§6.4)
- **istemci-içi** kapanış: request başlatıldığında `epoch = store.contextEpoch` yakalanır

Response geldiğinde: `if (epoch !== store.contextEpoch) → discard` (ne cache'e ne state'e). Bu, `AbortController` iptali kaçırılsa bile (ör. response body okunurken switch olur) eski veriyi keser — §13.4/13.7.

### 2.3 Capability: opak server-backed snapshot

**(REV.1 — P1-3 düzeltmesi)**

Shell, yetenek/yetki tazeliğini **opak bir server-backed capability snapshot** üzerinden alır. Bu snapshot:

- actor + session + aktif context + kabul edilmiş contract revizyonuna bağlıdır;
- **exact endpoint, envelope, alan adları ve transport Codex-owned platform contract'ında kabul edilir** — bu doküman bunları seçmez ve `/api/v1/identity/me`'ye yeni alan zorunluluğu getirmez;
- shell'e yalnız **kaba feature/action kullanılabilirliği** ("bu mount açık mı / bu aksiyon görünür mü") verir — permission listesi, ABAC scope envanteri veya rol adı **vermez** ve shell bunları authority saymaz;
- opak bir `snapshotId` / revizyon işareti taşır; shell yalnız "değişti mi" bilgisini kullanır, içeriğini yorumlamaz;
- endpoint-side reauthorization'ın yerine geçmez — her tenant çağrısı server'da yeniden doğrulanır.

**Snapshot çözülemediğinde (P1-1 düzeltmesi):**

```text
capability/snapshot unresolved | missing | malformed | stale
→ base shell + safe company-directory state (Issue #120) YALNIZCA
→ tüm capability-gated feature mount ve action FAIL-CLOSED
→ uydurma revizyon YOK; snapshot/geçici business route simüle EDİLMEZ
→ pilot devre dışı (disabled) shell render edebilir; kabul edilmiş bir capability revizyonu taklit EDEMEZ
```

`SHELL-PILOT-01` sabit bir bootstrap revizyonu kullanmaz.

### 2.4 TanStack Query entegrasyonu + namespace invariant

`ctxKey` = context-owned opak birleşik anahtar; kaba bileşenleri: `userId`, `companyScopeMode` (`single` | `group`), `companyId ?? 'group'`, `capabilitySnapshotId`.

- Her **context-bağlı** `queryKey`: `[ctxKey, domain, ...params]`.
- **Namespace invariant testi (P1 hardening):** repo'da bir lint/test kuralı, `useQuery`/`useMutation` çağrılarının `queryKey[0]`'ının merkezi `ctxKey` üreticisinden geldiğini denetler. Ham string prefix'e güvenilmez; her feature'ın hatırlaması gerekmez — kural bozan PR CI'da kırmızı.
- **Context-free / session / capability query'leri (P1 hardening):** ayrı ve açık sahiplik. Bunlar `ctxKey` altında DEĞİL; kendi namespace'leri (`['session', ...]`, `['capability', snapshotId]`) ve kendi invalidation kuralları var. Switch bunları `activeContext` verisi gibi silmez; capability query snapshot revizyonuyla, session query login/logout ile invalidate olur.
- Switch akışında (yalnız başarılı commit'te): `queryClient.cancelQueries()` → `queryClient.removeQueries({ predicate })` — predicate context-owned key namespace'ine scoped, context-free/session/capability korunur. `removeQueries` (invalidate değil) — eski context verisi cache'te hiç kalmaz.
- Mutation'lar `meta: { epoch }` taşır; `onSettled`'da epoch stale ise UI etkisi (toast/navigation) üretilmez — §13.3.

### 2.5 React ağaç sınırı

```
<ShellProviders>                 ← store, queryClient (sabit)
  <SessionScope key={userId}>    ← kullanıcı değişimi tüm ağacı remount
    <CompanyScope key={ctxKey}>  ← başarılı switch / snapshot revizyonu feature ağacını remount
      <RouterOutlet />
```

`key={ctxKey}` = §13.7/13.9 için en güçlü garanti: eski context'in hiçbir React state'i, effect'i, ref'i yeni context'e sızmaz. Maliyet: başarılı switch'te tam remount (kabul edilebilir; switch nadir ve kullanıcı-bilinçli). Nötr switching state remount sırasında görünür (§3).

## 3. Şirket değişim akışı — iki fazlı geçiş

**(REV.1 — P1-2 düzeltmesi: `activeContext` REAUTH başarısından önce X'e ayarlanmaz.)**

Store iki alan tutar: `activeContext` (bağlı, doğrulanmış) ve `pendingTarget` (aday, doğrulanmamış).

```
IDLE  (activeContext = A, pendingTarget = null)
 │  user selects company X
 ▼
GUARD ── dirtyScopes boş değil ──▶ CONFIRM (kal / vazgeç / güvenli-kaydet)
 │  dirtyScopes boş                              │ vazgeç → IDLE (A korunur)
 ▼
SET-PENDING   pendingTarget = X   (activeContext HÂLÂ A — commit yok)
 │  1. tüm açık overlay kapat, focus main
 │  2. queryClient.cancelQueries()  (context-owned namespace)
 │  3. write-registry: unknown-outcome mutation varsa epoch'a bağlı kalır, silinmez
 ▼
NEUTRAL-SWITCHING   activeContext = null-safe  (A verisi fence'lendi, X verisi YOK)
 │  UI: nötr "şirket değiştiriliyor" durumu; ne A ne X verisi/adı/route'u görünür
 ▼
REAUTH   server, pendingTarget = X için yeniden yetkilendirir + capability snapshot çözer
 │  başarısız (401/403/stale/malformed) ──▶ FAIL
 │  başarılı
 ▼
ATOMIC-COMMIT   tek işlemde:
 │  activeContext = X ; contextEpoch++ ; capabilitySnapshotId = yeni ; ctxKey yeniden hesapla
 │  queryClient.removeQueries (yeni ctxKey dışı context-owned)
 ▼
MOUNT   <CompanyScope key=yeni-ctxKey> remount; mevcut route loader server reauth ile yeniden çalışır
 │  route X'te yetkisiz → non-disclosing yönlendirme (§10.2)
 ▼
IDLE  (activeContext = X)

FAIL:
 → ya former context A'yı FRESH reauthorize edip geri yükle (A capability snapshot yeniden çözülür)
 → ya da explicit "aktif şirket yok" güvenli durumda kal
 → X hiçbir zaman aktif olarak görünmez/erişilmez; X'in adı/ID'si commit öncesi expose edilmez
```

Grup-okuma moduna geçiş aynı akış; `pendingTarget = { mode: 'group' }`, commit'te `companyId = null`, `companyScopeMode = 'group'`, `X-Company-Id` gönderilmez, yalnız server route registry'nin grup-okuma beyan ettiği route'lar açılır (§6.4).

## 4. Dirty state kaydı

- Feature bileşenleri `useDirtyScope(id, isDirty)` ile shell store'a kayıt olur (form dirty, workspace düzenlemede, onay preview açık).
- Switch GUARD bu seti okur. Boş değilse `CONFIRM` — kullanıcı görür: hangi ekran(lar) kayıtsız.
- "Güvenli kaydet" seçeneği yalnız feature owner kabul edilmiş save akışı sunuyorsa görünür (`ERP-000K` §13.2); shell generic save uydurmaz.
- `beforeunload` ayrıca bağlanır (sekme kapatma / hard reload).

## 5. Pending write + ERP-000E köprüsü

**(REV.1 — P1 hardening)**

- Her mutation başlarken write-registry'ye kayıt: `{ commandIdentity, payloadHash, contextBinding: {epoch, companyId, userId}, idempotencyKey, status: 'pending' }`.
- **`idempotencyKey` insecure browser storage'a (localStorage/sessionStorage) YAZILMAZ** — yalnız in-memory registry; sekme kapanırsa kayıt kaybolur ve sunucu ERP-000E replay'i tekil gerçeği tutar.
- Response gelirse `status = 'settled'`, kayıt düşer.
- **Unknown outcome** (network hatası, timeout): `status = 'unknown'` kalır. Switch bu kaydı **silmez** ve **tekrar göndermez** (§13.3).
- Kullanıcı yeni context'e geçer; eski context'e döndüğünde veya ilgili ekranı açtığında: **önce server reauthorization**, sonra ERP-000E result/replay sözleşmesi (aynı `commandIdentity` + `payloadHash`) sonucu çözer. Replay sonucu (kayıt oluştu mu / conflict mi) kullanıcıya **reauth'tan sonra** gösterilir; commit öncesi disclosure yok.
- Bu, `AT-005` (concurrency conflict) ve task-local senaryo 8 için browser testinde kanıtlanır.

## 6. Codex'ten gereken sözleşme girdileri

Bu tasarım şu backend gerçeklerine bağlı — Codex `W0` içinde sağlamalı, yoksa `HANDOFF-*`. **Bu doküman alan adı/endpoint/tag seçmez; ihtiyacı tanımlar.**

1. **Opak server-backed capability snapshot** — actor/session/context/contract-revision'a bağlı; shell'e yalnız kaba feature/action availability + opak revizyon işareti verir. Exact endpoint/envelope/transport Codex kararı (`ERP-000K` §8.1). **Çözülemezse:** shell fail-closed disabled (§2.3); `SHELL-PILOT-01` bunu bekler, uydurma revizyon kullanmaz. Bu bir `ASSUMPTION` değil — davranış fail-closed olarak tanımlı.
2. **Stale/invalid capability sinyali** — server 401/403 veya explicit "snapshot stale" → shell fail-closed (`ERP-000K` §8.2). Mekanizma Codex kararı.
3. **Route-level reauthorization** — her tenant endpoint `X-Company-Id`'yi (veya grup-okuma bağlamını) server'da yeniden doğrular (mevcut davranış, korunur).
4. **Grup-okuma route beyanı (P1-4 düzeltmesi)** — hangi route'ların header'sız çok-şirket okumayı desteklediği **kabul edilmiş server-owned route declaration / registry**'den gelir. OpenAPI bu gerçeği generated client için **projekte edebilir**, ama tag veya elle yazılmış extension **güvenlik kaynağı değildir**. Shell bu route'ları yalnız server registry'nin beyanıyla grup modunda açar.

## 7. Ölçülen alternatifler

| Yaklaşım | Neden seçilmedi |
|---|---|
| Yalnız `AbortController` | §13 son paragraf reddediyor; gecikmiş resolve / body okuma sırasında switch keslemez |
| Route değişiminde manuel cache temizliği | Hataya açık; her feature'ın hatırlaması gerekir; §13.5 garanti edilmez (namespace invariant testi bunu çözer) |
| `queryClient.clear()` (tümü) | Kullanıcı state'i (tercih, layout) + session + capability de gider; aşırı |
| Context'i URL'ye koymak | `ERP-000K` §14: CompanyId URL'de sızıntı yüzeyi; deep-link paylaşımı yetki taşımaz |
| `activeContext`'i REAUTH öncesi X'e ayarlamak (rev.0) | P1-2: başarısız reauth'ta X'in kimliği/state'i expose olur; ambiguous context |
| Bootstrap `policyRevision` sabiti (rev.0) | P1-1: permission daraltması sonrası stale menü/cache/route korunur |
| `/identity/me`'ye per-company capability alanı (rev.0) | P1-3: client-visible permission/scope authority olamaz; kabul edilmiş envelope yok |
| **İki fazlı geçiş + opak snapshot + epoch fence + doğrulanmış namespace + write-registry** | §13'ün 10 maddesini + 4 P1'i birlikte karşılıyor |

## 8. Test planı (`ERP-000K` §18.2 task-local senaryolar)

Browser E2E (Playwright), `SHELL-PILOT-01` kabul kapısı:

- **S1**: A kullanıcısı URL/localStorage/cache üzerinden B şirket kaydının varlığını öğrenemez.
- **S2**: Missing/stale/malformed capability snapshot → fail-closed disabled shell; menü görünürlüğü permission üretmez; uydurma revizyon yok.
- **S6**: Dirty formda switch → GUARD/CONFIRM; `activeContext` kullanıcı kararı öncesi değişmez.
- **S7**: A'nın yavaş response'u (artificial delay) switch sonrası B context'ine uygulanmaz — epoch fence.
- **S8**: Timeout'lu write → switch → duplicate write yok; geri dönüşte önce reauth, sonra ERP-000E replay.
- **S9**: Cache/count/suggestion şirketler ve kullanıcılar arası paylaşılmaz — namespace invariant testi + runtime denetimi.
- **S10**: Snapshot revizyon değişimi → açık route/drawer/favorite yeniden doğrulanır (remount + route reauth).
- **S-FAIL (yeni, P1-2)**: `pendingTarget` reauthorization **başarısız** → X'in adı/ID'si hiçbir zaman aktif veya görünür olmaz; shell ya former context'i fresh reauth ile geri yükler ya da "aktif şirket yok" güvenli durumunda kalır.
- Component test: iki fazlı switch state machine reducer (`activeContext`/`pendingTarget`); write-registry payload-hash + context binding; `useDirtyScope`; namespace invariant lint kuralı.

## 9. Codex adversarial review isteği (fresh exact-head)

```text
REVIEWED HEAD: <REV.1 PR head SHA>
KATMAN: K3 — doğru | yanlış
SOURCE OF TRUTH: ERP-000K §8/§9/§13/§18.2, OTONOM_TESLIM_PLANI §6/§9, Issue #120, Issue #122, AT-005/AT-008
P1-1 (fabricated revision yok → fail-closed disabled shell): kabul | <eksik>
P1-2 (iki fazlı geçiş, activeContext commit-sonrası): akış tutarlı | <çelişki>
P1-3 (opak capability snapshot, endpoint Codex kararı): sınır doğru | <ihlal>
P1-4 (grup-okuma yetkisi server route registry): OpenAPI yalnız projeksiyon — kabul | <karşı öneri>
NAMESPACE INVARIANT: ctxKey merkezi üretici + lint/test kuralı yeterli | <endişe>
WRITE-REGISTRY: payload-hash + command identity + context binding + reauth-before-replay + no insecure storage — kabul | <bulgu>
CONTEXT-FREE/SESSION QUERY SAHİPLİĞİ: ayrı namespace + ayrı invalidation — kabul | <bulgu>
REMOUNT MALİYETİ: key={ctxKey} kabul edilebilir | <endişe>
KARAR: HANDOFF-ACCEPTED | BLOCKER
```

Yakınsarsa: bu tasarım `SHELL-PILOT-01` foundation PR'ının kabul kriteri olur. Opak capability snapshot endpoint'i ayrı Codex platform PR'ı; bu tarafta shell fail-closed davranışı (uydurma revizyon değil) beklenir.
