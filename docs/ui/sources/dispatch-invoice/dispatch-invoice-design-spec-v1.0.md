# GÖYMEN ERP — İrsaliye & Fatura Çalışma Alanı

## Satış ve Alış Alt Menüleri İçin Nihai Bilgi Mimarisi, Ekran Kataloğu ve İşlevsel Tasarım Şartnamesi

**Sürüm:** 1.0  
**Tarih:** 24 Ağustos 2026  
**Karar statüsü:** Tasarım önerisi tamamlandı — kullanıcı kabulü sonrası kilitlenebilir  
**Ana karar kaynağı:** Göymen ERP Master Karar ve Kilitli Tasarım Raporu v3.0  
**Kapsam:** Satış ve Alış modüllerindeki ana menü düzeyindeki irsaliye, mal/hizmet kabulü, faturaya hazırlık, fatura eşleştirme, kontrol ve düzeltme ekranları

---

## 1. Nihai menü kararı

### 1.1 Sol menü sadeleştirmesi

Satış ve Alış altında ayrı ayrı duran **İrsaliyeler** ile **Faturalar** alt menüleri, her modülün kendi içinde tek bir alt menüde birleştirilir.

**Satış**

- Talep / Siparişler
- **İrsaliye & Fatura**
- İade / Red

**Alış**

- Alış Siparişleri
- **İrsaliye & Fatura**
- İade / Red

Bu birleşme yalnız menü ve ana operasyon merkezi düzeyindedir. Satış kayıtları ile alış kayıtları kesinlikle aynı tabloda karıştırılmaz. Satış ekranı yalnız satış yönünü; alış ekranı yalnız alış yönünü yönetir. Alış ve satışın birleşik görünümü yalnız **Raporlar** altında bulunur.

### 1.2 Sipariş detay ekranları korunur

Satış siparişi detayındaki:

- Gerçekleşme / İrsaliye
- Faturalar

sekmeleri kaldırılmaz. Alış siparişi detayındaki:

- Mal Kabul / İrsaliye
- Faturalar

sekmeleri de korunur.

Ancak bunlar ayrı kodlanan ikinci ekranlar olmaz. Ana **İrsaliye & Fatura** çalışma alanındaki aynı bileşenler, `orderId` bağlamıyla süzülerek sipariş detayında yeniden kullanılır. Böylece:

- tek hesap motoru,
- tek durum sözlüğü,
- tek kolon tanımı,
- tek yetki ve onay davranışı,
- tek audit zinciri

kullanılır.

### 1.3 Ana sayfa sekmeleri

Ana sayfanın üstünde her zaman görünen kompakt operasyon özeti bulunur; ayrıca tekrar eden bir “Genel Bakış” sekmesi açılmaz.

**Satış İrsaliye & Fatura sekmeleri**

1. Sevkiyat / İrsaliyeler
2. Faturaya Hazır
3. Faturalar
4. Kontrol Merkezi
5. İade / Düzeltme

**Alış İrsaliye & Fatura sekmeleri**

1. Mal / Hizmet Kabulü
2. Faturaya Hazır
3. Gelen Faturalar
4. Üçlü Kontrol
5. Red / İade / Düzeltme

İade / Red ana alt menüsü, tüm iadeleri görev merkezi mantığıyla yönetmeye devam eder. Birleşik sayfadaki son sekme ise yalnız seçili irsaliye, kabul veya fatura bağlamındaki ilişkili iade/düzeltmeleri gösterir ve hızlı geçiş sağlar.

---

## 2. Ortak sayfa kabuğu

### 2.1 Görsel sistem

- Sabit koyu lacivert sol menü
- Üstte beyaz/global araç çubuğu
- Açık gri-yeşilimsi içerik zemini
- Beyaz kartlar, ince sınırlar ve sınırlı gölge
- Mavi ana işlem ve seçili sekme
- Teal AI ve yardımcı ilişki vurgusu
- Yeşil başarı/uygunluk
- Amber kontrol/dikkat
- Kırmızı kritik hata/blokaj
- Mor fatura/finansal ilişki
- Gri pasif, eski sürüm veya terslenmiş kayıt

Renk tek başına anlam taşımaz; ikon ve açık durum metni birlikte kullanılır.

