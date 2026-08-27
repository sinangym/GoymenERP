# Göymen ERP Finans v4 — Ekran ve Navigasyon Mimarisi

## 1. Sol menü mimarisi

```text
Finans & Hazine
├─ Genel Bakış
├─ Günlük İşlemler
│  ├─ Tahsilat / Ödeme
│  └─ Açık Kalemler ve Dağıtım
├─ Tahsilat & Ödeme
│  ├─ Tahsilat Takibi / Cari Risk
│  └─ Ödeme Yönetimi
├─ Banka & Araçlar
│  ├─ Banka / Kasa
│  └─ Ödeme Araçları
├─ Hazine
│  ├─ Nakit Planı
│  ├─ Krediler ve Limitler
│  ├─ Teminatlar / Yükümlülükler
│  └─ Nakit Bütçesi
└─ Kontrol & Rapor
   ├─ Finans Kontrol Merkezi
   └─ Finans Raporları
```

Menü grupları açılır/kapanır. Aktif ekranın bulunduğu grup otomatik açılır. Menü araması ekran adlarını anlık filtreler.

---

## 2. Global görünüm seçenekleri

### Rol görünümü

- Benim İşlerim
- Yönetim
- Hazine
- Finans Kontrol

Rol görünümü veri yetkisini değiştirmez; kullanıcının varsayılan ekran ve öncelik sırasını değiştirir.

### Yoğunluk görünümü

- **Sade:** Günlük karar ve işlem alanları
- **Ayrıntılı:** Ek KPI, kolon ve gelişmiş alanlar

Veri modeli her iki görünümde aynıdır.

---

## 3. Ana ekran kataloğu

| Kod | Ekran | Varsayılan görünüm | Ana işlem | AI’nin ana rolü |
|---|---|---|---|---|
| F-01 | Finans Genel Bakış | Benim İşlerim | Yeni Finans İşlemi | Günlük işleri ve nakit riskini önceliklendirme |
| F-02 | Tahsilat / Ödeme | Onaylı + bekleyen kaynak listesi | Yeni Finans İşlemi | Mükerrerlik ve cari eşleşme |
| F-03 | Açık Kalemler ve Dağıtım | Dağıtım kuyruğu | Dağıtım Workspace | Açık kalem eşleşmesi, vade ve valör açıklaması |
| F-04 | Tahsilat Takibi / Cari Risk | Vadesi geçmiş cariler | Tahsilat Görevi | Tahsilat önceliği ve görüşme hazırlığı |
| F-05 | Banka / Kasa | Hesaplar ve güncellik | Ekstre Yükle | Ekstre eşleştirme ve banka masrafı kontrolü |
| F-06 | Ödeme Yönetimi | Ödeme önerileri | Ödeme Önerisi | Öncelik ve banka hesabı önerisi |
| F-07 | Ödeme Araçları | Çek/Senet | Yeni Finansal Araç | Araç vade, garanti ve nakitleşme riski |
| F-08 | Nakit Planı | Baz senaryo | Yeni Senaryo | Stres testi ve fonlama seçenekleri |
| F-09 | Krediler ve Limitler | Banka özeti | Yeni Kredi | Borç servisi ve operasyonel limit açıklaması |
| F-10 | Teminatlar / Yükümlülükler | Teminat envanteri | Yeni Teminat | Teminat boşluğu ve yaklaşan görevler |
| F-11 | Nakit Bütçesi | Onaylı bütçe + RF | Bütçe Satırı | Sapma ve revize tahmin önerisi |
| F-12 | Finans Kontrol Merkezi | Açık istisnalar | Gün Sonu Kontrolü | Kapanış engeli ve kök neden analizi |
| F-13 | Finans Raporları | Yönetim raporları | Rapor Oluştur | Yönetim özeti ve açıklanabilir rapor |

---

## 4. F-01 Finans Genel Bakış

### Benim İşlerim görünümü

- 4 kompakt günlük gösterge
- 4 öncelikli görev kartı
- 13 haftalık nakit görünümü
- Bugünün takvimi
- AI önceliklendirme listesi

### Yönetim Özeti görünümü

- Sade modda 4 ana KPI
- Ayrıntılı modda 6 ana KPI + 3 ikincil gösterge
- 13 haftalık nakit akışı
- Yaklaşan tahsilat ve ödeme
- Banka hesapları
- Kredi/limit özeti
- Teminat/yükümlülük
- Nakit bütçesi
- Kontrol/istisna

---

## 5. F-03 Açık Kalemler ve Dağıtım

### Sade görünüm

- Dağıtım bekleyen kaynak tablosu
- Seçili kaynak özeti
- AI dağıtım önerisi
- Vade/valör özeti
- `Dağıtımı Aç ve Kontrol Et`

### Ayrıntılı görünüm

- Sol: finansal kaynaklar
- Orta: açık kalemler ve dağıtım tutarı
- Sağ: dağıtım sepeti ve etki

### Kesin işlem

Tam ekran dağıtım workspace’inde yapılır.

---

## 6. Ortak sayfa şablonu

1. Breadcrumb
2. Sayfa başlığı ve tek cümlelik amaç
3. Kılavuz / paylaşım / ana işlem
4. AI dikkat noktası
5. Sekme veya görev filtresi
6. Gerekirse kompakt KPI
7. Ana tablo veya karar paneli
8. Sağ detay çekmecesi
9. Tam ekran işlem workspace’i

---

## 7. Responsive davranış

### Masaüstü

- Tam sol menü
- Çok kolonlu kartlar
- Sağ AI çekmecesi
- Tam ekran workspace

### Tablet

- Daraltılmış menü
- İki kolon
- AI overlay

### Mobil

- Sol menü overlay
- Tek kolon görev ve kart düzeni
- AI sayfa genişliğine yakın çekmece
- Workspace tam ekran
- Tablo kontrollü yatay kaydırma
