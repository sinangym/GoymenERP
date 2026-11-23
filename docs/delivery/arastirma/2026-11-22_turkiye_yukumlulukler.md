<!-- Ajan araştırma çıktısı (22 Kasım 2026); karar değildir. Birleşik değerlendirme: docs/delivery/ERP_FIKIR_HAVUZU_v1.md -->

# Göymen ERP: Türkiye yasal ve fiilî yükümlülükler araştırması (22 Kasım 2026)

Kapsam ve yöntem: Repoda hiçbir dosyayı değiştirmedim. Önce `docs/delivery/ERP_BOSLUK_ANALIZI_v1.md` §5'i okudum, sonra yaklaşık 45 web araması yaptım.

**Önemli sınırlama:** Birincil metinlerin çoğunu açamadım. Bu oturumun ağ ayarlarında `www.alomaliye.com`, `www.turmob.org.tr` ve `orgtr.org` izinli değil, bu yüzden bu sitelerden sayfa çekilemedi. Resmî Gazete ve tebliğ metinlerini doğrudan okumadım; aşağıdaki bilgiler arama sonuçlarındaki özetlere ve çoğunlukla ikincil kaynaklara (danışmanlık bültenleri, mali müşavir siteleri) dayanıyor. Rakamlar yalnız kaynakta açıkça gördüklerim. Doğrulayamadığım her şey **[BELİRSİZ]** olarak işaretli.

Tablolardaki "Bize?" sütunu: **E** evet, **H** hayır, **B** belirsiz.

---

## 0. §5'teki mevcut maddelere düzeltme ve güncellemeler

| Konu | §5 / görevdeki ifade | Güncel bulgu | Kaynak |
|---|---|---|---|
| Ba-Bs | "Eylül 2024'te kaldırıldı" | Doğru. Dayanak **565 Sıra No.lu VUK GT** (RG 25.09.2024, 32673), Eylül 2024 döneminden itibaren. | ismmmo.org.tr/dosya/5020/…babs-kaldirildi.pdf ; kpmgvergi.com/…/2746 |
| 589 sayılı tebliğ | "2026'dan itibaren tutardan bağımsız e-Arşiv" | 589 (RG 31.12.2025), 509'un IV.2.4.3 bölümündeki tarihleri bazı gruplar için **1 yıl öteledi**. Basit usul ve işletme hesabı 2027'ye kaydı; bilanço esası için erteleme görünmüyor. Bilanço esasındaki bizim şirketler için §5 büyük olasılıkla doğru, yine de **[BELİRSİZ]**, teyit edilmeli. | alomaliye.com/2025/12/31/vergi-usul-kanunu-genel-tebligi-sira-no-589/ ; mondaq e-arşiv 2027 |
| KDV tevkifatı demir-çelik | 5/10, kod 627 | Doğrulandı: 1.11.2022'den beri 5/10 (önce 4/10), beyannamede kod 627. **Ek bulgu:** 5/10 tevkifatla demir alan tüccar, malı satarken de aynı oranda tevkifat uygular. Biz tüccar olduğumuz için satışlarda da sürekli tevkifat ve devreden KDV oluşur. | muhasebenews.com/5-10-kdv-tevkifatli-demir-alan-firma… ; parasut.com/blog/demir-celik-sektoru-kdv-tevkifati |
| Hurda tevkifatı | 7/10 | Metal hurda teslimi **KDVK 17/4-g ile istisnadır**. Satıcı istisnadan vazgeçerse (KDVK 18) tevkifat 7/10'dur; 1.4.2020'den beri bu oranda. Hurdadan elde edilen hammadde teslimi için 9/10 oranı geçiyor **[BELİRSİZ]**. Hurda tevkifat kodu (609/610?) **doğrulanamadı [BELİRSİZ]**. | muhasebenews.com/hurdalarda-kdv-tevkifati-uygulamasi/ |
| Belge saklama | Görevde "VUK 10 yıl" | **Düzeltme:** VUK 253'te süre **5 yıl** (izleyen takvim yılından başlar), TTK 82'de **10 yıl**. ERP'de esas alınacak süre 10 yıldır. İnceleme veya dava sürüyorsa imha dondurulur. | turmob.org.tr Basın Arşivi "VUK'a göre defter ve belgelerin saklanma süreleri" ; sovos.com/tr/blog/kdv/e-arsiv-faturalarinin-uzun-donemli… |
| e-Defter berat | — | 2026'dan itibaren berat yükleme **aylık ya da geçici vergi dönemi** bazında seçilebiliyor; 2/2/2026'ya kadar seçim yapmayan aylık sayılıyor. 2026'da çok sayıda süre uzatma sirküleri çıktı (VUK-198, 201, 202). Yükleme Mikro tarafında yapılıyor, ERP için yalnız fiş aktarımının zamanında olması gerekir. | alomaliye.com/2026/01/20/e-defter-ve-berat-dosyalarinin-aylik… ; pwc.com.tr e-Dönüşüm Bülteni 2026/14 |
| e-Tebligat | (yok) | **Yeni:** 7587 sayılı Kanun VUK 107/A'yı yeniden yazdı. **VUK GT 596** (RG 11.10.2026, 33397) 456 sayılı tebliğin yerine geçti. Tebligat, sisteme iletildiği tarihi izleyen **5. günün sonunda** yapılmış sayılıyor. Mevcut mükellefler için son tarih olarak 30.11.2026 geçiyor (tek ikincil özet, **[BELİRSİZ]**). | hukukihaber.net/vergi-usul-kanunu-genel-tebligi-sira-no-596 ; imer.av.tr/rehber/gib-elektronik-tebligat-zorunlulugu-teblig-596 ; cottgroup.com (e-tebligat yeni dönem) |
| 2026 hadleri | — | Fatura düzenleme sınırı ve doğrudan gider yazılabilecek demirbaş sınırı (VUK 313) **12.000 TL** (VUK GT 588). | yontemymm.com.tr/pratik-bilgiler/vuk-2026-yili-hadleri-ve-tutarlari |

---

## 1. e-Dönüşüm ailesi

