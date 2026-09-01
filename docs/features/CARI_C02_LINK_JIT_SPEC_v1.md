# CARI-C02-LINK-JIT-001 — Yeni Cari ve şirkete bağlama çalışma alanı

- Süreç: `docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md`
- Belge türü: **C-01/C-03 sonrasında uygulanacak C-02 + CARI-LINK dilimi için JIT iş/ekran sözleşmesi**
- İş ve kabul sahibi: `CHATGPT-BUSINESS`
- Backend/API yazarı: `CODEX`
- Frontend/browser yazarı: `CLAUDE-FRONTEND`
- İncelenen taban: `main@4cd05d8983786f2eb1a512ae7d3da7e303ec6ba8`
- Üst kapsam: Issues `#78`, `#95`
- Mevcut backend: `POST /api/v1/counterparties`, `POST /api/v1/counterparties/{partyId}/company-account`
- Eksik backend: `GET /api/v1/counterparties/company-account-link-candidates` (`#95`)
- Production activation: **NO-GO**

Bu belge yeni görev dağılımı, readiness paketi veya governance değildir. Mevcut backend davranışını, son kullanıcı kararlarını ve accepted Cari sözleşmesini çalışan C-02/create ile CARI-LINK ekranlarına dönüştürmek için doğrudan implementation girdisidir.

## 1. Bağlayıcı kaynaklar ve değişmezler

Kaynak sırası:

1. Son kullanıcı kararları ve `AGENTS.md`.
2. Issue `#43`, authoritative correction `#issuecomment-5461694092`:
   - `CARI-PERM-001=A`;
   - `counterparty.accounts.create` yalnız kanonik `SYSTEM_ADMINISTRATOR` rolüne verilir;
   - diğer roller default-deny;
   - yeni `COUNTERPARTY_ACCOUNT_OPERATOR` rolü yoktur;
   - şirketler arası Cari bağlantısında kullanıcı tarafından girilen gerekçe zorunlu değildir.
3. `KARAR_SICILI.md`: `DEC-CAR-001`, `DEC-CAR-002`, `DEC-CAR-003`.
4. Master v3 §7.5, §12 ve §27.5.
5. `docs/features/CARI_UI_VERTICAL_SLICE_v1.md`.
6. Issue `#93` stable Cari ProblemDetails ve Issue `#95` candidate sözleşmesi.
7. Canlı endpoint/domain/persistence davranışı ve kanonik OpenAPI.
8. `docs/ui/screens/cari/CARI-ROUTE-04.md` yalnız `DESIGNED_NOT_LOCKED / PROTOTYPE_REFERENCE` girdisidir; HTML üretim gerçeği değildir.

Değişmezler:

- Bir hukuki karşı taraf tek `Party` kaydıdır; müşteri, tedarikçi, fason ve hizmet rolleri için kopya Party oluşturulmaz.
- Bir Göymen şirketiyle ilişki `CompanyPartyAccount` kaydıdır.
- Yeni Cari oluşturma, **bir Party + aktif CompanyContext şirketinde bir CompanyPartyAccount** yaratır.
- Mevcut Party’yi başka şirkete bağlama yeni Party yaratmaz; yalnız hedef şirket için yeni CompanyPartyAccount yaratır.
- Party ve şirket hesabı varsayılan olarak aktif oluşur; sonraki pasife alma ayrı optimistic-concurrency command’idir.
- Cari kodu server tarafından sıralı `CAR-000001` biçiminde üretilir. Browser sonraki kodu tahmin etmez veya göndermez.
- UI görünürlüğü authorization değildir. Backend her command’de CompanyContext ve permission’ı yeniden doğrular.
- Kritik write’lar tek logical attempt için tek `Idempotency-Key` kullanır; aynı olay ikinci business kaydı üretmez.
- C-02A’nın mevcut command sözleşmesinde VKN/TCKN, adres, kişi, banka, vergi/sicil veya belge alanı yoktur. Bu alanlar uydurulmaz ve `AT-009` mükerrer VKN/kimlik kabulü bu alt dilimle tamamlandı sayılmaz.

