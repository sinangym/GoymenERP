# Kritik Analiz — Shell CompanyContext / async fence / stale-response

- Tarih: 2026-08-31
- Protokol: `GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md` §6 Adım 4 (mimari çatal) + §9
- Üst sözleşme: `ERP-000K_SHELL_CONTRACT_v1.md` §8, §9, §13, §18.2
- İncelenen main: `e9c48605260e52a2905609dc377e14e9dbb68582`
- Analizi yapan: `CLAUDE-FRONTEND`
- Durum: **ADVERSARIAL REVIEW BEKLİYOR — reviewer: CODEX-PLATFORM** (CompanyContext + `/identity/me` semantic owner)
- Karar sınıfı: `SHELL-PILOT-01` implementation'ını bağlayan mimari; kod eklemez.

---

## 0. Problem

`ERP-000K_SHELL_CONTRACT` §13, şirket değişimi (ve login/logout, permission daraltması) sırasında 10 zorunluluk sıralar. Bunların çoğu **yalnız `AbortController` ile karşılanmaz** (§13 son paragraf açıkça söyler). Gecikmiş resolve, iptal edilemeyen write, cache sızıntısı ve kısa süreli eski-veri gösterimi senaryoları browser testinde kanıtlanmalıdır (`AT-008`, task-local senaryo 1/6/7/9).

Bu analiz, shell'in bu 10 maddeyi hangi mekanizmayla karşılayacağını sabitler. `SHELL-PILOT-01` bu tasarıma göre kodlanır.

## 1. §13 zorunluluklarının mekanizmaya eşlenmesi

| § | Zorunluluk | Mekanizma |
|---|---|---|
| 13.1 | Dirty form/workspace/onay preview sessizce taşınmaz | **Dirty-gate**: switch isteği açık `dirtyScopes` seti varsa engellenir |
| 13.2 | Kullanıcı kal/vazgeç veya güvenli save tamamlamadan context commit edilmez | Dirty-gate kullanıcı kararına kadar `pendingSwitch` tutar |
| 13.3 | Pending write sonucu bilinmiyorsa switch duplicate write tetiklemez | **Write-registry**: aktif mutation'lar `contextEpoch` ile etiketli; unknown-outcome write ERP-000E replay anahtarıyla tutulur, switch onu tekrar göndermez |
| 13.4 | Eski şirket request'i iptal + geç response atılır | **Context epoch fence**: her request `epoch` taşır; `epoch < currentEpoch` olan response reducer/cache'e yazılmadan düşer |
| 13.5 | Query/cache/optimistic/total/suggestion contextler arası paylaşılmaz | **Cache namespace**: her `queryKey` ilk elemanı `ctxKey` (aşağıda) |
| 13.6 | Yeni context server tarafından yeniden yetkilendirilmeden route/data açılmaz | Switch akışında `capabilityQuery` + route loader reauth zorunlu adım |
| 13.7 | Eski şirket verisi skeleton/animation/error fallback/history'de görünmez | Epoch değişiminde ilgili subtree `key={ctxKey}` ile unmount → remount; hiçbir eski state taşınmaz |
| 13.8 | Açık modal/drawer kapanır veya yeniden yetkilendirilir; focus güvenli yere döner | **Overlay-registry**: switch'te tüm açık overlay'ler `onContextChange` ile kapatılır, focus `main`'e |
| 13.9 | Logout/kullanıcı değişiminde hassas view/form state yeni session'a taşınmaz | `ctxKey` `userId` içerir → kullanıcı değişimi tüm subtree'yi remount eder; `sessionStorage`/memory temizlenir |
| 13.10 | Permission daraltması açık page/favorite/recent/background refresh'i yeniden doğrular | `policyRevision` değişimi `capabilityQuery`'yi invalidate eder; aktif route loader yeniden çalışır |

## 2. Context epoch fence — çekirdek mekanizma

### 2.1 `contextEpoch`

Shell store'da monotonik artan tamsayı. Şu olaylarda `++`:

- şirket değişimi (tek şirket ↔ tek şirket, veya grup-okuma moduna geçiş)
- login / logout / kullanıcı değişimi
- `policyRevision` değişimi (server bildirimi veya 401/403 sonrası capability yenileme)

### 2.2 Request etiketleme

`openapi-fetch` middleware'i her isteğe iki şey ekler:

- header `X-Company-Id` (tek şirket modunda; grup-okuma route'unda owner contract'ına göre bilinçli atlanır — `ERP-000K` §9.2)
- **istemci-içi** kapanış: request başlatıldığında `epoch = store.contextEpoch` yakalanır

