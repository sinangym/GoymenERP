<!-- Ajan araştırma çıktısı (22 Kasım 2026); karar değildir. Birleşik değerlendirme: docs/delivery/ERP_FIKIR_HAVUZU_v1.md -->

# Ana akım dışı ERP ve iş fikirleri: tarama ve pilot önerisi (22 Kasım 2026)

Kapsam: salt okuma ve web araması yaptım. Repoda dosya değişmedi, commit veya push yok. Bu belge `docs/delivery/ERP_BOSLUK_ANALIZI_v1.md` belgesini tamamlar. Oradaki yöntemleri tekrar etmedim: cep marjı, taban/hedef bandı, kazanma/kaybetme analizi, mükerrer fatura, yaşlanan stok, tahsilat önceliği, tek iş kutusu, kayıtlı görünüm, artık parçayla kesim eşleştirme ve alarm yorgunluğu. Burada önerilenlerin çoğu bunların **üstüne** kurulur.

**Kanıt düzeyleri:**
- **A:** hakemli çalışma (tek vaka veya laboratuvar çalışması olabilir, ayrıca belirtilir)
- **B:** analist veya danışman kıyası
- **C:** satıcı vakası veya basın
- **D:** spekülatif veya anekdot

**[İDDİA]** işareti, kaynağın kendi beyanı olan ve bağımsız doğrulanmamış rakamları gösterir. Benim rakamım yoktur; bütün sayılar kaynaktandır. Birincil metinlerin çoğunu tam okuyamadım, değerlendirmeler özet ve abstract düzeyindedir. Türk hukuku notları (Rekabet Kanunu, transfer fiyatlandırması, KVKK) **M düzeyindedir: avukat veya mali müşavir teyidi gerekir.**

Repoda doğrulanan bağlam:
- **DEC-DOC-007 (KİLİTLİ):** WhatsApp ilk fazda yalnız kullanıcının başlattığı paylaşım olarak kullanılır. Business/API entegrasyonu yoktur.
- Repoda temporal table, command palette veya karar günlüğü izi yok.
- "Remnant/offcut" yalnız üretim çıktı sınıflandırmasında geçiyor.

---

## 1. ERP mimarisi ve UX fikirleri

