# Menü sadeleştirme ve devam planı

> Kapsam: `main` `ddc6cba`, 23 Kasım 2026. Bu belge öneridir; karar, onay ya da görev ataması
> değildir. Menü biçimini ve iş sırasını Sinan seçer. `00_GENEL_OZET.md` ile birlikte okunur.

## 1. Kısa cevaplar

**Analiz bütün modülleri kapsıyor mu?** Evet. On yedi arka uç modülünün ve menüdeki on üç başlığın
hepsi bir belgede incelendi (§2). Ayrıntılı incelenmeyen dört konu var: StokERP'den veri geçişi,
performans, yedekten dönüş ve güvenlik testi.

**Satış menüsü sadeleşebilir mi?** Evet, ve hedef belgeler zaten bunu istiyor. Master rapor satışta
dört ana alan tanımlıyor; bugün menüde on giriş var.

**Talep, teklif ve sipariş durum filtresiyle tek listede toplanabilir mi?** Kısmen.

- Üç ekran aynı yapı taşlarını kullanıyor: ortak tablo, durum bloğu, durum göstergesi ve sayfalama.
  Birleştirmek ucuz.
- Ama bunlar **üç ayrı kayıt**: talep teklife, teklif siparişe dönüşür. Her birinin kendi alanları,
  işlemleri, yetkisi ve sunucu ucu var. Tek tabloda durum filtresiyle karıştırmak hem sütunları hem
  düğmeleri bulandırır, ayrıca sunucuda yeni bir birleşik liste gerektirir.
- Önerim: **tek sayfa, üstte sekmeler** (Talepler · Teklifler · Siparişler) ve **her sekmede durum
  filtresi**. Talep ve teklif listelerinde durum filtresi sunucuda zaten var. Siparişte yalnız
  onay durumu filtrelenebiliyor; "açık / sevk ediliyor / kapandı" filtresi için arka uca parametre
  eklemek gerekiyor (§3.7).

**Ne yapmamız lazım?** §4'teki sırayla: önce senin üç kararın, onlarla paralel olarak karar
beklemeyen geliştirme işleri, sonra test ortamı kurulumu ve üç firmada deneme.

## 2. Analizin kapsamı

| Menü başlığı (KLN-D1) | Arka uç modülü | Belge |
|---|---|---|
| Ana Sayfa | Kabuk | 12 |
| Çalışma Merkezi | Work | 12 |
| Satış | Sales | 05, zincir 20 |
| Satın Alma (işletme alımları dahil) | Purchasing | 06, zincir 21 |
| Üretim | Manufacturing | 08, zincir 22 |
| Stok & Lojistik | Inventory, Logistics | 07, 09 |
| Ürün & Hizmet | Product | 03 |
| Cari | Counterparty | 04 |
| Finans & Hazine | Finance | 10 |
| Muhasebe | Accounting | 11 |
| Belgeler | Documents, DocumentGeneration | 12 |
| Raporlar | (modül yok) | 12 |
| Ayarlar | Settings, Organization, Identity, Audit, SecurityEvents | 01, 02 |
| AI (karar bekliyor) | Api/Ai | 12 |
| Platform altyapısı | Idempotency, BuildingBlocks | 01 |

Modüller arası bağımlılık matrisi ve olay listesi `22_...` belgesinin 5. bölümünde.

## 3. Menü sadeleştirme

### 3.1 Bugün

Menüde 107 giriş var:

| Grup | Giriş | Bunların içinde onay sayfası |
|---|---:|---:|
| Satış | 10 | 0 (onaylar sipariş listesinden bağlantıyla) |
| Satın Alma | 4 | 0 |
| Üretim | 1 | 0 |
| Stok & Lojistik | 12 | 3 |
| Ürün & Hizmet | 2 | 0 |
| Cari | 3 | 0 |
| Finans & Hazine | 20 | 4 |
| Muhasebe | 16 | 5 |
| Ayarlar | 39 | 0 |

Kalabalığın üç nedeni var:

1. **Her kayıt türü ayrı giriş:** talep, teklif ve sipariş; sevk, faturaya hazır ve fatura.
2. **"Hazırla" ve "onayla" ayrı giriş:** örneğin "Sevk SMM hazırlama" ve "Sevk SMM onayı". On iki
   giriş yalnız onay listesi.
3. **Ayarlar'daki her politika ayrı giriş.** Otuz dokuz satır, iki alt başlık altında.

