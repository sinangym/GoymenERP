# Sinan Kararları — 2026-11-12 (Üretim: başlamış üretim emrinde düzeltme ve iptal (F-009); elle termin (F-010))

Bu belge önceki karar dosyalarını silmez. URT-D28…URT-D39'u ekler. URT-D1…URT-D27 geçerli kalır
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

**Kaynak (iptal sonrası yeni emirde ayırma):** Issue #66 yorumu
[`6822303233`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822303233) ("SINAN-KARARI 2026-11-12 (20.
tur)") madde 1. Bu soru #1114 DOCS review B `6822168607` üzerine soruldu. Aynı yorumun 2. maddesi Kimlik'in, 3. maddesi
Finans'ın işidir.

**Kaynak (yeniden açılan emirde çıktının Stok maliyeti):** Issue #66 yorumu
[`6820601088`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820601088) (11. tur) madde 2. Geri alınan
çıktı, kendi giriş birim maliyetiyle ters hareketle stoktan çıkar. Kalan stoğun ortalaması yeniden hesaplanır. Geçmiş
geriye dönük düzeltilmez. Bu kural Stok'undur. Hangi karar dosyasına ve kimliğe yazılacağı Stok + Üretim için
`NEEDS-SPEC`'tir (`SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md`, Seçilmeyenler). Bu kuralın
yeniden açılan emre uygulanışı 22. tur m.2 ile teyit edildi. Sinan kimlik atamadı; URT-D36 kimliğini bu belge açar
(aşağıda).

**Kaynak (etiket seçimi ve ters hareket maliyeti):** Issue #66 yorumu
[`6822949341`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822949341) ("SİNAN KARARI — 22. tur").
Sorular `NEEDS-SPEC` [`6822790075`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822790075) ile
soruldu (analiz §6, Stok ST-F009-D1 cevabı). Madde 1 URT-D35 olarak yazılır. Madde 2, 11. tur m.2'yi yeniden açılan emir için teyit eder ve iki kaynakla birlikte URT-D36 olarak yazılır.

**Kaynak (yeni emirde farklı etiket ve sahiplik):** Issue #66 yorumu
[`6823484848`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823484848) ("Sinan kararları — tur 24").
Sorular #1114 r3 review A `6823447079` / B `6823440416` P2-2 üzerine soruldu. Madde 1 URT-D37, madde 2 URT-D38 olarak
yazılır. Aynı yorumdaki koordinatör düzeltmesi 22. tur m.2'nin "Anlamı" satırındaki URT-D8 atfını geri alır (URT-D36
satırı).

**Kaynak (kısmi dilme ve yeniden onayda ayırma):** Issue #66 yorumu
[`6823809221`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823809221) ("Sinan kararı — tur 26"). Sorular #1114 r4 review A `6823733757` / B
`6823752203` P2-1/P2-2 üzerine soruldu. Madde 1 AT-044'ün iş sorusunu (NEEDS-BUSINESS `5741547443`) cevaplar; madde 2
URT-D37'nin yeniden onayındaki ayırma anını belirler. İkisi yeni kimlik açmaz; URT-D37 satırına uygulama sınırı olarak
işlenir; m.2 ayrıca URT-D34/URT-D35'in ayırma anını belirler (koordinatör okuması, r5 B `6824122323` P2-1). Kayıt OPEN-*
kapatmaz.

