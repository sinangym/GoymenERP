# Sonraki modüller için başlangıç ekleri

Durum: PREPARATION ONLY. Bunlar yeni feature spec, kabul edilmiş backlog sırası veya çalıştırma yetkisi değildir. Koordinatör mevcut hedefi kapatıp sonraki işi yetkili kayıttan seçer; Business güncel kaynakları doğrulayarak ilk akışı kesinleştirir. Aşağıdaki adlar mevcut AT/DEC kimliği yerine geçmez.

## Sipariş / Satış

Kaynak: [Master §7.2, §8.1, §11](../../master/GOYMEN_ERP_MASTER_RAPOR_V3.md), [karar sicili DEC-SAL-001/002/003](../../decisions/KARAR_SICILI.md); güncel tarihli kararlar öncelikli.

Olası ilk küçük akış: yetkili şirkette mevcut Cari ve Ürün referansıyla sipariş taslağı -> liste -> detay; kapsam Business tarafından doğrulanır. Sipariş draft sözleşmesi/alanları eksikse önce kısa spec hazırlanır, endpoint veya izin adı uydurulmaz.

Yeniden kullanım: Ürün liste/form/kart örneği, Cari kimlik ve şirket hesabı ayrımı, mevcut command/audit/version altyapısı. Yeni fark: sipariş şirket kapsamında; Product global kayıt olsa bile sipariş yazma yetkisi ayrı. Cari otomatik şirket hesabı açma B önerisi onaylanmış sayılmaz; ilk adopter için karar gerekiyorsa ayrıca çözülür.

Erken örnekler: referans bulunamıyor/yetkisiz -> bilgi sızmıyor; miktar ve fiyat birimleri ayrışıyor -> backend sözleşmesi belirli; çift submit -> tek taslak; eski version -> tanımlı conflict; taslak okuma -> stok/finans hareketi yok. Fiyat/yuvarlama/vergi/iskonto kesinliği kaynak olmadan frontend'de üretilmez.

İlk pakete tüm satış, satın alma, tahsis, sevkiyat, fatura ve finansı aynı anda ekleme. Sonraki revizyon/onay dilimi ayrı kabul edilir; bunların eksikliği tam Sipariş modülü tamamlandı denmesini engeller.

Ek prompt:

```text
Bu görev yalnız koordinatörün seçtiği Sipariş akışıdır. Business DEC-SAL ve güncel karar/spec bağını netleştirsin; Backend ve Claude aynı contract ile küçük taslak/list/detail yolunda buluşsun. Ürün/Cari master'larını yeniden oluşturma. Yeni stok veya finans etkisi, otomatik Cari hesabı ve onay politikası bu başlangıç ekinden yetki almaz. Miktar/fiyat birimi ve revizyon sınırını erken doğrula.
```

## Stok / Tahsis

Kaynak: [Master §7.6 ve §9](../../master/GOYMEN_ERP_MASTER_RAPOR_V3.md), [DEC-STK-001..009](../../decisions/KARAR_SICILI.md), [tarihli karar §4 maliyet ertelemesi](../../decisions/SINAN_KARARLARI_2026-08-31.md).

Olası ilk küçük akış: mevcut yetkili veriden malzeme pozisyonu liste/detay; gerçek kaynak yoksa ilk kaydı üretecek tek onaylı hareket ayrıca tanımlanır. Sadece mock bakiye gösterip Stok çalışıyor denmez.

Yeniden kullanım: Ürün referansı, Organizasyon lokasyonları, ortak list/command/SQL altyapısı. Yeni fark: mülkiyet, sorumluluk, fiziksel konum ve ticari bağ ayrıdır; tahsis fiziksel hareket değildir. FIFO/LIFO veya maliyet yöntemi bu pakette seçilmez.

Erken örnekler: başka şirket/izinsiz konum; fiziki stok ile gelecek arz ayrımı; aynı command replay'de tek hareket; tahsis diliminde iki eşzamanlı isteğin serbest miktarı aşmaması; iptal/ters etkinin auditli olması. Hareket mi tahsis mi yazıldığı ilk contract'ta açık olmalı.

Ek prompt:

```text
Bu görev yalnız atanmış Stok akışıdır. Kaynağın fiziki/gelecek arz/tahsis olduğunu Business kilitli kurallarla ayırsın. Aynı olaydan ikinci hareket üretme; modüller arası doğrudan tablo yazma. Ürün/Organizasyon ve mevcut SQL araçlarını kullan. Stok eksiye düşme, maliyet, mülkiyet veya tolerans politikasını tahmin etme; sadece etkilediği yolu açık karar olarak ayır.
```

