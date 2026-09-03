# CARI-C01-C03-ACCEPTANCE-001 — Çalıştırılabilir kabul matrisi

- Süreç: Hızlı Teslim v3.0, JIT acceptance
- İş sahibi: `CHATGPT-BUSINESS`
- Backend kanıt sahibi: `CODEX`
- Frontend/browser kanıt sahibi: `CLAUDE-FRONTEND`
- Kaynak spec: `docs/features/CARI_C01_C03_JIT_SPEC_v1.md`
- Kanonik kabul bağlantıları: `AT-001`, `AT-010`, `AT-013`
- Bilinçli kapsam dışı kanonik kabuller: `AT-009`, `AT-011`, `AT-012`, `AT-014`
- Normal provider current truth: **LIST=false / CREATE=false / LINK=false / DETAIL=false**
- Production activation: **NO-GO**

Bu dosya yeni `AT-*` kimliği üretmez. Aşağıdaki `CASE-*` kimlikleri yalnız mevcut kanonik kabullerin C-01/C-03 test vakalarıdır.

## 1. Ortak test verisi

### Şirketler

| Kimlik | Kod | Görünen ad | Aktör erişimi |
|---|---|---|---|
| `aaaaaaaa-0000-0000-0000-000000000001` | `GDC` | Göymen Demir Çelik | Actor-AB ve Actor-A |
| `bbbbbbbb-0000-0000-0000-000000000002` | `GSP` | Göymen Sac Profil | Actor-AB ve Actor-B |

Gerçek test seed’i farklı UUID kullanabilir; test çıktısı UUID’yi kullanıcı etiketi yapamaz.

### Aktörler

| Aktör | Erişim ve izin |
|---|---|
| `Actor-A` | Yalnız GDC; `counterparty.parties.read` company-wide |
| `Actor-B` | Yalnız GSP; `counterparty.parties.read` company-wide |
| `Actor-AB` | GDC ve GSP; ikisinde de `counterparty.parties.read` company-wide |
| `Actor-A-Narrow` | GDC erişimi var fakat company-wide Cari read izni yok |
| `Actor-None` | Cari read izni veya erişilebilir şirketi yok |

### Party ve şirket hesapları

| Party | Global alanlar | GDC hesabı | GSP hesabı |
|---|---|---|---|
| `P-001` | `C0001`, Tüzel, `Örnek Metal A.Ş.`, aktif | aktif, roles=Customer\|Supplier | yok |
| `P-002` | `C0002`, Tüzel, `Pasif İlişki Sanayi A.Ş.`, aktif | pasif, roles=Customer | yok |
| `P-003` | `C0003`, Gerçek Kişi, `Ayşe Örnek`, aktif | aktif, roles=Service | aktif, roles=Supplier |
| `P-004` | `C0004`, Tüzel, `Tarihsel Cari A.Ş.`, pasif | aktif, roles=Tolling | yok |
| `P-005` | `C0005`, Tüzel, `Yalnız GSP A.Ş.`, aktif | yok | aktif, roles=Supplier |

Test seed’inde Party ve account ID’leri boş olmayan UUID’dir. Bir şirket içinde aynı Party için ikinci account bulunmaz.

## 2. Kanıt standardı

Her vaka en az şu çıktıları üretir:

- HTTP request/response ve stable problem code.
- Gerçek SQL Server sonucunda görünür Party/account kümesi.
- Frontend component veya browser sonucu.
- DOM/accessibility ağacında foreign/hidden veri bulunmadığına ilişkin negatif assertion.
- DB write cardinality: `0`.
- `CommandExecution`/idempotency cardinality: `0`.
- Business audit cardinality: `0`; yalnız mevcut request correlation/log/trace kabul edilir.
- Skip sayısı: `0`.

Mock/InMemory testi gerçek SQL veya browser kanıtının yerine geçmez.

### 2.1 Normal-runtime `ImplementationAvailable` kapısı

Bu bölüm yeni `CASE-*` veya kanonik `AT-*` üretmez. Her route kendi exact-head kanıtıyla bağımsız açılır.