Response geldiğinde: `if (epoch !== store.contextEpoch) → discard` (ne cache'e ne state'e). Bu, `AbortController` iptali kaçırılsa bile (ör. response body okunurken switch olur) eski veriyi keser — §13.4/13.7.

### 2.3 TanStack Query entegrasyonu

`ctxKey` = `` `${userId}:${companyScopeMode}:${companyId ?? 'group'}:${policyRevision}` ``

- Her `queryKey`: `[ctxKey, domain, ...params]` → `ERP-000K` §13.5 namespace izolasyonu.
- Switch akışında: `queryClient.cancelQueries()` → `queryClient.removeQueries({ predicate: q => q.queryKey[0] !== newCtxKey })`. `removeQueries` (invalidate değil) — eski veri cache'te hiç kalmaz, §13.7.
- `gcTime` düşük tutulur; `staleTime` domain-bazlı.
- Mutation'lar `meta: { epoch }` taşır; `onSettled`'da epoch stale ise UI etkisi (toast/navigation) üretilmez — §13.3.

### 2.4 React ağaç sınırı

```
<ShellProviders>            ← store, queryClient (sabit)
  <AuthedApp key={userId}>  ← kullanıcı değişimi tüm ağacı remount
    <CompanyScope key={ctxKey}>   ← şirket/policy değişimi feature ağacını remount
      <RouterOutlet />
```

`key={ctxKey}` = §13.7/13.9 için en güçlü garanti: eski context'in hiçbir React state'i, effect'i, ref'i yeni context'e sızmaz. Maliyet: switch'te tam remount (kabul edilebilir; switch nadir ve kullanıcı-bilinçli).

## 3. Şirket değişim akışı (state machine)

```
IDLE
 │  user selects company X
 ▼
GUARD  ── dirtyScopes boş değil ──▶ CONFIRM (kal / vazgeç / güvenli-kaydet)
 │  dirtyScopes boş                         │ vazgeç
 ▼                                          ▼
TEARDOWN                              (GUARD'a dön veya IDLE)
 │  1. tüm açık overlay kapat, focus main
 │  2. queryClient.cancelQueries()
 │  3. write-registry: unknown-outcome mutation varsa → epoch'a bağla, replay anahtarını koru
 │  4. store.contextEpoch++, store.companyId = X, ctxKey yeniden hesapla
 │  5. queryClient.removeQueries (yeni ctxKey dışı)
 ▼
REAUTH
 │  capabilityQuery(X) yeniden çalışır  → 401/403/stale → FAIL-CLOSED (X'e geçme, hata state)
 │  başarılı → capability cache yeni ctxKey altında
 ▼
MOUNT
 │  <CompanyScope key=yeni-ctxKey> remount, mevcut route loader yeniden çalışır (server reauth)
 │  route yeni context'te yetkisiz → non-disclosing yönlendirme (§10.2)
 ▼
IDLE (yeni context)
```

Grup-okuma moduna geçiş aynı akış; `companyId = null`, `companyScopeMode = 'group'`, `X-Company-Id` gönderilmez, yalnız owner-declared route'lar açılır (`ERP-000K` §9.2).

## 4. Dirty state kaydı

- Feature bileşenleri `useDirtyScope(id, isDirty)` ile shell store'a kayıt olur (form dirty, workspace düzenlemede, onay preview açık).
- Switch GUARD bu seti okur. Boş değilse `CONFIRM` — kullanıcı görür: hangi ekran(lar) kayıtsız.
- "Güvenli kaydet" seçeneği yalnız feature owner kabul edilmiş save akışı sunuyorsa görünür (`ERP-000K` §13.2); shell generic save uydurmaz.
- `beforeunload` ayrıca bağlanır (sekme kapatma / hard reload).

## 5. Pending write + ERP-000E köprüsü

- Her mutation başlarken write-registry'ye `{ key: idempotencyKey, epoch, status: 'pending' }`.
- Response gelirse `status = 'settled'`, kayıt düşer.
- **Unknown outcome** (network hatası, timeout): `status = 'unknown'` kalır. Switch bu kaydı **silmez** ve **tekrar göndermez** (§13.3). Kullanıcı yeni context'e geçer; eski context'e döndüğünde veya ilgili ekranı açtığında ERP-000E result/replay sözleşmesi (aynı `idempotencyKey`) sonucu çözer.
- Bu, `AT-005` (concurrency conflict) ve task-local senaryo 8 (unknown pending write duplicate üretmez) için browser testinde kanıtlanır.

