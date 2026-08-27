# Göymen ERP Finans — İşlem Workspace Kataloğu

Bu belge, “Yeni Finans İşlemi” kataloğundaki **73 işlemin** ekran tasarımını tanımlar. Her işlemde amaç, adımlar, temel alanlar, finansal etki, kontroller ve nihai aksiyon ayrı gösterilir. Gerçek uygulamada alan zorunluluğu, veri tipi, varsayılan değer, yetki ve revizyon kuralı API sözleşmesine aynen taşınmalıdır.

## Ortak workspace standardı

- Tam ekran açılış ve kayıt bağlamı
- 3–5 adımlı ilerleme göstergesi
- Temel Bilgiler
- İlişkiler ve Belgeler
- Sağda Etki Önizlemesi
- Sağda Kontrol Listesi
- Taslak Kaydet / Onaya Gönder sabit alt çubuğu
- Kaydedilmemiş değişiklik uyarısı
- Kritik işlemde ikinci onay ve audit

## Günlük Finans

### Tahsilat / Ödeme Kaydı

**Kod:** `transaction`  
**Sınıf:** Sık kullanılan  
**Amaç:** Gerçek banka veya kasa hareketini cari hesaba kaydedin; dağıtımı ayrı bırakın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| İşlem Yönü | select | Evet | Gelen / Tahsilat / Giden / Ödeme |
| İşlem Türü | select | Evet | Banka / Kasa / İade / Virman / Diğer |
| Cari / Karşı Taraf | text | Evet | Körfez İnşaat A.Ş. |
| Banka / Kasa Hesabı | select | Evet | Ziraat · TL 1234 / İş Bankası · TL 5678 / Merkez Kasa |
| Tutar | amount | Evet | 500.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-25 |
| Banka Referansı | text | Hayır | TRX-983741 |

**Etki önizlemesi:**

- **Banka/Kasa Etkisi:** İşlem yönüne göre oluşur
- **Cari Etkisi:** Onayda oluşur
- **Açık Kalem Kapama:** Dağıtımda oluşur
- **Nakit Akışı:** Gerçekleşmiş hareket

**Kontrol listesi:**

- [ ] Şirket ve cari aktif
- [ ] Mükerrerlik anahtarı kontrol edildi
- [ ] Banka/kasa hesabı yetkili
- [ ] Tutar ve para birimi geçerli

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Dekont / Belge Yükleme

**Kod:** `receiptUpload`  
**Sınıf:** Sık kullanılan  
**Amaç:** PDF veya fotoğraftan banka dekontunu okuyup kontrollü taslak oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| İşlem Yönü | select | Evet | Gelen / Tahsilat / Giden / Ödeme |
| İşlem Türü | select | Evet | Banka / Kasa / İade / Virman / Diğer |
| Cari / Karşı Taraf | text | Evet | Körfez İnşaat A.Ş. |
| Banka / Kasa Hesabı | select | Evet | Ziraat · TL 1234 / İş Bankası · TL 5678 / Merkez Kasa |
| Tutar | amount | Evet | 500.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-25 |
| Banka Referansı | text | Hayır | TRX-983741 |
| Belge Kaynağı | select | Evet | PDF / Fotoğraf / E-posta Eki / Tarayıcı |
| Mükerrerlik Anahtarı | text | Hayır | Banka + Tutar + Tarih + Referans |

**Etki önizlemesi:**

- **Banka/Kasa Etkisi:** İşlem yönüne göre oluşur
- **Cari Etkisi:** Onayda oluşur
- **Açık Kalem Kapama:** Dağıtımda oluşur
- **Nakit Akışı:** Gerçekleşmiş hareket

**Kontrol listesi:**

- [ ] Şirket ve cari aktif
- [ ] Mükerrerlik anahtarı kontrol edildi
- [ ] Banka/kasa hesabı yetkili
- [ ] Tutar ve para birimi geçerli

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Sınıflandırılmamış Hareketi Eşleştir

**Kod:** `classifyMovement`  
**Sınıf:** Sık kullanılan  
**Amaç:** Carisi veya işlem türü belli olmayan banka hareketini doğru tarafa bağlayın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| İşlem Yönü | select | Evet | Gelen / Tahsilat / Giden / Ödeme |
| İşlem Türü | select | Evet | Banka / Kasa / İade / Virman / Diğer |
| Cari / Karşı Taraf | text | Evet | Körfez İnşaat A.Ş. |
| Banka / Kasa Hesabı | select | Evet | Ziraat · TL 1234 / İş Bankası · TL 5678 / Merkez Kasa |
| Tutar | amount | Evet | 500.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-25 |
| Banka Referansı | text | Hayır | TRX-983741 |
| İçe Aktarılan Satır | text | Evet | TRX-983741 |
| Eşleşme Güveni | text | Hayır | %86 |

**Etki önizlemesi:**

- **Banka/Kasa Etkisi:** İşlem yönüne göre oluşur
- **Cari Etkisi:** Onayda oluşur
- **Açık Kalem Kapama:** Dağıtımda oluşur
- **Nakit Akışı:** Gerçekleşmiş hareket

**Kontrol listesi:**

- [ ] Şirket ve cari aktif
- [ ] Mükerrerlik anahtarı kontrol edildi
- [ ] Banka/kasa hesabı yetkili
- [ ] Tutar ve para birimi geçerli

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Müşteri / Tedarikçi İadesi

**Kod:** `refund`  
**Sınıf:** Standart  
**Amaç:** Fazla ödeme, avans veya iptal sonrası kontrollü geri ödeme yapın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| İşlem Yönü | select | Evet | Gelen / Tahsilat / Giden / Ödeme |
| İşlem Türü | select | Evet | Banka / Kasa / İade / Virman / Diğer |
| Cari / Karşı Taraf | text | Evet | Körfez İnşaat A.Ş. |
| Banka / Kasa Hesabı | select | Evet | Ziraat · TL 1234 / İş Bankası · TL 5678 / Merkez Kasa |
| Tutar | amount | Evet | 500.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-25 |
| Banka Referansı | text | Hayır | TRX-983741 |
| İade Nedeni | select | Evet | Fazla ödeme / Sipariş iptali / Yanlış tahsilat / Diğer |
| Bağlı Finansal Kaynak | text | Evet | BNK-2026-00851 |

**Etki önizlemesi:**

- **Banka/Kasa Etkisi:** İşlem yönüne göre oluşur
- **Cari Etkisi:** Onayda oluşur
- **Açık Kalem Kapama:** Dağıtımda oluşur
- **Nakit Akışı:** Gerçekleşmiş hareket

**Kontrol listesi:**

- [ ] Şirket ve cari aktif
- [ ] Mükerrerlik anahtarı kontrol edildi
- [ ] Banka/kasa hesabı yetkili
- [ ] Tutar ve para birimi geçerli

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Ters Kayıt / Düzeltme

**Kod:** `reverseSource`  
**Sınıf:** Standart  
**Amaç:** Kesinleşmiş finansal kaydı silmeden etkilerini tersleyip doğru kaydı oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| İşlem Yönü | select | Evet | Gelen / Tahsilat / Giden / Ödeme |
| İşlem Türü | select | Evet | Banka / Kasa / İade / Virman / Diğer |
| Cari / Karşı Taraf | text | Evet | Körfez İnşaat A.Ş. |
| Banka / Kasa Hesabı | select | Evet | Ziraat · TL 1234 / İş Bankası · TL 5678 / Merkez Kasa |
| Tutar | amount | Evet | 500.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-25 |
| Banka Referansı | text | Hayır | TRX-983741 |
| Terslenecek Kayıt | text | Evet | BNK-2026-00851 |
| Düzeltme Gerekçesi | textarea | Evet | Yanlış cari seçimi. |

**Etki önizlemesi:**

- **Banka/Kasa Etkisi:** İşlem yönüne göre oluşur
- **Cari Etkisi:** Onayda oluşur
- **Açık Kalem Kapama:** Dağıtımda oluşur
- **Nakit Akışı:** Gerçekleşmiş hareket

**Kontrol listesi:**

- [ ] Dağıtım etkileri listelendi
- [ ] Banka/kasa etkisi önizlendi
- [ ] Üst onay gerekli

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Grup Şirketi Adına Ödeme

**Kod:** `intercompanyPayment`  
**Sınıf:** Gelişmiş  
**Amaç:** Parayı ödeyen şirket ile borcu kapanan şirketi iki bacaklı kaydedin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| İşlem Yönü | select | Evet | Gelen / Tahsilat / Giden / Ödeme |
| İşlem Türü | select | Evet | Banka / Kasa / İade / Virman / Diğer |
| Cari / Karşı Taraf | text | Evet | Körfez İnşaat A.Ş. |
| Banka / Kasa Hesabı | select | Evet | Ziraat · TL 1234 / İş Bankası · TL 5678 / Merkez Kasa |
| Tutar | amount | Evet | 500.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-25 |
| Banka Referansı | text | Hayır | TRX-983741 |
| Kaynak Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Borcu Kapanan Şirket | select | Evet | Grup Şirketi A / Grup Şirketi B |
| Grup İçi Karşı Hareket | select | Evet | Otomatik oluştur |

**Etki önizlemesi:**

- **Banka/Kasa Etkisi:** İşlem yönüne göre oluşur
- **Cari Etkisi:** Onayda oluşur
- **Açık Kalem Kapama:** Dağıtımda oluşur
- **Nakit Akışı:** Gerçekleşmiş hareket

**Kontrol listesi:**

- [ ] Şirket ve cari aktif
- [ ] Mükerrerlik anahtarı kontrol edildi
- [ ] Banka/kasa hesabı yetkili
- [ ] Tutar ve para birimi geçerli

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Döviz Alım / Satım İşlemi

**Kod:** `fxDeal`  
**Sınıf:** Gelişmiş  
**Amaç:** Kaynak ve hedef para birimi, banka kuru, masraf ve valör etkisini kaydedin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Kasa | select | Evet | Ziraat Bankası / İş Bankası / Akbank / Garanti BBVA / Merkez Kasa |
| Hesap / IBAN | text | Evet | TRxx xxxx xxxx 1234 |
| İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-24 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka Referansı | text | Hayır | TRX-983741 |
| İşlem | select | Evet | Döviz Alış / Döviz Satış / Çapraz Döviz |
| Kaynak Döviz | select | Evet | TL / USD / EUR |
| Hedef Döviz | select | Evet | USD / EUR / TL |
| Uygulanan Kur | text | Evet | 47,2500 |

**Etki önizlemesi:**

- **ERP Bakiye Etkisi:** İşleme göre
- **Ekstre Eşleşmesi:** Kontrol edilir
- **Valör Etkisi:** Ayrı hesaplanır
- **Audit:** Değişmez banka satırı

**Kontrol listesi:**

- [ ] Hesap aktif ve şirketle uyumlu
- [ ] Dosya/satır mükerrer değil
- [ ] Valör tarihi kontrol edildi
- [ ] Yetki ve dönem açık

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Bankalar Arası Virman

**Kod:** `bankTransfer`  
**Sınıf:** Sık kullanılan  
**Amaç:** Aynı şirketin banka hesapları arasında iki bacaklı kontrollü transfer oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Kasa | select | Evet | Ziraat Bankası / İş Bankası / Akbank / Garanti BBVA / Merkez Kasa |
| Hesap / IBAN | text | Evet | TRxx xxxx xxxx 1234 |
| İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-24 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka Referansı | text | Hayır | TRX-983741 |
| Kaynak Hesap | select | Evet | Ziraat · 1234 |
| Hedef Hesap | select | Evet | İş Bankası · 5678 |
| Masraf | amount | Hayır | 250,00 |

**Etki önizlemesi:**

- **ERP Bakiye Etkisi:** İşleme göre
- **Ekstre Eşleşmesi:** Kontrol edilir
- **Valör Etkisi:** Ayrı hesaplanır
- **Audit:** Değişmez banka satırı

**Kontrol listesi:**

- [ ] Hesap aktif ve şirketle uyumlu
- [ ] Dosya/satır mükerrer değil
- [ ] Valör tarihi kontrol edildi
- [ ] Yetki ve dönem açık

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

## Açık Kalem ve Dağıtım

### Finansal Dağıtım

**Kod:** `allocation`  
**Sınıf:** Sık kullanılan  
**Amaç:** Bir finansal kaynağı fatura, taksit, sipariş avansı veya diğer açık kalemlere dağıtın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Finansal Kaynak | text | Evet | BNK-2026-00851 |
| Cari | text | Evet | Körfez İnşaat A.Ş. |
| Kaynak Türü | select | Evet | Banka/Kasa / Çek/Senet / POS / DBS / Şirket Kartı |
| Kaynak Tutarı | amount | Evet | 500.000,00 |
| Dağıtılacak Tutar | amount | Evet | 480.000,00 |
| Etki Türü | select | Evet | Kesin nakit kapama / Finansal araçla kapama / DBS kapsamı / Sipariş avansı / Cari mahsup |
| Ödeme Tarihi Esası | select | Evet | Banka işlem tarihi / Araç teslim tarihi / Tahsil/valör tarihi |
| Dağıtım Tarihi | date | Hayır | 2026-08-24 |

**Etki önizlemesi:**

- **İkinci Banka Hareketi:** Oluşmaz
- **İkinci Cari Hareketi:** Oluşmaz
- **Ticari Kapama:** Seçilen tutar kadar
- **Dağıtılmamış Kalan:** 20.000 TL

**Kontrol listesi:**

- [ ] Kaynak kalan tutarı yeterli
- [ ] Açık kalem tutarı aşılmıyor
- [ ] Para birimi/kur yöntemi seçildi
- [ ] Ödeme ve valör tarihi ayrıldı

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Dağıtım Revizyonu / Geri Alma

**Kod:** `allocationRevision`  
**Sınıf:** Sık kullanılan  
**Amaç:** Yanlış dağıtımı silmeden serbest bırakın ve yeni dağıtım oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Finansal Kaynak | text | Evet | BNK-2026-00851 |
| Cari | text | Evet | Körfez İnşaat A.Ş. |
| Kaynak Türü | select | Evet | Banka/Kasa / Çek/Senet / POS / DBS / Şirket Kartı |
| Kaynak Tutarı | amount | Evet | 500.000,00 |
| Dağıtılacak Tutar | amount | Evet | 480.000,00 |
| Etki Türü | select | Evet | Kesin nakit kapama / Finansal araçla kapama / DBS kapsamı / Sipariş avansı / Cari mahsup |
| Ödeme Tarihi Esası | select | Evet | Banka işlem tarihi / Araç teslim tarihi / Tahsil/valör tarihi |
| Dağıtım Tarihi | date | Hayır | 2026-08-24 |
| Revize Edilecek Dağıtım | text | Evet | DGT-2026-00412 |
| Revizyon Gerekçesi | textarea | Evet | Müşteri ödeme açıklamasında farklı fatura bildirildi. |

**Etki önizlemesi:**

- **İkinci Banka Hareketi:** Oluşmaz
- **İkinci Cari Hareketi:** Oluşmaz
- **Ticari Kapama:** Seçilen tutar kadar
- **Dağıtılmamış Kalan:** 20.000 TL

