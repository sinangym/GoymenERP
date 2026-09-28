# Göymen AI Merkezi — Kodlama Tasarımı v1.3

**Belge kimliği:** `GOYMEN_AI_MERKEZI_KODLAMA_TASARIMI_V1`
**Yayın:** 28 Eylül 2026 (v1.0); **revizyon:** 28 Eylül 2026 (v1.3 — yeniden kontrol bulguları R-01…R-09; v1.2 — ikinci kapanış kontrolü; v1.1 — kapanış kontrolü; değişiklik günlüğü §24)
**Belge türü:** `GOYMEN_AI_MERKEZI_MASTER_RAPOR_V1.md` (v1.5) ile tanımlanan Göymen AI Merkezi'nin **nasıl kodlanacağını** — çözüm yapısı, proje dosyaları, DI kaydı, veri modeli ve EF eşlemeleri, aggregate'ler ve durum makineleri, araç sözleşmesi, politika değerlendirici, bağlam kurucu, etki kuralı motoru, öneri/onay/gönderim/kurtarma, sağlayıcı portu ve router, ölçü referansı, bilgi katmanı, iş akışı motoru, endpoint'ler ve OpenAPI, capability provider, frontend, testler, CI, yapılandırma ve gözlemlenebilirlik — kod örnekleriyle açıklayan **kodlama tasarımı ve analiz belgesi**
**Durum:** **YALNIZ ANALİZ / TASARIM TASLAĞI** — ana yaklaşımı belirlenmiş, önemli düzeltmeleri yapılmış tasarım taslağı; yeniden kontrolün eşzamanlılık, zamanlı tarama ve ikinci kesinti altındaki karşı örnekleri (R-01…R-09) v1.3'te işlendi, **bağımsız yeniden kontrol bekliyor**; "kapanış tamamlandı" beyanı değildir. Bu belge kod değildir; repoya kod, migration, izin, capability, görev veya karar eklemez; hiçbir geliştirme görevini kuyruğa almaz; Faz 0'ı başlatmaz; sağlayıcı/SDK/model seçmez; canlı veri aktarımı veya sağlayıcı aktivasyonu onayı değildir. `ERP-000J_AI_GATEWAY_CONTRACT_v1.md` §24 "Şimdi yasak" listesi (gateway endpoint, tool handler, worker, prompt, retrieval/vector store kodlamak; DB migration eklemek; provider/SDK seçmek) **aynen geçerlidir**; buradaki kod blokları uygulama anında yazılacak kodun **önerilen biçimidir**, repoya alınmış kod değildir.
**Doğrulama tabanı:** `sinangym/GoymenErp` `main@00a879816a7a9bb9a800712e59dc9ae5f52f08b0` (27 Eylül 2026); kod desenleri salt-okunur envanterle çıkarılmıştır (Ek K-1 kanıt tablosu)
**Kaynak önceliği:** `AGENTS.md` sırası — son tarihli `SINAN_KARARLARI_*.md` > `KARAR_SICILI.md` > Master Rapor v3 > feature spec/ADR (`ERP-000J`, `ERP-000K`, ADR-002/009/011/013) > `GOYMEN_AI_MERKEZI_MASTER_RAPOR_V1.md` v1.5 > bu belge. Bu belge master raporla çelişirse master rapor, master rapor kanonik kararla çelişirse kanonik karar geçerlidir.
**Adlandırma:** Belgedeki bütün proje, sınıf, tablo, permission, capability, endpoint, araç ve yapılandırma adları **ÖNERİ**dir; ADR-008 ve ilgili K3 review ile kilitlenmeden bağlayıcı değildir.

---

## İçindekiler

