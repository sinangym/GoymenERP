# GöymenERP — son rapor ve geliştirmeye başlangıç planı

Sürüm: 7 • Tarih: 10 Eylül 2026 • Durum: **incelemeye sunulan teslim/teknik paket**

## 1. Sonuç

Genel geliştirme hazırlığını ve kararları belli dilimlerin teknik çalışmasını başlatmaya engel yoktur. Bu, bütün modüllerin kodlanmaya hazır veya mevcut sistemin tamamlanmış olduğu anlamına gelmez. Doğru sıra: mevcut kod/PR'ları uzlaştırmak, bir çalışma dilimini sözleşme ve kabul testleriyle netleştirmek, backend ve frontend'i aynı dilimde geliştirmek, kanıtla bitirip sıradakine geçmektir.

Ürün yönü tutarlıdır: **Hubvagol'ün cari/sipariş çalışma merkezi yaklaşımı + StokERP'nin stok/üretim ayrıntıları**, Göymen'in çok şirketli ve ticari/fason akışına göre yeniden tasarlanacaktır. Bu esinlenmedir; eski kod, kusurlar veya ticaret rolü zorunlulukları kör kopyalanmaz. Ekran benzerliği tek başına doğru stok/finans/üretim davranışı kanıtı değildir.

Yeni genel tercih anketi gerekmiyor. KUL-01=B ve KUL-02=B kaydedildi. Açık gerçek değerler ileride tam ihtiyaç duyulan dilimde, örnek ve 2–3 seçenekle sorulacak; zaten kararlaştırılmış konular yeniden sorulmayacak. Belirsiz para/stok/yetki politikasıyla o işlev etkinleştirilmeyecek; bağımsız iş bekletilmeyecek.

Bu paket **kodun çalıştığını bildiren teslim belgesi değildir**. Testler aşağıda istenen kabul ölçütleridir; çalıştırılmamış testlere geçti denmez. Bu turda ürün kodu, veritabanı, portal, gerçek müşteri kaydı veya production ortamı değiştirilmez.

## 2. Kaynak, gözlem ve güncellik

Kaynak önceliği: son açık kullanıcı kararı → tarihli karar kaydı → `KARAR_SICILI.md` → master rapor → ilgili spec/ADR → eski sistem referansları. Süreç için mevcut v3/v2/orchestrator governance ve AGENTS geçerlidir; bu rapor yeni governance değildir.