## 2. Dikey dilim sonucu

### 2.1 C-02A — Yeni Cari oluşturma

Yetkili kullanıcı aktif tek şirket bağlamında:

1. taraf türünü seçer;
2. hukuki adı ve opsiyonel ticari adı girer;
3. bir veya daha fazla ticari rol seçer;
4. aktif şirketi salt okunur görür;
5. formu tek logical submit olarak gönderir;
6. başarıda server’ın ürettiği Cari kodu ve yalnız hedef şirket hesabıyla C-03 çekirdek özetine gider.

Bir submit:

```text
1 Party
+ 1 CompanyPartyAccount
+ 1 counterparty.party.created.v1 AuditEvent
+ 1 completed CommandExecution
```

üretir. Validation, authorization, malformed metadata veya transport hatası kısmi Party/account/audit bırakmaz.

### 2.2 CARI-LINK-WORKSPACE-001 — Mevcut Party’yi hedef şirkete bağlama

`SYSTEM_ADMINISTRATOR`, shell tarafından doğrulanmış **hedef şirket** bağlamında:

1. hedef şirkete henüz bağlı olmayan, başka yetkili şirket ilişkisinden görülebilen global aktif Party’leri server-paged candidate aramasıyla bulur;
2. source hesap özetini şirket etiketi, roller ve aktiflik ile görür;
3. hedef şirkette oluşturulacak rolleri seçer;
4. aynı hedef CompanyContext ve tek logical `Idempotency-Key` ile link command’ini gönderir;
5. başarıda hedef şirket kapsamlı C-03’e gider.

Bu işlem:

```text
Party sayısı: değişmez
Yeni hedef CompanyPartyAccount: 1
Başarılı business AuditEvent: 1
İlk terminal CommandExecution: 1
```

üretir. Kaynak hesabı değiştirmez veya yeniden aktifleştirmez.

## 3. C-02A HTTP sözleşmesi

```http
POST /api/v1/counterparties
X-Company-Id: <shell tarafından doğrulanmış aktif şirket>
Idempotency-Key: <tek logical attempt anahtarı>
Content-Type: application/json
```

Request:

```json
{
  "kind": 1,
  "legalName": "Örnek Metal Sanayi ve Ticaret A.Ş.",
  "tradeName": "Örnek Metal",
  "roles": 3
}
```

Wire enum/flag anlamı:

| Alan | Teknik değer | Kullanıcı etiketi |
|---|---:|---|
| `kind` | `1` | Tüzel Kişi |
| `kind` | `2` | Gerçek Kişi |
| `roles` bit `1` | Customer | Müşteri |
| `roles` bit `2` | Supplier | Tedarikçi |
| `roles` bit `4` | Tolling | Fason |
| `roles` bit `8` | Service | Hizmet |

`roles=3`, aynı şirket hesabında Müşteri + Tedarikçi anlamına gelir. İki account veya iki Party oluşturmaz.

### 3.1 Alan doğrulaması

| Alan | Kural |
|---|---|
| `kind` | zorunlu; yalnız tanımlı PartyKind |
| `legalName` | trim sonrası 1–250 karakter |
| `tradeName` | opsiyonel; blank → `null`; trim sonrası en çok 200 karakter |
| `roles` | zorunlu; en az bir bilinen bit; yalnız `1|2|4|8` |
| aktif şirket | form/body alanı değildir; shell CompanyContext ve `X-Company-Id` |
| Cari kodu | request alanı değildir; server üretir |
| kullanıcı gerekçesi | create command alanı değildir |
| VKN/TCKN | mevcut C-02A command alanı değildir |

Frontend aynı kurallarla erken geri bildirim verir; backend doğrulaması asıl otoritedir.

### 3.2 Yetki

- Exact permission: `counterparty.parties.create`.
- Scope: aktif şirkette company-wide.
- Şirket header’ı body/query/path ile değiştirilemez.
- Capability yalnız route/action görünürlüğüdür; backend permission kontrolünün yerine geçmez.

