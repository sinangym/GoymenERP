# Göymen ERP Finans — UI/UX ve Tasarım Sistemi Kararları v3

## 1. Tasarım hedefi

Finans modülü yüksek veri yoğunluğuna rağmen ilk bakışta şu üç soruya cevap vermelidir:

1. Bugün ve yakın dönemde nakit durumumuz nedir?
2. Hangi tahsilat, ödeme, risk veya kontrol dikkat gerektiriyor?
3. İşleme başlamak için hangi ana aksiyonu kullanmalıyım?

Bu nedenle tasarımın ana ilkesi **özet → detay → işlem workspace’i** hiyerarşisidir.

## 2. Göymen ERP ile görsel uyum

### Uygulama kabuğu

- Sabit koyu lacivert sol menü
- Üstte beyaz global araç çubuğu
- Global arama
- Şirket, tarih ve para birimi bağlamı
- Modül ve durum rozetleri
- Açık gri/yeşilimsi içerik zemini
- Beyaz kartlar ve ince sınırlar
- Sağdan açılan bağlamsal AI paneli
- Yoğun işlemlerde tam ekran workspace

### Renk davranışı

| Renk | Kullanım |
|---|---|
| Mavi | Aktif modül, ana işlem, seçili sekme |
| Teal | Finansal kaynak, ilişki ve nötr-pozitif vurgu |
| Yeşil | Başarı, kullanılabilirlik, gerçekleşen olumlu değer |
| Amber | Yaklaşan görev, kontrol, orta risk, valör bekliyor |
| Kırmızı | Vadesi geçmiş, kritik risk, engelleyici hata |
| Mor | Finansal araç ve ikincil sınıflandırma |
| Gri | Pasif, arşiv, bilgi veya tamamlanmamış bağlam |

Renk tek başına anlam taşımaz; metin, ikon ve durum etiketi birlikte kullanılır.

## 3. Tipografi ve yoğunluk

- Sayfa başlığı: 24–28 px, güçlü ama tek satır
- Kart başlığı: 14–16 px
- KPI değeri: 22–28 px
- Tablo: 12–14 px
- Yardım metni: 11–12 px
- Tıklanabilir hedefler: masaüstünde en az 32–36 px, mobilde 40–44 px
- Bir görünümde en fazla 6 ana KPI
- Bir sayfada yalnız bir baskın mavi ana aksiyon
- Aynı toplam farklı kartlarda tekrar edilmez

## 4. Global sayfa şablonu

### Liste/görev merkezi

1. Breadcrumb
2. Başlık ve kısa amaç
3. Görünüm / Paylaş / Çıktı
4. Ana işlem
5. Durum sekmeleri
6. Arama ve filtreler
7. Gerekirse kompakt KPI/özet
8. Ana liste/grid
9. Sağ detay çekmecesi

### Detay/işlem workspace’i

1. Kayıt başlığı ve durum
2. Adım göstergesi
3. Kayıt bağlamı
4. Temel bilgiler
5. İlişkiler ve belgeler
6. Sağda etki önizlemesi
7. Sağda kontrol listesi
8. Sabit alt işlem çubuğu
9. Kaydedilmemiş değişiklik koruması

### Modal–workspace ayrımı

- Küçük not, hızlı seçim, tek alanlı düzeltme: modal
- Mali etkili, çok adımlı, belge/onay gerektiren işlem: tam ekran workspace

## 5. Kullanıcı dostu bilgi yoğunluğu

- Ana dashboard 6 ana KPI + 3 ikincil hızlı gösterge kullanır.
- “Net nakit hareketi” ile “dönem sonu tahmini nakit” ayrıdır.
- Planlanan ödeme ve borç servisi pozitif tutarla, çıkış etiketiyle gösterilir.
- Nakit grafiğinde giriş, çıkış, kümülatif tahmini nakit ve minimum tampon birlikte gösterilir.
- Yaklaşan tahsilat ve ödemelerde kaynak türü görünür.
- Banka bakiyesi yanında güncel, eşleşmemiş, bloke ve valör durumu gösterilir.
- Teminatlarda “vade” yerine yaklaşan işlem türü gösterilir: ekspertiz, sigorta, limit yenileme, çözüm vb.
- Uzun listeler ayrı sayfalara bölünmez; durum sekmesi, filtre ve detay çekmecesi kullanılır.

## 6. Menü ve işlem erişimi

### Sol menü

Günlük Finans ve Hazine grupları ayrıdır. Kullanıcının günlük işi üstte, ağır modüller altta yer alır.

### Yeni Finans İşlemi kataloğu

- Toplam 73 işlem
- Varsayılan 22 sık kullanılan işlem
- Günlük Finans / Hazine / Bütçe / Kontrol / Tümü filtreleri
- İşlem, banka, çek, bütçe, teminat veya kontrol araması
- Her kartta kısa açıklama, standart/gelişmiş ve sık kullanılan etiketi