0. Statü, okuma kuralları ve "iş takibi ajanı" sorusunun kısa cevabı · 1. Kodlama özeti · 2. Çözüm yapısı, proje dosyaları ve bağımlılık kuralları · 3. Host kaydı (Program.cs, modül extension'ları, in-memory test factory) · 4. Veri modeli ve EF eşlemeleri (`work` ve `ai` şemaları, write guard, migration/rollback) · 5. Domain: aggregate'ler ve durum makineleri (AiProposal ve OperationIntentId, AiRun, Issue/Evaluation, WorkItem/ApprovalRequest, risk istisnası) · 6. Araç sözleşmesi ve kataloğu (`Ai.Contracts`, adaptörler) · 7. Politika değerlendirici (13 adım) · 8. Bağlam kurucu ve ortak veri kesiti · 9. Etki kuralı motoru (`Work.Rules`), kapı sorgusu, kural-tabanlı iş öğesi · 10. Öneri → onay → gönderim → kurtarma (kanıt matrisi dahil) · 11. Sağlayıcı portu, router ve API-birincil yol · 12. Ölçü referansı, çıktı doğrulayıcı, güvenli renderer · 13. Bilgi katmanı ve erişim · 14. İş akışı motoru (`Ai.Workflow`) ve **iş takibi ajanı** · 15. Endpoint'ler, problem kodları ve OpenAPI · 16. Capability provider ve registry pinleme · 17. Frontend (React shell, lane, api/contract, testler) · 18. Test stratejisi ve CI kapıları · 19. Yapılandırma, bayraklar, secret'lar · 20. Gözlemlenebilirlik · 21. Kodlama sırası ve PR dilimleri (kuyruğa alma değildir) · 22. Kapanış kontrol tablosu (üçüncü eleştiri) · 23. Açık noktalar · Ek K-1 Kod deseni kanıt tablosu · Ek K-2 Problem kodu ve HTTP durum sözlüğü · Ek K-3 Dosya listesi

---

## 0. Statü, okuma kuralları ve "iş takibi ajanı" sorusunun kısa cevabı

### 0.1 Bu belge neyi çözer

Master rapor v1.5 **ne** yapılacağını ve **neden** yapılacağını söyler; bu belge **nasıl kodlanacağını** söyler: hangi proje, hangi sınıf, hangi tablo, hangi endpoint, hangi test. İki belge birlikte okunur; bu belge master raporun bölüm numaralarına (§) atıf yapar.

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
2. **Kalıcı çalıştırıcılar:** modelsiz takip — zamanlı kural taraması, uzlaştırma, eskalasyon, olay tüketimi — Work'ün kendi `WorkRulesWorker`'ında çalışır ve AI bayraklarının hiçbirine bağlı değildir. `Ai.Workflow` ise model destekli şablonları ("fatura–sipariş–kabul karşılaştır ve açıkla", "günlük yönetici brifingi", kullanıcının "bu haftaki teslimatları incele" isteği) sunucuda saklanan `WorkflowInstance` olarak yürütür; modelsiz adımlarda Work'ün tracker'ını çağırır, kendisi görev yazmaz. Kullanıcının bilgisayarı kapansa da iş kaybolmaz; bu bir **kalıcılık** hedefidir: sunucu kapalıyken işleme durur, açılınca kaldığı adımdan devam eder ve çift işlem yapmaz; bekleme durumlarında (`WaitingHuman/WaitingApproval/WaitingData`) model çağrılmaz (§14.1).
3. **`Modules.Ai` (model):** iş öğesine açıklama, seçenek gerekçesi ve taslak (e-posta, görev metni) ekler; tutarları yazmaz (`{{measure}}`); hiçbir iş kaydını onaysız yazmaz.

Ajanın **yapamayacakları** (KİLİTLİ-KAYNAK): onaysız iş kaydı yazmak (`DEC-AI-003`); e-posta/mesaj göndermek (yalnız taslak; gönderim kullanıcı onayıyla, `DEC-DOC-006/007`); eskalasyon zincirini kendi belirlemek (politika tablosundan uygular); ERP'nin kesin kurallarını aşmak; "hep böyle yapıyoruz" cümlesini kurala çevirmek (`DEC-AI-006`). Sohbetten "görev aç" isteğinde AI yalnız **görev taslağı** üretir (`work.item.draft@1`, DRAFT); kullanıcı taslağı normal Çalışma Merkezi ekranında kendi komutuyla kaydeder (Work kullanıcı komutu; AI WRITE aracı değildir).

Bu ajanın ilk sürümü Faz 3'tür (master §11.2); ön koşulu `Modules.Work` çekirdeği ve `ERP-000F` outbox dispatcher'ıdır. Bu belge her iki parçanın kod tasarımını verir; kuyruğa alınması Sinan kararıdır.

---

## 1. Kodlama özeti

| Soru | Cevap |
|---|---|
| **Ne kodlanacak?** | İki yeni modül (`Modules/Work`, `Modules/Ai`), iki host runtime klasörü (`GoymenERP.Api/RulesRuntime`, `GoymenERP.Api/AiRuntime`), iki şema (`work`, `ai`), üç çalıştırıcı (`WorkRulesWorker` — Work, AI'dan bağımsız; `AiRecoveryWorker` — önceden gönderilmiş işlemlerin kurtarması, yeni AI yürütme bayraklarından bağımsız; `AiWorkflowWorker` — model destekli işler; §14.1), frontend'de iki lane (`src/work`, `src/ai`) ve K shell entegrasyonu, testler ve CI kapıları. |
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
                                               # WorkflowInstance durum makinesi çalıştırıcısı (v1.3: kurtarma burada DEĞİL — GoymenERP.Api/AiRuntime/Recovery)
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
builder.Services.AddRulesRuntime(builder.Configuration); // GoymenERP.Api/RulesRuntime: IImpactRuleEvaluatorPort adaptörleri + WorkRulesWorker (Work:Scheduler:Enabled; Ai bayraklarından bağımsız)

builder.Services.AddAiModule(builder.Configuration,
    (services, options) => options.AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddAiRuntime();                       // GoymenERP.Api/AiRuntime: IAiTool adaptörleri, AiCommandEnvelopeFactory, snapshot runner, sahip sonuç okuyucuları
builder.Services.AddAiRecovery(builder.Configuration); // (v1.3) AiRecoveryWorker + AiWorkBridge + AiTakeoverReconciler — Ai:Recovery:Enabled; Ai:Enabled/KillSwitch/Workflow'dan BAĞIMSIZ
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

/// <summary>(v1.3, R-05) Takip başı: (CompanyId, IssueKey) başına tek satır; koşul OLSUN OLMASIN her kalıcı (gölge olmayan) değerlendirme bu satırı
/// ekler veya günceller. Tracker satırı hesaptan ÖNCE yükler; commit rowversion/tekil indeksle doğrulanır. Böylece kaydedilen durum her zaman
/// "önceki kaydedilmiş durumdan SONRA okunmuş veriden" türer — eski hesap yeni durumu geri alamaz (§9.1).</summary>
internal sealed class TrackHeadRow : IWorkTenantRow
{ /* CompanyId, RuleId, TargetEntityType, TargetId (IssueKey; PK), LastEvaluationId, LastEvaluatedAtUtc, Seq (her değerlendirmede +1), Version, RowVersion */ }

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
    public string ScopeKey { get; set; } = "party";            // (v1.3) değerlendirmenin karar kapsamı (R-04)
    public long HeadSeq { get; set; }                           // (v1.3) TrackHead.Seq — değerlendirmenin sırası (R-05)
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
    public string TitleTemplateCode { get; set; } = string.Empty; // başlık şablon kodu (örn. "FinansRiskOnayi"); serbest metin değil
    public string? TitleParamsRef { get; set; }                 // şablon parametreleri içerik deposunda (§4.5)
    public string ScopeKey { get; set; } = "party";            // karar ihtiyacı kapsamı: "party" | "instruction:{id}"
    public int DataClass { get; set; }                          // içeriğin gerçek sınıfı (kaynaktan miras; Finans kaynaklı → Gizli)
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
    public string DedupKey { get; set; } = string.Empty;        // IssueKey + ScopeKey (karar ihtiyacı başına); yalnız açık öğeler için tekil (filtered index)
    public string VisibilityMatrixJson { get; set; } = "{}";    // kanal × rol açıklık düzeyi
    public string IdempotencyKeyHash { get; set; } = string.Empty; // takip komutunun key hash'i: rule:{deliveryId}:{ruleId}:{issueKey}:{scopeKey} / sweep:{ruleId}:{slot}:{issueKey}:{scopeKey} / istemci key (§9.1)
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

internal sealed class ShadowEvaluationRow : IWorkTenantRow   // (v1.2) gölge ölçüm kaydı; operasyonel Issue/WorkItem'a FK YOK; append-only
{ /* Id, CompanyId, RuleId, RuleVersion, IssueKey, ScopeKey, TriggerKind, WouldOutcome, WouldOpenScope, MeasuresRef + MeasuresHash, EvaluatedAtUtc */ }

internal sealed class SchedulerLeaseRow : IWorkTenantRow     // (v1.2) WorkRulesWorker kiralaması; PK (CompanyId, TaskKind, Slot)
{ /* CompanyId, TaskKind ("rules.schedule" | "rules.reconciliation" | "escalation" | "rules.fanout"), Slot (fanout: ruleId+subject), LeaseOwner, LeaseUntilUtc, Cursor (keyset), CompletedAtUtc, Version, RowVersion */ }
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

modelBuilder.Entity<TrackHeadRow>().HasKey(x => new { x.CompanyId, x.RuleId, x.TargetEntityType, x.TargetId });   // (v1.3) IssueKey başına tek baş; ilk ekleme yarışı PK 2627 → tracker yeniden dener
modelBuilder.Entity<ShadowEvaluationRow>().ToTable("ShadowEvaluations", Schema);                                      // operasyonel tablolara FK yok
modelBuilder.Entity<SchedulerLeaseRow>().HasKey(x => new { x.CompanyId, x.TaskKind, x.Slot });

var attempts = modelBuilder.Entity<WorkAttemptRow>();
attempts.ToTable("Attempts", Schema, t => t.HasCheckConstraint("CK_Attempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64"));
attempts.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });   // ADR-011 deneme kimliği
```

`ApplyWriteGuards` izinli `Modified` alanları (ÖNERİ): `IssueRow` → `Status, Severity, DecisionRefJson, LastEvaluationId, ResolvedByRef, ReopenCount, Version, UpdatedAtUtc`; `WorkItemRow` → `OwnerActorId, Status, DueAtUtc, ClosureEvidenceRef, Version, UpdatedAtUtc` ve **yalnız çift olarak** `OwnerRole` + `EscalationStep`: guard, `OwnerRole` değiştiğinde (a) `EscalationStep`'in tam 1 arttığını, (b) aynı save'de bu öğe ve bu adım için `Kind=Escalated` bir `WorkItemEventRow` eklendiğini ve olaydaki `FromRole/ToRole/PolicyId/PolicyVersion` değerlerinin satırla eşleştiğini doğrular; aksi halde save reddedilir. Böylece rol değişikliği genel düzenlemeye kapalı, yalnız eskalasyon komutunun politika doğrulamalı yoluna açıktır (politika zinciriyle eşleşme komutta `Escalate` içinde denetlenir). `TrackHeadRow` → `LastEvaluationId, LastEvaluatedAtUtc, Seq, Version`; `SchedulerLeaseRow` → kiralama alanları; `EvaluationRow`, `WorkItemEventRow`, `WorkAttemptRow`, `ShadowEvaluationRow` → **hiç** (append-only). `Deleted` her satırda yasak.

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
    public string DiffRef { get; set; } = string.Empty;     // kullanıcıya gösterilen fark İÇERİKTİR → içerik deposunda (§4.5)            // kullanıcıya gösterilen fark (açıklık matrisine tabi)
    public string ValidationRef { get; set; } = string.Empty; // doğrulama sonucu iş değeri taşıyabilir → içerik deposunda
    public string ValidationOutcome { get; set; } = string.Empty; // satırda yalnız kod: "Ok" | "Invalid" (içerik değil)
    public string PolicyRefsJson { get; set; } = "{}";      // toolCatalog/promptPolicy/riskThreshold sürümleri
    public string IdempotencyKeyHash { get; set; } = string.Empty;    // büyük harf sha256("ai:{OperationIntentId:N}") — raw key saklanmaz
    public int Status { get; set; }                         // §5.1 durum makinesi
    public string RequestedByActorId { get; set; } = string.Empty;
    public Guid PreparedByRunId { get; set; }
    public string? ApprovedByActorId { get; set; } public string? SecondApprovedByActorId { get; set; }
    public int RequiredApprovals { get; set; }              // 1 | 2 — sahip modülün kabul edilmiş onay politikasından (ApprovalPolicy, §5.1)
    public bool RequiresSeparateApprover { get; set; }      // (v1.3, R-08) isteyen ≠ onaylayan YALNIZ sahip politikası istiyorsa; varsayılan false
    public string ApprovalPolicyRef { get; set; } = string.Empty; // (v1.3) politikanın kaynağı ve sürümü: "owner:sales.order-draft.create.v1@3" | "default@1"
    public string? ExecutedByRef { get; set; }              // "system:ai-runtime"
    public string? OnBehalfOfActorId { get; set; }
    public string? DispatchedPayloadHash { get; set; }      // gönderim anında dondurulan hash (== CanonicalPayloadHash olmalı)
    public string? ExecutedCommandRef { get; set; }         // sahip komut sonucu (örn. draft id) + orijinal correlation
    public bool TargetLock { get; set; }                    // Unknown çözülene kadar aynı hedef/niyet için ikinci dispatch yok
    public bool FailedBeforeDispatch { get; set; }          // Failed ama hiçbir şey gönderilmedi (ör. zarf bütünlüğü)
    public bool RejectedForFix { get; set; }                // Rejected yalnız ReturnForFix ise yeniden önizlenebilir
    public string? FailureCode { get; set; }
    public DateTimeOffset? DispatchRequestedAtUtc { get; set; } // Approved olunca yazılır: kalıcı gönderim talebi (süpürücü onaylanmış-gönderilmemişi bulur)
    public DateTimeOffset? DispatchedAtUtc { get; set; }
    public string? LastAttemptRefusalCode { get; set; }     // (v1.3) bir gönderim denemesinin KAYITSIZ reddi (niyetin reddi DEĞİL; ilk veya kurtarma denemesi)
    public int FailureBasis { get; set; }                   // (v1.3) Failed'ın dayanağı: 1 PreDispatch · 2 OwnerRecordedRefusal · 3 SingleSendUnrecordedRefusal · 4 ContentConflict · 5 OwnerKeyFence
    public bool OutcomeCorrected { get; set; }              // geç gelen başarı önceki Failed çıkarımını düzeltti
    public string? ResolutionEvidenceRef { get; set; }      // (v1.3) insan devralma kanıtı: SAHİP MODÜLÜN doğrulanmış cevabının referansı (istemci serbest metni değil)
    public string? ResolvedByActorId { get; set; }
    public int RecoveryAttempts { get; set; }               // 0 | 1 — "en fazla bir aynı-key kurtarma gönderimi" DB düzeyinde (CK_Proposals_Recovery)
    public int SendsIssued { get; set; }                    // (v1.3, R-01) bu key ile sahibe çıkan gönderim sayısı: MarkDispatched → 1, MarkRecoveryAttempt → 2 (CK_Proposals_Sends)
    public bool TakeoverRequired { get; set; }              // (v1.3, R-03) insan devralma ihtiyacı AI'nın KENDİ satırında; Work görevi olgudan açılır (§10.4a)
    public string? TakeoverReason { get; set; }
    public int ReviewState { get; set; }                    // (v1.3, R-02) 0 None · 1 Required · 2 Resolved — işlem sonucundan AYRI inceleme ihtiyacı
    public string? ReviewEvidenceRef { get; set; }
    public DateTimeOffset? LastRecoveryAtUtc { get; set; }
    public DateTimeOffset PreviewCreatedAtUtc { get; set; } public DateTimeOffset ExpiresAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty; public string? CausationId { get; set; }
    public long Version { get; set; } public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class ApprovalRow : IAiTenantRow            // onay KANITI; append-only; yürütme kimliği değildir
{ /* Id, CompanyId, ProposalId, OperationIntentId, ApproverActorId, Decision (1 Approve · 2 Reject · 3 ReturnForFix · 4 RequestInfo · 5 Conditional), BoundHash, BoundTargetVersion, ReauthRef, CorrelationId, ApprovedAtUtc */ }

/// <summary>ai.* komutlarının ADR-011 deneme kaydı (Sales DraftAttemptRow deseni): öneri oluşturma, onay, ret, çözüm.
/// PK (CompanyId, CommandName, IdempotencyKeyHash); aktör ve istek hash'i karşılaştırılır; etkiyle aynı save'de yazılır. Append-only.</summary>
internal sealed class CommandAttemptRow : IAiTenantRow
{ /* CompanyId, CommandName ("ai.proposal.create.v1" | "ai.proposal.approve.v1" | "ai.proposal.reject.v1" | "ai.proposal.resolve.v1"), IdempotencyKeyHash,
     RequestHash, ActorId, Outcome (1 Recorded · 2 Refused), ProposalId, OperationIntentId, RefusalJson, CorrelationId, RecordedAtUtc */ }

/// <summary>(v1.3, R-03) AI modülünün olgu kutusu (transactional outbox; InventoryOutboxRow deseni, Ek K-1 madde 13). Öneri durum değişikliğiyle
/// AYNI save'de yazılır; append-only. Olgular: ai.proposal.takeover-required.v1 · ai.proposal.review-required.v1 · ai.proposal.resolved.v1.
/// Tüketici: AiWorkBridge (§10.4a). Yük yalnız kimlik/durum/kod/kanıt referansı taşır; içerik yok.</summary>
internal sealed class AiOutboxRow : IAiTenantRow
{ /* CompanyId, MessageId (Guid v7), EventType, AggregateId (ProposalId), AggregateVersion, PayloadJson, CorrelationId, OccurredAtUtc;
     UX_AiOutbox_Aggregate (CompanyId, AggregateId, AggregateVersion, EventType) — aynı durum geçişi iki olgu üretemez */ }

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
    t.HasCheckConstraint("CK_Proposals_Sends", "[SendsIssued] BETWEEN 0 AND 2 AND [SendsIssued] = CASE WHEN [DispatchedAtUtc] IS NULL THEN 0 ELSE 1 + [RecoveryAttempts] END");
    t.HasCheckConstraint("CK_Proposals_Review", "[ReviewState] BETWEEN 0 AND 2 AND ([ReviewState] = 0 OR [Status] IN (9,10)) AND ([Status] <> 10 OR [FailureBasis] BETWEEN 1 AND 5) AND ([TakeoverRequired] = 0 OR [Status] = 8)");
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
- İlk migration'lar ve şema sahipliği (v1.3'te tamamlandı): **`work` şeması (Work modülü, `WorkDbContext`)** — `AddWorkCore` (Issues, TrackHeads, Evaluations, ShadowEvaluations, WorkItems, WorkItemEvents, Attempts, EscalationPolicies, ApprovalRequests, ImpactRules), `AddWorkScheduler` (SchedulerLeases; `WorkRulesWorker` ile aynı dilim). **`ai` şeması (Ai modülü, `AiDbContext`)** — `AddAiCore` (Runs, RunSteps, PolicyVersions, Budgets, UsageLedger), `AddAiProposals` (Proposals, Approvals, **CommandAttempts**, **Outbox**, AssistantFindings, Feedback, PayloadBlobs), `AddAiKnowledge`, `AddAiWorkflow` (Jobs, WorkflowInstances, Inbox), `AddAiEval`. Bir tablo yalnız kendi modülünün migration'ında doğar; host (`RulesRuntime`, `AiRuntime`) tablo sahibi değildir. Faz başına bir migration; hepsi **geri alınabilir** (`Down` tabloyu düşürür; veri kaybı yalnız o fazın kendi tablolarında) veya forward-fix planlı (audit/attempt satırları hiçbir `Down`'da silinmez — `Down` yalnız hiç canlıya çıkmamış migration için çalıştırılır; canlıdaki tablo için forward-fix).
- `organization.Companies` dışında şemalar arası FK yoktur; `OrganizationCompanyReference` `ExcludeFromMigrations()`.
- Migration K3'tür (AGENTS.md); gerçek SQL kabul testi `Database.MigrateAsync()` ile boş veritabanında koşar (Ek K-1 madde 17a `Run.StartAsync`).
- Vektör sütunu (`KnowledgeVersions.Embedding`) SQL Server yerleşik `vector` türü doğrulanana kadar migration'a **girmez**; ilk sürüm embedding'i ayrı depoya veya `varbinary(max)`'e yazar (NS-AI-07).

### 4.5 İçerik deposu, saklama ve değişmezlik

Master §5.5 matrisi şu tasarımla uygulanır:

| Satır türü | Depo | Değişmezlik | Süre sonu |
|---|---|---|---|
| `ai.RunSteps`, `ai.Approvals`, `ai.CommandAttempts`, `ai.Outbox`, `work.Attempts`, `work.TrackHeads` (güncellenir, içeriksiz), `ai.Inbox`, `work.SchedulerLeases` | SQL (`ai`/`work`) | Append-only (write guard; `TrackHeads`/`SchedulerLeases` yalnız sayaç/kiralama alanları); içerik alanı yok (hash, sürüm, karar, sonuç, referans) | Kalır |
| `work.Evaluations` (ölçü değerleri), `work.WorkItemEvents` (yorum metni), `ai.Feedback` (düzeltme), `ai.Runs.UserCorrectionRef` | Satır SQL'de append-only; **içerik alanları** içerik deposunda (`MeasuresRef`, `PayloadRef`, `CorrectionRef` + hash) | Satır değişmez; içerik yazıldıktan sonra değişmez | Süre sonunda içerik silinir; satır, referans ve hash kalır ("şu tarihte şu karar verildi" kanıtı korunur, tutar/yorum metni saklanmaz) |
| `ai.Runs`, `ai.Proposals`, `ai.Jobs`, `ai.WorkflowInstances`, `work.Issues`, `work.WorkItems` | SQL | Silinmez; izinli alanlar rowversion ile güncellenir. Satırda **yalnız kod/durum/sürüm/hash/referans**: öneri farkı (`DiffRef`), doğrulama sonucu (`ValidationRef`), görev başlık parametreleri (`TitleParamsRef`) içerik deposundadır; görev başlığı şablon kodudur, serbest metin değildir | Satır kalır |
| Öneri zarfı, öneri farkı, doğrulama sonucu, görev başlık parametreleri, prompt/cevap içeriği (saklanmasına karar verilirse) | **Şifreli, immutable içerik deposu** (`IAiPayloadVault`: SQL `ai.PayloadBlobs` `varbinary(max)` + AES-GCM, anahtar KMS/`DataProtection`; veya S3-uyumlu nesne depo — ADR-008 madde 10) | Yazıldıktan sonra değişmez; referans = hash + anahtar sürümü | Süreli; süre sonunda blob silinir, satır metadata'sı ve hash'i kalır. **Yasal bekletme:** `Approved`, `Dispatched`, `Unknown` durumundaki, `TargetLock` taşıyan veya `ReviewState=Required` önerinin zarfı süre dolsa da **silinmez**; süre öneri çözüldüğünde (`Executed/Failed` + devralma/inceleme kapandı) başlar. Silme işi bu durumları sorguyla dışlar (§10.4 madde 9) |
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

public enum DispatchAttempt { First = 1, RecoveryResend = 2 }   // (v1.3) yalnız audit/metrik: reddin KESİNLİĞİ bu ayrımdan çıkarılmaz (R-01)

/// <summary>(v1.3, R-01) Sahip cevabının dört anlamı ayrı taşınır. Adaptör sahip sözleşmesinden eşler (Ek K-2). UnrecordedRefusal YALNIZ sahibin
/// açık listesindeki kodlardır; eşlenemeyen her kod Unknown sayılır (en muhafazakâr: öneri kilitli kalır, kurtarma/probe çözer) + security-event.</summary>
public enum OwnerOutcomeKind
{
    Executed = 1,           // bu key için sahip kaydı: ilk yürütme veya replay
    RecordedRefusal = 2,    // sahip bu key'e terminal ret KAYDETTİ (ADR-011 terminal ret, IsReplay ret, key fence) → aynı key ile hiçbir gönderim artık yürütemez
    SameKeyMismatch = 3,    // key'de BAŞKA içerik kayıtlı (bütünlük)
    UnrecordedRefusal = 4,  // key'de kayıt YOK — sahibin AÇIK listesindeki kodlar (fence/403, *_concurrent_change, geçici doğrulama); yalnız BU isteğin reddi
    Unknown = 5,            // cevap yok / transport / beklenmeyen istisna
}
public sealed record OwnerOutcome(OwnerOutcomeKind Kind, string? CommandRef, string? Code);

public enum FailureBasis { PreDispatch = 1, OwnerRecordedRefusal = 2, SingleSendUnrecordedRefusal = 3, ContentConflict = 4, OwnerKeyFence = 5 }
public enum ProposalReviewState { None = 0, Required = 1, Resolved = 2 }

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
    public ApprovalPolicy ApprovalPolicy { get; private set; } = ApprovalPolicy.Default;   // (v1.3) { RequiredApprovals, RequiresSeparateApprover, Ref }
    public int RequiredApprovals => ApprovalPolicy.RequiredApprovals;
    public int SendsIssued { get; private set; }                                             // (v1.3) bu key ile çıkan gönderim sayısı
    public IReadOnlyList<AiProposalFact> PendingFacts => facts;                               // (v1.3) store aynı save'de ai.Outbox'a yazar; her olguya KAYDEDİLEN
                                                                                              // Version'ı ve olgu sırasını store damgalar (aggregate sürüm vermez) → UX_AiOutbox çakışmaz
    private readonly List<AiProposalFact> facts = [];
    private void Raise(AiProposalFact fact) => facts.Add(fact);
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
        ProposalActors actors, ApprovalPolicy approvalPolicy, bool validationPassed, bool shadowMode, DateTimeOffset now, TimeSpan expiry)
    {
        RequireHash(canonicalPayloadHash);
        return new AiProposal
        {
            Id = Guid.CreateVersion7(), CompanyId = companyId, OperationIntentId = Guid.CreateVersion7(), IntentRevision = 1,
            Status = shadowMode ? AiProposalStatus.ShadowOnly : validationPassed ? AiProposalStatus.AwaitingApproval : AiProposalStatus.Draft,
            CanonicalPayloadHash = canonicalPayloadHash,
            ApprovalPolicy = approvalPolicy,                                   // (v1.3) sahip modülün kabul edilmiş politikası; RequiredApprovals ∈ {1,2}, RequiresSeparateApprover
            Version = 1, /* … */
        };
    }

    /// <summary>Aynı niyetin yeni önizlemesi. YALNIZ izin verilen başlangıç durumlarından (beyaz liste):
    /// Draft, Stale, Rejected (ReturnForFix) ve ShadowOnly. Diğer her durum reddedilir:
    /// AwaitingApproval/PartiallyApproved/Approved (önce Stale olmalı), Dispatched/Unknown (belirsizliği aşma olurdu),
    /// Executed (iş bitti), Failed (sahip reddi aynı key'e terminal kaydedilmiş olabilir → YENİ niyet), Superseded (zaten eski).
    /// Gölge öneri yeniden üretilince de GÖLGE kalır. Eski revizyon aynı save'de Superseded olur.</summary>
    public AiProposal Reissue(string newCanonicalPayloadHash, ProposalTarget refreshedTarget, ApprovalPolicy refreshedPolicy, bool validationPassed, bool shadowModeNow, DateTimeOffset now, TimeSpan expiry)
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
            CanonicalPayloadHash = newCanonicalPayloadHash, ApprovalPolicy = refreshedPolicy /* içerik değişince eşik değişebilir: yeniden çözülür */, Version = 1, /* … */ };
    }

    /// <summary>Onay: bağlı hash ve hedef sürüm eşleşmeli; kota dolunca Approved. (v1.3, R-08) İnsan onayı HER WRITE için zorunludur; kişi ayrılığı
    /// (isteyen ≠ onaylayan) ise YALNIZ sahip modülün kabul edilmiş politikası istiyorsa uygulanır (ApprovalPolicy.RequiresSeparateApprover).
    /// Politika istemiyorsa isteyen kendi önizlemesini açık onayla kaydeder — aynı yetkiyle elle kaydetmekle eşdeğerdir. Kota 2 ise iki farklı kişi gerekir.</summary>
    public AiApprovalOutcome Approve(string approverActorId, string boundHash, long? boundTargetVersion, DateTimeOffset now)   // isteyen satırdan okunur, çağıran vermez
    {
        if (Status is not (AiProposalStatus.AwaitingApproval or AiProposalStatus.PartiallyApproved)) return AiApprovalOutcome.NotApprovable;
        if (now >= ExpiresAtUtc) { Status = AiProposalStatus.Stale; Version++; return AiApprovalOutcome.Expired; }
        if (!string.Equals(boundHash, CanonicalPayloadHash, StringComparison.Ordinal) || boundTargetVersion != ExpectedVersion) return AiApprovalOutcome.BindingMismatch;
        if (ApprovalPolicy.RequiresSeparateApprover && string.Equals(approverActorId, RequestedByActorId, StringComparison.Ordinal))
            return AiApprovalOutcome.SegregationOfDuties;                       // maker≠checker YALNIZ sahip politikası istiyorsa (GOREV_AYRILIGI_ONAY eşikleri)
        if (approvers.Contains(approverActorId, StringComparer.Ordinal)) return AiApprovalOutcome.AlreadyApprovedByActor;
        approvers.Add(approverActorId);
        Status = approvers.Count >= RequiredApprovals ? AiProposalStatus.Approved : AiProposalStatus.PartiallyApproved;
        if (Status == AiProposalStatus.Approved) DispatchRequestedAtUtc = now;   // KALICI gönderim talebi: süreç gönderimden önce kapanırsa süpürücü bunu bulur (§10.4)
        Version++;
        return Status == AiProposalStatus.Approved ? AiApprovalOutcome.Approved : AiApprovalOutcome.AwaitingSecondApproval;
    }

    public void MarkDispatched(DateTimeOffset now)
    {
        if (Status != AiProposalStatus.Approved) throw new AiProposalRuleException(AiProposalCodes.NotApproved);
        DispatchedPayloadHash = CanonicalPayloadHash;   // dondurma: gönderilen == onaylanan
        DispatchedAtUtc = now; SendsIssued = 1; TargetLock = true; Status = AiProposalStatus.Dispatched; Version++;
    }

    public void MarkExecuted(string executedCommandRef) { Require(Status is AiProposalStatus.Dispatched or AiProposalStatus.Unknown); Status = AiProposalStatus.Executed; TargetLock = false; TakeoverRequired = false; ExecutedCommandRef = executedCommandRef; Raise(AiProposalFact.Resolved(this)); Version++; }
    /// <summary>Gönderim sonrası belirsizlik; ayrıca süresi geçmiş Dispatched kurtarmaya alınırken ilk adım (reason: dispatch_grace_expired).</summary>
    public void MarkUnknown(string reason) { Require(Status is AiProposalStatus.Dispatched or AiProposalStatus.Unknown); Status = AiProposalStatus.Unknown; /* TargetLock kalır */ Version++; }

    /// <summary>(v1.3, R-01) Sahip cevabının TEK uygulama noktası: ilk gönderim, kurtarma gönderimi, probe ve geç gelen cevap aynı kuralla işlenir.
    /// afterConcurrentChange: satır, bu cevabı doğuran gönderimden SONRA başka yoldan değişti (taze satıra uygulanıyor).
    /// Kesinlik kuralı: kayıtlı sonuç (başarı, kayıtlı terminal ret, key'de başka içerik) her zaman niyetin sonucudur. KAYITSIZ ret yalnız
    /// "bu key ile tek gönderim çıktı ve satır o gönderimden beri değişmedi" durumunda kesindir; bu koşulu bellek değil, save'in rowversion
    /// kontrolü kanıtlar (kurtarma yeniden gönderimden ÖNCE SendsIssued=2'yi commit eder, §10.4). Diğer her kayıtsız ret yalnız deneme reddidir.</summary>
    public void ApplyOwnerOutcome(OwnerOutcome o, bool afterConcurrentChange)
    {
        var open = Status is AiProposalStatus.Dispatched or AiProposalStatus.Unknown;
        switch (o.Kind)
        {
            case OwnerOutcomeKind.Executed:                                          // başarı her zaman kazanır
                if (open) { MarkExecuted(o.CommandRef!); return; }
                if (Status == AiProposalStatus.Failed && FailureBasis != FailureBasis.PreDispatch)   // önceki "Failed" çıkarımı yanlıştı
                {
                    Status = AiProposalStatus.Executed; ExecutedCommandRef = o.CommandRef; OutcomeCorrected = true; TargetLock = false; TakeoverRequired = false;
                    RequireReview("outcome_corrected"); Raise(AiProposalFact.Resolved(this)); Version++;
                }
                return;                                                              // zaten Executed
            case OwnerOutcomeKind.RecordedRefusal:                                   // key'de terminal ret kayıtlı: aynı key ile hiçbir gönderim artık yürütemez → kesin
                if (open) Fail(o.Code!, FailureBasis.OwnerRecordedRefusal, keepLock: false);
                return;
            case OwnerOutcomeKind.SameKeyMismatch:                                   // key'de başka içerik: bu payload bu key ile asla yürümez; kayıt kimin? → inceleme
                if (open) { Fail(AiProposalCodes.ContentConflict, FailureBasis.ContentConflict, keepLock: true); RequireReview("content_conflict"); }
                return;
            case OwnerOutcomeKind.UnrecordedRefusal:                                 // key'de kayıt yok: yalnız BU isteğin reddi
                if (!afterConcurrentChange && Status == AiProposalStatus.Dispatched && SendsIssued == 1)
                { Fail(o.Code!, FailureBasis.SingleSendUnrecordedRefusal, keepLock: false); return; }   // tek gönderim + satır değişmedi (save doğrular) → başka gönderim yok ve olamaz
                if (open) { Status = AiProposalStatus.Unknown; LastAttemptRefusalCode = o.Code; Version++; }  // deneme reddi: Unknown + kilit; niyetin sonucu bilinmiyor
                return;
            case OwnerOutcomeKind.Unknown:
                if (open) { Status = AiProposalStatus.Unknown; Version++; }
                return;
        }
    }

    private void Fail(string code, FailureBasis basis, bool keepLock)
    {
        Status = AiProposalStatus.Failed; FailureCode = code; FailureBasis = basis; TargetLock = keepLock; TakeoverRequired = false;   // devralma biter; kilitliyse İNCELEME başlar (ayrı görev)
        if (!keepLock) Raise(AiProposalFact.Resolved(this));                        // kilitli Failed çözülmüş sayılmaz: inceleme ihtiyacı açık (R-02)
        Version++;
    }

    private void RequireReview(string reason) { ReviewState = ProposalReviewState.Required; Raise(AiProposalFact.ReviewRequired(this, reason)); }

    /// <summary>(v1.3, R-03) İnsan devralma ihtiyacı AI'nın KENDİ satırına yazılır; olgu aynı save'de ai.Outbox'a gider (§10.4a). Work'e doğrudan çağrı yok.</summary>
    public bool RequireTakeover(string reason)
    {
        Require(Status == AiProposalStatus.Unknown);
        if (TakeoverRequired && TakeoverReason == reason) return false;             // aynı neden: değişiklik yok (idempotent)
        TakeoverRequired = true; TakeoverReason = reason; Raise(AiProposalFact.TakeoverRequired(this, reason)); Version++;
        return true;
    }

    /// <summary>(v1.3, R-02) İnsan devralma — yalnız Unknown'dan. Kanıt istemcinin serbest metni DEĞİLDİR; sunucu sahip modülden alır (§10.4a):
    /// Executed ← sahip okuması: bu key'de BU istekle eşleşen kayıt (sahip kendi eşleşme kuralıyla hükmeder; gateway hash'i sahip hash'iyle eşitlenmez).
    /// Failed   ← sahip anahtar kilidi (key fence) veya key'de kayıtlı terminal ret. "Şu anda kayıt görünmüyor" YETMEZ: yoldaki istek sonra kaydolabilir;
    ///            fence, key'e terminal "kapatıldı" kaydı yazdığı için bundan sonra aynı key ile hiçbir gönderim yürüyemez (R-01 kesinlik kuralı korunur).</summary>
    public void ResolveByHuman(OwnerRecordEvidence evidence, string actorId)
    {
        Require(Status == AiProposalStatus.Unknown && evidence.IsOwnerVerified);
        switch (evidence.Kind)
        {
            case OwnerEvidenceKind.RecordMatchesThisRequest: MarkExecuted(evidence.RecordRef!); break;
            case OwnerEvidenceKind.KeyFencedNoEffect: Fail(AiProposalCodes.ResolvedByKeyFence, FailureBasis.OwnerKeyFence, keepLock: false); break;
            case OwnerEvidenceKind.RecordedTerminalRefusal: Fail(evidence.Code!, FailureBasis.OwnerRecordedRefusal, keepLock: false); break;
            case OwnerEvidenceKind.RecordMatchesDifferentRequest:
                Fail(AiProposalCodes.ContentConflict, FailureBasis.ContentConflict, keepLock: true); RequireReview("content_conflict"); break;
            default: throw new AiProposalRuleException(AiProposalCodes.ResolutionEvidenceInvalid);        // NotFound tek başına hiçbir sonucu kanıtlamaz
        }
        ResolutionEvidenceRef = evidence.Ref; ResolvedByActorId = actorId;
    }

    /// <summary>(v1.3, R-02) İnceleme çözümü: "işlem sonucu belli, inceleme gerekli" satırlar (kilitli Failed(ContentConflict), düzeltilmiş sonuç).
    /// Bütün Failed kayıtlarını AÇMAZ: yalnız ReviewState=Required olanlar; yetki ai.recovery.review (K3); kanıt yine sahibin doğrulanmış okumasıdır.</summary>
    public void ResolveReview(ReviewDecision decision, OwnerRecordEvidence evidence, string actorId)
    {
        Require(ReviewState == ProposalReviewState.Required && evidence.IsOwnerVerified);
        if (decision == ReviewDecision.LinkOwnerRecord)                              // key'deki kayıt bu niyetin etkisidir (ör. hatalı revizyon gönderimi) → sonuç Executed
        {
            Require(Status == AiProposalStatus.Failed && FailureBasis == FailureBasis.ContentConflict && evidence.Kind == OwnerEvidenceKind.RecordMatchesDifferentRequest);
            Status = AiProposalStatus.Executed; ExecutedCommandRef = evidence.RecordRef; OutcomeCorrected = true;
        }
        else
        {
            Require(decision == ReviewDecision.ConfirmAndRelease);                   // kanıt türü durumla eşleşmeli (serbest kanıt yok):
            Require(Status == AiProposalStatus.Failed ? evidence.Kind == OwnerEvidenceKind.RecordMatchesDifferentRequest   // key dolu, başka içerik → bu payload asla yürümez
                                                      : evidence.Kind == OwnerEvidenceKind.RecordMatchesThisRequest);      // düzeltilmiş sonuç: sahip kaydı bu istek
        }
        TargetLock = false; ReviewState = ProposalReviewState.Resolved; ReviewEvidenceRef = evidence.Ref; ResolvedByActorId = actorId;
        Raise(AiProposalFact.ReviewResolved(this)); Raise(AiProposalFact.Resolved(this)); Version++;   // inceleme görevi ve (açıksa) devralma görevi ayrı ayrı kapanır
    }

    /// <summary>Gönderim ÖNCESİ ret (örn. zarf bütünlüğü bozuk, güncel yetki düştü). Hiçbir şey gönderilmedi: kilit yok, belirsizlik yok; yeniden deneme yeni niyet.</summary>
    public void FailBeforeDispatch(string code) { Require(Status == AiProposalStatus.Approved); Status = AiProposalStatus.Failed; FailureCode = code; FailureBasis = FailureBasis.PreDispatch; TargetLock = false; FailedBeforeDispatch = true; Version++; }
    public void MarkStale(string reason) { Require(Status is AiProposalStatus.AwaitingApproval or AiProposalStatus.PartiallyApproved or AiProposalStatus.Approved); Status = AiProposalStatus.Stale; Version++; }   // yalnız GÖNDERİM ÖNCESİ
    public void MarkRecoveryAttempt(DateTimeOffset now) { Require(Status == AiProposalStatus.Unknown && RecoveryAttempts == 0); RecoveryAttempts = 1; SendsIssued = 2; LastRecoveryAtUtc = now; Version++; }   // rowversion: tek koordinatör; SendsIssued=2 yeniden gönderimden ÖNCE commit edilir (R-01)
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
Unknown ──(kurtarma/probe: key'de başarı)──▶ Executed · (key'de kayıtlı terminal ret)──▶ Failed · (kayıtsız ret / ikinci belirsizlik)──▶ Unknown + TargetLock + devralma olgusu
Unknown ──(insan: sahip okuması "bu istek")──▶ Executed · (insan: sahip key fence)──▶ Failed  — serbest metin kanıtla geçiş YOK (v1.3)
Dispatched/Unknown ──(key'de başka içerik)──▶ Failed(ContentConflict) + TargetLock + ReviewState=Required ──(ResolveReview)──▶ Executed (bağla) | Failed (serbest bırak)
Dispatched ──(kayıtsız ret, SendsIssued=1, satır değişmemiş)──▶ Failed (SingleSendUnrecordedRefusal)   · aksi halde kayıtsız ret ──▶ Unknown (deneme reddi)
```

- `Approved` geçişi **tek kazanan**: `AiProposalStore.TransitionAsync` rowversion ile günceller; `DbUpdateConcurrencyException` → kaybeden `version_conflict` (409). İki ekran/çift tıklama/iki onaycı senaryosu (G-27) bununla kapanır.
- **Onay politikası kaynağı (v1.3, R-08):** `ApprovalPolicy` öneri başlarken sahip modülün **kabul edilmiş** politikasından çözülür (`IOwnerApprovalPolicyV1.ForAsync(ownerCommand, canonicalPayload, context)` — sahip dilim eki, ÖNERİ; kaynağı `GOREV_AYRILIGI_ONAY.md` eşikleri ve `ErpPermissions.Critical`). Sahip politikası bir komut için kişi ayrılığı veya ikinci onay tanımlamıyorsa varsayılan `{ RequiredApprovals = 1, RequiresSeparateApprover = false }`'dur: isteyen kendi önizlemesini **açık onayla** kaydedebilir (insan onayı zorunlu kalır; aynı kullanıcının aynı yetkiyle elle kaydetmesinden daha geniş bir kural üretilmez). "Bütün AI yazmalarında farklı kişi" gibi genel bir kural ancak ayrıca kabul edilmiş iş kuralıyla gelir (NEEDS-SINAN NS-AI-20); teknik örnek sessizce eklemez. Politika kaynağı ve sürümü satırda (`ApprovalPolicyRef`) ve audit'te durur.
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

### 5.3 `Issue` / `Evaluation` (`Work.Rules`), karar ihtiyacı ve risk istisnası (v1.2)

Dört şey ayrı tutulur ve biri diğerinin yerine geçmez:

| Nesne | Ne anlatır | Neye göre değişir |
|---|---|---|
| **Sorun (Issue)** — müşteri düzeyinde tek | İşletme koşulu sürüyor mu? (net risk > limit, gecikme politikası) | Yalnız sahip calculator'ın **koşul** yargısı. Karar veya istisna Sorunu **kapatmaz** |
| **Karar (Decision)** — Finance kaydı | Yetkili ne dedi ve **hangi kapsam için**? `Party` (müşteri geneli: bekletme, genel süreli istisna) veya `Instruction(id)` (tek sevkiyat istisnası) | Finance onay kaydı; süre, kapsam ve durum (`Active/Consumed/Expired/Revoked`) |
| **Karar ihtiyacı (DecisionNeed)** — kapsam başına | Bu kapsam için karar bekleyen iş var mı? | Koşul sürüyor **ve** kapsamı karşılayan geçerli karar yok |
| **İş öğesi (WorkItem)** — ihtiyaç başına tek | Karar ihtiyacının görevi | `DedupKey = IssueKey + ScopeKey`; ihtiyaç karşılanınca kanıtla kapanır |

**Kapsam kuralı:** `Instruction(A)` kararı yalnız A ihtiyacını karşılar; B talimatının ihtiyacını **karşılamaz**. `Party` kararı bütün kapsamları karşılar. Müşteri düzeyi ihtiyaç (`Party`, olay/zaman tetiklemelerinde odak talimat yoksa) herhangi bir geçerli karar varken doğmaz — Finance konuyla ilgilenmektedir; ama talimat ihtiyaçları kendi kapsamlarıyla ayrıca izlenir.

```csharp
// GoymenERP.Modules.Work.Domain/Issues/Issue.cs
public enum IssueStatus { Open = 1, Acknowledged = 2, Resolved = 3, Reopened = 4, Suppressed = 5 }
// Open/Reopened: koşul var ve en az bir açık karar ihtiyacı var (Reopened: Resolved'dan geri döndü)
// Acknowledged : koşul var, geçerli karar var, AÇIK karar ihtiyacı YOK · Resolved: koşul kalktı · Suppressed: politika
public enum EvaluationOutcome { Raised = 1, Unchanged = 2, Cleared = 3, Partial = 4, Acknowledged = 5 }   // CK_Evaluations_Outcome: 1..5

public sealed record DecisionScope(string Kind, Guid? InstructionId)                 // "Party" | "Instruction"
{
    public static readonly DecisionScope Party = new("Party", null);
    public string Key => Kind == "Party" ? "party" : $"instruction:{InstructionId:N}";
}

public sealed record DecisionRef(string ExceptionRef, string Kind, DecisionScope Scope, DateTimeOffset? ValidUntil)
{
    public bool Covers(DecisionScope need) => Scope.Kind == "Party" || Scope == need;  // A'nın istisnası B'yi KARŞILAMAZ
}

public sealed class Issue
{
    public Guid Id { get; private set; }
    public IssueKey Key { get; private set; } = default!;        // (RuleId, CompanyPartyAccount, id) — müşteri düzeyinde TEK Sorun
    public IssueStatus Status { get; private set; }
    public Severity Severity { get; private set; }
    public IReadOnlyList<DecisionRef> ActiveDecisions { get; private set; } = [];   // Finance port'undan; talimattan bağımsız
    public string? ResolvedByRef { get; private set; }            // koşulu kaldıran olay (tahsilat, teminat…) — istisna DEĞİL
    public int ReopenCount { get; private set; }
    public long Version { get; private set; }
    public bool ConditionHolds => Status is IssueStatus.Open or IssueStatus.Acknowledged or IssueStatus.Reopened;

    /// <summary>Sorun YALNIZ koşul varken oluşur (koşul yoksa satır yazılmaz). İlk tespit Raised'dır.</summary>
    public static Issue Open(Guid companyId, IssueKey key, RuleEvaluationResult result)
    {
        if (!result.ConditionHolds || result.IsPartial) throw new WorkRuleException(WorkCodes.IssueRequiresHoldingCondition);
        return new Issue { Id = Guid.CreateVersion7(), Key = key, Status = IssueStatus.Open, Severity = result.Severity, ActiveDecisions = result.ActiveDecisions, Version = 1 };
    }

    /// <summary>1. aşama: KOŞUL. Kararlar yalnız güncellenir; kararın değişmesi/yenilenmesi durumu değiştirmez (bkz. SyncDecisionState).</summary>
    public EvaluationOutcome ApplyCondition(RuleEvaluationResult result)
    {
        if (result.IsPartial) return EvaluationOutcome.Partial;                            // kaynak eksik: durum DEĞİŞMEZ
        ActiveDecisions = result.ActiveDecisions;
        if (!result.ConditionHolds)
        {
            if (Status == IssueStatus.Resolved) return EvaluationOutcome.Unchanged;
            Status = IssueStatus.Resolved; ResolvedByRef = result.ClearingEventRef; Version++; return EvaluationOutcome.Cleared;
        }
        if (Status == IssueStatus.Resolved) { Status = IssueStatus.Reopened; ReopenCount++; Severity = result.Severity; Version++; return EvaluationOutcome.Raised; }
        Severity = result.Severity; return EvaluationOutcome.Unchanged;
    }

    /// <summary>2. aşama (tracker, ihtiyaçlar işlendikten sonra): açık ihtiyaç yoksa ve geçerli karar varsa Acknowledged; ihtiyaç varsa Open/Reopened.
    /// Geçerli kararın BAŞKA geçerli kararla yenilenmesi ihtiyaç doğurmadığı için durum değişmez ("karar geçersizleşti" ≠ "karar yenilendi").</summary>
    public EvaluationOutcome SyncDecisionState(int openNeeds)
    {
        if (!ConditionHolds || Status == IssueStatus.Suppressed) return EvaluationOutcome.Unchanged;
        if (openNeeds == 0 && ActiveDecisions.Count > 0 && Status != IssueStatus.Acknowledged) { Status = IssueStatus.Acknowledged; Version++; return EvaluationOutcome.Acknowledged; }
        if (openNeeds > 0 && Status == IssueStatus.Acknowledged) { Status = IssueStatus.Open; Version++; return EvaluationOutcome.Raised; }   // yeni/karşılanmayan ihtiyaç
        return EvaluationOutcome.Unchanged;
    }

    /// <summary>Kapsamı karşılayan geçerli karar (TEK yüklem: ihtiyaç açma ve görev kapatma aynı kuralı kullanır).
    /// Talimat kapsamında o talimatı kapsayan karar; müşteri kapsamında ("geçerli karar yok" ihtiyacı) herhangi bir geçerli karar.</summary>
    public DecisionRef? SatisfyingDecision(DecisionScope scope) =>
        scope.Kind == "Party" ? ActiveDecisions.FirstOrDefault() : ActiveDecisions.FirstOrDefault(d => d.Covers(scope));

    public bool NeedsDecisionFor(DecisionScope scope) => ConditionHolds && Status != IssueStatus.Suppressed && SatisfyingDecision(scope) is null;
}
```

**Kararın Sorun'a ulaşması:** Finance istisna/bekletme kararını kendi komutuyla kaydeder; `finance.risk-exception.approved.v1` / `.consumed.v1` / `.revoked.v1` olayları (ÖNERİ; `W4-RISK` + `ERP-000F`) IR-LOG-001'i tetikler. Değerlendirmede Finance port'u o müşterinin **geçerli kararlarını** (`ActiveDecisions`: referans, kapsam, durum) döndürür; karar geçerliliği odaktaki talimattan değil kararın kendi kaydından okunur. Olay altyapısı gelene kadar geçici yol §9.4'tedir (sınırlı).

**Tek sevkiyata verilen risk istisnası — senaryolar (ÖNERİ; sahibi Finance, `DEC-FIN-009`; `W4-RISK` spec girdisi F0-10):**

| Olay | Müşterinin risk Sorunu | Kapı kararı | Karar ihtiyaçları ve görevler |
|---|---|---|---|
| A talimatı için risk tespiti | `Open` | A: `ReviewRequired` | İhtiyaç `instruction:A` → görev açılır |
| Finance A için tek sevkiyat istisnası onaylar | Açık kalır; açık ihtiyaç kalmadı → `Acknowledged` | A: `Proceed` + `exceptionRef` | A görevi kanıtla (istisna onay kaydı) **kapanır** |
| A'nın istisnası hâlâ aktifken **B** talimatı hazırlanır | Koşul sürer; B ihtiyacı doğar → `Acknowledged → Open` | B: `ReviewRequired` (A'nın istisnası B'yi kapsamaz) | İhtiyaç `instruction:B` → **B için ayrı görev açılır**; A'nın istisnası bunu kapatamaz |
| A sevk edilir (istisna `Consumed`) | Koşul sürüyor, geçerli karar kalmadıysa müşteri düzeyi ihtiyaç → `Open` | Yeni talimatlar `ReviewRequired` | `party` ihtiyacı → görev (B görevi ayrıca açıksa açık kalır) |
| Finance A'nın istisnasını B'yi de kapsayan `Party` kararıyla **yeniler** | Durum değişmez (`Acknowledged` kalır / olur) | A, B: `Proceed` | Açık görevler kapsam karşılandığı için kapanır; "yenileme" yeni görev AÇMAZ |
| İstisna süresi dolar / geri alınır | Kapsanmayan ihtiyaç varsa `Open` (yeni Sorun açılmaz); (v1.3) değerlendirme **kapsam dağıtımıyla** müşteri + açık talimatlar + sona eren kararın kapsamında ayrı ayrı yapılır (§9.1a) | `ReviewRequired`/`Stop` | İlgili kapsam için görev |
| Talimat iptal edilir / başka yolla kapanır | Değişmez | — | O talimatın ihtiyacı ve görevi kanıtla (iptal olayı) kapanır |
| Tahsilat/DBS/teminat limit aşımını kaldırır | `Resolved` | `Proceed` (istisnasız) | Bütün açık ihtiyaç görevleri kanıtla kapanır |

**Müşteri düzeyi ihtiyaç** "müşteri için hiç geçerli karar yok" demektir; ihtiyaç açma ve görev kapatma aynı yüklemi (`Issue.SatisfyingDecision`) kullanır. Örnek: A tükendi, müşteri düzeyi görev açık; Finance yalnız B için karar verir → müşteri düzeyi görev bu kararla kapanır, sonraki C talimatı kendi talimat görevini açar. Tabloda "Open" müşteri Sorununun durumudur; `Reopened` yalnız `Resolved` bir Sorunun koşulu geri döndüğünde kullanılır.

Kapı sorgusu istisnanın **yalnız** durumunu ve referansını döndürür; gerekçe ve tutar Finance açıklık matrisine tabidir. Kapsamı eşleşmeyen istisna `Active` sayılmaz.

### 5.4 `WorkItem` ve `ApprovalRequest`

```csharp
public sealed class WorkItem
{
    public WorkItemStatus Status { get; private set; }   // Open · InProgress · WaitingInfo · Escalated · Done · Cancelled
    public int EscalationStep { get; private set; }

    public DecisionScope Scope { get; private set; } = DecisionScope.Party;   // karar ihtiyacının kapsamı (Party | Instruction(id))

    public static WorkItem ForDecisionNeed(Guid companyId, Issue issue, DecisionScope scope, WorkItemTemplate template, DisclosureMatrix visibility, EscalationPolicy policy, DateTimeOffset now)
        => /* DedupKey = issue.Key.ToDedupKey() + ":" + scope.Key (ihtiyaç başına tek açık görev); Scope = scope; SourceKind = ImpactRule;
              Title = şablon kodu + parametre referansı (serbest metin YOK; §4.5); DataClass = kuralın değerlendirici sınıfı (IR-LOG-001: Gizli); OwnerRole = policy.Chain[0] (template.OwnerRole ile eşit olmalı, aksi WorkRuleException);
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
        RuleTrigger.Event("finance.risk-exception.consumed.v1"),           // tek sevkiyat istisnası tükendi → Acknowledged → Open (koşul sürüyor, karar kalmadıysa)
        RuleTrigger.Event("finance.risk-exception.revoked.v1"),
        RuleTrigger.Event("logistics.loading-instruction.cancelled.v1"),   // talimatın karar ihtiyacı ve görevi kanıtla kapanır
        RuleTrigger.Schedule("work.rules.reconciliation"),                 // uzlaştırma taraması: DURUM sorgusu, delta değil (§9.4)
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
    public Task<AiPolicyDecision> EvaluateDispatchAsync(AiDispatchRequest r, CancellationToken ct)  => /* adım 12 ön koşulları: Approved, TargetLock yok, payload bütünlüğü; kill switch, Ai:Enabled, araç bayrağı (→ IsExecutionDisabled: yazma yok, bekle); politika/hedef değişimi (→ IsDrift: Stale); güncel yetki düşüşü (→ gönderim öncesi ret); sahip komut expected version + idempotency ile çağrılır */ default!;
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

**B — sıralı (kimlik, rowversion) çifti eşitliği (çok adımlı model döngüleri; v1.3, R-06):** Karşılaştırma iki ayrı alanı karıştırmaz: `Version` uygulamanın concurrency sayacıdır (satır başına 1, 2, 3…); `RowVersion` SQL Server'ın veritabanı genelinde her insert/update'te atanan 8 baytlık değeridir — tarih veya uygulama revizyonu değildir. **v1.2 düzeltmesi yetersizdi:** `(sayım, MAX(RowVersion), kimlik kümesi hash'i)` bütün güncellemeleri yakalamaz, çünkü rowversion değeri güncelleme sırasında atanır ve **değer sırası commit sırası değildir** (commit edilmemiş aktif değerler için `MIN_ACTIVE_ROWVERSION`; Microsoft senkronizasyon notu). Karşı örnek: A'nın güncellemesi 101'i alır, commit etmez; B 102 ile güncellenip commit eder. İlk okuma A=100, B=102 görür; A sonra commit eder. Sayım (2), en yüksek sürüm (102) ve kimlik kümesi ({A,B}) aynı kalır; A'nın değeri değişmiştir.

| Okuma türü | Başlangıçta kaydedilen | Döngü sonunda kontrol | Yakaladığı / yakalamadığı |
|---|---|---|---|
| Tek kayıt | `(id, RowVersion)` | Aynı `id` için güncel `RowVersion` **eşit** | Commit edilmiş her güncelleme (pasifleştirme dahil; ERP'de silme yok) |
| Liste/sorgu | Sorgu parmak izi (araç + normalize argümanlar + şirket) ile **kimliğe göre sıralı `(id, değişim belirteci)` çiftlerinin** SHA-256 özeti (belirteç: `RowVersion`, yoksa satırın her güncellemede artan `Version` concurrency sayacı — Finance'te `RowVersion` yok, `Version` var, §10.6); aracın beyan ettiği **bağlı kayıtlar** (ör. sipariş satırları, tahsisler, fiyat kaydı) aynı özete girer | **Aynı sorgu** + aynı bağlı kayıt seti yeniden çalıştırılır; özet **eşit** olmalı | Ekleme, çıkarma ve kapsamdaki her satırın commit edilmiş güncellemesi — commit sırası ne olursa olsun, çünkü satırın kendi değeri değişir. **Yakalamaz:** beyan edilmemiş bağlı kayıtlar; son kontrol anında henüz commit edilmemiş değişiklik (o, kesitten "sonra"dır) |

Aracın her kaynağı için değişim belirteci **beyan edilir**; belirteci olmayan kaynak B ile doğrulanamaz: o kaynağı içeren birleşik okuma ya mekanizma A ile yapılır ya da sonuç her zaman "eskimiş olabilir" damgası alır. `sayım + MAX(RowVersion)` yalnız ucuz ön kontrol olarak kalabilir; eşitlik **kanıtı** değildir. Uyuşmazlıkta kaynak yeniden okunur veya sonuç `coverage.Stale(refs)` damgası alır (G-28, G-57). Sahip modülde hafif `GetVersionsAsync` benzeri okuma yoksa (DOĞRULANACAK) aynı READ aracı yeniden çağrılır.

**B'nin verdiği güvence (daraltılmış):** beyan edilen kapsamdaki satırların ilk okuma ile son kontrol arasında commit edilmiş değişikliğe uğramadığını gösterir; kaynaklar farklı anlarda okunduğu için bu, "son kontrol anında hâlâ geçerli olan okumaların birleşimi"dir — **ortak anlık görüntü değildir**. Kritik birleşik okumada (tutar kesinliği gereken yönetim toplamı, stok + tahsis + termin birleşimi) gerçekten ortak kesit gerekiyorsa **A kullanılır**; B yalnız çok adımlı model döngülerinde değişim tespiti içindir. Master §5.7 aynı tarifi taşır.

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
        foreach (var rule in rules.For(trigger, context.OnlyRule))                               // (v1.3) fan-out tek kurala daraltır; IssueKeysFor aynı filtreyi kullanır
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
    private const int MaxAttempts = 3;

    public async Task<RuleTrackResult> TrackAsync(RuleTrigger trigger, RuleContext context, WorkCommandMetadata metadata, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await TrackOnceAsync(trigger, context, metadata, ct); }
            catch (WorkStoreConflictException) when (attempt < MaxAttempts)                 // 2601/2627 veya rowversion (TrackHead, Issue, görev tekilliği): TÜM save geri alındı
            {
                store.Reset();                                                                // ChangeTracker.Clear; durum YENİDEN yüklenir ve hesap YENİDEN yapılır
            }
            // Üçüncü çakışmada istisna yükselir: work.Attempts yazılmadığı için aynı key yeniden denenebilir; olay tüketicisi teslimi tamamlanmış
            // saymaz (yeniden teslim), zamanlı yol bir sonraki periyotta işler; metrik work.rules.track.contention.
        }
    }

    private async Task<RuleTrackResult> TrackOnceAsync(RuleTrigger trigger, RuleContext context, WorkCommandMetadata metadata, CancellationToken ct)
    {
        if (await store.FindTrackReplayAsync(metadata, ct) is { } replay) return replay;      // aynı teslim / aynı key (kapsam dahil, R-04) → ilk sonuç (work.Attempts)
        var now = clock.GetUtcNow();

        // (v1.3, R-05) SIRA ŞARTTIR: önce kaydedilmiş durum — TrackHead + Issue + açık karar görevleri — YÜKLENİR, SONRA hesap yapılır.
        // Commit, yüklenen TrackHead'in rowversion'ını (yoksa PK tekilliğini) doğrular. Böylece kaydedilen her durum, bir önceki kaydedilmiş
        // durumdan SONRA okunmuş veriden türer: eski bir "risk var" hesabı, arada yeni tahsilatla kapanmış Sorunu yeniden açamaz (çakışır → yeniden yükle + yeniden hesapla).
        var state = await store.LoadTrackStateAsync(context.CompanyId, rules.IssueKeysFor(trigger, context)   /* context.OnlyRule filtresiyle */, ct);
        var outcomes = new List<RuleOutcome>();
        foreach (var result in await gate.EvaluateAsync(trigger, context, ct))                // context.OnlyRule doluysa yalnız o kural (fan-out, §9.1a)
        {
            var rule = rules.Get(result.RuleId, result.RuleVersion);
            var key = rule.IssueKeyFor(context);
            var head = state.HeadFor(key);                                                    // yoksa "yeni baş" (insert → PK yarışında çakışma)

            // GÖLGE: yalnız ölçüm kaydı. Operasyonel Sorun, karar ihtiyacı, görev ve TrackHead ne AÇILIR ne KAPANIR ne DEĞİŞİR.
            if (result.Mode == RuleMode.Shadow)
            { store.StageShadowEvaluation(ShadowEvaluation.From(rule, key, context.Scope, result, trigger, now)); outcomes.Add(RuleOutcome.Shadow(rule, key, result)); continue; }

            Issue? issue = state.IssueFor(key); EvaluationOutcome outcome;
            if (issue is null)
            {
                if (!result.ConditionHolds || result.IsPartial)
                { store.TouchHead(head, evaluationId: null, now); outcomes.Add(RuleOutcome.NothingToTrack(rule, key, result)); continue; }   // koşul yoksa Sorun satırı YOK; baş yine yazılır
                issue = Issue.Open(context.CompanyId, key, result); outcome = EvaluationOutcome.Raised;
            }
            else outcome = issue.ApplyCondition(result);                                      // 1. aşama: koşul

            var openItems = state.OpenDecisionItemsFor(key);
            var opened = new List<WorkItem>(); var closed = new List<WorkItem>();
            if (outcome != EvaluationOutcome.Partial)                                           // kaynak eksikken ihtiyaç/görev DEĞİŞMEZ
            {
                // 2. aşama: KARAR İHTİYAÇLARI (kapsam başına). Önce karşılanan/ortadan kalkan ihtiyaçların görevleri kanıtla kapanır…
                foreach (var item in openItems)
                {
                    var satisfiedBy = !issue.ConditionHolds ? issue.ResolvedByRef
                        : issue.SatisfyingDecision(item.Scope)?.ExceptionRef                           // NeedsDecisionFor ile AYNI yüklem
                        ?? (context.ClosedInstructionId == item.Scope.InstructionId && item.Scope.Kind == "Instruction" ? context.TriggerRef : null);
                    if (satisfiedBy is not null) { item.Close(ClosureEvidence.FromRule(satisfiedBy, now)); closed.Add(item); }
                }
                // …sonra BU değerlendirmenin kapsamı (context.Scope; fan-out her ilgili kapsamı ayrı çağırır, §9.1a) için ihtiyaç varsa ve görevi yoksa görev BU işlemde açılır.
                var scope = context.Scope;
                if (rule.Effect.HasFlag(EffectKind.WorkItem) && issue.NeedsDecisionFor(scope)
                    && !openItems.Any(i => i.Scope == scope && !closed.Contains(i)))
                {
                    var escalation = await store.GetEscalationPolicyAsync(rule.WorkItem.EscalationPolicyId, ct);
                    opened.Add(WorkItem.ForDecisionNeed(context.CompanyId, issue, scope, rule.WorkItem, rule.Disclosure, escalation, now));   // DedupKey = key + scope
                }
                var sync = issue.SyncDecisionState(openItems.Count - closed.Count + opened.Count);   // Acknowledged ⇄ Open (karar yenilenmesi durumu değiştirmez)
                if (outcome == EvaluationOutcome.Unchanged) outcome = sync;
            }

            var evaluation = Evaluation.From(issue, result, outcome, trigger, context, head.NextSeq, now);   // sonuç hesaplandıktan SONRA append-only kayda yazılır (ScopeKey, HeadSeq)
            store.Stage(issue, evaluation, opened, closed);                                       // + WorkItemEvent (Created/Closed)
            store.TouchHead(head, evaluation.Id, now);                                             // (v1.3) Seq+1; rowversion ile doğrulanır
            outcomes.Add(new RuleOutcome(rule.RuleId, rule.Version, key, outcome, result.Gate, rule.Disclosure, opened.Select(x => x.Id).ToArray()));
        }
        store.StageAttempt(metadata, outcomes);                                                  // work.Attempts: (CompanyId, "work.rules.track.v1", keyHash)
        await store.CommitAsync(ct);                                                             // TEK SaveChanges → tek SQL işlemi; audit aynı save'de (ADR-009)
        return new RuleTrackResult(outcomes, IsReplay: false, metadata.CorrelationId);
    }
}
```

- **Tek işlem:** `Issue`, `Evaluation`, açılan/kapanan `WorkItem`, `WorkItemEvent` ve `Attempt` aynı `WorkDbContext`'tedir (hepsi Work modülünün tablosu); tek `SaveChangesAsync` hepsini birlikte yazar ya da hiçbirini yazmaz. Başka modüle yazma yoktur (AGENTS.md).
- **Görevler karar ihtiyacı başına ve kapsamlıdır:** `DedupKey = IssueKey + ScopeKey`. Bir talimata verilen karar yalnız o talimatın görevini kapatır; başka talimatın bekleyen ihtiyacı açık iş olarak görünür (§5.3 A/B senaryosu). Görev açma ve kapama aynı işlemdedir; kapanan eski görev, sonradan açılması gereken **yeni** görevi `UX_WorkItems_OpenDedupKey` üzerinden engellemez.
- **Eşzamanlılık:** iki değerlendirici aynı anda görev açmaya kalkarsa `UX_WorkItems_OpenDedupKey` ikinciyi 2601/2627 ile reddeder; **bütün save geri alınır**, tracker durumu sıfırlayıp yeniden yükler, yeniden hesaplar ve bu kez kazananın görevini görür (ikinci görev açılmaz).
- **(v1.3, R-05) Eski hesap yeni durumu geri alamaz:** karşı örnek — eski değerlendirme "risk var" sonucunu alıp beklerken yeni değerlendirme güncel tahsilatı görüp Sorunu kapatır; eski değerlendirme devam edince kapanmış Sorunu taze okuyup yeniden açardı. Şimdi: her kalıcı değerlendirme **önce** `TrackHead` + Sorun + açık görevleri yükler, **sonra** hesaplar ve commit'te yüklediği `TrackHead` sürümünü doğrular; arada başka değerlendirme kaydettiyse çakışır, durum yeniden yüklenir ve hesap **yeniden** yapılır. Kaydedilen durumun dayandığı veri, önceki kaydedilmiş durumdan sonra okunmuştur (önceki durumun hesabı onun commit'inden önce, bizim okumamız ise o commit'i yükledikten sonra yapıldı). `TrackHead` koşul yokken de yazılır; böylece "koşul yok" diyen yeni değerlendirme, eski "koşul var" değerlendirmesinin Sorun açmasını da çakıştırır. Bu, iş satırının rowversion'ını **kaynak doğrulaması** saymaz; kaynağın tazeliğini sıralama + başlık sürümü sağlar.
- **Takip kimliği (v1.3, R-04):** her ayrı değerlendirme **kapsamı** tekrar anahtarında da ayrılır: olay tetiklemede `rule:{deliveryId}:{ruleId}:{issueKey}:{scopeKey}` (tek teslim birden fazla kural/hedef/kapsama dağılabilir), zamanlı taramada `sweep:{ruleId}:{slot}:{issueKey}:{scopeKey}`, takip komutunda istemcinin `Idempotency-Key`'i (tek istek = tek odak kapsamı; istek hash'i `scopeKey`'i içerir, farklı kapsamla aynı key → 409). Hepsi `work.Attempts`'a yazılır. Aynı taramanın tekrarı aynı anahtarı üretir (replay; fazladan görev yok); aynı dilimde A ve B ayrı anahtarlarla ayrı değerlendirilir.
- **Gölge mod operasyonel durumu değiştirmez:** kural `Shadow` modundaysa (veya `Work:Rules:ShadowOnly=true`) değerlendirme yalnız ayrı, append-only `work.ShadowEvaluations` tablosuna yazılır; operasyonel `Issue`, karar ihtiyacı ve `WorkItem` **ne açılır, ne kapanır, ne değişir**. Önceden aktif çalışmış bir kural gölgeye alınırsa mevcut Sorunlar ve görevler olduğu gibi kalır (insan kapatır veya kural yeniden aktif olunca normal akış sürer). Gölgeden aktife geçişte toplu görev üretilmez; görev, aktivasyondan sonraki ilk değerlendirmede (olay, zamanlı tarama veya uzlaştırma taraması) oluşur; ilk taramada kaç görev açılacağı yönetim ekranında önizlenir.
- **Kural politikası okuması yazma değildir:** `IWorkRulesPolicySnapshot`, `work.ImpactRules` durumlarını **salt-okunur** bir bağlamdan (`WorkReadOnlyDbContext`: `NoTracking`, `SaveChanges*` fırlatır — mevcut `CommandExecutionDbContext` deseni, Ek K-1 madde 14) okuyup bellekte tutar; kapı yolu bu anlık görüntüyü kullanır.

- Motor **blok üretmez**: `EffectKind` içinde `Block` üyesi **yoktur** (derleme zamanı garanti).
- AI kapalıyken çalışır: `Work.Rules` hiçbir `Ai.*` türüne referans vermez; capability/kill switch bayrakları `Ai:*` altındadır, `Work:Rules:Enabled` ayrıdır (§19).

#### 9.1a Kapsam dağıtımı: karar ihtiyacının kaynağı açık görevler değildir (v1.3, R-04/R-05)

**Karşı örnek (v1.2):** A ve B için ayrı istisnalar verilmiş, iki görev de kapanmış; B'nin istisnası sona erer, A'nınki aktif kalır. Süre-sonu değerlendirmesi `Party` kapsamıyla çalışırsa A'nın kararı müşteri düzeyi ihtiyacı karşılar; kapanmış B görevi `FindOpenDecisionItemsAsync`'e hiç gelmez → B için görev açılmaz. **Düzeltme:** müşteri düzeyi tetikleyiciler (Finance karar olayları, istisna süre sonu, tahsilat, uzlaştırma) değerlendirmeyi **ihtiyacın doğabileceği bütün kapsamlara** dağıtır; kapsam listesi sahip kaynaktan gelir:

```csharp
// GoymenERP.Modules.Work.Rules/RuleScopeFanOut.cs (ÖNERİ)
internal sealed class RuleScopeFanOut(IImpactRuleCatalog rules, IDecisionScopeSource scopes, IFanOutContinuationStore continuations, IImpactRuleTracker tracker) : IRuleScopeFanOut
{
    public async Task<IReadOnlyList<RuleTrackResult>> TrackAsync(RuleTrigger trigger, RuleSubject subject, TrackKeyBase keyBase, CancellationToken ct)
    {
        var results = new List<RuleTrackResult>();
        foreach (var rule in rules.For(trigger))
        {
            var issueKey = rule.IssueKeyFor(subject);
            var cursor = await continuations.GetAsync(rule.RuleId, trigger, subject, ct);        // (v1.3 iç kontrol) yarım kalmış dağıtımın imleci; yoksa baştan
            var page = await scopes.ScopesForAsync(rule, trigger, subject, cursor, ct);          // durum sorgusu, keyset, kardinalite sınırı
            if (page.IsTruncated) await continuations.SaveAsync(rule.RuleId, trigger, subject, page.NextCursor, ct);   // work.SchedulerLeases TaskKind "rules.fanout"
            else await continuations.ClearAsync(rule.RuleId, trigger, subject, ct);              // WorkRulesWorker devamı hemen alır (olay yolunda da kaybolmaz)
            foreach (var scope in page.Scopes)
                results.Add(await tracker.TrackAsync(trigger, RuleContext.For(subject, scope, onlyRule: rule),
                    keyBase.For(rule.RuleId, issueKey, scope), ct));                              // anahtar kapsamı içerir (R-04)
        }
        return results;
    }
}
```

| Kural | `IDecisionScopeSource.ScopesForAsync` — eksiksiz kapsam kümesi (hangi sorgudan) |
|---|---|
| IR-LOG-001 | `{Party}` ∪ **açık yükleme talimatları** (Logistics durum port'u `ILogisticsOpenInstructionReadsV1.ListOpenForPartyAsync(companyId, partyAccountId)`: kaydedilmiş, sevk edilmemiş, iptal edilmemiş talimatlar — sahip dilim eki, DOĞRULANACAK) ∪ **bu tetikleyicide değişen kararın kapsamı** (olay/süre-sonu yükündeki `DecisionScope`) ∪ **açık karar görevlerinin kapsamları** (kapanması gerekenler için). Yeni tablo gerekmez; kaynak sahip modülün durum sorgusudur |
| Diğer kurallar (Ek B) | Kural tanımında `ScopeSource` zorunlu alan; tanımsızsa yalnız `{Party}` ve kural katalog testi uyarı verir |

Sonuç: A aktifken B sona erdiğinde dağıtım B'yi (hâlâ açık talimat ve değişen kararın kapsamı olarak) ayrıca değerlendirir; `SatisfyingDecision(Instruction(B))` yoktur → B için görev açılır; A'nın kapsamı karşılanmış kalır (G-55). B sevk edilmiş veya iptal edilmişse açık talimat değildir → ihtiyaç doğmaz. Talimat odaklı takip komutu (`POST /rules/evaluations`) zaten tek kapsamla (o talimat) çalışır; dağıtım yalnız müşteri düzeyi tetikleyicilerdedir.

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
// GoymenERP.Api/RulesRuntime/RulesEndpoints.cs (ÖNERİ; v1.3 — R-09: kapı kimliği, niyet ve hedef kapsamı SUNUCU kataloğunda bağlıdır)
var group = endpoints.MapGroup("/api/v1/rules").WithTags("Work").RequireAuthorization().RequireCompanyContext();
group.MapPost("/gate", async (GateQueryRequestV1 body, IGateCatalog gates, IImpactRuleGate gate, IGateQueryAuthorizer auth, HttpContext http, CancellationToken ct) =>
{
    SetPrivateNoStore(http.Response);
    if (!gates.TryGet(body.GateToolId, out var descriptor))                                      // GateDescriptor: { GateToolId, AllowedIntents{IntentKind → TargetEntityType}, Permission, TargetReads }
        return ErpProblems.BadRequest("gate_tool_unknown");
    if (!descriptor.Allows(body.IntentKind, body.Target.EntityType))                             // yetkili kapı adıyla BAŞKA niyet veya hedef türü gönderilemez
        return ErpProblems.BadRequest("gate_intent_mismatch");
    var actor = await auth.AuthorizeAsync(descriptor, body.Target, ct);                          // (1) descriptor.Permission (örn. finance.risk.gate.read — ÖNERİ)
    if (!actor.Granted) return Results.Problem(statusCode: 403);                                 // (2) hedef kaydın bu aktör için sahip modülde okunabilirliği ve kapsamı
                                                                                                 //     (şirket, saha/lokasyon) — descriptor.TargetReads port'uyla; aksi 403 (nondisclosing)
    var context = RuleContext.ForGate(descriptor, body, actor);                                  // kurallar istemci alanından değil, descriptor'ın bağladığı niyetten seçilir
    var results = await gate.EvaluateAsync(RuleTrigger.OnDemand(descriptor.IntentFor(body.IntentKind)), context, ct);   // saf hesap: YAZMA YOK
    return Results.Ok(GateQueryResponseV1.From(results, actor.DisclosureLevel));                  // yalnız kapı kararı + izinli gerekçe kodları
})
.WithName("QueryRuleGateV1").Accepts<GateQueryRequestV1>("application/json").Produces<GateQueryResponseV1>(200)
.ProducesErpBadRequest("validation_failed", "gate_tool_unknown", "gate_intent_mismatch").ProducesErpUnsupportedMediaType().ProducesErpAuthenticatedProblems();
```

**Kaydedilmemiş taslak (v1.3 iç kontrol):** form içi kapı çoğu zaman henüz kaydedilmemiş bir talimat taslağı için çağrılır (master §3.4 [1]). Bu durumda hedef `{ entityType, id }` değil `DraftBasis`'tir: müşteri kimliği + taslağın dayandığı **kaynak satır kimlikleri** (sipariş satırları, tahsis). Sunucu yetkiyi bu kayıtların aktör için okunabilirliğiyle doğrular ve miktar/tutarı **kendisi** bu satırlardan hesaplar; istemcinin gönderdiği tutar veya `stageTransition` değeri kabul edilmez. Böylece farklı tutarlarla tekrar sorgulayıp limit boşluğunu ikili aramayla çıkarmak mümkün olmaz; ek olarak aktör başına sorgu oranı sınırlıdır (`Work:Rules:GateRateLimit`, yapılandırma).

**Kapı–niyet–hedef sözleşmesi (v1.3, R-09):** yetki yalnız kapı adına verilmez. `IGateCatalog` (Work.Rules, kod tanımlı ve sürümlü; `GateCatalogManifestV1` testte dondurulur) her kapı için **hangi niyetleri**, **hangi hedef türlerini** ve **hangi kayıt kapsamını** değerlendirebileceğini bağlar. Örnek: `finance.risk.shipment-gate@1` → niyet `logistics.loading-instruction.issue-intent` / `logistics.shipment.record-intent`, hedef türü `Logistics.LoadingInstruction`, izin `finance.risk.gate.read` + hedef talimatın aktör için Logistics'te okunabilir olması (sahip okuma port'u). Eşleşme yoksa hiçbir kural çalışmaz. Negatif testler (§18.3 `RulesGateHttpAcceptanceTests`): yetkili kapı adı + başka niyet → 400 `gate_intent_mismatch`; + başka hedef türü → 400; + aktörün kapsamı dışındaki hedef kayıt → 403 (kodsuz); üçünde de değerlendirici port'u **hiç çağrılmaz** (sayaçlı sahte port) (G-60).