Owner source bu PR merge edildiğinde tanımlıdır. PR #192 birleştiği için list ve detail activation implementation başlangıç uygunluğu `true`dır. Issue #148'in kalan bulguları C-03 candidate'ında kapanır; detail runtime GO'yu kanıt tamamlanana kadar bloke eder, implementation başlangıcını değil. Bundan ayrı iki route'un da `runtimeActivationGo=false` gerçeği korunur; hiçbir normal-provider `ImplementationAvailable` alanı yalnız doküman merge'iyle `true` olmaz.

| Route | Current normal provider | Aktivasyon hedefi | Açık blocker |
|---|---|---|---|
| `cari.parties.list` | `SingleOrAuthorizedMultiCompanyRead + false` | C-01A `SingleCompany + CompanyWide + true` | C-03 bağımsız activation + Issue #199 + Issue #204 normal-provider profili + native Back/Forward + beşli temiz seri |
| `cari.parties.detail` | `SingleOrAuthorizedMultiCompanyRead + false` | C-03 `SingleCompany + CompanyWide + true` | Issue #148 + Issue #204 normal-provider profili + ayrı C-03 SQL/browser/a11y + beşli temiz seri |

C-01A kanıtı üç sınıftır:

1. PR #192 final head `c7410fa0151118e9f9976ae6bd1b5f4e06bb5c82`, coordinator'ın kaydettiği beş ardışık tam `218/218` koşum ve final head'deki uzak `7/7` sonuçla merge SHA `68aa15c81c049330e5ff08bf75b25a9ec6217f82` olarak birleşmiştir. Beş koşuma ait beş ayrı uzak run kimliği yayımlanmamıştır; bunlar normal-provider uzak aktivasyon serisi diye yeniden sınıflandırılmaz. PR #183 `B-C01-001…010` `10/10` ve `SAB-006…009` `4/4`, `skip=0` da yalnız tamamlanmış davranış önkoşuludur.
2. PR #196 final head `7e284c7833e2a3130adc84d9c53efbdfc08f08ce`, merge `3fceea9e191907f370bf9e240b3b80cb8974b2d6` ve browser run `33721831760` ile `C01A-HISTORY-REVOKE-forward` vakasını `1/1`, `skip=0` çalıştırmıştır. Koşum `C01E2e__EnableListCapability=true` disposable override'ı kullanır; yalnız Forward davranış ölçümüdür. Native Back, normal-provider origin veya aktivasyon serisi değildir. Issue #199'un source/JUnit fail-open, cache-age başlangıcı ve crash-safe/exact-equality grant restore P1'leri kapanmadan bu run aktivasyon kredisi alamaz. Issue #204'ün disposable-key-free normal-provider profili olmadan beşli seri başlayamaz.
3. C-01 implementation C-03 beklenmeden başlayabilir; fakat evidence base, #148 kapandıktan, C-03 kendi normal-provider detail candidate'ıyla merge edildikten ve post-main CI yeşil olduktan sonra pinlenir. C-01 candidate yalnız LIST declaration'ını `SingleCompany + ImplementationAvailable=true` yapar; pinli base'deki bağımsız `detail=true` gerçeğini değiştirmez. Owner source LIST current-runtime/context/scope, `runtimeActivationGo=true` ve blocker `[]` alanları aynı first evidence-bearing head'de provider ile eşleşir.
4. Pinli base yalnız bağımsız aktif C-03 ile production row-link/route topolojisini taşır; LIST kapalıyken gerçek kullanıcı erişilebilirliği iddia etmez. Candidate LIST'i açınca exact iki production-navigation setup'ı kullanılabilir. `C01A-HISTORY-REVOKE-back`: shell bootstrap → production Cari click'i → ilk başarılı business response/satır → production satır linkiyle detail → revoke → `page.goBack()` ile `/cari`. `C01A-HISTORY-REVOKE-forward`: shell bootstrap → production Cari click'i → ilk başarılı business response/satır → `page.goBack()` ile shell → revoke → `page.goForward()` ile `/cari`. İlk bootstrap sonrası yeni `page.goto`, `window.history.pushState/replaceState`, doğrudan History API, test-invoked router navigation veya test-only route/link kanıt değildir.
5. Aynı candidate'ta gerçek Chromium + API/SQL exact iki direction case'ini çalıştırır. Her birinde ilk başarılı business response'tan dönüşe ölçülen `elapsedMilliseconds` değeri `30000`'den küçük olur; taze capability snapshot grantı içermez; post-revoke business GET `0`; önceden kurulan `MutationObserver`, DOM ve accessibility tree eski satırı hiçbir karede görmez; route non-disclosing kapanır. Grant revoke/restore Issue #199 ölçütlerine göre run/attempt/project/database'e bağlı, transaction'lı, process-kill sonrası dış cleanup ile geri alınabilir ve post-check'te `RolePermissions` preimage exact eşittir.
6. Normal-provider kökeni capability/revision tahminiyle çıkarılmaz. Running API DI sonrası tam bir `IShellCapabilityProvider` resolve eder: exact `CounterpartyShellCapabilityProvider` type'ı `normal`, exact `C01E2eShellCapabilityProvider` type'ı `c01-disposable` source channel'ını process içinde üretir; eksik/çoklu/bilinmeyen type fail-closed olur. Workflow/artifact etiketi ve `Configure` authority/revision dönüşü origin kaynağı değildir. Attestation resolved count/type/source channel/process-instance ile aynı process'in `registryRevision` değerini taşır. Empty override envelope exact normal provider'ı korur; actual API `IConfiguration` görünümünde beş disposable anahtar yoktur; bu yokluk job/runner ortamına uygulanmaz. API container ID, image ID/digest ve OCI revision kaydedilir; OCI revision candidate head'e eşittir. Current `sha256:3Giw…` ve `sha256:SXjo…` yalnız bugünkü declaration kümeleridir; `ComputeRevision` provider type'ını hashlemez ve HTTP `generation` origin göstermez.
7. Kalıcı artifact `(base SHA, head SHA, tested tree SHA, workflow/test SHA, run ID, attempt, API container ID, image ID/digest, OCI revision, process-instance ID, resolved provider count/type, provider origin, registry revision, API overrideKeysPresent=[], capability tuple, browser request correlations, server request bindings)`, endpoint/request sayaçları, cache zamanı, DOM mutation ve SQL preimage/equality sonuçlarını aynı container/process'e bağlar. Browser network artifact'ındaki her capability/business request correlation'ı, attested API'nin structured request kaydında server-attached container/process-instance ile exact bir kez eşleşir; client origin etiketi güven kaynağı değildir. Validator origin omit/spoof/disposable, disposable-selected+normal-label, eksik/çoklu/bilinmeyen provider, farklı container/process, eksik/çift/eşleşmeyen correlation, normal attestation+disposable browser traffic, API override, image/head drift veya direction case silme mutasyonlarını kırmızı yapar.
8. Hem C-03 hem C-01 kendi immutable `(base/head/tested-tree/workflow-test-source/OCI-revision/provider-origin/registry-revision)` kimliğinde beş ayrı tam uzak koşumu ardışık `completed/success` üretir. Run ID/attempt, API container ID, image ID/digest ve process-instance ID her run'a özgü atomik bağdır; dolu olmalı, run'lar arasında değişebilir ve streak kimliği diye eşitlenmez. Her run `providerOrigin=normal`, farklı run ID, `attempt=1`, test-runner retry/quarantine kapalı ve required `skipped/cancelled/timeout=0` taşır. C-01 route manifesti exact iki direction case'i ile mevcut `CASE-C03-009@list-available` dalını (`3/3`), C-03 route manifesti `CASE-C03-001…008 + CASE-C03-009@list-unavailable` kümesini (`9/9`) çalıştırır; suffix'ler yeni CASE kimliği değildir. Failure veya invariant base/head/tree/source/OCI/origin/revision değişimi seriyi sıfırlar; selective rerun sayılmaz. Aynı exact head tam CI/container/UI-golden ve iki bağımsız K3 `P0=0/P1=0` alır; merge tree tested tree ile eşittir ve kanıttan sonra toggle commit'i yoktur.