### 2.2 Bilgi hiyerarşisi

1. Breadcrumb ve sayfa kimliği
2. Sayfa yönü: Satış veya Alış
3. Ana işlem butonu
4. Kapsam ve filtreler
5. En fazla altı kritik KPI
6. Sekmeler
7. Ana tablo veya işlem kartları
8. Bağlamsal detay çekmecesi
9. Sağ AI paneli
10. Yoğun işlem için tam ekran workspace

### 2.3 Kapsam davranışı

Sayfa iki şekilde açılır:

**Genel alt menüden açılış**

- Modül kapsamındaki tüm satış veya alış kayıtları gösterilir.
- Kullanıcı müşteri/tedarikçi, şirket, tarih, durum ve istisna bazında çalışır.

**Sipariş detayından açılış**

- `Sipariş: SP-2026-1842` benzeri kilitli kapsam etiketi görünür.
- Yalnız seçili siparişin irsaliye/kabul payları ve faturaları gösterilir.
- Kullanıcı “Tüm satış kayıtlarına dön” veya “Tüm alış kayıtlarına dön” aksiyonuyla genel merkeze geçebilir.

### 2.4 Ortak filtreler

- Hukuki şirket
- Müşteri veya tedarikçi
- Tarih aralığı
- Sipariş no
- İrsaliye/kabul/fatura no
- Belge durumu
- Fatura durumu
- Para birimi
- Depo veya çıkış yeri
- Sorumlu kullanıcı
- Onay durumu
- İstisna türü
- Yalnız benim görevlerim
- Belgesi eksik olanlar
- Mükerrerlik şüphesi olanlar

### 2.5 Ortak durumlar

**İrsaliye / Mal-Hizmet Kabulü**

- Taslak
- Kontrol Bekliyor
- Onay Bekliyor
- Kesinleşti / Onaylandı
- İptal / Düzeltme

**Fatura**

- Taslak
- Kontrol Bekliyor
- Onay Bekliyor
- Dış Programa Aktarıldı
- Dış Belgeyle Eşleşti
- Kesinleşti
- İptal / Düzeltme

Kesinleşmiş hiçbir kayıt doğrudan silinmez. Taslaklar yetkiye göre silinebilir; kesin kayıtlar iptal, ters kayıt, düzeltme veya ayrı iade/red işlemiyle düzeltilir.

---

## 3. Satış — İrsaliye & Fatura ana ekranı

### 3.1 Üst KPI’lar

Varsayılan altı KPI:

1. Bugün planlanan sevkiyat: araç ve ayrı birimlerde miktar
2. Kontrol bekleyen irsaliye
3. Fatura bekleyen gerçekleşen tutar
4. Faturaya hazır grup
5. Onay bekleyen fiyat/kur/vade farkı
6. Kritik istisna

Karma birimler asla tek toplamda birleştirilmez. KG, adet, metre, m², saat ve sefer ayrı gösterilir.

### 3.2 Sekme 1 — Sevkiyat / İrsaliyeler

#### Amaç

Sevkiyat planı, yükleme talimatı, fiilî yükleme, satış irsaliyesi ve hizmet gerçekleşmesini tek operasyon zincirinde göstermek.

#### Ana işlem

**+ Yeni Sevkiyat / Gerçekleşme**

#### İç görünüm seçenekleri

- Sevkiyat zinciri
- Sipariş satırı görünümü
- Belge görünümü
- Zaman çizelgesi

#### Ana tablo kolonları

- Sevkiyat planı no
- Yükleme talimatı no
- İrsaliye no
- Müşteri
- Sipariş bağlantısı
- Sevk tarihi
- Araç / sürücü
- Teslimat adresi
- Çıkış yeri / fiziksel kaynak
- Sahiplik
- Planlanan miktar
- Fiilî miktar
- Gerçek KG
- İrsaliye miktarı
- Plan–fiilî farkı
- Net gerçekleşen
- Tolerans / fazla sevk
- Gerçekleşme durumu
- Fatura durumu
- İşlemler

#### Değişmez hesap davranışı