**Kontrol listesi:**

- [ ] Kaynak kalan tutarı yeterli
- [ ] Açık kalem tutarı aşılmıyor
- [ ] Para birimi/kur yöntemi seçildi
- [ ] Ödeme ve valör tarihi ayrıldı

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Toplu Dağıtım

**Kod:** `bulkAllocation`  
**Sınıf:** Gelişmiş  
**Amaç:** Çok sayıda finansal kaynağı kural/öneri ile topluca açık kalemlere bağlayın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Finansal Kaynak | text | Evet | BNK-2026-00851 |
| Cari | text | Evet | Körfez İnşaat A.Ş. |
| Kaynak Türü | select | Evet | Banka/Kasa / Çek/Senet / POS / DBS / Şirket Kartı |
| Kaynak Tutarı | amount | Evet | 500.000,00 |
| Dağıtılacak Tutar | amount | Evet | 480.000,00 |
| Etki Türü | select | Evet | Kesin nakit kapama / Finansal araçla kapama / DBS kapsamı / Sipariş avansı / Cari mahsup |
| Ödeme Tarihi Esası | select | Evet | Banka işlem tarihi / Araç teslim tarihi / Tahsil/valör tarihi |
| Dağıtım Tarihi | date | Hayır | 2026-08-24 |
| Kaynak Havuzu | select | Evet | Dağıtılmamış Banka Tahsilatları / POS Slipleri / DBS Tahsilatları |
| Dağıtım Kuralı | select | Evet | Belge referansı / En eski vade / Tam tutar eşleşmesi / AI önerisi |

**Etki önizlemesi:**

- **İkinci Banka Hareketi:** Oluşmaz
- **İkinci Cari Hareketi:** Oluşmaz
- **Ticari Kapama:** Seçilen tutar kadar
- **Dağıtılmamış Kalan:** 20.000 TL

**Kontrol listesi:**

- [ ] Kaynak kalan tutarı yeterli
- [ ] Açık kalem tutarı aşılmıyor
- [ ] Para birimi/kur yöntemi seçildi
- [ ] Ödeme ve valör tarihi ayrıldı

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Sipariş Avansını Faturaya Aktar

**Kod:** `advanceTransfer`  
**Sınıf:** Sık kullanılan  
**Amaç:** Siparişe bağlı avansı yeni oluşan faturaya kontrollü ilişkilendirin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Finansal Kaynak | text | Evet | BNK-2026-00851 |
| Cari | text | Evet | Körfez İnşaat A.Ş. |
| Kaynak Türü | select | Evet | Banka/Kasa / Çek/Senet / POS / DBS / Şirket Kartı |
| Kaynak Tutarı | amount | Evet | 500.000,00 |
| Dağıtılacak Tutar | amount | Evet | 480.000,00 |
| Etki Türü | select | Evet | Kesin nakit kapama / Finansal araçla kapama / DBS kapsamı / Sipariş avansı / Cari mahsup |
| Ödeme Tarihi Esası | select | Evet | Banka işlem tarihi / Araç teslim tarihi / Tahsil/valör tarihi |
| Dağıtım Tarihi | date | Hayır | 2026-08-24 |
| Sipariş | text | Evet | SP-2026-1848 |
| Hedef Fatura | text | Evet | FTR-2026-5710 |

**Etki önizlemesi:**

- **İkinci Banka Hareketi:** Oluşmaz
- **İkinci Cari Hareketi:** Oluşmaz
- **Ticari Kapama:** Seçilen tutar kadar
- **Dağıtılmamış Kalan:** 20.000 TL

**Kontrol listesi:**

- [ ] Kaynak kalan tutarı yeterli
- [ ] Açık kalem tutarı aşılmıyor
- [ ] Para birimi/kur yöntemi seçildi
- [ ] Ödeme ve valör tarihi ayrıldı

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Cari Mahsup

**Kod:** `offset`  
**Sınıf:** Standart  
**Amaç:** Aynı veya yetkili farklı carilerin borç/alacak açık kalemlerini nakitsiz kapatın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Finansal Kaynak | text | Evet | BNK-2026-00851 |
| Cari | text | Evet | Körfez İnşaat A.Ş. |
| Kaynak Türü | select | Evet | Banka/Kasa / Çek/Senet / POS / DBS / Şirket Kartı |
| Kaynak Tutarı | amount | Evet | 500.000,00 |
| Dağıtılacak Tutar | amount | Evet | 480.000,00 |
| Etki Türü | select | Evet | Kesin nakit kapama / Finansal araçla kapama / DBS kapsamı / Sipariş avansı / Cari mahsup |
| Ödeme Tarihi Esası | select | Evet | Banka işlem tarihi / Araç teslim tarihi / Tahsil/valör tarihi |
| Dağıtım Tarihi | date | Hayır | 2026-08-24 |
| Borçlu Cari | text | Evet | ABC Demir A.Ş. |
| Alacaklı Cari | text | Evet | ABC Demir A.Ş. |
| Mahsup Dayanağı | select | Evet | Aynı cari müşteri/tedarikçi / Grup içi onaylı mahsup / Diğer |

**Etki önizlemesi:**

- **İkinci Banka Hareketi:** Oluşmaz
- **İkinci Cari Hareketi:** Oluşmaz
- **Ticari Kapama:** Seçilen tutar kadar
- **Dağıtılmamış Kalan:** 20.000 TL

**Kontrol listesi:**

- [ ] Kaynak kalan tutarı yeterli
- [ ] Açık kalem tutarı aşılmıyor
- [ ] Para birimi/kur yöntemi seçildi
- [ ] Ödeme ve valör tarihi ayrıldı

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Dağıtılmamış Bakiyeyi İade Et

**Kod:** `unallocatedRefund`  
**Sınıf:** Standart  
**Amaç:** Dağıtılmamış finansal kaynak kalanını iade sürecine alın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Finansal Kaynak | text | Evet | BNK-2026-00851 |
| Cari | text | Evet | Körfez İnşaat A.Ş. |
| Kaynak Türü | select | Evet | Banka/Kasa / Çek/Senet / POS / DBS / Şirket Kartı |
| Kaynak Tutarı | amount | Evet | 500.000,00 |
| Dağıtılacak Tutar | amount | Evet | 480.000,00 |
| Etki Türü | select | Evet | Kesin nakit kapama / Finansal araçla kapama / DBS kapsamı / Sipariş avansı / Cari mahsup |
| Ödeme Tarihi Esası | select | Evet | Banka işlem tarihi / Araç teslim tarihi / Tahsil/valör tarihi |
| Dağıtım Tarihi | date | Hayır | 2026-08-24 |
| İade Edilecek Kalan | amount | Evet | 20.000,00 |
| Onaylı IBAN | text | Evet | TRxx xxxx xxxx 5612 |

**Etki önizlemesi:**

- **İkinci Banka Hareketi:** Oluşmaz
- **İkinci Cari Hareketi:** Oluşmaz
- **Ticari Kapama:** Seçilen tutar kadar
- **Dağıtılmamış Kalan:** 20.000 TL

**Kontrol listesi:**

- [ ] Kaynak kalan tutarı yeterli
- [ ] Açık kalem tutarı aşılmıyor
- [ ] Para birimi/kur yöntemi seçildi
- [ ] Ödeme ve valör tarihi ayrıldı

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

## Tahsilat ve Cari Risk

### Tahsilat Görevi Oluştur

**Kod:** `collectionTask`  
**Sınıf:** Sık kullanılan  
**Amaç:** Vadesi gelen veya geciken açık kalem için sorumlu, hedef tarih ve görüşme planı oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Cari | text | Evet | Körfez İnşaat A.Ş. |
| Tahsilat / Risk İşlemi | select | Evet | Tahsilat Görevi / Ödeme Sözü / İhtilaf / Yapılandırma / Hukuki Takip |
| Bağlı Açık Kalem / Sipariş | text | Hayır | FTR-2026-5612 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Hedef / Söz Tarihi | date | Evet | 2026-08-28 |
| Sorumlu | select | Evet | Tahsilat Ekibi / Finans Müdürü / Satış Sorumlusu / Hukuk |
| Önem | select | Evet | Kritik / Yüksek / Normal / Bilgi |
| Görev Kanalı | select | Evet | Telefon / E-posta / Ziyaret / Satış Sorumlusu |
| Beklenen Sonuç | select | Evet | Ödeme Tarihi Al / Mutabakat Yap / Belge Tamamla / Yapılandırma Görüş |

**Etki önizlemesi:**

- **Brüt Açık Alacak:** 18.400.000 TL
- **Ödeme Sözü / Hedef:** 2.850.000 TL
- **Net Cari Risk:** 15.550.000 TL
- **Sevkiyat Blokajı:** Politikaya göre

**Kontrol listesi:**

- [ ] Cari açık kalemleri ve risk limiti güncel
- [ ] Ödeme sözü mükerrer değil
- [ ] Sorumlu ve hedef tarih tanımlı
- [ ] Hukuki/istisna işlemlerinde üst onay belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Ödeme Sözü Kaydı

**Kod:** `paymentPromise`  
**Sınıf:** Sık kullanılan  
**Amaç:** Müşterinin bildirdiği tarih, tutar ve ödeme aracını açık kalemlerle ilişkilendirin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Cari | text | Evet | Körfez İnşaat A.Ş. |
| Tahsilat / Risk İşlemi | select | Evet | Tahsilat Görevi / Ödeme Sözü / İhtilaf / Yapılandırma / Hukuki Takip |
| Bağlı Açık Kalem / Sipariş | text | Hayır | FTR-2026-5612 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Hedef / Söz Tarihi | date | Evet | 2026-08-28 |
| Sorumlu | select | Evet | Tahsilat Ekibi / Finans Müdürü / Satış Sorumlusu / Hukuk |
| Önem | select | Evet | Kritik / Yüksek / Normal / Bilgi |
| Söz Verilen Tutar | amount | Evet | 2.850.000,00 |
| Söz Tarihi | date | Evet | 2026-08-28 |
| Beklenen Araç | select | Evet | Banka Havalesi / Çek / Senet / DBS / POS / Kısmi Ödeme |
| Teyit Kaynağı | select | Evet | E-posta / Telefon Görüşmesi / WhatsApp Belgesi / Islak İmzalı Plan |

**Etki önizlemesi:**

- **Brüt Açık Alacak:** 18.400.000 TL
- **Ödeme Sözü / Hedef:** 2.850.000 TL
- **Net Cari Risk:** 15.550.000 TL
- **Sevkiyat Blokajı:** Politikaya göre

**Kontrol listesi:**

- [ ] Cari açık kalemleri ve risk limiti güncel
- [ ] Ödeme sözü mükerrer değil
- [ ] Sorumlu ve hedef tarih tanımlı
- [ ] Hukuki/istisna işlemlerinde üst onay belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### İhtilaflı Alacak Kaydı

**Kod:** `receivableDispute`  
**Sınıf:** Standart  
**Amaç:** Fatura, miktar, fiyat, kalite veya belge ihtilafını tahsilattan ayrı izleyin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Cari | text | Evet | Körfez İnşaat A.Ş. |
| Tahsilat / Risk İşlemi | select | Evet | Tahsilat Görevi / Ödeme Sözü / İhtilaf / Yapılandırma / Hukuki Takip |
| Bağlı Açık Kalem / Sipariş | text | Hayır | FTR-2026-5612 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Hedef / Söz Tarihi | date | Evet | 2026-08-28 |
| Sorumlu | select | Evet | Tahsilat Ekibi / Finans Müdürü / Satış Sorumlusu / Hukuk |
| Önem | select | Evet | Kritik / Yüksek / Normal / Bilgi |
| İhtilaf Türü | select | Evet | Fiyat / Miktar / Kalite / İrsaliye / Belge / Vade |
| İhtilaflı Tutar | amount | Evet | 450.000,00 |
| Karşı Taraf Açıklaması | textarea | Evet | Fatura fiyat farkı için inceleme talep edildi. |

**Etki önizlemesi:**

- **Brüt Açık Alacak:** 18.400.000 TL
- **Ödeme Sözü / Hedef:** 2.850.000 TL
- **Net Cari Risk:** 15.550.000 TL
- **Sevkiyat Blokajı:** Politikaya göre

**Kontrol listesi:**

- [ ] Cari açık kalemleri ve risk limiti güncel
- [ ] Ödeme sözü mükerrer değil
- [ ] Sorumlu ve hedef tarih tanımlı
- [ ] Hukuki/istisna işlemlerinde üst onay belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Tahsilat Yapılandırma Planı

**Kod:** `receivableRestructure`  
**Sınıf:** Gelişmiş  
**Amaç:** Gecikmiş alacağı yeni taksit ve vade planına kontrollü olarak dönüştürün.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Cari | text | Evet | Körfez İnşaat A.Ş. |
| Tahsilat / Risk İşlemi | select | Evet | Tahsilat Görevi / Ödeme Sözü / İhtilaf / Yapılandırma / Hukuki Takip |
| Bağlı Açık Kalem / Sipariş | text | Hayır | FTR-2026-5612 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Hedef / Söz Tarihi | date | Evet | 2026-08-28 |
| Sorumlu | select | Evet | Tahsilat Ekibi / Finans Müdürü / Satış Sorumlusu / Hukuk |
| Önem | select | Evet | Kritik / Yüksek / Normal / Bilgi |
| Yapılandırılacak Tutar | amount | Evet | 6.500.000,00 |
| Taksit Sayısı | select | Evet | 2 / 3 / 4 / 6 |
| İlk Taksit Tarihi | date | Evet | 2026-09-05 |
| Vade Farkı Politikası | select | Evet | Yok / Sipariş Koşulu / Yönetim Onaylı Oran |

**Etki önizlemesi:**

- **Brüt Açık Alacak:** 18.400.000 TL
- **Ödeme Sözü / Hedef:** 2.850.000 TL
- **Net Cari Risk:** 15.550.000 TL
- **Sevkiyat Blokajı:** Politikaya göre

**Kontrol listesi:**

- [ ] Cari açık kalemleri ve risk limiti güncel
- [ ] Ödeme sözü mükerrer değil
- [ ] Sorumlu ve hedef tarih tanımlı
- [ ] Hukuki/istisna işlemlerinde üst onay belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Hukuki Takibe Devir

**Kod:** `legalFollowup`  
**Sınıf:** Gelişmiş  
**Amaç:** Alacağı hukuk/avukat/icra sürecine belge, karar ve etki önizlemesiyle devredin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Cari | text | Evet | Körfez İnşaat A.Ş. |
| Tahsilat / Risk İşlemi | select | Evet | Tahsilat Görevi / Ödeme Sözü / İhtilaf / Yapılandırma / Hukuki Takip |
| Bağlı Açık Kalem / Sipariş | text | Hayır | FTR-2026-5612 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Hedef / Söz Tarihi | date | Evet | 2026-08-28 |
| Sorumlu | select | Evet | Tahsilat Ekibi / Finans Müdürü / Satış Sorumlusu / Hukuk |
| Önem | select | Evet | Kritik / Yüksek / Normal / Bilgi |
| Takip Türü | select | Evet | Avukat Takibi / İcra / İhtarname / Dava |
| Hukuk Sorumlusu | select | Evet | Şirket Hukuk / Dış Avukat |
| Devir Tarihi | date | Evet | 2026-08-24 |
| Yönetim Kararı / Belge | text | Evet | YK-2026-041 |

