# CARI-UI-001 — Cari Kartlar ve Cari 360 İlk Uçtan Uca Dilim

- Sürüm: `1.1`
- Tarih: `2026-08-30`
- İş sahibi: `CHATGPT-BUSINESS/CARI`
- Ortak kabuk ve server-capability sahibi: `CODEX-PLATFORM`
- İncelenen `main`: `83197c50f515db959638fadd7cc11ad6eb7a4fc8`
- Kanonik ilk ekranlar: `C-01`, `C-03`, `C-02` create alt kümesi
- Yardımcı işlem workspace’i: `CARI-LINK-WORKSPACE-001`
- Takip: Issue `#78`
- Contract durumu: **REVIEW_PENDING**
- Implementation: **BLOCKED**
- Production activation: **NO-GO**
- Event delivery: `synchronous_transaction`

## 1. Sonuç

Bu sözleşme, Göymen ERP’de ilk görünür Cari dikey dilimini tanımlar; production kodunu başlatmaz. İlk dilim:

1. `C-01 Cari Kartlar`,
2. `C-02 Yeni Cari` ekranının mevcut backend’in desteklediği create alt kümesi,
3. `C-03 Cari 360` çekirdek özeti,
4. mevcut aktif Party’yi başka şirkete bağlayan `CARI-LINK-WORKSPACE-001`

ile sınırlıdır.

`C-04…C-12` bu sözleşmede C-03 sekmelerine dönüştürülmez. Her biri ayrı kanonik ekran veya workspace olarak korunur. Bakiye, açık kalem, risk, limit, adres, kişi, banka, vergi, sicil, belge, sipariş, stok, tahsis ve takip görevi için backend sahibi olmayan veri mock, sıfır veya sahte KPI olarak gösterilmez.

## 2. Kaynak ve otorite

Bağlayıcı kaynaklar:

- `docs/ui/CANONICAL_SCREEN_STATUS.csv`
- `docs/ui/SCREEN_CATALOG.csv`
- `docs/ui/screens/cari/CARI-ROUTE-01.md`
- `docs/ui/screens/cari/CARI-ROUTE-03.md`
- `docs/ui/screens/cari/CARI-ROUTE-04.md`
- `docs/ui/screens/cari/CARI-ROUTE-09.md`
- `docs/ui/UI_DESIGN_SYSTEM.md`
- `docs/features/ERP-000K_SHELL_CONTRACT_v1.md`
- mevcut Counterparty API, `PartyCoreView` ve `CompanyPartyAccountView`
- PR `#77`

Görsel Cari referansı `DESIGNED_NOT_LOCKED / PROTOTYPE_REFERENCE` durumundadır. Bilgi mimarisi ve çalışma alanı yönü kullanılabilir; mock veri, istemci tarafı yetki, kontrolsüz `innerHTML`, sahte AI/upload ve prototip route’ları production gerçeği değildir.

`ERP-000K-SHELL-CONTRACT` fiziksel olarak `main`dedir; ancak contract `OPEN`, implementation `BLOCKED`, production `NO-GO` durumundadır. Bu nedenle fiziksel dosyanın bulunması shell runtime, feature mount, route registry, server-backed capability veya browser harness’in hazır olduğu anlamına gelmez.

## 3. Sahiplik ve tek-writer sınırı

### CODEX-PLATFORM

- root `App` ve ortak shell,
- router ve route registry altyapısı,
- server-backed capability provider ve ortak istemcisi,
- shared query/cache ve CompanyContext generation fence,
- global navigasyon, tokenlar, global CSS ve ortak bileşenler,
- Playwright, visual ve accessibility harness,
- generic feature-mount sözleşmesi.

### CHATGPT-BUSINESS/CARI

- accepted mount API üzerinden Cari feature route tanımları,
- Cari API istemcisi ve güvenli response mapping,
- C-01, C-02 create alt kümesi, C-03 ve link workspace,
- Cari’ye özel filtre, form, state, error ve fixture’lar,
- `CARI-READ-PAGE-001`,
- `CARI-LINK-CANDIDATE-001`,
- `CARI-ERROR-CODE-001`,
- Cari component, integration, gerçek-SQL ve browser kabulü.

Shell handoff tamamlanana kadar ChatGPT-Cari şu alanlarda read-only’dir:

