# Göymen ERP Finans — Ana Ekran Kataloğu

Bu belge 13 ana ekranın **amacını, kullanıcı rolünü, ana aksiyonunu, bilgi mimarisini, sekmelerini, satır detayını, boş/hata durumlarını ve responsive davranışını** tanımlar.

## Ortak ekran standardı

Her ekran aşağıdaki ortak kabuğu kullanır:

- Göymen ERP koyu lacivert sabit sol menü
- Global arama: cari, sipariş, dekont, çek, kredi veya belge
- Şirket, rapor tarihi ve raporlama para birimi bağlamı
- Görünüm ve Paylaş/Çıktı menüsü
- Tek ana işlem düğmesi
- Sayfaya özel AI asistan hızlı aksiyonları
- Liste satırında sağ detay çekmecesi
- Kritik işlemde tam ekran workspace

---

## F-01 — Finans Genel Bakış

**Amaç:** Yönetimin ve finans ekibinin nakit, yakın dönem tahsilat/ödeme, borç, limit ve kontrol durumunu tek bakışta görmesi.

**Birincil roller:** Genel Müdür, CFO/Finans Müdürü, Hazine Yöneticisi, Finans Operasyon.

**Ana işlem:** `+ Yeni Finans İşlemi`

**Ana içerik:**

- Altı ana KPI
- Üç ikincil hızlı gösterge
- 13 haftalık nakit akışı
- Yaklaşan tahsilatlar ve ödemeler
- Banka hesapları
- Kredi ve limit özeti
- Yaklaşan teminat/banka yükümlülükleri
- Nakit bütçesi ve kontrol kuyruğu

**Kullanılabilirlik kuralı:** Dashboard işlem kayıt sayfası değildir. Kart veya satır tıklandığında ilgili filtreli sayfa açılır.

**Boş durum:** “Bu görünüm için finansal veri bulunamadı” + tarih/şirket filtresi önerisi.

**Hata durumu:** Güncel olmayan banka ekstresi, eski kur veya eksik veri kart seviyesinde işaretlenir; rakamın güvenilirliği gizlenmez.

**Responsive:** Masaüstünde 6 KPI; tablet 3×2; telefon 1–2 kolon. Ana nakit grafiği ilk sırada kalır.

---

## F-02 — Tahsilat / Ödeme

**Amaç:** Gerçek banka/kasa hareketini cari hesaba kaydetmek ve finansal kaynağın dağıtım durumunu izlemek.

**Birincil roller:** Finans Operasyon, Kasa Sorumlusu, Finans Kontrolörü.

**Ana işlem:** `+ Tahsilat / Ödeme Kaydı`

**Sekmeler:**

- Tümü
- Gelen / Tahsilat
- Giden / Ödeme
- Dağıtılmamış
- Kontrol Bekleyen
- Terslenen

**Liste kolonları:** İşlem no, tarih, valör, cari/karşı taraf, banka/kasa, yön, tür, para birimi, brüt/net tutar, dağıtılan, kalan, durum, kaynak kanal.

**Satır detay çekmecesi:**

- Finansal kaynak özeti
- Banka/kasa etkisi
- Cari etkisi
- Bağlı dağıtımlar
- Belge ve audit
- Ters kayıt/revizyon geçmişi

**Ana iş kuralı:** Kayıt onayında banka/kasa ve cari etkisi oluşur; fatura/sipariş dağıtımı bu ekranda zorunlu değildir.

**Boş durum:** “Henüz finansal kaynak yok” + dekont yükle / manuel kayıt seçenekleri.

**Hata durumları:** Mükerrer banka referansı, kapalı dönem, yetkisiz hesap, valör tarihi hatası, bilinmeyen cari.

---

## F-03 — Açık Kalemler ve Finansal Dağıtım

**Amaç:** Banka, kasa, çek/senet, POS, kart, DBS, avans ve mahsup kaynaklarını açık fatura, taksit ve siparişlere çoktan çoğa dağıtmak.

