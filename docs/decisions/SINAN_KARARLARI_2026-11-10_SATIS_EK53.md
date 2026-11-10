# Sinan Kararları — 2026-11-10 (Satış: S-05 marjının net satışında vade farkı)

Bu belge önceki karar dosyalarını silmez; SAT-D275'i ekler. EK51 (`SINAN_KARARLARI_2026-11-09_SATIS_EK51.md`)
SAT-D259'daki "KDV hariç net satış" ifadesinin müşteriye yansıtılan vade farkını içerip içermediğini netleştirir.
SAT-D247, SAT-D250 (EK49), SAT-D256…SAT-D261 (EK51), SAT-D262…SAT-D274 (EK52), DEC-PRD-011 ve `OPEN-008`
geçerlidir; bu belge onların metnini değiştirmez. SAT-D263 (3) (EK52, `SINAN_KARARLARI_2026-11-10_SATIS_EK52.md`)
"ikinci maliyet türü" notu olarak aynen kalır.

**Kaynak:**

- Sinan'ın 10 Kasım 2026'da Claude Code cloud oturumunda (`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine
  doğrudan soruyla verdiği cevap. Soru, PR #1045'teki marj hesaplayıcısının (`SalesLineMarginCalculator`) net satışı
  vade farkı dahil satır tutarından aldığı fark edilince soruldu. Cevap koordinatör tarafından bu belgeyi yazan ajana
  iletildi.
- Seçilen seçeneğin açıklaması aynen yazılmıştır: "Marj yalnız malın satış fiyatından hesaplanır; vade farkı
  finansman getirisi sayılır, ürün kârlılığını şişirmez." Seçilmeyen seçenekler, açıklamaları ve "(Önerilen)"
  işaretleri bu ajana iletilmediği için yazılmadı (uydurulmadı). "Cevap" sütunu iletilen anlamdır.

Bu belge merge makbuzu dışında Issue #66'ya karar yazmaz.

## Soru ve cevaplar

| # | Soru | Cevap (iletilen anlam) | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Müşteriye yansıtılan vade farkı, marjın "KDV hariç net satış" tutarına (SAT-D259 (2)) girsin mi? | **Hayır.** Marjın net satışı vade farkı hariçtir: KDV hariç, iskonto sonrası ve vade farkı eklenmeden önceki satır tutarı. | "Marj yalnız malın satış fiyatından hesaplanır; vade farkı finansman getirisi sayılır, ürün kârlılığını şişirmez." (aynen) Seçilmeyen seçenekler iletilmedi. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D275 | Marjın net satışında vade farkı | (1) S-05 marjının net satışı (SAT-D259 (2) ve (3)) müşteriye yansıtılan **vade farkını içermez**: net satış = miktar × iskonto sonrası, vade farkı eklenmeden önceki birim fiyat, **KDV hariç**. (2) Marj tutarı = bu net satış − maliyet; marj yüzdesi = marj tutarı / bu net satış. SAT-D259'un formülü değişmez; yalnız "net satış"ın kapsamı netleşir. (3) Gerekçe (seçilen seçeneğin açıklaması, aynen): "Marj yalnız malın satış fiyatından hesaplanır; vade farkı finansman getirisi sayılır, ürün kârlılığını şişirmez." (4) Vade farkı satırın fatura/sipariş tutarında, KDV matrahında ve belge toplamlarında **olduğu gibi kalır**; bu karar yalnız marjın satış tarafını etkiler. (5) SAT-D263 (3) değişmez: vade farkının ileride marja **ayrı bir maliyet türü** olarak girip girmeyeceği ayrı, ileride verilecek bir karardır. Uygulama ayrıntısı (geri alınabilir): net satış, vade farkı dahil satır tutarıyla aynı yarı-yukarı kuralla (SAT-D11, mevcut `Money` kuralı) satır başına bir kez yuvarlanır; tek deterministik hesaplayıcı (SAT-D247) bu tutarı mevcut satır fiyat hesaplayıcısının iskonto adımından alır, ekranda yeniden hesaplanmaz. | Marj hesaplayıcısı (`SalesLineMarginCalculator`), satır toplamları (`SalesOrderTotalsCalculator`), line-pricing marj alanları, S-05 (UI-50) açıklama metni |

## Uygulama listesi

| Karar | İş | Sahip |
|---|---|---|
| SAT-D275 | Marj hesaplayıcısının net satış girdisi vade farkı hariç satır tutarıdır; golden testte aynı satırın vade farkı olan ve olmayan hâli aynı marjı verir. Stok portu main'e girene kadar line-pricing marj bloğu "kullanılamıyor" kalır (EK52). | Satış (Claude) şeridi (PR #1045) |

## Sınırlar

- **Belge kod değiştirmez.** Bu belge kod, manifest, migration, OpenAPI veya Ayarlar verisi değiştirmez; uygulama PR #1045'teki ayrı commit'tedir.
- **Vade farkının ayrı maliyet türü olarak marja girmesi açık kalır** (SAT-D263 (3)); bu belgede seçilmez.
- **Stok portu gelene kadar marj "kullanılamıyor" kalır** (SAT-D247 fail-closed; EK52 "Sınırlar").
- EK52 "Sınırlar"daki `NEEDS-SINAN` maddeleri (birim dönüşümü, döviz cinsi alış maliyeti, ek maliyetler, iade, negatif
  stok, karma kaynaklı ürün) bu belgede seçilmez.
- **`OPEN-008` açık kalır.** Bu belge hiçbir `OPEN-*` kaydını kapatmaz; grant/rol ataması veya production değişikliği
  yapmaz; yeni izin adı açmaz.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
