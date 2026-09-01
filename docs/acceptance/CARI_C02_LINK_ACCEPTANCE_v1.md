# CARI-C02-LINK-ACCEPTANCE-v1 — Yeni Cari ve şirkete bağlama kabulü

- Kaynak spec: `docs/features/CARI_C02_LINK_JIT_SPEC_v1.md`
- Süreç: Hızlı Teslim v3.0 / JIT bir dilim önde
- Global acceptance ilişkisi: `AT-001`, `AT-003`, `AT-005`, `AT-006`, `AT-008`, `AT-010`, `AT-013`
- Açık kalan global acceptance: `AT-009`, `AT-011`, `AT-012`, `AT-014`
- Production activation: **NO-GO**

Bu dosyadaki `C02-AC-*` ve `CLINK-AC-*` kimlikleri task-local çalıştırılabilir vakalardır; yeni global `AT-*` kimliği üretmez. Her vaka gerçek SQL/HTTP ve gerektiğinde browser kanıtına bağlanır.

## 1. Ortak fixture sözlüğü

### Şirketler

| Kimlik | Anlam |
|---|---|
| `COMPANY_A` | actor’ın yetkili olduğu aktif hedef/source şirket |
| `COMPANY_B` | ikinci aktif Göymen şirketi |
| `COMPANY_FOREIGN` | actor’ın allowed setinde olmayan şirket |

Test çıktısında ham UUID kullanıcıya gösterilmez; şirket adları Organization directory fixture’ından gelir.

### Aktörler

| Aktör | Yetki/kapsam |
|---|---|
| `CREATE_A` | COMPANY_A’da company-wide `counterparty.parties.create` |
| `READ_ONLY_A` | COMPANY_A’da yalnız company-wide `counterparty.parties.read` |
| `SYSTEM_ADMIN_AB` | kanonik `SYSTEM_ADMINISTRATOR`; COMPANY_A ve B’de company-wide read/create/account-create/status-update |
| `NARROW_ADMIN_B` | B’de `counterparty.accounts.create` veya read dar Site/Location scope; company-wide değil |
| `FOREIGN_ACTOR` | COMPANY_A/B allowed setinde değil |

`counterparty.accounts.create` için v1 kabulü yalnız `SYSTEM_ADMINISTRATOR`dır. UI testleri role adına göre buton açmaz; capability fixture’ı server sonucunu temsil eder.

### Mevcut Party/account’lar

| Fixture | Durum |
|---|---|
| `P_ACTIVE_A` | global aktif; A’da aktif Customer source account; B’de account yok |
| `P_ACTIVE_A_PASSIVE_SOURCE` | global aktif; A source account pasif Supplier; B’de account yok |
| `P_INACTIVE_A` | global pasif; A account aktif; B’de account yok |
| `P_ALREADY_B` | global aktif; A source account + B target account mevcut |
| `P_FOREIGN_ONLY` | global aktif; yalnız actor’a görünmeyen foreign source account |
| `P_MULTI_SOURCE` | global aktif; A ve başka allowed source hesapları; B’de account yok |

## 2. Kanıt formatı

Her backend acceptance şu sonuçları açıkça raporlar:

```text
HTTP status + stable code
Party delta
CompanyPartyAccount delta
AuditEvent delta ve event type
CommandExecution delta/outcome
Idempotency-Replayed header
Original-Correlation-Id header
foreign CompanyId / name / role leakage
```

Read-only candidate vakalarında Party/account/audit/CommandExecution delta `0` olmalıdır.

Browser acceptance ayrıca:

- görünür metin ve accessible tree;
- request header/query/body;
- CompanyContext generation ve stale-response davranışı;
- focus/live-region/keyboard/mobile;
- tekrar toast/navigation/business effect sayısı

kanıtı üretir.

# Bölüm A — C-02A Yeni Cari

## C02-AC-001 — başarılı tek Party + tek şirket hesabı

**Actor:** `CREATE_A`  
**Context:** COMPANY_A  
**Payload:** Tüzel Kişi, hukuki ad `Örnek Metal Sanayi ve Ticaret A.Ş.`, ticari ad `Örnek Metal`, roles `Customer|Supplier`.

**Adımlar:**

