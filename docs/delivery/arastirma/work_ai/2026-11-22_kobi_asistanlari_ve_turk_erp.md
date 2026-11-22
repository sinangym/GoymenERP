# KOBİ yazılımlarında ve Türk ERP / muhasebe ürünlerinde yapay zekâ asistanları: işini konuşarak yürüten sahip (22 Kasım 2026)

> **Yöntem ve sınırlar (önce okunmalı)**
>
> - Durum tarihi 22 Kasım 2026. Kaynakların çoğu 2025–2026 tarihli; daha eski olanlar "(eski)" diye işaretlendi.
> - **39 web araması** tamamlandı. Sorgularda yalnız genel terimler ve incelenen ürünlerin adları kullanıldı; şirket adı, kişi adı ya da şirkete özgü veri sorgulara girmedi.
> - **Planlanan 12 arama yapılamadı.** Bu turun bütün ajanlarca paylaşılan web arama bütçesi (200 arama) doldu. Yapılamayan konular: HubSpot Breeze'de eylem onayı; Notion'da izin, geri alma ve kredi; monday'de onay adımı; ClickUp otomasyon kurucusu; Airtable denetim kaydı ayrıntısı; Slackbot hafıza denetimleri; Microsoft 365 Copilot hafıza yönetimi; Google Workspace Studio; Gemini Spark; ChatGPT Tasks ve Pulse; metinden iş akışı üretiminin benchmark'ları; proaktif asistanlar üzerine kullanıcı çalışmaları. Bunlar ilgili bölümlerin "Boşluklar" kısmında yazılı.
> - **WebFetch hiçbir sayfayı açamadı.** Denenen 10 hostun hepsinde DNS hatası alındı; oturumun ağ izin listesi bu siteleri kapsamıyor (help.shopify.com, changelog.shopify.com, www.shopify.com, www.usecarly.com, fortune.com, www.accountingtoday.com, www.xero.com, www.odoo.com, knowledge.hubspot.com, www.notion.com). **Bu yüzden aşağıdaki bütün bulgular arama motorunun sayfa özetlerine dayanır; hiçbir birincil sayfa satır satır okunmadı.** Tırnak içindeki İngilizce cümleler arama özetinin aktardığı alıntılardır ve birincil sayfadan teyit edilmelidir.
> - **Kanıt düzeyleri:** **A** hakemli yayın ya da bağımsız denetim · **B** analist, bağımsız test, bağımsız güvenlik araştırması ya da ticari basının kendi incelemesi ("B-anekdot" = kullanıcı forumundaki tekil rapor, "B−" = ikincil blog ya da rehber) · **C** satıcının kendi dokümanı, vakası ya da beyanı (fayda ve performans iddiaları ayrıca **[İDDİA]**) · **D** spekülatif ya da doğrulanmamış · **M** mevzuat (teyit gerekir).
> - "(eşleşme belirsiz)": arama özeti iddiayı verdi ama hangi bağlantıdan geldiğini açıkça göstermedi; aday bağlantılar yazıldı.
> - Repo yalnız okundu; yazılan tek dosya budur. Bağlam olarak `docs/delivery/AI_TREN_PLANI_v2.md`, `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md` (AI-D10–AI-D21) ve `KARAR_SICILI.md` içindeki DEC-AI-001..009 okundu. Hafıza mimarisi, onay yorgunluğu, otonomi ölçütleri ve istem enjeksiyonu savunmaları `docs/delivery/arastirma/2026-11-22_kanitlanmamis_ai_yontemleri.md` dosyasında zaten işlenmiş; burada tekrar edilmedi, yalnız atıf verildi.

## 1. Sahip ya da yönetici bugün konuşarak ne yapabiliyor? Değişiklik nasıl önizleniyor, onaylanıyor, kaydediliyor ve geri alınıyor? Neler reddediliyor ya da insan istiyor?

### Özet çıkarım (Takeaway)

Olgun KOBİ ürünlerinde ortak kalıp "yapay zekâ hazırlar, insan uygular"dır: Shopify Sidekick canlı mağazada onaysız değişiklik yapamaz ve iş akışı düzenlemelerini "Apply changes / Discard changes" ile bekletir, Odoo 19'un standart asistanı veritabanını hiç değiştiremez, Xero yalnız yüksek güvenli eşleşmeleri otomatik yapar. Kullanıcı, rol ve yetki yönetimini sohbetle yapan bir ürün için kaynak bulunamadı; onay adımını atlayan ya da kapatılamayan otomasyon (Intuit) ise en çok şikâyet alan tasarımdır.

### Kaynaklı bulgular (Cited Findings)

(Hepsi arama özetine dayanır.)

**Shopify Sidekick**

