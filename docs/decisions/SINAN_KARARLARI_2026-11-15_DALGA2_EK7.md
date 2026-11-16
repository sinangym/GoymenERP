# Sinan Kararları — 2026-11-15 (Dalga 2 ek 7: kg/m başlangıç tablosu, kantar farkı ve kütle dengesi başlangıç değerleri, benzer ürün ölçü toleransı)

Bu belge önceki karar dosyalarını silmez; URN-D17, KLN-D69, URT-D76 ve KLN-D70 kararlarını ve
ASSUMPTION-STK-SIMILAR-ORDER-1 varsayımını ekler; bir noktayı `NEEDS-SINAN` olarak açık bırakır. URN-D13…URN-D16 (ağırlık tablosu içe aktarma, kaynak etiketi,
izin, tablo kaynağı), BS-9/BS-13/BS-16/BS-17 (yoğunluk, yuvarlama, kantar–hesap %1), DEC-STK-009, DEC-SET-001,
URT-D6, URT-D9, KLN-D34, KLN-D39 ve KLN-D40 aynen geçerlidir.

**Kaynak:** Sinan, 15 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP 28). Sorular,
Sinan'ın "Benden ürün için beklenen onayları araştır; kolay ve mühendislik hesabı gibi ya da basit bir mantıkla
düşünülebilir" isteği üzerine açık ürün maddelerinden derlendi. "(Önerilen)" işaretleri soranındır. Dört sorunun
üçünde önerilen seçenek seçildi; dördüncüsüne serbest metinle cevap verildi (aşağıda kelimesi kelimesine).

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 28.1 | kg/m taslak tablosu (720 satır, yoğunluk 7,85, EN 10219-2 formülleri, standart tabloya göre en büyük sapma %0,36) başlangıç tablosu olarak kabul edilsin mi? | **Kabul, kaynak etiketli (Önerilen)** | "Tablo 'Formülle hesaplandı (EN 10219-2, 7,85)' kaynak etiketiyle içe aktarılır. Tedarikçi veya kantar verisi geldikçe satır satır düzeltilir; tablo Ayarlar'dan değiştirilebilir." Seçilmeyenler: "Kendi tablomu vereceğim" — taslak kullanılmaz, Sinan'ın Excel tablosu gelene kadar profil/boru kg hesabı 'kaynak yok' döner; "Yalnız standart ölçüler" — taslakta yalnız standart katalogda bulunan ölçüler alınır, geri kalan satırlar Sinan'ın verisiyle eklenir. |
| 28.2 | Mal kabulde kantar–irsaliye kg farkı (OPEN-004) için %1 uyarı, %2 onay, %3 blok başlangıç değerleri olarak onaylansın mı? (Saha ölçümü geldikçe Ayarlar'dan değişir.) | **%1 / %2 / %3 onay (Önerilen)** | "İki kantarın toplam hatası yaklaşık %0,2–0,4; %1 bunun rahatça üstünde, gerçek bir eksiği de kaçırmaz. Değerler başlangıç olarak onaylanır; saha ölçümü (golden test) ayrıca yapılır." Seçilmeyenler: "Daha sıkı: %0,5 / %1 / %2" — daha küçük farklar da yakalanır, ancak ambalaj ve kantar hatası yüzünden yanlış uyarı artar; "Daha gevşek: %2 / %3 / %5" — daha az uyarı çıkar, ancak gerçek bir eksiğin gözden kaçma ihtimali artar. |
| 28.3 | Üretimde hat bazında kütle dengesi (giren kg ile çıkan kg + fire + hurda farkı, OPEN-005) için ±%2 başlangıç değeri olarak onaylansın mı? | **±%2 onay (Önerilen)** | "Boya ekleyince kg artar, kesim ve uç firesi düşürür; normal bir hatta toplam fark %1–1,5 civarındadır. ±%2 bunu karşılar. Yetkili kişi plan bazında değiştirebilir; Ayarlar'dan değişir." Seçilmeyenler: "Hat türüne göre ayrı" — boya hattında ±%3, kesme/dilme hattında ±%1 gibi her hat için ayrı başlangıç değeri girilir; "Daha sıkı: ±%1" — fire kaçağı daha erken yakalanır, ancak boya ağırlığı yüzünden boya hattında sık uyarı çıkabilir. |
| 28.4 | Rezervasyonda aynı ürün yoksa 'benzer ürün' önerisinin başlangıç ölçü toleransı ne olsun? (Aynı malzeme, kalite ve ürün tipi şartı zaten kararlı; değerler Ayarlar'dan değişir.) | Serbest metin: "Bence kalınlık en boy da 3 ü içinde +/- toleranslı yakın olanlar olabilir, sadece örnek vereceğim 4 mm kalınlık için 3.50 mm ile 4.50 mm olanlarıda önerebilir en boy da aynı şekilde bir %tolerans mantığı kur" | Sunulan seçenekler: "Kalınlık aynı, büyük olur (Önerilen)" — kalınlık birebir aynı; en ve boy siparişe eşit ya da en fazla %10 büyük olabilir, küçük parça önerilmez; "Simetrik tolerans" — kalınlık ±0,1 mm, en ±5 mm, boy ±%5; "Yalnız aynı ürün" — tolerans 0, benzer ürün önerilmez. Hiçbiri olduğu gibi seçilmedi; Sinan üç ölçüde de yüzdeyle ± tolerans istedi. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URN-D17 | kg/m başlangıç tablosu (28.1; URN-D16'yı tamamlar) | (1) Sinan'a sunulan 720 satırlık taslak tablo, profil ve boru kg/m **başlangıç tablosu** olarak onaylandı. Veri dosyası `docs/operations/veri/kgm_baslangic_tablosu_v1.csv` ayrı veri PR'ındadır (#1184); SHA-256 değeri o PR'ın veri notunda yazılıdır. Veri PR'ı kg/m değerlerini değiştirmez; yalnız kaynak etiketi kolonunu bu karara göre düzenler. (2) Tablo ERP'ye seed veya migration ile yazılmaz; yetkili kullanıcı URN-D13'teki içe aktarma yoluyla yükler (dosya kasası ve tarama kuralları aynen geçerlidir). (3) EN 10219-2 satırlarının kaynak etiketi (URN-D14) **"Formülle hesaplandı (EN 10219-2, 7,85)"** olur. Tablodaki 31 EN 10255 borusunun etiketi Sınırlar'daki `NEEDS-SINAN` maddesine bağlıdır. (4) Tedarikçi belgesi veya kantar ölçümü geldikçe satır yeni kaynak etiketiyle düzeltilir; düzeltme denetim kaydına girer. (5) Tablonun dışında kalan ölçü `NoSource` döner (BS-9). | Ürün ağırlık tablosu içe aktarma (STK-S37), ürün birim çevrim calculator'ı |
| KLN-D69 | Mal kabul kantar–irsaliye farkı başlangıç değerleri (28.2; OPEN-004) | (1) Kantar–irsaliye kg farkı için başlangıç değerleri **%1 uyarı, %2 onay, %3 blok** olarak işletme onayı aldı. (2) Değerler DEC-SET-001'e göre ürün/işlem bazlı Ayarlar politikasında tutulur; kodda sabit yazılmaz. (3) Saha golden ölçümü ayrıca yapılır; ölçüm sonucuna göre değerler Ayarlar'dan değişir. (4) `OPEN-004` sicil durumu bu belgeyle değiştirilmez; saha golden kanıtı geldikten sonra ayrı Sinan kararıyla kapanır. | Mal kabul/kantar akışı, Ayarlar tolerans politikası |
| URT-D76 | Hat bazında kütle dengesi başlangıç değeri (28.3; OPEN-005, URT-D6'yı doğrular) | (1) Hat bazında kütle dengesi toleransı başlangıç değeri **±%2** olarak işletme onayı aldı. (2) Yetkili kullanıcı plan bazında değiştirebilir (URT-D6); değer Ayarlar'dan değişir, kodda sabit yazılmaz. (3) Saha doğrulaması ayrıca yapılır. (4) `OPEN-005` sicil durumu bu belgeyle değiştirilmez; saha kanıtı geldikten sonra ayrı Sinan kararıyla kapanır. | Üretim kütle dengesi kontrolü, Ayarlar |
| KLN-D70 | Benzer ürün ölçü toleransı (28.4; KLN-D40'ı tamamlar) | **Sinan'ın sözü** (yukarıda kelimesi kelimesine). (1) Aynı ürün yoksa benzer ürün önerisinde **kalınlık, en ve boyun üçü de** yüzde ile **simetrik ±** toleransla karşılaştırılır; üç ölçünün her biri kendi tolerans aralığında olmalıdır. Aynı malzeme/kalite ve ürün tipi şartı (KLN-D40) aynen geçerlidir. (2) Başlangıç değeri, Sinan'ın örneğinden (4 mm için 3,50–4,50 mm) türetilen **±%12,5**'tir ve üç ölçüye ayrı ayrı uygulanır. (3) Her ölçünün yüzdesi Ayarlar'da ayrı ayrı değiştirilebilir; kodda sabit yazılmaz. (4) Öneri listesinin sıralaması karar değildir; ASSUMPTION-STK-SIMILAR-ORDER-1 ile geri alınabilir varsayımdır. (5) Ürün kartında bir ölçü eksikse ne olacağı Sinan'ın cevabında yoktur; Sınırlar'da `NEEDS-SINAN` olarak açıktır. (6) Öneri yalnız öneridir; rezervasyon kullanıcı onayıyla yapılır (KLN-D34). | Stok rezervasyon önerisi (benzer ürün), Ayarlar |

## Varsayım (geri alınabilir)

| No | Konu | Varsayım |
|---|---|---|
| ASSUMPTION-STK-SIMILAR-ORDER-1 | Benzer ürün öneri listesinin sıralaması (KLN-D70 (4)); Sinan'ın cevabında sıralama yoktur | Yakınlık, karşılaştırılan ölçülerdeki göreli sapmaların en büyüğüyle ölçülür (`max(|aday − istenen| / istenen)`); eşitlikte göreli sapmaların toplamı, sonra etiket kodu sırası kullanılır. Yalnız sıralamayı etkiler; hangi ürünün önerileceğini KLN-D70 (1)–(3) belirler. Para/stok/yetki etkisi yoktur. Sinan itiraz ederse değiştirilir. |

## Sınırlar

- `NEEDS-SINAN`: Ürün kartında kalınlık, en veya boydan biri eksikse benzer ürün karşılaştırması ne yapar (eksik
  ölçüyü atlar mı, ürünü öneriden çıkarır mı)? Bu belge bu durumu seçmez; uygulama dilimi bu
  davranışı Sinan'ın cevabından önce etkinleştirmez.
- Taslaktaki 31 EN 10255 borusunun kaynak etiketi SINAN-CEVAP 42.4 ile cevaplandı (EK20 URN-D24, #1186). L serisi 9 satırın ölçüleri doğrulanmamıştır ve öyle işaretlidir.
- 28.1 sorusundaki "en büyük sapma %0,36" rakamı, taslak hazırlanırken 19 ölçünün yayımlanmış tablolarla
  karşılaştırılmasından gelir. Karşılaştırma listesi depoda yoktur ve rakam doğrulanmış kabul edilmez. Soru,
  Sinan'a sorulduğu biçimiyle kayıtlıdır. Veri tablosundaki yuvarlak değerler tam katsayı π·7,85/1000 ile
  hesaplandı ve 3 ondalığa yuvarlandı; CSV `Note` kolonundaki yöntem notu da tam katsayı π·7,85/1000'dir (#1184).
- URN-D17'deki içe aktarma dilimi (STK-S37) SC27 karar PR'ına (#1170) dayanır.
- Bu belge `OPEN-004` ve `OPEN-005`'i kapatmaz; başlangıç değerlerine işletme onayı verir. Kapanış saha golden kanıtı
  ve ayrı Sinan kararı ister.
- Tablonun ERP'ye fiilen yüklenmesi, `product.profile-weights.import` izninin kişilere verilmesi (URN-D15) ve
  AI destekli benzer ürün önerisinin açılması (KLN-D40, AI-D6) ayrıca insan kararıdır; bu belge bunları açmaz.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