Bu yapı, sol menüyü onlarca işlemle doldurmadan bütün işlevlere erişim sağlar.

## 7. Tablo standardı

- İlk sütun güçlü kayıt/cari kimliği
- Para sütunları sağa hizalı
- Tarih formatı tutarlı
- Durumlar kısa rozetlerle gösterilir
- Satırın tamamı veya detay ikonu sağ çekmece açar
- Kritik değer yalnız renkle değil metin/ikonla belirtilir
- Kolonlar rol ve kayıtlı görünüme göre özelleştirilebilir
- Excel/PDF/Paylaş ortak menüden açılır

## 8. Form standardı

- Zorunlu alan `*` ile gösterilir
- İlk görünümde yalnız gerekli alanlar
- Gelişmiş alanlar kapalı bölümde
- Tutar + para birimi birlikte
- İşlem, vade, valör ve kayıt tarihleri ayrı
- Belge yükleme alanı ortak
- Mükerrerlik ve yetki kontrolü formu kaydetmeden önce görünür
- Hata alanın altında açıklanır; genel hata üstte özetlenir
- Etki önizlemesi mali sonuçları kaydetmeden önce gösterilir

## 9. AI panel standardı

AI paneli varsayılan olarak kapalıdır; ana içeriği daraltmaz. Sayfaya göre hızlı aksiyonlar sunar:

- Finans Genel Bakış: nakit açığını açıkla, kritik ödemeleri sırala
- Tahsilat/Ödeme: dekontu oku, mükerrerliği kontrol et
- Dağıtım: eşleşme öner, vade/valör etkisini açıkla
- Tahsilat/Risk: öncelikli carileri sırala, söz bozma riskini açıkla
- Banka/Kasa: mutabakat öner, masrafı sınıflandır
- Ödeme Yönetimi: ödeme önceliği ve banka hesabı öner
- Araçlar: risk ve nakitleşme durumunu açıkla
- Nakit Planı: stres senaryosu oluştur
- Kredi/Limit: teklifleri karşılaştır, vade riskini açıkla
- Teminat: boşluk ve yaklaşan yükümlülüğü göster
- Bütçe: sapmayı açıkla
- Kontroller: kök neden ve çözüm öner
- Raporlar: yönetim özeti hazırla

AI’nın önerisi nihai kayıt değildir; kullanıcı önizleme ve onay verir.

## 10. Rol bazlı sadeleştirme

- Finans Operasyon: günlük işlemler ve dağıtım
- Tahsilat Sorumlusu: cari risk ve görevler
- Hazine: nakit, kredi, limit, teminat
- Bütçe Sorumlusu: bütçe ve sapma
- Finans Kontrolörü: onay, mutabakat, kapanış
- Yönetim: konsolide özet, risk ve kararlar
- Denetçi: salt okunur/audit

Kullanıcı yetkisi olmayan sekme veya ana işlem görünmez; yalnız butonu pasif göstermek tercih edilmez.

## 11. Responsive davranış

- 1440–1600 px: tam sol menü, çok kolonlu dashboard
- 1024–1439 px: daraltılabilir menü, 3×2 KPI, paneller yeniden akar
- 768–1023 px: tablet iki kolon, AI drawer overlay
- <768 px: mobil tek/iki kolon, sol menü overlay, workspace tam ekran
- Tablolar kontrollü yatay kaydırılır
- Sabit alt işlem çubuğu mobilde düğmeleri iki satıra bölebilir
- Mobilde yatay sayfa taşması olmamalıdır

## 12. Boş, yükleniyor ve hata durumları

Her ana ekran şu durumları tasarlamalıdır:

- Yükleniyor: iskelet/skeleton
- Boş: neden + ana işlem önerisi
- Filtre sonucu boş: filtreyi temizle
- Yetkisiz: erişim nedeni ve doğru rol
- Güncel olmayan veri: son güncelleme ve etkilenen hesap
- Kısmi veri: rakamın güvenilirlik etiketi
- Hata: işlem kaybolmadan tekrar dene ve destek/audit referansı

## 13. Tasarım kilitleri

- Finansal kaynak ve dağıtım birleştirilmeyecek.
- Ticari kapama, nakitleşme ve risk tek durum etiketine indirgenmeyecek.
- Kritik işlem hızlı inline düzenleme ile sessizce değiştirilmeyecek.
- Dashboard’a ağır facility/covenant alanları taşınmayacak.
- Ana menü 13 ekranın ötesinde işlem bazlı sayfalarla şişirilmeyecek.
- Tedarikçi POS eklenmeyecek.
- Mikro mutabakatı ve resmî muhasebe/vergi ekranları bu çalışma alanına eklenmeyecek.
