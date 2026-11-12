# Sinan Kararları — 2026-11-12 (Lojistik: fason hizmet bedeli kg kaynağı, e-İrsaliyesiz transferde "yola çıktı" anı)

Bu belge önceki karar dosyalarını silmez; `LOJ-D9` ve `LOJ-D10` ekler. `SINAN_KARARLARI_2026-11-12_LOJISTIK_F012_F020.md`
içindeki `LOJ-D1…D8` aynen geçerlidir; oradaki "Sınırlar" bölümünün ilk iki maddesinden **kg kaynağı** burada kapanır
(aşağıda), sahiplik kaydı maddesi ayrı bir kod doğrulamasıdır (spec 04, "Kod doğrulaması").

**Kaynak:** Sinan'ın 12 Kasım 2026 11. ve 12. tur `AskUserQuestion` cevapları; koordinatör Issue #66 yorumu
[`6820601088`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820601088) olarak kaydetti
(2026-11-12T10:03:40Z); Lojistik şeridi yorumu `gh api` ile birincil kaynaktan okudu. Aşağıdaki iki cevap kelimesi
kelimesine aktarılmıştır; her alıntının başındaki tur/madde etiketi alıntı **dışındadır** (yazar notu).

**11. tur, m.4 — F-012 fason hizmet bedeli kg kaynağı (#1115 spec NEEDS-SINAN-2):**

> **Teslim tartısı net kg** (önerilen "üretimde işlenen girdi kg" seçilmedi). Bedel, müşteriye geri teslim edilen malın tartıdaki net kg'ı üzerinden hesaplanır;
> fire bedele girmez. Fatura Satış'taki fason hizmet siparişinden kesilir.

**12. tur, m.5 — e-İrsaliyesiz transferde "yola çıktı" anı (#1108 review B P2-1; KLN-D20 (3)/KLN-D25):**

> **Çıkış
> tartısında** (önerilen). Araç çıkış tartısı ya da araç çıkışı kaydedilince mal yola çıkmış sayılır. Tartı kaydı yoksa
> sevkin kaydedildiği an esas alınır. Bu andan sonra iptal yoktur; geri dönüş teslim + ters transferle olur. e-İrsaliye'li
> sevkte 6. tur m.1 (GİB'e gönderim) geçerli kalır.

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| LOJ-D9 | Fason hizmet bedeli kg kaynağı | Bedel kg'ı = müşteriye geri teslim edilen malın **teslim tartısı net kg**'ı (WGT-001 net). Fire bedele girmez. Fatura Satış'taki fason hizmet siparişinden kesilir; Lojistik fatura kesmez. Paylaşım ve eksik tartı kuralları `..._LOJISTIK_EK3.md` LOJ-D12'dedir. Satış tarafındaki karşılığı SAT-D309 (net kg kaynağı) ve SAT-D312 (Satış #1119). | F-012 (spec 04), Satış/Finans faturalama |
| LOJ-D10 | e-İrsaliyesiz **Stok transferinde** "yola çıktı" anı | Araç çıkış tartısı ya da araç çıkışı kaydedilince mal yola çıkmış sayılır; tartı kaydı yoksa sevk kaydının oluştuğu an esas alınır. Sonrasında iptal yoktur (geri dönüş teslim + ters transfer). e-İrsaliye'li sevkte GİB'e gönderim anı (6. tur m.1) geçerlidir. Hangi depoda "tartı yok" sayılacağı `..._LOJISTIK_EK3.md` LOJ-D11 (depo ayarı) ile ayrılır; kantarlı depoda e-İrsaliye'li transferin penceresi LOJ-D11'deki "hangisi önce" kuralıyla kapanır. | Stok transfer (KLN-D20 (3), KLN-D25 (7), KLN-D27 (e-İrsaliye'li iptal penceresi), #1108 — aynı cevap Stok kaydında; karşılıklı atıf: LOJ-D10, `SINAN_KARARLARI_2026-11-12_LOJISTIK_EK2.md`, #1115) |

LOJ-D10 **yalnız Stok transferi** içindir; yükleme talimatı iptalini (LOJ-D8) değiştirmez ve ona bağlanmaz. F-020'de
"kesinleşme" = talimatta kayıtlı sevk (7. tur m.4, LOJ-D8); çıkış tartısı F-020 kapısını belirlemez.

**Yazarın okumaları (geri alınabilir, karar değil):** Lojistik'in net kg / çıkış olgusunu **okuma portuyla** sunması ve
Stok'un bu anı Lojistik'ten okuması. Net kg portunun sahibinin Lojistik mi Stok mu olacağı Satış #1119'da (SAT-D309,
N-7) ele alınır; transfer çıkış portunun adı ve şekli Stok/Lojistik arasında doğrulanır. İkisi de yazar okumasıdır.

## Karar olmayanlar (koordinatör/ajan notları)

- NEEDS-SINAN-3 (kesinleşme anı) 7. tur m.4 ile cevaplıdır: sevk kaydı yoksa talimat sebeple iptal edilir (LOJ-D6/D8).
- NEEDS-SINAN-4 (tahsis/rezerv serbest bırakma) teknik noktadır, PR'da gerekçelendirilir; politika seçmez. İptalin
  bildirim/rapor etkisi (`F012_F020` dosyası, Sınırlar) bu PR'ın kapsamı dışındadır.
- NEEDS-SINAN-5 (iptal yetkisi): yalnız izin adı kodlanır; grant insan kapısıdır.

## Sınırlar

- "Araç çıkışı" kaydının hangi ekran/olayla girileceği ve Lojistik'te yeni bir "araç çıkış" okuma portunun şekli
  Stok oturumuyla sözleşme netleştirilince spec'te ayrıntılanır; bu belge tablo/ekran açmaz (kapsam sınırı; SAT-D209
  yalnız satış/alış/finans/üretim şeritlerini bağlar, Lojistik için KLN-D5 ile kalkmıştır).
- Fason hizmet bedelinin birim fiyatı/yuvarlaması Satış/Finans kararıdır; burada seçilmez.

İnsan kapıları değişmez: deployment, environment, secret, Settings'ten workflow etkinleştirme, grant/rol/Critical,
production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` somut kullanıcı kararı ister. Bu belge hiçbirini
vermez.
