# Göymen ERP Finans Ana Menüsü — Ayrıntılı Analiz ve Revizyon Raporu

## 1. Çalışmanın amacı

Bu çalışma, önceki finans dashboard ve alt ekran tasarımlarını yalnız görsel olarak değil; günlük kullanım hızı, veri doğruluğu, finansal anlam, görevler ayrılığı, Göymen ERP ile görsel uyum, mobil davranış ve gelecekte genişleyebilirlik bakımından yeniden değerlendirmiştir.

Nihai hedef şudur:

> Günlük finans işlemleri tam kullanılabilir, hazine tarafı yeterli, kredi-limit-teminat-bütçe tarafı ilk sürümde sade, kontrol ve audit tarafı güçlü, ağır kurumsal fonksiyonlar ise veri modeli bozulmadan sonradan açılabilir olmalıdır.

## 2. Önceki tasarımlarda güçlü bulunan noktalar

- Nakit, tahsilat, ödeme, borç, limit ve kontrolün aynı ana ekranda toplanması doğruydu.
- 13 haftalık nakit akışı ana karar paneli olarak doğru yerdeydi.
- Tahsilat/ödeme, açık kalem dağıtımı ve banka/kasa ayrı görev alanları olarak ele alınmıştı.
- Kredi, limit, teminat ve bütçe ana menüde özet düzeyinde tutulmuştu.
- Göymen ERP’ye özgü koyu lacivert uygulama kabuğu, mavi ana işlem, beyaz kartlar ve sağ AI paneli doğru görsel yöndü.
- “Finansal kaynak” ile “açık kalem dağıtımı”nı ayıran iş kuralı doğru kurulmuştu.
- Kesin kayıt silmek yerine ters kayıt ve tarihçe kullanılması doğruydu.

## 3. Tespit edilen eksiklikler ve uygulanan çözümler

### 3.1 Tahsilat süreci yalnız tahsilat kaydı ve dağıtım düzeyindeydi

**Eksik:** Vadesi geçmiş alacak, ödeme sözü, tahsilat görevi, ihtilaf, yapılandırma, hukuki takip ve risk istisnası için birleşik görev ekranı yoktu.

**Çözüm:** “Tahsilat Takibi ve Cari Risk” ana ekranı eklendi. Alt sekmeler:

- Vadesi Geçmiş
- Ödeme Sözleri
- Tahsilat Görevleri
- İhtilaf / Yapılandırma
- Hukuki Takip
- Risk / Limit

Buna bağlı altı işlem workspace’i oluşturuldu.

### 3.2 Ödeme yönetimi bankaya gönderim sonrası tamamlanmıyordu

**Eksik:** Ödeme önerisi, paket, talimat ve onay vardı; bankanın kabul, kısmi gerçekleşme, red veya iade sonucu ayrı işlem olarak modellenmemişti.

**Çözüm:** “Banka Ödeme Sonucu / Red” workspace’i eklendi. Böylece ödeme talimatı ile gerçek banka sonucu ayrıldı.

### 3.3 Ödeme araçlarının yaşam döngüsü eksikti

**Eksik:** Çek/senet kayıt ve karşılıksız süreci vardı; tahsilat/ödeme sonucu ayrı işlem değildi. POS chargeback/iade süreci bulunmuyordu.

**Çözüm:** Aşağıdaki işlemler eklendi:

- Çek / Senet Tahsilat veya Ödeme Sonucu
- POS İade / Chargeback

Araç kaydı, açık kalem kapaması, nakitleşme ve risk kapanışı ayrı tutuldu.

### 3.4 Nakit açığı görülüyor fakat aksiyon planı yoktu

**Eksik:** 13 haftalık akış açığı gösteriyor, fakat hangi limit, kredi, erteleme veya tahsilat aksiyonuyla kapatılacağını yöneten işlem bulunmuyordu.

**Çözüm:** “Nakit Açığı Eylem Planı” workspace’i eklendi. Bu işlem gerçek kaydı değiştirmeden finansman ve ödeme/tahsilat aksiyonlarını senaryo olarak oluşturur.

