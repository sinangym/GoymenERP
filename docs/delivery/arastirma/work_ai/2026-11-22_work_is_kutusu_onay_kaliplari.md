# Work iş kutusu ve onay kalıpları: tek kutu, vekâlet, eskalasyon, SLA, bildirim ve belgeye bağlı iletişim (22 Kasım 2026)

> **Yöntem ve sınırlar (önce okuyun).**
> - Aramalar yalnız genel terimlerle yapıldı; şirket adı, kişi adı ya da şirkete özgü veri kullanılmadı.
> - **Bütün dış bulgular arama özetlerine (snippet) dayanır.** Birincil sayfaların tam metni okunamadı: oturumun ağ izin listesi `learn.microsoft.com`, `blogs.sap.com`, `arxiv.org`, `www.ncbi.nlm.nih.gov` ve `pmc.ncbi.nlm.nih.gov` alan adlarını çözmedi; başka alan adı denenmedi. Kritik bir rakam uygulamaya girmeden birincil kaynaktan doğrulanmalıdır.
> - **Ortak arama bütçesi (tur başına 200 arama, tüm ajanlar paylaşır) araştırma bitmeden tükendi.** Aranamayan başlıklar her sorunun "Boşluklar" bölümünde tek tek yazılıdır.
> - Kanıt düzeyleri: **A** = hakemli çalışma ya da bağımsız denetim, **B** = analist ya da bağımsız kıyas, **C** = satıcı belgesi, vakası ya da iddiası (yarar iddiası ise **[İDDİA]**), **D** = spekülatif / bu notun kendi önerisi, **M** = mevzuat (teyit gerekir).
> - 2023 öncesi kaynaklar "eski" diye işaretlendi. Repo içi kaynaklar dosya yoluyla gösterildi.
> - Bu not bir **araştırma notudur, karar değildir.** Eşik, süre, oran gibi değerler parametre önerisidir; politika Sinan'ın kararıdır.

## 1. Ürünler tek iş kutusunu, toplu onayı, vekâleti, eskalasyonu, hatırlatmayı ve kayda bağlı işbirliğini nasıl uyguluyor?

### Çıkarım özeti (Takeaway)

Satıcılar aynı dar çekirdekte birleşiyor: (1) farklı kaynaklardan gelen işleri tek listede toplayan kutu, (2) süreli ve kapsamı seçilebilen vekâlet, (3) adım başına süre ve eskalasyon hedefi, (4) hatırlatma, (5) kayda bağlı not ve görev. Ayrıştıkları iki yer, yani e-postadan onay ve grafik iş akışı tasarımcısı, aynı zamanda en çok arıza kaydı üreten yerlerdir.

### Kaynaklı bulgular (Cited Findings)

**Tek kutu ve toplu işlem**

