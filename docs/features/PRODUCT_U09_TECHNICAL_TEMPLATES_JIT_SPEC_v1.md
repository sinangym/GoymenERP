# PRODUCT-U09-JIT-001 — Teknik şablonlar

> Kanonik ekran: `U-09`
> Durum: `BUSINESS_READY / FIELD_DICTIONARY_AND_UI_SPLIT_GATED`
> Ana kararlar: `DEC-PRD-002`, `DEC-PRD-006`, `DEC-PRD-009`, `DEC-PRDCT-004`, `DEC-PRDCT-008`
> Ana kabul: `AT-015`, `AT-016`

## 1. Amaç

U-09, Product family'lerine göre kullanılacak teknik alan sözlüğünü, doğrulama kurallarını ve sürümlü formula binding'lerini yönetir. Şablon; Product sınıfı, ticari rol, fiziksel lot, reçete veya işlem değildir. Amaç farklı ürün tiplerinde ortak ve izlenebilir teknik veri üretmek, geometri hesabını authoritative veriyle karıştırmamaktır.

## 2. Kapsam

- `PRD-TEMPLATE-FIELD-001` onayını bekleyen aday başlangıç family kapsamı: düz ürün, bant/rulo, sac, boru/profil, uzun ürün ve ara ürün. Bu liste exact assignable hierarchy/seed değildir.
- Sürümlü template header, section, field definition, UnitId/dimension, validation ve formula parameter binding.
- Product'ın belirli bir template version'a bağlanması ve alan değerlerini saklaması.
- Yeni template version için karşılaştırma, etki analizi ve kontrollü Product migration taslağı.
- Manuel doğrulanmış/standart ağırlık ile geometri tahmininin ayrılması.
- U-02 alanlarının seçilen template version'a göre server contract üzerinden oluşması.

## 3. Kapsam dışı

- Service kartı ve Service için template kapsamı (`PRD-SRV-001`).
- Üretim reçetesi, rota, makine parametresi, kalite ölçüm sonucu veya lot niteliği.
- Formula motoru ve exact formula/rounding/tolerance değerlerini kilitlemek.
- Custom code/plugin çalıştıran field type veya client-side expression language.
- Existing Product'ları toplu ve otomatik migrate etmek.
- `PRODUCT-A-01` ortak görselini U-08/U-09 için piksel kilitlemek.

## 4. Şablon ile diğer kavramların sınırı

| Kavram | Şablonun yapabildiği | Şablonun yapamadığı |
|---|---|---|
| Product family | Teknik alan seti ve görünüm grubu verir | Ticari/stok rolü üretmez |
| Product class/group | Bir seçim/filtre ilişkisi olabilir | Family veya rol yerine geçmez |
| Fiziksel lot/etiket | Product takip politikasına referans sağlayabilir | Lot değeri/menşe/sertifika saklamaz |
| Production | Hesap için Product teknik değerleri sağlayabilir | Reçete, rota ve fiilî üretim sonucu tutmaz |
| Formula registry | Parameter key'lerini approved FormulaId/version'a bağlar | Formülü UI içinde tanımlamaz |
| Unit registry | Alanın UnitId/boyutunu kısıtlar | Serbest metin birim oluşturmaz |

## 5. Kavramsal model

> `PROPOSED_JIT`: Aşağıdaki schema hash, section, migration draft ve ayrıntılı metadata modeli `G-PRD-TEMPLATE-01` kabulünü bekler. Bağlayıcı çekirdek immutable FieldId/key/type/dimension, requiredness'in backend doğrulaması, published version immutability ve unknown field fail-closed davranışıdır.

| Nesne | Minimum bilgi | Değişmez |
|---|---|---|
| `TechnicalTemplate` | immutable id, code, family, name | Kimlik sürümler arasında sabit |
| `TechnicalTemplateVersion` | version, canonical schema hash, status, effective range, reason, source | Approved içerik yerinde değişmez |
| `TemplateSection` | stable key, label resource, order | Görsel gruplama; domain ownership yaratmaz |
| `TemplateFieldDefinition` | immutable FieldId, stable key, type, required, UnitId/dimension, constraints | FieldId/key yeniden kullanılarak anlam değiştirilemez |
| `FormulaParameterBinding` | FormulaId/version, parameter key, source field key | Formula version pinned |
| `ProductTemplateBinding` | ProductId, template id/version, schema hash, values, revision | Eski template değişince sessiz güncellenmez |
| `ProductTechnicalSnapshot` | girilen value + UnitId, normalize/canonical value + UnitId, ConversionVersion | Yeni template/unit version geçmiş anlamı değiştirmez |
| `MigrationDraft` | from/to version, mapping, validation, impact | Onaysız apply edilemez |

