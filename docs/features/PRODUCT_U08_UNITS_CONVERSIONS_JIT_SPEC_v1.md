# PRODUCT-U08-JIT-001 — Birimler ve dönüşümler

> Kanonik ekran: `U-08`
> Durum: `BUSINESS_READY / REGISTRY_AND_PLATFORM_GATED`
> Ana kararlar: `DEC-PRD-006`, `DEC-PRD-007`, `DEC-PRD-009`, `DEC-PRDCT-004`, `DEC-PRDCT-008`
> Ana kabul: `AT-016`

## 1. Amaç

U-08, bir Product'ın ticari ve operasyonel birim rollerini, bu birimlerden birincil stok miktarına giden sürümlü dönüşüm kurallarını ve teorik/fiilî değer ayrımını güvenli biçimde yönetir. Ekran bir hesap makinesi değildir; server tarafından yayınlanan registry, formül ve önizleme sonuçlarını yönetir ve açıklar.

## 2. Kapsam

- Birincil stok birimi ve satın alma, satış, fiyat, üretim, paket birimi rollerinin atanması.
- Sabit katsayı, sürümlü formül ve işlem anı fiilî ölçüm dönüşüm türleri.
- FormulaId/version, UnitId, effective date, rounding, tolerance, source ve theoretical/actual metadata görünürlüğü.
- Taslak doğrulama, etki/örnek hesap önizlemesi, approval/effective önkoşulları ve sürüm tarihçesi.
- Boru/profil için `kg + adet` zorunlu takip sözleşmesi; metre/paket ikincil izleme.
- Hareket snapshot tüketicileri için kavramsal contract.

## 3. Kapsam dışı

- Unit master seed değerlerini, exact sembolleri veya decimal scale'i bu belgede kilitlemek.
- Formula registry'nin motoru, endpoint isimleri veya error code string'leri.
- Stok hareketini, fiyatlandırmayı, üretim sarfını veya muhasebe yuvarlamasını hesaplamak.
- Toplu değişikliği otomatik uygulamak; AI yalnız taslak ve simülasyon oluşturabilir.
- Service birimleri ve Service kartı.

## 4. Kavramsal model

| Kavram | Zorunlu anlam | Değişmez |
|---|---|---|
| `UnitReference` | Registry'deki değişmez UnitId, sembol ve boyut | UI serbest metin UnitId oluşturmaz |
| `ProductUnitRole` | Product + rol + UnitId bağı | Aynı rolün aynı yürürlük anında tek aktif UnitId'si vardır |
| `ConversionRuleVersion` | Kaynak birimden hedef/birincil birime sürümlü kural | Onaylı sürüm yerinde değişmez |
| `FormulaBinding` | FormulaId + version + parametre kaynakları | UI formülü kopyalamaz veya yeniden çalıştırmaz |
| `ConversionPreview` | Server'ın örnek girdiye verdiği teorik sonuç ve iz | Kalıcı stok değildir |
| `MovementQuantitySnapshot` | Girilen miktar/birim + çevrilmiş birincil miktar/birim + rule/formula version | Geçmiş kural değişiminden etkilenmez |

Semantik alanlar aşağıdaki minimumu taşımalıdır; teknik isimler OpenAPI ile kesinleşir:

- Product kimliği ve optimistic-concurrency token'ı;
- rol (`stock`, `purchase`, `sales`, `price`, `production`, `package` semantiği);
- source UnitId, target UnitId ve birim boyutları;
- yöntem: `FIXED_FACTOR`, `FORMULA`, `ACTUAL_AT_TRANSACTION` semantiği;
- factor veya FormulaId/version; ikisi aynı kuralda birlikte authoritative olamaz;
- effective-from ve varsa effective-to;
- rounding mode/scale ve tolerance referansı;
- theoretical/actual sınıfı, source/provenance, status ve revision.

Bu enum/string örnekleri permission/API sabiti değildir; iş anlamını tarif eder.

## 5. Birim rolü kuralları

> `PROPOSED_JIT`: Deterministik primary conversion, version pin, theoretical/actual ayrımı ve snapshot binding'dir. Graph storage, cycle/ambiguity algoritması, reversible kural ve effective resolver ayrıntıları `G-PRD-UNIT-01` ile owner/API kabulünü bekler.