1. `/cari/yeni` açılır.
2. Aktif şirket salt okunur COMPANY_A etiketi olarak görülür.
3. Form doldurulur ve tek kez `Cari oluştur` seçilir.

**Beklenen HTTP:** `201`.

**Beklenen kayıt:**

```text
Party +1
- server-generated CAR-xxxxxx code
- kind LegalEntity
- legalName trim edilmiş
- tradeName trim edilmiş
- IsActive true
- Version 1

CompanyPartyAccount +1
- CompanyId COMPANY_A
- PartyId yeni Party
- Roles Customer|Supplier
- IsActive true
- Version 1

AuditEvent +1
- counterparty.party.created.v1
- CompanyId COMPANY_A
- entity yeni Party

CommandExecution +1
- counterparty.party.create.v1
- Created / 201
```

**Beklenmeyen:** ikinci Party/account, B/foreign CompanyId, client-generated Cari code, user-entered reason, VKN/banka/risk alanı.

**Browser:** tek success sonucu; C-03’e yeni Party ID ile bir kez navigation.

## C02-AC-002 — birleşik roller kopya kayıt üretmez

**Payload:** roles=`Customer|Supplier|Service`.

**Beklenen:** bir Party, bir CompanyPartyAccount, account üzerinde bit flag birleşimi; üç rol etiketi. Rol başına Party/account yok.

**Kaynak:** `DEC-CAR-001`, `AT-010` write bölümü.

## C02-AC-003 — required/length/enum validation

Ayrı mutation setleri:

- unknown/zero `kind`;
- blank `legalName`;
- 251 karakter legalName;
- 201 karakter tradeName;
- `roles=0`;
- unknown role bit;
- missing required JSON property / malformed body.

**Beklenen:** `400 validation_failed` veya malformed transport için canonical 400; Party/account/audit/CommandExecution delta `0`; form korunur; UI localized `detail` parse etmez.

## C02-AC-004 — şirket body/query ile override edilemez

**Actor/context:** CREATE_A / COMPANY_A.  
**Giriş:** payload veya query’ye `companyId=COMPANY_B` benzeri fazladan veri ekleme denemesi; header COMPANY_A.

**Beklenen:** oluşturulan account yalnız server-resolved COMPANY_A’dadır veya unknown field contract gereği request reddedilir; B’de etki `0`. Browser CompanyId form alanı sunmaz.

## C02-AC-005 — permission default-deny

**Actor:** READ_ONLY_A.  
**Beklenen:** `403 forbidden`; Party/account/audit/CommandExecution `0`; form/record adı üzerinden probing yok.

**Not:** Client route/action capability olmadan görünmez; direct request yine backend’de reddedilir.

## C02-AC-006 — invalid Idempotency-Key cardinality

Ayrı HTTP vakaları:

- header yok;
- blank;
- 129+ karakter;
- comma-joined iki key;
- iki ayrı header value.

**Beklenen:** `400 command_metadata_invalid`; Party/account/audit/CommandExecution delta `0`.

## C02-AC-007 — same-key replay

**İlk istek:** key `K1`, payload P, actor CREATE_A, COMPANY_A → `201`.

**İkinci istek:** aynı key/actor/company/payload.

**Beklenen:** aynı Party resource; `Idempotency-Replayed=true`; original correlation header ilk isteği gösterir; toplam Party/account/audit/CommandExecution cardinality ilk başarıdan sonra artmaz. UI ikinci success toast/list insert/navigation üretmez.

**Kaynak:** `AT-005` idempotency bölümü.

## C02-AC-008 — same key, different payload veya actor

Mutation:

- K1 ile legalName/roles değişir;
- K1 başka actor tarafından kullanılır.

**Beklenen:** `409 idempotency_conflict`; yeni Party/account/audit/CommandExecution yok; UI otomatik yeni key üretip tekrar göndermez.

## C02-AC-009 — concurrent same-key create

İki eşzamanlı istek, aynı actor/company/key/payload.

**Beklenen:** tek Party/account/audit/CommandExecution; bir canonical success ve diğer aynı durable sonucun replay’i. Gerçek SQL transaction kanıtı zorunlu.

## C02-AC-010 — unknown outcome retry aynı attempt’i kullanır

İlk response browser’a ulaşmadan bağlantı kesilir; server transactionının işlendiği ve işlenmediği iki fixture ayrı çalışır.