### 3.2 Hedef belgeler ne diyor

- **Master §7.2, satış:** dört ana alan: talep/teklif havuzu, siparişler, İrsaliye & Fatura,
  raporlar. "İade/Düzeltme bir yaşam döngüsü görünümüdür; ayrı sol menü değildir."
- **Master §5.1, ortak şablon:** her sayfada "durum veya iş evresine göre az sayıda sekme".
- **Master §4:** fiyatlar Ürün & Hizmet altında (Ürünler; Hizmetler; Fiyatlar). Bugün fiyat
  listeleri Satış menüsünde.
- **Master §4 ve KLN-D1:** onaylar Çalışma Merkezi'nde toplanır. "Menüde ekran sayısı değil, iş
  sorumluluğu gösterilir."

Yani sadeleştirme yeni bir karar değil; bugünkü menü hedeften sapmış durumda.

### 3.3 Satış ekranlarının teknik durumu

| Ekran | Sunucu ucu | Sunucudaki filtre | Ortak yapı taşları |
|---|---|---|---|
| Satış talepleri | `/sales/requests` | durum | tablo, durum bloğu, gösterge, sayfalama |
| Satış teklifleri | `/sales/quotes` | durum | aynı |
| Satış siparişleri | `/sales/drafts` | yalnız onay durumu | aynı |
| Sevkiyat / İrsaliyeler | `/sales/dispatches` | durum | aynı |
| Faturaya hazır | `/sales/invoice-candidates` | yalnız cari hesap | aynı |
| Satış faturaları | `/sales/invoices` | yok | aynı |
| Müşteri iadeleri | `/sales/customer-returns` | yalnız fatura | aynı |
| Resmî fatura iptal kaydı | (form sayfası) | — | durum bloğu |

Sonuç: ekranlar aynı yapı taşlarından kurulu, ama her biri ayrı kayıt türü ve ayrı uç. En doğru
birleştirme **sekme** düzeyinde yapılır: her sekme kendi ucunu ve kendi yetkisini korur, yalnız
aynı sayfada durur. Arka uç ve yetki değişmez.

### 3.4 Önerilen satış menüsü

```
Bugün (10 giriş)                    Öneri (4 giriş)
─────────────────                    ─────────────────────────────────────────
Satış talepleri                      Talep & Teklif
Satış teklifleri                       sekmeler: Talepler · Teklifler
Satış Siparişleri                    Siparişler
Sevkiyat / İrsaliyeler                 sekmeler: Tümü · Onayımı bekleyen · Açık · Kapanmış
Faturaya Hazır                       İrsaliye & Fatura
Satış faturaları                       sekmeler: Kontrol merkezi · Sevkiyat/İrsaliye ·
Resmî fatura iptal kaydı                         Faturaya hazır · Faturalar · İadeler
Müşteri iadeleri                     Raporlar
Satış raporları
Fiyat listeleri  ──────────────────► Ürün & Hizmet › Fiyatlar
```

Ayrıntılar:

- **Resmî fatura iptali** fatura detayında bir işlem olur. İptal kayıtları "Faturalar" sekmesinde
  durum filtresiyle görünür.
- **Müşteri iadeleri** "İrsaliye & Fatura"da bir sekme olur, ayrıca sipariş ve fatura detayında
  görünür (master: iade bir yaşam döngüsü görünümü).
