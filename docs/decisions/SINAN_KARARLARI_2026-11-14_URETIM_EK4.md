# Sinan Kararları — 2026-11-14 URETIM EK4 (Çıkan > giren kapanışı: artık/WIP, rulosuz proses, kg dışı girdi, kg çevrimi, boru/profil, eski emirler, tartım düzeltmesi; üretim maliyeti: yuvarlama, döviz, yeniden açılan emir, hurda fiyatı, sıfır maliyet, fire açıkken artık; kasa pasifleştirme ve yeniden etkinleştirme)

Bu belge önceki karar dosyalarını silmez ve değiştirmez; **URT-D48…URT-D59, FNS-D40 ve FNS-D41**'i ekler. İkisi de
Issue #66'ya yazılmış ama henüz `docs/decisions/` altına işlenmemiş Sinan cevaplarıdır (14 Kasım 2026).
`SINAN_KARARLARI_2026-11-13_EK1.md` (aşağıda "EK1"; URT-D43…URT-D47, FNS-D33…FNS-D39),
`SINAN_KARARLARI_2026-11-13_URETIM_MALIYETI_VE_EKSI_STOK.md` (aşağıda "13 Kasım dosyası"; URT-D40…URT-D42,
SAT-D316…SAT-D325), `SINAN_KARARLARI_2026-11-12_URETIM_EMRI_DUZELTME.md`, SAT-D305…SAT-D308 ve
`SINAN_KARARLARI_2026-11-02_URETIM_EK3.md` dahil önceki üretim dosyaları geçerlidir; bu belge onların dosyalarını
değiştirmez. Aşağıdaki yerlerde daha yeni kullanıcı cevabı eski metnin, bir "yazar okuması"nın ya da bir
`NEEDS-SINAN` kaydının önüne geçer:

- URT-D48 ↔ URT-D40 (3)'teki "DEC-PRDCT-006'nın ayrı saydığı kalemler … bu tanımda yoktur" yazar okuması ve EK1
  "Açık kalanlar"daki "DEC-PRDCT-006 kalemlerinin … kütle dengesinde hangi tarafa yazılacağı" satırı (yalnız çıkan >
  giren karşılaştırmasındaki taraf).
- URT-D49 ↔ EK1 "Açık kalanlar"daki "URT-D42 (1)'in rulo girdisi olmayan süreçlerde nasıl ölçüleceği".
- URT-D50 ↔ URT-D45 (3) ve EK1 "Açık kalanlar"daki "Kg dışı girdilerin URT-D40 (1) kütle karşılaştırmasında nasıl
  ölçüleceği".
- URT-D52 ↔ URT-D42 (5) ve EK1 "Açık kalanlar"daki "URT-D42 (3)'teki eski emirlerin maliyetinin nasıl okunur hale
  geleceği".
- URT-D57 ↔ URT-D47 (3)'teki "Hurda fiyatını kimin gireceği açık kalır" ve EK1 "Açık kalanlar"daki aynı satır.
- URT-D59 ↔ URT-D46 (6) ve EK1 "Açık kalanlar"daki "Fire anahtarı açıkken kullanılabilir artığın maliyet payı"
  (yalnız artık payı).

`KARAR_SICILI` ve `ACIK_KARARLAR` değişmez; hiçbir `OPEN-*` kapatılmaz (`OPEN-005` KISMEN kalır). DEC-PRDCT-006
satırı değişmez.

**Kimlikler:** `origin/main` `77a3f863`'te (bu dalın çıkış noktası) en büyük kimlikler URT-D47 ve FNS-D39'dur
(`git grep -ohE "URT-D[0-9]+|FNS-D[0-9]+" origin/main -- docs/decisions`). Aynı gün açık 10 PR'ın eklemeleri tarandı;
URT-D48 ve FNS-D40 ve sonrası hiçbirinde yoktur. EK1'deki sırayla, üretim emrinin kapanışı ve maliyeti URT-D
serisine, kasa FNS-D serisine yazıldı. Yeni önek icat edilmedi.

