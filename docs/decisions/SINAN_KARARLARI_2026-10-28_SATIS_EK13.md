# Sinan Kararları — 2026-10-28 (Satış S5: irsaliye ve fatura, KDV/tevkifat satır kararı)

Bu belge önceki karar dosyalarını silmez; SAT-D85…SAT-D100'ü ekler. S5 dalgasının (API-30…37, MOD-04, MOD-06, UI-60…65;
`docs/delivery/SATIS_HAZIRLIK_PLANI_v1.md`) açık sorularını cevaplar. SAT-D7 (elle akış: ERP taslak hazırlar, dışa aktarır,
resmî belge dış programda kesilir, sonra eşleştirilir), DEC-FIN-001/002/003/005, DEC-SAL-006/008, No 25/26 ve DEC-FIN-010
aynen geçerlidir. SAT-D93, SAT-D73/D74'ü (EK12) müşteri durumu, belge ve satır düzeyi değişiklik boyutlarıyla genişletir.

**Kaynak:** Sinan'ın 28 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevapları (üç tur ve bir araştırma sonrası tur). Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi; birkaç soruda
Sinan kendi metnini yazdı, o metin aynen verilir. Sinan iki konuda "araştır" dedi; araştırma notu aşağıdadır ve bağlayıcı
değildir.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| Fatura kesinleşince cari açık kalem (alacak kaydı) nasıl oluşsun? | Eşleştirmede ERP oluştursun | "Dış programdaki resmî fatura ERP taslağıyla eşleştirilince, resmî faturanın tutarıyla tek bir açık kalem oluşur. CSV içe aktarım yalnız ERP'de başlamayan faturalar için kalır." (seçilmeyenler: "Yalnız CSV içe aktarım", "ERP taslağı tutarıyla") |
| Faturanın vade (son ödeme) tarihi nereden gelsin? | Siparişin vadesinden | "ERP, siparişteki vade modelinden ve resmî fatura tarihinden hesaplar; resmî faturadaki vade farklıysa Kontrol Merkezi'ne düşer." (seçilmeyenler: "Resmî faturadaki vade", "Eşleştirmede elle") |
| Faturaya hazır sevkiyatlar varsayılan olarak nasıl gruplansın? | Müşteri başına en büyük uyumlu grup | "Aynı müşteri, para birimi, vergi ve vade koşulundaki tüm sevkiyatlar tek faturada önerilir; kullanıcı bölebilir, kısmi miktar seçebilir." (seçilmeyenler: "İrsaliye başına bir fatura", "Sipariş başına bir fatura", "Dönemsel toplu fatura") |
| Fatura taslağını kim onaylasın? | Muhasebe, hazırlayan ≠ onaylayan | "Taslağı hazırlayan kişi onaylayamaz; onayı Muhasebe verir." (seçilmeyenler: "Müdür", "Rutin onay yok") |
| Dış programda kesilen resmî fatura ERP taslağıyla eşleştirilirken tutarlarda ne kadar fark kabul edilsin? | Kuruş toleransı, Ayarlar'da | "Fatura no, ETTN, müşteri, para birimi tam eşleşmeli; tutar/KDV/tevkifatta Ayarlar'daki kuruş toleransı (varsayılan 0) kadar fark kabul, fazlası onaya gider; resmî değer esas." (seçilmeyenler: "Her şey birebir", "Yüzde tolerans") |
| Resmî faturanın tarihi taslaktaki tarihten farklıysa döviz kuru nasıl alınsın? (TL karşılığı için) | Resmî tarihe göre yeniden | "Kur, resmî fatura tarihinden önceki iş gününün TCMB kuruyla yeniden bulunur; fark işaretlenir." (seçilmeyenler: "Taslaktaki kur sabit", "Resmî faturadaki kur") |
| ERP fatura taslağı dış programa hangi biçimde aktarılsın? | Kendi metni: "Excel, ubl-tr xml ve programın şablonuna göre, kullandılığım program mikro ve e-mikro portal bunlara göre araştır" | — (seçenekler: "Excel + UBL-TR XML (Önerilen)", "Programın kendi şablonu", "Yalnız Excel", "Yalnız UBL-TR XML") |
| Faturadaki birim fiyat sipariş fiyatından YÜKSEK çıkarsa ne olsun? | Sipariş revizyonu şart | "Fiyat değişikliği sipariş revizyonu ister (DEC-SAL-002); revizyon onaylanana kadar fatura ilerlemez." (seçilmeyenler: "Müdür faturada onaylasın", "Yalnız uyarı") |
| KDV tevkifatındaki tutar eşiği (belirli tutarın altındaki faturalarda tevkifat uygulanmaması) nasıl ele alınsın? | Kendi metni: "Kdv oranı ve tevkifat oranı na sipariş oluştururken karar vermeli, ürün/hizmet satırı bazında kdv ve tevkifat değişebilir/değiştirilebilir, (kdv ve tevkifat ürün/hizmet biçimine türüne göre değişiklik gösterebiliyor, ) kdv ve tevkifat uygulanması müşteriye bağlı olarak değişebiliyor, nihai tüketici mi istisnalı mı bir belgeye istinaden mi alıyor yada alıcının yatırım teşvik belgesini ibraz ediyorsa kdv siz veya tevkifatsız satış yapılabilir yada ihracat/ihraç kayıtlı satışsa durumlar farklı olabilir gibi bunlarıda araştır" | — (seçenekler: "Eşik Ayarlar'da (Önerilen)", "Dış program karar versin", "Faturada elle") |
| Bir sevkiyat ne zaman faturalanabilir sayılsın ve hangi miktarla? | Kendi metni: "Bir malzeme sevk edilip irsaliyesi kesilince fatura düzenlenebilir yada siparişe istinaden toplu tek bir fatura düzenlenip( teslimi depo teslim olacak şekilde) bu tek kesilmiş faturaya istinaden daha sonra irsaliyeler kesilebilir ve burdada tek kesilen faturaya iliştirilen irsaliyelerin toplamı faturayı geçerse, geçen miktar için ekstra fatura kesilir" | — (seçenekler: "Onaylı irsaliye yeter (Önerilen)", "e-İrsaliye numarası da şart", "Sevk edilen miktarın tamamı") |
| Faturalanmamış bir sevkiyat kaç gün sonra 'gecikmiş' sayılıp Kontrol Merkezi'nde görünsün? | Sevk tarihinden 7 gün, Ayarlar'da | "VUK'taki 7 günlük fatura süresine göre; gün sayısı Ayarlar'dan değiştirilebilir." (seçilmeyenler: "Teslim tarihinden", "Kontrol yok") |
| Resmî fatura iptal edilir veya müşteri reddederse (e-Fatura RED / e-Arşiv iptal) ne olsun? | ERP'de iptal komutu | "'Resmî belge iptal' komutu açık kalemi ters kayıtla kapatır, sevkiyatlar yeniden faturalanabilir olur. Fiyat farkı faturası S6'da." (seçilmeyen: "Şimdilik ERP dışında") |
| Sipariş satırında sistemin bulduğu KDV/tevkifat (veya istisna) değiştirilirse bu değişikliği kim onaylasın? | Muhasebe onaylasın | "Satışçı gerekçeyle değiştirir, Muhasebe onaylayınca geçerli olur; vergi riski muhasebede." (seçilmeyenler: "Satış sorumlusu yeter", "İki onay") |
| Depo teslimde faturası kesilmiş ama henüz sevk edilmemiş mal stokta nasıl görünsün? | Kendi metni: "Müşteriye rezerve edilmemiş ürünler içinde sipariş miktarı ve tutarı üzerinden de fatura kesilebilir, stoktaki ürün müşteri siparişene rezerv edilince o zaten müşteri rezervi şeklinde görülecektir" | — (seçenekler: "Müşteri emaneti (Önerilen)", "Rezerv", "Stoktan düşülsün") |
| Depo teslimde faturadan AZ sevk edilirse (kalan teslim edilmezse) hangi belgeyle kapatılsın? | Kendi metni: "Mali müşavir karar versin(önerilen) ama alıcının iade faturası kesilebileceği gibi eksik kalan miktar için daha sonra irsaliye düzenlenerek ek malda gönderilebilri buna mali müşavir ve satışçı ve müdür farkedip üçüde durumu yönetebilmeli" | — (seçenekler: "Mali müşavir karar versin (Önerilen)", "Fiyat farkı/iptal faturası", "Alıcının iade faturası") |
| İhraç kayıtlı satış (KDV'nin tahsil edilmediği, ihracatçıya teslim) hangi şirketlerde olacak? (Yalnız imalatçı yapabilir) | Kendi metni: "3 firmada yapabilir" | — (seçenekler: "Yalnız imalatçı şirketler", "Hiçbiri", "Mali müşavirle teyit") |

## Araştırma notu (bağlayıcı değil; mali müşavir/YMM teyidi gerekir)

Sinan'ın "araştır" dediği iki konu için yapılan kısa araştırmanın özeti. Birincil GİB metinleri bu oturumda doğrudan
açılamadı; bulguların çoğu ikincil kaynaklara dayanır.

- **Satır KDV'si** üç girdiden çözülür: ürün/hizmet türü (oran sınıfı: genel %20, indirimli %10/%1), alıcının durumu
  (KDV mükellefi, nihai tüketici, belirlenmiş alıcı, ihracatçı) ve alıcının belgesi (istisna/teşvik belgesi, ihraç kaydı).
- **İstisnalar:** ihracat (KDVK 11/1-a); ihraç kayıtlı teslim (KDVK 11/1-c: KDV hesaplanır ama tahsil edilmez, tecil-terkin;
  ikincil kaynaklara göre yalnız **imalatçı** satıcı yararlanabilir ve ihracatçı malı 3 ay içinde ihraç etmelidir); yatırım
  teşvik belgesi (KDVK 13/d, yalnız belge kapsamındaki makine-teçhizat; tipik çelik ürünü genelde kapsam dışı); KDVK Geçici 37
  (teşvik belgeli yatırımın inşaat işlerine ilişkin teslimler, alıcının vergi dairesinden aldığı istisna belgesiyle KDV'siz);
  diplomatik istisna. İstisnalı teslimde tevkifat da uygulanmaz.
- **Demir-çelik tevkifatı** (KDV GUT I/C-2.1.3.3.8): oran 5/10 (2022'den beri); belirlenmiş alıcılar ve KDV mükellefi
  alıcılar uygular; nihai tüketicide uygulanmaz. Hurda teslimlerinde oranlar kaynaklarda tutarsızdır (teyit gerekir).
- **Tevkifat tutar sınırı:** kısmi tevkifat KDV dahil bedel VUK 232 fatura sınırını aşarsa uygulanır (2026 için 12.000 TL,
  ikincil kaynak); sınır işlem bütünü üzerinden değerlendirilir, fatura bölünerek aşılmaz.
- **Önce fatura, sonra irsaliye:** VUK teslimden önce fatura düzenlemeyi yasaklamaz; KDVK 10/b gereği vergiyi doğuran olay
  faturadaki miktar için fatura tarihinde doğar. Her sevk için ayrıca irsaliye gerekir ve irsaliye/fatura birbirine bağlanır.
  Fazla sevk edilen miktar için teslimden itibaren 7 gün içinde ek fatura gerekir; eksik sevkte KDVK 35 kapsamında matrah
  düzeltmesi yapılır (belge biçimi mali müşavir kararıdır).
- **Dış program:** Mikro'nun Excel içe aktarma şablonu açık kaynaklarda bulunamadı; e-Mikro GİB lisanslı özel entegratör
  olduğundan UBL-TR 1.2.1 XML'i kabul etmesi beklenir (teyit edilmedi).

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D85 | Açık kalemi kim oluşturur | ERP'de başlayan faturada açık kalem, resmî fatura ERP taslağıyla **eşleştirilince** ERP tarafından, **resmî faturanın tutarıyla** ve tek kayıt olarak oluşur (Finance'taki ortak kaynak kimliğiyle; ikinci açık kalem üretilmez). Finance CSV içe aktarımı yalnız ERP'de başlamayan faturalar için kalır (DEC-FIN-002/003/005) | API-35/36, MOD-04 |
| SAT-D86 | Vade | Vade, siparişin vade modelinden (DEC-SAL-008 anlık görüntüsü) ve **resmî fatura tarihinden** ERP tarafından hesaplanır; resmî faturadaki vade farklıysa Kontrol Merkezi'ne düşer | API-36, API-37, MOD-04 |
| SAT-D87 | Varsayılan gruplama | Faturaya hazır kayıtlar DEC-SAL-006 uyum anahtarlarına göre **müşteri başına en büyük uyumlu grup** olarak önerilir; kullanıcı grubu bölebilir ve kısmi miktar seçebilir | API-31, UI-61/63 |
| SAT-D88 | Fatura taslağı onayı | Fatura taslağını **Muhasebe** onaylar; **hazırlayan onaylayamaz** (P6 aynen). İzin adı uygulama PR'ında manifest'e eklenir, hiçbir role atanmaz | API-33 |
| SAT-D89 | Eşleştirme toleransı | Fatura no, ETTN, müşteri ve para birimi **tam** eşleşmelidir. Tutar, KDV ve tevkifatta Ayarlar'daki **kuruş toleransı** (varsayılan 0) kadar fark kabul edilir; fazlası onaya gider; **resmî değer esastır**. EK12'de S5'e bırakılan tevkifat kuruş farkı sorusunu kapatır | API-35, Ayarlar |
| SAT-D90 | Kur tarihi | Resmî fatura tarihi taslaktan farklıysa TL karşılığı için kur, **resmî fatura tarihinden önceki iş gününün TCMB kuruyla** yeniden bulunur (DEC-FIN-010, SAT-D54/D55) ve fark işaretlenir | API-35/36 |
| SAT-D91 | Dışa aktarım biçimi | Fatura taslağı **Excel**, **UBL-TR (1.2.1) XML** ve dış programın **kendi içe aktarma şablonu** biçimlerinde dışa aktarılır. Dış program **Mikro** ve **e-Mikro portal**dır. Mikro şablonu için örnek şablon dosyası gereklidir; gelene kadar Excel ve UBL-TR XML üretilir | API-34 |
| SAT-D92 | Siparişten yüksek fatura fiyatı | Faturadaki birim fiyat sipariş fiyatından **yüksekse** sipariş **revizyonu** gerekir (DEC-SAL-002); revizyon onaylanana kadar fatura ilerlemez. Düşük fiyat No 25 gereği uyarı ve fark kaydıdır | API-32/33 |
| SAT-D93 | KDV/tevkifat satır kararı | KDV oranı, istisna ve tevkifat **sipariş oluşturulurken satır bazında** belirlenir ve satırda **değiştirilebilir**. Belirlemede ürün/hizmet türü (SAT-D82 KDV sınıfı, SAT-D74 tevkifat kodu), **müşterinin durumu** (KDV mükellefi, nihai tüketici, belirlenmiş alıcı, istisnalı) ve **müşterinin sunduğu belge** (istisna belgesi, yatırım teşvik belgesi, ihracat, ihraç kayıtlı) birlikte dikkate alınır. Satırdaki değişikliğin onayı SAT-D97'dedir. Oran, kod ve belge kuralları kodla sabitlenmez; Ayarlar/Cari/Ürün kayıtlarında efektif tarihli tutulur. SAT-D73/D74'ü bu boyutlarla genişletir. **Araştırmaya dayalı, geri alınabilir:** tevkifat tutar sınırı (VUK 232 fatura sınırı) Ayarlar'da efektif tarihli tutulur ve işlem (sipariş) bütünü üzerinden değerlendirilir | Sipariş satırı fiyatlama, API-03, API-32, Ayarlar, Cari kartı, Ürün kartı |
| SAT-D94 | Faturalanabilirlik ve iki akış | (1) **Önce irsaliye:** malzeme sevk edilip irsaliyesi kesilince fatura düzenlenebilir. (2) **Önce fatura (depo teslim):** siparişe istinaden toplu tek fatura düzenlenebilir; sonra bu faturaya istinaden irsaliyeler kesilir. Bir faturaya bağlanan irsaliyelerin toplamı fatura miktarını **aşarsa, aşan miktar için ek fatura** kesilir. Fatura–irsaliye bağı bu iki akışı taşıyacak biçimde kurulur | API-31/32, MOD-06, UI-61/63 |
| SAT-D95 | Faturalanmamış sevk gecikmesi | Faturalanmamış sevkiyat, **sevk tarihinden itibaren Ayarlar'daki gün sayısı** (başlangıç 7, VUK 231/5) sonra gecikmiş sayılır ve Kontrol Merkezi'nde görünür | API-37, Ayarlar |
| SAT-D96 | Resmî fatura iptali/reddi | ERP'de **"resmî belge iptal" komutu** açık kalemi **ters kayıtla** kapatır ve ilgili sevk paylarını yeniden faturalanabilir yapar (silme yok). Fiyat farkı faturası S6'dadır | API-35/36, MOD-04 |
| SAT-D97 | KDV/tevkifat değişikliğinin onayı | Satırda sistemin bulduğu KDV/tevkifat/istisnayı satışçı **gerekçeyle** değiştirir; değişiklik **Muhasebe onayıyla** geçerli olur (audit) | Sipariş satırı, API-12 benzeri onay deseni |
| SAT-D98 | Depo teslimde stok | Depo teslim faturası **rezerve edilmemiş ürünler için de sipariş miktarı ve tutarı üzerinden** kesilebilir; fatura stok hareketi yapmaz. Ürün siparişe rezerve edildiğinde zaten **müşteri rezervi** olarak görünür (mevcut tahsis/rezerv). Ayrı "emanet" lokasyonu açılmaz | API-32, Stok tahsisi |
| SAT-D99 | Depo teslimde eksik sevk | Fatura miktarından az sevk edilen kalan, ERP'de **"faturalanmış, sevk edilmemiş" bakiye** olarak görünür. Kapatma yolu (alıcının iade faturası **veya** kalan miktarın sonradan irsaliyeyle sevki) **mali müşavirle** belirlenir; bakiye **mali müşavir (Muhasebe), satışçı ve Müdür** tarafından görülür ve yönetilir | API-37, UI-64/65, S6 iade |
| SAT-D100 | İhraç kayıtlı satış | İhraç kayıtlı satış **üç şirkette de** yapılabilir (Sinan'ın kararı). Araştırma notuna göre yasal koşul (satıcının imalatçı olması, 3 aylık ihracat süresi) mali müşavir sorumluluğundadır; ERP 3 aylık süreyi izler ve uyarır | Sipariş satırı, API-32, API-37 |

## Uygulama notları

- SAT-D100 ile `DEC-ORG-005`/`OPEN-030` birlikte okunur: GIN gerçek unvan/VKN gelene kadar `InvoiceCapable=false` ve resmî
  belgeye kapalıdır; bu durumda GIN için fatura taslağı reddedilir (fail-closed). SAT-D100 bu kilidi açmaz.
- SAT-D93'teki müşteri durumu ve belgeler (istisna belgesi, teşvik belgesi no/tarih/kapsam/kalan miktar, ihraç kayıtlı
  belge) Cari kartında **geçerlilik süreli** tutulur; belge kapsamı aşılırsa satır normal KDV'ye döner (öneri, geri alınabilir).
- UBL-TR alanları (fatura tipi SATIS/TEVKIFAT/ISTISNA/IHRACKAYITLI, istisna kodu, tevkifat kodu) taslakta ve eşleştirme
  anahtarında tutulur; kod listeleri Ayarlar'da efektif tarihli olur (kodla sabitlenmez).
- Fatura–irsaliye bağı N:N'dir; bakiyeler: faturalanıp sevk edilmemiş (SAT-D99), sevk edilip faturalanmamış (SAT-D95),
  fazla sevk (SAT-D94 ek fatura taslağı).

## Açık kalanlar

- Hurda alım/satımı ve fason metal işleme hizmeti satışı olup olmadığı ve bunların tevkifat oranları sorulmadı (araştırmada
  oranlar tutarsız; mali müşavir teyidi gerekir).
- KDVK Geçici 37 istisna belgeli satışların sıklığı sorulmadı.
- Mikro içe aktarma şablonunun örnek dosyası bekleniyor (SAT-D91).
- Araştırma notundaki oran ve sınırlar (5/10, 12.000 TL, istisna/tevkifat kodları) mali müşavir/YMM tarafından teyit
  edilmelidir; teyit edilene kadar Ayarlar değeri yoksa ilgili alan "kaynak yok" kalır.
- `OPEN-016` (e-belge sağlayıcı ve entegrasyon), `OPEN-012` (kur saati), `OPEN-013` (yeni `sales.invoices.*` izinlerinin
  grant'i ve Muhasebe personası), `OPEN-018/021` (saklama), `OPEN-019` (şablon/logo) açık kalır; bu belge hiçbirini kapatmaz.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
- Araştırma notu hukuki görüş değildir.