- **Satış kontrol merkezi** bugün yazılmış ama menüde yok. "İrsaliye & Fatura"nın ilk sekmesi olur
  (master §7.2'de bu alanın parçası).
- **"Siparişler"de Açık / Kapanmış sekmeleri** için arka uca sipariş durumu filtresi eklenmeli.
  O gelene kadar sekmeler "Tümü" ve "Onayımı bekleyen" ile başlar.
- **Siparişe dönüşüm akışı korunur.** Talepten teklif, tekliften sipariş düğmeleri aynı kalır;
  kullanıcı yalnız sekme değiştirir.

Sayfa görünümü (Talep & Teklif):

```
Talep & Teklif                                              [ Yeni talep ]
┌ Talepler (12) ┬ Teklifler (5) ┐
│ Durum: [ Açık ▾ ]   Cari: [        ]   Tarih: [    ]–[    ]
│ ─────────────────────────────────────────────────────────────
│ No      Cari            Ürün özeti        Termin     Durum
│ ...
```

Sekmedeki sayılar ve cari/tarih filtreleri arka uç desteği ister (§3.7). İlk adımda durum filtresi
yeterli.

### 3.5 Diğer gruplar için aynı ilke

| Grup | Bugün | Öneri | Hedef giriş |
|---|---:|---|---:|
| Satın Alma | 4 | Talep & Sipariş (sekmeli) · Hizmet ve fason · Faturalar. Fatura ekranları bugün kapalı | 3 |
| Stok & Lojistik | 12 | Stok pozisyonu · Transferler (planla, çıkış, fazla kabul, kayıp: sekmeler) · Yükleme & Sevk (satış iş listesi, talimatlar, fason teslim, plan üstü: sekmeler) · Mal kabul işleri · Fason ayırma | 5 |
| Ürün & Hizmet | 2 | Ürünler · Fiyatlar · Boya ürünleri | 3 |
| Cari | 3 | Değişmez | 3 |
| Finans & Hazine | 20 | Master'daki ekran ailelerine göre: Tahsilat & Ödeme · Banka & Kasa · Açık kalemler & Dağıtım · Çek/Senet · Kredi, Limit & Teminat · Resmî faturalar · Açılış & Devir. "Hazırla/onay" çiftleri tek sayfada sekme olur | 7 |
| Muhasebe | 16 | Kontrol · Fişler · Kaynak fişler · Maliyet düzeltmeleri (sevk SMM, alış farkı, üretim farkı, fatura satırı: sekmeler) · Hesap planı & Eşlemeler · Dönemler · Mizan (bugün menüde yok) · Cari hesaplar | 8 |
| Ayarlar | 39 | İki giriş: Ana Veriler ve Sistem Yönetimi. Her biri kartlarla gruplanmış ve aranabilir bir merkez sayfası | 2 |
| **Toplam** | **107** | | **yaklaşık 36** |

**Onay sayfaları:** hedef, onayların Çalışma Merkezi'nde toplanması. Ama ortak onay kutusu bugün
yalnız 4 onay türünü listeliyor (63 onay işleminden). Bu yüzden iki adım öneriyorum:

1. Önce her onay sayfası ilgili çalışma alanında **"Onay bekleyenler" sekmesi** olur. Menüden
   çıkar, işlev kaybolmaz.
2. Ortak onay kutusu genişledikçe sekmedeki iş Çalışma Merkezi'nde de görünür.

### 3.6 Nasıl yapılır

- **Tek ortak bileşen:** "çalışma alanı + sekmeler". Sipariş detayındaki sekme bileşeni
  (`sales/SalesOrderTabs.tsx`) erişilebilirlik kurallarıyla zaten yazılmış; genelleştirilerek
  `ui/` altına alınabilir.
- **Her sekme kendi yetkisiyle açılır.** Yetkisi olmayan kullanıcı o sekmeyi görmez. Hiçbir sekmesi
  açık olmayan giriş menüde çizilmez (boş menü gösterilmez kuralı, DEC-GOV-007).
- **Eski adresler çalışmaya devam eder.** `/satis/teklifler` gibi adresler yeni sayfanın ilgili
  sekmesine yönlenir; kayıtlı bağlantılar kırılmaz.
- **Arka uç, veritabanı, yetki ve sözleşme değişmez.** Yalnız ön yüz değişir.
- **Testler:** menü testleri ve "mount drift" testleri güncellenir; her sekmenin yetki kapısı için
  test eklenir.
- **Sınıf ve büyüklük:** ön yüz, Claude şeridi, K1/K2. Ortak bileşen ve Satış birlikte orta
  (yaklaşık bir hafta); diğer gruplar orta–büyük (iki ila üç hafta).

### 3.7 Arka uç gerektiren liste eksikleri

Master §5.3 sunucu tarafı filtre, sıralama ve arama istiyor. Satış listelerinde bugün yalnız durum
filtresi ve sayfalama var:

| Liste | Eksik |
|---|---|
| Siparişler | İşlem durumu (açık, sevk ediliyor, kapandı), cari, tarih aralığı, arama |
| Faturalar | Hiç filtre yok: durum, cari, tarih, resmî numara |
| Talepler, teklifler | Cari, tarih, arama |
| Hepsi | Sekme sayıları için sayım ucu; dışa aktarım |

Bu işler arka uç şeridinindir (Codex). Sekmeli düzen bunları beklemeden başlayabilir.

### 3.8 Senden onay gereken noktalar

1. Satış için önerilen dört girişli düzen.
2. Fiyat listelerinin Ürün & Hizmet'e taşınması (master §4 böyle diyor).
3. Onay sayfalarının menüden çıkıp çalışma alanında sekme olması.
4. Diğer gruplar için §3.5'teki düzen; istersen önce yalnız Satış yapılır, görüp karar verirsin.

Bunlar geri alınabilir ekran ayarlarıdır; işletme politikası değildir.

## 4. Nasıl devam edeceğiz

### 4.1 Senin yapacakların (bu hafta)

Kararlar `00_GENEL_OZET.md` §8'de. En çok işi açan üçü:

1. **İş rolleri:** kim, hangi şirkette, hangi işi yapacak. Bir tablo yeter: kişi, şirket, görev.
   Ben izin eşlemesini bu tablodan hazırlarım; sen onaylarsın.
2. **Muhasebenin pilot rolü:** kapalı, iç defter ya da Mikro'ya aktarım.
3. **Şirketler arası işlemler:** GDC, GSP ve GIN birbirine mal satıyor ya da devrediyor mu?
   Ediyorsa en sık üç örnek.

Ayrıca §3.8'deki menü onayı.

### 4.2 Karar beklemeden başlayabilecek işler

| # | İş | Şerit | Sınıf | Büyüklük |
|---|---|---|---|---|
| 1 | Rol tanımlama ve role izin atama ekranı (arka uç + ön yüz) | Codex + Claude | K3 | B |
| 2 | Test ortamı kurulum yolu: şema, üç şirket, ilk yönetici, yerel hesapla giriş; bir kez baştan sona denenmiş | Codex | K2 | O |
| 3 | Yazılmış ama kapalı ekranların denenip yayımlanması (alış faturası, cari şirkete bağlama, krediler, resmî fatura, hesap planı, fason teslim, yeniden açma) | Claude | K1/K2 | O |
| 4 | Menü sadeleştirme: ortak sekme bileşeni ve Satış (§3) | Claude | K1/K2 | O |
| 5 | Ekranlarda kimlik kodu yerine okunur ad ve numara (lojistik, üretim, muhasebe) | Claude + Codex | K2 | O |
| 6 | Zincir kopuklukları: risk okuması, sipariş kapatma, alış siparişinin talebe bağlanması ve çok satır, üretim plan ekranı alanları, çıktıdaki blok | Codex + Claude | K2/K3 | O–B |
| 7 | Toplu aktarım: cari, ürün, açılış stoku | Codex + Claude | K3 | B |
| 8 | Kurulum durumu ekranı: üç şirket için hangi ayar dolu, hangisi boş | Claude | K1 | K–O |
| 9 | Liste filtreleri ve sayım ucu (§3.7) | Codex | K2 | O |
| 10 | Zorunlu parametre eksik geldiğinde 500 yerine 400 dönmesi (16 uç) | Codex | K1 | K |

### 4.3 Kararlardan sonra

- Rollerin seed'e ve test ortamına işlenmesi.
- Muhasebe kararına göre: hesap planı yükleme, dönemlerin açılması ya da Mikro aktarım dosyası.
- GSP ve GIN finans profilleri ile dövizli işlemler.
- Şirketler arası asgari temsil.
- Test ortamında anahtarların açılması: ürün çalışma zamanı, olay taşıyıcısı, muhasebe kabulü,
  TCMB kuru.

### 4.4 Test ortamı ve deneme

1. Test sunucusuna kurulum (§4.2 iş 2).
2. Üç şirketin kurulum verisi (Ayarlar kontrol listesi, `02_...` belgesi).
3. Açılış verisi: cari, ürün, stok, bakiye.
4. Her şirkette gerçek kullanıcılarla üç zincirin denenmesi: siparişten nakde, satın almadan
   ödemeye, hammaddeden ürüne.
5. Bulguların haftalık listeye girmesi ve düzeltme turu.

### 4.5 Detaylı analizin devamı

İlk mesajda "önce yol, sonra ayrı ayrı detaylı analiz" demiştin. Modül analizleri bu yolun ilk
turuydu. Önerdiğim ikinci tur, her P0 iş için kısa bir **iş tanımı**: ne yapılacak, kabul ölçütü,
hangi dosyalar, hangi test. Sıra §4.2'deki tabloyla aynı. İlk iki iş tanımı: rol ekranı ve test
ortamı kurulum yolu. Menü sadeleştirmesi için ayrı iş tanımı gerekmez; bu belgenin 3. bölümü yeterli.