## 6. Codex'ten gereken sözleşme girdileri

Bu tasarım şu backend gerçeklerine bağlı — Codex `W0` içinde sağlamalı, yoksa `HANDOFF-*`:

1. **`/api/v1/identity/me` capability yanıtı** — `ERP-000K` §8: bugün yalnız `UserId/DisplayName/Email/AllowedCompanyIds` dönüyor. Gerekli ek: `policyRevision` (opaque string), her şirket için `capabilities`/`scope`. Exact şekil ayrı platform contract'ında kabul edilecek (§8.1). **Bu olmadan §13.6/13.10 tam kapanmaz** — `SHELL-PILOT-01` `policyRevision`'ı bootstrap'ta sabit alır, `ASSUMPTION` kaydı açılır.
2. **Stale/invalid capability sinyali** — server 401/403 veya explicit "capability stale" → shell fail-closed (§8.2). Header veya body alanı Codex kararı.
3. **Route-level reauthorization** — her tenant endpoint `X-Company-Id`'yi yeniden doğrular (mevcut davranış, korunur).
4. **Grup-okuma route beyanı** — hangi endpoint'ler header'sız çok-şirket okumayı destekler (`ERP-000K` §9.2); OpenAPI'de işaretlenmeli (extension veya tag).

## 7. Ölçülen alternatifler

| Yaklaşım | Neden seçilmedi |
|---|---|
| Yalnız `AbortController` | §13 son paragraf reddediyor; gecikmiş resolve / body okuma sırasında switch keslemez |
| Route değişiminde manuel cache temizliği | Hataya açık; her feature'ın hatırlaması gerekir; §13.5 garanti edilmez |
| `queryClient.clear()` (tümü) | Kullanıcı state'i (tercih, layout) de gider; aşırı |
| Context'i URL'ye koymak | `ERP-000K` §14: CompanyId URL'de sızıntı yüzeyi; ayrıca deep-link paylaşımı yetki taşımaz |
| **Epoch fence + `key={ctxKey}` + namespace + write-registry** | §13'ün 10 maddesini birlikte karşılıyor; remount maliyeti switch nadirliğiyle kabul edilebilir |

## 8. Test planı (`ERP-000K` §18.2 task-local senaryolar)

Browser E2E (Playwright), `SHELL-PILOT-01` kabul kapısı:

- **S1**: A kullanıcısı URL/localStorage/cache üzerinden B şirket kaydının varlığını öğrenemez.
- **S6**: Dirty formda switch → GUARD/CONFIRM; kullanıcı kararı öncesi context değişmez.
- **S7**: A'nın yavaş response'u (artificial delay) switch sonrası B context'ine uygulanmaz — epoch fence.
- **S8**: Timeout'lu write → switch → duplicate write yok; geri dönüşte ERP-000E replay.
- **S9**: Cache/count/suggestion şirketler ve kullanıcılar arası paylaşılmaz — `queryKey[0]` denetimi.
- **S10**: `policyRevision` değişimi → açık route/drawer/favorite yeniden doğrulanır.
- **S2**: Missing/stale/malformed capability → fail-closed; menü görünürlüğü permission üretmez.
- Component test: switch state machine reducer; write-registry epoch etiketleme; `useDirtyScope`.

## 9. Codex adversarial review isteği

```text
REVIEWED HEAD: <PR head SHA>
KATMAN: K3 — doğru | yanlış
SOURCE OF TRUTH: ERP-000K §8/§9/§13/§18.2, OTONOM_TESLIM_PLANI §6/§9, AT-005/AT-008
EPOCH FENCE YETERLİ Mİ: §13.1-13.10 eşlemesi doğru | <açık madde>
/identity/me CAPABILITY ŞEKLİ: policyRevision + per-company capability önerisi kabul | <karşı öneri>
GRUP-OKUMA BEYANI: OpenAPI'de nasıl işaretlenmeli — öneri | <karşı öneri>
ERP-000E KÖPRÜSÜ: unknown-outcome write akışı idempotency sözleşmesiyle tutarlı | <bulgu>
REMOUNT MALİYETİ: key={ctxKey} kabul edilebilir | <endişe>
KARAR: HANDOFF-ACCEPTED | BLOCKER
```

Yakınsarsa: bu tasarım `SHELL-PILOT-01` foundation PR'ının kabul kriteri olur; `/identity/me` genişletme ayrı Codex platform PR'ı + bu tarafta `ASSUMPTION` köprüsü.