## Finans / Hazine

Kaynak: [Master §7.10–7.11 ve §10](../../master/GOYMEN_ERP_MASTER_RAPOR_V3.md), [karar sicili Finans kayıtları](../../decisions/KARAR_SICILI.md), [tarihli karar §4 ve §9/Q3](../../decisions/SINAN_KARARLARI_2026-08-31.md).

Olası ilk küçük akış: Business'ın yetkilendirdiği manuel finansal kaynak kaydı/listesi/detayı. Bu tek başına risksiz CRUD değildir; tutar/para birimi, kaynak olay, yetki, audit ve correction/ters kayıt sözleşmesi belirlenmeden yazma açılmaz. Kaynak henüz tanımlı değilse yalnız sözleşme hazırlığı yapılır.

Yeniden kullanım: Cari referansı ve güvenli command altyapısı. Yeni fark: kaynak para olayı ile onun açık kalemlere dağıtımı farklıdır; dağıtım ikinci banka/kasa/cari hareketi yaratmaz. Finans, Muhasebe ile aynı modül sayılmaz. İlk sürüm için banka/muhasebe/e-belge entegrasyonu otomatik eklenmez.

Erken örnekler: tek kaynak + iki dağıtım -> bir para olayı; şirket/döviz ayrımı; toplamın korunması; yetkisiz veya kilitli durum; aynı-key replay; stale-version; ters kayıt izi. Kesin formül/kur/tolerans/golden değerleri Business güncel kaynaktan bağlar; prompt içinde tutar/eşik uydurulmaz.

Ek prompt:

```text
Bu görev yalnız atanmış Finans akışıdır. Finansal kaynak, dağıtım ve muhasebe etkisinin sahibini ayrı tanımla. Aynı para olayını iki kez kaydetme; UI hesaplarını backend calculator'dan tüket. Yeni banka entegrasyonu, onay eşiği, kur veya muhasebe politikası seçme. Mevcut Cari ilişkisini kullan; ilk normal akışla birlikte ilgili golden ve gerçek SQL doğrulamasını hazırla.
```

## Üretim

Kaynak: [Master §7.7, §10, §25.3](../../master/GOYMEN_ERP_MASTER_RAPOR_V3.md), [DEC-PRDCT-001/004/005/006/008](../../decisions/KARAR_SICILI.md), [tarihli karar §4](../../decisions/SINAN_KARARLARI_2026-08-31.md).

Olası ilk küçük akış: kilitli motor sırasındaki yetkili tek motor için plan önizlemesi; formül/golden/Ürün/Organizasyon girdileri hazırsa taslak plan. Beş motor aynı anda başlanmaz. Planlama ile fiilî stok tüketim/üretim hareketi ayrı dilimlerdir; sipariş/stok bağımlılıkları hazır sayılmaz.

Yeniden kullanım: Ürün, lokasyon, UI form/list, command/version ve calculator altyapısı. Yeni fark: kütle dengesi, fire/ikinci kalite/artık/WIP ayrımı, çoktan çoğa girdi-çıktı soy ağacı ve formül snapshot'ı.

Erken örnekler: golden hesap ve birimler; farklı formül sürümünün geçmiş snapshot'ı değiştirmemesi; geç önizleme yanıtının yeni girdiyi ezmemesi; önizlemenin kayıt/stok etkisi olmaması; gerçekleşme diliminde aynı command'in ikinci hareket üretmemesi. Yoğunluk/kerf/hız/tolerans bu promptta sabitlenmez.

Ek prompt:

```text
Bu görev yalnız atanmış Üretim motoru ve küçük akıştır. Business güncel kaynaklı formül/golden/parametre snapshot'ını netleştirsin. Backend deterministic calculator, Claude aynı API'den önizleme kullansın; ekranda ikinci formül yazma. Planı gerçekleşme sayma; hesap önizlemesinden stok hareketi üretme. Eksik saha/formül/onay kararını tahmin etme. Mevcut yapıdan gerekli parçaları kullan, beş motorluk genel framework projesi başlatma.
```

## Devam sınırı

Ekler önerilen çalışma yönleridir; mevcut backlog'un yerine geçmez. İş bağımlılığı hazır olmayan modülde kod üretmek hız ölçüsü değildir. Bir akış tamamlandığında tam modül için kalan dilimler Business'ın mevcut listesinde görünür tutulur; ayrı paralel backlog kopyası açılmaz.