**Etki önizlemesi:**

- **Brüt Açık Alacak:** 18.400.000 TL
- **Ödeme Sözü / Hedef:** 2.850.000 TL
- **Net Cari Risk:** 15.550.000 TL
- **Sevkiyat Blokajı:** Politikaya göre

**Kontrol listesi:**

- [ ] Cari açık kalemleri ve risk limiti güncel
- [ ] Ödeme sözü mükerrer değil
- [ ] Sorumlu ve hedef tarih tanımlı
- [ ] Hukuki/istisna işlemlerinde üst onay belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Cari Risk / Sevkiyat İstisnası

**Kod:** `riskException`  
**Sınıf:** Standart  
**Amaç:** Limit veya gecikme blokajına rağmen kontrollü istisna talebi oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Cari | text | Evet | Körfez İnşaat A.Ş. |
| Tahsilat / Risk İşlemi | select | Evet | Tahsilat Görevi / Ödeme Sözü / İhtilaf / Yapılandırma / Hukuki Takip |
| Bağlı Açık Kalem / Sipariş | text | Hayır | FTR-2026-5612 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Hedef / Söz Tarihi | date | Evet | 2026-08-28 |
| Sorumlu | select | Evet | Tahsilat Ekibi / Finans Müdürü / Satış Sorumlusu / Hukuk |
| Önem | select | Evet | Kritik / Yüksek / Normal / Bilgi |
| İstisna Türü | select | Evet | Geçici Limit / Tek Sevkiyat / Sipariş Devamı / Blokaj Kaldırma |
| İstisna Tutarı | amount | Evet | 3.000.000,00 |
| Geçerlilik Bitişi | date | Evet | 2026-08-31 |
| Gerekçe | textarea | Evet | Onaylı ödeme sözü ve DBS teminatı bulunmaktadır. |

**Etki önizlemesi:**

- **Brüt Açık Alacak:** 18.400.000 TL
- **Ödeme Sözü / Hedef:** 2.850.000 TL
- **Net Cari Risk:** 15.550.000 TL
- **Sevkiyat Blokajı:** Politikaya göre

**Kontrol listesi:**

- [ ] Net cari risk yeniden hesaplandı
- [ ] Teminat ve ödeme sözü doğrulandı
- [ ] Satış/finans ortak onayı gerekli

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

## Banka ve Kasa

### Banka Ekstresi İçe Aktar

**Kod:** `statement`  
**Sınıf:** Sık kullanılan  
**Amaç:** CAMT/MT940/Excel/CSV/PDF kaynağını hesaba ve döneme bağlayın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Kasa | select | Evet | Ziraat Bankası / İş Bankası / Akbank / Garanti BBVA / Merkez Kasa |
| Hesap / IBAN | text | Evet | TRxx xxxx xxxx 1234 |
| İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-24 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka Referansı | text | Hayır | TRX-983741 |
| Dosya Formatı | select | Evet | CAMT XML / MT940 / Excel/CSV / PDF |
| Dönem | select | Evet | 24.08.2026 / Ağustos 2026 |

**Etki önizlemesi:**

- **ERP Bakiye Etkisi:** İşleme göre
- **Ekstre Eşleşmesi:** Kontrol edilir
- **Valör Etkisi:** Ayrı hesaplanır
- **Audit:** Değişmez banka satırı

**Kontrol listesi:**

- [ ] Hesap aktif ve şirketle uyumlu
- [ ] Dosya/satır mükerrer değil
- [ ] Valör tarihi kontrol edildi
- [ ] Yetki ve dönem açık

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Banka Mutabakatı

**Kod:** `reconciliation`  
**Sınıf:** Sık kullanılan  
**Amaç:** Bir-bir, bir-çok, çok-bir veya çok-çok eşleşme yapın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Kasa | select | Evet | Ziraat Bankası / İş Bankası / Akbank / Garanti BBVA / Merkez Kasa |
| Hesap / IBAN | text | Evet | TRxx xxxx xxxx 1234 |
| İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-24 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka Referansı | text | Hayır | TRX-983741 |
| Eşleştirme Modeli | select | Evet | 1 ↔ 1 / 1 ↔ Çok / Çok ↔ 1 / Çok ↔ Çok |
| Banka Satırı | text | Evet | TRX-983741 |
| ERP Hareketleri | text | Evet | BNK-2026-00851 |

**Etki önizlemesi:**

- **ERP Bakiye Etkisi:** İşleme göre
- **Ekstre Eşleşmesi:** Kontrol edilir
- **Valör Etkisi:** Ayrı hesaplanır
- **Audit:** Değişmez banka satırı

**Kontrol listesi:**

- [ ] Hesap aktif ve şirketle uyumlu
- [ ] Dosya/satır mükerrer değil
- [ ] Valör tarihi kontrol edildi
- [ ] Yetki ve dönem açık

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Yeni Banka Hesabı

**Kod:** `bankAccount`  
**Sınıf:** Gelişmiş  
**Amaç:** Hesap açılış talebi, IBAN, yetkililer ve çift kontrol akışını başlatın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Kasa | select | Evet | Ziraat Bankası / İş Bankası / Akbank / Garanti BBVA / Merkez Kasa |
| Hesap / IBAN | text | Evet | TRxx xxxx xxxx 1234 |
| İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-24 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka Referansı | text | Hayır | TRX-983741 |
| Hesap Türü | select | Evet | Vadesiz / Kredili Hesap / Vadeli / Katılım |
| Şube | text | Hayır | Gaziantep Ticari Şube |
| Yaşam Döngüsü | select | Evet | Talep / Kontrol / Onay / Aktif |

**Etki önizlemesi:**

- **ERP Bakiye Etkisi:** İşleme göre
- **Ekstre Eşleşmesi:** Kontrol edilir
- **Valör Etkisi:** Ayrı hesaplanır
- **Audit:** Değişmez banka satırı

**Kontrol listesi:**

- [ ] Hesap aktif ve şirketle uyumlu
- [ ] Dosya/satır mükerrer değil
- [ ] Valör tarihi kontrol edildi
- [ ] Yetki ve dönem açık

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Banka Hesabı Değişiklik / Kapatma

**Kod:** `bankAccountChange`  
**Sınıf:** Gelişmiş  
**Amaç:** IBAN, yetkili veya hesap durumunu kontrollü ve tarihçeli değiştirin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Kasa | select | Evet | Ziraat Bankası / İş Bankası / Akbank / Garanti BBVA / Merkez Kasa |
| Hesap / IBAN | text | Evet | TRxx xxxx xxxx 1234 |
| İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-24 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka Referansı | text | Hayır | TRX-983741 |
| İşlem | select | Evet | IBAN Bilgisi Değişikliği / Yetkili Değişikliği / Askıya Alma / Kapatma |
| Mevcut Hesap | select | Evet | Ziraat · 1234 |
| Değişiklik Gerekçesi | textarea | Evet | Hesap kapatma talebi. |

**Etki önizlemesi:**

- **ERP Bakiye Etkisi:** İşleme göre
- **Ekstre Eşleşmesi:** Kontrol edilir
- **Valör Etkisi:** Ayrı hesaplanır
- **Audit:** Değişmez banka satırı

**Kontrol listesi:**

- [ ] Hesap aktif ve şirketle uyumlu
- [ ] Dosya/satır mükerrer değil
- [ ] Valör tarihi kontrol edildi
- [ ] Yetki ve dönem açık

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Kasa Hareketi

**Kod:** `cash`  
**Sınıf:** Sık kullanılan  
**Amaç:** Kasa tahsilatı, ödemesi, virmanı veya sayım farkını kaydedin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Kasa | select | Evet | Ziraat Bankası / İş Bankası / Akbank / Garanti BBVA / Merkez Kasa |
| Hesap / IBAN | text | Evet | TRxx xxxx xxxx 1234 |
| İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-24 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka Referansı | text | Hayır | TRX-983741 |
| Kasa | select | Evet | Merkez Kasa / Döviz Kasa / Şube Kasa |
| Kasa İşlemi | select | Evet | Tahsilat / Ödeme / Virman / Sayım Farkı |

**Etki önizlemesi:**

- **ERP Bakiye Etkisi:** İşleme göre
- **Ekstre Eşleşmesi:** Kontrol edilir
- **Valör Etkisi:** Ayrı hesaplanır
- **Audit:** Değişmez banka satırı

**Kontrol listesi:**

- [ ] Hesap aktif ve şirketle uyumlu
- [ ] Dosya/satır mükerrer değil
- [ ] Valör tarihi kontrol edildi
- [ ] Yetki ve dönem açık

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Kasa Sayımı

**Kod:** `cashCount`  
**Sınıf:** Standart  
**Amaç:** Fizikî sayım, sistem bakiyesi, fark ve onay kaydı oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Kasa | select | Evet | Ziraat Bankası / İş Bankası / Akbank / Garanti BBVA / Merkez Kasa |
| Hesap / IBAN | text | Evet | TRxx xxxx xxxx 1234 |
| İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-24 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka Referansı | text | Hayır | TRX-983741 |
| Kasa | select | Evet | Merkez Kasa |
| Sistem Bakiyesi | amount | Evet | 8.420.000,00 |
| Fizikî Sayım | amount | Evet | 8.407.500,00 |
| Fark Nedeni | textarea | Hayır | 12.500 TL fark kontrol ediliyor. |

**Etki önizlemesi:**

- **ERP Bakiye Etkisi:** İşleme göre
- **Ekstre Eşleşmesi:** Kontrol edilir
- **Valör Etkisi:** Ayrı hesaplanır
- **Audit:** Değişmez banka satırı

**Kontrol listesi:**

- [ ] Hesap aktif ve şirketle uyumlu
- [ ] Dosya/satır mükerrer değil
- [ ] Valör tarihi kontrol edildi
- [ ] Yetki ve dönem açık

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Banka Masrafı Sınıflandırma

**Kod:** `bankFee`  
**Sınıf:** Standart  
**Amaç:** Beklenen ve gerçekleşen banka masrafını hizmet türüne bağlayın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Kasa | select | Evet | Ziraat Bankası / İş Bankası / Akbank / Garanti BBVA / Merkez Kasa |
| Hesap / IBAN | text | Evet | TRxx xxxx xxxx 1234 |
| İşlem Tarihi | date | Evet | 2026-08-24 |
| Valör Tarihi | date | Hayır | 2026-08-24 |
| Tutar | amount | Evet | 2.850.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Banka Referansı | text | Hayır | TRX-983741 |
| Masraf Türü | select | Evet | EFT/Havale / SWIFT / POS Komisyonu / DBS Komisyonu / Kredi Komisyonu |
| Beklenen Tutar | amount | Hayır | 1.250,00 |
| Gerçekleşen Tutar | amount | Hayır | 1.480,00 |

**Etki önizlemesi:**

- **ERP Bakiye Etkisi:** İşleme göre
- **Ekstre Eşleşmesi:** Kontrol edilir
- **Valör Etkisi:** Ayrı hesaplanır
- **Audit:** Değişmez banka satırı

**Kontrol listesi:**

- [ ] Hesap aktif ve şirketle uyumlu
- [ ] Dosya/satır mükerrer değil
- [ ] Valör tarihi kontrol edildi
- [ ] Yetki ve dönem açık

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

## Ödeme Yönetimi

### Ödeme Önerisi Oluştur

**Kod:** `paymentProposal`  
**Sınıf:** Sık kullanılan  
**Amaç:** Vade, öncelik, üretim etkisi, bütçe ve likiditeye göre ödeme listesi hazırlayın.

**Adımlar:** 1 Kapsam → 2 Açık Kalem → 3 Öncelik → 4 Nakit/Bütçe → 5 Taslak

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Ödeme Dönemi | select | Evet | Bugün / 7 Gün / 30 Gün |
| Ödeme Türü | select | Evet | Tedarikçi / Kredi / Vergi/SGK / Personel / Diğer |
| Tedarikçi / Alıcı | text | Evet | Bora Boru San. A.Ş. |
| Toplam Tutar | amount | Evet | 3.450.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Planlanan Ödeme Tarihi | date | Evet | 2026-08-25 |
| Önerilen Banka | select | Evet | İş Bankası · 5678 / Ziraat · 1234 / Garanti · 3456 |
| Öncelik | select | Evet | Kritik / Yüksek / Normal / Ertelenebilir |

**Etki önizlemesi:**

- **Ödeme Paketi:** 3.450.000 TL
- **30 Günlük Nakit Etkisi:** −3.450.000 TL
- **Bütçe:** Uygun
- **Onay Seviyesi:** Finans Müdürü

**Kontrol listesi:**

- [ ] Açık kalemler onaylı
- [ ] Bütçe/likidite uygun
- [ ] IBAN doğrulandı
- [ ] Ödeme onay matrisi belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Toplu Ödeme Paketi

**Kod:** `paymentBatch`  
**Sınıf:** Sık kullanılan  
**Amaç:** Onaylı önerileri banka ve para birimi bazında toplu ödeme paketine alın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Ödeme Dönemi | select | Evet | Bugün / 7 Gün / 30 Gün |
| Ödeme Türü | select | Evet | Tedarikçi / Kredi / Vergi/SGK / Personel / Diğer |
| Tedarikçi / Alıcı | text | Evet | Bora Boru San. A.Ş. |
| Toplam Tutar | amount | Evet | 3.450.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Planlanan Ödeme Tarihi | date | Evet | 2026-08-25 |
| Önerilen Banka | select | Evet | İş Bankası · 5678 / Ziraat · 1234 / Garanti · 3456 |
| Öncelik | select | Evet | Kritik / Yüksek / Normal / Ertelenebilir |
| Paket No | text | Evet | PAY-2026-0088 |
| İşlem Sayısı | text | Hayır | 12 ödeme |
| Toplam Paket | amount | Evet | 28.600.000,00 |

**Etki önizlemesi:**

- **Ödeme Paketi:** 3.450.000 TL
- **30 Günlük Nakit Etkisi:** −3.450.000 TL
- **Bütçe:** Uygun
- **Onay Seviyesi:** Finans Müdürü

**Kontrol listesi:**

- [ ] Açık kalemler onaylı
- [ ] Bütçe/likidite uygun
- [ ] IBAN doğrulandı
- [ ] Ödeme onay matrisi belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Banka Ödeme Talimatı

