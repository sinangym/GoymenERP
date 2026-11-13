# Sinan Kararları — 2026-11-13 (Üretim maliyeti: hurdanın maliyetten düşülmesi, çıkan ≤ giren, yalnız kapalı emir ve üretimden iade; Stok: hiçbir modülde eksi stok yok)

Bu belge önceki karar dosyalarını silmez; **SAT-D316, SAT-D317, SAT-D318 ve URT-D40**'ı ekler. SAT-D297 (EK59),
SAT-D301…SAT-D307 (`SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER.md`), SAT-D308
(`SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md`), URT-D5, URT-D6
(`SINAN_KARARLARI_2026-11-01_URETIM.md`) ve URT-D23 (`SINAN_KARARLARI_2026-11-02_URETIM_EK2.md`) geçerlidir; bu belge
onların dosyalarını değiştirmez. İki yerde daha yeni kullanıcı kararı eski metnin önüne geçer; ikisi de aşağıda cümle
cümle yazılıdır: SAT-D316 ↔ SAT-D308 (1)–(2) ve SAT-D318 ↔ SAT-D301 (2). `KARAR_SICILI` ve `ACIK_KARARLAR` değişmez;
hiçbir `OPEN-*` kapatılmaz.

**Kimlikler:** Maliyet zinciri EK59'dan beri SAT-D serisindedir (Stok için ayrı önek yoktur); üretim emrinin kütle
kuralı URT-D serisindedir. `origin/main` `63041c6c`'de (bu dalın çıkış noktası; sonraki `84dcb86b` `docs/decisions` değiştirmez) en büyük kimlikler SAT-D315 ve URT-D39'dur
(`git grep -ohE "SAT-D[0-9]+|URT-D[0-9]+" origin/main -- docs/decisions`). Aynı gün `origin` üzerindeki bütün dalların
`docs/decisions` eklemeleri tarandı; SAT-D316+ veya URT-D40+ geçmiyordu (#1146 ve #1107 dalları karar dosyası
eklemiyor). Yeni önek icat edilmedi.

**Kaynak:** 13 Kasım 2026, Claude şeridi orkestratör oturumunda Sinan'ın sohbet cevabı. Sorular düz metin olarak soruldu,
cevaplar serbest metindir ve aşağıda **aynen** (yazım hataları dahil) alıntılanır. Sorular iki K3 PR'ının review
bulgularından doğdu: #1146 (üretim girişi maliyet hesaplayıcısı; fire-hariç formülünün ve iki kuralın karar kimliği
yok, kapanış toleransı ile maliyet reddi çelişiyor) ve #1107 (ortalama maliyette eksi stoktan düzelme; golden yok).

