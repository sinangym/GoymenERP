# Product Price Simulation Golden v1

## Durum

Bu veri seti U-06'nın eski-yeni fiyat önizlemesinde yalnız **exact decimal delta** ve, eski değer sıfır değilse, **exact yüzde değişim** matematiğini kanıtlayan sentetik fixture'dır. Sonuç `PREPARATION_READY_IMPLEMENTATION_NO_GO`; production fiyatlandırma sözleşmesi değildir.

## Kapsam

- İşlem: `PREVIEW_DELTA_PERCENT_ONLY`.
- Aritmetik: base-10 değerler rational olarak değerlendirilir; floating-point toleransı kullanılmaz.
- Delta: `NewAmount - OldAmount`.
- Yüzde: `(NewAmount - OldAmount) / OldAmount × 100`.
- `OldAmount = 0` olduğunda yüzde `UNDEFINED`; sonsuzluk, sıfır veya varsayılan yüzde üretilmez.
- Para ve birim kimlikleri sentetik bağlam belirtecidir; gerçek CurrencyId/UnitId değildir.

## Bilinçli olarak dışarıda

Vergi, FX dönüşümü, yuvarlama, iskonto bileşimi, miktar kademesi seçimi, minimum fiyat/marj verdict'i, permission kararı ve persistence davranışı bu fixture ile doğrulanmaz. Bu alanların hiçbiri production default'u olarak yorumlanamaz.

## Promotion descriptor

Manifestten bağımsız doğrulanan descriptor şu durumda kalır:

| Alan | Değer |
|---|---|
| Status | `BLOCKED` |
| promotionReady | `false` |
| priceCalculationPolicyVersion | `null` |
| roundingPolicyVersion | `null` |
| taxPolicyVersion | `null` |
| fxPolicyVersion | `null` |
| permissionManifestVersion | `null` |

Bütün pin'ler owner-approved sürüm kimliği almalı, blockerlar kapanmalı, fixture gerçek production policy ile yeniden üretilmeli, exact-head K3 review geçmeli ve visible shared-CI adımı nonzero propagation göstermelidir. Sadece CSV/manifesti değiştirerek promotion yapılamaz.

## Fixture özeti

| Fixture | Eski | Yeni | Delta | Exact % | Amaç |
|---|---:|---:|---:|---:|---|
| PRD-PRICE-SIM-001 | 100 | 110 | 10 | 10 | Pozitif değişim |
| PRD-PRICE-SIM-002 | 250 | 237.5 | -12.5 | -5 | Negatif değişim |
| PRD-PRICE-SIM-003 | 0 | 10 | 10 | UNDEFINED | Sıfır denominator |
| PRD-PRICE-SIM-004 | 99.99 | 99.99 | 0 | 0 | Değişiklik yok |
| PRD-PRICE-SIM-005 | 1200 | 1260 | 60 | 5 | Büyük değer |
| PRD-PRICE-SIM-006 | 0.125 | 0.150 | 0.025 | 20 | Küçük decimal |
| PRD-PRICE-SIM-007 | 1000000 | 999999.99 | -0.01 | -0.000001 | Hassas küçük düşüş |
| PRD-PRICE-SIM-008 | 7.5 | 8.25 | 0.75 | 10 | Decimal oran |

## Çalıştırılabilir kanıt

CSV ile manifest satır/sıra/değer parity'si validator tarafından exact kontrol edilir. Promotion descriptor authority dosyasında ayrıca pinlenir. Shared CI wiring bu pakette yazılmaz; `G-PRD-REMAIN-CI-01` açık kalır.

