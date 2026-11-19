using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GoymenERP.ArchitectureTests;

public sealed partial class PlatformTenantQueryFilterBypassGuardTests
{
    private const string AllowedSeederPath =
        "src/backend/Modules/Organization/"
        + "GoymenERP.Modules.Organization.Infrastructure/Seeding/OrganizationDataSeeder.cs";

    private const int ExpectedSeederBypassCount = 4;
    private const string BackendRootPath = "src/backend";
    private const string ExcludedCounterpartyPrefix =
        "src/backend/Modules/Counterparty/";
    private const string CounterpartyCompositionRootContractPath =
        "src/backend/Modules/Counterparty/COUNTERPARTY_MODULE_ENTRYPOINTS.json";
    private const string CounterpartyContractSourcePath =
        "docs/features/CARI_MODULE_ENTRYPOINT_CONTRACT_v1.md";
    private const string ApiProjectPath =
        "src/backend/GoymenERP.Api/GoymenERP.Api.csproj";
    private const string ManufacturingApplicationProjectPath =
        "src/backend/Modules/Manufacturing/"
        + "GoymenERP.Modules.Manufacturing.Application/"
        + "GoymenERP.Modules.Manufacturing.Application.csproj";
    private const string PdfProjectPath =
        "src/backend/DocumentGeneration/GoymenERP.DocumentGeneration.Pdf/"
        + "GoymenERP.DocumentGeneration.Pdf.csproj";
    private static readonly HashSet<string> ReviewedPdfContentPaths =
        new(StringComparer.Ordinal)
        {
            "Assets\\Fonts\\NotoSans-Regular.ttf",
            "Assets\\Fonts\\OFL-1.1.txt",
            "Assets\\Fonts\\PROVENANCE.md",
            "Assets\\Licenses\\PDFsharp-MIT.txt",
            "Assets\\Licenses\\PDFsharp-6.2.4-PROVENANCE.md",
            "THIRD_PARTY_NOTICES.md"
        };
    private const string OpenApiGenerationModePath =
        "src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/OpenApi/"
        + "OpenApiDocumentGenerationMode.cs";
    private const string OpenApiStartupPath =
        "src/backend/GoymenERP.Api/Program.cs";
    // Re-fingerprinted for MFG-UI (#323) on top of FIN-UI-1a (#325), BROWSER-CI a2 part 2 (#330), SALES-API
    // (#321) and PRD-UNIT-1 (#335): the staged Finance and Manufacturing shell providers registered before the
    // shell authority is chosen (C+O+P+S+F+M row), beside #330's one call to
    // C01E2eFixtureWriterActivation.Configure and #335's one singleton registration of Settings' unit-identity
    // catalog. Re-fingerprinted for INV-OPEN-1 part 2: two scoped host ports of the opening command
    // (Organization's location module read, Product's stock eligibility). No OpenAPI-generation, network or
    // hosted-service surface of the startup source changed otherwise.
    // Re-fingerprinted for LOG-4a: the Logistics module (the Cari customer port, AddLogisticsModule, its endpoints
    // and initializer after Organization) and its staged shell provider before the authority is chosen.
    // Re-fingerprinted for PUR-4a: the Purchasing module (the Cari supplier port, AddPurchasingModule, its endpoints
    // and initializer after Organization) and its staged shell provider before the authority is chosen.
    // Re-fingerprinted for ACC-V0a: the Accounting module (AddAccountingModule, its endpoints and initializer after
    // Organization); no shell provider (ACC-V0b).
    // Re-fingerprinted for FIN-T05: the two scoped host registrations of the record period rule (the Accounting
    // period-status port and the occurredOn rule) placed before AddFinanceServices, and then TWICE MORE for
    // COMMENT-ONLY edits beside them. The comment edits changed no registration, no generation mode, no network
    // and no hosted-service surface — and they moved this fingerprint anyway, because a content hash has no
    // opinion about whether a change is semantic. Any edit to a pinned file, comment included, is re-pinned in
    // the same push.
    // Re-fingerprinted for the #478 forward onto main c287128f: Program.cs is the clean union of the reviewed
    // PUR-HOST composition (#478, b66b1cca…) and main's reviewed LOG-HOST/#474-#485 composition (e6a57d52…).
    // Re-fingerprinted for R19 (#483): one added host line, builder.Services.AddInventoryLabelGenealogy(), and its
    // comment (588121e3… -> ab4aa1be…); no other startup change.
    // Re-fingerprinted for AT-037: one added host line, builder.Services.AddInventoryStockCounts(), and its comment
    // (ab4aa1be… -> 1609e7b4…); no other startup change.
    // Re-fingerprinted for SY-01/SY-02: one added using and one added host line registering
    // IdentityShellCapabilityProvider before the shell authority is chosen, with its comment (1609e7b4… -> 50c5acf2…);
    // no other startup change.
    // Re-fingerprinted for the ST-02 scrap write-off: one added host line, builder.Services.AddInventoryStockScraps(),
    // and its comment (50c5acf2… -> da93806b…); no other startup change.
    // Re-fingerprinted for AT-002 on top of it: one added using (GoymenERP.Api.Search), builder.Services.AddGlobalSearch()
    // with its comment, and app.MapGlobalSearchEndpoints() (da93806b… -> 869f1682…); no other startup change.
    // Re-fingerprinted for ERP-000G / AT-065: two added usings (GoymenERP.Api.DocumentsRuntime,
    // GoymenERP.Modules.Documents.Infrastructure), builder.Services.AddDocumentsRuntime(...) with its comment,
    // app.MapDocumentEndpoints() and the Development/Testing initializer after Accounting's
    // (869f1682… -> abe26a09…); no OpenAPI-generation, network or hosted-service surface changed.
    // Re-fingerprinted once more for AT-065: only the comment above AddDocumentsRuntime changed, to say that
    // production uses the own-server store and Defender when both are configured and answers 503 otherwise
    // (abe26a09… -> c027dce9…); no code line changed.
    // Re-fingerprinted for customer-owned Inventory opening: one scoped registration binds Inventory's
    // owner-account seam to Cari's existing authorized source; no network or hosted-service surface changed.
    // Re-fingerprinted for ST-04 inventory movement publication: the reviewed Inventory cursor seam is registered
    // from configuration; no key is generated and no network or hosted-service surface is added.
    // Re-fingerprinted for the Sales-Cari bounded display adapter: scoped in-process DI only; no network or hosted service.
    // Re-fingerprinted for D-05: one scoped in-process approval-read adapter/query registration and one endpoint map;
    // no OpenAPI-generation, network or hosted-service surface was added.
    // Re-fingerprinted for D-05 phase A correction: the detail schema removes the serializer-generated $type
    // discriminator because route kind owns dispatch; no network or hosted-service behavior changes.
    // Re-fingerprinted for the D-05 Sales adapter: one scoped in-process owner adapter is added beside Finance;
    // no permission, network, hosted-service or production-activation surface changes.
    // Re-fingerprinted for C01 request evidence activation (#199): one Testing-only recorder creation and
    // conditional singleton registration; no OpenAPI-generation, network or hosted-service surface changes.
    // SETTINGS–MFG-POLICY-01 adds one in-process Settings module registration and its Development/Testing
    // initializer. It adds no endpoint, hosted service or build-time network surface.
    // PUR-P05 adds one in-process record-bound Cari display host; no network or hosted-service surface.
    // D05 Inventory approvals add two scoped in-process owner readers and two Work adapters; no permission,
    // network, hosted-service or production-activation surface changes.
    // GOY-UX-TEXT-FILTER-01 adds one scoped in-process Product read adapter used only to evaluate
    // version-bound stock filters before public cursor pagination; it adds no network, hosted-service or
    // production-activation surface.
    // ORG-LOCATION-OPERATOR-API-01B adds one scoped in-process Counterparty-to-Organization eligibility
    // adapter after both modules are registered; it adds no network, hosted-service or production-activation surface.
    // M01.3-AUTH-SESSION-CONTRACT-01 adds one scoped in-process authentication-session binding resolver;
    // it adds no OpenAPI-generation, network or hosted-service surface.
    // I01.2 adds two scoped in-process transfer outgoing-document adapters; it adds no OpenAPI-generation,
    // network or hosted-service surface.
    // MOD-03 slice 1 adds one scoped in-process Settings Sales policy port registration (the approved
    // provider); it adds no OpenAPI-generation, network, hosted-service or production-activation surface.
    // SAT-D18 follow-up adds one scoped in-process operation-aware Settings candidate reader registration and
    // passes it to the Sales reference read; no OpenAPI-generation, network or hosted-service surface changes.
    // MOD-08 adds one scoped in-process Sales-to-Identity user display adapter beside the Sales-Cari display host,
    // with its comment (4cf4cba9… -> cae283bd…); it adds no OpenAPI-generation, network or hosted-service surface.
    // SAT-D27 adds the reference reads' alternative request/quote write names and the sales-owner eligibility name to the
    // existing SalesReferenceReadBindings singleton, passes the optional ISalesOwnerCandidateSourceV1 to the Sales reference
    // read factory, and registers one scoped in-process SalesOwnerCandidateHost, with comments (cae283bd… -> 2eae99cc…), then
    // a COMMENT-ONLY edit saying the sales-owner picker opens on the order create name alone (2eae99cc… -> 13f02f10…); no
    // OpenAPI-generation, network or hosted-service surface changes.
    // SAT-D61…D70 (main) maps the Settings limit routes and registers the central adapters and the shell provider;
    // SAT-D25 / SAT-D39 (main) register one scoped Settings quote policy provider; SAT-D102 registers one scoped
    // Settings e-Fatura scenario port (ISettingsEInvoiceScenarioPolicyProviderV1 -> ApprovedSettingsPrerequisiteProvider).
    // Comments / scoped in-process registrations only; no OpenAPI-generation, network or hosted-service surface changes.
    private const string ReviewedOpenApiStartupFingerprint =
        // MOD-03 (EK19) maps the tax master and product item type routes and registers one scoped in-process Settings product
        // item type adapter, with comments; no OpenAPI-generation, network or hosted-service surface changes.
        // Üretim U1-a maps the Settings Manufacturing parameter / tolerance profile routes; no OpenAPI-generation, network or
        // hosted-service surface changes.
        // Purchasing A0 (FZL-03, main) rewrites two Purchasing comments only (eligibility port is registered; order screens
        // no longer "staged"); merged with U1-a (f52999f9…/f46fcab5… -> 722a6d6d…); no registration, generation, network or
        // hosted-service change.
        // DEC-FIN-010 / SAT-D169 maps the Settings exchange-rate routes; no registration, generation, network or hosted-service
        // change (the TCMB source is composed inside AddSettingsModule and is off by default) (722a6d6d… -> d219b1f5…).
        // S5 API-36 (Fatura kesinlestirme): maps the finalize and re-drive routes and registers the host-side Sales finance admission adapter (DI only);
        // no generation, network or hosted-service change (d219b1f5… -> f4d42b16…).
        // Üretim U3-a (URT-D21): maps the plan approval/cancel allocation host registration (DI only); no generation, network or hosted-service change (f4d42b16… -> 069c2674…).
        // Üretim U2-b (API-16, SAT-D219): registers the two host-side Manufacturing/Sales order-line adapters (DI only); no
        // generation, network or hosted-service change (a1562dc1… -> a5bb99f0…).
        // Üretim U5-b1 (API-36, SAT-D175): registers the two host-side subcontract service order / binding adapters (DI only);
        // no generation, network or hosted-service change (a5bb99f0… -> 096c3e0b…).
        // F-021 / B-24b: registers the Work module and the ERP-000F outbox dispatcher (off by default, hosted service skipped in OpenAPI generation) and initializes Work in Dev/Testing; no network surface change; re-fingerprinted after merging main (096c3e0b… -> ee696d73…).
        // AI-2a (ADR-SEC-001), after the main merge: also registers AddSecurityEventsModule and runs
        // InitializeSecurityEventsModuleAsync with the other module initializers (a5bb99f0… -> 015f177e…).
        // Main merge after #1146: the AI-2a registration (015f177e…) and the U5-b1 and F-021 / B-24b registrations
        // (ee696d73…) are all present; re-measured on the merged Program.cs, neither parent's value (-> 3a7d166d…).
        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
        // Re-fingerprinted for tren-1 wave 2 trial integration: the team registrations combined, including AI-3's AddAiRuntime (off unless Ai:Enabled);
        // the network-isolation surface needs the K3 review of AI-3 (5653c485... -> 6c36f610...).
        // Re-fingerprinted for tren-1 wave 3 (integration): AddErpSecretStore (E8 secret store reader, no value configured), the ALC-S16 batch host registration and the two Settings policy endpoint maps (KIM-F1) combined; no generation or hosted-service change (6c36f610… -> f11de9cd…).
        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): stok merge: the Product similarity facts host and the Purchasing shortage supply host registrations and the kg/m table endpoint map; no generation or hosted-service change (f11de9cd… -> f66beec7…).
        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (f66beec7… -> 65d7e684…).
        // Re-fingerprinted for tren-1 W6: committed in-process owner registrations and endpoint maps; final host integration must re-pin its exact Program source (65d7e684... -> 1fa4780c...).
        // Tren-1 W6: owner bridges and scoped DI only; source fingerprint re-derived.
        // Re-fingerprinted for tren-1 W7: in-process owner registrations; activation flags unchanged; K3 review pending.
        "28827f9d097dea14b231e056e097bb06a51ce8d71818a57c9a6e974ffa0d4e14";
    private const string ReviewedOpenApiGenerationModeFingerprint =
        "74ff9d4e8033732b06b8b6639699ce97781a967b3a3f658da4c8571d7573da50";
    private const string ReviewedProductManufacturingTechnicalRegistrationPath =
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
        + "Persistence/ProductManufacturingTechnicalRuntimeRegistration.cs";
    private const string ReviewedProductManufacturingTechnicalRegistrationFingerprint =
        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): B-09 (stok): the V2 snapshot port behind the same runtime predicate plus the dimension read port, with its fail-closed 503 default (b9621bcc… -> a577fc64…).
        "a577fc646d75adf8bd58fe3cfa2a3b2df8625bb3b696f8570ec917c4d351854d";
    private const string ReviewedIdentityReconcilerPath =
        "src/backend/Modules/Identity/"
        + "GoymenERP.Modules.Identity.Infrastructure/Seeding/"
        + "SystemRolePermissionReconciler.cs";
    private const string ReviewedSqlServerRcsiReadinessProbePath =
        "src/backend/BuildingBlocks/GoymenERP.BuildingBlocks.SqlServer/"
        + "SqlServerRcsiReadiness.cs";
    private const string ReviewedSqlServerRcsiToolProgramPath =
        "tools/GoymenERP.SqlServerRcsiReadiness/Program.cs";
    private const string ReviewedSqlServerRcsiWrapperPath =
        "tools/GoymenERP.SqlServerRcsiReadiness/"
        + "Invoke-GoymenERP.SqlServerRcsiReadiness.ps1";
    private const string ReviewedSqlServerRcsiWrapperFingerprint =
        "45598900747141d62bce9b983b43ffb26eef123a1a2a49d1c72e6645e2990d7e";
    private const string ReviewedSqlServerRcsiToolRootPath =
        "tools/GoymenERP.SqlServerRcsiReadiness";
    private const string ReviewedSqlServerRcsiToolProjectPath =
        "tools/GoymenERP.SqlServerRcsiReadiness/"
        + "GoymenERP.SqlServerRcsiReadiness.csproj";
    private const string ReviewedSqlServerRcsiToolProjectFingerprint =
        "bbfb2841dbb968a34c65e9da6acabc67e810660fd77647416421e54deccbe478";
    private const string SolutionPath = "GoymenERP.slnx";
    private const string SqlServerRcsiToolTestsProjectPath =
        "tests/GoymenERP.SqlServerRcsiReadiness.Tests/"
        + "GoymenERP.SqlServerRcsiReadiness.Tests.csproj";
    private const string ReviewedSqlServerRcsiToolTestsProjectFingerprint =
        "0a5d40cdf5b24b687eb6acbf6f96d4b70d050a24e9268498aa6a12007bb9e525";
    private const string SqlServerRcsiIntegrationProjectPath =
        "tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj";
    // Re-fingerprinted for #391: two linked Compile items (the browser-suite runner's BrowserSuiteHost.cs
    // and its closed seed-output validator, both for BrowserSuiteHostEnvelopeTests), after the module SQL
    // acceptance ProjectReferences. Publish target and discovery contract unchanged.
    // Re-fingerprinted for MOD-07: the PdfPig test package (the sales order PDF's text is read back from its bytes, as
    // the PDF component's own tests do). Publish target and discovery contract unchanged.
    private const string ReviewedSqlServerRcsiIntegrationProjectFingerprint =
        "82a6bb89fa2bacd24c30c92fd71f3927fbfff42c2d640630d3b76d1b7babbd05";
    private const string SqlServerRcsiRelationalAcceptancePath =
        "tests/GoymenERP.Api.IntegrationTests/"
        + "SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.cs";
    private const string SqlServerRcsiVerifierPath =
        "tools/verify_rcsi_relational_acceptance.py";
    // Re-fingerprinted for the per-module relational inventory: the broad test list moved out of the
    // verifier into tools/relational_inventory/<module>.txt (pinned below, one entry per file); the
    // verifier loads it fail-closed, checks every test's executions in the TRX and prints one line per
    // module. The move kept main's exact set and totals (146 / 153 at #326); the counts now come from the
    // files, so adding a test changes only its module file and that file's pin.
    // Re-fingerprinted for LOG-4a: the Logistics module joins INVENTORY_MODULES and the class prefixes
    // (tools/relational_inventory/logistics.txt, pinned below).
    // Re-fingerprinted for PUR-4a: the Purchasing module joins INVENTORY_MODULES and the class prefixes
    // (tools/relational_inventory/purchasing.txt, pinned below).
    // Re-fingerprinted for the broad suite's wall-clock limit 1200 s -> 2400 s (user decision 2026-09-29): only
    // the subprocess timeout changes; the exact inventory, every listed test and the 0-skip rule are unchanged.
    // Re-fingerprinted for CI-HIZ: SHARD_MODULES moves logistics from shard C to shard B (wall-clock balance only);
    // the exact inventory, every listed test and the 0-skip rule are unchanged.
    // W8 SQL diagnostics: bounded allowlisted FQN/outcome/duration progress survives execution timeout.
    // The single 2400-second deadline, exact discovery/theory multiplicity, skip0 and TRX/proof gates remain.
    // Re-fingerprinted from the house producer; independent exact-head K3 review is still pending.
    // W8 failure diagnosis: whole-theory method batches retain one global 2400s deadline,
    // strict result/identity/union checks and skip0; only a complete passing union writes proof.
    // Process/static lifetime changes require independent K3 review; no workflow or runtime bypass added.
    // W8: complete exact failed batches continue only for diagnostics; the first native failure
    // stays fatal and prevents proof. Infrastructure failures still stop; one global deadline remains.
    // W8: normal adapter Warning RunInfo metadata occurs in completed runs as well.
    // W8 diagnostic collection: RunInfo is not a veto once an exact complete Failed TRX is proved.
    // This branch always retains native failure and cannot write proof; passing acceptance is unchanged.
    // User decision 2026-11-18: one broad SQL shard budget is 10800s; each native process stays capped at 2400s.
    // No renewal, retries, inventory changes, skips or partial proof; independent exact-head K3 review is pending.
    private const string ReviewedSqlServerRcsiVerifierFingerprint =
        "43af48e91901fb24b77b7cec50bcc3738b6b8e501f3fd591e1bf78a1fcc0f73e";
    private const string SqlServerRcsiVerifierTestsPath =
        "tests/rcsi_relational_acceptance_verifier/"
        + "test_verify_rcsi_relational_acceptance.py";
    // Re-fingerprinted for the per-module inventory: count literals replaced by totals computed from
    // the files, every listed test mandatory, loader and TRX rules tested, the synthetic run's verdict
    // captured instead of printed.
    // Re-fingerprinted for LOG-4a: a Logistics class maps to its own module.
    // Re-fingerprinted for PUR-4a: a Purchasing class maps to its own module.
    // Re-fingerprinted for Work desired-state admission: the mandatory-set test now pins the three
    // canonical admission/snapshot SQL cases already present in work.txt; verifier behavior is unchanged.
    // Re-fingerprinted for B-24: the mandatory Work set now also pins the three outbox dispatch facts.
    // Re-fingerprinted for tren-1 wave 1: the mandatory Work set also pins the inbox dedup fact already in work.txt.
    // W8: the exact-set test also requires all four real notification SQL facts (586176b32);
    // verifier discovery, inventory loading, TRX and skip rules are unchanged. Python suite: 48/48.
    // W8: synthetic broad/shard fixtures now emit numbered, identity-bound batch TRX files;
    // negative coverage retains native failure, missing/duplicate/skip rejection and no partial proof.
    private const string ReviewedSqlServerRcsiVerifierTestsFingerprint =
        "f81256442c584997842457d45f57e383ee6af10c13dbd4a22c7e23c4bf8d4162";
    // The broad relational inventory, one file per module (see RelationalInventoryDirectory). Each
    // pin is its own paragraph so that two modules re-pinning their own files change lines that are
    // not adjacent and combine cleanly; a file's owner is its module, the pin line is central.
    private const string RelationalInventoryDirectory = "tools/relational_inventory";
    private static readonly IReadOnlyDictionary<string, string> ReviewedRelationalInventoryFingerprints =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // accounting
            // Fresh authority: +1 real-SQL post-read refusal acceptance fact.
            // Re-fingerprinted for B-04a: +3 posting account mapping SQL facts (one save, race, Down guard)
            // (2865c0fb… -> 327c5913…).
            // Re-fingerprinted for B-04a review P2-1: +1 cross-company mapping SQL fact (327c5913… -> 077b19a0…).
            // Re-fingerprinted for B-04b: +4 source document admission SQL facts (077b19a0… -> af80f704…).
            ["tools/relational_inventory/accounting.txt"] =
                "40d6d292a4aaac6cfeac68ae3cbf933c9c0bf5b868514c18d204d776d86693be",

            // counterparty
            // AT-009: +2 real-SQL fresh-authority interleavings (duplicate read and same-key winner replay).
            // Sales party display source: +1 bounded, company-scoped and fresh-authority batch SQL fact.
            // Record-bound Sales projection: +1 company/actor-fenced, single-query and non-disclosing SQL fact.
            // Record-bound Purchasing projection: +1 company/actor-fenced, single-query and non-disclosing SQL fact.
            // Contact information fresh authority: +5 late-revocation, replay, no-op and winner SQL facts.
            // GOY-UX-TEXT-FILTER-01: +1 same-address-row, pre-pagination address-filter SQL fact.
            // Location operator eligibility: +1 exact-party, supplier-only, company/actor-fenced read-only SQL fact.
            // SAT-D57 customer group: +5 list lifecycle, account assignment/port, company fence, 403 and rollback facts.
            // SAT-D102/SAT-D119 e-belge profile: +10 VKN-shared status lifecycle, cross-company sharing without the
            // checker, VKN change (profile-bound fence, ABA 409), existing-party isolation + port, concurrent first creation,
            // per-company Idempotency-Key, Settings-listed override/effective scenario, no-VKN and DB checks, 403 and
            // rollback facts (d8e2b013… -> 7b1a22af…). Review round 3: +2 facts — another company's audit carries
            // neither the checker nor the checking company, and a passive account's commands are a stored 422
            // (7b1a22af… -> 53063d1f…).
            // Re-fingerprinted for tren-1 wave 2 (trial integration): +8 facts, ordinal-sorted (e31d1c5b… -> 01f5d3ee…).
            ["tools/relational_inventory/counterparty.txt"] =
                // Re-fingerprinted for tren-1 wave 1: +4 facts, ordinal-sorted (53063d1f… -> e31d1c5b…).
                // Re-fingerprinted for tren-1 wave 3 (integration): +13 facts, ordinal-sorted (01f5d3ee… -> 02d8add9…).
                // Re-fingerprinted for tren-1 W5 (integration): +1 fact(s) from the W5 team merges; value from guard_fingerprint.py (02d8add9… -> 0f10d031…).
                "4332c931905c65c91d596623854c40ded037433ab9e75e6250cb15a64f26cee7",

            // finance
            // DEC-FIN-014: +3 POS collection SQL facts (b18c13d9… -> 7af8ecf1…).
            // DEC-FIN-016: +6 customer cheque / note SQL facts (7af8ecf1… -> de54339e…).
            // Party ledger: +2 SQL facts (de54339e… -> 84295fb2…).
            // Official invoice approval: +3 checker list/detail/approve/reopen and migration rollback SQL facts.
            // F-03 allocation read: +1 company/fresh-authority list/detail/reopen SQL fact.
            // D-05 approval inbox: the existing pending-approval fact is renamed and strengthened to public HTTP/DI.
            // FI04: +2 approved-source open-item read facts (real HTTP authority/redaction and SQL keyset/no-write).
            // SAT-D18: +1 Sales-only GSP/GIN TRY profile keeps the bank-account path closed, kept ordinal-sorted.
            // MOD-01 / SAT-D52: +2 Sales risk-port facts (unallocated payments deducted, reversed ones never; a refunded advance
            // no longer deducted — review A P2-1), ordinal-sorted (cb46a836… -> 7dc420a3…).
            // MOD-15 / FNS-D7: +1 party-ledger direction fact (payable credit row, CK_OpenItems_Direction, receivable-only
            // risk), ordinal-sorted (7dc420a3… -> cf9cecf6…).
            // MOD-15 review B P3: +1 direction-migration fact on a populated store, ordinal-sorted (cf9cecf6… -> c7ce4f0f…).
            // MOD-04 F2a: +6 invoice admission facts, ordinal-sorted (c7ce4f0f… -> 4575e229…).
            // F2b: +4 invoice cancellation facts, ordinal-sorted (4575e229… -> c2205fa2…).
            // F3a / API-20 +7 facts, then F2b follow-up +3 facts (admission after cancellation, late fence and race, status-only Down), ordinal-sorted (c2205fa2… -> bfc11064…).
            // F2b K3 takip: +1 not-understood-code fact, ordinal-sorted (33dcc442… -> 19001373…).
            // #1006 takip: +1 multi-allocation cancellation fact, ordinal-sorted (19001373… -> 7634c7da…).
            // FNS-D16 scale rule: +1 allocation scale refusal fact, ordinal-sorted (7634c7da… -> cbae4f24…).
            // #1070 P3: +1 receipt Create scale refusal fact, ordinal-sorted (cbae4f24… -> 36413356…).
            // API-23: +3 payment reversal facts, ordinal-sorted (36413356… -> 4684446d…).
            // API-23 review A: +4 facts (allocation/approval race, double approval, reused key and lost grant, HTTP
            // company scope), ordinal-sorted (4684446d… -> 96fd63e4…).
            // API-30: +4 cash-account master facts, ordinal-sorted (36413356… -> b4e424b4…).
            // API-30 K3 review: +8 cash-account facts (replay/conflict, recorded refusals, rejection audit, races,
            // currency re-check), ordinal-sorted (b4e424b4… -> 0a36a1d0…).
            // API-30 K3 review B P2-1: +1 cash-account fact (authority withdrawn during the Settings read or just
            // before the save), ordinal-sorted (0a36a1d0… -> b93d6306…).
            // API-30 passivation/reactivation (#66, 13 November 2026): +8 cash-account facts (two-person change flow,
            // balance seam, database checks, company, replay/stale, permission revoked, race, Down guard),
            // ordinal-sorted (b93d6306… -> dbfc6f19…).
            // Stack merge of API-23 (#1095) and API-30 (#1109): both fact sets are present; recomputed over the
            // union, ordinal-sorted (96fd63e4… + dbfc6f19… -> e549e2b4…).
            // F-005 D1: +3 official sales invoice facts (maker≠approver, CK refusals, Down guard), ordinal-sorted (36413356… -> 41ad9daf…).
            // F-005 D1 review: +3 facts (approval race, same-key record race, another company), ordinal-sorted (41ad9daf… -> d9eae78e…).
            // #66 6860586175 (PDF first): +4 facts (reservation consumed once + FK, owner read/other company, same PDF
            // content per company, same-content race) and the Down-guard fact renamed to the reservation, ordinal-sorted
            // (d9eae78e… -> e823c44f…).
            // FNS-D39: +2 facts (number/ETTN/PDF reuse after reject or withdraw while two live records still collide,
            // the reject/withdraw Down guard), ordinal-sorted (e823c44f… -> cff641d7…).
            // Stack merge of API-23 + API-30 (#1095, #1109) and F-005 D1 (#1102): all three fact sets are present;
            // recomputed over the union, ordinal-sorted (e549e2b4… + cff641d7… -> bfdc3f20…).
            // Re-fingerprinted for tren-1 wave 2 (trial integration): +20/-2 lines, ordinal-sorted (50273987… -> 09b1b156…).
            ["tools/relational_inventory/finance.txt"] =
                // Re-fingerprinted for tren-1 wave 1: +15 facts, ordinal-sorted (bfdc3f20… -> 50273987…).
                // Re-fingerprinted for tren-1 wave 3 (integration): +12/-2 lines, ordinal-sorted (09b1b156… -> eb0d35db…).
                // Re-fingerprinted for tren-1 W5 (integration): +7 fact(s) from the W5 team merges; value from guard_fingerprint.py (eb0d35db… -> 1b47e9a3…).
                // Re-fingerprinted for tren-1 W7: source facts kept ordinal-sorted; SQL execution NOT_RUN.
                "3ed95909038b2f1363defb4e6791ab793cfc5645cae26c84ee7c0a23d5802cac",

            // identity
            // SY-01/SY-02: +2 IdentityAccessViewSqlServerRelationalAcceptanceTests facts (b159330c… -> a521143a…).
            // BLG-03: +1 IdentityAuditReadFreshAuthoritySqlServerRelationalAcceptanceTests fact (a521143a… -> 9a5ea313…).
            // MOD-08: +1 IdentityUserDirectorySqlServerRelationalAcceptanceTests fact (9a5ea313… -> b2ba31aa…).
            // SAT-D27 (1): +1 sales-owner picker holder-filter SQL fact, ordinal-sorted (b2ba31aa… -> 12e280b9…).
            // ID-1: +5 IdentityLocalCredentialSqlServerRelationalAcceptanceTests facts, ordinal-sorted (12e280b9… -> aa5edfd7…).
            // Re-fingerprinted for tren-1 wave 2 (trial integration): +6 facts, ordinal-sorted (aa5edfd7… -> cb7cf51d…).
            ["tools/relational_inventory/identity.txt"] =
                // Tren-1 W6: +3 source facts, ordinal-sorted; SQL execution NOT_RUN.
                // Re-fingerprinted for tren-1 W7: source facts kept ordinal-sorted; SQL execution NOT_RUN.
                "fc9ef7ef249cd79b2556f96ce6f95f30f1019c18ad995273a1753f909d6fb986",

            // inventory
            // AT-037: +3 stock-count SQL facts (d709faf1… -> 4e783c62…).
            // DEC-SAL-005: +1 allocation-blocks-cancellation SQL fact (4e783c62… -> 953d329a…).
            // INV-AL-1: +8 allocation-change SQL facts (953d329a… -> 1ca5b9e1…).
            // ST-02 scrap write-off: +5 stock-scrap SQL facts (1ca5b9e1… -> bd676b05…).
            // R20 transfer lifecycle: +3 transfer SQL facts (bd676b05… -> c74360d3…).
            // Product-version provenance rollback: +1 isolated no-loss SQL fact (c444f707… -> d9cb549b…).
            // Customer-owned opening: +2 exact account-context and fail-closed authority SQL facts.
            // Inventory stock-position read: +1 actual bounded filter/classification SQL fact;
            // every listed case remains mandatory and zero-skip.
            // Inventory location display and ST-01 Product-authority capability: +2 reviewed Inventory SQL facts.
            // ST-01 owner/custodian read: +1 exact-party, read-without-side-effect SQL fact.
            // D05 Inventory approvals: +3 pending stock-count/scrap read and fail-closed SQL facts.
            // GOY-UX-TEXT-FILTER-01: +1 typed-filter pre-pagination and cursor-resume SQL fact.
            // SAT-D12 force-close: +1 active-allocation-blocks SQL fact, kept ordinal-sorted.
            // Re-fingerprinted for tren-1 wave 2 (trial integration): +9 facts, ordinal-sorted (f9b96739… -> b9a25cb6…).
            ["tools/relational_inventory/inventory.txt"] =
                // Re-fingerprinted for B-01: six plan-input allocation SQL acceptance tests added to the inventory.
                // SAT-D262..D267: +4 average-cost ledger/port SQL facts (c005163c... -> 0b766916...), kept ordinal-sorted.
                // B-03: +6 Manufacturing genealogy/V2 output-class SQL facts (ca832f2d... -> eecee421...), kept ordinal-sorted.
                // SAT-D318: +2 input-issue physical-stock shortfall SQL facts (free and plan-allocated issue), kept
                // ordinal-sorted (ca832f2d... -> a830a228...).
                // Re-measured on the combined inventory after merging main 6f1ff559: B-03 (eecee421...) and SAT-D318
                // (a830a228...) combined, +8 facts in total; neither parent's value.
                // Re-fingerprinted for tren-1 wave 1: +3 facts and one renamed fact, ordinal-sorted (d7b1ef8c… -> f9b96739…).
                // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): +18 facts, ordinal-sorted (b9a25cb6… -> 86ec6a12…).
                // Re-fingerprinted for tren-1 W5 (integration): +6 fact(s) from the W5 team merges; value from guard_fingerprint.py (86ec6a12… -> 5d0beeaf…).
                // Tren-1 W6: +4 source facts, ordinal-sorted; SQL execution NOT_RUN.
                // Re-fingerprinted for tren-1 W7: source facts kept ordinal-sorted; SQL execution NOT_RUN.
                "294081a7b96e47e34edefea0f7129613db78dedcc955a3a5eb5377b808d86e27",

            // logistics
            // AT-053: +4 weighbridge SQL facts (81c1ff0b… -> 264898d7…).
            // AT-025: +3 operations work list SQL facts (264898d7… -> e410a445…).
            // AT-055: +4 dispatch-note draft SQL facts (e410a445… -> 010fcbd6…).
            // AT-055 lifecycle (#518, user decision 2026-09-30): +6 approve/cancel/e-İrsaliye/numbering SQL facts (010fcbd6… -> c5969829…).
            // #527: +3 cross-instruction Sales line-limit atomicity facts (c5969829… -> fc08c53d…).
            // I01.2: +3 transfer outgoing-document schema/idempotency/version SQL facts (459c5cf8… -> e8a7ea83…).
            // SAT-D103 / SAT-D111 (K3-REVIEW-A P1-1 b): +1 shipment-time Sales cap fact (e8a7ea83… -> 576e58f6…).
            // F-020: +3 loading-instruction cancel SQL facts (576e58f6... -> 991fa70f...).
            // F-020 review fixes: +9 cancel SQL facts (absent line, released limits, weighing row kept, three races, route grant and
            // company refusal) and one renamed fact, kept ordinal-sorted (991fa70f... -> 15a22a9f...).
            // RCSI deadlock-victim read closure: +3 SQL facts (the aggregate instructed and loaded SUM reads, one transfer
            // outgoing-document Serializable read), kept ordinal-sorted (15a22a9f... -> 82e1c19f...).
            ["tools/relational_inventory/logistics.txt"] =
                // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): +5 facts, ordinal-sorted (82e1c19f… -> 3d8ec0f2…).
                // Re-fingerprinted for tren-1 W5 (integration): token change since the wave-4 merge (stale pin), same fact count; value from guard_fingerprint.py (3d8ec0f2… -> 2b3edb43…).
                "9d500d95b5d17d665e504053a94284763957018144eb75ee950fbd74f8035451",

            // manufacturing
            // MFG close: owner-added closure/tolerance SQL facts extend the exact Manufacturing discovery inventory.
            // MFG technical snapshot: +4 migration/constraint SQL facts (38590e95... -> b1f5f069...).
            // Customer-owned input context: replace one obsolete refusal with three exact persistence/replay facts.
            // Slitting draft spec: +1 public API SQL fact (e6aa3795... -> 286281c0...).
            // Settings Manufacturing tolerance registry: +1 company/constraint/provider SQL fact.
            // Settings tolerance serialization: +3 valid/invalid predecessor and empty round-trip SQL facts.
            // M01.2: +1 real-SQL lossless cut-line preview/save/replay/revise/reopen fact.
            ["tools/relational_inventory/manufacturing.txt"] =
                // Üretim U1-b: +1 ManufacturingApiSqlServerRelationalAcceptanceTests fact (parameter snapshot at approval and
                // catalog tolerance at close) (e03d7c7b… -> 7f38855d…).
                // Üretim U2-a: +2 ManufacturingApiSqlServerRelationalAcceptanceTests facts (rejection, line speed, one order,
                // cancellation; slitting approval with net width, line speed and label) and +3
                // ManufacturingSqlServerRelationalAcceptanceTests facts (rejection replay, lifecycle migration, store rules)
                // (7f38855d… -> f5bf9d89…).
                // Üretim U2-b1: +1 ManufacturingApiSqlServerRelationalAcceptanceTests fact (process spec save, revision,
                // approval capture) and +1 ManufacturingSqlServerRelationalAcceptanceTests fact (process spec migration)
                // (f5bf9d89… -> e5d404de…). U2-b1 review: +1 ManufacturingApiSqlServerRelationalAcceptanceTests fact
                // (process spec preview, oversized value, idempotency key) (e5d404de… -> 1a881db5…).
                // Üretim U2-d: +2 ManufacturingSqlServerRelationalAcceptanceTests facts (preparer refusal and revision,
                // legacy preparer and preparer migration) (1a881db5… -> ab2bcb33…). U2-d review: the migration fact
                // renamed (Down refusal 51190) and +1 ManufacturingApiSqlServerRelationalAcceptanceTests fact (the
                // preparer's 422 over HTTP) (ab2bcb33… -> 0df4c0f3…).
                // Üretim U2-b (API-16): +2 ManufacturingSqlServerRelationalAcceptanceTests facts (sales order link store
                // rules, link migration Down 51261) (b30fa7d7… -> 1b1e7223…).
                // TAKIP-U3a P3-2: +1 ManufacturingApiSqlServerRelationalAcceptanceTests fact (approval and closure without
                // the allocation port are code-less 503s; the same key succeeds once it is back) (1b1e7223… -> f6ebea91…).
                // Üretim U5-b1 (API-36): +1 ManufacturingSqlServerRelationalAcceptanceTests fact (Sales's binding read of
                // linked, not cancelled plans) (1b1e7223… -> d3ffa8f8…).
                // Main merge after #1104: both facts present (re-derived from the merged inventory) (-> 85d84d52…).
                // URT-D41/URT-D42: +2 ManufacturingApiSqlServerRelationalAcceptanceTests facts (output above input on a
                // parent roll refuses the close with no tolerance and no approval path; the read-only report of earlier
                // closes with output above input) (85d84d52… -> 15ad9659…).
                // Üretim U2-b follow-up: +1 supply read fact (revised plan read at its current revision kg) and +3
                // ManufacturingApiSqlServerRelationalAcceptanceTests facts (host link/replay/unlink, half link and Sales
                // refusals, grant lost during the Sales read) (1b1e7223… -> a4032147…).
                // Main merge after #1104: both fact sets present (re-derived from the merged inventory) (-> 8a43892e…).
                // Main merge after #1146: the U2-b follow-up facts (8a43892e…) and the U5-b1 and URT-D41/URT-D42 facts
                // (15ad9659…) are all present; re-derived from the merged inventory, neither parent's value
                // (-> e17f2323…).
                // Re-fingerprinted for tren-1 wave 1: +4 facts, ordinal-sorted (e17f2323… -> d968a25e…).
                // Re-fingerprinted for tren-1 wave 2 trial integration: the Manufacturing team's facts added, ordinal-sorted (d968a25e... -> d6273307...).
                // Re-fingerprinted for tren-1 wave 3 (integration): +11 facts, ordinal-sorted (d6273307… -> bb328fa5…).
                // Re-fingerprinted for tren-1 W5 (integration): token change since the wave-4 merge (stale pin), same fact count; value from guard_fingerprint.py (bb328fa5… -> 884e8c98…).
                // Tren-1 W6: +7 source facts, ordinal-sorted; SQL execution NOT_RUN.
                // Re-fingerprinted for tren-1 W7: source facts kept ordinal-sorted; SQL execution NOT_RUN.
                "3d94c4988bb02aba1ed11b6d50fb09e755bc8815766ec1ebfa9c9fdc028545f1",

            // organization
            // ORG master edit: +2 OrganizationMasterDataSqlServerRelationalAcceptanceTests facts (6b504dac… -> 3bad6ad8…).
            // Location display reader: +1 real-SQL tenant/display identity fact.
            // ORG-LOCATION-OPERATOR-CRUD-01A: +1 real-SQL persistence/concurrency/destination-read fact.
            // ORG-LOCATION-OPERATOR-API-01B: +1 real-SQL host-adapter/API/read/replay fact.
            // Re-fingerprinted for tren-1 wave 2 (trial integration): +2 facts, ordinal-sorted (c6fb2c8a… -> bbb2421d…).
            ["tools/relational_inventory/organization.txt"] =
                // Re-fingerprinted for tren-1 wave 1: +4 facts, ordinal-sorted (c2609fdb… -> c6fb2c8a…).
                "8e8010a34087d63ced220e0cbd8df6e9c5b12c162b443bf2ecf12bcc53505976",

            // platform
            // AT-002: +2 GlobalSearchSqlServerRelationalAcceptanceTests facts (5bc60202… -> a5df5046…).
            // ERP-000G / AT-065: +8 DocumentVaultSqlServerRelationalAcceptanceTests facts (4ed7462b… -> e57500e0…).
            // Inventory material-label exact-code global search: +1 reviewed Platform SQL fact, kept ordinal-sorted.
            // SAT-D61…D70: +2 Settings discount limit SQL facts (groups/limits lifecycle; multi-company binding)
            // (b384d836… -> 765b00e2…).
            // SAT-D78 / SAT-D84: +2 Settings discount limit SQL facts (future-dated records; withdraw/correct)
            // (765b00e2… -> e68b1182…).
            // MOD-03 (EK19): +2 Settings tax master SQL facts (tax master lifecycle; product item type → line tax port)
            // (e68b1182… -> 9c53f777…).
            // SAT-D149/D158 (EK20/EK21): +2 Settings tax master SQL facts (buyer situation list; item type move to the shared
            // card) (9c53f777… -> 534be0f1…).
            // DEC-FIN-010 / SAT-D54/D55/D169…D172: +2 Settings exchange-rate SQL facts (record lifecycle; disabled sources and
            // empty-store rollback) (534be0f1… -> a90275a7…).
            ["tools/relational_inventory/platform.txt"] =
                // Üretim U1-a: +3 Settings manufacturing profile SQL facts (parameter lifecycle; tolerance publication; migration
                // round trip) (534be0f1… -> 026f3c54…).
                // Üretim U1-b: +1 Settings manufacturing profile SQL fact (tolerance line scope, catalog fallback, supersession)
                // (026f3c54… -> 69a1024a…).
                // DEC-FIN-010 / SAT-D54/D55/D169…D172: +2 Settings exchange-rate SQL facts (record lifecycle; disabled sources and
                // empty-store rollback) (69a1024a… -> 84945670…).
                // F-022: +2 readiness SQL facts (Settings/Documents history drift; price-list drift only while the
                // Product runtime is on) (84945670… -> 912a850f…).
                // AI-2a (ADR-SEC-001), after the main merge: +4 security event SQL facts (company isolation and hash;
                // append-only trigger and checks; Down refusal; empty Down/Up round trip) (912a850f… -> 70eab799…).
                // AI-2a hardening and AI-2b, after the AI-2a merge: +2 security event SQL facts (duplicate EventId
                // retry/conflict; code-list checks and their migration round trip) and +1 read fact (own-company rows
                // only; the read is recorded as security.events.read) (70eab799… -> fe173b17…).
                // Re-fingerprinted for tren-1 wave 1: +2 facts, ordinal-sorted (fe173b17… -> c34f45fc…).
                // Re-fingerprinted for tren-1 wave 2 trial integration: the team facts combined, ordinal-sorted (c34f45fc... -> a75473ad...).
                // Re-fingerprinted for tren-1 wave 3 (integration): +8 facts, ordinal-sorted (a75473ad… -> 71017138…).
                // Re-fingerprinted for tren-1 W5 (integration): +2 fact(s) from the W5 team merges; value from guard_fingerprint.py (71017138… -> 19a6d7c9…).
                // Tren-1 W6: +4 source facts, ordinal-sorted; SQL execution NOT_RUN.
                // Re-fingerprinted for tren-1 W7: source facts kept ordinal-sorted; SQL execution NOT_RUN.
                "7bab7a6470dab14afe1ba73fe66449786dc1266ee23b37550e4a94e7c580b09c",

            // product
            // DEC-CAR-003: +4 ProductDeactivationSqlServerRelationalAcceptanceTests facts (d7dbec7f… -> ddeead62…).
            // DEC-PRD-010: +5 ProductDuplicateCandidatesSqlServerRelationalAcceptanceTests facts (ddeead62… -> d434c2b8…).
            // Q16 / AT-020: +3 Product import API SQL facts (d434c2b8… -> 9958c91f…).
            // Product 4A: +3 initial technical-definition release/rollback SQL facts.
            // PRODUCT-TECH-FAMILY-02: replace one renamed runtime fact and add two cut-sheet/plate facts.
            ["tools/relational_inventory/product.txt"] =
                // Re-fingerprinted for tren-1 wave 1: +1 facts, ordinal-sorted (100c0a2f… -> 1f399639…).
                // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): +3 facts, ordinal-sorted (1f399639… -> ea3dcfbe…).
                // Re-fingerprinted for tren-1 W5 (integration): +4 fact(s) from the W5 team merges; value from guard_fingerprint.py (ea3dcfbe… -> 392d7170…).
                // Tren-1 W6: +1 source facts, ordinal-sorted; SQL execution NOT_RUN.
                "da06f16d25bda67c8b21f0641e62cc4b3040b2b5036fc36174f29c0f5954f196",

            // purchasing
            // Purchasing supplier filter: +1 company-scoped cursor-paging SQL fact, kept ordinal-sorted.
            // A1-1 (plan API-01): +5 request approval/rejection SQL facts (one save + maker/decision checks, bound approved
            // order + rejected request's draft order, rejection vs binding approval race, company isolation, migration
            // backfill + Down refusal), kept ordinal-sorted (23decb33… -> 27a9dd4f…).
            // A1-1b (plan API-01b; ALS-D46): +4 request withdrawal SQL facts (one save + only-the-submitter + withdrawal
            // check, bound approved order vs draft order, company isolation, Down refusal + binary maker-checker), kept
            // ordinal-sorted (27a9dd4f… -> 88f2c7a5…). ALS-D47 (creator or submitter withdraws): +1 fact for creator ≠
            // submitter, a third party and a legacy request, and the one-save fact renamed for it (88f2c7a5… -> 37e3e2bd…).
            // A1-2 (plan API-02): +2 request line source SQL facts (one save + audit + rebuild + the pairing check, and the
            // Down refusal 51167), kept ordinal-sorted (37e3e2bd… -> 70c956bd…).
            // A1-3 (plan API-10; No.161 §2): +5 order remainder cancellation SQL facts (one save after a partial receipt + both
            // supply ports + checks, draft / fully accepted refusals, Σ-shares kept + request rejection unblocked, company
            // isolation, Down refusal 51168), kept ordinal-sorted (70c956bd… -> ae6ee9f7…).
            // A2a-1 (plan TEK-01/API-20): +4 order line price SQL facts (one save + audit + rebuild + approval + the price/money checks,
            // unpriced columns null + priced technical line refused, company isolation, Down refusal 51169), kept ordinal-sorted
            // (ae6ee9f7… -> 2085f158…); +1 explicit zero price fact (Sinan #66 6704170096; 2085f158… -> c3fa56d7…).
            // A2a-1c (ALS-D72): +2 unit-price scale SQL facts (four-decimal price stored + rebuilt + the scale check, Down refusal
            // 51170), kept ordinal-sorted (c3fa56d7… -> dab7b47e…).
            // A2a-2 (ALS-D38, ALS-D75): +2 maturity surcharge SQL facts (stored + audited + rebuilt + the surcharge check, Down refusal
            // 51171), kept ordinal-sorted (dab7b47e… -> e4eea63d…).
            // A2a-5a (ALS-D3, Fable F-015): +3 order approval SQL facts (preparer refused + another actor's approval stored, audited
            // and rebuilt; the approval check on direct writes + a legacy approval still loads; Down refusal 51172), kept
            // ordinal-sorted (70821828… -> c307bc9f…).
            // A3 API-30 (ALS-D33, ALS-D21): +3 line delivery schedule SQL facts (an append-only revision stored in one save, replayed,
            // rebuilt between the acceptance shares + the slice checks and keys on direct writes; a Draft schedule before the
            // approval + company isolation; Down refusal 51173), kept ordinal-sorted (c307bc9f… -> f81eff47…).
            // A3 API-30 r3 (review A P3): +1 same-line schedule race SQL fact (two saves at one version yield one revision, one
            // audit record and a concurrent change for the loser), kept ordinal-sorted (f81eff47… -> 11e38799…).
            // Re-fingerprinted for tren-1 wave 2 (trial integration): +4 facts, ordinal-sorted (ef97c10c… -> 710ae3f8…).
            ["tools/relational_inventory/purchasing.txt"] =
                // Re-fingerprinted for tren-1 wave 1: +5 facts, ordinal-sorted (11e38799… -> ef97c10c…).
                // Re-fingerprinted for tren-1 wave 3 (integration): +1 fact, ordinal-sorted (710ae3f8… -> f808203c…).
                // Re-fingerprinted for tren-1 W5 (integration): +2 fact(s) from the W5 team merges; value from guard_fingerprint.py (f808203c… -> 188008c1…).
                // Tren-1 W6: +4 source facts, ordinal-sorted; SQL execution NOT_RUN.
                // W8 EK24 FNS-D105: +1 finalized outbox Down-guard fact, kept ordinal-sorted (4bea9141… -> 982791b3…).
                "982791b33dfc8b38c1bde154f3e5e8256f231fe180ae0be6c6b7fc6798345c4b",

            // sales
            // DEC-SAL-003: +3 SalesDeliveryScheduleSqlServerRelationalAcceptanceTests facts (16fa5110… -> 00189b7b…).
            // AT-024: +2 SalesDeliveryProgressSqlServerRelationalAcceptanceTests facts (00189b7b… -> 719d0d03…).
            // DEC-SAL-005: +4 SalesOrderCancellationSqlServerRelationalAcceptanceTests facts (719d0d03… -> 5508f4ed…).
            // DEC-SAL-002: +4 SalesOrderQuantityRevisionSqlServerRelationalAcceptanceTests facts (5508f4ed… -> 3ef99851…).
            // #527 Q9: +1 customer-acceptance persistence/replay/cutover fact (3ef99851… -> dd3c72d9…).
            // PL04: +1 exact price-floor approval replay/down-refusal fact.
            // Sales-Cari bounded display acceptance: +1 reviewed Sales SQL fact, kept ordinal-sorted.
            // D-05: +1 pending-approval owner-read SQL fact, kept ordinal-sorted.
            // No 30 / SAT-D12: +4 SalesOrderForceCloseSqlServerRelationalAcceptanceTests facts (38262b4b… -> 5932b012…).
            // TEK-13: +5 SalesRemainderChangeSqlServerRelationalAcceptanceTests facts, kept ordinal-sorted (5932b012… -> f2c6c3ac…).
            // SAT-D8 / API-10: +2 SalesCustomerReturnsSqlServerRelationalAcceptanceTests facts, kept ordinal-sorted (f2c6c3ac… -> aabdd034…).
            // API-20 / API-21: +3 sales request SQL facts (one save + decisions, company isolation + race, routes + rollback), ordinal-sorted
            // (merged over main's: aabdd034… -> 19d5e743…).
            // API-22…24: +5 sales quote SQL facts; merged over DEC-SAL-001 / API-08's +2 SalesOrderStatusSqlServerRelationalAcceptanceTests
            // facts, kept ordinal-sorted (a9873855… -> 06afeb99…).
            // SAT-D2 / API-12: +2 SalesDiscountExceptionSqlServerRelationalAcceptanceTests facts, kept ordinal-sorted (06afeb99… -> b1a5335a…).
            // UI-20 shell 1.8: +1 S-01 pool capability SQL fact, ordinal-sorted (b1a5335a… -> efdb5edd…).
            // MOD-07: +1 SalesOrderPdfSqlServerRelationalAcceptanceTests fact, kept ordinal-sorted (merged over UI-20 shell 1.8:
            // efdb5edd… -> 87dc8166…).
            // MOD-01 / API-06: +2 SalesRiskCheckSqlServerRelationalAcceptanceTests facts, merged over MOD-07, kept ordinal-sorted
            // (87dc8166… -> d74f7c2d…).
            // API-03 / SAT-D11: +2 SalesOrderTotalsSqlServerRelationalAcceptanceTests facts, merged over MOD-01 / API-06, kept
            // ordinal-sorted (d74f7c2d… -> 0d7854cc…); +1 out-of-range totals fact after review B P2-1 (0d7854cc… -> 78eb83d7…).
            // API-30 / API-31 / MOD-06: +2 SalesDispatchSqlServerRelationalAcceptanceTests facts, merged over API-03, kept ordinal-sorted
            // (78eb83d7… -> 9587a78b…).
            // SAT-D24/D44, SAT-D25 (+2) and SAT-D71/D72 (+1) SalesQuoteSqlServerRelationalAcceptanceTests facts merged over API-03 and
            // API-30 / API-31, kept ordinal-sorted (9587a78b… -> 2b0ee4d9…).
            // SAT-D103 / SAT-D104 / SAT-D111 / SAT-D112 (EK13, EK14), merged over #849's quote facts: the quantity revision's "a
            // loading line blocks" fact replaced by the shipped/loaded floor and re-approval cap fact, +1 price re-approval fact,
            // +1 rejected decrease resubmission fact (K3-REVIEW-B P1-1), kept ordinal-sorted (2b0ee4d9… -> ece55741…).
            // API-41 / API-42: +2 SalesReportSqlServerRelationalAcceptanceTests facts, kept ordinal-sorted (ece55741… -> a85d5b59…).
            // API-04 (merged over API-41 / API-42): +4 header revision facts (route + one save, customer/currency through the store,
            // isolation/authority, migration backfill and 51133 rollback refusal), +2 (every revision's own audit reason; 51133's
            // column arms alone) and +1 (1000/1001/4000-character reasons recorded whole), kept ordinal-sorted (a85d5b59… -> 2e764e76…).
            // K3-REVIEW-B P2-1 (#864 × #865): +1 SalesReportSqlServerRelationalAcceptanceTests fact (a pending customer revision keeps
            // the accepted open quantity under the accepted customer), kept ordinal-sorted (2e764e76… -> af6987da…).
            // SAT-D115 / SAT-D152 (EK14, EK21): +3 SalesOrderCustomerRejectionSqlServerRelationalAcceptanceTests facts (route + one
            // save, isolation/authority, 51134 rollback refusal), +1 SalesQuote (quote customer lock) and +1 SalesReport (a rejected
            // revision awaits nothing) fact, kept ordinal-sorted (af6987da… -> 55482d2d…).
            // K3-REVIEW-B P2-1 / P2-2, SAT-D165 / SAT-D166 (EK23): +1 SalesOrderPdf (the accepted revision is printed, never the
            // reason) and +1 SalesRiskCheck (the remainder is the accepted revision's) fact, kept ordinal-sorted (55482d2d… -> a921318d…).
            // MOD-03 → Sales (SAT-D93/D97/D105/D140/D147): +3 SalesOrderTax facts (an approved order's tax change archives the
            // approved tax and the tax/totals routes answer the stored snapshots; a buyer tax change moves the header and line tax
            // columns; the real Settings tax options and the 51135 rollback refusal), kept ordinal-sorted (a921318d… -> 1c5c3996…).
            // SAT-D184 (EK26): +1 SalesOrderTax fact (the BackfillDraftBuyerTax migration writes "Normal" once on a legacy order),
            // kept ordinal-sorted (1c5c3996… -> 4fa6ad29…).
            // SAT-D175/D176 (EK25): +2 SalesSubcontractServiceOrder facts (the flag, item type and price persisted and the read
            // port answered for its company only; the write guard, CK_DraftLines_ItemType and the 51141 rollback refusal), kept
            // ordinal-sorted (4fa6ad29… -> 0886d9ff…).
            // SAT-D195 (EK29): +1 SalesSubcontractServiceOrder fact (a quantity revision of an approved fason order re-enters its
            // internal approval with no discount check) and the first fact renamed (per-kg price), kept ordinal-sorted (0886d9ff… -> 1f679068…).
            // SAT-D167 (EK24): +3 SalesOrderLineRevision facts (an approved order's line revision adds a row, passivates a line,
            // archives Rev.0 and rebuilds; the write guard, CK_DraftLines_LineRevision and the 51142 rollback refusal; the route's grant,
            // replay and company refusals), kept
            // ordinal-sorted (1f679068… -> ba7de976…).
            // SAT-D211 (EK36; K3 review B P2-1): +1 SalesOrderLineRevision fact (a rejected removal brings the line back in the
            // stored order, the line port and the approved-line list; the guard admits no other reinstatement move), kept
            // ordinal-sorted (ba7de976… -> ee57b276…).
            // SAT-D219 (EK38): +1 SalesOrderLineRevision fact (the route refuses a removal of a line with an open linked supply, asks the
            // owner for the server company, and passes once the link is closed), kept ordinal-sorted (ee57b276… -> a2ac1f92…).
            // K3 review A/B P1-1 + B P2-2 (SAT-D211 / SAT-D219): +3 SalesOrderLineRevision facts (the second rejection of one revision, a
            // customer rejection after a second removal, the submission and approval re-read of the linked supply on the real routes),
            // kept ordinal-sorted (a2ac1f92… -> a58234a1…).
            // K3 review A P1-1 / P2-1 (SAT-D211): +1 SalesOrderLineRevision fact (a quantity revision waits for the reinstatement after a
            // customer rejection; an empty line revision reinstates), kept ordinal-sorted (a58234a1… -> 96505e5c…).
            // Merged with main's #952 (SAT-D32) Sales facts: recomputed over the union, ordinal-sorted (-> 78be7220…).
            // SAT-D17: +1 SalesOrderLineClosure fact (Down refusal 51228, the store close and restore, the PK/FK constraints), kept ordinal-sorted.
            // SAT-D17 / SAT-D215 K3 pre-review P1-4: +4 SalesOrderLineClosure facts (the one-save close with attempt and audit, the recorded
            // refusal and its replay, company isolation, the line port refusing a closed line), kept ordinal-sorted (aea264d8... -> 6a9fd4ab...).
            ["tools/relational_inventory/sales.txt"] =
            // SAT-D30 / SAT-D213 (merged over SAT-D32 and SAT-D167): +6 SalesOrderOverDeliveryApproval facts (the one-save approval with
            // replay, the real Logistics owner's fail-closed refusal, 404 / 403 / self-approval, the 51213 rollback refusal, two races),
            // unioned with main's facts and kept ordinal-sorted (78be7220… -> 249ea3fc…).
            // S5 API-32 (Fatura cekirdegi 1): +2 SalesInvoice facts (the invoice draft's one save, replay, active-share index and
            // company isolation; the 51221 rollback refusal), kept ordinal-sorted (95e16c66... -> 25ed266d...).
                // S5 API-33 (Fatura cekirdegi 2): +3 SalesInvoice facts (submit/approve by another actor and the frozen rows, the cancelled invoice's
            // share release in the same save, the recorded and replayed terminal refusal), kept ordinal-sorted (d9564cc8... -> 0030e572...).
                // S5 API-35 (Fatura cekirdegi 3): +2 SalesInvoice facts (the official match's one save with attempt and audit, the untouched Approved
                // invoice and the replay; the append-only match table, its amount checks and the 51225 rollback refusal), kept ordinal-sorted (0030e572... -> be1b2f40...).
                // SAT-D132 (quote vade): +1 Sales fact (quote due terms per revision, carry-over to the order, the 51227 rollback refusal), kept ordinal-sorted (be1b2f40... -> 525d38a4...).
                // Merged with main's #984 (SAT-D132) fact: SAT-D17 line closure +5 facts recomputed over the union (525d38a4... -> 7c089718...).
                // Line identity FKs (review finding): +1 SalesOrderLineClosure fact (a LineId of another line is refused by the three
                // 4-column FKs; the Up guard 51234 refuses a mismatch), kept ordinal-sorted (55ad9807... -> e2b0d6ba...).
                // Satis modul baglantilari 4a/4d/5d/5e/5f/5k2: +6 facts (the real Settings rate resolver and rate lists, the real Cari
                // account, contact and account selection ports, the real Identity SALES_SUPPORT reader), kept ordinal-sorted (e2b0d6ba... -> 096ea842...).
                // Merged with main's #1020 (+6 facts) and SAT-D232 (EK46): +1 SalesInvoice fact (a USD invoice's 0,06 TL difference writes
                // nothing unapproved and is stored approved), recomputed over the union, kept ordinal-sorted (096ea842... -> 2016a227...).
                // EK46 P3 follow-up (API-24, SAT-D234): +1 SalesQuote fact (a catalogued loss reason loses the quote through the real host
                // with its Settings snapshot, attempt and audit), kept ordinal-sorted (2016a227... -> 21fade35...).
                // SAT-D245 (EK48): +1 SalesInvoice fact (a single-instalment USD invoice whose TL payable is 0,00 is refused at finalisation
                // and writes nothing), kept ordinal-sorted (21fade35... -> 834500af...).
                // SAT-D246 (EK49): +1 SalesInvoice fact (the control center lists every order of a two-order invoice in line order and
                // another company sees none), kept ordinal-sorted (834500af... -> d9d2c108...).
                // SAT-D241 (EK48): +2 SalesInvoice facts (a VAT or withholding TL difference above the tolerance writes nothing unapproved and
                // is stored approved; the tax-comparison migration's Down passes with a legacy match and refuses with 51242 while a compared
                // match exists), kept ordinal-sorted (d9d2c108... -> e49ab090...).
                // SAT-D252..SAT-D255 (API-11 source plan, merged after SAT-D241): +8 SalesDraftSourcePlan facts (one save with attempt and
                // audit, the CK and FKs, company isolation, the partial-delivery remainder, terminal refusals, unreadable owners, the
                // permission, the 51241 rollback refusal), kept ordinal-sorted; recomputed on the merged inventory (e49ab090... -> 320e578b...).
                // S5 API-34 (SAT-D244, merged after API-11): +1 SalesInvoice fact (the export reads only finalised invoices of the range
                // with every order number and company isolation), kept ordinal-sorted; recomputed on the merged inventory (320e578b... -> 17f70832...).
                // API-07 (SAT-D153..SAT-D162): +4 SalesShipmentRiskException facts (one save with attempt and audit, the maker-checker CK and
                // decision guard, company isolation, the 51251 rollback refusal), kept ordinal-sorted (17f70832... -> e083b70f...).
                // EK56 SAT-D286: +2 SalesShipmentRiskException facts (the requester's withdrawal in one save with attempt and audit, the
                // shipment's new request and the 51252 guard on a withdrawn row; the 51254 rollback refusal), kept ordinal-sorted
                // (e083b70f... -> 1628581b...).
                // API-40 (SAT-D288..SAT-D294): +4 SalesCustomerReturn facts (one save with attempt and audit, self-approval refusal and
                // Finance-port finalisation, the document/step/delete and attempt guards, company isolation, the 51257 rollback refusal),
                // kept ordinal-sorted; recomputed on the merged inventory (1628581b... -> e02ba740...).
                // SAT-D290 (2, 3): +2 SalesCustomerReturn facts (the rejection by another actor and the draft's cancellation, both terminal
                // under the 51255 guard and the maker-checker CK; the 51259 rollback refusal), kept ordinal-sorted (e02ba740... -> 766e032e...).
                // SAT-D216 / SAT-D222 (B-33): the Logistics owner names the shipper, so the Sales over-delivery approval fact is
                // replaced by 3 facts (shipper named and refused, a share without actor refused fail-closed, the 51260 rollback
                // refusal and the RecordedBy check/collation), kept ordinal-sorted (766e032e... -> eeed0741...).
                // SAT-D216 P2 follow-up: +1 SalesDeliveryProgress fact (the Logistics owner port names each shipper once in ordinal order and
                // one unnamed shipment makes the whole line unnamed), kept ordinal-sorted (eeed0741... -> 46a4216c...).
                // F-020 review fixes: +1 SalesOrderCancellation fact (a cancelled instruction's loading line reads as no loading through
                // the real Sales preview, never as a malformed owner answer), kept ordinal-sorted (46a4216c... -> 5a014a10...).
                // Tren-1 wave 2 trial integration: +3 F018 limit-warning/risk-gate facts (satis wave 2) and +2 SAT-S14 SalesContract facts,
                // kept ordinal-sorted (5a014a10... -> cebac90b...).
                // Re-fingerprinted for tren-1 W5 (integration): +1 fact(s) from the W5 team merges; value from guard_fingerprint.py (cebac90b… -> 8c3781c4…).
                // Tren-1 W6: +6 source facts, ordinal-sorted; SQL execution NOT_RUN.
                "defdb1fe6ab7a78087df79d1e8f1cd32b6bc35a0704071330e5b04a6237e98de",

            // work
            // JV03: the five ordinal desired-state storage SQL facts are mandatory and skip-free.
            // Re-fingerprinted for B-24 (Work outbox dispatcher): +3 dispatch facts (8eeffadb... -> 6e3a97bd...).
            ["tools/relational_inventory/work.txt"] =
                // Re-fingerprinted for tren-1 wave 1: +1 facts, ordinal-sorted (6e3a97bd… -> f745f376…).
                "d0ca8b4d5283af3e1ee36da8b4f1259d0bf99368dbb4e9bd0b27de1841f39196",
        };
    private const string ContinuousIntegrationWorkflowPath = ".github/workflows/ci.yml";
    private const string BackendTestGroupVerifierPath = "tools/verify_backend_test_groups.py";
    private const string BackendTestGroupVerifierTestsPath =
        "tests/backend_test_group_verifier/test_verify_backend_test_groups.py";
    private const string ReviewedBackendTestGroupVerifierFingerprint =
        // AI-2a: + GoymenERP.SecurityEvents.UnitTests (75 solution / 22 TestSDK projects) (30b8186c… -> ac738bab…).
        // W8: Organization's same nine cases now have scalar SDK-discovered identities; remove only
        // its obsolete culture-dependent deferred exception. All verifier functions are unchanged.
        // W8 Windows: only an oversized argv uses whole-class filters after full-discovery equivalence.
        // Class fixtures, partition, exact native IDs/theories, SQL-only skips and proof checks remain.
        // House-producer fingerprint; independent exact-head K3 review is pending.
        // W8: Manufacturing's four primitive InlineData identities are discovered exactly; no production deferred exception remains.
        "1cc19d0b6ba103fbf4e4bdacd545de31be45902c04f080dd1940ffe4f3fc2612";
    private const string ReviewedBackendTestGroupVerifierTestsFingerprint =
        // Deferred metadata count 2 -> 1, with Organization explicitly excluded; negative gates retained.
        // W8: production deferred cases are empty; explicit synthetic source/identity negative gates remain.
        "211465ba0b5738fc1abbfe4934bd3216e6e189d40d5a7b51415eb22f1ef02ee5";
    // Re-reviewed with the existing recovery proof intact and the release-manifest proof bound to the exact head:
    // both jobs are artifact-free, fail closed and do not deploy; ci_safe_trx_summary guards them separately.
    // Re-fingerprinted for CIH-D5: only the trigger path filter under `on:` changed (prose no job reads does not
    // start the workflow); every job is byte-identical. test_workflow_path_filters.py pins the filter.
    // Re-fingerprinted for CIH-D8: the three aggregate jobs (backend, frontend, relational) run on
    // `!cancelled()` instead of `always()`. A failed, timed-out or fail-fast-cancelled worker still makes them
    // run and fail closed; only a cancelled run skips them, so a superseded run frees its runners at once.
    private const string ReviewedContinuousIntegrationWorkflowFingerprint =
        "7190110523c47a2669c374d00cd53937eed3f78c95fcfbc3b9c297d7915801bd";
    // Re-fingerprinted for the ORG master edit: the seeder's three existing IgnoreQueryFilters projections also read
    // the row id, so a seeded record a user renamed is recognised by its deterministic id and never re-added under
    // its old code. No new bypass, query or write path; the IgnoreQueryFilters reads are the same three.
    private const string ReviewedSeederFingerprint =
        // Re-fingerprinted for tren-1 wave 2 trial integration (KIM-D28): the seeder refuses duplicate seed VKNs before any read; the three IgnoreQueryFilters
        // projections are unchanged (84c4a5cd... -> 5b46dec3...).
        "5b46dec3a28ece710b935c63c3b5497114938107e5c6a8924edc20eb9cdd95ee";

    // This is a reviewed source inventory, not a claim that every entity mapped by every
    // context has a global query filter. Any context change must receive a fresh tenant-safety
    // review and a new fingerprint.
    private const string ReviewedWorkDbContextPath =
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/"
        + "Persistence/WorkDbContext.cs";
    // W8 existing source fingerprints re-measured; API occurrence guards and K3 pending status remain unchanged.
    private static readonly IReadOnlyDictionary<string, string> ReviewedPlatformDbContexts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/"
             + "Persistence/AuditDbContext.cs"] =
                // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                // Re-fingerprinted for tren-1 W6: PLT audit ownership: enlisted owner batch append keeps existing tenant filters; reviewed API counts unchanged (fe7d5631... -> f2061c89...).
                "6c4db3fd6f5e61295e946a6ee2e3c2624c4f11a640f2161954badc197745e447",
            // AI-2a (ADR-SEC-001): append-only security event channel, company query filter on CompanyId.
            ["src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/"
             + "Persistence/SecurityEventsDbContext.cs"] =
                "61fc1b4e3dadbdf5f58bd3972e3253d2bed68bda039abf308df585814dd367ac",
            ["src/backend/Modules/Idempotency/"
             + "GoymenERP.Modules.Idempotency.Infrastructure/Persistence/"
             + "CommandExecutionDbContext.cs"] =
                // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (36220363… -> 09c6699a…).
                "09c6699a05434d3038d1972f91319e91ab2ce9a5f55ddd33f360f252d88905b0",
            // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (4cf3e94b… -> d2c26ac3…).
            ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/"
             + "Persistence/IdentityDbContext.cs"] =
                // Re-fingerprinted for ID-1: the identity.LocalCredentials mapping (one row per user, a restricted FK
                // to Users, a unique binary-collated normalized user name, fail-closed counter/format checks). Like Users
                // it is group-wide and carries no CompanyId (spec "Kullanıcı rolleri ve CompanyId kapsamı"); the
                // existing company query filters and tenant write guard are unchanged (31367b8f… -> 4cf3e94b…).
                // Re-fingerprinted for tren-1 W6: PLT audit ownership: Identity no longer maps the Audit table; reviewed API counts unchanged (d2c26ac3... -> 1eb7ccd3...).
                // Re-fingerprinted for tren-1 W7: owner append-only schema changes; company filters retained; K3 review pending.
                // W8: nullable approval mode cannot pass Approved checks through SQL UNKNOWN; tenant filters unchanged. K3 review pending.
                "cf04e11942803d47dbccfacd52140be75ceaaa2e87a49205d16847e76f134136",
            // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (208f9114… -> 6881862d…).
            ["src/backend/Modules/Organization/"
             + "GoymenERP.Modules.Organization.Infrastructure/Persistence/"
             + "OrganizationDbContext.cs"] =
                // Re-fingerprinted for ORG-LOCATION-OPERATOR-CRUD-01A: one nullable OperatorPartyId and two
                // fail-closed check constraints; the CompanyId query filters and tenant write guard are unchanged.
                // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (6881862d… -> 440ed323…).
                // Re-fingerprinted for tren-1 W7: owner append-only schema changes; company filters retained; K3 review pending.
                "6b49a2549b292506855fdf8a3c80b11dd91f37e725cfee2b0b6908aa5df1321d",
            ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
             + "Persistence/SettingsDbContext.cs"] =
                // Re-fingerprinted for SAT-D61…D70: the company-filtered discount limit group, limit record and binding
                // sets (company inside every key and foreign key), the platform AuditEvent/CommandExecution mappings with
                // the safe CompanyId filter, and the write fence (Manufacturing tolerance stays read-only, nothing is
                // deleted, limit records and binding rows are immutable, every row in the context's company).
                // Re-fingerprinted for SAT-D84: the company-filtered, insert-only limit withdrawal set (company inside its
                // key and both foreign keys) joins the immutable rows of the write fence.
                // Re-fingerprinted for MOD-03: the company-filtered tax master sets (categories, immutable rate records and code list versions with insert-only withdrawals; company inside every key and foreign key) join the write fence (immutable rows, every row in the context company).
                // Re-fingerprinted for SAT-D93 (Sinan 2026-10-31): the company-filtered VUK 232 withholding threshold fence, its
                // immutable records and insert-only withdrawals join the write fence.
                // Re-fingerprinted for SAT-D158 (EK21): the company-filtered buyer situation list (fence, immutable whole
                // versions and entries with their tax effect, insert-only withdrawals; company inside every key and foreign key)
                // joins the write fence.
                // Re-fingerprinted for Üretim U1-a: the class becomes partial (the Manufacturing profile sets live in
                // SettingsDbContext.ManufacturingProfiles.cs) and the write fence admits a tolerance registry row only as the
                // insert of its lifecycle publication (same save, Approved → Published); the new rows join the company check.
                // Re-fingerprinted for Üretim U1-b: the write fence also admits the one supersession update of a published
                // tolerance registry row (its open-ended window closed beside the successor's publication in the same save).
                // Re-fingerprinted for DEC-FIN-010 / KUR_GUNU / SAT-D169…D171: the company-filtered exchange-rate record (day fence,
                // append-only records and their rate lines; company inside every key and foreign key) joins the write fence
                // (records and rate lines immutable, every row in the context company).
                // Re-fingerprinted for review B P2 / EK31 SAT-D198: the record state check admits the official NotFound bulletin
                // state and the reasoned manual half-day mark; the write fence is unchanged.
                // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                // Re-fingerprinted for tren-1 wave 2 trial integration: the Settings team slice (1.3) combined; reviewed tokens unchanged (1505fc34... -> 1338eb2e...).
                // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (1338eb2e… -> c7b896e7…).
                // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (c7b896e7… -> a0575b35…).
                // Re-fingerprinted for tren-1 W7: owner append-only schema changes; company filters retained; K3 review pending.
                "b808440b06c3c104e8eb372867e1d795708e26aef8fc43978b9b8620f77c0606",
            // SALES-P2 (#315): company-scoped like Organization; every tenant row and AuditEvent carries the
            // CompanyId filter. A standard entry, not an exception.
            // Re-fingerprinted for DEC-SAL-003: the two delivery-schedule tables (DraftDeliverySchedules,
            // DraftDeliveryScheduleDates), each with the same CompanyId query filter and a company-leading key.
            // Re-fingerprinted for DEC-SAL-005: the cancellation table (DraftCancellations), with the same CompanyId
            // query filter and a company-leading key, plus the lifecycle column on Drafts.
            // Re-fingerprinted for DEC-SAL-002: the nullable Reason column on DraftRevisions and the line write guard
            // admitting Quantity/QuantityText under a quantity revision; no new table, query filter or key.
            // Re-fingerprinted for Q9: the customer-acceptance facts and their approval-state constraint remain on the
            // same company-filtered Draft row; no query-filter bypass, direct database surface or tenant key changed.
            // Re-fingerprinted for PL04: the immutable price-floor replay-result constraint stays on the same
            // company-filtered attempt row; no query-filter bypass, direct database surface or tenant key changed.
            // Re-fingerprinted for No 30 / SAT-D12: the force-closure table (DraftForceClosures), with the same CompanyId
            // query filter and a company-leading key, the widened lifecycle CK (Closed = 3) and the Draft -> Closed guard.
            // Re-fingerprinted for API-20 / API-21: the sales request tables (Requests, RequestLines, DocumentCounters and the
            // request commands' own RequestAttempts, ADR-SAL-001 §2), each with the same CompanyId query filter and a
            // company-leading key, the attempt table's declared immutability trigger, and the request's decision-only write guard.
            // Re-fingerprinted for API-22…24: the quote tables (Quotes, QuoteRevisions, QuoteLines, QuoteRequests), each with the
            // same CompanyId query filter and a company-leading key; the quote commands' own QuoteAttempts (ADR-SAL-001 §2, declared
            // immutability trigger) and the order's nullable SourceQuoteId (company-leading foreign keys, filtered unique
            // UX_Drafts_SourceQuoteId); the quote's lifecycle-only guard. DraftAttempts is unchanged.
            // Re-fingerprinted for SAT-D2 / API-12: CK_DraftAttempts_Result admits the discount-exception approval's replay
            // result on the same company-filtered attempt row; no query-filter bypass, direct database surface or tenant key changed.
            // Re-fingerprinted for SAT-D24/D44 and SAT-D25: the company-filtered QuoteExpiryApprovals table (company-leading
            // key and FK to the quote revision, append-only in the write guard), the nullable list-reason snapshot on Quotes and
            // the widened CK_Quotes_Lifecycle / CK_QuoteAttempts_Command; no query-filter bypass or direct database surface.
            // Re-fingerprinted for API-04: the company-filtered DraftRevisionHeaders table (company-leading key and FK to the archived
            // revision, append-only in the write guard), two nullable header columns with CK_Drafts_HeaderRevision, and the
            // header's write guard widened by exactly the revisable header columns; no query-filter bypass or direct database surface.
            // Re-fingerprinted for SAT-D115: the company-filtered DraftCustomerRejections table (company-leading key and FK to the
            // draft, append-only in the write guard); no query-filter bypass or direct database surface.
            // Re-fingerprinted for MOD-03 → Sales: four nullable JSON columns (BuyerTaxJson on Drafts / DraftRevisionHeaders, TaxJson
            // on DraftLines / DraftRevisionLines) with ISJSON check constraints, and the write guard widened by exactly
            // Drafts.BuyerTaxJson and DraftLines.TaxJson; no query-filter bypass or direct database surface.
            // Re-fingerprinted for SAT-D175/D176 (EK25): Drafts / DraftRevisionHeaders.IsSubcontractService and DraftLines.ItemType
            // (CK_DraftLines_ItemType); the write guard is NOT widened (the flag and the item type never change after the insert).
            // Re-fingerprinted for SAT-D167 (EK24): DraftLines.AddedInRevision / RemovedInRevision / ReplacedByLineId
            // (CK_DraftLines_LineRevision); the write guard is widened by exactly DraftLines.RemovedInRevision and ReplacedByLineId,
            // and a removal is never undone or changed (a passive line never comes back).
            // Re-fingerprinted for SAT-D211 (EK36): DraftLines.ReinstatedInRevision (in CK_DraftLines_LineRevision); the guard admits it
            // and exactly one way back — RemovedInRevision R → null with the replacement cleared and ReinstatedInRevision → R.
            // Re-fingerprinted for K3 review A/B P1-1: the way back judges ReinstatedInRevision's FINAL value (== R), not whether EF
            // reports it modified (a line removed again in the same revision and reverted again writes R -> R).
            ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/"
             // Re-fingerprinted for SAT-D17 (line closure): the company-filtered DraftLineClosures table (key per line, FKs to the draft and its line, CK_DraftLineClosures_Identity), append-only in the write guard.
             + "Persistence/SalesDbContext.cs"] =
            // Re-fingerprinted for SAT-D30 / SAT-D213 (merged over SAT-D32 and SAT-D167): the company-filtered DraftOverDeliveryApprovals
            // table (company-leading key, FKs to the draft and its line, CK_DraftOverDeliveryApprovals_Identity, append-only in the write guard).
                // Re-fingerprinted for S5 API-32 (Fatura cekirdegi 1): Invoices / InvoiceLines / InvoiceAttempts tenant rows under the
                // same company filter, the filtered unique UX_InvoiceLines_ActiveShipment and their check constraints; the write guard
                // admits no modification of them (append-only); no query-filter bypass or direct database surface.
                // API-33: the header may change only its nine decision columns (state, version, submitted/decided/cancelled stamps and reasons); lines and
                // attempts stay append-only; TR_Invoices_Guard / TR_InvoiceLines_Guard enforce the same in SQL (1a8c08ee... -> ab9a9588...).
                // API-35: the append-only InvoiceMatches tenant rows (one per invoice, unique official number and ETTN per company) under the same
                // company filter; no query-filter bypass or direct database surface (ab9a9588... -> 5d465acf...).
                // SAT-D132: QuoteRevisions.DueTermsJson (nullable, CK_QuoteRevisions_DueTerms ISJSON) on the same company-filtered revision row; no query-filter bypass or direct database surface (5d465acf... -> da465c59...).
                // SAT-D17: DraftLineClosures (keyed per line, FK to draft and line, CK) on the same company-filtered context; no query-filter bypass or direct database surface (da465c59... -> af489a5a...).
                // Line identity FKs (review finding): alternate key AK_DraftLines_LineIdentity (CompanyId, DraftId, LineNo, LineId) and a 4-column
                // Restrict FK to it from DraftOverDeliveryApprovals, DraftLineClosures and DraftRemainderChanges; no query-filter bypass or direct database surface (17c1be19... -> 1d567f05...).
                // SAT-D241 (EK48): six nullable VAT/withholding columns on the same company-filtered, append-only InvoiceMatches row,
                // CK_InvoiceMatches_TaxAmounts and the widened CK_InvoiceMatches_Amounts; no query-filter bypass or direct database surface (1d567f05... -> 22f3ea3d...).
                // The tax check's second branch names every column IS NOT NULL (a CHECK accepts UNKNOWN) (22f3ea3d... -> 48ee7004...).
                // SAT-D252..SAT-D255 (merged after SAT-D241): DraftSourcePlanLines (keyed per draft version and line, FKs to the draft and
                // its line identity, CK_DraftSourcePlanLines_Identity), append-only in the write guard, on the same company-filtered
                // context; no query-filter bypass or direct database surface; recomputed on the merged context (48ee7004... -> c40fbf07...).
                // API-07 (SAT-D153..SAT-D162): ShipmentRiskExceptions and ShipmentRiskExceptionAttempts on the same company-filtered context
                // (CK_ShipmentRiskExceptions_MakerChecker); no query-filter bypass or direct database surface (c40fbf07... -> b7efaf4b...).
                // API-07: both guard triggers declared with HasTrigger (EF Core then saves without OUTPUT); no query-filter bypass or
                // direct database surface (b7efaf4b... -> 7694fe06...).
                // EK56 SAT-D286: the state, decision and attempt-command checks admit a withdrawn exception (State 4, no decision facts)
                // and the write guard lets Requested move to Withdrawn; no query-filter bypass or direct database surface
                // (7694fe06... -> 8a351e15...).
                // API-40 (SAT-D288..SAT-D294): CustomerReturns and CustomerReturnAttempts on the same company-filtered context (FK to the
                // original invoice, CK_CustomerReturns_MakerChecker, both guard triggers declared with HasTrigger); no query-filter bypass
                // or direct database surface; recomputed on the merged context (8a351e15... -> 28dc8473...).
                // SAT-D290 (2, 3): the facts, maker-checker and attempt-command checks admit a rejected (State 5) or cancelled (State 6)
                // return with its decision facts; no query-filter bypass or direct database surface (28dc8473... -> e838b21d...).
                // Tren-1 wave 2 trial integration: satis wave 2 (F018 limit warnings) and SAT-S14 contracts, contract attempts, payment plan and
                // discount rule rows on the same company filter; no query-filter bypass or direct database surface (e838b21d... -> cedf03b1...).
                // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (cedf03b1… -> f8d28845…).
                "a6194826336fa8a0538884be679ac989d89dfd8d28505a3779eabfe4d6ab2d36",
            // LOG-4a: company-scoped like Organization; every tenant row (instructions, lines, shares, attempts) and
            // AuditEvent carries the CompanyId filter. A standard entry, not an exception.
            ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/"
             + "Persistence/LogisticsDbContext.cs"] =
            // Re-measured for AT-053: VehicleWeighings, company-scoped with the same CompanyId query filter and a
            // company-leading key; one weighing per instruction.
            // Re-measured for AT-055: DispatchNotes and DispatchNoteLines, company-scoped with the same CompanyId query filter and
            // company-leading keys; UX_DispatchNoteLines_ShipmentId over non-nullable columns (a shipment on one dispatch note).
            // Re-measured for the AT-055 lifecycle (#518): DispatchNoteSeries, company-scoped with the same CompanyId query filter
            // and a company-leading key; the single-link key filtered to unreleased lines; forward-only write guard.
            // Re-measured for I01.2: transfer outgoing documents and ordered line bindings carry the same CompanyId
            // query filter and company-leading keys; the transfer link remains unique inside one company.
            // Re-measured for SAT-D216 / SAT-D222: LoadingShares.RecordedBy (nullable, binary collation, non-blank check); the same
            // CompanyId query filter and keys, no new entity.
            // Re-measured for F-020: LoadingInstructions.CancelledBy/CancelledAtUtc/CancelReason (nullable), the Cancelled status in
            // the status check and the cancel-facts check; the same CompanyId query filter and keys, no new entity.
            // Re-measured for the F-020 write guard: the instruction's allow-list admits the three cancel marks and each is written
            // from empty exactly once; no query-filter bypass or direct database surface (0a6d41e6... -> 13659996...).
                // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): STK unload weighings (AddUnloadWeighings) mapped on the company-filtered context (13659996… -> d4a4b458…).
                // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (d4a4b458… -> c4c0fbbc…).
                "1167541b56f82916db809a04f538f39c15dd53154950f7dacb85d26ad7e96c92",
            // PUR-4a: company-scoped like Organization; every tenant row (requests and their lines, orders and their
            // lines, attempts) and AuditEvent carries the CompanyId filter. A standard entry, not an exception.
            // Re-measured for A1-1: the request's decision columns, the decision and maker-checker checks and the write
            // guard's decision-fact allow-list; filters and keys unchanged.
            // Re-measured for A1-1b: the withdrawal columns and check, the Withdrawn branch of the decision check, the
            // binary maker-checker comparison (#900 review P3) and the write guard's withdrawal-fact allow-list; filters
            // and keys unchanged. Re-measured for ALS-D47: the withdrawal check accepts the preparer or the submitter.
            // Re-measured for A1-2 (plan API-02): the request line's source columns, their pairing check and the filtered
            // source index; the line stays insert-only under the write guard; filters and keys unchanged.
            // Re-measured for A1-3 (plan API-10, No.161 §2): the order's Cancelled status and cancellation-facts check, the line's
            // cancelled share inside the shares check, and the write guard's allow-list for exactly those columns; filters and
            // keys unchanged.
            // Re-measured for A2a-1 (plan TEK-01/API-20): the order's money snapshot columns and their check, the line's price
            // columns (precisions) and their check (a unit price ≥ 0: an explicit 0 is a price); the write guard's allow-list is
            // unchanged (prices never move); filters and keys unchanged.
            // Re-measured for A2a-1c (ALS-D72): the order's nullable UnitPriceScale and its own check (beside a currency, 0..28);
            // the money check, the write guard's allow-list, filters and keys unchanged.
            // Re-measured for A2a-2 (ALS-D38, ALS-D75): the line's two maturity surcharge columns (precisions) and their own check
            // (one, beside a unit price above 0); the price check, the write guard's allow-list, filters and keys unchanged.
            // Re-measured for A2a-5a (ALS-D3, Fable F-015): the order's two nullable approval columns (ApprovedBy 128, ApprovedAtUtc)
            // and their own check (both or neither, only on an approved order, approver ≠ CreatedBy in binary collation); the
            // write guard's allow-list, filters and keys unchanged.
            // Re-measured for A3 API-30 (ALS-D33, ALS-D21): the append-only tenant table PurchaseOrderLineDeliverySlices (its DbSet,
            // the CompanyId query filter, key, checks, the line and order foreign keys, indexes); the write guard refuses any update
            // or delete of a slice; the other filters and keys unchanged.
            // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (80279983… -> c8164526…).
            ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
             + "Persistence/PurchasingDbContext.cs"] =
                // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (c8164526… -> fb48d6ed…).
                // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (fb48d6ed… -> e4ca95ad…).
                // W8 EK24 FNS-D105: finalized invoice outbox event joins the existing typed check; tenant filters unchanged.
                "a0539ef259a0441471c8066dde39dcb000ffdf0cde4be01867a14ef69728fef4",
            // ERP-000G / AT-065: company-scoped like Organization; every tenant row (blobs, documents, versions, links,
            // attempts) and AuditEvent carries the CompanyId filter, and the write guard refuses any update or delete.
            // A standard entry, not an exception.
            // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (c607c37a… -> 7bb4fea2…).
            ["src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/"
             + "Persistence/DocumentsDbContext.cs"] =
                // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (7bb4fea2… -> 89c71d17…).
                "afa87503dc789bdefba1722ee853744d934b78c7c6f28a14d42bf3ebf98df17b",
            // Re-measured for MFG-API (#313): the ADR-011 command log (CommandRecords) is mapped here, company-
            // scoped with the CompanyId filter like every other tenant row.
            // MFG-5 (#303): company-scoped like Organization; every tenant row and AuditEvent carries the
            // CompanyId filter. A standard entry, not an exception.
            // Re-measured for MFG-PLAN-EDIT: the append-only PlanRevisions / PlanRevisionInputs tables, each with the
            // same CompanyId query filter and a company-leading key and FK to the plan.
            // Re-measured for immutable Product/Settings technical snapshots on creation and revision inputs.
            // M01.2: plan and immutable revision cut lines carry the same CompanyId filter and company-leading keys.
            // Üretim U1-b: the append-only PlanParameterSnapshots / PlanParameterSnapshotValues carry the same CompanyId
            // filter and company-leading keys and FKs; the closure decision gains source, line and mass-balance columns.
            // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (d828adc1… -> 74bc9c39…).
            ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/"
             + "Persistence/ManufacturingDbContext.cs"] =
                // Üretim U2-a: the append-only PlanRejections carry the same CompanyId filter and company-leading key and FK;
                // the plan row admits only its cancellation columns beside the approval; the order index becomes unique.
                // Üretim U2-b1: the plan and revision rows gain the nullable process spec columns and their checks (no new
                // table, filter, key or bypass).
                // Üretim U2-d: the plan row gains the nullable preparer column (CreatedBy) and its check (no new table,
                // filter, key or bypass).
                // Üretim U2-b: the plan row gains the nullable sales order link columns, the link revision and their
                // all-or-nothing check (no new table, filter, key or bypass).
                // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (74bc9c39… -> 77a98ea3…).
                // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (77a98ea3… -> 848771df…).
                // Re-fingerprinted for tren-1 W7: owner append-only schema changes; company filters retained; K3 review pending.
                // URT-D33/D34: permit only the first Open-to-Cancelled facts; cancelled history remains immutable.
                // No query filter or tenant bypass changed; measured by the house generator; K3 review pending.
                "aee92d72ed7571c1ba4e2e5a2c8b51948608615e5e74de2829f625cb89cb90dc",
            // FIN-T04a (#305): company-scoped like Organization; every tenant row and AuditEvent
            // carries the CompanyId filter. A standard entry, not an exception.
            // Re-measured for DEC-FIN-014: nullable POS fact columns on ReceiptDrafts and MoneyEvents; no new table,
            // query filter or key. Re-measured for DEC-FIN-016: the customer instrument, its movements and the
            // risk read are company-scoped with the same CompanyId filter; the command log and money events gain
            // the instrument link and the collection kind.
            // Official-invoice Work deliveries have their own CompanyId filter, company-leading
            // batch/receipt relationships and an immutable-write guard.
            ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
             + "Persistence/FinanceDbContext.cs"] =
            // F2b: InvoiceCancellations / InvoiceCancellationItems carry the CompanyId filter and company-leading keys;
            // CK_OpenItems_Status admits 3 = Cancelled (973395d3… -> 578e4f20…).
            // F3a / API-20: PaymentAllocations / PaymentAllocationStates carry the CompanyId filter, company-leading
            // keys to ManualBankPayments and OpenItems, and a release-only write guard (578e4f20… -> 95024798…).
            // API-23: PaymentReversalProposals / OutgoingMoneyEventReversals carry the CompanyId filter, company-leading
            // keys to ManualBankPayments and OutgoingMoneyEvents, and an append-only reversal guard (95024798… -> 61b34a3d…).
            // API-30: CashAccounts / CashAccountCommands carry the CompanyId filter and company-leading keys;
            // CK_CashAccounts_Decision (maker≠decider, BIN2), UX_CashAccounts_LiveName and a decision-only write
            // guard (95024798… -> e9c49087…).
            // API-30 passivation/reactivation: CashAccounts gains the change columns, CK_CashAccounts_StateChange,
            // the decision check and live-name filter widened to the passive status, and a write guard that also
            // admits one change proposal or decision per save; the filter and keys are unchanged (e9c49087… -> c4668c43…).
            // Stack merge of API-23 (#1095) and API-30 (#1109): both sets of entities, filters and write guards
            // are present; recomputed on the merged context (61b34a3d… + c4668c43… -> fe8918ac…).
            // F-005 D1 (FNS-D17…FNS-D21): OfficialSalesInvoices / OfficialSalesInvoiceCommands carry the CompanyId filter,
            // company-leading keys, unique ETTN/official number per company and CK_OfficialSalesInvoices_Approval
            // (approver ≠ recorder) (95024798… -> 6bb4c498…).
            // #66 6860586175 (PDF first): OfficialSalesInvoiceReservations carries the CompanyId filter, a company-leading
            // alternate key and an append-only write guard; each invoice references its reservation (CompanyId, Id),
            // restrict; UX_OfficialSalesInvoices_PdfSha256 keeps one PDF content per company (6bb4c498… -> b43a052c…).
            // FNS-D39: the three unique indexes are filtered to live records ([Status] IN (1, 2)) and
            // CK_OfficialSalesInvoices_Approval admits Rejected/Withdrawn with their actor fields (b43a052c… -> e342b071…).
            // Stack merge of API-23 + API-30 (#1095, #1109) and F-005 D1 (#1102): all three sets of entities, filters
            // and write guards are present; recomputed on the merged context (fe8918ac… + e342b071… -> a17bc2d3…).
            // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
            // Re-fingerprinted for tren-1 wave 2 trial integration: the Finance team slice (FIN-S1..S5) combined; reviewed tokens unchanged (659f985f... -> 9aa2e8a8...).
            // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (9aa2e8a8… -> 17ccb0c9…).
            // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (17ccb0c9… -> 22cd5638…).
            "04cc265618df3fd57b00d90ab8434fa600cfa80304d8fd3d3ac26157b26bc40a",
            // ACC-3 (#298): company-scoped like Organization; every tenant row and AuditEvent
            // carries the CompanyId filter. A standard entry, not an exception.
            // Re-measured for ACC-V0a: the ADR-011 command log (Commands) is mapped here, company-scoped with the
            // CompanyId filter like every other tenant row; the period's preparer/approver columns. ACC-V1 adds
            // company-scoped normal journal draft headers/lines and immutable lifecycle evidence.
            ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/"
             + "Persistence/AccountingDbContext.cs"] =
                // Re-measured for B-04a: the posting account mapping versions and rows, both company-scoped with
                // the CompanyId filter and company-leading keys; the write guard admits only activation facts.
                // Re-measured for B-04b: the source document admission rows (company-scoped with the CompanyId
                // filter and company-leading keys); the write guard admits only the status facts of the admission.
                "f880ce05d79af381784d25fb626dba3608a38aef3c851c5479248a5584608ebe",
            // INV-2 (#293, ADR-INV-001 option A): company-scoped like Organization; every tenant
            // row and AuditEvent carries the CompanyId filter. A standard entry, not an exception.
            // Re-measured for AT-037: StockCounts and StockCountDecisions, each with the CompanyId query filter and a
            // company-leading key, and the CountAdjustment movement kind.
            // Re-measured for INV-AL-1: the append-only AllocationChanges history (CompanyId query filter,
            // company-leading key) and the allocation write guard admitting an AllocatedKg resize.
            // Re-measured for the ST-02 scrap write-off: StockScraps and StockScrapDecisions, each with the CompanyId
            // query filter and a company-leading key, and the Scrap movement kind (CK_Movements_Kind 1..7).
            // Re-measured for R20 same-company transfers: every transfer aggregate row stays company-filtered and
            // company-keyed, while movement kinds 8/9 remain inside Inventory's reviewed write guards.
            // Re-measured for INV label classification: LabelRow.Classification is persisted beside the existing
            // Version/UpdatedAtUtc fields; no query filter, company-leading key or table boundary changes.
            // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (f26be8e0… -> 8a04bad7…).
            ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
             + "Persistence/InventoryDbContext.cs"] =
                // Re-fingerprinted for B-01: plan-input demand check, ReleasedKg column/check/write guard, active plan-input unique index; provider tokens unchanged.
                // Re-fingerprinted for SAT-D262: AverageCostLedger table (company filter, company-leading unique position key, write guard); provider tokens unchanged.
                // Re-fingerprinted for B-03: CK_LabelTransformations_Kind widened to (1, 2, 3) through a named constant; no filter, key or table change.
                // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): STK transfer receipts/overage/loss, future supply conversions, substitute allocations and supplier return exit mapped on the company-filtered context (8a04bad7… -> d88129d1…).
                // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (d88129d1… -> 550ed6dc…).
                "d26780b1b1aca90c2828f05c63f93cb19e85363ace73a205ab89270818f78520",
            ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
             + "Persistence/ProductDbContext.cs"] =
                "f9d6ed01001b1cbfcb725a21daaae4c9f393564e804f1f1293ce9e3717128b04",
            // AT-018: the Product-owned price-list aggregate is company-filtered independently from the
            // existing Product card context; every durable row and audit event carries CompanyId.
            ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
             + "PriceLists/ProductPriceListDbContext.cs"] =
                // Re-fingerprinted for MOD-03: the company-filtered product tax classification (classes, version and update facts the only moving columns) and its insert-only command answers join the write guard.
                // Re-fingerprinted for SAT-D149 (EK20): the item type moves to the SHARED card (ProductCardItemTypes keyed by the
                // product, no company filter like the global card it extends); its own write guard refuses a delete, a write
                // outside a resolved company or naming another acting company, and any column but the type and update facts.
                // The command answers stay company-filtered.
                // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): STK kg/m table imports (AddProductProfileWeightImports) mapped on the company-filtered context (7559b730… -> 99a56c77…).
                // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (99a56c77… -> 5d363ce3…).
                "7012abe06a432095c78802391de8d9e5b7ba4b7206e67147d1b118932fbc6b83",
            // JV03: every Work snapshot, inbox and outbox row is company-filtered and company-keyed;
            // AuditEvent uses the same resolved company fence and the write guard refuses foreign rows.
            // Re-fingerprinted for B-24: dispatcher cursor/delivery tables are company-filtered, company-keyed and write-guarded.
            [ReviewedWorkDbContextPath] =
                // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                "221a99fd21364e8a08599feaa77fced3b4e3dca88cdbf6d80c7fae963c155db2",
            ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/"
             // Re-measured for DEC-CAR-003: the terminal command-name check admits the Product deactivate/reactivate
             // commands beside activate; no query filter, key or table change.
             + "Persistence/GroupSharedCommandDbContext.cs"] =
                "7c195a463c5280391d9dfcd0f0d75773dc0d184252ab49f1da3f84afc0c7288c"
        };

    // Accepted module initializers own one exact Migrate/EnsureCreated provider branch each.
    // The Identity reconciler owns one parameterized row/range-lock query. The shared RCSI
    // readiness probe owns one reviewed read-only command. Exact source fingerprints and API
    // counts keep these from becoming general database bypass exemptions.
    private const string ReviewedInventoryDefiniteShipmentParticipantPath =
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
        + "Persistence/InventoryDefiniteShipmentParticipant.cs";
    private const string ReviewedInventoryCustomerReturnParticipantPath =
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
        + "Persistence/InventoryCustomerReturnParticipant.cs";
    private const string ReviewedLogisticsCustomerReturnRuntimePath =
        "src/backend/GoymenERP.Api/LogisticsRuntime/LogisticsCustomerReturnRuntime.cs";
    private const string ReviewedLogisticsCustomerReturnStorePath =
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/"
        + "Persistence/LogisticsCustomerReturnStore.cs";
    private const string ReviewedLogisticsCustomerReturnReadPortsPath =
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/"
        + "Persistence/LogisticsCustomerReturnReadPorts.cs";
    private const string ReviewedInventoryTransferOutgoingDocumentProofAdapterPath =
        "src/backend/GoymenERP.Api/LogisticsRuntime/InventoryTransferOutgoingDocumentProofAdapter.cs";
    private const string ReviewedLogisticsTransferOutgoingDocumentStorePath =
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/"
        + "Persistence/LogisticsTransferOutgoingDocumentStore.cs";
    private const string ReviewedInventoryGoodsReceiptParticipantPath =
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
        + "Persistence/InventoryGoodsReceiptParticipant.cs";
    private const string ReviewedInventoryManufacturingRealizationParticipantPath =
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
        + "Persistence/InventoryManufacturingRealizationParticipant.cs";
    private const string ReviewedPurchaseAcceptanceParticipantPath =
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
        + "Persistence/PurchaseAcceptanceParticipant.cs";
    private const string ReviewedPurchaseAcceptanceSourcePath =
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
        + "Persistence/PurchaseAcceptanceSource.cs";
    private const string ReviewedPurchaseGoodsReceiptHostPath =
        "src/backend/GoymenERP.Api/PurchasingRuntime/PurchaseGoodsReceiptHost.cs";
    private const string ReviewedFinanceOpenItemSourceAdmissionServicePath =
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
        + "FinanceOpenItemSourceAdmissionService.cs";
    private const string ReviewedFinanceInvoiceOpenItemAdmissionServicePath =
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
        + "FinanceInvoiceOpenItemAdmissionService.cs";
    private const string ReviewedFinanceInvoiceOpenItemCancellationServicePath =
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
        + "FinanceInvoiceOpenItemCancellationService.cs";
    private const string ReviewedFinanceOpenItemReadServicePath =
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
        + "FinanceOpenItemReadService.cs";
    private const string ReviewedAccountingStorePath =
        "src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/"
        + "Persistence/AccountingStore.cs";
    private const string ReviewedApprovedAccountingSourceCatalogStorePath =
        "src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/"
        + "Persistence/ApprovedAccountingSourceCatalogStoreV1.cs";
    private const string ReviewedManufacturingInventoryInputIssueHostPath =
        "src/backend/GoymenERP.Api/ManufacturingRuntime/ManufacturingInventoryInputIssueHost.cs";
    private const string ReviewedManufacturingPlanAllocationHostPath =
        "src/backend/GoymenERP.Api/ManufacturingRuntime/ManufacturingPlanAllocationHost.cs";
    private const string ReviewedManufacturingInventoryReturnOutputHostPath =
        "src/backend/GoymenERP.Api/ManufacturingRuntime/ManufacturingInventoryReturnOutputHost.cs";
    private const string ReviewedProductRuntimeExtensionsPath =
        "src/backend/GoymenERP.Api/ProductRuntime/ProductRuntimeExtensions.cs";
    private const string ReviewedProductImportSqlCommandRunnerPath =
        "src/backend/GoymenERP.Api/ProductRuntime/ProductImportSqlCommandRunner.cs";
    private const string ReviewedProductImportParticipantPath =
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
        + "Persistence/ProductImportParticipant.cs";
    private static readonly IReadOnlyDictionary<string, (string ContextTypeName, int ExpectedCount)>
        ReviewedRuntimeContextConstructions =
            new Dictionary<string, (string ContextTypeName, int ExpectedCount)>(StringComparer.Ordinal)
            {
                [ReviewedInventoryDefiniteShipmentParticipantPath] = ("InventoryDbContext", 1),
                [ReviewedInventoryCustomerReturnParticipantPath] = ("InventoryDbContext", 1),
                [ReviewedLogisticsCustomerReturnRuntimePath] = ("LogisticsDbContext", 2),
                [ReviewedLogisticsCustomerReturnStorePath] = ("LogisticsDbContext", 13),
                [ReviewedLogisticsCustomerReturnReadPortsPath] = ("LogisticsDbContext", 2),
                [ReviewedInventoryGoodsReceiptParticipantPath] = ("InventoryDbContext", 1),
                [ReviewedInventoryManufacturingRealizationParticipantPath] = ("InventoryDbContext", 1),
                [ReviewedPurchaseAcceptanceParticipantPath] = ("PurchasingDbContext", 1),
                [ReviewedPurchaseAcceptanceSourcePath] = ("PurchasingDbContext", 3),
                // MOD-04 (F2a) invoice admission: the one counted site is a target-typed refusal result.
                [ReviewedFinanceInvoiceOpenItemAdmissionServicePath] = ("FinanceDbContext", 1),
                // Target-typed DTO/cursor/ReadResult return sites are conservatively counted as context
                // constructions. FI04 has four exact sites; all database access is tenant-filtered EF/DI.
                [ReviewedFinanceOpenItemReadServicePath] = ("FinanceDbContext", 4),
                ["src/backend/GoymenERP.Api/Shell/FinanceOfficialInvoiceImportHost.cs"] =
                    ("FinanceDbContext", 2),
                // tren-1 W5 fix: target-typed JSON-options/result/row/event/issue sites are conservatively counted; each context is
                // DI-resolved and none is constructed here. Exact source counts, pending the owning teams' K3 review.
                ["src/backend/GoymenERP.Api/PurchasingRuntime/"
                 + "PurchaseSupplierReturnHost.cs"] = ("PurchasingDbContext", 1),
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceCreditAccountService.cs"] = ("FinanceDbContext", 4),
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceOpeningBalanceService.cs"] = ("FinanceDbContext", 6),
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Bootstrap/"
                 + "FounderRecoveryService.cs"] = ("IdentityDbContext", 4),
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/"
                 + "LogisticsWeighingDifferenceStore.cs"] = ("LogisticsDbContext", 11),
                // The conservative target-typed scanner includes DTO/ReadResult return sites in the
                // reviewed approval service; 21 is its exact source count, not a new DbContext writer.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceOfficialInvoiceImportApprovalServiceV1.cs"] = ("FinanceDbContext", 21),
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceOfficialInvoiceImportPendingApprovalReadServiceV1.cs"] = ("FinanceDbContext", 12),
                [ReviewedProductImportSqlCommandRunnerPath] = ("AuditDbContext", 1),
                // tren-1 wave 3 integration (stok_lojistik): the future supply conversion and the supplier return exit each build ONE
                // InventoryDbContext on the owner's open connection/transaction under the server-resolved company; the demand cap and the
                // suggestion reader count target-typed result sites only. Exact source counts. Pending the owning team's K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/InventoryGoodsReceiptFutureSupplyConversion.cs"] = ("InventoryDbContext", 1),
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/InventorySupplierReturnExitParticipant.cs"] = ("InventoryDbContext", 1),
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/InventoryAllocationDemandCap.cs"] = ("InventoryDbContext", 2),
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/InventoryAllocationSuggestionReader.cs"] = ("InventoryDbContext", 1),
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/"
                 + "Bootstrap/FounderBootstrapService.cs"] = ("IdentityDbContext", 3),
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/SalesQuoteAndEInvoicePolicyReads.cs"] = ("SettingsDbContext", 1),
                // tren-1 wave 2 trial integration (STK future allocation): the writer and the engine count target-typed result/option
                // sites only; the read/release port also constructs one InventoryDbContext on the Purchasing owner's open connection and
                // transaction under the server-resolved company (release session, no own connection). Pending the owning team's K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryFutureAllocationWriter.cs"] = ("InventoryDbContext", 1),
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryFutureAllocationEngine.cs"] = ("InventoryDbContext", 2),
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryFutureAllocationPorts.cs"] = ("InventoryDbContext", 3),
                // tren-1 wave 1: target-typed refusal/plan results only (no new DbContext writer); exact source counts.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceInvoiceAcceptanceService.cs"] = ("FinanceDbContext", 2),
                // SAT-D66: the multi-company binding write builds ONE Settings context per other company over the request
                // context's connection and transaction, each with that company's own company context, audit collector and
                // audit interceptor (the PurchaseAcceptanceParticipant shape), so every row passes its company's write fence.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/SettingsSalesDiscountLimitAdministration.Bindings.cs"] = ("SettingsDbContext", 1),
                // Tren-1 W8; K3 review pending. One module-owned context bound to the explicit resolved company and caller
                // connection/transaction; owner query filters and transaction lifetime checks remain in force, and disposal does
                // not commit the borrowed transaction.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingPeriodStatusReadSession.cs"] = ("AccountingDbContext", 1),
                // Tren-1 W8; K3 review pending. One Documents-owned context from the existing factory options and resolved
                // company; its own EF command transaction and owner query filters remain in force.
                ["src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentBrandingService.cs"] = ("DocumentsDbContext", 1),
                // Tren-1 W8; K3 review pending. One module-owned context bound to the explicit resolved company and caller
                // connection/transaction; owner query filters and transaction lifetime checks remain in force, and disposal does
                // not commit the borrowed transaction.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinancePeriodCarryForwardSession.cs"] = ("FinanceDbContext", 1),
                // Tren-1 W8; K3 review pending. Two Identity-owned contexts for the explicit global CLI confirmation/read and
                // Serializable recovery write; no company-scoped data or grants are admitted.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/LocalAccount/LocalTwoFactorOperatorRecoveryServiceV1.cs"] = ("IdentityDbContext", 2),
                // Tren-1 W8; K3 review pending. One module-owned context bound to the explicit resolved company and caller
                // connection/transaction; owner query filters and transaction lifetime checks remain in force, and disposal does
                // not commit the borrowed transaction.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryGoodsReceiptReversalParticipant.cs"] = ("InventoryDbContext", 1),
                // Tren-1 W8; K3 review pending. One Inventory-owned read context bound to the captured company and the caller
                // connection/transaction; only historical owner links are read.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryHistoricalSalesLinksV1.cs"] = ("InventoryDbContext", 1),
                // Tren-1 W8; K3 review pending. One module-owned context bound to the explicit resolved company and caller
                // connection/transaction; owner query filters and transaction lifetime checks remain in force, and disposal does
                // not commit the borrowed transaction.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryManufacturingCostGenerationParticipant.cs"] = ("InventoryDbContext", 1),
                // Tren-1 W8; K3 review pending. One module-owned context bound to the explicit resolved company and caller
                // connection/transaction; owner query filters and transaction lifetime checks remain in force, and disposal does
                // not commit the borrowed transaction.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryManufacturingCostReadSession.cs"] = ("InventoryDbContext", 1),
                // Tren-1 W8; K3 review pending. One Inventory-owned context bound to the validated company and transaction;
                // physical balance evidence stays inside the Inventory owner.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryNegativeStockValidationGuard.cs"] = ("InventoryDbContext", 1),
                // Tren-1 W8; K3 review pending. One module-owned context bound to the explicit resolved company and caller
                // connection/transaction; owner query filters and transaction lifetime checks remain in force, and disposal does
                // not commit the borrowed transaction.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventorySubcontractDeliveryParticipant.cs"] = ("InventoryDbContext", 1),
                // Tren-1 W8; K3 review pending. One module-owned context bound to the explicit resolved company and caller
                // connection/transaction; owner query filters and transaction lifetime checks remain in force, and disposal does
                // not commit the borrowed transaction.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsSubcontractDeliveryReadSession.cs"] = ("LogisticsDbContext", 1),
                // Tren-1 W8; K3 review pending. One module-owned context bound to the explicit resolved company and caller
                // connection/transaction; owner query filters and transaction lifetime checks remain in force, and disposal does
                // not commit the borrowed transaction.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingSubcontractCompletionReadSessionV2.cs"] = ("ManufacturingDbContext", 1),
                // Tren-1 W8; K3 review pending. One module-owned context bound to the explicit resolved company and caller
                // connection/transaction; owner query filters and transaction lifetime checks remain in force, and disposal does
                // not commit the borrowed transaction.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseReceiptAdjustmentParticipant.cs"] = ("PurchasingDbContext", 1),
                // Tren-1 W8; K3 review pending. One Purchasing-owned read context on the caller open connection/transaction;
                // company-filtered immutable acceptance evidence, no commit of the borrowed transaction.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseReceiptCostProof.cs"] = ("PurchasingDbContext", 1),
                // Tren-1 W8; K3 review pending. One module-owned context bound to the explicit resolved company and caller
                // connection/transaction; owner query filters and transaction lifetime checks remain in force, and disposal does
                // not commit the borrowed transaction.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesSubcontractInvoiceClaimReadSession.cs"] = ("SalesDbContext", 1),
                // Tren-1 W8; K3 review pending. One module-owned context bound to the explicit resolved company and caller
                // connection/transaction; owner query filters and transaction lifetime checks remain in force, and disposal does
                // not commit the borrowed transaction.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesSubcontractServiceOrderReadSession.cs"] = ("SalesDbContext", 1),
            };
    private const string ReviewedManualBankPaymentServicePath =
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
        + "ManualBankPaymentService.cs";

    private static readonly IReadOnlyDictionary<string, ReviewedRuntimeDatabaseBypassSource>
        ReviewedRuntimeDatabaseBypassSources =
            new Dictionary<string, ReviewedRuntimeDatabaseBypassSource>(StringComparer.Ordinal)
            {
                // Tren-1 W7; K3 review pending. FNS-D102: namespace only for DbException and borrowed SERIALIZABLE isolation/rollback; no connection creation, command text or filter bypass.
                // W8 measured SQL repair: SqlClient only classifies the exact EF-wrapped 1205 refusal; no new SQL access.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceTreasuryTransaction.cs"] =
                    new("584ec666fcf65e5e04a8bf7d8bb352c02de474ff0a8ad8df833454f0cccd09bf",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["System.Data.Common"] = 1, ["Microsoft.Data.SqlClient"] = 1 }),
                // Tren-1 W8; K3 review pending. FNS-D70: SqlClient only classifies duplicate keys; CollateralTypeReads.FindAsync is an owner-filtered AsNoTracking helper, not DbSet.Find. Fresh company/permission checks remain.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/SettingsCollateralTypeAdministration.cs"] =
                    new("175c0ed8c182090bea46dbcf78548d75c6b5e30d1c5659fd0e67f9a8c8fa3f1d",
                        new Dictionary<string, int>(StringComparer.Ordinal) { [".FindAsync"] = 1, ["Microsoft.Data.SqlClient"] = 1 }),
                // Tren-1 W7; K3 review pending. SAT-D364: namespace only to recognize SQL unique-key 2601/2627 after owner SaveChanges; no connection, SQL command or filter bypass.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/SettingsCompanyPolicyAdministration.cs"] =
                    new("df502050059278a5cec35f1e854c8dcfc9980a3f27f383de1e84888e69cfa2ba",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["Microsoft.Data.SqlClient"] = 1 }),
                // Tren-1 W7; K3 review pending. KIM-D20/D21: one fixed parameterless MIN_ACTIVE_ROWVERSION visibility horizon prevents cursor loss; event rows remain explicitly ambient-company filtered and read-only.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/IdentityCompanySetupOutboxSource.cs"] =
                    new("15b1eec9c864f68a69026e1c7054c25d116fce74162179608eee5f870bef9441",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["SqlQueryRaw"] = 1 }),
                // Tren-1 W7; K3 review pending. KIM-D20/D21: one fixed parameterless MIN_ACTIVE_ROWVERSION visibility horizon prevents cursor loss; event rows remain explicitly ambient-company filtered and read-only.
                ["src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationCompanySetupOutboxSource.cs"] =
                    new("318f3ca2622f6d3b21aed9afe6ffddc4a5165416ce79496edb45a1b466e30662",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["SqlQueryRaw"] = 1 }),
                // Tren-1 W6 Audit ownership repair. K3 review pending: this namespace is used only for the borrowed
                // DbTransaction type. A fresh Audit-owned EF context validates every event's CompanyId and appends
                // inside that transaction; it neither commits nor owns the connection. No raw SQL/filter bypass.
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Services/AuditTransactionWriterV1.cs"] =
                    // Re-fingerprinted for tren-1 W7: owner transaction or response changes; existing API counts retained; K3 review pending.
                    new("58ed045d18a866febdc2eec5ad656323838c34d43ec7824ff98750927c088e29",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["System.Data.Common"] = 1 }),
                // SAT-S8 / KLN-D105. K3 review pending: the adapter uses the caller's DbTransaction type only,
                // binds existing owner locks/ports, and revalidates Sales authority. Sales and Inventory alone
                // write their own tables; no new command text, connection creation or tenant-filter bypass.
                ["src/backend/GoymenERP.Api/Endpoints/SalesFutureDemandReductionHostParticipant.cs"] =
                    new("97eeee2f68ff063ab6ac861d9e68b5d7e106848b329e8ce3d696f079e6e0ed66",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["System.Data.Common"] = 1 }),
                // Re-measured for C3: the Product owner's sales and stock eligibility runtimes are composed
                // here (no new database capability; MigrateAsync stays 2).
                // Re-measured for #316 on top of C3: ProductShellCapabilityProvider is registered here (a
                // singleton shell provider; no new database capability, MigrateAsync stays 2).
                // Re-measured for Inventory opening stock: the bounded Product stock-candidate reader/service are
                // DI-only additions over the existing Product read store; no new database capability, MigrateAsync stays 2.
                // Re-measured for Product history: the scoped Audit-owner adapter and Product history reader are
                // read-only DI additions; no new database capability, MigrateAsync stays 2.
                // Re-measured for DEC-CAR-003: the denied (unconfigured) Product service gains the deactivate and
                // reactivate members, which throw like its siblings; no new database capability, MigrateAsync stays 2.
                // Re-measured for DEC-PRD-010: the denied Product service gains the read-only duplicate-candidates
                // member, which throws like its siblings; no new database capability, MigrateAsync stays 2.
                // Re-measured for Q16 import: DI adds the owner preview/service and the host transaction runner;
                // initialization still owns exactly the same two migrations.
                // Re-measured for Product stock-display publication: one DI-only batch projection registration
                // is gated by the existing Product runtime flag; database capability counts stay unchanged.
                [ReviewedProductRuntimeExtensionsPath] =
                    new("37ce44db3bddf4a215feece2dc2b11ca7dbb46c2c57954bf0e6d71805829f514",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["MigrateAsync"] = 2 }),
                // Re-measured for PRD-UNIT-1: the Product participant gets Settings' unit-identity catalog
                // (GetService; unregistered stays fail-closed). SqlClient/SqlConnection counts unchanged.
                ["src/backend/GoymenERP.Api/ProductRuntime/ProductSqlCommandRunner.cs"] =
                    new("36ee56a13d31b3c4aea130b801db15b983768eacfc2f5827047059ead463ed36",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Microsoft.Data.SqlClient"] = 1, ["SqlConnection"] = 1 }),
                // Q16 / AT-020: one reviewed host runner borrows one SQL connection/transaction for Product,
                // canonical audit batch and the single idempotency terminal. It creates no context or provider.
                [ReviewedProductImportSqlCommandRunnerPath] =
                    new("bd3105e907778f1ca845fd4572a14f69fda5542da4f3c72869ec22dc40655b8d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Microsoft.Data.SqlClient"] = 1, ["SqlConnection"] = 1 }),
                // Product owner participant borrows the host connection/transaction; it creates no provider and
                // commits nothing. The central runner remains the only commit owner.
                [ReviewedProductImportParticipantPath] =
                    new("270d184afcea8a3070ce270eebdaf26da18d73852550731e40cb3985fd48af1c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["System.Data.Common"] = 1, ["DbConnection"] = 3 }),
                // AT-018: the price-list initializer owns one provider branch; normal runtime never creates
                // or drops databases and the reviewed source carries no general SQL surface.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
                 + "PriceLists/ProductPriceListRegistration.cs"] =
                    new(
                        // Re-fingerprinted for MOD-03: the named product tax classification service factory and its two port registrations (DI-only, the Settings category reader optional); the database surface and its counts are unchanged.
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): the kg/m table import service and the dimension weight source registered; no new provider branch (14f385e3… -> e1edccf5…).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (e1edccf5… -> 67464188…).
                        "e3e5e0624faab50caff7bcd7798ea417625d2d59fe5b83b7e94ef8bf7ea53728",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // AT-018: SqlClient is referenced only to classify unique-key conflicts after EF SaveChanges.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
                 + "PriceLists/ProductPriceListStore.cs"] =
                    new(
                        "0ec797ef39bd37ee97ffb8a945de0c7fd515a38d2ee6891914e6dac6e3e2362d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Services/GroupSharedAuditSession.cs"] =
                    new("efbf67c36b5848e6f2d05e989f3aac1636c7e202da94aee750757a43b33fccfe",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["System.Data.Common"] = 1, ["DbConnection"] = 1 }),
                ["src/backend/Modules/Audit/"
                 + "GoymenERP.Modules.Audit.Infrastructure/AuditModuleExtensions.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (9bcf2dd9… -> 36bd6641…).
                        // Re-fingerprinted for tren-1 W6: PLT audit ownership: register the Audit-owned enlisted batch participant; reviewed API counts unchanged (36bd6641... -> f8d2efb3...).
                        "cb3acd4c5c417a397c1c02a3dc92faa7798db2aca216165bd5eeb8d84f13adad",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // AI-2a (ADR-SEC-001): security event channel bootstrap; migrates its own schema like Audit.
                // AI-2a hardening: registers TimeProvider.System for server-clock retention (4fa27691… -> 308b6836…).
                // AI-2b: registers the scoped read service ISecurityEventQueryServiceV1 (DI only; same token counts)
                // (308b6836… -> e27292d7…).
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (e27292d7… -> 6927e4a0…).
                ["src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/"
                 + "SecurityEventsModuleExtensions.cs"] =
                    new(
                        "2079c8dcab0d862cbf230e919ddfb6b81800161cd169f1bd6f6009c42ec0433d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // AI-2a hardening: the security event store classifies a duplicate EventId (SqlException 2601/2627)
                // after SaveChanges and re-reads it under the company filter; no connection, command or raw SQL.
                ["src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/"
                 + "Writing/SecurityEventStore.cs"] =
                    new(
                        "53f6379121a52a5d6c1754c1f76cdf2bc800c7cbaa7d8092c69fdf2493acc95d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/"
                 + "Executions/SqlGroupSharedCommandTerminalStore.cs"] =
                    new(
                        "5ecf927a5505ff79b5d2f3d79e436dc674d1b98aa05db28c8ae6e6669ac9b301",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["DbConnection"] = 1
                        }),
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
                 + "Persistence/ProductDraftParticipant.cs"] =
                    // Re-measured for PRD-UNIT-1 (ÜRÜN): unit identities are resolved through Settings'
                    // catalog; System.Data.Common/DbConnection counts unchanged.
                    new(
                        "f59d7bbd85559638482f4caf718bff0bd5e596fc259424d8ac12553ba4f0d815",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["DbConnection"] = 3
                        }),
                ["src/backend/Modules/Idempotency/"
                 + "GoymenERP.Modules.Idempotency.Infrastructure/"
                 + "IdempotencyModuleExtensions.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (aeb8697c… -> 742e05c0…).
                        "742e05c0037e73cd15143eba3b5e29b2be2ba80c3d833fdbcf89ffda888324e2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (935e8a44… -> 7b8efa68…).
                ["src/backend/Modules/Identity/"
                 + "GoymenERP.Modules.Identity.Infrastructure/IdentityModuleExtensions.cs"] =
                    // Re-fingerprinted for the D2 active-role reader registration: the same scoped
                    // authorization service now also exposes a permission-independent role view.
                    // The database startup surface below is unchanged, which is what the API
                    // counts hold.
                    // Re-fingerprinted for SY-01/SY-02: one scoped registration of the read-only
                    // IIdentityAccessReadService; the database startup surface and its counts are unchanged.
                    // Re-fingerprinted for MOD-08: one scoped registration of the read-only IUserDirectoryReadPortV1,
                    // with its comment (f2b86575… -> a27b4290…); the database startup surface and its counts are unchanged.
                    // Re-fingerprinted for SAT-D66: the same directory also registered as the read-only
                    // IUserCompanyMembershipReadPortV1, with its comment (a27b4290… -> 355ade3a…); counts unchanged.
                    // Re-fingerprinted for ID-1: the LocalAccount options binding and the scoped local credential
                    // store, login service and session validator registrations (355ade3a… -> 935e8a44…); the database
                    // startup surface and its counts are unchanged.
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (7b8efa68… -> ce4693e5…).
                        // Re-fingerprinted for tren-1 W7: in-process owner registrations; activation flags unchanged; K3 review pending.
                        "49ecbeefa12e557ba7d4ca8f1146a222d84fd666a16e470c4f3d55463a28afbf",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1,
                            ["GetDbConnection"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                [ReviewedSqlServerRcsiReadinessProbePath] =
                    new(
                        "b6291e2db3fef669f312b3046fdc46d826992901a7a2ca7243d35bd0557c9c34",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["DbConnection"] = 1,
                            ["CreateCommand"] = 1
                        }),
                [ReviewedSqlServerRcsiToolProgramPath] =
                    new(
                        "5da18330229e541cd90e9463a181c88ce72bb821c43449f44673f761faabaf04",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["SqlConnection"] = 4
                        }),
                [ReviewedIdentityReconcilerPath] =
                    new(
                        "6c5f20ecb02e6803238c4234509ff557dd3e6da1f7a4f5c1c8d7c173a7d90751",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["FromSqlInterpolated"] = 1
                        }),
                // OrganizationModuleExtensions re-fingerprinted for the location module read registration: one
                // AddScoped line; its Migrate/EnsureCreated branch is unchanged.
                // Re-fingerprinted for the ORG master edit: one more AddScoped line (the master-data command service);
                // the Migrate/EnsureCreated branch and its token counts are unchanged.
                // Re-fingerprinted for the bounded location-display batch reader: one more scoped Organization-owned
                // read registration; the Migrate/EnsureCreated branch and its token counts are unchanged.
                // Re-fingerprinted for ORG-LOCATION-OPERATOR-CRUD-01A: one fail-closed eligibility port and one
                // company-scoped destination source registration; initialization behavior is unchanged.
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (7bea1ef2… -> 9b3cc6d8…).
                ["src/backend/Modules/Organization/"
                 + "GoymenERP.Modules.Organization.Infrastructure/"
                 + "OrganizationModuleExtensions.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (9b3cc6d8… -> 77ddeb88…).
                        // Re-fingerprinted for tren-1 W7: in-process owner registrations; activation flags unchanged; K3 review pending.
                        "606cf2f828b51ba3789ae30a2d747d378f8d51c392e80ddc3d281c92ab908ca1",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // INV-2 (#293): InitializeInventoryModuleAsync's one provider branch, as Organization.
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (bbfa521c… -> 1fa2b806…).
                ["src/backend/Modules/Inventory/"
                 + "GoymenERP.Modules.Inventory.Infrastructure/"
                 + "InventoryModuleExtensions.cs"] =
                    // Re-fingerprinted for DEC-SAL-005: one scoped registration of the Sales-declared allocation read
                    // port. Re-fingerprinted for ST-04: one scoped movement-timeline reader registration; the
                    // provider branch and its token counts are unchanged.
                    new(
                        // Re-fingerprinted for B-01: one scoped registration of the plan-input allocation read port; provider tokens unchanged.
                        // Re-fingerprinted for SAT-D262: one scoped registration of the versioned average-cost read port; provider tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): the stok wave-3 slices (transfer receipts, future supply conversion, supplier return exit, shortage warnings, substitute allocation) registered (1fa2b806… -> b9269aed…).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (b9269aed… -> ae06f965…).
                        // Re-fingerprinted for tren-1 W6: STK-S17/S18: register future-allocation commands, queries and the fail-closed schedule source; reviewed API counts unchanged (ae06f965... -> ea6ca888...).
                        "0949093725e151338c69bd98351c80027ce06dc931f0c21b292e0ce68c5c27a0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // LOG-5: the enlisted participant uses the host transaction's connection and
                // explicitly constructs one InventoryDbContext over that exact connection. The
                // source fingerprint and API counts keep this narrower than a general runtime
                // context-construction or direct-database exemption.
                [ReviewedInventoryDefiniteShipmentParticipantPath] =
                    new(
                        // Re-fingerprinted for B-01: a definite shipment refuses a non-Sales (plan-input) allocation; provider tokens unchanged.
                        "5c6f5106974dcf98dd6da309256d867bad49db9184cc9256358645106a84c93a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["DbConnection"] = 3
                        }),
                // Customer-return approval borrows the host's exact connection and transaction and constructs
                // one Inventory context; its source and token counts are pinned independently from shipment.
                [ReviewedInventoryCustomerReturnParticipantPath] =
                    new(
                        "1652ab5f2d4ed5317948e93bca102230ff2c6bd46af392eea745c2535d49e43b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["DbConnection"] = 3
                        }),
                // MFG inventory realization: the API host opens the Inventory transaction connection and
                // constructs exactly one ManufacturingDbContext over it. The exact fingerprint and three
                // API counts keep this reviewed bridge narrower than a general direct-database exemption.
                // Re-fingerprinted for SAT-D318: Inventory's physical-stock refusal carries its shortfall to the port only
                // when it names exactly this input issue; same token count (33399d74 -> 091796f7).
                // Re-fingerprinted for SAT-D318 / SINAN-CEVAP (8) Q5: the shortfall also carries Inventory's usable kg
                // as computed there, never recomputed here; same token count (091796f7 -> 63ba7632).
                [ReviewedManufacturingInventoryInputIssueHostPath] =
                    new(
                        "4a43e91c09e40fb989b3d5a15fea914b1150ab3afa2a21217a21271a8c7eab88",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["GetDbConnection"] = 1
                        }),
                // Üretim U3-a (URT-D21, B-01): the plan approval / cancellation and the order closure open one Manufacturing
                // transaction and enlist Inventory's plan-allocation participant on its connection; no context is built.
                // Re-fingerprinted for TAKIP-U3a (#1004): an ArgumentException inside the Inventory stage becomes a 503 host fault; same token count (b8fe85c2 -> 80c8184c).
                [ReviewedManufacturingPlanAllocationHostPath] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (80c8184c… -> 57a638d8…).
                        // Re-fingerprinted for tren-1 W6: URT-S4/S5: require released allocation provenance before a new label generation; reviewed API counts unchanged (57a638d8... -> da0cf179...).
                        // Re-fingerprinted for tren-1 W7: owner transaction or response changes; existing API counts retained; K3 review pending.
                        "a698b633056b55dbb589beb886880aa42c07a56e131bcd282bd107d747d44d37",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["GetDbConnection"] = 1
                        }),
                // Return/output uses the same reviewed borrowed-transaction boundary but obtains both the
                // Inventory connection and current parent-label truth before constructing one Manufacturing context.
                [ReviewedManufacturingInventoryReturnOutputHostPath] =
                    new(
                        // Re-fingerprinted for tren-1 wave 2 trial integration (URT-S16): output-class mapping refusal before any read and the remnant classification
                        // passed through; reviewed tokens and the one context construction unchanged (91e00eeb... -> 0e650b11...).
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (0e650b11… -> b745df6e…).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (b745df6e… -> 656d12ae…).
                        // tren-1 W5 (integration, uretim): a third GetDbConnection (guard count 2 -> 3, URT order cancellation/output
                        // correction on the same borrowed boundary). Exact source count; pending the owning team's K3 review.
                        // Re-fingerprinted for tren-1 W6: URT-S2: forward the explicit output-line reference to Inventory; reviewed API counts unchanged (656d12ae... -> 80abcca8...).
                        "9a5ad51a3cde5e6ef6cb2157ee33fa4aba15a465e4af3ba621d2fe2f4ee1c2b7",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["GetDbConnection"] = 3
                        }),
                // INV-2 (#293): the SqlClient namespace only to recognise a unique-key violation
                // (SqlException 2601/2627) after SaveChanges, as the Idempotency terminal store does.
                // No connection, command or raw SQL. Re-fingerprinted for #333 (#324 P3-1): a lot/label
                // primary-key collision reads, tenant-filtered and AsNoTracking, whether this company holds
                // the id (retry) or not (conflict); still no connection, command or raw SQL. Re-fingerprinted
                // for INV-OPEN-1 (T07 = A): the opening count is written through the same tenant-filtered
                // context in one save; SqlClient count unchanged (1).
                // INV-OPEN-1 part 2: the opening-count command reads through the tenant-filtered context and uses
                // the SqlClient namespace only to recognise a unique-key violation (SqlException 2601/2627) in a
                // catch filter, as the other module stores do. No connection, command or raw SQL.
                // Customer-owned opening adds only the reviewed owner-account validation branch; SqlClient count stays 1.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/InventoryOpeningCommandService.cs"] =
                    new(
                        "fd384d4cdc34ec642db71049b1dbd0180f2d317f38acfd3a6758d943d571bebb",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // Re-fingerprinted for ÜRÜN's P3 follow-up: the colliding lot/label id is read from the
                // key error itself (SqlException.Errors 2601/2627); SqlClient count unchanged (1).
                // Re-fingerprinted for INV-OPEN-1 part 2 on top of #341: the opening count's PK collision
                // uses the same key-error read ('PK_OpeningCounts'); SqlClient count unchanged (1).
                // Re-fingerprinted for Product-version provenance rollback isolation: the target-typed MaterialLabel
                // construction is explicit so it cannot be mistaken for InventoryDbContext; SqlClient count stays 1.
                // Customer-owned opening persists its immutable position context through the same store; count stays 1.
                ["src/backend/Modules/Inventory/"
                 + "GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryStore.cs"] =
                    new(
                // Re-fingerprinted for B-01: one internal audit helper for plan-input allocations; provider tokens unchanged.
                // Re-fingerprinted for B-03: the staged label carries its classification; provider tokens unchanged.
                // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): the stok wave-3 store partials (transfer receipts, physical allocation suggestions, substitute flag) over the same filtered context (a1b00519… -> c79a6bb3…).
                // stok_lojistik (STK-S6 / B-22c): one tracked-entity Balances.Local read so a second movement staged in the same unit of
                // work (acceptance then receipt shortage) sees the first one's added rows; filtered by the movement's CompanyId and
                // EntityState.Added in the same save (the InventoryAverageCostLedgerWriter shape). Pending the owning team's K3 review.
                "05a213e4c8138f675ab668925f16dc2f1f6fc3ab3bcd5fb8fdb4db32f2f426d0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                            [".Local"] = 1
                        }),
                ["src/backend/Modules/Inventory/"
                 + "GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryStore.Allocations.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): the substitute allocation flag and the physical demand cap over the same filtered context (6ea71ce1… -> 6b96062b…).
                        "6b96062b5d3965fc2eef46a23ed31eae1a49d8079fda9b7920b13194497cc349",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // INV-AL-1: the allocation change (resize in place or move to another label) references SqlException
                // only to classify a 2601/2627 unique-key race after SaveChanges. No connection, command, raw SQL or
                // DbContext construction.
                ["src/backend/Modules/Inventory/"
                 + "GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryStore.AllocationChanges.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): the substitute allocation flag over the same filtered context (7df810e1… -> 9249f5d0…).
                        "9249f5d039065ffdd00b13c7d88bb51e9de4931ed14dd54ea46f3348158c006b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // ACC-3 (#298): the Accounting initializer's one provider branch, as Organization.
                // Re-fingerprinted for ACC reversal: its lifecycle and proposal store are registered;
                // Ensure/Migrate counts remain unchanged. Re-fingerprinted for the approved Finance receipt
                // source binding provider registration; the database initialization surface is unchanged.
                // BLG-05b: re-fingerprinted for two read-only registrations (IAccountPlanMirrorReaderV1 ->
                // ApprovedAccountPlanMirrorReaderV1, NormalJournalDraftAccountReadServiceV1); no bypass,
                // Ensure/Migrate or initialization change (a09a1e08… -> 3783d2ed…).
                // B-04b: re-fingerprinted for the source document admission authority default, store and port
                // registrations; no bypass, Ensure/Migrate or initialization change (c97d6217… -> de4305d6…).
                // (B-04a: posting account mapping store, service and resolver port registrations.)
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (de4305d6… -> 2e19d8c0…).
                ["src/backend/Modules/Accounting/"
                 + "GoymenERP.Modules.Accounting.Infrastructure/"
                 + "AccountingModuleExtensions.cs"] =
                    new(
                        "b7b0655e783c3d3369dce8e0a0cdc7908da39047fa02c399b622378f4482dc98",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // ACC-3 (#298): the SqlClient namespace only to recognise a unique-key violation
                // (SqlException 2601/2627) after SaveChanges, as InventoryStore and the Idempotency
                // terminal store do. No connection, command or raw SQL. Re-fingerprinted for ACC-V1: the normal
                // journal draft and reversal lifecycles persist through EF and carry late authority fences;
                // SqlClient count remains unchanged (1). Re-fingerprinted for the #464 K3 fix (#485): one company-bound
                // EF read admits the reversal period before staging; no SQL, connection, context or filter bypass.
                [ReviewedAccountingStorePath] =
                    new(
                        "9753a303d40da5210fe67cb2f542edf6c293ebe318ddfaeed8e579d4b8df3368",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // ACC source-catalog store uses the tenant-filtered AccountingDbContext directly.
                // It has no raw SQL, connection or filter-bypass API; the exact source and its three
                // reviewed context construction sites are pinned together.
                [ReviewedApprovedAccountingSourceCatalogStorePath] =
                    new(
                        "59878830636538c33306793dea8074d7d4fe2e7507e2ed8b37af0585f80177e8",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // FIN-T04a (#305): the Finance initializer's one provider branch, as Organization.
                // Re-fingerprinted for DEC-FIN-016: the customer-instrument service registration; the provider
                // branch and its token counts are unchanged. Re-fingerprinted for the party ledger: one read-service
                // registration (no writer); the provider branch and its token counts are unchanged. Re-fingerprinted for
                // MOD-01: one read-port registration (the Sales-declared ISalesPartyRiskReadPortV1 over Finance's own open
                // items, no writer); the provider branch and its token counts are unchanged. Re-fingerprinted for F3a /
                // API-20: the payment allocation writer and read service registrations; token counts unchanged.
                // Re-fingerprinted for API-23: the payment reversal service registration and its two audit envelopes;
                // token counts unchanged.
                // Re-fingerprinted for API-30: the cash-account master service registration; token counts unchanged.
                // Re-fingerprinted for API-30 passivation: the four change envelopes and the zero-answering balance
                // reader (TryAddScoped); token counts unchanged (5e7d06a8… -> 79b587d8…).
                // Stack merge of API-23 (#1095) and API-30 (#1109): both registrations are present; recomputed on the
                // merged source, token counts unchanged (58ddb659… + 79b587d8… -> 13043002…).
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (114cd199… -> 32243ecc…).
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceModuleExtensions.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (32243ecc… -> 145799ea…).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (145799ea… -> 3cd51383…).
                        // tren-1 W5 fix: three read-port lambdas moved to named factories (ICompanyContext single registration); token counts unchanged (3cd51383… -> 46d85fde…).
                        // Re-fingerprinted for tren-1 W7: in-process owner registrations; activation flags unchanged; K3 review pending.
                        "981431ddbb7dc595d01e83269c049a3651861e5966369ff7e79a3f51cb540f9c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // FIN-T04a (#305): the SqlClient namespace only to recognise a unique-key violation
                // (SqlException 2601/2627) after SaveChanges, as InventoryStore and AccountingStore
                // do; here in the draft service, which is the module's only writer. No connection,
                // command or raw SQL.
                // Re-fingerprinted for DEC-FIN-014: the POS facts (gross, commission, server-computed bank net,
                // bearer) travel on the same draft save and record; SqlClient count unchanged. K3 5330956606: the
                // unallocated amount is redacted like every other amount for an actor without amount read.
                // UX-TECH-001: the receipt list resolves approved reversal relations in one tenant-filtered EF batch;
                // it adds no direct database API and the reviewed SqlClient count remains unchanged.
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/ReceiptDraftService.cs"] =
                    new(
                        "60f18042fcef1d300340fdfdc6c4bd34c689cbb5a11f8eec14a046ae9e62f7ff",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // DEC-FIN-016: the customer cheque / promissory-note writer references SqlException only to
                // classify a 2601/2627 unique-key race after SaveChanges (a race of two collections makes one
                // money event). No connection, command, raw SQL or DbContext construction. K3 5332370492: the
                // risk view reads each instrument's state as of the chosen business date; SqlClient count unchanged.
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/CustomerInstrumentService.cs"] =
                    new(
                        "7722a259c3c3bec24f88a61553a8326d9b0769a8b45ad165d4711186badd27b9",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // R47: the manual-payment writer references SqlException only to classify SQL
                // unique-key races after SaveChanges. Exact fingerprint/API count keeps this from
                // authorizing a connection, command, raw SQL or a DbContext construction.
                [ReviewedManualBankPaymentServicePath] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (b9b6ee9a… -> b4276752…).
                        // Re-fingerprinted for tren-1 W7: owner transaction or response changes; existing API counts retained; K3 review pending.
                        "a8029dd0d67d77eeb16ac3047b7e1b57d89f281633ccaa6e07d60d8687c311ee",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 wave 1 (FNS-D45…D52 expense payment): the writer stays on the tenant-filtered FinanceDbContext; SqlClient is
                // referenced only to classify 2601/2627 races after SaveChanges. No connection, command, raw SQL or bypass. K3 review.
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (df32d807… -> 7043f54a…).
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/ExpensePaymentService.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W7: owner transaction or response changes; existing API counts retained; K3 review pending.
                        "26828b64480b92a844fce7590413ed7100fc5727b164b7f8e06cc735e6a77de8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 wave 1 (FIN customer-return acceptance): same R47 shape; SqlClient classifies 2601/2627 races after
                // SaveChanges only. No connection, command, raw SQL or bypass. K3 review.
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (e39a8514… -> 9f8f8994…).
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/FinanceInvoiceAcceptanceService.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (9f8f8994… -> 8949362f…).
                        "8949362fef136f4fb3d386603649c42f5acb5c8d20d19efdcdcc8e5b28d3d45f",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 wave 1 (F-018 party credit limit): same R47 shape; SqlClient classifies 2601/2627 races after SaveChanges
                // only. No connection, command, raw SQL or bypass. K3 review.
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (f9edfa24… -> d99e0fc2…).
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/FinancePartyCreditLimitService.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (d99e0fc2… -> e31360ad…).
                        "e31360adde051e12f46f433cb831f044338a085dbd64368f18098ddcc3f4cb04",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 wave 1 (F-017 F17-1): the BuildingBlocks purchase line lock contract names DbConnection/DbTransaction only in
                // its binder signature; it opens nothing and runs no statement. K3 review.
                ["src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Concurrency/PurchaseLineSerializationLockContractsV1.cs"] =
                    new(
                        "dc0ef5743c031bd2c383eb461983ce52a19807a70e7574912565026b676c337c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["DbConnection"] = 1
                        }),
                // tren-1 wave 1 (F-017 F17-1): the host binder passes the caller's own open connection and transaction to the
                // Purchasing lock under the server-resolved company only; it refuses an unresolved company before any statement. K3 review.
                ["src/backend/GoymenERP.Api/Endpoints/PurchasingSalesLinkedSupplyHostPorts.cs"] =
                    new(
                        "8b805fa30f98b1f5a946f0d614c5b35e3c1653749e6603cacaa14000ff2c6a71",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["DbConnection"] = 1
                        }),
                // tren-1 wave 1 (F-017 F17-1): one fixed sp_getapplock statement (Exclusive, Transaction owner) on the bound
                // transaction; the company-scoped resource and the wait are parameters, never SQL text; a negative answer is refused
                // (fail-closed). No other command, no query-filter bypass. K3 review.
                ["src/backend/Modules/Purchasing/"
                 + "GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseLineSerializationLock.cs"] =
                    new(
                        "fa446f6f9d19b4d0400a7eb073bfde4993e0feb999483534df676ee70cbce642",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["DbConnection"] = 3,
                            ["CreateCommand"] = 1
                        }),
                // tren-1 wave 2 trial integration (SAT sales order line lock): the BuildingBlocks contract names DbConnection/DbTransaction only in
                // its binder signature; it opens nothing and runs no statement. Pending K3 review.
                ["src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Concurrency/SalesOrderLineSerializationLockContractsV1.cs"] =
                    new(
                        "ffe175cb4dab081ab559bf8bb89becbe502c0605fdeb2e3f16d6e40b10aeaf00",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["DbConnection"] = 1
                        }),
                // tren-1 wave 2 trial integration (STK future allocation): the host binder passes the caller's own open connection and transaction
                // under the server-resolved company only (sp_getapplock lock path). Pending K3 review.
                ["src/backend/GoymenERP.Api/Endpoints/InventoryFutureAllocationHostPorts.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (45cfb077… -> eb6a30d7…).
                        "eb6a30d781afc5a05d0d025fb3eb33654f1ba2b49da8c7d147c8901d0fcd8d57",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["DbConnection"] = 4,
                            ["CreateCommand"] = 1
                        }),
                // tren-1 wave 2 trial integration (FIN cash transfer): same R47 shape; SqlClient classifies 2601/2627 races after SaveChanges
                // only. No connection, command, raw SQL or bypass. Pending K3 review.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/CashTransferService.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W7: owner transaction or response changes; existing API counts retained; K3 review pending.
                        "e17c8b935548619c0094588563bd471a9c33b338f1a57a1caf60e48a9ee00ae8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 wave 2 trial integration (kimlik founder bootstrap): SqlClient classifies 2601/2627 races only; the counted constructions
                // are target-typed results. Pending K3 review.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Bootstrap/FounderBootstrapService.cs"] =
                    new(
                        "9dd6232849741c9f396415761baab66dd62c1441649fc25bd63f3c4c5f19d0aa",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 wave 2 trial integration (kimlik local credential): SqlClient classifies 2601/2627 races only. Pending K3 review.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/LocalAccount/SqlLocalCredentialCommandStore.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (4dcb30ad… -> cc4ff8c8…).
                        "cc4ff8c85374abce5c76d8594014c1565828900d0cb1ec6fb3295d9f298cf2c9",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 wave 2 trial integration (kimlik ID-3 assignment): SqlClient classifies 2601/2627 races only. Pending K3 review.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityAccessAssignmentService.Plumbing.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W6: PLT audit ownership: flush and discard the Audit owner batch in the existing transaction; reviewed API counts unchanged (da39ce74... -> 418f2f40...).
                        "bbb6d1a13710800065c8f8274e1b32b9461b93533d25dc49fad45fd666d95916",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 wave 2 trial integration (kimlik ID-3 / KIM-D17): two fixed statements on the caller's open EF transaction, parameters
                // only: sp_getapplock (Exclusive, Transaction owner) and a READCOMMITTEDLOCK count of the other company-scoped Admin
                // holders filtered by @companyId. This is a raw read outside the EF query filter. Pending K3 review (two reviews).
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityAdminLock.cs"] =
                    new(
                        "bbfad86c1173eb133eb693ef62f0afd540b170d5bb549dc0f6605a7714b34b96",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["CreateCommand"] = 4,
                            ["DbCommand"] = 2,
                            ["GetDbConnection"] = 1
                        }),
                // tren-1 wave 2 trial integration (STK future allocation): the contract names DbConnection/DbTransaction only in its port
                // signature; it opens nothing and runs no statement. Pending K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/InventoryFutureAllocationContractsV1.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): the future supply conversion port joins the contracts (32b1ac1d… -> dec5f299…).
                        // Re-fingerprinted for tren-1 W6: SAT-S8: expose the enlisted demand reduction contract; reviewed API counts unchanged (dec5f299... -> e86e0233...).
                        // W8 STK-S17: reject noncanonical version text; existing port signatures and API counts unchanged. K3 review pending.
                        "b1e1473c5d9d7799928263ab9f32c96106514a76d81ed9c1b553e361fce0c195",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["DbConnection"] = 1
                        }),
                // tren-1 wave 2 trial integration (STK future allocation): the release port binds one InventoryDbContext to the owner's open
                // connection and transaction (no own connection, no command, no bypass). Pending K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryFutureAllocationPorts.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W6: SAT-S8: expose the existing enlisted demand reduction session; reviewed API counts unchanged (938eaa5b... -> cac1ca20...).
                        "cac1ca20f7d2a71bd3db340c065596b95bb7d0137e7eb7310efbd6b41c3bc2a7",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["DbConnection"] = 2
                        }),
                // tren-1 wave 2 trial integration (STK future allocation): the writer uses the bound connection to check the owner transaction
                // and classifies 2601/2627 with SqlClient. Pending K3 review (NEEDS-SINAN item in the integration report).
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryFutureAllocationWriter.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): the future supply conversion at goods receipt over the same owner session (534220af… -> 2775fb6e…).
                        "2775fb6e719389e407f03ca9091feda7423bd1468cc85f1124a28e159a7d51e7",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["GetDbConnection"] = 2,
                            ["DbConnection"] = 2,
                            ["System.Data.Common"] = 4
                        }),
                // tren-1 wave 2 trial integration (STK future allocation): engine helpers only; no provider token. Pending K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryFutureAllocationEngine.cs"] =
                    new(
                        "73702690ac154502d831fd30c5d81cf8e99f6cbace2a39a66d620cfa5c8bf676",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // tren-1 wave 2 trial integration (SAT-D262 average cost ledger): one tracked-entity .Local lookup in the same save. Pending K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryAverageCostLedgerWriter.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (39fa1418… -> 15227b1f…).
                        // tren-1 W5 (integration, stok_lojistik): a second '.Local' read (guard count 1 -> 2). Exact source count;
                        // pending the owning team's K3 review.
                        "15227b1ff6f2bbb2c961a9b27e3b388d872156ebfaa1db8bee38bedfb0412737",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".Local"] = 2
                        }),
                // tren-1 wave 2 trial integration (URT monthly scrap prices): System.Data.Common is used for the DbException catch only. Pending K3 review.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingStore.ScrapPrices.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (5e8f6e1e… -> c4f362af…).
                        "c4f362af6c5013c0b25f98966db0ac173397ba10c3048dbbc4541c48aff95c05",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1
                        }),
                // tren-1 wave 2 trial integration (AI-3): .Find is an in-memory tool catalog lookup, not EF. Pending K3 review.
                ["src/backend/GoymenERP.Api/Ai/AiAskService.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (26545122… -> 557774c7…).
                        "557774c7885403ec99126fcc147b4e2417e6e83d2dd604d265d6140ed7b42b7d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".Find"] = 1
                        }),
                // tren-1 wave 3 integration (stok_lojistik, physical demand cap and allocation suggestions): tenant-filtered owner reads;
                // an empty API set plus exact fingerprint authorizes only their target-typed result constructions. Pending K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/InventoryAllocationDemandCap.cs"] =
                    new(
                        "548ba9a9dc3a331d1a17a09eb55a486c04b40eaeee57e11f71fba7331e779982",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/InventoryAllocationSuggestionReader.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (a02d01af… -> b53e0794…).
                        "b53e07945ae6245aa65dd5b1729a39273d333ccef9187fa04212d3ef6e25eaf7",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // tren-1 wave 3 integration (stok_lojistik, F017-c future supply conversion): the Application port only names the owner's
                // borrowed connection/transaction types (EnlistAsync); no provider call. Pending the owning team's K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/"
                 + "InventoryGoodsReceiptFutureSupplyContractsV1.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W7: owner transaction or response changes; existing API counts retained; K3 review pending.
                        "6f3542a3bc38536db84e5db5b91e6abb9756fb3638e3312bb2a481efb5deb7f0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["System.Data.Common"] = 1,
                        ["DbConnection"] = 1
                        }),
                // tren-1 wave 3 integration (stok_lojistik, F017-c): the conversion session builds one InventoryDbContext on the Purchasing
                // owner's open connection and transaction under the server-resolved company (the goods-receipt participant shape); no own
                // connection. Pending the owning team's K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/InventoryGoodsReceiptFutureSupplyConversion.cs"] =
                    new(
                        "fd16f9ec61c3a016aac251e94322549dc3f62ac0cd0e4703c2e0231bfd577179",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["System.Data.Common"] = 1,
                        ["DbConnection"] = 2
                        }),
                // tren-1 wave 3 integration (stok_lojistik, physical demand cap): one GetDbConnection binds the Sales line serialization
                // locks to the same filtered context's transaction. Pending the owning team's K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/InventoryPhysicalAllocationDemandCapWriter.cs"] =
                    new(
                        "e626d7226902866b05757d7fbd3c9ba88ec1d5ede21eba85fd7543ce7fa9fd69",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["GetDbConnection"] = 1
                        }),
                // tren-1 wave 3 integration (stok_lojistik, supplier return exit): the participant builds one InventoryDbContext on the
                // owner's open connection and transaction under the server-resolved company; SqlClient classifies key violations only
                // (the goods-receipt participant shape). Pending the owning team's K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/InventorySupplierReturnExitParticipant.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (36116eed… -> 95e9ff7c…).
                        "a1c12b96cb7e9091bc04ae375040ed8b4fc04457001bcf180102e18e0d0263e6",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["System.Data.Common"] = 1,
                        ["Microsoft.Data.SqlClient"] = 1,
                        ["DbConnection"] = 3
                        }),
                // tren-1 wave 3 integration (stok_lojistik, kg/m table): .FindAsync is ProductProfileWeightTableService.FindAsync over the
                // company-filtered context, not an EF DbSet.Find. Pending the owning team's K3 review.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
                 + "PriceLists/ProductDimensionService.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (1a96c566… -> 917f76d5…).
                        "917f76d56262c646943d23540c306f9eecbf035fe77fbeae18c7ef309cb2e115",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        [".FindAsync"] = 2
                        }),
                // tren-1 wave 3 integration (ALC-S16 goods-receipt batch): mirrors PurchaseGoodsReceiptHost; the batch host joins the owner
                // participants over the borrowed connection/transaction (one GetDbConnection, DbConnection/System.Data.Common for the bound
                // transaction type). Pending the owning team's K3 review.
                ["src/backend/GoymenERP.Api/PurchasingRuntime/PurchaseGoodsReceiptBatchHost.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): stok merge: the batch host also passes Inventory's future supply port to each label's participants (conflict resolution, K3 review pending) (3f16d9b2… -> 47d40498…).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (47d40498… -> f64f8304…).
                        // Re-fingerprinted for tren-1 W7: batch prelocks share the caller connection; GetDbConnection 1 to 5; K3 review pending.
                        "8212b69e653e1d7ea8fed5688f4dc4f58e5c4c1cf6b2fc9bcdc1ab463e4e6d7a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["GetDbConnection"] = 5,
                        ["DbConnection"] = 1,
                        ["System.Data.Common"] = 2
                        }),
                // tren-1 wave 3 integration (FIN-S2 party collaterals): same R47 shape as CashTransferService; SqlClient classifies 2601/2627
                // races after SaveChanges only; the counted constructions are target-typed results. Pending the owning team's K3 review.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinancePartyCollateralService.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (a23d1eed… -> 68315d3a…).
                        "ff97f2ec411a72cc3ae7ca57038d0e94447db856da2423dbd09cee8de08629bb",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 wave 3 integration (OPEN-021 / NS-12 idempotency retention purge): fixed parameterized purge statements over the
                // command-execution rows across companies; the hosted runner is off by default (Idempotency:Retention:Purge:Enabled) and
                // skipped in OpenAPI generation. Cross-company raw SQL: Pending the owning team's K3 review.
                ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/"
                 + "Retention/SqlIdempotencyRetentionPurgeStore.cs"] =
                    new(
                        "5a5b37ff0a619ba32a2deeee5981a50bf54c124652762b01f6967d1216b7a6dd",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["Microsoft.Data.SqlClient"] = 1,
                        ["SqlConnection"] = 2,
                        ["CreateCommand"] = 2
                        }),
                // tren-1 wave 3 integration (ALC-S16): .FindAsync is the batch store port lookup (orderId, batchId), not an EF DbSet.Find.
                // Pending the owning team's K3 review.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Application/"
                 + "PurchasingService.GoodsReceiptBatch.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (00ee5787… -> 5c81176d…).
                        // Re-fingerprinted for tren-1 W7: owner transaction or response changes; existing API counts retained; K3 review pending.
                        "4394742cb265fb028007b29af06f25548dd6d531f4e3e5940b738373567feae2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        [".FindAsync"] = 3
                        }),
                // tren-1 wave 3 integration (ALC-S16): SqlClient classifies 2601/2627 races only. Its cross-company batch-id collision probe
                // (IgnoreQueryFilters) is NOT authorised here; it stays red for the owning team's K3 tenant-isolation review.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/PurchaseGoodsReceiptBatchStore.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W6: W5 correction: replace the unauthorized filter bypass with a company-bound collision lookup; reviewed API counts unchanged (c7baa6ff... -> ad37942b...).
                        "d8f149c014a8c0323888478cecc57a91e2b9a9caceef61670355995da6020c08",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 W5 fix (Purchasing future-allocation link): DbConnection/DbTransaction only bind the Inventory release session to the
                // request's open Purchasing transaction; no command text, no new connection. Pending the owning team's K3 review.
                ["src/backend/GoymenERP.Api/Endpoints/"
                 + "PurchasingFutureAllocationLinkHostParticipant.cs"] =
                    new(
                        "0f2bd73f7b6ce4bacc129937a4eacd827dad7e1af279ee5271a8fae796132186",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["System.Data.Common"] = 1,
                        ["DbConnection"] = 2
                        }),
                // tren-1 W5 fix (supplier return): GetDbConnection/DbConnection hand the open Purchasing transaction to the Inventory exit
                // participant (one commit); the counted construction is a target-typed metadata record. Pending the owning team's K3 review.
                ["src/backend/GoymenERP.Api/PurchasingRuntime/"
                 + "PurchaseSupplierReturnHost.cs"] =
                    new(
                        "8ca8d7a1044b0d53cfd02deb5b13892cca5bdb991d4ca010e414734fe5cedd22",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["System.Data.Common"] = 1,
                        ["GetDbConnection"] = 1,
                        ["DbConnection"] = 1
                        }),
                // tren-1 W5 fix (credit accounts): SqlClient classifies 2601/2627 races only; the counted constructions are target-typed
                // JSON options/rows/issues over the DI context. Pending the owning team's K3 review.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceCreditAccountService.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W6: FIN-SC47: reject an undefined credit group after authority validation and before request hashing; reviewed API counts unchanged (256ab3b0... -> bf0d7546...).
                        "8c3cd1d3609ee51f45ce933e387e199e5e27a10da084b8982a3ef3864b072d21",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 W5 fix (opening balances): SqlClient classifies 2601/2627 races only; the counted constructions are target-typed
                // JSON options/rows/issues over the DI context. Pending the owning team's K3 review.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceOpeningBalanceService.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W7: owner transaction or response changes; existing API counts retained; K3 review pending.
                        "b264220af4b1147e09e305d325aa3ae1fc3d62fca63d580b4c585664a4c0801c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 W5 fix (founder recovery): System.Data.Common is the provider exception type only; the counted constructions are
                // target-typed security events and refusal results. Pending the owning team's K3 review.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/"
                 + "Bootstrap/FounderRecoveryService.cs"] =
                    new(
                        "f6b250878159c4f90df3fabe1caf78850a73a0465a6d75f0857b3e2d7dc79c70",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["System.Data.Common"] = 1
                        }),
                // tren-1 W5 fix (Logistics outbox source): SqlQueryRaw reads the fixed, parameterless visibility horizon; the rows themselves
                // are read through the company-filtered context with an explicit CompanyId. Pending the owning team's K3 review.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/"
                 + "Outbox/LogisticsDefiniteShipmentOutboxEventSource.cs"] =
                    new(
                        "fa7de0eb89420961c0b62289989c263a844ddffd45a0c580a8ba40c5dd827170",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["SqlQueryRaw"] = 1
                        }),
                // tren-1 W5 fix (weighing difference): SqlClient classifies 2601/2627 and 1205 only; the counted constructions are
                // target-typed results/rows over the DI context. Pending the owning team's K3 review.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/"
                 + "Persistence/LogisticsWeighingDifferenceStore.cs"] =
                    new(
                        "6a8f49845400b0d6380441734e2b68434e8004cdf639615752ece8be239d433b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 W5 fix (supplier return): .FindAsync is the return store port lookup (orderId, returnId), not an EF DbSet.Find.
                // Pending the owning team's K3 review.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Application/"
                 + "PurchasingService.SupplierReturn.cs"] =
                    new(
                        "520c4c6c7c309707ffb369575fcf00f9f280555785611dc80d883fe273449b13",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        [".FindAsync"] = 3
                        }),
                // tren-1 W5 fix (supplier return): SqlClient classifies 2601/2627 races only. Its cross-company return-id collision probe
                // (IgnoreQueryFilters) is NOT authorised here; it stays red for the owning team's K3 tenant-isolation review.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/PurchaseSupplierReturnStore.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W6: W5 correction: replace the unauthorized filter bypass with a company-bound collision lookup; reviewed API counts unchanged (e7e33daa... -> e9eab045...).
                        "55e8a86217760de4e1ffc1d6cc7a49ffc3f44347b374bd890d31ccaa9a9639ad",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        ["Microsoft.Data.SqlClient"] = 1
                        }),
                // tren-1 W5 fix (reference lists): .FindAsync is ReferenceListReads.FindAsync over the company-filtered Settings context,
                // not an EF DbSet.Find. Pending the owning team's K3 review.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/SettingsReferenceListAdministration.cs"] =
                    new(
                        "645792d26be3431d04d263c09db02d9f54135b1b5346745dcff63fb170034577",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        [".FindAsync"] = 1
                        }),
                // tren-1 W5 fix (reference lists): .FindAsync is ReferenceListReads.FindAsync over the company-filtered Settings context,
                // not an EF DbSet.Find. Pending the owning team's K3 review.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/SettingsReferenceListReaderV1.cs"] =
                    new(
                        "90362beaa729c2508589757d8d3bc1f14cb481638a710f937ed9eb6dd0c7b49d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        [".FindAsync"] = 1
                        }),
                // tren-1 W5 fix (work-center capacity): .FindAsync is the work-center reference port lookup (companyId, workCenterId),
                // not an EF DbSet.Find. Pending the owning team's K3 review.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/SettingsWorkCenterCapacityAdministration.cs"] =
                    new(
                        "0e9dbcffcbf5539e459b77c5042e1aa050106313a181c7acb8feb9a5c6b9fa74",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                        [".FindAsync"] = 1
                        }),
                // tren-1 wave 3 integration: construction-only surfaces (target-typed sites); no provider token. Pending K3 review.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceOfficialSalesInvoiceReadPortV1.cs"] =
                    new(
                        "ee6d419229193cf4d3ebfac7f030927d2ffb81058b6a2032661d6013324a6572",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinancePartyCreditPanelService.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (ff17932e… -> 6a44e4bb…).
                        "152e087591c2378e207e78d7d567330262a08380645a0425e7cec6e87b60b2c2",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // tren-1 wave 2 trial integration: construction-only surfaces (target-typed sites); no provider token. Pending K3 review.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialSalesInvoiceImportDocumentsV2.cs"] =
                    new(
                        "33db841532f5c744f378e11f57fe395c747781720c588e7bdef723e1906d904a",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/PartyLedgerReadService.cs"] =
                    new(
                        "7579c62ff7d75135f7a8f203edd8f9eee498aab2b8a6c8b35577e45695d794f9",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/SalesQuoteAndEInvoicePolicyReads.cs"] =
                    new(
                        "a880d3c603a76c4ae310bf09e16cd460f3ced8331c975ff8f026c885ec88d0b5",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryLabelDispositionReader.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): the substitute allocation and supplier return exit dispositions (b0cf25ca… -> aac2077a…).
                        "b3b32ebc94095ef5583f140953409b263406b52aab78d8a9afa9e48d011b7231",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // F-03: allocation writes stay on the tenant-filtered FinanceDbContext. SqlClient is
                // referenced only to classify 2601/2627 races after SaveChanges; there is no direct
                // connection, command or raw SQL surface.
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/FinanceAllocationService.cs"] =
                    new(
                        // W8: pure receipt allocation policy preserves error priority before both capacity fences; same single SqlClient race classification. K3 review pending.
                        "38a75b25f2435bd7fefa4fa45225f656205f7662bec7904f5d080c578bdcbb52",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // F3a / API-20: the payment allocation writer has the F-03 allocation shape. SqlClient is referenced
                // only to classify 2601/2627 races after SaveChanges; no connection, command, raw SQL or bypass.
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/FinancePaymentAllocationService.cs"] =
                    new(
                        // Re-fingerprinted for API-23: a reversed payment is refused with its own code
                        // (finance_source_already_reversed) through one tracked read; token counts unchanged.
                        // W8: pure allocation error priority precedes availability fences; same single SqlClient race classification. K3 pending.
                        // W8 DEC-FIN-006: require the tenant-filtered original outgoing payment fact; no new bypass or SqlClient occurrence. K3 review pending.
                        "7d6cbf203442603600642948c83f671ae9ea230a5aa4df1ee31a289dfd8af348",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // API-23: the payment reversal maker-checker writer has the same shape as the allocation writer. SqlClient
                // is referenced only to classify 2601/2627 races after SaveChanges; no connection, command, raw SQL or bypass.
                // Re-fingerprinted for the #66 round-20 reversal date rule: one PolicyRefusal arm
                // (finance_reversal_before_payment); same SqlClient use (a2b0ef1a... -> 11231d88...).
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/PaymentReversalProposalService.cs"] =
                    new(
                        "703f493f14b5fd332d1ce258813a042b0d7a90fe59d5e21cea0ecb4072dd0fc4",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // F-005 D1 (FNS-D17…FNS-D21): the official sales invoice service writes and reads through the tenant-filtered
                // FinanceDbContext only. SqlClient is referenced only to classify 2601/2627 races after SaveChanges; no
                // connection, command, raw SQL or bypass. #66 6860586175: the reservation, the one-PDF-per-company check
                // and the owner read use the same filtered context (362c03de… -> abc98b65…). FNS-D39: reject and withdraw
                // use the same filtered context and race classification (abc98b65… -> a6f808c3…).
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialSalesInvoiceServiceV1.cs"] =
                    new(
                        "7ad45f56c7c70c7f951530fb407c4af9e3a42d0031df86ed94a6cf4f3df12f65",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // F3a / API-20 reads use only the tenant-filtered FinanceDbContext through EF/DI.
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/FinancePaymentAllocationReadService.cs"] =
                    new(
                        "11c5db6eef3c3c73e0bac2f2c6f91b1a7ff5300bd5e39b4c3f592b7d3d7fb5da",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // F-03 reads use only the tenant-filtered FinanceDbContext through EF/DI. The exact
                // fingerprint and empty API inventory keep direct SQL and filter-bypass surfaces closed.
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/FinanceAllocationReadService.cs"] =
                    new(
                        "9cfe9429c78722de63658d7c37a52b587d35061fcade4895720e11f2eee9d1b8",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // F-03 source admission uses the tenant-filtered FinanceDbContext. SqlClient is
                // referenced only to classify 2601/2627 races after SaveChanges; there is no direct
                // connection, command, raw SQL or query-filter bypass surface.
                // Re-fingerprinted for tren-1 wave 2 trial integration (FIN-S5 D5): one more target-typed issue list; same SqlClient use (cb022616... -> 8df7bc91...).
                [ReviewedFinanceOpenItemSourceAdmissionServicePath] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (8df7bc91… -> e7d0d213…).
                        "de1afdfe2c221e5bb9fe9eaa49f9ae1763aaf8b5ec127f3de6f8b9f6b79f8c62",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // MOD-04 (F2a) invoice admission uses the tenant-filtered FinanceDbContext like F-03's source
                // admission. SqlClient is referenced only to classify 2601/2627 races after SaveChanges; there is
                // no direct connection, command, raw SQL or query-filter bypass surface.
                [ReviewedFinanceInvoiceOpenItemAdmissionServicePath] =
                    new(
                        "a2c5dcae6c456d691639780401714b000dcbbbb9c27de5aef978b6c343153174",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // F2b invoice cancellation: the same shape as the F2a admission — tenant-filtered FinanceDbContext,
                // SqlClient only to classify 2601/2627 races after SaveChanges; no connection, command, raw SQL or bypass.
                // F3a / API-20: an active payment allocation also refuses (one more tenant-filtered EF read;
                // 2ae7b299… -> f9ef0286…).
                // F2b K3 takip: the corrupt-state refusals use their own code (2abc569a… -> 5f81e19b…).
                // #1000 takip: class XML summary now states the confirmed-release rule (comment only; 5f81e19b… -> 51e12213…).
                [ReviewedFinanceInvoiceOpenItemCancellationServicePath] =
                    new(
                        "c399f24befdd07edfcca0947fd4bde624016765567e9dc8003d9be0dbe8693d0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // FI04 adds a separate read-only scoped service. No raw SQL, connection, save or filter bypass.
                // F2b: maps the stored Cancelled status (2f7afd0e… -> 0fa87abe…).
                [ReviewedFinanceOpenItemReadServicePath] =
                    new(
                        "0fa87abe22bfc10cf6b0b04f8f6f207a82ec3dc073608b54c8bb4c0227c16f8a",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // FS0 (#312): the bank-account master is the module's second writer; the SqlClient
                // namespace only to recognise a unique-key violation (SqlException 2601/2627) after
                // SaveChanges, as ReceiptDraftService does. No connection, command or raw SQL.
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/FinanceBankAccountService.cs"] =
                    new(
                        "dfa5d4d6015d24d6fe1095c4b61e05b4ada96de0d900ecb2068855384d821ac8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // API-30: the cash-account master writer; the SqlClient namespace only to recognise a
                // unique-key violation (SqlException 2601/2627, UX_CashAccounts_LiveName) after SaveChanges,
                // as FinanceBankAccountService does. No connection, command or raw SQL.
                // Re-fingerprinted for API-30 K3 review B P3-5: a rejection's audit context names the decider
                // as rejectedBy instead of approvedBy; same token count (5540ddc5… -> ff441d02…).
                // Re-fingerprinted for API-30 passivation/reactivation: the change proposal, approval and rejection
                // through the same fence, replay, attempt and audit path, and the balance seam; still no
                // connection, command or raw SQL, same token count (ff441d02… -> 72827fa6…).
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (72827fa6… -> eccbaab5…).
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/FinanceCashAccountService.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W7: owner transaction or response changes; existing API counts retained; K3 review pending.
                        "88673cddf3d04e897a9232759fcc3bb95de70f32c103bcb7b376570f15c13244",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // FIN-T06: the reversal workflow is the module's third writer. SqlClient is used only
                // to recognise unique-key violations (2601/2627) after SaveChanges; there is no
                // connection, command, raw SQL or query-filter bypass.
                // Re-fingerprinted for the #66 round-20 reversal date rule applied to FIN-T06: one PolicyRefusal arm
                // (finance_reversal_before_receipt); same SqlClient use (5c391ebb... -> 70ca258a...).
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/ReversalProposalService.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W7: owner transaction or response changes; existing API counts retained; K3 review pending.
                        "ef6cf6d39630820b37d90a6f77f726b842c4183eff4064164bcfc8439324b9b9",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // Approved official-invoice upload/approval persists through the tenant-filtered FinanceDbContext.
                // SqlClient is referenced only to classify 2601/2627 races after SaveChanges; no direct connection,
                // command, raw SQL or query-filter bypass is introduced.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceOfficialInvoiceImportApprovalServiceV1.cs"] =
                    new(
                        "573d1b13dafb963b6521a099600130a759711d016f22c62529c3da0b2227a1af",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // Observation payload construction and hash restoration use no direct database API.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceOfficialInvoiceImportWorkObservationFactoryV1.cs"] =
                    new("e8da7c446f2d0764288b16910eed001aa6ef40dee5de11097967b6bc41c24daa",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // The host and two read services resolve/use the tenant-filtered FinanceDbContext only through DI/EF.
                // Empty API inventories plus exact fingerprints keep direct SQL, connection and filter-bypass surfaces closed.
                ["src/backend/GoymenERP.Api/Shell/FinanceOfficialInvoiceImportHost.cs"] =
                    new("d6eb42c3009778d83872e20dfdbb2112c50230eff5893f1568930ed6de8bdf10",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // F-005 D1: the official sales invoice host resolves FinanceDbContext only through DI; no direct database API.
                // #66 6860586175: the PDF check asks the finance.official-sales-invoice owner (26e7f8d1… -> cbdeeed2…).
                // FNS-D39: Reject maps to Approve and Withdraw to the new Withdraw permission (cbdeeed2… -> 863996fc…).
                ["src/backend/GoymenERP.Api/Shell/FinanceOfficialSalesInvoiceHost.cs"] =
                    new("067738040467806edac14e7cc3d2fa40677619d4e0484fc12f988784c7e749d7",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceOfficialInvoiceImportPendingApprovalReadServiceV1.cs"] =
                    new("1ffef3861109082971714f8f76bbb3e7abf871e7f4094ce370bc6597dd8da77a",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "FinanceOfficialInvoiceImportReadPortV1.cs"] =
                    new("51fd30746f0033550fa005a9692c54daa321c98d70900f3d9fcba3708ec83a08",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // MFG-5 (#303): the Manufacturing initializer's one provider branch, as Organization.
                // Re-measured for MFG-API (#313): the command surface registrations (mass verifier and
                // command service); the one provider branch is unchanged (EnsureCreated 1, Migrate 1).
                // Re-fingerprinted for MFG-PLAN-EDIT: one scoped registration of the plan-revision store over the same
                // ManufacturingStore; the provider branch and its token counts are unchanged.
                // Re-fingerprinted for Üretim U2-a: one scoped registration of the plan lifecycle store (rejection,
                // cancellation) over the same ManufacturingStore; the provider branch and its token counts are unchanged.
                // Re-fingerprinted for Üretim U2-b: the plan sales order link store and the open-supply read for Sales over
                // the same ManufacturingStore; the provider branch and its token counts are unchanged.
                // Re-fingerprinted for Üretim U5-b1: the binding read for Sales (SAT-D175) over ManufacturingDbContext; the
                // provider branch and its token counts are unchanged.
                // Re-fingerprinted for URT-D42 (3): one scoped registration of the read-only closed-order list port over
                // the same ManufacturingStore; the provider branch and its token counts are unchanged.
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (6bd0c296… -> 1ea7b3bf…).
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/"
                 + "ManufacturingModuleExtensions.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (1ea7b3bf… -> 1655138f…).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (1655138f… -> dcbeabc2…).
                        // Re-fingerprinted for tren-1 W6: URT-S5: compose the Inventory free-quantity reader for released-plan revision; reviewed API counts unchanged (dcbeabc2... -> 638d7cd6...).
                        // Re-fingerprinted for tren-1 W7: in-process owner registrations; activation flags unchanged; K3 review pending.
                        "3e212ce64c940ab2dfa71052c952fc7ffc4e3edc0227107cf9cf7e35c8ec80c0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // MFG-5 (#303): the SqlClient namespace only to recognise a unique-key violation
                // (SqlException 2601/2627) after SaveChanges, as the other module stores do. No
                // connection, command or raw SQL.
                // Re-measured for MFG-API (#313): the same single SqlClient use now also recognises 547 on the
                // named composite work-center FK (manufacturing_work_center_not_found); still recognition only,
                // no connection, command or raw SQL. Re-fingerprinted for MFG-BAL: the Decimal18Scale3Limit rename
                // (FİNANS #342 INFO-3: one neutral name for every decimal(18,3) column bound); SqlClient count
                // unchanged (1). Re-fingerprinted for MFG-PLAN-EDIT: the Draft revision append and the history read,
                // company-resolved through the filtered context; no connection, command or raw SQL; SqlClient count
                // unchanged (1).
                // Re-fingerprinted for technical snapshot persistence and stored-plan restoration; SqlClient count
                // remains recognition-only and unchanged (1). M01.2 adds lossless cut-line plan/revision persistence
                // through the same filtered context; the reviewed SqlClient API count remains unchanged.
                // Üretim U1-b: the approval persists the captured parameter snapshot and the close decision its source, line
                // and mass-balance percent, through the same filtered context; SqlClient count unchanged (1).
                // Üretim U2-a: the rejection and cancellation writes, the plan rebuilt with its rejections, cancellation and
                // one order, and the unique (company, plan) order index's 2601/2627 recognised by name as the plan's own
                // rule; still recognition only, no connection, command or raw SQL; SqlClient count unchanged (1).
                // Üretim U2-b1: the process spec is written on the plan and revision rows and restored with them through the
                // same filtered context; SqlClient count unchanged (1).
                // Üretim U2-d: the preparer is written on the plan row and read back with the revisions through the same
                // filtered context; SqlClient count unchanged (1).
                // Üretim U2-b: the sales order link change (plan row, link revision, audit, command record in one save) and
                // the bounded open-supply read by sales order, through the same filtered context; SqlClient count
                // unchanged (1).
                // URT-D42 (3): the read-only page of closed orders (closedAtUtc desc, id desc, a company- and
                // ordering-bound cursor) rebuilt through the same filtered context and RestoreOrder; it writes nothing;
                // SqlClient count unchanged (1).
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (59a56981… -> 165dbfdd…).
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingStore.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (165dbfdd… -> aa1e4254…).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (aa1e4254… -> 14691ba6…).
                        // Re-fingerprinted for tren-1 W6: URT-S4/S5: persist released-plan revisions while preserving order and approval history; reviewed API counts unchanged (14691ba6... -> 4dcbc99e...).
                        "e49f4fb8bc094d5fca5ff76a60a578408e38c9ae4583062b5f3ff2964d640d0c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // LOG-4a: the Logistics initializer's one provider branch, as Sales.
                // Re-fingerprinted for AT-024: one scoped registration of the Sales-declared delivery read port; the
                // provider branch and its token counts are unchanged.
                ["src/backend/Modules/Logistics/"
                 + "GoymenERP.Modules.Logistics.Infrastructure/"
                 + "LogisticsModuleExtensions.cs"] =
                    // Re-fingerprinted for DEC-SAL-005: one scoped registration of the Sales-declared loading read port;
                    // the provider branch and its token counts are unchanged. Re-fingerprinted for AT-025: the operations
                    // work list's progress reader, bindings and service registrations; token counts unchanged.
                    // Re-fingerprinted for I01.2: transfer outgoing-document store, proof source, existing-permission
                    // bindings and service registrations; provider branch and token counts are unchanged.
                    // Re-fingerprinted for MOD-06 / API-30 / API-31: one scoped owner implementation of the two Sales-declared
                    // dispatch reads and its two port registrations (DI-only); provider branch and token counts are unchanged.
                    new(
                        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): the unload weighing slice registered (c695b524… -> 1069a748…).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (1069a748… -> 0e7a30b4…).
                        "3fbc504da02c04d583bc93883042deca14c5fbdeb630cda3fecd9666cbb2d045",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // LOG-5: shipment and delivery share the Logistics transaction boundary. Only shipment
                // borrows the current Inventory connection/participant; delivery never resolves Inventory.
                ["src/backend/GoymenERP.Api/LogisticsRuntime/"
                 + "LogisticsRealizationTransactionRunner.cs"] =
                    new(
                        "b902db0d9c232f51c603b539b2995c2d1d6d0d4494fd205bf3354ec86d8f8252",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["GetDbConnection"] = 1,
                            ["DbConnection"] = 1
                        }),
                // Customer-return approval owns the Serializable Logistics transaction and lends its
                // exact connection/transaction to the reviewed Inventory participant.
                [ReviewedLogisticsCustomerReturnRuntimePath] =
                    new(
                        "15797a87960f3a99595356dcbfefa9e0a60740b09a4f3e9772e96879dbf5bf0e",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["GetDbConnection"] = 1,
                            ["DbConnection"] = 1
                        }),
                // The module source and Sales read ports use only tenant-filtered Logistics contexts.
                // Re-fingerprinted for SAT-D8 / API-10: the Sales approved-returns aggregate is projected to an anonymous
                // row in SQL and mapped to the contract after the read (the record projection was not translatable on
                // SQL Server); same tenant-filtered context, filter, grouping and order; no capability token added.
                [ReviewedLogisticsCustomerReturnReadPortsPath] =
                    new(
                        "a885ae19839f97d1de8ee6be5e12c7307c2d189e7c5d5e8dad9cb81abc8bbb6e",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // The customer-return store recognises SQL uniqueness and its tracked aggregate; both
                // capabilities remain exact-source and exact-count reviewed.
                [ReviewedLogisticsCustomerReturnStorePath] =
                    new(
                        "06808d6fe56293d16cf04d41fdff20505dcc0ed1862471ceae600e48a30c91df",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                            [".Local"] = 1
                        }),
                // I01.2 proof lookup reads through the company-filtered Inventory transfer context; FindAsync is
                // exact-source and exact-count reviewed and performs no bypass or write.
                [ReviewedInventoryTransferOutgoingDocumentProofAdapterPath] =
                    new(
                        "658c983d5a1cace101562f83ac38bbd359c35f5aab929b63ce5f1cbb762c1321",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".FindAsync"] = 1
                        }),
                // I01.2 uses SqlClient only to classify replay-safe uniqueness and serialization conflicts; it
                // opens no direct connection or command outside the company-filtered Logistics context.
                [ReviewedLogisticsTransferOutgoingDocumentStorePath] =
                    new(
                        // Re-fingerprinted for the read-phase deadlock closure: a SQL 1205 raised by a read inside the
                        // Serializable section maps to ConcurrentChange; no new database API and SqlClient count unchanged.
                        "9cad954953c033049b2b879ba47a16a6c9cde69fd0449673e1f6b4228ea2b918",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Application/LogisticsCustomerReturnService.cs"] =
                    new(
                        "7a5111e3a85b068ea6b8d8f27f8f5a2df23b290b9aa8cfb8a12ca2f471089c6d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".FindAsync"] = 2
                        }),
                // LOG-4a: the SqlClient namespace only to recognise a unique-key violation (SqlException
                // 2601/2627) after SaveChanges, as the Sales store does. No connection, command or raw SQL.
                ["src/backend/Modules/Logistics/"
                 + "GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsStore.cs"] =
                    new(
                        // Re-fingerprinted for AT-053: the weighing is saved in the instruction's one save and
                        // replayed at its share position; SqlClient count unchanged.
                        // Re-fingerprinted for AT-055: the dispatch-note draft is saved in the instruction's one save and
                        // replayed at its share position, plus the Sales order read; SqlClient count unchanged.
                        // Re-fingerprinted for the AT-055 lifecycle (#518): approve/cancel/e-İrsaliye steps written in the same save,
                        // the company series moved under its token, every step replayed in order; SqlClient count unchanged.
                        // Re-fingerprinted for the cross-instruction aggregate deadlock closure: SQL 1205 is mapped through
                        // the existing replay-safe wrapper chain; no new database API and SqlClient count unchanged.
                        // Re-fingerprinted for SAT-D103 / SAT-D111 (K3-REVIEW-A P1-1 b): one untracked, company-resolved sum of
                        // a Sales line's shipped total through the filtered context; no new database API, SqlClient count unchanged.
                        // Re-fingerprinted for SAT-D216 / SAT-D222: a shipment share records its actor (RecordedBy) in the same
                        // save; no new database API and SqlClient count unchanged.
                        // Re-fingerprinted for F-020: the instruction cancel is stored in the same save as its attempt and audit;
                        // no new database API and SqlClient count unchanged.
                        // Re-fingerprinted for the F-020 review fixes: the aggregate instructed and loaded limits leave cancelled
                        // instructions out and the rebuild comparison covers CancelledAtUtc; no new database API, SqlClient count unchanged.
                        // Re-fingerprinted for the read-phase deadlock closure: a SQL 1205 raised by an aggregate SUM read inside
                        // the Serializable section maps to ConcurrentChange; no new database API and SqlClient count unchanged.
                        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): the unload weighing record over the same filtered context (7045422a… -> e0254929…).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (e0254929… -> ea2ff89e…).
                        "20c61595c4c05a84097373db7cf44ba8078514f430df459fb98ad75ea0bca290",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // PUR-4a: the Purchasing initializer's one provider branch, as Sales.
                // Re-fingerprinted for AT-025: one scoped registration of the open-line read port; token counts unchanged.
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (8ca9aeca… -> c786a176…).
                ["src/backend/Modules/Purchasing/"
                 + "GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "PurchasingModuleExtensions.cs"] =
                    new(
                        // PUR-P05 adds one scoped read decorator; provider capability counts are unchanged.
                        // A2a-1: the service factory also passes the existing Settings money port and its candidate read; counts unchanged.
                        // A2a-2: and the existing Settings line tax port (ISettingsLineTaxResolverV1, optional); counts unchanged.
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (c786a176… -> d849429f…).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (d849429f… -> 8bb1a10c…).
                        // Re-fingerprinted for tren-1 W6: STK-S17/S18: register the Purchasing-owned current unreceived schedule reader; reviewed API counts unchanged (8bb1a10c... -> 90022337...).
                        "de8afeef36bf4c9388f20bc8c44bfe3bbcc37cce4a6a05466581409a206404a7",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // R16/P04: reviewed provider participants. Exact source and provider-boundary token counts are
                // pinned; changes require a fresh source review rather than broadening this allowlist.
                [ReviewedInventoryGoodsReceiptParticipantPath] =
                    new(
                        "d4ad69114b162cbf25762879d2e56c246f606d09623977556374306ec9f4f13b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["DbConnection"] = 3
                        }),
                // PUR-HOST: central composes the Purchasing and Inventory owner participants over one
                // borrowed transaction. The exact source and two connection lookups keep the bridge narrow.
                // Tren-1 W8; K3 review pending. The seventh connection access enlists the typed Purchasing receipt-cost proof in the same owner transaction; company/actor fences and owner writes are retained.
                [ReviewedPurchaseGoodsReceiptHostPath] =
                    new(
                        // Re-fingerprinted for tren-1 wave 3 (integration): ALC-S16 (goods-receipt batch) shares the host's helpers; GetDbConnection count unchanged (377d659c… -> 724126ed…).
                        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): stok merge: the Participants record also carries Inventory's future supply port and the owner connection/transaction (conflict resolution, K3 review pending) (724126ed… -> f5285ea4…).
                        // stok merge: the enlisted Participants record takes the owner's connection/transaction (System.Data.Common,
                        // DbConnection) and the future supply conversion enlists on a third GetDbConnection. K3 review pending.
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (f5285ea4… -> e1ba5d1d…).
                        // Re-fingerprinted for tren-1 W7: Sales-before-Purchase prelocks share the caller connection; GetDbConnection 3 to 6; K3 review pending.
                        "f87be49a8d3cc123e2ddff5a5d1bb0d76bc0eb0d50e2dd49d047a3c47045fe6c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 7,
                            ["System.Data.Common"] = 1,
                            ["DbConnection"] = 1
                        }),
                // Owner read projection remains tenant-filtered; an empty API set plus exact fingerprint
                // authorizes only its three reviewed PurchasingDbContext constructions.
                [ReviewedPurchaseAcceptanceSourcePath] =
                    new(
                        "cd8704f39608ab73d0557c397bc8c8b84ff5c11f65d54061bc93d1ee68cd7169",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // tren-1 wave 1 (F-017 future supply read port): tenant-filtered owner read; an empty API set plus exact
                // fingerprint authorizes only its three target-typed result constructions. K3 review.
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (087cba10… -> 61d3b007…).
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/PurchaseFutureSupplyReadPort.cs"] =
                    new(
                        "b1ceec11bcd27ced6c230330e3c7bb72eebd104a9ca14052c335e88956a181a0",
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                // R17: the Manufacturing realization participant owns one Inventory transaction and
                // one reviewed provider connection; exact source/count drift remains fail-closed.
                [ReviewedInventoryManufacturingRealizationParticipantPath] =
                    new(
                        // Re-measured at the #479 forward onto main e1ffefe8: #479's customer-owned issue context
                        // (17e9470a…) and #489's Sales-reservation deduction (a3d8f262…) combined; neither parent's value.
                        // Re-fingerprinted for B-01: partial class, plan-allocation interface, issue consumes its own plan's allocation, legacy-stable issue hash; provider tokens unchanged.
                        // Re-fingerprinted for B-03: V2 output overload, net-issued input read (company-filtered context) and Manufacturing genealogy staging; provider tokens unchanged.
                        // Re-fingerprinted for SAT-D318: the input issue's physical-stock refusal (pre-check and ledger fallback)
                        // names the issue, label, place and checked kg; provider tokens unchanged (72ba6be8 -> 02a98e75).
                        // Re-fingerprinted for SAT-D318 / SINAN-CEVAP (8) Q5: the pre-check hands the free physical kg to
                        // the shortfall, which derives the usable kg; provider tokens unchanged (02a98e75 -> 4c1a5d57).
                        // Re-measured on the combined file after merging main 6f1ff559: B-03 (1a5a23b7...) and SAT-D318
                        // (4c1a5d57...) combined; neither parent's value; provider tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        "95fc82c394062d4311678e254971bad5c809da36ee29c6ab9279bcbae0ceaa00",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["DbConnection"] = 3
                        }),
                [ReviewedPurchaseAcceptanceParticipantPath] =
                    new(
                        // Re-fingerprinted for A3 API-30 (ALS-D33, ALS-D21): the order rebuild also reads the line's delivery
                        // slices (a tenant-filtered, no-tracking read on the same context) so it replays the schedule revisions;
                        // the reviewed constructions and provider tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 2 trial integration (API-11): the restore also reads the order's line closure events (AsNoTracking, same
                        // company-filtered context); reviewed tokens and the three construction sites unchanged (23106d16... -> 60aab061...).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (60aab061… -> 4f423055…).
                        // Re-fingerprinted for tren-1 W7: owner transaction or response changes; existing API counts retained; K3 review pending.
                        "72ae3cae1dae9eff49d017cec110e2e1b62a28740d649c9f88ae624903bfde08",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["DbConnection"] = 3
                        }),
                // PUR-4a: the SqlClient namespace only to recognise a unique-key violation (SqlException
                // 2601/2627) after SaveChanges, as the Sales store does. No connection, command or raw SQL.
                // Re-fingerprinted for A1-1: request decisions (bound approved-order fact read through the filtered
                // context, decision facts written, source request state at order approval); same token count.
                // Re-fingerprinted for A1-1b: the withdrawal facts written and rebuilt through the domain; same token count.
                // Re-fingerprinted for A1-2: the request line's source written once, audited and rebuilt through the domain;
                // same token count.
                // Re-fingerprinted for A1-3: the order cancellation (facts and cancelled shares written, audited and rebuilt
                // through the domain; cancelled orders keep counting in the request line's Σ-shares); same token count.
                // Re-fingerprinted for A2a-1: the order's money snapshot and the line prices written once, audited and rebuilt
                // through the domain; same token count.
                // Re-fingerprinted for A2a-1c: the order's unit-price scale written once with the money snapshot, audited and
                // rebuilt through the domain; same SqlClient use.
                // Re-fingerprinted for A2a-2: the line's maturity surcharge written once with the price, audited and rebuilt through
                // the domain; same SqlClient use.
                // Re-fingerprinted for A2a-5a: the create envelope's actor stamped as the order's preparer before the row is written
                // (row CreatedBy, domain and audit agree); the approver and approval time written, audited and rebuilt through the
                // domain, a legacy approval rebuilt without an approver; same SqlClient use.
                // Re-fingerprinted for A3 API-30 (ALS-D33, ALS-D21): a line schedule revision appended in the order's one save (the
                // header version, the slices, the attempt and the audit), the snapshot's deliverySchedule, and every revision
                // rebuilt through the domain at its version between the acceptance shares; same SqlClient use. Review fix: the
                // Σ-within-need check runs for every command but the schedule command (pre-existing behaviour kept).
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (e644ba29… -> 31b03aa5…).
                ["src/backend/Modules/Purchasing/"
                 + "GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs"] =
                    new(
                        // tren-1 wave 1 (F-017 F17-1): +1 GetDbConnection, handed with the current EF transaction to the reviewed
                        // PurchaseLineSerializationLock (sp_getapplock only); no command is created here. K3 review.
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (31b03aa5… -> 35f883d4…).
                        // Re-fingerprinted for tren-1 W6: KLN-D105 / SAT-S8: serialize schedule changes with future-allocation reduction using the existing order-line locks; reviewed API counts unchanged (35f883d4... -> 8bd3ca9b...).
                        // W8 numbered cancellation uses one Serializable owner transaction; same API counts, K3 pending.
                        "ebaac3eb7efcd7e0adc530d45904b0e11d57df1eac1e7ca1731d73026b1ebc8a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // ERP-000G / AT-065: the vault initializer's one provider branch, as Logistics. Re-fingerprinted for
                // the production adapters (own-server store + Windows Defender registration); the branch and its
                // token counts are unchanged.
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (8e6108de… -> 95c7dea8…).
                ["src/backend/Modules/Documents/"
                 + "GoymenERP.Modules.Documents.Infrastructure/"
                 + "DocumentsModuleExtensions.cs"] =
                    new(
                        "daf96c1173fd67e0374d6c2b33a15df758aaae54fd9ee527e13e2849b0d5c8a6",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // ERP-000G / AT-065 / DEC-DOC-003: the SqlClient namespace only recognises a unique-key violation
                // (SqlException 2601/2627) after SaveChanges, as the Logistics store does. Re-fingerprinted for the
                // immutable version append/history implementation; no connection, command or raw SQL was added.
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (637ee9a5… -> f6cc0f27…).
                ["src/backend/Modules/Documents/"
                 + "GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentsStore.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (f6cc0f27… -> b3111730…).
                        "5650ad7c0588f9b054964b578ab26fdc7d6d10a8a6ba646e9ecf4c5ff4714217",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                // SALES-P2 (#315): the Sales initializer's one provider branch, as Organization.
                // Re-fingerprinted for AT-024: the draft service also receives the optional delivery read port; the
                // provider branch and its token counts are unchanged.
                // W8 SAT-S8 / KLN-D105: pass the already registered reduction participant to Sales; DI-only, same API counts.
                ["src/backend/Modules/Sales/"
                 + "GoymenERP.Modules.Sales.Infrastructure/"
                 + "SalesModuleExtensions.cs"] =
                    // Re-fingerprinted for #641: the scoped pending-approval read seam is DI-only; the provider
                    // branch and its database capability token counts are unchanged.
                    // Re-fingerprinted for SAT-D8 / API-10: the draft service also receives the optional Logistics
                    // customer-return read port (DI-only); the provider branch and its token counts are unchanged.
                    // Re-fingerprinted for API-20 / API-21: the request store and service are DI-only seams over the same
                    // SalesStore and SalesDraftService; the provider branch and its database token counts are unchanged.
                    // Re-fingerprinted for API-22…24 over DEC-SAL-001 / API-08: the quote store and the optional Settings Sales
                    // policy port are DI-only seams; the provider branch and its database token counts are unchanged.
                    // Re-fingerprinted for SAT-D2 / API-12: the draft service also receives the optional limit-group source
                    // (DI-only); the provider branch and its token counts are unchanged.
                    // Re-fingerprinted for MOD-07 (merged over SAT-D2 / API-12): the sales order PDF's print read is a DI-only
                    // seam over the same SalesDraftService; the provider branch and its database token counts are unchanged.
                    // Re-fingerprinted for MOD-01 / API-06 (merged over MOD-07): the draft service also receives the optional
                    // customer-finance risk-input port (DI-only, owner-registered); the provider branch and its token counts are unchanged.
                    // Re-fingerprinted for MOD-02 (SAT-D9): the draft service receives Product's V2 tier-chain reader instead
                    // of the V1 reader (DI-only); the provider branch and its database token counts are unchanged.
                    // Re-fingerprinted for API-30 / API-31: the dispatch read service's bindings, the Sales-own order reference reader
                    // and the named service factory (DI-only, the owner ports optional); provider branch and token counts unchanged.
                    // Re-fingerprinted for SAT-D63 (limit-group source) merged over SAT-D25 / SAT-D39 (the draft service also
                    // receives the optional Settings quote policy port, DI-only); the provider branch and its token counts are unchanged.
                    // Re-fingerprinted for API-41 / API-42: the report store and report service registrations over the same Sales store
                    // and draft service, and the draft service's optional Cari party display (DI-only); provider branch and token counts
                    // are unchanged.
                    // Re-fingerprinted for API-04 merged over API-41 / API-42: the draft service also receives the optional Identity
                    // directory adapters (the sales owner's display and eligibility, DI-only); provider branch and token counts unchanged.
                    // Re-fingerprinted for MOD-03 → Sales: the draft service also receives the optional Settings line tax port and is
                    // registered as the tax read service (DI-only); provider branch and token counts unchanged.
                    // Re-fingerprinted for SAT-D181 (EK26): the draft service also receives the optional Settings rate list port (DI-only).
                    // Re-fingerprinted for SAT-D47 (DEC-FIN-010): the draft service also receives the optional Settings rate-day port
                    // (DI-only); provider branch and token counts unchanged.
                    // Re-fingerprinted for SAT-D90 / SAT-D101: the dispatch read service also receives the optional Settings
                    // rate-day port (DI-only); provider branch and token counts unchanged.
                    // Re-fingerprinted for SAT-D175/D176 (EK25): the draft service also receives the optional Product item type reader and
                    // the subcontract binding hook, and the subcontract service order read port is registered over the same store (DI-only).
                    // Re-fingerprinted for S-1 / MOD-03: the draft service also receives the supply owners' optional linked-supply
                    // adapters (GetServices, DI-only; none registered yet); provider branch and token counts unchanged.
                    // Re-fingerprinted for SAT-D219 (EK38): the draft service also receives the supply owners' optional recorder
                    // declarations (GetServices, DI-only; none registered yet); provider branch and token counts unchanged.
                    // Re-fingerprinted for S5 API-32 (Fatura cekirdegi 1): the invoice store over the same SalesStore and the invoice
                    // service factory (company context resolved, never registered); provider branch and token counts unchanged.
                    // API-35 (Fatura cekirdegi 3): the invoice service factory also receives the optional official-invoice read port and the
                    // tolerance policy (DI-only, none registered yet; the match is 503 fail-closed); provider branch and token counts unchanged.
                    // API-36 (Fatura kesinlestirme): the invoice store also feeds the finalize path and the optional finance admission port (DI-only,
                    // fail-closed 503 when unregistered); provider branch and token counts unchanged (e6274962... -> a3fdee0a...).
                    // API-37 (Kontrol Merkezi): the read-only control-center service factory and its policy singleton are registered; provider branch and token counts unchanged (e627496e... -> 846faead...; with API-36 also feeding the finalize path).
                    // Re-fingerprinted after #998: comments only (Purchasing's linked-supply adapter and recorder are now registered
                    // by the host; Production's are not); provider branch and token counts unchanged (7fc85621... -> b7241b6f...).
                    // API-07 (SAT-D153..SAT-D162): registers the risk exception service, its store and the versioned read port
                    // ISalesShipmentRiskExceptionReadPortV1; provider branch and token counts unchanged (b7241b6f... -> 4ad1aeaf...).
                    // EK56 SAT-D284: registers the order risk outcome source the risk exception request consults; provider branch and
                    // token counts unchanged (4ad1aeaf... -> ecf53c18...).
                    // API-40 (SAT-D288..SAT-D294): registers the customer return service over the Sales store with the optional Finance
                    // reversal port (DI-only, none registered; finalisation is 503 fail-closed); provider branch and token counts
                    // unchanged; recomputed on the merged registration (ecf53c18... -> 8389326f...).
                    // Re-fingerprinted for the linked-supply comment sync: comments only (Production adapter and
                    // recorder registered since #1085); registrations and token counts unchanged (8389326f... -> 1c25c7fd...).
                    new(
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 2 trial integration: SAT-S14 contract service/store and the shipment risk gate read port are
                        // DI-only registrations; reviewed tokens unchanged (146d4145... -> bf9e0937...).
                        // Re-fingerprinted for tren-1 wave 3 (integration, stok_lojistik): STK-S16: the optional allocation read port V2 passed to the cancellation impact read; reviewed tokens unchanged (bf9e0937… -> dbb50d2c…).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (dbb50d2c… -> 1944e0bd…).
                        "19f1733835eaf252ef28bac5b52810fe5787e4f387a818dfed9cd39a25ee6ffc",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // SALES-P2 (#315): the SqlClient namespace only to recognise a unique-key violation
                // (SqlException 2601/2627) after SaveChanges, as the other module stores do. No
                // connection, command or raw SQL. Re-fingerprinted for SALES-A1: the approval commands
                // (one save each, attempts keyed per command); SqlClient count unchanged.
                // Re-fingerprinted for DEC-SAL-003: the delivery-schedule write and history read, company-resolved
                // and saved once through the filtered context; no connection, command or raw SQL; SqlClient count unchanged.
                ["src/backend/Modules/Sales/"
                 // Re-fingerprinted for SAT-D17 (line closure): CloseLineAsync, one save through the filtered context and the closure restore; SqlClient count unchanged.
                 + "GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.cs"] =
                    // Re-fingerprinted for DEC-SAL-005: the cancellation write and read, company-resolved and saved
                    // once through the filtered context; no connection, command or raw SQL; SqlClient count unchanged.
                    // Re-fingerprinted for DEC-SAL-002: the price and quantity revisions share one archive-and-save
                    // core (still a single save through the filtered context); SqlClient count unchanged.
                    // Re-fingerprinted for PL04: the first complete price-floor approval response is persisted and
                    // replayed from the existing company-filtered attempt; SqlClient count remains unchanged.
                    // Re-fingerprinted for the stored product-snapshot replay check: structural summary equality
                    // replaces record equality; no database, connection, command, raw SQL or filter-bypass API was added.
                    // Re-fingerprinted for No 30 / SAT-D12: the force-close write and its restore, company-resolved and saved
                    // once through the filtered context; no connection, command or raw SQL; SqlClient count unchanged.
                    // Re-fingerprinted for API-20 / API-21: the class became partial (the request half lives in SalesStore.Requests.cs,
                    // same filtered context, one save) and the list cursor takes an ordering label; SqlClient count unchanged.
                    // Re-fingerprinted for API-22…24: the quote half lives in SalesStore.Quotes.cs (same filtered context, one save
                    // per command), the cursor gains the quote ordering label and the draft command check admits the conversion;
                    // the order's submit/approve reads its source quote's requests through the same filtered context (No 160);
                    // SqlClient count unchanged.
                    // Re-fingerprinted for SAT-D2 / API-12: the discount-exception approval shares the commercial line save and
                    // the replay-result path; no connection, command or raw SQL; SqlClient count unchanged.
                    // Re-fingerprinted for MOD-01 / API-06 (merged over API-12): one untracked read of a customer's open approved
                    // orders through the same filtered context (explicit company predicate too, rebuilt through the domain); no write,
                    // connection, command, raw SQL or filter-bypass API; SqlClient count unchanged.
                    // Re-fingerprinted for SAT-D72 (EK12): the cancellation's one save also deactivates its source quote's active
                    // request links (tracked through the same filtered context, IsActive 1 -> 0 only); no connection, command, raw
                    // SQL or filter-bypass API; SqlClient count unchanged.
                    // Re-fingerprinted for API-04: the archive write is shared by the line revisions and the header revision
                    // (SalesStore.HeaderRevision.cs, same filtered context, one save) and every archive writes its header row;
                    // the restore reads those rows through the same filtered context; SqlClient count unchanged.
                    // Re-fingerprinted for SAT-D115 / SAT-D152: the state command appends a customer-rejection row and the restore
                    // reads those rows (and binds the source quote) through the same filtered context; SqlClient count unchanged.
                    // Re-fingerprinted for MOD-03 → Sales: the line, archive and header rows carry the buyer tax / line tax JSON written
                    // and restored through the same filtered context (SalesTaxJson); SqlClient count unchanged.
                    // Re-fingerprinted for SAT-D175/D176 (EK25): the header, archive header and line rows carry the subcontract flag and
                    // the line item type, written and restored through the same filtered context; SqlClient count unchanged.
                    // Re-fingerprinted for SAT-D167 (EK24): the line revision passivates removed line rows (RemovedInRevision /
                    // ReplacedByLineId) and inserts added line rows, and the restore rebuilds both, through the same filtered context;
                    // SqlClient count unchanged.
                    // Re-fingerprinted for SAT-D211 (EK36): a rejection (and the next revision of a customer-rejected one) writes the
                    // reverted lines' revision columns (ReinstatedInRevision) through the same filtered context, checked move by move;
                    // the restore rebuilds reinstatements; SqlClient count unchanged.
                    // Re-fingerprinted for K3 review A/B P1-1: the line revision's audit picture also carries ReinstatedInRevision.
                    // Re-fingerprinted for K3 review A P1-1 / P2-1: an empty line revision that only reinstates is a valid line command.
                    // Re-fingerprinted for S5 API-32 (Fatura cekirdegi 1): the list cursor gains the invoice ordering label (the invoice
                    // store itself is the partial SalesStore.Invoices.cs over the same filtered context); SqlClient count unchanged.
                    new(
                    // Re-fingerprinted for SAT-D30 / SAT-D213: the over-delivery approval write (one save: approval row, header version,
                    // attempt, audit) and its restore through the same filtered context; SqlClient count unchanged.
                    // Re-fingerprinted for SAT-D252..SAT-D255: the source-plan command joins the draft replay command set (its write is the
                    // partial SalesStore.SourcePlan.cs over the same filtered context); SqlClient count unchanged.
                        // Re-fingerprinted for tren-1 wave 2 trial integration (F-018 S1): the draft transition may append its limit warning in the same save;
                        // reviewed tokens unchanged (44b86f83... -> 07051578...).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (07051578… -> 35298087…).
                        "40e8cbe7e8b23c89ce388c314af2f106058d6ecadd156eb2d613b646a0716315",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                         {
                             ["Microsoft.Data.SqlClient"] = 1
                         }),
                // JV03: the Work initializer owns one provider branch. Its exact source and
                // migration/empty-database API counts remain reviewed independently.
                ["src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/"
                 + "WorkModuleExtensions.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        "2653c62d85ff8899156f8f76e1f4f105e1abbb238e00f2832ee1f83f3ea681f4",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["MigrateAsync"] = 1,
                            ["EnsureCreatedAsync"] = 1
                        }),
                // JV03: SqlClient is used only to classify SQL unique-key races after
                // the tenant-filtered EF save; no direct connection or raw SQL is opened.
                ["src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/"
                 + "Persistence/WorkDesiredStateStoreV2.cs"] =
                    new(
                        "c8b78e13e5a6b5d2075730346a718dbf48672ca47356de212271240a7f76b472",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/SettingsManufacturingToleranceProviderV1.cs"] =
                    new(
                        // Re-fingerprinted for Üretim U1-a: the snapshot also carries the URT-D6 mass-balance tolerance
                        // (an init property); the DbException branch and its token count are unchanged.
                        // Re-fingerprinted for Üretim U1-b: the line-scoped read (line → process → version-0 catalog) with
                        // its own DbException branch over the same filtered context; the token count is unchanged.
                        "9f87150fb34c1b53232cafa1708f2bd5ba8de4954bbd20e832682f718d098027",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1
                        }),
                // SAT-D66: GetDbConnection only to share the request context's connection and transaction with the per-company
                // Settings contexts of one all-or-nothing binding write; every read and write stays tenant-filtered EF.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/SettingsSalesDiscountLimitAdministration.Bindings.cs"] =
                    // Re-fingerprinted for SAT-D116 (EK15) and SAT-D117: the write covers only the companies where the person is a member,
                    // and a passive group the person is already bound to may be kept
                    // (membership asked through the Identity port first); the one connection and context per company unchanged.
                    new(
                        "05d49a3a709f9272b07ea25d6aca23863c482c3ae65b7c83249ca4dd09dee265",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1
                        }),
                // Re-fingerprinted for SAT-D61…D70: the Sales policy port is replaced by the catalog + limit-group store
                // provider, and the binding reader and limit administration are registered (DI-only); the provider branch
                // and its database token counts are unchanged.
                // Re-fingerprinted for tren-1 wave 2 (trial integration): the team slices combined; token counts checked by the guard (60366fd0… -> a9ccced8…).
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "SettingsModuleExtensions.cs"] =
                    new(
                        // Re-fingerprinted for MOD-03: the tax master administration and the one tax master reader behind its two ports (DI-only); the database surface and its counts are unchanged.
                        // Re-fingerprinted for Üretim U1-a (+ SAT-D181 tax rate list port, merged): the Manufacturing parameter port and the profile administration
                        // behind its two ports (DI-only); the database surface and its counts are unchanged.
                        // Re-fingerprinted for DEC-FIN-010 / SAT-D169: the exchange-rate administration, the rate-day port and the two
                        // official sources (disabled unless configuration names an https TCMB address; DI-only); counts unchanged.
                        // Re-fingerprinted for tren-1 wave 1: the seven team slices combined; reviewed tokens unchanged.
                        // Re-fingerprinted for tren-1 wave 3 (integration): the team slices combined; token counts checked by the guard (a9ccced8… -> df39cc97…).
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (df39cc97… -> f985bf2f…).
                        // Re-fingerprinted for tren-1 W6: KIM-S13: register tenant-bound money profile administration and lookup; reviewed API counts unchanged (f985bf2f... -> d9a03cdf...).
                        // Re-fingerprinted for tren-1 W7: in-process owner registrations; activation flags unchanged; K3 review pending.
                        "d768aca25864735b3e2d2712f2e72cb18c99582b5d93f53e8d7deb8f3347c757",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                // Tren-1 W8; K3 review pending. System.Data.Common imports DbException solely for endpoint failure-to-503 mapping;
                // no DbConnection/context/SQL is created. Company-required routes pass current server company/actor metadata to
                // Identity owner; password/TOTP proofs stay in typed service.
                ["src/backend/GoymenERP.Api/Endpoints/IdentityEndpoints.TwoFactor.cs"] =
                    new("a3bc3ba402e21f6e4c734851ecc0c330b024289a2c21383d935f30bed451d998",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Typed ISalesSubcontractWeighingCommitReadPortV1 implementation delegates supplied
                // SQL handles to LogisticsSubcontractDeliveryReadSession; no direct query/write or handle completion.
                ["src/backend/GoymenERP.Api/Endpoints/SalesSubcontractWeighingCommitReadHost.cs"] =
                    new("9e969657440f9ef07bf74400f6ea2b0eb31ef8a6637ff9343c0f113af1d3951e",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. GetDbConnection hands Finance current transaction to
                // AccountingPeriodStatusReadSession; actual Accounting owner alone reads its Periods. Active same-connection
                // Serializable requirements live in owner session; bridge never disposes or completes Finance SQL handles.
                ["src/backend/GoymenERP.Api/FinanceRuntime/FinanceCreditAccountingPeriodPortV1.cs"] =
                    new("3ca9ea219809fd24b0b542327d019f47809f4b5f972ebea339f17354d7ff6e27",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. GetDbConnection hands Accounting current transaction to
                // FinancePeriodCarryForwardSession. Finance alone writes immutable carry voucher/counter-record; bridge invokes
                // owner contract, never foreign DbSet or SQL.
                ["src/backend/GoymenERP.Api/FinanceRuntime/FinancePeriodCarryForwardPortV1.cs"] =
                    new("0fba5bd1cccae07ecf1fc1cd04d0a9f450ff0f9f2a51712f5c7009681be074f6",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Current Logistics transaction supplies same connection to
                // SalesSubcontractInvoiceClaimReadSession; claim company/instruction/weighing proof checked before Logistics own
                // command.
                ["src/backend/GoymenERP.Api/LogisticsRuntime/LogisticsSubcontractDeliveryHost.Commands.cs"] =
                    new("63e1799f30f5d733b55d3205ee267ab62d44f6ba8a246b4b4b2cf597d60494de",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Logistics owns Serializable outer transaction; Sales line locks plus Inventory
                // write participant, Sales read session and Manufacturing completion read session borrow it. Actual mutations stay
                // in each owner, caller commits after fresh authority; negative refusal proof recording follows rollback.
                ["src/backend/GoymenERP.Api/LogisticsRuntime/LogisticsSubcontractDeliveryHost.cs"] =
                    new("eea479c2fd58d0deb7b20d8400621f26bacedf08bbac0d150a33551666e6ec25",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 4,
                        }),
                // Tren-1 W8; K3 review pending. Manufacturing owns Serializable outer transaction; supplied participant factory
                // creates Inventory owner session for cost stage/reversal. Inventory/Manufacturing own persistence and Audit
                // participate atomically; fresh authority/context validation around command/commit; typed SqlException
                // classification only.
                ["src/backend/GoymenERP.Api/ManufacturingRuntime/ManufacturingCostGenerationHost.cs"] =
                    new("83f3d81bb57e1591b610739b76a1fac220173164222da4d13082926a0de105d2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["GetDbConnection"] = 4,
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Rate-priority reader borrows current Manufacturing connection only through
                // InventoryManufacturingCostReadSession; own source order/plan/Sales link and returned label/company proof
                // validated, Scope checked across awaits.
                ["src/backend/GoymenERP.Api/ManufacturingRuntime/ManufacturingInputRatePriorityReaderV1.cs"] =
                    new("35bd5474f6a8b0c9313fdb5d9fffa8cbec6d70f2426f39d7d4c000fdc90cb634",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Actual Inventory movement-cost reader borrows Manufacturing current transaction
                // through Inventory owner read session when present; returned company/order/source proof verified, scope
                // before/after; no crossmodule write.
                ["src/backend/GoymenERP.Api/ManufacturingRuntime/ManufacturingInventoryCostMovementReader.cs"] =
                    new("77871e2fbdcf98904cb9ff5248fb8553793a1200af7144ee666016eabcd8d598",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Manufacturing reopen period bridge invokes AccountingPeriodStatusReadSession on
                // caller transaction; current company/actor and Accounting period read permission fenced; no period mutation.
                ["src/backend/GoymenERP.Api/ManufacturingRuntime/ManufacturingReopeningAccountingPeriodStatusPortV1.cs"] =
                    new("2a13ee3ac7efa93ec5c9fda6f2cb3702eb9e9e3ecf71afd93ed77c23e03caaa9",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. The configured Product connection is used for its Serializable global-catalog
                // transaction. Fresh group/actor/origin-company authority and typed Product/Audit/idempotency participants fence
                // owner writes; no direct foreign-schema DML.
                ["src/backend/GoymenERP.Api/ProductRuntime/ProductCogsClassificationRuntime.cs"] =
                    new("8a77323933b9b0e01ffdfd7a1147c3582c237ffa391adaa6255dba1b6dcb6ad5",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["SqlConnection"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. FindAsync is IPurchaseInvoiceImportReservationStoreV1 typed contract, implemented
                // by PurchasingStore.InvoiceImports using AsNoTracking.SingleOrDefaultAsync(Id) with Purchasing
                // InvoiceImportReservations CompanyId query filter. Captured company/returned reservation company/id/version
                // validated; not DbSet.FindAsync.
                ["src/backend/GoymenERP.Api/PurchasingRuntime/PurchaseInvoiceImportHost.cs"] =
                    new("4a985d450852a1d3a1407f43366c19f2e65251fac011aaa7a11e3689edff8468",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".FindAsync"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. API PurchaseReceiptCostScope wraps only Purchasing-owned PurchaseReceiptCostProof
                // borrowed read session; failure disposes proof, normal disposal delegates; no direct foreign table write.
                ["src/backend/GoymenERP.Api/PurchasingRuntime/PurchaseReceiptCostScope.cs"] =
                    new("1a194931234b5afd68668ecb3c670c5debd8fd171a857d1bade48cc3f8b8a88b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Batch host owns Purchasing Serializable transaction and composes same-handle
                // Purchasing adjustment, Inventory reversal and future allocation/lock participants; replay and result identities
                // companybound; fresh authority before commits; all participants disposal scoped.
                ["src/backend/GoymenERP.Api/PurchasingRuntime/PurchaseReceiptReversalHost.Batch.cs"] =
                    new("156bb60418ceb9d8e2b4f7ae15d25ccf37e29230e8c6757e3b62977778b46787",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["GetDbConnection"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Batch prelock accepts supplied DbConnection/transaction solely to enlist Inventory
                // future allocation owner and bind Sales/Purchasing serialization ports; validates bound company/order/acceptance
                // identities and lock order.
                ["src/backend/GoymenERP.Api/PurchasingRuntime/PurchaseReceiptReversalHost.BatchPrelock.cs"] =
                    new("c3b7927299016732ea8c3e8c36ddacbd0ba5f3f312c98e3f887e4830fa989f3f",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Correction host composes Purchasing cost proof and Inventory goods-receipt owner
                // participant on same caller handles; replacement request/actual answers exact company/acceptance proof checked;
                // no direct foreign DML.
                ["src/backend/GoymenERP.Api/PurchasingRuntime/PurchaseReceiptReversalHost.Correction.cs"] =
                    new("9072477383c7eb71d55bee4da80d47dca62f9d441d35a791899b1fdb6d987606",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Notification helper borrows InventoryHistoricalSalesLinksV1 owner read session;
                // original company/order/source proof and returned historical Sales targets validated; writes no
                // notification/Inventory rows directly.
                ["src/backend/GoymenERP.Api/PurchasingRuntime/PurchaseReceiptReversalHost.Notifications.cs"] =
                    new("8d1544bdd11aa1db302cb350c5a8c53625bec3a4259b133f252fb8d0abf34f27",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Single reversal prelock accepts caller handles only for Inventory future-
                // allocation participant and Sales/Purchasing owner lock binders; actual read targets company/order/acceptance
                // validated.
                ["src/backend/GoymenERP.Api/PurchasingRuntime/PurchaseReceiptReversalHost.Prelock.cs"] =
                    new("3fbd9f56ac8d48ea6600a9af118f57dc8adf0f7d0eec6b268a87cf48f648a604",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. The existing Purchasing transaction connection is passed to typed owner
                // participants for receipt reversal. Captured company/actor and fresh authority are fenced; each module writes its
                // own records. The unused Common import is removed.
                ["src/backend/GoymenERP.Api/PurchasingRuntime/PurchaseReceiptReversalHost.cs"] =
                    new("e3e45b4d1bd234aa1458abdb618b91da083041daa4c08045c090434f26749317",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. FindAsync is IAccountingFinalizedInvoiceLineSourceV1.FindAsync, NOT EF
                // DbSet.FindAsync. Two fully-qualified System.Data.Common DbException catches map source errors to unavailable; no
                // connection/raw SQL.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Application/InvoiceLineCogsCorrectionServiceV1.cs"] =
                    new("aad4eb901a044364e7550a7e905cedcd773e42cbd327fe9af88d998fe397f59f",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".FindAsync"] = 1,
                            ["System.Data.Common"] = 2,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient import is used only for unique/deadlock typed catches;
                // System.Data.Common.DbException catch maps source failure. Store uses owner injected AccountingDbContext with
                // query filters and own Serializable transaction.
                // W8 measured SQL repair: classify only the known EF-wrapped deadlock; same API counts.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingInventoryCostStoreV1.cs"] =
                    new("40a2737173a6f41889a6cf5872ea299e41a13fbc67a24fa918a64d65896490f9",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. New AccountingDbContext uses supplied connection contextOwnsConnection:false,
                // UseTransactionAsync on exact supplied transaction. Requires open same connection Serializable; ReadAsync forbids
                // tracker changes and only AsNoTracking Periods read; DisposeAsync disposes context only.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingPeriodStatusReadSession.cs"] =
                    new("0bb24cfdce88b992893f1793a5b1a8ec816b6a479735e1fa6edd9d701f0b8945",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 3,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient import only for typed SQL1205/DbUpdate deadlock classification; standard
                // EF owner Serializable admission transaction and command/audit commit.
                // W8 measured SQL repair: preserve concurrent-change refusal for exact EF-wrapped 1205; same API counts.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingSourceDocumentAdmissionStoreV1.cs"] =
                    new("8ca65bf50efebdeb14e2b4f53cb21e2bbed63fda725d5eaf80818cafe0da4076",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient import only in unique/deadlock filters; owner Accounting cancellation
                // receipt recorded under Serializable, replay hash proof preserved.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingSourceInvoiceCancellationStoreV1.cs"] =
                    new("f2ad1124ffec9b674a9199f02bab9c79329bffe152af76dafb7cfc13628fd79d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient import only in unique/deadlock filters; partial reversal store owns
                // Accounting transaction/command and own sourcevoucher state.
                // W8 measured SQL repair: preserve concurrent-change refusal for exact EF-wrapped 1205; same API counts.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingSourceVoucherStoreV1.Reversal.cs"] =
                    new("d937e05e076a3d807a5249e344b9caef5b3f2b8234e5d8f230ffaa99eb1c1b5a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient import only in unique/deadlock filters; store owns Serializable EF
                // sourcevoucher mutation.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingSourceVoucherStoreV1.cs"] =
                    new("b276f035c3fbdd281f5f34910727aecbcc67a4066a734b80e8a1fb5466154ed1",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient import only SQL1205 DbUpdate/deadlock classification; owner period
                // lifecycle wraps mutation in Accounting Serializable transaction. Finance carry is invoked through owner
                // bridge/contract.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingStore.PeriodLifecycle.cs"] =
                    new("542d8dcaeca8e5b194aa662a1bf95373e9c2d47135aca7037e422c4d46f1c23f",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient and System.Data.Common are typed exception catches for
                // concurrency/source-unavailable handling; actual EF writes in own Accounting Serializable transaction.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/InvoiceLineCogsCorrectionStoreV1.cs"] =
                    new("48af3e2195439da53f7044f80acf37404d0a97a3e065216a3e1825955f654f0a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["System.Data.Common"] = 2,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient only unique constraint classification in SaveAsync; owner
                // ManufacturingCostDifferenceStore uses Accounting Serializable transaction.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/ManufacturingCostDifferenceStoreV1.cs"] =
                    new("1e7e4532f4971658aa9809965bba46daffd586f7079d501388975ebf92b60fba",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. System.Data.Common provides DbTransaction type for open Serializable contract.
                // Fresh DI scope resolves AuditDbContext, requires empty tracker/no transaction,
                // SetDbConnection(contextOwnsConnection:false), UseTransaction; scope disposal leaves caller handles owned by
                // caller.
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Services/FounderRecoverySystemAuditV1.cs"] =
                    new("7d6a18e9269f2e88ab82cb7f99f81c41829278a393eba230afd0190cf410b684",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Typed DbTransaction openSerializable check; fresh clean Audit scope borrows caller
                // transaction/connection non-owning, appends owner Audit batch then disposes scope.
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Services/LocalCredentialSelfServiceAuditWriterV1.cs"] =
                    new("72e372f810213abd6fcec17b7442f4c64c0a3a20bd143724af1a7e6b1776a0b8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Typed DbTransaction activeSerializable check; new clean Audit owner scope borrows
                // non-owning SQL handles for AppendPlatformCredentialRecoveryAsync.
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Services/LocalTwoFactorOperatorRecoveryAuditV1.cs"] =
                    new("593de40b791dae2ba0a3f81fa3093d19b59bdb5d51b67ed1daf9581c2c62a797",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Three fully-qualified System.Data.Common mentions only DbException catch filters;
                // no borrowed/raw connection. Documents owner EF Serializable transaction atomically stores
                // draft/publication/command/audit.
                // W8 SQL repair: closed refusal operation mapping only; same three exception-type mentions, K3 review pending.
                ["src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentBrandingService.cs"] =
                    new("f1f98dac6ef80c409ab8b165a65066c229f5264e7ab28c23180dfb759fc75e4e",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 3,
                        }),
                // Tren-1 W8; K3 review pending. System.Data.Common imports DbException for typed read/transaction failure mapping
                // and rollback catch. Own Finance EF Serializable transaction, atomic proposal/decision/command/audit.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/CashExchangeBookValueServiceV1.cs"] =
                    new("8987e87fcdbdc99cd3b71e34e836f925bbd48175a7fdbcee4121e3f434ef9d2a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Single SqlQueryRaw<byte[]> fixed literal SELECT MIN_ACTIVE_ROWVERSION() AS Value
                // has no dynamic parameters or tenantdata. Then actual EF owner outbox query applies CompanyId==captured, after
                // Position, before horizon, order/take.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCreditOutboxEventSourceV1.cs"] =
                    new("e3bad193ea2b4c08abb1b5d907cf4719cd14ebe495915af2768f324e3a8c1b74",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["SqlQueryRaw"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. One new FinanceDbContext borrows open sameconnection Serializable transaction
                // contextOwnsConnection:false/UseTransaction. Session owns/disposes only context, never commits/rolls back
                // borrowed handles.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinancePeriodCarryForwardSession.cs"] =
                    new("32a68cfa05ccdc2402199426c3eb31a36c4d9c2d1ae922893edd9374428d3069",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 3,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient only typed failure catch. GetDbConnection reads configured
                // DataSource/Database for interactive target confirmation, not raw SQL/secondarymutation. Two fresh Identity
                // contexts: readconfirmation then Serializable owner reread; both awaited disposal.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/LocalAccount/LocalTwoFactorOperatorRecoveryServiceV1.cs"] =
                    new("268960c33752e59e78d78b6608993aa209dc883fb07e6491d9c74c0a6034b85c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. System.Data.Common imports DbException catch type; own injected Identity EF
                // Serializable transaction updates actual TOTP/recoverycode use and rolls back denied state.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/LocalAccount/SqlLocalTwoFactorLoginV1.cs"] =
                    new("4038ad4fcbd9b0bb0f788958d028bf9eac9b4c6db2a29f572c604a2f3e8a8d53",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. System.Data.Common is DbException failure classification only.
                // InventoryCostLineageReader reads actual label/transformation/movement/balance facts with explicit CompanyId
                // predicates on every batch, validates all projected source facts; no SQL handle/raw query/write.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryCostLineageReader.cs"] =
                    new("315ee82a22fe4d0a081150442efd55df53d31353bc578142c5c5d1ce6c15f49a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Fresh InventoryDbContext bound to explicit/captured company, UseTransactionAsync
                // on supplied exact open connection/transaction; own receipt/future-supply conversion reversal, immutable history,
                // command/outbox. Disposes only session context, never commits caller transaction. Existing read/write source
                // predicates and owner query filters retained.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryGoodsReceiptReversalParticipant.cs"] =
                    new("98a24d19a271862839c54a26596d626e5b7267e69565eed05f50952c2cdbd249",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 3,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Fresh InventoryDbContext bound to explicit/captured company, UseTransactionAsync
                // on supplied exact open connection/transaction; read-only historical future-allocation Sales demand links.
                // Disposes only session context, never commits caller transaction. Existing read/write source predicates and owner
                // query filters retained.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryHistoricalSalesLinksV1.cs"] =
                    new("7153b61207fc98cebda5f539021ad5ee1261f05f39e295a253f73a35f39ff636",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. One Inventory context borrows the validated caller transaction. Local pool
                // selectors carry explicit CompanyId and position/product/currency; only Inventory generations/effects/pools
                // change.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryManufacturingCostGenerationParticipant.cs"] =
                    new("91a5945632b609d265e14e654ab9c5b3afb95d0c75241faf4c04cfaebe2d8dbd",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".Local"] = 3,
                            ["DbConnection"] = 3,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Fresh InventoryDbContext bound to explicit/captured company, UseTransactionAsync
                // on supplied exact open connection/transaction; read-only actual manufacturing costs and first-entry rate
                // evidence. Disposes only session context, never commits caller transaction. Existing read/write source predicates
                // and owner query filters retained.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryManufacturingCostReadSession.cs"] =
                    new("646bd9f7fbdbcc5aeabb323a2042635aa6be5357bbd7488d0aaa797eb6610a12",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 3,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. GetDbConnection binds small fresh InventoryDbContext with own immutable
                // CompanyContext to existing owner transaction; UseSqlServer(contextOwnsConnection:false)/UseTransaction. Only
                // company-keyed NegativeStockValidationGuards INSERT/UPDATE and SaveChanges, never caller pending physical/audit
                // rows. Context disposal does not own outer commit/handles.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryNegativeStockValidationGuard.cs"] =
                    new("5f5db67ae7b975437b16e5dd5d9c42fec6c24ac8d9ee4f25ce07e4c35b7f9322",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. One SqlQueryRaw<byte[]> fixed SELECT MIN_ACTIVE_ROWVERSION() AS Value reads only
                // SQL metadata, no interpolated input. Typed20digit ulong cursor and8byte horizon; owner outbox EF query
                // explicitly CompanyId==captured, Position>after && Position<horizon, ordered/taken; samecompany fence after
                // metadata and rows. Read-only, no DML.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryOutboxEventSource.cs"] =
                    new("daa1db1ef57c1f33180e043be0dc029b266ca14edc258c56dcd53b12df197d47",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["SqlQueryRaw"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Tracked Local rows are selected with explicit CompanyId and
                // product/position/currency identity. Inventory alone changes its exact-cost pools; no query-filter bypass or
                // foreign-schema write.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryStore.ExactCostPools.cs"] =
                    new("247f15b8e1494b19dc9743da5b7ec2a1eda3c3f3649cad486e569c84de2ff421",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".Local"] = 8,
                        }),
                // Tren-1 W8; K3 review pending. Tracked Local pool rows carry explicit CompanyId plus position/product/currency
                // predicates. Inventory alone writes immutable movement stamps and exact pools under its existing owner
                // transaction.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryStore.MovementCosting.cs"] =
                    new("ae2427be012009bb8f9d5cce74b85b04e9614b0046f9305fff176d85c7e1e823",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".Local"] = 3,
                        }),
                // Tren-1 W8; K3 review pending. Old .Find occurrence was typed InventoryNegativeStockAttemptEvidenceException
                // traversal, not EF Find. Current source uses FindEvidence; do not add .Find allowance.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryStore.NegativeStockAttempts.cs"] =
                    new("11a503cf2dd546c3498a5f20d5c2809da1e252866d606d618e1ab1577dbf573d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Local movement cost stamp reads only already-staged immutable approval result
                // status; predicate explicitly CompanyId+MovementId+LineNumber+TL currency, no price exposed or extra query.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryStore.StockCounts.cs"] =
                    new("44946d5b777c0fd70b16fb9f995b51b0996ee43263490097d5917aa5d3993007",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".Local"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Clean owner Inventory context and own Serializable transaction; GetDbConnection
                // only binds typed Sales line serialization lock owner port. Allocation/label first loaded through owner query
                // filter, ReloadAsync refreshes only those tracked composite-company-key rows, then fresh authority and explicit
                // source/label CompanyId validation. SqlClient only duplicate/concurrency classification. No unscoped client
                // entity reload.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventorySubcontractAllocationReleaseStoreV1.cs"] =
                    new("f2914be6fff3ff2adc24c28d702db738d9969cdf0b968b4ae63d4b317a4a3a19",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".ReloadAsync"] = 2,
                            ["GetDbConnection"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Fresh InventoryDbContext bound to explicit/captured company, UseTransactionAsync
                // on supplied exact open connection/transaction; customer-owned subcontract reserve/ship, actual movement and
                // owner outbox. Disposes only session context, never commits caller transaction. Existing read/write source
                // predicates and owner query filters retained.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventorySubcontractDeliveryParticipant.cs"] =
                    new("bc0906ec02de0242eb46b3fdb37631cf4dfd7867435556b676c3d9c5781e6a65",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 3,
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Microsoft.Data.SqlClient is used only to classify owner EF unique-key exceptions.
                // Transfer document rows and immutable replay remain company-bound; no command creation, raw SQL or explicit
                // DbContext construction.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryTransferReceiptDocuments.cs"] =
                    new("7172f4012c6b1020f2d9267ca02f431be73af320687cd48dfd3707c3f4de0cf1",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Fresh LogisticsDbContext bound to explicit/captured company, UseTransactionAsync
                // on supplied exact open connection/transaction; read-only actual subcontract delivery/weighing source. Disposes
                // only session context, never commits caller transaction. Existing read/write source predicates and owner query
                // filters retained.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsSubcontractDeliveryReadSession.cs"] =
                    new("03a184462a48ae27f15a0913b1d8fb5ccade8d61fe6588a4bc150136ac737d7c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 3,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient is SqlException numeric classification only. Logistics departure owner
                // has own Serializable transaction/attempt/event rows, envelope actor/company binding and fresh authority;
                // official document read/cancel is typed ownerport, no new provider availability or direct foreign DML.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferDepartureOwner.cs"] =
                    new("5f4b17b228d7cb9211c38fd4e1d99dfcd07afaae983f5c773ec6a6a55f698e4c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient/Common used only typed SQL deadlock/timeout/DbException categories.
                // Manufacturing manual queue uses own EF transactions; all WorkCenterQueue/Plan/Order/Priority queries explicitly
                // CompanyId bound; no raw SQL.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingStore.ManualQueues.cs"] =
                    new("19ffbb9acb365f552719a23de495c382e08f453f906871efbab4c8aa563ca9ed",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Fresh ManufacturingDbContext bound to explicit/captured company,
                // UseTransactionAsync on supplied exact open connection/transaction; read-only actual subcontract completion
                // evidence. Disposes only session context, never commits caller transaction. Existing read/write source predicates
                // and owner query filters retained.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingSubcontractCompletionReadSessionV2.cs"] =
                    new("5243f5af033ba7afaf73f4926b3bc45b7fcc4ce05e423b807e8b6565fc341b22",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 3,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Global Product catalog operation validates GroupSharedMaster command actor,
                // originating company and operation kind. The Product-owned participant borrows the Serializable transaction; it
                // writes only Product classification records. This is not a tenant-filtered Product catalog.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/ProductCogsClassificationParticipant.cs"] =
                    new("da70ded30f493f67e35f20428c96319ea4ac794e25a5f5543596ef99f403b694",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 3,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. System.Data.Common.DbException only appears in rollback failure catch; Product
                // paint memberships own EF Serializable transaction, explicit company/actor metadata/fresh permission fences,
                // CompanyId owner rows; no raw SQL/foreign mutation.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPaintMembershipService.cs"] =
                    new("f92ce179bcb00200f361a6ff41ea5e1eb27840db9ff54f22966c8192d9b35355",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Application contract typed DbConnection/DbTransaction parameters document borrowed
                // same-transaction owner factory, not executable SQL or connection creation.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Application/Contracts/PurchaseReceiptReversalBatchContractsV1.cs"] =
                    new("ffd5dadef0a5f3b27bcd8e6cb8a819c483fbf2b317da3b3347473dcbc2a48f63",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Application contract typed DbConnection/DbTransaction parameters document same-
                // transaction owner factory; no executable query/write/handle ownership.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Application/Contracts/PurchaseReceiptReversalContractsV1.cs"] =
                    new("3eba4ed4e3adeccbd87ae03d2e633d7d0c8c1413d96d770ae418c652000e5004",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. FindAsync is IPurchaseInvoiceImportReservationStoreV1 typed contract, implemented
                // by PurchasingStore.InvoiceImports using AsNoTracking.SingleOrDefaultAsync(Id) with Purchasing
                // InvoiceImportReservations CompanyId query filter. Captured company/returned reservation company/id/version
                // validated; not DbSet.FindAsync.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Application/PurchaseInvoiceImport.cs"] =
                    new("07e888aad2aaa1b82e29f19e42b9f5b578b3f3c0a1ad1c3fb49bb203a1a31b99",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".FindAsync"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. One SqlQueryRaw<byte[]> fixed SELECT MIN_ACTIVE_ROWVERSION() AS Value reads only
                // SQL metadata, no interpolated input. Typed20digit ulong cursor and8byte horizon; owner outbox EF query
                // explicitly CompanyId==captured, Position>after && Position<horizon, ordered/taken; samecompany fence after
                // metadata and rows. Read-only, no DML.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Outbox/PurchasingOutboxEventSource.cs"] =
                    new("e1f6b97b23e5e43bf13f5b7bc43f0f9064cecc9485e8e26a15839166abcef51b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["SqlQueryRaw"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient only typed duplicate/concurrency catch in Purchasing additional cost
                // owner Serializable transaction. Company() captured, order/acceptance/service source CompanyId verified; own
                // AdditionalCosts/Attempts only, Audit ownerport.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseAdditionalCostStoreV1.cs"] =
                    new("5585809e7df76ec243b3e2365da393325516d585d3c51013dbf3a4b6fee03b3c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Partial Purchasing batch participant uses caller borrowed handle only for company-
                // keyed PurchaseLineSerializationLock; EF writes own immutable receipt/acceptance/batch/attempt/outbox; all
                // target/result company proof checked.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseReceiptAdjustmentParticipant.Batch.cs"] =
                    new("ebe9dfdb74335f44f0ad871810632ced7b7107f94cb21e0f6930a98670a6ce2c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Fresh PurchasingDbContext bound to explicit/captured company, UseTransactionAsync
                // on supplied exact open connection/transaction; own receipt adjustment, replacement acceptance,
                // attempt/audit/outbox. Disposes only session context, never commits caller transaction. Existing read/write
                // source predicates and owner query filters retained.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseReceiptAdjustmentParticipant.cs"] =
                    new("25ba794e0eac1a5583a040e560f012558b381b17bbcf1f37b3b43fa68b53112a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 3,
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Fresh PurchasingDbContext bound to explicit/captured company, UseTransactionAsync
                // on supplied exact open connection/transaction; read-only receipt/order/invoice/entry-rate exact cost proof.
                // Disposes only session context, never commits caller transaction. Existing read/write source predicates and owner
                // query filters retained.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseReceiptCostProof.cs"] =
                    new("bd7cb9ceddf8a5d222b9b672e0004a6d1cfb57143a13aa530928cfb74b1dffb7",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 3,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient only typed duplicate catch. Purchasing service order owner Serializable
                // transaction; Company() and every snapshot/actual source company bound; Settings number reservation called
                // through same transaction ownerport, no direct Settings write.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseServiceOrderStoreV1.cs"] =
                    new("3c064ffbe5c35643eed1cbb5dba845fde2f7d7a8f3e3587b14609c0a9ed2685d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Purchasing incoming invoice finalization uses GetDbConnection only to bind
                // company-keyed PurchaseLineSerializationLock within own Serializable transaction. Writes Purchasing
                // invoice/bindings/finalized outbox; no Inventory directwrites.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.IncomingInvoices.cs"] =
                    new("a99b3fcace66aca1921f198bde6ee99fbcb85059c51b18c33cc114bc4d599317",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. FindAsync is IPurchaseInvoiceImportReservationStoreV1 typed contract, implemented
                // by PurchasingStore.InvoiceImports using AsNoTracking.SingleOrDefaultAsync(Id) with Purchasing
                // InvoiceImportReservations CompanyId query filter. Captured company/returned reservation company/id/version
                // validated; not DbSet.FindAsync.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.InvoiceImports.cs"] =
                    new("087ef79936d91f86345379055410440d9d398fac469ee49452c11da0e5e24249",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [".FindAsync"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. One SqlQueryRaw<byte[]> fixed SELECT MIN_ACTIVE_ROWVERSION() AS Value reads only
                // SQL metadata, no interpolated input. Typed20digit ulong cursor and8byte horizon; owner outbox EF query
                // explicitly CompanyId==captured, Position>after && Position<horizon, ordered/taken; samecompany fence after
                // metadata and rows. Read-only, no DML.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Outbox/SalesOutboxEventSourceV1.cs"] =
                    new("9bd8682328121e36e27bf7518b2935b348c291b8f70d77bba327fbe0d70dbfd1",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["SqlQueryRaw"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Infrastructure port exposes typed DbConnection/DbTransaction for owner read host;
                // interface has no executable SQL/body or context construction.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/ISalesSubcontractWeighingCommitReadPortV1.cs"] =
                    new("7e7ae71290968f8be7a763cf427e75be12975978387f9d4ccffdfbb68a7f6cf2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. System.Data.Common imports DbException only for read-unavailable handling. Sales
                // responsible reader joins header/line on CompanyId+Id, explicit capturedCompanyId predicate, before/after
                // livecompany fence; no SQL handles or write.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesLineResponsibleReadPort.cs"] =
                    new("3025efe8c79859a88227af913bb2a8c24d2fd0f321a7af72471344bf4e92aece",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Sales owns Serializable subcontract transaction; GetDbConnection passes handles to
                // typed Logistics commit-read owner port and checks exact invoice company/weighing/source facts; no Logistics
                // directwrite.
                // W8 SQL repair: translate the exact EF-wrapped deadlock refusal; same raw API counts, K3 review pending.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.SubcontractTransaction.cs"] =
                    new("ee0cbf1683b66b9013820342d037751dd858cb45d4a2e8f7898d87808761c4cf",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["GetDbConnection"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Fresh SalesDbContext bound to explicit/captured company, UseTransactionAsync on
                // supplied exact open connection/transaction; read-only subcontract invoice claim/frozen owner identity. Disposes
                // only session context, never commits caller transaction. Existing read/write source predicates and owner query
                // filters retained.
                // W8 SQL repair: retain the defined officially-cancelled history state, never an active claim; same API counts.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesSubcontractInvoiceClaimReadSession.cs"] =
                    new("a5cd63bfb54bcbd975ed88c3d78f292ae84d393aff91171ecc7d9addf169c87f",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 3,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Fresh SalesDbContext bound to explicit/captured company, UseTransactionAsync on
                // supplied exact open connection/transaction; read-only subcontract service order/current revision. Disposes only
                // session context, never commits caller transaction. Existing read/write source predicates and owner query filters
                // retained.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesSubcontractServiceOrderReadSession.cs"] =
                    new("1c13cd63fed4ac28ac3f67ea1d15016efffc327c59fb78758e532b523365022d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["DbConnection"] = 3,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Typed DbTransaction activeSerializable check; fresh clean SecurityEvents DI scope
                // borrows non-owned SQL connection/transaction, validates typed signal and existing retention, then owner
                // AppendAsync.
                ["src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/Writing/LocalTwoFactorOperatorRecoverySecurityWriterV1.cs"] =
                    new("e519633ac314e559838dbbb14203c91dd2d299d537901ffa25c5a3763c01c084",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. Common DbTransaction contract requires open Serializable; scoped clean
                // SettingsDbContext borrows transaction connection non-owning/UseTransaction, disposed via scope; SqlClient import
                // only duplicateclassification. Standalone format change owns separate Serializable transaction.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/SettingsDocumentNumberSeriesV1.cs"] =
                    new("7f212b7fb4486ff113dc7c01cf62bfb31dea14eb33b91162cf9ade4a1746b21a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient used only duplicate key DbUpdate catch; standard Settings owner
                // Serializable EF policy/command/audit transaction; no raw SQL/connection created.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/SettingsFinancePolicyAdministration.cs"] =
                    new("cbe5222096497a8467019349826b174fbefdd94cfc88a8e16fb1ea47d3760919",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient only duplicatekey classification in explicit baseline-transfer EF
                // transaction; immutable one-time ownerhistory race protected by uniqueconstraint and companykind query.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/SettingsManufacturingProfileAdministration.Baselines.cs"] =
                    new("5cf5c16cbe13969f29ad6b4d55098bebde55cb6cf86373075df39845f6ee9249",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. System.Data.Common imports DbException solely to map failed CompanyPolicyReads to
                // provider-unavailable. No raw SQL/connection/context creation.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/SettingsNotificationRetentionAutomationV1.cs"] =
                    new("1fdff1365097ee7b0dac52ab7b39c9e136b1b219b3b7cc920b962c1c3282415f",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient only duplicatekey DbUpdate catch; Settings own Serializable
                // policy/command/audit transaction.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/SettingsPurchaseInvoiceVariancePolicy.cs"] =
                    new("79969f22738109d510f25fa822e18b38c85220deca41da4f8ff51c1ce88cb5f4",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
                // Tren-1 W8; K3 review pending. SqlClient only duplicatekey DbUpdate catch; Settings own Serializable
                // warehousepolicy/command/audit transaction.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/SettingsWarehouseTransferScalePolicy.cs"] =
                    new("1b53d3ffa10b0c3d1e9c0bc6dee96818f39cc3c2014df145d1889cfd4625db13",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                        }),
            };

    private static readonly IReadOnlyDictionary<string, ReviewedContextFactory>
        ReviewedPlatformContextFactories =
            new Dictionary<string, ReviewedContextFactory>(StringComparer.Ordinal)
            {
                ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/GroupSharedCommandDesignTimeDbContextFactory.cs"] =
                    new("GroupSharedCommandDbContext", "cb3032c17681f28ce368afd1b0e13ace3c515a92bf5a23b11f41f8336bb3b37a"),
                // W8 Product price-list model generation only: unresolved company, no Open/Migrate/Save calls,
                // one owner context; not host registration or runtime authority. Pending independent K3 review.
                // Explicit constructor type exposes that same single context to the existing scanner.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListDesignTimeDbContextFactory.cs"] =
                    new("ProductPriceListDbContext", "a8185f7a8636444dbad19ac6743cfc2bc2fd5afb42ed1c49e908bc4f6c577d60"),
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/ProductDesignTimeDbContextFactory.cs"] =
                    new("ProductDbContext", "14980801f97830d0887a538d88a4f9ccb68f9ddfbb696fb9b3c4e2d8d45f7374"),
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/"
                 + "Persistence/AuditDesignTimeDbContextFactory.cs"] =
                    new(
                        "AuditDbContext",
                        "38245af104fd713832e2926dba5798614515bb324d2560da8217f9cd49492b56"),
                ["src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/"
                 + "Persistence/SecurityEventsDesignTimeDbContextFactory.cs"] =
                    new(
                        "SecurityEventsDbContext",
                        "ed53cad05d7bee2554a10758f94e6b2b22236dd3ff4229e4a3021f510c26ddbf"),
                ["src/backend/Modules/Idempotency/"
                 + "GoymenERP.Modules.Idempotency.Infrastructure/Persistence/"
                 + "CommandExecutionDesignTimeDbContextFactory.cs"] =
                    new(
                        "CommandExecutionDbContext",
                        "1d588ddec5f6d7e57a6ce3981eb0cd04ce110d6ac9380e2d177a64156950dc4c"),
                ["src/backend/Modules/Identity/"
                 + "GoymenERP.Modules.Identity.Infrastructure/Persistence/"
                 + "IdentityDesignTimeDbContextFactory.cs"] =
                    new(
                        "IdentityDbContext",
                        "6856fafc70ce956a698cdca896ed529f6f1bfdd743475162f5907356df9429e5"),
                ["src/backend/Modules/Organization/"
                 + "GoymenERP.Modules.Organization.Infrastructure/Persistence/"
                 + "OrganizationDesignTimeDbContextFactory.cs"] =
                    new(
                        "OrganizationDbContext",
                        "d02da60cb32fdd67dceeae4cc0b1936c562d06ef10021f5b9cd1b9c686cc6626"),
                ["src/backend/Modules/Inventory/"
                 + "GoymenERP.Modules.Inventory.Infrastructure/Persistence/"
                 + "InventoryDesignTimeDbContextFactory.cs"] =
                    new(
                        "InventoryDbContext",
                        "14f56a93a57e7eb18db82a535b0867da7692e858aaaaca397e32f57f062dc204"),
                ["src/backend/Modules/Accounting/"
                 + "GoymenERP.Modules.Accounting.Infrastructure/Persistence/"
                 + "AccountingDesignTimeDbContextFactory.cs"] =
                    new(
                        "AccountingDbContext",
                        "646f93b9f56cfb49f005359fe52e625d4079b0da9071276c37cd2d126c3f3a6c"),
                ["src/backend/Modules/Finance/"
                 + "GoymenERP.Modules.Finance.Infrastructure/Persistence/"
                 + "FinanceDesignTimeDbContextFactory.cs"] =
                    new(
                        "FinanceDbContext",
                        "1fc2f7b4c3f1d44064f532f8cc27607ffd09ce24b7fee8e32250714a8a6390ce"),
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/"
                 + "ManufacturingDesignTimeDbContextFactory.cs"] =
                    new(
                        "ManufacturingDbContext",
                        "d69368bd24c02978808ff616f5462784bc05608eb5836e800920f64e0cf5d6a6"),
                ["src/backend/Modules/Sales/"
                 + "GoymenERP.Modules.Sales.Infrastructure/Persistence/"
                 + "SalesDesignTimeDbContextFactory.cs"] =
                    new(
                        "SalesDbContext",
                        "22edbd5646b4bb67b6502a07b06696e750a915995b71298d951fe4483b283ceb"),
                // LOG-4a: the Logistics design-time factory (migrations only), as Sales.
                ["src/backend/Modules/Logistics/"
                 + "GoymenERP.Modules.Logistics.Infrastructure/Persistence/"
                 + "LogisticsDesignTimeDbContextFactory.cs"] =
                    new(
                        "LogisticsDbContext",
                        "65969060034c72a61e8c88be2bb64207820931ee84974fa51d3955a1e8576004"),
                // PUR-4a: the Purchasing design-time factory (migrations only), as Sales.
                ["src/backend/Modules/Purchasing/"
                 + "GoymenERP.Modules.Purchasing.Infrastructure/Persistence/"
                 + "PurchasingDesignTimeDbContextFactory.cs"] =
                    new(
                        "PurchasingDbContext",
                        "1f18bc244888b237e6de9884f761bb54139c4da11e2446b4cdfac0e9d5d844d6"),
                // JV03: migrations only; the factory constructs exactly one WorkDbContext.
                ["src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/"
                 + "Persistence/WorkDesignTimeDbContextFactory.cs"] =
                    new(
                        "WorkDbContext",
                        "a41e35035988a73069c64ac96f13ed73078c2b40a0f88394e8548e8e583522b4"),
                // ERP-000G / AT-065: the vault design-time factory (migrations only), as Logistics.
                ["src/backend/Modules/Documents/"
                 + "GoymenERP.Modules.Documents.Infrastructure/Persistence/"
                 + "DocumentsDesignTimeDbContextFactory.cs"] =
                    new(
                        "DocumentsDbContext",
                        "4e4f135fb522abd72921ee45d36657f1c21a8ba411507612291382ca640a38d4"),
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/SettingsDesignTimeDbContextFactory.cs"] =
                    new(
                        "SettingsDbContext",
                        "03654429b388dabc3f2fe6fdb369235d460bf0a36329d6b61fd1cbf58c3f642e")
            };

    // Migration data/raw SQL operations are deployment-time DDL/DML, not runtime global-query-
    // filter behavior. They remain exact source/API reviewed because they can mutate tenant data.
    private const string ReviewedCounterpartyIdentityRevisionRepairMigrationPath =
        "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/Migrations/20261118165020_RepairPartyIdentityRevisionTaxNumberFormat.cs";
    private static readonly IReadOnlyDictionary<string, ReviewedMigrationMutationSource>
        ReviewedMigrationMutationSources =
            new Dictionary<string, ReviewedMigrationMutationSource>(StringComparer.Ordinal)
            {
                // W8 EK24 FNS-D105: Down first refuses (51815) while finalized purchase-invoice outbox history exists.
                // One deployment-time SQL guard preserves immutable history; no runtime SQL/tenant bypass. K3 review pending.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/"
                 + "20261119012425_AllowPurchaseIncomingInvoiceFinalizedOutboxEvents.cs"] =
                    new("e2c8f201f7328c1a7dd6cfc9e16977ba94a74c841ebba7bc8fb8e59e6db49967",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // W8 approval-mode null guard: Down refuses (51798) while approved access-grant history exists.
                // One deployment-time SQL forward-fix guard, no runtime tenant bypass or new grant; K3 review pending.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/"
                 + "20261118211933_RejectNullAccessGrantApprovalModes.cs"] =
                    new("041d8273b784b14ea76b6717c3ada8e7dc538ae72d5957829a4e228c0bb04212",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // W8 Unicode history check repair: Down refuses before restoring the broken old check while tax history exists.
                // Group-level history guard only; no data rewrite or runtime bypass. K3 review pending; THROW 51799 is unique.
                [ReviewedCounterpartyIdentityRevisionRepairMigrationPath] =
                    new("64d48934a449bc8115ca919cc6855b82aa211b0477b79b81acedc28b7d612d90",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W7; K3 review pending. Down first refuses with THROW 51941 while owner history exists; one SQL guard, no runtime bypass.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116181354_AddCollateralTypeCatalog.cs"] =
                    new("a79b1853a1327224090f450f26bc5f4879996780a791ac78cab4d6aafc0649d6",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W7; K3 review pending. Down first refuses with THROW 51942 while owner history exists; one SQL guard, no runtime bypass.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116182730_AddSalesContractDiscountLimit.cs"] =
                    new("965aaae9b57680210a93acbdc97f2705bf65ef9583d51966c53661e2e7f24856",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W7; K3 review pending. Down first refuses with THROW 51943 while owner history exists; one SQL guard, no runtime bypass.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116184005_AddManufacturingPlanPriorities.cs"] =
                    new("344d803d93798e19a8d1515550d6c6aaef7ec99431bf45f7a44bac4d2fe7106f",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W7; K3 review pending. Down first refuses with THROW 51944 while owner history exists; one SQL guard, no runtime bypass.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261116185533_AddCompanySetupInboxAndOutboxPosition.cs"] =
                    new("d13c78c42f8daced925b0280dcc6c94a273e56c9b593ac1eefa0128074fe5b6b",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W7; K3 review pending. Down first refuses with THROW 51945 while owner history exists; one SQL guard, no runtime bypass.
                ["src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261116185536_AddCompanySetupLifecycleAndMessaging.cs"] =
                    new("796940ff52424832a2eff7b51c55600c62094b89cb00fc4ed5123fe3bd0989cd",
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // KIM-S13 / URN-D2/D3 / KIM-D9: Down refuses (51940) while money profile versions or baseline transfers exist.
                // This deployment-time forward-fix guard prevents loss of recorded profile history; one Sql, no runtime bypass.
                // K3 review pending; no grant, role assignment or profile seed is admitted here.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261116162357_AddMoneyProfileVersions.cs"] =
                    new(
                        "5746f6eb8c351bf5e735a477bb9c940eb13d39ca7c84e84d0371bfb7f2325f61",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 (stok_lojistik) — STK substitute allocation flag: Down refuses (51653) while a substitute allocation exists (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261115183012_AddInventoryAllocationSubstituteFlag.cs"] =
                    new(
                        "e85e1d0a2a315d6dcfd24713ac4980e699a8db6c3d75233d39e651b855567b99",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 (stok_lojistik) — STK transfer receipts: Down refuses (51646) while receipt/overage/loss movements, receipts, decisions, loss requests or received transfers exist (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261115191058_AddInventoryTransferReceipts.cs"] =
                    new(
                        "5ef4499d2e550eb551adb1a9e0f7ddda4a74ddc12420733c081746a255b8fd62",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 (stok_lojistik) — F017-c future supply conversions: Down refuses (51711) while conversion links exist (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261115192415_AddInventoryFutureSupplyConversions.cs"] =
                    new(
                        "a91eb54d4d011149b8342a957e37ca35275217437797a1035dace43a515dfe8b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 (stok_lojistik) — STK supplier return exit: Down refuses (51654) while a supplier return exit movement exists (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261115205912_AddInventorySupplierReturnExit.cs"] =
                    new(
                        "405e17b597744f01a3e137efa977b29fb1575957b6b6c497640849222794da21",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 (stok_lojistik) — LOG unload weighings: Down refuses (51266) while unload weighings exist (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/"
                 + "Persistence/Migrations/20261115180036_AddUnloadWeighings.cs"] =
                    new(
                        "06d8c9367017e8171c0efd86f4eac30d77e0f56ca8580eb586f6602affe20971",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 (stok_lojistik) — PRD kg/m table imports: Down refuses (51483) while imports, their attempts or profile cards without a stored kg/m exist (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
                 + "PriceLists/Migrations/20261115184532_AddProductProfileWeightImports.cs"] =
                    new(
                        "4d4f7d87c8504aa578ac78372b0a52650358672829c28b87eee435d48dff03ca",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 — system-generated document scan state: Down refuses (51592) while system-generated versions exist (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/"
                 + "Persistence/Migrations/20261115165534_AddSystemGeneratedDocumentScanState.cs"] =
                    new(
                        "696d192d6dc442a560e93d5b1119323e35296085308187bf2aebaf593f234730",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 — one-step party credit limit: Down refuses (51563, number shared with the Idempotency retention purge migration) while direct limits exist (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20261115163811_RestorePartyCreditLimitOneStep.cs"] =
                    new(
                        "de6ed627df74b84c81f4bf80cf44b41e7d5addc748a28fc41a632004158eedbe",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 — FIN-S2 party collaterals: Down refuses (51585) while collaterals or their commands exist (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20261115184536_AddPartyCollaterals.cs"] =
                    new(
                        "97a224728959527b46bd4be62a7c27ff582ef2d9fee158b1a585611701bce033",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 — OPEN-021 / NS-12 retention purge: an append-only trigger on the purge evidence (51561/51562), the CommandExecutions mutation trigger re-created to admit the recorded purge (51010), and in Down a refusal (51563, shared with the Finance one-step credit limit migration) plus the original trigger restored. Four Sql; K3 review pending.
                ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/"
                 + "Persistence/Migrations/20261115162318_AddIdempotencyRetentionPurge.cs"] =
                    new(
                        "c8fe3a692c86d7a57832a1b391a077f24fb7e2619ecc28928d8a90b7d2524fde",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 4
                        }),
                // tren-1 wave 3 — monthly scrap price versions: Down refuses (51627) once a price was corrected (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/"
                 + "Persistence/Migrations/20261115174427_AddManufacturingMonthlyScrapPriceVersions.cs"] =
                    new(
                        "71925e440ce8e3617320c3c945cc8c81bbec93b742c55acc016f0e1e97d9f9a5",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 — output lineage edges: Down refuses (51543) while edges exist (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/"
                 + "Persistence/Migrations/20261115174432_AddManufacturingOutputLineageEdges.cs"] =
                    new(
                        "fbc87e563d880d6781d0141e267fab9f1d2d1eb2bcd06732699708d65319cd33",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration): the KIM placeholder-company migration (20261115200655_ClearPlaceholderCompanyVkn) left this list; KIM-D41/D48 emptied its Up and Down (no Sql), the VKN fix now goes through StageCompanyTaxIdCorrections.
                // tren-1 wave 3 — ALC-S16 goods-receipt batches: Down refuses (51616) while batches or batch attempts exist (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261115184944_AddPurchaseGoodsReceiptBatches.cs"] =
                    new(
                        "f0af1d3f09d50b4c7da31f30629dcb21beb415023db435e0cb343491f7ab96fc",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 — auth.login.rate_limited event: Down refuses (51591) while such events exist (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/"
                 + "Persistence/Migrations/20261115163955_AddLoginRateLimitedSecurityEvent.cs"] =
                    new(
                        "6c8d4b35ec8d79e310922dba7bd25abb2d1809d8f6a266869c30c8c336989b52",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 — KIM-F1 segregation of duties policy versions: Down refuses (51622) while versions exist (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261115193955_AddSegregationOfDutiesPolicyVersions.cs"] =
                    new(
                        "f111129966658ad5ca2f8993fcc0866f2eeb4fb2a9a8153aebdfdf5e7dc7dbf7",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 3 — KIM-F1 login rate limit versions: Down refuses (51331) while versions exist (forward fix). One Sql; K3 review pending.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261115201944_AddLoginRateLimitVersions.cs"] =
                    new(
                        "d4210a5f9a98bcaeda8118bd12bc5fe990ac6437f9c1e2f8617363a0ccb384df",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — SAT-S14 sales contracts: TR_Contracts_Guard keeps the contract facts, payment plan and discount rules and allows one discount decision (51471, number shared with Inventory's transfer dispatch cancellation guard), TR_ContractAttempts_Guard keeps attempts append-only (51472); Down refuses (51473) while contracts exist (forward fix). Three Sql; K3 review.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/"
                 + "Persistence/Migrations/20261115160935_AddSalesContracts.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (f77825b7… -> 50d8bf05…).
                        "50d8bf0548549a8c735c7cd7d687bbc7d56d3e303f8d6768907aa9a9b5e5f4b0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 3
                        }),
                // tren-1 wave 2 — KIM founder bootstrap state: Down refuses (51399) while bootstrap state, outbox or credential command rows exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/"
                 + "Persistence/Migrations/20261115144305_AddIdentityBootstrapState.cs"] =
                    new(
                        "cf15714336cf41c73aa4a2b01a9c6ac3cef2a547319ff29bf8b72b5c689e77fd",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — KIM access grant workflow: Up backfills ApprovalMode = SecondApproval on already approved requests; Down refuses (51484) while revocations or new workflow data exist (forward fix). Two Sql; K3 review.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/"
                 + "Persistence/Migrations/20261115170730_AddAccessGrantRequestWorkflow.cs"] =
                    new(
                        "c6f6cef8dff4f274292c603a653343d8a5da0db9bc694e47a32ca405d9a978d4",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 2
                        }),
                // tren-1 wave 2 — KIM company VKN change requests: Up refuses while a non-placeholder company has no VKN (51445) or two companies share a VKN (51442); Down refuses (51443) while change requests exist (forward fix). Three Sql; K3 review.
                ["src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/"
                 + "Persistence/Migrations/20261115145301_AddCompanyTaxIdChangeRequests.cs"] =
                    new(
                        "acb067d615fba90b4c9a7703dbe1b528bc4e6efbda736e686d12fa634afd1142",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 3
                        }),
                // tren-1 wave 2 — KIM overnight work-centre shifts: Down refuses (51444) while an overnight shift exists (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/"
                 + "Persistence/Migrations/20261115153652_AllowWorkCenterOvernightShifts.cs"] =
                    new(
                        "2726378290f935b6a5ef8e28559545727ede31583dbe7a2ad4c648e4b7368673",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — KIM sales quote and e-invoice scenario policies: Down refuses (51312) while any policy row exists (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261115132145_AddSalesQuoteAndEInvoiceScenarioPolicies.cs"] =
                    new(
                        "e121127855d7d7ebb9204ca6ba820cc4912edd03faaf22d30acb264b60f17f14",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — KIM tax rate lists: Down refuses (51377) while tax rate list rows exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261115150359_AddTaxRateLists.cs"] =
                    new(
                        "d876ef50d6137d92de9bf24e69d00954289dc04eccd46dbacc676c09917268a2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — KIM unit conversion policy versions: Down refuses (51330) while versions exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261115163747_AddUnitConversionPolicyVersions.cs"] =
                    new(
                        "185cc51e7a9b635edbd160198308bd1d9f7a012aa89091d054ac916b47d6a809",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — KIM bank CSV mapping versions: Down refuses (51527) while versions exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261115182354_AddBankCsvMappingVersions.cs"] =
                    new(
                        "91f66cdd18d55be82539844148dd043eeebeb1384656e1334068cb80db2299e9",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — PLT primary document link: Up backfills one primary (earliest) link per document; Down refuses (51447) while a marker disagrees with the earliest link (forward fix). Two Sql; K3 review.
                ["src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/"
                 + "Persistence/Migrations/20261115132357_AddDocumentLinkPrimaryMarker.cs"] =
                    new(
                        "d7bd87ddc40d1669d270b59d190f3279e6094a76a0e5db2b78cb9784fb953969",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 2
                        }),
                // tren-1 wave 2 — PLT documents bank-statement kinds: Down refuses (51517) while a bank-statement blob exists (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/"
                 + "Persistence/Migrations/20261115134058_WidenDocumentContentKinds.cs"] =
                    new(
                        "15b621a3aa9bbaad25d4a894c0d36bdecfb69d5e548d1c3fc128090c9970965e",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — FIN FNS-D45 second person: Up refuses (51362) while a direct credit limit record exists (no rewrite of limit history). One guard Sql; K3 review.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20261115121907_RequirePartyCreditLimitSecondPerson.cs"] =
                    new(
                        "ca8d397a94f2f293091b8269c44b259ebeb3f567f8cf1cf92d6344a7012c24bb",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — FIN official invoice identity claims: Down refuses (51372) while claims exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20261115122126_AddOfficialInvoiceIdentityClaims.cs"] =
                    new(
                        "1f53788309bda5adf3c0fe8387d9c25568f24394d941cf5cdfa25575aa147190",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — FIN cash transfers: Down refuses (51458) while transfers exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20261115145712_AddCashTransfers.cs"] =
                    new(
                        "0372703bb8e6aa2e1323c349656a55f0e997094498be3293259358bdfa7c2e4c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — STK manufacturing output reversal: Down refuses (51420) while a reversal movement exists (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261115123925_AddInventoryManufacturingOutputReversal.cs"] =
                    new(
                        "8c453443288ab58fb91d6437366afaeec76126b5a1aa02268077549dbe760f6c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — STK future supply allocations: Down refuses (51460) while allocations exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261115142457_AddInventoryFutureSupplyAllocations.cs"] =
                    new(
                        "066e035cd9c1a10988e0ace9730eb64018d46a2a55bbae7aec9c1e55ab5e57e2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — STK transfer dispatch cancellation: Down refuses (51471) while cancellations, window closures or stored scale settings exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261115151824_AddInventoryTransferDispatchCancellation.cs"] =
                    new(
                        // Re-fingerprinted for tren-1 W5 (integration): the seven team merges combined; value from the guard output (ec316c9d… -> a1a0ba7e…).
                        "a1a0ba7e1a65cff5e9f2a29694ae5f13333680aef7046ca1a85d4d4834ec1414",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — URT monthly scrap prices: Down refuses (51387) while prices exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/"
                 + "Persistence/Migrations/20261115123405_AddManufacturingMonthlyScrapPrices.cs"] =
                    new(
                        "2eac0ac6e03bc116047172dbcb48bd211a4174e1c745e50f081141c158921b0d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — URT order due date: Down refuses (51466) while an order holds or held a due date (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/"
                 + "Persistence/Migrations/20261115140512_AddManufacturingOrderDueDate.cs"] =
                    new(
                        "49bbff1c92874105ffd8bd07af1a90ccf72c1446d6355536ca3faf49c5fb9100",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — ALS purchase order line closure: Down refuses (51174) while closures, outbox messages or closed lines exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261115132609_AddPurchaseOrderLineClosure.cs"] =
                    new(
                        "3bf7262ef726792215365985ab63d906d6a22ba84a28942feabb3b21c607810d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 2 — STS order credit limit warnings: Down refuses (51418) while recorded warnings exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/"
                 + "Persistence/Migrations/20261115123637_AddOrderRiskLimitWarnings.cs"] =
                    new(
                        "209ac520d97be9e0d2dd0cf434af97e2236026555f6cf5d617e8c754d591ed60",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 1 — F-018 credit limits: Down refuses (51322) while credit limit rows exist, before any drop (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20261114200445_AddPartyCreditLimits.cs"] =
                    new(
                        "553fb7ed8231c4aa51f753df199867fb0fe94f443c93d01b450b57572254f269",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 1 — Customer return credits: Down refuses (51320) while credit rows exist, before any drop (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20261114231451_AddCustomerReturnCredits.cs"] =
                    new(
                        "3ff4cf855c8b85aa04d4cc5f161ae64c0083740145978c3faf5714636a781037",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 1 — FNS-D45…D52 expense payments: Down refuses (51296) while payment rows exist, before any drop (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20261115000505_AddExpensePayments.cs"] =
                    new(
                        "0ff131ad16a59944da64704b7f8f4fca8639d370278fb5ca299654d0b79d6a5c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 1 — KLN label production status: Down refuses (51291) while a marked label exists, before the drop (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261114193604_AddInventoryLabelProductionStatus.cs"] =
                    new(
                        "42344023914a63e51978249b725b51b7bcc5b76ad0dbf9a73c8098cedfaa1ecf",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 1 — URT plan due date: Down refuses (51281) while a plan carries a due date, before the drop (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/"
                 + "Persistence/Migrations/20261114093512_AddManufacturingPlanDueDate.cs"] =
                    new(
                        "ec3893064e843ea5bb19995df18d88474487561898bdf2413651c6b727820e29",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 1 — Output reversals: Down refuses (51284) while reversals or output line references exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/"
                 + "Persistence/Migrations/20261114204549_AddManufacturingOutputReversals.cs"] =
                    new(
                        "0b41ad45928883f8ee6dd734f5f2cfcefe976dae4c7014beff35fade39ac00de",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 1 — Work-centre calendar: Down refuses (51361) while calendars exist, before any drop (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/"
                 + "Persistence/Migrations/20261114220943_AddWorkCenterCalendar.cs"] =
                    new(
                        "837670d26f8d5309ad4ec65eff8dbbd51b6351fbf648b43469b75e6164484553",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 1 — Product card dimensions: Down refuses (51290) while dimension rows or attempts exist (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
                 + "PriceLists/Migrations/20261114194618_AddProductCardDimensions.cs"] =
                    new(
                        "73c056f993af8a6e83835370bcc76d08db9679f1e39335ad421c50b678346ec5",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 wave 1 — Sales policy versions: Down refuses (51311) while version rows exist, before any drop (forward fix). One guard Sql; K3 review.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261114184046_AddSalesPolicyVersions.cs"] =
                    new(
                        "05cc6e567dd05c5da65c127d76a096dd63ed44d8068be9f3959d4f82341d89b7",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/"
                 + "Migrations/20261010095724_AddManufacturingTolerancePolicyRegistry.cs"] =
                    new(
                        "9868ed38480264de7dc83081ef6dfed6f4390d10e38be6a160d6354bda0ef04b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 3
                        }),
                // SAT-D61…D70: Down refuses (51095) while limit groups or bindings exist, before any drop (forward fix).
                // Re-fingerprinted for SAT-D84: the same migration also creates (and Down drops) the withdrawal table; the
                // one guard Sql is unchanged in behaviour (a withdrawal cannot exist without its group).
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/"
                 + "Migrations/20261027221309_AddSalesDiscountLimitGroups.cs"] =
                    new(
                        "54a56ef67049ac6263c2f9d410f0401fc938c59ace113d184a0a259ecdbbb35c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Existing overlap fails before trigger replacement; concurrent publications are serialized by
                // pre-insert key-range locks. Down refuses history before restoring the predecessor trigger.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/"
                 + "Migrations/20261015150000_SerializeManufacturingTolerancePolicyWindows.cs"] =
                    new(
                        "5d7736e49d2c7024c7ddfd2ef92b395029ed2d98b32b092f1b440ead50f8e4de",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 4
                        }),
                // PRODUCT-TECH-FAMILY-02 publishes one exact cut-sheet/plate family and template; Up and
                // Down use two reviewed raw-SQL mutation surfaces without changing legacy technical rows.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/"
                 + "20261015110000_ReleaseHotRolledCutSheetPlateTechnicalDefinition.cs"] =
                    new(
                        "2972fbf7027788026a2d589785190b7087eef97d40c7b6f5fe4e8d29ed4245a8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 2
                        }),
                // AT-018 price-list history is durable; Down refuses destructive rollback while any
                // company-scoped price-list row remains.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/"
                 + "Migrations/20260930113416_AddProductPriceLists.cs"] =
                    new(
                        "f4223440444ebc7327b176f085ae98ed6f67d126e830fbe2d9887bd573cb7dba",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // MOD-02 (SAT-D9): the list scope; Down refuses (51072) while any customer-specific list remains, so dropping
                // the scope columns never turns a customer's price into a company-wide one.
                // MOD-03 (EK19 SAT-D133/D135/D139): Down refuses (51131) while any item-type default record, code list or
                // VUK 232 threshold exists, before any drop, so a rollback never turns a resolved tax back into "kaynak yok"
                // (forward fix).
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/"
                 + "Migrations/20261031194631_AddTaxMaster.cs"] =
                    new(
                        "285bff1b47db99f02583ec2465f5da796bd894ed92d90e01004e05503920bb79",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // MOD-03 (EK19 SAT-D134): Down refuses (51132) while any product item type or its command answer exists
                // (forward fix).
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/"
                 + "Migrations/20261031195253_AddProductItemTypes.cs"] =
                    new(
                        "94c66a0c670078d3a5068659653d5323a39402c7669d3d578cf529628ae4ea93",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // SAT-D149 (EK20): the item type moves to the shared product card. Up refuses (51151) when one product has
                // different item types in different companies (never chooses), then moves the agreed value; Down refuses (51152)
                // while any shared item type exists (forward fix).
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/"
                 + "Migrations/20261101144030_MoveProductItemTypesToProductCard.cs"] =
                    new(
                        "8eec9c301e2d1b4114a6c396394b6032bdf4ac6c38093485fb594fe141f04bf4",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 3
                        }),
                // Review B P2 / EK31 SAT-D198: Down refuses (51202) while a NotFound or manual half-day record exists (forward fix).
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/"
                 + "Migrations/20261102192125_AmendExchangeRateRecordStates.cs"] =
                    new(
                        "bd0810226629d0334ff06ba2844363b837bf86e72a1c733b8b098d45150da156",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // DEC-FIN-010 / SAT-D169…D171: Down refuses (51201) while any exchange-rate record exists (forward fix).
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/"
                 + "Migrations/20261102143743_AddExchangeRateRecords.cs"] =
                    new(
                        "91f70d3e3d62e83d7ce2293b5707acfcb62d91416a35703653afbe2f582d79ab",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Üretim U1-a: Up re-creates the tolerance registry trigger with the new column and adds three lifecycle guard
                // triggers; Down refuses (51183) while any profile or mass-balance value exists, then restores the trigger.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/"
                 + "Migrations/20261102082723_AddManufacturingParameterProfiles.cs"] =
                    new(
                        "7053b62d754ec39a5624b4a4e607dd024cdd06010582f49727980cf7e8137b43",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 6
                        }),
                // Üretim U1-b: Up re-creates the tolerance registry trigger (windows per line scope; a published row changes
                // only by closing its open-ended window) and the lifecycle guard with the line; Down refuses (51185) while any
                // profile exists, then restores the U1-a trigger and guard.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/"
                 + "Migrations/20261102121332_AddManufacturingToleranceLineScope.cs"] =
                    new(
                        "5799d4fe228cc08b4afacbfb87b4bec724390bc29855bb7ad51afe3cb0492070",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 5
                        }),
                // Üretim U1-b: Down's only raw SQL refuses (51186) while a plan parameter snapshot or a source-bearing close
                // decision exists (forward fix), before any table or column is dropped.
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/"
                 + "20261102121844_AddManufacturingPlanParameterSnapshots.cs"] =
                    new(
                        "0de4af1d0690280a2e7ec8fd8fe9817c642aa6fa3c05d8f426a0fdd138bd7b7d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Üretim U2-a: Up's first raw SQL refuses (51188) while a plan already has more than one order, before the
                // unique (company, plan) index; Down's first raw SQL refuses (51187) while any cancellation, rejection or
                // plan-entered value exists (forward fix), before anything is dropped.
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/"
                 + "20261102181335_AddManufacturingPlanLifecycle.cs"] =
                    new(
                        "ef908af46c91c0363c840c0e7f4ae116b9440664b3af5f7397b3b702463ca139",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 2
                        }),
                // Üretim U2-b1: Down's only raw SQL refuses (51189) while any plan-entered process value, or a plan-sourced
                // snapshot value other than the U2-a line speed, exists (forward fix), before anything is dropped.
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/"
                 + "20261102224501_AddManufacturingPlanProcessSpecs.cs"] =
                    new(
                        "94fa32cf0ecbe4a0d8b172a7e73ef62d9d41c2964b59eed1a6b69341bbe11eeb",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Üretim U2-d (URT-D2): Down's only raw SQL refuses (51190) while a Draft never revised records its
                // preparer (dropping it would let that Draft's creator decide it after a later Up), before anything is
                // dropped (forward fix).
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/"
                 + "20261107082508_AddManufacturingPlanPreparer.cs"] =
                    new(
                        "4f89517e3f10cf2736d650fd3c91b93445f4625a9201554ad77a73f14b8fbb37",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Üretim U3-a (URT-D21, B-01): Down's only raw SQL refuses (51191) while any plan input allocation row
                // exists (Inventory still holds the allocation it names), before the table is dropped (forward fix).
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/"
                 + "20261107145415_AddManufacturingPlanInputAllocations.cs"] =
                    new(
                        "5c407961e8bc61ce4a00098cad0963f479e0c15e4cbbf86aeac087c773bfa1ac",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Üretim U2-b (API-16, SAT-D219): Up is pure DDL; Down's only raw SQL refuses (51261, used by no other
                // migration) while any plan was ever linked to a sales order line (link revision above zero), before the
                // link columns are dropped (forward fix).
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/"
                 + "20261111183848_AddManufacturingPlanSalesOrderLinks.cs"] =
                    new(
                        "7c1b7050978c5c07e370518c14ba1e0f23963838d025e9247aa18d33e02c8efb",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // SAT-D158 (EK21): Down refuses (51153) while any buyer situation list exists (forward fix).
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/"
                 + "Migrations/20261101143556_AddTaxBuyerSituationLists.cs"] =
                    new(
                        "ed57b4c0083c649e6918145cb88b2f25a0d6ca57505afa61849d9cca0f2b55b8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/"
                 + "Migrations/20261027123034_AddPriceListScope.cs"] =
                    new(
                        "e744d35a662fdd14664736f55d213050c7810468602adefc0ad9bb33b4010414",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Manufacturing technical snapshots are immutable provenance; Down uses one reviewed statement to
                // refuse downgrade before either creation or revision provenance can be dropped.
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/"
                 + "20261001200000_AddManufacturingPlanTechnicalSnapshots.cs"] =
                    new(
                        "253f3953a27b7f1b6938e1bd16ef83bb9a55b6c093b51fa504271cdae2d56b7e",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // M01.2 cut-line plan/revision history is durable; Down refuses destructive rollback while any
                // cut line exists, before either table can be dropped.
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/"
                 + "20261014120000_AddManufacturingPlanCutLines.cs"] =
                    new(
                        "f24f28df3aeb0970909cc32b9a93f1b9f15938660ebc70878cb8f2ec07d600da",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // PRODUCT-TECH-FAMILY-02 technical length is immutable plan/revision provenance; Down uses
                // one reviewed statement to refuse downgrade while either snapshot table retains a value.
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/"
                 + "20261015123000_AddManufacturingPlanTechnicalLengthSnapshots.cs"] =
                    new(
                        "c64289e7907573faa848ce95f382245f90ca60845f952e0432ba543050b20db0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Sales pricing snapshots are durable; Down refuses destructive rollback while any
                // draft line keeps accepted pricing JSON.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/"
                 + "Migrations/20260930120000_AddLinePricing.cs"] =
                    new(
                        "e78046ed155a3043d17777e0459382abab7712cedcd41664a72ebcd26b459121",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // JV03 desired-state history is durable; Down refuses destructive rollback while any
                // company-scoped snapshot, inbox or outbox row remains.
                ["src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/"
                 + "Migrations/20261005190000_InitialWorkDesiredStateStorage.cs"] =
                    new(
                        "f75391a21884cba32bd1a285336038e8c7a126e5dbb1b7678e23445ccd259b0b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // B-24 Work outbox dispatch state (cursors, inbox/dedup, retry, poison) is durable; Down refuses
                // (51234) while any dispatch record exists and is a forward fix then.
                ["src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/"
                 + "Migrations/20261108221330_AddWorkOutboxDispatch.cs"] =
                    new(
                        "02bd8e3d1660f19505ed203f1a8ef8f026fbd0067237413e23a96278a7f4e43b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Manufacturing commercial context is immutable history; Down refuses before any captured plan,
                // revision or order context can be dropped.
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/"
                 + "20261004100000_AddManufacturingCommercialContexts.cs"] =
                    new(
                        "58c604c5d1397ccfe980714e517cbd2ac82afe09fbe42305d32d69ad13d993f9",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Manufacturing input label versions are durable; Down uses one reviewed statement
                // to refuse downgrade while any trusted accepted version remains.
                ["src/backend/Modules/Manufacturing/"
                 + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/"
                 + "20260924210000_AddManufacturingInputLabelVersions.cs"] =
                    new(
                        "6ed747c9211a2809fdc8f96e2f2d239292d3609a35abad397a09d2dd5a29699c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Inventory transfer and transfer-movement history is durable; Down uses one reviewed statement
                // to refuse downgrade while either surface contains data.
                ["src/backend/Modules/Inventory/"
                 + "GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/"
                 + "20260930182024_AddInventoryTransfers.cs"] =
                    new(
                        "347436e9b814863c397b9a7c201718726424ed5af7cf23cdc6dd7fc0badf55e8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // ProductVersion provenance is durable; Down refuses while any label stores it.
                ["src/backend/Modules/Inventory/"
                 + "GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/"
                 + "20261001160000_AddInventoryLabelProductVersion.cs"] =
                    new(
                        "9d3f1dd752d292a6013ebc6844ad66f0b630cbdaed9b284fa681f79f9ebee2a1",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Accounting reversal history is durable; Down refuses rollback while any
                // proposal remains instead of silently dropping lifecycle evidence.
                ["src/backend/Modules/Accounting/"
                 + "GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/"
                 + "20260925061753_AddAccountingJournalReversalProposals.cs"] =
                    new(
                        "af5aad11127802f67c94eb261d644400844940b7be991d38b3a52df5a76d79bb",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Approved source-catalog history is durable; Down refuses rollback while any package remains.
                ["src/backend/Modules/Accounting/"
                 + "GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/"
                 + "20261002063616_AddApprovedAccountingSourceCatalog.cs"] =
                    new(
                        "89ee19354a152fb3da1ebe91a852a7ada8a34acae181ff15b547d8f66a042ba0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // B-04a: posting account mapping history is durable; Down refuses (51212, used by no other
                // migration) while any mapping version exists; the single Sql call precedes both drops.
                ["src/backend/Modules/Accounting/"
                 + "GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/"
                 + "20261107140038_AddPostingAccountMappings.cs"] =
                    new(
                        "4c59513ac11ad0e19502fc1ba4944faafa6fd5b74606f83de34180ae13d9d215",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Official-invoice source history is immutable. Down refuses rollback while any imported batch,
                // approval or command exists; the single reviewed Sql call precedes destructive schema operations.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20260930185113_AddOfficialInvoiceImportApprovalV1.cs"] =
                    new(
                        "30abbd33afddf9564faa7d4e65d2705e3e54603cd5f83299676ce0b328895ee2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Work-delivery Down uses one reviewed Sql refusal (51051) before destructive drops
                // whenever a delivery or versioned command receipt remains; populated history needs forward-fix.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20261005213000_AddOfficialInvoiceImportWorkDeliveryV1.cs"] =
                    new("5e5ffef0ce93e35244685e766490e172f2c6417f36e5c071229fa5d7975d8ec1",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // A1-1 (plan API-01): two reviewed statements. Up records the submitter of every submitted request from its
                // own stored submit attempt (purchasing.PurchaseAttempts, same schema; nothing guessed); Down refuses
                // (51164, used by no other migration) while any request is Approved or Rejected, before every drop.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261102125710_AddPurchaseRequestDecisions.cs"] =
                    new(
                        "86686db520f0be62c2fb11e8bc50ca59c4769c45a052f955f25e23f10a29b42f",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 2
                        }),
                // A1-1b (plan API-01b; ALS-D46): two reviewed statements, both before every drop. Down refuses (51165, used by
                // no other migration) while any request is Withdrawn, and (51166, used by no other migration; #907 review P3)
                // while a stored decision differs from its maker only by case, which the restored A1-1 check would reject.
                // Up only re-creates checks and adds nullable columns.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261102161658_AddPurchaseRequestWithdrawal.cs"] =
                    new(
                        "e9948461dac12ae23a6f93c9e93e5a468e0ddacafc63ebec339bb317a0e23bf3",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 2
                        }),
                // A1-2 (plan API-02): one reviewed statement, before every drop. Down refuses (51167, used by no other
                // migration) while any request line carries a source, which dropping the columns would silently turn manual.
                // Up only adds nullable columns, a filtered index and a check.
                // A1-3 (plan API-10, No.161 §2): one reviewed statement, before every drop. Down refuses (51168, used by no other
                // migration) while any order is Cancelled or any line carries a cancelled share, which dropping the column would
                // silently turn back into future supply. Up only adds columns and checks.
                // A2a-1 (plan TEK-01/API-20): one reviewed statement, before every drop. Down refuses (51169, used by no other
                // migration) while any order has a currency or any line a price, which dropping the columns would silently turn
                // unpriced. Up only adds nullable columns and checks.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261107063820_AddPurchaseOrderLinePrice.cs"] =
                    new(
                        "2c29e418a7a2afbf62d8f2953385135b1a5c03ad2a2164d6aa6389a862ae91a1",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // A3 API-30 (ALS-D33, ALS-D21): one reviewed statement, before the drop. Down refuses (51173, used by no other
                // migration) while any delivery slice exists, which dropping the table would erase (the recorded schedules every
                // rebuilt order version counts). Up only creates the table; nothing is backfilled.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261112014446_AddPurchaseOrderLineDeliverySchedule.cs"] =
                    new(
                        "b79ebfa7f83116ac87a83100b47023432e4fb2adcc7fedd8657e2c47eda95ce2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // A2a-5a (ALS-D3, Fable F-015): one reviewed statement, before every drop. Down refuses (51172, used by no other
                // migration) while any order records an approver, which dropping the columns would erase (who approved, and when).
                // Up only adds two nullable columns and their check; orders approved before it keep no approver.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261111182351_AddPurchaseOrderApproval.cs"] =
                    new(
                        "43e1297bf25e0f92a99e0f8b35e58a864c3b83da5f77b907d8022db4ef8f2e7a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // A2a-1c (ALS-D72): one reviewed statement, before every drop. Down refuses (51170, used by no other migration)
                // while any order carries a unit-price scale, which dropping the column would leave with unit prices wider than
                // the amount scale the domain rebuilds them with. Up only adds a nullable column and its check.
                // A2a-2 (ALS-D38, ALS-D75): one reviewed statement, before every drop. Down refuses (51171, used by no other migration)
                // while any line carries a maturity surcharge, which dropping the columns would silently remove from its net and
                // amount. Up only adds two nullable columns and their check.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261107151838_AddPurchaseOrderLineDeferral.cs"] =
                    new(
                        "e98ff6107f7fad6094c8d38ea90ebff6d90625f3812da4d611838927dd710af0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261107131321_AddPurchaseOrderUnitPriceScale.cs"] =
                    new(
                        "d08f90d904a061f55578696a5c233fca4a4cf3015b73714d69dab4c47b8a84ef",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261103053923_AddPurchaseOrderCancellation.cs"] =
                    new(
                        "ee556c11702c65754f388f5f01706a9278930fdb18915fe40ffe661e14d55ea5",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // B-01: Down refuses (51211, used by no other migration) while a manufacturing plan-input allocation or
                // a recorded ReleasedKg exists, then restores the Sales-only demand check with the trust/enabled state
                // the widened check had, so an older migration's own Down guard still refuses rows planted with the
                // check disabled.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261107081411_AddManufacturingPlanInputAllocations.cs"] =
                    new(
                        "4a2fee655c1c19c4cf4c335549e8cab5dd0035ff8446ea5c481355d1dd4d382b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 2
                        }),
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261103012142_AddPurchaseRequestLineSource.cs"] =
                    new(
                        "891e22ead7083ac2ebdb6529370b15b9c4a0f2bafb453c1c269ee94327b45ec3",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Purchasing acceptance history is durable; Down uses one reviewed statement to refuse
                // downgrade while any acceptance share exists.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20260924102017_AddPurchaseAcceptanceParticipant.cs"] =
                    new(
                        "8d1988632ec1be7042cfd1eda10677b468be20cdb060677bfaca508a3b09ad19",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Manufacturing process expansion uses one reviewed DDL statement in this exact migration.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/"
                 + "Persistence/Migrations/20260924150000_ExpandManufacturingProcesses.cs"] =
                    new(
                        "631d6b751b5c01ad166935e802c1e80ed5a601d087dc3eab1f7c74b01d57528e",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // SALES-A1: the Down's only raw SQL is the refusal (51045) while any draft left Preparing or any
                // state-command attempt carries a DraftId (the pre-A1 unique index could not be restored);
                // forward fix, no approval fact or attempt is dropped.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260919120000_AddDraftApproval.cs"] =
                    new("d5aa478dc70559fa55e54df47a2fc6703a4677b1db4caab425fbe9b3ea10e89c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SALES-P Head B: the Down's only raw SQL is Sales' module refusal (51049) while any archived approved
                // revision is stored — Sinan's K3 says the previous approved snapshot is not deleted, so a rollback that
                // would drop those rows is refused. Reviewed rather than inventoried: the THROW precedes both DropTables,
                // an empty DraftRevisions rolls back freely, and 51049 is used by no other module (counted across
                // src/backend). Forward fix, the AddDraftApproval (51045) pattern.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260920073846_AddDraftRevisions.cs"] =
                    new("d2ec4a9b1f6fbe736ee18da1898ded863cfc3486c8bcb6b81923d6ec9ea893bf",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SALES-D2: the Down's only raw SQL refuses rollback while any withdrawn draft exists;
                // the refusal precedes the constraint replacement, so no withdrawal trail is discarded.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260921090000_AddDraftWithdrawal.cs"] =
                    new("df0b7eb737a3eeaeaed7ff3f9c9166ed3d0ad713486d0186e5eb8a8942e5117d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SALES-D2: the Down's only raw SQL refuses rollback while a terminal mutation refusal is linked
                // to a draft. The refusal runs before replacing the outcome constraint, so no durable refusal
                // identity is erased; environments containing one require a forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260922120000_LinkMutationRefusalsToDrafts.cs"] =
                    new("b34e318fa4e27a6f30b23a19d7370fd5783c7d7ece01204f36337b11cebbcc91",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // DEC-SAL-003: the Down's only raw SQL refuses rollback (51054, used by no other module) while any
                // delivery schedule is stored — after a change the schedule is the only home of the order's current
                // dates. The THROW precedes both DropTables; an empty database rolls back freely. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260926090000_AddDeliverySchedules.cs"] =
                    new("dc0019ef55d4283bdba596e50ce1ceaf7504efe11172457bb45db9f11109ffa9",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // DEC-SAL-005: the Down's only raw SQL refuses rollback (51055, used by no other module) while any
                // order cancellation is stored; the THROW precedes the DropTable and DropColumn. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260927120000_AddDraftCancellations.cs"] =
                    new("651cae770ecc2d18d3b24479523d95ead0259fe231188a47c390f24da8d0f21e",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // DEC-SAL-002: the Down's only raw SQL refuses rollback (51057, used by no other migration) while any
                // revision reason is stored; the THROW precedes the DropColumn. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260928090000_AddRevisionReasons.cs"] =
                    new("236f74197308a8e753fa2b74cc25070be726c0de4ea0ab38f785e3f60284a8a3",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // Q9 remainder changes are immutable order facts; Down refuses before dropping them.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260930171959_AddDraftRemainderChanges.cs"] =
                    new("38f3b04fce07c3260eab79e0d225030148e032e5041eda9b7963777cd12e4b28",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // Q9 customer acceptance is immutable; Down refuses before removing the acceptance columns.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260930223000_AddDraftCustomerAcceptance.cs"] =
                    new("faf491c8f59d0fe55a0f929d91c20468134d07b816fd468389e3ae632f9f64a6",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // PL04: the Down's only raw SQL refuses rollback (51064) while an immutable price-floor approval
                // replay result exists; the refusal precedes removal of the constraint and result column.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261004160000_AddSalesPriceFloorApprovalReplayResults.cs"] =
                    new("b1f3e21b2f6b13a6340edac1b29b5d5d25d5599212ab4d0596d905ed54679d3e",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // No 30 / SAT-D12: the Down's only raw SQL refuses rollback (51061, used by no other migration) while any
                // order force-closure is stored; the THROW precedes the DropTable and the CK restore. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261025120000_AddDraftForceClosures.cs"] =
                    new("adaea424b2a2238dbd0a962f96c660c9855756b67148f774aa4463118a3fe8a6",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // API-20 / API-21: two raw SQL statements. Up creates the request attempt table's immutability trigger
                // (ADR-SAL-001 §6: TR_RequestAttempts_RejectMutation, THROW 51067 on any UPDATE/DELETE); the Down's refuses
                // rollback (51065) while any sales request or request attempt is stored, before every drop. Forward fix. Both
                // codes are used by no other migration.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261027063346_AddSalesRequests.cs"] =
                    new("3acdd5d39c1a8f740211ed8c35395978b02d0e4c3441c559e2ecae34e2656927",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 2 }),
                // API-22…24: two raw SQL statements. Up creates the quote attempt table's immutability trigger (ADR-SAL-001 §6:
                // TR_QuoteAttempts_RejectMutation, THROW 51068 on any UPDATE/DELETE); the Down's refuses rollback (51066) while any
                // quote, quote attempt, converted order or conversion attempt is stored, before every drop. Forward fix. Both codes
                // are used by no other migration.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261027071300_AddSalesQuotes.cs"] =
                    new("372aa029a3b74e41404828d8a83cccc0abb3723ec5912df75a1773fa89396b7d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 2 }),
                // SAT-D2 / API-12: the Down's only raw SQL refuses rollback (51069, used by no other migration) while a
                // discount-exception approval replay result exists; the THROW precedes the constraint narrowing. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261027134109_AddSalesDiscountExceptionApprovalReplayResults.cs"] =
                    new("d240f6b2dcb9cf474af621a47c84daa70881abda4bce6a665a005e80ddbb3cc4",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SAT-D24/D44, SAT-D25: the Down's only raw SQL refuses rollback (51117, used by no other migration) while an
                // expired-quote approval, a list loss reason or an attempt of the two new quote commands exists; the THROW precedes
                // every drop and the constraint narrowing. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261027222533_AddSalesQuoteExpiryApprovalsAndLossReasons.cs"] =
                    new("d89c457fd4f0abae21736f09ad6dd79023c266301bcbbe0875c8818b9065f739",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // API-04: two raw SQL calls — Up's one-statement backfill of DraftRevisionHeaders from the (never revised) draft
                // headers of the archives written before it, and Down's refusal (51133, used by no other migration) while a header
                // revision was recorded or any draft carries a sales owner or due terms; the THROW precedes every drop. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261031131127_AddDraftHeaderRevisions.cs"] =
                    new("d2f0eb7e622945e26ebe3de8328fc8fd64d7e39248b6a209a72592fb0ed470b6",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 2 }),
                // SAT-D115: the Down's only raw SQL is Sales' refusal (51134, used by no other migration) while any customer rejection
                // is recorded (row or recorded attempt); the THROW precedes the DropTable, an empty table rolls back freely. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261101143750_AddDraftCustomerRejections.cs"] =
                    new("8cd14cc9c383002f48c83fabfb21b5a87bcf7f50daadce9d0563f885bfc73901",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // MOD-03 → Sales: the Down's only raw SQL is Sales' refusal (51135, used by no other migration) while any order or
                // archived revision carries a buyer tax or a line tax snapshot; the THROW precedes every drop. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261102071113_AddDraftOrderTax.cs"] =
                    new("044a5787cc02e0c83a8dad54c743fedff2c374d3d22961fc47e27ecca4787231",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SAT-D184 (EK26): Up's only raw SQL writes the buyer tax "Normal" once on every Sales order without one (the store's
                // exact JSON, one statement, no other table); Down is a documented no-op (forward fix).
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261102090531_BackfillDraftBuyerTax.cs"] =
                    new("fd822bd630315a4f8b78b9e948073c74ce01b289f5823f30f0718f01ca6f946d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SAT-D175/D176 (EK25): the Down's only raw SQL is Sales' refusal (51141, used by no other migration) while any
                // subcontract order, archived "fason" header or line item type exists; the THROW precedes
                // every drop. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261102115043_AddDraftSubcontractService.cs"] =
                    new("46c360a4b22d6cadc80cb18a6ddca57e214f9819ddd8e25fa4b6fddaa0868760",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SAT-D167 (EK24): the Down's only raw SQL is Sales' refusal (51142, used by no other migration) while any line was
                // added, removed or (SAT-D211) reinstated by a line revision; the THROW precedes every drop. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261102212525_AddDraftLineRevisions.cs"] =
                    new("32e82f8eec6987b42b9a4b1ae631cb6256d0798705bd6f9e29728c748c37766d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SAT-D32: the Down's only raw SQL is Sales' refusal (51212, used by no other migration) while any user closure row or
                // recorded close attempt exists; the THROW precedes the DropTable, an empty table rolls back freely. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261103073447_AddDraftUserClosures.cs"] =
                    new("586dd38857e8a9c76bb654243710782e28d627ce64abc58ea7783f6c8b5c0d97",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SAT-D30 / SAT-D213: the Down's only raw SQL is Sales' refusal (51213, used by no other migration) while any
                // over-delivery approval is recorded; the THROW precedes the DropTable, an empty table rolls back freely. Forward fix.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261107103824_AddDraftOverDeliveryApprovals.cs"] =
                    new("666f5e3e6cc223d58cc46874a5ea36d36c79a55f66f33c331fd6e487a81786d4",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // S5 API-32 (Fatura cekirdegi 1): the Down's only raw SQL is Sales' refusal (51221, used by no other migration) while
                // any invoice draft or invoice attempt is stored; the THROW precedes every drop, an empty store rolls back freely.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261107155919_AddSalesInvoices.cs"] =
                    new("9c3a25864f2e7fed58ef3f6c02cf501f326a1d9306294361149eb95c2fc09db7",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // S5 API-33 (Fatura cekirdegi 2): Up adds the two freeze triggers (TR_Invoices_Guard with the cancelled/rejected share release,
                // TR_InvoiceLines_Guard; 51223 / 51224); the Down's only other raw SQL is the forward-fix refusal (51222) before the DROP TRIGGERs.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108025944_AddSalesInvoiceApproval.cs"] =
                    new("df8d28cfe00b6d587ad474aca1fea1e08bce98a6a3872f6f9279daf4b1197faf",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 3 }),
                // S5 API-35 (Fatura cekirdegi 3): Up adds the append-only TR_InvoiceMatches_Guard (51226); the Down's only other raw SQL is the
                // forward-fix refusal (51225) while any match exists, before the DROP TRIGGER and DropTable.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108091314_AddSalesInvoiceOfficialMatch.cs"] =
                    new("78f3bf9588b31a0a04908076237bfd611ce8937b59fc9ba6c0179998b44a1574",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 2 }),
                // S5 API-36 (Fatura kesinlestirme): Up alters TR_Invoices_Guard (header may also gain the Finalized state) and creates the append-only
                // finalization ledger guard (51232) and the outbox freeze guard (51233); the Down's raw SQL is the forward-fix refusal (51231) while any
                // finalization exists, the DROP of both triggers, and the restore of the previous header guard.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108143000_AddSalesInvoiceFinalization.cs"] =
                    new("43509d583ce1ab2fa35b9546721e9fbf2c457e4546a28250d0e1fff3ac232d9b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 6 }),
                // SAT-D132: Down's only raw SQL is the forward-fix refusal (51227, used by no other migration) while any quote revision carries due terms.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108131842_AddQuoteDueTerms.cs"] =
                    new("61217a679714c6964973ef09bdf1476c27baf4111fb456fb0cd74dec5c2b8bfa",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SAT-D17 (line closure): Up creates DraftLineClosures; the Down's only raw SQL is the forward-fix refusal (51228, used by no
                // other migration; 51227 is AddQuoteDueTerms') while any closure exists, before the DropTable (an empty table rolls back freely).
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108135920_AddDraftLineClosures.cs"] =
                    new("f5a39316e3c68ce0f529184f152ab52d24629830151bf6b70375a54feb24ca51",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // Line identity FKs (review finding): the Up's only raw SQL is a pre-check that refuses (51234, used by no other migration) while
                // any approval, closure or remainder change names a LineId that is not the line at its LineNo, before any DDL (no data-fix policy);
                // the Down is pure DDL (dropping the FKs, indexes and alternate key loses no data).
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108194345_AddDraftLineIdentityForeignKeys.cs"] =
                    new("35b2d66e710fe11d6710eabf69a0d56ab5a259c55986a1e75b523d9700c97989",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SAT-D241 (EK48): the Up is pure DDL (six nullable columns, two checks); the Down's only raw SQL is the forward-fix refusal
                // (51242, used by no other migration) while any match carries the VAT/withholding comparison, before any DDL.
                // The tax check names every column IS NOT NULL in its second branch (2e4c2ae6... -> 3bb21c0b...).
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261110065844_AddInvoiceMatchTaxComparison.cs"] =
                    new("3bb21c0bd25f4a6df6c0284d387e989094ec66c453d6bff9cdc19c6a7fa21a37",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SAT-D252..SAT-D255 (source plan): Up creates DraftSourcePlanLines; the Down's only raw SQL is the forward-fix refusal (51241,
                // used by no other migration) while any recorded source plan exists, before the DropTable (an empty table rolls back freely).
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261109221322_AddDraftSourcePlanLines.cs"] =
                    new("b4961b6aae644bb81eb7bd9b82d84013862f6b0e951f6e89e5f2abb03e47424d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // API-07 (SAT-D153..SAT-D162): Up's two guard triggers (51252 keeps the request facts and allows one decision, 51253 keeps
                // the attempts append-only); the Down's forward-fix refusal (51251, used by no other migration) while any exception exists.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261110155556_AddShipmentRiskExceptions.cs"] =
                    new("40e4e1ca6cf28d81513a567ecc74980ba168ace81c7d4eff2de38b41879b4048",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 3 }),
                // EK56 SAT-D286: Up is pure DDL (the widened checks); the Down's only raw SQL is the forward-fix refusal (51254, used by no
                // other migration) while any withdrawn exception exists, before the checks are narrowed again.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261110192441_AddShipmentRiskExceptionWithdrawal.cs"] =
                    new("4d05b81018565b5461570aeaa4b40b64356706bbccb40a3e670bc520414ecda8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // API-40 (SAT-D288..SAT-D294): Up's two guard triggers (51255 keeps the return's document, lines and totals and allows one
                // step forward, no delete; 51256 keeps the attempts append-only); the Down's forward-fix refusal (51257, used by no other
                // migration) while any customer return or attempt exists, before both DropTable operations.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261110215452_AddCustomerReturns.cs"] =
                    new("3ad749cf9b2dd185f7acbba31d52bac1bf98103f769398c93ca8d2af8eb21cb9",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 3 }),
                // SAT-D290 (2, 3): Up replaces the 51255 guard trigger so a pending return may also move to Rejected and a draft or pending
                // one to Cancelled (both terminal); the Down's forward-fix refusal (51259, used by no other migration) while any rejected or
                // cancelled return or such an attempt exists, then the earlier trigger is restored.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261111090320_AddCustomerReturnRejectionAndCancellation.cs"] =
                    new("3e25438ea4a5e5122da96c84980f552b730c9388d4982854d2ac58f37e2526ff",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 3 }),
                // AT-053: the Down's only raw SQL is Logistics' module refusal (51056, used by no other migration) while
                // any weighbridge record is stored; the THROW precedes the DropTable, an empty table rolls back freely.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20260928120000_AddVehicleWeighings.cs"] =
                    new("beb572dd1dd3450169f3c2b927e143161f2a15daedbd6b391eccc2b598817f4b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // AT-055: the Down's only raw SQL is Logistics' module refusal (51056, the AddVehicleWeighings code) while any
                // dispatch-note draft or line is stored; the THROW precedes both DropTable operations, empty tables roll back freely.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20260930120000_AddDispatchNoteDrafts.cs"] =
                    new("19fd92b05acf08afeed254230091de3aa4828ed66020a76ae02750c3abbee01a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // AT-055 lifecycle (#518): two raw SQL calls — Up's one-statement backfill of the new CreatedStep from NoteNo (a
                // draft's only step before this migration was its creation), and Down's module refusal (51056) while any series row,
                // non-draft note or released line exists; the THROW precedes every drop.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20260930150000_AddDispatchNoteLifecycle.cs"] =
                    new("72191dd5733ec6f8a5497ce436c45df2a271610449e60c6f816837b627990450",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 2 }),
                // Customer returns are immutable; Down refuses while a return or attempt exists before dropping either table.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261003230000_AddCustomerReturns.cs"] =
                    new("ef3a120dcc60ae748c64139c194eda91d9aadfb3878abc6c4ef826be738df281",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // I01.2: Down's only raw SQL refuses rollback with Logistics' transfer-document code 51075 while
                // retained document history exists; the refusal precedes both destructive table drops.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261015163000_AddTransferOutgoingDocuments.cs"] =
                    new("038252750bb4bfc8abdda14c8a1ce5d073104cbdc82020058e0d8bd86694459a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // ERP-000G / AT-065: the Down's only raw SQL is the vault's module refusal (51058, used by no other migration
                // on main or any branch) while any document exists; the THROW precedes every DropTable, an empty schema
                // rolls back freely.
                ["src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20260930120000_InitialDocuments.cs"] =
                    new("9c2911253ccefc7409055fefdce4c2dc77a58108648e2eb7d3a1635526409de2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // ACC-V0a: the Down's only raw SQL is Accounting's module refusal (51048, coordinator-assigned) while any
                // period or command row exists (the preparer/approver columns are NOT NULL facts of Ö4); forward fix.
                // SAT-D216 / SAT-D222: the Down's only raw SQL is Logistics' module refusal (51260, used by no other migration)
                // while any share records its shipper; the THROW precedes the drops, unnamed legacy shares roll back freely.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261111182437_AddLoadingShareRecordedBy.cs"] =
                    new("7238460d4c8a64dd7f93bc47d9edc1fde0eb401a05d54f8a744e59d79951b753",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // F-020: the Down's only raw SQL is Logistics' module refusal (51265, used by no other migration) while any
                // loading instruction is cancelled; the THROW precedes the drops, so an empty store rolls back freely (forward fix).
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261112090000_AddLoadingInstructionCancel.cs"] =
                    new("0f18b6d0ae4a74f59139647884290faf6896778ea9d7e9e3a148d9044d2c2e12",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20260919140000_AddPeriodApproval.cs"] =
                    new("e1e6fe554c5b58ea381316b99d17dd2fd64dd73780e0a475e849715b28ac42f9",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // ACC-V1: Down refuses with Accounting's existing 51048 forward-fix signal while any normal
                // journal draft or line exists; the refusal precedes both destructive DropTable operations.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20260924122703_AddNormalJournalDrafts.cs"] =
                    new("487ed66a9cdd2a4810b2cde704e1270bd0371b7aa2ad9017e186bb0b673337ee",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // B-04b: the Down's only raw SQL is Accounting's refusal (51240, used by no other migration) while any
                // source document admission exists; the refusal precedes every drop. Forward fix; nothing is deleted.
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261108222146_AddSourceDocumentAdmissions.cs"] =
                    new("fe8034a5ec54b40b479bb05d23a1b7ac415604d2c22fbcc96e8220b5380a188a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // FIN-T05: the Down's only raw SQL is Finance's module refusal (51051) while any money event
                // exists or any draft has left Stage 0 — so a rollback can drop neither a money fact nor the
                // trace of a record. Reviewed rather than assumed: the THROW precedes every Drop in the Down,
                // and 51051 is used by no other module. Forward fix; nothing is deleted.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260920090000_AddMoneyEvents.cs"] =
                    new("5af1d703aca150af6ca748c57b42e9f088cec37aaa3841116e31016a35a72eed",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // R47: Down refuses before its first destructive operation while a manual payment,
                // outgoing money fact or command target exists. Empty schemas can still roll back.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260923213000_AddManualBankPayments.cs"] =
                    new("2ab197f7060b5bb166f8ab9dd5aef6b22fc870c42ea400d28073561faa1418b9",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // F-03: Down refuses before destructive rollback while an allocation, open item or
                // source-allocation state exists. Empty schemas can still roll back; populated ones
                // require an explicit forward fix.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260924113000_AddFinanceAllocations.cs"] =
                    new("431c95117e3a0216d30a3341ffd72b58e6274be9b938ecd671f6a725d9f2cc67",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // DEC-FIN-014: the Down's only raw SQL is Finance's module refusal (51051) while any POS fact is
                // stored on a draft or money event; the THROW precedes every DropColumn. Forward fix.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260927120000_AddPosCollectionFacts.cs"] =
                    new("9741c5fbb341827f77056fa578b0ec8403144990066b40c38d5bcdd48d1da23e",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // MOD-15 (FNS-D7): Up's one raw SQL fills Direction = 1 (receivable) on every stored row before the column
                // becomes NOT NULL; the Down's other raw SQL is Finance's module refusal (51051) while any payable is
                // stored, before the constraint and column drops. Forward fix.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261102134844_AddOpenItemDirection.cs"] =
                    new("9af40ee74959eb4524d6166c25a0ebee328cb7a12c9c0fa245fea60e4895fcb8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 2 }),
                // API-23: Up is pure DDL; the Down's only raw SQL is Finance's module refusal (51051) while any payment
                // reversal proposal or recorded payment reversal is stored; the THROW precedes the table drops. Forward fix.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261111184316_AddPaymentReversals.cs"] =
                    new("a17ae3ac73ad47179d26ac7d8a05b3b6d21efa289a3e5fcc54ff127910ede1a8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // API-30: Up is pure DDL; the Down's only raw SQL is Finance's module refusal (51051) while any cash account
                // or cash-account command is stored; the THROW precedes the table drops. Forward fix.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261112070144_AddCashAccounts.cs"] =
                    new("d48f0b982c32c16ed5c011576277a3045fab26dc1fd30a66ffdf68ceada82f8b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // API-30 passivation and reactivation (#66, 13 November 2026): Up is pure DDL (seven nullable change columns,
                // the widened decision check and live-name filter, CK_CashAccounts_StateChange); the Down's only raw SQL is
                // Finance's module refusal (51051) while any cash account is passive or carries a change; the THROW precedes
                // every index, constraint and column drop. Forward fix.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261113161917_AddCashAccountStateChanges.cs"] =
                    new("9edb092789cf1388583bc6cb5aa9972b9ccd45b3118cf6967d183a0e2fc3420e",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // F-005 D1: Up is pure DDL; the Down's only raw SQL is Finance's module refusal (51051) while any official sales
                // invoice reservation, record or command is stored (#66 6860586175: reservations added in place, the
                // designer and snapshot regenerated); the THROW precedes the table drops. Forward fix.
                // FNS-D39: Up is pure DDL (the three unique indexes become filtered to live records, the approval check
                // widens to the rejected and withdrawn states); the Down's only raw SQL is Finance's refusal (51273) while any
                // rejected or withdrawn record is stored; the THROW precedes the index and check changes. Forward fix.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261114073527_AddOfficialSalesInvoiceRejectionAndWithdrawal.cs"] =
                    new("fc2d6e8edabd46c29cb7af28b85f09c54b9197e96c91f0f9517edbcd19f85df8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261111233047_AddOfficialSalesInvoices.cs"] =
                    new("ba2a7e0d86e0ba0b1aa9dcfb02e61394c87ee4ae2ac1a091d902bb4d68b6bcfd",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // F3a / API-20: Up is pure DDL; the Down's only raw SQL is Finance's module refusal (51051) while any payment
                // allocation or payment allocation state is stored; the THROW precedes the table drops. Forward fix.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261107154419_AddPaymentAllocations.cs"] =
                    new("5fee84517da703301d4b2d551731a6458efa4795948e491d457cc14016aba90a",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // F2b: the Down's only raw SQL is Finance's module refusal (51051) while any invoice cancellation or cancelled
                // open item is stored; the THROW precedes the table drops and the status constraint narrowing. Forward fix.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261107131505_AddInvoiceCancellations.cs"] =
                    new("7a1dd608847d3c5a069dc68c1fde39bd209dc01c7ca26493fd593bc1042f0e9c",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // MOD-04 (F2a): the Down's only raw SQL is Finance's module refusal (51051) while any open item carries
                // an exchange rate or functional amount; the THROW precedes the constraint and column drops. Forward fix.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261102220923_AddOpenItemInvoiceExchange.cs"] =
                    new("00f75a4ac79800b13816a01d05a1632e5572d14026f0fdf300a64b7df430e02f",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // DEC-FIN-016: the Down's only raw SQL is Finance's module refusal (51051) while any customer
                // instrument, instrument command or collection money event is stored; the THROW is the Down's first
                // statement, before every drop. Forward fix.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260927180000_AddCustomerInstruments.cs"] =
                    new("594e12d2598d6f4dfba756790666d15d4f493980af10ca6cdf1b9e636e58c3f6",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // MFG-6 slice 2: the Down's only raw SQL is Manufacturing's module refusal (51047) while any plan
                // slitting spec is stored; forward fix, no spec is dropped.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20260919130000_AddPlanSlittingSpecs.cs"] =
                    new("2c9558291e1159f12ec838b7ab8d0a9e468630e384b0777896473a5f3c336f51",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // MFG-PLAN-EDIT: the Down's only raw SQL is Manufacturing's module refusal (51047) while any plan
                // revision is stored; the THROW precedes both DropTables. Forward fix.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20260927180000_AddManufacturingPlanRevisions.cs"] =
                    new("39a0d9bdb1ed7f19934491b4177708af32ef45fadf7c7ebbf1010fa372b6ec23",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // MFG close: Down refuses with the module-owned 51048 while an immutable closure decision exists.
                // The refusal precedes the drop, so the supported recovery is an explicit forward fix.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20260930220000_AddManufacturingOrderClosureDecision.cs"] =
                    new("f20280c9fa754dd73f91bd880c68f6eccfba9d10e9396fcd1b5efe956a0f0996",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // INV-OPEN-1 part 2: each Down's only raw SQL is the Inventory refusal (51046) while an opening
                // count, its posted stock or a command record exists; forward fix, no row is dropped.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260919120000_AddOpeningCounts.cs"] =
                    new("85e41efd96d2e38d19db1ac6bc22fd761203937119e9af44c0c9bb68a2c7d2a9",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260919140000_AddInventoryCommands.cs"] =
                    new("450f545cc1ce2e303794dbf0a80566f18bef41cba222366603e3e35288dbe69d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // LOG-5: Down refuses with Inventory's 51047 forward-fix signal while a definite
                // shipment effect exists; the refusal precedes every destructive rollback step.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260924001222_AddInventoryDefiniteShipmentRuntime.cs"] =
                    new("3b696ed5dff4a414c00e939b5921b2d2d5f30d865627d4ce7b1c0019a6f3835d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260924013338_AddInventoryPhysicalAllocationLifecycle.cs"] =
                    new("5452302167c69f77c89c1cd44e3c4d90639df7bb4020e15f54a01309bd18e3a0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // Label genealogy is durable; Down uses one reviewed statement to refuse
                // schema mutation after any transformation has been recorded.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260924095845_AddInventoryLabelGenealogy.cs"] =
                    new("7ab6813aed35a31711887f953a98f54e975553a5c7bb380f521482d1e6e6d2c2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // Label classification is durable label history; Down refuses before removing populated state.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261002110000_AddInventoryLabelClassification.cs"] =
                    new("5ae139301f3fbd8f0b730a83b47170289e5d643168a4113f9bb83bd4cdc01980",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // AT-037: stock counts, their decisions and CountAdjustment movements are durable facts; the Down's only
                // raw SQL is Inventory's module refusal (51053, as the other Inventory migrations) while any exists, and
                // the THROW precedes every drop. An empty schema rolls back freely; a populated one needs a forward fix.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260927150554_AddInventoryStockCounts.cs"] =
                    new("f466f4c74b927c5885f8d2469aee1a9b53ebdf0a787091b6cb55c5ee3006131f",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // INV-AL-1: allocation changes are append-only history; the Down's only raw SQL is Inventory's module
                // refusal (51053) while any change exists, and the THROW precedes the drop. Forward fix otherwise.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260928120614_AddInventoryAllocationChanges.cs"] =
                    new("64ff8f317b94bc2e8beed57e3346ddab9b2f8181dae7885013baa167f633ccdd",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // ST-02 scrap write-off: stock scraps, their decisions and Scrap movements are durable facts; the Down's
                // only raw SQL is Inventory's module refusal (51053) while any exists, and the THROW precedes every
                // drop. An empty schema rolls back freely; a populated one needs a forward fix.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260928161227_AddInventoryStockScraps.cs"] =
                    new("9a4b4e0fd2233b33b8b95ffca4a9df4b06049dc86b7cfd0fd84e1a47b83424d4",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // B-03: widens CK_LabelTransformations_Kind to (1, 2, 3) (Manufacturing genealogy); the Down's only raw SQL is the
                // Inventory refusal (51262, used by no other migration) while a Kind 3 row exists, and the THROW precedes the check swap.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261111205905_WidenInventoryLabelTransformationKindToManufacturing.cs"] =
                    new("0e75914b6647cf6ee11f83680a1590d354de818b7b4ca24e10e22c164cb255c4",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // B-22a: adds the nullable proved DocumentVersion (positive check) to transfer dispatches; the Down's only raw
                // SQL is the Inventory refusal (51272, used by no other migration) while a dispatch stores a proved version,
                // and the THROW precedes the column drop.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261111231419_AddInventoryTransferDispatchDocumentVersion.cs"] =
                    new("9e5a05561292abbe02449ae15690589d9e967f224a93dec8d921e6a3ecdfaef0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SAT-D297: widens the ledger reason check to 1..9 (zero_cost_receipt); the Down's only raw SQL is the Inventory
                // refusal (51258, used by no other migration) while a reason 9 row exists, and the THROW precedes the check swap.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261111091157_RelaxInventoryAverageCostReasonToZeroCostReceipt.cs"] =
                    new("927921240465ff834888409d12f20282d4634069a452b5c75170fe62e5c92946",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // SAT-D262: the average-cost ledger is a durable fact; the Down's only raw SQL is the Inventory refusal
                // (51250, used by no other migration) while any ledger row exists, and the THROW precedes the drop.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261110065424_AddInventoryAverageCostLedger.cs"] =
                    new("6605994dc4912511c0a1af641f67b304c2f135378db41a16a54374c96c4a002e",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // R17: Down refuses before dropping manufacturing-return state while durable facts exist.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260924193000_AddInventoryManufacturingReturn.cs"] =
                    new("a75260a68cb041c3c3f1bb63141e6ef719a4a556391bfa43b0c1f1aef4fd7c34",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/20260912175128_InitialProductDrafts.cs"] =
                    new("09b167e9837c486f8487bc8a30518d37a98ee90d6cea5842a40df6fd7c7e00f8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // PRD-FAM-1 (#334): the Down's only raw SQL is Product's existing rollback refusal (51044)
                // while any card or registry row exists; forward fix, no row is dropped. Re-measured for
                // ORG #2 P3-1 (CK_TemplateFields_Candidate in Up); the Down is unchanged.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/20260919100000_AddProductFamilyRegistry.cs"] =
                    new("8a7f1a3e29cf5c1855797ccf2008bf09789e141ef76ab9169201789e415fe649",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // Product's initial technical registry release is durable. Down first refuses when the
                // accepted family/template/link/field/entry footprint or a terminal card reference exists;
                // only the unchanged, unreferenced release can be removed.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/20261004130000_ReleaseInitialProductTechnicalDefinition.cs"] =
                    new("b57c95cb4f4c65b6a254c7f83b76473cead3d979b58535ec85955d1c03449467",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 2 }),
                // The accepted initial Product code group is durable. Down refuses when its identity,
                // byte-exact name or prefix has drifted or any Product/card/import replay reference uses it.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/20261004213000_ReleaseInitialProductCodeGroup.cs"] =
                    new("f5f455d90e4758107648997743788dab5f511bb3a01a87676a1d832a0563290b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 2 }),
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20260912175120_AddExplicitProductAssignments.cs"] =
                    new("b366831c39d682f7f1d45664300082806248a8b54910799c83adfcca3f64d14b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20260912175124_InitialGroupSharedTerminals.cs"] =
                    new("ed1ef07eb88ee1ea324479d5ab498d213ecf3cfdba4cf5ab4eae49292c4dc4c7",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // #329: each Down's only raw SQL is the module's existing rollback refusal (51042/51043).
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20260918230000_AllowProductActivateAssignments.cs"] =
                    new("7b5ca37647eb351caa33e1dcc24b5c726e9d732ab2b4e4e434e5c285d4f2dd30",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20260918230100_AllowProductActivateTerminals.cs"] =
                    new("4716d015c2cd1a883d84d95589654f2d57077c06c8c641f2f148234453138c15",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // DEC-CAR-003 / PRD-PERM-001: widen the same checks to Deactivate/Reactivate; each Down's only raw SQL is
                // again the module's existing rollback refusal (51042/51043) while such a row is stored.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20260928120000_AllowProductLifecycleAssignments.cs"] =
                    new("a5a3b8dd24acab43331d6e2558c9c885fdf9a19db4ecafa927f81169587964ae",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                // ID-1: the Down's only raw SQL refuses (51280, used by no other migration) while a local credential
                // exists; Up is plain EF schema operations.
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261114164716_AddLocalCredentials.cs"] =
                    new("bc8b5203f4181c7719470391984baa2e95da8d3b4bc92eaefdbad970bdfa5ff0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20260928120100_AllowProductLifecycleTerminals.cs"] =
                    new("407833cc812c41921226bb8bbbddf182c15e12549bdc1569147b6d161d965b9f",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Persistence/Migrations/20260912173914_AddGlobalProductAuditSubject.cs"] =
                    new("e0bc0755399eb10c87793f35c832ce01ea80c4ed7d2c4c02fda465a3feae2df5",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/"
                 + "Persistence/Migrations/20260827160529_InitialAudit.cs"] =
                    new(
                        "356d30c600bccd0f8e134a0d25208336da8bd2a6d6e8fb5ed6387ac92ca72278",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // AI-2a: Up creates TR_SecurityEvents_RejectUpdateDelete (THROW 51271, append-only); Down refuses
                // (51270, used by no other migration) while security events exist, then drops the empty table.
                ["src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/"
                 + "Persistence/Migrations/20261111205852_InitialSecurityEvents.cs"] =
                    new(
                        "4ac64ee93c5972ef917b045aecab74a86c673e11a3dfd940333c6d1c1a179f08",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 2
                        }),
                ["src/backend/Modules/Idempotency/"
                 + "GoymenERP.Modules.Idempotency.Infrastructure/Persistence/"
                 + "Migrations/20260827175405_InitialIdempotency.cs"] =
                    new(
                        "298b1aed84ba4f5ba663b90076a0914ee87d49d7016eacf71e8ee9c6128e1e9d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                ["src/backend/Modules/Organization/"
                 + "GoymenERP.Modules.Organization.Infrastructure/Persistence/"
                 + "Migrations/20260831142045_AddCompanyFiscalRuntimeSeed.cs"] =
                    new(
                        "27bc99e999554b75715f834aec63b0e5d2bf81a00c70dc18b66eba09266b1f02",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, finans) — 20261116080924_AddOpeningBalances: Down refuses (51809) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20261116080924_AddOpeningBalances.cs"] =
                    new(
                        "1d600c87b4adc71f3f457a2abfe30d3d444cd7d73008fdc7824d34d96c417940",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, finans) — 20261116104128_AddOpeningBalanceReversalsAndMovements: Down refuses (51841) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20261116104128_AddOpeningBalanceReversalsAndMovements.cs"] =
                    new(
                        "c18a436267b7956e4254fc8fe99f9ea77d0be7aad0421c6d6dfca05dc1ec79a8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, finans) — 20261116111738_AddCreditAccounts: Down refuses (51861) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/"
                 + "Persistence/Migrations/20261116111738_AddCreditAccounts.cs"] =
                    new(
                        "ebfe50d03c15ce3d48fa4fa1c718860eeaa3369802748b7a8107b957b7b47851",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, stok_lojistik) — 20261115235658_AddInventoryTransferReversals: Down refuses (51722) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261115235658_AddInventoryTransferReversals.cs"] =
                    new(
                        "9fed5c19c807b7839ea4c2b3775e956d1947e909af4a0f9c69bb5392980bb47b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, stok_lojistik) — 20261116000110_AllowInventoryZeroBookStockCounts: Down refuses (51748) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261116000110_AllowInventoryZeroBookStockCounts.cs"] =
                    new(
                        "1cc44f324c812558d2933f3426ecc3388c6da62db9b5b1c5aefa9f0b52d0dcf0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, stok_lojistik) — 20261116022005_AddInventoryTransferLossRequestNumbers: Down refuses (51811) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261116022005_AddInventoryTransferLossRequestNumbers.cs"] =
                    new(
                        "a5011ee3c7e513b9d5ab00b37398cae9e63c8defef11d640037a128407867984",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, stok_lojistik) — 20261116064346_AddInventoryOpeningCosts: Down refuses (51712) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261116064346_AddInventoryOpeningCosts.cs"] =
                    new(
                        "dac68386439bbec37826490bc9d4fb8bb1b8071966f471e427fbe2a812c75c4b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, stok_lojistik) — 20261116081500_AddInventoryTransferOverageRenewalsAndLossWithdrawals: Down refuses (51836) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/"
                 + "Persistence/Migrations/20261116081500_AddInventoryTransferOverageRenewalsAndLossWithdrawals.cs"] =
                    new(
                        "916045b0625ccd9b2e014778275d6778c7b93e8a21bd438286df3759884c1e62",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, stok_lojistik) — 20261116003008_AddWeighingDifferences: Down refuses (51761) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/"
                 + "Persistence/Migrations/20261116003008_AddWeighingDifferences.cs"] =
                    new(
                        "80c87d21cb575bbfc39ef893aa7030af0c42fe45398f9f4e9fbfa035dafc1fcc",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, stok_lojistik) — 20261116010428_AddShipmentRiskGateOutbox: Down refuses (51787) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/"
                 + "Persistence/Migrations/20261116010428_AddShipmentRiskGateOutbox.cs"] =
                    new(
                        "b79f2003556ae7cd03886832c0677a399b7785570578c54e9268b7a62b49059b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, uretim) — 20261115232234_AddManufacturingOutputLabelCodes: Down refuses (51731) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/"
                 + "Persistence/Migrations/20261115232234_AddManufacturingOutputLabelCodes.cs"] =
                    new(
                        "e59c01aca3596f841f1155165a8ad82104456308d39a06f6a622bba8ea98f9d7",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, uretim) — 20261116104108_AddManufacturingOrderCancellation: Down refuses (51870) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/"
                 + "Persistence/Migrations/20261116104108_AddManufacturingOrderCancellation.cs"] =
                    new(
                        "738533a362038cb4533cc0068c83138c763ecfe132ae127f5c348295fc5a7747",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, kimlik) — 20261116111056_StageCompanyTaxIdCorrections: Up stages one Pending tax-id correction per placeholder company still holding the stand-in VKN (no company row is rewritten); Down refuses (51877) once a correction was applied (forward fix). Two Sql (guard count); K3 review pending.
                ["src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/"
                 + "Persistence/Migrations/20261116111056_StageCompanyTaxIdCorrections.cs"] =
                    new(
                        "f8822d51cdb39d832f42f80d525b21cfbe526fb421f480461e31d49c2da6a192",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 2
                        }),
                // tren-1 W5 (integration, stok_lojistik) — 20261115234445_AddProductPackageStandards: Down refuses (51492) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
                 + "PriceLists/Migrations/20261115234445_AddProductPackageStandards.cs"] =
                    new(
                        "5818371e1366a6425b08543a3860aa02043fadc269e0e926341b11ea25283097",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, stok_lojistik) — 20261116015957_AddProductSectionFamilies: Down refuses (51737) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
                 + "PriceLists/Migrations/20261116015957_AddProductSectionFamilies.cs"] =
                    new(
                        "4ee5e65acb4cfabf662ad65a4f8d1c17303e9fb3f88a5b3f0aae48e84645feb0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, stok_lojistik) — 20261116111051_AddProductPackageStandardApprovalAndFiles: Up creates the append-only guard trigger on ProductPackageStandardImportFiles (51871); Down refuses (51872) while import files or approval rows exist, then drops the trigger (forward fix). Three Sql (guard count); K3 review pending.
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
                 + "PriceLists/Migrations/20261116111051_AddProductPackageStandardApprovalAndFiles.cs"] =
                    new(
                        "16a1641b586d48cadd456c2492c48d816cf29ff9dd428800a423313682e5ed17",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 3
                        }),
                // tren-1 W5 (integration, alis_cari) — 20261116104436_AddPurchaseOrderExchangeRate: Down refuses (51833) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261116104436_AddPurchaseOrderExchangeRate.cs"] =
                    new(
                        "ce95efcbd5657838b2d6dc555fe71823dd2ddbf4f297bb657f2937c0540ba6d8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, alis_cari) — 20261116113129_AddPurchaseSupplierReturns: Down refuses (51893) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/"
                 + "Persistence/Migrations/20261116113129_AddPurchaseSupplierReturns.cs"] =
                    new(
                        "b5ee6dfc1f18267a7ae4cf666ab76f8a44fadc8cbf29fe4cabe660cf613bda76",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, satis) — 20261116001237_AddShipmentRiskExceptionUsages: Up creates the append-only guard trigger on ShipmentRiskExceptionUsages (51487); Down refuses (51488) while mirrored usages exist (forward fix). Two Sql (guard count); K3 review pending.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/"
                 + "Persistence/Migrations/20261116001237_AddShipmentRiskExceptionUsages.cs"] =
                    new(
                        "ff7f1d3303fc0d5328bdd0b813c4ed1319efa669342fbd43a7b1ee6b335ee909",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 2
                        }),
                // tren-1 W5 (integration, satis) — 20261116002458_AddSubcontractServiceInvoices: Down refuses (51732) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/"
                 + "Persistence/Migrations/20261116002458_AddSubcontractServiceInvoices.cs"] =
                    new(
                        "2b683bd5c70d7272be4b5198ae8e39d71f4fc7e9e633f50ade0a69109884ebd2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, kimlik) — 20261116100000_AddFounderRecoverySecurityEvents: Down refuses (51851) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/"
                 + "Persistence/Migrations/20261116100000_AddFounderRecoverySecurityEvents.cs"] =
                    new(
                        "05c6d20a803a45261997de2e1c635bd0514ab4dc724d2ca03fd73b3659cc2935",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, kimlik) — 20261116002246_AddUnitProfileVersions: Down refuses (51341) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261116002246_AddUnitProfileVersions.cs"] =
                    new(
                        "10d8d47a5fe2cae54746803679e73f58a0631c0246bcaecaeae9818a665de817",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, kimlik) — 20261116002821_AddWorkCenterCapacityVersions: Down refuses (51333) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261116002821_AddWorkCenterCapacityVersions.cs"] =
                    new(
                        "6552d97c07409d08f2de125aa09a5f0ea73246f7e8783f31929a1dce4443ba4b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, kimlik) — 20261116004451_AddLoginRateLimitTrustedProxies: Down refuses (51734) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261116004451_AddLoginRateLimitTrustedProxies.cs"] =
                    new(
                        "a5aaa9ceb82fb316bedc25c2277fe4ff79a2f0bb91601fae575a58d8ae4b5404",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, kimlik) — 20261116103621_AddCompanyPolicyVersions: Down refuses (51853) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261116103621_AddCompanyPolicyVersions.cs"] =
                    new(
                        "25830290ee317d9ca0e1a5f4fd0c40b84336467d0a8617acb7b9c1944f5d4466",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, kimlik) — 20261116120112_AddReferenceListEntries: Down refuses (51886) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261116120112_AddReferenceListEntries.cs"] =
                    new(
                        "2e8e32e2939dbec73f1c64a5b84c348ce68eccd8392d2daed31af627d2bd77c2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // tren-1 W5 (integration, kimlik) — 20261116120328_AdoptManufacturingProfileSingleFlow: Down refuses (51345) instead of losing data (forward fix). One Sql (guard count); K3 review pending.
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/"
                 + "Persistence/Migrations/20261116120328_AdoptManufacturingProfileSingleFlow.cs"] =
                    new(
                        "9512f80fbcf4763df0b75bd7125f137699ffd0456e5418fb02e59a1be869eee2",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Manufacturing cost-difference source or human decision exists; use a forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117000534_AddManufacturingCostDifferenceProposals.cs"] =
                    new("51923346b5c45e5e7df4b518c8544a6e3920a19253530b04c4e8644004f108cf", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; An immutable credit movement proposal source exists; use a forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117005516_AddCreditMovementProposals.cs"] =
                    new("8cb16498b72880e56ab2e8e1186fc8d9d54ca36ea77a7b5d78a20879b0fb2b37", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Invoice line COGS correction history exists; AddInvoiceLineCogsCorrections is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117095844_AddInvoiceLineCogsCorrections.cs"] =
                    new("b6665d4a933d06d773d80251a940412d318861c4fdede2455c528435ad85c1f7", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Received Finance source provenance exists; AddFinanceJournalSources is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117102540_AddFinanceJournalSources.cs"] =
                    new("fb65bfec47d4c6be4a6df5ca215bfb3705d12ffad8a53924185e1a203d5b23c3", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Source voucher approval, cancellation or official document facts exist; AddSourceVoucherLifecycle is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117123611_AddSourceVoucherLifecycle.cs"] =
                    new("8911e81f66cadb87310c6add8222d251a40efae490bb98092c3d9914a0bd15b3", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Period closure and reopening history exists; AddPeriodClosureCarryAndReopening is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117154311_AddPeriodClosureCarryAndReopening.cs"] =
                    new("4fbb25886fb66576bfbc977a82b43091c601004cd722c618bee06a5ab96073ea", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; COGS mapping choices exist; WidenPostingMappingsToCogs is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117171929_WidenPostingMappingsToCogs.cs"] =
                    new("e3c1dbacf6b43bc6319e5d2a566aa1f61c25f25d90fddb60b23a145b36083699", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Owner-only new immutable/forward-only evidence trigger(s), THROW 51930,51931,51932; Down refuses history loss before owner schema rollback. No data backfill or cross-module DML in inspected SQL. 3 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117175214_AddInventoryCostProposals.cs"] =
                    new("2f1a32d058444cbd70bb24d6704d4a6d2eafbd308703a0a583ba8ec5c9373667", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 3 }),
                // Tren-1 W8; K3 review pending. Owner-only new immutable/forward-only evidence trigger(s), THROW 51800,51801,51802; Down refuses history loss before owner schema rollback. No data backfill or cross-module DML in inspected SQL. 2 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117190022_AddPostedManufacturingCostAttribution.cs"] =
                    new("75ddbe7f68f3bb1f46b46db2099dc4778920e684dba26dc91aab589b8e8df629", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 2 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Exact component rounding evidence exists; AddInventoryCostComponentRounding is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117194231_AddInventoryCostComponentRounding.cs"] =
                    new("61b7744e87199289f618aa1ec38788381e73b4d05b75e3be95c30b2947d28be5", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Owner-only new immutable/forward-only evidence trigger(s), THROW 51804,51805,51806; Down refuses history loss before owner schema rollback. No data backfill or cross-module DML in inspected SQL. 2 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117205436_AddPostedPurchaseCostAttribution.cs"] =
                    new("605a1640baad035b3c59e3b34a1d031fd28f14b911056fbdb3484136905acca6", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 2 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Cash exchange source evidence exists; AllowCashExchangeJournalSources is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117213704_AllowCashExchangeJournalSources.cs"] =
                    new("090593568186c67313d24d68a03c226ed5da6fa550ff7251a39cee80e2c6eb2b", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Owner-only new immutable/forward-only evidence trigger(s), THROW 51812,51813,51814; Down refuses history loss before owner schema rollback. No data backfill or cross-module DML in inspected SQL. 2 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117223234_AddInventoryCostComponentCancellations.cs"] =
                    new("31f37361161b5b320662ac4a94db9f63e391ea47c1e849cb0dbd9111636417cc", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 2 }),
                // Tren-1 W8; K3 review pending. Owner-only new immutable/forward-only evidence trigger(s), THROW 51840,51842,51843; Down refuses history loss before owner schema rollback. No data backfill or cross-module DML in inspected SQL. 2 lexical Sql call(s).
                ["src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117232712_AddInvoiceCogsClaimSuccessions.cs"] =
                    new("5dfc6d82c93a3877e83fde70ee7fcd3cf7a718b5c237f572f59134abc21e9bd1", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 2 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Typed System audit history exists; AddTypedFounderRecoverySystemAudit is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Persistence/Migrations/20261116220434_AddTypedFounderRecoverySystemAudit.cs"] =
                    new("2eaba0016516fd9541dd91d55ff66e958c019c2fac2e7db868c34da690d08c20", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Self-service credential audit history exists; AddLocalCredentialSelfServiceAudit requires a forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Persistence/Migrations/20261117003912_AddLocalCredentialSelfServiceAudit.cs"] =
                    new("8e3265b87aff8462b16f246689f5e5fdd5136db263b1c9b3405a6c0397e7ffec", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Platform credential recovery audit exists; AddPlatformCredentialRecoveryAudit requires a forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Persistence/Migrations/20261117160915_AddPlatformCredentialRecoveryAudit.cs"] =
                    new("a0c09cc3546cb4025ecdda1b2ada6709dd7d58c2b0c8d6501d2fdef0612cb925", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; UBL invoice source exists; AddPurchaseInvoiceXmlContentKind is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261117112418_AddPurchaseInvoiceXmlContentKind.cs"] =
                    new("a79686204a833c18e35c9fccdd55be81e789dd17412ec00e1f870ba7ecae987d", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Document branding or publication evidence exists; AddDocumentBrandingPublications is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261117140908_AddDocumentBrandingPublications.cs"] =
                    new("cc36054353dd505cdebd58b455e06a99bf7903c6d023a524e7e37f6f3b436243", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Credit source profiles or bank charges exist; AddCreditSourceProfiles is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261116233417_AddCreditSourceProfiles.cs"] =
                    new("770b93ee56c293cbba7ba744fa8fbd08859d8d7eb3373718537624e477489a17", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Official invoice source dates exist; use a forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117000542_AddOfficialSalesInvoiceSourceDate.cs"] =
                    new("7d53de1ad7d9d3267f5065b738a1cd44e3e37101fd7008af357e81b827c1d423", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Credit actual source, command receipt or outbox event exists; use a forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117005509_AddCreditActualMovements.cs"] =
                    new("552c40158bf69769ad184ccd9aeed9b521ecddfe1678b747350a50b7ee3e079c", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Credit bank/group limit source history exists; preserve it with a forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117012305_AddCreditBankGroupLimits.cs"] =
                    new("7516e0b2cae0b7f6c30391b0c7cdface4cafae44fb53ce1989cb70d67e472003", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Party balance transfer or allocation beneficiary evidence exists; AddFinancePartyBalanceTransfers is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117093515_AddFinancePartyBalanceTransfers.cs"] =
                    new("17c8a81438c4c40a7c7833e6fea8bc6a42a2f5f24361269f7ec13975b35f42dd", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Period carry source or reversal proof exists; AddPeriodCarryVouchers is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117154319_AddPeriodCarryVouchers.cs"] =
                    new("2f8843fa7207771188e2fe3b9689582b0b01e106eed4d7c6a9c8a4a1ca6c321d", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Recorded exchange source evidence exists; AddCashTransferExchangeSnapshots is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117175952_AddCashTransferExchangeSnapshots.cs"] =
                    new("160f82e3e1399e867cc5269d6ef3b14f168d2671c930880170543fc2b534289e", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Recorded finance document number proof exists; AddFinanceRecordedDocumentNumbers is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117200743_AddFinanceRecordedDocumentNumbers.cs"] =
                    new("530d732675dc055b3b1f8ad871e659c01893718802ccd0f7f530cba1ab1ec294", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Owner-only new immutable/forward-only evidence trigger(s), THROW 51912,51913,51914,51915; Down refuses history loss before owner schema rollback. No data backfill or cross-module DML in inspected SQL. 4 lexical Sql call(s).
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117212319_AddCashExchangeBookValues.cs"] =
                    new("4b796833e8ee92039effd8868d64e8dbb0f138f30ab7a78421e35ee5af48b096", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 4 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Explicit collateral bank proof exists; AddCollateralBankProof is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117220054_AddCollateralBankProof.cs"] =
                    new("90aa325edba4dc64deb81eb8cd6590b644e06b6b92aab408414fcf7e5df9248c", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Explicit COGS classification evidence exists; AddProductCogsClassificationTerminals is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20261117082927_AddProductCogsClassificationTerminals.cs"] =
                    new("b56cc14affae99cd499f5f2f53c38d73c3ddabe749c7977bbea75b2db9a81549", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; AddExplicitCompanySetupAuthority history exists; rollback is refused (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261116205256_AddExplicitCompanySetupAuthority.cs"] =
                    new("641b74d691ef5dadd04659ac2d9460d3c5d3e4116e4f3571601b7745e42fc105", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Two-factor credentials or command evidence exist; AddLocalTwoFactorSecrets is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261117004612_AddLocalTwoFactorSecrets.cs"] =
                    new("74870470e4301b0899189f7b1876da65abf89da8a24307e9a2018446b0e3756d", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Explicit COGS classification evidence exists; AddProductCogsClassificationAuthority is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261117082918_AddProductCogsClassificationAuthority.cs"] =
                    new("1b02d353c6a49f74f3c14a106e4c7dea0ac059e1769c59cee5ffddd66f1722fb", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Owner-only new immutable/forward-only evidence trigger(s), THROW 51922,51925; Down refuses history loss before owner schema rollback. No data backfill or cross-module DML in inspected SQL. 2 lexical Sql call(s).
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261117160905_AddOperatorTwoFactorRecoveryAttempts.cs"] =
                    new("4b499e3f856fb4da64b5b709a9ced08c928b678e86032a605525d26d707e2b01", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 2 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Receipt reversal history or movements exist; AddInventoryGoodsReceiptReversals is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116210057_AddInventoryGoodsReceiptReversals.cs"] =
                    new("0778c5f84f323a61e219f361a3a357f06a7b9f14aed48d316256016ee3ab13da", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Inventory movement cost provenance exists; AddInventoryMovementCostProvenance is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116232813_AddInventoryMovementCostProvenance.cs"] =
                    new("c52bfc58adf18d074f3acbeaaf5c85ea28d7d3c4ef9942e9ca1131c7c18b862c", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Inventory exact movement cost provenance exists; forward fix is required. 2 lexical Sql call(s).
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116235649_AddInventoryOutboxCursorAndExactCostRatios.cs"] =
                    new("11e2b783b1b671a9bf0cb98f1d707364e2e7dac8b716b8dff9dbf40843a8353f", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 2 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Exact cost pool or recognition mass provenance exists; AddInventoryExactCostPools is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117003341_AddInventoryExactCostPools.cs"] =
                    new("220178b663ba7477f5b38426bbd9a6ef4ad691d625db0f15dd93fa780065acc1", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Immutable first-entry or exact issue FX proof exists; AddInventoryFirstEntryExchangeRateProof is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117010119_AddInventoryFirstEntryExchangeRateProof.cs"] =
                    new("81f3418a14b90870fd2fda820f9b6cbe8a62ff6675938c0bfa8139f9177fbd26", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Negative stock attempt history is append-only; use a reviewed forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117091723_AddInventoryNegativeStockAttempts.cs"] =
                    new("02a8fef9fe8d112a48bc5763f4a7e3c5bda7f7acdd5a6e59366edeeb19d2522b", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Negative stock count-boundary proof is retained; use a reviewed forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117095345_AddInventoryNegativeStockCountBoundaries.cs"] =
                    new("9e895764548defb2a74e4c77a9c6900e0718f91265fe865550a2d9c3e21f4f61", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Transfer receipt document history exists; AddInventoryTransferReceiptDocuments is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117122205_AddInventoryTransferReceiptDocuments.cs"] =
                    new("5074d0c03b98dd09d44854d091c3349c1c0b330e7b503001082b57c43102bf19", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Customer-owned subcontract allocation evidence exists; AddInventorySubcontractDeliverySources is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117140912_AddInventorySubcontractDeliverySources.cs"] =
                    new("3b895c4c9d1867a446ea3372a41c1348810ac6f1f1783f6d7865d0456e111b43", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Owner-only new immutable/forward-only evidence trigger(s), THROW 51926,51927,51928; Down refuses history loss before owner schema rollback. No data backfill or cross-module DML in inspected SQL. 3 lexical Sql call(s).
                ["src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117165518_AddPurchaseInvoiceCostApplications.cs"] =
                    new("d030c29e2862cb59d8701d260e8d14bdb4ef7e8ffb271abfb216798b0af5b189", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 3 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Dispatch kilogram source allocations exist; AddDispatchKilogramAllocations is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117010812_AddDispatchKilogramAllocations.cs"] =
                    new("67b9b0cab9569062490b2b4d77ed5ebb4f56fabed30129e1f7db48695c316bb5", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Transfer departure, association or no-exit history exists; AddTransferDepartures is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117111253_AddTransferDepartures.cs"] =
                    new("55903d515b4d99af93b8fc6c60588e603ea78edc8bb9b1f1136251c19c69a269", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Subcontract delivery, correction or replay evidence exists; AddLogisticsSubcontractDeliveries is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117151710_AddLogisticsSubcontractDeliveries.cs"] =
                    new("66a0051d39d32eb336ac4cd5fe2429400d1578d9e469a6a2154f6069be8e3bc1", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Owner-only new immutable/forward-only evidence trigger(s), THROW 51817,51818,51819,51820; Down refuses history loss before owner schema rollback. One pending1/version1 to approved2/version2 exception transition only; evidence fields stay fixed. 4 lexical Sql call(s).
                ["src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117233641_AddLogisticsShipmentPlanExcess.cs"] =
                    new("9a3a5579147cdf1e31d30f7caa137d3a3da04363efa81c8017a2b2f5ddc6275a", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 4 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Recorded work-center queue history exists; AddManufacturingManualQueues is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116210842_AddManufacturingManualQueues.cs"] =
                    new("9e89fe6607f60f88bf23c5985bf800ca672e6f2a1f3b68da1cbe7b37f6d2f281", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Recorded reopening or multiple closure generations exist; AddManufacturingOrderReopenings is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116214107_AddManufacturingOrderReopenings.cs"] =
                    new("177548934fcefa7f94f76edd4ebeb946b45d862e503038fef0933a3a3a12d7d2", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Immutable manufacturing cost generations exist; AddManufacturingCostGenerations is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116232039_AddManufacturingCostGenerations.cs"] =
                    new("b48ac008b61994fbf0dcf756d82ff5e3e33b13b3da54a31bccc531ccfe2fea0d", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Actual non-kg consumption history exists; AddManufacturingNonKgInputFacts is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116234916_AddManufacturingNonKgInputFacts.cs"] =
                    new("9eeb1d8145841223da49d66cbe30e72c32557578f000182c6f66ca3ff4c635c6", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Stored paint owner references would be lost; AddManufacturingPaintSelections is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261117014136_AddManufacturingPaintSelections.cs"] =
                    new("f0364d671f6b5b179567f6372687ae9f6c9d18252ce7922e6be841afd88299b8", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; AddAuthorizedCompanyOpening history exists; rollback is refused (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261116205304_AddAuthorizedCompanyOpening.cs"] =
                    new("d8188f063f4bbf7428c75ec9f5245e146ca27925d88de187a13219bfd53a6a0c", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Explicit COGS classification evidence exists; AddProductCogsClassifications is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/20261117082903_AddProductCogsClassifications.cs"] =
                    new("d204be52e71238aa76c5e9a8e0ab74444a9e0642487643effb01bbc631626332", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Paint membership source, history or command answers exist; AddProductPaintMemberships is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261117013540_AddProductPaintMemberships.cs"] =
                    new("2c7290a512b1decc59d86621ca150237181f7d3c61de84734993c50977599afc", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Up copies existing cancelled purchase orders into new PurchaseOrderCancellationHistory, preserving CompanyId/OrderId/version/actor/reason/time, only Status=3. Down51949 refuses any adjustment, cancellation, receipt or supplier-return history. No cross-module DML. 2 lexical Sql call(s).
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261116213347_AddPurchaseReceiptAdjustments.cs"] =
                    new("6534bd550b87b2cd0d98f7a6ba3233980f60e6476ba5aea2b9c3edbec99fbbc9", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 2 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Recorded supplier-return response snapshots require a forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261116215806_AddPurchaseSupplierReturnResponseSnapshots.cs"] =
                    new("0d0274ee83e51843cfe0e833622d4a69ed5601d1e26c173df4e25bda2facb748", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Receipt adjustment batch or outbox cursor history exists; AddPurchaseReceiptAdjustmentBatches is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261116222957_AddPurchaseReceiptAdjustmentBatches.cs"] =
                    new("4a268970bcdebd1029964588b824011fa5809bd31181663c7432d51be6c01493", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Purchase order number history exists; AddPurchaseOrderDocumentNumbers is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117002600_AddPurchaseOrderDocumentNumbers.cs"] =
                    new("5637b2475144747a429a387108b53e69fcd733605fd78d7c0ce639cbc415e74c", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Supplier revision or invoice binding history exists; AddPurchaseSupplierRevisions is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117072202_AddPurchaseSupplierRevisions.cs"] =
                    new("2ee0321ca47fdeee9ab673832b2a28ffadd0f9533a99e7079672a832df37a944", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Incoming invoice source, history or terminal records exist; AddPurchaseIncomingInvoices is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117084001_AddPurchaseIncomingInvoices.cs"] =
                    new("b39355d6aac1cf8e6372106682efd4f5ba1dd3c012ed3dcd608be92f55672f7c", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Service order, acceptance, invoice binding or terminal history exists; AddPurchaseServiceOrders is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117110054_AddPurchaseServiceOrders.cs"] =
                    new("57b9fc8eb27f584e9e422778bcc2b1de6eb61b68e32acfd8aa71bbd93770dd2d", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Invoice import owner identity exists; AddPurchaseInvoiceImportReservations is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117112436_AddPurchaseInvoiceImportReservations.cs"] =
                    new("3278858d7fb61bb296cd1a022bc5e9a87321aa6fca35d17c0340d0f8133afa53", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Additional purchase expense history exists; AddPurchaseAdditionalCosts is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117114517_AddPurchaseAdditionalCosts.cs"] =
                    new("2baebc361924345f4823d54f47aa9465e874ce9666ad926a9eee5f162ba5e293", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Purchase expense invoice matching history requires a forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117120452_AddPurchaseAdditionalCostInvoiceBindings.cs"] =
                    new("dd5de753e963574b8552aaee95d5f0022440800f46d5ceb26ec28e46537da761", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Official invoice source evidence exists; AddSalesInvoiceOfficialSourceProof is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117081514_AddSalesInvoiceOfficialSourceProof.cs"] =
                    new("25cbcc65bbab142cd8a9e3e4f9b3f6c43a8faeae401cdd48dd1e3731852487ad", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Sales outbox events retain cursor evidence; AddSalesOutboxCommittedCursor requires a forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117084843_AddSalesOutboxCommittedCursor.cs"] =
                    new("3aceeb931798464c76be74153d0e1d611228593c0d87e0f96fcb6c29b7c8db37", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Recorded approval margin cost evidence exists; AddSalesMarginCostSnapshots is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117105309_AddSalesMarginCostSnapshots.cs"] =
                    new("9b32e6cd4a2a907e341b675d40c5590bf40be7bbdbcdc9b1720619d24cbd2f3c", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Up copies existing sales InvoiceMatches into new OfficialInvoiceClaims with CompanyId/InvoiceId/official number/ETTN and source kind dispatch; new owner append-only/lifecycle triggers51997. Down51994 refuses non-reconstructable lifecycle/identity evidence before dropping new triggers/tables. No cross-module DML. 6 lexical Sql call(s).
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117113343_AddSubcontractInvoiceLifecycle.cs"] =
                    new("9750c2069e5318e97ec5283475aa8d9c06b185495450310bc63734fed04da966", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 6 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Exact subcontract invoice command response history exists; AddSubcontractInvoiceReplaySequence is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117155825_AddSubcontractInvoiceReplaySequence.cs"] =
                    new("8e8977d99ab13cf9a075b2b2dfefd711c4237186ce5d053b72a4842686cc0c3a", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. One lexical Sql call runs SalesOfficialInvoiceCancellationGuards.DownSql. The guard51919 refuses cancellation/attempt/delivery or state7 history before dropping new triggers; Up calls the separately fingerprinted helper to install owner cancellation/lifecycle guards. Both files must be pinned. 1 lexical Sql call(s).
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117195343_AddSalesOfficialInvoiceCancellation.cs"] =
                    new("ffe4980fd1853d3afa0d3426dafb8b79454611e0b7e90e91ba604995ad5da3a4", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8 SAT-D96; K3 review pending. Forward repair: Up and Down reapply only the sales invoice guard with exact BIN2 actor comparison. No data/schema loss, new THROW, grant or cross-module write; CompanyId/version/time checks remain. 2 lexical Sql calls.
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261118050006_RepairSalesOfficialInvoiceCancellationActorCollation.cs"] =
                    new("5ff0aa6e239af9739c473066998325f2d95478ac7685782a3dcb89a7841dd6d0", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 2 }),
                // Tren-1 W8; K3 review pending. Seven lexical Sql calls install three immutable cancellation evidence triggers51916/17/18 and alter four sales-only existing guards. Normal finalized6 to officially-cancelled7 requires exact same-company immutable cancellation fact; invoice line release is one same-owner UPDATE joined by CompanyId and InvoiceId. DownSql51919 blocks recorded cancellation history then restores old guard definitions. Reuses existing51223/24/32/51997 intentionally; no new cross-module write. 7 lexical Sql call(s).
                ["src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/SalesOfficialInvoiceCancellationGuards.cs"] =
                    new("11d2eb4d9eafee146372461936adffe7f64514fb463a500a16230ff1e26757ff", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 7 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Operator two-factor recovery security history exists; AddOperatorTwoFactorRecoverySecurityEvents requires a forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/Persistence/Migrations/20261117161010_AddOperatorTwoFactorRecoverySecurityEvents.cs"] =
                    new("f455411dd08aaefaedbd6e40fbcf9eb1d40a3cedbbbc631f85a1d17ded93af3b", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Document number or operational policy history exists; AddDocumentNumberSeriesAndOperationalPolicies is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116220424_AddDocumentNumberSeriesAndOperationalPolicies.cs"] =
                    new("f63ed6f51a44421deb2e83992dfcae3014ead46fd857c1de9bf0156e5dbd3bfa", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Manufacturing baseline source history exists; AddManufacturingProfileBaselineTransfers is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117001828_AddManufacturingProfileBaselineTransfers.cs"] =
                    new("71ad691efe3a071a0564c224b0bc290ffb5200634d00676bc68880c3f189d7eb", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Published purchase invoice variance history exists; AddPurchaseInvoiceVariancePolicies is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117085524_AddPurchaseInvoiceVariancePolicies.cs"] =
                    new("2fd6131e7fe6b757a14966b3d6258fb0ccd70b1307adfe0395627cbc5b72aaed", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Warehouse transfer scale history exists; AddWarehouseTransferScalePolicies is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117111242_AddWarehouseTransferScalePolicies.cs"] =
                    new("80359213e4c141de9d79182ac5697b0493345f3f8a8b50d8e5cdda65867a618d", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Finance receipt or voucher number history exists; AddFinanceDocumentNumberSeries is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117200733_AddFinanceDocumentNumberSeries.cs"] =
                    new("be5146a204c1eec60dbdea2e3dd1506b9a738ec67101e7f6ecf6aed3a478f463", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Financial policy or bank source history exists; AddFinancePolicyFamilies is not rolled back (forward fix). 1 lexical Sql call(s).
                ["src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117220044_AddFinancePolicyFamilies.cs"] =
                    new("3d7264a234b9c41fa1738da1f9870b7a2327a55e8ccbf0a79d1e3dbc439f8381", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
                // Tren-1 W8; K3 review pending. Deployment-time Down refuses recorded owner history loss; Notification delivery, deduplication or passivation history exists; use a forward fix. 1 lexical Sql call(s).
                ["src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/Migrations/20261116234150_AddPersonalNotifications.cs"] =
                    new("410215fe07c1fc94023109418d24e027019103b05932f6839482f0eccac27682", new Dictionary<string, int>(StringComparer.Ordinal) { ["Sql"] = 1 }),
            };

    private static readonly HashSet<string> ReviewedProjectSdks =
        new(StringComparer.Ordinal)
        {
            "Microsoft.NET.Sdk",
            "Microsoft.NET.Sdk.Web"
        };

    private static readonly IReadOnlyDictionary<string, string> ReviewedPackageReferences =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Microsoft.AspNetCore.Authentication.JwtBearer"] = "10.0.8",
            ["Microsoft.AspNetCore.OpenApi"] = "10.0.8",
            ["Microsoft.EntityFrameworkCore.Design"] = "10.0.8",
            ["Microsoft.EntityFrameworkCore.SqlServer"] = "10.0.8",
            ["Microsoft.Extensions.ApiDescription.Server"] = "10.0.11",
            ["Microsoft.Extensions.Hosting.Abstractions"] = "10.0.8",
            ["Microsoft.Extensions.Options.ConfigurationExtensions"] = "10.0.8",
            ["Microsoft.OpenApi"] = "[2.7.5]",
            ["OpenTelemetry.Exporter.OpenTelemetryProtocol"] = "1.20.1",
            ["OpenTelemetry.Extensions.Hosting"] = "1.20.1",
            ["PDFsharp"] = "6.2.4"
        };

    private static readonly HashSet<string> ForbiddenGeneratedSourceItems =
        new(StringComparer.Ordinal)
        {
            "AdditionalFiles",
            "Analyzer",
            "CompilerVisibleItemMetadata",
            "CompilerVisibleProperty",
            "RazorComponent",
            "RazorGenerate",
            "SourceRoot"
        };

    private static readonly HashSet<string> UnsupportedManagedSourceExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".csx",
            ".fs",
            ".fsi",
            ".fsx",
            ".il",
            ".vb",
            ".vbx"
        };

    private static readonly HashSet<string> ReviewedProjectProperties =
        new(StringComparer.Ordinal)
        {
            "ImplicitUsings",
            "Nullable",
            "TargetFramework"
        };

    private static readonly IReadOnlyDictionary<string, string> ReviewedApiOpenApiProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OpenApiGenerateDocuments"] = "true",
            ["OpenApiGenerateDocumentsOnBuild"] = "true",
            ["OpenApiDocumentsDirectory"] = "../../../artifacts",
            ["OpenApiGenerateDocumentsOptions"] =
                "--document-name v1 --file-name openapi"
        };

    private static readonly HashSet<string> RequiredApiOpenApiPackages =
        new(StringComparer.Ordinal)
        {
            "Microsoft.AspNetCore.OpenApi",
            "Microsoft.Extensions.ApiDescription.Server",
            "Microsoft.OpenApi"
        };

    private static readonly HashSet<string> ReviewedProjectItems =
        new(StringComparer.Ordinal)
        {
            "Compile",
            "PackageReference",
            "ProjectReference"
        };

    private static readonly HashSet<string> ReviewedRepositoryBuildProperties =
        new(StringComparer.Ordinal)
        {
            "Deterministic",
            "EnforceCodeStyleInBuild",
            "TreatWarningsAsErrors"
        };

    private const string ReviewedRepositorySecurityPackage =
        "System.Security.Cryptography.Pkcs";
    private const string ReviewedRepositorySecurityPackageVersion = "10.0.13";

    [Fact]
    public void Platform_non_counterparty_bypass_surfaces_match_the_reviewed_inventory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var inventory = ReadBackendSourceInventory(
            repositoryRoot,
            requireCounterpartyContract: true);
        var errors = inventory.DefinitionErrors.ToList();
        var directDatabaseSources = inventory.Sources.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
        var toolInventory = ReadSqlServerRcsiToolSourceInventory(repositoryRoot);
        var toolSources = toolInventory.Sources;
        errors.AddRange(toolInventory.DefinitionErrors);
        foreach (var (path, source) in toolSources)
        {
            directDatabaseSources[path] = source;
        }

        errors.AddRange(ValidateNonVacuousScope(inventory));
        errors.AddRange(ValidateExactAllowlist(
            FindOccurrences(inventory.Sources),
            inventory.Sources));
        errors.AddRange(ValidateNoForbiddenBypassCapabilities(inventory.Sources));
        errors.AddRange(ValidateNoDirectDatabaseBypassCapabilities(
            directDatabaseSources,
            requireReviewedSources: true));
        errors.AddRange(ValidateNoForbiddenBypassCapabilities(toolSources));
        errors.AddRange(ValidateSqlServerRcsiToolMutationSurface(toolSources));
        errors.AddRange(ValidateReviewedSqlServerRcsiToolProject(
            File.ReadAllText(Path.Combine(
                repositoryRoot,
                ReviewedSqlServerRcsiToolProjectPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)))));
        errors.AddRange(ValidateReviewedSqlServerRcsiWrapper(
            File.ReadAllText(Path.Combine(
                repositoryRoot,
                ReviewedSqlServerRcsiWrapperPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)))));
        // Counterparty keeps its separate runtime guard. Include only this newly reviewed deployment migration
        // in the mutation inventory; this does not admit any Counterparty runtime source or bypass.
        var migrationSources = inventory.Sources.ToDictionary(
            entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        migrationSources.Add(
            ReviewedCounterpartyIdentityRevisionRepairMigrationPath,
            File.ReadAllText(Path.Combine(repositoryRoot,
                ReviewedCounterpartyIdentityRevisionRepairMigrationPath.Replace('/', Path.DirectorySeparatorChar))));
        errors.AddRange(ValidateReviewedMigrationMutationSurface(migrationSources));
        errors.AddRange(ValidateReviewedPlatformDbContextInventory(inventory.Sources));
        errors.AddRange(ValidateReviewedPlatformContextConstructions(inventory.Sources));

        Assert.True(errors.Count == 0, string.Join("\n\n", errors));
    }

    [Fact]
    public void Pdfsharp_dependency_is_limited_to_the_isolated_pdf_component()
    {
        var errors = new List<string>();
        ValidatePackageReference(
            XElement.Parse("<PackageReference Include=\"PDFsharp\" Version=\"6.2.4\" />"),
            "src/backend/BuildingBlocks/Unexpected.csproj",
            errors);

        Assert.Contains(
            "PDFsharp is allowed only in the isolated PDF component project.",
            errors);
    }

    [Fact]
    public void Pdf_component_content_inventory_cannot_drop_the_font_license()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            PdfProjectPath.Replace('/', Path.DirectorySeparatorChar));
        var project = XDocument.Load(projectPath);
        project.Descendants()
            .Single(element =>
                element.Name.LocalName == "Content"
                && string.Equals(
                    element.Attribute("Include")?.Value,
                    "Assets\\Fonts\\OFL-1.1.txt",
                    StringComparison.Ordinal))
            .Remove();

        var analysis = AnalyzeProjectDefinition(project, PdfProjectPath);

        Assert.Contains(
            "The PDF component content inventory must match the reviewed font and license assets.",
            analysis.Errors);
    }

    [Fact]
    public void Pdf_component_content_inventory_cannot_drop_the_font_provenance()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            PdfProjectPath.Replace('/', Path.DirectorySeparatorChar));
        var project = XDocument.Load(projectPath);
        project.Descendants()
            .Single(element =>
                element.Name.LocalName == "Content"
                && string.Equals(
                    element.Attribute("Include")?.Value,
                    "Assets\\Fonts\\PROVENANCE.md",
                    StringComparison.Ordinal))
            .Remove();

        var analysis = AnalyzeProjectDefinition(project, PdfProjectPath);

        Assert.Contains(
            "The PDF component content inventory must match the reviewed font and license assets.",
            analysis.Errors);
    }

    [Fact]
    public void Pdf_component_content_inventory_rejects_duplicate_asset_routes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            PdfProjectPath.Replace('/', Path.DirectorySeparatorChar));
        var project = XDocument.Load(projectPath);
        var itemGroup = project.Root!.Elements()
            .First(element => element.Name.LocalName == "ItemGroup"
                              && element.Elements().Any(item => item.Name.LocalName == "Content"));
        itemGroup.Add(new XElement(
            "Content",
            new XAttribute("Include", "Assets\\Fonts\\OFL-1.1.txt"),
            new XAttribute("CopyToOutputDirectory", "PreserveNewest"),
            new XAttribute("CopyToPublishDirectory", "PreserveNewest")));

        var analysis = AnalyzeProjectDefinition(project, PdfProjectPath);

        Assert.Contains(
            "The PDF component content inventory must match the reviewed font and license assets.",
            analysis.Errors);
    }

    [Fact]
    public void OpenApi_build_generation_startup_matches_the_exact_network_isolation_surface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sources = Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, BackendRootPath),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = NormalizeRepositoryPath(Path.GetRelativePath(repositoryRoot, path)),
                Source = File.ReadAllText(path),
            })
            .Where(file => !IsBuildOutput(file.Path))
            .ToDictionary(file => file.Path, file => file.Source, StringComparer.Ordinal);

        var errors = ValidateOpenApiBuildTimeNetworkIsolation(sources);

        Assert.True(errors.Count == 0, string.Join("\n\n", errors));
    }

    [Fact]
    public void Removing_an_OpenApi_build_generation_gate_is_rejected()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            OpenApiStartupPath.Replace('/', Path.DirectorySeparatorChar)));
        var identityPath = ReviewedRuntimeDatabaseBypassSources.Keys.Single(path =>
            path.EndsWith("IdentityModuleExtensions.cs", StringComparison.Ordinal));
        var identitySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            identityPath.Replace('/', Path.DirectorySeparatorChar)));
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OpenApiStartupPath] = programSource.Replace(
                "if (!isOpenApiDocumentGeneration",
                "if (isOpenApiDocumentGeneration",
                StringComparison.Ordinal),
            [identityPath] = identitySource.Replace(
                "if (!OpenApiDocumentGenerationMode.IsActive)",
                "if (OpenApiDocumentGenerationMode.IsActive)",
                StringComparison.Ordinal),
        };

        var errors = ValidateOpenApiBuildTimeNetworkIsolation(sources);

        Assert.Contains(errors, error => error.Contains(
            "startup fingerprint",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "hosted-service gate",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Disabled_unterminated_regular_string_cannot_mask_following_bypass()
    {
        const string source = "#if NEVER\n\"\n#endif\nquery.IgnoreQueryFilters();";

        var masked = MaskCommentsAndNonInterpolatedLiterals(source);
        var occurrences = FindOccurrences(new Dictionary<string, string>
        {
            ["src/backend/GoymenERP.Api/ConditionalProbe.cs"] = source
        });

        Assert.Contains("IgnoreQueryFilters", masked, StringComparison.Ordinal);
        Assert.Single(occurrences);
        Assert.Equal(4, occurrences[0].LineNumber);
    }

    [Theory]
    [InlineData("#if NEVER\n\"\n#endif\nquery.IgnoreQueryFilters();")]
    [InlineData("#if NEVER\n@\"\n#endif\nquery.IgnoreQueryFilters();")]
    [InlineData("#if NEVER\n\"\"\"\n#endif\nquery.IgnoreQueryFilters();")]
    [InlineData("#if NEVER\n/*\n#endif\nquery.IgnoreQueryFilters();")]
    public void Conditional_compilation_is_rejected_before_disabled_text_can_poison_scanning(
        string source)
    {
        var errors = ValidateNoForbiddenBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/ConditionalProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Conditional compilation is forbidden",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Synthetic_unauthorized_bypass_is_rejected_without_comment_noninterpolated_string_or_path_noise()
    {
        var allowedSource = string.Join(
            '\n',
            "companies.IgnoreQueryFilters();",
            "sites.IgnoreQueryFilters();",
            "locations.IgnoreQueryFilters();",
            "workCenters.IgnoreQueryFilters();",
            "// comments.IgnoreQueryFilters();",
            "/* blockComments.IgnoreQueryFilters(); */",
            "const string regularText = \"text.IgnoreQueryFilters();\";",
            "const string verbatimText = @\"text.IgnoreQueryFilters();\";");

        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AllowedSeederPath] = allowedSource,
            ["src/backend/GoymenERP.Api/UnauthorizedProbe.cs"] =
                "query.IgnoreQueryFilters();"
        };

        var occurrences = FindOccurrences(sources);
        Assert.Equal(ExpectedSeederBypassCount + 1, occurrences.Count);

        var errors = ValidateExactAllowlist(occurrences, sources);

        Assert.Contains(errors, error => error.Contains(
            "Unauthorized IgnoreQueryFilters calls",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "UnauthorizedProbe.cs:1",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "reviewed fingerprint",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Seeder_allowlist_requires_the_canonical_path_and_reviewed_fingerprint()
    {
        const string arbitraryFourCalls = """
            a.IgnoreQueryFilters();
            b.IgnoreQueryFilters();
            c.IgnoreQueryFilters();
            d.IgnoreQueryFilters();
            """;
        var wrongCasePath = AllowedSeederPath.Replace(
            "OrganizationDataSeeder.cs",
            "organizationdataseeder.cs",
            StringComparison.Ordinal);
        var wrongCaseSources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [wrongCasePath] = arbitraryFourCalls
        };

        var pathErrors = ValidateExactAllowlist(
            FindOccurrences(wrongCaseSources),
            wrongCaseSources);
        Assert.Contains(pathErrors, error => error.Contains(
            "Unauthorized IgnoreQueryFilters calls",
            StringComparison.Ordinal));

        var arbitrarySources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AllowedSeederPath] = arbitraryFourCalls
        };
        var fingerprintErrors = ValidateExactAllowlist(
            FindOccurrences(arbitrarySources),
            arbitrarySources);
        Assert.Contains(fingerprintErrors, error => error.Contains(
            "reviewed fingerprint",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Escaped_identifier_cannot_hide_an_unauthorized_bypass()
    {
        var occurrences = FindOccurrences(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Audit/EscapedProbe.cs"] =
                    @"query.IgnoreQuery\u0046ilters();"
            });

        var occurrence = Assert.Single(occurrences);
        Assert.Equal("src/backend/Modules/Audit/EscapedProbe.cs", occurrence.RepositoryPath);
        Assert.Equal(1, occurrence.LineNumber);
    }

    [Theory]
    [InlineData("query.Ignore‌QueryFilters();")] // Literal U+200C ZERO WIDTH NON-JOINER.
    [InlineData("query.Ignore‍QueryFilters();")] // Literal U+200D ZERO WIDTH JOINER.
    [InlineData(@"query.Ignore\u200CQueryFilters();")]
    [InlineData(@"query.Ignore\u200DQueryFilters();")]
    public void Format_characters_cannot_split_the_guarded_identifier(string source)
    {
        var occurrences = FindOccurrences(
            new Dictionary<string, string>
            {
                ["src/backend/BuildingBlocks/FormatProbe.cs"] = source
            });

        Assert.Single(occurrences);
    }

    [Fact]
    public void Interpolated_expression_cannot_hide_a_bypass_after_a_nested_literal()
    {
        var occurrences = FindOccurrences(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Identity/InterpolatedProbe.cs"] =
                    "var text = $\"{(name == \"x\" ? first : second).IgnoreQueryFilters()}\";"
            });

        var occurrence = Assert.Single(occurrences);
        Assert.Equal("src/backend/Modules/Identity/InterpolatedProbe.cs", occurrence.RepositoryPath);
    }

    [Fact]
    public void Method_group_cannot_hide_an_unauthorized_bypass_reference()
    {
        var occurrences = FindOccurrences(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Idempotency/MethodGroupProbe.cs"] =
                    "var bypass = EntityFrameworkQueryableExtensions.IgnoreQueryFilters;"
            });

        Assert.Single(occurrences);
    }

    [Fact]
    public void Reflection_name_concatenation_is_rejected_but_normal_delegate_invoke_is_not()
    {
        const string reflectionProbe = """
            var name = "Ignore" + "QueryFilters";
            var method = typeof(EntityFrameworkQueryableExtensions).GetMethod(name)!;
            method.Invoke(null, new object[] { query });
            """;
        var reflectionSources = new Dictionary<string, string>
        {
            ["src/backend/GoymenERP.Api/ReflectionProbe.cs"] = reflectionProbe
        };

        var reflectionErrors = ValidateNoForbiddenBypassCapabilities(reflectionSources);
        Assert.Contains(reflectionErrors, error => error.Contains(
            "GetMethod",
            StringComparison.Ordinal));

        var delegateSources = new Dictionary<string, string>
        {
            ["src/backend/Modules/Organization/DelegateProbe.cs"] =
                "configureDbContext?.Invoke(serviceProvider, options);"
        };
        Assert.Empty(ValidateNoForbiddenBypassCapabilities(delegateSources));
    }

    [Theory]
    [InlineData("using System.Reflection;")]
    [InlineData("MethodInfo method = value; method.Invoke(null, null);")]
    [InlineData("MethodBase method = value; method.Invoke(null, null);")]
    [InlineData("typeof(Target).GetMethod(\"Run\");")]
    [InlineData("typeof(Target).GetMethods();")]
    [InlineData("typeof(Target).GetMember(\"Run\");")]
    [InlineData("typeof(Target).GetMembers();")]
    [InlineData("Expression.Call(method);")]
    [InlineData("dynamic target = value;")]
    [InlineData("handler.DynamicInvoke();")]
    [InlineData("var type = Type.GetType(name);")]
    [InlineData("var value = Activator.CreateInstance(type);")]
    [InlineData("var value = ActivatorUtilities.CreateInstance<AuditDbContext>(services);")]
    [InlineData("var assembly = Assembly.Load(name);")]
    [InlineData("AssemblyLoadContext.Default.LoadFromAssemblyPath(path);")]
    [InlineData("typeof(AuditDbContext).GetConstructor(types);")]
    [InlineData("typeof(AuditDbContext).GetConstructors();")]
    [InlineData("PropertyInfo property = type.GetProperty(name);")]
    [InlineData("FieldInfo field = type.GetField(name);")]
    [InlineData("EventInfo eventInfo = type.GetEvent(name);")]
    [InlineData("var flags = BindingFlags.Instance | BindingFlags.NonPublic;")]
    [InlineData(
        "typeof(CompanyContext).GetField(\"_companyId\", flags)!"
        + ".SetValue(context, companyB);")]
    [InlineData("var value = property.GetValue(instance);")]
    [InlineData("using ReflectionAlias = @System.@Reflection;")]
    public void Reviewed_reflection_and_dynamic_capability_routes_have_a_zero_allowlist(
        string source)
    {
        var errors = ValidateNoForbiddenBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/CapabilityProbe.cs"] = source
            });

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Work_original_values_getvalue_is_exact_source_and_receiver_bound()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ReviewedWorkDbContextPath.Replace('/', Path.DirectorySeparatorChar)));
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ReviewedWorkDbContextPath] = source
        };

        Assert.Empty(ValidateNoForbiddenBypassCapabilities(sources));

        sources[ReviewedWorkDbContextPath] = source + "\nproperty.GetValue(instance);";
        Assert.Contains(ValidateNoForbiddenBypassCapabilities(sources),
            error => error.Contains("GetValue", StringComparison.Ordinal));

        sources[ReviewedWorkDbContextPath] = source + "\noriginal.GetValue<Guid>(nameof(snapshot.CompanyId));";
        Assert.Contains(ValidateNoForbiddenBypassCapabilities(sources),
            error => error.Contains("GetValue", StringComparison.Ordinal));

        sources.Clear();
        sources["src/backend/GoymenERP.Api/ForeignWorkDbContext.cs"] = source;
        Assert.Contains(ValidateNoForbiddenBypassCapabilities(sources),
            error => error.Contains("GetValue", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""
        using E = System.Linq.Expressions.Expression;
        var methodName = "Ignore" + "QueryFilters";
        var call = E.Call(instance: null, method: Resolve(methodName));
        """)]
    [InlineData("""
        using static System.Linq.Expressions.Expression;
        var methodName = "Ignore" + "QueryFilters";
        var call = Call(instance: null, method: Resolve(methodName));
        """)]
    public void Linq_expression_capability_is_rejected_through_alias_or_using_static(
        string source)
    {
        var errors = ValidateNoForbiddenBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/ExpressionProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "System.Linq.Expressions",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Linq_expression_namespace_cannot_be_hidden_behind_a_parent_namespace_alias()
    {
        const string source = """
            using Linq = System.Linq;
            var methodName = "Ignore" + "QueryFilters";
            var call = Linq.Expressions.Expression.Call(
                instance: null,
                method: Resolve(methodName));
            """;

        var errors = ValidateNoForbiddenBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/ExpressionPrefixAliasProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Expressions",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("database.ExecuteSqlRaw(\"DELETE FROM T\");")]
    [InlineData("database.ExecuteSqlRawAsync(\"DELETE FROM T\");")]
    [InlineData("database.ExecuteSqlInterpolated($\"DELETE FROM T\");")]
    [InlineData("database.ExecuteSqlInterpolatedAsync($\"DELETE FROM T\");")]
    [InlineData("database.EnsureCreated();")]
    [InlineData("await database.EnsureCreatedAsync();")]
    [InlineData("database.EnsureDeleted();")]
    [InlineData("await database.EnsureDeletedAsync();")]
    [InlineData("database.Migrate();")]
    [InlineData("await database.MigrateAsync();")]
    [InlineData("IRelationalDatabaseCreator creator = value; await creator.DeleteAsync();")]
    [InlineData("RelationalDatabaseCreator creator = value;")]
    [InlineData("IDatabaseCreator creator = value;")]
    [InlineData("IMigrator migrator = value;")]
    [InlineData("database.SqlQueryRaw<int>(\"SELECT 1\");")]
    [InlineData("database.SqlQuery<int>($\"SELECT 1\");")]
    [InlineData("entities.FromSqlRaw(\"SELECT * FROM T\");")]
    [InlineData("entities.FromSqlInterpolated($\"SELECT * FROM T\");")]
    [InlineData("entities.FromSql($\"SELECT * FROM T\");")]
    [InlineData("context.Database.GetDbConnection();")]
    [InlineData("await entities.FindAsync(id);")]
    [InlineData("entities.Find(id);")]
    [InlineData("await entry.GetDatabaseValuesAsync();")]
    [InlineData("entry.GetDatabaseValues();")]
    [InlineData("await entry.ReloadAsync();")]
    [InlineData("entry.Reload();")]
    [InlineData("var find = entities.Find;")]
    [InlineData("Func<Task> reload = entry.ReloadAsync;")]
    [InlineData("var databaseValues = entry.GetDatabaseValues;")]
    [InlineData("var tracked = entities.Local;")]
    [InlineData("var context = new DbContext(options);")]
    [InlineData("var context = new Microsoft.EntityFrameworkCore.DbContext(options);")]
    [InlineData("DbContext context = new(options);")]
    [InlineData("DbConnection connection = provider.Open();")]
    [InlineData("IDbConnection connection = provider.Open();")]
    [InlineData("DbCommand command = connection.CreateCommand();")]
    [InlineData("DbDataAdapter adapter = provider.CreateDataAdapter();")]
    [InlineData("IDbDataAdapter adapter = provider.CreateDataAdapter();")]
    [InlineData("DbDataSource dataSource = provider.CreateDataSource();")]
    [InlineData("DbBatch batch = connection.CreateBatch();")]
    [InlineData("DbBatchCommand command = batch.CreateBatchCommand();")]
    [InlineData("DbProviderFactories.GetFactory(providerName);")]
    [InlineData("SqlConnection connection = new(value);")]
    [InlineData("SqlCommand command = new(value);")]
    [InlineData("SqlDataAdapter adapter = new(command);")]
    [InlineData("SqlDataSource dataSource = SqlDataSource.Create(value);")]
    [InlineData("SqlBatch batch = connection.CreateBatch();")]
    [InlineData("SqlBatchCommand command = batch.CreateBatchCommand();")]
    [InlineData("SqlBulkCopy bulkCopy = new(connection);")]
    [InlineData("using Microsoft.Data.SqlClient;")]
    [InlineData("using @Microsoft.@Data.@SqlClient;")]
    [InlineData("using System.Data.Common;")]
    [InlineData("using System.Data.SqlClient;")]
    [InlineData("IRelationalConnection connection = value;")]
    [InlineData("IDbContextFactory<AuditDbContext> factory = value;")]
    [InlineData("services.AddDbContextFactory<AuditDbContext>();")]
    [InlineData("services.AddDbContextPool<AuditDbContext>();")]
    [InlineData("services.AddPooledDbContextFactory<AuditDbContext>();")]
    [InlineData("PooledDbContextFactory<AuditDbContext> factory = value;")]
    [InlineData("options.UseModel(model);")]
    [InlineData("options.ReplaceService<IModelSource, CustomModelSource>();")]
    [InlineData("options.UseInternalServiceProvider(provider);")]
    [InlineData("IModelCustomizer customizer = value;")]
    [InlineData("IModelCacheKeyFactory cacheKeys = value;")]
    [InlineData("IQueryExpressionInterceptor interceptor = value;")]
    [InlineData("DbCommandInterceptor interceptor = value;")]
    public void Direct_database_and_raw_sql_capability_routes_have_a_zero_allowlist(
        string source)
    {
        var errors = ValidateNoDirectDatabaseBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/DirectDatabaseProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Forbidden direct database/query-filter bypass capability",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "runtime application",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"database.Execute\u0053qlRaw(""SELECT 1"");")]
    [InlineData("database.Execute‌SqlRaw(\"SELECT 1\");")] // Literal U+200C.
    [InlineData("database.GetDb‍Connection();")] // Literal U+200D.
    public void Unicode_identifier_obfuscation_cannot_hide_a_direct_database_route(
        string source)
    {
        Assert.NotEmpty(ValidateNoDirectDatabaseBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/UnicodeDatabaseProbe.cs"] = source
            }));
    }

    [Fact]
    public void Direct_database_route_noise_in_comments_and_noninterpolated_literals_is_ignored()
    {
        const string source = """
            // database.ExecuteSqlRaw("DELETE FROM T");
            /* DbConnection connection = value; */
            const string regular = "SqlCommand CreateCommand GetDbConnection";
            const string verbatim = @"FromSqlRaw ExecuteSqlInterpolated System.Data.Common";
            """;

        Assert.Empty(ValidateNoDirectDatabaseBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/DirectDatabaseNoise.cs"] = source
            }));
    }

    [Fact]
    public void Reviewed_runtime_database_bypass_requires_exact_source_and_api_count()
    {
        var repositoryRoot = FindRepositoryRoot();
        var exactSources = ReviewedRuntimeDatabaseBypassSources.Keys.ToDictionary(
            path => path,
            path => File.ReadAllText(Path.Combine(
                repositoryRoot,
                path.Replace('/', Path.DirectorySeparatorChar))),
            StringComparer.Ordinal);
        var reviewedSource = exactSources[ReviewedIdentityReconcilerPath];
        var reviewedProbeSource = exactSources[ReviewedSqlServerRcsiReadinessProbePath];
        var reviewedToolSource = exactSources[ReviewedSqlServerRcsiToolProgramPath];

        Assert.Empty(ValidateNoDirectDatabaseBypassCapabilities(
            exactSources,
            requireReviewedSources: true));

        exactSources[ReviewedIdentityReconcilerPath] = reviewedSource + "\n// fingerprint drift";
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true),
            error => error.Contains("does not match the exact fingerprint", StringComparison.Ordinal));

        exactSources[ReviewedIdentityReconcilerPath] = reviewedSource.Replace(
            ".FromSqlInterpolated(",
            ".AsQueryable(",
            StringComparison.Ordinal);
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true),
            error => error.Contains(
                "Expected exactly 1 reviewed runtime 'FromSqlInterpolated' occurrence(s), but found 0",
                StringComparison.Ordinal));

        exactSources[ReviewedIdentityReconcilerPath] = reviewedSource;
        exactSources[ReviewedSqlServerRcsiReadinessProbePath] =
            reviewedProbeSource + "\n// fingerprint drift";
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true),
            error => error.Contains(
                "does not match the exact fingerprint",
                StringComparison.Ordinal)
                && error.Contains(
                    ReviewedSqlServerRcsiReadinessProbePath,
                    StringComparison.Ordinal));

        exactSources[ReviewedSqlServerRcsiReadinessProbePath] =
            reviewedProbeSource.Replace(
                ".CreateCommand()",
                ".CreateReadOnlyCommand()",
                StringComparison.Ordinal);
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true),
            error => error.Contains(
                "Expected exactly 1 reviewed runtime 'CreateCommand' occurrence(s), but found 0",
                StringComparison.Ordinal));

        exactSources[ReviewedSqlServerRcsiReadinessProbePath] = reviewedProbeSource;
        exactSources[ReviewedSqlServerRcsiToolProgramPath] =
            reviewedToolSource + "\nconnection.CreateCommand();";
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true),
            error => error.Contains(
                "Forbidden direct database/query-filter bypass capability 'CreateCommand'",
                StringComparison.Ordinal)
                && error.Contains(
                    ReviewedSqlServerRcsiToolProgramPath,
                    StringComparison.Ordinal));

        exactSources[ReviewedSqlServerRcsiToolProgramPath] =
            reviewedToolSource + "\ncommand.ExecuteNonQuery();";
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true),
            error => error.Contains(
                "Forbidden direct database/query-filter bypass capability 'ExecuteNonQuery'",
                StringComparison.Ordinal)
                && error.Contains(
                    ReviewedSqlServerRcsiToolProgramPath,
                    StringComparison.Ordinal));
    }

    [Fact]
    public void SqlServer_RCSI_tool_project_and_mutation_surface_are_exactly_guarded()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            ReviewedSqlServerRcsiToolProjectPath.Replace(
                '/',
                Path.DirectorySeparatorChar)));
        var toolInventory = ReadSqlServerRcsiToolSourceInventory(repositoryRoot);
        var toolSources = toolInventory.Sources;
        var wrapperSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            ReviewedSqlServerRcsiWrapperPath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Empty(toolInventory.DefinitionErrors);
        Assert.Empty(ValidateReviewedSqlServerRcsiToolProject(projectSource));
        Assert.Empty(ValidateSqlServerRcsiToolMutationSurface(toolSources));
        Assert.Empty(ValidateReviewedSqlServerRcsiWrapper(wrapperSource));

        var withoutPackage = projectSource.Replace(
            "Microsoft.Data.SqlClient",
            "Removed.SqlClient",
            StringComparison.Ordinal);
        Assert.Contains(
            ValidateReviewedSqlServerRcsiToolProject(withoutPackage),
            error => error.Contains(
                "exact Microsoft.Data.SqlClient package",
                StringComparison.Ordinal));

        var withoutSharedProject = projectSource.Replace(
            "GoymenERP.BuildingBlocks.SqlServer.csproj",
            "Removed.BuildingBlocks.SqlServer.csproj",
            StringComparison.Ordinal);
        Assert.Contains(
            ValidateReviewedSqlServerRcsiToolProject(withoutSharedProject),
            error => error.Contains(
                "exact shared SQL Server project reference",
                StringComparison.Ordinal));

        var dangerousSources = toolSources.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
        var dangerousPath = ReviewedSqlServerRcsiToolRootPath + "/Dangerous.cs";
        dangerousSources[dangerousPath] =
            "SqlConnection connection = value; connection.CreateCommand(); "
            + "command.ExecuteNonQuery(); const string sql = \"ALTER DATABASE [X] SET OFFLINE\";";

        var directDatabaseErrors = ValidateNoDirectDatabaseBypassCapabilities(
            dangerousSources);
        Assert.Contains(
            directDatabaseErrors,
            error => error.Contains("CreateCommand", StringComparison.Ordinal)
                     && error.Contains(dangerousPath, StringComparison.Ordinal));
        Assert.Contains(
            directDatabaseErrors,
            error => error.Contains("ExecuteNonQuery", StringComparison.Ordinal)
                     && error.Contains(dangerousPath, StringComparison.Ordinal));
        Assert.Contains(
            ValidateSqlServerRcsiToolMutationSurface(dangerousSources),
            error => error.Contains("ALTER DATABASE", StringComparison.Ordinal)
                     && error.Contains(dangerousPath, StringComparison.Ordinal));

        foreach (var dangerousWrapperFragment in new[]
                 {
                     "ALTER DATABASE [X] SET OFFLINE",
                     "Invoke-Expression $payload",
                     "[Diagnostics.ProcessStartInfo]::new('dotnet')",
                     "Password=PlaintextSecret",
                 })
        {
            Assert.Contains(
                ValidateReviewedSqlServerRcsiWrapper(
                    wrapperSource + Environment.NewLine + dangerousWrapperFragment),
                error => error.Contains("wrapper", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void SqlServer_RCSI_publish_and_test_discovery_contracts_are_fail_closed()
    {
        var repositoryRoot = FindRepositoryRoot();
        var solutionSource = File.ReadAllText(Path.Combine(repositoryRoot, SolutionPath));
        var integrationProjectSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            SqlServerRcsiIntegrationProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        var toolTestsProjectSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            SqlServerRcsiToolTestsProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        var relationalAcceptanceSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            SqlServerRcsiRelationalAcceptancePath.Replace('/', Path.DirectorySeparatorChar)));
        var workflowSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            ContinuousIntegrationWorkflowPath.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedWorkflowSource = workflowSource.Replace(
            "\r\n",
            "\n",
            StringComparison.Ordinal);
        Assert.Equal(
            ReviewedBackendTestGroupVerifierFingerprint,
            ComputeReviewedSourceFingerprint(File.ReadAllText(Path.Combine(
                repositoryRoot,
                BackendTestGroupVerifierPath.Replace('/', Path.DirectorySeparatorChar)))));
        Assert.Equal(
            ReviewedBackendTestGroupVerifierTestsFingerprint,
            ComputeReviewedSourceFingerprint(File.ReadAllText(Path.Combine(
                repositoryRoot,
                BackendTestGroupVerifierTestsPath.Replace('/', Path.DirectorySeparatorChar)))));
        var verifierSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            SqlServerRcsiVerifierPath.Replace('/', Path.DirectorySeparatorChar)));
        var verifierTestsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            SqlServerRcsiVerifierTestsPath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Equal(
            ReviewedSqlServerRcsiVerifierFingerprint,
            ComputeReviewedSourceFingerprint(verifierSource));
        Assert.Equal(
            ReviewedSqlServerRcsiVerifierTestsFingerprint,
            ComputeReviewedSourceFingerprint(verifierTestsSource));
        Assert.NotEqual(
            ReviewedSqlServerRcsiVerifierFingerprint,
            ComputeReviewedSourceFingerprint(verifierSource + "\n# discovery bypass"));
        var relationalInventoryEntries = Directory.GetFileSystemEntries(Path.Combine(
                repositoryRoot,
                RelationalInventoryDirectory.Replace('/', Path.DirectorySeparatorChar)))
            .Select(entry => RelationalInventoryDirectory + "/" + Path.GetFileName(entry))
            .Order(StringComparer.Ordinal);
        Assert.Equal(
            ReviewedRelationalInventoryFingerprints.Keys.Order(StringComparer.Ordinal),
            relationalInventoryEntries);
        foreach (var (inventoryPath, inventoryFingerprint) in ReviewedRelationalInventoryFingerprints)
        {
            Assert.Equal(
                (inventoryPath, inventoryFingerprint),
                (inventoryPath, ComputeReviewedSourceFingerprint(File.ReadAllText(Path.Combine(
                    repositoryRoot,
                    inventoryPath.Replace('/', Path.DirectorySeparatorChar))))));
        }
        Assert.Equal(
            ReviewedSqlServerRcsiToolTestsProjectFingerprint,
            ComputeReviewedSourceFingerprint(toolTestsProjectSource));
        var toolTestsProject = XDocument.Parse(toolTestsProjectSource);
        Assert.Single(toolTestsProject.Descendants("PackageReference"), element =>
            string.Equals(
                element.Attribute("Include")?.Value,
                "Microsoft.NET.Test.Sdk",
                StringComparison.Ordinal));
        Assert.DoesNotContain(toolTestsProject.Descendants("IsTestProject"), element =>
            string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(
            ReviewedSqlServerRcsiToolTestsProjectFingerprint,
            ComputeReviewedSourceFingerprint(toolTestsProjectSource.Replace(
                "<IsPackable>false</IsPackable>",
                "<IsPackable>false</IsPackable>\n    <IsTestProject>false</IsTestProject>",
                StringComparison.Ordinal)));

        Assert.Empty(ValidateSqlServerRcsiPublishAndDiscoveryContracts(
            solutionSource,
            integrationProjectSource,
            relationalAcceptanceSource,
            normalizedWorkflowSource));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                relationalAcceptanceSource,
                normalizedWorkflowSource + "\n# unreviewed workflow drift"),
            error => error.Contains("exact reviewed fingerprint", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource.Replace(
                    "tests/GoymenERP.SqlServerRcsiReadiness.Tests/"
                    + "GoymenERP.SqlServerRcsiReadiness.Tests.csproj",
                    "tests/Removed/Removed.csproj",
                    StringComparison.Ordinal),
                integrationProjectSource,
                relationalAcceptanceSource,
                normalizedWorkflowSource),
            error => error.Contains("solution membership", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource.Replace(
                    "$(MSBuildProjectDirectory)\\..\\..\\tools\\"
                    + "GoymenERP.SqlServerRcsiReadiness\\"
                    + "GoymenERP.SqlServerRcsiReadiness.csproj",
                    "$(MSBuildProjectDirectory)\\..\\Alternate\\Alternate.csproj",
                    StringComparison.Ordinal),
                relationalAcceptanceSource,
                normalizedWorkflowSource),
            error => error.Contains("tool project path", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource.Replace(
                    "SelfContained=true",
                    "SelfContained=false",
                    StringComparison.Ordinal),
                relationalAcceptanceSource,
                normalizedWorkflowSource),
            error => error.Contains("SelfContained=true", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource.Replace(
                    "<RemoveDir Directories=\"$(RcsiToolSelfContainedPublishDir)\" />",
                    "<Message Text=\"clean removed\" />",
                    StringComparison.Ordinal),
                relationalAcceptanceSource,
                normalizedWorkflowSource),
            error => error.Contains("cleaned before publication", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource.Replace(
                    "<RemoveDir Directories=\"$(RcsiToolSelfContainedPublishDir)\" />",
                    "<RemoveDir Directories=\"$(RcsiToolSelfContainedPublishDir)\" "
                    + "Condition=\"'false'\" />",
                    StringComparison.Ordinal),
                relationalAcceptanceSource,
                normalizedWorkflowSource),
            error => error.Contains("cleaned before publication", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                relationalAcceptanceSource.Replace(
                    "SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests",
                    "RenamedRcsiAcceptanceTests",
                    StringComparison.Ordinal),
                normalizedWorkflowSource),
            error => error.Contains("relational discovery", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                relationalAcceptanceSource.Replace(
                    "SqlServerRcsiReadinessProcessAcceptanceTests",
                    "RemovedRcsiProcessAcceptanceTests",
                    StringComparison.Ordinal),
                normalizedWorkflowSource),
            error => error.Contains("process acceptance discovery", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                "#if NEVER" + Environment.NewLine + relationalAcceptanceSource
                + Environment.NewLine + "#endif",
                normalizedWorkflowSource),
            error => error.Contains("relational discovery", StringComparison.Ordinal));

        foreach (var verifierBypass in new[] { " --list-tests", " || true", "; true" })
        {
            Assert.Contains(
                ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                    solutionSource,
                    integrationProjectSource,
                    relationalAcceptanceSource,
                    normalizedWorkflowSource.Replace(
                        "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py",
                        "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py"
                        + verifierBypass,
                        StringComparison.Ordinal)),
                error => error.Contains("relational CI verifier", StringComparison.Ordinal));
        }

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                relationalAcceptanceSource,
                normalizedWorkflowSource.Replace(
                    "GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: >-",
                    "REMOVED_SQLSERVER_CONNECTION: >-",
                    StringComparison.Ordinal)),
            error => error.Contains("SQL Server fixture binding", StringComparison.Ordinal));

        foreach (var softGate in new[]
                 {
                     "        if: ${{ false }}",
                     "        continue-on-error: true",
                     "        shell: echo {0}",
                     "        env:\n          GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: ''",
                 })
        {
            Assert.Contains(
                ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                    solutionSource,
                    integrationProjectSource,
                    relationalAcceptanceSource,
                    normalizedWorkflowSource.Replace(
                        "      - name: Run exact RCSI relational acceptance verifier",
                        "      - name: Run exact RCSI relational acceptance verifier\n"
                        + softGate,
                        StringComparison.Ordinal)),
                error => error.Contains("relational CI verifier", StringComparison.Ordinal));
        }

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                relationalAcceptanceSource,
                normalizedWorkflowSource.Replace(
                    "      - name: Run exact backend test group",
                    "      - name: Run exact backend test group\n        if: ${{ false }}",
                    StringComparison.Ordinal)),
            error => error.Contains("backend test group step", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                relationalAcceptanceSource,
                normalizedWorkflowSource.Replace(
                    "\n    env:\n      GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: >-\n        Server=",
                    "\n        env:\n          GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: >-\n            Server=",
                    StringComparison.Ordinal)),
            error => error.Contains("SQL Server fixture binding", StringComparison.Ordinal));

        foreach (var (original, replacement) in new[]
                 {
                     (
                         "      - name: Run exact SQL Server relational shard\n        run: |",
                         "      - name: Run exact SQL Server relational shard\n"
                         + "        shell: echo {0}\n        run: |"),
                     (
                         "          --rcsi \\",
                         "          --rcsi --list-tests \\"),
                     (
                         "          --rcsi-result \"${{ needs.relational-rcsi.result }}\"",
                         "          --rcsi-result \"${{ needs.relational-rcsi.result }}\" || true"),
                     (
                         "          --aggregate \\",
                         "          # --aggregate \\"),
                     (
                         "      - name: Upload exact SQL shard proof\n"
                         + "        uses: actions/upload-artifact@v7",
                         "      - name: Upload exact SQL shard proof\n"
                         + "        if: ${{ false }}\n"
                         + "        uses: actions/upload-artifact@v7"),
                     (
                         "      - name: Download exact SQL shard proofs\n"
                         + "        uses: actions/download-artifact@v8",
                         "      - name: Download exact SQL shard proofs\n"
                         + "        shell: echo {0}\n"
                         + "        uses: actions/download-artifact@v8"),
                 })
        {
            Assert.Contains(
                ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                    solutionSource,
                    integrationProjectSource,
                    relationalAcceptanceSource,
                    normalizedWorkflowSource.Replace(
                        original,
                        replacement,
                        StringComparison.Ordinal)),
                error => error.Contains("relational CI verifier", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void SqlServer_RCSI_tool_compile_inputs_and_ancestor_targets_are_scanned()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-rcsi-tool-guard-{Guid.NewGuid():N}");
        var repositoryRoot = Path.Combine(testRoot, "repository");
        var toolRoot = Path.Combine(
            repositoryRoot,
            ReviewedSqlServerRcsiToolRootPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        try
        {
            Directory.CreateDirectory(toolRoot);
            File.WriteAllText(Path.Combine(toolRoot, "Program.cs"), "internal class Program;");
            File.WriteAllText(
                Path.Combine(toolRoot, "AssemblyInfo.cs"),
                "internal class AssemblyInfo;");
            File.WriteAllText(
                Path.Combine(repositoryRoot, "tools", "Dangerous.cs"),
                "SqlConnection connection = value; connection.CreateCommand(); "
                + "command.ExecuteNonQuery(); const string sql = \"ALTER DATABASE [X]\";");
            File.WriteAllText(
                Path.Combine(
                    repositoryRoot,
                    ReviewedSqlServerRcsiToolProjectPath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="..\Dangerous.cs" Link="Dangerous.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(repositoryRoot, "tools", "Directory.Build.targets"),
                "<Project><Target Name=\"Inject\" /></Project>");
            File.WriteAllText(
                Path.Combine(testRoot, "Directory.Build.targets"),
                "<Project><Target Name=\"InjectOutsideRepository\" /></Project>");
            File.WriteAllText(
                Path.Combine(testRoot, "Directory.Packages.props"),
                "<Project><ItemGroup><PackageReference Include=\"Injected\" /></ItemGroup></Project>");
            File.WriteAllText(
                Path.Combine(
                    repositoryRoot,
                    ReviewedSqlServerRcsiToolProjectPath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)) + ".user",
                "<Project><Import Project=\"Injected.targets\" /></Project>");

            var inventory = ReadSqlServerRcsiToolSourceInventory(repositoryRoot);
            var dangerousPath = "tools/Dangerous.cs";
            Assert.Contains(
                inventory.DefinitionErrors,
                error => error.Contains("Directory.Build.targets", StringComparison.Ordinal));
            Assert.Contains(
                inventory.DefinitionErrors,
                error => error.Contains("outside the repository", StringComparison.Ordinal)
                         && error.Contains(
                             "Directory.Build.targets",
                             StringComparison.Ordinal));
            Assert.Contains(
                inventory.DefinitionErrors,
                error => error.Contains("outside the repository", StringComparison.Ordinal)
                         && error.Contains(
                             "Directory.Packages.props",
                             StringComparison.Ordinal));
            Assert.Contains(
                inventory.DefinitionErrors,
                error => error.Contains(".csproj.user", StringComparison.Ordinal));
            Assert.True(inventory.Sources.ContainsKey(dangerousPath));
            var directErrors = ValidateNoDirectDatabaseBypassCapabilities(inventory.Sources);
            Assert.Contains(
                directErrors,
                error => error.Contains("CreateCommand", StringComparison.Ordinal)
                         && error.Contains(dangerousPath, StringComparison.Ordinal));
            Assert.Contains(
                directErrors,
                error => error.Contains("ExecuteNonQuery", StringComparison.Ordinal)
                         && error.Contains(dangerousPath, StringComparison.Ordinal));
            Assert.Contains(
                ValidateSqlServerRcsiToolMutationSurface(inventory.Sources),
                error => error.Contains("ALTER DATABASE", StringComparison.Ordinal)
                         && error.Contains(dangerousPath, StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void SqlServer_RCSI_tool_compile_policy_accepts_literal_Link_and_rejects_hiding_directives()
    {
        var sourceRepositoryRoot = FindRepositoryRoot();
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-rcsi-tool-compile-policy-{Guid.NewGuid():N}");
        var repositoryRoot = Path.Combine(testRoot, "repository");
        var toolRoot = Path.Combine(
            repositoryRoot,
            ReviewedSqlServerRcsiToolRootPath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            Directory.CreateDirectory(toolRoot);
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "tools"));
            File.Copy(
                Path.Combine(sourceRepositoryRoot, "Directory.Build.props"),
                Path.Combine(repositoryRoot, "Directory.Build.props"));
            foreach (var sourceName in new[] { "Program.cs", "AssemblyInfo.cs" })
            {
                File.WriteAllText(Path.Combine(toolRoot, sourceName), $"internal class {sourceName[..^3]};");
            }

            File.WriteAllText(Path.Combine(repositoryRoot, "tools", "Safe.cs"), "internal class Safe;");
            Directory.CreateDirectory(Path.Combine(toolRoot, "obj"));
            Directory.CreateDirectory(Path.Combine(toolRoot, "bin"));
            File.WriteAllText(
                Path.Combine(toolRoot, "obj", "Dangerous.cs"),
                "connection.CreateCommand().ExecuteNonQuery();");
            File.WriteAllText(
                Path.Combine(toolRoot, "bin", "Dangerous.cs"),
                "connection.CreateCommand().ExecuteNonQuery();");

            var projectPath = Path.Combine(
                repositoryRoot,
                ReviewedSqlServerRcsiToolProjectPath.Replace('/', Path.DirectorySeparatorChar));
            var baseProject = File.ReadAllText(Path.Combine(
                sourceRepositoryRoot,
                ReviewedSqlServerRcsiToolProjectPath.Replace('/', Path.DirectorySeparatorChar)));
            var linkedProject = baseProject.Replace(
                "</Project>",
                """
                  <ItemGroup>
                    <Compile Include="..\Safe.cs">
                      <Link>Safe.cs</Link>
                    </Compile>
                  </ItemGroup>
                </Project>
                """,
                StringComparison.Ordinal);
            File.WriteAllText(projectPath, linkedProject);

            var inventory = ReadSqlServerRcsiToolSourceInventory(repositoryRoot);
            Assert.Empty(inventory.DefinitionErrors);
            Assert.Contains("tools/Safe.cs", inventory.Sources.Keys);
            Assert.DoesNotContain(
                inventory.Sources.Keys,
                path => path.Contains("/obj/", StringComparison.Ordinal)
                        || path.Contains("/bin/", StringComparison.Ordinal));

            foreach (var mutation in new[]
                     {
                         "<Compile Remove=\"Program.cs\" />",
                         "<Compile Update=\"Program.cs\" />",
                         "<Compile Include=\"..\\Safe.cs\" Exclude=\"Program.cs\" />",
                     })
            {
                var mutatedProject = baseProject.Replace(
                    "</Project>",
                    $"<ItemGroup>{mutation}</ItemGroup></Project>",
                    StringComparison.Ordinal);
                File.WriteAllText(projectPath, mutatedProject);
                Assert.Contains(
                    ReadSqlServerRcsiToolSourceInventory(repositoryRoot).DefinitionErrors,
                    error => error.Contains("RCSI tool Compile", StringComparison.Ordinal));
            }
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Migration_and_design_time_sources_are_classified_but_not_exempted()
    {
        var errors = ValidateNoDirectDatabaseBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Audit/Infrastructure/Persistence/Migrations/Probe.cs"] =
                    "database.ExecuteSqlRaw(\"DELETE FROM T\");",
                ["src/backend/Modules/Audit/Infrastructure/Persistence/"
                 + "ProbeDesignTimeDbContextFactory.cs"] =
                    "DbConnection connection = provider.Open();"
            });

        Assert.Contains(errors, error => error.Contains(
            "migration",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "design-time",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("migrationBuilder.Sql(\"DELETE FROM T\");", "Sql")]
    [InlineData("var sql = migrationBuilder.Sql;", "Sql")]
    [InlineData("Sql(\"DELETE FROM T\");", "Sql")]
    [InlineData("var sql = Sql;", "Sql")]
    [InlineData("migrationBuilder.@InsertData(table: \"T\");", "InsertData")]
    [InlineData("migrationBuilder.UpdateData(table: \"T\");", "UpdateData")]
    [InlineData("migrationBuilder.DeleteData(table: \"T\");", "DeleteData")]
    [InlineData("var operation = new SqlOperation();", "SqlOperation")]
    [InlineData("var operation = new InsertDataOperation();", "InsertDataOperation")]
    [InlineData("var operation = new UpdateDataOperation();", "UpdateDataOperation")]
    [InlineData("var operation = new DeleteDataOperation();", "DeleteDataOperation")]
    public void Unreviewed_migration_raw_or_data_mutation_surface_is_rejected(
        string source,
        string apiName)
    {
        var errors = ValidateReviewedMigrationMutationSurface(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Reporting/Infrastructure/Persistence/"
                 + "Migrations/Unreviewed.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            $"Unreviewed migration mutation API '{apiName}'",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Migration_mutation_api_is_forbidden_in_runtime_application_source()
    {
        var errors = ValidateReviewedMigrationMutationSurface(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/RuntimeMigrationProbe.cs"] =
                    "migrationBuilder.Sql(\"DELETE FROM T\");"
            });

        Assert.Contains(errors, error => error.Contains(
            "forbidden outside migration source",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Reviewed_migration_mutation_source_requires_exact_fingerprint()
    {
        var (path, reviewed) = ReviewedMigrationMutationSources.First();
        var errors = ValidateReviewedMigrationMutationSurface(
            new Dictionary<string, string>
            {
                [path] = "migrationBuilder.Sql(\"SELECT 1\");"
            });

        Assert.Contains(errors, error => error.Contains(
            "does not match the reviewed fingerprint",
            StringComparison.Ordinal));
        Assert.NotEmpty(reviewed.ExpectedApiOccurrences);
    }

    [Fact]
    public void Migration_mutation_noise_in_comments_and_literals_is_ignored()
    {
        const string source = """
            // migrationBuilder.Sql("DELETE FROM T");
            /* migrationBuilder.InsertData(table: "T"); */
            const string text = "SqlOperation UpdateData DeleteData";
            """;
        var errors = ValidateReviewedMigrationMutationSurface(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Reporting/Infrastructure/Persistence/"
                 + "Migrations/Noise.cs"] = source
            });

        Assert.DoesNotContain(errors, error => error.Contains(
            "Unreviewed migration mutation API",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Compiled_source_categories_keep_runtime_migration_design_time_and_build_distinct()
    {
        Assert.Equal(
            PlatformSourceCategory.RuntimeApplication,
            ClassifySource("src/backend/GoymenERP.Api/Program.cs"));
        Assert.Equal(
            PlatformSourceCategory.Migration,
            ClassifySource("src/backend/Modules/Audit/Persistence/Migrations/Probe.cs"));
        Assert.Equal(
            PlatformSourceCategory.DesignTime,
            ClassifySource(
                "src/backend/Modules/Audit/Persistence/AuditDesignTimeDbContextFactory.cs"));
        Assert.Equal(
            PlatformSourceCategory.BuildArtifact,
            ClassifySource("src/backend/Modules/Audit/obj/GeneratedProbe.cs"));
    }

    [Fact]
    public void New_filterless_platform_dbcontext_is_rejected()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            public sealed class ReportingDbContext(
                DbContextOptions<ReportingDbContext> options)
                : DbContext(options)
            {
            }
            """;
        var errors = ValidateReviewedPlatformDbContextInventory(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Reporting/Infrastructure/ReportingDbContext.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Unreviewed platform DbContext declaration",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "ReportingDbContext",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("using ContextBase = Microsoft.EntityFrameworkCore.DbContext;")]
    [InlineData("global using ContextBase = global::Microsoft.EntityFrameworkCore.DbContext;")]
    public void Dbcontext_base_type_alias_cannot_hide_a_new_filterless_context(string alias)
    {
        var errors = ValidateReviewedPlatformDbContextInventory(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Reporting/Infrastructure/ReportingDbContext.cs"] =
                    alias + "\npublic sealed class ReportingDbContext : ContextBase { }"
            });

        Assert.Contains(errors, error => error.Contains(
            "DbContext base-type alias",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Reviewed_dbcontext_path_and_fingerprint_are_both_fail_closed()
    {
        var reviewedPath = ReviewedPlatformDbContexts.Keys.First();
        var errors = ValidateReviewedPlatformDbContextInventory(
            new Dictionary<string, string>
            {
                [reviewedPath] = "public sealed class AuditDbContext : DbContext { }"
            });

        Assert.Contains(errors, error => error.Contains(
            "reviewed fingerprint",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        "new OrganizationDbContext(options, fakeCompanyContext, bootstrapScope);")]
    [InlineData(
        "new @OrganizationDbContext(options, fakeCompanyContext, bootstrapScope);")]
    [InlineData(
        "new GoymenERP.Modules.Organization.Infrastructure.Persistence."
        + "@OrganizationDbContext(options, fakeCompanyContext, bootstrapScope);")]
    [InlineData(
        "OrganizationDbContext context = new(options, fakeCompanyContext, bootstrapScope);")]
    [InlineData(
        "services.AddScoped<OrganizationDbContext>(_ => "
        + "new(options, fakeCompanyContext, bootstrapScope));")]
    [InlineData(
        "OrganizationDbContext Create() { "
        + "return new(options, fakeCompanyContext, bootstrapScope); }")]
    [InlineData(
        "OrganizationDbContext Context { get; } = "
        + "new(options, fakeCompanyContext, bootstrapScope);")]
    public void Runtime_construction_of_a_reviewed_context_is_rejected(string source)
    {
        var errors = ValidateReviewedPlatformContextConstructions(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/FabricatedCompanyContextProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Unauthorized reviewed platform DbContext construction",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "OrganizationDbContext",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Reviewed_context_alias_cannot_hide_an_unauthorized_construction()
    {
        const string source = """
            using Context = GoymenERP.Modules.Organization.Infrastructure.Persistence
                .OrganizationDbContext;
            var context = new Context(options, fakeCompanyContext, bootstrapScope);
            """;
        var errors = ValidateReviewedPlatformContextConstructions(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/AliasedContextConstructionProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Reviewed platform DbContext type alias",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Design_time_context_construction_requires_the_exact_factory_fingerprint()
    {
        var (path, factory) = ReviewedPlatformContextFactories.First();
        var errors = ValidateReviewedPlatformContextConstructions(
            new Dictionary<string, string>
            {
                [path] = $"return new {factory.ContextTypeName}(options);"
            });

        Assert.Contains(errors, error => error.Contains(
            "factory does not match the reviewed fingerprint",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Work_design_time_factory_requires_one_exact_reviewed_construction()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sources = ReviewedPlatformContextFactories.Keys.ToDictionary(
            candidate => candidate,
            candidate => File.ReadAllText(Path.Combine(
                repositoryRoot, candidate.Replace('/', Path.DirectorySeparatorChar))),
            StringComparer.Ordinal);
        var path = ReviewedPlatformContextFactories.Keys.Single(candidate =>
            candidate.EndsWith("/WorkDesignTimeDbContextFactory.cs", StringComparison.Ordinal));
        var source = sources[path];

        Assert.Empty(ValidateReviewedPlatformContextConstructions(sources));

        sources[path] = source.Replace("new WorkDbContext(", "new UnreviewedContext(", StringComparison.Ordinal);
        Assert.Contains(ValidateReviewedPlatformContextConstructions(sources),
            error => error.Contains("Expected exactly one 'WorkDbContext' construction", StringComparison.Ordinal));
        sources[path] = source + "\n// fingerprint drift";
        Assert.Contains(ValidateReviewedPlatformContextConstructions(sources),
            error => error.Contains("factory does not match the reviewed fingerprint", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        "new WorkDbContext(options, companyContext);",
        "Unauthorized reviewed platform DbContext construction 'WorkDbContext'")]
    [InlineData(
        "using WorkAlias = WorkDbContext;",
        "Reviewed platform DbContext type alias 'WorkAlias'")]
    [InlineData(
        "new WorkDesignTimeDbContextFactory().CreateDbContext([]);",
        "Unauthorized design-time DbContext factory reference 'WorkDesignTimeDbContextFactory'")]
    public void Unreviewed_work_context_construction_alias_or_factory_is_rejected(
        string source,
        string expectedFailure)
    {
        var repositoryRoot = FindRepositoryRoot();
        var sources = ReviewedPlatformContextFactories.Keys.ToDictionary(
            candidate => candidate,
            candidate => File.ReadAllText(Path.Combine(
                repositoryRoot, candidate.Replace('/', Path.DirectorySeparatorChar))),
            StringComparer.Ordinal);
        sources["src/backend/GoymenERP.Api/UnreviewedWorkContext.cs"] = source;
        var errors = ValidateReviewedPlatformContextConstructions(sources);

        Assert.Contains(errors, error => error.Contains(expectedFailure, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        "new OrganizationDesignTimeDbContextFactory().CreateDbContext([]);")]
    [InlineData(
        "IDesignTimeDbContextFactory<OrganizationDbContext> factory = value;")]
    public void Runtime_access_to_a_design_time_context_factory_is_rejected(string source)
    {
        var errors = ValidateReviewedPlatformContextConstructions(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/DesignTimeFactoryAccessProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Unauthorized design-time DbContext factory reference",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public sealed class Reporting : @DbContext { }")]
    [InlineData(
        "public sealed class Reporting : "
        + "Microsoft.EntityFrameworkCore.@DbContext { }")]
    public void Verbatim_identifier_cannot_hide_a_new_filterless_context(string source)
    {
        var errors = ValidateReviewedPlatformDbContextInventory(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Reporting/Infrastructure/ReportingDbContext.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Unreviewed platform DbContext declaration",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Verbatim_identifier_cannot_hide_a_dbcontext_base_type_alias()
    {
        const string source = """
            using Base = Microsoft.EntityFrameworkCore.@DbContext;
            public sealed class Reporting : Base { }
            """;
        var errors = ValidateReviewedPlatformDbContextInventory(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Reporting/Infrastructure/ReportingDbContext.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "DbContext base-type alias",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Project_definition_guard_tracks_literal_links_and_rejects_generation_indirection()
    {
        var safe = AnalyzeProjectDefinition(
            XDocument.Parse("""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="..\Shared\TenantGuard.cs">
                      <Link>Linked\TenantGuard.cs</Link>
                    </Compile>
                    <Compile Remove="Legacy.cs" />
                  </ItemGroup>
                </Project>
                """),
            "src/backend/Synthetic/Synthetic.csproj");

        Assert.Empty(safe.Errors);
        Assert.Contains(safe.CompileDirectives, directive =>
            directive.Kind == CompileDirectiveKind.Include
            && directive.Items.Single() == @"..\Shared\TenantGuard.cs"
            && directive.Link == @"Linked\TenantGuard.cs");
        Assert.Contains(safe.CompileDirectives, directive =>
            directive.Kind == CompileDirectiveKind.Remove);

        var unsafeDefinition = AnalyzeProjectDefinition(
            XDocument.Parse("""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="generated.targets" />
                  <ItemGroup>
                    <Analyzer Include="Generator.dll" />
                    <Compile Include="$(IntermediateOutputPath)Generated.cs" />
                    <PackageReference Include="Unreviewed.SourceGenerator" Version="1.0.0" />
                  </ItemGroup>
                  <Target Name="GenerateSource" />
                </Project>
                """),
            "src/backend/Synthetic/Synthetic.csproj");

        Assert.Contains(unsafeDefinition.Errors, error => error.Contains(
            "Import",
            StringComparison.Ordinal));
        Assert.Contains(unsafeDefinition.Errors, error => error.Contains(
            "Analyzer",
            StringComparison.Ordinal));
        Assert.Contains(unsafeDefinition.Errors, error => error.Contains(
            "MSBuild indirection",
            StringComparison.Ordinal));
        Assert.Contains(unsafeDefinition.Errors, error => error.Contains(
            "Unreviewed package",
            StringComparison.Ordinal));
        Assert.Contains(unsafeDefinition.Errors, error => error.Contains(
            "Target",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("OpenApiDocumentsDirectory", "../../../artifacts/openapi")]
    [InlineData(
        "OpenApiGenerateDocumentsOptions",
        "--document-name goymen-erp-v1 --file-name openapi")]
    public void Api_OpenApi_canonical_artifact_path_and_name_are_exactly_guarded(
        string propertyName,
        string unsafeValue)
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            repositoryRoot,
            ApiProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        document.Descendants()
            .Single(element => element.Name.LocalName == propertyName)
            .Value = unsafeValue;

        var analysis = AnalyzeProjectDefinition(document, ApiProjectPath);

        Assert.Contains(analysis.Errors, error => error.Contains(
            $"API OpenAPI property {propertyName}",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Api_OpenApi_build_tool_and_security_pin_versions_are_exactly_guarded()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            repositoryRoot,
            ApiProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        document.Descendants()
            .Single(element =>
                element.Name.LocalName == "PackageReference"
                && element.Attribute("Include")?.Value
                    == "Microsoft.Extensions.ApiDescription.Server")
            .SetAttributeValue("Version", "10.0.8");
        document.Descendants()
            .Single(element =>
                element.Name.LocalName == "PackageReference"
                && element.Attribute("Include")?.Value == "Microsoft.OpenApi")
            .SetAttributeValue("Version", "2.7.5");

        var analysis = AnalyzeProjectDefinition(document, ApiProjectPath);

        Assert.Contains(analysis.Errors, error => error.Contains(
            "Microsoft.Extensions.ApiDescription.Server",
            StringComparison.Ordinal));
        Assert.Contains(analysis.Errors, error => error.Contains(
            "Microsoft.OpenApi",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Repository_build_security_pin_is_exact_and_fail_closed()
    {
        var canonical = """
            <Project>
              <PropertyGroup>
                <Deterministic>true</Deterministic>
                <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.13" />
              </ItemGroup>
            </Project>
            """.ReplaceLineEndings("\n");
        var canonicalErrors = new List<string>();
        ValidateReviewedDirectoryBuildProps(
            XDocument.Parse(canonical),
            "Directory.Build.props",
            canonicalErrors);
        Assert.Empty(canonicalErrors);

        var mutants = new[]
        {
            canonical.Replace("10.0.13", "10.0.12", StringComparison.Ordinal),
            canonical.Replace(
                "System.Security.Cryptography.Pkcs",
                "PDFsharp",
                StringComparison.Ordinal),
            canonical.Replace(
                " Version=\"10.0.13\"",
                " Version=\"10.0.13\" Condition=\"'$(Configuration)' == 'Release'\"",
                StringComparison.Ordinal),
            canonical.Replace(
                "</ItemGroup>",
                "<PackageReference Include=\"PDFsharp\" Version=\"6.2.4\" /></ItemGroup>",
                StringComparison.Ordinal),
            canonical.Replace(
                "  <ItemGroup>\n"
                + "    <PackageReference Include=\"System.Security.Cryptography.Pkcs\" Version=\"10.0.13\" />\n"
                + "  </ItemGroup>\n",
                string.Empty,
                StringComparison.Ordinal)
        };

        foreach (var mutant in mutants)
        {
            Assert.NotEqual(canonical, mutant);
            var errors = new List<string>();
            ValidateReviewedDirectoryBuildProps(
                XDocument.Parse(mutant),
                "Directory.Build.props",
                errors);
            Assert.NotEmpty(errors);
        }
    }

    [Theory]
    [InlineData(@"..\..\Shared\LinkedBypass.cs")]
    [InlineData("../../Shared/LinkedBypass.cs")]
    public void Linked_compile_source_outside_backend_is_scanned_on_both_path_styles(
        string compileInclude)
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-{Guid.NewGuid():N}");

        try
        {
            var projectDirectory = Path.Combine(repositoryRoot, "src", "backend", "Synthetic");
            var linkedDirectory = Path.Combine(repositoryRoot, "src", "Shared");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(linkedDirectory);
            File.WriteAllText(
                Path.Combine(projectDirectory, "Synthetic.csproj"),
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="{{compileInclude}}">
                      <Link>Linked\LinkedBypass.cs</Link>
                    </Compile>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(linkedDirectory, "LinkedBypass.cs"),
                "query.IgnoreQueryFilters();");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Empty(inventory.DefinitionErrors);
            Assert.Contains("src/Shared/LinkedBypass.cs", inventory.Sources.Keys);
            var occurrence = Assert.Single(FindOccurrences(inventory.Sources));
            Assert.Equal("src/Shared/LinkedBypass.cs", occurrence.RepositoryPath);
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Non_csharp_explicit_compile_input_is_rejected_fail_closed()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-noncs-{Guid.NewGuid():N}");

        try
        {
            var projectDirectory = Path.Combine(repositoryRoot, "src", "backend", "Synthetic");
            var linkedDirectory = Path.Combine(repositoryRoot, "src", "Shared");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(linkedDirectory);
            File.WriteAllText(
                Path.Combine(projectDirectory, "Synthetic.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="..\..\Shared\Bypass.txt">
                      <Link>Linked\Bypass.txt</Link>
                    </Compile>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(linkedDirectory, "Bypass.txt"),
                "query.IgnoreQueryFilters();");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "canonical .cs source extension",
                StringComparison.Ordinal));
            Assert.DoesNotContain("src/Shared/Bypass.txt", inventory.Sources.Keys);
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(@"..\..\Shared\CaseVariant.CS")]
    [InlineData("../../Shared/CaseVariant.CS")]
    public void Explicit_compile_input_requires_canonical_lowercase_cs_on_every_platform(
        string compileInclude)
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-case-source-{Guid.NewGuid():N}");

        try
        {
            var projectDirectory = Path.Combine(repositoryRoot, "src", "backend", "Synthetic");
            var linkedDirectory = Path.Combine(repositoryRoot, "src", "Shared");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(linkedDirectory);
            File.WriteAllText(
                Path.Combine(projectDirectory, "Synthetic.csproj"),
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="{{compileInclude}}" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(linkedDirectory, "CaseVariant.CS"),
                "internal class CaseVariant;");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "canonical .cs source extension",
                StringComparison.Ordinal));
            Assert.DoesNotContain("src/Shared/CaseVariant.CS", inventory.Sources.Keys);
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Unsupported_backend_project_and_managed_source_extensions_are_rejected()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-unsupported-language-{Guid.NewGuid():N}");

        try
        {
            var backendRoot = Path.Combine(repositoryRoot, "src", "backend");
            var csharpDirectory = Path.Combine(backendRoot, "Synthetic");
            var hiddenDirectory = Path.Combine(backendRoot, "Hidden");
            Directory.CreateDirectory(csharpDirectory);
            Directory.CreateDirectory(hiddenDirectory);
            File.WriteAllText(
                Path.Combine(csharpDirectory, "Synthetic.csproj"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");
            File.WriteAllText(
                Path.Combine(hiddenDirectory, "Hidden.fsproj"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");
            File.WriteAllText(
                Path.Combine(hiddenDirectory, "WrongCase.CSPROJ"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");
            File.WriteAllText(Path.Combine(hiddenDirectory, "Hidden.fs"), "module Hidden");
            File.WriteAllText(
                Path.Combine(hiddenDirectory, "Hidden.vb"),
                "Public Class Hidden\nEnd Class");
            File.WriteAllText(
                Path.Combine(hiddenDirectory, "CaseVariant.CS"),
                "internal class CaseVariant;");
            File.WriteAllText(
                Path.Combine(hiddenDirectory, "MixedCase.cS"),
                "internal class MixedCase;");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Hidden.fsproj",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "WrongCase.CSPROJ",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Hidden.fs",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Hidden.vb",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "CaseVariant.CS",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "MixedCase.cS",
                StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Project_reference_must_target_a_parsed_canonical_csharp_project()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-project-reference-{Guid.NewGuid():N}");

        try
        {
            var backendRoot = Path.Combine(repositoryRoot, "src", "backend");
            var sourceDirectory = Path.Combine(backendRoot, "Source");
            var hiddenDirectory = Path.Combine(backendRoot, "Hidden");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(hiddenDirectory);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "Source.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Hidden\Hidden.fsproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(hiddenDirectory, "Hidden.fsproj"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");
            File.WriteAllText(Path.Combine(hiddenDirectory, "Hidden.fs"), "module Hidden");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "canonical .csproj",
                StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(@"..\Target\Target.csproj")]
    [InlineData("../Target/Target.csproj")]
    public void Parsed_csharp_project_reference_supports_both_path_styles(
        string projectReferenceInclude)
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-csharp-reference-{Guid.NewGuid():N}");

        try
        {
            var backendRoot = Path.Combine(repositoryRoot, "src", "backend");
            var sourceDirectory = Path.Combine(backendRoot, "Source");
            var targetDirectory = Path.Combine(backendRoot, "Target");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "Source.csproj"),
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="{{projectReferenceInclude}}" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(targetDirectory, "Target.csproj"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");
            File.WriteAllText(Path.Combine(sourceDirectory, "Source.cs"), "internal class Source;");
            File.WriteAllText(Path.Combine(targetDirectory, "Target.cs"), "internal class Target;");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Empty(inventory.DefinitionErrors);
            Assert.Contains("src/backend/Source/Source.csproj", inventory.ProjectPaths);
            Assert.Contains("src/backend/Target/Target.csproj", inventory.ProjectPaths);
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Project_reference_target_must_be_successfully_parsed()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-malformed-reference-{Guid.NewGuid():N}");

        try
        {
            var backendRoot = Path.Combine(repositoryRoot, "src", "backend");
            var sourceDirectory = Path.Combine(backendRoot, "Source");
            var targetDirectory = Path.Combine(backendRoot, "Target");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "Source.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Target\Target.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(targetDirectory, "Target.csproj"), "<Project");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Could not parse backend project src/backend/Target/Target.csproj",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "not successfully parsed",
                StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Non_counterparty_project_cannot_reference_the_excluded_ownership_scope()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-counterparty-reference-{Guid.NewGuid():N}");

        try
        {
            var backendRoot = Path.Combine(repositoryRoot, "src", "backend");
            var sourceDirectory = Path.Combine(backendRoot, "Source");
            var ownedDirectory = Path.Combine(
                backendRoot,
                "Modules",
                "Counterparty",
                "Owned");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(ownedDirectory);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "Source.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference
                      Include="..\Modules\Counterparty\Owned\Owned.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(ownedDirectory, "Owned.csproj"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "excluded Counterparty ownership scope",
                StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("missing", "exactly three canonical edges")]
    [InlineData("extra", "exactly three canonical edges")]
    [InlineData("unknown", "unknown property 'unexpected'")]
    [InlineData("duplicate-property", "duplicate property 'schema_version'")]
    [InlineData("wildcard", "not a canonical repository path")]
    [InlineData("alternate-caller", "caller must be an exact reviewed composition root")]
    [InlineData("domain-target", "layer must be Application or Infrastructure")]
    public void Counterparty_contract_rejects_non_exact_declarations(
        string scenario,
        string expectedError)
    {
        var json = scenario switch
        {
            "missing" => CreateCounterpartyContractJson(includeInfrastructure: false),
            "extra" => CreateCounterpartyContractJson(includeExtraEdge: true),
            "unknown" => CreateCounterpartyContractJson(includeUnknownProperty: true),
            "duplicate-property" => CreateCounterpartyContractJson().Replace(
                "{\"schema_version\":1,",
                "{\"schema_version\":1,\"schema_version\":1,",
                StringComparison.Ordinal),
            "wildcard" => CreateCounterpartyContractJson(
                applicationTarget: ExcludedCounterpartyPrefix + "*.csproj"),
            "alternate-caller" => CreateCounterpartyContractJson(
                source: "src/backend/Alternate/Alternate.csproj"),
            "domain-target" => CreateCounterpartyContractJson(
                applicationTarget:
                    ExcludedCounterpartyPrefix
                    + "GoymenERP.Modules.Counterparty.Domain/"
                    + "GoymenERP.Modules.Counterparty.Domain.csproj",
                applicationLayer: "Domain"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };
        var errors = new List<string>();

        _ = ParseCounterpartyCompositionRootContract(json, errors);

        Assert.Contains(errors, error => error.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void Required_counterparty_contract_cannot_disappear_with_all_observed_edges()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-required-contract-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "backend"));

            var inventory = ReadBackendSourceInventory(
                repositoryRoot,
                requireCounterpartyContract: true);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "composition-root contract is missing",
                StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Counterparty_contract_and_observed_edges_must_match_exactly()
    {
        var parseErrors = new List<string>();
        var contract = ParseCounterpartyCompositionRootContract(
            CreateCounterpartyContractJson(),
            parseErrors);
        Assert.Empty(parseErrors);

        var exactEdges = contract.Edges.ToArray();
        Assert.Empty(ValidateCounterpartyCompositionRootEdges(contract, exactEdges));

        var missingErrors = ValidateCounterpartyCompositionRootEdges(
            contract,
            exactEdges.Take(1).ToArray());
        Assert.Contains(missingErrors, error => error.Contains(
            "Stale Counterparty composition-root declaration",
            StringComparison.Ordinal));

        var extraErrors = ValidateCounterpartyCompositionRootEdges(
            contract,
            exactEdges.Append(new CompositionRootEdge(
                "src/backend/Unexpected/Unexpected.csproj",
                ExcludedCounterpartyPrefix + "Unexpected/Unexpected.csproj")).ToArray());
        Assert.Contains(extraErrors, error => error.Contains(
            "without an exact module-owned declaration",
            StringComparison.Ordinal));

        var duplicateErrors = ValidateCounterpartyCompositionRootEdges(
            contract,
            exactEdges.Append(exactEdges[0]).ToArray());
        Assert.Contains(duplicateErrors, error => error.Contains(
            "Duplicate Counterparty composition-root ProjectReference",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Project_user_and_all_ancestor_msbuild_definitions_are_fail_closed()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-msbuild-{Guid.NewGuid():N}");

        try
        {
            var sourceRoot = Path.Combine(repositoryRoot, "src");
            var backendRoot = Path.Combine(sourceRoot, "backend");
            var projectDirectory = Path.Combine(backendRoot, "Synthetic");
            Directory.CreateDirectory(projectDirectory);
            var projectPath = Path.Combine(projectDirectory, "Synthetic.csproj");
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(projectDirectory, "Safe.cs"), "internal class Safe;");
            File.WriteAllText(
                projectPath + ".user",
                """
                <Project>
                  <Import Project="UserInjected.targets" />
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(repositoryRoot, "Directory.Build.props"),
                """
                <Project>
                  <Import Project="RootInjected.targets" />
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(sourceRoot, "Directory.Build.props"),
                """
                <Project>
                  <Import Project="AncestorInjected.targets" />
                  <ItemGroup>
                    <Compile Include="Injected.cs" />
                    <Analyzer Include="Generator.dll" />
                    <SourceGenerator Include="Generator.dll" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(backendRoot, "Directory.Build.targets"),
                """
                <Project>
                  <Target Name="InjectCompile" />
                </Project>
                """);

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Synthetic.csproj.user",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Directory.Build.targets",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Compile: src/Directory.Build.props",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Analyzer: src/Directory.Build.props",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Import: src/Directory.Build.props",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "SourceGenerator: src/Directory.Build.props",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.EndsWith(
                "Import: Directory.Build.props",
                StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Counterparty_ownership_exclusion_is_exact_and_case_sensitive()
    {
        Assert.True(IsExcludedCounterpartyPath(
            "src/backend/Modules/Counterparty/Owned.cs"));
        Assert.False(IsExcludedCounterpartyPath(
            "src/backend/Modules/counterparty/NotCanonical.cs"));
        Assert.False(IsExcludedCounterpartyPath(
            "src/backend/Modules/CounterpartyExtensions/NotOwned.cs"));
    }

    private static string CreateCounterpartyContractJson(
        string? source = null,
        string? applicationTarget = null,
        string applicationLayer = "Application",
        bool includeInfrastructure = true,
        bool includeExtraEdge = false,
        bool includeUnknownProperty = false)
    {
        source ??= ApiProjectPath;
        applicationTarget ??=
            ExcludedCounterpartyPrefix
            + "GoymenERP.Modules.Counterparty.Application/"
            + "GoymenERP.Modules.Counterparty.Application.csproj";
        var edges = new List<Dictionary<string, object?>>
        {
            new(StringComparer.Ordinal)
            {
                ["source"] = source,
                ["target"] = applicationTarget,
                ["layer"] = applicationLayer,
                ["reason"] = "Exact application composition edge."
            },
            new(StringComparer.Ordinal)
            {
                ["source"] = ManufacturingApplicationProjectPath,
                ["target"] =
                    ExcludedCounterpartyPrefix
                    + "GoymenERP.Modules.Counterparty.Application/"
                    + "GoymenERP.Modules.Counterparty.Application.csproj",
                ["layer"] = "Application",
                ["reason"] = "Manufacturing consumes the exact authorized Counterparty application read boundary."
            }
        };
        if (includeInfrastructure)
        {
            edges.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["source"] = source,
                ["target"] =
                    ExcludedCounterpartyPrefix
                    + "GoymenERP.Modules.Counterparty.Infrastructure/"
                    + "GoymenERP.Modules.Counterparty.Infrastructure.csproj",
                ["layer"] = "Infrastructure",
                ["reason"] = "Exact infrastructure composition edge."
            });
        }

        if (includeExtraEdge)
        {
            edges.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["source"] = source,
                ["target"] = applicationTarget,
                ["layer"] = applicationLayer,
                ["reason"] = "Duplicate extra edge."
            });
        }

        var contract = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schema_version"] = 1,
            ["owner"] = "CHATGPT-CARI",
            ["module"] = "Counterparty",
            ["excluded_source_prefix"] = ExcludedCounterpartyPrefix,
            ["contract_source"] = CounterpartyContractSourcePath,
            ["composition_root_edges"] = edges
        };
        if (includeUnknownProperty)
        {
            contract["unexpected"] = true;
        }

        return JsonSerializer.Serialize(contract);
    }

    private static BackendSourceInventory ReadBackendSourceInventory(
        string repositoryRoot,
        bool requireCounterpartyContract = false)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var definitionErrors = new List<string>();
        var projectPaths = new List<string>();
        var observedCounterpartyCompositionRootEdges = new List<CompositionRootEdge>();
        var automaticDefinitionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var backendRoot = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            BackendRootPath.Replace('/', Path.DirectorySeparatorChar)));

        if (!IsUnderDirectory(repositoryRoot, backendRoot) || !Directory.Exists(backendRoot))
        {
            throw new DirectoryNotFoundException(
                $"Tenant query-filter guard backend scope is missing: {BackendRootPath}");
        }

        foreach (var sourcePath in Directory.EnumerateFiles(
                     backendRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            AddPhysicalSource(repositoryRoot, sourcePath, sources, definitionErrors);
        }

        ValidateNoUnsupportedBackendInputs(
            repositoryRoot,
            backendRoot,
            definitionErrors);

        var projectDocuments = new List<BackendProjectDocument>();
        foreach (var projectPath in Directory.EnumerateFiles(
                     backendRoot,
                     "*",
                     SearchOption.AllDirectories)
                 .Where(path => string.Equals(
                     Path.GetExtension(path),
                     ".csproj",
                     StringComparison.Ordinal)))
        {
            var repositoryProjectPath = NormalizeRepositoryPath(
                Path.GetRelativePath(repositoryRoot, projectPath));
            if (IsExcludedCounterpartyPath(repositoryProjectPath)
                || IsBuildOutput(repositoryProjectPath))
            {
                continue;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(projectPath, LoadOptions.SetLineInfo);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.Xml.XmlException)
            {
                definitionErrors.Add(
                    $"Could not parse backend project {repositoryProjectPath}: "
                    + exception.Message);
                continue;
            }

            projectDocuments.Add(new BackendProjectDocument(
                projectPath,
                repositoryProjectPath,
                document));
        }

        var parsedProjectPaths = projectDocuments
            .Select(project => project.RepositoryPath)
            .ToHashSet(StringComparer.Ordinal);
        projectPaths.AddRange(parsedProjectPaths);

        foreach (var project in projectDocuments)
        {
            CollectAutomaticBuildDefinitionPaths(
                repositoryRoot,
                project.FullPath,
                automaticDefinitionPaths,
                definitionErrors);
            var analysis = AnalyzeProjectDefinition(
                project.Document,
                project.RepositoryPath);
            definitionErrors.AddRange(analysis.Errors);
            ValidateProjectReferences(
                repositoryRoot,
                backendRoot,
                project.FullPath,
                project.Document,
                parsedProjectPaths,
                observedCounterpartyCompositionRootEdges,
                definitionErrors);
            AddExplicitCompileIncludes(
                repositoryRoot,
                project.FullPath,
                analysis.CompileDirectives,
                sources,
                definitionErrors);
        }

        ValidateAutomaticBuildDefinitions(
            repositoryRoot,
            automaticDefinitionPaths,
            definitionErrors);
        ValidateGeneratedViewFiles(backendRoot, repositoryRoot, definitionErrors);
        var counterpartyContractFullPath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            CounterpartyCompositionRootContractPath.Replace(
                '/',
                Path.DirectorySeparatorChar)));
        if (requireCounterpartyContract
            || observedCounterpartyCompositionRootEdges.Count > 0
            || File.Exists(counterpartyContractFullPath))
        {
            var counterpartyContract = ReadCounterpartyCompositionRootContract(
                repositoryRoot,
                definitionErrors);
            definitionErrors.AddRange(ValidateCounterpartyCompositionRootEdges(
                counterpartyContract,
                observedCounterpartyCompositionRootEdges));
        }

        return new BackendSourceInventory(
            sources,
            projectPaths.Order(StringComparer.Ordinal).ToArray(),
            definitionErrors);
    }

    private static CounterpartyCompositionRootContract ReadCounterpartyCompositionRootContract(
        string repositoryRoot,
        ICollection<string> errors)
    {
        var contractPath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            CounterpartyCompositionRootContractPath.Replace(
                '/',
                Path.DirectorySeparatorChar)));
        if (!IsUnderDirectory(repositoryRoot, contractPath) || !File.Exists(contractPath))
        {
            errors.Add(
                "Counterparty composition-root contract is missing or outside the repository: "
                + CounterpartyCompositionRootContractPath);
            return new CounterpartyCompositionRootContract([]);
        }

        CounterpartyCompositionRootContract contract;
        try
        {
            contract = ParseCounterpartyCompositionRootContract(
                File.ReadAllText(contractPath),
                errors);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException)
        {
            errors.Add(
                "Counterparty composition-root contract could not be read as strict JSON: "
                + exception.Message);
            return new CounterpartyCompositionRootContract([]);
        }

        var contractSourcePath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            CounterpartyContractSourcePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnderDirectory(repositoryRoot, contractSourcePath)
            || !File.Exists(contractSourcePath))
        {
            errors.Add(
                "Counterparty composition-root contract source is missing or outside the "
                + $"repository: {CounterpartyContractSourcePath}");
        }

        return contract;
    }

    private static CounterpartyCompositionRootContract ParseCounterpartyCompositionRootContract(
        string json,
        ICollection<string> errors)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Counterparty composition-root contract root must be an object.");
            return new CounterpartyCompositionRootContract([]);
        }

        ValidateExactJsonProperties(
            root,
            [
                "schema_version",
                "owner",
                "module",
                "excluded_source_prefix",
                "contract_source",
                "composition_root_edges"
            ],
            "Counterparty composition-root contract",
            errors);

        ValidateExactJsonInteger(
            root,
            "schema_version",
            expected: 1,
            "Counterparty composition-root contract",
            errors);
        ValidateExactJsonString(
            root,
            "owner",
            "CHATGPT-CARI",
            "Counterparty composition-root contract",
            errors);
        ValidateExactJsonString(
            root,
            "module",
            "Counterparty",
            "Counterparty composition-root contract",
            errors);
        ValidateExactJsonString(
            root,
            "excluded_source_prefix",
            ExcludedCounterpartyPrefix,
            "Counterparty composition-root contract",
            errors);
        ValidateExactJsonString(
            root,
            "contract_source",
            CounterpartyContractSourcePath,
            "Counterparty composition-root contract",
            errors);

        if (!TryGetSingleJsonProperty(
                root,
                "composition_root_edges",
                JsonValueKind.Array,
                "Counterparty composition-root contract",
                errors,
                out var edgeArray))
        {
            return new CounterpartyCompositionRootContract([]);
        }

        var edgeElements = edgeArray.EnumerateArray().ToArray();
        if (edgeElements.Length != 3)
        {
            errors.Add(
                "Counterparty composition-root contract must declare exactly three canonical "
                + $"edges, but found {edgeElements.Length}.");
        }

        var edges = new List<CompositionRootEdge>();
        var layers = new List<string>();
        for (var index = 0; index < edgeElements.Length; index++)
        {
            var edge = edgeElements[index];
            var context = $"Counterparty composition-root edge[{index}]";
            if (edge.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{context} must be an object.");
                continue;
            }

            ValidateExactJsonProperties(
                edge,
                ["source", "target", "layer", "reason"],
                context,
                errors);
            if (!TryReadJsonString(edge, "source", context, errors, out var source)
                || !TryReadJsonString(edge, "target", context, errors, out var target)
                || !TryReadJsonString(edge, "layer", context, errors, out var layer)
                || !TryReadJsonString(edge, "reason", context, errors, out var reason))
            {
                continue;
            }

            if (!IsCanonicalContractRepositoryPath(source))
            {
                errors.Add($"{context} source is not a canonical repository path: {source}");
            }

            if (!IsCanonicalContractRepositoryPath(target))
            {
                errors.Add($"{context} target is not a canonical repository path: {target}");
            }

            if (!string.Equals(source, ApiProjectPath, StringComparison.Ordinal)
                && !string.Equals(source, ManufacturingApplicationProjectPath, StringComparison.Ordinal))
            {
                errors.Add(
                    $"{context} caller must be an exact reviewed composition root: {source}");
            }

            if (string.Equals(source, ManufacturingApplicationProjectPath, StringComparison.Ordinal)
                && !string.Equals(layer, "Application", StringComparison.Ordinal))
            {
                errors.Add($"{context} Manufacturing caller may reference only the Application layer.");
            }

            var expectedTarget = layer switch
            {
                "Application" =>
                    ExcludedCounterpartyPrefix
                    + "GoymenERP.Modules.Counterparty.Application/"
                    + "GoymenERP.Modules.Counterparty.Application.csproj",
                "Infrastructure" =>
                    ExcludedCounterpartyPrefix
                    + "GoymenERP.Modules.Counterparty.Infrastructure/"
                    + "GoymenERP.Modules.Counterparty.Infrastructure.csproj",
                _ => null
            };
            if (expectedTarget is null)
            {
                errors.Add(
                    $"{context} layer must be Application or Infrastructure: {layer}");
            }
            else if (!string.Equals(target, expectedTarget, StringComparison.Ordinal))
            {
                errors.Add(
                    $"{context} target does not match its exact canonical {layer} project: "
                    + target);
            }

            if (string.IsNullOrWhiteSpace(reason) || reason.Length != reason.Trim().Length)
            {
                errors.Add($"{context} reason must be non-empty and trimmed.");
            }

            layers.Add(layer);
            edges.Add(new CompositionRootEdge(source, target));
        }

        if (edges.Distinct().Count() != edges.Count)
        {
            errors.Add("Counterparty composition-root contract contains duplicate edges.");
        }

        if (layers.Count(layer => string.Equals(
                layer,
                "Application",
                StringComparison.Ordinal)) != 2
            || layers.Count(layer => string.Equals(
                layer,
                "Infrastructure",
                StringComparison.Ordinal)) != 1)
        {
            errors.Add(
                "Counterparty composition-root contract must contain exactly two Application "
                + "and one Infrastructure edge.");
        }

        return new CounterpartyCompositionRootContract(edges);
    }

    private static IReadOnlyList<string> ValidateCounterpartyCompositionRootEdges(
        CounterpartyCompositionRootContract contract,
        IReadOnlyCollection<CompositionRootEdge> observedEdges)
    {
        var errors = new List<string>();
        var duplicateObservedEdges = observedEdges
            .GroupBy(edge => edge)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.Ordinal)
            .ToArray();
        foreach (var duplicate in duplicateObservedEdges)
        {
            errors.Add(
                "Duplicate Counterparty composition-root ProjectReference was observed: "
                + $"{duplicate.Source} -> {duplicate.Target}");
        }

        var declared = contract.Edges.ToHashSet();
        var observed = observedEdges.ToHashSet();
        foreach (var missing in declared.Except(observed)
                     .OrderBy(edge => edge.Source, StringComparer.Ordinal)
                     .ThenBy(edge => edge.Target, StringComparer.Ordinal))
        {
            errors.Add(
                "Stale Counterparty composition-root declaration has no matching "
                + $"ProjectReference: {missing.Source} -> {missing.Target}");
        }

        foreach (var extra in observed.Except(declared)
                     .OrderBy(edge => edge.Source, StringComparer.Ordinal)
                     .ThenBy(edge => edge.Target, StringComparer.Ordinal))
        {
            errors.Add(
                "A non-Counterparty project cannot reference the excluded Counterparty "
                + "ownership scope without an exact module-owned declaration: "
                + $"{extra.Target} ({extra.Source})");
        }

        return errors;
    }

    private static void ValidateExactJsonProperties(
        JsonElement element,
        IReadOnlyCollection<string> expectedProperties,
        string context,
        ICollection<string> errors)
    {
        var properties = element.EnumerateObject().ToArray();
        foreach (var duplicate in properties
                     .GroupBy(property => property.Name, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key)
                     .Order(StringComparer.Ordinal))
        {
            errors.Add($"{context} contains duplicate property '{duplicate}'.");
        }

        var expected = expectedProperties.ToHashSet(StringComparer.Ordinal);
        foreach (var unknown in properties
                     .Select(property => property.Name)
                     .Where(property => !expected.Contains(property))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            errors.Add($"{context} contains unknown property '{unknown}'.");
        }

        var actual = properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var missing in expected
                     .Where(property => !actual.Contains(property))
                     .Order(StringComparer.Ordinal))
        {
            errors.Add($"{context} is missing property '{missing}'.");
        }
    }

    private static void ValidateExactJsonInteger(
        JsonElement element,
        string propertyName,
        int expected,
        string context,
        ICollection<string> errors)
    {
        if (!TryGetSingleJsonProperty(
                element,
                propertyName,
                JsonValueKind.Number,
                context,
                errors,
                out var value))
        {
            return;
        }

        if (!value.TryGetInt32(out var actual) || actual != expected)
        {
            errors.Add(
                $"{context} property '{propertyName}' must equal {expected}, found "
                + value.GetRawText());
        }
    }

    private static void ValidateExactJsonString(
        JsonElement element,
        string propertyName,
        string expected,
        string context,
        ICollection<string> errors)
    {
        if (!TryReadJsonString(
                element,
                propertyName,
                context,
                errors,
                out var actual))
        {
            return;
        }

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            errors.Add(
                $"{context} property '{propertyName}' must equal '{expected}', found "
                + $"'{actual}'.");
        }
    }

    private static bool TryReadJsonString(
        JsonElement element,
        string propertyName,
        string context,
        ICollection<string> errors,
        out string value)
    {
        if (!TryGetSingleJsonProperty(
                element,
                propertyName,
                JsonValueKind.String,
                context,
                errors,
                out var property))
        {
            value = string.Empty;
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetSingleJsonProperty(
        JsonElement element,
        string propertyName,
        JsonValueKind expectedKind,
        string context,
        ICollection<string> errors,
        out JsonElement value)
    {
        var matches = element.EnumerateObject()
            .Where(property => string.Equals(
                property.Name,
                propertyName,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            value = default;
            return false;
        }

        value = matches[0].Value;
        if (value.ValueKind == expectedKind)
        {
            return true;
        }

        errors.Add(
            $"{context} property '{propertyName}' must be {expectedKind}, found "
            + $"{value.ValueKind}.");
        return false;
    }

    private static bool IsCanonicalContractRepositoryPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != value.Trim().Length
            || value.StartsWith("/", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.IndexOfAny(['*', '?', '[', ']', ':']) >= 0)
        {
            return false;
        }

        var segments = value.Split('/');
        return segments.Length > 1
               && segments.All(segment => segment.Length > 0
                                          && !string.Equals(
                                              segment,
                                              ".",
                                              StringComparison.Ordinal)
                                          && !string.Equals(
                                              segment,
                                              "..",
                                              StringComparison.Ordinal));
    }

    private static void ValidateNoUnsupportedBackendInputs(
        string repositoryRoot,
        string backendRoot,
        ICollection<string> errors)
    {
        foreach (var path in Directory.EnumerateFiles(
                     backendRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            var repositoryPath = NormalizeRepositoryPath(
                Path.GetRelativePath(repositoryRoot, path));
            if (IsBuildOutput(repositoryPath)
                || IsExcludedCounterpartyPath(repositoryPath))
            {
                continue;
            }

            var extension = Path.GetExtension(path);
            if (extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".csproj", StringComparison.Ordinal))
            {
                errors.Add(
                    $"Unsupported backend project input '{extension}' is outside the reviewed "
                    + $"C# production-source inventory: {repositoryPath}");
            }

            if (UnsupportedManagedSourceExtensions.Contains(extension))
            {
                errors.Add(
                    $"Unsupported managed source input '{extension}' is outside the reviewed "
                    + $"C# production-source inventory: {repositoryPath}");
            }

            if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".cs", StringComparison.Ordinal))
            {
                errors.Add(
                    $"Non-canonical C# source extension '{extension}' is not guaranteed to "
                    + $"participate in the same compiled-input inventory on every platform: "
                    + repositoryPath);
            }
        }
    }

    private static void AddPhysicalSource(
        string repositoryRoot,
        string sourcePath,
        IDictionary<string, string> sources,
        ICollection<string> errors)
    {
        var repositoryPath = NormalizeRepositoryPath(
            Path.GetRelativePath(repositoryRoot, sourcePath));
        if (IsBuildOutput(repositoryPath) || IsExcludedCounterpartyPath(repositoryPath))
        {
            return;
        }

        if (!IsUnderDirectory(repositoryRoot, Path.GetFullPath(sourcePath)))
        {
            errors.Add($"Backend source is outside the repository: {sourcePath}");
            return;
        }

        if (!sources.TryAdd(repositoryPath, File.ReadAllText(sourcePath)))
        {
            errors.Add($"Backend source was scanned more than once: {repositoryPath}");
        }
    }

    private static void ValidateProjectReferences(
        string repositoryRoot,
        string backendRoot,
        string projectPath,
        XDocument document,
        IReadOnlySet<string> parsedProjectPaths,
        ICollection<CompositionRootEdge> observedCounterpartyCompositionRootEdges,
        ICollection<string> errors)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var repositoryProjectPath = NormalizeRepositoryPath(
            Path.GetRelativePath(repositoryRoot, projectPath));

        foreach (var projectReference in document.Descendants()
                     .Where(element => element.Name.LocalName == "ProjectReference"))
        {
            var include = projectReference.Attribute("Include")?.Value?.Trim();
            if (include is null
                || HasMsBuildIndirection(include)
                || projectReference.Attribute("Condition") is not null
                || projectReference.Ancestors()
                    .Any(ancestor => ancestor.Attribute("Condition") is not null))
            {
                errors.Add(
                    "ProjectReference must be a literal unconditional backend project path: "
                    + repositoryProjectPath);
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(
                projectDirectory,
                ToNativeFileSystemPath(include)));
            if (!IsUnderDirectory(repositoryRoot, targetPath)
                || !IsUnderDirectory(backendRoot, targetPath)
                || !File.Exists(targetPath))
            {
                errors.Add(
                    $"ProjectReference target is missing or outside src/backend: {include} "
                    + $"({repositoryProjectPath})");
                continue;
            }

            var targetRepositoryPath = NormalizeRepositoryPath(
                Path.GetRelativePath(repositoryRoot, targetPath));
            if (!string.Equals(
                    Path.GetExtension(targetPath),
                    ".csproj",
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "ProjectReference target must be a canonical .csproj in the reviewed "
                    + $"C# inventory: {targetRepositoryPath} ({repositoryProjectPath})");
                continue;
            }

            if (IsExcludedCounterpartyPath(targetRepositoryPath))
            {
                observedCounterpartyCompositionRootEdges.Add(new CompositionRootEdge(
                    repositoryProjectPath,
                    targetRepositoryPath));
                continue;
            }

            if (!parsedProjectPaths.Contains(targetRepositoryPath))
            {
                errors.Add(
                    "ProjectReference target was not successfully parsed into the reviewed "
                    + $"C# project inventory: {targetRepositoryPath} "
                    + $"({repositoryProjectPath})");
            }
        }
    }

    private static ProjectDefinitionAnalysis AnalyzeProjectDefinition(
        XDocument document,
        string repositoryProjectPath)
    {
        var errors = new List<string>();
        var directives = new List<CompileDirective>();
        var root = document.Root;

        if (root is null || root.Name.LocalName != "Project")
        {
            errors.Add($"Backend project has no Project root: {repositoryProjectPath}");
            return new ProjectDefinitionAnalysis(directives, errors);
        }

        var sdk = root.Attribute("Sdk")?.Value?.Trim();
        if (sdk is null || !ReviewedProjectSdks.Contains(sdk))
        {
            errors.Add(
                $"Backend project uses an unreviewed SDK '{sdk ?? "<missing>"}': "
                + repositoryProjectPath);
        }
        ValidateAllowedAttributes(
            root,
            new HashSet<string>(StringComparer.Ordinal) { "Sdk" },
            "Project",
            repositoryProjectPath,
            errors);

        foreach (var topLevel in root.Elements())
        {
            if (topLevel.Name.LocalName is not ("ItemGroup" or "PropertyGroup"))
            {
                errors.Add(
                    $"Unreviewed top-level MSBuild element {topLevel.Name.LocalName}: "
                    + repositoryProjectPath);
            }
        }

        foreach (var propertyGroup in root.Elements()
                     .Where(element => element.Name.LocalName == "PropertyGroup"))
        {
            ValidateAllowedAttributes(
                propertyGroup,
                new HashSet<string>(StringComparer.Ordinal),
                "PropertyGroup",
                repositoryProjectPath,
                errors);
            foreach (var property in propertyGroup.Elements())
            {
                ValidateAllowedAttributes(
                    property,
                    new HashSet<string>(StringComparer.Ordinal),
                    property.Name.LocalName,
                    repositoryProjectPath,
                    errors);
                var propertyName = property.Name.LocalName;
                var isReviewedOpenApiProperty = string.Equals(
                        repositoryProjectPath,
                        ApiProjectPath,
                        StringComparison.Ordinal)
                    && ReviewedApiOpenApiProperties.TryGetValue(
                        propertyName,
                        out var reviewedOpenApiValue)
                    && string.Equals(
                        property.Value.Trim(),
                        reviewedOpenApiValue,
                        StringComparison.Ordinal);
                if (!ReviewedProjectProperties.Contains(propertyName)
                    && !isReviewedOpenApiProperty)
                {
                    errors.Add(
                        $"Unreviewed project property {propertyName} can alter "
                        + $"compilation: {repositoryProjectPath}");
                }
                if (HasMsBuildIndirection(property.Value))
                {
                    errors.Add(
                        $"Project property {property.Name.LocalName} contains MSBuild "
                        + $"indirection: {repositoryProjectPath}");
                }
            }
        }

        if (string.Equals(repositoryProjectPath, ApiProjectPath, StringComparison.Ordinal))
        {
            foreach (var (propertyName, reviewedValue) in ReviewedApiOpenApiProperties)
            {
                var matches = root.Descendants()
                    .Where(element => string.Equals(
                        element.Name.LocalName,
                        propertyName,
                        StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length != 1
                    || !string.Equals(
                        matches[0].Value.Trim(),
                        reviewedValue,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        $"API OpenAPI property {propertyName} must occur exactly once with "
                        + $"the reviewed value '{reviewedValue}': {repositoryProjectPath}");
                }
            }
        }

        foreach (var itemGroup in root.Elements()
                     .Where(element => element.Name.LocalName == "ItemGroup"))
        {
            ValidateAllowedAttributes(
                itemGroup,
                new HashSet<string>(StringComparer.Ordinal),
                "ItemGroup",
                repositoryProjectPath,
                errors);
            foreach (var item in itemGroup.Elements())
            {
                var includedContent = item.Attribute("Include")?.Value;
                var isReviewedPdfFontContent = string.Equals(
                        repositoryProjectPath,
                        PdfProjectPath,
                        StringComparison.Ordinal)
                    && string.Equals(item.Name.LocalName, "Content", StringComparison.Ordinal)
                    && includedContent is not null
                    && ReviewedPdfContentPaths.Contains(includedContent)
                    && string.Equals(
                        item.Attribute("CopyToOutputDirectory")?.Value,
                        "PreserveNewest",
                        StringComparison.Ordinal)
                    && string.Equals(
                        item.Attribute("CopyToPublishDirectory")?.Value,
                        "PreserveNewest",
                        StringComparison.Ordinal)
                    && item.Attributes().Count() == 3
                    && !item.Elements().Any();
                if (!ReviewedProjectItems.Contains(item.Name.LocalName)
                    && !isReviewedPdfFontContent)
                {
                    errors.Add(
                        $"Unreviewed project item {item.Name.LocalName} can alter compilation: "
                        + repositoryProjectPath);
                }
            }
        }

        foreach (var element in root.Descendants())
        {
            var localName = element.Name.LocalName;
            if (localName is "Import" or "Target" or "Sdk")
            {
                errors.Add(
                    $"Unreviewed MSBuild {localName} can alter compiled production input: "
                    + repositoryProjectPath);
            }

            if (ForbiddenGeneratedSourceItems.Contains(localName))
            {
                errors.Add(
                    $"Unreviewed generated-source item {localName} is forbidden: "
                    + repositoryProjectPath);
            }

            if (localName.Contains("Generator", StringComparison.OrdinalIgnoreCase)
                || localName.Contains("Razor", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Unreviewed generator/Razor property or item {localName} is forbidden: "
                    + repositoryProjectPath);
            }
        }

        foreach (var packageReference in root.Descendants()
                     .Where(element => element.Name.LocalName == "PackageReference"))
        {
            ValidatePackageReference(packageReference, repositoryProjectPath, errors);
        }

        if (string.Equals(repositoryProjectPath, PdfProjectPath, StringComparison.Ordinal))
        {
            var pdfsharpReferences = root.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Where(element => string.Equals(
                    element.Attribute("Include")?.Value,
                    "PDFsharp",
                    StringComparison.Ordinal))
                .ToArray();
            if (pdfsharpReferences.Length != 1)
            {
                errors.Add("The PDF component must reference PDFsharp exactly once.");
            }

            var contentElements = root.Descendants()
                .Where(element => element.Name.LocalName == "Content")
                .ToArray();
            var reviewedContent = contentElements
                .Select(element => element.Attribute("Include")?.Value)
                .Where(path => path is not null)
                .ToHashSet(StringComparer.Ordinal);
            if (contentElements.Length != ReviewedPdfContentPaths.Count
                || reviewedContent.Count != ReviewedPdfContentPaths.Count
                || !reviewedContent.SetEquals(ReviewedPdfContentPaths))
            {
                errors.Add("The PDF component content inventory must match the reviewed font and license assets.");
            }
        }


        if (string.Equals(repositoryProjectPath, ApiProjectPath, StringComparison.Ordinal))
        {
            var apiPackageNames = root.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element => element.Attribute("Include")?.Value.Trim())
                .Where(name => name is not null)
                .ToArray();
            foreach (var packageName in RequiredApiOpenApiPackages)
            {
                if (apiPackageNames.Count(name => string.Equals(
                        name,
                        packageName,
                        StringComparison.Ordinal)) != 1)
                {
                    errors.Add(
                        $"API must reference exact OpenAPI package {packageName} once: "
                        + repositoryProjectPath);
                }
            }
        }

        foreach (var projectReference in root.Descendants()
                     .Where(element => element.Name.LocalName == "ProjectReference"))
        {
            ValidateAllowedAttributes(
                projectReference,
                new HashSet<string>(StringComparer.Ordinal) { "Include" },
                "ProjectReference",
                repositoryProjectPath,
                errors);
            if (projectReference.Elements().Any())
            {
                errors.Add(
                    "ProjectReference metadata requires explicit compilation review: "
                    + repositoryProjectPath);
            }
            if (HasMetadata(projectReference, "OutputItemType")
                || HasMetadata(projectReference, "Generator")
                || HasMetadata(projectReference, "ReferenceSourceTarget"))
            {
                errors.Add(
                    "ProjectReference cannot act as an analyzer/source generator without review: "
                    + repositoryProjectPath);
            }
        }

        foreach (var compile in root.Descendants()
                     .Where(element => element.Name.LocalName == "Compile"))
        {
            var directive = AnalyzeCompileDirective(
                compile,
                repositoryProjectPath,
                errors);
            if (directive is not null)
            {
                directives.Add(directive);
            }
        }

        return new ProjectDefinitionAnalysis(directives, errors);
    }

    private static void ValidatePackageReference(
        XElement packageReference,
        string repositoryProjectPath,
        ICollection<string> errors)
    {
        var include = packageReference.Attribute("Include")?.Value?.Trim();
        var version = packageReference.Attribute("Version")?.Value?.Trim()
                      ?? packageReference.Elements()
                          .SingleOrDefault(element => element.Name.LocalName == "Version")
                          ?.Value.Trim();
        ValidateAllowedAttributes(
            packageReference,
            new HashSet<string>(StringComparer.Ordinal) { "Include", "Version" },
            "PackageReference",
            repositoryProjectPath,
            errors);

        if (include is null
            || version is null
            || !ReviewedPackageReferences.TryGetValue(include, out var reviewedVersion)
            || !string.Equals(version, reviewedVersion, StringComparison.Ordinal))
        {
            errors.Add(
                $"Unreviewed package/source-generator route '{include ?? "<missing>"}' "
                + $"version '{version ?? "<missing>"}': {repositoryProjectPath}");
        }

        if (string.Equals(include, "PDFsharp", StringComparison.Ordinal)
            && !string.Equals(repositoryProjectPath, PdfProjectPath, StringComparison.Ordinal))
        {
            errors.Add("PDFsharp is allowed only in the isolated PDF component project.");
        }

        if (packageReference.Attribute("Condition") is not null
            || packageReference.Ancestors()
                .Any(ancestor => ancestor.Attribute("Condition") is not null)
            || packageReference.Attribute("Update") is not null
            || packageReference.Attribute("Remove") is not null
            || HasMetadata(packageReference, "OutputItemType")
            || HasMetadata(packageReference, "GeneratePathProperty")
            || HasMetadata(packageReference, "Generator"))
        {
            errors.Add(
                "Conditional or generator-shaped PackageReference is forbidden without review: "
                + repositoryProjectPath);
        }

        if (string.Equals(
                include,
                "Microsoft.Extensions.ApiDescription.Server",
                StringComparison.Ordinal))
        {
            var includeAssets = packageReference.Elements()
                .SingleOrDefault(element => element.Name.LocalName == "IncludeAssets")
                ?.Value.Trim();
            var privateAssets = packageReference.Elements()
                .SingleOrDefault(element => element.Name.LocalName == "PrivateAssets")
                ?.Value.Trim();
            if (!string.Equals(
                    includeAssets,
                    "runtime; build; native; contentfiles; analyzers",
                    StringComparison.Ordinal)
                || !string.Equals(privateAssets, "all", StringComparison.Ordinal))
            {
                errors.Add(
                    "Microsoft.Extensions.ApiDescription.Server must remain a private, "
                    + "exact build-time dependency with its reviewed IncludeAssets set: "
                    + repositoryProjectPath);
            }
        }

        var allowedMetadata = new HashSet<string>(StringComparer.Ordinal)
        {
            "ExcludeAssets",
            "IncludeAssets",
            "PrivateAssets",
            "Version"
        };
        var unreviewedMetadata = packageReference.Elements()
            .Select(element => element.Name.LocalName)
            .Where(name => !allowedMetadata.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unreviewedMetadata.Length > 0)
        {
            errors.Add(
                "Unreviewed PackageReference metadata can alter compile inputs: "
                + string.Join(", ", unreviewedMetadata)
                + $" ({repositoryProjectPath})");
        }
    }

    private static CompileDirective? AnalyzeCompileDirective(
        XElement compile,
        string repositoryProjectPath,
        ICollection<string> errors)
    {
        ValidateAllowedAttributes(
            compile,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Condition",
                "Exclude",
                "Include",
                "Remove",
                "Update"
            },
            "Compile",
            repositoryProjectPath,
            errors);
        if (compile.Attribute("Condition") is not null
            || compile.Ancestors().Any(ancestor => ancestor.Attribute("Condition") is not null))
        {
            errors.Add(
                "Conditional Compile items require evaluated MSBuild review: "
                + repositoryProjectPath);
        }

        var candidates = new[]
        {
            (Kind: CompileDirectiveKind.Include, Value: compile.Attribute("Include")?.Value),
            (Kind: CompileDirectiveKind.Remove, Value: compile.Attribute("Remove")?.Value),
            (Kind: CompileDirectiveKind.Update, Value: compile.Attribute("Update")?.Value)
        }.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Value)).ToArray();

        if (candidates.Length != 1)
        {
            errors.Add(
                "Compile item must have exactly one literal Include, Remove, or Update: "
                + repositoryProjectPath);
            return null;
        }

        var items = SplitLiteralMsBuildItems(
            candidates[0].Value!,
            "Compile",
            repositoryProjectPath,
            errors);
        var exclude = compile.Attribute("Exclude")?.Value;
        var excludes = string.IsNullOrWhiteSpace(exclude)
            ? Array.Empty<string>()
            : SplitLiteralMsBuildItems(
                exclude,
                "Compile Exclude",
                repositoryProjectPath,
                errors);
        var link = compile.Elements()
            .SingleOrDefault(element => element.Name.LocalName == "Link")
            ?.Value.Trim();
        if (link is not null && HasMsBuildIndirection(link))
        {
            errors.Add(
                $"Compile Link contains MSBuild indirection '{link}': {repositoryProjectPath}");
        }

        var allowedMetadata = new HashSet<string>(StringComparer.Ordinal)
        {
            "AutoGen",
            "CopyToOutputDirectory",
            "DependentUpon",
            "DesignTime",
            "Link",
            "Visible"
        };
        var unreviewedMetadata = compile.Elements()
            .Select(element => element.Name.LocalName)
            .Where(name => !allowedMetadata.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unreviewedMetadata.Length > 0)
        {
            errors.Add(
                "Unreviewed Compile metadata can alter generated inputs: "
                + string.Join(", ", unreviewedMetadata)
                + $" ({repositoryProjectPath})");
        }

        return new CompileDirective(candidates[0].Kind, items, excludes, link);
    }

    private static string[] SplitLiteralMsBuildItems(
        string value,
        string kind,
        string repositoryProjectPath,
        ICollection<string> errors)
    {
        var items = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length == 0)
        {
            errors.Add($"{kind} contains no paths: {repositoryProjectPath}");
        }

        foreach (var item in items.Where(HasMsBuildIndirection))
        {
            errors.Add(
                $"{kind} contains unsupported MSBuild indirection '{item}': "
                + repositoryProjectPath);
        }

        return items;
    }

    private static bool HasMsBuildIndirection(string value)
    {
        return value.IndexOfAny(['*', '?']) >= 0
               || value.Contains("$(", StringComparison.Ordinal)
               || value.Contains("@(", StringComparison.Ordinal)
               || value.Contains("%(", StringComparison.Ordinal);
    }

    private static bool HasMetadata(XElement element, string localName)
    {
        return element.Attribute(localName) is not null
               || element.Elements().Any(child => child.Name.LocalName == localName);
    }

    private static void ValidateAllowedAttributes(
        XElement element,
        IReadOnlySet<string> allowedAttributes,
        string itemKind,
        string repositoryProjectPath,
        ICollection<string> errors)
    {
        var unreviewed = element.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration)
            .Select(attribute => attribute.Name.LocalName)
            .Where(name => !allowedAttributes.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unreviewed.Length > 0)
        {
            errors.Add(
                $"Unreviewed {itemKind} attributes can alter compile inputs: "
                + string.Join(", ", unreviewed)
                + $" ({repositoryProjectPath})");
        }
    }

    private static void AddExplicitCompileIncludes(
        string repositoryRoot,
        string projectPath,
        IReadOnlyList<CompileDirective> directives,
        IDictionary<string, string> sources,
        ICollection<string> errors)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;

        foreach (var directive in directives.Where(
                     directive => directive.Kind == CompileDirectiveKind.Include))
        {
            var excluded = directive.Excludes
                .Select(path => NormalizeRepositoryPath(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var item in directive.Items)
            {
                if (HasMsBuildIndirection(item)
                    || excluded.Contains(NormalizeRepositoryPath(item)))
                {
                    continue;
                }

                var nativeItem = ToNativeFileSystemPath(item);
                if (!Path.GetExtension(nativeItem).Equals(".cs", StringComparison.Ordinal))
                {
                    errors.Add(
                        $"Explicit Compile Include must use the canonical .cs source extension "
                        + $"and cannot use '{item}': "
                        + NormalizeRepositoryPath(
                            Path.GetRelativePath(repositoryRoot, projectPath)));
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(
                    projectDirectory,
                    nativeItem));
                if (!IsUnderDirectory(repositoryRoot, fullPath) || !File.Exists(fullPath))
                {
                    errors.Add(
                        $"Explicit Compile source is missing or outside the repository: {item} "
                        + $"({NormalizeRepositoryPath(Path.GetRelativePath(repositoryRoot, projectPath))})");
                    continue;
                }

                var repositoryPath = NormalizeRepositoryPath(
                    Path.GetRelativePath(repositoryRoot, fullPath));
                if (IsExcludedCounterpartyPath(repositoryPath))
                {
                    errors.Add(
                        "A non-Counterparty project cannot link excluded Counterparty source: "
                        + repositoryPath);
                    continue;
                }

                if (Path.GetExtension(fullPath).Equals(".cs", StringComparison.OrdinalIgnoreCase)
                    && !sources.ContainsKey(repositoryPath))
                {
                    sources.Add(repositoryPath, File.ReadAllText(fullPath));
                }
            }
        }
    }

    private static void CollectAutomaticBuildDefinitionPaths(
        string repositoryRoot,
        string projectPath,
        ISet<string> automaticDefinitionPaths,
        ICollection<string> errors)
    {
        var userPath = projectPath + ".user";
        if (File.Exists(userPath))
        {
            automaticDefinitionPaths.Add(userPath);
        }

        var current = new DirectoryInfo(Path.GetDirectoryName(projectPath)!);
        var reachedRepositoryRoot = false;
        while (current is not null && IsUnderDirectory(repositoryRoot, current.FullName))
        {
            foreach (var fileName in new[]
                     {
                         "Directory.Build.props",
                         "Directory.Build.targets",
                         "Directory.Packages.props"
                     })
            {
                var candidate = Path.Combine(current.FullName, fileName);
                if (File.Exists(candidate))
                {
                    automaticDefinitionPaths.Add(candidate);
                }
            }

            if (Path.GetRelativePath(repositoryRoot, current.FullName) == ".")
            {
                reachedRepositoryRoot = true;
                break;
            }

            current = current.Parent;
        }

        if (!reachedRepositoryRoot)
        {
            errors.Add(
                "Could not enumerate automatic MSBuild ancestors from project to repository root: "
                + NormalizeRepositoryPath(Path.GetRelativePath(repositoryRoot, projectPath)));
            return;
        }

        current = new DirectoryInfo(repositoryRoot).Parent;
        while (current is not null)
        {
            foreach (var fileName in new[]
                     {
                         "Directory.Build.targets",
                         "Directory.Packages.props"
                     })
            {
                var candidate = Path.Combine(current.FullName, fileName);
                if (File.Exists(candidate))
                {
                    automaticDefinitionPaths.Add(candidate);
                }
            }

            current = current.Parent;
        }
    }

    private static void ValidateAutomaticBuildDefinitions(
        string repositoryRoot,
        IEnumerable<string> definitionPaths,
        ICollection<string> errors)
    {
        foreach (var definitionPath in definitionPaths.Order(StringComparer.OrdinalIgnoreCase))
        {
            var repositoryPath = NormalizeRepositoryPath(
                Path.GetRelativePath(repositoryRoot, definitionPath));
            if (!IsUnderDirectory(repositoryRoot, Path.GetFullPath(definitionPath)))
            {
                errors.Add(
                    "Automatic MSBuild definition is outside the repository: "
                    + definitionPath);
                continue;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(definitionPath, LoadOptions.SetLineInfo);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.Xml.XmlException)
            {
                errors.Add(
                    $"Could not parse automatic MSBuild definition {repositoryPath}: "
                    + exception.Message);
                continue;
            }

            var fileName = Path.GetFileName(definitionPath);
            if (fileName.EndsWith(".csproj.user", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    "Automatic project .csproj.user definition is forbidden without review: "
                    + repositoryPath);
                continue;
            }

            if (fileName.Equals("Directory.Build.targets", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    "Automatic Directory.Build.targets is forbidden without generated-input "
                    + $"review: {repositoryPath}");
                continue;
            }

            if (fileName.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    "Automatic Directory.Packages.props is forbidden because package/source-"
                    + $"generator inputs are exact-allowlisted: {repositoryPath}");
                continue;
            }

            ValidateReviewedDirectoryBuildProps(document, repositoryPath, errors);
        }
    }

    private static void ValidateReviewedDirectoryBuildProps(
        XDocument document,
        string repositoryPath,
        ICollection<string> errors)
    {
        var root = document.Root;
        if (root is null || root.Name.LocalName != "Project" || root.Attributes().Any())
        {
            errors.Add(
                "Repository build props must have an attribute-free Project root: "
                + repositoryPath);
            return;
        }

        var reviewedSecurityPackageItemGroups = 0;
        foreach (var topLevel in root.Elements())
        {
            if (topLevel.Name.LocalName == "PropertyGroup" && !topLevel.Attributes().Any())
            {
                foreach (var property in topLevel.Elements())
                {
                    if (!ReviewedRepositoryBuildProperties.Contains(property.Name.LocalName)
                        || property.Attributes().Any()
                        || property.Elements().Any()
                        || HasMsBuildIndirection(property.Value))
                    {
                        errors.Add(
                            $"Unreviewed repository build property {property.Name.LocalName}: "
                            + repositoryPath);
                    }
                }

                continue;
            }

            if (topLevel.Name.LocalName == "ItemGroup" && !topLevel.Attributes().Any())
            {
                reviewedSecurityPackageItemGroups++;
                ValidateReviewedRepositorySecurityPackage(
                    topLevel,
                    repositoryPath,
                    errors);
                continue;
            }

            errors.Add(
                $"Unreviewed repository build element {topLevel.Name.LocalName}: "
                + repositoryPath);
        }

        if (reviewedSecurityPackageItemGroups != 1)
        {
            errors.Add(
                "Repository build props must contain exactly one reviewed security "
                + $"PackageReference ItemGroup: {repositoryPath}");
        }

        foreach (var element in document.Descendants())
        {
            var localName = element.Name.LocalName;
            if (localName is "Compile" or "Import" or "Target"
                || ForbiddenGeneratedSourceItems.Contains(localName)
                || localName.Contains("Generator", StringComparison.OrdinalIgnoreCase)
                || localName.Contains("Razor", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Repository build definition has unreviewed compile/generator input "
                    + $"{localName}: {repositoryPath}");
            }
        }
    }

    private static void ValidateReviewedRepositorySecurityPackage(
        XElement itemGroup,
        string repositoryPath,
        ICollection<string> errors)
    {
        var items = itemGroup.Elements().ToArray();
        if (items.Length != 1
            || items[0].Name.LocalName != "PackageReference"
            || items[0].Attributes().Count() != 2
            || !string.Equals(
                items[0].Attribute("Include")?.Value,
                ReviewedRepositorySecurityPackage,
                StringComparison.Ordinal)
            || !string.Equals(
                items[0].Attribute("Version")?.Value,
                ReviewedRepositorySecurityPackageVersion,
                StringComparison.Ordinal)
            || items[0].Elements().Any())
        {
            errors.Add(
                "Repository build security ItemGroup must contain only the exact, "
                + "unconditional System.Security.Cryptography.Pkcs 10.0.13 reference: "
                + repositoryPath);
        }
    }

    private static void ValidateGeneratedViewFiles(
        string backendRoot,
        string repositoryRoot,
        ICollection<string> errors)
    {
        foreach (var pattern in new[] { "*.razor", "*.cshtml" })
        {
            foreach (var path in Directory.EnumerateFiles(
                         backendRoot,
                         pattern,
                         SearchOption.AllDirectories))
            {
                var repositoryPath = NormalizeRepositoryPath(
                    Path.GetRelativePath(repositoryRoot, path));
                if (!IsBuildOutput(repositoryPath)
                    && !IsExcludedCounterpartyPath(repositoryPath))
                {
                    errors.Add(
                        "Generated Razor/C# source requires explicit tenant-guard review: "
                        + repositoryPath);
                }
            }
        }
    }

    private static IReadOnlyList<string> ValidateNonVacuousScope(
        BackendSourceInventory inventory)
    {
        var errors = new List<string>();
        if (inventory.ProjectPaths.Count == 0 || inventory.Sources.Count == 0)
        {
            errors.Add("Backend project/source inventory is empty.");
        }

        foreach (var requiredSource in new[]
                 {
                     "src/backend/GoymenERP.Api/Program.cs",
                     "src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/PermissionScope.cs",
                     AllowedSeederPath
                 })
        {
            if (!inventory.Sources.ContainsKey(requiredSource))
            {
                errors.Add($"Required backend scope source was not scanned: {requiredSource}");
            }
        }

        var excluded = inventory.Sources.Keys
            .Where(IsExcludedCounterpartyPath)
            .ToArray();
        if (excluded.Length > 0)
        {
            errors.Add(
                "Counterparty ownership sources entered the platform guard scope:\n"
                + string.Join("\n", excluded.Select(path => $"- {path}")));
        }

        return errors;
    }

    private static IReadOnlyList<BypassOccurrence> FindOccurrences(
        IReadOnlyDictionary<string, string> sources)
    {
        var occurrences = new List<BypassOccurrence>();

        foreach (var (path, source) in sources)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));

            foreach (Match match in IgnoreQueryFiltersIdentifierRegex().Matches(searchableSource))
            {
                occurrences.Add(new BypassOccurrence(
                    normalizedPath,
                    GetLineNumber(searchableSource, match.Index)));
            }
        }

        return occurrences
            .OrderBy(occurrence => occurrence.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(occurrence => occurrence.LineNumber)
            .ToArray();
    }

    private static IReadOnlyList<string> ValidateExactAllowlist(
        IReadOnlyList<BypassOccurrence> occurrences,
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();
        var unauthorized = occurrences
            .Where(occurrence => !string.Equals(
                occurrence.RepositoryPath,
                AllowedSeederPath,
                StringComparison.Ordinal))
            .ToArray();

        if (unauthorized.Length > 0)
        {
            errors.Add(
                "Unauthorized IgnoreQueryFilters calls were found in the platform production scope:\n"
                + string.Join("\n", unauthorized.Select(occurrence => $"- {occurrence}")));
        }

        var allowedCount = occurrences.Count(occurrence => string.Equals(
            occurrence.RepositoryPath,
            AllowedSeederPath,
            StringComparison.Ordinal));

        if (allowedCount != ExpectedSeederBypassCount)
        {
            errors.Add(
                $"Expected exactly {ExpectedSeederBypassCount} IgnoreQueryFilters calls in "
                + $"{AllowedSeederPath}, but found {allowedCount}.");
        }

        var exactSeeder = sources
            .Where(pair => string.Equals(pair.Key, AllowedSeederPath, StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .SingleOrDefault();
        var actualFingerprint = exactSeeder is null
            ? "<missing>"
            : ComputeReviewedSourceFingerprint(exactSeeder);
        if (!string.Equals(
                actualFingerprint,
                ReviewedSeederFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Authorized seeder does not match the reviewed fingerprint. Expected "
                + $"{ReviewedSeederFingerprint}, found {actualFingerprint}: {AllowedSeederPath}");
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateNoForbiddenBypassCapabilities(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();

        foreach (var (path, source) in sources)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            foreach (Match directive in ConditionalCompilationDirectiveRegex().Matches(source))
            {
                errors.Add(
                    "Conditional compilation is forbidden in the platform tenant-guard "
                    + $"source inventory because disabled text can hide reviewed code: "
                    + $"{NormalizeRepositoryPath(path)}:"
                    + GetLineNumber(source, directive.Index));
            }

            if (string.Equals(
                    normalizedPath,
                    OpenApiGenerationModePath,
                    StringComparison.Ordinal))
            {
                var fingerprint = ComputeReviewedSourceFingerprint(source);
                if (!string.Equals(
                        fingerprint,
                        ReviewedOpenApiGenerationModeFingerprint,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        "OpenAPI build-time entry-assembly detection left its exact reviewed "
                        + "source fingerprint. Expected "
                        + $"{ReviewedOpenApiGenerationModeFingerprint}, found {fingerprint}: "
                        + normalizedPath);
                }
                else
                {
                    continue;
                }
            }

            if (string.Equals(
                    normalizedPath,
                    ReviewedSqlServerRcsiToolProgramPath,
                    StringComparison.Ordinal))
            {
                var fingerprint = ComputeReviewedSourceFingerprint(source);
                var reviewedFingerprint = ReviewedRuntimeDatabaseBypassSources[
                    ReviewedSqlServerRcsiToolProgramPath].ReviewedFingerprint;
                if (!string.Equals(
                        fingerprint,
                        reviewedFingerprint,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        "SQL Server RCSI tool reflection surface left its exact reviewed "
                        + $"source fingerprint. Expected {reviewedFingerprint}, found "
                        + $"{fingerprint}: {normalizedPath}");
                }
                else
                {
                    continue;
                }
            }

            if (string.Equals(
                    normalizedPath,
                    ReviewedProductManufacturingTechnicalRegistrationPath,
                    StringComparison.Ordinal))
            {
                var fingerprint = ComputeReviewedSourceFingerprint(source);
                if (!string.Equals(
                        fingerprint,
                        ReviewedProductManufacturingTechnicalRegistrationFingerprint,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        "Product Manufacturing technical registration left its exact reviewed "
                        + "source fingerprint. Expected "
                        + $"{ReviewedProductManufacturingTechnicalRegistrationFingerprint}, found "
                        + $"{fingerprint}: {normalizedPath}");
                }
                else
                {
                    continue;
                }
            }

            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));
            var forbiddenMatches = ForbiddenBypassCapabilityRegex().Matches(searchableSource)
                .Cast<Match>()
                .ToArray();
            var reviewedOriginalValueOffsets = new HashSet<int>();
            if (string.Equals(NormalizeRepositoryPath(path), ReviewedWorkDbContextPath, StringComparison.Ordinal)
                && ReviewedPlatformDbContexts.TryGetValue(ReviewedWorkDbContextPath, out var reviewedWorkContextFingerprint)
                && string.Equals(
                    ComputeReviewedSourceFingerprint(source), reviewedWorkContextFingerprint, StringComparison.Ordinal))
            {
                var originalValueOffsets = Regex.Matches(
                        searchableSource,
                        @"\boriginal\s*\.\s*(?<api>GetValue)\s*<",
                        RegexOptions.CultureInvariant)
                    .Cast<Match>()
                    .Select(match => match.Groups["api"].Index)
                    .ToHashSet();
                var getValueOffsets = forbiddenMatches
                    .Where(match => string.Equals(match.Value, "GetValue", StringComparison.Ordinal))
                    .Select(match => match.Index)
                    .ToArray();
                if (originalValueOffsets.Count == 7
                    && getValueOffsets.Length == 7
                    && originalValueOffsets.SetEquals(getValueOffsets))
                {
                    reviewedOriginalValueOffsets = originalValueOffsets;
                }
            }

            foreach (var match in forbiddenMatches)
            {
                if (reviewedOriginalValueOffsets.Contains(match.Index))
                {
                    continue;
                }

                errors.Add(
                    $"Forbidden reflection/dynamic bypass capability '{match.Value}' at "
                    + $"{NormalizeRepositoryPath(path)}:{GetLineNumber(searchableSource, match.Index)}");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateNoDirectDatabaseBypassCapabilities(
        IReadOnlyDictionary<string, string> sources,
        bool requireReviewedSources = false,
        IReadOnlyDictionary<string, ReviewedRuntimeDatabaseBypassSource>? reviewedSources = null)
    {
        var errors = new List<string>();
        var occurrences = FindDirectDatabaseBypassOccurrences(sources);
        reviewedSources ??= ReviewedRuntimeDatabaseBypassSources;

        foreach (var occurrence in occurrences)
        {
            var isReviewed = reviewedSources.TryGetValue(
                                 occurrence.RepositoryPath,
                                 out var reviewed)
                             && reviewed.ExpectedApiOccurrences.ContainsKey(
                                 occurrence.ApiName);
            if (!isReviewed)
            {
                errors.Add(
                    "Forbidden direct database/query-filter bypass capability "
                    + $"'{occurrence.ApiName}' in "
                    + $"{DescribeSourceCategory(occurrence.SourceCategory)} source at "
                    + $"{occurrence.RepositoryPath}:{occurrence.LineNumber}");
            }
        }

        foreach (var (reviewedPath, reviewed) in reviewedSources)
        {
            if (!sources.TryGetValue(reviewedPath, out var source))
            {
                if (requireReviewedSources)
                {
                    errors.Add(
                        "Reviewed runtime database bypass source is missing: "
                        + reviewedPath);
                }

                continue;
            }

            if (ClassifySource(reviewedPath) != PlatformSourceCategory.RuntimeApplication)
            {
                errors.Add(
                    "Reviewed runtime database bypass source left the runtime application "
                    + $"category: {reviewedPath}");
            }

            var actualFingerprint = ComputeReviewedSourceFingerprint(source);
            if (!string.Equals(
                    actualFingerprint,
                    reviewed.ReviewedFingerprint,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "Reviewed runtime database bypass source does not match the exact "
                    + $"fingerprint. Expected {reviewed.ReviewedFingerprint}, found "
                    + $"{actualFingerprint}: {reviewedPath}");
            }

            var sourceOccurrences = occurrences
                .Where(occurrence => string.Equals(
                    occurrence.RepositoryPath,
                    reviewedPath,
                    StringComparison.Ordinal))
                .ToArray();
            foreach (var (apiName, expectedCount) in reviewed.ExpectedApiOccurrences)
            {
                var actualCount = sourceOccurrences.Count(occurrence => string.Equals(
                    occurrence.ApiName,
                    apiName,
                    StringComparison.Ordinal));
                if (actualCount != expectedCount)
                {
                    errors.Add(
                        $"Expected exactly {expectedCount} reviewed runtime '{apiName}' "
                        + $"occurrence(s), but found {actualCount}: {reviewedPath}");
                }
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateReviewedSqlServerRcsiToolProject(
        string projectSource)
    {
        var errors = new List<string>();
        var fingerprint = ComputeReviewedSourceFingerprint(projectSource);
        if (!string.Equals(
                fingerprint,
                ReviewedSqlServerRcsiToolProjectFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "SQL Server RCSI tool project does not match its exact reviewed fingerprint. "
                + $"Expected {ReviewedSqlServerRcsiToolProjectFingerprint}, found "
                + $"{fingerprint}: {ReviewedSqlServerRcsiToolProjectPath}");
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(projectSource, LoadOptions.SetLineInfo);
        }
        catch (System.Xml.XmlException exception)
        {
            errors.Add(
                $"SQL Server RCSI tool project is not valid XML: {exception.Message}");
            return errors;
        }

        var exactPackages = document.Descendants("PackageReference")
            .Where(element => string.Equals(
                element.Attribute("Include")?.Value,
                "Microsoft.Data.SqlClient",
                StringComparison.Ordinal)
                && string.Equals(
                    element.Attribute("Version")?.Value,
                    "6.1.1",
                    StringComparison.Ordinal))
            .ToArray();
        if (exactPackages.Length != 1)
        {
            errors.Add(
                "SQL Server RCSI tool must retain exactly one exact "
                + "Microsoft.Data.SqlClient package reference at version 6.1.1.");
        }

        var exactProjectReferences = document.Descendants("ProjectReference")
            .Where(element => string.Equals(
                NormalizeRepositoryPath(element.Attribute("Include")?.Value ?? string.Empty),
                "../../src/backend/BuildingBlocks/"
                + "GoymenERP.BuildingBlocks.SqlServer/"
                + "GoymenERP.BuildingBlocks.SqlServer.csproj",
                StringComparison.Ordinal))
            .ToArray();
        if (exactProjectReferences.Length != 1)
        {
            errors.Add(
                "SQL Server RCSI tool must retain exactly one exact shared SQL Server "
                + "project reference.");
        }

        var exactRuntimeIdentifiers = document.Descendants("RuntimeIdentifiers")
            .Where(element => !element.HasAttributes
                              && !element.Elements().Any()
                              && string.Equals(
                                  element.Value.Trim(),
                                  "$(NETCoreSdkRuntimeIdentifier)",
                                  StringComparison.Ordinal))
            .ToArray();
        if (exactRuntimeIdentifiers.Length != 1)
        {
            errors.Add(
                "SQL Server RCSI tool must bind self-contained publication to the "
                + "current reviewed SDK runtime identifier.");
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateReviewedSqlServerRcsiWrapper(
        string wrapperSource)
    {
        var errors = new List<string>();
        var fingerprint = ComputeReviewedSourceFingerprint(wrapperSource);
        if (!string.Equals(
                fingerprint,
                ReviewedSqlServerRcsiWrapperFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "SQL Server RCSI wrapper does not match its exact reviewed fingerprint. "
                + $"Expected {ReviewedSqlServerRcsiWrapperFingerprint}, found {fingerprint}: "
                + ReviewedSqlServerRcsiWrapperPath);
        }

        foreach (var required in new[]
                 {
                     "#requires -Version 7.2",
                     "[Parameter(Mandatory)][string]$ExpectedWrapperScriptSha256",
                      "Get-VerifiedWrapperScriptIdentity",
                      "$finalWrapperScript = Get-VerifiedWrapperScriptIdentity",
                      "if ($evidenceValidated -and $null -ne $validatedStdoutBytes)",
                      "'--expected-wrapper-script-sha256'",
                     "'--observed-wrapper-script-sha256'",
                     "'expectedWrapperScriptSha256'",
                     "'observedWrapperScriptSha256'",
                     "[Diagnostics.ProcessStartInfo]::new($verifiedPayload.EntryPath)",
                     "$startInfo.Environment.Clear()",
                     "$startInfo.RedirectStandardInput = $true",
                     "'--connection-secret-stdin', 'true'",
                     "[GoymenErpRcsiProcessContainment]::CreateWindowsJob()",
                     "[GoymenErpRcsiProcessContainment]::KillUnixProcessGroup",
                     "$overallDeadlineStopwatch",
                     "[Array]::Clear($probeSecretBytes",
                     "-ccontains",
                     "$process.Kill($true)",
                     "exit $scriptExitCode",
                 })
        {
            if (!wrapperSource.Contains(required, StringComparison.Ordinal))
            {
                errors.Add($"SQL Server RCSI wrapper lost required fail-closed surface: {required}");
            }
        }

        foreach (var (pattern, description) in new[]
                 {
                     (@"(?i)\bALTER\s+DATABASE\b", "ALTER DATABASE"),
                     (@"(?i)\bInvoke-Expression\b", "Invoke-Expression"),
                     ("(?i)ProcessStartInfo\\]\\s*::\\s*new\\s*\\(\\s*['\"]dotnet['\"]", "PATH dotnet host"),
                     (@"(?i)\b(?:Password|Pwd|User\s+ID|Server|Data\s+Source)\s*=", "plaintext connection value"),
                 })
        {
            if (Regex.IsMatch(wrapperSource, pattern, RegexOptions.CultureInvariant))
            {
                errors.Add($"SQL Server RCSI wrapper contains forbidden {description} surface.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateSqlServerRcsiPublishAndDiscoveryContracts(
        string solutionSource,
        string integrationProjectSource,
        string relationalAcceptanceSource,
        string workflowSource)
    {
        var errors = new List<string>();
        var workflowFingerprint = ComputeReviewedSourceFingerprint(workflowSource);
        if (!string.Equals(
                workflowFingerprint,
                ReviewedContinuousIntegrationWorkflowFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "RCSI CI workflow left its exact reviewed fingerprint. Expected "
                + $"{ReviewedContinuousIntegrationWorkflowFingerprint}, found "
                + $"{workflowFingerprint}: {ContinuousIntegrationWorkflowPath}");
        }

        XDocument solution;
        XDocument project;
        try
        {
            solution = XDocument.Parse(solutionSource, LoadOptions.SetLineInfo);
            project = XDocument.Parse(integrationProjectSource, LoadOptions.SetLineInfo);
        }
        catch (System.Xml.XmlException exception)
        {
            return [$"RCSI publish/discovery XML is invalid: {exception.Message}"];
        }

        var integrationProjectFingerprint =
            ComputeReviewedSourceFingerprint(integrationProjectSource);
        if (!string.Equals(
                integrationProjectFingerprint,
                ReviewedSqlServerRcsiIntegrationProjectFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "RCSI integration publish project left its exact reviewed fingerprint. "
                + $"Expected {ReviewedSqlServerRcsiIntegrationProjectFingerprint}, found "
                + $"{integrationProjectFingerprint}: {SqlServerRcsiIntegrationProjectPath}");
        }

        foreach (var requiredProject in new[]
                 {
                     "tools/GoymenERP.SqlServerRcsiReadiness/"
                     + "GoymenERP.SqlServerRcsiReadiness.csproj",
                     "tests/GoymenERP.SqlServerRcsiReadiness.Tests/"
                     + "GoymenERP.SqlServerRcsiReadiness.Tests.csproj",
                     SqlServerRcsiIntegrationProjectPath,
                 })
        {
            var matches = solution.Descendants("Project")
                .Count(element => string.Equals(
                    NormalizeRepositoryPath(element.Attribute("Path")?.Value ?? string.Empty),
                    requiredProject,
                    StringComparison.Ordinal));
            if (matches != 1)
            {
                errors.Add(
                    "RCSI solution membership must contain exactly one reviewed project: "
                    + requiredProject);
            }
        }

        var publishDirectoryProperties = project.Descendants("RcsiToolSelfContainedPublishDir")
            .Where(element => !element.HasAttributes
                              && !element.Elements().Any()
                              && string.Equals(
                element.Value.Trim(),
                "$(MSBuildProjectDirectory)\\bin\\$(Configuration)\\"
                + "rcsi-tool-self-contained\\$(NETCoreSdkRuntimeIdentifier)\\",
                StringComparison.Ordinal))
            .ToArray();
        if (publishDirectoryProperties.Length != 1)
        {
            errors.Add(
                "RCSI self-contained publish directory must remain runtime-specific and reviewed.");
        }

        var toolProjectProperties = project.Descendants("RcsiToolProject")
            .Where(element => !element.HasAttributes
                              && !element.Elements().Any()
                              && string.Equals(
                                  element.Value.Trim(),
                                  "$(MSBuildProjectDirectory)\\..\\..\\tools\\"
                                  + "GoymenERP.SqlServerRcsiReadiness\\"
                                  + "GoymenERP.SqlServerRcsiReadiness.csproj",
                                  StringComparison.Ordinal))
            .ToArray();
        if (toolProjectProperties.Length != 1)
        {
            errors.Add("RCSI publish caller must retain the exact reviewed tool project path.");
        }

        var publishTargets = project.Descendants("Target")
            .Where(element => string.Equals(
                element.Attribute("Name")?.Value,
                "PublishSelfContainedSqlServerRcsiReadinessTool",
                StringComparison.Ordinal))
            .ToArray();
        if (publishTargets.Length != 1)
        {
            errors.Add("RCSI integration project must retain exactly one reviewed publish target.");
        }
        else
        {
            var target = publishTargets[0];
            var tasks = target.Elements().ToArray();
            if (target.Attributes().Count() != 3
                || !string.Equals(
                    target.Attribute("AfterTargets")?.Value,
                    "Build",
                    StringComparison.Ordinal)
                || !string.Equals(
                    target.Attribute("Condition")?.Value,
                    "'$(DesignTimeBuild)' != 'true' and "
                    + "'$(IsCrossTargetingBuild)' != 'true'",
                    StringComparison.Ordinal))
            {
                errors.Add("RCSI self-contained publish target scheduling drifted.");
            }

            if (tasks.Length != 2
                || !string.Equals(tasks[0].Name.LocalName, "RemoveDir", StringComparison.Ordinal)
                || tasks[0].Attributes().Count() != 1
                || !string.Equals(
                    tasks[0].Attribute("Directories")?.Value,
                    "$(RcsiToolSelfContainedPublishDir)",
                    StringComparison.Ordinal))
            {
                errors.Add("RCSI publish directory must be cleaned before publication.");
            }

            var publishTask = tasks.Length == 2
                && string.Equals(tasks[1].Name.LocalName, "MSBuild", StringComparison.Ordinal)
                    ? tasks[1]
                    : null;
            const string reviewedPublishProperties =
                "Configuration=$(Configuration);"
                + "RuntimeIdentifier=$(NETCoreSdkRuntimeIdentifier);"
                + "SelfContained=true;PublishSingleFile=false;PublishTrimmed=false;"
                + "PublishDir=$(RcsiToolSelfContainedPublishDir);NoRestore=true";
            if (publishTask is null
                || publishTask.Attributes().Count() != 4
                || !string.Equals(
                    publishTask.Attribute("Projects")?.Value,
                    "$(RcsiToolProject)",
                    StringComparison.Ordinal)
                || !string.Equals(
                    publishTask.Attribute("Targets")?.Value,
                    "Publish",
                    StringComparison.Ordinal)
                || !string.Equals(
                    publishTask.Attribute("BuildInParallel")?.Value,
                    "false",
                    StringComparison.Ordinal)
                || !string.Equals(
                    publishTask.Attribute("Properties")?.Value,
                    reviewedPublishProperties,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "RCSI publish caller must retain exact RuntimeIdentifier and "
                    + "SelfContained=true semantics.");
            }
        }

        if (Regex.IsMatch(
                relationalAcceptanceSource,
                @"(?m)^\s*#\s*(?:if|elif|else|endif)\b",
                RegexOptions.CultureInvariant)
            || Regex.Matches(
                relationalAcceptanceSource,
                @"\bpublic\s+sealed\s+class\s+"
                + "SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests\\b",
                RegexOptions.CultureInvariant).Count != 1
            || Regex.Matches(
                relationalAcceptanceSource,
                Regex.Escape("[SqlServerFact]"),
                RegexOptions.CultureInvariant).Count != 14)
        {
            errors.Add(
                "RCSI relational discovery must retain the reviewed class name and fourteen tests.");
        }

        if (Regex.Matches(
                relationalAcceptanceSource,
                @"\bpublic\s+sealed\s+class\s+"
                + "SqlServerRcsiReadinessProcessAcceptanceTests\\b",
                RegexOptions.CultureInvariant).Count != 1
            || Regex.Matches(
                relationalAcceptanceSource,
                Regex.Escape("[Fact]"),
                RegexOptions.CultureInvariant).Count != 3)
        {
            errors.Add(
                "RCSI process acceptance discovery must retain the reviewed "
                + "non-SQL class and three tests.");
        }

        var normalizedWorkflow = workflowSource.Replace("\r\n", "\n", StringComparison.Ordinal);
        var backendBuildStart = normalizedWorkflow.IndexOf("\n  backend-build:\n", StringComparison.Ordinal);
        var backendWorkerStart = normalizedWorkflow.IndexOf("\n  backend-test-group:\n", StringComparison.Ordinal);
        var backendAggregateStart = normalizedWorkflow.IndexOf("\n  backend:\n", StringComparison.Ordinal);
        var frontendStart = normalizedWorkflow.IndexOf("\n  frontend:\n", StringComparison.Ordinal);
        var backendBuildJob = backendBuildStart >= 0 && backendWorkerStart > backendBuildStart
            ? normalizedWorkflow[backendBuildStart..backendWorkerStart]
            : string.Empty;
        var backendWorkerJob = backendWorkerStart >= 0 && backendAggregateStart > backendWorkerStart
            ? normalizedWorkflow[backendWorkerStart..backendAggregateStart]
            : string.Empty;
        var backendAggregateJob = backendAggregateStart >= 0 && frontendStart > backendAggregateStart
            ? normalizedWorkflow[backendAggregateStart..frontendStart]
            : string.Empty;
        var backendTestStepStart = backendWorkerJob.IndexOf(
            "\n      - name: Run exact backend test group\n",
            StringComparison.Ordinal);
        var backendTestStep = ExtractYamlStep(backendWorkerJob, backendTestStepStart);
        var activeBackendTestLines = ReadActiveYamlLines(backendTestStep);
        var expectedBackendTestLines = new[]
        {
            "- name: Run exact backend test group",
            "run: |",
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py",
            "python3 -I -S -B tools/verify_backend_test_groups.py \\",
            "--group \"${{ matrix.group }}\" ${{ matrix.shard_args }} \\",
            "--results-directory \"artifacts/ci-test-results/backend-groups/${{ matrix.unit }}\" \\",
            "--proof-output \"artifacts/ci-backend-group-proofs/proof-${{ matrix.unit }}.json\" \\",
            "--source-head \"${{ github.event.pull_request.head.sha || github.sha }}\" \\",
            "--tested-commit \"$(git rev-parse HEAD)\"",
        };
        if (backendTestStepStart < 0
            || !activeBackendTestLines.SequenceEqual(
                expectedBackendTestLines,
                StringComparer.Ordinal))
        {
            errors.Add(
                "RCSI CLI/policy tests require the exact unconditional backend test group step.");
        }
        if (!backendBuildJob.Contains("dotnet build GoymenERP.slnx -noAutoResponse", StringComparison.Ordinal)
            || !backendBuildJob.Contains("Architecture dependency guard", StringComparison.Ordinal)
            || !backendBuildJob.Contains("Check vulnerable dependencies", StringComparison.Ordinal)
            || !backendAggregateJob.Contains("needs: [backend-build, backend-test-group, relational-shard, relational-rcsi]", StringComparison.Ordinal)
            || !backendAggregateJob.Contains("if: ${{ !cancelled() }}", StringComparison.Ordinal)
            || !backendAggregateJob.Contains("Download same-run SQL proofs for excluded backend tests", StringComparison.Ordinal)
            || !backendAggregateJob.Contains("--relational-proof-directory artifacts/ci-relational-proofs", StringComparison.Ordinal)
            || !backendAggregateJob.Contains("--relational-worker-result \"${{ needs.relational-shard.result }}\"", StringComparison.Ordinal)
            || !backendAggregateJob.Contains("--rcsi-result \"${{ needs.relational-rcsi.result }}\"", StringComparison.Ordinal)
            || !backendAggregateJob.Contains("Verify complete backend build and test groups", StringComparison.Ordinal))
        {
            errors.Add("RCSI CLI/policy tests require the reviewed fail-closed backend build and aggregate jobs.");
        }

        var shardJobStart = normalizedWorkflow.IndexOf("\n  relational-shard:\n", StringComparison.Ordinal);
        var rcsiJobStart = normalizedWorkflow.IndexOf("\n  relational-rcsi:\n", StringComparison.Ordinal);
        var relationalJobStart = normalizedWorkflow.IndexOf("\n  relational:\n", StringComparison.Ordinal);
        var contractJobStart = normalizedWorkflow.IndexOf("\n  contract-drift:\n", StringComparison.Ordinal);
        var shardJob = shardJobStart >= 0 && rcsiJobStart > shardJobStart
            ? normalizedWorkflow[shardJobStart..rcsiJobStart]
            : string.Empty;
        var rcsiJob = rcsiJobStart >= 0 && relationalJobStart > rcsiJobStart
            ? normalizedWorkflow[rcsiJobStart..relationalJobStart]
            : string.Empty;
        var relationalJob = relationalJobStart >= 0 && contractJobStart > relationalJobStart
            ? normalizedWorkflow[relationalJobStart..contractJobStart]
            : string.Empty;
        var shardLines = ReadActiveYamlLines(shardJob);
        var rcsiLines = ReadActiveYamlLines(rcsiJob);
        var aggregateLines = ReadActiveYamlLines(relationalJob);
        var shardStep = ExtractYamlStep(shardJob, shardJob.IndexOf(
            "\n      - name: Run exact SQL Server relational shard\n", StringComparison.Ordinal));
        var rcsiStep = ExtractYamlStep(rcsiJob, rcsiJob.IndexOf(
            "\n      - name: Run exact RCSI relational acceptance verifier\n", StringComparison.Ordinal));
        var aggregateStep = ExtractYamlStep(relationalJob, relationalJob.IndexOf(
            "\n      - name: Verify complete SQL Server relational acceptance\n", StringComparison.Ordinal));
        var proofUploadStep = ExtractYamlStep(shardJob, shardJob.IndexOf(
            "\n      - name: Upload exact SQL shard proof\n", StringComparison.Ordinal));
        var proofDownloadStep = ExtractYamlStep(relationalJob, relationalJob.IndexOf(
            "\n      - name: Download exact SQL shard proofs\n", StringComparison.Ordinal));
        var expectedShardStepLines = new[]
        {
            "- name: Run exact SQL Server relational shard",
            "run: |",
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py",
            "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py \\",
            "--shard \"${{ matrix.shard }}\" \\",
            "--trx-output \"artifacts/ci-test-results/relational/broad-${{ matrix.shard }}.trx\" \\",
            "--proof-output \"artifacts/ci-relational-proofs/proof-${{ matrix.shard }}.json\" \\",
            "--source-head \"${{ github.event.pull_request.head.sha || github.sha }}\" \\",
            "--tested-commit \"$(git rev-parse HEAD)\"",
        };
        var expectedRcsiStepLines = new[]
        {
            "- name: Run exact RCSI relational acceptance verifier",
            "run: |",
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py",
            "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py \\",
            "--rcsi \\",
            "--trx-output artifacts/ci-test-results/relational/rcsi.trx",
        };
        var expectedAggregateStepLines = new[]
        {
            "- name: Verify complete SQL Server relational acceptance",
            "run: |",
            "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py \\",
            "--aggregate \\",
            "--proof-directory artifacts/ci-relational-proofs \\",
            "--source-head \"${{ github.event.pull_request.head.sha || github.sha }}\" \\",
            "--tested-commit \"$(git rev-parse HEAD)\" \\",
            "--worker-result \"${{ needs.relational-shard.result }}\" \\",
            "--rcsi-result \"${{ needs.relational-rcsi.result }}\"",
        };
        var expectedProofUploadStepLines = new[]
        {
            "- name: Upload exact SQL shard proof",
            "uses: actions/upload-artifact@v7",
            "with:",
            "name: ci-relational-shard-proof-${{ matrix.shard }}",
            "path: artifacts/ci-relational-proofs/proof-${{ matrix.shard }}.json",
            "if-no-files-found: error",
            "retention-days: 7",
        };
        var expectedProofDownloadStepLines = new[]
        {
            "- name: Download exact SQL shard proofs",
            "uses: actions/download-artifact@v8",
            "with:",
            "pattern: ci-relational-shard-proof-*",
            "path: artifacts/ci-relational-proofs",
        };
        if (shardJob.Length == 0 || rcsiJob.Length == 0 || relationalJob.Length == 0
            || !shardLines.Contains("name: SQL Server relational shard (${{ matrix.shard }})",
                StringComparer.Ordinal)
            || !shardLines.Contains("fail-fast: false", StringComparer.Ordinal)
            || !shardLines.Contains("shard: [A, B, C]", StringComparer.Ordinal)
            || !rcsiLines.Contains("name: Exact RCSI relational acceptance", StringComparer.Ordinal)
            || !aggregateLines.Contains("name: SQL Server relational acceptance", StringComparer.Ordinal)
            || !aggregateLines.Contains("needs: [relational-shard, relational-rcsi]",
                StringComparer.Ordinal)
            || !aggregateLines.Contains("if: ${{ !cancelled() }}", StringComparer.Ordinal)
            || !ReadActiveYamlLines(shardStep).SequenceEqual(
                expectedShardStepLines, StringComparer.Ordinal)
            || !ReadActiveYamlLines(rcsiStep).SequenceEqual(
                expectedRcsiStepLines, StringComparer.Ordinal)
            || !ReadActiveYamlLines(aggregateStep).SequenceEqual(
                expectedAggregateStepLines, StringComparer.Ordinal)
            || !ReadActiveYamlLines(proofUploadStep).SequenceEqual(
                expectedProofUploadStepLines, StringComparer.Ordinal)
            || !ReadActiveYamlLines(proofDownloadStep).SequenceEqual(
                expectedProofDownloadStepLines, StringComparer.Ordinal)
            || ContainsConditionalOrSoftFailure(ReadActiveYamlLines(shardStep))
            || ContainsConditionalOrSoftFailure(ReadActiveYamlLines(rcsiStep))
            || ContainsConditionalOrSoftFailure(ReadActiveYamlLines(aggregateStep))
            || aggregateLines.Count(line => line.StartsWith("if:", StringComparison.Ordinal)) != 1
            || aggregateLines.Any(line => line.StartsWith("continue-on-error:", StringComparison.Ordinal)))
        {
            errors.Add("RCSI relational CI verifier shard and aggregate gates must remain exact and non-vacuous.");
        }

        var expectedSqlEnvironmentPrefix =
            "\n    env:\n      GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: >-\n        Server=";
        foreach (var job in new[] { shardJob, rcsiJob })
        {
            var activeLines = ReadActiveYamlLines(job);
            var sqlEnvironmentStart = job.IndexOf(expectedSqlEnvironmentPrefix, StringComparison.Ordinal);
            var sqlEnvironmentLine = sqlEnvironmentStart < 0
                ? string.Empty
                : job[(sqlEnvironmentStart + expectedSqlEnvironmentPrefix.Length -
                    "Server=".Length)..].Split('\n')[0].Trim();
            if (sqlEnvironmentStart < 0
                || !sqlEnvironmentLine.StartsWith("Server=", StringComparison.Ordinal)
                || !sqlEnvironmentLine.Contains("Database=master", StringComparison.Ordinal)
                || !sqlEnvironmentLine.Contains("Password=", StringComparison.Ordinal)
                || !activeLines.Contains("services:", StringComparer.Ordinal)
                || !activeLines.Contains("sqlserver:", StringComparer.Ordinal)
                || !activeLines.Contains("dotnet restore tests/GoymenERP.Api.IntegrationTests/"
                    + "GoymenERP.Api.IntegrationTests.csproj \\", StringComparer.Ordinal)
                || ContainsConditionalOrSoftFailure(ReadActiveYamlLines(job.Split("\n    steps:\n")[0])))
            {
                errors.Add("RCSI relational SQL Server fixture binding must retain isolated, unconditional SQL workers.");
            }
        }

        return errors;
    }

    private static string ExtractYamlStep(string jobSource, int stepStart)
    {
        if (stepStart < 0)
        {
            return string.Empty;
        }

        var step = jobSource[stepStart..];
        var nextStep = step.IndexOf("\n      - name:", 2, StringComparison.Ordinal);
        return nextStep > 0 ? step[..nextStep] : step;
    }

    private static string[] ReadActiveYamlLines(string source) =>
        source.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

    private static bool ContainsConditionalOrSoftFailure(IEnumerable<string> lines) =>
        lines.Any(line => line.StartsWith("if:", StringComparison.Ordinal)
                          || line.StartsWith("continue-on-error:", StringComparison.Ordinal));

    private static BackendSourceInventory
        ReadSqlServerRcsiToolSourceInventory(string repositoryRoot)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var errors = new List<string>();
        var toolRoot = Path.Combine(
            repositoryRoot,
            ReviewedSqlServerRcsiToolRootPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        if (!Directory.Exists(toolRoot))
        {
            return new BackendSourceInventory(sources, [], ["RCSI tool root is missing."]);
        }

        foreach (var path in Directory.EnumerateFiles(
                     toolRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            AddPhysicalSource(repositoryRoot, path, sources, errors);
        }

        ValidateNoUnsupportedBackendInputs(repositoryRoot, toolRoot, errors);
        var projectPath = Path.Combine(
            repositoryRoot,
            ReviewedSqlServerRcsiToolProjectPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        var automaticDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath, LoadOptions.SetLineInfo);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Xml.XmlException)
        {
            errors.Add($"Could not parse RCSI tool project: {exception.Message}");
            return new BackendSourceInventory(sources, [], errors);
        }

        CollectAutomaticBuildDefinitionPaths(
            repositoryRoot,
            projectPath,
            automaticDefinitions,
            errors);
        var analysis = AnalyzeReviewedSqlServerRcsiToolProjectDefinition(document);
        errors.AddRange(analysis.Errors);
        AddExplicitCompileIncludes(
            repositoryRoot,
            projectPath,
            analysis.CompileDirectives,
            sources,
            errors);
        ValidateAutomaticBuildDefinitions(repositoryRoot, automaticDefinitions, errors);
        ValidateGeneratedViewFiles(toolRoot, repositoryRoot, errors);

        foreach (var requiredSource in new[]
                 {
                     ReviewedSqlServerRcsiToolProgramPath,
                     ReviewedSqlServerRcsiToolRootPath + "/AssemblyInfo.cs",
                 })
        {
            if (!sources.ContainsKey(requiredSource))
            {
                errors.Add($"Required RCSI tool compile source was not scanned: {requiredSource}");
            }
        }

        return new BackendSourceInventory(
            sources,
            [ReviewedSqlServerRcsiToolProjectPath],
            errors);
    }

    private static IReadOnlyList<string> ValidateSqlServerRcsiToolMutationSurface(
        IReadOnlyDictionary<string, string> toolSources)
    {
        var errors = new List<string>();
        foreach (var (path, source) in toolSources)
        {
            if (Regex.IsMatch(
                    source,
                    @"(?<![\p{L}\p{Nd}_])ALTER\s+DATABASE(?![\p{L}\p{Nd}_])",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                errors.Add(
                    "SQL Server RCSI tool must not contain an ALTER DATABASE mutation "
                    + $"surface: {path}");
            }
        }

        return errors;
    }

    private static ProjectDefinitionAnalysis
        AnalyzeReviewedSqlServerRcsiToolProjectDefinition(XDocument document)
    {
        var reviewedDocument = new XDocument(document);
        var exactOutputType = reviewedDocument.Descendants()
            .Where(element => element.Name.LocalName == "OutputType")
            .Where(element => !element.HasAttributes
                              && !element.Elements().Any()
                              && string.Equals(
                                  element.Value.Trim(),
                                  "Exe",
                                  StringComparison.Ordinal))
            .ToArray();
        if (exactOutputType.Length == 1)
        {
            exactOutputType[0].Remove();
        }

        var exactRuntimeIdentifiers = reviewedDocument.Descendants()
            .Where(element => element.Name.LocalName == "RuntimeIdentifiers")
            .Where(element => !element.HasAttributes
                              && !element.Elements().Any()
                              && string.Equals(
                                  element.Value.Trim(),
                                  "$(NETCoreSdkRuntimeIdentifier)",
                                  StringComparison.Ordinal))
            .ToArray();
        if (exactRuntimeIdentifiers.Length == 1)
        {
            exactRuntimeIdentifiers[0].Remove();
        }

        var exactSqlClientPackage = reviewedDocument.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Where(element => element.Attributes().Count() == 2
                              && !element.Elements().Any()
                              && string.Equals(
                                  element.Attribute("Include")?.Value,
                                  "Microsoft.Data.SqlClient",
                                  StringComparison.Ordinal)
                              && string.Equals(
                                  element.Attribute("Version")?.Value,
                                  "6.1.1",
                                  StringComparison.Ordinal))
            .ToArray();
        if (exactSqlClientPackage.Length == 1)
        {
            exactSqlClientPackage[0].Remove();
        }

        var analysis = AnalyzeProjectDefinition(
            reviewedDocument,
            ReviewedSqlServerRcsiToolProjectPath);
        var errors = analysis.Errors.ToList();
        foreach (var directive in analysis.CompileDirectives)
        {
            if (directive.Kind is CompileDirectiveKind.Remove or CompileDirectiveKind.Update)
            {
                errors.Add(
                    $"RCSI tool Compile {directive.Kind} is forbidden because the exact "
                    + "compile inventory must remain additive and reviewable.");
            }

            if (directive.Excludes.Count != 0)
            {
                errors.Add(
                    "RCSI tool Compile Exclude is forbidden because it can hide reviewed "
                    + "compile inputs.");
            }
        }

        return new ProjectDefinitionAnalysis(analysis.CompileDirectives, errors);
    }

    private static IReadOnlyList<DirectDatabaseBypassOccurrence>
        FindDirectDatabaseBypassOccurrences(IReadOnlyDictionary<string, string> sources)
    {
        var occurrences = new List<DirectDatabaseBypassOccurrence>();
        foreach (var (path, source) in sources)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));
            foreach (Match match in DirectDatabaseBypassCapabilityRegex().Matches(
                         searchableSource))
            {
                occurrences.Add(new DirectDatabaseBypassOccurrence(
                    normalizedPath,
                    match.Value,
                    GetLineNumber(searchableSource, match.Index),
                    ClassifySource(normalizedPath)));
            }
        }

        return occurrences
            .OrderBy(occurrence => occurrence.RepositoryPath, StringComparer.Ordinal)
            .ThenBy(occurrence => occurrence.LineNumber)
            .ThenBy(occurrence => occurrence.ApiName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ValidateReviewedMigrationMutationSurface(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();
        var occurrences = FindMigrationMutationOccurrences(sources);

        foreach (var occurrence in occurrences)
        {
            if (occurrence.SourceCategory != PlatformSourceCategory.Migration)
            {
                errors.Add(
                    $"Migration mutation API '{occurrence.ApiName}' is forbidden outside "
                    + $"migration source: {occurrence.RepositoryPath}:{occurrence.LineNumber}");
                continue;
            }

            if (!ReviewedMigrationMutationSources.TryGetValue(
                    occurrence.RepositoryPath,
                    out var reviewed)
                || !reviewed.ExpectedApiOccurrences.ContainsKey(occurrence.ApiName))
            {
                errors.Add(
                    $"Unreviewed migration mutation API '{occurrence.ApiName}' at "
                    + $"{occurrence.RepositoryPath}:{occurrence.LineNumber}");
            }
        }

        foreach (var (reviewedPath, reviewed) in ReviewedMigrationMutationSources)
        {
            if (!sources.TryGetValue(reviewedPath, out var source))
            {
                errors.Add($"Reviewed migration mutation source is missing: {reviewedPath}");
                continue;
            }

            if (ClassifySource(reviewedPath) != PlatformSourceCategory.Migration)
            {
                errors.Add(
                    "Reviewed migration mutation source unexpectedly left the migration "
                    + $"category: {reviewedPath}");
            }

            var actualFingerprint = ComputeReviewedSourceFingerprint(source);
            if (!string.Equals(
                    actualFingerprint,
                    reviewed.ReviewedFingerprint,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "Migration mutation source does not match the reviewed fingerprint. "
                    + $"Expected {reviewed.ReviewedFingerprint}, found {actualFingerprint}: "
                    + reviewedPath);
            }

            var sourceOccurrences = occurrences
                .Where(occurrence => string.Equals(
                    occurrence.RepositoryPath,
                    reviewedPath,
                    StringComparison.Ordinal))
                .ToArray();
            foreach (var (apiName, expectedCount) in reviewed.ExpectedApiOccurrences)
            {
                var actualCount = sourceOccurrences.Count(occurrence => string.Equals(
                    occurrence.ApiName,
                    apiName,
                    StringComparison.Ordinal));
                if (actualCount != expectedCount)
                {
                    errors.Add(
                        $"Expected exactly {expectedCount} reviewed migration '{apiName}' "
                        + $"occurrence(s), but found {actualCount}: {reviewedPath}");
                }
            }
        }

        return errors;
    }

    private static IReadOnlyList<MigrationMutationOccurrence>
        FindMigrationMutationOccurrences(IReadOnlyDictionary<string, string> sources)
    {
        var occurrences = new List<MigrationMutationOccurrence>();

        foreach (var (path, source) in sources)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));
            foreach (Match match in MigrationMutationCapabilityRegex().Matches(
                         searchableSource))
            {
                occurrences.Add(new MigrationMutationOccurrence(
                    normalizedPath,
                    match.Groups["api"].Value,
                    GetLineNumber(searchableSource, match.Index),
                    ClassifySource(normalizedPath)));
            }
        }

        return occurrences
            .OrderBy(occurrence => occurrence.RepositoryPath, StringComparer.Ordinal)
            .ThenBy(occurrence => occurrence.LineNumber)
            .ThenBy(occurrence => occurrence.ApiName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ValidateReviewedPlatformDbContextInventory(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();
        var declarations = new List<DbContextDeclaration>();

        foreach (var (path, source) in sources)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));

            foreach (Match alias in DbContextBaseTypeAliasRegex().Matches(searchableSource))
            {
                errors.Add(
                    $"DbContext base-type alias '{alias.Groups["alias"].Value}' is forbidden "
                    + "because it can hide an unreviewed filterless context at "
                    + $"{normalizedPath}:{GetLineNumber(searchableSource, alias.Index)}");
            }

            foreach (Match declaration in DbContextDeclarationRegex().Matches(
                         searchableSource))
            {
                declarations.Add(new DbContextDeclaration(
                    normalizedPath,
                    declaration.Groups["name"].Value.TrimStart('@'),
                    GetLineNumber(searchableSource, declaration.Index),
                    ClassifySource(normalizedPath)));
            }
        }

        foreach (var declaration in declarations.Where(
                     declaration => !ReviewedPlatformDbContexts.ContainsKey(
                         declaration.RepositoryPath)))
        {
            errors.Add(
                $"Unreviewed platform DbContext declaration '{declaration.TypeName}' in "
                + $"{DescribeSourceCategory(declaration.SourceCategory)} source at "
                + $"{declaration.RepositoryPath}:{declaration.LineNumber}");
        }

        foreach (var (reviewedPath, reviewedFingerprint) in ReviewedPlatformDbContexts)
        {
            if (!sources.TryGetValue(reviewedPath, out var source))
            {
                errors.Add($"Reviewed platform DbContext source is missing: {reviewedPath}");
                continue;
            }

            var expectedTypeName = Path.GetFileNameWithoutExtension(reviewedPath);
            var pathDeclarations = declarations
                .Where(declaration => string.Equals(
                    declaration.RepositoryPath,
                    reviewedPath,
                    StringComparison.Ordinal))
                .ToArray();

            if (pathDeclarations.Length != 1
                || !string.Equals(
                    pathDeclarations[0].TypeName,
                    expectedTypeName,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"Expected exactly one reviewed DbContext declaration '{expectedTypeName}' "
                    + $"at {reviewedPath}, but found "
                    + (pathDeclarations.Length == 0
                        ? "none."
                        : string.Join(
                            ", ",
                            pathDeclarations.Select(declaration => declaration.TypeName))
                          + "."));
            }

            if (ClassifySource(reviewedPath) != PlatformSourceCategory.RuntimeApplication)
            {
                errors.Add(
                    "Reviewed platform DbContext unexpectedly left the runtime application "
                    + $"source category: {reviewedPath}");
            }

            var actualFingerprint = ComputeReviewedSourceFingerprint(source);
            if (!string.Equals(
                    actualFingerprint,
                    reviewedFingerprint,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"Platform DbContext does not match the reviewed fingerprint. Expected "
                    + $"{reviewedFingerprint}, found {actualFingerprint}: {reviewedPath}");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateReviewedPlatformContextConstructions(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();
        var constructions = new List<ReviewedContextConstruction>();

        foreach (var (path, source) in sources)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));

            foreach (Match factoryReference in ReviewedDesignTimeFactoryIdentifierRegex()
                         .Matches(searchableSource))
            {
                var isInsideReviewedFactory =
                    ReviewedPlatformContextFactories.ContainsKey(normalizedPath);
                if (!isInsideReviewedFactory)
                {
                    errors.Add(
                        "Unauthorized design-time DbContext factory reference "
                        + $"'{factoryReference.Value}' at "
                        + $"{normalizedPath}:"
                        + GetLineNumber(searchableSource, factoryReference.Index));
                }
            }

            foreach (Match alias in ReviewedContextTypeAliasRegex().Matches(searchableSource))
            {
                errors.Add(
                    $"Reviewed platform DbContext type alias '{alias.Groups["alias"].Value}' "
                    + $"for '{alias.Groups["type"].Value}' is forbidden because it can hide "
                    + "an unauthorized construction at "
                    + $"{normalizedPath}:{GetLineNumber(searchableSource, alias.Index)}");
            }

            foreach (Match construction in ReviewedContextConstructionRegex().Matches(
                         searchableSource))
            {
                constructions.Add(new ReviewedContextConstruction(
                    normalizedPath,
                    construction.Groups["type"].Value,
                    GetLineNumber(searchableSource, construction.Index)));
            }

            // The existing company-context construction prohibition is unchanged.
            // Only these exact, fingerprinted design-time factories gain this additional
            // construction counter; this is not a runtime construction exemption.
            if (ReviewedPlatformContextFactories.TryGetValue(normalizedPath, out var additionalFactory)
                && additionalFactory.ContextTypeName is "GroupSharedCommandDbContext" or "ProductDbContext" or "ProductPriceListDbContext")
            {
                foreach (Match construction in Regex.Matches(searchableSource,
                             @"\bnew\s+" + Regex.Escape(additionalFactory.ContextTypeName) + @"\s*\(",
                             RegexOptions.CultureInvariant))
                    constructions.Add(new ReviewedContextConstruction(normalizedPath, additionalFactory.ContextTypeName,
                        GetLineNumber(searchableSource, construction.Index)));
            }

            if (!ReviewedPlatformContextFactories.ContainsKey(normalizedPath)
                && !string.Equals(
                    normalizedPath,
                    ReviewedInventoryDefiniteShipmentParticipantPath,
                    StringComparison.Ordinal))
            {
                var reviewedTypes = ReviewedContextTypeIdentifierRegex()
                    .Matches(searchableSource)
                    .Select(match => match.Groups["type"].Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                foreach (Match targetTypedNew in TargetTypedNewRegex().Matches(
                             searchableSource))
                {
                    foreach (var reviewedType in reviewedTypes)
                    {
                        constructions.Add(new ReviewedContextConstruction(
                            normalizedPath,
                            reviewedType,
                            GetLineNumber(searchableSource, targetTypedNew.Index)));
                    }
                }
            }
        }

        foreach (var construction in constructions)
        {
            var authorizedByDesignTimeFactory = ReviewedPlatformContextFactories.TryGetValue(
                                  construction.RepositoryPath,
                                  out var factory)
                              && string.Equals(
                                  construction.ContextTypeName,
                                  factory.ContextTypeName,
                                  StringComparison.Ordinal);
            var authorizedByReviewedRuntimeParticipant =
                ReviewedRuntimeContextConstructions.TryGetValue(
                    construction.RepositoryPath,
                    out var runtimeConstruction)
                && string.Equals(
                    construction.ContextTypeName,
                    runtimeConstruction.ContextTypeName,
                    StringComparison.Ordinal)
                && sources.TryGetValue(construction.RepositoryPath, out var runtimeSource)
                && ReviewedRuntimeDatabaseBypassSources.TryGetValue(
                    construction.RepositoryPath,
                    out var reviewedRuntimeSource)
                && string.Equals(
                    ComputeReviewedSourceFingerprint(runtimeSource),
                    reviewedRuntimeSource.ReviewedFingerprint,
                    StringComparison.Ordinal);
            var authorized = authorizedByDesignTimeFactory || authorizedByReviewedRuntimeParticipant;
            if (!authorized)
            {
                errors.Add(
                    "Unauthorized reviewed platform DbContext construction "
                    + $"'{construction.ContextTypeName}' at "
                    + $"{construction.RepositoryPath}:{construction.LineNumber}");
            }
        }

        foreach (var (participantPath, reviewedConstruction) in ReviewedRuntimeContextConstructions)
        {
            if (!sources.ContainsKey(participantPath))
                continue;

            var participantConstructions = constructions.Count(construction =>
                string.Equals(
                    construction.RepositoryPath,
                    participantPath,
                    StringComparison.Ordinal)
                && string.Equals(
                    construction.ContextTypeName,
                    reviewedConstruction.ContextTypeName,
                    StringComparison.Ordinal));
            if (participantConstructions != reviewedConstruction.ExpectedCount)
            {
                errors.Add(
                    $"Expected exactly {reviewedConstruction.ExpectedCount} "
                    + $"'{reviewedConstruction.ContextTypeName}' construction(s) in reviewed "
                    + $"runtime participant {participantPath}, "
                    + $"but found {participantConstructions}.");
            }
        }

        foreach (var (factoryPath, factory) in ReviewedPlatformContextFactories)
        {
            if (!sources.TryGetValue(factoryPath, out var source))
            {
                errors.Add($"Reviewed platform design-time context factory is missing: {factoryPath}");
                continue;
            }

            if (ClassifySource(factoryPath) != PlatformSourceCategory.DesignTime)
            {
                errors.Add(
                    "Reviewed platform context factory unexpectedly left the design-time "
                    + $"source category: {factoryPath}");
            }

            var factoryConstructions = constructions
                .Where(construction => string.Equals(
                    construction.RepositoryPath,
                    factoryPath,
                    StringComparison.Ordinal))
                .ToArray();
            if (factoryConstructions.Length != 1
                || !string.Equals(
                    factoryConstructions[0].ContextTypeName,
                    factory.ContextTypeName,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"Expected exactly one '{factory.ContextTypeName}' construction in reviewed "
                    + $"design-time factory {factoryPath}, but found "
                    + (factoryConstructions.Length == 0
                        ? "none."
                        : string.Join(
                            ", ",
                            factoryConstructions.Select(
                                construction => construction.ContextTypeName))
                          + "."));
            }

            var actualFingerprint = ComputeReviewedSourceFingerprint(source);
            if (!string.Equals(
                    actualFingerprint,
                    factory.ReviewedFingerprint,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "Platform design-time context factory does not match the reviewed "
                    + $"fingerprint. Expected {factory.ReviewedFingerprint}, found "
                    + $"{actualFingerprint}: {factoryPath}");
            }
        }

        return errors;
    }

    [Theory]
    [InlineData("src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/GroupSharedCommandDesignTimeDbContextFactory.cs", "GroupSharedCommandDbContext")]
    [InlineData("src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/ProductDesignTimeDbContextFactory.cs", "ProductDbContext")]
    [InlineData("src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListDesignTimeDbContextFactory.cs", "ProductPriceListDbContext")]
    public void Additional_design_time_factories_require_exact_constructors_and_do_not_authorize_other_paths(string path, string contextType)
    {
        var sources = ReadBackendSources(FindRepositoryRoot());
        var source = sources[path];
        var constructor = "new " + contextType + "(";
        Assert.Equal(1, CountAuditOccurrences(source, constructor));
        sources[path] = source.Replace(constructor, "new UnapprovedContext(", StringComparison.Ordinal);
        Assert.Contains(ValidateReviewedPlatformContextConstructions(sources),
            error => error.Contains("Expected exactly one '" + contextType + "' construction", StringComparison.Ordinal));
        sources[path] = source;
        sources["src/backend/GoymenERP.Api/Unapproved" + contextType + "DesignTimeDbContextFactory.cs"] = source;
        Assert.Contains(ValidateReviewedPlatformContextConstructions(sources),
            error => error.Contains("Unauthorized design-time DbContext factory reference", StringComparison.Ordinal));
    }

    [Fact]
    public void Price_list_design_time_factory_invocation_is_rejected_outside_its_exact_path()
    {
        var sources = ReadBackendSources(FindRepositoryRoot());
        sources["src/backend/GoymenERP.Api/UnapprovedPriceListFactoryUse.cs"] =
            "new ProductPriceListDesignTimeDbContextFactory().CreateDbContext([]);";
        Assert.Contains(ValidateReviewedPlatformContextConstructions(sources),
            error => error.Contains("Unauthorized design-time DbContext factory reference", StringComparison.Ordinal));
    }

    private static string ComputeReviewedSourceFingerprint(string source)
    {
        var normalizedLineEndings = source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var normalized = string.Join(
            '\n',
            normalizedLineEndings.Split('\n').Select(line => line.TrimEnd()))
            .TrimEnd('\n') + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
    }

    private static IReadOnlyList<string> ValidateOpenApiBuildTimeNetworkIsolation(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();
        if (!sources.TryGetValue(OpenApiStartupPath, out var programSource))
        {
            errors.Add($"OpenAPI startup source is missing: {OpenApiStartupPath}");
            return errors;
        }

        var identityPath = ReviewedRuntimeDatabaseBypassSources.Keys.Single(path =>
            path.EndsWith("IdentityModuleExtensions.cs", StringComparison.Ordinal));
        if (!sources.TryGetValue(identityPath, out var identitySource))
        {
            errors.Add($"OpenAPI hosted-service source is missing: {identityPath}");
            return errors;
        }

        var startupFingerprint = ComputeReviewedSourceFingerprint(programSource);
        if (!string.Equals(
                startupFingerprint,
                ReviewedOpenApiStartupFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "OpenAPI startup fingerprint does not match the reviewed network-isolated "
                + $"composition. Expected {ReviewedOpenApiStartupFingerprint}, found "
                + $"{startupFingerprint}: {OpenApiStartupPath}");
        }

        var reviewedIdentity = ReviewedRuntimeDatabaseBypassSources[identityPath];
        var identityFingerprint = ComputeReviewedSourceFingerprint(identitySource);
        if (!string.Equals(
                identityFingerprint,
                reviewedIdentity.ReviewedFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "OpenAPI hosted-service gate fingerprint does not match the reviewed "
                + $"composition. Expected {reviewedIdentity.ReviewedFingerprint}, found "
                + $"{identityFingerprint}: {identityPath}");
        }

        if (!identitySource.Contains(
                "if (!OpenApiDocumentGenerationMode.IsActive)",
                StringComparison.Ordinal))
        {
            errors.Add(
                "OpenAPI hosted-service gate must remain fail-closed under the exact "
                + "GetDocument.Insider entry assembly signal.");
        }

        // B-24: the Work outbox dispatcher is the second reviewed custom hosted service. It is default-off
        // configuration and, like Identity's, registered only outside OpenAPI document generation.
        var workHostedServicePath = ReviewedRuntimeDatabaseBypassSources.Keys.Single(path =>
            path.EndsWith("WorkModuleExtensions.cs", StringComparison.Ordinal));
        var hostedServiceRegistrations = sources
            .Where(source => source.Value.Contains("AddHostedService<", StringComparison.Ordinal))
            .Select(source => source.Key)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        // tren-1 W5: the idempotency retention purge (OPEN-021, NS-12) is the third reviewed custom hosted service,
        // registered only outside OpenAPI document generation like the two above.
        var idempotencyHostedServicePath = ReviewedRuntimeDatabaseBypassSources.Keys.Single(path =>
            path.EndsWith("IdempotencyModuleExtensions.cs", StringComparison.Ordinal));
        var reviewedHostedServicePaths = new[] { identityPath, workHostedServicePath, idempotencyHostedServicePath }
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (!hostedServiceRegistrations.SequenceEqual(reviewedHostedServicePaths, StringComparer.Ordinal))
        {
            errors.Add(
                "OpenAPI build-time composition must have exactly the reviewed custom "
                + "hosted-service registrations (Identity, Work outbox dispatcher and idempotency retention purge): "
                + string.Join(", ", hostedServiceRegistrations));
        }

        if (!sources.TryGetValue(workHostedServicePath, out var workHostedServiceSource)
            || !Regex.IsMatch(
                workHostedServiceSource,
                @"if\s*\(\s*!OpenApiDocumentGenerationMode\.IsActive\s*\)\s*\{\s*services\.AddHostedService<WorkOutboxDispatcherHostedService>\(\);",
                RegexOptions.CultureInvariant))
        {
            errors.Add(
                "The Work outbox dispatcher hosted service must be registered only inside "
                + "if (!OpenApiDocumentGenerationMode.IsActive).");
        }

        if (!sources.TryGetValue(idempotencyHostedServicePath, out var idempotencyHostedServiceSource)
            || !Regex.IsMatch(
                idempotencyHostedServiceSource,
                @"if\s*\(\s*!OpenApiDocumentGenerationMode\.IsActive\s*\)\s*\{\s*services\.AddHostedService<IdempotencyRetentionPurgeHostedService>\(\);",
                RegexOptions.CultureInvariant))
        {
            errors.Add(
                "The idempotency retention purge hosted service must be registered only inside "
                + "if (!OpenApiDocumentGenerationMode.IsActive).");
        }

        const string initializerGuard =
            "if (!isOpenApiDocumentGeneration\n"
            + "    && databaseStartupMode == DatabaseStartupMode.Initialize\n"
            + "    && (app.Environment.IsDevelopment() || app.Environment.IsEnvironment(\"Testing\")))";
        var normalizedProgram = programSource.Replace("\r\n", "\n", StringComparison.Ordinal);
        var guardIndex = normalizedProgram.IndexOf(initializerGuard, StringComparison.Ordinal);
        var runIndex = normalizedProgram.IndexOf("await app.RunAsync();", StringComparison.Ordinal);
        if (guardIndex < 0 || runIndex <= guardIndex)
        {
            errors.Add(
                "OpenAPI module-initializer gate must precede application RunAsync under "
                + "the exact entry-assembly signal.");
        }

        foreach (var initializer in new[]
                 {
                     "InitializeOrganizationModuleAsync",
                     "InitializeIdentityModuleAsync",
                     "InitializeAuditModuleAsync",
                     "InitializeIdempotencyModuleAsync",
                     "InitializeCounterpartyModuleAsync",
                 })
        {
            var occurrences = Regex.Matches(
                normalizedProgram,
                Regex.Escape(initializer),
                RegexOptions.CultureInvariant);
            if (occurrences.Count != 1
                || occurrences[0].Index <= guardIndex
                || occurrences[0].Index >= runIndex)
            {
                errors.Add(
                    $"OpenAPI build-time initializer {initializer} must occur exactly once "
                    + "inside the reviewed guarded startup block.");
            }
        }

        if (sources.Any(source => source.Value.Contains("MapOpenApi", StringComparison.Ordinal)))
        {
            errors.Add("Runtime MapOpenApi exposure is forbidden in the reviewed backend surface.");
        }

        return errors;
    }

    private static string MaskCommentsAndNonInterpolatedLiterals(string source)
    {
        var masked = source.ToCharArray();
        var index = 0;

        while (index < source.Length)
        {
            if (StartsWith(source, index, "//"))
            {
                var end = source.IndexOf('\n', index + 2);
                end = end < 0 ? source.Length : end;
                Mask(masked, index, end);
                index = end;
                continue;
            }

            if (StartsWith(source, index, "/*"))
            {
                var closing = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var end = closing < 0 ? source.Length : closing + 2;
                Mask(masked, index, end);
                index = end;
                continue;
            }

            if (TryReadRawString(source, index, out var rawEnd, out var isInterpolatedRaw))
            {
                // Interpolated strings stay searchable as a fail-closed tradeoff: their
                // expressions can execute the bypass, while ordinary/raw text is masked.
                if (!isInterpolatedRaw)
                {
                    Mask(masked, index, rawEnd);
                }

                index = rawEnd;
                continue;
            }

            if (StartsWith(source, index, "$@\"") || StartsWith(source, index, "@$\""))
            {
                index = FindInterpolatedStringEnd(source, index + 3, isVerbatim: true);
                continue;
            }

            if (StartsWith(source, index, "$\""))
            {
                index = FindInterpolatedStringEnd(source, index + 2, isVerbatim: false);
                continue;
            }

            if (StartsWith(source, index, "@\""))
            {
                var end = FindVerbatimStringEnd(source, index + 2);
                Mask(masked, index, end);
                index = end;
                continue;
            }

            if (source[index] == '"')
            {
                var end = FindRegularStringEnd(source, index + 1);
                Mask(masked, index, end);
                index = end;
                continue;
            }

            if (source[index] == '\'')
            {
                var end = FindCharacterLiteralEnd(source, index + 1);
                Mask(masked, index, end);
                index = end;
                continue;
            }

            index++;
        }

        return new string(masked);
    }

    private static bool TryReadRawString(
        string source,
        int start,
        out int end,
        out bool isInterpolated)
    {
        var cursor = start;
        var dollarCount = 0;

        while (cursor < source.Length && source[cursor] == '$')
        {
            dollarCount++;
            cursor++;
        }

        var quoteStart = cursor;
        while (cursor < source.Length && source[cursor] == '"')
        {
            cursor++;
        }

        var delimiterLength = cursor - quoteStart;
        if (delimiterLength < 3)
        {
            end = start;
            isInterpolated = false;
            return false;
        }

        isInterpolated = dollarCount > 0;
        while (cursor < source.Length)
        {
            if (source[cursor] != '"')
            {
                cursor++;
                continue;
            }

            var closingStart = cursor;
            while (cursor < source.Length && source[cursor] == '"')
            {
                cursor++;
            }

            if (cursor - closingStart >= delimiterLength)
            {
                end = cursor;
                return true;
            }
        }

        end = source.Length;
        return true;
    }

    private static int FindRegularStringEnd(string source, int contentStart)
    {
        var index = contentStart;

        while (index < source.Length)
        {
            if (source[index] is '\r' or '\n')
            {
                // A regular C# string cannot cross a physical line. Stopping here also
                // prevents malformed text in a disabled preprocessor branch from masking
                // executable source that follows the branch.
                return index;
            }

            if (source[index] == '\\')
            {
                if (index + 1 < source.Length
                    && (source[index + 1] is '\r' or '\n'))
                {
                    return index + 1;
                }

                index = Math.Min(index + 2, source.Length);
                continue;
            }

            if (source[index] == '"')
            {
                return index + 1;
            }

            index++;
        }

        return source.Length;
    }

    private static int FindInterpolatedStringEnd(
        string source,
        int contentStart,
        bool isVerbatim)
    {
        var index = contentStart;
        var interpolationDepth = 0;

        while (index < source.Length)
        {
            if (interpolationDepth == 0)
            {
                if (!isVerbatim && source[index] == '\\')
                {
                    index = Math.Min(index + 2, source.Length);
                    continue;
                }

                if (source[index] == '"')
                {
                    if (isVerbatim
                        && index + 1 < source.Length
                        && source[index + 1] == '"')
                    {
                        index += 2;
                        continue;
                    }

                    return index + 1;
                }

                if (source[index] == '{')
                {
                    if (index + 1 < source.Length && source[index + 1] == '{')
                    {
                        index += 2;
                        continue;
                    }

                    interpolationDepth = 1;
                }

                index++;
                continue;
            }

            if (StartsWith(source, index, "//"))
            {
                var lineEnd = source.IndexOf('\n', index + 2);
                index = lineEnd < 0 ? source.Length : lineEnd;
                continue;
            }

            if (StartsWith(source, index, "/*"))
            {
                var closing = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = closing < 0 ? source.Length : closing + 2;
                continue;
            }

            if (TryReadRawString(source, index, out var rawEnd, out _))
            {
                index = rawEnd;
                continue;
            }

            if (StartsWith(source, index, "$@\"") || StartsWith(source, index, "@$\""))
            {
                index = FindInterpolatedStringEnd(source, index + 3, isVerbatim: true);
                continue;
            }

            if (StartsWith(source, index, "$\""))
            {
                index = FindInterpolatedStringEnd(source, index + 2, isVerbatim: false);
                continue;
            }

            if (StartsWith(source, index, "@\""))
            {
                index = FindVerbatimStringEnd(source, index + 2);
                continue;
            }

            if (source[index] == '"')
            {
                index = FindRegularStringEnd(source, index + 1);
                continue;
            }

            if (source[index] == '\'')
            {
                index = FindCharacterLiteralEnd(source, index + 1);
                continue;
            }

            if (source[index] == '{')
            {
                interpolationDepth++;
            }
            else if (source[index] == '}')
            {
                interpolationDepth--;
            }

            index++;
        }

        return source.Length;
    }

    private static int FindVerbatimStringEnd(string source, int contentStart)
    {
        var index = contentStart;

        while (index < source.Length)
        {
            if (source[index] != '"')
            {
                index++;
                continue;
            }

            if (index + 1 < source.Length && source[index + 1] == '"')
            {
                index += 2;
                continue;
            }

            return index + 1;
        }

        return source.Length;
    }

    private static int FindCharacterLiteralEnd(string source, int contentStart)
    {
        var index = contentStart;

        while (index < source.Length)
        {
            if (source[index] == '\\')
            {
                index = Math.Min(index + 2, source.Length);
                continue;
            }

            if (source[index] == '\'')
            {
                return index + 1;
            }

            index++;
        }

        return source.Length;
    }

    private static string DecodeIdentifierUnicodeEscapes(string source)
    {
        var decoded = UnicodeEightDigitEscapeRegex().Replace(
            source,
            match => DecodeCodePoint(match.Groups[1].Value));
        return UnicodeFourDigitEscapeRegex().Replace(
            decoded,
            match => DecodeCodePoint(match.Groups[1].Value));
    }

    private static string NormalizeExecutableIdentifiers(string source)
    {
        var decoded = DecodeIdentifierUnicodeEscapes(source);
        var normalized = new StringBuilder(decoded.Length);

        foreach (var rune in decoded.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format)
            {
                normalized.Append(rune.ToString());
            }
        }

        return normalized.ToString();
    }

    private static string DecodeCodePoint(string hexadecimal)
    {
        var codePoint = Convert.ToInt32(hexadecimal, 16);
        return codePoint is >= 0 and <= 0x10FFFF
            ? char.ConvertFromUtf32(codePoint)
            : string.Empty;
    }

    private static int GetLineNumber(string source, int position)
    {
        var lineNumber = 1;
        for (var index = 0; index < position; index++)
        {
            if (source[index] == '\n')
            {
                lineNumber++;
            }
        }

        return lineNumber;
    }

    private static void Mask(char[] target, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (target[index] is not ('\r' or '\n'))
            {
                target[index] = ' ';
            }
        }
    }

    private static bool StartsWith(string source, int index, string value)
    {
        return index + value.Length <= source.Length
               && source.AsSpan(index, value.Length).SequenceEqual(value);
    }

    private static bool IsBuildOutput(string repositoryPath)
    {
        return repositoryPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static PlatformSourceCategory ClassifySource(string repositoryPath)
    {
        var normalized = NormalizeRepositoryPath(repositoryPath);
        if (IsBuildOutput(normalized))
        {
            return PlatformSourceCategory.BuildArtifact;
        }

        if (normalized.Contains("/Migrations/", StringComparison.Ordinal))
        {
            return PlatformSourceCategory.Migration;
        }

        if (Path.GetFileName(normalized).EndsWith(
                "DesignTimeDbContextFactory.cs",
                StringComparison.Ordinal))
        {
            return PlatformSourceCategory.DesignTime;
        }

        return PlatformSourceCategory.RuntimeApplication;
    }

    private static string DescribeSourceCategory(PlatformSourceCategory category)
    {
        return category switch
        {
            PlatformSourceCategory.RuntimeApplication => "runtime application",
            PlatformSourceCategory.Migration => "migration",
            PlatformSourceCategory.DesignTime => "design-time",
            PlatformSourceCategory.BuildArtifact => "build artifact",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };
    }

    private static bool IsExcludedCounterpartyPath(string repositoryPath)
    {
        var normalized = NormalizeRepositoryPath(repositoryPath);
        return normalized.StartsWith(
            ExcludedCounterpartyPrefix,
            StringComparison.Ordinal);
    }

    private static bool IsUnderDirectory(string directory, string candidatePath)
    {
        var relativePath = Path.GetRelativePath(directory, candidatePath);
        return relativePath != ".."
               && !relativePath.StartsWith(
                   $"..{Path.DirectorySeparatorChar}",
                   StringComparison.Ordinal)
               && !Path.IsPathRooted(relativePath);
    }

    private static string NormalizeRepositoryPath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static string ToNativeFileSystemPath(string path)
    {
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("GoymenERP repository root was not found.");
    }

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])IgnoreQueryFilters(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex IgnoreQueryFiltersIdentifierRegex();

    [GeneratedRegex(
        @"^[^\S\r\n]*#[^\S\r\n]*(?:if|elif|else|endif|define|undef)"
        + @"(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex ConditionalCompilationDirectiveRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])(?:dynamic|MethodInfo|MethodBase|RuntimeMethodHandle|"
        + @"GetMethod|GetMethods|GetMember|GetMembers|GetRuntimeMethod|GetRuntimeMethods|"
        + @"GetProperty|GetProperties|PropertyInfo|GetField|GetFields|FieldInfo|"
        + @"GetEvent|GetEvents|EventInfo|BindingFlags|GetValue|SetValue|"
        + @"GetRuntimeBaseDefinition|GetTypeInfo|GetDeclaredMethod|GetDeclaredMethods|"
        + @"DeclaredMethod|DeclaredMethods|ResolveMethod|ResolveMember|InvokeMember|"
        + @"ConstructorInfo|GetConstructor|GetConstructors|"
        + @"DynamicInvoke|CreateDelegate|GetFunctionPointer|CallSite|GetType|"
        + @"Activator|ActivatorUtilities|CreateInstance|AssemblyLoadContext|Assembly|"
        + @"LoadFromAssemblyPath)"
        + @"(?![\p{L}\p{Nd}_])|"
        + @"(?<![\p{L}\p{Nd}_])(?:@?System\s*\.\s*@?Reflection|"
        + @"@?System\s*\.\s*@?Dynamic|"
        + @"@?System\s*\.\s*@?Linq\s*\.\s*@?Expressions|"
        + @"Expressions|Expression\s*\.\s*Call)"
        + @"(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenBypassCapabilityRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])(?:"
        + @"FromSql(?:Raw|Interpolated)?(?:Async)?|"
        + @"SqlQuery(?:Raw)?(?:Async)?|"
        + @"ExecuteSql(?:Raw|Interpolated)?(?:Async)?|"
        + @"ExecuteNonQuery(?:Async)?|"
        + @"EnsureCreated(?:Async)?|EnsureDeleted(?:Async)?|Migrate(?:Async)?|"
        + @"GetDbConnection|CreateDbCommand|CreateCommand|"
        + @"DbConnection|IDbConnection|DbCommand|IDbCommand|DbDataReader|"
        + @"DbDataAdapter|IDbDataAdapter|DbProviderFactory|DbProviderFactories|"
        + @"DbDataSource|DbBatch|DbBatchCommand|"
        + @"SqlConnection|SqlCommand|SqlDataReader|SqlDataAdapter|"
        + @"SqlDataSource|SqlBatch|SqlBatchCommand|SqlBulkCopy|"
        + @"IRelationalConnection|RelationalConnection|"
        + @"IDatabaseCreator|IRelationalDatabaseCreator|RelationalDatabaseCreator|"
        + @"IMigrator|Migrator|"
        + @"IRelationalCommand|RelationalCommand|RawSqlCommand|IRawSqlCommandBuilder|"
        + @"RelationalDatabaseFacadeExtensions|RelationalQueryableExtensions|"
        + @"IDbContextFactory|AddDbContextFactory|AddDbContextPool|"
        + @"AddPooledDbContextFactory|PooledDbContextFactory|UseModel|"
        + @"ReplaceService|UseInternalServiceProvider|UseApplicationServiceProvider|"
        + @"IModelCustomizer|IModelCacheKeyFactory|IModelSource|IModelRuntimeInitializer|"
        + @"IConventionSetPlugin|IQueryExpressionInterceptor|DbCommandInterceptor|"
        + @"IDbCommandInterceptor"
        + @")(?![\p{L}\p{Nd}_])|"
        + @"(?<![\p{L}\p{Nd}_])(?:"
        + @"@?Microsoft\s*\.\s*@?Data\s*\.\s*@?SqlClient|"
        + @"@?System\s*\.\s*@?Data\s*\.\s*@?(?:Common|SqlClient|Odbc|OleDb)"
        + @")(?![\p{L}\p{Nd}_])|"
        + @"(?<![\p{L}\p{Nd}_])new\s+"
        + @"(?:global\s*::\s*)?(?:@?[\p{L}_][\p{L}\p{Nd}_]*\s*\.\s*)*"
        + @"@?DbContext\s*\(|"
        + @"(?<![\p{L}\p{Nd}_])@?DbContext\s+"
        + @"@?[\p{L}_][\p{L}\p{Nd}_]*\s*=\s*new\s*\(|"
        + @"\.\s*@?(?:Find(?:Async)?|GetDatabaseValues(?:Async)?|Reload(?:Async)?)"
        + @"(?![\p{L}\p{Nd}_])|"
        + @"\.\s*@?Local(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex DirectDatabaseBypassCapabilityRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])@?(?<api>Sql|InsertData|UpdateData|DeleteData)"
        + @"(?![\p{L}\p{Nd}_])|"
        + @"(?<![\p{L}\p{Nd}_])@?(?<api>SqlOperation|InsertDataOperation|"
        + @"UpdateDataOperation|DeleteDataOperation)(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex MigrationMutationCapabilityRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])(?:class|record(?:\s+class)?)\s+"
        + @"(?<name>@?[\p{L}_][\p{L}\p{Nd}_]*)[^;{}]*?:\s*"
        + @"(?:global\s*::\s*)?(?:@?[\p{L}_][\p{L}\p{Nd}_]*\s*\.\s*)*"
        + @"@?DbContext(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex DbContextDeclarationRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])(?:global\s+)?using\s+"
        + @"(?<alias>@?[\p{L}_][\p{L}\p{Nd}_]*)\s*=\s*"
        + @"(?:global\s*::\s*)?(?:@?[\p{L}_][\p{L}\p{Nd}_]*\s*\.\s*)*"
        + @"@?DbContext(?![\p{L}\p{Nd}_])\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex DbContextBaseTypeAliasRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])new\s+"
        + @"(?:global\s*::\s*)?(?:@?[\p{L}_][\p{L}\p{Nd}_]*\s*\.\s*)*@?"
        + @"(?<type>AuditDbContext|CommandExecutionDbContext|IdentityDbContext|"
        + @"OrganizationDbContext|InventoryDbContext|AccountingDbContext|FinanceDbContext|ManufacturingDbContext|SalesDbContext|LogisticsDbContext|PurchasingDbContext|DocumentsDbContext|WorkDbContext|SettingsDbContext|SecurityEventsDbContext)(?![\p{L}\p{Nd}_])\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReviewedContextConstructionRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])@?(?<type>AuditDbContext|CommandExecutionDbContext|"
        + @"IdentityDbContext|OrganizationDbContext|InventoryDbContext|AccountingDbContext|FinanceDbContext|ManufacturingDbContext|SalesDbContext|LogisticsDbContext|PurchasingDbContext|DocumentsDbContext|WorkDbContext|SettingsDbContext|SecurityEventsDbContext)"
        + @"(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReviewedContextTypeIdentifierRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])(?:return\s+new|new|=>\s*new)\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex TargetTypedNewRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])(?:global\s+)?using\s+"
        + @"(?<alias>@?[\p{L}_][\p{L}\p{Nd}_]*)\s*=\s*"
        + @"(?:global\s*::\s*)?(?:@?[\p{L}_][\p{L}\p{Nd}_]*\s*\.\s*)*@?"
        + @"(?<type>AuditDbContext|CommandExecutionDbContext|IdentityDbContext|"
        + @"OrganizationDbContext|InventoryDbContext|AccountingDbContext|FinanceDbContext|ManufacturingDbContext|SalesDbContext|LogisticsDbContext|PurchasingDbContext|DocumentsDbContext|WorkDbContext|SettingsDbContext|SecurityEventsDbContext)(?![\p{L}\p{Nd}_])\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReviewedContextTypeAliasRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])@?(?:IDesignTimeDbContextFactory|"
        + @"AuditDesignTimeDbContextFactory|"
        + @"CommandExecutionDesignTimeDbContextFactory|"
        + @"IdentityDesignTimeDbContextFactory|"
        + @"OrganizationDesignTimeDbContextFactory|"
        + @"InventoryDesignTimeDbContextFactory|"
        + @"AccountingDesignTimeDbContextFactory|"
        + @"FinanceDesignTimeDbContextFactory|"
        + @"ManufacturingDesignTimeDbContextFactory|"
        + @"SalesDesignTimeDbContextFactory|"
        + @"LogisticsDesignTimeDbContextFactory|"
        + @"PurchasingDesignTimeDbContextFactory|"
        + @"DocumentsDesignTimeDbContextFactory|WorkDesignTimeDbContextFactory|SettingsDesignTimeDbContextFactory|SecurityEventsDesignTimeDbContextFactory|ProductPriceListDesignTimeDbContextFactory)(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReviewedDesignTimeFactoryIdentifierRegex();

    [GeneratedRegex(@"\\u([0-9a-fA-F]{4})", RegexOptions.CultureInvariant)]
    private static partial Regex UnicodeFourDigitEscapeRegex();

    [GeneratedRegex(@"\\U([0-9a-fA-F]{8})", RegexOptions.CultureInvariant)]
    private static partial Regex UnicodeEightDigitEscapeRegex();

    private sealed record BypassOccurrence(string RepositoryPath, int LineNumber)
    {
        public override string ToString() => $"{RepositoryPath}:{LineNumber}";
    }

    private sealed record DbContextDeclaration(
        string RepositoryPath,
        string TypeName,
        int LineNumber,
        PlatformSourceCategory SourceCategory);

    private sealed record ReviewedContextFactory(
        string ContextTypeName,
        string ReviewedFingerprint);

    private sealed record ReviewedMigrationMutationSource(
        string ReviewedFingerprint,
        IReadOnlyDictionary<string, int> ExpectedApiOccurrences);

    private sealed record ReviewedRuntimeDatabaseBypassSource(
        string ReviewedFingerprint,
        IReadOnlyDictionary<string, int> ExpectedApiOccurrences);

    private sealed record ReviewedContextConstruction(
        string RepositoryPath,
        string ContextTypeName,
        int LineNumber);

    private sealed record MigrationMutationOccurrence(
        string RepositoryPath,
        string ApiName,
        int LineNumber,
        PlatformSourceCategory SourceCategory);

    private sealed record DirectDatabaseBypassOccurrence(
        string RepositoryPath,
        string ApiName,
        int LineNumber,
        PlatformSourceCategory SourceCategory);

    private sealed record CompositionRootEdge(string Source, string Target);

    private sealed record CounterpartyCompositionRootContract(
        IReadOnlyCollection<CompositionRootEdge> Edges);

    private sealed record BackendSourceInventory(
        IReadOnlyDictionary<string, string> Sources,
        IReadOnlyList<string> ProjectPaths,
        IReadOnlyList<string> DefinitionErrors);

    private sealed record BackendProjectDocument(
        string FullPath,
        string RepositoryPath,
        XDocument Document);

    private sealed record ProjectDefinitionAnalysis(
        IReadOnlyList<CompileDirective> CompileDirectives,
        IReadOnlyList<string> Errors);

    private sealed record CompileDirective(
        CompileDirectiveKind Kind,
        IReadOnlyList<string> Items,
        IReadOnlyList<string> Excludes,
        string? Link);

    private enum CompileDirectiveKind
    {
        Include,
        Remove,
        Update
    }

    private enum PlatformSourceCategory
    {
        RuntimeApplication,
        Migration,
        DesignTime,
        BuildArtifact
    }
}