**Önceki kayıt (soru 1'in dayandığı):** Issue #66 yorumları
[6826603722](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6826603722) ve
[6826763903](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6826763903) (12 Kasım 2026, koordinatör
aracılığıyla sorulan (a)–(d) cevapları). Bu yorumlar bugüne kadar hiçbir karar dosyasına işlenmemişti. Soru 1 bu
yorumlardaki (a), (c) ve (d)'yi sordu; (b) sorulmadı (Sınırlar).

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

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D316 | Fire anahtarı kapalıyken hurdanın maliyetten düşülmesi (soru 1; #66 6826603722 (a), 6826763903 (c)–(d)) | (1) **Tartılan hurda × hurda fiyatı maliyetten düşülür; kalan mamule yüklenir.** (2) **Proses firesi (tartılmayan kayıp) sıfır değerlidir**; maliyeti mamulde kalır (kayıttaki ifade "sağlam ürüne kalır"dır; 2. kaliteyi de kapsayan "mamul" ifadesi SAT-D307 (2) ile hizalı **yazar okumasıdır**). (3) **Hurda fiyatı dönemlik, elle girilir.** Bu belge formülün payını ve paydasını yazmaz: paydanın anahtar kapalıyken de SAT-D307'deki "sağlam kg + 2. kalite kg" olduğu #1146 kodundaki okumadır, Sinan'ın sözü değildir; K3 review'u SAT-D308 uygulama notundaki golden şartıyla doğrular, golden gelmeden formül kilitli sayılmaz. (4) Sinan'ın eki: **hurda ayrıca satılır**; "o zamana kadar" maliyetlendirme için **elle fiyat girilebilir**. "O zamana kadar"ın "hurda satılana kadar" olduğu **yazar okumasıdır**. (5) SAT-D308 ile ilişki: bu madde SAT-D305 (2) fire anahtarı **kapalıyken** geçerlidir; anahtar açıkken SAT-D307 (1)–(2) değişmez. SAT-D308 (1) "fire maliyeti ürün maliyetine yüklenmez" ve (2) "ayrı fire/dönem gideri olarak raporlanır" cümlelerinin **tutarını** bu madde belirler: ürüne yüklenmeyen ve ayrı raporlanan tutar tartılan hurda × hurda fiyatıdır, firenin bunu aşan maliyeti mamule yüklenir. Daha yeni kullanıcı kararı olduğu için (1)–(2), SAT-D308 (1)–(2)'nin **kısmen yerine geçer** (SAT-D308 (1) "fire maliyeti ürün maliyetine yüklenmez" diyordu; anahtar kapalıyken firenin hurda değerini aşan maliyeti artık ürüne **yüklenir**); SAT-D308 (3)–(6) aynen geçerlidir (gider kaydı hesap eşlemesi gelmeden etkinleşmez). İlişkinin bu ifadesi **yazar okumasıdır**. (6) 6826763903 (d) seçenek metnindeki ayrıntılar — fiyatı yetkili biri her ay için TL/kg girer, geçmiş dönem değişmez, fiyat girilmemişse hesap tahmin yapmaz ve "okunamadı" der — Sinan'ın bugün "evet" dediği kaydın parçasıdır; bugünkü soruda tek tek sayılmadıkları için "evet"in bunları da kapsadığı **yazar okumasıdır**. Fiyat yokken "okunamadı" ayrıca fail-closed kuralın gereğidir (tahminle para üretilmez). | `ProductionReceiptCostCalculatorV1` (#1146), Üretim maliyet portu, hurda fiyatı girişi (ayrı dilim), fire raporu |
| URT-D40 | Üretimde çıkan girenden fazla olamaz (soru 2) | (1) **Üretimde çıkan, girenden fazla olamaz.** (2) Bu yüzden maliyet hesabının **çıktı > tüketim** durumunu reddetmesi (`mass_unbalanced`, değer üretmez) doğrudur; fark tolerans içinde olsa da değer üretilmez. Sinan A/B harfi seçmedi ve kendi sorusu ("cevabı olabildi mi") cevaplanmadı; (1)'den çıkarılan bu sonuç (soru 2'deki B yönü) **yazar okumasıdır**, Sinan'ın kilitlediği kural yalnız (1)'dir. Yön fail-closed'dır (değer üretmez); kapsam sınırları ve URT-D6 artı yönüyle çelişki Sınırlar'da `NEEDS-SINAN`'dır. Emrin kapanışta nasıl davranacağı bu belgede karara bağlanmadı (bkz. Sınırlar). (3) "Çıkan" = sağlam + 2. kalite + tartılan hurda + beyan edilen proses kaybı, "giren" = tüketilen kg (çıkış − iade) eşlemesi #1146'daki hesabın tanımıdır ve **yazar okumasıdır**; DEC-PRDCT-006'nın ayrı saydığı kalemler (geri kazanılabilir artık, WIP, ölçüm düzeltmesi) bu tanımda yoktur ve girdisi kg olmayan süreçler (Boyama) kapsanmaz (Sınırlar). (4) URT-D6'nın tolerans **değeri** (hat başına ±%2) ve URT-D23 değişmez; bu madde yeni eşik seçmez. | `ProductionReceiptCostCalculatorV1` (#1146); emir kapanışı için bkz. Sınırlar (izlenecek dilim) |
| SAT-D317 | Maliyetin hesaplanacağı an ve üretimden iade (soru 3) | (1) **Üretim girişi maliyeti yalnız kapanmış emirler için hesaplanır.** Kapanmamış emirde değer üretilmez (fail-closed). (2) **Üretimden iade, bağlı olduğu çıkışın o günkü maliyetiyle değerlenir**; hareketli ortalamanın iade anındaki değeri kullanılmaz. Sinan'ın onayı şartlıydı ("sencede mantıklıysa"); şartın sağlandığı değerlendirmesi orkestratöründür, Sinan'ın sözü değildir: (1) fail-closed'dır, (2) iadeyi ilk hareketin maliyetiyle değerleyen SAT-D303 ve SAT-D306 (1) ile aynı yöndedir. (3) "O günkü maliyet"in, çıkış hareketine çıkış anında yazılan birim maliyet olduğu ve bağlı çıkışı ya da okunur maliyeti olmayan iadede sonucun "okunamadı" olduğu **yazar okumasıdır** (geri alınabilir); değeri Stok verir, Üretim sürümlü portla okur (cross-module, K3). (4) SAT-D303 (müşteri iadesi) ve SAT-D306 (alış iadesi) değişmez; bu madde yalnız üretime verilen girdinin iadesini kapsar. | `ProductionReceiptCostCalculatorV1` (#1146), Üretim maliyet portu, Stok çıkış maliyeti |
| SAT-D318 | Hiçbir modülde eksi stok yok (soru 4) | (1) **ERP'nin hiçbir modülünde stok eksiye düşmez**; "stok −1" diye bir durum yoktur. Bu madde **fiziksel** stok (eldeki miktar) içindir; projeksiyon/ATP ve serbest stok hesapları (Master INV-009) ayrı kavramlardır ve bu maddeyle değişmez (**yazar okuması**). **Kapsam sınırı:** aşağıdaki (3)–(6) yalnız **fiziksel stokun eksiye düşmesi** halini kapsar; fiziksel stok eksiye düşmediği halde maliyet pozisyonunun `NegativeStock` okunduğu hali kapsamaz (Sınırlar, `NEEDS-SINAN`); o hal için SAT-D301 (2) olduğu gibi geçerlidir. (2) Stoğu eksiye götürecek hareket **reddedilir** (mevcut `StockLedger` davranışı: `NegativePhysicalStock`); bu davranış artık Sinan kararına bağlıdır. (3) Bu yüzden **fiziksel eksi stoktan** "düzelme" hesabı ve golden'ı gerekmez; formül olarak yazılmaz. (4) Ortalama maliyet hesabında fiziksel eksi stok durumu bir **değişmez ihlalidir** ve **fail-closed** kalır: SAT-D301 (1) aynen geçerlidir (maliyet "hesaplanamadı", değer uydurulmaz, sıfır sayılmaz). İhlal durumundaki pozisyonun bir girişle kendiliğinden açılmaması Sinan'ın bugünkü sözü değildir, **yazar okumasıdır**: Sinan bugün yalnız eksi stokun oluşmamasını söyledi; 11 Kasım'daki "giriş gelince düzelir" sözü bu durum için uygulanmamış olur. Yön güvenlidir (değer üretilmez); Sinan farklı isterse ayrı karar gerekir (Sınırlar). (5) **Yerine geçtiği cümle:** SAT-D301 (2)'nin ikinci cümlesi — "Düzelmenin hesap biçimi (hangi girişten sonra, hangi ortalamayla yeniden başlanacağı) Sinan'ın metninde yazmaz; **uygulama okunuşu (geri alınabilir)** kod PR'ında yazılır ve review edilir." — ve aynı dosyanın "Uygulama notu"ndaki "Düzelme hesabı uygulama okunuşudur ve kod PR'ında yazılır … Formül niteliğindeki bu okuma için golden örnek ChatGPT şeridinden ya da gerekirse `NEEDS-SINAN` ile gelir; K3 review'u golden'la doğrular" cümleleri **fiziksel eksi stok hali için** artık uygulanmaz: o hal için düzelme hesabı kod PR'ında yazılmaz, golden beklenmez; kapsam sınırındaki hal için bu cümleler geçerliliğini korur. Aynı nedenle o dosyanın uygulama notundaki "eksi stok nedeni yapışkan kalamaz" cümlesi ve dilim tablosundaki "Eksi stok nedeninin yapışkan olmaması, düzelme hesabı ve testleri" satırı da yalnız fiziksel eksi stok hali için uygulanmaz. (6) SAT-D301 (2)'nin ilk cümlesi ("Bu durum **kalıcı değildir**: stok bir girişle düzelince maliyet yeniden okunur") fiziksel eksi stok oluşmadığı için o hal bakımından **uygulama alanı kalmayan** cümledir; kapsam sınırındaki hal için aynen geçerlidir; ihlal durumundaki bir pozisyonun bir girişle kendiliğinden açılmaması (fail-closed kalması) bu maddenin (4)'üdür. (5)–(6)'daki cümle eşlemesi **yazar okumasıdır**. (7) SAT-D306 (c)–(d)'deki "eksiye düşerse" halleri de aynı nedenle oluşmaz; metinleri değişmez (**yazar okuması**). | `StockLedger`, `AverageCostCalculator` / `AverageCostUnreadableReason.NegativeStock` (#1107), stok hareketi üreten bütün modüller |

## Kod etkisi (kod şeridi; bu PR'da yapılmadı)

Bu belge kod değiştirmez. Aşağıdakiler yazarın review bulgularına dayanan okumasıdır; K3 kod PR'larında iki bağımsız
exact-head review ile doğrulanır.

- **#1146 (üretim girişi maliyet hesaplayıcısı):** fire-hariç formülünün kaynağı SAT-D316 (1)–(3); "yalnız kapalı emir"
  ve "iade, bağlı çıkışın maliyetiyle" kurallarının kaynağı SAT-D317; çıktı > tüketimde `mass_unbalanced` reddinin kaynağı
  URT-D40'tır. Kod yorumu ve test özetindeki atıflar bu kimliklere çevrilir; formül değişmez. SAT-D308 uygulama notundaki golden
  şartı #1146 için sürer.
- **#1107 (ortalama maliyet, eksi stok):** fiziksel eksi stok hali için "girişle düzelme" hesabı ve golden'ı SAT-D318
  (3)'e göre gerekmez. Ancak kodda `NegativeStock` nedeni fiziksel eksi olmadan da doğabilir (maliyet pozisyonu yokken
  veya eldeki maliyet miktarından büyük çıkışta; `AverageCostCalculator.ApplyIssue`). O hal SAT-D318 kapsamında
  değildir: SAT-D301 (2) geçerli kalır, düzelme hesabının golden'ı `NEEDS-SINAN`'dır (Sınırlar). #1107 bu ayrımı
  yapmadan düzelmeyi tümden kaldıramaz. SAT-D302 (ek masraf ortalamaya girmez) bu belgeden etkilenmez.
- **Emir kapanışı (`OrderClosureAssessment`, URT-D6 toleransı):** izlenecek ayrı dilim; bkz. Sınırlar.

## Sınırlar

- **Kod, veri ve migration değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; izin veya grant açmaz. `KARAR_SICILI`/`ACIK_KARARLAR` değişmez, `OPEN-*` kapatılmaz (`OPEN-005` ve
  `OPEN-008` dahil).
- **İzlenecek dilim — emir kapanışı (karar değil):** bugünkü kod, tolerans tanımlıyken çıktı > tüketim farkını tolerans
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
  - `NEEDS-SINAN`: **Boyama ve girdisi kg olmayan süreçler.** Boya litreyle tüketilir (Master PNT-006, URT-D9);
    boyanmış çıktının kg'ı girdi kg'ından meşru olarak fazla olabilir. URT-D40 (1)'in bu hatlarda nasıl ölçüleceği
    (boya ağırlığı girene sayılır mı) sorulmadı; URT-D40 (2)–(3) bu hatlara uygulanmaz, maliyet hesabı o emirlerde
    karar gelene kadar değer üretmez.
  - `NEEDS-SINAN`: **DEC-PRDCT-006 kalemleri.** Kütle dengesinde ayrı sayılan geri kazanılabilir artık, WIP ve ölçüm
    düzeltmesinin "çıkan/giren" karşılaştırmasında hangi tarafa yazılacağı; URT-D40 (3) bunları saymaz.
  - `NEEDS-SINAN`: **URT-D6 artı yönü ile URT-D40 (1) çelişkisi.** URT-D6 toleransı ±%2'dir (artı yön dahil) ve o da
    Sinan kararıdır. Bugünkü kodla artı yönde tolerans içinde kapanmış emir + SAT-D317 (1) + URT-D40 (2) = maliyeti
    hiç hesaplanamayan kapalı emir. URT-D6'nın artı yönünün kalkıp kalkmadığı ve böyle kapanmış emrin nasıl
    düzeltileceği Sinan'a sorulur; cevap gelene kadar bu emirlerde maliyet "okunamadı" kalır.
  - `NEEDS-SINAN`: **Fiziksel eksi olmadan maliyet pozisyonunun `NegativeStock` okunması** (maliyet defterinde önceki
    girişi olmayan ya da eldeki maliyet miktarından büyük çıkış). SAT-D318 bu hali kapsamaz; SAT-D301 (2) ("giriş
    gelince düzelir") geçerlidir, düzelmenin hesap biçimi ve golden'ı Sinan'a sorulur. Hangi akışların bu hali
    ürettiği uçtan uca doğrulanmadı.
  - Hurda fiyatını kimin gireceği (izin adı kod PR'ının işidir; grant/rol insan kapısıdır), dönemin emrin mi girişin mi
    dönemi olduğu, hurda fiyatının girdi birim maliyetini aşması halinde sınır/uyarı.
  - Kullanılabilir artığın (UsableRemnant) maliyet payı (OC-3) açık kalır; hesaplayıcı artık varken fail-closed kalır.
  - Sıfır maliyetli çıkışın üretim maliyetinde nasıl okunacağı (SAT-D297 ile hizalama) ve yuvarlamanın nerede
    uygulanacağı (SAT-D265) port sözleşmesinin işidir; bu belge seçmez.
  - Tartı hassasiyetinden doğan çok küçük çıktı > tüketim farkı için ayrı bir pay tanınıp tanınmayacağı: Sinan'ın
    cevabı pay tanımıyor; ayrı bir pay istenirse yeni karar gerekir.
  - `NEEDS-SINAN`: eksi stok değişmezi ihlal edilmiş bir kayıt (ör. veri aktarımı hatası) görülürse nasıl düzeltileceği
    ve pozisyonun bir girişle yeniden okunur olup olmayacağı; SAT-D318 yalnız oluşmamasını söyler, hesabın o zamana
    kadar fail-closed kalması yazar okumasıdır.
  - Yeniden açılan emirde (`SINAN_KARARLARI_2026-11-12_URETIM_EMRI_DUZELTME.md`) daha önce hesaplanmış maliyete ne
    olacağı; SAT-D317 (1) yalnız kapanmamış emirde değer üretilmediğini söyler.
  - Finans'ın kasa/banka eksi bakiye kuralları (FNS-D11, FNS-D22) para bakiyesidir, stok değildir; SAT-D318 onlara
    dokunmaz (**yazar okuması**).
- "Yazar okuması" diye işaretli noktalar Sinan'ın sözü değildir; Sinan farklı isterse ayrı karar gerekir.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