- `src/frontend/goymen-erp-web/src/App.tsx`
- global navigasyon
- global CSS/tokenlar
- shared router
- shared query client
- common capability client

## 4. Ekran ve route kapsamı

| Kimlik | Ad | Route | Tür | Durum |
|---|---|---|---|---|
| `C-01` | Cari Kartlar | `/cari` | Liste + hızlı detay | Blocked |
| `C-02:create-subset` | Yeni Cari | `/cari/yeni` | Rehberli create formu | Blocked |
| `C-03` | Cari 360 çekirdek özet | `/cari/:partyId` | 360 çekirdek görünüm | Blocked |
| `CARI-LINK-WORKSPACE-001` | Mevcut Cariyi Başka Şirkete Bağla | `/cari/sirkete-bagla` | Rehberli işlem workspace’i | Blocked |

`CARI-LINK-WORKSPACE-001` yeni kanonik ekran kimliği değildir; C-02’nin company-account bağlama işlemi için task-local workspace kimliğidir.

## 5. C-04…C-12 ayrı kanonik ekranları

Aşağıdaki ekranlar C-03 içinde “gelecek sekme” olarak eritilmez:

| Ekran | Ad | Business owner | Runtime owner | İlişki türü | Availability gate |
|---|---|---|---|---|---|
| `C-04` | Cari Hesap Takibi | `CHATGPT-BUSINESS/CARI` | Açık | Ayrı kanonik route/workspace | Owner contract + backend projection + capability |
| `C-05` | Açık Kalemler | `CHATGPT-BUSINESS/CARI` | Açık | Ayrı kanonik route/workspace | Finance/open-item owner ve API |
| `C-06` | Adres / Kişi / İletişim | `CHATGPT-BUSINESS/CARI` | Açık | Ayrı kanonik route/workspace | Veri modeli, API ve capability |
| `C-07` | Banka / Vergi / Sicil | `CHATGPT-BUSINESS/CARI` | Açık | Ayrı kanonik route/workspace | Hassas veri permission/redaction ve API |
| `C-08` | Risk / Limit / Vade | `CHATGPT-BUSINESS/CARI` | Açık | Ayrı kanonik route/workspace | Risk/limit owner, kurallar ve API |
| `C-09` | Mutabakat | `CHATGPT-BUSINESS/CARI` | Açık | Ayrı kanonik route/workspace | Mutabakat yaşam döngüsü ve API |
| `C-10` | Belgeler / Sözleşmeler | `CHATGPT-BUSINESS/CARI` | Açık | Ayrı kanonik route/workspace | Belge Kasası capability ve API |
| `C-11` | Mükerrer ve Birleştirme | `CHATGPT-BUSINESS/CARI` | Açık | Ayrı kanonik route/workspace | Karşılaştırma/onay/merge API |
| `C-12` | İlişki / Takip Görevleri | `CHATGPT-BUSINESS/CARI` | Açık | Ayrı kanonik workspace | Task/CRM veri sahibi ve API |

Business owner Cari iş kuralı sahibini gösterir. “Runtime owner: Açık”, ilgili entegrasyon/uygulama sahibinin kaynaklarda bağlayıcı biçimde atanmadığını belirtir; bu boşluk sessizce doldurulmaz.

C-03 içinde ileride yalnız owner projection ve server capability kabul edilirse:

- `S-02` ve `P-03` için sipariş özeti/bağlantısı,
- `ST-01`, `ST-08`, `AL-03` için stok/tahsis özeti/bağlantısı

embedded related summary olarak gösterilebilir. Bunlar C-04…C-12’nin yerine geçmez.

## 6. Effective route/action capability matrisi

| Capability | UI / API | Zorunlu permission | Scope | Not |
|---|---|---|---|---|
| `cari.parties.list` | `/cari` → `GET /api/v1/counterparties` | `counterparty.parties.read` | Şirket veya explicit authorized multi-company read | Mevcut enforcement service-level/per-company |
| `cari.parties.detail` | `/cari/:partyId` → `GET /api/v1/counterparties/{partyId}` | `counterparty.parties.read` | Şirket veya explicit authorized multi-company read | Non-disclosing 404 |
| `cari.parties.create` | `/cari/yeni` → `POST /api/v1/counterparties` | `counterparty.parties.create` | Company-wide, tek şirket | Idempotency zorunlu |
| `cari.accounts.status.update` | C-03 → `PUT .../company-account/active-state` | `counterparty.accounts.status.update` | Company-wide, tek şirket | Idempotency + version + gerekçe |
| `cari.accounts.link` | Link workspace → `POST .../{partyId}/company-account` | `counterparty.accounts.create` **ve** `counterparty.parties.read` | Her ikisi hedef şirkette company-wide | `CARI-PERM-001=A`; PR #77 kapısı |

