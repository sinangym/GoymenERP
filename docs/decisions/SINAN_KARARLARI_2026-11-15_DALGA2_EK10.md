# Sinan Kararları — 2026-11-15 (Dalga 2 ek 10: üretim maliyetinde yarım yuvarlama yönü ve ondalık, kalan kuruş, kapanmış emirde tartım düzeltmesi, sözleşmede iskonto oranlarının gizlenmesi)

Bu belge önceki karar dosyalarını silmez; URT-D78…URT-D81 ve SAT-D363 kararlarını ekler. SAT-D11 (yarımda sıfırdan
uzağa), SAT-D265 (stok ortalaması 4 ondalık), 21 Ekim 2026 kararlarındaki "Bankacı yuvarlama kullanılmaz" kuralı,
URT-D12, URT-D32 (yeniden açma kuralları), URT-D54, URT-D77 (yeniden açma izni) ve SAT-D361 (fiyat okuma izni
olmayana sözleşme tutarları gizli) aynen geçerlidir. URT-D54 ve URT-D77 henüz merge edilmemiş karar PR'larındadır;
bu belge onlara o PR'lardaki metinle atıf yapar.

**Kaynak:** Sinan, 15 Kasım 2026, bu Claude Code oturumunda soru penceresiyle iki turda cevapladı (SINAN-CEVAP 31).
Sorular, Üretim takımının tren-1 dalga 3 raporundaki (URT-S14, URT-S15) ve Satış takımının SC29 takip raporundaki
(SAT-S14) `NEEDS-SINAN` maddelerinden geldi. "(Önerilen)" işaretleri soranındır. İlk turda yarım yuvarlama yönü
sorusuna serbest metinle cevap verildi: **"Hesaplama biçiminin diğer hesaplamaları fiyat tutar hesaplamalarına kadar
nasıl etkiledğini araştır mantıklı karar verelim gerekirse ondalık hanesinide arttıralım"**. Bunun üzerine depodaki
yuvarlama kuralları araştırıldı ve araştırma özeti ikinci turdaki iki sorunun metnine yazıldı (31.5 ve 31.6,
kelimesi kelimesine aşağıda). Diğer dört soruda ve ikinci turdaki iki soruda önerilen seçenek seçildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 31.1 | Üretim maliyeti yuvarlanırken tam yarıda kalan tutar (ör. 0,125 → 2 ondalık) hangi yöne yuvarlansın? Karar yokken sistem maliyeti 'bekliyor' bırakıyor. | Serbest metin: **"Hesaplama biçiminin diğer hesaplamaları fiyat tutar hesaplamalarına kadar nasıl etkiledğini araştır mantıklı karar verelim gerekirse ondalık hanesinide arttıralım"** | Seçilmeyenler: "Sıfırdan uzağa (Önerilen)" — "0,125 → 0,13. Satıştaki SAT-D11 ile aynı; muhasebede alışılan 'yarım yukarı' kuralı. Tüm sistemde tek kural olur."; "Çift sayıya (banker)" — "0,125 → 0,12; 0,135 → 0,14. Çok sayıda satırda toplam sapmayı azaltır, ama satıştaki kuraldan farklı olur." Karar 31.5 ve 31.6 ile verildi. |
| 31.2 | Bir üretim emrinde birden çok iyi ürün etiketi varsa, yuvarlamadan kalan kuruş farkı hangi satıra yazılsın? | **En çok kg'lı satıra (Önerilen)** | "Fark en ağır satırda oransal olarak en küçük kalır; eşitlikte en küçük etiket numarası. İyi ürün yoksa maliyet 'bekliyor' kalır." Seçilmeyenler: "İlk etikete" — "Basit ve öngörülebilir; ama küçük bir satırda birim maliyeti görünür şekilde oynatabilir."; "Son etikete" — "Kapanışta en son üretilen satır farkı alır." |
| 31.3 | Eskiden kapanmış, girdiden fazla çıktısı olan bir üretim emrinde tartım hatası bulunursa düzeltme nasıl yapılsın? | **Önce emir yeniden açılır (Önerilen)** | "Mevcut yeniden açma kuralları geçer (URT-D32: muhasebe dönemi kapalıysa ret; URT-D77 izni). Sonra ters kayıt + doğru tartım; öneren ≠ onaylayan." Seçilmeyen: "Kapalı emirde düzeltme" — "Emir kapalı kalır; ters kayıt + doğru tartım doğrudan girilir, öneren ≠ onaylayan. Daha hızlı, ama kapalı emrin maliyeti açmadan değişir." |
| 31.4 | Fiyat okuma izni olmayan kullanıcı satış sözleşmesini açtığında tutarlar gizleniyor (SAT-D361). İskonto oranları ve iskonto sınırı yüzdesi de gizlensin mi? | **Evet, oranlar da gizli (Önerilen)** | "Sipariş satırlarında iskonto yüzdesi zaten gizli; oran fiyatı tahmin ettirir. Tutarlı olur." Seçilmeyen: "Oranlar görünsün" — "Yalnız tutarlar gizli; oranlar ve sınır yüzdesi açık kalır." |
| 31.5 | "Araştırma sonucu: sistemde zaten tek yarım kuralı var, 'sıfırdan uzağa' (0,125→0,13). Satış, alış, stok ortalaması ve muhasebe bunu kullanıyor; 21 Ekim kararınız 'Bankacı yuvarlama kullanılmaz' diyor. Üretim maliyetinde yön farkı kg başına en çok 0,0001 TL (25 tonda ≤ 2,5 TL). Kalan kuruş kuralı toplamı zaten sıfırlıyor. Fatura tutarını hiç etkilemiyor; yalnız marjı ve ileride satılan malın maliyetini etkiliyor. Üretim maliyetinde hangi kural?" | **Sıfırdan uzağa (Önerilen)** | "Tüm sistemde tek kural kalır (SAT-D11, 21 Ekim No 15 ile aynı). Üretim → stok → satış → muhasebe tutarlı." Seçilmeyenler: "Bankacı (çift sayıya)" — "21 Ekim 'bankacı yuvarlama kullanılmaz' kuralını üretim için değiştirir; iki kural olur."; "Ayarlar'dan seçilsin" — "Şirket bazında ayar; esnek ama yeni ayar ve test yükü, ve karışık zincir riski." |
| 31.6 | "Ondalık hanesi: asıl farkı yön değil ondalık sayısı yaratıyor. Örnekte 2 ondalıklı kg maliyeti 3,50 TL sapma üretirken 4 ondalıkta sapma 0,10 TL. Etiket tutarları yuvarlanmamış maliyetten hesaplanınca sapma sıfır oluyor. kg maliyeti kaç ondalık saklansın?" | **4 ondalık + tam hesap (Önerilen)** | "kg maliyeti kayıtta 4 ondalık (stok ortalaması SAT-D265 ve URT-D54 ile aynı). Etiket tutarı yuvarlanmamış maliyet × kg ile hesaplanıp bir kez 2 ondalığa yuvarlanır. Toplam = giren TL, sapma yok." Seçilmeyen: "6 ondalığa çıkar" — "Daha ince, ama stok ortalaması, marj ve 3 modüller arası sözleşme değişir (K3 iş). Bu örnekte kazanç pratikte yok." |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URT-D78 | Üretim maliyetinde yarım yuvarlama yönü | Üretim maliyetinde tam yarıda kalan tutar sıfırdan uzağa yuvarlanır (0,125 → 0,13; −0,125 → −0,13). Bu, SAT-D11 ve 21 Ekim 2026 "Bankacı yuvarlama kullanılmaz" kuralıyla aynı tek kuraldır; şirket bazında ayar açılmaz ve yön için ayrı bir politika kaynağı gerekmez. Yön kararı bulunduğundan maliyet artık "yuvarlama kuralı belirsiz" gerekçesiyle bekletilmez. Uygulama ayrıntısı (geri alınabilir): ortak para yuvarlama yardımcısının kullanılması ve hesaplayıcıdaki politika girdisinin sabit kurala bağlanma biçimi. | URT-S14 üretim maliyeti yuvarlama hesaplayıcısı ve maliyet portu |
| URT-D79 | Üretim maliyetinde ondalık ve hesap sırası | Hesap tam hassasiyetle yapılır. kg maliyeti kayıtta 4 ondalık saklanır (SAT-D265 ve URT-D54 ile aynı). Etiket (satır) tutarı, yuvarlanmamış kg maliyeti × o satırın kg'ı ile hesaplanır ve yalnız bir kez 2 ondalığa yuvarlanır; saklanmış 4 ondalıklı kg maliyetinden yeniden hesaplanmaz. Emir toplamı satır tutarlarının toplamıdır ve ayrıca yuvarlanmaz; URT-D80'deki kalan kuruşla birlikte giren TL = dağıtılan TL olur. 6 ondalığa geçilmez; stok ortalaması, marj ve modüller arası sözleşmelerin ondalık sayısı değişmez. Bu karar fatura ve satış tutarlarının hesabını değiştirmez. | URT-S14 hesaplayıcısı, üretim giriş maliyeti, maliyet izi raporu |
| URT-D80 | Kalan kuruşun yazıldığı satır | Yuvarlamadan kalan kuruş farkı, emrin iyi ürün etiketleri içinde en çok kg'lı satıra yazılır. Uygulama ayrıntısı (geri alınabilir): kg eşitliğinde en küçük etiket numaralı satır seçilir. Emirde iyi ürün etiketi yoksa maliyet "bekliyor" kalır, kalan kuruş başka satıra (hurda, fire) yazılmaz. | URT-S14 hesaplayıcısı |
| URT-D81 | Kapanmış, girdiden fazla çıktılı emirde tartım düzeltmesi | Eskiden girdiden fazla çıktıyla kapanmış bir emirde tartım düzeltmesi (URT-D53) yapılabilmesi için emir önce yeniden açılır. Yeniden açma URT-D32 kurallarına (muhasebe dönemi kapalıysa ret) ve URT-D77 iznine tabidir. Emir açıldıktan sonra yanlış çıkışın ters kaydı ve doğru tartımın girişi URT-D53'teki öneren ≠ onaylayan akışıyla yapılır. Kapalı emirde doğrudan düzeltme yapılmaz; deneme reddedilir. | URT-S15 tartım düzeltmesi, üretim emri yeniden açma akışı |
| SAT-D363 | Sözleşmede iskonto oranlarının gizlenmesi | Şirket çapında `sales.orders.price.read` izni olmayan kullanıcıya satış sözleşmesinde tutarlarla birlikte iskonto oranları ve iskonto sınırı yüzdesi de gösterilmez. Gizleme sunucuda yapılır (alan boş döner); ekran "Gizli" yazar. Bu, sipariş satırlarındaki iskonto yüzdesinin gizlenmesiyle aynı kuraldır. | SAT-S14 satış sözleşmesi okuma uçları ve ekranı |

