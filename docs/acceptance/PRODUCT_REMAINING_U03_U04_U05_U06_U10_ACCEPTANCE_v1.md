# Product Remaining U-03/U-04/U-05/U-06/U-10 Acceptance v1

## 1. Karar

Paket sonucu `PREPARATION_READY_IMPLEMENTATION_NO_GO`'dur. 62 local vaka, authority dosyasındaki exact case→canonical AT pin'iyle değerlendirilir. Local PASS schema/API/UI GO, owner gate closure veya #181 promotion kanıtı değildir.

## 2. Canonical AT semantik sınırı

| Canonical AT | Exact metin | Bu pakette izinli kullanım |
|---|---|---|
| AT-001 | Kullanıcı yalnız yetkili şirket, şube ve depo kayıtlarını görür. | Yalnız cross-cutting server scope vakası |
| AT-003 | Kritik değişiklik önce/sonra, neden, kullanıcı, rol ve korelasyonla audit üretir. | Yalnız cross-cutting audit vakası |
| AT-005 | Eşzamanlı değişiklikte ikinci kullanıcıya çatışma ve fark gösterilir. | Yalnız cross-cutting stale-write vakası |
| AT-006 | Başarısız kaydetme kullanıcının form verisini kaybettirmez. | Yalnız cross-cutting save-failure vakası |
| AT-008 | Şirket kapsamı değişikliği açık formu sessizce başka şirkete taşımaz. | Yalnız cross-cutting context-switch vakası |
| AT-018 | Fiyat sürümü para, birim, KDV, kanal ve geçerlilik bağlamını korur. | Yalnız U-05/U-06 exact version/snapshot vakaları |
| AT-019 | Excel içe aktarma eşleme/önizleme/onay olmadan kayıt yazmaz. | Yalnız U-10 no-write zinciri |
| AT-020 | Aynı Excel dosyasının tekrarı mükerrer kalıcı kayıt üretmez. | Yalnız U-10 duplicate/replay zinciri |

Service vakaları module-specific canonical AT'ye bağlanmaz; yalnız scenarios JSON'daki cross-cutting seri canonical AT kullanır. Yakın anlam, ekran benzerliği veya “genel CI geçti” AT kapanışı değildir.

## 3. Seri ve kanıt matrisi

| Seri | Sayı | Ana kanıt | Canonical bağ |
|---|---:|---|---|
| X | 10 | Scope, audit, concurrency, save, context, AI ve NO_GO | AT-001/003/005/006/008 yalnız exact vakalarda |
| U03 | 10 | Hizmet listesi/detayı, stockless invariant | Local-only |
| U04 | 10 | Yeni/düzenle hizmet ve impact preview | Local-only |
| U05 | 10 | Fiyat context, lifecycle ve tarihçe | AT-018 yalnız exact vakalarda |
| U06 | 10 | Simülasyon, diff, yeni revision ve snapshot | AT-018 yalnız exact vakalarda |
| U10 | 12 | Mapping, preview, duplicate, approval/write ve unsafe-file/injection | AT-019/020 yalnız exact vakalarda |
| **Toplam** | **62** | Gapless ID ve authority parity | Exact pinned |

## 4. Çalıştırma

Standalone artefakt denetimi:

`node docs/acceptance/PRODUCT_REMAINING_JIT_VALIDATOR_v1.mjs --standalone`

Canonical kaynakla denetim:

`node docs/acceptance/PRODUCT_REMAINING_JIT_VALIDATOR_v1.mjs`

Mutation güvenlik ağı:

`node --test docs/acceptance/PRODUCT_REMAINING_JIT_VALIDATOR_MUTATION_TEST_v1.mjs`

Bu komutların yerelde geçmesi `G-PRD-REMAIN-CI-01` kapısını kapatmaz. Shared workflow içinde görünür runner step/job ve nonzero failure propagation ayrıca kanıtlanmalıdır.

## 5. Promotion yasağı

Golden verisi sentetiktir. `priceCalculationPolicyVersion`, `roundingPolicyVersion`, `taxPolicyVersion`, `fxPolicyVersion` ve `permissionManifestVersion` owner değerleri null iken descriptor `BLOCKED` kalır. Validator veya mutation testini gevşetmek promotion kanıtı değildir.

`PRD-PERM-001=A` pilot pin'i yalnız mevcut `SYSTEM_ADMINISTRATOR` rolünü Product/Service read + ilk faz write ve U-10 Apply için allowlist eder; diğer roller default-deny'dır. Price read/manage/simulate/approve bu allowlist'in dışındadır. Exact permission strings unresolved ve `G-PRD-REMAIN-PERM-01` açık kalır.

## 6. Exact vakalar

Vaka başlığı, beklenen sonuç, owner gate ve canonical AT eşlemesinin bağlayıcı makine kaynağı `PRODUCT_REMAINING_U03_U04_U05_U06_U10_SCENARIOS_v1.json`; digest ve mapping pin'i `PRODUCT_REMAINING_JIT_AUTHORITY_v1.json`'dır. Markdown ile JSON ayrışırsa validator nonzero çıkar.
