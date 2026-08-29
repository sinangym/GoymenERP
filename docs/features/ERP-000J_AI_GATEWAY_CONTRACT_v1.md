---
contract_id: ERP-000J-AI-CONTRACT
version: 1
contract_status: OPEN
readiness_document_status: ACCEPTABLE
implementation_status: BLOCKED
production_activation: NO-GO
owner: CODEX-PLATFORM
review_class: SHARED_AI_SECURITY_CROSS_MODULE_CONTRACT
reviewed_base_sha: d5bcd5b30ee79885cf64b3c4c638ddeb91426d20
official_openai_docs_verified_at: 2026-08-29
---

# ERP-000J — AI Gateway Fiziksel Readiness ve Contract v1

- Current-main yeniden doğrulama tarihi: 2026-08-29
- Görev: `ERP-000J — AI gateway iskeleti`
- Fiziksel contract kimliği: `ERP-000J-AI-CONTRACT`
- Kapsam: provider-agnostic gateway güven sınırı, tool protokolü ve kabul planı
- Readiness belgesi: **ACCEPTABLE**
- Contract kapanışı: **OPEN**
- Implementation: **NO-GO / BLOCKED**
- Production activation: **NO-GO**

## 1. Sonuç

Göymen ERP'nin AI yönü yeterince nettir: AI her sayfada yardım edebilir, fakat ERP'nin
üzerinde değildir. Model; kimlik, yetki, CompanyContext, onay, formül, iş doğrusu veya
transaction sahibi olamaz. AI doğrudan DB'ye, `DbContext`e, repository'ye ya da ham SQL'e
erişemez; yalnız server tarafından tanımlanmış dar ve sürümlü application tool'larını
kullanabilir.

Bugünkü platform CompanyContext, default-deny RBAC+ABAC, transactional append-only audit,
server-side correlation ve kalıcı idempotency/concurrency için doğru bir temel taşır.
Ancak aşağıdaki üç kapı kapanmamıştır:

1. `ADR_REGISTER.md`, `ADR-008`i `Kabul` gösterir; `docs/adr/` altında uygulanabilir
   fiziksel ADR-008 yoktur.
2. `OPEN-020`; bulut AI sağlayıcısı, sözleşme, KVKK aktarım politikası ve izinli veri
   sınıfları için açık kalır. Bulut egress'i default-deny'dır.
3. Bu dosyanın tanımladığı tool, politika, preview/onay, retention, kanıt ve operasyon
   alt kapıları kabul edilmemiştir. Bunların task-local toplu kimliği
   `ERP-000J-AI-CONTRACT`tır.

Bu artifact yeni provider, model, paket, SDK, endpoint, prompt, rol, permission, eşik,
retention süresi veya production davranışı seçmez. Readiness belgesinin `main`e alınması,
contract kapanışı ya da implementation izni değildir.

## 2. Bağlayıcı kaynaklar

Kaynak önceliği kök `AGENTS.md` sırasıdır.

| Kaynak | ERP-000J için bağlayıcı sonuç |
|---|---|
| `DEC-AI-001` | AI kesintisi temel ERP akışını durdurmaz; normal/manual akış yaşar. |
| `DEC-AI-002` | AI okur, açıklar, önerir, taslaklar ve deterministik önizlemeyi sunar; kendi kendine onay/yetki veremez. |
| `DEC-AI-003` | Tool kataloğu READ/DRAFT/SIMULATE/WRITE olarak ayrılır; write için idempotency, preview hash'i ve insan onayı gerekir. |
| `DEC-AI-004` | Hassas finans/cari/belge verisi local-first ve bulutta default-deny'dır. |
| `DEC-AI-005` | Kullanıcı, şirket, bağlam, model/prompt/tool sürümü, veri kapsamı, sonuç, düzeltme, onay ve ERP etkisi auditlenir. |
| `DEC-AI-006` | Geçici mesaj otomatik kalıcı kurumsal kural olmaz. |
| `DEC-AI-007` | Kimlik, miktar, fiyat, IBAN, vergi ve finans alanları deterministik doğrulama ister. |
| `DEC-AI-008` | OCR kesin kayıt yazmaz; özgün belge ve kullanıcı düzeltmesiyle bağlı taslak üretir. |
| `DEC-GOV-002` | Grup görünümü, CompanyId güven sınırını kaldırmaz. |
| `DEC-TEC-001` | Modüler monolit korunur; AI başka modül tablosuna doğrudan yazmaz. |
| `DEC-TEC-009` | Log, metric, trace, correlation ve alarm platform sınırına bağlıdır. |
| `AT-059` | Kalıcı değişiklik için fark önizlemesi ve yetkili onay zorunludur. |
| `AT-060` | Yetkisiz şirket verisi cevapta, özette, tool çağrısında veya kaynakta sızamaz. |
| `AT-068` | AI yetki taslağı/risk/fark üretir; yönetici onayı olmadan permission değişmez. |
| `OPEN-020` | Provider, sözleşme, KVKK aktarımı ve izinli veri sınıfları açık karardır. |
| `ERP-000K-SHELL-CONTRACT` v1 | AI yalnız server-capability-gated shell extension'ı olarak mount edilir; contract path/version fiziksel handoff'tur. |

`AT-059`, `AT-060` ve `AT-068`, `ACCEPTANCE_MATRIX.csv` içinde **Taslak** durumundadır
ve kanıt alanları boştur. Bu belge bu senaryoları geçmiş veya accepted saymaz.

## 3. Current-main gerçeği

İncelenen `main@d5bcd5b30ee79885cf64b3c4c638ddeb91426d20` üzerinde:

- ERP-000A/B/C/D/E `done`; ERP-000M/O/N/L/K readiness artifact'leri `main`dedir ve
  ilgili görevler `blocked` kalır.
- Bu PR öncesi ERP-000J satırı A-only `ready` idi. Bu delta yalnız J satırını
  fail-closed `blocked` ve A/B/C/D/E/L doğrudan bağımlılıklarıyla düzeltir.
