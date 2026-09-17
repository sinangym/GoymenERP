# Sinan kararları — 17 Eylül 2026: K-1 ve maliyet kapsamı

Durum: **Kullanıcının açık kararı kaydedildi; kod uygulaması, test, bağımsız review ve production kabulü ayrı.**

Kaynak: mevcut GöymenERP Business konuşmasında Sinan'ın ifadesi: “K-1:önerilerin doğrultusunda devam edelim ama maliyet yönetimini bütün sistem bittikten sonra detaylandıracağız”. Aynı mesaj önceki raporların güncellenip GitHub'a gönderilmesini ve Koordinatörün buna göre çalışmasını ister. [Issue #66 / 5719251344](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5719251344).

## 1. K-1(a)

[Asıl sorudaki](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5683890499) öneri (a) kabul edildi: Product modülünün ayrı ve dar operasyonel seçim okuma izni; tüketen Satış/diğer operasyonun kendi izni de ayrıca gerekir. Product her okumada kendi dar yetkisini doğrular. Default-deny; otomatik grant/rol ataması yok. İzin adı ve teknik eşleme mevcut Product ve Identity/merkezi sahibindedir.

Dar seçim izni tam Product master yönetimi, geniş liste, fiyat/maliyet veya denetim geçmişi yetkisi değildir. UUID/kod varlığı yetkisize açıklanmaz. Actor/company/authority bağlamı await ve tekrar doğrulamada korunur. Genel yeni rol, family/unit/profile kararı veya production aktivasyonu seçilmez.

Etkilenen uygulama #265. K-1(b) yaklaşımı mevcut tek yazarla düzeltilir; bağımsız exact-head K3 review, fresh CI ve uygulanabilir gerçek runtime kanıtı korunur. İnsan seçimi tekrar sorulmaz; uygulama tamamlandı diye işaretlenmez.

## 2. Ayrıntılı maliyet yönetimi ertelendi

Stok değerleme/üretim maliyet yöntemi, enerji/işçilik/genel gider, ortak/yan ürün/hurda dağıtımı, standart-gerçek sapma ve gelişmiş kârlılık **ana sistem bittikten sonra** detaylandırılır. Hareketli ortalama, FIFO veya başka yöntem seçilmedi.

Şimdi: fiilî miktar ve ölçüm niteliği, sahiplik/konum/lot, kaynak alış/fatura tutarı ve parası, onaylı kur referansı, gerçek sarf/süre, WIP/çıktı bağlantıları korunur. Fiyatlandırma, ticari belgeler, finansal kaynak/açık kalem/dağıtım ve temel genel muhasebe ertelenmez.

Bilinmeyen maliyet sıfır değil; null/değerlenmedi/bekliyor. Değerleme gerektiren muhasebe/rapor kısmı eksik ayrılır; tam mali kapanış veya hesaplanmış kâr iddiası yok. Önceki çek kırdırarak USD/kâr koruma ve yaklaşık 15 günlük üretim çevrimi optimizasyonu ertelemeleri değişmez.

## 3. Uygulama raporu

[Güncellenmiş rapor](../delivery/GOYMEN_ERP_BUTUNLESIK_UYGULAMA_PLANI_v2.md) mevcut çekirdek kapsamın görev eşlemesidir. Kullanıcı hazır işlerin Koordinatörce başlamasını istedi. Uygulayıcı ayrıntıları dar PR'da gerçek kaynakla doğrular; açık iş politikası uydurmaz. Bu belge governance/CI/master/kabul statüsü, banka/sistem ayarı veya canlı migration değiştirme yetkisi vermez. Rapor/karar kaydı review ve merge'i ayrı kapıdadır; doğrudan kullanıcı K-1 kararı mevcut Issue kaydından uygulanabilir.