### 3.3 Başarı cevabı

```text
HTTP 201
PartyCoreView
- id
- code                       // server-generated
- kind
- legalName
- tradeName
- isActive = true
- version
- companyAccounts[]
  - exactly active CompanyContext account
  - roles
  - isActive = true
  - version
```

`externalIntegrationCode` mevcut genel DTO’da bulunabilse de C-02A tarafından girilmez ve frontend görünümüne/log/telemetry/persistent storage’a taşınmaz.

## 4. C-02A form ve UX sözleşmesi

### 4.1 Görünür alanlar

- Başlık: `Yeni Cari`.
- Aktif şirket: shell Organization directory’den salt okunur şirket kodu + hukuki ad; ham UUID yok.
- Taraf türü: Tüzel Kişi / Gerçek Kişi.
- Hukuki ad.
- Ticari ad — opsiyonel.
- Roller: Müşteri / Tedarikçi / Fason / Hizmet; en az bir seçim.
- Ana aksiyon: `Cari oluştur`.
- Güvenli dönüş: `Cari Kartlar`.

Gösterilmeyecek/girilmeyecek:

- Cari kodu için manuel alan;
- VKN/TCKN, banka, adres, kişi, vergi/sicil, risk, limit, bakiye;
- owner endpoint olmadan AI önerisi;
- kullanıcı tarafından link/create gerekçesi;
- `externalIntegrationCode`.

### 4.2 Dirty form ve şirket değişimi

İlk kullanıcı değişikliğiyle form shell dirty-scope’a kaydolur. Şirket değiştirme isteği:

```text
discard onayı verilmeden
→ form başka şirkete taşınmaz
→ payload/header/idempotency key değiştirilmez
```

Kullanıcı `Kal` derse aktif şirket ve form korunur. `Değişiklikleri bırak` derse form/idempotency attempt temizlenir, yeni şirket reauthorization tamamlandıktan sonra boş form açılır.

### 4.3 Logical attempt, anahtar üretimi ve unknown outcome

`Idempotency-Key` üreticisi frontend form controller’ıdır; backend anahtarı doğrular, request fingerprint’iyle bağlar ve dayanıklı sonucu saklar.

- Route/form mount edildiğinde, kullanıcı alan düzenlediğinde veya client-side validation geçmeyen bir tıklamada key üretilmez.
- İlk **client-valid ve kullanıcı tarafından onaylanmış** submit, o anda immutable bir submitted snapshot oluşturur: actor/session binding, aktif CompanyContext kimliği/generation’ı, operation kimliği, link akışında PartyId ve normalize payload.
- Bu snapshot için kriptografik olarak rastgele **tam bir key** üretilir ve yalnız bellekte tutulur. Bu, “her submit tıklamasında yeni key” değildir.
- Pending request, çift tıklama, timeout, bağlantı kopması veya sonucu bilinmeyen retry aynı snapshot + aynı actor/company + **aynı key** kullanır. Retry düğmesi yeni key üretmez.
- Key bütün React component ömrüne veya değişebilir “form oturumuna” kör biçimde bağlı değildir. Kullanıcı payload’ı, PartyId’yi, actor/session’ı ya da CompanyContext’i değiştirirse önceki snapshot artık farklı bir business attempt’tir.
- Kesin `validation_failed` sonucu sonrası kullanıcı alanı değiştirip yeniden gönderirse yeni submitted snapshot + yeni key oluşur; eski key farklı payload ile kullanılmaz.
- Timeout/unknown outcome sırasında submitted snapshot dondurulur. Kullanıcı ya aynı attempt’i aynı key ile yeniden dener ya da onu açıkça terk edip değiştirilmiş payload için yeni attempt başlatır; UI sessizce key döndürmez veya otomatik yeni request göndermez.
- `idempotency_conflict` alındığında UI yeni key üretip kör retry yapmaz; conflict kullanıcıya güvenli biçimde gösterilir.
- Success veya durable replay aynı logical attempt’i tamamlar. Tek business kaynağına bir kez navigation/toast yapılır ve tamamlanmış key tekrar kullanılmaz.
- Logout, actor/session değişimi, şirket değişimi veya formun açıkça discard edilmesi in-memory attempt’i geçersiz kılar; eski attempt yeni context altında otomatik gönderilmez.
- Raw key localStorage/sessionStorage, URL, log veya telemetry’ye yazılmaz.

