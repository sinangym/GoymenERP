<!-- Ajan araştırma çıktısı (22 Kasım 2026); karar değildir. Birleşik değerlendirme: docs/delivery/ERP_FIKIR_HAVUZU_v1.md -->

# Metal ticaret ve işleme şirketi için ERP'ye gömülü karar destek, analitik ve yapay zekâ: kanıt düzeyleri

**Yöntem notu:** Bu çalışmada web araması yapıldı. Ancak bu oturumda WebFetch bazı sitelere ulaşamadı (pubsonline.informs.org ve insight2profit.com ağ izni dışında kaldı). Bu yüzden sayıların bir kısmı tam metinden değil, arama sonucu özetlerinden ve makale özetlerinden alındı. Kritik bir rakam karar belgesine girecekse birincil kaynaktan teyit edilmeli.

**Kanıt düzeyleri:**
- **A:** Hakemli çalışma ya da bağımsız denetim.
- **B:** Danışmanlık veya analist kıyas verisi (McKinsey, Gartner, APQC). Yarı bağımsızdır.
- **C:** Satıcının kendi vaka çalışması veya pazarlama iddiası.
- **D:** Spekülatif ya da yeni çıkan fikir.

---

## 1. Kârlılık kaldıraçları

**1.1 Sipariş, müşteri ve ürün bazında marj ve hizmet maliyeti (cost-to-serve)**
- **Ne:** Her satırın marjından fatura altı indirimler, navlun, kesim ve işleme, vade maliyeti ve iade düşülür. Sonuç "cep marjı" (pocket margin) olarak hesaplanır. Ardından müşteriler kârlılığa göre sıralanıp "balina eğrisi" çizilir.
- **ERP'de nasıl çalışır:** Zaman bazlı faaliyet maliyetlemesi (TDABC) kullanılır. Örnek sürücüler: sipariş satırı sayısı, kesim adedi, sevkiyat sayısı, tahsilat gecikmesi. Bu sürücüler deterministik bir backend hesaplayıcısıyla müşteriye dağıtılır.
- **Gerekli veri:** Fatura satırı, iskonto ve iade kayıtları, sevkiyat ve navlun, operasyon süreleri, vade ve tahsilat bilgisi.
- **Kanıt:** A/B. Kaplan ve Narayanan (2001) yöntemi akademik olarak yerleşiktir. İkincil özetlere göre en kârlı %20 müşteri toplam kârın %150–300'ünü üretiyor; bu oran ikincil kaynak bilgisidir, orijinal makaleden doğrulanmalıdır. Metal sektörüne özgü örnek: bir çelik servis merkezi faaliyet bazlı maliyetleme aracı benimsemiş, ancak sonuç rakamı yayımlanmamış (C).
- **Beklenen etki:** Doğrudan kâr rakamı bulunamadı. Asıl etki, zarar eden müşterileri görünür kılmasıdır. Ardından fiyat, asgari sipariş, kanal değişikliği gibi aksiyonlar gelir.

**1.2 Fiyat optimizasyonu ve taban fiyat rehberi**
- **Ne:** Endeks bazlı maliyet (HRC, LME ve hurda) ile segment hedef marjından taban, hedef ve tavan fiyat bandı üretilir. Satışçı bandın altına inerse onay gerekir.
- **Kanıt:**
  - Bir şirket için %1 fiyat artışı, hacim sabitse faaliyet kârını yaklaşık %8, 2010 güncellemesinde %8,7 artırır. Bu bir ortalama hesap modelidir, deney değildir (B, McKinsey).
  - PROS, malzeme dağıtımında %2–3 marj kaçağı bulduğunu ve fiyatlama sistemlerinin %2–10 marj artışı sağladığını öne sürüyor; bu son rakamı Gartner'a dayandırıyor (C).
  - Bir metal işleyici ve dağıtıcıda endekse bağlı dinamik fiyat ve taban fiyat uygulamasıyla yaklaşık %3 marj artışı iddia ediliyor (C, Insight2Profit).
- **Not:** Fiyat, formül ve tolerans ekranda değil, tek bir backend hesaplayıcısında tutulmalı.

