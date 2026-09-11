# Cari Opsiyonel Ticari Rol Uzlaştırma Şartnamesi v1

- Tarih: 11 Eylül 2026
- Şerit: `CHATGPT-BUSINESS`
- Durum: **REVIEW EDİLEBİLİR İŞ ŞARTNAMESİ / ERP KABULÜ DEĞİL**
- Dosya: `docs/features/CARI_ROLE_OPTIONAL_RECONCILIATION_SPEC_v1.md`
- Mevcut PR: **#232**; yeni belge veya paralel şartname değildir.
- Hazırlık tabanı: `main@060da63f2644cb271d78d9eab8b7a6985d544ad1`
- Önceki belge revizyonları: `1299a6ee9f817c552ee08bbe385d4cb12c11bb8e` → `4e063e26f2c5848f7330891016d99a82088efa7b` → `5b3c8d80bf48bb18bd4b27b17006237429c04257` → `def6ff7faa8ad14e4e6d48d2b12fee5f572610aa` → `24f751ad489b4d1d2bb3a7b900fd6d279b9b534e` → `3eb3efa3532c0b15f27e129d32eab0b90a3bc8fb`. Bu toplu güncelleme mevcut içeriğin devamıdır.
- Öncelikli transport kaynağı: [#233 / 5626451657](https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5626451657), backend `080e7a1d24f74975b45546c5277f0529c13f06d1`.
- Koşullu hata başlığı düzeltmesi: [#230 / 5630622096](https://github.com/sinangym/GoymenERP/pull/230#issuecomment-5630622096), backend `880cae838e5d2f01a988f1e4a89afbe67858f48c`.

Bu belge uygulama kodu yazmaz, kanonik karar/task durumunu değiştirmez, `docs/planning/2026-09-10/**` veya paylaşılan ilerleme belgelerinin tek-yazar sahipliğini devralmaz. PR #229 karar paketi ile #232'nin uygulanabilir insan inceleme/kabul kapıları korunur. Kaynak göstermek, bu kapıları geçmek veya production yetkisi vermek değildir. Otomatik şirket ilişkisi için `B` yalnız öneridir; kullanıcı kararı değildir.

- Bu güncellemenin tetikleyicisi: **aynı gerçek `c006-303760997c6f` koşumunun legacy HTTP, L02, başarısız L01, STRICT SQL delta ve owned cleanup paketinin birlikte teslimidir**. [Merkezî kayıt][co-record] ve §9.5 kaynak/hash bağlarını içerir. Önceki CRO-AC-009 özel REAL_SQL kapanışı §9.1.1'de korunur; yeni test sayısı veya makbuz için faaliyet değildir.
- Koşumun frontend kaynağı #233/`6bfc21eec43f02f3c8f2e0d86fd124c0f387bff4`; derlenmiş acceptance kaynağı #234/`32392ca97e3ea74a4300855f015ee65febd66f87`; API bağımlılığı #230/`880cae838e5d2f01a988f1e4a89afbe67858f48c`. Bu yayımlanmış başlar doğrulanmıştır. Koşum sonrası harness düzeltmesi henüz yayımlanmış kaynak/başarılı koşum sayılmaz.
- **L01 `RUN_FAILED`, L02 yalnız ölçtüğü kapsamda PASS, legacy8 yalnız gerçek HTTP kapsamında PASS, STRICT global SQL delta ve cleanup başarılıdır.** L01 trace gözlemleri `TRACE_DERIVED/PARTIAL`; ölçülmeyen yollar `NOT_MEASURED` kalır. Gerçek ENV_READY üretilmiş, sonra ortam temizlenmiştir: E tarihsel, `readyForBrowserNow=false`; ERP kabulü/tamamlanması yoktur.

## 1. Kaynak önceliği ve güncel depo gerçeği

Öncelik: `AGENTS.md` ve uygulanabilir governance altında en yeni açık kullanıcı kararı → tarihli kullanıcı kararları → `KARAR_SICILI` → Master Rapor → ilgili feature/ADR → eski referanslar. Açık yetki, güvenlik, migration veya production politikası tahminle kapatılmaz.

11 Eylül 2026 kaynak kesiti:

- #227 merge edilmiştir; main `060da63f2644cb271d78d9eab8b7a6985d544ad1`. Mevcut create/list/detail/company-link API yolculuğunun gerçek-SQL baseline'ıdır; yeni roleless frontend/transport veya production kabulü değildir.
- #228 `d46f7f819456fc578a25cdcabf5c98ad6f634456` yayımlanmış donor kaynağı korunur. **83 yolun aktif yazarlığı [HUMAN-OWNERSHIP-TRANSFER5632970696][ownership-approved] ile aynı Claude/#233'e verilmiş, [3703862ce28bfc5bee55248e5ca7d2ef5e46fe28][fe-import] ile seçmeli import yapılmıştır.** Bu soru artık onay beklemiyor. Donör HANDOFF-RELEASED değildir; mevcut recipient5626181170 tekrar istenmez. Bilinmeyen LENOVO WIP alınmış sayılmaz;17 Backend yolu aynı sahibinde kalır.
- #229 `e1fd69da69df36a72e559ea07163fb3f359b3c61`, insan incelemesi gereken açık/draft karar paketidir.
- #230 `080e7a1d24f74975b45546c5277f0529c13f06d1`, main uzlaştırması ve yeni transport gate'in önceki kanıt başıdır: 7 kontrol, SQL75/RCSI10, sıfır skip. James'in bu SHA'daki conditional error-header P2'si tarihsel olarak koşulludur; geriye dönük koşulsuz onaya çevrilmez.
- #230 `880cae838e5d2f01a988f1e4a89afbe67858f48c` için beş dosyalık OpenAPI/handoff/test P2 düzeltmesi, James/Ampere dar fark incelemeleri ve kendi fresh CI/SQL75/RCSI10 kanıtı korunur. Bu tur aynı kapanmış P2, eski main conflict'leri veya SQL kapsamı yeniden açılmadı; ayrıntı §9.1.
- #234, #230/880 tabanlı aynı Backend sahibinin C006 adayıdır. `2b21e3b` özel roles=None+inactive+stale+permission SQL kanıtı §9.1.1'de tarihsel exact-source bağıyla korunur. Güncel compiled/acceptance başı `32392ca97e3ea74a4300855f015ee65febd66f87`; matching local build, gerçek ENV_READY, STRICT doğrulama ve cleanup §9.5'tedir. Eski manifest/izin/source-P2 engelleri yeniden açılmaz; ortam şu anda çalışıyor denmez.
- #233'ün yayımlanmış güncel başı `6bfc21eec43f02f3c8f2e0d86fd124c0f387bff4`, main060 tabanlı açık/draft adaydır. c118/ca19 ve sonraki kaynak kapanışları §9.2'de tarihsel olarak ayrılır; aynı [Dirac][run-fe-review-1]/[Averroes][run-fe-review-2] exact6b incelemeleri kaynak kapanışıdır. Sonraki gerçek L01 harness hatası bu incelemelerle kapatılmış sayılmaz.
- #231/173b003 ve #233'ün424/9a/07a/ef/6f kapanışları korunur. İzin/header/helper/transport ve kapanmış retry implementation işleri yeniden dağıtılmaz. **Tam L01 gerçek koşumu artık NOT_RUN değil RUN_FAILED'dır; ölçülen alt sonuçlar PARTIAL, henüz ölçülmeyen kabul yolları NOT_MEASURED/NOT_RUN'dır.** Aynı koşum tekrar edilmemiştir.

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

**CRO-AC-001/003/004/008/010/011 — gerçekleşen dar düzeltmeler:**9a6b43d'de başarılı GET sonrasındaki fetch rejection/offline eski hazırlığı bırakıyordu; [07a teslimi][fe-fix] bu yolu liste ve detay transport'unda revoke ile düzeltmiştir. Saf503 gözlem testiyle eş tutulmaz; aynı transport'ta başarılı okuma→cevapsız ağ hatası/offline dizisi testlidir. İlgili helper/transport hedefi yeniden AÇIK yazılmaz. Form/komut bağlantısı c118'de mevcuttur; güncel gerçek L01/L02 kapsamı ve ölçülmeyen alt yollar §9.5'te ayrıdır.

[Ef43700 dar teslimi][fe-final] ve [iki][fe-review-1] [non-author inceleme][fe-review-2] şu iki kalan P2'yi yalnız yeni dört-dosya farkında kapatır: (1) `beginContractRead` bağlamı istek çıkarken kaydeder; OLD bekler→NEW valid+ack200→OLD geç valid+ack200 dizisinde eski gözlem NEW grant'ini ne değiştirebilir ne geri alabilir. (2) `response.json()` sırasında aborted signal, `fence-stale` ve readiness'e dokunmayan sonuçtur; gerçek, aborted olmayan malformed body hâlâ readiness'i geri alır. Abort, sunucunun uyumsuzlaştığı kanıtı değildir.

Kanıt sınırı: [transport testleri][fe-transport] liste/detay deferred OLD-success yollarını ve body-stage abort/malformed ayrımını kapsar. Abort testleri pre-aborted signal + mock body rejection kullanır; gerçek response-stream zamanlaması veya komut DOM'u kanıtlanmış değildir. c118 form component testleri create probe auth/tipli hata ve link readiness/disabled/POST0 yollarını ayrıca kapsar; bunlar yalnız helper sayılmaz ([Dirac][fe-dirac], [Averroes][fe-averroes]). ca19 ve sonraki kaynak kapanışları tarihsel olarak korunur. Gerçek L02'de aborted/body-unavailable GET'ten başarılı geç body, leak veya grant nedenselliği çıkarılmaz; gerçek L01'in izlediği kendi GET+ack/retry yolu testin son başarısızlığını kaldırmaz (§9.5). Kapanmış helper/transport kapsamına ikinci paralel düzeltme veya eski review turu açılmaz.

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

**CRO-AC-005/007/010/011/012 — yardımcı ve gerçek form kanıtları ayrı:**9a6b43d'nin headers-only `settlesUnknownOutcome` açığı [07a4af2 tesliminde][fe-fix] düzeltildi. [Ef kaynaklı yardımcı][fe-helper], aynı bağlam/key/normalize payload parmak izi, kendi güncel hazır bağlamından gelen matching ack ve `validated`2xx komut sonucu/replay ister. Timeout/no-response,500, çözülemeyen gövde, bütün4xx ve sonraki compatibility409 unknown'ı kapatmaz; farklı key/payload/context de kapatmaz. `mayRetrySameKey` yeni key üretmez. Bu davranışın [saf işlev testleri][fe-unit] vardır; ef43700 dar incelemesinde eski settlement kapsamı yeniden incelenmedi.

c118 gerçek form/command akışında `unresolvedSince` retry boyunca korunur; create DOM testinde sonraki compatibility409'un eski unknown'ı kapatmaması ve aynı-key kontrolü vardır. [Tamamlanmış iki inceleme][fe-dirac] [bunu doğrular][fe-averroes]. Bu artık yalnız `validated` etiketi verilen yardımcı testi değildir; fakat stub/component çalışması gerçek server commit/replay veya browser kabulü değildir.

Değişmeyen kabul: unknown → retry ön kontrol GET pending → kullanıcı bırakır veya aday aramasını değiştirir → geç/cache'e dönen/iptal edilmiş kontrol **yeni POST üretmemeli**. Retry kendi taze, başarılı, decoder-valid ve ack'li okumasını gerektirir; yalnız refetch promise'inin çözülmesi veya eski readiness boolean'ı yetmez. Unknown mevcutken üst uyumluluk uyarısı da “hiçbir istek gönderilmedi / sayfayı yenileyin” diyerek girişim/key kaybına yöneltmemeli. c118'de saptanıp sonraki kaynaklarda kapanan implementation yolları yeniden açılmaz. §9.5 L01 izinde gerçek upstream201 kaybı→unknown→kendi taze GET200+ack→explicit same-key201 replay vardır; global tek SQL etkisi ayrıca doğrulanmıştır. Pending-retry abandon/query, ack'li500/sonraki409 ve malformed-stream recovery bu gerçek koşumda ölçülmüş sayılmaz; L01 RUN_FAILED korunur.

Güncel yetki/bağlam tekrar doğrulandıktan ve o bağlamda taze başarılı GET + exact teyit alındıktan sonra, yalnız açık same-attempt retry aynı key ve normalize payload ile yapılabilir. Kullanıcı/oturum/şirket değişimi otomatik recovery veya otomatik retry değildir; önceki girişim yeni bağlama taşınmaz. Eski yanıt yeni ekrana success/navigation yaptıramaz.

Create ile link ayrı logical attempts'tir; farklı key kullanır. Aynı command/context/key içinde missing roles ile numeric0 eşdeğerdir. HTTP hata statüleri korunur; başlık bir iş işleminin commit olup olmadığının tek başına makbuzu sayılamaz.

### 4.6 Mixed-node sınırı

Protokol eski backend binary'sini sonradan uyumlu hale getirmez. Teyit veren node'a GET yapılması, sonraki yazmanın aynı uyumlu node'a gideceğini kendiliğinden kanıtlamaz.

[Koordinatör kaydı][co-record] ve [ef teslimi][fe-final] mevcut compose/C-01 topolojisini tek API + nginx tek upstream olarak ayırır. **Tek-node C006 için yeni gateway/loadbalancer/drain altyapısı işi açılmaz.** Eski istemci/yeni sunucu ve yeni istemci/eski sunucu kabulü gerekli kalır; tek-node gerçeği bunları kaldırmaz. Production topolojisi doğrulanmış değildir.

Yalnız gerçek ortak-DB/karma backend havuzu varsa ayrı deployment kapısı olarak ilk roles0 yazımından önce tüm Cari GET/list/detail/candidate/command/active-state/replay rotaları eski node'lardan çıkarılmalı veya eşdeğer doğrulanmış guard bulunmalıdır; direct-old-node ingress ve inflight drain dahildir. GET acknowledgment sonrasında POST/retry'ın eski node'a fallback yapması yasaktır. Yalnız sticky-session, ortak DB'deki0'ı eski okuyucudan korumaz. Bu koşullar uygulanmış routing/production politikası diye kaydedilmez.

Sonraki command teyitsiz kalırsa §4.5 uygulanır. Normal capability kapısı ve production aktivasyonu ayrı kalır; koşullu karma-havuz kapısı mevcut C006 hazırlığına koşulsuz yeni ön şart yapılmaz.

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
- **FRONTEND_UNIT:** saf yardımcı/decoder testi; gerçek form veya command entegrasyonu değildir.
- **FRONTEND_TRANSPORT_STUB:** gerçek reader işlevleri çağrılır, fetch/response stub'dır; canlı HTTP/API/SQL değildir.
- **COMPONENT:** render edilmiş React ekran/form, düğme, state ve command bağlantısı; yalnız Vitest kullanılması testi component yapmaz. Gerçek browser/API ayrıca gerekir.
- **CONTROLLED_BROWSER:** kontrollü/stub/seam Chromium; gerçek durable server replay değildir.
- **ISOLATED_C01_BROWSER:** gerçek API+frontend, yalnız etkin C-01 kapsamı; create/link kabulü değildir.
- **INTEGRATED_ROLELESS_BROWSER:** pinlenmiş güncel frontend/backend/schema/fixture üzerinde roleless create/list/detail/link, context/protocol ve gerçek persistence birlikte.

Test tasarımı PASS değildir. Her sonuç exact source, gerçek checkout, run/job ve kapsama bağlıdır.080/880, önceki frontend başları, c118/ca19 ve2b/1d/e35 sonuçları birbirine otomatik taşınmaz. Kaynak incelemesi, yazar/koordinatör log aktarımı, doğrudan CI günlüğü okuması ve yeni test çalıştırma ayrı tutulur. Önceki tur CRO009 kaynağı/gerçek SQL job günlüğü doğrudan okunmuştur. Bu tur §9.5 ham dosya/trace/hash/typedRows sonuçları, koordinatörün ham kanıt doğrulamasına atıflıdır; BUSINESS yayımlanmış kayıtları ve kaynak başlarını okumuş, yerel Windows artefaktlarını veya ürün testlerini yeniden çalıştırmış sayılmamıştır. B-C01-001…010, L01/L02 ve protokolün8 browser ölçümü CRO/CR kimliklerinin yerine geçirilmez.

`TRACE_DERIVED`, başarısız gerçek koşumun korunmuş izinden çıkarılan sınırlı gözlemdir; başarılı test veya normal network/measurement JSON'u değildir. `RUN_FAILED` çalıştırılmış başarısız koşumu, `NOT_MEASURED` belirli koşumda ölçülmeyen alt yolu, `NOT_RUN` hiç çalıştırılmamış kalan kabulü belirtir. Bunlar yeni kabul kimlikleri değildir.

## 9. Kanıta bağlı kabul matrisi

Her satırda başlangıç/eylem, beklenen kayıt ve yasak etki belirtilmiştir. Korunan backend kapsamı880cae8/969a4a8; CRO009 REAL_SQL kanıtı2b21e3b/45dc98d koşumudur (§9.1.1). **Yeni gerçek paket §9.5'teki aynı run/instance ve FE6b/compiled323/API880 bileşimidir.** L01 RUN_FAILED, izde görülen alt davranışlar TRACE_DERIVED/PARTIAL; L02/legacy HTTP/STRICT SQL/cleanup yalnız kendi ölçtükleri kapsamda başarılıdır. Eski unit/component/C-01 veya yeni kaynak CI'sı bu başarısızlığı kapatmaz.

| Kabul | Başlangıç, eylem ve beklenen/yasak etki | Backend REAL_SQL / HTTP | Frontend unit / stub / component | Browser kanıtının türü ve kapsamı | Bütünleşik kabul ve kalan |
|---|---|---|---|---|---|
| CRO-AC-001 | Yetkili A kullanıcısı; taze valid GET+teyit, sonra roles eksik/0 create. Bir Party+A account, roles0, ilgili audit/durable result. Eski server200/no-ack varyantında POST0, kayıt0. |880 SQL korunur. §9.5 STRICT global delta yeni1Party+2roles0 A/B hesap ve tek create/link audit/execution doğrular; legacy8 header reddi aralığı sıfır-etkili. | Gerçek form/probe/command ve önceki component kanıtları mevcut; kaynak review/CI ayrı. | L01 TRACE_DERIVED: gerçek roleless A create201, liste/detay, lost-delivery→unknown→kendi GET+ack→aynı-key replay. Eski controlled/C-01 bunun yerine kullanılmaz. | **PARTIAL / L01 RUN_FAILED.** Eski binary200/no-ack form POST/PUT0, bütün ağ/context/failure varyantları ölçülmüş sayılmaz. |
| CRO-AC-002 | Eski1/3/15 kayıtlarıyla upgrade; kimlik/rol/audit/replay değişmez. Zero sonrası Down reddi; DDL/veri kaybı yok. | OptionalRoles migration/legacy/Down senaryoları broad75 içinde PASS. [SQL kaynak][be-sql], §9.1. | N/A; UI testi SQL migration kanıtı değildir. | N/A; production migration yapılmış değildir. | Backend scope kanıtlı; yeni production/kanonik kabul yok. |
| CRO-AC-003 | Party A'da görünür, B account yok. B bağlamında taze valid GET+ack; `{}` veya roles0 link. Aynı Party, tek B account, A değişmez. A teyidiyle B yazması yasak. |880 link/isolation korunur. STRICT global delta aynı Party'ye tek B roles0 hesabı ve tek link audit/execution; eski smoke satırları korunur. |83 kaynak importu, dört reader ve gerçek rolsüz form vardır; kapanmış disabled/retry implementation yolları yeniden açılmaz. | L01 TRACE_DERIVED: B öncesi listede yok/detail404→candidate→gerçek link201/unknown→B'deki kendi GET+ack→same-key replay→B list/detail/dönüş; A hesabı aynı. | **PARTIAL / L01 RUN_FAILED.** Pending retry sırasında abandon/query ve tüm hedef-company readiness olumsuz yolları bu run'da NOT_MEASURED. |
| CRO-AC-004 | Yetkisiz/foreign Party ve şirket varyantları. Doğru başlık grant vermez; mevcut403/404, kimlik sızıntısı ve business etki0. |880/2b SQL güvenlik kanıtı korunur. L01'de ayrı APIRequestContext ile viewer/foreign2GET403+2POST403 gerçek HTTP; browser logout/revocation değildir. L02 bütün invocation aralığında6counter+orderedRows değişmez. | Önceki auth-boundary/component ve transport kapanışları ayrı kalır. | L02 dar PASS:13API/0POST, barrier0; A/B0candidate/selectionfalse. L01 B öncesi görünmezlik/detail404 TRACE_DERIVED. | **PARTIAL.** Gerçek logout/session exit/revocation/API-origin ve başarılı late-body/leak/grant nedenselliği NOT_MEASURED; L01 yine FAILED. |
| CRO-AC-005 | Command sonucu unknown; aynı attempt/key/company/Party/path/payload korunur; tazeGET+ack sonrası açık same-key retry. İkinci etki/otomatik yeni key yok. |880 durable replay ve2b stale409 replay korunur. §9.5 STRICT tüm-run global delta create/link başına tek execution/audit/iş etkisini doğrular. | c118/ca19 ve sonraki same-key/unknown component kapanışları korunur; yeni helper/retry görevi açılmaz. | L01 TRACE_DERIVED: her iki gerçek upstream201 lost-delivery→unknown korunumu→kendi fresh200+ack→explicit aynı-key201, replaytrue ve byte-identical body. Sentetik replay cevabı değildir. | **PARTIAL / L01 RUN_FAILED.** Ara POST-commit→retry SQL anlık ölçümü NOT_MEASURED; yeni ara-SQL zorunlu kapısı icat edilmez. Ack500/sonraki409/malformed-stream/pending-abandon varyantları bu run'da ölçülmüş değildir. |
| CRO-AC-006 | İki farklı key aynı Party/B link yarışı; ayrıca audit/idempotency hata enjeksiyonu. Tek target account/başarılı created audit; yarım write yok. | OptionalRoles different-key race + audit/idempotency rollback; broad75 PASS. [SQL kaynak][be-sql], §9.1. | N/A; frontend birim testi SQL yarış/rollback yerine geçmez. | N/A; bu SQL kabulünün browser karşılığı uydurulmaz. | Backend scope kanıtlı. Kaybedenin durable conflict'i ikinci hesap değildir. |
| CRO-AC-007 | Eski1..15 ve yeni0 durable sonuçlar; uyumlu declaration ile replay. Eski hash/snapshot korunur, yeni business etki yok. Headerless replay command'i reddedilir. |880 legacy/roleless replay ve2b recorded-key reauthorization korunur. Yeni STRICT global tek etki; eski smokeRows aynı. | Mevcut komut/attempt/decoder bağlantısı helper'dan ayrı tutulur. | L01 gerçek server201/replaytrue/byte-identical response izleri ve same-key gözlemi TRACE_DERIVED; eski controlled persistentServerReplayClaimed=false kanıtı ile karıştırılmaz. | **PARTIAL / L01 RUN_FAILED.** Global SQL sonucu ara commit/retry SQL snapshot'ı değildir; tüm legacy/mixed-client/recovery yolları bu run'da çalışmış sayılmaz. |
| CRO-AC-008 | List/detail/candidate roles0; filtre/count; GET DB değiştirmez. Visible0 legacy projection409; gizli foreign0 sızmaz. |880/2b read/no-write korunur. L02 tüm aralık6counter+orderedRows0; legacy HTTP8 aralığı da0. STRICT L01 kontrollü toplam yeni kayıtları ayrıca doğrular. | Dört reader/form mevcut; eski helper/stub/component ve C-01 scope korunur. | L01 dört-reader/list/detail/candidate/B-link/dönüş izleri TRACE_DERIVED. L02 dar PASS13API/0POST; eski C-01 yalnız liste regresyonudur. | **PARTIAL / L01 RUN_FAILED.** Boş adaylarla seçim taşıma ve başarılı geç response body ölçülmedi; bütün GET/context/stream kabulü kapatılmaz. |
| CRO-AC-009 | Roles=None hesap; yetkili pasifleştirme→version2; stale1→409/replay; viewer/foreign yetki reddi; yetkili reactivation→version3. Rol/aktiflik/yetki ayrımı korunur. | **REAL_SQL alt varyant PASS @2b21e3b / checkout45dc98d.** [Named fact][cro009-source] ve [job103280752609][cro009-job] bağı korunur; eski SQL kapanışı yeniden açılmaz. | Active-state form/browser kanıtı bu SQL fact'inden veya create/link component testinden çıkarılmaz. | Bu gerçek L01/L02 paketi active-state browser ölçümü içermez: **NOT_MEASURED / kabul NOT_RUN**. | **PARTIAL toplam / özel REAL_SQL boşluk kapalı.** Koşumun ENV_READY veya global delta başarısı CRO009 browser kabulü değildir. |
| CRO-AC-010 | İki yönde eski/yeni client-server; logout/company/session/API/late-response. Current valid GET+ack readiness; teyitsiz yazma0. Karma havuz yalnız doğrulanırsa §4.6. | Yeni gerçek legacy8HTTP: create/link×omitted/zero×missing/unsupportedheader exact409 ve bütün-aralık sıfır-etki. Eski backend binary çalıştırılmadı. | Önceki readiness/abort/retry source/component kapanışları ayrı kalır. | L02 dar PASS;0candidate/selectionfalse ve heldGET abort/bodyunavailable sınırı var. L01 fresh GET+ack/replay izleri PARTIAL; son test FAILED. | **PARTIAL; tüm CR-10 PASS değil.** İki yönde eski/yeni binary, logout/revocation/API-origin, başarılılatebody ve pendingretryabandon/query NOT_MEASURED. Yeni gateway/mixed-node altyapısı yok. |
| CRO-AC-011 | Request geçersizroles/header; response0/15 geçerli, missing/null/16 geçersiz. Hata+ack success/readiness değil; error-no-ack eski server kanıtı değil. |880 invalid-wire/HTTP/OpenAPI kanıtları korunur. Yeni legacy8 yalnız missing/unsupported declaration ve omitted/zero request matrisi; tüm invalid-body/role matrisi yeniden koşulmadı. | Dört decoder ve uyumluluk/unknown hata metni kaynakları mevcut; eski bulgular yeni görev değildir. | Legacy8 **gerçek HTTP dar PASS**, eski binary veya capability-off kanıtı değil. L01 lost201/replay TRACE_DERIVED; hata+ack ve malformed-stream recovery bu live run'da NOT_MEASURED. | **PARTIAL / ilgili L01 RUN_FAILED.** Yeni HTTP sonucu bütün form/error/recovery/stream kabulünü kapatmaz. |
| CRO-AC-012 | Roleless create/link valid mutation+ack ile tek success/detail; güncel yetki/cursor ile dönüş. Unknown'da success/clear/navigation0. | STRICT global +1Party/+2roles0A-Baccount/+1'ercreate-link audit/execution; eski smokeRows korunur. SQL tek başına odak/dönüş kanıtı değildir. | Form/return/retry source/component geçmişi korunur. | L01 TRACE_DERIVED: create→list/detail→B gizlilik→candidate/link→B list/detail/returnfocus→A candidate/detail aynı.13 screenshot+trace var; normal network/measurementJSON yok. | **RUN_FAILED / PARTIAL gözlem.** 19:29:09Z tek koşu retry0/skip0; final net.quiesce30s/2outstanding FAIL. Odak izi veya başarılı STRICT/cleanup, başarısız testi PASS yapmaz. |

### 9.1 Backend exact-source kanıt kaydı

**Tarihsel080e7a1:** [CI34538870747](https://github.com/sinangym/GoymenERP/actions/runs/34538870747), SQL75/RCSI10, skip0; C-01 ve UI golden önceki exact-source kanıtıdır. James'in o SHA'daki P2'li incelemesi tarihsel koşullu kayıttır.

**Korunan880cae8:** [CI34571306671](https://github.com/sinangym/GoymenERP/actions/runs/34571306671), [UI golden34571306701](https://github.com/sinangym/GoymenERP/actions/runs/34571306701), [C-01 browser34571306722](https://github.com/sinangym/GoymenERP/actions/runs/34571306722) tamamlanmış success. CI'nın backend/frontend/OpenAPI/SQL/container işleri ile iki ayrı workflow toplam yedi kontrolü oluşturur. Backend dalındaki frontend işi #233'ün yeni protocol implementasyonu değildir.

Önceki 5b3c8d8 tesliminde doğrudan okunan, bu tur yeniden çalıştırılmayan [SQL job103173794207](https://github.com/sinangym/GoymenERP/actions/runs/34571306671/job/103173794207):

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

#### 9.1.1 CRO-AC-009: korunan gerçek SQL alt varyantı

Kaynak: [#234/2b21e3b named fact][cro009-source], `RolelessInactiveAccount_StaleVersionAndPermissionGatesRemainEnforced`; `[SqlServerFact]` ve mevcut `[Trait("AcceptanceId", "CRO-AC-009")]`. Yeni acceptance kimliği değildir.

| Başlangıç / işlem | Gerçekte assert edilen sonuç | Oluşmaması gereken etki |
|---|---|---|
| A şirketinde roles=None/0 hesap oluştur; yetkili writer expectedVersion1 ile pasifleştirir. |204; aynı hesap roles0, inactive, version2; ilgili audit +1. |Rolün legacy değere çevrilmesi veya pasifliğin rol yokluğu ile aşılması. |
| Aynı pasif hesaba expectedVersion1 ile reactivation dene. |409 version_conflict; expected1/current2; Party/account/audit snapshot değişmez. Terminal409'un durable idempotency kaydı oluşturması izinlidir. |Aktifliğin, version'ın, business kaydın veya audit'in değişmesi; bütün durable etkileri körce0 şart koşmak. |
| Aynı stale key ve yeni correlation ile replay. |409 ve replay/orijinal correlation başlıkları; afterStale snapshot bütünüyle aynı. |İkinci business/audit etkisi veya ikinci sonuç gibi davranma. |
| Viewer ve yabancı-company aktörü, hem version1 hem2 ile dener; viewer eski kaydedilmiş key'i de kullanır. |403; body'de currentVersion veya accountId yok; recorded key yetkiyi aşmaz; snapshot değişmez. |Version/kimlik sızıntısı, permission grant veya business/audit/durable değişikliği. |
| Reader detail GET, sonra yetkili writer expectedVersion2 ile aktifleştirir. |GET roles0/inactive/version"2" ve no-write; yetkili204 sonrası aynı hesap roles0/active/version3, toplam ilgili audit +2. |GET'in hesap açması/aktifleştirmesi, izinsiz reactivation veya tekrar audit. |

**Önceki teslimde doğrudan okunan gerçek SQL kanıtı:** [CI34604875177 / job103280752609][cro009-job]. Gerçek checkout `45dc98dcd21590be24916a36c768bce6d45ed1a9` = `2b21e3bcc55518f6bf6c993f7e9738ffbb4fd284` into `880cae838e5d2f01a988f1e4a89afbe67858f48c`. Bu test-merge ne main060 ne de daha yeni e35/323 diye yeniden adlandırılır.

- 13:38:02Z: gerçek .NET SQL koşumu **76/76 PASS, failed0/skipped0**.
- 13:39:38Z: gerçek RCSI koşumu **10/10 PASS, failed0/skipped0**.
- 13:33:27Z unittest/verifier çıktısındaki MOCK SQL satırları kabul kanıtına dahil değildir.
- Envanter69 discovery/76 execution'a çıkmıştır; eski zorunlu testler ve skip-zero/omission sınırı korunur.
- [Koordinatör][co-record], ayrıca aynı kaynak ön-commit named CRO009 TRX1/1, SDK103/103 ve architecture253/253 dosyalarını ve hashlerini okuduğunu bildirir. Bu ayrı yerel TRX/hash aktarımıdır; BUSINESS bu dosyaları yeniden çalıştırmış veya ham TRX/hashleri yeniden üretmiş sayılmaz. Kaynakta verilmeyen yeni hash uydurulmaz.

**Hüküm:** yalnız önceki özel REAL_SQL boşluğu kapalıdır. CRO-AC-009 toplamı **PARTIAL**, active-state browser/entegre alt kabul **NOT_RUN**. Bu fact ENV_READY veya tüm Cari/production kabulü değildir; gerçek ortamın sonraki yaşam döngüsü §9.5'tedir.2b'nin gerçek sonucu1d/e35/323 veya daha yeni FE başına otomatik taşınmaz.

### 9.2 Frontend ve browser kanıt sınırı

**Güncel yayımlanmış ve gerçek koşumda kullanılan baş `6bfc21eec43f02f3c8f2e0d86fd124c0f387bff4` (§9.5).** Aşağıdaki c118/ca19/ef kayıtları tarihsel kaynak ve kanıt düzeyleriyle korunur. [9a makbuzu][fe-handoff], [07a teslimi][fe-fix], [ef teslimi][fe-final] ve önceki6f kapanışları yeniden görevleştirilmez. Import/form, helper, component, browser ve final kabul farklıdır.

| Kanıt | Exact kaynak / doğrulama düzeyi | Kanıtlamadığı |
|---|---|---|
| Onaylı import ve uygulama | [3703862][fe-import], ebeveyn6f2dfe8+d46;83 seçilmiş yol,82 birebir blob ve contract.ts uzlaştırması koordinatörce doğrulanmış. c118 dört reader0..15, rolsüz create/link ve gerçek probe/command/attempt bağlantısı içerir. |Donör WIP'i alınmış sayılmaz; import veya kodun varlığı gerçek API/browser kabulü değildir. |
| Tamamlanmış form/component incelemesi | [Dirac][fe-dirac] ve [Averroes][fe-averroes], parent8f8e241→exact c118, temiz başlangıç/son head: unknown'ın sonraki compatibility/local ret ile silinmemesi, create probe auth/tipli hata ve link submit readiness bağlantısı kapalı. Create DOM'da same-key/unknown, link disabled/POST kontrollerini helper'dan ayrı değerlendirirler. |İncelemeler test çalıştırma veya insan kabulü değildir; o SHA'da üç retry yolu bırakmıştır. |
| c118 CI | [CI34604086840](https://github.com/sinangym/GoymenERP/actions/runs/34604086840), [frontend job103278176900][fe-form-ci], C-0134604086871 ve UI34604086953:7/7 ve25 dosya/458 PASS koordinatörün gerçek-log okumasıdır. Checkout `37e32039baa10c809f73f676a31dd8c318aa910c` = c118+main060. |458'in tamamı component değildir; bu tur yeniden koşturulmadı. #234/2b ya dae35 ile tam bütünleşik bileşim değildir. |
| c118'dе saptanan üç somut yol | Dirac: pending retry GET sırasında bırakma sonrası geç POST; Averroes: abandoned link retry sırasında query/cancel-revert'in eski readiness ile POST üretmesi; ikisi: unknown varken üst uyarının reload/“hiçbir istek gönderilmedi” demesi. Severity Dirac1P1+1P2, Averroes2P1; ortak reload bulgusu mükerrer sayılmaz. |Tarihsel bulgular sonraki kaynak kapanışlarına rağmen güncel implementation işi olarak tekrar dağıtılmaz. |
| ca19 düzeltmesi ve sonraki kapanış | [Yazar teslimi5637323519][fe-retry-fix]: retry generation/read-sequence/attempt-aware uyarı,4 yeni hook/DOM testi ve462 PASS raporu. Koordinatörün sonraki devrindeki [Dirac][fe-retry-review-1]/[Averroes][fe-retry-review-2] kapanışları ile güncel6b exact kaynak incelemeleri ayrıca korunur. |Yazar/test/review kanıtı gerçek live koşum değildir. Bu kapanışlar §9.5'te sonradan ortaya çıkan harness failure'ını örtmez; eski kapsam yeniden incelenmez. |
| Korunan ef helper/transport/component | Ef CI'da saf yardımcı32, stub-transport21, liste component84, detay19; toplam18 dosya/324. Gerçek checkout `ee6a2deaad920522332f5c0bbcb070b1a3f5d620`, [job103192346328][fe-ci-job]. [İki][fe-review-1] [dar inceleme][fe-review-2] önceki iki P2'yi yalnız dört dosyalık farkta kapatmıştır. |Mock body rejection gerçek stream değildir; eski helper sonucu yeni form/retry kapanışının yerine geçmez. Eski kapsam yeniden incelenmez. |
| Korunan C-01/protokol browser | [Ef yazar teslimi][fe-final]:880+ef, fixture54/55, B-C01-001…01010PASS veprotocol8PASS, teardown raporlu. Ef CI34577227055/C-0134577227048/UI34577227054 kendi7/7 sonucudur. |Create→list→detail→B-link→dönüş/failure/retry kanıtı değil; ef sayıları c118/ca19/6b üzerinde tekrar çalışmış gibi gösterilmez. C006 ENV_READY sayılmaz. |

**Tarihsel zincir korunur:**9a6b43d için302 test, CI34573865458/C-0134573865597/UI34573865578 ve yazarın880+9a yerel8e83fb1 list10/protocol8 raporu kendi kaynağına aittir.07a4af2 için319 test ve [CI34575684274](https://github.com/sinangym/GoymenERP/actions/runs/34575684274)/[C-0134575684283](https://github.com/sinangym/GoymenERP/actions/runs/34575684283)/[UI34575684265](https://github.com/sinangym/GoymenERP/actions/runs/34575684265)7/7 ayrı geçmiş kanıttır; o tur browser tekrarlanmadığı [teslimde][fe-fix] belirtilir.07a incelemelerinin iki P2'si tarihsel olarak korunur, ef kapanışı geriye dönük koşulsuz eski onay üretmez.

**Tarihsel424a586:** Node24 suite267/267, C-01 list10/10 ve ayrı16/16 liste davranışı raporu kendi kaynaklarında korunur. [Eski devir dosyası](https://github.com/sinangym/GoymenERP/blob/424a586fc07efa291db4b37866ba5cce54fb7443/src/frontend/goymen-erp-web/CARI_FRONTEND_DEVIR_2026-09-10.md) ve eski #231266/266 kaydı tarihsel kapsamındadır. Node26'daki iki yerel hata yeniden mandatory CI arızası diye açılmaz.

Liste dönüşü artık yalnız uyarı değildir: ilk sayfa taze okunur, gerekirse en çok dört ek sayfaya güncel cevap cursor'ıyla yürünür; eski cursor replay edilmez. Bu sınır mevcut implementation parametresidir, yeni genel işletme politikası değildir. Kayıt bulunursa odak döner; bütçede bulunamaması gerçek dönüş başarısı değildir. Tarihsel C-01 bunu kanıtlamaz. Yeni gerçek L01'de returnfocus TRACE_DERIVED görülmüş, fakat final quiescence başarısız olduğundan tam dönüş/yolculuk PASS ilan edilmemiştir (§9.5).

### 9.3 Donor ve kontrollü browser sınırı

#228 recipient `HANDOFF-ACCEPTED`5626181170 vardır; tekrar istenmez. [HUMAN-OWNERSHIP-TRANSFER5632970696][ownership-approved] verilmiştir; [83 yol/blob önerisi][fe-ownership] artık cevapsız bir soru değildir. Aynı Claude/#233 aktif tek yazardır; [3703862 importu][fe-import] yayımlanmıştır. Karar donor HANDOFF-RELEASED diye imzalanmaz. Eski dal/geçmiş/kanıt korunur; unpublished LENOVO C023/F024/F025 kaynakları devralınmış değildir.17 backend/test/lifecycle/CI yolu aynı Backend sahibinde kalır. Bu sahiplik kararı #229/B/#232 insan kabulü, merge veya production açmaz.

[Koordinatörün controlled evidence kaydı](https://github.com/sinangym/GoymenERP/pull/228#issuecomment-5624268419): run34103473977 artifact10011561394; SDK80/80, helper54/54, controlled Chromium17/17; test-merge checkout2ebcbc7 ile d46 kaynak ağacının eşliği ayrı doğrulanmıştı. `persistentServerReplayClaimed=false`, `erp_accepted=false`; bu sayılar gerçek roleless server/browser birleşimi veya yeni protocol kabulü değildir. CSS/DPR eşdeğerleri gerçek browser zoom/screen-reader konuşması sayılmaz.

### 9.4 C006: yayımlanmış hazırlık, source-binding ve runtime sınırı

[Merkezî kayıt][co-record]: önceki CreateProcess reddi aynı Backend görevi ve yerleşik sınırlı onaylarla çözülmüştür; başka ajan/araçla aşılmamıştır. Mevcut CODEX-BACKEND `01a0865c-1990-7783-8aee-4aee15618b31`, #228/d46 kaynaklı17 yolun tek sahibi olarak manifesti üretmiş ve #234'ü yayımlamıştır. Eski “hiç yazılamadı/manifest yok/özel SQL yok” hükümleri tarihsel kalır; her yeni araç ret/onayı yine kendi kapsamında ele alınır.

| Ayrı durum | Gerçek kaynak / kanıt | Kalan sınır |
|---|---|---|
|17 kaynak ve manifest | [#234][c006-pr] provenance: bütün17 çalışma kopyası byte-eşliği raporlu; source commit/path/Git blob/ham SHA256/boyut. İlk yerel CRLF manifest SHA256 `460fbb5f26533098b87743c3056c821d90cde2d3bae9d3c194547f95aa92a9a6`; yayımlanmış LF blob SHA256 `aa958fe261d29097c92129dce5451e6271556ea7a1e67f7f03ba458c1b66ae4e`. |İki serileştirme aynı ham hash değildir. Manifest, çalışan host veya memory-image attestasyonu değildir; eski WIP/DPAPI korunur. |
|2b CI/BackendVerify | [CI34604875177][c006-ci], C00634604875174, C-0134604875197, UI34604875279 toplam8/8 koordinatörce doğrulanmış. C006 artefaktı10265439602: checkout45dc98d; SDK103/103 skip0, scan11dosya/0bulgu; `publish_ready=false`, `erp_accepted=false`, Chromium/helpers NOT_RUN. |Bu tarihsel BackendVerify, sonraki gerçek ENV_READY'nin kendisi değildir. Immutable8f scanner kaynağı final frontend entegrasyon pini değildir. |
|CRO009 ve diğer yerel testler | §9.1.1 gerçek SQL76/RCSI10 önceki doğrudan log kanıtı; named1/1 ve architecture253/253 ayrı koordinatör TRX aktarımı. |Architecture/SDK/mock çıktısı gerçek SQL veya browser diye sayılmaz. |
|2b→1d kaynak P2'si |2b'de untracked managed build-input SourceBinding P2 açıktı. [1d022d8 teslimi][c006-source-fix] üç dosyalık düzeltme; koordinatör hedef11 test ve iki beklenen BackendVerify reddini doğrulamış, yazar Ampere/James kapanışını aktarmıştır. James o SHA'da ayrı publisher build-hash bağlama P2 bırakmıştır. |Tarihsel2b/1d review'ları geriye dönük kusursuz onaya çevrilmez; beklenen negatif ret ortam PASS'i değildir. |
|e35 kaynak/publisher kapanışı | [e35ad38 teslimi][c006-publisher-fix], parent1d: test-output DLL envanteri/önce-sonra hash ve runtime-test eşliği, tek validated summary buffer. [Aynı iki bağımsız AI reviewer'ın exact e35 kapanışı][c006-publisher-review] P0/P1/P2=0; sonraki koordinatör devrinde kendi8/8CI tamamlandı. |Reviewer testleri NOT_RUN;2b SQL76 e35'e taşınmadı. Eski kaynak/P2 kapanışları yeni implementation görevi değildir. |
|323 ve gerçek ortam yaşam döngüsü | [323 teslimi][run-be-delivery], kendi8/8CI ve [Ampere][run-be-review-1]/[James][run-be-review-2] exact-source incelemeleri; matching local121SDK+18publisher,19tested/18runtime DLL hashleri koordinatörce doğrulandı. Gerçek ENV_READY19:15:24Z, STRICT/cleanup §9.5. |Kaynak/CI/build başarısı L01FAIL'i kapatmaz. Temizlik sonrası E tarihsel ve readyForBrowserNow=false; yeni frontend düzeltmesi/yeni tuple olmadan bu ortam tekrar kullanılıyor denmez. |

Mevcut dört-capability C006 yeniden kullanılır; ikinci lifecycle yazarı, C-01 golden genişletmesi veya yeni tek-node routing/gateway/loadbalancer işi yoktur. Kaynak P2 kapanışı, CI başarısı ve runtime kabulü farklı kapılardır. Eski LAPTOP/05b/run/DPAPI pinleri körce gevşetilmez; başka build'in başarılı receipt'i yerel runtime'a ait kanıt olamaz.

### 9.5 Tek gerçek kabul paketi: L01 RUN_FAILED, dar PASS'ler ve cleanup

Bu bölüm yalnız `c006-303760997c6f` / `3a6f9f42-2d52-4874-95d0-7cff415aaf15` koşumudur. Kaynak, koordinatörün bu BUSINESS görevine verdiği tek paket ile [merkezî kaydın 11 Eylül 19:43 UTC kesiti][co-record]dir. Koordinatör ham dosya/hash/typedRows/cleanup kontrolünü bildirmiştir. BUSINESS yayımlanmış kaynakları ve devri okumuştur; yerel Windows artefaktlarını veya trace'i kendisi açmış, hashlerini yeniden hesaplamış ya da testleri yeniden çalıştırmış gibi raporlamaz. Aşağıdaki SHA256 değerleri bu devrin ham-byte pinleridir.

#### Exact kaynak, build ve ortam bağı

| Bağ | Bu gerçek koşumdaki değer / sınır |
|---|---|
| Frontend | #233 `6bfc21eec43f02f3c8f2e0d86fd124c0f387bff4`; ayrı frontend CI checkout'u `279d7fb2739bd4b4db3d152a4d4873cd3047e683` =6b+main060, canlı run checkout'u diye yeniden adlandırılmaz. |
| Compiled/acceptance | #234 `32392ca97e3ea74a4300855f015ee65febd66f87`; API dependency #230 `880cae838e5d2f01a988f1e4a89afbe67858f48c`. |
| Backend source tree | `4e0b1a5d6a1116ee2ba76df132aeec6e8b9c99d5`; src/backend ağacı, process-memory kanıtı değil. |
| Migration source | SHA256 `1ec8222d998cb59891f437fc1ce4f62f954203508f3b2cffd4a00d97b1933e44`; **live DDL hash'i değildir**. |
| OpenAPI | SHA256 `062c06175916295bc97a438adea6fd01cdb608413c58501aa62208f87038a829`. |
| Fixture | SHA256 `ec44853417e251f5f39adbb18d1e62f50d3bd8b3959c895eb7556795101f3095`. |
| FE raw diff | 1053010 byte; SHA256 `b9e9354e971da26bbd5a0743dcefc78c2f3b4a7e4cbb6d59d74793c7c3b25c11`. |
| Dış contract | Tek yazarı Backend; FE kendi hash'ini kendi diff'ine gömmedi. Tam yol ve hash aşağıda. |
| Ortam | Gerçek ENV_READY **19:15:24Z** üretildi. Tek-node loopback API25080/SQL25433/UI25173; mevcut AcceptanceFactory/source-handoff koşulları. Yeni UI-port manifest alanı veya gateway varmış gibi kabul edilmez. |
| Şimdiki hazır oluş | Cleanup sonrası **readyForBrowserNow=false**; environment/ENV_READY tarihsel kanıttır, yeniden yazılmaz. `erpAccepted=false`, `erpEndToEndComplete=false`. Synthetic readyForBrowser:true fixture gerçek ENV üretmiş sayılmaz. |

Yerel kanıt konumları (GitHub dosyası veya sandbox linki değildir):

```text
RunRoot = C:/src/GoymenERP-cari-acceptance-envelope-20260911/artifacts/cari-acceptance-006/c006-303760997c6f
Contract = C:/src/GoymenERP-cari-acceptance-envelope-20260911/artifacts/cari-acceptance-006/inputs-c006-303760997c6f/contract.json
L01Root = C:/src/GoymenERP-claude/artifacts/c009-l01-303760997c6f/L
L02Root = C:/src/GoymenERP-claude/artifacts/c009-l02-303760997c6f/L
```

| Artefakt / bağ | Devralınan ham SHA256 |
|---|---|
| Contract | `0fbeef4e8ad034d98702c5b6013fc3991683b2caeca0b2ce2334ffb91a6a5a43` |
| RunRoot/environment.json | `814f296ab8becb5c65a7626104ed3c584643917a46f5b2ff7d6a1f8bea8d4bac` |
| ENV_READY | `e1a21256e3888e23c842493ece92cbbd310fdf0e4c74a74e53aced7f8f4eb480` |
| Source-binding | `8987451290fd690a27fcb81cd1a25e7f880aaf65ba46406891c9c4cc2cdf3f89` |
| RunRoot/intervals/legacy-protocol-result.json | `a8b5d5e6a0a3ad1c8bbded6f8540e6c4ea96811e24ccd3cadaf15539ecf88c57` |
| L02 results | `dcbcc3831a5b56871232c623a8e490552a81fec1343e295c1e387f768bd20ed5` |
| RunRoot/intervals/l02-result.json | `ecd46efb0af9f73b566657d3bcc93236a2151f48ea49f8d612330e71bd914ec5` |
| L01 results | `c27edf8288b41bd2ef679d35a2a6713b59c4b6565f687a32cb6c0ca3a5662de4` |
| L01 JUnit | `f3b5588e13de0faa2c14107734f025328889c486b8047d726b7f2390a0669252` |
| L01 manifest | `3ea46f0c592a5cf170d87f45d5ebcd4ccc131ebf3f7df7489a203b7ba105a9f3` |
| L01 trace | `ba344606b9bda8c2766badd1ebd90508cfe19304997f4524c5546509ef6b800f` |
| RunRoot/intervals/l01-strict-verify-output.json | `6d394760a23a828d9e77b52ee50d595155a49a19bcd4f0f3257ee04a14a84cc2` |
| RunRoot/intervals/cleanup-verification.json (11152 byte) | `a0e4dba04c7eda72156f00282a6516e9164dc71d32c9416fa8afb20e97988353` |

#### Çalıştırılmış ölçümler ve ölçülmemiş sınırlar

**Legacy HTTP8 — dar PASS (CRO-AC-010/011).** Gerçek create/link × roles omitted/zero × missing/unsupported declaration denendi; tam `409 client_contract_required/unsupported` görüldü. Bütün invocation aralığında altı sayaç ve orderedRows değişmedi. Bu gerçek HTTP ve interval SQL kanıtıdır; eski backend binary çalıştırılmadı, capability-off bir write-block kabulü diye kullanılmadı. Bütün invalid-wire veya iki yönlü eski/yeni binary matrisi kapanmaz.

**L02 — dar gerçek browser PASS (CRO-AC-004/008/010).** [Mevcut L02 kaynağı][run-l02-source], retry0/skip0; pre-L69ok/0fail,13API/0POST, final inFlight0. Backend bütün L02 invocation aralığını ölçtü: before/after ham içerik eşliği, altı sayaç ve orderedRows sıfır delta. A/B'de candidate0 ve selectionfalse olduğundan seçili adayın şirketler arasında taşınması ölçülmedi. Held GET, B'nin ilk GET'inden önce abort oldu; bodyunavailable. Bu nedenle başarılı geç body'nin sızmaması veya yeni write grant'ine neden olmaması **NOT_MEASURED**. “Başarılı geç yanıt da güvenli” veya tüm CRO010 PASS sonucu çıkarılmaz.

**L01 — gerçek RUN_FAILED (CRO-AC-001/003/005/007/008/012).** [Mevcut L01 kaynağı][run-l01-source] ile19:29:09Z tek koşu yapıldı; runner retry0/skip0. Sondaki `net.quiesce()`30 saniye içinde kapanmadı,2 outstanding gözlemle FAIL oldu. Bu artık NOT_RUN değildir; fakat tamamlanmış başarılı bütünleşik kabul de değildir.13 screenshot ve trace korunuyor; normal network/measurement JSON'u hata nedeniyle yok. Runner retry0, aşağıdaki kullanıcının açık same-key iş-komutu tekrarlarının yapılmadığı anlamına gelmez.

**TRACE_DERIVED:** gerçek A roleless create→A list/detail→B bağından önce listede görünmezlik/detail404→candidate/link→B list/detail/gerçek return focus→A candidate/detail aynı kimlik. Her iki command'de gerçek upstream201'in teslimi kaybettirildi; unknown korundu; command'in kendi yeni GET200+ack'i ardından açık aynı-key retry201/replaytrue ve byte-identical response body gözlendi. Bunlar mock server başarıları değil, başarısız gerçek koşunun izinden türetilen gözlemlerdir. Normal ağ/ölçüm raporu yokken bağımsız başarılı test maddeleri veya tüm yolculuk PASS'i olarak sunulmaz.

**CRO-AC-004 HTTP negatifleri:** ayrı APIRequestContext'ten viewer/foreign kapsamında2GET403+2POST403 gerçek HTTP gözlemi vardır. B-link öncesindeki404/list görünmezliği ayrıca L01 izindedir. Bunlar browser logout, oturum sonlandırma, yetki iptali veya token revocation testi değildir; L01global SQL sonucundan her403 çağrısı arasında alınmış ayrı snapshot varmış gibi sonuç türetilmez.

**STRICT global SQL — gerçek PASS; L01FAIL'den ayrı.** Aynı Backend `ExpectedNewParties=1` ile19:35:25.099–19:35:27.735Z doğruladı; exit0/passedtrue. Altı sayaç baseline `[1,2,1,1,1,1]` → current `[2,4,2,2,2,2]`: +1Party, +2roles0 A/B account, +1create audit, +1link audit, +1create execution, +1link execution. Yeni Party `9a2ac59f-2954-4fef-9ffb-e973d5379cfb` version1; A account `70a72fbe-4c5d-4a1e-8da4-651f555677c1`, B account `ebf687de-c0dd-47e1-909c-f53b42336428`, ikisi roles0/version1. Önceki smokeRows korunmuştur.

Bu **global gerçek tek iş etkisi** ve actual replay gözlemi birlikte kaydedilir. İlk POST commit'i ile retry arasındaki ayrı SQL snapshot **NOT_MEASURED**; varmış gibi raporlanmaz. Bu sınırı belirtmek, yeni bir ara-SQL zorunlu kabul kapısı icat etmek değildir; mevcut STRICT sözleşmesi değiştirilmez. CRO005/007'de gerçek replay ve tek global SQL etkisi ölçülmüş olsa da L01RUN_FAILED ve toplam PARTIAL korunur.

**Owned cleanup — gerçek PASS.** Aynı Backend stop19:36:07–09, release-check09–11, owned teardown11–13Z adımlarını exit0 ile tamamladı. Son salt-okunur kontrol19:39:16–17Z: PID11388gone;25080/25433/25173listener0; owned container/volume/network0; privatefilegone;18 önceki proof hash'i değişmemiş. Koordinatör beş ham dosya/hash/typedRows/cleanup sonucunu doğruladı. Temizlik başarısı test başarısı değildir; E tarihsel ve L01FAILED kalır. Bu koşu tüketilmiş/temizlenmiştir; reset veya rerun yapılmadı.

**Bu pakette ölçülmeyenler:** gerçek logout/session exit/revocation/API-origin değişimi; başarılı late body/leak/grant causality; dolu aday seçiminin taşınması; gerçek pending-retry abandon/query yolları; iki yönde eski/yeni binary; malformed-stream recovery ve active-state browser. Ack'li500/sonraki compatibility409 unknown varyantlarının eski helper/component kanıtı, bu gerçek run'da yapılmış ölçüme dönüştürülmez. NOT_MEASURED altları controlled veya tam PASS diye yükseltilmez; CRO009'un SQL kapanışı active-state browser kabulü değildir.

**Açık somut teknik bulgu ve aynı sahipte sonraki adım:** full-page goto ile şirket GET'inin çakışması iki isteğin terminal gözlemini eksik bırakmış; hata halinde evidence flush gerçekleşmemiştir. Aynı Claude yalnız bu dar harness düzeltmesine yetkilidir. `framenavigated` olayını request-finished sayıp `accepted:true` üretmek veya quiescence/capture kapısını gevşetmek kabul edilmiş çözüm değildir. Bu kesitte yeni düzeltme/FE head yayımlanmış değildir. Yeni fark→aynı Dirac/Averroes exact-head review+freshCI→aynı Backend yeni local matching-build/final pin/izole tuple→gerçek browser/SQLdelta/teardown sırası korunur. Yeni gateway, işletme politikası veya Sinan müdahalesi gerektiren bir karar açılmaz.

## 10. Frontend implementation sözleşmesi

### 10.1 Dört reader

Aynı #233 sahibi onaylı83 kaynak devriyle list/detail/mutation/candidate'i required integer0..15 contract'a uzlaştırmıştır; mevcut form adayı §9.2'de kaynaklıdır. 15 kabul,16 ret; response null/missing→zero yok. Identifier, şirket, PartyKind, aktiflik ve version korumaları değişmez. Yeni decoder ailesi veya paralel create/link uygulaması açılmaz. Dört-reader gözlemi §9.5'te kısmi gerçek kanıttır; L01FAIL tam kabulü engeller.

### 10.2 Form, düğme ve hata

Mevcut create/link formlarındaki mandatory-role validation ve metin kaldırılmıştır; başka zorunlu sınıfa dönüştürülmez. Firma/Kişi, hukuki ad, opsiyonel ticari ad ve salt okunur seçili şirket korunur. Kaynak roller hedefe kopyalanmaz.

Permission kaynaklı aksi mevcut non-disclosing kuralıyla sunulmaz; yetkili kullanıcı için uyumluluk doğrulanamaması işlevin çalışıyormuş gibi gösterilmesine izin vermez. Güvenli Türkçe açıklama ve mevcut error/dirty/pending/focus davranışı korunur. Teknik başlık kullanıcıdan elle istenmez.

Yükleniyor/GET hatası/uyum doğrulanamadı durumunda write kapalıdır. Yalnız valid GET+ack açabilir. Command başarısı ancak kendi tam response ve teyidiyle gösterilir; belirsizlikte başarı bildirimi, formu temizleme veya detail navigation yapılmaz. Hata sınıfı başlık yokluğundan uydurulmaz. Unknown/abandoned girişim varken genel sayfa uyarısı da key kaybettiren reload veya yanlış “hiçbir istek gönderilmedi” ifadesi kullanmamalıdır. Önceki ca19 kapanışı yeniden görevleştirilmez; gerçek paketin ölçüm sınırı §9.5'tir.

### 10.3 Bağlama bağlı hazırlık

`(apiOrigin, actor/session, selectedCompanyId, generation)` değişirse readiness düşer. Taze GET cevabı aynı tuple'a ait olmalıdır. Gerçek sunucu teyidi, response decoder ve mevcut authorization ayrı zorunluluklardır. Transport header eski source-account görünürlüğünü target link yetkisine çeviremez. Ef issue-time context/abort guard'larının dar kapanışı §4.2'dedir. Gerçek retry ön kontrolü bırakma/query değişiminden sonra eski hazırlık üzerinden komut gönderemez; kendi taze başarılı okumasının kanıtını gerektirir. L02'nin boş aday/abort varyantı bütün context koşullarını ölçmüş sayılmaz.

### 10.4 Belirsiz girişim

§4.5 tek attempt/key/payload/context kaynağıdır. Mevcut commandAttempt/useCommandAttempt, dirty/pending/company/logout fence ve accessible retry kaydı korunur. Daha sonra gelen409 önceki belirsiz girişimi yok saydıramaz. Fresh valid GET+ack sonrasında yalnız explicit same-key retry; otomatik yeni key yoktur. Eski yardımcı/form düzeltmeleri kapalıdır. §9.5 actual upstream201 kaybı→unknown→aynı-key replay ve global tek SQL etkisini kaydeder; L01FAIL ve ölçülmeyen failure/stream/context altları ayrıca kalır.

## 11. Kalan görevler — mevcut sahiplik ve görevlerin devamı

Aşağıdaki CB/CF/CO/GB etiketleri mevcut #232 görev satırlarıdır; yeni issue, bağımsız görev sistemi veya paralel writer atanması değildir.

### CODEX-BACKEND — #230/#234 mevcut sahibi

**CB-1 — korunan880 kapanışı.** §4.4/§9.1: eski P2, main conflict ve SQL75/RCSI10 kanıtı yeniden açılmaz; eski kapsam için yeni review/test turu istenmez. Yeni kaynak yalnız kendi kanıtına bağlanır.

**CB-2 — CRO009 REAL_SQL teslimi mevcut.** §9.1.1 named None+inactive+stale+permission fact'i ve2b gerçek SQL76/RCSI10 ile özel boşluk kapanmıştır. Aynı testi yeniden yazma/roles3 fixture'ını yeniden analiz etme. Browser/genel CRO009 PARTIAL kalır; bu run'da active-state browser NOT_MEASURED'dır.

**CB-3 — aynı C006'nın yeni koşuma geçiş sınırı.** Manifest/source/publisher ve323 matching-build/ENV_READY, STRICTglobaldelta, stop/release/teardown teslimleri §9.4/9.5'tedir; eski teknik ret ve kapalı P2 yeniden açılmaz. Tüketilmiş run'ı reset/rerun etme. Aynı Claude'un dar düzeltmesi ve exact-head review/freshCI sonrası yeni kabul tuple'ını, kendi local matching-build/final pin/ENV_READY ve son SQLdelta/teardown kanıtını aynı sahip sağlar. Ara commit→retry SQL ölçülmediği için yeni zorunlu kapı eklenmez; STRICT sözleşmesi korunur. Yeni lifecycle/gateway veya permission politikası yok.

### CLAUDE-FRONTEND — #233 mevcut sahibi

**CF-1 — eski kapanışları koru.**9a/07a/ef/6f, c118 ve ca19'da kapanmış kapsam yeniden görevleştirilmez. Önceki tamamlanmış Dirac/Averroes ve6b exact-head incelemeleri kaynaklarında kalır. Bunlardan sonra ortaya çıkan L01 harness failure ayrı gerçek bulgudur.

**CF-2 — sahiplik ve import tamam.** [İnsan kararı][ownership-approved] ve [3703862][fe-import] geçerlidir;83 yol için yeniden karar/makbuz bekleme. Dört reader, rolsüz formlar ve gerçek command kaynakları aynı dalda mevcuttur. Eski donor/WIP/17Backend sınırı korunur; ikinci uygulama açılmaz.

**CF-3 — mevcut dar harness düzeltmesi.** [Koordinatörün gerçek run devri][co-record]: full-page goto/şirket GET çakışması, iki eksik terminal gözlem ve hata halinde evidence flush. Aynı Claude mevcut kaynağı düzeltir; terminal/capture/quiescence kapısı gevşetilmez, navigation olayından yapay tamamlanma üretilmez. Bu kesitte yeni implementation yayımlanmış değildir. Yeni farkın mevcut iki bağımsız reviewer ve freshCI kanıtı gereklidir; eski transport/SQL incelemesi tekrar istenmez.

**CF-4 — same-key gözlemini koru, kapsamı büyütme.** §9.5 gerçek iki upstream201 kaybı, unknown, kendi tazeGET+ack, explicit aynı-key replay ve global tek-etki ölçümünü koru. Bunlar L01FAIL içinde kısmi kanıttır. Ölçülmeyen pending abandon/query, sonraki500/409, stream veya session varyantlarını kapandı sayma; kapanmış helper'ı tekrar yazma. Yeni koşum mevcut kabul kapsamını gerçekten ölçmeli, eski başarısız artefaktları değiştirmemelidir.

**CF-5 — yeni izole koşumda mevcut browser kabulü.** Aynı Backend'in yeni exact tuple/localmatchingbuild/manifesti sonrası mevcut L01/L02 ve ilgili kontrollü kaynaklar kullanılır; yeni kabul kimliği yok. Run/instance/source/build/fixture ayrımı, API/UI bağları, gerçek SQLglobaldelta ve teardown birlikte raporlanır. L01RUN_FAILED ile L02darPASS ayrımı korunur; controlled response eski binary veya gerçek durable replay diye etiketlenmez. Bu run'a sonradan PASS eklenmez.

### CODEX-ORCHESTRATOR

**CO-1.** Mevcut #230/#234 Backend ve #233 Claude sahipliği korunur; yeni parallel spec/backend/create-link writer yok.

**CO-2.**83 yolun insan kararı ve importu tamam; recipient/donor makbuzu tekrar istenmez veya donor adına üretilmez.17 Backend/WIP sınırı ve #229/B/#232 insan kapıları ayrı kalır. Bu BUSINESS teslim merkezî kayda paralel yazmaz.

**CO-3.**880 eski kanıtı,2b/45dc98d CRO009SQL, önceki source/publisher/retry kapanışları ve gerçek6b/323/880 run birbirine otomatik taşınmaz. L01RUN_FAILED, trace-derived kısmi davranış, L02darPASS, legacyHTTP8 ve STRICTglobalSQL/cleanup ayrı sonuçlardır. Yeni sayılar ERPaccepted veya insan onayı değildir.

**CO-4.**Mevcut run tüketildi/temizlendi; E tarihsel ve readyForBrowserNow=false. Aynı Claude darfix→aynı reviewer/freshCI→aynı Backend yeni matching-build/finalpin/ENV_READY→gerçekbrowser/SQLdelta/teardown sırası sürer. Yeni FE düzeltmesi henüz yayımlanmış gibi gösterilmez. Tek-node için gateway/drain işi yok; karma havuz yalnız doğrulanırsa §4.6 kapısıdır.

**CO-5.**Final K3 exact kaynaklar, uygulanabilir fresh CI/SQL/browser/failure ve bağımsız review kapılarını doğrula. Kaynak review kapanışı runtime kanıtı değildir; yeni harness failure açıkken eski review0 merge kapısını tamamlamaz. Lane self-merge yok; #229/#232 insan incelemesi ve production Sinan kapısı ayrıdır.

### CHATGPT-BUSINESS

**GB-1.** Yalnız mevcut #232 dosyası exact-source kanıtla güncellenir; kanonik/shared ilerleme kaydına paralel yazılmaz.

**GB-2.** CRO-AC-001…012 ve eski CR eşlemesi korunur; gerçek run alt sonuçları bunlara bağlanır. Ölçülmeyen davranış PASS'e çevrilmez.

**GB-3.** Hemen sonraki hazır iş mevcut bütünleşik Cari kabulündeki dar failure'ın aynı sahipte düzeltilmesinden sonraki gerçek kanıttır. Aşağıdaki otomatik şirket ilişkisi yalnız önceden hazırlanmış bir sonraki pre-spec olarak korunur; implementation sırası/permission kararı onaylanmış sayılmaz. Bunun ötesinde sipariş veya genel ERP analizi açılmaz.

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

Mevcut roleless/transport Cari düzeltmesi için yeni işletme kararı gerekmiyor. `roles=0/null` tercihi tekrar sorulmaz; request/response §3'te sabittir.83 dosyanın insan sahiplik kararı verilmiştir; bunu yeniden açık soruya çevirme. Kapanmış source/publisher/retry düzeltmeleri veya yeni harness failure yeni işletme politikası değildir.

`NEEDS-SINAN-CARI-AUTO-001`: yalnız gelecekte implicit account permission kaynağı A/B/C kararı gerekir. ÖneriB, **onay değil**; mevcut Cari düzeltmelerinin ön şartı değildir. Çok şirketli piksel düzeni tüm akışı bekleten bir kullanıcı kararı yapılmaz.

Kaynak olarak [tarihli kullanıcı kararları](https://github.com/sinangym/GoymenERP/blob/e1fd69da69df36a72e559ea07163fb3f359b3c61/docs/decisions/SINAN_KARARLARI_2026-09-10.md), `docs/decisions/KARAR_SICILI.md`, `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`, mevcut Cari JIT/yetki sözleşmeleri ve öncelikli iki protocol handoff birlikte okunur. Eski #227 headerless örnekleri yeni contract'a aynen çalıştırılmaz; tarihsel kanıt olarak korunur.

#229 ve #232 insan review/acceptance kapıları açık kalır. Bu dosya yalnız gerçek paketin kaynak/test/kanıt eşlemesini günceller; görevi veya kanonik kabulü DONE yapmaz. BUSINESS yeni uygulama göndermez, backend/frontend değiştirmez, merge/production yapmaz. Yerel ham run dosyalarını root doğrulamıştır; BUSINESS yayımlanmış kaynakları ve root devrini işlemiştir. Ürün testleri yeniden çalıştırılmamıştır. L01RUN_FAILED ve temizlik sonrası tarihselENV/readyForBrowserNow=false korunur.

[be-sql]: https://github.com/sinangym/GoymenERP/blob/880cae838e5d2f01a988f1e4a89afbe67858f48c/tests/GoymenERP.Api.IntegrationTests/CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.cs
[be-write]: https://github.com/sinangym/GoymenERP/blob/880cae838e5d2f01a988f1e4a89afbe67858f48c/tests/GoymenERP.Api.IntegrationTests/CounterpartyWriteSqlServerRelationalAcceptanceTests.cs
[be-http]: https://github.com/sinangym/GoymenERP/blob/880cae838e5d2f01a988f1e4a89afbe67858f48c/tests/GoymenERP.Api.IntegrationTests/CounterpartyClientContractTests.cs
[fe-list]: https://github.com/sinangym/GoymenERP/blob/ef43700b3f5d25e8cdfc7d28a76c420e233f96fe/src/frontend/goymen-erp-web/src/cari/api.ts
[fe-detail]: https://github.com/sinangym/GoymenERP/blob/ef43700b3f5d25e8cdfc7d28a76c420e233f96fe/src/frontend/goymen-erp-web/src/cari/detailApi.ts
[fe-helper]: https://github.com/sinangym/GoymenERP/blob/ef43700b3f5d25e8cdfc7d28a76c420e233f96fe/src/frontend/goymen-erp-web/src/cari/clientContract.ts
[fe-unit]: https://github.com/sinangym/GoymenERP/blob/ef43700b3f5d25e8cdfc7d28a76c420e233f96fe/src/frontend/goymen-erp-web/src/cari/clientContract.test.ts
[fe-transport]: https://github.com/sinangym/GoymenERP/blob/ef43700b3f5d25e8cdfc7d28a76c420e233f96fe/src/frontend/goymen-erp-web/src/cari/contractTransport.test.ts
[fe-component]: https://github.com/sinangym/GoymenERP/blob/ef43700b3f5d25e8cdfc7d28a76c420e233f96fe/src/frontend/goymen-erp-web/src/cari/CariListPage.test.tsx
[fe-handoff]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5630924431
[fe-fix]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5631155345
[fe-final]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5631367978
[fe-review-1]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5631431324
[fe-review-2]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5631431518
[fe-ownership]: https://github.com/sinangym/GoymenERP/pull/228#issuecomment-5631168111
[fe-ci]: https://github.com/sinangym/GoymenERP/actions/runs/34577227055
[fe-ci-job]: https://github.com/sinangym/GoymenERP/actions/runs/34577227055/job/103192346328
[fe-c01]: https://github.com/sinangym/GoymenERP/actions/runs/34577227048
[fe-ui]: https://github.com/sinangym/GoymenERP/actions/runs/34577227054
[co-record]: https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5624159337
[cro009-source]: https://github.com/sinangym/GoymenERP/blob/2b21e3bcc55518f6bf6c993f7e9738ffbb4fd284/tests/GoymenERP.Api.IntegrationTests/CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.cs#L620-L711
[cro009-job]: https://github.com/sinangym/GoymenERP/actions/runs/34604875177/job/103280752609
[ownership-approved]: https://github.com/sinangym/GoymenERP/pull/228#issuecomment-5632970696
[fe-import]: https://github.com/sinangym/GoymenERP/commit/3703862ce28bfc5bee55248e5ca7d2ef5e46fe28
[fe-dirac]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5637138148
[fe-averroes]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5637138370
[fe-form-ci]: https://github.com/sinangym/GoymenERP/actions/runs/34604086840/job/103278176900
[fe-retry-fix]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5637323519
[browser-handoff]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5637334937
[c006-pr]: https://github.com/sinangym/GoymenERP/pull/234
[c006-ci]: https://github.com/sinangym/GoymenERP/actions/runs/34604875177
[c006-source-fix]: https://github.com/sinangym/GoymenERP/pull/234#issuecomment-5637242940
[c006-publisher-fix]: https://github.com/sinangym/GoymenERP/pull/234#issuecomment-5637358347
[c006-publisher-review]: https://github.com/sinangym/GoymenERP/pull/234#issuecomment-5637396229
[fe-retry-review-1]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5637369806
[fe-retry-review-2]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5637370041
[run-fe-review-1]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5639080159
[run-fe-review-2]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5639080364
[run-be-delivery]: https://github.com/sinangym/GoymenERP/pull/234#issuecomment-5639375778
[run-be-review-1]: https://github.com/sinangym/GoymenERP/pull/234#issuecomment-5639375442
[run-be-review-2]: https://github.com/sinangym/GoymenERP/pull/234#issuecomment-5639375600
[run-l01-source]: https://github.com/sinangym/GoymenERP/blob/6bfc21eec43f02f3c8f2e0d86fd124c0f387bff4/tests/e2e/cari-acceptance-009/specs/l-01-live-ab-journey.spec.ts
[run-l02-source]: https://github.com/sinangym/GoymenERP/blob/6bfc21eec43f02f3c8f2e0d86fd124c0f387bff4/tests/e2e/cari-acceptance-009/specs/l-02-pending-context-change.spec.ts