**Kaynak:** İki Issue #66 yorumu. Yorumlar depo sahibinin hesabından yazıldı; cevabın sahibi her yorumda Sinan diye
yazılıdır. Cevaplar **aynen** (yazım hataları dahil) alıntılanır; "(Önerilen)" işaretleri soranındır (Claude). R9'daki
"Soranın notları" ve "Nereye gider" tablosu Sinan'ın sözü değildir ve karar olarak yazılmadı. Bu belgenin yazımı
sırasında iki yorum da düzenlenmemişti.

| Etiket | Yorum | Saat (UTC) | Kayıt | Bu belgedeki satırlar |
|---|---|---|---|---|
| R9 | [6874794245](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6874794245) | 2026-11-14 08:51:00 | "SINAN-CEVAP (9)"; 16 soru, 4 grup, `AskUserQuestion`; Sinan her soruda bir seçenek seçti ya da kendi cevabını yazdı | 9.1–9.16 |
| R10 | [6874860782](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6874860782) | 2026-11-14 08:57:16 | "SINAN-CEVAP (10)"; 1 soru, `AskUserQuestion`; #1146 K3 review'larındaki P1-1 | 10.1 |

Satır numaraları: 9.n = R9'un n. sorusu; 10.1 = R10'un tek sorusu. R9'da bazı soruların seçenekleri yalnız etiketle
verilmişti; açıklaması olmayan seçenekte "açıklama kayıtta yok" yazılır.

## Çıkan > giren kapanışı (#1154 takibi) — soru ve cevaplar (R9 Grup 1, 3, 4)

Cevap sütunu Sinan'ın cevabıdır, aynen. #1154 merge edilmiş K3 PR'ıdır (URT-D41/URT-D42).

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Seçilen seçeneğin açıklaması | Seçilmeyenler |
|---|---|---|---|---|
| 9.1 | "Artık/WIP — Çıkan ile giren karşılaştırmasında kullanılabilir artık, WIP okuması ve düzeltmeler hangi tarafta sayılsın?" | "Çıkan tarafında (Önerilen)" | "Artık, WIP ve düzeltme de çıkan kg'a eklenir. Bugünkü kod böyle (fail-closed)." | "Yalnız iyi + 2. kalite + hurda + kayıp — Artık ve WIP karşılaştırma dışında kalır." |
| 9.2 | "Rulosuz proses — Ana rulo girdisi olmayan proseste çıkan > giren kontrolü nasıl yapılsın?" | "Emir toplamıyla (Önerilen)" | "Rulo yoksa emrin toplam giren kg'ı ile toplam çıkan kg'ı karşılaştırılır; rulolu proseslerde rulo başına kural aynen kalır." | "Kontrol yapılmasın." |
| 9.3 | "Kg dışı girdi — Boya dışında adet/litre/metreyle tüketilen girdi varsa kütle kontrolünde ne olsun?" | "Kütleye girmesin, ayrıca kg giren ama çıkan adet veya metrede olsa çıkan adet yada metrenin kg karşılışı giren kg ile kontrol edilecek boru profil de özellikle böyle" | Kendi cevabı (açıklama yok) | "Kütleye girmesin (Önerilen)" · "Emir kapanmasın" (açıklama kayıtta yok) |
| 9.4 | "Tartım düzeltme — Fazla tartılmış çıkış için düzeltme yolu." | "Burda kararı sen ver ama düzeltme yapılabilsin ve bunun için zaten onaya gidiyır" | Kendi cevabı; soranın seçimi 9.16'da Sinan'a soruldu | "Ters kayıt + 2. kişi onayı (Önerilen)" · "Ters kayıt, onaysız" · "Şimdilik açılmasın" (açıklama kayıtta yok) |
| 9.9 | "Kg çevrimi (#1154) — Adet/metrenin kg karşılığı nereden alınsın?" | "Ürün kartından alacak ama toleranslardan dolayı burda o hesaplanan yardımcı olacak ama gerçek kg ile dağıtım olması lazım teorik hesaba göre bunu bizim sektör gerçeği ile araştırıp mantıklı bir çözüm bul" | Kendi cevabı; soranın önerisi 9.13'te soruldu | "Ürün kartı teorik ağırlık (Önerilen)" · "Gerçek tartım" · "Tartım varsa o, yoksa teorik" (açıklama kayıtta yok) |
| 9.13 | "Boru/profil (#1154) — Teorik ile gerçek ağırlık arasında birkaç yüzde fark sektörde normaldir (EN 10219-2 tek boy kütlesi için +%6)." | "Öneri (Önerilen)" | "Kontrol: çıkan paketler kantarda tartılır; tartılan kg giren kg ile karşılaştırılır. Teorik (adet × kartın kg/adet) yardımcı olarak görünür, Ayarlar'daki sapmayı aşarsa uyarı verir. Dağıtım: emrin gerçek kg'ı (hurda/kayıp düşüldükten sonra) çıkan ürünlere teorik ağırlıkları oranında pay edilir; her ürünün stok kg'ı ve maliyeti bu gerçek kg'dan gelir." | "Tartım yoksa teorik kabul" · "Yalnız teorik" (açıklama kayıtta yok) |
| 9.14 | "Eski artı yönlü emirlerin maliyeti (#1154)" | "Hesaplanamadı + raporda (Önerilen)" | "Maliyet "hesaplanamadı — çıkan giren kg'dan fazla" görünür ve rapor listesinde yer alır; gerekirse tartım düzeltmesiyle düzeltilir." | "Aynı kuralla hesaplansın." |
| 9.16 | "Tartım düzeltme onayı (#1154) — Claude'un seçimi: yanlış tartım silinmez; ters kayıt + doğru tartım girilir, ikinci kişi onaylar, iz kalır; yeni yetki kimseye verilmeden gelir." | "Evet, böyle (Önerilen)" | Soru metnindeki seçim | "Mevcut onay yeterli." |