**1.3 Teklif kazanma/kaybetme analizi ve kazanma olasılığı modeli**
- **Ne:** Geçmiş tekliflerin fiyatı, kazanıldı/kaybedildi sonucu, müşteri, ürün ve miktar verisiyle lojistik regresyon kurulur. Bu model "bid-response" eğrisi verir. Her fiyat için beklenen kâr = marj × kazanma olasılığıdır.
- **Gerekli veri:** Kaybedilen teklifler de dahil olmak üzere disiplinli bir teklif kaydı ve kayıp nedeni.
- **Kanıt:** A. Ferguson ve Agrawal'ın "customized pricing" modelleri yöneylem literatüründe yerleşiktir. Metal sektörüne özgü etki rakamı bulunamadı.

**1.4 Dinamik ek ücretler (alaşım, enerji, navlun)**
- **Ne:** Endeksle eşleşen formüllü ek ücret uygulanır. Hammadde oynaklığının marja aktarılmasını engeller.
- **Kanıt:** Yalnız C düzeyinde var: endeks bağlantılı fiyat motoru iddiası. Bağımsız ölçüm bulunamadı.

**1.5 Müşteri segmentasyonu (RFM/ABC) ve kaybedilme riski taşıyan müşteri tespiti**
- **Ne:** RFM (son alım, sıklık, tutar) skorlarıyla müşteri segmentleri oluşturulur. Satın alma aralığında sapma görülünce "riskli müşteri" uyarısı üretilir.
- **Kanıt:**
  - Toptancılık bağlamında, eğitim verisinin birden fazla zaman diliminden kurulmasının (multi-slicing) ve dönem dışı test yapılmasının tahmin kalitesini artırdığı gösterilmiş (A, Gattermann-Itschert ve Thonemann).
  - Alman bir promosyon ürünleri dağıtıcısında makine öğrenmesiyle churn azaltımı üzerine INFORMS hakemli uygulama vakası var (A, DOI 10.1287/inte.2025.0292). Sonuç rakamı tam metne erişilemediği için alınamadı.
- **Beklenen etki:** Önce basit RFM ile başlanmalı. Makine öğrenmesi modelini ancak tutundurma aksiyonunun etkisi ölçülebiliyorsa kurmak anlamlı.

**1.6 Kredi riski skoru ve tahsilat önceliklendirme (DSO düşürme)**
- **Ne:** Fatura bazında "geç ödeme olasılığı" tahmin edilir. Tahsilatçının iş listesi tutara göre değil, beklenen tahsilat etkisine göre sıralanır.
- **Gerekli veri:** Fatura ve ödeme geçmişi, vade, limit kullanımı, ihtilaf kayıtları.
- **Kanıt:**
  - 41 sınıflandırıcıyı 8 veri setinde karşılaştıran kapsamlı bir kredi skorlama kıyas çalışması var (A, Lessmann ve arkadaşları, EJOR 2015).
  - IBM ve bir banka ortaklığında tahsilat önceliklendirme prototipi geliştirilmiş. Geç/zamanında ödeme tahmininde %77–81 doğruluk raporlanmış (B/A, KDD çalıştayı).
  - Akademik literatür küçük. DSO düşüşü için bağımsız rakam bulunamadı.
- **Not:** Limit ve blokaj politikası tahminle otomatikleştirilmemeli. Model öneri verir, karar insanda kalır.

**1.7 Çalışma sermayesi: talep tahmini, emniyet stoku, yavaş dönen ve atıl stok**
- **Kanıt:**
  - M5 yarışması: gradient boosting ve sinir ağları istatistiksel yöntemleri geçti. Ancak kazanan LightGBM yaklaşımı basit üstel düzeltme benchmark'ından yalnız yaklaşık %3 daha isabetliydi. Benchmark'ı geçebilen takımlar yalnız yaklaşık %7,5'ti (A). Sonuç perakende verisinden geliyor; metal sektöründe az kalemli ve kesikli talep için genellenmesi tartışmalı.
  - 60.000'den fazla tahmin üzerinde yapılan çalışmada, insan düzeltmelerinden büyük olanlar faydalı çıktı. Küçük ve yukarı yönlü düzeltmeler çoğu zaman isabeti bozdu (A, Fildes ve Goodwin, 2009).
  - Bir metal servis merkezinde stok maliyetinde %25 azalma ve hizmet seviyesinde %15 artış iddia ediliyor (C, GAINSystems). Başka bir vakada çalışma sermayesinde %61 azalma iddia ediliyor (C, Worthington).