- İşlendiyse aynı key/payload retry replay olur.
- İşlenmediyse aynı key/payload retry ilk success’i üretir.

Her iki durumda toplam business effect `1`; UI formu ve submitted snapshot’ı korur, otomatik resend yapmaz.

## C02-AC-011 — payload değişikliği yeni attempt ister

Unknown result sonrası kullanıcı hukuki ad veya rolleri değiştirir.

**Beklenen:** eski key yeni payload ile gönderilmez. UI önce eski sonucu kontrol/retry veya açıkça yeni attempt seçimi ister. Sessiz yeni key ve otomatik submit yok.

## C02-AC-012 — dirty form şirket değişimi

Form COMPANY_A’da değiştirildikten sonra A→B switch istenir.

- `Kal`: COMPANY_A context/form/key korunur.
- `Değişiklikleri bırak`: form/key temizlenir; B reauthorization sonrası boş form.

A payload’ı B header’ıyla gönderilmez; eski A response’u B ekranına yazmaz.

**Kaynak:** `AT-008`.

## C02-AC-013 — malformed success body fail-closed

Server `201` fakat eksik/foreign/duplicate account veya unknown enum/role bitli body döndürür.

**Beklenen:** UI başarı/navigation yapmaz; generic unknown-result state; raw response veya foreign ID DOM/log/persistent state’e girmez. Backend contract testinde canonical response ayrıca doğrulanır.

## C02-AC-014 — VKN/TCKN duplicate kabulü bu alt dilime ait değildir

C-02A formunda VKN/TCKN alanı ve duplicate endpoint yoktur.

**Beklenen:** UI sahte duplicate taraması/uyarısı yapmaz; aynı unvanı VKN duplicate kanıtı saymaz. `AT-009` açık kalır ve C-07/kimlik masterı ile sonraki genişlemede tamamlanır.

# Bölüm B — CARI-LINK Candidate Read

## CLINK-AC-001 — aktif görünür Party candidate olur

**Actor/context:** SYSTEM_ADMIN_AB / target COMPANY_B.  
**Fixture:** P_ACTIVE_A.

**Beklenen HTTP:** `200` candidate page.

**Görünür:** Party kimliği/adı/türü; yalnız actor-visible A source account özeti.  
**Görünmez:** foreign account, externalIntegrationCode, banka/vergi/risk/bakiye.

**DB delta:** tüm tablolar `0`.

## CLINK-AC-002 — pasif source account candidate olmayı engellemez

Fixture P_ACTIVE_A_PASSIVE_SOURCE.

**Beklenen:** candidate görünür; source account `Pasif` olarak etiketlenir. Target account henüz yoktur; source otomatik reaktifleşmez.

## CLINK-AC-003 — global pasif Party candidate değildir

Fixture P_INACTIVE_A.

**Beklenen:** listede yok; count/timing/item shape existence oracle üretmez; DB delta `0`.

## CLINK-AC-004 — target’ta mevcut account candidate değildir

Fixture P_ALREADY_B.

**Beklenen:** listede yok. Direct POST yarışında `already_linked` davranışı ayrıca CLINK-AC-012’de kanıtlanır.

## CLINK-AC-005 — foreign-only Party sızmaz

Fixture P_FOREIGN_ONLY.

**Beklenen:** item, count ve visibleSourceAccounts içinde yok; foreign CompanyId/name/role raw body, browser state, DOM ve accessible tree’de bulunmaz.

## CLINK-AC-006 — yalnız visible source hesaplar döner

Fixture P_MULTI_SOURCE; actor yalnız source A’yı okuyabilir.

**Beklenen:** source A özeti var; diğer source relation yok. Party rolü hesaplar arasında union edilmez.

## CLINK-AC-007 — target permission matrisi

Ayrı aktörler:

- target read var, account-create yok;
- account-create var, read yok;
- ikisi var fakat biri dar scope;
- ikisi company-wide SYSTEM_ADMINISTRATOR.

**Beklenen:** ilk üçünde `403`, candidate data ve count yok; sonuncuda yetkili sonuç. UI role textinden izin türetmez.

## CLINK-AC-008 — target CompanyContext tek otoritedir

Header target B iken body/query/path ile A veya foreign target override denemesi.