1. Her aktif Product'ın tek bir birincil stok birimi bulunur ve v1 için bu birim `kg` semantiğindedir.
2. Boru/profil ailesinde birincil `kg` yanında `adet` takibi zorunludur. Metre ve paket ikincil olabilir.
3. Satın alma, satış, fiyat, üretim ve paket rolleri birbirinden farklı UnitId kullanabilir.
4. Bir rolün UnitId'si değiştirilirken mevcut işlemler güncellenmez; yeni effective version oluşturulur.
5. Transactional birim, etkin tarihte birincil stok birimine deterministik biçimde çevrilemiyorsa Product aktive edilemez ve ilgili nihai işlemde seçilemez.
6. Birim boyutu ile dönüşüm türü uyumlu olmalıdır. Aynı boyuttaki doğrusal dönüşüm sabit katsayı olabilir; `adet → kg` veya `m → kg` gibi ürün geometrisi/ağırlığı gerektiren dönüşüm formül ya da fiilî ölçüm ister.
7. Sıfır/negatif factor, NaN/Infinity, tanımsız UnitId ve bitişi başlangıcından önce olan effective range reddedilir.
8. Aynı kaynak, hedef, Product ve yürürlük bağlamında birden fazla Effective sonuç yolu oluşamaz. Ambiguous path approval/effective resolution fail-closed'dur.
9. Cycle içeren graph approve edilemez. UI cycle'ı hesaplayıp “düzeltmiş” gibi göstermez; server validation sonucunu sunar.
10. Ters dönüşüm ancak registry kuralı açıkça reversible ise kullanılabilir. UI katsayının tersini sessizce üretmez.

## 6. Dönüşüm yöntemleri

### 6.1 Sabit katsayı

- Factor sıfırdan büyük ve registry precision sınırları içinde olmalıdır.
- Kaynak, hedef, factor, effective version ve rounding metadata audit edilir.
- Exact metrik dönüşümler dahil değer registry'den gelir; bu JIT belgesi seed değeri ilan etmez.

### 6.2 Sürümlü formül

- FormulaId ve FormulaVersion zorunludur; “latest” gibi kayan bağ production snapshot'ında kullanılamaz.
- Girdi parametreleri UnitId ile birlikte doğrulanır.
- Formül theoretical/actual niteliği, veri kaynağı, rounding, tolerance ve golden set ile yayınlanır.
- Formula değişimi eski version'ı yeniden yazmaz. Yeni version'ın effective range'i ve etki önizlemesi gerekir.
- `MAT-*`, `TUB-*`, `BAL-*` kayıtları formül kataloğundaki kimliklerle referanslanır; yeni FormulaId bu ekranda oluşturulmaz.

### 6.3 İşlem anı fiilî ölçüm

- Master düzeyinde sabit sonuç üretmez; hareket anında yetkili tüketici modülün ölçümünden doğar.
- Girilen miktar/birim, fiilî primary miktar/birim, ölçüm kaynağı, tarih ve actor snapshot'ta saklanır.
- Fiilî sonuç Product'ın teorik birim ağırlığını veya eski hareketleri sessizce güncellemez.
- Master tahmini ile fiilî değer arasındaki fark tolerance dışındaysa uyarı/görev doğabilir; exact eşik `OPEN-007` çözülmeden üretim etkisi yaratmaz.

## 7. Otorite ve ağırlık önceliği

Bir ağırlık dönüşümünde anlam önceliği:

1. İlgili hareketin fiilî ölçüm snapshot'ı — yalnız o hareket için authoritative.
2. Product üzerindeki manuel doğrulanmış veya standart master ağırlık — gelecekteki teorik hesap için.
3. Sürümlü geometri/formül hesabı — yardımcı teorik tahmin.

Alt sıradaki kaynak üst sıradakini ezemez. UI “teorik” ve “fiilî” etiketlerini renk dışında metin/ikon ile de ayırmalıdır.

## 8. Yaşam döngüsü ve sürümleme

> `PROPOSED_JIT`: Exact enum ve transition isimleri registry/API contract'ı değildir. Bağlayıcı çekirdek onaylı version immutability, effective-date pin ve geçmiş snapshot'ın yeniden hesaplanmamasıdır.

Önerilen minimum iş durumları `Draft`, `Approved`, yürürlük aralığında türetilen `Effective` ve `Retired` semantiğidir:

