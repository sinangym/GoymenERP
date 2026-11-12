# FEATURE — F023 Reddedilen işlem denemelerinin denetim kaydı

- Sürüm: v1 (taslak, yalnız spec; kod yok)
- Tarih: 2026-11-12
- Yazan: Claude şeridi · Backend sahibi: Codex şeridi (Audit / platform) · Güvenlik kanalı sahibi: AI Merkezi şeridi
- Bulgu kaynağı: `docs/delivery/FABLE_5_2_ANALIZLERI/01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md` F-023
  ("Merkezi denetim kaydı yalnız başarılı işlemleri alıyor")
- Bağlayıcı karar: Sinan, Issue #66 yorum `6819201876` madde 2 (2026-11-12) — aşağıda §Karar.
- İlgili: `docs/adr/ADR-009-audit-and-correlation.md`, `docs/adr/ADR-SEC-001-durable-security-event-channel.md`,
  `docs/adr/ADR-011-idempotency-and-concurrency.md`, `docs/features/PLATFORM_HTTP_ERROR_CONTRACT_v1.md`,
  `docs/decisions/SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md` (BS-11, BS-15), `docs/delivery/AI_KARAR_PAKETI_2026-11.md` (Grup E),
  `.claude/skills/k3-command-skeleton/SKILL.md`, açık PR #1093 (`claude/ai2a-guvenlik-olay-kanali`, AI-2a).

## Amaç

