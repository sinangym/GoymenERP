# Göymen ERP Finans v4 — Uygulama ve Kabul Kriterleri

## 1. Geliştirme sırası

### Faz 1 — Ortak kabuk ve AI standardı

- Açılır finans menü grupları
- Rol görünümü
- Sade / Ayrıntılı görünüm
- Sayfa başlığı ve AI dikkat noktası
- AI sonuç paneli
- Sayfa kılavuzu
- Ortak workspace AI kontrolü

### Faz 2 — Günlük finans

- Tahsilat / ödeme
- Açık kalem ve dağıtım
- Banka / kasa
- Tahsilat takibi
- Ödeme yönetimi
- Ödeme araçları
- Kontrol merkezi

### Faz 3 — Sade hazine

- 13 haftalık nakit planı
- Krediler ve limitler
- Teminatlar
- Nakit bütçesi
- Raporlar

---

## 2. Kullanıcı deneyimi kabul kriterleri

- Kullanıcı ilk beş saniyede bugünkü görevlerini görür.
- Her sayfada yalnız bir baskın ana işlem bulunur.
- Her sayfada AI dikkat noktası görünür.
- AI en az iki bağlamsal aksiyon sunar.
- AI önerisi kaynak ve güven oranı olmadan gösterilmez.
- Sade görünüm varsayılandır.
- Ayrıntılı görünüm veri kaybı olmadan açılır.
- Aktif finans menü grubu otomatik açılır.
- Menü araması ekranları filtreler.
- Sayfa kılavuzu bulunur.
- İşlem workspace’inde AI kontrol kartı bulunur.
- Aynı AI kontrol kartı iki kez gösterilmez.

---

## 3. Finansal doğruluk kabul kriterleri

- Tahsilat/ödeme kaydı açık kalemi otomatik kapatmaz.
- Dağıtım ikinci banka veya cari hareketi oluşturmaz.
- Vade, işlem, valör ve dağıtım tarihleri ayrı kalır.
- Çek/senet, DBS, POS ve kart için kapama/nakitleşme/risk ayrımı korunur.
- Bütçe ödeme anında ikinci kez tüketilmez.
- Kredi kullanımı ile limit tahsisi ayrıdır.
- Teminat nominal ve bankaca kabul değeri ayrıdır.
- Kesin kayıt silme işlemi bulunmaz.

---

## 4. AI güvenlik kabul kriterleri

- AI hiçbir ödeme veya tahsilatı kesinleştiremez.
- AI hiçbir banka talimatını gönderemez.
- AI hiçbir kredi kullanımını başlatamaz.
- AI hiçbir teminatı çözemez.
- AI hiçbir bütçe transferini onaylayamaz.
- AI çıktısı kullanıcı kararıyla audit’e bağlanır.
- AI önerisi reddedilebilir veya değiştirilebilir.

---

## 5. Teknik kabul kriterleri

- 13 ana ekran hatasız açılır.
- Her ana ekranda bir AI paneli bulunur.
- 73 işlem workspace’i açılır.
- Her workspace’te tek AI kontrol kartı bulunur.
- JavaScript çalışma hatası yoktur.
- Yinelenen HTML kimliği yoktur.
- Mobil görünümde yatay sayfa taşması yoktur.
- Klavye kısayolları çalışır.
- Responsive görünüm 390 px, 768 px, 1024 px ve 1440+ px seviyelerinde kullanılabilir olur.