**Kod:** `paymentInstruction`  
**Sınıf:** Standart  
**Amaç:** Onaylı paketi banka formatı veya talimat belgesine dönüştürün.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Ödeme Dönemi | select | Evet | Bugün / 7 Gün / 30 Gün |
| Ödeme Türü | select | Evet | Tedarikçi / Kredi / Vergi/SGK / Personel / Diğer |
| Tedarikçi / Alıcı | text | Evet | Bora Boru San. A.Ş. |
| Toplam Tutar | amount | Evet | 3.450.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Planlanan Ödeme Tarihi | date | Evet | 2026-08-25 |
| Önerilen Banka | select | Evet | İş Bankası · 5678 / Ziraat · 1234 / Garanti · 3456 |
| Öncelik | select | Evet | Kritik / Yüksek / Normal / Ertelenebilir |
| Talimat Formatı | select | Evet | Banka Excel / ISO 20022 pain.001 / PDF Talimat / Manuel Portal |
| Yetkili İmza Grubu | select | Evet | A + B Grubu / Tek Yönetici |

**Etki önizlemesi:**

- **Ödeme Paketi:** 3.450.000 TL
- **30 Günlük Nakit Etkisi:** −3.450.000 TL
- **Bütçe:** Uygun
- **Onay Seviyesi:** Finans Müdürü

**Kontrol listesi:**

- [ ] Açık kalemler onaylı
- [ ] Bütçe/likidite uygun
- [ ] IBAN doğrulandı
- [ ] Ödeme onay matrisi belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Ödeme Onayı

**Kod:** `paymentApproval`  
**Sınıf:** Sık kullanılan  
**Amaç:** Ödeme etkisini, IBAN’ı, bütçeyi ve banka seçimini inceleyip karar verin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Ödeme Dönemi | select | Evet | Bugün / 7 Gün / 30 Gün |
| Ödeme Türü | select | Evet | Tedarikçi / Kredi / Vergi/SGK / Personel / Diğer |
| Tedarikçi / Alıcı | text | Evet | Bora Boru San. A.Ş. |
| Toplam Tutar | amount | Evet | 3.450.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Planlanan Ödeme Tarihi | date | Evet | 2026-08-25 |
| Önerilen Banka | select | Evet | İş Bankası · 5678 / Ziraat · 1234 / Garanti · 3456 |
| Öncelik | select | Evet | Kritik / Yüksek / Normal / Ertelenebilir |
| Onay Kararı | select | Evet | Onayla / Düzeltmeye Gönder / Reddet |
| Onay Notu | textarea | Hayır | Banka ve likidite kontrol edildi. |

**Etki önizlemesi:**

- **Ödeme Paketi:** 3.450.000 TL
- **30 Günlük Nakit Etkisi:** −3.450.000 TL
- **Bütçe:** Uygun
- **Onay Seviyesi:** Finans Müdürü

**Kontrol listesi:**

- [ ] Açık kalemler onaylı
- [ ] Bütçe/likidite uygun
- [ ] IBAN doğrulandı
- [ ] Ödeme onay matrisi belirlendi

**Nihai aksiyon:** Kararı Kaydet

---

### IBAN Değişikliği Doğrulama

**Kod:** `ibanChange`  
**Sınıf:** Standart  
**Amaç:** Eski-yeni değer, belge, bağımsız doğrulama ve ikinci onayı yönetin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Ödeme Dönemi | select | Evet | Bugün / 7 Gün / 30 Gün |
| Ödeme Türü | select | Evet | Tedarikçi / Kredi / Vergi/SGK / Personel / Diğer |
| Tedarikçi / Alıcı | text | Evet | Bora Boru San. A.Ş. |
| Toplam Tutar | amount | Evet | 3.450.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Planlanan Ödeme Tarihi | date | Evet | 2026-08-25 |
| Önerilen Banka | select | Evet | İş Bankası · 5678 / Ziraat · 1234 / Garanti · 3456 |
| Öncelik | select | Evet | Kritik / Yüksek / Normal / Ertelenebilir |
| Cari | text | Evet | Bora Boru San. A.Ş. |
| Eski IBAN | text | Evet | TRxx ... 3210 |
| Yeni IBAN | text | Evet | TRxx ... 9876 |
| Doğrulama Kanalı | select | Evet | Kayıtlı telefon / Yetkili e-posta / Islak imzalı yazı |

**Etki önizlemesi:**

- **Ödeme Paketi:** 3.450.000 TL
- **30 Günlük Nakit Etkisi:** −3.450.000 TL
- **Bütçe:** Uygun
- **Onay Seviyesi:** Finans Müdürü

**Kontrol listesi:**

- [ ] Açık kalemler onaylı
- [ ] Bütçe/likidite uygun
- [ ] IBAN doğrulandı
- [ ] Ödeme onay matrisi belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Banka Ödeme Sonucu / Red

**Kod:** `paymentResult`  
**Sınıf:** Standart  
**Amaç:** Bankanın kabul, kısmi gerçekleşme, red veya iade sonucunu ödeme paketiyle eşleştirin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Ödeme Dönemi | select | Evet | Bugün / 7 Gün / 30 Gün |
| Ödeme Türü | select | Evet | Tedarikçi / Kredi / Vergi/SGK / Personel / Diğer |
| Tedarikçi / Alıcı | text | Evet | Bora Boru San. A.Ş. |
| Toplam Tutar | amount | Evet | 3.450.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Planlanan Ödeme Tarihi | date | Evet | 2026-08-25 |
| Önerilen Banka | select | Evet | İş Bankası · 5678 / Ziraat · 1234 / Garanti · 3456 |
| Öncelik | select | Evet | Kritik / Yüksek / Normal / Ertelenebilir |
| Talimat / Paket | text | Evet | PAY-2026-0088 |
| Banka Sonucu | select | Evet | Tam Gerçekleşti / Kısmi Gerçekleşti / Reddedildi / İade Edildi |
| Gerçekleşen Tutar | amount | Evet | 28.600.000,00 |
| Banka Referansı | text | Evet | BNK-RESULT-8841 |
| Red / İade Nedeni | textarea | Hayır | — |

**Etki önizlemesi:**

- **Ödeme Paketi:** 3.450.000 TL
- **30 Günlük Nakit Etkisi:** −3.450.000 TL
- **Bütçe:** Uygun
- **Onay Seviyesi:** Finans Müdürü

**Kontrol listesi:**

- [ ] Açık kalemler onaylı
- [ ] Bütçe/likidite uygun
- [ ] IBAN doğrulandı
- [ ] Ödeme onay matrisi belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

## Ödeme Araçları

### Çek / Senet Kaydı

**Kod:** `instrument`  
**Sınıf:** Sık kullanılan  
**Amaç:** Alınan veya verilen aracı, kullanım amacını ve risk dönüşümünü kaydedin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Araç Türü | select | Evet | Alınan Çek / Verilen Çek / Alınan Senet / Verilen Senet / DBS |
| Cari / Karşı Taraf | text | Evet | Marmara Metal San. A.Ş. |
| Kullanım Amacı | select | Evet | Ödeme / Tahsilat / Teminat / Bankaya Tahsil / Bankaya Teminat / Ciro |
| Nominal / Brüt Tutar | amount | Evet | 1.200.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Teslim / İşlem Tarihi | date | Evet | 2026-08-24 |
| Vade / Valör Tarihi | date | Hayır | 2026-09-12 |
| Banka / Kart / POS | select | Evet | Ziraat / İş Bankası / Akbank / Garanti |
| Belge / Araç No | text | Hayır | CEK-2026-00212 |

**Etki önizlemesi:**

- **Ticari Kapama:** Araç türüne göre
- **Banka Nakit Etkisi:** Sonuç tarihinde
- **Risk Türü:** Araç riskine dönüşür
- **Dağıtım:** Ayrı kontrol

**Kontrol listesi:**

- [ ] Araç no mükerrer değil
- [ ] Kullanım amacı seçildi
- [ ] Risk ve nakitleşme politikası uygun
- [ ] Belge görseli eklendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Çek Ciro / Bankaya Verme

**Kod:** `checkEndorse`  
**Sınıf:** Standart  
**Amaç:** Portföydeki çeki tedarikçiye ciro edin veya bankaya tahsil/teminata verin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Araç Türü | select | Evet | Alınan Çek / Verilen Çek / Alınan Senet / Verilen Senet / DBS |
| Cari / Karşı Taraf | text | Evet | Marmara Metal San. A.Ş. |
| Kullanım Amacı | select | Evet | Ödeme / Tahsilat / Teminat / Bankaya Tahsil / Bankaya Teminat / Ciro |
| Nominal / Brüt Tutar | amount | Evet | 1.200.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Teslim / İşlem Tarihi | date | Evet | 2026-08-24 |
| Vade / Valör Tarihi | date | Hayır | 2026-09-12 |
| Banka / Kart / POS | select | Evet | Ziraat / İş Bankası / Akbank / Garanti |
| Belge / Araç No | text | Hayır | CEK-2026-00212 |
| İşlem | select | Evet | Ciro / Bankaya Tahsil / Bankaya Teminat |
| Hedef Tedarikçi / Banka | text | Evet | Bora Boru San. A.Ş. |

**Etki önizlemesi:**

- **Ticari Kapama:** Araç türüne göre
- **Banka Nakit Etkisi:** Sonuç tarihinde
- **Risk Türü:** Araç riskine dönüşür
- **Dağıtım:** Ayrı kontrol

**Kontrol listesi:**

- [ ] Araç no mükerrer değil
- [ ] Kullanım amacı seçildi
- [ ] Risk ve nakitleşme politikası uygun
- [ ] Belge görseli eklendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Karşılıksız / İade / Protesto

**Kod:** `checkReturn`  
**Sınıf:** Standart  
**Amaç:** Araç sonucu başarısız olduğunda kapamaları tersleyip riski yeniden açın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Araç Türü | select | Evet | Alınan Çek / Verilen Çek / Alınan Senet / Verilen Senet / DBS |
| Cari / Karşı Taraf | text | Evet | Marmara Metal San. A.Ş. |
| Kullanım Amacı | select | Evet | Ödeme / Tahsilat / Teminat / Bankaya Tahsil / Bankaya Teminat / Ciro |
| Nominal / Brüt Tutar | amount | Evet | 1.200.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Teslim / İşlem Tarihi | date | Evet | 2026-08-24 |
| Vade / Valör Tarihi | date | Hayır | 2026-09-12 |
| Banka / Kart / POS | select | Evet | Ziraat / İş Bankası / Akbank / Garanti |
| Belge / Araç No | text | Hayır | CEK-2026-00212 |
| Sonuç Türü | select | Evet | Karşılıksız / İade / Protesto / İptal |
| Banka Sonuç Tarihi | date | Evet | 2026-09-12 |
| Masraf | amount | Hayır | 8.250,00 |

**Etki önizlemesi:**

- **Ticari Kapama:** Araç türüne göre
- **Banka Nakit Etkisi:** Sonuç tarihinde
- **Risk Türü:** Araç riskine dönüşür
- **Dağıtım:** Ayrı kontrol

**Kontrol listesi:**

- [ ] Etkilenen faturalar listelendi
- [ ] Orijinal vadeler geri yüklenecek
- [ ] Hukuk/tahsilat görevi oluşturulacak

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### DBS Fatura Gönderimi / Kapsam

**Kod:** `dbsInstruction`  
**Sınıf:** Sık kullanılan  
**Amaç:** Faturaları DBS’ye gönderin, limit ve garanti içi/dışı tutarı belirleyin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Araç Türü | select | Evet | Alınan Çek / Verilen Çek / Alınan Senet / Verilen Senet / DBS |
| Cari / Karşı Taraf | text | Evet | Marmara Metal San. A.Ş. |
| Kullanım Amacı | select | Evet | Ödeme / Tahsilat / Teminat / Bankaya Tahsil / Bankaya Teminat / Ciro |
| Nominal / Brüt Tutar | amount | Evet | 1.200.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Teslim / İşlem Tarihi | date | Evet | 2026-08-24 |
| Vade / Valör Tarihi | date | Hayır | 2026-09-12 |
| Banka / Kart / POS | select | Evet | Ziraat / İş Bankası / Akbank / Garanti |
| Belge / Araç No | text | Hayır | CEK-2026-00212 |
| DBS Bankası | select | Evet | Akbank / Garanti / Ziraat |
| DBS Limiti | amount | Hayır | 25.000.000,00 |
| Gönderilecek Fatura Toplamı | amount | Evet | 6.250.000,00 |

**Etki önizlemesi:**

- **Ticari Kapama:** Araç türüne göre
- **Banka Nakit Etkisi:** Sonuç tarihinde
- **Risk Türü:** Araç riskine dönüşür
- **Dağıtım:** Ayrı kontrol

**Kontrol listesi:**

- [ ] Araç no mükerrer değil
- [ ] Kullanım amacı seçildi
- [ ] Risk ve nakitleşme politikası uygun
- [ ] Belge görseli eklendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### DBS Tahsilat / Red Sonucu

**Kod:** `dbsResult`  
**Sınıf:** Standart  
**Amaç:** Banka sonucunu kaydedin; tahsilatta kesin kapama, redde kapsam kaldırma yapın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Araç Türü | select | Evet | Alınan Çek / Verilen Çek / Alınan Senet / Verilen Senet / DBS |
| Cari / Karşı Taraf | text | Evet | Marmara Metal San. A.Ş. |
| Kullanım Amacı | select | Evet | Ödeme / Tahsilat / Teminat / Bankaya Tahsil / Bankaya Teminat / Ciro |
| Nominal / Brüt Tutar | amount | Evet | 1.200.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Teslim / İşlem Tarihi | date | Evet | 2026-08-24 |
| Vade / Valör Tarihi | date | Hayır | 2026-09-12 |
| Banka / Kart / POS | select | Evet | Ziraat / İş Bankası / Akbank / Garanti |
| Belge / Araç No | text | Hayır | CEK-2026-00212 |
| Banka Sonucu | select | Evet | Tam Tahsil / Kısmi Tahsil / Red / İptal |
| Tahsil Edilen | amount | Hayır | 6.250.000,00 |
| Red Nedeni | text | Hayır | — |

**Etki önizlemesi:**

- **Ticari Kapama:** Araç türüne göre
- **Banka Nakit Etkisi:** Sonuç tarihinde
- **Risk Türü:** Araç riskine dönüşür
- **Dağıtım:** Ayrı kontrol

**Kontrol listesi:**

- [ ] Araç no mükerrer değil
- [ ] Kullanım amacı seçildi
- [ ] Risk ve nakitleşme politikası uygun
- [ ] Belge görseli eklendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### POS Slip Kaydı

**Kod:** `posSlip`  
**Sınıf:** Sık kullanılan  
**Amaç:** Müşteri kart tahsilatını brüt tutarla cari kapamaya hazırlayın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Araç Türü | select | Evet | Alınan Çek / Verilen Çek / Alınan Senet / Verilen Senet / DBS |
| Cari / Karşı Taraf | text | Evet | Marmara Metal San. A.Ş. |
| Kullanım Amacı | select | Evet | Ödeme / Tahsilat / Teminat / Bankaya Tahsil / Bankaya Teminat / Ciro |
| Nominal / Brüt Tutar | amount | Evet | 1.200.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Teslim / İşlem Tarihi | date | Evet | 2026-08-24 |
| Vade / Valör Tarihi | date | Hayır | 2026-09-12 |
| Banka / Kart / POS | select | Evet | Ziraat / İş Bankası / Akbank / Garanti |
| Belge / Araç No | text | Hayır | CEK-2026-00212 |
| POS Terminali | select | Evet | Garanti POS 01 / Akbank POS 02 |
| Brüt Slip Tutarı | amount | Evet | 100.000,00 |
| Komisyon Oranı | text | Hayır | %3,00 |
| Beklenen Net Banka | amount | Hayır | 97.000,00 |

