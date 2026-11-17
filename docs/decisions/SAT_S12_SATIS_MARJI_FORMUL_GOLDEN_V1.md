# SAT-S12 Satış Marjı Formül Golden v1

## Kaynak ve kabul sınırı

Bu belge yeni Sinan kararı eklemez; SAT-D265(5) için bağımsız formül golden adayını kaydeder. Kullanıcı, bağımsız incelemenin raporunu ve aşağıdaki kopyalanabilir golden metnini çalışma sohbetine iletti. Aktarımın kaynak kaydı: [Issue #1189 / 6960758947](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6960758947). Bu bağlantı **ajan aktarımıdır**, bağımsız insan GitHub yazarlığı veya DOCS/K3 review imzası değildir. İnceleyen oturumun kimliği burada bağımsız olarak doğrulanmış sayılmaz.

İncelenen kaynak: `codex/tren-1-satis-kapanis-w8`, exact head `e20f0ad14a44ecfe927060dbe0a2ff7e3eda9c8c`. İnceleme formül çekirdeği için şartlı **APPROVE**, P0/P1 yok; açılışı engelleyen **P2-1** bildirir: Satış çağrısının fonksiyonel para birimini maliyetin kendi para biriminden seçmesi TL kuralını geçersiz kılar. Yazarın `3aa5b4c58` düzeltmesi ayrı tren kaynağıdır; bu golden incelemesi yeni uygulama head'ini onaylamaz.

Kaynak karar dosyaları: `SINAN_KARARLARI_2026-11-09_SATIS_EK51.md`, `SINAN_KARARLARI_2026-11-10_SATIS_EK52.md` ve daha yeni ilgili satış kararları. Formül golden metni aşağıya iletildiği biçimiyle aktarılmıştır; yazar testlerinden türetilmemiştir. İnceleme kesin rasyonel aritmetik kullandığını bildirir. Proje Unit/SQL/HTTP/Vitest/browser/CI koşumu bu incelemenin kapsamında değildir.

Bu dosyanın varlığı formülü açmaz. İki bağımsız exact-head DOCS review, main'e alınma ve P2-1'in giderilmesi gerekir. Son uygulama K3 kapısı ayrıca sürer. Eski karar dosyası, grant/rol/Critical, production, OPEN veya Finance STOP değiştirilmez.

## İletilen golden metni

Kaynak: SAT-D259, D261, D262, D263, D265(1-4), D275, D295, D297, D298; OPEN-INV-AVGCOST-FX.
Kapsam: TL, maliyet birimi = sipariş birimi, miktar > 0.
Tanım: NetSatış = iskonto sonrası, vade farkı öncesi, KDV hariç satır tutarı; kuruşa
bir kez half-away-from-zero yuvarlanmış satır tutarıdır (SAT-D11).
Maliyet = birim alış maliyeti (≤ 4 ondalık) × miktar; vade farkı/ek maliyet eklenmez.
Marj = NetSatış − Maliyet (kesin). Yüzde = Marj × 100 / NetSatış (kesin, yuvarlanmamış Marj'dan).
Gösterim: tutar 0,01; yüzde 0,01; ikisi de sıfırdan uzağa (negatif dahil).
NetSatış ≤ 0: tutar gösterilir, yüzde "kullanılamıyor" (NetSaleNotPositive).

| # | NetSatış | BirimMaliyet | Miktar | Marj (kesin) | Yüzde (kesin) | Tutar | Yüzde |
|---|---|---|---|---|---|---|---|
| G01 | 1000.00 | 7.1234 | 100 | 287.66 | 28.766 | 287.66 | 28.77 |
| G02 | 100.00 | 120.5 | 1 | -20.5 | -20.5 | -20.50 | -20.50 |
| G03 | 0.00 | 5 | 2 | -10 | — | -10.00 | kullanılamıyor (NetSaleNotPositive) |
| G04 | -50.00 | 1 | 1 | -51 | — | -51.00 | kullanılamıyor (NetSaleNotPositive) |
| G05 | 10.00 | 4.995 | 1 | 5.005 | 50.05 | 5.01 | 50.05 |
| G06 | 10.00 | 10.005 | 1 | -0.005 | -0.05 | -0.01 | -0.05 |
| G07 | 1000.00 | 999.95 | 1 | 0.05 | 0.005 | 0.05 | 0.01 |
| G08 | 1000.00 | 1000.05 | 1 | -0.05 | -0.005 | -0.05 | -0.01 |
| G09 | 10.00 | 3.3335 | 1 | 6.6665 | 66.665 | 6.67 | 66.67 (yuvarlanmış tutardan 66.70 OLMAZ) |
| G10 | 10.00 | 3.3325 | 1 | 6.6675 | 66.675 | 6.67 | 66.68 (66.70 OLMAZ) |
| G11 | 900.00 (fiyat 100, %10 isk., 10 kg; vade farkı yok / %5 / 2.5 TL) | 60 | 10 | 300 | 33.333… | 300.00 | 33.33 (üç durumda aynı) |
| G12 | 30.02 (10.005 × 3 = 30.015 → 30.02) | 10 | 3 | 0.02 | 0.0666… | 0.02 | 0.07 |
| G13 | 1500.00 | 12.3456 | 2.5 | 1469.136 | 97.9424 | 1469.14 | 97.94 |
| G14 | 300.00 | 0.3333 | 3 | 299.0001 | 99.6667 | 299.00 | 99.67 |
| G15 | 100.00 | 5.00010 | 1 | 94.9999 | 94.9999 | 95.00 | 95.00 |

Kullanılamıyor (tutar ve yüzde yok; sıfır/tahmini değer yazılmaz):

- G16 maliyet 5.00001 (> 4 ondalık) → CostInvalid
- G17 maliyet -0.0001 → CostInvalid
- G19 maliyet birimi ≠ sipariş birimi → UnitConversionUndecided
- G20 satış veya maliyet para birimi ≠ TL → CurrencyConversionUndecided (OPEN-INV-AVGCOST-FX)
- G21 maliyet SourceMissing/Unreadable/NoSource/ProducedItem/MixedSourceItem → ilgili neden
- G22 NetSatış yok → SalesAmountUnavailable (maliyet nedeninden önce)
- G23 NetSatış 1.00, maliyet 999999999999999.9999, miktar 10^14 → AmountOutOfRange
- G24 NetSatış 0.01, maliyet 999999999999999.9999, miktar 10^10 → yüzde taşar → AmountOutOfRange (tüm marj)

Açık (NEEDS-SINAN; bu golden politika seçmez):

- G18 birim maliyet 0: mekanik sonuç 100.00 / 100.00; SAT-D297 gereği porttan gelmemeli,
  Satış katmanında reddi kararla kapanmadı.
- Kg dışı birimde Stok tarafı çift 4-ondalık yuvarlama; bu golden yalnız aynı birimi kapsar.
- Yalnız yüzde taştığında tutarın da düşmesi bugünkü davranıştır; karar kapsamı dışında.

## Uygulama okumasının sınırları

G22 ve G24 hesaplayıcının kaynak davranışını kaydeder; tüm uygulama katmanlarının neden önceliği veya erişilebilir miktar sınırları hakkında yeni karar değildir. G18 için sıfır maliyeti Satış'ta reddetme politikası seçilmemiştir. Golden bu açıkları, döviz/birim dönüşümünü veya ilk alış maliyeti muhasebeleştirme kuralını kapatmaz.

Formülün açılması ayrı uygulama değişikliğidir; bu belgeyle `MarginGoldenAccepted` değiştirilmez. Main'e alınma, runtime kabulü, gerçek sağlayıcı, SQL, browser veya CI başarısı anlamına gelmez.