- Yükleme talimatı yalnız plandır; stok hareketi yaratmaz.
- Planlanan miktar sipariş Net/Kalan hesabına girmez.
- Fiilî miktar plan ve tahsisten farklı olabilir.
- Stok ve tahsis, kontrol edilip kesinleşen fiilî/irsaliye miktarına göre düzeltilir.
- İrsaliye taslağı fiilî yüklemeden oluşur.
- Yalnız kesinleşmiş irsaliye veya hizmet gerçekleşmesi Net Gerçekleşen’e girer.
- Negatif stok oluşmasına izin verilmez.
- Etiket zorunlu ürün lot/etiketsiz kesinleşemez.

#### Çoklu sipariş ve çoklu yükleme noktası

- Aynı araç, aynı carinin birden fazla siparişini taşıyabilir.
- Farklı cariler için ayrı irsaliye düzenlenir; araç bazlı sevkiyat planı ortak olabilir.
- Aynı teslimatta birden fazla fiziksel kaynak bulunabilir.
- Her fiziksel yükleme noktası için ayrı yükleme talimatı oluşur.
- İrsaliye satırının siparişlere miktar dağılımı zorunludur.
- Belge toplamı ile seçili siparişin payı ayrı gösterilir.
- Dış fason kaynaktan doğrudan sevkte merkez depoya yapay giriş/çıkış yapılmaz.

#### Satır genişletme

Açılan detayda:

- Plan → talimat → fiilî → irsaliye → fatura zaman çizgisi
- Fiziksel kaynak dağılımı
- Tahsis planı ve fiilî tüketim farkı
- Sipariş payları
- Lot/etiket/heat/coil
- Belge önizleme
- Fatura ilişkileri
- Audit

bulunur.

### 3.3 Sekme 2 — Faturaya Hazır

#### Amaç

Kesinleşmiş fakat tamamen faturalanmamış gerçekleşme paylarını uyum kurallarına göre gruplamak ve fatura taslağı hazırlamak.

#### Faturalanabilir en küçük birim

İrsaliye belgesinin tamamı değil:

**İrsaliye satırı × siparişe bağlı gerçekleşme payı**

Bu sayede:

- bir irsaliye birden fazla faturaya,
- bir fatura birden fazla irsaliyeye,
- aynı satır kısmi miktarlarla birden fazla faturaya

bağlanabilir.

#### Grup uyumluluk şartları

Aynı fatura taslağına yalnız aşağıdakilerin tamamı aynı olan paylar alınabilir:

- Hukuki şirket
- Müşteri
- Para birimi
- Tek fatura kuru
- Normalize ödeme planı
- Vergi/fatura senaryosu

Farklı vade kuralları aynı tek ödeme tarihini üretse bile diğer bütün kriterler de aynı olmalıdır. Taksitli planda bütün tarih ve oran yapısı aynı olmalıdır.

#### Hazır grup kartı

- Grup no
- Hukuki şirket
- Müşteri
- Para birimi
- Kur
- Vade/ödeme planı
- Tahmini ödeme tarihi
- Bağlı irsaliye sayısı
- Gerçekleşme payı sayısı
- Toplam miktarları ayrı birimlerde
- Tahmini matrah
- Uyum açıklaması
- Ayrı grup nedeni
- **Fatura Taslağı Hazırla**

#### Alt tablo kolonları

- İrsaliye no / satır / pay
- Sipariş no / satır
- Ürün / hizmet
- Fiyatlandırma birimi
- Gerçekleşen miktar
- Önceden faturalanan
- Bu faturaya alınabilir kalan
- Sipariş nihai fiyatı
- Gerçek KG ve efektif KG fiyatı
- Para birimi
- Ödeme planı
- Vergi senaryosu
- Grup uygunluğu
- İşlem

### 3.4 Sekme 3 — Faturalar

#### Amaç

ERP fatura taslağını, dış programda oluşan resmî faturayı, gerçekleşme eşleşmelerini ve cari etkiyi tek yerde izlemek.

#### İlk faz sınırı

- Resmî satış faturası başka programda kesilir.
- ERP “Fatura Kes” ifadesini kullanmaz.
- Kullanılan aksiyonlar:
  - Fatura Taslağı Hazırla
  - Onaya Gönder
  - Dış Programa Aktar
  - Dış Belgeyi Yükle
  - Dış Belgeyle Eşleştir
  - Kesinleştir