Bu kural **form oturumunda bir logical attempt için bir key** demektir; submit/retry tıklaması başına key üretmek mükerrer Cari riski yaratır, değişmiş payload’ı aynı key ile göndermek ise idempotency conflict üretir.

### 4.4 Replay

Backend:

```text
Idempotency-Replayed: true
Idempotency-Original-Correlation-Id: <ilk istek>
```

ile replay döndürürse UI aynı business kaydı ikinci kez yaratılmış gibi ikinci toast, ikinci liste insert’i veya zincirlenmiş ikinci navigation üretmez. Mevcut kaynağa tek kez gider.

## 5. C-02A hata sözleşmesi

| Durum | Stable code / davranış | UI sonucu |
|---|---|---|
| 400 | `validation_failed` | alan/özet hatası; form korunur; localized detail ile branch yapılmaz |
| 400 | `command_metadata_invalid` | logical attempt/transport hatası; otomatik yeni key yok |
| 401 | `authentication_required` | shell session-ended; business cache ve form authority kapanır |
| 403 | `forbidden` | form veya kayıt bilgisi gösterilmez; write etkisi 0 |
| 409 | `idempotency_conflict` | aynı key farklı actor/payload; otomatik tekrar yok |
| 415 | unsupported media type | güvenli teknik hata; business etkisi 0 |
| 500/503/offline | platform güvenli hata | form korunur; outcome kesin değilse aynı attempt retry |
| malformed 201/body | success sayılmaz | navigation yok; safe unknown/unexpected state |

Missing/multiple/comma-joined/blank/overlength `Idempotency-Key` server’da fail-closed `command_metadata_invalid` olur ve Party/account/audit/CommandExecution etkisi bırakmaz.

## 6. Candidate read sözleşmesi — Issue #95

Eksik endpoint:

```http
GET /api/v1/counterparties/company-account-link-candidates
X-Company-Id: <server tarafından doğrulanmış hedef şirket>
```

### 6.1 Hedef yetki

Hedef şirkette birlikte ve company-wide:

- `counterparty.accounts.create`
- `counterparty.parties.read`

Son kullanıcı düzeltmesine göre `counterparty.accounts.create` v1’de yalnız kanonik `SYSTEM_ADMINISTRATOR` rolündedir. UI role adına bakarak authority türetmez; server capability/authorization kullanır. Diğer roller default-deny kalır.

### 6.2 Candidate olma şartı

Dönen Party:

- global `Party.IsActive=true`;
- actor’ın en az bir allowed ve `counterparty.parties.read` yetkili source şirket hesabından görünür;
- hedef şirkete henüz bağlı değil;
- yalnız actor’a görünür source account özetlerini taşır;
- source account pasif olabilir; bu durum candidate olmayı tek başına engellemez;
- global pasif Party candidate değildir.

Foreign/unseen Party varlığı farklı hata, count, timing veya item shape ile açıklanmaz.

### 6.3 Query

| Parametre | Kural |
|---|---|
| `q` | C-01 ile aynı trim + NFC + Turkish case-insensitive, accent-sensitive contains |
| `pageSize` | 1–100; default 25 |
| `cursor` | opaque/authenticated; target company, actor/session, permission/policy, query ve expiry’ye bağlı |

Invalid/tampered/expired/scope-mismatch cursor:

```text
HTTP 400
code = invalid_cursor
partial candidate data = yok
```

Browser başka şirketlere ayrı ayrı çağrı yapıp candidate union oluşturmaz.

### 6.4 Candidate item

Yalnız:

