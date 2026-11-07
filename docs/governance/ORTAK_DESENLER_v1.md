# Göymen ERP — Ortak Desenler ve Beceriler v1

**Durum:** öneri (governance belgesi). Bu belge kural koymaz; `AGENTS.md`, `KARAR_SICILI.md`, tarihli
`SINAN_KARARLARI_*.md` ve v2/v3/v4 teslim belgeleri aynen geçerlidir. Çelişkide onlar üstündür.

**Amaç:** Paralel Claude şeritleri (Satış, Alış, Finans, Üretim, Kalan Modüller, AI) aynı teknik işleri tekrar tekrar
çözüyor. Bu belge (1) bu tekrar eden işleri ve kod desenlerini kanıtla listeler, (2) her biri için `.claude/skills/`
altındaki beceriyi gösterir, (3) ortak koda taşınabilecek tekrar eden **teknik** kodu sıralar.

**Temel sınır:** İş kuralları modülde kalır. Durum makinesi, onay kuralı, fiyat, formül, tolerans, eşik, risk, vergi,
stok ve üretim kuralları ortak koda taşınmaz. Ortak koda yalnız modülden bağımsız teknik iskelet gider (başlık okuma,
hash, yetki çiti, decoder ilkelleri gibi). Bir modül başka bir modülün tablosuna yazmaz; ortak kod bu kuralı
değiştirmez.

Kanıt tabanı: `origin/main` `d8a86e4` (3 Kasım 2026), `git log --since=2026-10-20` (69 commit) ve PR/Issue #66
yorumları.

## 1. Tekrar eden işler (prosedürler)