**Beklenen:** candidate çözümü yalnız B; override parametresi reddedilir veya yok sayılır ve hiçbir A/foreign veri sızmaz.

## CLINK-AC-009 — Turkish search ve cursor

Fixture code/legal/trade adlarında `I/İ/ı/i`, aksanlı ve aksansız değerler.

**Beklenen:** Issue #94 ile aynı server search semantiği; browser lokal yeniden filtreleme yapmaz. `pageSize=25` default, max 100. Cursor target B, actor/session, query ve permission revision’a bağlıdır.

## CLINK-AC-010 — invalid cursor

Tampered, expired, başka target/actor/query/policy cursor’u.

**Beklenen:** `400 invalid_cursor`; partial candidate data yok; browser cursor zincirini atar ve ilk sayfaya döndüğünü duyurur.

# Bölüm C — CARI-LINK Write

## CLINK-AC-011 — başarılı target account oluşturma

**Actor/context:** SYSTEM_ADMIN_AB / target B.  
**Candidate:** fresh response içindeki P_ACTIVE_A.  
**Roller:** Customer|Supplier.  
**Reason alanı:** yok.

**Beklenen HTTP:** `201`.

**Kayıt:**

```text
Party delta = 0
B CompanyPartyAccount +1, active, roles Customer|Supplier
A source account delta = 0
AuditEvent +1 = counterparty.company-account.created.v1, CompanyId B
CommandExecution +1 = counterparty.company-account.create.v1 / Created
```

**Browser:** target-scoped C-03’e bir kez gider; source/target rolleri birbirine karışmaz.

## CLINK-AC-012 — different-key duplicate race

Aynı Party/target için iki eşzamanlı request, farklı key.

**Beklenen:** biri `201`, diğeri `409 already_linked`; B’de tek account, tek business audit. Her ilk terminal attempt kendi durable CommandExecution sonucunu taşır; duplicate business effect yok.

## CLINK-AC-013 — same-key replay ve mismatch

- Aynı key/actor/target/roles → durable replay, ikinci account/audit yok.
- Aynı key farklı roles veya actor → `409 idempotency_conflict`, ikinci business effect yok.

Replay ikinci toast/navigation üretmez.

## CLINK-AC-014 — candidate ile POST arasında global pasifleşme

P_ACTIVE_A fresh candidate seçildikten sonra Party global pasif olur.

**Beklenen:** POST `404 not_found`; missing/foreign/inactive ayrımı açıklanmaz; B account/audit `0`; terminal NotFound CommandExecution first attempt için `1`, aynı-key replay aynı sonucu verir.

## CLINK-AC-015 — candidate ile POST arasında target link oluşması

Fresh candidate sonrası başka işlem B account’u oluşturur.

**Beklenen:** `409 already_linked`; ikinci account/audit yok. UI blind success demez; candidate/detail’i yeniden alır ve yalnız fresh authorized read sonrasında mevcut ilişkiyi gösterir.

## CLINK-AC-016 — no user-entered reason

Request body yalnız `roles` taşır. Missing `reason` hiçbir validation rejection üretmez.

**Beklenen:** diğer şartlar sağlanırsa `201`; audit’in server-owned fixed business reason’ı vardır. UI reason alanı göstermez.

**Kaynak:** authoritative Issue #43 correction `#issuecomment-5461694092`.

## CLINK-AC-017 — direct PartyId hint authority değildir

Workspace’e query/navigation state ile P_ACTIVE_A ID’si taşınır fakat fresh candidate response bu Party’yi içermez.

**Beklenen:** seçili kabul edilmez; POST otomatik gönderilmez; Party adı/source hesaplar gösterilmez.

## CLINK-AC-018 — CompanyContext switch bütün target state’ini temizler

B target workspace’inde candidate seçili ve roles formu dirty iken B→A switch.

- Onay verilmeden switch yok.
- Discard sonrası B candidate/cache/cursor/selection/roles/idempotency attempt temizlenir.
- Geç B response’u A workspace’ine yazmaz.

## CLINK-AC-019 — unknown outcome link retry

POST response kaybolur.

- Server account oluşturduysa aynı key/roles/Party/target retry replay olur.
- Server işlemediyse aynı attempt ilk success’i üretir.