Bir kullanıcının (veya sistemin/AI'nin) denediği ama sistemin **reddettiği** her işlem, sonradan "kim, ne zaman, hangi
işlemi denedi, neden reddedildi" sorusuna cevap verecek şekilde kalıcı ve değiştirilemez olarak kaydedilir. Bugün
merkezi denetim kaydı yalnız başarılı/no-change iş etkisini alır; retler ya hiç yazılmaz ya da yalnız modülün kendi
idempotency deneme tablosunda (replay için) durur ve denetim ekranında görünmez.

### Bugünkü durum (kod okuması, `origin/main`)

| Ret türü | Nerede doğar | Bugün nereye yazılıyor |
|---|---|---|
| 401 kimlik yok / 403 izin yok (endpoint policy) | `src/backend/GoymenERP.Api/Errors/ErpAuthorizationMiddlewareResultHandler.cs` | **Hiçbir yere** (yalnız problem cevabı) |
| `company_context_invalid` | `src/backend/GoymenERP.Api/Multitenancy/CompanyContextMiddleware.cs` → `ErpRequestMetadataProblem.WriteCompanyContextInvalidAsync` | **Hiçbir yere** |
| Servis içi 401/403 (`RequireAsync` / `RequireStillAsync` çiti) | ör. `PurchasingService.cs`, `SalesDraftService.cs` | **Hiçbir yere** |
| 503 `permission_unregistered` | servis `RequireAsync` | Hiçbir yere (kurulum açığı, kullanıcı reddi değil) |
| 400 biçim / header / strict body | endpoint okuyucuları (`GoymenERP.Api/Endpoints/*Endpoints.cs`) | **Hiçbir yere** |
| 422 `validation_failed` (düzeltilip tekrar denenebilir) | servis/domain | **Hiçbir yere** (skill: "422 (not recorded)") |
| 409 terminal ret (`version_conflict`, domain durum kodları) | servis `TerminalAsync` → `store.RecordRefusalAsync` | Yalnız modül deneme tablosu: `purchasing.PurchaseAttempts`, `sales.DraftAttempts/QuoteAttempts/RequestAttempts/InvoiceAttempts/…`, Logistics, Product fiyat listesi (`Outcome = AttemptOutcomeRefused`, `RefusalJson`); Settings/Counterparty/Organization `Idempotency` modülünün `CommandExecution` satırında `Outcome`/`HttpStatusCode` ile |
| 409 `same_key_payload_mismatch`, `concurrent_change` | store | **Hiçbir yere** (kazananın cevabı döner) |
| AI retleri | AI gateway (henüz runtime yok) | ADR-SEC-001: `ai.request.refused`, `ai.authz.denied` güvenlik olayı kanalına (PR #1093) |

Teknik engel: `Modules/Audit/GoymenERP.Modules.Audit.Application/Contracts/AuditModels.cs` içinde `AuditOutcomes`
yalnız `Succeeded` taşır ve `Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Services/AuditEventFactory.cs`
`ValidateAllowed(outcome, …, AuditOutcomes.Succeeded)` ile başka sonucu reddeder. Ayrıca `audit.AuditEvents`
satırı `CompanyId` boş olamaz (`CK_AuditEvents_CompanyId_NotEmpty`) ve `BeforeJson/AfterJson/Reason/EntityId`
zorunludur (`AuditEvent.cs` `RequireText`).

## Kapsam

1. Ortak "reddedilen deneme" sözleşmesi (`RefusedAttemptV1`) ve tek yazma yolu; zarfta yalnız izinli alanlar.
2. İş kuralı ve teknik retlerin (400, 409, 422, idempotency çakışması) **denetim kaydına** (`audit.AuditEvents`,
   `Outcome = Refused`) yazılması; terminal retlerde deneme satırıyla **aynı `SaveChanges`** içinde.
3. Yetki/kimlik/şirket bağlamı retlerinin (401, 403, `company_context_invalid`, çit kaybı) **güvenlik olayı
   kanalına** (`authz.denied`) tek kez yazılması ve denetim görünümünden okunabilir olması (bkz. §Sınır).
4. Hacim kontrolü: BS-15 desenindeki toplulaştırma; pencere/eşik ayar, değer girilene kadar kapalı.
5. Denetim ekranında retlerin filtrelenip okunması (mevcut `src/frontend/goymen-erp-web/src/audit/AuditLogPage.tsx`).

## Kapsam dışı

- Saklama süresi ve imha: **seçilmez** (karar metni: "Saklama süresi ayrı politikadır"). Bkz. `NEEDS-SINAN-F023-1`.
- İsteklerin hız sınırlaması (rate limiting / throttling). BS-15'te hız sınırı **seçilmedi**; bu spec yalnız kaydın
  hacmini yönetir, isteği engellemez.
- Başarılı girişler, izin değişikliği, dışa aktarım vb. güvenlik olayları (ADR-SEC-001'in kendi kapsamı).
- Başarılı audit satırlarındaki mevcut IP/User-Agent alanının değiştirilmesi (bkz. §Açık gözlem).
- Okuma (GET) retleri: v1 yalnız **işlem** (komut, yazma) denemelerini kapsar. GET 403'leri güvenlik kanalında
  `authz.denied` olarak zaten yer alır (F023-2); denetim kaydına ayrıca yazılmaz.
- Grant, rol ataması, yeni izin adının kesinleşmesi (insan kapısı).

## Karar ve açık politika ID'leri

**Karar (Sinan, #66 `6819201876` madde 2, 2026-11-12):** "Yetki retleri ve iş kuralı retleri dahil reddedilen tüm
işlem denemeleri denetim kaydına yazılır (kim, ne zaman, hangi işlem, ret nedeni). Kişisel veri/şifre/secret
yazılmaz. Saklama süresi ayrı politikadır." Bu karar henüz `docs/decisions/` altında tarihli dosyaya işlenmemiştir;
işlenmesi `decision-docs-validation` akışıyla ayrı PR'dır (bu spec onu değiştirmez).

Dayanılan kararlar: ADR-009 (append-only, aynı transaction), ADR-SEC-001 + BS-11 (Grup E kabul: E1 yazma hatası
davranışı, E5 IP/UA saklanmaz, E9), BS-15 (toplulaştırma, pencere/eşik Ayarlar'dan, ilk değer ölçümden sonra),
AGENTS.md "Fiziksel/ticari/finansal olay bir kez kaydedilir".

Açık politika: `OPEN-018` ve `OPEN-021` (tür bazlı saklama/imha matrisi, legal hold), `OPEN-015` (hacim; eşik
ölçümü), `OPEN-013` (production kimlik sağlayıcısı; anonim/oturum retleri).

NEEDS-SINAN (Issue #66'ya tek satır olarak yazılacak; cevap gelene kadar ilgili dilim başlamaz veya varsayılan
davranış "kapalı/yazılmaz"dır):

| ID | Soru | Öneri | Cevapsızken |
|---|---|---|---|
| `NEEDS-SINAN-F023-1` | Reddedilen deneme kayıtları ne kadar saklansın? | Öneri yok (karar gereği ayrı politika). Ayar anahtarı hazır: `Audit:RefusedAttempts:Retention:PolicyRef` | Ayar boş; imha yolu yok (v1'de zaten yok) → kayıt silinmez |
| `NEEDS-SINAN-F023-2` | "Denetim kaydı" yetki retleri için güvenlik olayı kanalını da kapsar mı (tek kayıt güvenlik kanalında, denetim ekranı oradan okur)? | **Evet** (§Sınır, seçenek A) | F023-2 yalnız güvenlik kanalına yazar; denetim ekranındaki görünüm (F023-5b) açılmaz |
| `NEEDS-SINAN-F023-3` | Denetim ekranında yetki retlerini kim görsün: `identity.audit.read` sahibi mi, yoksa yalnız güvenlik olayı okuma izni (aday `platform.security-events.read`) sahibi mi? | Yalnız güvenlik olayı okuma izni (E2 ile tutarlı) | Görünmez |
| `NEEDS-SINAN-F023-4` | BS-15 toplulaştırması denetim kaydındaki tekrar eden 400/422 retlerine de uygulansın mı? | Evet, aynı desen; değerler ölçümden sonra | Kapalı: her ret ayrı satır |
| `NEEDS-SINAN-F023-5` | Aynı idempotency anahtarıyla tekrarlanan (replay) bir ret yeni denetim kaydı mı? | Hayır; ilk ret kanıttır, replay yalnız metrik | Yazılmaz |
| `NEEDS-SINAN-F023-6` | Ayrı kayıt (400/422) yazılamazsa: ret geçerli kalır + alarm metriği (E1'deki ret türleriyle aynı) uygun mu? | Evet | Ret geçerli kalır + metrik (ret asla başarıya dönmez) |

## Kullanıcı rolleri ve CompanyId kapsamı

- Kaydı **yazan** rol yoktur; yazma yalnız sunucu içindir. Public mutation endpoint'i yoktur (ADR-009 §6).
- **Okuma:** iş kuralı retleri mevcut `/api/v1/audit/events` ile, aktif şirket için Company kapsamlı
  `identity.audit.read` izniyle (ADR-009 §7). Yetki retleri `NEEDS-SINAN-F023-3`'e bağlı. Rol adı kullanılmaz.
- `CompanyId` her zaman **sunucu çözümlü** değerdir (`ICompanyContext`), istemci header'ından alınmaz.
  Şirket çözülemeden doğan ret (401, `company_context_invalid`) denetim tablosuna giremez (CHECK); bu retler güvenlik
  kanalında `ScopeKind = Platform`, `CompanyId = null` olarak durur; istenen şirket yalnız kimliği doğrulanmış aktör için
  `RequestedCompanyId` alanına yazılır (ADR-SEC-001 §3). Anonim istekte istenen şirket de yazılmaz.
- A şirketinin ret kaydı B şirketi okumasında görünmez (global EF filtresi + query service savunması).

## Domain nesneleri ve sahip modül

| Nesne | Sahip | Not |
|---|---|---|
| `RefusedAttemptV1` (yeni, BuildingBlocks sözleşmesi) | Platform / Audit | Modülün doldurduğu kısıtlı taslak; serbest metin alanı yok |
| `AuditEventDraft` + `AuditOutcomes.Refused` (yeni sabit) | Audit | `AuditEventFactory` kabul listesi genişler |
| `IRefusedAttemptRecorderV1` (yeni port) | Audit | Modülün kendi DbContext'ine bağlı uygulama (ADR-009 deseni) |
| Modül deneme satırı (`PurchaseAttemptRow`, Sales `*Attempts`, `CommandExecution` …) | Her modül | Değişmez; replay kaynağı olmaya devam eder |
| `SecurityEventV1` (`authz.denied`) | SecurityEvents (PR #1093) | Yetki retlerinin tek kaynağı |

`RefusedAttemptV1` alanları (izinli liste — başka alan eklemek sözleşme değişikliğidir):

| Alan | Kaynak | Audit kolonuna eşleme |
|---|---|---|
| `CompanyId` | `ICompanyContext` (çözülmüş) | `CompanyId` |
| `CommandName` (ör. `purchasing.purchase-request.approve.v1`) | modülün `<M>Commands` sabiti | `EventType` |
| `Operation` | modül sabiti | `Operation` |
| `TargetType`, `TargetId` | route'taki hedef kimliği; yoksa sabit `"-"` | `EntityType`, `EntityId` |
| `ExpectedVersion` | istekteki sürüm (parse edilebildiyse) | `EntityVersionBefore`; `EntityVersionAfter = null` |
| `ActorType`, `ActorId` | iç kullanıcı kimliği (envelope) | `ActorType`, `ActorId` |
| `PermissionCode`, `RoleCodes` | çit kapsamı | aynı |
| `HttpStatus`, `ProblemCode`, `FieldErrors[{field, code}]` | problem cevabı | `AfterJson` (yalnız bu anahtarlar) |
| `RefusalClass` (`Validation` / `BusinessRule` / `Concurrency` / `Idempotency` / `RequestShape`) | kapalı liste | `AfterJson` |
| `Reason` | sabit Türkçe metin, resource dosyasından (ret sınıfı başına) | `Reason` |
| `CorrelationId`, `CausationId`, `SourceChannel` | `ICorrelationContext` | aynı |
| `IdempotencyKey` | header (varsa) | yalnız SHA-256 özeti (mevcut factory davranışı) |
| `AiInvolvement`, `AiContextJson` | AI gateway güvenilir bağlamı (`AiAuditContextV1`) | aynı; AI yoksa `None`/null |

`BeforeJson = "{}"`. `ClientIpAddress = null`, `UserAgent = null` (karar: kişisel veri yazılmaz; E5 ile aynı).

**Yasak alanlar** (sözleşmede tip olarak yoktur; mimari test ve unit test ile korunur): ham istek gövdesi, alan
**değerleri** (yalnız alan adı + hata kodu), şifre, token, `Authorization`/cookie header'ı, ham idempotency anahtarı,
IBAN/VKN/TCKN/telefon/e-posta, tutar/fiyat, kullanıcı adı, IP, User-Agent, serbest metin gerekçe veya exception
mesajı. `ProblemDetails.detail` metni de yazılmaz (yalnız `code`). Alan adı listesi, modülün OpenAPI'deki sabit alan
adlarıdır; serbest metin taşımaz. AI dış çıkış filtresindeki sınıflama (`BuildingBlocks/Ai/AiExternalEgressV1.cs`,
`AiEgressRulesV1`, `NeverEgress`) burada **kullanılmaz**: bu kanal değer taşımadığı için maskeleme yerine "değer yok"
kuralı uygulanır; filtre yalnız ileride değer taşıyan bir alan istenirse referans alınır (yeni karar gerekir).

## Durum geçişleri

Kayıt değişmezdir (append-only); durum geçişi yoktur. Ret türü → kayıt yeri:

| Ret | Kayıt yeri | Aynı transaction | Toplulaştırma |
|---|---|---|---|
| 409 terminal (`RecordRefusalAsync` yolu) | audit `Refused` | **Evet**, deneme satırıyla aynı `SaveChanges` | **Hayır** (deneme satırıyla 1:1) |
| 409 `same_key_payload_mismatch` / `concurrent_change` (kaybeden) | audit `Refused`, sınıf `Idempotency`/`Concurrency` | Ayrı kısa yazma | Uygun (F023-4) |
| 422 `validation_failed` | audit `Refused`, sınıf `Validation` | Ayrı kısa yazma | Uygun |
| 400 biçim/header/strict body (kimlik ve şirket çözülmüşse) | audit `Refused`, sınıf `RequestShape` | Ayrı kısa yazma (endpoint filtresi) | Uygun |
| 401, 403, `company_context_invalid`, çit kaybı | güvenlik kanalı `authz.denied` | — | ADR-SEC-001/BS-15 |
| 503 `permission_unregistered` | yalnız log/metrik | — | — |
| Replay edilen ret | yazılmaz (`NEEDS-SINAN-F023-5`) | — | — |

## Birim/formül/yuvarlama/tolerans

Para/miktar/formül yoktur. Toplulaştırma penceresi (`TimeSpan`) ve eşiği (`int`) Ayarlar'dandır; kodda sabit yoktur.

## Yetki/onay/görev ayrılığı

- Yazma yetki istemez (sunucu içi); okuma için mevcut `identity.audit.read`. Yeni izin adı **önerilmez**; yetki retleri
  görünümü için ADR-SEC-001'deki aday ad (`platform.security-events.read`) kullanılır, kesinleşmesi ve grant insan
  kapısıdır (`NEEDS-SINAN-F023-3`).
- Ret kaydının varlığı kullanıcıya ek bilgi sızdırmaz: problem cevabı değişmez (401/403/404/503 kodsuz kalır).
- Kendi retlerini görme (self-service) v1'de yoktur.

## Audit, idempotency ve ters işlem

### Sınır: denetim kaydı ↔ güvenlik olayı kanalı (tek kayıt kuralı)

ERP-000L §6 üç kanalı ayırır; aynı olgu iki kanala ayrı satır olarak yazılmaz.

- **Denetim kaydı (iş kanıtı):** şirket çözülmüş, kimliği doğrulanmış, izni geçmiş bir aktörün **iş işlemini**
  denediği ve iş/teknik kural nedeniyle reddedildiği olgu. Sahip modül yazar; `CompanyId` zorunlu.
- **Güvenlik olayı kanalı (güvenlik telemetrisi, kalıcı):** kimlik/yetki/şirket sınırı reddi — çit **geçilemedi**.
  Aktör anonim olabilir, şirket çözülmemiş olabilir; toplulaştırma (E7/BS-15) burada.
- **Seçenek A (öneri):** yetki retlerinin tek kaynağı `authz.denied` güvenlik olayıdır; denetim ekranı bunları
  güvenlik kanalının okuma portundan (SecurityEvents modülü sahibi, okuma yetkisiyle) **referansla** gösterir; denetim
  tablosuna kopya yazılmaz. Bağ `CorrelationId` ile kurulur. Kullanıcı kararındaki "denetim kaydına yazılır" ifadesinin
  bu şekilde karşılandığı `NEEDS-SINAN-F023-2` ile teyit edilir.
- **Seçenek B (reddedilen):** her yetki reddini iki depoya yazmak — aynı olgu iki kez kaydedilir (AGENTS.md ihlali),
  anonim/şirketsiz retler zaten audit CHECK'ine takılır.
- Ayrım kuralı tektir: **ret, permission çitinden önce/çitte mi doğdu?** Evet → güvenlik kanalı; hayır → denetim.
  Bir istek yalnız bir ret üretir (ilk ret cevabı döner), dolayısıyla çift kayıt oluşmaz. Mimari test: aynı
  `CorrelationId` için hem `audit.AuditEvents(Outcome=Refused)` hem `security.SecurityEvents(EventType=authz.denied)`
  satırı oluşmaz (SQL kabul testi).

### Atomiklik ve yazma hatası

- Terminal ret: `RecordRefusalAsync` deneme satırını eklerken aynı DbContext'e `IAuditEventCollector.Enqueue` ile
  `Refused` taslağı bırakır; `AuditSaveChangesInterceptor` ikisini aynı SQL transaction'ında yazar. Biri başarısızsa
  ikisi de yoktur; mevcut davranış (store hatası → 503 `service_unavailable`) değişmez.
- Ayrı kısa yazma (400/422/çakışma): ret cevabı **her durumda** döner; kayıt yazılamazsa düşük kardinaliteli
  `audit_refused_attempt_write_failed{refusal_class}` metriği artar (`NEEDS-SINAN-F023-6`). Ret hiçbir durumda başarıya
  dönüşmez; yazma hatası cevap süresini bloklamaz (kısa zaman aşımı ayar).
- Değişmezlik: mevcut `[audit].[TR_AuditEvents_RejectUpdateDelete]` trigger'ı ve `AuditEventIntegrity` hash'i aynen
  geçerlidir; `Outcome` hash zarfının parçasıdır.
- Idempotency: ret kaydı idempotency anahtarı **tüketmez**; terminal retlerin replay davranışı modül deneme
  tablosunda kalır. Ham anahtar yazılmaz.
- Ters işlem: yoktur (kayıt iş etkisi değildir).

### Hacim kontrolü (F023-4)

- Yalnız "ayrı kısa yazma" retleri toplulaştırılır. Anahtar: `(CompanyId, ActorId, CommandName, ProblemCode)`.
- Ayar bölümü `Audit:RefusedAttempts:Aggregation` — `Window` (TimeSpan) ve `Threshold` (int).
  `SecurityEventAggregationOptions` (PR #1093) ile aynı doğrulama: ikisi birlikte boş → **kapalı**, her ret ayrı satır;
  yalnız biri dolu → uygulama başlamaz (fail-closed); pozitif olmayan değer reddedilir. Varsayılan `appsettings.json`
  değeri **boştur**; değer Sinan ölçüm sonrası girer (`NEEDS-SINAN-F023-4`, `OPEN-015`).
- Açıkken: pencere içinde eşiğe kadar ret ayrı satır; eşikten sonrası sayılır ve pencere sonunda **tek özet satırı**
  (`AfterJson`: `count`, `windowStartUtc`, `windowEndUtc`, `refusalClass`, `problemCode`) eklenir. Özet de append-only
  insert'tür; mevcut satır güncellenmez. Sayaç süreç içi tutulur; süreç düşerse kaybolan sayı
  `audit_refused_attempt_aggregation_lost` metriğine yazılır (kalıcılık garantisi değil, hacim koruması).
- Terminal retler, yetki retleri (güvenlik kanalının kendi toplulaştırması) ve AI retleri bu ayarın dışındadır.

### Saklama

Seçilmez. `Audit:RefusedAttempts:Retention:PolicyRef` ayar anahtarı tanımlanır, boş kalır; v1'de imha yolu yoktur
(ADR-009 trigger'ı) ve OPEN-018/021 ADR'si gelene kadar kayıt silinmez. Kod sabiti süre yoktur (`NEEDS-SINAN-F023-1`).

### Açık gözlem (karar değil)

Başarılı audit satırları bugün `ClientIpAddress`/`UserAgent` saklıyor (`AuditEventDraft`); güvenlik kanalında E5 ile
saklanmıyor, ret kayıtlarında da saklanmayacak. Başarılı satırlardaki davranışın E5 ile hizalanıp hizalanmayacağı bu
spec'in kapsamı dışındadır; ayrıca sorulabilir.

## API/event/AI tool sözleşmesi

- **İç sözleşme (BuildingBlocks/Audit Application):** `RefusedAttemptV1` (sürümlü record), `RefusedAttemptClassesV1`
  (kapalı liste), `IRefusedAttemptRecorderV1.Record(RefusedAttemptV1)` (aynı transaction: collector'a bırakır) ve
  `RecordStandaloneAsync(RefusedAttemptV1, ct)` (kısa yazma; sonuç nesnesi döner, istisna fırlatmaz — `ISecurityEventWriterV1`
  sonuç deseniyle aynı). Modüller audit tablosuna port dışında yazmaz (ADR-009 §1).
- **Güvenlik kanalı:** `ISecurityEventWriterV1.WriteAsync(SecurityEventV1 { EventType = authz.denied, Outcome = Denied,
  ReasonCode ∈ {permission_missing, company_context_mismatch, session_invalid}, ActorType = Anonymous | User })`.
  Yeni olay türü **eklenmez**.
- **HTTP okuma:** `/api/v1/audit/events`'e isteğe bağlı `outcome` filtresi (`Succeeded` | `Refused`) eklenir;
  `IAuditQueryService.GetEventsAsync` imzası genişler, cursor/keyset sözleşmesi korunur. OpenAPI yeniden üretilir
  (`openapi-regenerate`), sözleşme testi eklenir. Ret cevaplarının HTTP sözleşmesi değişmez.
- **Outbox/event:** ret kaydı dış sisteme yayımlanmaz.
- **AI:** AI kaynaklı istek retleri ve AI yetki retleri güvenlik kanalında kalır (`ai.request.refused`,
  `ai.authz.denied`). AI'nin çağırdığı backend aracı iş kuralıyla reddedilirse denetim kaydı `AiInvolvement` ve
  `AiContextJson` (`AiAuditContextV1`) ile yazılır. AI gateway kapalıyken kayıt yolu aynen çalışır (AI'ye bağımlılık yok).

## UI durumları ve erişilebilirlik

Mevcut `src/frontend/goymen-erp-web/src/audit/AuditLogPage.tsx` genişler (Claude şeridi, F023-5):

- "Sonuç" filtresi: Tümü / Başarılı / Reddedilen. Ret satırında: zaman, kişi (görünen ad okuma portundan, yoksa
  kimlik), işlem, hedef, ret nedeni (problem kodunun Türkçe karşılığı, resource dosyasından), korelasyon kimliği.
  Özet satırı "N kez reddedildi (pencere: …)" olarak gösterilir.
- Durumlar (`frontend-screen-states`): loading, empty-no-data, empty-filtered, unauthenticated, forbidden, network-error,
  service-unavailable, malformed (strict decoder: bilinmeyen `outcome`/`refusalClass` → fail-closed), stale.
- Yetki retleri bölümü yalnız `NEEDS-SINAN-F023-2/3` cevabından sonra ve okuma izni varsa görünür; izin yoksa bölüm
  hiç render edilmez (boş liste gibi gösterilmez).
- Erişilebilirlik: ret satırı yalnız renkle değil metin etiketiyle ("Reddedildi") ayrılır; tablo başlıkları
  `scope`'lu; filtre klavyeyle kullanılabilir.

## Migration/rollback

- `audit.AuditEvents.Outcome` için CHECK constraint yoktur (`AuditEventModelBuilderExtensions.cs` yalnız uzunluk);
  `Refused` değeri **şema değişikliği olmadan** yazılabilir. Yeni kolon yok (özet sayaçları `AfterJson`'da).
- Okuma filtresi için indeks önerisi: `(CompanyId, Outcome, OccurredAtUtc, Id)` — Audit migration'ı (K3,
  `ef-migration-forward-fix`). Down: indeks bırakılır (veri kaybı yok). Veri oluştuktan sonra audit tablosu düşürülmez
  (ADR-009).
- İsteğe bağlı CHECK (`Outcome IN ('Succeeded','Refused')`) ayrı forward-fix olarak değerlendirilir.
- Geri alma: uygulama eski sürüme dönerse eski `AuditEventFactory` `Refused` yazmaz; mevcut `Refused` satırları okumada
  sorun çıkarmaz (eski okuma `Outcome`'u metin olarak döndürür). Güvenlik kanalı migration'ı PR #1093'tedir.
- Recovery envanteri, architecture guard ve relational inventory yeniden pinlenir (`recovery-inventory-repin`,
  `architecture-guard-repin`).

## Kabul senaryoları

1. **Terminal ret kaydı:** *Given* satın alma talebi v3'te (Submitted), kullanıcı `purchasing.requests.approve` iznine sahip;
   *When* `expectedVersion = 2` ile gönderir; *Then* 409 `version_conflict` döner, aynı transaction'da bir
   `purchasing.PurchaseAttempts` (Refused) satırı ve bir `audit.AuditEvents` (`Outcome = Refused`,
   `EventType = purchasing.purchase-request.approve.v1`, `AfterJson.problemCode = version_conflict`) satırı oluşur.
2. **Atomiklik:** *Given* audit insert'i SQL tarafında hata verecek şekilde ayarlanmış; *When* aynı terminal ret
   denenir; *Then* ne deneme satırı ne audit satırı kalır; cevap 503'tür.
3. **Kişisel veri yok:** *Given* 422 dönen bir istek gövdesinde IBAN, telefon ve serbest açıklama var; *When* ret
   kaydedilir; *Then* kayıtta bu değerlerin hiçbiri, ham gövde, `Authorization` header'ı, ham idempotency anahtarı,
   IP ve User-Agent yoktur; yalnız alan adları ve hata kodları vardır (unit + SQL testi tüm kolonları tarar).
4. **Şirket izolasyonu:** *Given* A şirketinde 3 ret kaydı var; *When* B şirketi bağlamıyla `identity.audit.read`
   sahibi kullanıcı `outcome=Refused` okur; *Then* hiçbir A satırı dönmez.
5. **Kimliksiz 401:** *Given* `Authorization` header'ı olmayan istek; *When* bir komut endpoint'ine gelir; *Then* 401
   kodsuz döner; denetim tablosuna satır yazılmaz; güvenlik kanalına `authz.denied`, `ActorType = Anonymous`,
   `ActorId = null`, `CompanyId = null`, `ScopeKind = Platform`, `ReasonCode = session_invalid` bir satır yazılır;
   istenen şirket yazılmaz.
6. **403 tek kayıt:** *Given* oturumlu kullanıcıda izin yok; *When* komut denenir; *Then* güvenlik kanalında tek
   `authz.denied` (`permission_missing`, `PermissionCode` dolu) vardır ve aynı `CorrelationId` ile `audit.AuditEvents`
   satırı **yoktur**.
7. **Şirket sınırı:** *Given* kullanıcı yalnız A şirketine erişebilir; *When* B şirketi başlığıyla komut dener;
   *Then* güvenlik kanalında `company_context_mismatch`, `CompanyId = null`, `RequestedCompanyId = B` yazılır.
8. **Sel, toplulaştırma kapalı:** *Given* `Audit:RefusedAttempts:Aggregation` boş; *When* aynı kullanıcı aynı 422'yi
   50 kez üretir; *Then* 50 ayrı satır yazılır ve her cevap 422'dir.
9. **Sel, toplulaştırma açık:** *Given* test ayarında `Window` ve `Threshold` dolu; *When* aynı anahtarla eşiğin üstünde
   ret üretilir; *Then* eşik kadar tekil satır ve pencere sonunda `count` taşıyan tek özet satırı yazılır; mevcut
   satır güncellenmez.
10. **Yarım ayar:** *Given* yalnız `Window` dolu; *Then* uygulama başlangıçta Türkçe hata ile durur.
11. **Yazma hatası:** *Given* audit deposu ayrı kısa yazmada erişilemez; *When* 422 üretilir; *Then* cevap yine
    422'dir, `audit_refused_attempt_write_failed` metriği artar, istek başarıya dönmez.
12. **AI kapalı:** *Given* AI gateway devre dışı ve AI ayarı yok; *When* kullanıcı terminal ret üretir; *Then* kayıt
    `AiInvolvement = None`, `AiContextJson = null` ile yazılır, akış AI'ye hiç dokunmaz.
13. **Replay:** *Given* bir terminal ret kayıtlı; *When* aynı anahtar ve gövdeyle tekrar gönderilir; *Then* aynı ret
    `Idempotency-Replayed: true` ile döner ve yeni audit satırı yazılmaz (`NEEDS-SINAN-F023-5` varsayılanı).
14. **Değişmezlik:** *When* uygulama hesabıyla `UPDATE`/`DELETE` denenir; *Then* trigger `51000` ile reddeder;
    integrity hash kalıcı satırdan yeniden hesaplanır.
15. **Ekran:** *Given* okuma izni olan kullanıcı; *When* "Reddedilen" filtresini seçer; *Then* yalnız ret satırları,
    Türkçe ret nedeniyle listelenir; izin yoksa ekran `forbidden` durumunu gösterir.

## Definition of Done

- Karar `docs/decisions/` altına işlenmiş ve NEEDS-SINAN satırları #66'ya yazılmış; cevapsız maddeler varsayılan
  "kapalı/yazılmaz" davranışında.
- `RefusedAttemptV1` için yasak alan unit testi, mimari test (audit tablosuna port dışı yazma yok; aynı
  `CorrelationId` için çift kanal yok), gerçek SQL kabul testleri (atomiklik, izolasyon, trigger, hash).
- Her modül dilimi: servis testi (terminal ret → audit satırı), HTTP testi (cevap sözleşmesi değişmedi).
- OpenAPI ve sözleşme testleri güncel; recovery/guard/relational envanterleri yeniden pinli.
- Log/metric: yazma hatası ve toplulaştırma kaybı metrikleri; kişisel veri loglanmaz.
- UI: tüm durumlar Vitest ile; Türkçe metinler resource dosyasında.
- Açık P0/P1 yok; K3 dilimlerde iki bağımsız exact-head review.

## Dilim planı

| Dilim | İçerik | Sahip | Sınıf | Bağımlılık |
|---|---|---|---|---|
| F023-0 | Kararın `docs/decisions/`'a işlenmesi, NEEDS-SINAN-F023-1…6'nın #66'ya yazılması | ChatGPT/koordinatör | governance | — |
| F023-1 | `RefusedAttemptV1`, `RefusedAttemptClassesV1`, `AuditOutcomes.Refused`, `AuditEventFactory` kabul listesi, `IRefusedAttemptRecorderV1` (aynı transaction + kısa yazma), yasak alan ve mimari testler, Retention ayar anahtarı (boş) | Codex (Audit) | **K3** (cross-module contract) | — |
| F023-2 | Yetki retleri: `ErpAuthorizationMiddlewareResultHandler`, `CompanyContextMiddleware`, servis `RequireAsync`/`RequireStillAsync` çiti → `authz.denied` (tek ortak yardımcı) | AI Merkezi + Codex | **K3** (permission/cross-module) | PR #1093 merge; `NEEDS-SINAN-F023-2` |
| F023-3 | İş kuralı retleri, modül başına ayrı PR; her modülün sahip şeridi kendi modülünü yapar: Purchasing, Sales, Logistics, Product (fiyat listesi), Counterparty, Organization, Settings (`CommandExecution` yolu), Finance, Inventory, Accounting, Manufacturing, Documents, Work. Terminal retlerde `RecordRefusalAsync`'e Enqueue; 422/400/çakışma kısa yazma | Modül sahibi şerit | K2 (kalıp uygulaması; yeni migration/izin yoksa). Para/stok komutunda K3 kapsamına giren değişiklik varsa K3 | F023-1 |
| F023-4 | Toplulaştırma (`Audit:RefusedAttempts:Aggregation`, boş = kapalı), özet satırı, metrikler | Codex (Audit) | K2 | F023-1, `NEEDS-SINAN-F023-4` (değer girilmeden açılmaz) |
| F023-5a | `/api/v1/audit/events` `outcome` filtresi + indeks migration'ı + OpenAPI | Codex (Audit) | **K3** (migration) | F023-1 |
| F023-5b | Denetim ekranı "Reddedilen" filtresi; yetki retleri bölümü (izinli ise) | Claude | K2 | F023-5a; bölüm için `NEEDS-SINAN-F023-2/3` |

Her dilim AGENTS.md PR kabul kapısına uyar; K3 dilimlerde iki bağımsız exact-head review ve gerçek SQL kanıtı gerekir.
Production aktivasyonu, grant ve saklama politikası insan kapısıdır.
