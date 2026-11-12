# Sinan Kararları — 2026-11-12 (Üretim: başlamış üretim emrinde düzeltme ve iptal (F-009); elle termin (F-010))

Bu belge önceki karar dosyalarını silmez. URT-D28 ve URT-D29'u ekler. URT-D1…URT-D27 geçerli kalır
(`SINAN_KARARLARI_2026-11-01_URETIM.md`, `…_2026-11-02_URETIM_EK1.md`, `…_EK2.md`, `…_EK3.md`). Ana dosyanın §0 yetki ve
sınır kuralları aynen geçerlidir. `KARAR_SICILI` satırı eklemez.

**Kaynak:** Sinan, cevapları 12 Kasım 2026'da koordinatör oturumunda `AskUserQuestion` ile verdi. Koordinatör bunları Issue
#66 yorumu [`6819147253`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819147253) ("SINAN-KARARI
2026-11-12 (3. tur)") olarak kaydetti. Bu belge o yorumun 1. ve 3. maddelerini bağlar. 2. madde (F-017, gelecek
tedarikten ayırma) Alış/Satış şeridinin, 4. madde (KLN-D20, ters e-İrsaliye) Lojistik şeridinin işidir; burada karar
olarak yazılmaz.

**Kaynak (fire maliyeti):** Aynı gün Issue #66 yorumu
[`6818566934`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6818566934), madde 4. Bu madde SAT-D307
(3)'ü cevaplar. Kimliği Satış/Stok serisindedir; burada yalnız Üretim tarafındaki uygulaması kaydedilir (§Bağlı
kararlar).

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Termin nasıl belirlenir (F-010 / OPEN-007)? | **Elle girilsin** | Termin elle girilir; sistem hat hızı veya takvimle hesaplamaz. Soranın önerdiği seçenek (sistemin hat hızı ve takvimle hesaplaması) seçilmedi. Kapasite ve takvim hesabı v1 kapsamı dışıdır. |
| 2 | Başlamış üretim emrinde düzeltme ve iptal nasıl olur (F-009)? | Serbest metin, aşağıda aynen | — |

Sinan'ın 2. sorudaki metni (aynen):

> "Tüketim miktarından sevk olan varsa o üretim emrindeki ilgili satırda sevk olan malzeme miktarı kadar zaten
> değiştirilememesi gerekiyor, sevk olmayan malzeme miktarı kadar üretim emri satırında güncelleme yapabilir yada sevk
> yoksa komple o satırı silebilir, yani bir üretim emrinde sevk olan veya gerçekten üretilen malzeme varsa o miktara
> kadar ilgili satırlarda güncelleme yaptıktan sonra üretim emri tamamlanabilir, eğer bir üretim emrinde hiç sevk olan
> yoksa ve yanlışlıkla üretim yapıldıysa ilgili satırdaki üretim gerçekleştirmesi silinip hammadde eski miktarına
> ulaşınca o üretim emri silinebilir, ilk önce söylediklerimi analiz edip mantıklı bir şekilde yol haritası
> belirledikten sonra uygulamaya başla"

Koordinatörün aynı yorumdaki okuması (yazar okuması, geri alınabilir): sevk edilmiş miktar kilitlidir; satır bunun
altına indirilemez ve silinemez. Sevk edilmemiş kısım düzeltilebilir. Satırlar fiilî üretim ve sevk miktarına
çekildikten sonra emir tamamlanabilir. Hiç sevk yoksa yanlış üretim kaydı geri alınır, hammadde eski miktarına döner ve
emir iptal edilebilir.

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URT-D28 | Başlamış üretim emrinde düzeltme ve iptal (F-009) | (a) Bir emir satırında **sevk edilmiş miktar değiştirilemez**; satır bu miktarın altına indirilemez, sevk varken satır silinemez. (b) **Sevk edilmemiş miktar kadar** satır güncellenebilir; satırda hiç sevk yoksa satırın gerçekleştirmesi tamamen geri alınabilir. (c) Satırlar sevk edilen veya gerçekten üretilen miktara çekildikten sonra **emir tamamlanabilir**. (d) Emirde **hiç sevk yoksa** ve üretim yanlışlıkla yapıldıysa, satırların üretim gerçekleştirmesi geri alınır; **hammadde eski miktarına dönünce emir iptal edilebilir**. (e) Sinan'ın isteğiyle uygulama **analiz ve yol haritası** belgesinden sonra başlar. | `docs/delivery/URETIM_EMRI_DUZELTME_ANALIZ_v1.md`; Üretim emir aggregate'i, Stok ters hareketi, Lojistik sevk okuması |
| URT-D28 uygulama | Kullanıcının gördüğü "silme" | Mimari değişmez gereği (AT-052, Stok "silme yerine ters kayıt"): satır silme ve emir silme veritabanında **ters kayıt + iptal durumu** ile yapılır. Hareket ve olgu fiziksel olarak silinmez, denetim izi kalır. Bu Sinan'ın kararını değiştirmez, nasıl uygulanacağını söyler. | Üretim ve Stok kayıtları |
| URT-D29 | Termin (F-010 / OPEN-007) | Termin **elle girilir**. Sistem hat hızı veya takvimle hesaplamaz. Kapasite ve takvim hesabı v1 kapsamı dışıdır. Hangi kayıtta (plan veya emir) tutulacağı uygulama ayrıntısıdır (geri alınabilir), analiz belgesi §8'de önerilir. | Üretim planı / emri; ekranlar |

## Bağlı kararlar (başka şeridin kimliği)

| Kimlik | Karar | Üretim tarafındaki uygulama |
|---|---|---|
| SAT-D307 (3) | Fire maliyete yükleme anahtarı **kapalıyken** fire ürün maliyetine yüklenmez; **ayrı fire/dönem gideri** olarak raporlanır. Anahtar açıkken SAT-D307 (1)-(2) geçerlidir. | Üretim alış maliyeti hesaplayıcısı: anahtar kapalıyken kg maliyeti = net girdi maliyeti / tüketilen kg; fire ve işlem kaybının payı ayrı tutar olarak döner. Gider raporu Stok/Finans'ındır. |

## Sınırlar

- "Satır" sözcüğünün kod modelindeki karşılığı, sevk dışındaki tüketimlerin (sonraki prosese verilmiş, transfer edilmiş,
  ayrılmış çıktı) kilit sayılıp sayılmadığı, kapalı emirde düzeltme ve iptal edilen emrin planına ne olacağı bu kararda
  yoktur. Bunlar analiz belgesinin §9'unda `NEEDS-SINAN` olarak sorulur ve Issue #66'ya yazılır.
- Yeni yetki adları (düzeltme, iptal) yalnız kayıt olarak eklenir. Rol, grant ve Critical işaretleme insan kapısıdır.
- Dağıtım, ortam, secret, Ayarlar etkinleştirme, grant/rol/Critical, production etkinleştirme
  (`production.execution.enabled`), `OPEN-*` kapatma ve `.claude/settings.json` insan kapıları değişmez. URT-D29
  OPEN-007'yi kapatmaz.
