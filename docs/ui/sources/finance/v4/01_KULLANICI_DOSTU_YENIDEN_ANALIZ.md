# Göymen ERP Finans — Kullanıcı Dostu ve AI Odaklı Yeniden Analiz v4

## 1. Nihai değerlendirme

v3 çalışma alanı finansal kapsam, işlem ayrımları, kontrol yaklaşımı ve Göymen ERP görsel uyumu bakımından güçlüydü. Ancak günlük kullanıcı açısından üç temel sorun devam ediyordu:

1. **Ana ekran yönetim ağırlıklıydı.** Finans personeli sisteme girdiğinde hangi işi önce yapacağını görmek için birçok KPI ve paneli yorumlamak zorunda kalıyordu.
2. **Finans menüsündeki 13 ekran aynı anda görünüyordu.** Ekranların tamamı gerekli olsa da menüde sürekli görünmeleri bilişsel yük oluşturuyordu.
3. **AI her sayfada erişilebilir olsa da yeterince görünür, açıklanabilir ve işleme bağlı değildi.** Sağ çekmece genel öneri veriyor; önerinin kaynağı, güven oranı ve işlem etkisi her zaman aynı standartta görünmüyordu.

v4 bu sorunları aşağıdaki ana kararla çözer:

> Finans modülü varsayılan olarak **Benim İşlerim + Sade Görünüm** ile açılır. Yönetim özeti ve gelişmiş hazine alanları kaybolmaz; rol ve görünüm seçimiyle açılır. AI her sayfada görünür bir dikkat noktası, bağlamsal aksiyon, kaynak, güven oranı ve etki önizlemesi sunar; hiçbir mali işlemi kullanıcı adına kesinleştirmez.

---

## 2. v3’te korunması gereken güçlü alanlar

- Finansal kaynak ile açık kalem dağıtımının ayrılması
- Ticari kapama, nakitleşme ve riskin ayrı durumlar olması
- Vade, işlem tarihi, valör ve dağıtım tarihinin ayrı tutulması
- Kesinleşen kaydın silinmemesi; ters kayıt ve revizyon kullanılması
- Tahsilat/ödeme, banka/kasa, araçlar, nakit planı, kredi-limit, teminat, bütçe ve kontrol ekranlarının ayrı görev merkezleri olması
- Koyu lacivert Göymen ERP uygulama kabuğu
- Tam ekran işlem workspace’i
- 73 işlemin menüyü şişirmeden katalogdan açılabilmesi
- Mikro mutabakatı ile resmî muhasebe/vergi kapsamının bilinçli olarak dışarıda bırakılması

Bu alanlar v4’te aynen korunmuştur.

---

## 3. Tespit edilen kullanıcı deneyimi sorunları

### 3.1 Ana ekran karar ekranıydı, iş ekranı değildi

Yönetim için doğru olan nakit, borç, limit ve bütçe özetleri günlük finans personelinin ilk ihtiyacı değildir. Kullanıcı çoğu zaman önce şunları görmek ister:

- Dağıtılmamış finansal kaynaklar
- Mutabakatsız banka satırları
- Onay bekleyen ödeme paketleri
- Bozulan ödeme sözleri
- Bugünkü görev ve son tarihler

**v4 çözümü:** Genel Bakış iki görünüme ayrıldı:

- **Benim İşlerim:** Varsayılan görev odaklı görünüm
- **Yönetim Özeti:** Nakit, borç, limit, bütçe ve risk görünümü

### 3.2 Menü görünürde gereğinden uzundu

13 ana ekran gerekli olmakla birlikte her birinin sürekli aynı seviyede görünmesi karmaşıklık yaratıyordu.

**v4 çözümü:** Ekranlar beş açılır görev grubuna toplandı:

1. Günlük İşlemler
2. Tahsilat & Ödeme
3. Banka & Araçlar
4. Hazine
5. Kontrol & Rapor

Genel Bakış doğrudan görünür kalır. Aktif ekranın grubu otomatik açılır.

### 3.3 AI yalnız çekmece seviyesinde kalıyordu

Kullanıcı AI butonuna basmadığında kritik AI uyarısını göremiyordu. AI önerisinin hangi veriye dayandığı ve ne kadar güvenilir olduğu da standartlaşmamıştı.

**v4 çözümü:** Her ana sayfada başlığın altında bir **AI Dikkat Noktası** bulunur:

- Tek cümlelik önemli bulgu
- “Neden?” açıklaması
- Kullanılan veri kaynakları
- İki bağlamsal AI aksiyonu
- Tam AI çekmecesine geçiş

### 3.4 AI önerileri eyleme fazla yakın, açıklamaya uzaktı