- Yeni kural Draft başlar.
- Draft düzenlenebilir; stock etkisi yoktur.
- Approved olabilmek için UnitId/graph/formula/golden/effective date/permission doğrulamaları geçer; yalnız yürürlük aralığında `Effective` olur.
- Approved içerik yerinde değiştirilemez. Değişiklik yeni Draft version oluşturur.
- Retired version yeni işlemde seçilmez; tarihsel snapshot çözümlemesi için kalır.
- Geçmiş effective aralığı değiştiren backdate, etkilenen işlemleri yeniden hesaplamaz ve owner-approved migration olmadan uygulanamaz.

Toplu değişiklikte AI veya import yalnız Draft change-set + etki simülasyonu üretir. Kullanıcı onayı olmadan approve/apply yapılamaz.

## 9. U-08 ekran sözleşmesi

> `PROPOSED_JIT`: Aşağıdaki bölüm/kolon/editor kompozisyonu `PRODUCT-A-01` candidate split referansından türetilmiştir; `G-PRD-UI-01` ve API contract kabul edilmeden piksel veya exact payload kilidi değildir. Server-authoritative sonuç, UnitId/version görünürlüğü ve güvenli durumlar bağlayıcı çekirdektir.

### 9.1 Üst alan

- Product kimliği/kodu/adı ve global lifecycle durumu, read-only bağlam.
- Aktif unit-role completeness özeti.
- Taslak değişiklik, validation ve yürürlük uyarıları.
- Yetkili kullanıcı için “Yeni taslak sürüm”; capability varsa ayrı AI öneri girişi.

### 9.2 Birim rolleri bölümü

Her rol için UnitId/sembol, yürürlük, kaynak dönüşüm, primary erişilebilirliği ve validation durumu gösterilir. Sınıf/grup rol üretmez; rol ataması bağımsızdır.

### 9.3 Dönüşüm matrisi

Minimum kolonlar: source unit, target unit, method, factor veya FormulaId/version özeti, theoretical/actual, effective range, status, validation, revision. Görsel matrix yönü açıkça oklarla/etiketle belirtilir; yalnız renge dayanılmaz.

### 9.4 Kural düzenleyici

- Registry kontrollü UnitId seçimleri.
- Yönteme göre koşullu factor/formula/actual alanları.
- Parameter mapping, provenance, effective date, rounding/tolerance readout.
- Server validation ve etki önizlemesi.
- Draft kaydetme ile approval ayrı aksiyonlardır.

### 9.5 Önizleme ve iz

Kullanıcı örnek miktar girer; numeric preview ancak exact pinned FormulaVersion ile çalışır. Non-production/sandbox version açıkça non-authoritative etiketlenir. FormulaVersion/rounding/tolerance `TBD` ise yalnız structural validation yapılır, sayı üretilmez. Server sonuçla birlikte kullanılan rule version, FormulaId/version, input/output UnitId, rounding ve intermediate trace'in güvenli özetini döndürür. Önizleme “stok işlendi” algısı yaratmaz ve persistence yan etkisi olmaz.

### 9.6 Tarihçe

Version, status, effective range, actor, reason, source ve değişiklik özeti bulunur. Audit before/after kullanıcı tarafından düzenlenemez.

## 10. Güvenli durumlar

- Unit registry veya formula registry erişilemiyorsa mevcut veri read-only gösterilebilir; create/approve kapalıdır.
- Golden eksik ya da `Taslak` ise formula-bound rule approve/effective yapılamaz.
- Stale revision kaydında kullanıcının girişi korunur, server sürümüyle fark gösterilir; otomatik overwrite yoktur.
- Permission yoksa ekran/aksiyon default-deny; direct URL ile veri sızdırılmaz.
- Product inactive ise yeni operasyonel kullanım global olarak kapalıdır. `SYSTEM_ADMINISTRATOR` reactivation eyleminin varlığı kilitlidir; exact precondition/impact/in-flight davranışı `PRD-LC-001` ve platform kapıları altında fail-closed kalır. U-08 kendiliğinden ek lifecycle yasağı veya izni üretmez.
- AI capability kapalıysa panel, buton, placeholder ve çağrı mount edilmez.

## 11. Yetki, şirket ve güvenlik