**Birincil roller:** Finans Operasyon, Finans Kontrolörü.

**Ana işlem:** `+ Finansal Dağıtım`

**Sekmeler:**

- Dağıtım Bekleyen Kaynaklar
- Kısmi Dağıtılanlar
- Tam Dağıtılanlar
- Açık Faturalar
- Sipariş Avansları
- Dağıtım Geçmişi

**Üç bölümlü workspace:**

1. Sol: finansal kaynak ve kalan tutar
2. Orta: açık kalemler/taksitler/siparişler
3. Sağ: dağıtım sepeti, vade/valör ve etki özeti

**Dağıtım hedefleri:** Açık fatura, fatura taksiti, satış siparişi avansı, alış siparişi avansı, cari avans, mahsup, bilgi bağlantısı.

**Özel metrikler:** Ödeme gecikmesi, araç vade farkı, valör farkı, iç dağıtım süresi.

**Engeller:** Kaynak kalanını aşma, açık kalemi aşma, para birimi/kur yöntemi eksikliği, terslenen kaynağa dağıtım, teminatın fatura kapaması için kullanılması.

---

## F-04 — Tahsilat Takibi ve Cari Risk

**Amaç:** Vadesi geçmiş alacakları, ödeme sözlerini, tahsilat görevlerini, ihtilafları, yapılandırmayı, hukuki takibi ve risk istisnalarını tek merkezde yönetmek.

**Birincil roller:** Tahsilat Sorumlusu, Finans Müdürü, Satış Sorumlusu, Hukuk, Yönetim.

**Ana işlem:** `+ Tahsilat Görevi Oluştur`

**Sekmeler:**

- Vadesi Geçmiş
- Ödeme Sözleri
- Tahsilat Görevleri
- İhtilaf / Yapılandırma
- Hukuki Takip
- Risk / Limit

**Vadesi geçmiş liste kolonları:** Cari, açık kalem sayısı, gecikmiş tutar, ortalama gecikme, söz verilen, risk azaltıcı, net risk, önem.

**Sağ özet:** Bugün aranacak cariler, bugünkü ödeme sözleri, bozulan sözler, teminatsız net risk.

**Temel işlemler:** Görev oluştur, ödeme sözü kaydet, ihtilaf aç, yapılandır, hukuk devri, risk/sevkiyat istisnası.

**Kural:** Tahsilat görevi veya ödeme sözü cari hareketi ve fatura kapaması oluşturmaz; yalnız takip ve tahmin güvenini etkiler.

---

## F-05 — Banka / Kasa

**Amaç:** Banka hesapları, ERP ve banka bakiyeleri, hareketler, ekstre, mutabakat ve kasayı tek görev merkezinde yönetmek.

**Birincil roller:** Finans Operasyon, Hazine, Kasa Sorumlusu, Mutabakat Personeli.

**Ana işlem:** Sekmeye göre `Ekstre İçe Aktar`, `Mutabakat Yap`, `Kasa Hareketi`.

**Sekmeler:**

- Banka Hesapları
- Banka Hareketleri
- Ekstre / Mutabakat
- Kasa

**Banka hesabı satırı:** Banka/hesap, para birimi, ERP bakiye, ekstre bakiye, valörlü/kullanılabilir, bloke, mutabakatsız tutar, güncellik.

**Mutabakat modelleri:** 1↔1, 1↔çok, çok↔1, çok↔çok.

**Kasa işlevleri:** Tahsilat, ödeme, virman, kasa sayımı, sayım farkı ve gün sonu.

**Kural:** İçe aktarılan banka ekstresi satırı değiştirilmez; yalnız sınıflandırılır/eşleştirilir veya eşleşmesi çözülür.

---

## F-06 — Ödeme Yönetimi

**Amaç:** Açık tedarikçi kalemlerinden ödeme önerisi, toplu paket, banka talimatı, onay ve banka sonucuna kadar kontrollü ödeme akışı kurmak.

