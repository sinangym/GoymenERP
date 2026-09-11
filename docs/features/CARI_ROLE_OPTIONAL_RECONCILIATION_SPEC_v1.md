# Cari Opsiyonel Ticari Rol Uzlaştırma Şartnamesi v1

- Tarih: 11 Eylül 2026
- Şerit: `CHATGPT-BUSINESS`
- Durum: **REVIEW EDİLEBİLİR İŞ ŞARTNAMESİ / ERP KABULÜ DEĞİL**
- Dosya: `docs/features/CARI_ROLE_OPTIONAL_RECONCILIATION_SPEC_v1.md`
- Mevcut PR: **#232**; yeni belge veya paralel şartname değildir.
- Hazırlık tabanı: `main@060da63f2644cb271d78d9eab8b7a6985d544ad1`
- Önceki belge revizyonları: `1299a6ee9f817c552ee08bbe385d4cb12c11bb8e` → `4e063e26f2c5848f7330891016d99a82088efa7b` → `5b3c8d80bf48bb18bd4b27b17006237429c04257` → `def6ff7faa8ad14e4e6d48d2b12fee5f572610aa`. Bu toplu güncelleme mevcut içeriğin devamıdır.
- Öncelikli transport kaynağı: [#233 / 5626451657](https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5626451657), backend `080e7a1d24f74975b45546c5277f0529c13f06d1`.
- Koşullu hata başlığı düzeltmesi: [#230 / 5630622096](https://github.com/sinangym/GoymenERP/pull/230#issuecomment-5630622096), backend `880cae838e5d2f01a988f1e4a89afbe67858f48c`.

Bu belge uygulama kodu yazmaz, kanonik karar/task durumunu değiştirmez, `docs/planning/2026-09-10/**` veya paylaşılan ilerleme belgelerinin tek-yazar sahipliğini devralmaz. PR #229 karar paketi ile #232'nin uygulanabilir insan inceleme/kabul kapıları korunur. Kaynak göstermek, bu kapıları geçmek veya production yetkisi vermek değildir. Otomatik şirket ilişkisi için `B` yalnız öneridir; kullanıcı kararı değildir.

- Güncel frontend kaynak/teslimi: [#233 / ef43700](https://github.com/sinangym/GoymenERP/commit/ef43700b3f5d25e8cdfc7d28a76c420e233f96fe), parent `07a4af2a881e3b17aa7d5854ce6d1ad28beb892a`; [5631367978 dar teslimi][fe-final]. Protokolün ilk uygulanmış/alınmış kaydı [9a6b43d / 5630924431][fe-handoff], önceki yardımcı/transport düzeltmesi [07a4af2 / 5631155345][fe-fix] olarak korunur.
- C006/sahiplik kaynağı: [merkezî kayıt][co-record], [83 yayımlanmış yol için insan sahiplik önerisi][fe-ownership] ve [son frontend tesliminin değişmeyen sınırları][fe-final]. Eski FE424/no-publication/header-P1 hükümleri güncel engel değildir; C006 teknik politika engeli ile #228 insan sahiplik kararı ayrı kalır (§9.4).

## 1. Kaynak önceliği ve güncel depo gerçeği

Öncelik: `AGENTS.md` ve uygulanabilir governance altında en yeni açık kullanıcı kararı → tarihli kullanıcı kararları → `KARAR_SICILI` → Master Rapor → ilgili feature/ADR → eski referanslar. Açık yetki, güvenlik, migration veya production politikası tahminle kapatılmaz.

11 Eylül 2026 kaynak kesiti:

- #227 merge edilmiştir; main `060da63f2644cb271d78d9eab8b7a6985d544ad1`. Mevcut create/list/detail/company-link API yolculuğunun gerçek-SQL baseline'ıdır; yeni roleless frontend/transport veya production kabulü değildir.
- #228 `d46f7f819456fc578a25cdcabf5c98ad6f634456`, açık/draft donor kaynağıdır. Create/link/router/attempt ve kontrollü Chromium emeği yeniden yazılmaz. Recipient `5626181170` makbuzu mevcuttur; gerçek donor `HANDOFF-RELEASED` yerine geçmez. Donör erişilemediği için yalnız yayımlanmış83 yolun aynı Claude'a insan kararıyla devri önerilmiştir; [5631168111][fe-ownership] onay verilmiş kayıt değildir.
- #229 `e1fd69da69df36a72e559ea07163fb3f359b3c61`, insan incelemesi gereken açık/draft karar paketidir.
- #230 `080e7a1d24f74975b45546c5277f0529c13f06d1`, main uzlaştırması ve yeni transport gate'in önceki kanıt başıdır: 7 kontrol, SQL75/RCSI10, sıfır skip. James'in bu SHA'daki conditional error-header P2'si tarihsel olarak koşulludur; geriye dönük koşulsuz onaya çevrilmez.
- #230 `880cae838e5d2f01a988f1e4a89afbe67858f48c` için beş dosyalık OpenAPI/handoff/test P2 düzeltmesi, James/Ampere dar fark incelemeleri ve kendi fresh CI/SQL75/RCSI10 kanıtı korunur. Bu tur aynı kapanmış P2, eski main conflict'leri veya SQL kapsamı yeniden açılmadı; ayrıntı §9.1.
- Aktif frontend #233 `ef43700b3f5d25e8cdfc7d28a76c420e233f96fe`, açık/draft ve main `060da63` tabanlı20 frontend yoludur. **List/detail başlık gönderimi ve özel compatibility mesajları9a6b43d'de; network/offline revoke ve unknown settlement yardımcı düzeltmeleri07a4af2'de uygulanmıştır.** Ef43700 yalnız dört dosyalık devamdır: geç OLD başarılı GET'in NEW grant'ini değiştirmesi ve body-stage abort'un malformed/revoke olması P2'lerini kapatır. İki non-author AI incelemesi yalnız bu farkta P0/P1/P2=0 bildirir; tüm dilim veya insan kabulü değildir.
- Ef43700 frontend CI günlüğünde gerçek checkout `ee6a2deaad920522332f5c0bbcb070b1a3f5d620` (`ef43700 + main060`),18 dosya/324 PASS görülmüştür. Bu tur ana CI'nın SQL/container işleri dahil beş işi ve ayrı C-01/UI workflow'ları completed/success olarak canlı doğrulandı;07a'nın7/7 sonucu taşınmadı. Ayrıntı ve browser kapsam sınırı §9.2.
- #231 `173b003`, #233'ün424/267,9a/302 ve07a/319 kanıtları kendi tarihsel kaynaklarında kalır. Header/mesaj P1'i ve Claude'un eski izin reddi güncel engel değildir. Gerçek create/link/form-command ve bütünleşik kabul hâlâ NOT_RUN'dır.
- C006'nın17 kaynak dosyası hazırlığı, araç izni,83 donor yolu devri ve çalıştırılmış kabul farklı durumlardır; çalışan ortam veya bütünleşik PASS yoktur (§9.4).

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

**CRO-AC-001/003/004/008/010/011 — gerçekleşen dar düzeltmeler:**9a6b43d'de başarılı GET sonrasındaki fetch rejection/offline eski hazırlığı bırakıyordu; [07a teslimi][fe-fix] bu yolu liste ve detay transport'unda revoke ile düzeltmiştir. Saf503 gözlem testiyle eş tutulmaz; aynı transport'ta başarılı okuma→cevapsız ağ hatası/offline dizisi testlidir. İlgili helper/transport hedefi yeniden AÇIK yazılmaz; gerçek form/command bağlantısı ayrı NOT_RUN'dır.

[Ef43700 dar teslimi][fe-final] ve [iki][fe-review-1] [non-author inceleme][fe-review-2] şu iki kalan P2'yi yalnız yeni dört-dosya farkında kapatır: (1) `beginContractRead` bağlamı istek çıkarken kaydeder; OLD bekler→NEW valid+ack200→OLD geç valid+ack200 dizisinde eski gözlem NEW grant'ini ne değiştirebilir ne geri alabilir. (2) `response.json()` sırasında aborted signal, `fence-stale` ve readiness'e dokunmayan sonuçtur; gerçek, aborted olmayan malformed body hâlâ readiness'i geri alır. Abort, sunucunun uyumsuzlaştığı kanıtı değildir.

Kanıt sınırı: [transport testleri][fe-transport] liste/detay deferred OLD-success yollarını ve body-stage abort/malformed ayrımını kapsar. Abort testleri pre-aborted signal + mock body rejection kullanır; gerçek response-stream zamanlaması veya komut DOM'u kanıtlanmış değildir. Gerçek form/command kabulünde network sonrası düğme/submit kapalı ve yeni POST/PUT0; eski bağlamın gecikmiş başarısı/hatası güncel formu değiştirmemeli; abort malformed hata göstermemeli; gerçek bozuk gövde write kapısını kapatmalıdır. Bu bütünleşik varyantlar NOT_RUN kalır; kapanmış helper/transport kapsamına ikinci paralel düzeltme veya eski review turu açılmaz.

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

**CRO-AC-005/007/010/011/012 — yardımcı düzeyindeki hedef karşılandı, form entegrasyonu NOT_RUN:**9a6b43d'nin headers-only `settlesUnknownOutcome` açığı [07a4af2 tesliminde][fe-fix] düzeltildi. [Güncel yardımcı][fe-helper], aynı bağlam/key/normalize payload parmak izi, kendi güncel hazır bağlamından gelen matching ack ve `validated`2xx komut sonucu/replay ister. Timeout/no-response,500, çözülemeyen gövde, bütün4xx ve sonraki compatibility409 unknown'ı kapatmaz; farklı key/payload/context de kapatmaz. `mayRetrySameKey` yeni key üretmez. Bu davranışın [saf işlev testleri][fe-unit] vardır; ef43700 dar incelemesinde eski settlement kapsamı yeniden incelenmedi.

Açık bütünleşik kabul: önce gerçek bir unknown attempt oluştur; fresh GET sonrası readiness açıkken ayrı ayrı500+ack ve compatibility409 uygula. Mevcut create/link validated decoder'ı, command/path/Party binding'i ve gerçek attempt kaydı yardımcıya bağlanmalı; aynı key/context/normalize payload korunmalı, otomatik yeni key/istek, success toast, form temizleme veya navigation0 olmalıdır. Yalnız açık same-key retry'ın doğrulanmış kendi komut sonucu/durable replay'i çözüm sayılır. Testte verilen `validated` etiketi gerçek decoder entegrasyonunun veya server commit'inin kanıtı değildir. Bu kalan iş yardımcı hatası yeniden açılmış gibi raporlanmaz.

Güncel yetki/bağlam tekrar doğrulandıktan ve o bağlamda taze başarılı GET + exact teyit alındıktan sonra, yalnız açık same-attempt retry aynı key ve normalize payload ile yapılabilir. Kullanıcı/oturum/şirket değişimi otomatik recovery veya otomatik retry değildir; önceki girişim yeni bağlama taşınmaz. Eski yanıt yeni ekrana success/navigation yaptıramaz.

Create ile link ayrı logical attempts'tir; farklı key kullanır. Aynı command/context/key içinde missing roles ile numeric0 eşdeğerdir. HTTP hata statüleri korunur; başlık bir iş işleminin commit olup olmadığının tek başına makbuzu sayılamaz.

### 4.6 Mixed-node sınırı

Protokol eski backend binary'sini sonradan uyumlu hale getirmez. Teyit veren node'a GET yapılması, sonraki yazmanın aynı uyumlu node'a gideceğini kendiliğinden kanıtlamaz.

[Koordinatör kaydı][co-record] ve [son teslim][fe-final] mevcut compose/C-01 topolojisini tek API + nginx tek upstream olarak ayırır. **Tek-node C006 için yeni gateway/loadbalancer/drain altyapısı işi açılmaz.** Eski istemci/yeni sunucu ve yeni istemci/eski sunucu kabulü gerekli kalır; tek-node gerçeği bunları kaldırmaz. Production topolojisi doğrulanmış değildir.

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

Test tasarımı PASS değildir. Her sonuç exact source, gerçek checkout, run/job ve kapsama bağlıdır.080,880,9a,07a veef sonuçları birbirine otomatik taşınmaz. Kaynak incelemesi, yazar/koordinatör log aktarımı, doğrudan CI günlüğü okuması ve yeni test çalıştırma ayrı tutulur. Bu tur frontend CI günlüğü okundu; backend'in önceki SQL kanıtı korundu ve ürün testleri CHATGPT-BUSINESS tarafından yeniden koşturulmadı. B-C01-001…010 ve protokolün8 browser ölçümü CRO/CR kimliklerinin yerine geçirilmez.

## 9. Kanıta bağlı kabul matrisi

Her satırda başlangıç/eylem, beklenen kayıt ve yasak etki belirtilmiştir. Backend kanıt kesiti §9.1'deki880cae8/969a4a8 koşumudur. Frontend güncel kaynak ef43700, gerçek CI checkout'u ee6a2de'dir. Yardımcı/transport kapanışları ile regresyon browser'ı ayrı belirtilir; tam integrated create→list→detail→B-link/dönüş/failure/retry bütün ilgili satırlarda NOT_RUN kalır.

| Kabul | Başlangıç, eylem ve beklenen/yasak etki | Backend REAL_SQL / HTTP | Frontend unit / stub / component | Controlled / C-01 browser | Bütünleşik kabul ve kalan |
|---|---|---|---|---|---|
| CRO-AC-001 | Yetkili A kullanıcısı; taze valid GET+teyit, sonra roles eksik/0 create. Bir Party+A account, roles0, ilgili audit/durable result. Eski server200/no-ack varyantında POST0, kayıt0. | OptionalRoles SQL missing/zero create ve command-gate senaryoları; broad75 PASS. SQL, frontend'in önce GET yaptığını kanıtlamaz. [SQL kaynak][be-sql], §9.1. | [Helper][fe-unit]/[stub reader][fe-transport] hazırlığı mevcut; network revoke07a ve iki readiness P2'si ef'de dar kapsamda kapalı. Gerçek create formu/command bağlantısı yok. | #228 roleful controlled create tarihsel (§9.3); C-01 oluşturma kanıtı değil. | NOT_RUN: gerçek form GET/ack→POST201→detail; old-server ve ağ-hatasında POST/PUT0; geç OLD/abort sonucu komut DOM'unda ayrıca ölçülmeli. |
| CRO-AC-002 | Eski1/3/15 kayıtlarıyla upgrade; kimlik/rol/audit/replay değişmez. Zero sonrası Down reddi; DDL/veri kaybı yok. | OptionalRoles migration/legacy/Down senaryoları broad75 içinde PASS. [SQL kaynak][be-sql], §9.1. | N/A; UI testi SQL migration kanıtı değildir. | N/A; production migration yapılmış değildir. | Backend scope kanıtlı; yeni production/kanonik kabul yok. |
| CRO-AC-003 | Party A'da görünür, B account yok. B bağlamında taze valid GET+ack; `{}` veya roles0 link. Aynı Party, tek B account, A değişmez. A teyidiyle B yazması yasak. | OptionalRoles create/link ve target isolation SQL; broad75 PASS. [SQL kaynak][be-sql], §9.1. | List/detail transport ve ef context fence mevcut; donor mutation/candidate ve link formu uyarlaması açık. Hedef şirket readiness'i gerçek formda henüz kanıtlanmadı. | #228 roleful controlled link tarihsel; C-01 hedef-company link kanıtı değil. | NOT_RUN: B-context fresh readiness, aynı Party/tek B account; B ağ-hatası/no-ack sonrasında yazma0. |
| CRO-AC-004 | Yetkisiz/foreign Party ve şirket varyantları. Doğru başlık grant vermez; mevcut403/404, kimlik sızıntısı ve business etki0. | OptionalRoles authorization/isolation + transport HTTP/SQL senaryoları; §4.4 error yolları ayrıca HTTP contract. [SQL kaynak][be-sql], §9.1. | Ef [stub testlerinde][fe-transport] OLD bekler→NEW başarı→OLD geç başarı NEW grant'ini bozmuyor; body-stage abort no-op, gerçek malformed revoke. İki P2 dar review'da kapalı. | C-01 regresyonu/B-C01-003 komut readiness DOM/stream kanıtı değil. | NOT_RUN: mevcut yetkili formda context reset, gerçek geç-response/stream ve yetkisiz yazma0; eski helper hatası tekrar açılmıyor. |
| CRO-AC-005 | Gönderilmiş command'in yanıtı timeout/malformed/ack eksik. Aynı attempt/key/company/Party/payload korunur; sonra409 ve tazeGET+ack sonrası açık same-key retry. İkinci business etki ve auto-new-key yok. | Durable replay/rollback ve protocol key-consumption SQL; broad75 PASS. [SQL kaynak][be-sql], §9.1. | [Saf yardımcı][fe-unit]07a'dan beri500/timeout/malformed/4xx/compatibility409 ile unknown kapatmıyor; key/context/payload ve validated2xx bağları testli. Ef review eski settlement'ı tekrarlamadı. | #228 controlled unknown-outcome: persistentServerReplayClaimed=false; gerçek server replay değildir. | NOT_RUN: gerçek create/link decoder + attempt/key/path/Party/payload/retry bağlantısı,500/409 sonrası tek girişim ve tek kalıcı business etki. |
| CRO-AC-006 | İki farklı key aynı Party/B link yarışı; ayrıca audit/idempotency hata enjeksiyonu. Tek target account/başarılı created audit; yarım write yok. | OptionalRoles different-key race + audit/idempotency rollback; broad75 PASS. [SQL kaynak][be-sql], §9.1. | N/A; frontend birim testi SQL yarış/rollback yerine geçmez. | N/A; bu SQL kabulünün browser karşılığı uydurulmaz. | Backend scope kanıtlı. Kaybedenin durable conflict'i ikinci hesap değildir. |
| CRO-AC-007 | Eski1..15 ve yeni0 durable sonuçlar; uyumlu declaration ile replay. Eski hash/snapshot korunur, yeni business etki yok. Headerless replay command'i reddedilir. | OptionalRoles legacy replay + transport command/retry SQL, broad75 PASS. [SQL kaynak][be-sql], §9.1. | [sameCommandAttempt/mayRetrySameKey][fe-helper] saf yardımcı sınırı uygulanmış; gerçek durable replay tüketimi bağlanmış sayılmaz. | Controlled replay tarihsel; gerçek istemciyle replay kabulü yok. | NOT_RUN: aynı key/payload/context ve eski hash korunumu gerçek komutla; koşullu mixed-node kapısı §4.6'dan ayrı değerlendirilir. |
| CRO-AC-008 | Liste/detail/candidate roles0; named filtre ve count; GET'ler DB değiştirmez. Legacy authorized projection1..15 başarı olabilir, visible0 ise409; gizli foreign0 sızmaz. | OptionalRoles read/no-write + iki protocol SQL fact'in read kapsamı; broad75 PASS. [SQL kaynak][be-sql], §9.1. | [Liste][fe-list]/[detay][fe-detail] declaration/ack/decoder bağı var. [Transport21 testi][fe-transport] ef CI'da PASS; ağ iptali ve geç OLD/body-abort P2'leri dar kapsamda kapalı. Mutation/candidate devri açık. | Ef C-01 workflow success;880+ef yerel list10/protocol8 raporu ayrı (§9.2). Dört-reader veya command DOM kabulü değil. | PARTIAL / integrated NOT_RUN: dört reader+gerçek form; boolean readiness testinden gerçek command0 sonucu çıkarılmaz. |
| CRO-AC-009 | Roles0 pasif account, current/stale active-state sürümleri. Contract header zorunlu; gerekçe/yetki/version atlanmaz; pasiflik otomatik aşılmaz. | AÇIK: [WriteSqlServer fixture][be-write] inactive/stale için Customer+Supplier (roles3); [RolelessReads][be-sql] no-write/permission, inactive+stale içermez. [Koordinatör kaydı][co-record] exact None varyantını NOT_IMPLEMENTED/NOT_RUN tutar. Stale409 durable result üretebilir; business/audit değişmezliği ayrı sınanır. | Yeni roleless inactive/stale/permission form-command kanıtı yok. | Roleless active-state/protocol browser NOT_RUN. | PARTIAL / REAL_SQL alt varyant NOT_RUN: exact None+inactive+stale+permission testi gerekir; önceki korundu ifadesi kapatmaz. |
| CRO-AC-010 | Eski/yeni client-server birleşimleri; pending sırasında logout/company/session/API değişimi. Current valid GET+ack readiness; old server200/no-ack write0; hata readiness değil. Karma havuz ancak doğrulanırsa §4.6 deployment kapısı. | Backend legacy GET/zero withholding ve command header reddi SQL PASS; client readiness veya karma havuz kanıtı değildir. [SQL kaynak][be-sql], §9.1. | Ef helper32 + stub21 test kapsamı;07a yardımcı hedefleri ve ef iki P2 dar kapanışı. Old-server POST0 yalnız gate boolean testiyle kapatılamaz. | C-01/protocol8 yalnız regresyon; gerçek command DOM/stream/retry ve iki yönde tam uyumluluk kabulü değil. | PARTIAL / CR-10 NOT_RUN: iki yönde old/new kabulü ve gerçek context/unknown yolları. Tek-node C006'ya yeni gateway/drain işi yok; karma havuz şartı koşulludur. |
| CRO-AC-011 | Request roles/null/string/fraction/negative/16; header missing/empty/duplicate/combined/unsupported. Response0/15 geçerli, null/missing/16 geçersiz. Error400/403 ack var;401/binding400 ack yok. | Invalid-role/protocol SQL + §4.4 dört HTTP test/OpenAPI P2 senaryoları.880 drift başarılı; yerel11 ayrı rapor. [SQL][be-sql] / [HTTP-OpenAPI][be-http] ayrı kanıtlardır. | Özel compatibility mesajları9a'da uygulanmış; [helper32][fe-unit] hata settlement sınırlarını, [stub21][fe-transport] gerçek malformed/abort ayrımını kapsıyor. Yeni P0/P1/P2 dar review'da yok. | Yerel protocol8 sağlıklı list/header regresyonu;500/409 command recovery veya gerçek stream testi değil. | PARTIAL / integrated NOT_RUN: gerçek form hata/state/attempt entegrasyonu açık; eski header/mesaj P1'i güncel engel değil. |
| CRO-AC-012 | Başarılı roleless create/link ancak valid mutation+ack ile tek success/detail. Listeye dönüş güncel yetkiyle; eski cursor/generation geri yüklenmez. Belirsiz command'te success toast/navigation yok. | #227 roleful SQL journey korunur; backend transaction kanıtı ekran dönüşü kanıtı değildir. [SQL kaynak][be-sql], §9.1. | Sınırlı taze cursor walk component kapsamı korunur; settlement helper düzeltmesi gerçek form success/unknown/navigation bağlantısı değildir. | C-01 liste regresyonu; detail/return walk ve roleless form/link journey browser NOT_RUN. | NOT_RUN: gerçek akış/odak/failure/retry; unknown'da success/clear/navigation0. Bulunamadı uyarısı kayda dönüş PASS'i değildir. |

### 9.1 Backend exact-source kanıt kaydı

**Tarihsel080e7a1:** [CI34538870747](https://github.com/sinangym/GoymenERP/actions/runs/34538870747), SQL75/RCSI10, skip0; C-01 ve UI golden önceki exact-source kanıtıdır. James'in o SHA'daki P2'li incelemesi tarihsel koşullu kayıttır.

**Yeni880cae8:** [CI34571306671](https://github.com/sinangym/GoymenERP/actions/runs/34571306671), [UI golden34571306701](https://github.com/sinangym/GoymenERP/actions/runs/34571306701), [C-01 browser34571306722](https://github.com/sinangym/GoymenERP/actions/runs/34571306722) tamamlanmış success. CI'nın backend/frontend/OpenAPI/SQL/container işleri ile iki ayrı workflow toplam yedi kontrolü oluşturur. Backend dalındaki frontend işi #233'ün yeni protocol implementasyonu değildir.

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

### 9.2 Frontend ve browser kanıt sınırı

**Güncel kaynak #233 `ef43700b3f5d25e8cdfc7d28a76c420e233f96fe`; parent `07a4af2a881e3b17aa7d5854ce6d1ad28beb892a`.** [9a makbuzu][fe-handoff] declaration/compatibility mesajı uygulamasını; [07a teslimi][fe-fix] network revoke ve unknown settlement yardımcı düzeltmelerini; [ef teslimi][fe-final] yalnız dört-dosya P2 kapanışını kaynaklandırır. Aynı eski kapsam için yeniden review/test görevi açılmaz. Kaynak zinciri gerçek create/link komut entegrasyonunu tamamlamaz.

| Kanıt | Exact kaynak / doğrulama düzeyi | Kanıtlamadığı |
|---|---|---|
| Saf yardımcı | [clientContract.test.ts][fe-unit]: ef CI frontend günlüğünde32 PASS.07a'da eklenen11 test500/timeout/malformed/4xx/409, context/key/payload ve same-key sınırını kapsar. | `validated` test girdisi gerçek mutation decoder/attempt store/path/Party bağlantısı değildir; render edilmiş create/link veya SQL commit kanıtı yoktur. |
| Stub transport | [contractTransport.test.ts][fe-transport]: ef CI'da21 PASS.07a'nın6 hedefi üzerine ef'de5 test: liste/detay geç OLD başarısı, iki body-stage abort ve aborted olmayan malformed ayrımı. | Fetch/response mock'tur. Pre-aborted signal + mock body rejection gerçek stream zamanlaması veya command DOM kabulü değildir. |
| Component | [CariListPage.test.tsx][fe-component] ef CI'da84 PASS; CariDetailPage19 PASS. Özel compatibility metni ve önceki liste/odak kapsamı korunur. | Readiness için henüz devralınmamış create/link formunun disabled/submit/key/retry davranışı değil. |
| Gerçek frontend CI | [Job103192346328][fe-ci-job] bu tur doğrudan okundu:08:02:44Z checkout `ee6a2deaad920522332f5c0bbcb070b1a3f5d620` = ef43700 + main060;08:03:23Z **18 dosya/324 PASS**, lint/build success. |324'ün tamamı component değildir. Test checkout'u ef head diye yeniden adlandırılmaz; backend880 ile tam integrated source değildir. |
| Fresh mandatory CI | [CI34577227055][fe-ci] beş iş; SQL103192346286 ve container103194004115 dahil completed/success. [C-0134577227048][fe-c01] ve [UI34577227054][fe-ui] success: toplam7/7 bu turun canlı run/job okumasıdır. |Son koordinatör kesitinde ana CI tamamının teyidi bekliyordu; yeni gözlem ayrı tutulur.07a'nın7/7'si taşınmadı. SQL job success metadatasından880 SQL75 veya yeni roleless test sonucu üretilmez; coordinator merge/insan kapısı geçilmiş sayılmaz. |
| Yerel red→green ve browser regresyonu | [Ef yazar teslimi][fe-final]: hedefler önce kırmızı, ardından324 PASS;880+ef izole yığın, fixture54 Party/55 account, B-C01-001…010 **10 PASS**, ayrı protocol **8 PASS** ve teardown raporlu. Koordinatörün bu konuşmadaki yerel tool-log gözlemi: önce4 hedef FAIL→324 PASS;08:02:06 fixture54/55+C-01 10,08:02:16 protocol8. |Ham yerel tool günlüğü bu BUSINESS turunda yeniden okunmadı. Bu sayılar yazara/koordinatör gözlemine atfedilir; readiness değişkeninin gerçek command DOM/stream/retry kanıtı değildir. C006 runtime kabulü de değildir. |
| İki bağımsız dar AI inceleme | [Reviewer1 /5631431324][fe-review-1] ve [Reviewer2 /5631431518][fe-review-2], ef exact head, dört dosya api/detailApi/clientContract/contractTransport.test farkı: önceki iki P2 KAPALI, yeni P0/P1/P2 yok. |Non-author kaynak incelemeleri, yeniden test koşumu veya insan onayı değildir. Eski decoder/pagination/C-01/settlement kapsamı tekrarlanmadı; bütün PR/dilim/merge/production kabulü yoktur. |

**Tarihsel zincir korunur:**9a6b43d için302 test, CI34573865458/C-0134573865597/UI34573865578 ve yazarın880+9a yerel8e83fb1 list10/protocol8 raporu kendi kaynağına aittir.07a4af2 için319 test ve [CI34575684274](https://github.com/sinangym/GoymenERP/actions/runs/34575684274)/[C-0134575684283](https://github.com/sinangym/GoymenERP/actions/runs/34575684283)/[UI34575684265](https://github.com/sinangym/GoymenERP/actions/runs/34575684265)7/7 ayrı geçmiş kanıttır; o tur browser tekrarlanmadığı [teslimde][fe-fix] belirtilir.07a incelemelerinin iki P2'si tarihsel olarak korunur, ef kapanışı geriye dönük koşulsuz eski onay üretmez.

**Tarihsel424a586:** Node24 suite267/267, C-01 list10/10 ve ayrı16/16 liste davranışı raporu kendi kaynaklarında korunur. [Eski devir dosyası](https://github.com/sinangym/GoymenERP/blob/424a586fc07efa291db4b37866ba5cce54fb7443/src/frontend/goymen-erp-web/CARI_FRONTEND_DEVIR_2026-09-10.md) ve eski #231266/266 kaydı tarihsel kapsamındadır. Node26'daki iki yerel hata yeniden mandatory CI arızası diye açılmaz.

Liste dönüşü artık yalnız uyarı değildir: ilk sayfa taze okunur, gerekirse en çok dört ek sayfaya güncel cevap cursor'ıyla yürünür; eski cursor replay edilmez. Bu sınır mevcut implementation parametresidir, yeni genel işletme politikası değildir. Kayıt bulunursa odak döner; bütçede bulunamaması gerçek dönüş başarısı değildir. Detail capability kapalı C-01 yığını bu walk'ı veya create/link/detail zincirini tarayıcıda kanıtlamaz.

### 9.3 Donor ve kontrollü browser sınırı

#228 recipient `HANDOFF-ACCEPTED`5626181170 vardır; tekrar istenmez. Donöre erişilemediği için [5631168111][fe-ownership] yalnız yayımlanmış36 frontend +47 browser =83 yolun aktif yazarlığının aynı Claude/#233'e Sinan kararıyla geçmesini önerir. **Henüz onay kaydı yoktur; shared83 açılmaz.** Onay gelirse HUMAN-OWNERSHIP-TRANSFER olarak kaydedilir, donor HANDOFF-RELEASED diye imzalanmaz. Eski dal/geçmiş korunur; unpublished LENOVO C023/F024/F025 kaynakları devralınmış değildir.17 backend/test/lifecycle/CI yolu bu karardan ayrıdır.

[Koordinatörün controlled evidence kaydı](https://github.com/sinangym/GoymenERP/pull/228#issuecomment-5624268419): run34103473977 artifact10011561394; SDK80/80, helper54/54, controlled Chromium17/17; test-merge checkout2ebcbc7 ile d46 kaynak ağacının eşliği ayrı doğrulanmıştı. `persistentServerReplayClaimed=false`, `erp_accepted=false`; bu sayılar gerçek roleless server/browser birleşimi veya yeni protocol kabulü değildir. CSS/DPR eşdeğerleri gerçek browser zoom/screen-reader konuşması sayılmaz.

### 9.4 C006: kaynak hazırlığı, araç engeli ve donör devri

[Merkezî kayıt][co-record] mevcut CODEX-BACKEND `01a0865c-1990-7783-8aee-4aee15618b31` oturumunun #228/d46 içindeki **17 backend/test/lifecycle/CI kaynağının** yeni izole C006 adayı için tek sahibi atamasını ALDIĞINI bildirir. [Son ef teslimi][fe-final] C006 politika engelinin sürdüğünü teyit eder. Mevcut dört-capability C006 yeniden kullanılacaktır; ikinci lifecycle yazarı yoktur, C-01 list-only/golden genişletilmez.

| Ayrı durum | Kaynakta bildirilen gerçek | Açık kalan / sınır |
|---|---|---|
| Kaynak hazırlığı |880 tabanlı `codex/cari-acceptance-envelope-backend-20260911`, `C:/src/GoymenERP-cari-acceptance-envelope-20260911`; yalnız yayımlanmış17 yolun çalışma kopyası alındı. |17 çalışma kopyasının ham-byte/hash eşliği belgelenmemiştir. Bu asistan bu yerel dosyaları/runtime'ı doğrulamış sayılmaz. |
| Araç engeli |Manifest/yeni envelope ve özel roleless inactive-stale-permission SQL varyantı hazırlığı CreateProcess policy engelindedir; yeni yazım/koşum NOT_RUN. |Aynı Backend görevinin izin yöneticisi düzeltmelidir. Başka araç/oturumla aşılmaz; bu yeni Sinan işletme politikası gerektiren bir karar değildir. |
| Donör/insan sahipliği |83 yayımlanmış yol için recipient5626181170 mevcut; [insan sahiplik önerisi][fe-ownership] henüz onaysızdır. |17 backend yolu sahipliği83 yol release'i değildir; donor adına makbuz yok. Unpublished LENOVO WIP alınmış sayılmaz. |
| Uygulanmamış entegrasyon |#233/ef helper/transport hedefleri dar kapsamda kapalı; create/link, mutation/candidate ve gerçek form-command bağlantısı tamamlanmadı. |Eski frontend izin/header-P1'i engel diye tekrarlanmaz. Kapanmış iki P2 gerçek form kabulü yerine kullanılamaz. |
| Çalışan test ortamı |Yeni C006 runtime/preflight/browser/SQL/teardown kanıtı YOK: **NOT_RUN**. |17 kaynak hazırlığı veya880+ef ayrı list/protocol regresyonu C006 runtime PASS değildir. Ef frontend adayıdır; bütünleşik final source/manifest veya kabul değildir. |

Devamın manifesti exact backend880/şema/OpenAPI hash'i, seçilen final frontend source, disposable run, instance, workspace, DB ve port bağlarını taşımalıdır. Eski LAPTOP/05b/run/DPAPI pinleri körce gevşetilmez. Manifest hazırlanması ile doğrulanmış runtime kabulü ayrı teslimlerdir; finalFE olmadan tam bütünleşik kabul PASS yazılmaz. Mevcut tek-node C006 için yeni routing/gateway/loadbalancer işi açılmaz (§4.6).

CRO-AC-009 ayrıca AÇIK: eski WriteSqlServer inactive/stale fixture roles3; RolelessReads no-write/permission testi inactive+stale içermez. [Koordinatör kaydı][co-record] `RolelessInactiveAccount_StaleVersionAndPermissionGatesRemainEnforced` dar REAL_SQL fact'ini gerekli fakat henüz yazılmamış/NOT_RUN olarak bildirir. Exact None+inactive+stale+permission kanıtı gerekir; denied snapshot ile stale business/audit/version değişmezliği ayrılır, stale409'un izin verilen durable idempotency sonucu yanlışlıkla yasaklanmaz.

## 10. Frontend implementation sözleşmesi

### 10.1 Dört reader

Mevcut #233 sahibi list/detail'i, onaylı exact-surface donor/insan sahiplik devri sonrasında mevcut mutation/candidate'i aynı required integer0..15 contract'a uzlaştırır. 15 kabul,16 ret; response null/missing→zero yok. Identifier, şirket, PartyKind, aktiflik ve version korumaları değişmez. Yeni decoder ailesi veya paralel create/link uygulaması açılmaz.

### 10.2 Form, düğme ve hata

Mevcut create/link formlarındaki mandatory-role validation ve metin kaldırılır; başka zorunlu sınıfa dönüştürülmez. Firma/Kişi, hukuki ad, opsiyonel ticari ad ve salt okunur seçili şirket korunur. Kaynak roller hedefe kopyalanmaz.

Permission kaynaklı aksi mevcut non-disclosing kuralıyla sunulmaz; yetkili kullanıcı için uyumluluk doğrulanamaması işlevin çalışıyormuş gibi gösterilmesine izin vermez. Güvenli Türkçe açıklama ve mevcut error/dirty/pending/focus davranışı korunur. Teknik başlık kullanıcıdan elle istenmez.

Yükleniyor/GET hatası/uyum doğrulanamadı durumunda write kapalıdır. Yalnız valid GET+ack açabilir. Command başarısı ancak kendi tam response ve teyidiyle gösterilir; belirsizlikte başarı bildirimi, formu temizleme veya detail navigation yapılmaz. Hata sınıfı başlık yokluğundan uydurulmaz.

### 10.3 Bağlama bağlı hazırlık

`(apiOrigin, actor/session, selectedCompanyId, generation)` değişirse readiness düşer. Taze GET cevabı aynı tuple'a ait olmalıdır. Gerçek sunucu teyidi, response decoder ve mevcut authorization ayrı zorunluluklardır. Transport header eski source-account görünürlüğünü target link yetkisine çeviremez. Ef issue-time context/abort guard'larının dar kapanışı §4.2'dedir; bunların gerçek form/stream etkisi ayrıca kanıtlanır.

### 10.4 Belirsiz girişim

§4.5 tek attempt/key/payload/context kaynağıdır. Mevcut commandAttempt/useCommandAttempt, dirty/pending/company/logout fence ve accessible retry kaydı korunur. Daha sonra gelen409 önceki belirsiz girişimi yok saydıramaz. Fresh valid GET+ack sonrasında yalnız explicit same-key retry; otomatik yeni key yoktur. Yardımcı settlement hedefi karşılanmıştır; gerçek validated decoder + attempt/key/payload/retry bağlantısı NOT_RUN'dır.

## 11. Kalan görevler — mevcut sahiplik ve görevlerin devamı

Aşağıdaki CB/CF/CO/GB etiketleri mevcut #232 görev satırlarıdır; yeni issue, bağımsız görev sistemi veya paralel writer atanması değildir.

### CODEX-BACKEND — #230 mevcut sahibi

**CB-1 — P2 kanıtı güncellendi.** Kaynak: §4.4 ve §9.1.880'de beş dosyalık düzeltme, James/Ampere delta kapanışı ve kendi CI/SQL kanıtı korunur. Aynı P2 veya eski main conflict'leri yeniden açılmaz; eski kapsam için yeni review/test turu istenmez. Yeni backend kaynak değişirse yalnız o değişikliğin ilgili kanıtı ayrıca bağlanır.

**CB-2 — protocol ve açık REAL_SQL alt varyant.** Mevcut header/legacy GET/error-ack/CORS/GET no-write/durable retry/migration/#227 envanterini koru. CRO-AC-009 için§9.4'teki exact None+inactive+stale+permission fact'i yazılmamış/NOT_RUN'dır. Aynı görev izinli hale gelince tek dar REAL_SQL testi tamamla; denied sıfır-etki ile stale409'un business/audit değişmezliğini ve izin verilen durable sonucunu ayrı ölç. Toplam75 veya “önceki korundu” bu hücreyi kapatmaz.

**CB-3 — mevcut C006 hazırlığını sürdür.** Kaynak:§9.4/[koordinatör][co-record].17 yol ataması alındı; kaynak kopyası çalışan ortam değildir. CreateProcess engelini aynı görevin izin yöneticisi çözmeli; başka araç/oturumla zorlanmaz. İzinli aynı sahip/dal üzerinde hash envanteri ve exact backend/schema/OpenAPI/finalFE/run/instance/workspace/DB/port manifesti; sonra runtime/preflight/SQL/browser/teardown ayrı kanıtlanır. Yeni tek-node gateway/drain veya ikinci lifecycle işi yok;83 sahiplik kararı ve production ayrı kalır.

### CLAUDE-FRONTEND — #233 mevcut sahibi

**CF-1 — yayımlanmış ilerleme ve dar kapanışlar alındı.** [9a makbuzu][fe-handoff], [07a yardımcı düzeltmesi][fe-fix], [ef43700 teslimi][fe-final] ve [iki][fe-review-1] [dar inceleme][fe-review-2] esas alınır. Header/mesaj P1'i, network revoke/unknown yardımcı hedefleri ve önceki iki P2 yeniden görevleştirilmez. Yeni kaynak değişmeden eski decoder/pagination/settlement/C-01 için tekrar review isteme; mevcut kanıtı aynı sınırda koru.

**CF-2 — onaylı sahiplik devri sonrası dört reader/form.** Recipient makbuzu tekrar istenmez. [83 yolun insan sahiplik önerisi][fe-ownership] onaylanmadan shared yüzeye yazma; donor adına release üretme. Yetkili devir açılınca mevcut d46 create/link/router/attempt emeğini provenance ile al; mutation/candidate>=1 ve mandatory role kaldırılır. Bitiş: dört decoder0/15 pozitif, null/missing/16 ve şirket/version negatifleri; roles eksik/0 form submit; eski ProblemCode'lar korunur.

**CF-3 — readiness'ın gerçek form/komut entegrasyonu.** Kaynak:CRO-AC-001/003/004/008/010/011,§4.2. Helper/transport kapanışları tamam; kalan, devredilmiş formda GET→network/offline sonrası submit/düğme kapalı ve POST/PUT0; OLD/NEW geç yanıt, gerçek body stream abort ve malformed ayrımının komut DOM etkisi. Ef stub testini veya C-01 regresyonunu bu kanıt yerine kullanma. Yeni transport implementasyonu sıfırdan açılmaz.

**CF-4 — gerçek belirsiz attempt entegrasyonu.** Kaynak:CRO-AC-005/007/010/011/012,§4.5.07a'da düzelmiş helper'ı yeniden yazma; mevcut create/link validated decoder ve attempt store'a bağla. Bitiş:500/timeout/malformed/compatibility409 sonrası aynı key/path/payload/company/Party korunur, otomatik key/request ve success/clear/navigation0; açık same-key recovery gerçek server'da tek etki üretir. Helper kanıtı bu kalan entegrasyonu kapatmaz.

**CF-5 — gerçek bütünleşik browser.** Kaynak: aynı CRO seti, yeni kimlik yok. Pinli backend/frontend/schema/fixture ile roleless create→list→detail→B link/dönüş; wrong-company/permission; old-client/new-server ve new-client/old-server; pending/logout/unknown/retry. Bitiş: gerçek browser+API+SQL kayıt sayıları, skip ayrımı ve teardown. C-01 list10/protocol8 veya controlled17 bunun yerine geçmez. Dönüş walk/gerçek zoom/screen-reader ayrı ölçülür; çalışmayan PASS değildir.

### CODEX-ORCHESTRATOR

**CO-1.** Mevcut #230/#233/#228 sahiplik sırasını koru; yeni parallel spec/backend/create-link writer açma.

**CO-2.** Recipient5626181170 ve yayımlanmış protocol/follow-up receipts tekrar istenmez.83 yolun insan sahiplik önerisinin sonucunu kendi kaydında takip et; onaysız açma ve17 C006 atamasından donor release üretme. Bu BUSINESS teslim merkezî kayda paralel yazmaz.

**CO-3.** 880 backend kanıtını ve9a/07a/ef frontend kanıtlarını ayrı tut. Ef CI frontend324 ve yeni7/7 metadata §9.2'de;07a'nın7/7'si taşınmadı. İki P2 dar kapanışı tüm dilim veya insan kabulü değildir; backend880/P2/SQL kapsamı yeniden açılmaz.

**CO-4.** C006 teknik politika engeli,83 yol insan sahipliği ve uygulanmamış form-command entegrasyonunu ayrı tut. Same-owner izinli C006 hazırlığı + yetkili donor entegrasyonu → pinli gerçek integrated browser. Tek-node C006 için gateway/loadbalancer/drain işi yok; yalnız gerçek ortakDB/karma havuz doğrulanırsa §4.6 ayrı deployment kapısıdır. Production topolojisi varsayılmaz.

**CO-5.** Final K3 exact backend/frontend/schema/fixture, fresh CI/SQL/browser ve iki bağımsız exact-head review kapılarını doğrula. Lane self-merge yok. #229/#232 insan incelemesi ve production Sinan kapısı ayrıdır.

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

Mevcut roleless/transport Cari düzeltmesi için yeni işletme kararı gerekmiyor. `roles=0/null` tercihi tekrar sorulmaz; request/response §3'te sabittir. P2 teknik düzeltmesi kaynakta tamamlanmıştır; yeni kullanıcı politikası değildir.

`NEEDS-SINAN-CARI-AUTO-001`: yalnız gelecekte implicit account permission kaynağı A/B/C kararı gerekir. ÖneriB, **onay değil**; mevcut Cari düzeltmelerinin ön şartı değildir. Çok şirketli piksel düzeni tüm akışı bekleten bir kullanıcı kararı yapılmaz.

Kaynak olarak [tarihli kullanıcı kararları](https://github.com/sinangym/GoymenERP/blob/e1fd69da69df36a72e559ea07163fb3f359b3c61/docs/decisions/SINAN_KARARLARI_2026-09-10.md), `docs/decisions/KARAR_SICILI.md`, `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`, mevcut Cari JIT/yetki sözleşmeleri ve öncelikli iki protocol handoff birlikte okunur. Eski #227 headerless örnekleri yeni contract'a aynen çalıştırılmaz; tarihsel kanıt olarak korunur.

#229 ve #232 insan review/acceptance kapıları açık kalır. Bu dosyanın güncellenmesi diğer ajana uygulama gönderildiği, onların çalıştırdığı, merge edildiği, runtime açıldığı veya ERP'nin kabul edildiği anlamına gelmez. Bu teslim kaynak okuma ve şartname güncellemesidir; ürün testleri CHATGPT-BUSINESS tarafından yeniden çalıştırılmamıştır.

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