```text
id
code
kind
legalName
tradeName
isActive
visibleSourceAccounts[]
  id
  companyId
  roles
  isActive
  version
```

`externalIntegrationCode`, banka/vergi/sicil, bakiye, risk, target foreign account veya yetkisiz source account yoktur. Source `companyId`, Organization directory ile kullanıcı şirket etiketi/koduna çevrilir; ham UUID metni gösterilmez.

## 7. CARI-LINK workspace UX

### 7.1 Route ve target context

```text
Route ID: cari.company-link
Path: /cari/sirkete-bagla
Context: single target company
Safe return: cari.list
```

Hedef şirket workspace içinde serbestçe seçilen body/query alanı değildir. Kullanıcı shell şirket seçicisiyle hedef CompanyContext’i commit eder; workspace onu salt okunur gösterir.

CompanyContext değişirse candidate sonuçları, cursor, seçim, source özetleri, form rolleri ve in-memory idempotency attempt temizlenir. Eski context response’u yeni target’a yazamaz.

### 7.2 Candidate görünümü

- Arama: kod / hukuki ad / ticari ad.
- Server cursor ile devam.
- Satır: Cari kodu, hukuki/ticari ad, tür, görünür source şirket etiketleri, source roller ve source account aktiflikleri.
- Global aktiflik yalnız candidate contract gereği aktiftir; UI bunu target account aktifliği gibi göstermez.
- Preselection hint authority değildir. Party, fresh target-scoped candidate response içinde görülmeden seçili kabul edilmez.

### 7.3 Hedef roller

- Müşteri / Tedarikçi / Fason / Hizmet.
- En az bir rol zorunlu.
- Source roller hedefe otomatik kopyalanmaz veya varsayılan authority sayılmaz.
- Kullanıcı hedef ilişki rollerini açıkça seçer.
- Kullanıcı gerekçesi alanı yoktur; authoritative karar gereği missing reason link rejection nedeni değildir.

### 7.4 Link command

```http
POST /api/v1/counterparties/{partyId}/company-account
X-Company-Id: <aynı hedef CompanyContext>
Idempotency-Key: <tek logical attempt>
Content-Type: application/json

{
  "roles": 3
}
```

Path PartyId yalnız fresh candidate seçiminin kimliğidir. Body/query/path ile target CompanyId override edilemez.

Başarı:

```text
HTTP 201
Party sayısı değişmez
hedef CompanyPartyAccount = 1 yeni kayıt, active
AuditEvent = counterparty.company-account.created.v1, 1
CommandExecution = 1
```

Başarı response’u yalnız hedef hesapla C-03’e bağlanır. Source hesap pasif/aktif durumu değişmez.

## 8. CARI-LINK hata ve yarış davranışı

| Durum | Stable code | UI / business sonucu |
|---|---|---|
| candidate 400 | `invalid_cursor` / `validation_failed` | partial candidate yok; ilk sayfaya dön veya filtreyi düzelt |
| 401 | `authentication_required` | session-ended; candidate/form/cache authority kapanır |
| 403 | `forbidden` | target permission veya scope yok; candidate/Party sayısı sızmaz |
| POST 400 | `validation_failed` | roller/form korunur |
| POST 400 | `command_metadata_invalid` | key/metadata hatası; yeni key ile kör tekrar yok |
| POST 404 | `not_found` | missing/foreign/hidden/global-inactive ayrımı açıklanmaz; seçimi temizle, candidates yenile |
| POST 409 | `already_linked` | başka işlem target hesabı oluşturmuş olabilir; blind success değil, target C-03/read ile yeniden doğrula |
| POST 409 | `idempotency_conflict` | aynı key farklı actor/payload; otomatik yeniden gönderme yok |
| 500/503/offline | platform güvenli hata | unknown outcome’da aynı key/payload retry; source/target veri karışmaz |
| malformed | generic unexpected | partial Party/account render edilmez |

### 8.1 Yarışlar