| Kaynak | Bu raporda kullanımı / sınırı |
|---|---|
| [10 Eylül karar kaydı](../../decisions/SINAN_KARARLARI_2026-09-10.md) | Son konuşma cevaplarının açık anlamı; Q01–Q13, F01, KUL-01/02 ve korunan temel kararlar. |
| Yerel v6.1 raporu | Önceki analizin kaynak kopyası; SHA-256 karar kaydındadır. v6.1'in KUL önerilerinin yerine son B cevapları geçer. |
| `origin/main` — `9f5b9bfdef249b9c8e89c6668f66b6aa55c34602` | Bu paketin başlangıç kod tabanı. Sonraki çalışan HEAD tekrar doğrulanır. |
| [Cari #227](https://github.com/sinangym/GoymenERP/pull/227), [#228](https://github.com/sinangym/GoymenERP/pull/228) | 10 Eylül okumasında açık draft adaylar; var olan işi tekrar yazmadan uzlaştır. Merge edilmiş veya kabul testleri bu turda geçmiş varsayılmaz. |
| [Ürün JIT #181](https://github.com/sinangym/GoymenERP/pull/181), [belge JIT #202](https://github.com/sinangym/GoymenERP/pull/202) | Mevcut spec adayları; ilk kapsamları yeniden icat etme. Güncel durum/head uygulamadan önce okunur. |
| [Issue #66](https://github.com/sinangym/GoymenERP/issues/66) | Ortak koordinasyon ve açık karar girişi; eski issue gövdesi yeni yorumlardan daha güncel sanılmaz. |
| Hubvagol / StokERP incelemeleri | Önceki gözlemler işlev referansıdır. Bu turda canlı sistem yeniden açılmadı; bütün alt sayfalar/butonlar bugün tekrar doğrulandı denmez. |

Paket mevcut `task_manifest.json`, `DELIVERY_BACKLOG_v1.json`, `AUTONOMOUS_PROGRESS.md` veya `DECISION_PACK.md` sahipliğini devralmaz. Envanter, bunlara uzlaştırılacak öneri katmanıdır. Hazır görünen eski bir manifest satırı yeni iş politikasını seçme izni değildir.

## 3. Ürünü kullanıcı açısından netleştirelim

### 3.1 Cari: tek firma, bütün günlük işler

Bir firmanın kartını açınca kimliği ve yetkiniz olan Göymen şirketleriyle ilişkisi görünür. Birden fazla şirket seçebilirsiniz. Her şirketin siparişi, faturası, ekstresi ve bakiye para birimi belirgindir; toplam görünüm kendiliğinden borç/alacak mahsuplaştırmaz. Yazma anında işlemin hangi şirkete ait olduğu nettir.

Cariyi müşteri/tedarikçi/fasoncu diye zorunlu sınıflandırmayız. Aynı firma bir işlemde mal satar, diğerinde hizmet alır. İşlem türü sipariş/belgede seçilir. Firma/Kişi ayrımı, VKN/TCKN gibi kimlik bilgisi ve kullanıcı yetkisi ise farklı ihtiyaçlardır. Eski rollerin verisi/audit geçmişi korunarak günlük zorunluluk kaldırılmalıdır.

Kartın alt sayfaları: özet ve firma bilgileri; açık/kapalı siparişler ve kalanlar; irsaliyeler ve detayları; faturalar; dekont/ödeme araçları; şirket/para birimi bazlı ekstre; belge ve sözleşme arşivi; Ticaret Sicili kaydı ve uyarıları. Arama, filtre, sekme, sayfa ve geri dönüş konumu kullanıcıya işini bıraktığı yerden sürdürmelidir. Çalışmayan düğme veya gerçek veriye dayanmayan metrik tamamlanmış gibi gösterilmez.

Ortak firma kimliği ile şirket hesabı farklıdır. İlk yetkili iş işleminde gerekirse hesap oluşturulması hedefi, ayrı bir komut/izin sözleşmesiyle kurulacaktır. Listeyi açmak veya aramak kayıt yaratmaz; bugünkü yönetim izni sessiz genişletilmez.

### 3.2 Sipariş: tek ekran, talep ve kesinleşme

Ön sipariş sayfası yoktur. Talep ve kesin sipariş aynı yerde anlaşılır durum/ikon/renk ile yönetilir; yalnız renge dayalı gösterim yapılmaz. Talep malzemeyi bloke etmez. Kesin sipariş geldiğinde stok uygunluğu yeniden değerlendirilir; talep otomatik tahsis sayılmaz.

Sipariş şirketi, tarafı, alış/satış yönü, mal/hizmet niteliği, mal sahibi, miktar/birim, teslim ve fiyat koşullarını taşır. Fason hizmet alışı ile satışı ayrı senaryolardır; siparişte gelen/giden malzeme irsaliyeleri satır bazında kısmi ilişkilendirilir. Fiziksel teslim, hizmet hak edişi ve resmî fatura birbirinin yerine geçmez.

Başlangıç onayı Q01-B'dir. Sonra müşteri kaşeli yazısının gerekeceği Q01-A etkinleştirilebilecek şekilde tasarlanır; bugün erkenden zorunlu yapılmaz. Revizyon beklerken eski onaylı sürüm Q02-B ile yürür; yeni sürüm onayında artık gerçekleşmiş miktarlar tekrar doğrulanır. Gelecekte Q02-A yalnız etkilenen gerçekleşmemiş bölümü bekletebilir.

Sipariş değişikliği önceki sözleşmeyi silmez. Kullanıcı önceki sürümü açıp neyin değiştiğini ve o tarihteki çıktıyı görür. Yeni şablon yüklemek eski PDF'yi değiştirmez.

### 3.3 Stok: fiziksel gerçek, sahiplik ve etiket

Ürün kartı malzemenin tanımıdır; stok etiketi belirli fiziksel malzemedir. Kalınlık, en, boy, teknik kalite/alt kalite, standart/ikinci kalite ve kırıklı/dalgalı/çapak gibi birden fazla kusur doğru seviyede saklanır. Belirli bir parça farklıysa bütün lotu yanlış etiketlemek yerine ayrılan alt miktar izlenir.

Stok miktarı, uygun/ayrılmış miktar, beklenen plan çıktısı, müşteri mülkiyeti ve fiziksel konum ayrı gerçeklerdir. Planlanan ürün bugün depoda varmış gibi satılabilir stok yapılmaz. Normal kalite serbest bırakma kapısı eklenmez.

Şirket malından hurda şirkete; müşteri malından hurda müşteriye aittir. Müşteriye iade edilene kadar depoda görünür. Kullanılabilir artan parça/ uçbaş, hurda ve gerçek kayıp birbirinin yerine kullanılmaz; her uçbaş kusurlu veya hurda değildir. Yalnız transfer için irsaliye tutulabilir; bunun için sahte sipariş veya ikinci hareket oluşturulmaz.

### 3.4 Üretim: planın beklenen çıkışından zincir plan

Beş motorun ortak zincir yönetimi vardır: dilme, hadde, boru/profil, boy kesme ve boya. Kullanıcı rotayı görür, her adımın beklenen girdisini/çıktısını, iç/dış operasyonu, bağlı siparişleri ve geciken/eksik kısmı yönetir.

| Asgari rota | Kullanıcının göreceği örnek akış |
|---|---|
| D → P → B | Dilmeden beklenen şerit → profil planı → boya planı |
| D → H → P → B | Dilme → hadde → profil → boya |
| P → B | Profil → boya |
| D → K | Dilme → boy kesme |
| D / H / P / K / B tek başına | Malzeme yalnız seçilen bir operasyondan geçer |

Bu dokuz yol asgari kabul kapsamıdır; fiziksel açıdan uygunsuz her kombinasyona otomatik izin verildiği anlamına gelmez. Operasyonun girdi/çıktı uygunluğu sözleşmede doğrulanır. Her ara adım kısmi gerçekleşebilir; çıkan uygun fiziksel miktar, üst plan tümü bitmeden sonraki hatta ilerleyebilir. Aynı malzemenin beklenen çıktısı için alt plan önceden oluşturulabilir; tahmini ve gerçek kimlik bağı açıkça tutulur.

Üst plan azaltılırsa etkilenen alt planlar görünür; gerçekleşmiş tüketim/çıktı silinmez. Ortak araç veya hat kullanımına rağmen paketler mal sahibine göre ayrıdır. Dış işlem müşterinin malıysa o işe ilişkin açık yetki gerekir.

Boya için zorunlu gram sarf ayrıştırması yoktur. Ambalajsız gerçek net kg kullanılır; giriş–çıkış farkı açıklanabilir/onaylanabilir görünürlükte kalır. Homojen grup tartımında ölçülmüş veri ve onayla dağıtılmış miktar ayrılır. Formüller UI, rapor ve AI'de farklı hesaplanmaz.

### 3.5 Sevk, yükleme, iade ve fatura

Yükleme talimatı ile fiilî yükleme ayrıdır. Plan sapması, toplam sipariş teslim toleransı ve üretim tartı farkı tek “tolerans” alanına sıkıştırılmaz. Artı/eksi tolerans vardır; sayısal sınır kullanıcıca belirlenmeden tahminen production değeri girilmez.

Tolerans içi eksik son teslimde kalan açık durur. Örnek: 10 birim siparişte 9,9 fiilî teslim, 0,1 açık kalandır. Yetkili 0,1'i gerekçeyle kapatabilir; fiilî teslim yine 9,9'dur. Bu örnek bir tolerans veya yuvarlama politikası seçmez. Bağlı plan/tahsis ve başka siparişler etkileniyorsa güncel sürüme göre önizlenir; diğer siparişler topluca kapatılmaz.

İade; fiziksel geri geliş, siparişte ikame veya kapatma isteği, fatura düzeltmesi ve para iadesi olarak ayrıştırılır. Birini yapmak diğerlerini otomatik olmuş saymaz. Geçen yılın kapalı işlemi de izinli iade/düzeltmeye referans olabilir; geçmiş kayıt yeniden yazılmaz.

Sipariş dışı gerçek gelen/giden faturalar işlenir. Hizmet faturalanabilir miktarı işlenip teslim edilen uygun kısımla ilişkilidir. İngilizce proforma ise resmî fatura değildir; muhasebe/finans/stock hareketi doğurmaz.

### 3.6 Finans, kasa/banka ve muhasebe

Bu modüller “ileride entegrasyon” gerekçesiyle yok sayılmaz: gerçek fatura, tahsilat/ödeme, dekont, kasa/banka hareketi, ödeme aracı ve cari ekstre ilişkileri ilk işlev envanterindedir. Bankanın dış sistem entegrasyonu ile ERP'de banka hesabı/hareketi tutmak ayrı kapsamdır.

Tek hukukî olayın finansal kaydı iki kez üretilmez. Şirket, para birimi, işlem tarihi, vade, kaynak belge ve düzeltme bağı açık olur. Tamamlanmış tahsilat sonrası iade veya başarısız ödeme aracı karşılıklı ama ayrı olaylarla işlenir. Defter/hesap eşleme ve para kuralları kilitlenmeden hazır muhasebe davranışı iddia edilmez. Mevzuat doğrulaması gerekli kapıda yetkili uzman ve güncel birincil kaynakla yapılır; bu rapor hukukî görüş değildir.

Maliyet/değerleme, ayrıntılı maliyet dağıtımı ve dış banka entegrasyonunun bilinçli ertelenmesi korunur.

### 3.7 Belgeler, yetki ve AI

Karışık biçimde şablon yüklenir; AI alan eşlemesini önerir, yetkili doğrular. Yayınlanan şablon ve belge sürümü değişmez kaynağa bağlanır. Modelin bulduğu herhangi bir alan SQL ile çekilmez; izinli alan kataloğundan gelir.

Merkez yöneticiler şirket ve işlem bazında yetki verir. Sayfayı görme, kaydetme, onaylama, fiyat/tutar görme, dışa aktarma ve gönderme farklı izinlerdir. Onaylı siparişi oluşturan yetkili kendi siparişini onaylayabilir; bu kendi erişim ayrıcalığını onaylamak değildir.

Fiyatı göremeyen yetkili için kör gönderim ayrı akıştır: sabit onaylı belge, gerçek hedef ve kanal, gönderimde tekrar yetki kontrolü. Kullanıcıya veya onun AI bağlamına fiyatlı dosya/preview/URL verilmez. Hedef adresini değiştirip belgeyi kendine yönlendirme kontrol edilir. Bu güvenlik normal yetkili belge paylaşımını kapatmaz.

AI; ekran bağlamı, yüklenen görüntü, ses ve sistemde izinli kaynaklarla hangi işin gerekli olabileceğini önerir. Belirsiz görseli kesin gerçek saymaz; kritik yazma öncesi kaynak, şirket, komut ve etkiyi doğrular. İş öğrenmesi kaynaklı ve sürümlü bilgi güncellemesidir; kontrolsüz kendi kendine iş politikası değiştirme değildir. “Genel zekâ” garantisi verilmez; ölçülebilir görev başarısı, yetkili araç kullanımı ve hata davranışı hedeflenir. AI kapalıyken ERP'nin normal iş akışı devam eder.

Mikro ve Ticaret Sicili hedeflerinde tarayıcı otomasyonu olabilir; API zorunlu değildir. Ancak arayüz/oturum değişikliği, MFA ve CAPTCHA nedeniyle insana devir gerekir. Ortak hesap parolası sır kasasındadır; model veya git dosyalarında değildir. İrsaliye/fatura dış gönderiminde sonuç belirsizse önce sorgu ve uzlaştırma yapılır. Gelen belge AI önerisidir; fiilî mal kabulünü kendiliğinden gerçekleştirmez. Haftalık/tek tuş sicil sorgusu aynı izlenebilir hat üzerinde çalışır. Aktif/pasif bütün carilerin yetkili kontrol durumu görünür; aynı kimliğin kaynak sorgusu gereksiz çoğaltılmaz. Kimlik eksik/kapsam dışı/çelişkili eşleşme ve erişim hatası “ilan yok” değildir; yabancı firmanın Türkiye'deki sicilli şubesi ayrıca değerlendirilir. Bugünkü çalışma bu görevleri gerçekten kurmaz.

### 3.8 Şirket yedeği ve kesinti

Kullanıcının istediği restore yöntemi nettir: **temiz aktif sisteme yalnız seçilen şirketin yedeğini yükleyip o şirket varmış gibi çalışmak**. Demir yüklenirse yalnız Demir'in işlem/stok/finans geçmişi ve gerekli ortak master alt kümesi vardır: cari, ürün/hizmet, birim, kullanılan politika/sürüm ve zorunlu kaynak bağları. Demir'in Sac ile kendi ticareti Demir kaydı olarak kalabilir; bu Sac'ın defterini yüklemek değildir.

Mevcut dolu sistemi sessizce silmek veya iki şirket verisini otomatik birleştirmek bu restore işi değildir. Ayrı temiz hedef hazırlanır; doğrulanmış yedek ve geçiş kararıyla aktif kullanıma alınır. Veritabanı kadar belge dosyaları, anahtarlar, audit ve arama/iş kuyruğu durumu da ele alınır; eski dış gönderimler yeniden yollanmaz. Bu hedefin kodda tamamlandığı söylenmez: gerçek şirket-ayırma/restore testleri şarttır.

KUL-01=B ile sayımda hareketler sürebilir; her sayımın ölçüm zamanı ve o aralıktaki fiziksel hareketler uzlaştırılır. Sayım farkı otomatik ikinci kez stok yazmaz. KUL-02=B ile ERP'ye erişim yokken yeni başlangıç ve çıkış bekler. Yalnız dış internet/AI/Mikro kesilmiş fakat ERP erişilebilir ise bunlara bağımlı olmayan normal ERP işleri devam eder. Devam eden makinede güvenlik kararını model vermez; saha prosedürü uygulanır. Çevrimdışı onaysız stok işleme ilk kapsama eklenmez.

## 4. Girdiler ne zaman gerekli? (§13.5 yanıtı)

**Tamamını sistem bittikten sonraya bırakamayız; tamamını bugün doldurmak da gerekmez.** Yapı şimdi kurulmalı, gerçek değerler ilgili kabulden önce verilmelidir.

| Zaman | Gerekli olan | Beklerse etkisi |
|---|---|---|
| Şimdi / mimari ve ilgili dilim tasarımı | CompanyId sınırları, tipli birimler/para, ayar ve izin altyapısı, audit, sürüm, idempotency, outbox, belge değişmezliği, yedek/restore tasarımı, AI araç sınırı | Sonradan eklenmesi çoğu modülü yeniden yazdırır; bunlar ertelenmez. |
| İlgili hesap/stock/üretim diliminin kabulünden önce | Onaylı formül ve golden örnekler, birim dönüşümleri, yuvarlama, tolerans ve miktar/teslim esasları | Arayüz ve test iskeleti hazırlanabilir; belirsiz iş etkisi etkinleştirilemez. Test verisi açıkça sentetik ve production dışıdır. |
| İlgili ekranın kullanıcı kabulünden önce | Gerçek antet/şablon örnekleri, temel firma bilgileri, yetki senaryoları ve representative belge/sipariş örnekleri | Sentetik fixture ile geliştirme sürer; gerçek çıktı ve kullanım kabulü bitmiş sayılamaz. |
| İlgili donanım/ortam provasından önce | Yazıcı/etiket, tartı, tarayıcı, ağ/sunucu, kullanıcı/cihaz ve hacim örnekleri | Soyut arayüz kurulabilir; gerçek cihaz/performance uyumu doğrulanmış olmaz. |
| Canlıya çıkıştan önce | Kesin kullanıcı–şirket izinleri, sır/anahtar yönetimi, yedek kurtarma hedefleri ve prova, saklama/imha kararı, izleme, release/rollback ve eğitim kabulü | Production kapısı açılmaz; yalnız geliştirici ortamında açılması yeterli değildir. |
| Dış sistem dilimi etkinleşmeden önce | Sağlayıcı erişim koşulları, hesap/oturum yöntemi, izinli kanal, test ortamı/örneği, onay ve belirsiz sonuç uzlaştırması | Çekirdek ERP devam eder; gerçek Mikro/sicil otomasyonu açılmaz. |
| Bilinçli sonraki faz | Maliyet/değerleme, dış banka entegrasyonları ve kapsam dışına bırakılmış diğer gelişmiş modüller | İlk teslimi yapay biçimde genişletmez. |

VKN/şirket gibi daha önce verilmiş gerçek bilgiler yeniden sorulmaz. Eksik GIN gibi somut girdi sadece kendi şirket/işlev kapısını bekletir. Sayısal örnekler, demo rol/adetleri, RPO/RTO veya saklama süreleri onaysız gerçek politika olarak sabitlenmez.

## 5. Risklerin çözümü ve kabul kanıtı

| Risk | Tasarım çözümü | Kabulde görmek istediğimiz kanıt |
|---|---|---|
| Eski kuralla yeni kararın çelişmesi | Kaynak/karar farkı ve genişlet–daralt geçişi | Eski geçerli kayıt okunur; yeni zorunluluk doğru değişir; açık politika sessiz kapanmaz. |
| Aynı miktarın iki kez ayrılması / çift tıklama | Transaction + expected version + idempotency | Yarışta yalnız geçerli tek etki; aynı anahtar aynı sonucu, farklı payload conflict verir. |
| Modüller arası kısmi işlem | Sahip modül tek yazıcı, outbox/inbox | Commit sonrası worker çöküşü/retry ikinci stok/para olayı üretmez. |
| Eski önizlemeyle yazma | Sürüm/izin/bağlı gerçeklerin uygulamada denetimi | Preview sonrası değişen kayıt reddedilir veya yeni preview ister. |
| Üst plan düzeltmesi | Beklenen çıktı ile gerçekleşmenin ayrımı | Alt plan etkisi görünür; gerçekleşen stock geçmişi değişmez. |
| Farklı hesap sonuçları | Tek backend calculator | UI/rapor/API/AI aynı golden sonucu gösterir. |
| Kalan kapamanın alacak affı sanılması | Ayrı operasyonel kapanış nedeni/miktarı | F01 işlemi finansal veya fiziksel hareket oluşturmaz. |
| Dosya–metadata ayrışması | Durumlu upload/finalize + immutable sürüm | Yarım upload/retry/restore geçmiş belgeyi bozmaz. |
| Şirket/fiyat sızıntısı | Alan ve nesne yetkisi bütün çıkışlarda | Liste/detay/export/search/log/file/AI için olumsuz yetki testleri. |
| Geçen yılın iadesi | Kaynak geçmişi salt okunur, yeni olay referansı | Kapalı eski belge bulunur; yeni dönem iade yanlış geçmişi değiştirmez. |
| Ödenmiş fatura / başarısız araç | Fiziksel, belge ve para etkilerinin ayrımı | İade ve ters finansal kayıt bir kez oluşur; tahsilat yanlışlıkla silinmez. |
| İşletme sağlığının görünmemesi | DB/file/queue/backup sağlık ve uyarı | Denetimli hata görünür, doğru yetkiliye gider; log sır/fiyat sızdırmaz. |
| Eksik şirket restore | Manifestli DB+dosya+anahtar/audit; güvenli yeniden kurma | Temiz hedefte yalnız seçilen şirket; bozuk/eksik paket reddi; dış tekrar gönderim yok. |
| Yalnız geliştirici cihazında çalışması | Temiz kurulum, release ve forward-fix provası | Ayrı temiz ortamda başlatma, yükseltme, hata ve toparlanma kaydı. |

Bu çözüm yönleri kabul edildi; sayısal eşik, rol ataması, veri silme veya production aktivasyonu bu tabloyla seçilmez.

## 6. Tek teslim envanteri ve ilk adım

[TESLIM_ENVANTERI.json](TESLIM_ENVANTERI.json) mevcut kapsamı paketlere ayırır. [KARAR_ISLEV_TEST_ESLEMESI.json](KARAR_ISLEV_TEST_ESLEMESI.json) her önemli kararı teslim paketi, kabul senaryosu ve gerekli kanıta bağlar. Bunlar yeni yürütme otoritesi değil, mevcut backlog'a uzlaştırılacak tek öneri envanteridir.

Sıra bağımlılığa göre yürür: ortak güvenli temel ve mevcut cari adaylarının uzlaştırılması → ürün/birim/hesaplama → sipariş ve stock → üretim zinciri ve motorlar → sevk/gerçek belge/finans → restore/işletim ve hedef AI/dış sistem dilimleri. AI ve restore sınırları en baştan tasarlanır; bunların tüm kullanıcı yüzeylerinin ilk gün bitmesi beklenmez. İlgili açık kapı beklerken sadece gerçekten bağımsız hazır dilime geçilir.

**İlk somut teknik dilim:** [cari ticari rol zorunluluğunu kaldırma ve mevcut adayları uzlaştırma](ILK_DILIM_CARI_ROL_UZLASTIRMA.md). Mevcut domain/API/SQL/client davranışıyla yeni kullanıcı kararı arasındaki dar farkı ele alır. Bu dilim bütün cari çalışma merkezini veya ilk iş işleminde otomatik şirket hesabını bitirmiş sayılmaz. #227/#228 ile örtüşen sahiplik/head önce koordinatör tarafından çözülür; yeni paralel kopya cari modülü açılmaz.

## 7. Her dilimin teknik paketi

Tablo ve bütün API'leri görmeden bir kerede yüzlerce kesin şema yazmak mevcut kodla çelişme ve tekrar maliyeti doğurur. Gerekli teknik paket **aynı içerik standardıyla, bir dilim önden** hazırlanır. Bu tur tam ERP'nin bütün migration/OpenAPI/test kodlarını ürettiğimiz iddia edilmez; ortak envanter, eşleme ve ilk dilim şartnamesi hazırdır.

| Paket parçası | Somut teslim ve kabul sorusu |
|---|---|
| Karar/scope | Kullanıcının hangi kararı uygulanıyor; açık olan ne, kapsam dışı ne? |
| Tablo/ilişki şeması | Var olan ve önerilen model, CompanyId/anahtar/FK/benzersizlik, sahip modül, indeks/constraint. |
| Durum ve kombinasyonlar | İzinli geçişler ve yasak geçişler, kısmi miktar, iç/dış, ticari/fason, şirket/owner, eski sürüm örnekleri. |
| Komut ve API | Sürümlü request/response, izin, expected version, idempotency, correlation, hatalar; GET yan etkisiz. |
| Olay sözleşmesi | Olay sahibi, commit sınırı, outbox/inbox, retry/dedup, sıra ve uzlaştırma; başka modül tablosuna doğrudan yazma yok. |
| Yetki matrisi | Sayfa/komut/onay/fiyat/dosya/export/AI, şirket sınırı ve maker/checker. |
| Ekran şartnamesi | Alanların Türkçe iş adı, doğrulama, boş/yükleniyor/hata/conflict/yetkisiz durum, filtre/geri dönüş ve erişilebilirlik. |
| Test paketi | Yeni davranış happy-path, olumsuz yetki/CompanyId, concurrency/idempotency, miktar/para golden, gerçek SQL, browser ve hata senaryosu gerektiğinde. |
| Geçiş/işletim | Eski veri okuma, migration/forward-fix, log/metric/trace, release/restore etkisi. |
| Teslim kaydı | Exact HEAD, komut/sonuç/artifact, bağımsız review, taze zorunlu CI, açık bulgu ve sonraki hazır iş. |

Modüler monolit, .NET 10/ASP.NET Core, React/TypeScript, SQL Server/EF Core başlangıcı ve S3-uyumlu dosya soyutlaması korunur. Stack'i bu raporla değiştirmiyoruz.

## 8. AI iş bölümü ve hızlı ama doğrulanabilir yürütme

- **ChatGPT-BUSINESS:** bir dilim önden iş şartnamesi, kabul senaryosu, karar farkı, formül golden ve backlog uzlaştırması. Aynı anda kopuk kod parçaları üretip elle taşıma ana yöntem olmayacak.
- **Codex:** aynı dilimin backend, endpoint, gerçek SQL testleri, OpenAPI/olaylar, deterministik hesaplayıcı tarafı.
- **Claude:** aynı sözleşmenin frontend/React ekranı, generated client tüketimi, bütün UI durumları, browser/erişilebilirlik doğrulaması.
- **CODEX-ORCHESTRATOR:** ayrı koordinatör olarak sahiplik, bağımlılık, exact-head kanıt/review ve mevcut kurallarla entegrasyon/merge. Yazar kendi PR'ını merge etmez.

Ortak kaynak GitHub, yerel worktree'ler bunun çalışma kopyasıdır. Her AI son ilgili kararı/spec'i okumalı; sohbet belleğinin güncel olduğunu varsaymamalıdır. ChatGPT özel depoya erişemiyorsa aynı paket dosyaları yüklenir. ChatGPT'nin ürettiği karar/spec de onaydan sonra depoya kaydedilir; yalnız sohbet içinde kalmaz.

Her görev döngüsü: hazır işi seç → exact base/scope doğrula → sözleşmeyi netleştir → uygula → ilgili test/olumsuz senaryolar → bağımsız review/taze CI → somut sonuç ve checkpoint → sıradaki gerçekten hazır iş. Tekrar test yalnız kod, ortam, hata, dış bağımlılık veya çözülmemiş risk değişince genişletilir. **Zorunlu CI, K3 kanıtları ve bağımsız review tasarruf adına atlanmaz.** Aynı HEAD için değişmeyen büyük analizi baştan üretmek teslim değildir.

K1/K2 uygulama PR'ında bir bağımsız exact-head review; dar K3 kapsamlarında iki bağımsız exact-head review gerekir. Karar/governance değişikliği insan review kapısındadır. Açık iş politikasına rastlanırsa `NEEDS-SINAN` kaydı bir kez verilir; yetkili bağımsız işe geçilir. Sırf hızlı olmak için para/stok/formül/yetki kararı seçilmez.

Tam kopyala–yapıştır promptları [AI çalışma planındadır](AI_CALISMA_PLANI_VE_PROMPTLAR.md). Prompt kesintisiz davranışı tarif eder; kapalı bilgisayar, erişim/oturum kaybı veya kullanım limiti karşısında sınırsız çalışma garantisi vermez. Checkpoint kalıcı olmalı; devam eden oturum son kanıttan ilerlemelidir. Bu tur yeni zamanlanmış görev kurulmamıştır.

OpenAI'nin resmî rehberleri de uygun test kapsamını bitirip, ek değişiklik/hata/çözülmemiş endişe yokken gereksiz tekrar yerine ilerlemeyi destekler. Yerel otomasyon için cihazın açık ve uygulamanın çalışıyor olması gerekir. Bu bilgiler iş bölümü veya şirket politikasının kaynağı değil, çalışma sınırlarının açıklamasıdır. [Doğrulama yaklaşımı](https://developers.openai.com/api/docs/guides/latest-model), [yerel otomasyon koşulları](https://learn.chatgpt.com/docs/automations?surface=app).

## 9. Teslim ve geliştirmeye geçiş kapısı

1. Bu PR'ın yeni kullanıcı kararlarını doğru taşıdığı insan incelemesinde doğrulanır; var olan sicil/spec/backlog farkları sahipleri tarafından uzlaştırılır.
2. Koordinatör #227/#228 ve ilgili aktif işlerin güncel head/owner durumunu okur; dar ilk dilimin tek yazıcısını ve bağımlı tabanını belirler.
3. ChatGPT ilk dilimin iş/kabul farkını, Codex ve Claude sözleşme etkisini tamamlar. Açık politika seçmeden DoR sağlanan bölüm uygulanır.
4. Her dilim gerçek kayıt, ekran ve hata davranışıyla kabul edilir. Test hiç çalışmadıysa “NOT_RUN”, çalıştı ama altyapı nedeniyle tamamlanmadıysa “BLOCKED”, hata varsa “FAILED” yazılır; doküman varlığı “PASSED” değildir.
5. Veri aktarımı hariç temiz kurulum ve temsilî sentetik uçtan uca senaryolar tamamlanır. Gerçek değerler/cihazlar ve production onayı gelmeden sistem ticari kullanıma hazır ilan edilmez.

Paketin yapısal doğrulaması ile ürünün SQL/browser/security/restore kabulü ayrı sonuçlardır. Bu raporun kendisi de karar kapsamı, çelişki, bağımlılık ve test eşlemesi açısından doğrulanacaktır; sonucu README'deki teslim notundan izleyin.
