# Sinan Kararları — 2026-11-13 EK1 (Üretim maliyeti: payda, hurda DUR, boya ve kg dışı girdi, artık, hurda ayı; eksi stok, deneme kaydı, sayım fazlası ve eksiği; teslim toleransı, tam %0,5 ve fark toplamı; mal kabul bantları; çıktı sınıfı eşlemesi; kasa; resmî satış faturası; yükleme talimatı iptali; DELTA-OK)

Bu belge önceki karar dosyalarını silmez ve değiştirmez; **SAT-D326…SAT-D344, URT-D43…URT-D47, FNS-D33…FNS-D39,
LOJ-D15 ve GOV-D6**'yı ekler. Hepsi Issue #66'ya yazılmış ama henüz `docs/decisions/` altına işlenmemiş Sinan
cevaplarıdır. Cevapların biri dışında hepsi 13 Kasım 2026 tarihlidir; 8. turun 10. sorusu düzeltilmiş metinle yeniden
soruldu ve cevabı 14 Kasım'da yazıldı (SAT-D341). `SINAN_KARARLARI_2026-11-13_URETIM_MALIYETI_VE_EKSI_STOK.md`
(SAT-D316…SAT-D325, URT-D40…URT-D42; aşağıda "13 Kasım dosyası"), `SINAN_KARARLARI_2026-11-11_FINANS_F005.md`
(FNS-D17…), `SINAN_KARARLARI_2026-11-12_LOJISTIK_F012_F020.md`, `SINAN_KARARLARI_2026-11-07_K3_DELTA.md` (GOV-D1),
`SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md` (BS-5, BS-14), SAT-D5 (`SINAN_KARARLARI_2026-10-25_SATIS.md`), SAT-D15
ve SAT-D16 (`SINAN_KARARLARI_2026-10-26_SATIS_EK.md`), SAT-D296 ve SAT-D297
(`SINAN_KARARLARI_2026-11-11_SATIS_EK59_ORTALAMA_MALIYET.md`) ve `SINAN_KARARLARI_2026-10-21.md` geçerlidir; bu belge
onların dosyalarını değiştirmez. Aşağıdaki yerlerde daha yeni kullanıcı cevabı eski metnin, bir "yazar okuması"nın ya
da bir `NEEDS-SINAN` kaydının önüne geçer:

- URT-D43 ↔ `docs/delivery/URETIM_HAZIRLIK_PLANI_v1.md` §5.3a'daki "eşleme yapılandırılabilir 1:1 tablo olur, koda
  gömülmez" ve "Kurallar"daki şirket başına tablo ifadesi. Plan bu PR'da güncellendi; eski ifade üstü çizili kalır,
  tablodaki değerler (OC-1, OC-2, OC-4, OC-5) değişmez.
- URT-D44 ↔ #1146 kodundaki brüt karşılaştırma. Issue #66 yorumu `6861267281`'deki brüt/net `NEEDS-SINAN` kaydı 8.1
  ("Net") ile kapandı.
- URT-D45 ve URT-D46 ↔ #1146 hesaplayıcısının kg dışı girdi ve kullanılabilir artık varken maliyeti "hesaplanamadı"
  bırakması. URT-D46, 13 Kasım dosyası Sınırlar'ındaki OC-3 (artığın maliyet payı) satırını cevaplar.
- SAT-D326 (1) ↔ SAT-D316 (3)'teki "paydanın … #1146 kodundaki okumadır, Sinan'ın sözü değildir" ve "golden gelmeden
  formül kilitli sayılmaz" kaydı: payda artık Sinan cevabıdır, golden 4.1 ve Y.1 örneğidir.
- SAT-D328 (2) ↔ SAT-D318 (2)'deki "yolun ret olduğu **yazar okumasıdır**" kaydı: kantar farkı dışındaki işlemlerde
  durdurma artık Sinan cevabıdır.
- SAT-D335 ↔ SAT-D318 (4) ve SAT-D323 (2)'deki fail-closed yazar okumaları ve 13 Kasım dosyasındaki "eksi stok
  değişmezi ihlal edilmiş bir kayıt" `NEEDS-SINAN` kaydı.
- SAT-D341 ↔ SAT-D321 (3) ve SAT-D324'teki "%0,5 üstü" ifadesinin harfiyen okunuşu: tam %0,5 fark onaya düşer.
- SAT-D342 ↔ SAT-D333'ün "cevap gelene kadar" geçici hali; 13 Kasım dosyası 4.–8. tur ve 9. tur Sınırlar'ındaki
  "fiziksel stok yeterliyken %0,5 onayı" `NEEDS-SINAN` kaydı.
- SAT-D343 ↔ 13 Kasım dosyası 3. tur Sınırlar'ındaki "toplam fark" sorusu.
- FNS-D39 ↔ F005 belgesindeki "Onay bekleyen kaydın girene ait geri çekme yolu bu belgede karara bağlanmadı" kaydı.
- LOJ-D15 ↔ F012–F020 belgesindeki "Talimatı iptal edilen sevk için daha önce ayrılmış tahsis ve stok rezervinin
  serbest bırakılma politikasının ayrıntıları" satırı (yalnız sorulan kısım).
- GOV-D6 (4) ↔ GOV-D1'in imzacı cümlesi: yalnız **yazar okuması** düzeyinde; GOV-D1 dosyası değişmez.

Sonraki turun cevabı önceki turun aynı konudaki **cevabının**, "yazar okuması"nın ve o cevapla kapanan `NEEDS-SINAN`
kaydının önüne geçer; bir turda **sorulmamış** nokta sonraki turun cevabıyla kendiliğinden kapanmaz. `KARAR_SICILI` ve
`ACIK_KARARLAR` değişmez; hiçbir `OPEN-*` kapatılmaz (`OPEN-004` KISMEN kalır, `DEC-STK-009` değişmez).

**Kimlikler:** `origin/main` `1de1d640`'ta (bu dalın çıkış noktası) en büyük kimlikler SAT-D325, URT-D42, FNS-D32,
LOJ-D14 ve GOV-D5'tir (`git grep -ohE "SAT-D[0-9]+|URT-D[0-9]+|FNS-D[0-9]+|LOJ-D[0-9]+|GOV-D[0-9]+" origin/main -- docs/decisions`).
Aynı gün açık 16 PR'ın `docs/decisions` eklemeleri tarandı; yalnız #1101 karar dosyası ekliyor ve o dosya ALS
kimlikleri kullanıyor, çakışma yoktur. Maliyet zinciri EK59'dan beri SAT-D serisindedir (Stok için ayrı önek yoktur);
8. turun üretim maliyeti cevapları (8.1, 8.13, 8.14, 8.15) üretim emrinin hesabına ait olduğu için URT-D serisine
yazıldı. 4.1/Y.1'in paydası SAT-D326'da, hurda DUR karşılaştırmasının tanımı URT-D44'tedir; iki madde birbirine atıf
verir. Yeni önek icat edilmedi.

**Kaynak:** Bütün kayıtlar Issue #66 yorumlarıdır. Ajanların hepsi aynı `sinangym` hesabıyla yazar; yorumu yazan hesap
cevabın sahibini göstermez, cevabın sahibi her yorumda Sinan diye yazılıdır. Cevaplar **aynen** (yazım hataları dahil)
alıntılanır; "(Önerilen)" işaretleri soranındır. Yorumlardaki şu bölümler Sinan'ın sözü değildir ve karar olarak
yazılmadı: A'daki "İnceleyici oturumun okumaları", E'deki "Okumalarım (Sinan'ın sözü değil)", I'daki "Okumam",
"Araştırma özeti" ve "#1102'ye etkisi (okumam)", 8. turdaki "Soranın notları" ve "Nereye gider" tablosu ve kaydı
yazanın cevaba eklediği açıklamalar (aşağıda "aktaranın notu"). Bu belgenin yazımı sırasında hiçbir yorum
düzenlenmemişti (oluşturma ve güncelleme zamanları aynı).

| Etiket | Yorum | Saat (UTC) | Kayıt | Bu belgedeki satırlar |
|---|---|---|---|---|
| A | [6856195059](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6856195059) | 2026-11-13 14:41:56 | "SINAN-CEVAP 2026-11-13 — #1109, #1151, #1147, #1078 inceleme soruları"; cevaplar seçilen seçenek metniyle, seçilmeyen seçenekler kayıtta yok | 1.1–1.8 |
| B | [6857132760](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6857132760) | 2026-11-13 15:16:10 | "SINAN-CEVAP 2026-11-13 (2)"; dört soru, serbest metin cevaplar | 2.1–2.4 |
| C | [6857269313](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6857269313) | 2026-11-13 15:22:05 | "SINAN-CEVAP 2026-11-13 (3)"; maddeler numarasız | 3.2–3.4 |
| D | [6857438754](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6857438754) | 2026-11-13 15:29:36 | "SINAN-CEVAP 2026-11-13 (4)"; #1146 review bulgularından üç soru | 4.1–4.3 |
| E | [6857496471](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6857496471) | 2026-11-13 15:32:07 | "Sinan yanıtları — #1146 üretim girişi maliyet hesaplayıcısı"; aynı metin PR #1146 yorumu `6857500964`'te de var (yalnız başlık ve giriş farklı) | Y.1–Y.5 |
| F | [6859166721](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6859166721) | 2026-11-13 16:44:48 | "SINAN-CEVAP 2026-11-13 (5)" | 5.1–5.3 |
| G | [6859329107](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6859329107) | 2026-11-13 16:51:55 | "SINAN-CEVAP 2026-11-13 (6)"; (5)'in teyidi | 6.1–6.3 |
| H | [6859454003](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6859454003) | 2026-11-13 16:57:24 | "SINAN-CEVAP 2026-11-13 (7)"; sayım fazlası maliyeti | 7.1 |
| I | [6860586175](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6860586175) | 2026-11-13 17:47:44 | "SINAN-KARAR — #1102 resmî satış faturası"; `AskUserQuestion` cevapları | K.1–K.4 |
| NEEDS | [6861267281](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6861267281) | 2026-11-13 18:17:32 | Hurda DUR karşılaştırmasında brüt/net sorusu (`NEEDS-SINAN`) | 8.1 ile kapandı (URT-D44) |
| R8 | [6864631364](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6864631364) | 2026-11-13 20:51:57 | "SINAN-CEVAP (8)"; 16 soru, 4 grup, `AskUserQuestion`; 10. sorunun ilk cevabı geçersiz (R8-EK) | 8.1–8.9, 8.11–8.16 |
| R8-EK | [6873121501](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6873121501) | 2026-11-14 06:28:11 | "SINAN-CEVAP (8) ek — 10. soru yeniden soruldu ve cevaplandı" | 8.10 |