UI capability’yi rol adından, JWT decode işleminden, localStorage’dan, route isminden veya `/api/v1/identity/me` cevabından türetmez. Bugünkü `/identity/me` permission/scope/capability kaynağı değildir. Unknown, malformed veya stale capability fail-closed olur. Control görünürlüğü authorization değildir; backend endpoint her çağrıda yeniden yetkilendirir.

## 7. Mevcut read model ve redaction sınırı

Çekirdek UI gerçek kabul edebileceği Party alanlarını şunlarla sınırlar:

- `id`
- `code`
- `kind`
- `legalName`
- `tradeName`
- `isActive`
- `version`

Şirket hesabı alanları:

- `id`
- `companyId`
- `roles`
- `isActive`
- `version`

`externalIntegrationCode` mevcut transport modelinde bulunabilir; fakat dedicated read permission ve redaction sahibi kabul edilmediği için çekirdek UI mapping’ine alınmaz, component prop’una taşınmaz ve ekranda gösterilmez.

Global Party aktifliği ile şirket hesabı aktifliği ayrı durumdur. Cari kodu salt okunurdur.

## 8. CARI-READ-PAGE-001 — uygulanabilir liste API sözleşmesi

Production tamamlanmadan `GET /api/v1/counterparties` server-side arama, filtre, deterministik sıralama ve cursor sayfalama kazanmalıdır.

### 8.1 Context modları

**Tek şirket**

- Exact `X-Company-Id` gönderilir.
- Server şirket erişimini ve `counterparty.parties.read` iznini doğrular.
- Response yalnız seçili şirketteki yetkili company-account görünümünü taşır.

**Yetkili çok-şirket okuma**

- Header yalnız bu route contract’ı açıkça desteklediği için bilinçli olarak gönderilmez.
- Server `AllowedCompanyIds` ve her şirket için `counterparty.parties.read` iznini kendisi çözer.
- Browser şirket şirket fan-out yapmaz ve sonuçları istemci tarafında birleştirmez.
- Response içindeki şirket hesapları yalnız yetkili kapsamdan gelir.

### 8.2 Query parametreleri

| Parametre | Sözleşme |
|---|---|
| `q` | Opsiyonel; en çok 200 karakter; dış Unicode whitespace trim + NFC; code/legalName/tradeName içinde contains |
| `kind` | `LegalEntity` veya `NaturalPerson` |
| `role` | Repeatable: `Customer`, `Supplier`, `Tolling`, `Service` |
| `roleMatch` | `any` veya `all`; varsayılan `any` |
| `partyState` | `active`, `inactive`, `all`; varsayılan `active` |
| `accountState` | `active`, `inactive`, `all`; varsayılan `all` |
| `sort` | `code`, `legalName`, `updatedAt`; varsayılan `code` |
| `direction` | `asc`, `desc`; varsayılan `asc` |
| `pageSize` | 1–200; varsayılan 50 |
| `cursor` | Opsiyonel opaque cursor |
| `includeCount` | Boolean; varsayılan `false` |

Rol eşleştirmesinde şirketler arası rol birleşimi yapılmaz. Tek bir context-eligible görünür şirket hesabı, `any` için rollerden en az birini; `all` için bütün istenen rolleri taşımalıdır.

`accountState` yalnız context-eligible görünür hesaplarda değerlendirilir.

### 8.3 Türkçe arama semantiği

- Arama case-insensitive, accent-sensitive’dir.
- Türkçe eşleşme çiftleri: `i = İ`, `ı = I`.
- `i != I`, `İ != ı`.
- Accent folding yapılmaz: `s != ş`, `g != ğ`, `o != ö`, `u != ü`, `c != ç`.
- Trim sonrası boş `q`, parametre verilmemiş gibi davranır.
- Fuzzy, transliteration veya ses benzerliği bu dilimde yoktur.

