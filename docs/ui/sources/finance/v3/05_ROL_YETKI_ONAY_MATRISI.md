# Göymen ERP Finans — Rol, Yetki ve Onay Matrisi

## 1. Temel prensip

Finans işlemlerinde **hazırlayan, kontrol eden, onaylayan, bankada uygulayan ve mutabakat yapan** roller aynı kritik işlem üzerinde tek kişide birleşmemelidir. Küçük tutarlı veya düşük riskli işlemler parametrik olarak daha kısa akış kullanabilir; audit kaydı her durumda korunur.

## 2. Roller

| Rol | Ana sorumluluk | Varsayılan erişim |
|---|---|---|
| Finans Operasyon | Tahsilat/ödeme, dağıtım, banka satırı sınıflandırma, ödeme önerisi | Günlük finans ekranları |
| Tahsilat Sorumlusu | Vadesi geçmiş alacak, görev, ödeme sözü, ihtilaf | Tahsilat/Cari Risk |
| Kasa Sorumlusu | Kasa hareketi, sayım ve gün sonu | Kasa |
| Mutabakat Personeli | Banka ekstresi ve eşleştirme | Banka/Kasa |
| Hazine Yöneticisi | Nakit planı, banka, kredi, limit, teminat, fonlama | Hazine ekranları |
| Finans Kontrolörü | Belge, dağıtım, mükerrerlik, bütçe ve dönem kontrolü | Kontrol ve onay |
| Finans Müdürü / CFO | Büyük tutar, kredi, limit, teminat, bütçe, risk istisnası | Tüm finans |
| Bütçe Sorumlusu | Nakit bütçesi, sürüm, transfer | Nakit Bütçesi |
| Satış Sorumlusu | Tahsilat görüşmesi ve cari risk bilgisi | Kısıtlı cari/tahsilat |
| Hukuk | İhtilaf, ihtar, icra ve hukuki takip | Hukuki Takip |
| Genel Müdür / Yönetim | Yüksek riskli ve stratejik onaylar | Konsolide yönetim görünümü |
| Denetçi | Salt okunur kayıt, audit ve rapor | Okuma/çıktı |

## 3. İşlem bazlı yetki özeti

| İşlem | Hazırlayan | Kontrol eden | Onaylayan | İkinci onay koşulu |
|---|---|---|---|---|
| Tahsilat/ödeme kaydı | Finans Operasyon | Finans Kontrolörü | Yetki matrisine göre | Büyük tutar, geriye tarih, farklı şirket |
| Dağıtım | Finans Operasyon | Finans Kontrolörü | Parametrik | Kapalı kalemi yeniden açma, farklı cari/kur |
| Tahsilat görevi | Tahsilat Sorumlusu | — | Gerekmez | Hukuki devir/risk istisnası |
| Ödeme sözü | Tahsilat/Satış | Finans Operasyon | Parametrik | Yapılandırma veya sevkiyat blokajı etkisi |
| İhtilaf | Tahsilat/Satış | Finans + ilgili birim | Finans Müdürü | Büyük tutar/hukuki devir |
| Ödeme önerisi | Finans Operasyon | Hazine/Finans Kontrol | Finans Müdürü | Yönetim eşiği üstü |
| Ödeme paketi/talimat | Finans Operasyon | Hazine | Finans Müdürü | Yeni IBAN, grup şirketi, büyük tutar |
| IBAN değişikliği | Cari/Finans | Bağımsız doğrulayan | Finans Müdürü | Her zaman çift kontrol |
| Banka ekstresi içe aktar | Mutabakat Personeli | Sistem kontrolü | Gerekmez | Manuel dosya düzeltmesi yapılamaz |
| Banka mutabakatı | Mutabakat Personeli | Finans Kontrolörü | Parametrik | Tutar farkını manuel kapatma |
| Kasa sayımı farkı | Kasa Sorumlusu | Finans Kontrolörü | Finans Müdürü | Eşik üzeri fark |
| Çek/senet kaydı | Finans Operasyon | Finans Kontrolörü | Parametrik | Teminat/ciro/karşılıksız sonucu |
| DBS | Finans Operasyon | Hazine | Finans Müdürü | Garanti dışı veya limit aşımı |
| POS/kart | Finans Operasyon | Mutabakat/Finans Kontrol | Parametrik | Chargeback, yüksek komisyon farkı |
| Kredi kaydı/kullandırım | Hazine | Finans Kontrolörü | CFO/Yönetim | Her zaman stratejik onay |
| Limit oluşturma/yenileme | Hazine | Finans Kontrolörü | CFO/Yönetim | Her zaman |
| Teminat kaydı | Hazine | Hukuk/Finans Kontrol | Finans Müdürü | Teminat verme/çözme |
| Teminat çözümü | Hazine | Hukuk + Finans Kontrol | CFO/Yönetim | Aktif risk varsa zorunlu |
| Bütçe girişi | Bütçe Sorumlusu | Finans Kontrolörü | Finans Müdürü/Yönetim | Onaylı sürüm |
| Bütçe transferi | Bütçe Sorumlusu | Finans Kontrolörü | Yetki matrisine göre | Sert kontrollü kategori/CAPEX |
| Risk istisnası | Finans/Satış | Finans Kontrolörü | Finans Müdürü/Yönetim | Limit/sevkiyat aşımı |
| Gün/ay sonu kapama | Finans Kontrolörü | Finans Müdürü | Parametrik | İstisna ile kapama |
| Dönem yeniden açma | Finans Müdürü | Denetim/Finans Kontrol | CFO/Yönetim | Her zaman |

## 4. Kritik ikinci onay tetikleyicileri

- Yeni banka hesabı veya hesap kapama
- Cari/tedarikçi IBAN değişikliği
- İlk kez kullanılacak alıcı IBAN’ı
- Şirket politikası üzerindeki ödeme tutarı
- Grup şirketi adına ödeme veya tahsilat
- Geriye tarihli finans hareketi
- Kapalı dönem kaydı veya dönem yeniden açma
- Kesinleşmiş dağıtım revizyonu
- Karşılıksız çek sonrası açık kalem yeniden açma
- Banka/mutabakat farkını manuel kapatma
- Yeni kredi veya limit kullanımı
- Teminat verme, başka krediye tahsis veya çözme
- Bütçe aşımı ve sert kontrol istisnası
- Cari risk/sevkiyat blokaj istisnası
- Covenant manuel değer değişikliği

## 5. Onay kararları

Her onay ekranında:

- Kaynak kayıt ve özet
- Finansal/nakit/risk/bütçe etkisi
- Belge durumu
- Hazırlayan ve kontrol eden
- Yetki eşiği
- Onay geçmişi
- Onayla / Düzeltmeye Gönder / Reddet
- Zorunlu karar notu

bulunmalıdır.

## 6. AI sınırı

AI:

- Uygun onaylayan rolü önerebilir.
- Eksik belge ve riskleri açıklayabilir.
- Etki özetini hazırlayabilir.
- Benzer işlemleri ve istisnaları gösterebilir.

AI:

- Ödeme onaylayamaz.
- Bankaya talimat gönderemez.
- Kredi kullanamaz.
- Teminat çözemez.
- Limiti veya bütçeyi kesinleştiremez.
- Dönemi açıp kapatamaz.
