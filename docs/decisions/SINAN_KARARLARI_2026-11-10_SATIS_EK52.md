# Sinan Kararları — 2026-11-10 (Satış: S-05 marj maliyeti — maliyetin sahibi Stok, sipariş anı anlık görüntüsü, yalnız alış maliyeti, iç hassasiyet ve yuvarlama, line-pricing marj alanı, iç onayda sabitleme, revizyon, okunamayan port)

Bu belge önceki karar dosyalarını silmez; SAT-D262…SAT-D270'i ekler. EK51
(`SINAN_KARARLARI_2026-11-09_SATIS_EK51.md`) "Sınırlar"da açık bırakılan üç noktayı (hareketli ortalama maliyet
üreticisinin sahibi, SAT-D256'daki "satış anı", SAT-D261'deki ara yuvarlama ve iç hassasiyet), EK52'nin ilk taslağında `NEEDS-SINAN` bırakılan taslak aşaması önizlemesi
sorusunu, anlık görüntünün koddaki adımı, revizyon ve okunamayan port sorularını (SAT-D268…SAT-D270) ve EK49
(`SINAN_KARARLARI_2026-11-09_SATIS_EK49.md`) SAT-D247'deki "API-50 marj alanı" ifadesinin hangi uç noktayı
anlattığını kayda geçirir. SAT-D247, SAT-D250 (EK49), SAT-D256…SAT-D261 (EK51), DEC-PRD-011 ve `OPEN-008`
geçerlidir; bu belge onların metnini değiştirmez (SAT-D256 ve SAT-D261'deki açık kısımların cevaplanması için
"Bilinçli değişiklikler"e bakın). Sinan'ın "Taslak siparişe dönüşünce" cevabının koddaki karşılığını Sinan sonradan
**iç onay** olarak belirtmiştir (SAT-D268).

**Kaynak:**