- **[C]** Yardım merkezine göre Sidekick müşterilerle iletişim kuramaz ve destek konuşması yürütemez; özel veriyi ya da başka satıcıların verisini paylaşamaz; canlı mağazada onay olmadan değişiklik yapamaz ("can't make changes to your live store without your approval"); tema ve ürün listesindeki görselleri düzenleyemez. Apple Watch'ta soru yanıtlar ama değişiklik yapamaz. İkincil siteler yardım sayfasındaki sınır sayısını farklı veriyor (dört ya da beş); canlı sayfa okunamadı. — [Shopify Help: Sidekick set-up](https://help.shopify.com/en/manual/ai-powered-tools/sidekick/set-up); [Talk Shop: Sidekick limitations (2026)](https://www.letstalkshop.com/blog/shopify-sidekick-limitations-and-what-it-cannot-do)
- **[C]** Asistan kullanıcının yetkisini miras alır: "Sidekick uses your staff permissions. If you can't view Shopify Payments information in your Shopify admin, then Sidekick can't view it for you." Ödeme vadesini yalnız yetki varsa belirler: "Sidekick can set payment terms only when your staff permissions allow it." — [Shopify Help: Sidekick set-up](https://help.shopify.com/en/manual/ai-powered-tools/sidekick/set-up); [Shopify Help: help and guidance](https://help.shopify.com/en/manual/ai-powered-tools/sidekick/help-and-guidance)
- **[C]** Sidekick ile uygulama üretmek kurum düzeyinde "Build and manage apps" yetkisi ister. Üretilen uygulamayı Sidekick'le yalnız onu üreten kişi düzenleyebilir; mağaza sahibi dahil başkası düzenleyemez. Üretilen uygulamalar yalnız mağazanın Admin API'sine erişir; müşteriye görünen veriyi de değiştirebilir. — [Shopify Help: generate apps](https://help.shopify.com/en/manual/ai-powered-tools/sidekick/generate-apps)
- **[C]** Shopify Flow'da Sidekick'in yaptığı düzenleme bekletilir: "When changes are made by Sidekick, click Apply changes or Discard changes to keep or reject the modifications to your workflow." Üretilen iş akışı inceleme için Flow düzenleyicisinde açılır; etkinleştirme ayrı ve elle yapılan bir adımdır ("To activate your workflow, click Turn on workflow."). Üretilen akışlar köken etiketi alır: "Workflows generated by Sidekick are automatically tagged with sidekick." — [Shopify Help: create workflow](https://help.shopify.com/en/manual/shopify-flow/create/create-workflow); [Shopify changelog: Create Flow automations with Sidekick](https://changelog.shopify.com/posts/create-flow-automations-with-sidekick)
- **[C]** Flow'un test koşusu, akış etkinleştirilmeden önce ne yapacağını gösterir. İki Shopify yazısı bunu farklı anlatıyor (biri "örnek veriyle", diğeri "gerçek veriye dokunmadan"); ikisi de testin zarar vermediğini söylüyor. — [Shopify blog: Flow automation updates 2025](https://www.shopify.com/blog/flow-automation-updates-2025); [eCommerce Fastlane (ikincil)](https://ecommercefastlane.com/how-shopify-flow-automation-got-faster-safer-and-smarter-in-2025/)
- **[B−]** Haziran 2026 sürümüyle (150'den fazla güncelleme; kaynaklar "Spring '26 Edition" ve "Summer '26 / Everywhere Edition" adlarını karışık kullanıyor) Sidekick'in her ekranda, yazarak ya da sesle çalıştığı ve 15'ten fazla iş ortağı uygulamasına bağlandığı yazılıyor (ikincil kaynak; eşleşme belirsiz). — [Shopify: Spring '26 Edition](https://www.shopify.com/news/spring-26-edition-merchant); [Shopappy: what Shopify actually announced](https://shopappy.com/ecommerce/shopify-editions-summer-26-what-shopify-actually-announced)

**Intuit Assist / QuickBooks yapay zekâ ajanları**

- **[C]** Accounting Agent'ın işi: "automatically categorize transactions, detect and resolve anomalies, and reconcile accounts". Emin olduğu işlemleri kategoriye, müşteriye ya da tedarikçiye göre gruplayıp önerir; kullanıcı tek tıkla inceleyip kaydeder ya da kaydetmeden önce değiştirir. Intuit anomali özelliğini "with you in control" diye sunuyor. — [QuickBooks Community: AI Accounting Agent duyurusu](https://quickbooks.intuit.com/community/reports-and-accounting-5/introducing-your-quickbooks-online-ai-accounting-agent-transforming-business-finances-60660); [QuickBooks: AI agents](https://quickbooks.intuit.com/global/ai-agents/)
- **[C]** Geri alma kuyruğa geri gönderir: otomatik kategorilenen giderde "Undo" atanmış kategoriyi kaldırır ve gideri "For review" sekmesine taşır; banka işlemlerinde "Undo" Action sütunundadır ve QuickBooks'un oluşturmaya çalıştığı işlemi sıfırlar. Bu yardım makaleleri daha eski olabilir ve bugünkü ajan arayüzüyle birebir örtüşmeyebilir. — [QuickBooks Help: automatically categorize expenses](https://quickbooks.intuit.com/learn-support/en-us/help-article/manage-expenses/automatically-categorize-expenses-quickbooks/L2I4JyDvA_US_en_US); [QuickBooks Help: unmatch downloaded bank transactions](https://quickbooks.intuit.com/learn-support/en-us/help-article/bank-registers/unmatch-downloaded-bank-transactions-move-another/L71oaBb86_US_en_US)
- **[B-anekdot]** Eylül 2025 tarihli forum başlığında bir kullanıcı, yapay zekâ eşleştirmesinin tahsilatları aynı tutarda borcu olan yanlış müşterilere otomatik kaydettiğini yazıyor ve özelliği kapatmak istiyor. QuickBooks ekibinin yanıtı: ajanları kapatma seçeneği o tarihte yok. — [QuickBooks Community: how can I turn off all AI agents](https://quickbooks.intuit.com/community/banking-4/how-can-i-turn-off-all-ai-agents-in-processing-transactions-in-my-banking-24243)
- **[B-anekdot]** Başka bir başlık kategorilerin kendiliğinden değiştiğinden yakınıyor ("like ai autocorrect is making things up"). Önerilen çözüm, önerilen kategorilendirme ayarını ve koşula uyan her işleme otomatik uygulanan banka kurallarını kapatmak. — [QuickBooks Community: changing categories](https://quickbooks.intuit.com/community/reports-and-accounting-5/nightmare-with-qb-lately-changing-categories-like-ai-autocorrect-is-making-things-up-horrible-anyone-else-with-similar-nightmare-51745)
- **[C]** Intuit'in kendi uyarısı: "We recommend you review all outputs carefully." Hesapta başka dillerde bilgi varsa ajan çıktılarında sınırlamalar olabileceği de belirtiliyor (eşleşme belirsiz). — [QuickBooks: accounting agent for advisors](https://quickbooks.intuit.com/global/resources/accountants/quickbooks-accounting-agent-advisors/); [QuickBooks: meet your partner in business](https://quickbooks.intuit.com/global/resources/product-update/meet-your-partner-in-business/)

**Xero JAX**

- **[C]** Ağustos 2026'daki Xerocon US duyurusunda Xero yaklaşımını "Accountable Intelligence" diye adlandırıyor. JAX mutabakatta yüksek güvenli işlemleri banka akışıyla eşleştiriyor ve istisnaları insana bırakıyor. Bill Protection faturaları ödemeden önce inceleyip olası dolandırıcılığı işaretliyor. XeroForce'a eklenen ay sonu ajanı erken erişimde. — [Xero basın bülteni: Xerocon US](https://www.xero.com/media-releases/new-ai-innovations-xerocon-denver/); [Xero blog: Xerocon Denver 2026](https://blog.xero.com/nz/product-updates/xerocon-denver-2026-agentic-workflows-ai-era/)
- **[B]** Belge isteme akışı "yakında" diye duyuruldu: JAX müşteriden eksik belgeyi ister, hatırlatma gönderir, soruları yanıtlar ve belgeyi işlemle eşleştirir; kullanıcı her adımı onaylar. Bölünmüş ödeme gibi karmaşık durumlar sonraki aylara bırakıldı. — [Accounting Today: Xerocon 2026](https://www.accountingtoday.com/news/xerocon-2026-jax-ai-improvements-focused-on-automating-unbillable-admin-work)
- **[C]** Xero, otomatikleştirdiği her şeyin kullanıcının denetleyebilmesi için günlüğe yazıldığını söylüyor ("everything it automates is logged for you to check"; eşleşme belirsiz). — [Xero: Just Ask Xero](https://www.xero.com/my/just-ask-xero/)
- **[B]** Gerçek müşteri dosyalarında yapıldığı söylenen bir denemeye göre düşük güvenli eylemler otomatik uygulanmıyor, insan incelemesine işaretleniyor. — [Digit Business: Xero JAX explained](https://digit.business/insights/xero/xero-jax-explained-australian-business)

**Zoho Zia ve Zia Agents**

- **[C]** Zoho Books'ta Ask Zia istenen bilgiyi gösterir "or performs the desired action in the background, and notifies you once complete". Yapay zekâ özellikleri Settings → AI Preferences'tan açılır; sağlayıcı ve model orada seçilir. — [Zoho Books Help: AI features](https://www.zoho.com/us/books/help/ai-features/ai-features.html)
- **[C/B]** Zoho CRM'de düz metin istemle özel iş akışı kurulabiliyor. Ask Zia ile rapor kullanıcının yetkilerine göre üretiliyor ve kullanıcı kurulumu canlı izleyebiliyor. — [destinationCRM: Zia AI capabilities to CRM for Everyone](https://www.destinationcrm.com/Articles/CRM-News/CRM-Across-the-Wire/Zoho-Adds-Zia-AI-Capabilities-to-CRM-for-Everyone-169537.aspx); [Zoho: AI in Zoho CRM](https://www.zoho.com/crm/ai-features-in-zoho-crm.html)
- **[C]** Zoho Flow'da metinden üretilen iş akışı önizlenir ve kullanılmadan önce değiştirilebilir; özellik için AI'ın Settings → AI Setup'tan açılması gerekir. — [Zoho Flow Help: text to flow](https://help.zoho.com/portal/en/kb/flow/user-guide/ai-features/articles/generating-workflows-using-zia-text-to-flow)
- **[C]** Zia Agents (Şubat 2025) ve Agent Studio: istemle, kodsuz ajan kurma; Zoho ürünlerine yayılan 700'den fazla yerleşik eylem; hazır ajanlar (Account Manager, SDR, HR, Customer Support, IT Help Desk, Sales Coach); Agent Marketplace. Agent Studio sayfasında hâlâ erken erişim formu var; yayılım bölgeye ve ürüne göre değişebilir. — [Business Wire: Zia Agents](https://www.businesswire.com/news/home/20250204066995/en/Zoho-Corporation-Announces-Zia-Agents-AI-Platform-Supporting-Autonomous-Agents-Across-Organizations-Broad-Portfolio); [Futurum: Zia LLM, Agent Studio](https://futurumgroup.com/insights/zoho-unveils-zia-llm-no%e2%80%91code-agent-studio-and-open-agent-interoperability/); [Zoho: Zia Agent Studio](https://www.zoho.com/zia/agents/agent-studio.html)
- **[C]** SalesIQ'nun 2026 yaz sürümünde portal başına 10 ajana kadar izin var. Ajan sohbette yakaladığı adayı Zoho CRM'e doğrudan yazıyor ve kaydın sahibi (Lead Owner) olarak Zia Agent atanıyor. — [Zoho blog: Zia Agents in SalesIQ](https://www.zoho.com/blog/salesiq/meet-zia-agents-autonomous-ai-workforce.html)

**Odoo**

- **[C]** Odoo 19 dokümanı: "The standard Ask AI agent cannot make changes to the database. As such, it can open views and display reports, but it cannot create leads or alter data." Ajanlar Topics ve Sources bileşenlerinden kurulur; ajan oluşturmak AI uygulamasının kurulu olmasını ister; önizlemede açık olan belge konuşmaya bağlam olur. — [Odoo 19 docs: AI agents](https://www.odoo.com/documentation/19.0/applications/productivity/ai/agents.html); [Odoo 19 docs: AI](https://www.odoo.com/documentation/19.0/applications/productivity/ai.html)
- **[B−]** Odoo 20 (Eylül 2026) için ikincil kaynaklar şunları yazıyor: ajanlar kayıt oluşturup güncelleyebiliyor (yüklenen bir talimat dosyasından da); "AI düşünüyor" yerine ne yaptığını adım adım canlı gösteriyor; Claude, Gemini CLI ve n8n gibi MCP istemcileri kapsamı sınırlı erişimle ve kullanıcı yetkilerine uyarak bağlanabiliyor; otomatik ve zamanlı eylemler ajan çağırabiliyor ve otomasyon sohbetleri sonradan incelenebiliyor; bütün yapay zekâ özellikleri kredi (IAP) istiyor; ajan konuşmaları son mesajdan sonra en çok 30 gün saklanıyor. Resmî sürüm notu okunamadı; bazı yazılar özelliklerin geliştirme dalından alındığını ve değişebileceğini söylüyor. — [Cybrosys: Odoo 20 features](https://www.cybrosys.com/odoo/odoo-20-features/); [Webkul: Odoo 20 what's new](https://webkul.com/blog/odoo-20-whats-new-key-features-and-comparison/); [Ecosire: what's new in Odoo 20](https://ecosire.com/blog/whats-new-in-odoo-20); [Odoo 20 release notes (okunamadı)](https://www.odoo.com/zh_TW/odoo-20-release-notes)

**Notion, monday.com, ClickUp, Airtable, Slack**

- **[C]** Notion Custom Agents yalnız açıkça erişim verilen içeriği kullanır ("Custom Agents only use content you explicitly grant them access to for their work"). — [Notion Help: Custom Agents](https://www.notion.com/help/custom-agent)
- **[B]** PromptArmor'un Ocak 2026 bulgusu: Notion AI'da belge düzenlemeleri kullanıcı onayından **önce** kaydediliyordu; gizli yönerge içeren bir PDF, belge metnini saldırganın alan adına taşıyan bir görsel bağlantısı ekletebiliyor ve kullanıcı değişikliği reddetse bile veri sızıyordu. Notion bulguyu önce "Not Applicable" diye kapattı, sonra doğrulayıp düzeltme yayımladı. — [PromptArmor: Notion AI data exfiltration](https://promptarmor.com/resources/notion-ai-unpatched-data-exfiltration)
- **[B]** Eylül 2025'te Notion 3.0 ajanlarında, PDF'e gizlenmiş bir istemin ajanın web arama aracı üzerinden özel veriyi dışarı gönderebildiği gösterildi. Notion tespit sistemlerini güncellediğini ve şüpheli ya da modelin ürettiği bağlantıyı açmadan önce kullanıcı onayı istediğini açıkladı. — [Schneier on Security](https://schneier.com/blog/archives/2025/09/abusing-notions-ai-agent-for-data-theft.html); [Simon Willison](https://simonwillison.net/2025/Sep/19/notion-lethal-trifecta); [The Decoder: security update](https://the-decoder.com/notion-ai-agents-get-security-update-after-data-leak/)
- **[C]** monday sidekick "can create workflows, automations, dashboards, and forms from a simple request"; Ocak 2026 topluluk duyurusuna göre beta'dan çıkıyor ve monday'de yapay zekâya ana giriş noktası oluyor. Elevate '26'ya göre herhangi bir ekibin kurduğu ajan, ajan dizini üzerinden kurumdaki herkese açık. — [monday: sidekick](https://monday.com/w/sidekick); [monday community: AI 2026](https://community.monday.com/t/ai-2026-what-s-new-and-what-s-coming/123164); [monday blog: Elevate '26](https://monday.com/blog/product/elevate-26-ai-workspace-updates/)
- **[C]** ClickUp Super Agents doğal dille kurulur: kullanıcı işi ClickUp Brain'e anlatır, kurucu sorularla ajanın davranışını, bilgisini ve araçlarını belirler. Sıfırdan, hazır katalogdan ya da Brain'e kurdurarak başlanabilir; ClickUp 4.0 ve AI ClickApp gerekir. Eski "Autopilot Agents" tetikleyici ve koşula bağlıdır, Super Agents daha uyarlanabilirdir. — [ClickUp Help: What are Super Agents](https://help.clickup.com/hc/en-us/articles/31010910371991-What-are-Super-Agents); [ClickUp Help: Create a Super Agent](https://help.clickup.com/hc/en-us/articles/37033397273111-Create-a-Super-Agent)
- **[C]** Airtable Omni uygulama kurar, web'de araştırır, veri ve belge inceler, kayıt oluşturur ve günceller. Uygulama kurma sayfası Omni'nin kullanıcının izinlerini kullandığını ve tümüyle denetlenebilir olduğunu söylüyor. Ekim 2026 sonbahar sürümünde ajan denetim kayıtları geldi, ajanın tam olarak neyi okuyup yazabileceğini belirleyen ajan izinleri "yakında" diye duyuruldu. — [Airtable Support: Using Omni](https://support.airtable.com/articles/1744327578-using-omni-ai-in-airtable); [Airtable: app building](https://www.airtable.com/platform/app-building); [Airtable Community: fall release](https://community.airtable.com/announcements-6/fall-release-omni-2-0-airtable-canvas-a-bigger-mcp-agent-audit-logs-and-upcoming-agent-permissions-48850)
- **[C]** Slackbot (Ocak 2026'dan beri Business+ ve Enterprise+ müşterilerine kademeli) belge taslağı hazırlar, toplantı planlar, konuşmalardan ve bağlı sistemlerden bilgi çeker; "Slackbot only taps into information you have permission to access". — [UC Today: Slackbot as personal AI agent](https://www.uctoday.com/productivity-automation/salesforce-launches-slackbot-as-personal-ai-agent-to-eliminate-context-switching/); [Slack blog: Meet Slackbot](https://slack.com/blog/news/slackbot-context-aware-ai-agent-for-work)

### Çıkarımlar (Inferences)

- **Asistan kullanıcının yetkisini miras alır, aşamaz.** Shopify, Slack, Zoho CRM (rapor) ve Airtable aynı ilkeyi beyan ediyor. Bu, AI-D15'teki "tek sistem yapay zekâsı, kullanıcının yetkisiyle" kararının piyasadaki karşılığıdır.
- **Üç ayrı onay tasarımı var.** (a) Bekletilen değişiklik: Shopify Flow "Apply / Discard" ve ayrı etkinleştirme. (b) Güven eşiği: Xero yüksek güvenliyi otomatik yapar, kalanı insana bırakır. (c) Arka planda yap, sonra bildir: Zoho Books Ask Zia. DEC-AI-003 (her yazma için önizleme hash'i, idempotency ve kullanıcı onayı) yalnız (a) ile uyumludur. (b) ancak AI-D21 kapsamında, geri alınabilir ve düşük riskli iş türlerinde düşünülebilir. (c) bizde uygulanamaz.
- **Önizleme "gerçekten" beklemeli.** Notion vakası, onaydan önce kaydedilen ya da önizleme sırasında dış istek tetikleyen taslağın (dış görsel, bağlantı) onay kapısını boşa çıkardığını gösteriyor. Bizde taslak ilgili modülün taslak durumunda durmalı ve önizleme ekranı yapay zekânın ürettiği içerikten hiçbir dış kaynak yüklememeli.
- **Odoo'nun sırası planımızla aynı yönde.** Odoo 19'da standart asistan yalnız okuyordu, Odoo 20'de ajanlar kayıt yazıyor (ikincil kaynak). AI_TREN_PLANI_v2'deki "önce okuma, açıklama ve taslak; yazma sonra (AI-24)" sırası bu ilerleyişle örtüşüyor.
- **Yetki ve kullanıcı yönetimini sohbetle yapan ürün örneği bulunamadı.** Arama sonuçlarına göre piyasada bu işin örneği yok görünüyor (yapılamayan aramalar nedeniyle kesin değil). AI-D16'daki "yapay zekâ yalnız yetki talebi taslağı hazırlar, atama insan kapısıdır" kuralı için kopyalanacak hazır bir kalıp yok; tasarım bize ait olacak.
- **Köken etiketi ve ajan kimliği ucuz ve yararlı.** Shopify üretilen akışı "sidekick" diye etiketliyor, Zoho kaydın sahibi olarak ajanı yazıyor, Airtable ajan denetim kaydı ekledi. Bizde DEC-AI-005 denetim alanlarına ek olarak her taslakta "yapay zekâ ile hazırlandı" işareti ve konuşma bağlantısı tutulabilir. Kaydın iş sahibi yine insan kalmalı (Zoho'daki "sahip = ajan" kalıbı sorumluluğu bulanıklaştırır).

### Boşluklar (Gaps)

- Shopify Sidekick'in yaptığı değişikliklerin denetim kaydı ve genel bir "geri al" işlevi için kaynak bulunamadı. Mağaza ayarlarının (vergi, kargo, ödeme) sohbetle değiştirilmesinin kapsamı ve onay ekranının görünümü de doğrulanamadı; yalnız "onaysız değişiklik yapamaz" ifadesi ve ödeme vadesi örneği var.
- HubSpot Breeze Assistant'ın kayıt oluşturma ve güncellemede onay isteyip istemediği aranamadı (bütçe).
- monday sidekick ve ClickUp'ta değişiklikten önce onay adımı, Notion'da geri alma ve sürüm geçmişi aranamadı (bütçe).
- Zoho CRM'de Ask Zia'nın kayıt ya da iş akışı kaydetmeden önce onay isteyip istemediği arama sonuçlarında yok.
- Intuit ajanları için kapatma seçeneğinin 2026'da eklenip eklenmediği bulunamadı. Bir üçüncü taraf rehberi ajanların Ağustos–Eylül 2025'te bütün QuickBooks Online kullanıcılarına kapatma seçeneği olmadan açıldığını yazıyor; Intuit dokümanıyla doğrulanamadı. — [Carly: QuickBooks AI (okunamadı)](https://www.usecarly.com/blog/quickbooks-ai)
- Xero'nun zor işler için yayımlanmış hata oranı ve muhasebecinin müşteriler arasında geçiş yaptığı durumda yetki sınırlarının korunduğuna dair kanıt bulunamadı.

## 2. Proaktif özellikler (günlük özet, anomali uyarısı, öneri): gürültü nasıl önleniyor, fayda ya da rahatsızlık kanıtı var mı?

### Özet çıkarım (Takeaway)

En net belgelenmiş proaktif katman Shopify Sidekick Pulse: mağaza başına en çok beş öneri, yönetim paneli ana sayfasında kart olarak, yalnız sahip ya da yönetici erişimli kullanıcıya ve yeterli satış verisi olan mağazalara. Faydasını ölçen bağımsız bir kaynak bulunamadı; rahatsızlık kanıtı ise var (QuickBooks kullanıcılarının "ekranı dolduran, işe yaramayan yapay zekâ kutuları" ve kapatma seçeneği olmaması şikâyeti).

### Kaynaklı bulgular (Cited Findings)

(Hepsi arama özetine dayanır.)

- **[C]** Shopify, 10 Aralık 2025 tarihli değişiklik günlüğünde Sidekick'in proaktif iş önerilerini duyurdu: birkaç haftada yayılacak, mağaza başına en çok beş öneri gösterecek. Pulse, soru beklemeden mağaza verisi ve pazar eğilimleri üzerinde kendi araştırmasını yapıyor. — [Shopify changelog: proactive business recommendations](https://changelog.shopify.com/posts/proactive-business-recommendations-from-sidekick); [Shopify: Winter '26 Edition](https://www.shopify.com/news/winter-26-edition-merchant)
- **[C]** Pulse koşulları: yalnız yakın zamanda satışı olan bazı mağazalarda var; kullanıcı mağaza sahibi olmalı ya da yönetici erişimi taşımalı; "Shopify Network Intelligence" etkin olmalı. — [Shopify Help: Sidekick Pulse](https://help.shopify.com/manual/ai-powered-tools/sidekick/pulse)
- **[B−]** Pulse kartları yönetim panelinin ana sayfasında, önerilen eylemlerle birlikte görünüyor. Bazı üçüncü taraf rehberleri Pulse'un e-posta özeti gönderdiğini yazıyor; HyperSKU mevcut sürümde bunun doğru olmadığını söylüyor (çelişki; ilk cümlenin eşleşmesi belirsiz). — [HyperSKU: Shopify AI in 2026](https://www.hypersku.com/v2/blog/shopify-ai-2026/); [EasySell: Pulse setup (2026)](https://easysellapp.com/de/blogs/wiki/shopify-sidekick-pulse-setup-first-actions-2026)
- **[B−]** Shopify uygunluk ölçütlerini açık yayımlamamış; sipariş geçmişi az olan çok yeni mağazalar az kart alabilir ya da hiç almayabilir. Nisan 2026 tarihli bir rehbere göre Pulse ayarlardan açılır, hangi kanalları okuyacağı seçilir ve ilk içgörüler genelde bir gün içinde gelir. — [Talk Shop: Sidekick Pulse how to use](https://www.letstalkshop.com/blog/shopify-sidekick-pulse-how-to-use)
- **[B−]** Bir analiz yazısına göre Pulse gürültüyü azaltmak için içgörüleri olası iş etkisine göre sıralıyor (eşleşme belirsiz). — [Flatline Agency: Sidekick Pulse](https://www.flatlineagency.com/blog/sidekick-pulse-transforms-store-data)
- **[C]** Xero'nun Cash Flow Actions özelliği nakit açığını oluşmadan fark etmeyi amaçlıyor; Bill Protection ödeme öncesinde olası dolandırıcılığı işaretliyor. — [Xero blog: Xerocon Denver 2026](https://blog.xero.com/nz/product-updates/xerocon-denver-2026-agentic-workflows-ai-era/); [Xero basın bülteni](https://www.xero.com/media-releases/new-ai-innovations-xerocon-denver/)
- **[C]** Zoho Books'ta anomali tespiti raporlar modülünde, grafik görünümünden seçilerek çalışır (kullanıcı çağırır); amaç raporlardaki olağandışı örüntü ve hataları görmek. Aynı ürünün tanıtım yazısı ise Zia'nın muhasebe verisini sürekli izleyip olağandışı örüntüleri işaretlediğini söylüyor **[İDDİA]**. — [Zoho Books Help: AI features](https://www.zoho.com/us/books/help/ai-features/ai-features.html); [Zoho Books Academy: accounting on autopilot](https://www.zoho.com/books/academy/accounting-trends-and-insights/how-zohobooks-and-zia-run-accounting-on-autopilot.html)
- **[C]** Gemini'de zamanlanmış bir işin sonucu mobilde bildirim olarak gelir, web'de arayüzde görünür; bildirim o çalıştırmanın çıktısını içerir. — [Google Help: Schedule actions in Gemini Apps](https://support.google.com/gemini/answer/16316416?hl=en-GB)
- **[B-anekdot]** Rahatsızlık: Intuit topluluğunda Ocak 2026'da bir kullanıcı "the AI boxes clutter my screen and don't help" diye yazıyor; Haziran 2025'te bir başkası müşterilerin yapay zekâdan kolayca çıkabilmesini istiyor ("customers should have a choice and easy way to OPT OUT of AI Assist"). — [QuickBooks Community: turn off AI assist](https://quickbooks.intuit.com/community/other-questions-9/turn-off-ia-assist-89112/index1.html)
- **[C + B-anekdot]** Shopify yardım sayfaları yalnız beğen ve beğenme düğmelerini anlatıyor. Bir topluluk başlığına göre Sidekick ürün sorunlarını sohbetin içinden Shopify ekiplerine iletemiyor, kullanıcıyı ayrıca topluluk forumuna yönlendiriyor ("It tells me I have to do this separately within a Community forum"). — [Shopify Community: when will Sidekick create issue logs](https://community.shopify.com/t/when-will-sidekick-create-issue-logs-for-shopify-resolution/590321)
- **[C] [İDDİA]** Fayda beyanı: yönetici düzeyindeki bir açıklamaya göre Sidekick'in kurulum yönlendirmesi, 15 gün içinde beş siparişe ulaşan yeni satıcı oranını %8 artırdı (eşleşme belirsiz; kazanç çağrısı dökümü bulunamadı). — [Marketing Brew: Shopify agents and AI](https://www.marketingbrew.com/stories/shopify-is-pushing-forward-with-agents-and-ai); [CP24: Shopify Q4 sonuçları](https://www.cp24.com/news/canada/2026/02/11/shopify-reports-us743m-q4-profit-revenue-up-31-per-cent-from-year-ago/)

### Çıkarımlar (Inferences)

- **Gürültüyü azaltan kalıplar belgelenmiş beş tane:** (1) sabit ve küçük üst sınır (en çok beş), (2) itme bildirimi yerine ana sayfada kart, (3) yalnız yetkili role gösterme (sahip ya da yönetici), (4) veri eşiği (yeterli geçmiş yoksa öneri yok), (5) olası etkiye göre sıralama. Bunların hepsi AI-18 sabah özetine ve AI-26 tek iş kutusuna doğrudan uygulanabilir.
- **Proaktif öneri veri paylaşım izniyle birlikte geliyor.** Pulse için "Network Intelligence"in açık olması şart. Bizde karşılığı, hangi veri sınıflarının özet ve uyarıya girebileceğinin insan kapısı olmasıdır (AI-D6/D7).
- **Kapatılamayan proaktif arayüz tepki çekiyor.** Intuit şikâyetleri DEC-AI-001 ("yapay zekâ isteğe bağlıdır") kararını destekliyor: kullanıcı öneri kartlarını tür bazında susturabilmeli; asistan kapalıyken ekran eksiksiz çalışmalı.
- **Geri bildirim görünür bir sonuca bağlanmalı.** Shopify'da beğenmeme düğmesi var ama kullanıcı sorunu ürün ekibine sohbetten iletemiyor. Bizde "faydasız" ve "sorun bildir" tek tıkla AI Merkezi kuyruğuna (AI-19, AI-20) ve gerekirse eksik araç talebine (AI-D19c) düşebilir.
- **Önceden hazırlanan özet bayatlayabilir.** Gemini zamanlı yanıtı teslimden önceki saat içinde hazırlıyor (bkz. bölüm 3). Sabah özetinde her kartın "veri saati" yazmalı; kullanıcı kartı açınca sayı canlı okunmalı.

### Boşluklar (Gaps)

- Pulse önerilerinin açılma, kabul ya da uygulanma oranı için hiçbir rakam bulunamadı; bağımsız değerlendirme de yok. Pulse'ta öneri türlerini susturma ya da sıklık ayarı olup olmadığı doğrulanamadı.
- Intuit ve Xero'da proaktif uyarıların kullanıcı tarafından nasıl ayarlandığı (eşik, sıklık) bulunamadı.
- Proaktif asistanların zamanlaması ve rahatsızlığı üzerine akademik kullanıcı çalışmaları aranamadı (bütçe). Onay yorgunluğu ve otomasyon yanlılığı için bkz. `docs/delivery/arastirma/2026-11-22_kanitlanmamis_ai_yontemleri.md` (bölüm E); istisna öncelikli ana sayfa fikri için bkz. `docs/delivery/arastirma/2026-11-22_kanitlanmamis_is_erp_fikirleri.md` (A1).
- Slack'in günlük kanal özetleri ve ChatGPT Pulse aranamadı. Araştırmacının eğitim verisinden hatırladığı, bu oturumda kaynakla doğrulanmamış bilgi (**D, teyit gerekir**): Slack AI'da kullanıcının seçtiği kanallar için sabah teslim edilen günlük özet vardı; ChatGPT Pulse (2025 sonbaharı) gece boyunca araştırma yapıp sabah kişiye özel kartlar sunuyor ve kullanıcının beğen/beğenme ile yön vermesine dayanıyordu.

## 3. Zamanlı görevler ve yinelenen araştırma ("her sabah şunu yap"): yetenekler, sınırlar, hata yönetimi, maliyet denetimi

### Özet çıkarım (Takeaway)

Zamanlı görev genel amaçlı asistanlarda var (Gemini, Microsoft 365 Copilot, Notion, Slackbot, ClickUp, Odoo 20); muhasebe ve e-ticaret asistanlarında (Sidekick, Intuit, Xero) belgelenmiş bir karşılığı bulunamadı. Ortak sınır az sayıda etkin görevdir (Gemini'de 10, Copilot destek sayfasına göre 10). Hata yönetimi zayıf belgelenmiş: Copilot'ta Mayıs 2026'da bütün zamanlı istemlerin başarısız olduğu raporlandı, Gemini için başarısız çalıştırma bildirimi bulunamadı.

### Kaynaklı bulgular (Cited Findings)

(Hepsi arama özetine dayanır.)

**Google Gemini**

- **[C]** "You can have up to 10 active scheduled actions at a time." Eylem tek seferlik ya da yinelenen olabilir; Ayarlar'daki "Scheduled actions" bölümünden yönetilir; duraklatılmış ya da pasif eylemler etkin olanlardan ayrı sayılır. — [Google Help: Schedule actions in Gemini Apps](https://support.google.com/gemini/answer/16316416?hl=en-GB); [Android Authority: rollout](https://androidauthority.com/gemini-scheduled-actions-rollout-3565152)
- **[C]** Gemini zamanlı yanıtı teslimden önceki saat içinde hazırlar; bu yüzden özellik canlı ya da zaman açısından kritik bilgiye uygun değildir. — [Google Help: Schedule actions in Gemini Apps](https://support.google.com/gemini/answer/16316416?hl=en-GB)
- **[C]** Ayrı bir özellik olan Gemini Spark'ta, o anda 15 görev zaten çalışıyorsa zamanlama çalışmaz; kullanım sınırı da çalışmamaya yol açabilir. Aynı sayfa Spark zamanlamalarının sohbetteki zamanlı eylemlerden farklı olduğunu belirtiyor. — [Google Help: schedules for tasks in Gemini Spark](https://support.google.com/gemini/answer/17094710)
- **[C]** Workspace'te yapay zekâ kullanım sınırları sürüme göre değişir; bazı özellikler 1 Kasım 2026'da zorunlu sınıra geçti. Workspace Studio'da kurulan akışlar ayrı bir sınıra tabidir ve sınırlar özelliğe göre günlük ya da aylık sıfırlanır. — [Google Workspace: About AI usage limits](https://knowledge.workspace.google.com/admin/generative-ai/workspace-with-gemini/about-ai-usage-limits); [Google Workspace: usage limits in Workspace Studio](https://knowledge.workspace.google.com/ai/usage-limits-workspace-studio)
- **[B]** TechRadar'ın lansman haberi özelliği ChatGPT'ye yetişme hamlesi olarak sunuyor; yani zamanlı görev ChatGPT'de daha önce vardı. — [TechRadar: Gemini scheduled actions](https://www.techradar.com/computing/artificial-intelligence/geminis-new-scheduled-actions-feature-puts-catching-up-with-chatgpt-on-its-dayplanner)

**Microsoft 365 Copilot**

- **[C]** Zamanlı istem için Copilot lisansı ve Copilot Chat, Teams'te Copilot ya da Outlook gerekir. Destek sayfasına göre en çok 10 farklı zamanlı istem oluşturulabilir. — [Microsoft Support: Schedule your most used Copilot prompts](https://support.microsoft.com/microsoft-365-copilot/schedule-your-most-used-copilot-prompts)
- **[B−]** Ocak 2026 tarihli bir üçüncü taraf blog yazısı, zamanlamanın 15 çalıştırmayla sınırlı olduğunu ve çalıştırmaların lisansa dahil olmayıp kredi tükettiğini yazıyor. Microsoft dokümanında bunu doğrulayan bir ifade bulunamadı; destek sayfasındaki "10 istem" bilgisiyle çelişiyor olabilir ya da farklı bir sınırı anlatıyor olabilir. — [Bright Ideas Agency: The 15-Run Limit](https://brightideasagency.com/blog/halfbaked-in-copilot)
- **[C]** Yöneticiler zamanlı istemleri yönetebilir. Bağlı deneyimler kapatılırsa kurumda kimse zamanlı istemleri göremez. Açık önizleme döneminde oluşturulan istemler Power Automate'te, 31 Aralık 2026'dan önce süreleri dolana kadar çalışmayı sürdürür. — [Microsoft Learn: scheduled prompts](https://learn.microsoft.com/copilot/microsoft-365/scheduled-prompts); [Microsoft Learn: scheduled prompts environment](https://learn.microsoft.com/en-us/copilot/microsoft-365/scheduled-prompts-environment)
- **[B-anekdot]** Mayıs 2026 tarihli bir Tech Community başlığında bütün zamanlı istemlerin "couldn't be completed" hatasıyla başarısız olduğu, "Run now"ın da çalışmadığı yazılıyor. Bir kullanıcı istemi kapatıp açınca yeniden denemede çalıştığını söylüyor, bir başkası Microsoft'ta destek kaydı açmış. Bir topluluk yanıtı sorunu Power Platform ortamının hazırlanmasına bağlıyor; resmî tanı değil. — [Microsoft Tech Community: all scheduled prompts failing](https://techcommunity.microsoft.com/discussions/microsoft365copilot/all-scheduled-prompts-failing-%E2%80%94-couldnt-be-completed-error-%E2%80%94-power-platform-prov/4516704)
- **[B-anekdot]** Ayrı bir başlık Copilot Tasks'ta "haftalık görev sınırına ulaştınız" uyarısından söz ediyor. — [Microsoft Tech Community: weekly tasks limit](https://techcommunity.microsoft.com/discussions/microsoft-copilot/how-to-avoid-tasks-copilot-youve-reached-our-weekly-tasks-limit/4522426)

**Notion, Slack, ClickUp, Odoo, Shopify**

- **[C + B−]** Notion Custom Agents Şubat 2026'da açık betaya çıktı. Yinelenen zamanlama günlük, haftalık, aylık olabilir. Ajanın Triggers ayarında sıklık ve saat dilimi seçilir, sıradaki çalıştırma zamanı gösterilir. Diğer tetikleyiciler veritabanı olayı, Slack mesajı ve e-postadır; bir ajanda birden çok tetikleyici birleştirilebilir. Business ya da Enterprise plan gerekir; 4 Mayıs'tan itibaren ajanlar ek paket olarak satılan Notion kredisiyle çalışır. — [Notion Help: Custom Agents](https://www.notion.com/help/custom-agent); [IT Brief: Notion unveils Custom Agents](https://itbrief.com.au/story/notion-unveils-custom-agents-to-automate-team-workflows)
- **[C]** Slackbot'ta yinelenen iş, ne olması gerektiği ve ne zaman olacağı düz dille anlatılarak kurulur; verilen örnek haftalık satış hattı özetidir. 31 Mart 2026 tarihli yazı derin araştırma, yapay zekâ becerileri ve hafızayı duyuruyor. — [Slack blog: agent orchestration](https://slack.com/blog/news/agent-orchestration)
- **[B−]** ClickUp ajanları elle, zamanlamayla ya da otomasyondan tetiklenebilir (eşleşme belirsiz). — [ClickUp updates and changelog (2026)](https://clickup.com/learn/topic/project-management/tools/clickup/news/)
- **[B−]** Odoo 20'de otomatik ve zamanlı eylemler ajan çağırabilir; ajanın işi nasıl yürüttüğünü görmek için otomasyon sohbetleri incelenebilir. — [Cybrosys: Odoo 20 features](https://www.cybrosys.com/odoo/odoo-20-features/)
- **[B−]** Shopify Sidekick'te zamanlı ya da yinelenen görev olduğunu doğrulayan kaynak bulunamadı. En yakın özellik, istemlerin yinelenen işler (sosyal medya gönderisi, ürün açıklaması, satış raporu) için yeniden kullanılabilir "Skills" olarak kaydedilmesi; tek bir ikincil kaynakta geçiyor, tarihi ve çerçevesi belirsiz. — [DataSolution: Shopify Winter Editions 2026](https://www.datasolution.fr/en/shopify-winter-editions-2026-major-announcements/)

**Maliyet ve kota**

- **[C]** monday.com: Enterprise hesaplarda Sidekick Plus dahil; Standard ve Pro hesaplarda koltuk başına günde 5 ücretsiz mesaj var, kullanım artırılabiliyor. — [monday community: AI 2026](https://community.monday.com/t/ai-2026-what-s-new-and-what-s-coming/123164)
- **[C]** HubSpot Breeze Assistant'ta içerik üretimi için hız sınırı var; sayfanın farklı sürümleri farklı rakam veriyor (dakikada 30 ve günde 1.000; ya da hesap başına kayan bir dakikada 100). Bulunan en yeni sürüm 4 Kasım 2026 tarihli; hangi rakamın geçerli olduğu doğrulanamadı. — [HubSpot Knowledge Base: Use Breeze Assistant](https://knowledge.hubspot.com/ai/use-breeze-assistant)
- **[C]** Airtable Omni yapay zekâ kredisi ister; Odoo 20'de bütün yapay zekâ özellikleri kredi ister (ikincil); Microsoft'ta sınırlar özelliğe, giriş noktasına ve sistem koşullarına göre değişir. — [Airtable Support: Using Omni](https://support.airtable.com/articles/1744327578-using-omni-ai-in-airtable); [Webkul: Odoo 20](https://webkul.com/blog/odoo-20-whats-new-key-features-and-comparison/); [Microsoft Support: AI credits and limits](https://support.microsoft.com/Microsoft-365-Copilot/ai-credits-and-limits-for-microsoft-365-subscriptions)
- **[B]** Satıcı tarafında maliyet baskısı: Shopify'ın 2026 ilk çeyreğinde abonelik çözümlerinin brüt marjı %80'de, bir yıl öncesiyle aynı kaldı; destek verimliliği ve ölçek kazançlarını, başta Sidekick olmak üzere yapay zekâ ürünlerinin artan kullanımına bağlı LLM maliyetleri kısmen götürdü (Zacks analizi). — [Can Shopify Balance Sidekick Growth With Higher LLM Costs?](https://www.metatrader.com/it/news/1706819-can-shopify-balance-sidekick-growth-with-higher-llm-costs)

### Çıkarımlar (Inferences)

- **Zamanlı görev piyasada dar ve sınırlı bir özellik; sahibin isteği piyasanın ilerisinde.** Etkin görev sayısı 10 dolayında tutuluyor, sonuç bildirim olarak veriliyor ve belgelenen örneklerin hepsi okuma ve özetleme işi. AI-D15'teki "zamanlı görev yazma yapmaz; okur, özetler, bildirir" kuralı piyasadaki uygulamayla aynı.
- **Kopyalanacak form alanları belli:** sıklık, saat dilimi ve "sıradaki çalıştırma" gösterimi (Notion); etkin ve duraklatılmış ayrımı (Gemini); sonucun bildirimin içinde gelmesi (Gemini).
- **Piyasanın eksiği bizim fırsatımız: hata görünürlüğü.** Copilot arızası ve Gemini'de belgelenmemiş hata davranışı, kullanıcının görevinin çalışıp çalışmadığını bilemediğini gösteriyor. AI-18'de her görev için çalıştırma geçmişi, başarısızlıkta bildirim, "şimdi çalıştır" denemesi ve yeniden deneme kuralı baştan olmalı.
- **Eşzamanlılık ve bütçe tavanı şart.** Gemini Spark'taki "15 görev çalışıyorsa yenisi çalışmaz" kuralı ve kredi sistemleri, sınırsız zamanlamanın satıcılar için bile taşınamaz olduğunu gösteriyor. Yerel model kullanılacaksa (DEC-AI-004/009) darboğaz para değil GPU zamanı olur; sabah saatlerinde görevlerin yığılması ayrıca planlanmalı.
- **Çalıştırma sayısı sınırı "unutulmuş görev" sorununa çare olabilir.** Copilot için yazılan 15 çalıştırma sınırı doğruysa etkisi şudur: görev kendiliğinden sona erer ve kullanıcı yenilemek zorunda kalır. Bizde daha yumuşak bir biçimi düşünülebilir: sonucu belli süre hiç açılmayan görev kendini duraklatır ve sahibine sorar. (Çıkarım; kaynakta böyle bir tasarım gerekçesi yok.)
- **Yetki her çalıştırmada yeniden sınanmalı.** Kaynaklar asistanın kullanıcı yetkisiyle çalıştığını söylüyor ama zamanlı görev kurulduktan sonra yetkinin değişmesi durumunu anlatmıyor. Bizde görev her çalıştırmada kullanıcının o anki yetkisi ve şirket kapsamıyla çalışmalı; yetki kalkmışsa görev durmalı ve bildirmeli. (Çıkarım.)

### Boşluklar (Gaps)

- ChatGPT Tasks ve Pulse aranamadı (bütçe). Sahibin 22 Kasım kararında örnek gösterdiği ürün bu olduğu için önemli bir boşluk. Araştırmacının eğitim verisinden hatırladığı, doğrulanmamış bilgi (**D, teyit gerekir**): ChatGPT'de zamanlı görev özelliği 2025 başında geldi ve etkin görev sayısı sınırlıydı; kesin sınır ve bugünkü durum doğrulanmalı.
- Gemini'de başarısız ya da atlanan zamanlı eylem için kullanıcıya bildirim gidip gitmediği bulunamadı.
- Çalıştırma başına maliyet, zamanlı görevlerin gerçek kullanım oranı ve kaç kullanıcının bu özelliği sürdürdüğü için hiçbir rakam bulunamadı.
- Google Workspace Studio'da (metinden akış) zamanlama ayrıntıları ve Slackbot'ta yinelenen işlerin sınırları aranamadı (bütçe).
- Copilot'taki "10 istem" ile "15 çalıştırma" bilgileri arasındaki çelişki birincil sayfadan çözülmeli.

## 4. Kullanıcıya özel kişiselleştirme ve hafıza: ne saklanıyor, kullanıcı ve yönetici neyi denetliyor?

### Özet çıkarım (Takeaway)

En açık belgelenmiş kullanıcı hafızası HubSpot Breeze Assistant'ta: saklanan şey iş verisi değil kişisel tercihler ve tekrarlayan talimatlardır; kullanıcı sohbetten "hatırla" ya da "unut" der, hafıza listesini görür, tek tek siler ve yanıt hafızaya dayandığında bunu kaynak olarak görür. Yönetici denetimleri ve saklama süreleri için kaynak zayıf; bulunan tek süre Odoo 20'deki 30 günlük konuşma saklamadır (ikincil kaynak).

### Kaynaklı bulgular (Cited Findings)

(Hepsi arama özetine dayanır.)

- **[C]** Breeze Assistant kullanıcıya ait ad, unvan, ton, yazım üslubu ve tekrarlayan talimatlar gibi bilgileri saklar; amaç zamanla daha kişisel yanıt vermektir. Kullanıcı sohbette neyi hatırlayacağını ya da unutacağını söyleyebilir (örnek: madde işaretlerini tercih ettiğini hatırlaması, samimi ton kullandığını unutması). — [HubSpot Knowledge Base: Use Breeze Assistant](https://knowledge.hubspot.com/ai/use-breeze-assistant)
- **[C]** Hafızalar sağ üstteki üç nokta menüsünden "Memories" ile görüntülenir; her hafıza yanındaki silme simgesiyle silinir. — [HubSpot Knowledge Base: Use Breeze Assistant](https://knowledge.hubspot.com/ai/use-breeze-assistant)
- **[C]** Konuşma geçmişi varsayılan olarak açıktır; asistan ilgili olduğunda önceki sohbetlere kendiliğinden başvurabilir. Yanıtlardaki atıflar bilginin nereden geldiğini gösterir (kayıtlar, bağlı uygulamalar ve kayıtlı hafızalar); kullanıcı bunları "Sources" bölümünde inceleyebilir. — [HubSpot Knowledge Base: Use Breeze Assistant](https://knowledge.hubspot.com/ai/use-breeze-assistant)
- **[C]** Slack'in 31 Mart 2026 tarihli yazısına göre Slackbot derin araştırma, yapay zekâ becerileri ve hafıza ile ekibin çalışma biçimini zamanla öğrenir ve uyum sağlar; her çalışan için kurulum ya da eğitim gerektirmeden gelir. — [Slack blog: agent orchestration](https://slack.com/blog/news/agent-orchestration); [Slack blog: Meet Slackbot](https://slack.com/blog/news/slackbot-context-aware-ai-agent-for-work)
- **[C] [İDDİA]** ClickUp, Super Agents'ın "infinite Memory" taşıdığını söylüyor; ne saklandığı ve nasıl silindiği arama sonuçlarında yok. — [ClickUp: Super Agents](https://clickup.com/solutions/ai-agent-builder); [ClickUp blog: Super Agents launch](https://clickup.com/blog/super-agents-launch/)
- **[C + B−]** Notion için bulunan kaynaklar ayrı bir kalıcı hafıza özelliği anlatmıyor; ajanlar çalışma alanındaki mevcut belge ve veritabanlarını bağlam olarak kullanıyor ve yalnız erişim verilen içeriği okuyor. — [Notion Help: Custom Agents](https://www.notion.com/help/custom-agent); [Notion: Agents](https://www.notion.com/en-gb/product/agents)
- **[B−]** Odoo 20'de ajanlarla konuşmalar son mesajdan sonra en çok 30 gün saklanıyor (tek ikincil kaynak). — [Ecosire: what's new in Odoo 20](https://ecosire.com/blog/whats-new-in-odoo-20)
- **[C]** Notion'ın istem enjeksiyonu rehberi, bağlı araçların gerçek dış yan etkiler doğurabileceğini ve bir dış uygulamayı bağlamadan önce o bağlantının ajanın davranışını etkileyebileceğinin varsayılması gerektiğini yazıyor. — [Notion Help: prompt injection risks](https://www.notion.com/help/how-notion-protects-against-prompt-injection-risks)
- **[C] (Türkiye)** Paraşüt yapay zekâ modülü için ayrı kullanım koşulları ve aydınlatma metni yayımlamış. Modül "sohbet robotu, raporlama aracı vb." diye tanımlanıyor; kişisel veri işleme kapsamında kullanıcı soruları ve yanıt içerikleri işleniyor. — [Paraşüt: yapay zekâ destekli modül kullanım koşulları](https://www.parasut.com/yapay-zeka-destekli-modul-kullanim-kosullari-yeni); [Paraşüt: yapay zekâ destekli modül aydınlatma metni](https://www.parasut.com/yapay-zeka-destekli-modul-aydinlatma-metni)

### Çıkarımlar (Inferences)

- **Kullanıcı hafızasının içeriği "tercih"tir, "iş doğrusu" değildir.** Breeze'in sakladığı örnekler (ton, biçim, unvan, tekrarlayan talimat) AI-D15'teki "kullanıcıya ait hafıza ve tercihler" tanımıyla ve önceki araştırma notundaki "ERP doğrusu hafızaya kopyalanmaz; bakiye, stok ve durum her zaman araçla canlı okunur" ilkesiyle örtüşüyor.
- **Dört denetim kopyalanmaya değer:** (1) sohbetten "hatırla / unut", (2) görülebilir hafıza listesi, (3) tek tek silme, (4) yanıtta "bu cevap şu hafızaya dayanıyor" atfı. Dördüncüsü AI-D12'deki "her bilginin kaynağı tutulur" kuralının kullanıcıya görünen yüzüdür. Bizde silme fiziksel silme değil geçersiz kılma olur (ters kayıt ilkesi).
- **"Sonsuz hafıza" bir pazarlama ifadesi;** saklama süresi, silme hakkı ve yönetici görünürlüğü tanımlanmadan hafıza açılmamalı. Bu üçü AI_TREN_PLANI_v2 §6'da zaten açık karar.
- **Türkiye'de yapay zekâ modülü ayrı hukuki metinle sunuluyor.** Paraşüt örneği, kullanıcı sorularının ve yanıtlarının kişisel veri sayıldığını ve ayrı aydınlatma gerektirdiğini gösteriyor **(M, teyit gerekir)**. Bizde kullanıcı hafızası ve sohbet kayıtları için çalışan aydınlatma metni ve saklama süresi gündeme gelir; mevcut 90 gün / 1 yıl / 2 yıl kararlarıyla (159, 157, E9) uyum ayrıca netleştirilecek.

### Boşluklar (Gaps)

- Microsoft 365 Copilot hafızasının kullanıcı ve kiracı düzeyindeki denetimleri aranamadı (bütçe). Doğrulanmamış hatıra (**D, teyit gerekir**): Copilot'ta kayıtlı hafızalar ve özel talimatlar kullanıcı ayarlarından görülüp silinebiliyordu, yöneticiler özelliği kiracı düzeyinde kapatabiliyordu.
- Slackbot hafızasının ne sakladığı, ne kadar süre sakladığı, kullanıcının ve yöneticinin neyi silebildiği aranamadı (bütçe). Gemini'nin kişisel bağlam ayarları da aranamadı.
- Hiçbir üründe yöneticinin bir kullanıcının hafızasını görüp göremediğine dair kaynak bulunamadı.
- Breeze'in hız sınırı rakamları sayfa sürümleri arasında çelişkili (bkz. bölüm 3).
- Hafıza mimarisi (çift zamanlı olgu deposu, hafıza zehirleme savunması, şirket kapsamına onaysız yazmama) için bkz. `docs/delivery/arastirma/2026-11-22_kanitlanmamis_ai_yontemleri.md` bölüm B ve (2).

## 5. Sohbetle otomasyon kurma (metinden iş akışına): güvenilirlik kanıtı, sınırlar, inceleme adımı

### Özet çıkarım (Takeaway)

Metinden otomasyon üretimi incelenen hemen her üründe var, ama hepsi "taslak üret, insan incelesin, elle etkinleştirsin" diye sunuluyor. Shopify kendi dokümanında Sidekick'in karmaşık akışlarda işe özgü mantığı atlayabileceğini yazıyor. Üretilen otomasyonların doğruluğunu ölçen bağımsız bir rakam bulunamadı.

### Kaynaklı bulgular (Cited Findings)

(Hepsi arama özetine dayanır.)

- **[C]** Shopify, Sidekick'in ürettiği akışların incelenip test edilmesini öneriyor; gerekçesi kendi cümlesiyle: "Sidekick might omit business-specific logic in complex workflows that you need to refine before using the workflow." Üretilen akış kendiliğinden etkinleşmez, "sidekick" etiketi alır ve düzenlemeler "Apply changes / Discard changes" ile bekletilir (bkz. bölüm 1). — [Shopify Help: create workflow](https://help.shopify.com/en/manual/shopify-flow/create/create-workflow)
- **[B-anekdot]** Shopify Flow uygulama yorumlarında bazı kullanıcılar Sidekick'in kurduğu akışı bulmakta zorlandığını yazıyor. — [Shopify App Store: Flow reviews](https://apps.shopify.com/flow/reviews?page=2)
- **[C]** Aynı yardım sayfasının başlığı iş akışlarının "elle ya da şablondan" oluşturulmasıdır; yani şablon kütüphanesi sohbetle üretimin yanında duran ayrı bir yoldur. — [Shopify Help: Creating workflows manually or from a template](https://help.shopify.com/en/manual/shopify-flow/create/create-workflow)
- **[C]** Zoho Flow: metinden üretilen akış önizlenir ve kullanılmadan önce değiştirilebilir. Zoho RPA'da da yapay zekâ ile akış oluşturma var. Zoho Desk'teki Zia Actions için CRM'de Generative AI'ın açık olması gerekir. — [Zoho Flow Help: text to flow](https://help.zoho.com/portal/en/kb/flow/user-guide/ai-features/articles/generating-workflows-using-zia-text-to-flow); [Zoho RPA Help: create an RPA flow using AI](https://help.zoho.com/portal/en/kb/zohorpa/user-guide/rpa-flow/create-an-rpa-flow/articles/create-an-rpa-flow-using-ai); [Zoho Desk: Zia Actions FAQ](https://help.zoho.com/portal/en/kb/desk/faqs/zia/articles/faqs-ai-powered-workflow-automation-with-zia-actions)
- **[C]** Airtable Omni karmaşık otomasyonları düz dilden kurabiliyor; yerleşik eylem yoksa betik yazıp otomasyona yerleştiriyor; veri modelini kurup veriyi içe aktarıp otomasyonları ve yapay zekâ alanlarını tek seferde ayarlayabiliyor. Field Agents hücre düzeyinde veri getiren, inceleyen ya da üreten yapay zekâ alanlarıdır; elle eklenir ya da Omni tarafından üretilir. — [Airtable: fall release](https://www.airtable.com/articles/fall-release); [Airtable Support: Using Airtable AI in fields](https://support.airtable.com/articles/8052242094-using-airtable-ai-in-fields)
- **[C]** monday sidekick basit bir istekten iş akışı, otomasyon, pano ve form oluşturuyor. — [monday: sidekick](https://monday.com/w/sidekick)
- **[C]** ClickUp'ta ajan doğal dil kurucusuyla, hazır katalogdan ya da sıfırdan oluşturulur. — [ClickUp Help: Create a Super Agent](https://help.clickup.com/hc/en-us/articles/37033397273111-Create-a-Super-Agent)
- **[B−]** Odoo 19'da "AI" türünde yeni bir sunucu eylemi olduğu, bağlama göre işlemi LLM ile yürüttüğü yazılıyor (resmî olmayan eğitim sayfası). — [Odoo eğitim sayfası (resmî değil)](https://pran-odoo.github.io/odoo-training/26-ai.html)
- **[C] [İDDİA]** Kullanım göstergesi: Shopify'a göre satıcılar 2026 ikinci çeyreğinde Sidekick'le 36.000'den fazla özel uygulama kurdu (ilk çeyrekte 12.000). Bu, sohbetle üretimin kullanıldığını gösterir; üretilenlerin doğru çalıştığını göstermez. — [Retail TouchPoints: Shopify Q2 2026](https://www.retailtouchpoints.com/news/shopify-credits-ai-for-34-revenue-growth-in-q2-2026/620805/)

### Çıkarımlar (Inferences)

- **En güçlü güvenilirlik kanıtı satıcının kendi uyarısıdır.** Shopify gibi olgun bir satıcı bile üretilen akışın işe özgü mantığı atlayabileceğini yazıyorsa, bizde tarif kütüphanesi (AI-25) "üret ve çalıştır" değil "üret, dene, etkinleştir" diye kurulmalı.
- **Kopyalanacak inceleme zinciri dört halkalı:** (1) taslak düzenleyicide açılır, (2) örnek veriyle ya da gerçek veriye dokunmadan deneme koşusu, (3) elle etkinleştirme, (4) köken etiketi. Bizde beşinci halka eklenmeli: tarif yalnız kayıtlı, şemalı araçları çağırır; yazma adımı varsa yine önizleme ve onaydan geçer (AI-D19).
- **Şablon önce, serbest metin sonra.** Shopify şablon yolunu koruyor, ClickUp ve Zoho hazır ajan kataloğu sunuyor. AI_TREN_PLANI_v2 §4'teki örnekler (sabah onay özeti, geciken tedarikçi teslimleri, haftalık marj kaybı) hazır şablon olarak gelirse hem güvenilirlik hem keşfedilebilirlik artar; "kurduğum akışı bulamıyorum" şikâyeti de önlenir.
- **Betik yazan kurucu bizde olmaz.** Airtable Omni yerleşik eylem yoksa betik yazıyor. Bizim değişmezlerimizde yapay zekâ yalnız şemalı backend araçlarını kullanır; aracı olmayan iş "eksik araç talebi"ne dönüşür (AI-D19c).

### Boşluklar (Gaps)

- Metinden iş akışı üretiminin doğruluğunu ölçen akademik ya da bağımsız benchmark aranamadı (bütçe).
- Zoho CRM, monday ve ClickUp'ta üretilen otomasyonun etkinleştirilmeden önceki inceleme adımı doğrulanamadı.
- Üretilen otomasyonların ne kadarının etkinleştirildiği, ne kadarının sonradan kapatıldığı ya da düzeltildiği için hiçbir rakam yok.
- Google Workspace Studio (metinden akış) ve Slack iş akışı kurucusu aranamadı (bütçe).

## 6. Türk ürünleri: hangi yapay zekâ özellikleri var, Türkçe kalite iddiaları, fiyat

### Özet çıkarım (Takeaway)

Arama sonuçlarına göre Türk ürünlerinde yapay zekâ üç kümede toplanıyor: belge okuma (Paraşüt'te fiş için AI OCR), doğal dille raporlama ya da "dijital danışman" (Paraşüt, Logo Cloud) ve işlem adımlarını anlatan yardım asistanı. Sohbetle ayar ya da yetki değiştirme, zamanlı görev, kişisel hafıza ya da sohbetle otomasyon kurma için hiçbir Türk üründe kaynak bulunamadı. Fiyat bilgisi ve Türkçe kalite ölçümü de bulunamadı; bu bölüm en zayıf kanıtlı bölümdür.

### Kaynaklı bulgular (Cited Findings)

(Hepsi arama özetine dayanır. Ürün başına bir, Logo için iki arama yapılabildi.)

**Logo (Tiger, j-Platform, İşbaşı)**

- **[C, basın bülteni aktarımı]** Logo 2026 sonbaharında ürün mimarisini yeniden düzenledi: Tiger, Netsis ve j-Platform ürün grupları Logo Edge çatısı altında sırasıyla T-Series, N-Series ve J-Series adlarıyla sunulacak; bulut ürünleri Logo Cloud altında toplandı. — [Capital: Logo ürün mimarisini yeniden yapılandırdı](https://www.capital.com.tr/haberler/tum-haberler/logo-yazilim-urun-mimarisini-yeniden-yapilandirdi); [Chip: Logo Cloud ile Logo Edge](https://www.chip.com.tr/haber/logo-yazilim-bulut-ve-hibrit-cozumlerini-logo-cloud-ile-logo-edge-altinda-topladi_184299.html)
- **[C, basın bülteni aktarımı]** Logo Cloud'a eklenen yapay zekâ destekli dijital danışman, kullanıcının doğal dilde soru sorarak şirket verisine ulaşmasını sağlıyor. Bir kaynağa göre danışman Logo Base üzerinden ürünlere ilişkin videolu anlatımlara ve dokümanlara da erişim sunuyor. Bir başka haber Logo Edge'in PaaS altyapısıyla entegre analitik pano, çevrim içi teklif toplama ve yapay zekâ destekli dijital asistan özelliklerini saydığını yazıyor. Asistanın hangi üründe hangi kapsamda çalıştığı kaynaklarda net değil (cümle–kaynak eşleşmesi belirsiz). — [Oksijen (6 Ekim 2026)](https://gazeteoksijen.com/ekonomi/yeni-logo-cloud-ve-logo-edge-isletmelerin-donusen-ihtiyaclarina-cevap-veriyor-291532); [Foreks (24 Eylül 2026)](https://www.foreks.com/haber/detay/6ab4f7ae24008d7d4373672e/FRKS/tr/logo-yazilim-yeni-nesil-kurumsal-uygulama-yazilimi-deneyimi-sunuyor-24-09-26/); [Teknoblog: Logo Cloud ve Edge farkları](https://www.teknoblog.com/logo-cloud-edge-yenilendi-farklar/)
- **[C]** Logo j-Platform ürün sayfası yapay zekâ özelliği anlatmıyor. — [Logo j-Platform](https://www.logo.com.tr/product/logo-j-platform)
- **[C, eski]** Logo İşbaşı için bulunan tek "akıllı" özellik sesli komutla fatura kesme; bilgi 2018 tarihli bir habere dayanıyor ve bugün geçerli olup olmadığı görülemiyor. Güncel bir yapay zekâ özellik listesi doğrulanamadı. — [Webrazzi (2018, eski)](https://webrazzi.com/2018/05/25/sesli-komut-destekli-on-muhasebe-cozumu-logo-isbasi/); [Logo İşbaşı](https://www.logo.com.tr/product/logo-isbasi)

**Mikro (Run / Jump / Fly)**

- **[C]** Arama sonuçlarında Mikro Run, Jump ve Fly için duyurulmuş bir yapay zekâ asistanı çıkmadı. Bulunan sahip odaklı ürün "Mikro Şirketim" mobil uygulaması: Run, Jump ve Fly'daki işletme verisinin özet raporlarına erişim veriyor; lisanslı ve MAG hizmeti etkin olan şirketlerin sahip ve yöneticilerine yönelik. Yapay zekâ özelliği olarak tanıtılmıyor. — [App Store: Mikro Şirketim](https://apps.apple.com/us/app/id1526677643)

**Paraşüt**

- **[C]** Paraşüt'ün yapay zekâ sayfası üç özellik sayıyor: (1) AI OCR ile fişi telefon kamerasından okutup saniyeler içinde gider olarak kaydetme, (2) gelir ve gideri birlikte değerlendirip kârlılığı hesaplayan ve müşteri bazlı soruları yanıtlayan raporlama asistanı (örnek: bu ay kaç farklı müşterinin alışveriş yaptığı), (3) işlem adımlarıyla ilgili soruları yanıtlayan yapay zekâ destekli asistan. — [Paraşüt: yapay zekâ özellikleri](https://www.parasut.com/yapay-zeka-ozellikleri); [Paraşüt kılavuzu: AI OCR fiş okutma](https://www.parasut.com/kullanim-kilavuzu/parasut-ai-ocr-fis-okutma-nasil-yapilir)
- **[C]** Yapay zekâ modülü için ayrı kullanım koşulları ve KVKK aydınlatma metni var (bkz. bölüm 4). Asistanın hangi planlarda sunulduğu ve fiyatı arama sonuçlarında yok. — [Paraşüt: yapay zekâ destekli modül kullanım koşulları](https://www.parasut.com/yapay-zeka-destekli-modul-kullanim-kosullari-yeni)

**BizimHesap, Uyumsoft, Univera, DİA, Canias, Workcube**

- **[D]** BizimHesap ve Univera için yapay zekâ özelliği anlatan hiçbir kaynak çıkmadı (birer arama). Bu, özelliğin olmadığını göstermez.
- **[C]** Uyumsoft: şirketin kurumsal hizmetler genel müdürünün köşe yazısına göre LioX ERP, üzerinde çalışılan yapay zekâ uygulamalarıyla daha akıllı hale gelecek; yani özellikler geliştirme aşamasında anlatılıyor, yayımlanmış bir asistan doğrulanamadı. — [Ekonomi Gazetesi: köşe yazısı](https://www.ekonomigazetesi.com/kose-yazisi/kuresel-erp-pazari-2028de-104-milyar-dolara-ulasacak-45854)
- **[C]** DİA: genel müdür, yapay zekâ altyapısının KOBİ'ler için erişilmez olmamasında SaaS modelinin kritik olduğunu ve her işletmenin bu altyapıyı kendi bünyesinde kuramayacağını söylüyor; somut bir yapay zekâ özelliği anlatılmıyor. Foreks DİA'nın 2026'da Mikrogrup'a katıldığını yazıyor; başka kaynaklar satın almanın Rekabet Kurumu başvurusu aşamasında ya da "beklemede" olduğunu gösteriyor (çelişki). — [Foreks (23 Eylül 2026)](https://www.foreks.com/haber/detay/6ab38cd8867705495594a5c6/FRKS/tr/dia-yazilim-kobi-ler-yapay-zeka-icin-milyon-dolarlik-yatirim-yapmak-zorunda-degil-23-09-26/); [Capital](https://www.capital.com.tr/haberler/tum-haberler/kobiler-yapay-zeka-icin-milyon-dolarlik-yatirim-yapmak-zorunda-degil); [Startup Centrum: Mikro acquires DİA](https://media.startupcentrum.com/en/mikro-yazilim-acquires-dia-yazilim/)
- **[C, basın]** Canias (IAS): WIN EURASIA 2026'da gösterilen yapay zekâ katmanı Canias IoT ve MES ürünleriyle birlikte çalışıyor; üretim darboğazlarını bulma, iş emirlerini yeniden yönlendirme, kamerayla kalite kontrol ve enerji izleme sayılıyor. Sohbet tabanlı bir ERP asistanı doğrulanamadı. — [Chip: fabrikalarda yapay zekâ](https://www.chip.com.tr/haber/fabrikalarda-yapay-zekanin-basarisi-guvenilir-veri-mimarisinden-geciyor_181361.html)
- **[C] [İDDİA]** Workcube: web sayfalarında ziyaretçi sorularını yanıtlayan "Wai" adlı yapay zekâ avatarı var; şirket yazısı WAI'nin öğrenme ve otomasyonla uygulama maliyetini ve süresini düşürdüğünü söylüyor. Ayrıca muhasebe kayıtlarını tarih aralıkları arasında karşılaştıran "Magic Auditor" anlatılıyor. Wai'nin ERP içinde işlem yapıp yapmadığına dair doküman bulunamadı. — [Workcube: Workcube and AI](https://www.workcube.com/en/workcube-and-ai-a-new-era-in-erp/); [Workcube: Financial Audit](https://www.workcube.com/en/finanz-buchhaltung-finanz-kontrolle)

**e-Fatura entegratörleri**

- **[D]** Türkiye'deki özel entegratörlerin yapay zekâ ile fatura eşleştirme ya da OCR özelliğini anlatan doğrudan bir kaynak çıkmadı (tek arama). Çıkan sonuçlar özel entegratörün ne olduğunu anlatan genel yazılardı. — [Paraşüt blog: e-fatura özel entegratörü nedir](https://www.parasut.com/blog/e-fatura-ozel-entegratoru-nedir); [BirFatura: özel entegratör firmaları](https://birfatura.com/ozel-entegrator-firmalari/)
- **[C]** Yabancı rakip Türkçe pazarlıyor: Odoo'nun Türkçe sayfaları yapay zekâ uygulamasını ve OCR ile yapay zekâyı birlikte kullanan fatura otomasyonunu tanıtıyor (taranan belge önce OCR motorundan geçiyor, sonra yapay zekâ işliyor). — [Odoo TR: yapay zekâ](https://www.odoo.com/tr_TR/app/artificial-intelligence); [Odoo TR: fatura otomasyonu](https://www.odoo.com/tr_TR/app/invoice-automation)

**Türk bankalarının işletmelere yönelik asistanları**

- **[C]** İş Bankası: işletmelere yönelik DijiKolay platformunda yapay zekâ sanal asistanı üzerinden hızlı ihtiyaç analizi ve şirket danışmanlığı sunuluyor. DijiKolay müşterilerine, pazaryeri mağazasına gelen ürün sorularını 7/24 yanıtlayan "Dot-Assist" yapay zekâ müşteri temsilcisi 3 ay ücretsiz veriliyor. Bireysel asistan Maxi bankacılık sorularını yanıtlıyor. — [İş Bankası: Cepte Danışmanlık](https://www.isbank.com.tr/is-ticari/cepte-danismanlik); [İş Bankası: YZ pazaryeri müşteri temsilcisi](https://www.isbank.com.tr/is-ticari/yz-pazaryeri-musteri-temsilcisi); [İş Bankası blog: bankacılık ve yapay zekâ](https://www.isbank.com.tr/blog/bankacilik-ve-yapay-zeka)
- **[C] [İDDİA]** Garanti BBVA: asistan Ugi ile son bir yılda 60 milyonun üzerinde müşteri etkileşimi; rakam genel müşteri tabanına ait, KOBİ'ye özel değil. — [Garanti BBVA blog](https://www.garantibbva.com.tr/blog/bankacilikta-donusum-veriden-duyguya-islemden-baglama)
- **[C]** Akbank: çalışanlar için Azure OpenAI altyapısıyla geliştirilmiş akıllı yardım asistanı; Haziran 2026'da kredili ürünlerin ödeme süreçleri için üretken yapay zekâ destekli sesli sanal asistan. — [Akbank haber](https://www.akbank.com/haberler/akbank-bankacilik-cozumlerinde-uretken-yapay-zeka-ile-fark-yaratiyor); [Ekonomim](https://www.ekonomim.com/sirketler/akbanktan-odeme-cozumlerinde-oncu-hamle-uretken-yapay-zeka-destekli-asistan-haberi-902961)
- **[C]** Halkbank kredi ve kampanya bilgilerini ChatGPT içinde sunduğunu ve 2026'da 25 yapay zekâ projesini tamamladığını duyurdu; KOBİ'ye özgü bir asistan bu sonuçlarda yok. — [CNBC-e: Halkbank](https://www.cnbce.com/yapay-zeka/halkbanktan-dijital-hamle-kredi-ve-kampanya-bilgileri-chatgptde-h39128)
- **[C]** TEB'in bir iş ortağıyla KOBİ'lere yönelik, veriyi portföy yöneticisine ve şube üzerinden müşteriye taşıyan danışmanlık odaklı bir model geliştirdiği yazılıyor; yapay zekâ asistanı olarak tanımlanmıyor. — [Ekonomi Gazetesi: TEB](https://www.ekonomigazetesi.com/sirket-haberleri/teb-ve-bankprodan-kobiler-icin-is-birligi-66734)
- **[C] [İDDİA]** Bir Türk girişiminin (KobiAI) haber başlığındaki tezi: "Yapay zeka ERP ve CRM ile konuştuğunda gerçek değer üretiyor." Yalnız başlık görüldü. — [Milliyet Uzmanpara](https://uzmanpara.milliyet.com.tr/raporlar-analizler/kobiai-kaya-yapay-zeka-erp-ve-crm-ile-konustugunda-gercek-deger-uretiyor--fnc-news/3481521/)

**Türkçe kalite**

- **[C]** Türkçe kalite için hiçbir satıcıdan ölçülmüş iddia bulunamadı. Tek ilgili ifade Intuit'ten: hesapta İngilizce dışı dillerde bilgi varsa ajan çıktılarında sınırlamalar olabilir (eşleşme belirsiz). — [QuickBooks: accounting agent for advisors](https://quickbooks.intuit.com/global/resources/accountants/quickbooks-accounting-agent-advisors/)

### Çıkarımlar (Inferences)

- **Sahibin istek listesi Türk pazarının ilerisinde.** Bulunan Türk örnekleri okuma tarafında kalıyor (belge okuma, raporlama, yardım). Konuşarak ayar, yetki taslağı, zamanlı görev ve tarif kütüphanesi için yerli bir emsal bulunamadı. Bu, riskli özellikleri "rakipte var" baskısıyla öne çekmeye gerek olmadığını; ama yerli bir ürünü örnek alarak kopyalama imkânının da olmadığını gösterir.
- **Türk pazarında kanıtı en açık üç özellik planın ilk treniyle örtüşüyor:** belgeden taslak (AI-15), doğal dille sorgu ve rapor (AI-16), sistemi anlatan asistan (AI-12). Bu, AI-D14'teki sıralamayı destekler.
- **Muhasebe programından yapay zekâ beklenmemeli.** Repo notlarına göre grup muhasebeyi Mikro'da tutuyor; Mikro Run, Jump ve Fly için duyurulmuş bir asistan bulunamadı. Fatura okuma, e-fatura eşleştirme ve banka mutabakatı yardımının ERP tarafında tasarlanması gerekecek gibi görünüyor (tek aramaya dayalı çıkarım; satıcıya sorulmalı).
- **Banka asistanları ERP'ye bağlanacak bir kanal değil.** Bulunan banka örnekleri bilgi verme, danışmanlık, ödeme hatırlatma ve pazaryeri müşteri yanıtı işleri. Hesap hareketi, mutabakat ya da ödeme onayı için bir banka yapay zekâ arayüzü bulunamadı; banka entegrasyonu ekstre ve API üzerinden planlanmalı.
- **Türkçe kalite kendi değerlendirme setimizle ölçülmeli.** Satıcılardan Türkçe ölçüm gelmiyor; AI-D11'deki "şirketin gerçek görev setiyle karşılaştırma" kararı bu yüzden zorunlu. Türkçe benchmark'lar için bkz. önceki araştırma notu, bölüm E ("Yerel / açık model ve bulut").

### Boşluklar (Gaps)

- Ürün başına yalnız bir arama yapılabildi; Logo, Mikro, Uyumsoft, Univera, DİA, Canias ve Workcube'un kendi sitelerindeki ürün sayfaları ve sürüm notları okunamadı. "Bulunamadı" sonuçları "yoktur" diye okunmamalı.
- Arama sonuçlarında "MİA Kahin ve MİA Bilge ile yapay zeka hamlesi yaptık" başlıklı bir Dünya gazetesi haberi çıktı. Hangi şirkete ait olduğu özetten anlaşılmıyor; Mikro'ya ait olduğu doğrulanmadı ve başka bir şirketin ürünü olabilir. — [Dünya (yalnız başlık görüldü)](https://www.dunya.com/sirketler/mia-kahin-ve-mia-bilge-ile-yapay-zeka-hamlesi-yaptik-haberi-793560)
- Hiçbir Türk üründe yapay zekâ özelliğinin fiyatı, hangi pakette olduğu ya da kullanım kotası bulunamadı.
- Tahminleme (satış, nakit), banka mutabakatı ve e-fatura eşleştirmede yapay zekâ kullanan yerli ürün için kaynak bulunamadı. Entegratörlerin (ve mali müşavir yazılımlarının) yapay zekâ özellikleri ayrı ve hedefli bir arama ister.
- Bankaların ticari mobil uygulamalarındaki asistanlar (işletme hesabı için sohbetle işlem) ayrıca aranmalı; bulunan sonuçların çoğu bireysel bankacılık.
- Türk KOBİ'lerinde yapay zekâ kullanım oranı için resmî istatistik aranamadı (bütçe).

## 7. Kanıt: benimseme rakamları, kullanıcı yorumları, bağımsız testler; hatalar ve şikâyetler

### Özet çıkarım (Takeaway)

Kullanım rakamı yalnız Shopify'da ayrıntılı (2026 ikinci çeyreğinde yaklaşık 34 milyon konuşma) ve hepsi satıcı beyanı; doğruluk rakamları da (Intuit %95, Xero %97) bağımsız doğrulanmamış. Bağımsız kanıt daha çok olumsuz tarafta birikiyor: halüsinasyon şikâyetleri, kapatılamayan otomasyon, Notion'da iki veri sızdırma açığı ve Copilot'ta zamanlı istem arızası. Sahibin en çok istediği özellikler (zamanlı görev, sabah özeti, konuşarak ayar) faydası en az ölçülmüş olanlar.

### Kaynaklı bulgular (Cited Findings)

(Hepsi arama özetine dayanır.)

**Kullanım ve benimseme (satıcı beyanı)**

- **[C] [İDDİA]** Shopify, 2026 ikinci çeyrek: Sidekick'te çeyrekte yaklaşık 34 milyon satıcı konuşması; aracı günlük kullanan satıcı sayısı yıllık 3,6 kat, günlük oturum 4,8 kat arttı; satıcılar 36.000'den fazla özel uygulama kurdu (ilk çeyrekte 12.000). Rakamlar ticari basın aktarımı; kazanç çağrısı dökümü bulunamadı. — [Retail TouchPoints: Shopify Q2 2026](https://www.retailtouchpoints.com/news/shopify-credits-ai-for-34-revenue-growth-in-q2-2026/620805/)
- **[C] [İDDİA]** Shopify, 2026 ilk çeyrek: Sidekick kullanan haftalık aktif mağaza sayısı yıllık yaklaşık 4 kat arttı. — [Zacks analizi (yeniden yayım)](https://www.metatrader.com/it/news/1706819-can-shopify-balance-sidekick-growth-with-higher-llm-costs)
- **[C] [İDDİA]** Shopify, 2025 üçüncü çeyrek: 750.000'den fazla satıcı aracı ilk kez kullandı; lansmandan beri toplam etkileşim 100 milyona yaklaştı (eşleşme belirsiz). — [Marketing Brew](https://www.marketingbrew.com/stories/shopify-is-pushing-forward-with-agents-and-ai); [Asianet Newsable](https://newsable.asianetnews.com/markets/shopify-president-bets-big-on-ai-for-holiday-season-sales-articleshow-hn34w5n)
- **[C] [İDDİA]** Intuit: müşterilerin %76'sı QuickBooks ve Intuit Assist ajanlarıyla ortalamada daha az elle iş yaptığını bildiriyor (şirketin yaptırdığı anket; eşleşme belirsiz). — [QuickBooks: AI agents](https://quickbooks.intuit.com/global/ai-agents/); [QuickBooks UK basın](https://quickbooks.intuit.com/uk/press/intuits-all-in-one-platform-introduces-a-virtual-team-of-ai-agents-to-help)
- **[C] [İDDİA]** Intuit'e atfedilen %95 kategorilendirme doğruluğu ve ayda 12 saat tasarruf rakamları bir üçüncü taraf incelemesinin aktardığı satıcı iddiasıdır. Kategorilendirme hata oranını ölçen bağımsız çalışma bulunamadı. — [Litmus Tools: QuickBooks (Intuit Assist) review](https://litmustools.com/review/quickbooks-intuit-assist/)
- **[C] [İDDİA]** Xero otomatik banka mutabakatı için %97 doğruluk veriyor ve kendi teknolojisinin yalnız LLM'e dayanan modellerden çok daha doğru olduğunu söylüyor; bağımsız doğrulama bulunamadı (eşleşme belirsiz). — [Xero: breakthrough tech behind JAX](https://www.xero.com/media-releases/xero-unveils-breakthrough-tech-behind-jax/); [Xero: Just Ask Xero](https://www.xero.com/my/just-ask-xero/)

**Bağımsız testler ve basın incelemeleri**

- **[B]** Avustralya muhasebe basınının Eylül 2025 tarihli incelemesi (Xero, MYOB, Intuit ve Sage asistanları): gerçeklik beklentinin gerisinde kaldı; muhasebeciler yavaşlıktan ve JAX'in "generating response" mesajında takılmasından yakındı; bir muhasebeci JAX istenen geliri çekemeyince hesap hareketi raporunu alıp Excel'de düzenlediğini anlattı. Anekdot niteliğinde ve 2025 tarihli. — [Public Accountant: Ledgerbots](https://publicaccountant.com.au/features/ai-assistants-accounting-software/)
- **[B]** Gerçek müşteri dosyalarında yapıldığı söylenen deneme: düzenli tedarikçi, tutarlı ödeme örüntüsü ve temiz banka akışı olan yalın işlemlerde doğruluk güçlü; düşük güvenli eylemler insan incelemesine işaretleniyor. Tam hata oranı verilmiyor, dağınık durumlar yalnız ima ediliyor. — [Digit Business: Xero JAX explained](https://digit.business/insights/xero/xero-jax-explained-australian-business)
- **[B]** VentureBeat'in başlığındaki tez: Xero JAX, gösterişli yapay zekâdan çok doğruluğun ve kullanıcı denetiminin önemli olduğunu gösteriyor. — [VentureBeat: Xero's JAX](https://venturebeat.com/ai/xeros-jax-shows-why-accuracy-and-user-control-matter-more-than-flashy-ai)
- **[B]** Notion'da iki ayrı açıklanmış veri sızdırma yolu (Eylül 2025 web arama aracı, Ocak 2026 onaydan önce kaydedilen düzenleme); geniş çaplı gerçek istismar raporu bulunamadı. Schneier vakayı "öldürücü üçlü" (özel veriye erişim, güvenilmeyen içerik, dışa iletişim kanalı) örneği olarak çerçeveliyor. — [Schneier on Security](https://schneier.com/blog/archives/2025/09/abusing-notions-ai-agent-for-data-theft.html); [PromptArmor](https://promptarmor.com/resources/notion-ai-unpatched-data-exfiltration)

**Kullanıcı şikâyetleri**

- **[B-anekdot]** Shopify topluluğunda "Sidekick is hallucinating quite a lot" başlığı sorunun zamanla sıklaştığını söylüyor; bir geliştirici Sidekick'in verilen SSS içeriği hakkında kendinden emin biçimde uydurduğunu yazıyor (ikinci alıntının eşleşmesi belirsiz). — [Shopify Community: Sidekick is hallucinating quite a lot](https://community.shopify.com/t/sidekick-is-hallucinating-quite-a-lot/666319); [Shopify Dev Community](https://community.shopify.dev/t/how-to-remove-sidekick-from-my-app/37975?page=2)
- **[B-anekdot]** Beş ay sonra platformdan ayrılan bir satıcı, Sidekick ve Shopify Magic'in teknik veriyi uydurduğunu ve markasının yönetimine ve SEO stratejisine zarar verdiğini yazıyor. — [Shopify Community: warning](https://community.shopify.com/t/warning-shopify-ai-sidekick-magic-hallucinates-technical-data-and-sabotages-strategic-seo/589483)
- **[B−]** Bir blog yazısına göre Sidekick satış vergisi sorusunu kendinden emin ve bazen yanlış yanıtlıyor (eşleşme belirsiz). — [Talk Shop: Sidekick limitations](https://www.letstalkshop.com/blog/shopify-sidekick-limitations-and-what-it-cannot-do); [SureBright: Shopify users and Sidekick](https://www.surebright.com/blog/why-shopify-users-are-losing-patience-with-its-ai-support-sidekick)
- **[B-anekdot]** Olumlu örnek: dar işlerde iyi çalıştığını söyleyenler var; bir satıcı hangi ürünlerin yeniden stoklanacağı sorusuna çok iyi yanıt aldığını yazıyor. — [Shopify Community: Sidekick best practices](https://community.shopify.com/t/sidekick-best-practices-sharing-examples-usecases-learnings/410541)
- **[B-anekdot]** QuickBooks: yanlış müşteriye otomatik kayıt, kendiliğinden değişen kategoriler ve yapay zekâyı kapatamama (bkz. bölüm 1 ve 2). İkincil bir kaynağa göre bir muhasebe eğitim kuruluşu ajanların "kur ve unut" olmadığını, kullanıcıların her hafta yanlış kategorilenen işlemleri düzeltmeye zaman harcadığını söylüyor (özgün rehber doğrulanamadı). — [QuickBooks Community: turn off AI agents](https://quickbooks.intuit.com/community/banking-4/how-can-i-turn-off-all-ai-agents-in-processing-transactions-in-my-banking-24243); [Carly: QuickBooks AI (okunamadı)](https://www.usecarly.com/blog/quickbooks-ai)
- **[B-anekdot]** Microsoft 365 Copilot'ta zamanlı istemlerin toplu arızası (Mayıs 2026; bkz. bölüm 3). — [Microsoft Tech Community](https://techcommunity.microsoft.com/discussions/microsoft365copilot/all-scheduled-prompts-failing-%E2%80%94-couldnt-be-completed-error-%E2%80%94-power-platform-prov/4516704)

**Sohbet arayüzlü ERP üzerine hakemli kanıt (önceki araştırma notundan aktarım; bu oturumda yeniden doğrulanmadı)**

- **[A, tek saha]** Önceki araştırma notuna göre hakemli bir çalışma (Odoo + RAG, Tayland'da KOBİ) doğal dille erişimin beş adımı ikiye indirdiğini raporluyor; yenilik etkisi ve doğruluk kaygısı da raporlanmış. Ayrı, 17 kişilik bir tezde kullanıcılar menü tabanlı botu serbest sohbete tercih etmiş (tez için kanıt düzeyi B−). — [MDPI Computers 15(7) 427](https://www.mdpi.com/2073-431X/15/7/427); [tez (core.ac.uk)](https://core.ac.uk/download/237427409.pdf); aktaran: `docs/delivery/arastirma/2026-11-22_kanitlanmamis_is_erp_fikirleri.md` (A7)

### Çıkarımlar (Inferences)

- **Kullanım kanıtlanmış, fayda kanıtlanmamış.** Shopify rakamları sohbet asistanının yoğun kullanıldığını gösteriyor; ama hepsi satıcı beyanı ve hiçbiri önerinin doğru çıktığını ya da iş sonucunu iyileştirdiğini ölçmüyor.
- **Doğruluk iddiaları dar ve temiz işlere ait.** %95 ve %97 rakamları kategorilendirme ve mutabakat gibi tekrarlı, geçmiş örüntüsü olan işlere ait; bağımsız deneme de "temiz akışta güçlü" diyor. Açık uçlu sorularda (vergi, teknik veri) şikâyet yoğun. Bizde otonomi (AI-D21) ancak dar, tekrarlı, geri alınabilir iş türlerinde ve kendi ölçümümüzle düşünülmeli.
- **Şikâyetlerin ortak kökü denetimin kullanıcıdan alınması.** Kapatılamayan ajan, sessizce değişen kategori, onaydan önce kaydedilen düzenleme: üçü de teknik hata değil tasarım tercihi. DEC-AI-001/002/003 bu üçünü baştan engelliyor.
- **Kanıtın en zayıf olduğu yer sahibin en çok istediği yer.** Zamanlı görev, sabah özeti, hafıza ve konuşarak ayar için ne benimseme rakamı ne bağımsız test bulundu. Bu özellikler küçük pilotla, kendi ölçütümüzle (açılma oranı, "faydalı" oranı, sonuçtan doğan eylem, susturma oranı) açılmalı.

### Boşluklar (Gaps)

- HubSpot, Notion, monday, ClickUp, Airtable, Slack, Microsoft ve Google için benimseme rakamı aranamadı ya da bulunamadı.
- Xero, Intuit ve Shopify için zor durumlarda yayımlanmış hata oranı yok.
- Bir blog Shopify'ın tüketici derecelendirme kuruluşunda en düşük notu taşıdığını, bir başkası QuickBooks'un yorum sitesindeki puanının çok düşük olduğunu ve Intuit'in 2026'da işten çıkarma duyurduğunu yazıyor; hiçbiri doğrulanamadı ve yapay zekâya özgü değil, bu yüzden bulgu sayılmadı.
- Türk ürünleri için kullanıcı yorumu, benimseme rakamı ya da bağımsız test bulunamadı.

## 8. Özel ERP için dersler: ne kopyalanmalı, ne uyarlanmalı, ne atlanmalı?

### Özet çıkarım (Takeaway)

İşe yarayan kalıp tek cümleyle şudur: asistan kullanıcının yetkisiyle okur ve taslak hazırlar; değişiklik bekletilir, fark olarak gösterilir ve insan uygular; üretilen her otomasyon kapalı doğar ve denenmeden açılmaz. Atlanacaklar da belli: kapatılamayan ajan, arka planda yapıp sonra bildiren asistan, onaydan önce kaydedilen düzenleme ve sohbetle uygulama üretimi.

### Kaynaklı bulgular (Cited Findings)

(Önceki bölümlerdeki bulguların kalıp olarak özeti; hepsi arama özetine dayanır.)

- **Bekletilen değişiklik ve ayrı etkinleştirme [C]:** "Apply changes / Discard changes"; üretilen akış elle "Turn on workflow" ile açılır; köken etiketi "sidekick". — [Shopify Help: create workflow](https://help.shopify.com/en/manual/shopify-flow/create/create-workflow)
- **Etkinleştirmeden önce deneme koşusu [C]:** — [Shopify blog: Flow automation updates 2025](https://www.shopify.com/blog/flow-automation-updates-2025)
- **Yetkiyi miras alan asistan [C]:** "Sidekick uses your staff permissions." — [Shopify Help: Sidekick set-up](https://help.shopify.com/en/manual/ai-powered-tools/sidekick/set-up)
- **Varsayılan asistan yalnız okur [C]:** "The standard Ask AI agent cannot make changes to the database." — [Odoo 19 docs: AI agents](https://www.odoo.com/documentation/19.0/applications/productivity/ai/agents.html)
- **Kuyruğa geri gönderen geri alma [C]:** "Undo" gideri "For review" sekmesine taşır. — [QuickBooks Help: automatically categorize expenses](https://quickbooks.intuit.com/learn-support/en-us/help-article/manage-expenses/automatically-categorize-expenses-quickbooks/L2I4JyDvA_US_en_US)
- **Güven eşiği ve istisna kuyruğu [C/B]:** yüksek güvenli eşleşme otomatik, kalan insana; düşük güvenli eylem incelemeye işaretlenir. — [Xero basın bülteni](https://www.xero.com/media-releases/new-ai-innovations-xerocon-denver/); [Digit Business](https://digit.business/insights/xero/xero-jax-explained-australian-business)
- **Görülebilir, silinebilir, atıf yapılan hafıza [C]:** — [HubSpot Knowledge Base: Use Breeze Assistant](https://knowledge.hubspot.com/ai/use-breeze-assistant)
- **Zamanlama formu ve tavan [C]:** sıklık, saat dilimi, sıradaki çalıştırma; en çok 10 etkin görev. — [Notion Help: Custom Agents](https://www.notion.com/help/custom-agent); [Google Help: Schedule actions in Gemini Apps](https://support.google.com/gemini/answer/16316416?hl=en-GB)
- **Küçük üst sınırlı, role bağlı proaktif kart [C]:** mağaza başına en çok beş öneri; sahip ya da yönetici. — [Shopify changelog](https://changelog.shopify.com/posts/proactive-business-recommendations-from-sidekick); [Shopify Help: Sidekick Pulse](https://help.shopify.com/manual/ai-powered-tools/sidekick/pulse)
- **Adım şeffaflığı ve ajan denetim kaydı [C/B−]:** ajan ne yaptığını canlı gösterir; otomasyon sohbetleri incelenir; ajan denetim kayıtları. — [Cybrosys: Odoo 20 features](https://www.cybrosys.com/odoo/odoo-20-features/); [Airtable Community: fall release](https://community.airtable.com/announcements-6/fall-release-omni-2-0-airtable-canvas-a-bigger-mcp-agent-audit-logs-and-upcoming-agent-permissions-48850)
- **Karşı örnek: onaydan önce kaydedilen düzenleme [B]:** — [PromptArmor](https://promptarmor.com/resources/notion-ai-unpatched-data-exfiltration)
- **Karşı örnek: kapatılamayan otomasyon [B-anekdot]:** — [QuickBooks Community](https://quickbooks.intuit.com/community/banking-4/how-can-i-turn-off-all-ai-agents-in-processing-transactions-in-my-banking-24243)
- **Karşı örnek: arka planda yap, sonra bildir [C]:** — [Zoho Books Help: AI features](https://www.zoho.com/us/books/help/ai-features/ai-features.html)

### Çıkarımlar (Inferences)

**Kopyalanmaya değer arayüz kalıpları (bizdeki karşılığıyla)**

1. **Fark önizlemesi ("Uygula / Vazgeç").** Yapay zekâ hiçbir kaydı değiştirmez; ilgili modülün taslak durumunda bir taslak üretir (politika taslağı, sipariş taslağı, yetki talebi taslağı). Ekran alan bazında eski → yeni farkı, yürürlük tarihini, deterministik simülasyondan gelen etki sayısını ve kural ihlallerini gösterir. "Uygula" normal komuttur: expected version, idempotency anahtarı ve önizleme hash'i taşır (DEC-AI-003). Önizlemeden sonra kayıt değişmişse önizleme bayattır ve yeniden üretilir.
2. **Önizleme yan etkisizdir.** Taslak onaydan önce kalıcı iş kaydına dönüşmez; önizleme ekranı yapay zekânın ürettiği içerikten dış görsel ya da bağlantı yüklemez (Notion dersi).
3. **Kapalı doğan otomasyon.** Yapay zekânın kurduğu tarif ve zamanlı görev pasif oluşur; ilk çalıştırma sonuç gösteren bir denemedir; etkinleştirme ayrı bir kullanıcı eylemidir.
4. **Köken etiketi.** Yapay zekânın hazırladığı her taslak ve tarif "asistanla hazırlandı" işareti ve konuşma bağlantısı taşır; listelerde süzülebilir. Shopify'daki "kurduğum akışı bulamıyorum" şikâyetini de önler.
5. **Kuyruğa geri gönderen geri alma.** Öneriye dayalı eşleştirme ve sınıflandırmada "geri al" kaydı silmez, "incelenecekler" kuyruğuna döndürür. Kesinleşmiş para ve stok etkisinde geri alma ters kayıttır (mevcut değişmez).
6. **Adım şeffaflığı.** Panel hangi araçların hangi kapsamda çağrıldığını gösterir; aynı bilgi denetim kaydına yazılır (DEC-AI-005).
7. **Hafıza listesi ve hafıza atfı.** "Hatırla / unut", görülebilir liste, tek tek geçersiz kılma, yanıtta "şu tercihe göre" atfı.
8. **Zamanlama formu.** Sıklık, saat, sıradaki çalıştırma; etkin ve duraklatılmış ayrımı; çalıştırma geçmişi; başarısızlık bildirimi; "şimdi çalıştır".
9. **Küçük tavanlı, role bağlı, susturulabilir özet.** Madde tavanı, etkiye göre sıralama, veri saati, "faydalı / faydasız", tür bazında susturma.
10. **Şablon önce.** Zamanlı görev ve tarif için hazır şablon kütüphanesi; serbest metinle kurma ikinci yol.
11. **Görünür geri bildirim.** "Faydasız" ve "sorun bildir" bir sahibi olan kuyruğa düşer; aracı olmayan istek eksik araç talebine dönüşür.
12. **Kapatma hakkı.** Kullanıcı öneri kartlarını susturabilir; asistan kapalıyken bütün ekranlar çalışır (DEC-AI-001).

**Kaçınılacak kalıplar**

- Onaydan önce kaydetme ya da önizlemede yan etki (Notion).
- Kapatılamayan ajan ve mevcut kaydı sessizce değiştiren öneri (Intuit).
- Arka planda yapıp sonra bildirme (Zoho Books).
- Özel veri, güvenilmeyen içerik ve dışa iletişim kanalının aynı ajanda birleşmesi (Notion).
- Yalnız üreten kişinin düzenleyebildiği yapay zekâ ürünü (Shopify uygulama üretimi): kişi ayrılınca sahipsiz kalır.
- Hata davranışı belgelenmemiş zamanlı görev (Copilot, Gemini).
- Satıcı doğruluk iddiasına dayanarak otonomi açmak.

**Genel değerlendirme**

- AI-D10–AI-D21 kararları piyasadaki olgun uygulamayla aynı yönde, birkaç noktada daha temkinli (yetki ataması, onay komutlarının yapay zekâya kapalı olması). Arama sonuçlarında bu temkinin gereksiz olduğunu gösteren bir kanıt bulunamadı; tersine, şikâyetler denetimin gevşediği yerlerde toplanıyor.
- İncelenen ürünlerin hepsi bulut hizmeti ve satıcının büyük modeliyle çalışıyor. Bizde yerel model varsayılan (DEC-AI-004/009) olduğundan model yeteneği daha düşük olabilir; bu, önizleme ve deterministik doğrulamayı daha da önemli kılar ve maliyet tavanını krediden GPU zamanına çevirir.
- Sahibin istek listesinin kanıtı en zayıf dört kalemi zamanlı görev, sabah özeti, kişisel hafıza ve konuşarak ayardır. Bunlar için piyasadan kopyalanacak olan özellik değil sınırdır (tavan, role kısıt, susturma, kapalı doğma).

### Boşluklar (Gaps)

- Bu bölümdeki öneriler arama özetlerine dayanan bulgulardan türetildi; hiçbir birincil sayfa okunmadı. Karar belgesine dönüşmeden önce en az şu sayfalar okunmalı: Shopify Sidekick ve Flow yardım sayfaları, HubSpot Breeze Assistant, Notion Custom Agents, Google zamanlı eylemler, Microsoft zamanlı istemler, Odoo 19 ve 20 yapay zekâ dokümanı, Paraşüt yapay zekâ sayfası ve koşulları, Logo Cloud duyuruları.
- Bunun için oturumun ağ izin listesine en az şu hostların eklenmesi gerekir: help.shopify.com, changelog.shopify.com, www.shopify.com, knowledge.hubspot.com, www.notion.com, support.google.com, support.microsoft.com, learn.microsoft.com, www.odoo.com, www.xero.com, quickbooks.intuit.com, www.zoho.com, www.parasut.com, www.logo.com.tr.
- Yapılamayan 12 arama (başlıktaki yöntem notunda sayıldı) tamamlanmadan HubSpot, Notion, monday, ClickUp, Airtable, Slack, Microsoft hafıza ve Google Workspace Studio için "onay, geri alma, yönetici denetimi" sütunları eksik kalır.
- Uyarlama tablosundaki sayısal örnekler (5 madde, 10 görev) piyasa örneğidir, öneri değildir. Tavan, kanal ve eşikler işletme politikasıdır; yapılandırılabilir bırakılmalı ve sahibi karar verene kadar `OPEN-*` adayı sayılmalıdır.

### Bizim sisteme uyarlama

| # | Sahibin isteği (karar) | KOBİ ürünlerindeki kalıp (bölüm) | Öneri | Bizde nasıl (dilim, değişmez) | Kanıt |
|---|---|---|---|---|---|
| 1 | "Her şey yapay zekâ üzerinden" | Sidekick her ekranda, yazarak ya da sesle (§1). Odoo 19 standart asistanı yalnız okur (§1). 17 kişilik bir tezde kullanıcılar menü tabanlı botu serbest sohbete tercih etmiş (§7) | **Uyarla** | Asistan her ekranda bağlama duyarlı panel olur. Okuma, arama, açıklama ve taslak sohbetten; yazma her zaman önizleme kartı ve normal komutla (DEC-AI-003). Asistan kapalıyken ekranlar eksiksiz çalışır (DEC-AI-001). AI-13, AI-16, AI-17, AI-24 | C; A (tek saha) |
| 2 | Ayarı konuşarak değiştirme (AI-D16) | Shopify Flow: düzenleme bekletilir, "Apply / Discard", ayrı etkinleştirme, deneme koşusu, köken etiketi (§1, §5) | **Kopyala** | Yapay zekâ politika taslağı sürümü üretir. Ekranda eski → yeni fark, yürürlük tarihi, etkilenecek işlem sayısı (deterministik simülasyon) ve kural ihlalleri görünür. "Uygula" yetkilinin normal yayımlama komutudur; kritik ayarda ikinci kişi onayı sürer. Taslak köken etiketi taşır. AI-21 | C |
| 3 | Yetkiyi konuşarak yönetme (AI-D16) | Emsal bulunamadı. Asistanlar kullanıcının yetkisini miras alır, aşamaz (§1) | **Uyarla** (hazır kalıp yok) | Yapay zekâ yalnız yetki talebi taslağı hazırlar: istenen yetkinin kapsamı, görevler ayrılığı çakışması, benzer kullanıcılar. Atama insan kapısıdır; yapay zekâ kendi talebini onaylayamaz (DEC-AI-002). "Bu ekranı neden göremiyorum" bir okuma aracıdır. AI-21 | D |
| 4 | Her kullanıcının kendi asistanı (AI-D15) | Slackbot ve Breeze: tek asistan, kullanıcının izinleri ve kişisel hafızası (§1, §4) | **Kopyala** | Tek sistem yapay zekâsı; araçlar kullanıcının o anki yetkisi ve şirket kapsamıyla çalışır. AI-17 | C |
| 5 | Kişisel hafıza ve tercihler (AI-D12, AI-D15) | Breeze: "hatırla / unut", görülebilir liste, tek tek silme, yanıtta hafıza atfı (§4) | **Kopyala** | Hafıza yalnız tercih ve tekrarlayan talimat tutar; iş verisi her zaman araçla canlı okunur. Silme geçersiz kılmadır. Şirket bilgisi yalnız öneri → yönetici onayı yoluyla girer (DEC-AI-006). AI-17, AI-12 | C |
| 6 | Zamanlı görev ve araştırma takibi (AI-D15) | Gemini: 10 etkin görev, sonuç bildirimde, duraklatma. Notion: sıklık, saat dilimi, sıradaki çalıştırma (§3) | **Kopyala, eksiğini kapat** | Görev yalnız okuma araçlarıyla çalışır. Etkin görev tavanı yapılandırılabilir. Piyasada eksik olanlar baştan konur: çalıştırma geçmişi, başarısızlık bildirimi, "şimdi çalıştır", yeniden deneme. Her çalıştırmada yetki yeniden sınanır. AI-18 | C; B-anekdot |
| 7 | Sabah özeti (AI-D20) | Sidekick Pulse: en çok beş öneri, ana sayfada kart, etkiye göre sıralı, yalnız yetkili role, veri eşiği (§2) | **Uyarla** | İçerik role göre; istisna öncelikli; madde tavanı yapılandırılabilir. Her kartta kaynak belge, veri saati, "faydalı / faydasız" ve "sustur". Varsayılan kanal ekran içi kart. AI-18, AI-26 | C |
| 8 | Olay güdümlü uyarı | Xero Bill Protection ve Cash Flow Actions; Zoho'da anomali kullanıcı çağırınca (§2) | **Uyarla** | Sapmayı kural ya da istatistik bulur; yapay zekâ ilgili kayıtları toplayıp açıklar. Uyarı türü kullanıcı bazında susturulabilir; zorunlu uyarı listesi ayrı karar. AI-18 | C |
| 9 | Onayları kolaylaştırma (AI-D13, AI-D20) | Intuit: emin olunanları grupla, tek tıkla kaydet. Xero: yüksek güven otomatik, istisna insana (§1). Intuit şikâyetleri (§7) | **Uyarla, dikkatle** | Benzer onaylar gruplanır ve özetlenir. Para, stok, üretim, yetki ve politika onayında toplu tek tık yoktur; her onay kendi komutudur. Kural kontrolü ve kanıt önce, öneri sonra gösterilir. AI-14, AI-26 | C; B-anekdot |
| 10 | Sohbetle otomasyon ve tarif (AI-D19) | Sidekick → Flow, Zoho Flow önizleme, Airtable Omni, monday (§5) | **Kopyala** (inceleme zinciriyle) | Tarif taslak doğar → örnek veriyle deneme → elle etkinleştirme → köken etiketi ve sürüm. Önce hazır şablonlar. Tarif yalnız kayıtlı araçları çağırır; yazma adımı önizleme ve onaydan geçer. AI-25 | C |
| 11 | Zamanlı dış e-posta ve yanıt raporu (AI-D17) | Xero JAX belge isteme: iste, hatırlat, eşleştir; her adım onaylı (§1) | **Kopyala** | E-postalar tek ekranda toplu onaylanır; şablon ve alıcı bazında otomatik gönderim sonradan, yönetici izniyle. Gelen yanıt dış içeriktir; şemalı alanlara ayrıştırılmadan araç tetikleyemez. AI-22 | B; C |
| 12 | Belgeden taslak (AI-D14) | Paraşüt AI OCR; Odoo'da OCR ve yapay zekâ ile fatura (§6) | **Kopyala** | Okuma taslak üretir, kaydı insan onaylar (DEC-AI-008). Tutar, miktar ve kimlik alanları her zaman doğrulanır (DEC-AI-007). AI-15 | C |
| 13 | Doğal dille sorgu ve rapor (AI-D14) | Logo Cloud dijital danışman, Paraşüt raporlama asistanı (§6). Zoho'da rapor kullanıcının yetkisine göre (§1) | **Kopyala** | Sorgu yetki sınırında; sayılar deterministik calculator'dan; her sonuçta kaynak. AI-16 | C |
| 14 | Kazanılan otonomi (AI-D21) | Xero: güven eşiği ve istisna kuyruğu. Odoo 19 → 20: önce okuma, sonra yazma (§1). Intuit: kapatılamayan otomasyona tepki (§7) | **Uyarla** | Yalnız dar, tekrarlı ve geri alınabilir iş türlerinde. Eşik satıcı iddiasından değil kendi ölçümümüzden gelir. Otomatik yapılan her iş günlükte görünür ve tek tıkla kuyruğa geri gönderilir; iş türü bazında kapatma düğmesi vardır. AI-27 | C; B-anekdot |
| 15 | Denetim ve ajan kimliği (DEC-AI-005) | Zoho: kaydın sahibi "Zia Agent". Airtable: ajan denetim kaydı. Odoo 20: otomasyon sohbeti incelenebilir. Xero: "her şey günlükte" (§1) | **Uyarla** | Denetim kaydında kullanıcı, "asistan aracılığıyla" işareti, araç ve sürüm, konuşma bağlantısı tutulur. İş kaydının sahibi insan kalır. Asistan ne yaptığını adım adım gösterir. AI-20, AI-29 | C |
| 16 | Maliyet denetimi (AI-D11) | monday: koltuk başına günde 5 ücretsiz mesaj. Notion, Airtable, Odoo: kredi. Gemini ve Copilot: görev tavanı. Shopify'da LLM maliyet baskısı (§3) | **Uyarla** | Kredi satışı değil bütçe: kullanıcı ve görev başına tavan, görev türüne göre model seçimi, AI Merkezi'nde kullanım görünürlüğü. Yerel modelde tavan GPU zamanıdır. AI-11, AI-20 | C; B |
| 17 | Sistem dışı sohbet ve sektör araştırması (AI-D15) | Slackbot derin araştırma (§3). Notion vakaları: özel veri, dış içerik ve dış kanal bir arada tehlikeli (§7) | **Uyarla** | Ayrı mod; şirket verisine erişen araçlar bu modda kapalı. Web araması yapan oturum şirket verisi göremez; birleştirme AI-D3/D4 kurallarıyla ve ayrı kararla. Sonuçlar kaynaklı. AI-17, AI-18 | B |
| 18 | Geri bildirim ve öğrenme döngüsü (AI-D12) | Sidekick: beğen / beğenme var ama sorun sohbetten ürün ekibine iletilemiyor (§2) | **Uyarla** | "Faydasız" ve "sorun bildir" tek tıkla AI Merkezi kuyruğuna düşer; aracı olmayan istek eksik araç talebine dönüşür. AI-19, AI-20, AI-25 | B-anekdot |
| 19 | Sohbetle uygulama ya da ekran ürettirme | Sidekick uygulama üretimi: çeyrekte 36.000'den fazla uygulama; yalnız üreten kişi düzenleyebiliyor (§1, §5) | **Atla** | Modül sınırı, şema, yetki ve denetim değişmezleriyle çelişir. Yeni ihtiyaç "eksik araç talebi" yoluyla geliştirme sürecine girer (AI-D19c) | C |

### Önerilmeyenler / fazlalık

1. **Kapatılamayan ya da varsayılan açık otomatik kayıt.** Intuit'te en çok şikâyet alan tasarım; DEC-AI-001 ile çelişir.
2. **Arka planda işlem yapıp sonra bildiren asistan** (Zoho Books'taki Ask Zia tanımı). DEC-AI-003'teki önizleme ve onay şartıyla bağdaşmaz.
3. **Onaydan önce kaydedilen taslak ve önizlemede dış içerik yükleme.** Notion'daki veri sızıntısının kök nedeni.
4. **Mevcut kaydı sessizce değiştiren öneri motoru** (QuickBooks'ta kendiliğinden değişen kategoriler). Öneri yeni bir taslaktır, eski kaydın üzerine yazılmaz.
5. **Şirket verisine erişen asistana aynı oturumda web araması ya da dış bağlantı aracı vermek.** Notion vakalarındaki üçlü birleşim.
6. **Sohbetle uygulama, ekran ya da betik üretimi** (Sidekick uygulama üretimi, Airtable'ın otomasyona betik yazması). Yapay zekâ yalnız şemalı backend araçlarını kullanır.
7. **Ajan pazaryeri ve üçüncü taraf ajan ya da MCP bağlantısı** (Zoho Agent Marketplace, Odoo 20'de dış MCP istemcileri). Tek şirketli bir sistemde kazanç yok, saldırı yüzeyi var; bkz. önceki araştırma notundaki araç zehirleme bulguları.
8. **"Sonsuz hafıza".** Saklama süresi, silme hakkı ve görünürlük tanımlanmadan hafıza açılmaz.
9. **İş kaydının sahibi olarak ajan** (Zoho'da Lead Owner = Zia Agent). Sorumluluk insanda kalmalı; ajan yalnız denetim kaydında aracı olarak görünür.
10. **Tavansız özet, e-posta ve bildirim yağmuru.** Pulse'un küçük üst sınırı ve "AI kutuları ekranı dolduruyor" şikâyeti aynı dersi veriyor.
11. **Sesle ya da saatten yazma komutu.** Sidekick'in saat sürümü yalnız soru yanıtlıyor; doğru sınır bu.
12. **Satıcıların doğruluk rakamlarına (%95, %97) dayanarak otonomi açmak.** Rakamlar bağımsız doğrulanmamış ve dar işlere ait.
13. **Zamanlı görevi canlı izleme aracı gibi kullanmak.** Gemini zamanlı yanıtı bir saat önceden hazırlıyor ve zaman açısından kritik bilgi için uygun olmadığını söylüyor; anlık durumlar için olay tetikleyici ve kural gerekir.
14. **Web sitesi avatarı ya da ERP işlevine bağlı olmayan genel sohbet botu** (Workcube'un site avatarı örneği). AI_TREN_PLANI_v2 §7'deki "sistemin gerçek işlevlerine bağlanmamış sohbet botu" uyarısıyla aynı.
15. **Banka asistanlarına entegrasyon planı.** Bulunan banka asistanları bilgi ve danışmanlık veriyor; ERP'ye bağlanacak bir işlem kanalı değiller.
16. **Yalnız sohbetten çalışan, ekransız ERP.** Tek hakemli çalışmada bile doğruluk kaygısı raporlanmış; yazma ekrandan ve onaydan geçmeli.
17. **Kredi ya da mesaj paketi satış modeli.** Şirket içi sistemde gereksiz; bütçe tavanı ve kullanım görünürlüğü yeter.
18. **Müşteri ya da tedarikçiyle onaysız doğrudan konuşan asistan.** Sidekick bunu hiç yapmıyor; bizde dış e-posta AI-D17'deki toplu onayla sınırlı.

### Sahibine açık sorular

1. **Zamanlı görev tavanı:** Bir kullanıcı aynı anda en çok kaç etkin zamanlı görev kurabilsin? (Piyasa örneği: Gemini'de ve Copilot destek sayfasında 10.) En sık çalışma aralığı ne olsun: günde bir mi, saatte bir mi?
2. **Sabah özeti:** Kaç madde olsun? (Piyasa örneği: Sidekick Pulse en çok beş öneri gösteriyor.) Saat kaçta hazır olsun? Yalnız ekranda mı görünsün, e-posta ya da telefon bildirimi de gitsin mi?
3. **Susturma hakkı:** Kullanıcı yapay zekâ öneri kartlarını ve uyarı türlerini kendi ekranında kapatabilsin mi? Kimsenin kapatamayacağı zorunlu uyarılar hangileri olsun (örnek: kredi limiti aşımı, tolerans dışı tartım farkı)?
4. **Konuşarak ayar:** Hangi ayarlar "kritik" sayılıp ikinci kişi onayı istesin? Etki özeti hangi veriye bakarak hesaplansın (açık belgeler, son 30 gün, son 90 gün)?
5. **Yetki talebi taslağı:** Kimler hazırlatabilsin? Herkes yalnız kendisi için mi, yöneticiler ekipleri için de mi?
6. **Kişisel hafıza:** Yönetici bir kullanıcının asistan hafızasını ve sohbet geçmişini görebilsin mi? Saklama süresi ve kullanıcının silme hakkı ne olsun? (AI_TREN_PLANI_v2 §6'da açık.)
7. **Tarifler ve otomasyonlar:** Kullanıcının kurduğu tarifi kim etkinleştirsin (kullanıcı kendisi için, yönetici şirket kütüphanesi için)? Tarifi kuran kişi ayrılırsa tarif kime geçsin?
8. **Gruplanmış onay:** Dış e-posta dışında hangi iş türlerinde toplu onay kabul edilebilir? Para, stok, üretim ve yetki onayları tek tek mi kalsın?
9. **Otomatik eşleştirme adayları:** İleride hangi dar işler yüksek güvenle otomatik yapılmaya aday (örnek: banka hareketi ile fatura eşleştirme, tedarikçi teyidi ile sipariş eşleştirme)? Bunlarda "kuyruğa geri gönder" yeterli bir geri alma mı?
10. **Yapay zekâ bütçesi:** Kullanıcı ve görev başına günlük ya da aylık tavan olsun mu? Tavan dolunca ne olsun (dursun, yöneticiye sorsun, ertesi güne kalsın)?
11. **Sistem dışı araştırma:** Hangi konular (çelik ve emtia fiyatı, mevzuat, kur) ve hangi kaynaklar izlensin? Bu modda şirket verisi hiç kullanılmasın mı? (AI_TREN_PLANI_v2 §6'da açık.)
12. **Muhasebe programı, entegratör ve bankalar:** Fatura okuma, e-fatura eşleştirme ve banka mutabakatı yardımı ERP'de mi yapılsın? Kullanılan muhasebe programının ve e-fatura entegratörünün kendi yapay zekâ özellikleri olup olmadığı satıcılara sorulsun mu?
13. **Araştırmanın devamı:** Bu not arama özetlerine dayanıyor. Birincil sayfaların okunabilmesi için ağ izin listesine ilgili sitelerin eklenmesi ve yapılamayan 12 aramanın tamamlanması istenir mi?
