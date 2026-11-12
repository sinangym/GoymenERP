# Sinan Kararları — 2026-11-12 (Satış: fason hizmet faturasında kg kaynağı, sevk risk kapısı, sevk planı esaslı risk)

Bu belge önceki karar dosyalarını silmez; **SAT-D309, SAT-D310, SAT-D311, SAT-D312, SAT-D313 ve SAT-D314**'ü ekler. URT-D16 ("fatura hizmet bedelidir"), SAT-D175, SAT-D195, SAT-D209,
SAT-D11 ve SAT-D110 aynen geçerlidir; bu belge onların metnini değiştirmez. `KARAR_SICILI` değişmez.

**Kimlik:** SAT-D308 main'dedir (#1108, `2efb3f68`; Stok,
`SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md`, fire anahtarı kapalıyken fire maliyeti); bu karar
**SAT-D309** alır. #66 16. tur yorumu bu maddeyi "LOJ-D9 / SAT-D308" etiketiyle anar; o etiket koordinatörün çalışma
adıdır, sicilde bu madde **SAT-D309**'dur. SAT-D309 yalnız #1115'te aynı Sinan cevabına atıf olarak geçer; SAT-D310–D314
main'de ve açık `claude/*`, `codex/*` dallarında kullanılmaz (`git grep`, 2026-11-12).

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

> **Fason hizmet siparişinin tutarı müşteri riskinde tartıdan önce nasıl sayılsın?** "Sevk planı için açılan miktar x
> birim fiyat, bu fason ve satışlarda gerçekleşecek risk oluyor, irsaliyesi faturası kesilene kadar"

> **Onaylı siparişin henüz sevk planına alınmamış kalanı riskte sayılmaya devam etsin mi?** "Siparişin tamamı risk
> sayılmaz, sevk planına alınan sevk olan faturası kesilenler durumuna göre risk durumlarında görünür"

Aynı soru (NEEDS-SINAN `6822311631`) koordinatör oturumunda da `AskUserQuestion` ile soruldu; kayıt #66
[6822767886](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822767886) (21. tur, madde 1; 2026-11-12T11:48:36Z). Birincil yorumdan `gh api` ile doğrulandı. Cevap
kelimesi kelimesine:

> **Satış/Lojistik — fason hizmet siparişinin tartıdan önceki risk tutarı:** "Sevk plan miktarı x onaylı fiyat"

Yorumdaki koordinatör okuması (karar metni değil): tartıdan önce riske yazılan tutar sevk planındaki (yükleme talimatı)
miktar × onaylı kg fiyatıdır (SAT-D11 KDV kuralıyla), tartıdan sonra gerçek tutara geçer; sevk planı olmayan fason sipariş
için ek bir sayım yöntemi seçilmemiştir, yazar ayrı yöntem eklemez, gerekirse yeni `NEEDS-SINAN` açar. Üç cevap aynı
yöndedir; SAT-D313 üçünden yazılır.

**Ek kaynak (SAT-D314):** Sinan, #66 [6822767886](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822767886) madde 2 (koordinatör oturumunda `AskUserQuestion`); soru
PR #1116 `STOK_F017_GELECEK_TEDARIK_AYIRMA_SPEC_v1.md` §9.1 NEEDS-SINAN adayı 1'dir. Cevap kelimesi kelimesine:

> **Stok/Satış — gelecek tedarikten ayrılmış satış satırı iptal/azaltılınca:** "Otomatik bırakılır (Önerilen)"