#### Ana tablo kolonları

- ERP fatura no
- Dış fatura no / ETTN / belge kimliği
- Müşteri
- Hukuki şirket
- Fatura tarihi
- Fatura senaryosu
- Para birimi
- Tek fatura kuru
- Ödeme planı / vade
- Bağlı irsaliye payları
- Fatura toplamı
- Seçili siparişe düşen tutar
- Fiyat farkı / vade farkı / iskonto / masraf / nakliye
- Vergi / tevkifat
- Cari hareket durumu
- Mikro/dış program durumu
- Onay durumu
- İşlemler

Cari hareket yalnız fatura ERP’de dış belgeyle eşleşip kesinleştiğinde oluşur.

### 3.5 Sekme 4 — Kontrol Merkezi

#### Kontrol katmanları

- Ürün eşleşmesi
- Miktar
- Fiyatlandırma birimi
- Sipariş nihai fiyatı ↔ fatura nihai fiyatı
- Onaylı fiyat hareketleri
- Kur
- Vade / normalize ödeme planı
- KDV
- Tevkifat
- Fatura senaryosu
- Mükerrer fatura/irsaliye
- Belge hash’i
- Eksik belge
- Açık/bağlanmamış satır
- Yetki ve onay

#### Sonuç sınıfları

- Kritik / bloklayan
- Onay gerektiren
- Tolerans dahil
- Bilgi

#### Aksiyonlar

- Eşleşmeyi İncele
- Grubu Ayır
- Fiyat Hareketi Hazırla
- Onaya Gönder
- Belgeyi Karşılaştır
- AI’ye Nedenini Sor
- Kontrol Raporu Dışa Aktar

### 3.6 Sekme 5 — İade / Düzeltme

Bu sekme ilişkili hareketleri gösterir; asıl merkezi görev yönetimi İade / Red alt menüsünde devam eder.

Gösterilecek etkiler:

- Kaynak belge
- İade/düzeltme nedeni
- Ters kayıt bağlantısı
- Yeni düzeltme belgesi
- Stok etkisi
- Net Gerçekleşen etkisi
- Fatura etkisi
- Cari etkisi
- Onay ve audit

Yanlış kesin irsaliye doğrudan değiştirilmez; iptal/ters kayıt ve yeni düzeltme belgesiyle çözülür.

---

## 4. Alış — İrsaliye & Fatura ana ekranı

### 4.1 Üst KPI’lar

1. Bugün beklenen mal/hizmet kabulü
2. Kontrol bekleyen kabul
3. Fatura bekleyen net kabul
4. Faturaya hazır grup
5. Fiyat/kur/vergi sapması
6. Kritik üçlü kontrol istisnası

### 4.2 Sekme 1 — Mal / Hizmet Kabulü

#### Amaç

Tedarikçi irsaliyesi, mal kabul, hizmet kabulü, net kabul, red/eksik, kantar, depo, lot/etiket ve kalite sonucunu tek akışta yönetmek.

#### Ana işlem

**+ Yeni Mal / Hizmet Kabulü**

#### Ana tablo kolonları

- ERP kabul no
- Tedarikçi irsaliye no
- Tedarikçi
- Alış siparişi / satır
- Kabul tarihi
- Araç
- Depo
- Sahiplik
- Ürün / hizmet
- İrsaliye miktarı
- Net kabul
- Red / eksik
- Red nedeni
- Kantar net KG
- Lot / etiket
- Heat / coil
- MTC / 3.1
- Kalite sonucu
- Fatura durumu
- Kabul durumu
- İşlemler

#### Değişmez hesap davranışı

- İrsaliye miktarı, net kabul, red/eksik ve kantar miktarı ayrı saklanır.
- Net kabul stok ve alış siparişi gerçekleşmesine esas olur.
- Kantar yalnız kontrol verisidir; doğrudan sipariş Net/Kalan hesabının kaynağı değildir.
- Red/eksik miktar sipariş Net’ine girmez.
- Etiket zorunlu ürün lot/etiketsiz kesinleşemez.
- Müşteriye ait fason mal başka müşterinin satışına kaynak olamaz.
- Depo fiziksel konumu, sahiplik malın kime ait olduğunu gösterir; birbirinin yerine kullanılmaz.
- Hizmet kabulünde ticari hizmet miktarı ile fiziksel girdi/çıktı miktarları karıştırılmaz.