- `ErpPermissions` içinde AI'ye özel permission yoktur. Mevcut roller veya rol adları
  AI capability'si sayılmaz.
- `/api/v1/identity/me`, aktif kullanıcı profili ve izinli şirket kimliklerini döndürür;
  permission, ABAC scope, feature availability veya AI capability sağlamaz.
- Identity içindeki `AccessGrantRequest`, rol erişim talebine özgüdür. Genel AI write
  approval/reauth owner'ı veya her domain komutuna uygulanabilen onay sözleşmesi değildir.
- Audit zarfında `ApprovalContextJson`, `AiInvolvement` ve `AiContextJson` alanları vardır.
  Bu alanların varlığı AI Gateway, policy motoru, onay sahibi veya provider adapter'ı
  uygulandığı anlamına gelmez.
- ERP-000D audit'i Company-scoped, append-only ve kritik başarılı/no-change business
  etkisiyle transactional çalışır. Reddedilen/yetkisiz genel denemeler için kabul edilmiş
  durable security-event channel yoktur.
- Issue `#63` ile `IDENTITY_GLOBAL_SECURITY_EVIDENCE_v1.md`, yalnız Identity/control-plane
  için **DRAFT / NO-GO** sınırıdır; genel AI güvenlik olayı kanalı değildir.
- Server-side correlation vardır; correlation kimlik, CompanyContext, authorization veya
  business success kanıtı değildir.
- Kalıcı idempotency/concurrency omurgası vardır; AI Gateway'in bu tabloya doğrudan yazma
  hakkı yoktur.
- AI provider adapter'ı, gateway endpoint'i, tool registry, prompt store, model/SDK,
  retrieval/vector store, attachment pipeline, eval harness, token budget store veya
  production egress yapılandırması yoktur.
- `ERP-000K-SHELL-CONTRACT` v1 fiziksel olarak vardır; contract `OPEN`, implementation
  `BLOCKED`, production activation `NO-GO`dur. AI slotu owner contract ve server
  capability olmadan görünür işlev değildir.

Bu mevcut parçaların hiçbiri tek başına ERP-000J'yi implementation-ready yapmaz.

## 4. Amaç ve kapsam dışı

ERP-000J'nin ilerideki dar amacı:

1. ERP tarafından tanımlanan, sürümlü ve default-deny tool registry sunmak.
2. Kimlik, CompanyContext, permission, ABAC ve data-class policy'yi modelden önce ve her
   tool çağrısında server-side değerlendirmek.
3. Tool argümanlarını strict schema sonrasında da deterministik biçimde yeniden
   doğrulamak.
4. READ, DRAFT, SIMULATE ve WRITE etkilerini fiziksel portlarla ayırmak.
5. WRITE niyetini kanonik preview/hash, açık insan onayı, gerekiyorsa reauth ve güncel
   authorization ile bağlamak.
6. Onaylı niyeti sahip domain'in idempotent ve concurrency-safe application command'ına
   iletmek.
7. Business audit, durable security event ve transient telemetry kanallarını doğru
   correlation ile ayırmak.
8. Provider kesintisinde normal ERP akışını korumak ve capability'yi görünür biçimde
   degrade etmek.

Bu readiness çalışmasının dışında kalanlar:

- OpenAI veya başka bir production provider seçmek;
- model ailesi/snapshot, SDK, NuGet/NPM paketi, fiyat, kota, bölge veya SLA seçmek;
- API key, secret, endpoint, deployment, network egress veya data-residency ayarı yapmak;
- production system/developer prompt'u veya business tool implementasyonu yazmak;
- Remote MCP, web search, file search, hosted shell/code interpreter ya da computer use
  açmak;
- ham SQL, DB, repository, object-store key'i veya sınırsız modül API'si açmak;
- finans, stok, üretim, rapor, belge veya yetki iş kuralı üretmek;
- yeni role/permission/approval threshold/reauth süresi uydurmak;
- DB schema, migration, source, package, workflow, test veya UI implementation değiştirmek;
- `AT-059`, `AT-060` veya `AT-068`i `Geçti` yapmak.

## 5. Bağımlılık ve sahiplik

| Yetenek | Sahip | ERP-000J sınırı |
|---|---|---|
| Kimlik ve CompanyContext | ERP-000B/C | Server doğrulamalı bağlamı tüketir; modelden actor/tenant almaz. |
| RBAC/ABAC ve SoD | ERP-000C | Güncel default-deny sonucu tüketir; permission veya rol üretmez. |
| Business audit ve correlation | ERP-000D | Kanonik AI metadata'sını sahip domain etkisine bağlar; audit bypass etmez. |
| Idempotency ve concurrency | ERP-000E | Domain command zarfını kullanır; J doğrudan execution DB'sine yazmaz. |
| Gözlemlenebilirlik ve security event | ERP-000L | Telemetry ve genel durable security-event owner/failure/retention sözleşmesini sağlar. |
| Ortak UI shell | ERP-000K | AI extension point'i path/version ile sunar; J business shell'i sahiplenmez. |
| Async/outbox | ERP-000F, yalnız gerekiyorsa | Uzun iş veya dış etki seçilirse kabul edilmiş event contract tüketilir. |
| Belge/OCR | ERP-000G, yalnız ilgili tool varsa | Orijinal/version/hash ve yetkili extraction contract'ını tüketir. |
| Global arama | ERP-000I, yalnız ilgili tool varsa | Search sonucu yetki bileti değildir; source backend yeniden doğrular. |
| Domain command/query | İlgili sahip modül | İş doğrusu, formula ve transaction sahibidir; J ikinci system-of-record kurmaz. |

Manifest doğrudan bağımlılıkları A/B/C/D/E/L'dir. K, J'nin backend hard dependency'si
değildir; buna rağmen görünür AI deneyimi ancak `ERP-000K-SHELL-CONTRACT` v1 extension
gate'i uygulanıp kabul edildiğinde mount edilebilir. F/G/I ve domain modülleri, ilgili
tool registry'ye girdiğinde explicit dependency/contract review'u ister; bugünden
varsayılmaz.