**Etki önizlemesi:**

- **Ticari Kapama:** Araç türüne göre
- **Banka Nakit Etkisi:** Sonuç tarihinde
- **Risk Türü:** Araç riskine dönüşür
- **Dağıtım:** Ayrı kontrol

**Kontrol listesi:**

- [ ] Araç no mükerrer değil
- [ ] Kullanım amacı seçildi
- [ ] Risk ve nakitleşme politikası uygun
- [ ] Belge görseli eklendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### POS Batch / Banka Eşleştirme

**Kod:** `posBatch`  
**Sınıf:** Standart  
**Amaç:** Toplu banka POS ödemesini slipler ve komisyonlarla çoktan çoğa eşleştirin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Araç Türü | select | Evet | Alınan Çek / Verilen Çek / Alınan Senet / Verilen Senet / DBS |
| Cari / Karşı Taraf | text | Evet | Marmara Metal San. A.Ş. |
| Kullanım Amacı | select | Evet | Ödeme / Tahsilat / Teminat / Bankaya Tahsil / Bankaya Teminat / Ciro |
| Nominal / Brüt Tutar | amount | Evet | 1.200.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Teslim / İşlem Tarihi | date | Evet | 2026-08-24 |
| Vade / Valör Tarihi | date | Hayır | 2026-09-12 |
| Banka / Kart / POS | select | Evet | Ziraat / İş Bankası / Akbank / Garanti |
| Belge / Araç No | text | Hayır | CEK-2026-00212 |
| Batch No | text | Evet | POSB-2026-0824 |
| Slip Sayısı | text | Hayır | 18 slip |
| Brüt Toplam | amount | Hayır | 2.420.000,00 |
| Net Banka | amount | Hayır | 2.348.600,00 |

**Etki önizlemesi:**

- **Ticari Kapama:** Araç türüne göre
- **Banka Nakit Etkisi:** Sonuç tarihinde
- **Risk Türü:** Araç riskine dönüşür
- **Dağıtım:** Ayrı kontrol

**Kontrol listesi:**

- [ ] Araç no mükerrer değil
- [ ] Kullanım amacı seçildi
- [ ] Risk ve nakitleşme politikası uygun
- [ ] Belge görseli eklendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Şirket Kredi Kartı Harcaması

**Kod:** `cardExpense`  
**Sınıf:** Sık kullanılan  
**Amaç:** Kart harcamasını tedarikçi faturası, gider veya avansa dağıtın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Araç Türü | select | Evet | Alınan Çek / Verilen Çek / Alınan Senet / Verilen Senet / DBS |
| Cari / Karşı Taraf | text | Evet | Marmara Metal San. A.Ş. |
| Kullanım Amacı | select | Evet | Ödeme / Tahsilat / Teminat / Bankaya Tahsil / Bankaya Teminat / Ciro |
| Nominal / Brüt Tutar | amount | Evet | 1.200.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Teslim / İşlem Tarihi | date | Evet | 2026-08-24 |
| Vade / Valör Tarihi | date | Hayır | 2026-09-12 |
| Banka / Kart / POS | select | Evet | Ziraat / İş Bankası / Akbank / Garanti |
| Belge / Araç No | text | Hayır | CEK-2026-00212 |
| Şirket Kartı | select | Evet | Garanti · **** 4218 / Akbank · **** 1180 |
| Harcama Türü | select | Evet | Tedarikçi Faturası / Masraf / Avans / Diğer |
| Harcama Tutarı | amount | Evet | 250.000,00 |

**Etki önizlemesi:**

- **Ticari Kapama:** Araç türüne göre
- **Banka Nakit Etkisi:** Sonuç tarihinde
- **Risk Türü:** Araç riskine dönüşür
- **Dağıtım:** Ayrı kontrol

**Kontrol listesi:**

- [ ] Araç no mükerrer değil
- [ ] Kullanım amacı seçildi
- [ ] Risk ve nakitleşme politikası uygun
- [ ] Belge görseli eklendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Kart Ekstresi ve Ödeme

**Kod:** `cardStatement`  
**Sınıf:** Standart  
**Amaç:** Kart harcamalarını ekstreye bağlayın ve banka ödemesiyle kart borcunu kapatın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Araç Türü | select | Evet | Alınan Çek / Verilen Çek / Alınan Senet / Verilen Senet / DBS |
| Cari / Karşı Taraf | text | Evet | Marmara Metal San. A.Ş. |
| Kullanım Amacı | select | Evet | Ödeme / Tahsilat / Teminat / Bankaya Tahsil / Bankaya Teminat / Ciro |
| Nominal / Brüt Tutar | amount | Evet | 1.200.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Teslim / İşlem Tarihi | date | Evet | 2026-08-24 |
| Vade / Valör Tarihi | date | Hayır | 2026-09-12 |
| Banka / Kart / POS | select | Evet | Ziraat / İş Bankası / Akbank / Garanti |
| Belge / Araç No | text | Hayır | CEK-2026-00212 |
| Ekstre Dönemi | month | Evet | 2026-08 |
| Ekstre Toplamı | amount | Evet | 5.650.000,00 |
| Son Ödeme | date | Evet | 2026-08-30 |
| Ödenecek Banka | select | Evet | Garanti · TL 3456 |

**Etki önizlemesi:**

- **Ticari Kapama:** Araç türüne göre
- **Banka Nakit Etkisi:** Sonuç tarihinde
- **Risk Türü:** Araç riskine dönüşür
- **Dağıtım:** Ayrı kontrol

**Kontrol listesi:**

- [ ] Araç no mükerrer değil
- [ ] Kullanım amacı seçildi
- [ ] Risk ve nakitleşme politikası uygun
- [ ] Belge görseli eklendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Çek / Senet Tahsilat veya Ödeme Sonucu

**Kod:** `checkSettlement`  
**Sınıf:** Standart  
**Amaç:** Araç vadesindeki gerçek banka sonucunu portföy, risk ve açık kalem ilişkilerine yansıtın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Araç Türü | select | Evet | Alınan Çek / Verilen Çek / Alınan Senet / Verilen Senet / DBS |
| Cari / Karşı Taraf | text | Evet | Marmara Metal San. A.Ş. |
| Kullanım Amacı | select | Evet | Ödeme / Tahsilat / Teminat / Bankaya Tahsil / Bankaya Teminat / Ciro |
| Nominal / Brüt Tutar | amount | Evet | 1.200.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Teslim / İşlem Tarihi | date | Evet | 2026-08-24 |
| Vade / Valör Tarihi | date | Hayır | 2026-09-12 |
| Banka / Kart / POS | select | Evet | Ziraat / İş Bankası / Akbank / Garanti |
| Belge / Araç No | text | Hayır | CEK-2026-00212 |
| Araç No | text | Evet | CEK-2026-00212 |
| Sonuç | select | Evet | Tahsil Edildi / Ödendi / Kısmi Tahsil / Kısmi Ödeme / İade |
| Gerçekleşen Tutar | amount | Evet | 1.200.000,00 |
| Banka İşlem / Valör Tarihi | date | Evet | 2026-09-12 |

**Etki önizlemesi:**

- **Ticari Kapama:** Araç türüne göre
- **Banka Nakit Etkisi:** Sonuç tarihinde
- **Risk Türü:** Araç riskine dönüşür
- **Dağıtım:** Ayrı kontrol

**Kontrol listesi:**

- [ ] Araç no mükerrer değil
- [ ] Kullanım amacı seçildi
- [ ] Risk ve nakitleşme politikası uygun
- [ ] Belge görseli eklendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### POS İade / Chargeback

**Kod:** `posChargeback`  
**Sınıf:** Standart  
**Amaç:** POS tahsilatının iade veya chargeback sonucunu müşteri borcu ve banka masrafıyla birlikte tersleyin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Araç Türü | select | Evet | Alınan Çek / Verilen Çek / Alınan Senet / Verilen Senet / DBS |
| Cari / Karşı Taraf | text | Evet | Marmara Metal San. A.Ş. |
| Kullanım Amacı | select | Evet | Ödeme / Tahsilat / Teminat / Bankaya Tahsil / Bankaya Teminat / Ciro |
| Nominal / Brüt Tutar | amount | Evet | 1.200.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Teslim / İşlem Tarihi | date | Evet | 2026-08-24 |
| Vade / Valör Tarihi | date | Hayır | 2026-09-12 |
| Banka / Kart / POS | select | Evet | Ziraat / İş Bankası / Akbank / Garanti |
| Belge / Araç No | text | Hayır | CEK-2026-00212 |
| POS Slip / Batch | text | Evet | POS-2026-00088 |
| İşlem | select | Evet | Müşteri İadesi / Chargeback / Kısmi İade |
| Terslenecek Brüt Tutar | amount | Evet | 100.000,00 |
| Chargeback Masrafı | amount | Hayır | 1.250,00 |
| Sonuç Tarihi | date | Evet | 2026-08-28 |

**Etki önizlemesi:**

- **Ticari Kapama:** Araç türüne göre
- **Banka Nakit Etkisi:** Sonuç tarihinde
- **Risk Türü:** Araç riskine dönüşür
- **Dağıtım:** Ayrı kontrol

**Kontrol listesi:**

- [ ] İlgili müşteri kapaması listelendi
- [ ] POS bekleyen alacak etkisi hesaplandı
- [ ] Masraf ayrı finans gideri olacak

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

## Nakit Planı

### Manuel Nakit Olayı

**Kod:** `cashPlan`  
**Sınıf:** Sık kullanılan  
**Amaç:** Vergi, maaş, enerji veya henüz belgeye bağlanmamış planlı nakit olayını ekleyin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Senaryo | select | Evet | Baz Senaryo / Tahsilat +15 gün / Stres / İyimser |
| Nakit Olayı Türü | select | Evet | Beklenen Tahsilat / Planlanan Ödeme / Kredi Kullanımı / Kredi Ödemesi / Vergi/SGK |
| Kaynak Belge / Referans | text | Hayır | MAN-2026-0041 |
| Beklenen Tarih | date | Evet | 2026-09-05 |
| Beklenen Valör | date | Hayır | 2026-09-05 |
| Tutar | amount | Evet | 4.500.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Güven Seviyesi | select | Evet | Kesinleşmiş / Yüksek / Orta / Düşük / Senaryo |

**Etki önizlemesi:**

- **13 Haftalık Etki:** İlgili haftaya yansır
- **Çifte Sayım:** Kaynak zinciriyle engellenir
- **Güven Seviyesi:** Ayrı gösterilir
- **Senaryo:** Gerçek kaydı değiştirmez

**Kontrol listesi:**

- [ ] Kaynak olay zinciri bulundu
- [ ] Çifte sayım engellendi
- [ ] Güven seviyesi belirlendi
- [ ] Senaryo gerçek kaydı değiştirmiyor

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Nakit Stres Senaryosu

**Kod:** `cashScenario`  
**Sınıf:** Gelişmiş  
**Amaç:** Tahsilat gecikmesi, ödeme ötelemesi veya limit daralması etkisini modelleyin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Senaryo | select | Evet | Baz Senaryo / Tahsilat +15 gün / Stres / İyimser |
| Nakit Olayı Türü | select | Evet | Beklenen Tahsilat / Planlanan Ödeme / Kredi Kullanımı / Kredi Ödemesi / Vergi/SGK |
| Kaynak Belge / Referans | text | Hayır | MAN-2026-0041 |
| Beklenen Tarih | date | Evet | 2026-09-05 |
| Beklenen Valör | date | Hayır | 2026-09-05 |
| Tutar | amount | Evet | 4.500.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Güven Seviyesi | select | Evet | Kesinleşmiş / Yüksek / Orta / Düşük / Senaryo |
| Tahsilat Gecikmesi | select | Evet | 0 gün / +7 gün / +15 gün / +30 gün |
| Limit Daralması | text | Hayır | -%10 |
| Faiz Artışı | text | Hayır | +250 baz puan |

**Etki önizlemesi:**

- **13 Haftalık Etki:** İlgili haftaya yansır
- **Çifte Sayım:** Kaynak zinciriyle engellenir
- **Güven Seviyesi:** Ayrı gösterilir
- **Senaryo:** Gerçek kaydı değiştirmez

**Kontrol listesi:**

- [ ] Kaynak olay zinciri bulundu
- [ ] Çifte sayım engellendi
- [ ] Güven seviyesi belirlendi
- [ ] Senaryo gerçek kaydı değiştirmiyor

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Nakit Açığı Eylem Planı

**Kod:** `fundingPlan`  
**Sınıf:** Gelişmiş  
**Amaç:** Tahsilat hızlandırma, ödeme bölme, limit kullanımı ve grup içi fonlama seçeneklerini karşılaştırın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Senaryo | select | Evet | Baz Senaryo / Tahsilat +15 gün / Stres / İyimser |
| Nakit Olayı Türü | select | Evet | Beklenen Tahsilat / Planlanan Ödeme / Kredi Kullanımı / Kredi Ödemesi / Vergi/SGK |
| Kaynak Belge / Referans | text | Hayır | MAN-2026-0041 |
| Beklenen Tarih | date | Evet | 2026-09-05 |
| Beklenen Valör | date | Hayır | 2026-09-05 |
| Tutar | amount | Evet | 4.500.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Güven Seviyesi | select | Evet | Kesinleşmiş / Yüksek / Orta / Düşük / Senaryo |
| Açık Dönemi | select | Evet | 7. Hafta / 8–9. Haftalar / 11–13. Haftalar |
| Maksimum Açık | amount | Evet | 52.200.000,00 |
| Eylem Seçeneği | select | Evet | Tahsilatı Erken Çek / Ödemeyi Böl / Rotatif Limit Kullan / Grup İçi Fonlama / Kombine Plan |
| Hedef Minimum Nakit | amount | Evet | 40.000.000,00 |

**Etki önizlemesi:**

- **13 Haftalık Etki:** İlgili haftaya yansır
- **Çifte Sayım:** Kaynak zinciriyle engellenir
- **Güven Seviyesi:** Ayrı gösterilir
- **Senaryo:** Gerçek kaydı değiştirmez

**Kontrol listesi:**

- [ ] Kaynak olay zinciri bulundu
- [ ] Çifte sayım engellendi
- [ ] Güven seviyesi belirlendi
- [ ] Senaryo gerçek kaydı değiştirmiyor

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

## Krediler ve Limitler

### Yeni Kredi Kaydı

