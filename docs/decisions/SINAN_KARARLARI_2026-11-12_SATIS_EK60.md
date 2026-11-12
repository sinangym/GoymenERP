# Sinan Kararları — 2026-11-12 (Satış: fason hizmet faturasında kg kaynağı, sevk risk kapısı, sevk planı esaslı risk)

Bu belge önceki karar dosyalarını silmez; **SAT-D309, SAT-D310, SAT-D311, SAT-D312, SAT-D313, SAT-D314 ve SAT-D315**'i ekler. URT-D16 ("fatura hizmet bedelidir"), SAT-D175, SAT-D195, SAT-D209,
SAT-D11 ve SAT-D110 aynen geçerlidir; bu belge onların metnini değiştirmez. `KARAR_SICILI` değişmez.

**Kimlik:** SAT-D308 main'dedir (#1108, `2efb3f68`; Stok,
`SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md`, fire anahtarı kapalıyken fire maliyeti); bu karar
**SAT-D309** alır. #66 16. tur yorumu bu maddeyi "LOJ-D9 / SAT-D308" etiketiyle anar; o etiket koordinatörün çalışma
adıdır, sicilde bu madde **SAT-D309**'dur. SAT-D310–D315 main'de (`e165aaee`) geçmez. #1115 (ölçüm `394e9ed4`, son head `9cfc9ce8`; merge `26ad52f7`) SAT-D309, SAT-D310, SAT-D312
ve SAT-D313'ü yalnız bu PR'a atıf olarak anar; başka bir dalda bu kimliklere ayrı tanım verilmez (`git grep`,
2026-11-12). SAT-D314 ile aynı Sinan cevabının Stok kimliği **KLN-D28**'dir (#1116). SAT-D315 hiçbir dalda geçmez
(`git grep`, 2026-11-12). SAT-D315 ile aynı Sinan cevaplarının Stok kimlikleri **KLN-D30** (m.1) ve **KLN-D31** (m.2)'dir
(#1116 `25de5424`); 25. tur cevabının (SAT-D314 (2), SAT-D315 (5)) Stok kimliği **KLN-D32**'dir (#1116 head `19095be0`;
kesin SHA #1116 merge'ünde).

**Kaynak:** Sinan, Issue #66 yorumu
[6820601088](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820601088) (11. tur, madde 4;
2026-11-12T10:03:40Z; koordinatör oturumunda `AskUserQuestion` ile alındı). Birincil yorumdan `gh api` ile doğrulandı;
koordinatör aktarımı karar kaynağı sayılmadı. Cevap kelimesi kelimesine:

> **11. tur, m.4 — F-012 fason hizmet bedeli kg kaynağı (#1115 spec NEEDS-SINAN-2):** **Teslim tartısı net kg** (önerilen
> "üretimde işlenen girdi kg" seçilmedi). Bedel, müşteriye geri teslim edilen malın tartıdaki net kg'ı üzerinden hesaplanır;
> fire bedele girmez. Fatura Satış'taki fason hizmet siparişinden kesilir.

Aynı cevap Lojistik şeridinde **LOJ-D9** olarak kayda geçmiştir (`SINAN_KARARLARI_2026-11-12_LOJISTIK_EK2.md`, PR #1115).
SAT-D309 aynı cevaptan yazılır; Satış etkisi ayrı yazıldı, Lojistik metnini değiştirmez.

**Ek kaynak (SAT-D310–D312):** Sinan, Issue #66 yorumları
[6821105608](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821105608) (16. tur, madde 2–3;
2026-11-12T10:27:50Z) ve [6821262098](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821262098) (17. tur,
madde 1–2; 2026-11-12T10:35:27Z); koordinatör oturumunda `AskUserQuestion` ile alındı, sorular bu PR'ın #66 yorumu
[6821077996](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821077996) N-5/N-6/N-8'dir. Birincil yorumlardan
`gh api` ile doğrulandı. Cevaplar kelimesi kelimesine:

> **N-5 — risk okunamazsa sevk (#1119)** — Soru: müşteri riski okunamıyorsa (Finans, kur veya fatura kaynağına
> ulaşılamıyorsa) sevk "tek sevk istisnası" onayıyla geçebilsin mi? Öneri "Hayır, sevk durur" idi. **Cevap: Evet,
> istisnayla geçer.** (Yetkili onaylarsa risk okunamasa da tek sevk çıkar.)

> **N-6 — kesilmiş ama Finans'ın işlemediği fatura (#1119)** — **Cevap (Sinan'ın kendi metni): "Faturalanmamış sevkte evet
> ama bu tutarda ayrı bir risk türü olarak gösterilir".** (Tutar Finans işleyene kadar faturalanmamış sevk toplamında sayılır,
> ama ekranda ayrı bir risk türü/satırı olarak görünür; işlenince açık bakiyeye geçer.)

> **Fason net kg paylaşımı (LOJ-D9 / SAT-D308, N-8)** — Soru: tek tartı aynı araçtaki birden çok fason siparişini kapsıyorsa
> veya tartı hiç yoksa ne olsun? **Cevap: Oranla böl, tartı zorunlu.** Net kg, sevk satırlarındaki beyan kg oranında
> siparişlere paylaştırılır. Tartısı olmayan fason teslim kaydedilemez.