## 6. Güven modeli ve veri sınıfları

Model çıktısı, tool adı/argümanı, confidence, citation, OCR sonucu, belge metni, retrieval
sonucu ve provider metadata'sı **güvenilmeyen girdidir**. Şema uyumu yalnız sözdizimsel
uyumdur; authorization veya business doğruluğu değildir.

AI şu yetkilere sahip değildir:

- actor, session, CompanyId, SiteId, LocationId veya WorkCenterId seçmek/genişletmek;
- permission, scope, SoD, approval veya reauth sonucunu ilan etmek;
- veri sınıfını düşürmek ya da redaction/egress politikasını gevşetmek;
- fiyat, vergi, kur, IBAN, miktar, stok, üretim veya finans değerini kesinleştirmek;
- preview hash, approval, expected version veya idempotency sonucunu doğrulamak;
- transaction commit etmek ya da audit/security-event yazımını başarı saymak;
- registry'ye tool, URL, provider, MCP server veya dış kaynak eklemek.

Kanonik veri sınıfları yalnız şunlardır:

1. `Genel`
2. `Şirket içi`
3. `Gizli`
4. `Çok gizli`

`docs/security/AI_VERI_GUVENLIK.md` uyarınca `Çok gizli` veri yereldir ve buluta
gönderilmez. `OPEN-020` bu mevcut yasağı sessizce gevşetemez; böyle bir değişiklik yeni,
açık ve daha yüksek öncelikli ürün sahibi kararı ile güvenlik/KVKK review'u gerektirir.
Türetilmiş çıktı, kaynakların en yüksek koruma sınıfını ve policy'nin yükselttiği sınıfı
miras alır. Birleştirme/re-identification riski sınıf düşürmez.

## 7. Tool registry sözleşmesi

Her tool implementation öncesinde review edilmiş değişmez metadata taşır:

- benzersiz, semantik sürümlü tool kimliği ve sahip modül;
- `READ`, `DRAFT`, `SIMULATE` veya `WRITE` risk sınıfı;
- dar JSON input/output schema, field allowlist ve byte/cardinality limitleri;
- gerekli permission ve Company/Site/Location/WorkCenter scope semantiği;
- kabul edilen veri sınıfları, minimizasyon/redaction ve egress matrisi;
- deterministik validator ve sahip command/query contract kimliği;
- timeout, retry-safety, idempotency ve expected-version gereksinimi;
- preview, approval, reauth ve expiry gereksinimi;
- audit/security-event/telemetry routing ve hassas alan yasağı;
- owner, deprecation, compatibility, rollback ve kill-switch yolu.

Registry default-deny'dır. Tanımsız tool/sürüm/risk/permission/data class/schema/provider
capability çalıştırılmaz. Model serbest metinle endpoint veya tool adı üretemez.

Risk sınıfları:

1. **READ:** Yetkili backend query; source açılışında yeniden authorization.
2. **DRAFT:** Düzenlenebilir, business etkisiz taslak.
3. **SIMULATE:** Sahip deterministik servisinden gelen kayıt dışı önizleme.
4. **WRITE:** Yalnız bağlı preview + insan onayı + güncel authorization + idempotent
   domain command.

READ/DRAFT/SIMULATE business tablosu, domain event, outbox veya command-execution kaydı
üretemez. İzinli security evidence/telemetry davranışı ayrı kanaldır. Provider duplicate,
parallel, mixed ya da multiple WRITE call üretebilir; her write niyeti ayrı preview ve
onay ister. Yalnız sahip domain'in önceden kabul edilmiş atomik composite command'ı tek
preview altında birlikte ele alınabilir.

## 8. Zorunlu politika sırası

Gateway aşağıdaki sırayı atlayamaz:

1. Dış kimliği aktif yerel ERP kullanıcısına çöz.
2. Request-scoped CompanyContext'i server-side çöz; boş/değişmiş bağlamda fail-closed ol.
3. Server-backed AI capability ve tool/version allowlist'ini kontrol et.
4. Tool risk sınıfı ile gerekli permission/ABAC/SoD koşulunu çöz.
5. Güncel authorization'ı değerlendir; istemci rol adı/JWT decode/local sabit kullanma.
6. Input schema, boyut, alan allowlist'i ve argüman kanonikleştirmesini doğrula.
7. Company/resource kimliklerini aktif context ve source backend üzerinden yeniden doğrula.
8. Veri sınıfı, minimizasyon, redaction, attachment ve provider egress politikasını uygula.
9. Sahip domain'in deterministik validator/formula/query'sini çalıştır.
10. READ/DRAFT/SIMULATE ise kendi etkisiz portunu kullan; WRITE niyetinde yalnız kanonik
    preview/hash üret ve dur.
11. Onay anında actor, CompanyContext, capability, permission/scope/SoD, tool/policy
    sürümü, target/version/hash/expiry ve gerekiyorsa reauth'ı yeniden doğrula.
12. Sahip domain'in public application command'ını idempotency ve expected version ile
    çağır; AI Gateway idempotency DB'sine doğrudan yazmaz.
13. Business sonucu transactional audit'e; yetkisiz/rejected denemeyi kabul edilmiş
    durable security-event kanalına; transient provider/ağ tanısını telemetry'ye yönlendir.

Provider'ın `authorized`, `approved`, `success`, `safe` veya benzeri alanı bu sıranın
hiçbir adımını geçemez.

## 9. Güncel OpenAI teknik aday notları

Bu bölüm OpenAI seçimi değildir. Yalnız adapter tasarımında yeniden doğrulanması gereken
güncel resmî capability notudur. Doğrulama tarihi `2026-08-29`dur.

- Responses API function tools ve `tool_choice` ile modelin çağırabileceği tool setini
  kısıtlamayı destekler.
