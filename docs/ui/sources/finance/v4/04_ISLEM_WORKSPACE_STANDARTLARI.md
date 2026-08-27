# Göymen ERP Finans v4 — İşlem Workspace Standartları

## 1. Kapsam

v3’te bulunan 73 işlem workspace’i korunmuştur. v4, bütün işlemlere aynı kullanıcı deneyimi ve AI kontrol standardını ekler.

---

## 2. Ortak ekran sırası

```text
İşlem bağlamı
→ Temel bilgiler
→ Taraf / hesap / araç ilişkileri
→ Tutar ve tarihler
→ Belge ve açıklama
→ Dağıtım / bağlantı
→ Finansal etki
→ AI kontrolü
→ Yetki / onay
```

---

## 3. Sabit alt işlem çubuğu

- Vazgeç
- Taslak Kaydet
- AI ile Kontrol Et
- Kontrole Gönder

Nihai onay yetkisi olmayan kullanıcıda `Kesinleştir` gösterilmez.

---

## 4. Sağ panel standardı

### AI İşlem Kontrolü

- Alan doğrulama
- İlişki doğrulama
- Mükerrerlik
- Vade/valör/risk uyarısı
- Güven oranı
- Açıklama

### Finansal Etki

İşleme göre:

- Banka/kasa etkisi
- Cari etkisi
- Dağıtılmamış kaynak
- Açık kalem kapama
- Nakit akışı
- Limit kullanımı
- Teminat serbest değeri
- Bütçe sapması
- Onay seviyesi

---

## 5. Sade / ayrıntılı alan davranışı

### Sade görünüm

- Zorunlu ve günlük alanlar
- Tek açıklama alanı
- Temel belge yükleme
- Temel etki

### Ayrıntılı görünüm

- Facility / üst limit
- Referans faiz / spread
- Kur kaynağı
- Covenant bağlantısı
- Çoklu değerleme
- İleri tahsis
- Entegrasyon kimliği
- Teknik revizyon alanları

---

## 6. Kritik işlem örnekleri

### Tahsilat / ödeme kaydı

AI kontrolü:

- Cari eşleşmesi
- Banka hesabı
- Mükerrerlik
- Valör farkı

Etki:

- Banka/kasa
- Cari tahsilat/ödeme
- Dağıtılmamış finansal kaynak
- Fatura kapamasının henüz oluşmaması

### Finansal dağıtım

AI kontrolü:

- Kaynak toplamı aşımı
- Açık kalem tutarı
- Vade ve ödeme tarihi
- Etki türü

Etki:

- Ticari kapama
- Sipariş avansı
- Kalan kaynak
- Yeni banka/cari hareketi oluşmaması

### Ödeme önerisi

AI kontrolü:

- Likidite tamponu
- Bütçe
- IBAN değişikliği
- Mükerrer ödeme
- Banka maliyeti

### Kredi kaydı

AI kontrolü:

- Ödeme planı toplamı
- Limit yeterliliği
- Belge ve teminat koşulları
- 13 haftalık nakit etkisi

### Teminat tahsisi

AI kontrolü:

- Bankaca kabul değeri
- Aktif risk
- Tahsis toplamı
- Serbest değer
- Sigorta/ekspertiz tarihi

### Bütçe işlemi

AI kontrolü:

- Sürüm ve dönem
- Plan/tahmin farkı
- Çifte bütçe tüketimi
- Onay gereksinimi

---

## 7. Değişmez finans kuralları

- Kaynak ile dağıtım ayrı işlemdir.
- Dağıtım ikinci banka/cari hareketi oluşturmaz.
- Kesin kayıt silinmez.
- Ters kayıt ve revizyon geçmişi korunur.
- Siparişe dağıtım avanstır.
- POS brüt kapama, net banka ve komisyon ayrı izlenir.
- DBS tahsilata kadar kapsamdır.
- Çek/senet kapama, nakitleşme ve risk ayrı durumdur.