Exact DB alanları ve API isimleri Codex contract'ında belirlenir.

## 6. Field definition sözleşmesi

> `PROPOSED_JIT`: Search/display/sensitivity/deprecation metadata ve exact field-type vocabulary owner/API contract'ıyla kilitlenir; bu liste production enum/DB schema değildir.

Her alan en az şu semantik metadata'yı taşımalıdır:

- template içinde değişmez, makine-okur `fieldKey`;
- yerelleştirilebilir label/help ve section/order;
- veri tipi: decimal, integer, text, boolean, date veya approved enum/reference;
- gerekiyorsa UnitId veya unit dimension;
- required/optional ve null anlamı;
- min/max, exclusive/inclusive sınır, scale/precision referansı;
- enum/reference allowlist kaynağı;
- default varsa kaynağı ve “öneri mi authoritative mi” sınıfı;
- searchable/filterable/displayable bayrakları;
- sensitive/audit sınıfı;
- formula parameter veya output binding'i;
- deprecation/replacement bilgisi.

Kurallar:

1. Label field kimliği değildir; çeviri değişimi geçmiş değer anlamını değiştirmez.
2. Bir field key aynı template lineage içinde farklı boyut/anlam için yeniden kullanılamaz.
3. Unit-bearing decimal değer UnitId'siz saklanamaz.
4. `0`, boş string ve `null` aynı anlamda kullanılamaz.
5. Zorunlu alan ve sınırlar Draft kaydına izin verebilir; Product aktivasyonu sırasında tam set zorunludur.
6. Registry'de olmayan enum/reference değeri fail-closed'dur.
7. Client yalnız server'ın yayınladığı field contract'ı render eder; kendi field sözlüğünü authoritative kopya olarak taşımaz.
8. HTML/JS expression, script, SQL veya kullanıcı girdisinden çalıştırılabilir code kabul edilmez.
9. Schema'da tanımlı olmayan field key/FieldId fail-closed reddedilir; sessiz “extra JSON” deposu olmaz.
10. Family hiyerarşisi kullanılırsa cycle ve aynı parent altında çakışan stable key approval'ı engeller.

## 7. Family başlangıç matrisi

Aşağıdaki matris alan adı değil, ilk sözlük çalışmasının kapsamıdır. Exact field key, zorunluluk ve precision `G-PRD-TEMPLATE-01` ile onaylanır.

| Family | Aday teknik boyutlar | Aday formula bağı | Özel kural |
|---|---|---|---|
| Düz ürün | kalınlık, en, boy, yoğunluk/kalite referansı | `MAT-002/003/004` ailesi | Teorik sac ağırlığı |
| Bant/rulo | kalınlık, en, teorik/fiilî kg-metre ilişkisi | uygun `MAT/CUT/CR` kayıtları | Bobin/lot fiilî değeri Product'a taşınmaz |
| Sac | kalınlık, en, boy | `MAT-002/003/004` | mm input, kg output semantiği |
| Boru/profil | kesit türü, dış ölçüler/çap, et kalınlığı, boy | `MAT-005..009`, `TUB-*` | kg + adet zorunlu; standart ağırlık geometriyi ezer |
| Uzun ürün | kesit/çap, boy, birim ağırlık kaynağı | approved MAT binding | metre ikincil olabilir |
| Ara ürün | parent/reference, process-relevant teknik alanlar | registry-approved | Reçete/rota alanı şablona konmaz |

Bu tablo exact seed değildir. Onaysız density, kerf, coating veya rounding değeri eklenemez.

### 7.1 Kaynak-backed alan eksenleri ve açık requiredness

Kalite, kaplama, kalınlık, en, boy, çap, et kalınlığı, renk ve takip politikası başlangıç teknik eksenleridir. Bunların family bazında zorunlu/opsiyonel dağılımı, controlled vocabulary sahibi, exact field key'i ve precision'ı henüz kilitli değildir. Özellikle profil kesiti opak bir `Ölçü` string'iyle authoritative tutulamaz; `shape + width + height/depth + outer diameter + wall thickness + commercial length` benzeri structured model `PRD-TEMPLATE-FIELD-001` kararıyla kesinleşmelidir.

Gerçek menşe Product attribute değildir; aynı global Product farklı menşeli lotlarla kullanılabilir. Menşe, heat, coil, MTC ve gerçek etiket/lot değerleri Stock/Traceability/Document sahipliğinde kalır. Product/template yalnız sürümlü takip politikası referansı taşır.

