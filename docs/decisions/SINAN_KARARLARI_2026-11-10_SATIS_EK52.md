# Sinan Kararları — 2026-11-10 (Satış: S-05 marj maliyeti — maliyetin sahibi Stok, sipariş anı anlık görüntüsü, yalnız alış maliyeti, iç hassasiyet ve yuvarlama, line-pricing marj alanı)

Bu belge önceki karar dosyalarını silmez; SAT-D262…SAT-D266'yı ekler. EK51
(`SINAN_KARARLARI_2026-11-09_SATIS_EK51.md`) "Sınırlar"da açık bırakılan üç noktayı (hareketli ortalama maliyet
üreticisinin sahibi, SAT-D256'daki "satış anı", SAT-D261'deki ara yuvarlama ve iç hassasiyet) ve EK49
(`SINAN_KARARLARI_2026-11-09_SATIS_EK49.md`) SAT-D247'deki "API-50 marj alanı" ifadesinin hangi uç noktayı
anlattığını kayda geçirir. SAT-D247, SAT-D250 (EK49), SAT-D256…SAT-D261 (EK51), DEC-PRD-011 ve `OPEN-008`
geçerlidir; bu belge onların metnini değiştirmez (SAT-D256 ve SAT-D261'deki açık kısımların cevaplanması için
"Bilinçli değişiklikler"e bakın).

**Kaynak:**

