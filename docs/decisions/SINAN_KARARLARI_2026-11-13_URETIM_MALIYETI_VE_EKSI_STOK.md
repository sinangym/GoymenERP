# Sinan Kararları — 2026-11-13 (Üretim maliyeti: hurdanın maliyetten düşülmesi, çıkan ≤ giren, yalnız kapalı emir ve üretimden iade; Stok: eksi stok görünmez, satışta kantar farkı kaydı)

Bu belge önceki karar dosyalarını silmez; **SAT-D316, SAT-D317, SAT-D318, SAT-D319, SAT-D320, SAT-D321, SAT-D322, SAT-D323, URT-D40, URT-D41 ve URT-D42**'yi ekler
(SAT-D319 ve URT-D41 aynı günün 2. tur, SAT-D320 3. tur, SAT-D321…SAT-D323 ve URT-D42 4.–8. tur cevaplarıdır; "2. tur", "3. tur" ve "4.–8. tur" bölümleri). SAT-D297 (EK59),
SAT-D301…SAT-D307 (`SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER.md`), SAT-D308
(`SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md`), URT-D5, URT-D6
(`SINAN_KARARLARI_2026-11-01_URETIM.md`) ve URT-D23 (`SINAN_KARARLARI_2026-11-02_URETIM_EK2.md`) geçerlidir; bu belge
onların dosyalarını değiştirmez. Dört yerde daha yeni kullanıcı kararı eski metnin önüne geçer ya da ona istisna
getirir; hepsi aşağıda yazılıdır: SAT-D316 ↔ SAT-D308 (1)–(2), SAT-D318 ↔ SAT-D301 (2), URT-D41 (2) ↔ URT-D6'nın artı
yönü (URT-D6'nın değeri ve eksik yönü geçerli kalır) ve URT-D41 (3) ↔ URT-D5 (Boyamada çıktı yeniden tartılmaz; URT-D5'in
fiilî tartım kuralına istisna olduğu **yazar okumasıdır**). Ayrıca SAT-D319 (8), SAT-D318 (2)'deki reddi satışta kantar
farkı hali için kaldırır. 4.–8. turda iki yer daha eklenir: URT-D42 (2) ↔ URT-D6'daki onayla kapanış yolunun artı yönü
ve SAT-D323 (1) ↔ SAT-D301 (2) ("giriş gelince düzelir" cümlesi aşıldı; 11 Kasım dosyası değiştirilmez). SAT-D319/SAT-D320'nin
kilitli `DEC-STK-009` ve `OPEN-004` (kantar/yükleme toleransı) ile ilişkisi 5. turda cevaplandı (SAT-D321 (3)): iki ayrı
kontroldür, ikisi de uygulanır; bu belge "yerine geçer" demez, bantlar ve `OPEN-004` değişmez. Sonraki turun cevabı önceki
turun aynı konudaki "yazar okuması" ve `NEEDS-SINAN` kaydının önüne geçer. `KARAR_SICILI` ve
`ACIK_KARARLAR` değişmez; hiçbir `OPEN-*` kapatılmaz (`OPEN-004` dahil).

**Kimlikler:** Maliyet zinciri EK59'dan beri SAT-D serisindedir (Stok için ayrı önek yoktur); üretim emrinin kütle
kuralı URT-D serisindedir. `origin/main` `63041c6c`'de (bu dalın çıkış noktası; sonraki `84dcb86b` ve `fb07312b` `docs/decisions` değiştirmez) en büyük kimlikler SAT-D315 ve URT-D39'dur
(`git grep -ohE "SAT-D[0-9]+|URT-D[0-9]+" origin/main -- docs/decisions`). Aynı gün `origin` üzerindeki bütün dalların
`docs/decisions` eklemeleri tarandı; SAT-D316+ veya URT-D40+ geçmiyordu (#1146 ve #1107 dalları karar dosyası
eklemiyor). Yeni önek icat edilmedi.

**Kaynak:** 13 Kasım 2026, Claude şeridi orkestratör oturumunda Sinan'ın sohbet cevabı. Sorular düz metin olarak soruldu,
cevaplar serbest metindir ve aşağıda **aynen** (yazım hataları dahil) alıntılanır. 1. turun Issue #66'da ayrı bir
kaydı yoktur (2. ve 3. turun vardır); 1. tur için tek kayıt bu belgedeki soru–cevap tablosudur. Sorular iki K3 PR'ının review
bulgularından doğdu: #1146 (üretim girişi maliyet hesaplayıcısı; fire-hariç formülünün ve iki kuralın karar kimliği
yok, kapanış toleransı ile maliyet reddi çelişiyor) ve #1107 (ortalama maliyette eksi stoktan düzelme; golden yok).

**2. tur kaynağı:** aynı gün, Claude şeridi inceleme oturumunda Sinan'ın seçenekli sorulara cevapları; kayıt Issue #66
yorumu [6849107070](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6849107070) (soru, seçilen ve
seçilmeyen seçenekler, serbest metin aynen). Sorular bu belgenin önceki head'indeki iki incelemenin bulgularından doğdu.
"(Önerilen)" işaretleri soruyu soranındır. Bu belgenin yazarı o cevapları ilgili oturumun soru kaydından doğruladı.
2. tur, 1. turdaki dört `NEEDS-SINAN` noktasından ikisini kapatır (URT-D6 artı yönü, Boyama) ve SAT-D318'i tamamlar (SAT-D319 (8) satışta kantar farkı hali için
SAT-D318 (2)'deki reddi kaldırır); 1. tur metninde bunlarla çelişen "yazar okuması" ifadeleri için **2. tur bölümü önce gelir**.

