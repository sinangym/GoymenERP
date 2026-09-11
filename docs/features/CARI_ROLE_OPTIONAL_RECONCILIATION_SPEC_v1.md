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

**Güncel gerçek kapanış — fc (bu delta):** Başlangıç #232 HEAD'i `2208aa8e58206113fb79d42a320b071ad09b30ef`; önceki atıf düzeltmeleri korunur. [Tek fc teslimi][fc-final] ile `c006-fc721a1b9752` / FE `c3cb45e1f85a7079c742bc0abbe62ca3aa805dfe` / Acceptance=Compiled `5e83b5f09b2721d59b8dba09e8fed93d59b217cf` / API `880cae838e5d2f01a988f1e4a89afbe67858f48c` bağında **L01 PASS, L02 dar PASS, STRICT global SQL PASS ve owned cleanup doğrulanmıştır** (§9.7). Aşağıdaki 3037/ac5 kesitleri tarihseldir: 3037 L01 RUN_FAILED, ac5 L02 LOAD_FAILED/L01 NOT_RUN aynen kalır. Bu yeni sonuç yalnız ölçülen alt kabulü kapatır; tüm Cari/CR-10, #229/#232 insan kabulü, B önerisi veya production onayı değildir. Ortam tüketilmiştir: `readyForBrowserNow=false`, `erpAccepted=false`.

- Bu güncellemenin tetikleyicisi: **aynı gerçek `c006-303760997c6f` koşumunun legacy HTTP, L02, başarısız L01, STRICT SQL delta ve owned cleanup paketinin birlikte teslimidir**. [Merkezî kayıt][co-record] ve §9.5 kaynak/hash bağlarını içerir. Önceki CRO-AC-009 özel REAL_SQL kapanışı §9.1.1'de korunur; yeni test sayısı veya makbuz için faaliyet değildir.
- Koşumun frontend kaynağı #233/`6bfc21eec43f02f3c8f2e0d86fd124c0f387bff4`; derlenmiş acceptance kaynağı #234/`32392ca97e3ea74a4300855f015ee65febd66f87`; API bağımlılığı #230/`880cae838e5d2f01a988f1e4a89afbe67858f48c`. Bu yayımlanmış başlar doğrulanmıştır. Koşum sonrası harness düzeltmesi henüz yayımlanmış kaynak/başarılı koşum sayılmaz.
- **L01 `RUN_FAILED`, L02 yalnız ölçtüğü kapsamda PASS, legacy8 yalnız gerçek HTTP kapsamında PASS, STRICT global SQL delta ve cleanup başarılıdır.** L01 trace gözlemleri `TRACE_DERIVED/PARTIAL`; ölçülmeyen yollar `NOT_MEASURED` kalır. Gerçek ENV_READY üretilmiş, sonra ortam temizlenmiştir: E tarihsel, `readyForBrowserNow=false`; ERP kabulü/tamamlanması yoktur.

**Yeni gerçek kapanış kesiti — ac5:** Başlangıç #232 HEAD'i `e320f91cc432c32be3f6b535417f57f3c226441e` olarak doğrulandı; önceki A-detail/candidate ve TRACE RESPONSE/SQL atıf düzeltmeleri zaten yayımlanmıştır, yeniden uygulanmaz. Kimin fiziken pushladığına ilişkin makbuz üretilmez. Yukarıdaki 3037 özeti ve §9.1–9.5 kendi tarihsel kaynaklarında korunur. Yeni [ac5 kapanışı][ac5-final]: L02 **LOAD_FAILED**, sıfır test gövdesi; ayrı warm-up gerçekleşmiş, API çağrı sayısı **NOT_MEASURED**; L01 ve `ExpectedNewParties=1` yolculuk doğrulaması **NOT_RUN**. Ölçülen SQL projection delta0 ve cleanup, browser kabulü değildir. ac5 ortamı da tüketilmiş/tarihseldir (§9.6).

## 1. Kaynak önceliği ve güncel depo gerçeği

Öncelik: `AGENTS.md` ve uygulanabilir governance altında en yeni açık kullanıcı kararı → tarihli kullanıcı kararları → `KARAR_SICILI` → Master Rapor → ilgili feature/ADR → eski referanslar. Açık yetki, güvenlik, migration veya production politikası tahminle kapatılmaz.

11 Eylül 2026 tarihsel 3037 kaynak kesiti (ac5 koşum bağı ve sonraki kaynak ayrımı §9.6):

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

Test tasarımı PASS değildir. Her sonuç exact source, gerçek checkout, run/job ve kapsama bağlıdır.080/880, önceki frontend başları, c118/ca19 ve2b/1d/e35 sonuçları birbirine otomatik taşınmaz. Kaynak incelemesi, yazar/koordinatör log aktarımı, doğrudan CI günlüğü okuması ve yeni test çalıştırma ayrı tutulur. Önceki tur CRO009 kaynağı/gerçek SQL job günlüğü doğrudan okunmuştur. §9.5 tarihsel ham dosya/trace/hash/typedRows sonuçları, koordinatörün ham kanıt doğrulamasına atıflıdır; BUSINESS yayımlanmış kayıtları ve kaynak başlarını okumuş, yerel Windows artefaktlarını veya ürün testlerini yeniden çalıştırmış sayılmamıştır. B-C01-001…010, L01/L02 ve protokolün8 browser ölçümü CRO/CR kimliklerinin yerine geçirilmez.

`TRACE_DERIVED`, başarısız gerçek koşumun korunmuş izinden çıkarılan sınırlı gözlemdir; başarılı test veya normal network/measurement JSON'u değildir. `RUN_FAILED` çalıştırılmış başarısız koşumu, `NOT_MEASURED` belirli koşumda ölçülmeyen alt yolu, `NOT_RUN` hiç çalıştırılmamış kalan kabulü belirtir. Bunlar yeni kabul kimlikleri değildir. `LOAD_FAILED`, gerçek test çağrısının yükleme aşamasında test gövdesine geçememesidir; ayrı global warm-up veya sıfır test sayımı bunu browser PASS yapmaz.

Fc ham results/measurement/strict/hashlerini Root, HAR/network/trace yorumunu aynı Dirac, cleanup ve manifest234/234 byte/hash eşliğini aynı Schrodinger doğrulamıştır; BUSINESS [yayımlanmış fc teslimini][fc-final] ve bu koordinatör devrini okumuştur. Bu belge yerel Windows ham ölçümünü BUSINESS’a atfetmez. Gerçek sunucu201 etkisi üzerinde testin yanıt teslimini kesmesi **kontrollü teslim kaybıdır**; stub sunucu/replay cevabı veya kendiliğinden oluşmuş ağ olayı değildir. Bu varyantın gerçek persistence sonucu ayrıca SQL’den gelir.

## 9. Kanıta bağlı kabul matrisi

**Güncel ölçülen alt sonuçlar §9.7'deki fc / FEc3 / compiled5e / API880 bileşimine aittir.** L01 ve L02 sonuçları, gerçek SQL etkisi ve cleanup ayrı kanıtlardır; kontrollü teslim kaybı gerçek sunucu üzerinde uygulanmıştır. 3037 RUN_FAILED/TRACE_DERIVED/dar PASS kayıtları §9.5'te, ac5 LOAD_FAILED/L01 NOT_RUN ve atıf düzeltmesi §9.6'da kendi kaynaklarıyla korunur; fc bu geçmişi yeniden etiketlemez. CRO009 özel REAL_SQL bağı §9.1.1'dir. Satırdaki **ölçülen alt kapsam PASS**, tüm satırın veya kanonik kabulün koşulsuz tamamlandığı anlamına gelmez. Unit/helper/stub/component/controlled010/C-01/HTTP/gerçek SQL/entegre browser birbirinin yerine geçmez.