| # | İş | Kanıt (main) | Beceri |
|---|---|---|---|
| P1 | Recovery envanterini yeniden sabitleme (kaynak yüzeyi, migration sayısı/parmak izi, incelenmiş metot ve blob hash'leri) | `tools/generate_recovery_surface_inventory.py` 22 commit, `contracts/recovery/recovery-surface-inventory-v1.schema.json` 17, `tests/recovery_surface_inventory/…` 18, `tests/release_manifest/…` 17; commit gövdelerinde ≈55 "re-pin" satırı (ör. #928 `81418f8`, #921 `342fb0e`, #907 `06c34c0`) | `recovery-inventory-repin` |
| P2 | Mimari koruma testinin parmak izlerini yenileme (DbContext, store, ModuleExtensions, `Sql` çağıran migration, ilişkisel envanter dosyası) | `tests/GoymenERP.ArchitectureTests/PlatformTenantQueryFilterBypassGuardTests.cs` 19 commit — en çok değişen test dosyası | `architecture-guard-repin` |
| P3 | OpenAPI belgesini yeniden üretme ve beklenti listelerini güncelleme | `contracts/openapi/goymen-erp-v1.json` 18, `OpenApiContractGenerationTests.cs` 18 commit; "regenerate OpenAPI … after the main merge" | `openapi-regenerate` |
| P4 | EF migration + veri varsa `THROW 51xxx` ile reddeden forward-fix `Down` + gerçek SQL testi | 23 commit gövdesinde "Down", 12'sinde "THROW"; örnek `20261103012142_AddPurchaseRequestLineSource.cs` (51167) | `ef-migration-forward-fix` |
| P5 | İlişkisel test envanteri ve test adı kuralları (FQN yalnız ASCII) | `tools/relational_inventory/*.txt` 14 commit gövdesinde; `FQN_PATTERN` (`tools/verify_backend_test_groups.py`) | `test-naming-and-groups` |
| P6 | Yeni izin ve shell yeteneği kaydı, registry revizyonlarının yeniden türetilmesi | `ErpPermissions.cs` 12, `ShellCapabilityRegistryAuthorities.cs` 7 commit; örnek #907 | `permission-capability-registration` |
| P7 | `origin/main` ilerleyince birleştirip bütün sabitleri yeniden üretme | 18 commit gövdesinde "after merging origin/main / main merge" | `main-merge-resync` |
| P8 | Review imzaları, merge kapıları, #66 makbuzu, PR gövdesi "Modül bağlantıları" satırı | PR #902/#914/#928/#933/#872 review'ları; #66'da farklı biçimlerde makbuzlar | `review-signatures` |
| P9 | Karar dosyası yazımı ve `docs/decisions` hafif kontrolleri | 64 dosya `docs/decisions/`; #863 `95c7d45` sicil hash'i | `decision-docs-validation` |
| P10 | Ekran durumları, sıkı decoder, komut sonucu, Vitest kalıpları | 15 frontend modül klasörü; `StateBlock` 89 dosyada | `frontend-screen-states` |
| P11 | Kritik komut iskeleti ve modüller arası port/participant | `PurchasingService.cs`, `SalesDraftService.cs`, `LogisticsInstructionService.cs`… | `k3-command-skeleton` |

## 2. Ortak kod desenleri kataloğu

Her desen için önce okunacak örnek dosya. Yollar `src/backend/` veya `src/frontend/goymen-erp-web/src/` altındadır.

### 2.1 Backend

| Desen | Kanonik örnek | Diğer modüllerdeki karşılığı |
|---|---|---|
| Kritik komut (yetki çiti → sürüm → idempotency replay → `RequireStill` → domain → tek kayıt + attempt + audit → terminal ret kaydı) | `Modules/Purchasing/…Application/PurchasingService.cs` (`SubmitRequestAsync`, `TerminalAsync`, `RequireAsync`, `GuardedAsync`) | Satış `Modules/Sales/…Application/SalesDraftService.cs`; Lojistik `Modules/Logistics/…Application/LogisticsInstructionService.cs`; Belgeler `Modules/Documents/…Application/DocumentService.cs`; Üretim `Modules/Manufacturing/…Application/ManufacturingApiV1.cs` (sonuç nesnesi biçimi); Finans `Modules/Finance/…Infrastructure/FinanceAllocationService.cs`; Ayarlar `Modules/Settings/…Infrastructure/Persistence/Settings*Administration.cs`; Stok `Modules/Inventory/…Infrastructure/Inventory*CommandService.cs` |
| Store mutasyonu (attempt bul → aynı içerik → replay; yükle → domain → tek `SaveChanges`) | `Modules/Purchasing/…Infrastructure/Persistence/PurchasingStore.cs` (`MutateRequestAsync`) | `Modules/Sales/…Infrastructure/Persistence/SalesStore*.cs`, `Modules/Manufacturing/…Infrastructure/Persistence/ManufacturingStore.cs` |
| İstek hash'i (kanonik JSON → SHA-256) | `Modules/Purchasing/…Application/Contracts/PurchasingStoreContracts.cs` (`PurchasingRequestHash`) | `LogisticsRequestHash` (`LogisticsInstructionStoreContracts.cs`), Satış `SalesRequestContractsV1.cs` / `ISalesOrderDraftStore.cs` |
| Komut adı / problem kodu sabitleri | `PurchasingCommands` (`purchasing.purchase-request.submit.v1`), `PurchasingProblemCodesV1` (`PurchasingContractsV1.cs`) | `SalesDraftProblemCodesV1`, `LogisticsInstructionContractsV1.cs`, Settings `Contracts/V1/*V1.cs`; platform kodları yalnız `GoymenERP.Api/Errors/ErpProblemCodes.cs` |
| Endpoint metadata (tek `Idempotency-Key` ≤128, replay başlıkları, kodsuz 401/403/404/503) | `GoymenERP.Api/Endpoints/PurchasingEndpoints.cs` (`TryReadIdempotencyKey`, `ApplyReplayHeaders`, `Problem`) | `LogisticsEndpoints.cs`, `SalesEndpoints.cs`, `DocumentEndpoints.cs`, `InventoryEndpoints.cs`, `FinanceEndpoints.cs` … |
| Okuma portu (`I*ReadPortV1`: tüketici tanımlar, sahip uygular; yetki taşımaz; `Unavailable`/`NoSource`/`Available`, asla sessiz sıfır) | `Modules/Sales/…Application/Contracts/ISalesPartyRiskReadPortV1.cs` ← `Modules/Finance/…Infrastructure/FinanceSalesPartyRiskReadPortV1.cs` | `IPurchaseOpenLineReadPortV1`, `ISalesOrderDeliveryReadPortV1`, `IFinanceOfficialInvoiceImportReadPortV1`; varsayılan ret: `UnavailableOrganizationLocationOperatorEligibilityPortV1` (`TryAddScoped`) |
| Participant (`EnlistAsync`: sahip modülün DbContext'i host'un açık transaction'ına katılır; commit host'ta) | `Modules/Purchasing/…Infrastructure/Persistence/PurchaseAcceptanceParticipant.cs` + host `GoymenERP.Api/PurchasingRuntime/PurchaseGoodsReceiptHost.cs` | `Modules/Inventory/…Infrastructure/Persistence/Inventory{GoodsReceipt,DefiniteShipment,CustomerReturn,ManufacturingRealization}Participant.cs`, `Modules/Product/…Infrastructure/Persistence/Product{Draft,Import}Participant.cs` |
| Outbox | `Modules/Inventory/…Application/InventoryDefiniteShipmentOutboxV1.cs` | `InventoryCustomerReturnOutboxV1.cs`, Work `WorkDesiredStateStoreV2.cs` |
| Forward-fix migration | `Modules/Purchasing/…/Migrations/20261103012142_AddPurchaseRequestLineSource.cs` | Settings `20261102143743_AddExchangeRateRecords.cs` (51201), Üretim `20261102181335_AddManufacturingPlanLifecycle.cs` (51187/51188), Finans 51051 |
| İzin + shell yeteneği | `BuildingBlocks/…/Security/ErpPermissions.cs`, `Modules/Purchasing/…/Contracts/PurchasingPermissionManifestV1.cs`, `…/Shell/PurchasingShellCapabilityProvider.cs`, `GoymenERP.Api/Shell/ShellCapabilityRegistryAuthorities.cs` | Her modülün `*PermissionManifestV1` ve `*ShellCapabilityProvider` dosyaları |
| Değer nesneleri | `BuildingBlocks/…/Monetary/Money.cs`, `ExchangeRate.cs`, `Quantities/Quantity.cs` | modüllerdeki `QuantityAliases.cs` |

### 2.2 Testler

| Desen | Kanonik örnek |
|---|---|
| Domain | `tests/GoymenERP.Purchasing.UnitTests/PurchaseRequestWithdrawalTests.cs` |
| Servis (yetki, sürüm, replay, aynı anahtar farklı içerik) | `tests/GoymenERP.Purchasing.UnitTests/PurchasingServiceTests.RequestDecisions.cs` |
| Model + migration işlemleri | `tests/GoymenERP.Purchasing.UnitTests/PurchasingPersistenceModelTests.cs` |
| HTTP kabul | `tests/GoymenERP.Api.IntegrationTests/PurchasingHttpAcceptanceTests.cs` |
| Gerçek SQL (tek kayıt, yarış, check, Down reddi) | `tests/GoymenERP.Api.IntegrationTests/PurchasingSqlServerRelationalAcceptanceTests.RequestSource.cs` |
| Frontend DOM | `src/purchasing/PurchaseRequestSource.test.tsx`, lane harness `src/sales/testHarness.tsx` |

### 2.3 Frontend

| Desen | Kanonik örnek | Not |
|---|---|---|
| Sıkı decoder (tam anahtar kümesi, `null` = malformed) | `src/purchasing/contract.ts` | aynı ilkeller 28 dosyada kopya (bkz. §4) |
| Okuma durumu → `StateBlock` | `src/purchasing/PurchaseRequestListPage.tsx`, `src/shell/uiState.ts`, `src/shell/StateBlock.tsx` | `ERP-000K_SHELL_CONTRACT §12` durumları |
| Yetki sınırı bildirimi | `src/shell/useAuthBoundary.ts` (`capture` / `report`) | 401/403 ekran tarafından değil reducer tarafından yorumlanır |
| Komut sonucu + replay başlıkları | `src/purchasing/requestSubmissionApi.ts` | `applied` / `failed` / `unknown` |
| Bekleyen anahtar deposu | `src/purchasing/pendingRequestStore.ts` | `unknown` sonuçta aynı anahtarla yeniden dene |
| Metin dosyası | `src/purchasing/purchasingText.ts` | TSX içinde sabit Türkçe yok (TEK-03) |
| Tasarım sistemi | `src/ui/Button.tsx`, `DataTable.tsx`, `Field.tsx`, `ConflictDiff.tsx` | TEK-02 |

## 3. Beceriler

`.claude/skills/<ad>/SKILL.md`. Her beceri doğrulanmış komutları ve dosya yollarını içerir.

| Beceri | Ne zaman |
|---|---|
| `recovery-inventory-repin` | `src/backend/` veya build ayar dosyası değişti, migration eklendi, main birleştirildi; "Recovery surface inventory"/"Release manifest" kırmızı. Yardımcı: `scripts/print_recovery_actuals.py` |
| `architecture-guard-repin` | DbContext/store/ModuleExtensions/`Sql` migration/ilişkisel envanter değişti; mimari grup "Expected …, found …". Yardımcı: `scripts/guard_fingerprint.py` |
| `openapi-regenerate` | Endpoint/DTO/durum/problem kodu değişti; "OpenAPI contract drift" kırmızı |
| `ef-migration-forward-fix` | Yeni EF migration |
| `k3-command-skeleton` | Kritik komut, okuma portu veya participant yazımı/review'u |
| `permission-capability-registration` | Yeni izin adı veya shell yeteneği |
| `review-signatures` | Review, imza, merge kararı, #66 makbuzu, PR gövdesi |
| `decision-docs-validation` | `docs/decisions/**`, `docs/backlog/**`, `.github/**` değişikliği |
| `test-naming-and-groups` | Yeni test adı/sınıfı, SQL kabul testi, test projesi |
| `frontend-screen-states` | Ekran, API istemcisi, decoder, Vitest |
| `main-merge-resync` | `origin/main` ilerledi, sabit dosyalarda çakışma |

## 4. Ortak koda taşıma adayları

Kural: aday yalnız **teknik** koddur; davranış birebir korunur; taşıma kendi PR'ıdır (özellik dilimiyle karışmaz);
önce bütün kopyalar satır satır karşılaştırılır, farklı olan kopya ya korunur ya da farkı ayrıca kararlaştırılır.
Her backend taşıması recovery ve mimari koruma sabitlerini yeniden üretir. Satır sayıları yaklaşıktır.

| # | Tekrar eden kod | Nerede (kanıt) | Boyut | Önerilen hedef | K / risk |
|---|---|---|---|---|---|
| C1 | `Idempotency-Key` başlık okuma ve `MaximumIdempotencyKeyLength = 128` | **Tek bir davranış yok** (DOCS-REVIEW #946/#950), davranışa göre üç grup var: (A) başta/sonda boşluğu reddeder — Purchasing, Logistics (CustomerReturn dahil), Sales, Inventory, Finance, ProductPriceList, Documents; (B) kırpar ve kabul eder — Accounting, Manufacturing, Organization, Settings (4 uç), ProductItemType, Counterparty; (C) Product — yalnız 33–126 ASCII. Hata kodu da farklı: çoğu 400 `command_metadata_invalid`, Inventory ve ProductPriceList `validation_failed`, Finance kodsuz 400. Sabit 15 dosyada | ~14 satır × ~20 | `src/backend/GoymenERP.Api/Http/IdempotencyKeyHeader.cs` (HTTP'ye özgü; BuildingBlocks'a değil). **Yalnız davranışı ve hata kodu birebir aynı olanlar** aynı fonksiyonu paylaşır; gruplar ve kodlar korunur | K3 · orta. Davranışı ya da hata kodunu birleştirmek HTTP sözleşmesini ve saklanan anahtarı değiştirir; bu bir Sinan kararıdır, taşıma PR'ı seçmez |
| C2 | Replay yanıt başlıkları (`ApplyReplayHeaders`: `Idempotency-Replayed`, `Idempotency-Original-Correlation-Id`) | 6 endpoint dosyası (`Sales`, `Document`, `Logistics`, `Purchasing`, `Counterparty`, `Inventory`) | ~5 satır × 6 | C1 ile aynı dosya | K2 · düşük |
| C3 | Servis istisnasını ProblemDetails'e çevirme (`Problem(...)`: 401/403/404/503 kodsuz, diğerleri `code` + `errors`, replay başlığı) | `PurchasingEndpoints.cs`, `LogisticsEndpoints.cs` birebir; Satış/Belgeler/Stok benzer | ~30 satır × 4–6 | `GoymenERP.Api/Errors/ErpCommandProblem.cs` — modül istisnası küçük bir arayüzle (`Status`, `Code`, `Errors`, `IsReplay`, `OriginalCorrelationId`) | K3 (çapraz modül sözleşme yüzeyi, problem biçimi) · orta — `PlatformRequestMetadataProblemOwnershipGuardTests` ve `CodeLessProblemResponses` beklentileri korunmalı |
| C4 | Komut metadata kaydı: `PurchasingCommandMetadata`, `SalesCommandMetadata`, `LogisticsCommandMetadata`, `AccountingCommandMetadata`, `DocumentCommandMetadata`, … (çoğunda aynı 4 alan: anahtar, korelasyon, nedensellik, zaman; `DocumentCommandMetadata` 3 alan) | 9 kayıt tipi | ~1–3 satır × 9 + her endpoint'teki `Metadata(...)` | `BuildingBlocks/…/Observability/ErpCommandMetadata.cs` (ortak kayıt; `CommandMetadata` adı Organization ve Finance'ta zaten kullanıldığı için başka ad; modül tipleri geçiş için takma ad olarak kalabilir) | K3 (sözleşme tipi) · orta — `ManufacturingCommandMetadata` farklı (aktör/izin/rol taşır), ona dokunulmaz |
| C5 | Yetki çiti: `Scope` kaydı + `RequireAsync` (401/403/503 `permission_unregistered`, `RevalidatePermissionScopeAsync`, `IsCompanyWide`) + `RequireStillAsync` + `GuardedAsync` + `Matches` | `sealed record Scope(` 24 dosyada; `RequireStillAsync` 50, `GuardedAsync` 7, `Matches` 12 dosyada (Satınalma, Satış ×3, Belgeler, Muhasebe, Lojistik ×2, Ayarlar ×4, Stok, Ürün, Cari) | ~60–80 satır × 10+ | `BuildingBlocks/…/Security/ErpCommandAuthorityFence.cs`: sonucu enum/ayrık birleşim olarak döner, her modül kendi istisnasına ve problem koduna çevirir; modüle özgü alanlar (ör. Satış `IsSalesSupport`) modülde kalır | **K3 (yetki)** · yüksek — güvenlik çiti; iki tam review, mevcut servis testleri birebir geçmeli; modül modül, tek seferde değil |
| C6 | `ParseVersion` (kanonik pozitif int64 metin → 400) ve sayfa boyutu/imleç kontrolü | `PurchasingService.cs`, `LogisticsCustomerReturnService.cs`, `CounterpartyContactInformationEditor.cs` + satır içi benzerleri | ~10 satır × 3+ | `BuildingBlocks/…/Concurrency/ExpectedVersionText.cs` (yeni klasör) | K2 · düşük — hata alanı adı (`expectedVersion`) korunmalı |
| C7 | SHA-256 küçük harf hex (`Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x))).ToLowerInvariant()`) | migration dışı 60 dosya, 15 modülde | 1–3 satır × 60 | `BuildingBlocks/…/Hashing/Sha256Hex.cs` | K2 · düşük — **yalnız yardımcı taşınır; istek hash'inin girdisi (JSON alan adları `targetId`/`instructionId`, serializer ayarları) modülde kalır**, yoksa eski idempotency anahtarları replay'de `same_key_payload_mismatch` verir. Recovery'nin incelenmiş metot parmak izleri değişir |
| C8 | Participant `EnlistAsync` iskeleti (açık bağlantı + aynı transaction kontrolü, `CompanyContext`, `AuditEventCollector`, `AuditSaveChangesInterceptor`, `UseTransactionAsync`, hata halinde dispose) | 7 dosya: `PurchaseAcceptanceParticipant.cs`, 4 × `Inventory*Participant.cs`, 2 × `Product*Participant.cs` | ~30 satır × 7 | `Modules/Audit/…Infrastructure` veya `BuildingBlocks` içinde `ParticipantDbContextFactory<TContext>` | K3 (çapraz modül transaction) · orta-yüksek — kopyalar farklı: Stok `contextOwnsConnection: false` veriyor, Satınalma vermiyor; önce bu farkın etkisi ölçülmeli. Mimari koruma bu dosyaları sabitliyor Product participant'ları `AuditSaveChangesInterceptor` kullanmaz; ortak fabrika bunu seçenek olarak taşımalı. |
| C9 | Frontend decoder ilkelleri: `record`/`isRecord`, `exact`/`hasExactKeys`, `guid` (nil değil), `canonicalVersion`, `dateOnly`, `instant`, GUID/DATE/INSTANT regex'leri | `function exact(` 28 dosya, `hasExactKeys` 14, `isRecord` 14, `record` 15, `guid` 9, `instant` 5; GUID regex 17 dosya | ~40–80 satır × 20+ | `src/frontend/goymen-erp-web/src/shared/contract/primitives.ts` (+ birim testleri) | K2 · düşük-orta — saf fonksiyonlar; bazı kopyalar küçük farklılık taşıyor (ör. `instant` uzunluk sınırı, nil GUID kabulü) — farklı olanlar ya korunur ya ayrıca kararlaştırılır |
| C10 | Frontend komut sonucu sınıflandırma: `codeOf(body)`, replay başlığı tutarlılığı, `[400,401,403,404,409,415,422]` → `failed`, ≥500 → `service-unavailable` | `Idempotency-Replayed` 32 dosyada, `codeOf` 3 dosyada, `Idempotency-Key` başlığı 37 dosyada | ~25 satır × 30 | `src/shared/command/commandOutcome.ts` (`classifyCommandResponse(response, decode, expect)`) | K2 · orta — ekran başına beklenen durum ve kimlik kontrolü (`companyId`, `id`, `status`) çağıranda kalır |
| C11 | Frontend test harness'ları: `json`, `problem`, `installFetch`, `renderAt`, `requestsFor` | `installFetch` 9, `json` 9, `problem` 6, `renderAt` 5, `requestsFor` 4 harness (`accounting`, `identity`, `logistics`, `manufacturing`, `organization`, `product`, `sales`, `settings`, `cari`) — toplam 3.433 satır | ~60–120 satır × 9 | `src/test/http.ts` (yalnız test kodu) | K1 · düşük — üretim koduna dokunmaz; şerit fixture'ları (şirket kimlikleri, gövdeler) modülde kalır |
| C12 | İstek çiti (`createRequestFence`: sayfa isteklerini iptal + güncellik) | `src/purchasing/requestFence.ts` tek; 36 dosyada elle `new AbortController()` | ~30 satır | `src/shell/requestFence.ts` (shell'e taşı, diğer ekranlar kullanmaya başlar) | K1 · düşük |

**Taşınmayacaklar (iş kuralı):** domain aggregate'leri ve durum makineleri, `*ProblemCodesV1` içerikleri, izin adları,
risk/vergi/indirim/kur hesapları, tolerans ve eşikler, `*Text.ts` metinleri, okuma portlarının sözleşmeleri
(tüketici tanımlar, sahip uygular — ortak kütüphaneye konmaz).

**Önerilen sıra:** C11 → C12 → C9 → C2 → C1(a) → C6 → C7 → C10 → C4 → C3 → C8 → C5 (düşük riskten yükseğe). Her biri ayrı
PR; C1/C3/C4/C5/C8 K3'tür. Sayımlar #946/#950 review'larıyla düzeltildi. Bu belge hiçbir taşımayı başlatmaz; sıra ve sahiplik koordinatör kararıdır.

## 5. Gözlenen sapmalar (bilgi için)

- K3 PR'larında (#902, #914, #928) yeni head için `DELTA-OK` / "delta review" yazılmış. `SINAN_KARARLARI_2026-10-21.md`
  (satır 3) ve v4 §1 "K3'te delta review yoktur; her yeni head için iki tam exact-head review" der. `review-signatures`
  yazılı kuralı izler; uygulamanın kurala çekilmesi ya da kuralın değişmesi koordinatör/Sinan kararıdır.
- Issue #66 makbuzları farklı biçimlerde yazılıyor (`MERGED #…`, `Makbuz — …`, `MERGE makbuzu: …`). `review-signatures`
  AGENTS.md'nin istediği alanlarla (PR, merge SHA, iki review bağlantısı) tek satırlık biçim önerir.
- "AI araçları" PR gövdesi satırı için `main`'de kaynak belge bulunamadı; yalnız "Modül bağlantıları" satırı belgeli.

## 6. Bu belgenin kuralı

`docs/governance/**` ve `.claude/**` değişikliğidir: iki bağımsız exact-head review (`DOCS-REVIEW-1/2`), açık P0/P1
yok; CI şartı yok. `.claude/settings.json` değiştirilmez.