- Sinan'ın 10 Kasım 2026'da Claude Code cloud oturumunda (`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine
  doğrudan sorularla verdiği cevaplar (sorular 1–5: Issue #66 yorumu
  [6760682324](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6760682324); cevapların özeti: #66 yorumu
  [6760794644](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6760794644)). Cevapların anlamı bu belgeyi yazan ajana koordinatör tarafından iletildi ve
  aşağıda iletildiği biçimiyle kayda geçirildi. Soruların seçilmeyen seçenekleri, seçenek açıklamaları ve
  "(Önerilen)" işaretleri bu ajana iletilmediği için yazılmadı (uydurulmadı); "Cevap" sütunu aynen alıntı değil,
  iletilen cevabın anlamıdır.
- **"Yalnız alış maliyeti" ekran etiketi (soru 2, SAT-D263 (2)):** kaynağı şerit sohbetindeki cevaptır (koordinatör
  tarafından iletildi); #66 yorumu 6760794644 etiketten söz etmez.
- **SAT-D267 (soru 6):** bu belgenin ilk taslağında açılan `NEEDS-SINAN` maddesine (taslak aşamasında marj önizlemesi ve
  "sipariş anı"nın komut karşılığı) Sinan'ın aynı gün (10 Kasım 2026) Satış (Claude) şeridine doğrudan sorularla
  verdiği cevaplar; koordinatör tarafından iletildi. Fiyat okuma izni olanların da önizlemeyi görüp görmeyeceği
  sorulduğunda Sinan görünürlüğü açıkça yeniden teyit etti: "Yalnız GM ve Admin kalsın" (aynen). Anlık görüntünün
  zamanı için Sinan'ın sözü: "Taslak siparişe dönüşünce" (aynen). Öteki ifadeler iletilen anlamdır; seçilmeyen
  seçenekler iletilmediği için yazılmadı.
- **SAT-D268…SAT-D270 (sorular 7–9):** kodda ayrı bir taslak→sipariş adımı olmadığı için (sipariş `SalesOrderDraft`
  olarak Taslak durumunda kalır; onaya gönderim, iç onay ve ayrı müşteri kabulü adımları vardır) Sinan'a üç soru #66
  yorumu [6761194105](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6761194105) ile soruldu. Sinan aynı
  gün (10 Kasım 2026) Satış (Claude) şeridine doğrudan sorularla cevap verdi; cevaplar #66 yorumu
  [6761272162](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6761272162) ile kayda geçti ve koordinatör tarafından bu ajana iletildi. "Cevap" sütunu iletilen anlamdır;
  seçilmeyen seçenekler iletilmediği için yazılmadı.

Bu belge merge makbuzu dışında Issue #66'ya karar yazmaz.

## Soru ve cevaplar

| # | Soru | Cevap (iletilen anlam) | Kayda geçen anlam |
|---|---|---|---|
| 1 | Depo bazında hareketli ortalama alış maliyetini hangi modül üretsin? (EK51 "Sınırlar": sahiplik) | Maliyeti **Stok (Inventory)** modülü üretir ve Stok'un sahip olduğu sürümlü bir okuma portuyla verir; Satış sipariş anında okur, sipariş satırına anlık görüntü olarak yazar, sonra değişmez; Satış maliyeti kendisi hesaplamaz. | Üretici ve okuma portu Stok'a aittir; Satış yalnız okur ve anlık görüntüyü kendi sipariş satırında saklar. |
| 2 | İlk aşamada marjda hangi maliyet türleri kullanılsın? | İlk aşamada **yalnız alış maliyeti**; ekran bunu "yalnız alış maliyeti" diye etiketler (etiket: şerit sohbeti cevabı, #66'da yok). İkinci maliyet türü (vade farkı, öteki giderler/ek maliyetler) ayrı ve ileride verilecek bir karardır. | Marj yalnız alış maliyetiyle verilir ve ekranda öyle etiketlenir; ikinci maliyet türü açık kalır. |
| 3 | SAT-D256'daki "satış anı" hangi zaman? (EK51 "Sınırlar" `NEEDS-SINAN`) | **Sipariş anı.** | Marjda kullanılan hareketli ortalama sipariş anındaki ortalamadır. |
| 4 | Ara yuvarlama ve iç hassasiyet ne olsun? (EK51 "Sınırlar" `NEEDS-SINAN`, SAT-D261) | Marj yüzdesi **yuvarlanmamış marj tutarından** hesaplanır; hareketli ortalama maliyetin iç hassasiyeti **4 ondalık basamaktır**; gösterimde tutar kuruşa, yüzde 2 ondalık basamağa yuvarlanır. | Yüzde yuvarlanmamış marj tutarından hesaplanır, yuvarlanmış (kuruşa indirilmiş) tutardan türetilmez; hareketli ortalama maliyetin iç hassasiyeti 4 ondalık basamaktır; gösterimde tutar kuruşa, yüzde 0,01'e yuvarlanır; yuvarlama kipi mevcut para yuvarlama kuralıdır (bağlayıcı metin SAT-D265). |
| 5 | EK49'daki "API-50 marj alanı" hangi uç noktadır? | Satış'ın sahip olduğu `GET /api/v1/sales/drafts/{id}/line-pricing` okumasıdır; Ayarlar'daki API-50 (iskonto limiti politikası) değişmez. | Marj alanları Satış'ın line-pricing okumasında yer alır; Ayarlar API-50 bu işten etkilenmez. |
| 6 | Taslak aşamasında line-pricing marjı Stok portunun o anki cevabıyla önizleme olarak gösterilsin mi; anlık görüntü hangi komutta yazılsın; fiyat okuma izni olanlar da görsün mü? (EK52 ilk taslak `NEEDS-SINAN`) | Taslakta canlı **önizleme** marjı gösterilebilir; "tahmini/önizleme" diye etiketlenir ve **saklanmaz**; satır/sayfa kalabalık olmasın diye ayrı bir **"Marjı göster"** düğmesiyle açılır. Görünürlük: "Yalnız GM ve Admin kalsın". Anlık görüntünün zamanı: "Taslak siparişe dönüşünce" (aynen); sonra değişmez. | Önizleme saklanmayan, etiketli, düğmeyle açılan bir okumadır; görünürlük SAT-D250 aynen. "Taslak siparişe dönüşünce" ifadesinin koddaki adımını Sinan soru 7'de iç onay olarak belirtti (SAT-D268). |
| 7 | Alış maliyeti anlık görüntüsü hangi adımda yazılsın: onaya gönderim, iç onay ya da müşteri kabulü? (#66 yorumu 6761194105, soru 1) | **İç onayda** (GM veya yetkili onaycının onayı). | "Taslak siparişe dönüşünce" = iç onay; anlık görüntü iç onay komutunda yazılır (SAT-D268). |
| 8 | Onaylı siparişin revizyonunda değişen veya yeni eklenen satırların anlık görüntüsüne ne olsun? (#66 yorumu 6761194105, soru 2) | Revizyon onaylanınca **yalnız değişen ve yeni satırlar** yeniden sabitlenir; değişmeyen satırlar eski anlık görüntüyü korur. | Revizyon onayında yalnız değişen/yeni satırlar yeniden okunur ve sabitlenir (SAT-D269). |
| 9 | Port o an `Unreadable` dönerse marj kalıcı olarak "kullanılamıyor" mu kalsın, yoksa sonradan bir kez tamamlanabilsin mi? (#66 yorumu 6761194105, soru 3) | Sabitleme anında maliyet okunamazsa (`Unreadable`/`NoSource`) **onay engellenmez**; o satırın marjı **kalıcı olarak "kullanılamıyor"** kaydedilir. | Onay engellenmez; satırda kalıcı "kullanılamıyor" kaydı; sonradan tamamlama yok (SAT-D270). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D262 | Maliyet üreticisinin sahibi (EK51 "Sınırlar" sahiplik sorusunun cevabı) | (1) SAT-D256…SAT-D258 ve SAT-D260'taki depo bazında, `CompanyId` kapsamlı hareketli ortalama **alış** maliyetini **Stok (Inventory) modülü üretir** (soru 1). (2) Maliyet, **Stok'un sahip olduğu sürümlü bir okuma portuyla** verilir; portun cevabı EK51'deki gibi değer, `Unreadable` (okunamıyor) ve `NoSource` (kayıt yok) durumlarını ayırır. (3) **Satış maliyeti hiçbir zaman kendisi hesaplamaz**; Stok'un hiçbir tablosuna yazmaz ve o tabloları doğrudan okumaz (Mimari değişmez: bir modül başka modül tablosuna doğrudan yazmaz). (4) Satış maliyeti **sipariş anında** (SAT-D264) porttan okur ve **satış siparişi satırına anlık görüntü (snapshot) olarak yazar**; bu anlık görüntü sonradan **değişmez** (sonraki stok girişleri, transferler veya ortalama güncellemeleri kayıtlı siparişin maliyetini değiştirmez). (5) Port `Unreadable` veya `NoSource` döndürürse ya da EK51'de `NEEDS-SINAN` kalan bir durum gerekiyorsa marj **"kullanılamıyor"** kalır (SAT-D247 fail-closed); sıfır veya tahmini maliyet yazılmaz. Master v3'teki M-09 Maliyet Muhasebesi kapısı bu kararla S-05 marjının maliyet üreticisi olmaz; genel stok değerleme ve muhasebe maliyet yöntemi `OPEN-008`'de açık kalır | Stok modülü (hareketli ortalama maliyet üreticisi + sürümlü okuma portu, yeni), Satış sipariş satırı (maliyet anlık görüntüsü), marj hesaplayıcısı (SAT-D247) |
| SAT-D263 | İlk aşamada maliyet türü | (1) İlk aşamada marj **yalnız alış maliyetiyle** hesaplanır (soru 2; SAT-D258 ile aynı yönde). (2) S-05 ekranı marjın maliyet tarafını **"yalnız alış maliyeti"** diye etiketler (kaynak: şerit sohbetindeki cevap; #66'da yok); etiket metni Türkçe kullanıcı metni olarak kaynak dosyadadır. (3) **İkinci maliyet türü** — vade farkı, öteki giderler ve ek (landed) maliyetler — **ayrı ve ileride verilecek bir karardır**; bu belgede seçilmez, açık kalır (bkz. "Sınırlar"). O karar verilene kadar bu maliyetler marja eklenmez, sıfır sayıldığı ima edilmez | Marj hesaplayıcısı, line-pricing marj alanları, S-05 (UI-50) etiketi |
| SAT-D264 | "Satış anı" (SAT-D256 açık kısmının cevabı) | SAT-D256'daki **"satış anı" sipariş anıdır** (soru 3). Marjda kullanılan hareketli ortalama, sipariş anında Stok portunun verdiği ortalamadır ve SAT-D262 (4) uyarınca sipariş satırına anlık görüntü olarak yazılır. Fatura zamanındaki ortalama marjda kullanılmaz | Marj hesaplayıcısı, Satış sipariş satırı anlık görüntüsü |
| SAT-D265 | İç hassasiyet ve yuvarlama (SAT-D261 açık kısmının cevabı) | (1) **Marj yüzdesi yuvarlanmamış marj tutarından** hesaplanır; yuvarlanmış (kuruşa indirilmiş) tutardan türetilmez (soru 4). (2) Hareketli ortalama maliyetin **iç hassasiyeti 4 ondalık basamaktır**. (3) **Gösterimde** marj tutarı **kuruşa (0,01 TL)**, marj yüzdesi **2 ondalık basamağa (0,01)** yuvarlanır; SAT-D261'in 0,01 TL / 0,01 hassasiyeti aynen geçerlidir. (4) Yuvarlama kipi için yeni bir kural açılmaz: **mevcut para yuvarlama kuralı** uygulanır; EK51 SAT-D261 bu kuralı `Money` değer nesnesindeki yarımda sıfırdan uzağa yuvarlama (`MidpointRounding.AwayFromZero`) olarak kaydetmiştir. (5) Bu kurallar formül golden'ında (ChatGPT şeridi) kilitlenir; golden main'e girene kadar formül etkinleştirilmez | Marj hesaplayıcısı, Stok hareketli ortalama hesabı (iç hassasiyet), formül golden'ı |
| SAT-D266 | Marjın uç noktası (EK49 SAT-D247/SAT-D250 "API-50 marj alanı" ifadesinin netleşmesi) | (1) EK49'da "API-50 marj alanı" diye anılan yer, **Satış'ın sahip olduğu `GET /api/v1/sales/drafts/{id}/line-pricing` okumasıdır** (soru 5). (2) Marj tutarı ve yüzdesi (SAT-D259) bu okumanın cevabında yer alır; SAT-D250 görünürlüğü (yalnız Genel Müdür ve Admin; yetkisizde backend'de esirgenir) bu alanlara uygulanır. (3) **Ayarlar'daki API-50** (satış iskonto limiti politikası, `SATIS_HAZIRLIK_PLANI_v1.md` API-50 satırı) **değişmez**; marj alanı oraya eklenmez. EK51'deki "API-50 marj alanları" ifadeleri de aynı biçimde line-pricing okumasını anlatır | `GET /api/v1/sales/drafts/{id}/line-pricing` (`SalesDraftContractsV1`), OpenAPI, S-05 (UI-50); Ayarlar API-50 (değişmez) |
| SAT-D267 | Taslak önizlemesi ve anlık görüntünün zamanı (SAT-D262 (4) ve SAT-D264 ile birlikte okunur) | (1) **Taslak aşamasında** S-05 line-pricing okuması Stok portunun o anki cevabından **canlı bir önizleme marjı** gösterebilir; bu marj **"tahmini/önizleme"** diye etiketlenir ve **saklanmaz** (sipariş satırına, taslağa veya rapora yazılmaz). (2) Önizleme, satır ve sayfa kalabalık olmasın diye **ayrı bir "Marjı göster" düğmesiyle** açılır (UX; Claude frontend şeridi). (3) Görünürlük **aynen SAT-D250'dir**: yalnız Genel Müdür ve Admin; backend'de uygulanır, yetkisize önizleme de verilmez. Sinan fiyat okuyabilenlerin de görüp görmeyeceği sorulduğunda "Yalnız GM ve Admin kalsın" diye yeniden teyit etti; **yeni grant veya izin yoktur**. (4) Sinan'ın sözüyle maliyet anlık görüntüsü **"Taslak siparişe dönüşünce"** sipariş satırına yazılır ve **sonradan değişmez**. Kodda ayrı bir taslak→sipariş adımı yoktur; Sinan bu ifadenin **iç onay** olduğunu sonradan belirtti (SAT-D268); revizyon SAT-D269'da, okunamayan port SAT-D270'te karara bağlanmıştır. (5) Line-pricing okuması, satırın anlık görüntüsü **varsa anlık görüntü marjını**, **yoksa** (iç onaydan önce) (1)–(3)'teki saklanmayan önizlemeyi gösterir | `GET /api/v1/sales/drafts/{id}/line-pricing` (önizleme / anlık görüntü), Satış sipariş satırı, S-05 (UI-50) "Marjı göster" düğmesi |
| SAT-D268 | Anlık görüntünün adımı (SAT-D267 (4) "Taslak siparişe dönüşünce" ifadesinin kod karşılığı) | (1) Alış maliyeti anlık görüntüsü siparişin **iç onayında** (Genel Müdür veya yetkili onaycının onayı; onaya gönderim → iç onay ekseninde `Approved` geçişi) yazılır (soru 7). (2) Onaya gönderimde ve müşteri kabulünde anlık görüntü yazılmaz; müşteri kabulü anlık görüntüyü değiştirmez. (3) SAT-D262 (4) ve SAT-D264'teki "sipariş anı" bu iç onay anıdır; anlık görüntü sonradan değişmez (revizyon istisnası SAT-D269). (4) İç onaydan önce line-pricing yalnız saklanmayan önizlemeyi (SAT-D267 (1)–(3)), iç onaydan sonra satırın anlık görüntü marjını gösterir | Satış iç onay komutu (anlık görüntü yazımı), Satış sipariş satırı, `GET /api/v1/sales/drafts/{id}/line-pricing` |
| SAT-D269 | Revizyonda anlık görüntü | (1) Onaylı siparişin revizyonu **onaylandığında** yalnız **değişen ve yeni eklenen satırlar** için maliyet Stok portundan yeniden okunur ve yeniden sabitlenir (soru 8). (2) **Değişmeyen satırlar eski anlık görüntüsünü korur**. (3) Revizyon onaylanmadan anlık görüntü değişmez | Satış revizyon onayı (commercial/line/quantity revision), Satış sipariş satırı anlık görüntüsü |
| SAT-D270 | Sabitleme anında okunamayan port | (1) İç onayda (SAT-D268) veya revizyon onayında (SAT-D269) Stok portu satır için `Unreadable` veya `NoSource` döndürürse **onay engellenmez** (soru 9). (2) O satırın marjı **kalıcı olarak "kullanılamıyor"** kaydedilir; sonradan yeniden okunmaz ve tamamlanmaz (o satırın yeniden sabitlenmesi yalnız SAT-D269'daki revizyon onayıyla olur). (3) Sıfır veya tahmini maliyet yazılmaz (SAT-D247, SAT-D262 (5)) | Satış iç onay ve revizyon onayı komutları, Satış sipariş satırı anlık görüntüsü |

Uygulama ayrıntısı (geri alınabilir):

- SAT-D262'de anlık görüntünün sipariş satırındaki alan adı, okunan portun sürümünü/kaynak kimliğini birlikte
  saklayıp saklamadığı ve port durumunun (`Unreadable`/`NoSource`) satırda nasıl kayda geçtiği backend sözleşmesinde
  kesinleşir; anlık görüntü portun verdiği değeri 4 basamak iç hassasiyetle (SAT-D265) aynen saklar.
- SAT-D269'da revizyonda "değişen satır" sayılan alanlar mevcut revizyon modelindeki satır/miktar değişikliğinden
  türetilir ve backend sözleşmesinde kesinleşir; bu türetme yeni bir para/stok politikası seçmeyi gerektirirse
  `NEEDS-SINAN` olur.
- SAT-D263'teki etiketin tam metni ve yeri (marj alanının başlığı ya da yardım metni) UI'da belirlenir; "yalnız
  alış maliyeti" anlamı çıkarılamaz.

## Kapatılan açık sorular

- EK51 "Sınırlar" — hareketli ortalama maliyet üreticisinin sahibi (Stok/Inventory ya da M-09) — SAT-D262 (Stok).
- EK51 "Sınırlar" — "satış anı" sipariş zamanı mı, fatura zamanı mı (`NEEDS-SINAN`, SAT-D256) — SAT-D264 (sipariş anı).
- EK51 "Sınırlar" — ara yuvarlama ve hareketli ortalamanın iç hassasiyeti (`NEEDS-SINAN`, SAT-D261) — SAT-D265.
- EK52 ilk taslak "Sınırlar" — taslak aşamasında marj önizlemesi (`NEEDS-SINAN`) — SAT-D267 (1)–(3).
- #66 yorumu 6761194105 — anlık görüntünün adımı, revizyon ve o anda okunamayan port (`NEEDS-SINAN`) — SAT-D268
  (iç onay), SAT-D269 (yalnız değişen/yeni satırlar), SAT-D270 (onay engellenmez, kalıcı "kullanılamıyor").
- EK49 SAT-D247/SAT-D250 — "API-50 marj alanı"nın hangi uç nokta olduğu — SAT-D266 (Satış line-pricing okuması).
- **Bu belge hiçbir `OPEN-*` kaydını kapatmaz.** Yukarıdaki soruların `OPEN-NNN` kimliği yoktur. `OPEN-008` (stok
  değerleme ve ayrıntılı üretim maliyeti) **açık kalır**: SAT-D262…SAT-D270 yalnız S-05 marjının maliyet tarafını
  belirler; genel stok değerleme, muhasebe maliyet yöntemi ve üretim maliyeti seçilmez.

## Bilinçli değişiklikler

- **SAT-D256:** metni değişmez; "satış anı"nın hangi zaman olduğunu seçilmemiş bırakan açık kısmı **SAT-D264 ile
  cevaplanmıştır** (sipariş anı). SAT-D256'nın "seçilene kadar marj kullanılamıyor" koşulu bu noktada kalkar; marjın
  öteki fail-closed koşulları (SAT-D247, SAT-D262 (5)) aynen geçerlidir.
- **SAT-D261:** metni değişmez; ara yuvarlama ve iç hassasiyeti seçilmemiş bırakan açık kısmı **SAT-D265 ile
  cevaplanmıştır**. 0,01 TL / 0,01 hassasiyet ve mevcut `Money` yuvarlama kuralı aynen geçerlidir.
- **EK51 "Uygulama listesi" ve "Sınırlar" sahiplik maddesi:** üreticinin sahibi SAT-D262 ile Stok olarak
  belirlenmiştir; EK51 dosyası yerinde düzenlenmez.
- **SAT-D262 (4) ve SAT-D264:** metni değişmez. Sinan "sipariş anı"nı SAT-D267 (4)'te "Taslak siparişe dönüşünce"
  diye açıkladı, ardından bunun iç onay olduğunu belirtti (SAT-D268). Anlık görüntünün sonradan değişmemesi aynen
  geçerlidir; tek istisna revizyon onayında değişen/yeni satırların yeniden sabitlenmesidir (SAT-D269). Taslaktaki önizleme (SAT-D267 (1))
  anlık görüntü değildir ve saklanmaz.
- **SAT-D250:** metni değişmez; SAT-D267 (3) ile önizlemeye de aynen uygulanır (Sinan'ın yeniden teyidi).
- **SAT-D247 ve SAT-D250:** metni değişmez; "API-50 marj alanı" SAT-D266 ile Satış'ın line-pricing okuması olarak
  netleşir.
- **DEC-PRD-011:** metni değişmez; KARAR_SICILI DEC-PRD-011 notuna bu belgeye atıf eklenir.
- Başka hiçbir kararın anlamı değişmez.

## Uygulama listesi

Sahiplik: hareketli ortalama maliyet üreticisi ve okuma portu **Stok'a** aittir (SAT-D262). Satış maliyeti yalnız bu
sürümlü porttan okur; Stok'un hiçbir tablosuna yazmaz ve o tabloları doğrudan okumaz. Para/formül ve cross-module
contract içerdiği için işler **K3**'tür (iki bağımsız exact-head review, gerçek-SQL kanıtı).

| Karar | İş | Kapı / bağımlılık |
|---|---|---|
| SAT-D262, SAT-D265; SAT-D256, SAT-D257, SAT-D258, SAT-D260 | Stok modülünde depo bazında, `CompanyId` kapsamlı hareketli ortalama alış maliyeti üreticisi (alış girişlerinde yeniden hesap, transferde kaynak deponun ortalamasıyla hedefe giriş, üretim girişleri ortalamaya girmez, iç hassasiyet 4 basamak) ve Stok'un sahip olduğu **sürümlü okuma portu** (değer / `Unreadable` / `NoSource`). Migration geri alınabilir veya forward-fix planlı; port için sürümlü contract testi. | Stok şeridi; **K3** (cross-module contract + para/formül + migration) |
| SAT-D262, SAT-D264, SAT-D267, SAT-D268, SAT-D269, SAT-D270 | **İç onay komutunda** Stok portundan okunan maliyetin sipariş satırına anlık görüntüsü; sonradan değişmez. Revizyon onayında yalnız değişen ve yeni satırlar yeniden okunur ve sabitlenir; değişmeyen satırlar eski anlık görüntüyü korur. Port `Unreadable`/`NoSource` ise onay engellenmez, satırın marjı kalıcı "kullanılamıyor" kaydedilir. Migration geri alınabilir veya forward-fix planlı; idempotency/expected version mevcut onay komutlarıyla. | Satış (Claude) şeridi; **Stok portu henüz yok (bekliyor), main'de olmalı**; **K3** (para + migration + cross-module contract) |
| SAT-D263, SAT-D265, SAT-D266; SAT-D259, SAT-D261 | `GET /api/v1/sales/drafts/{id}/line-pricing` cevabında marj tutarı ve yüzdesi (yalnız alış maliyeti; yüzde yuvarlanmamış tutardan; gösterimde kuruş ve 0,01; mevcut `Money` kuralı). **Stok portu main'e girene kadar line-pricing marj bloğu "kullanılamıyor" kalır**; sıfır, tahmini veya ekranda hesaplanmış marj yok. **Satırın anlık görüntüsü varsa line-pricing anlık görüntü marjını, yoksa (iç onaydan önce) saklanmayan taslak önizlemesini (SAT-D267 (1)–(3)) gösterir**; kalıcı "kullanılamıyor" kaydı olan satırda marj "kullanılamıyor"dur (SAT-D270). OpenAPI yenilenir. Formül golden'ı (ChatGPT şeridi), birim + gerçek-SQL kanıtı. | Satış (Claude) şeridi; **K3** (para/formül) |
| SAT-D250, SAT-D266 | Marj alanlarının line-pricing cevabında yalnız marj görme izniyle verilmesi; yetkisizde ikisi de backend'de esirgenir (SAT-D250'de karara bağlandığı gibi). | **K3** (permission/rol); grant insan kapısıdır |
| SAT-D267, SAT-D250 | Taslak line-pricing okumasında saklanmayan canlı önizleme marjı ("tahmini/önizleme"); yalnız marj görme izniyle, yetkisizde backend'de esirgenir; hiçbir kayda yazılmaz. | Satış (Claude) şeridi; Stok portu main'de olmalı; **K3** (para/formül + permission) |
| SAT-D267 | S-05 (UI-50) taslakta ayrı "Marjı göster" düğmesi; önizleme "tahmini/önizleme" etiketiyle; "kullanılamıyor" ve "gizli" durumları; ekranda marj hesabı yok. | Satış (Claude) frontend şeridi; backend main'de |
| SAT-D263 | S-05 (UI-50) marj alanında backend tutar ve yüzdesinin "yalnız alış maliyeti" etiketiyle gösterilmesi; "kullanılamıyor" ve "gizli" durumları; ekranda marj hesabı yok; strict decoder. | Satış (Claude) şeridi; backend main'de |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, migration, OpenAPI veya Ayarlar verisi değiştirmez; yukarıdaki
  işler ayrı PR'lardır.
- **Stok portu gelene kadar marj "kullanılamıyor" kalır** (SAT-D247 fail-closed). Hareketli ortalama maliyet üreticisi
  ve Stok okuma portu main'e girmeden marj sıfır, tahmini veya ekranda hesaplanmış gösterilmez.
- **İkinci maliyet türü açık kalır:** vade farkı, öteki giderler ve ek (landed) maliyetlerin marja girip girmeyeceği
  ve nasıl gireceği ayrı, ileride verilecek bir karardır (SAT-D263 (3)).
- EK51 "Sınırlar"da açık kalan ve bu belgede de **seçilmeyen** noktalar (`NEEDS-SINAN`; para/stok/formül olduğu için
  tahminle etkinleştirilmez, seçilene kadar ilgili durumda marj "kullanılamıyor" kalır):
  - **Birim dönüşümü (`NEEDS-SINAN`):** stok birimi (kg) ile satış siparişi biriminin farklı olduğu durumda maliyetin
    satış birimine çevrilme kuralı.
  - **Döviz cinsi alış maliyeti (`NEEDS-SINAN`):** dövizli alış maliyetinin TL'ye hangi kur ve hangi tarihle
    çevrileceği.
  - **"Alış maliyeti"nin kapsamı / ek maliyetler (`NEEDS-SINAN`):** nakliye, gümrük gibi ek (landed) maliyetlerin
    ortalamaya girip girmeyeceği (SAT-D263'teki ikinci maliyet türü kararıyla birlikte).
  - **İade (`NEEDS-SINAN`):** satış ve alış iadelerinin hareketli ortalamaya etkisi.
  - **Negatif stok (`NEEDS-SINAN`):** stok negatife düştüğünde ortalamanın ve marjın davranışı.
  - **Karma kaynaklı ürün (`NEEDS-SINAN`):** hem satın alınan hem üretilen üründe marjın alış ortalamasıyla mı
    verileceği yoksa "kullanılamıyor" mu kalacağı (SAT-D258).
- **Taslak önizlemesi ve anlık görüntü** SAT-D267, SAT-D268…SAT-D270 ile kararlaştırılmıştır; #66 yorumu 6761194105'teki
  üç `NEEDS-SINAN` maddesi kapanmıştır. Anlık görüntü yazımı Stok portu main'e girene kadar uygulanamaz; o zamana kadar
  sipariş marjı "kullanılamıyor" kalır (yukarıdaki Stok portu maddesi).
- **`OPEN-008` açık kalır.** Genel stok değerleme, muhasebe maliyet yöntemi ve üretim maliyeti bu belgede seçilmez;
  ileride farklı bir değerleme yöntemi seçilirse S-05 marjının onunla hizalanıp hizalanmayacağı `NEEDS-SINAN` olur
  (EK49 ve EK51 ile aynı).
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; grant/rol ataması veya production değişikliği yapmaz; yeni izin adı
  açmaz. SAT-D250 marj izninin gerçek ortamda kimlere verileceği (grant, rol seed'i) insan kapısıdır.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
