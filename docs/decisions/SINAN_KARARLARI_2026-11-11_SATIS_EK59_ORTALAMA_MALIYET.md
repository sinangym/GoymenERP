# Sinan Kararları — 2026-11-11 (Stok: ortalama maliyet takibi — fiyat girdisi ve döviz, sıfır stok, sıfır maliyetli alış girişi)

Bu belge önceki karar dosyalarını silmez; SAT-D295…SAT-D299'u ekler ve EK52'nin
(`SINAN_KARARLARI_2026-11-10_SATIS_EK52.md`) "Sınırlar"ında ve Stok kodundaki `OPEN-INV-AVGCOST-*` yorumlarında açık
bırakılan noktalardan dördünü cevaplar (birim çevrimi aynı gün SAT-D298 ile, ALS-D58'in Ürün çevrim portu için kalkması SAT-D299 ile; aşağıdaki "Ek karar" bölümleri). SAT-D262…SAT-D274 (EK52), SAT-D256…SAT-D261 (EK51), SAT-D263 (3), ALS-D6, ALS-D14,
ALS-D15, ALS-D41, KLN-D19 ve `OPEN-008` geçerlidir; bu belge onların metnini değiştirmez. `KARAR_SICILI` değişmez.

**Kimlik öneki (neden SAT-D):** Stok için ayrı bir karar öneki yoktur (`STK-D…` kayıtlı değildir; `git grep` ile
doğrulandı). Bu karar EK52'nin Satış marjı maliyet zincirinin devamıdır ve numara SAT-D serisinin son kimliğinden
(SAT-D294, EK58; `main` `36885556a`) sonra gelir; yeni ön ek icat edilmedi. Açık PR'larda (#760, #782, #797) SAT-D295 ve
sonrası kullanan karar dosyası yoktur.

**Kaynak:**

- Sinan'ın 11 Kasım 2026'da koordinatör oturumunda `AskUserQuestion` ile verdiği doğrudan cevaplar; koordinatörün Issue #66'ya
  yazdığı kayıt: yorum
  [6791690928](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6791690928) ("6760990097 ve 6762605433 için").
  Cevap metni (aynen): "1. Fiyat girdisi (Alış sipariş satırı): **her iki fiyat tutulur** — satır USD (döviz) birim
  fiyatlıysa maliyet hem USD olarak hem **faturalanmış kurla TL karşılığı** olarak okunur. Birim dönüşümü bu cevapta ayrıca
  kararlaştırılmadı: açık kalır (`NEEDS-SINAN` olarak ayrı sorulacak; dilim stok birimi ≠ alış birimi kayıtları için
  NoSource kalır). Kur kaynağı "faturalanmış kur" olarak yazıldı; kuru hangi belgenin (alış faturası) taşıdığı Kalan Modüller
  şeridince doğrulanıp gerekirse NEEDS-SINAN yazılır. 2. Stok tam sıfıra düşünce son ortalama **okunabilir değer olarak
  kalır** (OPEN-INV-AVGCOST-* ilgili kısım kapanır, karar dosyasında işlenecek). 3. Sıfır birim maliyetli alış girişi
  **"maliyet bilinmiyor" (Unreadable)** sayılır, ortalamaya katılmaz. Kod şu an sıfırı kabul ediyor; bu cevaba göre
  düzeltilecek. Kayıt: Kalan Modüller şeridi karar dosyası/PR'ı hazırlar."
- Yorumda soruların seçenek listesi yer almaz; seçenekler bu belgeye yazılmadı (uydurulmadı). Soru/cevap metni yorumdaki
  haliyle kaydedildi.

## Soru ve cevaplar

| # | Soru (yorumdaki konu) | Cevap | Kayda geçen anlam |
|---|---|---|---|
| 1 | Ortalama maliyetin fiyat girdisi (Alış sipariş satırı); dövizli satır nasıl okunur? | "Her iki fiyat tutulur": satır USD birim fiyatlıysa maliyet hem USD hem faturalanmış kurla TL karşılığı olarak okunur | Girdi Alış sipariş satırıdır; USD ve TL karşılığı birlikte okunur. Birim dönüşümü bu cevapta **seçilmedi** |
| 2 | Stok tam sıfıra düşünce ortalama ne olsun? | "Son ortalama okunabilir değer olarak kalır" | Sıfır stokta Unreadable değil, son ortalama değer okunur |
| 3 | Sıfır birim maliyetli alış girişi nasıl sayılsın? | "Maliyet bilinmiyor (Unreadable)"; ortalamaya katılmaz | Sıfır, maliyet değil "bilinmiyor" sayılır; ortalamayı sulandırmaz |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D295 | Fiyat girdisi ve döviz cinsi maliyet (soru 1) | (1) Hareketli ortalama alış maliyetinin **fiyat girdisi Alış sipariş satırıdır**. (2) Satır döviz (USD) birim fiyatlıysa maliyet **hem USD olarak hem faturalanmış kurla TL karşılığı olarak** okunur; iki değer birlikte tutulur. (3) **Birim dönüşümü açık kalır:** stok birimi ≠ alış birimi olan kayıtlarda port `NoSource` kalır (`NEEDS-SINAN`, aşağıda). (4) Kurun hangi belgeden geldiği ("faturalanmış kur") Sinan'ın kendi cevabında Kalan Modüller şeridince doğrulanacak diye bırakıldı; bulgu "Doğrulama"dadır ve kur kaynağı **doğrulanana kadar** `OPEN-INV-AVGCOST-FX` açık kalır. | Stok maliyet defteri girdisi, `IInventoryAverageCostReadPortV1` (dövizli değer), Alış sipariş satırı sözleşmesi |
| SAT-D296 | Sıfır stokta ortalama (soru 2) | Stok tam sıfıra düştüğünde **son ortalama okunabilir değer olarak kalır** (Value). Pozisyon bu nedenle Unreadable'a çevrilmez. Negatif stok bu karardan **etkilenmez** (açık). | `AverageCostCalculator` (sıfır stok), okuma portu |
| SAT-D297 | Sıfır birim maliyetli alış girişi (soru 3) | Birim maliyeti **sıfır** olan alış girişi **"maliyet bilinmiyor" (Unreadable)** sayılır ve **ortalamaya katılmaz**. Uygulama okunuşu: pozisyon, mevcut Unreadable davranışı gibi **yapışkan** olarak okunamaz olur (`ZeroCostReceipt`); sıfır, ortalamayı sulandırmaz ve sessizce kabul edilmez. Bu okunuş geri alınabilir uygulama ayrıntısıdır, kod PR'ında yazılıdır; Sinan farklı isterse (ör. mevcut ortalamayı koru) ayrı karar gerekir. | `AverageCostCalculator`, `AverageCostUnreadableReason`, okuma portu |

## Doğrulama (yazarın repo taraması; Sinan kararı değildir)

Soru: "Faturalanmış kur" hangi belgeden geliyor?

- **Alışta iki ayrı kur var:** ERP **kontrol kuru** (kabul/irsaliye gününden önceki iş günü TCMB kuru) faturaya sayı olarak
  yazılır ve sabit kalır (ALS-D6, ALS-D14: `SINAN_KARARLARI_2026-11-02_ALIS.md`, `…_ALIS_EK1.md`); **resmî kur
  tedarikçinin faturasındaki değerdir**, ayrıca kaydedilir, ERP değiştirmez; ikisi farklıysa Satın alma müdürü onayı
  (ALS-D15, `…_ALIS_EK1.md` "ALS-D6 ile ilişki" notu, DEC-FIN-003).
- **TL borç/fiş kuru:** kur farkı onaylandığında TL borç kuru **tedarikçi faturasındaki kurdur** (ALS-D41,
  `SINAN_KARARLARI_2026-11-02_ALIS_EK3.md`); Muhasebe fişinde de alışta tedarikçi faturasındaki kur geçerlidir (KLN-D19,
  `SINAN_KARARLARI_2026-11-07_MUHASEBE_FISI_VE_RECOVERY.md`; `…_ALIS_EK9.md`: `ExchangeRate` = tedarikçi faturası kuru).
  Satışta karşılığı faturaya yazılan kurdur (SAT-D226, `…_SATIS_EK42.md`); KUR_GUNU kararı
  (`SINAN_KARARLARI_2026-10-27_KUR_GUNU.md`, SAT-D54/D55) yalnız "önceki iş günü" TCMB kurunun nasıl bulunacağını söyler.
- **Kodda henüz yok:** `src/backend/Modules/Purchasing/README.md` ("Still absent"): bilgi amaçlı kur (A2a-KUR), gelen fatura
  ve üçlü eşleşme Alış modülünde **yoktur**; Purchasing kodunda döviz kuru alanı bulunmaz (`git grep` ile doğrulandı).
- **Sonuç:** "Faturalanmış kur" büyük olasılıkla **tedarikçi (alış) faturasındaki kurdur** (ALS-D41, KLN-D19). Ancak (a) bu
  adı taşıyan açık bir karar yoktur; ERP kontrol kuru da faturaya yazılır; (b) maliyet **Alış sipariş satırından** okunur,
  fatura ise sonra gelir: fatura gelmeden dövizli satırın TL karşılığının hangi kurla okunacağı yazılı değildir; (c) ilgili
  Alış kodu henüz yoktur. Bu yüzden `OPEN-INV-AVGCOST-FX` kapatılmadı.

`NEEDS-SINAN:` Alış sipariş satırı USD iken stok maliyetinin TL karşılığı **tedarikçi faturasındaki (resmî) kurla** mı hesaplansın (fatura gelene kadar dövizli satırın TL maliyeti "kullanılamıyor" kalsın), yoksa fatura gelmeden ERP kontrol kuruyla (ALS-D6/D14) geçici hesaplanıp faturadaki kurla düzeltilsin mi?

Birim dönüşümü için ayrı soru (**CEVAPLANDI, bkz. SAT-D298**; kalan açık soru yalnız kur sorusudur):

`NEEDS-SINAN:` Alış sipariş satırının birimi stok biriminden (kg) farklıysa (örn. adet, metre, ton) stok maliyeti kg başına nasıl hesaplansın: ürün kartındaki birim çevrim katsayısı mı kullanılsın (katsayı yoksa "kullanılamıyor" kalsın), yoksa satır başına elle kg girişi mi istensin?

## Kapatılan ve kapanmayan `OPEN-INV-AVGCOST-*` kimlikleri

`OPEN-INV-AVGCOST-*` kimlikleri yalnız Stok kodunda ve README'sinde geçer (`AverageCostPosition.cs`, `AverageCostCalculator.cs`,
`tests/GoymenERP.Inventory.UnitTests/InventoryAverageCostCalculatorTests.cs`, `src/backend/Modules/Inventory/README.md`);
`docs/` altında, `KARAR_SICILI.*` ve `ACIK_KARARLAR.md` içinde kayıtlı **değildir** (`git grep OPEN-INV` docs'ta boş).
EK52 de bunları `OPEN-NNN` olarak kaydetmemiştir. Bu nedenle sicil dosyalarında değişiklik gerekmez; **registry MD/CSV
eşliği ve hash yeniden üretimi gerekmez** (dosyalar değişmedi). Aşağıdaki kapanış, Sinan'ın doğrudan cevabına
(#66 yorumu 6791690928) bağlıdır ve kod yorumlarının güncellenmesi kod PR'ının işidir.

| Kimlik | Durum | Dayanak |
|---|---|---|
| `OPEN-INV-AVGCOST-ZERO-STOCK` | **Kapanır** | SAT-D296 (son ortalama okunabilir) |
| Sıfır birim maliyetli alış girişi (kodda kimliği yok; yeni durum) | **Cevaplanır** | SAT-D297 (Unreadable, ortalamaya girmez) |
| `OPEN-INV-AVGCOST-UNIT-CONVERSION` | **Kapanır** (aşağıdaki "Ek karar: birim çevrim" bölümü); fiilî çevrim katsayı kaynağı U-08 cevabına bağlıdır, o zamana kadar `NoSource` kalır (SAT-D299 "Açık kalan") | SAT-D298; #66 yorumu 6794310745 madde 1 |
| `OPEN-INV-AVGCOST-FX` | **Açık** (SAT-D295 ilkesi verildi; kur belgesi doğrulanmadı) | "Doğrulama" bölümü; `NEEDS-SINAN` |
| `OPEN-INV-AVGCOST-NEGATIVE-STOCK` | Açık | Cevaplanmadı |
| `OPEN-INV-AVGCOST-LANDED` | Açık | Cevaplanmadı (SAT-D263 (3)) |
| `OPEN-INV-AVGCOST-RETURN` | Açık | Cevaplanmadı |
| `OPEN-INV-AVGCOST-MIXED-SOURCE` | Açık | Cevaplanmadı (SAT-D258) |
| `OPEN-INV-AVGCOST-UNCOSTED-STOCK` | Açık | Cevaplanmadı |

Kodda geçen `OPEN-INV-AVGCOST-PRODUCTION` (test yorumu) `…-MIXED-SOURCE` ile aynı konudur; açık kalır. `OPEN-008` açık
kalır.

## Uygulama notu (kod şeridi; geri alınabilir ayrıntılar işaretlidir)

Bunlar ayrı **K3** kod PR'larıdır (para/formül, cross-module sözleşme; iki bağımsız exact-head review, gerçek SQL/failure
kanıtı). Bu belge kod değiştirmez.

- **SAT-D297:** yeni `AverageCostUnreadableReason.ZeroCostReceipt = 9`; okuma portunun halka açık nedenler listesine yeni kod
  `zero_cost_receipt` eklenir (sürümlü sözleşme testi). Sıfır maliyetli alış girişi ortalamaya girmez; pozisyon yapışkan
  Unreadable olur (geri alınabilir uygulama okunuşu; kod PR'ında ve CHECK 1..9 forward-fix migration'ında belirtilir).
- **SAT-D296:** `AverageCostCalculator` sıfır stokta son ortalamayı **okunabilir** (Value) döndürür; mevcut davranış
  ("last average is kept") korunur, yalnız "okunabilir mi" sorusundaki yorum ve test yorumu (`OPEN-INV-AVGCOST-ZERO-STOCK`)
  güncellenir; port sıfır stokta Value cevabını testle sabitler.
- **SAT-D295:** Alış sipariş satırı girişi bağlanmadan port `NoSource` kalır. Dövizli değer için ek alanlar (USD tutar,
  TL karşılığı, kur) ve kur kaynağı, `NEEDS-SINAN` (kur) cevaplanana kadar **uygulanmaz**; birim dönüşümü SAT-D298 ile cevaplandı, ancak Ürün katsayı kaynağı (SAT-D299 portu ve U-08 politikası) gelene kadar
  stok birimi ≠ alış birimi satırı `NoSource` (veya mevcut `UnitConversion` nedeniyle Unreadable) kalır.

| Karar | İş | Şerit / Kapı |
|---|---|---|
| SAT-D296 | Sıfır stokta okunabilir ortalama testi ve yorum güncellemesi | Stok backend; K3 (para/formül) |
| SAT-D297 | `ZeroCostReceipt = 9`, okuma portu yeni kod, calculator ve birim/sözleşme testleri | Stok backend; K3 |
| SAT-D299 | `IProductPurchaseUnitConversionV1` portu (katsayı modeli U-08 cevabına bağlı); Stok kg başına hesap | Ürün (port) + Stok; cross-module contract K3 |
| SAT-D295 | Alış satırı girişi ve dövizli okuma (kur `NEEDS-SINAN` sonrası; birim için SAT-D298) | Alış + Stok; cross-module contract K3 |

## Ek karar: birim çevrim (SAT-D298; 11 Kasım 2026, aynı gün)

**Kaynak:** Sinan'ın koordinatör oturumunda `AskUserQuestion` ile verdiği cevap; koordinatörün Issue #66'ya yazdığı kayıt:
yorum [6794310745](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6794310745) (6791808512 için; aynı yorum 6794251775'e de atıf yapar, o konu Finans şeridindedir ve bu belgeye alınmadı). Cevap metni
(aynen): "Alış satırı birimi stok biriminden farklıysa stok maliyeti **ürün kartındaki birim çevrim katsayısı** ile kg
başına hesaplanır; katsayı yoksa "kullanılamıyor" (NoSource) kalır. Satırda elle kg girişi yok." Yukarıdaki ikinci
`NEEDS-SINAN` (birim) bu cevapla kapanmıştır; kur sorusu açıktır.

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D298 | Alış birimi ≠ stok birimi | (1) Çevrim kaynağı **ürün kartındaki birim çevrim katsayısıdır** (ALS-D51 ile uyumlu). (2) Maliyet katsayı ile **kg başına** hesaplanır. (3) Katsayı yoksa port `NoSource` döner; tahmini katsayı üretilmez. (4) Alış satırında **elle kg girişi yoktur**. | Stok maliyet defteri girdisi, okuma portu, Alış sipariş satırı sözleşmesi |

**Yazarın tespiti (Sinan kararı değildir):** Katsayıyı okuyacak bir Ürün portu bugün yoktur (`Product/README.md`: varsayılan
çevrim yok; `IProductPurchaseUnitConversionV1` önerisi `docs/handoffs/ALIS_URUN_DEVIR_v1.md` U-A1'de ertelendi). ALS-D58
(`SINAN_KARARLARI_2026-11-02_ALIS_EK9.md`) Ürün için yeni port açılmasını Alış/Satış/Finans/Üretim bitene kadar yasaklar.
Bu nedenle uygulama o gün şuydu: çevrim gerektiren satır `NoSource` kalır (katsayı yok). **Güncelleme:** ALS-D58'in bu port
için kalkması aynı gün SAT-D299 ile Sinan tarafından verildi (aşağıda); bu paragraftaki "yasaklar" ve "açılmaz" ifadeleri
yalnız `IProductPurchaseUnitConversionV1` için artık geçerli değildir. Port açıldığında hesap bu karara göre bağlanır. Kod PR'ı (K3) kapsamı: kg başına hesap ve 4 ondalık
AwayFromZero yuvarlama (mevcut defter kuralı), `AverageCostPosition.cs` yorumundaki `NEEDS-SINAN` ibaresinin kaldırılması;
katsayı kaynağı gelmeden calculator'a katsayı girişi eklenmez.

## Ek karar: ALS-D58 Ürün birim çevrim portu için kalkar (SAT-D299; 11 Kasım 2026)

**Kaynak:** Koordinatörün Issue #66'ya yazdığı `SINAN-KARARI (2026-11-11, koordinatör kaydı)`: yorum
[6794527264](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6794527264) (kaydın "EVET" ifadesi koordinatör aktarımından gelir; yorum metninde geçmez). Kayıt metni (kısaltılmış; yorumun son iki cümlesi "Karar dosyası/ALS-D58 güncellemesi ilgili şeritte yazılır. FX belgesi sorusu (6791808196) hâlâ açık." alıntıya alınmadı): "Stok ortalama maliyeti
için ALS-D58 erteleme kararı kalkar; ürün kartı birim çevrim katsayısını okuyan `IProductPurchaseUnitConversionV1` Ürün
şeridinde açılabilir (katsayı yoksa NoSource kuralı değişmez, elle kg girişi yok). Kalan Modüller K3 kod PR'ını
başlatabilir (iki bağımsız exact-head review)."

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D299 | ALS-D58 ve Ürün çevrim portu | (1) ALS-D58 erteleme kararı, kayıt metnine göre Stok ortalama maliyeti için kalkar; port olarak **ürün kartı birim çevrim katsayısını okuyan `IProductPurchaseUnitConversionV1`** açılabilir. (2) Port Ürün şeridinde açılabilir. (3) Katsayı yoksa `NoSource` kuralı ve "elle kg girişi yok" değişmez (SAT-D298). | `ALIS_URUN_DEVIR_v1.md` U-A1, Ürün modülü, Stok maliyet okuma portu |

**Yazarın dar okuması (Sinan kararı değildir; kayıt metni daha geniş yazılmıştır):** Bu belge kalkışı yalnız
`IProductPurchaseUnitConversionV1` ile sınırlı uygular. ALS-D58'in diğer kalemleri (Stok/Ayarlar/Cari yeni port-tablo-ekran,
Lojistik ALS-D65, ALS-D63/ALS-D69 dar istisnaları dahil) bu kararla değiştirilmiş sayılmaz; Ürün için yeni tablo/ekranın
bu kararla açılıp açılmadığı kayıtta yazmaz ve burada seçilmez. Sınırlama muhafazakârdır; Sinan daha geniş istiyorsa ayrı
karar verir.

**Açık kalan (yazarın tespiti; Sinan kararı değildir):** Ürün kartında bugün çevrim verisi yoktur ve çevrim politikası
(U-08: hangi ürün sınıfında sabit, formül bağlı veya fiilî katsayı; hangi birim çifti) **kabul edilmemiştir** (`Product/README.md`:
"No offered-unit or conversion policy is accepted (U-08)"). Port açılması, katsayının nerede saklanacağını ve hangi
sınıfların v1'de okunacağını seçmez; bu bir işletme/model politikasıdır ve tahminle seçilmez. Soru #66'ya yazıldı: yorum [6794545274](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6794545274) (`NEEDS-SINAN`: v1 katsayı tanımı için (a) ürün başına tek sabit katsayı, (b) ürün sınıfına göre sabit/formül bağlı/fiilî, (c) önce yalnız sabit katsayı seçenekleri; öneri yok).
Politika cevaplanana kadar Stok kodu katsayı girişi eklemez; çevrim gerektiren satır `NoSource` kalır.

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi değiştirmez; grant
  açmaz.
- **Kur kaynağı** yukarıdaki `NEEDS-SINAN` ile açıktır; tahminle seçilmez. Birim dönüşümü SAT-D298 ile kararlıdır; yalnız katsayı kaynağı eksiktir: Ürün portu SAT-D299 ile açılabilir, ancak katsayı modeli (U-08) cevaplanmadıkça veri yoktur ve çevrim gerektiren satır `NoSource` kalır. SAT-D299 kimliği de SAT-D294 sonrası aynı seriden alındı (kimlik önekindeki kontrol SAT-D295…SAT-D299 için geçerlidir).
- Negatif stok, ek (landed) maliyet, iade, karma kaynak ve defterde maliyetlenmemiş stok açık kalır.
- Bu belge `OPEN-008`i veya `KARAR_SICILI`/`ACIK_KARARLAR` kayıtlarını değiştirmez. `OPEN-INV-AVGCOST-*` kimlikleri
  docs'ta kayıtlı olmadığı için "kapanış" yalnız bu belge ve kod yorumlarında tutulur; Sinan'ın cevabına
  (#66 yorumu 6791690928) bağlıdır.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