| Kabul | Başlangıç, eylem ve beklenen/yasak etki | Backend REAL_SQL / HTTP | Frontend unit / stub / component | Browser kanıtının türü ve kapsamı | Bütünleşik kabul ve kalan |
|---|---|---|---|---|---|
| CRO-AC-001 | Yetkili A kullanıcısı; taze valid GET+teyit, sonra roles eksik/0 create. Bir Party+A account, roles0, ilgili audit/durable result. Eski server200/no-ack varyantında POST0, kayıt0. | 880 missing/zero SQL korunur. Fc STRICT (§9.7) yeni1Party+2roles0 A/B hesap ve tek create/link audit/execution doğrular. Tarihsel3037 legacy8 header reddi ayrı sıfır-etki kanıtıdır. | Gerçek form/probe/command ve önceki component kanıtları mevcut; kaynak review/CI ayrı. | **Fc L01 PASS:** roles alanı olmayan gerçek create, A list/detail; upstream201+ACK teslimi testçe kesilir→unknown→kendi taze200+ACK GET transferi→explicit aynı-key201 replay. Son barrier kabul edildi. | **Ölçülen roleless create altı PASS.** Browser explicit roles0 request varyantı bu koşumdan değil önceki SQL’den kanıtlıdır. Actual old-binary200/no-ack form POST/PUT0 ve bütün ağ/context olumsuzları NOT_RUN/NOT_MEASURED; fc başarısı bunları kapatmaz. 3037FAIL/ac5NOT_RUN tarihsel. |
| CRO-AC-002 | Eski1/3/15 kayıtlarıyla upgrade; kimlik/rol/audit/replay değişmez. Zero sonrası Down reddi; DDL/veri kaybı yok. | OptionalRoles migration/legacy/Down senaryoları broad75 içinde PASS. [SQL kaynak][be-sql], §9.1. | N/A; UI testi SQL migration kanıtı değildir. | N/A; production migration yapılmış değildir. | Backend scope kanıtlı; yeni production/kanonik kabul yok. |
| CRO-AC-003 | Party A'da görünür, B account yok. B bağlamında taze valid GET+ack; `{}` veya roles0 link. Aynı Party, tek B account, A değişmez. A teyidiyle B yazması yasak. | 880 link/isolation korunur. Fc STRICT aynı yeni Party için iki şirket hesabı, tek create/link audit/execution ve korunan smoke satırlarını doğrular; AccountId bu projection’da yoktur. | 83 kaynak importu, dört reader ve rolsüz form mevcut; kapanmış disabled/retry implementation yolları yeniden açılmaz. | **Fc L01 PASS:** B listesinde yok/detail404→B candidate→{} ile hedefB link. Gerçek commit sonrası aday artık listede yokken aynı girişim kendi taze GET+ACK sonrası replay201 olur; uygulama-içi dönüş/odak ve Aroles0/version1 korunur. | **Ölçülen hedefB link/dönüş altı PASS.** Pending-retry abandon/query, uçuşta gerçek POST sırasında company/session değişimi ve tüm negatif readiness varyantları NOT_RUN/NOT_MEASURED. 3037/ac5 ayrı kalır. |
| CRO-AC-004 | Yetkisiz/foreign Party ve şirket varyantları. Doğru başlık grant vermez; mevcut403/404, kimlik sızıntısı ve business etki0. | 880/2b güvenlik kanıtı korunur. Fc’de dört403/forbidden, sayfadan ayrı **tek APIRequestContext içindeki dört gerçek HTTP isteğidir**; dört ayrı context değildir. Fc L02 whole-invocation typed6counter+2smokeRows değişmez. | Önceki auth-boundary/component ve transport kapanışları kendi türünde korunur. | **Fc L01/L02 ölçülen altları PASS:** B bağından önce listede görünmezlik/detail404; L02 page13API/0POST, tam barrier. Held GET ERR_ABORTED, causeEstablished=false; A/B0aday. | **PARTIAL toplam.** Gerçek logout/IdP/session-revocation ve gerçek POST-inflight context NOT_RUN. L02 seçim/başarılılatebody/leak, iptalin uygulama nedeni ve submit-gate nedenselliği NOT_MEASURED. Global SQL’den her403 arasına ayrı snapshot türetilmez. |
| CRO-AC-005 | Command sonucu unknown; aynı attempt/key/company/Party/path/payload korunur; tazeGET+ack sonrası açık same-key retry. İkinci etki/otomatik yeni key yok. | 880 durable replay ve2b stale409 korunur. Fc ExpectedNewParties1 STRICT globaldelta create/link başına tek execution/audit/iş etkisini doğrular; ara commit→retry SQL NOT_MEASURED. | c118/ca19 ve sonraki same-key/unknown component kapanışları korunur; helper veya kontrollü010 kanıtı live varyanta yükseltilmez. | **Fc L01 PASS:** iki gerçek upstream201+ACK’in teslimi test tarafından kesilir; unknown korunur→her girişimin kendi taze200+ACK GET transferi→aynı key/path/body/company ile explicit201/replaytrue. Doğal ağ olayı veya stub201 değildir. | **Bu kontrollü teslim-kaybı + gerçek replay/tek-global-etki altı PASS.** Yeni key/otomatik retry üretilmedi. Ack500/sonraki409/malformed-stream/pending-abandon/query ve POST-inflight context live ölçülmedi; ara-SQL zorunlu kapısı eklenmez. |
| CRO-AC-006 | İki farklı key aynı Party/B link yarışı; ayrıca audit/idempotency hata enjeksiyonu. Tek target account/başarılı created audit; yarım write yok. | OptionalRoles different-key race + audit/idempotency rollback; broad75 PASS. [SQL kaynak][be-sql], §9.1. | N/A; frontend birim testi SQL yarış/rollback yerine geçmez. | N/A; bu SQL kabulünün browser karşılığı uydurulmaz. | Backend scope kanıtlı. Kaybedenin durable conflict'i ikinci hesap değildir. |
| CRO-AC-007 | Eski1..15 ve yeni0 durable sonuçlar; uyumlu declaration ile replay. Eski hash/snapshot korunur, yeni business etki yok. Headerless replay command'i reddedilir. | 880 legacy/roleless durable replay ve2b reauthorization kendi kaynaklarında kalır. Fc global STRICT tek yeni Party/iki hesap/tekil execution-audit etkisi; smokeRows korunur. | Mevcut command/attempt/decoder/component ve controlled persistentServerReplayClaimed=false kayıtları ayrı türdür. | **Fc L01 PASS:** aynı-key/path/body/company gerçek201 replay; link commit sonrası candidateStillListedAfterCommit=false olsa da aynı girişim replay olur. Gerçek HTTP/trace ve nihai SQL ayrı kaynaklandırılmıştır. | **Ölçülen roleless replay altı PASS.** Fc bütün legacy/mixed-client/recovery matrisini yeniden çalıştırmış değildir; ara SQL ve tüm global API trafiği NOT_MEASURED. 3037FAIL ve ac5NOT_RUN korunur. |
| CRO-AC-008 | List/detail/candidate roles0; filtre/count; GET DB değiştirmez. Visible0 legacy projection409; gizli foreign0 sızmaz. | 880/2b read/no-write korunur. Fc L02 before/after780B aynıhash,6sayaç+iki smoke typedRows eşit: yalnız bütün invocation projection ZERO. Tarihsel legacy8HTTP ve3037 SQL ayrı kalır. | Dört reader/form ve önceki helper/stub/component/C-01 kapsamları korunur. | **Fc L01 dört reader roles0 PASS; L02 dar PASS.** List/detail/mutation/B-candidate gerçek response kaynakları; L02 page13API/0POST, barrier kabul. Page sayımı global trafik değildir. | **Ölçülen okuma altları PASS / kalan PARTIAL.** Başarılı geç body/selection/leak/cancel cause NOT_MEASURED; projection0 tüm DB’de ara write yokluğu değildir. 3037 A candidate yanıtı doğrulanmadı atfı aynen kalır. |
| CRO-AC-009 | Roles=None hesap; yetkili pasifleştirme→version2; stale1→409/replay; viewer/foreign yetki reddi; yetkili reactivation→version3. Rol/aktiflik/yetki ayrımı korunur. | **REAL_SQL alt varyant PASS @2b21e3b / checkout45dc98d.** [Named fact][cro009-source] ve [job103280752609][cro009-job] bağı korunur; eski SQL kapanışı yeniden açılmaz. | Active-state form/browser kanıtı bu SQL fact'inden veya create/link component testinden çıkarılmaz. | Fc L01/L02 aktiflik değiştirme tarayıcı adımı içermez: **active-state browser NOT_RUN**; aynı koşumun create/link başarısı SQL lifecycle fact’inin yerine geçmez. | **PARTIAL toplam / özel REAL_SQL boşluk kapalı.** 2b exact test ve5e SQL76/RCSI10 kaynakları ayrı; fc’de yeni lifecycle browser kabulü yok. 3037/ac5 geçmişi korunur. |
| CRO-AC-010 | İki yönde eski/yeni client-server; logout/company/session/API/late-response. Current valid GET+ack readiness; teyitsiz yazma0. Karma havuz yalnız doğrulanırsa §4.6. | Tarihsel3037 legacy8 create/link×omitted/zero×missing/unsupportedheader exact409 ve interval sıfır-etki **gerçek HTTP** olarak korunur; eski binary çalıştırılması değildir. Fc SQL projection ZERO ayrı. | Önceki readiness/abort/retry ve controlled010 sonuçları kendi etiketleriyle kalır; live old-binary veya IdP davranışına çevrilmez. | **Fc L02 dar PASS**, tam barrier,0aday ve ERR_ABORTED/causeEstablished=false; fc L01 kendi taze GET+ACK sonrası dispatch/replay PASS. Bunlar bütün context/uyumluluk matrisini ölçmez. | **PARTIAL; tüm CR-10 PASS değil.** İki yönlü eski/yeni istemci-sunucu gereksinimi korunur. Actual oldbinary, real logout/IdP ve gerçek POST-inflight context NOT_RUN; selection/latebody/leak/API-origin/pending-abandon altları NOT_MEASURED. Tek-node geliştirme kanıtı rollout/production kabulü değildir; yeni gateway yok. |
| CRO-AC-011 | Request geçersizroles/header; response0/15 geçerli, missing/null/16 geçersiz. Hata+ack success/readiness değil; error-no-ack eski server kanıtı değil. | 880 invalid-wire/HTTP/OpenAPI ve3037 legacy8 kanıtları korunur. Fc create roles omitted/link{} ve geçerli0 response/replay gözlemi; tüm invalid-role/header/body matrisi yeniden koşulmadı. | Dört decoder ve uyumluluk/unknown component ile controlled010 hata varyantları kendi kaynaklarında kalır. | **Fc L01 gerçek lost-delivery/unknown/replay altı PASS.** Başlangıçtaki201+ACK gerçek sunucudan gelir, teslim kaybı kontrollüdür. Hata+ACK ve malformed-stream recovery live ölçüldü diye sunulmaz. | **PARTIAL toplam.** Invalid-wire eski kanıt türüyle, ölçülen fc recovery ise gerçek sunucu + kontrollü teslim kaybı olarak kayıtlıdır. Actual oldbinary/capability-off write-block veya bütün error/stream yolları kapatılmaz. |
| CRO-AC-012 | Roleless create/link valid mutation+ack ile tek success/detail; güncel yetki/cursor ile dönüş. Unknown'da success/clear/navigation0. | Fc STRICT ExpectedNewParties1: +1Party/+2roles0A-Baccount/+1’ercreate-link audit/execution; iki smoke satırı korunur. AccountGUID’leri HTTP/trace, SQL ise global sayaç+typedRows kaynağıdır. | Form/return/retry component geçmişi korunur; odak kabulü SQL’den türetilmez. | **Fc L01 PASS:** roleless create→A list/detail→B listesinde yok/detail404→B candidate/link→uygulama-içi dönüş ve Party satırında odak; Aroles0/v1 aynı. Son barrier tam konjonksiyonla kabul; results/measurement/network/trace mevcut. | **Bu gerçek yolculuk/dönüş altı PASS.** 3037RUN_FAILED/TRACE_DERIVED ve A-candidate atıf düzeltmesi; ac5LOAD_FAILED/L01NOT_RUN silinmez. Tüm logout/context/stream/abandon/oldbinary ve diğer kapsamlar ölçülmeden kabul edilmez. |

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