### 4.3 Sekme 2 — Faturaya Hazır

Faturalanabilir en küçük birim:

**Kabul satırı × alış siparişine bağlı net kabul payı**

Hazır grup şartları:

- Hukuki şirket
- Tedarikçi
- Para birimi
- Tek fatura kuru
- Normalize ödeme planı
- Vergi/fatura senaryosu

Ek olarak grup kartında üçlü kontrol hazırlığı gösterilir:

- Ürün uyumu
- Net kabul miktarı
- Sipariş nihai fiyatı
- Belge/lot/kalite eksikleri
- MTC durumu

### 4.4 Sekme 3 — Gelen Faturalar

#### İlk faz akışı

1. Tedarikçi faturası XML, PDF veya Excel olarak yüklenir.
2. AI belge türünü ve alanları çıkarır.
3. Fatura no/cari/tutar/hash mükerrerliği kontrol edilir.
4. Alış siparişi ve mal/hizmet kabul payları için eşleşme adayları oluşturulur.
5. Kullanıcı eşleşmeyi kontrol eder.
6. Üçlü kontrol çalıştırılır.
7. Gerekirse fiyat/kur/vergi farkı onaya gönderilir.
8. Fatura kesinleşince cari hareket oluşur.

#### Ana tablo kolonları

- ERP fatura no
- Tedarikçi fatura no / ETTN
- Tedarikçi
- Fatura tarihi
- Para birimi / kur
- Ödeme planı / vade
- Bağlı alış siparişleri
- Bağlı net kabul payları
- Fatura toplamı
- Eşleşen tutar
- Üçlü kontrol durumu
- Cari hareket durumu
- Belge güveni
- Mükerrerlik durumu
- Onay durumu
- İşlemler

### 4.5 Sekme 4 — Üçlü Kontrol

Karşılaştırma yapısı:

**Alış Siparişi ↔ Net Kabul ↔ Gelen Fatura**

#### Karşılaştırılan alanlar

- Ürün kodu, kalite ve ebat
- Sipariş miktarı
- İrsaliye miktarı
- Net kabul
- Red/eksik
- Fatura miktarı
- Fiyatlandırma birimi
- Sipariş baz fiyatı
- Sipariş nihai fiyatı
- Fatura nihai fiyatı
- Onaylı PriceAdjustment kayıtları
- Para birimi
- Kur
- Vade/ödeme planı
- KDV
- Tevkifat
- Fatura senaryosu
- Mükerrerlik
- Belge eksikleri

#### Kritik blokaj örnekleri

- Fatura miktarı net kabulden büyük
- Ürün veya kalite uyuşmuyor
- Fiyat farkı yetki limitini aşıyor
- Kur tolerans dışında ve gerekçe yok
- Mükerrer fatura şüphesi
- Etiket/lot zorunlu fakat eksik
- Kalite sonucu red

### 4.6 Sekme 5 — Red / İade / Düzeltme

- Mal kabul reddi
- Kısmi red
- Alış iadesi
- Hizmet kabul düzeltmesi
- Kantar kontrol revizyonu
- Fatura düzeltmesi

stok, sipariş Net’i, fatura ve cari etkileri ayrı gösterilir. Eski belge doğrudan değiştirilmez.

---

## 5. Tam ekran işlem çalışma alanları

## 5.1 SW-01 — Yeni Sevkiyat / Gerçekleşme

### Başlangıç modları

1. Tahsislerden Yükleme Talimatı Oluştur
2. Yükleme Sonucu Gir / İrsaliye Taslağı
3. Belgeyi AI ile Oku
4. Hizmet Gerçekleşmesi Gir

### Adımlar

1. Başlangıç modu
2. Sevkiyat ve sipariş kapsamı
3. Planlanan / fiilî giriş
4. Tahsis, stok, tolerans ve dağılım kontrolü
5. İrsaliye taslağı ve onay

### Ana alanlar