- **ERP'de:** Önce istatistiksel bir temel model kurulmalı (Croston ve üstel düzeltme). Planlamacı düzeltmeleri gerekçeleriyle kaydedilmeli ve düzeltmenin katma değeri (FVA) ölçülmeli. Emniyet stoku hizmet seviyesi hedefinden türetilmeli; bu hedef yapılandırılabilir bir politika olmalı. Yaşlanan stok için bir rapor kurulmalı.

**1.8 Satınalma fiyat farkı (PPV) ve tedarikçi puanlama**
- **Ne:** Endekse göre normalize edilmiş satınalma fiyatı farkı ile zamanında teslim, kalite reddi ve sertifika uyumu metrikleri ölçülür.
- **Kanıt:** Yöntem standart bir yönetim muhasebesi pratiği. ERP'ye özgü bağımsız etki rakamı bulunamadı (B/D).

**1.9 Fire ve verim analitiği, artık parça (remnant) yeniden kullanımı**
- **Ne:** Kesim planlama, kesim stoğu problemi (cutting stock) olarak modellenir ve kullanılabilir artıklar dikkate alınır.
- **Kanıt:** Kullanılabilir artıklı kesim stoğu problemi (CSPUL) EJOR'da iki derleme makaleyle ele alınmış: 2014 tarihli tek boyutlu derleme ve 2025/2026 tarihli 1B/2B/3B, 52 makalelik derleme (A). Yöntem olgun. Şirkete özgü fire düşüşü verisi gerektirir.

**1.10 Kapasite, darboğaz ve zamanında teslim analitiği; yük optimizasyonu**
- Darboğaz analizi (OEE, kuyruk) ve araç yükleme klasik yöneylem araştırması konularıdır (A, genel literatür). Bu sektöre özgü ERP vakası bulunamadı.

---

## 2. Karar destek arayüzü

- **İstisna bazlı uyarılar:** Klinik karar destek sistemlerinde uyarı reddetme oranları %90–96'ya ulaşıyor. Uyarı sayısını azaltmak reddetme oranını her zaman düşürmüyor (A, sağlık literatürü).
  - **Çıkarım:** Az sayıda, eşiği yapılandırılabilir, sahibi ve SLA'sı olan uyarı tasarlanmalı. Uyarının işe yarayıp yaramadığı ölçülmeli.
- **KPI karnesi ve detaya inme (drill-down):** Etkisi yönetim literatüründe yerleşik kabul edilir. Rakamsal kanıt bulunamadı (B).
- **What-if ve senaryo simülasyonu:** Örnek: "HRC +%10 olursa marj ne olur?" Senaryo aynı deterministik hesaplayıcı üzerinde çalışmalı. Bağımsız etki rakamı bulunamadı (D).
- **Açıklanabilir öneriler ("next best action"):** Her öneri gerekçe, kullanılan veri ve güven düzeyi göstermeli. Kullanıcı kabul veya red eder; bu geri bildirim kaydedilir. Etki rakamı bulunamadı (D).
- **İşlem anomalisi ve mükerrer fatura:**
  - APQC kıyas verisine göre ödemelerin adet bazında en iyi kurumlarda %0,8'i, medyanda %1,5'i mükerrer veya hatalı (B). Bu oran yalnız mükerrerleri değil, hatalı ödemeleri de kapsıyor.
  - Bir belediye iç denetiminde 237.261 faturada 50 mükerrer ödeme bulunmuş, toplam yaklaşık 5,76 milyon dolar. Bu, adet bazında yaklaşık %0,02, tutar bazında yaklaşık %0,11 ediyor (A, kamu denetimi).
  - Yevmiye kayıtlarında otoenkoder tabanlı anomali tespiti var. F1 skoru düşük (yaklaşık 17–33) ama baseline yöntemleri geçmiş (A/ön baskı, Schreyer ve arkadaşları).
  - **Öneri:** Kural tabanlı kontrollerle başlanmalı (aynı tedarikçi + tutar + tarih yakınlığı + fatura numarası benzerliği). Makine öğrenmesi ikinci katman olarak eklenmeli.