| # | Fikir | Kaynak / kim savunuyor | Ne iddia ediliyor | Kanıt | Bizim işe uyarlama | Risk / başarısızlık biçimi | Efor | URL |
|---|---|---|---|---|---|---|---|---|
| A1 | **İstisna öncelikli ("inbox-zero") ana sayfa:** her şey yolundaysa ekran boştur | AWS'nin SAP ajan rehberi, NetSuite Exception Management, Ramco | İnsan yalnız sapmaya müdahale eder. Ajanlar "danışman → denetimli → otonom" sırasıyla ilerler. NetSuite yaklaşık 18 aylık geçmiş veri istiyor [İDDİA] | C. Satıcı belgeleri; bağımsız ölçüm yok | Boşluk analizindeki "sayaç + tek kutu" fikrini bir adım ileri taşır. Ana sayfa varsayılan olarak **yalnız sahibi ve SLA'sı olan istisnaları** gösterir. Kapatırken **kapanış nedeni kodu** zorunludur (gerçek sorun / yanlış alarm / eşik yanlış). "Yanlış alarm" oranı kural sahibine geri bildirim olur. İlk üç istisna: kantar farkı, taban fiyat altı, sertifikası eksik sevk | Sessiz başarısızlık: kural tanımlanmamış bir sorun hiç görünmez. Eşikler tahminle konursa alarm yorgunluğu doğar | M | docs.amazonaws.cn/sap/latest/general/rise-agenticai-erpexceptions.html · docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/article_5094345081.html |
| A2 | **"Bu sayıyı açıkla":** her sayının kökenine tıklanır | Journal of Accountancy (tıklanabilir finansal tablolar), Sage Intacct ve Thomson Reuters detaya inme | Tablodaki sayıdan kaynak fişe inilir. Güven ve mutabakat hızı artar | Detaya inme için B/C. "Her hesaplanan sayı için hesap izi" fikri D | **Deterministik hesaplayıcı değişmezine en iyi uyan fikir.** Hesaplayıcı sonucu yanında yapılandırılmış bir **iz** döndürür: girdiler, formül sürümü, yuvarlama adımları, kaynak belge kimlikleri. UI bu izi yalnız gösterir, yeniden hesaplamaz. İlk hedef: satır marjı, üretim maliyeti, kütle dengesi | İz şişebilir ve performansı düşürebilir. UI'ın izi kendisi yeniden hesaplamaya başlaması değişmez ihlalidir. İz, yetkisiz kullanıcıya maliyet sızdırabilir | M | journalofaccountancy.com/issues/2017/nov/drillable-financial-statements/ · intacct.com/ia/docs/en_US/help_action/Reporting/Distribute_and_run_reports/Run_reports/drill-down-txn-info.htm |
| A3 | **Uyarı yerine "geri al"** ("undo everywhere") | Aza Raskin, A List Apart (2007); Gmail'in "gönderimi geri al" deseni | Kullanıcı onay penceresini okumadan "Tamam"a basar. Onay hatayı önlemez, geri alma kurtarır | D/C. Tasarım argümanı ve yaygın pratik; kontrollü ERP çalışması yok | Kesinleşmiş para/stok olayı geri alınamaz, yalnız ters kayıt yapılır. Uyarlama iki katmanlıdır. (1) **Bekleme penceresi:** dış etki (e-posta, paylaşım, entegratör gönderimi) outbox'ta N saniye bekler; "geri al" kuyruğu iptal eder. (2) Kesinleşmiş kayıtta "geri al" = **önceden doldurulmuş ters kayıt taslağı**, normal yetki ve onaydan geçer | "Geri al" sessiz silme gibi uygulanırsa audit ve "kopya hareket yok" ilkesi bozulur | S–M | alistapart.com/article/neveruseawarning |
| A4 | **Zaman yolculuğu:** "bu cari kartı 3 Kasım'da nasıldı?" | SQL Server sistem sürümlü temporal tablolar, EF Core `TemporalAsOf` | `FOR SYSTEM_TIME AS OF` ile geçmiş durum sorgulanır; ilişkili tablolar aynı ana göre birleşir | Teknoloji olgun (C, satıcı belgesi). İş değeri D | Okuma amaçlı "o tarihteki hali" görünümü. Önce kredi limiti/risk, fiyat listesi ve ürün kartında. Anlaşmazlık çözümünde ("teklif verdiğimizde liste fiyatı neydi?") kullanılır. Alternatif: audit ve outbox olaylarından yeniden kurma | Migration K3 kapsamına girer. Geçmiş tablosu büyür. Saklama/imha politikası (OPEN-018/021) ve KVKK ile çakışabilir. UTC/yerel saat tuzağı var | M | learn.microsoft.com/en-us/sql/relational-databases/tables/temporal-tables · learn.microsoft.com/en-us/ef/core/providers/sql-server/temporal-tables |
| A5 | **Tamamen olay kaynaklı (event-sourced) ERP** | Microsoft CQRS rehberi; CLEI 2018 ERP vakası; eleştiri: V. Voss | Muhasebe zaten olay günlüğüdür; audit doğal olarak gelir ve hata izlenebilirliği artar | C/D. Tek vaka; maliyeti (şema evrimi, projeksiyon gecikmesi) iyi belgelenmiş | Bizde outbox, audit ve correlation zaten var. Tam olay kaynaklı mimariye geçiş önerilmez (bkz. §6). Yalnız stok defteri gibi zaten ekleme-yalnız olan yerlerde mevcut desen korunur | Yeniden yazım maliyeti, projeksiyon hataları, ekipte bilgi eksikliği | L | learn.microsoft.com/en-gb/previous-versions/msp-n-p/jj591559(v=pandp.10) · vivianvoss.net/blog/the-event-sourcing-tax |
| A6 | **Klavye öncelikli kullanım / komut paleti (Ctrl+K)** | Lane, Napier ve ark. (IJHCI 2005); Linear, Superhuman | Öğrenildikten sonra kısayol menüden hızlıdır, ama kullanıcıların çoğu geçiş yapmaz | Hız farkı için A. Linear'ın üretkenlik iddiası C | React kabuğuna Ctrl+K paleti: "cari bul", "yeni teklif", "etiket no'ya git", "son 10 belge". Palet **yalnız yetkili komutları** listeler (shell capability registry). Ölçüm telemetrisi kişisel performans izlemeye dönmez | Kimse kullanmazsa ölü özellik olur. Yetkisiz komutun listede görünmesi bilgi sızıntısıdır | S | scitepress.org/Papers/2019/84945/84945.pdf · linear.app/features/level-up |
| A7 | **Sohbetle çalışan / arayüzsüz ("zero-UI") ERP** | MDPI Computers 2026 (Odoo + RAG, Tayland KOBİ); satıcılar | Doğal dille erişim beş adımı ikiye indirir | A ama tek saha. Yenilik etkisi ve doğruluk kaygısı raporlanmış. 17 kişilik bir tezde kullanıcılar menü botunu sohbete tercih etmiş | Yalnız **okuma, arama ve taslak** için. Yazma komutu sohbetten değil normal ekrandan ve onaydan geçer. Bu, AI treni planıyla aynı yöndür | Yanlış ama kendinden emin yanıt. Kullanıcı ekranları öğrenmeyi bırakır | (AI treni) | mdpi.com/2073-431X/15/7/427 · core.ac.uk/download/237427409.pdf |
| A8 | **Birleştirilebilir (composable) / headless ERP** | Gartner ("postmodern ERP" 2013 → "composable"); Gartner'ın "10 Ugly Truths" raporu | Paket bileşenler serbestçe takılıp çıkarılır, çeviklik artar | B. Gartner'ın kendisi bile 2025'te modüler stratejilerin beklenen değeri vermediğini yazıyor | Uymaz. Modüler monolit doğru seçim (bkz. §6) | Entegrasyon yükü ve veri tutarsızlığı | L | techtarget.com/searcherp/podcast/Evolution-of-postmodern-ERP-to-composable-ERP-explained |
| A9 | **"ERP bir tablo (spreadsheet) gibi"** | Airtable/Rows tarzı ürünler; HN kültürü | Kullanıcı ızgarada serbestçe düzenler, formül yazar, hızlanır | Yararı için D. **Risk tarafı A:** Panko'nun saha denetimleri yeni denetimlerde tabloların %86–94'ünde hata buldu; karar değiştiren hata yaklaşık %5 | Yalnız **ızgara biçiminde toplu giriş** (fiyat listesi, paket standardı, sayım). Her hücre backend doğrulamasından geçer, kaydetme tek idempotent komuttur. **UI'da formül yok** | Formül UI'a girerse deterministik hesaplayıcı ilkesi çöker | M | arxiv.org/pdf/0802.3457 |
| A10 | **Yerel öncelikli / çevrimdışı öncelikli** (local-first, offline-first) | Ink & Switch (Kleppmann ve ark., Onward! 2019) | Veri cihazda yaşar; CRDT ile çakışmasız eşitlenir | A. Kavramsal ve prototip düzeyinde; kurumsal depo vakası bulamadım | Depo/hat başı PWA için **çevrimdışı kuyruk**: okutma ve sayım taslağı cihazda bekler, her biri idempotency anahtarıyla sunucuya gider, karar sunucuda verilir. Stok ve para için CRDT **yok** | CRDT'nin "son yazan kazanır" mantığı stokta çift hareket veya kayıp üretir | M | inkandswitch.com/local-first/ |
| A11 | **Kendi audit/outbox günlüğümüzle süreç madenciliği** (process mining) | TUM KOBİ odak grubu; JEIM 2018 üretim planlama vakası; Politecnico di Milano satınalma–ödeme vakası | Gerçek süreç varyantları, döngüler ve beklemeler görünür. KOBİ'lerde beklenen fayda gerçekleşmiş, dokümantasyon yükü azalmış | A. Nitel, tek vaka ağırlıklı. Satıcı ROI rakamları C [İDDİA] | **Bizde olay günlüğü bedava:** correlation ve causation kimlikleri var. Teklif → sipariş → sevk → fatura → tahsilat zinciri dışa aktarılıp çevrimdışı analiz edilir (ör. pm4py). Sorular: onaydan sonra fiyat kaç kez değişiyor, teklif yanıt süresi ile kazanma ilişkisi ne | Kişi bazlı performans izlemesine kayarsa KVKK ve çalışan güveni sorunu çıkar. Dışa aktarılan veride şirket filtresi korunmalı | S–M | i17vm1.in.tum.de/zotero/PIAFAAI7.pdf · emeraldinsight.com/doi/full/10.1108/JEIM-01-2017-0003 |
| A12 | **Organizasyonun dijital ikizi** (DTO) | Gartner (2017 tanım; 2024 Market Guide, 2026 MQ) | Organizasyonun simülasyon modeliyle karar alınır | B, ve hype. Bağımsız sonuç kanıtı yok | Önerilmez. Boşluk analizi de önermiyor | Model bakımı gerçeğin gerisinde kalır | L | techtarget.com/data-technologies/definition/What-is-decision-intelligence |
| A13 | **Dürtme (nudge): varsayılan değer olarak model fiyatı** | Karlinsky-Shichor ve Netzer, *Marketing Science* 2024 (ABD B2B **alüminyum** satıcısında saha deneyi) | Satışçıya modelin fiyatı gerçek zamanlı gösterildiğinde işlenen tekliflerin kârı kontrol grubuna göre yaklaşık %11 arttı. Olağandışı ve karmaşık tekliflerde insan daha iyi; karma (hibrit) yönlendirme ikisinden de iyi | **A, ve sektöre çok yakın** | Boşluk analizindeki bant fikrinden farklıdır: bant **sınır** koyar, bu fikir **referans gösterir**. Önce "gölge" mod: satışçının fiyatı ile backend'in önerdiği fiyat geriye dönük karşılaştırılır. Sonra teklif bazında rastgele "öneri göster / gösterme" deneyi. Öneri motoru backend'de tek hesaplayıcıdır | Öneri politikası (hedef marj) **tahminle etkinleşemez**. Satışçılar öneriye körü körüne uyabilir veya hiç güvenmeyebilir | M | pubsonline.informs.org/doi/fpi/10.1287/mksc.2023.1449 |
| A14 | **Oyunlaştırma** (depo/hat) | Amazon depo oyunları; Kenco; Manhattan Associates | Üretkenlik %3–5 arttı [İDDİA, Kenco]. Manhattan "iyi verimiz yok" diyor | D/C | Bireysel lider tablosu önerilmez. En fazla takım düzeyinde ilerleme ("sayım %80 tamam") | Gizli hedef yükseltme, gözetim, KVKK; küçük ekipte sosyal baskı | S | supplychaindive.com/news/warehouses-workers-labor-gamification-productivity/598971/ |
| A15 | **Sosyal norm içeren tahsilat hatırlatması** | Hallsworth, List, Metcalfe, Vlaev, *J. Public Econ.* 2017 (HMRC, 200 binden fazla kişi) | "Çoğu kişi zamanında öder" cümlesi vadesi geçmiş vergi ödemesini artırdı. Betimleyici norm, buyurgan normdan güçlü çıktı | A, ama **vergi/B2C bağlamı**; B2B'ye aktarımı kanıtsız | Hatırlatma şablonunda iki varyant (nötr / betimleyici norm). Normdaki oran **backend'in gerçek verisinden** hesaplanır, uydurulmaz. Gönderim kullanıcı başlatmalı paylaşım veya e-postayla olur (DEC-DOC-007 ile uyumlu) | Yanlış norm cümlesi yanıltıcı olur. B2B'de ilişkiye zarar verebilir | S | nber.org/papers/w20007 |

