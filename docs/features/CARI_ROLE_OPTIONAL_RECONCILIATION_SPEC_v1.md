# Cari Opsiyonel Ticari Rol Uzlaştırma Şartnamesi v1

- Tarih: 11 Eylül 2026
- Şerit: `CHATGPT-BUSINESS`
- Durum: **REVIEW EDİLEBİLİR İŞ ŞARTNAMESİ / ERP KABULÜ DEĞİL**
- Dosya: `docs/features/CARI_ROLE_OPTIONAL_RECONCILIATION_SPEC_v1.md`
- Mevcut PR: **#232**; yeni belge veya paralel şartname değildir.
- Hazırlık tabanı: `main@060da63f2644cb271d78d9eab8b7a6985d544ad1`
- Önceki belge revizyonları: `1299a6ee9f817c552ee08bbe385d4cb12c11bb8e` → `4e063e26f2c5848f7330891016d99a82088efa7b` → `5b3c8d80bf48bb18bd4b27b17006237429c04257` → `def6ff7faa8ad14e4e6d48d2b12fee5f572610aa` → `24f751ad489b4d1d2bb3a7b900fd6d279b9b534e`. Bu toplu güncelleme mevcut içeriğin devamıdır.
- Öncelikli transport kaynağı: [#233 / 5626451657](https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5626451657), backend `080e7a1d24f74975b45546c5277f0529c13f06d1`.
- Koşullu hata başlığı düzeltmesi: [#230 / 5630622096](https://github.com/sinangym/GoymenERP/pull/230#issuecomment-5630622096), backend `880cae838e5d2f01a988f1e4a89afbe67858f48c`.

Bu belge uygulama kodu yazmaz, kanonik karar/task durumunu değiştirmez, `docs/planning/2026-09-10/**` veya paylaşılan ilerleme belgelerinin tek-yazar sahipliğini devralmaz. PR #229 karar paketi ile #232'nin uygulanabilir insan inceleme/kabul kapıları korunur. Kaynak göstermek, bu kapıları geçmek veya production yetkisi vermek değildir. Otomatik şirket ilişkisi için `B` yalnız öneridir; kullanıcı kararı değildir.

- Bu güncellemenin tetikleyicisi: **CRO-AC-009 özel REAL_SQL alt varyantının #234/2b21e3b ile gerçekten çalışması**; test sayısı veya makbuz için ayrı faaliyet değildir. [Exact test][cro009-source] ve [gerçek SQL job][cro009-job] §9.1.1'e bağlıdır.
- Frontend kaynak zinciri: önceki [ef dar teslimi][fe-final] korunur; [insan sahiplik kararı][ownership-approved] sonrası [3703862 importu][fe-import] ve `c1182427bbd9ddd80a778b51ac529dbac1a1cc56` gerçek form/komut adayı vardır. İnceleme kesitinde yayımlanmış yeni baş `ca19ac1107be1887a5261021a38c1f4d5e32d094`; [yazarın retry düzeltme teslimi][fe-retry-fix] bağımsız final kabul yerine geçmez.
- C006 kaynak zinciri: #234/`2b21e3bcc55518f6bf6c993f7e9738ffbb4fd284` → `1d022d824aa2cc7623989bef84397da2923c5304` → `e35ad3815a8aaf5b1f5b1fb5591bc48427600031`. SQL76 kanıtı ilk SHA/test-merge'ine aittir; sonraki kaynak/yayın düzeltmelerine otomatik taşınmaz. [Merkezî kayıt][co-record] ve [e35 dar kapanışı][c006-publisher-review] §9.4'te ayrılır.

## 1. Kaynak önceliği ve güncel depo gerçeği

Öncelik: `AGENTS.md` ve uygulanabilir governance altında en yeni açık kullanıcı kararı → tarihli kullanıcı kararları → `KARAR_SICILI` → Master Rapor → ilgili feature/ADR → eski referanslar. Açık yetki, güvenlik, migration veya production politikası tahminle kapatılmaz.

11 Eylül 2026 kaynak kesiti:

- #227 merge edilmiştir; main `060da63f2644cb271d78d9eab8b7a6985d544ad1`. Mevcut create/list/detail/company-link API yolculuğunun gerçek-SQL baseline'ıdır; yeni roleless frontend/transport veya production kabulü değildir.
- #228 `d46f7f819456fc578a25cdcabf5c98ad6f634456` yayımlanmış donor kaynağı korunur. **83 yolun aktif yazarlığı [HUMAN-OWNERSHIP-TRANSFER5632970696][ownership-approved] ile aynı Claude/#233'e verilmiş, [3703862ce28bfc5bee55248e5ca7d2ef5e46fe28][fe-import] ile seçmeli import yapılmıştır.** Bu soru artık onay beklemiyor. Donör HANDOFF-RELEASED değildir; mevcut recipient5626181170 tekrar istenmez. Bilinmeyen LENOVO WIP alınmış sayılmaz;17 Backend yolu aynı sahibinde kalır.
- #229 `e1fd69da69df36a72e559ea07163fb3f359b3c61`, insan incelemesi gereken açık/draft karar paketidir.
- #230 `080e7a1d24f74975b45546c5277f0529c13f06d1`, main uzlaştırması ve yeni transport gate'in önceki kanıt başıdır: 7 kontrol, SQL75/RCSI10, sıfır skip. James'in bu SHA'daki conditional error-header P2'si tarihsel olarak koşulludur; geriye dönük koşulsuz onaya çevrilmez.
- #230 `880cae838e5d2f01a988f1e4a89afbe67858f48c` için beş dosyalık OpenAPI/handoff/test P2 düzeltmesi, James/Ampere dar fark incelemeleri ve kendi fresh CI/SQL75/RCSI10 kanıtı korunur. Bu tur aynı kapanmış P2, eski main conflict'leri veya SQL kapsamı yeniden açılmadı; ayrıntı §9.1.
- #234, #230/880 tabanlı aynı Backend sahibinin C006 adayıdır. `2b21e3b` içinde manifest ve özel roles=None+inactive+stale+permission SQL fact'i yayımlanmış; SQL76/RCSI10 skip0 gerçekten çalışmıştır. **Önceki “manifest hiç üretilemedi / özel SQL testi yazılmadı” statüleri tarihsel kalır.** Son yayımlanmış baş `e35ad3815a8aaf5b1f5b1fb5591bc48427600031`; kaynak/yayın P2 zinciri, yeni-head doğrulama sınırı ve ENV_READY/whole-browser NOT_RUN §9.4'tedir.
- #233'te dört reader ve gerçek create/link form-command bağlantısı mevcuttur. `c1182427bbd9ddd80a778b51ac529dbac1a1cc56` için25 dosya/458 test ve7/7 CI koordinatörce doğrulanmış; [Dirac][fe-dirac] ve [Averroes][fe-averroes] incelemeleri tamamlanmış, üç somut retry yolu bırakmıştır. Bunlar kaynak incelemesidir, insan veya bütünleşik kabul değildir.
- İnceleme kesitinde #233 başı `ca19ac1107be1887a5261021a38c1f4d5e32d094`, main060 tabanlı açık/draft adaydır. Yazar üç retry yolunu düzelttiğini ve462 testi bildirir; yeni head'in bağımsız kapanışları/tüm CI kapıları bu dosyada koşulsuz PASS ilan edilmez. c118 bulguları ile ca19 düzeltme iddiası ayrı tutulur (§9.2).
- #231/173b003 ve #233'ün424/9a/07a/ef/6f kapanışları tarihsel kaynaklarında korunur. Eski izin/header P1'i veya kapanmış helper/transport hedefleri yeniden görevleştirilmez. Formun mevcut olması, gerçek API/SQL üzerinde tam create→list→detail→B-link→dönüş/context/unknown/retry kabulü değildir: **INTEGRATED_ROLELESS_BROWSER NOT_RUN**.

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

**CRO-AC-001/003/004/008/010/011 — gerçekleşen dar düzeltmeler:**9a6b43d'de başarılı GET sonrasındaki fetch rejection/offline eski hazırlığı bırakıyordu; [07a teslimi][fe-fix] bu yolu liste ve detay transport'unda revoke ile düzeltmiştir. Saf503 gözlem testiyle eş tutulmaz; aynı transport'ta başarılı okuma→cevapsız ağ hatası/offline dizisi testlidir. İlgili helper/transport hedefi yeniden AÇIK yazılmaz. Form/komut bağlantısı artık c118 adayında mevcuttur; gerçek server/browser kabulü ayrıca NOT_RUN'dır.

[Ef43700 dar teslimi][fe-final] ve [iki][fe-review-1] [non-author inceleme][fe-review-2] şu iki kalan P2'yi yalnız yeni dört-dosya farkında kapatır: (1) `beginContractRead` bağlamı istek çıkarken kaydeder; OLD bekler→NEW valid+ack200→OLD geç valid+ack200 dizisinde eski gözlem NEW grant'ini ne değiştirebilir ne geri alabilir. (2) `response.json()` sırasında aborted signal, `fence-stale` ve readiness'e dokunmayan sonuçtur; gerçek, aborted olmayan malformed body hâlâ readiness'i geri alır. Abort, sunucunun uyumsuzlaştığı kanıtı değildir.

Kanıt sınırı: [transport testleri][fe-transport] liste/detay deferred OLD-success yollarını ve body-stage abort/malformed ayrımını kapsar. Abort testleri pre-aborted signal + mock body rejection kullanır; gerçek response-stream zamanlaması veya komut DOM'u kanıtlanmış değildir. c118 form component testleri create probe auth/tipli hata ve link readiness/disabled/POST0 yollarını ayrıca kapsar; bunlar yalnız helper sayılmaz ([Dirac][fe-dirac], [Averroes][fe-averroes]). Buna karşılık retry ön kontrolünün kendi taze okumasına bağlanması, iptal/arama değişiminde eski cache/readiness ile gönderim yapmaması §9.2'de ayrı kapanış kapısıdır. Gerçek API/SQL/browser varyantları NOT_RUN kalır; kapanmış helper/transport kapsamına ikinci paralel düzeltme veya eski review turu açılmaz.

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

Kalan kabul: unknown → retry ön kontrol GET pending → kullanıcı bırakır veya aday aramasını değiştirir → geç/cache'e dönen/iptal edilmiş kontrol **yeni POST üretmemeli**. Retry kendi taze, başarılı, decoder-valid ve ack'li okumasını gerektirir; yalnız refetch promise'inin çözülmesi veya eski readiness boolean'ı yetmez. Unknown mevcutken üst uyumluluk uyarısı da “hiçbir istek gönderilmedi / sayfayı yenileyin” diyerek girişim/key kaybına yöneltmemeli. c118 bulguları ve ca19 yazar düzeltmesi §9.2'de kaynaklandırılır; final bağımsız kapanış ile gerçek API/browser kanıtı ayrı kalır.

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

Test tasarımı PASS değildir. Her sonuç exact source, gerçek checkout, run/job ve kapsama bağlıdır.080/880, önceki frontend başları, c118/ca19 ve2b/1d/e35 sonuçları birbirine otomatik taşınmaz. Kaynak incelemesi, yazar/koordinatör log aktarımı, doğrudan CI günlüğü okuması ve yeni test çalıştırma ayrı tutulur. Bu tur CRO009 kaynağı ve gerçek SQL job günlüğü okundu; ürün testleri CHATGPT-BUSINESS tarafından yeniden koşturulmadı. B-C01-001…010 ve protokolün8 browser ölçümü CRO/CR kimliklerinin yerine geçirilmez.

## 9. Kanıta bağlı kabul matrisi

Her satırda başlangıç/eylem, beklenen kayıt ve yasak etki belirtilmiştir. Korunan backend kapsamı880cae8/969a4a8; **yeni CRO009 REAL_SQL kanıtı2b21e3b/45dc98d** koşumudur (§9.1.1). c118 gerçek form/component kanıtı ile ca19 yazar düzeltme teslimi §9.2'de ayrılır. Tam integrated create→list→detail→B-link/dönüş/failure/retry bütün ilgili satırlarda NOT_RUN kalır.

| Kabul | Başlangıç, eylem ve beklenen/yasak etki | Backend REAL_SQL / HTTP | Frontend unit / stub / component | Controlled / C-01 browser | Bütünleşik kabul ve kalan |
|---|---|---|---|---|---|
| CRO-AC-001 | Yetkili A kullanıcısı; taze valid GET+teyit, sonra roles eksik/0 create. Bir Party+A account, roles0, ilgili audit/durable result. Eski server200/no-ack varyantında POST0, kayıt0. | OptionalRoles SQL missing/zero create ve command-gate senaryoları;880 broad75 kanıtı korunur. SQL, frontend'in önce GET yaptığını kanıtlamaz. [SQL kaynak][be-sql], §9.1. | Gerçek create formu/probe/command bağlantısı c118'de mevcuttur; probe401/403/tipli hata component kontrolleri iki incelemede kapalı. ca19 retry farkı ayrıca §9.2. | #228 controlled create tarihsel; C-01 oluşturma kanıtı değil. | NOT_RUN: pinli gerçek API/SQL form GET/ack→POST201→detail; old-server ve ağ-hatasında yeni POST/PUT0, retry iptali ve geç yanıt yolları. |
| CRO-AC-002 | Eski1/3/15 kayıtlarıyla upgrade; kimlik/rol/audit/replay değişmez. Zero sonrası Down reddi; DDL/veri kaybı yok. | OptionalRoles migration/legacy/Down senaryoları broad75 içinde PASS. [SQL kaynak][be-sql], §9.1. | N/A; UI testi SQL migration kanıtı değildir. | N/A; production migration yapılmış değildir. | Backend scope kanıtlı; yeni production/kanonik kabul yok. |
| CRO-AC-003 | Party A'da görünür, B account yok. B bağlamında taze valid GET+ack; `{}` veya roles0 link. Aynı Party, tek B account, A değişmez. A teyidiyle B yazması yasak. | OptionalRoles create/link ve target isolation SQL;880 broad75 PASS. [SQL kaynak][be-sql], §9.1. |83 kaynak import edilmiş; mutation/candidate ve rolsüz link formu vardır. c118 `canSubmit/writeReady`, ack'siz aday cevabında seçim/disabled/POST kontrolleri component kapsamındadır. | Eski roleful L-01/controlled link yeni roleless kabul sayılmaz. | NOT_RUN: gerçek B-context fresh readiness, aynı Party/tek B account; abandoned retry sırasında arama/iptal değişiminde yeni POST0. |
| CRO-AC-004 | Yetkisiz/foreign Party ve şirket varyantları. Doğru başlık grant vermez; mevcut403/404, kimlik sızıntısı ve business etki0. |880 authorization/isolation korunur. Yeni CRO009 testinde viewer/foreign403 ve currentVersion/AccountId non-disclosure ayrıca gerçek SQL'de kanıtlı. [Exact test][cro009-source]. | Ef geç yanıt/abort kapanışları korunur; c118 create probe merkezi auth boundary bağlantısı ve link disabled davranışı gerçek component kapsamıdır. | C-01 regresyonu komutların tüm context/stream yollarını kanıtlamaz. | NOT_RUN: gerçek şirket/oturum değişimi, pending/abandon/query değişimi ve yetkisiz komut/SQL etkisi0. |
| CRO-AC-005 | Command sonucu unknown; aynı attempt/key/company/Party/path/payload korunur; tazeGET+ack sonrası açık same-key retry. İkinci etki/otomatik yeni key yok. |880 durable replay/protocol kanıtı korunur. Yeni CRO009 stale409 replay'inde idempotency ve snapshot eşliği ayrıca testli; bu create/link DOM replay'i değildir. | c118 `unresolvedSince` ve DOM'da unknown→retry→compatibility409 aynı-key kanıtı vardır. Retry pending bırakma/query değişimi ve yanlış reload uyarısı c118 inceleme bulgularıdır; ca19 yazar düzeltmesi final kapanıştan ayrıdır. | Controlled unknown/replay gerçek kalıcı server kanıtı değildir. | NOT_RUN: gerçek server commit/replay+tek etki; bırakma/iptal sonrası yeni POST0; unknown'da success/clear/navigation0. |
| CRO-AC-006 | İki farklı key aynı Party/B link yarışı; ayrıca audit/idempotency hata enjeksiyonu. Tek target account/başarılı created audit; yarım write yok. | OptionalRoles different-key race + audit/idempotency rollback; broad75 PASS. [SQL kaynak][be-sql], §9.1. | N/A; frontend birim testi SQL yarış/rollback yerine geçmez. | N/A; bu SQL kabulünün browser karşılığı uydurulmaz. | Backend scope kanıtlı. Kaybedenin durable conflict'i ikinci hesap değildir. |
| CRO-AC-007 | Eski1..15 ve yeni0 durable sonuçlar; uyumlu declaration ile replay. Eski hash/snapshot korunur, yeni business etki yok. Headerless replay command'i reddedilir. |880 legacy/roleless replay korunur.2b CRO009 aynı-key stale409 replay ve recorded-key reauthorization kanıtı §9.1.1'de ayrıca. | Komut/attempt bağlantısı artık mevcuttur; helper ile sınırlı diye gösterilmez. c118/ca19 retry kanıt düzeyleri ayrıdır. | Eski controlled replay ve normal API smoke gerçek browser unknown recovery değildir. | NOT_RUN: kabul edilen final form/server bileşiminde aynı key/payload/context, kalıcı replay ve tek iş etkisi. |
| CRO-AC-008 | List/detail/candidate roles0; filtre/count; GET DB değiştirmez. Visible0 legacy projection409; gizli foreign0 sızmaz. |880 read/no-write/protocol kanıtı korunur;2b CRO009 pasif roles0 detail GET sonrası snapshot değişmezliği de testlidir. | Dört reader0..15 ve form bağlantısı yayımlanmıştır; eski “mutation/candidate devri yok” hükmü geçersiz. c118 component ve önceki helper/transport kanıtları farklıdır. | C-01/protocol8 liste regresyonu tam dört-reader/write kabulü değildir. | PARTIAL / integrated NOT_RUN: gerçek dört-reader/form ve retry'ın kendi taze GET'i; cached promise/eski readiness'ten komut çıkmaması. |
| CRO-AC-009 | Roles=None hesap; yetkili pasifleştirme→version2; stale1→409/replay; viewer/foreign yetki reddi; yetkili reactivation→version3. Rol/aktiflik/yetki ayrımı korunur. | **REAL_SQL alt varyant PASS @2b21e3b / checkout45dc98d.** `[Trait("AcceptanceId", "CRO-AC-009")]` taşıyan [named fact][cro009-source]; [job103280752609][cro009-job] SQL76/RCSI10 fail0/skip0; named TRX1/1 koordinatör aktarımı ayrı. Stale409 yalnız durable result etkisine izin verir; business/audit sabit; replay ve denial snapshot'ları değişmez. | Roleless active-state'in gerçek form/browser kabulü bu SQL testiyle kanıtlanmaz. | **NOT_RUN**; SDK103 veya C006 BackendVerify browser değildir. | **PARTIAL toplam / REAL_SQL özel boşluk kapalı / browser ve integrated NOT_RUN.** Yeni test yazdırma veya eski roles3 fixture'ını yeniden görevleştirme yok; ENV_READY/ERP kabulü çıkarılmaz. |
| CRO-AC-010 | İki yönde eski/yeni client-server; logout/company/session/API/late-response. Current valid GET+ack readiness; teyitsiz yazma0. Karma havuz yalnız doğrulanırsa §4.6. |880 protocol ve2b76 SQL kendi kapsamlarında; yeni e35 kaynağına veya frontend bileşimine otomatik taşınmaz. | Form/probe/readiness mevcut; c118 retry'da cached/aborted kontrol ve bırakma yolları incelenmiş; ca19 düzeltme adayı ayrı. | C-01 veprotocol8 yalnız regresyon; gerçek old-binary/command DOM/unknown/retry kapsamı değildir. | PARTIAL / CR-10 NOT_RUN; C006 son FE/backend/build/fixture bağları ve iki yönde gerçek kabul gerekir. Yeni gateway/drain işi yok. |
| CRO-AC-011 | Request geçersizroles/header; response0/15 geçerli, missing/null/16 geçersiz. Hata+ack success/readiness değil; error-no-ack eski server kanıtı değil. |880 HTTP/OpenAPI ve SQL ayrımı korunur; özel CRO009 permission/non-disclosure ilavesi kendi fact'ine bağlı. | Dört decoder/form hata bağlantısı mevcuttur; c118 typed probe/compatibility ayrımı component'te görülür. Unknown recovery üst uyarısının ca19 düzeltmesi kaynak/CI/review ayrımıyla tutulur. | Liste/header regresyonu500/409 gerçek command recovery veya stream testi değildir. | PARTIAL / integrated NOT_RUN: yanlış reload/“gönderilmedi” ile key kaybı ve hatadan false-ready/success üretilmemeli. |
| CRO-AC-012 | Roleless create/link valid mutation+ack ile tek success/detail; güncel yetki/cursor ile dönüş. Unknown'da success/clear/navigation0. |#227/880 backend journey ve2b SQL kayıt etkileri ekran odağı/dönüş kanıtı değildir. | Gerçek form/komut ve return bileşenleri vardır; c118 üç retry bulgusu/ca19 düzeltmesi ayrı. Önceki sınırlı taze cursor walk korunur. | Tam roleless L-01/live journey ve dönüş henüz çalıştırılmış kabul değildir; eski role3/2 browser örneği uyarlanmalıdır. | **NOT_RUN:** gerçek create→list→detail→B-link→dönüş + context/failure/retry; warning “bulunamadı” kayda dönüş PASS'i değildir. |

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

#### 9.1.1 CRO-AC-009: yeni gerçek SQL alt varyantı

Kaynak: [#234/2b21e3b named fact][cro009-source], `RolelessInactiveAccount_StaleVersionAndPermissionGatesRemainEnforced`; `[SqlServerFact]` ve mevcut `[Trait("AcceptanceId", "CRO-AC-009")]`. Yeni acceptance kimliği değildir.

| Başlangıç / işlem | Gerçekte assert edilen sonuç | Oluşmaması gereken etki |
|---|---|---|
| A şirketinde roles=None/0 hesap oluştur; yetkili writer expectedVersion1 ile pasifleştirir. |204; aynı hesap roles0, inactive, version2; ilgili audit +1. |Rolün legacy değere çevrilmesi veya pasifliğin rol yokluğu ile aşılması. |
| Aynı pasif hesaba expectedVersion1 ile reactivation dene. |409 version_conflict; expected1/current2; Party/account/audit snapshot değişmez. Terminal409'un durable idempotency kaydı oluşturması izinlidir. |Aktifliğin, version'ın, business kaydın veya audit'in değişmesi; bütün durable etkileri körce0 şart koşmak. |
| Aynı stale key ve yeni correlation ile replay. |409 ve replay/orijinal correlation başlıkları; afterStale snapshot bütünüyle aynı. |İkinci business/audit etkisi veya ikinci sonuç gibi davranma. |
| Viewer ve yabancı-company aktörü, hem version1 hem2 ile dener; viewer eski kaydedilmiş key'i de kullanır. |403; body'de currentVersion veya accountId yok; recorded key yetkiyi aşmaz; snapshot değişmez. |Version/kimlik sızıntısı, permission grant veya business/audit/durable değişikliği. |
| Reader detail GET, sonra yetkili writer expectedVersion2 ile aktifleştirir. |GET roles0/inactive/version"2" ve no-write; yetkili204 sonrası aynı hesap roles0/active/version3, toplam ilgili audit +2. |GET'in hesap açması/aktifleştirmesi, izinsiz reactivation veya tekrar audit. |

**Doğrudan okunan gerçek SQL kanıtı:** [CI34604875177 / job103280752609][cro009-job]. Günlükte13:32:27Z checkout `45dc98dcd21590be24916a36c768bce6d45ed1a9` = `2b21e3bcc55518f6bf6c993f7e9738ffbb4fd284` into `880cae838e5d2f01a988f1e4a89afbe67858f48c`. Bu test-merge ne main060 ne de daha yeni e35 diye yeniden adlandırılır.

- 13:38:02Z: gerçek .NET SQL koşumu **76/76 PASS, failed0/skipped0**.
- 13:39:38Z: gerçek RCSI koşumu **10/10 PASS, failed0/skipped0**.
- 13:33:27Z unittest/verifier çıktısındaki MOCK SQL satırları kabul kanıtına dahil değildir.
- Envanter69 discovery/76 execution'a çıkmıştır; eski zorunlu testler ve skip-zero/omission sınırı korunur.
- [Koordinatör][co-record], ayrıca aynı kaynak ön-commit named CRO009 TRX1/1, SDK103/103 ve architecture253/253 dosyalarını ve hashlerini okuduğunu bildirir. Bu ayrı yerel TRX/hash aktarımıdır; BUSINESS bu dosyaları yeniden çalıştırmış veya ham TRX/hashleri yeniden üretmiş sayılmaz. Kaynakta verilmeyen yeni hash uydurulmaz.

**Hüküm:** yalnız önceki özel REAL_SQL boşluğu kapalıdır. CRO-AC-009 toplamı **PARTIAL**, browser/entegre alt kabul **NOT_RUN**; C006 ENV_READY, tüm Cari kabulü veya production onayı yoktur.2b'nin bu gerçek sonucu1d/e35 ya da daha yeni FE başına otomatik taşınmaz.

### 9.2 Frontend ve browser kanıt sınırı

**Güncel yayımlanmış baş `ca19ac1107be1887a5261021a38c1f4d5e32d094`; bu turdaki tamamlanmış bağımsız form incelemesi `c1182427bbd9ddd80a778b51ac529dbac1a1cc56` içindir.** [9a makbuzu][fe-handoff], [07a teslimi][fe-fix], [ef teslimi][fe-final] ve önceki6f kapanışları yeniden görevleştirilmez. Import/form, helper, component, browser ve final kabul farklıdır.

| Kanıt | Exact kaynak / doğrulama düzeyi | Kanıtlamadığı |
|---|---|---|
| Onaylı import ve uygulama | [3703862][fe-import], ebeveyn6f2dfe8+d46;83 seçilmiş yol,82 birebir blob ve contract.ts uzlaştırması koordinatörce doğrulanmış. c118 dört reader0..15, rolsüz create/link ve gerçek probe/command/attempt bağlantısı içerir. |Donör WIP'i alınmış sayılmaz; import veya kodun varlığı gerçek API/browser kabulü değildir. |
| Tamamlanmış form/component incelemesi | [Dirac][fe-dirac] ve [Averroes][fe-averroes], parent8f8e241→exact c118, temiz başlangıç/son head: unknown'ın sonraki compatibility/local ret ile silinmemesi, create probe auth/tipli hata ve link submit readiness bağlantısı kapalı. Create DOM'da same-key/unknown, link disabled/POST kontrollerini helper'dan ayrı değerlendirirler. |İncelemeler test çalıştırma veya insan kabulü değildir; üç retry yolu bırakmıştır. |
| c118 CI | [CI34604086840](https://github.com/sinangym/GoymenERP/actions/runs/34604086840), [frontend job103278176900][fe-form-ci], C-0134604086871 ve UI34604086953:7/7 ve25 dosya/458 PASS koordinatörün gerçek-log okumasıdır. Checkout `37e32039baa10c809f73f676a31dd8c318aa910c` = c118+main060. |458'in tamamı component değildir; bu tur yeniden koşturulmadı. #234/2b ya dae35 ile tam bütünleşik bileşim değildir. |
| c118'de kalan üç somut yol | Dirac: pending retry GET sırasında bırakma sonrası geç POST; Averroes: abandoned link retry sırasında query/cancel-revert'in eski readiness ile POST üretmesi; ikisi: unknown varken üst uyarının reload/“hiçbir istek gönderilmedi” demesi. Severity Dirac1P1+1P2, Averroes2P1; ortak reload bulgusu mükerrer sayılmaz ve kapıda P1 olarak ele alınır. |Bunlar eski ef/6f kapanışları değildir. Tamamlanmış review “bekleniyor” diye gösterilmez. |
| Sonraki ca19 düzeltme adayı | [Yazar teslimi5637323519][fe-retry-fix]: retry generation, gerçek yeni read-sequence şartı ve unresolved üst uyarısı;4 yeni hook/DOM testi,25 dosya/462 PASS ve tsc/lint raporu. Kaynak başı PR metadata'sından doğrulandı. |Bu turun okunan kaydında yeni final-head bağımsız kapanış/tüm CI teyidi bekleniyor. Yazar “kapandı” ifadesi c118 review'ını geriye dönük koşulsuz onaya veya integrated PASS'e çevirmez. Browser bu dar turda çalıştırılmamış. |
| Korunan ef helper/transport/component | Ef CI'da saf yardımcı32, stub-transport21, liste component84, detay19; toplam18 dosya/324. Gerçek checkout `ee6a2deaad920522332f5c0bbcb070b1a3f5d620`, [job103192346328][fe-ci-job]. [İki][fe-review-1] [dar inceleme][fe-review-2] önceki iki P2'yi yalnız dört dosyalık farkta kapatmıştır. |Mock body rejection gerçek stream değildir; eski helper sonucu yeni form/retry kapanışının yerine geçmez. Eski kapsam yeniden incelenmez. |
| Korunan C-01/protokol browser | [Ef yazar teslimi][fe-final]:880+ef, fixture54/55, B-C01-001…01010PASS veprotocol8PASS, teardown raporlu. Ef CI34577227055/C-0134577227048/UI34577227054 kendi7/7 sonucudur. |Create→list→detail→B-link→dönüş/failure/retry kanıtı değil; ef sayıları c118/ca19 üzerinde tekrar çalışmış gibi gösterilmez. C006 ENV_READY sayılmaz. |

**Tarihsel zincir korunur:**9a6b43d için302 test, CI34573865458/C-0134573865597/UI34573865578 ve yazarın880+9a yerel8e83fb1 list10/protocol8 raporu kendi kaynağına aittir.07a4af2 için319 test ve [CI34575684274](https://github.com/sinangym/GoymenERP/actions/runs/34575684274)/[C-0134575684283](https://github.com/sinangym/GoymenERP/actions/runs/34575684283)/[UI34575684265](https://github.com/sinangym/GoymenERP/actions/runs/34575684265)7/7 ayrı geçmiş kanıttır; o tur browser tekrarlanmadığı [teslimde][fe-fix] belirtilir.07a incelemelerinin iki P2'si tarihsel olarak korunur, ef kapanışı geriye dönük koşulsuz eski onay üretmez.

**Tarihsel424a586:** Node24 suite267/267, C-01 list10/10 ve ayrı16/16 liste davranışı raporu kendi kaynaklarında korunur. [Eski devir dosyası](https://github.com/sinangym/GoymenERP/blob/424a586fc07efa291db4b37866ba5cce54fb7443/src/frontend/goymen-erp-web/CARI_FRONTEND_DEVIR_2026-09-10.md) ve eski #231266/266 kaydı tarihsel kapsamındadır. Node26'daki iki yerel hata yeniden mandatory CI arızası diye açılmaz.

Liste dönüşü artık yalnız uyarı değildir: ilk sayfa taze okunur, gerekirse en çok dört ek sayfaya güncel cevap cursor'ıyla yürünür; eski cursor replay edilmez. Bu sınır mevcut implementation parametresidir, yeni genel işletme politikası değildir. Kayıt bulunursa odak döner; bütçede bulunamaması gerçek dönüş başarısı değildir. Detail capability kapalı tarihsel C-01 yığını bu walk'ı veya create/link/detail zincirini tarayıcıda kanıtlamaz; mevcut C006 dört-capability hazırlığı ayrı kaynaktır.

### 9.3 Donor ve kontrollü browser sınırı

#228 recipient `HANDOFF-ACCEPTED`5626181170 vardır; tekrar istenmez. [HUMAN-OWNERSHIP-TRANSFER5632970696][ownership-approved] verilmiştir; [83 yol/blob önerisi][fe-ownership] artık cevapsız bir soru değildir. Aynı Claude/#233 aktif tek yazardır; [3703862 importu][fe-import] yayımlanmıştır. Karar donor HANDOFF-RELEASED diye imzalanmaz. Eski dal/geçmiş/kanıt korunur; unpublished LENOVO C023/F024/F025 kaynakları devralınmış değildir.17 backend/test/lifecycle/CI yolu aynı Backend sahibinde kalır. Bu sahiplik kararı #229/B/#232 insan kabulü, merge veya production açmaz.

[Koordinatörün controlled evidence kaydı](https://github.com/sinangym/GoymenERP/pull/228#issuecomment-5624268419): run34103473977 artifact10011561394; SDK80/80, helper54/54, controlled Chromium17/17; test-merge checkout2ebcbc7 ile d46 kaynak ağacının eşliği ayrı doğrulanmıştı. `persistentServerReplayClaimed=false`, `erp_accepted=false`; bu sayılar gerçek roleless server/browser birleşimi veya yeni protocol kabulü değildir. CSS/DPR eşdeğerleri gerçek browser zoom/screen-reader konuşması sayılmaz.

### 9.4 C006: yayımlanmış hazırlık, source-binding ve runtime sınırı

[Merkezî kayıt][co-record]: önceki CreateProcess reddi aynı Backend görevi ve yerleşik sınırlı onaylarla çözülmüştür; başka ajan/araçla aşılmamıştır. Mevcut CODEX-BACKEND `01a0865c-1990-7783-8aee-4aee15618b31`, #228/d46 kaynaklı17 yolun tek sahibi olarak manifesti üretmiş ve #234'ü yayımlamıştır. Eski “hiç yazılamadı/manifest yok/özel SQL yok” hükümleri tarihsel kalır; her yeni araç ret/onayı yine kendi kapsamında ele alınır.

| Ayrı durum | Gerçek kaynak / kanıt | Kalan sınır |
|---|---|---|
|17 kaynak ve manifest | [#234][c006-pr] provenance: bütün17 çalışma kopyası byte-eşliği raporlu; source commit/path/Git blob/ham SHA256/boyut. İlk yerel CRLF manifest SHA256 `460fbb5f26533098b87743c3056c821d90cde2d3bae9d3c194547f95aa92a9a6`; yayımlanmış LF blob SHA256 `aa958fe261d29097c92129dce5451e6271556ea7a1e67f7f03ba458c1b66ae4e`. |İki serileştirme aynı ham hash değildir. Manifest, çalışan host veya memory-image attestasyonu değildir; eski WIP/DPAPI korunur. |
|2b CI/BackendVerify | [CI34604875177][c006-ci], C00634604875174, C-0134604875197, UI34604875279 toplam8/8 koordinatörce doğrulanmış. C006 artefaktı10265439602: checkout45dc98d; SDK103/103 skip0, scan11dosya/0bulgu; `publish_ready=false`, `erp_accepted=false`, Chromium/helpers NOT_RUN. |BackendVerify success, bind/setup/start/smoke/publish veya ENV_READY değildir. Immutable8f scanner kaynağı final frontend entegrasyon pini değildir. |
|CRO009 ve diğer yerel testler | §9.1.1 gerçek SQL76/RCSI10 doğrudan log kanıtı; named1/1 ve architecture253/253 ayrı koordinatör TRX aktarımı. |Architecture/SDK/mock çıktısı gerçek SQL veya browser diye sayılmaz. |
|2b→1d kaynak P2'si |2b'de untracked managed build-input SourceBinding P2 açıktı. [1d022d8 teslimi][c006-source-fix] üç dosyalık düzeltme; koordinatör hedef11 test ve iki beklenen BackendVerify reddini doğrulamış, yazar Ampere/James kapanışını aktarmıştır. James o SHA'da ayrı publisher build-hash bağlama P2 bırakmıştır. |Tarihsel2b/1d review'ları geriye dönük kusursuz onaya çevrilmez; beklenen negatif ret ortam PASS'i değildir. |
|İnceleme sırasında yayımlanan e35 | [e35ad38 teslimi][c006-publisher-fix], parent1d: test-output DLL envanteri/önce-sonra hash ve runtime-test eşliği, tek validated summary buffer. [Aynı iki bağımsız AI reviewer'ın exact e35 kapanışı][c006-publisher-review] P0/P1/P2=0; publisher P2 kapalı, değişmeyen kapsam korunmuş. Yerel18 Python regression/parsing PASS yazar raporudur. |Kapanış kaynak incelemesi; reviewer testleri NOT_RUN. Yeni head'in bütün zorunlu CI/local matching-build/live kapıları bu belgeyle tamamlanmış sayılmaz.2b SQL76 e35'e otomatik taşınmaz. |
|Frontend ve gerçek ortam |83 yol devri/importu tamam; gerçek form/komut kodu mevcut; ca19 retry düzeltme adayı ile son browser/harness uyarlaması aynı Claude'dadır. |Final backend/FE/schema/OpenAPI/fixture/build/run/instance/workspace/DB/port bileşimi ve eşleşen yerel BackendVerify receipt gerekir. **Live bind/setup/smoke/publish/ENV_READY ve tam integrated browser NOT_RUN.** |

Mevcut dört-capability C006 yeniden kullanılır; ikinci lifecycle yazarı, C-01 golden genişletmesi veya yeni tek-node routing/gateway/loadbalancer işi yoktur. Kaynak P2 kapanışı, CI başarısı ve runtime kabulü farklı kapılardır. Eski LAPTOP/05b/run/DPAPI pinleri körce gevşetilmez; başka build'in başarılı receipt'i yerel runtime'a ait kanıt olamaz.

## 10. Frontend implementation sözleşmesi

### 10.1 Dört reader

Aynı #233 sahibi onaylı83 kaynak devriyle list/detail/mutation/candidate'i required integer0..15 contract'a uzlaştırmıştır; mevcut form adayı §9.2'de kaynaklıdır. 15 kabul,16 ret; response null/missing→zero yok. Identifier, şirket, PartyKind, aktiflik ve version korumaları değişmez. Yeni decoder ailesi veya paralel create/link uygulaması açılmaz; kalan gerçek browser/harness kabulüdür.

### 10.2 Form, düğme ve hata

Mevcut create/link formlarındaki mandatory-role validation ve metin kaldırılmıştır; başka zorunlu sınıfa dönüştürülmez. Firma/Kişi, hukuki ad, opsiyonel ticari ad ve salt okunur seçili şirket korunur. Kaynak roller hedefe kopyalanmaz.

Permission kaynaklı aksi mevcut non-disclosing kuralıyla sunulmaz; yetkili kullanıcı için uyumluluk doğrulanamaması işlevin çalışıyormuş gibi gösterilmesine izin vermez. Güvenli Türkçe açıklama ve mevcut error/dirty/pending/focus davranışı korunur. Teknik başlık kullanıcıdan elle istenmez.

Yükleniyor/GET hatası/uyum doğrulanamadı durumunda write kapalıdır. Yalnız valid GET+ack açabilir. Command başarısı ancak kendi tam response ve teyidiyle gösterilir; belirsizlikte başarı bildirimi, formu temizleme veya detail navigation yapılmaz. Hata sınıfı başlık yokluğundan uydurulmaz. Unknown/abandoned girişim varken genel sayfa uyarısı da key kaybettiren reload veya yanlış “hiçbir istek gönderilmedi” ifadesi kullanmamalıdır; ca19 düzeltme kanıt düzeyi §9.2'dedir.

### 10.3 Bağlama bağlı hazırlık

`(apiOrigin, actor/session, selectedCompanyId, generation)` değişirse readiness düşer. Taze GET cevabı aynı tuple'a ait olmalıdır. Gerçek sunucu teyidi, response decoder ve mevcut authorization ayrı zorunluluklardır. Transport header eski source-account görünürlüğünü target link yetkisine çeviremez. Ef issue-time context/abort guard'larının dar kapanışı §4.2'dedir. Gerçek retry ön kontrolü bırakma/query değişiminden sonra eski hazırlık üzerinden komut gönderemez; kendi taze başarılı okumasının kanıtını gerektirir.

### 10.4 Belirsiz girişim

§4.5 tek attempt/key/payload/context kaynağıdır. Mevcut commandAttempt/useCommandAttempt, dirty/pending/company/logout fence ve accessible retry kaydı korunur. Daha sonra gelen409 önceki belirsiz girişimi yok saydıramaz. Fresh valid GET+ack sonrasında yalnız explicit same-key retry; otomatik yeni key yoktur. Yardımcı ve c118 gerçek form bağlantısı mevcuttur; ca19 düzeltmesinin final kapanışı ve gerçek server+browser replay/tek-etki kabulü ayrı kapılardır.

## 11. Kalan görevler — mevcut sahiplik ve görevlerin devamı

Aşağıdaki CB/CF/CO/GB etiketleri mevcut #232 görev satırlarıdır; yeni issue, bağımsız görev sistemi veya paralel writer atanması değildir.

### CODEX-BACKEND — #230/#234 mevcut sahibi

**CB-1 — korunan880 kapanışı.** §4.4/§9.1: eski P2, main conflict ve SQL75/RCSI10 kanıtı yeniden açılmaz; eski kapsam için yeni review/test turu istenmez. Yeni kaynak yalnız kendi kanıtına bağlanır.

**CB-2 — CRO009 REAL_SQL teslimi mevcut.** §9.1.1 named None+inactive+stale+permission fact'i ve2b gerçek SQL76/RCSI10 ile özel boşluk kapanmıştır. Aynı testi yeniden yazma/roles3 fixture'ını yeniden analiz etme. Browser/genel CRO009 PARTIAL kalır; sonraki kaynak değişikliği varsa ilgili exact-head kanıtını ayrı bağla.

**CB-3 — aynı C006'nın kalan yayın/ortam kapısı.** Manifest üretilmiş/#234 yayımlanmış; eski CreateProcess engeli güncel statü değildir.2b untracked P2→1d kaynak düzeltmesi→e35 publisher düzeltmesi ve dar AI kapanışını kendi provenance'ıyla koru. Kalan: yeni başın uygulanabilir CI kapısı, son FE/browser kaynağıyla eşleşen yerel test-build/runtime hash kanıtı ve mevcut bind→setup→start→smoke→publish/verify→stop→release-check→teardown teslimi. ENV_READY çalışmadan verilmez; yeni lifecycle/gateway veya permission politikası yok.

### CLAUDE-FRONTEND — #233 mevcut sahibi

**CF-1 — eski kapanışları koru.**9a/07a/ef/6f ve c118'de kapanmış unknown/probe-auth/link-disabled kapsamı yeniden görevleştirilmez. [Dirac][fe-dirac]/[Averroes][fe-averroes] tamamlanmış incelemelerdir; aynı eski review tekrar istenmez.

**CF-2 — sahiplik ve import tamam.** [İnsan kararı][ownership-approved] ve [3703862][fe-import] geçerlidir;83 yol için yeniden karar/makbuz bekleme. Dört reader, rolsüz formlar ve gerçek command kaynakları aynı dalda mevcuttur. Eski donor/WIP/17Backend sınırı korunur; ikinci uygulama açılmaz.

**CF-3 — aynı retry dar farkının kapanışı.** c118'deki pending GET sırasında bırakma ve abandoned-link/query/cancel-cache yolları; ca19 yazar düzeltmesi mevcut. Yeni exact-head bağımsız kapanış ve hedef hook/DOM kanıtını aynı teslimde bağla: geri çekilmiş kontrol sonrası yeni POST0; retry'ın kendi taze valid GET/ack'i olmadan gönderim0. Eski ef transport hedefleri tekrarlanmaz.

**CF-4 — recovery metni ve gerçek same-key.** c118 üst uyarı bulgusu ile ca19 attempt-aware uyarı düzeltmesini ayır. Sonuçlanmamış komutta reload/“gönderilmedi” önerisi yok; aynı key/path/Party/company/payload korunur. Component kanıtına ek olarak final ortamda gerçek commit/replay ve tek etki kanıtı gereklidir; helper başarısı bunu kapatmaz.

**CF-5 — mevcut browser/harness uyarlaması ve bütünleşik koşum.** [Koordinatörün aynı Claude'ya devri][browser-handoff] kapsamında mevcut L-01/T-04/T-05/T-07 ve010 kaynakları kullanılır; yeni kimlik ailesi yok. Eski L-01 role3/2/checkbox adımları roleless create→list→detail→B-link{}→gerçek dönüşe uzlaştırılır. Son C006 manifestinin actor/company/prefix/API/UI-port/source/build bağları tüketilir; gerekli header/ack gözlemi Authorization/Cookie sızdırmaz. Tam gerçek API/SQL/browser context/old-new/unknown/retry ve teardown kabulü NOT_RUN; kontrollü hata/response koşumu gerçek old binary veya durable server replay diye etiketlenmez.

### CODEX-ORCHESTRATOR

**CO-1.** Mevcut #230/#234 Backend ve #233 Claude sahipliği korunur; yeni parallel spec/backend/create-link writer yok.

**CO-2.**83 yolun insan kararı ve importu tamam; recipient/donor makbuzu tekrar istenmez veya donor adına üretilmez.17 Backend/WIP sınırı ve #229/B/#232 insan kapıları ayrı kalır. Bu BUSINESS teslim merkezî kayda paralel yazmaz.

**CO-3.**880 eski kanıtı,2b/45dc98d SQL76,1d/e35 source/publisher kapanışları ve c118/ca19 frontend kanıtları ayrı tutulur. c118458/7 ile2b8/8 veya yeni yerel test sayıları whole-Cari/ENV_READY değildir. Yeni head'in kanıtı öncekinden etiket değiştirerek taşınmaz.

**CO-4.**Manifest hiç yok/form bağlantısı hiç yok/83 yol onaysız statüleri tarihsel kalır. Aynı sahiplerin final FE-browser/backend/build/fixture/run bileşimi → mevcut C006 ENV_READY → gerçek bütünleşik browser/failure/retry ve son delta/teardown sırası korunur. Tek-node için gateway/drain işi yok; karma havuz yalnız doğrulanırsa §4.6 kapısıdır.

**CO-5.**Final K3 exact kaynaklar, uygulanabilir fresh CI/SQL/browser/failure ve bağımsız review kapılarını doğrula. Kaynak review kapanışı runtime kanıtı değildir. Lane self-merge yok; #229/#232 insan incelemesi ve production Sinan kapısı ayrıdır.

### CHATGPT-BUSINESS

**GB-1.** Yalnız mevcut #232 dosyası exact-source kanıtla güncellenir; kanonik/shared ilerleme kaydına paralel yazılmaz.

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

Mevcut roleless/transport Cari düzeltmesi için yeni işletme kararı gerekmiyor. `roles=0/null` tercihi tekrar sorulmaz; request/response §3'te sabittir.83 dosyanın insan sahiplik kararı verilmiştir; bunu yeniden açık soruya çevirme. Teknik source/publisher/retry düzeltmeleri yeni işletme politikası değildir.

`NEEDS-SINAN-CARI-AUTO-001`: yalnız gelecekte implicit account permission kaynağı A/B/C kararı gerekir. ÖneriB, **onay değil**; mevcut Cari düzeltmelerinin ön şartı değildir. Çok şirketli piksel düzeni tüm akışı bekleten bir kullanıcı kararı yapılmaz.

Kaynak olarak [tarihli kullanıcı kararları](https://github.com/sinangym/GoymenERP/blob/e1fd69da69df36a72e559ea07163fb3f359b3c61/docs/decisions/SINAN_KARARLARI_2026-09-10.md), `docs/decisions/KARAR_SICILI.md`, `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`, mevcut Cari JIT/yetki sözleşmeleri ve öncelikli iki protocol handoff birlikte okunur. Eski #227 headerless örnekleri yeni contract'a aynen çalıştırılmaz; tarihsel kanıt olarak korunur.

#229 ve #232 insan review/acceptance kapıları açık kalır. Bu dosyanın güncellenmesi diğer ajana uygulama gönderildiği, onların çalıştırdığı, merge edildiği, runtime açıldığı veya ERP'nin kabul edildiği anlamına gelmez. Bu teslim kaynak/gerçek SQL günlüğü okuma ve şartname güncellemesidir; ürün testleri CHATGPT-BUSINESS tarafından yeniden çalıştırılmamıştır.

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
