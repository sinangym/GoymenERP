# Sinan Kararları — 2026-11-12 (Lojistik: fason hizmet bedeli kg kaynağı, e-İrsaliyesiz transferde "yola çıktı" anı)

Bu belge önceki karar dosyalarını silmez; `LOJ-D9` ve `LOJ-D10` ekler. `SINAN_KARARLARI_2026-11-12_LOJISTIK_F012_F020.md`
içindeki `LOJ-D1…D8` aynen geçerlidir; oradaki "Sınırlar" bölümünün ilk iki maddesinden **kg kaynağı** burada kapanır
(aşağıda), sahiplik kaydı maddesi ayrı bir kod doğrulamasıdır (spec 04, "Kod doğrulaması").

**Kaynak:** Sinan, Issue #66 yorumu 6820601088 (11. ve 12. tur; 2026-11-12T10:03:40Z). Birincil yorumdan doğrulanmıştır;
koordinatör aktarımı karar kaynağı sayılmadı. Cevaplar kelimesi kelimesine:

> **11. tur, m.4 — F-012 fason hizmet bedeli kg kaynağı (#1115 spec NEEDS-SINAN-2):** **Teslim tartısı net kg** (önerilen
> "üretimde işlenen girdi kg" seçilmedi). Bedel, müşteriye geri teslim edilen malın tartıdaki net kg'ı üzerinden hesaplanır;
> fire bedele girmez. Fatura Satış'taki fason hizmet siparişinden kesilir.

> **12. tur, m.5 — e-İrsaliyesiz transferde "yola çıktı" anı (#1108 review B P2-1; KLN-D20 (3)/KLN-D25):** **Çıkış
> tartısında** (önerilen). Araç çıkış tartısı ya da araç çıkışı kaydedilince mal yola çıkmış sayılır. Tartı kaydı yoksa
> sevkin kaydedildiği an esas alınır. Bu andan sonra iptal yoktur; geri dönüş teslim + ters transferle olur. e-İrsaliye'li
> sevkte 6. tur m.1 (GİB'e gönderim) geçerli kalır.

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| LOJ-D9 | Fason hizmet bedeli kg kaynağı | Bedel kg'ı = müşteriye geri teslim edilen malın **teslim tartısı net kg**'ı (WGT-001 net). Fire bedele girmez. Fatura Satış'taki fason hizmet siparişinden kesilir; Lojistik fatura kesmez, yalnız net kg olgusunu okuma portuyla sunar. | F-012 (spec 04), Satış/Finans faturalama |
| LOJ-D10 | e-İrsaliyesiz transferde "yola çıktı" anı | Araç çıkış tartısı ya da araç çıkışı kaydedilince mal yola çıkmış sayılır; tartı kaydı yoksa sevk kaydının oluştuğu an esas alınır. Sonrasında iptal yoktur (geri dönüş teslim + ters transfer). e-İrsaliye'li sevkte GİB'e gönderim anı (6. tur m.1) geçerlidir. Stok bu anı Lojistik'ten okur. | Lojistik okuma portu (araç çıkışı), Stok transfer, LOJ-D8 "kesinleşme" |

LOJ-D10, LOJ-D8'in "kesinleşme" tanımını e-İrsaliyesiz sevk için somutlaştırır; D8'i değiştirmez.

## Karar olmayanlar (koordinatör/ajan notları)

- NEEDS-SINAN-3 (kesinleşme anı) 7. tur m.4 ile cevaplıdır: sevk kaydı yoksa talimat sebeple iptal edilir (LOJ-D6/D8).
- NEEDS-SINAN-4 (tahsis/rezerv serbest bırakma) teknik noktadır, PR'da gerekçelendirilir; politika seçmez.
- NEEDS-SINAN-5 (iptal yetkisi): yalnız izin adı kodlanır; grant insan kapısıdır.

## Sınırlar

- "Araç çıkışı" kaydının hangi ekran/olayla girileceği ve Lojistik'te yeni bir "araç çıkış" okuma portunun şekli
  Stok oturumuyla sözleşme netleştirilince spec'te ayrıntılanır; bu belge tablo/ekran açmaz (SAT-D209 sınırı).
- Fason hizmet bedelinin birim fiyatı/yuvarlaması Satış/Finans kararıdır; burada seçilmez.

İnsan kapıları değişmez: deployment, environment, secret, Settings'ten workflow etkinleştirme, grant/rol/Critical,
production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` somut kullanıcı kararı ister. Bu belge hiçbirini
vermez.