“Müşterileri sırala” veya “dağıtım öner” gibi komutlar vardı; fakat önerinin gerekçesi, güveni ve etkisi her zaman görünmüyordu.

**v4 çözümü:** Her AI sonucu şu yapıda gösterilir:

1. Özet sonuç
2. Neden bu öneri?
3. Kullanılan kaynaklar
4. Güven oranı
5. Etkilenecek finans alanları
6. Kaynak ekrana git
7. İşlem önizlemesini aç
8. Kullanıcı onayı olmadan kesinleştirmeme uyarısı

### 3.5 Açık kalem dağıtım sayfası ilk kullanım için yoğundu

Üç kolonlu kaynak–açık kalem–sepet yapısı güçlü bir operasyon ekranıdır; fakat günlük kuyruk görünümü olarak fazla yoğundur.

**v4 çözümü:**

- Sade görünümde: kaynak kuyruğu + seçili kaynak özeti + AI önerisi
- Ayrıntılı görünümde: eski üç panelli çalışma düzeni
- Kesin dağıtım: tam ekran workspace

### 3.6 Ağır hazine alanları günlük kullanıcıya görünüyordu

Kredi, limit, teminat ve bütçenin ileri alanları veri modelinde gerekli olsa da günlük kullanıcının önüne sürekli çıkmamalıdır.

**v4 çözümü:** Global **Sade / Ayrıntılı** görünüm anahtarı eklendi. Sade mod varsayılandır. Gelişmiş alanlar ve ek KPI’lar ancak Ayrıntılı görünümde açılır.

### 3.7 İşlem workspace’inde AI standardı tutarlı değildi

Bazı işlemlerde AI kartı, bazılarında yalnız bilgilendirme notu bulunuyordu.

**v4 çözümü:** Bütün 73 işlem workspace’inde aynı AI İşlem Kontrolü bulunur:

- Zorunlu alan kontrolü
- Cari/banka/belge ilişki kontrolü
- Mükerrerlik kontrolü
- Vade/valör veya risk etkisi
- Güven oranı
- “AI ile Kontrol Et”
- “Açıkla”

---

## 4. v4 kullanıcı deneyimi kararları

### 4.1 Varsayılan açılış

```text
Rol görünümü: Benim İşlerim
Ekran yoğunluğu: Sade
Ana ekran: Finans Genel Bakış / Benim İşlerim
```

Yönetici rolü kaydedilmiş tercihle Yönetim Özeti’ni varsayılan yapabilir.

### 4.2 Beş saniye kuralı

Kullanıcı ilk beş saniyede şu üç sorunun cevabını görebilmelidir:

1. Bugün ne yapmalıyım?
2. Hangi tutar veya risk en kritik?
3. İşleme hangi düğmeden başlamalıyım?

### 4.3 Bir sayfada bir baskın ana işlem

Her sayfada yalnız bir ana mavi düğme vardır. Diğer işlemler:

- Satır işlemleri
- Sekmeler
- İşlem kataloğu
- Sağ detay paneli
- Tam ekran workspace

üzerinden açılır.

### 4.4 Özet → detay → işlem

```text
Özet/Kuyruk
→ Sağ detay çekmecesi
→ Tam ekran işlem workspace’i
→ Etki önizlemesi
→ Kontrol / onay
```

### 4.5 Sayfa kılavuzu

Her sayfada `? Kılavuz` düğmesi bulunur. Kullanıcıya:

- Önce ne yapacağı
- Sonra ne yapacağı
- Hangi finansal ayrımı kontrol edeceği
- Klavye kısayolları

anlatılır.

### 4.6 AI güvenlik ilkesi

AI şu işlemleri tek başına yapmaz:

- Tahsilat/ödeme kesinleştirme
- Fatura kapama
- Banka talimatı gönderme
- Kredi kullanma
- Teminat çözme
- Bütçe transferi onaylama
- Dönem kapatma/açma
- Cari risk istisnası verme

AI yalnız analiz, taslak, eşleşme, sınıflandırma, kontrol ve etki önizlemesi üretir.

---

## 5. Sonuç

v4 ile finans çalışma alanı:

- Günlük kullanıcının önceliğine göre açılır.
- Yönetim ve hazine detayını kaybetmez.
- 13 ekranı menüde beş anlaşılır gruba indirger.
- AI’ı her sayfada görünür ve bağlamsal yapar.
- AI sonucunu kaynak, güven ve etkiyle açıklar.
- Bütün işlem workspace’lerinde AI kontrol standardı uygular.
- Kredi, limit, teminat ve bütçeyi sade fakat geliştirilebilir tutar.
- Göymen ERP’nin satış, alış, cari ve üretim ekranlarıyla aynı tasarım dilini korur.