**Kod:** `credit`  
**Sınıf:** Standart  
**Amaç:** Sade kredi kartı, ödeme planı, limit ve teminat ilişkisini oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Banka | select | Evet | Garanti BBVA / Türkiye Finans / VakıfBank / Halkbank / TEB |
| Kredi Türü | select | Evet | Spot / Taksitli Ticari / Rotatif/BCH / Döviz Kredisi / Katılım Finansmanı |
| Kredi / Sözleşme No | text | Evet | KRD-2026-018 |
| Brüt Kullandırım | amount | Evet | 50.000.000,00 |
| Net Hesaba Geçen | amount | Evet | 49.250.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Kullandırım Tarihi | date | Evet | 2026-08-24 |
| Vade Tarihi | date | Evet | 2027-08-24 |
| Faiz / Kâr Payı | text | Evet | %44,50 |
| Bağlı Limit / Facility | select | Hayır | Garanti · Genel Nakdi Limit / Bağımsız Kullandırım |

**Etki önizlemesi:**

- **Kredi Anapara:** 50.000.000 TL
- **Net Nakit Girişi:** 49.250.000 TL
- **Limit Kullanımı:** +50.000.000 TL
- **Borç Servisi:** Takvime eklenir

**Kontrol listesi:**

- [ ] Banka limiti yeterli
- [ ] Ödeme planı dengeli
- [ ] Teminat/belge koşulları kontrol edildi
- [ ] Nakit akışı ve borç servisi güncellenecek

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Kredi Ödeme Planı İçe Aktar

**Kod:** `creditPlanImport`  
**Sınıf:** Standart  
**Amaç:** Banka Excel/PDF planını anapara, faiz, vergi ve masraf satırlarına dönüştürün.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Banka | select | Evet | Garanti BBVA / Türkiye Finans / VakıfBank / Halkbank / TEB |
| Kredi Türü | select | Evet | Spot / Taksitli Ticari / Rotatif/BCH / Döviz Kredisi / Katılım Finansmanı |
| Kredi / Sözleşme No | text | Evet | KRD-2026-018 |
| Brüt Kullandırım | amount | Evet | 50.000.000,00 |
| Net Hesaba Geçen | amount | Evet | 49.250.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Kullandırım Tarihi | date | Evet | 2026-08-24 |
| Vade Tarihi | date | Evet | 2027-08-24 |
| Faiz / Kâr Payı | text | Evet | %44,50 |
| Bağlı Limit / Facility | select | Hayır | Garanti · Genel Nakdi Limit / Bağımsız Kullandırım |
| Plan Kaynağı | select | Evet | Excel / PDF / Banka Portalı |
| Satır Sayısı | text | Hayır | 12 taksit |
| İlk Vade | date | Hayır | 2026-09-24 |

**Etki önizlemesi:**

- **Kredi Anapara:** 50.000.000 TL
- **Net Nakit Girişi:** 49.250.000 TL
- **Limit Kullanımı:** +50.000.000 TL
- **Borç Servisi:** Takvime eklenir

**Kontrol listesi:**

- [ ] Banka limiti yeterli
- [ ] Ödeme planı dengeli
- [ ] Teminat/belge koşulları kontrol edildi
- [ ] Nakit akışı ve borç servisi güncellenecek

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Kredi Ödeme / Erken Kapama / Yapılandırma

**Kod:** `creditEvent`  
**Sınıf:** Standart  
**Amaç:** Kredi olayını plan ve gerçek banka çıkışıyla kaydedin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Banka | select | Evet | Garanti BBVA / Türkiye Finans / VakıfBank / Halkbank / TEB |
| Kredi Türü | select | Evet | Spot / Taksitli Ticari / Rotatif/BCH / Döviz Kredisi / Katılım Finansmanı |
| Kredi / Sözleşme No | text | Evet | KRD-2026-018 |
| Brüt Kullandırım | amount | Evet | 50.000.000,00 |
| Net Hesaba Geçen | amount | Evet | 49.250.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Kullandırım Tarihi | date | Evet | 2026-08-24 |
| Vade Tarihi | date | Evet | 2027-08-24 |
| Faiz / Kâr Payı | text | Evet | %44,50 |
| Bağlı Limit / Facility | select | Hayır | Garanti · Genel Nakdi Limit / Bağımsız Kullandırım |
| Kredi Olayı | select | Evet | Normal Taksit / Kısmi Ödeme / Erken Kapama / Vade Uzatımı / Yapılandırma |
| İşlem Tutarı | amount | Evet | 8.450.000,00 |
| Yeni Vade | date | Hayır | 2027-12-24 |

**Etki önizlemesi:**

- **Kredi Anapara:** 50.000.000 TL
- **Net Nakit Girişi:** 49.250.000 TL
- **Limit Kullanımı:** +50.000.000 TL
- **Borç Servisi:** Takvime eklenir

**Kontrol listesi:**

- [ ] Banka limiti yeterli
- [ ] Ödeme planı dengeli
- [ ] Teminat/belge koşulları kontrol edildi
- [ ] Nakit akışı ve borç servisi güncellenecek

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Yeni Banka Limiti

**Kod:** `limit`  
**Sınıf:** Standart  
**Amaç:** Etkin, kullanılan, rezerve, bloke ve kullanılabilir limit kaydı oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Banka | select | Evet | Garanti BBVA / Türkiye Finans / VakıfBank / Akbank |
| Limit Türü | select | Evet | Genel Nakdi / Spot/Taksitli / Rotatif/BCH / Gayrinakdi / Teminat Mektubu |
| Etkin Limit | amount | Evet | 82.900.000,00 |
| Kullanılan | amount | Hayır | 25.000.000,00 |
| Rezerve | amount | Hayır | 0,00 |
| Bloke | amount | Hayır | 0,00 |
| Başlangıç | date | Evet | 2026-08-24 |
| Bitiş | date | Evet | 2027-08-24 |
| Üst Limit / Ortak Havuz | select | Hayır | Garanti · Genel İlişki Limiti / Bağımsız |

**Etki önizlemesi:**

- **Etkin Limit:** 82.900.000 TL
- **Kullanılan:** 25.000.000 TL
- **Rezerve / Bloke:** 0 TL
- **Kullanılabilir:** 57.900.000 TL

**Kontrol listesi:**

- [ ] Üst limit ilişkisi kontrol edildi
- [ ] Kullanım limiti aşmıyor
- [ ] Belge/teminat durumu doğrulandı
- [ ] Vade uyarısı oluşturuldu

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Limit Yenileme / Revizyon

**Kod:** `limitRenewal`  
**Sınıf:** Standart  
**Amaç:** Mevcut limitin süre, tutar, alt limit veya koşullarını tarihçeli revize edin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Banka | select | Evet | Garanti BBVA / Türkiye Finans / VakıfBank / Akbank |
| Limit Türü | select | Evet | Genel Nakdi / Spot/Taksitli / Rotatif/BCH / Gayrinakdi / Teminat Mektubu |
| Etkin Limit | amount | Evet | 82.900.000,00 |
| Kullanılan | amount | Hayır | 25.000.000,00 |
| Rezerve | amount | Hayır | 0,00 |
| Bloke | amount | Hayır | 0,00 |
| Başlangıç | date | Evet | 2026-08-24 |
| Bitiş | date | Evet | 2027-08-24 |
| Üst Limit / Ortak Havuz | select | Hayır | Garanti · Genel İlişki Limiti / Bağımsız |
| Mevcut Limit | text | Evet | LMT-2026-0041 |
| Revizyon Türü | select | Evet | Süre Uzatma / Tutar Artışı / Tutar Azalışı / Koşul Değişikliği |
| Yeni Etkin Limit | amount | Hayır | 95.000.000,00 |

**Etki önizlemesi:**

- **Etkin Limit:** 82.900.000 TL
- **Kullanılan:** 25.000.000 TL
- **Rezerve / Bloke:** 0 TL
- **Kullanılabilir:** 57.900.000 TL

**Kontrol listesi:**

- [ ] Üst limit ilişkisi kontrol edildi
- [ ] Kullanım limiti aşmıyor
- [ ] Belge/teminat durumu doğrulandı
- [ ] Vade uyarısı oluşturuldu

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Diğer Finansman Aracı

**Kod:** `otherFinance`  
**Sınıf:** Gelişmiş  
**Amaç:** Factoring, leasing, grup içi finansman veya kısa vadeli yatırımı kaydedin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Banka | select | Evet | Garanti BBVA / Türkiye Finans / VakıfBank / Halkbank / TEB |
| Kredi Türü | select | Evet | Spot / Taksitli Ticari / Rotatif/BCH / Döviz Kredisi / Katılım Finansmanı |
| Kredi / Sözleşme No | text | Evet | KRD-2026-018 |
| Brüt Kullandırım | amount | Evet | 50.000.000,00 |
| Net Hesaba Geçen | amount | Evet | 49.250.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Kullandırım Tarihi | date | Evet | 2026-08-24 |
| Vade Tarihi | date | Evet | 2027-08-24 |
| Faiz / Kâr Payı | text | Evet | %44,50 |
| Bağlı Limit / Facility | select | Hayır | Garanti · Genel Nakdi Limit / Bağımsız Kullandırım |
| Araç Türü | select | Evet | Factoring / Leasing / Grup İçi Finansman / Vadeli Mevduat / Akreditif |
| Net Nakit Etkisi | amount | Hayır | 12.100.000,00 |

**Etki önizlemesi:**

- **Kredi Anapara:** 50.000.000 TL
- **Net Nakit Girişi:** 49.250.000 TL
- **Limit Kullanımı:** +50.000.000 TL
- **Borç Servisi:** Takvime eklenir

**Kontrol listesi:**

- [ ] Banka limiti yeterli
- [ ] Ödeme planı dengeli
- [ ] Teminat/belge koşulları kontrol edildi
- [ ] Nakit akışı ve borç servisi güncellenecek

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Kredi Teklif Karşılaştırma

**Kod:** `creditOfferCompare`  
**Sınıf:** Gelişmiş  
**Amaç:** Banka tekliflerini net nakit, toplam maliyet, vade, teminat ve covenant yüküyle karşılaştırın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Banka | select | Evet | Garanti BBVA / Türkiye Finans / VakıfBank / Halkbank / TEB |
| Kredi Türü | select | Evet | Spot / Taksitli Ticari / Rotatif/BCH / Döviz Kredisi / Katılım Finansmanı |
| Kredi / Sözleşme No | text | Evet | KRD-2026-018 |
| Brüt Kullandırım | amount | Evet | 50.000.000,00 |
| Net Hesaba Geçen | amount | Evet | 49.250.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Kullandırım Tarihi | date | Evet | 2026-08-24 |
| Vade Tarihi | date | Evet | 2027-08-24 |
| Faiz / Kâr Payı | text | Evet | %44,50 |
| Bağlı Limit / Facility | select | Hayır | Garanti · Genel Nakdi Limit / Bağımsız Kullandırım |
| Finansman İhtiyacı | amount | Evet | 50.000.000,00 |
| Hedef Vade | select | Evet | 3 Ay / 6 Ay / 12 Ay / 24 Ay |
| Teklif Sayısı | text | Hayır | 3 banka |
| Karşılaştırma Ölçütü | select | Evet | Toplam Maliyet / Efektif Yıllık Maliyet / Nakit Akışı / Teminat Yükü / Dengeli Skor |

**Etki önizlemesi:**

- **Kredi Anapara:** 50.000.000 TL
- **Net Nakit Girişi:** 49.250.000 TL
- **Limit Kullanımı:** +50.000.000 TL
- **Borç Servisi:** Takvime eklenir

**Kontrol listesi:**

- [ ] Banka limiti yeterli
- [ ] Ödeme planı dengeli
- [ ] Teminat/belge koşulları kontrol edildi
- [ ] Nakit akışı ve borç servisi güncellenecek

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

## Teminatlar ve Yükümlülükler

### Yeni Teminat Kaydı

**Kod:** `collateral`  
**Sınıf:** Standart  
**Amaç:** Teminat envanteri, kabul edilen değer ve ilk tahsis ilişkisini oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Yön | select | Evet | Verilen Teminat / Alınan Teminat |
| Teminat Türü | select | Evet | İpotek / Makine Rehni / Taşıt Rehni / Nakit Blokaj / Mevduat Rehni |
| Teminat Sahibi | text | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Karşı Taraf | select | Evet | Ziraat Bankası / İş Bankası / VakıfBank / Garanti BBVA |
| Nominal Değer | amount | Evet | 90.000.000,00 |
| Bankaca Kabul Edilen | amount | Hayır | 75.000.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Başlangıç Tarihi | date | Hayır | 2026-08-24 |
| Vade / Kontrol Tarihi | date | Hayır | 2027-08-24 |
| Bağlı Kredi / Limit | select | Hayır | Ziraat · Genel Nakdi Limit / Henüz tahsis edilmedi |

**Etki önizlemesi:**

- **Kabul Edilen Değer:** 75.000.000 TL
- **Aktif Tahsis:** 62.000.000 TL
- **Serbest Değer:** 13.000.000 TL
- **Risk Karşılama:** Yeniden hesaplanır

**Kontrol listesi:**

- [ ] Değerleme belgesi mevcut
- [ ] Tahsis toplamı kabul edilen değeri aşmıyor
- [ ] Aktif risk etkisi hesaplandı
- [ ] Çözüm için onay seviyesi belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Teminat Değerleme Güncellemesi

**Kod:** `collateralValuation`  
**Sınıf:** Standart  
**Amaç:** Yeni ekspertiz/yönetim değeri girin; eski değer üzerine yazmayın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Yön | select | Evet | Verilen Teminat / Alınan Teminat |
| Teminat Türü | select | Evet | İpotek / Makine Rehni / Taşıt Rehni / Nakit Blokaj / Mevduat Rehni |
| Teminat Sahibi | text | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Karşı Taraf | select | Evet | Ziraat Bankası / İş Bankası / VakıfBank / Garanti BBVA |
| Nominal Değer | amount | Evet | 90.000.000,00 |
| Bankaca Kabul Edilen | amount | Hayır | 75.000.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Başlangıç Tarihi | date | Hayır | 2026-08-24 |
| Vade / Kontrol Tarihi | date | Hayır | 2027-08-24 |
| Bağlı Kredi / Limit | select | Hayır | Ziraat · Genel Nakdi Limit / Henüz tahsis edilmedi |
| Değerleme Türü | select | Evet | Ekspertiz / Yönetim Değeri / Banka Kabul Değeri |
| Yeni Değer | amount | Evet | 92.500.000,00 |
| Değerleme Tarihi | date | Evet | 2026-08-24 |
| Geçerlilik Bitişi | date | Hayır | 2027-08-24 |

**Etki önizlemesi:**

- **Kabul Edilen Değer:** 75.000.000 TL
- **Aktif Tahsis:** 62.000.000 TL
- **Serbest Değer:** 13.000.000 TL
- **Risk Karşılama:** Yeniden hesaplanır

**Kontrol listesi:**

- [ ] Değerleme belgesi mevcut
- [ ] Tahsis toplamı kabul edilen değeri aşmıyor
- [ ] Aktif risk etkisi hesaplandı
- [ ] Çözüm için onay seviyesi belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Teminat Tahsisi

