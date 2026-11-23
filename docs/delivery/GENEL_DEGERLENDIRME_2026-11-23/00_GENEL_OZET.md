# Göymen ERP genel değerlendirme — özet ve yol haritası

> Kapsam: `main` `ddc6cba` (23 Kasım 2026). Bu belge durum tespiti ve öneridir; karar, onay,
> `OPEN-*` kapatma ya da görev ataması değildir. Hangi işin yapılacağını Sinan seçer.
> Ayrıntı modül ve zincir belgelerindedir (§9'daki dizin).

## 1. Sonuç

Kod tabanı büyük ve sağlam: 17 arka uç modülü, 829 HTTP işlemi, yaklaşık 340 veritabanı göçü.
Derleniyor, testleri geçiyor, gerçek SQL Server üzerinde ayağa kalkıyor ve ekranları açılıyor.
Ama **bugün hiçbir kullanıcı satış, satın alma, stok, üretim, lojistik, finans ya da muhasebe
işlemi yapamaz.** Engel kod eksikliği değil, kodun önündeki kapılardır:

1. **İş rolleri yok.** 287 iznin yalnız 14'ü bir rolde. Rol oluşturan ya da role izin ekleyen
   ekran ve uç da yok; roller yalnız tohum dosyasından geliyor.
2. **Ürün modülü çalışma zamanında kapalı.** Ayarı verilmeden Satış, Satın Alma ve Stok ürün
   seçemiyor.
3. **Modüller arası olay taşıyıcısı kapalı.** Fatura → muhasebe fişi, maliyet güncellemesi ve
   bildirimler akmıyor.
4. **Yazılmış ekranların bir bölümü yayımlanmamış** (alış faturası, sözleşme, krediler, resmî
   fatura kaydı, hesap planı ve diğerleri).
5. **Kurulum verisi ve açılış kayıtları için toplu yol yok** (cari, ürün, açılış stoku, açılış
   bakiyesi tek tek girilir).
6. **GSP ve GIN'de finans para profili yok**; tahsilat, ödeme ve virman yalnız GDC'de ve
   yalnız TL çalışır.
7. **Kur pratikte çalışmıyor** (TCMB kapalı, elle kur girişi yok).

Bu yedi kapı açılmadan üç firmanın test ortamında kullanıma başlaması mümkün değil. Açıldıktan
sonra satış ve satın alma zincirlerinin büyük bölümü yürür; kalan kopukluklar §5'te.

## 2. Ölçtüklerim

Analiz yalnız kod okumaya dayanmıyor; projeyi kurup çalıştırdım.

| Ölçüm | Sonuç |
|---|---|
| Arka uç derlemesi (Release) | 0 hata, 0 uyarı |
| Arka uç testleri (yerel, SQL'siz) | 21 test projesinde 14.534 testten 14.528'i geçti. Kalan 6'sı bu ortama özgü (4'ü PowerShell ister, 2'si `root` kullanıcı adına takılır). API entegrasyon test projesi yerelde süre sınırına takıldı ve tamamlanmadı; onun kanıtı aşağıdaki CI koşusudur |
| Ön yüz | lint ve derleme temiz; 640 test dosyası, 10.421 test, hepsi geçti |
| CI, `main` `9e6107f` (koşu 45164205956) | 22 işin 22'si başarılı; gerçek SQL Server kabul testleri (üç parça, her biri yaklaşık 2 saat) dahil. `ddc6cba` bunun üstüne yalnız 5 satır belge ekliyor |
| Çalıştırma | API gerçek SQL Server 2022'de açıldı, bütün göçler uygulandı, `/health/ready` sağlıklı |
| En yetkili hazır kullanıcının erişimi | 208 liste ucundan 15'i açık, 161'i "yetkiniz yok" |
| Aynı kullanıcı, yerel deneme rolüyle (bütün izinler) | 226 ekran yeteneği; 208 uçtan 146'sı açık, 15'i "hizmet kullanılamıyor" |
| Menü yürüyüşü (tarayıcı, deneme rolü) | 78 ekranın 72'si hatasız açıldı; 5'i 503, 1'i geliştirme girişine özgü 401 |

İki düzeltme:

- `docs/handoffs/TREN1_DEVIR/durum/W8_ANLIK_KAYNAK_DURUMU.md` "test, SQL ve CI kabulü
  koşulmadı" diyor. Bu eskimiş: aynı kodun CI'ı `main` üzerinde yeşil.
- Deneme rolü yalnız benim yerel, atılacak veritabanımda ve derleme çıktısındaki tohum
  kopyasında var. Depoda hiçbir kaynak dosyası değişmedi, kimseye yetki verilmedi.

Ölçmediğim: yazma işlemlerini (sipariş aç, onayla, sevk et, faturala) ekrandan baştan sona
yürütmedim. Ekranların açılması, akışın çalıştığını kanıtlamaz; bu §7'deki ilk işlerden biri.

Küçük bir kusur: zorunlu sorgu parametresi eksik gelen 16 uç "geçersiz istek" (400) yerine
"beklenmeyen hata" (500) döndürüyor. Kullanıcı bunu ekrandan görmez, ama düzeltilmeli.

## 3. Ölçüm merdiveni

| Seviye | Anlamı |
|---|---|
| 0 | Yok |
| 1 | Kod yazılmış |
| 2 | Doğrulanmış (testler ve CI yeşil) |
| 3 | Kullanıcı ekrandan baştan sona yapabiliyor |
| 4 | Canlıya hazır |

Bütün yazılmış kod bugün **2. seviyede**. **3. seviyede olan tek alan** hazır rollerle
erişilebilen küçük kısımdır: organizasyon okuma, cari listesi ve yeni cari, denetim kaydı.

## 4. Modül durumu

"Kod" sütunu, hedefin (master rapor ve kararlar) ne kadarının yazıldığına dair yaklaşık
değerlendirmedir; ölçüm değildir.

| Modül | Kod | Bugün kullanılabilir mi | En önemli eksikler | Belge |
|---|---|---|---|---|
| Kimlik, organizasyon, denetim | Geniş | Kısmen (yalnız yönetici, organizasyon ve cari) | Rol tanımlama ekranı yok; yöneticide kullanıcı açma izni yok; kurulum yolu denenmemiş | 01 |
| Ayarlar | Geniş (28 ekran) | Hayır (39 izin rolsüz) | Kur çalışmıyor; 5 politikanın ekranı yok; 7 politikanın tüketicisi yok | 02 |
| Ürün ve hizmet | Geniş | Hayır (çalışma zamanı kapalı) | Katalog tek grup, tek kalite; boru/profil ve adetle satış kesiliyor; aktif kart düzeltilemiyor | 03 |
| Cari | Orta | Kısmen (liste, yeni kart) | Şirkete bağlama yayımlanmamış; IBAN, vade, vergi dairesi yok; toplu aktarım yok | 04 |
| Satış | En geniş | Hayır | Sözleşme ekranı bağlı değil; sipariş kapatma ve resmî iptal çalışmıyor; ikinci sevkte risk "hesaplanamıyor" | 05 |
| Satın alma | Geniş | Hayır | Fatura ekranları kapalı; sipariş formu tek satır ve talepten kopuk; ithalat ve işletme alımları yok | 06 |
| Stok ve tahsis | En çok kod | Hayır | Toplu açılış yok; blok kaldırılamıyor; şirketler arası transfer yok | 07 |
| Üretim | Geniş | Hayır | Plan ekranı motorların istediği değerleri sormuyor; okunur numara yok; operatör ekranı yok | 08 |
| Lojistik ve kantar | Orta | Hayır | Plaka, şoför, nakliyeci yok; ekranlar kimlik kodu gösteriyor; sevk iptali yok | 09 |
| Finans ve hazine | Geniş | Hayır | Banka mutabakatı, nakit tahsilat, verilen çek, kur farkı, yaşlandırma yok; kasa ekranı yok | 10 |
| Muhasebe | Orta | Hayır | Mikro'ya aktarım yok; 16 menü girişinin 12'si yayımlanmamış; fişte hesap adı görünmüyor | 11 |
| Çalışma merkezi | Dar | Kısmen (bildirim) | 63 onay işleminden 4'ü ortak kutuda; görev, vekâlet yok | 12 |
| Belgeler | Orta | Hayır (depolama ayarı boş) | Merkezi arşiv, OCR yok; PDF yalnız 3 belge | 12 |
| Raporlar ve ana sayfa | Çok dar | — | Rapor merkezi yok; ana sayfa boş; genel dışa aktarım yok | 12 |
| AI | İskelet | Hayır (kapalı) | Sağlayıcı sahte, araç 0 | 12 |

Hiç yazılmamış hedef alanlar: işletme alımları, hizmet kataloğu, ithalat, kalite ve karantina,
banka mutabakatı, nakit planı, bütçe, şirketler arası işlemler, rapor merkezi, StokERP veri
geçiş araçları.

## 5. Uçtan uca zincirler

| Zincir | Durum | İlk kopuş | Belge |
|---|---|---|---|
| Siparişten nakde | 15 halkanın 10'u gerçek, 4'ü kısmi, 1'i yok | İkinci sevkte risk hesaplanamıyor; fatura muhasebeye ulaşmıyor; banka mutabakatı yok | 20 |
| Satın almadan ödemeye | 14 halkanın 5'i gerçek, 6'sı kısmi, 3'ü yok | Sipariş talebe bağlanmıyor; fatura ekranı kapalı; ödeme önerisi ve banka talimatı yok | 21 |
| Hammaddeden ürüne | Çekirdek baştan sona var | Çıktı siparişe kendiliğinden ayrılmıyor; "bloklu" çıktı açılamıyor | 22 |
| Belgeden kayda | Yükleme ve bağlama var | OCR, e-posta, AI taslağı yok | 22 |
| Sicilden cariye | Yalnız elle tarihçeli değişiklik | Kaynak tarama ve eşleştirme yok | 22 |

Modüller arası bağların iyi tarafı:

- "Bir modül başka modülün tablosuna yazmaz" kuralının ihlali bulunmadı.
- Stok hareketi üreten her yol (mal kabul, üretim, sevk, iade) kaynak belgeyle aynı veritabanı
  işleminde ve tek kez yazıyor; aynı olayın ikinci kez hareket üretmesi veritabanı kısıtıyla
  engelli.
- Fatura → Finans açık kalemi doğrudan ve tekrar güvenli; olay taşıyıcısına bağlı değil.

Zayıf tarafı:

- **Olay taşıyıcısı kapalı.** 7 kaynak ve 13 tüketici kayıtlı, hiçbir olay ulaşmıyor. 13 olay
  türü yazılıp hiç yayımlanmıyor.
- **Çift alacak riski.** Açık kalem hem satış faturası kesinleşince hem Finans'ın CSV
  aktarımıyla doğabiliyor; ikisi arasında fatura numarası karşılaştırması ertelenmiş.
- **Tanımlı ama okunmayan ayarlar:** finans onay matrisi, görev ayrılığı, kasa sayım farkı,
  satış belge numara serileri.
- **Ayar bekleyip bulamayan modüller:** mal kabul kg toleransı, kantar fark bantları, benzer
  ürün toleransı. Bunlar boş kaldığı için her fark onaya düşüyor.
- **Şirketler arası hiçbir temsil yok.** GDC, GSP ve GIN arasında mal, fatura ya da borç
  hareketi sistemde ifade edilemiyor. Üç firma aynı anda kullanacaksa bu erken karar ister.
- **Tedarikçi iadesi ve alış faturası iptali** stoku düzeltiyor, borcu ve muhasebeyi
  düzeltmiyor.

## 6. Bir ERP'de olması gereken, hedefte de eksik kalanlar

Master rapor ve kararlarda zayıf geçen ya da ertelenmiş, ama çelik ticareti ve üretimi yapan
üç şirketli bir grup için gerekli olanlar:

- İthalat: akreditif, gümrük beyannamesi, konşimento, antrepo, ithalat maliyetinin stoka
  yansıması.
- Kalite: karantina ve serbest bırakma, uygunsuzluk, üretici test sertifikası (MTC) bağı.
- Türkiye uyumu: BA/BS, KDV ve tevkifat hazırlık raporu, kur değerleme, mutabakat mektubu,
  e-Fatura/e-İrsaliye bağlantısı (ilk fazda dışarıda kalması karardır; mutabakat yolu yine
  gerekir).
- Nakliye: araç, şoför, nakliyeci, navlun ve navlunun maliyete etkisi.
- Depo: raf/göz, toplu sayım, el terminali.
- Planlama: malzeme ihtiyaç planı, asgari stok, kapasite çizelgesi.
- Yönetim görünümü: yaşlandırma, kârlılık, stok değeri, nakit durumu.

## 7. Önerilen sıra

Sıra bağımlılığa göredir. "İnsan kapısı" Sinan'ın kararını ya da işlemini ister.

### Aşama A — kullanıma açma (üç firmanın test ortamına girmesi için şart)

| # | İş | Not |
|---|---|---|
| A1 | İş rollerinin tanımı ve izin eşlemesi | İnsan kapısı. Mevcut öneri: `SATIS_ROL_YETKI_ONERISI_v1.md`; bütün modüllere genişletilmeli. İki kişi kuralı için şirket başına en az iki yetkili |
| A2 | Rol tanımlama ve role izin atama ekranı | Yoksa her rol değişikliği kod değişikliği olur |
| A3 | Test ortamı kurulum yolu: şema, üç şirket, roller, ilk yönetici, yerel hesapla giriş | Bir kez baştan sona denenmeli |
| A4 | Ürün çalışma zamanını açmak; ürün kartı yetkisine yönetim yolu | İnsan kapısı |
| A5 | Olay taşıyıcısını ve muhasebe kabul anahtarını test ortamında açmak | İnsan kapısı; 5 ayar değeri seçilmeli |
| A6 | GSP ve GIN için finans para profilleri; USD/EUR profili | Karar gerekir (SAT-D18) |
| A7 | Kur: günlük otomatik alma, yarım gün kuralı, TCMB kapalıyken elle giriş | Kısmen karar gerekir |
| A8 | Yazılmış ama kapalı ekranları yayımlamak | Alış faturası, cari şirkete bağlama, krediler, resmî fatura, hesap planı, fason teslim, yeniden açma |
| A9 | Kurulum durumu ekranı ve üç şirket için kurulum verisi | Her modül belgesinde kontrol listesi var |
| A10 | Belge depolama ve virüs tarayıcı kurulumu | API Linux imajı, tarayıcı Windows Defender: karar gerekir |

### Aşama B — açılış verisi ve zinciri yürütme

| # | İş |
|---|---|
| B1 | Toplu aktarım: cari, ürün, açılış stoku (adet ve müşteri malı dahil), cari açılış bakiyesi (açık kalem doğuracak biçimde), portföydeki çek/senet |
| B2 | Katalog yönetimi: kod grubu, aile, kalite, kaplama; boru/profil; adetle satış |
| B3 | Üç şirkette uçtan uca senaryo: siparişten nakde, satın almadan ödemeye, hammaddeden ürüne. Gerçek kullanıcı rolleriyle, tarayıcıdan |
| B4 | Zincir kopukluklarını kapatmak: risk okuması, sipariş kapatma, resmî iptal, alış siparişinin talebe bağlanması ve çok satır, çıktıdaki blok, plan ekranı alanları, kg toleransları |
| B5 | Ekranlarda kimlik kodu yerine okunur ad ve numara (lojistik, üretim, muhasebe) |
| B6 | Çift alacak riskini kapatmak |
| B7 | Menü sadeleştirme: 107 girişten yaklaşık 36'ya; aynı işin kayıt türleri tek sayfada sekme olur, onay sayfaları çalışma alanına taşınır (`30_MENU_SADELESTIRME_VE_DEVAM_PLANI.md`) |

### Aşama C — günlük kullanım için gerekenler

Ortak onay kutusu, ana sayfa, eksik PDF'ler (teklif, fatura, alış siparişi, ekstre), genel Excel
dışa aktarımı, kasa ekranı, nakit tahsilat, verilen çek/senet, kur farkı, yaşlandırma, banka
ekstresi ve mutabakat, tedarikçi iadesi ve fatura iptalinin finans etkisi, cari kartında IBAN,
vade ve vergi dairesi, nakliye ana verisi.

### Aşama D — hedefin kalanı

Şirketler arası işlemler, ithalat, kalite, Mikro'ya aktarım ve mutabakat, raporlar, nakit planı
ve bütçe, AI, malzeme ihtiyaç planı, StokERP geçişi.

## 8. Senden beklenen kararlar

1. **İş rolleri.** Hangi kişi hangi şirkette hangi işi yapacak; rol–izin tablosunun onayı.
2. **Muhasebenin pilot rolü.** Kapalı mı kalacak, ERP iç defter mi tutacak, Mikro'ya aktarım mı
   yapılacak. Bu seçim Aşama A'daki muhasebe işlerinin yarısını belirliyor.
3. **Şirketler arası işlemler.** GDC, GSP ve GIN birbirine mal satıyor, devrediyor ya da
   borçlanıyorsa asgari temsil ne olmalı. 22 Kasım kararıyla ertelenmiş; üç firma aynı anda
   başlayacağı için yeniden bakmak gerekir.
4. **GSP ve GIN finans profili ve dövizli işlemler.**
5. **Test ortamında açılacak anahtarlar:** ürün çalışma zamanı, olay taşıyıcısı, muhasebe
   kabulü, TCMB kuru.
6. **Açılış verisinin kaynağı.** StokERP'den mi, Excel'den mi; hangi tarih itibarıyla.
7. **Belge taraması.** Sunucu Linux ise hangi tarayıcı.
8. **GIN.** Bugün yer tutucu: vergi numarası yok, fatura kesemez. Gerçek bilgileri gerekli.

## 9. Belge dizini

| Dosya | Konu |
|---|---|
| `01_PLATFORM_KIMLIK_ORGANIZASYON.md` | Kimlik, yetki, şirketler, denetim, menü |
| `02_AYARLAR_ANA_VERILER.md` | Politikalar, kur, kurulum sırası |
| `03_URUN_HIZMET.md` | Ürün kartı, katalog, fiyat listeleri |
| `04_CARI.md` | Cari kart, Cari 360 |
| `05_SATIS.md` | Talep, teklif, sipariş, fatura |
| `06_SATIN_ALMA.md` | Talep, sipariş, mal kabul, fatura, işletme alımları |
| `07_STOK_TAHSIS.md` | Stok defteri, etiket, tahsis, transfer, maliyet |
| `08_URETIM.md` | Motorlar, plan, emir, maliyet |
| `09_LOJISTIK_KANTAR.md` | Yükleme, tartım, irsaliye |
| `10_FINANS_HAZINE.md` | Tahsilat, ödeme, açık kalem, çek, kredi |
| `11_MUHASEBE.md` | Muhasebenin rolü, fiş, dönem, mizan |
| `12_CALISMA_MERKEZI_BELGELER_RAPORLAR_AI.md` | Ana sayfa, onay kutusu, belgeler, raporlar, arama, AI |
| `20_ZINCIR_SIPARISTEN_NAKDE.md` | Satış zinciri halka halka |
| `21_ZINCIR_SATINALMADAN_ODEMEYE.md` | Satın alma zinciri halka halka |
| `22_ZINCIR_URETIM_BELGE_SICIL_VE_BAGLANTI_MATRISI.md` | Üretim zinciri, belge, sicil, modüller arası bağımlılık matrisi |
| `30_MENU_SADELESTIRME_VE_DEVAM_PLANI.md` | Menü sadeleştirme önerisi ve devam planı |

## 10. Bu değerlendirmenin sınırları

- Modül ve zincir belgeleri kod okumasına dayanır ve ayrı analiz oturumlarında hazırlandı.
  En kritik iddiaları (izinlerin rolsüz olması, ürün çalışma zamanı, olay taşıyıcısı, kapalı
  ekran bayrakları, risk portu, sipariş kapatma) kodda ve çalışan sistemde ayrıca doğruladım.
  Kalan ayrıntılar tek tek ikinci kez doğrulanmadı; her belgenin sonunda "doğrulanamayanlar"
  bölümü var.
- Yazma akışları ekrandan baştan sona yürütülmedi (§2).
- Performans, yük, yedekten dönüş ve güvenlik sızma testi bu değerlendirmenin dışındadır.
- İş büyüklükleri tahmindir.