Kapı sorgusu **hiçbir kayıt yazmaz** — değerlendirme kaydı dahil (v1.0'daki "kanıt kaydı" seçeneği kaldırıldı; K-AÇIK-06 bu öneriyle kapanır, onay ADR-008 madde 6'da). Endpoint'in DI grafiğinde **iş verisine** yazma yapabilen hiçbir bileşen bulunmaz: `IImpactRuleTracker`, `IWorkRulesStore`, yazılabilir `WorkDbContext` ve `IWorkItemCommandsV1` yoktur; tek yazıcı, yalnız **reddedilen** çağrılarda kullanılan `ERP-000L` security-event kanalıdır (iş/work/audit şemaları dışında; yetkili normal çağrı onu da kullanmaz); kural durumu yalnız salt-okunur `WorkReadOnlyDbContext` üzerinden gelen anlık görüntüden okunur. Sahip modülün değerlendirme port'u da **salt-okunur sistem port'u** olmak zorundadır (§9.2). Kanıt iki katmanlıdır: mimari test (`GateQueryHasNoWriteDependenciesTests`: grafikte yazma bağımlılığı yok) ve davranış testi — `IServiceScopeFactory` üzerinden çözülen sahip port'larını da kapsamak için statik grafik yetmez; **yetkili, normal** bir kapı sorgusundan önce ve sonra **bütün şemalardaki** satır sayıları ve rowversion'lar, audit, outbox ve security-event tabloları dahil, aynı kalmalıdır. Reddedilen/yetkisiz çağrılar güvenlik olayı üretir ve ayrı test edilir.