- Sevk tarihi
- Araç / plaka
- Sürücü
- Çıkış yeri
- Teslimat adresi
- Sevk şekli
- Mevcut sipariş
- Aynı carinin uyumlu açık siparişleri
- Sipariş satırı
- Açık miktar
- Aktif tahsis
- Planlanan miktar
- Fiilî miktar
- Gerçek KG
- Kaynak
- Yükleme noktası
- Tolerans
- Çoklu sipariş dağılımı
- Etiket/lot/heat/coil
- Kantar
- Belge ekleri

### Sabit sağ panel

- Planlanan toplamlar ayrı birimlerde
- Fiilî toplamlar ayrı birimlerde
- Plan–fiilî farkı
- Tahsis etkisi
- Stok etkisi
- Negatif stok kontrolü
- Tolerans kontrolü
- Çoklu sipariş dağılımı toplamı
- Eksik etiket/belge
- AI uyarıları

### Alt butonlar

- İptal
- Taslak Kaydet
- Kontrol Et
- Onaya Gönder / Kesinleştir

Sağ üst X ve İptal aynı çıkış davranışını kullanır. Kaydedilmemiş değişiklik varsa uyarı zorunludur.

## 5.2 SW-02 — Satış Fatura Taslağı / Eşleştirme

### Adımlar

1. Hazır grup veya dış belge
2. Gerçekleşme payı eşleştirme
3. Fiyat, vergi, kur ve ödeme planı
4. Kontrol ve etki önizlemesi
5. Onay / dış program / eşleşme

### Kısmi miktar zinciri

- Gerçekleşen
- Önceden faturalanan
- Bu faturaya
- Sonra kalan

### Fiyat köprüsü

- Ürün kartı başlangıç fiyatı
- Sipariş baz fiyatı
- Sipariş aşaması fiyat hareketleri
- Sipariş nihai fiyatı
- Fatura aşaması hareketleri
- Fatura nihai fiyatı

Satır fiyat hareketleri ile fatura geneli masraf/nakliye birbirinden ayrı tutulur ve ayrı raporlanır.

## 5.3 PW-01 — Yeni Mal / Hizmet Kabulü

### Adımlar

1. Belge ve tedarikçi
2. Alış siparişi ve satır eşleştirme
3. İrsaliye miktarı / net kabul / red
4. Depo, sahiplik, lot, kalite ve belgeler
5. Stok ve sipariş etkisi / kesin kabul

### Kritik alanlar

- Tedarikçi irsaliye no
- Belge tarihi
- Araç
- Depo
- Sahiplik
- İrsaliye miktarı
- Net kabul
- Red/eksik
- Red nedeni
- Kantar brüt/dara/net
- Lot/etiket
- Heat/coil
- MTC/3.1
- Kalite sonucu
- Hizmet gerçekleşme miktarı

## 5.4 PW-02 — Gelen Fatura Girişi / Eşleştirme

### Adımlar

1. XML/PDF/Excel yükleme veya manuel başlık
2. OCR/vision alan güvenleri
3. Sipariş ve net kabul eşleştirme
4. Üçlü kontrol
5. Onay ve kesinleştirme

### Sağ panel

- Mükerrerlik skoru
- Eşleşme güveni
- Fatura miktarı ↔ net kabul
- Sipariş nihai ↔ fatura nihai fiyat
- Kur sapması
- Vergi farkı
- Ödeme planı farkı
- Eksik belge
- Cari hareket önizlemesi

---

## 6. Belge detay çekmecesi

Satırdaki **İncele** aksiyonu sağdan açılan detay çekmecesini kullanır.

Sekmeler:

- Özet
- Satırlar ve sipariş payları
- Belge önizleme
- Fiyat / kontrol
- İlişkiler
- Audit

Özet alanları:

- Hukuki şirket
- Cari
- Belge no/tarih
- Durumlar
- Belge toplamı
- Seçili siparişe düşen pay
- Faturalanan
- Kalan
- Fiziksel kaynak / depo
- Sahiplik
- İlişki zinciri

---

## 7. AI davranışı

### Yapabilir