**Kod:** `collateralAllocation`  
**Sınıf:** Standart  
**Amaç:** Bir teminatı bir veya daha fazla kredi/limit riskine bağlayın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Yön | select | Evet | Verilen Teminat / Alınan Teminat |
| Teminat Türü | select | Evet | İpotek / Makine Rehni / Taşıt Rehni / Nakit Blokaj / Mevduat Rehni |
| Teminat Sahibi | text | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Karşı Taraf | select | Evet | Ziraat Bankası / İş Bankası / VakıfBank / Garanti BBVA |
| Nominal Değer | amount | Evet | 90.000.000,00 |
| Bankaca Kabul Edilen | amount | Hayır | 75.000.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Başlangıç Tarihi | date | Hayır | 2026-08-24 |
| Vade / Kontrol Tarihi | date | Hayır | 2027-08-24 |
| Bağlı Kredi / Limit | select | Hayır | Ziraat · Genel Nakdi Limit / Henüz tahsis edilmedi |
| Tahsis Edilecek Değer | amount | Evet | 62.000.000,00 |
| Hedef Kredi / Limit | select | Evet | Ziraat · Genel Nakdi Limit / KRD-2026-018 |
| Tahsis Sırası | select | Evet | 1. Derece / 2. Derece / Pari Passu |

**Etki önizlemesi:**

- **Kabul Edilen Değer:** 75.000.000 TL
- **Aktif Tahsis:** 62.000.000 TL
- **Serbest Değer:** 13.000.000 TL
- **Risk Karşılama:** Yeniden hesaplanır

**Kontrol listesi:**

- [ ] Değerleme belgesi mevcut
- [ ] Tahsis toplamı kabul edilen değeri aşmıyor
- [ ] Aktif risk etkisi hesaplandı
- [ ] Çözüm için onay seviyesi belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Teminat Çözüm / Serbest Bırakma

**Kod:** `collateralRelease`  
**Sınıf:** Standart  
**Amaç:** Aktif risk etkisini gösterip kontrollü çözüm talebi oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Yön | select | Evet | Verilen Teminat / Alınan Teminat |
| Teminat Türü | select | Evet | İpotek / Makine Rehni / Taşıt Rehni / Nakit Blokaj / Mevduat Rehni |
| Teminat Sahibi | text | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Karşı Taraf | select | Evet | Ziraat Bankası / İş Bankası / VakıfBank / Garanti BBVA |
| Nominal Değer | amount | Evet | 90.000.000,00 |
| Bankaca Kabul Edilen | amount | Hayır | 75.000.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Başlangıç Tarihi | date | Hayır | 2026-08-24 |
| Vade / Kontrol Tarihi | date | Hayır | 2027-08-24 |
| Bağlı Kredi / Limit | select | Hayır | Ziraat · Genel Nakdi Limit / Henüz tahsis edilmedi |
| Çözüm Türü | select | Evet | Tam Çözüm / Kısmi Serbest / Tahsis Değişikliği |
| Serbest Bırakılacak Değer | amount | Evet | 13.000.000,00 |
| Çözüm Gerekçesi | textarea | Evet | Kredi riski azaldı. |

**Etki önizlemesi:**

- **Kabul Edilen Değer:** 75.000.000 TL
- **Aktif Tahsis:** 62.000.000 TL
- **Serbest Değer:** 13.000.000 TL
- **Risk Karşılama:** Yeniden hesaplanır

**Kontrol listesi:**

- [ ] Aktif kredi riski kontrol edildi
- [ ] Teminat karşılama oranı yeniden hesaplandı
- [ ] CFO onayı gerekli

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Teminat Mektubu Kaydı

**Kod:** `bankGuarantee`  
**Sınıf:** Gelişmiş  
**Amaç:** Verilen/alınan teminat mektubunu gayrinakdi limit ve yükümlülükle ilişkilendirin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Yön | select | Evet | Verilen Teminat / Alınan Teminat |
| Teminat Türü | select | Evet | İpotek / Makine Rehni / Taşıt Rehni / Nakit Blokaj / Mevduat Rehni |
| Teminat Sahibi | text | Evet | Göymen Demir Çelik A.Ş. |
| Banka / Karşı Taraf | select | Evet | Ziraat Bankası / İş Bankası / VakıfBank / Garanti BBVA |
| Nominal Değer | amount | Evet | 90.000.000,00 |
| Bankaca Kabul Edilen | amount | Hayır | 75.000.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Başlangıç Tarihi | date | Hayır | 2026-08-24 |
| Vade / Kontrol Tarihi | date | Hayır | 2027-08-24 |
| Bağlı Kredi / Limit | select | Hayır | Ziraat · Genel Nakdi Limit / Henüz tahsis edilmedi |
| Mektup Yönü | select | Evet | Verilen / Alınan |
| Muhatap / Lehtar | text | Evet | Kamu Kurumu / Müşteri |
| Mektup Türü | select | Evet | Kesin / Geçici / Avans / Gümrük / Diğer |
| Mektup Tutarı | amount | Evet | 15.000.000,00 |

**Etki önizlemesi:**

- **Kabul Edilen Değer:** 75.000.000 TL
- **Aktif Tahsis:** 62.000.000 TL
- **Serbest Değer:** 13.000.000 TL
- **Risk Karşılama:** Yeniden hesaplanır

**Kontrol listesi:**

- [ ] Değerleme belgesi mevcut
- [ ] Tahsis toplamı kabul edilen değeri aşmıyor
- [ ] Aktif risk etkisi hesaplandı
- [ ] Çözüm için onay seviyesi belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Banka Yükümlülüğü / Covenant Görevi

**Kod:** `bankObligation`  
**Sınıf:** Standart  
**Amaç:** Belge, oran, ekspertiz, sigorta veya limit yenileme yükümlülüğü oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Kontrol Türü | select | Evet | Onay / Mutabakat / Vade/Yükümlülük / Limit/Teminat / Dönem Kapama |
| Önem | select | Evet | Kritik / Yüksek / Orta / Bilgi |
| Kaynak Kayıt | text | Evet | CTL-2026-0421 |
| Sorumlu | select | Evet | Finans Operasyon / Hazine / Finans Müdürü / CFO/Yönetim |
| Son Tarih | date | Evet | 2026-08-24 |
| Finansal Etki | amount | Hayır | 12.850.000,00 |
| Durum | select | Evet | Açık / İnceleniyor / Onay Bekliyor / Çözüldü |
| Yükümlülük | select | Evet | Finansal Paket Gönderimi / Limit Yenileme / Ekspertiz / Sigorta / Finansal Covenant |
| Banka / Facility | select | Evet | Türkiye Finans / Garanti BBVA / VakıfBank |
| Ölçüm / Teslim Tarihi | date | Evet | 2026-08-30 |

**Etki önizlemesi:**

- **Finansal Etki:** 12.850.000 TL
- **Kapanış Etkisi:** Kontrol edilir
- **Sorumlu:** Atanır
- **Audit:** Çözüm geçmişi korunur

**Kontrol listesi:**

- [ ] Kaynak kayıt mevcut
- [ ] Sorumlu ve son tarih tanımlı
- [ ] Kapanışı engelleme durumu belirlendi
- [ ] Çözüm geçmişi korunacak

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

## Nakit Bütçesi

### Nakit Bütçesi Girişi

**Kod:** `budget`  
**Sınıf:** Standart  
**Amaç:** Aylık nakit giriş/çıkış planı ve güncel tahmini girin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Bütçe Sürümü | select | Evet | Onaylı Bütçe 2026 v1 / RF-03 / Yeni Taslak |
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Dönem | month | Evet | 2026-09 |
| Kategori | select | Evet | Müşteri Tahsilatları / Hammadde / Personel/SGK / Vergi / Enerji |
| Yön | select | Evet | Nakit Girişi / Nakit Çıkışı |
| Planlanan Tutar | amount | Evet | 12.500.000,00 |
| Güncel Tahmin | amount | Hayır | 13.250.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |

**Etki önizlemesi:**

- **Planlanan:** 12.500.000 TL
- **Gerçekleşen:** Kaynak hareketlerden
- **Güncel Tahmin:** 13.250.000 TL
- **Sapma:** +750.000 TL

**Kontrol listesi:**

- [ ] Bütçe sürümü açık
- [ ] Kategori ve dönem geçerli
- [ ] Transfer mükerrer değil
- [ ] Nakit gerçekleşme kaynağı belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Bütçe Transfer / Revizyon

**Kod:** `budgetTransfer`  
**Sınıf:** Standart  
**Amaç:** Onaylı bütçe üzerine yazmadan iki kategori arasında kontrollü transfer yapın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Bütçe Sürümü | select | Evet | Onaylı Bütçe 2026 v1 / RF-03 / Yeni Taslak |
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Dönem | month | Evet | 2026-09 |
| Kategori | select | Evet | Müşteri Tahsilatları / Hammadde / Personel/SGK / Vergi / Enerji |
| Yön | select | Evet | Nakit Girişi / Nakit Çıkışı |
| Planlanan Tutar | amount | Evet | 12.500.000,00 |
| Güncel Tahmin | amount | Hayır | 13.250.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Kaynak Kategori | select | Evet | Diğer Giderler / CAPEX / Nakliye |
| Hedef Kategori | select | Evet | Enerji / Bakım / Hammadde |
| Transfer Tutarı | amount | Evet | 1.250.000,00 |

**Etki önizlemesi:**

- **Planlanan:** 12.500.000 TL
- **Gerçekleşen:** Kaynak hareketlerden
- **Güncel Tahmin:** 13.250.000 TL
- **Sapma:** +750.000 TL

**Kontrol listesi:**

- [ ] Bütçe sürümü açık
- [ ] Kategori ve dönem geçerli
- [ ] Transfer mükerrer değil
- [ ] Nakit gerçekleşme kaynağı belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Bütçe Sürümü / Kopyalama

**Kod:** `budgetVersion`  
**Sınıf:** Standart  
**Amaç:** Onaylı bütçe, rolling forecast veya stres senaryosu için yeni sürüm oluşturun.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Bütçe Sürümü | select | Evet | Onaylı Bütçe 2026 v1 / RF-03 / Yeni Taslak |
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Dönem | month | Evet | 2026-09 |
| Kategori | select | Evet | Müşteri Tahsilatları / Hammadde / Personel/SGK / Vergi / Enerji |
| Yön | select | Evet | Nakit Girişi / Nakit Çıkışı |
| Planlanan Tutar | amount | Evet | 12.500.000,00 |
| Güncel Tahmin | amount | Hayır | 13.250.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Sürüm Türü | select | Evet | Revize Bütçe / Rolling Forecast / Stres Senaryosu / İyimser Senaryo |
| Kaynak Sürüm | select | Evet | BÜTÇE-2026-v1 / RF-03 |
| Yeni Sürüm Kodu | text | Evet | RF-04 |

**Etki önizlemesi:**

- **Planlanan:** 12.500.000 TL
- **Gerçekleşen:** Kaynak hareketlerden
- **Güncel Tahmin:** 13.250.000 TL
- **Sapma:** +750.000 TL

**Kontrol listesi:**

- [ ] Bütçe sürümü açık
- [ ] Kategori ve dönem geçerli
- [ ] Transfer mükerrer değil
- [ ] Nakit gerçekleşme kaynağı belirlendi

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Bütçe Sürümü Onayı

**Kod:** `budgetApproval`  
**Sınıf:** Standart  
**Amaç:** Taslak veya revize nakit bütçesinin toplam, varsayım, sapma ve yetki kontrolleriyle onayını yönetin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Bütçe Sürümü | select | Evet | Onaylı Bütçe 2026 v1 / RF-03 / Yeni Taslak |
| Şirket | select | Evet | Göymen Demir Çelik A.Ş. |
| Dönem | month | Evet | 2026-09 |
| Kategori | select | Evet | Müşteri Tahsilatları / Hammadde / Personel/SGK / Vergi / Enerji |
| Yön | select | Evet | Nakit Girişi / Nakit Çıkışı |
| Planlanan Tutar | amount | Evet | 12.500.000,00 |
| Güncel Tahmin | amount | Hayır | 13.250.000,00 |
| Para Birimi | select | Evet | TL / USD / EUR |
| Onaylanacak Sürüm | select | Evet | RF-04 / Revize Bütçe 2026 v2 |
| Karar | select | Evet | Onayla / Düzeltmeye Gönder / Reddet |
| Karar Notu | textarea | Evet | Kur ve enerji varsayımları yönetim kararıyla uyumludur. |

**Etki önizlemesi:**

- **Planlanan:** 12.500.000 TL
- **Gerçekleşen:** Kaynak hareketlerden
- **Güncel Tahmin:** 13.250.000 TL
- **Sapma:** +750.000 TL

**Kontrol listesi:**

- [ ] Bütçe sürümü açık
- [ ] Kategori ve dönem geçerli
- [ ] Transfer mükerrer değil
- [ ] Nakit gerçekleşme kaynağı belirlendi

**Nihai aksiyon:** Bütçe Kararını Kaydet

---

## Kontrol ve Kapanış

### İstisna Çözüm Kaydı

**Kod:** `controlResolution`  
**Sınıf:** Sık kullanılan  
**Amaç:** Kontrol maddesini kaynağa bağlayın, sorumlu atayın ve çözümü belgeleyin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Kontrol Türü | select | Evet | Onay / Mutabakat / Vade/Yükümlülük / Limit/Teminat / Dönem Kapama |
| Önem | select | Evet | Kritik / Yüksek / Orta / Bilgi |
| Kaynak Kayıt | text | Evet | CTL-2026-0421 |
| Sorumlu | select | Evet | Finans Operasyon / Hazine / Finans Müdürü / CFO/Yönetim |
| Son Tarih | date | Evet | 2026-08-24 |
| Finansal Etki | amount | Hayır | 12.850.000,00 |
| Durum | select | Evet | Açık / İnceleniyor / Onay Bekliyor / Çözüldü |
| Çözüm Aksiyonu | select | Evet | Düzeltme Yapıldı / Onay Alındı / Risk Kabulü / Takip Görevi / Reddedildi |
| Çözüm Notu | textarea | Evet | Kaynak hareket doğru cariye eşleştirildi. |

**Etki önizlemesi:**

- **Finansal Etki:** 12.850.000 TL
- **Kapanış Etkisi:** Kontrol edilir
- **Sorumlu:** Atanır
- **Audit:** Çözüm geçmişi korunur

**Kontrol listesi:**

- [ ] Kaynak kayıt mevcut
- [ ] Sorumlu ve son tarih tanımlı
- [ ] Kapanışı engelleme durumu belirlendi
- [ ] Çözüm geçmişi korunacak

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Finans Gün Sonu

**Kod:** `dailyClose`  
**Sınıf:** Sık kullanılan  
**Amaç:** Banka dosyaları, kasa, mutabakat, dağıtım ve kredi olaylarını kontrol ederek günü kapatın.