### 9.4 Kalıcı takip komutu (form kaydında veya olayda)

Form, kaydı başarıyla yaptıktan sonra (örn. yükleme talimatı kaydedildi veya "issue" niyeti gönderildi) **ayrı** bir komut çağırır; `ERP-000F` olayları geldiğinde aynı iş olay tüketicisinden yapılır:

| Yöntem ve yol | İzin (ÖNERİ) | Idempotency-Key | Cevap |
|---|---|---|---|
| `POST /api/v1/rules/evaluations` | `work.rules.track` — formu kullanan operasyon rolüne verilir; **finans verisi görme izni değildir**, yalnız "takibi başlat" iznidir | Zorunlu | 201: kapı kararı + "takip açıldı" bayrağı + görevin kimliği (kullanıcının açıklık düzeyinde; tutar yok) |

Bu komut `IImpactRuleTracker.TrackAsync` çağırır (§9.1): Sorun, Değerlendirme ve Finans görevi aynı işlemde yazılır. Sevkiyat kullanıcısının ekranındaki "Finans'a görev açıldı" metni **yalnız** bu komutun 201 cevabından sonra görünür.

**Takibin hiç başlatılmaması boşluğu ve garanti sırası (v1.2):** Work içindeki tek işlem, Sorun ile görevin birlikte yazılmasını garanti eder; ama takibin **başlatılmasını** garanti etmez. Kaynak kayıt (talimat, Finance kararı) başarıyla yazılıp ekran ikinci çağrıyı yapamadan kapanırsa Work'te hiçbir kayıt oluşmaz. Bu yüzden üç yolun rolü açıkça ayrılır:

| Yol | Rolü | Garanti verir mi? |
|---|---|---|
| Ekranın ikinci çağrısı (`POST /rules/evaluations`) | **Hızlandırıcı**: kullanıcı görevi hemen görür | Hayır — tarayıcı/bağlantı kapanırsa kaybolabilir |
| Transactional outbox olayı (`ERP-000F`; kaynak değişikliği ile olay aynı işlemde) | **Birincil garanti**: olay kaynak kayıtla birlikte kalıcıdır, aktarım istemciye bağlı değildir; consumer `work.Attempts`/inbox ile tekildir | Evet (ERP-000F kabulünden sonra) |
| **Uzlaştırma taraması** (`work.rules.reconciliation`, zamanlı; §14.1 `WorkRulesWorker`) | Outbox gelene kadar **geçici garanti**, sonra emniyet ağı: sahip modüllerin **durum** sorgusuyla (ör. açık yükleme talimatları, geçerli/tükenmiş Finance kararları) kuralı yeniden çalıştırır; takip edilmemiş kaynakları yakalar | Evet, tarama periyodu kadar gecikmeyle |

Kurallar: (1) ekranın ikinci çağrısı hiçbir zaman **tek dayanak** değildir; outbox kabul edilene kadar bu kural için uzlaştırma taraması **zorunludur** ve kural katalogunda "geçici yol" olarak işaretlenir. (2) Tarama **delta değil durum** sorgusudur: "son çalıştırmadan beri güncellenen" filtresi zamanla doğan koşulları (hiç güncellenmemiş kaydın vadesinin/termininin geçmesi) kaçırır; tarama "açık ve koşul tarihi bugünden önce" gibi durum koşullarıyla, anahtar tabanlı sayfalama (keyset) ve kardinalite sınırıyla çalışır; bütçe aşımında `Partial` biter ve kalan sayfadan devam eder. (3) Tarama da `TrackAsync`'i çağırır; aynı kaynak için tekrar görev üretmez (karar ihtiyacı başına tek açık görev + `work.Attempts`).


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

