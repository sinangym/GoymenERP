# Göymen AI Merkezi — Kodlama Tasarımı v1.1

**Belge kimliği:** `GOYMEN_AI_MERKEZI_KODLAMA_TASARIMI_V1`
**Yayın:** 28 Eylül 2026 (v1.0); **revizyon:** 28 Eylül 2026 (v1.1 — kapanış kontrolü bulguları; değişiklik günlüğü §24)
**Belge türü:** `GOYMEN_AI_MERKEZI_MASTER_RAPOR_V1.md` (v1.3) ile tanımlanan Göymen AI Merkezi'nin **nasıl kodlanacağını** — çözüm yapısı, proje dosyaları, DI kaydı, veri modeli ve EF eşlemeleri, aggregate'ler ve durum makineleri, araç sözleşmesi, politika değerlendirici, bağlam kurucu, etki kuralı motoru, öneri/onay/gönderim/kurtarma, sağlayıcı portu ve router, ölçü referansı, bilgi katmanı, iş akışı motoru, endpoint'ler ve OpenAPI, capability provider, frontend, testler, CI, yapılandırma ve gözlemlenebilirlik — kod örnekleriyle açıklayan **kodlama tasarımı ve analiz belgesi**
**Durum:** **YALNIZ ANALİZ / TASARIM TASLAĞI.** Bu belge kod değildir; repoya kod, migration, izin, capability, görev veya karar eklemez; hiçbir geliştirme görevini kuyruğa almaz; Faz 0'ı başlatmaz; sağlayıcı/SDK/model seçmez; canlı veri aktarımı veya sağlayıcı aktivasyonu onayı değildir. `ERP-000J_AI_GATEWAY_CONTRACT_v1.md` §24 "Şimdi yasak" listesi (gateway endpoint, tool handler, worker, prompt, retrieval/vector store kodlamak; DB migration eklemek; provider/SDK seçmek) **aynen geçerlidir**; buradaki kod blokları uygulama anında yazılacak kodun **önerilen biçimidir**, repoya alınmış kod değildir.
**Doğrulama tabanı:** `sinangym/GoymenErp` `main@00a879816a7a9bb9a800712e59dc9ae5f52f08b0` (27 Eylül 2026); kod desenleri salt-okunur envanterle çıkarılmıştır (Ek K-1 kanıt tablosu)
**Kaynak önceliği:** `AGENTS.md` sırası — son tarihli `SINAN_KARARLARI_*.md` > `KARAR_SICILI.md` > Master Rapor v3 > feature spec/ADR (`ERP-000J`, `ERP-000K`, ADR-002/009/011/013) > `GOYMEN_AI_MERKEZI_MASTER_RAPOR_V1.md` v1.3 > bu belge. Bu belge master raporla çelişirse master rapor, master rapor kanonik kararla çelişirse kanonik karar geçerlidir.
**Adlandırma:** Belgedeki bütün proje, sınıf, tablo, permission, capability, endpoint, araç ve yapılandırma adları **ÖNERİ**dir; ADR-008 ve ilgili K3 review ile kilitlenmeden bağlayıcı değildir.

---

## İçindekiler