**Önceki kayıt (soru 1'in dayandığı):** Issue #66 yorumları
[6826603722](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6826603722) ve
[6826763903](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6826763903) (12 Kasım 2026, koordinatör
aracılığıyla sorulan (a)–(d) cevapları). Bu yorumlar bugüne kadar hiçbir karar dosyasına işlenmemişti. Soru 1 bu
yorumlardaki (a), (c) ve (d)'yi sordu; (b) sorulmadı (Sınırlar).

**3. tur kaynağı:** aynı gün, aynı inceleme oturumunda Sinan'ın dört seçenekli soruya cevabı; kayıt Issue #66 yorumu
[6849616256](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6849616256). Bu belgenin yazarı cevapları
ilgili oturumun soru kaydından doğruladı. 3. tur, 2. tur Sınırlar'ındaki dört `NEEDS-SINAN` noktasını cevaplar.

**4.–8. tur kaynağı:** aynı gün, aynı inceleme oturumunda Sinan'ın seçenekli sorulara cevapları; kayıtlar Issue #66
yorumları [6850620225](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6850620225) (4. tur),
[6850826600](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6850826600) (5. tur),
[6851081845](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6851081845) (6. tur),
[6851540840](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6851540840) (7. tur) ve
[6851616762](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6851616762) (8. tur). Bu turların tek kaynağı
o yorumlardır; bu belgenin yazarı cevapları yorumlardan aldı. Sorular bu belgenin önceki head'lerindeki incelemelerin
bulgularından ve önceki turların açık bıraktığı noktalardan doğdu.

## Soru ve cevaplar

"Okunuş" sütunu Sinan'ın sözü değildir; soruyu soranla birlikte varılan anlamdır.

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Okunuş |
|---|---|---|---|
| 1 | "Issue #66'da şu cevaplar yazılı [yorumlar 6826603722 / 6826763903] … Bunlar senin kararın mı? — Tartılan hurda × hurda fiyatı maliyetten düşülür, kalan mamule yüklenir. — Proses firesi (tartılmayan kayıp) sıfır değerlidir. — Hurda fiyatı dönemlik, elle girilir." | "1-evet, hurdalar aynı zamanda zatılıyor ama o zamana kadar maliyetlendirmek için elle fiyat girilebilir" | Üç kural Sinan'ın kararıdır. Ek: hurda ayrıca satılır; o zamana kadar maliyetlendirme için elle fiyat girilebilir. "O zamana kadar"ın "hurda satılana kadar" diye okunması **yazar okumasıdır**. "zatılıyor" yazım hatasıdır; "satılıyor" diye okunması **yazar okumasıdır** |
| 2 | "Çıktı kg, tüketilen kg'dan biraz fazla çıkarsa emir tolerans içinde kapanıyor, ama maliyet hesabı 'kütle dengesiz' diye reddediyor. Hangisi olsun? A) Tolerans içindeyse maliyet de hesaplansın. B) Maliyet hep reddetsin, emir düzeltilsin." | "2-üretimde çıkan girenden fazla olamaz, bu sorunun cevabı olabildi mi yoksa tekrar soracakmısın" | Sinan A/B harfi seçmedi; kuralı söyledi: çıkan girenden fazla olamaz. Buna göre maliyet hesabının çıktı > tüketim durumunu reddetmesi doğrudur (B yönü). Seçilmeyen: A (tolerans içindeyse maliyet de hesaplansın). Sinan'ın "bu sorunun cevabı olabildi mi yoksa tekrar soracakmısın" sorusu bu belge yazılırken **cevaplanmadı ve B yönü Sinan'a ayrıca teyit ettirilmedi**; B yönü yazar okumasıdır, açık kalan yanlar Sınırlar'da `NEEDS-SINAN` olarak durur |
| 3 | "Kodda var, karar kaydı yok. Onaylıyor musun? — Maliyet yalnız kapanmış emirler için hesaplanır. — Üretimden iade, bağlı olduğu çıkışın o günkü maliyetiyle değerlenir." | "3-evet olabilir sencede mantıklıysa" | İki kural onaylandı; onay "sence de mantıklıysa" şartına bağlıdır. Orkestratör değerlendirmesi (Sinan'ın sözü değil): iki kural da fail-closed ve iadeyi ilk hareketin maliyetiyle değerleyen SAT-D303 / SAT-D306 (1) ile aynı yönde; uygulanır |
| 4 | "Stok −1 kg iken 10 kg giriş (birim maliyet X) gelirse: yeni miktar 9 kg mı sayılacak, 10 kg mı? Ortalama maliyet X mi olacak, yoksa eksiye düşmeden önceki ortalama da hesaba katılacak mı?" | "4-stok -1 diye bir kavram olmaması lazım, erp nin hiçbir modülünde stok eksiye düşmemesi gerekli" | Sorunun öncülü reddedildi: eksi stok hiçbir modülde oluşmaz. Bu yüzden "eksi stoktan düzelme" hesabı gerekmez ve yazılmaz; 9 kg / 10 kg ve ortalama sorusu cevapsız değil, **konusuz**dur |

Aynı oturumdaki 5. soru süreç sorusudur; karar belgesine girmez.

## Kararlar

**Okuma notu (1. tur satırları):** aşağıdaki 1. tur satırlarının bazı cümleleri sonraki turlarda geri alındı; satırlar
tur kaydı olarak aynen bırakılmıştır. Geri alınanlar: URT-D40 (2)–(3)'teki "yazar okumasıdır" ve "Boyama kapsanmaz"
kayıtları → URT-D41 (3)–(4) · SAT-D318 kapsam sınırı ile (5)–(6)'daki "o hal için SAT-D301 (2) geçerlidir" → SAT-D323
(1)–(2) · SAT-D318 (2)'deki ret, satışta kantar farkı hali için → SAT-D319 (8). Kod şeridi bu satırları sonraki tur
kararlarıyla birlikte okur; çelişkide sonraki tur geçerlidir.

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D316 | Fire anahtarı kapalıyken hurdanın maliyetten düşülmesi (soru 1; #66 6826603722 (a), 6826763903 (c)–(d)) | (1) **Tartılan hurda × hurda fiyatı maliyetten düşülür; kalan mamule yüklenir.** (2) **Proses firesi (tartılmayan kayıp) sıfır değerlidir**; maliyeti mamulde kalır (kayıttaki ifade "sağlam ürüne kalır"dır; 2. kaliteyi de kapsayan "mamul" ifadesi SAT-D307 (2) ile hizalı **yazar okumasıdır**). (3) **Hurda fiyatı dönemlik, elle girilir.** Bu belge formülün payını ve paydasını yazmaz: paydanın anahtar kapalıyken de SAT-D307'deki "sağlam kg + 2. kalite kg" olduğu #1146 kodundaki okumadır, Sinan'ın sözü değildir; K3 review'u SAT-D308 uygulama notundaki golden şartıyla doğrular, golden gelmeden formül kilitli sayılmaz. (4) Sinan'ın eki: **hurda ayrıca satılır**; "o zamana kadar" maliyetlendirme için **elle fiyat girilebilir**. "O zamana kadar"ın "hurda satılana kadar" olduğu **yazar okumasıdır**. (5) SAT-D308 ile ilişki: bu madde SAT-D305 (2) fire anahtarı **kapalıyken** geçerlidir; anahtar açıkken SAT-D307 (1)–(2) değişmez. SAT-D308 (1) "fire maliyeti ürün maliyetine yüklenmez" ve (2) "ayrı fire/dönem gideri olarak raporlanır" cümlelerinin **tutarını** bu madde belirler: ürüne yüklenmeyen ve ayrı raporlanan tutar tartılan hurda × hurda fiyatıdır, firenin bunu aşan maliyeti mamule yüklenir. Daha yeni kullanıcı kararı olduğu için (1)–(2), SAT-D308 (1)–(2)'nin **kısmen yerine geçer** (SAT-D308 (1) "fire maliyeti ürün maliyetine yüklenmez" diyordu; anahtar kapalıyken firenin hurda değerini aşan maliyeti artık ürüne **yüklenir**); SAT-D308 (3)–(6) aynen geçerlidir (gider kaydı hesap eşlemesi gelmeden etkinleşmez). İlişkinin bu ifadesi **yazar okumasıdır**. (6) 6826763903 (d) seçenek metnindeki ayrıntılar — fiyatı yetkili biri her ay için TL/kg girer, geçmiş dönem değişmez, fiyat girilmemişse hesap tahmin yapmaz ve "okunamadı" der — Sinan'ın bugün "evet" dediği kaydın parçasıdır; bugünkü soruda tek tek sayılmadıkları için "evet"in bunları da kapsadığı **yazar okumasıdır**. Fiyat yokken "okunamadı" ayrıca fail-closed kuralın gereğidir (tahminle para üretilmez). | `ProductionReceiptCostCalculatorV1` (#1146), Üretim maliyet portu, hurda fiyatı girişi (ayrı dilim), fire raporu |
| URT-D40 | Üretimde çıkan girenden fazla olamaz (soru 2) | (1) **Üretimde çıkan, girenden fazla olamaz.** (2) Bu yüzden maliyet hesabının **çıktı > tüketim** durumunu reddetmesi (`mass_unbalanced`, değer üretmez) doğrudur; fark tolerans içinde olsa da değer üretilmez. Sinan A/B harfi seçmedi ve kendi sorusu ("cevabı olabildi mi") cevaplanmadı; (1)'den çıkarılan bu sonuç (soru 2'deki B yönü) **yazar okumasıdır**, Sinan'ın kilitlediği kural yalnız (1)'dir. Yön fail-closed'dır (değer üretmez); kapsam sınırları ve URT-D6 artı yönüyle çelişki Sınırlar'da `NEEDS-SINAN`'dır. Emrin kapanışta nasıl davranacağı bu belgede karara bağlanmadı (bkz. Sınırlar). (3) "Çıkan" = sağlam + 2. kalite + tartılan hurda + beyan edilen proses kaybı, "giren" = tüketilen kg (çıkış − iade) eşlemesi #1146'daki hesabın tanımıdır ve **yazar okumasıdır**; DEC-PRDCT-006'nın ayrı saydığı kalemler (geri kazanılabilir artık, WIP, ölçüm düzeltmesi) bu tanımda yoktur ve girdisi kg olmayan süreçler (Boyama) kapsanmaz (Sınırlar). (4) URT-D6'nın tolerans **değeri** (hat başına ±%2) ve URT-D23 değişmez; bu madde yeni eşik seçmez. | `ProductionReceiptCostCalculatorV1` (#1146); emir kapanışı için bkz. Sınırlar (izlenecek dilim) |
| SAT-D317 | Maliyetin hesaplanacağı an ve üretimden iade (soru 3) | (1) **Üretim girişi maliyeti yalnız kapanmış emirler için hesaplanır.** Kapanmamış emirde değer üretilmez (fail-closed). (2) **Üretimden iade, bağlı olduğu çıkışın o günkü maliyetiyle değerlenir**; hareketli ortalamanın iade anındaki değeri kullanılmaz. Sinan'ın onayı şartlıydı ("sencede mantıklıysa"); şartın sağlandığı değerlendirmesi orkestratöründür, Sinan'ın sözü değildir: (1) fail-closed'dır, (2) iadeyi ilk hareketin maliyetiyle değerleyen SAT-D303 ve SAT-D306 (1) ile aynı yöndedir. (3) "O günkü maliyet"in, çıkış hareketine çıkış anında yazılan birim maliyet olduğu ve bağlı çıkışı ya da okunur maliyeti olmayan iadede sonucun "okunamadı" olduğu **yazar okumasıdır** (geri alınabilir); değeri Stok verir, Üretim sürümlü portla okur (cross-module, K3). (4) SAT-D303 (müşteri iadesi) ve SAT-D306 (alış iadesi) değişmez; bu madde yalnız üretime verilen girdinin iadesini kapsar. | `ProductionReceiptCostCalculatorV1` (#1146), Üretim maliyet portu, Stok çıkış maliyeti |
| SAT-D318 | Hiçbir modülde eksi stok yok (soru 4) | (1) **ERP'nin hiçbir modülünde stok eksiye düşmez**; "stok −1" diye bir durum yoktur. Bu madde **fiziksel** stok (eldeki miktar) içindir; projeksiyon/ATP ve serbest stok hesapları (Master INV-009) ayrı kavramlardır ve bu maddeyle değişmez (**yazar okuması**). **Kapsam sınırı:** aşağıdaki (3)–(6) yalnız **fiziksel stokun eksiye düşmesi** halini kapsar; fiziksel stok eksiye düşmediği halde maliyet pozisyonunun `NegativeStock` okunduğu hali kapsamaz (Sınırlar, `NEEDS-SINAN`); o hal için SAT-D301 (2) olduğu gibi geçerlidir. (2) Stoğu eksiye götürecek hareket **reddedilir** (mevcut `StockLedger` davranışı: `NegativePhysicalStock`); bu davranış artık Sinan kararına bağlıdır. Sinan "eksiye düşmez" dedi, "ret" demedi; yolun ret olduğu **yazar okumasıdır** ve satışta kantar farkı halinde geçerli değildir (bkz. SAT-D319 (8)). (3) Bu yüzden **fiziksel eksi stoktan** "düzelme" hesabı ve golden'ı gerekmez; formül olarak yazılmaz. (4) Ortalama maliyet hesabında fiziksel eksi stok durumu bir **değişmez ihlalidir** ve **fail-closed** kalır: SAT-D301 (1) aynen geçerlidir (maliyet "hesaplanamadı", değer uydurulmaz, sıfır sayılmaz). İhlal durumundaki pozisyonun bir girişle kendiliğinden açılmaması Sinan'ın bugünkü sözü değildir, **yazar okumasıdır**: Sinan bugün yalnız eksi stokun oluşmamasını söyledi; 11 Kasım'daki "giriş gelince düzelir" sözü bu durum için uygulanmamış olur. Yön güvenlidir (değer üretilmez); Sinan farklı isterse ayrı karar gerekir (Sınırlar). (5) **Yerine geçtiği cümle:** SAT-D301 (2)'nin ikinci cümlesi — "Düzelmenin hesap biçimi (hangi girişten sonra, hangi ortalamayla yeniden başlanacağı) Sinan'ın metninde yazmaz; **uygulama okunuşu (geri alınabilir)** kod PR'ında yazılır ve review edilir." — ve aynı dosyanın "Uygulama notu"ndaki "Düzelme hesabı uygulama okunuşudur ve kod PR'ında yazılır … Formül niteliğindeki bu okuma için golden örnek ChatGPT şeridinden ya da gerekirse `NEEDS-SINAN` ile gelir; K3 review'u golden'la doğrular" cümleleri **fiziksel eksi stok hali için** artık uygulanmaz: o hal için düzelme hesabı kod PR'ında yazılmaz, golden beklenmez; kapsam sınırındaki hal için bu cümleler geçerliliğini korur. Aynı nedenle o dosyanın uygulama notundaki "eksi stok nedeni yapışkan kalamaz" cümlesi ve dilim tablosundaki "Eksi stok nedeninin yapışkan olmaması, düzelme hesabı ve testleri" satırı da yalnız fiziksel eksi stok hali için uygulanmaz. (6) SAT-D301 (2)'nin ilk cümlesi ("Bu durum **kalıcı değildir**: stok bir girişle düzelince maliyet yeniden okunur") fiziksel eksi stok oluşmadığı için o hal bakımından **uygulama alanı kalmayan** cümledir; kapsam sınırındaki hal için aynen geçerlidir; ihlal durumundaki bir pozisyonun bir girişle kendiliğinden açılmaması (fail-closed kalması) bu maddenin (4)'üdür. (5)–(6)'daki cümle eşlemesi **yazar okumasıdır**. (7) SAT-D306 (c)–(d)'deki "eksiye düşerse" halleri de aynı nedenle oluşmaz; metinleri değişmez (**yazar okuması**). | `StockLedger`, `AverageCostCalculator` / `AverageCostUnreadableReason.NegativeStock` (#1107), stok hareketi üreten bütün modüller |

## 2. tur — soru ve cevaplar (Issue #66 yorumu 6849107070)

Tablo o yorumdaki kaydın özetidir (cevap etiketleri aynen; numaralar ve soru/açıklama metinleri kısaltılmıştır, asıl metin yorumdadır); "Okunuş" sütunu Sinan'ın sözü değildir. 8. soru (#1149 Faz 0 (c)) süreç sorusudur
ve bu belgeye girmez; kaydı aynı yorumdadır.

| # | Soru (özet) | Cevap (Sinan, aynen) | Seçilmeyenler | Okunuş |
|---|---|---|---|---|
| 2.1 | Üretimde tartı farkıyla çıkan kg, giren kg'dan ±%2 tolerans içinde fazla çıkarsa ne olsun? | "Emir kapanmasın" — seçenek metni: çıkan girenden fazlaysa emir kapanmaz, tartım düzeltilir; maliyet de hesaplanmaz; ±%2 tolerans yalnız eksik yönde (fire/kayıp) geçerli olur | Tolerans içinde kabul (emir kapanır, maliyet hesaplanır) · Kapansın, maliyet beklesin (bugünkü davranış) | 1. turdaki soru 2'nin açık kalan yanını kapatır; URT-D40 (2)'deki B yönü artık Sinan kararıdır |
| 2.2 | Boyamada boyalı ürün tartılırsa çıkan kg giren çelik kg'ından fazla olabilir; kural boyamada nasıl uygulansın? | "Boyamada kg değişmez" — seçenek metni: boyalı ürün yeniden tartılmaz; çıkan kg, giren çelik kg'ı kabul edilir; boya ağırlığı stok kg'ına yansımaz | Boya da giren sayılsın · Boyama kuraldan muaf · Şimdilik açık kalsın | Boyama URT-D40 (1)'den muaf değildir; çıkan kg tanımı gereği giren çelik kg'ına eşittir |
| 2.3 | Veri hatasıyla eksi stok kaydı oluşmuşsa maliyet nasıl davransın? (11 Kasım: "giriş gelince düzelir") | Serbest metin: "Üretimde eksiye düşemez sadece alınan malzeme kantar farklarından dolayı satılırken eksiye düşebilir, birde malzeme iade veya rezerv(tahsis), rezerv iptallerinde stok miktarları doğru şekilde korunsun iade veya tahsis süreçlerinden kaynaklı eksiye düşmesin , bu sorunun cevabı oldu mu bilmiyorum" | Seçenek seçilmedi (Elle düzeltilene kadar kapalı · Giriş gelince düzelsin · Şimdilik açık kalsın) | Sorulan soru (ihlal kaydında maliyet) cevaplanmadı. Sinan eksi riskinin nereden doğabileceğini söyledi: yalnız satışta kantar farkı; üretim, iade, tahsis ve tahsis iptali stoğu eksiye düşürmez |
| 2.4 | Satışta kantar farkı yüzünden satılan kg sistemdeki stoktan fazla çıkarsa (ör. sistemde 1.000 kg, kantarda 1.004 kg) sistem ne yapsın? | "Kantar farkı kaydıyla tamamla" — seçenek metni: satış geçer; eksik kalan kg ayrı bir "kantar farkı" giriş kaydıyla tamamlanır, stok 0'da kalır ve hiç eksi görünmez; fark raporda ayrı izlenir | Stok eksi görünsün (maliyet hesaplanamadı, giriş gelince düzelir) · Satış reddedilsin (bugünkü davranış) | 2.3'ün devamı: tek eksi riski de eksi stok üretmez |
| 2.5 | Kantar farkı kaydı en fazla ne kadarlık farkı kendiliğinden tamamlasın? (Ayarlar'dan değiştirilebilir başlangıç değeri) | "Satılan kg'ın %0,5'i" | %1 · %2 · sabit kg sınırı | Değer soruyu soranın önerisiydi; Sinan seçti. Başlangıç değeridir, Ayarlar'dan değişir |
| 2.6 | Kantar farkı bu sınırı aşarsa satış ne olsun? | "Yetkili onayıyla geçsin" — seçenek metni: satış bekler; yetkili onaylarsa fark kaydıyla tamamlanır ve onay kayda geçer. Kimin onaylayacağı (rol) sorulmadı | Reddedilsin, önce sayım | Onaylayan rol 3. turda cevaplandı (3.1); grant insan kapısıdır |
| 2.7 | Kantar farkı kaydı maliyette nasıl değerlensin? | "Mevcut ortalama maliyetle" — seçenek metni: tamamlanan kg ürünün o anki ortalama maliyetiyle girer; ortalama değişmez | Sıfır maliyetle · Şimdilik açık kalsın | — |

## 2. tur — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URT-D41 | Çıkan > giren: emir kapanışı; Boyama (2.1, 2.2) | (1) **Çıkan kg giren kg'dan fazlaysa emir kapanmaz; tartım düzeltilir; maliyet de hesaplanmaz.** Fark ±%2 içinde olsa da böyledir. (2) **URT-D6'nın ±%2 toleransı yalnız eksik yönde (fire/kayıp) geçerlidir**; daha yeni kullanıcı kararı olarak URT-D6'nın artı yönünün yerine geçer. URT-D6'nın değeri (hat başına %2), eksik yöndeki davranışı ve URT-D23 değişmez. (3) **Boyamada kg değişmez:** boyalı ürün yeniden tartılmaz; çıkan kg, giren çelik kg'ı kabul edilir; boya ağırlığı stok kg'ına yansımaz. Böylece Boyama URT-D40 (1)'i tanım gereği sağlar; boya tüketimi litreyle izlenmeye devam eder (Master PNT-006, URT-D9 değişmez). (4) URT-D40 (2) bu maddeyle Sinan kararına dayanır; oradaki "yazar okumasıdır" kaydı (1) için geçerliliğini yitirir. (5) Sinan'ın cevabında olmayanlar **yazar okumasıdır** ya da açıktır (Sınırlar): reddin hata kodu ve ekran metni kod diliminin işidir; bugüne kadar artı yönde tolerans içinde kapanmış emirlere ne olacağı sorulmadı. Boyama dışındaki, girdisi kg olmayan süreçler ve DEC-PRDCT-006 kalemleri bu maddeyle cevaplanmadı. | `OrderClosureAssessment` (bugün `Math.Abs` ile artı yönü tolerans içinde kapatıyor; ayrı Üretim dilimi, K3), `ProductionReceiptCostCalculatorV1` (#1146), Boyama emri çıktı kaydı |
| SAT-D319 | Eksi stokun tek kaynağı ve satışta kantar farkı kaydı (2.3–2.7) | (1) **Üretimde stok eksiye düşemez.** (2) **İade, tahsis (rezerv) ve tahsis iptali süreçlerinde stok miktarları doğru korunur; bu süreçler stoğu eksiye düşürmez.** (3) Eksi riski **yalnız satışta, alınan malzemenin kantar farkından** doğar. (4) O durumda **satış geçer; eksik kalan kg ayrı bir "kantar farkı" giriş kaydıyla tamamlanır; stok 0'da kalır ve hiç eksi görünmez; fark raporda ayrı izlenir.** (5) Kendiliğinden tamamlanan fark sınırı: **satılan kg'ın %0,5'i** (Ayarlar'dan değiştirilebilir başlangıç değeri; kodda sabit yazılmaz). (6) Sınır aşılırsa **satış bekler; yetkili onaylarsa fark kaydıyla tamamlanır ve onay kayda geçer.** Onaylayan rol 2. turda sorulmadı; 3. turda cevaplandı (SAT-D320 (1)). İzin adı kod diliminin işidir, grant/rol insan kapısıdır. (7) **Kantar farkı kaydı ürünün o anki ortalama maliyetiyle girer; ortalama değişmez.** (8) SAT-D318 ile ilişki: SAT-D318 (1) ("stok eksiye düşmez") aynen durur ve bu maddeyle sağlanır. SAT-D318 (2)'deki "eksiye götürecek hareket reddedilir" cümlesi **satışta kantar farkı hali için** bu maddenin (4)–(6)'sına yerini bırakır; diğer bütün hareketlerde ret sürer (**yazar okuması**). DEC-SAL-006'daki "kantar farkı ayrıdır" kaydıyla aynı yöndedir. (9) Kantar farkı kaydı, satış hareketinin kopyası değildir; ayrı nedenli, ayrı izlenen bir stok giriş kaydıdır ("olay bir kez kaydedilir" değişmezi korunur) — **yazar okuması**, K3 review'u doğrular. (10) Sinan 2.3'te sorulan soruya (ihlal edilmiş kayıtta maliyet; 11 Kasım "giriş gelince düzelir" cümlesinin akıbeti) cevap vermedi; SAT-D318 (4)–(6) ve Sınırlar'daki ilgili `NEEDS-SINAN` aynen durur. | `StockLedger` (bugün bu satışı `NegativePhysicalStock` ile reddediyor), Satış sevk/kantar akışı, Ayarlar (sınır değeri), onay akışı, ortalama maliyet (#1107), kantar farkı raporu — yeni kod dilimi, K3 (stok/para/yetki) |

### 2. tur — sınırlar

- Kod değişmez; kantar farkı kaydı ve emir kapanışı düzeltmesi ayrı K3 kod dilimleridir. Hiçbir `OPEN-*` kapatılmaz.
- **3. turda cevaplandı (SAT-D320):** sınırı aşan satışı kimin onaylayacağı · %0,5'in neye göre ölçüleceği · ters
  yöndeki kantar farkı · kantar farkı kaydının muhasebede işleneceği yer.
- 2. turda açık kalıp **4.–8. turda cevaplananlar:** stok yeterliyken doğan kantar fazlası → kayıt açılmaz (5.3,
  SAT-D322 (1)) · ortalama maliyeti okunamayan üründe kantar farkı kaydının değeri → 6.7, 7.2, 8.1 (SAT-D323 (3)–(5)) ·
  bugüne kadar artı yönde kapanmış üretim emirleri → 6.6 (URT-D42 (3)) · 11 Kasım "giriş gelince düzelir" cümlesi →
  4.14 (SAT-D323 (1)).
- Hâlâ karara bağlanmayanlar (`NEEDS-SINAN`; tahminle seçilmez): Boyama dışındaki girdisi kg olmayan süreçler ·
  DEC-PRDCT-006 kalemleri · ihlal edilmiş eksi stok kaydında emniyet davranışı (4.–8. tur Sınırlar).
- **5. turda cevaplandı (5.1, SAT-D321 (3): iki ayrı kontrol, ikisi de uygulanır; aşağıdaki paragraf 2. tur kaydıdır):** ~~`NEEDS-SINAN`~~ — **`DEC-STK-009` / `OPEN-004` ile ilişki.** `DEC-STK-009` (kilitli):
  kantar/yükleme toleransı ürün ve işlem profiline göre ayarlanır. `OPEN-004` (kısmen): başlangıç varsayımı uyarı %1,
  onay %2, blok %3. SAT-D319 (5)–(6) ve SAT-D320 (2) ise tek oran (%0,5), irsaliye toplamında ölçüm ve üstünde Satış
  Müdürü onayı der; blok bandı anmaz. Bunların aynı ölçü mü (yeni kural eskisinin yerine mi geçer), yoksa iki ayrı
  kontrol mü olduğu (kantar ↔ sistem stoğu farkının kaydı ayrı, yükleme toleransı ayrı) Sinan'a sorulur. Örnek:
  sistemde 1.000 kg, kantarda 1.035 kg (%3,5) `OPEN-004` varsayımıyla blok, SAT-D319 (6) ile onayla geçer; 1.008 kg
  (%0,8) `OPEN-004`'e göre uyarı bile değil, SAT-D319'a göre onay bekler. Cevap gelene kadar bu belge `DEC-STK-009`'u
  ve `OPEN-004`'ü değiştirmez, kapatmaz ve "yerine geçer" demez; kantar farkı kod dilimi bu cevap olmadan iki kuralı
  birleştiren bir davranış etkinleştirmez.
- **5. turda kısmen cevaplandı (5.3, SAT-D322 (1)–(2): örnekteki satış geçer, eksik sonraki satışta görülür; `DEC-STK-004` ile ilişkinin biçimi 4.–8. tur Sınırlar'ında `NEEDS-SINAN` kalır):** **SAT-D319 (2)'nin kapsadığı miktar.** Tahsis fiziksel hareket değildir (`DEC-STK-003`); "stok
  miktarları doğru korunur" cümlesinin tahsisli/serbest miktarı da kapsayıp kapsamadığı (ör. fiziksel 1.000 kg, 600 +
  400 tahsis, ilk satışın kantarı 603: fiziksel eksi yok ama serbest −3) sorulmadı. SAT-D318 (1)'deki "serbest stok
  hesapları bu maddeyle değişmez" yazar okuması ve `DEC-STK-004` ("serbest miktar aşılmaz") olduğu gibi durur.
- **5. ve 6. turda cevaplandı (5.2 → rulo bazında, URT-D42 (1); 6.5 → artı yönde onayla da kapanmaz, URT-D42 (2); aşağıdaki "yazar okuması" artık Sinan kararıdır):** ~~`NEEDS-SINAN`~~ — **URT-D41 (1)'in ölçü birimi:** kuralın rulo başına mı emir toplamında mı uygulanacağı Sinan'ın
  sözünde yoktur (bugünkü kod rulo başına değerlendirir). **Yazar okuması:** seçenek metni genel olduğundan ("çıkan
  girenden fazlaysa emir kapanmaz") URT-D6'daki "tolerans dışı fark Üretim müdürü onayıyla kapanır" yolu artı yönde de
  kalkar; onayla kapanış yalnız eksik yönde sürer.
- Not (URT-D41 (3), Boyama; Sinan'ın sözü değildir): Boyama hattında hurda çıkarsa "çıkan kg = giren çelik kg"
  cümlesinin sağlam çıktı için mi toplam için mi olduğu 2. turda sorulmamıştı; **6. turda cevaplandı** (6.3 → sağlam +
  hurda = giren, URT-D42 (4)). Boya ağırlığı stok kg'ına yansımadığı için boyalı ürün satışında kantar farkı artı yönde
  çıkabilir; büyüklüğü ölçülmedi. **6. turda cevaplandı** (6.4 → kural aynı kalır, veri görülünce yeniden karar;
  SAT-D321 (5)).
- Araştırma notu (inceleme oturumu; Sinan'ın sözü değildir): 1. tur Sınırlar'daki "fiziksel eksi olmadan maliyet
  pozisyonunun `NegativeStock` okunması" senaryosu `origin/main`'deki `AverageCostCalculator.Apply` ile doğrulanamadı;
  defter stoğu ≠ maliyet stoğu durumu önce `LedgerStockMismatch` ile duruyor. O `NEEDS-SINAN` satırı bu notla birlikte
  okunur; #1107 kod review'u doğrular.
- İnsan kapıları değişmez.

## 3. tur — soru ve cevaplar (Issue #66 yorumu 6849616256)

Tablo o yorumdaki kaydın özetidir (cevap etiketleri aynen; numaralar ve soru/açıklama metinleri kısaltılmıştır, asıl metin yorumdadır); "(Önerilen)" işaretleri soruyu soranındır; "Okunuş" sütunu Sinan'ın sözü değildir.

| # | Soru (özet) | Cevap (Sinan, aynen) | Seçilmeyenler | Okunuş |
|---|---|---|---|---|
| 3.1 | Satışta kantar farkı %0,5 sınırını aşınca satışı kim onaylasın? | "Satış Müdürü" — seçenek metni: satışı giren kişi dışında Satış Müdürü onaylar; satışı giren kendi farkını onaylayamaz | Muhasebe Müdürü · İkisi birlikte · Şimdilik açık kalsın | 2.6'nın açık kalan rolünü kapatır. "Kendi farkını onaylayamaz" kısmı seçenek metnindeydi (dayanak: 21 Ekim EK-14, sayım farkında açan ≠ onaylayan) |
| 3.2 | %0,5 sınırı neye göre ölçülsün? (Örnek: irsaliyede 3 ürün; birinde fark %0,8, toplamda %0,3) | "İrsaliye toplamına göre" — seçenek metni: tüm irsaliyenin toplam farkı toplam kg'a oranlanır; örnekte %0,3 olduğu için kendiliğinden geçer; tek üründeki büyük fark gizlenebilir | Her ürün satırına göre (önerilen buydu; Sinan bunu seçmedi) | Örnekteki %0,8'lik satır onaya düşmez |
| 3.3 | Ters durum: kantarda satılan kg sistemdekinden az çıkarsa (sistemde 1.000 kg, kantarda 996 kg, depo boşaldı) kalan 4 kg ne olsun? | "Stokta kalsın, sayımda düzelsin" — seçenek metni: satış kantar kg'ı ile geçer; 4 kg sistemde stok olarak kalır ve sayım farkı kuralıyla (sebep + başka kişinin onayı) düzeltilir; yeni otomatik kayıt gerekmez | Kantar farkı kaydıyla silinsin · Şimdilik açık kalsın | Yeni kural getirmez; mevcut sayım farkı kuralına (DEC-STK-007, EK-14) bağlanır |
| 3.4 | Kantar farkı kaydı muhasebede nereye işlensin? | "Ayrı "kantar farkı" hesabı" — seçenek metni: farkın tutarı ayrı bir hesapta birikir; hesap kodu Ayarlar'dan seçilir | Sayım farkı hesabıyla aynı · Şimdilik açık kalsın | Hesap kodu seçilmedi |

## 3. tur — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D320 | Satışta kantar farkı kaydı: onaylayan, sınırın ölçüsü, ters yön, muhasebe (3.1–3.4; SAT-D319 (5)–(7)'yi tamamlar) | (1) Kantar farkı sınırı aşılınca satışı **Satış Müdürü** onaylar; **satışı giren kişi kendi farkını onaylayamaz**. Bu bir rol kararıdır; izin adının kaydı ve role grant'i ayrı insan kapısıdır, bu madde kendi başına grant değildir. (2) **%0,5 sınırı irsaliye toplamına göre ölçülür:** tüm irsaliyenin toplam farkı toplam kg'a oranlanır; toplam sınırın içindeyse tek üründeki daha büyük fark da kendiliğinden geçer (Sinan önerilen "her ürün satırına göre"yi seçmedi). Oran SAT-D319 (5)'teki Ayarlar değeridir. (3) **Ters yön (kantar kg'ı < sistemdeki kg):** satış kantar kg'ı ile geçer; kalan kg **stokta kalır** ve **sayım farkı kuralıyla** (sebep + başka kişinin onayı; DEC-STK-007, 21 Ekim EK-14) düzeltilir; **yeni otomatik kayıt oluşmaz**. (4) **Muhasebe:** kantar farkının tutarı **ayrı bir "kantar farkı" hesabında** birikir; **hesap kodu Ayarlar'dan seçilir** (kodda sabit yazılmaz; bu belge kod seçmez). (5) Sınır irsaliye toplamında ölçülse de stok ürün bazında tutulduğu için tamamlama kaydının eksik kalan **her ürün için ayrı** oluşması **yazar okumasıdır** (Sinan'a sorulmadı; SAT-D318 (1) başka türlü sağlanamaz) | Sales (irsaliye/satış onayı), Inventory (kantar farkı girişi, sayım), Finance (hesap eşlemesi; Finance STOP sürer), Ayarlar |

### 3. tur — sınırlar

- Kod, izin, grant, Ayarlar verisi ve hesap planı değişmez. (1) rol atamaz; (4) Finance STOP'u kaldırmaz ve hesap kodu
  seçmez. Hiçbir `OPEN-*` kapatılmaz; insan kapıları değişmez.
- 3. turda açık kalıp **4.–8. turda cevaplananlar:** satışı giren Satış Müdürü'nün kendisiyse kimin onaylayacağı →
  4.13 (SAT-D321 (1)) · aynı ürün birden çok etiketten/stok yerinden satıldıysa farkın yazılacağı kalem → 6.2, 7.1
  (SAT-D322 (3)–(4)) · oranın paydası → 6.1 (SAT-D321 (4)) · "satışı giren kişi" → 5.4 (SAT-D321 (2)) ·
  `DEC-STK-009`/`OPEN-004` ilişkisi → 5.1 (SAT-D321 (3)).
- Hâlâ karara bağlanmayanlar (`NEEDS-SINAN`; tahminle seçilmez): irsaliyede "toplam fark"ın nasıl toplanacağı (bir
  üründe fazla, başka üründe eksik çıkarsa farklar birbirini götürür mü) · irsaliye toplamı sınırın içindeyken tek
  üründeki büyük fark için ayrı bir üst sınır olup olmadığı · "kantar farkı" hesabının kodu · 2. tur Sınırlar'ında
  hâlâ açık kalanlar (yukarıda).

## 4.–8. tur — soru ve cevaplar (Issue #66 yorumları 6850620225, 6850826600, 6851081845, 6851540840, 6851616762)

Tablo o yorumlardaki kayıtların özetidir (cevap etiketleri aynen; soru/seçenek metinleri kısaltılmıştır, asıl metin yorumlardadır); cevap sütunu Sinan'ın seçtiği seçeneğin etiketidir (aynen). "(Önerilen)"
işaretleri soruyu soranındır ve kayıtta nasıl geçiyorsa öyle yazılmıştır. Soruyu soranın notları karar değildir.

| # | Soru | Cevap (Sinan, aynen) | Seçilen seçeneğin açıklaması | Seçilmeyenler |
|---|---|---|---|---|
| 4.13 | Kantar farkı %0,5 sınırını aşan satışı giren kişi Satış Müdürü'nün kendisiyse (kendi farkını onaylayamaz) onayı kim versin? | "Başka bir Satış Müdürü" | Aynı role sahip ikinci bir kişi onaylar; şirkette tek Satış Müdürü varsa satış onaysız bekler | Muhasebe Müdürü (önerilen buydu; Sinan bunu seçmedi) · Genel Müdür / siz · Şimdilik açık kalsın |
| 4.14 | 11 Kasım kararındaki "eksi stokta maliyet hesaplanmaz, giriş gelince düzelir" cümlesi ne olsun? | "Geçersiz sayılsın" | Cümle 13 Kasım kararlarıyla aşılmış diye işaretlenir; eksi stok oluşamayacağı için uygulanacağı durum kalmıyor | Emniyet kuralı olarak kalsın · Kalsın ama elle düzeltilsin |
| 5.1 | Satışta kantar farkı için seçilen %0,5 sınırı ile eski yükleme toleransı bantları (uyarı %1, onay %2, blok %3) birlikte nasıl çalışsın? Örnek: sistemde 1.000 kg, kantarda 1.035 kg (%3,5). | "İkisi de uygulansın (Önerilen)" | İki ayrı kontrol. %0,5 üstü fark Satış Müdürü onayı ister; %3 üstü yine bloklanır. Örnekteki 1.035 kg geçmez. Eski bantlar ve OPEN-004 değişmez | Yenisi eskisinin yerine geçsin · Şimdilik açık kalsın |
| 5.2 | Üretimde "çıkan kg girenden fazlaysa emir kapanmaz" kuralı neye göre ölçülsün? Örnek: 1. rulo giren 1.000 çıkan 1.005, 2. rulo giren 1.000 çıkan 990; emir toplamında çıkan girenden az. | "Rulo bazında (Önerilen)" | Her giren rulo ayrı bakılır. Örnekte 1. rulo yüzünden emir kapanmaz, tartım hatası hangi ruloda ise orada düzeltilir. Bugünkü kod da rulo başına değerlendiriyor | Emir toplamına göre |
| 5.3 | Bir satışın kantarı fazla çıkınca fiziksel stok eksiye düşmüyor ama başka siparişe ayrılmış (tahsisli) miktar karşılanamıyorsa ne olsun? Örnek: stok 1.000 kg, A siparişine 600, B siparişine 400 ayrılmış; A'nın kantarı 603 çıktı, B için 397 kg kaldı. | "B satışında ortaya çıksın" | A satışı 603 kg ile geçer, kayıt açılmaz. Eksik, B satılırken görülür ve o zaman kantar farkı kuralına girer. B'nin müşterisi beklenmedik eksikle karşılaşabilir | Kantar farkı kaydı açılsın (Önerilen; Sinan bunu seçmedi) · A'nın fazlası reddedilsin |
| 5.4 | "Satışı giren kendi kantar farkını onaylayamaz" kuralında satışı giren kişi kim sayılsın? | "Kantar/sevk kaydını giren (Önerilen)" | Fark bu kayıtta doğduğu için onaylayamayacak kişi kantar veya sevk kaydını girendir. Siparişi açan Satış Müdürü onaylayabilir | Siparişi açan · İkisi de onaylayamaz |
| 6.1 | Satışta %0,5 kantar farkı sınırı hangi kg'a göre hesaplansın? Örnek: sistemde 1.000 kg, kantarda 1.005,02 kg; fark 5,02 kg. | "Sistemdeki kg'a göre (Önerilen)" | 5,02 / 1.000 = %0,502, onaya düşer. Daha önce seçilen "satılan kg'ın %0,5'i" ifadesiyle aynı yönde ve biraz daha sıkı | Kantardaki kg'a göre |
| 6.2 | Aynı ürün bir satışta birden çok etiketten/stok yerinden çıkıyorsa kantar farkı hangisine yazılsın? Örnek: iki etiketten 500 + 500 kg satıldı, kantar 1.004 kg çıktı; 4 kg fark var. | "En büyük kaleme yazılsın" | Farkın tamamı en çok kg satılan etikete yazılır. Tek kayıt olur ama o etiketin kg'ı gerçeğinden fazla sapabilir | Kg oranında dağıtılsın (Önerilen; Sinan bunu seçmedi) · Kullanıcı seçsin |
| 6.3 | Boyamada "kg değişmez" denmişti. Boyama hattında hurda çıkarsa çıkan kg nasıl yazılsın? Örnek: giren 1.000 kg çelik, 20 kg hurda çıktı. | "Sağlam + hurda = giren (Önerilen)" | Sağlam çıktı 980 kg, hurda 20 kg yazılır; toplam giren 1.000 kg'a eşit kalır. "Çıkan girenden fazla olamaz" kuralı bozulmaz | Sağlam = giren, hurda ayrıca |
| 6.4 | Boya ağırlığı stok kg'ına yansımadığı için boyalı ürün satışında kantar hep biraz fazla çıkacak. Bu fark %0,5 sınırını sürekli aşarsa ne olsun? (Soruda geçen "%1 civarı" soruyu soranın ölçülmemiş kaba tahminidir; veri değildir.) | "Kural aynı kalsın" | Boyalı üründe de %0,5 uygulanır. Tahmin doğruysa boyalı satışların çoğu Satış Müdürü onayı bekler; gerçek veri görülünce yeniden karar verilir | Boyalı ürüne ayrı oran (Önerilen; Sinan bunu seçmedi) · Boya kg'ı stoğa eklensin |
| 6.5 | Üretimde çıkan kg, girenden %2 toleransın da üstünde fazlaysa ne olsun? Örnek: giren 1.000 kg, çıkan 1.030 kg. Bugün bu durum Üretim Müdürü onayıyla kapanabiliyor. | "Onayla da kapanmasın (Önerilen)" | Çıkan girenden fazlaysa emir hiçbir onayla kapanmaz; tartım düzeltilir. "Emir kapanmasın" kararıyla aynı yönde. Üretim Müdürü onayı yalnız eksik yönde kalır | Üretim Müdürü onayıyla kapansın |
| 6.6 | Bu kuraldan önce "çıkan girenden fazla" haliyle kapanmış eski üretim emirlerine ne yapılsın? | "Dokunulmasın, listelensin (Önerilen)" | Kapanmış emirler açılmaz ve değiştirilmez. Yalnız bir raporda listelenir; gerekirse tek tek bakılır. Kural yeni kapanışlara uygulanır | Dokunulmasın, liste de olmasın · Yeniden açılıp düzeltilsin |
| 6.7 | Kantar farkı kaydı "mevcut ortalama maliyetle" değerlenecek. Ürünün ortalama maliyeti o anda okunamıyorsa satış ne olsun? | "Satış geçsin, değer beklesin (Önerilen)" | Kantar farkı kg olarak kaydedilir ve satış tamamlanır; para değeri yazılmaz, maliyet düzelince tamamlanmak üzere işaretli bekler. Uydurma değer üretilmez, araç da beklemez | Satış beklesin · Sıfır değerle yazılsın |
| 7.1 | Kantar farkı "en büyük kaleme" yazılacak. İki etiket eşit kg'daysa (örnek: 500 + 500 kg, fark 4 kg) hangisine yazılsın? | "İrsaliyedeki ilk kalem (Önerilen)" | Eşitlikte irsaliyede önce gelen kaleme yazılır. Sonuç her zaman aynıdır, kişiye bağlı değildir ve kontrol etmesi kolaydır | En eski etiket · Kullanıcı seçsin |
| 7.2 | Ortalama maliyet okunamadığı için para değeri bekleyen kantar farkı kaydı nasıl tamamlansın? | "Kendiliğinden tamamlansın (Önerilen)" | Ürünün ortalama maliyeti yeniden okunabilir olduğunda sistem değeri kendisi yazar. Bekleyen kayıtlar bir listede görünür; kimsenin elle işlem yapması gerekmez | Muhasebe onayıyla tamamlansın · Elle girilsin |
| 8.1 | Para değeri bekleyen kantar farkı kaydı kendiliğinden tamamlanırken hangi ortalama maliyet kullanılsın? Örnek: satış 10 Kasım'da yapıldı, ürünün maliyeti 15 Kasım'da yeniden okunabilir oldu. | "Satış tarihindeki ortalama (Önerilen)" | Maliyet düzelince sistem, satış günü (10 Kasım) için geçerli ortalamayı hesaplayıp yazar. Kayıt, hiç beklememiş bir kantar farkıyla aynı değeri alır | Tamamlandığı andaki ortalama |

## 4.–8. tur — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URT-D42 | Çıkan > giren kuralının ölçüsü, onay yolu, eski emirler; Boyamada hurda (5.2, 6.3, 6.5, 6.6; URT-D41'i tamamlar) | (1) **URT-D41 (1) rulo bazında ölçülür:** her giren rulo ayrı bakılır; bir ruloda çıkan girenden fazlaysa, emir toplamında çıkan girenden az olsa da emir kapanmaz ve tartım o ruloda düzeltilir. (2) **Çıkan girenden fazlaysa emir hiçbir onayla kapanmaz; tartım düzeltilir.** Bu, %2 toleransın üstündeki fark için de geçerlidir. **Üretim Müdürü onayıyla kapanış yalnız eksik yönde kalır.** Daha yeni kullanıcı kararı olarak URT-D6'daki "tolerans dışı fark Üretim müdürü onayıyla kapanır" yolunun **artı yönünün** yerine geçer; eksik yöndeki onay yolu, URT-D6'nın değeri ve URT-D23 değişmez. 2. tur Sınırlar'ındaki "yazar okuması" kaydı bu maddeyle Sinan kararına dönüşür. (3) **Bu kuraldan önce "çıkan girenden fazla" haliyle kapanmış emirlere dokunulmaz:** açılmaz ve değiştirilmez; **yalnız bir raporda listelenir**. Kural yeni kapanışlara uygulanır. (4) **Boyamada sağlam + hurda = giren:** hurda çıkarsa sağlam çıktı, giren çelik kg'ından hurda kg'ı düşülerek yazılır (örnek: giren 1.000, hurda 20, sağlam 980); toplam giren kg'a eşit kalır. URT-D41 (3)'ü tamamlar: "çıkan kg = giren çelik kg" toplam (sağlam + hurda) içindir. (5) Sinan'ın cevabında olmayanlar (Sınırlar): (3)'teki emirlerin maliyeti URT-D40 (2) nedeniyle "okunamadı" kalır, bunun nasıl giderileceği sorulmadı; raporun yeri ve biçimi kod diliminin işidir. | `OrderClosureAssessment` (artı yönde tolerans içi ve onaylı kapanış kalkar; ayrı Üretim dilimi, K3), eski emir listesi raporu, Boyama emri çıktı kaydı, `ProductionReceiptCostCalculatorV1` (#1146) |
| SAT-D321 | Satışta kantar farkı: onaylayan, "giren" tanımı, eski tolerans bantları, payda, boyalı ürün (4.13, 5.4, 5.1, 6.1, 6.4; SAT-D319 (5)–(6) ve SAT-D320 (1)–(2)'yi tamamlar) | (1) **Sınır üstü satışı giren kişi Satış Müdürü'nün kendisiyse onayı başka bir Satış Müdürü verir** (aynı role sahip ikinci kişi). **Şirkette tek Satış Müdürü varsa satış onaysız bekler.** Onaylayan rol Satış Müdürü olarak kalır (Sinan önerilen "Muhasebe Müdürü"nü seçmedi); kural "giren ≠ onaylayan"dır. Bu madde rol atamaz ve grant değildir. (2) **"Satışı giren" = kantar veya sevk kaydını giren kişidir**; kendi farkını onaylayamayacak olan odur. Siparişi açan Satış Müdürü onaylayabilir. (3) **%0,5 sınırı ile yükleme toleransı bantları iki ayrı kontroldür ve ikisi de uygulanır:** %0,5 üstü fark Satış Müdürü onayı ister; %3 üstü yine bloklanır (örnek: sistemde 1.000 kg, kantarda 1.035 kg geçmez). **Eski bantlar (uyarı %1, onay %2, blok %3) ve `OPEN-004` değişmez; `OPEN-004` kapatılmaz**; `DEC-STK-009` aynen geçerlidir. Bu belge SAT-D319/SAT-D320'nin bunların "yerine geçtiğini" söylemez. (4) **%0,5 sistemdeki kg'a göre hesaplanır** (payda sistemdeki kg; örnek: 5,02 / 1.000 = %0,502, onaya düşer). SAT-D320 (2) ile birlikte paydanın irsaliyedeki toplam sistem kg'ı olduğu **yazar okumasıdır**. (5) **Boyalı üründe de aynı kural (%0,5) uygulanır**; ayrı oran tanınmaz ve boya kg'ı stoğa eklenmez (Sinan önerilen "boyalı ürüne ayrı oran"ı seçmedi). **Gerçek veri görülünce yeniden karar verilir**; boya ağırlığının kantar farkına etkisi ölçülmemiştir. | Sales (kantar/sevk kaydı, satış onayı), Inventory (yükleme toleransı kontrolü), Ayarlar (oran), izin kaydı (kod dilimi; grant insan kapısı) |
| SAT-D322 | Kantar fazlası tahsisli miktarı yediğinde; farkın yazılacağı kalem (5.3, 6.2, 7.1) | (1) **Kantarı fazla çıkan satış, fiziksel stok yetiyorsa kantar kg'ı ile geçer ve kantar farkı kaydı açılmaz**, başka siparişe tahsisli miktar bu yüzden karşılanamaz hale gelse de (örnek: stok 1.000; A 600, B 400 tahsisli; A 603 ile geçer). **Eksik, sonraki satışta (B) görülür ve o zaman kantar farkı kuralına (SAT-D319 (4)–(6), SAT-D320, SAT-D321) girer.** Sinan önerilen "kantar farkı kaydı açılsın"ı seçmedi; B'nin müşterisinin beklenmedik eksikle karşılaşabileceği seçenek metninde yazılıydı. (2) Sonuç (karar değil, (1)'in gereği): A satışı ile B satışı arasında serbest miktar geçici olarak eksik kalabilir (örnekte −3 kg); fiziksel stok eksiye düşmez, SAT-D318 (1) sağlanır. `DEC-STK-004` ("serbest miktar aşılmaz") ile ilişki Sinan'a ayrıca sorulmadı; bu belge `DEC-STK-004`'ün yerine geçtiğini ya da ona istisna getirdiğini **söylemez** (Sınırlar, `NEEDS-SINAN`). (3) **Aynı ürün bir satışta birden çok etiketten/stok yerinden çıkıyorsa kantar farkının tamamı en çok kg satılan kaleme yazılır** (tek kayıt; Sinan önerilen "kg oranında dağıtılsın"ı seçmedi). (4) **Eşit kg'da irsaliyedeki ilk kaleme yazılır.** (5) (3)–(4), SAT-D320 (5)'teki "her ürün için ayrı kayıt" yazar okumasını değiştirmez: ürün içinde tek kalem seçilir. | Sales (irsaliye), Inventory (tahsis/serbest miktar, kantar farkı girişi) |
| SAT-D323 | 11 Kasım "giriş gelince düzelir" cümlesi; ortalama okunamazken kantar farkı kaydının değeri (4.14, 6.7, 7.2, 8.1; SAT-D319 (7)'yi tamamlar) | (1) **11 Kasım kararındaki "eksi stokta maliyet hesaplanmaz, giriş gelince düzelir" cümlesi geçersiz sayılır:** 13 Kasım kararlarıyla **aşıldı**; eksi stok oluşamayacağı için uygulanacağı durum kalmadı. Cümle `SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER.md` içindedir (SAT-D301 (2)); o dosya geçmiş karar kaydı olarak **değiştirilmez ve silinmez**, aşılma burada kayıtlıdır. Daha yeni kullanıcı kararı olarak SAT-D301 (2)'nin yerine geçer. (2) Bu yüzden SAT-D318 kapsam sınırı ile (5)–(6)'daki ve 1. tur Sınırlar'ındaki "o hal için SAT-D301 (2) geçerlidir / geçerliliğini korur" yazar okumaları **geri alınır**: eksi stoktan girişle düzelme hesabı hiçbir hal için yazılmaz, golden beklenmez. Değişmez ihlalinde hesabın fail-closed kalması (SAT-D318 (4): "hesaplanamadı", değer uydurulmaz) **yazar okuması** olarak durur; hata sonucu eksi kayıt oluşursa ne yapılacağı (emniyet davranışı) bu cevapla kurala bağlanmadı (Sınırlar, `NEEDS-SINAN`). (3) **Ortalama maliyet okunamıyorsa satış geçer, değer bekler:** kantar farkı kg olarak kaydedilir ve satış tamamlanır; **para değeri yazılmaz**, tamamlanmak üzere işaretli bekler; uydurma ya da sıfır değer yazılmaz. (4) **Bekleyen değer kendiliğinden tamamlanır:** ürünün ortalama maliyeti yeniden okunabilir olduğunda sistem değeri kendisi yazar; **bekleyen kayıtlar bir listede görünür**; elle işlem ya da muhasebe onayı gerekmez. (5) **Tamamlamada satış tarihindeki ortalama kullanılır** (tamamlandığı andaki değil); kayıt, hiç beklememiş bir kantar farkıyla aynı değeri alır. (6) Muhasebe fişi Finance STOP sürerken üretilmez; bu madde onu kaldırmaz. | Ortalama maliyet (#1107: "girişle düzelme" hesabı yazılmaz), Inventory (kantar farkı girişi, bekleyen değer listesi), Finance (hesap eşlemesi; Finance STOP sürer) |

### 4.–8. tur — sınırlar

- Kod, izin, grant, Ayarlar verisi ve hesap planı değişmez. Hiçbir `OPEN-*` kapatılmaz (`OPEN-004` dahil);
  `DEC-STK-004` ve `DEC-STK-009` değişmez. 11 Kasım dosyası düzenlenmez. İnsan kapıları değişmez.
- Bu turlarla cevaplanan eski `NEEDS-SINAN` noktaları ilgili Sınırlar bölümlerinde yerinde işaretlenmiştir.
- Karara bağlanmayanlar (`NEEDS-SINAN`; tahminle seçilmez):
  - "Kantar farkı" hesabının kodu (SAT-D320 (4); Ayarlar'dan seçilecek, seçilmedi).
  - Hata sonucu eksi stok kaydı oluşursa emniyet davranışı ve kaydın nasıl düzeltileceği (4.14 bunu kurala bağlamadı;
    kod bugün `LedgerStockMismatch` / `NegativePhysicalStock` ile durduruyor).
  - `DEC-STK-004` ("serbest miktar aşılmaz") ile SAT-D322 (1) arasındaki ilişkinin biçimi (istisna mı, kural mı
    değişti); A satışından sonra B'nin tahsis kaydına ne olacağı (400 kalır mı, 397'ye iner mi); fiziksel stok
    yeterliyken (kayıt açılmayan halde) %0,5 onayının ve yükleme bantlarının A satışına uygulanıp uygulanmayacağı.
  - İrsaliyede "toplam fark"ın nasıl toplanacağı (bir üründe fazla, başka üründe eksik çıkarsa farklar birbirini
    götürür mü) ve irsaliye toplamı sınırın içindeyken tek üründeki büyük fark için ayrı üst sınır olup olmadığı
    (3. turdan açık).
  - Tek Satış Müdürü olan şirkette bekleyen satışın nasıl çözüleceği (SAT-D321 (1) yalnız "bekler" der).
  - Boyalı ürün satışında gerçek kantar farkı verisi (SAT-D321 (5): veri görülünce yeniden karar).
  - URT-D42 (3)'teki eski emirlerin maliyetinin nasıl okunur hale geleceği; URT-D42 (1)'in rulo girdisi olmayan
    süreçlerde nasıl ölçüleceği.
  - Boyama dışındaki, girdisi kg olmayan süreçler ve DEC-PRDCT-006 kalemleri (1.–2. turdan açık).
  - SAT-D323 (5)'te satış günü için geçerli ortalamanın, o güne ait kayıtlar sonradan düzeltildiyse hangi veriyle
    hesaplanacağı (port sözleşmesi ve K3 review'u; formül golden'ı gerekir).

### İnceleme sonrası ekler (DOCS-REVIEW bulguları; yeni karar değildir, ilişki ve `NEEDS-SINAN` kaydıdır)

Bu bölüm Sinan'a sorulmamış hiçbir noktayı karara bağlamaz; yalnız eksik atıfları ve açık soruları yazar.

- **`NEEDS-SINAN` — 12 Kasım "tartı farkı yalnız kaydedilir, sevk durmaz" kararıyla ilişki.** Sinan'ın Issue #66 yorumu
  `6818788693` (12 Kasım 2026, 2. tur) madde 2: "Tartı farkı (F-020 / OPEN-004): Teorik ve kantar ağırlığı farkı
  yalnız kaydedilir ve raporlanır; sevk durmaz (tolerans/onay akışı yok)." Bu karar `main`'de geçerli olarak anılır
  (`SINAN_KARARLARI_2026-11-12_LOJISTIK_F012_F020.md` girişi). 13 Kasım cevapları ise aynı kantar farkına satışta onay
  (SAT-D319 (6), SAT-D320 (1)) ve blok (SAT-D321 (3)) getirir. Bu turlarda 12 Kasım kararı Sinan'a hatırlatılmadı;
  iki kararın birlikte nasıl okunacağı **sorulmadı**. Olası okumalar: (a) 13 Kasım cevapları daha yeni karar olarak
  12 Kasım madde 2'nin yerine geçer; (b) kapsamlar ayrıdır (12 Kasım: teorik ↔ kantar ağırlığı farkı, Lojistik
  yükleme/sevk; 13 Kasım: sistem stoğu ↔ kantar farkı, satış ve stok kaydı). Hangisinin geçerli olduğu bu belgede
  **seçilmez**. Cevap gelene kadar: bu belge 12 Kasım madde 2'yi değiştirmez ve "yerine geçer" demez; kantar farkı
  kod dilimi **sevki/satışı bekleten onay ve blok davranışını etkinleştirmez** (SAT-D319 (6), SAT-D320 (1), SAT-D321
  (1)–(3)'ün bekletme/ret kısmı); farkın kaydı ve raporu (SAT-D319 (4), her iki kararla uyumlu) bu kayıttan
  etkilenmez. `OPEN-004` kapatılmaz.
- **Satıştaki tolerans bantlarının dayanağı.** SAT-D321 (3)'teki "yükleme toleransı bantları"nın satışta uygulanması
  `SINAN_KARARLARI_2026-10-25_SATIS.md` SAT-D5'e dayanır (`OPEN-004` başlangıç değerleri satışta da geçerli: %1 uyarı /
  %2 onay / %3 blok; ürün/işlem bazında Ayarlar'dan değişir); yüzdeleri kimin değiştireceği aynı dosyada SAT-D15 ve
  SAT-D17'dedir. Bu üç karar değişmez. %0,5 sınırı (SAT-D319 (5)) bunlardan ayrı bir Ayarlar değeridir.
- **Bantlar geçici varsayımdır.** %1/%2/%3 değerleri `ACIK_KARARLAR.md` `OPEN-004` (KISMEN) ve geçici varsayım notu
  gereği saha golden testi ve nihai işletme onayı bekleyen başlangıç değerleridir. SAT-D321 (3)'teki "%3 üstü yine
  bloklanır" cümlesi bu değerleri nihai yapmaz. Aynı cümledeki "`DEC-STK-009` aynen geçerlidir" ifadesi 5.1 cevabının
  sözü değil, **yazar okumasıdır** (belge `DEC-STK-009`'u değiştirmediği için).
- **`NEEDS-SINAN` — iki kontrol aynı satışta çakışınca.** (i) Fark hem %0,5'in hem onay bandının üstündeyse (ör.
  %2–%3) tek onay ikisini karşılar mı, iki ayrı onay mı gerekir; bant onayını kim verir (SAT-D320 (1) Satış Müdürü
  der; EK-14 No 63 mal kabulde onay bandı için Depo/Lojistik yöneticisini anar). (ii) EK-14 No 62–63'te blok bandı
  "varsayılan ret"tir ve iki kademeli istisna onayıyla aşılabilir (mal kabul için yazılmıştır); 5.1 cevabı "geçmez"
  der. İstisna yolunun satışta geçerli olup olmadığı sorulmadı. (iii) Bantların ölçü tabanı (ürün/işlem profili,
  satır) ile %0,5'in tabanı (irsaliye toplamı, sistem kg) farklıdır; 5.1 örneği ikisini aynı sayıyla gösterir, hangi
  tabanın hangi kontrolde kullanılacağı sorulmadı. (iv) Tek %0,5 oranının `DEC-STK-009`'daki "ürün ve işlem profiline
  göre" ilkesine tabi olup olmadığı (SAT-D321 (5) boyalı ürün için ayrı oran tanımaz). Cevap gelene kadar kod dilimi
  iki onayı birleştiren ya da birini ötekinin yerine sayan davranışı etkinleştirmez.
- **`DEC-STK-004` koruması.** SAT-D322 (1)–(2) ile `DEC-STK-004` ("serbest miktar aşılmaz") ilişkisi `NEEDS-SINAN`'dır
  (yukarıda). Cevap gelene kadar kod dilimi **tahsis kısıtını gevşeten davranışı etkinleştirmez**; `DEC-STK-004` ve
  DB kısıtları olduğu gibi kalır.
- **`OPEN-005`.** URT-D41 (2) ve URT-D42 (2) toleransı artı yönde kaldırır; `OPEN-005` metni ("±%2") değiştirilmez ve
  `OPEN-005` KISMEN kalır, kapatılmaz.
- **SAT-D301 (1).** 4.14'te geçersiz sayılan cümlenin "eksi stokta maliyet hesaplanmaz" kısmı için: bu belge yalnız
  SAT-D301 (2)'yi ("giriş gelince düzelir") aşılmış sayar; SAT-D301 (1)'in fail-closed yönü yazar okuması olarak
  sürer ve ihlal edilmiş kayıt için emniyet davranışı `NEEDS-SINAN` kalır.
- **Genel Sınırlar'daki "Boyama ve girdisi kg olmayan süreçler" maddesi:** 2. turda yalnız **Boyama** cevaplandı
  (URT-D41 (3)); girdisi kg olmayan öteki süreçler `NEEDS-SINAN` kalır (2. tur ve 4.–8. tur Sınırlar'ında yazılı).
- **1. tur kaydı.** 1. tur soru–cevabının Issue #66'da ayrı kaydı yoktur (bkz. Kaynak); SAT-D317 dahil 1. tur
  kararlarının tek kaydı bu belgedir. Kaydın #66'ya yazılması Sinan'ın onayına bırakılmıştır.

## Kod etkisi (kod şeridi; bu PR'da yapılmadı)

Bu belge kod değiştirmez. Aşağıdakiler yazarın review bulgularına dayanan okumasıdır; K3 kod PR'larında iki bağımsız
exact-head review ile doğrulanır.

- **#1146 (üretim girişi maliyet hesaplayıcısı):** fire-hariç formülünün kaynağı SAT-D316 (1)–(3); "yalnız kapalı emir"
  ve "iade, bağlı çıkışın maliyetiyle" kurallarının kaynağı SAT-D317; çıktı > tüketimde `mass_unbalanced` reddinin kaynağı
  URT-D40'tır. Kod yorumu ve test özetindeki atıflar bu kimliklere çevrilir; formül değişmez. SAT-D308 uygulama notundaki golden
  şartı #1146 için sürer.
- **#1107 (ortalama maliyet, eksi stok):** fiziksel eksi stok hali için "girişle düzelme" hesabı ve golden'ı SAT-D318
  (3)'e göre gerekmez. 1. turda `NegativeStock` nedeninin fiziksel eksi olmadan da doğabileceği iddia edildi (maliyet
  pozisyonu yokken veya eldeki maliyet miktarından büyük çıkışta); bu iddia `origin/main`'de **doğrulanamadı**:
  `AverageCostCalculator.Apply`, defter stoğu ≠ maliyet stoğu ise `ApplyIssue`'ya girmeden `LedgerStockMismatch` döner
  (2. tur araştırma notu). 4. turdan sonra (SAT-D323 (1)–(2)) SAT-D301 (2) hiçbir hal için uygulanmaz: "girişle
  düzelme" hesabı ve golden'ı yazılmaz; böyle bir hal #1107'nin K3 review'unda bulunursa davranışı `NEEDS-SINAN`'dır. SAT-D302 (ek masraf ortalamaya girmez) bu belgeden etkilenmez.
- **Emir kapanışı (`OrderClosureAssessment`, URT-D6 toleransı):** URT-D41 (1)–(2) ve URT-D42 (1)–(2)'ye göre artı yönde
  kapanış rulo bazında ve onayla da reddedilir; eski emirler yalnız listelenir (URT-D42 (3)); ayrı Üretim dilimi (K3).
- **Satışta kantar farkı kaydı:** SAT-D319 (4)–(7), SAT-D320 ve SAT-D321…SAT-D323; yeni dilim (K3). Yükleme toleransı
  bantları ayrı kontrol olarak kalır (SAT-D321 (3)). Sınır aşımı onayı yeni bir izin adı
  ister; kayıt kod diliminin, Satış Müdürü rolüne grant insan kapısının işidir. Muhasebe fişi Finance STOP sürerken
  üretilmez.

## Sınırlar

- **Kod, veri ve migration değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; izin veya grant açmaz. `KARAR_SICILI`/`ACIK_KARARLAR` değişmez, `OPEN-*` kapatılmaz (`OPEN-005` ve
  `OPEN-008` dahil).
- **İzlenecek dilim — emir kapanışı (2. turda karara bağlandı: URT-D41 (1); aşağıdaki paragraf 1. tur kaydıdır):** bugünkü kod, tolerans tanımlıyken çıktı > tüketim farkını tolerans
  içinde dengeli sayıp emri kapatabiliyor, dışında onayla kapatıyor (#1146 review B bulgusu; `OrderClosureAssessment`).
  Bu davranış URT-D40 (1) ile çelişir. Kapanışın çıktı > tüketimde nasıl davranacağı (reddetmek, düzeltme istemek,
  hangi hata kodu) **bu belgede karara bağlanmadı**; ayrı Üretim diliminde ele alınır ve Sinan'ın cevabında olmayan
  ayrıntılar `NEEDS-SINAN` olur. URT-D6'nın ±%2 değeri ve tüketim > çıktı yönündeki (fire/kayıp) tolerans davranışı
  değişmedi.
- **Karara bağlanmayanlar (`NEEDS-SINAN` veya sahip şeridin kararı; tahminle seçilmez):**
  - Hurda satıldıktan sonra ne olacağı: elle girilen fiyatla hesaplanmış maliyetin gerçek satış fiyatına göre
    düzeltilip düzeltilmeyeceği, farkın nereye yazılacağı (SAT-D316 (4) yalnız "satışa kadar elle fiyat" der).
  - #66 6826603722 (b) ("aynı gider, ayrı satır": tartılan fire ve ölçülmeyen proses kaybı raporda iki satır) bugün
    sorulmadı; bu belge onu karar olarak yazmaz, yorumdaki kayıt olarak kalır.
  - #66 6826763903 (c) seçenek metnindeki "Raporda ayrı satırda yalnız kg ve oranı görünür" cümlesi de bugün
    sorulmadı; karar olarak yazılmaz.
  - **2. turda cevaplandı (URT-D41 (3)):** ~~`NEEDS-SINAN`~~ **Boyama ve girdisi kg olmayan süreçler.** Boya litreyle tüketilir (Master PNT-006, URT-D9);
    boyanmış çıktının kg'ı girdi kg'ından meşru olarak fazla olabilir. URT-D40 (1)'in bu hatlarda nasıl ölçüleceği
    (boya ağırlığı girene sayılır mı) sorulmadı; URT-D40 (2)–(3) bu hatlara uygulanmaz, maliyet hesabı o emirlerde
    karar gelene kadar değer üretmez.
  - `NEEDS-SINAN`: **DEC-PRDCT-006 kalemleri.** Kütle dengesinde ayrı sayılan geri kazanılabilir artık, WIP ve ölçüm
    düzeltmesinin "çıkan/giren" karşılaştırmasında hangi tarafa yazılacağı; URT-D40 (3) bunları saymaz.
  - **2. turda cevaplandı (URT-D41 (1)–(2)):** ~~`NEEDS-SINAN`~~ **URT-D6 artı yönü ile URT-D40 (1) çelişkisi.** URT-D6 toleransı ±%2'dir (artı yön dahil) ve o da
    Sinan kararıdır. Bugünkü kodla artı yönde tolerans içinde kapanmış emir + SAT-D317 (1) + URT-D40 (2) = maliyeti
    hiç hesaplanamayan kapalı emir. URT-D6'nın artı yönünün kalkıp kalkmadığı ve böyle kapanmış emrin nasıl
    düzeltileceği Sinan'a sorulur; cevap gelene kadar bu emirlerde maliyet "okunamadı" kalır.
  - **4. turda kısmen cevaplandı (4.14, SAT-D323 (1)–(2): "giriş gelince düzelir" cümlesi aşıldı, bu hal için de
    uygulanmaz; aşağıdaki cümle 1. tur kaydıdır, halin davranışı bulunursa `NEEDS-SINAN`):** **Fiziksel eksi olmadan
    maliyet pozisyonunun `NegativeStock` okunması** (maliyet defterinde önceki
    girişi olmayan ya da eldeki maliyet miktarından büyük çıkış). SAT-D318 bu hali kapsamaz; SAT-D301 (2) ("giriş
    gelince düzelir") geçerlidir, düzelmenin hesap biçimi ve golden'ı Sinan'a sorulur. Hangi akışların bu hali
    ürettiği uçtan uca doğrulanmadı; `origin/main`'de bu hal üretilemedi (2. tur araştırma notu), satır yalnız #1107
    review'u böyle bir akış bulursa geçerlidir.
  - Hurda fiyatını kimin gireceği (izin adı kod PR'ının işidir; grant/rol insan kapısıdır), dönemin emrin mi girişin mi
    dönemi olduğu, hurda fiyatının girdi birim maliyetini aşması halinde sınır/uyarı.
  - Kullanılabilir artığın (UsableRemnant) maliyet payı (OC-3) açık kalır; hesaplayıcı artık varken fail-closed kalır.
  - Sıfır maliyetli çıkışın üretim maliyetinde nasıl okunacağı (SAT-D297 ile hizalama) ve yuvarlamanın nerede
    uygulanacağı (SAT-D265) port sözleşmesinin işidir; bu belge seçmez.
  - Tartı hassasiyetinden doğan çok küçük çıktı > tüketim farkı için ayrı bir pay tanınıp tanınmayacağı: 2. turda
    cevaplandı — pay tanınmaz, emir kapanmaz (URT-D41 (1)).
  - `NEEDS-SINAN`: eksi stok değişmezi ihlal edilmiş bir kayıt (ör. veri aktarımı hatası) görülürse nasıl düzeltileceği
    ve pozisyonun bir girişle yeniden okunur olup olmayacağı; SAT-D318 yalnız oluşmamasını söyler, hesabın o zamana
    kadar fail-closed kalması yazar okumasıdır. (4. tur 4.14: "giriş gelince düzelir" cümlesi aşıldı, SAT-D323 (1);
    emniyet davranışı yine kurala bağlanmadı ve `NEEDS-SINAN` kalır.)
  - Yeniden açılan emirde (`SINAN_KARARLARI_2026-11-12_URETIM_EMRI_DUZELTME.md`) daha önce hesaplanmış maliyete ne
    olacağı; SAT-D317 (1) yalnız kapanmamış emirde değer üretilmediğini söyler.
  - Finans'ın kasa/banka eksi bakiye kuralları (FNS-D11, FNS-D22) para bakiyesidir, stok değildir; SAT-D318 onlara
    dokunmaz (**yazar okuması**).
- "Yazar okuması" diye işaretli noktalar Sinan'ın sözü değildir; Sinan farklı isterse ayrı karar gerekir.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
