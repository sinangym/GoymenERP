# Kritik Analiz — Shell CompanyContext / async fence / stale-response

- Tarih: 2026-08-31
- Protokol: `GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md` §6 Adım 4 (mimari çatal) + §9
- Üst sözleşme: `ERP-000K_SHELL_CONTRACT_v1.md` §8, §9, §13, §18.2
- Bağlı: Issue #120 (`ORGANIZATION-COMPANY-DIRECTORY-V1`), Issue #122 (`W0-CICD-001`), Issue #124 (`W0-SHELL-001`)
- İncelenen main: `382d9b5` (branch bu head'e merge edildi)
- Analizi yapan: `CLAUDE-FRONTEND`
- Durum: **REV.2 — ChatGPT+Codex BLOCKER (P1-REV1-1..4 + hardening) ele alındı; fresh exact-head CODEX-PLATFORM + ChatGPT re-review bekliyor**
- Karar sınıfı: `SHELL-PILOT-01` (Issue #124) implementation'ını bağlayan **package-bağımsız güvenlik sözleşmesi**; kod eklemez, paket seçmez, endpoint/alan seçmez.

---

## Revizyon günlüğü

| Rev | Değişiklik |
|---|---|
| 0 | İlk analiz (head `676abb9`) |
| 1 | ChatGPT 4×P1: fabricated revision kaldırıldı, iki fazlı geçiş, opak snapshot, route registry |
| 2 | ChatGPT+Codex BLOCKER: **(REV1-1)** PR body REV.1 ile senkronlandı. **(REV1-2)** güvenlik değişmezi package-bağımsız yeniden yazıldı; TanStack Query / spesifik API yalnız §B'de "aday eşleme, #118/#122 kapısına bağlı". **(REV1-3)** ERP-000E tab-close: uydurma payload-hash backend lookup kaldırıldı; sekme kapanışı → explicit unknown-outcome/manuel mutabakat durumu, yeni anahtarla kör resend yasak. **(REV1-4)** capability cache key = local session/actor generation + context binding (yalnız opak snapshotId değil). Grup modu somut owner route declaration olmadan devre dışı. Package-bağımsız browser kabulü Playwright/TanStack "zorunlu" denmeden önce yazıldı. Branch `382d9b5`'e senkron. |

---

## 0. Problem ve sınır

`ERP-000K_SHELL_CONTRACT` §13, şirket değişimi (ve login/logout, permission daraltması) sırasında 10 zorunluluk sıralar; "yalnız `AbortController` yetmez" (§13 son paragraf). Gecikmiş resolve, iptal edilemeyen write, cache sızıntısı ve kısa süreli eski-veri gösterimi browser testinde kanıtlanmalıdır.

**Bu doküman ne yapar:** shell'in §13'ü hangi **package-bağımsız değişmezlerle** karşılayacağını sabitler (Bölüm A). Bu değişmezler `SHELL-PILOT-01` kabul kriteridir.

**Bu doküman ne yapmaz:** paket (router/query/table/E2E), capability endpoint şekli, permission alanı, route yetkisi veya generated-client biçimi **seçmez**. Bunlar sırasıyla PR #118/Issue #122, Codex-owned platform contract'ları ve server-owned route registry kararıdır. Bölüm B yalnız **bilgilendirici aday eşleme**dir ve normatif değildir.

---

## A. Package-bağımsız güvenlik değişmezleri (normatif)

### A.1 §13 → değişmez eşlemesi

| § | Zorunluluk | Package-bağımsız değişmez |
|---|---|---|
| 13.1 | Dirty form/workspace/onay preview sessizce taşınmaz | **INV-DIRTY**: açık `dirtyScopes` varken switch kullanıcı kararına kadar `activeContext`'i değiştiremez |
| 13.2 | Kullanıcı karar vermeden context commit edilmez | **INV-PENDING**: aday `pendingTarget` ayrı tutulur; `activeContext` yalnız §A.3 ATOMIC-COMMIT'te değişir |
| 13.3 | Pending write sonucu bilinmiyorsa switch duplicate write tetiklemez | **INV-WRITE**: aktif write'lar transition-generation'a bağlı; unknown-outcome write silinmez ve yeni anahtarla yeniden gönderilmez (§A.5) |
| 13.4 | Eski şirket request'i iptal + geç response atılır | **INV-FENCE**: her request başlatıldığı `transitionGeneration`'ı taşır; dönüşte generation güncel değilse response state/cache'e **yazılmaz** — iptal kaçırılsa bile |
| 13.5 | Query/cache/optimistic/total/suggestion contextler arası paylaşılmaz | **INV-NS**: her context-bağlı cache girişi context-owned namespace anahtarı taşır; anahtarın merkezi üreticiden geldiğini denetleyen invariant testi vardır (§A.4) |
| 13.6 | Yeni context server reauth olmadan route/data açılmaz | **INV-REAUTH**: `pendingTarget` için server reauthorization başarısız olana kadar hiçbir hedef verisi/route'u/kimliği açılmaz veya görünmez |
| 13.7 | Eski şirket verisi skeleton/animation/error/history'de görünmez | **INV-NEUTRAL**: transition sırasında ne eski ne yeni context verisi/adı görünür; nötr "değiştiriliyor" durumu |
| 13.8 | Açık modal/drawer kapanır/yeniden yetkilendirilir; focus güvenli yere | **INV-OVERLAY**: transition başında tüm açık overlay kapanır, focus ana içeriğe |
| 13.9 | Logout/kullanıcı değişiminde hassas view/form state yeni session'a taşınmaz | **INV-SESSION**: kullanıcı/session değişimi tüm context-bağlı ağacı ve in-memory state'i sıfırlar; hassas veri insecure storage'a hiç yazılmaz |
| 13.10 | Permission daraltması açık page/favorite/recent/background refresh'i yeniden doğrular | **INV-REVOKE**: capability generation değişimi context-bağlı ağacı yeniden bağlar + aktif route server reauth |

### A.2 Transition generation fence (INV-FENCE)

- Shell, monotonik artan bir `transitionGeneration` sayacı tutar. Şu **başarılı** olaylarda `++`: şirket switch commit, grup-okuma commit, login/logout/kullanıcı değişimi, capability generation değişimi. **Başarısız switch artırmaz.**
- Her giden request başlatıldığı generation'ı yakalar (istemci-içi kapanış, header değil).
- Response/settle anında: `capturedGeneration !== currentGeneration` → sonuç state'e ve cache'e **yazılmaz**. Bu değişmez, transport iptalinin (abort) kaçırıldığı durumda da (ör. body okuma sırasında switch) eski veriyi keser.
- Bu bir kütüphane özelliği değildir; herhangi bir data-fetch katmanının üstüne yazılabilen bir kurgudur.

### A.3 İki fazlı geçiş (INV-PENDING / INV-REAUTH / INV-NEUTRAL)

Store: `activeContext` (bağlı, doğrulanmış) + `pendingTarget` (aday, doğrulanmamış).

```
IDLE (active=A, pending=null)
 │ user selects X
 ▼
GUARD ── dirtyScopes ≠ ∅ ──▶ CONFIRM (kal / vazgeç / güvenli-kaydet) ── vazgeç ──▶ IDLE (A korunur)
 │ dirtyScopes = ∅
 ▼
SET-PENDING     pending = X ; active HÂLÂ A (commit yok)
 │  overlay'leri kapat, focus main ; giden request'leri iptal et
 ▼
NEUTRAL         active = <no-active-safe> ; A verisi fence'lendi ; X verisi YOK
 │  UI: nötr "şirket değiştiriliyor"; ne A ne X verisi/adı/route'u
 ▼
REAUTH          server, pending = X için yeniden yetkilendirir + capability generation çözer
 │  başarısız (401/403/stale/malformed) ──▶ FAIL
 │  başarılı
 ▼
ATOMIC-COMMIT   tek işlemde: active = X ; transitionGeneration++ ; capabilityGeneration = yeni ;
 │              context namespace anahtarı yeniden hesaplanır ; eski context-owned cache girişleri temizlenir
 ▼
MOUNT           context-bağlı ağaç yeni anahtarla yeniden bağlanır ; aktif route loader server reauth ile yeniden çalışır
 │              route X'te yetkisiz → non-disclosing yönlendirme (§10.2)
 ▼
IDLE (active = X)

FAIL:
 → former context A'yı FRESH reauthorize edip geri yükle (A capability yeniden çözülür)
   VEYA explicit "aktif şirket yok" güvenli durumda kal
 → X'in adı/ID'si/state'i commit öncesi hiç expose edilmez
```

Grup-okuma: aynı akış; `pending = { mode: 'group' }`. **Grup modu, somut bir server-owned owner route declaration açıkça grup-okumayı desteklediğini beyan etmeden devre dışıdır** — shell contract'ı tek başına kullanılabilir bir grup route'u yaratmaz (Codex+ChatGPT hardening).

### A.4 Context-owned namespace (INV-NS) + ayrı yaşam döngüleri

- **Context-bağlı** cache girişleri (liste, 360, count, suggestion, optimistic state) merkezi bir `contextKey` üreticisinden gelen anahtar taşır. `contextKey` kaba bileşenleri: `userId` (session/actor generation), `companyScopeMode`, `companyId` veya grup, `capabilityGeneration`.
- **Invariant testi:** repo lint/test kuralı, context-bağlı fetch/cache çağrılarının anahtarının merkezi üreticiden geldiğini denetler. Ham string prefix'e güvenilmez; kural bozan PR CI'da kırmızı.
- **Ayrı yaşam döngüleri (açık sahiplik):**
  - **session/actor** state → login/logout ile sıfırlanır; context switch'ten etkilenmez.
  - **company-directory** state (Issue #120) → context-free; kendi namespace'i; switch onu context verisi gibi silmez, yalnız yeniden doğrular.
  - **capability** state → `capabilityGeneration` + local session/actor binding ile anahtarlanır (yalnız opak bir global `snapshotId` değil — global benzersizliği henüz sözleşmeli değil); capability generation değişiminde invalidate.
- Switch commit'inde yalnız **context-owned** girişler temizlenir; session/directory/capability korunur ve kendi kurallarıyla yenilenir.

### A.5 Unknown-outcome write (INV-WRITE) — ERP-000E sınırı

**(REV.2 — uydurma backend lookup kaldırıldı)**

- Her write başında in-memory registry: `{ commandIdentity, payloadHash, contextBinding: {generation, companyId, userId}, idempotencyKey, status: 'pending' }`.
- `idempotencyKey` **insecure browser storage'a (localStorage/sessionStorage) yazılmaz** — yalnız in-memory.
- `payloadHash` yalnız **bütünlük/binding metadata**'sıdır; sonuç sorgulama yetkisi değildir.
- **Oturum-içi şirket switch'i:** anahtar in-memory korunur; kullanıcı eski context'e döndüğünde **önce fresh reauthorization**, sonra ERP-000E'nin **anahtar-tabanlı** replay sözleşmesiyle (aynı `idempotencyKey`) sonuç çözülür ve reauth'tan sonra gösterilir.
- **Sekme kapanışı / reload:** in-memory anahtar kaybolur. Shell, **otomatik kurtarma iddia etmez**. Ayrı bir kabul edilmiş owner contract güvenli anahtar saklama veya non-disclosing command-status lookup tanımlamadıkça:
  - kullanıcıya **explicit unknown-outcome / manuel mutabakat** durumu gösterilir;
  - **yeni anahtarla kör yeniden gönderim yasaktır**;
  - sonuç, ilgili ekranın normal server-side listesi/durumu üzerinden (reauth sonrası) kullanıcı tarafından teyit edilir.
- Bu, `AT-005` ve task-local senaryo 8 için browser testinde kanıtlanır.

---

## B. Aday implementation eşlemesi (bilgilendirici — normatif DEĞİL)

Aşağıdaki eşleme **yalnızca** PR #118 paket kapısı ve Issue #122 generated-client kapısı kapandıktan sonra ve ancak o kararlar bunları seçerse geçerlidir. K3 sözleşmesi bunları önceden yetkilendirmez.

| Değişmez | Olası eşleme (aday) |
|---|---|
| INV-FENCE | herhangi bir fetch katmanı + generation kapanışı; TanStack Query kullanılırsa `meta.generation` + `onSettled` guard |
| INV-NS | `contextKey` merkezi üretici; TanStack Query kullanılırsa `queryKey[0] = contextKey`, switch'te scoped `removeQueries` |
| INV-PENDING / A.3 | shell store reducer (Zustand veya context/reducer — paket kararı değil); `key={contextKey}` ile ağaç remount |
| INV-OVERLAY | overlay-registry; React Aria veya başka headless — paket kararı değil |
| A.5 replay | ERP-000E anahtar-tabanlı sözleşme; ek endpoint YOK |
| browser kabulü | herhangi bir çok-tarayıcı E2E; Playwright aday (#118), Cypress aday — kapı kapanmadan "zorunlu" değil |

Router loader'ları **backend authorization sağlamaz**; endpoint reauthorization her zaman authoritative'dir. `removeQueries` + `AbortController` **tek başına** stale-response fence değildir — INV-FENCE generation kontrolü gerektirir. Şema doğrulama (ör. Zod) **ikinci bir OpenAPI/domain gerçeği olamaz**; yalnız transport şekli veya UI form niyeti doğrular, server kısıtları ve generated tipler authoritative kalır.

---

## C. Codex'ten gereken sözleşme girdileri

Bu tasarım şu backend gerçeklerine bağlı; Codex `W0` içinde sağlamalı, yoksa `HANDOFF-*`. **Doküman alan adı/endpoint/tag seçmez; ihtiyacı tanımlar.**

1. **Opak server-backed capability** — actor/session/aktif context/contract-revision'a bağlı; shell'e yalnız kaba feature/action availability + değişimi saptanabilir bir generation işareti verir. Exact endpoint/envelope/transport Codex kararı. **Çözülemezse:** shell fail-closed disabled — base shell + Issue #120 company-directory YALNIZCA, tüm capability-gated mount/action kapalı, **uydurma generation yok**. Bu tanımlı davranıştır, `ASSUMPTION` değil.
2. **Stale/invalid capability sinyali** — 401/403 veya explicit; mekanizma Codex kararı; shell fail-closed.
3. **Route-level reauthorization** — her tenant çağrısı server'da yeniden doğrulanır (mevcut, korunur).
4. **Grup-okuma route beyanı** — kabul edilmiş **server-owned route declaration/registry**'den. OpenAPI generated client için **projekte edebilir**; tag/extension **güvenlik kaynağı değildir**. Somut owner beyanı yoksa grup modu shell'de devre dışı.
5. **ERP-000E** — anahtar-tabanlı replay sözleşmesi (mevcut). Payload-hash tabanlı sonuç-sorgulama endpoint'i **talep edilmiyor**; sekme kapanışı senaryosu explicit manuel mutabakatla ele alınıyor.

---

## D. Test planı (`ERP-000K` §18.2 task-local senaryolar)

Package-bağımsız kabul; browser E2E aracı #118/#122 kapandığında seçilir.

- **S1**: A kullanıcısı URL/localStorage/cache üzerinden B kaydının varlığını öğrenemez.
- **S2**: Missing/stale/malformed capability → fail-closed disabled shell; menü görünürlüğü permission üretmez; uydurma generation yok.
- **S6**: Dirty formda switch → GUARD/CONFIRM; `activeContext` kullanıcı kararı öncesi değişmez.
- **S7**: A'nın yavaş response'u (artificial delay) switch sonrası B'ye uygulanmaz — INV-FENCE.
- **S8**: Timeout'lu write → oturum-içi switch → duplicate yok; dönüşte önce reauth, sonra ERP-000E anahtar replay. **Sekme kapanışı → otomatik kurtarma iddiası yok; explicit unknown-outcome durumu; yeni anahtarla resend yok.**
- **S9**: Cache/count/suggestion şirketler ve kullanıcılar arası paylaşılmaz — INV-NS invariant testi + runtime.
- **S10**: Capability generation değişimi → açık route/drawer/favorite yeniden doğrulanır.
- **S-FAIL**: `pendingTarget` reauth **başarısız** → X'in adı/ID'si hiçbir zaman aktif/görünür olmaz; former context fresh reauth ile geri yüklenir veya "aktif şirket yok" güvenli durum.
- Component test: iki fazlı switch reducer; write-registry payload-hash + context binding; `useDirtyScope`; namespace invariant kuralı.

---

## E. Fresh review isteği (CODEX-PLATFORM + ChatGPT re-review)

```text
REVIEWED HEAD: <REV.2 PR head SHA>   BASE: 382d9b5
KATMAN: K3 — doğru | yanlış
SOURCE: ERP-000K §8/§9/§13/§18.2, OTONOM_TESLIM_PLANI §6/§9, Issue #120/#122/#124, AT-005/AT-008
REV1-1 (PR body REV.2 ile senkron): kabul | <sapma>
REV1-2 (güvenlik değişmezi package-bağımsız; Bölüm B non-normatif): sınır doğru | <ihlal>
REV1-3 (ERP-000E sekme-kapanışı: otomatik kurtarma yok, explicit manuel mutabakat): kabul | <bulgu>
REV1-4 (capability key = session/actor generation + context binding; grup modu owner-route'suz devre dışı): kabul | <bulgu>
INV-FENCE / INV-NS / A.3 / A.5: değişmezler §13'ü karşılıyor | <açık madde>
KARAR: HANDOFF-ACCEPTED | BLOCKER
```

Yakınsarsa: Bölüm A `SHELL-PILOT-01` (Issue #124) kabul kriteri olur. Bölüm B, #118/#122 kapandığında revize edilir. Foundation implementation bu head'den değil, review yakınsamasından sonra başlar.
