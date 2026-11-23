<!-- Ajan araştırma notu (22 Kasım 2026); karar değildir. Rapor yazarı için ham ama kaynaklı malzemedir. -->

# Bütün sistemi konuşarak yönetmek ("her şey yapay zekâ üzerinden"): teknik kalıplar ve .NET yapı taşları — Kasım 2026

**Kapsam ve yöntem**
- Tarih: 22 Kasım 2026. Repoda salt okuma yapıldı; yazılan tek dosya bu nottur. Web sorgularında şirket, kişi veya şirkete özgü veri kullanılmadı (yalnız genel terimler).
- Önce okunanlar (tekrar edilmedi): `docs/delivery/AI_TREN_PLANI_v2.md`, `docs/delivery/arastirma/2026-11-22_kanitlanmamis_ai_yontemleri.md`. O iki belgenin ele aldığı konular (CaMeL/çift LLM, MCP araç zehirleme, hafıza mimarisi, çoklu ajan, pass^k, LLM hakem yanlılığı, otomasyon yanlılığı, telafi komutu, kazanılmış otonomi, AWM/Voyager) burada yalnız atıfla geçer.
- Repodan bağlam için bakılanlar (değiştirilmedi): `src/backend/GoymenERP.Api/Ai/*`, `BuildingBlocks/.../Ai/AiToolContractsV1.cs`, `Api/Shell/ShellCapabilityRegistry.cs`, paket referansları.
- Komşu konular (kurumsal ERP asistanları, model sağlayıcı/altyapı/KVKK, iş kutusu ve onay kalıpları, değer kanıtı) aynı klasördeki diğer notların kapsamındadır; bu not onlar okunmadan yazıldı ve o alanlara girmedi.

**Kanıt düzeyleri (görev tanımındaki ölçek)**
- **A:** hakemli yayın veya bağımsız denetim. **B:** analist ya da bağımsız benchmark. **C:** satıcı/proje belgesi, vakası veya iddiası; rakam içeriyorsa **[İDDİA]**. **D:** spekülatif. **M:** mevzuat (teyit gerekir).
- Protokol spesifikasyonları ve resmî ürün belgeleri C sayıldı; bir davranış iddiası değil sözleşme tanımı olduklarında "normatif" notu eklendi. NuGet paket kayıtları olgusal kayıttır; düzey atanmadı, "kayıt" diye işaretlendi.

**Kaynağa erişim etiketleri (her bulguda)**
- **[BK]** birincil kaynak bu oturumda açılıp okundu. **[AÖ]** yalnız arama özeti (snippet) — tam metin okunmadı. **[YP]** Anthropic'in Claude API başvuru paketi (yerel kopya, önbellek tarihi 2026-11-03); canlı sayfa ayrıca açılmadı.

**Bu araştırmanın sınırları (önemli)**
- Oturumun ağ izin listesi dardı: `arxiv.org`, `developers.openai.com`, `learn.microsoft.com`, `devblogs.microsoft.com`, `modelcontextprotocol.io`, `opentelemetry.io`, `docs.vllm.ai`, `docs.temporal.io`, `rfc-editor.org` açılamadı. Bunların yerine aynı içeriğin GitHub'daki kaynak dosyaları (`raw.githubusercontent.com`), NuGet kayıt API'si, `anthropic.com`, `platform.claude.com`, `code.claude.com` ve `blog.modelcontextprotocol.io` okundu.
- Web arama kotası (tur başına 200, bütün ajanlarca paylaşılan) araştırmanın ortasında bitti. Bu yüzden bazı sorular (her bölümün "Gaps" başlığında) aramayla kapatılamadı; ezberden rakam yazılmadı.
- arXiv makalelerinin hiçbiri tam metin okunmadı; hepsi [AÖ].

---

## 1. Araç kataloğunu ölçekleme: yüzlerce operasyon, araç araması, ad alanları, MCP, OpenAPI'den araç üretimi

### Takeaway
Yüzlerce operasyonu tek listede modele vermek hem bağlamı tüketiyor hem araç seçimini bozuyor; 2025–2026'da sağlayıcıların ortak cevabı "ertelenmiş yükleme + araç araması + ad alanı" ve "az sayıda, görev düzeyinde araç". OpenAPI'den otomatik araç üretimi prototip için uygun, üretim için değil: sözleşme şemanın kaynağı olmalı, araç yüzeyi elle seçilmeli. Yerel (açık ağırlıklı) modelde barındırılan araç araması yok; aramayı kendi kataloğumuz üzerinde bizim yazmamız gerekir.