- Sinan'ın 10 Kasım 2026'da Claude Code cloud oturumunda (`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine
  doğrudan sorularla verdiği cevaplar. Cevapların anlamı bu belgeyi yazan ajana koordinatör tarafından iletildi ve
  aşağıda iletildiği biçimiyle kayda geçirildi. Soruların seçilmeyen seçenekleri, seçenek açıklamaları ve
  "(Önerilen)" işaretleri bu ajana iletilmediği için yazılmadı (uydurulmadı); "Cevap" sütunu aynen alıntı değil,
  iletilen cevabın anlamıdır.

Bu belge merge makbuzu dışında Issue #66'ya karar yazmaz; yalnız aşağıdaki `NEEDS-SINAN` maddeleri #66'ya tek satır
olarak ayrıca yazılır.

## Soru ve cevaplar

| # | Soru | Cevap (iletilen anlam) | Kayda geçen anlam |
|---|---|---|---|
| 1 | Depo bazında hareketli ortalama alış maliyetini hangi modül üretsin? (EK51 "Sınırlar": sahiplik) | Maliyeti **Stok (Inventory)** modülü üretir ve Stok'un sahip olduğu sürümlü bir okuma portuyla verir; Satış sipariş anında okur, sipariş satırına anlık görüntü olarak yazar, sonra değişmez; Satış maliyeti kendisi hesaplamaz. | Üretici ve okuma portu Stok'a aittir; Satış yalnız okur ve anlık görüntüyü kendi sipariş satırında saklar. |
| 2 | İlk aşamada marjda hangi maliyet türleri kullanılsın? | İlk aşamada **yalnız alış maliyeti**; ekran bunu "yalnız alış maliyeti" diye etiketler. İkinci maliyet türü (vade farkı, öteki giderler/ek maliyetler) ayrı ve ileride verilecek bir karardır. | Marj yalnız alış maliyetiyle verilir ve ekranda öyle etiketlenir; ikinci maliyet türü açık kalır. |
| 3 | SAT-D256'daki "satış anı" hangi zaman? (EK51 "Sınırlar" `NEEDS-SINAN`) | **Sipariş anı.** | Marjda kullanılan hareketli ortalama sipariş anındaki ortalamadır. |
| 4 | Ara yuvarlama ve iç hassasiyet ne olsun? (EK51 "Sınırlar" `NEEDS-SINAN`, SAT-D261) | Marj yüzdesi **yuvarlanmamış marj tutarından** hesaplanır; hareketli ortalama maliyetin iç hassasiyeti **4 ondalık basamaktır**; gösterimde tutar kuruşa, yüzde 2 ondalık basamağa yuvarlanır. | Yüzde yuvarlanmış tutardan türetilmez; maliyet 4 basamak iç hassasiyetle tutulur; yuvarlama yalnız gösterim adımındadır. |
| 5 | EK49'daki "API-50 marj alanı" hangi uç noktadır? | Satış'ın sahip olduğu `GET /api/v1/sales/drafts/{id}/line-pricing` okumasıdır; Ayarlar'daki API-50 (iskonto limiti politikası) değişmez. | Marj alanları Satış'ın line-pricing okumasında yer alır; Ayarlar API-50 bu işten etkilenmez. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D262 | Maliyet üreticisinin sahibi (EK51 "Sınırlar" sahiplik sorusunun cevabı) | (1) SAT-D256…SAT-D258 ve SAT-D260'taki depo bazında, `CompanyId` kapsamlı hareketli ortalama **alış** maliyetini **Stok (Inventory) modülü üretir** (soru 1). (2) Maliyet, **Stok'un sahip olduğu sürümlü bir okuma portuyla** verilir; portun cevabı EK51'deki gibi değer, `Unreadable` (okunamıyor) ve `NoSource` (kayıt yok) durumlarını ayırır. (3) **Satış maliyeti hiçbir zaman kendisi hesaplamaz**; Stok'un hiçbir tablosuna yazmaz ve o tabloları doğrudan okumaz (Mimari değişmez: bir modül başka modül tablosuna doğrudan yazmaz). (4) Satış maliyeti **sipariş anında** (SAT-D264) porttan okur ve **satış siparişi satırına anlık görüntü (snapshot) olarak yazar**; bu anlık görüntü sonradan **değişmez** (sonraki stok girişleri, transferler veya ortalama güncellemeleri kayıtlı siparişin maliyetini değiştirmez). (5) Port `Unreadable` veya `NoSource` döndürürse ya da EK51'de `NEEDS-SINAN` kalan bir durum gerekiyorsa marj **"kullanılamıyor"** kalır (SAT-D247 fail-closed); sıfır veya tahmini maliyet yazılmaz. Master v3'teki M-09 Maliyet Muhasebesi kapısı bu kararla S-05 marjının maliyet üreticisi olmaz; genel stok değerleme ve muhasebe maliyet yöntemi `OPEN-008`'de açık kalır | Stok modülü (hareketli ortalama maliyet üreticisi + sürümlü okuma portu, yeni), Satış sipariş satırı (maliyet anlık görüntüsü), marj hesaplayıcısı (SAT-D247) |
| SAT-D263 | İlk aşamada maliyet türü | (1) İlk aşamada marj **yalnız alış maliyetiyle** hesaplanır (soru 2; SAT-D258 ile aynı yönde). (2) S-05 ekranı marjın maliyet tarafını **"yalnız alış maliyeti"** diye etiketler; etiket metni Türkçe kullanıcı metni olarak kaynak dosyadadır. (3) **İkinci maliyet türü** — vade farkı, öteki giderler ve ek (landed) maliyetler — **ayrı ve ileride verilecek bir karardır**; bu belgede seçilmez, açık kalır (bkz. "Sınırlar"). O karar verilene kadar bu maliyetler marja eklenmez, sıfır sayıldığı ima edilmez | Marj hesaplayıcısı, line-pricing marj alanları, S-05 (UI-50) etiketi |
| SAT-D264 | "Satış anı" (SAT-D256 açık kısmının cevabı) | SAT-D256'daki **"satış anı" sipariş anıdır** (soru 3). Marjda kullanılan hareketli ortalama, sipariş anında Stok portunun verdiği ortalamadır ve SAT-D262 (4) uyarınca sipariş satırına anlık görüntü olarak yazılır. Fatura zamanındaki ortalama marjda kullanılmaz | Marj hesaplayıcısı, Satış sipariş satırı anlık görüntüsü |
| SAT-D265 | İç hassasiyet ve yuvarlama (SAT-D261 açık kısmının cevabı) | (1) **Marj yüzdesi yuvarlanmamış marj tutarından** hesaplanır; yuvarlanmış (kuruşa indirilmiş) tutardan türetilmez (soru 4). (2) Hareketli ortalama maliyetin **iç hassasiyeti 4 ondalık basamaktır**. (3) **Gösterimde** marj tutarı **kuruşa (0,01 TL)**, marj yüzdesi **2 ondalık basamağa (0,01)** yuvarlanır; SAT-D261'in 0,01 TL / 0,01 hassasiyeti aynen geçerlidir. (4) Yuvarlama kipi için yeni bir kural açılmaz: **mevcut para yuvarlama kuralı** uygulanır; EK51 SAT-D261 bu kuralı `Money` değer nesnesindeki yarımda sıfırdan uzağa yuvarlama (`MidpointRounding.AwayFromZero`) olarak kaydetmiştir. (5) Bu kurallar formül golden'ında (ChatGPT şeridi) kilitlenir; golden main'e girene kadar formül etkinleştirilmez | Marj hesaplayıcısı, Stok hareketli ortalama hesabı (iç hassasiyet), formül golden'ı |
| SAT-D266 | Marjın uç noktası (EK49 SAT-D247/SAT-D250 "API-50 marj alanı" ifadesinin netleşmesi) | (1) EK49'da "API-50 marj alanı" diye anılan yer, **Satış'ın sahip olduğu `GET /api/v1/sales/drafts/{id}/line-pricing` okumasıdır** (soru 5). (2) Marj tutarı ve yüzdesi (SAT-D259) bu okumanın cevabında yer alır; SAT-D250 görünürlüğü (yalnız Genel Müdür ve Admin; yetkisizde backend'de esirgenir) bu alanlara uygulanır. (3) **Ayarlar'daki API-50** (satış iskonto limiti politikası, `SATIS_HAZIRLIK_PLANI_v1.md` API-50 satırı) **değişmez**; marj alanı oraya eklenmez. EK51'deki "API-50 marj alanları" ifadeleri de aynı biçimde line-pricing okumasını anlatır | `GET /api/v1/sales/drafts/{id}/line-pricing` (`SalesDraftContractsV1`), OpenAPI, S-05 (UI-50); Ayarlar API-50 (değişmez) |

Uygulama ayrıntısı (geri alınabilir):

- SAT-D262'de anlık görüntünün sipariş satırındaki alan adı, okunan portun sürümünü/kaynak kimliğini birlikte
  saklayıp saklamadığı ve port durumunun (`Unreadable`/`NoSource`) satırda nasıl kayda geçtiği backend sözleşmesinde
  kesinleşir; anlık görüntü portun verdiği değeri 4 basamak iç hassasiyetle (SAT-D265) aynen saklar.
- SAT-D263'teki etiketin tam metni ve yeri (marj alanının başlığı ya da yardım metni) UI'da belirlenir; "yalnız
  alış maliyeti" anlamı çıkarılamaz.

## Kapatılan açık sorular

- EK51 "Sınırlar" — hareketli ortalama maliyet üreticisinin sahibi (Stok/Inventory ya da M-09) — SAT-D262 (Stok).
- EK51 "Sınırlar" — "satış anı" sipariş zamanı mı, fatura zamanı mı (`NEEDS-SINAN`, SAT-D256) — SAT-D264 (sipariş anı).
- EK51 "Sınırlar" — ara yuvarlama ve hareketli ortalamanın iç hassasiyeti (`NEEDS-SINAN`, SAT-D261) — SAT-D265.
- EK49 SAT-D247/SAT-D250 — "API-50 marj alanı"nın hangi uç nokta olduğu — SAT-D266 (Satış line-pricing okuması).
- **Bu belge hiçbir `OPEN-*` kaydını kapatmaz.** Yukarıdaki soruların `OPEN-NNN` kimliği yoktur. `OPEN-008` (stok
  değerleme ve ayrıntılı üretim maliyeti) **açık kalır**: SAT-D262…SAT-D266 yalnız S-05 marjının maliyet tarafını
  belirler; genel stok değerleme, muhasebe maliyet yöntemi ve üretim maliyeti seçilmez.

## Bilinçli değişiklikler

- **SAT-D256:** metni değişmez; "satış anı"nın hangi zaman olduğunu seçilmemiş bırakan açık kısmı **SAT-D264 ile
  cevaplanmıştır** (sipariş anı). SAT-D256'nın "seçilene kadar marj kullanılamıyor" koşulu bu noktada kalkar; marjın
  öteki fail-closed koşulları (SAT-D247, SAT-D262 (5)) aynen geçerlidir.
- **SAT-D261:** metni değişmez; ara yuvarlama ve iç hassasiyeti seçilmemiş bırakan açık kısmı **SAT-D265 ile
  cevaplanmıştır**. 0,01 TL / 0,01 hassasiyet ve mevcut `Money` yuvarlama kuralı aynen geçerlidir.
- **EK51 "Uygulama listesi" ve "Sınırlar" sahiplik maddesi:** üreticinin sahibi SAT-D262 ile Stok olarak
  belirlenmiştir; EK51 dosyası yerinde düzenlenmez.
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
| SAT-D262, SAT-D264 | Satış sipariş satırına sipariş anında porttan okunan maliyetin anlık görüntüsü; sonradan değişmez; port `Unreadable`/`NoSource` ise anlık görüntü yok ve marj "kullanılamıyor". | Satış (Claude) şeridi; Stok portu main'de olmalı; **K3** (para + migration + cross-module contract) |
| SAT-D263, SAT-D265, SAT-D266; SAT-D259, SAT-D261 | `GET /api/v1/sales/drafts/{id}/line-pricing` cevabında marj tutarı ve yüzdesi (yalnız alış maliyeti; yüzde yuvarlanmamış tutardan; gösterimde kuruş ve 0,01; mevcut `Money` kuralı). **Stok portu main'e girene kadar line-pricing marj bloğu "kullanılamıyor" kalır**; sıfır, tahmini veya ekranda hesaplanmış marj yok. OpenAPI yenilenir. Formül golden'ı (ChatGPT şeridi), birim + gerçek-SQL kanıtı. | Satış (Claude) şeridi; **K3** (para/formül) |
| SAT-D250, SAT-D266 | Marj alanlarının line-pricing cevabında yalnız marj görme izniyle verilmesi; yetkisizde ikisi de backend'de esirgenir (SAT-D250'de karara bağlandığı gibi). | **K3** (permission/rol); grant insan kapısıdır |
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
- **Taslak aşaması ve "sipariş anı"nın komutu (`NEEDS-SINAN`):** Sinan'ın cevabı anlık görüntüyü sipariş anına
  bağlar. Siparişten önce, taslağın line-pricing okumasında marjın Stok portunun o anki cevabıyla önizleme olarak
  gösterilip gösterilmeyeceği ve "sipariş anı"nın satış sipariş yaşam döngüsünde hangi komuta (taslağın siparişe
  dönüştüğü an) karşılık geldiği bu belgede seçilmez; seçilene kadar anlık görüntüsü olmayan satırda marj
  "kullanılamıyor" kalır.
- **`OPEN-008` açık kalır.** Genel stok değerleme, muhasebe maliyet yöntemi ve üretim maliyeti bu belgede seçilmez;
  ileride farklı bir değerleme yöntemi seçilirse S-05 marjının onunla hizalanıp hizalanmayacağı `NEEDS-SINAN` olur
  (EK49 ve EK51 ile aynı).
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; grant/rol ataması veya production değişikliği yapmaz; yeni izin adı
  açmaz. SAT-D250 marj izninin gerçek ortamda kimlere verileceği (grant, rol seed'i) insan kapısıdır.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