**Kaynak (yeniden onayda değişmiş ayarlar):** Issue #66 yorumu [`6824450207`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6824450207) ("Sinan kararı — 27. tur"). Soru #1114 r6
review A `6824391793` / B `6824411703` P2 üzerine soruldu (D5 yeniden onayında DEC-PRDCT-008 snapshot'ı). URT-D39 olarak
yazılır.

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
| 7 (N5, 20. tur m.1) | İptalden sonra yeni emirde girdi nasıl ayrılsın? | **Yeni emirde yeniden ayır** (önerilen) | Plan Onaylı'ya döner. Yeni emir açılırken stok yeniden kontrol edilip ayrılır. Yetmezse emir açılmaz ve eksik girdiler açıkça gösterilir. |
| 8 (22. tur m.1) | Emir açılırken girdi ürünü hangi etiketlerden ayrılır? | "Etiket önemli, kullanıcı kendisi seçer" (aynen) | Seçilmeyen: otomatik FIFO, planda kayıtlı etiketler. |
| 9 (22. tur m.2) | Yeniden açılan emrin çıktısı hangi maliyetle ters harekete alınır? | "Emrin kendi maliyeti (Önerilen)" (aynen) | Seçilmeyen: güncel ortalama maliyet. |
| 10 (24. tur m.1) | Üretim emri iptal edilip yeni emir açılınca, planlanan bobin/etiket yerine başka bir etiket seçilirse ne olsun? (Plan o bobinin enine ve teknik değerlerine göre hesaplanmıştı.) | "Plan yeniden hesaplanır (Önerilen)" (aynen) | Seçenek metni (aynen): "Kullanıcı başka etiket seçebilir; sistem planı yeni etikete göre yeniden hesaplar (en, fire, kütle dengesi) ve plan yeniden onaya düşer. Uymuyorsa seçim reddedilir." Seçilmeyenler: yalnız aynı teknik değer; serbest, uyarı ile. |
| 11 (24. tur m.2) | Yeni emirde seçilen etiketin sahipliği farklı olabilir mi? (Örnek: plan şirket malına göreyken müşteriye ait fason malzeme seçmek ya da tersi.) | "Aynı sahiplik zorunlu (Önerilen)" (aynen) | Seçenek metni (aynen): "Şirket malı yerine yalnız şirket malı, müşteri malı yerine yalnız aynı müşterinin malı seçilebilir; farklıysa sistem reddeder." Seçilmeyen: uyarıyla izin. |
| 12 (26. tur m.1; AT-044) | Dilmede ana bobin: bir dilme planı bobinin yalnız bir kısmını dilebilir mi? (Örnek: etikette 12 t var, sipariş için 5 t dilinir, kalan 7 t bobin olarak depoda kalır.) | "Kısmi dilinir, kalan aynı etikette (Önerilen)" (aynen) | Seçenek metni (aynen): "Planlanan kg etiket bakiyesini aşamaz; kalan kısım aynı etiket numarasıyla, düşmüş bakiyeyle depoda kalır. İzlenebilirlik bozulmaz, yeni etiket basılmaz." Seçilmeyenler: kısmi dilinir, kalan yeni etiket alır; her zaman bobinin tamamı. |
| 13 (26. tur m.2) | İptal edilen üretim emrinin yerine yeni emir açılırken plan yeniden onaylanıyor. Bobin/etiket ayırması ne zaman yapılsın? | "Yeniden onayda ayrılır (Önerilen)" (aynen) | Seçenek metni (aynen): "Bugünkü ilk onayla aynı: plan onaylanınca etiketler ayrılır, yeni emir bu ayırmayı kullanır. Arada etiketler boşta görünmez, çift ayırma olmaz. Sinan'ın "yeni emirde yeniden ayır" kararı bu adımda uygulanır." Seçilmeyen: yeni emir açılınca ayrılır. |
| 14 (27. tur) | Üretim emri iptal edilince plan yeniden onaylanırken, ilk onaydan sonra Ayarlar'daki üretim/dilimleme parametreleri değişmişse ne olsun? | "Değiştiyse reddedilsin (Önerilen)" (aynen) | Seçenek metni (aynen): "Ayarlar aynıysa yeniden onay yapılır. Ayarlar değiştiyse yeniden onay reddedilir; plan iptal edilip yeni planla devam edilir. İlk onayın kaydı ve anlamı değişmez (DEC-PRDCT-008)." Seçilmeyenler: yeni ayarlarla yeniden hesaplayıp onaylamak; ilk onayın ayarlarını değişmiş olsalar da korumak. |

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
| URT-D28 | Başlamış üretim emrinde düzeltme ve iptal (F-009) | Karar, Sinan'ın yukarıda aynen verilen 2. soru metnidir. Aşağıdaki (a)–(d) o metnin **koordinatör okumasıdır (geri alınabilir)**: (a) Bir emir satırında **sevk edilmiş miktar değiştirilemez**. Satır bu miktarın altına indirilemez, sevk varken satır silinemez. (b) **Sevk edilmemiş miktar kadar** satır güncellenebilir. Satırda hiç sevk yoksa satırın gerçekleştirmesi tamamen geri alınabilir. (c) Satırlar sevk edilen veya gerçekten üretilen miktara çekildikten sonra **emir tamamlanabilir**. (d) Emirde **hiç sevk yoksa** ve üretim yanlışlıkla yapıldıysa satırların üretim gerçekleştirmesi geri alınır. **Hammadde eski miktarına dönünce emir iptal edilebilir**. (e) Sinan'ın sözü: uygulama **analiz ve yol haritası** belgesinden sonra başlar. | `docs/delivery/URETIM_EMRI_DUZELTME_ANALIZ_v1.md`; Üretim emir aggregate'i, Stok ters hareketi, Lojistik sevk okuması |
| URT-D28 uygulama | Kullanıcının gördüğü "silme" | Mimari değişmez gereği (AT-052, Stok "silme yerine ters kayıt"), satır silme ve emir silme veritabanında **ters kayıt + iptal durumu** ile yapılır. Hareket ve olgu fiziksel olarak silinmez, denetim izi kalır. Bu Sinan'ın kararını değiştirmez, nasıl uygulanacağını söyler. | Üretim ve Stok kayıtları |
| URT-D29 | Termin (F-010 / OPEN-007) | Sinan'ın sözü: termin **elle girilir**. Sistem hat hızı veya takvimle hesaplamaz. Kapasite ve takvim hesabı v1 kapsamı dışıdır. Hangi kayıtta (plan veya emir) tutulacağı uygulama ayrıntısıdır (geri alınabilir); analiz belgesi §8'de önerilir. | Üretim planı / emri; ekranlar |
| URT-D30 | "Satır" (N1) | Sinan'ın sözü: "Plan bazında üretime girecek veya üretimden çıkacak her ürün satırı". Koordinatörün 7. tur okuması (geri alınabilir): satır, planın **girdi ürün satırı** ve **çıktı ürün satırıdır**, girdi rulosu değildir. Kilit ve düzeltme sınırı, o ürün satırına bağlı gerçekleşmelerin **toplamı** üzerinden uygulanır. Rulo ve etiket bu satırın altındaki ayrıntıdır. **Yazar okuması (geri alınabilir):** toplama giren gerçekleşmeler çıkış, iade ve çıktı beyanıdır. Ters kayıt fiziksel olarak yine etiket üzerinde yapılır. Bugünkü koddaki karşılığı analiz belgesinin §3'ündedir. | Analiz §3, §4.2; Üretim emir aggregate'i |
| URT-D31 | Kilitli çıktı (N2) | Sinan'ın sözü: sevk edilmiş çıktıya ek olarak **başka emre girdi olarak verilmiş, transfer edilmiş veya satışa ayrılmış** çıktı da kilitlidir. Düzeltmeden önce o iş geri alınır (ayırma bırakılır, transfer geri alınır). **Yazar okuması (geri alınabilir):** bu işleri Üretim kendisi geri almaz, kendi modüllerinin komutları yapar. | Stok etiket durum okuma portu; analiz §4.2, §4.5 |
| URT-D32 | Kapalı emri yeniden açma (N3) | Sinan'ın sözü: kapalı üretim emrini **yetkili kişi gerekçeyle yeniden açabilir**. Düzeltme ters kayıtla yapılır ve denetim kaydına düşer. **Muhasebe dönemi kapanmışsa** emir yeniden açılamaz. Yeni yetki adı yalnız kayıt olarak eklenir; grant insan kapısıdır. Dönemin nasıl okunacağı §Sınırlar'dadır. Yeniden açılan emirde geri alınan çıktının Stok maliyeti 11. tur m.2'ye bağlıdır (Kaynak). | Üretim emir aggregate'i; analiz §4.6; yeni yetki adı |
| URT-D33 | İptal edilen emrin planı (N4) | Sinan'ın sözü: emir iptal edilince plan **Onaylı'ya döner** ve aynı plandan yeniden üretim emri açılabilir. **URT-D1 ile ilişkisi:** URT-D1 "tek plan = tek emir" der. URT-D33 daha yeni karardır ve URT-D1'i iptal edilmiş emir için daraltır. **Yazar okuması (geri alınabilir):** bir planın aynı anda en çok bir **iptal edilmemiş** emri olur. İptal edilmiş emirler planın geçmişinde kalır. Bugünkü kodda bu kural tam tekil indeksle (`ManufacturingDbContext.cs:508`, `(CompanyId, PlanId)` unique) ve `PlanOrderExists` ret koduyla tutulur. D5, iptal edilmemiş emirle sınırlı filtreli tekil indekse geçer: migration, forward-fix Down ve yarış testi gerekir, K3'tür (analiz §4.7, §5). | Üretim planı ve emri; analiz §4.7 |
| URT-D34 | İptal sonrası yeni emirde girdi ayırması (20. tur m.1) | Sinan'ın seçtiği seçenek: **Yeni emirde yeniden ayır** (önerilen). Açıklaması: plan Onaylı'ya döner; yeni emir açılırken stok yeniden kontrol edilip ayrılır; yetmezse emir açılmaz ve eksik girdiler açıkça gösterilir. **Yazar okuması (geri alınabilir; mimari değişmezden ve K3 tanımından gelir, politika seçmez):** ayırma Stok'un ayırma portu üzerinden yapılır ve fail-closed'dır: port okunamazsa yeniden onay reddedilir (yeniden onaylanmamış planda emir açılmaz). Cross-module sözleşme ve stok kuralı olduğu için K3'tür. **Ayırma anı (26. tur m.2, koordinatör okuması [`6823809221`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823809221) m.2, geri alınabilir):** iptal sonrası her yeni emir, girdiler değişse de değişmese de planın yeniden onayından geçer; ayırma bu adımda URT-D21 yolundan, yeni plan sürümünden türeyen anahtarla yapılır. Emir açılışı ayırmaz. Girdi değişmediyse yeniden onay yeniden hesap yapmaz, yalnız yeniden ayırır. **Legacy plan (yazar okuması, r6 review A `6824391793` / B `6824411703`):** hiç `PlanInputAllocations` satırı olmayan (ayırmasız onaylanmış) planda yeniden onay reddedilir; çıkış yolu plan iptali + yeni plandır. | Üretim emir açma; Stok ayırma portu; D5'te plan sürümü başına ayırma kimliği ve `PlanInputAllocations` şema değişikliği (K3); analiz §4.7 |
| URT-D35 | Emir açılırken girdi etiketinin seçimi (22. tur m.1) | Sinan'ın sözü: "Etiket önemli, kullanıcı kendisi seçer". Koordinatör okuması (lafzi): emir açılırken her girdi ürünü için ayrılacak etiketleri **kullanıcı seçer**; sistem otomatik FIFO veya planda kayıtlı etiket seçimi yapmaz. **Kapsam (yazar okuması, geri alınabilir):** bu seçim URT-D34'ün iptal sonrası yeni emirdeki yeniden ayırmasıdır. İlk emirde URT-D21 ve URT-D3 aynen sürer: plan onayında planı hazırlayan kullanıcının planda seçtiği etiket ayrılır. Yeni emirde plandakinden farklı etiket seçilirse URT-D37 ve URT-D38 uygulanır. URT-D34 aynen sürer: seçilen etiketlerin serbest kg'ı ihtiyacı karşılamıyorsa yeniden onay reddedilir (yeniden onaylanmamış planda emir açılmaz), eksik girdi/kg gösterilir; kısmi ayırma yoktur, ayırma all-or-nothing ve fail-closed'dır. **Ayırma anı (26. tur m.2, koordinatör okuması [`6823809221`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823809221) m.2, geri alınabilir):** seçim emir açılışında değil, iptal sonrası planın yeniden onay adımında yapılır; ayırma o adımda URT-D21 yolundan yapılır. Ekrandaki etiket/serbest kg listesi (Stok'un önerdiği yan etkisiz okuma portu) bu kararın aracıdır, karar kaynağı değildir; nihai kontrol Stok'un ayırma komutundadır. Seçilmeyen: otomatik FIFO, planda kayıtlı etiketler. **D5 sınırı (yazar okuması, geri alınabilir):** yeniden onay isteği her girdi için kullanıcının seçtiği etiketi taşır, sistem doldurmaz; D5b'ye kadar yalnız plan etiketi kabul edilir (geçici fail-closed sınır, otomatik seçim değildir). Cross-module, K3. | Üretim emir açma ekranı ve komutu; D5 yeniden onay adımı (analiz §4.7 (d2)); Stok Free kg okuma portu ve ayırma komutu; analiz §4.7, §6 |
| URT-D36 | Yeniden açılan emrin çıktısının ters hareket maliyeti (11. tur m.2; 22. tur m.2 teyit) | İki kaynak birlikte: (a) 11. tur m.2 ([`6820601088`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820601088)): "Kendi giriş maliyetiyle ters hareket"; geri alınan çıktı kendi giriş birim maliyetiyle ters hareketle stoktan çıkar, kalan stoğun ortalaması yeniden hesaplanır, **geçmiş çıkış maliyetleri geriye dönük düzeltilmez**. (b) 22. tur m.2 ([`6822949341`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822949341)) aynı kuralı yeniden açılan emir için teyit eder: "Emrin kendi maliyeti (Önerilen)". İçerik aynıdır; 22. tur yeni bir kural koymaz. Anlamı (koordinatör okuması, geri alınabilir): geri alınan çıktı, **girişte kaydedildiği maliyetle aynen** ters harekete alınır; kalan stoğun ortalaması bu girişin etkisi çıkarılarak yeniden hesaplanır (deterministik backend calculator). Geçmiş çıkış maliyetleri geriye dönük düzeltilmez. **Geçici fail-closed (D8 dilimi, analiz §4.6 madde 5):** Stok bu ters hareketi portunda sunana kadar maliyeti ortalamaya girmiş emrin yeniden açılması reddedilir. Bu kalıcı bir "yeniden açılmaz" kuralı değildir; port gelince URT-D32 ve bu karar uygulanır. (22. tur kaydındaki "URT-D8 aynen sürer" atfı yanlıştı, URT-D8 hurda/fire kararıdır; koordinatör düzeltmesi [`6823484848`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823484848).) Ters hareket ve ortalama hesabı Stok'un kodudur; Üretim yalnız komutu tetikler. Seçilmeyen: güncel ortalama maliyet. Para ve stok değeri etkisi olduğu için K3; kod PR'ı golden örnek taşır. | Stok ters hareketi ve ortalama calculator'ı; Üretim yeniden açma (D8); analiz §4.6 |
| URT-D37 | Yeni emirde plandakinden farklı etiket (24. tur m.1) | Sinan'ın seçtiği seçenek: **"Plan yeniden hesaplanır (Önerilen)"**. Seçenek metni (aynen): "Kullanıcı başka etiket seçebilir; sistem planı yeni etikete göre yeniden hesaplar (en, fire, kütle dengesi) ve plan yeniden onaya düşer. Uymuyorsa seçim reddedilir." Seçilmeyenler: yalnız aynı teknik değer; serbest, uyarı ile. **Yazar okuması (geri alınabilir; politika seçmez):** yeniden hesap planın bugünkü hesap motoruyla yapılır (deterministik backend calculator; ekran yeniden hesaplamaz). Plan girdisinin etikete bağlı alanları (`PlannedInput.ParentRollId`, `ExpectedLabelVersion`, `TechnicalSnapshot`, `PlannedMass`) yeni etikete göre değişir; `CommercialContext` değişmez (URT-D38). Eski değerler silinmez: main'deki taslak revizyon tarihçesi (`PlanRevisions` / `PlanRevisionInputs`, MFG-PLAN-EDIT) deseniyle eklenir, plan satırı ilk içeriğini korur; URT-D3 etiket izi korunur. Yeniden hesaplanan plan onaylanmadan emir açılmaz. Motorun reddettiği seçim (ör. net kullanılabilir en yeni bobin genişliğini aşar, kütle dengesi tutmaz) reddedilir. **Uygulama sınırı (26. tur, koordinatör notu 6823809221):** iş soruları cevaplıdır (m.1: planlanan kg etiketin güncel bakiyesini aşamaz, kalan aynı etikette kalır; m.2: ayırma yeniden onayda URT-D21 yolundan yapılır; emir açılışı istisnasız hiçbir durumda ayırmaz, girdi değişmese de yeniden onay vardır — URT-D34; kaynak: koordinatör okuması 6823809221 m.2 ve koordinatör kararı (a), r6 oturum mesajı, r6 review A `6824391793` / B `6824411703`). AT-044 kuralı genel dilme kuralıdır; ilk kayıt, taslak revizyon ve onayda da D5b ile gelir (analiz §4.7 (e)). Main'de yalnız taslak plan revize edilir (`ProductionPlan.RequireRevisable`, `PlanNotDraft`) ve dilme ayrıntılı plan `manufacturing_plan_slitting_spec_fixed` ile kilitlidir. Onaylı → Onaylı yeniden onay geçişinin tek sahibi D5'tir (analiz §4.7 (d2)); farklı etiketle yeniden onay, revizyon tarihçesi, kısıtın kaldırılması ve HamKg ≤ güncel bakiye kontrolü (Stok portundan okunur, aşan plan reddedilir) ayrı bir K3 uygulama dilimidir. Kısıt o dilim main'e gelene kadar yerinde kalır. Para/stok/üretim kuralı ve cross-module sözleşme olduğu için K3. | Üretim planı (yeniden hesap, yeniden onay); emir açma; analiz §4.7 (e), §5 D5b |
| URT-D38 | Yeni emirde seçilen etiketin sahipliği (24. tur m.2) | Sinan'ın seçtiği seçenek: **"Aynı sahiplik zorunlu (Önerilen)"**. Seçenek metni (aynen): "Şirket malı yerine yalnız şirket malı, müşteri malı yerine yalnız aynı müşterinin malı seçilebilir; farklıysa sistem reddeder." Seçilmeyen: uyarıyla izin. **Yazar okuması (geri alınabilir):** kontrol backend'dedir (UI tek güvenlik değildir); sahiplik bilgisi Stok'un etiket okumasından gelir, okunamazsa seçim reddedilir (fail-closed). Karşılaştırma anahtarı `CommercialContext.PartyId`'dir (`CompanyPartyAccountId` değil): `CommercialContext` null ise girdi şirket malıdır ve yalnız şirket malı etiket kabul edilir (dayanak: main host ve Stok `OwnedAsPlanned` bunu zaten böyle uygular, null girdinin şirket malı oluşu ilk onayda Stok tarafından doğrulanmıştır; `PlannedInput.cs:55-58` docstring'iyle gerilim analiz §4.7 (e)'de açıklanır; ayırmasız onaylanmış legacy plan reddedilir); dolu ise etiketin sahibi aynı `PartyId` olmalıdır. Main'de Stok'un plan ayırması bunu zaten uygular (URT-D22, `OwnedAsPlanned`, `InventoryManufacturingRealizationParticipant.PlanAllocations.cs:284-299`; Üretim `CustomerPartyId = CommercialContext.PartyId` gönderir, `ManufacturingPlanAllocationHost.cs:52-60`). Yeniden onaydaki ayırma aynı kontrolden geçer; Üretim'in ekran ve komut kontrolü erken ret içindir. K3. | Üretim emir açma ve plan yeniden hesabı; D5 yeniden onay adımı (analiz §4.7 (d2)); Stok etiket okuması; analiz §4.7 |
| URT-D39 | Yeniden onayda değişmiş ayarlar (27. tur, [`6824450207`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6824450207)) | Sinan'ın seçtiği seçenek: **"Değiştiyse reddedilsin (Önerilen)"**. Seçenek metni (aynen): "Ayarlar aynıysa yeniden onay yapılır. Ayarlar değiştiyse yeniden onay reddedilir; plan iptal edilip yeni planla devam edilir. İlk onayın kaydı ve anlamı değişmez (DEC-PRDCT-008)." Seçilmeyenler: yeni ayarlarla yeniden hesaplayıp onaylamak; ilk onayın ayarlarını değişmiş olsalar da korumak. **Yazar okuması (geri alınabilir):** iptal sonrası yeniden onayda (D5) güncel Settings üretim/dilimleme profili okunur ve ilk kuşağın DEC-PRDCT-008 snapshot'ıyla karşılaştırılır. Aynıysa yeni onay kuşağı aynı değerlerle (kopya) kaydedilir ve `SlittingApprovalGate` yeniden koşar. Farklıysa yeniden onay reddedilir, ayırma yapılmaz, eski snapshot ve ilk onay kaydı değişmez; kullanıcı planı iptal edip yeni planla devam eder. Profil okunamazsa ret (fail-closed). **"Aynı" ölçütü (koordinatör kararı, #1114 r7 A `6824765446` P2; fail-closed yazar okuması, geri alınabilir; bu cevabın uygulanış biçimidir):** karşılaştırma anahtarı `ProfileId` + `ProfileVersion` + `Source` + `Process` + `WorkCenterId` + geçerlilik penceresi + yalnız profilin kendi (Settings kaynaklı) değerleri; `SelectedForDate` (onay günü damgası) ve plana girilmiş değerler (`Source = "Plan"`) karşılaştırma dışıdır. Yeniden onay günü için farklı profil veya pencere seçiliyorsa ret; ilk kuşak snapshot'ı null ise ret (izin verilip verilmeyeceği NEEDS-SINAN adayı, #66); null ↔ dolu değişimi "değişti" sayılır; kopya kuşak ilk kuşağın `SelectedForDate`'ini korur. Problem kodu (**yazar önerisi**, ad D5 PR'ında kesinleşir): `manufacturing_plan_reapproval_settings_changed` (domain kodu `ManufacturingRuleCodes.PlanReapprovalSettingsChanged`), 422 (main'de domain kuralları 422, sürüm çakışması 409). Kapsam: D5; D5b genişletir. K3. | Üretim D5 yeniden onay adımı (D5b genişletir); Settings profil okuması; analiz §4.7 (d2), §5 D5 |

## Bağlı kararlar (başka şeridin kimliği)

| Kimlik | Karar | Üretim tarafındaki uygulama |
|---|---|---|
| SAT-D308 (#1108; SAT-D307 (3) notunu cevaplar) | Fire maliyete yükleme anahtarı **kapalıyken** fire ürün maliyetine yüklenmez; **ayrı fire/dönem gideri** olarak raporlanır. Anahtar açıkken SAT-D307 (1)-(2) geçerlidir. Hesap eşlemesi `NEEDS-SPEC`; gelene kadar gider kaydı açılmaz, yalnız raporlanır (SAT-D308 (5)–(6) kaynakta "yazarın okumasıdır, geri alınabilir" etiketlidir). | Üretim alış maliyeti hesaplayıcısının anahtar kapalı yolu SAT-D308'e göre yazılır. Bu belge formül yazmaz. Formül `NEEDS-SPEC`'tir: ChatGPT golden örneği ve gerekirse `NEEDS-SINAN` ile gelir. Kod PR'ı K3'tür ve golden örnek taşır. Fire tutarının Stok ortalamasında ve maliyet portunda nasıl taşınacağı Stok'un işidir (SAT-D308 karar dosyası, "SAT-D308 — Üretim maliyet portu dilimi"); burada yazılmaz. |

## Sınırlar

- URT-D28'in açık bıraktığı dört soru (N1–N4) URT-D30…URT-D33 ile cevaplandı. Bunlardan doğan yeni emirde ayırma sorusu URT-D34 ile, etiket seçimi URT-D35 ile, yeniden açılan emirde ters hareket maliyeti URT-D36 ile, yeni emirde farklı etiket URT-D37 ile, sahiplik URT-D38 ile, yeniden onayda değişmiş ayarlar URT-D39 ile cevaplandı. 26. tur (N10/N11): planlanan kg etiketin güncel bakiyesini aşamaz, kalan aynı etikette kalır; iptal sonrası ayırma yeniden onayda yapılır, emir açılışı ayırmaz. Çıktı ürün satırının bugünkü kodda
  karşılığı olmaması (çıktı beyanı plan çıktı satırına bağlı değil) bir uygulama ayrıntısıdır; analiz belgesi §3'te
  `ASSUMPTION-URT-F009-1` olarak yazılır ve geri alınabilir.
- "Muhasebe dönemi kapalı" bilgisinin sahibi Muhasebe'dir. Yeni port açılmaz. Üretim, main'deki
  `IAccountingPeriodStatusPortV1`'i (`src/backend/Modules/Accounting/…/Contracts/IAccountingPeriodStatusPortV1.cs`) kullanır.
  Port bir tarih için `Open` / `NotOpen` / `None` / `Denied` döner (Finans planı: `FINANS_F018_KREDI_LIMITI_PLANI_v1.md` §3,
  Üretim satırı). **Eşleme yazar okumasıdır (fail-closed, geri alınabilir):** yeniden açma yalnız `Open`'da yapılır. `NotOpen` reddedilir;
  port belgesine göre `NotOpen` önerilmiş (onaylanmamış) veya kapalı dönemi kapsar, bu yüzden ret kodu nötr adlandırılır
  ("muhasebe dönemi açık değil") ve ret metni portun `Status` alanına göre `Proposed` ile `Closed`'u ayırır. Sinan'ın
  kuralı yalnız kapalı dönemi söyler; `Proposed` ve `None`'da ret kuralı fail-closed yönde daraltır. `None` ve `Denied` ayrı
  ret kodlarıyla reddedilir. **`Denied` "dönem kapalı" diye gösterilmez.** Port aktörün `accounting.periods.read`
  yetkisiyle okur. Üretim kullanıcısına bu yetkinin verilmesi grant kararıdır (insan kapısı). Hangi tarihin dönemi
  belirlediği Üretim'in kuralıdır (analiz §4.6).
- Yeni yetki adları (düzeltme, iptal, yeniden açma) yalnız kayıt olarak eklenir. Rol, grant ve Critical işaretleme insan kapısıdır.
- SAT-D308, #1108 ile main'dedir (`2efb3f68d`).
- Dağıtım, ortam, secret, Ayarlar etkinleştirme, grant/rol/Critical, production etkinleştirme
  (`production.execution.enabled`), `OPEN-*` kapatma ve `.claude/settings.json` insan kapıları değişmez. URT-D29
  OPEN-007'yi kapatmaz.
