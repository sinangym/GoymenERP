# Sinan Kararları — 2026-11-12 (Üretim: başlamış üretim emrinde düzeltme ve iptal (F-009); elle termin (F-010))

Bu belge önceki karar dosyalarını silmez. URT-D28…URT-D33'ü ekler. URT-D1…URT-D27 geçerli kalır
(`SINAN_KARARLARI_2026-11-01_URETIM.md`, `…_2026-11-02_URETIM_EK1.md`, `…_EK2.md`, `…_EK3.md`). Ana dosyanın §0 yetki ve
sınır kuralları aynen geçerlidir. `KARAR_SICILI` satırı eklemez.

**Kaynak:** Sinan, cevapları 12 Kasım 2026'da koordinatör oturumunda `AskUserQuestion` ile verdi. Koordinatör bunları Issue
#66 yorumu [`6819147253`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819147253) ("SINAN-KARARI
2026-11-12 (3. tur)") olarak kaydetti. Bu belge o yorumun 1. ve 3. maddelerini bağlar. 2. madde (F-017, gelecek
tedarikten ayırma) Alış/Satış şeridinin, 4. madde (KLN-D20, ters e-İrsaliye) Lojistik şeridinin işidir; burada karar
olarak yazılmaz.

**Kaynak (F-009 takip soruları N1–N4):** Analiz belgesinin §9 soruları Issue #66'ya
[`6819518778`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819518778) ile soruldu. Sinan cevapları aynı
gün koordinatör oturumunda `AskUserQuestion` ile verdi:
- N4: 5. tur, [`6819640816`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819640816) madde 2.
- N2 ve N3: 6. tur, [`6819835966`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819835966) madde 2 ve 3.
- N1: 7. tur, [`6819908312`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819908312) madde 1.

Bu yorumların başka maddeleri (F-018, F-017, KLN-D25, API-30, F-012, F-020) başka şeritlerin işidir; burada karar
olarak yazılmaz.