## Sınırlar

- URT-D78, Sinan'ın ilk turdaki serbest metin cevabı üzerine yapılan araştırmadan sonra ikinci turda seçilen
  seçeneğin yazıya geçirilmiş halidir. Araştırma özeti 31.5 ve 31.6 soru metinlerindedir.
- Üretimdeki döviz kaynağı ve kapsamı (URT-D55: stok çıkışına hangi girişin kuru ve döviz maliyeti damgalanır; aynı
  kural satılan malın maliyetine ve stok değerlemesine de uygulanır mı) ve URN-D8 katsayı yuvarlama yönü bu belgede
  seçilmedi; `NEEDS-SINAN` olarak kalır. URT-D78 yön için şirket bazında ayar açmadığından "yuvarlama politikası
  kaynağının sahibi" sorusu üretim maliyeti için kalkmıştır.
- URT-D53'te yukarı yönlü (eksik tartılmış) düzeltmenin aynı onaylı yolla yapılıp yapılamayacağı bu belgede
  seçilmedi; `NEEDS-SINAN` olarak kalır.
- Satılan malın maliyetindeki yuvarlama farkının muhasebeleştirilmesi `OPEN-008` kapsamındadır ve bu belgeyle
  kapanmaz.
- Bu belge yeni rol açmaz ve hiçbir kişiye izin vermez; gerçek grant insan kapısındadır.
- `OPEN-*` kararları bu belgeyle kapanmaz.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
