# PRODUCT-UNIT-GOLDEN-001 — Ürün birim/dönüşüm golden tohumu

> Durum: `BUSINESS_ARITHMETIC_ORACLE / NOT_EXECUTABLE_FOR_PRODUCTION`
> Veri: `PRODUCT_UNIT_CONVERSION_GOLDEN_v1.csv`
> Makine zarfı: `PRODUCT_UNIT_CONVERSION_GOLDEN_MANIFEST_v1.json`
> Bağ: `U-07`, `U-08`, `U-09`, `AT-016`, `AT-045`
> Açık politika: `OPEN-007`

## 1. Amaç

Bu set, mevcut `FORMUL_KATALOGU` kimliklerini kullanarak formül zincirinin aritmetik anlamını ve snapshot beklentisini sabitler. Yeni FormulaId oluşturmaz. `FormulaVersion`, exact UnitId/precision, rounding, tolerance, gerçek yoğunluk/profil ağırlığı kaynağı ve owner onayı açık olduğu için production gate açmaz.

CSV satırları şu nedenle `Taslak`tır:

- FormulaId mevcut olsa da immutable FormulaVersion registry'de yayınlanmamıştır.
- Master'daki `AwayFromZero`, kg toplamı 3 hane ve birim kg gösterim önerileri kilitli production kararı değildir.
- `7.85 kg/dm³` yalnız sentetik fixture input'udur; Product/material master değeri üstündür.
- Gerçek profil weight golden'ı iş sahibi tarafından doğrulanmamıştır.
- Tolerance değeri ve boundary inclusivity belirsizdir.

## 2. Hesaplama ilkeleri

1. Decimal arithmetic kullanılır; binary floating point kabul kanıtı değildir.
2. Girdi değeri UnitId/birim anlamıyla birlikte verilir.
3. Ara sonuçlar erken yuvarlanmaz; raw result korunur.
4. Final rounding yalnız onaylı politika + version ile uygulanır.
5. Tolerance, rounding'den ayrı metadata ve karar kuralıdır.
6. NaN/Infinity API, UI veya DB'ye çıkamaz.
7. Theoretical sonuç fiilî stok/maliyet değerini ezemez.
8. Yeni FormulaVersion geçmiş işlem snapshot'ını yeniden hesaplamaz.

## 3. Aritmetik oracle'lar

| Test | Katalog formülü | Ham hesap | Beklenen ham sonuç | Nitelik |
|---|---|---|---|---|
| `GT-PRD-MAT-002-001` | `MAT-002` sac birim kg | `2×1000×2000×7.85/1,000,000` | `31.4 kg/adet` | Teorik sentetik fixture |
| `GT-PRD-MAT-002-002` | `MAT-002` sac birim kg | `3×1500×3000×7.85/1,000,000` | `105.975 kg/adet` | Teorik sentetik fixture |
| `GT-PRD-MAT-003-001` | `MAT-003` sac toplam kg | `31.4×10` | `314 kg` | Teorik |
| `GT-PRD-MAT-004-001` | `MAT-004` teorik adet | `314/31.4` | `10 adet` | Teorik |
| `GT-PRD-TUB-001-001` | `TUB-001` toplam adet | `20×50` | `1000 adet` | Paket ilişkisi |
| `GT-PRD-TUB-002-001` | `TUB-002` toplam metre | `1000×6000/1000` | `6000 m` | Teorik |
| `GT-PRD-TUB-004-001` | `TUB-004` toplam kg | `8.5×1000` | `8500 kg` | Verified-weight sentetik fixture |
| `GT-PRD-BAL-006-001` | `BAL-006` fiilî birim kg | `8475/1000` | `8.475 kg/adet` | Sentetik fiilî işlem fixture'ı |
| `GT-PRD-MAT-009-001` | `MAT-009` profil birim kg | `(4×40−4×2)×2×6000×7.85/1,000,000` | `14.3184 kg/adet` | Geometri tahmini |

`8.5 kg/adet` ve `7.85 kg/dm³` gerçek Product master seed'i değildir; yalnız hesap zincirini görünür kılan fixture'dır. `MAT-009` satırı ana FormulaId'dir; kare dış çevre ve etkin çevre girdilerinin hazırlanmasında katalogdaki `MAT-005` ve `MAT-008` bileşen anlamlarını kullanır.

## 4. Yapısal negatif golden vakaları

Bu vakalar exact domain error code kilitlenene kadar CSV'nin sayısal oracle satırlarına eklenmez; acceptance testlerinde semantik sonuç olarak çalışır.

| Test | Girdi | Beklenen semantik |
|---|---|---|
| `GT-PRD-STRUCT-001` | `1 kg → m`, Product-specific bridge yok | Dimension mismatch; sonuç ve kalıcı etki yok |
| `GT-PRD-STRUCT-002` | Primary'ye Effective path yok | No-path; production conversion yok |
| `GT-PRD-STRUCT-003` | Aynı context'te farklı sonuç veren iki path | Ambiguous; approval/effective resolution yok |
| `GT-PRD-STRUCT-004` | Graph cycle | Validation failure; rule approval yok |
| `GT-PRD-STRUCT-005` | Sıfır/negatif factor | Rule publish yok |
| `GT-PRD-STRUCT-006` | `MAT-004` veya `BAL-006` paydası `0` | Sonuç yok; NaN/Infinity yok; DB etkisi yok |
| `GT-PRD-STRUCT-007` | Required formula input `null` | Missing-input; sonuç yok |
| `GT-PRD-STRUCT-008` | Inactive UnitId/FormulaVersion | Yeni kullanım yok; geçmiş snapshot okunur |
| `GT-PRD-STRUCT-009` | Teorik `8500 kg`, fiilî `8475 kg` | İki değer ayrı; fiilî master factor'a auto-write olmaz |
| `GT-PRD-STRUCT-010` | V1 `8.5`, V2 `8.6`, `1000 adet` | Eski snapshot `8500`, yeni `8600`; eski değişmez |
| `GT-PRD-STRUCT-011` | Geometri boyutlarından biri veya yoğunluk `≤0` | Physical-domain validation; numeric sonuç/persistence yok |
| `GT-PRD-STRUCT-012` | Kare/dikdörtgen profilde et kalınlığı kesit geometrisini fiziksel olarak geçersiz kılar | Formula çalıştırılmadan domain validation; teorik ağırlık yok |