- **Onay risk skoru:** Örneğin fiyat taban altında, kredi limiti aşılmış veya yeni müşteri ise risk skoru yükselir. Sektöre özgü kanıt bulunamadı (D). Skor yalnız onay sırasını ve önceliğini belirlemeli, yetkiyi değiştirmemeli.

---

## 3. Yapay zekâ ve makine öğrenmesi yöntemleri

- **Tahmin:** İstatistiksel ve makine öğrenmesi farkı küçük olabilir; M5'te kazananın avantajı yaklaşık %3'tü (A). Hiyerarşi tutarlılığı, ensemble yöntemler ve istatistiksel temel modelle sürekli kıyaslama önerilir.
- **Doküman yapay zekâsı (fatura ve sipariş okuma):** Güçlü çok modlu dil modelleri OCR hattına eşit veya ondan iyi sonuç verebiliyor (ön baskılar, ör. arXiv 2509.04469, 2603.02789). Ancak hakemli ve ortak kabul görmüş bir fatura benchmark'ı yok; satıcıların doğruluk rakamları küçük örneklemlere dayanıyor (C).
  - **Öneri:** Alan bazlı güven skoru kullanılmalı. Toplam, KDV ve satır toplamı aritmetik olarak çapraz kontrol edilmeli. Güven düşükse insan kuyruğuna gitmeli.
- **Analitik için LLM copilot (text-to-SQL):**
  - Spider 2.0 kurumsal benchmark'ında model ilk yayında yalnız %21,3 başarı gösterdi. Liderlik tablosu sonradan yaklaşık %80'e çıktı (A, ICLR 2025).
  - data.world çalışmasında GPT-4 sıfır-atış SQL'de %16,7 doğruluk aldı; bilgi grafiği eklenince %54,2'ye çıktı (C, satıcı ön baskısı, 43 soru).
  - dbt Labs'ın benchmark'ında semantik katmanla cevaplanabilen sorularda doğruluk %83 ile yaklaşık %100 arasında. Aynı sorularda doğrudan text-to-SQL yaklaşık %50 aldı (C).
  - FinanceBench: GPT-4-Turbo, retrieval ile birlikte soruların %81'ini yanlış cevapladı veya reddetti (A/ön baskı).
  - **Çıkarım:** Yapay zekâya ham SQL açılmamalı. Bunun yerine semantik katman ve şemalı backend araçları ("metric X'i filtre Y ile getir") kullanılmalı. Sayılar deterministik hesaplayıcıdan gelmeli. Şirkete özgü bir değerlendirme soru seti kurulmalı. Kaynak göstermeyen yanıt engellenmeli.
- **Ajan tabanlı süreç otomasyonu:** Gartner, ajan tabanlı yapay zekâ projelerinin %40'ından fazlasının 2027 sonuna kadar iptal edileceğini öngörüyor. Gerekçeler: maliyet, belirsiz değer ve risk kontrolü. Ayrıca "agent washing" uyarısı yapıyor (B).
  - **Öneri:** Dar, geri alınabilir görevler seçilmeli. Ajan yalnız taslak hazırlamalı; kritik komutlar yetki, idempotency ve audit kapısından insan onayıyla geçmeli.
- **Bilinen hata türleri:** Halüsinasyon (kesin görünen ama yanlış sayı), eski veri, master data kirliliği (mükerrer müşteri ve ürün kodları), veri sızıntısı (data leakage), model kayması (drift).
- **Önlemler:** Semantik katman, deterministik hesaplayıcı, insan onayı, altın değerlendirme seti, gölge modda çalıştırma ve A/B ölçümü, model izleme.

---

## 4. Yeni çıkan ama dikkatli uygulanırsa etkili olabilecek fikirler