- SAP Task Center çok sağlayıcılı ("task provider") bir kutudur; vekâlet önce SAP Fieldglass için açıldı, diğer sağlayıcılar için "soon" denildi (Mayıs 2023) — [SAP blog](https://blogs.sap.com/2023/05/10/substitution-management-delegation-is-now-possible-in-sap-task-center/) (C)
- SAP My Inbox'ta çoklu seçimle birden çok fatura tek seferde onaylanabilir; toplu onay/red ayara bağlıdır, yanlış ayarda çoklu seçim görünmez; bir başka makaleye göre toplu onay yetkiyle engellenebilir — SAP bilgi bankası makaleleri, arama özeti; hangi makale olduğu doğrulanamadı, aday bağlantılar: [KBA 2424121](https://userapps.support.sap.com/sap/support/knowledge/en/2424121), [KBA 3130484](https://userapps.support.sap.com/sap/support/knowledge/en/3130484) (C)
- Dynamics 365 Finance: "Work items assigned to me" sayfasından birden çok iş kalemi toplu devredilebilir; yalnız bazı iş akışı türleri (satın alma siparişi, satın alma talebi, satıcı faturası) uygundur — [Microsoft Learn](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/fin-ops/organization-administration/tasks/delegate-work-items-workflow) (C)
- ServiceNow (Advanced Approval Management): onaycı çalışma alanından, mobil bildirimden, e-postadan ve "My Approvals" listesinden işlem yapabilir; birleştirilmiş e-postada yanıt metniyle tek ya da birden çok talep onaylanıp reddedilebilir — [ServiceNow belgeleri](https://www.servicenow.com/docs/r/order-management/sales-and-order-management/approving-approval-requests.html) (C)
- Odoo Approvals: yönetici "Approvals to Review" panosunda yalnız onaycısı olduğu talepleri görür; onay türleri kart olarak tanımlanır — [Odoo 19 belgeleri](https://www.odoo.com/documentation/19.0/applications/hr/approvals.html) (C)
- Odoo'da talep, tanımlı "en az onaycı" sayısına ulaşınca onaylanır; herkesin onayı isteniyorsa en az sayı toplam onaycı sayısına eşitlenir (Odoo 14 forum cevabı) — [Odoo forum](https://www.odoo.com/hi_IN/forum/help-1/odoo-approval-request-all-approvers-to-approve-190818) (C; eski)

**Bildirimden, e-postadan ya da mobilden onay**

- Salesforce: onaycı e-postayı "Approve / Approved / Yes" ya da "Reject / Rejected / Decline / No" sözcüğüyle yanıtlayarak karar verir; belirlenen alanın üstündeki metin yorum sayılır — [Trailhead](https://trailhead.salesforce.com/content/learn/modules/advanced-approvals-for-admins/configure-email-alerts-for-approvers-and-approval-requests) (C)
- Oracle BPM: bildirim e-postası "actionable" yapılabilir; ayrıntıyı dışarıda bırakan güvenli bildirim, hatırlatma ve durum değişikliği bildirimi ayrı ayarlardır — [Oracle BPM geliştirici kılavuzu](https://docs.oracle.com/en/middleware/fusion-middleware/bpm/12.2.1.4/bpm-develop/configuring-human-tasks.html) (C; içindekiler düzeyinde özet)
- NetSuite: hatırlatmalar Employee Center'da onay kuyruğuna iner; bildirim e-postasından kayda gidilir ve formdaki Approve/Reject düğmeleri kullanılır — [NetSuite yardım](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N910874.html), [SuiteFlow masraf onayı](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N2397278.html) (C)
- Power Automate / Outlook: e-posta içi onay (actionable message) konuk kullanıcılarda desteklenmez; eski Outlook Desktop sürümlerinde çalışmayabilir — [Microsoft: bilinen sorunlar](https://learn.microsoft.com/en-us/power-automate/approvals-known-issues), [Microsoft destek](https://support.microsoft.com/en-nz/help/4515128/issues-with-flow-approval-emails-in-outlook-desktop-and-web) (C)
- Power Automate: yanıtlanmayan onay 30 günde zaman aşımına uğrar; süre bütün akışı kapsar, çok adımlı onayda bekleyen her şey birlikte düşer; önerilen çözüm ebeveyn-çocuk akışlara bölmektir. Başka bir Microsoft sayfası 28 gün, bir Dynamics topluluk makalesi 29,5 gün diyor — [Microsoft Learn modülü](https://learn.microsoft.com/en-us/learn/modules/approvals-power-automate/5-issues-limits), [bilinen sorunlar](https://learn.microsoft.com/en-us/power-automate/approvals-known-issues), [topluluk makalesi](https://community.dynamics.com/article/KA-25075) (C; süre konusunda kaynaklar çelişkili)
- Power Automate için resmî kaynaklarda yerleşik bir eskalasyon özelliği bulunamadı. Talep sahibi onayı yeniden atayamaz, yalnız iptal edip akışı düzenleyebilir — [Microsoft Learn: onay taleplerini işleme](https://learn.microsoft.com/en-us/power-automate/guidance/business-approvals-templates/process-approval-requests), [Microsoft Q&A](https://learn.microsoft.com/en-ca/answers/questions/5537447/power-automate-approval-function) (C; cümlenin iki sayfadan hangisine ait olduğu arama özetinden kesinleşmedi)

**Vekâlet, yerine bakma, izin kuralı**

- SAP: planlı ve plansız yokluk için iki tür kural vardır. Planlıda işler vekilin listesine otomatik iletilebilir. Plansızda vekil önceden adlandırılır, ama kural vekil etkinleştirene kadar pasif kalır — [SAP Help](https://help.sap.com/saphelp_gateway20sp12/helpdata/en/1c/4d8b52039efe46e10000000a441470/content.htm), [SAP Learning](https://learning.sap.com/learning-journeys/developing-business-processes-with-sap-process-orchestration/using-the-business-process-management-bpm-inbox_e7045520-f471-4148-9304-de29edbadb97) (C)
- SAP Task Center: vekâlet kullanıcı menüsünden kurulur, dönem zorunludur, süresi dolan vekâlet kendiliğinden silinir — [SAP blog](https://blogs.sap.com/2023/05/10/substitution-management-delegation-is-now-possible-in-sap-task-center/) (C)
- Dynamics 365 Finance: otomatik devir kullanıcı seçeneklerinden kurulur: kapsam (tüm işler ya da belirli modül / iş akışı), vekil, başlangıç ve bitiş tarihi, sonra kural etkinleştirilir — [Microsoft Learn](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/fin-ops/organization-administration/tasks/delegate-work-items-workflow) (C)
- Oracle: izin (vacation) kuralı görevleri yeniden atar ya da devreder; izin dönemi başlangıç ve bitiş tarihiyle ayrıca tanımlanır; yönetici bu özelliği bütün kullanıcılar için açıp kapatabilir — [Oracle Procurement 25A](https://docs.oracle.com/en/cloud/saas/procurement/25a/oapro/how-you-work-with-vacation-rules.html), [Oracle BPM kullanıcı kılavuzu](https://docs.oracle.com/en/middleware/fusion-middleware/bpm/12.2.1.3/bpm-user/managing-workloads-users-process-workspace.html) (C)
- NetSuite: "When you delegate approval, your approval limits still apply."; vekâlet kurulunca devredene de vekile de e-posta gider — [NetSuite: çalışan onay bilgisi](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/bridgehead_1504578604.html) (C)
- NetSuite SuiteApprovals: onay isteği sıradaki onaycıya ve varsa onun vekiline birlikte gönderilir — [NetSuite: SuiteApprovals vekâlet](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/bridgehead_1506060872.html) (C)
- ServiceNow (Zurich sürümü, "granular delegation"): onaylar görevlerden ayrı devredilebilir; kural koşulları vekilin hangi kayıtları onaylayabileceğini sınırlar; vekâlet dönemi başlayınca vekile bildirim gider — [ServiceNow belgeleri](https://www.servicenow.com/docs/r/zurich/employee-service-management/granular-delegation/granular-delegation-explore.html) (C)
- Odoo: standart Approvals uygulamasında vekâlete ilişkin resmî belge bulunamadı; devir ve vekâlet üçüncü taraf modüllerde sunuluyor — [Odoo 19 belgeleri](https://www.odoo.com/documentation/19.0/applications/hr/approvals.html), [örnek üçüncü taraf modül](https://apps.odoo.com/apps/modules/17.0/x2_approval_workflow) (C)
- Salesforce (Flow onay süreçleri): tamamlanmamış onay iş kalemini yeniden atayan eylem "Approval Admin" yetkisi ister — [Salesforce geliştirici belgeleri](https://developer.salesforce.com/docs/atlas.en-us.api_action.meta/api_action/actions_obj_reassign_approval_work_item.htm) (C)

**Eskalasyon, süre (SLA) ve hatırlatma**

- Dynamics 365 Finance: adımın "Time limit" sekmesinde süre tanımlanır; süresi geçen iş eskalasyon yolundaki birinci kişiye, yine tamamlanmazsa ikinci kişiye gider; belgedeki örnekte son eylem "Reject"tir — [Microsoft Learn: elle görev](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/fin-ops/organization-administration/configure-manual-task-workflow), [onay adımı](https://learn.microsoft.com/en-gb/dynamics365/fin-ops-core/fin-ops/organization-administration/configure-approval-step-workflow) (C)
- Oracle BPM: yükseltilen görev yöneticinin tanımladığı eskalasyon yoluna, yol yoksa kullanıcının amirine gider; sona erme (expiration) politikası ve uzatma ayrı ayardır — [Oracle BPM: çalışma alanı yönetimi](https://docs.oracle.com/en/middleware/bpm/12.2.1.3/bpm-user/managing-process-workspace.html) (C)
- ServiceNow Advanced Approval Management: bekleyen onay yapılandırılabilir sürenin sonunda atanmış onaycıya otomatik yükseltilir ve e-posta gider; hatırlatma e-postası da yapılandırılabilir bekleme süresinden sonra gönderilir — [ServiceNow: onay bildirimleri](https://www.servicenow.com/docs/r/order-management/sales-and-order-management/setting-up-approval-notifications.html) (C)
- SAP S/4HANA Cloud Public Edition: Joule sohbetiyle onay bekleyen satın alma siparişleri bulunur, bekleyen kalemler ve onaycıları görülür, hatırlatma yapılır — [SAP ürün sayfası](https://www.sap.com/products/spend-management/s4hana-cloud-public-edition-purchase-order-approvals-reminder.html) (C)

**Kayda bağlı görev, not, takipçi**

- Odoo chatter: "Log note" iç nottur; "Send message" kaydın takipçilerine otomatik gider. Mesajı almaması gereken takipçi gönderimden önce çıkarılmalı ya da not kullanılmalıdır. `@` ile kullanıcı etiketlenir — [Odoo 19 chatter](https://www.odoo.com/documentation/19.0/applications/productivity/discuss/chatter.html) (C)
- Odoo etkinlikleri (activities): kayda bağlı takip görevidir; "Schedule", "Mark as Done" ve "Done & Schedule Next" seçenekleri vardır; geciken etkinlik kırmızı gösterilir — [Odoo 19 chatter](https://www.odoo.com/documentation/19.0/applications/productivity/discuss/chatter.html) (C)
- Salesforce: kayda bağlı konuşma Slack içinde "Salesforce Channels" olarak sunuluyor (Messages sekmesi + Record Details sekmesi). Üçüncü taraf bir yazıya göre Summer '26'dan itibaren yeni Enterprise ve Unlimited org'lar hazır bir Slack çalışma alanıyla ve Chatter kapalı olarak açılıyor; mevcut org'lar etkilenmiyor — [Rapidi](https://www.rapidionline.com/blog/salesforce-chatter-to-slack), [Salesforce Ben](https://www.salesforceben.com/im-killing-chatter-will-parker-harris-retire-the-collaboration-tool) (C; resmî emeklilik tarihi doğrulanamadı)

**Kontrol listesi ve yinelenen görev (ay sonu kapanış araçları)**

- FloQast: kontrol listesi her görevin sahibini, şirketini (entity), süresini, bağımlılıklarını ve son tarihini izler — [FloQast](https://floqast.com/optimize-the-close) (C)
- FloQast: görevler rol düzeyinde atanabilir; hazırlayan ve gözden geçiren için ayrı son tarih ve imza vardır; hazırlayana, gözden geçirene, son tarihe ve duruma göre süzülür — [FloQast yardım](https://help.floqast.com/hc/en-us/articles/360002040252-Close-Checklist-Tab) (C)
- Kullanıcı yorumları: ad hoc ve yinelenen görev eklemek kolay bulunuyor; çeyrek sonu dışına düşen yinelenmeler ve "hazırlayan aylık, gözden geçiren üç aylık" ayrımı zor bulunuyor — [G2](https://g2crowd.com/products/floqast/reviews), [TrustRadius](https://www.trustradius.com/reviews/floqast-2025-02-25-14-47-41) (C; tekil kullanıcı yorumları)

**Kutuda yapay zekâ:** Soru 4'te.

### Çıkarımlar (Inferences)

- Ortak çekirdek "kaynak adaptörü + tek liste + süreli vekâlet + süre ve eskalasyon + hatırlatma + kayda bağlı not ve görev"dir. Bizdeki dört `*PendingApprovalAdapterV1.cs` dosyası (`src/backend/GoymenERP.Api/WorkRuntime/`) aynı adaptör kalıbıdır. İş yeni bir motor yazmak değil, adaptör sözleşmesini bütün onay türlerine yaymak ve karar komutunu kutuya getirmektir.
- Beş satıcının vekâlet alanları aynıdır: kimden kime, başlangıç ve bitiş, kapsam (tür ya da modül). Bu, 21 Ekim kararı No 125 ile birebir örtüşür (`docs/decisions/SINAN_KARARLARI_2026-10-21.md`). NetSuite'in "devredenin limiti geçerli kalır" kuralı ile ServiceNow'un "koşulla sınırlama" kuralı No 125'in "işlem türü seçimli" maddesini tamamlar.
- SAP'nin "plansız yokluk" kalıbı (önceden adlandırılmış ama pasif duran vekil) hastalık gibi durumlar için ucuz bir çözümdür ve AI-D20'deki "geciken onayı vekile yönlendirmeyi öner" maddesiyle birleşir.
- E-postadan onay en kırılgan kısımdır (istemci sürümü, konuk kullanıcı, anahtar sözcük ayrıştırma). Bizde ayrıca KLN-D110/D111 e-posta kanalını kapalı tutuyor ve üretim e-posta sağlayıcısı insan kapısıdır (No 155).
- Dynamics örneğinde zaman aşımının son eylemi "Reject"tir. Sessiz otomatik red ya da onay işletme politikası seçmek demektir; bizde zaman aşımı yalnız "yükselt" olmalıdır.
- "Kimde bekliyor" bilgisini talep sahibine göstermek (SAP Joule örneği) "nerede kaldı" sorularını azaltır; ucuzdur, çünkü aynı veriyi kullanır. (D; etkisini ölçen kaynak bulunamadı)
- Kayıt sayfasında tek zaman akışı (olaylar, notlar, görevler ve onay kararları bir arada) Odoo chatter'ın sunduğu şeydir. Bizde olay verisi audit kaydında duruyor; eksik olan bunu kayıt sayfasında not ve görevle birlikte göstermektir. Kayıt okuyucusuna hangi olayların gösterileceği yetki konusudur. (C/D)
- Salesforce'un Chatter'dan Slack'e yönelmesi, uygulama içine gömülü genel akışın tek başına yaşamadığını düşündürür. Kalıcı olan fikir konuşmanın kayda bağlı olmasıdır. (D; tek satıcı örneği)

### Boşluklar (Gaps)

- Linear, Asana, ClickUp ve Jira'nın triage kutusu ayrıntıları (erteleme, arşiv, "sonra hatırlat", takip görevi) doğrulanamadı. İlk arama yalnız pazarlama blogları döndürdü; ikinci arama bütçe bittiği için yapılamadı.
- Slack ve Teams'te sessiz saat, bildirim takvimi ve Teams Approvals ayrıntıları aranamadı.
- BlackLine görev yönetimi, Workday ve Coupa onay zinciri belgeleri aranamadı.
- SAP Task Center'ın kendi arayüzünde toplu onay olup olmadığı doğrulanamadı (bulunan kaynaklar My Inbox'a ait).
- Salesforce'ta "delegated approver" için resmî belge bulunamadı.
- Devir notu (hand-over), "birini bekliyor" durumu ve iş yükü görünümü için ürün belgesi düzeyinde kaynak bulunamadı. Oracle BPM kullanıcı kılavuzunda "Managing Workloads Among Users" başlığı var, içeriği okunamadı.
- SAP bilgi bankası makalelerinde hangi cümlenin hangi makaleye ait olduğu arama özetinden çıkarılamadı.

## 2. Onay politikası tasarımı: eşik, dört göz, görevler ayrılığı, istisna bazlı onay, seviye sayısı ve çevrim süresi kıyasları

### Çıkarım özeti (Takeaway)

Kıyaslar iyi işleyen onayın saatler ile bir gün arasında bittiğini gösteriyor; günler süren onayın nedeni çoğu zaman onaycının hızı değil, eksik bağlam, belirsiz onay yolu ve gereksiz zincirdir. Bulunan kaynakların (çoğu satıcı kaynaklı) gösterdiği yön az seviye, limiti yeten kişiye doğrudan yönlendirme ve temiz kaydın onaysız akmasıdır. "Tipik seviye sayısı", kredi limiti onayı ve fiyat istisnası onayı için bağımsız kıyas bulunamadı.

### Kaynaklı bulgular (Cited Findings)

**Çevrim süresi kıyasları**

- APQC ölçüsü "satın alma talebinden satın alma siparişine" çevrim süresini gün olarak (hafta sonu dahil) tanımlar; medyan değerler üye sayfasındadır, arama özetinde yoktur — [APQC](https://www.apqc.org/what-we-do/benchmarking/open-standards-benchmarking/measures/cycle-time-issue-purchase-order-goods) (B)
- APQC verisine dayanan yazı: en iyi kuruluşlar satın alma siparişini yaklaşık bir günde çıkarıyor, zayıflar bunun iki katından uzun sürede — [Supply Chain Management Review](https://www.scmr.com/article/how-efficient-is-your-procurement-process) (B; yayın tarihi doğrulanamadı)
- APQC'ye dayanan ikincil kaynak: sipariş verme süresinin medyanı en iyilerde 8,0 saat, diğerlerinde 11,0 saat — [SupplyChainBrain](https://www.supplychainbrain.com/articles/9766-cycle-time-in-hours-to-place-purchase-order) (B; tarihsiz, muhtemelen eski, dikkatle kullanın)
- APQC'nin fatura ölçüsü: "receipt of invoice until approved and scheduled for payment" gün olarak tanımlı; değer görülemedi — [APQC](https://www.apqc.org/resources/benchmarking/open-standards-benchmarking/measures/cycle-time-days-receipt-invoice-until-0) (B)
- Coupa, kendi müşteri tabanı: "leading companies" için talepten siparişe 6,2 saat, fatura onayı 23,1 saat, masraf raporu onayı 24,8 saat (toplam saat, iş saati değil; tarihsiz blog) — [Coupa blog](https://www.coupa.com/blog/4-proven-spend-management-benchmarks-you-ll-want-to-reach-for) (C [İDDİA])
- Coupa 2019 kıyas özeti: talepten siparişe 12,7 saat, fatura onayı 32,5 saat, masraf onayı 21,7 saat — [Coupa 2019 kıyas belgesi](https://www.scribd.com/document/446794656/Coupa-Benchmark-2019-pdf) (C [İDDİA]; eski; kaynak eşlemesi arama özetinden kesin değil)
- Coupa, şirket büyüklüğüne göre talepten siparişe ortalama: 16 saat (250 milyon – 1,5 milyar $ ciro), 17 saat (1,5 milyar $ üstü), 27,5 saat (250 milyon $ altı) — [Coupa blog](https://www.coupa.com/blog/coupa-benchmark-requisition-to-approval-workflow-times) (C [İDDİA]; tarihsiz)
- Coupa 2026: talepten siparişe 2,8 iş saati (orta ölçek) ve 3,4 iş saati (büyük ölçek). Bu rakamlar iş saatidir; yukarıdaki toplam saatlerle karşılaştırılamaz — [Coupa](https://www.coupa.com/?p=70746) (C [İDDİA])
- Procurify 2026 raporu: talepten siparişe medyan 55 saat, en hızlılar 40 saatin altında. Satıcının yorumu: gecikmenin nedeni çoğu zaman onayın kendisi değil, eksik talep bağlamı, belirsiz onay yolu ve bütçe görünürlüğü; gecikme bir ya da iki aşamada kümelenir — [Procurify](https://www.procurify.com/blog/improve-purchase-order-cycle-time/) (C [İDDİA])
- Fatura, Ardent Partners 2025 raporuna atıf yapan satıcı blogları: en iyi sınıfta fatura işleme 3,1 gün; ortalama bir kaynakta 17,4 gün, bir başkasında 10,9 gün. İstisna oranı en iyi sınıfta %9, diğerlerinde %22. Dokunmadan (touchless) işleme üst dilimde %49,2 — [Corpay](https://www.corpay.com/resources/blog/ap-team-productivity-benchmarks), [Thomson Reuters](https://europe.thomsonreuters.com/blog/3-ap-metrics-that-matter) (B kaynağın C aktarımı)
- Aynı kuruma atfedilen başka bir tablo farklı rakamlar veriyor: işleme süresi 8,2 gün ve 2,9 gün, istisna oranı %18,4 ve %11,1 — [Tipalti](https://tipalti.com/blog/ap-automation-roi-benchmark/) (rakamlar çelişkili; rapordan doğrulanmalı)

**Yönlendirme ve seviye**

- NetSuite klasik yönlendirme: işlem, onay limiti tutara eşit ya da ondan büyük olan amir ya da onaycıya gider — [NetSuite: Approval Routing](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N2395258.html), [limitler](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N2395573.html) (C)
- NetSuite'te standart bir seçenek limiti yetmeyen onaycıların atlanmasını sağlar; ardışık olmayan ya da koşullu yönlendirme için özel SuiteFlow iş akışı gerekir — [NetSuite: özel iş akışı](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N2396465.html) (C)
- Coupa'nın bir yazısının başlığı talepten siparişe süreyi kısaltmak için "shorter approval chains" öneriyor; içerik okunamadı — [Coupa blog](https://www.coupa.com/blog/how-speed-requisition-order-time-shorter-approval-chains/) (C [İDDİA]; yalnız başlık)
- Süreç madenciliği araçları "onay sayısı ile geçiş süresi" ilişkisini hazır pano olarak sunar: talep başına ortalama onay sayısı ve onay geçiş süresi — [UiPath: satın alma talepleri](https://docs.uipath.com/process-mining/standalone/2021.10/user-guide/p2p-procurement-purchase-requisitions), [UiPath KPI'ları](https://docs.uipath.com/process-mining/automation-cloud/latest/user-guide/p2p-kpis) (C)

**Dört göz, görevler ayrılığı, sonradan denetim**

- Dört göz ilkesinin etkisi deneylerde karışıktır; ayrıntı Soru 3'te.
- ACFE 2024 raporuna atıfla: masraf iadesi yolsuzluğu fark edilmeden ortalama 12 ay sürüyor — [Corpay](https://www.corpay.com/resources/blog/expense-approval-process) (B kaynağın C aktarımı; ACFE raporundan doğrulanmalı)
- Repo içi: istisna bazlı inceleme (K3) ve istisna öncelikli ana sayfa (A1) için bulunan kanıt C düzeyindedir (satıcı belgeleri, bağımsız ölçüm yok) — `docs/delivery/arastirma/2026-11-22_sektorler_arasi_erp.md`, `docs/delivery/arastirma/2026-11-22_kanitlanmamis_is_erp_fikirleri.md`
- Repo içi: hazırlayan ≠ onaylayan kuralı kararlarda yerleşik (ör. SAT-D88 fatura taslağı, FNS-D100 kredi) — `docs/decisions/SINAN_KARARLARI_2026-10-28_SATIS_EK13.md`, `docs/decisions/SINAN_KARARLARI_2026-11-16_DALGA4_EK23.md`. Ayarlar'da görevler ayrılığı ve finans onay matrisi politika alanları mevcut — `docs/delivery/arastirma/2026-11-22_goymen_erp_envanteri.md`

### Çıkarımlar (Inferences)

- Kıyasların ortak mesajı: iyi işleyen onay saatlerle ölçülür. Bizdeki 2 iş günü eskalasyon başlangıcı (No 125) bu ölçeğe göre bir üst sınırdır, hedef değildir; hedef medyanı saat cinsinden izlemek anlamlıdır.
- Varsayılan tek seviye (hazırlayan ≠ onaylayan) olmalı; ikinci onaycı yalnız kural tablosunda açıkça işaretlenen türlerde (para eşiği, kritik ayar, yetki) açılmalı. İş, limiti yeten kişiye doğrudan gitmeli; merdiven tırmanmamalı (NetSuite'in "atla" kalıbı). (C + D)
- Satıcı kıyasının iddiası doğruysa gecikmenin asıl kaynağı onaycının karar süresi değil, eksik bağlam yüzünden yaşanan "bilgi iste" ve iade turlarıdır. Onay kartında eksiksiz bağlam (ekler, sapmalar, "bu sayıyı açıkla" izi) bu turları azaltmanın en ucuz yoludur. (C [İDDİA]; bağımsız ölçüm yok, bizde "dokunuş sayısı" metriğiyle sınanabilir)
- İstisna bazlı akışın ölçüsü fatura dünyasındaki "dokunmadan işleme" ve "istisna oranı"dır. Aktarılan rakamlara göre en iyi sınıfta bile faturaların %9–11'i istisnaya düşüyor ve dokunmadan işlenen pay yarıyı geçmiyor; "sıfır onay" gerçekçi bir hedef değildir. (B/C; rakamlar çelişkili aktarımlardan)
- Onay tek başına hileyi yakalamıyor (12 ay). Onaysız akan kayıtlar için sonradan örneklem denetimi ve kural bazlı tarama, onay zincirini uzatmaktan daha uygun bir güvencedir. Örneklem oranı politikadır. (D)
- Eşikler, bantlar, hangi türün onaysız akacağı ve örneklem oranı Sinan'ın kararıdır; bu not değer seçmez.

### Boşluklar (Gaps)

- Tipik onay seviyesi sayısı için bağımsız kıyas bulunamadı.
- Kredi limiti onayı ve fiyat istisnası (taban fiyat altı satış) onayı için çevrim süresi kıyası bulunamadı; arama bütçesi bittiği için ikinci deneme yapılamadı.
- "Bir onay seviyesini kaldırınca süre şu kadar kısalır" diyen hakemli çalışma bulunamadı; süreç madenciliği araçları bunu ölçmeye yarar ama genel bir sonuç yayımlamıyor.
- Otomatik onay ve örneklem denetimi oranı üzerine kanıt aranamadı.
- APQC medyanları üye duvarının arkasında; Ardent ve Coupa raporlarının kendisi okunamadı.

## 3. Göstermelik onay, onay yorgunluğu ve bildirim yükü: kanıt ve tasarım kuralları

### Çıkarım özeti (Takeaway)

En sağlam üç bulgu şunlar: (a) tekrar eden uyarı daha çok yok sayılır; (b) ikinci göz, bireysel hesap verebilirlik ve bağımsızlık yoksa beklenen katkıyı vermeyebilir; (c) acil olmayan bildirimi günde yaklaşık üç kez toplu vermek hem anlık akıştan hem tam sessizlikten daha iyi sonuç verdi. Onayların yüzde kaçının göstermelik (rubber-stamp) olduğunu ölçen çalışma bulunamadı.

### Kaynaklı bulgular (Cited Findings)

**Göstermelik onay**

- Onayların ne kadarının incelenmeden verildiğini ölçen çalışma bulunamadı. Bir satıcı yazısı yöneticilerin "at a high level (not looking at receipts, not checking for duplicates with prior reports, etc.), at month end, and usually alongside multiple other last-minute approval requests" onay verdiğini anlatıyor — [AppZen](https://www.appzen.com/blog/manager-approval-process-for-expense-reports-useless-or-useful) (C; anlatı, ölçüm değil)
- Bir başka satıcı yazısına göre yönetici kontrolü, kurala devredilemeyen ve en sık göstermelik yapılan kontroldür; harcamanın gerçek ve yerinde olup olmadığını yalnız yönetici bilir — [Corpay](https://www.corpay.com/resources/blog/expense-approval-process) (C; görüş)
- GBTA'ya atıfla: masraf raporlarının %19'unda hata var; bir raporu düzeltmek ortalama 18 dakika ve 52 $ tutuyor — [Concur](https://concur.com/blog/article/rubber-stamping-it-could-be-saving-you-time-costing-you-money) (B kaynağın C aktarımı; eski olması muhtemel)

**İkinci göz (dört göz) deneyleri**

- Otomasyon izleme deneyi (Human Factors, 2018): bireysel performansının ölçüleceği söylenmeyen ekipler daha az çapraz kontrol yaptı ve hata yakalamada tek çalışanlardan iyi değildi; bireysel geri bildirim bekleyenlerde ekip üstünlüğü ortaya çıktı — [PubMed](https://pubmed.ncbi.nlm.nih.gov/29939767/) (A; 2018, eski)
- Ardışık çift kontrol deneyi (Cymek ve Manzey, 2022): "blinded processes, however, seem to provoke less social loafing than nonblinded processes"; kör olmayan düzende de ikinci kontrolcü tek kontrolcüye göre güvenlik payı ekledi — [yayın kaydı](https://biomed.reviewer.ly/app/publication/12b597c2-5378-4f18-acba-bc7ce36812ae) (A; 2022, eski)
- 2024 deneyi (seyrek hedefli görsel arama): birinci deneyde sosyal kaytarma izi görülmedi; ekip düzeninde süre, taranan alan, yakalama ve yanlış alarm arttı; yedeklilik kör ve kör olmayan biçimde etkiliydi — [PMC](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11450219/) (A)
- Rüşvet oyunu deneyleri: bir çalışmada dört göz kuralı tek seferlik kurguda etkisizdi, tekrarlı kurguda rüşveti azalttı; bir başkasında net etki olumsuzdu — [Applied Economics Letters, 2018](https://informahealthcare.com/doi/abs/10.1080/13504851.2018.1456644), [Schikora, "Bringing the four-eyes-principle to the lab"](https://www.econbiz.de/Record/bringing-the-four-eyes-principle-to-the-lab/10010270756) (A; eski; yolsuzluk bağlamı; hangi sonucun hangi makaleye ait olduğu arama özetinden kesinleşmedi)
- Düzeltme okuması (Nihei, 2002): ikili ekipler bireylerden daha çok hata buldu — [makale PDF](https://researchmap.jp/niheiyoshiaki/published_papers/17492187/attachment_file.pdf) (A; klasik)
- Bu konuda kapsamlı bir meta-analiz arama sonuçlarında çıkmadı; çalışmalar küçük laboratuvar deneyleridir.

**Uyarı yorgunluğu (sağlık bilişimi; en iyi ölçülmüş alan)**

- 2025 sistematik derleme ve meta-analiz (JMIR ön baskı, henüz hakem sürecinden geçmemiş): ilaç etkileşimi uyarılarında birleşik yok sayma oranı %79,8 (%95 güven aralığı 73,0–86,7), çalışmalar arasında %55,4–%95,7; uyarı yorgunluğu için tutarlı bir ölçü yok — [JMIR Preprints](https://preprints.jmir.org/preprint/88578) (A−)
- Ancker ve ark. 2017 (112 birinci basamak hekimi, geriye dönük kohort): "Clinicians became less likely to accept alerts as they received more of them, particularly more repeated alerts." Yeni devreye alınan bir uyarıda zamanla duyarsızlaşma kanıtı bulunmadı. Önerileri aynı hasta için tekrarları azaltmak — [BMC Medical Informatics and Decision Making](https://link.springer.com/10.1186/s12911-017-0430-8), [AHRQ PSNet özeti](https://psnet.ahrq.gov/issue/effects-workload-work-complexity-and-repeated-alerts-alert-fatigue-clinical-decision-support) (A; 2017, eski)
- Karşı bulgu: aynı makalenin andığı bir sistematik derlemede çalışmalar arasında uyarı hacmi ile yok sayma yüzdesi arasında ilişki bulunmamış. Hacim etkisi tartışmalı, tekrar etkisi daha iyi destekleniyor — [BMC Medical Informatics and Decision Making](https://link.springer.com/10.1186/s12911-017-0430-8) (A)
- Alerji uyarılarında yok sayma on yıl içinde arttı; tekrar eden uyarı ilk kez çıkan uyarıdan daha sık yok sayılıyor — [Wolters Kluwer](https://www.wolterskluwer.com/en/expert-insights/clinicians-continue-hitting-snooze-button-on-patient-alarms) (C aktarım; kaynak eşlemesi arama özetinden kesin değil)
- Repo içi düzeltme notu: `docs/delivery/ERP_BOSLUK_ANALIZI_v1.md` (satır 128) "alarm yorgunluğu %90–96 red" diyor. 2025 meta-analizi birleşik oranı yaklaşık %80, aralığı %55–96 veriyor; %90–96 aralığın üst ucudur.

**Bildirim: anlık mı, toplu mu?**

- Fitz ve ark. 2019 (Computers in Human Behavior; 237 katılımcı, iki hafta, rastgele atama: olağan / günde üç kez toplu / saatlik toplu / hiç bildirim): günde üç kez toplu alanlar kendilerini daha dikkatli, daha üretken, daha iyi ruh hâlinde ve telefon üzerinde daha kontrollü hissetti, stresleri daha düşüktü. Saatlik toplu vermek çok az fayda sağladı. Hiç bildirim almayanlarda kaygı ve "kaçırma korkusu" arttı — [Duke Scholars](https://scholars.duke.edu/publication/1402953), [Springer Nature topluluk yazısı](https://communities.springernature.com/posts/batching-notifications-can-improve-attention-well-being-and-productivity) (A; 2019, eski; öz bildirim; etki büyüklüğü özetlerde yok. Özet metninde "more productive" ve "lower productivity" birlikte geçiyor; birincil metinden doğrulanmalı)
- Kushlev ve Dunn 2015 (124 yetişkin, iki hafta, denek içi tasarım): e-postayı günde üç kez kontrolle sınırlamak günlük stresi düşürdü. Uyum tam değildi: hedef 3, gerçekleşen yaklaşık 4,5; olağan düzen yaklaşık 15,5 kontrol — [UBC Psychology](https://psych.ubc.ca/news/checking-your-email-less-can-help-reduce-stress-new-study-by-phd-candidate-kostadin-kushlev/) (A; klasik, 2015)
- Hollanda'da bir finans kuruluşunda yarı deney: e-postayı toplu işleyen grup daha az e-posta kesintisi yaşadı; toplu işleme özellikle yüksek e-posta hacminde duygusal tükenmeyi azalttı. Aynı makalenin aktardığı 2013 saha deneyinde e-postayı günde bir kez kontrol etmek sürekli kontrolden daha az stres yarattı — [PMC](https://pmc.ncbi.nlm.nih.gov/articles/PMC8897209) (A)
- Microsoft Work Trend Index özel raporu (Haziran 2025, telemetri): Microsoft 365 kullanan çalışanlar ortalama iki dakikada bir toplantı, e-posta ya da bildirimle kesiliyor; basın özetlerine göre günde 117 e-posta ve 153 Teams mesajı alınıyor; çalışanların %48'i ve liderlerin %52'si işi "chaotic and fragmented" buluyor — [Microsoft WorkLab](https://microsoft.com/en-us/worklab/work-trend-index/breaking-down-infinite-workday), [Microsoft News](https://news.microsoft.com/de-ch/2025/06/17/new-microsoft-study-reveals-the-rise-of-the-infinite-workday-40-of-employees-check-email-before-6-a-m-evening-meetings-up-16/) (C; satıcı telemetrisi ve anketi)
- Yöntem uyarısı: "günde 275 kesinti" ve "iki dakikada bir" rakamlarının bildirim hacmine göre en üst %20'deki kullanıcıları kapsadığını tek bir kaynak yazıyor; Microsoft'un yöntem notundan doğrulanamadı — [Silicon Canals](https://siliconcanals.com/?p=104955) (tek kaynak)

### Çıkarımlar (Inferences): tasarım kuralları

1. **Tekrar yok.** Aynı kayıt, aynı alıcı ve aynı olay için ikinci "yeni" bildirim üretilmez; güncelleme mevcut bildirimi yeniler. Hatırlatma en çok bir kezdir; sonrası eskalasyondur. (A yönünde: tekrar eden uyarı daha çok yok sayılıyor. Sağlık alanından aktarım olduğu için ERP onayına genelleme D.)
2. **Az ve anlamlı onay.** İstisna bazlı akış "her seferinde aynı şeyi onaylama" yükünü kaldırır; onaya düşen her iş neden düştüğünü (hangi kural, hangi sapma) ilk satırda söyler.
3. **İkinci göz bağımsız ve kişisel olmalı.** Onaycıya hazırlayanın vardığı sonuç değil, kanıt gösterilir (kör ikinci kontrol benzetmesi). Karar her zaman bir kişiye yazılır; "gruptan biri onaylar" kuralında bile onaylayan kişi kayıtlıdır. Zincir uzadıkça herkes bir sonrakine güvenir; varsayılan en çok iki onaycıdır. (A; laboratuvar görevlerinden aktarım)
4. **İki bildirim sınıfı.** "Şimdi" (birinin işini ya da bir aracı bekleten, SLA'sı dolan, güvenlik) ve "özet" (geri kalan her şey). Özet günde yaklaşık üç kezdir; saatlik özet fayda sağlamadı; tamamen kapatmak da kaygıyı artırdı. Onay ve eskalasyon bildirimleri kullanıcı tarafından kapatılamaz. (A; telefon bildirimlerinden aktarım)
5. **Kutu bir çekme yüzeyidir.** Sayaç ve rozet anlık güncellenir; itme (toast, ileride push ya da e-posta) yalnız "şimdi" sınıfı içindir. Kutu, zaten sık kesilen kullanıcıya bir kesinti kanalı daha eklememelidir. (C/D)
6. **Göstermelik onay ölçülür.** Kanıt alanı açılmadan verilen onay payı, çok kısa karar süresi, %100'e yakın onay oranı, toplu onay payı ve örneklem denetiminde çıkan hata oranı izlenir. (D; doğrudan kanıt yok)
7. **Toplu onay dar tutulur.** Yalnız sapmasız, eşik altı, aynı türden kayıtlar; her kayıt için ayrı denetim satırı. (D)

### Boşluklar (Gaps)

- "Onayların %X'i incelenmeden veriliyor" diyen ölçüm bulunamadı.
- Sessiz saatlerin (quiet hours) ve önem katmanı sayısının etkisini ölçen çalışma aranamadı (bütçe).
- İş yerinde kesintinin maliyeti üzerine klasik insan-bilgisayar etkileşimi çalışmaları bu oturumda doğrulanamadı; bu yüzden rakam yazılmadı. Doğrulanacak adaylar (hatırlanan, bu oturumda aranmadı): Mark, Gudith ve Klocke, "The cost of interrupted work: more speed and stress" (CHI 2008); Iqbal ve Horvitz, "Disruption and recovery of computing tasks" (CHI 2007).
- Fitz 2019 ve Kushlev 2015 için etki büyüklükleri arama özetlerinde yoktu.
- Uyarı yorgunluğu kanıtı sağlık alanından; ERP onaylarına doğrudan ölçüm yok.

## 4. Kutuda yapay zekâ: satıcılar ne sevk ediyor, karar kalitesi ve aşırı güven üzerine kanıt ne diyor, güvenli arayüz kalıbı nedir?

### Çıkarım özeti (Takeaway)

Satıcıların sevk ettiği şey çoğunlukla özet ve sohbetle erişimdir; "önerilen karar" ve öneriye göre onay bulunan kaynaklarda yalnız ServiceNow'da var. Bağımsız kanıt uyarıcıdır: karar görevlerinde insan ile yapay zekânın birlikte çalışması ortalamada ikisinden iyi olanın gerisinde kalıyor; düşünmeye zorlayan tasarım aşırı güveni azaltıyor ama kullanıcılar bunu sevmiyor. Güvenli kalıp: önce kanıt, öneri yüksek riskte isteğe bağlı, tek tık itiraz ve neden kodu, sonradan sonuç geri bildirimi.

### Kaynaklı bulgular (Cited Findings)

**Satıcıların sevk ettikleri**

- Dynamics 365 Finance "Workflow history summary": iş akışı geçmişi sayfasının üstünde yapay zekâ özeti. İlk satır göndereni, tarihi, güncel durumu ve yorumları, sonraki satırlar en son eylemleri verir. Amaç onaycının geçmişte arama süresini azaltmaktır. Azure OpenAI kullanır; 10.0.40 sürümünden itibaren varsayılan açıktır — [Microsoft Learn](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/fin-ops/organization-administration/workflow-history-summary), [yayın planı](https://learn.microsoft.com/en-us/dynamics365/release-plan/2024wave1/finance-supply-chain/dynamics365-finance/make-faster-decisions-using-workflow-summary) (C)
- Aynı özelliğin belgelenmiş sınırları: özet yalnız görüntülenen iş akışı geçmişine dayanır, yalnız en son eylemleri gösterir ve onay sırasında girilen yorumları işlemez — [Microsoft: şeffaflık SSS](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/fin-ops/copilot/workflow-history-summary-faq) (C)
- ServiceNow Now Assist, "Platform Approval assistance" ajanı: kullanıcı bekleyen onaylarını ve ayrıntılarını görür, sanal asistan içinden onaylar ya da reddeder — [ServiceNow belgeleri](https://www.servicenow.com/docs/r/zurich/intelligent-experiences/enable-ai-experiences/platform-approval-aia.html) (C)
- ServiceNow Now Assist, "Approval Assistance" ajanı: onay kayıtlarını çeker, politika, kontrol listesi ve değerlendirme ölçütlerini inceler ve "Now Assist recommended decision"a göre talebi onaylayabilir — [ServiceNow belgeleri](https://www.servicenow.com/docs/r/SogDT~k7ktsbe1ZYegnaLg/CdoJx2nsRcjyRo7VAwUbuQ) (C)
- SAP Joule (S/4HANA Cloud Public Edition): sohbetle onay bekleyen satın alma siparişlerini bulma, bekleyen kalemleri ve onaycılarını görme, hatırlatma. Bulunan sayfalar üretilmiş bir karar önerisi tarif etmiyor — [SAP ürün sayfası](https://www.sap.com/products/spend-management/s4hana-cloud-public-edition-purchase-order-approvals-reminder.html) (C)
- Bu özelliklerin karar kalitesine ya da onay süresine etkisini ölçen bağımsız bir çalışma arama sonuçlarında çıkmadı.

**Karar kalitesi ve aşırı güven üzerine bağımsız kanıt**

- Vaccaro, Almaatouq ve Malone (Nature Human Behaviour, 2024; 106 deney, 370 etki büyüklüğü, 2020–2023 yayınları): insan ile yapay zekâ birleşimi ortalamada, insan ya da yapay zekâdan iyi olanın gerisinde kaldı; yalnız insana göre ise daha iyiydi. Kayıp karar verme görevlerinde, kazanç içerik üretme görevlerinde görüldü — [PMC](https://pmc.ncbi.nlm.nih.gov/articles/PMC11659167/), [MIT Sloan](https://mitsloan.mit.edu/node/49007) (A)
- Aynı çalışmanın etki büyüklükleri, atıf yapan bir makaleye göre: en iyi tek başına olana karşı Hedges g = −0,23; yalnız insana karşı g = 0,64 — [arXiv 2512.13253](https://arxiv.org/html/2512.13253) (A; ikincil aktarım. Aynı makale derlemeyi 106 deney yerine 74 çalışma olarak anıyor; makaleden doğrulanmalı)
- Sonraki yeniden analiz: insana sonuç geri bildirimi verilen çalışmalarda birliktelik kazancı daha yüksek — [arXiv 2512.13253](https://arxiv.org/html/2512.13253) (A−; ön baskı)
- Buçinca, Malaya ve Gajos (CSCW 2021; 199 katılımcı): üç "cognitive forcing" (düşünmeye zorlayan) tasarım, iki basit açıklanabilir yapay zekâ yaklaşımı ve yapay zekâsız temel ile karşılaştırıldı. Zorlayıcı tasarımlar aşırı güveni basit açıklamalara göre azalttı, ama tamamen gidermedi. Aşırı güveni en çok azaltan tasarımlar en düşük öznel beğeniyi aldı. Fayda en çok "düşünme ihtiyacı" (Need for Cognition) yüksek katılımcılarda görüldü — [arXiv](https://arxiv.org/abs/2102.09692v1), [Harvard yayın sayfası](https://www.eecs.harvard.edu/~kgajos/papers/2021/bucinca2021trust.shtml) (A; 2021, eski)
- Repo içi: önceki araştırma notu, insanların yanlış yapay zekâ önerisini anlamlı oranda kabul ettiğini, gerekçe yazılınca yanlışa uymanın arttığını ve kabul oranının tek başına otonomi ölçütü olamayacağını kaydediyor — `docs/delivery/arastirma/2026-11-22_kanitlanmamis_ai_yontemleri.md` (satır 78 ve 182) (B)

### Çıkarımlar (Inferences)

- AI-D13'ün üç parçası kanıta göre aynı risk düzeyinde değildir. Özet ve risk işareti "kanıt hazırlama"dır. "Öneri" ise karar görevinin içine girer; kayıp tam burada ölçülmüş. Bu yüzden öneri düşük riskli türlerde görünür başlayabilir, ama para eşiği üstü ya da ikinci onaylı türlerde kapalı başlamalı ve onaycı isteyince açılmalıdır. Hangi türlerin yüksek riskli sayılacağı Sinan'ın kararıdır.
- Düşünmeye zorlama her onaya konmamalı: en etkili tasarımlar en az sevilenler. Yalnız yüksek riskli türlerde kullanılmalı; aksi halde kullanıcı kutudan kaçar.
- Kart sırası: (1) neden onaya düştü: hangi kural, hangi sapma; (2) deterministik kural sonuçları ve hesap izi; (3) ekler ve kayıt geçmişi; (4) yapay zekâ özeti, her cümlesi kaynağa bağlı; (5) öneri (isteğe bağlı); (6) karar düğmeleri. Kural sonuçları backend'den okunur (AI-D13).
- Tek tık "öneriye katılmıyorum" ve kısa neden kodu (veri yanlış / kural yanlış / bağlam eksik / risk yanlış değerlendirilmiş / diğer) hem AI-D12 öğrenme döngüsünü hem AI-D21 ölçümünü besler.
- Kabul oranı otomasyon yanlılığıyla şiştiği için otonomi kararında tek başına kullanılmamalı; yanında sonradan geri alınan karar oranı ve örneklem denetimi sonucu bulunmalı (repo içi önceki notla aynı sonuç).
- Sonuç geri bildirimi ucuz ve kanıtı olan bir eklemedir: onaycıya geçmiş kararlarının sonradan ne olduğu gösterilir (iade, ters kayıt, tahsilat gecikmesi, şikâyet). (A−)
- Özet neyi okumadığını söylemelidir. Microsoft kendi özetinin yorumları işlemediğini belgeliyor; bizde aynı açıklık kartta görünmelidir ("şu ekler okunmadı").
- Önceliklendirme deterministik başlamalı: SLA'ya kalan süre, bekleyen araç ya da sevk, tutar. Yapay zekâ gruplama ve açıklama ekler. Kullanıcı her zaman "SLA'ya göre sırala" görünümüne dönebilmelidir; sessiz bir sıralama hatası kullanıcıya görünmez.
- Yönlendirme önerisi (geciken onayı vekile) yetkili onayıyla uygulanır (AI-D20). Mükerrer tespiti kural tabanlı kalır (fikir havuzu 1.9); yapay zekâ yalnız açıklar.
- ServiceNow'daki "öneriye göre onayla" yolu AI-D13 ile çelişir. Bizde yapay zekâ onay komutu çalıştıramaz (AI-24 onay komutlarını dışarıda bırakıyor, `docs/delivery/AI_TREN_PLANI_v2.md`); otomatik onay yalnız AI-D21 yoluyla ve tür bazında ayrı kararla açılabilir.

### Boşluklar (Gaps)

- Satıcıların onay yapay zekâsının karar kalitesine etkisini ölçen bağımsız çalışma bulunamadı.
- Önceliklendirme, otomatik yönlendirme ve mükerrer tespiti için satıcı belgesi bulunamadı; Jira'daki "smart triage" yalnız bir pazarlama blogunda geçti ve doğrulanmadı.
- Büyük dil modeli özetlerinin ve önerilerinin onaycı davranışına etkisi (2024–2026) aranamadı (bütçe); bu yüzden rakam yazılmadı. Doğrulanacak adaylar (hatırlanan, bu oturumda aranmadı): üretken yapay zekâ kullanan bilgi çalışanlarında eleştirel düşünme üzerine anket çalışması (CHI 2025); işe alım uzmanlarıyla yapılan "Falling Asleep at the Wheel" saha deneyi.
- Buçinca çalışmasındaki üç tasarımın adları arama özetlerinde yoktu. Hatırlanan biçim: öneriyi karardan önce gizleme, bekletme ve isteğe bağlı gösterme; birincil metinden doğrulanmalı.
- "Hypothesis-driven decision support" başlıklı bir makale arama sonucunda çıktı ([arXiv](https://arxiv.org/pdf/2302.12389)); içeriği okunamadı. "Öneri yerine kanıt" tasarımının kuramsal dayanağı olabilir.
- Oracle, Workday, Coupa, NetSuite ve Odoo'nun onay yapay zekâsı özellikleri bu oturumda aranamadı; AI kararları belgesinin kaynak notunda anılan önceki satıcı taraması bu boşluğu kısmen kapatır (`docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md`).

## 5. Yönetilebilirlik: kural tablosu mu, grafik tasarımcı mı; ne ters gidiyor; en yalın yapılandırma modeli

### Çıkarım özeti (Takeaway)

Satıcılarda iki model var: hiyerarşi ve limit tablosu ile grafik iş akışı tasarımcısı. Bulunan arıza kayıtları kişiye bağlı onaycı ve adım adım kurulan akışlar çevresinde toplanıyor: ayrılan onaycıda sessizce bekleyen onay, kendine dönen eskalasyon, çakışan vekâlet kuralı, yetkisi eksik vekil, platform zaman aşımı. En yalın model tek, sürümlü bir kural tablosu, izin adına bağlı onaycı, süreli vekâlet kaydı ve "sahipsiz onay" korumasıdır.

### Kaynaklı bulgular (Cited Findings)

- NetSuite iki yolu birlikte sunuyor: amir ya da onaycı hiyerarşisi ve onay limitiyle çalışan "Approval Routing" ve özel SuiteFlow iş akışı. Satın almada iş akışı tabanlı onaya geçiş ayrıca belgelenmiş — [NetSuite: Approval Routing](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N2395258.html), [SuiteFlow'a geçiş](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_3960256538.html) (C; satın almada hiyerarşik yönlendirmenin sürüp sürmediği konusunda belgeler çelişkili görünüyor)
- Dynamics 365 Finance'ta süre ve eskalasyon her adımda ayrı ayarlanır — [Microsoft Learn](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/fin-ops/organization-administration/configure-manual-task-workflow) (C)
- Ayrılan onaycı, ServiceNow topluluğu: işten ayrılmış yöneticiye giden talep için "the workflow doesn't fail, nobody gets notified, no emails get sent". Önerilen: kişi yerine onay grubu ve onaycı pasifse amire ya da yedek gruba otomatik yönlendirme — [ServiceNow topluluk](https://www.servicenow.com/community/servicenow-ai-platform-forum/approach-to-inactive-approvers/m-p/1233796) (C; topluluk)
- Atlassian topluluğu, "The Approver Left the Company, Jira Kept Waiting": açık onayların devri işten çıkış sürecine eklenmeli — [Atlassian topluluk](https://community.atlassian.com/forums/App-Central-articles/The-Approver-Left-the-Company-Jira-Kept-Waiting/ba-p/3281266) (C; topluluk)
- Oracle Cloud: pasif hâle gelen çalışanların bekleyen onaylarını yeniden atayan zamanlanmış süreç vardır; günde en az bir kez çalışacak biçimde kurulabilir — [Oracle Financials 25A](https://docs.oracle.com/en/cloud/saas/financials/25a/fafcf/how-you-can-regularly-reassign-pending-approvals-for-workers.html), [Oracle HCM](https://docs.oracle.com/en/cloud/saas/human-resources/fawhr/how-you-reassign-pending-approvals-and-invalid-supervisor.html) (C)
- SAP: bekleyen onay pasif kullanıcıya yönlendirilemez: "The newly assigned approver has to be an Active user in the system." — SAP bilgi bankası, arama özeti; aday bağlantı: [KBA 2698630](https://userapps.support.sap.com/sap/support/knowledge/en/2698630) (C)
- ServiceNow: süren kayıtta onaycıyı değiştirmek akışı ilerletmeyebiliyor; bir kullanıcı pasif hesabı geçici olarak açıp onun adına onaylamak zorunda kaldığını yazıyor. Önerilen yol isteği iptal edip yeniden başlatmak — [ServiceNow topluluk](https://www.servicenow.com/community/member-feedback-forum/can-we-redirect-approvals/td-p/3470337/page/3) (C; tekil anlatı)
- Dynamics topluluğu: hiyerarşi tabanlı eskalasyon satın alma talebi akışında aynı onaycıya geri dönebiliyor — [dynamicsuser.net](https://www.dynamicsuser.net/t/workflow-escalation-to-next-level-in-hierarchy/68074) (C; tekil anlatı)
- SAP My Inbox: aynı vekille plansız vekâlet açılınca mevcut planlı kural kaybolabiliyor; vekil "Open Task" düğmesinde yetki hatası alabiliyor — SAP bilgi bankası, arama özeti; aday bağlantılar: [KBA 3020379](https://userapps.support.sap.com/sap/support/knowledge/en/3020379), [KBA 2934221](https://userapps.support.sap.com/sap/support/knowledge/en/2934221) (C; eşleme doğrulanamadı)
- Power Automate: yaklaşık 30 günlük akış zaman aşımı çok adımlı onayı düşürür; resmî kaynaklarda yerleşik eskalasyon bulunamadı — [Microsoft Learn modülü](https://learn.microsoft.com/en-us/learn/modules/approvals-power-automate/5-issues-limits) (C)
- İkincil kanallar vekâleti taşımayabiliyor: bir sohbet asistanı entegrasyonu ServiceNow vekil onaylarını desteklemediğini belgeliyor — [Moveworks](https://help.moveworks.com/service-management/approval-mirroring/enterprise-approvals-servicenow) (C)
- Süreç madenciliği, onay adımının gecikme yarattığı yerleri ve atlanan onayları görünür kılmak için kullanılıyor — [ServiceNow süreç madenciliği yazısı](https://www.servicenow.com/community/process-mining-blog/how-to-use-process-mining-to-identify-approval-bottlenecks-on/ba-p/2713328) (C)
- Repo içi: Ayarlar'da sürümlü ve denetimli politika altyapısı (hazırla / onayla / yayınla izinleri, geçerlilik tarihi, kullanılan sürümün snapshot'ı) zaten var — `docs/decisions/SINAN_KARARLARI_2026-10-21.md` (No 133, K17, K19–K20)

### Çıkarımlar (Inferences): en yalın yapılandırma modeli (D; bu notun önerisi)

**Tek kural tablosu, Ayarlar'da bir politika ailesi olarak.** Her satır bir onay türü ve şirket içindir:

| Alan | Anlamı | Not |
|---|---|---|
| Onay türü | Kodda kayıtlı adaptörün kimliği | Yeni tür kodla gelir, tabloyla değil |
| Şirket | `CompanyId` | Her şirket kendi satırı |
| Koşul bandı | Mevcut eşik politikasına referans | Değer bu tabloda yazılmaz; eşik kendi politikasında durur |
| Onaylayan | İzin adı | Kişi adı yazılmaz |
| İkinci onaylayan | İzin adı ya da boş | Yalnız açıkça işaretli türlerde |
| SLA süresi | İş saati ya da iş günü | Başlangıç 2 iş günü (No 125) |
| Hatırlatma | SLA'nın yüzde kaçında, bir kez | Parametre |
| Eskalasyon hedefi | İzin adı | No 125 böyle tanımlıyor |
| Zaman aşımı eylemi | Yalnız "yükselt" | Otomatik onay ya da red yok |
| Toplu onaya uygun | Evet / hayır | Varsayılan hayır |
| Bildirim sınıfı | Şimdi / özet | Varsayılan özet |
| Onaysız akabilir | Evet / hayır, hangi koşulda | Açılması Sinan kararı; örneklem oranıyla birlikte |

**Ayrı kayıt: vekâlet.** Kimden kime, başlangıç ve bitiş (zorunlu, üst sınır parametre), kapsanan onay türleri, durum (planlı / hazır ama pasif), isteğe bağlı devir notu. No 125 ile aynı.

**Arıza korumaları (bulunan arıza kayıtlarından türetildi):**

1. Onaycı kişi değil izin adıdır. Ayrılan kişi sorunu büyük ölçüde kendiliğinden çözülür.
2. Sahipsiz onay taraması: uygun onaycı yoksa (hesap pasif, herkes vekâletsiz izinde, tek uygun kişi hazırlayanın kendisi) iş beklemeden eskalasyon hedefine gider ve "sahipsiz onay" istisnası açılır. Hedef sayı sıfırdır.
3. Kullanıcıyı pasifleştirme komutu açık onay, vekâlet ve görev sayısını gösterir ve devri ister.
4. Eskalasyonda döngü koruması: hedef mevcut onaycıyla ya da hazırlayanla aynı kişiye çıkıyorsa bir üst hedefe geçilir; hedef kalmadıysa yönetici istisnası açılır.
5. Vekâlette zincirleme devir yoktur; vekil kendi hazırladığı kaydı onaylayamaz (hazırlayan ≠ onaylayan korunur); vekil devredenin iznini ve limitini aşamaz (NetSuite kalıbı); karar kaydında "X adına Y" ve vekâlet kimliği yazılır; devreden işi görmeye devam eder. Vekilin o izne zaten sahip olması gerekip gerekmediği açık soru 5'tedir.
6. Bütün kanallar (kutu, kayıt ekranı, ileride mobil) aynı karar komutundan geçer; kanal başına ayrı kural yorumu oluşmaz.
7. Kural değişikliği mevcut politika akışından geçer (hazırla, onayla, yayınla; geçerlilik tarihi). Bekleyen işte hangi kural sürümünün uygulandığı kayda yazılır.
8. Simülasyon: yeni kural sürümü yayından önce son N günün kayıtlarına gölge modda uygulanır ve kaç kaydın onaya düşeceğini, kime gideceğini, kaç SLA ihlali olacağını gösterir.
9. Grafik tasarımcı ve serbest koşul dili yoktur. Koşul, mevcut eşik politikalarına referanstır.

Bu model vekâleti, eskalasyonu ve SLA'yı kapsar; kapsamadığı şeyler (paralel onay, oylama, koşullu dallanma, adım başına farklı süre) bilerek dışarıda bırakılmıştır.

### Boşluklar (Gaps)

- Kural tablosu ile grafik tasarımcıyı bakım maliyeti ya da hata oranı açısından karşılaştıran bağımsız çalışma bulunamadı.
- Vekâletin kötüye kullanımı (ör. kendi talebini vekil üzerinden onaylatma) üzerine denetim bulgusu aranamadı (bütçe).
- Bakımsız kalan iş akışlarının yaygınlığına dair veri bulunamadı.
- Kural simülasyonu ve test özellikleri için ürün belgesi aranamadı.

## 6. İletişim: belgeye bağlı mesajlaşma yeter mi, ekipler WhatsApp ve e-postaya döner mi; Türkiye'de saklama ve hukuk (M)

### Çıkarım özeti (Takeaway)

Anketler çalışanların iş için WhatsApp'ı yaygın kullandığını gösteriyor; ancak bunlar satıcı anketleridir ve bölgeye göre değişir. Belgeye bağlı ERP mesajlaşmasının bu kullanımı tek başına azalttığını gösteren kanıt bulunamadı. Gerçekçi hedef sohbeti ERP'ye taşımak değil, kararı ve gerekçeyi kayda bağlamaktır. Türkiye'de saklama süresi ve çalışan yazışmalarının görülmesi konuları teyit ister (M).

### Kaynaklı bulgular (Cited Findings)

**Kullanım ve satıcı yönelimi**

- KnowBe4 Afrika anketi 2025 (800 katılımcı, sekiz ülke): katılımcıların %93'ü iş için WhatsApp kullanıyor. Güney Afrika'da WhatsApp %89, e-posta %88, Microsoft Teams gibi kurumsal platformlar %45 — [IOL](https://iol.co.za/technology/2025-07-24-chats-hacks-and-cyber-traps-when-whatsapp-groups-become-serious-cyber-risk-zones/) (C; güvenlik eğitimi satıcısı)
- Flip, Birleşik Krallık 2025 (perakende, üretim gibi sektörlerde saha çalışanları): üç çalışandan biri iş iletişiminde WhatsApp ve Telegram gibi uygulamalara dayanıyor; bunların %65'i işverenin bilgisi dahilinde — [HRreview](https://hrreview.co.uk/hr-news/one-in-three-uk-workers-use-unsecure-messaging-apps-for-work/379683) (C; iletişim yazılımı satıcısı)
- Wire 2026 raporu: kuruluşların %42'si iş birliği için WhatsApp, Signal ya da benzeri tüketici uygulamaları kullanıyor — [Wire](https://wire.com/en/blog/whatsapp-enterprise-governance-risk) (C; güvenli mesajlaşma satıcısı)
- Salesforce kayda bağlı konuşmayı Slack'e taşıyor; yeni org'larda Chatter varsayılan olarak kapalı geliyor (üçüncü taraf yazı) — [Rapidi](https://www.rapidionline.com/blog/salesforce-chatter-to-slack) (C)
- Odoo chatter iç not ile takipçilere giden mesajı ayırıyor ve etiketlemeyi destekliyor — [Odoo 19 chatter](https://www.odoo.com/documentation/19.0/applications/productivity/discuss/chatter.html) (C)
- Repo içi: müşteri imzalı sipariş onayını e-posta ya da WhatsApp ile iletiyor (YOL-D11); WhatsApp yalnız kullanıcının başlattığı paylaşımdır, API entegrasyonu yoktur (DEC-DOC-007); e-postayı yapay zekâ taslaklar, kullanıcı onayıyla gider (DEC-DOC-006) — `docs/decisions/SINAN_KARARLARI_2026-11-22_FIKIR_HAVUZU.md`, `docs/decisions/KARAR_SICILI.md`

**Türkiye: saklama ve yazışmaların görülmesi (hepsi M; hukukçu ya da mali müşavir teyidi gerekir)**

- TTK m.82: tacir aldığı ticari mektupları ve gönderdiği ticari mektupların suretlerini saklar; kaynaklara göre "ticari mektuplar, bir ticari işe ilişkin tüm yazışmalardır"; süre TTK m.82/5'e göre 10 yıl, VUK m.253'e göre 5 yıl — [Alomaliye](https://www.alomaliye.com/2013/11/22/defter-ve-belgelerin-saklanmasinda-zaman-asimi-sureleri/), [MuhasebeNews](https://www.muhasebenews.com/?p=18783), [VUK m.253–256 yazısı (2026)](https://bilalalyar.av.tr/?p=94754) (M)
- Uygulama içi mesajın, WhatsApp yazışmasının ya da e-postanın "ticari mektup" sayılıp sayılmayacağı konusunda arama sonuçlarında görüş bulunamadı. (M; açık soru)
- Anayasa Mahkemesi Genel Kurulu, E.Ü. başvurusu (B. No: 2016/13010, 17.09.2020): önceden bilgilendirme yapılmadan kurumsal e-postaların incelenmesi ve feshe dayanak yapılması, haberleşme hürriyetinin ve kişisel verilerin korunmasını isteme hakkının ihlali sayıldı — [Alomaliye](https://www.alomaliye.com/2020/09/17/aymden-kurumsal-e-posta-incelemesi-karari/), [NTV](https://www.ntv.com.tr/ekonomi/aymden-kurumsal-mail-karari-isverenin-yazismalari-incelemesi-hak-ihlali,IaOhekEpFUSjNDRXryaJIA) (M)
- Anayasa Mahkemesi Birinci Bölüm (B. No: 2018/31036, 12.01.2021): işveren açık bilgilendirme yapmışsa kurumsal e-posta denetimi rıza aranmadan yapılabilir; ihlal bulunmadı — [Alomaliye](https://alomaliye.com/2021/02/05/anayasa-mahkemesi-karari-2018-31036-aciklama/), [Hukuki Haber](https://www.hukukihaber.net/kurumsal-e-posta-hesabinin-isveren-tarafindan-denetlenmesinin-kisisel-verilerin-korunmasini-isteme-hakki-ve-haberlesme-hurriyetini-ihlal-etmedigi) (M)
- KVKK Kurul kararı 2021/1187: eski çalışanın kurumsal e-posta hesabına aydınlatma yapılmadan erişilmesi konu edildi; kararın sonucu arama özetinden doğrulanamadı — [KVKK](https://www.kvkk.gov.tr/Icerik/7269/2021-1187) (M)
- KVKK'ya atıfla: işverenin yalnız trafik ya da log kaydını incelemesi de kişisel veri işlemedir; amaca daha az müdahaleli bir yolla ulaşılabiliyorsa yazışma içeriğine erişilmemelidir — [Hukuki Haber](https://www.hukukihaber.net/kvkkdan-onemli-kurumsal-e-posta-karari) (M; ikincil aktarım)
- Anayasa Mahkemesi (B. No: 2018/34548, 28.12.2021): çalışanın WhatsApp yazışmalarına dayanılarak iş akdinin feshi incelendi; kararın sonucu arama özetinden doğrulanamadı — [Hukuki Haber](https://www.hukukihaber.net/anayasa-mahkemesinin-whatsapp-yazismalari-nedeniyle-is-sozlesmesi-feshedilen-calisan-kararina-iliskin-bilgi-notu) (M)
- Repo içi: belge saklamada VUK 5 yıl, TTK 10 yıl; ERP 10 yılı esas alır. KVKK yurt dışı aktarım bildirimi yapay zekâ ve bulut için açık madde olarak kayıtlı — `docs/delivery/ERP_FIKIR_HAVUZU_v1.md` (§2)
- Repo içi: uygulama içi bildirimler 90 gün sonra pasife alınır, kalıcı silme kapalıdır, denetim kaydı ayrıdır ve süresiz kalır (KLN-D114, KLN-D111) — `docs/decisions/SINAN_KARARLARI_2026-11-16_DALGA5_EK25.md`, `docs/decisions/SINAN_KARARLARI_2026-11-16_DALGA4_EK23.md`

### Çıkarımlar (Inferences)

- **Yeter mi?** Karar, gerekçe ve verilen söz için evet; anlık saha koordinasyonu için hayır, o kısım WhatsApp'ta ya da telefonda kalır. Hedef, kararı etkileyen bilginin kayıtta bulunmasıdır; WhatsApp kullanımını sıfırlamak gerçekçi değildir. (D)
- **Taktikler (C/D):**
  - `@bahsetme` kişiye "şimdi" sınıfı bildirim üretir.
  - Kayda derin bağlantı kopyalanıp WhatsApp'a yapıştırılabilir; bağlantı giriş ister ve içerik taşımaz. Bu, DEC-DOC-007 ile uyumludur.
  - "Dışarıdaki yazışmayı kayda not olarak yapıştır" girişi; fikir havuzundaki A.11 (yapıştır → taslak sipariş) ile aynı kapıyı kullanır.
  - Mesajdan görev çıkarma (AI-D20) kullanıcı onayıyla.
  - Telefonda hızlı açılan kayıt sayfası; kutu mobil tarayıcıda kullanılabilir olmalı.
- **Kapsam dar tutulmalı:** yalnız kayda bağlı konu ve bahsetme. Genel sohbet, doğrudan mesaj ve kanal yok. Bu hem yalınlık sağlar hem "özel yazışma" beklentisini azaltır.
- **Aydınlatma (M):** iki Anayasa Mahkemesi kararı arasındaki fark önceden ve açık bilgilendirmedir. Kullanıcılara, kayda bağlı mesajların iş kaydı olduğu, kaydı okuma yetkisi olan herkesin gördüğü, denetim kaydı tutulduğu ve ne kadar saklandığı baştan bildirilmelidir. Metni hukukçu yazmalıdır.
- **Saklama (M):** mesaj bildirim değildir. Bildirim 90 günde pasife düşer; kayda bağlı mesajın ise bağlı olduğu belgenin saklama ailesini izlemesi mantıklıdır (ticari belgede 10 yıl esası). Bu ayrı bir karar ve teyit ister.
- **"Silme yok" değişmezi ile KVKK'nın silme ve anonimleştirme yükümlülüğü arasındaki gerilim (M):** mesaj kutusunda "özel hayat, sağlık gibi kişisel veri yazmayın" uyarısı; düzeltme için "geri çekildi" işareti; imha ayrı politika (OPEN-021).
- **Mobil push (M/D):** telefon bildirimleri işletim sistemi sağlayıcılarının yurt dışındaki altyapısından geçtiği için push içeriği kişisel veri ya da ticari ayrıntı taşımamalıdır ("1 onay bekliyor"). Bu genel bilgidir; bu oturumda kaynak aranamadı. Fikir havuzundaki KVKK yurt dışı aktarım maddesiyle birlikte sorulmalıdır.
- Müşteri ve tedarikçiyle yazışma ERP içi mesajlaşmanın kapsamı dışındadır; e-posta DEC-DOC-006 yolundan gider.

### Boşluklar (Gaps)

- Kayda bağlı mesajlaşmanın WhatsApp ya da e-posta kullanımını azalttığını ölçen çalışma bulunamadı.
- Türkiye'ye özgü "işte WhatsApp kullanımı" oranı bulunamadı; bulunan anketler Afrika, Birleşik Krallık ve küresel satıcı anketleridir.
- TTK m.82'nin elektronik iç yazışmaya uygulanması, KVKK saklama ve imha yönetmeliğinin ayrıntıları, push bildirimi ve yurt dışı aktarım bu oturumda aranamadı ya da doğrulanamadı.
- Başka ülkelerde kayıt dışı mesajlaşma kanalları nedeniyle uygulanan yaptırımlar aranamadı (bütçe).
- Mevzuat maddelerinin tamamı ikincil kaynaklardan (mali ve hukuk siteleri) alınmıştır; mevzuat.gov.tr ve karar metinleri okunmadı.

## 7. Çıktı: değer / efor sırasına göre yalın özellik seti, başlangıç parametreleri ve izlenecek metrikler

### Çıkarım özeti (Takeaway)

Yalın çekirdek altı kalemdir: kutudan karar ve bütün onay türleri, tek kural tablosu, SLA ile tek hatırlatma ve eskalasyon, vekâlet, iki sınıflı bildirim ve ölçüm. Kanıtı en güçlü olan tasarım kuralları bildirim tarafındadır (toplu verme, tekrar etmeme) ve yapay zekâ önerisinin yüksek riskte geri planda tutulmasıdır. Sürelerin ve oranların tamamı parametredir; bir kısmı zaten karara bağlıdır (No 125, KLN-D114).

### Kaynaklı bulgular (Cited Findings)

- Zaten karara bağlı parametreler: vekâlet kullanıcıdan kullanıcıya, süre zorunlu, üst sınır başlangıçta 30 gün, işlem türü seçimli; "bilgi iste" askıya alır, "iade" hazırlayana döndürür; eskalasyon süresi başlangıçta 2 iş günü; eskalasyon hedefi izin adıyla tanımlanır — `docs/decisions/SINAN_KARARLARI_2026-10-21.md` (No 125)
- Onay kararı türleri: onay, red, düzeltmeye iade, bilgi iste, koşullu onay; vekâlet, zaman aşımı ve eskalasyon kayıtlıdır — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` (§12.3)
- Bildirim: uygulama içi; e-posta kanalı açılmaz; 90 gün sonra pasif; kalıcı silme kapalı — `docs/decisions/SINAN_KARARLARI_2026-11-16_DALGA4_EK20.md` (KLN-D110), `docs/decisions/SINAN_KARARLARI_2026-11-16_DALGA4_EK23.md` (KLN-D111), `docs/decisions/SINAN_KARARLARI_2026-11-16_DALGA5_EK25.md` (KLN-D114)
- Üretim e-posta sağlayıcısını etkinleştirmek Sinan'da kalır — `docs/decisions/SINAN_KARARLARI_2026-10-21.md` (No 155)
- Yapay zekâ: özet, risk ve gerekçeli öneri; otomatik onay ilk aşamada yok (AI-D13); sabah özeti zamanlı görev olarak (AI-D15); tek iş kutusu (AI-D20); kazanılan otonomi (AI-D21) — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md`
- İstisna öncelikli ana sayfa: her istisnanın sahibi, SLA'sı ve kapanış neden kodu (gerçek / yanlış alarm / eşik yanlış); tek iş kutusu ve istisna bazlı inceleme — `docs/delivery/ERP_FIKIR_HAVUZU_v1.md` (1.2, 1.3)
- Bugünkü kod: 4 onay türü için salt okunur kutu, tek tüketicili kişisel bildirim, ortak onay motoru yok — `docs/delivery/arastirma/2026-11-22_goymen_erp_envanteri.md`, `src/backend/GoymenERP.Api/WorkRuntime/`, `src/backend/Modules/Work/README.md`
- Dış kanıt Soru 1–6'da kaynaklarıyla verilmiştir; aşağıdaki tablolarda soru numarasıyla anılır.

### Çıkarımlar (Inferences)

**7.1 Değer / efor sırasına göre yalın özellik seti.** Efor ölçeği fikir havuzuyla aynıdır: S = 1 dilim, M = 2–4 dilim, L = tren ölçeği. "Kanıt" sütunu kalemin dayandığı en güçlü kanıtı gösterir; satıcı belgesi özelliğin var olduğunu kanıtlar, yararını kanıtlamaz.

| Sıra | Kalem | Ne kazandırır | Değer | Efor | Kanıt | Dayanak |
|---|---|---|---|---|---|---|
| 1 | Kutudan karar ve bütün onay türleri (ortak adaptör sözleşmesi) | Onay bekleyen iş kaybolmaz; karar tek yerden verilir | Yüksek | M–L (tür başına S) | C | Soru 1 |
| 2 | Tek kural tablosu, izin adına bağlı onaycı, sahipsiz onay koruması | "Kimde bekliyor" her zaman bellidir; ayrılan kişide iş kalmaz | Yüksek | M | C (arıza kayıtları) | Soru 5 |
| 3 | SLA sayacı, tek hatırlatma, eskalasyon | Geciken onay kendiliğinden görünür ve yükselir | Yüksek | M | C; süre ölçeği B | Soru 1, 2 |
| 4 | Vekâlet (planlı ve "hazır ama pasif") | İzin ve hastalıkta onay durmaz | Yüksek | S–M | C (beş satıcıda aynı çekirdek) | Soru 1, 5 |
| 5 | İki sınıflı bildirim, tekilleştirme, günlük özet | Daha az kesinti; önemli olan gözden kaçmaz | Yüksek | S–M | A (yön; başka alandan aktarım) | Soru 3 |
| 6 | Ölçüm olayları (ilk dilimden) ve metrik panosu | Değişiklikten önceki durum bilinir; etkisi ölçülür | Orta–Yüksek | S + S–M | B/C | Soru 2 |
| 7 | Önce kanıt gösteren onay kartı (neden düştü, kural sonuçları, hesap izi, ekler) | "Bilgi iste" turları azalır; göstermelik onay zorlaşır | Yüksek | M | C/D | Soru 2, 3 |
| 8 | Yapay zekâ özeti, isteğe bağlı öneri, tek tık itiraz ve neden kodu | Okuma süresi kısalır; aşırı güven sınırlanır | Orta–Yüksek | M (AI treni) | A (risk tarafı), C (özellik) | Soru 4 |
| 9 | İstisna bazlı akış, neden kodlu kapanış, örneklem denetimi | Temiz kayıt beklemez; yanlış alarm ölçülür | Yüksek | M | B/C | Soru 2, 3 |
| 10 | Kayda bağlı görev (atanan, son tarih, "kimde bekliyor") | Sözlü iş takibi kayda geçer | Orta–Yüksek | M | C | Soru 1 |
| 11 | Kayda bağlı not, `@bahsetme`, takipçi | Karar ve gerekçe kayıtta kalır | Orta | M | C | Soru 1, 6 |
| 12 | Dar toplu onay | Sapmasız rutin işte tık sayısı düşer | Orta | S | C; riski D | Soru 1, 3 |
| 13 | Kural simülasyonu (gölge mod) | Kural değişikliği yayından önce denenir | Orta | S–M | D | Soru 5 |
| 14 | Yinelenen görev şablonu ve kontrol listesi (ay sonu, uyum takvimi) | Dönemsel işler unutulmaz | Orta | M | C | Soru 1 |
| 15 | Mobil tarayıcıda kutu ve derin bağlantı | Masadan uzakta onay; WhatsApp'tan kayda dönüş | Orta | S–M | C/D | Soru 1, 6 |
| 16 | İş yükü görünümü (rol başına açık ve geciken iş) | Yığılma görülür, iş yeniden dağıtılır | Düşük–Orta | S | D | Soru 1 |

Önerilen paketleme: 1–6 çekirdektir (fikir havuzu 1.3 ile aynı kalem); 7–9 onay kutusu yardımcısıyla (AI-14) birlikte gelir; 10–11 tek iş kutusu dilimidir (AI-26); 12–16 çekirdek ölçülmeye başladıktan sonra, ölçüm gerektiriyorsa yapılır.

**7.2 Kullanım ve yönetim kolaylığı için ekran ilkeleri (D).**

- Kullanıcı tarafı tek ekran, dört görünüm: "Benden karar bekleyen", "Benim başlattığım (kimde bekliyor)", "Bana atanan görevler ve bahsedildiklerim", "Bilgi". Varsayılan sıralama SLA'ya kalan süredir.
- Yönetim tarafı tek ekran, üç sekme: "Kurallar" (kural tablosu), "Vekâletler", "Geciken ve sahipsiz işler". AI-D20'deki "tek ekrandan yönetilir" maddesinin karşılığıdır.
- Her iş kalemi aynı dört bilgiyi taşır: neden burada, kimde, ne zamana kadar, bir sonraki adım ne.

**7.3 Başlangıç parametreleri.** Hepsi Ayarlar'da tutulur; kodda varsayılan olmaz. "Durum" sütunu değerin karara bağlı mı yoksa bu notun önerisi mi olduğunu gösterir.

| Parametre | Başlangıç önerisi | Dayanak | Durum |
|---|---|---|---|
| Vekâlet süresi üst sınırı | 30 gün | No 125 | Karar var |
| Vekâlet kapsamı | Onay türü seçilerek; "hepsi" varsayılan değil | No 125; Soru 1 (C) | Karar var; ayrıntı öneri |
| Eskalasyon süresi, standart türler | 2 iş günü | No 125 | Karar var |
| Eskalasyon süresi, araç / sevk / üretim bekleten türler | Saat ölçeğinde ayrı sınıf; değer Sinan'da | Soru 2: iyi işleyen onay saatlerle ölçülüyor (B/C) | NEEDS-SINAN |
| Hatırlatma | SLA süresinin yarısında, bir kez | Soru 3: tekrar eden uyarı yok sayılıyor (A yön) | Öneri (D) |
| Zaman aşımı eylemi | Yalnız yükselt; otomatik onay ya da red yok | AI-D13; politika tahminle etkinleşmez | Öneri |
| Eskalasyon basamağı | En çok iki; ikinci hedef de yanıtsızsa "sahipsiz iş" istisnası | Soru 5 (D) | Öneri |
| "Bilgi iste" | SLA sayacını durdurur; cevap için ayrı sayaç, 1 iş günü sonra hazırlayana tek hatırlatma | No 125 (askıya alır); süre D | Kısmen karar |
| SLA takvimi | İş saati ve iş günü, `Europe/Istanbul`, resmî tatiller hariç | K17 ile aynı gün tanımı | Öneri |
| Özet (digest) sıklığı | Günde üç; örnek saatler 09:00, 13:00, 16:30; kullanıcı saati kaydırabilir | Soru 3: Fitz 2019 (A, eski) | Öneri |
| Sabah özeti | İş günü başında bir kez; günlük üç özetten biri sayılır | AI-D15 | Karar var; saat açık |
| "Şimdi" sınıfı | Tabloda "bekleten" işaretli onay türleri, SLA ihlali ve eskalasyon, güvenlik olayı, `@bahsetme` | Soru 3 (A yön); liste D | Öneri; liste NEEDS-SINAN |
| Sessiz saatler | Yalnız push ya da e-posta açılırsa gerekir: mesai dışında ve hafta sonu gönderim yapılmaz, iş kutuda bekler; istisna güvenlik olayı | D; kanıt aranamadı | NEEDS-SINAN |
| Tekilleştirme | Aynı kayıt + aynı alıcı + aynı olay türü tek bildirim; güncelleme mevcut bildirimi yeniler | Soru 3 (A yön) | Öneri |
| Toplu onay | Yalnız tabloda işaretli türler; sapmasız ve eşik altı kayıtlar; tek seferde en çok N kayıt | Soru 1 (C); risk Soru 3 | Öneri; N NEEDS-SINAN |
| Örneklem denetimi | Onaysız akan kayıtların tür bazında yüzde X'i rastgele incelemeye; ilk dönemde yüksek, hata çıkmadıkça düşürülür | D; kanıt aranamadı | NEEDS-SINAN |
| Yapay zekâ önerisinin görünürlüğü | Düşük riskli türlerde açık; ikinci onaylı ve eşik üstü türlerde kapalı başlar, tıklayınca açılır | Soru 4 (A) | Öneri; tür listesi NEEDS-SINAN |
| İtiraz neden kodları | Veri yanlış / kural yanlış / bağlam eksik / risk yanlış / diğer + metin | D | Öneri |
| Kapanış neden kodu | Gerçek sorun / yanlış alarm / eşik yanlış; zorunlu | Fikir havuzu 1.2 | Plan |
| Görev hatırlatması | Son tarihten 1 iş günü önce, bir kez; gecikince atayana tek bildirim | D | Öneri |
| Bildirim saklama | 90 gün sonra pasif | KLN-D114 | Karar var |
| Kayda bağlı mesaj saklama | Bağlı belgenin saklama süresi | Soru 6 (M) | NEEDS-SINAN + hukukçu |

**7.4 İzlenecek metrikler.** Hepsi onay türü, şirket ve rol kesitinde izlenir. Kişi sıralaması yapılmaz; fikir havuzu bireysel lider tablosunu KVKK ve sosyal baskı nedeniyle zaten dışarıda bırakıyor (`docs/delivery/ERP_FIKIR_HAVUZU_v1.md` §5).

| Metrik | Tanım | Ne için |
|---|---|---|
| Onay çevrim süresi | Onaya gönderimden karara; medyan ve P90, iş saati; "bilgi iste" bekleme süresi ayrı | Ana sonuç ölçüsü; kıyasla aynı birim (Soru 2) |
| Dokunuş sayısı | Kayıt başına insan adımı: gönder, bilgi iste, iade, yeniden gönder, karar | Eksik bağlamın göstergesi |
| İlk seferde karar oranı | "Bilgi iste" ya da "iade" olmadan karara bağlanan pay | Onay kartının yeterliliği |
| Onaysız akan kayıt payı | Kurallara göre temiz akan kayıt / toplam kayıt | İstisna bazlı akışın kapsamı; fatura dünyasındaki "touchless" ölçüsünün karşılığı |
| Yanlış alarm payı | "Yanlış alarm" ya da "eşik yanlış" koduyla kapanan / toplam istisna, kural bazında | Eşik kalitesi; kural sahibine geri bildirim |
| SLA ihlal oranı ve eskalasyon oranı | SLA'sı dolan / toplam; yükselen / toplam | Süre parametrelerinin gerçekçiliği |
| Sahipsiz iş sayısı | Uygun onaycısı olmayan iş | Hedef sıfır |
| Vekâletle verilen karar payı | "Adına" verilen karar / toplam | Vekâlet kullanımı ve kötüye kullanım sinyali |
| Hatırlatma etkinliği | Hatırlatmadan sonraki sürede karara bağlanan pay | İkinci hatırlatmaya gerek olup olmadığı |
| Göstermelik onay göstergeleri | Kanıt açılmadan verilen onay payı; çok kısa karar süresi payı; onay oranı; toplu onay payı | Onayın anlamlı kalıp kalmadığı |
| Örneklem denetimi hata oranı | İncelenen onaysız kayıtlarda bulunan hata / incelenen | "Temiz" kuralının güvenilirliği |
| Yapay zekâ ölçüleri | Öneri kabul ve itiraz oranı; sonradan geri alınan karar oranı; özet kaynağına tıklama oranı | AI-D21 için; kabul oranı tek başına kullanılmaz |
| Bildirim yükü | Rol başına günlük "şimdi" ve "özet" sayısı; okunmadan pasife düşen pay; bildirimden işleme geçiş oranı | Gürültü; sınıf atamasının doğruluğu |
| Kayda bağlı iletişim | Konuşması olan kayıt payı; bahsetmeye yanıt süresi | Kullanılıp kullanılmadığı |

Ölçüm olayları çekirdeğin ilk diliminde üretilmelidir; aksi halde "öncesi" değeri olmaz ve etki ölçülemez. Kaynak, zaten var olan audit ve outbox olay günlüğüdür.

### Boşluklar (Gaps)

- Sıralamadaki "değer" sütunu bu notun yargısıdır; onay çevrim süresine etkisi bağımsız olarak ölçülmüş tek bir kalem yoktur. Bulunan ölçümler ya başka alandandır (bildirim, klinik uyarı, laboratuvar deneyi) ya da satıcı kıyasıdır.
- Hatırlatma zamanı (SLA'nın yarısı), özet saatleri, toplu onay sınırı ve örneklem oranı için doğrudan kanıt bulunamadı; bu değerler başlangıç önerisidir ve pilotla ayarlanmalıdır.
- Araç ya da sevk bekleten onay türlerinin listesi ve süreleri iş bilgisidir; dış kaynaktan çıkarılamaz.

## Bizim sisteme uyarlama

| Kalıp | Bugün (kod) | Mevcut karar | Öneri (yalın biçim) | Efor | Kapı / değişmez |
|---|---|---|---|---|---|
| Tek kutu, bütün onay türleri | 4 adaptör, salt okunur (`/api/v1/work/approvals`, `/work/queue`); diğer onaylar modül ekranlarında | AI-D20; fikir havuzu 1.3; AI-14 | Ortak adaptör sözleşmesi bütün türlere yayılır; kutu karar için sahibi modülün komutunu çağırır | M–L | Work başka modülün tablosuna yazmaz; karar komutu sahibinin K3 iskeletinden geçer (yetki, expected version, idempotency, audit) |
| Karar türleri | Onay ve red modül ekranlarında | Master §12.3; No 125 | İlk sürüm: onay, red, iade, bilgi iste. Koşullu onay ertelenir | S–M | Hazırlayan ≠ onaylayan her türde korunur |
| Kural tablosu | Ayarlar'da finans onay matrisi, satış onay eşikleri, görevler ayrılığı politikaları ayrı ayrı | No 133; DEC-SET-001 | Tek "onay yönlendirme" politika ailesi; eşiklere referans verir, eşik değerini kopyalamaz | M | Yayın mevcut politika akışıyla; kritik aileler `publish-critical`; grant insan kapısı |
| Vekâlet | Yok | No 125 | Karardaki biçim + "hazır ama pasif" vekil, zincirleme devir yok, "adına" izi, devredenin limiti geçerli | S–M | Vekâlet yeni yetki vermemeli; bkz. açık soru 5 |
| Eskalasyon ve SLA | Yok | No 125 (2 iş günü; hedef izin adıyla) | + tek hatırlatma, döngü koruması, sahipsiz iş istisnası, iş takvimi | M | Zaman aşımında otomatik karar yok |
| Bildirim | Kişisel uygulama içi bildirim, tekilleştirme kaydı, 90 günde pasif; tek tüketici | KLN-D110, KLN-D111, KLN-D114; No 128; No 155 | Bildirim sınıfı (şimdi / özet), günlük özet, onay / görev / bahsetme olaylarına tüketici | S–M | Outbox dispatcher'ın üretimde açılması insan kapısı; e-posta kapalı kalır |
| İstisna bazlı akış | Yok (planda) | Fikir havuzu 1.2 ve 1.3; AI-D13 | "Temiz" kararını deterministik kural verir; kapanış neden kodu zorunlu; onaysız akanlara örneklem denetimi | M | Eşikler ve "hangi tür onaysız akar" Sinan kararı; kararı yapay zekâ değil kural verir |
| Onay kartı ve yapay zekâ | Yapay zekâ kapalı; iskelet var | AI-D13, AI-D20, AI-D21; AI-14; fikir havuzu A.8 | Kanıt önce; özet kaynağa bağlı; öneri yüksek riskte isteğe bağlı; tek tık itiraz ve neden kodu; sonuç geri bildirimi | M | Yapay zekâ onay komutu çalıştıramaz; veri kuralları (AI-D3, AI-D4) |
| Kayda bağlı görev | Yok | AI-D20; AI-26 | Kayıt bağı, atanan, son tarih, durum (açık / bekliyor / bitti), tek hatırlatma | M | `CompanyId`; görev atamak kayıt okuma yetkisi vermez |
| Kayda bağlı mesaj | Yok | AI-D20; DEC-DOC-006; DEC-DOC-007; benzetme olarak No 154 (belge içeriğini kaydın okuyucusu okur) | İç not, bahsetme, takipçi; görünürlük kaydın okuyucularıyla sınırlı; genel sohbet yok | M | Saklama ailesi ve aydınlatma metni (M); silme yok, "geri çekildi" işareti |
| Toplu onay | Yok | — | Yalnız işaretli türler, sapmasız kayıtlar, N sınırı; her kayıt ayrı denetim satırı | S | İkinci onaylı ve eşik üstü türlerde kapalı |
| Yinelenen görev ve kontrol listesi | Yok | Fikir havuzu 1.10 (uyum takvimi) | Şablon dönem başında görev üretir; hazırlayan ve gözden geçiren imzası; bağımlılık yok | M | Yasal süreler teyitle (M) |
| Metrikler | Audit ve outbox olay günlüğü var; pano yok | Fikir havuzu 1.2; AI-D21 | Olaylardan türeyen pano; onay türü, şirket ve rol kesiti | S–M | Kişi sıralaması yok (KVKK) |
| Mobil, e-posta, push | Hedef Web/PWA (master §13) | KLN-D110 (e-posta kapalı); No 155 | Kutu mobil tarayıcıda çalışır; e-posta ve push ayrı karar | S–M | Sağlayıcı etkinleştirme insan kapısı; yurt dışı aktarım (M) |

## Önerilmeyenler / fazlalık

- **Grafik iş akışı tasarımcısı (sürükle-bırak, BPMN).** Üç şirketlik yapı için fazladır; bulunan arıza kayıtlarının bir bölümü adım adım kurulan akışlardan geliyor (Soru 5), karşılaştırmalı ölçüm ise yok. Kural tablosu yeter.
- **Paralel onay, oylama, yüzdeli onay ve üç ya da daha çok seviyeli zincir.** Süreyi uzatır; ikinci gözün katkısı hesap verebilirlik dağıldıkça azalıyor (Soru 3), üçüncü gözün yararına dair kanıt bulunamadı.
- **E-postayı yanıtlayarak ya da e-postadaki düğmeyle onay.** En kırılgan kanal (Soru 1); bizde e-posta kanalı kapalı (KLN-D110). E-posta ileride açılırsa yalnız kayda derin bağlantı taşımalı.
- **Zaman aşımında otomatik onay ya da otomatik red.** Politika seçmek demektir; yalnız yükseltme önerilir.
- **Yapay zekânın önerisine göre tek tıkla ya da otomatik onay.** AI-D13 ilk aşamada kapatıyor; kanıt da karar görevinde temkin gerektiriyor (Soru 4). Yolu AI-D21'dir.
- **Genel sohbet: doğrudan mesaj, kanal, grup, "yazıyor…", okundu bilgisi, emoji tepkisi.** Kayda bağlı olmayan yazışma hem saklama hem gizlilik yükü getirir (Soru 6); WhatsApp ile yarışamaz.
- **WhatsApp, Slack ya da Teams entegrasyonu.** DEC-DOC-007; sağlayıcı ve yurt dışı aktarım yükü.
- **Birden çok hatırlatma ve her olay için anlık bildirim.** Tekrar eden uyarı yok sayılıyor (Soru 3).
- **Kullanıcı başına ayrıntılı bildirim ayar matrisi.** Yönetimi zorlaştırır; iki sınıf ve özet saati yeter.
- **Kullanıcının kendi "şu olursa şunu yap" otomasyonunu yazması.** Bakımsız kural yığını ve yetki riski; kurallar politika akışından geçer.
- **Zincirleme ya da süresiz vekâlet.** Sorumluluk izini bozar; No 125 süreyi zorunlu tutuyor.
- **Kişi bazlı onay hızı sıralaması ya da oyunlaştırma.** Hızı ödüllendirmek göstermelik onayı artırır; KVKK ve sosyal baskı.
- **Tam iş yönetimi paketi: proje, Gantt, alt görev ağacı, zaman takibi, görev bağımlılıkları.** Kayda bağlı tek seviyeli görev yeter.
- **Koşullu onay (ilk sürümde).** Koşulun takibi ayrı bir iş akışı ister; "iade + not" aynı ihtiyacı karşılar.
- **Ayrı yerel mobil uygulama (ilk sürümde).** Mobil tarayıcıda çalışan kutu yeter; push ayrı karar.
- **Sohbet asistanı içinden onay verme.** İkincil kanallar vekâleti ve kuralı taşımayabiliyor (Soru 5); onay yalnız karar komutunun bulunduğu ekrandan verilir.

## Sinan için açık sorular

1. **Onay türleri listesi.** Kutuya bağlanacak bütün onay türleri hangileri, öncelik sırası ne? (Bugün 4 tür bağlı.)
2. **Süre sınıfları.** Bütün türlerde eskalasyon 2 iş günü mü kalsın, yoksa araç, sevk ya da üretim bekleten türler için saatlik ayrı bir sınıf mı olsun? Hangi türler "bekleten" sayılır?
3. **Zaman aşımı.** Süre dolunca yalnız yükseltme mi olsun (öneri), yoksa bazı türlerde otomatik iade mi?
4. **Eskalasyon hedefleri.** Tür bazında hangi izin adı hedef olsun; son basamak kim; o da yanıt vermezse ne olsun?
5. **Vekil kim olabilir.** Vekil yalnız o onay iznine zaten sahip kişiler arasından mı seçilsin (öneri; yeni yetki doğmaz), yoksa vekâlet izni süreli olarak başkasına mı taşısın? İkincisi fiilen yetki vermektir ve insan kapısına girer.
6. **Vekâleti kim açar.** Kullanıcı kendi vekilini kendisi mi seçer, yönetici mi onaylar? Plansız yoklukta (hastalık) "hazır ama pasif" vekâleti kim etkinleştirir?
7. **Onaysız akış.** Hangi onay türlerinde temiz kayıt onaysız akabilir, eşikler ne olsun, örneklem denetimi oranı ne olsun ve örneklemi kim incelesin?
8. **Toplu onay.** Hangi türlerde açılsın, tek seferde en çok kaç kayıt?
9. **Bildirim kanalları.** E-posta kanalı kapalı kalsın mı (KLN-D110)? Telefon için push istenir mi? Mesai saatleri ve sessiz saatler ne; sessiz saati hangi olaylar delebilir?
10. **Özet saatleri.** Günde üç özet uygun mu, saatleri ne olsun; sabah özeti (AI-D15) kaçta gelsin?
11. **Yapay zekâ önerisi.** Hangi onay türlerinde öneri kapalı başlasın (tıklayınca açılsın)?
12. **Mesaj saklama (M).** Kayda bağlı mesajlar bağlı belgenin süresi kadar mı saklansın (ticari belgede 10 yıl esası)? Hukukçu ya da mali müşavir teyidi istenir.
13. **Aydınlatma (M).** Kullanıcılara "kayda bağlı mesajlar iş kaydıdır, kaydı okuyabilen herkes görür, denetim kaydı tutulur" bilgilendirmesi yapılacak mı; metni kim hazırlayacak?
14. **Bugünkü iletişim.** Şirket içinde iş yazışması bugün ağırlıkla nerede yürüyor: WhatsApp grupları mı, e-posta mı, kurumsal bir sohbet aracı mı? Kayda bağlı mesajlaşmadan beklenti bunların yerine geçmesi mi, yoksa kararın kayda bağlanması mı?
15. **İş yükü görünümü.** Kim görsün; kişi bazlı sayılar gösterilsin mi (öneri: rol bazlı, kişi sıralaması yok)?
16. **Çok şirket.** Üç şirkette de yetkisi olan kullanıcı onayları tek listede şirket etiketiyle mi görsün, yoksa etkin şirkete göre mi?
17. **Koşullu onay.** Master rapordaki "koşullu onay" ilk sürümde gerekli mi (öneri: ertele)?
