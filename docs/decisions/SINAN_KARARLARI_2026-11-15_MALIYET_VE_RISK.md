# Sinan Kararları — 2026-11-15 (tren-1 dalga 2 hazırlığı: çıkış maliyeti, üretim girişi maliyeti, iptal faturanın riski, kg/m tablosu)

Bu belge önceki karar dosyalarını silmez; KLN-D53, URT-D67, FNS-D53 ve KLN-D54 ekler. SAT-D256, SAT-D257, SAT-D260
(`SINAN_KARARLARI_2026-11-09_SATIS_EK51.md`), SAT-D305 ve SAT-D307 (`SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER.md`),
SAT-D311 (`SINAN_KARARLARI_2026-11-12_SATIS_EK60.md`), URT-D47 (`SINAN_KARARLARI_2026-11-13_EK1.md`), URT-D65
(`SINAN_KARARLARI_2026-11-14_DALGA1_SORULAR.md`), EK-23 / DEC-PRD-009 (`SINAN_KARARLARI_2026-10-21.md` satır 9) ve
KLN-D48 (`SINAN_KARARLARI_2026-11-15_DALGA1_EK.md`, PR #1162) geçerliliğini korur. Bu kararlar onların açık bıraktığı
noktaları kapatır. Bir istisna var:

- URT-D67, SAT-D258'in (1) "ortalamaya şimdilik yalnız alış maliyeti girer" ve (2) "üretilen ürünlerde marj
  kullanılamıyor kalır" kısımlarının yerine geçer; SAT-D258'in "alış maliyetinin kapsamı" ve karma kaynak notları
  aynen kalır (karma kaynak için SAT-D305 (1) zaten tek hareketli ortalama der).

Bu değişiklik Sinan'ın daha yeni cevabıdır; ayrıntısı aşağıda.

**Kaynak:** Sinan, 2026-11-15, oturum içinde önerili seçeneklerle soruldu (tek tur, dört soru); Issue #66,
SINAN-CEVAP 20 (https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6887080780). "(Önerilen)" işaretleri
soranındır. Sorular tren-1 dalga 2 hazırlığında `NEEDS-SINAN` kalan dört noktadan doğdu: STK-S39 (çıkış maliyeti),
URT-S14 (üretim maliyet defteri), SAT-S3 / FIN-S20 (işlenmiş fatura kümesi) ve STK-S37 (yapısal profil kg/m).

**Not (soru metni):** 20.2 sorulurken soru metninde "Cevabınız OPEN-008'i kapatır" yazıyordu. Bu ifade fazla geniştir:
`OPEN-008` (`ACIK_KARARLAR.md`) stok değerlemesini ve muhasebe tarafındaki maliyet yöntemini de kapsar. Bu belge
yalnız 20.1 (çıkış değerlemesi) ve 20.2 (üretim girişi maliyeti) cevaplarını kaydeder; `OPEN-008`i kapatmaz (bkz.
"Sınırlar").

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 20.1 | Stok çıkış hareketine (satış sevki, üretime sarf, fire) hangi birim maliyet damgalansın? Mevcut kararlar: hareketli ortalama, depo bazında (SAT-D256/D257). Dalga 3'teki STK-S39 bunu bekliyor. | **Hareket anı ortalaması (Önerilen)** | Çıkış kaydedildiği an, çıktığı deponun o anki hareketli ortalaması damgalanır ve sonradan değişmez. SAT-D256 marj kuralıyla aynı mantık. — Seçilmeyenler: *Ay sonu yeniden değerleme* (çıkışa anlık ortalama yazılır, ay kapanışında o ayın ağırlıklı ortalamasıyla yeniden hesaplanır; daha doğru ama geçmiş kayıtlar değişir; dönem ortalaması SAT-D256'da reddedilmişti); *Belge tarihindeki ortalama* (geriye tarihli belgede, belge tarihine kadar olan girişlerle hesaplanmış ortalama kullanılır; geriye dönük yeniden hesaplama gerekir, karmaşık). |
| 20.2 | Üretilen mamul stoğa girerken ortalamaya hangi maliyetle girsin? Bugün SAT-D258'e göre üretilen üründe maliyet "kullanılamıyor" (OPEN-008 açık). | **Gerçek üretim maliyeti (Önerilen)** | Emre sarf edilen malzemelerin damgalı maliyetleri toplanır, hurda (aylık tek hurda fiyatı, URT-D65/D47) düşülür; kalan, üretilen kg'a bölünüp mamul ortalamasına girer. Maliyet defteri (URT-S14) hesaplar. — Seçilmeyenler: *Şimdilik girmesin* (OPEN-008 açık kalır; üretilen üründe marj ve maliyet "kullanılamıyor" olarak kalır; sonra yeniden sorulur); *Standart maliyetle* (her mamul için Ayarlar'da standart birim maliyet tanımlanır, giriş bununla yapılır, fark ayrı izlenir; ek tanım işi ve fark raporu gerekir). |
| 20.3 | Kredi riskinde Finans'ın "işlenmiş Satış faturaları" kümesinde, iptal edilmiş (ters kaydı yapılmış) bir fatura nasıl görünsün? (SAT-D311; SAT-S3 ve FIN-S20 bekliyor) | **Kümeden çıksın (Önerilen)** | İptal edilen fatura işlenmemiş sayılır; açık alacaktan da düşer. Sevkiyatı yeniden "sevk edilmiş, faturalanmamış" riskine girer. Böylece risk ne iki kez sayılır ne kaybolur. — Seçilmeyenler: *İptal işaretiyle kalsın* (fatura kümede "iptal" durumuyla kalır; Satış bunu görüp sevkiyatı yeniden faturalanmamış sayar; aynı sonuç, ama iki modülün de iptali ayrıca yorumlaması gerekir); *Riskten tamamen düşsün* (iptal edilen faturanın sevkiyatı hiçbir risk satırında sayılmaz; basit, ama yeniden faturalanacak sevkiyatın riski görünmez olur). |
| 20.4 | Yapısal profillerde (I, H, U, köşebent) kg/m elle yazılmayacak, standart tablodan gelecek (KLN-D48). Tablonun kaynağı ne olsun? STK-S37 bunu bekliyor. | **Ayarlarda yönetilen tablo (Önerilen)** | Ayarlar'da sürümlü, kaynağı yazılı bir kg/m tablosu tutulur. Yönetici elindeki standart veya katalogdan (örn. EN 10365) içe aktarır; her değişiklik denetim kaydına girer. Tablo yoksa ağırlık "kaynak yok" kalır. — Seçilmeyenler: *Koda gömülü standart* (seçilen standardın değerleri koda yazılır, yalnız yazılım güncellemesiyle değişir; standart metninin telif ve lisans durumu kontrol edilmeli); *Tedarikçi kataloğu* (her tedarikçinin kendi kataloğu ayrı ayrı yüklenir; aynı profil için tedarikçiye göre farklı kg/m olabilir). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KLN-D53 | Çıkış hareketinin birim maliyeti | Her stok çıkış hareketine (satış sevki, üretime sarf, fire) kaydedildiği an, malın çıktığı deponun o anki hareketli ortalaması (SAT-D256/SAT-D257) damgalanır. Damga sonradan değişmez; ay sonu yeniden değerleme ve belge tarihine göre geriye dönük hesap yapılmaz. Depolar arası transferde SAT-D260 aynen geçerlidir (kaynak deponun o anki ortalaması). Ortalama okunamıyorsa çıkışın maliyeti "okunamaz" kalır; sıfır sayılmaz, tahmin edilmez. Uygulama ayrıntısı (geri alınabilir): damga, ortalamayı hesaplayan aynı deterministik backend hesaplayıcısından okunur; port adı ve sürümü STK-S39 sözleşmesinde yazılır. | Stok çıkış maliyeti portu (STK-S39), hareketli ortalama defteri (STK-S23), Satış marjı (SAT-D256), Üretim sarfı (URT-S14) |
| URT-D67 | Üretim girişinin ortalamaya maliyeti | Üretilen mamul, gerçek üretim maliyetiyle hareketli ortalamaya girer. Net maliyet = emre sarf edilen malzemelerin KLN-D53'e göre damgalanmış maliyetlerinin toplamı − hurda değeri. Hurda değeri, hurdanın tartıldığı ayın tek hurda fiyatıyla hesaplanır (URT-D65, URT-D47). Birim maliyet SAT-D307'deki gibi hesaplanır: net maliyet / (sağlam kg + 2. kalite kg); fire anahtarı SAT-D305 (2)'ye göre uygulanır. İşçilik ve genel gider v1'de yoktur (SAT-D305 (4)). Hesabı Üretim maliyet defteri (URT-S14) yapar ve değeri sürümlü bir portla Stok'a verir. Bu karar SAT-D258'in (1) ve (2) kısımlarının yerine geçer: URT-S14 maliyeti hesapladığı an üretilen ürünün maliyeti ortalamaya girer ve marj hesaplanabilir olur. O ana kadar ve sarf maliyetlerinden biri veya hurda fiyatı okunamıyorsa üretim girişinin maliyeti "kullanılamıyor" kalır (fail-closed; URT-D65'teki "hurda fiyatı yoksa kesinleşmez" kuralı aynen geçerlidir). | Üretim maliyet defteri (URT-S14), Stok üretim girişi maliyet portu (STK-S39), `AverageCostCalculator` (`ProductionReceipt`), Satış marjı |
| FNS-D53 | İptal edilen faturanın risk kümesindeki yeri | Finans'ın Satış'a verdiği "işlenmiş Satış faturaları" kümesinde iptal edilmiş (ters kaydı yapılmış) fatura yer almaz; işlenmemiş sayılır ve açık alacaktan da düşer. Satış, o faturanın sevkiyatını SAT-D311'e göre yeniden "sevk edilmiş, faturalanmamış" riskinde sayar. Aynı tutar hiçbir anda iki risk bileşeninde birden sayılmaz ve hiçbir bileşenden kaybolmaz (AGENTS çift sayım yasağı). "Kümede mi" ve "açık alacak" aynı Finans cevabından okunur (SAT-D311, plan S0 (b)). Küme okunamıyorsa cevap "okunamaz" olur. | Finans risk okuma portu (FIN-S20), Satış risk kontrolü (SAT-S3) |
| KLN-D54 | Yapısal profil kg/m tablosunun kaynağı | Yapısal profillerin (KLN-D48 (2)) kg/m değerleri Ayarlar'da yönetilen, sürümlü ve kaynağı yazılı bir tabloda tutulur. Yönetici değerleri elindeki standart veya katalogdan (örn. EN 10365) içe aktarır; her değişiklik denetim kaydına girer. Tablo veya ilgili satır yoksa ağırlık "kaynak yok" gösterilir; değer uydurulmaz, elle yazılmaz, koda gömülmez. Bu tablo, EK-23 / DEC-PRD-009'daki "doğrulanmış/standart master" kaynağıdır; geometrik hesap yalnız tahmin olarak kalır. Uygulama ayrıntısı (geri alınabilir): tablo şeması, içe aktarma dosya biçimi ve izin adı STK-S37 / Ayarlar dilim spec'inde yazılır; içe aktarma dosyası B-26 dosya güvenliğine tabidir. | Ayarlar (kg/m tablosu), ürün ölçü okuyucusu (STK-S37), Ürün kartı |

## Sınırlar

- `OPEN-008` bu belgeyle kapanmaz. 20.1 ve 20.2 yalnız çıkış değerlemesini ve üretim girişinin maliyetini cevaplar.
  Muhasebe tarafındaki maliyet/değerleme yöntemi ve `OPEN-008`in resmî kapanışı ayrı bir adımdır ve Sinan'ın somut
  kararını ister (SAT-D305 (6)). `ACIK_KARARLAR.md` ve `KARAR_SICILI.md` bu belgeyle değişmez.
- Hurda fiyatının değeri, kg/m tablosunun içeriği ve hangi standardın içe aktarılacağı seçilmez; değerler yönetici
  tarafından girilir. Formül golden'ları olmadan URT-D67 hesabı etkinleşmez.
- İşçilik ve genel giderin maliyete eklenmesi, standart maliyet ve fark raporu seçilmez (`NEEDS-SINAN` olarak
  sorulabilir).
- Bu belge hiçbir izni hiçbir role vermez. kg/m tablosunu içe aktarma izninin herhangi bir role verilmesi Sinan'ın
  somut adımıdır.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