Her durumda tek target account ve tek business audit; otomatik resend veya yeni key yok.

## CLINK-AC-020 — malformed success fail-closed

`201` body foreign/duplicate/missing target account veya unknown role bit içerir.

**Beklenen:** UI success/navigation yapmaz; partial Party/source/target data göstermez; same-key replay/authorized target detail ile sonuç doğrulanmadan yeni key oluşturmaz.

# Bölüm D — Browser ve erişilebilirlik

## UX-AC-001 — form semantiği ve klavye

- `h1` ve tek ana action.
- Kind ve roles kontrolleri doğru label/fieldset/legend taşır.
- Validation summary ilk invalid submitte focus alır; field errors `aria-describedby` ile bağlıdır.
- Pending submit focus’u kaybetmez ve ikinci submit kapalıdır.
- Görünür focus; renk tek hata/durum sinyali değildir.

## UX-AC-002 — responsive/reflow

C-02 ve link workspace:

- desktop/tablet/mobile’da aynı iş tamamlanabilir;
- %200 zoom ve %400 reflow’da yatay zorunlu form kaydırması yok;
- candidate satırı mobilde kart/özet biçimine dönebilir fakat alan anlamı değişmez;
- source şirket etiketi ham UUID’ye dönüşmez.

## UX-AC-003 — live regions

- validation/error/unknown outcome ve invalid-cursor mesajı kontrollü live region’da bir kez duyurulur;
- replay ikinci success duyurusu üretmez;
- stale response sessiz atılır, yanlış başarı duyurusu yoktur.

## UX-AC-004 — deep-link ve capability revoke

- Capability yokken `/cari/yeni` ve `/cari/sirkete-bagla` non-disclosing safe state; API write/candidate çağrısı yok.
- Route açıkken capability revoke/policy revision değişirse form/candidate/business cache kapanır.
- Browser back/forward/refresh fresh capability ve backend authorization olmadan korumalı içerik flash’i üretmez.

# Bölüm E — Global AT doğruluk matrisi

| Global AT | Bu dilimdeki kanıt | Durum |
|---|---|---|
| `AT-001` | target CompanyContext, permission ve foreign-source non-disclosure | kısmi/ilgili bölüm |
| `AT-003` | create/link başarılı AuditEvent; rejected requestte business audit `0` | kısmi/ilgili bölüm |
| `AT-005` | idempotency, concurrent same/different-key ve conflict | create/link bölümü |
| `AT-006` | validation/unknown outcome form verisini kaybettirmez | browser bölümü |
| `AT-008` | dirty form başka şirkete sessiz taşınmaz | browser bölümü |
| `AT-010` | tek Party + tek account üzerinde birleşik roller | write/read görünümü |
| `AT-013` | global pasif Party link candidate değildir; history ayrı | kısmi |
| `AT-009` | VKN/TCKN alanı ve duplicate motoru yok | **tamamlanmadı** |
| `AT-011` | bakiye bu ekranlarda yok | **tamamlanmadı** |
| `AT-012` | Party merge yok | **tamamlanmadı** |
| `AT-014` | vergi/banka belge kapısı yok | **tamamlanmadı** |

# Bölüm F — Merge kapıları

## Backend

- Issue #95 candidate endpoint + gerçek SQL/HTTP acceptance.
- Existing create/link endpoint OpenAPI parity.
- Permission/CompanyContext/default-deny.
- Same-key/different-key/replay/unknown outcome.
- Candidate read DB delta `0`.
- Successful writes exact Party/account/audit/CommandExecution cardinality.
- No `externalIntegrationCode`/foreign source disclosure.
- P0/P1 `0`, fresh exact-head CI/real SQL/container.

## Frontend

- Generated client veya açık geçici handoff; ikinci DTO business truth yok.
- Runtime response binding ve Int64 kayıpsızlığı.
- Stable code branching.
- Dirty form + unknown attempt + context fence.
- Browser/keyboard/mobile/reflow/live-region.
- Capability-gated route/action; direct backend authorization.
- P0/P1 `0`, fresh exact-head review.

## Activation

Capability declaration `ImplementationAvailable=true` yalnız backend + route + browser kabulünden sonra ayrı exact-head değişiklikle etkinleşir. Production activation bu dosyanın kapsamında değildir.