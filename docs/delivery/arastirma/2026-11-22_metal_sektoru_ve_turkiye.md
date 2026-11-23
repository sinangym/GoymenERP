<!-- Ajan araştırma çıktısı (22 Kasım 2026); karar değildir. Birleşik değerlendirme: docs/delivery/ERP_FIKIR_HAVUZU_v1.md -->

# Metal/çelik dağıtım, servis merkezi ve hafif işleme şirketleri için ERP araştırması (Türkçe, 22 Kasım 2026)

**Yöntem ve sınırlar.** WebSearch çalıştı. WebFetch ise ziyaret edilen hostlarda (parasut.com, alomaliye.com) DNS hatası verdi; bu oturumun ağ izin listesi bu siteleri kapsamıyor. Bu yüzden aşağıdaki bulgular arama motorunun sayfa özetlerine dayanıyor; birincil metinleri (tebliğ, vendor dokümanı) satır satır okuyamadım. Vendor ifadeleri **[İDDİA]** diye işaretli. Türkiye mevzuatı bölümünde kaynaklar çeliştiği yerleri **[BELİRSİZ]** diye işaretledim. Bu noktalar GİB ve Resmî Gazete metniyle ya da mali müşavirle teyit edilmeli.

---

## 1. Metal sektörüne özgü ERP fonksiyonları

**1.1 Heat/lot ve sertifika izlenebilirliği (EN 10204 3.1)**
- 3.1 belgesi parti (batch) bazındadır. Üreticinin üretimden bağımsız yetkili muayene temsilcisi imzalar. 3.1 ve 3.2 belgeleri heat numarasına bağlıdır, 2.1 ve 2.2 bağlı değildir. Malzeme üzerindeki işaret, paket evrakı ve sertifika aynı heat numarasını taşımalıdır (ikincil kaynaklar: projectmaterials, materialwelding).
- ERP için anlamı şu: alımda sertifika (MTC/MTR) heat/parti kaydına bağlanır. Dilme veya boy kesme ile oluşan her çocuk bobin, sac ve artık bu bağı **miras alır**. Sevkte müşteriye otomatik iletilir.
  - Epicor heat sertifikası ve test raporu takibini, ayrıca "heat sertifikası/test sonucu gibi özniteliklerle planlama"yı söylüyor [İDDİA].
  - unitop (Business Central tabanlı) tedarikçi bildiriminden fabrika sertifikalarını arşivleyip teslimat partilerine atadığını söylüyor [İDDİA].
  - Enmark Eniteo "fiziksel/MTR özelliklerini görüntüleme" ve heat bazlı elektronik izlenebilirlik sunuyor [İDDİA].
- Dijital sertifika: S1Seven'in SAP S/4HANA eklentisi makine-okunur sertifika üretip doğruluyor [İDDİA]. EN 10204'ün elektronik belgenin geçerliliği için ne şart koştuğunu bulamadım [BELİRSİZ]. 3.1 belge sonradan 3.2'ye yükseltilemez.

**1.2 Boyutsal stok ve çift birim**
- SAP Mill Products sıcak haddelenmiş bobini özellik bazlı (konfigüre edilebilir) malzeme olarak modelliyor: kalite, norm, kalınlık, genişlik vb. Her bobin kendi boyutu, kalite verisi ve ağırlığıyla ayrı bir parti numarasıdır (SAP blog/help). Bu kaynaklar ECC 6.0 dönemine ait; S/4HANA için ayrıca doğrulanmalı.
- Epicor "Advanced UoM" ile tek malzemeyi ağırlık, uzunluk ve büküm gibi birden çok boyutta izlediğini söylüyor [İDDİA]. Infor CloudSuite Industrial Enterprise boyutsal stok ve malzeme fiyat sürşarjı yönetimi sunduğunu söylüyor [İDDİA]. Crowe (D365 F&SCM tabanlı) boyut/öznitelik bazlı stok sunduğunu söylüyor [İDDİA].
- Pratikte gereken: kg, adet ve metre arasında teorik ağırlık (yoğunluk × boyut) ile tartılmış gerçek ağırlığın birlikte tutulması, aradaki farkın raporlanması ve fiyatlamanın hangi birimden yapıldığının satır bazında açık olması. Bu, sektör uygulamasından yaptığım çıkarımdır, tek bir kaynağa dayanmaz.