- PDF, XML, foto ve Excel’den belge taslağı çıkarmak
- Belge türünü sınıflandırmak
- Cari, ürün, sipariş, irsaliye, kabul ve fatura eşleşme adayları önermek
- Mükerrerlik kontrolü yapmak
- Faturaya hazır grupları önermek
- Farklı kur/vade/vergi nedeniyle neden ayrı grup gerektiğini açıklamak
- Plan–fiilî farkını ve tahsis/stok etkisini önizlemek
- Üçlü kontrol farklarını özetlemek
- Eksik lot, MTC, kalite veya belgeyi bildirmek
- Fiyat köprüsünü açıklamak
- Kontrol raporu hazırlamak

### Tek başına yapamaz

- İrsaliye veya mal kabulü kesinleştirmek
- Negatif stokla işlem yapmak
- Fiyat/kur/vade farkını onaysız uygulamak
- Faturayı nihai onaylamak
- Cari hareketi kontrolsüz oluşturmak
- Kesin belgeyi silmek
- Ticaret Sicili veya master veriyi onaysız değiştirmek

Her AI önerisi kaynak, güven puanı, kullanıcı değişikliği ve onay iziyle audit’e yazılır.

---

## 8. Yetki ve sorumluluk ayrımı

### Satış

- Satış personeli: sipariş bağını ve sevkiyat talebini hazırlar
- Depo/operasyon: yükleme talimatı ve fiilî sonucu girer
- Sevkiyat yetkilisi: irsaliye taslağını kontrol eder
- Muhasebe: fatura taslağı/eşleştirme ve vergi kontrolü yapar
- Satış müdürü/finans: fiyat, kur ve vade istisnasını onaylar
- Yönetici: limit üstü ve iptal/ters kayıt işlemlerini onaylar

### Alış

- Satın alma: sipariş ve tedarikçi bağını yönetir
- Depo: fiziksel kabul ve miktarı girer
- Kalite: kalite sonucu, red ve belge kontrolünü yapar
- Muhasebe: gelen faturayı eşleştirir ve üçlü kontrolü yönetir
- Satın alma yöneticisi/finans: fiyat, kur, vade ve miktar istisnasını onaylar
- Yönetici: limit üstü, iade ve ters kayıt işlemlerini onaylar

---

## 9. Mobil / PWA davranışı

- Tablo yerine kart görünümü
- Kolon seçimi ve yatay kaydırma
- Kamera ile irsaliye/fatura/MTC çekimi
- Barkod/etiket okutma
- Mobil kantar veya fiilî miktar girişi
- Sabit alt işlem çubuğu
- Kaydedilmemiş değişiklik uyarısı
- Onay görevleri için bildirim
- Çevrimdışı taslak; çevrimiçi olunca idempotent senkronizasyon

Mobilde hiçbir kritik toplam veya durum yalnız renkle ifade edilmez.

---

## 10. Teknik bileşen ve rota önerisi

### Rotalar

- `/sales/documents`
- `/sales/documents?orderId=SP-2026-1842`
- `/purchase/documents`
- `/purchase/documents?orderId=AS-2026-0442`

### Ortak bileşenler

- `DocumentHubShell`
- `OperationSummaryStrip`
- `DocumentFilterBar`
- `FulfillmentChainGrid`
- `InvoiceReadyGroupBoard`
- `InvoiceGrid`
- `ControlExceptionGrid`
- `CorrectionRelationGrid`
- `DocumentDetailDrawer`
- `ShipmentRealizationWorkspace`
- `GoodsReceiptWorkspace`
- `InvoiceDraftMatchWorkspace`
- `ThreeWayControlPanel`
- `PriceBridge`
- `PaymentPlanNormalizer`
- `DocumentPreview`
- `AuditTimeline`
- `ContextualAIPanel`

Satış ve alış aynı kabuk ve bileşen sözleşmesini kullanır; veri yönü ve iş kuralları ayrı adaptörlerle çalışır.

---

## 11. Kabul kriterleri