**Birincil roller:** Finans Operasyon, Finans Kontrolörü, Hazine, Finans Müdürü, Yönetim.

**Ana işlem:** `+ Ödeme Önerisi Oluştur`

**Sekmeler:**

- Ödeme İhtiyaçları
- Öneriler
- Paketler
- Talimatlar
- Onaylar
- Banka Sonuçları

**Ödeme seçim ölçütleri:** Vade, gecikme, tedarikçi önceliği, üretime etkisi, erken ödeme avantajı, bütçe, kullanılabilir nakit, banka masrafı ve valör.

**Görevler ayrılığı:** Hazırlayan, kontrol eden, onaylayan, bankada uygulayan ve mutabakat yapan ayrıdır.

**Kritik kontroller:** IBAN doğrulama, mükerrer ödeme, bütçe/likidite, yetki limiti, kapalı dönem.

---

## F-07 — Ödeme Araçları

**Amaç:** Çek/senet, DBS, POS ve şirket kredi kartlarını ortak kabukta; araç türüne özel kapama, nakitleşme ve risk davranışıyla yönetmek.

**Birincil roller:** Finans Operasyon, Hazine, Tahsilat, Finans Kontrolörü.

**Ana işlem:** Sekmeye göre `Çek/Senet Ekle`, `DBS İşlemi`, `POS Slip`, `Kart Harcaması`.

**Sekmeler:**

- Çek / Senet
- DBS
- POS
- Şirket Kredi Kartı
- Araç Sonuçları

**Durum eksenleri:**

- Ticari kapama
- Nakitleşme
- Risk
- Fiziksel/operasyonel durum

**Özel işlemler:** Ciro, bankaya tahsil/teminat, karşılıksız/iade/protesto, DBS kabul/red/tahsilat, POS batch/chargeback, kart ekstresi/ödeme.

**Kapsam dışı:** Tedarikçi POS.

---

## F-08 — Nakit Planı

**Amaç:** İlk 30 gün günlük, devamı haftalık olmak üzere 13 haftalık likidite görünümü ve sade stres senaryosu sunmak.

**Birincil roller:** Hazine, CFO/Finans Müdürü, Genel Müdür.

**Ana işlem:** `+ Manuel Nakit Olayı`

**Görünümler:** Günlük, haftalık, detay; baz ve basit stres senaryosu.

**Kaynaklar:** Açık faturalar, çek/senet, DBS, POS, kredi taksitleri, kart ekstresi, ödeme önerileri, vergi/maaş/enerji planı ve nakit bütçesi.

**Ana grafik:** Nakit girişi, nakit çıkışı, tahmini dönem sonu nakit, minimum tampon.

**Kural:** Aynı olay sipariş→fatura→araç→banka zincirinde yalnız en güncel/öncelikli kaynakla hesaba katılır.

**Ek işlem:** Nakit açığı eylem planı; gerçek kaydı değiştirmeden limit, kredi, tahsilat ve ödeme erteleme senaryosu oluşturur.

---

## F-09 — Krediler ve Limitler

**Amaç:** İlk sürümde sade banka ilişkisi, kredi, limit ve ödeme takvimi sunmak; ileri facility altyapısını korumak.

**Birincil roller:** Hazine, Finans Müdürü, CFO/Yönetim.

**Ana işlem:** `+ Yeni Kredi`

**Sekmeler:**

- Banka Özeti
- Krediler
- Limitler
- Ödeme Takvimi
- Diğer Finansman

**Kredi kartı alanları:** Banka, tür, sözleşme no, brüt/net kullandırım, para birimi, tarih/vade, faiz, kalan anapara, ödeme planı, bağlı limit/teminat ve belge.

**Limit alanları:** Etkin limit, kullanılan, rezerve, bloke, kullanılabilir, başlangıç/bitiş, limit türü ve üst facility ilişkisi.

**İşlemler:** Kredi kaydı, plan içe aktar, ödeme/erken kapama/yapılandırma, yeni limit, limit yenileme, kredi teklif karşılaştırma, diğer finansman.