- Function tool'da `strict: true`, schema uyumunu best-effort yerine güçlendirir.
  Strict object parametrelerinde `additionalProperties: false` ve bütün property'lerin
  `required` olması gerekir; opsiyonel alan null-union ile modellenebilir.
- Sistem tool/function/data ile bağlanıyorsa function calling; kullanıcıya verilecek
  yapılandırılmış cevap için Responses `text.format` altındaki JSON Schema uygundur.
- Structured Outputs schema uyumunu sağlar; permission, CompanyContext, SoD, approval,
  veri sınıfı veya business doğruluğunu kanıtlamaz. Bütün tool argümanları server-side
  yeniden doğrulanır.
- `refusal`, `incomplete`, content-filter interruption, timeout, failed/truncated output,
  streaming argument delta ve finalleşmemiş/multiple call normal başarı parse'ı değildir;
  handler, preview veya write başlatmadan ayrı terminal state olarak ele alınır.

Resmî kaynaklar:

- Function calling ve `tool_choice`/strict:
  <https://developers.openai.com/api/docs/guides/function-calling>
- Structured Outputs ve `text.format`:
  <https://developers.openai.com/api/docs/guides/structured-outputs>
- API data controls:
  <https://developers.openai.com/api/docs/guides/your-data>

Bu bilgiler release/implementation anında yeniden doğrulanır. Provider/model/package/SDK
seçimi, current capability'nin varlığından otomatik çıkmaz. Doğrulanmamış Agent Builder
kapanış tarihi veya başka ürün yaşam döngüsü iddiası bu contract'a alınmaz.

## 10. Preview, insan onayı ve reauth binding

### 10.1 Kanonik preview zarfı

Preview en az şunları aynı değişmez snapshot/hash'e bağlar:

- server-side actor/session ve CompanyContext;
- tool kimliği/sürümü/risk sınıfı ve sahip domain command türü;
- target kayıt kimlikleri ile expected version'lar;
- kanonik command payload veya güvenli hash'i;
- deterministik validation sonucu ve kullanıcıya gösterilen fark;
- permission/scope/SoD ve policy sürüm referansları;
- veri sınıfı/redaction, prompt policy ve adapter/model deployment referansları;
- oluşturma zamanı, expiry ve tekil preview kimliği;
- correlation/causation.

Model açıklaması preview hash kapsamı değildir. Kullanıcıya gösterilen fark ile
yürütülecek command birebir aynı kanonik payload'a bağlı olmalıdır.

### 10.2 Onay ve reauth

- Onay belirli actor + company + preview ID/hash + target/version + tool/policy sürümü
  içindir; başka bağlama taşınamaz.
- Payload, kayıt sürümü, actor, CompanyContext, capability, permission/scope/SoD, tool
  sürümü veya policy değişirse eski onay reddedilir ve yeni preview gerekir.
- Zımni sohbet onayı, blanket consent veya modelin “onaylandı” ifadesi kabul edilmez.
- Backend onay binding'ini UI'dan bağımsız yeniden doğrular.
- Hangi tool için second approval veya reauth gerektiği; reauth yöntemi, TTL/expiry ve
  approval owner'ı mevcut kaynaklarda kilitli değildir. Bunlar ADR-008 ve ilgili domain
  policy ile açıkça kabul edilmeden implementation yoktur.
- Mevcut Identity `AccessGrantRequest`, generic AI approval/reauth yerine kullanılamaz.

## 11. Idempotency, concurrency ve domain command sınırı

AI Gateway:

- transaction, business table veya `CommandExecutions` tablosuna doğrudan yazmaz;
- yalnız sahip modülün server-side reauthorized public application command'ını çağırır;
- idempotency key'i/command type/company/payload hash bağını domain contract'a geçirir;
- expected version ve stale preview kontrolünü sahip domain ile uygular;
- provider timeout'unda bilinmeyen write sonucunu otomatik ikinci kez başlatmaz;
- aynı key + aynı payload için önceki güvenli sonucu replay eder;
- aynı key + farklı payload'ı kesin reddeder;
- duplicate/parallel provider çağrısının ikinci business olayı veya ikinci başarılı audit
  üretmesine izin vermez;
- audit başarısızlığında business command'ı başarılı göstermez.

Retry provider adapter ve domain command için ayrı değerlendirilir. READ/DRAFT çağrısının
retry-safe olması WRITE command'ın yeniden yürütülebilir olduğu anlamına gelmez.

## 12. Tenant, permission ve capability sınırı

- Company filtresi provider context, retrieval, query, count, facet, suggestion, source,
  cache ve hata üretilmeden önce uygulanır.
- Yetkisiz kayıt; cevap, özet, tool argümanı, source link, toplam/sayı, timing farkı,
  error text, log/trace veya token metric'inde varlığını belli edemez.
- Tool argümanındaki CompanyId authorization kaynağı değildir. Alan ya yasaklanır ya da
  active server CompanyContext ile exact eşleşir.
- Dar Site/Location/WorkCenter scope, temsil edilemeyen tool için company-wide'a
  yükseltilmez; tool fail-closed kalır.
- Grup okuma tek-company context'ten türetilmez. Ayrı server-side permission ve explicit
  owner contract olmadan yasaktır.
- Permission/capability daralınca cached response, retrieval state, pending approval,
  provider object ve açık UI session eski kapsamla devam edemez.
- Modelden, JWT'den, rol adından, client sabitinden veya endpoint probing'den capability
  türetilmez. Server-backed versioned capability response'u gerekir.

AI'ye özel permission, capability response şekli ve assignment matrisi bugün yoktur.
Bu contract permission adı veya rol ataması üretmez.

## 13. Prompt injection, tool allowlist ve egress

Belge, e-posta, cari/ürün açıklaması, iç mesaj, OCR, web içeriği, model cevabı ve tool
sonucu **veridir; talimat değildir**.