Issue #146'daki “iki istek de tekrarlar” şartı fail-closed dallarıyla uygulanır: grant korunmuş dönüşte taze capability ve business request'in tekrarı PR #192'nin merge edilmiş suite'inde kanıtlanır; grant kaldırılmış gerçek-browser dönüşünde taze capability reddinden sonra business GET'in tekrar etmemesi zorunludur. Capability korunurken ayrıca zorlanmış business `403` varyantı C-01A'nın ikinci bir aktivasyon eksiği değildir; business `401/403` sınırı tamamlanmış `SAB-006…009` ile izlenir. Normal declaration hedefi `cari.list + C-01 + /cari + cari.parties.list + SingleCompany + CompanyWide + counterparty.parties.read + true`dır; C-01B group/headerless renderer/implementation kapalıdır.

Issue #146 bütünüyle kapanmış değildir. [Owner denetimi](https://github.com/sinangym/GoymenERP/issues/146#issuecomment-5521747972) `4 PASS / 2 PARTIAL / 1 FAIL` vermiştir: `C01-AC-002` active/inactive/all matrisi ve inactive-row yokluğunu, `C01-AC-005` `invalid_cursor` sonrası yeni cursor'sız page-1 GET'i, `C01-AC-007` bütün filtre/continuation keyboard-visible-focus, mobil/%200 görev tamamlama ve exactly-once live announcement kanıtını mevcut vaka kimlikleri üzerinde tamamlar. Bu üç boşluk kapanmadan C-01 runtime GO yoktur.

