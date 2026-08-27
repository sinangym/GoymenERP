# Göymen ERP Finans v4 — AI Kullanım Matrisi

## 1. AI çalışma ilkesi

Her AI çıktısı şu zinciri izler:

```text
Kaynak veriler
→ AI analizi / önerisi
→ Neden ve güven oranı
→ Finansal etki önizlemesi
→ Kullanıcı kontrolü
→ Yetkili onay
```

AI doğrudan kesin kayıt oluşturmaz.

---

## 2. Sayfa bazında AI matrisi

| Ekran | AI dikkat noktası | Hızlı aksiyon 1 | Hızlı aksiyon 2 | Ana kaynaklar |
|---|---|---|---|---|
| Genel Bakış | Nakit tamponu ve günlük iş önceliği | Bugünkü işleri sırala | Açığı kapatma seçenekleri | Nakit planı, kredi, tahsilat |
| Tahsilat / Ödeme | Belirsiz cari ve mükerrerlik | Mükerrerleri kontrol et | Carisi belirsizi eşleştir | Ekstre, cari, dekont hash |
| Açık Kalem / Dağıtım | Yüksek güvenli dağıtım önerisi | Dağıtım öner | Vade ve valörü açıkla | Kaynak, açık kalem, vade |
| Tahsilat / Risk | Bozulma riski taşıyan sözler | Bugünün arama listesi | Görüşme taslağı | Açık kalem, söz, teminat |
| Banka / Kasa | Eşleşmeyen banka satırları | Eşleşme öner | Masraf kontrolü | Ekstre, ERP hareketi, tarife |
| Ödeme Yönetimi | Onay ve IBAN riski | Ödeme önceliği | Banka hesabı öner | Öneri, bütçe, bakiye, IBAN audit |
| Ödeme Araçları | Vade ve garanti dışı risk | Araç vade sırası | DBS garanti dışı | Portföy, banka kabulü, limit |
| Nakit Planı | Tampon altına düşüş | Stres senaryosu | Fonlama sırası | Nakit olayları, kredi, bütçe |
| Krediler / Limitler | Borç servisi ve belge eksiği | Borç servisini açıkla | Limit boşluğu karşılaştır | Kredi planı, limit, belge |
| Teminatlar | Teminat açığı ve yaklaşan görev | Teminat boşluğu | Görevleri sırala | Tahsis, risk, takvim |
| Nakit Bütçesi | Kategori sapması | Sapmayı açıkla | Revize tahmin | Bütçe, RF, banka/kasa |
| Kontroller | Kapanışı engelleyen kayıtlar | Engelleri bul | Kök nedeni açıkla | Kontrol kuyruğu, kapanış, audit |
| Raporlar | Veri güncelliği ve karar özeti | Yönetim özeti | Tahsilat performansı | Rapor kaynakları ve güncellik |

---

## 3. AI öneri kartının zorunlu alanları

- Öneri başlığı
- Kısa sonuç
- Neden bu öneri?
- Kullanılan kaynaklar
- Kaynakların güncelliği
- Güven oranı
- Etkilenecek tutar ve kayıt sayısı
- Kullanıcı tarafından açılabilecek kaynak ekran
- İşlem önizlemesi
- Otomatik kesinleştirmeme uyarısı

---

## 4. İşlem workspace AI kontrolü

Bütün işlem workspacelerinde AI şu kontrolleri yapabilir:

- Zorunlu alan kontrolü
- Alan biçimi ve tutar kontrolü
- Cari / VKN / unvan / IBAN uyumu
- Banka hesabı ve para birimi kontrolü
- Mükerrer işlem kontrolü
- Belge / referans / hash kontrolü
- Vade ve valör açıklaması
- Dağıtım toplamı kontrolü
- Bütçe / limit / teminat etkisi
- Onay gereksinimi

AI’nın `uygun` demesi nihai onay değildir.

---

## 5. Açıklanabilirlik ve audit

Her AI çıktısında saklanması gereken asgari bilgiler:

- Kullanıcı
- Şirket
- Sayfa ve seçili kayıt bağlamı
- AI aksiyonu
- Kullanılan kaynak kimlikleri
- Model/sürüm
- Güven oranı
- Üretilen öneri
- Kullanıcının kabul/değişiklik/red kararı
- Kesinleşen işlemin audit kimliği

---

## 6. Yasaklanan AI davranışları

- Ham SQL üretip çalıştırma
- Onaysız banka talimatı
- Onaysız ödeme/tahsilat
- Onaysız fatura kapama
- Onaysız kredi kullanımı
- Onaysız teminat çözümü
- Kesinleşmiş kaydı silme
- Resmî muhasebe veya vergi kaydını otomatik kesinleştirme
