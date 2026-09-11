# Cari Opsiyonel Ticari Rol Uzlaştırma Şartnamesi v1

- Tarih: 11 Eylül 2026
- Şerit: `CHATGPT-BUSINESS`
- Durum: **REVIEW EDİLEBİLİR İŞ ŞARTNAMESİ / ERP KABULÜ DEĞİL**
- Dosya: `docs/features/CARI_ROLE_OPTIONAL_RECONCILIATION_SPEC_v1.md`
- Mevcut PR: **#232**; yeni belge veya paralel şartname değildir.
- Hazırlık tabanı: `main@060da63f2644cb271d78d9eab8b7a6985d544ad1`
- Önceki belge revizyonları: `1299a6ee9f817c552ee08bbe385d4cb12c11bb8e` → `4e063e26f2c5848f7330891016d99a82088efa7b`. Bu güncelleme mevcut içeriğin devamıdır.
- Öncelikli transport kaynağı: [#233 / 5626451657](https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5626451657), backend `080e7a1d24f74975b45546c5277f0529c13f06d1`.
- Koşullu hata başlığı düzeltmesi: [#230 / 5630622096](https://github.com/sinangym/GoymenERP/pull/230#issuecomment-5630622096), backend `880cae838e5d2f01a988f1e4a89afbe67858f48c`.

Bu belge uygulama kodu yazmaz, kanonik karar/task durumunu değiştirmez, `docs/planning/2026-09-10/**` veya paylaşılan ilerleme belgelerinin tek-yazar sahipliğini devralmaz. PR #229 karar paketi ile #232'nin uygulanabilir insan inceleme/kabul kapıları korunur. Kaynak göstermek, bu kapıları geçmek veya production yetkisi vermek değildir. Otomatik şirket ilişkisi için `B` yalnız öneridir; kullanıcı kararı değildir.

## 1. Kaynak önceliği ve güncel depo gerçeği

Öncelik: `AGENTS.md` ve uygulanabilir governance altında en yeni açık kullanıcı kararı → tarihli kullanıcı kararları → `KARAR_SICILI` → Master Rapor → ilgili feature/ADR → eski referanslar. Açık yetki, güvenlik, migration veya production politikası tahminle kapatılmaz.

11 Eylül 2026 kaynak kesiti:

- #227 merge edilmiştir; main `060da63f2644cb271d78d9eab8b7a6985d544ad1`. Mevcut create/list/detail/company-link API yolculuğunun gerçek-SQL baseline'ıdır; yeni roleless frontend/transport veya production kabulü değildir.
- #228 `d46f7f819456fc578a25cdcabf5c98ad6f634456`, açık/draft donor kaynağıdır. Create/link/router/attempt ve kontrollü Chromium emeği yeniden yazılmaz. Recipient `5626181170` makbuzu mevcuttur; gerçek donor `HANDOFF-RELEASED` yerine geçmez.
- #229 `e1fd69da69df36a72e559ea07163fb3f359b3c61`, insan incelemesi gereken açık/draft karar paketidir.
- #230 `080e7a1d24f74975b45546c5277f0529c13f06d1`, main uzlaştırması ve yeni transport gate'in önceki kanıt başıdır: 7 kontrol, SQL75/RCSI10, sıfır skip. James'in bu SHA'daki conditional error-header P2'si tarihsel olarak koşulludur; geriye dönük koşulsuz onaya çevrilmez.
- #230 güncel `880cae838e5d2f01a988f1e4a89afbe67858f48c`, **beş dosyalık OpenAPI/handoff/test düzeltmesidir**; yeni protokol sürümü veya runtime filter değişikliği değildir. James ve Ampere bu exact SHA'nın dar farkında P0/P1/P2=0 bildirmiştir. Bu tur yeni CI ve gerçek SQL günlüğü ayrıca okunmuştur: SQL75/RCSI10, sıfır skip. Kanıt eski SHA'dan kopyalanmamıştır; ayrıntı §9.1.
- Aktif frontend #233 `424a586fc07efa291db4b37866ba5cce54fb7443`, main tabanlı 15 frontend yoludur. Node24 component suite267/267 ve C-01 browser10/10 kendi kapsamlarında raporludur. **Bu head yeni transport/readiness protokolünü veya donor create/link birleşimini henüz uygulamaz.**
- #231 `173b003` tarihsel kaynaktır. Yeni frontend görevleri #233'te sürer; ikinci create/link veya ikinci şartname açılmaz.

## 2. Kilitli kullanıcı sonucu, kapsam ve değişmezler

Aynı hukuki karşı taraf tek `Party` kimliğidir. Cari oluştururken veya mevcut Party'yi yetkili olunan şirkete bağlarken önceden sabit Müşteri/Tedarikçi/Fason/Hizmet sınıflandırması seçmek zorunlu değildir.

Ticari yön gerçek iş işleminden gelir. Legacy ticari sınıflandırma kullanıcı yetkisi, şirket erişimi, alış/satış ön şartı veya aktiflik durumu değildir; ayrı Party üretmez. `Party.Kind`, değişmez Cari kodu, `CompanyPartyAccount`, şirket izolasyonu, Identity permission/rolleri, aktif/pasiflik, audit, idempotency ve concurrency korunur. Rol yokluğu mal sahibini değiştirmez.

Mevcut dilim: roleless create/link, dört okuyucu, istemci-sunucu uyumluluğu, güvenli retry ve bunların kabulü. Sipariş implementasyonu, VKN/TCKN mükerrer motoru, otomatik şirket ilişkisi implementasyonu, yeni permission, toplu legacy veri silme, saklama politikası veya production aktivasyonu kapsam dışıdır. Para/miktar/formül/yuvarlama politikası bu dilimde yoktur.

İş anlamı: `Party` ortak hukuki kimlik; `CompanyPartyAccount` tek şirketin bu tarafla ilişkisi; `Roles` opsiyonel eski ticari işaretler; `IsActive` ayrı yaşam döngüsü; `Version/RowVersion` ayrı eşzamanlılık kontrolüdür. Fiziksel şema/API sahibi Codex, ekran sahibi Claude'dur.

## 3. Payload wire sözleşmesi

### 3.1 İstek

`POST /api/v1/counterparties`: `kind` ve `legalName` zorunlu; `tradeName` ve `roles` opsiyoneldir. Mevcut Firma/Kişi, trim ve alan uzunluğu kuralları korunur.

`POST /api/v1/counterparties/{partyId}/company-account`: geçerli JSON object `{}` roleless link isteğidir. HTTP body'nin tamamen yokluğu `{}` değildir.

- Eksik `roles` veya sayısal tamsayı `0` → aynı `PartyRole.None` ve aynı normalize logical payload.
- Sayısal tamsayı `1..15` → mevcut legacy flag birleşimleri; **15 geçerlidir**.
- `null`, string, kesirli/uygunsuz sayısal temsil, negatif, `16` gibi tanımsız bit ve malformed JSON geçersizdir. Client veya server geçersizi otomatik sıfıra çeviremez.

### 3.2 Yanıt

List, detail, mutation ve link-candidate account projection'larında `roles` **zorunlu sayısal tamsayı 0..15**'tir. Request opsiyonelliği response'a taşınmaz.

Geçerli: `0`, `1..15` ve özellikle `15`. Geçersiz: eksik alan, `null`, string, kesirli, negatif, `16` veya tanımsız bit. Null/missing response'u `0` kabul eden fallback yoktur. Şirket, kimlik ve version kontrolleri gevşetilmez.

Liste version'ı mevcut numeric int64; detail/mutation/candidate account version'ı mevcut pozitif decimal string olarak kalır. Create/link için yeni `expectedVersion` üretilmez; active-state command'inin mevcut expectedVersion, gerekçe ve yetki sınırı korunur. Liste sayısından güvenilmez write version'ı türetilmez.

### 3.3 GET değişmezi

Liste/detail/candidate/filter/count/pagination Party veya account oluşturmaz, aktifleştirmez, business audit veya durable command etkisi üretmez. Filtresiz sorgu `role` ve gereksiz `roleMatch` göndermez. HTTP `role=0`, rol-yok kayıt seçme sözleşmesi değildir; mevcut named legacy filtreler görünür ve temizlenebilir kalır.

## 4. İstemci-sunucu uyumluluk sözleşmesi

**Yalnız `roles=0` okuyabilmek yeterli değildir.** Payload doğruluğu, uyumlu sunucuyla konuşulduğunu veya bir komutun gerçekten işlendiğini kanıtlamaz.

### 4.1 Declaration ve sunucu cevabı

Yeni uyumlu istemci her desteklenen Cari exchange'de, GET dahil, tam bir adet şu başlığı gönderir:

```text
X-ERP-Counterparty-Contract: optional-trade-roles-v1
```

Sunucu teyidi de aynı başlık/değerdir; API'nin ürettiği değerdir, keyfi istemci değerinin yansıması değildir.

Tüm Cari commands, active-state ve durable replay dahil, desteklenen declaration gerektirir:

| Command isteği | Beklenen sonuç | Oluşmaması gereken etki |
|---|---|---|
| Başlık yok | 409 `client_contract_required` | Handler çalışması, business/audit/idempotency kaydı, attempt key tüketimi |
| Boş, duplicate, virgülle birleşmiş veya unsupported declaration | 409 `client_contract_unsupported` | Aynı sıfır etki |
| Tek doğru declaration | Mevcut authorization/validation/command akışı devam eder | Otomatik permission, şirket erişimi, capability veya transaction başarısı |

Bu başlık authentication, entitlement veya feature flag değildir. Private/no-store response ve mevcut CORS origin allowlist korunur. Başlığı okumak için allowlist genişletilmez. İstemcinin taze GET hazırlığı, sunucu tarafındaki yetki ve iş kuralı denetimlerinin yerine geçmez.

### 4.2 Yazma öncesi taze başarılı GET

Herhangi bir Cari yazması açılmadan önce istemci aynı güncel bağlamda yeni, başarılı bir Cari GET alışverişiyle uyumu doğrular. Hazırlık koşulları birlikte sağlanır:

```text
başarılı HTTP GET
+ tam ve decoder-valid response body
+ exact X-ERP-Counterparty-Contract: optional-trade-roles-v1 teyidi
+ aynı API origin
+ aynı kullanıcı/oturum
+ aynı seçili CompanyId
+ aynı generation/fence
```

Eski cache verisi, önceki oturumdan boolean, yalnız `roles=0` görülmesi, hata cevabı veya command cevabı bu hazırlığın yerine geçmez. Sayısal freshness TTL'si burada seçilmez. Gerekli bağlam yenilendiğinde taze GET yeniden yapılır.

Eski server declaration'ı yok sayıp 200 ve geçerli gövde dönebilir, fakat teyit vermeyebilir. **Form/yazma kapalı, POST/PUT sayısı 0** kalmalıdır. Uyumu denemek için POST yapılmaz; `Customer=1` veya `15` uydurularak dolaşılmaz.

Taze GET doğru teyit taşısa bile hata veya geçersiz/yanlış şirkete ait gövdeyse hazır oluş kurulmaz. Başarılı GET'te eksik/uyumsuz/çelişkili teyit de uyumun kanıtlanamadığı anlamına gelir; yalnız başlığa bakarak sunucu sürümü teşhisi yapılmaz.

Company/session/actor/API origin/generation değişikliği, logout ve mevcut fence olayları hazırlığı iptal eder. Eski response yeni bağlamı hazır yapamaz. A şirketindeki teyit B'ye link yazmasını açmaz; B bağlamında yetkili başarılı GET gerekir. B hesabı henüz yoksa buna uygun mevcut list/candidate okuması kullanılabilir; GET yine hesap açmaz.

### 4.3 Legacy GET farklıdır

Yeni istemcinin her GET'te declaration göndermesi, backend'in tüm headerless GET'leri koşulsuz reddettiği anlamına gelmez:

- Declaration'sız legacy GET yalnız yetkili projection'da roller1..15 ise mevcut başarıyı döndürebilir.
- Aynı yetkili projection'da herhangi bir görünür account `roles=0` içeriyorsa incompatible success body yerine 409 `client_contract_required` döner.
- Unsupported GET declaration yalnız başarılı yetkili projection'ı 409 `client_contract_unsupported` yapar; mevcut authorization/validation/not-found/availability hatasını maskelemez.
- Örneğin unsupported declaration'lı yetkisiz liste 403 olarak kalır; teyit ve account kimliği sızmaz. Başka şirketin gizli zero hesabı görünürlüğe veya compatibility kararına katılmaz.

### 4.4 P2: hata başlığı koşulludur

`880cae8` OpenAPI'de bu başlığı **opsiyonel response metadata** olarak belgelemiştir. Her hata/başarı cevabında varlığı garanti edilmez. Buna rağmen yeni istemcinin command hazırlığı için §4.2'deki başarılı GET + teyit birlikte zorunludur; bunlar çelişmez.

Mevcut filter doğru declaration'ı kabul ettikten sonra, handler'dan önce API-owned teyidi kurar. Bazı handler hataları bu başlığı taşır. Authentication, company resolution, body binding ve diğer filter öncesi hatalar taşımayabilir. Unsupported declaration'a compatibility reddi teyit vermez.

Aşağıdaki gerçek HTTP contract örnekleri P2 tesliminde aynı doğru request declaration ile ayrılmıştır:

| Yol | HTTP | Teyit | İstemci hükmü |
|---|---|---|---|
| Yetkili GET, geçersiz `role=0` query | 400 handler validation | Var | Hata; hazır oluş ve business başarı yok |
| Cari read permission'ı olmayan company viewer, handler/read-service reddi | 403 | Var | Yetki hatası; hazır oluş yok |
| Kimlik doğrulanmamış GET | 401 filter öncesi | Yok | Oturum/auth hatası; başlık yokluğu eski server kanıtı değil |
| Yetkili POST, malformed JSON | 400 body binding | Yok | Mevcut hata/attempt kuralları korunur; eski server teşhisi yapılmaz |

**Hata + doğru teyit ne transaction makbuzu ne command başarısı ne readiness'tir. Hata + teyit yokluğu tek başına eski server kanıtı değildir. Başarısız GET ve command cevapları readiness kurmaz.**

P2 yeni protokol sürümü değildir. Beş dosya: `ErpOpenApiContract.cs`, canonical OpenAPI, backend handoff, `OpenApiContractGenerationTests.cs`, `CounterpartyClientContractTests.cs`. Runtime filter/CORS/permission/SQL/retry/version davranışı değiştirilmemiştir. Final SHA'nın yeni kanıtı §9.1'de ayrı tutulur.

### 4.5 Belirsiz command sonucu

Command response'unda eksik/uyumsuz acknowledgment, malformed başarı gövdesi veya ağ/timeout gibi belirsizlik başarıya çevrilmez. Mevcut unknown-outcome davranışı korunur; körce yeni logical attempt açılmaz.

Korunan işlem: command/path, actor, CompanyContext, API hedefi, Party/resource bağı, normalize payload, **aynı Idempotency-Key** ve mevcut correlation/attempt izi. Aynı key'le başka şirket/Party/payload'a taşınmaz.

Daha sonra compatibility409 alınması daha önce belirsiz kalan girişimin hiç işlenmediğini kanıtlamaz. Önceki attempt silinmez; otomatik yeni key veya otomatik blind retry yoktur. Yeni işlem olduğu kesin olan ilk compatibility reddinin sıfır etkisiyle, daha eski belirsiz bir girişimin sonucu karıştırılmaz.

Güncel yetki/bağlam tekrar doğrulandıktan ve o bağlamda taze başarılı GET + exact teyit alındıktan sonra, yalnız açık same-attempt retry aynı key ve normalize payload ile yapılabilir. Kullanıcı/oturum/şirket değişimi otomatik recovery veya otomatik retry değildir; önceki girişim yeni bağlama taşınmaz. Eski yanıt yeni ekrana success/navigation yaptıramaz.

Create ile link ayrı logical attempts'tir; farklı key kullanır. Aynı command/context/key içinde missing roles ile numeric0 eşdeğerdir. HTTP hata statüleri korunur; başlık bir iş işleminin commit olup olmadığının tek başına makbuzu sayılamaz.

### 4.6 Mixed-node sınırı

Protokol eski backend binary'sini sonradan uyumlu hale getirmez. Teyit veren node'a GET yapılması, sonraki yazmanın aynı uyumlu node'a gideceğini kendiliğinden kanıtlamaz. Karma node ortamı için koordinatörün somut routing/affinity/drain checkpoint'i gerekir; var olan bir mekanizma varsayılmaz.

Sonraki command teyitsiz kalırsa §4.5 uygulanır. Routing/production politikası bu şartnameyle seçilmez. Normal capability kapısı ve production aktivasyonu ayrı kalır.

## 5. SQL, işlem durumları ve geri alma sınırı

`CompanyPartyAccount.Roles` NOT NULL integer kalır; constraint1..15 →0..15 genişler. Uygulanmış eski migration veya legacy kimlik/rol/audit/durable response/fingerprint rewrite edilmez.

İzinli: aktif veya mevcut pasif hesabın roles0 taşıması; farklı şirket hesaplarında farklı legacy işaretler. Yasak: rol yok diye reaktivasyon, grant, sahiplik devri veya ikinci Party oluşturulması. Pasif hesabı aktifleştirme ayrı mevcut yetkili/version'lı command'dir.

Create atomik başarıda bir Party, bir seçili şirket hesabı, bir ilgili created audit ve bir durable result üretir. Link aynı Party için bir target account, bir ilgili created audit ve kendi durable result'unu üretir; kaynak hesap değişmez. Bu normal başarı örnekleri sistem genelindeki tüm audit/log kayıt sayısı iddiası değildir.

Same-key replay ikinci business effect üretmez. Farklı key'li aynı `(CompanyId, PartyId)` link yarışında tek account ve tek başarılı created audit kalır. Kaybeden command'in durable conflict sonucu saklanabilir; iki ayrı attempt olduğu için toplam CommandExecution sayısını körce1 şart koşmayın.

Audit veya idempotency persistence hatası business transaction'ı geri alır; yarım Party/account kalmaz. SQL sequence boşluğu business kaydı sayılmaz; kesintisiz kod sırası garantisi üretilmez. Formdan Vazgeç, sunucunun işlemiş olabileceği command'in rollback'i değildir.

Zero account yokken eski constraint'e güvenli Down yolu testlidir. Zero account varsa Down DDL öncesi SQL51000 ile reddeder. Hesap silme veya sahte Customer/15 atama yoktur; expanded schema ve uyumlu reader korunur, yeni yazma durdurulur, forward-fix yolu izlenir. Bu dilimde finansal iade/ters stok hareketi veya genel silme komutu yoktur.

## 6. Çok şirketli Cari görünümü

Hukuki Party tek; şirket ilişkileri ayrı `CompanyPartyAccount`'lardır. Grup görünümünde tek hukuki başlık altında şirket hesapları açıkça ayrılır. Her account'un CompanyId'si, aktifliği ve legacy sınıflandırması kendi hesabına aittir. Ortak Party görünürlüğü yetkisiz şirket bilgilerini açmaz.

Gelecekte mali tutarlar yalnız yetkili gerçek kaynakla ve şirket + para birimi boyutunda gösterilir; ayrı şirket/döviz bakiyeleri sessizce toplanmaz veya mahsuplaştırılmaz. Bugün olmayan bakiye servisi için sıfır/sahte metrik üretilmez.

Alt satır, açılır account bölümü veya şirket rozeti gibi sunum tercihi geri alınabilir UX kararıdır. Hiçbiri hesap ayrımını kaldıramaz. Bu tercih tüm tek-şirket C-01/C-02/C-03/link akışına veya protocol düzeltmesine blokaj yapılmaz. Her yazma yine tek açık CompanyContext'tedir; grup okuması tek başına write readiness değildir.

## 7. Kabul kimliği uzlaştırması

Paket kaynağı: [#229 exact ilk dilim](https://github.com/sinangym/GoymenERP/blob/e1fd69da69df36a72e559ea07163fb3f359b3c61/docs/planning/2026-09-10/ILK_DILIM_CARI_ROL_UZLASTIRMA.md). CR kimlikleri değiştirilmez; bu belgedeki CRO kimlikleri kanonik AT/DEC kaydı değildir.

| Paket-local | Bu belge | Değişmeyen anlam |
|---|---|---|
| CR-01 | CRO-AC-001 | Roleless create |
| CR-02 | CRO-AC-002 | Migration/legacy koruma |
| CR-03 | CRO-AC-003 | Roleless company link |
| CR-04 | CRO-AC-004 | Şirket/yetki/non-disclosure |
| CR-05 | CRO-AC-005 | Same-key retry/conflict |
| CR-06 | CRO-AC-006 | Different-key race/rollback |
| CR-07 | CRO-AC-007 | Durable legacy/roleless replay |
| CR-08 | CRO-AC-008 | Read/filter/candidate ve GET saflığı |
| CR-09 | CRO-AC-009 | Passive/version/permission |
| CR-10 | CRO-AC-010 | Mixed client/rollout |
| — | CRO-AC-011 | Geçersiz wire/transport |
| — | CRO-AC-012 | Başarı/detail/list dönüşü |

İlk sohbet taslağının CR'leri paket CR'leri değildir: sohbet CR-01→CRO-AC-001; sohbet CR-02/CR-06/CR-09→CRO-AC-008; sohbet CR-03→CRO-AC-002 kısmi; sohbet CR-04→CRO-AC-003; sohbet CR-05→CRO-AC-004 kısmi; sohbet CR-07→CRO-AC-006 kısmi; sohbet CR-08→CRO-AC-005. Paket CR-07/CR-09/CR-10'un tamamı bu eski sohbet karşılıklarıyla kanıtlanmış sayılmaz.

Sohbet CR-10'daki gelecekte alış+satış siparişinde aynı Cari kullanımı `FUT-CARI-ORDER-001 / FUTURE / NOT_IMPLEMENTED / NOT_RUN` olarak ayrıdır. Yeni transport alt senaryoları aşağıdaki aynı CRO maddelerine bağlanır; yeni kimlik sistemi/görev sistemi açılmaz.

## 8. Kanıt sınıfları

- **REAL_SQL:** gerçek SQL Server + backend/HTTP persistence kabulü. JS readiness veya gerçek tarayıcı değildir.
- **HTTP_CONTRACT / OPENAPI:** HTTP hata/header yolları ve şema/drift; SQL olarak sayılmaz.
- **COMPONENT:** React/Vitest decoder/form/context davranışı; browser veya gerçek API değildir.
- **CONTROLLED_BROWSER:** kontrollü/stub/seam Chromium; gerçek durable server replay değildir.
- **ISOLATED_C01_BROWSER:** gerçek API+frontend, yalnız etkin C-01 kapsamı; create/link kabulü değildir.
- **INTEGRATED_ROLELESS_BROWSER:** pinlenmiş güncel frontend/backend/schema/fixture üzerinde roleless create/list/detail/link, context/protocol ve gerçek persistence birlikte.

Test tasarımı PASS değildir. Her sonuç exact source, gerçek checkout, run/job ve kapsama bağlıdır. 080e7a1 sonuçları 880cae8'e veya sonraki SHA'ya otomatik taşınmaz. Kaynak incelemesi, başkasının koşum raporu, doğrudan CI günlüğü okuması ve yeni test çalıştırma ayrı tutulur. CHATGPT-BUSINESS bu teslimde ürün testlerini yeniden koşturmadı; aşağıdaki belirtilen günlükleri okudu.

## 9. Kanıta bağlı kabul matrisi

Her satırda başlangıç/eylem, beklenen kayıt ve yasak etki belirtilmiştir. Backend için yeni kanıt kesiti §9.1'deki 880cae8/969a4a8 koşumudur; eski080 sonucu değildir. Entegre protokol browser'ı bütün ilgili satırlarda henüz NOT_RUN'dır.

| Kabul | Başlangıç, eylem ve beklenen/yasak etki | Backend kanıtı | Component / controlled / C-01 | Bütünleşik kabul ve kalan |
|---|---|---|---|---|
| CRO-AC-001 | Yetkili A kullanıcısı; taze valid GET+teyit, sonra roles eksik/0 create. Bir Party+A account, roles0, ilgili audit/durable result. Eski server200/no-ack varyantında POST0, kayıt0. | OptionalRoles SQL missing/zero create ve command-gate senaryoları; yeni broad75 PASS. SQL, frontend'in önce GET yaptığını kanıtlamaz. | #228 roleful create donor; #233 list/detail dışında form/protocol birleşimi yok. C-01 bu senaryonun kanıtı değil. | NOT_RUN: gerçek form→GET/ack→POST201→doğru detail; no-ack sunucuda hiç POST olmaması. |
| CRO-AC-002 | Eski1/3/15 kayıtlarıyla upgrade; kimlik/rol/audit/replay değişmez. Zero sonrası Down reddi; DDL/veri kaybı yok. | OptionalRoles migration/legacy/Down senaryoları yeni broad75 içinde PASS. | UI uygulanmaz. | Backend scope kanıtlı; production migration yapılmış değildir. |
| CRO-AC-003 | Party A'da görünür, B account yok. B bağlamında taze valid GET+ack; `{}` veya roles0 link. Aynı Party, tek B account, A değişmez. A teyidiyle B yazması yasak. | OptionalRoles create/link ve target isolation SQL; yeni broad75 PASS. | Donor link ve candidate/mutation uyarlaması bekler. Controlled roleful link, roleless kanıt değildir. | NOT_RUN: B-readiness, same Party, yeni B hesabı ve no-ack B'de POST0. |
| CRO-AC-004 | Yetkisiz/foreign Party ve şirket varyantları. Doğru başlık grant vermez; mevcut403/404, kimlik sızıntısı ve business etki0. | OptionalRoles authorization/isolation + transport HTTP/SQL senaryoları; §4.4 error yolları ayrıca HTTP contract. | #233 company/generation okuyucu kontrolleri kendi kapsamlarında; C-01 yetki listesi sınırlı. | NOT_RUN: context reset, yanlış şirket cevabından readiness kurulmaması, yetkisiz yazma0. |
| CRO-AC-005 | Gönderilmiş command'in yanıtı timeout/malformed/ack eksik. Aynı attempt/key/company/Party/payload korunur; sonra409 ve tazeGET+ack sonrası açık same-key retry. İkinci business etki ve auto-new-key yok. | Durable replay/rollback ve protocol key-consumption SQL; yeni broad75 PASS. | #228 controlled unknown-outcome yalnız controlledResponsesOnly/persistentServerReplayClaimed=false. Yeni transport component zinciri henüz yok. | NOT_RUN: gerçek browser+server aynı key/path/payload ve tek kalıcı business etki. |
| CRO-AC-006 | İki farklı key aynı Party/B link yarışı; ayrıca audit/idempotency hata enjeksiyonu. Tek target account/başarılı created audit; yarım write yok. | OptionalRoles different-key race + audit/idempotency rollback; yeni broad75 PASS. | UI bu SQL kanıtının yerine geçmez. | Backend scope kanıtlı. Kaybedenin durable conflict'i ikinci hesap değildir. |
| CRO-AC-007 | Eski1..15 ve yeni0 durable sonuçlar; uyumlu declaration ile replay. Eski hash/snapshot korunur, yeni business etki yok. Headerless replay command'i reddedilir. | OptionalRoles legacy replay + transport command/retry SQL, yeni broad75 PASS. | Controlled replay gerçek durable replay değildir. | Client/mixed-node birleşimi NOT_RUN; eski hash yeniden yazılmaz. |
| CRO-AC-008 | Liste/detail/candidate roles0; named filtre ve count; GET'ler DB değiştirmez. Legacy authorized projection1..15 başarı olabilir, visible0 ise409; gizli foreign0 sızmaz. | OptionalRoles read/no-write + iki protocol SQL fact'in read kapsamı; yeni broad75 PASS. | #233 list/detail roles0..15 component kanıtlı; mutation/candidate donor uyarlaması açık. C-01 yalnız list/regression. | NOT_RUN: dört reader+header/ack+valid GET. 200 fakat malformed body readiness kurmamalı. |
| CRO-AC-009 | Roles0 pasif account, current/stale active-state sürümleri. Contract header zorunlu; gerekçe/yetki/version atlanmaz; pasiflik otomatik aşılmaz. | Mevcut active-state/version kuralları ve header gate kapsamı korunur. Toplam75 sonucundan özel roleless stale-version alt varyantına koşulsuz PASS çıkarılmaz; exact test bağı sahibi tarafından teyit edilecek. | Yeni active-state/protocol browser kanıtı yok. | PARTIAL; özellikle stale/no-op kayıt/version/audit sonucu ayrıca pinlenmeli. |
| CRO-AC-010 | Eski/yeni client-server dört birleşim; mixed nodes; bekleyen response sırasında logout/company/session/API değişimi. Yalnız current valid GET+ack readiness; old server200/no-ack write0; error asla readiness değil. | Backend legacy GET/zero withholding ve command header reddi SQL PASS; client readiness/mixed-node kanıtı değildir. | #233 C-01 yeni payload reader+eski backend kanıtı; yeni transport handshake değil. | CR-10 NOT_RUN: hata başlığı var/yok, geç response, yeni node teyidinden sonra eski node yazma/unknown, somut affinity/drain. |
| CRO-AC-011 | Request roles/null/string/fraction/negative/16; header missing/empty/duplicate/combined/unsupported. Response0/15 geçerli, null/missing/16 geçersiz. Error400/403 ack var;401/binding400 ack yok. | Invalid-role/protocol SQL + §4.4 dört HTTP test/OpenAPI P2 senaryoları. 880 yeni drift CI başarılı; yerel11 ayrı rapor. | #233 list/detail decoder testleri var; error-ack/readiness ve dört consumer birleşimi henüz yok. | NOT_RUN: hiçbir hata success veya readiness olmaz; error no-ack yalnızca eski server diye etiketlenmez. |
| CRO-AC-012 | Başarılı roleless create/link ancak valid mutation+ack ile tek success/detail. Listeye dönüş güncel yetkiyle; eski cursor/generation geri yüklenmez. Belirsiz command'te success toast/navigation yok. | #227 roleful SQL journey korunur; backend transaction kanıtı ekran dönüşü kanıtı değildir. | #233424a586 sınırlı taze cursor walk component'i; detail/return browser NOT_RUN. C-01 liste PASS, form/link/detail yerine geçmez. | NOT_RUN: gerçek roleless akış+odak/konum. Bütçede bulunamadı uyarısı o kayda dönüş PASS'i değildir. |

### 9.1 Backend exact-source kanıt kaydı

**Tarihsel080e7a1:** [CI34538870747](https://github.com/sinangym/GoymenERP/actions/runs/34538870747), SQL75/RCSI10, skip0; C-01 ve UI golden önceki exact-source kanıtıdır. James'in o SHA'daki P2'li incelemesi tarihsel koşullu kayıttır.

**Yeni880cae8:** [CI34571306671](https://github.com/sinangym/GoymenERP/actions/runs/34571306671), [UI golden34571306701](https://github.com/sinangym/GoymenERP/actions/runs/34571306701), [C-01 browser34571306722](https://github.com/sinangym/GoymenERP/actions/runs/34571306722) tamamlanmış success. CI'nın backend/frontend/OpenAPI/SQL/container işleri ile iki ayrı workflow toplam yedi kontrolü oluşturur. Backend dalındaki frontend işi #233'ün yeni protocol implementasyonu değildir.

Bu tur doğrudan okunan [SQL job103173794207](https://github.com/sinangym/GoymenERP/actions/runs/34571306671/job/103173794207):

- CI'nin ilişkilendirildiği PR head: `880cae838e5d2f01a988f1e4a89afbe67858f48c`.
- Günlükte gerçek checkout: `969a4a864b800e7e96fc0158aecbe60d7bf22866`, GitHub test-merge (`880cae8` into `060da63`). Test checkout'u PR head gibi yeniden adlandırılmaz.
- 11 Eylül06:50:34 UTC: **SQLSERVER_RELATIONAL_ACCEPTANCE PASS total75/passed75/failed0/skipped0**.
- 11 Eylül06:52:09 UTC: **RCSI_RELATIONAL_ACCEPTANCE PASS total10/passed10/failed0/skipped0**.
- Bunlar gerçek .NET SQL koşum sonuçlarıdır, verifier MOCK veya self-test satırları değildir. Envanter kaynak handoff'unda68 discovery/75 execution olarak tanımlıdır.

İlgili kaynak/test yüzeyleri:

- [CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.cs @880](https://github.com/sinangym/GoymenERP/blob/880cae838e5d2f01a988f1e4a89afbe67858f48c/tests/GoymenERP.Api.IntegrationTests/CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.cs): missing/zero create-link, legacy migration/replay, Down, authorization/read-only, race, audit/idempotency failure ve protocol alt senaryoları.
- [CounterpartyClientContractTests.cs @880](https://github.com/sinangym/GoymenERP/blob/880cae838e5d2f01a988f1e4a89afbe67858f48c/tests/GoymenERP.Api.IntegrationTests/CounterpartyClientContractTests.cs): HTTP transport ve koşullu hata başlığı; SQL/browser diye sayılmaz.
- [OpenApiContractGenerationTests.cs @880](https://github.com/sinangym/GoymenERP/blob/880cae838e5d2f01a988f1e4a89afbe67858f48c/tests/GoymenERP.Api.IntegrationTests/OpenApiContractGenerationTests.cs) ve canonical OpenAPI: request/response/header metadata.
- #227'nin `CounterpartyJourneySqlServerRelationalAcceptanceTests.cs` mevcut baseline yolculuğu yeni mandatory envanterden çıkarılmaz.

P2 tesliminin yerel raporu: dört yeni HTTP case + yedi OpenAPI/contract testi =11 PASS; schema SHA256 `062c06175916295bc97a438adea6fd01cdb608413c58501aa62208f87038a829`. Bu yerel sayı bu asistan tarafından tekrar koşturulmuş sayılmaz.

[James880 delta incelemesi](https://github.com/sinangym/GoymenERP/pull/230#issuecomment-5630628142) ve [Ampere880 delta incelemesi](https://github.com/sinangym/GoymenERP/pull/230#issuecomment-5630627940) P0/P1/P2=0, önceki P2'nin yeni SHA'da kapandığını bildirir. Bağımsız AI incelemesi olarak aktarılmışlardır; insan/production onayı değildir. Böylece 880'e ait P2 kaynak ve yeni CI kanıtı mevcutken, 080'in koşullu incelemesi geriye dönük değiştirilmemiş olur.

### 9.2 Frontend ve browser kanıt sınırı

#233 `424a586fc07efa291db4b37866ba5cce54fb7443`: Node24 suite267/267 ve exact-source C-01 list10/10 raporu, 15 frontend yoluna aittir. [PR kaynak raporu](https://github.com/sinangym/GoymenERP/pull/233) ile [devir dosyası](https://github.com/sinangym/GoymenERP/blob/424a586fc07efa291db4b37866ba5cce54fb7443/src/frontend/goymen-erp-web/CARI_FRONTEND_DEVIR_2026-09-10.md) esas alınır.

Liste dönüşü artık yalnız uyarı değildir: ilk sayfa taze okunur, gerekirse en çok dört ek sayfaya güncel cevap cursor'ıyla yürünür; eski cursor replay edilmez. Bu sınır mevcut implementation parametresidir, yeni genel işletme politikası değildir. Kayıt bulunursa odak döner; bütçede bulunamaması gerçek dönüş başarısı değildir. Detail capability kapalı C-01 yığını bu walk'ı veya create/link/detail zincirini tarayıcıda kanıtlamaz.

#233'ün bu head'i yeni declaration/ack/readiness'i henüz uygulamaz. Eski C-01 veya component yeşili yeni protokolün PASS'i değildir. Ayrı tek seferlik16/16 liste davranışı raporu ve eski #231266/266 kaydı tarihsel/kapsamlı kalır. Node26'daki iki yerel hata yeni mandatory CI arızası diye tekrar açılmaz.

### 9.3 Donor ve kontrollü browser sınırı

#228 recipient `HANDOFF-ACCEPTED`5626181170 vardır; tekrar istenmez. Donor release ayrıca gereklidir; bu şartname donor gibi makbuz vermez veya onun unpublished successor dosyalarını sahiplenmez.

[Koordinatörün controlled evidence kaydı](https://github.com/sinangym/GoymenERP/pull/228#issuecomment-5624268419): run34103473977 artifact10011561394; SDK80/80, helper54/54, controlled Chromium17/17; test-merge checkout2ebcbc7 ile d46 kaynak ağacının eşliği ayrı doğrulanmıştı. `persistentServerReplayClaimed=false`, `erp_accepted=false`; bu sayılar gerçek roleless server/browser birleşimi veya yeni protocol kabulü değildir. CSS/DPR eşdeğerleri gerçek browser zoom/screen-reader konuşması sayılmaz.

## 10. Frontend implementation sözleşmesi

### 10.1 Dört reader

Mevcut #233 sahibi list/detail'i, donor release sonrasında mevcut mutation/candidate'i aynı required integer0..15 contract'a uzlaştırır. 15 kabul,16 ret; response null/missing→zero yok. Identifier, şirket, PartyKind, aktiflik ve version korumaları değişmez. Yeni decoder ailesi veya paralel create/link uygulaması açılmaz.

### 10.2 Form, düğme ve hata

Mevcut create/link formlarındaki mandatory-role validation ve metin kaldırılır; başka zorunlu sınıfa dönüştürülmez. Firma/Kişi, hukuki ad, opsiyonel ticari ad ve salt okunur seçili şirket korunur. Kaynak roller hedefe kopyalanmaz.

Permission kaynaklı aksi mevcut non-disclosing kuralıyla sunulmaz; yetkili kullanıcı için uyumluluk doğrulanamaması işlevin çalışıyormuş gibi gösterilmesine izin vermez. Güvenli Türkçe açıklama ve mevcut error/dirty/pending/focus davranışı korunur. Teknik başlık kullanıcıdan elle istenmez.

Yükleniyor/GET hatası/uyum doğrulanamadı durumunda write kapalıdır. Yalnız valid GET+ack açabilir. Command başarısı ancak kendi tam response ve teyidiyle gösterilir; belirsizlikte başarı bildirimi, formu temizleme veya detail navigation yapılmaz. Hata sınıfı başlık yokluğundan uydurulmaz.

### 10.3 Bağlama bağlı hazırlık

`(apiOrigin, actor/session, selectedCompanyId, generation)` değişirse readiness düşer. Taze GET cevabı aynı tuple'a ait olmalıdır. Gerçek sunucu teyidi, response decoder ve mevcut authorization ayrı zorunluluklardır. Transport header eski source-account görünürlüğünü target link yetkisine çeviremez.

### 10.4 Belirsiz girişim

§4.5 tek attempt/key/payload/context kaynağıdır. Mevcut commandAttempt/useCommandAttempt, dirty/pending/company/logout fence ve accessible retry kaydı korunur. Daha sonra gelen409 önceki belirsiz girişimi yok saydıramaz. Fresh valid GET+ack sonrasında yalnız explicit same-key retry; otomatik yeni key yoktur.

## 11. Kalan görevler — mevcut sahiplik ve görevlerin devamı

Aşağıdaki CB/CF/CO/GB etiketleri mevcut #232 görev satırlarıdır; yeni issue, bağımsız görev sistemi veya paralel writer atanması değildir.

### CODEX-BACKEND — #230 mevcut sahibi

**CB-1 — P2 kanıtı güncellendi.** Kaynak: §4.4 ve §9.1. 880'de beş dosyalık düzeltme, James/Ampere delta kapanışı ve yeni CI/SQL kanıtı mevcuttur. Aynı P2 veya eski main conflict'leri yeniden açılmaz. Daha yeni backend commit gelirse yalnız ona ait fresh CI/review/SQL kanıtı istenir; 880 sonucu taşınmaz.

**CB-2 — bütünleşik protocol sözleşmesini koru.** Kaynak:5626451657 ve5630622096. Headerless/unsupported commands sıfır etkiyle ayrılan409; legacy authorized GET, koşullu error-ack, CORS allowlist, GET no-write, durable same-key, migration ve #227 mandatory journey korunmalı. Ölçülebilir bitiş: seçilen integrated backend/schema SHA'sında mevcut inventory/negative tests ve uygun fresh kapılar; CRO-AC-009 roleless stale-version alt testinin exact bağı veya dürüst açık kaydı.

**CB-3 — mevcut izole kabul altyapısını değerlendir/devret.** Kaynak: koordinatörün mevcut altyapı sırası. P2 sonrası yayımlanmış donor lifecycle/fixture kullanılmalı; ikinci altyapı veya paylaşılan servis restart'ı açılmaz. Bitiş: tek kullanımlık, owner/run/commit/company/permission-bound ortamda gereken create/detail/link capability'leri ve teardown kanıtı; normal/production provider aktivasyonu değildir.

### CLAUDE-FRONTEND — #233 mevcut sahibi

**CF-1 — yeni transport receipt ve uygulama.** Kaynak:080 handoff +880 conditional error clarification. Mevcut #233'te exact declaration, iki stable compatibility code, valid GET+ack readiness uygulanmalı. Payload-only veya ownership receipt yeni protocol receipt değildir. Bitiş: exact frontend commit, kullandığı backend/schema pinleri ve yeni contract testleri.

**CF-2 — donor release sonrası dört reader/form.** Kaynak: mevcut #228 donor/recipient devri. Recipient makbuzu tekrar istenmez; donor release olmadan shared yüzey yazılmaz. Release sonrası mevcut create/link/router/attempt kodu alınır; mutation/candidate>=1 ve mandatory role kaldırılır, tüm eski ProblemCode'lar iki yeni code ile birlikte korunur. Bitiş: dört decoder için0/15 pozitif, null/missing/16 ve şirket/version negatifleri; roles eksik/0 form submit sözleşmesi.

**CF-3 — readiness component varyantları.** Kaynak:CRO-AC-001/003/004/008/010/011. Taze valid GET+ack; old server200/no-ack→POST0; error400/403+ack→hazır değil;401/binding400-no-ack→eski server iddiası yok; actor/session/company/API/generation ve late response. Bitiş: her varyantta gönderilen command sayısı ve readiness durumu assert edilen gerçek component koşumu; önceki267 bunun yerine kullanılmaz.

**CF-4 — belirsiz attempt.** Kaynak:CRO-AC-005/007/012. Timeout/malformed/ack eksik→unknown; later409'da aynı attempt; fresh GET+ack sonrası açık same-key retry. Bitiş: command key/path/payload/company eşliği, yeni otomatik key/request olmadığı ve stale success/navigation olmadığı testli.

**CF-5 — gerçek bütünleşik browser.** Kaynak: aynı CRO seti, yeni kimlik yok. Pinli backend/frontend/schema/fixture ile roleless create→list→detail→B link; wrong-company/permission; old-client/new-server ve new-client/old-server; pending/logout/unknown/retry. Bitiş: gerçekten çalışmış browser+API+SQL kayıt sayıları, skip ayrımı ve teardown. C-01 list10/10 veya controlled17/17 bunun yerine geçmez. Dönüş walk/gerçek zoom/screen-reader ayrı ölçülür; çalışmayan PASS değildir.

### CODEX-ORCHESTRATOR

**CO-1.** Mevcut #230/#233/#228 sahiplik sırasını koru; yeni parallel spec/backend/create-link writer açma.

**CO-2.** Var olan recipient receipt'i tekrar isteme; yalnız gerçek donor release'i ve yeni transport acceptance receipt'ini kendi anlamlarında takip et.

**CO-3.** 080 ve880 kaynak/test/incelemelerini ayrı tut. 880 yeni CI/SQL/P2 kanıtı §9.1'de vardır; sonraki SHA için taşıma yapılmaz. Belge değişikliği ürün kabulü değildir.

**CO-4.** P2 sonrası mevcut altyapı sahibinin integrated fixture hazırlığı → Claude protocol/consumer/form kabulü → pinli gerçek integrated browser sırasını koru. Mixed nodes için somut affinity/drain checkpoint'ini kayıtla; var sayma veya production ayarını bu belgeyle seçme.

**CO-5.** Final K3 exact backend/frontend/schema/fixture, fresh CI/SQL/browser ve iki bağımsız exact-head review kapılarını doğrula. Lane self-merge yok. #229/#232 insan incelemesi ve production Sinan kapısı ayrıdır.

### CHATGPT-BUSINESS

**GB-1.** Yalnız mevcut #232 dosyası ve kendi PR açıklaması exact-source kanıtla güncellenir; kanonik/shared ilerleme kaydına paralel yazılmaz.

**GB-2.** CRO-AC-001…012 ve eski CR eşlemesi korunur; protokol varyantları bunlara bağlanır.

**GB-3.** Hemen sonraki hazır iş mevcut bütünleşik Cari kabulüdür. Aşağıdaki otomatik şirket ilişkisi yalnız önceden hazırlanmış bir sonraki pre-spec olarak korunur; implementation sırası/permission kararı onaylanmış sayılmaz. Bunun ötesinde sipariş veya genel ERP analizi açılmaz.

## 12. Sonraki tek dilim — otomatik şirket ilişkisi pre-spec

Yerel çalışma adı: `CARI_AUTO_ACCOUNT_ON_FIRST_AUTHORIZED_BUSINESS_COMMAND_JIT_v1`. Ayrı dosya açılmaz. Durum: **PRE-SPEC / IMPLEMENTATION-READY DEĞİL / NOT_RUN**.

Mevcut Cari roleless/protocol/entegrasyon işlerini bloke etmez. Otomatik ilişki implementation'ı ancak koordinatör sırası doğrulandığında ve ilgili açık yetki politikası karara bağlandığında açılır; bu belge next-step dispatch değildir.

### 12.1 Tetikleyici ve bağlam

GET/list/detail/search/candidate tetikleyici değildir. Yalnız gerçekten var olan authenticated ve yetkili business write command ihtiyaç duyduğunda account ensure değerlendirilebilir. Henüz olmayan sipariş endpoint'i uydurulmaz.

En az actor, tek resolved CompanyContext, PartyId, business permission, Party görünürlük/uygunluk, command validation, idempotency/correlation gerekir. CompanyId body'den serbestçe seçilmez. Mevcut transport/readiness de business authority yerine geçmez.

### 12.2 Account davranışı

Uygun aktif hedef hesap varsa aynısı kullanılır. Hesap yoksa yalnız onaylanacak yetki politikası sağlanırsa roles0 ensure değerlendirilebilir. Yeni Party veya kaynak şirketten rol/aktiflik/bakiye kopyası üretilmez. Mevcut pasif hesap sessizce aktifleştirilmez; ayrı active-state/version/permission sözleşmesi korunur.

### 12.3 Atomiklik ve rollback

Yeni account ile onu gerektiren business write aynı logical başarıya bağlıdır. Business validation/persistence/audit/idempotency başarısızsa yeni hesap kalmaz. Bir business modülü Counterparty tablosuna doğrudan yazmaz; teknik transaction/orchestration Codex'in sahipliğindedir. Kısmi işlemin kapsamı gelecekte tetikleyen gerçek command sözleşmesine bağlanır; burada miktar/formül politikası seçilmez.

### 12.4 Retry ve yarış

Same business-command replay ikinci account/iş etkisi yaratmaz. İki ayrı command aynı `(CompanyId, PartyId)` hesabına ihtiyaç duyarsa tek account kalır; her business command kendi yetki ve idempotency sonucuna göre tamamlanır. Account ensure kendi başına GET yan etkisine çevrilmez.

### 12.5 Açık permission politikası

- **A:** business command permission + mevcut `counterparty.accounts.create` gerekir. Yetki değişimi azdır; günlük admin link ihtiyacı sürebilir.
- **B — öneri:** business command permission'ı yalnız kendi transaction'ındaki implicit account etkisini kapsar; standalone link yetkisi vermez. Günlük ayrı bağlama yükünü azaltır, command kapsamının açık audit/yetki sözleşmesini gerektirir.
- **C:** ayrı `counterparty.accounts.ensure` permission. Daha ince yönetim sağlar; yeni permission matrisi/yönetim yükü getirir.

B yalnız CHATGPT-BUSINESS önerisidir; kullanıcı onayı olmadan KILITLI/ACCEPTED/DONE veya production kuralı yapılamaz. Hiçbir A/B/C seçimi bu tur uygulanmamıştır.

## 13. Gerçek kullanıcı kararı ve devir sınırı

Mevcut roleless/transport Cari düzeltmesi için yeni işletme kararı gerekmiyor. `roles=0/null` tercihi tekrar sorulmaz; request/response §3'te sabittir. P2 teknik düzeltmesi kaynakta tamamlanmıştır; yeni kullanıcı politikası değildir.

`NEEDS-SINAN-CARI-AUTO-001`: yalnız gelecekte implicit account permission kaynağı A/B/C kararı gerekir. ÖneriB, **onay değil**; mevcut Cari düzeltmelerinin ön şartı değildir. Çok şirketli piksel düzeni tüm akışı bekleten bir kullanıcı kararı yapılmaz.

Kaynak olarak [tarihli kullanıcı kararları](https://github.com/sinangym/GoymenERP/blob/e1fd69da69df36a72e559ea07163fb3f359b3c61/docs/decisions/SINAN_KARARLARI_2026-09-10.md), `docs/decisions/KARAR_SICILI.md`, `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`, mevcut Cari JIT/yetki sözleşmeleri ve öncelikli iki protocol handoff birlikte okunur. Eski #227 headerless örnekleri yeni contract'a aynen çalıştırılmaz; tarihsel kanıt olarak korunur.

#229 ve #232 insan review/acceptance kapıları açık kalır. Bu dosyanın güncellenmesi diğer ajana uygulama gönderildiği, onların çalıştırdığı, merge edildiği, runtime açıldığı veya ERP'nin kabul edildiği anlamına gelmez. Bu teslim kaynak okuma ve şartname güncellemesidir; ürün testleri CHATGPT-BUSINESS tarafından yeniden çalıştırılmamıştır.