### 3.5 Kredi karar desteği eksikti

**Eksik:** Kredi kayıt ve ödeme planı vardı; banka tekliflerini net nakit, toplam maliyet, vade, teminat ve nakit etkisiyle karşılaştıran işlem yoktu.

**Çözüm:** “Kredi Teklif Karşılaştırma” workspace’i eklendi.

### 3.6 Bütçe onay yaşam döngüsü eksikti

**Eksik:** Bütçe giriş, sürüm ve transfer işlemleri vardı; sürümün yönetim/finans onayı ayrı işlem değildi.

**Çözüm:** “Bütçe Sürümü Onayı” workspace’i eklendi.

### 3.7 Kontrol merkezi bazı kritik yönetişim işlemlerini kapsamıyordu

**Eksik:** Ödeme dışındaki kredi, limit, teminat, bütçe ve risk istisnaları için ortak onay; mükerrer kayıt incelemesi; kapalı dönemi yeniden açma talebi yoktu.

**Çözüm:** Üç yeni workspace eklendi:

- Genel Finans Onay Kararı
- Mükerrer Ödeme / Kayıt İncelemesi
- Finans Dönemini Yeniden Açma

### 3.8 Finans raporları ile global rapor menüsü karışıyordu

**Eksik:** Sol ana menüde “Raporlar” bulunurken finans modülünde ayrıca “Finans Raporları” vardı; kullanıcı kapsamı karıştırabilirdi.

**Çözüm:** Global menü “Kurumsal Raporlar” olarak ayrıştırıldı. Finans Raporları yalnız operasyonel finans ve hazine kapsamını içerir.

## 4. Tespit edilen fazlalıklar ve sadeleştirmeler

### 4.1 Çok sayıda eşit ağırlıklı KPI

Önceki ana ekranlarda sekiz-on iki KPI aynı ağırlıktaydı. Bu durum karar önceliğini belirsizleştiriyordu.

**Yeni düzen:** Altı ana KPI + üç ikincil hızlı gösterge kullanıldı. Ana KPI’lar:

1. Kullanılabilir Nakit
2. 30 Günlük Beklenen Tahsilat
3. 30 Günlük Planlanan Ödeme
4. 30 Gün Sonu Tahmini Nakit
5. Toplam Finansal Borç
6. Kullanılabilir Banka Limiti

İkincil göstergeler:

- 30 Günlük Borç Servisi
- Dağıtılmamış Finansal Kaynak
- Açık Kontrol ve İstisna

### 4.2 “Net hareket” ve “dönem sonu nakit” karışıklığı

**Düzeltme:**

- Net hareket = beklenen tahsilat − planlanan ödeme
- Dönem sonu tahmini nakit = başlangıç kullanılabilir nakit + net hareket

İki değer ayrı gösterildi.

### 4.3 Planlanan ödeme ve borç servisinde gereksiz eksi işareti

Başlık zaten çıkışı belirttiği için tutar pozitif gösterildi; kırmızı/çıkış etiketi yeterli oldu. Eksi işareti yalnız net fark ve sapma değerlerinde kullanıldı.

### 4.4 Çok fazla işlemin “sık kullanılan” görünmesi

İlk katalogta kullanıcıya 40’tan fazla işlem gösteriliyordu.

**Düzeltme:** 73 işlemden yalnız 22’si varsayılan “Sık Kullanılan” görünümünde bırakıldı. Diğerleri Günlük Finans, Hazine, Bütçe, Kontrol veya Tümü filtreleriyle erişilir.

### 4.5 Ağır hazine alanlarının ilk formda görünmesi

Facility hiyerarşisi, faiz reseti, haircut, covenant formülü gibi alanların ilk sürümde kullanıcıyı yorması engellendi. Ana formda yalnız gerekli alanlar; ileri bilgiler kapalı/gelişmiş bölüm ve altyapı alanlarında tutulur.

## 5. Kullanıcı dostu bilgi mimarisi

### 5.1 Sol menü

Finans menüsü görev sırasına göre iki gruptur:

**Günlük Finans**

- Genel Bakış
- Tahsilat / Ödeme
- Açık Kalemler ve Dağıtım
- Tahsilat Takibi / Cari Risk
- Banka / Kasa
- Ödeme Yönetimi
- Ödeme Araçları

**Hazine**

- Nakit Planı
- Krediler ve Limitler
- Teminatlar / Yükümlülükler
- Nakit Bütçesi
- Finans Kontrol Merkezi
- Finans Raporları

Bu sıra, “para hareketini kaydet → açık kaleme bağla → tahsilatı takip et → banka ve ödeme araçlarını yönet → hazineyi planla → kontrol et” akışını destekler.

### 5.2 Her sayfada ortak yapı

1. Breadcrumb
2. Sayfa başlığı ve tek cümlelik amaç
3. Görünüm / Paylaş / Çıktı
4. Tek baskın ana işlem
5. Durum sekmeleri
6. Arama ve filtreler
7. Ana liste veya karar paneli
8. Sağ detay çekmecesi
9. Sağdan açılan bağlamsal AI paneli

### 5.3 İşlem workspace’i

Her mali etkili işlem:

- Tam ekran açılır
- 3–5 adım gösterir
- Zorunlu alanları işaretler
- Kaynak ilişkisini ve belgeyi taşır
- Etki özetini sağda gösterir
- Kontrol listesini sunar
- Taslak kaydetme ve onaya gönderme düğmelerini sabit alt çubukta tutar
- Kaydedilmemiş değişiklikte kapatma uyarısı verir

## 6. Finansal anlamı koruyan tasarım kararları

- Banka/kasa hareketi ile fatura kapaması aynı işlem değildir.
- Dağıtım, ikinci banka veya cari hareketi oluşturmaz.
- Dağıtılmamış tahsilat net cari riski azaltabilir; açık faturayı otomatik kapatmaz.
- Çekle kapanan fatura ticari olarak kapanır, fakat çek riski ve nakitleşme ayrı kalır.
- DBS banka kabulünde kapsam yaratır; gerçek tahsilatta kesin kapama olur.
- POS slipi müşteriyi brüt tutarla kapatır; komisyon ayrı gider, banka girişi net tutardır.
- Şirket kredi kartı tedarikçi borcunu kart borcuna dönüştürür; banka çıkışı ekstre ödemesinde olur.
- Siparişe dağıtılan tutar avanstır; fatura kapaması değildir.
- Aynı olay sipariş, fatura, araç ve banka hareketi aşamalarında nakit akışına birden fazla kez girmez.

## 7. Mobil ve erişilebilirlik değerlendirmesi

- Sol menü mobilde overlay olarak açılır.
- KPI kartları dar ekranda tek/iki kolon olur.
- Tablolar yatay kaydırılabilir; ana kimlik sütunları korunur.
- Workspace tam ekran olur; sabit alt işlem çubuğu erişilebilir kalır.
- Renk tek başına anlam taşımaz; metin, ikon ve durum etiketi birlikte kullanılır.
- Tıklanabilir hedefler mobilde en az 40–44 piksel yüksekliğe yaklaşır.
- Yatay taşma otomatik testte `0` olarak doğrulanmıştır.

## 8. Nihai kapsam kararı

Bu çalışma alanı aşağıdakileri eksiksiz tasarlar:

- Günlük tahsilat/ödeme ve dekont
- Açık kalem ve ortak dağıtım motoru
- Tahsilat takibi ve cari risk
- Banka/kasa ve operasyonel banka mutabakatı
- Ödeme önerisi, paket, talimat, onay ve banka sonucu
- Çek/senet, DBS, POS ve şirket kredi kartı
- 13 haftalık nakit planı ve basit stres senaryosu
- Sade kredi, limit, teminat ve banka yükümlülüğü yönetimi
- Nakit bütçesi ve sürümleri
- Kontrol, onay, kapanış, audit ve operasyonel finans raporları

Yalnız Mikro mutabakatı ve resmî muhasebe/vergi kapsam dışıdır.
