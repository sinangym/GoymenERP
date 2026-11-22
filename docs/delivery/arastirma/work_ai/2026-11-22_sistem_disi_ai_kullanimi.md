# Sistem dışı yapay zekâ kullanımı — orta ölçekli şirkette sahip, yönetici ve personel için dış AI asistanları ve özel ERP ile güvenli köprü

**Durum tarihi:** 22 Kasım 2026. **Kapsam:** ERP dışındaki genel sohbet, zamanlı araştırma/izleme, şirket sistemlerine bağlayıcılar; bunların özel ERP (C#/.NET + React) ile güvenli köprüsü. İlgili kararlar: AI-D10–AI-D21 (`docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md`), AI-D1–AI-D6 (`SINAN_KARARLARI_2026-11-03_AI.md`), plan `docs/delivery/AI_TREN_PLANI_v2.md`, açık kararlar OPEN-014 / OPEN-020.

**Okuma anahtarı (her bulguda iki etiket vardır):**

- Kanıt düzeyi: **A** = hakemli yayın / bağımsız denetim · **B** = analist veya bağımsız kıyaslama, bağımsız basın · **C** = satıcının kendi belgesi/beyanı (**[İDDİA]**; fiyat ve özellik sayfaları için birincil kaynaktır ama bağımsız doğrulama yoktur) · **D** = spekülatif / benim önerim · **M** = mevzuat (hukuk danışmanı teyidi gerekir).
- Doğrulama: **[SAYFA]** = sayfa bu oturumda (22.11.2026) açılıp okundu · **[ÖZET]** = yalnız arama sonucu özetine dayanır, sayfa açılamadı · **[BELLEK]** = modelin eğitim bilgisinden (yaklaşık Haziran 2026'ya kadar), bu oturumda doğrulanamadı; rapora girmeden önce teyit edilmeli.

**Yöntem sınırları (rapor yazarı için önemli):**

1. Oturumun web arama kotası (bütün ajanların ortak 200 arama sınırı) araştırmanın ortasında doldu; yalnız 20 arama yapılabildi. OpenAI veri politikası, KVKK ayrıntıları, çelik fiyat kaynakları ve AI kod kalitesi çalışmaları için planlanan aramalar **yapılamadı**.
2. Oturum ağı şu alan adlarına kapalıydı: `chatgpt.com`, `help.openai.com`, `workspace.google.com`, `www.perplexity.ai`, `www.kvkk.gov.tr`, `privacy.claude.com`, `modelcontextprotocol.io`, `docs.github.com`, `learn.microsoft.com`. Açılabilenler: `claude.com`, `support.claude.com`, `platform.claude.com`, `code.claude.com`, `www.microsoft.com`, `github.com`.
3. Sonuç: Claude/Anthropic, Microsoft fiyatları, GitHub güvenlik araçları ve MCP belirtimi bulguları **[SAYFA]** düzeyindedir; ChatGPT, Gemini, Perplexity ve KVKK bulgularının çoğu **[ÖZET]**, çelik kaynakları ve bağımsız doğruluk çalışmaları **[BELLEK]** düzeyindedir. Fiyatların hepsi ABD doları liste fiyatıdır; Türkiye'deki yerel fiyat ve vergi doğrulanmadı.
4. Claude API olguları için görevde istenen `claude-api` bilgi kaynağı (Claude Code yerleşik skill'i, önbellek tarihi 2026-11-03) kullanıldı ve resmî fiyat/saklama sayfalarıyla çapraz kontrol edildi.

---

## 1. Ürün yetenekleri ve sınırları (Kasım 2026): Claude, ChatGPT, Gemini, Microsoft 365 Copilot, Perplexity

### Takeaway

Beş ürün ailesinin hepsi zamanlı görev, web araştırması, belge/tablo analizi ve e-posta taslağı sunuyor; fark, hangi ofis paketine gömülü oldukları, iş planlarındaki yönetici denetimleri ve özel bağlayıcı desteğindedir. Şirket işi için yalnız iş planları uygundur (koltuk başı liste fiyatı ayda yaklaşık 14–40 USD); hiçbir üründe "gözetimsiz, onaysız dış yazma" güvenilir biçimde desteklenmiyor — zamanlı görevler okuma-özetleme-bildirme işinde güçlüdür.

### Cited Findings

**1.1 Claude (Anthropic)**

- **Planlar ve fiyat (22.11.2026):** Free $0; Pro aylık ödemede $20, yıllıkta $17/ay ($200 peşin); Max $100/ay; Ultra $200/ay; **Team** standart koltuk yıllıkta $20, aylıkta $25; premium koltuk yıllıkta $100, aylıkta $125 ("5x more usage than standard seats"); Team "2 to 150" koltuk; **Enterprise** "US$20/seat/month, billed annually, 20 seat minimum" ve "Seat price + usage at API rates". C [İDDİA] [SAYFA] — [claude.com/pricing](https://claude.com/pricing)
- **Team planında API kredisi:** fiyat sayfasında "Up to $500 per month in pooled API credits†" ifadesi var (dipnot koşulları okunamadı); sürüm notlarında 7 Eki 2026 "Monthly API credits for Max and Team plans". C [İDDİA] [SAYFA] — [claude.com/pricing](https://claude.com/pricing), [Release notes](https://support.claude.com/en/articles/12138966-release-notes)
- **Plana göre özellik:** bağlayıcılar bütün planlarda; Research, Claude Code, Claude for Microsoft 365 ve Claude in Chrome ücretli planlarda; "Admin controls for connectors" ve SSO Team ile Enterprise'da; SCIM, rol tabanlı erişim, denetim kaydı, Compliance API ve özel saklama süresi yalnız Enterprise'da; model eğitimi tüketici planlarında "Opt-out", Team/Enterprise'da "None by default". C [İDDİA] [SAYFA] — [claude.com/pricing](https://claude.com/pricing)
- **Zamanlı görevler:** Cowork içinde bütün ücretli planlarda; "Scheduled tasks run remotely, so they run on their cadence even when your computer is asleep"; görev bir klasöre bağlanırsa yalnız bilgisayar açık ve Claude Desktop çalışırken koşar; sıklık saatlik / günlük / haftalık / hafta içi / elle; bağlı araçları, skill'leri ve eklentileri kullanır; bir bağlayıcı aracı "Always ask" ise görev "may wait for your answer or finish without the tool"; görev sayısı üst sınırı sayfada yazmıyor. C [İDDİA] [SAYFA] — [Schedule recurring tasks in Claude Cowork](https://support.claude.com/en/articles/13854387-schedule-recurring-tasks-in-claude-cowork). Not: fiyat sayfasındaki "Hand off and schedule tasks" satırı yalnız Pro/Max/Ultra sütunlarında görüldü (sayfa kırpılmıştı); Team/Enterprise için yardım makalesi esas alındı.
- **Tarihli özellik geçmişi (sürüm notları):** 18 Ara 2025 Claude in Chrome bütün ücretli planlarda beta + kurum çapında skill yönetimi; 12 Şub 2026 self-serve Enterprise; 25 Şub 2026 Cowork'te zamanlı ve isteğe bağlı görevler; 9 Nis 2026 Cowork genel kullanımda (macOS/Windows) + Enterprise RBAC/SCIM/özel roller; 28 May 2026 özel rollerde araç bazında bağlayıcı izni; 23 Haz 2026 Claude Tag (Slack'te görev devri, Team/Enterprise); 7 Tem 2026 Cowork web ve mobilde; aynı gün Microsoft 365 bağlayıcısına yazma araçları ("admin and Entra consent required"); 25 Ağu 2026 bellek sohbet ve Cowork'te ortak; 16 Eyl 2026 Cowork özellikleri her sohbete taşındı; 12 Kas 2026 **Claude Meetings** beta (Team/Enterprise; Google Meet görüşmesine katılımcı olarak girer); 20 Kas 2026 computer use beta (Team/Enterprise). C [İDDİA] [SAYFA] — [Release notes](https://support.claude.com/en/articles/12138966-release-notes)
- **Office / tablo:** Claude for Excel beta 24 Kas 2025, Claude for PowerPoint 5 Şub 2026, 11 Mar 2026'da Excel ve PowerPoint eklentileri bağlam paylaşır hâle geldi. C [İDDİA] [SAYFA] — [Release notes](https://support.claude.com/en/articles/12138966-release-notes). Excel, PowerPoint ve Word eklentilerinin ücretli planlarda genel kullanımda olduğu; Outlook için bir sayfanın "beta", daha yeni bir sayfanın "generally available" dediği (çelişki) C [İDDİA] [ÖZET] — [Claude for Microsoft 365](https://claude.com/claude-for-microsoft-365), [Use Claude for Excel](https://support.claude.com/en/articles/12650343-use-claude-for-excel)
- **Tarayıcı kullanımı (Claude in Chrome):** Anthropic'in kendi güvenlik yazısı: "The biggest risk facing browser-using AI tools is prompt injection attacks"; "Our current configuration reduces attack success rates to less than 0.08% against our internal testing"; "The risk is not zero"; finans hesapları, hukuki belge/sözleşme, sağlık verisi, "work accounts with sensitive company data" ve başkalarının kişisel verisini içeren siteler için kullanılmaması önerilir; Team/Enterprise yöneticisi izin/engel listesi ve kurum çapı anahtar koyabilir. C [İDDİA] [SAYFA] — [Use Claude in Chrome safely](https://support.claude.com/en/articles/12902428-use-claude-in-chrome-safely)
- **Enterprise yönetim denetimleri:** SSO/SAML, domain capture, SCIM, rol tabanlı erişim, denetim kaydı ve OpenTelemetry, Compliance API, "custom data retention, customer-managed encryption keys, and US-only inference", IP izin listesi ve ağ düzeyinde kiracı kısıtı (kurum ağından kişisel hesapların engellenmesi); "admins approve which connectors are available org-wide and set per-tool permissions". C [İDDİA] [SAYFA] — [Claude Enterprise plan](https://claude.com/solutions/enterprise)
- **API tarafı (ERP içinde kullanılacaksa):** Claude Sonnet 5.5 $2 / $10, Claude Opus 5.5 $4 / $20, Claude Haiku 5.5 $0.10 / $0.50 (100.000 token'a kadar istemlerde), Claude Fable 5.2 $8 / $40 (milyon giriş / çıkış token'ı başına); web araması "$10 USD per 1,000 searches" artı arama sonuçlarının token maliyeti; web fetch ek ücretsiz; Batch %50 indirim; Managed Agents oturum çalışma süresi $0.08/saat. C [İDDİA] [SAYFA] — [Pricing (platform.claude.com)](https://platform.claude.com/docs/en/about-claude/pricing)
- **API'de zamanlama:** Managed Agents "scheduled deployments" (beta) bir ajanı cron ifadesi ve IANA saat dilimiyle tetikler; her tetikleme bir oturum açar; yük dağıtımı için tetikleme en çok 9 dakika gecikebilir; kurum başına en çok 1000 zamanlı dağıtım. C [İDDİA] [SAYFA: skill içeriği bu oturumda okundu; ürünün alt sayfası ayrıca açılmadı] — kaynak: `claude-api` bilgi kaynağı (önbellek 2026-11-03), ürün sayfası [Managed Agents overview](https://platform.claude.com/docs/en/managed-agents/overview)

**1.2 ChatGPT (OpenAI)** — resmî sayfalar açılamadı; hepsi [ÖZET]

- **Fiyat:** Business standart koltuk aylık ödemede $25, yıllıkta $20; premium koltuk $125 / $100 (yardım merkezi). Fiyat sayfası özeti ise Business için "$25 / user / month billed annually" gösteriyor — **çelişki**. Go $8 (ABD), Plus $20; Pro için ikincil kaynaklar "$100'den başlar" ve $100/$200/$500 kademeleri diyor (çelişkili). Enterprise satış ekibiyle. C [İDDİA] [ÖZET] — [Managing billing and seats in ChatGPT Business](https://help.openai.com/en/articles/8792536-managing-billing-and-seats-in-chatgpt-business), [ChatGPT Pricing](https://openai.com/chatgpt/pricing/); ikincil: [digitalapplied.com, 7 Ağu 2026](https://www.digitalapplied.com/blog/chatgpt-plans-compared-august-2026-guide)
- **Business koltuğunun içeriği:** "ChatGPT, ChatGPT Work, and Codex, as well as features such as GPTs, Projects, Apps, Company Knowledge, ChatGPT Agent, Deep Research". En az koltuk sayısında makaleler çelişiyor (2 koltuk / 1 koltuk). C [İDDİA] [ÖZET] — [ChatGPT Business overview](https://help.openai.com/en/articles/8792828-chatgpt-business-overview)
- **Zamanlı görevler (Tasks):** Free dâhil bütün planlarda; aktif görev sınırı Free ve Go 3, Plus 5, Business ve Edu 10, Pro ve Enterprise 15; "Tasks cannot run more than once per hour"; olay tetikli görevler (Gmail, Slack, GitHub etkinliğine tepki) "Work" ve uygun bir ücretli plan ister. C [İDDİA] [ÖZET] — [Scheduled tasks in ChatGPT](https://help.openai.com/en/articles/10291617-scheduled-tasks-in-chatgpt)
- **Özel MCP / developer mode:** bkz. Bölüm 2.

**1.3 Gemini / Google Workspace** — resmî sayfalar açılamadı; hepsi [ÖZET]

- **Fiyat ve paket:** Business Standard yıllık planda kullanıcı başına $14, esnek aylık planda $16.80 (Google destek sayfasındaki tabloda kademe adı açık yazmıyor; eşleştirme ikincil kaynağa dayanır). Gemini özellikleri Ocak 2025'ten beri Business Standard ve üstü planlara dâhil (ek paket olarak satılmıyor); o tarihte yıllık fiyat $12'den $14'e çıktı (2025 kaynağı — eski). C/B [ÖZET] — [Google Workspace yönetici yardımı](https://support.google.com/a/answer/15400543), [9to5google, 15 Oca 2025](https://9to5google.com/2025/01/15/google-workspace-gemini-price/)
- **Dâhil olanlar:** Gmail/Docs/Sheets yan paneli, Meet'te otomatik not, NotebookLM (genişletilmiş erişim). B [ÖZET] — [eesel.ai, 2026](https://www.eesel.ai/blog/gemini-workspace-pricing)
- **Zamanlanmış eylemler:** aynı anda en çok 10 aktif zamanlanmış eylem; Workspace iş/eğitim planlarında kullanılabilirlik sürüme ve yönetici ayarına bağlı; özellik Haziran 2025'te yayıldı. C [İDDİA] [ÖZET] — [Schedule actions in Gemini Apps](https://support.google.com/gemini/answer/16316416), [9to5google, 27 Haz 2025](https://9to5google.com/2025/06/27/gemini-scheduled-actions-rollout/)

**1.4 Microsoft 365 Copilot**

- **KOBİ fiyatı (22.11.2026):** Microsoft 365 Copilot Business eklentisi yıllık ödemede kullanıcı başına $18 ("Originally starting from $21.00 now starting from $18.00"; indirim 1 Tem–31 Ara 2026, yalnız ilk yıl, yıllık taahhütle), aylık ödemede $25.20; paketler: Business Standard + Copilot $23.50 (yıllık) / $28.20 (aylık), Business Premium + Copilot $32.00 / $38.40. Mevcut bir Microsoft 365 aboneliği gerekir. C [İDDİA] [SAYFA] — [Microsoft 365 Copilot pricing](https://www.microsoft.com/en-us/microsoft-365-copilot/pricing). Not: arama özetine göre Microsoft'un 1 Temmuz 2026 fiyat duyurusu paketleri farklı veriyor (ör. Business Standard + Copilot Business $33,50 → $35); resmî fiyat sayfasındaki rakamlar promosyonlu olabilir — sipariş öncesi teyit edilmeli. B [ÖZET] — [Microsoft Tech Community](https://techcommunity.microsoft.com/t5/microsoft-365-copilot-blog/act-now-lock-in-current-pricing-on-microsoft-365-copilot/ba-p/4502628), [partner.microsoft.com](https://partner.microsoft.com/en-US/blog/article/partner-led-smb-m365-copilot)
- **İçerik:** Researcher, Analyst ve Facilitator ajanları; Teams, Outlook, Word, Excel, PowerPoint, OneNote'ta Copilot; Agent Builder ve Copilot Studio ile iç ajan oluşturma; "Copilot Cowork, Copilot Autopilot, and Code" kullanım bazlı ayrıca faturalanır. C [İDDİA] [SAYFA] — [Microsoft 365 Copilot pricing](https://www.microsoft.com/en-us/microsoft-365-copilot/pricing)
- **Copilot Business:** 2 Aralık 2025'te genel kullanıma açıldı, "USD21 per user per month". C [İDDİA] [SAYFA] — [Microsoft 365 blog, 2 Ara 2025](https://www.microsoft.com/en-us/microsoft-365/blog/2025/12/02/microsoft-365-copilot-business-the-future-of-work-for-small-businesses/). 300 kullanıcıya kadar; kurumsal Microsoft 365 Copilot $30/kullanıcı/ay (yıllık). B [ÖZET] — [Computerworld](https://www.computerworld.com/article/4093224/), [alphabold.com](https://www.alphabold.com/guide-to-microsoft-copilot-pricing-licensing/)
- **Zamanlı istemler:** yol haritası kalemi RM531759 ("Scheduled prompts for Agents") için 2 Haz 2026 sürümünde Microsoft'un özelliği sürdürmediği yazıyor; ayrı bir kalem (MC1302906) Agent Builder'da saatlik/günlük/haftalık zamanlı istemleri, yalnız ajanı oluşturan kullanıcı için tanımlıyor (takvimi 31 Tem 2026'da güncellendi). C/B [ÖZET] — [RM531759](https://mc.merill.net/message/RM531759), [MC1302906](https://mc.merill.net/message/MC1302906)
- **Kota:** Researcher + Analyst için kullanıcı başına ayda 25 sorgu (2025 tarihli kaynaklar; 2026 için doğrulanamadı). B [ÖZET] — [Microsoft Tech Community](https://techcommunity.microsoft.com/discussions/microsoft365copilot/researcher-and-analyst-usage-limits/4420959)

**1.5 Perplexity** — resmî sayfalar açılamadı; hepsi [ÖZET]

- **Fiyat:** Enterprise Pro koltuk başına ayda $40 veya yılda $400; Enterprise Max ayda $325 veya yılda $3.250; yıllık indirimde kaynaklar çelişiyor (bazıları $34/ay diyor). B [ÖZET] — [Perplexity Enterprise pricing](https://enterprise-prod.perplexity.ai/enterprise/pricing), [Axios, 23 Nis 2024 (eski)](https://axios.com/2024/04/23/perplexity-ai-enterprise-search-answer-engine), ikincil: [perplexityaimagazine.com](https://perplexityaimagazine.com/perplexity-hub/perplexity-enterprise-pricing-pro-max/)
- **Zamanlı görevler (Tasks):** Pro ve Max'te en çok 10 görev; Enterprise Pro'da 30 aktif zamanlı görev (+25 fiyat alarmı, 5 araştırma, 5 dosya/uygulama oluşturma görevi); Enterprise Max'te 40. C [İDDİA] [ÖZET] — [Perplexity Tasks (yardım merkezi)](https://intercom.help/perplexity-ai/en/articles/11521526-perplexity-tasks)

**1.6 Fiyat özeti (ABD doları liste fiyatı, kullanıcı/ay; 22.11.2026)**

| Ürün / plan | Yıllık ödeme | Aylık ödeme | Doğrulama |
|---|---|---|---|
| Claude Team standart | 20 | 25 | [SAYFA] |
| Claude Team premium | 100 | 125 | [SAYFA] |
| Claude Enterprise | 20 + API fiyatından kullanım (en az 20 koltuk) | — | [SAYFA] |
| ChatGPT Business standart | 20 | 25 | [ÖZET], fiyat sayfasıyla çelişki var |
| ChatGPT Business premium | 100 | 125 | [ÖZET] |
| Google Workspace Business Standard (Gemini dâhil) | 14 | 16,80 | [ÖZET] |
| Microsoft 365 Copilot Business (M365 üstüne ek) | 18 promosyon / 21 liste | 25,20 | [SAYFA] |
| Microsoft 365 Copilot (kurumsal) | 30 | — | [ÖZET] |
| Perplexity Enterprise Pro | ≈33 (400/yıl) | 40 | [ÖZET] |

**1.7 Zamanlı görev sınırları**

| Ürün | Sınır | Not | Doğrulama |
|---|---|---|---|
| Claude (Cowork) | Sayı sınırı belirtilmemiş; en sık saatlik | Bulutta koşar; "Always ask" aracı görevi bekletir | [SAYFA] |
| ChatGPT Tasks | 3 / 5 / 10 / 15 aktif görev (plana göre); saatte birden sık değil | Olay tetikli görev "Work" ister | [ÖZET] |
| Gemini | 10 aktif zamanlanmış eylem | Workspace'te yönetici ayarına bağlı | [ÖZET] |
| Perplexity Tasks | 10 (Pro/Max), 30 (Ent. Pro), 40 (Ent. Max) | Ayrı fiyat alarmı kotası | [ÖZET] |
| M365 Copilot | Agent Builder zamanlı istem; yalnız oluşturan kullanıcı | Genel "scheduled prompts for agents" iptal | [ÖZET] |

### Inferences

- **Seçimi ofis paketi belirler (D):** şirket Microsoft 365 kullanıyorsa Outlook/Excel/Teams içi iş için Copilot Business ya da Claude'un Office eklentileri; Google Workspace kullanıyorsa Business Standard'daki Gemini zaten ödenmiş olur. Genel sohbet + araştırma + zamanlı görev için tek bir "ana asistan" (Claude Team ya da ChatGPT Business) seçmek veri dağınıklığını ve eğitim yükünü azaltır.
- **"ChatGPT'deki gibi sürekli zamanlı görev" isteği (AI-D15) bugün dört üründe hazır;** ancak hepsi okuma-özetleme-bildirme içindir. Claude'da onay isteyen araç gözetimsiz görevi durdurur; bu, AI-D15'in "zamanlı görev yazma yapmaz" sınırıyla aynı yöndedir.
- **Toplantı notu** için üç yol var: Claude Meetings (beta, şimdilik Google Meet), Meet'te Gemini notları, Teams'te Copilot — hangisinin kullanılacağı toplantı platformuna bağlı.
- **Bütçe (varsayım, ölçüm değil):** 30 kullanıcılık bir pilot, koltuk başı $20 ile ayda $600 liste fiyatı eder; kullanıcı sayısı bilinmiyor (bkz. açık sorular).

### Gaps

- **ChatGPT Pulse, agent mode kotaları, deep research kotaları, record mode (toplantı notu):** bu oturumda doğrulanamadı. [BELLEK]: Pulse Eylül 2025'te Pro kullanıcılara mobilde önizleme olarak, agent mode Temmuz 2025'te duyurulmuştu; Kasım 2026'daki plan kapsamı bilinmiyor.
- **Gemini Deep Research ve NotebookLM sınırları, Workspace yönetici denetimleri:** doğrulanamadı.
- **Perplexity Spaces sınırları, Comet tarayıcısı ve kurumsal yönetim denetimleri:** doğrulanamadı.
- **Microsoft 365 Copilot'ta zamanlı istemlerin güncel durumu ve Researcher/Analyst 2026 kotası:** yalnız ikincil özet var.
- **Türkiye'de yerel fiyat, KDV ve faturalama:** hiçbir ürün için doğrulanmadı.
- **ChatGPT fiyat çelişkileri** (Business yıllık $20 mı $25 mi; Pro kademeleri) resmî sayfadan teyit edilmeli.

---

## 2. Özel bağlayıcılar: iç sistemi remote MCP sunucusuyla dış asistanlara açmak

### Takeaway

Özel remote MCP bağlayıcısı bugün Claude'da (bütün planlar; kurum yönetimi Team/Enterprise'da) ve ChatGPT'de (Business/Enterprise/Edu, beta) desteklenir; ikisi de sunucunun genel internetten erişilebilir olmasını ve OAuth'u bekler. Asıl sınır teknik değil güvenliktir: özel veri + güvenilmeyen içerik + dışa çıkış yolu bir araya gelince ("lethal trifecta") istem enjeksiyonuyla veri sızar ve kesin bir önleme yöntemi yoktur; bu yüzden bağlayıcı salt-okunur, dar kapsamlı, kullanıcı kimliğiyle yetkili ve denetim kayıtlı olmalıdır.

### Cited Findings

**2.1 Claude: hangi plan, kim ekler, kimlik nasıl taşınır**

- Özel remote MCP bağlayıcıları Free, Pro, Max, Ultra, Team ve Enterprise'da; "Free users are limited to one custom connector"; claude.ai, Claude Desktop, Cowork ve mobil uygulamalarda çalışır (bağlantı Anthropic bulutundan kurulur). C [İDDİA] [SAYFA] — [Get started with custom connectors using remote MCP](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp)
- Team'de yalnız Owner/Primary Owner, Enterprise'da ayrıca "Manage access to Libraries" izinli özel rol kuruma bağlayıcı ekler; eklendikten sonra "users individually connect to and enable that connector" — yani erişim kullanıcının kendi kimliğiyle sınırlanır. C [İDDİA] [SAYFA] — [aynı makale](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp)
- OAuth seçenekleri: "Use Claude's published identity (Recommended)", otomatik kayıt (dynamic client registration) veya kendi OAuth istemciniz (Client ID/Secret; geri dönüş adresi `https://claude.ai/api/mcp/auth_callback`). Sabit kimlik bilgisi (API anahtarı/başlık) de verilebilir; o durumda "everyone who uses the connector reaches the service with that same credential". C [İDDİA] [SAYFA] — [aynı makale](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp)
- Ağ şartı: "your MCP server must be reachable over the public internet from Anthropic's IP ranges"; VPN/özel ağ arkasındaki sunucu, Anthropic IP aralıkları güvenlik duvarında izinli değilse bağlanmaz; yalnız HTTPS 443 ("Claude can't connect to a URL that specifies another port"). C [İDDİA] [SAYFA] — [aynı makale](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp)
- Güvenlik uyarıları: özel bağlayıcılar Anthropic tarafından doğrulanmaz; "Malicious MCP servers may include hidden instructions that try to make Claude perform unintended actions"; "Allow always" yalnız gözetimsiz çalışması güvenilir araçlar için; Research modu bağlayıcı araçlarını ek onay olmadan çağırabilir, bu yüzden "Disable any tools that can take write actions in external applications". C [İDDİA] [SAYFA] — [aynı makale](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp)

**2.2 Claude: yönetici izin listesi ve yazma onayı**

- Bir bağlayıcıyı üyeler kullanmadan önce Owner'ın kurum için etkinleştirmesi gerekir; "Enabling a connector makes it available to your team, but it doesn't automatically grant anyone access" (herkes ayrıca kendi oturumunu açar). Team'de üyeler varsayılan olarak bağlayıcı talep edebilir; Enterprise'da bu, Owner açana kadar kapalıdır. C [İDDİA] [SAYFA] — [Use connectors to extend Claude's capabilities](https://support.claude.com/en/articles/11176164-use-connectors-to-extend-claude-s-capabilities)
- Owner her aracı veya izin kategorisini **Always allow / Needs approval / Blocked** olarak ayarlar; bu kurum çapında geçerlidir ve kullanıcı aşamaz; "Restricting actions in Claude never grants more access than the source system permits—it only narrows it." Enterprise'da özel rol bunu daha da daraltabilir, katı olan uygulanır. C [İDDİA] [SAYFA] — [aynı makale](https://support.claude.com/en/articles/11176164-use-connectors-to-extend-claude-s-capabilities)
- Anthropic'in dizin inceleme ölçütleri iyi bir tasarım kılavuzudur: okuma ve yazma araçları ayrı olmalı, tek bir "her şeyi yapan" `api_request` aracı reddedilir; her araçta `title` ve `readOnlyHint: true` ya da `destructiveHint: true` bulunmalı — "Read-only tools can run without per-call confirmation, and destructive tools always prompt"; araç açıklamalarında gizli/yönlendirici talimat (istem enjeksiyonu kalıbı) yasak; "Don't collect conversation data beyond what the tool needs"; para aktaran bağlayıcılar kabul edilmez. C [İDDİA] [SAYFA] — [Connector pre-submission checklist](https://claude.com/docs/connectors/building/review-criteria)
- Anthropic bağlayıcıları dizine almadan önce ölçütlere göre inceler "but does not security-audit or manage any MCP server". C [İDDİA] [SAYFA] — [Claude Code security](https://code.claude.com/docs/en/security)

**2.3 Özel ağdaki sunucu için tünel: bugün yalnız API tarafında**

- MCP tunnels (araştırma önizlemesi) özel ağdaki MCP sunucusuna yalnız dışa doğru bağlantıyla (cloudflared + Anthropic vekili) erişim sağlar; ancak "MCP tunnels created through the Console are not available as connectors in claude.ai" — yalnız Messages API ve Managed Agents'tan kullanılır; taşıma sağlayıcısı (Cloudflare) alt işleyicidir ve bağlantı üst verisini görür; "provided "as-is" without any uptime, support, or continuity commitment"; ZDR'ye uygun değil. C [İDDİA] [SAYFA] — [MCP tunnels](https://platform.claude.com/docs/en/agents-and-tools/mcp-tunnels/overview)
- API'deki MCP connector (beta): "only tool calls are currently supported"; "The server must be publicly exposed through HTTP (supports both Streamable HTTP and SSE transports)"; OAuth erişim belirtecini API kullanıcısı kendisi alıp `authorization_token` olarak geçer; araç bazında izin/engel listesi vardır — "Denylisting write or destructive tools is recommended when building read-only assistants, or when you want a human confirmation step before state changes"; `mcp-client-2026-09-15` ile sunucunun araç listesi sabitlenebilir (pin); ZDR'ye uygun değil. C [İDDİA] [SAYFA] — [MCP connector](https://platform.claude.com/docs/en/agents-and-tools/mcp-connector)

**2.4 ChatGPT** — [ÖZET]

- "Apps, full MCP support, and developer mode are available for ChatGPT Business and Enterprise/Edu customers on ChatGPT web" (yazma/değiştirme eylemleri dâhil, beta); geliştirici belgeleri uygunluğu Pro, Plus, Business, Enterprise ve Education olarak listeliyor (çelişki). Business'ta yalnız yönetici/sahip developer mode'u açıp uygulama yayımlar; Enterprise/Edu'da RBAC ile belirli üyelere verilebilir. C [İDDİA] [ÖZET] — [Developer mode and MCP apps in ChatGPT](https://help.openai.com/en/articles/12584461-developer-mode-and-mcp-apps-in-chatgpt), [ChatGPT Developer mode](https://developers.openai.com/api/docs/guides/developer-mode.md)
- Yazma eylemlerinde ChatGPT "may ask for confirmation depending on app permissions, the action's context, and the action's potential impact"; `readOnlyHint` taşımayan araçlar yazma eylemi sayılır; onayı hatırlatma seçeneği yalnız güvenilen uygulamalar için önerilir. C [İDDİA] [ÖZET] — [ChatGPT Developer mode](https://developers.openai.com/api/docs/guides/developer-mode.md)
- Bağımsız basın özelliği "powerful but dangerous" diye niteledi. B [ÖZET] — [VentureBeat](https://venturebeat.com/dev/openai-adds-powerful-but-dangerous-support-for-mcp-in-chatgpt-dev-mode)

**2.5 MCP belirtimi (sürüm 2026-07-28) — sunucu yazarının uyması gerekenler**

- Yetkilendirme: istemciler "MUST include the `resource` parameter in authorization and token requests" (RFC 8707); sunucu belirtecin kendisi için verildiğini doğrulamalı, hedef kitle (audience) içinde kendisi yoksa reddetmeli; "Servers MUST NOT pass through the token they received from the client to upstream APIs"; PKCE zorunlu (S256); bütün yetkilendirme uçları HTTPS; yetkilendirme sunucusu kısa ömürlü belirteç vermeli (SHOULD) ve açık (public) istemcilerde yenileme belirtecini her kullanımda değiştirmeli (rotate, MUST). B (açık standart metni) [SAYFA] — [MCP authorization security considerations, 2026-07-28](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/docs/specification/2026-07-28/basic/authorization/security-considerations.mdx)
- Araçlar: "there SHOULD always be a human in the loop with the ability to deny tool invocations"; "clients MUST consider tool annotations to be untrusted unless they come from trusted servers"; sunucu girdileri doğrulamalı, erişim denetimi uygulamalı, çağrıları sınırlamalı (rate limit) ve çıktıları temizlemeli (MUST); istemci hassas işlemde onay istemeli, "Show tool inputs to the user before calling the server, to avoid malicious or accidental data exfiltration" ve araç kullanımını denetim için kaydetmeli (SHOULD). B [SAYFA] — [MCP tools, 2026-07-28](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/docs/specification/2026-07-28/server/tools.mdx)
- Vekil (proxy) MCP sunucularında "confused deputy" riski ve belirteç aktarma (token passthrough) açıkça yasak anti-kalıp olarak tanımlanır. B [ÖZET] — [MCP security best practices](https://modelcontextprotocol.io/specification/latest/basic/security_best_practices), açıklama: [Auth0](https://auth0.com/blog/mcp-specs-update-all-about-auth/)
- .NET için resmî C# SDK var ("Maintained in collaboration with Microsoft"); paketler `ModelContextProtocol` ve HTTP sunucuları için `ModelContextProtocol.AspNetCore`. C [İDDİA] [SAYFA] — [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)

**2.6 Bilinen riskler ve belgelenmiş olaylar**

- **Lethal trifecta** (Simon Willison, 16 Haz 2025): özel veriye erişim + güvenilmeyen içeriğe maruz kalma + dışarıyla iletişim yolu bir arada ise saldırgan ajanı veri çalmaya yönlendirebilir; güvenilir tek korunma üçünü aynı kurulumda bulundurmamaktır; MCP farklı kaynaklardan araçları karıştırmayı teşvik ettiği için riski kolaylaştırır. Örnek: GitHub MCP istismarı — herkese açık bir issue'daki gizli talimat ajana özel depo içeriğini okutup açık bir PR'a yazdırdı. B [ÖZET] — [simonwillison.net](https://simonwillison.net/2025/Jun/16/the-lethal-trifecta/), [UpGuard](https://www.upguard.com/blog/ai-github-agents-issue-leaked-private-repos)
- **Bağlantı/görsel yoluyla sızdırma:** Markdown görsel bağlantıları çalınan veriyi sorgu dizgisinde dışarı taşıyabilir; satıcılar çoğunlukla çıkış yolunu kapatarak yanıt verdi. B [ÖZET] — [simonwillison.net, 9 Ağu 2025](https://simonwillison.net/2025/Aug/9/bay-area-ai/)
- **EchoLeak (CVE-2025-32711, CVSS 9,3):** Microsoft 365 Copilot'ta tek bir hazırlanmış e-postayla, kullanıcı tıklaması olmadan veri sızdırmaya izin veren istem enjeksiyonu açığı; Aim Labs Ocak 2025'te bildirdi, Microsoft Mayıs 2025'te sunucu tarafında kapattı, Haziran 2025'te açıklandı; sahada istismar bildirilmedi. A/B [ÖZET] — [arXiv 2509.10540](https://arxiv.org/html/2509.10540v1), [The Hacker News](https://thehackernews.com/2025/06/zero-click-ai-vulnerability-exposes.html)
- **Araç zehirleme / "rug pull" / gölgeleme:** saldırgan talimatı araç açıklamasına gizler; sunucu, onaydan sonra araç tanımını değiştirebilir; kötü niyetli araç ortak bağlam üzerinden güvenilir aracı etkileyebilir. Ortak kök neden: "MCP clients inherit trust from the servers they connect to without continuous verification". B [ÖZET] — [Cloud Security Alliance, 6 May 2026](https://labs.cloudsecurityalliance.org/research-rb/csa-whitepaper-mcp-security-tool-poisoning-20260506-csa-styl/), [Elastic Security Labs](https://www.elastic.co/security-labs/mcp-tools-attack-defense-recommendations)
- Bir akademik çalışma katmanlı savunmayla (tanım bütünlüğü doğrulama, anlamsal inceleme) testlerinde GPT-4'ün güvensiz araç çağrılarının yaklaşık %71'ini engellediğini bildiriyor — yani savunmalı durumda bile kayda değer kaçak var. A (ön baskı) [ÖZET] — [arXiv 2512.06556](https://arxiv.org/abs/2512.06556)
- **2026 açıklamaları (ikincil blog; CVE numaraları bu oturumda birincil kaynaktan teyit edilemedi):** Anthropic'in Git MCP sunucusunda üç istem enjeksiyonu CVE'si (Ocak 2026); Cursor'da CVE-2026-50548 ve CVE-2026-50549 (Temmuz 2026; MCP sunucusu veya zehirli web aramasından sıfır tıkla uzaktan kod çalıştırma); Azure DevOps MCP sunucusunda confused deputy (Temmuz 2026); Microsoft 365 Copilot'ta tek tıkla sızdırma "SearchLeak" (CVE-2026-42824). B [ÖZET] — [Hive Security blog](https://hivesecurity.gitlab.io/blog/prompt-injection-attack-detect-2026/)
- ABD NSA, 2 Haziran 2026 tarihli bir "MCP Security" siber güvenlik bilgi notu yayımladı (yalnız başlık ve adres görüldü; içerik okunmadı). B [ÖZET] — [media.defense.gov](https://media.defense.gov/2026/Jun/02/2003943289/-1/-1/0/CSI_MCP_SECURITY.PDF)

**2.7 Önlemler**

- OWASP LLM01:2025: "it is unclear if there are fool-proof methods of prevention for prompt injection"; etkisini azaltan önlemler: model davranışını kısıtlama, çıktı biçimini deterministik kodla doğrulama, girdi/çıktı süzme, en az yetki (işlevler modelin değil kodun elinde, kendi belirteçleriyle), yüksek riskli eylemde insan onayı, dış içeriği ayırıp işaretleme, düzenli saldırı testi. B [SAYFA] — [OWASP Top 10 for LLM Applications, LLM01](https://github.com/OWASP/www-project-top-10-for-large-language-model-applications/blob/main/2_0_vulns/LLM01_PromptInjection.md)
- Anthropic'in CISO kılavuzu (17 Tem 2026): her ajan kullanım örneği için dört soru (hangi güvenilmeyen içeriği okuyor; hangi eylemleri kimin kimliğiyle yapıyor; hizasız davranırsa etki alanı ne; hangi gözlenebilirlik var); yedi denetim: kimliği mevcut kimlik sağlayıcıdan verme/geri alma, bağlayıcı izin listesi, araç ve eylem bazında onay ("Remove specific verbs", e-posta gönderme gibi tek yönlü eylemde insan incelemesi), korumalı çalışma ortamı (üretim kimlik bilgileri ajan döngüsünün ortamında olmasın), çıkış (egress) izin listesi, her araç çağrısını SIEM'e akıtma, kurum çapı kapatma anahtarı; istem enjeksiyonu başarı oranları "are not zero"; kişisel ajanlarla veri sızıntısı "the most likely threat vector for agentic systems". C [İDDİA] [SAYFA] — [CISO's guide to agentic AI](https://claude.com/blog/ciso-guide-to-agentic-ai)

### Inferences

- **ERP bağlayıcısı için tasarım kuralları (D; yukarıdaki kaynaklardan türetildi):** (1) yalnız okuma araçları, her biri tek iş yapan, `readOnlyHint` işaretli; (2) genel sorgu/`api_request`/SQL aracı yok — bu zaten "AI yalnız şemalı backend araçlarını çağırır" değişmeziyle aynı; (3) kullanıcı başına OAuth (yetkilendirme kodu + PKCE), belirteç hedef kitlesi MCP sunucusu, ERP kullanıcısı ve `CompanyId` ile eşleme, paylaşılan API anahtarı yok; (4) alan izin listesi ve satır yetkisi sunucuda; (5) her çağrı mevcut `ai.tool.called` denetimine; (6) hız sınırı ve sonuç boyutu sınırı (toplu veri çekmeyi engeller); (7) araç tanımları sürümlü ve sabit (rug pull'a karşı istemci tarafında pin); (8) kurum politikasında yazma kategorisi "Blocked".
- **Ağ gerçeği:** claude.ai bağlayıcısı için ERP MCP ucunun internetten (Anthropic IP aralıklarından, 443) erişilebilir olması gerekir; tünel seçeneği claude.ai'de yok. Şirket içi/hibrit kurulum hedefi (OPEN-014) için bu, içeri doğru bir yol açmak demektir — ters vekil + IP izin listesi + OAuth ile yapılabilir ama ayrı bir güvenlik ve sahip kararıdır.
- **Takma ad (AI-D4) dış arayüzde geri çözülemez:** eşleme yalnız ERP içinde tutulduğu için dış asistanın ekranında kullanıcı takma adları görür. Dolayısıyla bağlayıcı pratikte yalnız "Genel" ve "Şirket içi" sınıfı veriler için anlamlıdır; fiyat/bakiye gibi hassas sorular ERP içi asistanda kalmalıdır.
- **Trifecta'yı kırmanın gerçekçi yolu veriyi daraltmak ve oturumu ayırmaktır:** dış asistanın çıkış yolunu (bağlantı, görsel, web araması, başka bağlayıcılar) şirket tamamen kapatamaz; bu yüzden ERP bağlayıcısının açık olduğu çalışma alanında web araması ve e-posta gibi güvenilmeyen içerik getiren bağlayıcılar kapalı tutulmalı, bağlayıcı yalnız sızması tolere edilebilir veri vermelidir.
- **Zamanlı görev + bağlayıcı:** gözetimsiz koşuda onay isteyen araç görevi durdurduğu için (Claude) zamanlı görevlerde yalnız "Always allow" verilmiş okuma araçları çalışır — bu istenen davranıştır.

### Gaps

- **Gemini uygulaması / Workspace ve Perplexity'de özel remote MCP bağlayıcı desteği:** doğrulanamadı. [BELLEK]: Microsoft tarafında Copilot Studio ajanlarının MCP sunucularına bağlanabildiği 2025'te duyurulmuştu; güncel plan kapsamı teyit edilmedi.
- **Bağımsız, güncel istem enjeksiyonu başarı oranı** (bağlayıcılı sohbet asistanları için) bulunamadı; Anthropic'in %0,08 rakamı kendi iç testidir ve yalnız tarayıcı uzantısı içindir.
- **MCP "security best practices" belgesinin tam metni** (oturum ele geçirme, SSRF, yerel sunucu riski) açılamadı; yalnız arama özeti var.
- **2026 CVE'leri** için NVD/satıcı duyurusu teyidi yapılamadı.
- [BELLEK] olarak bilinen ama doğrulanamayan önlem çerçeveleri: Meta'nın "Agents Rule of Two" yazısı (Ekim 2025), Google DeepMind'ın CaMeL çalışması (arXiv 2503.18813), "Design Patterns for Securing LLM Agents against Prompt Injections" (arXiv 2506.08837), Invariant Labs'in GitHub MCP ve araç zehirleme yazıları (2025).

---

## 3. Veri koruma: sağlayıcı şartları, sıfır saklama, veri yerleşimi ve KVKK

### Takeaway

Belirleyici ayrım tüketici planı ile iş/API planı arasındadır: Anthropic'te tüketici verisi (ayar açıksa) eğitimde kullanılır ve 5 yıl saklanır; ticari planlarda varsayılan olarak eğitim yoktur; API/Claude Code tarafında belgelenen standart saklama 30 gündür, sohbet uygulamasında ise konuşmalar silinene (Enterprise'da ayarlanan özel süre dolana) kadar durur; sıfır saklama (ZDR) yalnız sözleşmeyle, yalnız API ve Claude Code için vardır — Team/Enterprise sohbet arayüzleri ZDR kapsamında değildir. Hiçbir sağlayıcı için Türkiye içi işleme doğrulanamadı; dolayısıyla kişisel veri göndermek KVKK md. 9 anlamında yurt dışına aktarımdır (M) ve pratikte en sağlam kural dış asistana kişisel veri ve "çok gizli" veri hiç girmemektir.

### Cited Findings

**3.1 Anthropic — eğitim ve saklama**

- Eğitim: tüketici planlarında "We will train new models using data from Free, Pro, Max, and Ultra accounts when this setting is on"; ticari kullanıcılarda (Team, Enterprise, API, üçüncü taraf platformlar) "Anthropic does not train generative models using code or prompts sent to Claude Code under commercial terms, unless the customer has chosen to provide their data to us for model improvement". C [İDDİA] [SAYFA] — [Claude Code data usage](https://code.claude.com/docs/en/data-usage)
- Saklama (Claude Code verisi için belgelenen süreler): tüketici — model geliştirmeye izin verenlerde 5 yıl, vermeyenlerde 30 gün; ticari (Team, Enterprise, API) — standart 30 gün; ZDR yalnız uygun hesaplara, hesap ekibi tarafından kurum bazında açılır. C [İDDİA] [SAYFA] — [Claude Code data usage](https://code.claude.com/docs/en/data-usage)
- Enterprise sohbet verisi: "By default, data is retained indefinitely unless a custom retention period is set"; "The minimum retention period is 30 days"; süreyi Owner/Primary Owner ayarlar; güvenlik sistemlerinin işaretlediği içerik ve yasal zorunluluk istisnadır. C [İDDİA] [SAYFA] — [Configure custom data retention controls for Enterprise plans](https://support.claude.com/en/articles/10440198-configure-custom-data-retention-controls-for-enterprise-plans)
- API: "Retained data is never used for model training without your express permission"; ZDR'de Anthropic "does not store customer prompts or responses at rest after the API response is returned"; ZDR satış ekibi üzerinden, kurum bazında. C [İDDİA] [SAYFA] — [API and data retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)
- **ZDR'nin kapsamadıkları:** Claude Console; Managed Agents (oturum dökümleri silinene kadar durur); tüketici planları; "Claude Teams and Claude Enterprise product interfaces: These interfaces are not ZDR-eligible" (tek istisna Enterprise'da Claude Code için ayrı bir ZDR teklifi); Claude for Excel; üçüncü taraf entegrasyonları. ZDR'ye uygun olmayan API özellikleri: MCP connector, code execution, Files API, Batch (29 gün), Agent Skills, message threads. Web araması ZDR'ye uygundur (dinamik süzme hariç). C [İDDİA] [SAYFA] — [API and data retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)
- **Model kısıtı:** Claude Fable 5/5.1/5.2 ve Mythos 5/5.1/5.2 "require 30-day data retention" ve Anthropic açıkça yetkilendirmedikçe ZDR altında kullanılamaz. C [İDDİA] [SAYFA] — [API and data retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)
- Her düzenlemede istisna: güvenlik sistemleri bir sohbeti işaretlerse "Anthropic may retain inputs and outputs for up to 2 years". C [İDDİA] [SAYFA] — [API and data retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)
- Rol: ticari ürünlerde "the customer is the "Controller" of the data submitted by its Users", Anthropic "Processor"dür. C [İDDİA] [SAYFA] — [Does Anthropic act as a Data Processor or Controller?](https://support.claude.com/en/articles/9267385-does-anthropic-act-as-a-data-processor-or-controller)
- Bağlayıcılar: "Connected services process data on their own infrastructure, under their own terms, which may be located outside the United States." C [İDDİA] [SAYFA] — [Use connectors to extend Claude's capabilities](https://support.claude.com/en/articles/11176164-use-connectors-to-extend-claude-s-capabilities)

**3.2 Anthropic — veri yerleşimi**

- API'de çıkarım coğrafyası yalnız `"global"` (varsayılan) veya `"us"` (1,1 kat fiyat); "Workspace geo: Only "us" is currently available" — birinci taraf API'de AB'ye özel seçenek yok. C [İDDİA] [SAYFA] — [Data residency](https://platform.claude.com/docs/en/manage-claude/data-residency)
- Amazon Bedrock ve Google Cloud üzerinden bölgesel/çok bölgeli uç noktalar vardır ve küresel uç noktalara göre %10 pahalıdır; o platformlarda veri işleyen bulut sağlayıcısıdır. C [İDDİA] [SAYFA] — [Pricing](https://platform.claude.com/docs/en/about-claude/pricing), [API and data retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)
- Enterprise uygulamasında adı geçen tek bölgesel seçenek "US-only inference". C [İDDİA] [SAYFA] — [Claude Enterprise plan](https://claude.com/solutions/enterprise)

**3.3 Microsoft** — yalnız pazarlama sayfası açılabildi

- "Microsoft 365 Copilot is designed to work within your organization's existing security and compliance boundaries"; "Copilot respects permissions and policies set by Microsoft Purview". C [İDDİA] [SAYFA] — [Microsoft 365 blog, 2 Ara 2025](https://www.microsoft.com/en-us/microsoft-365/blog/2025/12/02/microsoft-365-copilot-business-the-future-of-work-for-small-businesses/)

**3.4 KVKK — yurt dışına aktarım (M; resmî sayfalar açılamadı, teyit gerekir)**

- Standart sözleşme, "imzaların tamamlanmasından itibaren beş iş günü içinde fiziki olarak veya kayıtlı elektronik posta (KEP) adresi ya da Standart Sözleşme Bildirim Modülü vasıtasıyla Kuruma bildirilmesi gerekmektedir"; süre denetimi için iki tarafın imza tarihleri metinde yer almalı; sözleşmeyi aktarımın tarafları veya yetkili temsilcileri imzalamalı, imza eksikse sözleşme geçersizdir; yalnız seçimlik/alternatif maddeler değiştirilebilir. M [ÖZET] — [KVKK kamuoyu duyurusu (standart sözleşmelerde dikkat edilecek hususlar)](https://www.kvkk.gov.tr/Icerik/8170/Yurt-Disina-Kisisel-Veri-Aktariminda-Kullanilacak-Standart-Sozlesmelerde-Dikkat-Edilmesi-Gereken-Hususlara-Iliskin-Kamuoyu-Duyurusu)
- Standart sözleşme metinleri Kurul'un 04.06.2024 tarihli ve 2024/959 sayılı kararıyla belirlendi; imzalanıp bildirilen standart sözleşme ek izin gerektirmeden aktarıma imkân verir; Bildirim Modülü duyurusu 25 Ekim 2024 tarihlidir. M [ÖZET] — [KVKK Standart Sözleşme Bildirim Modülü duyurusu](https://www.kvkk.gov.tr/Icerik/8043/Standart-Sozlesme-Bildirim-Modulu-Hakkinda-Kamuoyu-Duyurusu), [KVKK Standart Sözleşmeler](https://www.kvkk.gov.tr/Icerik/7929/Standart-Sozlesmeler)
- Yapay zekâ hizmetlerine özgü ayrı bir aktarım rejimi, sektör rehberi veya Kurul kararı arama sonuçlarında çıkmadı; genel standart sözleşme ve bildirim kuralları geçerli görünüyor. M [ÖZET] — [KVKK Yurt Dışına Aktarım sayfası](https://www.kvkk.gov.tr/Icerik/2053/Yurtdisina-Aktarim)
- Bir hukuk bürosu yazısı (2026) 5 iş günü bildiriminin zorunlu olduğunu ve yapılmamasının idari yaptırıma bağlandığını aktarıyor (blog; resmî kaynak değil). M/B [ÖZET] — [bilalalyar.av.tr](https://bilalalyar.av.tr/?p=73582)

**3.5 Bellekten — bu oturumda doğrulanamadı, teyit edilmeden rapora girmemeli**

- [BELLEK] M: 6698 sayılı Kanun'un 9. maddesi 7499 sayılı Kanun'la değişti (Resmî Gazete 12.03.2024), değişiklik 1 Haziran 2024'te yürürlüğe girdi. Yeni sıra: (i) yeterlilik kararı; (ii) uygun güvenceler — kamu kurumları arası anlaşma, Kurul onaylı bağlayıcı şirket kuralları, Kurula 5 iş günü içinde bildirilen standart sözleşme, Kurul izinli yazılı taahhütname; (iii) yalnız **arızi** hâllerde istisnalar (ör. ilgili kişinin, riskler hakkında bilgilendirilerek açık rıza vermesi). Yani açık rıza artık süreklilik arz eden aktarımlar için genel dayanak değildir. Usul ve esaslar yönetmeliği Resmî Gazete'de 10.07.2024'te yayımlandı. Standart sözleşmeyi süresinde bildirmemek için kanunda ayrı bir idari para cezası aralığı vardır (2024 metninde 50.000–1.000.000 TL; her yıl yeniden değerlenir — **güncel tutar doğrulanmadı**). Kaynak adresi: [KVKK Yurt Dışına Aktarım](https://www.kvkk.gov.tr/Icerik/2053/Yurtdisina-Aktarim)
- [BELLEK] C: OpenAI, ChatGPT Business/Enterprise ve API verisini varsayılan olarak eğitimde kullanmadığını; API'de kötüye kullanım izleme kayıtlarını 30 güne kadar tuttuğunu, uygun müşterilere sıfır saklama ve (2025'ten beri) Avrupa veri yerleşimi sunduğunu beyan ediyordu — [openai.com/enterprise-privacy](https://openai.com/enterprise-privacy/)
- [BELLEK] C: Google, Workspace'teki Gemini'de müşteri verisinin izinsiz olarak alan dışı model eğitiminde kullanılmadığını beyan ediyordu — [Workspace üretken AI gizlilik merkezi](https://support.google.com/a/answer/15706919). Microsoft'un "enterprise data protection" taahhütleri — [learn.microsoft.com](https://learn.microsoft.com/en-us/copilot/microsoft-365/enterprise-data-protection)

### Inferences

- **AI-D5 ("veri saklamayan ve eğitmeyen sağlayıcı") dar okunursa** yalnız ZDR'li API kullanımı bu şartı karşılar; bu da (a) Fable/Mythos sınıfı modelleri, (b) MCP connector, code execution, Files API, Managed Agents gibi durumlu özellikleri ve (c) bütün Team/Enterprise sohbet aboneliklerini dışarıda bırakır. Personelin dış abonelikleri bu şartı sağlayamaz; dolayısıyla ya bu aboneliklere ERP verisi hiç girmez, ya da AI-D5'in kapsamı ("yalnız ERP'nin sağlayıcı bağlantısı için") açıkça yazılır. Bu bir sahip kararıdır.
- **AB veri yerleşimi KVKK sorununu çözmez (M, teyit gerekir):** veri Türkiye dışına çıktığı sürece md. 9 uygulanır; ABD yerine AB'de işleme yalnız risk profilini değiştirir.
- **Standart sözleşme yolu self-serve aboneliklerde pratikte zor (D):** KVKK metninin değiştirilmeden, iki tarafça imzalanıp 5 iş günü içinde bildirilmesi gerekir; büyük sağlayıcıların self-serve planlarda Türkiye'ye özgü metni imzalayıp imzalamadığı doğrulanmadı. Bu yüzden "kişisel veri hiç gönderilmez" kuralı hem hukuken hem operasyonel olarak en ucuz yoldur ve mevcut karar çizgisiyle (kişisel ve "çok gizli" veri dışarı çıkmaz) aynıdır.
- **Veri azaltma için pratik kurallar (D):** (1) kişi adı, T.C. kimlik no, telefon, e-posta, IBAN, imza, özlük ve sağlık verisi dış asistana girmez; (2) müşteri/tedarikçi unvanı + fiyat/bakiye birlikte girmez — gerekiyorsa "Müşteri A" gibi elle takma ad; (3) belge yüklemeden önce kişisel alanlar karartılır; (4) ERP ekran görüntüsü ve dışa aktarım dosyası yüklenmez; (5) yalnız iş planı hesabı, kurumsal e-postayla; (6) bellek özelliği iş hesabında şirket politikasına göre ayarlanır; (7) sohbet paylaşım bağlantıları kapalı.
- **Tüketici hesabıyla şirket işi yapılmamalı:** eğitimde kullanım ve 5 yıla varan saklama riski yalnız tüketici planlarında vardır; Enterprise'daki kiracı kısıtı kurum ağından kişisel hesapları engelleyebilir, Team'de bu yoktur — orada kural ve eğitimle yürür.

### Gaps

- **OpenAI, Google, Perplexity ve Microsoft'un güncel veri kullanımı / saklama / yerleşim şartları** birincil kaynaktan doğrulanamadı (alan adları kapalıydı).
- **Anthropic'in ticari saklama ayrıntıları** (privacy.claude.com'daki "How long do you store my organization's data?") açılamadı; API sayfasındaki "Conversation content ... is not retained by default" ifadesi ile Claude Code belgesindeki "Standard: 30-day retention" ifadesi arasındaki fark sözleşme/gizlilik merkezinden netleştirilmeli.
- **KVKK:** 9. maddenin güncel metni, idari para cezasının 2026 tutarı, yeterlilik kararı verilmiş ülke olup olmadığı, Kurumun üretken yapay zekâ rehberi/bilgi notları ve takma adlı verinin kişisel veri sayılıp sayılmayacağı doğrulanamadı — hukuk danışmanı teyidi şart.
- **Sağlayıcıların Türkiye standart sözleşmesini imzalama pratiği** hakkında kaynak bulunamadı.
- **Toplantı kaydı/not araçlarında aydınlatma ve rıza** gereklilikleri araştırılamadı.

---

## 4. Çelik tüccarı / işleyicisi için yüksek değerli dış kullanımlar ve gerçekçi doğruluk sınırları

### Takeaway

En yüksek değer okuma-özetleme-taslak işlerindedir: fiyat ve kur izleme özetleri, mevzuat takibi, müşteri/tedarikçi ön araştırması, ihale tarama, yazışma ve çeviri, sözleşme ön okuması, tablo analizi, personel eğitimi. En zayıf nokta rakam ve mevzuat doğruluğudur: asistan kaynak göstermeden fiyat ya da madde numarası üretebilir; bu yüzden "kaynak bağlantısı ve tarihi olmayan rakam yok, resmî metin açılmadan karar yok" alışkanlığı şarttır. Uyarı: bu bölümdeki kamuya açık kaynak listesi ve bağımsız doğruluk çalışmaları bu oturumda açılamadı ([BELLEK]); sektöre özgü bağımsız bir vaka kanıtı bulunamadı.

### Cited Findings

**4.1 Bu oturumda doğrulananlar**

- Zamanlı görev altyapısı dört üründe hazır (Bölüm 1.7); Claude'da zamanlı görev web araştırması, e-posta/takvim özeti ve tablo raporu örnekleriyle belgelenmiş. C [İDDİA] [SAYFA] — [Schedule recurring tasks in Claude Cowork](https://support.claude.com/en/articles/13854387-schedule-recurring-tasks-in-claude-cowork)
- Perplexity Enterprise Pro'da zamanlı görevlerden ayrı 25 "fiyat alarmı" kotası var. C [İDDİA] [ÖZET] — [Perplexity Tasks](https://intercom.help/perplexity-ai/en/articles/11521526-perplexity-tasks)
- Tarayıcı ajanı için satıcının kendi sınırı: finans hesapları, hukuki belge/sözleşme ve hassas şirket verisi içeren iş hesapları için kullanılmamalı. C [İDDİA] [SAYFA] — [Use Claude in Chrome safely](https://support.claude.com/en/articles/12902428-use-claude-in-chrome-safely)
- Research modu bağlı araçları ek onay olmadan çağırabilir; araştırma oturumlarında yazma araçları kapatılmalı. C [İDDİA] [SAYFA] — [Custom connectors](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp)
- Satıcı vaka beyanı (bağımsız doğrulama yok): bir derecelendirme kuruluşunda kredi notu hazırlığının "40 hours to 2 minutes"e indiği; klinik belgelerde "10 weeks to 10 minutes". C [İDDİA] [SAYFA] — [Claude Enterprise plan](https://claude.com/solutions/enterprise)
- Excel/tablo: Claude for Excel pivot tablo ve koşullu biçimlendirme desteğiyle (5 Şub 2026) ücretli planlarda; Microsoft tarafında Analyst ajanı ve Excel'de Copilot. C [İDDİA] [SAYFA] — [Release notes](https://support.claude.com/en/articles/12138966-release-notes), [Microsoft 365 Copilot pricing](https://www.microsoft.com/en-us/microsoft-365-copilot/pricing)
- Toplantı notu: Claude Meetings beta (12 Kas 2026; Team/Enterprise; Google Meet). C [İDDİA] [SAYFA] — [Release notes](https://support.claude.com/en/articles/12138966-release-notes)

**4.2 Bellekten: kamuya açık izleme kaynakları — adresler ve içerik bu oturumda doğrulanamadı**

| Konu | Kaynak (resmî adres) | Not |
|---|---|---|
| Hurda ve inşaat demiri vadeli fiyatı | LME Steel Scrap CFR Turkey (Platts) ve LME Steel Rebar FOB Turkey (Platts) sözleşmeleri — [lme.com](https://www.lme.com/en/metals/ferrous/lme-steel-scrap-cfr-turkey-platts) | Nakit uzlaşmalı vadeli sözleşmeler; sitedeki veri gecikmeli. Fiziki fiyat değildir. |
| Fiziki fiyat değerlendirmeleri | S&P Global Platts, Fastmarkets, Argus, Kallanish, SteelOrbis, MEPS | **Ücretli**; asistan ödeme duvarının arkasını okuyamaz, ikincil haberlerden eski rakam aktarabilir; yeniden dağıtım lisans kısıtı olabilir. |
| Döviz kuru | TCMB gösterge kurları ([tcmb.gov.tr/kurlar/today.xml](https://www.tcmb.gov.tr/kurlar/today.xml)) ve EVDS | Makinece okunur; LLM'e sordurmak yerine deterministik çekilmeli. |
| Mevzuat | Resmî Gazete ([resmigazete.gov.tr](https://www.resmigazete.gov.tr)), Gelir İdaresi duyuru ve mevzuatı ([gib.gov.tr](https://www.gib.gov.tr)), Ticaret Bakanlığı (ithalatta korunma, damping, gözetim tebliğleri — [ticaret.gov.tr](https://ticaret.gov.tr)) | Günlük yayın; asistan özetler, karar resmî metinden verilir. |
| AB tarafı | Sınırda Karbon Düzenleme Mekanizması (CBAM) — [Avrupa Komisyonu](https://taxation-customs.ec.europa.eu/carbon-border-adjustment-mechanism_en) | Çelik kapsamda; kesin dönem 1 Ocak 2026'da başladı (teyit edilmeli). |
| İhale | EKAP ([ekap.kik.gov.tr](https://ekap.kik.gov.tr)), AB için TED ([ted.europa.eu](https://ted.europa.eu)) | Oturum açmadan aranabilen ilanlar; giriş gerektiren kısımlar için tarayıcı ajanı önerilmez. |
| Sektör istatistiği | worldsteel ([worldsteel.org](https://worldsteel.org)), Türkiye Çelik Üreticileri Derneği ([celik.org.tr](https://celik.org.tr)) | Aylık üretim/dış ticaret verileri. |

**4.3 Bellekten: doğruluk sınırına dair bağımsız çalışmalar — rakamlar teyit edilmeden rapora girmemeli**

- [BELLEK] B: Avrupa Yayın Birliği ve BBC'nin "News Integrity in AI Assistants" çalışması (Ekim 2025; 18 ülke, 22 yayın kuruluşu): AI asistanlarının haber yanıtlarının %45'inde en az bir önemli sorun, %31'inde ciddi kaynak gösterme sorunu, %20'sinde önemli doğruluk sorunu — [ebu.ch](https://www.ebu.ch/research/open/report/news-integrity-in-ai-assistants)
- [BELLEK] B: Columbia Tow Center (Mart 2025): sekiz AI arama aracının haber kaynağı bulma sorgularının %60'ından fazlasına yanlış yanıt verdiği — [cjr.org](https://www.cjr.org/tow_center/we-compared-eight-ai-search-engines-theyre-all-bad-at-citing-news.php)
- [BELLEK] A: Stanford çalışması (Magesh ve ark., 2024): "halüsinasyonsuz" diye pazarlanan, kaynağa dayalı hukuk araştırma araçlarının sorguların %17–33'ünde uydurma/yanlış bilgi ürettiği — [arXiv 2405.20362](https://arxiv.org/abs/2405.20362)

**4.4 İş → araç → doğrulama alışkanlığı (öneri tablosu; D)**

| İş | Kim | Uygun özellik | Zorunlu doğrulama alışkanlığı | Veri kuralı |
|---|---|---|---|---|
| Günlük fiyat/kur/sektör özeti | Sahip, satış ve alış yöneticileri | Zamanlı görev + web araştırması | Her rakamın yanında kaynak bağlantısı, kaynak tarihi ve birimi; haftada bir özet elle örneklenir; "kaynak bulunamadıysa rakam yazma" talimatı | Şirketin kendi fiyatı/stok bilgisi girilmez |
| Mevzuat takibi | Mali işler, dış ticaret | Zamanlı görev (Resmî Gazete, vergi idaresi, ticaret politikası önlemleri) | Karar yalnız resmî metin açılıp sayı/tarih doğrulandıktan ve mali müşavir/hukukçu onayından sonra | Şirket verisi gerekmez |
| Müşteri/tedarikçi ön araştırması | Satış, alış, finans | Derin araştırma | Ticaret sicili, yaptırım listeleri gibi birincil kaynak bağlantısı; iddia başına kaynak | Yalnız tüzel kişi düzeyi açık bilgi; yetkili kişilerin kişisel verisi toplanmaz |
| İhale tarama | Satış | Zamanlı görev | İlan numarası ve son tarih resmî sayfadan teyit | — |
| E-posta taslağı ve çeviri | Herkes | Sohbet, Outlook/Gmail eklentisi | Gönderen okur ve sorumludur; rakam, tarih, teslim şekli (Incoterms), ödeme koşulu elle kontrol | Kişisel veri, IBAN, özel fiyat girilmez ya da elle takma adlanır |
| Sözleşme ön okuması | Yönetim | Belge analizi | Yalnız "bakılacak maddeler listesi"; hukuki görüş değildir, bağlayıcı yorum hukukçudan | Kişisel alanlar karartılır; "çok gizli" sözleşme yüklenmez |
| Tablo analizi | Yöneticiler | Excel/Sheets eklentisi, veri analizi | Toplamlar pivot veya ERP raporuyla karşılaştırılır; formül hücreleri gözden geçirilir | ERP dışa aktarımı yüklenmez; örnek/özet veriyle çalışılır |
| Toplantı notu | Yöneticiler | Meet/Teams/Claude Meetings | Katılımcılar bilgilendirilir; kararlar ve sorumlular elle teyit | Kayıt ve döküm saklama süresi belirlenir (M) |
| Personel eğitimi | İK, birim yöneticileri | Proje/defter (iç el kitabı yüklenmiş), soru-cevap | İçerik sahibi onaylar; sürüm tarihi yazılır | Yalnız "Genel/Şirket içi" belgeler |

### Inferences

- **Kur ve formül hesabı LLM'e bırakılmamalı:** depo değişmezi zaten "formül/UI/rapor aynı deterministik backend calculator'ı kullanır" der; dış asistanın ürettiği tablo ve hesap hiçbir zaman kayıt kaynağı olmamalı.
- **Fiyat izleme özeti bir "haber özeti"dir, fiyat beslemesi değildir:** ücretli değerlendirme aboneliği yoksa asistanın verdiği fiziki fiyatlar ikincil ve gecikmelidir; teklif fiyatına doğrudan girdi yapılmamalı.
- **Zamanlı özetlerde sessiz bozulma riski:** kaynak sayfa yapısı değişince görev eski veya boş veriyle "başarılı" görünebilir; özetin başına "kaynakların son güncelleme tarihi" satırı ve "değişiklik yoksa açıkça 'yok' yaz" talimatı eklenmeli.
- **Mevzuat özetinde en riskli hata uydurma madde/tebliğ numarasıdır;** hukuk araçlarında bile %17–33 hata bildirildiği ([BELLEK]) düşünülürse genel asistanlarda oran daha düşük varsayılamaz.

### Gaps

- **Çelik ticaretine özgü, bağımsız AI kullanım vakası veya ölçülmüş fayda** bulunamadı (arama kotası doldu).
- **Türkçe mevzuat sorularında asistan doğruluğunu ölçen bağımsız çalışma** bulunamadı.
- **Kaynak listesindeki adreslerin ve erişim koşullarının (ücret, lisans, API)** tamamı doğrulanmadı.
- **Çeviri ve toplantı notu kalitesi için güncel nicel kanıt** toplanamadı.
- **Ücretli fiyat sağlayıcılarının verisinin AI ile özetlenip şirket içinde dağıtılmasının lisans şartlarına uygunluğu** incelenmedi.

---

## 5. Yinelenen güvenlik ve teknoloji takibi: kaynaklar, uyarıdan hazır değişikliğe, insan kapıları

### Takeaway

Takibin omurgası deterministik beslemeler olmalı (GitHub Advisory Database / Dependabot, NuGet denetimi, npm audit, .NET duyuru deposu, KEV, EPSS); yapay zekânın değeri "bu bizi etkiliyor mu?" triyajı, haftalık özet ve hazır değişiklik önerisindedir. GitHub artık bir Dependabot uyarısını AI ajanına (Copilot, Codex, Claude) atayıp taslak PR açtırabiliyor; birleştirme onayı, dağıtım ve sırlar insanda kalmalıdır. Depoda bugün CI denetimleri var, ama sürekli izleme ve otomatik güncelleme PR'ı yapılandırması yok.

### Cited Findings

**5.1 Kaynaklar**

- **GitHub Advisory Database (22.11.2026):** 38.049 GitHub incelemeli tavsiye; npm 8.184, NuGet 1.242; incelenmemiş 372.160; **kötü amaçlı paket tavsiyesi 127.509, bunun 111.546'sı npm**, 264'ü NuGet. B [SAYFA] — [github.com/advisories](https://github.com/advisories)
- **.NET:** Microsoft güvenlik duyuruları `dotnet/announcements` deposunda "Security" etiketli issue olarak yayımlanır ("Microsoft Security Advisory CVE-… | ASP.NET Core Elevation of Privilege Vulnerability" biçiminde), aylık yama gününe "Patch-Tuesday" etiketi ve aylık özet issue eşlik eder; en son parti 10–11 Kasım 2026 (ör. CVE-2026-118741 ASP.NET Core yetki yükseltme, CVE-2026-105544 .NET yetki yükseltme). C [SAYFA] — [dotnet/announcements (Security)](https://github.com/dotnet/announcements/issues?q=is%3Aissue+label%3ASecurity)
- **NuGet:** restore sırasında bağımlılıklar bilinen açıklara karşı denetlenir (NU1901 düşük … NU1904 kritik); "The data source for nuget.org's vulnerability database is GitHub Advisory Database"; "NuGetAuditMode defaults to all when a project targets net10.0 or higher" (yani geçişli bağımlılıklar da); NU1903/NU1904 hata sayılacak biçimde yapılandırılabilir; bastırma (`NuGetAuditSuppress`) "should be considered a last resort"; `dotnet list package --vulnerable` için `--include-transitive` varsayılan değildir. C [SAYFA] — [NuGet: Auditing packages](https://github.com/NuGet/docs.microsoft.com-nuget/blob/main/docs/concepts/Auditing-Packages.md)
- **React:** Aralık 2025'ten beri React Server Components / Server Functions için 10 tavsiye; ikisi kritik: GHSA-fv66-9v8q-g76r (3 Ara 2025) ve GHSA-xqc2-xf4c-x3h3 / CVE-2026-126187 (14 Eki 2026, CVSS v4 9,5; `react-server-dom-webpack/turbopack/parcel` 19.0.0–19.3.0 etkilenir; yamalı sürümler 19.0.9, 19.1.10, 19.2.9, 19.3.1). Sunucu bileşeni desteklemeyen çatı/paketleyici kullanmayan uygulamalar için tavsiye açıkça "your app is not affected by this vulnerability" der. C [SAYFA] — [facebook/react security advisories](https://github.com/facebook/react/security/advisories), [GHSA-xqc2-xf4c-x3h3](https://github.com/facebook/react/security/advisories/GHSA-xqc2-xf4c-x3h3)
- **KEV (bilinen istismar edilen açıklar):** CISA kataloğunun JSON/CSV aynası GitHub'da; cisa.gov ile "dakikalar içinde" eşitlenir, güncellemeler genellikle ABD Doğu saatiyle hafta içi. B [SAYFA] — [cisagov/kev-data](https://github.com/cisagov/kev-data)
- [BELLEK] B: NVD ([nvd.nist.gov](https://nvd.nist.gov)); EPSS — bir açığın önümüzdeki 30 günde istismar edilme olasılığı tahmini ([first.org/epss](https://www.first.org/epss/)); OSV ([osv.dev](https://osv.dev)); Türkiye ulusal müdahale merkezi USOM güvenlik bildirimleri ([usom.gov.tr](https://www.usom.gov.tr)); 7545 sayılı Siber Güvenlik Kanunu (Mart 2025) ve Siber Güvenlik Başkanlığı — kapsam ve bildirim yükümlülüğünün özel bir ticaret şirketi için geçerliliği **M, teyit gerekir**. SQL Server için Microsoft Güvenlik Yanıt Merkezi güncelleme kılavuzu. Bu oturumda hiçbiri açılamadı.

**5.2 Uyarı nasıl hazır değişikliğe dönüşür**

- Dependabot varsayılan dalı tarar ve yeni tavsiye eklendiğinde ya da bağımlılık grafiği değiştiğinde uyarı üretir; yalnız GitHub'ın incelediği tavsiyeler uyarı tetikler; "Alerts can't catch every security issue". C [SAYFA] — [GitHub Docs: Dependabot alerts](https://github.com/github/docs/blob/main/content/code-security/concepts/supply-chain-security/dependabot-alerts.md)
- **Ajanlara atama:** yazma yetkili kullanıcı bir uyarıyı ortak çalışana, takıma veya AI ajanına atayabilir; ajan türleri GitHub Copilot ve depo ayarında etkinleştirilmişse "Codex or Claude" gibi üçüncü taraf ajanlar; atamada "the agent automatically creates a session and opens a draft pull request with a proposed fix"; düzeltme üretilemezse ajan atanmış kalır ve oturum günlüğü görülebilir. Bu özellik açık depolarda da GitHub Advanced Security / Code Security lisansı ister. C [SAYFA] — [GitHub Docs: Dependabot alerts](https://github.com/github/docs/blob/main/content/code-security/concepts/supply-chain-security/dependabot-alerts.md)
- Dependabot security updates: uyarı için, bağımlılık grafiğini bozmadan mümkünse "the minimum version that includes the patch"e yükselten PR açar, değilse uyarıya hata yazar; "compatibility score" aynı güncellemeyi yapan diğer açık depolardaki CI geçme yüzdesidir; NuGet `PackageReference` için geçişli bağımlılıkta üst paketi güncelleyebilir ya da doğrudan başvuru ekleyebilir. C [SAYFA] — [GitHub Docs: Dependabot security updates](https://github.com/github/docs/blob/main/content/code-security/concepts/supply-chain-security/dependabot-security-updates.md)
- Renovate `minimumReleaseAge`: yeni sürümü önermeden önce belirli süre bekler (örnek 14 gün — araştırmacılara ve otomatik araçlara kötü amaçlı paketi yakalama zamanı tanır); "Security updates bypass minimumReleaseAge". C [SAYFA] — [Renovate: minimum release age](https://github.com/renovatebot/renovate/blob/main/docs/usage/key-concepts/minimum-release-age.md)
- Claude Code GitHub Action bir `schedule` (cron) tetikleyicisiyle gözetimsiz çalışabilir; düz metin istemde, izin verilmedikçe kabuk ve GitHub API erişimi yoktur; tetikleyenin yazma yetkisi ve insan olması denetlenir; uzun ömürlü anahtar yerine iş yükü kimlik federasyonu kullanılabilir; "Grant the workflow only the permissions it needs, and review Claude's changes before merging." C [İDDİA] [SAYFA] — [Claude Code GitHub Actions](https://code.claude.com/docs/en/github-actions)
- Claude Security (Enterprise, beta): "continuously watches your codebases for security vulnerabilities", bulguları düzeltilene dek izler ve "suggests targeted patches for human review"; proje başına en çok 8 depo; bulgular "multi-stage verification"dan geçer; "Scans are stochastic" (aynı kodun iki taraması farklı sonuç verebilir); ZDR desteklenmez; doğrudan token maliyetiyle faturalanır. C [İDDİA] [SAYFA] — [Use Claude Security](https://support.claude.com/en/articles/14661296-use-claude-security)
- Anthropic'in PR güvenlik inceleme eylemi: "This action is not hardened against prompt injection attacks and should only be used to review trusted PRs." C [İDDİA] [SAYFA] — [anthropics/claude-code-security-review](https://github.com/anthropics/claude-code-security-review)

**5.3 Önceliklendirme ve yanlış pozitif**

- GitHub'ın önerisi: erişilebilirlik (reachability) sonucunu "severity, Exploit Prediction Scoring System (EPSS) scores, dependency scope" ile birlikte kullanmak. C [SAYFA] — [GitHub Docs: Dependabot alerts](https://github.com/github/docs/blob/main/content/code-security/concepts/supply-chain-security/dependabot-alerts.md)
- Erişilebilirlik analizi açık önizlemede ve **yalnız JavaScript/TypeScript** (npm, pnpm, Yarn) için; C#/NuGet yok; "Reachability shows that a call path exists, not that an attacker can use it to exploit the vulnerability"; yalnız bu sonuca dayanarak uyarı kapatılmamalı. C [SAYFA] — [GitHub Docs: reachability analysis](https://github.com/github/docs/blob/main/content/code-security/concepts/supply-chain-security/about-reachability-analysis-for-dependabot-alerts.md)
- Otomatik triyaj kuralları: GitHub'ın hazır kuralı, geliştirme kapsamlı bağımlılıklardaki düşük etkili uyarıları ("feel like false alarms to most developers") bildirim göndermeden kapatır; sayfada sayısal oran yok; özel kurallar (önem derecesi, paket adı, CWE) lisans ister. C [SAYFA] — [GitHub Docs: auto-triage rules](https://github.com/github/docs/blob/main/content/code-security/concepts/supply-chain-security/dependabot-auto-triage-rules.md)

**5.4 Bu deponun bugünkü durumu (salt-okunur kontrol, 22.11.2026)**

- CI zaten `dotnet list GoymenERP.slnx package --vulnerable --include-transitive` (`.github/workflows/ci.yml` satır 111) ve `npm audit --audit-level=high` (satır 454) çalıştırıyor. — depo dosyası
- `.github/dependabot.yml` ve Renovate yapılandırması yok. — depo kökü
- Ön yüzün doğrudan çalışma zamanı bağımlılıkları 4 adet (`react ^19.2.8`, `react-dom ^19.2.8`, `react-router 8.3.1` dâhil) ve 12 geliştirme bağımlılığı; `react-server-dom-*` doğrudan bağımlılık değil (geçişli bağımlılık kontrol edilmedi). — `src/frontend/goymen-erp-web/package.json`
- `Directory.Build.props` içinde `TreatWarningsAsErrors=true`; SDK `10.0.300` (`global.json`). — depo kökü

### Inferences

- **Önerilen akış (D):** (1) algılama deterministik — Dependabot uyarıları + mevcut CI denetimleri; (2) haftalık (ve kritik durumda anlık) AI özeti — yeni .NET/React/npm/NuGet tavsiyeleri, KEV'e eklenen ve yığınımıza değenler, destek süresi biten sürümler; her kalem için "etkiler / etkilemez / belirsiz" ve gerekçe; (3) düzeltme — bağımlılık yükseltmesi için Dependabot/Renovate PR'ı, kod değişikliği gerekiyorsa kodlama ajanının taslak PR'ı; (4) hepsi normal review + CI + (kapsamına göre) K3 yolundan geçer.
- **İnsanda kalması gerekenler** (AGENTS.md'deki insan kapılarıyla aynı): birleştirme onayı kuralları, dağıtım ve üretime alma, sır/anahtar yönetimi, iş akışlarının Settings → Actions'ta etkinleştirilmesi, yetki/rol değişikliği. `.github/dependabot.yml` eklemek bir `.github/**` değişikliğidir (K3, iki review).
- **React örneği triyajın değerini gösterir:** 2026'da altı React Server Components tavsiyesi çıktı, biri kritik (CVSS 9,5); bu ön yüz sunucu bileşeni paketlerini doğrudan kullanmadığı için büyük olasılıkla etkilenmiyor — "kritik" etiketi tek başına acil iş anlamına gelmez, ama gerekçe yazılı olmalı ve geçişli bağımlılık doğrulanmalı.
- **Olası operasyonel etki (teyit edilmeli):** .NET 10 hedefinde NuGet denetimi geçişli bağımlılıkları da kapsadığı ve uyarılar hata sayıldığı için, yeni yayımlanan bir NuGet tavsiyesi ilgisiz PR'ların restore/derleme adımını kırabilir; takip görevinin bunu CI'dan önce yakalayıp düzeltme PR'ını açması "fresh CI" kapısındaki beklenmedik kırmızıları azaltır.
- **npm tarafında asıl risk kötü amaçlı paketlerdir** (kötü amaçlı paket tavsiyelerinin büyük çoğunluğu npm): sürüm yaşı bekletmesi (cooldown), kilit dosyası ve az sayıda doğrudan bağımlılık (bugün 4) en etkili önlemlerdir; güvenlik güncellemeleri beklemeyi atlar.
- **.NET tarafında erişilebilirlik analizi yok:** C# için "çağrı yolu var mı" sorusunu AI triyajı ya da insan yanıtlamalı; AI'ın "etkilemiyor" hükmü tek başına kapatma gerekçesi olmamalı.
- **Önerilen öncelik sırası (D; eşikler ayrı karar):** KEV'de olan ve bizde erişilebilir → en kısa sürede; düzeltmesi yayımlanmış kritik/yüksek ve çalışma zamanında kullanılan → günler içinde; geliştirme kapsamlı ve düşük etkili → aylık toplu güncelleme.
- **Acil durum adımları (AI-D18) LLM tarafından değil deterministik kuralla tetiklenmeli (D):** örn. oturum açma denemesi eşiği → geçici kısıtlama; AI yalnız açıklar, bildirir ve sonradan gözden geçirme taslağı hazırlar.

### Gaps

- **Bağımsız yanlış-pozitif oranı** (bağımlılık uyarılarının ne kadarının gerçekte etkilemediği) ve **EPSS/KEV'in ayırt edicilik istatistikleri** bu oturumda kaynaklanamadı.
- **USOM bildirimlerinin makinece okunur beslemesi, 7545 sayılı Kanun'un bu şirkete uygulanıp uygulanmadığı** doğrulanamadı (M).
- **SQL Server güvenlik güncellemeleri kanalı ve destek sonu tarihleri** (.NET 10, SQL Server sürümü, Node LTS) doğrulanmadı.
- **GitHub Advanced Security / Code Security lisans fiyatı** (ajana atama ve özel triyaj kuralları için gerekir) araştırılamadı.
- **Copilot kodlama ajanının güncel güvenlik sınırları** (belge yolu bulunamadı) doğrulanamadı.
- **Claude Security'nin bağımsız doğruluk ölçümü** yok; yalnız satıcı beyanı.

---

## 6. ERP'nin kendisini AI kodlama ajanlarıyla geliştirmek ve sürdürmek: yönetişim, kalite ve güvenlik kanıtı

### Takeaway

Kanıt iki yönlüdür: ajanlar üretimi hızlandırır, ama hakemli çalışmalar AI üretimi kodda yüksek güvenlik açığı oranı ve var olmayan paket önerme riski gösteriyor; satıcıların kendi inceleme araçları bile "hata kaçırabilir" ve "istem enjeksiyonuna karşı sertleştirilmemiştir" uyarısı taşıyor. Güvenli kullanımın ortak paydası — yazar ile birleştirenin ayrılığı, bağımsız inceleme, CI kapıları, en az yetki, sırların ajan ortamından uzak tutulması ve insan kapıları — bu depodaki mevcut kurallarla aynı çizgidedir.

### Cited Findings

- OpenSSF kılavuzunun (1 Ağu 2025) derlediği hakemli bulgular: Pearce ve ark. (2021) — 89 senaryoda üretilen 1.689 programın "approximately 40%"ı açıklı; Perry ve ark. (2022) — AI yardımcısı kullananlar SQL enjeksiyonuna açık çözümü belirgin biçimde daha sık yazdı (yaklaşık %36'ya karşı %7); Spracklen ve ark. (2024) — önerilen paketlerin %19,7'si gerçekte yok ("slopsquatting" saldırısına kapı açar); Vero ve ark. (2025) — işlevsel olarak doğru programların yaklaşık yarısında istismar çalıştırılabildi. A (OpenSSF aktarımıyla) [SAYFA] — [OpenSSF: Security-Focused Guide for AI Code Assistant Instructions](https://github.com/ossf/wg-best-practices-os-developers/blob/main/docs/Security-Focused-Guide-for-AI-Code-Assistant-Instructions.md)
- Aynı kılavuzun önerileri: geliştirici denetimde kalır ("Critically evaluate and edit AI-generated code"); AI kodu inceleme, test, statik analiz ve sürüm denetiminin yerine geçmez; sırlar koda yazılmaz; "Do not add dependencies that may be malicious or hallucinated"; sürümler sabitlenir; güvenlik açısından kritik işlevler için olumsuz testler; "güvenlik uzmanı gibi davran" türü rol istemleri önerilmez (bazı deneylerde daha kötü sonuç). B [SAYFA] — [aynı kılavuz](https://github.com/ossf/wg-best-practices-os-developers/blob/main/docs/Security-Focused-Guide-for-AI-Code-Assistant-Instructions.md)
- Anthropic'in otomatik PR incelemesi (Team/Enterprise): birkaç ajan değişikliği okur, doğrulama adımı yanlış pozitifleri eler; ancak "A review never approves or blocks the pull request, so the decision to merge stays with your team" ve "may miss bugs, security vulnerabilities, or other issues. A review with no findings doesn't mean your code is error-free or secure"; inceleme talimat dosyası (`REVIEW.md`) temel daldan okunur, "never from the pull request's own changes". C [İDDİA] [SAYFA] — [Claude Code: Code Review](https://code.claude.com/docs/en/code-review)
- Claude Code güvenlik belgesi: "You're responsible for reviewing proposed code and commands for safety before approval"; "no system is completely immune to all attacks"; bulut oturumlarında GitHub kimlik bilgileri sanal makineye girmez, vekil dal silmeyi ve dal dışı itmeleri reddeder, hangi dalların güncellenebileceğini deponun dal koruma kuralları belirler; kurumlar yönetilen ayarlarla izinleri zorunlu kılabilir. C [İDDİA] [SAYFA] — [Claude Code: Security](https://code.claude.com/docs/en/security)
- Satıcı beyanı: Temmuz 2026 itibarıyla Anthropic'te PR'a gönderilen kodun %50'sinden fazlası iç bir ajan sistemi tarafından yazılmıştı; aynı yazı ajan eylemlerinin insan eylemlerinden ayırt edilebilir biçimde kaydedilmesini ve ajan ortamında "çalınmaya değer" kimlik bilgisi bulunmamasını öneriyor. C [İDDİA] [SAYFA] — [CISO's guide to agentic AI](https://claude.com/blog/ciso-guide-to-agentic-ai)
- Güvenlik inceleme eylemi yalnız güvenilir PR'larda çalıştırılmalı (istem enjeksiyonu uyarısı). C [İDDİA] [SAYFA] — [anthropics/claude-code-security-review](https://github.com/anthropics/claude-code-security-review)
- 2026'da geliştirme araçlarının kendisi de hedef oldu: Cursor'da MCP/zehirli arama üzerinden uzaktan kod çalıştırma açıkları (ikincil kaynak; teyit edilmedi) ve yedi MCP istemcisinde araç zehirleme analizi. B [ÖZET] — [Hive Security blog](https://hivesecurity.gitlab.io/blog/prompt-injection-attack-detect-2026/), [arXiv 2603.21642](https://arxiv.org/html/2603.21642v1)
- [BELLEK] A/B (doğrulanamadı): METR rastgele kontrollü çalışması (Temmuz 2025) — deneyimli açık kaynak geliştiriciler AI araçlarıyla ölçümde %19 daha yavaş çalıştı, kendileri hızlandıklarını sanıyordu ([metr.org](https://metr.org/blog/2025-07-10-early-2025-ai-experienced-os-dev-study/)); Veracode 2025 raporu — AI üretimi kod örneklerinin %45'inde bilinen açık sınıfları ([veracode.com](https://www.veracode.com/blog/genai-code-security-report/)); DORA 2025 — AI kullanımı teslim hızını artırırken kararsızlığı da artırabiliyor ([dora.dev](https://dora.dev/research/2025/dora-report/)); Temmuz 2025'te bir kodlama ajanının kod dondurma sırasında üretim veritabanını sildiği olay basında yer aldı.

### Inferences

- **Mevcut yönetişim kanıtla uyumlu (D):** yazar ≠ birleştiren, exact-head bağımsız inceleme, K3'te iki inceleme, taze zorunlu CI, dağıtım/sır/üretim aktivasyonunun insanda kalması — kaynakların önerdiği denetimlerin karşılığıdır. Eklenebilecek dersler:
  1. **Talimat dosyaları saldırı yüzeyidir:** `AGENTS.md`, `.claude/**`, skill ve inceleme talimatları yalnız temel daldan okunmalı; PR'ın kendi değiştirdiği talimatla o PR incelenmemeli (Anthropic'in `REVIEW.md` tasarımı bunu yapıyor).
  2. **Ajanı tetikleyebilen girdiler güvenilir aktörlerle sınırlı olmalı:** issue/PR yorumu, dış katkı, web içeriği istem enjeksiyonu taşıyabilir; ajan iş akışları yalnız yazma yetkili insanlarca tetiklenmeli.
  3. **Bağımlılık ekleyen her ajan PR'ında** paket adının gerçekten var ve doğru olduğu (uydurma paket riski %19,7), sürümün yeni yayımlanmamış olduğu ve kilit dosyasının güncellendiği ayrıca kontrol edilmeli.
  4. **Aynı ajanın yazdığı kod ve test aynı yanlış anlamayı paylaşabilir:** kabul ölçütü ve formül golden'larının ayrı şeritten gelmesi (mevcut uygulama) bu riski azaltır; para/stok/yetki yollarında olumsuz ve sınır testleri bağımsız incelenmeli.
  5. **Ajan ortamında sır yok:** uzun ömürlü anahtar yerine kısa ömürlü, dar kapsamlı kimlik; üretim kimlik bilgileri ajan döngüsünün çalıştığı ortama hiç girmemeli.
  6. **Ölçüm:** ajan PR'larında inceleme bulgusu sayısı, CI kırmızı oranı, birleşme sonrası düzeltme oranı ve geri alma sayısı izlenmeli; hız tek ölçüt olmamalı.

### Gaps

- **2026 tarihli, güncel modellerle yapılmış bağımsız kalite/güvenlik kıyaslaması** bu oturumda bulunamadı; eldeki hakemli sayılar 2021–2025 modellerine aittir ve bugünkü ajanlar için üst sınır değil, uyarı olarak okunmalı.
- **Ajanlarla geliştirilmiş ERP ölçeğinde sistemlerin uzun dönem bakım maliyeti** hakkında kanıt bulunamadı.
- **METR, Veracode, DORA rakamları ve üretim veritabanı silme olayı** yalnız bellekten; teyit edilmeli.

---

## 7. Yap ya da satın al: "sektörel işler için genel sohbet" ERP içinde mi olsun, dış abonelik + bağlayıcı mı?

### Takeaway

Değişken maliyet belirleyici değildir: benim varsayımlarımla ERP içinde API tabanlı genel sohbet aktif kullanıcı başına ayda yaklaşık 2–15 USD, iş planı aboneliği 14–25 USD tutar. Belirleyici olan denetim ve gizlilik (ERP içi çözüm kazanır) ile özellik genişliği ve bakım yüküdür (dış abonelik kazanır). Öneri karmadır: ERP içinde ince, denetimli bir genel sohbet modu (yalnız dış sağlayıcı kararı verilirse) + seçili kullanıcılara iş planı dış abonelik (ERP verisi olmadan) + ayrı bir kararla, yalnız hassas olmayan veriye açılan salt-okunur ERP bağlayıcısı pilotu.

### Cited Findings

- **API birim fiyatları (22.11.2026):** Claude Sonnet 5.5 $2 giriş / $10 çıkış, Claude Opus 5.5 $4 / $20, Claude Haiku 5.5 $0.10 / $0.50 (milyon token); web araması $10 / 1.000 arama; arama sonuçları giriş token'ı olarak ayrıca sayılır; web fetch ek ücretsiz; önbellekten okuma Sonnet 5.5 ve Opus 5.5'te giriş fiyatının 0,05 katı; Claude 4.7 ve sonrası modellerin belirteçleyicisi aynı metin için "approximately 30% more tokens" üretir. C [İDDİA] [SAYFA] — [Pricing](https://platform.claude.com/docs/en/about-claude/pricing)
- **Abonelik fiyatları:** Bölüm 1.6 tablosu (Claude Team $20–25 [SAYFA]; ChatGPT Business $20–25 [ÖZET]; Workspace Business Standard $14–16,80 [ÖZET]; Copilot Business $18–25,20 [SAYFA]; Perplexity Enterprise Pro $40 [ÖZET]).
- **Gizlilik sınırı:** ZDR yalnız API (ve Claude Code) için, satış ekibiyle; Team/Enterprise sohbet arayüzleri ZDR'ye uygun değil; MCP connector, code execution, Files API, Managed Agents ZDR dışı; Fable/Mythos modelleri 30 gün saklama ister. C [İDDİA] [SAYFA] — [API and data retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)
- **API'de web araçlarının denetimi:** web araması ve web fetch için alan adı izin/engel listesi ve çağrı sayısı sınırı verilebilir; web fetch yalnız konuşmada zaten geçen adresleri getirir (modelin kendi kurduğu adrese veri taşıması zorlaşır). C [İDDİA] [SAYFA: skill içeriği okundu] — `claude-api` bilgi kaynağı (önbellek 2026-11-03); özellik sayfası [Web fetch tool](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-fetch-tool)
- **API'de zamanlama ve onay:** Managed Agents zamanlı dağıtımlarında tetikleme anında bağlı bir istemci yoktur; onay bekleyen araç çağrısı (MCP araçlarında varsayılan "always_ask") koşuyu bekletir. C [İDDİA] [SAYFA: skill içeriği okundu] — `claude-api` bilgi kaynağı (önbellek 2026-11-03); [Managed Agents overview](https://platform.claude.com/docs/en/managed-agents/overview)
- **Dış ürünlerin özellik hızı:** 2026 sürüm notlarında hemen her ay birden çok yeni yetenek var (zamanlı görevler Şubat, Cowork genel kullanım Nisan, Slack'te görev devri Haziran, M365 yazma araçları Temmuz, toplantı katılımı ve computer use Kasım). C [İDDİA] [SAYFA] — [Release notes](https://support.claude.com/en/articles/12138966-release-notes)
- **Bağlayıcının ağ ve kimlik şartları:** Bölüm 2.1–2.3 (genel internetten erişilebilir uç, OAuth, kurum çapı araç politikası; tünel claude.ai için yok). C [İDDİA] [SAYFA] — [Custom connectors](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp), [MCP tunnels](https://platform.claude.com/docs/en/agents-and-tools/mcp-tunnels/overview)

### Inferences

**7.1 Maliyet modeli — varsayımlar benimdir, ölçüm değildir (D)**

Varsayılan token hacimleri: aramasız bir sohbet turu 6.000 giriş + 800 çıkış token'ı; aramalı tur 2 arama + 20.000 giriş + 1.000 çıkış; bir "derin araştırma" koşusu 30 arama + 400.000 giriş + 8.000 çıkış. Liste fiyatlarıyla:

| Birim | Haiku 5.5 | Sonnet 5.5 | Opus 5.5 |
|---|---|---|---|
| Aramasız tur | ≈ $0,001 | ≈ $0,02 | ≈ $0,04 |
| Aramalı tur (2 arama) | ≈ $0,023 | ≈ $0,07 | ≈ $0,12 |
| Derin araştırma koşusu | — | ≈ $1,2 | ≈ $2,1 |

Aylık örnek (20 iş günü × 15 tur = 300 tur, %30'u aramalı, ayda 4 derin araştırma): Sonnet 5.5 ile ≈ $10,5 + $4,7 ≈ **$15/kullanıcı/ay**; sohbet Haiku 5.5, araştırma Sonnet 5.5 ile ≈ $2,2 + $4,7 ≈ **$7/kullanıcı/ay**; hafif kullanıcı (günde 5 tur) bunun yaklaşık üçte biri. Önbellekleme girişi ucuzlatır; uzun konuşmalar ve dosya yüklemeleri pahalılaştırır. Arama ücreti küçük modellerde maliyetin çoğunu oluşturur. Sonuç: 30 kullanıcıda (varsayım) API ≈ $200–450/ay, abonelik ≈ $600/ay — aynı büyüklük mertebesi.

**7.2 Karşılaştırma**

| Ölçüt | A) ERP içinde API tabanlı genel sohbet (web aramalı) | B) Dış iş planı aboneliği (+ isteğe bağlı ERP bağlayıcısı) |
|---|---|---|
| Değişken maliyet | Kullanıcı başına ≈ $2–15/ay (varsayım) | $14–25/koltuk/ay liste (Perplexity $40) |
| Sabit maliyet | Yüksek: sohbet arayüzü, geçmiş, dosya ayrıştırma, alıntı gösterimi, zamanlayıcı, kota ve maliyet tavanı, değerlendirme seti, sürekli bakım | Düşük: yönetim ve eğitim; bağlayıcı yapılırsa o da geliştirme işi |
| Denetim | Tam: sunucu tarafı süzgeç, veri sınıfı izni, takma ad, her adım denetim kaydında, mod ayrımı | Sağlayıcının yönetici ayarlarıyla sınırlı; denetim kaydı ve Compliance API yalnız Enterprise'da |
| Gizlilik | Hangi verinin çıktığını sunucu belirler; sözleşmeyle ZDR mümkün (model/özellik kısıtlı) | Kullanıcı ne yapıştırırsa o çıkar; arayüzler ZDR değil (konuşmalar silinene kadar durur; Enterprise'da en az 30 günlük özel süre) |
| Hassas ERP verisiyle soru | Uygun: takma ad dönüşte ERP'de çözülür | Uygun değil: takma ad dış ekranda çözülemez |
| Kullanıcı deneyimi | Tek ekran, ERP bağlamı; özellik dar | Geniş: derin araştırma, dosya üretimi, Office eklentileri, mobil, ses, toplantı notu |
| Özellik hızı ve bakım | Model/araç sürüm değişiklikleri bize ait | Sağlayıcıda; aylık yeni yetenek |
| Zamanlı görev | ERP verisi için doğal (AI-18) | Dış araştırma/izleme için hazır |
| KVKK | Kişisel veri süzgeci teknik olarak zorlanabilir | Kural ve eğitimle; teknik zorlama zayıf |
| Yerel-önce ilkesi | Yerel modelle de çalışabilir (kalite ve web araması ayrı konu) | Tanım gereği bulut |

**7.3 Öneri (açık varsayımlarla)**

Varsayımlar: (V1) kullanıcı sayısı onlarla ölçülür, yüzlerle değil; (V2) dış asistanlara kişisel ve "çok gizli" veri hiç girmeyecek (mevcut karar); (V3) ERP içinde bulut sağlayıcı açmak hâlâ açık bir sahip kararıdır (OPEN-020) — açılmazsa A seçeneği yalnız yerel modelle, web araması olmadan ya da çok sınırlı kalır; (V4) geliştirme kapasitesi AI treninin çekirdek dilimlerine (araçlar, onay kutusu, belgeden taslak) öncelik verir; (V5) fiyatlar liste fiyatıdır.

1. **Hemen (ERP kararlarından bağımsız; yalnız bütçe onayı gerekir):** personel AI politikası + 5–10 kişilik iş planı dış abonelik pilotu (ofis paketine uyan tek ürün); ERP verisi yok. Sektörel araştırma, çeviri, yazışma, tablo ve zamanlı izleme burada yapılır. ERP içinde bunların benzerini inşa etmeye çalışmayın.
2. **AI treninde (AI-11 / AI-17):** ERP içi "genel sohbet modu" ince tutulur — ağ geçidi üzerinden model çağrısı + alan adı listeli web araması + kaynak gösterimi; dosya üretimi, derin araştırma, ses yok. **Mod ayrımı zorunlu:** genel modda ERP araçları kapalı, ERP modunda web kapalı; ikisini birleştiren iş ("şirket verisiyle birleştirme") sunucunun denetlediği iki adımlı akışla ve ERP verisi okunduktan sonra modelin serbest web isteği yapamayacağı biçimde.
3. **Ayrı kararla (OPEN-020 + AI-D3/D4):** salt-okunur ERP bağlayıcısı pilotu — 3–5 araç, yalnız "Genel/Şirket içi" sınıfı ve toplulaştırılmış veri (ör. sipariş durumu sayıları, ürün kataloğu, kural/karar metinleri); kullanıcı başına OAuth; kurum politikasında yazma "Blocked"; bağlayıcının açık olduğu çalışma alanında web araması ve e-posta bağlayıcısı kapalı.
4. **Değerlendirme:** 2–3 ay sonra kullanım, kabul oranı, olay sayısı ve maliyetle karar: bağlayıcı genişlesin mi, ERP içi genel mod yeterli mi, abonelik sayısı artsın mı.

- **Zamanlı dış e-posta ve yanıt raporu (AI-D17) ERP içinde kalmalı (D):** gönderim deterministik şablon + toplu onay + denetim kaydıyla; AI yalnız taslak ve yanıt sınıflandırma yapar. Gelen yanıtlar güvenilmeyen içeriktir — onları okuyan model adımının araç ve dışa çıkış yetkisi olmamalı. Dış asistanlarla otomatik toplu gönderim hem onay bekleyen araçlarda takılır hem de denetim izini ERP dışına taşır.
- **Güvenlik/teknoloji takibi (AI-D18) büyük ölçüde "satın al/yapılandır" işidir:** kod deposu araçlarıyla (Bölüm 5); ERP çalışma zamanına yalnız sistemin kendi güvenlik olayları ve deterministik acil kurallar düşer.

### Gaps

- **Gerçek kullanım verisi yok:** token hacimleri ve tur sayıları varsayımdır; pilot ölçümüyle değiştirilmeli.
- **Geliştirme ve bakım iş gücü maliyeti** sayısallaştırılmadı (depo ajanlarla geliştirildiği için insan-gün karşılığı belirsiz).
- **Yerel modelle genel sohbet + web araması** seçeneğinin kalitesi ve maliyeti (donanım, arama kaynağı) araştırılmadı (OPEN-014).
- **OpenAI ve Google API'lerinin web arama fiyatları** karşılaştırma için alınamadı; AI-D11'deki "faydaya göre model seçimi" için gerekli.
- **ChatGPT developer mode bağlayıcısının ağ/IP şartları** doğrulanamadı.

---

## 8. KOBİ için personel yapay zekâ kullanım politikası — kısa taslak

### Takeaway

Politika üç soruya kısa ve somut yanıt vermeli: hangi hesap ve araç serbest, neyi yapıştırmak yasak, çıktının sorumlusu kim. Teknik zorlama iş planlarında sınırlı olduğu için (kişisel hesapların ağ düzeyinde engellenmesi yalnız Enterprise'da) politikanın gücü kısa eğitim, örnek listeler ve yönetici takibine dayanır.

### Cited Findings

- Tüketici hesaplarında veri eğitimde kullanılabilir ve 5 yıla kadar saklanabilir; ticari planlarda varsayılan olarak eğitim yok (Claude Code/API için belgelenen standart saklama 30 gün) — "yalnız iş hesabı" kuralının dayanağı. C [İDDİA] [SAYFA] — [Claude Code data usage](https://code.claude.com/docs/en/data-usage)
- Kişisel ajanlarla veri sızıntısı satıcının kendi güvenlik liderine göre "the most likely threat vector for agentic systems"; önerilen denetimler: bağlayıcı izin listesi, eylem bazında onay, kurum çapı kapatma anahtarı, küçük grupla başlayıp telemetriyle genişletme. C [İDDİA] [SAYFA] — [CISO's guide to agentic AI](https://claude.com/blog/ciso-guide-to-agentic-ai)
- Tarayıcı ajanı için satıcı önerisi: finans hesapları, hukuki belgeler, sağlık verisi ve hassas şirket verisi içeren iş hesaplarında kullanmayın; yüksek riskli eylemler onay ister; risk "is not zero". C [İDDİA] [SAYFA] — [Use Claude in Chrome safely](https://support.claude.com/en/articles/12902428-use-claude-in-chrome-safely)
- Kurum sahibi araç bazında Always allow / Needs approval / Blocked belirleyebilir ve kullanıcı bunu aşamaz; Team'de üyeler bağlayıcı talep edebilir. C [İDDİA] [SAYFA] — [Use connectors to extend Claude's capabilities](https://support.claude.com/en/articles/11176164-use-connectors-to-extend-claude-s-capabilities)
- Özel bağlayıcılar satıcı tarafından doğrulanmaz; yalnız güvenilen kuruluşların sunucularına bağlanılmalı. C [İDDİA] [SAYFA] — [Custom connectors](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp)
- İstem enjeksiyonu için kesin önleme yok; yüksek riskli eylemde insan onayı ve en az yetki önerilir. B [SAYFA] — [OWASP LLM01:2025](https://github.com/OWASP/www-project-top-10-for-large-language-model-applications/blob/main/2_0_vulns/LLM01_PromptInjection.md)
- Ağ düzeyinde kiracı kısıtı (kurum ağından kişisel/kurum dışı hesapların engellenmesi) ve IP izin listesi Enterprise özelliğidir. C [İDDİA] [SAYFA] — [Claude Enterprise plan](https://claude.com/solutions/enterprise)
- AI çıktısının doğruluk sınırı için bağımsız çalışmalar: Bölüm 4.3 ([BELLEK]).

### Inferences

**Politika taslağı (D) — bir sayfalık metne çevrilebilir:**

1. **Amaç ve kapsam:** şirket işi için kullanılan her üretken yapay zekâ aracı (sohbet, eklenti, tarayıcı uzantısı, toplantı botu, kodlama ajanı); ERP içi asistan ayrı kurallara (AI-D kararları) tabidir.
2. **Onaylı araçlar ve hesaplar:** yalnız şirketin sağladığı iş planı hesabı, kurumsal e-postayla; kişisel (ücretsiz/bireysel) hesapla şirket işi yapılmaz; yeni araç, uzantı veya bağlayıcı yönetici onayıyla.
3. **Asla girilmeyecekler:** kişisel veri (ad-soyad + iletişim, kimlik no, IBAN, özlük, sağlık), parola/anahtar/erişim belirteci, "çok gizli" belgeler, müşteri veya tedarikçi kimliğiyle birlikte fiyat/bakiye/limit, ERP ekran görüntüsü ve dışa aktarım dosyaları, başkasına ait sözleşmenin tamamı.
4. **Girilebilecekler:** kamuya açık bilgi, genel soru, kimliksizleştirilmiş/elle takma adlı örnek, kendi yazdığınız taslak, "Genel/Şirket içi" sınıfı belge (kişisel alan içermiyorsa).
5. **Doğrulama:** rakam, tarih, mevzuat, madde numarası ve alıntı için kaynak açılıp bakılır; kaynak yoksa kullanılmaz; hesap tabloda/ERP'de yeniden yapılır; hukuki ve mali sonuç doğuran metin ilgili uzmana gider.
6. **Sorumluluk:** çıktıyı kullanan, gönderen ya da imzalayan kişi sorumludur; "yapay zekâ böyle dedi" savunma değildir; dışarı giden yazışma insan tarafından okunmadan gönderilmez.
7. **Bağlayıcılar ve ajanlar:** şirket e-postası, sürücüsü ve ERP'ye bağlayıcıyı yalnız yönetici açar; yazma/gönderme eylemleri onaylı; tarayıcı ve bilgisayar kullanan ajanlar banka, e-devlet, vergi ve ERP yönetim ekranlarında kullanılmaz.
8. **Zamanlı görevler:** yalnız okuma-özetleme; dış alıcıya otomatik gönderim yok; görev sahibi belirli ve aylık gözden geçirilir.
9. **Toplantı kaydı ve notu:** katılımcılar önceden bilgilendirilir; dış taraflı toplantıda açık onay; döküm saklama süresi belirlenir (M — hukuki teyit).
10. **Olay bildirimi:** yanlışlıkla yasak veri girildiyse ya da şüpheli çıktı/davranış görüldüyse aynı gün yöneticiye; sohbet silinir, gerekiyorsa sağlayıcıdan silme istenir.
11. **Eğitim ve gözden geçirme:** işe başlarken 30 dakikalık eğitim + örnek "yapıştırma / yapıştırmama" listesi; politika altı ayda bir ve her yeni araçta güncellenir.
12. **Kod üretimi:** depo kuralları (bağımsız inceleme, CI, insan kapıları) geçerlidir; sır koda ve isteme yazılmaz.

- **Uygulama notu:** Team planında kişisel hesap kullanımı teknik olarak engellenemez; bu yüzden politika imzalı kabul, örnekli eğitim ve yönetici örneklemesiyle desteklenmeli. Enterprise'a geçiş (en az 20 koltuk, kullanım ayrıca faturalanır) yalnız denetim kaydı ve ağ kısıtı gerçekten gerekiyorsa anlamlıdır.

### Gaps

- **Türkiye'de KOBİ'ler için resmî ya da sektörel örnek AI kullanım politikası** aranamadı.
- **Çalışanların yapay zekâya veri yapıştırma sıklığına dair güncel bağımsız istatistik** bu oturumda kaynaklanamadı.
- **Toplantı kaydı ve çalışan izleme boyutunun iş hukuku/KVKK değerlendirmesi** yapılmadı (M).

---

## Bizim sisteme uyarlama

| # | Konu | Bulgunun özü (kanıt etiketi) | İlgili karar / dilim | Öneri | Kapı — kim karar verir |
|---|---|---|---|---|---|
| 1 | Sistem dışı genel/sektörel sohbet | Dış ürünler derin araştırma, dosya, Office, mobil, toplantı gibi geniş özellikleri aylık hızla çıkarıyor (C [SAYFA]); API maliyeti abonelikle aynı mertebede (D, varsayım) | AI-D15, AI-17; plan §6 "sistem dışı web araştırması sağlayıcısı" | Karma: ERP içinde ince genel mod (yalnız sohbet + alan listeli web araması + kaynak); ağır sektörel iş için iş planı dış abonelik | Dış sağlayıcıyı açmak OPEN-020 / AI-D6 — Sinan |
| 2 | Mod ayrımı (trifecta) | Özel veri + güvenilmeyen içerik + dışa çıkış birlikteyse sızıntı mümkün; kesin önleme yok (B [ÖZET], B [SAYFA]) | AI-D1 ("genel soru ERP verisi içermez"), AI-D3/D4 | Genel modda ERP araçları kapalı; ERP modunda web kapalı; birleştirme yalnız sunucu denetimli iki adımlı akışla; web fetch yalnız konuşmada geçen adreslere | Tasarım kararı; sınıfı (K1/K2/K3) AI treninde belirlenir, güvenlik incelemesi şart |
| 3 | Zamanlı görev ve araştırma takibi | Dört dış üründe hazır; hepsi okuma-özetleme için; onay isteyen araç gözetimsiz görevi durdurur (C [SAYFA]/[ÖZET]) | AI-D15, AI-18 | ERP verisi gerektiren görevler ERP içinde (AI-18, salt-okunur); dış izleme (fiyat, mevzuat, ihale) dış asistanın zamanlı görevinde; sonuç ERP'ye otomatik yazılmaz | Yeni izin (zamanlı görev) insan kapısı — Sinan |
| 4 | ERP bağlayıcısı (remote MCP) | Claude ve ChatGPT iş planlarında destekli; internetten erişilebilir uç + OAuth şart; kurum çapı araç politikası var; claude.ai için tünel yok (C [SAYFA]/[ÖZET]) | DEC-AI ("yalnız şemalı backend araçları"), AI-D3/D4, OPEN-014, OPEN-020 | Önce bağlayıcısız başla. Sonra pilot: 3–5 salt-okunur araç, yalnız "Genel/Şirket içi" sınıfı, kullanıcı başına OAuth + `CompanyId`, `ai.tool.called` denetimi, hız ve boyut sınırı, resmî C# SDK | Veriyi buluta açmak + içeri ağ yolu — Sinan; uygulama büyük olasılıkla K3 (yetki, modüller arası sözleşme) |
| 5 | Takma ad (AI-D4) ve dış arayüz | Eşleme ERP içinde kaldığı için dış ekranda çözülemez (çıkarım, D) | AI-D4 | Hassas veri soruları yalnız ERP içi asistanda; bağlayıcı hassas alan döndürmez | — |
| 6 | Sağlayıcı şartı "saklamaz ve eğitmez" | ZDR yalnız API'de ve sözleşmeyle; sohbet arayüzleri ZDR değil; Fable/Mythos modelleri 30 gün saklama ister; MCP connector, code execution, Files API, Managed Agents ZDR dışı (C [SAYFA]) | AI-D5, AI-D11 | Ağ geçidi model/özellik seçimini ZDR uygunluğuna göre kısıtlasın; AI-D5'in personel aboneliklerini kapsayıp kapsamadığı yazılı netleşsin | Sinan (AI-D5 kapsamı); sözleşme/attestation |
| 7 | KVKK | Kişisel veri göndermek yurt dışına aktarım; standart sözleşme + 5 iş günü bildirim; açık rıza yalnız arızi hâlde (M [ÖZET]/[BELLEK]) | AI-D3/D4 ("kişisel veri hiç dışarı çıkmaz"), OPEN-020 | Dış asistana kişisel veri girmeme kuralı politika + eğitimle; ERP ağ geçidinde teknik süzgeç; hukuk danışmanından yazılı teyit | Hukuk danışmanı + Sinan |
| 8 | Zamanlı dış e-posta + yanıt raporu | Dış asistanla gözetimsiz gönderim güvenilir değil; gelen yanıt güvenilmeyen içerik (C [SAYFA], B [SAYFA]) | AI-D17, AI-22 | ERP içinde kur: deterministik şablon + toplu onay + denetim; yanıtı okuyan model adımı araçsız ve çıkışsız; rapor üst kullanıcıya | E-posta hesabı/sırrı ve otomatik gönderim izni — Sinan |
| 9 | Güvenlik ve teknoloji takibi | GitHub uyarıyı ajana atayıp taslak PR açtırabiliyor; erişilebilirlik analizi yalnız JS/TS; depoda CI denetimi var, sürekli izleme/otomatik PR yapılandırması yok (C [SAYFA]; depo kontrolü) | AI-D18, AI-23 | Takibi kod deposu araçlarında kur: Dependabot uyarıları + güvenlik güncellemeleri, haftalık AI özeti (zamanlı iş akışı), ajan taslak PR → normal review/CI; sürüm yaşı bekletmesi | `.github/**` K3 (iki review); Actions'ta etkinleştirme, lisans, sır — Sinan |
| 10 | Önceden onaylı acil adımlar | İstem enjeksiyonu ve stokastik tarama nedeniyle LLM tetikleyici olmamalı (B [SAYFA], C [SAYFA]) | AI-D18 | Acil adımı deterministik kural tetikler; AI açıklar, bildirir, sonradan inceleme taslağı yazar | Adım listesi — Sinan |
| 11 | Fiyat ve kur izleme | Fiziki fiyat kaynakları ücretli; kur resmî ve makinece okunur ([BELLEK]); AI yanıtlarında yüksek hata oranı ([BELLEK] B) | Plan §4 "sektörel sohbet ve araştırma" | Kur ERP'ye deterministik beslemeyle; AI özeti yalnız yorum ve bağlantı; teklif fiyatına doğrudan girdi değil | Ücretli veri aboneliği — Sinan |
| 12 | Değerlendirme seti | Halüsinasyon riski rakam ve mevzuatta yoğun ([BELLEK] A/B) | AI-D11, AI-27 | Sete "uydurma fiyat/madde" ve "kaynak göstermeden rakam" testleri ekle; genel modda kaynaksız rakam yanıtı başarısız sayılır | AI treni |
| 13 | Personel politikası | Tüketici hesabında eğitim + 5 yıl saklama riski; Team'de kişisel hesap teknik olarak engellenemez (C [SAYFA]) | Yeni (karar yok) | Bölüm 8 taslağını bir sayfalık politika + 30 dakikalık eğitim olarak yayımla | Sinan / İK |
| 14 | Ajanlarla geliştirme yönetişimi | AI kodunda yüksek açık oranı ve uydurma paket riski (A [SAYFA]); inceleme araçları hata kaçırabilir (C [SAYFA]) | AGENTS.md, GOV-D* | Mevcut kapıları koru; ek: talimat dosyaları temel daldan, ajan tetikleyicileri güvenilir aktörle sınırlı, bağımlılık ekleyen PR'da paket doğrulama, ajan ortamında sır yok | `docs/governance/**` değişikliği iki review |
| 15 | Tek iş kutusu ile ilişki | Dış asistan çıktılarının ERP'ye dönüş yolu tanımsız (boşluk) | AI-D20, AI-26 | İlk aşamada elle: kullanıcı özeti Work kutusuna not/görev olarak ekler; otomatik içeri aktarım yok | AI treni |

## Önerilmeyenler / fazlalık

- **ERP içinde tam bir "ChatGPT klonu" kurmak** (derin araştırma, dosya ve sunum üretimi, Office eklentileri, sesli mod, toplantı notu): dış ürünler bunu aylık hızla geliştiriyor; değişken maliyet avantajı küçük, bakım yükü büyük.
- **Tüketici (ücretsiz/bireysel) hesaplarla şirket işi:** eğitimde kullanım ve 5 yıla varan saklama riski.
- **Aynı kişiye birden çok asistan aboneliği** (Claude + ChatGPT + Copilot + Perplexity): maliyet ve veri dağınıklığı; tek ana ürün + ofis paketine gömülü olan yeterli. Perplexity Enterprise Max ($325/koltuk/ay) bu ölçekte gerekçesiz.
- **ERP bağlayıcısında yazma araçları, genel sorgu/SQL ya da "her şeyi yapan" tek araç:** hem depo değişmezine hem bağlayıcı inceleme ölçütlerine aykırı.
- **ERP bağlayıcısını web araması, e-posta ve başka bağlayıcılarla aynı çalışma alanında serbest bırakmak:** lethal trifecta'nın üç ayağını birleştirir.
- **Kuruma tek paylaşılan API anahtarıyla bağlayıcı:** herkes aynı yetkiyle erişir; kullanıcı kimliği ve `CompanyId` süzgeci devre dışı kalır.
- **claude.ai bağlayıcısı için MCP tüneline güvenmek:** desteklenmiyor; ayrıca araştırma önizlemesi, süreklilik taahhüdü yok.
- **ZDR şartı varken Fable/Mythos sınıfı modelleri ya da durumlu API özelliklerini (code execution, Files API, MCP connector, Managed Agents) seçmek.**
- **Fiyat, kur, mevzuat rakamını asistanın hafızasından almak; kaynak bağlantısı ve tarihi olmayan rakamı kullanmak.**
- **Dış asistanla onaysız toplu e-posta gönderimi** ve gelen yanıtları araç yetkisi olan bir ajana okutmak.
- **Tarayıcı / bilgisayar kullanan ajanları banka, e-devlet, vergi, ERP yönetim ekranlarında çalıştırmak** (satıcının kendi önerisi de bu yönde).
- **Güvenlik takibini ERP çalışma zamanına gömmek** ve acil adımları LLM'e tetikletmek: kod deposu araçları ve deterministik kurallar daha ucuz ve denetlenebilir.
- **"Erişilemez" sonucu ya da düşük EPSS nedeniyle uyarıyı kapatmak; "kritik" etiketi nedeniyle etkilenmeyen bileşen için acil çalışma başlatmak** — ikisi de gerekçesiz triyajdır.
- **Enterprise planına erken geçiş:** en az 20 koltuk ve kullanım ayrıca faturalanır; denetim kaydı, SCIM ya da ağ kısıtı gerçekten gerekmedikçe Team yeterli.
- **Toplantı kayıt botlarını bilgilendirme ve onay olmadan kullanmak.**
- **Satıcı vaka rakamlarını ("40 saatten 2 dakikaya" gibi) fayda hesabına girdi yapmak:** bağımsız doğrulama yok.

## Sinan'a açık sorular

1. **Ofis paketi:** şirket e-postası ve belgeleri Microsoft 365'te mi, Google Workspace'te mi, başka bir yerde mi? (Hangi asistanın en ucuz ve en kullanışlı olacağını bu belirler.)
2. **Kim, kaç kişi:** dış asistan aboneliği kimlere verilsin (yalnız yöneticiler mi, bütün ofis personeli mi) ve aylık bütçe tavanı ne olsun?
3. **"Saklamayan sağlayıcı" şartı (AI-D5):** bu şart personelin kullanacağı dış sohbet abonelikleri için de geçerli mi, yoksa yalnız ERP'nin kendi sağlayıcı bağlantısı için mi? (Sohbet aboneliklerinde konuşmalar silinene kadar saklanır; sıfır saklama yalnız API'de ve sözleşmeyle var.)
4. **ERP verisinin dış asistana açılması:** bir bağlayıcıyla ERP'den salt-okunur, hassas olmayan veri (ör. sipariş durumu, ürün kataloğu, kural metinleri) dış asistana verilsin mi, yoksa ERP verisi yalnız ERP içi asistanda mı kalsın?
5. **Genel sohbet nerede olsun:** "sistem dışı sohbet" ERP ekranının içinde de bulunsun mu (dış sağlayıcı açılmasını gerektirir), yoksa bu iş tamamen dış aboneliklere mi bırakılsın? Önerimiz ikisi birden, ERP içindeki ince tutularak.
6. **Dış sağlayıcı kararı (OPEN-020):** ERP içinde bulut modeli açılacaksa hangi koşulla (sözleşmeli sıfır saklama, yalnız hassas olmayan veri, yalnız genel sohbet modu)? Daha güçlü ama 30 gün saklama zorunlu olan model sınıfı kabul edilir mi?
7. **KVKK teyidi:** "dış asistana kişisel veri hiç girmezse standart sözleşme ve bildirim gerekmez" varsayımını ve takma adlı verinin durumunu hukuk danışmanı yazılı teyit etsin mi?
8. **Güvenlik takibi:** GitHub'da otomatik güvenlik uyarıları ve güncelleme PR'ları ile bir AI ajanının taslak düzeltme PR'ı açması etkinleştirilsin mi? (Kalite kapısı değişikliği iki inceleme ister; etkinleştirme ve olası lisans ücreti sizin kararınız.) Ücretli bir güvenlik tarama ürünü değerlendirilsin mi?
9. **Yama süreleri ve acil adımlar:** "aktif istismar edilen açık kaç gün içinde kapatılır" gibi hedef süreler ve önceden onaylı acil adımların listesi (ör. saldırı sırasında geçici giriş kısıtlaması) ne olsun?
10. **Zamanlı dış e-posta:** hangi e-posta altyapısı ve gönderen adresi kullanılacak; hangi şablon ve alıcı grupları için ileride otomatik gönderim düşünülüyor; gelen yanıtlar ne kadar süre saklansın?
11. **Fiyat verisi:** ücretli bir çelik/hurda fiyat aboneliği var mı ya da alınacak mı? (Asistan ücretli kaynakları okuyamaz; yalnız açık ve gecikmeli veriyi özetler.)
12. **Toplantı notu:** toplantılar hangi platformda yapılıyor ve yapay zekâ ile not/kayıt kullanılacak mı; dış taraflı toplantılarda bilgilendirme nasıl yapılacak?
13. **Dış çıktının ERP'ye dönüşü:** dış asistanın hazırladığı araştırma ve özetler ERP'ye nasıl girsin — elle not/görev olarak mı, belge eki olarak mı, yoksa hiç mi?
14. **Politika sahibi:** personel yapay zekâ kullanım politikasını kim sahiplenecek, eğitimi kim verecek ve ihlalde ne yapılacak?