Reviewed main'de C-03 `ImplementationAvailable=false` ve `runtimeActivationGo=false` kalır. Detail candidate'ın ilk evidence-bearing head'i normal provider detail declaration'ını `SingleCompany + CompanyWide + true` yaparken owner JSON'un detail current-runtime/context/scope, implementation-start `true/[]`, `runtimeActivationGo=true` ve blocker `[]` alanlarını exact eşler; list/create/link pinli base değerinde kalır. Aynı head Issue #148'in invalid/malformed/nil id no-call, route/response id binding, detail-only list-link non-disclosure, exact request/header, gerçek delayed A→B response rejection ve tam browser/a11y bulgularını; `CASE-C03-001…008 + CASE-C03-009@list-unavailable` exact JUnit manifestini (`9/9`, eksik/fazla/skip `0`; suffix yeni CASE değildir); normal-origin/container/image bağını, ayrı SQL/API/browser kanıtını, beşli temiz seriyi, fresh exact-head CI'ı, iki bağımsız K3'ü ve merge-tree eşitliğini tamamlar. C-03 C-01 kapalıyken direct deep-link ile bağımsız kanıtlanabilir; unavailable `/cari` linkini göstermez. C-03 merge + post-main yeşili, C-01 evidence base'inin pinlenme önkoşuludur; C-01 implementation başlangıç önkoşulu değildir. `ImplementationAvailable=true` yalnız normal/pre-production availability'dir; production cutover `NO-GO` kalır.

### 2.2 Issue #78 C-02 + company-link full-stack kapısı

Bu bölüm yeni `CASE-*`, `AT-*` veya `DEC-*` üretmez. Kanonik çalıştırılabilir küme mevcut `CARI_C02_LINK_ACCEPTANCE_v1.md` içindeki `C02-AC-001…014`, `CLINK-AC-001…020` ve `UX-AC-001…004` vakalarıdır; toplam `38/38` vaka eşlenir ve `skip=0` olur. Bağlar mevcut `AT-001/003/005/006/008/010/013`, `DEC-CAR-001…003`, `DEC-TEC-012/013` ve `DEC-TST-001/002` ile sınırlıdır. `C02-AC-014`, VKN/TCKN alanı ile duplicate motorunun bulunmadığını kanıtlayan zorunlu negatif vaka olarak `38/38` içinde geçer; global `AT-009` tamamlanmış sayılmaz.

#78 ancak C-01A normal-runtime activation merge'i ve bu merge sonrasında current-main CI yeşil olduğunda implementation'a açılır. İlk kanıt-bearing combined exact head; `/cari/yeni` ve `/cari/sirkete-bagla` gerçek route/component'lerini, generated client/runtime decoder'larını, bütün test/workflow kaynağını ve normal provider'da hem `cari.parties.create` hem `cari.accounts.link` için `SingleCompany + CompanyWide + ImplementationAvailable=true` değerini taşır. Aynı head'de owner JSON/provider current-runtime parity'si; iki route için `activationImplementationStartEligible=true`, route-local ve üst `activationImplementationStartBlockedBy=[]`, `runtimeActivationGo=true`, `runtimeActivationGoBlockedBy=[]`; üst C-02/link kapısında `state=unmerged-activation-evidence-candidate` ve `runtimeActivationGo=true` zorunludur. Reviewed main'de availability/GO değerleri `false`, üst başlangıç blocker'ları dolu ve kapı frozen kalır. İki route birlikte kanıtlanır; kanıttan sonra ayrı toggle commit'i yoktur. `cari.accounts.status.update`, group modları ve production deployment/cutover kapsam dışıdır.