1. Satış sayfasında alış kaydı görünemez; alış sayfasında satış kaydı görünemez.
2. Birleşik satış+alış görünümü yalnız Raporlar altında açılır.
3. Yükleme talimatı stok hareketi üretmez.
4. Net Gerçekleşen yalnız kesinleşmiş belge paylarından hesaplanır.
5. Planlanan miktar Net/Kalan hesabına girmez.
6. Fiilî miktar tahsis ve serbest stoktan fazlaysa negatif stok oluşmadan işlem durur veya yetkili ek tahsis önizlemesi açılır.
7. Çoklu siparişli irsaliyede satır–sipariş miktar dağılımı zorunludur.
8. Çoklu yükleme noktasında her fiziksel kaynağa ayrı talimat oluşur.
9. Etiket zorunlu ürün lot/etiketsiz kesinleşemez.
10. Alışta irsaliye miktarı, net kabul, red/eksik ve kantar ayrı saklanır.
11. Fatura miktarı net kabul veya satış gerçekleşme payını aşamaz.
12. Bir faturada tek para birimi ve tek kur bulunur.
13. Farklı normalize ödeme planları aynı faturada birleşemez.
14. Kısmi faturalama Gerçekleşen → Önceki → Bu Faturaya → Kalan zinciriyle çalışır.
15. Sipariş nihai fiyatı ile fatura nihai fiyatı arasındaki köprü açıkça gösterilir.
16. Yetkisiz kullanıcı fiyat/kur/vade farkını yalnız hazırlayıp onaya gönderebilir.
17. Fatura kesinleşmeden cari hareket oluşmaz.
18. Aynı hash ile ikinci belge yüklenirse mükerrerlik uyarısı verilir.
19. Kesinleşmiş belge doğrudan silinemez.
20. İade/red, stok, Net, fatura ve cari etkilerini ayrı hareketlerle düzeltir.
21. AI kullanıcı onayı olmadan kritik işlem kesinleştiremez.
22. Bütün kritik işlemler kullanıcı, zaman, eski/yeni değer, belge hash’i, onay ve ters kayıt ilişkisiyle audit edilir.
23. Sipariş detayındaki irsaliye ve fatura sekmeleri ana merkezle aynı veri ve bileşenleri kullanır.
24. Mobil görünümde belge fotoğrafı, etiket okuma ve kart görünümü kullanılabilir.

---

## 12. Ekran kimlikleri ve geliştirme sırası

| ID | Ekran / Workspace | Öncelik |
|---|---|---:|
| SDH-01 | Satış İrsaliye & Fatura Merkezi | 1 |
| SDW-01 | Yeni Sevkiyat / Gerçekleşme | 2 |
| SDW-02 | Satış Fatura Taslağı / Eşleştirme | 3 |
| PDH-01 | Alış İrsaliye & Fatura Merkezi | 4 |
| PDW-01 | Yeni Mal / Hizmet Kabulü | 5 |
| PDW-02 | Gelen Fatura Girişi / Eşleştirme | 6 |
| DCD-01 | Ortak Belge Detay Çekmecesi | 7 |
| DCE-01 | Kontrol ve İstisna Bileşeni | 8 |
| DCR-01 | İade / Düzeltme İlişki Görünümü | 9 |

Geliştirme, yalnız ekran çizimiyle paralel ilerlememelidir. Önce ortak durum sözlüğü, hesap motoru, çoktan-çoğa gerçekleşme–fatura bağlantısı, belge hash/version, yetki, onay, transaction, idempotency ve audit katmanları kurulmalıdır.

---

## 13. Son karar özeti

- Kullanıcının önerdiği şekilde, **İrsaliyeler** ve **Faturalar** her modülün kendi içinde tek **İrsaliye & Fatura** alt menüsünde birleştirilir.
- Satış ve alış yönleri kesin biçimde ayrı kalır.
- Ana çalışma alanı sekmeli olur; özet alanı sekmelerin üstünde sabit kalır.
- Sipariş detayındaki mevcut irsaliye ve fatura sekmeleri korunur; aynı bileşenleri sipariş kapsamıyla kullanır.
- Satışta sevkiyat/fiilî/irsaliye; alışta irsaliye/net kabul/red/kantar; her iki yönde faturaya hazır paylar ve fatura kontrolü tek uçtan uca zincir olarak yönetilir.
- İlk fazda resmî fatura dış programdadır; ERP taslak, kontrol, eşleştirme, onay, cari hareket ve audit merkezidir.
- AI önerir, okur, eşleştirir ve kontrol eder; hiçbir kritik kaydı tek başına kesinleştirmez.
