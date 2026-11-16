# Sinan Kararları — 2026-11-15 (Dalga 2 ek 6: artık etiket mirası, yükleme talimatı iptali, transfer iptal maliyeti, gelecek tedarik ayırması, şirket ortalaması ağırlığı, kg/m tabloları)

Bu belge önceki karar dosyalarını silmez; KLN-D63…KLN-D68, LOJ-D16, LOJ-D17 ve URN-D13…URN-D16 kararlarını ve
ASSUMPTION-STK-REASON-1 varsayımını ekler. KLN-D25 (transfer sevk iptali), KLN-D26…KLN-D32 (gelecek tedarikten
ayırma ve bırakma; özellikle KLN-D28 (4) açık miktar tanımı), KLN-D27 (kantarlı depoda iptal penceresi; okunamayan
olay olmuş sayılır, kapalı olan geçerlidir), KLN-D46 (kullanılabilir artık mirası, SINAN-CEVAP 18), KLN-D54 (profil
kg/m tablosu), KLN-D55 (şirket ortalaması toplama kuralı), LOJ-D15 (yükleme talimatı iptali), URN-D11 (dört kesit
türü), SAT-D347/SAT-D350 (şirket çapında ortalama) ve DEC-DOC-009 (yükleme beyaz listesi ve tarama) aynen geçerlidir.