- **Aynı key + aynı payload:** tek business account/audit; diğer cevap replay.
- **Aynı key + farklı payload/actor:** `idempotency_conflict`; ikinci business etkisi yok.
- **Farklı key ile aynı Party/target yarış:** bir `201`, diğer `already_linked`; tek target account ve tek business audit.
- Candidate ile POST arasında Party global pasifleşirse `not_found`.
- Candidate ile POST arasında target account oluşursa `already_linked`.
- Source hesap pasifleşirse POST mevcut owner görünürlük kuralını yeniden doğrular; browser eski candidate’a güvenmez.

## 9. Capability ve activation

Önerilen mevcut route/capability kimlikleri accepted Cari contracttan gelir:

| Ekran | Route | Capability | Permission |
|---|---|---|---|
| C-02A create | `cari.create` / `/cari/yeni` | `cari.parties.create` | `counterparty.parties.create` |
| Link workspace | `cari.company-link` / `/cari/sirkete-bagla` | `cari.accounts.link` | `counterparty.accounts.create` + `counterparty.parties.read` |

Capability provider önce `ImplementationAvailable=false` ile staging yapılabilir. Şu koşullar olmadan `true` olmaz:

- route/component merge;
- exact backend endpoint/OpenAPI;
- permission ve CompanyContext negatif acceptance;
- browser deep-link/revoke/stale-context testleri;
- fresh exact-head review ve coordinator merge.

## 10. Uygulama sahipliği ve sırası

### Codex

1. Mevcut C-02 create ve company-link POST’u kanonik OpenAPI/generated-client hattıyla doğrular; duplicate endpoint yazmaz.
2. Issue #95 candidate endpointini gerçek SQL, bounded query, cursor binding ve non-disclosure ile uygular.
3. C-02/link capability declarationlarını `ImplementationAvailable=false` staging olarak ekler; permission/role grant üretmez.
4. Stable errors, exact HTTP ve relational idempotency/race acceptance’ı tamamlar.
5. `externalIntegrationCode` ve Int64 alanlarının frontend’e güvenli projection/representation sınırını korur.

### Claude

1. Generated client + runtime decoder kullanır; request/response alanı uydurmaz.
2. `/cari/yeni` ve `/cari/sirkete-bagla` route/componentlerini uygular.
3. Dirty form, unknown outcome ve CompanyContext fence’i shell ile bağlar.
4. C-02/link durumlarını stable code ile gösterir; localized `title/detail` parse etmez.
5. Browser, responsive, keyboard, focus, live-region ve replay testlerini yazar.

### ChatGPT

- Bu spec ve ilgili acceptance’ın source/business parity’sini korur.
- Backend/frontend PR’larında kopya Party, silent role copy, reason requirement, VKN duplicate claim, CompanyId override veya idempotency retry sapmasını blocker sayar.
- Kaynakta olmayan işletme kararı çıkarsa `NEEDS-SINAN` önerir; bu dilimin mevcut çekirdek alanlarında açık karar yoktur.

## 11. Bilinçli kapsam dışı

- VKN/TCKN ve mükerrer kimlik motoru (`AT-009`).
- C-06 adres/kişi/iletişim.
- C-07 banka/vergi/sicil.
- C-11 birleştirme.
- Party global aktif/pasif command’i.
- Source account reactivation.
- Link için kullanıcı gerekçesi.
- Bakiye, risk, limit, açık kalem, finans, sipariş, stok, belge, AI.
- Yeni rol, permission grant, Identity reconciliation veya migration.
- Production activation.

## 12. Merge treni

```text
#139 shell foundation P1=0 + merge + new-main green
→ #151 C-01/C-03 JIT input accepted
→ corrected #147 C-01 merge + browser acceptance
→ C-03 backend/UI
→ Issue #95 candidate backend + OpenAPI
→ C-02 create UI
→ CARI-LINK workspace UI
→ capability activation
```

Aynı `CounterpartyEndpoints`/read-service yüzeyinde tek backend writer bulunur. Head veya base hareketi eski review/CI kanıtını geçersiz kılar. Lane yazarı kendi PR’ını merge etmez.