Bu, gözlenebilir API davranışıdır; belirli SQL collation seçimi bu contract’ta zorlanmaz. Gerçek SQL kabulü aynı sonuçları kanıtlamalıdır.

### 8.4 Sıralama

- `code`: `(Code, PartyId)`
- `legalName`: `(LegalName Turkish CI/AS, Code, PartyId)`
- `updatedAt`: `(UpdatedAtUtc, PartyId)`

Her sıralamada benzersiz tie-breaker zorunludur; aynı sayfa/cursor kapsamındaki kayıtlar atlanamaz veya tekrar edemez.

### 8.5 Cursor

Cursor:

- opaque ve server-authenticated,
- contract sürümüne,
- normalize query/filtrelere,
- sort/direction’a,
- context moduna,
- tek şirket modunda seçili şirkete,
- yetkili şirket seti veya policy revision’a,
- authenticated user/session’a

bağlıdır.

Malformed, değiştirilmiş, süresi geçmiş veya farklı scope/filter ile kullanılan cursor:

```text
400
code = invalid_cursor
partial data = yok
```

sonucu üretir.

### 8.6 Response ve count

```json
{
  "items": [],
  "nextCursor": null,
  "hasMore": false,
  "totalCount": null,
  "countScope": null
}
```

`items`, yalnız context-eligible yetkili company account’ları içeren `PartyCoreView[]` olur.

`includeCount=true` ise `totalCount`, bütün authorization/context/filtrelerden sonra kalan **distinct Party** sayısıdır. Yetkisiz şirket veya filtresiz grup toplamı değildir. `countScope` değeri `filtered-authorized-visible-parties` olur. `includeCount=false` iken iki alan `null` kalır.

Sınırsız client-side listeleme production tamamlanmış sayılmaz.

## 9. CARI-LINK-CANDIDATE-001 — uygulanabilir şirkete bağlama akışı

Mevcut API’de hedef şirkette hesabı olmayan Party, target-scoped C-03 detail ile açılamaz. Bu nedenle “Şirkete Bağla” işlemi target-local detail’e bağımlı tasarlanamaz.

### 9.1 Candidate endpoint

```http
GET /api/v1/counterparties/company-account-link-candidates
X-Company-Id: <exact target company>
```

Hedef şirket body, query veya route içinde bulunmaz.

Hedef şirkette zorunlu:

- company-wide `counterparty.accounts.create`
- company-wide `counterparty.parties.read`

Candidate:

- global `Party.IsActive=true` olmalıdır;
- actor’ın izinli ve `counterparty.parties.read` bulunan en az bir mevcut şirket ilişkisinden görünmelidir;
- hedef şirkete zaten bağlı olmamalıdır;
- yalnız actor’a görünür source account özetlerini taşımalıdır.

Query:

- `q`: `CARI-READ-PAGE-001` ile aynı normalizasyon ve Türkçe eşleşme,
- `pageSize`: 1–100, varsayılan 25,
- `cursor`: aynı authenticated/scope-bound cursor kuralları.

Item alanları:

- `id`, `code`, `kind`, `legalName`, `tradeName`, `isActive`
- `visibleSourceAccounts`: `id`, `companyId`, `roles`, `isActive`, `version`

### 9.2 UI akışı

1. Kullanıcı yetkili hedef şirketi seçer.
2. Shell exact target CompanyContext’i commit eder.
3. Eski context’in read/cache/selection/drawer’ları generation fence ile temizlenir.
4. `/cari/sirkete-bagla` açılır.
5. Candidate araması target header altında server’dan yapılır.
6. Önceki context’ten taşınan PartyId yalnız preselection hint olabilir; Party fresh candidate response içinde yeniden bulunmadan seçili kabul edilmez.
7. POST link command aynı exact target header ve yeni logical-attempt idempotency key ile gönderilir.
8. `201` sonrası target-scoped C-03 açılır.
9. `403`, `404`, `already_linked`, `idempotency_conflict` stable error code ile ayrılır.

Browser fan-out veya client-side candidate union yasaktır.

## 10. Global pasif Party kapısı

PR #77 kabul edilmeden link UI açılmaz.

Bağlayıcı davranış:

- `Party.IsActive=false` kayıt candidate listesine girmez.
- Doğrudan tahmin edilen PartyId ile POST yapılırsa non-disclosing terminal `404` döner.
- Hedef company account ve business audit oluşmaz.
- Tek terminal `NotFound` CommandExecution kalıcılaşır.
- Same-key replay aynı `404`, `Idempotency-Replayed=true` ve ilk correlation kimliğini döndürür.
- Kaynak `CompanyPartyAccount.IsActive` tek başına çapraz-şirket onboarding engeli değildir; şirket hesabı yaşam döngüleri bağımsızdır.

## 11. Idempotency ve replay wire contract

Aşağıdaki write’ların tamamı `Idempotency-Key` ister:

- yeni Party create,
- şirket hesabı active-state update,
- mevcut Party company-link.

Header:

- tek field-value,
- `Trim`,
- 1–128 karakter,
- missing, empty, multiple, comma-joined veya oversized ise `400 command_metadata_invalid`,
- invalid request business mutation, audit veya CommandExecution bırakmaz.

Logical attempt:

- kullanıcı tarafından onaylanan her logical submit için tek key;
- timeout/unknown result retry aynı payload + aynı key;
- payload değişirse veya kullanıcı yeni bir işlem başlatırsa yeni key;
- unknown write sonucu otomatik yeni key ile değiştirilmez;
- sonuç bilinmiyorken company switch write fence’i aşamaz.

Replay header’ları:

```text
Idempotency-Replayed: true
Idempotency-Original-Correlation-Id: <ilk correlation>
```

Replay duplicate toast, navigation veya business effect üretmez.

## 12. CARI-ERROR-CODE-001 — stable ProblemDetails discriminator

Mevcut localized `title/detail` metinleri UI branching için yeterli değildir. UI, 409 alt türünü veya başka domain hatasını metinden tahmin etmez.

Production UI başlamadan `application/problem+json` cevabında stable `code` extension’ı kabul edilmelidir.

Zorunlu alanlar:

- `type`
- `title`
- `status`
- `detail`
- `code`

Opsiyonel:

- `correlationId`
- `conflict`

Stable code matrisi:

| Code | HTTP |
|---|---|
| `validation_failed` | 400 |
| `command_metadata_invalid` | 400 |
| `invalid_cursor` | 400 |
| `authentication_required` | 401 |
| `forbidden` | 403 |
| `not_found` | 404 |
| `already_linked` | 409 |
| `version_conflict` | 409 |
| `idempotency_conflict` | 409 |
| `service_unavailable` | 503 |

ProblemDetails plain text olarak render edilir; `innerHTML` kullanılmaz.

## 13. UI state ve retry matrisi

| State | Davranış |
|---|---|
| Initial loading | Skeleton; önceki şirket verisi yok |
| No data | Yeni Cari aksiyonu yalnız capability varsa |
| Filter empty | Filtreleri temizle |
| Partial/stale read | Freshness açık; write’e authority olmaz |
| 400 | Form korunur; otomatik write retry yok |
| 401 | Session-expiry; yeni key/write yok |
| 403 | Forbidden; kayıt ayrıntısı sızmaz |
| 404 | Non-disclosing |
| 409 already-linked | Target detail yeniden yüklenir |
| 409 version-conflict | Güncel kayıt yüklenir; kullanıcı yeniden karar verir |
| 409 idempotency-conflict | Otomatik yeni key yok |
| Replay | Aynı sonuç; duplicate effect/toast yok |
| Timeout / unknown write | Yalnız aynı payload/key ile kontrollü retry |
| 500 | Read retry olabilir; write unknown-result kuralı |
| 502/503/504 | Read retry olabilir; write unknown-result kuralı |
| Malformed response | Fail-closed; yeni write yok |
| Offline | Kullanıcı kontrollü retry öncesi write yok |
| Company switch stale response | Eski generation response atılır |

Empty, 403, 404, service-error ve malformed response aynı state değildir.

## 14. C-01 Cari Kartlar

Yerleşim:

1. sayfa başlığı ve capability-gated `Yeni Cari`,
2. şirket/context göstergesi,
3. server-side arama ve filtre,
4. yoğun liste/tablo,
5. masaüstünde hızlı detail drawer, mobilde tam sayfa detail.

Varsayılan kolonlar:

- Cari Kodu
- Hukuki Ad
- Ticari Ad
- Tür
- Görünür roller
- Görünür şirket hesabı sayısı
- Party durumu
- Seçili şirket hesabı durumu