**1.3 İşleme iş emirleri (dilme/slitting, boy kesme/CTL, kaplama)**
- Dilmede bir bobin birden çok şerit verir (birden-çoğa ilişki). Yan kesim (trim) hurda olarak geri kazanım değeriyle kaydedilmeli. Boy kesmede kalan uç parça ya kullanılabilir artıktır ya hurdadır (pixeltech blog; ikincil).
- Aptean Metals ERP'de "Roll/Slit Planning" modülü var [İDDİA]. SAP tarafında kesim optimizasyonu çoğunlukla partner çözümüyle yapılıyor (T.CON TRIM SUITE, ConCircle) ve slitter kurulumu planlanmış dilme desenleriyle yönlendiriliyor.
- Bağımsız bir 2026 karşılaştırması uyarıyor: dilme, düzeltme, kesme ve çizelgeleme mantığı çoğu üründe ek konfigürasyon ya da planlama eklentisi gerektiriyor. Vendor demosunda kendi siparişinizle "bobin → çocuk bobin soy ağacı" ve "artığın stoğa dönüşü" gösterilmeli.

**1.4 Artık (remnant/offcut) ve nesting**
- Epicor artık ve fireler için yeni stok kalemi açtığını ve üçüncü parti nesting yazılımıyla entegre olup artıkları otomatik takip ettiğini söylüyor [İDDİA].
- Invera SM3 servis merkezinin hurda politikasına göre artık ve iskeleti hurdaya ayırabiliyor ya da stoğa geri alabiliyor [İDDİA].
- Ticari yayınlar artığın elle yeniden girilmeden, heat soy ağacıyla otomatik izlenmesini öneriyor. Enmark doğrusal nesting sunuyor [İDDİA].

**1.5 Fason / tolling (müşteri malı işleme)**
- Invera 2011'de STRATIX/TOLL modülünü çıkardı. Modül müşteri malının dilme ve boy kesme gibi işlemlerini, ayrı faturalama ve raporlama ile yönetiyor [İDDİA; tarihli basın bülteni].
- Sikich'in D365 makalesi dış işleme (outside processing) için şunları anlatıyor: bobin rezerve kalırken işleyiciye transfer edilebiliyor, işleyicinin orijinal tonaj mı üretilen tonaj mı üzerinden faturaladığı kaydediliyor.
- Infor kaplama ve ısıl işlem gibi taşeron adımlarını maliyet ve takvim görünürlüğüyle koordine ettiğini söylüyor [İDDİA].
- Gereken iki şey ayrı tutulmalı: (a) **müşteri malı** stoğu, kendi envanter değerine girmeden fiziksel olarak izlenmeli; (b) **bizim taşerona** gönderdiğimiz mal, fason çıkış/giriş irsaliyesi ve fire mutabakatıyla izlenmeli.