- System/developer policy, registry ve server authorization bu içerikten üsttedir.
- İçerikteki “şirket değiştir”, “önceki talimatı yok say”, “şu tool'u çağır” veya
  `approved=true` metni capability üretmez.
- Tool sonucu ikinci tool için trusted instruction olmaz; her hop yeniden schema,
  permission, data class ve DLP kontrolünden geçer.
- Model tarafından oluşturulan URL'ye serbest server-side HTTP yapılmaz. Network egress
  default-deny; exact scheme/host/port, DNS/redirect hop, private/link-local/metadata IP,
  IPv6-mapped adres, DNS rebinding, header/credential forwarding, MIME, byte ve timeout
  kontrolleri ister.
- Remote MCP ve hosted tool'lar otomatik allowlist değildir; her biri ayrı provider,
  data class, retention, egress, permission ve audit review'u ister.
- Model HTML/Markdown/URL çıktısı güvenli renderer ile escape/sanitize edilir; CSP ve
  izinli scheme uygulanır; script/event handler/`javascript:`/otomatik external resource
  veya sahte onay kontrolü üretilemez.
- Secret, token, signed URL, connection string ve ham hassas payload prompt/log/trace'e
  varsayılan olarak girmez.
- Tur/tool/token/byte/süre/maliyet budget'ı server-side bounded olmalıdır. Exact eşikler
  ölçüm ve kabul edilmiş politika olmadan bu belgede seçilmez.

## 14. Attachment, belge ve OCR sınırı

- Attachment yalnız belge sahibinin yetkili backend service'i üzerinden alınır; path,
  blob key, signed URL veya provider file ID authorization kaynağı değildir.
- MIME, signature/magic byte, boyut, sayfa, malware ve encrypted/password-protected file
  davranışı belge contract'ında fail-closed tanımlanır.
- Orijinal blob, logical document, version, hash ve record link kaybolmaz.
- OCR/extraction; sayfa/alan/source span, confidence, model/tool/policy version ve kullanıcı
  düzeltmesini taşıyan taslaktır. Kesin business kayıt değildir.
- Düşük güven, eksik sayfa, truncation, karışık belge, el yazısı veya desteklenmeyen format
  görünür state üretir; eski sonuç güncel gibi gösterilmez.
- Aynı attachment'ın farklı şirket/bağlam kullanımı fiziksel dedup gerekçesiyle yetki
  kapsamlarını birleştirmez.
- Provider file/conversation/vector-store nesnesi açılırsa lifecycle, deletion/expiry,
  CompanyContext invalidation ve legal-hold ilişkisi ayrıca kabul edilir.

## 15. Veri minimizasyonu, provider state ve retention

`OPEN-020` kapanmadan cloud AI default davranışı **reddet**tir. Local model dahi
authorization, minimizasyon, audit ve tool policy'yi atlayamaz.

OpenAI Docs üzerinde 2026-08-29 tarihinde doğrulanan current aday gerçekleri:

- API verisi varsayılan olarak model eğitimi için kullanılmaz; açık opt-in/data-sharing
  ayrı ve doğrulanması gereken bir karardır.
- `/v1/responses` application state varsayılan durumda veya `store=true` ile en az
  30 gün saklanır.
- `store=false`, application state davranışını değiştirir; fakat varsayılan abuse
  monitoring retention'ı ve özellik-bazlı istisnaları tek başına ortadan kaldırmaz.
- ZDR/MAM uygunluk ve etkinleştirme onaya/sözleşmeye tabidir. ZDR, Responses için
  `store`u false davranışına zorlar; ZDR-eligible olmayan capability'ler yine state
  saklayabilir.
- Background mode polling için yaklaşık 10 dakika disk state'i kullanabilir; `store`
  seçimi ve MAM/ZDR bağlamı ayrıca doğrulanmalıdır.
- Conversations “silinene kadar”, files manuel silme veya `expires_after` politikasına
  kadar tutulabilir; bunlar Responses çağrısıyla aynı lifecycle değildir.
- Remote MCP üçüncü taraf retention politikasına tabidir.
- Prompt caching, hosted containers/skills/shell/code interpreter, audio, file/image
  input ve üçüncü taraf network servislerinin ayrı storage/retention/eligibility sınırları
  vardır. Tek `store=false` bayrağı hepsini kapsamaz.
- File/image input güvenlik taraması gibi belgelenmiş istisnalar, ZDR/MAM olsa bile belirli
  içeriğin inceleme için tutulmasına yol açabilir.

Bu current facts ürün kararı değildir. OPEN-020 karar paketi hesap/project/endpoint/tool
bazında en az şunları kanıtlamalıdır:

- provider, alt işleyenler, sözleşme/DPA, KVKK hukuki dayanak ve veri bölgesi;
- dört kanonik veri sınıfı için local/cloud/region/tool allowlist'i;
- prompt/response/tool argument/result/attachment/cache/conversation/file/telemetry için
  ayrı retention ve deletion/expiry;
- ZDR/MAM veya eşdeğer kontrolün gerçek hesap/project attestation'ı;
- provider state kimliğinin şifreleme, mapping, access ve invalidation sahibi;
- data subject request, legal hold, incident ve provider deletion verification;
- training/data sharing ayarının sözleşme ve regression kanıtı;
- policy version, yetkili değişiklik, rollback ve egress kill switch.

## 16. Session, cache ve silme

- Stateless request, ERP-managed encrypted state veya provider-managed conversation
  seçeneklerinden biri açık kararla seçilir; sessiz kalıcı state yoktur.
- Şirket değişimi aynı state içinde context mutate etmez; eski CompanyContext'e bağlı
  history, retrieval, tool output ve approval invalid olur. Yeni güvenli context/session
  gerekir.
- Kullanıcı pasifleşmesi, permission daralması, role/scope değişimi, işten ayrılma,
  document access kaybı veya policy revision aktif state'i fail-closed geçersiz kılar.
- Provider object ID ham client/log/metric label'ı değildir; restricted mapping veya
  keyed reference gerekir.
