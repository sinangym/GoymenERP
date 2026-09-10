# Cari oluştur → listede bul → detay → şirkete bağla: frontend API devri

- Backend tabanı: `9f5b9bfdef249b9c8e89c6668f66b6aa55c34602`.
- Şerit: `CODEX`, branch: `codex/cari-create-list-detail-link-journey`.
- Görev/sahiplik: [Issue #66 claim](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5551797478); üst ürün kapsamı Issue #78.
- Kanonik sözleşme: `contracts/openapi/goymen-erp-v1.json`.
- Yukarıdaki tabandaki OpenAPI dosyasının SHA-256 değeri: `3e583a4e6cbf2b9c97e0aa44fb6b92f919381890ca7c017ecb9802ba821a9721`.
- Bu dosya yeni API, iş kuralı, permission, aktivasyon veya master rapor değildir. Mevcut backend'in frontend tarafından doğru kullanılmasına yönelik dar uygulama devridir.

## 1. Ne mevcut, ne bu devrin dışında?

Beş endpoint türü mevcut; tek yolculukta liste/detay farklı şirket bağlamlarında tekrar kullanılır:

| Sıra | Şirket | HTTP isteği | Beklenen başarılı sonuç |
|---|---|---|---|
| 1 | A | `POST /api/v1/counterparties/` | `201`; bir Party ve A hesabı |
| 2 | A | `GET /api/v1/counterparties/?q=<dönen code>&includeCount=true` | `200`; aynı Party listede |
| 3 | A | `GET /api/v1/counterparties/<dönen id>` | `200`; aynı Party, yalnız A hesabı |
| 4 | B | `GET /api/v1/counterparties/company-account-link-candidates?q=<dönen code>` | `200`; B'ye henüz bağlı olmayan aynı Party ve yetkili kaynak hesabı |
| 5 | B | `POST /api/v1/counterparties/<aynı id>/company-account` | `201`; yeni Party değil, yalnız B hesabı |
| 6 | B | Liste ve detay GET'lerini tekrarla | Aynı Party ID/kodu; yalnız B hesabı |

Başlangıçta B listesi Party'yi içermez ve B detay GET'i `404/not_found` döner. B'ye bağlandıktan sonra B candidate listesi Party'yi artık içermez; A hesabı ve A'daki roller değişmez. Son toplam: bir Party, iki şirket hesabı. GET işlemleri business audit/CommandExecution yaratmaz.

`docs/features/CARI_C02_LINK_JIT_SPEC_v1.md` başındaki “candidate endpoint eksik” ifadesi belgenin eski tabanına aittir. Güncel endpoint, parser, application DTO'ları ve OpenAPI'de bu endpoint vardır. Endpoint yeniden yazılmaz; aşağıdaki mevcut sözleşme tüketilir. İş kuralı kaynağı olan `DEC-CAR-001/002/003` ve kabul senaryoları korunur; bu not sahipli feature/acceptance belgelerini değiştirmez.

Normal `CounterpartyShellCapabilityProvider` içindeki dört beyan hâlâ `ImplementationAvailable=false`:

| Capability | Route | Ekran |
|---|---|---|
| `cari.parties.list` | `/cari` | `C-01` |
| `cari.parties.create` | `/cari/yeni` | `C-02` |
| `cari.parties.detail` | `/cari/:partyId` | `C-03` |
| `cari.accounts.link` | `/cari/sirkete-bagla` | `CARI-LINK-WORKSPACE-001` |

API/SQL testinin geçmesi bu rotaları kullanıcıya açmaz. C-02 kimliği `C-02A` veya `C-02:create-subset` olarak yeniden adlandırılmaz; `create-subset` varsa scope variant'tır. Frontend/browser ve normal-runtime aktivasyon kanıtı ayrı sahipliktedir. #207/#203/#205 Claude, #210 CODEX-PLATFORM yüzeylerine bu devir yazmaz.

## 2. Kimlik ve şirket otoritesi

- Normal OIDC/JWT modunda `Authorization: Bearer <access token>` kullanılır; issuer/audience/lifetime doğrulanır ve ERP kimlik/rol ataması sunucuda çözülür.
- `DevelopmentBootstrap` yalnız `Development`/`Testing` ortamında kullanılabilir. Testteki `X-Development-User` fixture başlığı production kimlik doğrulaması değildir; normal JWT isteğine eklenmez.
- Şirket A/B kimlikleri yetkili Organization directory'den seçilir. Bu akışta her istekte **tek** `X-Company-Id` gönderilir; body, query, URL veya localStorage şirket yetkisini genişletemez.
- Create: A'da company-wide `counterparty.parties.create`.
- Liste/detay: seçili şirkette company-wide `counterparty.parties.read`.
- Candidate ve link: B'de company-wide `counterparty.accounts.create` **ve** `counterparty.parties.read`; ayrıca Party'nin en az bir mevcut kaynak hesabında company-wide read yetkisi. Hedef yetkileri olması tek başına bilinen bir Party ID üzerinden kaynak görünürlüğünü vermez.
- Global pasif Party aday değildir. Yetkili kaynak hesabının pasif olması tek başına bağlamayı engellemez; kaynak hesabı otomatik aktifleşmez. Hedefte mevcut aktif **veya pasif** hesap varsa ikinci hesap açılmaz.
- Rol adı UI'da erişim kararı değildir. `SYSTEM_ADMINISTRATOR` account-create atamasının kanonik politikası korunur; bu devir yeni rol/grant eklemez.
- Bu tek-şirket yolculuğunda headerless grup okuması kullanılmaz. Başlık olmadan GET'in sunucuda yetkili çok-şirket okuması desteklemesi, frontend'in şirketleri dolaşıp sonuçları birleştirmesine izin vermez.

## 3. Request ve response alanları

### Create ve link

Create JSON gövdesi:

```json
{"kind":1,"legalName":"Örnek Cari Yolculuk A.Ş.","tradeName":null,"roles":3}
```

Link JSON gövdesi:

```json
{"roles":2}
```

Bu değerler sentetik örnektir. JSON wire enum'ları **sayısaldır**: `kind=1` LegalEntity, `kind=2` NaturalPerson; rol bitleri Customer=1, Supplier=2, Tolling=4, Service=8. Örnekte A hesabı müşteri+tedarikçi (`3`), B hesabı tedarikçi (`2`) olur. `0`, bilinmeyen bit veya JSON string enum gönderilmez. Query enum'ları farklıdır; §4'e bakın.

`legalName` trim sonrası 1–250 karakter, `tradeName` isteğe bağlı ve trim sonrası en fazla 200 karakterdir. Cari kodu/UUID server tarafından üretilir. Create/link gövdesinde `companyId`, `code`, `id`, `expectedVersion`, `reason`, VKN/TCKN, banka, adres veya entegrasyon kodu yoktur. Özellikle link için kullanıcı gerekçesi talep edilmez. Bu create alt kümesi mükerrer VKN/TCKN kontrolünü tamamlamaz.

Create/link `201` response'u `CounterpartyMutationView`; detail `200` response'u `CounterpartyDetailView` alan yapısını taşır:

```text
id: UUID
code: string
kind: number
legalName: string
tradeName: string | null
isActive: boolean                    // global Party durumu
version: string                      // pozitif ondalık tamsayı tokenı
companyAccounts: [{
  id: UUID,
  companyId: UUID,
  roles: number,
  isActive: boolean,                 // şirket hesabının ayrı durumu
  version: string
}]
```

Mutation'da `companyAccounts` tam bir öğe taşır ve ilgili isteğin şirketiyle eşleşir. Detail de bu yolculuğun tek-şirket context'inde yalnız seçili şirket hesabını verir. `externalIntegrationCode` bu projeksiyonlarda yoktur. `Location: /api/v1/counterparties/<id>` create/link yanıtında gelir; frontend navigasyonu API Location'ını körlemesine URL olarak açmaz, doğruladığı `id` ile kanonik detail route'u kullanır.

### Liste ve candidate birbirinin DTO'su değildir

| Cevap | Üst alanlar | Item hesap alanı | Version wire türü |
|---|---|---|---|
| `CounterpartyPage` | `items,nextCursor,hasMore,totalCount,countScope` | `companyAccounts[]` | Party/account **numeric int64** |
| `CounterpartyDetailView` | Yukarıdaki tek Party | `companyAccounts[]` | Party/account **string** |
| `CounterpartyMutationView` | Yukarıdaki tek Party | `companyAccounts[]` | Party/account **string** |
| `CounterpartyLinkCandidatePage` | `items,nextCursor,hasMore` | `visibleSourceAccounts[]` | Source account **string**; candidate Party'de `version` yok |

Candidate item: `id,code,kind,legalName,tradeName,isActive,visibleSourceAccounts`. Source account: `id,companyId,roles,isActive,version`. Candidate response'unda `totalCount`, `countScope` veya hedef hesap varmış gibi `companyAccounts` üretilmez. Görünen kaynak hesaplar yalnız aktörün yetkili şirketlerindendir.

Liste `includeCount=true` için `countScope="filtered-authorized-visible-parties"` döndürür; toplam yetkili, filtrelenmiş **tekil Party** sayısıdır, account sayısı değildir. Sayım istenmediğinde nullable alanlara sıfır anlamı yüklenmez.

JavaScript listede gelen int64 version'ı sayısal yazma tokenına dönüştürmez. Bu yolculuk create/link komutlarında zaten `expectedVersion` kullanmaz. Detail/mutation/candidate string tokenlarını `Number`, `parseInt`, aritmetik veya JSON round-trip ile sayıya çevirmeyin. Aktif/pasif güncelleme endpoint'i ayrı kapsamdır; mevcut `expectedVersion` int64 sözleşmesi ayrıca kayıpsız ele alınmalıdır.

`Testing` farklı bir version wire sözleşmesi değildir. Testte CLR `long` DTO'suna permissive JSON deserialization ile başarılı okuma yapılması, HTTP body'deki string'in number olduğu anlamına gelmez. Frontend doğrulaması ham JSON türünü ve OpenAPI'yi esas alır.

## 4. Filtreler ve cursor

Liste kabul ettiği query anahtarları:

```text
q, kind, role, roleMatch, partyState, accountState,
sort, direction, pageSize, cursor, includeCount
```

- `q`: trim + NFC, en fazla 200 Unicode rune; query değerini URL encode edin.
- `kind`: `LegalEntity` veya `NaturalPerson`.
- `role`: `Customer`, `Supplier`, `Tolling`, `Service`; çoklu seçim `role=Customer&role=Supplier`, virgüllü birleşim değil.
- `roleMatch`: `any` (varsayılan) / `all`.
- `partyState`: `active` (varsayılan) / `inactive` / `all`.
- `accountState`: `all` (varsayılan) / `active` / `inactive`.
- `sort`: `code` (varsayılan) / `legalName` / `updatedAt`; `direction`: `asc` (varsayılan) / `desc`.
- Liste `pageSize`: 1–200, varsayılan 50. `includeCount`: `true`/`false`, varsayılan false.
- Candidate yalnız `q,pageSize,cursor` kabul eder; `pageSize`: 1–100, varsayılan 25. Liste filtrelerini candidate endpoint'ine kopyalamayın.
- Bilinmeyen query anahtarları liste/candidate'da `400/validation_failed`; detail'de `companyId` query parametresi açıkça reddedilir.
- `nextCursor` opaque server tokenıdır; parçalamayın veya başka endpoint/aktör/şirket/filtreyle kullanmayın. `hasMore=false` son sayfadır. Query değişince eski cursor sıfırlanır.
- `invalid_cursor` sonrası eski React Query sayfasını başarı diye göstermeyin: cursor zincirini temizleyip aynı filtrelerle fresh cursorless GET yapın. Yeni başarılı yanıt gelmeden kurtarma tamamlandı denmez.

## 5. Çalıştırılabilir PowerShell istek şablonu

Bu örnek **sentetik Cari kaydı yazar**. Yalnız bu çalışma için hazırlanmış izole, izinli test API'sinde çalıştırılır. Production veritabanına veya başka geliştiricinin ortak fixture'ına karşı çalıştırılmaz. Yerel şirket kimlikleri ve kimlik doğrulama girdileri dışarıdan sağlanır; kaynak dosyada gerçek parola/token bulunmaz. Script test sonucu değildir; gerçek SQL testi §8'de ayrı kaydedilir.

Ön koşullar: API ayağa kalkmış, iki farklı yetkili şirket mevcut, aktör §2 yetkilerini taşıyor. Ortam değişkenleri `CARI_JOURNEY_API_BASE`, `CARI_JOURNEY_COMPANY_A`, `CARI_JOURNEY_COMPANY_B`; kimlik için ya `CARI_JOURNEY_ACCESS_TOKEN` ya da yalnız izole Development/Testing host'unda `CARI_JOURNEY_DEV_USER`. Dev kullanıcı test host'unda önceden tanımlanmış olmalıdır; bu script kullanıcı/grant üretmez.

```powershell
$journeyBase = [Uri]$env:CARI_JOURNEY_API_BASE
if (!$journeyBase.IsAbsoluteUri -or !$journeyBase.IsLoopback -or
    $journeyBase.Scheme -notin @('http', 'https') -or
    $journeyBase.UserInfo -or $journeyBase.Query -or $journeyBase.Fragment) {
    throw 'Yalnız açıkça seçilmiş loopback test API adresi kullanılabilir.'
}
$journeyCompanyA = [Guid]::Parse($env:CARI_JOURNEY_COMPANY_A)
$journeyCompanyB = [Guid]::Parse($env:CARI_JOURNEY_COMPANY_B)
if ($journeyCompanyA -eq [Guid]::Empty -or
    $journeyCompanyB -eq [Guid]::Empty -or
    $journeyCompanyA -eq $journeyCompanyB) { throw 'İki farklı şirket gerekir.' }
$journeyAuth = @{}
if ($env:CARI_JOURNEY_ACCESS_TOKEN -and $env:CARI_JOURNEY_DEV_USER) {
    throw 'Tek kimlik doğrulama yöntemi seçin.'
} elseif ($env:CARI_JOURNEY_ACCESS_TOKEN) {
    $journeyAuth.Authorization = 'Bearer ' + $env:CARI_JOURNEY_ACCESS_TOKEN
} elseif ($env:CARI_JOURNEY_DEV_USER) {
    $journeyAuth['X-Development-User'] = $env:CARI_JOURNEY_DEV_USER
} else { throw 'İzinli test kimliği sağlanmadı.' }

function Invoke-CariJourneyRequest {
    param([string]$Method, [string]$Path, [Guid]$Company,
          [string]$Body, [string]$AttemptKey)
    $requestHeaders = $journeyAuth.Clone()
    $requestHeaders['X-Company-Id'] = $Company.ToString()
    $requestHeaders['X-Correlation-Id'] = [Guid]::NewGuid().ToString('N')
    if ($AttemptKey) { $requestHeaders['Idempotency-Key'] = $AttemptKey }
    $requestArgs = @{
        Uri = [Uri]::new($journeyBase, $Path)
        Method = $Method
        Headers = $requestHeaders
        UseBasicParsing = $true
        MaximumRedirection = 0
        ErrorAction = 'Stop'
    }
    if ($Body) {
        $requestArgs.ContentType = 'application/json; charset=utf-8'
        $requestArgs.Body = [Text.Encoding]::UTF8.GetBytes($Body)
    }
    $http = Invoke-WebRequest @requestArgs
    [pscustomobject]@{
        Status = [int]$http.StatusCode
        Headers = $http.Headers
        Data = ($http.Content | ConvertFrom-Json)
    }
}

$createAttempt = [Guid]::NewGuid().ToString('N')
$createBody = @{
    kind = 1
    legalName = 'Örnek Cari Yolculuk ' + $createAttempt.Substring(0, 8)
    tradeName = $null
    roles = 3
} | ConvertTo-Json -Compress
$created = Invoke-CariJourneyRequest -Method POST -Path '/api/v1/counterparties/' `
    -Company $journeyCompanyA -Body $createBody -AttemptKey $createAttempt
if ($created.Status -ne 201) { throw 'Create 201 bekleniyordu.' }
$partyId = [Guid]::Parse($created.Data.id)
$partyQuery = [Uri]::EscapeDataString($created.Data.code)
$listPath = '/api/v1/counterparties/?q=' + $partyQuery + '&includeCount=true'
$detailPath = '/api/v1/counterparties/' + $partyId
$candidatePath = '/api/v1/counterparties/company-account-link-candidates?q=' + $partyQuery
$listA = Invoke-CariJourneyRequest GET $listPath $journeyCompanyA
$detailA = Invoke-CariJourneyRequest GET $detailPath $journeyCompanyA
$candidatesB = Invoke-CariJourneyRequest GET $candidatePath $journeyCompanyB
if (@($candidatesB.Data.items | Where-Object { $_.id -eq $partyId.ToString() }).Count -ne 1) {
    throw 'Fresh B aday sayfasında aynı Party görülmedi; link gönderilmeyecek.'
}
$linkAttempt = [Guid]::NewGuid().ToString('N')
$linkBody = '{"roles":2}'
$linked = Invoke-CariJourneyRequest -Method POST -Path ($detailPath + '/company-account') `
    -Company $journeyCompanyB -Body $linkBody -AttemptKey $linkAttempt
if ($linked.Status -ne 201) { throw 'Link 201 bekleniyordu.' }
$listB = Invoke-CariJourneyRequest GET $listPath $journeyCompanyB
$detailB = Invoke-CariJourneyRequest GET $detailPath $journeyCompanyB
$candidatesAfterLink = Invoke-CariJourneyRequest GET $candidatePath $journeyCompanyB
```

Değişkenlerdeki request/response'lar yerel inceleme içindir; auth header'larını veya kişisel veriyi log/PR'a dökmeyin. Host'ta varsayılan bootstrap header adı değiştirilmişse test profiline göre uyarlayın; production'a dev header ekleyerek çözmeye çalışmayın. Gerçek frontend ortak transport/CompanyContext fence'i kullanır; bu yardımcı fonksiyonu ikinci production API istemcisi olarak kopyalamaz.

## 6. Idempotency, belirsiz sonuç ve hata gösterimi

- Her yeni mantıksal create/link için ayrı `Idempotency-Key`; tek başlık, trim sonrası 1–128 karakter, virgül yok. Create ve link aynı işlem değildir.
- Başarı/replay örneği: ilk POST `201`; aynı aktör, şirket, endpoint ve aynı gövdeyle **aynı key** tekrar `201`, `Idempotency-Replayed: true`, `Idempotency-Original-Correlation-Id: <ilk correlation>` döner. Yeni Party/account/audit etkisi oluşmaz.
- Ağ kopması/timeout/5xx/malformed başarı, “kayıt kesin oluşmadı” değildir. Submit snapshot'ı ve aynı key korunur; güvenli retry aynı gövde, aktör ve şirketle yapılır. Tüm örnek scripti yeniden çalıştırmak yeni key ve yeni create üretir; retry değildir.
- Örnekte create sonucu belirsizse yalnız `$created = Invoke-CariJourneyRequest ... -Body $createBody -AttemptKey $createAttempt` satırı aynı context'te yeniden çalıştırılır. Link için de `$linkBody/$linkAttempt` korunur. Otomatik yeni key ile kör retry yoktur.
- Context/aktör değişiminde eski isteğin geç yanıtı yeni forma/şirkete uygulanmaz. Belirsiz yazma eski context'e ait kalır; yeni şirkette tekrar gönderilmez. Ayrılan UI belleği temizlenirken eski işlemin başarıyla iptal edildiği iddia edilmez.
- Kullanıcı gövdeyi değiştirirse bu artık yeni logical attempt'tir; önce belirsiz önceki attempt uzlaştırılır. Yeni key üretmek mükerrerliği çözmez.
- Candidate seçimi yazma yetkisi değildir. POST güncel yetki/Party görünürlüğü/global aktiflik/target ilişkiyi yeniden doğrular. Replay de geçerli endpoint yetkisini atlamaz.

| HTTP / `code` | Frontend davranışı |
|---|---|
| `400 validation_failed` | Form/query düzelt; localized mesajı kod diye parse etme |
| `400 command_metadata_invalid` | Aynı submit'i yeni key ile döngüye sokma; transport metadata hatasını düzelt |
| `400 company_context_invalid` / `correlation_metadata_invalid` | Platform hatası; context/metadata katmanına ilet |
| `400 invalid_cursor` | Filtreyi koru, cursor zincirini at, fresh ilk sayfa oku |
| `401 authentication_required` | Oturum sonlandırma ve business cache temizliği |
| `403 forbidden` | Yetkisiz güvenli durum; Party/şirket envanteri gösterme |
| `404 not_found` | Olmayan/gizli/yabancı/global-pasif ayrımı sızdırma; aday seçimini uzlaştır |
| `409 already_linked` | Başarı taklidi yapma; fresh yetkili detail/list/candidate al |
| `409 idempotency_conflict` | Key aktör/gövde uyuşmazlığı; kör resend veya sessiz key değişimi yok |
| `500 internal_error` / `503 service_unavailable` / boş veya malformed body | Güvenli hata; write için outcome unknown, read için retry |

`version_conflict` yalnız ilgili versioned update akışına aittir; create/link için beklenen conflict kodu değildir. ProblemDetails `code` yok/bozuksa frontend `title/detail` içeriğinden business kodu tahmin etmez. Raw response/HTML render edilmez.

## 7. Frontend tamamlanma kontrolü

1. Güncel backend SHA ve yukarıdaki OpenAPI SHA ile tüketilen contract eşleştirilir. Generator varsa üretilen dosya elle düzenlenmez; mevcut kabul edilmiş ince client/decoder düzeni kullanılıyorsa sapma üretmeden aynı alanlar doğrulanır. PR #156'nın Orval NO-GO çalışması generator adoption onayı sayılmaz.
2. Create formu yalnız mevcut dört alanı sunar; company shell'den salt okunurdur. Başarıdan sonra server ID/kod kullanılır.
3. Liste/detail/candidate/mutation için ayrı runtime decoder; seçili company, UUID, bilinen rol bitleri, exact account binding ve string version kontrolü vardır. Yabancı account içeren cevabın bir kısmı render edilmez.
4. Şirket/aktör/session/context generation, query ve cursor cache anahtarlarıyla bağlanır. Gecikmiş eski yanıt title, breadcrumb, toast, selection veya yeni company cache'ine yazamaz.
5. B workspace'i fresh target-scoped candidate GET'inden seçim yapar; query/navigation state Party ID'si yalnız hint'tir.
6. Link sonrası B list/detail/candidates yeniden alınır; A'daki başka hesap rolü/durumu B ekranına kopyalanmaz. Müşteri/tedarikçi rolleri için ikinci Party yaratılmaz.
7. Loading/empty/validation/forbidden/not-found/conflict/offline/outcome-unknown/stale-context durumları; çift submit engeli; klavye/focus ve tek anlamlı duyuru test edilir.
8. Gerçek browser zinciri ve normal provider aktivasyonu ayrı kanıt olmadan “ekran teslim edildi”, “capability açıldı”, “AT-009…014 tamamen geçti” denmez. Bu dosya tüm Cari veya ERP tamamlanma kaydı değildir.

## 8. Doğrulama ve yeniden çalıştırma

- Yeni test: `tests/GoymenERP.Api.IntegrationTests/CounterpartyJourneySqlServerRelationalAcceptanceTests.cs`.
- Hedef test komutu (repo kökünde):

```powershell
py -3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py
if ($LASTEXITCODE -ne 0) { throw 'Bootstrap doğrulanmadı.' }
if (!$env:GOYMEN_ERP_TEST_SQLSERVER_CONNECTION) { throw 'İzole SQL test bağlantısı gerekli; skip kabul edilmez.' }
dotnet test tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj -noAutoResponse --configuration Release --filter "FullyQualifiedName~CounterpartyJourneySqlServerRelationalAcceptanceTests" --logger "trx;LogFileName=cari-journey.trx" --results-directory artifacts/cari-journey
if ($LASTEXITCODE -ne 0) { throw 'Journey testi başarısız.' }
```

- 5 Eylül 2026 ilk yerel SQL Server 2022 Developer koşumu: **1 test / 1 başarılı / 0 başarısız / 0 skip**. HTTP handler'ları TestServer üzerinden, kalıcılık gerçek SQL Server üzerinde çalıştı. `DevelopmentBootstrap` test kimliği kullanıldı; normal OIDC/browser koşumu değildir.
- İzole test DB'si `SqlServerRelationalApiFactory` tarafından rastgele `goymen_erp_ci_<guid>` adıyla oluşturulur. Test `finally` bloğu yalnız bu ada uyan DB'yi temizler; gerçek işletme verisi kullanılmaz. Bağlantı string'i repository'ye yazılmaz. İlk koşum TRX: `artifacts/cari-journey/cari-journey.trx` (yerel, Git dışı).
- Test, gerçek yanıtları xUnit çıktısına yazar; TRX içindeki `StdOut` frontend'in response örneklerini yeniden üretmesini sağlar. Auth header veya bağlantı bilgisi yazılmaz. SQL sayımları: **1 Party, 2 account, 1 create audit + 1 link audit, 1 create execution + 1 link execution**. Okuma ve aynı-key retry yeni etki üretmez.
- Yeni test broad gerçek-SQL CI envanterine zorunlu eklendi: önceki 57 keşif/64 çalışma korunarak **58 keşif/65 çalışma**. Envanter regresyonu yeni test kaydı yokken kırmızı, kayıt eklendikten sonra **7/7 yeşil**; bu 7 Python testi kendi başına SQL çalıştırmaz. Mevcut skip=0 ve exact inventory kapıları gevşetilmedi.
- Fresh exact-head CI ve bağımsız review sonuçlarının güncel kaynağı bu branch'in PR checks/review kayıtlarıdır. Bu belge bir sonraki commit'in testlerini geçmiş saymaz; ilk yerel koşum, nihai release onayı değildir.
- **Frontend browser / kullanıcı kabulü / production aktivasyonu: BU DEVİRDE YOK.**

### 8.1 Gerçek SQL koşumundan response örnekleri

İlk başarılı koşumda dönen sentetik değerler aşağıdadır. UUID'ler yeni koşumda değişir; bunları uygulamaya sabitlemeyin. Her zaman kendi POST'unuzun `id/code` değerlerini kullanın. Bu örnekte B rolü `10` (Supplier + Service); §5 şablonundaki yalnız Supplier (`2`) seçiminden bilerek farklı geçerli bir rol birleşimidir.

A'da create ve A'da detail aynı kaynak alanlarını döndürdü:

```json
{"id":"04ec8a58-1bb1-4852-b275-3963d1cc1375","code":"CAR-000001","kind":1,"legalName":"Yolculuk Test Metal A.Ş.","tradeName":"Yolculuk Metal","isActive":true,"version":"1","companyAccounts":[{"id":"b37fcbac-235a-444f-abc6-1ec08568802f","companyId":"10000000-0000-0000-0000-000000000001","roles":3,"isActive":true,"version":"1"}]}
```

A'da kodla arama, sayısal version kullanan paged list sonucudur:

```json
{"items":[{"id":"04ec8a58-1bb1-4852-b275-3963d1cc1375","code":"CAR-000001","kind":1,"legalName":"Yolculuk Test Metal A.Ş.","tradeName":"Yolculuk Metal","isActive":true,"version":1,"companyAccounts":[{"id":"b37fcbac-235a-444f-abc6-1ec08568802f","companyId":"10000000-0000-0000-0000-000000000001","roles":3,"isActive":true,"version":1}]}],"nextCursor":null,"hasMore":false,"totalCount":1,"countScope":"filtered-authorized-visible-parties"}
```

B'ye link sonrası `201` ve B detail `200`, aynı Party fakat yalnız B hesabını taşıdı:

```json
{"id":"04ec8a58-1bb1-4852-b275-3963d1cc1375","code":"CAR-000001","kind":1,"legalName":"Yolculuk Test Metal A.Ş.","tradeName":"Yolculuk Metal","isActive":true,"version":"1","companyAccounts":[{"id":"9a721d85-2f01-4efc-8d39-c386734eb77c","companyId":"10000000-0000-0000-0000-000000000002","roles":10,"isActive":true,"version":"1"}]}
```

Candidate yanıtı bağlama öncesinde `visibleSourceAccounts` içinde A hesabını taşıdı; bağlama sonrasında `{"items":[],"nextCursor":null,"hasMore":false}` döndü. Aynı key/body ile link retry `201` ve aynı JSON'u, `Idempotency-Replayed: true` ile ilk correlation başlığını döndürdü. Headerless yetkili grup listesinde ise **bir Party / iki account / totalCount=1** görüldü.

Uygulama kaynakları: `CounterpartyEndpoints.cs`, `CounterpartyPageQueryParser.cs`, `CounterpartyLinkCandidateQueryParser.cs`, API `Contracts/CounterpartyMutationView.cs`, Counterparty application `Contracts/ICounterpartyCommandService.cs`, `CounterpartyModels.cs`, `CounterpartyReadPageModels.cs`, `CounterpartyLinkCandidateModels.cs`; persistence `CounterpartyReadService.cs`, `CounterpartyLinkCandidateReadService.cs`, `CounterpartyCompanyAccountLinkService.cs`; shell `CounterpartyShellCapabilityProvider.cs`; `ErpAuthenticationExtensions.cs`, `DevelopmentAuthenticationHandler.cs`, `CorrelationContextMiddleware.cs`; `contracts/openapi/goymen-erp-v1.json`.

Mevcut kabul bağları: `C02-AC-001/002/006/007/010`, `CLINK-AC-001/004/007/011/013/015/019` ve `DEC-CAR-001/002/003`. Birleşik test kapsamı hangi alt adımları gerçekten kanıtlıyorsa yalnız onlar raporlanır; bu kimliklerin tamamı otomatik tamamlandı sayılmaz.