**Kaynak (fire maliyeti):** Aynı gün Issue #66 yorumu
[`6818566934`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6818566934), madde 4. Stok şeridi bu maddeyi **SAT-D308** olarak
`SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md` (#1108) dosyasında yazar; SAT-D307 (3)'teki açık
notu cevaplar. Burada karar olarak yeniden yazılmaz, yalnız Üretim tarafındaki uygulaması kaydedilir (§Bağlı kararlar).

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Termin nasıl belirlenir (F-010 / OPEN-007)? | **Elle girilsin** | Termin elle girilir; sistem hat hızı veya takvimle hesaplamaz. Soranın önerdiği seçenek (sistemin hat hızı ve takvimle hesaplaması) seçilmedi. Kapasite ve takvim hesabı v1 kapsamı dışıdır. |
| 2 | Başlamış üretim emrinde düzeltme ve iptal nasıl olur (F-009)? | Serbest metin, aşağıda aynen | — |
| 3 (N1) | URT-D28'deki "satır" nedir? Soranın önerisi: bir girdi rulosunun gerçekleştirmesi. | Serbest metin, aşağıda aynen | Önerilen "girdi rulosu" okuması seçilmedi. |
| 4 (N2) | Sevk dışında başka emre girdi olarak verilmiş, transfer edilmiş veya satışa ayrılmış çıktı da kilitli sayılsın mı? | **Evet, hepsi kilitli** (önerilen) | Düzeltmeden önce o iş geri alınır: ayırma bırakılır, transfer geri alınır. |
| 5 (N3) | Kapalı emirde düzeltme gerekirse emir yeniden açılabilsin mi? | **Yetkili kişi gerekçeyle yeniden açabilir** (önerilen) | Düzeltme ters kayıtla yapılır ve denetim kaydına düşer. Muhasebe dönemi kapanmışsa emir yeniden açılamaz. |
| 6 (N4) | İptal edilen emrin planına ne olsun? | **Plan Onaylı'ya döner** (önerilen) | Aynı plandan yeniden üretim emri açılabilir. |

Sinan'ın 2. sorudaki metni (aynen):

> "Tüketim miktarından sevk olan varsa o üretim emrindeki ilgili satırda sevk olan malzeme miktarı kadar zaten
> değiştirilememesi gerekiyor, sevk olmayan malzeme miktarı kadar üretim emri satırında güncelleme yapabilir yada sevk
> yoksa komple o satırı silebilir, yani bir üretim emrinde sevk olan veya gerçekten üretilen malzeme varsa o miktara
> kadar ilgili satırlarda güncelleme yaptıktan sonra üretim emri tamamlanabilir, eğer bir üretim emrinde hiç sevk olan
> yoksa ve yanlışlıkla üretim yapıldıysa ilgili satırdaki üretim gerçekleştirmesi silinip hammadde eski miktarına
> ulaşınca o üretim emri silinebilir, ilk önce söylediklerimi analiz edip mantıklı bir şekilde yol haritası
> belirledikten sonra uygulamaya başla"

Sinan'ın 3. sorudaki (N1) metni (aynen):

> "Plan bazında üretime girecek veya üretimden çıkacak her ürün satırı bunu sen ne demek istediğimi anla"

Koordinatörün 7. tur okuması (geri alınabilir): "satır" girdi rulosu değildir. Planda tanımlı her girdi ürün satırı ve
her çıktı ürün satırıdır (plan/emir satırı). Düzeltme sınırı, o ürün satırına bağlı gerçekleşmelerin toplamı üzerinden
uygulanır. Rulo ve etiket kayıtları bu satırın altındaki ayrıntıdır.

Koordinatörün 2. sorudaki okuması (yazar okuması, geri alınabilir): sevk edilmiş miktar kilitlidir; satır bunun
altına indirilemez ve silinemez. Sevk edilmemiş kısım düzeltilebilir. Satırlar fiilî üretim ve sevk miktarına
çekildikten sonra emir tamamlanabilir. Hiç sevk yoksa yanlış üretim kaydı geri alınır, hammadde eski miktarına döner ve
emir iptal edilebilir.

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URT-D28 | Başlamış üretim emrinde düzeltme ve iptal (F-009) | (a) Bir emir satırında **sevk edilmiş miktar değiştirilemez**; satır bu miktarın altına indirilemez, sevk varken satır silinemez. (b) **Sevk edilmemiş miktar kadar** satır güncellenebilir; satırda hiç sevk yoksa satırın gerçekleştirmesi tamamen geri alınabilir. (c) Satırlar sevk edilen veya gerçekten üretilen miktara çekildikten sonra **emir tamamlanabilir**. (d) Emirde **hiç sevk yoksa** ve üretim yanlışlıkla yapıldıysa, satırların üretim gerçekleştirmesi geri alınır; **hammadde eski miktarına dönünce emir iptal edilebilir**. (e) Sinan'ın isteğiyle uygulama **analiz ve yol haritası** belgesinden sonra başlar. | `docs/delivery/URETIM_EMRI_DUZELTME_ANALIZ_v1.md`; Üretim emir aggregate'i, Stok ters hareketi, Lojistik sevk okuması |
| URT-D28 uygulama | Kullanıcının gördüğü "silme" | Mimari değişmez gereği (AT-052, Stok "silme yerine ters kayıt"): satır silme ve emir silme veritabanında **ters kayıt + iptal durumu** ile yapılır. Hareket ve olgu fiziksel olarak silinmez, denetim izi kalır. Bu Sinan'ın kararını değiştirmez, nasıl uygulanacağını söyler. | Üretim ve Stok kayıtları |
| URT-D30 | "Satır" (N1) | URT-D28'deki "satır", planın **girdi ürün satırı** ve **çıktı ürün satırıdır**. Girdi rulosu değildir. Kilit ve düzeltme sınırı, o ürün satırına bağlı gerçekleşmelerin (çıkış, iade, çıktı beyanı) **toplamı** üzerinden uygulanır. Rulo ve etiket bu satırın altındaki ayrıntıdır; ters kayıt fiziksel olarak yine etiket üzerinde yapılır. Bugünkü koddaki karşılığı analiz belgesinin §3'ündedir. | Analiz §3, §4.2; Üretim emir aggregate'i |
| URT-D31 | Kilitli çıktı (N2) | Sevk edilmiş çıktıya ek olarak **başka emre girdi olarak verilmiş, transfer edilmiş veya satışa ayrılmış** çıktı da kilitlidir. Düzeltmeden önce o iş geri alınır (ayırma bırakılır, transfer geri alınır). Üretim bu işleri kendisi geri almaz. | Stok etiket durum okuma portu; analiz §4.2, §4.5 |
| URT-D32 | Kapalı emri yeniden açma (N3) | Kapalı üretim emrini **yetkili kişi gerekçeyle yeniden açabilir**. Düzeltme ters kayıtla yapılır ve denetim kaydına düşer. **Muhasebe dönemi kapanmışsa** emir yeniden açılamaz. Yeni yetki adı yalnız kayıt olarak eklenir; grant insan kapısıdır. | Üretim emir aggregate'i; analiz §4.6; yeni yetki adı |
| URT-D33 | İptal edilen emrin planı (N4) | Emir iptal edilince plan **Onaylı'ya döner** ve aynı plandan yeniden üretim emri açılabilir. | Üretim planı; analiz §4.7 |
| URT-D29 | Termin (F-010 / OPEN-007) | Termin **elle girilir**. Sistem hat hızı veya takvimle hesaplamaz. Kapasite ve takvim hesabı v1 kapsamı dışıdır. Hangi kayıtta (plan veya emir) tutulacağı uygulama ayrıntısıdır (geri alınabilir), analiz belgesi §8'de önerilir. | Üretim planı / emri; ekranlar |

## Bağlı kararlar (başka şeridin kimliği)

| Kimlik | Karar | Üretim tarafındaki uygulama |
|---|---|---|
| SAT-D308 (#1108; SAT-D307 (3) notunu cevaplar) | Fire maliyete yükleme anahtarı **kapalıyken** fire ürün maliyetine yüklenmez; **ayrı fire/dönem gideri** olarak raporlanır. Anahtar açıkken SAT-D307 (1)-(2) geçerlidir. Hesap eşlemesi `NEEDS-SPEC`; gelene kadar gider kaydı açılmaz, yalnız raporlanır. | Üretim alış maliyeti hesaplayıcısı: anahtar kapalıyken kg maliyeti = net girdi maliyeti / tüketilen kg; fire ve işlem kaybının payı ayrı tutar olarak döner. Formül okuması K3'tür, kod PR'ı golden örnek taşır. Stok bu tutarı ortalamaya katmaz, maliyet portunda ayrı alan olarak taşır. |

## Sınırlar

- URT-D28'in açık bıraktığı dört soru (N1–N4) URT-D30…URT-D33 ile cevaplandı. Çıktı ürün satırının bugünkü kodda
  karşılığı olmaması (çıktı beyanı plan çıktı satırına bağlı değil) bir uygulama ayrıntısıdır; analiz belgesi §3'te
  `ASSUMPTION-URT-F009-1` olarak yazılır ve geri alınabilir.
- "Muhasebe dönemi kapalı" bilgisinin kaynağı Finans'ındır. Üretim bunu yalnız bir okuma portundan sorar; port yoksa ya da
  cevap okunamazsa yeniden açma reddedilir (fail-closed).
- Yeni yetki adları (düzeltme, iptal, yeniden açma) yalnız kayıt olarak eklenir. Rol, grant ve Critical işaretleme insan kapısıdır.
- SAT-D308 kimliği #1108 merge edilince kesinleşir. #1108 kimliği değiştirirse bu dosyadaki atıf düzeltilir.
- Dağıtım, ortam, secret, Ayarlar etkinleştirme, grant/rol/Critical, production etkinleştirme
  (`production.execution.enabled`), `OPEN-*` kapatma ve `.claude/settings.json` insan kapıları değişmez. URT-D29
  OPEN-007'yi kapatmaz.