**Sadeleştirme:** Facility ağacı varsayılan arayüzde düz görünür; veri ilişkisi arka planda korunur.

---

## F-10 — Teminatlar ve Yükümlülükler

**Amaç:** Verilen/alınan teminat envanteri, değerleme, tahsis, serbest değer ve banka yükümlülüklerini yönetmek.

**Birincil roller:** Hazine, Finans Müdürü, Hukuk, Yönetim.

**Ana işlem:** `+ Yeni Teminat`

**Sekmeler:**

- Teminat Envanteri
- Tahsisler
- Değerlemeler
- Teminat Mektupları
- Banka Yükümlülükleri

**Teminat değerleri:** Nominal, güncel, bankaca kabul edilen, tahsisli, serbest.

**İşlemler:** Yeni teminat, değerleme güncelleme, tahsis, çözüm/serbest bırakma, teminat mektubu, banka yükümlülüğü/covenant görevi.

**Kural:** Aktif riske bağlı teminat çözümü etki raporu ve yetkili onay olmadan tamamlanmaz.

---

## F-11 — Nakit Bütçesi

**Amaç:** İlk sürümde aylık nakit giriş/çıkış planı, gerçekleşen, güncel tahmin ve sapmayı izlemek.

**Birincil roller:** Bütçe Sorumlusu, Finans Müdürü, Hazine, Yönetim.

**Ana işlem:** `Bütçeyi Düzenle`

**Sekmeler:**

- Bütçe Özeti
- Bütçe Girişi
- Transfer / Revizyon
- Sürümler

**Temel boyutlar:** Şirket, dönem, kategori, yön, para birimi. Departman, masraf merkezi, proje ve ürün grubu altyapıda hazırdır fakat ilk görünümde zorunlu değildir.

**Sürüm kuralı:** Onaylı bütçe üzerine yazılmaz; yeni sürüm veya transfer hareketi oluşturulur.

**Gerçekleşme:** Yalnız onaylı banka/kasa ve sonuçlanmış finansal araç hareketlerinden hesaplanır; fatura kaydı nakit bütçesi gerçekleşmesi sayılmaz.

---

## F-12 — Finans Kontrol Merkezi

**Amaç:** Onay, mutabakat, vade, limit, teminat, mükerrerlik ve dönem kapama istisnalarını tek kuyrukta toplamak.

**Birincil roller:** Finans Kontrolörü, Finans Müdürü, CFO/Yönetim, Denetçi.

**Ana işlem:** `+ Kontrol Oluştur`

**Sekmeler:**

- Tümü
- Onaylar
- Mutabakat
- Vade / Yükümlülük
- Limit / Teminat
- Dönem Kapama

**Önem seviyeleri:** Kritik, yüksek, orta, bilgi.

**Kontrol kaydı:** Kaynak, finansal etki, sorumlu, son tarih, önerilen işlem, durum, çözüm notu ve audit.

**Kapanış:** Gün sonu, ay sonu/dönem kilidi ve yetkili yeniden açma ayrı işlemlerdir.

---

## F-13 — Finans Raporları

**Amaç:** Yönetim ve operasyon raporlarını kaynağa drill-down, kayıtlı görünüm ve planlı dağıtımla sunmak.

**Birincil roller:** Yönetim, Finans Müdürü, Hazine, Finans Operasyon, Denetçi.

**Ana işlem:** `Rapor Oluştur`

**Rapor grupları:**

- Yönetim
- Nakit / Likidite
- Tahsilat / Açık Kalem
- Banka / Kasa
- Kredi / Limit / Teminat
- Bütçe / Kontrol

**Rapor tasarımı:** Şirket, tarih, para birimi, ana kırılım, kolonlar, filtreler, ekran/PDF/Excel ve planlı dağıtım.

**Kural:** Her toplam kaynak kayda drill-down olmalıdır. Rapor snapshot’ı gerektiğinde korunur.

**Kapsam dışı:** Resmî muhasebe ve vergi raporları.
