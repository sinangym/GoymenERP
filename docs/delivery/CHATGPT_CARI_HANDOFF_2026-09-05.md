# ChatGPT Cari Handoff — 2026-09-05

## Amaç

Bu kayıt, bu konuşmada ChatGPT + kullanıcı tarafından netleştirilen Cari kararlarını ve daha önce açılmış paralel Cari geliştirme hattının güncel GitHub gerçeğiyle uzlaştırılmış handoff'unu Codex'e devretmek içindir.

Bu PR **production kodu değiştirmez**. `AGENTS.md` güncel şerit kuralına göre ChatGPT burada iş spec/acceptance/hakemlik sahibi olarak kalır; backend düzeltmeleri Codex şeridindedir.

## Exact base

- Repository: `sinangym/GoymenERP`
- Base: `main@9f5b9bfdef249b9c8e89c6668f66b6aa55c34602`
- Owner: `CHATGPT-CARI`
- Write scope: yalnız bu handoff dosyası

## Kullanıcı tarafından kilitlenen Cari kararları

1. **Tek Party / çok şirket hesabı**
   - Aynı gerçek/tüzel karşı taraf grup seviyesinde tek `Party` olarak tutulur.
   - Her Göymen hukuki şirketi için ayrı `CompanyPartyAccount/CariHesap` oluşur.
   - Fiziksel ve finansal şirket kayıtları birleşmez.
   - Kullanıcı yalnız yetkili olduğu şirket ilişkilerini görür.
   - Birden fazla şirkete yetkili kullanıcı, yalnız yetkili şirketlerinin read-only birleşik görünümünü görebilir.
   - Birleşik görünüm ayrı kayıtların kompozisyonudur; şirketler arası otomatik mahsup/kapama değildir.

2. **Cari türleri**
   - Tüzel kişi/firma ve gerçek kişi/şahıs işletmesi ilk günden desteklenir.

3. **Cari kodu**
   - Grup genelinde tek ve değişmez CariCode kullanılır (`CAR-000001` benzeri).
   - Şirket bazında ikinci CariCode üretilmez.
   - Gerekirse şirket bazlı muhasebe/entegrasyon kodu ayrı metadata alanıdır.

4. **Kritik değişiklik maker-checker**
   - Unvan, VKN/TCKN, MERSİS ve IBAN değişiklikleri onaya gider.
   - Değişikliği yapan kendi talebini onaylayamaz.
   - **Tek farklı yetkili onayı yeterlidir; iki ayrı onay gerekmez.**
   - Adres/telefon/e-posta yetki + audit ile doğrudan değişebilir.
   - Cari pasife alma yetki + gerekçe + audit ister; başlangıçta ayrıca approval kuyruğu zorunlu değildir.

## Önceki paralel Cari hattı — kaynak izleri

Bu konuşmada açılmış/geçmiş Cari PR ve branch'leri tarihsel kaynak olarak görülmelidir; **güncel `main` yerine yeniden uygulanmamalıdır**.

- PR #21: Cari readiness/karar/spec hattı.
- PR #22: Counterparty domain/application contracts hattı.
- PR #23: tenant-safe persistence/security sözleşmesi.
- PR #27: SQL/read API hattı.
- PR #33: atomik create + company-account active-state command hattı; daha sonra main'e girdi.
- PR #36: `parallel/chatgpt/cari-core-1c-company-link`; kapalı ve merge edilmemiş tarihsel branch. İçeriği doğrudan yeniden kullanılmamalı; güncel main ile semantik diff yapılmadan sahiplenilmemeli.

## Güncel repo gerçeğiyle uzlaştırma

- No-op optimistic concurrency için konuşmada daha önce P1 tespit edilmişti.
- Bu konu daha sonra Codex tarafından PR #191 ile deterministik gerçek-SQL acceptance'a bağlandı ve **merge edildi** (`0c66ba7fcf9fbddb799448d0da6fe401972f74df`). Bu nedenle eski "no-op P1 açık" ifadesi artık canlı gerçek değildir.
- Güncel main'de Counterparty command/read/link yüzeyleri, geçmiş PR #36'dan daha ileri durumdadır. Codex eski PR head'ini değil canlı main'i esas almalıdır.

## Codex için exact-head review hedefleri

Aşağıdakiler **açık bug iddiası değil**, güncel main üzerinde yeniden doğrulanması gereken review hedefleridir:

1. `Idempotency-Key` header'ında sıfır/çoklu/virgülle birleştirilmiş değerlerin fail-closed davranışı.
2. Counterparty create/status/link permission'larının intended mevcut roller ve upgrade/reconciliation davranışı.
3. CompanyId izolasyonu: list/detail/count/search/link-candidate/write/replay/error yolları.
4. Durable idempotency: aynı key/same payload replay; farklı actor/payload conflict; original correlation korunumu.
5. Audit + business + CommandExecution atomikliği ve rollback.
6. `CompanyPartyAccount` optimistic concurrency ve no-op yarışının PR #191 kanıtıyla güncel kodda hâlâ korunması.
7. Counterparty route/capability guard'ların güncel endpoint envanteriyle exact eşleşmesi.
8. Counterparty migration SQL artifact üretiminin güncel CI sözleşmesinde sahipliğinin ve kapsamının doğrulanması.

## Handoff kuralı

Codex bu dosyayı okuyunca:

- önce `git fetch --all --prune` ve canlı `main`/PR/CI gerçeğini yeniden hesaplamalı,
- bu handoff içindeki tarihsel PR/SHA'ları canlı gerçek yerine kullanmamalı,
- backend düzeltmesi gerekiyorsa `codex/...` branch/worktree açmalı,
- ChatGPT'nin `parallel/chatgpt/...` tarihsel branch'lerine yazmamalı,
- exact-head review sonucunu ilgili PR'a `HANDOFF-ACCEPTED` veya `REQUEST_CHANGES` olarak yazmalı,
- açık P0/P1 yoksa sıradaki güvenli işe geçmelidir.

## HANDOFF-V1

OWNER: `CHATGPT-CARI`

BASE: `main@9f5b9bfdef249b9c8e89c6668f66b6aa55c34602`

SCOPE: Cari iş kararları + acceptance/review hedefleri + tarihsel paralel hattın canlı GitHub ile uzlaştırılması.

PRODUCTION CODE CHANGED: `NO`

OPEN BUSINESS DECISIONS INTRODUCED: `NO`

NEXT SAFE ACTION: Codex exact-current-main review; gerekli backend düzeltmelerini kendi şeridinde ayrı PR olarak üretmek.