Mevcut `C02-AC-001` ve `CLINK-AC-011` başarı sözleşmeleri target-scoped C-03'e bir kez yönlenir. Bu sözleşme ayrıca yetkili owner tarafından değiştirilmedikçe #78 full-stack GO, `cari.parties.detail ImplementationAvailable=true` olmadan verilemez; C-01'e sessiz redirect kanıt değildir.

#78, C-01/C-03 için tanımlı registry authority attestation'ını aynen yeniden kullanır. Origin yalnız çalışan API'nin post-DI exact provider count/type sonucundan türetilir: count `1`, tip `GoymenERP.Modules.Counterparty.Infrastructure.Shell.CounterpartyShellCapabilityProvider`; API `IConfiguration` disposable activation anahtarlarını içermez. Revision, `Configure` sonucu veya client/workflow/artifact etiketi origin kanıtı değildir. Origin/config/registry/endpoint ile her browser capability/business request'i aynı attested API container/process'ine server-side correlation ile exact bir kez bağlanır; disposable trafik normal etiketle birlikte fail-closed olur.

Aynı immutable `(base main SHA, candidate head SHA, tested integration tree SHA, workflow/test source SHA, OCI revision=head, resolved provider count/type, provider origin, registry revision)` kimliğinde beş ardışık temiz tam uzak koşum zorunludur. Run ID/attempt, API container ID, image ID/digest ve process-instance ID koşuma özgü dolu bağlardır ve koşumlar arasında değişebilir. Her koşum `providerOrigin=normal`, ayrı workflow run ID'si ve `attempt=1` ile gerçek SQL/API cardinality, normal-provider gerçek-browser create/link akışları, keyboard/focus/live-region/%200 zoom/%400 reflow/mobil ve `38/38`, `skip=0` kanıtını çalıştırır. Retry/quarantine kapalıdır; failure, timeout, skip/cancel veya invariant kimlik/origin değişikliği seriyi sıfırlar ve selective rerun sayılmaz. Aynı exact head tam CI/container/UI-golden ve iki bağımsız K3 `P0=0/P1=0` alır. Base/head değişirse kanıt geçersizdir; merge sonucu tree SHA test edilmiş tree ile aynı olmalıdır.

## 3. C-01 Cari Kartlar vakaları

### CASE-C01-001 — birleşik roller, tek Party

**Kaynak:** `DEC-CAR-001`, `AT-010` read bölümü.

**Actor/context:** Actor-A, GDC.

**İstek:** varsayılan filtrelerle `GET /api/v1/counterparties`, `X-Company-Id=GDC`.

**Beklenen:**

- P-001 tam bir satırdır.
- `Müşteri` ve `Tedarikçi` iki etiket olarak aynı satırda görünür.
- P-001 ikinci kez listelenmez.
- Tek GDC account’u görünür; role başına account yaratılmaz.
- `Taraf Aktif` ve `Şirket hesabı Aktif` ayrı erişilebilir metindir.

**Beklenmeyen:** bakiye/risk/limit/sipariş/stok/AI alanı; GSP account bilgisi; DB write/audit/idempotency.

### CASE-C01-002 — global aktif, şirket hesabı pasif

**Kaynak:** `DEC-CAR-002`, `DEC-CAR-003`, `AT-013` read bölümü.

**Actor/context:** Actor-A, GDC.

**Matris:**

| Query | P-002 |
|---|---|
| default `partyState=active`, `accountState=all` | görünür |
| `partyState=active&accountState=active` | görünmez |
| `partyState=active&accountState=inactive` | görünür |

Görünür durumda `Taraf Aktif` ve `Şirket hesabı Pasif` ayrı gösterilir. Tek “Cari pasif” etiketi yoktur.

### CASE-C01-003 — global pasif Party

**Kaynak:** `DEC-CAR-003`, `AT-013` read bölümü.

- Varsayılan `partyState=active` ile P-004 görünmez.
- `partyState=inactive` veya `all` ile P-004 görünür.
- Görünür durumda global pasiflik açıkça gösterilir.
- Bu read ekranı “yeni işleme seçilemez” kısmını tamamlamış sayılmaz; o assertion tüketen write ekranındadır.