Takip politikası serbest metin değil controlled reference olmalıdır. Rulo/bant/profil, müşteri malı, dış fason ve heat/coil izli malzeme için kilitli fiziksel etiket/lot izlenebilirlik minimumları template ile zayıflatılamaz. Exact profil/flag matrisi `PRD-TRACK-001` altında onaylanmadan template seed'i çıkarılmaz.

## 8. Formula binding ve otorite

- Template formül metni saklamaz; FormulaId + pinned version ve parameter mapping saklar.
- Her parameter binding'in field key'i, UnitId/boyutu ve required durumu server tarafında doğrulanır.
- Formula output'u `calculated/theoretical` olarak işaretlenir; kullanıcı girişiyle aynı alanı sessizce ezmez.
- Manuel doğrulanmış veya standart master ağırlık ayrı alan/source/provenance taşır ve gelecekteki teorik kullanımlarda geometri tahminine üstün gelir.
- Fiilî hareket/ölçüm sonucu Product template alanı değildir; ilgili hareket snapshot'ında kalır.
- FormulaVersion değişirse mevcut Product binding'i eski version'da kalır. Migration taslağı farkı ve örnek etkiyi gösterir.
- Formula-bound template/binding için golden seti `Taslak`, rounding/tolerance `TBD` veya FormulaVersion belirsiz ise approval/effective kullanım yoktur. Formül kullanmayan template bu kapı nedeniyle tek başına bloklanmaz.

## 9. Yaşam döngüsü ve sürümleme

> `PROPOSED_JIT`: Exact enum, supersede/cancel ve transition adları owner/API kabulünü bekler. Bağlayıcı çekirdek published schema/version immutability, version pin ve geçmiş Product/işlem anlamının değişmemesidir.

Önerilen minimum durumlar `Draft`, `Approved`, yürürlük aralığında türetilen `Effective` ve `Retired` semantiğidir:

1. Yeni template veya değişiklik Draft version oluşturur.
2. Draft field/section/binding düzenlenebilir ve Product aktivasyonunda kullanılamaz.
3. Approval; unique key, UnitId/dimension, constraint, formula binding, golden ve compatibility validation ister.
4. Approved version immutable'dır. Düzeltme yeni version'dır.
5. Retired version yeni Product'a bağlanamaz; mevcut Product ve geçmiş snapshot için çözümlenebilir kalır.
6. Existing Product kendiliğinden latest version'a geçmez.
7. Migration önce Dry Run/impact, sonra explicit user approval ile uygulanır. AI yalnız mapping taslağı önerebilir.
8. Required alan eklenmesi, field type/unit/dimension değişimi ve formula output anlamı değişimi breaking kabul edilir.

## 10. Product binding davranışı

- U-02'de family seçimi compatible template version seçeneklerini getirir.
- Product kaydı template id/version ve field values revision'ını taşır.
- Template değiştirmek Product UUID/kod/alias veya tarihsel işlem snapshot'ını değiştirmez.
- Product aktivasyonunda template required alanları, UnitId'ler ve formula binding completeness server tarafından doğrulanır.
- Family/template değişikliği kritik bir edit'tir; impact preview ve reason zorunludur.
- Template kaldırıldığında bound Product okunmaya devam eder; yeni seçim fail-closed olur.

## 11. U-09 ekran sözleşmesi

> `PROPOSED_JIT`: Aşağıdaki liste/360/editor/migration kompozisyonu `PRODUCT-A-01` candidate split referansı ve JIT tasarımıdır; `G-PRD-UI-01` ve API contract kabul edilmeden final UI değildir. Version/FieldId görünürlüğü, server validation ve güvenli durumlar bağlayıcı çekirdektir.

### 11.1 Liste ve filtre

Template code/name, family, current effective version, status, version count, bound Product count ve validation durumu gösterilir. Arama, family/status ve “migration gereken” filtreleri desteklenir; sayılar server response'tur.

### 11.2 Template 360

- Header: immutable id/code, family, current effective version/status.
- Sections/fields ağacı: field key, type, unit/dimension, required, constraint ve provenance.
- Formula bindings: FormulaId/version, inputs, output ve golden status.
- Bound Products/impact: aggregate count ve güvenli drill-down.
- Version history/audit.

### 11.3 Version editor

- Section ve field sıralaması klavye ile yönetilebilir; drag-and-drop tek yol değildir.
- Field type değişiminde compatible/breaking uyarısı.
- Unit registry/reference picker; free-text UnitId yok.
- Formula parameter mapping ve server validation.
- Preview U-02 form görünümünü gösterir ama Product yazmaz.
- Save Draft, Validate, Compare, Request Approval ve Approve ayrı aksiyonlardır.