Yorumdaki açıklama: "Satış iptal/azaltma komutu aynı işlemde Stok bırakma portunu çağırır; artan ayırma kalmaz; audit'li.
(Seçilmeyen: Satış reddeder, önce elle bırak.)" Aynı yorumun madde 3'ü (canlanan alış satırı) Alış/Stok'undur; burada
yazılmaz.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Fason hizmet bedeli hangi kg üzerinden hesaplanır? | **Teslim tartısı net kg** | Bedel, müşteriye geri teslim edilen malın tartıdaki net kg'ı üzerinden hesaplanır; fire bedele girmez. Fatura Satış'taki fason hizmet siparişinden kesilir. Seçilmeyen (önerilen): "üretimde işlenen girdi kg". Yorumda öteki seçeneklerin açıklaması yer almaz; uydurulmadı. |
| 2 | Risk okunamazsa sevk tek sevk istisnasıyla geçebilsin mi? (N-5) | **Evet, istisnayla geçer** | Yetkili onaylarsa risk okunamasa da tek sevk çıkar. Seçilmeyen (önerilen): "Hayır, sevk durur". |
| 3 | Kesilmiş ama Finans'ın işlemediği fatura riskte nerede? (N-6) | **"Faturalanmamış sevkte evet ama bu tutarda ayrı bir risk türü olarak gösterilir"** (Sinan'ın kendi metni) | Finans işleyene kadar faturalanmamış sevk toplamında sayılır, ekranda ayrı risk türü/satırı; işlenince açık bakiyeye geçer. |
| 4 | Tek tartı birden çok fason siparişini kapsarsa ya da tartı yoksa? (N-8) | **Oranla böl, tartı zorunlu** | Net kg sevk satırlarındaki beyan kg oranında paylaştırılır; tartısız fason teslim kaydedilemez. |
| 5 | Çıkış sonrası tartı yanlışsa? | **Gerekçeli düzeltme** | Yetkili kişi gerekçeli düzeltme kaydı girer; eski değer denetim izinde kalır; fatura kesilmişse fark Satış'tan fark faturasıyla kapatılır. |
| 6 | Fason siparişin tartı öncesi risk tutarı; plana alınmamış sipariş kalanı | **Sinan'ın kendi metni** (iki soruda da önerilen seçilmedi; yukarıdaki alıntılar) | Risk sevk planına açılan miktar × birim fiyattan başlar (satış ve fason), irsaliye ve fatura kesilene kadar; siparişin tamamı risk sayılmaz, tutar durumuna göre (planlı / sevk edilmiş / faturalı) görünür. Koordinatör sorusunda (`6822767886` m.1) aynı yön: "Sevk plan miktarı x onaylı fiyat". |
| 7 | Gelecek tedarikten ayrılmış satış satırı iptal/azaltılınca ayırma ne olur? (#1116 §9.1 aday 1) | **Otomatik bırakılır** (önerilen) | Satış iptal/azaltma komutu aynı işlemde Stok bırakma portunu çağırır; artan ayırma kalmaz; audit'li. Seçilmeyen: "Satış reddeder, önce elle bırak". |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D309 (LOJ-D9 ile aynı cevap) | Fason hizmet bedeli kg kaynağı | Bedel kg'ı = müşteriye geri teslim edilen malın **teslim tartısı net kg**'ı. Fire bedele girmez. Fatura Satış'taki fason hizmet siparişinden kesilir. *Eşleme (karar eki değil; #1115 LOJ-D9 metniyle aynı):* teslim tartısı WGT-001 net olarak okunur, kesin tanım fason teslim dilimi spec'inde; Lojistik fatura kesmez. | Satış fason hizmet siparişi faturası (`docs/delivery/SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1.md` F1/F2), `SalesInvoiceCalculator` (tek hesaplayıcı), Lojistik net kg okuma portu (F1), Finans `sales.invoice.finalized.v1` tüketimi |
| SAT-D310 | Risk okunamazken sevk | Müşteri riski okunamıyorsa (Finans, kur veya fatura kaynağı yok; port sebebi `SourceUnavailable`) sevk, yetkili onaylı **tek sevk istisnası** (SAT-D154/D160, `Valid`) ile geçebilir; istisna yoksa durur. *Türetilmiş (yazar okuması; DEC-FIN-009 fail-closed, Lojistik L-7):* risk kapısı portunun kendisi okunamıyorsa (port hatası/zaman aşımı) istisna da okunamadığı için sevk durur. *Yazar okuması:* "yetkili" mevcut istisna onay yetkisidir; yeni izin/grant seçilmez. | `docs/delivery/SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1.md` S2 kapı kuralı, `ISalesShipmentRiskGateReadPortV1`, `SalesRiskExceptionService` (istisna talebi `Unavailable`'da zaten açık), Lojistik sevk kapısı |
| SAT-D311 | Finans'ın işlemediği kesilmiş fatura | Satış'ta kesilmiş, Finans'ın henüz işlemediği faturanın tutarı müşteri riskinde **faturalanmamış sevk toplamında sayılır**, ama ekranda **ayrı bir risk türü/satırı** olarak gösterilir. *Koordinatör/yazar okuması:* Finans işleyince bu satırdan düşer ve açık bakiyeye geçer; aynı tutar iki bileşende aynı anda sayılmaz (AGENTS çift sayım yasağı). Tutar fatura hesaplayıcısından gelir (SAT-D11 ve AGENTS tek hesaplayıcı; SAT-D90 yalnız kur); "Finans işledi" olgusu `OpenReceivables` ile aynı Finans cevabından okunur (plan S0 (b); olay kaynak sayılmaz), Satış tahmin etmez. | Plan S0, `SalesDraftService.RiskCheck.cs`, risk kontrol sözleşmesinde ayrı bileşen (`SalesDraftRiskCheckV1`, sürüm etkisi S0'da), Finans işlendi olgusu |
| SAT-D312 | Fason net kg paylaşımı ve tartı düzeltmesi (Satış etkisi) | (1) Tek teslim tartısı birden çok fason siparişini kapsıyorsa net kg, sevk satırlarındaki **beyan kg oranında** siparişlere paylaştırılır; fatura her siparişin payından kesilir (SAT-D309). (2) Tartısı olmayan fason teslim kaydedilemez; Satış tartısız fason satırı faturalamaz. (3) Çıkış sonrası tartı hatası yetkili kişinin gerekçeli düzeltme kaydıyla düzeltilir, eski değer denetim izinde kalır; fatura kesilmişse fark **Satış'tan fark faturasıyla** kapatılır. Düzeltme kaydı Lojistik tartı kaydının işidir (LOJ-D13); "yetkili kişi" için izin adı önerilebilir, grant/rol insan kapısıdır. Hesabın hangi modülde yapıldığı bu kararda seçilmedi (Sınırlar, yazar okuması). | Plan F1/F2, `SalesInvoiceCalculator`, Lojistik net kg okuma portu, fark faturası akışı |
| SAT-D313 | Siparişin risk tutarı: sevk planı esaslı (satış ve fason) | Müşteri riskinde siparişin tamamı sayılmaz. Risk, **sevk planına açılan miktar × birim fiyat** ile başlar; bu satış ve fason siparişler için aynıdır ve irsaliye/fatura kesilene kadar böyle sayılır. Tutar durumuna göre (sevk planına alınan / sevk olan / faturası kesilen) ayrı risk durumlarında görünür. Birim fiyat siparişin onaylı fiyatıdır (`6822767886` m.1: "onaylı fiyat"; fasonda onaylı kg fiyatı, SAT-D195). SAT-D175 ("risk kontrolü normal siparişle aynı") ile tutarlıdır: fason ve satış aynı kuralla sayılır. *Yazar okuması:* plana alınmamış sipariş kalanı riske girmez; plansız fason sipariş için ek yöntem seçilmedi; her tutar tam olarak bir durumda sayılır (AGENTS çift sayım yasağı); tutar ve KDV fatura hesaplayıcısından gelir (SAT-D11, SAT-D46). | Plan S0, `SalesDraftService.RiskCheck.cs` (bugünkü "onaylı kalan sipariş" bileşeni, `:412-430`), `SalesCustomerRisk.cs`, risk kontrol sözleşmesi, Lojistik sevk planı okuma portu (S0 spec), DEC-FIN-007 ilk aşaması (Finans bilgisine) |
| SAT-D314 | İptal/azaltmada gelecek tedarik ayırması | Gelecek tedarikten ayrılmış satış sipariş satırı iptal edilir veya miktarı azaltılırsa ayırma **otomatik bırakılır**: Satış iptal/azaltma komutu aynı işlemde Stok bırakma portunu çağırır; satır miktarını aşan ayırma kalmaz; audit'lidir. Satış komutu bu yüzden reddetmez. *Yazar okuması:* azaltmada yalnız yeni satır miktarını aşan kısım bırakılır; bırakma silme değildir (#1116 §3: `Released`, audit + outbox); bırakma portu okunamaz/hata verirse iptal/azaltma komutu da yazılmaz (aynı işlem, fail-closed). | Satış satır iptal/azaltma komutları, Stok bırakma portu (#1116 F017-b), #1116 F017-s dilimi (cross-module K3; §5.1 (c) kapalı yönün yerine bu karar), Satış tahsis okuması |

## Sınırlar

- Net kg okuma portunun anahtarı (Lojistik önerisi: talimat + tartım kimliği; "teslim tartısı = talimatın tek tartı
  kaydı", kesin tanım fason teslim dilimi spec'inde) bu kararla seçilmedi. Bölüşüm ve tartı düzeltmesi SAT-D312 ile
  cevaplandı. Oran paydasındaki "beyan kg"nın sevk satırında hangi alan olduğu ve paylaştırma artığının hangi siparişe
  yazılacağı SAT-D312'de seçilmedi; F2 spec'i önerir, para etkisi olduğu için gerekirse `NEEDS-SINAN`.
- **SAT-D312 hesap yeri (Sinan cevabı değil; AGENTS mimari değişmezi "Formula/UI/report aynı deterministic backend
  calculator'ı kullanır"):** Lojistik net kg okuma portu talimatın toplam net kg'ını, sevk satırı başına beyan kg'ı ve
  düzeltme sürümünü verir. **Lojistik bölme yapmaz;** paylaştırma (oran, kg payı), artık ve para (pay × fiyat, fark
  tutarı) yalnız `SalesInvoiceCalculator`'dadır (#1115 spec 04 `4f96492a` ile hizalı).
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
  AGENTS); sicil metni bu PR'da değişmez, Finans şeridine ve koordinatöre bildirilir. Sevk planı miktarının kaynağı
  (Lojistik sevk planı/yükleme talimatı okuması, mevcut port mu yeni mi), fasonda plan kg'ı, plan iptali/azaltmasında
  düşme ve durum adları bu kararla seçilmedi; S0 spec'i önerir, para etkisi varsa `NEEDS-SINAN`.
- SAT-D313'ün koda gelişine kadar main davranışı değişmez: bugünkü "onaylı kalan sipariş" fason siparişi de onaylı miktar
  × fiyatla sayar (SAT-D175; `SalesStore.cs` `ListOpenApprovedOrdersAsync`), toplam risk ise "faturalanmamış sevk" `NoSource`
  olduğu için hiç `Within` olmaz. S0 bu bileşeni sevk planı bileşeniyle değiştirir; sevk planı kaynağı okunamazsa bileşen
  `Unavailable` olur, sıfır veya tahmin sayılmaz ve risk `Within` dönmez (DEC-FIN-009 fail-closed; kapı yalnız `Valid`
  istisnayla geçer, SAT-D310).
- SAT-D314'ün kodu #1116 F017-s (Stok + Satış cross-module K3) ve Stok bırakma portu (F017-b) ile gelir; port main'de
  olmadan Satış komutuna bağlanmaz. Bırakma neden kodu adı Stok'un kod listesindedir (#1116 §3); bu belge ad seçmez.
- Bu belgede kod/şema/migration yoktur; SAT-D310–D314'ün kodu plan dilimlerinde K3 kapısıyla gelir (SAT-D209 sınırı).
- Hiçbir `OPEN-*` kapanmaz.

İnsan kapıları değişmez: deployment, environment, secret, Settings'ten workflow etkinleştirme, grant/rol/Critical,
production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` somut kullanıcı kararı ister. Bu belge hiçbirini
vermez.