**3037 tarihsel koşumunda kullanılan yayımlanmış baş `6bfc21eec43f02f3c8f2e0d86fd124c0f387bff4` (§9.5); ac5 ve loader düzeltmesi §9.6'dadır.** Aşağıdaki c118/ca19/ef kayıtları tarihsel kaynak ve kanıt düzeyleriyle korunur. [9a makbuzu][fe-handoff], [07a teslimi][fe-fix], [ef teslimi][fe-final] ve önceki6f kapanışları yeniden görevleştirilmez. Import/form, helper, component, browser ve final kabul farklıdır.

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

**TRACE_DERIVED:** gerçek A roleless create→A list/detail→B bağından önce listede görünmezlik/detail404→candidate/link→B list/detail/gerçek return focus→A detail aynı kimlik/içerik doğrulandı. A'ya dönüşte candidate GET kayıtları status=-1; body yok, biri ERR_ABORTED. A candidate yanıtı doğrulanmadı. **TRACE RESPONSE kaynaklı hesap GUID'leri:** A account `70a72fbe-4c5d-4a1e-8da4-651f555677c1`, B account `ebf687de-c0dd-47e1-909c-f53b42336428`. Her iki command'de gerçek upstream201'in teslimi kaybettirildi; unknown korundu; command'in kendi yeni GET200+ack'i ardından açık aynı-key retry201/replaytrue ve byte-identical response body gözlendi. Bunlar mock server başarıları değil, başarısız gerçek koşunun izinden türetilen gözlemlerdir. Normal ağ/ölçüm raporu yokken bağımsız başarılı test maddeleri veya tüm yolculuk PASS'i olarak sunulmaz.

**CRO-AC-004 HTTP negatifleri:** ayrı APIRequestContext'ten viewer/foreign kapsamında2GET403+2POST403 gerçek HTTP gözlemi vardır. B-link öncesindeki404/list görünmezliği ayrıca L01 izindedir. Bunlar browser logout, oturum sonlandırma, yetki iptali veya token revocation testi değildir; L01global SQL sonucundan her403 çağrısı arasında alınmış ayrı snapshot varmış gibi sonuç türetilmez.

**STRICT global SQL — gerçek PASS; L01FAIL'den ayrı.** Aynı Backend `ExpectedNewParties=1` ile19:35:25.099–19:35:27.735Z doğruladı; exit0/passedtrue. Altı sayaç baseline `[1,2,1,1,1,1]` → current `[2,4,2,2,2,2]`: +1Party, +2roles0 A/B account, +1create audit, +1link audit, +1create execution, +1link execution. Yeni Party `9a2ac59f-2954-4fef-9ffb-e973d5379cfb` version1; A/B typedRows roles0/version1. Önceki smokeRows korunmuştur. STRICT SQL çıktısı AccountId taşımaz; SQL kanıtı global sayaçlar ve typedRows etkisidir. A/B account GUID'leri yukarıdaki TRACE RESPONSE kaynağına aittir, SQL'e değil.

