# Sinan Kararları — 2026-11-21 (tren-1 kapanışı: dövizli marj, Finans reddi ve yeniden gönderim, muhasebe aktarımı, stok satırından tahsis, cari filtresi, mükerrer ürün uyarısı, fiyat listesi kapsamı)

Bu belge önceki karar dosyalarını silmez; SAT-D426, SAT-D427, SAT-D428, KLN-D116, KLN-D117, KLN-D118, URN-D25,
URN-D26, SAT-D429, SAT-D430 ve SAT-D431 ekler. SAT-D259 (2) (marj TL tutarıdır), SAT-D265, SAT-D230, SAT-D249, BS-1 (dövizli alışta TL maliyetin
tedarikçi faturasındaki resmî kurla hesaplanması), DEC-CAR-003 ve SAT-D306 geçerliliğini korur; bu kararlar onların
ve tren-1 kod incelemesinin açık bıraktığı noktaları kapatır.

**Kaynak:** Sinan, 2026-11-21, bu Claude Code oturumunda soru penceresiyle önerili seçeneklerle cevapladı ("Benim kararımı
bekleyen tüm soruları sor önerili şekilde"; SINAN-CEVAP 49, ajan aktarımı: Issue #1189 yorumu 7065185254). Dokuz cevabın sekizi önerilen seçenektir; 5. soruda önerilmeyen seçenek seçildi. 9. soru, 1. cevabın uygulanmasında çıkan boşluk için aynı oturumda soruldu. "(Önerilen)"
işaretleri soranındır. Sorular, PR #1201 (tren-1) incelemesinde koddaki `NEEDS-SINAN` notlarından ve marj golden'ının
(`SAT_S12_SATIS_MARJI_FORMUL_GOLDEN_V1.md`) açık bıraktığı sınırdan doğdu. 10–13. sorular aynı gün, K3 incelemesinin ardından
"Kararımı bekleyenleri önerili şekilde sor" isteğiyle soruldu (SINAN-CEVAP 50, ajan aktarımı: Issue #1189 yorumu 7067213965);
10. soruya seçenek dışı serbest cevap verildi, 12. soruda önerilmeyen seçenek seçildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması | Seçilmeyen seçenekler |
|---|---|---|---|---|
| 1 | Dövizli (USD) satışta kâr marjı nasıl hesaplansın? Şu an satış TL dışıysa marj "hesaplanamaz" görünüyor. | **Fatura/sipariş kuru (Önerilen)** | USD satış tutarı, siparişte/faturada kayıtlı kurla TL'ye çevrilir; marj TL hesaplanır. Maliyetteki "tedarikçi faturasının resmî kuru" kararıyla uyumlu. | Satış günü TCMB kuru (satış tarihindeki TCMB kuruyla TL'ye çevrilir); USD marj (TL maliyet USD'ye çevrilir, marj USD gösterilir); Şimdilik gösterme (mevcut durum). |
| 2 | Finans, kesinleştirilmiş bir satış faturasını "yetki" gerekçesiyle reddederse çıkış yolu ne olsun? | **Yetkili kişi yeniden gönderir (Önerilen)** | Red kişiye bağlıdır; kesinleştirme yetkisi olan başka bir kullanıcı "Finans'a yeniden gönder" ile aynı faturayı tekrar gönderir. Fatura değişmez. | İptal edip yeniden hazırla (ters kayıt, yeni taslak, baştan onay); Yalnız bilgi, elle çözülür (ERP dışında). |
| 3 | Finans yanıtı beklenen faturalar için otomatik yeniden gönderim olsun mu? | **Otomatik yok, yalnız elle (Önerilen)** | Arka planda hiçbir şey gönderilmez; yetkili kullanıcı düğmeyle yeniden gönderir. | 15 dk'da bir, en fazla 4 kez; Saatte bir, en fazla 24 kez. |
| 4 | Fatura olaylarının muhasebeye otomatik alınması (kod hazır, ayarla kapalı) pilotta açılsın mı? | **Kapalı kalsın (Önerilen)** | Pilotun ilk aşamasında satış/alış/stok akışları doğrulanır; muhasebe aktarımı ayrı kabulden sonra açılır. | Pilotta açılsın (kesinleşen faturalar muhasebe kaynak fişine otomatik düşer; muhasebe onayı yine iki kişiyle). |
| 5 | Stok listesinde bir etiket satırından "Satışa tahsis et" ve "Yükleme talimatı oluştur" başlatılabilsin mi? Şu an yalnız satış siparişi satırından başlıyor. | **Evet, etiketten de başlasın** | Etiket satırında sipariş seçilerek tahsis/yükleme talimatı başlatılır; ek ekran ve kontrol gerekir. | Hayır, satış satırından (Önerilen; tahsis ve talimat sipariş satırından başlar, stok satırı yalnız mevcut tahsisleri gösterir/bırakır). |
| 6 | Cari listesi açıldığında varsayılan filtre ne olsun? | **Yalnız aktif cariler (Önerilen)** | Pasif cariler filtreyle görülür; şu anki uygulama budur. | Tümü. |
| 7 | Yeni ürün kartında mükerrer uyarısı hangi kurala göre verilsin? | **Teknik ölçüler aynıysa uyar (Önerilen)** | Ürün ailesi + kalınlık + genişlik + kalite + kaplama aynı olan aktif kart varsa uyarı verilir; kayıt yine engellenmez. | Yalnız ad/kod benzerliği; Kural yok, liste yeter (mevcut durum). |
| 8 | Fiyat listelerinde şu an yalnız satış kanalı ve KDV hariç fiyat açık. Pilot için kapsam genişlesin mi? | **Şimdilik böyle kalsın (Önerilen)** | Satış kanalı, KDV hariç. Alış fiyatları satınalma siparişinde zaten tutuluyor. | Alış kanalı da açılsın; KDV dahil görünüm de eklensin. |
| 9 | (1. sorunun devamı; kod incelemesinde satış siparişinde kur alanı olmadığı, kurun yalnız faturada tutulduğu görüldü.) Dövizli marj için hangisi uygulansın? | **Onayda sipariş kuru dondur (Önerilen)** | Sipariş onaylanırken Ayarlar'daki o günün kuru (önceki iş günü TCMB satış, OPEN-012) siparişe yazılır; marj bu kurla TL hesaplanır. Yeni alan ve migration gerekir. Onay öncesi önizlemede kur yoksa marj gösterilmez. | Yalnız faturalanınca göster (siparişte gösterilmez; fatura kuruyla fatura tarafında); Şimdilik gösterme. |
| 10 | Dövizli satış marjı (SAT-D426) için formül golden'ı ana dala girmeden marj açılamıyor. Golden'ı kim hazırlasın? | **Serbest cevap:** "Bunu chatgpt ye söylemem ve sana aktarman zor , bunu sen organize edebilirmisin" | Seçeneklerin dışında; golden'ın hazırlanmasını Claude'un organize etmesi istendi (SAT-D431). | ChatGPT hazırlasın (Önerilen); Claude taslağı + iki review; Şimdilik kapalı kalsın. |
| 11 | Dövizli siparişin iç onayında o günün kuru Ayarlar'da bulunamazsa ne olsun? | **Onay engellenmesin (Önerilen)** | Sipariş onaylanır, yalnız marjı "hesaplanamaz" kalır. | Onay engellensin. |
| 12 | İlk onayda kur bulunamadığı için kursuz kalan bir sipariş, sonra revizyonla yeniden onaylanırsa kur o gün okunsun mu? | **Evet, ilk başarılı onayda donsun** | Kur, okunabildiği ilk onayda dondurulur; eski satırlar kursuz, yeni/değişen satırlar o kurla hesaplanır. | Hayır, kursuz kalsın (Önerilen). |
| 13 | İkinci bağımsız K3 review turu nasıl yapılsın? | **Modül modül tam kapsam (Önerilen)** | Her inceleme oturumu farkı parçalara bölüp her parçayı ayrı bir okuyucuya verir. | Yalnız son değişiklikler. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D426 | Dövizli satışta marj | Satış tutarı fonksiyonel para birimi (TL) dışındaysa, satış tutarı **siparişte kayıtlı kurla** TL'ye çevrilir ve marj TL olarak hesaplanır (SAT-D259 (2) korunur). Sipariş kuru **onay anında dondurulur**: Ayarlar'ın o gün geçerli kuru (OPEN-012 başlangıç kuralı: önceki iş günü TCMB satış) siparişe yazılır ve sonra değişmez (9. cevap). Onay öncesi önizlemede ve kayıtlı kuru olmayan eski onaylı siparişlerde marj hesaplanmaz ("hesaplanamaz" kalır); geçici kur üretilmez. Kur okunamazsa onay kur olmadan sürer ve marj hesaplanamaz kalır. Maliyet tarafı BS-1'e göre okunur (TL değer ya da Unavailable). Hesap merkezi marj hesaplayıcısındadır; ekran yeniden hesaplamaz. | Satış marj hesaplayıcısı (`SalesLineMargin`), marj golden'ı (yeni satırlar) |
| SAT-D427 | Finans'ın yetki gerekçeli reddinden çıkış | Finans faturayı yetki gerekçesiyle reddederse, `sales.invoices.finalize` yetkisi olan bir kullanıcı aynı faturayı "Finans'a yeniden gönder" ile tekrar gönderebilir; fatura içeriği değişmez, yeni bir fatura üretilmez. Sunucu her gönderimde yetkiyi yeniden ölçer. | Satış fatura kesinleştirme/yeniden gönderim (API-36), fatura paneli |
| SAT-D428 | Otomatik yeniden gönderim | Finans yanıtı beklenirken arka planda otomatik yeniden gönderim **yoktur**; yeniden gönderim yalnız yetkili kullanıcının elle başlattığı komuttur. | Satış fatura paneli metni, arka plan işleri (eklenmez) |
| SAT-D429 | Kur bulunamazsa iç onay | Dövizli siparişin iç onayında onay gününün kuru Ayarlar'da çözülemezse onay **engellenmez**; o onayda sabitlenen satırların marjı "hesaplanamaz" kalır. | Satış iç onayı, marj anlık görüntüsü |
| SAT-D430 | Kursuz kalan siparişin kuru | Sipariş kuru, **kurun çözülebildiği ilk onayda** dondurulur ve sonra değişmez; önceki onaylarda kursuz sabitlenen satırlar kursuz kalır, sonraki revizyon onaylarında sabitlenen satırlar dondurulan kuru kullanır. Başka para birimi için dondurulmuş kur yeni kurla değiştirilmez (türetilmiş; uygulama ayrıntısı). | Satış iç/revizyon onayı, marj anlık görüntüsü |
| SAT-D431 | SAT-D426 formül golden'ının hazırlanması | SAT-D426 golden satırlarını ChatGPT şeridi yerine **Claude organize eder**: uygulamanın yazarı olmayan, temiz bağlamlı ayrı bir oturum golden'ı karar metinlerinden bağımsız hesaplar; ayrı dokümantasyon PR'ı iki bağımsız review sonrası main'e girer. Döviz dönüşümü uygulamada ancak bu golden main'e girdikten ve uygulama ona eşit çıktıktan sonra açılır (SAT-D265 (5)). | Marj golden belgesi, `ForeignSaleMarginGoldenAccepted` |
| KLN-D116 | Muhasebeye otomatik aktarım pilotta | Fatura olaylarının muhasebe kaynak fişine otomatik alınması pilotun ilk aşamasında **kapalı** kalır; açılması ayrı muhasebe kabulünden sonra ayrı kullanıcı kararıyla yapılır. | Muhasebe aktivasyon ayarı (değişmez) |
| KLN-D117 | Stok etiket satırından tahsis ve yükleme talimatı | Stok listesinde bir etiket satırından "Satışa tahsis et" ve "Yükleme talimatı oluştur" başlatılabilir: kullanıcı sipariş satırını seçer; tahsis ve talimat, sipariş satırından başlayan akışla aynı sunucu komutlarını ve kurallarını kullanır (yetki, miktar, sahiplik, şirket sınırı). Sipariş satırından başlatma da korunur. | Stok listesi satır eylemleri, ilgili seçim ekranı |
| KLN-D118 | Cari listesinin varsayılan filtresi | Cari listesi varsayılan olarak **yalnız aktif** carileri gösterir; pasifler filtreyle görülür (mevcut uygulama). | Cari listesi (değişmez; açık not kapanır) |
| URN-D25 | Mükerrer ürün kartı uyarısı | Yeni/düzenlenen ürün kartında ürün ailesi + kalınlık + genişlik + kalite + kaplama aynı olan **aktif** bir kart varsa uyarı gösterilir; kayıt ve onay hiçbir durumda engellenmez. Karşılaştırma sunucuda yapılır. | Ürün mükerrer aday sorgusu ve paneli |
| URN-D26 | Fiyat listesi kapsamı | Pilotta fiyat listeleri yalnız satış kanalı ve KDV hariç fiyatla kalır; alış/ortak kanal ve KDV dahil görünüm açılmaz. | Fiyat listesi ekran metni (açık not kapanır) |

## Sınırlar

- SAT-D426 faturadaki kuru siparişin marjına taşımaz; fatura kendi kurunu korur. Kurun okunamadığı onay reddedilmez
  (SAT-D429).
  Uygulama marj golden'ına yeni satır ister (SAT-D265 (5)); main'deki golden G20'yi koruduğu sürece döviz dönüşümü uygulamada
  kapalıdır, kur onayda dondurulup saklanır. Sipariş kuru tektir: revizyon onayı ilk onayda dondurulan kuru kullanır.
- Maliyet birimi ile sipariş birimi farklı olduğunda marj (birim dönüşümü) ve karışık kaynaklı kalem marjı bu belgeyle
  açılmaz.
- KLN-D117'deki seçim ekranının ayrıntısı (hangi sipariş satırlarının listeleneceği) geri alınabilir uygulama ayrıntısıdır;
  tahsis/sevk kuralları değişmez.
- URN-D25 benzerlik (bulanık eşleşme) kuralı getirmez; yalnız beş alanın tam eşitliğine bakar.
- `OPEN-*` kapatılmaz; `KARAR_SICILI.md` ve `ACIK_KARARLAR.md` değişmez.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu (muhasebe otomatik aktarımının açılması dahil), `OPEN-*` kapatma ve `.claude/settings.json`
  her zaman somut kullanıcı kararı ister.