### 11.4 Migration preview

> `PROPOSED_JIT`: Migration command/payload, partial-success/atomicity ve transaction policy production contract'ı değildir; `G-PRD-TEMPLATE-01` ve platform idempotency kabulü gerekir.

From/to version, field mapping, eklenen/kaldırılan/breaking alanlar, etkilenmiş Product sayısı, validation failures ve örnek değer farkı gösterilir. Apply yalnız yetkili explicit onay ve idempotency key ile; kısmi başarının transaction politikası API contract'ında açık olmalıdır.

## 12. Güvenli durumlar

- Unit/formula registry unavailable: read-only history mümkün; edit/approve kapalı.
- Formula binding unresolved veya golden eksik: Draft saklanabilir, approval/effective kullanım reddedilir.
- Stale version: kullanıcı değişiklikleri korunur, compare/reload sunulur; overwrite yok.
- Bound Product varken approved field silme/anlam değiştirme: yeni breaking version + migration gerekir.
- Permission yok: liste/detail/action default-deny ve direct-ID non-disclosure.
- AI capability yok: AI control hiç mount edilmez.
- Preview hatası template kaydını bozmaz; draft form state korunur.

## 13. Yetki ve audit

V1 read/manage/approve/migrate yalnız exact `SYSTEM_ADMINISTRATOR` içindir. Exact permission string bu belgede üretilmez. Her write, approval, retirement ve migration; before/after, actor, reason, source, UTC, correlation/idempotency, template/version ve etkilenen Product sayısıyla audit edilir. Approval actor client payload'ından alınmaz.

## 14. API ve frontend sınırı

- Backend versioned field contract ve validation result döndürür.
- Frontend unknown field type'ı tahmin ederek render etmez; güvenli unsupported state gösterir.
- Server-computed preview client'ta yeniden hesaplanmaz.
- API, immutable id/version/status/actor/audit gibi alanlarda mass assignment'ı reddeder.
- Optimistic concurrency zorunludur; aynı request key + farklı payload reddedilir.

## 15. Kabul bağı

- `AT-015`: class/group/family/template ile bağımsız Product rolleri karıştırılmaz.
- `AT-016`: unit-bearing field ve formula binding doğru UnitId/version/golden ile çalışır.
- `AT-003`: kritik template/version değişikliği audit'i; `AT-005`: concurrent edit/approval conflict; `AT-006`: başarısız save sonrası form girdisinin korunması.
- `AT-045`: boru/profil template'inin adet–boy–kg golden'ı. Yeni formula/tolerance version'ının geçmiş fiilî snapshot'a etkisi U-08 altında `AT-049` ile kapanır.
- Auth/default-deny, idempotency, registry outage, migration ve genel UI vakaları local `CASE-PRD-*` kabulüdür; kanonik anlamı yoksa global `AT-*` ile zorla eşlenmez.
- Local case'ler: `CASE-PRD-U09-*`.

## 16. Implementation kapıları

Production için `G-PRD-PLATFORM-01`, `G-PRD-PERM-01`, `G-PRD-PERSIST-01`, `G-PRD-UNIT-01`, `G-PRD-TEMPLATE-01`, tracking kullanan template'larda `G-PRD-TRACK-01` ve migration atomicity/partial-success kararı gerekir. Final görsel uygulama için `G-PRD-UI-01` ayrıca zorunludur.

Ek owner kararları: `PRD-TEMPLATE-FIELD-001` exact family/field/requiredness ve structured section modelini; `PRD-TRACK-001` takip profillerini; `PRD-SRV-001` Service sınırını kilitler. Bu kararlar gelmeden prototip adlandırma, duplicate key veya default değerleri production seed'i yapılamaz.

## 17. Definition of Done

- Version immutability, binding pin, no-silent-latest, permission ve audit/idempotency gerçek DB senaryoları geçer. Owner/API migration modelini kabul ederse breaking migration/atomicity testleri de zorunludur.
- Formula parameter UnitId/dimension contract testleri ve approved golden çalışır.
- U-02 server-driven form ile U-09 field contract parity testi vardır.
- Existing Product ve tarihsel işlemler template update sonrası değişmez.
- Liste/detail/editor/preview; loading/empty/error/forbidden/stale durumlarında responsive ve klavye erişilebiliridir. Migration UI kabul edilirse aynı erişilebilirlik matrisi ona da uygulanır.