internal sealed class AiPreviewService(IAiProposalStore proposals, IAiCommandAttemptStore attempts, IAiPayloadVault vault, IAiToolCatalog catalog, IAiPolicyRefs policyRefs,
    IOwnerApprovalPolicyResolver approvalPolicies, IOptions<AiOptions> options, TimeProvider clock) : IAiPreviewService
{
    public async Task<AiPreview> CreateAsync(AiWriteIntent intent, AiToolContext context, AiCommandMetadata metadata, CancellationToken ct)
    {
        // 0) İSTEK düzeyinde tekrar gönderim: aynı aktör + şirket + aynı Idempotency-Key → DAHA ÖNCE üretilmiş aynı niyet ve önizleme.
        //    (OperationIntentId yine sunucuda üretilir; bu yalnız ona ilk erişimi tekrar gönderime dayanıklı yapar.)
        var requestHash = AiCanonicalJson.HashRequest(intent);                                  // istemcinin gönderdiği niyet gövdesi (araç, argümanlar, hedef, reissueOf)
        if (await attempts.FindReplayAsync(AiCommands.ProposalCreateV1, metadata.IdempotencyKey, context.ActorId, requestHash, ct) is { } replay)
            return await proposals.GetPreviewAsync(replay.ProposalId, isReplay: true, replay.OriginalCorrelationId, ct);   // farklı içerik/aktör → 409 same_key_payload_mismatch (FindReplayAsync fırlatır)

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
            ? (await proposals.GetAsync(prior, ct)).Reissue(hash, intent.Target, await approvalPolicies.ForAsync(registration.OwnerContractId, canonical, context, ct),
                validation.Passed, shadow, clock.GetUtcNow(), options.Value.Preview.Expiry)
            : AiProposal.StartIntent(context.CompanyId, context.RunId, registration.Ref, intent.Target, hash, intent.Actors,
                await approvalPolicies.ForAsync(registration.OwnerContractId, canonical, context, ct),   // (v1.3) sahip politikası; tanımsızsa Default (R-08)
                validation.Passed, shadow, clock.GetUtcNow(), options.Value.Preview.Expiry);
        attempts.Stage(AiCommands.ProposalCreateV1, metadata, context.ActorId, requestHash, proposal.Id, proposal.OperationIntentId);   // ai.CommandAttempts
        await proposals.AddAsync(proposal, cipherRef, validation, ct);                           // ai.Proposals + ai.CommandAttempts + audit TEK save ("ai.proposal.created.v1", Proposed)
        // Yarış: aynı key ile iki eşzamanlı istek → attempt PK 2601 → kaybeden kazananın önizlemesini replay eder (Sales StoreDraftAsync deseni).
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
    IAiDispatchScopeFactory scopes, IAiSecurityEventSink security, AiMetrics metrics, TimeProvider clock) : IAiCommandDispatcher
{
    public async Task<AiDispatchResult> DispatchAsync(Guid proposalId, CancellationToken ct)
    {
        var proposal = await proposals.GetAsync(proposalId, ct);
        if (proposal.Status != AiProposalStatus.Approved) return AiDispatchResult.Refused(AiProposalCodes.NotApproved);   // yalnız Approved gönderilir
        if (clock.GetUtcNow() >= proposal.ExpiresAtUtc)                                          // onay süresi geçti → gönderim öncesi Stale
        { proposal.MarkStale("expired"); await proposals.SaveAsync(proposal, ct); return AiDispatchResult.Refused(AiProposalCodes.ProposalExpired); }
        var gate = await policy.EvaluateDispatchAsync(AiDispatchRequest.For(proposal), ct);      // adım 12: güncel yetki/politika/hedef sürümü + kill switch + araç bayrağı (yeni yürütme izni)
        if (!gate.Allowed)
        {
            if (gate.IsExecutionDisabled) return AiDispatchResult.Deferred(gate.Code);           // (v1.3) kill switch / araç bayrağı / AI kapalı: HİÇBİR ŞEY YAZILMAZ; öneri Approved kalır,
                                                                                                   // süre dolunca Stale + bildirim (§10.4 madde 6) — terminal Failed'a çevrilmez
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
        try { await proposals.SaveAsync(proposal, ct); }                                         // kalıcı kayıt ÖNCE (çökme → süre aşımında Unknown'a alınıp kurtarılır)
        catch (AiProposalConcurrencyException)                                                   // satır bu arada değişti: kazananın durumunu dön (gönderildi / Stale / …)
        { var fresh = await proposals.ReloadAsync(proposalId, ct); return AiDispatchResult.From(fresh, concurrent: true); }   // Dispatched ise "zaten gönderiliyor"
        return await ExecuteAsync(proposal, verified, DispatchAttempt.First, ct);
    }

    /// <summary>Gönderim ve kurtarmanın ORTAK yürütme adımı: aynı doğrulanmış zarf, aynı key. (v1.3, R-01) Sahip cevabı önce SINIFLANDIRILIR
    /// (OwnerOutcome), sonra tek noktadan (AiProposal.ApplyOwnerOutcome) uygulanır. Reddin kesinliği "ilk deneme mi?" sorusuna bağlı DEĞİLDİR:
    /// (a) sahibin bu key için kaydı var mı, (b) kayıtsız retlerde bu key ile başka gönderim çıkmadığı SATIRDA (rowversion) kanıtlanıyor mu.
    /// attempt yalnız audit ve metrik içindir.</summary>
    internal async Task<AiDispatchResult> ExecuteAsync(AiProposal proposal, AiVerifiedEnvelope verified, DispatchAttempt attempt, CancellationToken ct)
    {
        await using var scope = scopes.CreateDispatchScope(proposal);                            // yeni DI scope: ICompanyContext, ICorrelationContext(AiAssisted), ICurrentUser(onaylayan)
        var registration = catalog.Resolve(proposal.ToolId, proposal.ToolVersion);               // yürütme YENİ çalıştırmadır: aktif katalog + bayrak (DispatchAsync / EvaluateResendAsync önceden doğruladı)
        OwnerOutcome outcome;
        try
        {
            var result = await registration.ExecuteWriteAsync(scope.ToolContext, verified.Envelope.Payload, ct);   // süreç içi çağrı; OTOMATİK TRANSPORT RETRY YOK (mimari test §18.4)
            outcome = new(OwnerOutcomeKind.Executed, result.CommandRef, null);                      // ilk yürütme VEYA replay: ikisi de bu key için sahibin kaydı
        }
        catch (AiOwnerRefusalException refusal)
        {
            outcome = new(refusal.Kind switch                                                      // adaptör sahip sözleşmesinden eşler (Ek K-2)
            {
                OwnerRefusalKind.SameKeyMismatch => OwnerOutcomeKind.SameKeyMismatch,
                OwnerRefusalKind.RecordedForKey  => OwnerOutcomeKind.RecordedRefusal,
                OwnerRefusalKind.NotRecorded     => OwnerOutcomeKind.UnrecordedRefusal,             // yalnız sahibin açık listesi
                _                                => OwnerOutcomeKind.Unknown,                       // eşlenemeyen kod: belirsiz (kilit kalır) + security-event (adaptör eksik)
            }, null, refusal.Code);
        }
        catch (Exception ex) { outcome = new(OwnerOutcomeKind.Unknown, null, ex.GetType().Name); }   // kesin ret dışındaki her istisna: sonuç bilinmiyor; OTOMATİK RETRY YOK

        proposal.ApplyOwnerOutcome(outcome, afterConcurrentChange: false);
        try { await proposals.SaveAsync(proposal, ct); }                                           // ai.Proposals + audit + ai.Outbox (PendingFacts) TEK save (§10.4a)
        catch (AiProposalConcurrencyException)                                                     // satır bu gönderimden SONRA değişti (kurtarma Unknown'a aldı ve/veya yeniden gönderdi)
        {
            var fresh = await proposals.ReloadAsync(proposal.Id, ct);
            fresh.ApplyOwnerOutcome(outcome, afterConcurrentChange: true);                          // kayıtsız ret burada ASLA kesin sayılmaz: başka gönderim çıkmış olabilir
            await proposals.SaveAsync(fresh, ct);                                                  // yine çakışırsa istisna yükselir; sahip kaydı key'de durur → kurtarma probe/replay ile yeniden okur
            proposal = fresh;
        }
        if (outcome.Kind == OwnerOutcomeKind.SameKeyMismatch) await security.RecordAsync(AiSecurityEvent.ContentConflict(proposal, outcome), ct);
        metrics.DispatchOutcome(proposal, attempt, outcome.Kind);
        return AiDispatchResult.From(proposal);
    }
}
```

**Örtüşen gönderim karşı örneği (R-01) bu kodla nasıl kapanır:** (1) İlk gönderim sahipte bekler; `DispatchGracePeriod` dolar. (2) Koordinatör `MarkUnknown` ve `MarkRecoveryAttempt` (`SendsIssued=2`) kaydeder, aynı key ile yeniden gönderir; sahip bunu **kaydeder**, cevap kaybolur → öneri `Unknown`. (3) İlk gönderimin 403'ü (kayıtsız ret) şimdi gelir: bellekteki nesne `Dispatched`/`SendsIssued=1` görünse de save rowversion çakışmasıyla düşer; taze satıra `afterConcurrentChange: true` ile uygulanır → yalnız `LastAttemptRefusalCode` yazılır, öneri `Unknown` + kilitli kalır. (4) Sonraki taramada probe başarıyı okur → `Executed`. Öneri hiçbir anda serbest bırakılmaz. Ters sıra (ilk gönderimin 403'ü, kurtarma satırı değiştirmeden **önce** kaydedilirse): satırda `SendsIssued=1` ve değişmemiş olduğu save ile kanıtlanır → `Failed`; koordinatörün eski kopyası `MarkUnknown`/`MarkRecoveryAttempt` save'inde çakışır ve yeniden gönderim **hiç çıkmaz** (§10.4: gönderim yalnız `SendsIssued=2` commit edildikten sonra).

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

### 10.4 `AiRecoveryCoordinator` (otomatik retry yok; yeni kimlik yok; v1.3)

Kurtarmanın iki yolu **ayrıdır** ve biri diğerinin yerine geçmez. (v1.3, R-02/R-03) Sonuç okuma **aktif araç kataloğundan bağımsızdır**: sahip sözleşmesine kayıtlı okuyucu (`IAiOwnerOutcomeRegistry`, anahtar `OwnerCommandContract`) araç kapatılmış, gölgeye alınmış veya emekliye ayrılmış olsa da çözülür. Okuyucu kaydı yeni çalıştırma izni **değildir**; yeniden gönderim ise yeni çalıştırmadır ve aktif katalog + bütün güncel koşullar ister.

| Yol | Ne yapar | Kimin yetkisiyle | Sonucu ne kanıtlar |
|---|---|---|---|
| **Sonuç okuma** (`IIdempotencyOutcomeV1.FindAsync`, sahip modülün salt-okunur port'u — ÖNERİ, sahip dilim eki) | Sahibin bu key için kaydını okur ve **kendi eşleşme kuralıyla** (kendi istek hash'i/kanonikleştirmesi) gönderilen payload'la eşleşip eşleşmediğini söyler; iş etkisi yok | Sistem kurtarma aktörü (yalnız bu port'un dar okuma izni; onaylayanın yetkisine bağlı değil) | `Recorded + ThisRequest` → başarı; `RecordedRefusal`/`Fenced` → kayıtlı terminal ret; `Recorded + DifferentRequest` → key'de başka içerik (inceleme); `NotFound` → **hiçbir şey** (yolda olabilir) |
| **Anahtar kilidi** (`IIdempotencyOutcomeV1.FenceAsync`, aynı port — ÖNERİ) | Key'de kayıt yoksa sahip, key'e terminal "kapatıldı" deneme satırı yazar (ADR-011 terminal ret deseni, PK aynı); kayıt varsa onu döndürür | Yalnız insan çözümünde (`ai.recovery.resolve`, K3) | `Fenced` → bundan sonra aynı key ile hiçbir gönderim yürüyemez (replay "kapatıldı" döner) → **etkisizlik kesin**; kayıt çıkarsa o kayıt sonuçtur |
| **Yeniden gönderim** (aynı key, aynı doğrulanmış zarf) | İlk kez iş etkisi üretebilir | Onaylayanın **güncel** yetkisi + gateway'in güncel yürütme koşulları (kill switch, araç bayrağı, capability, sahip onay politikası, azami süre) | Sonuç `ApplyOwnerOutcome` kuralıyla: kayıtlı sonuçlar kesin; kayıtsız ret yalnız deneme reddi (`SendsIssued=2`) |

```csharp
internal sealed class AiRecoveryCoordinator(IAiProposalStore proposals, IAiOwnerOutcomeRegistry outcomes, IAiToolCatalog catalog, AiEnvelopeVerifier verifier,
    AiCommandDispatcher dispatcher, IAiPolicyEvaluator policy, IAiSecurityEventSink security, AiMetrics metrics, IOptions<AiOptions> options, TimeProvider clock) : IAiRecoveryCoordinator
{
    /// <summary>AiRecoveryWorker'dan (başlangıçta ve zamanlı; §14.1) çağrılır — AI model/iş akışı bayraklarından BAĞIMSIZ.
    /// Üç küme: (a) onaylanmış ama gönderilmemiş, (b) süresi geçmiş Dispatched, (c) Unknown. Aynı OperationIntentId; asla yeni kimlik.</summary>
    public async Task RecoverAsync(Guid companyId, CancellationToken ct)
    {
        foreach (var candidate in await proposals.ListPendingRecoveryAsync(companyId, olderThan: options.Value.Recovery.DispatchGracePeriod, ct))
        {
            try { await RecoverOneAsync(companyId, candidate, ct); }
            catch (AiProposalConcurrencyException) { }                                               // başka yol (geç gelen ilk gönderim, diğer koordinatör) kazandı: sonraki taramada taze satır
            catch (Exception ex) when (ex is not OperationCanceledException)
            { metrics.RecoveryFailed(candidate.Id, ex); }                                             // bir öneri şirketin diğer önerilerinin kurtarılmasını DURDURMAZ
        }
    }

    private async Task RecoverOneAsync(Guid companyId, AiProposal proposal, CancellationToken ct)
    {
        // (a) Onay kalıcılaştı, gönderim başlamadan süreç kapandı: TAM gönderim yolu. Kill switch/araç bayrağı kapalıysa DispatchAsync göndermez;
        //     öneri Approved kalır, onay süresi dolunca Stale olur ve isteyene bildirim gider (hiçbir şey gönderilmedi → belirsizlik yok).
        if (proposal.Status == AiProposalStatus.Approved) { await dispatcher.DispatchAsync(proposal.Id, ct); return; }

        // (b) Yarım kalmış gönderim: açıkça Unknown'a alınır (rowversion: ilk gönderimin cevabı bu arada yazıldıysa burada çakışır ve satır olduğu gibi kalır).
        if (proposal.Status == AiProposalStatus.Dispatched) { proposal.MarkUnknown("dispatch_grace_expired"); await proposals.SaveAsync(proposal, ct); }

        // (c1) SONUÇ OKUMA — sahip sözleşmesine kayıtlı okuyucu; aktif araç kataloğundan BAĞIMSIZ; etkisiz; her zaman önce.
        var verified = await verifier.LoadVerifiedAsync(proposal, ct);                            // probe eşleşmesi için sahibe aynı payload verilir
        if (outcomes.Resolve(proposal.OwnerCommandContract) is { } reader && verified is not null)
        {
            var probe = await reader.FindAsync(companyId, proposal.OwnerCommandContract, proposal.IdempotencyKey, verified.Envelope.Payload, ct);
            if (probe.ToOwnerOutcome() is { } observed)                                             // NotFound → null (hiçbir şey kanıtlamaz)
            { proposal.ApplyOwnerOutcome(observed, afterConcurrentChange: false); await proposals.SaveAsync(proposal, ct); return; }   // sonuç olgusu aynı save'de (§10.4a)
        }

        // (c2) YENİDEN GÖNDERİM — en fazla bir kez; koşullar sağlanmazsa YAPILMAZ ve bu niyeti başarısız YAPMAZ (Unknown + kilit + devralma).
        if (proposal.RecoveryAttempts >= 1) { await RequireTakeoverAsync(proposal, "second_uncertainty", ct); return; }
        if (verified is null)
        {
            await security.RecordAsync(AiSecurityEvent.PayloadIntegrity(proposal, phase: "recovery"), ct);
            await RequireTakeoverAsync(proposal, "payload_integrity", ct); return;                  // bozuk zarf gönderilmez; "Failed" denmez
        }
        if (clock.GetUtcNow() - proposal.DispatchedAtUtc!.Value > options.Value.Recovery.MaxResendAge) { await RequireTakeoverAsync(proposal, "resend_window_closed", ct); return; }
        if (!catalog.TryResolve(proposal.ToolId, proposal.ToolVersion, out _)) { await RequireTakeoverAsync(proposal, "tool_not_active", ct); return; }   // yeniden gönderim = yeni çalıştırma
        var resend = await policy.EvaluateResendAsync(AiDispatchRequest.For(proposal), ct);        // güncel: kill switch, capability, onaylayanın izni, sahip onay politikası, politika sürümü
        if (!resend.Allowed) { await RequireTakeoverAsync(proposal, $"resend_blocked:{resend.Code}", ct); return; }

        proposal.MarkRecoveryAttempt(clock.GetUtcNow());                                           // SendsIssued 1→2; rowversion: tek koordinatör
        await proposals.SaveAsync(proposal, ct);                                                   // ÖNCE commit: ilk gönderimin geç kayıtsız reddi artık kesin sayılamaz (R-01)
        var result = await dispatcher.ExecuteAsync(proposal, verified, DispatchAttempt.RecoveryResend, ct);
        if (result.Proposal.Status == AiProposalStatus.Unknown)                                   // neden BU denemenin sonucundan (ilk gönderimden kalan eski kod kullanılmaz)
            await RequireTakeoverAsync(result.Proposal, result.Outcome.Kind == OwnerOutcomeKind.UnrecordedRefusal ? $"resend_refused:{result.Outcome.Code}" : "second_uncertainty", ct);
    }

    /// <summary>Devralma ihtiyacı AI'nın KENDİ satırına + ai.Outbox'a aynı save'de yazılır; Work görevini köprü ve uzlaştırma açar/kapatır (§10.4a).
    /// Koordinatör Work'e doğrudan çağrı YAPMAZ: "öneriyi kaydet, sonra görevi kapat" gibi iki ardışık yazma yoktur.</summary>
    private async Task RequireTakeoverAsync(AiProposal proposal, string reason, CancellationToken ct)
    { if (proposal.RequireTakeover(reason)) await proposals.SaveAsync(proposal, ct); }
}
```

Sözleşme (ADR-008 madde 6'ya öneri; v1.3 numaralandırması):

1. Kurtarma yalnız aynı `OperationIntentId` ve aynı doğrulanmış zarf ile yapılır; hiçbir yolda yeni kimlik üretilmez, `Reissue` gönderilmiş niyet için reddedilir (§5.1).
2. **Sonucun kesinliği gönderim sırasından değil, sahibin kaydından ve tek-gönderim kanıtından çıkar (R-01).** Kayıtlı sonuç (başarı/replay, kayıtlı terminal ret, key fence, key'de başka içerik) her denemede niyetin sonucudur. Kayıtsız ret (`403`, `*_concurrent_change`, geçici doğrulama) yalnız "bu key ile tek gönderim çıktı ve satır o gönderimden beri değişmedi" durumunda — save'in rowversion kontrolüyle — kesindir; aksi halde yalnız deneme reddidir: `Unknown` + kilit + devralma.
3. "Sahip modülde kayıt yok" hiç ulaşmadı anlamına gelmez; yeniden gönderim **en fazla bir** kez (kod önkoşulu + `CK_Proposals_Recovery` + `CK_Proposals_Sends` + rowversion) ve yalnız `SendsIssued=2` commit edildikten sonra.
4. Yeniden gönderim, gateway'in **güncel** yürütme koşullarını (kill switch, araç bayrağı, capability, onaylayanın izni, sahip onay politikası, politika sürümü) ve azami yeniden gönderim süresini (`Ai:Recovery:MaxResendAge`) sağlamadıkça yapılmaz; sağlamaması niyeti **başarısız yapmaz**.
5. Sonuç okuma (`FindAsync`) ve anahtar kilidi (`FenceAsync`) sahibin tek salt-okunur/terminal port'udur (`IIdempotencyOutcomeV1`); **WRITE beyaz listesine giren her sahip komut için Faz 4 ön koşulu** önerilir (K-AÇIK-15, NS-AI-19). Port'u olmayan komutta `Unknown` bir öneri **insan tarafından `Failed` kapatılamaz** (etkisizlik kanıtlanamaz); yalnız sahip okumasıyla `Executed`'a bağlanabilir veya kilitli bekler — bu yüzden port'suz komut beyaz listeye alınmaz.
6. Onaylanmış ama gönderilmemiş öneri (`Approved` + `DispatchRequestedAtUtc`) unutulmaz: süpürücü onu **tam gönderim yolundan** (`DispatchAsync`: durum, süre, kill switch, politika, zarf) geçirir; HTTP isteğiyle yarışta `MarkDispatched` tek kazananı seçer. Yeni yürütme kapalıysa gönderim yapılmaz; süre dolunca `Stale` + bildirim.
7. Geç gelen **başarı** önceki çıkarımı ne olursa olsun kazanır (`ApplyOwnerOutcome`: `Failed → Executed`, gönderim öncesi reddedilenler hariç) ve **inceleme ihtiyacı** (`ReviewState=Required`) + security-event üretir.
8. İnsan devralma (`POST /ai/proposals/{id}/resolution`) istemciden kanıt metni **almaz**: sunucu sahip modülden okur (`verify`) veya key'i kilitler (`fence`) ve dönen doğrulanmış cevabı `ResolveByHuman`'a verir. "Şu anda kayıt görünmüyor" `Failed` için yetmez.
9. Kilitli `Failed(ContentConflict)` ve düzeltilmiş sonuç **inceleme durumu** taşır (`ReviewState`); `POST /ai/proposals/{id}/review-resolution` (`ai.recovery.review`, K3) sahibin doğrulanmış okumasıyla `LinkOwnerRecord` (sonuç `Executed`) veya `ConfirmAndRelease` (hedef serbest) uygular. Bu yol bütün `Failed` kayıtlarını açmaz.
10. Koordinatör öneri başına yalıtılmıştır: bir önerideki eşzamanlılık kaybı veya beklenmeyen hata o öneriyi bir sonraki taramaya bırakır; diğer önerilerin kurtarılmasını durdurmaz.
11. Öneri durumu ile devralma görevinin açılıp kapanması **iki ardışık yazma değildir**: durum ve olgusu aynı save'dedir, Work tarafı olgudan ve uzlaştırmadan yürür (§10.4a).
12. `Unknown`, `Dispatched` veya `Approved` durumundaki, `TargetLock` taşıyan ya da `ReviewState=Required` önerinin içerik deposundaki zarfı **saklama süresi dolsa da silinmez** (yasal bekletme); süre, öneri çözülüp devralma/inceleme kapanınca başlar (§4.5).

### 10.4a Öneri sonucu ↔ devralma görevi: kesintiye dayanıklı bağ ve AI bayraklarından bağımsız kurtarma (v1.3, R-03)

**Karşı örnek (v1.2):** probe başarıyı bulur, öneri `Executed` kaydedilir, Work görevi kapatılmadan süreç çöker; sonraki tarama `Executed` öneriyi seçmediği için görev açık kalır. Bu, Sorun–görev boşluğunun kurtarma kapanışındaki karşılığıdır. **Düzeltme:** AI modülü Work tablosuna yazamaz (AGENTS.md), bu yüzden bağ tek işlemle değil **olgu + tüketici + uzlaştırma** ile kurulur:

| Adım | Nerede | Kesinti olursa |
|---|---|---|
| 1. Öneri durum geçişi + olgu (`ai.proposal.takeover-required.v1` / `review-required.v1` / `resolved.v1`) | `AiProposalStore.SaveAsync`: `ai.Proposals` + audit + `ai.Outbox` **tek save** (`PendingFacts`) | İkisi birlikte yazılır ya da hiçbiri; "öneri çözüldü, olgu yok" durumu oluşamaz |
| 2. Olgu → Work komutu | `AiWorkBridge` (host, `GoymenERP.Api/AiRuntime/Recovery`; Ai.Contracts + Work.Contracts'a bağlı, iki modül birbirine bağlı değil). **İki ayrı görev** (v1.3 iç kontrol): devralma `ai-recovery:{ProposalId}`, inceleme `ai-review:{ProposalId}`. `takeover-required` → `EnsureSystemItemAsync(ai-recovery:…)`; `resolved` → yalnız `ai-recovery:…`'yi kanıtla kapatır; `review-required` → `EnsureSystemItemAsync(ai-review:…)`; `review-resolved` → yalnız `ai-review:…`'yi kapatır. Aynı save'de hem `resolved` hem `review-required` doğarsa (geç başarı düzeltmesi) devralma kapanır, inceleme açılır. Work tarafı key `ai-fact:{MessageId}` ile `work.Attempts`'ta tekildir | Olgu kalıcıdır; köprü yeniden başlayınca kaldığı yerden okur; aynı olgu iki kez işlenirse replay (ikinci görev/kapanış yok) |
| 3. Uzlaştırma (garanti) | `AiTakeoverReconciler` (aynı klasör, zamanlı): (i) `IAiProposalRecoveryReadsV1.ListNeedingTakeoverAsync` (`Status=Unknown AND TakeoverRequired=1`) ve `ListNeedingReviewAsync` (`ReviewState=Required`) → açık görevi olmayanlar için `Ensure`; (ii) `IWorkItemReadsV1.ListOpenBySourceAsync(SourceKind.AiRecovery)` → `ai-recovery` görevi öneri artık `Unknown` değilse, `ai-review` görevi `ReviewState≠Required` ise kanıtla `Close`. `TakeoverRequired` her terminal geçişte temizlenir (şema kısıtı `Status=8` ister); görev açılıp kapanma döngüsüne girmez | ERP-000F outbox dağıtıcısı gelene kadar **zorunlu garanti yolu**; sonra emniyet ağı (§9.4 ile aynı ilke) |

**AI bayraklarından bağımsız kurtarma:** Kurtarma üç çalıştırıcıdan biridir: `AiRecoveryWorker` (§14.1). `Ai:Enabled=false`, `Ai:KillSwitch=true` veya `Ai:Workflow:Enabled=false` **yeni** model çağrılarını, yeni önizlemeleri, gönderimleri ve yeniden gönderimleri durdurur; önceden gönderilmiş işlemlerin **sonuç okuması, devralma/inceleme görevlerinin açılıp kapanması ve insan çözüm endpoint'leri** (`/resolution`, `/review-resolution`) çalışmaya devam eder (`ai_disabled`/`ai_kill_switch` 503'ü bu endpoint'lere uygulanmaz). `Ai:Recovery:Enabled` yalnız bakım için kapatılabilir; çözülmemiş öneri varken kapalıysa readiness uyarısı ve security-event üretilir.
### 10.5 Kaydedilen senaryolar (test sözleşmesi, §18.2)

| Senaryo | Beklenen (şema + metin aynı davranışı tarif eder) |
|---|---|
| **Aynı içerikte iki meşru sipariş:** kullanıcı iki kez "sipariş taslağı oluştur" niyeti başlatır; içerik birebir aynı | İki `OperationIntentId`, iki key, iki `sales.DraftAttempts` satırı, iki taslak; ikinci önizlemede "aynı içerikli 1 öneri az önce yürütüldü" bilgisi; engel yok; sahip modül mükerrer uyarısı varsa bulgu olarak gösterilir |
| **Belirsiz sonucun tekrar gönderilmesi:** dispatch transport hatası; sahip modül aslında kaydetmiş | Proposal `Unknown`; koordinatör aynı key/aynı payload ile bir kez gönderir → sahip `IsReplay=true` + ilk correlation → `Executed`; ikinci kayıt yok; audit'te ikinci başarılı olay yok |
| **Belirsiz sonucun tekrar gönderilmesi (ulaşmamış):** sahip modülde kayıt yok | Aynı gönderim ilk kez yürütür → `Executed`; toplam tek kayıt |
| **İkinci belirsizlik** | `Unknown` kalır; `TargetLock`; `work.WorkItems` "kurtarma devralma" açık; aynı hedef için yeni niyet `intent_pending_recovery` (409) |
| **Gönderimde sahip `version_conflict`** (onaydan sonra hedef değişti) | (v1.3) Sahip reddi key'e terminal **kaydediyorsa** (Purchasing/Accounting/Manufacturing, Ek K-2) `RecordedRefusal` → `Failed(version_conflict)` her durumda kesin; kaydetmiyorsa `UnrecordedRefusal` → yalnız `SendsIssued=1` ve satır değişmemişse `Failed`, aksi halde `Unknown` (deneme reddi). `Failed`'da etki yok, kilit yok; aynı niyet yeniden önizlenemez; kullanıcı **yeni niyetle** yeniden önizler. Onaydan **önce** fark edilen sürüm değişikliği `Stale` → aynı niyet rev+1 |
| **İki onay gerektiren işlem** (sahip politikası `RequiredApprovals=2`) | 1. onay → `PartiallyApproved` (kanıt satırı); aynı kişi 2. onay → `403`; farklı kişi → `Approved`; sahip politikası kişi ayrılığı istiyorsa isteyenin onayı `403`; iki onay arasında hedef sürümü değişti → `Stale`, iki onay da düşer; `Reissue` aynı niyet rev+1 |
| **Yalnız bir sevkiyata verilen istisna** | Müşterinin risk Sorunu **açık kalır** (`Acknowledged`, istisna referansıyla; limit aşımı sürdükçe `Resolved` olmaz); kapı `Proceed(exceptionRef)` yalnız o talimat için; istisna değerlendirme görevi kanıtla kapanır; sevkiyat kaydedilince istisna `Consumed` → aynı müşteriye ikinci talimat `ReviewRequired`, Sorun `Acknowledged → Open` ve yeni karar görevi (`Reopened` yalnız `Resolved`'dan döner); süre dolunca aynısı zamanlı taramayla; yeni Sorun açılmaz |
| **Aynı niyet, farklı içerik (bütünlük ihlali)** | Sahip `409 same_key_payload_mismatch`/`idempotency_conflict` → `Failed(ContentConflict)` + security-event; `TargetLock` kalır; (v1.3) `ReviewState=Required` + `ai.proposal.review-required.v1` → inceleme görevi; `review-resolution` sahibin doğrulanmış okumasıyla `LinkOwnerRecord` (→ `Executed`) veya `ConfirmAndRelease` (kilit kalkar) |
| **Sorun kaydı ile görev arasında kesinti** | Olamaz: ikisi aynı işlemdedir; kesinti ikisini birden geri alır; sonraki değerlendirme ikisini birlikte yazar. "Açık sorun, görev yok" durumu (eski veri) ilk değerlendirmede tamamlanır (§9.1 değişmezi) |
| **Kapı sorgusu** | Hiçbir satır yazmaz; kalıcı takip ayrı `POST /rules/evaluations` komutuyla (§9.3–9.4) |
| **Gölge öneri yeniden üretilir** | Yeni revizyon da `ShadowOnly`; eski revizyon `Superseded`; hiçbiri onaylanamaz |
| **Gönderim öncesi zarf bozuk** | `FailBeforeDispatch`: `Approved → Failed`, sahip çağrılmaz, kilit yok, security-event; yeniden deneme yeni niyet |
| **Kurtarmada zarf bozuk** | Gönderim yapılmaz; `Unknown` + kilit kalır; security-event; insan devralma görevi (ilk gönderim yürümüş olabilir, "Failed" denmez) |
| **(v1.2) İlk gönderim başarılı, kurtarma denemesi reddedildi** — iki yol: (i) gateway politikası onaylayanın yetkisinin düştüğünü önceden görür → `EvaluateResendAsync` reddeder, yeniden gönderim **yapılmaz** (`resend_blocked:*`); (ii) yetki/kapsam `EvaluateResendAsync`'ten sonra veya yalnız sahip tarafında (ör. saha/lokasyon kapsamı) değişmiştir → sahip fence'i replay aramasından önce 403 döner | İki yolda da ret **denemeye** aittir: (ii)'de `ApplyOwnerOutcome(UnrecordedRefusal)` + `LastAttemptRefusalCode` (`SendsIssued=2` olduğu için kesin sayılmaz); her ikisinde `Unknown` + `TargetLock` kalır, devralma olgusu → görev; `Failed` denmez. Sonra probe başarıyı okursa `Executed`, insan sahip okumasıyla çözerse `ResolveByHuman`; ikisinde de `resolved` olgusu görevi kanıtla kapatır; ilk sipariş tek kalır |
| **(v1.2) Yeniden gönderim koşulu artık sağlanmıyor** (capability kapalı, kill switch, azami süre `Ai:Recovery:MaxResendAge` geçti) | Gönderim yapılmaz; `Unknown` + kilit; yalnız probe ve insan devralma; `Failed` yok |
| **(v1.2) Geç başarı `Failed` üzerine** (ilk gönderimde transport hatası sanılan istek aslında sahipte kaydedilmiş; öneri başka yoldan `Failed` işaretlenmişti) | `ApplyOwnerOutcome(Executed)` `Failed → Executed` düzeltmesi (`OutcomeCorrected=true`, `ReviewState=Required`); security-event + inceleme görevi (olgu); gönderim öncesi reddedilmiş (`FailureBasis=PreDispatch`) öneriye uygulanmaz |
| **(v1.2) Onay kalıcılaştı, gönderim başlamadan süreç çöktü** | `Approve` ile `DispatchRequestedAtUtc` aynı save'de; süpürücü (worker başlangıcında ve zamanlı) `Approved` ve `DispatchRequestedAtUtc` dolu satırı **tam `DispatchAsync` yolundan** (durum/süre/politika/zarf) geçirir; önizleme süresi (`ExpiresAtUtc`) içinde süpürülürse kullanıcı yeniden onaylamaz; ikinci gönderim `MarkDispatched` eşzamanlılık koruması ve sahip PK ile engellenir. **Bilinçli istisna:** süpürme süre dolduktan sonra olursa süresi geçmiş onay gönderilmez → `Stale`, isteyene "onay süresi doldu, yeniden önizleyin" bildirimi; hiçbir şey gönderilmediği için belirsizlik yok |
| **(v1.2) Öneri oluşturma isteği tekrarlandı** (yanıt kayboldu, istemci aynı `Idempotency-Key` ile yeniden gönderdi) | `ai.CommandAttempts` (CompanyId, CommandName, IdempotencyKeyHash) öneriyle aynı save'de; aynı aktör + aynı içerik → aynı `ProposalId`/`OperationIntentId` replay; farklı içerik veya aktör → 409 `same_key_payload_mismatch`; ikinci niyet oluşmaz |
| **(v1.2) A istisnası aktifken B talimatı** | A için `Instruction(A)` kapsamlı karar B'yi karşılamaz: B için `DedupKey = IssueKey + Instruction(B)` ile ayrı Finans karar görevi açılır; A'nın görevi kapalı kalır; müşteri Sorunu `Acknowledged → Open` (`SyncDecisionState`: B ihtiyacı açık; A kararı geçerli kalır); B kapısı `ReviewRequired`; B kararı gelince B görevi kapanır ve Sorun yeniden `Acknowledged` |
| **(v1.2) Karar yenilendi** (Finance aynı kapsam için süreyi uzattı) | `SyncDecisionState`: yenileme ≠ geçersizleşme; yeni görev açılmaz, açık görev yoksa açılmaz; yalnız `Revoked`/`Expired`/`Consumed` ihtiyacı yeniden açar |
| **(v1.2) Kaynak kaydedildi, takip hiç başlatılmadı** (ekranın ikinci çağrısı yapılamadı) | Outbox olayı (birincil) veya `work.rules.reconciliation` taraması (outbox kabulüne kadar zorunlu) değerlendirmeyi başlatır; ekran çağrısı yalnız hızlandırıcıdır; aynı değerlendirme iki kez gelirse dedup |
| **(v1.2) Kural gölgeye alındı, gerçek görev açık** | Gölge değerlendirme yalnız `work.ShadowEvaluations` yazar; gerçek Sorun/görev kapanmaz veya açılmaz |
| **(v1.2) AI tamamen kapalı** (`Ai:*` bayrakları kapalı, sağlayıcı yok) | `WorkRulesWorker` (`Work:Scheduler:Enabled`) zamanlı takip ve eskalasyonu sürdürür; `AiWorkflowWorker` durur; (v1.3) `AiRecoveryWorker` önceden gönderilmiş önerilerin sonuç okumasını ve devralma/inceleme görevlerini sürdürür, yeni gönderim/yeniden gönderim yapmaz |
| **(v1.3, R-01) İlk gönderim ile kurtarma örtüşür; kurtarma kaydeder, ilk gönderimin 403'ü sonra gelir** | İlk gönderimin save'i rowversion çakışmasıyla düşer; taze satıra `afterConcurrentChange: true` → yalnız `LastAttemptRefusalCode`; öneri `Unknown` + kilitli kalır; probe → `Executed`; öneri hiçbir anda serbest bırakılmaz (G-51) |
| **(v1.3, R-01) İlk gönderimin 403'ü, kurtarma satırı değiştirmeden önce gelir** | Satırda `SendsIssued=1`, rowversion değişmemiş → `Failed(SingleSendUnrecordedRefusal)`, kilit kalkar; koordinatörün eski kopyası çakışır, yeniden gönderim hiç çıkmaz |
| **(v1.3, R-02) Probe key'de kayıt bulur ama içerik farklı** | `SameKeyMismatch` → `Failed(ContentConflict)` + kilit + inceleme; "key bulundu" tek başına başarı sayılmaz (G-52) |
| **(v1.3, R-02) İnsan "başarısız" kapatmak ister; sahipte kayıt görünmüyor** | `verify` → `NotFound` → çözüm reddedilir (`resolution_evidence_invalid`); `fence` → sahip key'e "kapatıldı" yazar → `Failed(OwnerKeyFence)`; ardından gelen eski istek sahipte "kapatıldı" replay'i alır, yürümez (G-52) |
| **(v1.3, R-03) Öneri `Executed` kaydedildi, görev kapatılmadan süreç çöktü** | Olgu öneriyle aynı save'de; köprü veya uzlaştırma yeniden başlatmada görevi kanıtla kapatır; aynı olgu iki kez → tek kapanış (G-53) |
| **(v1.3, R-03) Araç emekliye ayrıldı / kill switch açık; `Unknown` öneri var** | Probe sahip sözleşmesi okuyucusuyla çalışır (aktif katalog gerekmez); yeniden gönderim yapılmaz (`tool_not_active` / `resend_blocked`); devralma görevi açılır (G-53) |

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

## 12. Ölçü referansı, çıktı doğrulayıcı, güvenli renderer (v1.2; bağlam doğrulaması v1.3)

Tutarı regex ile **yakalamaya çalışmak** kapanış değildir (`5580000 TRY`, `₺5.580.000`, yazıyla sayı gibi biçimler kaçar). Bu yüzden kural tersine çevrilir: **model metninde rakam yasaktır**; kullanıcıya görünen her sayı, tarih ve kayıt numarası sunucunun doğruladığı yapılandırılmış referanslardan üretilir. Model çıktısı güvenilir kabul edilmez ve sonraki bileşene aktarılmadan doğrulanır (OWASP LLM Top 10 — güvensiz çıktı işleme).

| Kural | Uygulama |
|---|---|
| **Kapsam: bütün görünür metinler** | `AnswerEnvelope.UserVisibleParts()` — `summary[]` (cümle + `about`), `findings[].text`, `recommendedActions[].label`, `uncertainty.reasons[]`, `abstainReason`, taslakların konu/gövdesi. Hiçbir alan döngü dışında kalmaz; yeni alan eklenince şema testi bu listeyi zorlar |
| **Rakam yasağı** | Görünür metinde `\p{Nd}` sınıfından herhangi bir rakam (Arap, Doğu Arap vb. dahil) yalnız token içinde olabilir: `{{measure:mN}}` (ölçü), `{{ref:rN}}` (belge/kayıt no), `{{date:dN}}` (tarih). Token dışı her rakam → `Reject`. Yazıyla tutar ("beş milyon") için Türkçe sayı sözlüğü kontrolü → `Reject` |
| **Parça bağlamı (v1.3, R-07)** | Her görünür parçanın bir **bağlamı** (`BindingContext`: hakkında konuşulan kayıt kümesi) vardır ve sunucu belirler: bulgu metni → bulgunun hedefi; **özet** → model şemasında özet cümle listesidir, her cümle `about: [refId…]` taşır (bağlamda tek odak kayıt varsa varsayılan odaktır, birden fazlaysa `about` zorunlu); **eylem etiketi** → eylemin hedefi; **taslak** → paragraf başına `about`; belirsizlik/çekimserlik gerekçeleri → run odağı. **Tek özne kuralı (v1.3 iç kontrol):** ölçü, tarih veya yargı referansı taşıyan parçanın bağlamı **tek kayıttır** (sunucunun beyan ettiği ilişkiyle bağlı alt kayıtlar hariç); birden fazla özneli cümle yalnız `{{ref:}}` taşıyabilir. Böylece `about:[X,Y]` ile "{{ref:rX}} riski {{measure:m2}}" (m2 Y'nin) geçemez |
| **Token bağlama (v1.3 genişletildi)** | Her token araç çıktısında vardır **ve** parçanın bağlamına bağlıdır: `measure` → ölçünün `subject` (ve `period/scenarioRef`) bağlamdaki bir kayda eşit veya sunucunun beyan ettiği ilişkiyle (müşteri ↔ talimat) bağlı; `ref` → kaydın kendisi bağlamda; `date` → tarihin sahibi kayıt bağlamda. Kontrol **bulgu olsun olmasın** her parçada çalışır (G-32, G-58) |
| **Kayıt adları yalnız token ile (v1.3)** | Bağlamdaki ve araç çıktısındaki kayıtların görünen adları (müşteri unvanı, ürün adı, belge no) model metninde **serbest yazılamaz**; yalnız `{{ref:rN}}` ile gelir ve renderer yazar. Araç çıktısındaki bilinen bir adın token dışında geçmesi → `Reject`. Böylece "X müşterisinin riski {{measure:m2}}" ya "X" serbest yazıldığı için ya da `{{ref:rX}}` ile `m2`'nin öznesi (Y) eşleşmediği için reddedilir |
| **Yargı** | Model yargı üretmez. Uyarı ve üstü her bulgu, araç çıktısında bulunan bir `judgementRef` taşır (`GateDecision` veya `EngineResult` kimliği); **kritik yargı cümlesini sunucu yapılandırılmış referanstan şablonla kurar** ve önce o gösterilir, model metni yalnız açıklamadır. (v1.3) `judgementRef`'in öznesi parçanın bağlamında olmalıdır; özet cümlesi ve eylem etiketi de yargı ifadesi içeriyorsa aynı kurala tabidir. `judgementRef` yoksa, bağlam dışındaysa veya açıklamadaki yargı ifadesi referansın kararıyla çelişirse → `Reject` |
| **Sınır (dürüst beyan, v1.3)** | Türkçe sayı ve yargı sözlükleri **ek savunmadır**; serbest metnin bütün anlamsal çelişkilerini kapatan garanti değildir. Garanti edilen: görünür her sayı/tarih/kayıt no/kayıt adı sunucunun doğruladığı ve **doğru kayda bağlı** bir referanstan gelir; kritik yargı sunucudan gelir. Tokensız serbest değerlendirme ("bu müşteri güvenilmez") kanıtlanamaz: "AI açıklaması" etiketiyle gösterilir, hiçbir komuta, kapı kararına veya ölçüye girdi olmaz (OWASP sıfır güven: model çıktısı iş sonucu değildir) |
| **Açıklık düzeyi** | `GateDecision` düzeyinde ölçü tokenı yok ve metin yalnız şablondan (G-01) |

```csharp
internal static partial class AiTextTokens                         // doğrulayıcı ve renderer AYNI sözdizimini paylaşır
{
    [GeneratedRegex(@"\{\{(?<kind>measure|ref|date):(?<id>[mrd]\d{1,4})\}\}", RegexOptions.CultureInvariant)] internal static partial Regex Token();
    [GeneratedRegex(@"\p{Nd}", RegexOptions.CultureInvariant)] internal static partial Regex AnyDigit();
}

internal sealed class AiOutputValidator(IAiDisclosureMatrix disclosure, ITurkishNumberWords numberWords, IJudgementLexicon lexicon) : IAiOutputValidator
{
    public AiValidationResult Validate(AnswerEnvelope answer, AiToolOutputs outputs, AiToolContext context, DisclosureLevel level)
    {
        var problems = new List<AiValidationProblem>();
        var knownNames = outputs.EntityDisplayNames();                                     // araç çıktısındaki kayıtların görünen adları (müşteri, ürün, belge no …)
        foreach (var part in answer.UserVisibleParts(outputs, context))                    // BÜTÜN görünür parçalar: özet cümleleri, bulgular, eylem etiketleri, taslaklar, gerekçeler
        {                                                                                   // part = (Path, Text, BindingContext, JudgementRef?, Severity?) — bağlamı SUNUCU kurar (§12 tablo)
            if (part.Context.IsMissing) { problems.Add(AiValidationProblem.MissingContext(part.Path)); continue; }   // çok kayıtlı özet cümlesinde about yok → ret
            if (part.Context.SubjectCount > 1 && (AiTextTokens.HasMeasureOrDate(part.Text) || part.JudgementRef is not null || lexicon.ContainsJudgement(part.Text)))
                problems.Add(AiValidationProblem.MultiSubjectMeasure(part.Path));                // tek özne kuralı: ölçü/tarih/yargı yalnız tek kayıtlı parçada
            var withoutTokens = AiTextTokens.Token().Replace(part.Text, string.Empty);
            if (AiTextTokens.AnyDigit().IsMatch(withoutTokens) || numberWords.ContainsAmount(withoutTokens))
                problems.Add(AiValidationProblem.RawNumber(part.Path));                        // biçimi ne olursa olsun: token dışı sayı → ret (G-31, G-49)
            if (knownNames.FindUntokenized(withoutTokens) is { } name)
                problems.Add(AiValidationProblem.UntokenizedEntityName(part.Path, name));      // kayıt adı yalnız {{ref:}} ile (G-58)
            foreach (Match m in AiTextTokens.Token().Matches(part.Text))
            {
                if (!outputs.TryResolve(m.Groups["kind"].Value, m.Groups["id"].Value, out var value)) { problems.Add(AiValidationProblem.UnknownToken(part.Path, m.Value)); continue; }
                if (!part.Context.Binds(value)) problems.Add(AiValidationProblem.TokenBindingMismatch(part.Path, m.Value));   // measure/ref/date → bağlamdaki kayıt (G-32, G-58)
            }
            var claimsJudgement = lexicon.ContainsJudgement(part.Text);
            if (part.Severity >= Severity.Low || claimsJudgement)                              // bulgu olmasa da (özet/etiket) yargı ifadesi → referans şart
            {
                if (part.JudgementRef is null || !outputs.TryGetJudgement(part.JudgementRef, out var judgement)) problems.Add(AiValidationProblem.MissingJudgementRef(part.Path));
                else if (!part.Context.Binds(judgement.Subject)) problems.Add(AiValidationProblem.JudgementOutOfContext(part.Path));
                else if (lexicon.Contradicts(part.Text, judgement)) problems.Add(AiValidationProblem.JudgementContradiction(part.Path));   // ek savunma; garanti değil (§12 "Sınır")
            }
            if (level == DisclosureLevel.GateDecision && (AiTextTokens.Token().IsMatch(part.Text) || !disclosure.IsTemplateOnly(part.Text)))
                problems.Add(AiValidationProblem.DisclosureBreach(part.Path));                 // sayı içermeyen finans cümlesi de sızıntıdır (G-01)
        }
        if (!AiMeasureDictionary.Known(outputs.MeasureNames)) problems.Add(AiValidationProblem.UnknownMeasureName("measures"));   // G-14
        return problems.Count > 0 ? AiValidationResult.Rejected(problems) : AiValidationResult.Accepted();
    }
}

internal sealed class AiMeasureRenderer(IAiNumberFormatter format) : IAiMeasureRenderer   // ölçü + kayıt no + tarih tokenlarını yerleştirir
{
    // {{measure:m1}} → "5,58 MTL" (tr-TR; birim ölçü sözlüğünden) · {{ref:r1}} → yetkili kayıt no · {{date:d1}} → gg.aa.yyyy
    public string Render(string text, AiToolOutputs outputs, DisclosureLevel level)
        => AiTextTokens.Token().Replace(text, m => level == DisclosureLevel.Full || m.Groups["kind"].Value != "measure"
            ? format.Format(outputs.Resolve(m.Groups["kind"].Value, m.Groups["id"].Value)) : string.Empty);
}
```

- Reddedilen çıktı kullanıcıya gösterilmez; aynı run içinde **bir** kez yapılandırılmış düzeltme istemiyle yeniden istenir, yine reddedilirse deterministik şablon metni gösterilir ve run `Partial` işaretlenir.
- Güvenli renderer: çıktı düz metin + sınırlı markdown (kalın, liste); HTML, script, event handler, `javascript:`/`data:` URL, dış görsel yok; frontend `dangerouslySetInnerHTML` **kullanmaz** (`ERP-000J` §13); linkler yalnız ERP iç rotaları (`sourceRef` → `/satis/siparisler/{id}`).

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

### 14.1 Bileşenler: üç ayrı çalıştırıcı (v1.2; kurtarma v1.3'te ayrıldı)

Takibin AI'dan bağımsız çalışması yalnız "Work, AI'ya referans vermez" demekle kanıtlanmaz; zamanlı takip, eskalasyon ve uzlaştırmayı yürüten çalıştırıcının da AI'dan bağımsız olması gerekir. (v1.3, R-03) Aynı ilke önceden gönderilmiş AI işlemlerinin kurtarılmasına da uygulanır: yeni model çağrıları ve yeni gönderimler durabilir, ama sonuç okuma ve insan devralması erişilemez hale gelmemelidir. Bu yüzden üç çalıştırıcı vardır:

```text
WorkRulesWorker : BackgroundService                 # Work modülünün KENDİ çalıştırıcısı — GoymenERP.Api/RulesRuntime/Scheduling altında kayıtlı;
                                                    # Work:Scheduler:Enabled ile açılır; Ai:* bayraklarına (Enabled, KillSwitch, Workflow, Recovery) BAĞLI DEĞİL
  ├── RuleScheduleSweep     : zamanlı kurallar (vade, termin, istisna süresi) — DURUM sorgusu, keyset; → IRuleScopeFanOut (kapsam dağıtımı, §9.1a) → IImpactRuleTracker
  ├── ReconciliationSweep   : takibi başlatılmamış kaynakları yakalar (§9.4); outbox gelene kadar zorunlu, sonra emniyet ağı; → IRuleScopeFanOut
  ├── EscalationTicker      : work.WorkItems DueAtUtc geçmiş → work.item.escalate.v1 (politika zinciri; yalnız açık öğe)
  └── RulesInboxConsumer    : ERP-000F olayları → work.Attempts dedup → IRuleScopeFanOut / IImpactRuleTracker
  Kiralama: work.SchedulerLeases (şirket × görev × zaman dilimi; rowversion; Cursor) — çoklu instance'ta çift çalışma yok

AiRecoveryWorker : BackgroundService                # (v1.3) önceden gönderilmiş işlemler — GoymenERP.Api/AiRuntime/Recovery; Ai:Recovery:Enabled (varsayılan true);
                                                    # Ai:Enabled / Ai:KillSwitch / Ai:Workflow:Enabled'a BAĞLI DEĞİL (yalnız yeni yürütmeyi politika kapatır)
  ├── RecoverySweeper       : IAiRecoveryCoordinator.RecoverAsync — Approved (gönderilmemiş; yeni yürütme kapalıysa göndermez), Dispatched (süre aşımı), Unknown (§10.4)
  ├── AiWorkBridge          : ai.Outbox olguları → Work devralma/inceleme görevi aç/kapat (work.Attempts ile tekil; §10.4a)
  └── AiTakeoverReconciler  : öneri ↔ görev uzlaştırması (açık görevi olmayan devralma ihtiyacı / çözülmüş önerinin açık görevi)
  Kiralama: ai.Jobs (JobType "recovery.sweep", "recovery.reconcile"; DedupKey = slot)

AiWorkflowWorker : BackgroundService                # AI'ya özgü YENİ işler — Ai:Workflow:Enabled ve !OpenApiDocumentGenerationMode.IsActive iken
  ├── JobPoller        : ai.Jobs → lease → çalıştır (model destekli şablonlar, kullanıcı hedef işleri, brifing)
  ├── SchedulePlanner  : AI şablon zamanları → ai.Jobs (DedupKey = schedule:{key}:{slot})
  ├── InboxConsumer    : ERP-000F → ai.Inbox dedup → ai.Jobs (yalnız AI şablonlarının tetikleyicileri)
  └── WorkflowRunner   : WorkflowInstance durum makinesi; modelsiz adımlarda Work'ün tracker'ını ÇAĞIRIR, görev YAZMAZ
```

**Kabul koşulları (bayraklardan bağımsızlık):** (1) `Ai:Enabled=false`, `Ai:KillSwitch=true`, `Ai:Workflow:Enabled=false` iken zamanlı kural taraması, uzlaştırma, eskalasyon ve olay tüketimi çalışır; görevler açılır, eskalasyon olur, koşul kalkınca kapanır (`WorkRulesWorkerRunsWithAiFullyDisabled`, §18.2). (2) (v1.3) Aynı bayraklarla, önceden `Unknown` kalmış önerinin probe'u çalışır, sonuç kaydedilir, devralma görevi açılır/kapanır, `/resolution` ve `/review-resolution` cevap verir; **hiçbir** gönderim veya yeniden gönderim çıkmaz (`AiRecoveryRunsWithNewAiExecutionDisabled`, §18.2). (3) Kill switch açıkken `Approved` öneri süpürülürse hiçbir şey yazılmaz, öneri `Failed` olmaz; süre dolunca `Stale` + bildirim. AI kapalıyken yalnız model açıklaması, taslak, yeni önizleme/gönderim ve AI şablon işleri durur.

Repo'da bugün `BackgroundService` yoktur; tek `IHostedService` readiness probe'dur (Ek K-1 madde 12) — üç worker da **yeni** çalışma biçimidir ve ADR-008 madde 11 ile kabul edilir. İlk sürümde aynı process'te koşarlar; ayrı host'a alınabilirler (Master v3 §13.1); durum her zaman `work.*` / `ai.*` tablolarındadır. Kalıcılık hedefi: sunucu kapalıyken işleme durur, kayıt kaybolmaz; açılınca kiralama süresi dolan işler kaldığı yerden alınır.

### 14.2 `WorkflowInstance` durum makinesi ve şablon

```csharp
public enum WorkflowState { Pending = 1, Running = 2, WaitingHuman = 3, WaitingApproval = 4, WaitingData = 5, Escalated = 6, Done = 7, Failed = 8, Unknown = 9 }

public interface IWorkflowDefinition
{
    string Id { get; } int Version { get; }
    IReadOnlyList<WorkflowStep> Steps { get; }      // her adım: Kind (ToolRead | Track | ModelExplain | Draft | WaitFor | Brief), bütçe, bekleme koşulu — OpenWorkItem adımı YOK
}

// Örnek: geciken termin takibi (modelsiz çekirdek + opsiyonel model açıklaması)
internal sealed class OverduePromiseFollowUpWorkflow : IWorkflowDefinition
{
    public string Id => "wf.purchasing.overdue-promise-follow-up"; public int Version => 1;
    public IReadOnlyList<WorkflowStep> Steps { get; } =
    [
        WorkflowStep.ToolRead("purchasing.orders.list@1", args: "status=open&promiseBefore=today", cardinality: 200, paging: "keyset"),   // DURUM sorgusu (güncellenme zamanı DEĞİL): hiç güncellenmemiş ama bugün geciken sipariş de girer; bütçe aşımı → Partial, sonraki sayfadan devam
        WorkflowStep.Track("IR-PUR-003", perItem: true),                                                                          // Work'ün IImpactRuleTracker'ı: Sorun + karar ihtiyacı + görev TEK işlemde; adım yalnız dönen görev referanslarını saklar
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
| `POST /api/v1/ai/proposals/{id}/approvals` | `ApproveAiProposalV1` | Onay (adım 11) | Zorunlu | 201 `AiApprovalResponse` (`status` ∈ {PartiallyApproved, Approved, Dispatched, Executed, Failed, Unknown, Stale} — onay kaydedildi; gönderim sonucu aynı cevapta: `Failed` (ilk denemede sahip reddi veya gönderim öncesi ret), `Unknown` (sonuç belirsiz, kurtarma bekliyor), `Stale` (gönderim öncesi süre/politika/hedef değişimi)); 409 `version_conflict` / `approval_binding_mismatch` / `proposal_expired`; 403 |
| `POST /api/v1/ai/proposals/{id}/rejection` | `RejectAiProposalV1` | — | Zorunlu | 201 |
| `POST /api/v1/ai/proposals/{id}/resolution` | `ResolveAiProposalV1` (insan devralma; v1.3) | K3 yetki (`ai.recovery.resolve`); `ai_disabled`/kill switch 503'ü uygulanmaz | Zorunlu | Gövde `{ action: "verify" \| "fence" }` — kanıt **istemciden alınmaz**; sunucu sahip port'undan okur/kilitler. 201 `{status, evidence}`; 422 `resolution_evidence_invalid` (`NotFound` hiçbir sonucu kanıtlamaz); 409 `owner_outcome_port_unavailable` |
| `POST /api/v1/ai/proposals/{id}/review-resolution` | `ResolveAiProposalReviewV1` (v1.3) | K3 yetki (`ai.recovery.review`) | Zorunlu | Gövde `{ decision: "LinkOwnerRecord" \| "ConfirmAndRelease" }`; kanıt sunucuda sahip okumasından; yalnız `ReviewState=Required`; 201 / 409 `review_not_required` / 422 |
| `GET /api/v1/ai/center/brief` | `GetAiCenterBriefV1` | READ | — | 200; `coverage.Partial` |
| `GET/POST /api/v1/ai/knowledge…` | `…KnowledgeV1` | READ / öneri | POST zorunlu | — |
| `GET /api/v1/ai/policy/…` | `…PolicyV1` (AI-03) | READ; değişiklik K3 | — | — |
| `GET /api/v1/work/items` (`?queue=` (mine, role, overdue, waiting)) | `ListWorkItemsV1` | READ | — | 200 sayfa (`items, nextCursor`) |
| `GET /api/v1/work/items/{id}` | `GetWorkItemV1` | READ | — | 200 (görünürlük matrisi uygulanmış) |
| `POST /api/v1/work/items` | `CreateWorkItemV1` | WRITE-düşük | Zorunlu | 201 (+Location); 409 |
| `POST /api/v1/work/items/{id}/events` | `AppendWorkItemEventV1` (yorum/atama/kapanış) | yazma | Zorunlu | 201; kapanışta `closureEvidenceRef` zorunlu |
| `POST /api/v1/work/items/{id}/approval-decision` | `DecideApprovalRequestV1` | SoD | Zorunlu | 201; 403 maker=checker |
| `POST /api/v1/rules/gate` | `QueryRuleGateV1` | READ (kapı sorgusu; **hiçbir şey yazmaz**) | — | 200 `GateQueryResponse`; 400 `gate_tool_unknown` / `gate_intent_mismatch`; 403 kodsuz (hedef kapsam dışı) (§9.3) |
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
- `GateDecisionCard`: AI capability kapalıyken kart görünür ve çalışır (G-29). Kapı kartı **sahip modülün kapı iznine** bağlıdır (örn. `finance.risk.gate.read`; master §5.12): izin varsa `work.items.view` olmasa da görünür, izin yoksa görünmez. Görev/etki bulgusu kartı ise `work.items.view` ister. İki test ayrıdır.
- `ProposalPreviewCard`: `unknown` sonucu → yeniden deneme kontrolü **render edilmez**; ikinci onay tıklaması aynı `idempotencyKey` ile gider.
- Erişilebilirlik: klavye/odak, `aria-live` durum rozetleri, hareket azaltma (`ERP-000K` §15).

## 18. Test stratejisi ve CI kapıları

**Statü:** Bu bölümdeki bütün testler **kabul planıdır**; hiçbiri yazılmamış veya koşulmamıştır. Bir satırın burada bulunması, o davranışın uygulanıp geçtiği anlamına gelmez.

### 18.1 Birim ve sözleşme testleri (`GoymenERP.Ai.UnitTests`, `GoymenERP.Work.UnitTests`; xunit 2.9.3)

| Test sınıfı | Kanıtladığı |
|---|---|
| `AiProposalStateMachineTests` | §5.1 durum makinesi; `Reissue` `Dispatched/Unknown/Executed` için fırlatır; `Approve`: kişi ayrılığı **yalnız** `RequiresSeparateApprover` ile, varsayılan politikada isteyen kendi önizlemesini onaylar (G-59), ikinci onaycı ≠ birinci, kota; `MarkDispatched` hash dondurma + `SendsIssued=1`; `SameKeyMismatch` kilidi ve `ReviewState=Required`'ı korur; `ResolveReview` yalnız `Required`'da |
| `OperationIntentIdentityTests` | Üç durum tablosu (§5.1): aynı niyet+aynı içerik → aynı key; aynı niyet+farklı içerik yalnız **gönderim öncesi** `Reissue` ile oluşur (rev+1, aynı key, yeni hash) ve bir niyetin yalnız bir revizyonu gönderilebilir; gönderilmiş/`Failed` niyette `Reissue` reddi; farklı niyet+aynı içerik → farklı key; `Unknown` varken aynı kullanıcı+araç için yeni create niyeti `intent_pending_recovery` |
| `AiCanonicalJsonTests` | Alan sırası/kültür/boşluk farkı aynı hash; anlamlı fark farklı hash; hash küçük harf 64 |
| `AiPreviewEnvelopeTests` | Hash kapsamı zarfın tamamı: payload, sözleşme, araç sürümü, hedef/beklenen sürüm veya politika sürümü değişince hash değişir; model açıklaması zarfta yok; `Matches` Proposal satırıyla uyuşmazlığı yakalar |
| `AiPolicyEvaluatorOrderTests` | 13 adım sırası ve kısa devre; provider `authorized/approved` alanı etkisiz; `permission_unregistered` 503; temsil edilemeyen kapsam fail-closed |
| `AiToolCatalogTests` | Default-deny; bilinmeyen tool/sürüm; `Retired`; çıplak `decimal` çıktı alanı → başlatma reddi; manifest sürümü dondurma |
| `AiOutputValidatorTests` | Bütün görünür **parçalar** denetlenir (özet cümleleri, bulgu, eylem etiketi, belirsizlik gerekçesi, taslak); token dışı her rakam reddedilir: `5580000 TRY`, `₺5.580.000`, Doğu Arap rakamı, yazıyla tutar (G-31, G-49); yanlış varlık/dönem/senaryo tokenı bulgu metninde (G-32) **ve özet ile eylem etiketinde** (G-58: X'in bağlamında Y'nin ölçüsü, kaydı veya tarihi); kayıt adı token dışında → ret; çok kayıtlı özette `about` yok → ret; `judgementRef` olmayan veya bağlam dışı yargı; `GateDecision` düzeyinde sayı içermeyen finans cümlesi (G-01); ölçü sözlüğü dışı ölçü (G-14); `abstained=true` kabul |
| `AiMeasureRendererTests` | `{{measure}}`/`{{ref}}`/`{{date}}` → tr-TR biçim; `GateDecision` düzeyinde ölçü render yok |
| `ImpactRuleTrackerTests` (Work.Rules) | `EffectKind` içinde `Block` yok (reflection); Sorun yaşam döngüsü `Open⇄Acknowledged`, `→Resolved→Reopened` (Reopened yalnız Resolved'dan); `Partial` sonuç durumu değiştirmez; aynı varlık yeni sürüm → yeni Değerlendirme, yeni Sorun yok; kural sürümü değişimi; **değişmez (v1.2):** `NeedsDecisionFor(scope)` doğru ve o kapsam için açık görev yoksa görev sonuç `Unchanged` olsa da oluşur; `Party` kapsamlı karar bütün talimatları karşılar, `Instruction(A)` kararı B'yi karşılamaz (B için ayrı görev, `DedupKey = IssueKey + ScopeKey`); karar yenilemesi görev açmaz; (v1.3) durum hesaptan **önce** yüklenir; `TrackHead` koşul yokken de yazılır; eski hesap (yüklemeden sonra başka değerlendirme kaydetti) çakışır ve yeniden hesaplanır — kapanmış Sorun eski "risk var" hesabıyla açılmaz (G-56) |
| `ImpactRuleGateTests` | `ImpactRuleGate` yapıcısında yazılabilir store/DbContext/iş öğesi bağımlılığı yok (reflection); `WorkReadOnlyDbContext.SaveChanges` fırlatır; aynı girdide tracker ile aynı kapı kararı |
| `IssueDecisionTests` | İlk tespit yalnız koşul varken ve `Raised`; koşul yoksa satır yok; `Instruction(A)` kararı B ihtiyacını **karşılamaz**, `Party` kararı hepsini karşılar; geçerli kararın başka geçerli kararla yenilenmesi durumu değiştirmez; karar geçerliliği karar kaydından (tahsilat/zamanlı tetiklemede de doğru); koşul kalkınca `Resolved` |
| `WorkItemEscalationGuardTests` | `OwnerRole` yalnız `EscalationStep+1` ve eşleşen `Escalated` olayıyla (adım alanı dahil) aynı save'de değişir; genel güncellemede rol değişikliği reddedilir; politika sürümü uyuşmazlığı reddedilir; Done/Cancelled öğe eskalasyonla açılamaz |
| `ImpactRuleTrackerLifecycleTests` | Görev ihtiyaç başına (`IssueKey + ScopeKey`); A'nın kararı yalnız A görevini kapatır, B görevi açık kalır/açılır; talimat iptali o talimatın görevini kapatır; koşul kalkınca bütün görevler kapanır; **gölge modda operasyonel Sorun/görev hiç değişmez** (yalnız `ShadowEvaluations`); çakışmada tek yeniden deneme ve tek görev; değerlendirme satırı sonuç kodunu taşır |
| `RiskExceptionStateTests` | `Active(SingleShipment)` kapı kararı yalnız eşleşen talimat için `Proceed`; `Consumed` sonrası ikinci talimat `ReviewRequired`; `Expired` yeniden değerlendirme → `Acknowledged → Open` (başka geçerli karar yoksa) |
| `AiProposalReissueTests` | `Reissue` yalnız Draft/Stale/ShadowOnly/Rejected(ReturnForFix)'ten; araç şu an gölge moddaysa Stale/Draft da gölge revizyon üretir; `ShadowOnly → Reissue → ShadowOnly` (gölge korunur, `AwaitingApproval` olamaz); `Approved` doğrudan reissue edilemez; eski revizyon `Superseded` olur ve `Approve`/`MarkDispatched` onu reddeder |
| `AiRecoveryCoordinatorTests` (fake port) | Probe varsa önce o (etkisiz) ve **aktif katalogdan bağımsız** (emekli araç, kill switch açık); probe `Recorded+DifferentRequest` → `ContentConflict` (G-52); `Approved` + eski gönderim talebi → tam `DispatchAsync` (yeni yürütme kapalıysa gönderim yok); süresi geçmiş `Dispatched` önce `Unknown`; yeniden gönderim koşulları sağlanmazsa gönderim yok ve **Failed yok**; `MarkRecoveryAttempt` (`SendsIssued=2`) gönderimden **önce** commit; bir önerideki çakışma diğerlerini durdurmaz; devralma yalnız `RequireTakeover` olgusuyla (Work'e doğrudan çağrı yok); hiçbir yolda yeni `OperationIntentId` |
| `AiCommandDispatcherTests` | Gönderim öncesi zarf bozuk → `FailBeforeDispatch`; `Approved` olmayan veya süresi geçmiş öneri gönderilmez; politika/hedef değişimi → `Stale`, yetki düşüşü → gönderim öncesi ret; (v1.3) `ApplyOwnerOutcome` tablosu: kayıtlı ret → `Failed` (her denemede), kayıtsız ret → yalnız `SendsIssued=1` + satır değişmemişse `Failed`, aksi `Unknown`; **örtüşme**: ilk gönderimin kayıtsız reddi kurtarma satırı değiştirdikten sonra gelirse `Unknown` kalır (G-51); beklenmeyen istisna → `Unknown`; ikinci çakışmada sonuç kaybolmaz (probe yeniden okur); gönderim ve kurtarma aynı `AiEnvelopeVerifier`'ı çağırır |
| `AiPreviewRequestIdempotencyTests` | Aynı aktör + şirket + `Idempotency-Key` → aynı `OperationIntentId` ve önizleme (replay); aynı key farklı gövde/aktör → 409; eşzamanlı aynı key → tek niyet |
| `RuleScopeFanOutTests` (v1.3) | IR-LOG-001 müşteri düzeyi tetikleyici: kapsam kümesi = Party ∪ açık talimatlar ∪ değişen kararın kapsamı ∪ açık görev kapsamları; A aktif, B sona erdi → B için görev (G-55); B sevk edilmişse ihtiyaç yok; anahtarlar kapsam içerir, aynı dilimde A/B ayrı, aynı dilimin tekrarı replay (G-54) |
| `GateCatalogTests` (v1.3) | `GateCatalogManifestV1` dondurulur; her kapı niyet/hedef türü bağlı; bağlanmamış niyet → `gate_intent_mismatch` (G-60) |
| `OwnerApprovalPolicyTests` (v1.3) | Sahip politikası tanımsız → `Default` (1 onay, isteyen onaylayabilir); tanımlı eşik → kota/ayrılık; politika kaynağı `ApprovalPolicyRef`'e yazılır (G-59) |
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
| `…A_gate_query_writes_nothing_in_any_schema` | **Yetkili, normal** kapı sorgusundan önce/sonra bütün şemalarda (audit, outbox, security-event dahil) satır sayıları ve rowversion'lar aynı |
| `…A_refused_gate_query_records_only_a_security_event` | Yetkisiz/reddedilen kapı sorgusu: yalnız security-event kanalına bir kayıt; iş tabloları, audit ve outbox değişmez |
| `…An_escalation_changes_the_owner_role_only_with_its_event` | Eskalasyon komutu rol + adım + `Escalated` olayını tek save'de yazar; doğrudan rol güncellemesi reddedilir |
| `…A_single_shipment_exception_keeps_the_customer_risk_issue_open` | İstisna onayı → görev kapanır, Sorun `Acknowledged` kalır; ikinci talimat `ReviewRequired`; istisna tükenince `Open` + yeni görev |
| `AiProposalSqlServerRelationalAcceptanceTests.Two_screens_approving_the_same_proposal_produce_one_winner_one_key_one_effect` | Rowversion tek kazanan; iki `Approvals` satırı değil, bir onay + bir `version_conflict` (G-27) |
| `…Two_intents_with_identical_content_produce_two_owner_records_and_no_conflict` | **Aynı içerikte iki meşru sipariş**: iki `OperationIntentId`, iki `sales.DraftAttempts` satırı (gerçek Sales store ile), iki taslak |
| `…An_unknown_dispatch_is_recovered_with_the_same_key_and_never_creates_a_second_record` | Dispatch sırasında sahip kaydetti, gateway `Unknown`; koordinatör aynı key → replay (`Idempotency-Replayed`) → `Executed`; `sales.DraftAttempts` tek satır (G-11/G-26) |
| `…An_unknown_dispatch_that_never_reached_the_owner_is_sent_at_most_once` | Sahipte kayıt yok; bir gönderim → tek kayıt; ikinci belirsizlik → `Unknown` + `TargetLock` + `work.WorkItems` devralma; aynı hedef yeni niyet 409 |
| `…A_second_approval_by_the_same_actor_is_refused_and_a_target_change_between_approvals_makes_the_intent_stale` | İki onay; SoD; `Stale`; `Reissue` aynı niyet rev+1 |
| `…A_same_key_different_payload_from_the_owner_marks_the_proposal_failed_and_locks_the_target` | Sahip 409 mismatch (test, sahip deneme satırını farklı hash'le önceden yazar) → `Failed(ContentConflict)` + security-event; kilit kalır; `ReviewState=Required` + inceleme görevi; `review-resolution` sahibin okumasıyla `ConfirmAndRelease` → kilit kalkar (G-52) |
| `…An_owner_version_conflict_at_dispatch_fails_the_intent_and_a_retry_needs_a_new_intent` | Gönderimde sahip `version_conflict` → `Failed`; aynı niyetle `Reissue` reddedilir; yeni niyet → yeni key → yürür |
| `…An_approver_whose_permission_was_revoked_after_approval_cannot_dispatch` | Scope-yerel aktör = onaylayan; sahip fence'i güncel yetkiyi doğrular → ret; ikinci etki yok |
| `…Commit_then_crash_before_proposal_update_is_recovered_without_a_second_write` | `onStaged`-benzeri kanca ile `SaveAsync` sonrası süreç kesilir; yeniden başlatma → kurtarma → tek kayıt |
| `AiRunSqlServerRelationalAcceptanceTests.Read_draft_simulate_tools_leave_no_business_outbox_or_command_rows` | Sahip şemalarında satır sayısı değişmez (negatif test) |
| `AiWorkflowSqlServerRelationalAcceptanceTests.Worker_restart_with_running_instances_does_not_duplicate_work` | Lease/rowversion; `Unknown` kontrolü; tek iş öğesi (G-18) |
| `…Duplicate_outbox_delivery_creates_one_job` | `ai.Inbox` PK (G-19) |
| `…Exception_expiry_reevaluates_and_reopens_the_issue_without_a_new_card` | Zamanlı tetikleyici; `Acknowledged → Open` (yeni Sorun yok); yeni **karar görevi** açılır; koşul kalkıp geri dönmüşse `Resolved → Reopened` |
| `AiProposalSqlServerRelationalAcceptanceTests.A_recovery_resend_refused_after_approver_lost_permission_keeps_the_intent_unknown` | İlk gönderim sahipte kaydedildi, cevap kayboldu; probe yok. (a) onaylayanın izni kaldırıldı → gateway yeniden göndermez; (b) yalnız sahip tarafında kapsam daraldı → yeniden gönderim 403 → `LastAttemptRefusalCode`; ikisinde öneri `Unknown` + kilit + devralma görevi; `sales.DraftAttempts` tek satır (G-43) |
| `…An_overlapping_first_dispatch_refusal_never_releases_a_recorded_intent` (v1.3) | İlk gönderim sahipte bekletilir (test kancası); süre dolar; kurtarma `SendsIssued=2` commit eder ve yeniden gönderir, sahip kaydeder, cevap düşürülür; ilk gönderime sonra 403 verilir → öneri `Unknown` + kilit; sonraki tarama probe → `Executed`; ters sıra varyantında ilk 403 önce kaydedilir → `Failed` ve yeniden gönderim hiç çıkmaz (G-51) |
| `…A_human_cannot_fail_an_unknown_intent_without_an_owner_key_fence` (v1.3) | `verify` → `NotFound` → 422; `fence` → sahip key'e terminal "kapatıldı" yazar → `Failed(OwnerKeyFence)`; ardından bekletilen eski istek serbest bırakılır → sahipte "kapatıldı" replay'i, yeni kayıt yok (G-52) |
| `…A_crash_between_proposal_resolution_and_task_closure_is_healed` (v1.3) | Probe `Executed` kaydı + `ai.Outbox` olgusu aynı save; köprü görev kapatmadan kesilir; yeniden başlatmada köprü/uzlaştırma görevi kanıtla kapatır; olgu iki kez işlenirse tek kapanış (G-53) |
| `…AiRecoveryRunsWithNewAiExecutionDisabled` (v1.3) | `Ai:Enabled=false`, kill switch açık, `Ai:Workflow:Enabled=false`, araç emekli: `Unknown` önerinin probe'u çalışır, devralma görevi açılır/kapanır, `/resolution` cevap verir; hiçbir gönderim çıkmaz (G-53) |
| `…An_authorized_gate_with_another_intent_or_target_evaluates_nothing` (v1.3) | Yetkili kapı adı + başka niyet/hedef türü → 400; kapsam dışı hedef → 403; değerlendirici port çağrı sayısı 0 (G-60) |
| `…An_approved_proposal_whose_process_died_before_dispatch_is_dispatched_by_the_sweeper` | Onay kaydı sonrası süreç kesilir; süpürücü tam gönderim yolundan geçirir; tek gönderim (G-44) |
| `…A_repeated_proposal_create_request_returns_the_same_intent` | Aynı `Idempotency-Key` → aynı niyet; `ai.CommandAttempts` tek satır (G-45) |
| `WorkSqlServerRelationalAcceptanceTests.An_instruction_saved_without_the_client_tracking_call_is_picked_up_by_reconciliation` | Takip çağrısı yapılmadan talimat kaydı; uzlaştırma taraması görevi açar; tekrar taramada ikinci görev yok (G-46) |
| `…A_never_updated_order_that_becomes_overdue_is_detected` | Geçen hafta kaydedilmiş, hiç güncellenmemiş sipariş bugün termin geçince tespit edilir (G-47) |
| `…Instruction_B_gets_its_own_decision_task_while_A_exception_is_active` | A istisnası aktif; B için ayrı görev; A görevi kapalı kalır (G-41); karar yenilemesi gereksiz görev açmaz (G-42) |
| `…A_and_B_in_the_same_sweep_slot_are_evaluated_separately` (v1.3) | Aynı müşterinin A ve B talimatları aynı tarama diliminde: iki ayrı `work.Attempts` anahtarı, iki değerlendirme; aynı tarama tekrarında fazladan görev yok (G-54) |
| `…B_expiry_while_A_is_active_opens_a_task_for_B` (v1.3) | A ve B istisnaları, iki görev kapanmış; B sona erer (süre-sonu taraması, Party tetikleyici) → dağıtım B'yi değerlendirir → B görevi açılır; A karşılanmış kalır (G-55) |
| `…A_stale_risk_evaluation_cannot_reopen_an_issue_closed_by_a_newer_receipt` (v1.3) | Değerlendirme 1 kaynağı okur ve bekletilir; tahsilat kaydedilir; değerlendirme 2 Sorunu kapatır; değerlendirme 1 commit'te `TrackHead` çakışması → yeniden yükle + yeniden hesapla → Sorun kapalı kalır (G-56) |
| `…A_rule_switched_to_shadow_never_changes_operational_issues_or_tasks` | Gölgeye alınan kural mevcut Sorun/görevi değiştirmez (G-48) |
| `…WorkRulesWorkerRunsWithAiFullyDisabled` | `Ai:Enabled=false`, kill switch açık, `Ai:Workflow:Enabled=false`: zamanlı tarama, uzlaştırma, eskalasyon ve görev açma/kapama çalışır (G-50) |
| `AiProposalSqlServerRelationalAcceptanceTests.A_superseded_revision_can_never_be_dispatched` | `UX_Proposals_OneLiveRevisionPerIntent`; eski revizyon gönderim denemesi reddedilir |
| `…A_shadow_proposal_reissued_stays_shadow` | Gölge öneri yeniden üretilince `ShadowOnly`; onay endpoint'i `NotApprovable` |
| `…An_expired_dispatch_is_moved_to_unknown_before_recovery` | `Dispatched` + süre aşımı → `Unknown` → kurtarma yolu |
| `AiSnapshotSqlServerRelationalAcceptanceTests.A_composite_read_sees_one_snapshot_when_an_allocation_lands_midway` | Mekanizma A (`SNAPSHOT`); B için sıralı (kimlik, rowversion) özeti (G-28) |
| `…An_out_of_order_rowversion_commit_is_detected_by_mechanism_b` (v1.3) | İki bağlantı: A güncellemesi açık işlemde (rowversion alınmış, commit yok), B güncellenip commit; ilk okuma; A commit; son kontrol özeti **farklı** → yeniden okuma/`Stale` damgası (G-57) |
| `AiMigrationSqlServerRelationalAcceptanceTests.Migrations_apply_and_roll_back_on_an_empty_database` | `MigrateAsync` + `Down` (canlıya çıkmamış) |

### 18.3 HTTP kabul (in-memory `GoymenErpApiFactory` + stub servis; Ek K-1 madde 17b)

`AiHttpAcceptanceTests`: metadata (tek `Idempotency-Key`, `X-Company-Id`), 201 + Location, replay başlıkları, problem sınırı (401/403/404/503 kodsuz), `command_metadata_invalid`, `unsupported media type`; `WorkHttpAcceptanceTests` aynı; `RulesGateHttpAcceptanceTests`: kapı cevabında **tutar alanı olmadığı** şema testi (`exact` alan seti).

### 18.4 Mimari ve sözleşme kapıları

- `ModuleDependencyGuardTests`: allowlist'e §2.3 kenarları; başka kenar yok.
- Yeni: `WorkNeverReferencesAiTests`, `AiAndWorkNeverUseRawSqlTests`, `AiWriteToolsAreOnlyInvokedByDispatcherTests` (reflection: `IAiTool` WRITE `ExecuteAsync` çağıran tek tip `AiCommandDispatcher`/`AiRecoveryCoordinator`), `SystemActorHasNoWriteToolsTests`, `ScopedActorOverrideIsOnlySetByRuntimeFactoriesTests`, `DispatchPathHasNoTransportRetryTests` (v1.3: WRITE yürütme yolunda Polly/retry handler yok), `RecoveryDoesNotDependOnActiveCatalogForProbeTests` (v1.3), `AiNeverCallsWorkCommandsOutsideBridgeTests` (v1.3: devralma görevi yalnız `AiWorkBridge`/`AiTakeoverReconciler`) (`IScopedActorOverride.Set` çağıranlar yalnız `AiDispatchScopeFactory`, `RulesRuntime` ve worker scope fabrikaları), `GateQueryHasNoWriteDependenciesTests` (kapı endpoint'inin DI grafiğinde tracker/store/DbContext/iş öğesi komutu yok).
- `PlatformTenantQueryFilterBypassGuardTests` envanteri: AI/Work'te `IgnoreQueryFilters` yok (sıfır kayıt).
- OpenAPI drift (`contract-drift` işi) ve shell capability revizyon testleri (`C01E2eShellCapabilityActivation`).
- Eval harness (`GoymenERP.Ai.EvalTests`): fake provider + golden set (master Ek D G-01…G-60 + `ERP-000J` §20 1–35); P0 güvenlik seti CI'da zorunlu; iş kalitesi seti raporlayıcı.

### 18.5 CI (`.github/workflows/ci.yml` — K3; yalnız kalite kapısı)

Mevcut işlere ek: `ai-unit`, `work-unit`, `ai-eval-p0` (fake provider; ağ yok), gerçek SQL kabul işlerine yeni test sınıfları (`--filter FullyQualifiedName~Ai|Work`), architecture guard, frontend `vitest` (ai/work lane'leri). Opt-in sağlayıcı sözleşme testi **yalnız** `OPEN-020` kabulünden sonra ve ayrı, elle tetiklenen workflow'da (`workflow_dispatch`; secret'lar CI'da değil). `.github/**` değişikliği deployment/secret/production yüzeyine dokunmaz.

---

## 19. Yapılandırma, bayraklar, secret'lar

```jsonc
// appsettings.json (ÖNERİ; değerler placeholder) — .env.example yalnız placeholder taşır
"Work": { "Rules": { "Enabled": true, "ShadowOnly": true },             // AI'dan bağımsız; ShadowOnly: yalnız work.ShadowEvaluations; operasyonel Sorun/görev değişmez
          "Scheduler": { "Enabled": true, "ReconciliationInterval": "00:15:00", "LeaseDuration": "00:05:00" } },   // WorkRulesWorker — Ai:* bayraklarına bağlı değil
"Ai": {
  "Enabled": false,                                                       // false → capability DEFINED_DISABLED; YENİ run/önizleme/gönderim 503 ai_disabled; kurtarma ve çözüm yolları açık (v1.3)
  "KillSwitch": false,                                                    // acil: bütün model çağrıları ve panel kapalı; Work etkilenmez
  "CloudGateway": { "Activated": false },                                 // OPEN-020 aktivasyon kanıtı; yalnız insan; API'ye çıkış bunsuz imkânsız
  "Providers": { "primary": { "Kind": "HttpJsonSchema", "Endpoint": "", "DeploymentRef": "", "SecretName": "AI_PROVIDER_API_KEY", "MaxDataClass": "Gizli", "AllowedHosts": [] },
                 "fake":    { "Kind": "Fake", "FixturePath": "eval/fixtures" } },
  "Routes": { "Genel": ["primary"], "SirketIci": ["primary"], "Gizli": ["primary:minimized"], "CokGizli": [] },   // CokGizli boş liste; kod ayrıca sabit reddeder
  "Tools": { "finance.risk.evaluate@1": { "Enabled": true }, "sales.draft.create@1": { "Enabled": false } },       // tool bazlı flag; WRITE beyaz listesi Faz 4
  "Preview": { "Expiry": "24:00:00" },                                     // NEEDS-SINAN NS-AI-05
  "Budgets": { "PerRun": { "MaxToolCalls": 12, "MaxTokens": 20000, "MaxSeconds": 60, "MaxCostMicros": 500000 }, "Company": { "MonthlyCapMicros": 0 } },
  "Workflow": { "Enabled": false, "Standalone": false, "PollInterval": "00:00:15", "LeaseDuration": "00:05:00" },
  "Recovery": { "Enabled": true, "SweepInterval": "00:01:00", "DispatchGracePeriod": "00:02:00", "MaxResendAge": "01:00:00", "ReconcileInterval": "00:05:00" },
      // (v1.3) Ai:Enabled/KillSwitch/Workflow'dan bağımsız; DispatchGracePeriod > en uzun WRITE aracı Timeout + pay (AiOptions başlangıçta doğrular);
      // yine de örtüşme doğruluğu süreye değil §10.3 kuralına (kayıt + SendsIssued + rowversion) dayanır. MaxResendAge değeri NS-AI-05 kapsamında öneridir
  "Retention": { "PayloadVault": "90.00:00:00", "PromptContent": "0.00:00:00" }   // OPEN-018/020/021; 0 = içerik saklanmaz
}
```

- Secret'lar ortam değişkeni/KMS'ten `IAiSecretProvider` ile okunur; log, trace, `ai.*` tabloları ve prompt'a girmez; `.env.example` yalnız `AI_PROVIDER_API_KEY=` placeholder'ı taşır.
- `Ai:CloudGateway:Activated` ve `Ai:Tools:*:Enabled` (WRITE) değişiklikleri production'da **insan** işidir (production aktivasyonu Sinan); uygulama bu değerleri UI'dan değiştiremez (AI-03 salt-okunur gösterir).
- `DatabaseStartupMode` ile aynı biçimde `Ai:Workflow:Enabled` ve `Ai:Recovery:Enabled` OpenAPI üretim modunda her zaman `false`.
- (v1.3) `Ai:Enabled=false` modül **kaydını** kaldırmaz: `ai` şeması, kurtarma ve insan çözüm endpoint'leri kayıtlı kalır; yalnız yeni run/önizleme/gönderim endpoint'leri `503 ai_disabled` döner.

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

## 22. Kapanış kontrol tablosu (v1.3; üç durumlu)

Üç durum ayrı gösterilir: **Tasarımda karşılandı** (ilke ve sözleşme yazılı) · **Örnek eşitlendi** (metin, şema ve kod örneği birbiriyle çelişmiyor; "Evet" yalnız son bağımsız kontrolde açık bulgu kalmayan satırlar içindir; bu sürümde düzeltilen satırlar **"v1.3'te düzeltildi — yeniden kontrol bekliyor"**, son kontrolde ayrıca incelenmeyenler "yeniden kontrol bekliyor" olarak kalır) · **Uygulamada test edilecek** (kabul testi §18'de planlı; yazılmadı, koşulmadı). Hiçbir satır "uygulandı" veya "test geçti" anlamına gelmez.

| Konu | Tasarımda karşılandı | Örnek eşitlendi | Uygulamada test edilecek | Konum (bu belge / master) |
|---|---|---|---|---|
| Kalıcı `OperationIntentId`, içerik hash'inden ayrı; üç durum | Evet | Evet (son kontrolde korunması istendi) | Evet | §5.1, §4.3 / master §5.5, §5.6 |
| Belirsiz sonuç için yeni kimlik yok | Evet | Evet | Evet | §5.1, §10.4 / §5.6 |
| Öneri oluşturma isteğinin tekrarı | Evet | Evet (son kontrolde korunması istendi) | Evet | §4.3, §10.1 / §5.6, G-45 |
| Eskalasyonda rol değişikliği ile yazma koruması | Evet | Evet | Evet | §4.2, §5.4 / §5.7 |
| Gölge değerlendirmenin operasyonel kayıtlardan ayrılması | Evet | Evet (son kontrolde korunması istendi) | Evet | §9.1 / §5.5, G-48 |
| AI'dan bağımsız zamanlı takip (`WorkRulesWorker`) | Evet | Evet (son kontrolde korunması istendi) | Evet | §14.1 / §5.8, G-50 |
| Sonucun kesinliği: kayıtlı sonuç / tek-gönderim kanıtı; örtüşen ilk gönderim + kurtarma (R-01) | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor | Evet | §5.1 `ApplyOwnerOutcome`, §10.3, §10.4 / §5.6, G-51 |
| Kilitli `ContentConflict` inceleme yolu; insan çözümünde sahip kanıtı ve key fence; probe eşleşmesi (R-02) | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor | Evet | §5.1, §10.4, §15.1 / §5.6, G-52 |
| Öneri sonucu ↔ devralma görevi kesintiye dayanıklı bağ; kurtarmanın yeni AI yürütme bayraklarından ve aktif katalogdan bağımsızlığı (R-03) | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor | Evet | §4.3 `ai.Outbox`, §10.4a, §14.1, §19 / §5.6, G-53 |
| Onay kalıcılaştı, gönderim başlamadan çökme | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor (süpürücü artık `AiRecoveryWorker`'da) | Evet | §10.4 (a), §14.1 / §5.6, G-44 |
| Zamanlı taramanın tekrar anahtarında karar kapsamı (R-04) | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor | Evet | §9.1, §9.1a / §5.8, G-54 |
| İstisnası sona eren kapsamın görevi; eski hesabın yeni durumu geri alması (R-05) | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor | Evet | §9.1 (`TrackHead`, sıra), §9.1a / §3.4, §5.8, G-55, G-56 |
| Tek sevkiyat istisnası ve talimat kapsamlı karar ihtiyacı (A/B); yenileme ≠ geçersizleşme | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor (R-04/R-05 ile) | Evet | §5.3, §9.1 / §3.4, G-36, G-41, G-42 |
| Sorun–görev kesintisi ve takibin hiç başlatılmaması | Evet | yeniden kontrol bekliyor (outbox kabulüne kadar uzlaştırma zorunlu) | Evet | §9.1, §9.4 / G-38, G-46, G-47 |
| Kesit B: sıralı (kimlik, rowversion) çiftleri; güvence daraltıldı (R-06) | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor | Evet | §8.2 / §5.7, G-28, G-57 |
| Çıktı doğrulama: her görünür parçada bağlam doğrulaması; kritik yargı sunucudan (R-07) | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor | Evet | §12 / §6.2, G-31, G-49, G-58 |
| İnsan onayı ≠ kişi ayrılığı; SoD sahip politikasından (R-08) | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor | Evet | §5.1 `Approve`, `ApprovalPolicy` / §5.6, NS-AI-20, G-35, G-59 |
| Kapı kimliği–niyet–hedef kapsamı bağlaması (R-09) | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor | Evet | §9.3 / §3.4, G-60 |
| Salt-okunur kapı sorgusu | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor (kod v1.3'te değişti; kaydedilmemiş taslak tabanı, security-event ifadesi) | Evet (satır sayısı/rowversion değişmez) | §9.1, §9.3 / §3.3, G-39 |
| Veri sınıfı ve saklama (içerik alanları depoda, yasal bekletme) | Evet | yeniden kontrol bekliyor (v1.3: inceleme durumu yasal bekletmeye eklendi) | Evet | §4.2, §4.3, §4.5 / §5.5 |
| İki onay gerektiren işlem | Evet | v1.3'te düzeltildi — yeniden kontrol bekliyor (kota ve ayrılık sahip politikasından) | Evet | §5.1 / §5.6 |
| Kanıt matrisi ("repoda var" yerine dosya:satır) | Evet | Evet (daraltılmış) | Sahip dilimde: Counterparty farklı-aktör, Accounting key düzeyi, `IIdempotencyOutcomeV1`, kayıtlı ret kod listesi | §10.6 / §5.6a |
| API-birincil uzlaştırması; aktivasyon ayrı | Evet | Evet | Evet (router) | §11, §19 / §1, §5.10 |
| Nötr kapı metni öneridir | Evet | Evet | — | §9.3 / §3.4, NS-AI-10 |
| `Work` tasarımı ≠ geliştirme kuyruğu; repo/karar/görev/kod/izin değişmez | Evet | Evet | — | §0.2, §21 / §0.1 |

---

## 23. Açık noktalar (bu belgenin kapatamadıkları)

| ID | Konu | Tür |
|---|---|---|
| K-AÇIK-01 | `IFinanceRiskEvaluationV1` ve `finance.risk.shipment-gate@1` port'ları (`W4-RISK`) — bugün yok; IR-LOG-001 bunlarsız `Unavailable` döner | DOĞRULANACAK (sahip dilim) |
| K-AÇIK-02 | Sahip modüllerde salt-okunur `*ReadParticipant.EnlistAsync` (mekanizma A) port'ları | ÖNERİ; sahip dilim eki; olmadan yalnız mekanizma B (daraltılmış güvence, §8.2) |
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
| K-AÇIK-15 | (v1.3) Sahip modüllerde `IIdempotencyOutcomeV1`: `FindAsync` (key + payload → sahibin kendi eşleşme hükmüyle kayıtlı sonuç; etkisiz) ve `FenceAsync` (key'e terminal "kapatıldı" kaydı; varsa mevcut kaydı döndürür). WRITE beyaz listesi için **Faz 4 ön koşulu**; port'suz komutta `Unknown` öneri insan tarafından `Failed` kapatılamaz | ÖNERİ; sahip dilim eki (K3 sözleşme) — DOĞRULANACAK |
| K-AÇIK-16 | `policy.EvaluateResendAsync` koşulları (güncel capability, onaylayan izni/SoD, kill switch) ve `Ai:Recovery:MaxResendAge` değeri | NS-AI-04/05 kapsamında değer; kural ÖNERİ |
| K-AÇIK-17 | Kaynak modül olaylarının outbox dispatcher'ı (bugün dispatcher yok, Ek K-1 #13); kabul edilene kadar `work.rules.reconciliation` taraması zorunlu garanti yolu, tarama aralığı ve geriye bakış penceresi | Sahip dilim + NS-AI-04 |
| K-AÇIK-19 | (v1.3) Sahip başına iki açık kod listesi: "terminal ret deneme satırına yazılır" (`RecordedForKey`) ve "key'e hiçbir şey yazılmadan reddedilir" (`NotRecorded`); listelenmeyen kod `Unknown` sayılır (Ek K-2) | Sahip dilim; DOĞRULANACAK |
| K-AÇIK-20 | (v1.3) `ILogisticsOpenInstructionReadsV1` (müşterinin açık talimatları; durum sorgusu, keyset) — IR-LOG-001 kapsam dağıtımı için | Logistics sahip dilim eki; DOĞRULANACAK |
| K-AÇIK-21 | (v1.3) `IOwnerApprovalPolicyV1` (komut + içerik → `RequiredApprovals`, `RequiresSeparateApprover`) ve bütün AI yazmalarında genel iki-kişi kuralı istenip istenmediği | Sahip dilim + NS-AI-20 |
| K-AÇIK-18 | Finance karar kapsamı (`Party` / `Instruction(id)`) ve karar olaylarının (`finance.risk-exception.*.v1`) sözleşmesi | `W4-RISK` sahip dilim; ÖNERİ |

---

## 24. Değişiklik günlüğü

**v1.3 (28 Eylül 2026 — yeniden kontrol bulguları R-01…R-09):** Ana mimari korundu; yalnız bulguların etkilediği sözleşmeler, kod örnekleri, şemalar ve kabul senaryoları değişti. Master rapor §15.0 (v1.5) ile birebir eşleşir. Testler kabul planıdır; hiçbiri koşulmadı. Eksik helper/port varsayılarak "kapandı" yazılmadı: dayanılan her yeni port §23'te açık nokta olarak durur.

| Bulgu | Karşı örnek | Değişen sözleşme / metot | Beklenen sonuç | Kabul senaryosu |
|---|---|---|---|---|
| R-01 | İlk gönderim sahipte bekler; kurtarma aynı key ile gönderir ve sahip kaydeder, cevap kaybolur; ilk gönderimin 403'ü sonra gelir → eski kod `First` olduğu için `Failed` yapıp kilidi kaldırırdı | `OwnerOutcomeKind` (Executed / RecordedRefusal / SameKeyMismatch / UnrecordedRefusal / Unknown); tek uygulama noktası `AiProposal.ApplyOwnerOutcome(o, afterConcurrentChange)`; `SendsIssued` (+`CK_Proposals_Sends`); `MarkRecoveryAttempt` gönderimden önce commit; `ExecuteAsync` çakışmada taze satıra `afterConcurrentChange: true`; `DispatchAttempt` yalnız audit | Kayıtlı sonuç her zaman kesin; kayıtsız ret yalnız "tek gönderim + satır değişmedi" (rowversion) iken kesin; örtüşmede öneri `Unknown` + kilitli kalır, probe `Executed` yapar | G-51; `AiCommandDispatcherTests`; `…An_overlapping_first_dispatch_refusal_never_releases_a_recorded_intent` |
| R-02 | `Failed(ContentConflict)` kilitli; kurtarma onu seçmez, `ResolveByHuman` yalnız `Unknown` kabul eder → çıkmaz; insan "başarısız" için boş olmayan metin yeterliydi; probe yalnız anahtarı bulunca başarı sayabilirdi | `ReviewState` (işlem sonucundan ayrı); `ResolveReview(LinkOwnerRecord \| ConfirmAndRelease, OwnerRecordEvidence)`; `ResolveByHuman(OwnerRecordEvidence)` — kanıt sunucuda sahipten (`verify`/`fence`), istemciden değil; `IIdempotencyOutcomeV1.FindAsync` payload alır ve sahip kendi kuralıyla `ThisRequest`/`DifferentRequest` der; `FenceAsync` | Kilitli uyuşmazlık kanıtlı inceleme ile çözülür (bütün `Failed`'lar açılmaz); `Failed` insan çözümü yalnız key fence veya kayıtlı terminal retle; `NotFound` hiçbir şey kanıtlamaz | G-52; `…A_human_cannot_fail_an_unknown_intent_without_an_owner_key_fence`; `…A_same_key_different_payload…` |
| R-03 | Probe `Executed` kaydeder, görev kapatılmadan çöker; tarama `Executed`'ı seçmez → görev açık kalır; kurtarma `AiWorkflowWorker` altındaydı, AI kapatılınca sonuç okuma da dururdu; probe aktif katalogdan çözülüyordu | `ai.Outbox` + `PendingFacts` (durum ve olgu tek save); `AiWorkBridge` (olgu → Work, `ai-fact:{MessageId}` tekil) + `AiTakeoverReconciler` (iki yönlü uzlaştırma); `RequireTakeover` (Work'e doğrudan çağrı yok); `AiRecoveryWorker` (`Ai:Recovery:Enabled`, AI yürütme bayraklarından bağımsız); `IAiOwnerOutcomeRegistry` (sahip sözleşmesine göre, katalogdan bağımsız); çözüm endpoint'lerine `ai_disabled` uygulanmaz | Yeniden başlatmada görev kanıtla kapanır; yeni AI yürütme kapalıyken probe/devralma/çözüm çalışır, gönderim çıkmaz; emekli aracın eski işlemi okunur | G-53; `…A_crash_between_proposal_resolution_and_task_closure_is_healed`; `…AiRecoveryRunsWithNewAiExecutionDisabled` |
| R-04 | Aynı müşterinin A ve B talimatı aynı tarama diliminde → `sweep:{ruleId}:{slot}:{issueKey}` aynı → B için A'nın sonucu replay veya 409 | Anahtarlar kapsam içerir: `sweep:{ruleId}:{slot}:{issueKey}:{scopeKey}`, `rule:{deliveryId}:{ruleId}:{issueKey}:{scopeKey}`; takip komutunda istek hash'i `scopeKey` içerir | A ve B ayrı değerlendirilir; aynı taramanın tekrarı fazladan görev üretmez | G-54; `…A_and_B_in_the_same_sweep_slot_are_evaluated_separately` |
| R-05a | A ve B istisnaları, iki görev kapalı; B sona erer, A aktif; süre-sonu `Party` değerlendirmesinde A'nın kararı yeterli görünür, kapanmış B görevi sorguya gelmez → B görevsiz | `RuleScopeFanOut` + `IDecisionScopeSource`: kapsam kümesi = Party ∪ açık talimatlar (Logistics durum port'u) ∪ değişen kararın kapsamı ∪ açık görev kapsamları; `RuleContext.Scope` açık parametre | B için görev açılır; sevk edilmiş/iptal talimat ihtiyaç üretmez | G-55; `…B_expiry_while_A_is_active_opens_a_task_for_B` |
| R-05b | Eski değerlendirme "risk var" okur ve bekler; yeni değerlendirme tahsilatı görüp Sorunu kapatır; eskisi devam edip kapanmış Sorunu taze okuyarak yeniden açardı | Sıra: `LoadTrackStateAsync` (TrackHead + Issue + açık görevler) **önce**, hesap **sonra**; `work.TrackHeads` her kalıcı değerlendirmede (koşul yokken de) yazılır ve rowversion/PK ile doğrulanır; çakışmada yeniden yükle + yeniden hesapla (en fazla 3) | Kaydedilen durum önceki kaydedilmiş durumdan sonra okunmuş veriden türer; kapanmış Sorun eski hesapla açılmaz | G-56; `…A_stale_risk_evaluation_cannot_reopen_an_issue_closed_by_a_newer_receipt` |
| R-06 | A'nın güncellemesi 101'i alır, commit etmez; B 102 ile commit; ilk okuma A=100/B=102; A commit eder → sayım, MAX, kimlik kümesi aynı kalır | Kesit B: kimliğe göre sıralı `(id, RowVersion)` çiftlerinin özeti + beyan edilen bağlı kayıtlar; `sayım+MAX` yalnız ön kontrol; güvence "ortak anlık görüntü değildir" diye daraltıldı; kritik birleşik okumada A | Commit sırasından bağımsız olarak kapsamdaki her commit edilmiş değişiklik yakalanır | G-57; `…An_out_of_order_rowversion_commit_is_detected_by_mechanism_b` |
| R-07 | Özette "X müşterisinin riski {{measure:m2}}" — `m2` Y'nin; bağlama kontrolü yalnız bulgu varken çalışıyordu | `UserVisibleParts()` + sunucu kurduğu `BindingContext` (özet cümlesi `about`, eylem hedefi, taslak alıcıları); her token ve `judgementRef` bağlama bağlı; kayıt adları yalnız `{{ref:}}`; kritik yargı sunucudan; sözlükler "ek savunma" olarak etiketlendi | Yanlış kayda bağlı geçerli ölçü özet, etiket ve taslakta da reddedilir | G-58; `AiOutputValidatorTests` |
| R-08 | Normal kaydetme yetkili kullanıcı kendi satış taslağı önizlemesini onaylayamıyordu (koşulsuz maker≠checker) | `ApprovalPolicy` (sahip modülün kabul edilmiş politikası; `RequiresSeparateApprover`, `RequiredApprovals`, `ApprovalPolicyRef`); `Approve` ayrılığı yalnız politika isterse uygular; genel kural NS-AI-20 | İnsan onayı her WRITE'ta zorunlu; kişi ayrılığı sahip politikasından geniş değil | G-59; `OwnerApprovalPolicyTests`; `AiProposalStateMachineTests` |
| R-09 | Yetki `GateToolId` üzerinden, kurallar istemcinin `IntentKind`'ından seçiliyordu; bağlama görünmüyordu | `IGateCatalog`/`GateDescriptor` (niyet → hedef türü, izin, hedef okuma); `gate_intent_mismatch`; hedef kaydın aktör kapsamında okunabilirliği; kurallar descriptor'dan seçilir | Yetkili kapı adıyla başka niyet/hedef → 400/403, hiçbir kural çalışmaz | G-60; `…An_authorized_gate_with_another_intent_or_target_evaluates_nothing` |
| İç kontrol (v1.3 teslim öncesi, bağımsız ajan) | İnceleme görevi hiç açılmıyor / erken kapanıyordu; kill switch süpürmede `Approved`'ı `Failed` yapıyordu; eşlenemeyen sahip kodu hedefi serbest bırakabiliyordu; kapı `OnlyRule`'u yok sayıyordu; dağıtımın imleci yoktu; çok özneli `about` yanlış ölçüyü geçiriyordu; Finance'te `RowVersion` yok; kaydedilmemiş taslakta kapı yetkisi/limit sızıntısı; olgu sürüm çakışması | Ayrı `ai-review` görevi + `ReviewResolved` olgusu, `TakeoverRequired` temizlenir; `IsExecutionDisabled` → yazma yok; eşlenemeyen kod → `Unknown`; `rules.For(trigger, OnlyRule)`; `rules.fanout` imleci; tek özne kuralı; değişim belirteci `RowVersion`/`Version`, yoksa A veya "eskimiş" damgası; `DraftBasis` + sunucu hesabı + oran sınırı; store olgulara kaydedilen sürümü damgalar; küçükler (`AlreadyInProgress` taze durum, devralma nedeni bu denemeden, `Approve` isteyeni satırdan okur, `CK_Proposals_Review` genişledi, `ConfirmAndRelease` kanıt türü) | Aynı senaryolar (G-51…G-60) + §10.5 | — |
| Küçük | §0.1 master v1.3 atfı, §1 "bir worker"; Ek K-3'te `EscalationTicker` AI Workflow listesinde; §4.4'te `CommandAttempts`/`ShadowEvaluations`/`SchedulerLeases` sahipliği eksik; Ek K-2 `*_concurrent_change`/`version_conflict` → `Failed` eşlemesi; §18.4 G-01…G-40 | Hepsi eşitlendi: master v1.5 atfı, üç çalıştırıcı, dosya listeleri ve şema sahipliği, K-2 `OwnerRefusalKind` eşlemesi (adaptör `Failed` üretmez), G-01…G-60 | — | — |

**v1.2 (28 Eylül 2026 — ikinci kapanış kontrolü bulguları):** Yeni tasarım tartışması açılmadı; karşı örnekler kapatıldı. Master rapor §15.0 ile birebir eşleşir.

1. **Kurtarma reddi:** `ExecuteAsync(..., DispatchAttempt attempt, ...)` ilk gönderim ile kurtarma denemesini ayırır. Kurtarmada sahibin kayıtlı sonucu olmayan reddi (ör. 403) `RecordRecoveryRefusal` ile yalnız denemeye yazılır; `Unknown` + kilit + devralma sürer. Aynı key farklı içerik `Failed(ContentConflict)`; replay'li terminal ret `Failed`. `ApplyLateOutcome` geç başarıyı `Failed` üzerine de uygular (§5.1, §10.3).
2. **Probe ≠ yeniden gönderim:** koordinatör önce etkisiz sonuç okumayı (`IIdempotencyProbeV1`, varsa) dener; yeniden gönderim yalnız `EvaluateResendAsync` (güncel capability, onaylayanın izni/SoD, bayrak, kill switch) + `MaxResendAge` + zarf doğrulaması geçerse; koşul sağlanmazsa gönderim yok, `Failed` yok (§10.4, K-AÇIK-15/16).
3. **Onaylanmış-gönderilmemiş öneri:** `Approve` `DispatchRequestedAtUtc`'yi aynı save'de yazar; süpürücü `Approved` satırları tam `DispatchAsync` yolundan geçirir; `MarkDispatched` eşzamanlılık hatası `AlreadyInProgress` (§5.1, §10.3, §10.4).
4. **Öneri oluşturma idempotency'si:** `ai.CommandAttempts` + `AiPreviewService.CreateAsync(intent, context, metadata, ct)` replay araması; deneme satırı öneriyle aynı save'de (§4.3, §10.1).
5. **Karar kapsamı:** `DecisionScope` (`Party` / `Instruction(id)`), `DecisionRef.Covers`, `Issue.NeedsDecisionFor(scope)`, `SyncDecisionState`; görev karar ihtiyacı başına (`DedupKey = IssueKey + ScopeKey`); A/B senaryo tablosu (§5.3, §9.1).
6. **Takip başlatma garantisi:** outbox birincil, `work.rules.reconciliation` taraması outbox kabulüne kadar zorunlu, ekran çağrısı hızlandırıcı; zamanlı sorgular durum sorgusu (`status=open&promiseBefore=today`, keyset) (§9.4, §14.2, K-AÇIK-17).
7. **Gölge ve çalıştırıcı:** gölge değerlendirme yalnız `StageShadowEvaluation` → `work.ShadowEvaluations`; iş akışı adımı `WorkflowStep.Track` (tracker çağırır, doğrudan görev açmaz); `WorkRulesWorker` AI bayraklarından bağımsız, `AiWorkflowWorker` ayrı (§9.1, §14.1, §14.2, §3.1, §19).
8. **Çıktı doğrulama:** token dışı rakam yasağı (`{{measure:}}`, `{{ref:}}`, `{{date:}}`), bütün `UserVisibleTexts()` taranır, yargı cümleleri için zorunlu `judgementRef` + sözlük kontrolü; `AiMeasureRenderer` (§12).
9. **Veri sınıfı ve saklama:** `WorkItemRow` `TitleTemplateCode`+`TitleParamsRef`, `ScopeKey`, `DataClass`; `ProposalRow` `DiffRef`, `ValidationRef`; yasal bekletme `Approved`/`Dispatched`/`Unknown`/kilitli öneriyi kapsar (§4.2, §4.3, §4.5).
10. **Küçük eşitlemeler:** kesit B küme eşitliği (sayı, `MAX(RowVersion)`, kimlik kümesi hash'i; `Version` ≠ `RowVersion`) (§8.2); security-event kapsamı (§9.3); onay cevabı durumları `Failed`/`Unknown`/`Stale` (§15.1); kapı kartı izin testi ikiye ayrıldı (§17.4); yeni test adları G-41…G-50 ile eşlendi (§18); §10.5'e on senaryo; §22 üç durumlu tablo "yeniden kontrol bekliyor" ile; §23'e K-AÇIK-15…18.
11. **İç bağımsız kontrol (v1.2 teslim öncesi):** üç öncelikli senaryo (ilk gönderim başarılıyken kurtarma reddi; A istisnası aktifken B'nin Finans görevi; onay sonrası gönderim öncesi çökme) kod örnekleri üzerinden izlendi ve belgelenen sonucu verdi. Bulunan tutarsızlıklar düzeltildi: `ApplyLateOutcome` artık "niyetin sonucu mu" bilgisiyle karar verir ve deneme reddini taze satıra taşır; kurtarma koordinatörü öneri başına yalıtıldı (eşzamanlılık kaybı ve beklenmeyen hata diğer önerileri durdurmaz); devralma görevi "ensure" komutuyla açılır (farklı nedenle 409 yok) ve çözülünce kanıtla kapanır; `ResolveByHuman` eklendi; ihtiyaç açma ve görev kapatma tek yüklem (`Issue.SatisfyingDecision`); "istisna tükendi/süresi doldu → `Reopened`" ifadeleri `Acknowledged → Open` olarak düzeltildi (`Reopened` yalnız `Resolved`'dan); A/B satırı `Open` durumunu gösterir; süresi dolmuş onayın süpürülmesi bilinçli istisna olarak yazıldı; `Approve` süre dolması dalı sürüm artırır.

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
| `gate_intent_mismatch` | 400 | kapı sorgusu (v1.3) | Kapı kataloğu bu niyeti veya hedef türünü bağlamıyor |
| `resolution_evidence_invalid` | 422 | insan çözümü (v1.3) | Sahip cevabı hiçbir sonucu kanıtlamıyor (`NotFound`) |
| `owner_outcome_port_unavailable` | 409 | insan çözümü (v1.3) | Sahip komutunda `IIdempotencyOutcomeV1` yok; `Failed` kapatılamaz |
| `review_not_required` | 409 | inceleme çözümü (v1.3) | Önerinin `ReviewState`'i `Required` değil |
| `closure_evidence_required` | 422 | iş öğesi kapanış | Kanıt referansı yok |

Sahip modül kodlarının adaptör eşlemesi (v1.3; `OwnerRefusalKind`, §5.1): **`SameKeyMismatch`** ← `same_key_payload_mismatch` (Sales/Purchasing/Logistics), `idempotency_conflict` (Counterparty/Inventory/Accounting/Manufacturing), Finance gövde `outcome: "IdempotencyConflict"`. **`RecordedForKey`** ← sahibin bu key'e terminal kaydettiği ret: `IsReplay=true`/`Idempotency-Replayed` taşıyan her ret; sahibin sözleşmesinde "terminal ret deneme satırına yazılır" diye belgelenen kodlar (Purchasing `version_conflict`, Accounting `accounting_version_conflict`, Manufacturing `manufacturing_version_conflict`, Sales ADR-011 §7 terminal retleri — §10.6; kod listesi sahip dilimde DOĞRULANACAK); key fence cevabı. **`UnrecordedRefusal`** ← diğer her 4xx: fence/403, `*_concurrent_change` (kazanan yok, kayıt yazılmadı), sahip deneme satırına yazmadığı `version_conflict`, geçici doğrulama — **yalnız sahip dilimde belgelenmiş açık liste**. **Eşlenemeyen kod `Unknown`'dır** (v1.3 iç kontrol; öneri kilitli kalır, probe/insan çözer; adaptör eksikliği security-event üretir) — tek gönderimde bile hedefi serbest bırakmaz. `UnrecordedRefusal` genel olarak `Failed` **değildir**: yalnız `SendsIssued=1` ve satır o gönderimden beri değişmemişse kesindir; aksi halde deneme reddidir (`Unknown` + kilit). Adaptör hiçbir kodu doğrudan `Failed`'a eşlemez; eşleme yalnız `OwnerOutcomeKind` üretir, karar `ApplyOwnerOutcome`'dadır.

## Ek K-3 — Dosya listesi (ÖNERİ; iskelet + Faz 1–4 ilk set)

**Backend — `Modules/Work`:** `Contracts/{WorkItemV1,IssueV1,GateDecisionV1,IWorkItemReadsV1,IWorkItemCommandsV1,WorkPermissionManifestV1}.cs` · `Domain/{WorkItems/WorkItem,WorkItemEvent,ApprovalRequest,EscalationPolicy,VisibilityMatrix; Issues/Issue,Evaluation,IssueKey; Rules/ImpactRuleDefinition,EffectKind,RuleTrigger,DisclosureMatrix; WorkRuleException,WorkCodes}.cs` · `Application/{IWorkItemService,IWorkItemStore,IImpactRuleGate,IImpactRuleTracker,IWorkRulesStore,IImpactRuleEvaluatorPort,IEscalationScheduler,WorkCommandMetadata,WorkWriteEnvelope,WorkProblemCodesV1,WorkItemService}.cs` · `Infrastructure/{WorkModuleExtensions,WorkShellCapabilityProvider,Persistence/WorkDbContext,WorkRows,WorkStore,WorkAuditEventTenantGuard,WorkDbContextDesignTimeFactory,Migrations/*}.cs` · `Rules/{ImpactRuleGate,ImpactRuleTracker,RuleScopeFanOut,IDecisionScopeSource,IGateCatalog,GateCatalog,GateCatalogManifestV1,IImpactRuleCatalog,ImpactRuleCatalog,Catalog/IrLog001…,IrSal001…,IrPur003…,IrFin002…,IrInv003…,IrAcc001…,IrDoc001…}.cs` · `Infrastructure/Persistence/{WorkRows (IssueRow, TrackHeadRow, EvaluationRow, ShadowEvaluationRow, WorkItemRow, WorkItemEventRow, WorkAttemptRow, EscalationPolicyRow, ApprovalRequestRow, ImpactRuleRow, SchedulerLeaseRow)}.cs`
**Backend — `Modules/Ai`:** `Contracts/{Tools/IAiTool,AiToolDescriptor,AiToolContext,AiDispatchInfo,AiToolResult,MeasuredValue,AiEnums; Proposals/ProposalV1,ApprovalV1; Audit/AiContext; AiToolCatalogManifestV1}.cs` · `Domain/{Runs/AiRun,AiRunStep,AiRunBudget,AiContextManifest; Proposals/AiProposal,AiProposalCodes,AiApproval,OwnerOutcome,AiProposalFact,ApprovalPolicy,OwnerRecordEvidence; Knowledge/AiKnowledgeItem,AiKnowledgeVersion; Policy/AiPolicyVersion; Usage/AiBudget; Workflow/AiJob,WorkflowInstance,WorkflowState}.cs` · `Application/{Ports/*.cs (§2.1 listesi), Policy/AiPolicyEvaluator,Steps/*.cs, Context/AiContextBuilder,AiContextPlans, Output/AiOutputValidator,AiMeasureRenderer,AiMeasureDictionary, Preview/AiPreviewService,AiCanonicalJson,AiApprovalBinding, Dispatch/AiEnvelopeVerifier,AiCommandDispatcher,AiRecoveryCoordinator,AiDispatchScope,IAiOwnerOutcomeRegistry,IOwnerApprovalPolicyResolver, Orchestration/AiOrchestrator, Providers/IAiModelClient,AiModelOutcome,AiModelRouter, AiOptions,AiCommandMetadata,AiProblemCodesV1,AiServiceException}.cs` · `Infrastructure/{AiModuleExtensions,Persistence/AiDbContext,AiRows (… ProposalRow, CommandAttemptRow, AiOutboxRow …),AiRunStore,AiProposalStore,AiPayloadVault,Migrations/*; Providers/FakeAiModelClient,HttpJsonSchemaModelClient,AiEgressAllowlistHandler,AiSecretProvider; Prompts/AiPromptRegistry; Knowledge/AiKnowledgeIndex; Security/AiSecurityEventSink}.cs` · `Workflow/{AiWorkflowWorker,JobPoller,SchedulePlanner,InboxConsumer,WorkflowRunner,Definitions/*.cs,AiWorkflowExtensions}.cs` (v1.3: `EscalationTicker` Work'e, `RecoverySweeper` `AiRuntime/Recovery`'ye taşındı)
**Backend — `GoymenERP.Api`:** `RulesRuntime/{RulesRuntimeExtensions,RulesEndpoints,RulesOutboxConsumer,Evaluators/<Owner>/*.cs, Scopes/IrLog001ScopeSource.cs, Scheduling/{WorkRulesWorker,RuleScheduleSweep,ReconciliationSweep,EscalationTicker,RulesInboxConsumer,SchedulerLeaseStore}.cs}` · `AiRuntime/{AiRuntimeExtensions,AiEndpoints,AiCapabilityEndpoints,AiShellCapabilityProvider,AiCommandEnvelopeFactory,AiDispatchScopeFactory,AiDispatchUser,AiReadSnapshotRunner,Tools/<Owner>/*Tool.cs, OwnerOutcome/<Owner>OutcomeReader.cs, ApprovalPolicy/<Owner>ApprovalPolicy.cs, Recovery/{AiRecoveryWorker,RecoverySweeper,AiWorkBridge,AiTakeoverReconciler}.cs}` · **Sahip dilim ekleri (ÖNERİ; sahip modülde yazılır):** `IIdempotencyOutcomeV1` (Find + Fence), `IOwnerApprovalPolicyV1`, `ILogisticsOpenInstructionReadsV1`, salt-okunur `*ReadParticipant` · `Shell/{C01E2eShellCapabilityActivation (ek satırlar), ShellCapabilityRegistryAuthorities (yeni sabit)}.cs` · `Program.cs` (ek satırlar) · `GoymenERP.Api.csproj` (referanslar)
**Frontend:** `src/work/{capabilities,api,contract,workRoutes,WorkCenterPage,WorkItemDetailPage,GateDecisionCard,*.test.tsx}` · `src/ai/{capabilities,api,contract,aiRoutes,AiContextPanel,ProposalPreviewCard,MeasureText,AiCenterPage,AiQualityPage,AiPolicyPage,*.test.tsx}` · `src/shell/{capabilities.ts (ek), router.tsx (ek), AppShell.tsx (NAV ek), aiMountDrift.test.ts, workMountDrift.test.ts}`
**Testler:** `tests/GoymenERP.Work.UnitTests/*`, `tests/GoymenERP.Ai.UnitTests/*`, `tests/GoymenERP.Ai.EvalTests/{FakeProviderEvalTests, Fixtures/**}`, `tests/GoymenERP.Api.IntegrationTests/{Work,AiProposal,AiRun,AiWorkflow,AiSnapshot,AiMigration}SqlServerRelationalAcceptanceTests.cs`, `{Ai,Work,RulesGate}HttpAcceptanceTests.cs`, `tests/GoymenERP.ArchitectureTests/{WorkNeverReferencesAiTests,AiAndWorkNeverUseRawSqlTests,AiWriteToolsAreOnlyInvokedByDispatcherTests,SystemActorHasNoWriteToolsTests,DispatchPathHasNoTransportRetryTests,RecoveryDoesNotDependOnActiveCatalogForProbeTests,AiNeverCallsWorkCommandsOutsideBridgeTests}.cs`
**Docs (bu belgeyle birlikte yazılacak, kod değil):** `docs/adr/ADR-008-ai-gateway.md` (şablon), `docs/features/ERP-000P_WORK_CORE_v1.md` (ad ÖNERİ), `docs/ai/{IMPACT_RULES,MEASURE_DICTIONARY,TOOL_CATALOG}.md`, `docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json` (2 kenar), `contracts/openapi/goymen-erp-v1.json` (yeniden üretim).

---

*Bu belge yalnız analiz ve kodlama tasarımıdır; hiçbir dosya listesi, kod bloğu veya sıra bir geliştirme görevini başlatmaz. Repo, karar sicili, görev listesi, kod ve izinler bu belgeyle değişmez.*