## Çıkan > giren kapanışı (#1154 takibi) — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URT-D48 | Çıkan > giren karşılaştırmasında artık, WIP ve düzeltmenin tarafı (9.1; URT-D40 (3)'ü tamamlar) | (1) **Kullanılabilir artık, WIP okuması ve düzeltme çıkan tarafında sayılır; çıkan kg'a eklenir.** (2) Soru metnine göre bugünkü kod böyledir (fail-closed); bu karar kod değişikliği istemez. (3) URT-D40 (3)'teki "çıkan" tanımının bu kalemleri içermediği yazar okuması bu cevapla aşıldı. (4) Yalnız karşılaştırmadaki tarafı cevaplar. R10'daki "aynı satırdaki DEC-PRDCT-006 kalemleri açık kalır" cümlesi soranındır; bu kalemlerin maliyet payı ve ayrı uzlaştırma raporu sorulmadı (`NEEDS-SINAN`, Sınırlar). DEC-PRDCT-006 değişmez | Emir kapanış kontrolü (#1154 takip dilimi) |
| URT-D49 | Ana rulo girdisi olmayan proseste kontrol (9.2; URT-D42 (1)'i tamamlar) | (1) **Rulo yoksa emrin toplam giren kg'ı ile toplam çıkan kg'ı karşılaştırılır.** (2) **Rulolu proseslerde rulo başına kural (URT-D42 (1)) aynen kalır.** (3) URT-D41 (1) ve URT-D42 (2) bu proseste de geçerlidir: çıkan girenden fazlaysa emir kapanmaz, tartım düzeltilir (**yazar okuması**: cevap ölçünün nasıl yapılacağını söyler, sonucu değiştirmez) | Emir kapanış kontrolü (#1154 takip dilimi) |
| URT-D50 | Kg dışı girdi ve kg dışı çıktı kütle kontrolünde (9.3; URT-D45 (3)'ü cevaplar) | (1) **Boya dışında adet, litre veya metreyle tüketilen girdi kütle kontrolüne girmez.** (2) **Kg giren ama çıkan adet veya metre olan emirde çıkanın kg karşılığı giren kg ile kontrol edilir; boru ve profil özellikle böyledir.** (3) Kg karşılığının nereden geldiği ve dağıtım URT-D51'dedir. (4) URT-D45 (1) (kg dışı girdinin maliyeti ilk sürümde ürüne yüklenmez) değişmez | Emir kapanış kontrolü; boru/profil kg çevrimi dilimi |
| URT-D51 | Adet/metre çıktının kg karşılığı, kontrol ve dağıtım (9.9, 9.13) | (1) Sinan'ın sözü (9.9): **kg karşılığı ürün kartından alınır; toleranslar yüzünden bu hesap yardımcıdır; dağıtım gerçek kg ile yapılır;** çözüm sektör gerçeğine göre araştırılır. (2) Araştırmadan sonra seçilen öneri (9.13): **Kontrol:** çıkan paketler kantarda tartılır; tartılan kg giren kg ile karşılaştırılır. (3) **Teorik ağırlık (adet × kartın kg/adet) yardımcı olarak görünür; Ayarlar'daki sapmayı aşarsa uyarı verir.** (4) **Dağıtım:** emrin gerçek kg'ı (hurda ve kayıp düşüldükten sonra) çıkan ürünlere teorik ağırlıkları oranında pay edilir; her ürünün stok kg'ı ve maliyeti bu gerçek kg'dan gelir. (5) Sapma eşiğinin başlangıç değeri seçilmedi. "Ayarlar'da girilene kadar uyarı üretilmez, kontrol tartılan kg ile yapılır" cümlesi soranın notudur (**yazar okuması**); eşik değeri `NEEDS-SINAN`'dır (Sınırlar). Eşik koda gömülmez | Boru/profil kg çevrimi ve dağıtım dilimi (ayrı dilim); Ayarlar; ürün kartı kg/adet |
| URT-D52 | Bu kuraldan önce artı yönde kapanmış emirlerin maliyeti (9.14; URT-D42 (3) ve (5)'i tamamlar) | (1) **Maliyet "hesaplanamadı — çıkan giren kg'dan fazla" diye görünür ve rapor listesinde yer alır.** (2) **Gerekirse tartım düzeltmesiyle düzeltilir** (URT-D53). (3) URT-D42 (3) aynen: emir açılmaz ve değiştirilmez. Düzeltmenin URT-D53'teki ters kayıt yoluyla yapıldığı **yazar okumasıdır**; bunun için kapanmış emrin yeniden açılması gerekip gerekmediği sorulmadı (`NEEDS-SINAN`, Sınırlar) | #1146 hesaplayıcısı; eski emir listesi raporu |
| URT-D53 | Fazla tartılmış çıkışın düzeltilmesi (9.4, 9.16) | (1) Sinan'ın sözü (9.4): **düzeltme yapılabilsin ve onaya gider;** yolu soran seçsin. (2) Soranın seçimi 9.16'da onaylandı: **yanlış tartım silinmez; ters kayıt ve doğru tartım girilir; ikinci kişi onaylar; iz kalır.** (3) **Yeni yetki kimseye verilmeden gelir;** grant/rol insan kapısıdır. (4) Seçilmeyen: "Mevcut onay yeterli". Yeni izin adı ve komutun biçimi kod diliminin işidir | Tartım düzeltme komutu dilimi (yeni yetki, grant yok) |

## Üretim girişi maliyeti (#1146 takibi) — soru ve cevaplar (R9 Grup 2, 3, 4; R10)

Cevap sütunu Sinan'ın cevabıdır, aynen. #1146 merge edilmiş PR'dır.

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Seçilen seçeneğin açıklaması | Seçilmeyenler |
|---|---|---|---|---|
| 9.5 | "Yuvarlama — Yuvarlama ne zaman, kalan kuruş nereye?" | "Sonda yuvarla, kuruş sağlama (Önerilen)" | "Hesap tam hassasiyetle; yalnız kayıtta kg maliyeti 4 haneye, toplam TL 2 haneye yuvarlanır; kalan kuruş sağlam ürün satırına yazılır, giren TL = dağıtılan TL." | "Her adımda 2 hane." |
| 9.6 | "Para birimi" | "Bence bunların hepsinde döviz desteği olsun" | Kendi cevabı; kur sorusu 9.10'da | "Yalnız TL (Önerilen)" · "Dövizi TL'ye çevir" (açıklama kayıtta yok) |
| 9.7 | "Yeniden açılan emir" | "Yeniden hesapla, eskisi iz (Önerilen)" | "İkinci kapanışta maliyet yeniden hesaplanır; ilk sonuç silinmez, geçmiş olarak görünür; fark ters kayıt + yeni kayıtla stoka yansır." | "İlk maliyet kalsın." |
| 9.8 | "Hurda fiyatını kim girer" | "Satış modülü gibi ilerleyecek, yetkilerde aynı şekilde, zaten çıkan hurda satış modülünde sipariş ile satılabilecek" | Kendi cevabı | "Yeni yetki + 2. kişi onayı (Önerilen)" · "Yeni yetki, onaysız" (açıklama kayıtta yok) |
| 9.10 | "Döviz kuru (#1146)" | "Stoğa girdiği kurla (Önerilen)" | "Girdi, stoğa girdiği anda (mal kabul/alış faturası) kaydedilen kurla TL'ye çevrilmiş maliyetiyle gelir; döviz tutarı ve kur raporda görünür; kur yoksa "hesaplanamadı"." | "Emrin kapandığı günün kuru" · "Maliyet dövizde tutulsun" (açıklama kayıtta yok) |
| 9.15 | "Sıfır maliyetli girdi (#1146)" | "Hesaplanamadı (Önerilen)" | "Emrin maliyeti "hesaplanamadı" olur, sebep ve girdi görünür; maliyet girilince yeniden okunur. Bugünkü kod böyle." | "Sıfır kabul edilsin." |
| 10.1 | "'Fire maliyete dahil' ayarı açıkken (varsayılan) kullanılabilir artık stoka hangi kg maliyetiyle dönsün? Örnek: 1000 kg × 40 TL rulo; 900 kg sağlam ürün, 50 kg artık, 50 kg hurda." | "Hammadde kg maliyeti (Önerilen)" | "Artık iki ayarda da hammaddenin net kg maliyetiyle (40 TL) döner; 8.14 cevabıyla aynı kural. Fire payını yalnız ürün taşır: artık 2.000 TL, ürün 38.000/900 = 42,22 TL/kg." | "Ürünle aynı kg maliyeti — Fire, artık ve ürüne eşit dağılır: ikisi de 40.000/950 = 42,11 TL/kg." · "Hesaplanamadı kalsın — Fire açıkken artık varsa emrin maliyeti "hesaplanamadı" olur; ileride ayrıca karar verilir." |

## Üretim girişi maliyeti (#1146 takibi) — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URT-D54 | Yuvarlama ve kalan kuruş (9.5) | (1) **Hesap tam hassasiyetle yapılır; yalnız kayıtta kg maliyeti 4 haneye, toplam TL 2 haneye yuvarlanır.** (2) **Kalan kuruş sağlam ürün satırına yazılır; giren TL = dağıtılan TL.** (3) Seçilmeyen: her adımda 2 hane. Yuvarlama yönü sorulmadı; satış tutarları için SAT-D11 "2 ondalık, `AwayFromZero`" der, bu hesaba uygulanıp uygulanmayacağı `NEEDS-SINAN`'dır (Sınırlar) | `ProductionReceiptCostCalculatorV1` takip dilimi; golden testler |
| URT-D55 | Döviz desteği ve kur (9.6, 9.10) | (1) Sinan'ın sözü (9.6): **"bunların hepsinde döviz desteği olsun".** (2) Kur (9.10): **girdi, stoğa girdiği anda (mal kabul veya alış faturası) kaydedilen kurla TL'ye çevrilmiş maliyetiyle gelir; döviz tutarı ve kur raporda görünür; kur yoksa maliyet "hesaplanamadı" olur.** (3) Seçilmeyenler: emrin kapandığı günün kuru; maliyetin dövizde tutulması. (4) R9 "Soranın notları"ndaki "döviz desteği maliyet hesabının girdisi olarak stoğa giriş kurundan gelir; kur kaynağı politikası değişmez" cümlesi soranındır (**yazar okuması**). "Bunların hepsinde"nin maliyet hesabı dışındaki kapsamı sorulmadı (`NEEDS-SINAN`, Sınırlar) | #1146 hesaplayıcısı takip dilimi (döviz girdisi); maliyet raporu |
| URT-D56 | Yeniden açılan emrin maliyeti (9.7) | (1) **İkinci kapanışta maliyet yeniden hesaplanır.** (2) **İlk sonuç silinmez, geçmiş olarak görünür.** (3) **Fark ters kayıt ve yeni kayıtla stoka yansır.** (4) R9 "Nereye gider" tablosuna göre bugünkü davranışla uyumludur (soranın notu) | #1146 hesaplayıcısı; maliyet geçmişi; stok maliyet kaydı |
| URT-D57 | Hurda fiyatını kimin gireceği (9.8; URT-D47 (3)'teki açık noktayı cevaplar) | (1) **Hurda fiyatı Satış modülü gibi ilerler; yetkiler de aynı şekildedir.** (2) **Çıkan hurda Satış modülünde siparişle satılabilir.** (3) R9 "Soranın notları"ndaki okuma (hurda fiyatı satış fiyatı gibi satış tarafında tanımlanır, satış yetkileriyle aynı yetki/onay kalıbını izler; maliyetteki hurda değeri bu fiyattan okunur) soranındır (**yazar okuması**); ayrıntı kendi PR'ında review'a açıktır. (4) SAT-D316 (3)–(4) ve URT-D47 (1)–(2) değişmez. Yeni izin adı kod diliminin işidir; grant/rol insan kapısıdır | Hurda fiyatı — satış fiyat dilimi; #1146 hesaplayıcısının hurda fiyatı girdisi |
| URT-D58 | Sıfır maliyetli girdi (9.15) | (1) **Emrin maliyeti "hesaplanamadı" olur; sebep ve girdi görünür; maliyet girilince yeniden okunur.** (2) Soru metnine göre bugünkü kod böyledir. (3) Seçilmeyen: sıfır kabul edilsin | #1146 hesaplayıcısı |
| URT-D59 | Fire maliyete dahilken kullanılabilir artığın kg maliyeti (10.1; URT-D46 (6)'daki artık payı sorusunu cevaplar) | (1) **Artık, iki ayarda da hammaddenin net kg maliyetiyle döner** (URT-D46 ile aynı kural; net tanım URT-D44). (2) **Fire payını yalnız ürün taşır.** (3) Golden örnek: 1.000 kg × 40 TL rulo; 900 kg sağlam ürün, 50 kg artık, 50 kg hurda; **artık 2.000 TL; ürün 38.000 / 900 = 42,22 TL/kg** (42,222…). (4) Seçilmeyenler: ürünle aynı kg maliyeti (42,11 TL/kg); "hesaplanamadı" kalması. (5) R10'a göre #1146 hesaplayıcısının fire-açık artık kolu bu cevaba atıf yapar. DEC-PRDCT-006 kalemleri için bkz. URT-D48 (4) | `ProductionReceiptCostCalculatorV1` takip dilimi; golden testler |

## Kasa hesabı (#1109) — soru ve cevaplar (R9 Grup 3)

Cevap sütunu Sinan'ın cevabıdır, aynen. #1109 açık PR'dır.

| # | Soru (sorulan metin) | Cevap (Sinan, aynen) | Seçilen seçeneğin açıklaması | Seçilmeyenler |
|---|---|---|---|---|
| 9.11 | "Kasa pasifleştirme beklerken (#1109)" | "Onaya kadar girilebilsin (Önerilen)" | "Hesap yalnız onaydan sonra pasif olur. Bugünkü kod böyle." | "Beklerken dursun." |
| 9.12 | "Yeniden etkinleştirmede para birimi (#1109)" | "Denetlensin (Önerilen)" | "Para birimi artık Ayarlar'da izinli değilse yeniden etkinleştirme reddedilir; hesap para birimini değiştirmez." | "Denetlenmesin (bugünkü kod)." |

## Kasa hesabı (#1109) — kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D40 | Pasifleştirme onay beklerken kasaya giriş (9.11; FNS-D34'ü tamamlar) | (1) **Pasifleştirme önerisi onay beklerken kasaya giriş yapılabilir; hesap yalnız onaydan sonra pasif olur.** (2) Soru metnine göre bugünkü kod böyledir. (3) FNS-D34 (3)–(4) aynen: bakiyesi sıfır olmayan kasa pasife alınamaz; pasif kasaya yeni hareket yazılamaz | #1109 |
| FNS-D41 | Yeniden etkinleştirmede para birimi denetimi (9.12; FNS-D34 (5) ve FNS-D33 (2)'yi tamamlar) | (1) **Kasanın para birimi artık Ayarlar'da izinli değilse yeniden etkinleştirme reddedilir.** (2) **Yeniden etkinleştirme hesabın para birimini değiştirmez.** (3) Bugünkü kod denetlemiyor (seçilmeyen seçenek metni); bu kod takip diliminde değişir | #1109 takibi (yeniden etkinleştirme) |

## Sınırlar

- **Kod, veri ve migration değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; izin veya grant açmaz. `KARAR_SICILI`/`ACIK_KARARLAR` değişmez, `OPEN-*` kapatılmaz (`OPEN-005` KISMEN
  kalır).
- **Okumalar.** URT-D49 (3), URT-D51 (5), URT-D52 (3), URT-D55 (4) ve URT-D57 (3) yazar okumasıdır. R9'daki "Soranın
  notları" ve "Nereye gider" tablosu karar olarak yazılmadı; tablo kod şeridinin planıdır.
- **Bu belgeyle cevaplanan eski açık maddeler:** DEC-PRDCT-006 kalemlerinin çıkan > giren karşılaştırmasındaki tarafı
  (URT-D48); rulo girdisi olmayan süreçte ölçü (URT-D49); kg dışı girdilerin kütle karşılaştırması (URT-D50); eski
  artı yönlü emirlerin maliyeti (URT-D52); hurda fiyatını kimin gireceği (URT-D57); fire açıkken artığın maliyet payı
  (URT-D59).
- **Açık kalanlar (`NEEDS-SINAN`):**
  - Teorik–gerçek ağırlık sapma uyarı eşiğinin başlangıç değeri (URT-D51 (5)). Eşik Ayarlar'dan gelir; koda gömülmez.
  - DEC-PRDCT-006 kalemlerinin (artık, WIP, ölçüm düzeltmesi) karşılaştırmadaki taraf dışındaki yönleri: maliyet payı
    ve ayrı uzlaştırma raporu (URT-D48 (4)).
  - Eski artı yönlü emirde tartım düzeltmesinin kapanmış emri yeniden açmayı gerektirip gerektirmediği (URT-D52 (3)).
  - Yuvarlamanın yönü; SAT-D11 kuralının bu hesaba uygulanıp uygulanmayacağı (URT-D54 (3)).
  - "Bunların hepsinde döviz desteği"nin üretim maliyeti dışındaki kapsamı (URT-D55 (4)).
  - Boya ve kg dışı girdi maliyetinin sonraki sürümde ürüne yüklenip yüklenmeyeceği (EK1'den açık; bu turda sorulmadı).
  - EK1 "Açık kalanlar"daki diğer satırlar bu belgeyle değişmez.
- "Yazar okuması" diye işaretli noktalar Sinan'ın sözü değildir; Sinan farklı isterse ayrı karar gerekir.
- Yeni yetkiler kimseye verilmeden gelir. İnsan kapıları (deployment, environment, secret, Settings aktivasyonu,
  grant/rol/Critical, saklama/imha politikası, production, `OPEN-*` kapatma, `.claude/settings.json`) değişmez.
