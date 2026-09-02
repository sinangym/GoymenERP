# Product Remaining U-03/U-04/U-05/U-06/U-10 JIT Index v1

## Belge durumu

| Alan | Değer |
|---|---|
| Depo | `sinangym/GoymenERP` |
| Kaynak snapshot | `main@80c567a63da8e059ce92aa1b23dad9f4568ad2ca` |
| Snapshot tarihi | `2026-09-02` |
| Sahip | ChatGPT — iş kuralı, kabul ve sentetik golden hazırlığı |
| Kapsam | U-03/U-04 Hizmetler, U-05/U-06 Fiyatlar, U-10 Excel içe aktarma |
| Karar | `PREPARATION_READY_IMPLEMENTATION_NO_GO` |
| K3 nedeni | Para/fiyat, izin, modüller arası sözleşme, import ve veri yazma |
| Production schema/API/UI GO | **Hayır** |

Bu paket Product core PR #181'i tamamlanmış veya promotion-ready kabul etmez. #181'in exact-head review, shared-CI wiring ve bütün açık `G-PRD-*` kapıları bu paketten bağımsız olarak açık kalır.

## Otorite sırası

1. Product Epic #100 içindeki en son açık ürün sahibi kararları; grup-paylaşımlı Product sınırı için #104.
2. `docs/decisions/KARAR_SICILI.md` içindeki çelişmeyen kilitli kararlar.
3. `docs/decisions/SINAN_KARARLARI_2026-08-31.md`.
4. `docs/decisions/ACIK_KARARLAR.md`.
5. `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`.
6. `docs/acceptance/ACCEPTANCE_70.md`.
7. Kilitli görsel kaynaklar; iş kararıyla çelişirse yalnız yerleşim referansıdır.
8. Bu JIT paketi; açık noktada fail-closed olur ve yeni production gerçeği icat etmez.

Issue #100'deki `PRD-PERM-001=A`, eski “stok/planlama rolü import onaylayabilir” notunu pilot için supersede eder: Product/Service read + ilk faz write ve U-10 pilot Apply yalnız mevcut `SYSTEM_ADMINISTRATOR` rolüne açıktır; diğer roller default-deny'dır. Bu, exact permission string veya kalıcı rol matrisi değildir.

## Canonical ekran ve kaynak eşlemesi

| Canonical | Sayfa | Görsel kaynak | Mapping durumu | Sınır |
|---|---|---|---|---|
| U-03 | Hizmetler liste + detay | `PRODUCT-H-01` | `CANDIDATE_EXACT` | Stoksuz ticari hizmet |
| U-04 | Yeni/Düzenle Hizmet | `PRODUCT-H-02` + `PRODUCT-H-03` | H-02=`CANDIDATE_EXACT`; H-03=`CANDIDATE_MERGE` | Aktivasyon `SRV-SCOPE-001` kapısında |
| U-05 | Fiyatlar | `PRODUCT-PRICE-01` + `PRODUCT-PRICE-04` | PRICE-01=`CANDIDATE_EXACT`; PRICE-04=`CANDIDATE_MERGE` | Liste, zaman çizelgesi, sürüm ve geçerlilik |
| U-06 | Fiyat Simülasyonu / Toplu Güncelleme | `PRODUCT-PRICE-02` | `CANDIDATE_EXACT` | Preview → diff → approval → yeni sürüm |
| U-10 | Excel İçe Aktarma | `PRODUCT-PRICE-03` | `CANDIDATE_EXACT`, kapsam borçlu | Görsel fiyat importuna özeldir; genel Product importu sayılmaz |

### Kaynak-ID çakışma kuralları

- Eski `PRODUCT-U-03` kaynak kartı Product düzenlemeyi gösterir ve canonical U-02'ye aittir; U-03 Hizmetler için otorite değildir.
- Eski `PRODUCT-U-04` “duplicate compare” kartının U-02/R-04 eşlemesi çözülmemiştir; canonical U-04 Hizmet formuna taşınamaz.
- Eski `PRODUCT-U-05` paket standardıdır ve canonical U-07'ye aittir; U-05 Fiyatlar için kullanılamaz.
- Eski `PRODUCT-U-06` sipariş satırı seçicisidir ve S-03/P-04 içine gömülüdür; canonical U-06 fiyat simülasyonu değildir.
- `PRODUCT-PRICE-03` yalnız fiyat importu akışını gösterir. U-10'un ürün/hizmet/genel import kapsamı owner kararı olmadan genişletilmez.
- `CANDIDATE` eşleme frontend uygulama gerçeği veya route sözleşmesi değildir.

## Paket artefaktları