### CASE-C01-004 — seçili şirket izolasyonu

**Kaynak:** `AT-001`, `DEC-CAR-002`.

**Actor/context:** Actor-AB, shell active GDC.

**Beklenen:**

- Request yalnız `X-Company-Id=GDC` taşır.
- P-003 varsa yalnız GDC account’u `Hizmet/Aktif` olarak görünür.
- GSP `Tedarikçi` rolü, GSP account durumu, GSP ID/adı ve GSP’ye ait count DOM/state/accessibility ağacında bulunmaz.
- P-005 görünmez.

Aynı actor shell active GSP’ye geçince fresh request yapılır; geç GDC cevabı GSP listesine yazamaz.

### CASE-C01-005 — izin yok ve non-disclosure

| Actor/context | Beklenen HTTP/UI |
|---|---|
| Actor-A-Narrow / GDC | `403 forbidden`, liste/count/isim yok |
| Actor-None | güvenli permission/no-company state, kayıt varlığına ilişkin ipucu yok |
| Actor-A / yabancı GSP header | `403 forbidden`, GSP Party/count yok |

403 ekranı localized `detail` parse etmez ve probing sonucu üretmez.

### CASE-C01-006 — arama ve tür

**Actor/context:** Actor-A, GDC.

- `q` code/legalName/tradeName contains davranışını yalnız server sonucu belirler.
- UI trim/NFC dışında client-side Turkish case/accent algoritması uygulamaz.
- `kind=LegalEntity` yalnız tüzel; `NaturalPerson` yalnız gerçek kişi döndürür.
- 200 rune üzeri, bilinmeyen veya çoklu tekil parametre `400 validation_failed` olur; partial data yoktur.

### CASE-C01-007 — rol any/all aynı account içinde

Fixture’a GDC’de roles=`Customer|Supplier` olan P-001 ve yalnız Customer olan başka Party eklenir.

| Query | Beklenen |
|---|---|
| `role=Customer&role=Supplier&roleMatch=any` | en az bir biti taşıyanlar |
| `role=Customer&role=Supplier&roleMatch=all` | iki biti aynı GDC account’unda taşıyan P-001 |

Ayrı şirketlerdeki roller birleştirilerek `all` sağlanamaz.

### CASE-C01-008 — sort görünür seçenekleri

- `sort=code` ve `sort=legalName`, `asc/desc` server sırasıyla gösterilir.
- UI satırları yeniden sıralamaz.
- Backend `updatedAt` kabul etse de response satırı timestamp taşımadığından görünür “Güncellenme” kolonu veya sıralaması yoktur.
- Tarih uydurulmaz.

### CASE-C01-009 — cursor continuation

**Fixture:** pageSize sınırını aşan görünür Party kümesi.

1. İlk sayfa `hasMore=true` ve non-empty opaque `nextCursor` döndürür.
2. “Daha fazla göster” aynı filtre/context ile cursor’ı aynen gönderir.
3. Doğrulanmış ikinci sayfa append edilir.
4. Aynı Party ID devam sayfasında yeniden gelirse sayfa malformed sayılır; sessiz dedup yoktur.
5. Filter/sort/company/actor/session/generation değişirse eski cursor kullanılmaz.
6. Cursor localStorage/sessionStorage veya URL paylaşım otoritesi değildir.

### CASE-C01-010 — invalid cursor recovery

Tampered, expired veya scope-mismatch cursor:

- HTTP `400`, code=`invalid_cursor`.
- Reddedilen devam sayfası append edilmez.
- Cursor zinciri silinir.
- İlk sayfa fresh alınır.
- Kullanıcıya `Liste güncellendi; ilk sayfaya dönüldü.` gösterilir.
- Localized `title/detail` üzerinden branching yapılmaz.

### CASE-C01-011 — count scope parity

Runtime decoder yalnız aşağıdaki şekilleri kabul eder:

| totalCount | countScope | Sonuç |
|---|---|---|
| `null` | `null` | kabul |
| non-negative kayıpsız int64 | `filtered-authorized-visible-parties` | kabul |
| field eksik | herhangi | malformed |
| count var | `null`/bilinmeyen | malformed |
| count yok | non-null | malformed |
| negatif/ondalık/geçersiz string | herhangi | malformed |