Para, bakiye, açık kalem, risk ve limit kolonları owner projection olmadan eklenmez.

## 15. C-02 create alt kümesi

Mevcut backend’in desteklediği alanlar:

- Party türü
- Hukuki ad
- Ticari ad, opsiyonel
- İlk ticari roller
- Aktif şirket, shell’den salt okunur

VKN/TCKN/MERSİS/IBAN/banka/adres/kişi, kritik Party update ve duplicate merge yoktur.

Başarılı create sonrası aynı PartyId ile C-03 açılır. İstemci aynı logical attempt retry’ında key değiştirmez.

## 16. C-03 çekirdek 360

Gösterilir:

- Cari kodu
- Hukuki ad
- Ticari ad
- Party türü
- global Party aktifliği
- kullanıcının görebildiği şirket hesapları
- her hesapta company kimliği, roller, account aktifliği ve version
- capability-gated active-state işlemi
- capability-gated şirkete bağlama workspace geçişi

Şirket adı/kodu Organization projection’dan gelir; frontend sabit ID→ad tablosu üretmez.

C-03 yalnız mevcut core read modelini gösterir. C-04…C-12 ve ilgili satış/stok özetleri availability gate kapanmadan görünür boş sekme, sahte count veya mock kart oluşturmaz.

## 17. Mock ve veri sızıntısı yasağı

Aşağıdakiler owner backend truth olmadan render edilmez:

- bakiye
- açık kalem
- risk/limit/vade
- adres/kişi/iletişim
- banka/vergi/sicil
- belge/sözleşme
- satış/alış siparişleri
- stok/tahsis
- takip görevi/CRM
- AI işlemi
- grup toplamı

Party veya company-account bilgisi URL, title, breadcrumb, recent, favorite, toast, client log veya error timing yoluyla yetkisiz scope’a sızdırılmaz.

## 18. Responsive ve accessibility acceptance

- Klavye ile liste → detay → create → link → status-change görevleri tamamlanır.
- Görünür focus ve skip-link bulunur.
- Table sort semantik header ve `aria-sort` ile duyurulur.
- Validation summary focus alır ve invalid alanlara bağlanır.
- Drawer/modal focus trap, Escape, inert background ve invoker’a focus-return sağlar.
- Loading/error/success/replay live region’ları ekran okuyucuyu spam etmez.
- `%200 zoom` ve `%400 reflow` görev tamamlamayı bozmaz.
- Reduced-motion state anlamını korur.
- Renk tek durum işareti değildir.
- Mobil kart/list alternatifi aynı eylem ve label’ları taşır.
- Uzun Türkçe metin, dar viewport ve error detail ile overflow/focus kesilmesi test edilir.

## 19. Başlama kapıları

Production Cari UI implementation aşağıdakilerin tamamı kapanmadan başlamaz:

1. PR #77 merge + green new-main CI.
2. `ERP-000K-SHELL-CONTRACT` runtime alt kapıları:
   - feature mount,
   - route registry,
   - shared query/cache,
   - CompanyContext generation fence,
   - browser harness.
3. Server-backed capability provider.
4. `CARI-READ-PAGE-001` implementation + gerçek SQL.
5. `CARI-LINK-CANDIDATE-001` implementation + gerçek SQL.
6. `CARI-ERROR-CODE-001` stable machine error contract.
7. Frontend single-writer ataması.

## 20. Completion gates

- C-01 single-company ve authorized multi-company read
- C-02 create alt kümesi
- C-03 çekirdek 360
- link workspace
- permission ve CompanyContext fail-closed
- global pasif Party’nin sunulmaması ve bağlanamaması
- component ve API contract testleri
- gerçek SQL acceptance
- Playwright E2E
- visual regression
- keyboard, screen-reader, zoom, reflow, reduced-motion
- responsive task completion
- service-error, malformed-response ve unknown-write-result
- full CI
- container build
- UI golden
- CODEX exact-head review
- açık insan kabulü

## 21. Done statement

Bu dilim ancak gerçek C-01, C-02 create, C-03 ve company-link kullanıcı akışları; server capability, paged read, candidate search, stable error code, gerçek SQL ve browser acceptance ile birlikte kabul edildiğinde tamamlanır.

C-04…C-12 ile finansal, risk, iletişim, belge ve CRM alanları bu dilim tamamlandığında dahi tamamlanmış sayılmaz.