| Artefakt | Amaç |
|---|---|
| `PRODUCT_U03_U04_SERVICE_JIT_SPEC_v1.md` | Stoksuz hizmet kimliği, liste/detay/form ve açık scope sözleşmesi |
| `PRODUCT_U05_U06_PRICE_LIFECYCLE_SIMULATION_JIT_SPEC_v1.md` | Fiyat boyutları, lifecycle, tarihçe ve toplu simülasyon |
| `PRODUCT_U10_EXCEL_IMPORT_JIT_SPEC_v1.md` | Mapping/preview/validation/duplicate/approval/write zinciri |
| `PRODUCT_REMAINING_U03_U04_U05_U06_U10_ACCEPTANCE_v1.md` | Kabul politikası ve canonical AT eşlemesi |
| `PRODUCT_REMAINING_U03_U04_U05_U06_U10_SCENARIOS_v1.json` | 62 gapless, makine-okur vaka |
| `PRODUCT_PRICE_SIMULATION_GOLDEN_v1.*` | Sentetik ve promotion-blocked simülasyon golden verisi |
| `PRODUCT_REMAINING_JIT_AUTHORITY_v1.json` | Digest, case→AT ve descriptor pin'i |
| Validator + mutation testi | Standalone/canonical fail-closed kanıt |

## Owner kapıları

Bütün kapılar bu pakette `OPEN` kalır:

| Kapı | Sahip karar/kanıt |
|---|---|
| `SRV-SCOPE-001` | Hizmet scope, kod serisi, lifecycle, satın alma/satış kullanılabilirliği, vergi/muhasebe ve izin modeli |
| `PRD-PRICE-001` | Precedence/tie/overlap, lifecycle guard, tax/FX/tier/discount, floor-margin, rounding, maker-checker, batch ve exact Price izinleri |
| `PRD-IMPORT-001` | U-10 modu, format/locale/parse/güvenlik, mapping identity, partial-vs-atomic, fingerprint/replay/rollback, ERP-000G ve write |
| `OPEN-007` | Formül sahibi ile kesin yoğunluk/hız/kerf/kaplama/yuvarlama/tolerans değerleri |
| `G-PRD-REMAIN-BASE-01` | #181 core Product exact-head ve açık `G-PRD-*` kanıtları |
| `G-PRD-REMAIN-PLATFORM-01` | CompanyContext, auth boundary ve hata zarfı |
| `G-PRD-REMAIN-CARI-01` | Customer/customer-group referanslarının Cari kimlik ve scope kontratı; veri kopyalanmaz |
| `G-PRD-REMAIN-PERM-01` | Exact izin manifesti; rol adı veya UI görünürlüğü yeterli değildir |
| `G-PRD-REMAIN-DATA-01` | UUID/UnitId/reference ve persistence owner sözleşmesi |
| `G-PRD-REMAIN-AUDIT-01` | Global audit/origin sözleşmesi |
| `G-PRD-REMAIN-IDEMPOTENCY-01` | Global idempotency/replay sözleşmesi |
| `G-PRD-REMAIN-CONCURRENCY-01` | RowVersion/ETag ve 409 davranışı |
| `G-PRD-REMAIN-CALC-01` | Deterministik calculator, decimal, yuvarlama, vergi ve FX policy pin'leri |
| `G-PRD-REMAIN-DOC-01` | Document Vault, dosya saklama ve tarama sahibi |
| `G-PRD-REMAIN-AI-01` | AI tool/capability, audit ve öneri-only sınırı |
| `G-PRD-REMAIN-UI-01` | Route, gerçek API, durum matrisi, a11y ve real-browser kanıtı |
| `G-PRD-REMAIN-CI-01` | Exact validator ve mutation komutlarının shared runner'da görünür, nonzero-propagating adım olması |

## Pilot rol policy pin'i

| Alan | Pin |
|---|---|
| Kaynak | `github-issue:100 / PRD-PERM-001=A` |
| Product/Service read + ilk faz write | Yalnız mevcut `SYSTEM_ADMINISTRATOR` |
| U-10 pilot Apply | Yalnız mevcut `SYSTEM_ADMINISTRATOR` |
| Diğer roller | `DEFAULT_DENY` |
| Exact permission strings | `UNRESOLVED` |
| `G-PRD-REMAIN-PERM-01` | `OPEN` |

## Hazırlık ve uygulama ayrımı

Bu belgeler; Codex için backend kontrat girdisi, Claude için ekran davranışı ve reviewer için kabul kanıtıdır. Endpoint adı, DTO, tablo, migration, exact permission string, production rounding/tax/FX formülü veya route icat etmez. Owner kararları kapandıktan sonra önerilen uygulama sırası U-03/U-04 → U-05 → U-06 → U-10'dur; bu sıra dahi schema/API implementation GO değildir.