- V1 read/manage/approve eylemleri yalnız exact `SYSTEM_ADMINISTRATOR` rolüne verilir.
- UI permission kararı son güvenlik sınırı değildir; server her isteği doğrular.
- Product ve unit-role bağı group-shared'dır. İstemciden Product'a CompanyId enjekte edilemez.
- Bir hareketin company context'i tüketici modül tarafından server-side çözülür; dönüşüm snapshot'ı bu bağlama eklenir.
- API mass-assignment ile status, revision, actor, approval, formula version veya audit alanı kabul etmez.

## 12. Audit ve idempotency

Her write/approval için ProductId, rule/version kimliği, before/after, actor, UTC time, reason, correlation/request id ve kullanılan CompanyContext (varsa tüketici bağlamı) kaydedilir. Global Product conversion-rule mutation/replay scope'u `PROPOSED_JIT / G-PRD-PERSIST-01` altındadır: exact command key, authorization/origin binding ve cross-origin modeli platform contract'ında kabul edilene kadar persistent command/replay fail-closed kalır; mevcut CompanyId-scoped key, sahte owner CompanyId veya `Guid.Empty/all` kullanılmaz. Kabul edilen model tek stable outcome, güncel authorization ile replay ve non-disclosure kanıtı vermelidir. Preview read-only'dir fakat hassas/ağır işlem telemetry'si tutulabilir.

## 13. Kavramsal tüketici contract'ı

Her stok etkili kayıt en az şunları taşımalıdır:

```json
{
  "enteredQuantity": "decimal",
  "enteredUnitId": "immutable-unit-id",
  "primaryQuantity": "decimal",
  "primaryUnitId": "kg-unit-id",
  "conversionRuleVersionId": "immutable-version-id-or-null",
  "formulaId": "registry-id-or-null",
  "formulaVersion": "pinned-version-or-null",
  "measurementKind": "theoretical-or-actual",
  "roundingPolicyId": "approved-policy-id",
  "calculatedAtUtc": "server-time"
}
```

Bu JSON alan adları OpenAPI değildir; zorunlu bilgi içeriğini gösterir. Movement snapshot'ta `measurementKind` yalnız theoretical/actual iş anlamını taşır. Golden manifest'teki `STRUCTURAL_COUNT_FIXTURE` test sınıfıdır; hareket snapshot enum'u veya fiilî stok niteliği değildir. Exact isim ve nullability Codex contract PR'ında kilitlenir.

## 14. Kabul bağı

- `AT-016`: unit roles, deterministic conversion, version pin, theoretical/actual, rounding/tolerance ve movement snapshot.
- `AT-003`: kritik rule/version değişikliği audit'i; `AT-005`: concurrent rule/version conflict; `AT-006`: başarısız save sonrası form girdisinin korunması.
- `AT-045`: boru/profil adet–boy–kg golden'ı; `AT-049`: yeni formula/tolerance version'ının geçmiş fiilî snapshot'ı değiştirmemesi.
- Permission/default-deny, idempotency scope, registry outage ve genel UI vakaları local `CASE-PRD-*` kabulüdür; kanonik anlamı yoksa global `AT-*` ile zorla eşlenmez.
- Local case'ler: acceptance belgesindeki `CASE-PRD-U08-*`.

## 15. Implementation kapıları

U-08 production rule approval/effectiveness için `G-PRD-PLATFORM-01`, `G-PRD-PERM-01`, `G-PRD-PERSIST-01`, `G-PRD-UNIT-01` zorunludur. Piksel-kilitli frontend için ayrıca `G-PRD-UI-01` gerekir. Bu gate'ler geçilmemişken mock hesap sonucu production davranışı gibi sunulamaz.

## 16. Definition of Done

- Tek/çift boyut ve formula-bound dönüşümler unit + contract + gerçek DB integration testleriyle kanıtlanır.
- Deterministik çözüm, no-ambiguity, incompatible dimension, zero/negative, stale, permission ve unavailable registry negatif testleri geçer. Owner/API graph modelini kabul ederse cycle/path testleri de zorunludur.
- Golden fixture onaylı FormulaVersion/rounding/tolerance ile executable olur.
- Hareket snapshot'ı eski kuralı korur; yeni version geçmişi değiştirmez.
- U-08 bütün ortak ekran durumlarında responsive ve klavye erişilebiliridir.