Bu **global gerçek tek iş etkisi** ve actual replay gözlemi birlikte kaydedilir. İlk POST commit'i ile retry arasındaki ayrı SQL snapshot **NOT_MEASURED**; varmış gibi raporlanmaz. Bu sınırı belirtmek, yeni bir ara-SQL zorunlu kabul kapısı icat etmek değildir; mevcut STRICT sözleşmesi değiştirilmez. CRO005/007'de gerçek replay ve tek global SQL etkisi ölçülmüş olsa da L01RUN_FAILED ve toplam PARTIAL korunur.

**Owned cleanup — gerçek PASS.** Aynı Backend stop19:36:07–09, release-check09–11, owned teardown11–13Z adımlarını exit0 ile tamamladı. Son salt-okunur kontrol19:39:16–17Z: PID11388gone;25080/25433/25173listener0; owned container/volume/network0; privatefilegone;18 önceki proof hash'i değişmemiş. Koordinatör beş ham dosya/hash/typedRows/cleanup sonucunu doğruladı. Temizlik başarısı test başarısı değildir; E tarihsel ve L01FAILED kalır. Bu koşu tüketilmiş/temizlenmiştir; reset veya rerun yapılmadı.

**Bu pakette ölçülmeyenler:** gerçek logout/session exit/revocation/API-origin değişimi; başarılı late body/leak/grant causality; dolu aday seçiminin taşınması; gerçek pending-retry abandon/query yolları; iki yönde eski/yeni binary; malformed-stream recovery ve active-state browser. Ack'li500/sonraki compatibility409 unknown varyantlarının eski helper/component kanıtı, bu gerçek run'da yapılmış ölçüme dönüştürülmez. NOT_MEASURED altları controlled veya tam PASS diye yükseltilmez; CRO009'un SQL kapanışı active-state browser kabulü değildir.

**Açık somut teknik bulgu ve aynı sahipte sonraki adım:** full-page goto ile şirket GET'inin çakışması iki isteğin terminal gözlemini eksik bırakmış; hata halinde evidence flush gerçekleşmemiştir. Aynı Claude yalnız bu dar harness düzeltmesine yetkilidir. `framenavigated` olayını request-finished sayıp `accepted:true` üretmek veya quiescence/capture kapısını gevşetmek kabul edilmiş çözüm değildir. Bu kesitte yeni düzeltme/FE head yayımlanmış değildir. Yeni fark→aynı Dirac/Averroes exact-head review+freshCI→aynı Backend yeni local matching-build/final pin/izole tuple→gerçek browser/SQLdelta/teardown sırası korunur. Yeni gateway, işletme politikası veya Sinan müdahalesi gerektiren bir karar açılmaz.

### 9.6 ac5 gerçek kapanış: LOAD_FAILED, ölçülen projection delta0 ve cleanup