| Yükümlülük | Dayanak | Bize? | ERP'de karşılama | Ceza / risk | Kesinlik | Kaynak |
|---|---|---|---|---|---|---|
| e-Fatura **ihracat senaryosu** | 509 Sıra No.lu VUK GT | **E**: e-Fatura mükellefi olup mal ihraç eden düzenler, GÇB ile ilişkilendirilir | Satış faturası tipi = İHRACAT; teslim şekli (Incoterms), GTİP, kap/ağırlık; GÇB no ve tarihi geri bağlanır | Usulsüzlük cezası, KDV istisnasının reddi | Kesin | muhasebenews.com/?p=73834 ; alomaliye.com/2017/06/23/ihracatta-e-fatura/ |
| e-Müstahsil makbuzu | 509 | **H**: çiftçiden ürün alımı yok | — | — | Kesin | sovos.com (e-SMM/e-Müstahsil yazıları) |
| e-SMM (bize kesilen) | 509 (düzenleyen serbest meslek erbabı) | **E**, alıcı tarafında: avukat, mali müşavir, danışman | Gelen e-SMM'nin GİB'den doğrulanması; gider kaydı; GVK 94 stopajı muhtasara | Stopajın eksik beyanı | Kesin (alıcı tarafındaki ayrıntılar **[BELİRSİZ]**) | parasut.com/blog/e-smm-zorunlulugu |
| e-Gider pusulası | 509 IV.6 | **B**: genel olarak isteğe bağlı; GİB risk analiziyle en az 3 ay önceden zorunlu tutabilir. Vergi mükellefi olmayan kişiden hurda alımında gider pusulası ve stopaj gerekir | Hurda alımında gider pusulası; stopaj oranı parametre (bir kaynak hurda için %2 diyor) | Stopaj sorumluluğu | **[BELİRSİZ]**, oran GVK 94'ten teyit edilmeli | sovos.com/tr/blog/kdv/e-gider-pusulasi-zorunlu-mudur… ; muhasebenews.com/?p=143512 |
| e-Adisyon, e-Bilet | 509 | **H** | — | — | Kesin | — |
| e-Döviz ve Kıymetli Maden Alım-Satım Belgesi | 509 IV.10 (526 ile değişik) | **H**: yalnız yetkili müesseseler düzenler. Bankalardan gelen döviz alım belgeleri (DAB) ihracat dosyasına eklenir | Banka DAB'larını ihracat faturasına eşleme | — | Kesin | sovos.com/tr/blog/kdv/e-doviz-alim-satim-belgesi… |
| e-Mutabakat | Yasal dayanak yok; Ba-Bs kaldırıldıktan sonra fiilî uygulama | **E (fiilî)** | Cari mutabakat mektubu (e-posta/KEP), yanıt takibi, fark listesi | Denetimde ispat zayıflığı | Kesin (zorunlu değil) | — |
| e-Arşiv iptal / e-Fatura ret | GİB iptal-itiraz kılavuzu; TTK 21/2 | **E** | Belge durum makinesi: e-Arşiv iptali **8 gün** (alıcıya iletilme tarihinden). Ticari senaryoda ret de 8 gün. Süre geçince iade faturası düzenlenir. Sayaç ve uyarı | Hatalı belge düzeltilemez | Kesin (8 gün); başlangıç günü yorumu **[BELİRSİZ]** | parasut.com/blog/e-arsiv-fatura-nasil-iptal-edilir ; alomaliye.com/2025/01/06/e-arsiv-…kilavuzlarinda-degisiklik/ |
| GİB portal ile özel entegratör farkı | 509 | **E** | ERP entegrasyonu fiilen özel entegratör ister; portalde API yok **[BELİRSİZ]**. OPEN-016 | — | — | — |
| Mali mühür / e-imza | 509; 5070 | **E** | e-Defter Mikro'da; e-Fatura için entegratör mührü mü kendi mührümüz mü, kararı gerekir | Mühür süresinin dolması | Kesin | — |
| e-Tebligat (VUK) ve UETS | VUK 107/A, VUK GT 596; Tebligat Kanunu 7/a (A.Ş. ve Ltd. için elektronik tebligat) | **E** (her 3 şirket) | ERP dışı. Uyum takviminde günlük kontrol görevi; tebligat geldiğinde süre başlangıcı = 5. gün sonu | Tebligat kaçırılırsa itiraz süresi kaybı | Kesin (596'nın ayrıntıları **[BELİRSİZ]**) | hukukihaber.net (596) |
| KEP | TTK 18/3 (ticari ihbarlar noter, taahhütlü mektup, telgraf veya KEP ile) | **E (fiilî)** | Temerrüt ihtarı, fesih ve ayıp ihbarı KEP ile; gönderim kaydı | İspat kaybı | Kesin (genel kural) | — |

## 2. İhracat, ithalat ve gümrük

| Yükümlülük | Dayanak | Bize? | ERP'de karşılama | Ceza / risk | Kesinlik | Kaynak |
|---|---|---|---|---|---|---|
| Gümrük beyannamesi (BİLGE / e-Beyanname) | 4458 Gümrük K. | **E** (ihracat/ithalat varsa) | Beyannameyi gümrük müşaviri verir. ERP GÇB/İGB no, tarih, GTİP, FOB/CIF, fiilî ihraç tarihi ve kapanış bilgisini tutar | KDV istisnası ve kur farkı hataları | Kesin | — |
| İhracat KDV istisnası iadesi (11/1-a) | 3065 KDVK; KDV GUT | **E** | İade listeleri: GÇB listesi, satış faturaları listesi, yüklenilen KDV listesi; e-iade dosyası | İade gecikmesi | Kesin | — |
| İhraç kaydıyla teslim (11/1-c) | KDVK 11/1-c, Geçici 17 | **B**: alış tarafında E (üreticiden ihraç kaydıyla alıp ihraç edersek). Satış tarafında yalnız "imalatçı" satabilir; dilme/kesme/boya imalat sayılır mı **[BELİRSİZ]** | Fatura satırı bazında **3 ay** sayacı (teslimi izleyen aybaşından), GÇB eşleme, süre aşımında ihracatçı adına KDV ve gecikme zammı uyarısı | Tecil edilen KDV + gecikme zammı | Kesin (kural); bizim için **[BELİRSİZ]** | muhasebetr.com/yazarlarimiz/erenkaraogullari/009 ; parasut.com/blog/ihrac-kayitli-fatura |
| Dahilde İşleme Rejimi / DİİB | Ticaret Bakanlığı DİR mevzuatı; KDVK Geçici 17 | **B**: ithal bobinle ihracat yapılıyorsa | Belge bazında girdi/çıktı, randıman (fire), ihracat taahhüdü ve süre, belge kapatma dosyası. Ayrı modül büyüklüğünde iş | Teminatın irat kaydı, vergiler | **[BELİRSİZ]** | dunya.com "Dahilde işleme rejimi uygulamasında değişen ne?" |
| İhracat bedelinin yurda getirilmesi ve TCMB'ye satış | 32 sayılı Karar; İhracat Genelgesi | **E** | İhracat bazında tahsilat; getirme süresi (180 gün olarak biliniyor **[BELİRSİZ]**); DAB/IBKB; **%35'in TCMB'ye satışı 31.01.2027'ye kadar uzatıldı** | Kambiyo cezası | %35 ve tarih ikincil kaynakta; süre **[BELİRSİZ]** | alomaliye.com/2026/05/07/ihracat-bedellerinin-doviz-alim-belgesine… ; cnnturk.com (2027'ye uzatıldı) ; tcmb.gov.tr UT 11.11.2026 |
| Döviz dönüşüm desteği | TCMB tebliği ve uygulama talimatı (1.10.2026'dan yeni) | **E (fırsat)** | DAB ile destek tahakkuku | Fırsat kaybı | İkincil kaynak | bloomberght.com/tcmb-den-doviz-donusum-desteginde-yeni-duzenleme-3784465 |
| Kur farkı faturası | VUK 230/231; KDV GUT | **E** | Lehine fark oluşan taraf fatura düzenler. KDV oranı **asıl teslim tarihindeki** orandır. KDV dahil fiyatta iç yüzde. İhracatta KDV hesaplanıp hesaplanmayacağı konusunda kaynaklar çelişiyor **[BELİRSİZ]** | KDV farkı | Kural kesin; ihracat kısmı **[BELİRSİZ]** | muhasebenews.com/?p=171546 |

## 3. Vergi

| Yükümlülük | Dayanak | Bize? | ERP'de karşılama | Ceza / risk | Kesinlik | Kaynak |
|---|---|---|---|---|---|---|
| Tevkifat KDV iadesi | KDV GUT | **E**: satışlarda sürekli 5/10 tevkifat ve devreden KDV | Tevkifatlı satış listesi, alış listesi, iade listesi; mahsup ya da nakit seçimi. Kaynaklara göre **50.000 TL'ye kadar nakit iade raporsuz ve teminatsız** (KDV GUT Seri 57), üstü için YMM raporu ve süresinde tam tasdik sözleşmesi ya da teminat. Mahsuben iadede rapor aranmıyor | Nakit sıkışması | Eşik ikincil kaynakta **[BELİRSİZ]** | kobitime.com/?p=4732 ; muhasebetr.com/yazarlarimiz/aydinsahin/028/ |
| Geçici vergi ve kurumlar vergisi | KVK; GVK; 7524 (KVK 32/C) | **E** | Muhasebe Mikro'da. ERP yalnız gelir tahakkukunu (sevk/fatura eşlemesi) ve stok değerini zamanında üretir. 2026: genel oran **%25**, ihracat kazancına **5 puan** indirim, **yurt içi asgari KV %10**. Üretim indirimiyle ihracat indirimi aynı kazanca birlikte uygulanmaz (KV GT 26) | Eksik beyan | Kesin (oranlar); geçici vergi dönem sayısı **[BELİRSİZ]**, aramada doğrulanamadı | alomaliye.com/2026/07/04/kurumlar-vergisi-genel-tebligi-seri-no-26/ ; pwc.com.tr KV beyannamesi 2025 |
| **Damga vergisi**: sözleşme, imzalı sipariş formu, teyit | 488 DVK; 1 sayılı tablo I/A-1; 71 Seri DV GT | **E** | Oran **binde 9,48**, 2026 tavanı **29.115.961,10 TL**. **Karşı tarafın imzaladığı (veya e-imzayla teyit ettiği) sipariş formu sözleşme sayılabilir.** Teyitsiz ve sözleşmeye bağlanmamış sipariş emirleri vergi dışı. ERP: sipariş onay şablonu (imza alanı politikası), damga vergisi hesaplayıcı, sözleşme sicili, muhtasar beyana aktarım. İhracatla ilgili kâğıtlarda istisna olabilir **[BELİRSİZ]** | Vergi + ceza | Oran ve tavan kesin; sipariş formu yorumu **[BELİRSİZ]** | pwc.com.tr damga vergisi oranları ; muhasebetr.com/yazarlarimiz/vedaterdem/0327/ ; gazetebirlik.com "Sipariş formunda damga vergisi var mı" |
| ÖTV | 4760 | **H** (araç alımı hariç) | — | — | Kesin | — |
| Stopaj | GVK 94; KVK 30 | **E** | Kira, serbest meslek, hurda gider pusulası. **Yurt dışı yazılım, bulut ve AI lisans ödemelerinde KVK 30 stopajı ve sorumlu sıfatıyla KDV (2 No.lu beyan)**; çifte vergilendirme anlaşmasına göre oran **[BELİRSİZ]** | Sorumlu sıfatıyla tarhiyat | Kesin (genel kural) | — |
| **Transfer fiyatlandırması**: grup içi 3 şirket | KVK 13; 1 Seri No.lu TF GT | **E**: ortaklık/yönetim üzerinden ilişkili kişiler | Grup içi satış, hizmet ve finansman dökümü; emsal fiyat karşılaştırması (aynı ürün, aynı dönemdeki üçüncü kişi fiyatı); KV beyannamesi ekindeki "TF, KYK ve Örtülü Sermaye Formu" verisi; yıllık rapor gerekiyorsa hazırlık. 2026'da TF denetimleri artırıldı | Örtülü kazanç: KV ve kâr payı stopajı tarhiyatı | Kural kesin; yurt içi ilişkili işlemler için yıllık rapor yükümlülüğü **[BELİRSİZ]** | pwc.com.tr "5 soruda TF belgelendirme" ; kpmg.com (Türkiye TF denetimleri, 2026/02) |
| Örtülü sermaye | KVK 12 | **B**: grup içi borçlanma varsa | İlişkili kişiden borç / özsermaye ×3 izleme; faiz ve kur farkının KKEG'e atılması | KKEG ve kâr payı | Kesin (kural) | muhasebenews.com/?p=55228 |
| Ortaktan ve gruptan alacakta adat | KVK 13 / GVK 41 | **E (risk)** | Cari bazında günlük bakiye ile adat; emsal faiz parametresi; faturalama | Örtülü kazanç | Kesin (kural); 2026 emsal oranı **[BELİRSİZ]** | — |
| Amortisman ve sabit kıymet | VUK 313–321; 333 sayılı tebliğ | **E** (Mikro'da) | ERP'de varlık kartı (makine, hat); doğrudan gider sınırı 12.000 TL | — | Kesin | yontemymm.com.tr VUK 2026 hadleri |
| Nakit tahsilat/ödeme sınırı | VUK mük. 257 (banka/PTT zorunluluğu) | **E** (hurda ve küçük alıcılarda risk) | Nakit tahsilat/ödeme sınırı kontrolü (tutar parametre **[BELİRSİZ]**) | Özel usulsüzlük | Kural kesin; tutar **[BELİRSİZ]** | — |

## 4. SGK ve çalışan (bordro dışarıda)

| Yükümlülük | Dayanak | Bize? | ERP'de karşılama | Ceza / risk | Kesinlik | Kaynak |
|---|---|---|---|---|---|---|
| İşe giriş en az 1 gün önce, çıkış 10 gün içinde | 5510 m.8; e-Sigorta | **E** | Personel kartı (TCKN, özel nitelikli veriler KVKK'ya göre); işe başlama tarihi bildirim tarihinden önce olamaz kontrolü; işten çıkış kodu | 2026'da sigortalı başına aylık brüt asgari ücret (33.030 TL, ikincil kaynak) | Kesin | muhasebenews.com/sgk-ise-giris-bildirgesi… ; alomaliye 2026 5510 ceza tablosu |
| MUHSGK / e-Bildirge | 5510; GVK | **E** (bordrocu yapar) | Masraf, avans ve harcırah aktarımı; yurt içi harcırah istisnası (1.7–31.12.2026 için 850 TL/gün, ikincil kaynak) | — | Tutar **[BELİRSİZ]** | alomaliye 01.07–31.12.2026 harcırah |
| İSG: risk değerlendirmesi, İSG-KATİP, eğitim, KKD | 6331 | **E**. Metal işleme tehlike sınıfı NACE koduna göre "tehlikeli" ya da "çok tehlikeli" **[BELİRSİZ]**. Çok tehlikelide yılda en az 16 saat eğitim | Eğitim kayıtları, KKD zimmeti, makine periyodik kontrolleri (vinç, forklift, kaldırma ekipmanı), ramak kala kayıtları | İdari para cezası, cezai sorumluluk | Kural kesin; sınıf **[BELİRSİZ]** | csgb.gov.tr demir-çelik İSG rehberi |
| İş kazası bildirimi | 5510 m.13; 6331 m.14 | **E** | Kolluğa derhal, **SGK'ya 3 iş günü** içinde; ERP'de olay kaydı ve sayaç | İdari para cezası ve SGK rücu | Kesin; ilk günün sayılıp sayılmadığı **[BELİRSİZ]** | alomaliye.com/2026/01/08/is-kazasi-idari-para-cezasi… |

## 5. Çevre, iklim ve enerji

| Yükümlülük | Dayanak | Bize? | ERP'de karşılama | Ceza / risk | Kesinlik | Kaynak |
|---|---|---|---|---|---|---|
| **AB SKDM / CBAM** | Tüzük (AB) 2023/956; 2025/2083 (Omnibus); UR 2025/2620 | **E** (AB'ye 72/73 ihracatı varsa). Beyan yükümlüsü AB'li yetkili SKDM beyancısı, biz veri sağlarız. Kesin dönem **1.1.2026**'da başladı. Yıllık 50 ton altındaki ithalatçı muaf. Sertifika satışı **1.2.2027**'de başlıyor. 2026 ithalatının ilk beyanı ve sertifika teslimi **30.09.2027**. Doğrulayıcı tesis ziyaretleri 2027'den itibaren. Doğrulanmış veri yoksa **varsayılan değer** uygulanır; maliyet 2–3 kat artabilir | Sevkiyat bazında: CN kodu, net ton, **menşe tesis (haddehane) ve döküm no**, haddehanenin gömülü emisyon verisi (doğrudan/dolaylı, SEE). Boya ve dilme hattının kendi enerji tüketimi (doğalgaz, elektrik), fire ve randıman. AB şablonuna aktarım. **Kesim firesi ton başına emisyonu yükseltir** | Ticari kayıp: müşteri kaybı, fiyat indirimi | Kesin (AB tarafı). **Boyamanın bizi "işletmeci" yapıp yapmadığı [BELİRSİZ]** | trade.ec.europa.eu "Start of the definitive period" ; mayerbrown.com (2025/2083, 10 değişiklik) ; eur-lex 2025/2620 ; eurometal.net (çelik sektör rehberi) |
| Türkiye ETS / İklim Kanunu | 7552 (RG 9.7.2025); TR ETS Yönetmeliği (RG 27.08.2026, 33353) | **H** (doğrudan): pilot 2026–2027, yıllık 50 bin t CO2 üzeri tesisler. Dolaylı etki tedarikçi haddehane üzerinden | Haddehanenin TR ETS verisi CBAM paketine girer | — | Kesin | icapcarbonaction.com (TR ETS düzenlemesi) ; ember-energy.org TR ETS raporu |
| Atık yönetimi: atık üreticisi, beyan, tehlikeli atık | Atık Yönetimi Yönetmeliği; Atıkların Karayolunda Taşınmasına İlişkin Tebliğ | **E**: boya, tiner, çözücü atığı, atık yağ, kontamine ambalaj, üstüpü (tehlikeli) | Atık kodu bazında üretim ve stok; geçici depolama süresi; lisanslı alıcıya teslim; **MoTAT** (tehlikeli atık taşıma talebi, TKN/TDN); yıllık atık beyanı verisi (son tarih **[BELİRSİZ]**) | İdari para cezası (2872) | Kural kesin; süreler **[BELİRSİZ]** | webdosya.csb.gov.tr (MoTAT duyuruları) |
| Metal hurda (kendi üretim firesi) | KDVK 17/4-g; Atık Yönetimi Yönetmeliği | **E** | Hurda satışında **alıcının çevre lisansı numarası** kontrolü; KDV istisnası mı, vazgeçme ve 7/10 tevkifat mı seçimi; hurda ticareti yapılırsa ayrıca lisans **[BELİRSİZ]** | Lisanssız alıcıya teslimde sorumluluk | **[BELİRSİZ]** | — |
| Sıfır Atık belgesi | Sıfır Atık Yönetmeliği (RG 12.07.2019) | **E** (EK-1 takvimine göre) | Atık ayrıştırma kayıtları; belge geçerlilik takibi (SABS/UÇBS) | İdari para cezası | Takvim **[BELİRSİZ]** | webdosya.csb.gov.tr Sıfır Atık Belgesi Yardım Kılavuzu (08/2026) |
| **GEKAP** | 2872 Çevre K. Ek-1; GEKAP Tutarları Tebliği 2026/1 (RG 30.12.2025, 33123) | **B**: yurt içi sevkte palet, streç ve çember "piyasaya sürülen ambalaj" sayılabilir. **İhracat ambalajı beyan dışı** görülüyor. Görüşler çelişkili | Sevkiyat bazında ambalaj malzemesi (kg/adet, malzeme türü), yurt içi/ihracat ayrımı, GEKAP beyan raporu | GEKAP + ceza | **[BELİRSİZ]** | alomaliye.com 2026/1 tebliğ ; muhasebenews.com (ithal paslanmaz sac paletleri GEKAP) |
| Endüstriyel emisyon / çevre izni (boya hattı) | Endüstriyel Emisyonların Yönetimi Yönetmeliği (RG 14.01.2025, 32782); Çevre İzin ve Lisans Yönetmeliği | **B**: boya hattının solvent tüketimine bağlı | Boya ve solvent tüketim kaydı (kg/yıl) | Faaliyet durdurma | **[BELİRSİZ]** | webdosya.csb.gov.tr EEY Yönetmeliği |
| Enerji yöneticisi | 5627 Enerji Verimliliği K. | **B**: yıllık enerji tüketimi eşiğine bağlı (eşik **[BELİRSİZ]**) | Hat bazında enerji ölçümü; CBAM verisiyle aynı kaynak | İdari para cezası | **[BELİRSİZ]** | — |

## 6. Ticaret hukuku ve finans

| Yükümlülük | Dayanak | Bize? | ERP'de karşılama | Ceza / risk | Kesinlik | Kaynak |
|---|---|---|---|---|---|---|
| Belgelerde ticaret unvanı, merkez, sicil no, MERSİS no, web adresi | TTK 39/2 | **E** | Şirket ana verisi (3 şirket); tüm çıktı şablonlarında zorunlu alanlar | Usulsüzlük | Kesin | — |
| TTK defterleri (yevmiye, kebir, envanter, pay defteri, YK karar defteri, genel kurul defteri) | TTK 64–65; e-Defter | **E** (Mikro / hukuk) | ERP dışı | — | Kesin | — |
| Bağımsız denetim ve web sitesi | TTK 397; TTK 1524; **11066 sayılı CBK** (RG 17.03.2026) | **B**: eşikler aktif 500 milyon TL, net satış 1 milyar TL, 150 çalışan. **Üçünden ikisi art arda iki dönem** aşılırsa denetime tabi. **Bağlı ortaklıklar dahil edilir**, bu da 3 şirketli grup için önemli | Grup konsolide aktif/ciro raporu | Kararların hükümsüzlük riski | Eşikler kaynakta açık; II sayılı liste **[BELİRSİZ]** | bloomberght.com (aktif ve ciro kriterleri güncellendi) ; ey.com TTK bağımsız denetim 2026 |
| Fatura zorunlu alanları, 7 gün kuralı | VUK 230, 231/5 | **E** | Teslimden itibaren 7 gün sayacı; irsaliyeli fatura seçeneği | Fatura verilmemiş sayılır, özel usulsüzlük | Kesin | — |
| e-İrsaliye (GTİP 72/73) | 509 | **E** (ciro sınırı yok) | Sevkten önce düzenleme; yanıt (kabul/ret) takibi; yanıt süresi **[BELİRSİZ]** | Özel usulsüzlük | Kesin (kapsam) | birfatura.com/kimlere-e-irsaliye… |
| Çek: karekodlu çek, karşılıksız çek | 5941 Çek K. (son değişiklik 19.12.2025, içeriği doğrulanamadı) | **E** | Çek portföyü: karekod verisi, keşideci VKN, ciro zinciri, vade, Findeks çek raporu bağlantısı, karşılıksız işlemi. Çek yaprağı başına banka sorumluluğu 16.350 TL (ikincil kaynak) | Tahsil edilemeyen alacak | Kural kesin; 2025 değişikliği **[BELİRSİZ]** | bloomberght.com (karşılıksız çekler 270 milyar TL) |
| Ödeme süresi ve temerrüt faizi | TTK 1530; 3095 m.2 | **E** | Ödeme süresi en fazla 60 gün (açık anlaşma ve ağır haksızlık yoksa uzatılabilir; **KOBİ alacaklıda 60 gün kesin**). Süre kararlaştırılmamışsa faturadan 30 gün sonra **ihtarsız** temerrüt. Oranlar: TCMB'nin TTK 1530 oranı ve avans faizi (20.12.2025 itibarıyla **%39,75**; 2026'daki değişiklikler **[BELİRSİZ]**). Vade aşımı faizi hesaplayıcı; KOBİ bayrağı | Alacak kaybı | Kural kesin; oran **[BELİRSİZ]** | tcmb.gov.tr TTK Md. 1530 ; tr.andersen.com and_06-2026-temerrut-faizi ; tcmb.gov.tr Reeskont ve Avans |
| TCMB döviz kredisi ve raporlama | 32 sayılı Karar; TCMB SRVT | **B**: döviz kredisi veya bakiye eşiklerine bağlı (15 milyon USD; raporlamada 100 milyon TL'ye inmiş olabilir **[BELİRSİZ]**) | Döviz pozisyonu raporu (vade, kur) | Kambiyo cezası | **[BELİRSİZ]** | ekonomigazetesi.com (döviz pozisyonu bildirimi) ; muhasebenews.com/?p=163154 |
| MASAK | 5549 | **H** (yükümlü grup değiliz; liste kıymetli maden ve taş). Hurda ve nakit kaynaklı risk genel | Nakit sınırı, KYC (yeni cari için VKN doğrulama) | — | Büyük olasılıkla H, **[BELİRSİZ]** | ms.hmb.gov.tr (finansal kuruluş olmayan yükümlüler rehberi) |
| POS / kredi kartı | — | **B** | Banka mutabakatı | — | — | — |

## 7. Sektör ve ürün

| Yükümlülük | Dayanak | Bize? | ERP'de karşılama | Ceza / risk | Kesinlik | Kaynak |
|---|---|---|---|---|---|---|
| **İDİS** (inşaat demiri izleme) | İDİS Uygulama Genel Tebliği (RG 16.03.2023, 32134), VUK mük. 257 | **B**: inşaat demiri (7214.20) ticareti varsa **E**. Toptancı, bayi ve tüccar da kapsamda, 1.1.2024'ten beri | Bağ etiketi kodu (bundle ID), İDİS transfer bildirimi, e-İrsaliye ile bağlantı | Usulsüzlük, satış engeli | Kural kesin; 2026'daki değişiklikler doğrulanamadı | portal.idis.gov.tr/docs/notifications/16_03_2023_idis_uygulama_genel_tebligi.pdf |
| CE, Yapı Malzemeleri, ürün güvenliği | 7223 sayılı Kanun; Yapı Malzemeleri Yönetmeliği | **B**: yapı çeliğinde (EN 10025) CE'yi haddehane verir. Biz dağıtıcıyız; işleme (EN 1090 imalat) yaparsak üretici sayılabiliriz | Performans beyanı ve CE belgesinin lota bağlanması | Piyasa gözetimi yaptırımı | **[BELİRSİZ]** | — |
| EN 10204 3.1 sertifika ve izlenebilirlik | Sözleşme ve müşteri şartı (fiilî) | **E (fiilî)** | Döküm no, ana bobin ile çocuk lot ilişkisi, sertifika PDF'i, kesim sonrası sertifika aktarımı | Müşteri reddi | Kesin (fiilî) | — |
| Haksız fiyat | 6585 sayılı Kanun (Haksız Fiyat Değerlendirme Kurulu) | **B**, düşük | Fiyat geçmişinin saklanması | — | **[BELİRSİZ]** | — |

## 8. Veri ve KVKK

| Yükümlülük | Dayanak | Bize? | ERP'de karşılama | Ceza / risk | Kesinlik | Kaynak |
|---|---|---|---|---|---|---|
| **Yurt dışı aktarım** (bulut, AI sağlayıcısı) | KVKK 9 (7499 sayılı Kanun ile değişik); Kurul kararı 2024/959 (standart sözleşmeler) | **E** | Sağlayıcı envanteri; değiştirilmeden kullanılan standart sözleşme; **imzadan sonra 5 iş günü içinde Kurum'a bildirim** (modül/KEP). Açık rızaya dayalı eski aktarımlar 1.9.2024'e kadar geçerliydi. Hangi verinin AI'ya gittiğine dair kayıt; kişisel veri maskeleme | Bildirim yapmama cezası 2026'da 90.308–1.806.377 TL (ikincil kaynak) | Kesin (kural); tutar **[BELİRSİZ]** | kvkk.gov.tr/Icerik/8170/… ; kvkk.gov.tr/Icerik/2053/Yurtdisina-Aktarim |
| Veri ihlali bildirimi | KVKK 12/5; Kurul kararı 2019/10 | **E** | Kurul'a **72 saat** içinde; olay kaydı, etkilenen kişi listesi | Ceza (örnek kararda 1.000.000 TL) | Kesin | kvkk.gov.tr/Icerik/5362/Veri-Ihlali-Bildirimi |
| Aydınlatma, saklama-imha, VERBİS | KVKK 10, 7, 16 | **E** (§5'te var) | Saklama süresi matrisi (VUK 5, TTK 10, SGK…); imha işi; hukuki saklama (legal hold) | — | Kesin | — |
| 5651 log saklama | 5651; İnternet Toplu Kullanım Sağlayıcıları Yönetmeliği | **B**: misafir Wi-Fi varsa E; yalnız çalışan interneti için yorum farklı | ERP dışı (firewall); **2 yıl** | Ceza | **[BELİRSİZ]** | turkcell.com.tr 5651 loglama |

---

## (1) Kesin ve yüksek riskli ilk 10 madde

1. **KDV tevkifatı 5/10 (kod 627), alış ve satışta, tüccar zinciri dahil, ve tevkifat iadesi.** Her satış faturasını etkiler. Yanlış kod veya oran doğrudan tarhiyat ve iade kaybı demektir.
2. **e-Fatura / e-Arşiv / e-İrsaliye (GTİP 72/73): zamanında ve doğru düzenleme, 8 günlük iptal/ret penceresi.** Sevkten önce irsaliye, teslimden itibaren 7 gün içinde fatura.
3. **İhracat zinciri:** e-Fatura ihracat senaryosu, GÇB bağlantısı, KDV istisnası iadesi, ihracat bedelinin getirilmesi ve %35'in TCMB'ye satışı (31.01.2027'ye kadar).
4. **Grup içi transfer fiyatlandırması** (KVK 13), adat ve örtülü sermaye. 3 şirket arasında her işlem risk taşıyor; denetimler 2026'da artırıldı.
5. **CBAM veri sağlama.** Yasal yükümlü AB'li alıcı, ama veri gelmezse varsayılan değer uygulanır ve maliyet 2–3 kat artar. İlk beyan 30.09.2027 olduğu için 2026 verisi şimdiden toplanmalı.
6. **Damga vergisi:** imzalı sipariş formları ve sözleşmeler, binde 9,48. Fark edilmeyen yükümlülük olarak kalıyor.
7. **KVKK yurt dışı aktarım:** bulut ve AI sağlayıcısı için standart sözleşme, 5 iş günü içinde bildirim; ihlalde 72 saat.
8. **Belge saklama 10 yıl** (TTK 82). Görevdeki "VUK 10 yıl" ifadesi yanlış, VUK 5 yıl. İmha işi hukuki saklama durumunda durmalı.
9. **İSG ve iş kazası 3 iş günü bildirimi.** Metal işlemede ağır kaza riski, cezai sorumluluk.
10. **Tehlikeli atık:** boya, tiner, yağ, kontamine ambalaj, MoTAT ve atık beyanı. Boya hattı ile doğrudan bağlantılı.

(Bunlara yakın gelen iki madde: ihracat kaydıyla alımda 3 aylık süre, kullanılıyorsa; TTK 1530 kapsamında KOBİ tedarikçiye 60 gün sınırı.)

## (2) ERP'ye en çok iş yükü kazandıracak 5 madde

1. **Otomatik KDV iade dosyası** (tevkifat ve ihracat): indirilecek KDV, satış, GÇB ve yüklenilen KDV listeleri ERP'den çıkar. Her ay elle tablo hazırlama işi kalkar.
2. **Tek ihracat dosyası:** sipariş, ihracat e-faturası, GÇB, DAB/IBKB, kur farkı faturası ve iade bir kayıtta. İhraç kayıtlı ve DİİB süre sayaçları da bu dosyada.
3. **Lot/döküm izlenebilirliği ile sertifika, İDİS ve e-İrsaliye otomatiği.** Aynı veri CBAM menşe tesisini ve EN 10204 aktarımını besler.
4. **CBAM veri paketi:** sevkiyat bazında haddehane emisyonu, kendi hat enerjisi ve fire. Müşteriye şablon olarak gönderilir.
5. **Grup içi işlemler ve uyum takvimi:** ilişkili kişi işlem dökümü, emsal fiyat karşılaştırması, adat, otomatik cari mutabakat. e-Tebligat, beyan ve iade sayaçları için ortak takvim.

## (3) Mali müşavire ve YMM'ye sorulacak sorular

1. 589 sayılı tebliğden sonra bilanço esasındaki 3 şirketimiz için e-Arşiv tutarsız mı (2026)? Geçiş tarihleri nedir?
2. Dilme, boy kesme ve boya "imalat" sayılır mı (KDV 11/1-c satışı, e-İrsaliye'de "imal", sanayi sicili)?
3. Satışlarda 627 kodlu 5/10 tevkifatı tüccar olarak hangi alıcılara uyguluyoruz? Hurda satışında istisna mı seçiyoruz, vazgeçip 7/10 mu? Hurda kodu hangisi?
4. Tevkifat iadesinde nakit mi mahsup mu tercih ediyoruz? 2026 sınırı (50.000 TL) ve tam tasdik sözleşmemiz var mı?
5. İhraç kaydıyla alım yapıyor muyuz? DİİB kullanıyor muyuz veya kullanacak mıyız?
6. İhracat kur farkı faturasında KDV uygulaması (kaynaklar çelişiyor).
7. Karşı tarafın imzaladığı sipariş formlarımız ve teyitlerimiz damga vergisine tabi mi? İhracat istisnası uygulanıyor mu?
8. 3 şirket arasında hangi işlemler için TF formu veya yıllık rapor gerekiyor? Adat ve emsal faiz oranı ne olacak? Örtülü sermaye durumu nedir?
9. Yurt dışı yazılım, bulut ve AI ödemelerinde KVK 30 stopajı ve sorumlu sıfatıyla KDV; hangi çifte vergilendirme anlaşması oranı?
10. Grup, 11066 sayılı CBK eşiklerine göre (bağlı ortaklıklar dahil) bağımsız denetime giriyor mu?
11. e-Tebligat (VUK 596) için 3 şirkette hesap ve iletişim bilgileri güncel mi? UETS adresleri var mı?
12. İhracat bedeli getirme süresi ve %35 satış yükümlülüğü; DAB'lar kimde, nasıl takip ediliyor?
13. GEKAP: yurt içi sevkteki palet, çember ve streç için beyan veriyor muyuz?
14. Vergi mükellefi olmayan kişilerden hurda alıyor muyuz (gider pusulası ve stopaj)? Nakit ödeme sınırının 2026 tutarı nedir?
15. NACE kodlarımız ve İSG tehlike sınıfımız nedir? Boya hattı çevre izni veya emisyon iznine tabi mi?
16. İnşaat demiri satıyor muyuz (İDİS)?
17. CBAM: AB müşterilerimiz veri istedi mi? Haddehanelerden doğrulanmış emisyon verisi alabiliyor muyuz? Boyama bizi "işletmeci" yapar mı?
18. TCMB döviz pozisyonu raporlaması ve döviz kredisi eşiklerine giriyor muyuz?

---

## Kaynaklar (seçili)

- [Avrupa Komisyonu, CBAM kesin dönemin başlaması](https://trade.ec.europa.eu/access-to-markets/en/news/start-definitive-period-cbam-eu)
- [Mayer Brown, CBAM Omnibus (2025/2083)](https://www.mayerbrown.com/en/insights/publications/2025/10/eu-adopts-cbam-simplification-regulation-10-key-amendments-and-challenges-ahead)
- [EUR-Lex, Uygulama Tüzüğü 2025/2620](https://eur-lex.europa.eu/eli/reg_impl/2025/2620/oj/eng/pdf)
- [Eurometal, CBAM demir-çelik sektör rehberi](https://eurometal.net/ec-publishes-sector-specific-cbam-guidance-for-iron-and-steel-to-help-non-eu-operators/)
- [ICAP, Türkiye ETS düzenlemesi](https://icapcarbonaction.com/en/news/turkiye-unveils-regulation-its-national-emissions-trading-system)
- [Ember, TR ETS raporu](https://ember-energy.org/app/uploads/2026/08/Turkiye-Emisyon-Ticaret-Sistemi-ne-getiriyor.pdf)
- [KPMG Vergi, Ba-Bs'nin kaldırılması](https://kpmgvergi.com/yayinlar/mali-bultenler/vergi/form-ba-ve-form-bs-uygulamasina-son-verilmesine-iliskin-vuk-tebligi-yayimlandi/2746)
- [Alomaliye, 589 sayılı tebliğ](https://www.alomaliye.com/2025/12/31/vergi-usul-kanunu-genel-tebligi-sira-no-589/)
- [Hukuki Haber, VUK GT 596](https://www.hukukihaber.net/vergi-usul-kanunu-genel-tebligi-sira-no-596)
- [PwC, damga vergisi oranları](https://www.pwc.com.tr/tr/hizmetlerimiz/vergi/vergi-portali/pratik-bilgiler/oranlar/damga-vergisi-oran-ve-tutarlari.html)
- [Muhasebetr, sipariş formunda damga vergisi](https://www.muhasebetr.com/yazarlarimiz/vedaterdem/0327/)
- [Muhasebenews, demir alan firmanın satışta tevkifatı](https://www.muhasebenews.com/5-10-kdv-tevkifatli-demir-alan-firma-bunu-satarken-de-ayni-oranda-kdv-tevkifati-uygulamalidir/)
- [Muhasebenews, hurdada KDV tevkifatı](https://www.muhasebenews.com/hurdalarda-kdv-tevkifati-uygulamasi/)
- [Kobitime, tevkifat iadesi 50.000 TL sınırı](https://kobitime.com/?p=4732)
- [PwC, transfer fiyatlandırması belgelendirme](https://www.pwc.com.tr/tr/hizmetlerimiz/vergi/transfer-fiyatlandirmasi/yayinlar/5-soruda-transfer-fiyatlandirmasi-belgelendirme-yukumlulukleri.html)
- [KPMG, Türkiye TF denetimleri](https://kpmg.com/us/en/taxnewsflash/news/2026/02/turkiye-increased-transfer-pricing-audits.html)
- [TCMB, TTK 1530 oranı](https://www.tcmb.gov.tr/wps/wcm/connect/TR/TCMB%2BTR/Main%2BMenu/Istatistikler/Piyasa%2BVerileri/TTK%2BMd.%2B1530)
- [TCMB, reeskont ve avans faiz oranları](https://tcmb.gov.tr/wps/wcm/connect/TR/TCMB+TR/Main+Menu/Temel+Faaliyetler/Para+Politikasi/Reeskont+ve+Avans+Faiz+Oranlari)
- [Alomaliye, ihracat bedellerinin DAB'a bağlanma oranı](https://www.alomaliye.com/2026/05/07/ihracat-bedellerinin-doviz-alim-belgesine-dab-baglanma-orani-yuzde-35-olarak-devam-ediyor/)
- [CNN Türk, TCMB döviz kararı 2027'ye uzatıldı](https://www.cnnturk.com/ekonomi/tcmbden-doviz-icin-yeni-karar-sure-2027ye-kadar-uzatildi-3448992)
- [KVKK, yurt dışına aktarım](https://www.kvkk.gov.tr/Icerik/2053/Yurtdisina-Aktarim)
- [KVKK, standart sözleşme duyurusu](https://www.kvkk.gov.tr/Icerik/8170/Yurt-Disina-Kisisel-Veri-Aktariminda-Kullanilacak-Standart-Sozlesmelerde-Dikkat-Edilmesi-Gereken-Hususlara-Iliskin-Kamuoyu-Duyurusu)
- [KVKK, veri ihlali bildirimi](https://www.kvkk.gov.tr/Icerik/5362/Veri-Ihlali-Bildirimi)
- [Cottgroup, standart sözleşme bildirim cezaları](https://www.cottgroup.com/en/legislation/item/standart-sozlesme-bildirim-yukumlulugu-ve-yaptirimlari)
- [İDİS Uygulama Genel Tebliği](https://portal.idis.gov.tr/docs/notifications/16_03_2023_idis_uygulama_genel_tebligi.pdf)
- [KPMG Vergi, 2026 GEKAP tutarları](https://kpmgvergi.com/yayinlar/mali-bultenler/gumruk/2026-yilinda-uygulanacak-olan-geri-kazanim-katilim-payi-tutarlari-belirlenmistir/3368)
- [Muhasebenews, palet için GEKAP](https://www.muhasebenews.com/ithal-edilen-paslanmaz-sac-urunleri-ile-gelen-palet-ve-ahsap-takozlar-icin-ve-yurt-icine-urun-sevk-ederken-kullandigi-palet-ve-ahsap-takozlar-icin-gekap-beyani-verilecek-mi/)
- [ÇŞİDB, Sıfır Atık Belgesi Yardım Kılavuzu](https://webdosya.csb.gov.tr/v2/cygm/2026/08/S-f-r-At-k-Belgesi-Yard-m-K-lavuzu-20260813101414.pdf)
- [Bloomberg HT, bağımsız denetim eşikleri](https://www.bloomberght.com/sirketlerin-bagimsiz-denetiminde-aktif-ve-ciro-kriterleri-guncellendi-3772034)
- [TÜRMOB, belge saklama süreleri](https://www.turmob.org.tr/BasinOdasi/BasinArsiv/Detay/9682a497-ca2e-487e-abb6-fe3440cdfa69/vuk-a-gore-defter-ve-belgelerin-saklanma-sureleri-ve-ozellikli-durumlar)
- [Yöntem YMM, VUK 2026 hadleri](https://www.yontemymm.com.tr/pratik-bilgiler/vuk-2026-yili-hadleri-ve-tutarlari)
- [PwC, e-Defter berat süresi uzatıldı](https://www.pwc.com.tr/tr/hizmetlerimiz/vergi/dolayli-vergi/bultenler/e-donusum-bultenleri/2026/e-defter-berat-yukleme-suresi-uzatildi.html)
- [Alomaliye, iş kazası cezası ve bildirim](https://www.alomaliye.com/2026/01/08/is-kazasi-idari-para-cezasi-ve-bildirim-yukumlulugu/)
- [Sovos, e-Gider pusulası](https://sovos.com/tr/blog/kdv/e-gider-pusulasi-zorunlu-mudur-sartlari-nelerdir/)
- [Muhasebenews, ihracat e-faturası](https://www.muhasebenews.com/?p=73834)

**Birincil kaynağa ulaşılamayanlar:** Resmî Gazete metinleri (596, 589, 2026/1 GEKAP, 11066 CBK, KDV GUT Seri 57) doğrudan okunamadı. Bunları açabilmek için oturumun ağ ayarlarında `www.turmob.org.tr` ve `www.alomaliye.com` izinli olmalı.