**Kaynak:** Sinan, 15 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP 27). Sorular
Stok/Lojistik takımının dalga 2 raporundaki NEEDS-SINAN maddelerinden geldi; mevcut kararlarla cevaplı olanlar
sorulmadı (aşağıda "Sorulmadan cevaplı olanlar"). Üç soru penceresi kullanıldı. "(Önerilen)" işaretleri soranındır.
On iki sorunun on birinde önerilen seçenek seçildi; serbest metin cevap aynen alıntılanmıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 27.1 | Kullanılabilir artık etiket (BS-5) bölünür veya birleştirilirse: SC18'deki KLN-D46 kararı mı geçerli, yoksa SC21'in "soruldu, cevap gelmedi" notu mu? | **SC18 kararı geçerli (Önerilen)** | "Bölmede bütün alt etiketler kullanılabilir artık olur. Birleştirmede kaynakların hepsi artıksa sonuç da artık olur, karışıksa üretim durumu boş kalır. Geri alınan STK-S36 kodu yeniden açılır." Seçilmeyenler: "Miras yok" — bölme/birleştirmeden çıkan etiketin üretim durumu her zaman boş başlar; "Yalnız bölmede miras" — birleştirmede sonuç her zaman boş. |
| 27.2 | Yükleme talimatı iptal edilince (LOJ-D15) satış sipariş satırına yapılmış depodaki etiketli mal tahsisi de bırakılsın mı? Sipariş iptal edilmiyor, yalnız talimat iptal ediliyor. | **Tahsis siparişte kalır (Önerilen)** | "Yalnız talimatın sevk edilmemiş miktarı talimat/yükleme limitlerinden düşer (bugünkü sunucu davranışı). Etiketli mal siparişe tahsisli kalır; başka satışa vermek için tahsis ayrıca bırakılır. Sipariş hâlâ aynı malı bekliyor." Seçilmeyenler: "Tahsis de otomatik bırakılsın" — talimattaki sevk edilmemiş miktar kadar fiziksel tahsis de siparişten düşer; "İptalde kullanıcı seçsin" — iptal ekranında "tahsisi de bırak" seçeneği, varsayılan tahsisin kalması. |
| 27.3 | Yükleme talimatı iptalinden sonra araca yüklenmiş malın indirilmesi veya aracın yeniden tartılması nasıl kaydedilsin? (LOJ-D15 (4)) | **İndirme tartısı kaydedilsin (Önerilen)** | "İptalden sonra araç kantara girer, 'indirme tartısı' kaydedilir; yüklü kg ile fark varsa uyarı ve denetim kaydı olur. Tartı yoksa iptal yine geçerlidir ama 'indirme kaydı yok' olarak işaretlenir." Seçilmeyenler: "Yalnız indirme onayı" — tartı yok, depo sorumlusu tek tıkla onaylar; "Kayıt tutulmasın" — indirme sistem dışında kalır. |
| 27.4 | Transfer sevki iptal edilince kaynak depoya dönen ters hareket hangi maliyetle dönsün? (KLN-D25 (2) yazar okuması) | **Sevkin kendi birim maliyeti (Önerilen)** | "Ters hareket, sevk çıkışında kullanılan birim maliyetle kaynak depoya döner; kaynak deponun o anki ortalaması kullanılmaz. Transfer maliyet defterine yazmadığı sürece iptal de yazmaz; yazmaya başlayınca bu kural uygulanır." Seçilmeyen: "Kaynak deponun güncel ortalaması". |
| 27.5 | Bir satış satırına yapılan fiziksel tahsis ile gelecek ayırmanın toplamı satırın açık miktarını (sipariş − sevk edilen) aşabilsin mi? | **Toplam açık miktarı aşamaz (Önerilen)** | "Fiziksel tahsis + gelecek ayırma birlikte satırın açık miktarıyla sınırlıdır; aşan yeni tahsis veya ayırma reddedilir. İki taraf aynı kilit altında kontrol edilir." Seçilmeyenler: "Her biri ayrı sınırlı" — toplamları aşabilir; "Fazlaya uyarıyla izin" — uyarı ve onayla kaydedilir. |
| 27.6 | F-017'de satınalma satırının birimi ile satış satırının birimi farklıysa ayırma nasıl yapılsın? | **kg bazında, ürün kartından (Önerilen)** | "İki taraf da kg'a çevrilir; ton↔kg sabit, adet/metre↔kg ürün kartındaki ağırlık (kg/m, ebat) bilgisiyle. Kartta bilgi yoksa ayırma reddedilir. Çeviri ortak birim hesaplayıcısıyla yapılır." Seçilmeyenler: "Şimdilik yalnız aynı birim"; "Yalnız ton↔kg". |
| 27.7 | Satış satırı ile satınalma satırı aynı ürünün farklı kart revizyonuna bağlıysa ayırma yapılabilsin mi? | **Ürün aynıysa yeter (Önerilen)** | "Ayırma ürün kimliğine bakılır; kart revizyonu farklı olabilir. Mal kabulde etiket ürünün o anki kartıyla oluşur (bugünkü davranış)." Seçilmeyenler: "Sürüm de aynı olmalı"; "Farklıysa uyar". |
| 27.8 | Şirket çapında ortalama maliyette (SAT-D347/D350) ağırlık olarak hangi stok sayılsın? | **Yalnız şirketin kendi malı (Önerilen)** | "Sahibi şirket olan bütün stok sayılır (hangi depoda/kimin elinde olursa olsun, serbest veya bloke). Cariye ait (emanet/fason) mal sayılmaz. Bugünkü uygulama böyle." Seçilmeyenler: "Bloke stok hariç"; "Depodaki bütün stok" (sahibine bakılmaz). |
| 27.9 | Profil kg/m tablosu dosyadan içe aktarılırken yüklenen dosya belge olarak saklansın mı? (B-26) | **Belge kasasında saklansın (Önerilen)** | "Dosya normal yükleme kurallarıyla (beyaz liste, virüs taraması) belge kasasına kaydedilir ve içe aktarım kaydına bağlanır; sonradan 'bu değer nereden geldi' görülebilir." Seçilmeyen: "Saklanmasın" — yalnız okunan satırlar ve kaynak etiketi kalır. |
| 27.10 | Yapısal profil kg/m tablosu hangi kaynaktan alınsın? | **Serbest, kaynak etiketi zorunlu (Önerilen)** | "Sistem standart seçmez; içe aktaran kişi her dosyada kaynağı yazar (örn. 'EN 10365:2017' veya 'X tedarikçi kataloğu 2026'). Bugünkü uygulama." Seçilmeyen: "Tek standart sabitlensin". |
| 27.11 | Profil kg/m tablosunu içe aktarma yetkisi (`product.profile-weights.import`) hangi personalarda olsun? | **Üretim Müd. + Satın Alma Müd. + GM + Admin (Önerilen)** | "Teknik veriyi bilen müdürler ve yönetim. Kişi bazında ayrıca verilebilir. Gerçek grant yine sizde." Seçilmeyenler: "Yalnız GM + Admin"; "Yalnız Admin". |
| 27.12 | Yapısal profil dışındaki uzun ürünlerde (kaynaklı/boyalı kaynaklı profil, boru) kg/m nereden gelsin? | Serbest metin, aynen: **"Tablodan gelir, bunları ben tablo olarak vereceğim yada Web'te araştır"** | Seçilmeyenler: "Ürün kartına elle girilir (Önerilen)" — kartta yazılan değer (kaynak "Kart"); "Tablo varsa tablo, yoksa kart". Seçilen anlam "Bunlar da tablodan gelsin" seçeneğine en yakındır: kartta elle yazılmaz; tabloyu Sinan verir ya da web'den araştırılan taslak Sinan'ın onayına sunulur. |