- Cache key actor/tenant/scope/tool/policy/data-class sınırını taşır; cross-company veya
  cross-user reuse yasaktır.
- Audit kanıtı ile prompt/response içeriğini saklama aynı şey değildir. Mümkün olduğunda
  içerik yerine sürüm, hash, sınıf, kapsam, karar ve sonuç metadata'sı tutulur; exact
  retention OPEN-020 ve ERP-000L/N kararlarıyla belirlenir.

## 17. Outage, degradation ve maliyet sınırı

- AI kesilirse temel ERP ve manual kayıt akışı çalışır.
- Provider timeout/rate-limit/refusal/incomplete ile domain command conflict/failure ayrı
  kullanıcı state'leridir; sessiz başarıya çevrilmez.
- Capability unavailable olduğunda K shell AI extension'ını görünür disabled/degraded
  state ile yönetir; inert çalışan kontrol veya mock cevap göstermez.
- Salt-okuma/taslak çağrısı yalnız açıkça retry-safe ise bounded retry alabilir. Retry,
  backoff ve timeout exact değerleri config/policy ve ölçüm olmadan seçilmez.
- Circuit breaker/kill switch provider, adapter ve tool sürümü bazında fail-closed olur.
- Fallback provider/model daha geniş data class, tool veya permission alamaz; aynı policy
  ve egress review'una tabidir.
- Unknown write outcome kalıcı idempotency kaydından çözülmeden tekrar başlatılmaz.
- Token/latency/cost budget ve alert; low-cardinality telemetry, Company-scoped güvenli
  aggregate ve ERP-000L owner contract'ıyla belirlenir. Raw CompanyId/ActorId/provider ID
  global metric label'ı değildir.

## 18. Audit, durable security event ve telemetry

AI kanıtı en az şunları ilişkilendirebilmelidir:

- actor, CompanyContext, permission/scope/SoD değerlendirmesi;
- source channel, kullanıcı amacı ve correlation/causation;
- adapter/model deployment, prompt policy, tool ve schema sürümü;
- data class, alan allowlist'i, redaction/egress policy ve yaklaşık veri kapsamı;
- tool çağrıları, deterministic validation ve policy sonucu;
- preview ID/hash, approval ID/actor/zaman ve varsa reauth reference;
- idempotency sonucu, expected/actual version ve domain business etkisi;
- kullanıcı düzeltmesi, nihai sonuç ve iptal/rollback bağı.

Kanallar ayrıdır:

1. Başarılı/no-change kritik ERP etkisi, sahip domain transaction'ıyla Company-scoped
   append-only business audit'e bağlanır.
2. Yetkisiz/rejected, tenant-crossing, prompt/tool abuse veya policy bypass denemesi,
   kabul edilmiş durable security-event kanalına gider. Böyle bir genel kanal bugün yoktur;
   J implementation bu kapı kapanmadan başlamaz.
3. Provider/ağ/latency/rate-limit gibi transient tanı, ERP-000L telemetry'sindedir.

Audit alanlarının mevcut olması gateway'in uygulandığı anlamına gelmez. Trace sampling,
business audit veya durable security evidence kaybettiremez. Ham prompt/response/secret,
CompanyId, ActorId, record/document/provider ID, müşteri adı ya da belge no varsayılan
metric label/log payload değildir. Her kanalın failure, retention, access, legal-hold ve
redaction semantiği ayrı kabul edilir.

## 19. K shell extension gate

Fiziksel handoff:

- path: `docs/features/ERP-000K_SHELL_CONTRACT_v1.md`
- contract: `ERP-000K-SHELL-CONTRACT`
- version: `1`
- current status: `OPEN / BLOCKED / production NO-GO`

J, K'yi hard manifest dependency yapmaz; backend tool gateway headless kullanılabilir.
Ancak kullanıcıya görünen sayfa içi panel, sağ drawer, attachment, suggestion, preview,
approval veya `Uygula` davranışı şu kapılar olmadan mount edilemez:

- server-backed capability ve permission/scope response'u;
- versioned feature/route/extension owner contract'ı;
- single-company ve explicit group-read ayrımı;
- CompanyContext switch, dirty form, pending write, cache ve stale-response fence'i;
- loading/empty/unauthorized/error/conflict/degraded state'leri;
- accessibility, focus/keyboard, responsive drawer ve güvenli content renderer;
- J owner contract'ının kabul edilmiş path/version ve implementation capability'si.

K shell business prompt, tool, formula, mock cevap veya permission semantiği üretmez; J de
ortak navigation/router/query/cache davranışını sahiplenmez.

## 20. Task-local kabul planı

Aşağıdaki senaryolar readiness planıdır; yeni kanonik `AT-*` kimliği değildir. ADR-008 ve
owner contract review'uyla merkezi kabul kayıtlarına taşınmadan geçmiş sayılmaz.

### 20.1 Kimlik, tenant ve permission

1. A şirketi kullanıcısı, B şirketi ID'sini prompt/tool argümanına yazsa dahi cevap,
   source, count, timing, error, cache veya audit/telemetry'den B verisini öğrenemez.
2. CompanyContext yok/geçersiz/değişmişse provider çağrısından önce fail-closed olur.
3. Tanımsız AI capability, permission, tool veya sürüm hiçbir yetki üretmez.
4. Dar ABAC scope temsil edilemiyorsa tool company-wide'a yükselmez.
5. Permission/capability daralması eski session/cache/pending approval'ı geçersiz kılar.
6. Source link açılışında sahip backend güncel authorization'ı yeniden doğrular.

### 20.2 Tool ve prompt-injection

7. Allowlist dışı tool/URL/MCP/provider çağrısı çalışmaz.
8. Extra field, yanlış tip, aşırı boyut, malformed/old schema ve unsupported version
   fail-closed olur.
9. Strict-schema-valid olsa da foreign Company/resource ID, geçersiz business state,
   tutar/IBAN/miktar veya `authorized=true` deterministik validator'dan geçmez.