### Cited Findings
**Sağlayıcı rehberi ve ölçümler**
- Anthropic araç araması belgesi: özellik "stable", bütün güncel modellerde ve platformlarda var. "Claude's ability to pick the right tool degrades once you exceed 30–50 available tools." Tipik çok sunuculu kurulum tanımları ~55k token; araç araması bunu "over 85 percent" azaltır ve istek başına 3–5 araç yükler. İstek başına en çok 10.000 ertelenmiş araç; arama varsayılan 5 sonuç döndürür; "Keep your 3–5 most frequently used tools non-deferred"; 10+ araç veya 10k+ token tanım varsa kullan. Araç adı, açıklaması, argüman adı ve argüman açıklaması aranır. **C [İDDİA] [BK]** — [Anthropic, Tool search tool](https://platform.claude.com/docs/en/agents-and-tools/tool-use/tool-search-tool)
- Aynı belge: kendi arama mantığınızı (ör. gömme/embedding tabanlı) yazıp sonucu `tool_reference` bloklarıyla döndürebilirsiniz; `defer_loading` istek başındaki önek dışında tutulduğu için istem önbelleğini bozmaz; `strict` gramer bütün araç listesinden derlenir. **C [BK]** — [Anthropic, Tool search tool](https://platform.claude.com/docs/en/agents-and-tools/tool-use/tool-search-tool)
- Anthropic mühendislik yazısı (24 Kasım 2025; 2025 tarihli, eski sayılır): iç MCP değerlendirmesinde araç aramasıyla Opus 4 %49 → %74, Opus 4.5 %79,5 → %88,1; çalışma başlamadan tüketilen bağlam ~77K → ~8,7K token ("85% reduction"); araç kullanım örnekleri karmaşık parametrede %72 → %90; programatik araç çağrısı ortalama 43.588 → 27.297 token (%37). Hepsi iç ölçüm, bağımsız tekrar yok. **C [İDDİA] [BK]** — [Anthropic, Advanced tool use](https://www.anthropic.com/engineering/advanced-tool-use)
- Üçüncü taraf ölçümü: Spring AI'nin ön benchmark'ı OpenAI, Anthropic ve Gemini modellerinde %34–64 token azalması bildiriyor (başlıktaki %85'in altında). **C [İDDİA] [AÖ]** — [Spring AI blog, 11 Aralık 2025](https://spring.io/blog/2025/12/11/spring-ai-tool-search-tools-tzolov/)
- OpenAI: araç araması Responses API'de `gpt-5.4` ve sonrası modellerde; `tool_search` aracı + `defer_loading: true`; ad alanı (namespace) önerilir; barındırılan ve istemcinin yürüttüğü iki kip var. **C [AÖ]** — [OpenAI, Tool search guide](https://developers.openai.com/api/docs/guides/tools-tool-search)
- OpenAI Agents SDK belgesi: "As a rule of thumb, keep each namespace fairly small, ideally fewer than 10 functions"; "Prefer namespaces or hosted MCP servers over many individually deferred functions"; barındırılan araç araması "available only with OpenAI Responses models". **C [BK]** — [openai-agents-python, docs/tools.md](https://raw.githubusercontent.com/openai/openai-agents-python/main/docs/tools.md)
- OpenAI Cookbook (Mayıs 2025; eski): "any setup with fewer than ~100 tools and fewer than ~20 arguments per tool is considered in-distribution and should perform within expected reliability bounds". **C [BK]** — [OpenAI Cookbook, o3/o4-mini prompting guide](https://raw.githubusercontent.com/openai/openai-cookbook/main/examples/o-series/o3o4-mini_prompting_guide.ipynb)
- Claude Code (üretimdeki bir istemci) varsayılanı: MCP araç araması açık; `auto` kipinde araç tanımları bağlam penceresinin %10'unun altındayken peşin yüklenir, %10 ve üstünde ertelenir; peşin yüklenen araç açıklaması 4.096, aramayla yüklenen 16.384 karakterde kesilir; MCP araç çıktısı varsayılan en çok 25.000 token, 10.000 token'da uyarı. **C [BK]** — [Claude Code docs, MCP](https://code.claude.com/docs/en/mcp)
- İstemci sınırları: Cursor toplam 40 MCP aracı, GitHub Copilot sohbet isteği başına 128 araç; ~200 uç noktalı bir şartname ~200 araç ve 40.000–80.000 token şema üretir. **C [İDDİA] [AÖ]** — [Speakeasy blog](https://www.speakeasy.com/blog/generating-mcp-from-openapi-lessons-from-50-production-servers); [Truto blog](https://truto.one/blog/auto-generating-mcp-tools-from-openapi-specs-an-end-to-end-architecture-guide/)

**Araç sayısına karşı seçim doğruluğu (ölçüm)**
- RAG-MCP (arXiv 2505.03275, Mayıs 2025, hakemsiz ön baskı): tüm katalogla araç seçim doğruluğu %13,62; getirmeyle süzülmüş listeyle %43,13; istem token'ı %50'den fazla azalıyor. İkincil bir özet 5 araçta %85 → 20 araçta %45 düşüş eğrisi veriyor; bu eğri makale metninden doğrulanamadı. **C (hakemsiz) [İDDİA] [AÖ]** — [RAG-MCP, arXiv 2505.03275](https://arxiv.org/abs/2505.03275)
- MCP-Bench (Accenture; NeurIPS 2025 atölyesi; 28 MCP sunucusu): genel puan gpt-5 0,749; o3 0,715; gpt-oss-120b 0,692; gemini-2.5-pro 0,690; claude-sonnet-4 0,681; qwen3-235b 0,678; gpt-oss-20b 0,654; qwen3-30b 0,627; gpt-4o 0,595; llama-3.3-70b 0,558; mistral-small-2503 0,530; llama-3.1-8b 0,428. Puan kural tabanlı şema kontrolü ile LLM hakemin (o4-mini) ortalamasıdır; modeller 2025 kuşağı. **B [BK]** — [MCP-Bench README](https://raw.githubusercontent.com/Accenture/mcp-bench/main/README.md)
- BFCL V4 (Temmuz 2025): "As single-turn tasks approach saturation" gerekçesiyle genel puan ağırlığı ajan görevleri %40, çok turlu %30, Live / Non-Live / Irrelevance %10'ar olacak şekilde değişti. **B [BK]** — [BFCL changelog](https://raw.githubusercontent.com/ShishirPatil/gorilla/main/berkeley-function-call-leaderboard/CHANGELOG.md)

**Ham uç nokta mı, görev düzeyi araç mı**
- "More tools don't always lead to better outcomes"; API uç noktasını sarmalamak yerine "a few thoughtful tools targeting specific high-impact workflows": `list_users` + `list_events` + `create_event` yerine `schedule_event`, `read_logs` yerine `search_logs`. Ad alanı öneki/soneki seçiminin değerlendirmede "non-trivial effects" yarattığı, etkinin modele göre değiştiği belirtiliyor. `response_format` (concise/detailed) ile aynı cevap 206 yerine 72 token. Tutulan (held-out) test setiyle aşırı uyum kontrolü. (11 Eylül 2025; eski.) **C [BK]** — [Anthropic, Writing effective tools for agents](https://www.anthropic.com/engineering/writing-tools-for-agents)
- "One of the most common failure modes we see is bloated tool sets"; ölçüt: "If a human engineer can't definitively say which tool should be used in a given situation" modelden de beklenmez. (29 Eylül 2025; eski.) **C [BK]** — [Anthropic, Effective context engineering](https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents)
- Büyük bir MCP sunucusunun kendi tasarımı: araçlar "toolset" gruplarına ayrılmış; "Enabling only the toolsets that you need can help the LLM with tool choice and reduce the context size"; `--read-only` açıkça istenen yazma araçlarından bile önceliklidir; "lockdown mode" güvenilmeyen içeriğe karşı en-iyi-çaba süzgecidir ve "It is **not** an authorization boundary". **C [BK]** — [github-mcp-server README](https://raw.githubusercontent.com/github/github-mcp-server/main/README.md)

**OpenAPI'den araç üretimi: yarar ve hata modları**
- Bildirilen sorunlar: araç sayısı patlaması; `createContact` gibi genel `operationId`'ler; REST'in kaynak merkezli yapısı ile iş akışı odaklı araç tasarımı arasındaki uyumsuzluk; eksik/tutarsız şartname "primary cause of auto-generated MCP tool failures"; bir çerçevenin kendi belgesinin otomatik üretilen sunucuları "prototyping" için önerdiği aktarılıyor. Çareler: budama, etiket bazlı süzme, uç noktaları kaynağa göre kümeleme, yeniden üretimde kaybolmayan elle geçersiz kılma katmanı. **C [AÖ]** — [Making REST APIs Agent-Ready, arXiv 2507.16044](https://arxiv.org/html/2507.16044v3); [Speakeasy](https://www.speakeasy.com/blog/generating-mcp-from-openapi-lessons-from-50-production-servers); [Stainless](https://www.stainless.com/blog/lessons-from-openapi-to-mcp-server-conversion)
- Katı şema sınırı kataloğu doğrudan kısıtlar: istek başına en çok 20 `strict` araç, bütün katı şemalarda toplam 24 isteğe bağlı parametre ve 16 birleşim (union) tipli parametre; aşılırsa 400 "Schema is too complex for compilation". **C [BK]** — [Anthropic, Structured outputs](https://platform.claude.com/docs/en/build-with-claude/structured-outputs)

**MCP ve kullanıcıya göre araç listesi**
- MCP 2026-07-28: `tools/list` bağlantıya göre değişmez; "Servers **SHOULD** return tools from `tools/list` in a deterministic order to enable client-side caching and improve LLM prompt cache hit rates"; araç ek açıklamaları "untrusted unless they come from trusted servers". **C (normatif) [BK]** — [MCP 2026-07-28 changelog](https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/specification/2026-07-28/changelog.mdx); [MCP tools spec](https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/specification/2026-07-28/server/tools.mdx)
- Resmî MCP C# SDK'sı yetki süzgeçleri: liste işlemlerinde "the filters automatically remove unauthorized items from the results. Users only see tools, prompts, or resources they have permission to access"; tekil çağrıda "Access forbidden" hatası. **C [BK]** — [MCP C# SDK, filters.md](https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/main/docs/concepts/filters.md)

**Kodla araç birleştirme (hiyerarşik alternatif)**
- Araçları dosya sistemi üzerinde kod API'si olarak sunup modelin kod yazarak çağırması: 150.000 → 2.000 token (%98,7) iddiası; bedeli "secure execution environment with appropriate sandboxing, resource limits, and monitoring" ve "operational overhead and security considerations that direct tool calls avoid". (4 Kasım 2025; eski.) **C [İDDİA] [BK]** — [Anthropic, Code execution with MCP](https://www.anthropic.com/engineering/code-execution-with-mcp)
- OpenAI tarafında aynı fikir (Programmatic Tool Calling) için uyarı: "Approval-sensitive or high-impact tools are usually better kept as direct calls so a person can review each action before it becomes part of a larger program." **C [BK]** — [openai-agents-python, docs/tools.md](https://raw.githubusercontent.com/openai/openai-agents-python/main/docs/tools.md)

### Inferences
- ~450 HTTP rotasını 450 araç yapmak üç bağımsız kaynaktaki eşiklerin (30–50 araçta bozulma; ad alanı başına <10 işlev; ~100 araç "dağılım içi") hepsini aşar. Uygun yüzey: 5 civarı her zaman yüklü çekirdek araç + modül ad alanlı (`sales.*`, `purchasing.*` …) ertelenmiş görev araçları + bunların üzerinde arama.
- Yerel modelde barındırılan araç araması olmadığı için aramayı sunucu tarafında biz yaparız: araç tanımlayıcıları (`AiToolDescriptorV1`) üzerinde BM25 + gömme; sonuç en çok 5–10 araç olarak isteğe eklenir. Bu, RAG-MCP'nin ölçtüğü düzenektir; kazancı kendi değerlendirme setimizde ölçülmeden varsayılmamalı.
- Yerel sunucularda önek önbelleği (bkz. §8) baştan eşleşir. Araç listesini her turda değiştirmek önbelleği sıfırlar; bu yüzden çekirdek araçlar sabit ve sıralı kalmalı, bulunan araçlar konuşmanın sonuna eklenmeli ya da `catalog.search` → `catalog.invoke` biçiminde sabit üst-araç kalıbı kullanılmalı.
- Shell yetenek kaydı (kullanıcının görebildiği menü ve komutlar) kullanıcıya göre süzülmüş araç listesinin hazır kaynağıdır; MCP C# SDK'sının süzgeç anlamı ile aynı sonuç (görmediği aracı çağıramaz, listede de görmez) mevcut kayıt + `RequiredPermissions` ile elde edilir.
- OpenAPI sözleşmesinin doğru rolü: araç girdi şemasının komut DTO'suyla uyumunu sınayan sürüklenme (drift) testi ve araç adayı envanteri. Araç açıklaması, örnekleri ve gruplaması elle yazılır.
- Türkçe kullanıcı ifadeleriyle İngilizce araç adları arasındaki eşleşme arama kalitesini belirler; araç açıklamalarına Türkçe eş anlamlılar ve ekran adları eklenmeli (kaynak: araç aramasının açıklama metnini taraması; Türkçe için ölçüm yok).

### Gaps
- Araç sayısı arttıkça doğruluk eğrisini 2026 kuşağı açık ağırlıklı modellerde ölçen bağımsız çalışma bulunamadı. LiveMCPBench (arXiv 2508.01780) ve MCP-Universe büyük araç uzayını ölçüyor; README'lerinde başlık rakamı yok, makaleler açılamadı.
- OpenAI işlev çağırma rehberindeki araç sayısı tavsiyesi (resmî sayfa) bu oturumda yeniden doğrulanamadı; yalnız 2025 tarihli Cookbook cümlesi okundu.
- vLLM veya llama.cpp'nin `tool_search`/`defer_loading` anlamını uygulayıp uygulamadığına dair kaynak bulunamadı (vLLM belgesi yalnız Responses API'de `namespace__function` adlandırmasından söz ediyor).
- Türkçe sorguyla araç getirme (retrieval) isabetine dair ölçüm yok.

---

## 2. "Git ve doldur" (ekranı aç, alanları doldur, kullanıcı kaydetsin) ile "çalıştır" (asistan komutu yürütsün): kalıplar, önizleme kartları, onay, kuru çalıştırma, geri alma, idempotent eylem

### Takeaway
Sahadaki ERP asistanları ikisini birlikte kullanıyor: etkileşimli işte asistan değeri öneri olarak alana yazar ve kullanıcı kabul edene kadar hiçbir şey kaydedilmez; arka plan ajanında önce zararsız bir ara belge (taslak/teklif) üretilir ve insan adım adım "Onayla" der. Onay kapısı kütüphanenin ya da protokolün değil, komut hattının işidir: incelenen çerçevelerin hepsi "onay gerekir" bilgisini taşır ama zorlamaz. 2026'da arayüz protokolleri (AG-UI 1.0, MCP Apps, A2UI 1.0) kararlı sürüme geldi; .NET barındırma parçaları hâlâ önizlemede.

### Cited Findings
**"Git ve doldur" — ürün örnekleri**
- Business Central otomatik doldurma (önizleme; belge tarihi 2 Ekim 2026): "Copilot never saves suggestions automatically but presents them for you to decide what to keep"; öneriler italik yazılır, alan bazında kabul/ret, "Keep all"; kabul edilmemiş öneri varken sayfadan çıkış engellenir; iş mantığının atadığı varsayılan değer Copilot önerisini ezer. Özellik şu alanlarda **kapalı**: kurulum (setup) sayfaları, liste ve çalışma sayfaları, maskeli alanlar (parola/sır), tarih-saat, para birimi kodu, kayıt kimliği ve numara serisi alanları, aç/kapa anahtarları. **C [BK]** — [Business Central, Autofill fields with Copilot](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/autofill-fields-with-copilot.md)
- Business Central ajan gözetimi (belge tarihi 10 Kasım 2026): görev zaman çizelgesi (Tasks pane) + sayfanın üstünde "data review bar"; hangi ajanın hangi alanı değiştirdiği, gerekçe ("most frequently used value" gibi) ve güven düzeyi (beyaz/turuncu simge) alan bazında görünür; çubuk, kullanıcı ajanı kullanma yetkisine sahip olmasa bile görünür; "Done" seçeneği "records that you reviewed the document (including your name and the date)". **C [BK]** — [Business Central, Supervise agent activities](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/supervise-agent-tasks.md)
- Power Apps model güdümlü uygulamalar: öneriler isteğe bağlıdır ve kullanıcı kabul edene kadar kaydedilmez; Microsoft'un başvuru mimarisi "Agent responses never autowrite to Dataverse. The user must review and accept" diyor. **C [AÖ]** — [Power Apps, form fill assistance](https://learn.microsoft.com/power-apps/user/form-filling-assistance); [Power Platform reference architecture](https://learn.microsoft.com/power-platform/architecture/reference-architectures/contextual-ai-model-driven-app)

**"Çalıştır" — ürün örneği ve karşı örnek**
- Business Central Sales Order Agent: "The agent always creates a sales quote as the first step, even when the customer asks for an order. Sales quotes have no impact on planning, reservations, availability calculations, or cash flow forecasts, which makes them a safe intermediate document for AI-assisted processing"; ajan yöneticinin atadığı izin ve profil içinde çalışır. **C [BK]** — [Business Central, Sales Order Agent](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/sales-order-agent.md)
- Aynı ajan şemalı API yerine arayüz üst verisini kullanır: "starting from the designated Role Center, navigates pages, invokes UI actions, and enters data as a user would"; adımlar "aren't hardcoded". Bu, bizim "yalnız şemalı backend aracı" değişmezimizin tersine bir tasarım tercihidir. **C [BK]** — [Business Central, Sales Order Agent](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/sales-order-agent.md)

**Arayüz protokolleri ve üretken arayüz**
- AG-UI 1.0: "stable, frozen specification backed by a JSON Schema"; TypeScript, Python ve .NET SDK'ları şemadan üretilir. Yetenekler: *Frontend tools* ("The agent calls tools that run in the app, and gets the result back"), *Human in the loop* ("Runs end with an `interrupt` outcome; the user approves, edits, or rejects, and the next run resumes"), *Shared state* (anlık görüntü + JSON Patch farkları), üretken arayüz. "Adopted by Google, Microsoft, Amazon, and Oracle" proje beyanıdır. **C [İDDİA] [BK]** — [AG-UI README](https://raw.githubusercontent.com/ag-ui-protocol/ag-ui/main/README.md)
- .NET tarafı: `AGUI.Client` 1.0.1 (NuGet, 12 Ekim 2026) kararlı; Agent Framework'ün sunucu parçası `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` yalnız önizleme (1.28.0-preview). **kayıt [BK]** — [NuGet: AGUI.Client](https://www.nuget.org/packages/AGUI.Client); [NuGet: Microsoft.Agents.AI.Hosting.AGUI.AspNetCore](https://www.nuget.org/packages/Microsoft.Agents.AI.Hosting.AGUI.AspNetCore)
- MCP Apps (şartname 2026-01-26): araçlara bağlı etkileşimli arayüz (grafik, form, pano) `ui://` kaynağı olarak sunulur; "Mandatory iframe sandboxing with auditable communication"; CSP alanları bildirilir. **C [BK]** — [MCP Apps README](https://raw.githubusercontent.com/modelcontextprotocol/ext-apps/main/README.md); [MCP Apps spec](https://raw.githubusercontent.com/modelcontextprotocol/ext-apps/main/specification/2026-01-26/apps.mdx)
- A2UI 1.0: "A2UI expresses UI using _data_, not code. A schema for the data exchanged between the agent and the renderer enforces strict capability boundaries for the agent"; ajan arayüzü, uygulamanın sunduğu yerel bileşen kataloğundan birleştirir. 1.0 duyurusu Kasım 2026 (kaynaklarda 4 ve 11 Kasım olarak geçiyor). **C [BK]** README, duyuru tarihi **[AÖ]** — [A2UI README](https://raw.githubusercontent.com/google/A2UI/main/README.md); [Google Developers Blog](https://developers.googleblog.com/en/a2ui-10-build-delightful-fast-reliable-agentic-experiences/)

**Onay tasarımı: kim zorlar?**
- MCP: "there **SHOULD** always be a human in the loop with the ability to deny tool invocations"; uygulamalar onay istemi göstermeli. **C (normatif) [BK]** — [MCP tools spec](https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/specification/2026-07-28/server/tools.mdx)
- MCP araç ek açıklamaları (varsayılanlar: `readOnlyHint` false, `destructiveHint` true, `idempotentHint` false, `openWorldHint` true) yalnız ipucudur: "They aren't enforcement", "They don't make the model resist prompt injection"; istemci için öğüt: "keep your actual safety guarantees in deterministic controls". (16 Mart 2026.) **C [BK]** — [MCP blog, Tool annotations](https://blog.modelcontextprotocol.io/posts/2026-03-16-tool-annotations/)
- Microsoft.Extensions.AI kaynak kodu: `ApprovalRequiredAIFunction` "does not enforce the requirement for user approval; it is the responsibility of the invoker to obtain that approval before invoking the function." **C [BK]** — [dotnet/extensions, ApprovalRequiredAIFunction.cs](https://raw.githubusercontent.com/dotnet/extensions/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/Functions/ApprovalRequiredAIFunction.cs)
- OpenAI Agents SDK: araç başına `needs_approval` (sabit ya da çağrıya göre karar veren işlev); "Callable approval rules fail closed when the SDK cannot safely inspect the arguments"; bekleyen onaylar `interruptions` olarak döner, `RunState` serileştirilip sonra sürdürülür; "her zaman onayla" kararları ajana ve araca göre kapsamlıdır. **C [BK]** — [openai-agents-python, human_in_the_loop.md](https://raw.githubusercontent.com/openai/openai-agents-python/main/docs/human_in_the_loop.md)
- Anthropic Managed Agents izin politikaları: `always_allow` / `always_ask` / `auto`; `auto`'da sunucu her çağrıyı değerlendirir ve çalıştırır, reddeder ya da onay için duraklatır; "The server treats session content as material to assess, not instructions to follow". **C [YP]** — [Anthropic, Permission policies](https://platform.claude.com/docs/en/managed-agents/permission-policies)
- MCP elicitation: form kipi "flat objects with primitive properties only"; sunucular parola, API anahtarı, ödeme bilgisi gibi hassas veriyi form kipiyle **isteyemez**, URL kipi kullanılır; istemci kullanıcıya cevabı gönderme öncesi gözden geçirme imkânı vermelidir. **C (normatif) [BK]** — [MCP elicitation spec](https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/specification/2026-07-28/client/elicitation.mdx)

**Kuru çalıştırma / benzetim uç noktası**
- Kubernetes `dryRun=All`: istek kabul zinciri, doğrulama, birleştirme ve varsayılan atama aşamalarından geçer; "Kubernetes guarantees that dry-run requests will not be persisted in storage or have any other side effects"; kaydedilecek nihai nesne döner; yan etkili kabul denetleyicisi tetiklenecekse istek başarısız sayılır. **C (yerleşik pratik) [BK]** — [Kubernetes API concepts, Dry-run](https://raw.githubusercontent.com/kubernetes/website/main/content/en/docs/reference/using-api/api-concepts.md)
- Politika alanındaki karşılığı: ajanın önerdiği bütün yeni koşullu erişim politikaları "report-only" kipte oluşturulur ve etki görseliyle incelenir (ayrıntı §3). **C [BK]** — [Microsoft Entra, Conditional Access Optimization Agent](https://raw.githubusercontent.com/MicrosoftDocs/entra-docs/main/docs/security-copilot/conditional-access-agent-optimization.md)

**Yeniden deneme ve idempotent eylem**
- MCP 2026-07-28 akış yeniden bağlanmayı kaldırdı: "A broken response stream loses the in-flight request; clients **MUST** re-issue it as a new request with a new request ID." **C (normatif) [BK]** — [MCP 2026-07-28 changelog](https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/specification/2026-07-28/changelog.mdx)
- OpenAI Agents SDK, model program yazarak araç çağırdığında "stricter replay-safety boundary" uygular ve sağlayıcı tarafı otomatik yeniden denemeyi kapatır. **C [BK]** — [openai-agents-python, docs/tools.md](https://raw.githubusercontent.com/openai/openai-agents-python/main/docs/tools.md)
- MCP Tasks uzantısı: uzun işler için dayanıklı görev tanıtıcısı; durumlar `working`, `input_required`, `completed`, `failed`, `cancelled`; onay gerektiğinde görev `input_required` olur ve istemci `tasks/update` ile cevap verir. **C [BK]** — [MCP Tasks extension overview](https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/extensions/tasks/overview.mdx)

### Inferences
- "Her ekrana konuşarak ulaş" hedefinin en ucuz ve en düşük riskli hâli tek bir ön yüz aracıdır: `ui.navigate(routeId, params, draftId)`. Rota listesi shell yetenek kaydından, kullanıcıya göre süzülmüş olarak gelir; böylece ekran sayısı araç sayısını büyütmez. Bu, AG-UI'nin "frontend tools" kalıbının karşılığıdır.
- Doldurulacak değerler URL'ye yazılmamalı; sunucuda süreli bir taslak kaydı (`AiDraft`) tutulup ekrana kimliğiyle verilmeli. Gerekçe: URL uzunluğu, günlüklere sızan kişisel veri ve kurcalanabilirlik. Ekran taslağı her zamanki doğrulayıcıdan geçirerek gösterir; alan bazında "yapay zekâ önerdi" işareti ve kaynağı görünür (Business Central'ın gözden geçirme çubuğu örneği).
- Yapay zekâya doldurtulmayacak alanlar için Business Central'ın dışlama listesi iyi bir başlangıç varsayımıdır: sır/parola, kimlik ve numara serisi, para birimi, tarih, aç/kapa anahtarı ve kurulum sayfaları. Bizde buna para, miktar ve formül sonucu alanları eklenir (sayı deterministik calculator'dan gelir değişmezi).
- "Çalıştır" yolunda mevcut `AiPreviewV1` (kanonik komut JSON'u + fark + hedef sürüm + son kullanma) Kubernetes kuru çalıştırmasının ve report-only kipinin karşılığıdır. Onay, önizleme kimliğine bağlı tek kullanımlık olmalı; önizleme kimliği idempotency anahtarı olarak kullanılırsa "akış koptu, yeniden gönder" durumu (MCP'nin yeni kuralı) çift kayıt üretmez.
- Onay zorlaması kütüphaneye bırakılamaz (Microsoft.Extensions.AI sarmalayıcısı zorlamıyor; MCP ek açıklamaları ipucu). Kapı komut hattında, risk sınıfına (`AiToolRiskClassV1`) göre deterministik olmalı; onay kuralı argümanı çözemediğinde kapalı kalmalı (OpenAI SDK'sındaki "fail closed" davranışı).
- Üretken arayüzde modelin serbest arayüz üretmesine gerek yok: sabit bileşenli kartlar (önizleme, fark, kaynak listesi, "ekranda aç", "onayla") A2UI'nin "veri, kod değil; katalog uygulamanın" ilkesini zaten karşılar.
- İkisini birleştirme kuralı (öneri): Read → doğrudan cevap; Draft → `ui.navigate` + taslak; Simulate → calculator sonucu kartı; Write → önizleme kartı + kullanıcı onayı + komut hattı; onay komutları araç olarak hiç açılmaz (plan §2.4 ile aynı).

### Gaps
- "Git ve doldur" ile "çalıştır" yollarının hata oranını karşılaştıran bağımsız ölçüm bulunamadı; ürün belgeleri tasarımı anlatıyor, sonuç vermiyor.
- SAP, Salesforce ve Oracle asistanlarının gezinme/derin bağlantı kalıpları arama kotası bittiği için doğrulanamadı.
- Microsoft Agent Framework'ün araç onayı ve AG-UI insan-döngüde belgeleri (`learn.microsoft.com`) açılamadı; yalnız başlık düzeyinde görüldü.
- Geri alma (telafi komutu) önceki araştırma notunda ele alındığı için yeniden incelenmedi; yeni kanıt aranmadı.

---

## 3. Doğal dilden yapılandırmaya: ayar ve yetki değişikliğini sohbetle taslaklamak, şemayla doğrulamak, fark göstermek, yetkilinin onayı, sürümlü geri dönüş

### Takeaway
Üretimdeki en açık örnek aynı kalıbı uyguluyor: ajan politika **önerir**, yeni politika "yalnız raporla" kipinde doğar, mevcut politika ancak yetkili yönetici onaylarsa değişir, görüntüleyen roller işlem yapamaz. Akademik çalışmalar doğal dilden erişim politikası üretmenin doğrulayıcı (çözücü, istek kümesi) olmadan güvenilmez olduğunu ve doğal dil niyeti için çoğu zaman "doğru cevap" bulunmadığını vurguluyor. Dolayısıyla onaycıya modelin özeti değil, backend'in hesapladığı etki gösterilmelidir.

### Cited Findings
- Microsoft Entra Conditional Access Optimization Agent (belge tarihi 23 Haziran 2026): "The agent doesn't make any changes to existing policies unless an administrator explicitly approves the suggestion. All *new* policies that the agent suggests are created in report-only mode." **C [BK]** — [Entra, Conditional Access Optimization Agent](https://raw.githubusercontent.com/MicrosoftDocs/entra-docs/main/docs/security-copilot/conditional-access-agent-optimization.md)
- Rol ayrımı: Security Reader ve Global Reader önerileri görür ama "can't take any action"; Conditional Access Administrator ve Security Administrator işlem yapabilir. **C [BK]** — [aynı belge](https://raw.githubusercontent.com/MicrosoftDocs/entra-docs/main/docs/security-copilot/conditional-access-agent-optimization.md)
- Çalışma sınırları: tek çalıştırmada en çok 300 kullanıcı ve 150 uygulama, 40 benzer politika çifti; tarama 24 saatlik pencereyle sınırlı; başladıktan sonra durdurulamaz; "You can't customize or override suggestions from the agent"; ajan "reviews previous suggestions so that it won't suggest the same policy again". **C [BK]** — [aynı belge](https://raw.githubusercontent.com/MicrosoftDocs/entra-docs/main/docs/security-copilot/conditional-access-agent-optimization.md)
- İnceleme ekranı: değişikliğin özeti **veya JSON ayrıntısı**, "Policy impact" grafiği (politikanın etkileyeceği örnek oturum açma olayları), ajanın tam etkinlik dökümü, diğer yöneticiler için not, "Mark suggestion as reviewed", "Snooze for 14 days", "Turn on policy"; ayrıca aşamalı yayılım planı ve 24 saatte bir ya da etkinliğe bağlı (önizleme) çalıştırma. **C [BK]** — [Entra, Review and apply suggestions](https://raw.githubusercontent.com/MicrosoftDocs/entra-docs/main/docs/security-copilot/conditional-access-agent-optimization-review-suggestions.md)
- Business Central özel ajanlarında yetki talebi: ajan başka bir izin kümesine ihtiyaç duyunca görevi duraklatır, önerilen izin kümelerini açıklamalarıyla gösterir; ajanı yapılandırma yetkisi olmayan kullanıcıda verme denetimleri görünmez ve yöneticiye başvurması gerekir. **C [BK]** — [Business Central, Supervise agent activities](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/supervise-agent-tasks.md)
- Aynı üründe yapay zekâ ile alan doldurma kurulum sayfalarında (ör. şirket bilgisi, genel muhasebe kurulumu) kapalıdır. **C [BK]** — [Business Central, Autofill fields with Copilot](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/autofill-fields-with-copilot.md)
- Akademik: Vatsa ve ark. (2025) LLM'lerle erişim denetimi politikası sentezini istek kümesine ve doğrulamaya karşı sınıyor; devam çalışması "significant hurdles remain" diyor ve doğal dil gereksinimlerinde "there often exists no ground truth specification against which to verify the LLM's interpretation before generation and deployment" uyarısını yapıyor. Sonuç rakamları okunmadı. **C (hakemsiz ön baskı) [AÖ]** — [arXiv 2503.11573](https://arxiv.org/abs/2503.11573); [arXiv 2510.20692](https://www.arxiv.org/pdf/2510.20692)
- NIST yazarlı çalışma (Lawal ve ark., 2025) doğal dil gereksinimini ABAC politikasına çevirmede istem mühendisliğini inceliyor; AutoCedar (arXiv 2607.03656) Cedar + SMT çözücüyle "verifier-guided" sentez öneriyor ve doğrulama planını geçmenin doğal dil niyetine uygunluğu ne kadar öngördüğünü ayrı soru olarak ele alıyor; insan inceleme süresini model maliyetinden ayrı ölçüyor. Rakamlar okunmadı. **B/C [AÖ]** — [NIST yayın sayfası](https://www.nist.gov/publications/translating-natural-language-specifications-access-control-policies-leveraging-large); [AutoCedar, arXiv 2607.03656](https://arxiv.org/pdf/2607.03656)

### Inferences
- Öneri serbest metin değil, sürümlü ayar politikasının şemasına karşı **tipli yama** olmalı: hedef politika, beklenen sürüm (expected version), değişen alanlar, gerekçe, kaynak konuşma. Ayar ekranının kullandığı doğrulayıcıdan geçmeyen öneri onay kutusuna hiç düşmez.
- Onaycıya gösterilecek "etki" deterministik hesaplanmalı: bu eşik son N günde kaç işlemi farklı sonuçlandırırdı; bu izin kimin hangi komut ve ekranlara erişimini açar; hangi görev ayrılığı (SoD) kuralıyla çakışır. Bu, report-only kipinin ve "Policy impact" grafiğinin bizdeki karşılığıdır. Modelin yazdığı özet yardımcıdır, kanıt değildir.
- Onaycı, öneriyi **ham hâliyle** (JSON/fark) de görebilmeli; Entra örneği iki görünümü birlikte sunuyor. Yalnız doğal dil özeti göstermek, modelin yanlış özetlediği durumda onayı anlamsızlaştırır.
- Yetki tarafında asistan yalnız **talep** üretir; grant/rol/Critical zaten insan kapısıdır (AGENTS.md). "İhtiyacı olan her şeyi ver" gibi geniş istekler en dar izin kümesine çevrilmeli ve belirsizse soru sorulmalı.
- Geri dönüş, önceki sürümün yeni sürüm olarak yeniden yayımlanmasıdır (silme yok ilkesi); öneri kartında "bu değişikliği geri alacak yama" hazır gösterilebilir.
- Tuzaklar: (a) düşük yetkili kullanıcının sohbetle hazırlattığı öneriyi yöneticinin okumadan onaylaması — kartta kimin, hangi konuşmadan taslakladığı görünmeli; (b) aynı önerinin tekrar tekrar gelmesi — reddedilen öneri parmak iziyle hatırlanmalı (Entra'nın yaptığı gibi) ve erteleme seçeneği olmalı; (c) öneri hazırlanırken politikanın başka biri tarafından değişmesi — beklenen sürüm uyuşmazlığı çakışma olarak dönmeli; (d) şirket kapsamı — öneri tek `CompanyId` ile sınırlı olmalı.

### Gaps
- ERP ayarı ya da yetkisi için doğal dilden öneri doğruluğunu ölçen bağımsız çalışma bulunamadı; akademik çalışmaların sonuç tabloları okunamadı.
- AWS, Google ve Okta tarafındaki benzer ürün kalıpları arama kotası nedeniyle incelenemedi.
- Onay yorgunluğu ve otomasyon yanlılığı önceki notta ele alındı; ayar/yetki bağlamına özgü ölçüm bulunamadı.

---

## 4. Kullanıcı tanımlı zamanlı ve olay güdümlü yapay zekâ görevleri; dış e-posta gönderme ve yanıt işleme

### Takeaway
Tüketici asistanları bile zamanlı görevi sıkı kotayla veriyor (kullanıcı başına bir avuç etkin görev, en sık saatte bir). Kurumsal örneklerde bütçe "yeni model isteğinden önce kapı" olarak uygulanıyor ve bütçe bitince iş öldürülmüyor, duraklatılıyor. .NET'te şirket içinde barındırılabilen seçenekler olgun (Quartz.NET, Hangfire, Temporal, Durable Task Framework + SQL Server); Agent Framework'ün dayanıklı uzantısı ise Kasım 2026'da yeni kararlı oldu ve Durable Task Scheduler'a bağlı. Dış e-postada sahadaki ürün iki kuralı sabitlemiş: giden her ileti insan onayından geçer, gelen ileti işlenmeden önce süzülür ve gönderen yalnız kayıtlı e-posta adresiyle eşleştirilir.

### Cited Findings
**Ürünlerde kota, bütçe ve duraklatma**
- ChatGPT zamanlı görevleri: plana göre 3–15 etkin görev tavanı (yardım sayfasının iki sürümünde Business için 10 ve 15 değerleri çelişiyor); görev saatte birden sık çalışamaz; tavana gelince yeni görev kurulamaz. Gemini'de en çok 10 etkin zamanlı eylem (2025 basın haberi; güncelliği doğrulanmadı). **C [AÖ]** — [OpenAI Help, Scheduled tasks](https://help.openai.com/en/articles/10291617-scheduled-tasks-in-chatgpt); [TechRadar](https://www.techradar.com/computing/artificial-intelligence/geminis-new-scheduled-actions-feature-puts-catching-up-with-chatgpt-on-its-dayplanner)
- Anthropic Managed Agents zamanlı dağıtımları (beta): cron ifadesi + IANA saat dilimi, en ince ayrıntı dakika; her tetikleme yeni oturum açar ve başarılı ya da başarısız her deneme ayrı bir "deployment run" kaydı üretir. Tasarım ayrıntıları: yük dağıtmak için gerçek çalışma zamanına "jitter of up to 15 percent of the interval between runs, with a minimum of 5 seconds and a maximum of 9 minutes" eklenir; kuruluş başına en çok 1.000 zamanlı dağıtım; yaz saati geçişinde var olmayan saat tetiklenmez, iki kez yaşanan saat iki kez tetiklenir ("Schedule outside the 1–3 AM local window, or use UTC"); duraklatma sonrası "Missed triggers are not backfilled"; oturum açma hız sınırına takılan tetikleme yeniden denenmez, bir sonraki zamanda denenir; kurtarılamayan hata (arşivlenmiş ortam vb.) başarısız çalıştırma kaydı üretir ve dağıtımı kendiliğinden duraklatır; "A deployment runs one version of its agent and keeps it even when the agent changes"; zamanlamaya bağlamadan önce elle çalıştırma uç noktası var. **C [BK]** — [Anthropic, Scheduled deployments](https://platform.claude.com/docs/en/managed-agents/scheduled-deployments)
- Aynı platformda bütçe: dağıtımdaki tavan her çalıştırmaya ayrı ayrı kopyalanır (çalıştırmalar arası birikimli tavan değildir). Oturum bütçesi sert dolar tavanıdır: "Enforcement is a pre-request gate"; tavana ulaşan oturum sonlandırılmaz, `budget_reached` ile duraklar ve geçmişi korunur; tavanı aşan istek tamamlandığı için fiilî tutar iş parçacığı başına en çok bir model isteği kadar taşabilir; duraklamışken yalnız süren işi kapatan olaylar kabul edilir. Messages API'deki `task_budget` ise modelin kendini ayarladığı, öğüt niteliğinde token bütçesidir (en az 20.000). **C [BK]** dağıtım bütçesi, **[YP]** oturum bütçesi ayrıntıları — [Anthropic, Scheduled deployments](https://platform.claude.com/docs/en/managed-agents/scheduled-deployments); [Anthropic, Budgets](https://platform.claude.com/docs/en/managed-agents/budgets); [Anthropic, Task budgets](https://platform.claude.com/docs/en/build-with-claude/task-budgets)
- Bütçe bitince birikme tuzağı: Business Central Sales Order Agent kredisi bitince "stops processing new emails but stays active and scheduled. When credits become available again, the agent automatically resumes and processes all unprocessed emails that accumulated during the outage"; belge yenileme sonrası istenmeyen tüketime karşı ajanı kapatmayı öneriyor. **C [BK]** — [Business Central, Sales Order Agent](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/sales-order-agent.md)
- Adım ve süre sınırı: OpenAI Agents SDK'da `max_turns` aşılınca `MaxTurnsExceeded`; model ve araç çağrısı için ayrı zaman aşımı hataları; hata işleyiciyle denetimli son çıktı döndürülebilir. Aynı belge uzun bekleme, yeniden deneme ve süreç yeniden başlatma için dayanıklı yürütme bütünleşmelerini sayıyor: Dapr, Temporal, Restate, DBOS. **C [BK]** — [openai-agents-python, running_agents.md](https://raw.githubusercontent.com/openai/openai-agents-python/main/docs/running_agents.md)
- Olay güdümlü ve durdurulamayan çalıştırma örneği: Entra ajanı 24 saatte bir ya da "activity-based runs" (önizleme) ile tetiklenir; "After the agent starts, you can't stop or pause the run". **C [BK]** — [Entra, Conditional Access Optimization Agent](https://raw.githubusercontent.com/MicrosoftDocs/entra-docs/main/docs/security-copilot/conditional-access-agent-optimization.md)

**.NET'te şirket içinde barındırılabilen seçenekler (sürümler NuGet kaydından, 22 Kasım 2026)**
- Quartz.NET 4.8.1 (21 Kasım 2026); 4.0.0 kararlı sürümü 3 Eylül 2026. "Quartz.NET 4 targets .NET 10, and nothing else"; bağımlılık enjeksiyonu, barındırma, sağlık denetimi ve System.Text.Json serileştirme çekirdek pakette. **kayıt + C [BK]** — [NuGet: Quartz](https://www.nuget.org/packages/Quartz); [Quartz.NET README](https://raw.githubusercontent.com/quartznet/quartznet/main/README.md)
- Üçüncü taraf karşılaştırmalarının ortak yargısı (arama özetinde birleşik aktarıldı; hangi cümlenin hangi siteden geldiği ayrıştırılamadı): Quartz.NET zaman tabanlı zamanlayıcıdır (cron, takvim, kaçan tetik/misfire yönetimi, kümeli çalışma); Hangfire "kalıcılığı olan iş kuyruğu"dur ve dayanıklı iş akışı desteği sınırlıdır; Temporal dayanıklı yürütmede en güçlü seçenek sayılır ama ayrı altyapı ve daha yüksek karmaşıklık getirir. **C [AÖ]** — [C# Corner, durable execution frameworks karşılaştırması](https://www.c-sharpcorner.com/article/comparing-durable-execution-frameworks-for-net-distributed-applications/); [Code Maze, Quartz.NET ve Hangfire farkları](https://code-maze.com/chsarp-the-differences-between-quartz-net-and-hangfire/)
- Hangfire.Core 1.8.25 (28 Ağustos 2026): "fire-and-forget, delayed and recurring jobs"; "Backed by Redis, SQL Server, SQL Azure and MSMQ"; devam işleri (continuations) ve pano; LGPL v3. **kayıt + C [BK]** — [NuGet: Hangfire.Core](https://www.nuget.org/packages/Hangfire.Core); [Hangfire README](https://raw.githubusercontent.com/HangfireIO/Hangfire/main/README.md)
- Temporal .NET SDK: `Temporalio` 1.22.0 (10 Kasım 2026; 1.0.0 → 5 Aralık 2023); .NET Framework ≥ 4.6.2, .NET Core ≥ 3.1 ve .NET Standard ≥ 2.0 destekler. İnsan-döngüde için `[WorkflowSignal]`, `[WorkflowUpdate]` (doğrulayıcılı) ve dayanıklı zamanlayıcı (`Workflow.DelayAsync`). Kısıt: iş akışı kodu deterministik olmalı (G/Ç, sistem saati, `Task.Delay`, rastgelelik, sözlük üzerinde yineleme yasak) ve "Due to .NET's lack of a sandbox, there is not a good way to prevent non-deterministic calls, so developers need to be vigilant"; önerilen güvence tekrar oynatma (replay) testi. Sunucu ayrı bir servistir (MIT lisanslı). **kayıt + C [BK]** — [NuGet: Temporalio](https://www.nuget.org/packages/Temporalio); [Temporalio paket README](https://raw.githubusercontent.com/temporalio/sdk-dotnet/main/src/Temporalio/README.md); [Temporal sunucu README](https://raw.githubusercontent.com/temporalio/temporal/main/README.md)
- Temporal belgeleri: iş akışı "resumes automatically after a crash, a network timeout, or a multi-day wait for a human to approve a step"; insan adımına konan süre sınırı kesintilerden etkilenmez. Topluluk paketi `TemporalCommunity.Extensions.Agents` 0.12.1, .NET 10 ve Temporal Service ≥ 1.31.0 ister. **C [AÖ]** — [Temporal, Durable AI](https://docs.temporal.io/ai); [NuGet: TemporalCommunity.Extensions.Agents](https://www.nuget.org/packages/TemporalCommunity.Extensions.Agents)
- Durable Task ailesi üç ayrı şeydir: (1) `Microsoft.DurableTask.*` SDK'sı "specifically designed to connect to a 'sidecar' process" ve "Durable Task Scheduler provides durable execution in Azure"; (2) eski Durable Task Framework "supports running fully self-hosted apps using a storage-based backend like Azure Storage or MSSQL"; (3) SQL sağlayıcısı (`Microsoft.DurableTask.SqlServer` 1.9.0, 18 Kasım 2026) bütün durumu "in a Microsoft SQL database, which can be hosted in the cloud or in your own infrastructure" tutar. **kayıt + C [BK]** — [durabletask-dotnet README](https://raw.githubusercontent.com/microsoft/durabletask-dotnet/main/README.md); [durabletask-mssql README](https://raw.githubusercontent.com/microsoft/durabletask-mssql/main/README.md); [NuGet: Microsoft.DurableTask.SqlServer](https://www.nuget.org/packages/Microsoft.DurableTask.SqlServer)
- Agent Framework dayanıklı uzantısı: `Microsoft.Agents.AI.DurableTask` ilk kararlı sürümü 1.16.0, 9 Kasım 2026 (öncesi yalnız önizleme); `Microsoft.DurableTask.Client/Worker` ≥ 1.26.0'a bağımlı. README barındırmayı "Self-hosted / console / containers / AKS" diye adlandırıyor, ama bütünleşme testleri "Durable Task Scheduler emulator", Azurite ve Redis istiyor ve bağlantı `DURABLE_TASK_SCHEDULER_CONNECTION_STRING` ile kuruluyor. **kayıt + C [BK]** — [NuGet: Microsoft.Agents.AI.DurableTask](https://www.nuget.org/packages/Microsoft.Agents.AI.DurableTask); [agent-framework-durable-extension README](https://raw.githubusercontent.com/microsoft/agent-framework-durable-extension/main/README.md)
- Aynı uzantı için Microsoft'un anlatımı: her adımdan sonra otomatik denetim noktası; saatler, günler ya da haftalar süren insan onayı beklemeleri işlem gücü tüketmeden; Agent Framework iş akışlarında özel depolu `CheckpointManager`. **C [AÖ]** — [.NET Blog, Durable workflows in Microsoft Agent Framework (6 Mayıs 2026)](https://devblogs.microsoft.com/dotnet/durable-workflows-in-microsoft-agent-framework/); [Microsoft Learn, checkpointing and resuming](https://learn.microsoft.com/en-us/agent-framework/tutorials/workflows/checkpointing-and-resuming)
- Dapr Workflow (`Dapr.Workflow` 1.19.1, 10 Kasım 2026): başlat / sorgula / duraklat / sürdür / olay yükselt / sonlandır API'leri olan yerleşik iş akışı çalışma zamanı; Dapr yan arabası (sidecar) gerektirir. Elsa Workflows 3.10.2 (18 Kasım 2026): C#, görsel tasarımcı veya JSON ile tanımlanan, .NET uygulamasına gömülen iş akışı kitaplığı. **kayıt + C [BK]** — [Dapr workflow overview](https://raw.githubusercontent.com/dapr/docs/v1.16/daprdocs/content/en/developing-applications/building-blocks/workflow/workflow-overview.md); [Elsa README](https://raw.githubusercontent.com/elsa-workflows/elsa-core/main/README.md)

**Dış e-posta: gönderme ve yanıt işleme**
- Giden ileti onayı: "The agent always involves designated Business Central users to review and approve all outgoing messages before it sends them to customers." **C [BK]** — [Business Central, Sales Order Agent](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/sales-order-agent.md)
- Gelen ileti süzgeci: ajan gelen ileti ve ekleri işlemeden önce bir "relevance model"den geçirir; kapsam dışı iletileri, örneğin "attempts to access internal data", eler; her ek ayrı işlenir ve işlenemeyen ek diğerlerini engellemez. **C [BK]** — [aynı belge](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/sales-order-agent.md)
- Gönderen kimliği: "For security, the agent identifies contacts exclusively by matching the sender's email address to the **E-Mail** field on contact cards. It doesn't search by contact name, company name, or other fields when establishing the security context"; eşleşen müşterinin yalnız kendi belgeleri kullanılır; iletilen (forward) e-postada ileten kişinin adresi esas alınır; eşleşme yoksa insan üç seçenekten birini seçer (yeni kişi, bu seferlik başka kişi, kişinin adresini güncelle). **C [BK]** — [aynı belge](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/sales-order-agent.md)
- Mimari: belirlenmiş posta kutusunu sürekli izleyen, arka plan görevi olarak çalışan iç e-posta dağıtıcısı ajanı tetikler ve sonucu gönderir. **C [BK]** — [aynı belge](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/sales-order-agent.md)
- Yanıt zincirleme (.NET, MailKit 5.0.0 — 10 Kasım 2026): yanıtın `In-Reply-To` başlığı özgün iletinin `Message-Id` değeridir; `References` başlığı özgün `References` + özgün `Message-Id` olur; teslim durumu bildirimlerinde özgün ileti `Original-Message-Id` alanından bulunur. **kayıt + C [BK]** — [MailKit FAQ](https://raw.githubusercontent.com/jstedfast/MailKit/master/FAQ.md); [NuGet: MailKit](https://www.nuget.org/packages/MailKit)
- Posta kutusuna erişim: MailKit belgeleri Gmail için ve Office 365 paylaşılan posta kutuları için OAuth 2.0 ile IMAP/SMTP oturumu açmayı anlatıyor (temel parola yerine belirteç). **C [BK]** — [MailKit FAQ](https://raw.githubusercontent.com/jstedfast/MailKit/master/FAQ.md)
- Teslim edilebilirlik (posta sağlayıcı politikaları; satıcı bloglarından, ayrıntılarda çelişki var): Google ve Yahoo Şubat 2024'ten beri, Microsoft 5 Mayıs 2025'ten beri (outlook.com/hotmail.com/live.com) günde 5.000+ ileti gönderenlerden SPF, DKIM ve DMARC istiyor; Microsoft için DMARC en az `p=none` ve SPF ya da DKIM ile hizalı; Google ve Yahoo tek tıkla abonelikten çıkma ve ~2 gün içinde uygulama istiyor; şikâyet oranı sınırı çoğu kaynakta %0,3, bir kaynakta %0,1; Gmail'in kalıcı 550 reddi Kasım 2025'te sıkılaştırması planlanmıştı. Resmî sayfalardan teyit edilmeli. **C [AÖ]** — [Proofpoint](https://www.proofpoint.com/us/blog/email-and-cloud-threats/clock-ticking-stricter-email-authentication-enforcements-google-start); [Red Sift](https://redsift.com/guides/bulk-email-sender-requirements); [AWS Messaging Blog](https://aws.amazon.com/blogs/messaging-and-targeting/navigate-bulk-sender-requirements-with-amazon-ses)
- Gelen içerik riski: EchoLeak (CVE-2025-32711, CVSS 9,3) tek bir hazırlanmış e-postayla, kullanıcı tıklamadan, bir üretim asistanından veri sızdırdı; zincir enjeksiyon sınıflandırıcısını, bağlantı karartmayı, otomatik getirilen görselleri ve güvenilen bir vekil alan adı üzerinden içerik güvenlik politikasını aştı; sunucu tarafında düzeltildi, sahada sömürü kanıtı bildirilmedi. Çıkarılan dersler: güvenilen ve güvenilmeyen içeriğin istemde sert ayrımı, yanıta gömülü giden isteklerin engellenmesi ya da onaya bağlanması, çıktıda Markdown/görsel işlemenin kısıtlanması. **A/B (AAAI Symposium Series'te yayımlanan vaka çalışması) [AÖ]** — [arXiv 2509.10540](https://arxiv.org/abs/2509.10540); [AAAI-SS](https://ojs.aaai.org/index.php/AAAI-SS/article/view/36899)

### Inferences
- Planın AI-18 tanımı (yalnız okuma, kısa, sınırlı adım) için dayanıklılık gereksinimi "son tamamlanan adımdan devam"dır. Bunu görev tanımı + çalıştırma + adım tabloları ve idempotent adımlar karşılar; ayrı bir iş akışı sunucusu (Temporal kümesi, Dapr yan arabası, Durable Task Scheduler) bu kapsam için fazladır. Zamanlama, kaçan tetik ve kümeli çalışma için Quartz.NET 4 (.NET 10 hedefli; kalıcı iş deposu için SQL Server desteği kurulumdan önce resmî belgeden teyit edilmeli) ya da mevcut barındırılan servis kalıbı (repoda outbox dağıtıcısı ve saklama temizliği zaten böyle çalışıyor) yeterli görünüyor. Çok günlü, çok adımlı ve yazma içeren süreçler gündeme gelirse Temporal ya da Durable Task Framework + SQL Server o zaman değerlendirilir.
- Temporal'ın bedeli yalnız sunucu değil: deterministik iş akışı kodu disiplini ve tekrar oynatma testleri gerekir; .NET'te korumalı alan olmadığı için SDK'nın kendisi belirlenimsiz çağrıyı derleme zamanında yakalamaz (SDK dışı çözümleyiciler var); önerilen güvence tekrar oynatma testidir. Repodaki pin/envanter disiplinine bir katman daha ekler.
- Kota tasarımı için sahadaki değerler başlangıç noktası olabilir, ama hepsi **yapılandırılabilir** kalmalı (karar değil): kullanıcı başına etkin görev tavanı (tüketici ürünlerinde 3–15), en sık çalışma aralığı (saatte bir), çalıştırma başına adım/süre/token tavanı, kullanıcı ve şirket günlük bütçesi.
- Bütçe "her model isteğinden önce kapı" olarak uygulanmalı; tavan aşıldığında çalıştırma duraklatılıp sahibine bildirilmeli. Bütçe kesintisinden sonra birikmiş tetiklemeler topluca yeniden oynatılmamalı (Business Central'daki tuzak): kaçan tetik politikası "atla ve bildir" ya da "yalnız sonuncuyu çalıştır" olmalı.
- Zamanlayıcı ayrıntıları için hazır bir denetim listesi (Anthropic'in zamanlı dağıtım belgesinden uyarlanır; değerler yapılandırılabilir kalmalı): tetiklemeye küçük rastgele gecikme (aynı dakikaya yığılmayı önler); her deneme için ayrı çalıştırma kaydı (başarısızlık nedeni dahil); kaçan tetikleri geriye dönük çalıştırmama; kurtarılamayan hatada görevi kendiliğinden duraklatıp sahibine bildirme; görevin tarifin/aracın **sabit sürümüne** bağlı çalışması; zamanlamadan önce elle deneme çalıştırması; yaz saati geçişlerine karşı saat dilimini açıkça saklama (kullanıcı ya da muhatap şirket farklı saat diliminde olabilir).
- Zamanlı görev, oluşturulduğu andaki değil **çalıştığı andaki** yetkiyle çalışmalı; kullanıcı pasifleşince görevleri duraklamalı. Olay tetikleyicileri mevcut outbox olaylarından, inbox/tekilleştirme ile beslenmeli; olay fırtınasına karşı birleştirme (aynı varlık için N dakikada bir) gerekir.
- Dış e-posta için önerilen akış: (1) alıcı listesi ve şablon bir kez, toplu ekranda insan onayından geçer (plan AI-22 ile aynı); (2) her gönderim kendi `Message-Id`'siyle ve gönderim kaydıyla saklanır; (3) yanıt `In-Reply-To`/`References` ile eşlenir, eşleşmeyenler insan kuyruğuna düşer; (4) gönderen adresi kayıtlı ilgili kişi adresiyle birebir eşleşmeli ve alıcı sunucunun kimlik doğrulama sonucu (DMARC) olumlu olmalı — yalnız "From" eşleşmesi sahteciliğe açıktır; (5) gelen ileti karantinada şemalı alanlara çıkarılır (önceki notun CaMeL önerisi), bağlantı izlenmez, uzak görsel yüklenmez, HTML işlenmez; (6) rapor, çıkarılan şemalı yanıtlardan deterministik toplanır, düşük güvenli çıkarımlar insan incelemesine ayrılır.
- Otomatik yanıtlar ve geri dönen iletiler ayrı sınıflandırılmalı (standart başlıklar ve teslim durumu bildirimi); bu oturumda ilgili RFC metinleri açılamadı, MailKit belgesindeki `Original-Message-Id` örneği dışında kaynak gösterilemiyor.
- Ayrı bir gönderim alt alan adı ve SPF/DKIM/DMARC kurulumu, ana alan adının itibarını zamanlı toplu gönderimden korur (satıcı rehberlerinin ortak önerisi; hacim eşiğinin altında kalsak bile kimlik doğrulama beklenir).

### Gaps
- Agent Framework dayanıklı uzantısının Azure dışı (tamamen şirket içi) üretim arka ucu olup olmadığı doğrulanamadı; README yalnız Durable Task Scheduler ve onun emülatörünü gösteriyor. Microsoft Learn sayfası açılamadı.
- Temporal sunucusunun desteklediği kalıcılık veritabanları (SQL Server dahil mi) bu oturumda doğrulanamadı; karar öncesi resmî belgeden bakılmalı.
- Şirket içi Temporal/Dapr işletim maliyetine dair bağımsız veri bulunamadı.
- Posta sağlayıcı eşikleri yalnız satıcı bloglarından; Google, Yahoo ve Microsoft'un resmî sayfaları açılamadı.
- **M (teyit gerekir, bu notta araştırılmadı):** şirketlere zamanlı e-posta gönderimi ticari elektronik ileti sayılırsa 6563 sayılı Kanun ve İleti Yönetim Sistemi yükümlülükleri, ayrıca yanıtlardaki kişisel veriler için KVKK doğar; hukuk görüşü alınmalı.
- SMTP/IMAP ile sağlayıcı API'si (ör. posta sağlayıcısının REST API'si ve değişiklik bildirimleri) arasındaki seçim bu oturumda kaynakla karşılaştırılamadı; şirketin posta altyapısı belli olunca ayrıca bakılmalı.
- Yanıtlardan rapor üretiminin doğruluğuna (sınıflandırma, alan çıkarımı) dair ölçüm yok; pilotla ölçülmeli.

---

## 5. Metinden iş akışına ve tarifler: güvenilirlik kanıtı ve "tarif = kayıtlı araç çağrılarının sıralı listesi + şema + doğrulama adımı"

### Takeaway
Doğal dilden çalıştırılabilir iş akışı üretiminde kanıt olumsuz: 2026 tarihli benchmark, modellerin üst düzey niyeti yakaladığını ama doğru, kararlı ve çalışır iş akışını sıklıkla üretemediğini söylüyor. Sektörün yönü serbest iş akışı üretmek değil, "ihtiyaç anında yüklenen paketlenmiş yönerge + deterministik betik" (Agent Skills) ve çok adımlı işi tek görev aracına toplamak. Bizim için daha güvenli biçim, tarifin **veri** olması: sürümü sabitlenmiş kayıtlı araç çağrıları, parametre şeması ve deterministik doğrulama.

### Cited Findings
- Chat2Workflow (arXiv 2604.19667, Nisan 2026, hakemsiz): altı alanda 237 insan etiketli örnek; üretilen görsel iş akışı çalıştırılıp Pass Rate ve Resolve Rate ile ölçülüyor; otomatik onarımlı ajan taban çizgisi Resolve Rate'i %6,05 artırıyor ama "a large gap remains"; özet yargı: modeller üst düzey niyeti yakalıyor fakat doğru, kararlı, çalışır iş akışı üretmekte sıklıkla başarısız. Tablo rakamları okunmadı. **C (hakemsiz) [AÖ]** — [Chat2Workflow, arXiv 2604.19667](https://arxiv.org/abs/2604.19667)
- WorfBench (ICLR 2025; eski): GPT-4'te sıralı planlama ile çizge planlama arasında yaklaşık %15 fark; değerlendirme çalıştırmaya değil alt dizi/alt çizge eşleşmesine dayanıyor. **A [AÖ]** — [Benchmarking Agentic Workflow Generation, arXiv 2410.07869](https://arxiv.org/abs/2410.07869)
- Agent Skills (Anthropic; "stable"): beceri = yönerge + üst veri + isteğe bağlı betik/kaynak. Aşamalı yükleme: üst veri her zaman (~100 token/beceri), `SKILL.md` gövdesi tetiklenince (5k token altı), kaynaklar gerektiğinde; betikler "providing deterministic operations without loading their code into context". Ad en çok 64, açıklama en çok 1.024 karakter. Güvenlik: "Use Skills only from trusted sources"; kötü niyetli beceri aracı amacı dışında çağırtabilir. **C [BK]** — [Anthropic, Agent Skills overview](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/overview)
- Becerilerin protokol düzeyine taşınması: MCP Skills uzantısı (SEP-2640 "Final") sunucuların iş akışı yönergelerini ve destek dosyalarını sunmasını tanımlıyor; "SDK and host support is still being implemented". Resmî C# SDK'sında `ModelContextProtocol.Extensions.Skills` paketi "verifiable file manifests" ile var. **C [BK]** — [MCP Skills extension overview](https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/extensions/skills/overview.mdx); [MCP C# SDK README](https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/main/README.md)
- Tekrarlanan çok adımlı işi tek araca toplama önerisi (`schedule_event`, `search_logs` örnekleri): "a few thoughtful tools targeting specific high-impact workflows". **C [BK]** — [Anthropic, Writing effective tools for agents](https://www.anthropic.com/engineering/writing-tools-for-agents)
- Modelin program yazarak araç zincirlemesi korumalı alan ve izleme gerektirir; onay gerektiren araçların program dışında, tek tek çağrı olarak kalması önerilir. **C [BK]** — [Anthropic, Code execution with MCP](https://www.anthropic.com/engineering/code-execution-with-mcp); [openai-agents-python, docs/tools.md](https://raw.githubusercontent.com/openai/openai-agents-python/main/docs/tools.md)
- Sürümleme, dayanıklı yürütme motorlarında bilinen bir sorundur: Durable Task aynı adla birden çok sürümü `[DurableTask(Version = "...")]` ile kaydetmeyi ve belirli sürümü başlatmayı destekler; Temporal'da kod değişikliği tekrar oynatma testiyle doğrulanır. **C [BK]** — [durabletask-dotnet README](https://raw.githubusercontent.com/microsoft/durabletask-dotnet/main/README.md); [Temporalio paket README](https://raw.githubusercontent.com/temporalio/sdk-dotnet/main/src/Temporalio/README.md)
- Araç şeması değişince türetilmiş yapılar geçersizleşir: Anthropic'te derlenmiş gramer önbelleği şema yapısı ya da araç kümesi değişince geçersiz olur; yalnız `name`/`description` değişikliği geçersiz kılmaz. **C [BK]** — [Anthropic, Structured outputs](https://platform.claude.com/docs/en/build-with-claude/structured-outputs)

### Inferences
- Önerilen tarif biçimi (kod değil veri): `paramsSchema`, `steps[] = {toolId, toolVersion, contractRevision, argsTemplate, expect}`, `outputSchema`, `riskClass = adımların en yükseği`. Argüman şablonu yalnız tarif parametrelerine ve önceki adım çıktılarının adlandırılmış alanlarına bağlanır; serbest ifade dili yok. İlk sürümde döngü ve dallanma yok; en fazla "en çok N öğe için tekrarla" ve "koşul sağlanmazsa atla".
- Doğrulama üç katmanlı ve deterministik olmalı: (1) statik — her araç kullanıcının süzülmüş kataloğunda mı, tipler uyuyor mu, veri sınıfı akışı izinli mi; (2) kuru çalıştırma — örnek parametreyle okuma adımları gerçek, yazma adımları önizleme olarak; (3) çalışma zamanı — her adım sonrası `expect` (ör. toplamlar tutuyor, satır sayısı > 0). Zamanlı görevde yalnız okuma sınıfı tarif çalışır.
- Sürüm bağı: tarif, araç kimliği + sürümü + sözleşme revizyonunu (`AiContractRevisionV1`) saklar. Araç sürümü emekli olunca tarif "yeniden doğrulama gerekli" olur ve ona bağlı zamanlı görevler duraklar; kütüphanedeki tarifler her araç değişikliğinde değerlendirme CI'ında yeniden oynatılır.
- Üretim yolu: model, kısıtlı çözümlemeyle (§6) tarif JSON'u önerir; araç kimliği alanı kullanıcının kataloğundan getirilen adaylarla sınırlandırılır; statik doğrulama ve kuru çalıştırma geçerse kullanıcı onaylar; şirket kütüphanesine terfi ayrıca yönetici onayı ister (önceki notla aynı).
- Agent Skills kalıbından alınacak olan aşamalı yüklemedir: tarif kütüphanesinin yalnız ad + kısa açıklaması bağlamda durur (beceri başına ~100 token ölçüsü), gövde seçilince yüklenir. Serbest metin yönerge ve betik taşıyan beceriyi olduğu gibi almak ise "yalnız şemalı araç" değişmeziyle çelişir.
- Çok kullanılan ve kararlı bir tarif, sonunda modül sahibinin yazdığı tek bir görev aracına terfi ettirilmelidir (plan §7.8'deki "eksik araç talebi" ile aynı yön).

### Gaps
- "Sıralı kayıtlı araç çağrısı listesi" biçimindeki tariflerin güvenilirliğini serbest iş akışı üretimiyle karşılaştıran ölçüm bulunamadı; öneri mühendislik çıkarımıdır (D'ye yakın), pilotla ölçülmeli.
- Ticari otomasyon oluşturucularının (iş akışı üreten yardımcılar) doğruluk verisi arama kotası nedeniyle aranamadı.
- Chat2Workflow'un model bazlı rakamları okunamadı.

---

## 6. Güvenilir araç çağrısı için yapılandırılmış çıktı ve kısıtlı çözümleme (JSON Schema): büyük API'ler, yerel sunucular (vLLM, llama.cpp), küçük model yönlendiriciler, geri düşüş

### Takeaway
Kısıtlı çözümleme artık hem bulut API'lerinde hem yerel sunucularda var ve "ayrıştırılabilir, şemaya uygun çıktı"yı garanti ediyor; "doğru çıktı"yı değil. Yerel tarafta tuzak varsayılanlardadır: vLLM'de `tool_choice="auto"` ile, istemci `strict` işaretlemediyse hiç gramer uygulanmaz. Şema özellik desteği sunucudan sunucuya değişir ve desteklenmeyen özellik kimi yerde sessizce atlanır; bu yüzden tek, sade bir şema lehçesi ve sunucu tarafı doğrulama şart.

### Cited Findings
**Bulut API'si (Anthropic)**
- Yapılandırılmış çıktı ve katı araç kullanımı "stable", bütün modellerde. "Structured outputs guarantee schema-compliant responses through constrained decoding"; şema grameri derlenir, ilk kullanımda ek gecikme olur, derlenmiş gramer son kullanımdan itibaren 24 saat önbellekte kalır. **C [BK]** — [Anthropic, Structured outputs](https://platform.claude.com/docs/en/build-with-claude/structured-outputs)
- Desteklenmeyen şema özellikleri: özyinelemeli şema, `oneOf`, `not`, dış `$ref`, sayısal kısıtlar (`minimum`, `maximum`, `multipleOf`), dize uzunluk kısıtları, `minItems` için 0/1 dışı değerler; nesnelerde `additionalProperties: false` zorunlu. Karmaşıklık sınırları: 20 katı araç, toplam 24 isteğe bağlı parametre, 16 birleşim tipli parametre, 180 saniye derleme zaman aşımı. **C [BK]** — [aynı belge](https://platform.claude.com/docs/en/build-with-claude/structured-outputs)
- Garantinin istisnaları: güvenlik reddi (`stop_reason: "refusal"`) ve `max_tokens` kesilmesinde çıktı şemaya uymayabilir; `enum`/`const` değerlerinde büyük-küçük harf garantisi yoktur ("Compare enum values case-insensitively"). **C [BK]** — [aynı belge](https://platform.claude.com/docs/en/build-with-claude/structured-outputs)
- En yeni modellerde zorla araç seçimi (`tool_choice` `any`/`tool`) 400 döndürür; önerilen yol `auto` + `strict: true` ve isteği istemde belirtmek. `auto` çağrıyı garanti etmediği için çağrının yapıldığı denetlenmeli. **C [YP]** — [Anthropic, Tool use overview](https://platform.claude.com/docs/en/agents-and-tools/tool-use/overview)

**vLLM (ana dal belgesi, 22 Kasım 2026'da okundu)**
- Arka uçlar `xgrammar` ve `guidance`; varsayılan `auto`; `lm-format-enforcer` için geçiş uyarısı var ve düzenli ifade lehçesi arka uca göre değişiyor (Rust biçimi / Python biçimi). Kısıt türleri: `choice`, `regex`, `json`, `grammar`, `structural_tag`. **C [BK]** — [vLLM, Structured outputs](https://raw.githubusercontent.com/vllm-project/vllm/main/docs/features/structured_outputs.md)
- Araç çağırma: adlı işlev, `auto`, `required` (0.8.3'ten beri) ve `none`. Adlı ve `required` çağrılar yapılandırılmış çıktı arka ucunu kullanır: "You are guaranteed a validly-parsable function call - not a high-quality one"; şema ilk kullanımda derlenirken "several seconds of latency (or more)". Sunucu `--enable-auto-tool-choice` ve modele uygun `--tool-call-parser` ile başlatılmalı. **C [BK]** — [vLLM, Tool calling](https://raw.githubusercontent.com/vllm-project/vllm/main/docs/features/tool_calling.md)
- Varsayılan tuzak: `tool_choice="auto"` ile gramer yalnız en az bir araç `strict: true` taşıyorsa ya da sunucu `--tool-strict-level function|parameter` ile başlatıldıysa devreye girer. "Most OpenAI-compatible clients and agent frameworks never set `strict` on their tools, so with `tool_choice="auto"` the model generates tool calls without any grammar and malformed markup can leak into the response." `parameter` düzeyi her aracın argüman şemasını sabitler; şemalar "OpenAI strict-schema style" (bütün alanlar `required`) yazılmalı. **C [BK]** — [aynı belge](https://raw.githubusercontent.com/vllm-project/vllm/main/docs/features/tool_calling.md)

**llama.cpp**
- OpenAI biçimli işlev çağırma `--jinja` ile; sunucu README'sine göre `--jinja` artık varsayılan olarak açık. Yerel biçim işleyicileri (Llama 3.x, Hermes 2/3, Qwen 2.5, Mistral Nemo, GPT-OSS vb.) dışında "Generic" işleyici kullanılır ve "may consume more tokens and be less efficient". Paralel araç çağrısı varsayılan kapalı. "Beware of extreme KV quantizations (e.g. `-ctk q4_0`), they can substantially degrade the model's tool calling performance." **C [BK]** — [llama.cpp, function-calling.md](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/docs/function-calling.md); [llama.cpp server README](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/tools/server/README.md)
- JSON Schema → GBNF dönüşümü: `json_schema` (tamamlama uç noktaları) ve `response_format` (sohbet uç noktası); `additionalProperties` varsayılanı `false`; "Unsupported features are skipped silently"; C++ sürümünde uzak `$ref` yok; `x? x? x?…` biçimli gramerler "extremely slow sampling" üretebilir. **C [BK]** — [llama.cpp grammars README](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/grammars/README.md)
- Açık hata kaydı: büyük ama gerçekçi araç kümelerinde sunucunun isteği şema→gramer derleme aşamasında reddettiği bildirilmiş. **C [AÖ]** — [llama.cpp issue #25895](https://github.com/ggml-org/llama.cpp/issues/25895)
- Aynı sunucu OpenAI uyumlu uç noktaların yanında Anthropic uyumlu `POST /v1/messages` de sunar; belge "no strong claims of compatibility" diyor. **C [BK]** — [llama.cpp server README](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/tools/server/README.md)
- Ollama: `format` parametresine JSON şeması verilerek yapılandırılmış çıktı. **C [BK]** — [Ollama API](https://raw.githubusercontent.com/ollama/ollama/main/docs/api.md)

**Bağımsız karşılaştırma**
- JSONSchemaBench (arXiv 2501.10868): yaklaşık 10 bin gerçek dünya şeması (README'ye göre 9.558; 1.707'si işlev çağrısı şeması); Guidance, Outlines, Llamacpp, XGrammar, OpenAI ve Gemini karşılaştırılmış; en iyi çerçeve en kötünün iki katı kadar şemayı destekliyor; kısıtlı çözümleme üretimi %50'ye kadar hızlandırabiliyor ve alt görev başarımını %4'e kadar artırıyor. Çerçeve bazlı kapsama yüzdeleri okunmadı. **B (veri kümesi [BK], sonuçlar [AÖ])** — [JSONSchemaBench, arXiv 2501.10868](https://arxiv.org/abs/2501.10868); [JSONSchemaBench README](https://raw.githubusercontent.com/guidance-ai/jsonschemabench/main/README.md)
- Model boyutu ile araç kullanım başarısı arasındaki fark (MCP-Bench genel puanı): 120B sınıfı açık ağırlıklı model 0,692; 20B sınıfı 0,654; 8B sınıfı 0,428. **B [BK]** — [MCP-Bench README](https://raw.githubusercontent.com/Accenture/mcp-bench/main/README.md)

### Inferences
- Şema uygunluğu anlamsal doğruluk değildir (vLLM'nin kendi cümlesi). Mevcut sözleşmedeki "girdi güvenilmezdir; sahibin katı şema ve yetki denetimi yine gerekir" ilkesi (`AiToolInputV1`) kısıtlı çözümlemeyle birlikte de aynen kalmalı.
- Tek şema lehçesi önerisi: bütün alanlar `required`, `additionalProperties: false`, özyineleme yok, sayısal/uzunluk kısıtı şemada değil sunucu doğrulamasında, `oneOf` yerine `anyOf`, birleşim tipleri az, `enum` değerleri yalnız büyük-küçük harfle ayrışmayan. Bu alt küme Anthropic'in sınırlarına, vLLM'nin "OpenAI strict" önerisine ve llama.cpp'nin dönüştürücüsüne birlikte uyar.
- Yerel dağıtımda sunucu `--tool-strict-level parameter` (ya da her araçta `strict: true`) ile çalıştırılmalı; aksi hâlde "kısıtlı çözümleme var" sanılırken yoktur. İlk çağrı gecikmesi için sık kullanılan şemalar açılışta ısıtılabilir.
- Katı araç sayısı sınırları (bulutta 20) ve gramer derleme maliyeti, §1'deki "her turda az sayıda araç yükle" kararını ayrıca zorunlu kılar: yüzlerce aracın tamamı aynı anda katı olamaz.
- Küçük model yönlendirici için güvenli biçim sınıflandırmadır: `choice` kısıtıyla modül/niyet/risk sınıfı seçtirmek, sonra daraltılmış araç kümesiyle ana modele gitmek. Küçük modelin araç argümanı üretmesi MCP-Bench'teki boyut farkı nedeniyle ölçülmeden güvenilmemeli.
- Geri düşüş merdiveni (öneri): kısıtlı yerel çağrı → doğrulama hatasını modele geri verip bir kez yeniden deneme → daha büyük yerel model → (açıksa ve veri sınıfı izin veriyorsa) bulut → "bunu yapamadım; ilgili ekranı açıyorum" (`ui.navigate`). Son basamak, başarısızlığı kullanıcı için işe yarar kılar.
- KV önbellek nicemlemesi ve "Generic" şablon işleyicisi yerelde araç çağrısı kalitesini sessizce düşürebilir; model + şablon + nicemleme üçlüsü değerlendirme setiyle birlikte sürümlenmeli.

### Gaps
- OpenAI ve Gemini'nin yapılandırılmış çıktı belgeleri açılamadı; yalnız JSONSchemaBench üzerinden dolaylı bilgi var.
- 2026 kuşağı açık ağırlıklı modellerin kısıt altında araç çağrısı güvenilirliğine dair bağımsız, güncel ölçüm bulunamadı (BFCL panosu açılamadı).
- Türkçe istemle araç çağrısı kalitesine dair ölçüm yok.
- vLLM'de büyük araç kümeleriyle gramer derleme süresi ölçümü bulunamadı.

---

## 7. .NET yapı taşları ve olgunlukları: ne alınmalı, neden kaçınılmalı (kendi komut hattı olan modüler monolit için)

### Takeaway
Soyutlama katmanı (Microsoft.Extensions.AI) olgun ve sürüm ritmi .NET ile hizalı; ajan çatısı (Microsoft Agent Framework) Nisan 2026'da 1.0 oldu ama yedi ayda 32 kararlı sürüm çıkardı ve dayanıklı yürütme, AG-UI barındırma gibi parçaları yeni ya da önizlemede. MCP C# SDK'sı beş ayda iki ana sürüm gördü ve protokol Temmuz 2026'da kırıcı biçimde değişti. OpenTelemetry GenAI kuralları hâlâ "Development". Repo bugün sıfır yapay zekâ bağımlılığıyla, kendi protokol bağdaştırıcılarıyla çalışıyor; bu konumda mantıklı çizgi "soyutlama ve ölçüm kitaplığını al, yürütmeyi ve onayı kendi komut hattında tut, protokolleri kenarda bağdaştırıcı yap".

### Cited Findings
**Sürüm ve tarih tablosu (NuGet kaydı, 22 Kasım 2026)**

| Paket | Son kararlı (tarih) | İlk kararlı (tarih) | Not |
|---|---|---|---|
| Microsoft.Extensions.AI | 11.0.1 (21.11.2026) | 9.5.0 (16.05.2025) | 10.0.0 → 11.11.2025; 11.0.0 → 10.11.2026 |
| Microsoft.Extensions.AI.Evaluation | 11.0.1 (21.11.2026) | 9.5.0 (16.05.2025) | `.Quality` ve `.Reporting` kararlı; `.Safety` ve `.NLP` yalnız önizleme |
| Microsoft.Agents.AI | 1.28.0 (06.11.2026) | 1.0.0 (02.04.2026) | 1.0.0–1.28.0 arası 32 kararlı sürüm |
| Microsoft.Agents.AI.Workflows | 1.28.0 (06.11.2026) | 1.0.0 | OpenTelemetry.Api bağımlılığı var |
| Microsoft.Agents.AI.DurableTask | 1.16.0 (09.11.2026) | 1.16.0 (09.11.2026) | Öncesi yalnız önizleme |
| Microsoft.Agents.AI.Hosting.AGUI.AspNetCore | — | — | Yalnız önizleme (1.28.0-preview) |
| Microsoft.SemanticKernel | 1.82.0 (07.11.2026) | — | Yayın sürüyor |
| ModelContextProtocol | 2.4.0 (07.11.2026) | 1.0.0 (25.02.2026) | 2.0.0 → 28.07.2026 |
| OpenAI (.NET) | 2.15.0 (05.11.2026) | — | |
| Anthropic (C#) | 13.12.0 (20.11.2026) | — | |
| OllamaSharp | 5.5.2 (05.11.2026) | — | Microsoft.Extensions.AI.Ollama hiç kararlı olmadı (son 9.7.0-preview) |
| OpenTelemetry (.NET) | 1.21.0 (11.11.2026) | — | |
| Temporalio | 1.22.0 (10.11.2026) | 1.0.0 (05.12.2023) | |
| Quartz | 4.8.1 (21.11.2026) | 4.0.0 (03.09.2026) | 4.x yalnız .NET 10 |
| Hangfire.Core | 1.8.25 (28.08.2026) | — | |
| MailKit | 5.0.0 (10.11.2026) | — | |

**kayıt [BK]** — [NuGet: Microsoft.Extensions.AI](https://www.nuget.org/packages/Microsoft.Extensions.AI); [NuGet: Microsoft.Agents.AI](https://www.nuget.org/packages/Microsoft.Agents.AI); [NuGet: Microsoft.Agents.AI.DurableTask](https://www.nuget.org/packages/Microsoft.Agents.AI.DurableTask); [NuGet: ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol); [NuGet: Microsoft.SemanticKernel](https://www.nuget.org/packages/Microsoft.SemanticKernel); [NuGet: Temporalio](https://www.nuget.org/packages/Temporalio); [NuGet: Quartz](https://www.nuget.org/packages/Quartz)

**Microsoft.Extensions.AI**
- `IChatClient` ve `IEmbeddingGenerator` soyutlamaları; üst pakette otomatik işlev çağırma, telemetri ve önbellek ara katmanları; `IImageGenerator` deneysel. (Belge tarihi 14 Eylül 2026.) **C [BK]** — [dotnet/docs, Microsoft.Extensions.AI libraries](https://raw.githubusercontent.com/dotnet/docs/main/docs/ai/microsoft-extensions-ai.md)
- Onay sarmalayıcısı zorlamaz: `ApprovalRequiredAIFunction` "simply augments an `AIFunction` with an indication that approval is required… It does not enforce the requirement". **C [BK]** — [dotnet/extensions, ApprovalRequiredAIFunction.cs](https://raw.githubusercontent.com/dotnet/extensions/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/Functions/ApprovalRequiredAIFunction.cs)
- `FunctionInvokingChatClient` böyle sarmalanmış işlevi doğrudan çağırmaz; onay isteği ve yanıtı içerik tipleriyle (ör. `FunctionApprovalResponseContent`) taşınır; onayın `IChatClient` yığınında işlenmesi alttaki servisin onay kavramını desteklediği anlamına gelmez (dolayısıyla yerel OpenAI uyumlu sunucuda da istemci tarafında çalışması beklenir — çıkarım). **C [AÖ]** — [Microsoft Learn, FunctionInvokingChatClient](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.functioninvokingchatclient); [Microsoft Learn, Agent Framework tool approval](https://learn.microsoft.com/agent-framework/agents/tools/tool-approval)
- Telemetri: `OpenTelemetryChatClient` "provides an implementation of the Semantic Conventions for Generative AI systems v1.41… The specification is still experimental and subject to change; as such, the telemetry output by this client is also subject to change"; istem/yanıt içeriği yalnız `EnableSensitiveData` açıkken yazılır. **C [BK]** — [dotnet/extensions, OpenTelemetryChatClient.cs](https://raw.githubusercontent.com/dotnet/extensions/main/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/OpenTelemetryChatClient.cs)
- Değerlendirme kitaplıkları: herhangi bir test çatısıyla (xUnit dahil) ve CI ile çalışır; ajan odaklı `ToolCallAccuracyEvaluator`, `TaskAdherenceEvaluator`, `IntentResolutionEvaluator` LLM ile puanlar; yanıt önbelleği aynı istem + model için sonraki koşularda yanıtı önbellekten verir; `dotnet aieval` rapor üretir; güvenlik değerlendiricileri Foundry Evaluation servisine bağımlıdır; kendi ölçütünüz için `IEvaluator`. (Belge tarihi 9 Nisan 2026.) **C [BK]** — [dotnet/docs, Microsoft.Extensions.AI.Evaluation libraries](https://raw.githubusercontent.com/dotnet/docs/main/docs/ai/evaluation/libraries.md)

**Microsoft Agent Framework ve Semantic Kernel**
- Agent Framework 1.0, .NET ve Python için Nisan 2026 başında (resmî blog 3 Nisan) "production-ready" olarak duyuruldu: kararlı API, uzun süreli destek taahhüdü; kararlı yüzey tek ajan soyutlaması, ara katman, hafıza/bağlam sağlayıcıları, çizge tabanlı iş akışları; DevUI önizleme. Build 2026'da (2–3 Haziran) ajan "harness"i ve çoklu ajan kalıpları kararlı oldu. **C [AÖ]** — [Agent Framework blog, 1.0](https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-version-1-0/); [Visual Studio Magazine](https://visualstudiomagazine.com/articles/2026/04/06/microsoft-ships-production-ready-agent-framework-1-0-for-net-and-python.aspx); [InfoQ](https://infoq.com/news/2026/08/agent-framework-harness-ga)
- Bağımlılık grafiği (1.28.0, `net10.0`): Microsoft.Extensions.AI ≥ 10.11.0, Microsoft.Extensions.AI.Evaluation, Microsoft.Extensions.Compliance.Abstractions, Microsoft.Extensions.VectorData.Abstractions, Microsoft.ML.Tokenizers, Microsoft.Extensions.FileSystemGlobbing. **kayıt [BK]** — [NuGet: Microsoft.Agents.AI 1.28.0](https://www.nuget.org/packages/Microsoft.Agents.AI/1.28.0)
- Semantic Kernel deposunun kendi duyurusu: "Semantic Kernel is now Microsoft Agent Framework! Microsoft Agent Framework (MAF) is the enterprise‑ready successor to Semantic Kernel." **C [BK]** — [Semantic Kernel README](https://raw.githubusercontent.com/microsoft/semantic-kernel/main/README.md)

**MCP C# SDK ve protokol**
- Paketler: `ModelContextProtocol.Core`, `ModelContextProtocol`, `.AspNetCore`, `.Extensions.Apps`, `.Extensions.Skills`, `.Extensions.Tasks`; hedefler net8/net9/net10 + netstandard2.0. **C + kayıt [BK]** — [MCP C# SDK README](https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/main/README.md); [NuGet: ModelContextProtocol 2.4.0](https://www.nuget.org/packages/ModelContextProtocol/2.4.0)
- 2.0 ile HTTP taşımasında durumsuz kip varsayılan oldu; SDK belgesi "we recommend most servers disable sessions entirely" diyor ve eski istemciler için karma kip sunuyor. 1.0 sürümü 2025-11-25 şartnamesini, 2.0 sürümü 2026-07-28 şartnamesini uyguluyor. **C [BK]** belge, sürüm–şartname eşleşmesi **[AÖ]** — [MCP C# SDK, stateless.md](https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/main/docs/concepts/stateless/stateless.md); [.NET Blog, v2.0 duyurusu](https://devblogs.microsoft.com/dotnet/announcing-v20-of-the-official-mcp-csharp-sdk/)
- 2026-07-28 şartnamesinin kırıcı değişiklikleri: protokol düzeyi oturumlar ve `initialize` el sıkışması kaldırıldı; görevler çekirdekten resmî uzantıya taşındı; sunucudan istemciye istekler yerine çok turlu istek (MRTR) kalıbı geldi; akış yeniden bağlanma kaldırıldı; Roots, Sampling ve Logging kullanımdan kaldırılıyor; en az on iki aylık kullanımdan kaldırma penceresi tanımlandı. **C (normatif) [BK]** — [MCP 2026-07-28 changelog](https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/specification/2026-07-28/changelog.mdx)
- ASP.NET Core bütünleşmesinde `[Authorize]`, `[Authorize(Roles=…)]`, `[Authorize(Policy=…)]` araç düzeyinde desteklenir (`AddAuthorizationFilters()`). **C [BK]** — [MCP C# SDK, filters.md](https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/main/docs/concepts/filters.md)

**OpenTelemetry GenAI kuralları**
- Bütün GenAI belgeleri (istemci çıkarım, araç yürütme, ajan çağrısı, ajan span'ları, MCP kuralları) "Status: Development". Ajan span'ları: `create_agent`, `invoke_agent`, plan ve araç yürütme. **C (normatif taslak) [BK]** — [semantic-conventions-genai README](https://raw.githubusercontent.com/open-telemetry/semantic-conventions-genai/main/docs/gen-ai/README.md); [gen-ai-agent-spans.md](https://raw.githubusercontent.com/open-telemetry/semantic-conventions-genai/main/docs/gen-ai/gen-ai-agent-spans.md)
- GenAI kuralları Haziran 2026'da ayrı depoya taşındı ve 16 Temmuz 2026 itibarıyla etiketli sürümü yoktu; kararlılık için kamuya açık takvim yok; GenAI span'ında "Stable" olan öznitelikler yalnız çekirdekten gelen `error.type`, `server.address`, `server.port`. Bazı blogların "kararlı oldu" iddiasını birincil kaynak desteklemiyor. **C [AÖ]** — [DEV Community yazısı](https://dev.to/azena-ai/opentelemetrys-genai-semantic-conventions-are-not-stable-yet-heres-what-actually-shipped-in-2026-3mke); [Dash0 açıklaması](https://www.dash0.com/knowledge/opentelemetry-genai-semantic-conventions-explained)
- MCP 2026-07-28, iz bağlamının (`traceparent`, `tracestate`, `baggage`) `_meta` anahtarlarıyla taşınmasını belgeliyor. **C (normatif) [BK]** — [MCP 2026-07-28 changelog](https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/specification/2026-07-28/changelog.mdx)

**Yerel sunucular için istemci**
- llama.cpp sunucusu hem OpenAI uyumlu sohbet uç noktasını (araçlarla) hem Anthropic uyumlu `/v1/messages` uç noktasını sunar; vLLM OpenAI uyumlu sunucuda yapılandırılmış çıktıyı varsayılan destekler. **C [BK]** — [llama.cpp server README](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/tools/server/README.md); [vLLM, Structured outputs](https://raw.githubusercontent.com/vllm-project/vllm/main/docs/features/structured_outputs.md)
- Microsoft'un `IChatClient` belgesindeki işlev çağırma örneği yerel model istemcisi olarak `OllamaSharp.OllamaApiClient` kullanıyor; aynı belge katmanlanabilir ara katmanları (işlev çağırma, `DistributedCachingChatClient` ile yanıt önbelleği, telemetri) ve bağımlılık enjeksiyonuyla kurulumu anlatıyor. **C [BK]** — [dotnet/docs, IChatClient](https://raw.githubusercontent.com/dotnet/docs/main/docs/ai/ichatclient.md)

### Inferences
**Olgunluk özeti (bu notun değerlendirmesi)**

| Yapı taşı | Olgunluk | Bu depo için öneri |
|---|---|---|
| Microsoft.Extensions.AI (soyutlama) | Yüksek: 18 aydır kararlı, üç ana sürüm | **Al (isteğe bağlı):** mevcut `IAiExternalProtocolAdapterV1` bağdaştırıcıları araç çağrısı ve akışa genişletilecekse değişim tiplerini buradan almak iş azaltır. Otomatik işlev çağırma ara katmanı Draft/Write araçlarında **kullanılmamalı**. |
| Microsoft.Extensions.AI.Evaluation | Orta-yüksek: çekirdek/Quality/Reporting kararlı | **Al:** raporlama ve yanıt önbelleği için; deterministik ölçütler kendi `IEvaluator`'ımızla. Safety paketi buluta bağlı, almayın. |
| Microsoft Agent Framework (çekirdek) | Orta: kararlı ama çok hızlı sürüm, ağır bağımlılık | **Bekle / yalnız fikir al:** yetki, idempotency, audit ve onay zaten bizim hattımızda; çatının kattığı (ajan soyutlaması, çoklu ajan) şu an gerekmiyor. AI-29 gerçekten gerekirse yeniden bak. |
| Agent Framework dayanıklı uzantı | Düşük: iki haftalık kararlı, Durable Task Scheduler'a bağlı | **Alma.** |
| Semantic Kernel | Geçiş hâlinde: halefi duyuruldu | **Yeni kodda kullanma.** |
| MCP C# SDK | Orta: kararlı ama protokol ve SDK kırıcı değişti | **Sonra, kenarda:** iç araç kataloğunun önüne ince bir MCP cephesi; iç sözleşme asla MCP olmasın. |
| OpenAI .NET / Anthropic C# / OllamaSharp | Yüksek | Sağlayıcı SDK'sı almak yerine mevcut ince HTTP bağdaştırıcıları sürdürülebilir; karar bağımlılık maliyetine göre. |
| OpenTelemetry GenAI kuralları | Düşük: "Development" | **Yay ama sarmala:** `gen_ai.*` adlarını tek yardımcıda topla; ad değişince tek yer değişsin. İz, audit kaydının yerini tutmaz. |
| AG-UI (.NET istemci 1.0.1) | Orta: şartname 1.0, .NET sunucu parçası önizleme | **Kalıbı al:** kendi SSE olay akışımızda aynı olay türlerini kullan; pakete bağlanma. |
| Quartz.NET 4 | Orta-yüksek: 4.x Eylül 2026'da kararlı oldu, 3.x dalı sürdürülüyor | **Aday:** zamanlı görevler için; yalnız .NET 10'u hedefliyor. |
| Temporal .NET | Yüksek SDK, yüksek işletim yükü | **Şimdilik alma** (bkz. §4). |

- Repo bugün Microsoft.Extensions.AI, Agent Framework, Semantic Kernel, Hangfire ya da Quartz içermiyor; bağımlılıklar ASP.NET Core, EF Core SQL Server, OpenTelemetry ve PDFsharp ile sınırlı ve build-control/kurtarma envanteri her yeni bağımlılığı pin'liyor. Bu, "az bağımlılık, kenarda bağdaştırıcı" çizgisini depo kültürüyle de uyumlu kılar.
- Agent Framework'ün yedi ayda 32 kararlı sürümü API kırıldığı anlamına gelmez, ama her güncellemenin değerlendirme CI'ından geçmesi gerektiği için bakım yükü demektir; çatı komut hattının içine girerse bu yük kritik yola taşınır.
- MCP cephesi açılırsa SDK'nın yetki süzgeçleri kullanıcıya göre araç listesi anlamını hazır verir; yine de asıl yetki denetimi sahip modülde kalmalı (cephe ikinci savunma hattıdır).

### Gaps
- `learn.microsoft.com` ve `devblogs.microsoft.com` açılamadığı için Agent Framework'ün araç onayı, denetim noktası deposu ve AG-UI bütünleşmesi API ayrıntıları yalnız arama özetine dayanıyor; `microsoft/agent-framework` deposunun ham dosyaları da okunamadı (404).
- Microsoft.Extensions.AI'da gömme tabanlı araç azaltma (tool reduction) özelliğinin güncel durumu doğrulanamadı.
- Agent Framework sürümleri arasındaki kırıcı değişikliklerin sayısı ve niteliği incelenmedi (sürüm notları okunmadı).

---

## 8. Araç kullanan asistanın değerlendirmesi ve regresyon testi; maliyet ve gecikme denetimi

### Takeaway
Değerlendirme için güncel sağlayıcı rehberi önceki notun önerisiyle aynı yönde ve onu somutlaştırıyor: 20–50 gerçek hata vakasıyla başla, yolu değil sonucu puanla, deterministik puanlayıcıyı tercih et, tutarlılığı pass^k ile ölç, regresyon setini %100'e yakın tut. Maliyette en büyük kaldıraç sabit önek (araçlar + sistem istemi) ve önbellektir; bu kaldıraç yerel sunucularda da (vLLM, llama.cpp) hazırdır ve araç listesinin kararlı sıralanmasına bağlıdır.

### Cited Findings
**Değerlendirme**
- Tanımlar ve yöntem (9 Ocak 2026): görev, deneme, puanlayıcı, döküm, sonuç; "20-50 simple tasks drawn from real failures is a great start"; görev, iki alan uzmanının bağımsız olarak aynı geçti/kaldı yargısına varacağı kadar net olmalı; her deneme temiz ortamdan başlamalı. **C [BK]** — [Anthropic, Demystifying evals for AI agents](https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents)
- Puanlayıcı türleri: kod tabanlı (hızlı, ucuz, nesnel, tekrarlanabilir; geçerli çeşitlemelere karşı kırılgan), model tabanlı (esnek; deterministik değil, insan puanlayıcıya karşı ayarlanmalı), insan (altın standart; pahalı). Öneri: mümkünse deterministik, gerekince LLM, ölçülü insan; LLM hakeme "Unknown" seçeneği ver, her boyutu ayrı hakemle puanla. **C [BK]** — [aynı yazı](https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents)
- Yol değil sonuç: adım dizisini birebir puanlamak "too rigid and results in overly brittle tests"; "often better to grade what the agent produced, not the path it took". **C [BK]** — [aynı yazı](https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents)
- Tutarlılık: deneme başına %75 başarıda üç denemenin hepsinin geçme olasılığı (0,75)³ ≈ %42; k=10'da pass@k %100'e, pass^k %0'a yaklaşır; müşteriyle yüzleşen ajanlarda pass^k kullanılmalı. Yetenek değerlendirmesi düşük geçme oranıyla başlar; regresyon değerlendirmesi "should have a nearly 100% pass rate". Birçok denemede %0 geçme çoğunlukla bozuk görev işaretidir. **C [BK]** — [aynı yazı](https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents)
- Araç geliştirirken izlenecek ölçüler: doğruluk, süre, toplam araç çağrısı, token tüketimi, araç hataları; aşırı uyuma karşı tutulan test seti. **C [BK]** — [Anthropic, Writing effective tools for agents](https://www.anthropic.com/engineering/writing-tools-for-agents)
- .NET tarafında değerlendirme koşularının yanıt önbelleğiyle ucuzlatılması ve CI raporu: bkz. §7. **C [BK]** — [dotnet/docs, Evaluation libraries](https://raw.githubusercontent.com/dotnet/docs/main/docs/ai/evaluation/libraries.md)
- τ-bench 2.0: altı dil (İngilizce, İspanyolca, Portekizce, Hintçe, Çince, Korece) ve "Frozen task sets with pinned data" ekledi; Türkçe yok. **B [BK]** — [tau2-bench README](https://raw.githubusercontent.com/sierra-research/tau2-bench/main/README.md)
- MCP-Bench'in puanlama yapısı deterministik kontrol + LLM hakem karışımına örnektir (kural tabanlı şema anlama + o4-mini hakemli görev tamamlama). **B [BK]** — [MCP-Bench README](https://raw.githubusercontent.com/Accenture/mcp-bench/main/README.md)

**Maliyet ve gecikme**
- Bulutta istem önbelleği: önek eşleşmesi, sıra `tools` → `system` → `messages`; önekteki herhangi bir bayt değişikliği sonrasını geçersiz kılar. Önbellek okuması taban girdi fiyatının yaklaşık 0,1 katı (en yeni modellerde 0,05 ve 0,025 kat), yazma 5 dakikalık ömürde 1,25 kat; 1 saatlik ömür seçeneği var; en kısa önbelleklenebilir önek modele göre 512–4096 token. **C [YP]** — [Anthropic, Prompt caching](https://platform.claude.com/docs/en/build-with-claude/prompt-caching)
- Toplu işleme %50 indirim. **C [BK]** — [Anthropic, Structured outputs (feature compatibility)](https://platform.claude.com/docs/en/build-with-claude/structured-outputs)
- Yerelde aynı kaldıraç: vLLM Automatic Prefix Caching "caches the KV cache of existing queries, so that a new query can directly reuse the KV cache if it shares the same prefix"; llama.cpp sunucusunda istem önbelleği varsayılan açık, `cache_prompt` ortak öneki yeniden işlemez, yuva (slot) önbelleği dosyaya kaydedilip geri yüklenebilir. **C [BK]** — [vLLM, Automatic Prefix Caching](https://raw.githubusercontent.com/vllm-project/vllm/main/docs/features/automatic_prefix_caching.md); [llama.cpp server README](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/tools/server/README.md)
- Araç listesinin kararlı sırası önbellek için öneriliyor (MCP `tools/list`); araç araması ertelenmiş araçları önekin dışında tuttuğu için önbelleği bozmaz. **C [BK]** — [MCP 2026-07-28 changelog](https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/specification/2026-07-28/changelog.mdx); [Anthropic, Tool search tool](https://platform.claude.com/docs/en/agents-and-tools/tool-use/tool-search-tool)
- Bağlam yönetimi: "context rot" (bağlam büyüdükçe geri çağırma zayıflar); üç teknik: sıkıştırma (compaction), yapılandırılmış not alma, alt ajanlar — alt ajan iç çalışmasında on binlerce token harcayıp "often 1,000-2,000 tokens" özet döndürür; araç sonucu temizleme en hafif biçimdir. **C [BK]** — [Anthropic, Effective context engineering](https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents)
- API düzeyinde karşılıkları: bağlam düzenleme (eski araç sonuçlarını temizler; beta), sunucu tarafı sıkıştırma (varsayılan eşik 150K token; beta), istemci tarafı hafıza aracı (dosya dizini; erişim denetimi uygulayana aittir). **C [YP]** — [Anthropic, Context editing](https://platform.claude.com/docs/en/build-with-claude/context-editing); [Anthropic, Compaction](https://platform.claude.com/docs/en/build-with-claude/compaction); [Anthropic, Memory tool](https://platform.claude.com/docs/en/agents-and-tools/tool-use/memory-tool)
- Araç çıktısı boyutu: bir üretim istemcisi araç çıktısını varsayılan 25.000 token ile sınırlıyor; `response_format` ile kısa biçim aynı cevabı yaklaşık üçte bir token'a indiriyor (72'ye karşı 206); bir benchmark ekibi araç çıktısını modele gitmeden süzen sarmalayıcıyla "%50–75 token tasarrufu" iddia ediyor. **C [İDDİA] [BK]** — [Claude Code docs, MCP](https://code.claude.com/docs/en/mcp); [Anthropic, Writing effective tools for agents](https://www.anthropic.com/engineering/writing-tools-for-agents); [MCP-Universe README](https://raw.githubusercontent.com/SalesforceAIResearch/MCP-Universe/main/README.md)
- Birden çok araç çağrısını tek programda toplamak 20+ çağrıda 19+ model turunu ortadan kaldırır (bedeli korumalı alan). **C [İDDİA] [BK]** — [Anthropic, Advanced tool use](https://www.anthropic.com/engineering/advanced-tool-use)

### Inferences
- Deterministik kontroller bizde zengin ve ucuz: seçilen araç kullanıcının kataloğunda mı; argümanlar şemadan geçti mi; yetkisiz istekte ret döndü mü; cevaptaki her sayı bir calculator ya da okuma aracı çıktısıyla birebir eşleşiyor mu; yazma sınıfı araç önizlemesiz çağrılmadı mı; kaynak gösterildi mi. Bunlar LLM hakemden önce koşmalı ve regresyon setinin çekirdeği olmalı.
- "Yolu değil sonucu puanla" kuralının bizdeki istisnası güvenlik değişmezleridir: önizleme atlanması, onay komutu çağırma girişimi, ham SQL isteği gibi yol ihlalleri sonuç doğru olsa bile kalır sayılmalı.
- Altın görev seti plan §3'teki modül tablosundan türetilebilir; her kritik görev en az 3–5 kez koşulup pass^k raporlanmalı. Türkçe için hazır benchmark olmadığından set şirketin kendi sorularından kurulmalı (önceki notla aynı sonuç).
- Maliyet düzeni (yerelde de geçerli): sabit ve sıralı önek = sistem istemi + çekirdek araçlar; kullanıcıya ve tura özgü içerik (hafıza, bulunan araçlar, ekran bağlamı) önekin **sonrasına**; adımlar arasında araç sonuçlarını özetleyip atmak; sabah özetlerini gece toplu hazırlamak (önceki notun "uyku zamanı işi").
- Araç çıktıları varsayılan olarak "kısa" biçimde ve sayfa sınırlıyla dönmeli; ayrıntı ikinci çağrıyla istenmeli. Bu, hem bağlamı hem veri sınıfı sızıntı yüzeyini küçültür (alan izin listesiyle birlikte).

### Gaps
- Yerel GPU sunumunun maliyet modeli (OPEN-014) bu notun kapsamı dışında; ölçüm yok.
- Büyük şemalarla kısıtlı çözümlemenin yerel sunucularda gecikmeye etkisi ölçülmedi.
- OpenAI tarafındaki istem önbelleği ve toplu işleme koşulları açılamadığı için yazılmadı.

---

## 9. Çıktı: mevcut komut hattının üzerine "her şey yapay zekâ üzerinden" referans mimari taslağı, olgunluk sınıflaması ve efor sıralaması

### Takeaway
Hedefe "her rota bir araç" ile değil, dört yürütme şeridiyle varılır: oku, taslakla-ve-ekranda-aç, benzet, önizle-onayla-yürüt. Bunların üçü bugün düşük riskle kurulabilir, çünkü riskli kısım (yetki, sürüm, idempotency, audit, onay) zaten komut hattında ve yapay zekâ katmanı yalnız "doğru şeridi ve doğru aracı seçme" işini üstleniyor. Olgunlaşmamış olanlar çevre parçalarıdır: serbest iş akışı üretimi, dayanıklı ajan çalışma zamanları, GenAI telemetri kuralları, hızla değişen MCP ve küçük modelle ölçülmemiş yönlendirme.

### Cited Findings
Mimariyi belirleyen, yukarıdaki bölümlerde ayrıntısı verilen bulgular (kısa tekrar, kaynaklarıyla):
- Araç sayısı 30–50'yi geçince seçim bozulur; çözüm ertelenmiş yükleme ve arama, 3–5 çekirdek araç. **C [İDDİA] [BK]** — [Anthropic, Tool search tool](https://platform.claude.com/docs/en/agents-and-tools/tool-use/tool-search-tool)
- Ad alanı başına 10'dan az işlev önerisi; onay gerektiren araçlar tek tek çağrı olarak kalmalı. **C [BK]** — [openai-agents-python, docs/tools.md](https://raw.githubusercontent.com/openai/openai-agents-python/main/docs/tools.md)
- Sahadaki ERP asistanı öneriyi asla kendiliğinden kaydetmez; arka plan ajanı önce zararsız ara belge üretir ve insan onayıyla ilerler. **C [BK]** — [Business Central, Autofill](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/autofill-fields-with-copilot.md); [Business Central, Sales Order Agent](https://raw.githubusercontent.com/MicrosoftDocs/dynamics365smb-docs/main/business-central/sales-order-agent.md)
- Onayı ne kütüphane ne protokol zorlar. **C [BK]** — [dotnet/extensions, ApprovalRequiredAIFunction.cs](https://raw.githubusercontent.com/dotnet/extensions/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/Functions/ApprovalRequiredAIFunction.cs); [MCP blog, Tool annotations](https://blog.modelcontextprotocol.io/posts/2026-03-16-tool-annotations/)
- Politika önerisi "yalnız raporla" kipinde doğar; mevcut politika yalnız yetkili yönetici onayıyla değişir. **C [BK]** — [Entra, Conditional Access Optimization Agent](https://raw.githubusercontent.com/MicrosoftDocs/entra-docs/main/docs/security-copilot/conditional-access-agent-optimization.md)
- Zamanlı çalıştırmada bütçe istek öncesi kapıdır; kaçan tetikler geriye dönük çalıştırılmaz; görev sabit sürüme bağlıdır. **C [BK]** — [Anthropic, Scheduled deployments](https://platform.claude.com/docs/en/managed-agents/scheduled-deployments)
- Doğal dilden çalışır iş akışı üretimi güvenilir değil. **C (hakemsiz) [AÖ]** — [Chat2Workflow, arXiv 2604.19667](https://arxiv.org/abs/2604.19667)
- Yerel sunucuda kısıtlı çözümleme varsayılan olarak devrede olmayabilir. **C [BK]** — [vLLM, Tool calling](https://raw.githubusercontent.com/vllm-project/vllm/main/docs/features/tool_calling.md)
- Dayanıklı ajan uzantısı iki haftalık kararlı; GenAI telemetri kuralları "Development"; MCP Temmuz 2026'da kırıcı değişti. **kayıt + C [BK]** — [NuGet: Microsoft.Agents.AI.DurableTask](https://www.nuget.org/packages/Microsoft.Agents.AI.DurableTask); [semantic-conventions-genai README](https://raw.githubusercontent.com/open-telemetry/semantic-conventions-genai/main/docs/gen-ai/README.md); [MCP 2026-07-28 changelog](https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/specification/2026-07-28/changelog.mdx)

### Inferences
**9.1 Referans mimari taslağı** (öneri; karar değildir)

```
Kullanıcı — React kabuk: sohbet paneli + sabit kartlar + mevcut ekranlar
   │  olay akışı (SSE): metin · araç çağrısı · kart · kesinti/onay · ön yüz aracı (ui.navigate)
   ▼
[1] AI geçidi (mevcut Api/Ai): kimlik · şirket · veri sınıfı · çıkış süzgeci · model seçimi · bütçe kapısı
   ▼
[2] Yönlendirici: niyet + modül + risk sınıfı (kural ya da `choice` kısıtlı küçük model)
   ▼
[3] Katalog görünümü = araç kayıtları ∩ kullanıcının yetkileri (shell yetenek kaydı) ∩ ayar izin listesi
      └ arama (BM25 + gömme) → tur başına az sayıda araç; çekirdek araçlar hep yüklü ve sabit sırada
   ▼
[4] Planlayıcı döngü: sınırlı adım / süre / token; araç çağrısı kısıtlı çözümlemeyle, sonra sunucu doğrulaması
      ├ Read     → sahip modülün okuma aracı → kaynaklı cevap
      ├ Draft    → sunucuda süreli taslak → ui.navigate(rota, taslak) → kullanıcı ekranda kaydeder
      ├ Simulate → deterministik calculator / kuru çalıştırma → sonuç kartı
      └ Write    → önizleme (kanonik komut + fark + hedef sürüm + süre) → kullanıcı onayı
                   → mevcut komut hattı (yetki · expected version · idempotency = önizleme kimliği · audit · outbox)
   ▼
[5] Öneri hattı (ayar / yetki): tipli yama → ayar doğrulayıcısı → etki benzetimi → Work onay kutusu → yeni sürüm
[6] Görev çalıştırıcı: tanım (sahip · tetik · tarif@sürüm · bütçe) → çalıştırma ve adım kayıtları → bildirim; yalnız Read
      tetik: zamanlayıcı (cron) | outbox olayı (inbox + tekilleştirme + birleştirme)
[7] E-posta kapısı — giden: toplu insan onayı → gönderim (ayrı alt alan adı, SPF/DKIM/DMARC) → gönderim kaydı
                    gelen: posta kutusu okuyucu → karantina → şemalı çıkarım → yanıt eşleme → deterministik rapor
[8] Tarif kütüphanesi: JSON tarif (araç@sürüm adımları + şema + beklenti) → statik doğrulama → kuru çalıştırma → onay
[9] Gözlem ve ölçüm: audit (asıl kayıt) · gen_ai izleri (tek yardımcıda sarmalı) · değerlendirme CI'ı
```

Tasarım kuralları:
- Model [2]–[4]'te seçim yapar; [5], [7] ve [8]'de yalnız taslak ya da çıkarım üretir, doğrulama ve yürütme deterministiktir. [1] ve Write şeridinin sağ tarafı tamamen deterministiktir.
- Ekran sayısı araç sayısını büyütmez (tek `ui.navigate`); komut sayısı da doğrudan büyütmez (görev araçları + arama).
- Her şeridin başarısızlık çıkışı aynıdır: "yapamadım, ilgili ekranı açıyorum". Böylece kapsam eksikliği kullanıcıyı çıkmaza sokmaz ve "her ekran konuşarak ulaşılabilir" hedefi ilk günden karşılanır.
- [5], [6], [7] ve [8] ayrı ürün yüzeyleridir; hepsi [3]'teki aynı katalog görünümünü ve [1]'deki aynı bütçe kapısını kullanır.

**9.2 Olgunluk sınıflaması**

| Sınıf | Parçalar | Dayanak |
|---|---|---|
| **Şimdi düşük riskli** | Okuma araçları + kullanıcıya göre katalog + sunucu tarafı arama; `ui.navigate` ile taslak doldurma; kısıtlı çözümleme + sunucu doğrulaması; deterministik değerlendirme çekirdeği; önek önbelleği; cron tetikli salt-okuma görevler (bütçeli); insan onaylı toplu giden e-posta | §1, §2, §6, §8, §4 — birden çok bağımsız ürün/sağlayıcıda aynı kalıp |
| **Orta: pilotla ve ölçerek** | Önizleme-onay ile yazma (komut başına açılır); ayar/yetki öneri hattı (etki benzetimi gerekir); olay güdümlü tetikler (fırtına denetimi); gelen yanıt işleme ve rapor (enjeksiyon + kimlik); tarif üretimi ve kütüphanesi; küçük model yönlendirici | §2, §3, §4, §5, §6 — ürün örneği var, bağımsız ölçüm yok |
| **Olgunlaşmamış: bekle ya da küçük deney** | Rotalardan otomatik araç üretimi; serbest iş akışı/kod üretimi; Agent Framework dayanıklı uzantısı; GenAI telemetri kuralları (ad sabitliği); MCP'yi iç sözleşme yapmak; .NET'te AG-UI sunucu parçası; çoklu ajan | §1, §5, §7 — olumsuz kanıt ya da yeni/önizleme sürüm |

**9.3 Efor sıralaması** (S: bir dilime küçük ek · M: bir dilim · L: birden çok dilim; sıra değer/risk oranına göre)

| Sıra | İş | Efor | Risk | Bağlanacağı dilim | Neden bu sırada |
|---|---|---|---|---|---|
| 1 | Katalog görünümü (yetkiyle süzülmüş) + sunucu tarafı araç araması + çekirdek araç seti | M | Düşük | AI-13 | Diğer her şeyin önkoşulu; araç sayısı büyümeden kurulmalı |
| 2 | `ui.navigate` + süreli sunucu taslağıyla "git ve doldur" | S–M | Düşük | AI-15 / AI-17 | "Her ekran konuşarak" hedefini yazma riski olmadan karşılar |
| 3 | Tek şema lehçesi + kısıtlı çözümleme + doğrulama ve geri düşüş merdiveni | S | Düşük | AI-11 | Yerel modelde araç çağrısı güvenilirliğinin temeli |
| 4 | Değerlendirme CI'ının deterministik çekirdeği + pass^k | M | Düşük | AI-11 / AI-27 | Sonraki her adımın "işe yaradı mı" ölçüsü (önceki notun 1. maddesi) |
| 5 | Önizleme kartı + onaylı yazma, 3–5 komutla | M | Orta | AI-24 | Mevcut `AiPreviewV1` üzerine; komut başına açılır |
| 6 | Zamanlı salt-okuma görevler: tanım, çalıştırma kaydı, bütçe kapısı, bildirim | M | Düşük-orta | AI-18 | Sabah özeti gibi yüksek değerli kullanım; yazma yok |
| 7 | Ayar/yetki öneri hattı: tipli yama, etki benzetimi, onay kutusu | M–L | Orta | AI-21 | Etki benzetimi modül başına iş gerektirir |
| 8 | Giden toplu e-posta: şablon, alıcı listesi onayı, gönderim kaydı, alan adı kurulumu | M | Orta | AI-22 | Dış etki; insan kapıları (secret, alan adı) var |
| 9 | Tarif kütüphanesi: biçim, doğrulama, kuru çalıştırma, sürüm bağı | M–L | Orta | AI-25 | 1, 3 ve 6'ya dayanır |
| 10 | Olay güdümlü tetikler: outbox'tan tetik, birleştirme, kota | M | Orta | AI-18 | Fırtına ve maliyet riski; 6'dan sonra |
| 11 | Gelen yanıt işleme: karantina, çıkarım, eşleme, rapor | L | Yüksek | AI-22 | Güvenilmeyen dış içerik; önceki notun CaMeL önerisiyle birlikte |
| 12 | MCP cephesi (iç kataloğun önünde) | S–M | Orta | ihtiyaç doğarsa | Protokol değişkenliği nedeniyle en sona |
| 13 | Görev araçlarının kapsamını modül modül genişletme | L (sürekli) | Orta | AI-24 devamı | Kullanım verisi ve "eksik araç talebi" ile yönlenir |

**9.4 "Yapma" listesi** bu notun sonundaki "Önerilmeyenler / fazlalık" bölümündedir.

### Gaps
- Efor düzeyleri görelidir; süre ya da kişi-gün tahmini yapılmadı (ölçüm verisi yok).
- Sıralama, bu notta doğrulanamayan konulardaki (bkz. her bölümün "Gaps" kısmı) yeni kanıtla değişebilir; özellikle yerel modelin araç çağrısı başarımı ölçülünce 3–5. sıralar yeniden değerlendirilmeli.

---

## Bizim sisteme uyarlama

| # | Konu | Repodaki mevcut karşılık | Öneri | Dilim | Dayanak | Efor | Değişmez / insan kapısı |
|---|---|---|---|---|---|---|---|
| 1 | Araç yüzeyi | `AiToolCatalog` (yalnız Read, kayıtlı araç 0); `AiToolDescriptorV1` (kimlik, sürüm, sahip modül, risk, veri sınıfı, yetki, alan izin listesi) | Rota değil görev aracı; modül ad alanı; tanımlayıcıya arama metni (Türkçe eş anlamlı, ekran adı), örnek çağrı ve bağlı rota alanı eklenir | AI-13, AI-24 | §1 (C/B) | M | AI için DB/SQL yok; araç sahibi modüldür |
| 2 | Kullanıcıya göre katalog | Shell yetenek kaydı; `RequiredPermissions` | Katalog görünümü = kayıt ∩ yetki ∩ izin listesi; listede görmediğini çağıramaz | AI-13 | §1 (C) | S | Yeni izin adı ve grant insan kapısı |
| 3 | Araç araması | Yok | Sunucu tarafı BM25 + gömme; tur başına az araç; çekirdek araçlar sabit ve sıralı | AI-13, AI-16 | §1 (C; ölçülmeli) | M | — |
| 4 | Git ve doldur | React kabuk rotaları; `Draft` risk sınıfı; `AiDraftResultV1` | `ui.navigate` ön yüz aracı + süreli sunucu taslağı; alan bazında "önerildi" işareti ve kaynak; dışlama listesi | AI-15, AI-17 | §2 (C) | S–M | Para/miktar/formül alanları calculator'dan; UI doğrulaması tek güvenlik değildir |
| 5 | Onaylı yazma | `AiPreviewV1` (kanonik komut, fark, hedef sürüm, süre); kritik komut iskeleti | Önizleme kimliği idempotency anahtarı; onay tek kullanımlık; kapı komut hattında, risk sınıfına göre | AI-24 | §2 (C/normatif) | M | Kritik yazma insan onayı; onay komutları araca açılmaz |
| 6 | Kartlar | Sağ panel (`frontend/src/ai`) | Sabit bileşenli kartlar (önizleme, fark, kaynak, "ekranda aç", "onayla"); model arayüz üretmez | AI-14, AI-24 | §2 (C) | M | Kullanıcı metinleri Türkçe ve kaynak dosyalı |
| 7 | Ayar/yetki önerisi | Sürümlü ve denetimli ayar politikaları; Work onay kutusu | Tipli yama + ayar doğrulayıcısı + deterministik etki benzetimi + ham fark görünümü + reddedilen öneriyi hatırlama | AI-21 | §3 (C; akademik uyarı) | M–L | Politika yayımı, grant/rol/Critical insan kapısı; `OPEN-*` kapatmaz |
| 8 | Zamanlı görev | Barındırılan servis kalıbı (outbox dağıtıcısı, saklama temizliği) | Görev/çalıştırma/adım tabloları; bütçe kapısı; kaçan tetik atlanır; sabit tarif sürümü; Quartz.NET 4 aday | AI-18 | §4 (C) | M | Yalnız okuma; çalıştığı andaki yetki; kotalar yapılandırılabilir |
| 9 | Olay tetiği | Transactional outbox; inbox/tekilleştirme | Tetik outbox olayından; aynı varlık için birleştirme; olay başına kota | AI-18 | §4 (C) | M | Olay bir kez kaydedilir; kopya hareket yok |
| 10 | Giden e-posta | Yok (belge üretimi var) | Toplu onay ekranı; gönderim kaydı + `Message-Id`; ayrı gönderim alt alan adı | AI-22 | §4 (C; sağlayıcı politikası [AÖ]) | M | Dış etki outbox üzerinden; secret/deployment insan kapısı |
| 11 | Gelen yanıt | Yok | Karantina → şemalı çıkarım → `In-Reply-To`/`References` eşleme → deterministik rapor; gönderen birebir adres + kimlik doğrulama sonucu | AI-22 | §4 (C; A/B vaka) | L | Dış metin güvenilmez; KVKK ve ticari ileti mevzuatı (M) |
| 12 | Tarif | Yok | JSON tarif; araç kimliği + sürümü + `AiContractRevisionV1`; statik + kuru çalıştırma + çalışma zamanı doğrulaması | AI-25 | §5 (C; öneri D'ye yakın) | M–L | Şirket kütüphanesine terfi onaylı; tarif yazma içeriyorsa yine önizleme + onay |
| 13 | Kısıtlı çözümleme | `OpenAiChatCompletionsProtocolAdapter`, `AnthropicMessagesProtocolAdapter` | Tek şema lehçesi; yerel sunucuda katı kip; doğrulama + geri düşüş merdiveni | AI-11 | §6 (C/B) | S | Bulut açılışı insan kapısı (OPEN-020) |
| 14 | .NET bağımlılıkları | Yapay zekâ bağımlılığı yok; kendi ince bağdaştırıcıları | Microsoft.Extensions.AI ve Evaluation isteğe bağlı; Agent Framework, Semantic Kernel, Temporal, Durable Task Scheduler alınmaz; MCP kenarda | AI-11, AI-27 | §7 (kayıt + C) | S | Build-control ve kurtarma envanteri pin'leri |
| 15 | Gözlem | OpenTelemetry paketleri; `ai.tool.called` denetimi | `gen_ai.*` adları tek yardımcıda; içerik kaydı varsayılan kapalı; iz audit'in yerine geçmez | AI-20 | §7 (C) | S | DEC-AI-005 asıl kayıt |
| 16 | Değerlendirme | Yok | Deterministik çekirdek + pass^k + tutulan set; her model, istem, araç açıklaması ve tarif değişikliğinde | AI-11, AI-27 | §8 (C) | M | Kötüleşen sürüm canlıya çıkmaz |

## Önerilmeyenler / fazlalık

1. **~450 rotayı OpenAPI'den otomatik araca çevirmek.** Araç sayısı eşiklerini aşar; üretim deneyimi otomatik üretimi prototiple sınırlıyor (§1). Sözleşme yalnız şema kaynağı ve sürüklenme testi olsun.
2. **ERP içinde sayfa gezen, arayüz eylemi çağıran ajan.** Bir ERP satıcısı bunu arayüz üst verisiyle yapıyor (§2), ama bizde "yalnız şemalı backend aracı" değişmeziyle ve önceki notun computer-use sonucuyla çelişir.
3. **Agent Framework ya da Semantic Kernel'i komut hattının içine orkestratör olarak almak.** Yetki, idempotency, audit ve onay zaten bizde; çatı hızlı sürüm çıkarıyor ve bağımlılık grafiği ağır; Semantic Kernel'in halefi duyuruldu (§7).
4. **Agent Framework dayanıklı uzantısı (Durable Task Scheduler) ve Temporal kümesi — bugünkü kısa, salt-okuma görevler için.** Biri iki haftalık kararlı ve şirket içi arka ucu doğrulanamadı; diğeri ayrı sunucu ve deterministik kod disiplini ister (§4).
5. **Modelin serbest iş akışı ya da kod üretip üretimde çalıştırması; bunun için korumalı alan kurmak.** Kanıt olumsuz (§5); programatik araç çağrısı sağlayıcıların kendi uyarısıyla onaylı işlere uygun değil (§1).
6. **Modelin serbest arayüz üretmesi / kendi üretken arayüz dilimiz.** Sabit bileşenli kartlar yeterli; protokoller yeni kararlı, .NET sunucu parçası önizleme (§2).
7. **Onayı kütüphane sarmalayıcısına, MCP ek açıklamasına ya da LLM tabanlı "otomatik izin" değerlendirmesine bırakmak.** Hiçbiri zorlamaz; kapı deterministik olmalı (§2).
8. **Ayar ya da yetki önerisini otomatik uygulamak; onaycıya yalnız doğal dil özeti göstermek.** Doğal dil niyetinin doğrulanabilir karşılığı yok; ürün örneği ham farkı ve etkiyi birlikte gösteriyor (§3).
9. **Yazma yapabilen ya da kotasız zamanlı görev; bütçe kesintisinden sonra birikmiş tetikleri topluca çalıştırmak.** Tüketici ürünleri bile sıkı kota uyguluyor; birikme tuzağı ürün belgesinde yazılı (§4).
10. **İnsan onaysız dış e-posta; gelen e-postadaki bağlantıyı izlemek, uzak görseli yüklemek; göndereni adres yerine adından tanımak.** Sıfır tıklamalı sızıntı vakası ve ürün belgesindeki kimlik kuralı (§4).
11. **Bütün araçları aynı anda katı (strict) yapmak; dev `enum`'lar; şemada sayısal/uzunluk kısıtına ve özyinelemeye yaslanmak.** Sağlayıcı sınırları ve yerel dönüştürücülerin sessizce atladığı özellikler (§6).
12. **GenAI telemetri öznitelik adlarını panolara ve uyarılara doğrudan gömmek; izi audit yerine kullanmak.** Kurallar "Development"; uygulayan kitaplık çıktının değişeceğini söylüyor (§7).
13. **İç araç sözleşmesini MCP yapmak.** Protokol beş ayda kırıcı değişti; MCP yalnız kenarda bağdaştırıcı olabilir (§7). (Üçüncü taraf MCP sunucusu konusu önceki notta.)
14. **Microsoft.Extensions.AI.Ollama ve buluta bağlı Safety değerlendiricileri.** İlki hiç kararlı olmadı; ikincisi yerel öncelikli kurulumla uyuşmaz (§7).
15. **Küçük modeli ölçmeden araç argümanı üreticisi yapmak.** Bağımsız benchmark'ta küçük modeller belirgin geride; güvenli kullanım sınıflandırmadır (§6).
16. **Taslak değerlerini URL'ye yazarak derin bağlantı kurmak.** Sunucu taslağı varken gereksiz; kişisel veri günlüklere sızar (§2, çıkarım).

## Sahibine açık sorular

Aşağıdakiler karar değildir; yapılandırılabilir bırakılması ve gerekirse `OPEN-*` kimliği alması gereken noktalar olarak listelenmiştir.

1. **Araç yüzeyi:** İlk trende hangi görev araçları açılacak (modül başına 3–6 okuma aracının ötesinde)? "Her komut konuşarak" hedefi hangi modül sırasıyla genişlesin?
2. **Varsayılan şerit:** Yazma gerektiren isteklerde varsayılan "ekranı aç ve doldur" mu olsun; sohbetten onaylı doğrudan yürütme hangi komut sınıflarında açılsın?
3. **Doldurulmayacak alanlar:** Yapay zekânın öneri yazamayacağı alan sınıfları (para, miktar, formül sonucu, kimlik/numara serisi, tarih, kurulum sayfaları) onaylanıyor mu; eklenecek var mı?
4. **Sohbetten erişilmeyecekler:** Sohbetle açılamayacak ya da taslaklanamayacak ekran/komut listesi olacak mı (ör. yetki yönetimi, dönem kapanışı, ödeme)?
5. **Zamanlı görev kotaları:** Kullanıcı başına etkin görev sayısı, en sık çalışma aralığı, çalıştırma başına adım/süre/token tavanı, kullanıcı ve şirket günlük bütçesi ne olsun? Bütçe bitince davranış: atla ve bildir mi, duraklat mı?
6. **Görev izinleri:** Zamanlı görev kurma ve olay tetikleyicisi kurma ayrı izinler mi olacak; hangi rollere verilecek (insan kapısı)?
7. **Zamanlayıcı:** Quartz.NET 4 bağımlılığı kabul ediliyor mu, yoksa mevcut barındırılan servis kalıbıyla mı ilerlenecek?
8. **Yapay zekâ kitaplıkları:** Microsoft.Extensions.AI ve Evaluation paketleri alınsın mı, yoksa mevcut ince bağdaştırıcılar mı genişletilsin (pin ve envanter maliyeti)?
9. **E-posta altyapısı:** Hangi posta sistemi üzerinden gönderilecek ve yanıtlar hangi kutuya gelecek; ayrı gönderim alt alan adı açılabilir mi; beklenen günlük hacim ve gönderim penceresi nedir; toplu onayı kim verir?
10. **E-posta içeriğinin niteliği (M — hukuk teyidi):** İletiler operasyonel teyit/mutabakat mı, ticari ileti mi? Ticari ileti mevzuatı ve KVKK açısından hangi onay ve kayıt gerekecek?
11. **Yanıt raporu:** Otomatik sınıflandırma ve alan çıkarımı için kabul eşiği ne olsun; hangi durumlar her zaman insan incelemesine düşsün?
12. **Ayar/yetki önerisi:** Kim öneri taslaklatabilir (herkes mi, yalnız belirli roller mi)? Reddedilen öneri ne kadar süre yeniden önerilmesin? Etki benzetimi geriye dönük kaç günü kapsasın?
13. **Tarifler:** Kim tarif kaydedebilir; şirket kütüphanesine terfiyi kim onaylar; araç sürümü değişince bağlı zamanlı görevler otomatik dursun mu?
14. **Yerel model sunucusu (OPEN-014 ile birlikte):** vLLM mi llama.cpp mi; hedef eşzamanlı kullanıcı ve kabul edilebilir yanıt süresi nedir? Bu, katı kip ve önbellek tasarımını belirler.
15. **Değerlendirme seti:** Gerçek soru ve komut örneklerini kim derleyecek; kritik görevlerde tekrar sayısı (k) ve geçme eşiği ne olsun?
16. **Dış istemci erişimi:** İç araç kataloğunun MCP cephesiyle dış istemcilere açılması isteniyor mu; istenirse kimlik doğrulama ve kapsam nasıl sınırlanacak?