Default `includeCount=false`; geçerli count olmadan üst bilgi toplam göstermez.

### CASE-C01-012 — malformed company binding

Aşağıdakilerin her biri bütün sayfayı reddeder:

- Party’de zero account.
- Active company dışında foreign account.
- Aynı selected company için iki account.
- Duplicate Party ID veya account ID.
- Nil/malformed UUID.
- `roles=0`, unknown bit veya 15 dışı değer.
- Boş code/legalName.
- `hasMore`/`nextCursor` uyuşmazlığı.

Hiçbir kısmi satır veya foreign değer render edilmez.

### CASE-C01-013 — ilk/sonraki sayfa hata davranışı

- İlk sayfa 500/503/offline: blocking retry; eski şirket satırı yok.
- Sonraki sayfa 500/503/offline: önceki doğrulanmış sayfalar korunur, yeni append yok, retry vardır.
- Malformed body: raw body/HTML render edilmez.
- Stale generation sonucu: sessiz discard; başarı mesajı yok.

### CASE-C01-014 — erişilebilirlik ve responsive

Browser kabulü:

- semantic table/list ve başlık ilişkileri;
- sort kontrolünde mevcut alan ve yön erişilebilir;
- filtreler ile continuation yalnız klavyeyle çalışır;
- görünür focus;
- status yalnız renkle anlatılmaz;
- error/empty değişimi kontrollü live region;
- %200 zoom ve mobil reflow’da arama/filtre/liste/continuation tamamlanabilir;
- foreign data accessibility tree’de yoktur.

## 4. C-03 Cari 360 çekirdek vakaları

### CASE-C03-001 — tek kimlik ve roller

**Kaynak:** `DEC-CAR-001`, `AT-010` read bölümü.

**Actor/context:** Actor-A, GDC; P-001.

**Beklenen:** tek Party başlığı, tek GDC account özeti, Müşteri+Tedarikçi etiketleri, global ve account durumu ayrı. DB write/business audit/idempotency `0`.

### CASE-C03-002 — aktiflik ayrımı

P-002 detail:

```text
Taraf: Aktif
Şirket hesabı: Pasif
```

Capability olmadan create/link/status aksiyonu gösterilmez. Account pasifliği global Party’yi pasif göstermez.

### CASE-C03-003 — şirket izolasyonu

Actor-AB active GDC ile P-003 detail:

- Request GDC header taşır.
- Yalnız GDC account `Hizmet/Aktif` görünür.
- GSP Supplier/account/ID/ad/count yoktur.

### CASE-C03-004 — direct-ID non-disclosure

Aşağıdaki üç durum aynı `404 not_found` görünümüne sahiptir:

1. Olmayan Party ID.
2. Yalnız GSP’de görünür P-005’e GDC context’inden istek.
3. Policy/permission nedeniyle gizli Party.

İsim, rol, account sayısı, foreign şirket veya ayrıştırıcı açıklama gösterilmez.

### CASE-C03-005 — stale detail

1. GDC/P-001 detail isteği geciktirilir.
2. Shell GSP context’ine geçer ve fresh capability commit eder.
3. Eski GDC cevabı bırakılır.

Beklenen: GDC Cari adı, title, breadcrumb, rol veya page cache GSP ekranına yazılmaz. GSP içeriği fresh request olmadan açılmaz.

### CASE-C03-006 — hassas alan

Geçerli backend body `externalIntegrationCode` taşısa bile:

- görünür metin, DOM attribute, accessible name, log, telemetry, localStorage veya sessionStorage’da bulunmaz;
- tercih edilen backend projection bu alanı hiç yayınlamaz;
- runtime decoder alanı bilinçli discard eder.

### CASE-C03-007 — malformed detail

Zero account, foreign account, duplicate account, nil UUID, unknown role biti veya eksik required Party alanı bütün cevabı reddeder. Partial unvan gösterilmez.

### CASE-C03-008 — owner’sız alanları uydurmama

C-03A’da risk, bakiye, açık kalem, zaman çizgisi, sipariş, stok, belge, banka/vergi/sicil veya AI paneli bulunmaz. `0`, `—`, boş kart veya “yakında” sekmesi de gösterilmez.

### CASE-C03-009 — route ve browser