Tek kalıcı teslim [#234 /5641344986][ac5-final], [#233 pointer5641345193][ac5-pointer]; ortamın önceki gerçek teslimi [5641243814][ac5-env] ayrı kaynaktır. Root/mevcut Schrodinger ham final dosya/hash/tuple doğrulamasını bildirmiştir. BUSINESS bu yayımlanmış kayıtları ve koordinatör devrini okumuştur; Windows ham artefaktlarını kendisi açmış, hashlerini yeniden hesaplamış veya ürün testini tekrar çalıştırmış sayılmaz.

**Exact koşum bağı:** `c006-ac5fc7069528`, instance `292e8f25-7e06-4d8a-a96c-06586a316cb1`; FE `a5851a148b55f66ca6555cdcc810107f949fb6a4`, AcceptanceHead=CompiledHead `6543792f7aeb3a0d9aad7d131ee20d7d8ae24332`, API dependency `880cae838e5d2f01a988f1e4a89afbe67858f48c`. Main `78494ed9bc60692fac6c186aa52b0049bbf4c306` API kaynağının squash'ıdır; dependency yerine yazılmaz. Fixture SHA256 `86242c16f4280326f9c0dfcab92805f929283bb6df20d9e2bef6b56f6e97716f`. Daha yeni PR başları bu tuple'a veya geçmiş ENV'ye bağlanmış sayılmaz.

Yerel konumlar aşağıdaki gibidir; bunlar sandbox/GitHub artefakt linki veya yeni oluşturulmuş dosya değildir:

```text
RunRoot = C:/src/GoymenERP-cari-acceptance-envelope-20260911/artifacts/cari-acceptance-006/c006-ac5fc7069528
FinalDelivery = RunRoot/intervals/final-delivery.md
BrowserRoot = C:/src/GoymenERP-claude/artifacts/c009-l02-ac5fc7069528/L
```

| Devralınan ham kanıt | SHA256 / anlam |
|---|---|
| FinalDelivery | `3f187ec9063c74086d9176a86519e8634fa0924aa40635278065b7a6c908808c`; Root/Schrodinger ham doğrulama aktarımı. |
| RunRoot/environment.json | `8709d57eae77d05411b6bc2c02ff0f01bc1bc145ade8db30a7c9e0178e806830`; [gerçek ENV teslimi][ac5-env], cleanup sonrası tarihsel. |
| RunRoot/ENV_READY.json | `67ba6fd4c7efb5b1e57b8eb33390acdc20086179822e97947e3d50911a5c9f9d`; yeni c3cb kaynağının ENV_READY'si değildir. |
| RunRoot/intervals/l02-before.json ve l02-after.json | Her biri780byte, aynı `3336b57be0173484966ff24074167d8df3a664419bda837b4cef29ff1142dd7a`. |
| RunRoot/intervals/l02-load-failed-result.json | `7e4de2cd246326d871c77af2a75f2aada24c35c4015b8380ebe9a0ea6af8b052`; eski contextState atfı aşağıda açıkça düzeltilmiştir. |
| RunRoot/intervals/cleanup-verification.json | `1cf053a45488ad735d8ed4cf1c586f7a95136b9468a3becac3d8a87a9f31b5d6`; attributionCorrection ve owned cleanup ayrı olgulardır. |
| BrowserRoot/results.json | `f5203f6411a7b35eebd3e12497f514f606861811fc4acdd17db2d0c668ad60e4`; L02 LOAD_FAILED, suites boş. |
| BrowserRoot/junit.xml | `10e665bd4b5504c75212d83c5069ab2d0016a691a52ce912409d3b6e5749572f`. |
| BrowserRoot/manifest-verification.json | `f9dbe4dabefed7bc0b3958f5b5de846a8d838b3cc5e8b951a70060bb9296f2a0`; manifest doğrulaması test gövdesinin çalıştığı anlamına gelmez. |
| BrowserRoot/browser.json | `08730ecf4954227d70b3382704b6733bea9b9be940532ee855fc91bc18723416`; ayrı337ms warm-up gözlemi. |

**L02 gerçek çağrı — LOAD_FAILED.** Başlangıç `2026-09-11T22:20:53.623Z`, süre2973.174ms, exit1. Playwright loader hatası: `First argument must use the object destructuring pattern: _fixtures` (L02 afterEach line80). `suites=[]`; expected/skipped/unexpected/flaky sayaçları0 ve **hiçbir test gövdesi çalışmadı**. Sıfır test, başarılı veya atlanmış kabul sonucu değildir.

**Warm-up ve atıf düzeltmesi:** ayrı global Chromium warm-up `22:20:56.182Z`,337ms olarak gerçekleşmiştir. “Hiç browser/context açılmadı” ve “yalnız iki preflight GET yapıldı” iddiaları geri çekilmiştir. **API çağrı sayısı NOT_MEASURED**; noHTTP/POST0 sonucu çıkarılmaz. İlk immutable `l02-load-failed-result.json` korunur; `cleanup-verification.json.attributionCorrection`, o dosyanın eski contextState/NotCreated atfını gerçek browser.json ve düzeltilmiş setup gözlemine dayanarak **supersedes** eder. Diğer kanıtları geriye dönük yeniden yazmaz.

**Gerçek typed before/after — yalnız ölçülen projection delta0.** Aynı Backend'in mevcut `control.ps1 -Action snapshot` after çağrısı `22:24:08.8312668–22:24:10.9025203Z`,exit0.780byte ham before/after aynıdır; altı sayaç `[1,2,1,1,1,1]`, iki smoke A/B orderedRows roles0/version1 değişmemiştir. Bu, bütün invocation aralığındaki **ölçülen projection'ın** sıfır farkıdır; tüm DB atomik/no-write kanıtı, ara browser adımı ölçümü, HTTP POST yokluğu veya browser PASS değildir. Snapshot anındaki kayıtlı kaynak/instance kontrolü daha yeni FE'yi geçmiş ENV'ye yeniden pinlemek değildir.

**L01 ve ExpectedNewParties=1 yolculuk doğrulaması NOT_RUN.** L01 yolculuğu olmadığı için eski3037 STRICT +1Party/+2account sonucu bu koşuma taşınmaz. ac5 projection0 yeni roleless create/link, unknown/replay veya dönüş davranışını doğrulamaz. Mevcut CRO009 REAL_SQL, CRO002/006 ve3037 legacy8HTTP/L02darPASS/L01RUN_FAILED/TRACE_DERIVED/globalSQL/cleanup kanıtları kendi kaynaklarında aynen kalır.

**Owned cleanup — gerçek sonuç.** Aynı Backend stop `22:24:43.1609766–44.9527648Z`, release-check `44.9916513–46.7815862Z`, owned teardown `46.7907840–49.4488518Z`: exit0. Bağımsız salt-okunur gözlem `22:26:21.4294375–22.2127673Z`: kayıtlıPID5936 yok;25080/25433/25173 listener0; exact owned Docker kaynakları ve private file yok; unrelated kaynaklar/Docker Desktop kapatılmadı.11 ham kanıt hash'inin değişmediği Root/Schrodinger devridir. **readyForBrowserNow=false, erpAccepted=false**; ac5 ve3037 tüketilmiş/tarihsel koşumlardır. Temizlik, yüklenememiş L02'yi veya çalışmamış L01'i PASS yapmaz; eski ENV tekrar kullanılamaz.

**Sonraki kaynak düzeltmesi, kabul sonucu değil:** [aynı Claude'un c3cb45e teslimi][ac5-loader], exact `c3cb45e1f85a7079c742bc0abbe62ca3aa805dfe`, parenta585; iki afterEach hook imzası ve satıra özel lint açıklaması. Yazarın gerçek loader `--list` gözlemi önce exit1/0test, sonra exit0/2testtir. Bu, spec yükleme kontrolüdür; sentetik izole config girdisi ENV_READY, browser veya ERP kabulü üretmez. Bu düzeltme kaynağında gerçek L02/L01 **NOT_RUN**; bu belgede final-head CI/review kapanışı veya yeni runtime PASS'i ilan edilmez. ac5ENV c3cb'ye taşınmaz. Aynı Claude kaynak kapanışı → mevcut exact-head review/CI → aynı Backend gerekli dar pin/main/local matching-build ve yeni bağlı ENV → gerçek kabul/typed delta/cleanup sırası korunur. Bu bağımsız belge teslimi Backend veya Claude için yeni bekleme kapısı değildir; yeni gateway, yaşam döngüsü, işletme kararı veya yazar açılmaz.

### 9.7 fc gerçek uçtan uca paket: ölçülen L01/L02 PASS, STRICT SQL ve cleanup

**Tek kalıcı kaynak:** [#234 /5641808223][fc-final]. Bu delta #232/2208aa8 üzerindedir; 3037 ve ac5 bölümleri/atıf düzeltmeleri değiştirilmez. Root gerçek results/measurement/strict dosyalarını, aynı Dirac HAR/network/trace davranışlarını, aynı Schrodinger cleanup ve manifest234/234 dosyanın byte/hash eşliğini doğrulamıştır. BUSINESS yayımlanmış paketi, PR head’lerini ve koordinatör devrini okumuştur; yerel ham ölçümü veya yeniden test çalıştırmayı kendisine atfetmez.

**Sabit kaynak ve runtime bağı:** run `c006-fc721a1b9752`, instance `7ec92416-5a5d-42ad-9823-3574f27592a8`; FE #233 `c3cb45e1f85a7079c742bc0abbe62ca3aa805dfe`; Acceptance=Compiled #234 `5e83b5f09b2721d59b8dba09e8fed93d59b217cf`. API dependency `880cae838e5d2f01a988f1e4a89afbe67858f48c` ayrı kilitli anlamıyla kalır; main squash `78494ed9bc60692fac6c186aa52b0049bbf4c306` dependency veya derlenmiş acceptance head yerine yazılmaz. Fixture SHA256 `52ebab9c9b78dd3c69d0eb239f4c49cd787c6513f77b7c1e07e9ef432eb0c412`.

BackendTree `4e0b1a5d6a1116ee2ba76df132aeec6e8b9c99d5`; OpenAPI SHA256 `062c06175916295bc97a438adea6fd01cdb608413c58501aa62208f87038a829`; migration-source SHA256 `1ec8222d998cb59891f437fc1ce4f62f954203508f3b2cffd4a00d97b1933e44` **liveDDL hash’i değildir**. FE diff base `060da63f2644cb271d78d9eab8b7a6985d544ad1`, ham diff1077156byte/SHA256 `a184b46b34b2208dde74c366abe105b54c9d01515aa1930f0a597cb597fd6e53`. DB `GoymenCariC006_fc721a1b9752`, tek-node loopback API25080/SQL25433/UI25173; actor group-bootstrap-admin, A/B fixture şirketleri. API PID684/start `2026-09-11T22:47:25.7543609Z`. Dış contract tek yazarı Backend’dir; tam kaynak/build/DLL/owned kaynak pinleri ana pakettedir. Disk/startup hash’i process-memory attestasyonu değildir.

**Korunan kaynak kapıları:** C3 kendi7CI ve [Dirac5641353556](https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5641353556)/[Averroes5641378360](https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5641378360);5e kendi8CI ve [Ampere5641460778](https://github.com/sinangym/GoymenERP/pull/234#issuecomment-5641460778)/[James5641460957](https://github.com/sinangym/GoymenERP/pull/234#issuecomment-5641460957) kabul edilmiş exact-source kanıtlardır. Bunlar NON-AUTHOR AI incelemeleridir, insan onayı değildir; eski kapsam yeniden açılmaz. [5e gerçek SQL job103444816089](https://github.com/sinangym/GoymenERP/actions/runs/34654824343/job/103444816089) SQL76/RCSI10 failed0/skip0 ile yerel BackendVerify140+18publisher farklı kanıtlardır. Yerel140 testi22:37:36.018785–22:38:21.834787Z aynı runtime build’indedir:19tested ERP DLL,18runtime DLL eşleşmesi; CI checkout’u canlı process veya başka build yerine sunulmaz. Bu sayılar yeniden koşulmuş test değil paket aktarımıdır.

Yerel kanıt kökleri (sandbox/GitHub dosya linki değildir):

```text
RunRoot = C:/src/GoymenERP-cari-acceptance-envelope-20260911/artifacts/cari-acceptance-006/c006-fc721a1b9752
Inputs = C:/src/GoymenERP-cari-acceptance-envelope-20260911/artifacts/cari-acceptance-006/inputs-c006-fc721a1b9752
L01Root = C:/src/GoymenERP-claude/artifacts/c009-l01-fc721a1b9752/L
L02Root = C:/src/GoymenERP-claude/artifacts/c009-l02-fc721a1b9752/L
```

| Ham artefakt | Byte | Devralınan SHA256 |
|---|---:|---|
| RunRoot/intervals/final-delivery.md |22025| `7a73c17a4a7c3bfa6e782dde5a4ca384e66c1b750329cdeedf7b32b468af4da6` |
| Inputs/contract.json |1260| `913bce5dc62b6191da8f10e01fa5bf705c6032c8bd276c61d97fe164930b195d` |
| RunRoot/source-binding.json |3184| `a6628b8a4e138d8c574634b2e0787bad844151f573c3973e0a7718003fc90e39` |
| RunRoot/environment.json |19435| `b68f8fce3f51cf1c8bdc8f3d0202d7cdf87083a71775ce0818521b110e29d318` |
| RunRoot/ENV_READY.json |233| `1f01689031b30ac40cb53b71db7dfd9d34eea0ecf00dd24f514fd29dd055f8ee` |
| Inputs/local-backendverify-proof.json |25224| `c4145b1b27c38d1f8dd2d895a4715f8d77f3eeddd7122a84becbbcddfcbe5545` |
| RunRoot/intervals/l02-before.json ve l02-after.json |780’er| `9c1ed02edf6f21ac02c629b448112bd4a65fb63fce1b10202a4977c6e020943d` |
| RunRoot/intervals/l02-result.json |9606| `0e922bca79f436dfc06738c8c2aab776969acb96d6c16e801f58747403ee6d8c` |
| L02Root/results.json |4994| `b04843fbbfe4b24ba512ac9367d6bff3cd0d477879ddc3dfe80f8a148a03eb4d` |
| L02Root/measurements/l02-pending-context-change.json |4091| `72a79d46506efc2d789d2db0148641fc45d1bf40e54062ed338fb60bd7a5f20d` |
| L02Root/network/l02-pending-context-change.json |13930| `89db7c08f3c91584cbe9dccc374076be78d9d513bc8c1d9c9a2feb906ec264ea` |
| L01Root/results.json |5028| `1351e22569ac73509a122d09aa3a6dedbce2f4ddcf47a542a6a6e356aea8ea4d` |
| L01Root/measurements/l01-live-ab-journey.json |11353| `4bc79cd7f90b1adc585d56ca3df6426143bff24665a2cab1f8c9fbed2c2a668a` |
| L01Root/network/l01-live-ab-journey.json |97746| `0ec78d36793c32c4ad5d11bf31dcac8f6f511958e22d5dc6ba6832ab0b4a2583` |
| L01Root/playwright-output/l-01-live-ab-journey-L-01--b48a7-y-retry-A-account-preserved/trace.zip |2201543| `95f8316f474416081f8f6e23fd9d6aec29f00c955fc0168501dbd7df9ab9d62e` |
| RunRoot/intervals/l01-strict-verify-output.json |2365| `79d2e390d734c472292ac89179f7d756bd1d237fe8df6b104df8ee22cda7f5b7` |
| RunRoot/intervals/l01-strict-result.json |5359| `80ed93d7dd7221ae392d43ed4d98c036641a34a88613247d6e52d9121e118eea` |
| RunRoot/intervals/cleanup-verification.json |42952| `e43b90aae16ef15f2981c654762f17c526eb3677d440cc04fd44366870db4607` |
| RunRoot/intervals/artifact-manifest.json |86158| `704bb94077e39dfe127ab607c513d8aebe928dbabe6081055796bd1b52a55c22` |

**L02 — ölçülen dar gerçek PASS:** [exact c3 L02 kaynağı][fc-l02];11 Eylül22:53:58.420Z,7412.964ms,tek1PASS/exit0,0unexpected/flaky/skipped. Page13API/0POST; tam barrier kabul, quiet1617ms, outstanding boş. Held GET `ERR_ABORTED`, bodyunavailable, `causeEstablished=false`: uygulamanın iptal ettiği sonucu çıkarılmaz. A/B0aday olduğundan seçim taşınması/başarılı geç-body/leak ve submit-gate nedenselliği NOT_MEASURED. Before22:49:48.8108976–22:49:50.8174242Z /after22:57:08.8780427–22:57:10.9923481Z aynı780byte ham içerik;6sayaç `[1,2,1,1,1,1]` ve iki smoke typedRows eşit. Bu bütün invocation’ın **typed projection ZERO** sonucudur, ara adım/tüm DB atomikliği veya global HTTP POST yokluğu değildir.

**L01 — gerçek kapsamlı yolculuk altı PASS:** [exact c3 L01 kaynağı][fc-l01];11 Eylül22:59:51.270Z,11348.52ms,tek1PASS/exit0,workers1/retries0. Rolsüz create→A list→detail→B listesinde yok/detail404→B candidate→hedefB link→uygulama-içi dönüş/Party satırında odak; Aroles0/version1 korunur. List/detail/mutation/candidate dört reader roles0. Final barrier23:00:01.718Z tam konjonksiyonla kabul,quiet1603ms,outstanding boş. Page82API/4POST **page-scope** sayımıdır; tüm API trafiği değildir. Dört403/forbidden sayfadan ayrı **TEK APIRequestContext içindeki dört istektir**, dört context veya gerçek browser logout/revocation testi değildir.

**İki gerçek command ve kontrollü teslim kaybı:** create isteğinde roles yok; link `{}`. Her iki gerçek upstream201+ACK’in teslimi testçe kesilir; unknown korunur; her girişimin kendi taze200+ACK GET transferi tamamlanınca aynı key/path/body/company ile açık201/replaytrue gelir. Transfer→POST→capture tick’leri create46→47→48/link229→230→231. Link commit sonrası candidate artık yokken aynı girişim yine replay olur. Bu gerçek server etkisi üzerinde **kontrollü teslim kaybıdır**, doğal ağ olayı veya sahte201/replay cevabı değildir. Ara postcommit→retry SQL NOT_MEASURED; global sonucu ara ölçüm diye sunma veya yeni araSQL zorunlu kapısı üretme.

**STRICT gerçek global SQL PASS:**11 Eylül23:05:54.5905919–23:05:57.1704603Z,ExpectedNewParties1,exit0/passedtrue.6sayaç `[1,2,1,1,1,1]`→`[2,4,2,2,2,2]`: +1Party/+2roles0account/+1’ercreate-link audit/execution; iki smoke satırı korunur. Yeni Party `ea8e5f69-438e-40d8-b9fc-ba5faa428b30`/CAR-000002/version1; A/BtypedRows roles0/version1. **HTTP/trace kimlikleri:** Aaccount `f6776542-da9b-402e-aaf1-0e7f94062eae`, Baccount `8adbd1af-d47b-47cf-977e-0f0424bbb130`; SQL projection AccountId göstermez. Browser tek başına persistence kanıtı değildir; nihai SQL ve her command’in replay gözlemi ayrı kaynaklar olarak birlikte değerlendirilir.

**Owned cleanup ve koruma:** stop23:06:28.0886660–29.9055950Z/release-check29.9682016–31.7329865Z/teardown31.7404405–34.4159779Z exit0. Ayrı read-only gözlem23:24:21.4534597Z: kayıtlıPID684/üç port/ownedcontainer-volume-network/privatefile yok. Root/Schrodinger99/99 önceki hash’in korunduğunu, manifest234/234 kaydın byte/hash eşliğini doğrulamıştır;3037’nin66/ac5’in40 kayıtlı artefaktı korunur. Manifest233 önceki evidence/build dosyası+cleanup receipt içerir; manifestin self-hash’i ve daha sonra yazılmış final-delivery dışarıda tutularak hash döngüsü önlenir. `readyForBrowserNow=false`, `erpAccepted=false`; fcENV de tarihseldir ve yeniden kullanılamaz. Paketleme OS206 komut-uzunluğu hatası aynı Backend’de giderilmiştir; izin reddi veya güvenlik reddini başka araç/ajanla aşma değildir. Bu kapanış yeni kaynak veya test değişikliği üretmemiştir.

**Kabul sınırı ve kalan teknik altlar:** Fc’nin ölçülen create/link/readiness-GET-transfer/replay/dönüş altları PASS’tir; eski3037FAIL ve ac5LOAD_FAILED değiştirilmez. Gerçek eski binary/iki yönlü binary rollout, real logout/IdP/session-revocation ve gerçek command POST’u uçarken context değişimi NOT_RUN. L02selection/başarılılatebody/leak/cancel cause/submit-gate causality, globalAPI sayısı ve ara retrySQL NOT_MEASURED. Pending-retry abandon/query, API-origin, ack500/sonraki409/malformedstream ve active-state browser bu fc paketiyle ölçülmüş sayılmaz; eski controlled010/helper/component/legacy8HTTP/REAL_SQL etiketleri korunur. CRO009 özel SQL kapanışı korunurken browser/entegre altı PARTIAL/NOT_RUN kalır. Aynı Claude kalan browser varyantlarının, aynı Backend gerekli fixture/şemalı ortamın, koordinatör rollout/IdP/oldbinary ortam sırasının sahibidir; bunlar yeni işletme sorusu veya mevcut fc teslimini tekrar koşma talebi değildir.

**Geliştirme merge kanıtı ≠ tüm Cari/rollout kabulü:** Mevcut tek-node dilimin uygulanabilir K3 kaynak/iki NON-AUTHOR review/freshCI/ilgili gerçek SQL-browser-failure/cleanup kanıtı bu pakette birlikte mevcuttur. Ayrı CODEX-ORCHESTRATOR mevcut #230→#234→#233 expected-head kapılarını değerlendirir; BUSINESS merge ya da kanonik kabul kararı vermez. Eski/yeni istemci-sunucu gereksinimi silinmez; yeni gateway veya araSQL kapısı eklenmez. #229/#232 insan incelemesi, onaysız B ve production sınırları ayrıdır. Scoped teslim için kapanmış kaynakları yeniden incelemek veya yalnız makbuz/test sayısı için yeni koşum yapmak gerekmez.

## 10. Frontend implementation sözleşmesi

### 10.1 Dört reader

Aynı #233 sahibi onaylı83 kaynak devriyle list/detail/mutation/candidate'i required integer0..15 contract'a uzlaştırmıştır; mevcut form adayı §9.2'de kaynaklıdır. 15 kabul,16 ret; response null/missing→zero yok. Identifier, şirket, PartyKind, aktiflik ve version korumaları değişmez. Yeni decoder ailesi veya paralel create/link uygulaması açılmaz. Dört reader roles0 fc L01’de ölçülmüştür (§9.7). Tarihsel3037 kısmi izleri ve ac5 yükleme hatası kendi bölümlerinde kalır; bütün Cari/rollout kabulü bundan çıkarılmaz.

### 10.2 Form, düğme ve hata

Mevcut create/link formlarındaki mandatory-role validation ve metin kaldırılmıştır; başka zorunlu sınıfa dönüştürülmez. Firma/Kişi, hukuki ad, opsiyonel ticari ad ve salt okunur seçili şirket korunur. Kaynak roller hedefe kopyalanmaz.

Permission kaynaklı aksi mevcut non-disclosing kuralıyla sunulmaz; yetkili kullanıcı için uyumluluk doğrulanamaması işlevin çalışıyormuş gibi gösterilmesine izin vermez. Güvenli Türkçe açıklama ve mevcut error/dirty/pending/focus davranışı korunur. Teknik başlık kullanıcıdan elle istenmez.

Yükleniyor/GET hatası/uyum doğrulanamadı durumunda write kapalıdır. Yalnız valid GET+ack açabilir. Command başarısı ancak kendi tam response ve teyidiyle gösterilir; belirsizlikte başarı bildirimi, formu temizleme veya detail navigation yapılmaz. Hata sınıfı başlık yokluğundan uydurulmaz. Unknown/abandoned girişim varken genel sayfa uyarısı da key kaybettiren reload veya yanlış “hiçbir istek gönderilmedi” ifadesi kullanmamalıdır. Önceki ca19 kapanışı yeniden görevleştirilmez; 3037/ac5/fc ölçüm sınırları §9.5–9.7’de ayrıdır.

### 10.3 Bağlama bağlı hazırlık

`(apiOrigin, actor/session, selectedCompanyId, generation)` değişirse readiness düşer. Taze GET cevabı aynı tuple'a ait olmalıdır. Gerçek sunucu teyidi, response decoder ve mevcut authorization ayrı zorunluluklardır. Transport header eski source-account görünürlüğünü target link yetkisine çeviremez. Ef issue-time context/abort guard'larının dar kapanışı §4.2'dedir. Gerçek retry ön kontrolü bırakma/query değişiminden sonra eski hazırlık üzerinden komut gönderemez; kendi taze başarılı okumasının kanıtını gerektirir. L02'nin boş aday/abort varyantı bütün context koşullarını ölçmüş sayılmaz.

### 10.4 Belirsiz girişim

§4.5 tek attempt/key/payload/context kaynağıdır. Mevcut commandAttempt/useCommandAttempt, dirty/pending/company/logout fence ve accessible retry kaydı korunur. Daha sonra gelen409 önceki belirsiz girişimi yok saydıramaz. Fresh valid GET+ack sonrasında yalnız explicit same-key retry; otomatik yeni key yoktur. Eski yardımcı/form düzeltmeleri kapalıdır. §9.7 fc L01 gerçek201+ACK üzerinde kontrollü teslim kaybı→unknown→kendi taze GET transferi→aynı-key replay ile global tek SQL etkisini ayrı doğrular. §9.5 tarihselFAIL, §9.6 LOAD_FAILED ve ölçülmeyen failure/stream/context altları korunur.

## 11. Kalan görevler — mevcut sahiplik ve görevlerin devamı

Aşağıdaki CB/CF/CO/GB etiketleri mevcut #232 görev satırlarıdır; yeni issue, bağımsız görev sistemi veya paralel writer atanması değildir.

### CODEX-BACKEND — #230/#234 mevcut sahibi

**CB-1 — korunan880 kapanışı.** §4.4/§9.1: eski P2, main conflict ve SQL75/RCSI10 kanıtı yeniden açılmaz; eski kapsam için yeni review/test turu istenmez. Yeni kaynak yalnız kendi kanıtına bağlanır.

**CB-2 — CRO009 REAL_SQL teslimi mevcut.** §9.1.1 named None+inactive+stale+permission fact'i ve2b gerçek SQL76/RCSI10 ile özel boşluk kapanmıştır. Aynı testi yeniden yazma/roles3 fixture'ını yeniden analiz etme. Browser/genel CRO009 PARTIAL kalır; bu run'da active-state browser NOT_MEASURED'dır.

**CB-3 — fc kaynak/build/ortam ve temizlik teslimi mevcut.** [Tek fc paketi][fc-final] 5e source, matching local140+18publisher,19tested/18runtimeDLL, kendi8CI/SQL76/RCSI10, gerçek L02/L01, STRICT ve cleanup’ı ayrı verir (§9.7). Bu scoped teslim için yeniden pin/build/run veya aynı testleri tekrar isteme; üç tüketilmiş ENV yeniden kullanılmaz. Kalan canlı varyantlar koordinatörün doğruladığı sırada gerektiğinde aynı Backend’in mevcut şemalı fixture/typed projection/lifecycle alanındadır; yeni gateway, araSQL kapısı veya iş politikası yok.

### CLAUDE-FRONTEND — #233 mevcut sahibi

**CF-1 — önceki ve final kaynak kapanışlarını koru.** 9a/07a/ef/6f/c118/ca19, quiescence/flush/cancelled-copy/malformed-page ve c3 loader düzeltmeleri yeniden implementation işi olarak açılmaz. C3 kendi7CI ile aynı Dirac/Averroes NON-AUTHOR exact-head incelemeleri kaynağında korunur; insan onayı değildir.

**CF-2 — sahiplik ve import tamam.** [İnsan kararı][ownership-approved] ve [3703862][fe-import] geçerlidir;83 yol için yeniden karar/makbuz bekleme. Dört reader, rolsüz formlar ve gerçek command kaynakları aynı dalda mevcuttur. Eski donor/WIP/17Backend sınırı korunur; ikinci uygulama açılmaz.

**CF-3 — loader düzeltmesi sonrası gerçek fc kanıtı mevcut.** C3 loader --list kontrolü yalnız yükleme kanıtıdır; bundan ayrı yeni bağlı fc ortamında L02 ve tek L01 PASS, capture/terminal/barrier sonuçları yayımlanmıştır (§9.7). Ac5 LOAD_FAILED silinmez. Bu dar teslim için yeni koşum talep edilmez; başarısız geçmiş kaynak yeniden görevleştirilmez.

**CF-4 — ölçülen same-key kapanışı ve kalan varyantlar.** Fc iki gerçek201+ACK üzerinde kontrollü teslim kaybı, unknown, kendi taze GET transferi, aynı key/path/body/company replay ve STRICT global tek-etki altını kapatır. Pending-retry abandon/query, ack500/sonraki409/malformed-stream, gerçek IdP/logout ve POST-inflight context live kabulü ölçülmüş değildir. Aynı Claude mevcut harness/form kabul kaynaklarında bu teknik varyantları koordinatör sırasıyla tamamlar; kapanmış helper’ı tekrar yazmak veya yeni Sinan işletme sorusu açmak gerekmez.

**CF-5 — kalan context/read kapsamının sahibi.** L02’de dolu aday seçiminin taşınması ve gerçekten teslim edilen geç gövde/leak ölçülmedi; ERR_ABORTED için uygulama nedenselliği kurulmadı. Aynı Claude ilgili mevcut browser varyantını, gerekirse aynı Backend’in izinli fixture desteğiyle somutlaştırır. Yeni kabul kimliği veya gateway yok; sırf fc teslimini tekrar etmek için yeniden ENV harcanmaz. Controlled010/komponent sonucu actualoldbinary/IdP/gerçek POST-inflight kabulüne yükseltilmez.

### CODEX-ORCHESTRATOR

**CO-1.** Mevcut #230/#234 Backend ve #233 Claude sahipliği korunur; yeni parallel spec/backend/create-link writer yok.

**CO-2.**83 yolun insan kararı ve importu tamam; recipient/donor makbuzu tekrar istenmez veya donor adına üretilmez.17 Backend/WIP sınırı ve #229/B/#232 insan kapıları ayrı kalır. Bu BUSINESS teslim merkezî kayda paralel yazmaz.

**CO-3.** Fc / FEc3 / compiled5e / API880 kanıtı §9.7’de kendi kaynağındadır. 3037 L01RUN_FAILED/L02darPASS/legacy8/globalSQL/cleanup ve ac5LOAD_FAILED/typedZERO/cleanup, iki atıf düzeltmesiyle aynen kalır. 99/99 önceki hash ve234/234 manifest eşliği aktarılmıştır; güncel başarı geçmişi yeniden etiketlemez.

**CO-4.** Tek-node geliştirme kapsamındaki gerçek create/link/unknown/replay/dönüş ve ilgili SQL/failure/cleanup kanıtı artık mevcuttur. Kaynak c3 kendi7CI+Dirac/Averroes,5e kendi8CI+Ampere/James ve matching local build kanıtları yeniden açılmaz. Koordinatör #230 birleşmesinden sonra mevcut expected-head #234→#233 geliştirme merge kapılarını uygular; BUSINESS merge yapmaz. Gelişim merge’i bütün Cari/rollout/production kabulü değildir; #229/#232 insan kapıları ayrıdır.

**CO-5.** İki yönlü eski/yeni istemci-sunucu gereksinimi korunur. Actual oldbinary ve gerçek IdP/logout/POST-inflight context için kalan ortam/kabul sırasını mevcut sahiplerle koordine et; controlled010/legacy8HTTP’yi yeniden etiketleme. Bu altların eksikliği fc’de yeni implementation arızası iddiası değildir. Koşullu mixed-node kapısı ancak gerçek topoloji bunu gerektirirse geçerlidir; mevcut tek-node için gateway veya araSQL kapısı açılmaz. Yeni işletme kararı/Sinan sorusu yok.

### CHATGPT-BUSINESS

**GB-1.** Yalnız mevcut #232 dosyası exact-source kanıtla güncellenir; kanonik/shared ilerleme kaydına paralel yazılmaz.

**GB-2.** CRO-AC-001…012 ve eski CR eşlemesi korunur; gerçek run alt sonuçları bunlara bağlanır. Ölçülmeyen davranış PASS'e çevrilmez.

**GB-3.** Mevcut fc scoped kabul aktarımı tamamlanınca yalnız yeni anlamlı kanıtta aynı matris güncellenir; test sayısı/makbuz için ek tur yok. Aşağıdaki otomatik şirket ilişkisi pre-spec’i ve onaysız B korunur; sonraki dilim dispatch’i veya bütün ERP analizi açılmaz.

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

#229 ve #232 insan review/acceptance kapıları açık kalır. Bu dosya yalnız gerçek paketin kaynak/test/kanıt eşlemesini günceller; görevi veya kanonik kabulü DONE yapmaz. BUSINESS yeni uygulama göndermez, backend/frontend değiştirmez, merge/production yapmaz. Yerel ham run dosyalarını root doğrulamıştır; BUSINESS yayımlanmış kaynakları ve root devrini işlemiştir. Ürün testleri BUSINESS tarafından yeniden çalıştırılmamıştır. Fc L01 PASS/L02darPASS ve STRICT/cleanup yalnız ölçülen kapsam içindir; 3037RUN_FAILED/ac5LOAD_FAILED ve bütün tarihselENV/readyForBrowserNow=false korunur. Kanonik kabul veya production onayı verilmez.

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
[ac5-final]: https://github.com/sinangym/GoymenERP/pull/234#issuecomment-5641344986
[ac5-pointer]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5641345193
[ac5-env]: https://github.com/sinangym/GoymenERP/pull/234#issuecomment-5641243814
[ac5-loader]: https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5641332379

[fc-final]: https://github.com/sinangym/GoymenERP/pull/234#issuecomment-5641808223
[fc-l01]: https://github.com/sinangym/GoymenERP/blob/c3cb45e1f85a7079c742bc0abbe62ca3aa805dfe/tests/e2e/cari-acceptance-009/specs/l-01-live-ab-journey.spec.ts
[fc-l02]: https://github.com/sinangym/GoymenERP/blob/c3cb45e1f85a7079c742bc0abbe62ca3aa805dfe/tests/e2e/cari-acceptance-009/specs/l-02-pending-context-change.spec.ts