| Fikir | ERP'de nasıl çalışır | Kanıt |
|---|---|---|
| E-postadan otomatik teklif taslağı | E-posta veya PDF'ten LLM ile ürün, kalite, ebat ve miktar çıkarılır, SKU ile eşleştirilir, fiyat hesaplayıcısı çağrılır, satışçı onaylar | C: bir enerji ekipmanı üreticisinde teklif süresinde %67 kısalma (Sia); RFQ çıkarım doğruluğunun %75'ten %96'ya çıktığı iddiası (Toptal); bir metal servis merkezi SKU tanıma yaklaşımını anlatmış, rakam vermemiş |
| Otomatik artık parça yeniden kullanım önerisi | Gelen sipariş ölçüsü, artık stokla eşleştirilir; CSPUL algoritması çalışır | A (yöntem); şirkete özgü etki ölçülmemiş |
| Hurda fiyat tahmini | Gaussian süreç regresyonu veya NARX modeli, bölgesel hurda serileriyle | A: Çin bölgesel verisiyle çalışmalar var; başka pazarlara aktarılması belirsiz |
| HRC/LME endeks takibiyle alım zamanlaması | Endeks verisi alınır; stok kapsama süresi, açık siparişler ve fiyat bandı uyarısı birleştirilir | B/D: CME'nin eğitim materyali hedgingin spekülasyon değil risk azaltma olduğunu vurguluyor; alım zamanlamasıyla kâr sağlandığını gösteren akademik kanıt bulunamadı. Bir "piyasa zamanlama" motoru spekülatiftir; hedge ve stok politikası insan kararıdır |
| Müşteriye özgü fiyat esnekliği | Kazanma/kaybetme verisinden segment bazlı bid-response eğrisi | A (yöntem); veri az olduğunda segment düzeyinde kalınmalı |
| Üretimin dijital ikizi | Kesim, dilme ve boy kesim hatlarının simülasyonu | D: çelik üretimine özgü hakemli kanıt zayıf; olgunluk ve entegrasyon sorunları raporlanıyor. Tata Steel'de %25 duruş azalması rakamı hakemsiz bir ön baskıdan geliyor |

---

## Öncelik önerisi

Kanıt gücü ve uygulama riski birlikte düşünülerek önerilen sıra:
1. Cep marjı ve hizmet maliyeti
2. Taban fiyat bandı ve endeks bağlantılı ek ücret
3. Teklif kazanma/kaybetme kaydı
4. Kural tabanlı mükerrer fatura kontrolü
5. Yaşlanan stok raporu ve istatistiksel temel tahmin modeli
6. Tahsilat önceliklendirme
7. Semantik katmanlı copilot ve e-postadan teklif taslağı

Fiyat tabanı, kredi limiti, emniyet stoku hizmet seviyesi ve hedge politikası işletme politikasıdır. Bunlar yapılandırılabilir bırakılmalı ve açık karar olarak işaretlenmeli.

---

## Kaynaklar