0. Statü, okuma kuralları ve "iş takibi ajanı" sorusunun kısa cevabı · 1. Kodlama özeti · 2. Çözüm yapısı, proje dosyaları ve bağımlılık kuralları · 3. Host kaydı (Program.cs, modül extension'ları, in-memory test factory) · 4. Veri modeli ve EF eşlemeleri (`work` ve `ai` şemaları, write guard, migration/rollback) · 5. Domain: aggregate'ler ve durum makineleri (AiProposal ve OperationIntentId, AiRun, Issue/Evaluation, WorkItem/ApprovalRequest, risk istisnası) · 6. Araç sözleşmesi ve kataloğu (`Ai.Contracts`, adaptörler) · 7. Politika değerlendirici (13 adım) · 8. Bağlam kurucu ve ortak veri kesiti · 9. Etki kuralı motoru (`Work.Rules`), kapı sorgusu, kural-tabanlı iş öğesi · 10. Öneri → onay → gönderim → kurtarma (kanıt matrisi dahil) · 11. Sağlayıcı portu, router ve API-birincil yol · 12. Ölçü referansı, çıktı doğrulayıcı, güvenli renderer · 13. Bilgi katmanı ve erişim · 14. İş akışı motoru (`Ai.Workflow`) ve **iş takibi ajanı** · 15. Endpoint'ler, problem kodları ve OpenAPI · 16. Capability provider ve registry pinleme · 17. Frontend (React shell, lane, api/contract, testler) · 18. Test stratejisi ve CI kapıları · 19. Yapılandırma, bayraklar, secret'lar · 20. Gözlemlenebilirlik · 21. Kodlama sırası ve PR dilimleri (kuyruğa alma değildir) · 22. Kapanış kontrol tablosu (üçüncü eleştiri) · 23. Açık noktalar · Ek K-1 Kod deseni kanıt tablosu · Ek K-2 Problem kodu ve HTTP durum sözlüğü · Ek K-3 Dosya listesi

---

## 0. Statü, okuma kuralları ve "iş takibi ajanı" sorusunun kısa cevabı

### 0.1 Bu belge neyi çözer

Master rapor v1.3 **ne** yapılacağını ve **neden** yapılacağını söyler; bu belge **nasıl kodlanacağını** söyler: hangi proje, hangi sınıf, hangi tablo, hangi endpoint, hangi test. İki belge birlikte okunur; bu belge master raporun bölüm numaralarına (§) atıf yapar.

### 0.2 Bu belge neyi yapmaz

- Repo, `KARAR_SICILI.md`, `task_manifest.json`, kod, permission veya capability değiştirmez; mevcut ERP kapanış çalışmasını (M → O → N → L → J kuyruğu, W-dilimleri) etkilemez.
- `Modules.Work` tasarımını hazırlar; bunu aktif geliştirme kuyruğuna **almaz** ("şimdilik analiz" kararı — master §11.1, NS-AI-01 Sinan'da).
- `OPEN-*` kapatmaz; sağlayıcı, model, SDK, paket, eşik, rol adı veya retention süresi seçmez.
- API-birincil tercihini yeniden karar sorusu yapmaz (master §5.10 uzlaştırması); bunu canlı veri aktarımı veya sağlayıcı aktivasyonu onayı saymaz.
- "Repoda zaten var" demez; her dayandığı garanti için dosya/satır kanıtı verir (Ek K-1, §10.6) ve kanıtsız olanı **doğrulanacak** olarak işaretler.

### 0.3 Durum etiketleri

| Etiket | Anlam |
|---|---|
| **KİLİTLİ-KAYNAK** | Kanonik kararla veya mevcut kodla belirlenmiş; bu belge yalnız uygular (kanıt satırı verilir). |
| **ÖNERİ** | Bu belgenin önerisi; ADR-008 / K3 review olmadan kod olmaz. |
| **DOĞRULANACAK** | Bu belgenin dayandığı fakat repoda kanıtını gösteremediği varsayım; uygulama öncesi kanıt şart. |
| **NEEDS-SINAN** | Yalnız ürün sahibinin vereceği işletme kararı (Issue #66 formatı). |

### 0.4 "Bu AI modülü iş takiplerini yapan ajan olarak da kullanılabilir mi?" — kısa cevap

**Evet; tasarım tam olarak bunu hedefler, ama "ajan" burada özerk yazıcı değil, kalıcı takipçidir.** Üç bileşen birlikte iş takibi ajanını oluşturur (ayrıntı §14):

1. **`Modules.Work` + `Work.Rules` (modelsiz, deterministik):** olay (`ERP-000F` outbox), zaman (tarama) ve form içi niyet (on-demand) tetikleyicileriyle etki kurallarını çalıştırır; her tespit bir **Sorun** (IssueKey) olarak yaşam döngüsü kazanır (`Open → Acknowledged → Resolved → Reopened`); sahibi, son tarihi, SLA'sı, eskalasyon zinciri ve kapanış kanıtı olan **iş öğeleri** açar; koşul kalkınca kapatır, geri dönünce yeniden açar. Sorun kaydı ile görevi **aynı veritabanı işleminde** yazar; ikisinin arasında kesinti olsa bile "açık sorun var, görevi yok" durumu oluşamaz (§9.1). AI capability veya model kill switch kapalıyken de çalışır.
2. **`Ai.Workflow` (kalıcı iş akışı motoru):** "geciken terminleri takip et", "fatura–sipariş–kabul karşılaştır", "vade/tahsilat takibi", "sevkiyat öncesi risk", "günlük yönetici brifingi" gibi **iş akışı şablonları** sunucuda saklanan `WorkflowInstance` olarak yürür; kullanıcının bilgisayarı kapansa da iş kaybolmaz. Bu bir **kalıcılık** hedefidir: sunucu kapalıyken işleme durur, sunucu açılınca kaldığı adımdan devam eder ve çift işlem yapmaz; bekleme durumlarında (`WaitingHuman/WaitingApproval/WaitingData`) model çağırmaz. Kullanıcının "bu haftaki teslimatları incele" gibi istekleri de bir `WorkflowInstance` olur.
3. **`Modules.Ai` (model):** iş öğesine açıklama, seçenek gerekçesi ve taslak (e-posta, görev metni) ekler; tutarları yazmaz (`{{measure}}`); hiçbir iş kaydını onaysız yazmaz.

Ajanın **yapamayacakları** (KİLİTLİ-KAYNAK): onaysız iş kaydı yazmak (`DEC-AI-003`); e-posta/mesaj göndermek (yalnız taslak; gönderim kullanıcı onayıyla, `DEC-DOC-006/007`); eskalasyon zincirini kendi belirlemek (politika tablosundan uygular); ERP'nin kesin kurallarını aşmak; "hep böyle yapıyoruz" cümlesini kurala çevirmek (`DEC-AI-006`). Sohbetten "görev aç" isteğinde AI yalnız **görev taslağı** üretir (`work.item.draft@1`, DRAFT); kullanıcı taslağı normal Çalışma Merkezi ekranında kendi komutuyla kaydeder (Work kullanıcı komutu; AI WRITE aracı değildir).

Bu ajanın ilk sürümü Faz 3'tür (master §11.2); ön koşulu `Modules.Work` çekirdeği ve `ERP-000F` outbox dispatcher'ıdır. Bu belge her iki parçanın kod tasarımını verir; kuyruğa alınması Sinan kararıdır.

---

## 1. Kodlama özeti

| Soru | Cevap |
|---|---|
| **Ne kodlanacak?** | İki yeni modül (`Modules/Work`, `Modules/Ai`), iki host runtime klasörü (`GoymenERP.Api/RulesRuntime`, `GoymenERP.Api/AiRuntime`), iki şema (`work`, `ai`), bir worker (`AiWorkflowWorker`), frontend'de iki lane (`src/work`, `src/ai`) ve K shell entegrasyonu, testler ve CI kapıları. |
| **Neye dokunulmayacak?** | Sahip modüllerin Domain/Infrastructure'ına doğrudan referans yok; sahip tablolarına yazma yok; ham SQL yok; middleware sırası değişmez; `ErpPermissions` kataloğuna yalnız sahip modül dilimleri izin ekler (AI'ya özgü "geniş okuma" izni yok). |
| **Kod deseni nereden?** | Sales/Purchasing/Inventory/Accounting modüllerinin mevcut desenleri (Ek K-1): modül extension + named factory, `ApplyWriteGuards`, `AuditSaveChangesInterceptor`, attempt/command satırı + replay-or-conflict, `SaveAsync` 2601/2627 yakalama, host runner ile aynı `DbTransaction`'a enlist, minimal API konvansiyonları, `IShellCapabilityProvider` + pinli registry, frontend `CapabilityRoute` + `exact()` decoder + `ViewOutcome`. |
| **Hangi sırayla?** | §21: Work çekirdeği (AI'dan bağımsız) → Ai iskeleti + READ araçları + fake provider → Proposal/Approval → Work.Rules kuralları (gölge) → Workflow worker → WRITE beyaz listesi + dispatcher/kurtarma → yönetim zekâsı. Her adım master §11 fazlarına ve K sınıflarına bağlıdır; bu belge sırayı **tanımlar**, başlatmaz. |
| **En kritik teknik kararlar?** | (1) `OperationIntentId` — sunucu üretimli, kalıcı iş niyeti kimliği; içerik hash'inden ayrı (§5.1, §10.2). (2) WRITE dispatch'te sahip modülün attempt/command satırı tek etkiyi garanti eder; kurtarma en fazla bir aynı-key gönderim yapar, yeni kimlik üretmez (§10.4). (3) Etki kuralları ve iş öğeleri `Work` içinde, AI'dan bağımsız (§9). (4) Sağlayıcı portu API-birincil; yerel model opsiyonel; veri sınıfı matrisi ve aktivasyon bayrağı ayrı (§11). (5) Ölçü referansı: model tutar yazmaz (§12). |
| **Kaç dosya?** | Ek K-3'teki liste ~140 backend dosyası, ~45 frontend dosyası, ~35 test dosyası (ÖNERİ; iskelet + ilk araç seti için). |

---

## 2. Çözüm yapısı, proje dosyaları ve bağımlılık kuralları

### 2.1 Klasör ağacı (ÖNERİ)

```text
src/backend/Modules/Work/
  GoymenERP.Modules.Work.Contracts/            # V1 sözleşmeleri: WorkItemV1, GateDecisionV1, IssueV1, IWorkItemReadsV1, IWorkItemCommandsV1,
                                               # IImpactRuleGateQueryV1 — başka modüller ve Ai yalnız bunu görür
  GoymenERP.Modules.Work.Domain/               # WorkItem, WorkItemEvent, ApprovalRequest, EscalationPolicy, Issue, Evaluation,
                                               # ImpactRuleDefinition, VisibilityMatrix, değişmezler ve durum makineleri
  GoymenERP.Modules.Work.Application/          # IWorkItemService, IWorkItemStore, IImpactRuleGate (salt-okunur), IImpactRuleTracker (kalıcı), IImpactRuleEvaluatorPort,
                                               # IEscalationScheduler, WorkCommandMetadata, WorkWriteEnvelope, problem kodları
  GoymenERP.Modules.Work.Infrastructure/       # WorkDbContext (şema: work), Rows, Store, Migrations, WorkModuleExtensions,
                                               # WorkShellCapabilityProvider (Organization/Counterparty deseni: modül içinde kayıt)
  GoymenERP.Modules.Work.Rules/                # Kural kataloğu (IR-LOG-001 …), ImpactRuleGate + ImpactRuleTracker —
                                               # Ai'ye referans YOK; sahip port'larını RulesRuntime host'u üzerinden çağırır
src/backend/Modules/Ai/
  GoymenERP.Modules.Ai.Contracts/              # IAiTool, AiToolDescriptor, MeasuredValue, AiDataClass, AiRiskClass, AiContext (audit eki),
                                               # AiToolContext, AiToolResult, ProposalV1 DTO'ları
  GoymenERP.Modules.Ai.Domain/                 # AiRun, AiRunStep, AiProposal (OperationIntentId), AiApproval, AiKnowledgeItem/Version,
                                               # AiPolicyVersion, AiBudget, AiJob, WorkflowInstance — aggregate'ler
  GoymenERP.Modules.Ai.Application/            # Port'lar: IAiCapabilityService, IAiPolicyEvaluator, IAiToolCatalog, IAiContextBuilder,
                                               # IAiOrchestrator, IAiOutputValidator, IAiMeasureRenderer, IAiPreviewService, IAiApprovalBinding,
                                               # IAiCommandDispatcher, IAiRecoveryCoordinator, IAiModelClient, IAiModelRouter, IAiRunStore,
                                               # IAiUsageMeter, IAiKnowledgeIndex, IAiPromptRegistry, IAiEvalRunner, IAiSecurityEventSink
  GoymenERP.Modules.Ai.Infrastructure/         # AiDbContext (şema: ai), Rows, Stores, Migrations, AiModuleExtensions, provider adapter'ları
                                               # (FakeAiModelClient, HttpJsonSchemaModelClient), egress allowlist HttpClientHandler,
                                               # redaction, prompt registry (dosya + DB sürüm), vektör depo adaptörü
  GoymenERP.Modules.Ai.Workflow/               # AiWorkflowWorker (BackgroundService), IAiScheduler, iş akışı şablonları, inbox consumer,
                                               # WorkflowInstance durum makinesi çalıştırıcısı, kurtarma zamanlayıcısı
src/backend/GoymenERP.Api/
  RulesRuntime/                                # Sahip port'larını IImpactRuleEvaluator'a saran host adaptörleri (Ai'ye referans yok),
                                               # RulesEndpoints.cs (on-demand kapı sorgusu), RulesOutboxConsumer.cs (ERP-000F gelince)
  AiRuntime/                                   # Tools/<Owner>/…Tool.cs (IAiTool adaptörleri), AiEndpoints.cs, AiShellCapabilityProvider.cs,
                                               # AiCommandEnvelopeFactory.cs (SourceChannel=AiAssisted, AiContext), AiReadSnapshotRunner.cs
src/frontend/goymen-erp-web/src/
  work/                                        # WorkCenterPage, WorkItemDetailPage, GateDecisionCard, capabilities.ts, api.ts, contract.ts, routes
  ai/                                          # AiContextPanel, ProposalPreviewCard, AiCenterPage (AI-01/02/03), capabilities.ts, api.ts, contract.ts
  shell/                                       # CAPABILITY / CAPABILITY_ROUTE / NAV eklemeleri; AI slot; drift testleri
tests/
  GoymenERP.Work.UnitTests/  GoymenERP.Ai.UnitTests/  GoymenERP.Api.IntegrationTests/{Work,Ai}*SqlServerRelationalAcceptanceTests.cs
  GoymenERP.Ai.EvalTests/ (fake provider + golden)   GoymenERP.ArchitectureTests (mevcut; allowlist)
```

`Work.Rules` ayrı proje olarak `Work.Application`'a referans verir; `Ai.*` projeleri `Work.Contracts` ve `Work.Application`'a referans verir (tek yön). `Work` hiçbir `Ai.*` projesine referans vermez (architecture testi, §18.4).

### 2.2 Proje dosyaları (mevcut Sales deseniyle)

Bütün projeler `Directory.Build.props`'u miras alır (`Deterministic`, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors=true` — KİLİTLİ-KAYNAK, `Directory.Build.props` L1–7); SDK `10.0.300` (`global.json`).

```xml
<!-- src/backend/Modules/Ai/GoymenERP.Modules.Ai.Infrastructure/GoymenERP.Modules.Ai.Infrastructure.csproj (ÖNERİ) -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.8" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../../BuildingBlocks/GoymenERP.BuildingBlocks/GoymenERP.BuildingBlocks.csproj" />
    <ProjectReference Include="../../Audit/GoymenERP.Modules.Audit.Infrastructure/GoymenERP.Modules.Audit.Infrastructure.csproj" />
    <ProjectReference Include="../GoymenERP.Modules.Ai.Application/GoymenERP.Modules.Ai.Application.csproj" />
    <ProjectReference Include="../GoymenERP.Modules.Ai.Domain/GoymenERP.Modules.Ai.Domain.csproj" />
    <ProjectReference Include="../GoymenERP.Modules.Ai.Contracts/GoymenERP.Modules.Ai.Contracts.csproj" />
  </ItemGroup>
</Project>
```

Kural: `*.Domain.csproj` yalnız PropertyGroup taşır (Sales deseni); `*.Application.csproj` BuildingBlocks + Domain + (yalnız) diğer modüllerin **Application/Contracts** projelerine referans verir; `*.Infrastructure.csproj` Audit.Infrastructure'a referans verir (audit satırı aynı `SaveChanges`'ta yazılır — ADR-009). `GoymenERP.Api.csproj` yeni modüllerin Infrastructure ve Workflow projelerine referans alır (Ek K-1 madde 20).

**Sağlayıcı SDK paketi eklenmez** (`ERP-000J` §24 "şimdi yasak"): `IAiModelClient` uygulaması `HttpClient` + `System.Text.Json` ile yazılır; SDK kararı ADR-008'e aittir (§11).

### 2.3 Bağımlılık allowlist'i

`tests/GoymenERP.ArchitectureTests/ModuleDependencyGuardTests.cs` her `.Domain`/`.Infrastructure` hedefli modüller arası referansı `docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json` ile **birebir** eşler; fazla kenar da eksik kenar da testi kırar (Ek K-1 madde 17c). Yeni modüller için gereken kenarlar (ÖNERİ; allowlist değişikliği insan review kapısındadır):

| Kaynak | Hedef | ADR | Gerekçe |
|---|---|---|---|
| `Work.Infrastructure` | `Audit.Infrastructure` | ADR-009 | İş öğesi/değerlendirme yazımı ile audit satırı tek `SaveChanges`. |
| `Ai.Infrastructure` | `Audit.Infrastructure` | ADR-009 | `ai.*` durum yazımları ile audit satırı tek `SaveChanges`. |

Başka kenar yoktur: `Ai.*` ve `Work.*` sahip modüllerin Domain/Infrastructure projelerine referans vermez; sahip port'ları (`*.Application` / `*.Contracts`) `GoymenERP.Api/AiRuntime` ve `GoymenERP.Api/RulesRuntime` host adaptörlerinde çağrılır (mevcut `InventoryPhysicalAllocationHostService`, `PurchaseGoodsReceiptHost` deseni — Ek K-1 madde 11).

### 2.4 Statik kurallar (derleme/test zamanı)

- `Ai.Contracts` ve `Work.Contracts` yalnız `record`/`interface`/`enum` içerir; EF veya HTTP türü yok.
- `Work.Rules` projesinde `GoymenERP.Modules.Ai` namespace'ine `using` yasak (architecture testi: `WorkNeverReferencesAi`).
- Hiçbir `Ai.*`/`Work.*` projesinde `FromSqlRaw`, `ExecuteSqlRaw`, `SqlCommand` kullanılmaz (AGENTS.md "ham SQL yasak"; architecture testi `AiAndWorkNeverUseRawSql`).
- `IgnoreQueryFilters` çağrısı yalnız `PlatformTenantQueryFilterBypassGuardTests` envanterine kayıtlı yerlerde (Ek K-1 madde 17c notu); AI/Work'te başlangıçta **sıfır**.

## 3. Host kaydı

### 3.1 Program.cs (mevcut sıraya ekleme; middleware sırası değişmez)

Mevcut kayıt biçimi (KİLİTLİ-KAYNAK, `GoymenERP.Api/Program.cs`): `builder.Services.AddSalesModule(builder.Configuration, (services, options) => options.AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>()));` … `app.MapSalesEndpoints();` … `await app.Services.InitializeSalesModuleAsync();`. Aynı üç çağrı Work ve Ai için eklenir; **Work önce** (Ai, Work'e bağımlıdır; Work'ün Ai'ye bağımlılığı yoktur):

```csharp
// Program.cs (ÖNERİ; yalnız eklenen satırlar)
builder.Services.AddWorkModule(builder.Configuration,
    (services, options) => options.AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddWorkRules();                       // Work.Rules kataloğu + IImpactRuleGate + IImpactRuleTracker (Ai'den bağımsız)
builder.Services.AddRulesRuntime();                    // GoymenERP.Api/RulesRuntime: IImpactRuleEvaluator host adaptörleri

builder.Services.AddAiModule(builder.Configuration,
    (services, options) => options.AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddAiRuntime();                       // GoymenERP.Api/AiRuntime: IAiTool adaptörleri, AiCommandEnvelopeFactory, snapshot runner
builder.Services.AddAiWorkflow(builder.Configuration); // AiWorkflowWorker yalnız Ai:Workflow:Enabled=true ve OpenAPI üretim modu dışında
builder.Services.AddSingleton<IShellCapabilityProvider, AiShellCapabilityProvider>();   // Work provider'ı AddWorkModule içinde (Organization deseni)
// …
app.MapWorkEndpoints();
app.MapRulesEndpoints();
app.MapAiEndpoints();
// …
await app.Services.InitializeWorkModuleAsync();       // Organization'dan SONRA (OrganizationCompanyReference FK)
await app.Services.InitializeAiModuleAsync();
```

Middleware sırası (`ExceptionHandler → ShellCapabilityTransportMiddleware → CorrelationContextMiddleware → Cors → [yalnız DatabaseStartupMode.ValidateOnly iken DatabaseReadinessGateMiddleware] → Authentication → CompanyContextMiddleware → Authorization`) **değişmez**; AI endpoint'leri `X-Company-Id` ve `X-Correlation-Id`'yi aynı middleware'lerden alır. Worker ve dispatch tarafında HTTP bağlamı olmadığı için `ICompanyContext`, `ICorrelationContext` **ve sahip modüllerin okuduğu `ICurrentUser`** her iş için **yeni DI scope içinde elle** kurulur (§10.3, §14.3).

### 3.2 `AiModuleExtensions.AddAiModule` (Sales deseniyle)

```csharp
// GoymenERP.Modules.Ai.Infrastructure/AiModuleExtensions.cs (ÖNERİ)
[assembly: InternalsVisibleTo("GoymenERP.Ai.UnitTests")]
[assembly: InternalsVisibleTo("GoymenERP.Api.IntegrationTests")]

namespace GoymenERP.Modules.Ai.Infrastructure;

public static class AiModuleExtensions
{
    public static IServiceCollection AddAiModule(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        var connectionString = configuration["DB_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("DB_CONNECTION_STRING tanımlı değil.");
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Desteklenmeyen DB_PROVIDER: {provider}");
        }

        services.AddDbContext<AiDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AiDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });

        services.AddOptions<AiOptions>().Bind(configuration.GetSection("Ai")).ValidateDataAnnotations().ValidateOnStart();

        // Company context "architecture guard": ICompanyContext kayıt EDİLMEZ, çözülür (Sales CreateDraftService deseni).
        services.AddScoped<IAiRunStore, AiRunStore>();
        services.AddScoped<IAiProposalStore, AiProposalStore>();
        services.AddScoped<IAiToolCatalog, AiToolCatalog>();            // singleton içerik, scoped görünüm (aktör bazlı allowlist)
        services.AddScoped<IAiPolicyEvaluator>(CreatePolicyEvaluator);
        services.AddScoped<IAiCapabilityService>(CreateCapabilityService);
        services.AddScoped<IAiContextBuilder, AiContextBuilder>();
        services.AddScoped<IAiOutputValidator, AiOutputValidator>();
        services.AddScoped<IAiMeasureRenderer, AiMeasureRenderer>();
        services.AddScoped<IAiPreviewService, AiPreviewService>();
        services.AddScoped<IAiApprovalBinding, AiApprovalBinding>();
        services.AddScoped<IAiOrchestrator, AiOrchestrator>();
        services.AddScoped<IAiUsageMeter, AiUsageMeter>();
        services.AddScoped<IAiPromptRegistry, AiPromptRegistry>();
        services.AddSingleton<IAiModelRouter, AiModelRouter>();          // yapılandırma tabanlı; sağlayıcı EKLEYEMEZ
        services.AddTransient<AiEgressAllowlistHandler>();              // DelegatingHandler: transient + AddHttpMessageHandler (primary handler DEĞİL)
        services.AddHttpClient<HttpJsonSchemaModelClient>()             // egress allowlist handler zincirde; adı yapılandırmadan
            .AddHttpMessageHandler<AiEgressAllowlistHandler>();
        services.AddScoped<IAiModelClient>(CreateModelClient);          // Fake | HttpJsonSchema — yapılandırmadan; secret prompt'a girmez
        services.AddSingleton(TimeProvider.System);                    // zaten kayıtlıysa tekrar eklenmez
        return services;
    }

    private static AiPolicyEvaluator CreatePolicyEvaluator(IServiceProvider sp) => new(
        sp.GetRequiredService<ICompanyContext>(),                      // architecture guard: modül içinde çözülür
        sp.GetRequiredService<ICurrentUser>(),
        sp.GetRequiredService<IErpAuthorityRevalidationService>(),
        sp.GetRequiredService<IActiveRoleCodeReader>(),
        sp.GetRequiredService<IAiCapabilityService>(),
        sp.GetRequiredService<IAiToolCatalog>(),
        sp.GetRequiredService<IAiSecurityEventSink>(),
        sp.GetRequiredService<IOptions<AiOptions>>(),
        sp.GetRequiredService<TimeProvider>());

    public static async Task InitializeAiModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        if (dbContext.Database.IsRelational()) { await dbContext.Database.MigrateAsync(cancellationToken); }
        else { await dbContext.Database.EnsureCreatedAsync(cancellationToken); }
    }
}
```

`WorkModuleExtensions.AddWorkModule` aynı biçimdedir; ek olarak `services.AddSingleton<IShellCapabilityProvider, WorkShellCapabilityProvider>()` modül içinde kaydedilir (Organization/Counterparty/Inventory deseni — Ek K-1 madde 10). Tasarım zamanı `IDesignTimeDbContextFactory<AiDbContext>` `DesignTimeCompanyContext` ile üretilir (Sales deseni).

### 3.3 Test factory eklemeleri

`tests/GoymenERP.Api.IntegrationTests/GoymenErpApiFactory.UseInMemoryDatabases` her host-wired DbContext'i in-memory ile değiştirir (Ek K-1 madde 17b); `WorkDbContext` ve `AiDbContext` için aynı üç satır eklenir (`RemoveAll<DbContextOptions<T>>`, `RemoveAll<IDbContextOptionsConfiguration<T>>`, `AddDbContext<T>(UseInMemoryDatabase($"{databaseName}-ai"))`). Worker in-memory host'ta **kapalı** başlar (`Ai:Workflow:Enabled=false` varsayılan).

---

## 4. Veri modeli ve EF eşlemeleri

### 4.1 Ortak kurallar (KİLİTLİ-KAYNAK — mevcut modül deseni)

- Şema sabiti ve kimlik collation'ı: `public const string Schema = "ai"; IdentityCollation = "Latin1_General_100_BIN2"` (`SalesDbContext` deseni).
- Her satır `I{Module}TenantRow { Guid CompanyId }` uygular; her entity `HasQueryFilter(x => x.CompanyId == companyContext.CompanyId)`; `OrganizationCompanyReference` `organization.Companies`'e `ExcludeFromMigrations()` ile eşlenir ve FK `Restrict`.
- `modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true); modelBuilder.Entity<AuditEvent>().HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);`
- `SaveChanges*` override'ları `ApplyWriteGuards()` çağırır: `Deleted` durumunda entity yok; `CompanyId` boş değil ve context ile eşit; `Modified` yalnız satır türüne göre izinli alanlarda; append-only setlerde `Modified` yok. Audit satırı için `{Module}AuditEventTenantGuard.Validate(dbContext, companyContext)`.
- Sürüm: durum güncellenen satırlarda `long Version` (`IsConcurrencyToken`) + `byte[] RowVersion` (`IsRowVersion`), `HasAlternateKey(CompanyId, Id)`.
- Hash sütunları: key hash **büyük harf** SHA-256 hex (64), içerik hash'i **küçük harf** SHA-256 hex (64) — modüllerde bu iki durum karışıktır (Ek K-1 madde 14); `ai` ve `work` şemaları Sales/Purchasing/Logistics ile aynı seçimi yapar ve `CK_*_Hash` check constraint'i ile uzunluk 64'ü zorlar.

### 4.2 `work` şeması — satırlar ve eşlemeler

```csharp
// GoymenERP.Modules.Work.Infrastructure/Persistence/WorkRows.cs (ÖNERİ)
internal interface IWorkTenantRow { Guid CompanyId { get; } }

/// <summary>Sorun: kural + hedef varlık için sürümden bağımsız yaşam döngüsü. Silinmez; yalnız durum alanları güncellenir.</summary>
internal sealed class IssueRow : IWorkTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string RuleId { get; set; } = string.Empty;          // "IR-LOG-001"
    public string TargetEntityType { get; set; } = string.Empty;// "Counterparty.CompanyPartyAccount"
    public Guid TargetId { get; set; }
    public int Status { get; set; }                             // 1 Open · 2 Acknowledged · 3 Resolved · 4 Reopened · 5 Suppressed
    public int Severity { get; set; }                           // son değerlendirme
    public DateTimeOffset FirstSeenAtUtc { get; set; }
    public Guid? LastEvaluationId { get; set; }
    public string? ResolvedByRef { get; set; }                  // koşulu kaldıran olay kimliği
    public string? DecisionRefJson { get; set; }                // Acknowledged iken geçerli karar (istisna referansı, kapsam, geçerlilik)
    public int ReopenCount { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Değerlendirme: bir Sorun'un belirli kural sürümü + varlık sürümü + zamandaki tek hesabı. Append-only.</summary>
internal sealed class EvaluationRow : IWorkTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid IssueId { get; set; }
    public int RuleVersion { get; set; }
    public long TargetVersion { get; set; }
    public int TriggerKind { get; set; }                        // 1 Event · 2 OnDemand · 3 Schedule · 4 RuleVersionChange
    public string? DeliveryId { get; set; }                     // outbox teslim kimliği (üçüncü kimlik)
    public string MeasuresRef { get; set; } = string.Empty;     // MeasuredValue[] içerik deposunda (§4.5); satırda referans
    public string MeasuresHash { get; set; } = string.Empty;    // içerik silinse de kanıt olarak kalır
    public string? GateDecisionJson { get; set; }
    public int Outcome { get; set; }                            // 1 Raised · 2 Unchanged · 3 Cleared · 4 Partial · 5 Acknowledged (CK_Evaluations_Outcome 1..5)
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset EvaluatedAtUtc { get; set; }
}

/// <summary>Görev / uyarı / onay isteği. Silinmez; durum, sahip, son tarih ve kapanış kanıtı güncellenir.</summary>
internal sealed class WorkItemRow : IWorkTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int Kind { get; set; }                               // 1 Task · 2 Alert · 3 ApprovalRequest · 4 InfoRequest
    public string Title { get; set; } = string.Empty;
    public int SourceKind { get; set; }                         // 1 Human · 2 ImpactRule · 3 Workflow · 4 Assistant
    public string SourceRef { get; set; } = string.Empty;       // "IR-LOG-001@1" | "wf:…" | "run:…" | "user:…"
    public Guid? IssueId { get; set; }
    public string? TargetEntityType { get; set; }
    public Guid? TargetId { get; set; }
    public string OwnerRole { get; set; } = string.Empty;
    public string? OwnerActorId { get; set; }
    public string WatchersJson { get; set; } = "[]";
    public int Priority { get; set; }
    public DateTimeOffset? DueAtUtc { get; set; }
    public string EscalationPolicyId { get; set; } = string.Empty;
    public int EscalationPolicyVersion { get; set; }            // öğe açıldığı andaki politika sürümüne bağlıdır
    public int EscalationStep { get; set; }
    public int Status { get; set; }                             // 1 Open · 2 InProgress · 3 WaitingInfo · 4 Escalated · 5 Done · 6 Cancelled
    public string? ClosureEvidenceRef { get; set; }             // audit/komut kimliği
    public string DedupKey { get; set; } = string.Empty;        // IssueKey bazlı; yalnız açık öğeler için tekil (filtered index)
    public string VisibilityMatrixJson { get; set; } = "{}";    // kanal × rol açıklık düzeyi
    public string IdempotencyKeyHash { get; set; } = string.Empty; // takip komutunun key hash'i: rule:{deliveryId} / sweep:{ruleId}:{slot}:{issueKey} / istemci key (§9.1)
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class WorkItemEventRow : IWorkTenantRow      // yorum, atama, eskalasyon, kapanış — append-only
{ /* Id, CompanyId, WorkItemId, Seq, Kind (Created/Commented/Assigned/Escalated/Closed…), ActorId, EscalationStep, FromRole, ToRole, PolicyId, PolicyVersion,
     PayloadRef + PayloadHash (yorum metni içerik deposunda; açıklık matrisine tabi), CorrelationId, RecordedAtUtc */ }

internal sealed class WorkAttemptRow : IWorkTenantRow        // ADR-011 deneme kaydı (Sales DraftAttemptRow deseni); append-only
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;     // "work.item.create.v1", "work.item.escalate.v1" …
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;         // gerçek kullanıcı veya "system:work.rules"
    public int Outcome { get; set; }                            // 1 Recorded · 2 Refused
    public Guid? WorkItemId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal sealed class EscalationPolicyRow : IWorkTenantRow   // (impactClass, roleChain[], sla[], stopAt) — Policy Registry sürümü ile
{ /* Id, CompanyId, PolicyId ("EP-FIN-01"), Version, ChainJson, SlaJson, StopAtRole, EffectiveFrom, EffectiveTo, Status */ }

internal sealed class ApprovalRequestRow : IWorkTenantRow    // maker≠checker, ikinci onay; sahip modül onaylarının ÜSTÜNDE takip nesnesi
{ /* Id, CompanyId, WorkItemId, RequiresSecondApproval, MakerActorId, CheckerActorId, SecondCheckerActorId, Decision, Reason, DecidedAtUtc, Version, RowVersion */ }

internal sealed class ImpactRuleRow : IWorkTenantRow         // kod tanımlı kuralın DB kayıt satırı: durum ve politika anahtarı
{ /* Id, CompanyId, RuleId, Version, Trigger, EffectKind, OwnerModule, SourceDecision, Status (Shadow/Active/Disabled), PolicyKey, DisclosureMatrixJson */ }
```

EF eşlemesi (özet, `WorkDbContext.OnModelCreating`):

```csharp
var issues = modelBuilder.Entity<IssueRow>();
issues.ToTable("Issues", Schema, t =>
{
    t.HasCheckConstraint("CK_Issues_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [TargetId] <> '00000000-0000-0000-0000-000000000000'");
    t.HasCheckConstraint("CK_Issues_Status", "[Status] BETWEEN 1 AND 5 AND [ReopenCount] >= 0");
});
issues.HasKey(x => x.Id);
issues.Property(x => x.Id).ValueGeneratedNever();
issues.HasAlternateKey(x => new { x.CompanyId, x.Id });
issues.Property(x => x.RuleId).HasMaxLength(64).UseCollation(IdentityCollation).IsRequired();
issues.Property(x => x.TargetEntityType).HasMaxLength(160).IsUnicode(false).IsRequired();
issues.Property(x => x.Version).IsConcurrencyToken();
issues.Property(x => x.RowVersion).IsRowVersion();
issues.HasIndex(x => new { x.CompanyId, x.RuleId, x.TargetEntityType, x.TargetId }).IsUnique().HasDatabaseName("UX_Issues_IssueKey");
issues.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
issues.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

var items = modelBuilder.Entity<WorkItemRow>();
// … aynı desen; ek olarak:
items.HasIndex(x => new { x.CompanyId, x.DedupKey }).IsUnique()
     .HasFilter("[Status] IN (1,2,3,4)")                       // yalnız AÇIK öğeler için tekillik: kapanmış öğe yeni öğeyi engellemez
     .HasDatabaseName("UX_WorkItems_OpenDedupKey");
items.HasIndex(x => new { x.CompanyId, x.OwnerRole, x.Status, x.DueAtUtc }).HasDatabaseName("IX_WorkItems_Queue");

var attempts = modelBuilder.Entity<WorkAttemptRow>();
attempts.ToTable("Attempts", Schema, t => t.HasCheckConstraint("CK_Attempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64"));
attempts.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });   // ADR-011 deneme kimliği
```

`ApplyWriteGuards` izinli `Modified` alanları (ÖNERİ): `IssueRow` → `Status, Severity, DecisionRefJson, LastEvaluationId, ResolvedByRef, ReopenCount, Version, UpdatedAtUtc`; `WorkItemRow` → `OwnerActorId, Status, DueAtUtc, ClosureEvidenceRef, Version, UpdatedAtUtc` ve **yalnız çift olarak** `OwnerRole` + `EscalationStep`: guard, `OwnerRole` değiştiğinde (a) `EscalationStep`'in tam 1 arttığını, (b) aynı save'de bu öğe ve bu adım için `Kind=Escalated` bir `WorkItemEventRow` eklendiğini ve olaydaki `FromRole/ToRole/PolicyId/PolicyVersion` değerlerinin satırla eşleştiğini doğrular; aksi halde save reddedilir. Böylece rol değişikliği genel düzenlemeye kapalı, yalnız eskalasyon komutunun politika doğrulamalı yoluna açıktır (politika zinciriyle eşleşme komutta `Escalate` içinde denetlenir). `EvaluationRow`, `WorkItemEventRow`, `WorkAttemptRow` → **hiç** (append-only). `Deleted` her satırda yasak.

### 4.3 `ai` şeması — satırlar ve eşlemeler

```csharp
// GoymenERP.Modules.Ai.Infrastructure/Persistence/AiRows.cs (ÖNERİ)
internal interface IAiTenantRow { Guid CompanyId { get; } }

internal sealed class RunRow : IAiTenantRow                 // metadata; içerik yok
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public int Channel { get; set; }                        // 1 Panel · 2 Center · 3 Job · 4 Voice
    public string Intent { get; set; } = string.Empty;      // "explain" | "draft" | "simulate" | "propose" | "brief" …
    public string ContextManifestJson { get; set; } = "{}"; // kaynak, sürüm, alan allowlist, as-of — içerik DEĞİL
    public int PromptPolicyVersion { get; set; }
    public int ToolCatalogVersion { get; set; }
    public string ModelDeploymentRef { get; set; } = string.Empty; // yapılandırma anahtarı; model adı iş mantığına girmez
    public int DataClassCeiling { get; set; }               // 1 Genel · 2 ŞirketIçi · 3 Gizli · 4 ÇokGizli
    public int Status { get; set; }                         // 1 Running · 2 Completed · 3 Partial · 4 Refused · 5 Failed · 6 Unknown
    public long CostMicros { get; set; } public int TokensIn { get; set; } public int TokensOut { get; set; } public int LatencyMs { get; set; }
    public string? UserCorrectionRef { get; set; }          // düzeltme içeriği içerik deposunda (§4.5)
    public string CorrelationId { get; set; } = string.Empty; public string? CausationId { get; set; }
    public long Version { get; set; } public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class RunStepRow : IAiTenantRow             // append-only; ArgsHash/ResultHash — içerik yok
{ /* Id, CompanyId, RunId, Seq, Kind (1 ToolCall · 2 ModelCall · 3 Validation · 4 Policy · 5 Render), ToolId, ToolVersion, ArgsHash, ResultHash, PolicyDecision, DurationMs, Error, RecordedAtUtc */ }

/// <summary>WRITE niyeti önizlemesi. OperationIntentId sunucu üretimli ve KALICI iş niyeti kimliğidir; içerik hash'i onu doğrular, yerine geçmez.</summary>
internal sealed class ProposalRow : IAiTenantRow
{
    public Guid Id { get; set; }                            // Proposal (önizleme) kimliği — her yeni önizleme yeni Id
    public Guid CompanyId { get; set; }
    public Guid OperationIntentId { get; set; }             // iş niyeti kimliği — aynı niyetin yeni önizlemeleri AYNI değeri taşır
    public int IntentRevision { get; set; }                 // aynı niyetin kaçıncı önizlemesi (Stale sonrası yeniden önizleme = +1)
    public Guid RunId { get; set; }
    public string ToolId { get; set; } = string.Empty; public int ToolVersion { get; set; }
    public string OwnerCommandContract { get; set; } = string.Empty;   // "sales.order-draft.create.v1"
    public string TargetEntityType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }                     // create'te null
    public long? ExpectedVersion { get; set; }
    public string CanonicalPayloadHash { get; set; } = string.Empty;   // küçük harf sha256(kanonik JSON)
    public string PayloadCipherRef { get; set; } = string.Empty;       // şifreli/immutable payload deposu referansı (§4.5); içerik satırda yok
    public string DiffJson { get; set; } = "{}";            // kullanıcıya gösterilen fark (açıklık matrisine tabi)
    public string ValidationResultJson { get; set; } = "{}";
    public string PolicyRefsJson { get; set; } = "{}";      // toolCatalog/promptPolicy/riskThreshold sürümleri
    public string IdempotencyKeyHash { get; set; } = string.Empty;    // büyük harf sha256("ai:{OperationIntentId:N}") — raw key saklanmaz
    public int Status { get; set; }                         // §5.1 durum makinesi
    public string RequestedByActorId { get; set; } = string.Empty;
    public Guid PreparedByRunId { get; set; }
    public string? ApprovedByActorId { get; set; } public string? SecondApprovedByActorId { get; set; }
    public int RequiredApprovals { get; set; }              // 1 | 2 (sahip modül politikası / Critical set)
    public string? ExecutedByRef { get; set; }              // "system:ai-runtime"
    public string? OnBehalfOfActorId { get; set; }
    public string? DispatchedPayloadHash { get; set; }      // gönderim anında dondurulan hash (== CanonicalPayloadHash olmalı)
    public string? ExecutedCommandRef { get; set; }         // sahip komut sonucu (örn. draft id) + orijinal correlation
    public bool TargetLock { get; set; }                    // Unknown çözülene kadar aynı hedef/niyet için ikinci dispatch yok
    public bool FailedBeforeDispatch { get; set; }          // Failed ama hiçbir şey gönderilmedi (ör. zarf bütünlüğü)
    public bool RejectedForFix { get; set; }                // Rejected yalnız ReturnForFix ise yeniden önizlenebilir
    public string? FailureCode { get; set; }
    public int RecoveryAttempts { get; set; }               // 0 | 1 — "en fazla bir aynı-key kurtarma gönderimi" DB düzeyinde (CK_Proposals_Recovery)
    public DateTimeOffset? LastRecoveryAtUtc { get; set; }
    public DateTimeOffset PreviewCreatedAtUtc { get; set; } public DateTimeOffset ExpiresAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty; public string? CausationId { get; set; }
    public long Version { get; set; } public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class ApprovalRow : IAiTenantRow            // onay KANITI; append-only; yürütme kimliği değildir
{ /* Id, CompanyId, ProposalId, OperationIntentId, ApproverActorId, Decision (1 Approve · 2 Reject · 3 ReturnForFix · 4 RequestInfo · 5 Conditional), BoundHash, BoundTargetVersion, ReauthRef, CorrelationId, ApprovedAtUtc */ }

internal sealed class JobRow : IAiTenantRow                 // olay/zaman tetikli iş
{ /* Id, CompanyId, JobType, DedupKey (eventType+aggregateId+aggregateVersion | schedule:{key}:{slot}), TriggerRef, Status, NextRunAtUtc, Attempts,
     LeaseOwner, LeaseUntilUtc (çoklu worker güvenliği), LastError, Version, RowVersion, CreatedAtUtc, UpdatedAtUtc */ }

internal sealed class WorkflowInstanceRow : IAiTenantRow    // kalıcı iş akışı durumu
{ /* Id, CompanyId, DefinitionId, DefinitionVersion, State (§14.2), SnapshotJson (rowversion'lar), WorkItemRefsJson, WaitingOnRef, RequestedByActorId, RunAsActorId (§14.3),
     StartedAtUtc, LastTransitionAtUtc, Version, RowVersion */ }

internal sealed class InboxRow : IAiTenantRow               // outbox consumer dedup — accounting.Deliveries deseni; append-only
{ public Guid CompanyId { get; set; } public string Consumer { get; set; } = string.Empty; public string MessageId { get; set; } = string.Empty;
  public string RequestHash { get; set; } = string.Empty; public int OutcomeKind { get; set; } public Guid? JobId { get; set; }
  public string CorrelationId { get; set; } = string.Empty; public DateTimeOffset RecordedAtUtc { get; set; } }

// AssistantFindingRow, KnowledgeItemRow/KnowledgeVersionRow, PolicyVersionRow, BudgetRow/UsageLedgerRow, EvalCaseRow/EvalRunRow, FeedbackRow:
// alanlar master §5.5 tablosundaki gibi; hepsi IAiTenantRow; içerik alanları PayloadCipherRef ile ayrı depoya (§4.5).
```

Kritik indeksler ve kısıtlar (ÖNERİ):

```csharp
var proposals = modelBuilder.Entity<ProposalRow>();
proposals.ToTable("Proposals", Schema, t =>
{
    t.HasCheckConstraint("CK_Proposals_Hashes", "LEN([CanonicalPayloadHash]) = 64 AND LEN([IdempotencyKeyHash]) = 64 AND ([DispatchedPayloadHash] IS NULL OR [DispatchedPayloadHash] = [CanonicalPayloadHash])");
    t.HasCheckConstraint("CK_Proposals_Approvals", "[RequiredApprovals] IN (1,2)");
    t.HasCheckConstraint("CK_Proposals_Recovery", "[RecoveryAttempts] BETWEEN 0 AND 1");   // ikinci kurtarma gönderimi şema düzeyinde imkânsız
    t.HasCheckConstraint("CK_Proposals_Status", "[Status] BETWEEN 1 AND 12");
});
proposals.HasIndex(x => new { x.CompanyId, x.OperationIntentId, x.IntentRevision }).IsUnique().HasDatabaseName("UX_Proposals_IntentRevision");
proposals.HasIndex(x => new { x.CompanyId, x.OperationIntentId }).IsUnique()
    .HasFilter("[Status] <> 12")                               // bir niyetin EN FAZLA BİR canlı (Superseded olmayan) revizyonu: eski revizyon yürütülemez
    .HasDatabaseName("UX_Proposals_OneLiveRevisionPerIntent");  // gönderilmiş (7/8/9) revizyon da canlıdır → bir niyet en fazla bir kez gönderilir
proposals.HasIndex(x => new { x.CompanyId, x.TargetEntityType, x.TargetId }).IsUnique()
    .HasFilter("[TargetLock] = 1 AND [TargetId] IS NOT NULL")   // güncelleme hedefi başına tek kilit
    .HasDatabaseName("UX_Proposals_TargetLock");
proposals.HasIndex(x => new { x.CompanyId, x.IdempotencyKeyHash }).HasDatabaseName("IX_Proposals_IdempotencyKeyHash");   // tekil DEĞİL: aynı niyetin revizyonları aynı key hash'ini taşır;
                                                                                                            // gönderim tekilliği UX_Proposals_OneLiveRevisionPerIntent ile sağlanır (kurtarma araması için)

var inbox = modelBuilder.Entity<InboxRow>();
inbox.ToTable("Inbox", Schema);
inbox.HasKey(x => new { x.CompanyId, x.Consumer, x.MessageId });   // accounting.Deliveries deseni (Ek K-1 madde 13)

var jobs = modelBuilder.Entity<JobRow>();
jobs.HasIndex(x => new { x.CompanyId, x.JobType, x.DedupKey }).IsUnique().HasDatabaseName("UX_Jobs_DedupKey");
jobs.HasIndex(x => new { x.Status, x.NextRunAtUtc, x.LeaseUntilUtc }).HasDatabaseName("IX_Jobs_Due");   // worker tarama (şirketler arası; sistem aktörü)
```

`IX_Jobs_Due` şirketler arası taranır: worker `IgnoreQueryFilters` **kullanmaz**; bunun yerine `AiDbContext`'e `SystemCompanyContext` (tek şirket) ile şirket başına ayrı scope açar ve şirket listesini Organization'ın yetkili port'undan alır (`ERP-000J` §12 tenant sınırı; §14.3). Bu, worker'ın hiçbir sorgusunun filtre dışında kalmamasını sağlar.

### 4.4 Migration ve rollback

- Her şema kendi `__EFMigrationsHistory` tablosunu taşır (`MigrationsHistoryTable("__EFMigrationsHistory", Schema)`), mevcut desen.
- İlk migration'lar: `AddWorkCore` (Issues, Evaluations, WorkItems, WorkItemEvents, Attempts, EscalationPolicies, ApprovalRequests, ImpactRules), `AddAiCore` (Runs, RunSteps, PolicyVersions, Budgets, UsageLedger), `AddAiProposals` (Proposals, Approvals, AssistantFindings, Feedback), `AddAiKnowledge`, `AddAiWorkflow` (Jobs, WorkflowInstances, Inbox), `AddAiEval`. Faz başına bir migration; hepsi **geri alınabilir** (`Down` tabloyu düşürür; veri kaybı yalnız o fazın kendi tablolarında) veya forward-fix planlı (audit/attempt satırları hiçbir `Down`'da silinmez — `Down` yalnız hiç canlıya çıkmamış migration için çalıştırılır; canlıdaki tablo için forward-fix).
- `organization.Companies` dışında şemalar arası FK yoktur; `OrganizationCompanyReference` `ExcludeFromMigrations()`.
- Migration K3'tür (AGENTS.md); gerçek SQL kabul testi `Database.MigrateAsync()` ile boş veritabanında koşar (Ek K-1 madde 17a `Run.StartAsync`).
- Vektör sütunu (`KnowledgeVersions.Embedding`) SQL Server yerleşik `vector` türü doğrulanana kadar migration'a **girmez**; ilk sürüm embedding'i ayrı depoya veya `varbinary(max)`'e yazar (NS-AI-07).

### 4.5 İçerik deposu, saklama ve değişmezlik

Master §5.5 matrisi şu tasarımla uygulanır:

| Satır türü | Depo | Değişmezlik | Süre sonu |
|---|---|---|---|
| `ai.RunSteps`, `ai.Approvals`, `work.Attempts`, `ai.Inbox` | SQL (`ai`/`work`) | Append-only (write guard); içerik alanı yok (hash, sürüm, karar, sonuç) | Kalır |
| `work.Evaluations` (ölçü değerleri), `work.WorkItemEvents` (yorum metni), `ai.Feedback` (düzeltme), `ai.Runs.UserCorrectionRef` | Satır SQL'de append-only; **içerik alanları** içerik deposunda (`MeasuresRef`, `PayloadRef`, `CorrectionRef` + hash) | Satır değişmez; içerik yazıldıktan sonra değişmez | Süre sonunda içerik silinir; satır, referans ve hash kalır ("şu tarihte şu karar verildi" kanıtı korunur, tutar/yorum metni saklanmaz) |
| `ai.Runs`, `ai.Proposals`, `ai.Jobs`, `ai.WorkflowInstances`, `work.Issues`, `work.WorkItems` | SQL | Silinmez; izinli alanlar rowversion ile güncellenir | Kalır |
| Öneri payload'ı, prompt/cevap içeriği (saklanmasına karar verilirse), zarf içeriği | **Şifreli, immutable içerik deposu** (`IAiPayloadVault`: SQL `ai.PayloadBlobs` `varbinary(max)` + AES-GCM, anahtar KMS/`DataProtection`; veya S3-uyumlu nesne depo — ADR-008 madde 10) | Yazıldıktan sonra değişmez; `PayloadCipherRef` = hash + anahtar sürümü | Süreli; süre sonunda blob silinir, satır metadata'sı ve hash'i kalır |
| `ai.KnowledgeVersions` | SQL + embedding deposu | Sürümlü; eski sürüm pasif | Onaylı kural kalır; taslak süreli |
| Belge metin çıkarımları, embedding'ler | Türev depo | Yeniden üretilebilir | Kaynak pasifleşince geçersiz |

`IAiPayloadVault.PutAsync(companyId, bytes)` içeriğin `sha256`'sını döndürür; `ProposalRow.CanonicalPayloadHash` bununla eşleşmek zorundadır (dispatcher gönderim öncesi vault'tan okuduğu payload'ı yeniden hash'ler; eşleşmezse `Failed(PayloadIntegrity)` + security-event).

## 5. Domain: aggregate'ler ve durum makineleri

### 5.1 `AiProposal` ve `OperationIntentId`

**Tanım (ÖNERİ, üçüncü eleştiri düzeltmesi):** Bir WRITE niyeti üç ayrı kimlik taşır ve hiçbiri diğerinin yerine geçmez:

| Kimlik | Kim üretir | Ne zaman | Ne için |
|---|---|---|---|
| `OperationIntentId` | Sunucu (`Guid.CreateVersion7()`) | Kullanıcı bir WRITE niyetini ilk kez başlattığında ("bu taslağı sipariş yap") | **Kalıcı iş niyeti kimliği.** Sahip modüle giden idempotency key `ai:{OperationIntentId:N}` bundan türer; aynı niyetin bütün önizlemeleri, onayları ve kurtarma denemeleri aynı değeri taşır |
| `ProposalId` (+ `IntentRevision`) | Sunucu | Her önizleme üretiminde | Önizleme kimliği; `Stale` sonrası yeniden önizleme aynı niyetin yeni revizyonudur |
| `CanonicalPayloadHash` | Sunucu (`sha256` — önizleme zarfının kanonik JSON'u: payload + sahip sözleşmesi + hedef/beklenen sürüm + araç/politika sürümleri, §10.1) | Önizleme anında; onayda bağlanır; gönderimde dondurulur | **İçerik doğrulaması**: gönderilen içeriğin onaylanan içerikle aynı olduğunu kanıtlar. Kimlik **değildir** |

Araç + şirket + kanonik içerik **tek başına** iş niyeti kimliği olamaz: aynı müşteri, aynı ürün, aynı miktar için iki ayrı meşru sipariş verilebilir; içerik aynı diye ikincisi ilkinin tekrarı sayılmaz. Buna karşılık ilk niyetin bağlantı kopması nedeniyle yeniden gönderimi ikinci sipariş yaratmamalıdır. Bu iki gereksinimi yalnız **niyet kimliği + içerik hash'i** çifti karşılar.

**Üç durum tablosu (sözleşme; §18'de test edilir):**

| Durum | Sahip modüle giden | Sahip modülün cevabı (mevcut kod, Ek K-1 madde 14 / §10.6) | Gateway davranışı |
|---|---|---|---|
| Aynı `OperationIntentId` + aynı içerik | Aynı key, aynı payload | Attempt/command satırı bulunur, actor ve hash eşit → **replay** (`IsReplay=true`, `Idempotency-Replayed: true`, ilk correlation) | Önceki işlemin devamı; ikinci etki yok; Proposal `Executed`, `ExecutedCommandRef` replay sonucundan |
| Aynı `OperationIntentId` + farklı içerik | Aynı key, farklı payload | **409** same-key mismatch (`same_key_payload_mismatch` / `idempotency_conflict` / Finance `outcome: IdempotencyConflict`) | Sessizce yeni işlem **yok**: Proposal `Failed(ContentConflict)`, `TargetLock` korunur, security-event (bu durum normal akışta oluşamaz; oluşursa bütünlük ihlali veya hata demektir) |
| Açıkça başlatılmış **farklı** `OperationIntentId` + aynı içerik | Farklı key, aynı payload | Yeni deneme satırı; komut normal yürür | Meşru yeni işlem olabilir; sahip modülün kendi mükerrerlik kontrolleri (cari aday, belge hash `DEC-DOC-002`, sipariş tekrarı uyarısı IR-SAL-00x) ayrıca uygulanır; gateway önizlemede "aynı içerikli açık/son 24 saatte yürütülmüş öneri var" **bilgisi** gösterir, engellemez |

**Yeni kimlik yasağı:** Sonucu belirsiz (`Unknown`) bir niyet için **yeni `OperationIntentId` üretilemez**; kurtarma yalnız aynı kimlikle yapılır (§10.4). Kullanıcı aynı hedef için yeni bir niyet başlatmak isterse `TargetLock` engeller ve "önceki işlemin sonucu bekleniyor" durumunu gösterir. Yeni kimlik yalnız önceki niyet **bilinen** terminal durumdayken (`Executed`, `Failed`, `Rejected`, hiç gönderilmemiş `Stale`) veya farklı bir hedef/iş için başlatılabilir.

**Gönderim sonrası kesin ret (`Failed`) → yeni niyet:** Sahip modül gönderimde kesin 4xx döndürürse (örn. hedef sürümü değiştiği için `version_conflict`) sonuç **bilinir** ve etki yoktur; Proposal `Failed(code)` olur. Aynı niyet yeniden önizlenemez, çünkü bazı sahip modüller bu reddi aynı key'e **terminal ret** olarak kaydeder (Purchasing `version_conflict`, Accounting `accounting_version_conflict`, Manufacturing `manufacturing_version_conflict`; Sales ADR-011 §7 terminal ret kaydı — §10.6) ve aynı key ile tekrar gönderim yalnız o reddi replay eder. Yeniden deneme **yeni niyet** (yeni key) ile yapılır; sonuç bilindiği için bu, belirsizliği aşma değildir. Onaydan **önce** fark edilen sürüm/politika değişikliği ise `Stale`'dir (hiçbir şey gönderilmemiştir) ve aynı niyetin yeni revizyonuyla (`Reissue`) önizlenir.

**Aggregate (ÖNERİ):**

```csharp
// GoymenERP.Modules.Ai.Domain/Proposals/AiProposal.cs
public enum AiProposalStatus
{
    Draft = 1, ShadowOnly = 2, AwaitingApproval = 3, PartiallyApproved = 4, Approved = 5, Rejected = 6,
    Dispatched = 7, Unknown = 8, Executed = 9, Failed = 10, Stale = 11,
    Superseded = 12,   // aynı niyetin yeni revizyonu üretildi; bu satır bir daha onaylanamaz ve gönderilemez
}

public sealed class AiProposal
{
    private AiProposal() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid OperationIntentId { get; private set; }
    public int IntentRevision { get; private set; }
    public AiProposalStatus Status { get; private set; }
    public string CanonicalPayloadHash { get; private set; } = string.Empty;
    public string? DispatchedPayloadHash { get; private set; }
    public int RequiredApprovals { get; private set; }
    public IReadOnlyList<string> Approvers => approvers;
    public IReadOnlyList<Guid> ApprovalIds { get; private set; } = [];   // ai.Approvals kanıt satırları (store yükler; dispatch AiContext'ine gider)
    private readonly List<string> approvers = [];
    public bool TargetLock { get; private set; }
    public long Version { get; private set; }
    // … diğer alanlar (§4.3 ProposalRow ile birebir)

    /// <summary>Yeni iş niyeti: kimlik sunucuda üretilir; içerik hash'i kimliğe girmez.
    /// Başlangıç durumu: gölge modda ShadowOnly (asla onaylanamaz); sahip doğrulaması geçmediyse Draft (onaylanamaz, düzeltilip Reissue);
    /// aksi halde AwaitingApproval.</summary>
    public static AiProposal StartIntent(Guid companyId, Guid runId, AiToolRef tool, ProposalTarget target, string canonicalPayloadHash,
        ProposalActors actors, int requiredApprovals, bool validationPassed, bool shadowMode, DateTimeOffset now, TimeSpan expiry)
    {
        RequireHash(canonicalPayloadHash);
        return new AiProposal
        {
            Id = Guid.CreateVersion7(), CompanyId = companyId, OperationIntentId = Guid.CreateVersion7(), IntentRevision = 1,
            Status = shadowMode ? AiProposalStatus.ShadowOnly : validationPassed ? AiProposalStatus.AwaitingApproval : AiProposalStatus.Draft,
            CanonicalPayloadHash = canonicalPayloadHash,
            RequiredApprovals = requiredApprovals is 1 or 2 ? requiredApprovals : throw new ArgumentOutOfRangeException(nameof(requiredApprovals)),
            Version = 1, /* … */
        };
    }

    /// <summary>Aynı niyetin yeni önizlemesi. YALNIZ izin verilen başlangıç durumlarından (beyaz liste):
    /// Draft, Stale, Rejected (ReturnForFix) ve ShadowOnly. Diğer her durum reddedilir:
    /// AwaitingApproval/PartiallyApproved/Approved (önce Stale olmalı), Dispatched/Unknown (belirsizliği aşma olurdu),
    /// Executed (iş bitti), Failed (sahip reddi aynı key'e terminal kaydedilmiş olabilir → YENİ niyet), Superseded (zaten eski).
    /// Gölge öneri yeniden üretilince de GÖLGE kalır. Eski revizyon aynı save'de Superseded olur.</summary>
    public AiProposal Reissue(string newCanonicalPayloadHash, ProposalTarget refreshedTarget, bool validationPassed, bool shadowModeNow, DateTimeOffset now, TimeSpan expiry)
    {
        var allowed = Status is AiProposalStatus.Draft or AiProposalStatus.Stale or AiProposalStatus.ShadowOnly
                      || (Status == AiProposalStatus.Rejected && RejectedForFix);         // yalnız "düzeltip yeniden gönder" reddi; kesin ret yeni niyet ister
        if (!allowed)
        {
            throw new AiProposalRuleException(AiProposalCodes.ReissueNotAllowed);
        }
        var shadow = Status == AiProposalStatus.ShadowOnly || shadowModeNow;   // eski gölgeyse VEYA araç şu an gölge moddaysa yeni revizyon da gölge
        Status = AiProposalStatus.Superseded; Version++;                     // eski revizyon kalıcı olarak ölü: onay/gönderim yolları Superseded'ı kabul etmez
        return new AiProposal { Id = Guid.CreateVersion7(), OperationIntentId = OperationIntentId, IntentRevision = IntentRevision + 1,
            CompanyId = CompanyId,
            Status = shadow ? AiProposalStatus.ShadowOnly : validationPassed ? AiProposalStatus.AwaitingApproval : AiProposalStatus.Draft,
            CanonicalPayloadHash = newCanonicalPayloadHash, RequiredApprovals = RequiredApprovals, Version = 1, /* … */ };
    }

    /// <summary>Onay: bağlı hash ve hedef sürüm eşleşmeli; onaylayan ≠ isteyen; ikinci onaycı ≠ birinci; kota dolunca Approved.</summary>
    public AiApprovalOutcome Approve(string approverActorId, string boundHash, long? boundTargetVersion, string requestedByActorId, DateTimeOffset now)
    {
        if (Status is not (AiProposalStatus.AwaitingApproval or AiProposalStatus.PartiallyApproved)) return AiApprovalOutcome.NotApprovable;
        if (now >= ExpiresAtUtc) { Status = AiProposalStatus.Stale; return AiApprovalOutcome.Expired; }
        if (!string.Equals(boundHash, CanonicalPayloadHash, StringComparison.Ordinal) || boundTargetVersion != ExpectedVersion) return AiApprovalOutcome.BindingMismatch;
        if (string.Equals(approverActorId, requestedByActorId, StringComparison.Ordinal)) return AiApprovalOutcome.SegregationOfDuties; // maker≠checker
        if (approvers.Contains(approverActorId, StringComparer.Ordinal)) return AiApprovalOutcome.AlreadyApprovedByActor;
        approvers.Add(approverActorId);
        Status = approvers.Count >= RequiredApprovals ? AiProposalStatus.Approved : AiProposalStatus.PartiallyApproved;
        Version++;
        return Status == AiProposalStatus.Approved ? AiApprovalOutcome.Approved : AiApprovalOutcome.AwaitingSecondApproval;
    }

    public void MarkDispatched(DateTimeOffset now)
    {
        if (Status != AiProposalStatus.Approved) throw new AiProposalRuleException(AiProposalCodes.NotApproved);
        DispatchedPayloadHash = CanonicalPayloadHash;   // dondurma: gönderilen == onaylanan
        TargetLock = true; Status = AiProposalStatus.Dispatched; Version++;
    }

    public void MarkExecuted(string executedCommandRef) { Require(Status is AiProposalStatus.Dispatched or AiProposalStatus.Unknown); Status = AiProposalStatus.Executed; TargetLock = false; ExecutedCommandRef = executedCommandRef; Version++; }
    /// <summary>Gönderim sonrası belirsizlik; ayrıca süresi geçmiş Dispatched kurtarmaya alınırken ilk adım (reason: dispatch_grace_expired).</summary>
    public void MarkUnknown(string reason) { Require(Status is AiProposalStatus.Dispatched or AiProposalStatus.Unknown); Status = AiProposalStatus.Unknown; /* TargetLock kalır */ Version++; }
    /// <summary>Gönderim SONRASI bilinen kesin ret (sahip 4xx). ContentConflict kilidi korur (bütünlük ihlali); diğer kesin retler hedefi serbest bırakır.</summary>
    public void MarkFailed(string code) { Require(Status is AiProposalStatus.Dispatched or AiProposalStatus.Unknown); Status = AiProposalStatus.Failed; FailureCode = code; TargetLock = code == AiProposalCodes.ContentConflict; Version++; }
    /// <summary>Uzun süren gönderim sürerken kurtarma öneriyi Unknown'a aldıysa: gönderimin BİLİNEN sonucu (Executed/Failed) taze satıra uygulanır; Unknown ise değişmez.</summary>
    public void ApplyLateOutcome(AiProposalStatus observed, string? commandRef, string? failureCode)
    {
        if (Status is not (AiProposalStatus.Dispatched or AiProposalStatus.Unknown)) return;   // zaten sonuçlanmış (ör. kurtarma replay ile Executed yaptı)
        if (observed == AiProposalStatus.Executed) MarkExecuted(commandRef!);
        else if (observed == AiProposalStatus.Failed) MarkFailed(failureCode!);
    }

    /// <summary>Gönderim ÖNCESİ ret (örn. zarf bütünlüğü bozuk, güncel yetki düştü). Hiçbir şey gönderilmedi: kilit yok, belirsizlik yok; yeniden deneme yeni niyet.</summary>
    public void FailBeforeDispatch(string code) { Require(Status == AiProposalStatus.Approved); Status = AiProposalStatus.Failed; FailureCode = code; TargetLock = false; FailedBeforeDispatch = true; Version++; }
    public void MarkStale(string reason) { Require(Status is AiProposalStatus.AwaitingApproval or AiProposalStatus.PartiallyApproved or AiProposalStatus.Approved); Status = AiProposalStatus.Stale; Version++; }   // yalnız GÖNDERİM ÖNCESİ
    public void MarkRecoveryAttempt(DateTimeOffset now) { Require(Status == AiProposalStatus.Unknown && RecoveryAttempts == 0); RecoveryAttempts = 1; LastRecoveryAtUtc = now; Version++; }   // rowversion: tek koordinatör kazanır
    public void Reject(string actorId, string reason, bool returnForFix) { Require(Status is AiProposalStatus.AwaitingApproval or AiProposalStatus.PartiallyApproved); Status = AiProposalStatus.Rejected; RejectedForFix = returnForFix; Version++; }

    public string IdempotencyKey => $"ai:{OperationIntentId:N}";   // raw key yalnız gönderim anında üretilir; DB'de hash'i saklanır
}
```

**Durum makinesi:**

```text
StartIntent ─▶ Draft (sahip doğrulaması geçmedi; onaylanamaz) ──(düzelt + Reissue, aynı niyet rev+1)──▶ AwaitingApproval
StartIntent ─▶ ShadowOnly (gölge mod; hiçbir zaman Approved olmaz) ──(Reissue)──▶ ShadowOnly (rev+1; gölge statüsü korunur)
Her Reissue: eski revizyon ──▶ Superseded (aynı save; bir daha onaylanamaz/gönderilemez)
Approved ──(gönderim öncesi zarf/politika kontrolü başarısız)──▶ Failed (FailBeforeDispatch: kilit yok) · veya ──▶ Stale (politika sürümü değişti)
StartIntent ─▶ AwaitingApproval ──(approve, kota 1/2)──▶ [PartiallyApproved] ──▶ Approved ──(tek kazanan dispatch)──▶ Dispatched
                  │  │                                                            │                                     │
                  │  └─(reject)──▶ Rejected ─(ReturnForFix: Reissue)─▶ …          │                                     ├─(sahip 2xx/replay)─▶ Executed
                  └─(expiry | version drift | policy drift — GÖNDERİM ÖNCESİ)──▶ Stale ─(Reissue: aynı niyet rev+1)─▶ AwaitingApproval
                                                                                                                         ├─(sahip kesin 4xx, örn. version_conflict)─▶ Failed ─▶ (yeniden deneme = YENİ niyet; sonuç bilinir)
                                                                                                                         └─(transport belirsiz)─▶ Unknown
Unknown ──(kurtarma: replay/ilk yürütme)──▶ Executed · (kurtarma: kesin ret)──▶ Failed · (ikinci belirsizlik)──▶ Unknown + TargetLock + iş öğesi (insan)
```

- `Approved` geçişi **tek kazanan**: `AiProposalStore.TransitionAsync` rowversion ile günceller; `DbUpdateConcurrencyException` → kaybeden `version_conflict` (409). İki ekran/çift tıklama/iki onaycı senaryosu (G-27) bununla kapanır.
- **İki onay gerektiren işlem:** `RequiredApprovals = 2` (sahip modül politikası veya `ErpPermissions.Critical` seti — ADR-008 madde 5). Birinci onay `PartiallyApproved`; ikinci onay farklı gerçek kullanıcı kimliğiyle (`approvers` listesi, `Ordinal`) `Approved`. İki onay da aynı `OperationIntentId` ve aynı `CanonicalPayloadHash`'e bağlanır; birinci ile ikinci onay arasında hedef sürümü veya politika değişirse `Stale` → her iki onay düşer, `Reissue` gerekir. Her onay `ai.Approvals`'a ayrı kanıt satırı yazar; hiçbir onay satırı key üretmez.
- **`TargetLock` kapsamı:** güncelleme niyetinde `(CompanyId, TargetEntityType, TargetId)` (filtered unique index `UX_Proposals_TargetLock`); **create** niyetinde hedef kimliği olmadığı için kilit `(CompanyId, OperationIntentId)` düzeyindedir (`UX_Proposals_OneLiveRevisionPerIntent`) ve ek olarak `(CompanyId, ToolId, RequestedByActorId)` için **yumuşak kilit**: aynı kullanıcının aynı araçla `Unknown` durumda bir create niyeti varken yeni create niyeti başlatması `intent_pending_recovery` (409) ile reddedilir; farklı kullanıcı veya farklı araç engellenmez (meşru yeni iş).
- Onay `expiry` politika değeridir (ÖNERİ 24 saat; NS-AI-05); `Reissue` aynı niyeti korur.

### 5.2 `AiRun`

```csharp
public sealed class AiRun
{
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string ActorId { get; private set; } = "";
    public AiRunStatus Status { get; private set; }             // Running · Completed · Partial · Refused · Failed · Unknown
    public AiRunBudget Budget { get; private set; } = default!;  // maxToolCalls, maxTokens, maxBytes, maxSeconds, maxCostMicros — server-side
    public AiContextManifest Manifest { get; private set; } = default!;   // kaynak/sürüm/alan allowlist/as-of — içerik yok
    private readonly List<AiRunStep> steps = [];

    public static AiRun Start(Guid companyId, string actorId, AiChannel channel, string intent, AiPolicySnapshot policy, AiRunBudget budget, DateTimeOffset now) => /* … */;
    public AiRunStep RecordStep(AiRunStepKind kind, string? toolId, int? toolVersion, string argsHash, string resultHash, string policyDecision, int durationMs, string? error)
    {
        if (steps.Count >= Budget.MaxToolCalls + Budget.MaxModelCalls + 8) throw new AiRunRuleException(AiRunCodes.BudgetExceeded);
        var step = new AiRunStep(Id, steps.Count + 1, kind, toolId, toolVersion, argsHash, resultHash, policyDecision, durationMs, error);
        steps.Add(step); return step;
    }
    public void Complete(AiRunOutcome outcome, AiRunUsage usage) { Status = outcome.IsPartial ? AiRunStatus.Partial : AiRunStatus.Completed; /* … */ }
    public void Refuse(string code) { Status = AiRunStatus.Refused; }
    public void Fail(string code) { Status = AiRunStatus.Failed; }
    public void MarkUnknown() { Status = AiRunStatus.Unknown; }
}
```

`AiRun` **içerik saklamaz**; `ArgsHash/ResultHash` ile adımları kanıtlar (`ERP-000J` §16). Prompt/cevap içeriği yalnız politika izin verirse `IAiPayloadVault`'a gider (§4.5).

### 5.3 `Issue` / `Evaluation` (`Work.Rules`), karar ve risk istisnası (v1.1)

Üç şey ayrı tutulur ve biri diğerinin yerine geçmez:

| Nesne | Ne anlatır | Neye göre değişir |
|---|---|---|
| **Sorun (Issue)** | İşletme koşulu sürüyor mu? (örn. müşterinin net riski limitin üstünde) | Yalnız sahip calculator'ın **koşul** yargısına göre (`risk.LimitExceeded`, gecikme politikası). Kapı kararına veya istisnaya göre **kapanmaz** |
| **Karar (Decision)** | Koşul sürerken yetkili ne dedi? (istisna, bekle, kısmi sevkiyat) | Finance'in onay kaydı; süresi ve kapsamı vardır |
| **İş öğesi (WorkItem)** | Karar bekleyen bir iş var mı? | Sorun karar beklerken açılır; karar verilince kanıtla kapanır |

```csharp
// GoymenERP.Modules.Work.Domain/Issues/Issue.cs
public enum IssueStatus { Open = 1, Acknowledged = 2, Resolved = 3, Reopened = 4, Suppressed = 5 }
// Open: koşul var, karar bekleniyor (görev gerekir) · Acknowledged: koşul var, geçerli karar var (görev gerekmez, izlenir)
// Resolved: koşul kalktı · Reopened: koşul geri döndü VEYA karar geçersizleşti (görev gerekir) · Suppressed: politika ile bastırıldı
public enum EvaluationOutcome { Raised = 1, Unchanged = 2, Cleared = 3, Partial = 4, Acknowledged = 5 }   // CK_Evaluations_Outcome: 1..5

public sealed class Issue
{
    public Guid Id { get; private set; }
    public IssueKey Key { get; private set; } = default!;        // (RuleId, TargetEntityType, TargetId) — IR-LOG-001'de hedef CompanyPartyAccount
    public IssueStatus Status { get; private set; }
    public Severity Severity { get; private set; }
    public DecisionRef? Decision { get; private set; }            // Finance kararı: ExceptionRef, tür (Exception/Hold/PartialShipment), kapsam, geçerlilik
    public string? ResolvedByRef { get; private set; }            // koşulu kaldıran olay (tahsilat, teminat…) — istisna DEĞİL
    public int ReopenCount { get; private set; }
    public long Version { get; private set; }
    public bool AwaitsDecision => Status is IssueStatus.Open or IssueStatus.Reopened;   // görev değişmezi bu özelliğe bağlıdır (§9.1)

    /// <summary>Sorun YALNIZ koşul varken oluşur (koşul yoksa satır yazılmaz). İlk tespit her zaman Raised'dır.</summary>
    public static (Issue Issue, EvaluationOutcome Outcome) Open(Guid companyId, IssueKey key, RuleEvaluationResult result, DateTimeOffset now)
    {
        if (!result.ConditionHolds || result.IsPartial) throw new WorkRuleException(WorkCodes.IssueRequiresHoldingCondition);
        var issue = new Issue { Id = Guid.CreateVersion7(), Key = key, Status = IssueStatus.Open, Severity = result.Severity, Version = 1 };
        return result.ActiveDecisionFor(issue.Key) is { } decision                     // karar Sorun'dan önce verilmişse (nadir) doğrudan Acknowledged
            ? (issue.With(IssueStatus.Acknowledged, decision), EvaluationOutcome.Acknowledged)
            : (issue, EvaluationOutcome.Raised);
    }

    /// <summary>Tek giriş noktası: koşul, karar ve süre bu metotta değerlendirilir; Acknowledge ayrıca çağrılmaz.</summary>
    public EvaluationOutcome Apply(RuleEvaluationResult result, DateTimeOffset now)
    {
        if (result.IsPartial) return EvaluationOutcome.Partial;                            // kaynak eksik: durum DEĞİŞMEZ; "koşul kalktı" sayılmaz
        if (!result.ConditionHolds)
        {
            if (Status == IssueStatus.Resolved) return EvaluationOutcome.Unchanged;
            Status = IssueStatus.Resolved; Decision = null; ResolvedByRef = result.ClearingEventRef; Version++; return EvaluationOutcome.Cleared;
        }
        var active = result.ActiveDecisionFor(Key);                                        // Finance'in bu MÜŞTERİ için GEÇERLİ kararı (talimattan bağımsız; §5.5)
        switch (Status)
        {
            case IssueStatus.Suppressed:
                return EvaluationOutcome.Unchanged;
            case IssueStatus.Open or IssueStatus.Reopened when active is not null:           // Finance karar verdi → görev kanıtla kapanır (§9.1)
                Status = IssueStatus.Acknowledged; Decision = active; Severity = result.Severity; Version++; return EvaluationOutcome.Acknowledged;
            case IssueStatus.Acknowledged when active is null || active.ExceptionRef != Decision!.ExceptionRef:   // karar tüketildi / süresi doldu / geri alındı
                Status = IssueStatus.Reopened; ReopenCount++; Decision = null; Severity = result.Severity; Version++; return EvaluationOutcome.Raised;
            case IssueStatus.Resolved:
                Status = IssueStatus.Reopened; ReopenCount++; Severity = result.Severity; Version++; return EvaluationOutcome.Raised;
            default:                                                                          // Open/Reopened kararsız veya Acknowledged kararı geçerli
                Severity = result.Severity; return EvaluationOutcome.Unchanged;               // yeni Sorun ve yeni uyarı ÜRETİLMEZ; kart güncellenir
        }
    }
}
```

**Kararın Sorun'a ulaşması:** Finance istisna/bekletme kararını kendi komutuyla kaydeder; `finance.risk-exception.approved.v1` / `.consumed.v1` / `.revoked.v1` olayları (ÖNERİ; `W4-RISK` + `ERP-000F`) IR-LOG-001'i tetikler. Olay altyapısı gelene kadar Finance'in onay ekranı, onay başarılı olduktan sonra takip komutunu (§9.4) çağırır. Değerlendirmede Finance port'u o müşterinin **geçerli kararlarını** (`ActiveDecisions`: istisna referansı, kapsam, durum) döndürür; `Apply` Sorunu `Acknowledged` yapar ve tracker aynı işlemde karar görevini kapatır. Karar geçerliliği odaktaki talimattan değil, kararın kendi referansı ve durumundan okunur; tahsilat veya zamanlı tarama tetiklemesinde de doğru sonuç verir.

**Tek sevkiyata verilen risk istisnası (ÖNERİ; sahibi Finance, `DEC-FIN-009` "istisna süreli ve auditli"; `W4-RISK` spec girdisi F0-10):**

| Olay | Müşterinin risk Sorunu | İlgili sevkiyatın izni (kapı kararı) | İstisna değerlendirme görevi |
|---|---|---|---|
| Finance tek sevkiyat istisnası onaylar | **Açık kalır**: `Open → Acknowledged` (karar referansıyla); limit aşımı sürdüğü sürece `Resolved` olmaz | Yalnız o talimat için `Proceed` + `exceptionRef`; başka talimat `ReviewRequired` | Karar verildiği için **kapanır** (kanıt: istisna onay kaydı) |
| O sevkiyat kaydedilir (istisna `Consumed`) | Sonraki değerlendirmede karar geçersiz → `Reopened` (koşul sürüyorsa) | Aynı müşteriye yeni talimat `ReviewRequired` | Değişmez kuralı yeni görev açar (§9.1) |
| İstisna süresi dolar / geri alınır (`Expired`/`Revoked`) | Zamanlı tarama yeniden değerlendirir → koşul sürüyorsa `Reopened`; mevcut Sorun kartı güncellenir, **yeni Sorun açılmaz** | `ReviewRequired`/`Stop` (politika) | Yeni görev açılır |
| Tahsilat/DBS/teminat limit aşımını kaldırır | `Resolved` | `Proceed` (istisnasız) | Açık görev varsa kanıtla kapanır |

Kapı sorgusu istisnanın **yalnız** durumunu ve referansını döndürür; gerekçe ve tutar Finance açıklık matrisine tabidir. Kapsamı eşleşmeyen istisna `Active` sayılmaz.

### 5.4 `WorkItem` ve `ApprovalRequest`

```csharp
public sealed class WorkItem
{
    public WorkItemStatus Status { get; private set; }   // Open · InProgress · WaitingInfo · Escalated · Done · Cancelled
    public int EscalationStep { get; private set; }

    public static WorkItem FromRule(Guid companyId, Issue issue, Guid evaluationId, WorkItemTemplate template, DisclosureMatrix visibility, EscalationPolicy policy, DateTimeOffset now)
        => /* DedupKey = issue.Key.ToDedupKey(); SourceKind = ImpactRule; OwnerRole = policy.Chain[0] (template.OwnerRole ile eşit olmalı, aksi WorkRuleException);
              Watchers += policy.ConcurrentRoles[0] (EP-CRIT gibi "eşzamanlı" zincirlerde diğer roller aynı adımda gözlemci + bildirim);
              EscalationPolicyId/Version = policy.Id/Version; EscalationStep = 0; DueAtUtc = now + policy.Sla[0] */ default!;

    /// <summary>Yalnız yetkili eskalasyon komutu (work.item.escalate.v1, sistem aktörü EscalationTicker) çağırır. Politika sürümü öğeye bağlıdır.</summary>
    public WorkItemEvent Escalate(EscalationPolicy policy, DateTimeOffset now)
    {
        if (Status is not (WorkItemStatus.Open or WorkItemStatus.InProgress or WorkItemStatus.WaitingInfo or WorkItemStatus.Escalated))
            throw new WorkRuleException(WorkCodes.WorkItemNotOpen);                         // Done/Cancelled öğe eskalasyonla yeniden açılamaz
        if (policy.Id != EscalationPolicyId || policy.Version != EscalationPolicyVersion) throw new WorkRuleException(WorkCodes.EscalationPolicyMismatch);
        if (EscalationStep + 1 >= policy.Chain.Count) { return WorkItemEvent.EscalationStopped(Id, EscalationStep, now); }   // stopAt: KPI'a düşer
        var fromRole = OwnerRole;
        EscalationStep++; OwnerRole = policy.Chain[EscalationStep]; DueAtUtc = now + policy.Sla[EscalationStep]; Status = WorkItemStatus.Escalated; Version++;
        return WorkItemEvent.Escalated(Id, EscalationStep, fromRole, OwnerRole, policy.Id, policy.Version, now);   // aynı save'de yazılır; write guard bunu arar
    }

    public void Close(ClosureEvidence evidence) { if (evidence.Ref is null) throw new WorkRuleException(WorkCodes.ClosureEvidenceRequired); Status = WorkItemStatus.Done; ClosureEvidenceRef = evidence.Ref; Version++; }
}
```

- Kapanış **kanıt ister** (`ClosureEvidenceRef`: audit event id, komut sonucu, istisna onayı, olay kimliği); "tamam" tıklaması yeterli değildir (`Work.Rules` sorunları kanıtı olaydan alır; insan görevlerinde kullanıcı kanıt referansı seçer veya "kanıtsız kapatıldı" işareti düşer ve raporlanır).
- `ApprovalRequest` sahip modül onaylarının **üstünde** takip nesnesidir; SoD gerçek kullanıcı kimliğiyle (`MakerActorId != CheckerActorId`, `SecondChecker != Checker`) doğrulanır; sahip modülün kendi maker-checker'ı (Finance/Accounting) değişmez.

### 5.5 `ImpactRuleDefinition` (kod tanımlı, sürümlü)

```csharp
// GoymenERP.Modules.Work.Rules/Catalog/IrLog001ShipmentCustomerRisk.cs (ÖNERİ)
public sealed class IrLog001ShipmentCustomerRisk : IImpactRule
{
    public string RuleId => "IR-LOG-001";
    public int Version => 1;
    public IReadOnlySet<RuleTrigger> Triggers => new HashSet<RuleTrigger>
    {
        RuleTrigger.OnDemand("logistics.loading-instruction.issue-intent"),
        RuleTrigger.Event("logistics.loading-instruction.issued.v1"),
        RuleTrigger.Event("finance.manual-receipt.recorded.v1"),           // kısmi tahsilat → yeniden değerlendirme
        RuleTrigger.Event("finance.risk-exception.approved.v1"),           // karar → Sorun Acknowledged, görev kapanır (ÖNERİ olay; W4-RISK)
        RuleTrigger.Event("finance.risk-exception.consumed.v1"),           // tek sevkiyat istisnası tükendi → Reopened (koşul sürüyorsa)
        RuleTrigger.Event("finance.risk-exception.revoked.v1"),
        RuleTrigger.Schedule("finance.risk-exception.expiry"),             // istisna süresi dolunca
    };
    public EffectKind Effect => EffectKind.Warning | EffectKind.WorkItem; // Block ASLA
    public string SourceDecision => "DEC-FIN-007/DEC-FIN-009";            // KİLİTLİ-KAYNAK
    public string EvaluatorId => "finance.risk.evaluate@1";                // sahip calculator; Work.Rules hesap YAPMAZ
    public DisclosureMatrix Disclosure => DisclosureMatrix.Of(
        new DisclosureRule("Sevkiyat", DisclosureLevel.GateDecision),
        new DisclosureRule("Finans", DisclosureLevel.Full),
        new DisclosureRule("GenelYonetici", DisclosureLevel.Full, MinSeverity: Severity.High));   // record DisclosureRule(string Role, DisclosureLevel Level, Severity? MinSeverity = null)
    public WorkItemTemplate WorkItem => new("FinansRiskOnayi", OwnerRole: "Finans", EscalationPolicyId: "EP-FIN-01");

    public async Task<RuleEvaluationResult> EvaluateAsync(RuleContext context, IImpactRuleEvaluatorPort port, CancellationToken cancellationToken)
    {
        // Sistem aktörü ile sahip port'u; sonuç MeasuredValue[] + yargılar + GateDecision; hedef = CompanyPartyAccount (risk müşteri bazlıdır)
        var risk = await port.EvaluateRiskAsync(context.CompanyId, context.Focus.PartyAccountId, context.Focus.StageTransition, cancellationToken);
        if (risk.Outcome != PortOutcome.Computed) return RuleEvaluationResult.Partial(risk.MissingSources);   // "0" veya "yok" DENMEZ
        // KOŞUL calculator yargısından gelir, kapı kararından DEĞİL: istisna sevkiyatı serbest bırakır ama müşterinin limit aşımını kaldırmaz.
        var holds = risk.Judgements.LimitExceeded || risk.Judgements.OverduePolicyTriggered;
        return new RuleEvaluationResult(holds, holds ? Severity.High : Severity.Info, risk.Measures, risk.Gate,
            ClearingEventRef: holds ? null : risk.ClearingEventRef,                               // koşulu kaldıran tahsilat/teminat olayı
            ActiveDecisions: risk.ActiveDecisions);                                              // müşterinin GEÇERLİ kararları (ref + kapsam + durum) — talimattan bağımsız
    }
}
```

Kural sürümü değişince (`Version` 2) açık Sorunlar `RuleVersionChange` tetikleyicisiyle yeniden değerlendirilir; `Evaluation.RuleVersion` ayrışır. Kural kataloğu ve `docs/ai/IMPACT_RULES.md` (master Ek B) aynı kimlikleri kullanır; DB'deki `work.ImpactRules` satırı yalnız durum (`Shadow/Active/Disabled`) ve politika anahtarı taşır — kural mantığı DB'de değil, koddadır (test edilebilirlik).

---

## 6. Araç sözleşmesi ve kataloğu (`Ai.Contracts`)

### 6.1 Sözleşme türleri

```csharp
// GoymenERP.Modules.Ai.Contracts/Tools/IAiTool.cs (ÖNERİ)
public enum AiRiskClass { Read = 1, Draft = 2, Simulate = 3, Write = 4 }
public enum AiDataClass { Genel = 1, SirketIci = 2, Gizli = 3, CokGizli = 4 }
public enum AiScopeSemantics { CompanyWide = 1, SiteScoped = 2, LocationScoped = 3, WorkCenterScoped = 4 }
public enum AiApprovalRequirement { None = 0, Preview = 1, PreviewAndApproval = 2, PreviewApprovalReauth = 3 }

public sealed record AiToolDescriptor(
    string ToolId, int Version, string OwnerModule, AiRiskClass RiskClass,
    string RequiredPermission,                 // ErpPermissions.* — sahip modülün izni; AI'ya özgü izin DEĞİL
    AiScopeSemantics Scope, AiDataClass MaxDataClass, IReadOnlyList<string> FieldAllowlist,
    Type InputType, Type OutputType,           // strict JSON schema bu türlerden üretilir (§6.3)
    string DeterministicValidator, string OwnerContractId,
    TimeSpan Timeout, bool RetrySafe, bool RequiresIdempotencyKey, bool RequiresExpectedVersion,
    AiApprovalRequirement Approval, AiEgressPolicy Egress, string AuditRouting, AiCostClass CostClass,
    string Owner, string DeprecationPolicy, string KillSwitchKey,
    int MaxOutputBytes, int MaxCardinality);

public sealed record AiToolContext(
    Guid CompanyId, string ActorId, PermissionScope Scope, AiActorMode ActorMode,   // ActorMode sunucu-içi; istemci/model belirleyemez
    Guid RunId, int StepSeq, AiDataClass DataClassCeiling, string CorrelationId, AiToolBudget Budget,
    AiDispatchInfo? Dispatch = null);                                               // yalnız dispatcher/kurtarma doldurur; WRITE aracı bunsuz çalışmaz

public sealed record AiDispatchInfo(string IdempotencyKey /* "ai:{OperationIntentId:N}" */, Guid OperationIntentId, Guid ProposalId, IReadOnlyList<Guid> ApprovalIds);

public enum AiActorMode { OnBehalfOfActor = 1, SystemRuleEvaluation = 2 }           // ikincisi yalnız RulesRuntime/Workflow'dan

public interface IAiTool<TArgs, TResult> where TArgs : class where TResult : class
{
    AiToolDescriptor Descriptor { get; }
    Task<AiToolResult<TResult>> ExecuteAsync(AiToolContext context, TArgs args, CancellationToken cancellationToken);
}

public sealed record AiToolResult<TResult>(TResult? Value, IReadOnlyList<MeasuredValue> Measures, AiCoverage Coverage, string? RefusalCode)
{
    public static AiToolResult<TResult> Ok(TResult value, IReadOnlyList<MeasuredValue> measures) => new(value, measures, AiCoverage.Complete, null);
    public static AiToolResult<TResult> Partial(TResult? value, IReadOnlyList<MeasuredValue> measures, IReadOnlyList<string> missing) => new(value, measures, AiCoverage.PartialOf(missing), null);
    public static AiToolResult<TResult> Refused(string code) => new(null, [], AiCoverage.Complete, code);
}

/// <summary>Kaynak-bağlı sayı: her sayısal alan bu zarfla döner; model bunu üretemez, yalnız referans verir.</summary>
public sealed record MeasuredValue(
    string MeasureId, string Measure, decimal Value, string Unit, DateTimeOffset AsOf,
    MeasureSubject Subject, string? Period, string? ScenarioRef, SourceRef SourceRef, string Formula, AssertionKind AssertionKind);
public sealed record MeasureSubject(string EntityType, Guid Id);
public sealed record SourceRef(string Module, string Entity, Guid Id, long Version);
public enum AssertionKind { Fact = 1, Plan = 2, Forecast = 3, Scenario = 4 }

// Kapı kararı (tutar yok; deterministik; modele gitmez) TEK yerde tanımlıdır: Work.Contracts.GateDecisionV1
// (Owner, Tool, SubjectId, Decision: GateDecisionKind ∈ {Proceed, ReviewRequired, Stop}, ReasonCodes, ExceptionRef, ExceptionState ∈ {Active, Consumed, Expired, Revoked}?, AsOf, PolicyVersion).
// Ai.Contracts bunu yeniden tanımlamaz; Ai → Work.Contracts referansı tek yönlüdür (§2.1).
```

Master rapor §5.3'teki `AiToolDescriptor` ile bu belge aynı kaydı tarif eder; kod düzeyinde kanonik biçim buradakidir: girdi/çıktı `Type` olarak verilir, strict JSON schema başlangıçta bu türlerden üretilir (§6.3) ve `ai.PolicyVersions`'a hash'iyle yazılır; `MaxOutputBytes/MaxCardinality` bayt/kardinalite limitlerini (`ERP-000J` §7) taşır.

Girdi/çıktı türleri `sealed record` + `JsonSerializable` kaynak-üretim bağlamıyla tanımlanır; sayısal iş alanı `decimal` **çıplak** olamaz — descriptor doğrulaması (`AiToolCatalog` başlangıçta reflection ile `OutputType` içinde `MeasuredValue` olmayan `decimal/double` alanı bulursa **başlatmayı reddeder**; §18.1).

### 6.2 Katalog (default-deny, sürümlü)

```csharp
internal sealed class AiToolCatalog(IEnumerable<IAiToolRegistration> registrations, IOptions<AiOptions> options) : IAiToolCatalog
{
    private readonly FrozenDictionary<(string ToolId, int Version), IAiToolRegistration> tools =
        registrations.ToFrozenDictionary(r => (r.Descriptor.ToolId, r.Descriptor.Version));
    public int CatalogVersion { get; } = AiToolCatalogManifestV1.Version;      // her araç ekleme/çıkarma → sürüm artar (SalesPermissionManifestV1 deseni)

    public bool TryResolve(string toolId, int version, out IAiToolRegistration registration)
        => tools.TryGetValue((toolId, version), out registration!) && registration.Descriptor.DeprecationPolicy != "Retired"
           && options.Value.Tools.IsEnabled(toolId, version);                   // tool bazlı flag; kill switch anahtarı
    // Model "serbest metinle" araç adı üretemez: orchestrator yalnız TryResolve'dan dönen registration'ı çağırır.
}
```

Katalog manifesti (`AiToolCatalogManifestV1 { Version; Names: FrozenSet<string> }`) testte dondurulur; araç kimliği seti değişince sürüm artmadan test kırılır (mevcut `SalesPermissionManifestV1` deseni).

### 6.3 Strict JSON schema üretimi

.NET 10'daki `System.Text.Json.Schema.JsonSchemaExporter` ile `InputType/OutputType`'tan schema üretilir; sonra `AiStrictSchemaTransformer` her `object`'e `additionalProperties=false` ve bütün alanları `required` yapar (opsiyonel alan `null`-union ile — `ERP-000J` §9 notu). Üretilen schema `ai.PolicyVersions`'a hash'iyle yazılır; provider'a giden `text.format`/function schema **budur**; provider'dan dönen argümanlar aynı schema ile sunucuda **yeniden** doğrulanır (strict schema business doğrulamasının yerine geçmez — `ERP-000J` §24).

### 6.4 Adaptör örnekleri (`GoymenERP.Api/AiRuntime/Tools`)

**SIMULATE — `finance.risk.evaluate@1` (sahip calculator `W4-RISK`'te yapılacak; DOĞRULANACAK):**

```csharp
internal sealed class FinanceRiskEvaluateTool(IFinanceRiskEvaluationV1 risk /* W4-RISK port'u — bugün YOK */, TimeProvider clock)
    : IAiTool<FinanceRiskEvaluateArgs, FinanceRiskEvaluateResult>
{
    public AiToolDescriptor Descriptor { get; } = new("finance.risk.evaluate", 1, "Finance", AiRiskClass.Simulate,
        RequiredPermission: "finance.risk.read" /* ÖNERİ — ErpPermissions.All'a sahip dilim ekler */, AiScopeSemantics.CompanyWide, AiDataClass.Gizli,
        FieldAllowlist: ["netRiskBefore", "netRiskAfter", "limit", "stageTransition", "changeDrivers", "gate"],
        typeof(FinanceRiskEvaluateArgs), typeof(FinanceRiskEvaluateResult), DeterministicValidator: "CARI-RISK/1",
        OwnerContractId: "finance.risk.evaluate.v1", Timeout: TimeSpan.FromSeconds(5), RetrySafe: true, RequiresIdempotencyKey: false,
        RequiresExpectedVersion: false, AiApprovalRequirement.None, AiEgressPolicy.LocalOrApprovedApi, "telemetry", AiCostClass.Low,
        Owner: "Finance", DeprecationPolicy: "Active", KillSwitchKey: "Ai:Tools:finance.risk.evaluate", MaxOutputBytes: 8_192, MaxCardinality: 1);

    public async Task<AiToolResult<FinanceRiskEvaluateResult>> ExecuteAsync(AiToolContext context, FinanceRiskEvaluateArgs args, CancellationToken ct)
    {
        var result = await risk.EvaluateAsync(context.CompanyId, args.PartyAccountId, args.StageTransition?.ToPort(), args.AsOf, ct); // EngineResult deseni
        return result.Outcome switch
        {
            EngineOutcome.Computed => AiToolResult<FinanceRiskEvaluateResult>.Ok(Map(result.Value!), Measures(result.Value!, context)),
            EngineOutcome.Unavailable => AiToolResult<FinanceRiskEvaluateResult>.Partial(null, [], result.MissingParameters),   // "veri eksik" → kısmi
            _ => AiToolResult<FinanceRiskEvaluateResult>.Refused(result.RefusalCode!),
        };
    }
    // Measures(): netRiskBefore (Fact), stageTransition.amount (Fact), netRiskAfter (Scenario), limit (Plan) — subject = CompanyPartyAccount
}
```

**WRITE — `sales.draft.create@1` (sahip komut mevcut; KİLİTLİ-KAYNAK kanıt Ek K-1 madde 5/14):**

```csharp
internal sealed class SalesDraftCreateTool(ISalesDraftService sales, AiCommandEnvelopeFactory envelopes)
    : IAiTool<CreateSalesOrderDraftRequest, AiWriteResult<SalesOrderDraftResponse>>
{
    public AiToolDescriptor Descriptor { get; } = new("sales.draft.create", 1, "Sales", AiRiskClass.Write,
        RequiredPermission: ErpPermissions.SalesDraftsCreate, AiScopeSemantics.CompanyWide, AiDataClass.Gizli, FieldAllowlist: [/* request alanları */],
        typeof(CreateSalesOrderDraftRequest), typeof(AiWriteResult<SalesOrderDraftResponse>), DeterministicValidator: "sales.order-draft.validate.v1",
        OwnerContractId: "sales.order-draft.create.v1", Timeout: TimeSpan.FromSeconds(10), RetrySafe: false, RequiresIdempotencyKey: true,
        RequiresExpectedVersion: false, AiApprovalRequirement.PreviewAndApproval, AiEgressPolicy.None, "business", AiCostClass.None,
        Owner: "Sales", DeprecationPolicy: "Active", KillSwitchKey: "Ai:Tools:sales.draft.create", MaxOutputBytes: 32_768, MaxCardinality: 1);

    /// <summary>YALNIZ IAiCommandDispatcher çağırır; Approved Proposal ve dondurulmuş payload ile. Orchestrator bu metodu çağıramaz (§10.3).</summary>
    public async Task<AiToolResult<AiWriteResult<SalesOrderDraftResponse>>> ExecuteAsync(AiToolContext context, CreateSalesOrderDraftRequest args, CancellationToken ct)
    {
        var metadata = envelopes.ForDispatch(context);   // SalesCommandMetadata(IdempotencyKey: context.Dispatch!.IdempotencyKey, CorrelationId, CausationId, RequestedAtUtc) — §10.3
        var response = await sales.CreateDraftAsync(args, metadata, ct);
        return AiToolResult<AiWriteResult<SalesOrderDraftResponse>>.Ok(new(response, response.IsReplay, response.OriginalCorrelationId), []);
    }
}
```

Sahip modül `SalesDraftServiceException(409, "same_key_payload_mismatch")` fırlatırsa dispatcher bunu `ContentConflict` olarak işler (§10.4). `SourceChannel` Sales'te `ICorrelationContext`'ten türediği için `AiCommandEnvelopeFactory` dispatch scope'unda `ICorrelationContext.Set(..., SourceChannels.AiAssisted, ...)` çağırır (§10.3).

---

## 7. Politika değerlendirici (13 adım)

`ERP-000J` §8 sırası **aynen**, her adım ayrı sınıf, kısa devre, sonuç kaydı:

```csharp
internal sealed class AiPolicyEvaluator(/* §3.2 factory bağımlılıkları */) : IAiPolicyEvaluator
{
    private readonly IReadOnlyList<IAiPolicyStep> steps =
    [
        new ResolveLocalUserStep(),           // 1. dış kimlik → aktif yerel ERP kullanıcısı (ICurrentUser.IsAuthenticated; UserId)
        new ResolveCompanyContextStep(),      // 2. request-scoped CompanyContext server-side; boş/değişmiş → fail-closed
        new CapabilityAndAllowlistStep(),     // 3. server-backed AI capability (IAiCapabilityService) + tool/version allowlist (IAiToolCatalog)
        new PermissionRequirementStep(),      // 4. risk sınıfı → permission/ABAC/SoD gereksinimi (descriptor.RequiredPermission, Scope)
        new AuthorityRevalidationStep(),      // 5. IErpAuthorityRevalidationService.RevalidatePermissionScopeAsync; IsGranted && Matches(scope)
        new InputSchemaStep(),                // 6. strict schema, boyut, alan allowlist, argüman kanonikleştirme
        new ResourceRevalidationStep(),       // 7. company/resource kimlikleri aktif context + sahip backend ile yeniden doğrulanır
        new DataClassAndEgressStep(),         // 8. veri sınıfı tavanı, minimizasyon/redaction, attachment, provider egress
        new DeterministicValidatorStep(),     // 9. sahip validator/formula/query (READ/SIMULATE: port; WRITE: sahip validate port'u varsa)
        new EffectBoundaryStep(),             // 10. READ/DRAFT/SIMULATE → etkisiz port; WRITE → yalnız preview/hash üret ve DUR
        // 11–13 dispatcher/onay aşamasında aynı evaluator'ın ApproveAsync/DispatchAsync yollarıyla çalışır
    ];

    public async Task<AiPolicyDecision> EvaluateToolCallAsync(AiPolicyRequest request, CancellationToken ct)
    {
        foreach (var step in steps)
        {
            var verdict = await step.EvaluateAsync(request, ct);
            if (!verdict.Allowed)
            {
                await securityEvents.RecordAsync(AiSecurityEvent.PolicyDenied(request, step.Name, verdict.Code), ct);  // ERP-000L kanalı (ön koşul)
                return AiPolicyDecision.Denied(step.Name, verdict.Code);       // kısa devre; provider "authorized" alanı hiçbir adımı geçemez
            }
            request = request with { Facts = request.Facts.With(verdict.Facts) };
        }
        return AiPolicyDecision.Allowed(request.Facts);
    }

    public Task<AiPolicyDecision> EvaluateApprovalAsync(AiApprovalRequest r, CancellationToken ct) => /* adım 11: actor, CompanyContext, capability, permission/scope/SoD, tool/policy sürümü, target/version/hash/expiry, reauth */ default!;
    public Task<AiPolicyDecision> EvaluateDispatchAsync(AiDispatchRequest r, CancellationToken ct)  => /* adım 12 ön koşulları: Approved, TargetLock yok, payload bütünlüğü; sahip komut expected version + idempotency ile çağrılır */ default!;
    // adım 13: audit sahip transaction'da (AiContext); ret/yetkisiz → security-event; transient → telemetry — AiOrchestrator/Dispatcher yönlendirir
}
```

Adım 5 mevcut fence deseniyle yazılır (KİLİTLİ-KAYNAK, `SalesDraftService.RequireAsync`): `ErpPermissions.IsKnown(permission)` değilse **503 `permission_unregistered`**; `activeRoleCodes.GetActiveRoleCodesAsync`; `authority.RevalidatePermissionScopeAsync(permission, companyId)`; `IsGranted && (IsCompanyWide || scope semantiği temsil edilebiliyor)`; temsil edilemeyen `SiteScoped/LocationScoped/WorkCenterScoped` → fail-closed `scope_not_representable`.

Adım 8 için veri sınıfı kararı `IAiModelRouter.Route(dataClassCeiling, capability)` ile aynı tabloyu kullanır (§11); `CokGizli` bulut rotasına **hiçbir koşulda** çıkmaz (derleme zamanı sabit; testte golden).

## 8. Bağlam kurucu ve ortak veri kesiti

### 8.1 `AiContextBuilder`

```csharp
internal sealed class AiContextBuilder(IAiToolCatalog catalog, IAiToolInvoker invoker, IAiDisclosureMatrix disclosure, TimeProvider clock) : IAiContextBuilder
{
    public async Task<AiContextEnvelope> BuildAsync(AiContextRequest request, AiToolContext toolContext, CancellationToken ct)
    {
        var plan = AiContextPlans.For(request.Focus);                   // ekran/kayıt/niyet → hangi READ araçları, hangi alan allowlist'i
        var sources = new List<AiContextSource>(); var missing = new List<string>();
        foreach (var item in plan.Sources)                               // model zarfı KURAMAZ, genişletemez; plan koddadır
        {
            if (!catalog.TryResolve(item.ToolId, item.Version, out var tool)) { missing.Add(item.ToolId); continue; }
            var outcome = await invoker.InvokeReadAsync(tool, toolContext, item.Args, ct);   // 13 adım her araç çağrısında
            if (outcome.Denied) { continue; }                            // izinsiz kaynak zarfa HİÇ girmez; "yok" sayılmaz, sadece yok
            if (outcome.Coverage.IsPartial) missing.AddRange(outcome.Coverage.Missing);
            sources.Add(new AiContextSource(item.ToolId, item.Version, outcome.SourceRefs, outcome.AsOf, item.FieldAllowlist,
                disclosure.Filter(outcome.Value, toolContext.Scope, item.Owner)));            // alan bazlı açıklık: Full / GateDecision / Hidden
        }
        var gates = sources.SelectMany(s => s.GateDecisions).ToArray();  // sahip modüllerin kapı kararları ayrı liste
        return new AiContextEnvelope(sources, gates, AiCoverage.Of(missing), Manifest: AiContextManifest.From(sources, clock.GetUtcNow()));
    }
}
```

- Zarf **manifesti** (`kaynak, sürüm, alan allowlist, as-of`) `ai.Runs.ContextManifestJson`'a yazılır; içerik yazılmaz.
- Bütçe: kaynak başına satır/karakter limiti descriptor'dan (`MaxCardinality`, `MaxOutputBytes`); aşımda "en ilgili N + sayım" ve `coverage.Partial`.
- Önbellek: kaynak bazlı, rowversion anahtarlı, şirket/aktör/kapsam sınırlı (`ERP-000J` §16); `IMemoryCache` anahtarı `{companyId}:{actorId}:{toolId}@{v}:{argsHash}:{rowVersion}`; yetki daralınca (`AuthorizationSnapshot.Revision` değişimi (`BuildingBlocks/Security/AuthorizationSnapshot.cs`)) tamamen geçersiz.

### 8.2 Ortak veri kesiti — mekanizma A ve B

**A — SNAPSHOT izolasyonlu tek okuma işlemi (bileşik READ/SIMULATE araçları):** mevcut host-runner deseniyle (Ek K-1 madde 11) birden fazla DbContext aynı `DbConnection`/`DbTransaction`'a enlist olur; işlem `IsolationLevel.Snapshot` ile açılır.

```csharp
// GoymenERP.Api/AiRuntime/AiReadSnapshotRunner.cs (ÖNERİ)
internal sealed class AiReadSnapshotRunner(OrganizationDbContext anchor /* bağlantı sahibi */, IServiceProvider services)
{
    public async Task<T> ReadAsync<T>(Guid companyId, Func<IAiReadParticipants, CancellationToken, Task<T>> read, CancellationToken ct)
    {
        await using var transaction = await anchor.Database.BeginTransactionAsync(IsolationLevel.Snapshot, ct);   // ALLOW_SNAPSHOT_ISOLATION doğrulanır (readiness probe)
        var connection = anchor.Database.GetDbConnection(); var tx = transaction.GetDbTransaction();
        await using var sales = await SalesReadParticipant.EnlistAsync(connection, tx, companyId, ct);           // UseSqlServer(connection, contextOwnsConnection:false) + UseTransactionAsync
        await using var finance = await FinanceReadParticipant.EnlistAsync(connection, tx, companyId, ct);
        await using var inventory = await InventoryReadParticipant.EnlistAsync(connection, tx, companyId, ct);
        try { return await read(new Participants(sales, finance, inventory), ct); }
        finally { await transaction.RollbackAsync(CancellationToken.None); }   // salt okuma; commit yok; hiçbir yazma yapılamaz (participant'lar salt-okunur port sunar)
    }
}
```

Katılımcı sınıflar sahip modüllerin **Application** projesinde `internal`→`public` okuma port'ları sunar (`InventoryDefiniteShipmentParticipant.EnlistAsync` deseni; salt-okunur varyant sahip dilimde yazılır — **DOĞRULANACAK**: bugün yalnız yazma katılımcıları var). `SNAPSHOT` izolasyonu veritabanı düzeyinde `ALLOW_SNAPSHOT_ISOLATION ON` ister; Identity'deki `SqlServerRcsiReadinessProbe` RCSI'yi doğrular, snapshot izolasyonu ayrıca doğrulanır (NS-AI-16; readiness probe genişletmesi K3).

**B — rowversion küme karşılaştırması (çok adımlı model döngüleri):** her araç çıktısındaki `SourceRef.Version` toplanır; döngü sonunda aynı kaynaklar yeniden okunmaz, yalnız **hafif** `IAiRowVersionProbe.ProbeAsync(refs)` ile mevcut sürümler çekilir (sahip port'unun `GetVersionsAsync` benzeri ucuz okuması — DOĞRULANACAK; yoksa aynı READ aracı tekrar çağrılır) ve **küme üyeliği** kontrol edilir: başlangıç kümesi ⊆ bitiş kümesi ve her üye aynı sürümde → tutarlı; değişen üye → yeniden oku veya `coverage.Stale(refs)` damgası. Küme üyeliği önemlidir: döngü sırasında **yeni** bir kayıt (örn. araya giren tahsis) başlangıç kümesinde olmadığı için sürüm karşılaştırmasına yakalanmaz; bu yüzden liste sorguları için `aggregateCount + maxRowVersion` çifti de manifestte tutulur ve karşılaştırılır (G-28).

---

## 9. Etki kuralı motoru (`Work.Rules`), kapı sorgusu ve kural-tabanlı iş öğesi

### 9.1 Motor: salt-okunur değerlendirme ile kalıcı takip ayrı yollardır (v1.1)

İki ayrı arayüz vardır; kapı sorgusu yalnız birincisini görür:

| Arayüz | Ne yapar | Bağımlılıkları | Kim çağırır |
|---|---|---|---|
| `IImpactRuleGate` | Kuralı hesaplar, sonucu **döndürür**; hiçbir şey yazmaz | Kural kataloğu + sahip sistem port'ları; **store, DbContext, iş öğesi komutu YOK** (yapıcıda bulunmaz) | Kapı sorgusu endpoint'i (§9.3) |
| `IImpactRuleTracker` | Aynı hesabı yapar, sonra Sorun + Değerlendirme + (gerekiyorsa) iş öğesi + deneme kaydını **tek veritabanı işleminde** kalıcılaştırır | `IImpactRuleGate` + `IWorkRulesStore` | Olay tüketicisi, zamanlı tarama, takip komutu (§9.4) |

```csharp
// GoymenERP.Modules.Work.Rules/ImpactRuleGate.cs (ÖNERİ) — saf hesap; yazma yapabilecek hiçbir bağımlılığı yok
internal sealed class ImpactRuleGate(IImpactRuleCatalog rules, IImpactRuleEvaluatorPort port, IWorkRulesPolicySnapshot policy) : IImpactRuleGate
{
    public async Task<IReadOnlyList<RuleEvaluationResult>> EvaluateAsync(RuleTrigger trigger, RuleContext context, CancellationToken ct)
    {
        var results = new List<RuleEvaluationResult>();
        foreach (var rule in rules.For(trigger))
        {
            var mode = policy.ModeOf(context.CompanyId, rule);                              // Disabled | Shadow | Active — bellekteki anlık görüntüden; DB'ye yazmaz
            if (mode == RuleMode.Disabled) continue;
            results.Add((await rule.EvaluateAsync(context, port, ct)) with { Mode = mode });  // hesap sahip SİSTEM port'unda (salt-okunur; §9.2)
        }
        return results;
    }
}

// GoymenERP.Modules.Work.Rules/ImpactRuleTracker.cs (ÖNERİ) — kalıcı takip; Sorun, Değerlendirme, görev açma/kapama ve deneme kaydı TEK işlemde
internal sealed class ImpactRuleTracker(IImpactRuleGate gate, IImpactRuleCatalog rules, IWorkRulesStore store, TimeProvider clock) : IImpactRuleTracker
{
    public async Task<RuleTrackResult> TrackAsync(RuleTrigger trigger, RuleContext context, WorkCommandMetadata metadata, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await TrackOnceAsync(trigger, context, metadata, ct); }
            catch (WorkStoreConflictException) when (attempt == 1)                          // 2601/2627 veya rowversion: TÜM save geri alındı
            {
                store.Reset();                                                                // ChangeTracker.Clear; taze durumla bir kez daha (kazananın görevi artık görünür)
            }
        }
    }

    private async Task<RuleTrackResult> TrackOnceAsync(RuleTrigger trigger, RuleContext context, WorkCommandMetadata metadata, CancellationToken ct)
    {
        if (await store.FindTrackReplayAsync(metadata, ct) is { } replay) return replay;      // aynı teslim / aynı key → ilk sonuç (work.Attempts)
        var now = clock.GetUtcNow();
        var outcomes = new List<RuleOutcome>();
        foreach (var result in await gate.EvaluateAsync(trigger, context, ct))
        {
            var rule = rules.Get(result.RuleId, result.RuleVersion);
            var key = rule.IssueKeyFor(context);
            var existing = await store.FindIssueAsync(context.CompanyId, key, ct);

            Issue issue; EvaluationOutcome outcome;
            if (existing is null)
            {
                if (!result.ConditionHolds || result.IsPartial) { outcomes.Add(RuleOutcome.NothingToTrack(rule, key, result)); continue; }   // koşul yoksa satır YOK
                (issue, outcome) = Issue.Open(context.CompanyId, key, result, now);
            }
            else { issue = existing; outcome = issue.Apply(result, now); }                   // ÖNCE sonuç hesaplanır…

            var evaluation = Evaluation.From(issue, result, outcome, trigger, context, now); // …SONRA sonuç append-only değerlendirmeye yazılır

            var openItem = await store.FindOpenWorkItemAsync(key.ToDedupKey(), ct);
            WorkItem? created = null;
            if (openItem is not null && !issue.AwaitsDecision)                                // Cleared veya Acknowledged: bekleyen görev kanıtla KAPANIR
            {
                openItem.Close(ClosureEvidence.FromRule(issue.ResolvedByRef ?? issue.Decision?.ExceptionRef ?? $"eval:{evaluation.Id:N}", evaluation.Id));
            }
            else if (openItem is null && issue.AwaitsDecision && rule.Effect.HasFlag(EffectKind.WorkItem) && result.Mode == RuleMode.Active)
            {
                // DEĞİŞMEZ (sonuç türünden bağımsız): karar bekleyen Sorunun açık görevi yoksa görev BU işlemde oluşur.
                var escalation = await store.GetEscalationPolicyAsync(rule.WorkItem.EscalationPolicyId, ct);
                created = WorkItem.FromRule(context.CompanyId, issue, evaluation.Id, rule.WorkItem, rule.Disclosure, escalation, now);
            }
            store.Stage(issue, evaluation, openItem, created);                                    // + WorkItemEvent (Created/Closed)
            outcomes.Add(new RuleOutcome(rule.RuleId, rule.Version, key, outcome, result.Gate, rule.Disclosure, created?.Id));
        }
        store.StageAttempt(metadata, outcomes);                                                  // work.Attempts: (CompanyId, "work.rules.track.v1", keyHash)
        await store.CommitAsync(ct);                                                             // TEK SaveChanges → tek SQL işlemi; audit aynı save'de (ADR-009)
        return new RuleTrackResult(outcomes, IsReplay: false, metadata.CorrelationId);
    }
}
```

- **Tek işlem:** `Issue`, `Evaluation`, açılan/kapanan `WorkItem`, `WorkItemEvent` ve `Attempt` aynı `WorkDbContext`'tedir (hepsi Work modülünün tablosu); tek `SaveChangesAsync` hepsini birlikte yazar ya da hiçbirini yazmaz. Başka modüle yazma yoktur (AGENTS.md).
- **Görev açma VE kapama aynı işlemde:** karar verilince (`Acknowledged`) veya koşul kalkınca (`Cleared`) açık görev kanıtla kapanır; bu yüzden eski bir açık görev, istisna tükenince açılması gereken **yeni** karar görevini `UX_WorkItems_OpenDedupKey` üzerinden engellemez.
- **Eşzamanlılık:** iki değerlendirici aynı anda görev açmaya kalkarsa `UX_WorkItems_OpenDedupKey` ikinciyi 2601/2627 ile reddeder; **bütün save geri alınır**, tracker durumu sıfırlayıp bir kez daha dener ve bu kez kazananın görevini görür (ikinci görev açılmaz).
- **Takip kimliği:** olay tetiklemede key `rule:{deliveryId}`, zamanlı taramada `sweep:{ruleId}:{slot}:{issueKey}`, takip komutunda istemcinin `Idempotency-Key`'i; hepsi `work.Attempts`'a yazılır.
- **Gölge mod:** kural `Shadow` modundaysa (veya `Work:Rules:ShadowOnly=true`) Sorun ve Değerlendirme kaydedilir, görev **açılmaz**. Gölge kapatılınca toplu görev üretimi yapılmaz; görev, aktivasyondan sonraki ilk değerlendirmede (olay veya zamanlı tarama) oluşur. İlk taramada kaç görev açılacağı yönetim ekranında (AI-03 / Work yönetimi) önizlenir.
- **Kural politikası okuması yazma değildir:** `IWorkRulesPolicySnapshot`, `work.ImpactRules` durumlarını **salt-okunur** bir bağlamdan (`WorkReadOnlyDbContext`: `NoTracking`, `SaveChanges*` fırlatır — mevcut `CommandExecutionDbContext` deseni, Ek K-1 madde 14) okuyup bellekte tutar; kapı yolu bu anlık görüntüyü kullanır.

- Motor **blok üretmez**: `EffectKind` içinde `Block` üyesi **yoktur** (derleme zamanı garanti).
- AI kapalıyken çalışır: `Work.Rules` hiçbir `Ai.*` türüne referans vermez; capability/kill switch bayrakları `Ai:*` altındadır, `Work:Rules:Enabled` ayrıdır (§19).

### 9.2 `RulesRuntime` host adaptörü (sistem aktörü)

```csharp
// GoymenERP.Api/RulesRuntime/Evaluators/FinanceRiskEvaluatorAdapter.cs (ÖNERİ)
internal sealed class FinanceRiskEvaluatorAdapter(IServiceScopeFactory scopes) : IImpactRuleEvaluatorPort
{
    public async Task<RiskEvaluationV1> EvaluateRiskAsync(Guid companyId, Guid partyAccountId, StageTransitionV1? transition, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(companyId);   // write-once; middleware yok
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(Guid.NewGuid().ToString("N"), null, SourceChannels.System, null, "rules-runtime");
        var risk = scope.ServiceProvider.GetRequiredService<IFinanceRiskEvaluationV1>();                     // W4-RISK port'u (yok — DOĞRULANACAK)
        return await risk.EvaluateAsSystemAsync(partyAccountId, transition, ct);                              // sahip modül "sistem aktörü" yolunu kendi tanımlar
    }
}
```

Sahip modülün "sistem aktörü ile değerlendirme" yolu **sahip dilimde** tanımlanır (kesin kural kontrolleri gibi); `RulesRuntime` hiçbir izni atlamaz, sahip modülün açıkça sunduğu sistem port'unu çağırır. Bu port **salt-okunur** olmak zorundadır (SIMULATE/READ sınıfı; `ERP-000J` §7: iş tablosu, olay, outbox veya komut kaydı üretmez); kapı sorgusu ve tracker aynı port'u kullanır. Bu port'ların listesi Ek K-3'te "sahip dilim eki" olarak işaretlidir.

Neden ayrı sistem port'u: mevcut sahip servislerin fence'i (`SalesDraftService.RequireAsync` deseni) `ICurrentUser`'ı okur; host'ta `ICurrentUser` → `HttpCurrentUser` (`IHttpContextAccessor`) kayıtlıdır (`Program.cs`, `Security/HttpCurrentUser.cs`); HTTP bağlamı olmayan bir scope'ta `IsAuthenticated=false` → fence 401 `unauthenticated` döner. Dolayısıyla worker/kural scope'u sahip modülün **kullanıcı** port'unu çağıramaz; ya sahip modülün sistem port'unu çağırır (bu bölüm) ya da gerçek bir kullanıcı adına çalışır ve `ICurrentUser` scope-yerel override edilir (§10.3, §14.3).

### 9.3 Kapı sorgusu endpoint'i (on-demand; form içinden; salt-okunur)

```csharp
// GoymenERP.Api/RulesRuntime/RulesEndpoints.cs (ÖNERİ)
var group = endpoints.MapGroup("/api/v1/rules").WithTags("Work").RequireAuthorization().RequireCompanyContext();
group.MapPost("/gate", async (GateQueryRequestV1 body, IImpactRuleGate gate, IGateQueryAuthorizer auth, HttpContext http, CancellationToken ct) =>
{
    SetPrivateNoStore(http.Response);
    var actor = await auth.AuthorizeAsync(body.GateToolId, ct);          // örn. finance.risk.shipment-gate@1 → finance.risk.gate.read (ÖNERİ); aksi 403 (nondisclosing)
    if (!actor.Granted) return Results.Problem(statusCode: 403);
    var results = await gate.EvaluateAsync(RuleTrigger.OnDemand(body.IntentKind), RuleContext.From(body, actor), ct);   // saf hesap: YAZMA YOK
    return Results.Ok(GateQueryResponseV1.From(results, actor.DisclosureLevel));                                   // yalnız kapı kararı + izinli gerekçe kodları
})
.WithName("QueryRuleGateV1").Accepts<GateQueryRequestV1>("application/json").Produces<GateQueryResponseV1>(200)
.ProducesErpBadRequest("validation_failed", "gate_tool_unknown").ProducesErpUnsupportedMediaType().ProducesErpAuthenticatedProblems();
```

Kapı sorgusu **hiçbir kayıt yazmaz** — değerlendirme kaydı dahil (v1.0'daki "kanıt kaydı" seçeneği kaldırıldı; K-AÇIK-06 bu öneriyle kapanır, onay ADR-008 madde 6'da). Endpoint'in DI grafiğinde yazma yapabilen hiçbir bileşen bulunmaz: `IImpactRuleTracker`, `IWorkRulesStore`, yazılabilir `WorkDbContext` ve `IWorkItemCommandsV1` yoktur; kural durumu yalnız salt-okunur `WorkReadOnlyDbContext` üzerinden gelen anlık görüntüden okunur. Sahip modülün değerlendirme port'u da **salt-okunur sistem port'u** olmak zorundadır (§9.2). Kanıt iki katmanlıdır: mimari test (`GateQueryHasNoWriteDependenciesTests`: grafikte yazma bağımlılığı yok) ve davranış testi — `IServiceScopeFactory` üzerinden çözülen sahip port'larını da kapsamak için statik grafik yetmez; kapı sorgusundan önce ve sonra **bütün şemalardaki** satır sayıları ve rowversion'lar, audit, outbox ve security-event tabloları dahil, aynı kalmalıdır.

### 9.4 Kalıcı takip komutu (form kaydında veya olayda)

Form, kaydı başarıyla yaptıktan sonra (örn. yükleme talimatı kaydedildi veya "issue" niyeti gönderildi) **ayrı** bir komut çağırır; `ERP-000F` olayları geldiğinde aynı iş olay tüketicisinden yapılır:

| Yöntem ve yol | İzin (ÖNERİ) | Idempotency-Key | Cevap |
|---|---|---|---|
| `POST /api/v1/rules/evaluations` | `work.rules.track` — formu kullanan operasyon rolüne verilir; **finans verisi görme izni değildir**, yalnız "takibi başlat" iznidir | Zorunlu | 201: kapı kararı + "takip açıldı" bayrağı + görevin kimliği (kullanıcının açıklık düzeyinde; tutar yok) |

Bu komut `IImpactRuleTracker.TrackAsync` çağırır (§9.1): Sorun, Değerlendirme ve Finans görevi aynı işlemde yazılır. Sevkiyat kullanıcısının ekranındaki "Finans'a görev açıldı" metni **yalnız** bu komutun 201 cevabından sonra görünür.

---

## 10. Öneri → onay → gönderim → kurtarma

### 10.1 `AiPreviewService` (kanonikleştirme ve hash)

```csharp
internal static class AiCanonicalJson
{
    // Sabit seçenekler: alfabetik özellik sırası, camelCase, kültürden bağımsız decimal, UTC ISO-8601, boşluksuz, trailing zero yok
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false, /* + sıralı sözlük converter'ı */ };
    public static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();   // 64 küçük harf hex
}

/// <summary>Önizleme zarfı = hash kapsamının TAMAMI. Vault'a bu zarfın baytları yazılır; dispatcher aynı baytları yeniden hash'ler,
/// zarfı Proposal satırıyla karşılaştırır ve yalnız Payload'ı sahip komuta verir. Model açıklaması zarfta YOKTUR (ERP-000J §10.1).</summary>
internal sealed record AiPreviewEnvelope(
    string OwnerCommandContract, string ToolId, int ToolVersion, Guid CompanyId,
    string TargetEntityType, Guid? TargetId, long? ExpectedVersion,
    IReadOnlyDictionary<string, int> PolicyRefs,            // toolCatalog / promptPolicy / riskThresholds … sürümleri
    JsonElement Payload)                                    // kanonik komut gövdesi (strict schema → kanonik nesne)
{
    public bool Matches(AiProposal p) => OwnerCommandContract == p.OwnerCommandContract && ToolId == p.ToolId && ToolVersion == p.ToolVersion
        && CompanyId == p.CompanyId && TargetEntityType == p.TargetEntityType && TargetId == p.TargetId && ExpectedVersion == p.ExpectedVersion;
    public static AiPreviewEnvelope Read(byte[] bytes) => JsonSerializer.Deserialize<AiPreviewEnvelope>(bytes, AiCanonicalJson.Options)
        ?? throw new InvalidOperationException("Önizleme zarfı okunamadı.");
}

internal sealed class AiPreviewService(IAiProposalStore proposals, IAiPayloadVault vault, IAiToolCatalog catalog, IAiPolicyRefs policyRefs,
    IOptions<AiOptions> options, TimeProvider clock) : IAiPreviewService
{
    public async Task<AiPreview> CreateAsync(AiWriteIntent intent, AiToolContext context, CancellationToken ct)
    {
        var registration = catalog.Resolve(intent.ToolId, intent.Version);                     // WRITE sınıfı, Approval != None
        var canonical = registration.CanonicalizeArgs(intent.Args);                             // strict schema → kanonik JsonElement
        var validation = await registration.ValidateAsync(context, canonical, ct);              // sahip validator; geçmezse Proposal Draft (onaylanamaz)
        var envelope = new AiPreviewEnvelope(registration.OwnerContractId, registration.Descriptor.ToolId, registration.Descriptor.Version,
            context.CompanyId, intent.Target.EntityType, intent.Target.Id, intent.Target.ExpectedVersion, policyRefs.Current(registration), canonical);
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, AiCanonicalJson.Options);
        var hash = AiCanonicalJson.HashBytes(envelopeBytes);                                    // hash = zarfın tamamı
        var cipherRef = await vault.PutAsync(context.CompanyId, envelopeBytes, ct);             // vault AYNI baytları saklar (şifreli, immutable)
        var shadow = options.Value.Tools.IsShadowOnly(registration.Descriptor);
        var proposal = intent.ReissueOf is { } prior
            ? (await proposals.GetAsync(prior, ct)).Reissue(hash, intent.Target, validation.Passed, shadow, clock.GetUtcNow(), options.Value.Preview.Expiry)
            : AiProposal.StartIntent(context.CompanyId, context.RunId, registration.Ref, intent.Target, hash, intent.Actors,
                registration.RequiredApprovals(context), validation.Passed, shadow, clock.GetUtcNow(), options.Value.Preview.Expiry);
        await proposals.AddAsync(proposal, cipherRef, validation, ct);                           // ai.Proposals (+ audit: "ai.proposal.created.v1", AiInvolvement=Proposed)
        // Reissue'da store İKİ save'i TEK işlemde yapar: önce eski satır Superseded (UPDATE), sonra yeni satır (INSERT).
        // EF filtreli tekil indeksi bilmez; tek save'de INSERT önce giderse UX_Proposals_OneLiveRevisionPerIntent 2601 verir.
        return AiPreview.From(proposal, validation, diff: registration.Diff(canonical, intent.Target),   // gösterilen fark == yürütülecek payload
            similar: await proposals.FindSimilarAsync(context.CompanyId, registration.Descriptor.ToolId, hash, since: clock.GetUtcNow().AddDays(-1), ct));
    }
}
```

Hash kapsamı (kod ile metin aynı): önizleme zarfının tamamı — kanonik payload + `OwnerCommandContract` + araç kimliği/sürümü + şirket + hedef kimliği/beklenen sürüm + politika sürümleri (`PolicyRefs`); model açıklaması **kapsam dışı** (`ERP-000J` §10.1). Gönderim öncesi politika adım 12 (`EvaluateDispatchAsync`) zarftaki `PolicyRefs`'i güncel sürümlerle karşılaştırır; fark varsa gönderim yapılmaz, Proposal `Stale` olur (gönderim öncesi). Aynı içerikli açık/son yürütülmüş öneri bilgisi `FindSimilarAsync` ile önizlemede **bilgi** satırıdır ("Aynı içerikte 1 öneri 2 saat önce yürütüldü: …"); engel değil.

### 10.2 `AiApprovalBinding` ve onay endpoint'i

Onay isteği `POST /api/v1/ai/proposals/{id}/approvals` gövdesinde `{ boundHash, boundTargetVersion, decision, reauthRef? }` taşır; sunucu `EvaluateApprovalAsync` (adım 11) → `AiProposal.Approve(...)` → `AiProposalStore.TransitionAsync` (rowversion, tek kazanan) → `ai.Approvals` kanıt satırı + audit (`ai.proposal.approved.v1`, `ApprovalContextJson {proposedBy, approvedBy}`) **tek `SaveChanges`**. Kaybeden `409 version_conflict`; `BindingMismatch` → `409 approval_binding_mismatch` + yeni önizleme yönlendirmesi; `SegregationOfDuties` → `403` (nondisclosing kod: platform yazar). Zımni sohbet onayı, "onaylandı" model ifadesi kabul edilmez: onay yalnız bu endpoint'ten, gerçek kullanıcı kimliğiyle.

### 10.3 `AiCommandDispatcher` ve `AiCommandEnvelopeFactory`

```csharp
// GoymenERP.Modules.Ai.Application/Dispatch/AiEnvelopeVerifier.cs (ÖNERİ) — gönderim VE kurtarma bu tek kontrolü çağırır
internal sealed class AiEnvelopeVerifier(IAiPayloadVault vault)
{
    public async Task<AiVerifiedEnvelope?> LoadVerifiedAsync(AiProposal proposal, CancellationToken ct)
    {
        var bytes = await vault.GetAsync(proposal.CompanyId, proposal.PayloadCipherRef, ct);
        if (!string.Equals(AiCanonicalJson.HashBytes(bytes), proposal.CanonicalPayloadHash, StringComparison.Ordinal)) return null;   // hash
        var envelope = AiPreviewEnvelope.Read(bytes);
        return envelope.Matches(proposal) ? new AiVerifiedEnvelope(envelope) : null;                                                 // sözleşme/araç/şirket/hedef/sürüm
    }
}

// GoymenERP.Modules.Ai.Application/Dispatch/AiCommandDispatcher.cs (ÖNERİ)
internal sealed class AiCommandDispatcher(IAiProposalStore proposals, AiEnvelopeVerifier verifier, IAiToolCatalog catalog, IAiPolicyEvaluator policy,
    IAiDispatchScopeFactory scopes, IAiSecurityEventSink security, TimeProvider clock) : IAiCommandDispatcher
{
    public async Task<AiDispatchResult> DispatchAsync(Guid proposalId, CancellationToken ct)
    {
        var proposal = await proposals.GetAsync(proposalId, ct);
        if (proposal.Status != AiProposalStatus.Approved) return AiDispatchResult.Refused(AiProposalCodes.NotApproved);   // yalnız Approved gönderilir
        if (clock.GetUtcNow() >= proposal.ExpiresAtUtc)                                          // onay süresi geçti → gönderim öncesi Stale
        { proposal.MarkStale("expired"); await proposals.SaveAsync(proposal, ct); return AiDispatchResult.Refused(AiProposalCodes.ProposalExpired); }
        var gate = await policy.EvaluateDispatchAsync(AiDispatchRequest.For(proposal), ct);      // adım 12: güncel yetki/politika/hedef sürümü
        if (!gate.Allowed)
        {
            if (gate.IsDrift) { proposal.MarkStale(gate.Code); await proposals.SaveAsync(proposal, ct); }   // politika/hedef değişti → Stale (tekrar gönderilemez)
            else { proposal.FailBeforeDispatch(gate.Code); await proposals.SaveAsync(proposal, ct); }        // yetki düştü vb. → gönderim öncesi kesin ret
            return AiDispatchResult.Refused(gate.Code);
        }

        // GÖNDERİM ÖNCESİ: öneri hâlâ Approved. Bozuk zarf → FailBeforeDispatch (Approved → Failed; hiçbir şey gönderilmedi; kilit yok).
        if (await verifier.LoadVerifiedAsync(proposal, ct) is not { } verified)
        {
            proposal.FailBeforeDispatch(AiProposalCodes.PayloadIntegrity);
            await proposals.SaveAsync(proposal, ct);
            await security.RecordAsync(AiSecurityEvent.PayloadIntegrity(proposal, phase: "pre-dispatch"), ct);
            return AiDispatchResult.Failed(AiProposalCodes.PayloadIntegrity);
        }

        proposal.MarkDispatched(clock.GetUtcNow());                                              // Approved → Dispatched; DispatchedPayloadHash dondurulur; TargetLock
        await proposals.SaveAsync(proposal, ct);                                                 // kalıcı kayıt ÖNCE (çökme → süre aşımında Unknown'a alınıp kurtarılır)
        return await ExecuteAsync(proposal, verified, ct);
    }

    /// <summary>Gönderim ve kurtarmanın ORTAK yürütme adımı: aynı doğrulanmış zarf, aynı key, aynı hata sınıflaması.</summary>
    internal async Task<AiDispatchResult> ExecuteAsync(AiProposal proposal, AiVerifiedEnvelope verified, CancellationToken ct)
    {
        await using var scope = scopes.CreateDispatchScope(proposal);                            // yeni DI scope: ICompanyContext, ICorrelationContext(AiAssisted), ICurrentUser(onaylayan)
        var registration = catalog.Resolve(proposal.ToolId, proposal.ToolVersion);
        try
        {
            var result = await registration.ExecuteWriteAsync(scope.ToolContext, verified.Envelope.Payload, ct);   // key = context.Dispatch.IdempotencyKey
            proposal.MarkExecuted(result.CommandRef);                                              // replay da Executed sayılır (IsReplay=true kaydedilir)
        }
        catch (AiOwnerRefusalException refusal) when (refusal.IsSameKeyMismatch)                   // 409 same_key_payload_mismatch / idempotency_conflict
        { proposal.MarkFailed(AiProposalCodes.ContentConflict); await security.RecordAsync(AiSecurityEvent.ContentConflict(proposal, refusal), ct); }
        catch (AiOwnerRefusalException refusal)                                                    // kesin 4xx (validation, version_conflict, forbidden…)
        { proposal.MarkFailed(refusal.Code); }
        catch (Exception ex)                                                                       // sahibin kesin reddi DIŞINDAKİ her istisna: sonuç bilinmiyor
        { proposal.MarkUnknown(ex.GetType().Name); }                                               // OTOMATİK RETRY YOK; kurtarma koordinatörü
        try { await proposals.SaveAsync(proposal, ct); }                                           // ai.Proposals + audit (AiInvolvement=Executed / Failed / Unknown)
        catch (AiProposalConcurrencyException)                                                     // bu arada kurtarma öneriyi Unknown'a almış olabilir (uzun süren gönderim)
        {
            var fresh = await proposals.ReloadAsync(proposal.Id, ct);                              // taze satır üzerine BİLİNEN sonucu uygula: Executed/Failed Unknown'dan kabul edilir
            fresh.ApplyLateOutcome(proposal.Status, proposal.ExecutedCommandRef, proposal.FailureCode);
            await proposals.SaveAsync(fresh, ct);
            proposal = fresh;
        }
        return AiDispatchResult.From(proposal);
    }
}
```

```csharp
// GoymenERP.Api/AiRuntime/AiDispatchScopeFactory.cs (ÖNERİ)
internal sealed class AiDispatchScopeFactory(IServiceScopeFactory scopes) : IAiDispatchScopeFactory
{
    public AiDispatchScope CreateDispatchScope(AiProposal proposal)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(proposal.CompanyId);                                           // write-once
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
        correlation.Set(correlationId: $"airun-{proposal.PreparedByRunId:N}", causationId: proposal.Id.ToString("N"),
            sourceChannel: SourceChannels.AiAssisted, clientIpAddress: null, userAgent: "goymen-ai-runtime");                          // altı modül kanalı buradan türetir (Ek K-1 madde 7/10)
        // Sahip modül fence'i ICurrentUser okur; HTTP bağlamı yok → scope-yerel aktör: ONAYLAYAN (maker-checker deseni).
        scope.ServiceProvider.GetRequiredService<IScopedActorOverride>().Set(ScopedActor.Approver(proposal.ApprovedByActorId!, proposal.CompanyId));
        var dispatch = new AiDispatchInfo(proposal.IdempotencyKey, proposal.OperationIntentId, proposal.Id, proposal.ApprovalIds);
        return new AiDispatchScope(scope, new AiToolContext(proposal.CompanyId, proposal.ApprovedByActorId!, Scope: PermissionScope.Denied /* sahip fence'i kendi çözer */,
            AiActorMode.OnBehalfOfActor, proposal.PreparedByRunId, StepSeq: 0, AiDataClass.Gizli, correlation.CorrelationId, AiToolBudget.None, dispatch));
    }
}

// GoymenERP.Api/Security/ScopedCurrentUser.cs (ÖNERİ; K3 — platform kimlik kaydı değişir)
// Program.cs: AddScoped<HttpCurrentUser>(); AddScoped<IScopedActorOverride, ScopedActorOverride>(); AddScoped<ICurrentUser, ScopedCurrentUser>();  (bugün: AddScoped<ICurrentUser, HttpCurrentUser>() — L105)
internal sealed class ScopedCurrentUser(HttpCurrentUser http, IScopedActorOverride actorOverride, IIdentityCompanyAccessReader companyAccess) : ICurrentUser
{
    public bool IsAuthenticated => actorOverride.Actor is { } actor ? !string.IsNullOrWhiteSpace(actor.UserId) : http.IsAuthenticated;
    public string? UserId => actorOverride.Actor?.UserId ?? http.UserId;
    public IReadOnlySet<Guid> AllowedCompanyIds => actorOverride.Actor is { } actor
        ? companyAccess.GetAllowedCompanyIds(actor.UserId)            // claim yok; Identity yetki kaynağından GÜNCEL okunur (DOĞRULANACAK: bu port bugün yok)
        : http.AllowedCompanyIds;
    public bool CanAccessCompany(Guid companyId) => companyId != Guid.Empty && AllowedCompanyIds.Contains(companyId);
}

internal sealed class AiCommandEnvelopeFactory(ICorrelationContext correlation, TimeProvider clock)
{
    public SalesCommandMetadata ForDispatch(AiToolContext context)
        => new(context.Dispatch?.IdempotencyKey ?? throw new InvalidOperationException("WRITE yalnız dispatch bağlamında."),   // "ai:{OperationIntentId:N}"
               correlation.CorrelationId, correlation.CausationId, clock.GetUtcNow());                                             // SalesCommandMetadata(IdempotencyKey, CorrelationId, CausationId, RequestedAtUtc)
    // Counterparty/Organization/Inventory için SourceChannel parametreli metadata: SourceChannels.AiAssisted çağrı başına verilir (Ek K-1 madde 7/10).
    // AiContext (yeni opsiyonel alan — K3 sözleşme eki, Faz 2): { involvement: Executed, runId, toolId, toolVersion, promptPolicyVersion, modelDeploymentRef,
    //   previewHash, proposalId, operationIntentId, approvalIds[], requestedBy, preparedByRun, approvedBy, executedBy, onBehalfOf } → audit AiInvolvement + AiContextJson
}
```

- `ActorId` = **onaylayan** (Finance/Accounting maker-checker deseni); `requestedBy/preparedByRun/onBehalfOf` `AiContext`'te ayrı alanlardır.
- **Neden `ICurrentUser` override'ı:** mevcut sahip servislerin fence'i (`RequireAsync`) `ICurrentUser`'ı okur ve host'ta `ICurrentUser` → `HttpCurrentUser` (`Program.cs` L105) claim'lerden okur; dispatch/worker scope'unda HTTP bağlamı olmadığı için override olmadan her fence 401 döner. Override yalnız `AiDispatchScopeFactory`, `RulesRuntime` ve worker scope fabrikaları tarafından set edilebilir (architecture testi `ScopedActorOverrideIsOnlySetByRuntimeFactories`); yetki **genişletmez** — sahip fence'i onaylayanın **güncel** izinlerini `RevalidatePermissionScopeAsync` ile dispatch anında yeniden doğrular (onaydan sonra yetkisi düşmüşse gönderim reddedilir). Kanıt: `ErpAuthorizationService` (üç port'un tek uygulaması) aktörü `ICurrentUser`'dan okur — `IsAuthenticated`, `CanAccessCompany(companyId)`, `UserId` (`Identity.Infrastructure/Services/ErpAuthorizationService.cs` L23, L282–285); dolayısıyla scope-yerel `ICurrentUser` override'ı yetki okumasına doğrudan akar. Bu değişiklik platform kimlik kaydını değiştirdiği için K3'tür.
- Gateway `CommandExecutions`/sahip tablolarına yazmaz; sahip komutunun kendi attempt/command satırı tek etkiyi garanti eder (kanıt §10.6).

### 10.4 `AiRecoveryCoordinator` (otomatik retry yok; yeni kimlik yok)

```csharp
internal sealed class AiRecoveryCoordinator(IAiProposalStore proposals, IAiToolCatalog catalog, AiEnvelopeVerifier verifier, AiCommandDispatcher dispatcher,
    IWorkItemCommandsV1 workItems, IAiSecurityEventSink security, IOptions<AiOptions> options, TimeProvider clock) : IAiRecoveryCoordinator
{
    /// <summary>Worker başlangıcında ve zamanlı: süresi geçmiş Dispatched ve Unknown öneriler. Aynı OperationIntentId; asla yeni kimlik.</summary>
    public async Task RecoverAsync(Guid companyId, CancellationToken ct)
    {
        foreach (var proposal in await proposals.ListPendingRecoveryAsync(companyId, olderThan: options.Value.Workflow.DispatchGracePeriod, ct))
        {
            // 0) Yarım kalmış gönderim: Dispatched iken süre aşıldıysa önce AÇIKÇA Unknown'a alınır (MarkRecoveryAttempt yalnız Unknown kabul eder).
            if (proposal.Status == AiProposalStatus.Dispatched) { proposal.MarkUnknown("dispatch_grace_expired"); await proposals.SaveAsync(proposal, ct); }

            var registration = catalog.Resolve(proposal.ToolId, proposal.ToolVersion);
            // 1) Salt-okunur kurtarma sorgusu — sahip modül bir "deneme kaydı okuma" port'u sunuyorsa (ÖNERİ, sahip dilim eki: IIdempotencyProbeV1) önce o.
            var probe = registration.Probe is { } p ? await p.FindAsync(companyId, registration.OwnerContractId, proposal.IdempotencyKeyHash, ct) : ProbeResult.Unsupported;
            if (probe.Kind == ProbeKind.Recorded) { proposal.MarkExecuted(probe.CommandRef!); await proposals.SaveAsync(proposal, ct); continue; }
            if (probe.Kind == ProbeKind.RecordedRefusal) { proposal.MarkFailed(probe.Code!); await proposals.SaveAsync(proposal, ct); continue; }

            // 2) "Kayıt yok" ≠ "hiç ulaşmadı". EN FAZLA BİR aynı-key gönderim — ama ÖNCE gönderimle AYNI zarf kontrolü.
            if (proposal.RecoveryAttempts >= 1) { await OpenTakeoverAsync(proposal, reason: "second_uncertainty", ct); continue; }   // Unknown + kilit kalır
            if (await verifier.LoadVerifiedAsync(proposal, ct) is not { } verified)
            {
                // İlk gönderim yapılmış olabilir; bozuk zarfla tekrar gönderilmez, "Failed" da denmez (sonuç bilinmiyor).
                await security.RecordAsync(AiSecurityEvent.PayloadIntegrity(proposal, phase: "recovery"), ct);
                await OpenTakeoverAsync(proposal, reason: "payload_integrity", ct);                  // Unknown + TargetLock kalır; insan devralır
                continue;
            }
            proposal.MarkRecoveryAttempt(clock.GetUtcNow());                                         // Unknown önkoşulu; RecoveryAttempts 0→1 (CK_Proposals_Recovery)
            await proposals.SaveAsync(proposal, ct);                                                   // rowversion: iki koordinatör aynı anda deneyemez
            await dispatcher.ExecuteAsync(proposal, verified, ct);                                     // gönderimle AYNI yürütme ve hata sınıflaması
        }
    }

    private Task OpenTakeoverAsync(AiProposal proposal, string reason, CancellationToken ct) =>
        workItems.CreateFromSystemAsync(WorkItemRequests.RecoveryTakeover(proposal, reason),
            WorkCommandMetadata.System($"recovery:{proposal.Id:N}:{reason}", proposal.CorrelationId, null), ct);   // aynı key → replay; tek görev
}
```

Sözleşme (ADR-008 madde 6'ya öneri): (1) kurtarma yalnız aynı `OperationIntentId` ve aynı dondurulmuş payload ile; (2) "sahip modülde kayıt yok" **hiç ulaşmadı anlamına gelmez** — bu yüzden koşulsuz ikinci deneme yerine **en fazla bir** aynı-key gönderim ve sahip modülün PK garantisine yaslanma; (3) ikinci belirsizlik → `Unknown` + `TargetLock` + iş öğesi (AI süreç sahibi + sahip modül operatörü); (4) insan devralma sonucu (`Executed`/`Failed`) `POST /ai/proposals/{id}/resolution` ile **kanıt referansı** zorunlu girilir; (5) hiçbir yolda yeni kimlik üretilmez, `Reissue` `Unknown` için çalışma zamanında reddedilir (§5.1); (6) "en fazla bir" gönderim hem kodda (`MarkRecoveryAttempt` önkoşulu) hem şemada (`CK_Proposals_Recovery`) hem eşzamanlılıkta (rowversion) zorlanır; (7) gönderim ve kurtarma **aynı** `AiEnvelopeVerifier` ve **aynı** `ExecuteAsync` adımını kullanır — zarf kontrolü kurtarmada da çağrıdır, yorum değil; (8) gönderim öncesi ret (`FailBeforeDispatch`, `Approved → Failed`, kilit yok) ile gönderim sonrası belirsizlik (`Dispatched → Unknown`) ayrı geçişlerdir; süresi geçmiş `Dispatched` kurtarmaya alınmadan önce açıkça `Unknown` yapılır.

### 10.5 Kaydedilen senaryolar (test sözleşmesi, §18.2)

| Senaryo | Beklenen (şema + metin aynı davranışı tarif eder) |
|---|---|
| **Aynı içerikte iki meşru sipariş:** kullanıcı iki kez "sipariş taslağı oluştur" niyeti başlatır; içerik birebir aynı | İki `OperationIntentId`, iki key, iki `sales.DraftAttempts` satırı, iki taslak; ikinci önizlemede "aynı içerikli 1 öneri az önce yürütüldü" bilgisi; engel yok; sahip modül mükerrer uyarısı varsa bulgu olarak gösterilir |
| **Belirsiz sonucun tekrar gönderilmesi:** dispatch transport hatası; sahip modül aslında kaydetmiş | Proposal `Unknown`; koordinatör aynı key/aynı payload ile bir kez gönderir → sahip `IsReplay=true` + ilk correlation → `Executed`; ikinci kayıt yok; audit'te ikinci başarılı olay yok |
| **Belirsiz sonucun tekrar gönderilmesi (ulaşmamış):** sahip modülde kayıt yok | Aynı gönderim ilk kez yürütür → `Executed`; toplam tek kayıt |
| **İkinci belirsizlik** | `Unknown` kalır; `TargetLock`; `work.WorkItems` "kurtarma devralma" açık; aynı hedef için yeni niyet `intent_pending_recovery` (409) |
| **Gönderimde sahip `version_conflict`** (onaydan sonra hedef değişti) | Proposal `Failed(version_conflict)`; bilinen sonuç, etki yok, kilit yok; aynı niyet yeniden önizlenemez (sahip reddi aynı key'e terminal kaydedilmiş olabilir); kullanıcı **yeni niyetle** yeniden önizler. Onaydan **önce** fark edilen sürüm değişikliği `Stale` → aynı niyet rev+1 |
| **İki onay gerektiren işlem** | 1. onay → `PartiallyApproved` (kanıt satırı); aynı kişi 2. onay → `403`; farklı kişi → `Approved`; iki onay arasında hedef sürümü değişti → `Stale`, iki onay da düşer; `Reissue` aynı niyet rev+1 |
| **Yalnız bir sevkiyata verilen istisna** | Müşterinin risk Sorunu **açık kalır** (`Acknowledged`, istisna referansıyla; limit aşımı sürdükçe `Resolved` olmaz); kapı `Proceed(exceptionRef)` yalnız o talimat için; istisna değerlendirme görevi kanıtla kapanır; sevkiyat kaydedilince istisna `Consumed` → aynı müşteriye ikinci talimat `ReviewRequired`, Sorun `Reopened` ve yeni karar görevi; süre dolunca aynısı zamanlı taramayla; yeni Sorun açılmaz |
| **Aynı niyet, farklı içerik (bütünlük ihlali)** | Sahip `409 same_key_payload_mismatch`/`idempotency_conflict` → `Failed(ContentConflict)` + security-event; `TargetLock` kalır; insan inceler |
| **Sorun kaydı ile görev arasında kesinti** | Olamaz: ikisi aynı işlemdedir; kesinti ikisini birden geri alır; sonraki değerlendirme ikisini birlikte yazar. "Açık sorun, görev yok" durumu (eski veri) ilk değerlendirmede tamamlanır (§9.1 değişmezi) |
| **Kapı sorgusu** | Hiçbir satır yazmaz; kalıcı takip ayrı `POST /rules/evaluations` komutuyla (§9.3–9.4) |
| **Gölge öneri yeniden üretilir** | Yeni revizyon da `ShadowOnly`; eski revizyon `Superseded`; hiçbiri onaylanamaz |
| **Gönderim öncesi zarf bozuk** | `FailBeforeDispatch`: `Approved → Failed`, sahip çağrılmaz, kilit yok, security-event; yeniden deneme yeni niyet |
| **Kurtarmada zarf bozuk** | Gönderim yapılmaz; `Unknown` + kilit kalır; security-event; insan devralma görevi (ilk gönderim yürümüş olabilir, "Failed" denmez) |

### 10.6 Kanıt matrisi — "aynı key tek sonuç" garantisi sahip modüllerde nerede? (main@00a8798)

Bu tablo "repoda zaten var" iddiasının yerine geçer; her satır dosya/satır kanıtı taşır veya **DOĞRULANACAK** der. (Salt-okunur envanter, 28 Eylül 2026.)

| WRITE aracı → sahip komut | Deneme/komut satırı (PK) | Aynı key + aynı içerik | Aynı key + farklı içerik/aktör | Eşzamanlılık | Terminal ret kaydı | Gerçek SQL testi | Durum |
|---|---|---|---|---|---|---|---|
| `counterparty.party.create@1` → `CounterpartyCommandService.CreateAsync` (`CounterpartyCommandService.cs:32–123`) | `idempotency.CommandExecutions` — unique `IX_CommandExecutions_CompanyId_CommandName_IdempotencyKeyHash` (`CommandExecutionModelBuilderExtensions.cs:51–57`); `ResponseJson` saklar | Replay: `new CounterpartyCommandExecution(response, true, execution.OriginalCorrelationId)` (`:390–393`); başlıklar `CounterpartyEndpoints.cs:486–488` | Aktör **veya** hash farkı → `Responses.IdempotencyConflict()` 409 `idempotency_conflict` (`:361–368`; kod `ICounterpartyCommandService.cs:79`) | Unique IX; `Version`+`RowVersion` (`CounterpartyDbContext.cs:93–94, 121–122`); create yolunda `catch (DbUpdateException)` kazananı yeniden okur, **2601/2627 filtresi yok**, kazanan yoksa `throw;` (`:327–340`) | Yalnız 201 başarı | `CounterpartyWriteSqlServerRelationalAcceptanceTests.WriteIdempotencyAndTerminalReplay_AreDurable` (`:81`; mismatch `:212–222`); race `CounterpartyWriteSafety…ConcurrentCreatesAndDifferentKeyStatusWrites_AreSerialized` (`:15`) | KANITLI; **DOĞRULANACAK:** create için farklı-aktör testi yok; 2601/2627 filtresi eksikliği ADR-011 ile uzlaştırılmalı |
| `sales.draft.create@1` → `SalesDraftService.CreateDraftAsync` → `SalesStore.StoreDraftAsync` | `sales.DraftAttempts` PK (CompanyId, CommandName, IdempotencyKeyHash) (`SalesRows.cs:149–169`); pointer `DraftId` + `RefusalJson` | `FindReplayAsync` → `SalesDraftReplay(stored, null, attempt.CorrelationId)` (`SalesStore.cs:148–176`) | `SalesDraftStoreCodes.SameKeyPayloadMismatch` (`"sales_same_key_payload_mismatch"`, `ISalesOrderDraftStore.cs:74`) → 409 `same_key_payload_mismatch` (`SalesDraftService.cs:1310`) | PK; `Version`+`RowVersion`; `SaveAsync` `DbUpdateConcurrencyException` / `SqlException 2601/2627` → `ConcurrentChange`, tracker clear | Evet (`Outcome=2`, `RefusalJson`) | `SalesDraftSqlServerRelationalAcceptanceTests.A_draft_with_lines_and_slices_is_stored_replayed_and_audited_in_one_save` (`:44`; replay `:60`); `…The_same_key_with_other_content_is_refused_and_a_racing_same_key_loses_cleanly` (`:107`; farklı içerik `:117`, farklı aktör `:127`) — v1.1'de repodan okundu | KANITLI |
| `purchasing.request.create@1` → `PurchasingService.CreateRequestAsync` (`:57–128`) → `PurchasingStore.StoreRequestAsync` (`:39–115`) | `purchasing.PurchaseAttempts` PK (`PurchasingDbContext.cs:202`); `Outcome 1/2`, `RequestId` pointer, `RefusalJson` | `PurchasingWriteOutcome.Replayed` + `attempt.CorrelationId` (`:52–59`); `RequestResponse(request, isReplay: true, …)` (`PurchasingService.cs:659–665`) | `RequireSameContent` (`:696–703`) → `purchasing_same_key_payload_mismatch` → 409 `same_key_payload_mismatch` (`PurchasingService.cs:734`; `PurchasingContractsV1.cs:109`) | PK + filtered unique `UX_PurchaseAttempts_RequestId`; `Version`+`RowVersion`; `SaveAsync` (`:766–784`) 2601/2627 → `purchasing_concurrent_change`; servis kazananı replay eder (`:117–121`) | Evet (`version_conflict` terminal) | `PurchasingSqlServerRelationalAcceptanceTests.A_request_and_an_order_are_stored_in_one_save_each_and_the_same_key_replays_with_the_first_correlation` (`:42`; mismatch `:57`) | KANITLI |
| `inventory.allocation.create@1` → `InventoryStore.Allocations.CreateAsync` (`InventoryStore.Allocations.cs:20–155`) | `inventory.Commands` PK (`InventoryDbContext.cs:542`); `PayloadHash` **küçük harf**, `ResultJson` | `FindAllocationReplayAsync` → `IsReplay=true` (`:266–289`); HTTP replay **200** (`InventoryEndpoints.cs:553–559`) | Payload **veya** aktör farkı → 409 `idempotency_conflict` (`InventoryAllocationContractsV1.cs:78`) | PK; `label.Version` (`:128`) + `RowVersion`; `catch` 2601/2627 → kazananı okur, yoksa 409 `inventory_concurrent_change` (`:141–152`) | **Yalnız başarı** | `InventoryAllocationSqlServerRelationalAcceptanceTests.Physical_sales_allocation_reduces_free_capacity…` (`:101`; replay `:131–132`, 409 `:139`); race `Concurrent_physical_allocations_cannot_both_consume_the_same_free_capacity` (`:247`) | KANITLI |
| `finance.receipt.draft@1` → `ReceiptDraftService.DraftSaveAsync` (`:85–284`; upsert) | `finance.Commands` PK (`FinanceDbContext.cs:547`; ADR-011 hizalaması `20260919090000_AlignCommandsWithAdr011.cs:38–42`); `ResultJson` | `prior.PayloadHash == payloadHash && ActorId eşit` → `ReplayAsync` (`:666–668`, `:715–727`) `IsReplay=true` | `Refused(CommandOutcome.IdempotencyConflict)`; **problem kodu yok**, gövde `"outcome":"IdempotencyConflict"` (`ManualReceiptV1.cs:99–100`); HTTP 409 (`FinanceEndpoints.cs:929–930`) | PK; `ReceiptDraftRow.Version` concurrency token; **Finance'te `RowVersion` yok**; `SaveAsync` (`:676–691`) 2601/2627 → `PriorAsync` yeniden, yoksa `VersionConflict` | Yalnız güncelleme yolunda (`draft_not_found`, `version_conflict`); **create için yalnız başarı** | `FinanceDraftSqlServerRelationalAcceptanceTests.A_stale_version_changes_nothing_and_the_same_key_replays_or_conflicts` (`:83`); HTTP race `FinanceCommandApi…A_same_key_race_on_a_save_stores_one_draft…` (`:242`) | KANITLI; **DOĞRULANACAK:** adaptör `outcome` enum'unu problem koduna eşler (kod sözlüğü farklı) |
| `logistics.instruction.create@1` → `LogisticsInstructionService.CreateInstructionAsync` (`:49–112`) → `LogisticsStore.StoreInstructionAsync` (`:35–117`) | `logistics.LoadingAttempts` PK (`LogisticsDbContext.cs:155`); `Outcome 1/2`, `InstructionId` pointer | `LogisticsWriteOutcome.Replayed` (`:44–55`); `Response(..., isReplay: true, …)` (`:566–574`) | `RequireSameContent` (`:396–403`) → `logistics_same_key_payload_mismatch` → 409 `same_key_payload_mismatch` (`LogisticsInstructionContractsV1.cs:114`) | PK + filtered unique `UX_LoadingAttempts_InstructionId`; `Version`+`RowVersion`; `SaveAsync` (`:438–456`) → `logistics_concurrent_change`; servis kazananı replay eder | **Create için yalnız başarı** (terminal ret mutation yollarında) | `LogisticsInstructionSqlServerRelationalAcceptanceTests.An_instruction_with_lines_is_stored_issued_loaded_within_the_cap_replayed_and_rebuilt_in_one_save_each` (`:60`; replay `:72`, mismatch `:75`); race (`:156`) | KANITLI |
| `accounting.normal-journal-draft.create@1` → `NormalJournalDraftServiceV1.CreateAsync` (`:144–180` → `RunAsync :268–410`) → `AccountingStore.CreateNormalDraftAsync` (`:285–364`) | `accounting.Commands` PK (`AccountingDbContext.cs:303`); `ResultJson` | `Answer`: `stored with { IsReplay = true, OriginalCorrelationId }` (`:507–518`) | `Binds` başarısız → `AccountingApiStatus.Conflict` + `idempotency_conflict` (`AccountingApiV1.cs:97`); ayrıca aynı taslak Id için `ContentConflict` (`AccountingStore.cs:297–310`) | PK; `Version`+`RowVersion` (`AccountingDbContext.cs:342–343`); `AccountingStore.SaveAsync` (`:1185–1201`) 2601/2627 → `accounting_concurrent_change`; **tracker clear yok** | Terminal set: NotFound, `accounting_version_conflict`, başarı (`:391–393`) | **Key düzeyi gerçek SQL testi YOK**; yalnız fake store birim testi `NormalJournalDraftLifecycleV1Tests.Reordered_lines_with_the_same_idempotency_key_are_refused…` (`:125`); SQL yalnız Id düzeyi ContentConflict | **DOĞRULANACAK:** key düzeyi replay/mismatch SQL testi Faz 4 öncesi sahip dilime eklenmeli |
| `document.link.create@1` | Belge kasası yok (`ERP-000G` ready) | — | — | — | — | — | **YOK — sahip dilimde yazılacak** |
| Work kullanıcı komutu `WorkItemService.CreateAsync` (AI aracı **değil**; AI taslağını insan kaydeder — master Ek A #43) | `work.Attempts` PK (ÖNERİ) | Replay: `WorkWriteOutcome.Replayed` | `work_same_key_payload_mismatch` → 409 `same_key_payload_mismatch` | PK; `Version`+`RowVersion`; `SaveAsync` 2601/2627 | Evet (Sales deseni) | Bu belge §18.2'de tanımlı | **TASARIM** (henüz yok) |

**Kanıt durumu (daraltılmış, v1.1):** Altı komutta (Counterparty, Sales, Purchasing, Inventory, Finance, Logistics) "aynı key + aynı içerik → replay" ve "aynı key + farklı içerik → 409" davranışı kodda ve sahip modülün **store/API düzeyindeki** gerçek SQL testinde kanıtlıdır. Eksikler: Counterparty create için farklı-aktör testi yok; Accounting'de key düzeyi gerçek SQL testi yok; Documents ve Work komutları henüz yok. Bu testler sahip komutları doğrudan çağırır; **AI gönderici üzerinden** aynı davranışın kanıtı §18.2'deki kabul planıdır, henüz yoktur.

Genel gözlemler (uygulama öncesi uzlaştırma listesi, ADR-008 madde 6): (a) iki depolama stili var — pointer + `RefusalJson` (Sales/Purchasing/Logistics) ve `ResultJson` (Counterparty/Organization `CommandExecution`; Inventory/Finance/Accounting/Manufacturing kendi `Commands` tabloları); adaptör iki stili de `AiWriteResult` haline getirir. (b) Aynı-key-farklı-içerik kodu **üç yazımdadır** (`same_key_payload_mismatch`, `idempotency_conflict`, Finance `outcome` enum) — adaptör hepsini `IsSameKeyMismatch` olarak tanır (Ek K-2). (c) Hash büyük/küçük harf farkı: key hash her yerde büyük harf; payload hash Inventory/Finance'te küçük, `CommandExecution`'da büyük — gateway kendi `CanonicalPayloadHash`'ini sahip modülün hash'iyle **karşılaştırmaz** (sahip modül kendi hesabını yapar); gateway yalnız kendi payload bütünlüğünü doğrular. (d) Hiçbir create yolu açık `BeginTransactionAsync` kullanmaz; tek `SaveChangesAsync` (audit aynı save'de). (e) Counterparty create yolundaki 2601/2627 filtresi eksikliği ve Accounting/Manufacturing'de tracker clear yokluğu sahip modül konularıdır; AI adaptörü bunları **varsaymaz**: sahibin kesin reddi (`AiOwnerRefusalException`) dışındaki her istisnayı `Unknown` sayar ve kurtarma yoluna sokar (§10.3 genel `catch`).

## 11. Sağlayıcı portu, router ve API-birincil yol

### 11.1 Kayıt ve uzlaştırma (KİLİTLİ-KAYNAK + son kullanıcı tercihi)

- `DEC-AI-004` (26 Ağustos 2026) ve `DEC-AI-009` (31 Ağustos 2026): hassas veri yerel-first, bulut default-deny; bulut yalnız ayrı sağlayıcı/sözleşme/KVKK/veri sınıfı ve **aktivasyon kararıyla**.
- Kullanıcının **5 Eylül 2026** tarihli `GOYMEN_ERP_TUM_MODULLER_UYGULAMA_VE_KARAR_RAPORU_v2_1.docx` belgesindeki tercihi (`AI-K01`): **"API birincil; GPU sunucusu şart değil."** Bu tercih `DEC-AI-009`'dan daha yenidir; `AGENTS.md` "çelişkide daha yeni kullanıcı kararı geçerlidir" kuralıyla bu belge onu **çalışma varsayımı** olarak alır ve **yeniden karar sorusu yapmaz**. Belge repoda olmadığı için kaydın `docs/decisions/` altına (tarihli `SINAN_KARARLARI_2026-09-05.md` veya `KARAR_SICILI` eki) **aktarılması** Faz 0 F0-2'nin idari adımıdır — yeni karar değil, mevcut tercihin kaynağa doğru aktarılması (bu belge `KARAR_SICILI`'ni değiştirmez).
- API tercihi **≠** canlı veri aktarımı veya sağlayıcı aktivasyonu onayı. v2.1'in kendi sınırları korunur: `Gizli` sınıfta yalnız izinli alan/minimizasyon; `Çok gizli` kapalı; sağlayıcı seçimi, DPA/ZDR, veri bölgesi, saklama/silme kanıtı ve **production aktivasyonu** `OPEN-020` paketinde ve Sinan'dadır. Kodda bu ayrım iki ayrı yapılandırmayla temsil edilir: `Ai:Providers:<ad>` bölümünün dolu olması (sağlayıcı **tanımlı**) ve `Ai:CloudGateway:Activated` (OPEN-020 aktivasyon kanıtı; varsayılan **false**; yalnız insan değiştirir, §19). Tanımlı ama aktive edilmemiş sağlayıcıya istek çıkmaz.

### 11.2 Router tablosu (yapılandırma; kod sabit değil)

| Veri sınıfı | Birincil rota (API-birincil) | Alternatif | Kısıt |
|---|---|---|---|
| Genel | Onaylı API sağlayıcısı | Yerel model (opsiyonel) | `CloudGateway:Activated=true` olmadan API'ye **hiç** istek çıkmaz; fake provider veya yerel |
| Şirket içi | Onaylı API (minimum veri) | Yerel (opsiyonel) | Aynı |
| Gizli | Onaylı API **yalnız izinli alan seti + minimizasyon/maskeleme politikası** (OPEN-020 Q-AI-4) | Yerel model (GPU **şart değil**; varsa tercih edilebilir) | Maskeleme sınıf düşürmez; açıklama yalnız izinli alanlarla üretilir |
| Çok gizli | **Rota yok** (sabit kod; derleme zamanı) | Yerel model dahi minimizasyon + audit | Egress handler `CokGizli` etiketli payload'ı **fırlatır** |

Router **sağlayıcı ekleyemez**, yasak sınıfı ucuz modele gönderemez, başarısızlığı aşmak için yetki genişletemez; fallback aynı policy/egress review'una tabidir; model adı yapılandırmadır (`ModelDeploymentRef`), iş mantığına girmez.

### 11.3 Port ve adaptörler

```csharp
// GoymenERP.Modules.Ai.Application/Providers/IAiModelClient.cs (ÖNERİ)
public sealed record AiModelRequest(string DeploymentRef, AiPromptBundle Prompt, JsonSchemaRef OutputSchema, IReadOnlyList<AiToolSchema> Tools, AiToolChoice ToolChoice, AiModelBudget Budget, AiDataClass DataClassCeiling, string CorrelationId);
public abstract record AiModelOutcome
{
    public sealed record Structured(JsonElement Output, AiUsage Usage) : AiModelOutcome;          // schema-uyumlu yapılandırılmış cevap
    public sealed record ToolCalls(IReadOnlyList<AiToolCallRequest> Calls, AiUsage Usage) : AiModelOutcome;  // >1 çağrı: orchestrator sırayla ve tek tek doğrular
    public sealed record Refusal(string Reason, AiUsage Usage) : AiModelOutcome;                  // ayrı terminal durum
    public sealed record Incomplete(string Reason, AiUsage Usage) : AiModelOutcome;               // truncated / content filter / max tokens
    public sealed record Timeout() : AiModelOutcome;
    public sealed record TransportFailure(string Code) : AiModelOutcome;
}
public interface IAiModelClient
{
    Task<AiModelOutcome> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken);
    Task<AiEmbedOutcome> EmbedAsync(AiEmbedRequest request, CancellationToken cancellationToken);
    Task<AiTranscribeOutcome> TranscribeAsync(AiAudioRequest request, CancellationToken cancellationToken);   // STT ADR sonrası
}
```

- `FakeAiModelClient` (deterministik; fixture dosyalarından `Structured/ToolCalls/Refusal/Incomplete/Timeout` üretir) CI'daki tek sağlayıcıdır (`ERP-000J` §21: gerçek veri ve kişisel API anahtarı CI'da yok).
- `HttpJsonSchemaModelClient`: sağlayıcıdan bağımsız HTTP + JSON; `ERP-000J` §9'daki aday desen (Responses API `text.format` JSON Schema, function tools `strict:true`, `tool_choice` kısıtı) **adapter yapılandırmasıyla** eşlenir; SDK yok; `refusal/incomplete/timeout/truncated/multiple-call` ayrı terminal durumlar. Sağlayıcı yetenek iddiaları uygulama anında dokümandan doğrulanıp `ai.PolicyVersions`'a yazılır.
- `AiEgressAllowlistHandler : DelegatingHandler`: yalnız yapılandırılmış host'lara; model tarafından üretilen URL'ye **hiç** istek yok; istek gövdesinde `CokGizli` etiketi → `InvalidOperationException` + security-event; `Authorization` başlığı `IAiSecretProvider`'dan (ortam değişkeni/KMS), log/trace'e girmez.
- Retry: READ/DRAFT model çağrıları için yapılandırılmış sınırlı retry (`Ai:Provider:RetryCount`, jitter); WRITE dispatch'te **retry yok** (sağlayıcı zaten dispatch'te yer almaz — dispatch modelsizdir).

---

## 12. Ölçü referansı, çıktı doğrulayıcı, güvenli renderer

```csharp
internal static partial class AiMeasureSyntax                   // doğrulayıcı ve renderer AYNI sözdizimini paylaşır
{
    [GeneratedRegex(@"\{\{measure:(?<id>m\d{1,4})\}\}", RegexOptions.CultureInvariant)] internal static partial Regex MeasureRef();
}

internal sealed partial class AiOutputValidator(IAiDisclosureMatrix disclosure) : IAiOutputValidator
{
    [GeneratedRegex(@"(?<![\w{])[-+]?\d{1,3}([.,]\d{3})*([.,]\d+)?\s?(TL|TRY|₺|USD|EUR|kg|ton|MTL|%)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)] private static partial Regex RawFinancialNumber();

    public AiValidationResult Validate(AnswerEnvelope answer, IReadOnlyDictionary<string, MeasuredValue> measures, AiToolContext context, DisclosureLevel level)
    {
        var problems = new List<AiValidationProblem>();
        foreach (var finding in answer.Findings)
        {
            var refs = AiMeasureSyntax.MeasureRef().Matches(finding.Text).Select(m => m.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);
            foreach (var id in refs)
            {
                if (!measures.TryGetValue(id, out var mv)) { problems.Add(AiValidationProblem.UnknownMeasure(finding.Id, id)); continue; }
                if (mv.Subject != finding.Target.SubjectFor(mv.Measure) || mv.Period != finding.Period || mv.ScenarioRef != finding.ScenarioRef)
                    problems.Add(AiValidationProblem.MeasureBindingMismatch(finding.Id, id));          // doğru sayı yanlış varlığa/döneme bağlanamaz (G-32)
            }
            if (RawFinancialNumber().IsMatch(finding.Text)) problems.Add(AiValidationProblem.RawFinancialNumber(finding.Id));   // ham tutar → bastır (G-31)
            if (level == DisclosureLevel.GateDecision && (finding.Measures.Count > 0 || !disclosure.IsTemplateOnly(finding.Text)))
                problems.Add(AiValidationProblem.DisclosureBreach(finding.Id));                       // sayı içermeyen finans cümlesi de sızıntıdır
            if (!AiMeasureDictionary.Known(finding.Measures.Select(m => m.Measure))) problems.Add(AiValidationProblem.UnknownMeasureName(finding.Id));  // "net nakit" türetme (G-14)
        }
        return problems.Any(p => p.Severity == AiProblemSeverity.Reject) ? AiValidationResult.Rejected(problems) : AiValidationResult.Accepted(problems);
    }
}

internal sealed class AiMeasureRenderer(IAiNumberFormatter format) : IAiMeasureRenderer
{
    // {{measure:m1}} → "5,58 MTL" (kültür tr-TR; birim ölçü sözlüğünden); referans yoksa metin yayımlanmaz
    public string Render(string text, IReadOnlyDictionary<string, MeasuredValue> measures, DisclosureLevel level)
        => level == DisclosureLevel.Full ? AiMeasureSyntax.MeasureRef().Replace(text, m => format.Format(measures[m.Groups["id"].Value])) : text;
}
```

- Yargılar (limit üstünde mi, yeterli mi) **calculator'dan** gelir (`GateDecision`, `changeDrivers`); model yargı üretmez, yalnız açıklar — doğrulayıcı `finding.Judgement` alanının bir `GateDecision`/`EngineResult` referansı taşımasını ister; referanssız yargı `Reject`.
- Güvenli renderer: çıktı düz metin + sınırlı markdown (kalın, liste); HTML, script, event handler, `javascript:`/`data:` URL, dış görsel yok; frontend `dangerouslySetInnerHTML` **kullanmaz** (`ERP-000J` §13); linkler yalnız ERP iç rotaları (`sourceRef` → `/satis/siparisler/{id}`).

---

## 13. Bilgi katmanı ve erişim

```csharp
public sealed record AiKnowledgeQuery(Guid CompanyId, string ActorId, PermissionScope Scope, string Text, IReadOnlyList<string> Modules, DateOnly? EffectiveOn, bool IncludeHistorical, int TopK);
public interface IAiKnowledgeIndex
{
    Task<IReadOnlyList<AiKnowledgeHit>> SearchAsync(AiKnowledgeQuery query, CancellationToken ct);    // hibrit: anahtar kelime (ERP-000I) + vektör + metadata; ACL SORGU ANINDA
    Task<Guid> ProposeAsync(AiKnowledgeProposal proposal, WorkCommandMetadata metadata, CancellationToken ct); // öneri → süreç sahibi doğrulama → sürüm (DEC-AI-006)
}
```

- Sınıflar `ApprovedRule/OneTimeException/PastCase/UserComment/UnverifiedClaim` (`KnowledgeVersionRow.Class`); yürürlük filtresi (`EffectiveFrom/To`, iptal edilmiş belge sürümü) varsayılan; `IncludeHistorical` etiketli.
- Türkçe normalizasyon: arama alanında `I/İ/ı/i` ve aksan katlaması (SQL Server `Turkish_CI_AI` collation'lı ayrı arama sütunu — DOĞRULANACAK: `ERP-000I` arama tasarımıyla uyum); orijinal metin korunur (G-21).
- Belge içeriği **veridir**: indeksleme sırasında metin "talimat" olarak işaretlenmez; prompt'a her zaman `data` blokları içinde ve kaynak etiketli girer.
- Embedding deposu: SQL Server yerleşik `vector` doğrulanana kadar `varbinary(max)` + uygulama içi kosinüs (küçük korpus) veya ayrı depo adaptörü (NS-AI-07; ADR).

---

## 14. İş akışı motoru (`Ai.Workflow`) ve iş takibi ajanı

### 14.1 Bileşenler

```text
AiWorkflowWorker : BackgroundService                # tek worker; Ai:Workflow:Enabled ve !OpenApiDocumentGenerationMode.IsActive iken
  ├── JobPoller        : ai.Jobs (Status=Pending, NextRunAtUtc<=now, lease yok/expired) → lease al (rowversion) → çalıştır → tamamla/erte
  ├── SchedulePlanner  : iş günü takvimi (Organization/Settings) + şablon zamanları → ai.Jobs (DedupKey = schedule:{key}:{slot}) — çift üretim yok
  ├── InboxConsumer    : IOutboxSubscriber (ERP-000F dispatcher gelince) → ai.Inbox (CompanyId, Consumer, MessageId) dedup → ai.Jobs
  ├── WorkflowRunner   : WorkflowInstance durum makinesi; şablon adımlarını IAiOrchestrator (model) veya Work.Rules (modelsiz) ile yürütür
  ├── EscalationTicker : work.WorkItems DueAtUtc geçmiş → WorkItem.Escalate(policy) (Work modülünün kendi sistem komutu; AI'dan bağımsız da çalışabilir)
  └── RecoverySweeper  : IAiRecoveryCoordinator.RecoverAsync (şirket başına), Running/Dispatched süre aşımı → Unknown kontrolü
```

Repo'da bugün `BackgroundService` yoktur; tek `IHostedService` readiness probe'dur (Ek K-1 madde 12) — worker **yeni** bir çalışma biçimidir ve ADR-008 madde 11 ile kabul edilir. İlk sürümde aynı process'te koşar; `Ai:Workflow:Standalone=true` ile ayrı host'ta (`GoymenERP.AiWorker` projesi — Master v3 §13.1) çalışabilir; durum her zaman `ai.*` tablolarındadır.

### 14.2 `WorkflowInstance` durum makinesi ve şablon

```csharp
public enum WorkflowState { Pending = 1, Running = 2, WaitingHuman = 3, WaitingApproval = 4, WaitingData = 5, Escalated = 6, Done = 7, Failed = 8, Unknown = 9 }

public interface IWorkflowDefinition
{
    string Id { get; } int Version { get; }
    IReadOnlyList<WorkflowStep> Steps { get; }      // her adım: Kind (RuleEvaluation | ToolRead | ModelExplain | OpenWorkItem | WaitFor | Brief), bütçe, bekleme koşulu
}

// Örnek: geciken termin takibi (modelsiz çekirdek + opsiyonel model açıklaması)
internal sealed class OverduePromiseFollowUpWorkflow : IWorkflowDefinition
{
    public string Id => "wf.purchasing.overdue-promise-follow-up"; public int Version => 1;
    public IReadOnlyList<WorkflowStep> Steps { get; } =
    [
        WorkflowStep.ToolRead("purchasing.orders.list@1", args: "since=lastRun&promiseBefore=today", cardinality: 200),        // delta okuma; bütçe aşımı → Partial
        WorkflowStep.RuleEvaluation("IR-PUR-003", perItem: true),                                                                 // Work.Rules: Sorun/Değerlendirme; kart güncelle
        WorkflowStep.OpenWorkItem(template: "TerminTeyidi", when: EvaluationOutcome.Raised),                                     // kural-tabanlı, modelsiz, politika onaylı
        WorkflowStep.ModelExplain(optional: true, disclosure: DisclosureLevel.Full, promptPolicy: "task.follow-up.explain@1"),     // AI kapalıysa şablon metni
        WorkflowStep.Draft("doc.email.draft@1", optional: true),                                                                  // tedarikçiye termin teyidi taslağı; gönderim insan
        WorkflowStep.WaitFor(WorkflowWait.WorkItemClosedOrEvent("purchasing.purchase-order.promise-updated.v1"), sla: "policy:EP-MED"),
        WorkflowStep.Brief(target: "AI-01", when: WorkflowWait.Timeout),
    ];
}
```

- Bekleme durumlarında (`WaitingHuman/WaitingApproval/WaitingData`) model çağrısı **yok**; instance `WaitingOnRef` ile uyur; uyandırma olay/kapanış/zaman ile.
- `Unknown` başarısızlık değildir: önce önceki adımın sonucu (Proposal/iş öğesi) sorgulanır.
- Yeniden başlatma: `Running` kalan instance'lar `Unknown` kontrolünden geçer; `SnapshotJson` (rowversion'lar) ile kaldığı adım yeniden değerlendirilir; iş öğesi `DedupKey`/Attempts ile tekil; çift işlem yok (gerçek SQL testi G-18).

### 14.3 Worker'da tenant ve aktör bağlamı

```csharp
private async Task RunJobAsync(JobRow job, WorkflowInstanceRow instance, CancellationToken ct)
{
    await using var scope = scopes.CreateAsyncScope();
    scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(job.CompanyId);                                  // write-once; şirket başına scope
    scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set($"aijob-{job.Id:N}", job.TriggerRef, SourceChannels.System, null, "ai-workflow");
    // READ araçları sahip modülün KULLANICI port'larıdır (fence ICurrentUser okur): iş, gerçek bir kullanıcı adına koşar —
    //   kullanıcı başlatmalı işte işi başlatan; zamanlı şablonda şablonun RunAsActorId'si (ör. süreç sahibi; politika kaydı, NS-AI-02).
    scope.ServiceProvider.GetRequiredService<IScopedActorOverride>().Set(ScopedActor.WorkflowRunAs(instance.RunAsActorId, job.CompanyId));
    // Kural değerlendirmeleri ise sahip modülün SİSTEM port'larıyla (§9.2) — kullanıcı yetkisi gerektirmez, yalnız sahip modülün sunduğu dar sonuç.
    // …
}
```

Kurallar: (1) iş akışının **okuyabildiği** şey, adına koştuğu gerçek kullanıcının güncel yetkisiyle sınırlıdır (sahip fence'i her çağrıda yeniden doğrular; yetkisi düşen kullanıcının şablonu `WaitingHuman`'a düşer ve sahibine görev açılır); (2) üretilen bulgu ve iş öğesi, **alıcının** açıklık düzeyine göre süzülür (adına koşan kullanıcının değil); (3) worker aktörü **hiçbir WRITE aracını** çağıramaz (`AiActorMode.SystemRuleEvaluation` veya `WorkflowRunAs` bağlamında `RiskClass=Write` → reddedilir; architecture testi `SystemActorHasNoWriteToolsTests`); iş akışı bir yazma gerektiriyorsa `Proposal` üretir ve `WaitingApproval`'a geçer; onay insan; (4) `RunAsActorId` alanı `WorkflowInstanceRow`'a eklenir (§4.3) ve şablon kaydı politika registry'sinde sürümlenir.

### 14.4 İş takibi ajanı — ayrıntılı cevap

**Ne yapar (Faz 3 kapsamı):**

| Takip türü | Tetikleyici | Modelsiz çekirdek (`Work` / `Work.Rules` / `Ai.Workflow`) | Model katkısı (opsiyonel) | Kapanış kanıtı |
|---|---|---|---|---|
| Geciken alış termini | Zamanlı tarama + `purchasing.*` olayları | IR-PUR-003 Sorun; planlamacıya görev; SLA; eskalasyon | Etki açıklaması (hangi üretim planları etkilenir), tedarikçi e-posta taslağı | Termin güncelleme olayı veya sipariş kapanışı |
| Vade/tahsilat | Günlük tarama (`finance.open-items.read@1`) | IR-FIN-002 skor (deterministik); tahsilat görevi; sıralama | Hatırlatma e-postası taslağı; müşteri geçmişi özeti | Tahsilat olayı (`finance.manual-receipt.recorded.v1`) |
| Eksik belge / imza | Sevkiyat niyeti, belge olayları | IR-LOG-005 / IR-DOC-* Sorun; belge sahibine görev | Hangi belge neden gerekli (SOP) | Belge bağlantı olayı |
| Sevkiyat öncesi risk | On-demand kapı + olaylar + istisna süresi | IR-LOG-001; Finans görevi; istisna durumları (§5.3) | Seçenek gerekçeleri; hatırlatma taslağı | İstisna onayı / tahsilat / iptal |
| Fatura–sipariş–kabul karşılaştırma | Belge olayı (`ERP-000G` gelince) | IR-PUR-002 fark; muhasebe + satınalma görevi | Fark açıklaması; tedarikçi yazışma taslağı | Fark kapanış kaydı |
| Kullanıcı isteği ("bu haftaki teslimatları incele") | Panel / AI Merkezi | `WorkflowInstance(wf.user.goal@1)`: READ araçları + kural değerlendirmeleri; sonuç `AiFinding[]`; istenirse **görev taslağı** (`work.item.draft@1`) — kullanıcı Çalışma Merkezi'nde kaydeder | Özet ve öneri metni | Kullanıcı kapatır / görevler kapanır |
| Günlük yönetici brifingi | 07:30 iş günü | Açık Sorunlar, geciken görevler, onay bekleyenler, KPI (ölçü referanslı) | Anlatı özeti (kaynaklı); "kısmi" damgası | — |

**Ne yapmaz:** onaysız kayıt; e-posta/mesaj gönderme (taslak + insan gönderir); yeni eskalasyon zinciri uydurma; "hep böyle yapıyoruz"ı kurala çevirme; bütün ERP'yi yeniden okuma (delta + kardinalite limitleri); sonucu belirsiz işi kendiliğinden tekrar deneme.

**Neden güvenli:** takip nesneleri (`Issue`, `WorkItem`) ve motor `Work` modülündedir; model kapalıyken bile çalışır; her iş öğesi sahip/son tarih/kanıt taşır; her kural sürümlü ve `docs/ai/IMPACT_RULES.md` ile eşleşir; her görev üretimi `work.Attempts` ile idempotenttir; görünürlük matrisi bütün kanallara uygulanır.

**Ölçüsü:** kural-tabanlı takip oranı (modelsiz), ortalama kapanış süresi, yöneticiye ulaşan iş sayısı (eskalasyon KPI), gereksiz uyarı oranı (master §2.2, §8.6).

## 15. Endpoint'ler, problem kodları ve OpenAPI

### 15.1 Endpoint listesi (ÖNERİ; hepsi `RequireAuthorization().RequireCompanyContext()`)

| Yöntem ve yol | Ad | Sınıf | Idempotency-Key | Cevaplar |
|---|---|---|---|---|
| `GET /api/v1/ai/capabilities` | `GetAiCapabilitiesV1` | READ | — | 200 `{generation, capabilities[]}`; K shell `Generation` mekanizmasıyla |
| `POST /api/v1/ai/runs` | `StartAiRunV1` | READ/DRAFT/SIMULATE (panel sorusu) | Zorunlu (aynı soru çift gönderimde tek run) | 201 `AiRunResponse` (+Location); 202 asenkron; 400 `command_metadata_invalid`; 409 `same_key_payload_mismatch`; 422 `validation_failed`; 503 |
| `GET /api/v1/ai/runs/{runId}` | `GetAiRunV1` | READ | — | 200; 404 (nondisclosing) |
| `POST /api/v1/ai/runs/{runId}/feedback` | `RecordAiFeedbackV1` | düşük etki yazma (ai şeması) | Zorunlu | 201 |
| `POST /api/v1/ai/proposals` | `CreateAiProposalV1` (önizleme) | WRITE niyeti → **yalnız preview** | Zorunlu (`reissueOf` ile aynı niyet) | 201 `AiPreviewResponse`; 409 `intent_pending_recovery` / `target_locked`; 422 |
| `GET /api/v1/ai/proposals/{id}` | `GetAiProposalV1` | READ | — | 200 |
| `POST /api/v1/ai/proposals/{id}/approvals` | `ApproveAiProposalV1` | Onay (adım 11) | Zorunlu | 201 `AiApprovalResponse` (`status` ∈ {PartiallyApproved, Approved, Dispatched, Executed}); 409 `version_conflict` / `approval_binding_mismatch` / `proposal_expired`; 403 |
| `POST /api/v1/ai/proposals/{id}/rejection` | `RejectAiProposalV1` | — | Zorunlu | 201 |
| `POST /api/v1/ai/proposals/{id}/resolution` | `ResolveAiProposalV1` (insan devralma) | K3 yetki (`ai.recovery.resolve`) | Zorunlu | 201; kanıt referansı zorunlu (`evidenceRef`) |
| `GET /api/v1/ai/center/brief` | `GetAiCenterBriefV1` | READ | — | 200; `coverage.Partial` |
| `GET/POST /api/v1/ai/knowledge…` | `…KnowledgeV1` | READ / öneri | POST zorunlu | — |
| `GET /api/v1/ai/policy/…` | `…PolicyV1` (AI-03) | READ; değişiklik K3 | — | — |
| `GET /api/v1/work/items` (`?queue=` (mine, role, overdue, waiting)) | `ListWorkItemsV1` | READ | — | 200 sayfa (`items, nextCursor`) |
| `GET /api/v1/work/items/{id}` | `GetWorkItemV1` | READ | — | 200 (görünürlük matrisi uygulanmış) |
| `POST /api/v1/work/items` | `CreateWorkItemV1` | WRITE-düşük | Zorunlu | 201 (+Location); 409 |
| `POST /api/v1/work/items/{id}/events` | `AppendWorkItemEventV1` (yorum/atama/kapanış) | yazma | Zorunlu | 201; kapanışta `closureEvidenceRef` zorunlu |
| `POST /api/v1/work/items/{id}/approval-decision` | `DecideApprovalRequestV1` | SoD | Zorunlu | 201; 403 maker=checker |
| `POST /api/v1/rules/gate` | `QueryRuleGateV1` | READ (kapı sorgusu; **hiçbir şey yazmaz**) | — | 200 `GateQueryResponse` (§9.3) |
| `POST /api/v1/rules/evaluations` | `TrackRuleEvaluationV1` | Kalıcı takip komutu (Sorun + Değerlendirme + görev tek işlemde) | Zorunlu | 201; 409 `same_key_payload_mismatch` (§9.4) |

Onay ve gönderim **ayrı** adımlardır fakat kullanıcı tek tıkla görür: `ApproveAiProposalV1` kota dolunca `IAiCommandDispatcher.DispatchAsync`'i **aynı istek içinde** çağırır; dispatch sonucu `Executed/Failed/Unknown` cevapta döner; `Unknown` cevabı "sonucu belirsiz — kurtarma bekleniyor" durumudur (UI durum sözlüğü).

### 15.2 Endpoint kod örneği (mevcut konvansiyonlarla)

```csharp
// GoymenERP.Api/AiRuntime/AiEndpoints.cs (ÖNERİ)
internal static class AiEndpoints
{
    public const string ProposalsPath = "/api/v1/ai/proposals";

    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var proposals = endpoints.MapGroup(ProposalsPath).WithTags("Ai").RequireAuthorization().RequireCompanyContext();

        // Mevcut desen (SalesEndpoints.cs L238–291): SetPrivateNoStore / TryReadIdempotencyKey / ApplyReplayHeaders / *Problem
        // her endpoint sınıfının KENDİ private static helper'larıdır (ortak BuildingBlocks helper'ı yoktur); AiEndpoints aynı helper'ları kendi içinde tanımlar.
        proposals.MapPost("/{proposalId:guid}/approvals", async (
            Guid proposalId, HttpContext httpContext, [FromServices] IAiApprovalService service, ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey) || !correlation.IsResolved)
            {
                return AiProblem(StatusCodes.Status400BadRequest, AiProblemCodesV1.CommandMetadataInvalid, correlation);
            }
            if (!httpContext.Request.HasJsonContentType())
            {
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            }
            ApproveProposalRequestV1 request;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                request = AiJson.ReadApprove(await reader.ReadToEndAsync(cancellationToken));      // strict gövde (#257 deseni): fazla alan → 400
            }
            catch (JsonException)
            {
                return AiProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }
            try
            {
                var response = await service.ApproveAsync(proposalId, request,
                    new AiCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow), cancellationToken);
                if (response.IsReplay) { ApplyReplayHeaders(httpContext, response.OriginalCorrelationId); }
                httpContext.Response.Headers.Location = ProposalLocation(httpContext.Request, proposalId);
                return Results.Json(response, AiJson.Options, statusCode: AiApiV1.CreatedStatus);
            }
            catch (AiServiceException exception)                                                    // 401/403/404/503 kodsuz; diğerleri code + correlationId + errors
            {
                if (exception.IsReplay) { ApplyReplayHeaders(httpContext, exception.OriginalCorrelationId ?? throw new InvalidOperationException("Replayed refusal without its original correlation.")); }
                return AiProblem(exception, correlation);
            }
        })
        .WithName("ApproveAiProposalV1")
        .WithIdempotencyContract(AiApiV1.CreatedStatus, StatusCodes.Status409Conflict)               // replay edilebilir sonuçlar: 201 ve kayıtlı terminal ret (409)
        .Accepts<ApproveProposalRequestV1>("application/json")
        .Produces<AiApprovalResponseV1>(AiApiV1.CreatedStatus)
        .ProducesErpBadRequest("validation_failed", AiProblemCodesV1.CommandMetadataInvalid)
        .AllowsEmptyErpBadRequestBody()
        .ProducesErpUnsupportedMediaType()
        .ProducesErpConflict(AiProblemCodesV1.VersionConflict, AiProblemCodesV1.ApprovalBindingMismatch, AiProblemCodesV1.ProposalExpired,
            AiProblemCodesV1.SameKeyPayloadMismatch, AiProblemCodesV1.TargetLocked, AiProblemCodesV1.IntentPendingRecovery)
        .ProducesErpUnprocessableEntity("validation_failed")
        .ProducesErpServiceUnavailable()
        .ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static void SetPrivateNoStore(HttpResponse response) => response.Headers.CacheControl = "private, no-store";
    private static bool TryReadIdempotencyKey(IHeaderDictionary headers, out string key) { /* tam bir tane, trim'li, ≤128, virgülsüz — Sales helper'ının aynısı */ key = ""; return false; }
    private static void ApplyReplayHeaders(HttpContext http, string originalCorrelationId) { http.Response.Headers["Idempotency-Replayed"] = "true"; http.Response.Headers["Idempotency-Original-Correlation-Id"] = originalCorrelationId; }
    private static IResult AiProblem(int statusCode, string code, ICorrelationContext correlation) => Results.Problem(statusCode: statusCode, extensions: new Dictionary<string, object?> { ["code"] = code, ["correlationId"] = correlation.CorrelationId, ["errors"] = Array.Empty<object>() });
    private static IResult AiProblem(AiServiceException exception, ICorrelationContext correlation) => exception.StatusCode is 401 or 403 or 404 or 503
        ? Results.Problem(statusCode: exception.StatusCode)                                        // kodsuz; platform yazıcısı public kodu ekler
        : AiProblem(exception.StatusCode, exception.Code, correlation);
}
```

Problem sınırı (KİLİTLİ-KAYNAK, Ek K-1 madde "problem boundary"): 401/403/404 ve her 503 servisten **kodsuz** döner, platform yazıcısı public kodu ekler; diğer durumlar `code` + `correlationId` + `errors` taşır. `Idempotency-Key`: tam bir tane, trim'li, ≤128, virgülsüz; aksi 400 `command_metadata_invalid`.

### 15.3 OpenAPI

- Build `artifacts/openapi.json` üretir; CI `contract-drift` işi `tools/validate_openapi_contract.py --baseline contracts/openapi/goymen-erp-v1.json --generated artifacts/openapi.json` ile karşılaştırır (Ek K-1 madde 19). Yeni endpoint'ler baseline'ı değiştirir → `openapi:generate` etiketiyle `openapi-generate.yml` yeni baseline'ı byte-byte üretir; PR K3 (public API contract).
- `OpenApiContractGenerationTests.BuildTimeContract_IsDeterministicSecretFreeAndDoesNotContactConfiguredDependencies`: worker ve provider `OpenApiDocumentGenerationMode.IsActive` iken **kayıt edilmez** (Identity readiness deseni).
- Her yeni public DTO `V1` sonekli `sealed record`; sürüm değişikliği yeni tür (`…V2`), eski tür deprecation ile.

---

## 16. Capability provider ve registry pinleme

```csharp
// GoymenERP.Api/AiRuntime/AiShellCapabilityProvider.cs (ÖNERİ)
internal sealed class AiShellCapabilityProvider : IShellCapabilityProvider
{
    public const string PanelUseCapabilityId = "ai.panel.use";
    public const string CenterViewCapabilityId = "ai.center.view";
    public const string QualityViewCapabilityId = "ai.quality.view";
    public const string PolicyManageCapabilityId = "ai.policy.manage";
    public string Owner => "Ai"; public string ContractVersion => "1.0";

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var center = new ShellCapabilityDeclaration(CenterViewCapabilityId, "ai-center", "/ai-merkezi", "AI-01",
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
            RequiredPermissions: ["ai.center.view" /* ÖNERİ; ErpPermissions'a eklenir */], ImplementationAvailable: false /* Faz 1'e kadar DEFINED_DISABLED */);
        var panel = center with { CapabilityId = PanelUseCapabilityId, RouteId = "ai-panel", RoutePath = "", ScreenId = "AI-PANEL", RequiredPermissions = ["ai.panel.use"], ParentCapabilityId = null };
        var quality = center with { CapabilityId = QualityViewCapabilityId, RouteId = "ai-quality", RoutePath = "/ai-merkezi/kalite", ScreenId = "AI-02", RequiredPermissions = ["ai.quality.view"], ParentCapabilityId = CenterViewCapabilityId };
        var policy = center with { CapabilityId = PolicyManageCapabilityId, RouteId = "ai-policy", RoutePath = "/ai-merkezi/politika", ScreenId = "AI-03", RequiredPermissions = ["ai.policy.manage"], ParentCapabilityId = CenterViewCapabilityId };
        return [center, panel, quality, policy];
    }
}
```

Pinleme adımları (KİLİTLİ-KAYNAK süreç, Ek K-1 madde 10/"checked in code" 6): `C01E2eShellCapabilityActivation.OptionalModuleProviders`'a `typeof(WorkShellCapabilityProvider)`, `typeof(AiShellCapabilityProvider)`; `NormalAuthorities`'e yeni tuple satırı; `ShellCapabilityRegistryAuthorities`'e `ShellCapabilityRegistry.ComputeCandidateRevision(providers)` ile üretilmiş yeni `sha256:` sabiti (`^sha256:[A-Za-z0-9_-]{43}$`). Sabit olmadan host "Shell capability provider seti kabul edilmiş owner kaynağıyla eşleşmiyor." ile başlamaz. Bu değişiklik K3'tür.

`ImplementationAvailable=false` iken K shell slotu `DEFINED_DISABLED` gösterir (`ERP-000K` §7/§16.2); AI kapalı ≠ Work kapalı: `WorkShellCapabilityProvider` (`work.items.view`, `work.items.create`) ayrı provider'dır ve Faz 1'de `ImplementationAvailable=true` olabilir.

---

## 17. Frontend (React 19, react-router, TanStack Query, Vitest)

### 17.1 Lane dosyaları (Sales deseni)

```ts
// src/ai/capabilities.ts (ÖNERİ)
export const AI_CAPABILITY = {
  /** AI-PANEL sağ bağlam paneli; rota yok, slot. */
  panelUse: 'ai.panel.use',
  /** AI-01 AI Merkezi yönetim/görev görünümü. Rota `/ai-merkezi`. */
  centerView: 'ai.center.view',
  /** AI-02 kalite. Rota `/ai-merkezi/kalite`. AYRI grant. */
  qualityView: 'ai.quality.view',
  /** AI-03 araç/politika/veri kapsamı. Rota `/ai-merkezi/politika`. K3 yetkili. */
  policyManage: 'ai.policy.manage',
} as const
export type AiCapabilityId = (typeof AI_CAPABILITY)[keyof typeof AI_CAPABILITY]
export const AI_ROUTE = { center: '/ai-merkezi', quality: '/ai-merkezi/kalite', policy: '/ai-merkezi/politika' } as const

// src/work/capabilities.ts
export const WORK_CAPABILITY = { itemsView: 'work.items.view', itemsCreate: 'work.items.create' } as const
export const WORK_ROUTE = { list: '/calisma-merkezi', detail: '/calisma-merkezi/:itemId' } as const
export const workDetailPath = (itemId: string) => `/calisma-merkezi/${encodeURIComponent(itemId)}`
```

```tsx
// src/ai/aiRoutes.tsx — CapabilityRoute ile kapı; statik rotalar parametreliden önce
export const aiRoutes: RouteObject[] = [
  { path: AI_ROUTE.center.slice(1), element: gate(AI_CAPABILITY.centerView, <AiCenterPage />) },
  { path: AI_ROUTE.quality.slice(1), element: gate(AI_CAPABILITY.qualityView, <AiQualityPage />) },
  { path: AI_ROUTE.policy.slice(1), element: gate(AI_CAPABILITY.policyManage, <AiPolicyPage />) },
]
// shell/router.tsx: `...workRoutes, ...aiRoutes` catch-all'dan önce; shell/capabilities.ts: CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL eklemeleri;
// AppShell NAV_CAPABILITIES: aiCenterView ve workItemsView (detail ve create sidebar'da YOK — K5 kuralı); drift testi: src/shell/aiMountDrift.test.ts
```

### 17.2 `api.ts` ve `contract.ts`

```ts
// src/ai/api.ts (allocationApi.ts deseni: üç başlık, ViewOutcome, replay bayrağı)
export async function approveProposal(proposalId: string, companyId: string, snapshot: AiCommandSnapshot<ApproveProposalBody>): Promise<ApprovalMutationOutcome> {
  const raw = await request(`${AI_PROPOSALS_PATH}/${encodeURIComponent(proposalId)}/approvals`, {
    method: 'POST',
    headers: { ...headers(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': snapshot.idempotencyKey, 'X-Correlation-Id': snapshot.correlationId },
    body: JSON.stringify(snapshot.body),
  })
  if ('failure' in raw) return raw.failure === 'aborted' ? { type: 'aborted' } : { type: 'unknown', reason: raw.failure }   // belirsiz: UI "sonucu belirsiz" gösterir; İKİNCİ TIKLAMA AYNI KEY ile
  if (raw.status !== 201) return raw.status >= 500 ? { type: 'unknown', reason: 'unreachable' } : { type: 'http', status: raw.status, code: problemCode(raw.body) }
  const approval = decodeApprovalResponse(raw.body)
  if (approval === null || approval.proposalId !== proposalId || approval.companyId !== companyId) return { type: 'unknown', reason: 'malformed' }
  return { type: 'recorded', approval, replayed: raw.replayed }
}
```

```ts
// src/ai/contract.ts — exact(): fazla/eksik alan = malformed; sayı alanları MeasuredValue olarak decode edilir, çıplak sayı kabul edilmez
export function decodeMeasuredValue(value: unknown): MeasuredValue | null {
  const o = exact(value, ['measureId', 'measure', 'value', 'unit', 'asOf', 'subject', 'period', 'scenarioRef', 'sourceRef', 'formula', 'assertionKind'])
  if (o === null || typeof o.value !== 'number' || !isText(o.measureId, 8) || !isAssertionKind(o.assertionKind)) return null
  const subject = exact(o.subject, ['entityType', 'id']); const source = exact(o.sourceRef, ['module', 'entity', 'id', 'version'])
  if (subject === null || source === null) return null
  return { measureId: o.measureId, measure: o.measure as string, value: o.value, unit: o.unit as string, asOf: o.asOf as string, subject: subject as MeasureSubject, period: (o.period as string | null), scenarioRef: (o.scenarioRef as string | null), sourceRef: source as SourceRef, formula: o.formula as string, assertionKind: o.assertionKind }
}
// value: yalnız gösterim/sıralama içindir; istemci bu sayı üzerinde HESAP yapmaz (metindeki tutarlar sunucuda render edilir, §12)

// src/work/contract.ts — kapı kararı Work sözleşmesidir (Work.Contracts.GateDecisionV1)
export function decodeGateDecision(value: unknown): GateDecision | null { /* owner, tool, subjectId, decision ∈ {Proceed,ReviewRequired,Stop}, reasonCodes[], exceptionRef, exceptionState ∈ {Active,Consumed,Expired,Revoked}|null, asOf, policyVersion — TUTAR ALANI YOK; fazla alan → null */ }
```

Decoder testleri OpenAPI baseline'ındaki şema örneklerine pinlenir (Product/Sales deseni: `Examples/**/*.json`).

### 17.3 Bileşenler

| Bileşen | Sahip | Davranış |
|---|---|---|
| `GateDecisionCard` (`src/work`) | Work lane | Form içinde, alanın yanında; `Devam / Yetkili değerlendirmesi gerekli / Durdur` + izinli gerekçe kodu metinleri (kaynak dosyalı, Türkçe); **modelsiz**; AI kapalıyken aynen; `data-state` rozetleri |
| `WorkCenterPage`, `WorkItemDetailPage` | Work lane | Kuyruklar; kart Master §6.3 alanları; görünürlük matrisi sunucuda uygulanmış veriyi gösterir; kapanışta kanıt referansı seçimi |
| `AiContextPanel` (K slot) | Ai lane | 320–380 px, 1600 px altında drawer; şirket değişince oturum sıfırlanır; `DEFINED_DISABLED` iken görünmez işlev yok; durum sözlüğü rozetleri |
| `ProposalPreviewCard` | Ai lane | Fark = yürütülecek payload; `boundHash` ve `boundTargetVersion` onay isteğinde gider; `Stale` → "yeniden önizle" (aynı niyet); `Unknown` → "sonucu belirsiz" ve **yeniden dene butonu yok** |
| `AiCenterPage` (AI-01) / `AiQualityPage` (AI-02) / `AiPolicyPage` (AI-03) | Ai lane | Brifing (`coverage.Partial` → "kısmi" rozeti, yeşil yok), eval sonuçları, kill switch durumu (salt-okunur; değişiklik K3 backend yetkisiyle) |
| `MeasureText` | paylaşılan | `{{measure:id}}` sunucuda render edilmiş metni gösterir; istemci sayı üretmez; markdown alt kümesi, HTML yok |

`uiState.ts` `RemoteViewState/ViewOutcome/classifyOutcome` aynen kullanılır; yeni durum eklenmez ("sonucu belirsiz" `ready` verisinin bir alanıdır, HTTP durumu değil).

### 17.4 Frontend testleri

- `ProductCreatePage.test.tsx` deseni: capability yayımlanmadığında panel/`yapay zeka` metni **yok** (mevcut test bugün yeşil; AI capability gelince beklenti tersine döner ve test güncellenir — K1).
- `manufacturingNavEntry.test.tsx` deseni: yetkisiz aktör `/ai-merkezi`'ye URL ile gidince `not-found`, hiçbir `/ai/` okuması yapılmaz, sidebar'da link yok.
- `GateDecisionCard`: AI capability kapalı + `work.items.view` açık → kart görünür ve çalışır (G-29).
- `ProposalPreviewCard`: `unknown` sonucu → yeniden deneme kontrolü **render edilmez**; ikinci onay tıklaması aynı `idempotencyKey` ile gider.
- Erişilebilirlik: klavye/odak, `aria-live` durum rozetleri, hareket azaltma (`ERP-000K` §15).

## 18. Test stratejisi ve CI kapıları

**Statü:** Bu bölümdeki bütün testler **kabul planıdır**; hiçbiri yazılmamış veya koşulmamıştır. Bir satırın burada bulunması, o davranışın uygulanıp geçtiği anlamına gelmez.

### 18.1 Birim ve sözleşme testleri (`GoymenERP.Ai.UnitTests`, `GoymenERP.Work.UnitTests`; xunit 2.9.3)

| Test sınıfı | Kanıtladığı |
|---|---|
| `AiProposalStateMachineTests` | §5.1 durum makinesi; `Reissue` `Dispatched/Unknown/Executed` için fırlatır; `Approve` maker≠checker, ikinci onaycı ≠ birinci, kota; `MarkDispatched` hash dondurma; `MarkFailed(ContentConflict)` kilidi korur |
| `OperationIntentIdentityTests` | Üç durum tablosu (§5.1): aynı niyet+aynı içerik → aynı key; aynı niyet+farklı içerik yalnız **gönderim öncesi** `Reissue` ile oluşur (rev+1, aynı key, yeni hash) ve bir niyetin yalnız bir revizyonu gönderilebilir; gönderilmiş/`Failed` niyette `Reissue` reddi; farklı niyet+aynı içerik → farklı key; `Unknown` varken aynı kullanıcı+araç için yeni create niyeti `intent_pending_recovery` |
| `AiCanonicalJsonTests` | Alan sırası/kültür/boşluk farkı aynı hash; anlamlı fark farklı hash; hash küçük harf 64 |
| `AiPreviewEnvelopeTests` | Hash kapsamı zarfın tamamı: payload, sözleşme, araç sürümü, hedef/beklenen sürüm veya politika sürümü değişince hash değişir; model açıklaması zarfta yok; `Matches` Proposal satırıyla uyuşmazlığı yakalar |
| `AiPolicyEvaluatorOrderTests` | 13 adım sırası ve kısa devre; provider `authorized/approved` alanı etkisiz; `permission_unregistered` 503; temsil edilemeyen kapsam fail-closed |
| `AiToolCatalogTests` | Default-deny; bilinmeyen tool/sürüm; `Retired`; çıplak `decimal` çıktı alanı → başlatma reddi; manifest sürümü dondurma |
| `AiOutputValidatorTests` | Referanssız finansal sayı bastırma (G-31); yanlış varlık/dönem/senaryo referansı ret (G-32); `GateDecision` düzeyinde sayı içermeyen finans cümlesi ret (G-01); ölçü sözlüğü dışı ölçü ret (G-14); `abstained=true` kabul |
| `AiMeasureRendererTests` | `{{measure}}` → tr-TR biçim; `GateDecision` düzeyinde render yok |
| `ImpactRuleTrackerTests` (Work.Rules) | `EffectKind` içinde `Block` yok (reflection); Sorun yaşam döngüsü `Open→Acknowledged→Reopened→Resolved`; `Partial` sonuç durumu değiştirmez; aynı varlık yeni sürüm → yeni Değerlendirme, yeni Sorun yok; kural sürümü değişimi; **değişmez:** `AwaitsDecision` ve açık görev yoksa görev sonuç `Unchanged` olsa da oluşur; `Acknowledged` görev açmaz |
| `ImpactRuleGateTests` | `ImpactRuleGate` yapıcısında yazılabilir store/DbContext/iş öğesi bağımlılığı yok (reflection); `WorkReadOnlyDbContext.SaveChanges` fırlatır; aynı girdide tracker ile aynı kapı kararı |
| `IssueDecisionTests` | İlk tespit yalnız koşul varken ve `Raised` ile; koşul yoksa Sorun satırı yok; geçerli Finance kararı → `Acknowledged` (Sorun **açık**, `Resolved` olmaz); karar geçerliliği talimattan değil karar kaydından (tahsilat/zamanlı tetiklemede de doğru); istisna `Consumed/Expired/Revoked` → `Reopened`; tahsilat koşulu kaldırınca `Resolved` |
| `WorkItemEscalationGuardTests` | `OwnerRole` yalnız `EscalationStep+1` ve eşleşen `Escalated` olayıyla (adım alanı dahil) aynı save'de değişir; genel güncellemede rol değişikliği reddedilir; politika sürümü uyuşmazlığı reddedilir; Done/Cancelled öğe eskalasyonla açılamaz |
| `ImpactRuleTrackerLifecycleTests` | Karar → aynı işlemde görev kapanır; koşul kalkınca görev kapanır; istisna tükenince yeni karar görevi açılır (eski açık görev engellemez); gölge modda görev açılmaz; çakışmada tek yeniden deneme ve tek görev; değerlendirme satırı sonuç kodunu taşır |
| `RiskExceptionStateTests` | `Active(SingleShipment)` kapı kararı yalnız eşleşen talimat için `Proceed`; `Consumed` sonrası ikinci talimat `ReviewRequired`; `Expired` yeniden değerlendirme → `Reopened` |
| `AiProposalReissueTests` | `Reissue` yalnız Draft/Stale/ShadowOnly/Rejected(ReturnForFix)'ten; araç şu an gölge moddaysa Stale/Draft da gölge revizyon üretir; `ShadowOnly → Reissue → ShadowOnly` (gölge korunur, `AwaitingApproval` olamaz); `Approved` doğrudan reissue edilemez; eski revizyon `Superseded` olur ve `Approve`/`MarkDispatched` onu reddeder |
| `AiRecoveryCoordinatorTests` (fake port) | Süresi geçmiş `Dispatched` önce `Unknown` olur; replay bulundu → `Executed`; kayıt yok → en fazla bir gönderim; kurtarmada zarf bozuksa **gönderim yok**, `Unknown` + kilit + devralma görevi (Failed denmez); ikinci belirsizlik → iş öğesi; **hiçbir yolda yeni `OperationIntentId`**; same-key mismatch → `Failed(ContentConflict)` |
| `AiCommandDispatcherTests` | Gönderim öncesi zarf bozuk → `FailBeforeDispatch` (`Approved → Failed`, kilit yok, sahip çağrılmaz); `Approved` olmayan veya süresi geçmiş öneri gönderilmez; politika/hedef değişimi → `Stale`, yetki düşüşü → gönderim öncesi ret; beklenmeyen istisna → `Unknown`; kurtarma arada öneriyi `Unknown` yaptıysa geç sonuç taze satıra uygulanır; gönderim ve kurtarma aynı `AiEnvelopeVerifier` örneğini çağırır |
| `WorkflowRunnerTests` | Bekleme durumlarında model çağrısı yok (fake client çağrı sayacı 0); `Unknown` önce sorgu; bütçe aşımı → `Partial` |
| `AiModelRouterTests` | `CokGizli` → rota yok (her yapılandırmada); `Activated=false` → API rotası yok; router sağlayıcı ekleyemez |
| `AiEgressAllowlistHandlerTests` | Model üretimli URL → istek yok; `CokGizli` payload → fırlatma + security-event; `Authorization` log'a girmez |
| `AiContextBuilderTests` | İzinsiz kaynak zarfa girmez; manifestte içerik yok; kısmi kapsama |
| `AiStrictSchemaTests` | `additionalProperties=false`, tüm alanlar `required`, null-union; provider'dan dönen argüman yeniden doğrulanır |

### 18.2 Gerçek SQL kabul testleri (`[SqlServerFact]`, `GOYMEN_ERP_TEST_SQLSERVER_CONNECTION`; `Run.StartAsync` deseni — Ek K-1 madde 17a)

| Test | Senaryo |
|---|---|
| `WorkSqlServerRelationalAcceptanceTests.An_issue_is_created_once_per_rule_and_target_and_reevaluation_updates_it_without_a_second_issue` | `UX_Issues_IssueKey`; Değerlendirme append; kart tekilliği (`UX_WorkItems_OpenDedupKey`) |
| `…The_same_delivery_tracked_twice_replays_and_creates_one_work_item` | `work.Attempts` PK; `rule:{deliveryId}` replay; `UX_WorkItems_OpenDedupKey` |
| `…Company_B_sees_nothing_of_company_A_issues_items_and_events` | Query filter; count/timing nondisclosure |
| `…Work_rules_run_when_ai_capability_and_kill_switch_are_off` | `Ai:Enabled=false`, `Ai:KillSwitch=true` → kapı sorgusu ve iş öğesi çalışır (G-29) |
| `…A_failure_between_issue_and_work_item_leaves_neither_and_the_next_evaluation_creates_both` | Kesinti **tam iki adım arasına** yerleştirilir (Issue/Evaluation staged, WorkItem eklenirken interceptor hata fırlatır): hiçbir satır yazılmaz; sonraki değerlendirme (koşul sürdüğü için sonuç `Unchanged` olsa da) Sorun + görevi birlikte yazar |
| `…An_open_issue_without_a_work_item_gets_one_on_the_next_evaluation` | Veri tabanına elle "açık Sorun, görev yok" durumu yazılır (eski sürüm/elle müdahale simülasyonu); sonraki değerlendirme görevi tamamlar |
| `…A_gate_query_writes_nothing_in_any_schema` | Kapı sorgusundan önce/sonra bütün şemalarda (audit, outbox, security-event dahil) satır sayıları ve rowversion'lar aynı |
| `…An_escalation_changes_the_owner_role_only_with_its_event` | Eskalasyon komutu rol + adım + `Escalated` olayını tek save'de yazar; doğrudan rol güncellemesi reddedilir |
| `…A_single_shipment_exception_keeps_the_customer_risk_issue_open` | İstisna onayı → görev kapanır, Sorun `Acknowledged` kalır; ikinci talimat `ReviewRequired`; istisna tükenince `Reopened` + yeni görev |
| `AiProposalSqlServerRelationalAcceptanceTests.Two_screens_approving_the_same_proposal_produce_one_winner_one_key_one_effect` | Rowversion tek kazanan; iki `Approvals` satırı değil, bir onay + bir `version_conflict` (G-27) |
| `…Two_intents_with_identical_content_produce_two_owner_records_and_no_conflict` | **Aynı içerikte iki meşru sipariş**: iki `OperationIntentId`, iki `sales.DraftAttempts` satırı (gerçek Sales store ile), iki taslak |
| `…An_unknown_dispatch_is_recovered_with_the_same_key_and_never_creates_a_second_record` | Dispatch sırasında sahip kaydetti, gateway `Unknown`; koordinatör aynı key → replay (`Idempotency-Replayed`) → `Executed`; `sales.DraftAttempts` tek satır (G-11/G-26) |
| `…An_unknown_dispatch_that_never_reached_the_owner_is_sent_at_most_once` | Sahipte kayıt yok; bir gönderim → tek kayıt; ikinci belirsizlik → `Unknown` + `TargetLock` + `work.WorkItems` devralma; aynı hedef yeni niyet 409 |
| `…A_second_approval_by_the_same_actor_is_refused_and_a_target_change_between_approvals_makes_the_intent_stale` | İki onay; SoD; `Stale`; `Reissue` aynı niyet rev+1 |
| `…A_same_key_different_payload_from_the_owner_marks_the_proposal_failed_and_locks_the_target` | Sahip 409 mismatch (test, sahip deneme satırını farklı hash'le önceden yazar) → `Failed(ContentConflict)` + security-event; kilit kalır |
| `…An_owner_version_conflict_at_dispatch_fails_the_intent_and_a_retry_needs_a_new_intent` | Gönderimde sahip `version_conflict` → `Failed`; aynı niyetle `Reissue` reddedilir; yeni niyet → yeni key → yürür |
| `…An_approver_whose_permission_was_revoked_after_approval_cannot_dispatch` | Scope-yerel aktör = onaylayan; sahip fence'i güncel yetkiyi doğrular → ret; ikinci etki yok |
| `…Commit_then_crash_before_proposal_update_is_recovered_without_a_second_write` | `onStaged`-benzeri kanca ile `SaveAsync` sonrası süreç kesilir; yeniden başlatma → kurtarma → tek kayıt |
| `AiRunSqlServerRelationalAcceptanceTests.Read_draft_simulate_tools_leave_no_business_outbox_or_command_rows` | Sahip şemalarında satır sayısı değişmez (negatif test) |
| `AiWorkflowSqlServerRelationalAcceptanceTests.Worker_restart_with_running_instances_does_not_duplicate_work` | Lease/rowversion; `Unknown` kontrolü; tek iş öğesi (G-18) |
| `…Duplicate_outbox_delivery_creates_one_job` | `ai.Inbox` PK (G-19) |
| `…Exception_expiry_reevaluates_and_reopens_the_issue_without_a_new_card` | Zamanlı tetikleyici; `Reopened` (yeni Sorun yok); yeni **karar görevi** açılır |
| `AiProposalSqlServerRelationalAcceptanceTests.A_superseded_revision_can_never_be_dispatched` | `UX_Proposals_OneLiveRevisionPerIntent`; eski revizyon gönderim denemesi reddedilir |
| `…A_shadow_proposal_reissued_stays_shadow` | Gölge öneri yeniden üretilince `ShadowOnly`; onay endpoint'i `NotApprovable` |
| `…An_expired_dispatch_is_moved_to_unknown_before_recovery` | `Dispatched` + süre aşımı → `Unknown` → kurtarma yolu |
| `AiSnapshotSqlServerRelationalAcceptanceTests.A_composite_read_sees_one_snapshot_when_an_allocation_lands_midway` | Mekanizma A (`SNAPSHOT`); B için küme üyeliği tespiti (G-28) |
| `AiMigrationSqlServerRelationalAcceptanceTests.Migrations_apply_and_roll_back_on_an_empty_database` | `MigrateAsync` + `Down` (canlıya çıkmamış) |

### 18.3 HTTP kabul (in-memory `GoymenErpApiFactory` + stub servis; Ek K-1 madde 17b)

`AiHttpAcceptanceTests`: metadata (tek `Idempotency-Key`, `X-Company-Id`), 201 + Location, replay başlıkları, problem sınırı (401/403/404/503 kodsuz), `command_metadata_invalid`, `unsupported media type`; `WorkHttpAcceptanceTests` aynı; `RulesGateHttpAcceptanceTests`: kapı cevabında **tutar alanı olmadığı** şema testi (`exact` alan seti).

### 18.4 Mimari ve sözleşme kapıları

- `ModuleDependencyGuardTests`: allowlist'e §2.3 kenarları; başka kenar yok.
- Yeni: `WorkNeverReferencesAiTests`, `AiAndWorkNeverUseRawSqlTests`, `AiWriteToolsAreOnlyInvokedByDispatcherTests` (reflection: `IAiTool` WRITE `ExecuteAsync` çağıran tek tip `AiCommandDispatcher`/`AiRecoveryCoordinator`), `SystemActorHasNoWriteToolsTests`, `ScopedActorOverrideIsOnlySetByRuntimeFactoriesTests` (`IScopedActorOverride.Set` çağıranlar yalnız `AiDispatchScopeFactory`, `RulesRuntime` ve worker scope fabrikaları), `GateQueryHasNoWriteDependenciesTests` (kapı endpoint'inin DI grafiğinde tracker/store/DbContext/iş öğesi komutu yok).
- `PlatformTenantQueryFilterBypassGuardTests` envanteri: AI/Work'te `IgnoreQueryFilters` yok (sıfır kayıt).
- OpenAPI drift (`contract-drift` işi) ve shell capability revizyon testleri (`C01E2eShellCapabilityActivation`).
- Eval harness (`GoymenERP.Ai.EvalTests`): fake provider + golden set (master Ek D G-01…G-40 + `ERP-000J` §20 1–35); P0 güvenlik seti CI'da zorunlu; iş kalitesi seti raporlayıcı.

### 18.5 CI (`.github/workflows/ci.yml` — K3; yalnız kalite kapısı)

Mevcut işlere ek: `ai-unit`, `work-unit`, `ai-eval-p0` (fake provider; ağ yok), gerçek SQL kabul işlerine yeni test sınıfları (`--filter FullyQualifiedName~Ai|Work`), architecture guard, frontend `vitest` (ai/work lane'leri). Opt-in sağlayıcı sözleşme testi **yalnız** `OPEN-020` kabulünden sonra ve ayrı, elle tetiklenen workflow'da (`workflow_dispatch`; secret'lar CI'da değil). `.github/**` değişikliği deployment/secret/production yüzeyine dokunmaz.

---

## 19. Yapılandırma, bayraklar, secret'lar

```jsonc
// appsettings.json (ÖNERİ; değerler placeholder) — .env.example yalnız placeholder taşır
"Work": { "Rules": { "Enabled": true, "ShadowOnly": true } },            // AI'dan bağımsız; ShadowOnly: bulgu kaydı var, iş öğesi üretimi kapalı
"Ai": {
  "Enabled": false,                                                       // modül kaydı; false → capability DEFINED_DISABLED, endpoint'ler 503 ai_disabled
  "KillSwitch": false,                                                    // acil: bütün model çağrıları ve panel kapalı; Work etkilenmez
  "CloudGateway": { "Activated": false },                                 // OPEN-020 aktivasyon kanıtı; yalnız insan; API'ye çıkış bunsuz imkânsız
  "Providers": { "primary": { "Kind": "HttpJsonSchema", "Endpoint": "", "DeploymentRef": "", "SecretName": "AI_PROVIDER_API_KEY", "MaxDataClass": "Gizli", "AllowedHosts": [] },
                 "fake":    { "Kind": "Fake", "FixturePath": "eval/fixtures" } },
  "Routes": { "Genel": ["primary"], "SirketIci": ["primary"], "Gizli": ["primary:minimized"], "CokGizli": [] },   // CokGizli boş liste; kod ayrıca sabit reddeder
  "Tools": { "finance.risk.evaluate@1": { "Enabled": true }, "sales.draft.create@1": { "Enabled": false } },       // tool bazlı flag; WRITE beyaz listesi Faz 4
  "Preview": { "Expiry": "24:00:00" },                                     // NEEDS-SINAN NS-AI-05
  "Budgets": { "PerRun": { "MaxToolCalls": 12, "MaxTokens": 20000, "MaxSeconds": 60, "MaxCostMicros": 500000 }, "Company": { "MonthlyCapMicros": 0 } },
  "Workflow": { "Enabled": false, "Standalone": false, "PollInterval": "00:00:15", "LeaseDuration": "00:05:00", "DispatchGracePeriod": "00:02:00" },   // DispatchGracePeriod > en uzun WRITE aracı Timeout + pay (AiOptions başlangıçta doğrular); yine de yarış §10.3 ApplyLateOutcome ile kapanır
  "Retention": { "PayloadVault": "90.00:00:00", "PromptContent": "0.00:00:00" }   // OPEN-018/020/021; 0 = içerik saklanmaz
}
```

- Secret'lar ortam değişkeni/KMS'ten `IAiSecretProvider` ile okunur; log, trace, `ai.*` tabloları ve prompt'a girmez; `.env.example` yalnız `AI_PROVIDER_API_KEY=` placeholder'ı taşır.
- `Ai:CloudGateway:Activated` ve `Ai:Tools:*:Enabled` (WRITE) değişiklikleri production'da **insan** işidir (production aktivasyonu Sinan); uygulama bu değerleri UI'dan değiştiremez (AI-03 salt-okunur gösterir).
- `DatabaseStartupMode` ile aynı biçimde `Ai:Workflow:Enabled` OpenAPI üretim modunda her zaman `false`.

---

## 20. Gözlemlenebilirlik

- Üç kanal (`ERP-000J` §18): business audit sahip transaction'da (`AiContext`); yetkisiz/ret/injection/tenant-crossing → `IAiSecurityEventSink` → `ERP-000L` durable security-event (kanal yoksa **fail-closed**: WRITE dispatch ve yetki reddi gerektiren yollar çalışmaz; READ araçları yalnız `Ai:SecurityEvents:AllowTelemetryFallback=true` ile ve yalnız gölge modda); provider/ağ/gecikme → telemetry.
- Log alanları: `runId, proposalId, operationIntentId, toolId@v, policyStep, decisionCode, durationMs, costMicros, correlationId`; **ham `CompanyId`/`ActorId`/provider id metric label'ı değildir** (`ERP-000L`); trace'te payload yok.
- Metrikler: `ai_runs_total{status}`, `ai_tool_calls_total{tool,version,decision}`, `ai_proposals_total{status}`, `ai_recovery_total{outcome}`, `ai_cost_micros_sum{jobClass}`, `work_items_open{ownerRole}`, `work_escalations_total{policy,step}`, `work_rule_evaluations_total{rule,outcome}`.
- Runbook'lar (docs; kod değil): kill switch, provider outage, recovery devralma (`Unknown` listesi → `resolution` endpoint'i), registry revizyon uyuşmazlığı.

---

## 21. Kodlama sırası ve PR dilimleri (kuyruğa alma değildir)

Bu bölüm master §11 fazlarını PR dilimlerine çevirir; her satır yürürlükteki şerit/K sınıfı kaydına bağlıdır (NS-AI-17). **Hiçbir satır bu belgeyle başlamaz**; Faz 0 kapıları (ADR-008, OPEN-020, `ERP-000J-AI-CONTRACT`, F/L kabulü) ve Sinan kararı gerekir. `Modules.Work` dilimi AI kapılarını beklemez ama **kuyruğa alınması** ayrı karardır (NS-AI-01).

| # | PR dilimi | İçerik | Şerit | K | Kanıt (merge şartı) |
|---|---|---|---|---|---|
| W-1 | Work çekirdeği | `Modules/Work` Contracts/Domain/Application/Infrastructure; `work` migration; `WorkItemService`; `WorkShellCapabilityProvider`; allowlist kenarı; in-memory factory | Codex | K3 (migration, permission, capability) | gerçek SQL: attempt replay, tenant, guard; registry pin |
| W-2 | Work.Rules motoru | `Work.Rules` + `RulesRuntime` iskeleti; Issue/Evaluation yaşam döngüsü; kapı sorgusu endpoint'i (ShadowOnly); IR-LOG-001 kural sınıfı (sahip port'u gelene kadar `Unavailable` döner) | Codex | K2/K3 | G-02/G-03/G-29 SQL; DI grafiği testi |
| W-3 | Çalışma Merkezi UI | `src/work` lane, `GateDecisionCard`, shell entegrasyonu, drift testi | Claude | K1/K2 | vitest; a11y |
| A-1 | Ai iskeleti | `Modules/Ai` Contracts/Domain/Application/Infrastructure; `ai` core migration; `AiRun`; katalog; 13 adım; fake provider; capability provider + pin | Codex | K3 | `ERP-000J` §20.1–20.2 (1–12) |
| A-2 | READ araçları | `AiRuntime/Tools` Faz 1 seti (master Ek A #1–17, #19–20); `AiContextBuilder`; validator/renderer | Codex | K2 | sıfır etki negatif testi; G-31/G-32 |
| A-3 | Panel UI | `AiContextPanel`, durum sözlüğü, AI Merkezi iskeleti | Claude | K1/K2 | "AI kapalıyken disabled" |
| A-4 | Proposal/Approval | `ai.Proposals/Approvals` migration; `AiPreviewService`, `AiApprovalBinding`, vault; endpoint'ler (dispatch yok) | Codex | K3 | tek kazanan; çift onay; SoD; `Reissue` |
| A-5 | DRAFT/SIMULATE + gölge | DRAFT araçları; `finance.risk.evaluate@1` adaptörü (W4-RISK port'u şart); Work.Rules kuralları `ShadowOnly`; AI-02 | Codex + Claude | K2 | gölge raporu |
| A-6 | `AiContext` sözleşme eki | Modül zarflarına `AiContext?`; `X-AI-Run-Ref` sunucu doğrulaması; audit `Assisted/Proposed` | Codex | K3 (cross-module) | G-30 |
| A-7 | Workflow worker | `Ai.Workflow`: Jobs/WorkflowInstances/Inbox migration; worker; şablonlar (termin, vade, belge, risk, brifing); recovery sweeper; `ScopedCurrentUser` + `IScopedActorOverride` (platform kimlik kaydı) | Codex | K3 | G-18/G-19; yeniden başlatma; override yalnız runtime fabrikalarından |
| A-8 | AI-01 yönetim UI + bildirim | Brifing, istisna kartları, SignalR | Claude | K1/K2 | E2E |
| A-9 | WRITE beyaz listesi | `AiCommandDispatcher`, `AiRecoveryCoordinator`, `resolution` endpoint'i; 8 WRITE aracı (tool flag'leri kapalı) | Codex | K3 | §18.2 Proposal senaryolarının tamamı; §10.6 DOĞRULANACAK satırları kapatılmış |
| A-10 | Bilgi katmanı | Knowledge items/versions; hibrit erişim; öneri→sürüm | Codex | K3 | ACL testleri; G-15/G-21 |
| A-11 | Ses/uzman görev/senaryo | STT adaptörü (ADR sonrası); `AiTaskSpec`; senaryo motoru | Codex + Claude | K2 | gecikme/kalite |

Her PR'ın açıklaması ilgili karar/feature spec referansını, CompanyId ve yetki testlerini, audit/idempotency/concurrency etkisini, migration/rollback etkisini ve log/metric/trace notunu taşır (AGENTS.md PR kapısı). K3 PR'lar iki bağımsız exact-head review + gerçek SQL/browser/failure kanıtı ister.

---

## 22. Kapanış kontrol tablosu

Üç durum ayrı gösterilir: **Tasarımda karşılandı** (ilke ve sözleşme yazılı) · **Örnek eşitlendi** (metin, şema ve kod örneği bu sürümde birbiriyle çelişmiyor; son denetimde çelişki bulunan satırlar v1.1'de düzeltildi) · **Uygulamada test edilecek** (kabul testi §18'de planlı; yazılmadı, koşulmadı). Hiçbir satır "uygulandı" veya "test geçti" anlamına gelmez.

| Konu | Tasarımda karşılandı | Örnek eşitlendi | Uygulamada test edilecek | Konum (bu belge / master) |
|---|---|---|---|---|
| Kalıcı `OperationIntentId`, içerik hash'inden ayrı; üç durum | Evet | Evet (v1.0) | Evet | §5.1, §4.3 / master §5.5, §5.6 |
| Belirsiz sonuç için yeni kimlik yok | Evet | Evet | Evet | §5.1, §10.4 / §5.6 |
| Sorun kaydı ile görev arasında kesinti boşluğu | Evet | **v1.1'de düzeltildi** (tek işlem + değişmez) | Evet (kesinti iki adım arasına) | §9.1, §18.2 / §5.7, §5.8 |
| Eskalasyonda rol değişikliği ile yazma koruması çelişkisi | Evet | **v1.1'de düzeltildi** (rol yalnız eskalasyon olayıyla çift; yalnız açık öğe) | Evet | §4.2, §5.4 / §5.7 |
| Kapı sorgusunun yazma yapabilen motoru çağırması | Evet | **v1.1'de düzeltildi** (salt-okunur arayüz; takip ayrı komut) | Evet (satır sayısı/rowversion değişmez) | §9.1, §9.3, §9.4 / §3.3, §3.4 |
| Gönderim ve kurtarmada aynı zarf kontrolü | Evet | **v1.1'de düzeltildi** (`AiEnvelopeVerifier`, ortak `ExecuteAsync`) | Evet | §10.3, §10.4 / §5.6 |
| Gönderim öncesi ret ile gönderim sonrası belirsizliğin ayrı geçişleri | Evet | **v1.1'de düzeltildi** (`FailBeforeDispatch`; `Dispatched → Unknown` açık adım) | Evet | §5.1, §10.3, §10.4 / §5.6 |
| `Reissue` gölge modu ve eski revizyonu koruma | Evet | **v1.1'de düzeltildi** (beyaz liste, gölge korunur, `Superseded`, tekil canlı revizyon indeksi) | Evet | §5.1, §4.3 / §5.5, §5.6 |
| Tek sevkiyat istisnası: risk koşulu / sevkiyat izni / görev ayrımı | Evet | **v1.1'de düzeltildi** (Sorun `Acknowledged` kalır, koşul calculator'dan) | Evet | §5.3, §5.5 / §3.4, G-36 |
| İki onay gerektiren işlem | Evet | Evet | Evet | §5.1 / §5.6 |
| Kanıt matrisi ("repoda var" yerine dosya:satır) | Evet | **v1.1'de daraltıldı** (Sales SQL testi okundu; Counterparty farklı-aktör ve Accounting key düzeyi eksikleri açık) | Sahip dilimde eksik testler | §10.6 / §5.6a |
| API-birincil uzlaştırması; aktivasyon ayrı | Evet | Evet | Evet (router) | §11, §19 / §1, §5.10 |
| Nötr kapı metni öneridir | Evet | Evet | — | §9.3 / §3.4, NS-AI-10 |
| `Work` tasarımı ≠ geliştirme kuyruğu; repo/karar/görev/kod/izin değişmez | Evet | Evet | — | §0.2, §21 / §0.1 |

---

## 23. Açık noktalar (bu belgenin kapatamadıkları)

| ID | Konu | Tür |
|---|---|---|
| K-AÇIK-01 | `IFinanceRiskEvaluationV1` ve `finance.risk.shipment-gate@1` port'ları (`W4-RISK`) — bugün yok; IR-LOG-001 bunlarsız `Unavailable` döner | DOĞRULANACAK (sahip dilim) |
| K-AÇIK-02 | Sahip modüllerde salt-okunur `*ReadParticipant.EnlistAsync` (mekanizma A) ve `IIdempotencyProbeV1` (kurtarma sorgusu) port'ları | ÖNERİ; sahip dilim eki; olmadan B mekanizması ve "en fazla bir gönderim" yolu geçerli |
| K-AÇIK-03 | `ALLOW_SNAPSHOT_ISOLATION` doğrulaması (readiness probe genişletmesi) | NS-AI-16 |
| K-AÇIK-04 | Accounting normal fiş taslağı için key düzeyi gerçek SQL replay/mismatch testi | DOĞRULANACAK (Faz 4 öncesi) |
| K-AÇIK-05 | Counterparty create yolunda 2601/2627 filtresi ve kazanan yoksa `throw;` davranışı; Accounting/Manufacturing tracker clear | Sahip modül konusu; AI adaptörü varsaymaz |
| K-AÇIK-06 | ~~Kapı sorgusu on-demand değerlendirme kaydı~~ → v1.1 önerisi: kapı sorgusu hiçbir şey yazmaz; kalıcı takip ayrı komut (§9.3–9.4) | ÖNERİ; onay ADR-008 madde 6 |
| K-AÇIK-07 | `AiContext?` alanı için altı modülün metadata record'larında geriye uyumlu ek (F2-8) | K3 sözleşme; Codex |
| K-AÇIK-08 | Sağlayıcı/SDK/model; strict schema yeteneklerinin doğrulanması; OPEN-020 aktivasyon kanıtı | ADR-008 / NEEDS-SINAN |
| K-AÇIK-09 | 05.09.2026 v2.1 tercihinin (`AI-K01`) `docs/decisions/`'a kaynak izli aktarımı | Faz 0 F0-2 idari adım (Sinan/ChatGPT şeridi) |
| K-AÇIK-10 | `work.items.create.system` ve diğer izin adları; rol atamaları | NS-AI-02 |
| K-AÇIK-11 | Vektör deposu (SQL Server yerleşik `vector` türü) | NS-AI-07 |
| K-AÇIK-12 | Eskalasyon SLA değerleri, preview expiry | NS-AI-04/05 |
| K-AÇIK-13 | `ICurrentUser` kaydının `ScopedCurrentUser`'a çevrilmesi (gönderim/worker scope'unda scope-yerel aktör) ve Identity'de kullanıcı → şirket erişimi okuma port'u (`IIdentityCompanyAccessReader`) | K3 platform değişikliği; port bugün yok — DOĞRULANACAK |
| K-AÇIK-14 | Zamanlı iş akışı şablonlarının `RunAsActorId`'si (hangi gerçek kullanıcı adına okur) | NS-AI-02 / politika kaydı |

---

## 24. Değişiklik günlüğü

**v1.1 (28 Eylül 2026 — kapanış kontrolü bulguları):**

1. Sorun/değerlendirme ile görev oluşturma tek veritabanı işlemine alındı; görev üretimi "yalnız Raised" koşulundan çıkarılıp "karar bekleyen sorunun açık görevi yoksa" değişmezine bağlandı (§9.1, §5.3).
2. Eskalasyonda `OwnerRole` değişikliği yazma korumasına yalnız `EscalationStep+1` ve eşleşen `Escalated` olayıyla çift olarak eklendi; öğe politika sürümüne bağlandı (§4.2, §5.4).
3. Kapı sorgusu yazma yapabilecek hiçbir bağımlılığı olmayan `IImpactRuleGate`'i çağırıyor; kalıcı takip ayrı `POST /rules/evaluations` komutu (§9.1, §9.3, §9.4); K-AÇIK-06 öneriyle kapatıldı.
4. Gönderim ve kurtarma aynı `AiEnvelopeVerifier` ve aynı `ExecuteAsync` adımını kullanıyor; gönderim öncesi bozuk zarf `FailBeforeDispatch` ile, süresi geçmiş gönderim açık `Dispatched → Unknown` adımıyla işleniyor; kurtarmada bozuk zarf gönderilmiyor ve "Failed" denmiyor (§5.1, §10.3, §10.4).
5. `Reissue` beyaz listeyle sınırlandı; gölge öneri gölge kalıyor; eski revizyon `Superseded` oluyor; bir niyetin tek canlı revizyonu indeksle zorlanıyor (§5.1, §4.3).
6. Tek sevkiyat istisnası müşterinin risk Sorununu kapatmıyor: Sorun `Acknowledged` kalıyor, koşul calculator yargısından geliyor, kapı izni yalnız o talimat için, karar görevi kapanıyor (§5.3, §5.5).
7. İçerik taşıyan append-only alanlar (ölçü değerleri, yorum, düzeltme) içerik deposuna referansla taşındı (§4.2, §4.3, §4.5).
8. Sohbetten görev açma: AI yalnız taslak üretir, kullanıcı Work ekranında kaydeder (§0.4, §10.6, §14.4).
9. Kanıt matrisinde Sales satırı repodan doğrulandı; testler bölümüne "kabul planıdır" statüsü, kapanış tablosuna üç durumlu gösterim eklendi (§10.6, §18, §22).
10. İkinci iç kontrol: Finance kararı `Issue.Apply` içinde `Acknowledged`'a taşınır ve karar olayları kuralı tetikler; tracker görevi aynı işlemde açar ve kapatır; Sorun yalnız koşul varken `Raised` ile doğar; değerlendirme sonuç hesaplandıktan sonra yazılır; çakışmada işlem bir kez yeniden denenir; gölgede görev açılmaz; kural politikası salt-okunur bağlamdan okunur; eskalasyon yalnız açık öğede, olay satırı adımı taşır; gönderici durum/süre/politika reddini ele alır, genel istisna `Unknown`, geç sonuç taze satıra uygulanır; `Reissue` aracın güncel gölge modunu ve yalnız ReturnForFix reddini kabul eder, eski/yeni revizyon iki save + tek işlemle yazılır (§5.1, §5.3, §5.4, §9.1–9.3, §10.1, §10.3, §19).

## Ek K-1 — Kod deseni kanıt tablosu (main@00a8798, salt-okunur)

Bu belgenin "mevcut desen" dediği her şeyin kaynağı. Satır numarası verilmeyen kayıtlar dosya düzeyinde doğrulanmıştır.

| # | Desen | Kaynak | Bu belgede nerede kullanıldı |
|---|---|---|---|
| 1 | Modül kaydı `AddXModule(configuration, AddInterceptors(AuditSaveChangesInterceptor))`, `MapXEndpoints()`, `InitializeXModuleAsync()`; middleware sırası `ExceptionHandler → ShellCapabilityTransport → CorrelationContext → Cors → [ValidateOnly iken DatabaseReadinessGateMiddleware] → Authentication → CompanyContext → Authorization` (Program.cs L303–306 koşullu kapı); `ICurrentUser → HttpCurrentUser` (L105); `public partial class Program;` | `src/backend/GoymenERP.Api/Program.cs` | §3.1 |
| 2 | `RequireCompanyContext<TBuilder>()` → `RequiresCompanyContextAttribute`; `CompanyContextMiddleware` `X-Company-Id` okur, `CanAccessCompany`, `companyContext.Set(companyId)` (write-once) | `src/backend/GoymenERP.Api/Multitenancy/CompanyContextEndpointExtensions.cs`, `CompanyContextMiddleware.cs`; `BuildingBlocks/Multitenancy/ICompanyContext.cs` | §3.1, §9.2, §10.3, §14.3 |
| 3 | `SalesModuleExtensions.AddSalesModule(services, configuration, configureDbContext)`: `DB_PROVIDER`/`DB_CONNECTION_STRING`, `MigrationsHistoryTable("__EFMigrationsHistory", Schema)`, named factory `CreateDraftService` ("architecture guard": `ICompanyContext` çözülür, kayıt edilmez), `InitializeSalesModuleAsync` (`MigrateAsync`/`EnsureCreatedAsync`), `DesignTimeCompanyContext`, `InternalsVisibleTo` | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/SalesModuleExtensions.cs` | §3.2 |
| 4 | Endpoint konvansiyonları: `MapGroup(...).WithTags().RequireAuthorization().RequireCompanyContext()`; `SetPrivateNoStore`, `TryReadIdempotencyKey` (tek, trim, ≤128, virgülsüz), JSON içerik türü 415, `SalesCommandMetadata(idempotencyKey, correlationId, causationId, now)`, `ApplyReplayHeaders`, `Location`, `.WithName().WithIdempotencyContract(201, 409).Accepts<>().Produces<>().ProducesErpBadRequest().AllowsEmptyErpBadRequestBody().ProducesErpUnsupportedMediaType().ProducesErpConflict().ProducesErpUnprocessableEntity().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems()`; helper'lar endpoint sınıfına özel `private static` | `src/backend/GoymenERP.Api/Endpoints/SalesEndpoints.cs` L231–305, L710–731, L799 | §9.3, §15.2 |
| 5 | `SalesDbContext`: `Schema="sales"`, `IdentityCollation="Latin1_General_100_BIN2"`, `OrganizationCompanyReference` (`ExcludeFromMigrations`), `ConfigureAuditEvent(excludeFromMigrations:true)` + audit query filter, `SaveChanges*` → `ApplyWriteGuards()` (Deleted yok; CompanyId eşit; izinli Modified alanları), `DraftAttempts` PK (CompanyId, CommandName, IdempotencyKeyHash), hash uzunluk 64, `SalesAuditEventTenantGuard.Validate` | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs`, `SalesRows.cs` L149–169 | §4.1–4.3 |
| 6 | Satır + factory: `PeriodRow` (`Version` concurrency token, `RowVersion`, `HasAlternateKey(CompanyId, Id)`, query filter); `AccountingPeriod.Restore(...)`; Purchasing `RestoreRequest` sürüm kontrolü | Accounting/Purchasing Infrastructure Persistence | §4.1, §5 |
| 7 | Servis akışı: `RequireAsync(permission)` fence (`ErpPermissions.IsKnown` → 503 `permission_unregistered`; `GetActiveRoleCodesAsync`; `RevalidatePermissionScopeAsync`; `IsGranted && IsCompanyWide && Matches`), request hash, `Envelope(scope, metadata)` → `SalesDraftWriteEnvelope(ActorId, permission, RoleCodes, CorrelationId, CausationId, IdempotencyKey, SourceChannel, ClientIp, UserAgent)`, `FindReplayAsync`, `StoreDraftAsync(envelope, hash, build)` tek save, `RaceWon`/`ConcurrentChange` | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Application/SalesDraftService.cs` (kod eşlemesi L1308–1316) | §7 (adım 5), §10.6 |
| 8 | `SalesStore.StoreDraftAsync`/`FindReplayAsync` (L148–176): attempt arama, `SameKeyPayloadMismatch`, terminal ret replay, `DraftAttemptRow` ekleme, `AuditEventDraft` enqueue, `SaveAsync` `DbUpdateConcurrencyException`/`SqlException 2601/2627` → `ChangeTracker.Clear` + `ConcurrentChange`; key hash büyük harf (L956–958), request hash küçük harf (`ISalesOrderDraftStore.cs` L237–241, L271–272) | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.cs` | §4.1, §10.6 |
| 9 | `AuditEventDraft(CompanyId, EventType, EntityType, EntityId, Operation, EntityVersionBefore/After, ActorType, ActorId, PermissionCode, RoleCodes, BeforeJson, AfterJson, Reason, ApprovalContextJson, AiInvolvement, AiContextJson, SourceChannel, ClientIpAddress, UserAgent, CorrelationId, CausationId, IdempotencyKey, Outcome)`; `AuditActorTypes {User, System, Integration, Ai}`; `AuditAiInvolvement {None, Assisted, Proposed, Executed}`; `IAuditEventCollector.Enqueue`; scoped `AuditEventCollector`/`AuditSaveChangesInterceptor`; `SourceChannels {Web, Mobile, Import, Integration, AiAssisted, System}` (ayrı dosya) | `src/backend/Modules/Audit/GoymenERP.Modules.Audit.Application/Contracts/AuditModels.cs`; `BuildingBlocks/Observability/SourceChannels.cs`; `…Audit.Infrastructure/Services/AuditEventCollector.cs`; `…Audit.Infrastructure/Persistence/AuditSaveChangesInterceptor.cs` | §10.3, §20 |
| 10 | `ICompanyContext { IsResolved; CompanyId; Set }`, `ICurrentUser { IsAuthenticated; UserId; AllowedCompanyIds; CanAccessCompany }`, `ICorrelationContext { IsResolved; CorrelationId; CausationId; SourceChannel; ClientIpAddress; UserAgent; Set(...) }` (middleware her zaman `Web` yazar); `PermissionScope(IsGranted, IsCompanyWide, RoleCodes, SiteIds, LocationIds, WorkCenterIds)`, `ErpPermissions` (`All/Critical/CompanyWideOnly`, `IsKnown/IsCritical/RequiresCompanyWideScope`), `SalesPermissionManifestV1 { Version; Subject; Names }`; `IShellCapabilityProvider { Owner; ContractVersion; GetDeclarations() }`, `ShellCapabilityDeclaration(...)`, `ShellCapabilityRegistry.ComputeCandidateRevision` (L40), `ShellCapabilityAuthorityCatalog` (`^sha256:[A-Za-z0-9_-]{43}$`), `C01E2eShellCapabilityActivation.OptionalModuleProviders`/`NormalAuthorities` (L74–85, L90–151, L153–169), `ShellCapabilityRegistryAuthorities` sabitleri | `BuildingBlocks/Multitenancy`, `BuildingBlocks/Security`, `BuildingBlocks/Observability`, `BuildingBlocks/Shell`; `GoymenERP.Api/Shell/*` | §6.2, §7, §16 |
| 11 | Cross-module host runner: `IPurchaseGoodsReceiptTransactionV1.ExecuteAsync(companyId, operation, beforeCommit)`; `PurchaseGoodsReceiptHost` `BeginTransactionAsync` → `Participant.EnlistAsync(connection, transaction, companyId, …)` → `operation` → `beforeCommit` → `CommitAsync`; hata → `RollbackQuietlyAsync` + `ChangeTracker.Clear()`; katılımcı `UseSqlServer(connection, contextOwnsConnection:false)` + `UseTransactionAsync` + `EnsureActive`; Program.cs L246–248 kayıt | `GoymenERP.Api/PurchasingRuntime/PurchaseGoodsReceiptHost.cs` L13–57, L97–101; `Inventory.Infrastructure/Persistence/InventoryDefiniteShipmentParticipant.cs` L55–93, L321–330; `GoymenERP.Api/LogisticsRuntime/LogisticsRealizationTransactionRunner.cs` | §8.2 (mekanizma A) |
| 12 | Tek `IHostedService`: `AuthorizationSnapshotDatabaseReadinessService` (`OpenApiDocumentGenerationMode.IsActive` dışında; `enableAuthorizationSnapshotDatabaseReadiness`); **backend'de `BackgroundService` yok** | `Identity.Infrastructure/IdentityModuleExtensions.cs` L19–22, L77–83, L109–149; Program.cs L189–192 | §14.1, §15.3, §19 |
| 13 | Outbox satırı `InventoryOutboxRow` (append-only, dispatcher yok), `ConfigureOutboxMessages` (check constraint'ler, `UX_OutboxMessages_AggregateSequence`), event sabitleri (`inventory.definite-shipment.recorded.v1`), yazma yeri `StageAsync` (outbox + `InventoryCommandRow` + audit tek save; 2601/2627 → 409); inbox `DeliveryRow` PK (CompanyId, Consumer, MessageId), `ReplayAsync` içerik uyuşmazlığı → `DeliveryContentConflict` | `Inventory.Infrastructure/Persistence/InventoryRows.cs` L218–238, L296–307; `InventoryDbContext.cs` L61, L451–478; `Inventory.Application/InventoryDefiniteShipmentOutboxV1.cs` L7–13; `Accounting.Infrastructure/Persistence/AccountingRows.cs` L82–101; `AccountingDbContext.cs` L231–236; `AccountingStore.cs` L804–818 | §4.3 (`InboxRow`, `JobRow`), §14.1 |
| 14 | `IdempotencyHash.ComputeKey/ComputePayload` (büyük harf; yalnız Organization/Counterparty), Sales/Inventory/Finance kendi hash'leri (payload küçük harf); `InventoryCommandRow`, `DraftAttemptRow`; replay-or-conflict kararı; komut adları sürümlü sabit (`sales.order-draft.create.v1`) | `Idempotency.Domain/Executions/IdempotencyHash.cs`; `SalesStore.cs` L148–176, L956–958; `InventoryRows.cs` L296–307 | §4.1, §5.1, §10.6 |
| 15 | `EngineResult<T>` (`Computed/Unavailable/Refused`, `MissingParameters`, `RefusalCode`), `MassBalanceEngineV1.Difference` (`FormulaVersion`, `EngineInputs`, "veri eksik" → `Unavailable`) | `Manufacturing.Domain/Engines/EngineResult.cs` L3–63; `Engines/Balance/MassBalanceEngineV1.cs` L8–22, L53–78 | §6.4 (adaptör), §5.5 |
| 16 | `ISettingsPrerequisiteProvider` (`GetUnitProfileAsync`, `GetMoneyProfileAsync`, `GetPeriodEligibilityAsync`; "sonuç yazma izni değildir; varsayılan profil ikame edilmez"), `MoneyProfileV1`, `SettingsPrerequisiteException`, kodlar; Program.cs L152 kaydı | `Settings.Application/Contracts/V1/ISettingsPrerequisiteProvider.cs` | §6 (ölçü birim/para semantiği) |
| 17a | Gerçek SQL kabul: `SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable`, `SqlServerFactAttribute` (Skip), `SqlServerRelationalApiFactory` (disposable DB `goymen_erp_ci_{guid}`), `CreateClientFor(userId, companyId)` başlıkları, `Run.StartAsync` (`/health/live` + `MigrateAsync`), `Envelope(...)`, `StoreScope` (`AuditSaveChangesInterceptor` ile), DB drop | `tests/GoymenERP.Api.IntegrationTests/SqlServerRelationalAcceptanceTests.cs` L21–24, L1105–1141, L1207–1215; `PurchasingSqlServerRelationalAcceptanceTests.cs` L16–57, L424–458 | §18.2 |
| 17b | HTTP kabul: `GoymenErpApiFactory` (`UseEnvironment("Testing")`, placeholder connection string, `UseInMemoryDatabases` her host-wired DbContext için), `WithWebHostBuilder(... services.Replace(ServiceDescriptor.Scoped<IPurchasingService>(_ => stub)))`, başlıklar | `tests/GoymenERP.Api.IntegrationTests/PurchasingHttpAcceptanceTests.cs` L1–16, L41–55, L175–185; `GoymenErpApiFactory.cs` L26–28, L40–45, L73, L81, L115–121 | §3.3, §18.3 |
| 17c | Architecture: `ModuleDependencyGuardTests` (allowlist birebir; unauthorized ve stale kenarlar), `IsRestrictedCrossModuleTarget` (`.Domain`/`.Infrastructure`), allowlist şeması (`source/target/adr/reason`); `PlatformTenantQueryFilterBypassGuardTests`, `C01E2eActivationBoundaryTests`, `PlatformRequestMetadataProblemOwnershipGuardTests` | `tests/GoymenERP.ArchitectureTests/ModuleDependencyGuardTests.cs` L7–52, L96–121, L176–181; `docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json` | §2.3, §18.4 |
| 17d | Frontend testleri: AI paneli yok testi (`ProductCreatePage.test.tsx` L250–257), route gate + okuma yok + sidebar yok (`manufacturingNavEntry.test.tsx` L118–144), drift (`salesMountDrift.test.ts` L17–22) | `src/frontend/goymen-erp-web/src/**` | §17.4 |
| 18 | Frontend: `CapabilityRoute` (yetkilendirme değil, açıklamaz; giriş başına yeniden doğrulama; `not-found`/`network-error`/`initial-loading`), `CAPABILITY`/`CAPABILITY_ROUTE`/`CAPABILITY_NAV_LABEL`, `NAV_CAPABILITIES` (K5: create sidebar'da yok), lane `SALES_CAPABILITY`/`SALES_ROUTE`/`salesDetailPath`, `salesRoutes` (statik önce), `router.tsx`; `allocationApi.ts` `request/headers/command` (üç başlık; `failure` türleri; `Idempotency-Replayed`), `api.ts` `fetchDraftPage` (L121–138; `ViewOutcome`; başka şirket satırı → `malformed`) ve `createDraft` (L465–506), `contract.ts` `exact()`/`decodeDraftMutation`/`decodeDraftPage`/`decodeProblem`, `uiState.ts` `RemoteViewState/ViewOutcome/classifyOutcome` | `src/frontend/goymen-erp-web/src/shell/CapabilityRoute.tsx` L29–49, L69–73, L159–178; `shell/capabilities.ts`; `sales/capabilities.ts`; `sales/salesRoutes.tsx` L10–36; `shell/router.tsx` L22, L31–35, L147; `sales/allocationApi.ts` L14–57, L78–106; `sales/api.ts` L121–138, L465–506; `sales/contract.ts` L387–397, L659–681, L834–853; `shell/uiState.ts` L21–107 | §17 |
| 19 | OpenAPI: `GoymenERP.Api.csproj` (`Microsoft.AspNetCore.OpenApi 10.0.8`, `Microsoft.OpenApi [2.7.5]`, `Microsoft.Extensions.ApiDescription.Server 10.0.11`, `OpenApiGenerateDocumentsOnBuild`, `../../../artifacts`), CI `contract-drift` (`validate_dotnet_bootstrap_integrity.py`, `OpenApiContractGenerationTests`, `validate_openapi_contract.py --baseline contracts/openapi/goymen-erp-v1.json --generated artifacts/openapi.json`), `openapi-generate.yml` (`openapi:generate` etiketi), `OpenApiDocumentGenerationMode.IsActive` (Program.cs L58–84) | `src/backend/GoymenERP.Api/GoymenERP.Api.csproj` L29–50; `.github/workflows/ci.yml`; `.github/workflows/openapi-generate.yml`; `tools/validate_openapi_contract.py` L178–179 | §15.3, §18.5 |
| 20 | Build: `Directory.Build.props` (`Deterministic`, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`); `global.json` SDK 10.0.300; Sales Infrastructure/Application/Domain csproj'ları; test csproj paketleri (coverlet 6.0.4, Mvc.Testing 10.0.8, SqlClient 6.1.1, EF InMemory 10.0.8, Test.Sdk 17.14.1, xunit 2.9.3, runner 3.1.4); `GoymenERP.Api.csproj` modül Infrastructure referansları | kök ve ilgili csproj'lar | §2.2 |
| 21 | Şablonlar: `docs/adr/ADR_SABLONU.md`, `docs/features/FEATURE_SPEC_SABLONU.md`; mevcut ADR'ler 001, 002, 004, 009, 011, 013, CARI-001, INV-001, PRD-001 | `docs/adr`, `docs/features` | ADR-008 ve Work spec bu şablonlarla yazılır |

**Kodda doğrulanmış kısıtlar (yeni modülün sağlaması gerekenler):** (1) tek-save komutta açık transaction yok; `BeginTransactionAsync` yalnız çok-save store'larda (Accounting) ve host runner'larda. (2) Hash büyük/küçük harf farkı (madde 14). (3) `X-Correlation-Id` isteğe bağlı (sunucu üretir); komut endpoint'i `correlation.IsResolved` + tek `Idempotency-Key` ister, aksi 400 `command_metadata_invalid`. (4) Problem sınırı: 401/403/404/503 kodsuz. (5) Company context modülde çözülür, kayıt edilmez. (6) Shell provider pinli authority ister. (7) Permission adı `ErpPermissions.All`'da olmalı; aksi 503 `permission_unregistered`. (8) Host wiring: Program.cs üç çağrı; in-memory factory swap; `GoymenERP.Api.csproj` referansı; allowlist kenarı.

## Ek K-2 — Problem kodu ve HTTP durum sözlüğü (ÖNERİ)

| Kod | HTTP | Nerede | Anlam |
|---|---|---|---|
| `command_metadata_invalid` | 400 | tüm komut endpoint'leri | `Idempotency-Key` yok/çok/uzun/virgüllü veya correlation çözülmemiş |
| `validation_failed` | 400/422 | tüm | Gövde/alan hatası; `errors[]` |
| `same_key_payload_mismatch` | 409 | `ai.*`, `work.*` komutları | Aynı key farklı içerik/aktör (Sales/Purchasing/Logistics ile aynı yazım) |
| `version_conflict` | 409 | onay, iş öğesi güncelleme | Rowversion tek kazanan; kaybeden |
| `approval_binding_mismatch` | 409 | onay | `boundHash`/`boundTargetVersion` öneriyle eşleşmiyor → yeni önizleme |
| `proposal_expired` | 409 | onay | Expiry geçti → `Stale`, `Reissue` |
| `target_locked` | 409 | önizleme/onay | Aynı hedef için `Unknown` niyet var |
| `intent_pending_recovery` | 409 | önizleme (create) | Aynı kullanıcı+araç için `Unknown` create niyeti var |
| `intent_already_dispatched` | 409 | `Reissue` | Gönderilmiş/belirsiz niyet yeniden önizlenemez |
| `content_conflict` | (iç) | dispatcher | Sahip modül same-key mismatch döndü → `Failed(ContentConflict)` + security-event |
| `payload_integrity` | (iç) | dispatcher | Vault içeriği hash ile uyuşmuyor |
| `scope_not_representable` | 403 (kodsuz) | politika adım 5 | Site/Location/WorkCenter kapsamı araçta temsil edilemiyor → fail-closed |
| `permission_unregistered` | 503 | politika adım 4/5 | `ErpPermissions.All` dışı izin adı |
| `ai_disabled` / `ai_kill_switch` | 503 | tüm `ai/*` | Modül kapalı; Work etkilenmez |
| `security_event_channel_unavailable` | 503 | WRITE/onay yolları | `ERP-000L` kanalı yok → fail-closed |
| `budget_exceeded` | 429 | run | Bütçe tavanı; run `Partial` |
| `provider_unavailable` | 503 | run (model gerektiren) | Deterministik kartlar çalışır; panel degraded |
| `gate_tool_unknown` | 400 | kapı sorgusu | Tanımsız kapı aracı |
| `closure_evidence_required` | 422 | iş öğesi kapanış | Kanıt referansı yok |

Sahip modül kodlarının adaptör eşlemesi (`IsSameKeyMismatch`): `same_key_payload_mismatch` (Sales/Purchasing/Logistics), `idempotency_conflict` (Counterparty/Inventory/Accounting/Manufacturing), Finance gövde `outcome: "IdempotencyConflict"`; `*_concurrent_change` ve `version_conflict` → `Failed(code)` (kesin ret; kurtarma yok; yeni önizleme insan kararı).

## Ek K-3 — Dosya listesi (ÖNERİ; iskelet + Faz 1–4 ilk set)

**Backend — `Modules/Work`:** `Contracts/{WorkItemV1,IssueV1,GateDecisionV1,IWorkItemReadsV1,IWorkItemCommandsV1,WorkPermissionManifestV1}.cs` · `Domain/{WorkItems/WorkItem,WorkItemEvent,ApprovalRequest,EscalationPolicy,VisibilityMatrix; Issues/Issue,Evaluation,IssueKey; Rules/ImpactRuleDefinition,EffectKind,RuleTrigger,DisclosureMatrix; WorkRuleException,WorkCodes}.cs` · `Application/{IWorkItemService,IWorkItemStore,IImpactRuleGate,IImpactRuleTracker,IWorkRulesStore,IImpactRuleEvaluatorPort,IEscalationScheduler,WorkCommandMetadata,WorkWriteEnvelope,WorkProblemCodesV1,WorkItemService}.cs` · `Infrastructure/{WorkModuleExtensions,WorkShellCapabilityProvider,Persistence/WorkDbContext,WorkRows,WorkStore,WorkAuditEventTenantGuard,WorkDbContextDesignTimeFactory,Migrations/*}.cs` · `Rules/{ImpactRuleGate,ImpactRuleTracker,IImpactRuleCatalog,ImpactRuleCatalog,Catalog/IrLog001…,IrSal001…,IrPur003…,IrFin002…,IrInv003…,IrAcc001…,IrDoc001…}.cs`
**Backend — `Modules/Ai`:** `Contracts/{Tools/IAiTool,AiToolDescriptor,AiToolContext,AiDispatchInfo,AiToolResult,MeasuredValue,AiEnums; Proposals/ProposalV1,ApprovalV1; Audit/AiContext; AiToolCatalogManifestV1}.cs` · `Domain/{Runs/AiRun,AiRunStep,AiRunBudget,AiContextManifest; Proposals/AiProposal,AiProposalCodes,AiApproval; Knowledge/AiKnowledgeItem,AiKnowledgeVersion; Policy/AiPolicyVersion; Usage/AiBudget; Workflow/AiJob,WorkflowInstance,WorkflowState}.cs` · `Application/{Ports/*.cs (§2.1 listesi), Policy/AiPolicyEvaluator,Steps/*.cs, Context/AiContextBuilder,AiContextPlans, Output/AiOutputValidator,AiMeasureRenderer,AiMeasureDictionary, Preview/AiPreviewService,AiCanonicalJson,AiApprovalBinding, Dispatch/AiEnvelopeVerifier,AiCommandDispatcher,AiRecoveryCoordinator,AiDispatchScope, Orchestration/AiOrchestrator, Providers/IAiModelClient,AiModelOutcome,AiModelRouter, AiOptions,AiCommandMetadata,AiProblemCodesV1,AiServiceException}.cs` · `Infrastructure/{AiModuleExtensions,Persistence/AiDbContext,AiRows,AiRunStore,AiProposalStore,AiPayloadVault,Migrations/*; Providers/FakeAiModelClient,HttpJsonSchemaModelClient,AiEgressAllowlistHandler,AiSecretProvider; Prompts/AiPromptRegistry; Knowledge/AiKnowledgeIndex; Security/AiSecurityEventSink}.cs` · `Workflow/{AiWorkflowWorker,JobPoller,SchedulePlanner,InboxConsumer,WorkflowRunner,EscalationTicker,RecoverySweeper,Definitions/*.cs,AiWorkflowExtensions}.cs`
**Backend — `GoymenERP.Api`:** `RulesRuntime/{RulesRuntimeExtensions,RulesEndpoints,RulesOutboxConsumer,Evaluators/<Owner>/*.cs}` · `AiRuntime/{AiRuntimeExtensions,AiEndpoints,AiCapabilityEndpoints,AiShellCapabilityProvider,AiCommandEnvelopeFactory,AiDispatchScopeFactory,AiDispatchUser,AiReadSnapshotRunner,Tools/<Owner>/*Tool.cs}` · `Shell/{C01E2eShellCapabilityActivation (ek satırlar), ShellCapabilityRegistryAuthorities (yeni sabit)}.cs` · `Program.cs` (ek satırlar) · `GoymenERP.Api.csproj` (referanslar)
**Frontend:** `src/work/{capabilities,api,contract,workRoutes,WorkCenterPage,WorkItemDetailPage,GateDecisionCard,*.test.tsx}` · `src/ai/{capabilities,api,contract,aiRoutes,AiContextPanel,ProposalPreviewCard,MeasureText,AiCenterPage,AiQualityPage,AiPolicyPage,*.test.tsx}` · `src/shell/{capabilities.ts (ek), router.tsx (ek), AppShell.tsx (NAV ek), aiMountDrift.test.ts, workMountDrift.test.ts}`
**Testler:** `tests/GoymenERP.Work.UnitTests/*`, `tests/GoymenERP.Ai.UnitTests/*`, `tests/GoymenERP.Ai.EvalTests/{FakeProviderEvalTests, Fixtures/**}`, `tests/GoymenERP.Api.IntegrationTests/{Work,AiProposal,AiRun,AiWorkflow,AiSnapshot,AiMigration}SqlServerRelationalAcceptanceTests.cs`, `{Ai,Work,RulesGate}HttpAcceptanceTests.cs`, `tests/GoymenERP.ArchitectureTests/{WorkNeverReferencesAiTests,AiAndWorkNeverUseRawSqlTests,AiWriteToolsAreOnlyInvokedByDispatcherTests,SystemActorHasNoWriteToolsTests}.cs`
**Docs (bu belgeyle birlikte yazılacak, kod değil):** `docs/adr/ADR-008-ai-gateway.md` (şablon), `docs/features/ERP-000P_WORK_CORE_v1.md` (ad ÖNERİ), `docs/ai/{IMPACT_RULES,MEASURE_DICTIONARY,TOOL_CATALOG}.md`, `docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json` (2 kenar), `contracts/openapi/goymen-erp-v1.json` (yeniden üretim).

---

*Bu belge yalnız analiz ve kodlama tasarımıdır; hiçbir dosya listesi, kod bloğu veya sıra bir geliştirme görevini başlatmaz. Repo, karar sicili, görev listesi, kod ve izinler bu belgeyle değişmez.*