10. Belge/OCR/e-posta içindeki injection policy, context veya tool seçimini değiştirmez.
11. DNS/redirect/rebinding/private/metadata IP, unsafe scheme/MIME/header forwarding ve
    unsafe rendered content adversarial testte reddedilir.
12. Refusal/incomplete/timeout/truncated/stream delta/multiple call terminal state'i
    handler, preview veya write başlatmaz.

### 20.3 Preview, approval ve write

13. WRITE tool preview ve açık yetkili insan onayı olmadan domain command üretmez.
14. READ/DRAFT/SIMULATE gerçek SQL negatif testinde business tablo, domain event, outbox
    veya command-execution etkisi üretmez.
15. Kullanıcıya gösterilen fark ve yürütülecek kanonik payload aynı hash'e bağlıdır.
16. Actor/company/target/version/payload/tool/policy/permission değişirse eski onay
    reddedilir.
17. Gerekli reauth/second approval yoksa command çalışmaz; self-approval/SoD korunur.
18. Aynı key + aynı payload tek business effect/audit üretir ve güvenli replay yapar.
19. Aynı key + farklı payload kesin reddedilir.
20. Duplicate/parallel/multiple provider call ayrı onay olmadan ek etki üretmez.
21. Provider timeout sonrası unknown write otomatik yinelenmez.
22. Audit insert failure business write'ı rollback eder ve başarı gösterilmez.

### 20.4 Data, attachment ve retention

23. `Çok gizli` veri cloud adapter'a hiçbir koşulda gönderilmez.
24. OPEN-020 matrisinde izinli olmayan data class provider çağrısından önce reddedilir.
25. Redaction sonrası görev güvenli/doğru yapılamıyorsa daha fazla veri açmak yerine
    yetersizlik sonucu verilir.
26. Attachment erişimi source document/version/hash ve CompanyContext ile yeniden
    doğrulanır; OCR kesin kayıt değildir.
27. Provider `store`, conversation, file, cache, background veya hosted-tool drift'i
    attestation/regression gate'ini düşürür ve production çağrısını bloklar.
28. Deletion/expiry talebi ERP mapping ve provider kanıtıyla doğrulanır; legal hold
    sessizce aşılmaz.

### 20.5 Operasyon, kanıt ve kalite

29. Business audit, durable security event ve transient telemetry doğru kanala gider;
    kanallar birbirinin yerine geçmez.
30. AI kapalı/provider unavailable iken normal ERP akışı tamamlanır.
31. Fallback daha geniş data/tool/permission alamaz.
32. Türkçe golden eval; belirsiz miktar/birim/para, yanlış tool, tenant sızıntısı,
    hallucinated source, injection, unsafe attachment, timeout ve replay örneklerini kapsar.
33. Eval yalnız metin benzerliği değil authorization, tool selection, schema, source,
    deterministic value ve side-effect absence ölçer.
34. Adapter/model/prompt/tool revision'ında P0 güvenlik eval gerilerse rollout durur.
35. Gerçek SQL acceptance; unauthorized/rejected/no-effect, replay, conflict, audit
    rollback, tenant nondisclosure ve concurrent approval yarışını kanıtlar.

## 21. Test ve red-team stratejisi

### Unit/contract

- registry default-deny, schema/version/risk/data-class matrisleri;
- policy sırası ve provider çağrısından önce short-circuit;
- canonical preview/hash/approval/expiry/reauth binding;
- tool argument revalidation ve source authorization;
- redaction/taint/provenance ve `Çok gizli` cloud deny golden seti;
- refusal/incomplete/failed/timeout/stream/duplicate/parallel fixtures;
- attachment/OCR truncation ve unsupported state'leri;
- adapter state/retention config attestation;
- audit envelope canonicalization ve unsafe payload yasağı.

### Integration ve gerçek SQL

- gerçek CompanyContext/RBAC/ABAC ve sahip query/command sınırı;
- READ/DRAFT/SIMULATE için sıfır business/command/outbox etkisi;
- preview/onay sonrası permission/version/policy drift invalidation;
- idempotency replay/collision ve optimistic concurrency race;
- business write + audit atomikliği ve audit-failure rollback;
- durable security-event unavailable olduğunda fail-closed davranış;
- cross-company ID/count/source/timing/error nondisclosure;
- deterministic fake provider ile malformed, rate-limit, timeout ve unknown outcome.

### E2E, accessibility ve operasyon

- K shell üzerinden capability-gated READ/DRAFT/SIMULATE/WRITE ayrımı;
- preview/fark/açık onay/reauth/conflict/degraded UI;
- AI kapalıyken eksiksiz manual akış;
- Türkçe içerik, keyboard/focus, screen reader, responsive drawer ve safe renderer;
- kill switch, provider outage ve recovery runbook;
- yalnız OPEN-020 kabulünden sonra opt-in, secrets-safe provider contract testi;
- gerçek müşteri verisi veya geliştirici kişisel API anahtarı CI'da kullanılmaz.

Threat model ve red-team en az prompt injection, indirect injection, data exfiltration,
cross-tenant inference, tool confusion, excessive agency, replay, stale approval, unsafe
output rendering, SSRF/egress, attachment bomb, secret leakage, denial-of-wallet ve
provider-retention drift sınıflarını içerir.

## 22. Fiziksel ADR-008 karar paketi

Kodlamadan önce ayrı security/architecture review'uyla fiziksel `docs/adr/ADR-008-*.md`
oluşturulmalı ve en az şunları kilitlemelidir:

1. Gateway katmanları, provider portu ve modül sahipliği.
2. Tool registry metadata, sürüm, risk ve deprecation modeli.
3. Policy evaluation sırası ve default-deny davranış.
4. CompanyContext/RBAC/ABAC/SoD, capability ve source reauthorization.
5. Preview canonicalization/hash, approval binding, expiry ve reauth owner'ı.
6. Domain command, idempotency, concurrency ve unknown-outcome semantiği.
7. Business audit, durable security-event ve telemetry sahipliği/failure semantiği.
8. Dört veri sınıfı, minimizasyon/redaction/egress/secret policy bağlantısı.
9. Prompt injection, attachment, remote content, URL/MCP/hosted tool sınırı.
10. Provider state/retention/cache/conversation/file/deletion modeli.
11. Timeout/retry/fallback/kill switch ve manual ERP fallback.
12. Eval/red-team, rollout/rollback ve production activation kapıları.

ADR register'daki tek satır fiziksel ADR'nin yerine geçmez.

## 23. `ERP-000J-AI-CONTRACT` kapanış koşulları

Bu task-local blocker ancak aşağıdaki kanıtlarla kaldırılabilir:

- fiziksel ADR-008 security/architecture review ile kabul edildi;
- OPEN-020 provider/KVKK/data-class/retention/egress kararlarıyla kapandı;
- A/B/C/D/E/L doğrudan bağımlılıkları implementation olarak kabul edilip `main`dedir;
- AI-specific server capability ve permission/scope/SoD contract'ı kabul edildi;
- pilot READ/DRAFT/SIMULATE/WRITE tool seti ve owner modüller seçildi;
- strict schema, argüman revalidation ve deterministic validator owner'ları belirlendi;
- preview/approval/reauth/idempotency/concurrency/unknown outcome kilitlendi;
- durable general security-event owner/store/failure/retention/access contract'ı kabul
  edildi; Identity #63 bunun yerine sayılmadı;
- attachment/OCR ve source authorization contract'ları ilgili owner ile kabul edildi;
- provider account/project/endpoint/tool data-control attestation ve deletion kanıtı
  review edildi;
- K shell extension path/version ile server capability mount gate'i kabul edildi;
- task-local senaryolar merkezi acceptance kayıtlarına taşındı;
- gerçek SQL, fake-provider, eval/red-team ve opt-in provider test planları geçti;
- P0/P1 security/architecture/tenant bulgusu kalmadı.

## 24. Kodlama ve production kapısı

### Şimdi izinli

- ADR-008 ve OPEN-020 karar paketlerini insan review'una hazırlamak;
- data-class/tool/attachment/retention inventory çıkarmak;
- gerçek veri içermeyen threat-model ve eval fixture tasarlamak;
- capability, preview/approval ve durable security-event sözleşme seçeneklerini
  karar üretmeden analiz etmek;
- acceptance mapping ve kanıt planını review etmek;
- yalnız docs-only current-main reconciliation yapmak.

### Şimdi yasak

- provider/model/package/SDK/endpoint/secret/network egress seçmek veya eklemek;
- gateway endpoint, tool handler, worker, prompt, retrieval/vector store kodlamak;
- AI permission/rol/approval/reauth threshold uydurmak;
- DB migration, provider state veya command-execution write eklemek;
- gerçek ERP verisini dış servise göndermek;
- strict schema'yı business authorization/validation yerine kullanmak;
- ERP-000F/G/I/K/L veya domain owner davranışını J içinde sahiplenmek;
- bütün direct dependencies ve üç blocker kapanmadan implementation başlatmak;
- human security review, exact-head CI ve production activation kapısını atlamak.

## 25. Korunan paralel kapılar

- PR #2'nin yedi blocker'ı bu artifact ile çözülmez.
- ERP-000F ve ERP-000M implementation kapıları açılmaz.
- ERP-000L readiness'in merge'i observability/security-event implementation kabulü
  değildir; L `blocked` kalır.
- Identity Issue #62/#63 **DRAFT / NO-GO** ve production activation kapısıdır.
- Cari PR #77/#79/#65 ve `parallel/chatgpt/*` / `hotfix/chatgpt/*` sahiplikleri read-only
  kalır; bu branch Cari docs/code/decision üretmez.
- K shell contract `OPEN/BLOCKED` kalır; J readiness K implementation'ını başlatmaz.
- Hiçbir provider, topoloji, threshold, retention, role veya production behavior bu
  docs-only PR ile kabul edilmiş sayılmaz.

## 26. Manifest ve merge güvenliği

Bu clean rebuild `main@d5bcd5b30ee79885cf64b3c4c638ddeb91426d20` tabanındadır.
Eski PR #14 head'i ve eski CI sonuçları yalnız tarihsel kanıttır; bu branch için yeniden
kullanılamaz.

Güvenli invariant:

1. Manifest root metadata ve J dışındaki on dört task objesi current-main ile exact
   parity taşır.
2. J yalnız `blocked`, direct dependencies A/B/C/D/E/L ve blocker'lar
   ADR-008/OPEN-020/ERP-000J-AI-CONTRACT olarak değişir.
3. ERP-000E=`done`; ERP-000M/O/N/L/K=`blocked` ve diğer task truth-state current-main ile
   aynen korunur.
4. K ve J hard manifest dependency değildir; K physical extension gate ilişkisi bu
   contract içinde açıkça tanımlanır.
5. Bu shared contract/security/manifest PR'ı Draft ve human review kapısında kalır;
   otomatik merge edilmez.

## 27. Sonuç

ERP-000J için sonuç **readiness ACCEPTABLE; contract OPEN; implementation ve production
NO-GO**dur. Doğru hedef, modele daha çok yetki vermek değil, kullanıcı ve ERP bağlamını
her adımda yeniden doğrulayan dar bir gateway kurmaktır.

AI Gateway ancak A/B/C/D/E/L kabul edilip `main`e alındıktan; fiziksel ADR-008,
OPEN-020 ve `ERP-000J-AI-CONTRACT` birlikte kapandıktan; server capability, permission,
approval/reauth, durable security event, retention, attachment, K-shell extension ve
gerçek SQL/eval/red-team kanıtları geçtikten sonra kodlanabilir. O zamana kadar provider,
model, package, SDK, prompt, endpoint, threshold veya production davranışı tahmin
edilmeyecek; model ERP authority veya business truth sayılmayacaktır.