## Sorulmadan cevaplı olanlar

| Takım sorusu | Cevap veren karar |
|---|---|
| F-017 talep açık miktarı "sipariş − sevk edilen", iptal satır 0 mı? | KLN-D28 (4): açık miktar = satır miktarı − sevk edilen; iptal satırda bütün aktif gelecek ayırmalar bırakılır. |
| Bırakma portu Stok idempotency satırı yazmıyor, çağıranın anahtarına dayanıyor | KLN-D32 uygulama notu: anahtarın sahibi Satış revizyon komutudur; replay Stok portunu ikinci kez çağırmaz. |
| Kaynak deponun kantar ayarı sevkte okunamazsa | KLN-D27 (4): okunamayan olay olmuş sayılır, iptal kapalıdır (bugünkü davranış doğru). |
| Sevkten sonra e-İrsaliye bağlanırsa daha kapalı pencere mi geçerli? | KLN-D27 (7): iki karardan kapalı olan geçerlidir (bugünkü davranış doğru). |
| Şirket ortalamasında bir depoda defter satırı yoksa veya tutmuyorsa | Kapalı başarısızlık kuralı (AGENTS, KLN-D55): cevap "okunamaz" olur; bugünkü davranış korunur. Hangi deponun adlandırılacağı uygulama ayrıntısıdır. |
| KLN-D48 (SC19) ↔ URN-D11 (SC21) kesit türü sayısı | URN-D11 daha yeni kullanıcı kararıdır: dört tür. STK-S37 dört türe göre yeniden yapılır. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KLN-D63 | Kullanılabilir artık mirasının geçerliliği (27.1) | (1) KLN-D46 (SINAN-CEVAP 18) **geçerlidir**; SC21 "Sınırlar" bölümündeki "BS-5 mirası soruldu, cevap gelmedi" notu bu kararla kapanır. (2) Bölmede bütün alt etiketler kullanılabilir artık olur; birleştirmede kaynakların hepsi kullanılabilir artıksa sonuç da öyle olur, karışık kaynakta sonucun üretim durumu boş kalır. (3) Sınıflandırma (Standard/SecondQuality, BS-14, URT-D43) değişmez. | Stok etiket bölme/birleştirme (STK-S36 yeniden açılır) |
| LOJ-D16 | Yükleme talimatı iptalinde fiziksel tahsis (27.2; LOJ-D15 (1)–(2)'yi netleştirir) | (1) Talimat iptali sipariş satırının **fiziksel tahsisini bırakmaz**; tahsis sipariş satırına aittir ve siparişte kalır. (2) İptal yalnız talimatın sevk edilmemiş miktarını talimat ve yükleme limitlerinden düşürür (#1147 sunucu davranışı). (3) LOJ-D15 (2)'deki "diğer satışlara satılabilir" ifadesi, malın başka satışa verilmesi için tahsisin ayrıca (mevcut tahsis bırakma yoluyla) bırakılması anlamında okunur. | Lojistik talimat iptali (STK-S2), Lojistik ekranı |
| LOJ-D17 | Talimat iptalinden sonra indirme tartısı (27.3; LOJ-D15 (4)'ü cevaplar) | (1) İptalden sonra araca yüklenmiş mal indirilirken **indirme tartısı** kaydedilir. (2) Tartıdaki kg ile talimatta yüklü kg arasında fark varsa uyarı gösterilir ve denetim kaydına yazılır; fark iptali engellemez. (3) Tartı kaydı yoksa iptal geçerlidir; talimat "indirme kaydı yok" olarak işaretlenir. (4) İndirme tartısı stok hareketi üretmez; mal zaten depodan çıkmamıştır (sevk kesilmemiş). Fark eşiği, tartı ekranının biçimi ve işaretin adı **uygulama ayrıntısıdır (geri alınabilir)**; fark eşiği yoksa her fark uyarı verir. | Lojistik kantar/tartı, talimat iptal akışı |
| KLN-D64 | Transfer sevk iptalinde ters hareket maliyeti (27.4; KLN-D25 (2) okumasını kilitler) | (1) Ters hareket kaynak depoya **sevk çıkışında kullanılan birim maliyetle** döner; kaynak deponun iptal anındaki ortalaması kullanılmaz. (2) Transferler maliyet defterine yazmadığı sürece iptal de maliyet defterine yazmaz; transfer maliyet yazımı başladığında aynı kural uygulanır. (3) KLN-D25 (2)'deki "golden veya NEEDS-SINAN olmadan etkinleştirilmez" koşulu bu kararla karşılanır; golden örnek ChatGPT şeridince bu kurala göre yazılır. | Stok transfer iptali (STK-S5), maliyet defteri |
| KLN-D65 | Fiziksel tahsis + gelecek ayırma toplam sınırı (27.5; spec §9.1 aday 3'ü ve KLN-D31 (4)'teki açık noktayı cevaplar) | (1) Bir satış satırındaki **fiziksel tahsis + aktif gelecek ayırma toplamı**, satırın açık miktarını (KLN-D28 (4)) aşamaz. (2) Aşan yeni fiziksel tahsis veya gelecek ayırma reddedilir; mevcut kayıtlar değişmez. (3) Kontrol iki tarafı aynı satış satırı kilidi altında okur; yarışta toplam sınır korunur (gerçek SQL yarış testi). (4) Birim farklıysa karşılaştırma KLN-D66'daki kg tabanında yapılır. | Stok fiziksel tahsis ve F-017 oluşturma yolları |
| KLN-D66 | F-017'de birim çevirisi (27.6) | (1) Satış ve satınalma satırının birimleri farklıysa iki taraf **kg'a** çevrilir. (2) Ton↔kg sabit çevridir; adet/metre↔kg ürün kartındaki ağırlık bilgisiyle (kg/m, ebat; URN-D11, URN-D16) yapılır. (3) Kartta gerekli bilgi yoksa veya okunamıyorsa ayırma **reddedilir** (fail-closed). (4) Çeviri ortak deterministic birim hesaplayıcısıyla yapılır; ekran veya rapor çeviriyi yeniden yazmaz. Yuvarlama ve ret kodu **uygulama ayrıntısıdır (geri alınabilir)**. | Stok F-017 ayırma, ürün birim hesaplayıcısı |
| KLN-D67 | F-017'de ürün kart revizyonu (27.7) | Ayırma **ürün kimliğine** bakar; satış ve satınalma satırının kart revizyonu farklı olabilir. Mal kabulde etiket ürünün o anki kartıyla oluşur. | Stok F-017 ayırma |
| KLN-D68 | Şirket çapında ortalamada ağırlık (27.8; KLN-D55'i tamamlar) | (1) Ağırlığa **yalnız sahibi şirket olan stok** girer; depo veya emanetçi fark etmez; serbest ve bloke stok sayılır. (2) Cariye ait (emanet/fason) stok ağırlığa girmez. (3) Toplama kuralı ve okunamaz durum KLN-D55'teki gibidir. | Stok şirket ortalaması (STK-S35), Satış maliyet okuması |
| URN-D13 | Profil kg/m içe aktarım dosyasının saklanması (27.9; B-26) | (1) İçe aktarılan dosya **belge kasasında saklanır** ve içe aktarım kaydına bağlanır. (2) Dosya kullanıcı yüklemesidir: DEC-DOC-009 beyaz liste, MIME doğrulama ve malware taraması uygulanır; tarama servisi yoksa içe aktarım fail-closed reddedilir (KLN-D60 (3)). (3) Okunan satırlar ve kaynak etiketi ayrıca saklanır. | Ürün kg/m içe aktarımı (STK-S37), Platform belge kasası |
| URN-D14 | kg/m tablosunun kaynağı (27.10) | Sistem standart veya sürüm seçmez; içe aktaran kişi her dosyada **kaynak etiketini zorunlu** yazar (örn. standart ve sürümü ya da tedarikçi kataloğu ve yılı). | Ürün kg/m içe aktarımı |
| URN-D15 | `product.profile-weights.import` persona dağılımı (27.11) | (1) İzin şirket çapındadır, Critical değildir. (2) Persona dağılımı: **Üretim Müdürü, Satın Alma Müdürü, Genel Müdür, Admin**. Kişi bazında grant mümkündür. (3) Gerçek grant insan kapısındadır. | Ürün izin kaydı, persona grant listesi |
| URN-D16 | Yapısal profil dışındaki uzun ürünlerde kg/m (27.12) | (1) Kaynaklı profil, boyalı kaynaklı profil ve boruda kg/m **tablodan gelir**; ürün kartına elle yazılmaz. (2) Tabloyu Sinan verir veya web'den araştırılan taslak tablo Sinan'ın onayına sunulur; onaysız taslak içe aktarılmaz. (3) Tablo URN-D13/URN-D14 kurallarıyla içe aktarılır. (4) Tabloda karşılığı olmayan üründe kg/m yoktur: Profil türünde kg/m zorunlu olduğu için (URN-D11) kart kaydı reddedilir; Yuvarlak boruda kg/m isteğe bağlı kalır, kg gerektiren işlem (örn. KLN-D66 çevirisi) reddedilir. | Ürün kesit/kg/m alanları (STK-S37), kg/m içe aktarımı |

## Varsayım (geri alınabilir)

| No | Konu | Varsayım |
|---|---|---|
| ASSUMPTION-STK-REASON-1 | F-017 bırakma gerekçe kodları | Gerekçe kodu şimdilik yalnız biçimle doğrulanır (sabit katalog yok). Katalog gerekirse Ayarlar'da ayrı dilimle eklenir. Para/stok/yetki etkisi yoktur; Sinan veya bir tüketici itiraz ederse değiştirilir. |

## Uygulama notları (karar değildir)

- STK-S14 teknik maddeleri ev kurallarıyla kapanır, karar gerektirmez: 422 kural reddi de terminal ret olarak
  idempotent deneme kaydına yazılır (`k3-command-skeleton` terminal ret kuralı); denetim komut kaydını kapsar,
  türetilmiş defter satırı (`FutureSupplyLines`) ayrıca audit gerektirmez; satış satırı kilidinin host'tan Satış
  modülüne taşınması mimari ayrıntıdır ve ayrı dilimde değerlendirilir.
- STK-S5: transfer iptal hareketinin (tür 11) etiket durum özetinde diğer giriş türleri gibi sayılması uygulama
  ayrıntısıdır; mevcut davranış korunur.
- STK-S35: Alış sipariş satırı fiyat/para birimi/birim portu (SAT-D295) olmadığı için ortalama defter yazıcısı
  bağlanmadı; port Alış/Cari takımının işidir, karar değildir.

## Sınırlar

- KLN-D65: fiziksel tahsis ekranının bu reddi nasıl göstereceği ve ret kodu uygulama ayrıntısıdır.
- LOJ-D17: kantar donanımı, tartı entegrasyonu ve fark eşiği değeri bu kararla seçilmedi; eşik gerekirse Ayarlar'dan
  değişir ve ayrı Sinan kararıdır.
- URN-D16: web'den araştırılan taslak tablo kilitli veri değildir; Sinan onaylamadan içe aktarılmaz.
- Bu belge yeni rol açmaz ve hiçbir kişiye izin vermez; Critical sınıfı açmaz. Persona dağılımı yukarıda yazıldığı
  gibidir; gerçek grant insan kapısındadır.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