**1.6 Fiyatlama: baz + ekstralar + sürşarj**
- Çelik fiyat kitapları baz fiyata kalınlık, genişlik, kalite ve kaplama ekstraları ile bobin ağırlığı kademeleri ve minimum miktar ekstrası ekler. Alaşım sürşarjı (paslanmazda Ni/Cr/Mo) referans maliyet ile güncel element maliyeti arasındaki farktır ve **sevk tarihinde** uygulanır (Rolled Alloys; Steel Market Update'teki fiyat kitapları).
- ERP için anlamı: yürürlük tarihli ekstra tabloları ve endeks bazlı sürşarj, fiyatın sabit kodlanmaması. Invera fiyat kitabı ve teklifte işleme süresi/maliyet/ücret tahmini ile kârlılık tahmini sunduğunu söylüyor [İDDİA, 2010].

**1.7 Diğer sektör fonksiyonları**
- **Kantar/terazi entegrasyonu:** ERP/MES entegrasyonu etiket bilgisini doğru dilinmiş bobine bağlamak için öne çıkıyor. Bulduğum kaynaklarda kantar entegrasyonuna dair somut bir vendor dokümanı yoktu [BELİRSİZ].
- **Etiket ve barkod:** Infor WMS sac, levha, çubuk ve bobin stokunu çok sahalı yönettiğini söylüyor [İDDİA].
- **Verim ve fire analitiği:** Her adımda giriş ve çıkış ağırlığını kaydetmek kaybı görünür ve atfedilebilir yapar.
- **Konsinye:** IFS veri sayfasında genel bir yetenek olarak geçiyor; metal sektörüne özel değil.
- **EDI:** Invera Stratix/Edix.
- **IFS:** Metal üreticileri sayfası var. SSAB Finlandiya'da IFS Cloud kullanıyor (EAM, kalite, WMS, mobil iş emri). Servis merkezine özgü heat veya dilme fonksiyonunu doğrulayamadım.

---

## 2. Genel ERP modülleri: bu tip şirket için önem sırası (değerlendirme)

| Öncelik | Modül | Neden |
|---|---|---|
| Kritik | Satış/teklif + CRM | Teklif hızı servis merkezinde rekabet unsurudur. Fiyat kitabı ve kârlılık teklif anında görünmeli. |
| Kritik | WMS (bin/lokasyon, el terminali) | Bobin ve paket bazında yer, heat ve boyut takibi |
| Kritik | Kalite (sertifika, NCR/CAPA, kalite blokesi) | Enmark "kalite blokesi"ni temel modül sayıyor [İDDİA] |
| Kritik | Maliyet muhasebesi | Fiili ağırlık, fire ve yan ürün (hurda) değerlemesi; standart ve fiili maliyet karşılaştırması |
| Kritik | Tahsilat/risk/kredi limiti, çok döviz, kur farkı | Yüksek tutarlı ve dövize endeksli çelik ticareti |
| Kritik | Banka entegrasyonu, nakit akış tahmini | Aşağıda §3 |
| Yüksek | Kapasite/çizelgeleme (dilme hattı) | Hat setup süresi ve desen optimizasyonu |
| Yüksek | Bakım (CMMS) | Slitter ve CTL hattı duruşu doğrudan teslimat kaybıdır; SSAB örneği EAM ve mobil iş emri |
| Yüksek | BI/dashboard | Enmark EnVision (2024) [İDDİA] |
| Orta | Müşteri/tedarikçi portalı | Sertifika indirme, sipariş durumu, e-Mutabakat |
| Orta | Doküman yönetimi | MTC arşivi; ISO denetimi |
| Orta | Sabit kıymet, bütçe | Standart ihtiyaç |
| Orta | Talep tahmini | Stok ticaretinde faydalı; Invera "envanter planlama ve tahmin" sunuyor [İDDİA] |
| Düşük/dış | İK/bordro | Türkiye'de genellikle yerel bordro paketiyle entegre edilir (değerlendirme) |
| Düşük | Konsolidasyon | Grup yapısı varsa |
| Düşük | Kredi sigortası | Politika kararı; ERP yalnız limit verisini tutar |

---

## 3. Türkiye: zorunlu ve beklenen yükümlülükler (Kasım 2026 itibarıyla)

**3.1 e-Fatura ve e-Arşiv**
- Genel e-Fatura haddi: 2025 brüt satış hasılatı **3 milyon TL** ve üzeri olanlar ertesi yılın 1 Temmuz'unda geçer. Motorlu taşıt, gayrimenkul ve e-ticaret gibi kollarda ayrı bir 500 bin TL haddi var. Bir kaynak 2 milyon TL yazıyor [BELİRSİZ].
- **589 Sıra No.lu VUK Genel Tebliği** (RG 31.12.2025) iki şey getiriyor:
  - e-Fatura veya e-Arşiv kapsamında olmayan mükellefler **1.1.2026'dan itibaren tutara bakılmaksızın** e-Arşiv düzenler.
  - Basit usul ve işletme hesabı mükellefleri için bu tarih 1.1.2027'dir. O tarihe kadar vergiler dahil 3.000 TL'yi aşmayan faturalar kâğıtta kalabilir.
- **588 Sıra No.lu Tebliğ**: 2026 fatura düzenleme haddi 12.000 TL (2025'te 9.900 TL). Özel usulsüzlük cezası belge başına 17.000 TL'den başlıyor [ikincil kaynak].
- Bir metal servis merkezi neredeyse kesin olarak bilanço esaslı ve e-Fatura mükellefidir. Pratikte faturaların tamamı e-Fatura veya e-Arşiv olur.

**3.2 e-İrsaliye: demir-çelik için sektörel zorunluluk**
- 509 Sıra No.lu VUK Genel Tebliği kapsamı: **GTİP 72 (demir-çelik) ve GTİP 73 (demir veya çelikten eşya) imal, ithal veya ihraç eden** mükellefler (basit usul hariç) e-İrsaliyeye geçmek zorundadır. Kaynaklara göre e-Fatura mükellefi olma şartı sonradan kaldırıldı ve yükümlülük ciro haddinden bağımsız. GİB 2021'de sektörün kapsamdan çıkarıldığı haberini yalanladı.
- **Açık nokta:** Dilme, boy kesme ve kaplamanın GTİP 72/73 "imal" sayılıp sayılmadığı, ve yalnız ticaret yapan ama ithalatı olmayan bir dağıtıcının durumu [BELİRSİZ]. Mali müşavir veya özelgeyle netleştirilmeli. Ciro haddiyle bile (10 milyon TL ve üzeri e-Fatura mükellefleri) büyük olasılıkla kapsama girilir. Bu haddin başlangıcı için kaynaklar 1.1.2026 ile 1.7.2026 arasında çelişiyor [BELİRSİZ].
- **İDİS** (İnşaat Demiri İzleme Sistemi): kapsamdaki e-irsaliyelerde sevkiyat numarası ve etiket numarası zorunlu. 2025 hasılatı 1 milyon TL ve üzeri olanlar için 1.7.2026 (Sovos). İnşaat demiri ticareti varsa ERP bu alanları taşımalı.
- Fason gönderimler (kaplama/boya taşeronu) da irsaliyeyle sevk edilir. ERP'de fason çıkış ve dönüş e-İrsaliyesi olmalı (genel uygulama).

**3.3 e-Defter**
- e-Fatura mükellefleri e-Deftere de geçer. 1.7.2026'da e-Faturaya geçenler için e-Defter başlangıcını kaynakların çoğu 1.1.2027 olarak veriyor; bir kaynak 2026 yazıyor [BELİRSİZ].
- Berat yükleme süreleri kaynaklarda çelişiyor; resmi takvimden alınmalı. Saklama süresi 10 yıl.

**3.4 KDV tevkifatı: demir-çelik**
- Dayanak: KDV Genel Uygulama Tebliği I/C-2.1.3.3.8 "Demir-Çelik Ürünlerinin Teslimi". Oran **5/10**: 1.5.2022'de 4/10 olarak başladı, **1.11.2022'den itibaren 5/10**.
- Tevkifat KDV mükelleflerine ve belirlenmiş alıcılara yapılan teslimlerde uygulanır. Beyanname işlem kodu **627**; iade tablosunda 448.
- Hurda ve atık teslimi ayrı bölümdedir (I/C-2.1.3.3.3), oranı **7/10**. Külçe kodları: 617 (hurdadan elde edilen) ve 618 (diğer bakır, çinko, demir-çelik, alüminyum, kurşun külçe).
- **Hurda satışı için 609 kodunu doğrulayamadım.** Hurda kodu resmi listeden alınmalı.
- Özelgelere göre kapsam ürün bazında değişiyor. Tel ve dikenli tel tevkifata tabi, panel çit değil. Çelik konstrüksiyon montajı hizmettir. İthal edilip iade edilen ve niteliği değişmemiş mal tevkifatsız teslim edilir.
- ERP için anlamı:
  - Ürün kartında GTİP ve tevkifat kategorisi tutulmalı.
  - Satış satırında alıcı tipine göre tevkifat otomatik belirlenmeli.
  - Hurda satışı 7/10 olarak ayrı ele alınmalı.
  - Satıcı tarafında tevkifattan doğan **KDV iade alacağı** takip edilmeli.
  - Kur farkı faturalarındaki tevkifat ayrıca ele alınmalı (muhasebetr'de konu edilmiş).

**3.5 Ba-Bs formları kaldırıldı**
- 565 Sıra No.lu VUK Genel Tebliği (RG 25.09.2024) ile Eylül 2024 dönemi ve sonrası için Ba-Bs bildirimi verilmiyor. Hâlâ "5.000 TL sınırı" anlatan sayfalar eski mevzuata dayanıyor. ERP'de Ba-Bs modülü artık zorunlu değil; cari mutabakat için yararlı olabilir.

**3.6 Enflasyon düzeltmesi yapılmıyor**
- 7571 sayılı Kanun md. 34 (RG 25.12.2025) VUK'a Geçici 37. maddeyi ekledi. **2025, 2026 ve 2027 hesap dönemlerinde** (geçici vergi dönemleri dahil) enflasyon düzeltmesi yapılmıyor. Cumhurbaşkanı süreyi uzatabilir.
- ERP enflasyon muhasebesi altyapısını korumalı ama varsayılanı kapalı tutmalı. Bu bir politika parametresidir.

**3.7 KVKK**
- VERBİS kayıt istisnası: yıllık çalışan sayısı 50'den az **ve** yıllık mali bilanço toplamı 100 milyon TL'nin altında olan, ana faaliyeti özel nitelikli veri işleme olmayan veri sorumluları. Kurul kararları 2023/1154, 2025/1572 (RG 1.10.2025) ve 2025/2393.
- Muafiyet yalnız kayıt yükümlülüğünü kaldırır. Aydınlatma, veri güvenliği ve ilgili kişi başvurularına yanıt verme yükümlülükleri sürer.
- ERP'de rol bazlı erişim, loglama, saklama ve imha politikası olmalı. Personel ve iletişim kişisi verisi var.

**3.8 Yasal zorunlu olmayan ama yerleşik pratikler**
- **e-Mutabakat:** Yasal zorunluluk değil, gönüllü pratik (Sovos). Cari hesap mutabakatı için yaygın.
- **Banka entegrasyonu:** MT940 ekstresiyle cari, banka, muhasebe ve çek kayıtlarının otomatik oluşturulması (Logo/Netsis dokümanı). Bankaların online entegrasyon API'leri de var (ör. Ziraat).
- **TCMB kurları:** Değerlemede TCMB döviz alış kuru kullanımı VUK pratiğidir. Hangi kur ve tarihin uygulanacağını birincil kaynakla doğrulayamadım [BELİRSİZ].
- **e-SMM:** Serbest meslek erbabına özgü bir belgedir; şirketin düzenlemesi gerekmez. Yalnız alınan e-SMM'ler işlenir.

---

## 4. İş yükünü azaltan kullanılabilirlik pratikleri

- **Rol bazlı çalışma alanları:** Microsoft D365 F&O "workspace" deseni şunlardan oluşur: görev başlatan sayaç kutucukları (tiles), sekmeli iş listeleri, grafik/Power BI ve ilgili bağlantılar. Önemli bilgi kaydırmadan görünmeli. Business Central Role Center rehberi de günlük işte en önemli bilgiye hızlı erişimi öneriyor. Bu tasarım rehberidir, ölçülmüş verim kanıtı değildir.
- **İstisna bazlı yönetim:** Sayaç kutucuklarının kaydedilmiş sorguya bağlanması örneğin "sertifikası eksik heat", "kalite blokeli bobin", "vadesi geçen" gibi listeleri öne çıkarır. D365 deseninin özü budur.
- **Hızlandırıcılar, toplu işlem, kayıtlı görünümler:** Nielsen Norman Group'un "flexibility & efficiency" heuristiği ve UI accelerators makalesi şunu öneriyor: kısayolları sık yapılan görevler için tasarlayın, kademeli ekleyin, satır checkbox'ıyla toplu işlem sunun. Komut paletinin "%25 artış" sağladığı gibi istatistikler kaynaksız; bunları kullanmayın.
- **Barkod ve el terminali:** Yaygın tekrarlanan "elle girişte 300 tuşta 1 hata, barkodda 3 milyonda 1" rakamının birincil çalışmasını bulamadım (vendor blogu). Klinik bir çalışma 84.000 okutmada 1 yanlış kimlik ölçtü. Asıl risk süreç dışı "workaround"lar: bir hastane çalışmasında ilaçların %10,3'ünde teknoloji atlanmış. Sonuç: barkod hatayı büyük ölçüde azaltır, ama kesin oran iddiası yerine akışın atlanmasını engelleyen tasarım önemlidir.
- **Rol bazlı UI'nin verime etkisi:** Fiori ile SAP GUI'yi karşılaştıran 12 katılımcılı bir tez sınırlı iyileşme buldu. Kontrollü bir tuş/süre çalışması bulamadım. Kanıt "destekleyici ama zayıf".
- **Atölye ekranı ve tablet:** SSAB'nin IFS kurulumunda mobil iş emri var. Hat başında büyük butonlu, tartı ve etiket entegre ekran sektör pratiği; bunun nicel kanıtını bulamadım.
- **Akıllı varsayılanlar ve rehberli akışlar:** Teklifte fiyat kitabından otomatik fiyat ve işleme ücreti tahmini (Invera) bunun örneği [İDDİA].

---

## 5. Özet tasarım çıkarımları (değerlendirme)

1. Heat/parti ve sertifika bağı, dilme ve boy kesme ile oluşan soy ağacı (genealogy) ve artığın stoğa dönüşü çekirdek veri modelidir. Vendor'ların en çok "özelleştirme gerekir" dediği alan da burasıdır.
2. Fiyat = baz + yürürlük tarihli ekstralar + sevk tarihli sürşarj. Bunlar tek bir backend hesaplayıcıda (calculator) yapılmalı.
3. Teorik ve gerçek ağırlık ile çok birimli satır zorunludur.
4. Fason iki yönlüdür: müşteri malı ve bizim taşerona gönderdiğimiz mal.
5. Türkiye'de en az şunlar gerekli: e-Fatura/e-Arşiv, sektörel e-İrsaliye (GTİP 72/73; varsa İDİS alanları), e-Defter, 627 kodlu 5/10 ve hurdada 7/10 tevkifat ile KDV iade takibi, MT940/banka API, KVKK.
6. Ba-Bs kaldırıldı. Enflasyon düzeltmesi 2025-2027 arasında kapalı.
7. Tahminle kilitlenmemesi, parametre olarak bırakılması gerekenler:
   - GTİP 72/73 "imal" yorumu
   - e-İrsaliye ve e-Defter başlangıç tarihleri
   - hurda tevkifat kodu
   - TCMB kur tipi

---

## Kaynaklar
- https://www.epicor.com/en/solutions/industries/manufacturing/metal-service-centers/
- https://www.epicor.com/en/solutions/industries/distribution/ptw-metal-services/
- https://www.epicor.com/en/solutions/industries/manufacturing/fabricated-metals/
- https://www.infor.com/en/industries/industrial-manufacturing
- https://infor.com/industries/industrial-manufacturing/metal-fabrication
- https://www.infor.com/blog/metal-fabrication-ceo-competitive-edge
- https://www.crowe.com/services/consulting/microsoft/metals/metals-erp-solution-for-metal-service-centers
- https://www.crowe.com/services/consulting/metals/crowe-metals-accelerator
- https://www.gob.de/en/unitop-business-solutions/unitop-industry/metal-trading-service-centre
- https://modernmetals.com/software/enmark-systems-inc
- https://softwareconnect.com/reviews/enmark-eniteo-erp/
- https://www.prweb.com/releases/2014/10/prweb12231423.htm (Invera STRATIX)
- https://atrisha-trisha12.blogspot.com/2012/01/invera-announces-toll-processing.html (STRATIX/TOLL bülten kopyası)
- https://www.prweb.com/releases/monarch-steel-implements-stratix-erp-with-no-customizations-in-4-months-867669347.html
- https://pixeltech.ai/blog/erp-for-steel-processing-slitting-cutting-scrap/
- https://top10erp.org/blog/why-these-are-the-top-erp-systems-for-metals-and-steel-fabricators
- https://aptean.com/en/solutions/erp/products/aptean-metals-erp
- https://blogs.sap.com/2018/01/25/extending-fiori-apps-with-batch-classification-information-in-sap-s4-hana-1709/
- https://help.sap.com/saphelp_erp60_sp/helpdata/en/0d/cfc353b677b44ce10000000a174cb4/content.htm
- https://www.sap.com/products/scm/partners/tcon-gmbh-co-kg-mes-cat-public.html
- https://www.sap.com/swiss/products/erp/partners/s1seven-gmbh-s1seven-connect-for-sap-s4hana.html
- https://www.sikich.com/insight/outside-production-processing-for-sheet-metal-coils-in-dynamics-365/
- https://www.ifs.com/en/industries/manufacturing/metals
- https://blog.ifs.com/ifs-cloud-improves-the-maintenance-of-steelmaking-facilities-in-finland/
- https://blog.projectmaterials.com/quick-answers/epc/en-10204-certificate-types/
- https://www.materialwelding.com/material-test
- https://www.rolledalloys.com/what-are-stainless-steel-and-nickel-alloy-surcharges/
- https://www.turmob.org.tr/ekutuphane/Read/9208c1d3-f311-468d-9dd7-0d50a202bd6c (589 Tebliğ özeti)
- https://finrota.com/blog/2026-e-fatura-zorunluluk-limitleri-ve-gecis-rehberi
- https://www.stb-cpaturkey.com/2026-fatura-duzenleme-tutari-sinirie-arsiv-fatura-uygulamasi/
- https://www.turmob.org.tr/ekutuphane/Read/12e620ef-36bd-454a-8f6c-87e866549c0c (2026 VUK hadleri)
- https://gib.gov.tr/node/151354/pdf (e-İrsaliye GTİP 72/73)
- https://www.alomaliye.com/2020/06/15/e-irsaliye-uygulamasinin-kapsami-genisliyor/
- https://sovos.com/tr/blog/kdv/sirketler-icin-2026-e-donusum-takvimi-ve-zorunluluklar/
- https://kapukaya.dev/blog/e-donusum-takvimi-2026-2027.html
- https://faturaport.com/blog/on-muhasebe/e-defter-gecis-hadleri-2026
- https://www.defterbeyan.gov.tr/documents/kilavuz/kdv-genel-uygulama-tebliginde-degisiklik-yapilmasina-dair-41-seri-no_lu-teblig-kapsaminda-yapilan-duzenlemeler(1).pdf
- https://www.muhasebetr.com/yazarlarimiz/vedaterdem/0256/
- https://www.muhasebetr.com/yazarlarimiz/cengizdemir/001/
- https://www.bbdas.com.tr/bbdas-2022-114-demir-celik-urunleri-teslimlerinde-kdv-tevkifati-konusunda-verilen-ozelgeler-g-2130
- https://mdpgroup.com/blog/tevkifat-kodlari-nelerdir/
- https://www.qnbesolutions.com.tr/blog/ba-bs-formu-bildirim-siniri-2026da-kaldirildi-mi
- https://www.cottgroup.com/tr/mevzuat/item/2025-2027-yillari-arasinda-enflasyon-duzeltmesi-yapilmayacak
- https://ismmmo.org.tr/dosya/5798/Mevzuat-Dosya/25122025-enflasyon-duzeltmesi.pdf
- https://www.roedl.com.tr/tr/tema/sirkuler/2025/verbis-kayit-ve-bildirim-yukumlulugu-istisnalarinda-degisiklik-yapilmasi
- https://www.erdem-erdem.av.tr/bilgi-bankasi/verbis-kayit-yukumlulugune-iliskin-istisna-kriteri-degistirildi
- https://sovos.com/tr/blog/kdv/e-mutabakat-nedir/
- https://docs.logo.com.tr/download/attachments/50680033/banka_modulu_yenilikleri.pdf?api=v2
- https://www.ziraatbank.com.tr/z-donusum/e-donusum/online-banka-entegrasyonu
- https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/user-interface/build-workspaces
- https://learn.microsoft.com/dynamics365/fin-ops-core/dev-itpro/user-interface/page-navigation
- https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/developer/devenv-designing-role-centers
- https://www.nngroup.com/articles/flexibility-efficiency-heuristic/
- https://www.nngroup.com/articles/ui-accelerators/
- https://theses.cz/id/7tnw07/zaverecna_prace.pdf (Fiori ve GUI tezi)
- https://waspbarcode.com/buzz/why-barcodes-almost-never-fail (vendor; birincil kaynağı yok)
- https://pmc.ncbi.nlm.nih.gov/articles/PMC4321427
- https://pmc.ncbi.nlm.nih.gov/articles/PMC5297213/