- `/cari/:partyId` yalnız detail capability ile mount edilir.
- Boş/bozuk UUID’de backend çağrısı yok; güvenli not-found.
- Deep-link, refresh, back/forward ve yeni sekme fresh capability + backend authorization ister.
- Fresh list capability varsa main heading/focus ve keyboard-only `Cari Kartlar’a dön` production link'i çalışır. Detail-only candidate'ta bu link hiç render edilmez; dönüş yalnız başka yetkili production shell navigation'ıyla veya mevcutsa browser-native history entry'siyle olur ve unavailable `/cari` hedefi açıklanmaz.
- %200 zoom ve %400 reflow’da çekirdek kimlik/account özeti kullanılabilir.

## 5. Kanonik AT tamamlanma matrisi

| AT | Modül/dilim | Bu dosyadaki kanıt | Statü kuralı |
|---|---|---|---|
| `AT-001` | Shell + C-01 + C-03 | CASE-C01-004/005/012/013, CASE-C03-003/004/005/007 | gerçek SQL+HTTP+browser birlikte yeşil olmadan tamamlanmaz |
| `AT-009` | C-02 create | `CARI_C02_LINK_ACCEPTANCE_v1.md` / C02-AC-014 | VKN/TCKN bu alt dilimde yok; duplicate motoruna kadar açık |
| `AT-010` | C-01/C-03 read + C-02 write | CASE-C01-001/007, CASE-C03-001; future #78 için mevcut C02-AC kümesi | burada yalnız read görünümü kısmi; write ancak #78 kapısından sonra |
| `AT-011` | C-04/C-05/finans | API para döndürmez; UI para göstermez | omission completion değildir |
| `AT-012` | C-11 merge | bu dosyada yok | belge/audit bağlarıyla ayrı dilim |
| `AT-013` | C-01/C-03 read + tüketen işlem ekranları | CASE-C01-002/003, CASE-C03-002 | tarihsel görünüm kısmi; yeni işleme seçilememe sonra |
| `AT-014` | C-07 + ilgili işlem | bu dosyada yok | vergi/banka belge kapısı ayrı |

## 6. Merge kabul özeti

### C-01

- PR #169 merkezî business-read `401/403`, PR #183 C-01 gerçek SQL/browser `10/10` ve PR #188 kalıcı SAB `4/4`, `skip=0` browser/artifact kapısı main'dedir.
- Bu kanıt Testing/disposable C-01 override'ını kullanır; normal provider hâlâ `ImplementationAvailable=false`dır.
- PR #192 final head `c7410fa0…`, coordinator kayıtlı `5 × 218/218` ve görünür uzak `7/7` ile merge SHA `68aa15c8…` olarak birleşmiştir; taze Codex exact-head incelemesi ve gerçek-browser Back/Forward senaryosu waive edilmiştir. Route-entry suite'i, C-01 browser `10/10` ve SAB browser `4/4`, `skip=0` tamamlanmış önkoşuldur. Kalan C-01A kümesi yalnız history/revoke değildir: Issue #146 `C01-AC-002/005/007`, Issue #199, Issue #204 disposable-key-free normal-provider profili, C-03 sonrası evidence base, native Back+Forward ve beşli temiz seri §2.1'de birlikte zorunludur; jsdom veya disposable seri onların yerine geçmez.

### C-03

- PR #189 C-03 UI'yı main'e taşımıştır; normal provider `ImplementationAvailable=false` kalır.
- Issue #148 invalid/nil no-call, route/response ID binding, detail-only non-disclosure, exact request/header, delayed A→B ve Back/Forward tazelik bulguları açıktır.
- Ayrı C-03 gerçek SQL/browser/a11y JUnit/HTML paketi ve exact-head iki K3 incelemesi olmadan C-03 açılmaz.

### C-02 + company-link

- #78 implementation'ı C-01A activation merge'i ve post-main yeşilinden önce başlamaz.
- Full-stack GO; §2.2'deki mevcut `38/38` vaka, C-03 success-navigation bağı, beşli kararlılık serisi, tam exact-head CI ve iki bağımsız K3 tamamlanmadan verilmez.
- Create/link normal provider declaration'ları bugün `ImplementationAvailable=false` kalır.

Bu owner-source truth-sync'i provider, permission/grant, runtime veya production activation değiştirmez.