## 5. Rounding ve tolerance karar fixture'ları

Aşağıdaki girdiler owner kararı alındığında aynı veri setine onaylı satır olarak eklenmelidir:

| Test | Ham değer/fark | Kilitlenecek karar |
|---|---|---|
| `GT-PRD-ROUND-001` | `1.2345 kg`, hedef 3 hane | `AwayFromZero=1.235` veya `ToEven=1.234`; method owner kararı |
| `GT-PRD-ROUND-002` | Uzun zincirde yüksek precision | Ara rounding stage ve SQL decimal scale |
| `GT-PRD-TOL-001` | Fark tam tolerance sınırında | `<` veya `≤`; kabul/uyarı/blok sonucu |
| `GT-PRD-TOL-002` | Mutlak ve yüzde eşikleri farklı sonuç verir | Birleşim/öncelik kuralı |

## 6. Snapshot assertion'ı

Golden çalıştırıcı yalnız numeric output'u değil aşağıdaki envelope'u doğrulamalıdır:

```json
{
  "rawInputValuesAndUnitIds": "exact",
  "rawOutput": "decimal",
  "outputUnitId": "pinned",
  "formulaId": "catalog-id",
  "formulaVersion": "pinned",
  "parameterSources": "captured",
  "measurementKind": "theoretical-or-actual-or-structural",
  "roundingPolicyVersion": "pinned",
  "tolerancePolicyVersion": "pinned",
  "packageStandardVersion": "when-applicable",
  "calculationTraceHash": "stable"
}
```

Alan adları OpenAPI değildir; doğrulanacak bilgi içeriğini tanımlar. Pure package-count satırında `measurementKind=STRUCTURAL_COUNT_FIXTURE` semantiği kullanılır. CSV içindeki `BeklenenCiktiJSON.Nitelik`, manifest `measurementKindMapping@1.0.0` tablosundan exactly-one eşleşmeyle `expected.measurementKind` değerine çevrilir; bilinmeyen, ambiguous veya parity dışı değer validation ve promotion'ı fail-closed durdurur. Mapping girdileri `csvNitelik` sırasıyla canonical LF satırlarına çevrilip manifestteki SHA-256 digest ile doğrulanır. `packageStandardApplicable=true` olan satır production'a yükseltilirken `packageStandardVersion` zorunludur. Makine dosyasındaki `unitAlias` değerleri (`kg/dm3` gibi) yalnız ASCII fixture gösterimidir; production UnitId değildir.

Manifest `promotionSchema@1.0.0`, her required path için type, non-null, minimum eşleşme/uzunluk/kardinalite ve conditional kuralı makine-okur biçimde tanımlar. Zero-match yalnız field açıkça `minMatches=0` ilan ettiğinde geçerlidir; bu IF_PRESENT kuralı controlled-reference içermeyen case'leri kapsar, mevcut her controlled-reference eşleşmesi yine non-null approved `referenceId` taşır. Diğer zero-match, null/empty değer, type coercion, `SYNTHETIC_*` source veya unresolved approval evidence promotion'ı reddeder. `traceHashPolicy@1.0.0` her promoted case için `traceHash` değerini zorunlu `sha256:<64 lowercase hex>` yapar ve hash'i `traceHash` çıkarılmış case'in RFC 8785 JCS gösterimi üzerinden yeniden hesaplatır.

## 7. Production'a yükseltme kapısı

Bu dataset ancak şu adımlardan sonra `Onaylı` olabilir:

1. Formula registry her satır için immutable FormulaVersion yayınlar.
2. Unit registry exact UnitId, dimension, precision/scale ve inactive davranışını yayınlar.
3. Sinan/iş sahibi rounding, tolerance, density ve verified profil weight source kararlarını onaylar.
4. CSV `Onaylayan`, `FormulaVersion`, `Yuvarlama`, `Tolerans` alanları `TBD` olmaktan çıkar.
5. Companion manifest `promotionSchema@1.0.0` ile UnitId, controlled-reference ID, parameter source, output UnitId, policy version, applicable package version ve trace hash alanlarını type-safe/non-null doğrular; `SYNTHETIC_*` source'lar promotion'da reddedilir ve decimal string karşılaştırması kullanılır.
6. CSV `Nitelik` ↔ manifest `measurementKind` parity'si versioned mapping ve digest ile; her case `traceHash` değeri de RFC 8785 JCS recompute ile doğrulanır.
7. Parser/schema lint, decimal unit test, contract test ve gerçek SQL round-trip çalışır.
8. U-08 rule approval/effective resolution bu approved dataset + manifest hash'ine pinlenir.
9. `AT-016`; gerçek verified boru/profil fixture'ı da geçerse `AT-045` evidence ile kapanır.

Bu şartlar tamamlanmadan CSV değişimi production miktar veya stok etkisi oluşturamaz.