## 2. İş modeli ve operasyon fikirleri (metal ve B2B dağıtım)

| # | Fikir | Kaynak / kim savunuyor | Ne iddia ediliyor | Kanıt | Bizim işe uyarlama | Risk / başarısızlık biçimi | Efor | URL |
|---|---|---|---|---|---|---|---|---|
| B1 | **Artık / ikinci kalite / fazla stok pazaryeri** | Reibus (ABD), Metycle, Metaloop, Mercato Metalli | Reibus satışlarının yaklaşık %45'i "excess prime", %8'i ikinci kalite [İDDİA, 2020]. Hurda ticaretinde güven ve ilişki belirleyici | C | Dış platform kurma değil, **"artık vitrini"**: üretimden gelen artık ve yaşlanan etiketlerin haftalık listesi (boyut, kalite, döküm no, kg, taban fiyat). Önce grup içinde, sonra seçili müşterilere PDF ile paylaşılır. Hurda fiyatıyla karşılaştırmalı gösterilir | Taban fiyat politikası karar ister. Listeyi rakiplere açmak rekabet hukuku riski taşır. Ayrıca yamyamlık: birinci kalite satışını yiyebilir | S | steelmarketupdate.com/2020/06/05/digital-marketplace-strives-to-reinvent-industrial-business-during-pandemic · fastmarkets.com/insights/scrap-metal-trading-platform-metycle-boost-recycling |
| B2 | **Hizmet olarak çelik / abonelik** (steel-as-a-service, MaaS) | Columbia CCSI çalıştayı (2025), thyssenkrupp "Materials as a Service", WEF | Ton yerine performans veya kullanım satılır; ömür sonunda metal geri alınır | D/C. Pilot ve pazarlama düzeyinde; yaygın abonelik bulunamadı | Gerçekçi karşılığı: **çerçeve sözleşme + aylık tonaj + endeks bağlı fiyat + hurda geri alım kredisi**. Bu büyük ölçüde mevcut sipariş yapısıyla yapılabilir | Mülkiyet, muhasebe ve stok politikası belirsizleşir. Talep dalgalanınca bağlanmış stok kalır | L | ccsi.columbia.edu/?p=2518 · thyssenkrupp-materials-services.com/en/newsroom/stories/transformation/materials-as-a-service |
| B3 | **Müşteriye özel stok (VMI/konsinye) + dinamik fiyat** | SMU "Steel buyers basics"; Ryerson VMI vakası | Müşterinin stok maliyeti düşer; tedarikçi müşteriye bağlanır | C | Tek müşteriyle "ayrılmış stok + tüketildikçe faturala" deneyi. Yalnız F017 gelecek tedarik ayırma çalışması kesinleştikten sonra | Stok ve muhasebe politikası karar ister. Fiyat riski bizde kalır. SMU'ya göre konsinyede alıcı "tedarikçinin fiyatına mahkûm" kalabilir | L | steelmarketupdate.com/2014/03/02/steel-buyers-basics-consignment-inventory-deals |
| B4 | **Maliyet artı şeffaf fiyat** (açık defter, open-book) | Kajüter ve Kulmala, *Management Accounting Research* 2005; Kulmala tezi (2003) | Maliyet paylaşımı güven ve verim getirir. Ama açık defter **sık başarısız** olur: güç dengesi, güven ve tedarikçinin maliyet sistemi belirleyici | A (vaka). Sonuç: koşullu | Fason için uygun: müşteri malzemesi zaten kendisinin. Teklifte "işleme ücreti = hat dakikası × tarife + fire + paketleme" kırılımı **A2 iziyle** gösterilir. Ürün satışında açık defter önerilmez | Marj görünür olunca pazarlık gücü kaybedilir. Kırılımı ekranda yeniden hesaplamak değişmez ihlalidir | S–M | cris.vtt.fi/en/publications/open-book-accounting-in-networks-potential-achievements-and-reaso/ |
| B5 | **Müşteri kârlılığına göre hizmet seviyesi farklılaştırma** | Alexander Group, Vendavo, HBS Working Knowledge, Simon-Kucher | Acil sevk, küçük sipariş ve değişiklik talepleri alt seviyede ücretli; üst seviyede dahil | B/C. Önce–sonra marj rakamı olan yayımlanmış vaka bulunamadı | Cep marjı (boşluk analizi #1) üstüne bir **hizmet menüsü**. Önce **gölge ücret**: müşteri bir şey ödemez, raporda "bu acil sevk X maliyet yarattı" görünür. Ücret almak ayrı karardır | Fiyat ve hizmet politikası tahminle etkinleşmez. Sadık müşteri küsebilir | M | alexandergroup.com/insights/distributors-differentiate-service-levels-grow-margin/ · sdcexec.com/warehousing/article/10269228/vendavo-monetizing-supply-chain-excellence-in-distribution |
| B6 | **Kapasite satışı:** boş fason hat saatini pazarlamak | MakeTime, Orderfox (ABD), AB MANU-SQUARE, Würzburg prototipi | Parça yerine makine saati alınıp satılır. Özel makineler kapasite ticaretini zorlaştırır | C/D | Üretim planından haftalık **"boş kapasite bülteni"** (hat × gün × kalınlık/genişlik aralığı). Mevcut fason müşterilere ve satış ekibine gider. Dış platforma bağlanılmaz | Plan verisi güvenilmezse boş görünen saat aslında dolu çıkar ve müşteri güveni zedelenir | S | mmsonline.com/news/maketime-startup-enables-capacity-sharing · wiwi.uni-wuerzburg.de/fileadmin/12021300/2019/DIHPOkt19_Towards-Open-Production.pdf |
| B7 | **Grup şirketleri arasında ortak satın alma** | OECD transfer fiyatlandırması rehberi (grup sinerjisi paragrafları) | Birleşik hacim indirim getirir. Tasarruf, koordinatöre hizmet bedeli ödendikten sonra **hacim oranında** paylaşılmalı | B/M. Ampirik çalışma bulunamadı | Üç şirketin talebini gösteren **salt okuma konsolide görünüm**. Müzakere tek yerden yapılır, satın alma her şirkette ayrı kalır | **Şirket filtresi:** şirketler arası görünüm açık yetki ister. Türkiye'de transfer fiyatlandırması / örtülü kazanç (KVK md. 13) riski var (M: teyit) | M | tpcases.com/chapter-i-paragraph-1-162/ |
| B8 | **Karbon/emisyon etiketli ürün ve CBAM verisi** | Fastmarkets, S&P Global, IISD, SteelOrbis | AB'de yeşil yassı çelik primi 2025'te yaklaşık €100–180/t [İDDİA, Fastmarkets]. CBAM kesin dönemi 1.1.2026'da başladı. Doğrulanmış veri yoksa varsayılan değerler maliyeti 2–3 katına çıkarabilir [İDDİA] | B (analist). Türkiye'ye özgü prim verisi yok | **Soy ağacı avantajımıza oturur:** kabulde tedarikçinin emisyon beyanı (değirmen beyanı veya CBAM verisi) MTC ile birlikte kaydedilir, etikete bağlanır, dilme ve kesmede çocuk etikete geçer. Böylece ihracat yapan müşteriye kg bazlı emisyon verisi verilebilir. **Prim alma** ayrı bir ticari karardır | Veri tedarikçiye bağımlı. CBAM kapsamı (aşağı akım ürünler eki) teyit edilmeli. Yanlış emisyon beyanı hukuki risk taşır | M | fastmarkets.com/insights/global-green-steel-markets-in-2026-regulation-costs-and-regional-divergence/ · iisd.org/publications/guide/navigating-eu-carbon-border-adjustment-mechanism |
| B9 | **Dinamik erken ödeme iskontosu** (dynamic discounting) | Taulia, C2FO | Ne kadar erken ödenirse o kadar iskonto. Platformdaki tedarikçilerin yalnız %3'ü erken ödeme alıyor; %63'ü ilgili [İDDİA, Taulia anketi] | C. Kendi kendini seçmiş satıcı anketi | Türkiye'de vade ve çek ağırlıklı ticarette **alacak tarafında** anlamlı: "peşin veya erken ödersen x" teklifi. İskonto oranını sermaye maliyetinden backend hesaplayıcısı üretir | Sermaye maliyeti ve iskonto oranı **para politikasıdır**, karar ister. Mevzuat ve fatura altı iskonto muhasebesi teyit edilmeli | M | taulia.com/cn/wp-content/uploads/2024/03/Taulia-Supplier-Survey-23_24.pdf |
| B10 | **Tedarik zinciri finansmanı / ters faktoring** | Banerjee, Lucker, Ries (IJOPM 2021); de Goeij ve ark. (IJPDLM 2021); IDB Invest | Teoride iki taraf da kazanır. Pratikte tedarikçiler adaletsiz bulduğu teklifi kendi zararına da olsa reddediyor | A (deney ve vaka) | Biz "çapa alıcı" değiliz; değirmenler bizden büyük. Programı biz başlatamayız. Bankanın sunduğu bir program varsa ayrıca değerlendirilir | Güç dengesi; banka ve sözleşme riski | L | emerald.com/insight/content/doi/10.1108/ijopm-07-2020-0495/full/pdf |
| B11 | **B2B self-servis teklif portalı / e-ticaret** | Klöckner (Almanya); Paperless Parts; uyarıcı örnekler: Zhaogang ve Ouyeel (Çin) | Klöckner'in dijital satış payı 2021'in ilk çeyreğinde %45'in üzerinde [İDDİA, Eurometal]. Özel imalatta ortalama kazanma oranı yaklaşık %30 [İDDİA, Paperless Parts]. Zhaogang Hong Kong halka arzını 2019'da geri çekti; Ouyeel'in A hissesi başvurusu sonlandırıldı | C | Portal P3'te kalsın. Önce **teklif yanıt süresi** ölçülür (aşağıda C10). Portal geldiğinde fiyatı aynı backend hesaplayıcısı verir; müşteriye özel fiyat ve kredi kapısı uygulanır | Müşteri tabanı küçük, ağ etkisi yok. Bakım maliyeti yüksek | L | eurometal.net/klockner-increases-digital-sales-share/ · paperlessparts.com/processes/metal-service-providers/ · kallanish.com/en/news/steel/market-reports/article-details/baowu-ouyeel-terminates-listing-process-0724 |
| B12 | **WhatsApp ticareti** | Jelou (Arca Continental), AiSensy, go4whatsup; SAP Press mimari yazısı | WhatsApp siparişi ERP fiyat ve kredi kurallarıyla senkron. Arca'da 2,5 milyon USD sipariş [İDDİA] | C | **DEC-DOC-007 API'yi yasaklıyor.** Kilit içinde kalan uyarlama: satışçı WhatsApp'tan gelen sipariş metnini ERP'ye **yapıştırır** → backend ürün ve cari eşleştirmesiyle **taslak sipariş** üretilir (AI treniyle) → insan onaylar. Çıkış yönünde mevcut "Paylaş" düğmesi | Yanlış eşleşme (kalınlık, kalite). API'ye kayma kilitli kararı ihlal eder. KVKK (sohbet içeriği) | S–M | jelou.ai/en/case-studies/arca-b2b · blog.sap-press.com/secure-whatsapp-business-integration-with-sap |
| B13 | **Sipariş-tahsilat (order-to-cash) "dokunuşsuz" otomasyon iddiaları** | AP/AR otomasyon satıcıları (Medius vb.) | İşlemlerin büyük kısmı insan dokunmadan akar | C | İddiayı ölçüme çeviririz: **dokunuşsuz oran** = hiçbir elle düzeltme olmadan faturaya ve tahsilata giden siparişlerin payı. Bu metrik A11'in süreç madenciliğinden çıkar | Metrik oyunlaştırılabilir: düzeltmeler kayıt dışına itilir | S | medius.com/blog/why-erp-workflows-struggle-with-modern-ap-complexity/ |
| B14 | **Hurda geri alım döngüsü** (müşteri artığını geri alıp sonraki siparişe kredi) | CCSI MaaS arketipleri, Advanced Engineering UK | Kapalı döngü malzeme ve müşteri bağlılığı | D | Müşterinin dilme/kesme artığı geri alınır; kg × aylık hurda fiyatı sonraki faturada mahsup edilir | Hurdada KDV tevkifatı (7/10, kod teyidi) ve belge karmaşıklığı (M). Mahsup politikası karar ister | M | advancedengineeringuk.com/?p=5030 |

## 3. Ölçüm ve karar fikirleri

| # | Fikir | Kaynak / kim savunuyor | Ne iddia ediliyor | Kanıt | Bizim işe uyarlama | Risk / başarısızlık biçimi | Efor | URL |
|---|---|---|---|---|---|---|---|---|
| C1 | **Darboğaz muhasebesi** (throughput accounting, TOC) | Goldratt ekolü; Panizzolo (61 Avrupa firması, 2016); Brezilya metal işleme vakası (2025); Dugdale ve Jones (1998) | Ürün karması, kısıt kaynağın dakikası başına katkıyla seçilmeli; tam maliyetleme yanlış karar verdirir | A, ama küçük örneklem ve vaka ağırlıklı. Meta-analiz yok | Cep marjının **yanına** paralel rapor: (satış − malzeme − doğrudan değişken) ÷ **darboğaz hattın dakikası**, müşteri ve ürün bazında. Darboğaz plan verisinden belirlenir. Hesap backend'de tek hesaplayıcıda yapılır | İki farklı "kârlılık" sıralaması kafa karıştırır. Darboğaz yer değiştirir | M | research.unipd.it/retrieve/e14fb26b-f0bc-3de1-e053-1705fe0ac030/ijiem_vol7_no1_3.pdf · growingscience.com/ac/Vol12/ac_2025_26.pdf |
| C2 | **Amoeba yönetimi / saatlik verim** (Japonya, Kyocera) | Inamori; Urban 2016 (Avrupa'daki uygulamalar, nitel); Otani 2018 | Küçük birimlerin saat başı katma değeri herkese görünür olur ve sahiplenme artar | A (nitel). Eleştiri: birim kendi kârına odaklanıp bütünü zarara sokabilir | Altı üretim hattı için **yalnız rapor**: hat başına (çıktı değeri − malzeme − hat giderleri) ÷ çalışılan saat. Prim ve teşvik bağlanmaz. C1 ile aynı veri | Alt optimizasyon (hatlar arası transfer fiyatı kavgası). Teşviğe bağlanırsa veri manipülasyonu | M | agro.icm.edu.pl/agro/element/bwmeta1.element.agro-749dc100-0556-4118-93d7-90e7bfb1c34e/c/8.pdf |
| C3 | **Haier rendanheyi ve "kazan-kazan katma değer tablosu"** (Çin) | Haier, IMA, Renmin vaka merkezi | Mikro girişimler, kendi kullanıcı ve ekosistem gelirini raporlar | C (öğretim vakası) | Uymaz: 3 şirketlik ve sınırlı kadrolu yapıda yeniden örgütlenme maliyeti ağır. Yalnız "hat bazlı P&L" fikri C2'de karşılanıyor | Örgütsel kaos | L | casecenter.rmbs.ruc.edu.cn/web/case_eninfo.php?ID=782 |
| C4 | **Fiyat kararında oyun teorisi / fiyat savaşı analizi** | Rao, Bergen, Davis, HBR 2000 | Fiyat savaşına her zaman fiyatla cevap verilmez; fiyat dışı hamleler ve sinyal verme düşünülmeli | B (yönetim makalesi) | Rakip fiyat **gözlemi** kayıp nedeniyle birlikte kaydedilir; senaryo tablosu (rakip kırarsa, biz tutarsak) teklif ekranının yanında bilgi amaçlı durur. Algoritmik tepki yok | **Rekabet hukuku:** rakiple fiyat bilgisi paylaşımı veya "sinyal verme" uyumlu eylem sayılabilir (4054 sayılı Kanun; M, teyit) | S | hbswk.hbs.edu/archive/how-to-fight-a-price-war-analyzing-the-battleground |
| C5 | **B2B'de Bayesyen fiyat testleri** | Johari, Weintraub ve ark. (arXiv, girişim/yanlılık); Kameleoon; Optimizely | Fiyat A/B testleri yanlı olabilir. Pazarlıklı fiyatta klasik A/B uygulanamaz | A (yöntem eleştirisi) | **Fiyat rastgeleleştirilmez**; yalnız A13'teki "önerinin gösterilip gösterilmemesi" rastgeleleştirilir. Kazanma olasılığı modeli Bayesyen güncellemeyle iyileştirilir | Az teklif sayısında istatistiksel güç düşük. Müşteriler arası fiyat adaletsizliği ilişkiyi bozar | M | arxiv.org/abs/2310.17165v2 |
| C6 | **Ön-ölüm analizi** (pre-mortem) karar kaydı | Gary Klein; Veinott, Klein, Wiggins 2010 | "Proje başarısız oldu, neden?" diye düşünmek aşırı güveni en çok azaltan yöntem çıktı | A, ama öğrenci örneklemi. İş sonucu kanıtı yok | Büyük kararlarda (spekülatif stok alımı, yeni müşteriye yüksek limit, hat yatırımı) beş alanlık kısa form: en olası üç başarısızlık nedeni ve erken uyarı işareti | Formaliteye dönüşür | S | psychologytoday.com/ie/blog/seeing-what-others-dont/202101/the-pre-mortem-method |
| C7 | **Karar günlüğü ve otomatik sonuç takibi** (decision journal) | Umbrex, Alliance for Decision Education (Kahneman alıntısı); Gartner "decision intelligence" | Karar, sonuç bilinmeden gerekçesiyle kaydedilir; sonra süreç ve şans ayrıştırılır | D (pratisyen). Gartner tanımı B ve hype | **Yenilik: ERP sonucu kendisi doldurur.** "500 t HRC alımı; beklenti: 60 günde fiyat artar, marj ≥ hedef" diye kaydedilir. Gözden geçirme tarihinde ERP gerçekleşen endeks fiyatını ve hesaplayıcının marjını otomatik ekler. Alıcıların yön tahminleri Brier skoruyla kalibre edilir | Suçlama kültürüne dönerse kimse kaydetmez. Bu kayıtlar `SINAN_KARARLARI` dosyalarının yerine geçmez | S–M | umbrex.com/resources/frameworks/decision-making-frameworks/decision-journal/ |
| C8 | **Çalışanlar arası tahmin pazarı** | Cowgill ve Zitzewitz, *Review of Economic Studies* 2015 (Google, Ford, Firm X) | Kurumsal piyasalar alternatif tahmin yöntemlerinden iyi; başta iyimserlik yanlılığı var, deneyimle azalıyor. Genel sicil karışık | A, ama büyük firmalar; yazarlarda çıkar ilişkisi var | Kadromuz için katılımcı sayısı çok az. Bunun yerine C7 içinde basit **tahmin anketi** (gelecek ay HRC yönü, büyük müşterinin ödeme tarihi) ve skor tablosu | İnce piyasa, kumar algısı, motivasyon kaybı | S | econpapers.repec.org/RePEc:oup:restud:v:82:y:2015:i:4:p:1309-1341. · asteriskmag.com/issues/08/the-death-and-life-of-prediction-markets-at-google |
| C9 | **OKR'ların ERP'ye gömülmesi** | Doerr ekolü, BCG; arXiv 2311.00236 (512 yanıtlı anket) | Hedef ve sonuç hizalanır | OKR'a özgü kanıt zayıf. Locke ve Latham'ın hedef belirleme kuramı dolaylı destek (A) | Ayrı OKR modülü yerine şirket başına 3–5 hedef, rapor merkezindeki **canlı KPI'a** bağlanır | Metrik oyunu. Bürokrasi | S | arxiv.org/abs/2311.00236 |
| C10 | **Teklif yanıt hızı SLA'sı** | Oldroyd, McElheran, Elkington, HBR 2011 ("The Short Life of Online Sales Leads"); Paperless Parts | Lead ilk saatte aranırsa nitelikli müşteriye dönme olasılığı belirgin yüksek. İkincil kaynaklardaki çarpanlar çelişkili [İDDİA] | B. B2C ve B2B lead verisi; teklif kazanma kanıtı değil | Önce **kendi verimizde** teklif talebi → teklif gönderimi süresi ile kazanma ilişkisi ölçülür. İlişki varsa iş kutusunda SLA sayacı eklenir | Korelasyon nedensellik değildir: kolay teklif hem hızlı hem kazanılır | S | scholarsarchive.byu.edu/facpub/9711 |
| C11 | **Dijital vardiya panosu** (Almanya: shopfloor management, SQCDP) | KIT wbk çalışmaları; IEOM 2024 (TRUMPF) | Günlük 10–15 dakikalık panoda güvenlik, kalite, maliyet, teslim ve insan; aksiyon listesiyle hızlı karar | A (nitel). Etki büyüklüğü ölçülmemiş | Hat başı ekranı fikrinin (boşluk analizi) **toplantı katmanı**: ERP'den dünkü fire, kütle dengesi sapması, kantar farkları ve geciken iş emirleri; elle yazılan aksiyon maddesi sahibi ve tarihiyle | Pano güncellenmezse terk edilir | S–M | wbk.kit.edu/downloads/Auf%20dem%20Weg%20zum%20digitalen%20Shopfloor%20Management.pdf |

---

## 4. Denemeye değer ilk 12 fikir: 2–4 haftalık geri alınabilir pilotlar

Ortak kurallar:
- Her pilot **özellik bayrağıyla kapalı başlar**.
- Mümkün olan her yerde **salt okuma veya gölge mod** kullanılır.
- Para/stok kaydı üreten hiçbir pilot politika kararı olmadan etkinleşmez.
- Başarı ölçütünün eşiği **Sinan'la önceden yazılır**; aşağıdakiler ölçüt tanımıdır, hedef rakam değildir.

1. **İstisna öncelikli ana sayfa (A1), 3 istisna türüyle.**
   - Kurulum: kantar farkı, taban fiyat altı, sertifikası eksik sevk. Her biri sahip, SLA ve kapanış nedeni koduyla gelir. Bir şirkette 2–3 kullanıcı.
   - Ölçüt: istisnanın açık kalma süresinin medyanı; "yanlış alarm" kapanış oranının haftadan haftaya eğilimi; kullanıcının günde açtığı liste ekranı sayısı (öncesi/sonrası).
   - Geri alma: bayrak kapatılır, eski liste ekranları yerinde kalır.

2. **"Bu sayıyı açıkla" hesap izi (A2).**
   - Kurulum: satır marjı ve üretim maliyeti hesaplayıcısı yanıtına iz alanı eklenir; yalnız yetkili rolde açılan bir yan panel.
   - Ölçüt: rastgele 20 sayının izden elle yeniden üretilebilmesi (hedef tam eşleşme); "bu rakam nereden?" sorusunun çözülme süresi; kullanıcı güven anketi (önce/sonra).
   - Geri alma: alan opsiyoneldir, panel gizlenir.

3. **Audit/outbox günlüğünden süreç madenciliği + teklif yanıt hızı (A11 + C10 + B13).**
   - Kurulum: tek şirketin son N aylık zinciri, şirket filtresiyle CSV'ye aktarılır; çevrimdışı analiz yapılır. Kişi adı yerine rol kullanılır (KVKK).
   - Ölçüt: en sık 5 varyant ve döngü raporu; teklif yanıt süresi dilimine göre kazanma oranı; dokunuşsuz sipariş oranı. **Başarı:** en az 3 somut iyileştirme önerisi çıkması.
   - Geri alma: üretim sistemine dokunmaz.

4. **Komut paleti (A6).**
   - Kurulum: Ctrl+K ile gezinme ve 5 oluşturma komutu; capability registry süzgeci uygulanır.
   - Ölçüt: 4 hafta sonunda kullanıcıların kaçının paleti haftada en az 5 kez kullandığı; "yeni teklif" açma süresi (önce/sonra); yetkisiz komut sızıntısı testinin sıfır sonuç vermesi.
   - Geri alma: bayrak kapatılır.

5. **Gölge fiyat önerisi (A13 + C5).**
   - Kurulum: 1. faz yalnız geriye dönük çalışır. Backend, geçmiş tekliflere kazanma olasılığı ve marj dengesine göre bir öneri fiyatı hesaplar; satışçının fiyatıyla karşılaştırılır, **kimseye gösterilmez**. 2. faz (Sinan onayıyla) teklif bazında rastgele "öneriyi göster" uygulanır.
   - Ölçüt: öneri ile gerçekleşen fiyat arasındaki farkın kazanılan ve kaybedilen tekliflerde dağılımı; 2. fazda iki grubun gerçekleşen marjı ve kazanma oranı.
   - Geri alma: 1. faz tamamen çevrimdışıdır.

6. **Karar günlüğü + ön-ölüm + otomatik sonuç (C6 + C7 + C8).**
   - Kurulum: yalnız spekülatif stok alımı ve yüksek limit kararları için form ve gözden geçirme tarihi. ERP endeks fiyatını ve gerçekleşen marjı otomatik doldurur. Basit yön tahmini anketi eklenir.
   - Ölçüt: kayıt oranı (hedef kararların kaçı kaydedildi); 3. ayda Brier skoru; gözden geçirme toplantısında çıkan ders sayısı.
   - Geri alma: bağımsız tablo, başka modüle yazmaz.

7. **Darboğaz dakikası başına katkı ve hat saatlik verimi (C1 + C2).**
   - Kurulum: bir şirketin darboğaz hattı belirlenir; backend raporu cep marjıyla yan yana gösterir. Teşvik bağlanmaz.
   - Ölçüt: iki sıralamada en kârlı ve en kârsız 10 müşterinin örtüşmesi; rapor yüzünden değişen kararların sayısı ve gerekçesi (karar günlüğüne yazılır).
   - Geri alma: yalnız rapor.

8. **Boş kapasite bülteni (B6).**
   - Kurulum: üretim planından haftalık otomatik PDF (hat × gün × boyut aralığı); satış ekibi kullanıcı başlatmalı paylaşımla gönderir.
   - Ölçüt: bültene atfedilen fason iş emri saati; bültende "boş" görünüp aslında dolu çıkan saat oranı (plan doğruluğu).
   - Geri alma: rapor kapatılır.

9. **Artık vitrini (B1).**
   - Kurulum: üretimden gelen artık ve belli yaşı geçmiş etiketlerin haftalık listesi; önce grup içi, sonra 5 seçili müşteri. Fiyat taban politikasına bağlı kalır; taban yoksa yalnız "fiyat sorunuz" yazılır.
   - Ölçüt: listelenen artığın 4 hafta içinde satılan kg payı; artık stokun ortalama yaşı; gerçekleşen fiyat ile hurda fiyatı arasındaki fark.
   - Geri alma: liste durdurulur.

10. **WhatsApp metnini yapıştır → taslak sipariş (B12).**
    - Kurulum: 2 satışçı; backend ürün ve cari eşleştirmesi (AI treni şemalı aracıyla); taslak yalnız insan onayıyla siparişe döner; idempotency anahtarı metin özetinden üretilir.
    - Ölçüt: sipariş girme süresi (önce/sonra); taslakta düzeltilen alan oranı; yanlış ürünle onaylanan sipariş sayısı (hedef sıfır).
    - Geri alma: bayrak kapatılır.

11. **Tahsilat hatırlatma metni A/B (A15).**
    - Kurulum: vadesi geçmiş açık kalemler rastgele iki şablona ayrılır. Norm cümlesindeki oran backend'den hesaplanır, gönderim kullanıcı başlatmalıdır.
    - Ölçüt: hatırlatmadan sonra ödemeye kadar geçen gün sayısının iki gruptaki medyanı; müşteri şikâyeti sayısı.
    - Geri alma: şablon geri çekilir.

12. **Emisyon verisini döküm no'ya bağlama (B8).**
    - Kurulum: 4 hafta boyunca kabulde tedarikçi emisyon beyanı varsa MTC'yle birlikte belge kasasına alınır ve etikete bağlanır; soy ağacıyla çocuk etikete miras test edilir. Satışta prim **yok**.
    - Ölçüt: beyanı olan kabul satırı payı; ihracatçı müşterilerden gelen emisyon veri talebi sayısı; çocuk etikette eksiksiz miras oranı.
    - Geri alma: veri alanı opsiyoneldir. Not: kalite dilimiyle (Dalga 3) birlikte yapılması doğal.

Yedek liste: geri alma bekleme penceresi (A3), zaman yolculuğu görünümü (A4), dijital vardiya panosu (C11), şeffaf fason teklif kırılımı (B4), gölge hizmet ücreti (B5).

## 5. Cazip ama bizim için kötü fikirler

- **Tam olay kaynaklı yeniden yazım (A5):** outbox, audit ve correlation ile zaten faydanın çoğunu alıyoruz. Şema evrimi ve projeksiyon maliyeti, küçük bir ekip ve modüler monolit için fazla.
- **Composable/headless ERP (A8):** Gartner bile modüler stratejilerin beklenen değeri vermediğini yazıyor. Üç şirketlik grup için entegrasyon yükü kazancı yer.
- **Organizasyonun dijital ikizi (A12):** model bakımı gerçeğin gerisinde kalır; bağımsız sonuç kanıtı yok.
- **Defteri yazan otonom ajanlar:** AWS'nin kendi deseni bile "danışman → denetimli" sırasıyla ilerliyor. Bizde kritik komut insan onayından geçer.
- **Formüllü tablo arayüzü (A9):** saha denetimleri tabloların çoğunda hata buluyor (A). Hesap UI'a kayar.
- **Stok/para için CRDT ile local-first (A10):** çakışma çözümü çift hareket veya kayıp üretir.
- **Çalışanlar arası tahmin pazarı (C8):** katılımcı az, piyasa ince. Basit tahmin anketi aynı öğrenmeyi verir.
- **Bireysel oyunlaştırma ve lider tabloları (A14):** kanıt zayıf. KVKK, gözetim ve küçük ekipte sosyal baskı riski var.
- **Kendi açık pazaryerimiz veya ağ platformumuz (B11/B1'in dış hali):** ağ etkisi yok. Çin'deki en büyük çelik platformları bile halka arzda zorlandı.
- **Çelik aboneliği (B2):** yaygın uygulama yok. Stok ve fiyat riski bizde kalır.
- **Ters faktoringi başlatan taraf olmak (B10):** güç dengesi bizde değil.
- **Müşterilere rastgele farklı fiyat vermek (C5'in yanlış hali):** istatistiksel güç düşük, ilişki zarar görür, adalet ve rekabet riski var.
- **Haier tarzı mikro girişim yeniden örgütlenmesi (C3):** maliyet yüksek, kanıt öğretim vakası düzeyinde.
- **Algoritmik rakip tepkisi veya fiyat sinyali (C4):** rekabet hukuku riski taşır.
- **Ayrı OKR modülü (C9):** rapor merkezi KPI'ı yeter.

## 6. ERP değişmezleriyle çakışanlar

| Değişmez | Çakışan fikir | Çakışma | Güvenli biçim |
|---|---|---|---|
| **CompanyId / şirket filtresi** | B7 ortak satın alma, B1 grup içi artık vitrini, A11 dışa aktarım | Şirketler arası görünüm, şirket sınırını delmeye çağırır | Şirketler arası okuma açık ve ayrı bir yetkiyle verilir (grant insan kapısıdır). Konsolide görünüm salt okumadır, yazma her şirkette ayrı kalır. Dışa aktarım şirket başına yapılır |
| **Yetki** | A6 komut paleti, A7 sohbet arayüzü, A2 hesap izi | Palet veya sohbet yetkisiz komutu ya da maliyeti gösterebilir | Capability registry süzgeci kullanılır. İz alanları maliyet görme yetkisine bağlanır. Sohbet yalnız okur |
| **Audit, silme yok, ters kayıt** | A3 geri alma, A4 zaman yolculuğu | Geri alma sessiz silmeye, temporal tablo audit'in yerine geçmeye kayabilir | Geri alma yalnız gönderimden önceki bekleme penceresinde çalışır, sonrası ters kayıt taslağıdır. Temporal görünüm audit'i tamamlar, onun yerine geçmez. Saklama politikası OPEN-018/021'e bağlıdır |
| **Idempotency / expected version** | A10 çevrimdışı kuyruk, B12 yapıştır-taslak, A9 ızgara toplu giriş | Tekrar gönderim çift hareket üretebilir | Her kuyruk öğesi ve taslak idempotency anahtarı taşır. Izgara kaydı tek komuttur ve expected version kontrolü yapar |
| **Deterministik tek hesaplayıcı** | A2 hesap izi, A9 formül, A13 fiyat önerisi, B4 şeffaf kırılım, C1/C2 raporları, A15 norm oranı | Ekranda veya AI'da yeniden hesaplama ihlal olur | Bütün sayılar, oranlar ve izler backend hesaplayıcısından gelir; golden testlerle sabitlenir. UI yalnız gösterir |
| **Para/stok/fiyat politikası tahminle etkinleşmez** | A13 öneri varsayılanı, B5 hizmet ücreti, B9 iskonto oranı, B3 VMI, B1 taban fiyat, B14 hurda mahsubu, A1 istisna eşikleri | Politika seçimi gerektirir | Gölge mod ve yapılandırılabilir parametre kullanılır, politika `NEEDS-SINAN` / `OPEN-*` ile işaretlenir. Etkinleştirme Sinan kararıyla olur |
| **AI doğrudan DB'ye veya ham SQL'e erişemez** | A7 sohbet arayüzü, A11 süreç madenciliği (AI'a açılırsa) | Ham sorgu yüzeyi açılır | Yalnız şemalı backend araçları. Süreç madenciliği çevrimdışı ve insan analizi olarak kalır |
| **DEC-DOC-007 (WhatsApp API yok, KİLİTLİ)** | B12 WhatsApp ticareti | API veya otomatik gönderim kilitli kararı ihlal eder | Yalnız gelen metni yapıştırma ve kullanıcı başlatmalı paylaşım. API ancak yeni bir Sinan kararıyla gelir |
| **Kopya hareket yok** | B3 konsinye, B14 hurda geri alım, B1 artık satışı | Aynı fiziksel olay iki kez kaydedilebilir | Mülkiyet değişimi tek olaydır. Konsinye ve iade akışları mevcut stok hareket tipleriyle modellenir |
| **Hukuk (M, teyit gerekir)** | C4 rakip fiyatı, B1 vitrinin rakiplere açılması, B7 transfer fiyatlandırması, A11/A14 çalışan verisi, B8 emisyon beyanı | Rekabet Kanunu (4054), KVK md. 13, KVKK, CBAM beyan sorumluluğu | Avukat ve mali müşavir teyidi alınır. Varsayılan ayar kapalı ve dar kapsamlıdır |

## 7. Kısa sonuç

- **En güçlü kanıta sahip ve sektöre en yakın ana akım dışı bulgu**, B2B alüminyum satıcısındaki saha deneyi (A13). Satışçıya model fiyatını *göstermek* (zorlamak değil) kârı artırmış; karmaşık tekliflerde insan daha iyi kalmış. Bu, bizim "tek deterministik hesaplayıcı + insan onayı" mimarimize birebir uyuyor.
- **Mimarimize en ucuz oturanlar:** hesap izi (A2), audit/outbox günlüğünden süreç madenciliği (A11) ve soy ağacına emisyon verisi (B8). Üçü de zaten sahip olduğumuz değişmezleri (deterministik hesaplayıcı, correlation, döküm no mirası) kaldıraç olarak kullanıyor.
- **Kanıtı zayıf ama ucuz ve geri alınabilir olanlar:** karar günlüğü, istisna öncelikli ana sayfa, boş kapasite bülteni, artık vitrini. Ölçütleriyle birlikte pilotlanmaya değer.

Bu rapor bir öneridir, karar değildir. Pilotların hiçbiri Sinan'ın sırası ve eşik onayı olmadan etkinleşmez.

---

## Kaynaklar

- https://docs.amazonaws.cn/sap/latest/general/rise-agenticai-erpexceptions.html
- https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/article_5094345081.html
- https://www.journalofaccountancy.com/issues/2017/nov/drillable-financial-statements/
- https://www.intacct.com/ia/docs/en_US/help_action/Reporting/Distribute_and_run_reports/Run_reports/drill-down-txn-info.htm
- https://alistapart.com/article/neveruseawarning
- https://learn.microsoft.com/en-us/sql/relational-databases/tables/temporal-tables
- https://learn.microsoft.com/en-us/ef/core/providers/sql-server/temporal-tables
- https://learn.microsoft.com/en-gb/previous-versions/msp-n-p/jj591559(v=pandp.10)
- https://vivianvoss.net/blog/the-event-sourcing-tax
- https://clei.org/clei2018/docs/SLISW/182931.pdf
- https://www.scitepress.org/Papers/2019/84945/84945.pdf
- https://linear.app/features/level-up
- https://www.mdpi.com/2073-431X/15/7/427
- https://core.ac.uk/download/237427409.pdf
- https://www.techtarget.com/searcherp/podcast/Evolution-of-postmodern-ERP-to-composable-ERP-explained
- https://arxiv.org/pdf/0802.3457
- https://inkandswitch.com/local-first/
- https://i17vm1.in.tum.de/zotero/PIAFAAI7.pdf
- https://www.emeraldinsight.com/doi/full/10.1108/JEIM-01-2017-0003
- https://www.techtarget.com/data-technologies/definition/What-is-decision-intelligence
- https://pubsonline.informs.org/doi/fpi/10.1287/mksc.2023.1449
- https://www.supplychaindive.com/news/warehouses-workers-labor-gamification-productivity/598971/
- https://www.nber.org/papers/w20007
- https://www.steelmarketupdate.com/2020/06/05/digital-marketplace-strives-to-reinvent-industrial-business-during-pandemic
- https://fastmarkets.com/insights/scrap-metal-trading-platform-metycle-boost-recycling
- https://ccsi.columbia.edu/?p=2518
- https://www.thyssenkrupp-materials-services.com/en/newsroom/stories/transformation/materials-as-a-service
- https://steelmarketupdate.com/2014/03/02/steel-buyers-basics-consignment-inventory-deals
- https://cris.vtt.fi/en/publications/open-book-accounting-in-networks-potential-achievements-and-reaso/
- https://www.alexandergroup.com/insights/distributors-differentiate-service-levels-grow-margin/
- https://www.sdcexec.com/warehousing/article/10269228/vendavo-monetizing-supply-chain-excellence-in-distribution
- https://mmsonline.com/news/maketime-startup-enables-capacity-sharing
- https://www.wiwi.uni-wuerzburg.de/fileadmin/12021300/2019/DIHPOkt19_Towards-Open-Production.pdf
- https://tpcases.com/chapter-i-paragraph-1-162/
- https://www.fastmarkets.com/insights/global-green-steel-markets-in-2026-regulation-costs-and-regional-divergence/
- https://www.steelorbis.com/steel-news/latest-news/jack-laing-default-emission-values-could-double-or-triple-cbam-costs-1450163.htm
- https://www.iisd.org/publications/guide/navigating-eu-carbon-border-adjustment-mechanism
- https://taulia.com/cn/wp-content/uploads/2024/03/Taulia-Supplier-Survey-23_24.pdf
- https://www.emerald.com/insight/content/doi/10.1108/ijopm-07-2020-0495/full/pdf
- https://www.emerald.com/ijpdlm/article/51/8/813/389845/Understanding-SME-suppliers-response-to-supply
- https://eurometal.net/klockner-increases-digital-sales-share/
- https://www.paperlessparts.com/processes/metal-service-providers/
- https://kallanish.com/en/news/steel/market-reports/article-details/baowu-ouyeel-terminates-listing-process-0724
- https://www.jelou.ai/en/case-studies/arca-b2b
- https://blog.sap-press.com/secure-whatsapp-business-integration-with-sap
- https://www.medius.com/blog/why-erp-workflows-struggle-with-modern-ap-complexity/
- https://advancedengineeringuk.com/?p=5030
- https://www.research.unipd.it/retrieve/e14fb26b-f0bc-3de1-e053-1705fe0ac030/ijiem_vol7_no1_3.pdf
- https://www.growingscience.com/ac/Vol12/ac_2025_26.pdf
- https://agro.icm.edu.pl/agro/element/bwmeta1.element.agro-749dc100-0556-4118-93d7-90e7bfb1c34e/c/8.pdf
- https://casecenter.rmbs.ruc.edu.cn/web/case_eninfo.php?ID=782
- https://hbswk.hbs.edu/archive/how-to-fight-a-price-war-analyzing-the-battleground
- https://arxiv.org/abs/2310.17165v2
- https://www.psychologytoday.com/ie/blog/seeing-what-others-dont/202101/the-pre-mortem-method
- https://umbrex.com/resources/frameworks/decision-making-frameworks/decision-journal/
- https://econpapers.repec.org/RePEc:oup:restud:v:82:y:2015:i:4:p:1309-1341.
- https://asteriskmag.com/issues/08/the-death-and-life-of-prediction-markets-at-google
- https://arxiv.org/abs/2311.00236
- https://scholarsarchive.byu.edu/facpub/9711
- https://www.wbk.kit.edu/downloads/Auf%20dem%20Weg%20zum%20digitalen%20Shopfloor%20Management.pdf

**Açık bıraktıklarım:**
- Türkiye'ye özgü WhatsApp B2B kullanım oranı ve yeşil çelik primi için güvenilir veri bulamadım.
- Çin'de makine saati pazarlarına ait kaynak çıkmadı.
- Birincil makalelerin çoğu yalnız özet düzeyinde okundu; A düzeyi atamaları bu sınırla geçerlidir.