> **Çıkış sonrası tartı düzeltmesi** — Soru: araç çıktıktan sonra tartının yanlış girildiği anlaşılırsa (fason ücreti
> etkilenir) ne yapılsın? **Cevap: Gerekçeli düzeltme.** Yetkili kişi gerekçe yazarak düzeltme kaydı girer. Eski değer
> silinmez, denetim izinde kalır. Fatura kesilmişse fark Satış'tan fark faturasıyla kapatılır.

Yorumun notu: “"Yetkili kişi" (m.3) için yeni izin adı önerilebilir; grant/rol insan kapısıdır.” Aynı 16. tur m.2–3,
Lojistik şeridinde LOJ-D12 (paylaşım, tartısız teslim) ve LOJ-D13 (gerekçeli düzeltme) olarak işlenir
(`SINAN_KARARLARI_2026-11-12_LOJISTIK_EK3.md`, #1115); Satış metni aşağıda yalnız Satış etkisini yazar, Lojistik'in tartı
kaydı kuralını değiştirmez.

**Ek kaynak (SAT-D313):** Sinan, bu oturumda `AskUserQuestion` ile (2026-11-12); soru bu PR'ın #66 NEEDS-SINAN yorumu
[6822311631](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822311631) (önerilen: "sipariş kg × onaylı kg
fiyatı", alternatif: "tartıya kadar riske girmez"); iki seçenek de seçilmedi, Sinan kendi metnini yazdı. Kayıt: #66
[6822385722](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822385722) ve devam sorusu (önerilen: "plana
alınmamış kalan sayılır", seçilmedi) [6822674443](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822674443).
Cevaplar kelimesi kelimesine:

> *(soru, özet; birebir değil)* **Fason hizmet siparişinin tutarı müşteri riskinde tartıdan önce nasıl sayılsın?**
> Cevap: "Sevk planı için açılan miktar x
> birim fiyat, bu fason ve satışlarda gerçekleşecek risk oluyor, irsaliyesi faturası kesilene kadar"

> *(soru, özet; birebir değil)* **Onaylı siparişin henüz sevk planına alınmamış kalanı riskte sayılmaya devam etsin mi?**
> Cevap: "Siparişin tamamı risk
> sayılmaz, sevk planına alınan sevk olan faturası kesilenler durumuna göre risk durumlarında görünür"

Aynı soru (NEEDS-SINAN `6822311631`) koordinatör oturumunda da `AskUserQuestion` ile soruldu; kayıt #66
[6822767886](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822767886) (21. tur, madde 1; 2026-11-12T11:48:36Z). Birincil yorumdan `gh api` ile doğrulandı. Cevap
kelimesi kelimesine:

> **Satış/Lojistik — fason hizmet siparişinin tartıdan önceki risk tutarı:** "Sevk plan miktarı x onaylı fiyat"

Yorumdaki koordinatör okuması (karar metni değil): tartıdan önce riske yazılan tutar sevk planındaki (yükleme talimatı)
miktar × onaylı kg fiyatıdır (SAT-D11 KDV kuralıyla), tartıdan sonra gerçek tutara geçer; sevk planı olmayan fason sipariş
için ek bir sayım yöntemi seçilmemiştir. Bu boşluk kapanmıştır: koordinatör notu
[6822810785](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822810785) (2026-11-12T11:50:43Z) üç cevabın
aynı karar olduğunu, bağlayıcı metnin SAT-D313 olduğunu ve "plansız fason sipariş için ek yöntem seçilmedi" boşluğunun
`6822674443` ("Siparişin tamamı risk sayılmaz") ile kapandığını kaydeder: sevk planına alınmamış sipariş kalanı riske
sayılmaz. Aynı not DEC-FIN-007 sicil değişikliğinin "ayrı, incelenmiş bir PR ile" yapılacağını yazar. SAT-D313 üç
cevaptan yazılır.

**Ek kaynak (SAT-D314):** Sinan, #66 [6822767886](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822767886) madde 2 (koordinatör oturumunda `AskUserQuestion`); soru
PR #1116 `STOK_F017_GELECEK_TEDARIK_AYIRMA_SPEC_v1.md` §9.1 NEEDS-SINAN adayı 1'dir. Cevap kelimesi kelimesine:

> **Stok/Satış — gelecek tedarikten ayrılmış satış satırı iptal/azaltılınca:** "Otomatik bırakılır (Önerilen)"

Yorumdaki açıklama: "Satış iptal/azaltma komutu aynı işlemde Stok bırakma portunu çağırır; artan ayırma kalmaz; audit'li.
(Seçilmeyen: Satış reddeder, önce elle bırak.)" Aynı yorumun madde 3'ü (canlanan alış satırı) Alış/Stok'undur; burada
yazılmaz.

Aynı cevap Stok şeridinde **KLN-D28** olarak kayda geçmiştir (#1116 `SINAN_KARARLARI_2026-11-12_STOK_F017_EK.md`, rev4
`d5abb336` ve sonrası; son okunan head `d0a70227`). Stok'un gelecek tedarik ayırma modeli main'deki **KLN-D26**'dır
(`SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md`, `e165aaee`). Stok tarafının metni KLN-D28'dir;
SAT-D314 yalnız Satış etkisini yazar ve KLN-D26/KLN-D28 metnini değiştirmez.

**Ek kaynak (SAT-D315):** Sinan, #66 [6823243819](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823243819) (23. tur, koordinatör oturumunda `AskUserQuestion`)
madde 1 ve 2; sorular PR #1116 §9.1 NEEDS-SINAN adayı 4 ve adayı 3'tür. Cevaplar kelimesi kelimesine:

> **m.1 (aday 4)** — Soru: "Satış satırının miktarı azaltılınca, bırakılacak gelecek tedarik ayırması birden çok alış
> siparişine yayılmışsa hangisi önce bırakılsın? (Ör. 30 t'lik satır 20 t'ye iniyor; 10 t'si 15 Aralık'ta, 20 t'si 5
> Ocak'ta gelecek alıştan ayrılmış.)" Cevap: "En geç gelen önce (Önerilen)"

> **m.2 (aday 3)** — Soru: "Satış satırı azaltılınca satırda hem depodaki etiketli mala yapılmış tahsis hem de gelecek
> alıştan ayırma varsa önce hangisi bırakılsın?" Cevap: "Önce gelecek ayırma (Önerilen)"

Yorumdaki açıklamalar (koordinatörün seçeneğe yazdığı "Anlamı"; Sinan seçeneği bu açıklamayla seçti): m.1 "sistem en geç
gelecek alıştan bırakır, erken geleni müşteriye tutar; kullanıcı isterse komutta başka ayırma seçebilir" (seçilmeyenler:
"En son yapılan önce"; "Her seferinde kullanıcı seçer"); m.2 "depodaki hazır mal müşteride kalır, sevke hazırdır; henüz
gelmemiş alış ayırması önce bırakılır; kullanıcı isterse değiştirebilir" (seçilmeyenler: "Önce depodaki tahsis"; "Her
seferinde kullanıcı seçer"). Yorumun notu: "geçici "409, yazma yok" kapalı yönü kalkar. Kullanıcının komutta açık seçimi
varsayılandan üstündür." Aynı not SAT-D314 ölçüsünü max(0, aktif gelecek ayırma − (yeni satır miktarı − sevk edilen))
olarak yazar (koordinatörün teknik notu); bu ölçü **25. tur ile değişti** (25. tur koordinatör düzeltmesi): fiziksel tahsis de dahildir (SAT-D314 (1), `6823547103`). Madde 3 Kimlik'indir (KIM-D22); burada yazılmaz.

Aynı cevaplar Stok şeridinde **KLN-D30** (en geç gelen önce) ve **KLN-D31** (önce gelecek, yetmezse fiziksel) olarak
kayda geçmiştir (#1116 `SINAN_KARARLARI_2026-11-12_STOK_F017_EK.md`, head `25de5424`); 25. tur cevabı **KLN-D32** (gelecek yetmezse fiziksel otomatik bırakılır; ara red) olarak kayda
geçmiştir (#1116 head `19095be0`). Sıra Stok bırakma portunun içinde
hesaplanır; Satış sırayı hesaplamaz, yalnız açık seçimi iletir (KLN-D30 (5)). SAT-D315 yalnız Satış etkisini yazar ve
KLN-D30/KLN-D31/KLN-D32 metnini değiştirmez.

**Ek kaynak (SAT-D314/SAT-D315, 25. tur):** Sinan, #66 [6823547103](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823547103)
(koordinatör oturumunda `AskUserQuestion`). Soru (özet; birebir değil): gelecek ayırma bırakılacak fazlaya yetmezse
fiziksel tahsis fazlası da otomatik bırakılır mı? Örnek: 10 t fiziksel + 10 t gelecek, satır 20 → 5; 10 t gelecek ve 5 t
fiziksel bırakılır. Cevap kelimesi kelimesine: "Otomatik bırakılır (Önerilen)". Seçilen seçeneğin metni: "Fazla önce
gelecekten, yetmezse fiziksel ayırmadan otomatik bırakılır; kullanıcı komutta hangisini bırakacağını seçerse onun seçimi
geçer. Fiziksel bırakma ekranı hazır olana kadar böyle bir azaltma reddedilir." Seçilmeyenler: "azaltma reddedilir
(kalıcı)"; "fazla kalır, uyarı". *Koordinatör düzeltmesi (Sinan metni değil, aynı yorumda):* fazla = max(0, aktif fiziksel +
aktif gelecek − (yeni satır miktarı − sevk edilen)); önce gelecek (KLN-D30), sonra fiziksel; fiziksel yol yokken fiziksel
bırakma gereken kalan > 0 ise azaltmanın tamamı reddedilir, hiçbir şey yazılmaz. Golden: 10+10, 20 → 15 ⇒ gelecekten 5;
20 → 5 ⇒ gelecekten 10 + fiziksel 5 (ara dönemde ret); 100/40 sevk/60 gelecek → 80 ⇒ 20.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Fason hizmet bedeli hangi kg üzerinden hesaplanır? | **Teslim tartısı net kg** | Bedel, müşteriye geri teslim edilen malın tartıdaki net kg'ı üzerinden hesaplanır; fire bedele girmez. Fatura Satış'taki fason hizmet siparişinden kesilir. Seçilmeyen (önerilen): "üretimde işlenen girdi kg". Yorumda öteki seçeneklerin açıklaması yer almaz; uydurulmadı. |
| 2 | Risk okunamazsa sevk tek sevk istisnasıyla geçebilsin mi? (N-5) | **Evet, istisnayla geçer** | Yetkili onaylarsa risk okunamasa da tek sevk çıkar. Seçilmeyen (önerilen): "Hayır, sevk durur". |
| 3 | Kesilmiş ama Finans'ın işlemediği fatura riskte nerede? (N-6) | **"Faturalanmamış sevkte evet ama bu tutarda ayrı bir risk türü olarak gösterilir"** (Sinan'ın kendi metni) | Finans işleyene kadar faturalanmamış sevk toplamında sayılır, ekranda ayrı risk türü/satırı; işlenince açık bakiyeye geçer. |
| 4 | Tek tartı birden çok fason siparişini kapsarsa ya da tartı yoksa? (N-8) | **Oranla böl, tartı zorunlu** | Net kg sevk satırlarındaki beyan kg oranında paylaştırılır; tartısız fason teslim kaydedilemez. |
| 5 | Çıkış sonrası tartı yanlışsa? | **Gerekçeli düzeltme** | Yetkili kişi gerekçeli düzeltme kaydı girer; eski değer denetim izinde kalır; fatura kesilmişse fark Satış'tan fark faturasıyla kapatılır. |
| 6 | Fason siparişin tartı öncesi risk tutarı; plana alınmamış sipariş kalanı | **Sinan'ın kendi metni** (iki soruda da önerilen seçilmedi; yukarıdaki alıntılar) | Risk sevk planına açılan miktar × birim fiyattan başlar (satış ve fason), irsaliye ve fatura kesilene kadar; siparişin tamamı risk sayılmaz, tutar durumuna göre (planlı / sevk edilmiş / faturalı) görünür. Koordinatör sorusunda (`6822767886` m.1) aynı yön: "Sevk plan miktarı x onaylı fiyat". |
| 7 | Gelecek tedarikten ayrılmış satış satırı iptal/azaltılınca ayırma ne olur? (#1116 §9.1 aday 1) | **Otomatik bırakılır** (önerilen) | Satış iptal/azaltma komutu aynı işlemde Stok bırakma portunu çağırır; artan ayırma kalmaz; audit'li. Seçilmeyen: "Satış reddeder, önce elle bırak". Stok metni KLN-D28 (#1116). |
| 8 | Azaltmada bırakılacak gelecek ayırma birden çok alışa yayılıyorsa sıra (#1116 §9.1 aday 4) *(soru, özet; birebir değil)* | **En geç gelen önce** (önerilen) | En geç gelecek alıştan bırakılır, erken gelen müşteride kalır; kullanıcı komutta başka ayırma seçebilir. Seçilmeyenler: "En son yapılan önce", "Her seferinde kullanıcı seçer". |
| 9 | Azaltmada fiziksel tahsis ve gelecek ayırma birlikte varsa sıra (#1116 §9.1 aday 3) *(soru, özet; birebir değil)* | **Önce gelecek ayırma** (önerilen) | Depodaki hazır mal müşteride kalır; henüz gelmemiş alış ayırması önce bırakılır; kullanıcı değiştirebilir. Seçilmeyenler: "Önce depodaki tahsis", "Her seferinde kullanıcı seçer". |
| 10 | Gelecek ayırma fazlaya yetmezse fiziksel tahsis fazlası (25. tur, `6823547103`) *(soru, özet; birebir değil)* | **Otomatik bırakılır** (önerilen) | Fazla önce gelecekten, yetmezse fiziksel ayırmadan otomatik bırakılır; kullanıcının komuttaki seçimi geçer; fiziksel bırakma ekranı hazır olana kadar böyle bir azaltma reddedilir. Seçilmeyenler: "azaltma reddedilir (kalıcı)", "fazla kalır, uyarı". |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D309 (LOJ-D9 ile aynı cevap) | Fason hizmet bedeli kg kaynağı | Bedel kg'ı = müşteriye geri teslim edilen malın **teslim tartısı net kg**'ı. Fire bedele girmez. Fatura Satış'taki fason hizmet siparişinden kesilir. *Eşleme (karar eki değil; #1115 LOJ-D9 metniyle aynı):* teslim tartısı WGT-001 net olarak okunur, kesin tanım fason teslim dilimi spec'inde; Lojistik fatura kesmez. | Satış fason hizmet siparişi faturası (`docs/delivery/SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1.md` F1/F2), `SalesInvoiceCalculator` (tek hesaplayıcı), Lojistik net kg okuma portu (F1), Finans `sales.invoice.finalized.v1` tüketimi |
| SAT-D310 | Risk okunamazken sevk | Müşteri riski okunamıyorsa (Finans, kur veya fatura kaynağı yok; port sebebi `SourceUnavailable`) sevk, yetkili onaylı **tek sevk istisnası** (SAT-D154/D160, `Valid`) ile geçebilir; istisna yoksa durur. *Türetilmiş (yazar okuması; DEC-FIN-009 fail-closed, Lojistik L-7):* risk kapısı portunun kendisi okunamıyorsa (port hatası/zaman aşımı) istisna da okunamadığı için sevk durur. *Yazar okuması:* "yetkili" mevcut istisna onay yetkisidir; yeni izin/grant seçilmez. | `docs/delivery/SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1.md` S2 kapı kuralı, `ISalesShipmentRiskGateReadPortV1`, `SalesRiskExceptionService` (istisna talebi `Unavailable`'da zaten açık), Lojistik sevk kapısı |
| SAT-D311 | Finans'ın işlemediği kesilmiş fatura | Satış'ta kesilmiş, Finans'ın henüz işlemediği faturanın tutarı müşteri riskinde **faturalanmamış sevk toplamında sayılır**, ama ekranda **ayrı bir risk türü/satırı** olarak gösterilir. *Koordinatör/yazar okuması:* Finans işleyince bu satırdan düşer ve açık bakiyeye geçer; aynı tutar iki bileşende aynı anda sayılmaz (AGENTS çift sayım yasağı). Tutar fatura hesaplayıcısından gelir (SAT-D11 ve AGENTS tek hesaplayıcı; SAT-D90 yalnız kur); "Finans işledi" olgusu `OpenReceivables` ile aynı Finans cevabından okunur (plan S0 (b); olay kaynak sayılmaz), Satış tahmin etmez. | Plan S0, `SalesDraftService.RiskCheck.cs`, risk kontrol sözleşmesinde ayrı bileşen (`SalesDraftRiskCheckV1`, sürüm etkisi S0'da), Finans işlendi olgusu |
| SAT-D312 | Fason net kg paylaşımı ve tartı düzeltmesi (Satış etkisi) | (1) Tek teslim tartısı birden çok fason siparişini kapsıyorsa net kg, sevk satırlarındaki **beyan kg oranında** siparişlere paylaştırılır; fatura her siparişin payından kesilir (SAT-D309). (2) Tartısı olmayan fason teslim kaydedilemez; Satış tartısız fason satırı faturalamaz. (3) Çıkış sonrası tartı hatası yetkili kişinin gerekçeli düzeltme kaydıyla düzeltilir, eski değer denetim izinde kalır; fatura kesilmişse fark **Satış'tan fark faturasıyla** kapatılır. Düzeltme kaydı Lojistik tartı kaydının işidir (LOJ-D13); "yetkili kişi" için izin adı önerilebilir, grant/rol insan kapısıdır. Hesabın hangi modülde yapıldığı bu kararda seçilmedi (Sınırlar, yazar okuması). | Plan F1/F2, `SalesInvoiceCalculator`, Lojistik net kg okuma portu, fark faturası akışı |
| SAT-D313 | Siparişin risk tutarı: sevk planı esaslı (satış ve fason) | Müşteri riskinde siparişin tamamı sayılmaz. Risk, **sevk planına açılan miktar × birim fiyat** ile başlar; bu satış ve fason siparişler için aynıdır ve irsaliye/fatura kesilene kadar böyle sayılır. Tutar durumuna göre (sevk planına alınan / sevk olan / faturası kesilen) ayrı risk durumlarında görünür. Birim fiyat siparişin onaylı fiyatıdır (`6822767886` m.1: "onaylı fiyat"; fasonda onaylı kg fiyatı, SAT-D195). SAT-D175 ile tutarlıdır (EK25: "iç onay, risk kontrolü, müşteri kabulü ve revizyon kuralları normal siparişle aynıdır"): fason ve satış aynı kuralla sayılır. Sevk planına alınmamış sipariş kalanı riske girmez (`6822674443`; koordinatör notu `6822810785`); plansız fason sipariş de bu yüzden plana alınana kadar riske girmez. *Yazar okuması:* her tutar tam olarak bir durumda sayılır (AGENTS çift sayım yasağı); tutar ve KDV fatura hesaplayıcısından gelir (SAT-D11, SAT-D46). | Plan S0, `SalesDraftService.RiskCheck.cs` (bugünkü "onaylı kalan sipariş" bileşeni `ApprovedRemainderAsync`, `:340`), `SalesCustomerRisk.cs`, risk kontrol sözleşmesi, Lojistik sevk planı okuma portu (S0 spec), DEC-FIN-007 ilk aşaması (Finans bilgisine) |
| SAT-D314 (KLN-D28 ile aynı cevap) | İptal/azaltmada gelecek tedarik ayırması | Gelecek tedarikten ayrılmış satış sipariş satırı iptal edilir veya miktarı azaltılırsa **gelecek tedarik ayırması otomatik bırakılır**: Satış iptal/azaltma komutu aynı işlemde Stok bırakma portunu çağırır; artan ayırma kalmaz; audit'lidir. Satış, ayırma var diye "önce elle bırak" şartıyla reddetmez (seçilmeyen yön); ayırma yüzünden reddetmez, (3)'teki retler ayrı sebeptir. Stok tarafının metni KLN-D28'dir, ara red KLN-D32 (3)'tür (#1116 head `19095be0`), Stok modeli KLN-D26'dır (main); bu satır yalnız Satış etkisini yazar. *Yazar okuması (geri alınabilir; #1116 rev4 ile hizalı):* (1) Ölçü satırın **açık miktarıdır** (#1116 §3.2 adım 5): yeni açık miktar = yeni satır miktarı − sevk edilen/yüklenen toplam (SAT-D104/D112 tabanı); bırakılacak fazla = max(0, satıra bağlı aktif fiziksel tahsis + aktif gelecek ayırma − yeni açık miktar) (koordinatörün 25. tur düzeltmesi, `6823547103`; Sinan metni değil); fazla önce gelecek ayırmadan, yetmezse fiziksel tahsisten bırakılır (SAT-D315). Örnekler: satır 100, 40 sevk, 60 gelecek ayırma, 80'e azaltma → açık 40, bırakılan 20; satır 10, ayırma 4, 7'ye azaltma → bırakılan 0; fiziksel 10 + gelecek 10, 20 → 15 → gelecekten 5; 20 → 5 → gelecekten 10 + fiziksel 5. İptalde satırın bütün aktif gelecek ayırması bırakılır. Revizyonda satır kaldırma üç durumdur (koordinatör ayrımı; SAT-D167 metnini değiştirmez): (a) sevk edilmiş veya kısmi sevkli satır kalıcı 422 `sales_line_revision_line_shipped` ile reddedilir (SAT-D167, EK24:52; Satış kuralı, port çağrılmaz); (b) sevk edilmemiş, yalnız gelecek ayırması olan satır kaldırılabilir ve bütün gelecek ayırması bırakılır; (c) sevk edilmemiş, fiziksel tahsisli satır ara dönemde 409 `inventory_future_allocation_physical_release_unavailable` ile reddedilir (Stok, KLN-D32 (3); (2)). "Geçici" yalnız (c) içindir. (2) Gelecek ayırma fazlaya yetmezse fiziksel tahsis fazlası da otomatik bırakılır (Sinan 25. tur, SAT-D315 (5)). **Ara dönem:** Sinan'ın seçtiği seçeneğin metni "Fiziksel bırakma ekranı hazır olana kadar böyle bir azaltma reddedilir" der. *Koordinatör notu:* Sinan'ın "ekran" sözü aynen korunur; KLN-D32 (3) bunu "fiziksel bırakma yolu … main'e gelene kadar" diye okur. *Koordinatör/yazar fail-closed türetmesi (Sinan metni değil; KLN-D32 (3)):* ret tamdır; fiziksel bırakma gerektiren azaltmada (komutta açık fiziksel seçim dahil) hiçbir şey yazılmaz, gelecek ayırma da bırakılmaz. Reddi Stok bırakma portu #1116 §3.2 adım 0 kilitleri altında tespit eder; problem kodu (#1116 KLN-D32 (3), spec §7/§8; öneri kodu `inventory_future_allocation_physical_release_unavailable`, F017-s'de kesinleşir). Fiziksel tahsis bugünkü kurallarla kalır (DEC-SAL-005, SAT-D17, SAT-D104; #1116 §5.1 (b)). (3) Şu retler ayrı sebeplerdir ve sürer: bırakma portu okunamaz/hata verirse komut yazılmaz (aynı işlem, fail-closed); SAT-D104/D112 sevk/yükleme tabanı reddi; DEC-SAL-005 önizlemesi okunamazsa iptal reddi (KLN-D28 (9)); açık seçim bırakılacak fazlayı aşarsa ret (KLN-D30 (3), Stok yazar okuması); (2)'deki ara dönem reddi. (4) Bırakma sırası SAT-D315'tedir (#1116 §9.1 aday 3 ve 4, Sinan 23. tur); önceki geçici "seçim yoksa 409" kapalı yönü kalkmıştır. (5) Satış Stok tablosuna yazmaz; bırakmayı Stok kendi portuyla, host participant/port deseniyle yapar (port adı `IInventoryFutureAllocationReleasePortV1` #1116/#1110 önerisidir, main'de yok). Bırakma silme değildir (#1116 §3: `Released`, audit + outbox). | Satış komutları: DEC-SAL-005 sipariş iptali, SAT-D104/D112 miktar revizyonu (satır kaldırma dahil); Stok bırakma portu (#1116 F017-b), #1116 F017-s dilimi (cross-module K3), Satış tahsis okuması; Satış README :296-302, :378, :400 metni (F017-s spec görevi) |
| SAT-D315 (KLN-D30, KLN-D31 ve KLN-D32 ile aynı cevaplar) | Azaltmada bırakma sırası | (1) Satış satırı azaltılınca bırakılacak gelecek tedarik ayırması birden çok alış siparişine yayılmışsa **en geç gelecek alıştan** başlanarak bırakılır; erken gelen ayırma müşteride kalır. (2) Satırda hem depodaki etiketli mala yapılmış fiziksel tahsis hem gelecek alıştan ayırma varsa **önce gelecek ayırma** bırakılır; depodaki hazır mal müşteride kalır. (3) Kullanıcının komutta bırakılacak ayırmayı/tahsisi açıkça seçmesi bu varsayılandan üstündür. (4) SAT-D314 (4)'teki geçici "seçim yoksa 409, yazma yok" kapalı yönü kalkar; seçim yoksa varsayılan sıra uygulanır. (5) Gelecek ayırma fazlaya yetmezse kalan fazla fiziksel tahsisten **otomatik** bırakılır; fiziksel bırakma ekranı hazır olana kadar böyle bir azaltma reddedilir (Sinan 25. tur, `6823547103`, "Otomatik bırakılır (Önerilen)" ve seçenek metni). Sıra Stok bırakma portunun içinde hesaplanır; Satış yalnız açık seçimi iletir (KLN-D30 (5)). Stok metni KLN-D30/KLN-D31/KLN-D32'dir (#1116 head `19095be0`); bu satır yalnız Satış etkisini yazar. *Stok tarafının okumaları (Sinan kararı değil; #1116'da yazılır, burada seçilmez):* "en geç gelen" = alış satırının beklenen teslim tarihi (KLN-D30 (2), koordinatör iletimi); tarih kaynağı Alış `GetLineScheduleAsync`'tir (#1110 F17-1b, #1101; Stok portu kilit altında, aynı işlemde okur): satırın tarihi teslim alınmamış kalanı olan dilimlerin en geç `DeliveryDate`'i, dilimi olmayan satır tarihsiz (en geç sayılır), `Unreadable`/hata dönerse komut yazılmaz, 503 fail-closed ("tarihsiz" sayılmaz) (KLN-D30 (2), (4); #1116 head `19095be0` spec §5); eşitlik bozma kuralı (KLN-D30 (4)) ve açık seçimin bırakılacak miktarı aşamayacağı (KLN-D30 (3)) Stok yazarının geri alınabilir okumasıdır. *Miktar (koordinatörün 25. tur düzeltmesi, Sinan metni değil; KLN-D31 (3) ile hizalı):* bırakılacak fazla = max(0, aktif fiziksel tahsis + aktif gelecek ayırma − yeni açık miktar) (SAT-D314 (1)). Golden: fiziksel 10 + gelecek 10, satır 20 → 15, sevk 0 → gelecekten 5, fiziksel 10 kalır (KLN-D31 (5)); 20 → 5 → gelecekten 10 + fiziksel 5 (ara dönemde ret). *Fiziksel tahsis:* gelecek ayırma yetmediğinde veya kullanıcı seçtiğinde fiziksel tahsis otomatik bırakılır (Sinan 25. tur); bırakma Satış/Stok fiziksel tahsis kuralıyla yapılır (KLN-D31 (4)); etiket bazlı kısmi bırakma seçilmedi (#1116 F017-s `NEEDS-SPEC`). Fiziksel bırakma ekranı hazır olana kadar böyle bir azaltma reddedilir (Sinan); retin tam olması (açık fiziksel seçim dahil, hiçbir şey yazılmaz) koordinatör/yazar fail-closed türetmesidir (SAT-D314 (2), KLN-D32 (3)); DEC-SAL-005 iptal önizlemesi fiziksel tahsiste reddetmeye devam eder (#1116 §5.1 (b)). | X1 dilimi testleri, F017-s spec'i, Stok bırakma portu sözleşmesi (komutta seçim alanı), Satış revizyon komutu |

## Sınırlar

- Net kg okuma portunun anahtarı (Lojistik önerisi: talimat + tartım kimliği; "teslim tartısı = talimatın tek tartı
  kaydı", kesin tanım fason teslim dilimi spec'inde) bu kararla seçilmedi. Bölüşüm ve tartı düzeltmesi SAT-D312 ile
  cevaplandı. Oran paydasındaki "beyan kg"nın sevk satırında hangi alan olduğu ve paylaştırma artığının hangi siparişe
  yazılacağı SAT-D312'de seçilmedi; F2 spec'i önerir, para etkisi olduğu için gerekirse `NEEDS-SINAN`.
- **SAT-D312 hesap yeri (Sinan cevabı değil; AGENTS mimari değişmezi "Formula/UI/report aynı deterministic backend
  calculator'ı kullanır"):** Lojistik net kg okuma portu talimatın toplam net kg'ını, sevk satırı başına beyan kg'ı ve
  düzeltme sürümünü verir. **Lojistik bölme yapmaz;** paylaştırma (oran, kg payı), artık ve para (pay × fiyat, fark
  tutarı) yalnız `SalesInvoiceCalculator`'dadır (#1115 spec 04 `26ad52f7` ile hizalı).
- SAT-D312 (3) "fark faturası"nın yönü (tartı fazla yazıldıysa negatif fark: iade/alacak belgesi mi) cevapta yoktur; F2
  spec'i önerir, para etkisi olduğu için gerekirse `NEEDS-SINAN`.
- SAT-D311'de ayrı risk satırının adı/ekran metni ve Finans cevabında "işlendi" kümesini taşıyan alanın adı seçilmedi
  (S0 dilimi; Finans sözleşmesi #1117). Kaynak `OpenReceivables` ile aynı Finans cevabıdır; olay kaynak sayılmaz (plan S0 (b)).
- Birim fiyat, net kg'ın faturaya ondalık hassasiyeti, satır tutarı yuvarlaması ve sipariş kg'ı ile fark toleransı
  (`OPEN-004` ilişkili) bu kararla seçilmedi (plan N-9). Plan `ASSUMPTION-SAT-RISK-5` yalnız kilitli SAT-D11 satır
  tutarı yuvarlamasını uygular ve `OPEN-004`'ü etkinleştirmez; net kg/kg payı hassasiyeti ve artık F2 açık noktasıdır.
  Tevkifat kodu SAT-D110'a göre Ayarlar'dadır.
- #66 `6821949043` (LOJ-D14, Lojistik kararı): fason teslim satış sevkiyle aynı müşteri risk kapısına tabidir, istisnayla
  geçer. Satış etkisi plan S2/F1'dedir (aynı kapı portunun ikinci tüketicisi). Fason hizmet siparişinin tartıdan önceki
  risk tutarı SAT-D313 ile cevaplandı (sevk planına açılan miktar × birim fiyat).
- SAT-D313 `KARAR_SICILI` DEC-FIN-007'nin "onaylı kalan sipariş" bileşenini daraltır (son kullanıcı kararı önceliği,
  AGENTS); sicil metni bu PR'da değişmez; daraltma ayrı, incelenmiş bir sicil PR'ında yapılır (koordinatör notu `6822810785`).
  Finans şeridine ve koordinatöre bildirildi. Sevk planı miktarının kaynağı
  (Lojistik sevk planı/yükleme talimatı okuması, mevcut port mu yeni mi), fasonda plan kg'ı, plan iptali/azaltmasında
  düşme ve durum adları bu kararla seçilmedi; S0 spec'i önerir, para etkisi varsa `NEEDS-SINAN`.
- SAT-D313'ün koda gelişine kadar main davranışı değişmez: bugünkü "onaylı kalan sipariş" fason siparişi de onaylı miktar
  × fiyatla sayar (SAT-D175; `SalesStore.cs` `ListOpenApprovedOrdersAsync`), toplam risk ise "faturalanmamış sevk" `NoSource`
  olduğu için hiç `Within` olmaz. S0 bu bileşeni sevk planı bileşeniyle değiştirir; sevk planı kaynağı okunamazsa bileşen
  `Unavailable` olur, sıfır veya tahmin sayılmaz ve risk `Within` dönmez (DEC-FIN-009 fail-closed; kapı yalnız `Valid`
  istisnayla geçer, SAT-D310).
- SAT-D314'ün kodu #1116 F017-s (Stok + Satış cross-module K3) ve Stok bırakma portu (F017-b) ile gelir; port main'de
  olmadan Satış komutuna bağlanmaz. Bu, Satış ile Stok arasındaki ilk aynı-işlem yazmadır (host participant; emsal
  `InventoryManufacturingRealizationParticipant.StagePlanRemainderReleaseAsync`). Satış README :302'deki "Satış ile sahipler
  arasında transaction yok" yarış beyanı ve DEC-SAL-005/SAT-D104 metninin değişecek satırları (README :296-302, :378,
  :400) F017-s spec görevidir. Bırakma neden kodu adı Stok'un kod listesindedir (#1116 §3); bu belge ad seçmez.
- SAT-D314/SAT-D315'te seçilmeyenler (F017-s spec önerir, gerekirse `NEEDS-SINAN`): revizyonda bırakmanın gönderimde mi onayda mı yapılacağı (taban iki aşamada yeniden koşar); SAT-D115 müşteri
  reddiyle geri dönüşte bırakılmış ayırmanın ne olacağı; SAT-D17 satır kapatma ve zorla kapatma. Sinan'ın cevabı iptal ve
  azaltmayı anar; satır kapatmada bugünkü kural (aktif tahsiste red, README :850) değişmez.
- Bu belgede kod/şema/migration yoktur; SAT-D310–D315'in kodu plan dilimlerinde K3 kapısıyla gelir (SAT-D209 sınırı).
- Hiçbir `OPEN-*` kapanmaz.

İnsan kapıları değişmez: deployment, environment, secret, Settings'ten workflow etkinleştirme, grant/rol/Critical,
production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` somut kullanıcı kararı ister. Bu belge hiçbirini
vermez.