Satır numaraları: 1.n = A'nın n. maddesi; 2.n ve 4.n–7.n = "(2)" ve "(4)"…"(7)" yorumlarının n. maddesi; 3.n = C'deki
cevabın baştaki rakamı (C'nin maddeleri numarasızdır, 3.1 yoktur); Y.n = E'nin n. maddesi; K.n = I tablosunun n.
satırı; 8.n = "(8)" yorumunun n. sorusu (8.10, R8-EK'teki yeniden sorulan sorudur).

## Üretim girişi maliyeti (#1146) — soru ve cevaplar (Issue #66 yorumları 6857438754, 6857496471)

Cevap sütunu Sinan'ın cevabıdır, aynen; "(önerilen)"/"(Önerilen)" işaretleri soranındır. D'de seçenekler soru
metnindedir; E'de seçilmeyen seçenekler kayıtta yoktur.

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Seçenek metni | Seçilmeyenler |
|---|---|---|---|---|
| 4.1 | "Hurda ayrı takip edilirken (anahtar kapalı) ürünün kg maliyeti neye bölünür? Örnek: 1000 kg × 40 TL = 40.000 TL; çıktı 900 kg sağlam + 50 kg 2. kalite + 50 kg hurda; hurda 10 TL/kg → 500 TL hurdaya, 39.500 TL ürüne." | "1-a" | A (önerilen): "çıkan ürüne bölünür → 39.500 / 950 = 41,58 TL/kg (41,578947…); fire ve hurda farkı ürüne biner." Aktaranın notu: "Bu örnek SAT-D316 (3) için golden örnektir." | B: "giren hammaddeye bölünür → 39.500 / 1000 = 39,50 TL/kg." |
| 4.2 | "Hurda değeri hammadde maliyetine eşit ya da fazlaysa?" | "2-a" | A (önerilen): "sonuç sıfır ya da eksi çıkarsa maliyet "hesaplanamadı" sayılır, düzeltilene kadar ortalamaya girmez; hurda fiyatı hammadde kg maliyetini aşıyorsa girişte uyarı verilir." | B: "sıfır maliyet kabul edilir." |
| 4.3 | "Boyama'da boyanın (litre) maliyeti ürüne yüklensin mi?" | "3-a" | A (önerilen): "ilk sürümde hayır, ama emir "boya maliyeti dahil değil" diye açıkça işaretlenir." | B: "evet." |
| Y.1 | "Fire anahtarı kapalıyken kalan maliyet hangi kg'a bölünür?" (örnek: 1000 kg × 40 TL, 900 kg sağlam + 50 kg 2. kalite, 50 kg hurda × 10 TL; hurda gideri 500 TL, kalan 39.500 TL) | "Sağlam + 2. kalite kg (Önerilen)" | Kayıtta yok. Aktaranın notu: "→ 39.500 / 950 = 41,578947… TL/kg. Seçenek metni "Bu yanıt golden örnek sayılır" diyordu." | Kayıtta yok |
| Y.2 | "Hurda değeri hammadde maliyetinin tamamına eşit çıkarsa (ürün maliyeti 0 TL/kg)?" | "Okunamadı sayılsın (Önerilen)" | Kayıtta yok. Aktaranın notu: "→ Sonuç ≤ 0 ise "okunamadı"; ortalama maliyete girmez (SAT-D297 ile aynı mantık)." | Kayıtta yok |
| Y.3 | "Hurda TL/kg fiyatı, emirde kullanılan hammaddenin kg maliyetinden yüksekse?" | "Hesap dursun, fiyat kontrol edilsin (Önerilen)" | Kayıtta yok | Kayıtta yok |
| Y.4 | "Fire anahtarı kapalı, tartılan hurda 0 kg ve o ayın hurda fiyatı girilmemişse?" | "Evet, hesaplansın (Önerilen)" | Kayıtta yok | Kayıtta yok |
| Y.5 | "Boyama'da litreyle izlenen boyanın maliyeti v1'de?" | "Girmesin ama açıkça işaretlensin (Önerilen)" | Kayıtta yok. Aktaranın notu: "→ v1'de boya maliyeti ürüne yüklenmez; emir/maliyet "boya maliyeti dahil değil" diye işaretlenir, sessiz dışlama olmaz." | Kayıtta yok |

## Üretim girişi maliyeti (#1146) — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D326 | Fire anahtarı kapalıyken üretim girişi maliyetinin paydası, sıfır/eksi sonuç, hurda fiyatı sınırı, hurdasız emir (4.1, 4.2, Y.1–Y.4; SAT-D316 (3) ve (6)'yı tamamlar). Hurda DUR karşılaştırmasının tanımı URT-D44'te | (1) **Hurda gideri düşüldükten sonra kalan maliyet sağlam + 2. kalite kg'a bölünür.** Golden örnek (4.1, Y.1): 1.000 kg × 40 TL = 40.000 TL; çıktı 900 kg sağlam + 50 kg 2. kalite + 50 kg hurda; hurda 10 TL/kg → 500 TL; kalan 39.500 TL / 950 kg = **41,578947… TL/kg** (gösterimde 41,58). Bu örnek SAT-D316 (3) ve SAT-D308 uygulama notundaki golden şartını kapalı yol için karşılar; SAT-D316 (3)'teki payda okuması Sinan cevabı olur. (2) **Sonuç sıfır ya da eksi çıkarsa maliyet "hesaplanamadı" ("okunamadı") sayılır; düzeltilene kadar ortalama maliyete girmez** (4.2, Y.2; SAT-D297 ile aynı mantık). (3) **Hurdanın TL/kg fiyatı emirde kullanılan hammaddenin kg maliyetinden yüksekse hesap durur ve fiyat kontrol edilir** (Y.3). "Hammaddenin kg maliyeti"nin tanımı URT-D44'tür (net). (4) **Fire anahtarı kapalı, tartılan hurda 0 kg ve o ayın hurda fiyatı girilmemişse maliyet hesaplanır** (Y.4); fiyat yokluğu bu halde hesabı durdurmaz. Bu, SAT-D316 (6)'daki "fiyat yoksa okunamadı" okumasını hurdası 0 kg olan emir için daraltır. (5) **4.2 ile Y.3 arasındaki fark:** 4.2'nin seçenek metni "hurda fiyatı hammadde kg maliyetini aşıyorsa girişte uyarı verilir" der; Y.3 (daha sonra) "Hesap dursun, fiyat kontrol edilsin" der. İkisi de kayıttadır; **hesaptaki davranışı daha sonraki Y.3 belirler.** Hurda fiyatı giriş ekranının ayrıca uyarı gösterip göstermeyeceği giriş diliminin işidir (**yazar okuması**: uyarı, hesabın durmasına ek olabilir, onun yerine geçmez). (6) Yeni "hesaplanamadı" sebep kodlarının adları uygulama ayrıntısıdır | `ProductionReceiptCostCalculatorV1` (#1146); hurda fiyatı giriş dilimi; üretim maliyeti portu; golden testler |
| SAT-D327 | Boyamada boyanın (litre) maliyeti (4.3, Y.5) | (1) **İlk sürümde (v1) boya maliyeti ürüne yüklenmez.** (2) **Emir ve maliyet sonucu "boya maliyeti dahil değil" diye açıkça işaretlenir; sessiz dışlama olmaz.** (3) Boyama dışındaki kg dışı girdiler URT-D45'tedir. Boya maliyetinin sonraki sürümde yüklenip yüklenmeyeceği sorulmadı. (4) 13 Kasım dosyası Sınırlar'ındaki Boyama maddesinin yalnız boya maliyeti kısmını cevaplar; kütle ölçümü kısmı (URT-D41 (3)) değişmez | #1146 hesaplayıcısı; maliyet sonucu ve emir ekranı |

## Eksi stok ve sayım — soru ve cevaplar (Issue #66 yorumları 6857132760, 6857269313, 6859166721, 6859329107, 6859454003)

Cevap sütunu Sinan'ın cevabıdır, aynen. Seçenekler soru metnindedir ("(A, önerilen)" soranın işaretidir). G'de sorular
soranın bildirdiği özet ve teyit sorusudur.

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Seçenek metni | Seçilmeyenler |
|---|---|---|---|---|
| 2.3 | "Eksiye düşmüş stok kaydı nasıl düzeltilsin: kilitle + listele + ikinci kişi onaylı sayım farkı düzeltmesi (A, önerilen) / sonraki giriş kendiliğinden kapatsın (B)?" | "3-a burda stoktaki hiçbir ürün eksiye düşemez dediğim sevkiyat sırasında düşebilir, kantar farkından dolayı olanlar zaten raporlanacaktı, kantar farkı dışında da eksiye düşecek işlem olursa o anda sistem hangi işlemden dolayı eksiye düşürdüğünü söylesin ve ona göre devam düzeltme yada kodlarda alınması gereken eksik protokol varsa bunu düzeltebilmek için ekisye düşüren işlemin takibi yapılabilmeli" | A: "kilitle + listele + ikinci kişi onaylı sayım farkı düzeltmesi" | B: "sonraki giriş kendiliğinden kapatsın" |
| 3.3 | "kantar farkı dışındaki bir işlem stoğu eksiye düşürecekse işlem durdurulsun ve sebebi gösterilsin mi (A, önerilen), yoksa geçsin ama kaydedilsin mi (B)?" | "3-a" | A: "işlem durdurulsun ve sebebi gösterilsin". Aktaranın notu: "Önceki cevapla birlikte: kantar farkından doğan eksi yalnız sevkiyatta olabilir ve raporlanır; diğer her işlem durdurulur, hangi işlemin eksiye düşüreceği gösterilir ve izlenebilir kaydedilir; düzeltme ikinci kişi onaylı sayım farkı kaydıyladır." | B: "geçsin ama kaydedilsin" |
| 5.1 | "Eksiye düşürmeye çalışan işlem durdurulunca o bobin sayılana kadar kilitlensin mi?" | "1-a" | A (önerilen): "Hayır: işlem durur ve kaydedilir, bobin eksiye düşürmeyen işlemlerde kullanılmaya devam eder (üretimi durdurmaz, eksi zaten engelli)." | B: "Evet: sayım onaylanana kadar o bobinden hiç çıkış yapılamaz." |
| 6.1 | Bildirilen özet: "1-a: Eksiye düşürmeye çalışan işlem durur ve kaydedilir. Bobin kilitlenmez; eksiye düşürmeyen işlemlerde kullanılmaya devam eder." | "1-evet doğru" | Kayıtta yok (teyit) | Kayıtta yok |
| 5.2 | "Sistemde 0 görünen ama fiziksel olarak duran bobin, ikinci kişinin onayladığı sayımla sisteme geri eklenebilsin mi? Bugün 0 stoklu bobin sayılamıyor." | "2-a" | A (önerilen): "Evet, onaylı sayım fazlası olarak eklenir ("düzeltme sayımla" kararını tamamlar; maliyet için mevcut kural kullanılır, kural yoksa ayrıca sorulur)." | B: "Hayır, önce hatalı işlem ters kayıtla düzeltilir." |
| 6.2 | Bildirilen özet: "2-a: Sistemde 0 görünen bobin sayılabilecek. İkinci kişinin onayladığı sayım fazlası onu stoka geri ekler. Bu fazlanın maliyeti için mevcut bir kural yoksa bunu ayrıca soracağım." | "2- alış maliyeti ile eklensin yada en son bitmeden önceki maliyeti ne ise" | Kayıtta yok. Aktaranın notu: "Mevcut kural yok: sayım düzeltmesi bugün yalnız miktar hareketi, ortalama maliyet defterinde sayım hareketi türü yok." | Kayıtta yok |
| 7.1 | "Sistem maliyeti bobin başına tutmuyor; her ürün için, her depoda ayrı bir ortalama alış maliyeti tutuluyor. "alış maliyeti ile eklensin yada en son bitmeden önceki maliyeti ne ise" cevabı iki şekilde okunabiliyor:" | "A yani önerilen" | A (önerilen): "Sayım fazlası o ürünün o depodaki ortalama alış maliyetiyle eklenir, ortalama değişmez. Stok sıfırsa, sıfıra düşmeden önceki son ortalama kullanılır (SAT-D296 ile aynı mantık). Ortalama okunamıyorsa maliyet "hesaplanamadı" kalır, tahmin edilmez." İki seçenekte ortak: "İki durumda da kural yalnız 0 görünen bobinde değil, bütün sayım fazlalarında geçerli olur. İş, ortalama maliyet defteri stok hareketlerine bağlandıktan sonra yapılacak." | B: "Bobin, kendi alış faturasındaki fiyatla eklenir ve ortalama bu fiyatla yeniden hesaplanır. Üretimden gelen bobinin faturası olmadığı için orada maliyet "hesaplanamadı" kalır." |

## Eksi stok ve sayım — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D328 | Eksiye düşürecek işlem: kaynak, durdurma, gösterme, izleme, düzeltme (2.3, 3.3; SAT-D318 ve SAT-D319'u tamamlar) | (1) **Kantar farkından doğan eksi yalnız sevkiyatta olabilir ve raporlanır** (SAT-D319 (3)–(4) aynen; stok 0'da kalır, fark kantar farkı kaydıyla tamamlanır). (2) **Kantar farkı dışındaki bir işlem stoğu eksiye düşürecekse işlem durdurulur ve sebebi gösterilir** (3.3 "3-a"). (3) **Sistem o anda hangi işlemin stoğu eksiye düşüreceğini söyler.** (4) **Eksiye düşürmeye çalışan işlem izlenebilir biçimde kaydedilir;** amaç, devam/düzeltme kararını ve kodda eksik bir protokol varsa onun düzeltilmesini sağlamaktır (2.3 serbest metin). Kaydın okunması, saklanması ve sayımla bağı SAT-D337…SAT-D339'dadır; mesajdaki kg SAT-D336'dadır. (5) **Düzeltme ikinci kişinin onayladığı sayım farkı kaydıyla yapılır** (2.3 "3-a"; 21 Ekim No 60 "maker ≠ approver" ve `DEC-STK-007` ile aynı yön). (6) 2.3'ün A seçeneğindeki "kilitle" SAT-D329 ile daraltıldı: bobin kilitlenmez | Stok hareket yazma yolu (bütün modüllerin stok çıkışları); eksi stok deneme kaydı dilimi; sayım dilimi |
| SAT-D329 | Durdurulan işlemden sonra bobin kilidi (5.1, 6.1) | **Bobin kilitlenmez.** Eksiye düşürmeye çalışan işlem durur ve kaydedilir; bobin eksiye düşürmeyen işlemlerde kullanılmaya devam eder (üretimi durdurmaz, eksi zaten engelli) | Stok hareket yazma yolu; sayım dilimi |
| SAT-D330 | Sistemde 0 görünen bobinin sayımı (5.2, 6.2) | (1) **Sistemde 0 görünen ama fiziksel olarak duran bobin sayılabilir; ikinci kişinin onayladığı sayım fazlası onu stoğa geri ekler** ("düzeltme sayımla" kararını tamamlar). (2) Bugün 0 stoklu bobin sayılamıyor (soru metni); bu kod sayım diliminde değişir. (3) Sayım fazlasının maliyeti SAT-D331'dedir | Sayım dilimi (sayım adayı listesi, onaylı sayım fazlası hareketi) |
| SAT-D331 | Sayım fazlasının maliyeti (6.2, 7.1) | (1) **Sayım fazlası o ürünün o depodaki ortalama alış maliyetiyle eklenir; ortalama değişmez.** (2) **Stok sıfırsa sıfıra düşmeden önceki son ortalama kullanılır** (SAT-D296 ile aynı mantık). (3) **Ortalama okunamıyorsa maliyet "hesaplanamadı" kalır, tahmin edilmez.** (4) Kural yalnız 0 görünen bobinde değil, **bütün sayım fazlalarında** geçerlidir. (5) İş, ortalama maliyet defteri stok hareketlerine bağlandıktan sonra yapılır. (6) Sayım eksiğinin maliyeti 7.1'de seçilmedi; 8.2'de cevaplandı (SAT-D334) | Ortalama maliyet defteri (sayım hareketi türü); sayım dilimi |

## Satışta teslim toleransı ve %0,5 onayı (#1151) — soru ve cevaplar (Issue #66 yorumu 6856195059)

Cevap sütunu Sinan'ın seçtiği seçenek etiketidir, aynen; "Seçenek metni" seçeneğin açıklamasıdır. Seçilmeyen
seçenekler bu kayıtta yoktur. #1151, 13 Kasım dosyasını ekleyen PR'dır.

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Seçenek metni | Seçilmeyenler |
|---|---|---|---|---|
| 1.5 | "Satışta kantar fazlası aynı zamanda sipariş satırında %3 üstü fazla teslimse (örn. sipariş 1.000 kg, kantar 1.035 kg) sevkiyat bloklansın mı? Belge bu kesişimi açık bırakıyor." | "Evet, bloklanır (Önerilen)" | "Teslim toleransı (SAT-D5/D16) aynen geçerli kalır: sipariş miktarının %3 üstü sevk bloklanır. Kaldırılan yalnız teorik ağırlık ↔ kantar bant bloğudur." | Kayıtta yok |
| 1.6 | "Fiziksel stok yeterliyken sistem stoğu yetersiz görünürse (%0,5 onayı hali) sen cevap verene kadar kod ne yapsın?" | "Kayıtlı karardaki gibi geçer (Önerilen)" | "Satış geçer, onay istenmez (SAT-D322 (1) metni). Tam %0,5 hali cevap gelene kadar dilime alınmaz; sorulmamış bir onay fiilen etkinleşmez." | Kayıtta yok |

## Satışta teslim toleransı ve %0,5 onayı (#1151) — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D332 | Kantar fazlası ile sipariş satırı teslim toleransının kesişimi (1.5; SAT-D324'ü tamamlar) | (1) **Teslim toleransı (SAT-D5/SAT-D16) aynen geçerli kalır: sipariş miktarının %3 üstü sevk bloklanır** (örnek: sipariş 1.000 kg, kantar 1.035 kg → blok). (2) SAT-D324 ile kaldırılan **yalnız teorik ağırlık ↔ kantar bant bloğudur**; sipariş satırına göre teslim toleransı ondan ayrı bir kontroldür. (3) 13 Kasım dosyası 9. tur Sınırlar'ındaki satış teslim toleransı satırının yönü böylece Sinan cevabı olur | Satış sevk kontrolü; Lojistik sevk/irsaliye kontrolü |
| SAT-D333 | Fiziksel stok yeterliyken sistem stoğu yetersiz görünürse geçici davranış (1.6) | (1) **Satış geçer, onay istenmez** (SAT-D322 (1) metni). (2) Seçenek metnindeki "Tam %0,5 hali cevap gelene kadar dilime alınmaz" cümlesi bu halde %0,5 onayının uygulanmasıdır (**yazar okuması**: "tam" burada sınır değerini değil, onayın tamamını anlatır; sınır değeri SAT-D341'dedir). (3) **8.11 ile kalıcı oldu (SAT-D342)**: bu halde onay gerekmez | Satış kantar farkı dilimi |

## #1091 çıktı sınıfı eşlemesi — soru ve cevaplar (Issue #66 yorumları 6859166721, 6859329107)

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Seçenek metni | Seçilmeyenler |
|---|---|---|---|---|
| 5.3 | "#1091: Üretim çıktısının (sağlam / 2. kalite / hurda) stok etiket sınıfına eşlenmesi. Plan "şirket başına ayarlanabilir tablo" diyor, PR ise onaylı değerleri koda sabit yazmış. İnceleyici bu yüzden durdurdu." | "3-şirket başına ayarlı derken burayı anlamadım, üretimden çıkacak ürünler standart ürün varsayılan olur, eğer 2.kalite ve fire hurdaysa bunlar varsayılan standart değiştirilierek hangi ürün çıkıyorsa o değiştirilir" | A (önerilen): "İlk sürümde onaylı değerlerle sabit kalsın, tablo sonraki dilimde gelsin (hızlı, değerler zaten onaylı)." B: "Şimdi tabloya taşınsın (daha çok iş, her şirket için kurulum gerekir)." Cevap iki seçenekten birini seçmedi; 6.3 teyit sorusu soruldu | Cevap A/B seçmedi (6.3'e bakınız) |
| 6.3 | Teyit sorusu: "Üretim çıkış kaydında varsayılan 'standart ürün' gelir. 2. kalite veya hurda çıkarsa kullanıcı onu seçer, stok etiketi de seçilen türle açılır. Bu eşleme her şirkette aynıdır, ayrı bir ayar ekranı gerekmez. Doğru mu?" | "3- her şirkette aynı" | Kayıtta yok (teyit) | Kayıtta yok |

## #1091 çıktı sınıfı eşlemesi — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URT-D43 | Üretim çıktı sınıfı → stok etiket sınıfı eşlemesi (5.3, 6.3) | (1) **Eşleme sabittir ve her şirkette aynıdır; şirket başına ayar tablosu veya ayar ekranı yoktur.** (2) **Üretim çıkış kaydında varsayılan sınıf standart ürün (`GoodProduct`) gelir; 2. kalite veya hurda çıkarsa kullanıcı onu seçer ve stok etiketi seçilen sınıfla açılır.** (3) Plan §5.3a'daki "eşleme yapılandırılabilir 1:1 tablo olur, koda gömülmez" ifadesinin ve "Kurallar"daki şirket başına tablo ifadesinin yerine geçer; tablodaki değerler (OC-1, OC-2, OC-4, OC-5) değişmez. Plan bu PR'da güncellendi. (4) Eşlemenin hangi modülde tutulacağı, eşleme yoksa fail-closed davranış ve ekranın varsayılanı önceden seçmesi uygulama ayrıntısıdır. (5) BS-5 ve BS-14 (`SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md`) değişmez | #1091; `docs/delivery/URETIM_HAZIRLIK_PLANI_v1.md` §5.3a; üretim çıkış ekranı |

## Kasa (#1109) — soru ve cevaplar (Issue #66 yorumları 6856195059, 6857132760, 6857269313)

Cevap sütunu Sinan'ın cevabıdır, aynen. 1.n satırlarında "Seçenek metni" seçeneğin açıklamasıdır; seçilmeyen
seçenekler kayıtta yoktur.

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Seçenek metni | Seçilmeyenler |
|---|---|---|---|---|
| 1.1 | "Kasa izinleri (finance.cash-account.propose / approve) banka hesabı izinleri gibi Critical sayılsın mı? PR şu an Critical işaretlemiyor; banka eşleri Critical." | "Evet, Critical (Önerilen)" | "Kasa öner/onay izinleri banka ile aynı korumayı alır. Yazar şeridi PR'da işaretler; grant yine senin kararınla olur." | Kayıtta yok |
| 1.2 | "FNS-D24'teki "TL/USD/EUR kasaları olur, ayarlanabilir" hangi anlama gelsin? Kod şu an Ayarlar'da tanımlı her para birimi adayını kabul ediyor." | "Ayarlar'daki her para birimi (Önerilen)" | "Kodun bugünkü hali. Ayarlar'a eklenen her para birimi için kasa açılabilir; TL/USD/EUR başlangıç setidir." | Kayıtta yok |
| 1.3 | "PR plan satırındaki "öner/onay/liste"nin ötesinde üç şey ekliyor: ayrı ret komutu, reddin onay yetkisiyle yapılması, aynı şirkette canlı kasa adının tekil olması. Bunlar kalsın mı?" | "Üçü de kalsın (Önerilen)" | "Geri alınabilir seçimler olarak kabul; yazar PR gövdesine ASSUMPTION kimliğiyle yazar ve "banka emsali" gerekçesini düzeltir." | Kayıtta yok |
| 1.4 | "İki inceleme de P0/P1 bulmadı; eksikler şunlar: (1) "okuma ile yazma arasında yetki geri alınırsa" testi kasa için yok, (2) kendi önerisini onaylama denemesi iz bırakmıyor. Merge'den önce hangileri şart olsun?" | "Yalnız test şart (Önerilen)" | "Yetki-kaybı testi bu PR'a eklenir (K3 iskeleti zorunlu sayıyor). İz bırakmama banka ile aynı davranış; ikisi birlikte ayrı işte düzeltilir." | Kayıtta yok |
| 2.2 | "Kasa hesabını pasife alma ayrı dilim mi (A, önerilen), #1109 içinde mi (B)?" | "2-b, amaç bu işte bitsin" | B: "#1109 içinde" | A: "ayrı dilim" |
| 3.2 | Sorulan öneriler: "(1) Kim pasife alır: kasa açmadaki gibi biri önerir, başka biri onaylar. (2) Bakiyesi sıfır olmayan kasa pasife alınamaz; önce bakiye sıfırlanır. (3) Pasif kasaya yeni hareket yazılamaz; geçmişi ve raporları görünür kalır. (4) Yeniden açma aynı iki kişili onayla mümkündür." | "2- kasa -evet önerilerini uygula" | Öneriler soru metnindedir | Kayıtta yok |

## Kasa (#1109) — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D33 | Kasa izinleri, para birimi, ek seçimler, merge şartı (1.1–1.4; FNS-D24'ü açıklar) | (1) **Kasa öner/onay izinleri (`finance.cash-account.propose` / `approve`) banka hesabı izinleri gibi Critical'dır**; işaretlemeyi yazar şeridi kodda yapar, **grant Sinan'ın kararıyla olur** (insan kapısı). (2) **FNS-D24'teki "ayarlanabilir": Ayarlar'a eklenen her para birimi için kasa açılabilir; TL/USD/EUR başlangıç setidir** (kodun bugünkü hali). (3) **Ayrı ret komutu, reddin onay yetkisiyle yapılması ve aynı şirkette canlı kasa adının tekil olması kalır;** geri alınabilir seçimlerdir, yazar PR gövdesine `ASSUMPTION-*` kimliğiyle yazar ve "banka emsali" gerekçesini düzeltir. (4) **Merge'den önce yalnız yetki-kaybı testi ("okuma ile yazma arasında yetki geri alınırsa") şarttır;** kendi önerisini onaylama denemesinin iz bırakmaması banka ile aynı davranıştır ve ikisi birlikte ayrı işte düzeltilir | #1109; Finans izin manifesti; banka/kasa iz eksiği için ayrı iş |
| FNS-D34 | Kasa hesabını pasife alma (2.2, 3.2) | (1) **Pasife alma #1109 içinde yapılır** ("amaç bu işte bitsin"). (2) **Kim:** kasa açmadaki gibi biri önerir, başka biri onaylar. (3) **Bakiyesi sıfır olmayan kasa pasife alınamaz; önce bakiye sıfırlanır.** (4) **Pasif kasaya yeni hareket yazılamaz; geçmişi ve raporları görünür kalır.** (5) **Yeniden açma aynı iki kişili onayla mümkündür.** (6) C'deki "Yeni yetki adları kodda Critical işaretlenir; kimseye verilmez" cümlesi aktaranın notudur; Critical işaretlemesi FNS-D33 (1) ile aynı yöndedir (**yazar okuması**), grant insan kapısıdır | #1109; kasa ekranları |

## Resmî satış faturası (#1102) — soru ve cevaplar (Issue #66 yorumu 6860586175)

Kayıt yalnız konu başlığını ve cevabı içerir; "Seçenek metni", yorumdaki "Seçeneklerin soruda yazdığı anlam"dır (yalnız
K.2 ve K.4 için yazılı). Seçilmeyen seçenekler kayıtta yoktur.

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Seçenek metni | Seçilmeyenler |
|---|---|---|---|---|
| K.1 | Konu: "PDF sahibi (owner kind)" | "Önce finance.* yapılsın (Önerilen)" | Kayıtta yok | Kayıtta yok |
| K.2 | Konu: "Aynı PDF'in ikinci kayıtta kullanılması" | İlk yanıt: "Bunu aslında detaylıca araştırırmısın muhasebe finans kullanıcı yüzü açısıdan". Araştırmadan sonraki yanıt: "Reddedilsin, ilk kayıt gösterilsin (Önerilen)" | "İkinci kayıt 422 ile reddedilir. Ekranda "Bu PDF şu resmî fatura kaydında kullanılmış: &lt;numara&gt;" yazar. (CompanyId, PDF SHA-256) üzerinde tekil indeks olur; birleşik/toplu PDF de yakalanır. Baytları farklı ama yanlış bir PDF'i yine yalnız onaylayanın gözle kontrolü yakalar." | Kayıtta yok |
| K.3 | Konu: "Para birimi doğrulaması" | "Evet, Ayarlar adaylarıyla (Önerilen)" | Kayıtta yok | Kayıtta yok |
| K.4 | Konu: "PDF ile kayıt sırası" | "FNS-D17 aynen, PDF önce (Önerilen)" | "Tek "Kaydet" düğmesi vardır. PDF önce yeni kaydın kimliğine yüklenir, kayıt hemen ardından oluşur. Teknik yolu (önceden ayrılmış kimlik ya da geçici giriş zarfı) şerit seçer ve K3 review'unda doğrulanır. İşlem yarıda kalırsa sahipsiz bir PDF kalabilir; o da 10 yıl saklanır, silinmez." | Kayıtta yok |

## Resmî satış faturası (#1102) — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D35 | Resmî satış faturası PDF'inin sahibi (K.1; FNS-D18'i uygular) | **PDF'in sahibi önce Finans (`finance.*` sahip türü) yapılır.** Sahip türünün adı, adaptörü ve ara sahipliğin kalkması I'daki "Okumam"dır, Sinan'ın sözü değildir; kod PR'ı ve K3 review'u belirler (Documents sözleşmesine dokunduğu için cross-module contract) | #1102; Documents sahip bağlantı portu |
| FNS-D36 | Aynı PDF'in ikinci resmî fatura kaydında kullanılması (K.2) | (1) Sinan önce araştırma istedi; araştırmadan sonra: **ikinci kayıt reddedilir, ilk kayıt gösterilir.** (2) Seçenek metnine göre: **ikinci kayıt 422 ile reddedilir; ekranda "Bu PDF şu resmî fatura kaydında kullanılmış: &lt;numara&gt;" yazar; (CompanyId, PDF SHA-256) üzerinde tekil indeks olur; birleşik/toplu PDF de yakalanır.** (3) Baytları farklı ama yanlış bir PDF'i yine yalnız onaylayanın gözle kontrolü yakalar. (4) Reddedilen veya geri çekilen kaydın PDF'inin yeniden kullanılması FNS-D39'dadır | #1102; Finans resmî fatura tablosu (migration, K3) |
| FNS-D37 | Resmî satış faturasında para birimi doğrulaması (K.3) | **Para birimi Ayarlar'daki adaylara karşı doğrulanır.** Banka hesaplarındaki örüntünün kullanılması I'daki "Okumam"dır | #1102 |
| FNS-D38 | PDF ile kaydın sırası (K.4; FNS-D17'yi aynen uygular) | (1) **FNS-D17 aynen: PDF önce.** (2) **Tek "Kaydet" düğmesi vardır; PDF önce yeni kaydın kimliğine yüklenir, kayıt hemen ardından oluşur.** (3) **Teknik yolu (önceden ayrılmış kimlik ya da geçici giriş zarfı) şerit seçer ve K3 review'unda doğrulanır.** (4) **İşlem yarıda kalırsa sahipsiz bir PDF kalabilir; o da 10 yıl saklanır, silinmez** | #1102; Documents yükleme akışı |

## Yükleme talimatı iptali (#1147) — soru ve cevaplar (Issue #66 yorumları 6856195059, 6857132760, 6857269313)

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Seçenek metni | Seçilmeyenler |
|---|---|---|---|---|
| 1.7 | "Yükleme talimatı iptalinde "yüklenmiş ama sevk edilmemiş" miktarın serbest bırakıldığı okuması #66'ya kayıt olarak yazılsın mı? Ayrıca iptal izni ön yüz dilimi merge olmadan kimseye verilmesin mi?" | "İkisi de evet (Önerilen)" | "Okuma #66'ya senin onayınla yazılır; iptal izni ön yüz hazır olana kadar grant edilmez." | Kayıtta yok |
| 2.4 | "#1147'deki "yüklenmiş ama sevk edilmemiş miktar iptalde serbest bırakılır" okuması #66'ya yazılsın mı?" | "4-evet ama bunu anlamadım, bana daha detaylı anlatırmısın ve mantıklı olan neyse" | Kayıtta yok. Aktaranın notu (kayıt): "yükleme talimatı iptal edildiğinde, talimat için ayrılmış miktarın sevk edilmemiş kısmı (araca yüklenmiş ama irsaliyesi kesilmemiş olan dahil) serbest bırakılır; sevk edilmiş miktara dokunulmaz. Ön yüz dilimine kadar iptal yetkisi kimseye verilmez (önceki cevap 7)." | Kayıtta yok |
| 3.4 | Kayıtta yok (yorumda yalnız konu başlığı: "Yükleme talimatı iptali") | "4-Henüz araca yüklenmemiş kısım: serbest kalır, yükeleme talimatı iptal edilirse diğer satışlara satılabilir." | Kayıtta yok | Kayıtta yok |

## Yükleme talimatı iptali (#1147) — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| LOJ-D15 | Yükleme talimatı iptalinde ayrılmış miktarın serbest bırakılması ve iptal izni (1.7, 2.4, 3.4; F012–F020 belgesindeki serbest bırakma satırının sorulan kısmını cevaplar) | (1) **Yükleme talimatı iptal edildiğinde talimat için ayrılmış miktarın sevk edilmemiş kısmı serbest bırakılır; araca yüklenmiş ama irsaliyesi kesilmemiş olan da buna dahildir. Sevk edilmiş miktara dokunulmaz** (1.7, 2.4). (2) **Henüz araca yüklenmemiş kısım serbest kalır ve diğer satışlara satılabilir** (3.4). (3) **İptal izni ön yüz dilimi merge olana kadar kimseye grant edilmez** (1.7); grant zaten insan kapısıdır. (4) Araca yüklenmiş malın fiziksel olarak indirilmesi veya yeniden tartılması sorulmadı (`NEEDS-SINAN`, Sınırlar) | #1147 sonrası Lojistik iptal akışı; Satış tahsis/rezerv portu; Lojistik ön yüz dilimi |

## DELTA-OK imzacısı (#1078) — soru ve cevaplar (Issue #66 yorumları 6856195059, 6857132760)

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Seçenek metni | Seçilmeyenler |
|---|---|---|---|---|
| 1.8 | "Verdiğim DELTA-OK'i, o PR'ın K3-REVIEW-A/B incelemelerini yazmamış bir oturum olarak imzaladım (GOV-D1 imzacının A/B yazarlarından biri olmasını istiyor). Ayrıca PR head'i yeniden ilerledi (5bcf5e22 = main 330de202 merge + yeniden envanter). Nasıl ilerleyelim?" | "Hangisi mantıklı ve hızlandıracak ise" (serbest metin) | Kayıtta yok | Kayıtta yok |
| 2.1 | "Main birleştirmesinden gelen mekanik farkta (çakışmasız main merge + üreticiyle yeniden pin) tek bağımsız `DELTA-OK` yeter mi (A, önerilen), yoksa her seferinde iki tam inceleme mi (B)?" | "1-a" | A: "tek bağımsız `DELTA-OK`". Aktaranın notu: "Kapsam: yalnız mekanik fark; iş mantığı, migration, para, yetki, sözleşme, `.github/**` değişikliğinde K3 iki tam inceleme şartı aynen sürer. İmzalayan bağımsız inceleme oturumudur; yazar/merge eden kendi farkını imzalayamaz." | B: "her seferinde iki tam inceleme" |

## DELTA-OK imzacısı (#1078) — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| GOV-D6 | Main birleştirmesinden gelen mekanik farkta `DELTA-OK` (2.1, 1.8; GOV-D1 ile aynı yön) | (1) **Main birleştirmesinden gelen mekanik farkta (çakışmasız main merge + üreticiyle yeniden pin) tek bağımsız `DELTA-OK` yeter; her seferinde iki tam inceleme gerekmez** (2.1 "1-a"). Bu, GOV-D1'in (1) ve (2) numaralı mekanik sınıflarıyla aynı yöndedir. (2) Kapsam yalnız mekanik farktır; iş mantığı, migration, para, yetki, sözleşme veya `.github/**` değişikliğinde K3'teki iki tam inceleme şartı aynen sürer (2.1 aktaranın notu; GOV-D1 ile tutarlı). (3) Yazar veya merge eden kendi farkını imzalayamaz. (4) **İmzacının A/B review'larından birinin yazarı olması gerekmez; bağımsız bir inceleme oturumu imzalayabilir — bu yazar okumasıdır, Sinan'ın sözü değildir.** Dayanağı: 1.8'de Sinan seçimi "Hangisi mantıklı ve hızlandıracak ise" diyerek bıraktı; A'daki inceleyici okuması en hızlı seçeneği bu yönde okudu ve "merge eden bunu açık bir kullanıcı kararı gibi değil, okuma olarak değerlendirsin" dedi; 2.1'in aktaranın notu da "İmzalayan bağımsız inceleme oturumudur" der. Sinan farklı isterse tek satırla kesinleştirir ya da geri alır. (5) GOV-D1 dosyası, `AGENTS.md` ve `.claude/skills/review-signatures/SKILL.md`'deki imzacı cümlesi bu PR'da değişmez; hizalama ayrı bir governance PR'ının işidir (Sınırlar) | K3 PR'larının main resync akışı; `DELTA-OK` imzası |

## SINAN-CEVAP (8) — soru ve cevaplar (Issue #66 yorumları 6864631364, 6873121501)

Cevap sütunu Sinan'ın seçtiği seçenek etiketidir, aynen; "(Önerilen)" işaretleri soranındır; "Seçenek metni" soruda
Sinan'a gösterilen açıklamadır. Seçilmeyen seçeneklerin her biri açıklamasıyla yazılıdır. Sinan 16 sorunun hepsinde
önerilen seçeneği seçti. 10. sorunun 13 Kasım'daki ilk cevabı geçersizdir (soran seçeneğin gerekçesini yanlış yazmıştı);
8.10 satırı 14 Kasım'da düzeltilmiş metinle yeniden sorulan sorudur (R8-EK).

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Seçenek metni | Seçilmeyenler |
|---|---|---|---|---|
| 8.1 | "#1146 üretim maliyeti: Kararınıza göre hurdanın TL/kg fiyatı hammaddenin kg maliyetinden yüksekse hesap DURUR. Bu karşılaştırmada hammaddenin kg maliyeti nasıl hesaplansın? Örnek: 500 kg @40 TL ve 500 kg @50 TL çıkış yapıldı (brüt 45,00 TL/kg). Sonra 50 TL'lik partiden 400 kg iade geldi; net 25.000 TL / 600 kg = 41,67 TL/kg. 43 TL/kg hurda fiyatı brüt hesapta geçer, net hesapta DURUR." | "Net (Önerilen)" | "Maliyet, iadeler düşüldükten sonra tüketilen kg'a bölünür. Ürün maliyeti de net hesaplandığı için karşılaştırma aynı temelde yapılır." | "Brüt" — "İadeler düşülmez: bütün çıkışların maliyeti, çıkış kg'ına bölünür. Bugünkü kod böyle çalışıyor." |
| 8.2 | "Stok sayımında sistemdekinden daha az kg bulunursa ve bunu ikinci bir kişi onaylarsa, eksik kg stoktan hangi maliyetle düşülsün? (Sayım fazlası için verdiğiniz karar: fazla kg, o depodaki o ürünün ortalama alış maliyetiyle eklenir; ortalama değişmez.)" | "Ortalama maliyetle (Önerilen)" | "Eksik kg, o depodaki o ürünün güncel ortalama maliyetiyle düşülür; ortalama değişmez. Ortalama okunamazsa sistem 'hesaplanamadı' der ve düşüm durur." | "Son alış fiyatıyla" — "Eksik kg son alış fiyatıyla düşülür; kalan stokun ortalaması yeniden hesaplanır." · "Şimdilik açık kalsın" — "Karar sonraya kalır. O zamana kadar sayım eksiği maliyetiyle birlikte düşülemez." |
| 8.3 | "#1107 stok: Kuralınız 'hiçbir ürün eksiye düşemez'. Yine de eski bir veri aktarımı ya da bir hata yüzünden bir ürünün bir depodaki kaydı eksi görünürse ne olsun? (Bugünkü kod bu durumda işlemi durduruyor.)" | "Dursun, sayımla düzelsin (Önerilen)" | "O ürünün o depodaki maliyeti 'hesaplanamadı' olur; maliyete ihtiyacı olan işlemler sebebi gösterilerek durur. Düzeltme, ikinci kişinin onayladığı sayım farkıyla yapılır. Sonradan gelen bir giriş tek başına düzeltmez." | "Giriş gelince düzelsin" — "Sonraki giriş eksiyi kapatır, maliyet kendiliğinden yeniden okunur. Not: 13 Kasım'da 'giriş gelince düzelir' cümlesini geçersiz saymıştınız." |
| 8.4 | "#1102 Finans, elle girilen resmî satış faturası: Onay bekleyen yanlış bir kayıt bugün ne reddedilebiliyor ne de geri çekilebiliyor. Numara, ETTN ve PDF tekil olduğu için doğrusu da girilemiyor. Ne olsun?" | "Ret ve geri çekme (Önerilen)" | "Onaylayan kişi gerekçe yazarak reddedebilir; giren kişi kendi bekleyen kaydını geri çekebilir. Hiçbir şey silinmez, iz kalır. Numara, ETTN ve PDF yeniden kullanılabilir olur." | "Yalnız onaylayan reddetsin" — "Giren geri çekemez; yalnız onaylayan gerekçe yazarak reddeder. Reddedilen kaydın numarası, ETTN'si ve PDF'i serbest kalır." · "Şimdilik açık kalsın" — "Yanlış bekleyen kayıt olduğu gibi kalır; bu dilimde ret ya da geri çekme açılmaz." |
| 8.5 | "Eksi stok: Kurala göre stoğu eksiye düşürecek bir işlem durdurulur, ekranda gösterilir ve kaydedilir. Kullanıcıya gösterilen mesajda eldeki kg da yazsın mı? Örnek: 'Sistemde 480 kg var, 500 kg istendi.'" | "Evet, göster (Önerilen)" | "Mesajda eldeki ve istenen kg birlikte yazar. Kullanıcı farkı hemen görür ve sayım isteyebilir." | "Hayır, yalnız 'stok yetersiz'" — "Mesajda miktar yazmaz, yalnız 'stok yetersiz' der. Miktarı görmek isteyen stok ekranına bakar." |
| 8.6 | "Eksi stok: Durdurulan her işlem için bir deneme kaydı tutulur (kim, ne zaman, hangi ürün ve depo, kaç kg). Bu kayıtları kim görebilsin?" | "Stok raporu yetkilileri (Önerilen)" | "Stok raporlarını görme yetkisi olanlar görür; yeni yetki açılmaz. Bu yetkinin kime verileceğine yine siz karar verirsiniz." | "Yeni ayrı yetki" — "Yalnız bu kayıtlar için yeni bir yetki tanımlanır. Yetki kimseye verilmeden gelir; kime verileceğine siz karar verirsiniz." |
| 8.7 | "Eksi stok: Durdurulan işlemlerin deneme kayıtları ne kadar süre saklansın?" | "Denetim kaydıyla aynı (Önerilen)" | "Denetim kayıtları gibi silinmez. İleride ayrı bir saklama ve imha politikasına karar verirseniz ona uyar." | "Süreli saklansın" — "Süresini sizin belirleyeceğiniz bir zaman sonra imha edilir." |
| 8.8 | "Eksi stok: Kurala göre eksi stok, ikinci kişinin onayladığı sayım farkıyla düzeltilir. Durdurulan işlemin deneme kaydı bu düzeltme sayımıyla nasıl ilişkilendirilsin?" | "Sayıma bağla, görev aç (Önerilen)" | "Düzeltme sayımı onaylanınca o ürün ve depodaki açık denemeler 'çözüldü' görünür. Görev kutusu modülü devreye girdiğinde 'sayım gerekli' görevi de oluşur." | "Yalnız sayıma bağla" — "Denemeler sayımla 'çözüldü' görünür; görev açılmaz." · "Bağlantı olmasın" — "Deneme kaydı yalnız bilgi için durur; sayımla kendiliğinden ilişkilendirilmez." |
| 8.9 | "Alış/mal kabul: Teorik ağırlık ile kantar arasındaki fark için eski bantlar var: uyarı %1, onay %2, blok %3 (geçici değerler, Ayarlar'dan değişir). Satışta bu bantların yalnız kaydedilip raporlanmasına karar verdiniz. Mal kabulde bu bantlar nasıl çalışsın?" | "Mal kabulde aynen kalsın (Önerilen)" | "Mal kabulde fark %1'de uyarı, %2'de onay ister, %3'te bloklanır; 21 Ekim kararlarında yazıldığı gibi. Tedarikçiden eksik ya da fazla gelen mal kontrolsüz girmez." | "Satıştaki gibi yalnız kayıt" — "Mal kabulde de fark yalnız kaydedilir ve raporlanır. Mal kabul durmaz, onay istenmez." |
| 8.10 | (R8-EK, yeniden soruldu) "%0,5 kantar farkı sınırı (SAT-D321). Fark tam %0,5 olursa ne olsun? Örnek: sistemde 1.000 kg, kantarda 1.005 kg." | "Tam %0,5 onaya (Önerilen)" | "Tam %0,5 fark onaya düşer, %0,5'in altı onaysız geçer. Bu, projedeki diğer eşiklerle aynıdır: SAT-D15 ("Bir sınır değeri kendi başlattığı banda girer"), 21 Ekim K4 `AtOrAbove`, 21 Ekim No 40. Sınırda olan kayıt sıkı tarafa düşer." | "Sınır dahil, geçsin" — "tam %0,5 fark onaysız geçer, yalnız %0,5'i aşan fark onaya düşer. SAT-D321'deki "%0,5 üstü" ifadesinin kelimesi kelimesine okunuşudur ve diğer eşiklerden farklı davranır." |
| 8.11 | "Satışta kantar farkı: Stok 1.000 kg; A siparişine 600 kg, B siparişine 400 kg ayrılmış. A'nın kantarı 606 kg çıktı (%1 fazla). Fiziksel stok yettiği için kantar farkı kaydı açılmıyor; kararınıza göre eksik, B satılırken görülüyor. Bu durumda A satışı için Satış Müdürü onayı gereksin mi?" | "Hayır, onay gerekmesin (Önerilen)" | "Onay yalnız sistemdeki stoktan fazla çıkan kg için istenir (9. turda seçtiğiniz seçeneğin metni). A geçer; B'nin eksiği B satılırken kantar farkı kuralına girer." | "Evet, onay gereksin" — "Fark %0,5'i aştığı için stok yetse de A satışı Satış Müdürü onayını bekler." |
| 8.12 | "Satışta kantar farkı irsaliye toplamına göre ölçülüyor (kararınız). Bir üründe fazla, başka üründe eksik çıkarsa farklar birbirini götürsün mü? Örnek: A ürünü sistemde 500 kg, kantarda 508 kg (stoktan 8 kg fazla). B ürünü sistemde 500 kg, kantarda 494 kg (6 kg eksik). İrsaliye toplamı 1.000 kg." | "Hayır, yalnız fazlalar (Önerilen)" | "Yalnız stoktan fazla çıkan kg toplanır: 8 kg, yani %0,8; Satış Müdürü onayı ister. Farklı ürünlerin farkları birbirini kapatmaz." | "Evet, net fark" — "Fazla ve eksik netleşir: 8 − 6 = 2 kg, yani %0,2; satış kendiliğinden geçer." |
| 8.13 | "#1146 üretim maliyeti: Boya için kararınız şu: maliyeti ilk sürümde ürüne yüklenmez, sonuçta 'boya maliyeti dahil değil' yazar. Boyama dışında, kg ile değil adet, litre ya da metreyle tüketilen başka bir girdi olursa ne olsun? Bugünkü kod bu durumda o emrin maliyetini 'hesaplanamadı' bırakıyor." | "Boya gibi, not düşülsün (Önerilen)" | "O girdinin maliyeti de ilk sürümde ürüne yüklenmez; sonuçta 'kg dışı girdi maliyeti dahil değil' yazar. Ürün maliyeti yalnız kg girdilerden hesaplanır." | "Hesaplanamadı kalsın" — "Bugünkü davranış: böyle bir girdi varsa o emrin maliyeti hesaplanmaz, sebebi ekranda görünür." · "Birim maliyetiyle eklensin" — "Girdinin kendi birimindeki (adet/litre/metre) ortalama maliyeti ürün maliyetine eklenir; okunamazsa 'hesaplanamadı' olur." |
| 8.14 | "#1146 üretim maliyeti: Üretimden yeniden kullanılabilir artık (ör. dar şerit, kısa parça) çıkıp stoğa dönerse, girdi maliyetinden ne kadar pay alsın? Bugünkü kod artık varsa o emrin maliyetini hesaplamıyor. Örnek: 1.000 kg × 40 TL girdi; çıktı 900 kg sağlam, 50 kg artık, 50 kg hurda (hurda 10 TL/kg)." | "İade gibi stoğa dönsün (Önerilen)" | "Artık, hammaddenin kg maliyetiyle stoğa girer: 50 × 40 = 2.000 TL. Sağlam ürün (40.000 − 2.000 − 500) / 900 = 41,67 TL/kg olur. Kullanılmayan malzeme girdiği maliyetle döner; hurda kaybını sağlam ürün taşır." | "Ürünle aynı kg maliyeti" — "Artık da 2. kalite gibi paydaya girer: (40.000 − 500) / 950 = 41,58 TL/kg. Artık da 41,58 TL/kg ile stoğa girer, hurda kaybından pay alır." · "Şimdilik hesaplanmasın" — "Bugünkü davranış: artık varsa o emrin maliyeti hesaplanmaz, sebebi ekranda görünür." |
| 8.15 | "#1146 üretim maliyeti: Hurda fiyatı aylık ve elle giriliyor ('o ayın hurda fiyatı'). Emir iki aya yayılırsa hangi ayın fiyatı kullanılsın? Örnek: emir 20 Ekim'de açıldı; 30 Ekim'de 30 kg, 2 Kasım'da 20 kg hurda tartıldı; emir 3 Kasım'da kapandı." | "Hurdanın tartıldığı ay (Önerilen)" | "30 kg Ekim, 20 kg Kasım fiyatıyla değerlenir; her hurda çıktığı ayın fiyatını alır. O ayın fiyatı girilmemişse maliyet, fiyat girilene kadar bekler." | "Emrin kapandığı ay" — "50 kg'ın hepsi Kasım fiyatıyla değerlenir; emir başına tek fiyat kullanılır." · "Emrin açıldığı ay" — "50 kg'ın hepsi Ekim fiyatıyla değerlenir; emir başına tek fiyat kullanılır." |
| 8.16 | "Satışta 'stoktan fazla çıkan kg' sınırı %0,5; bunun kantar bantlarından ayrı bir kontrol olduğuna karar verdiniz. Kilitli DEC-STK-009 kararı kantar toleransının ürün ve işlem profiline göre ayarlanmasını ister. Bu %0,5 de profile göre değişebilsin mi? (Boyalı ürüne ayrı oran önerisini seçmemiştiniz.)" | "Tek oran, Ayarlar'da (Önerilen)" | "Tek %0,5 değeri Ayarlar'da tutulur, koda gömülmez; profil ayrımı yapılmaz. DEC-STK-009 kantar bantlarında aynen geçerli kalır. Veri görülünce gerekirse yeniden sorulur." | "Profile göre ayarlansın" — "Her ürün/işlem profili için ayrı oran tutulur, hepsi %0,5 başlar. Profillerin nasıl tanımlanacağı ayrıca karar ister." |

## SINAN-CEVAP (8) — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URT-D44 | Hurda DUR karşılaştırmasında hammaddenin kg maliyeti (8.1; SAT-D326 (3)'ün tanımı) | (1) **Hammaddenin kg maliyeti net hesaplanır: maliyet, iadeler düşüldükten sonra tüketilen kg'a bölünür.** Ürün maliyeti de net hesaplandığı için karşılaştırma aynı temelde yapılır. (2) Golden örnek: 500 kg @40 TL ve 500 kg @50 TL çıkış (brüt 45,00 TL/kg); 50 TL'lik partiden 400 kg iade; net 25.000 TL / 600 kg = **41,67 TL/kg** (41,666…). **43 TL/kg hurda fiyatında hesap durur;** brüt hesapta geçerdi. (3) #1146 kodundaki brüt karşılaştırma ("Bugünkü kod böyle çalışıyor") bu kararla değişir. (4) Issue #66 yorumu `6861267281`'deki brüt/net `NEEDS-SINAN` kaydı bu cevapla kapandı | `ProductionReceiptCostCalculatorV1` (#1146); golden testler |
| URT-D45 | Boyama dışındaki kg dışı girdinin (adet, litre, metre) maliyeti (8.13; SAT-D327'nin genellemesi) | (1) **Kg dışı girdinin maliyeti de ilk sürümde ürüne yüklenmez; sonuçta "kg dışı girdi maliyeti dahil değil" yazar. Ürün maliyeti yalnız kg girdilerden hesaplanır.** (2) #1146 hesaplayıcısının bu halde emrin maliyetini "hesaplanamadı" bırakması bu kararla değişir. (3) Yalnız maliyet tarafını cevaplar: bu satırların URT-D40 (1) kütle karşılaştırmasında nasıl ölçüleceği sorulmadı (`NEEDS-SINAN`, Sınırlar) | #1146 hesaplayıcısı; maliyet sonucu ve emir ekranı |
| URT-D46 | Kullanılabilir artığın maliyet payı (8.14; 13 Kasım dosyası Sınırlar'ındaki OC-3 satırını cevaplar) | (1) **Artık, hammaddenin kg maliyetiyle stoğa girer ("iade gibi").** Golden örnek: 1.000 kg × 40 TL girdi; çıktı 900 kg sağlam, 50 kg artık, 50 kg hurda (10 TL/kg); artık 50 × 40 = 2.000 TL; sağlam ürün (40.000 − 2.000 − 500) / 900 = **41,67 TL/kg** (41,666…). (2) **Kullanılmayan malzeme girdiği maliyetle döner; hurda kaybını sağlam ürün taşır.** (3) #1146 hesaplayıcısının artık varken fail-closed kalması bu kararla değişir. (4) BS-5 ve BS-14 değişmez; BS-5'teki "Alan gelene kadar Üretim UsableRemnant çıktısını fail-closed reddeder" kuralı ayrı bir alan şartıdır ve sürer (**yazar okuması**: bu karar maliyet payını söyler, artık çıktısının kabulünü açmaz). (5) **Yazar okumaları:** "hammaddenin kg maliyeti" URT-D44'teki net tanımdır; emirde 2. kalite de varsa kalan maliyet SAT-D326 (1)'e göre sağlam + 2. kalite kg'a bölünür; "artık" `UsableRemnant` çıktı sınıfıdır. (6) Fire anahtarı açıkken artığın payı ve DEC-PRDCT-006 kalemleri sorulmadı (`NEEDS-SINAN`, Sınırlar) | #1146 hesaplayıcısı; golden testler |
| URT-D47 | Emir iki aya yayılırsa hurda fiyatının ayı (8.15) | (1) **Her hurda tartıldığı ayın fiyatıyla değerlenir** (örnek: 30 kg Ekim, 20 kg Kasım fiyatıyla). (2) **O ayın fiyatı girilmemişse maliyet, fiyat girilene kadar bekler.** İstisna SAT-D326 (4): fire anahtarı kapalı ve tartılan hurda 0 kg ise fiyat yokluğu hesabı durdurmaz. (3) 13 Kasım dosyası Sınırlar'ındaki "dönemin emrin mi girişin mi dönemi olduğu" sorusunu cevaplar (hurdanın tartıldığı ay). Hurda fiyatını kimin gireceği açık kalır | #1146 hesaplayıcısı; hurda fiyatı giriş dilimi |
| SAT-D334 | Onaylı sayım eksiğinin maliyeti (8.2; SAT-D331'in karşı yönü) | (1) **Eksik kg, o depodaki o ürünün güncel ortalama maliyetiyle düşülür; ortalama değişmez.** (2) **Ortalama okunamazsa sistem "hesaplanamadı" der ve düşüm durur.** (3) H'deki "Sayım eksiğinin maliyet etkisi bu cevapla seçilmedi" kaydını cevaplar | Ortalama maliyet defteri; sayım dilimi |
| SAT-D335 | Eski veri aktarımı ya da hata yüzünden eksi görünen kayıt (8.3; SAT-D318 (4) ve SAT-D323 (2)'yi cevaplar) | (1) **O ürünün o depodaki maliyeti "hesaplanamadı" olur; maliyete ihtiyacı olan işlemler sebebi gösterilerek durur.** (2) **Düzeltme ikinci kişinin onayladığı sayım farkıyla yapılır. Sonradan gelen bir giriş tek başına düzeltmez** (SAT-D323 (1) ile aynı yön). (3) 13 Kasım dosyasındaki "eksi stok değişmezi ihlal edilmiş bir kayıt … nasıl düzeltileceği ve pozisyonun bir girişle yeniden okunur olup olmayacağı" `NEEDS-SINAN` kaydı ve SAT-D323 (2)'deki emniyet davranışı böylece Sinan cevabı olur. (4) Soru metnine göre bugünkü kod bu durumda işlemi durduruyor (#1107) | #1107; ortalama maliyet defteri; sayım dilimi |
| SAT-D336 | Durdurulan işlemin mesajında eldeki kg (8.5) | **Mesajda eldeki ve istenen kg birlikte yazar** (örnek: "Sistemde 480 kg var, 500 kg istendi."). Kullanıcı farkı hemen görür ve sayım isteyebilir | Stok çıkış ret ayrıntısı (409) ve ekran mesajı (#1155 ve stok çıkışı yapan ekranlar) |
| SAT-D337 | Durdurulan işlemlerin deneme kayıtlarını kimin göreceği (8.6) | **Stok raporlarını görme yetkisi olanlar görür; yeni yetki açılmaz.** Bu yetkinin kime verileceğine yine Sinan karar verir (grant insan kapısı) | Eksi stok deneme kaydı dilimi; stok raporları |
| SAT-D338 | Deneme kayıtlarının saklama süresi (8.7) | **Denetim kayıtları gibi silinmez. İleride ayrı bir saklama ve imha politikasına karar verilirse ona uyar** (saklama/imha politikası ayrı insan kararıdır) | Eksi stok deneme kaydı dilimi |
| SAT-D339 | Deneme kaydının düzeltme sayımıyla bağı (8.8; SAT-D328 (5)'i tamamlar) | (1) **Düzeltme sayımı onaylanınca o ürün ve depodaki açık denemeler "çözüldü" görünür.** (2) **Görev kutusu modülü devreye girdiğinde "sayım gerekli" görevi de oluşur;** o zamana kadar görev açılmaz | Eksi stok deneme kaydı dilimi; sayım dilimi; görev kutusu modülü |
| SAT-D340 | Mal kabulde teorik ağırlık ↔ kantar bantları (8.9; SAT-D324'ün satış dışı karşılığı) | (1) **Mal kabulde bantlar aynen kalır: fark %1'de uyarı, %2'de onay ister, %3'te bloklanır; 21 Ekim kararlarında yazıldığı gibi.** Tedarikçiden eksik ya da fazla gelen mal kontrolsüz girmez. (2) %1/%2/%3 geçici değerlerdir ve Ayarlar'dan değişir (soru metni). (3) "21 Ekim kararlarında yazıldığı gibi"nin `SINAN_KARARLARI_2026-10-21.md` No 62/63/64 olduğu soranın notudur, Sinan'ın sözü değildir. (4) 13 Kasım dosyası 9. tur Sınırlar'ındaki "Mal kabul (alış) tarafındaki bantların davranışı" maddesini cevaplar. `OPEN-004` KISMEN kalır; `DEC-STK-009` değişmez | Alış/mal kabul kantar kontrolü (değişiklik yok) |
| SAT-D341 | Satışta kantar farkı tam %0,5 olursa (8.10, R8-EK; SAT-D319 (5)–(6), SAT-D321 (3) ve SAT-D324'ü daraltır) | (1) **Tam %0,5 fark onaya düşer; %0,5'in altı onaysız geçer.** Örnek: sistemde 1.000 kg, kantarda 1.005 kg → onay. Onaylayan SAT-D320 (1) ve SAT-D321 (1)'deki gibidir. (2) Seçenek metnine göre bu, **SAT-D15 ("Bir sınır değeri kendi başlattığı banda girer"), 21 Ekim K4 `AtOrAbove` ve 21 Ekim No 40 ile aynıdır;** sınırda olan kayıt sıkı tarafa düşer. (3) SAT-D321 (3) ve SAT-D324'teki "%0,5 üstü" ifadesinin harfiyen okunuşu ("Sınır dahil, geçsin") uygulanmaz. (4) **8. turdaki ilk 10. soru cevabı ("Sınır dahil, geçsin") geçersizdir;** soran o seçeneğin gerekçesini yanlış yazmıştı ("sınırlar dahildir" ile uyumlu olduğunu söylemişti). 14 Kasım cevabı onun yerine geçer. (5) Oran değeri SAT-D344'e göre Ayarlar'daki tek %0,5 değeridir; profil yoktur. (6) 13 Kasım dosyası 9. tur Sınırlar'ındaki "Sınır değerinin dahil olup olmadığı" maddesini cevaplar | Satış kantar farkı dilimi (eşik karşılaştırması) |
| SAT-D342 | Fiziksel stok yeterliyken %0,5 onayı (8.11; SAT-D333'ü kalıcı yapar) | (1) **Fiziksel stok yettiği için kantar farkı kaydı açılmayan halde A satışı için Satış Müdürü onayı gerekmez.** (2) **Onay yalnız sistemdeki stoktan fazla çıkan kg için istenir;** B'nin eksiği B satılırken kantar farkı kuralına girer (örnek: stok 1.000 kg; A 600, B 400 ayrılmış; A'nın kantarı 606 kg). (3) 13 Kasım dosyası 4.–8. tur ve 9. tur Sınırlar'ındaki "fiziksel stok yeterliyken %0,5 onayı" `NEEDS-SINAN` kaydını cevaplar | Satış kantar farkı dilimi |
| SAT-D343 | İrsaliyede farkların toplanması (8.12; SAT-D320 (2)'nin payını açıklar) | (1) **Yalnız stoktan fazla çıkan kg toplanır; farklı ürünlerin farkları birbirini kapatmaz.** Örnek: A sistemde 500 kg, kantarda 508 kg (+8); B sistemde 500 kg, kantarda 494 kg (−6); irsaliye toplamı 1.000 kg → 8 kg = **%0,8 → Satış Müdürü onayı.** (2) SAT-D320 (2)'deki ölçüm tabanı (irsaliye toplamı) değişmez; bu karar oranın payını söyler. (3) B'nin −6 kg'ı SAT-D320 (3)'teki ters yön kuralına girer (**yazar okuması**). (4) 13 Kasım dosyası 3. tur Sınırlar'ındaki "toplam fark"ın nasıl toplanacağı sorusunu cevaplar; irsaliye toplamı sınırın içindeyken tek üründeki büyük fark için ayrı bir üst sınır sorusu açık kalır | Satış kantar farkı dilimi |
| SAT-D344 | %0,5 oranının ürün/işlem profiline göre değişip değişmeyeceği (8.16; SAT-D319 (5)'i tamamlar) | (1) **Tek %0,5 değeri Ayarlar'da tutulur, koda gömülmez; profil ayrımı yapılmaz.** (2) **DEC-STK-009 kantar bantlarında aynen geçerli kalır.** (3) Veri görülünce gerekirse yeniden sorulur. (4) 13 Kasım dosyası 9. tur Sınırlar'ındaki "Tek %0,5 oranının `DEC-STK-009`'daki "ürün ve işlem profiline göre" ilkesine tabi olup olmadığı" maddesini cevaplar | Satış kantar farkı dilimi; Ayarlar |
| FNS-D39 | Onay bekleyen yanlış resmî satış faturası kaydının reddi ve geri çekilmesi (8.4; F005'teki geri çekme satırını cevaplar) | (1) **Onaylayan kişi gerekçe yazarak reddedebilir; giren kişi kendi bekleyen kaydını geri çekebilir.** (2) **Hiçbir şey silinmez, iz kalır.** (3) **Reddedilen veya geri çekilen kaydın numarası, ETTN'si ve PDF'i yeniden kullanılabilir olur.** (4) FNS-D20 (onaylı kayıt değiştirilemez/iptal edilemez) ve FNS-D26 (maker ≠ approver) değişmez; bu karar yalnız onay bekleyen kaydı kapsar. (5) **Yazar okumaları:** numara/ETTN/PDF tekilliğinin yalnız canlı (bekleyen ve onaylı) kayıtlarda aranması uygulama ayrıntısıdır ve K3 migration review'unda doğrulanır; I'daki araştırma özetinin "İptal veya ret sonrası numara yeniden kullanılmaz" cümlesi resmî faturanın kendisi içindir, 8.4 ise ERP'ye yanlış girilmiş bekleyen kaydı serbest bırakır. (6) Yeni izin adları kod diliminin işidir; grant insan kapısıdır | #1102; Finans resmî fatura tablosu ve tekil indeksler (migration, K3) |

### SINAN-CEVAP (8) — sınırlar

- **Brüt/net `NEEDS-SINAN` kapandı.** Issue #66 yorumu `6861267281`'deki brüt/net sorusu 8.1 "Net" cevabıyla aşıldı
  (URT-D44); o yorum ayrıca karar sayılmaz.
- **10. soru.** 13 Kasım'daki ilk cevap geçersizdir ve bu belgede karar olarak yazılmadı; geçerli cevap 14 Kasım'daki
  R8-EK'tir (SAT-D341).
- **9. soru notu.** Mal kabul bantlarının 21 Ekim No 62/63/64'e eşlenmesi, No 40/41'in ayrı kural olduğu ve ALS
  kararlarıyla ilişkisi soranın notudur (Sinan'ın sözü değildir); SAT-D340 yalnız Sinan'ın seçtiği seçeneği kaydeder.
- **"Nereye gider" tablosu** kod şeridinin planıdır, karar değildir; aşağıdaki Kod etkisi bölümü onunla aynı yöndedir.
- **Ölçü tabanı** (13 Kasım dosyası 9. tur Sınırlar'ı) 8. turda sorulmadı ve açık kalır.

## Kod etkisi (kod şeridi; bu PR'da yapılmadı)

Bu belge kod değiştirmez. Aşağıdakiler yazarın review bulgularına dayanan okumasıdır; K3 kod PR'larında iki bağımsız
exact-head review ile doğrulanır.

- **#1146 (üretim girişi maliyeti):** SAT-D326 golden'ı (41,578947…) ve sıfır/eksi sonuçta "hesaplanamadı"; hurda DUR
  kuralı net tanımla (URT-D44, golden 41,67 ve 43 TL/kg'da DUR); hurdasız emirde fiyat yokken hesap (SAT-D326 (4)); boya
  ve kg dışı girdi notları (SAT-D327, URT-D45); artığın "iade gibi" payı (URT-D46, golden 41,67); hurdanın tartıldığı ayın
  fiyatı (URT-D47). Yeni head mekanik değildir; GOV-D1'e göre iki tam K3 review ister.
- **#1107 (ortalama maliyette eksi kayıt):** SAT-D335; soru metnine göre kod zaten durduruyor, sayımla düzeltme yolu
  sayım dilimindedir.
- **Sayım dilimi:** 0 görünen bobinin sayılması (SAT-D330), sayım fazlası ve eksiğinin maliyeti (SAT-D331, SAT-D334),
  düzeltme sayımının deneme kayıtlarını "çözüldü" yapması (SAT-D339). Ortalama maliyet defterinin stok hareketlerine
  bağlanmasını bekler.
- **Eksi stok deneme kaydı dilimi:** durdurma, gösterme ve kayıt (SAT-D328, SAT-D329), mesajda eldeki ve istenen kg
  (SAT-D336; #1155'teki 409 ayrıntısı), okuma yetkisi (SAT-D337), saklama (SAT-D338), "sayım gerekli" görevi görev kutusu
  modülünü bekler (SAT-D339).
- **Satış kantar farkı dilimi:** teslim toleransı bloğu (SAT-D332), tam %0,5'te onay (`AtOrAbove`; SAT-D341), fiziksel
  stok yeterliyken onaysız geçiş (SAT-D333, SAT-D342), yalnız fazlaların toplanması (SAT-D343), tek Ayarlar oranı
  (SAT-D344).
- **Mal kabul:** değişiklik yok (SAT-D340).
- **#1091:** sabit eşleme ve varsayılan `GoodProduct` (URT-D43).
- **#1109:** Critical işaretlemesi, yetki-kaybı testi, `ASSUMPTION-*` kayıtları ve pasife alma (FNS-D33, FNS-D34).
- **#1102:** `finance.*` sahip türü, PDF SHA-256 tekilliği ve 422, Ayarlar adaylarıyla para birimi, PDF-önce akışı, ret
  ve geri çekme (FNS-D35…FNS-D39).
- **Lojistik iptal akışı:** yüklenmiş ama sevk edilmemiş ve yüklenmemiş miktarın serbest bırakılması (LOJ-D15).
- Her yeni izin adı kod diliminin işidir; grant/rol/Critical ataması insan kapısıdır.

## Sınırlar

- **Kod, veri ve migration değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; izin veya grant açmaz. `KARAR_SICILI`/`ACIK_KARARLAR` değişmez, `OPEN-*` kapatılmaz (`OPEN-004` KISMEN
  kalır, `DEC-STK-009` değişmez). Bu PR'daki tek başka değişiklik `docs/delivery/URETIM_HAZIRLIK_PLANI_v1.md`
  §5.3a'nın URT-D43'e hizalanmasıdır.
- **Governance metinleri değişmez.** `AGENTS.md`, `.claude/skills/review-signatures/SKILL.md`'deki `DELTA-OK` imzacı
  metni ve GOV-D1 dosyası (`SINAN_KARARLARI_2026-11-07_K3_DELTA.md`) bu PR'da değişmez; GOV-D6 ile hizalanmaları ayrı
  bir governance PR'ının işidir. O PR gelene kadar GOV-D6 (4) yalnız yazar okumasıdır.
- **Okumalar.** SAT-D326 (5) (giriş ekranı uyarısı), SAT-D333 (2) ("tam" kelimesi), FNS-D34 (6), URT-D46 (4)–(5),
  SAT-D343 (3), FNS-D39 (5) ve GOV-D6 (4) yazar okumasıdır. A, E, I ve 8. turdaki okuma ve not bölümleri karar olarak
  yazılmadı (bkz. Kaynak).
- **Bu belgeyle cevaplanan eski `NEEDS-SINAN` ve açık maddeler:** brüt/net (`6861267281`; URT-D44); eksi stok değişmezi
  ihlal edilmiş kayıt (SAT-D335); fiziksel stok yeterliyken %0,5 onayı (SAT-D342); "toplam fark"ın toplanması (SAT-D343);
  sınır değerinin dahil olup olmadığı (SAT-D341); tek %0,5'in profile tabi olup olmadığı (SAT-D344); mal kabul bantları
  (SAT-D340); artığın maliyet payı OC-3 (URT-D46); hurda fiyatının dönemi (URT-D47); hurda fiyatı girdi maliyetini
  aşarsa (SAT-D326 (3)); F005'teki geri çekme yolu (FNS-D39); F012–F020'deki serbest bırakma ayrıntısı (LOJ-D15, yalnız
  sorulan kısım).
- **Açık kalanlar (`NEEDS-SINAN`):**
  - Yükleme talimatı iptalinde araca yüklenmiş malın fiziksel olarak indirilmesi veya yeniden tartılması ve iptalin
    bildirim/rapor etkisi.
  - Fire anahtarı açıkken kullanılabilir artığın maliyet payı ve DEC-PRDCT-006 kalemlerinin (geri kazanılabilir artık,
    WIP, ölçüm düzeltmesi) kütle dengesinde hangi tarafa yazılacağı.
  - Kg dışı girdilerin URT-D40 (1) kütle karşılaştırmasında nasıl ölçüleceği.
  - Boya ve kg dışı girdi maliyetinin sonraki sürümde ürüne yüklenip yüklenmeyeceği.
  - Hurda fiyatını kimin gireceği (izin adı kod PR'ının işidir; grant/rol insan kapısıdır).
  - Ölçü tabanı (13 Kasım dosyası 9. tur Sınırlar'ı).
  - İrsaliye toplamı sınırın içindeyken tek üründeki büyük fark için ayrı bir üst sınır olup olmadığı.
  - "Kantar farkı" hesabının kodu (SAT-D320 (4): Ayarlar'dan seçilir; bu belge kod seçmez).
  - Tek Satış Müdürü olan şirkette onay yolu (13 Kasım dosyası 4.–8. tur Sınırlar'ı).
  - Boyalı ürün satışında gerçek kantar farkı verisi (SAT-D321 (5)).
  - SAT-D323 (5)'te satış günü için geçerli ortalamanın, o güne ait kayıtlar sonradan düzeltildiyse hangi veriyle
    hesaplanacağı.
  - URT-D42 (3)'teki eski emirlerin maliyetinin nasıl okunur hale geleceği; URT-D42 (1)'in rulo girdisi olmayan
    süreçlerde nasıl ölçüleceği.
  - Fiziksel eksi olmadan maliyet pozisyonunun `NegativeStock` okunması (13 Kasım dosyası Sınırlar'ı). 8.3'ün bu hali
    kapsayıp kapsamadığı Sinan'a sorulmadı; kapsadığı okuması bu belgede karar olarak yazılmadı.
  - Kasa ve bankada kendi önerisini onaylama denemesinin iz bırakmaması (FNS-D33 (4): ayrı iş; kimin açacağı
    koordinatörün işidir).
- "Yazar okuması" diye işaretli noktalar Sinan'ın sözü değildir; Sinan farklı isterse ayrı karar gerekir.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