**Adımlar:** 1 Banka → 2 Kasa → 3 Mutabakat → 4 Dağıtım → 5 Kapat

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Kontrol Türü | select | Evet | Onay / Mutabakat / Vade/Yükümlülük / Limit/Teminat / Dönem Kapama |
| Önem | select | Evet | Kritik / Yüksek / Orta / Bilgi |
| Kaynak Kayıt | text | Evet | CTL-2026-0421 |
| Sorumlu | select | Evet | Finans Operasyon / Hazine / Finans Müdürü / CFO/Yönetim |
| Son Tarih | date | Evet | 2026-08-24 |
| Finansal Etki | amount | Hayır | 12.850.000,00 |
| Durum | select | Evet | Açık / İnceleniyor / Onay Bekliyor / Çözüldü |
| Kapanış Tarihi | date | Evet | 2026-08-24 |
| Kapanışı Engelleyen | text | Hayır | 3 mutabakatsız banka satırı |
| Kapanış Kararı | select | Evet | Kontrol Devam Ediyor / İstisna ile Kapat / Kapat |

**Etki önizlemesi:**

- **Finansal Etki:** 12.850.000 TL
- **Kapanış Etkisi:** Kontrol edilir
- **Sorumlu:** Atanır
- **Audit:** Çözüm geçmişi korunur

**Kontrol listesi:**

- [ ] Kaynak kayıt mevcut
- [ ] Sorumlu ve son tarih tanımlı
- [ ] Kapanışı engelleme durumu belirlendi
- [ ] Çözüm geçmişi korunacak

**Nihai aksiyon:** Gün Sonunu Kapat

---

### Ay Sonu / Dönem Kilidi

**Kod:** `periodClose`  
**Sınıf:** Gelişmiş  
**Amaç:** Finans dönemini kontrol listesi, snapshot ve yeniden açma kurallarıyla kilitleyin.

**Adımlar:** 1 Banka/Kasa → 2 Araçlar → 3 Kredi/Limit → 4 Bütçe → 5 Kilit

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Kontrol Türü | select | Evet | Onay / Mutabakat / Vade/Yükümlülük / Limit/Teminat / Dönem Kapama |
| Önem | select | Evet | Kritik / Yüksek / Orta / Bilgi |
| Kaynak Kayıt | text | Evet | CTL-2026-0421 |
| Sorumlu | select | Evet | Finans Operasyon / Hazine / Finans Müdürü / CFO/Yönetim |
| Son Tarih | date | Evet | 2026-08-24 |
| Finansal Etki | amount | Hayır | 12.850.000,00 |
| Durum | select | Evet | Açık / İnceleniyor / Onay Bekliyor / Çözüldü |
| Dönem | month | Evet | 2026-08 |
| Snapshot | select | Evet | 13 Haftalık Nakit RF-03 / Bütçe Gerçekleşen Ağustos |
| Kilit Durumu | select | Evet | Kontrol / Kilitlenmeye Hazır / Kilitli |

**Etki önizlemesi:**

- **Finansal Etki:** 12.850.000 TL
- **Kapanış Etkisi:** Kontrol edilir
- **Sorumlu:** Atanır
- **Audit:** Çözüm geçmişi korunur

**Kontrol listesi:**

- [ ] Kaynak kayıt mevcut
- [ ] Sorumlu ve son tarih tanımlı
- [ ] Kapanışı engelleme durumu belirlendi
- [ ] Çözüm geçmişi korunacak

**Nihai aksiyon:** Dönemi Kilitle

---

### Genel Finans Onay Kararı

**Kod:** `approvalDecision`  
**Sınıf:** Standart  
**Amaç:** Ödeme dışındaki kredi, limit, teminat, bütçe ve risk istisnası onaylarını ortak karar ekranında yönetin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Kontrol Türü | select | Evet | Onay / Mutabakat / Vade/Yükümlülük / Limit/Teminat / Dönem Kapama |
| Önem | select | Evet | Kritik / Yüksek / Orta / Bilgi |
| Kaynak Kayıt | text | Evet | CTL-2026-0421 |
| Sorumlu | select | Evet | Finans Operasyon / Hazine / Finans Müdürü / CFO/Yönetim |
| Son Tarih | date | Evet | 2026-08-24 |
| Finansal Etki | amount | Hayır | 12.850.000,00 |
| Durum | select | Evet | Açık / İnceleniyor / Onay Bekliyor / Çözüldü |
| Onay Türü | select | Evet | Kredi / Limit / Teminat / Bütçe / Cari Risk İstisnası |
| Kaynak Kayıt | text | Evet | APR-2026-0048 |
| Karar | select | Evet | Onayla / Düzeltmeye Gönder / Reddet |
| Karar Notu | textarea | Evet | Etki özeti ve belgeler incelendi. |

**Etki önizlemesi:**

- **Finansal Etki:** 12.850.000 TL
- **Kapanış Etkisi:** Kontrol edilir
- **Sorumlu:** Atanır
- **Audit:** Çözüm geçmişi korunur

**Kontrol listesi:**

- [ ] Kaynak kayıt mevcut
- [ ] Sorumlu ve son tarih tanımlı
- [ ] Kapanışı engelleme durumu belirlendi
- [ ] Çözüm geçmişi korunacak

**Nihai aksiyon:** Onay Kararını Kaydet

---

### Mükerrer Ödeme / Kayıt İncelemesi

**Kod:** `duplicateReview`  
**Sınıf:** Standart  
**Amaç:** Cari, tutar, tarih, IBAN, banka referansı ve belge hash eşleşmesini inceleyin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Kontrol Türü | select | Evet | Onay / Mutabakat / Vade/Yükümlülük / Limit/Teminat / Dönem Kapama |
| Önem | select | Evet | Kritik / Yüksek / Orta / Bilgi |
| Kaynak Kayıt | text | Evet | CTL-2026-0421 |
| Sorumlu | select | Evet | Finans Operasyon / Hazine / Finans Müdürü / CFO/Yönetim |
| Son Tarih | date | Evet | 2026-08-24 |
| Finansal Etki | amount | Hayır | 12.850.000,00 |
| Durum | select | Evet | Açık / İnceleniyor / Onay Bekliyor / Çözüldü |
| Şüpheli Kayıt | text | Evet | BNK-2026-00851 |
| Benzer Kayıt | text | Evet | BNK-2026-00844 |
| Benzerlik | text | Hayır | %94 |
| Karar | select | Evet | Mükerrer — Engelle / Farklı İşlem — Devam / Düzeltme İste |

**Etki önizlemesi:**

- **Finansal Etki:** 12.850.000 TL
- **Kapanış Etkisi:** Kontrol edilir
- **Sorumlu:** Atanır
- **Audit:** Çözüm geçmişi korunur

**Kontrol listesi:**

- [ ] Kaynak kayıt mevcut
- [ ] Sorumlu ve son tarih tanımlı
- [ ] Kapanışı engelleme durumu belirlendi
- [ ] Çözüm geçmişi korunacak

**Nihai aksiyon:** Kontrol Et ve Onaya Gönder

---

### Finans Dönemini Yeniden Açma

**Kod:** `periodReopen`  
**Sınıf:** Gelişmiş  
**Amaç:** Kilitli finans dönemini gerekçe, etki ve üst onayla geçici olarak yeniden açın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Kontrol Türü | select | Evet | Onay / Mutabakat / Vade/Yükümlülük / Limit/Teminat / Dönem Kapama |
| Önem | select | Evet | Kritik / Yüksek / Orta / Bilgi |
| Kaynak Kayıt | text | Evet | CTL-2026-0421 |
| Sorumlu | select | Evet | Finans Operasyon / Hazine / Finans Müdürü / CFO/Yönetim |
| Son Tarih | date | Evet | 2026-08-24 |
| Finansal Etki | amount | Hayır | 12.850.000,00 |
| Durum | select | Evet | Açık / İnceleniyor / Onay Bekliyor / Çözüldü |
| Dönem | month | Evet | 2026-08 |
| Açılacak Kapsam | select | Evet | Tek Kayıt / Banka/Kasa / Tüm Finans Dönemi |
| Bitiş / Otomatik Yeniden Kilit | date | Evet | 2026-08-26 |
| Gerekçe | textarea | Evet | Bankadan geç gelen valörlü hareketin kaydı. |

**Etki önizlemesi:**

- **Finansal Etki:** 12.850.000 TL
- **Kapanış Etkisi:** Kontrol edilir
- **Sorumlu:** Atanır
- **Audit:** Çözüm geçmişi korunur

**Kontrol listesi:**

- [ ] Kaynak kayıt mevcut
- [ ] Sorumlu ve son tarih tanımlı
- [ ] Kapanışı engelleme durumu belirlendi
- [ ] Çözüm geçmişi korunacak

**Nihai aksiyon:** Yeniden Açma Talebini Gönder

---

## Raporlama

### Finans Raporu Oluştur

**Kod:** `reportBuilder`  
**Sınıf:** Standart  
**Amaç:** Rapor kapsamı, kırılımlar, filtreler, çıktı ve kaynak drill-down tasarımını oluşturun.

**Adımlar:** 1 Rapor → 2 Kapsam → 3 Kırılım → 4 Önizleme → 5 Yayınla

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Kontrol Türü | select | Evet | Onay / Mutabakat / Vade/Yükümlülük / Limit/Teminat / Dönem Kapama |
| Önem | select | Evet | Kritik / Yüksek / Orta / Bilgi |
| Kaynak Kayıt | text | Evet | CTL-2026-0421 |
| Sorumlu | select | Evet | Finans Operasyon / Hazine / Finans Müdürü / CFO/Yönetim |
| Son Tarih | date | Evet | 2026-08-24 |
| Finansal Etki | amount | Hayır | 12.850.000,00 |
| Durum | select | Evet | Açık / İnceleniyor / Onay Bekliyor / Çözüldü |
| Rapor Şablonu | select | Evet | Yönetim Nakit ve Likidite / Banka Bakiye ve Mutabakat / Açık Kalem ve Yaşlandırma / Kredi / Limit / Teminat / Nakit Bütçesi ve Kontrol |
| Şirket / Görünüm | select | Evet | Göymen Demir Çelik / Grup Konsolide |
| Tarih Aralığı | select | Evet | Bugün / Bu Hafta / Bu Ay / Özel Tarih |
| Raporlama Para Birimi | select | Evet | TL / USD / EUR / Orijinal Döviz |
| Ana Kırılım | select | Evet | Banka / Cari / Vade / Belge Türü / Para Birimi |
| Çıktı | select | Evet | Ekran / PDF / Excel / Planlı Dağıtım |

**Etki önizlemesi:**

- **Kaynak İzlenebilirliği:** Her toplamdan kayda drill-down
- **Resmî Muhasebe:** Kapsam dışı
- **Rapor Snapshot:** İsteğe bağlı korunur
- **Yetki:** Şirket ve veri kapsamına göre

**Kontrol listesi:**

- [ ] Kaynak kayıt mevcut
- [ ] Sorumlu ve son tarih tanımlı
- [ ] Kapanışı engelleme durumu belirlendi
- [ ] Çözüm geçmişi korunacak

**Nihai aksiyon:** Raporu Önizle

---

### Kayıtlı Rapor Görünümü

**Kod:** `savedReportView`  
**Sınıf:** Standart  
**Amaç:** Filtre, kolon, sıralama ve şirket bağlamını kişisel veya ekip görünümü olarak kaydedin.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Kontrol Türü | select | Evet | Onay / Mutabakat / Vade/Yükümlülük / Limit/Teminat / Dönem Kapama |
| Önem | select | Evet | Kritik / Yüksek / Orta / Bilgi |
| Kaynak Kayıt | text | Evet | CTL-2026-0421 |
| Sorumlu | select | Evet | Finans Operasyon / Hazine / Finans Müdürü / CFO/Yönetim |
| Son Tarih | date | Evet | 2026-08-24 |
| Finansal Etki | amount | Hayır | 12.850.000,00 |
| Durum | select | Evet | Açık / İnceleniyor / Onay Bekliyor / Çözüldü |
| Görünüm Adı | text | Evet | CFO Günlük Özet |
| Kapsam | select | Evet | Kişisel / Finans Ekibi / Yönetim |
| Varsayılan Rapor | select | Evet | Yönetim Nakit ve Likidite / 7 Günlük Ödeme Riski / Kontrol İstisnaları |
| Varsayılan Şirket | select | Evet | Göymen Demir Çelik / Grup Konsolide |

**Etki önizlemesi:**

- **Finansal Etki:** 12.850.000 TL
- **Kapanış Etkisi:** Kontrol edilir
- **Sorumlu:** Atanır
- **Audit:** Çözüm geçmişi korunur

**Kontrol listesi:**

- [ ] Kaynak kayıt mevcut
- [ ] Sorumlu ve son tarih tanımlı
- [ ] Kapanışı engelleme durumu belirlendi
- [ ] Çözüm geçmişi korunacak

**Nihai aksiyon:** Görünümü Kaydet

---

### Planlı Rapor Dağıtımı

**Kod:** `reportSchedule`  
**Sınıf:** Gelişmiş  
**Amaç:** Raporu belirli zamanlarda yetkili alıcılara kontrollü biçimde hazırlayın veya gönderim kuyruğuna alın.

**Adımlar:** 1 Temel Bilgi → 2 İlişki → 3 Etki → 4 Kontrol → 5 Onay

**Alanlar:**

| Alan | Tip | Zorunlu | Varsayılan / Seçenek örneği |
|---|---|---:|---|
| Kontrol Türü | select | Evet | Onay / Mutabakat / Vade/Yükümlülük / Limit/Teminat / Dönem Kapama |
| Önem | select | Evet | Kritik / Yüksek / Orta / Bilgi |
| Kaynak Kayıt | text | Evet | CTL-2026-0421 |
| Sorumlu | select | Evet | Finans Operasyon / Hazine / Finans Müdürü / CFO/Yönetim |
| Son Tarih | date | Evet | 2026-08-24 |
| Finansal Etki | amount | Hayır | 12.850.000,00 |
| Durum | select | Evet | Açık / İnceleniyor / Onay Bekliyor / Çözüldü |
| Rapor | select | Evet | Yönetim Özeti / Banka / Limit Özeti / Bütçe Sapma / Kontrol İstisnaları |
| Sıklık | select | Evet | Her Gün / Haftalık / Aylık / Özel Takvim |
| Çalışma Saati | text | Evet | 08:30 |
| Alıcı Grubu | select | Evet | CFO / Yönetim / Finans Ekibi / Bütçe Sahipleri |
| Format | select | Evet | PDF / Excel / Ekran Bildirimi |

**Etki önizlemesi:**

- **Finansal Etki:** 12.850.000 TL
- **Kapanış Etkisi:** Kontrol edilir
- **Sorumlu:** Atanır
- **Audit:** Çözüm geçmişi korunur

**Kontrol listesi:**

- [ ] Alıcıların veri yetkisi doğrulanır
- [ ] Rapor snapshot tarihi kaydedilir
- [ ] Dağıtım logu ve hata kuyruğu oluşturulur

**Nihai aksiyon:** Dağıtımı Planla

---