- M5 Accuracy sonuçları (IJF 2022): https://econpapers.repec.org/RePEc:eee:intfor:v:38:y:2022:i:4:p:1346-1364
- M5'te kazananın benchmark'a göre yaklaşık %3 farkı: https://arxiv.org/pdf/2102.13209
- M5 ve benchmark'ı geçen takımların yaklaşık %7,5 olması: https://eprints.lancs.ac.uk/214016/1/Kolassainpress.pdf
- Fildes ve arkadaşları (2009), tahminlere insan düzeltmesi: https://researchportal.bath.ac.uk/en/publications/effective-forecasting-and-judgmental-adjustments-an-empirical-eva/
- Foresight özeti: https://forecasters.org/wp-content/uploads/Good-and-Bad-Judgment-in-Forecasting_Issue8.pdf
- McKinsey, "The power of pricing": https://www.mckinsey.com/capabilities/growth-marketing-and-sales/our-insights/the-power-of-pricing
- McKinsey, %8,7 güncellemesi: https://www.mckinsey.com/capabilities/growth-marketing-and-sales/our-insights/using-big-data-to-make-better-pricing-decisions
- PROS, malzeme dağıtımı: https://pros.com/learn/i/1305838/3
- Insight2Profit, metal işleyici vakası: https://www.insight2profit.com/resources/case-studies/metals-processor-and-distributor/
- Insight2Profit, dinamik fiyat motoru: https://insight2profit.com/wp-content/uploads/2024/05/Dynamic-Pricing-Engine-Case-Study.pdf
- GAINSystems, Metalwest: https://www.casestudies.com/company/gainsystems/case-study/increasing-customer-service-levels-while-reducing-cost-with-gains
- Worthington Steel vakası: https://www.worthingtonsteel.com/docs/default-source/default-document-library/steel-library/customer-stories/worthington-steel-customer-story-reduced-working-capital.pdf
- Müşteri kârlılığı ve balina eğrisi: https://en.wikipedia.org/wiki/Customer_profitability_analysis
- HBS, müşteri kârlılığı: https://hbswk.hbs.edu/archive/a-balanced-scorecard-approachto-measure-customer-profitability
- Çelik servis merkezinde hizmet maliyeti: https://www.sdcexec.com/sourcing-procurement/manufacturing/article/10290025/cracking-the-cost-analysis-nut
- Bid-response modelleri: https://repository.gatech.edu/entities/publication/44f04717-ebc4-4f15-954e-e7ed83f2a15b
- Lessmann ve arkadaşları (2015), kredi skorlama: https://ideas.repec.org/a/eee/ejores/v247y2015i1p124-136.html
- Alacak tahmini (IBM): https://arxiv.org/pdf/2008.07363
- Fatura ödeme tahmini: https://arxiv.org/pdf/1912.10828
- B2B churn, Geiger vakası (INFORMS): https://pubsonline.informs.org/doi/10.1287/inte.2025.0292
- Toptancılıkta churn: https://mdpi.com/2076-3417/12/10/5001
- CSPUL derlemesi (2014): https://econpapers.repec.org/RePEc:eee:ejores:v:236:y:2014:i:2:p:395-402
- CSPUL derlemesi (2025/26): https://ideas.repec.org/a/eee/ejores/v328y2026i1p1-14.html
- Hurda fiyat tahmini: https://library.kab.ac.ug/Record/doaj-art-995f28c0587e4d54add532bca7971b27
- Hurda fiyat tahmini (NARX): https://www.informahealthcare.com/doi/abs/10.1080/03019233.2023.2218243
- CME, HRC ile hedging: https://www.cmegroup.com/education/courses/hedging-price-risk-with-steel-future-contracts/hedging-with-hot-rolled-coil-hrc-steel-futures-contracts.html
- APQC, mükerrer ödeme (CFO.com): https://www.cfo.com/news/metric-of-the-month-detect-and-prevent-duplicate-or-erroneous-payments/656852/
- Belediye denetimi, mükerrer ödeme: https://rva.gov/sites/default/files/2026-05/OCA%202026-09%20Continuous%20Monitoring%20-%20Duplicate%20Payments%205.11.26.pdf
- Schreyer, yevmiye anomalisi: https://arxiv.org/abs/1709.05254
- Alarm yorgunluğu (AHRQ): https://psnet.ahrq.gov/primer/alert-fatigue
- Alarm yorgunluğu çalışması: https://pmc.ncbi.nlm.nih.gov/articles/PMC6938713
- Spider 2.0: https://arxiv.org/abs/2411.07763
- Spider 2.0 liderlik tablosu: https://spider2-sql.github.io/
- data.world KG benchmark'ı: https://arxiv.org/abs/2311.07509
- dbt, semantik katman ve text-to-SQL: https://docs.getdbt.com/blog/semantic-layer-vs-text-to-sql-2026
- FinanceBench: https://arxiv.org/abs/2311.11944
- Fatura okuma benchmark'ı: https://arxiv.org/html/2509.04469v1
- OCR gerekliliği çalışması: https://arxiv.org/html/2603.02789
- Gartner, ajan tabanlı yapay zekâ: https://www.gartner.com/en/newsroom/press-releases/2025-06-25-gartner-predicts-over-40-percent-of-agentic-ai-projects-will-be-canceled-by-end-of-2027
- Sia, RFQ aracı: https://www.sia-partners.com/en/insights/publications/67-faster-quoting-with-ai-powered-rfq-tool
- Toptal, RFQ: https://www.toptal.com/case-study/supply-chain-firm-lifts-rfq-accuracy-to-96
- AI ve metal imalat tedarik zinciri: https://www.thefabricator.com/thefabricator/blog/cadcamsoftware/how-ai-could-transform-the-metal-fabrication-supply-chain
- Dijital ikiz, sistematik derleme (WSC 2025): https://www.informs-sim.org/wsc25papers/con149.pdf
- Dijital ikiz, Nature Computational Science: https://www.nature.com/articles/s43588-024-00603-w
- Dijital ikiz, metal işleme (Frontiers): https://www.frontiersin.org/articles/10.3389/fmech.2025.1655565/full
