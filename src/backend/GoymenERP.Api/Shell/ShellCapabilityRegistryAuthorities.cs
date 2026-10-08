namespace GoymenERP.Api.Shell;

/// <summary>
/// Accepted registry revisions, one per provider set the host or a test builds.
///
/// ORG-03 moved Organization's contract version 1.0 → 1.1, which changes every revision containing
/// it: EIGHT constants were re-derived and renamed <c>OrganizationV10</c> → <c>OrganizationV11</c>.
/// The name states which provider versions the value is for, so leaving a 1.1 hash under a
/// <c>…V10</c> name would make the identifier lie — and the rename has a compiler for a verifier,
/// where a prose change would not.
///
/// FIVE constants deliberately keep <c>OrganizationV10</c> in both name and value, and they are
/// named here so nobody reads them as overlooked:
/// <c>CounterpartyV15OrganizationV10ProductV10AndSalesV10</c>,
/// <c>…ProductV10SalesV10AndFinanceV10</c>,
/// <c>…PublishedSalesV10AndFinanceV10</c>,
/// <c>…PublishedSalesV10FinanceV10AndManufacturingV10</c> and
/// <c>…PublishedSalesV10PublishedFinanceV10AndManufacturingV10</c>. Nothing constructs those
/// provider sets today — no host row, no test — so each remains internally consistent: a 1.0 value
/// under a 1.0 name, describing a composition that no longer exists. That is a true statement about
/// a dead set, which is a better thing to leave behind than a fresh value under a stale name.
/// Deleting them is a separate claim and is not made here.
///
/// CARI-NORMAL publishes THREE of Counterparty's four declarations — C-01 list, C-02 create and
/// C-03 detail — so all NINE host rows move, Counterparty being in every composition. Their names
/// carry <c>PublishedListCreateAndDetail</c>, and the form needs a word because it differs from the
/// <c>Published…</c> prefix Sales, Finance, Manufacturing and Accounting carry:
/// <list type="bullet">
/// <item>A bare <c>Published</c> prefix asserts WHOLENESS, which is false at two of four. That is
/// the defect caught in <c>…AndAccountingV10</c>, where the identifier said "staged" while six of
/// seven were live — there the name could not carry the partiality and a comment had to. Here it
/// can, and a name is checked by a compiler while a comment ages.</item>
/// <item>The DECLARATION set is already pinned by <c>V15</c>: the contract version IS the statement
/// "these four exist", and if that set changes the version moves. So the old
/// <c>ListCreateLinkAndDetail</c> restated an invariant the version already carried. The PUBLISHED
/// subset is the only part that varies, so it is the only part the name should carry — a name whose
/// content never changes is noise.</item>
/// <item>The convention closes on itself: at full publication these become
/// <c>PublishedListCreateLinkAndDetail</c>, the enumerative form Counterparty already used, rather
/// than leaving a prefix that needs reinterpreting.</item>
/// </list>
/// The values come from ORG's independent derivation, produced before CARİ's local PowerShell
/// execution-policy refusal was reported. It calibrated the same model against main (9/9) and the
/// obsolete create+detail-only set (9/9). The three disposable envelope constants do NOT move, all
/// nine normal-host values DO move, and none reuses its obsolete two-flag counterpart.
/// </summary>
internal static class ShellCapabilityRegistryAuthorities
{
    // ORG-04: Organization 1.2 adds the read-only work-center card route (organization.work-centers.read, no
    // new permission). Every live normal-host row carries Organization, so all nine were renamed V11 -> V12
    // and re-derived by ShellCapabilityRegistry.ComputeCandidateRevision over main f72978ef's real providers with
    // this change's Organization 1.2 provider; the historical ProductV10 row keeps its V11 name and value.
    // ORG master edit: Organization 1.3 adds five create/edit controls (organization.sites/locations/work-centers.write).
    // Every live normal-host row carries Organization, so all nine were renamed V12 -> V13 and re-derived by
    // ShellCapabilityRegistry.ComputeCandidateRevision over main 00a87981's providers with this change's Organization 1.3.
    internal const string CounterpartyV15PublishedListCreateAndDetailAndOrganizationV13 =
        "sha256:EnpuLRDSoDyVZrlaZ_nKRBnIPVLxrI7fYNWhTBgm8kk";

    // Counterparty 1.5 + Organization 1.1 + Product 1.1 (U-01-360): the normal host's set once Product
    // publishes its four screens.
    // Product 1.2 (DEC-CAR-003 / PRD-PERM-001) adds the deactivate and reactivate controls under the card. Every live
    // normal-host row carries Product, so all eight were renamed ProductV11 -> V12 and re-derived by
    // ShellCapabilityRegistry.ComputeCandidateRevision over main 514eb1fd's providers with this change's Product 1.2.
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13AndProductV12 =
        "sha256:pog9pT8uBedhcY4cR3AG0nmpU3vtY8H6y-7EpaJa510";

    // Counterparty 1.5 + Organization 1.0 + Product 1.0 + Sales 1.0 (#321): the normal host while Sales'
    // draft screens were staged. Superseded by the published set below (#328); no host serves it now.
    internal const string CounterpartyV15OrganizationV10ProductV10AndSalesV10 =
        "sha256:b6YPCP_FsrRvvQ-V6nM8bH6q96qEvqHghZEu40Ihsps";

    // Counterparty 1.5 + Organization 1.0 + Product 1.0 + Sales 1.0 PUBLISHED (#328, S03-c): the three Sales
    // draft screens available. Computed by the ComputeRevision reproduction (which re-derives every accepted
    // constant) and, independently, by ORG (5735275571) and SİPARİŞ; pinned by test.
    // Re-measured for SALES-A1 (the Sales approve control on S-04): ComputeRevision reproduction, and
    // independently re-checked (ORG DATA ONLY); the control keeps its parent's context; pinned by test.
    // DEC-SAL-003: Sales 1.2 adds the delivery-schedule control of the S-04 detail; this row was re-derived by
    // ShellCapabilityRegistry.ComputeCandidateRevision over main aaea20d0's providers with this change's Sales 1.2.
    // DEC-SAL-005: Sales 1.3 adds the cancel control of the S-04 detail; this row and every later live row carrying
    // PublishedSales were re-derived by ShellCapabilityRegistry.ComputeCandidateRevision over main 533de013's providers
    // with this change's Sales 1.3 (SalesV12 -> SalesV13).
    // DEC-SAL-002: Sales 1.4 adds the quantity-revision control of the S-04 detail; this row and the seven later live rows
    // carrying PublishedSales were re-derived by ShellCapabilityRegistry.ComputeCandidateRevision (drift-test "Actual:")
    // over main 27591d0d's providers with this change's Sales 1.4 (SalesV13 -> SalesV14).
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12AndPublishedSalesV15 =
        "sha256:VArNst5wJHe2QQsJlYDxHu_XwfmtxfhRjtILzJaUmT8";

    // ... + Finance 1.0 (#325, FIN-UI-1a) beside the STAGED Sales screens. Superseded when Sales was published
    // (#328, below); no host serves it now. Computed by the ComputeRevision reproduction and,
    // independently, by ORG (#325 5735351391, row C+O(1.0)+P+S(staged)+F); pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10SalesV10AndFinanceV10 =
        "sha256:lF5_r5EE4pCtayLb0jU4EpudtyzrbgqoRmuQP0KPsbM";

    // Counterparty 1.5 + Organization 1.0 + Product 1.0 + Sales 1.0 PUBLISHED + Finance 1.0 staged (#328 on #325):
    // the normal host once both had landed. Superseded when Finance was published (FIN-UI-1c, below); no host
    // serves it now. Computed by the ComputeRevision reproduction and, independently,
    // by SİPARİŞ and ORG (#325 5735351391, row C+O(1.0)+P+S(available)+F); pinned by test.
    // Re-measured for SALES-A1 (the Sales approve control on S-04): ComputeRevision reproduction, and
    // independently re-checked (ORG DATA ONLY); the control keeps its parent's context; pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10AndFinanceV10 =
        "sha256:k4ntHKfePXRG0zXAOwmnOhmIhz5vH9-nkPl-SAERoUo";

    // ... + Manufacturing 1.0 staged (#323, MFG-UI) on top of #328/#325: the normal host once Manufacturing
    // registered its (staged) order detail and facts control beside published Sales and staged Finance.
    // Superseded when Finance was published (FIN-UI-1c, below); no host serves it now.
    // Computed by the ComputeRevision reproduction and, independently, by SİPARİŞ and ORG; pinned by test.
    // Re-measured for SALES-A1 (the Sales approve control on S-04): ComputeRevision reproduction, and
    // independently re-checked (ORG DATA ONLY); the control keeps its parent's context; pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10FinanceV10AndManufacturingV10 =
        "sha256:4t0WLHeESBuAWHskrXQbOGmYsGTjY_hmkTu3D55qBSE";

    // FIN-T05/FIN-T06: Counterparty 1.5 + Organization 1.1 + Product 1.0 + Sales 1.0 PUBLISHED + Finance 1.1
    // PUBLISHED. Finance 1.2 adds the R47 payment routes and controls after the 1.1 record/reversal declarations while preserving
    // their exact context, scope and parent relations. Derived from the integrated provider tuple and pinned by test.
    // DEC-SAL-003: re-derived the same way over main aaea20d0's providers with Sales 1.2 (SalesV11 -> SalesV12).
    // Finance 1.3 adds the dedicated customer-instrument list route under the existing read permission. This row and
    // every later live normal-host row were re-derived from the integrated provider tuple; disposable envelopes do not move.
    // F-03 allocation reads: Finance 1.5 adds the allocation list route and write child control under separate read/write permissions; all live normal-host Finance rows move together.
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15AndPublishedFinanceV15 =
        "sha256:kja5w-G_8FGXYihdOqd3nxpMwlfVC0Zz-bneWwySotg";

    // FIN-UI-1c: ... + Manufacturing 1.0 staged: the normal host (C+O+P+S(published)+F(published)+M(staged)).
    // Computed by the ComputeRevision reproduction and, independently, by ORG (DATA ONLY); pinned by test.
    // Superseded when Manufacturing was published (MFG-UI-F, below); no host serves it now.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10PublishedFinanceV10AndManufacturingV10 =
        "sha256:90oJBLWkzXFLflvbeE_AAOrVoe9ZowFEqClCaq_tel4";

    // MFG-UI-F / MFG-ORD / MFG plan controls: ... + Manufacturing 1.2 PUBLISHED. Version 1.2 keeps the
    // four route declarations and the two order-detail controls, then adds approve and order-create as
    // child controls of the existing plan-detail route. Publication grants nothing; every control keeps
    // its parent's route, screen, context and company-wide scope and adds only existing permissions.
    // Derived from the integrated provider tuple through ComputeCandidateRevision and pinned by test.
    // MFG-PLAN-LIST: Manufacturing 1.3 adds the saved-plan list route /uretim/planlar (manufacturing.plans.list,
    // existing manufacturing.plans.read, no sidebar entry). This row and every later normal-host row that carries
    // Manufacturing were re-derived by ShellCapabilityRegistry.ComputeCandidateRevision over main b0b3b744's real
    // providers with this change's Manufacturing 1.3 provider (the pinning tests' own computation); the
    // ManufacturingV12 values are superseded.
    // DEC-SAL-003: re-derived the same way over main aaea20d0's providers with Sales 1.2 (SalesV11 -> SalesV12).
    // MFG-PLAN-EDIT: Manufacturing 1.4 adds the plan-detail control manufacturing.plans.edit (plans.read + plans.write).
    // This row and every later normal-host row that carries Manufacturing were re-derived by
    // ShellCapabilityRegistry.ComputeCandidateRevision over main 0b6a68cd's providers (ManufacturingV13 -> V14).
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV15AndPublishedManufacturingV15 =
        "sha256:VRCZL_Cb8DG0AEavVAvAfLb_gtncqhvAd2wQ14pQesc";

    // LOG-PUBLISH: ... + Logistics 1.1 PUBLISHED (the loading-instruction routes plus shipment/delivery actions,
    // master v3 §27.8): the normal host (C+O+P+S(published)+F(published)+M(published)+L(published)). Computed by
    // the ComputeRevision reproduction and, independently, by ORG (DATA ONLY); pinned by test.
    // Re-measured for Manufacturing 1.2's two plan-detail controls; pinned by the integrated provider test.
    // DEC-SAL-003: re-derived the same way over main aaea20d0's providers with Sales 1.2 (SalesV11 -> SalesV12).
    // MFG-PLAN-EDIT: re-derived over main 0b6a68cd's providers with Manufacturing 1.4 (ManufacturingV13 -> V14).
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV15PublishedManufacturingV15AndPublishedLogisticsV12 =
        "sha256:15160RxIBP7VxpGM5tdEC7fa-pS_dr8-sNH1acQRPYY";
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV15PublishedManufacturingV15AndPublishedLogisticsV13 =
        "sha256:81Fyf0AWZg0ZgaXbMoaw77U_GTLsBrtoQOfUDH-jd8g";
    // AT-021 + LOG-HOST: Sales 1.6's floor-approval child is present before Logistics 1.3 joins.
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15AndPublishedLogisticsV13 =
        "sha256:KqkE4-slYIAa4wPQoXpm9IHf_pD6Pw8DB99en5Mo4Jw";

    // PUR-HOST: ... + Purchasing 1.4, with the goods-receipt child control published beside the
    // request list/create/detail/submit and order list/create/detail/approve declarations. Every declaration remains
    // single-company; each child keeps its detail parent's route, context and scope. Derived from the
    // integrated provider tuple through ShellCapabilityRegistry.ComputeCandidateRevision and pinned by test.
    // RE-MEASURED AT THE #478 FORWARD onto main c287128f: #478 carried Logistics 1.0 + Purchasing 1.4
    // (sha256:YNbQJaOz…) and main carried Logistics 1.1 + Purchasing 1.3 (sha256:qLOHlF5W…); the merged host
    // serves Logistics 1.1 + Purchasing 1.4, a third value in neither parent. Both earlier values are SUPERSEDED.
    // Derived from the MERGE TREE's eight real providers by ComputeCandidateRevision (the pinning test's own
    // computation), not chosen from either side. The Accounting and Inventory rows below follow the same rule.
    // RE-MEASURED AT THE #466 FORWARD onto main 55bf0517: #466 carried Product 1.1 with Logistics 1.0 + Purchasing
    // 1.3 and main carried Product 1.0 with Logistics 1.1 + Purchasing 1.4; the merged host serves Product 1.1 +
    // Logistics 1.1 + Purchasing 1.4. The Logistics, Purchasing, Accounting and Inventory rows were derived by the
    // same rule from the merge tree's real providers; the Product 1.1 rows before Logistics are #466's unchanged.
    // DEC-SAL-003: re-derived the same way over main aaea20d0's providers with Sales 1.2 (SalesV11 -> SalesV12).
    // MFG-PLAN-EDIT: re-derived over main 0b6a68cd's providers with Manufacturing 1.4 (ManufacturingV13 -> V14).
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV12AndPublishedPurchasingV14 =
        "sha256:FH9nrN1OMJ-ni71NME3Txj98icm41IYT3NUbART40iU";
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13AndPublishedPurchasingV14 =
        "sha256:Z_-hXm4Ro9t8EZ5elBZP78KnlWU2SU8NmlJkOfg0cBQ";
    // AT-021 + PUR-HOST: the same Sales 1.6 tuple continues through Purchasing 1.4.
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13AndPublishedPurchasingV14 =
        "sha256:NiXC57E2jpBjxHptffDfBKLSIH7oQi6nOJY7HskPe2A";

    // ACC-V0b: ... + Accounting 1.0 STAGED (the period list M-07 and its propose screen, the approve and
    // close controls of that list, the journal list M-04 and its detail, the posting queues M-01): the normal
    // host (C+O+P+S(pub)+F(pub)+M(pub)+L(published)+Pu(staged)+Acc(staged)). Computed by the ComputeRevision
    // reproduction, independently re-checked (DATA ONLY); pinned by test.
    // RE-MEASURED for ACC-V0c: six of Accounting's seven declarations become available (the period
    // list and its propose screen, the journal list, the posting queues — and the approve and close
    // CONTROLS, which are `periods with { … }` and therefore inherit the list's availability, so the
    // count is six even though nothing says available: true on their own lines). M-04-DETAIL stays
    // staged: acc01 never opened a journal, so that route has no acceptance to stand on. The earlier
    // value (sha256:oQPU6TLY…) is SUPERSEDED — it describes a host that no longer exists.
    // RE-MEASURED after MFG-ORD (#360) landed: Manufacturing now declares FOUR capabilities, not two, so the
    // earlier value of this row (sha256:kxDfAaAN…) is SUPERSEDED and describes a host that no longer exists.
    // Its earlier independent check is superseded with it — a match against the old value is not evidence.
    //
    // RE-MEASURED AT THE MERGE, and this row is why the merge could not be resolved by choosing a side.
    // Two branches changed this one constant for unrelated reasons: main renamed it OrganizationV10 →
    // V11 and re-derived it (ORG-03 changed Organization's declarations), while this branch kept the
    // name and re-derived it (ACC-V0c published six of Accounting's seven). The merged host carries
    // BOTH, so its revision is a THIRD value that exists in neither parent: sha256:Ea6VIFmw… describes a
    // host with Accounting still staged, sha256:OOlEtnDx… one with Organization still at 1.0, and both
    // are now SUPERSEDED. Taking either side would have compiled and then failed in the registry
    // constructor during the build (the OpenAPI document is generated by starting the host) — loud, but
    // far from its cause: the message names a revision mismatch, not which module's declarations moved.
    // The NAME carries BOTH changes, and getting there took a correction worth recording. Organization
    // becomes V11 from main. Accounting keeps V10 — availability does not bump an owner's contract
    // version, and the provider really still reports "1.0". But that argument is about the VERSION
    // NUMBER and says nothing about the `Published` prefix, which is this file's separate AVAILABILITY
    // axis: `…AndSalesV10` became `…AndPublishedSalesV10` at Sales's flip, Finance and Manufacturing
    // likewise. Logistics gains `Published` at its availability flip; Purchasing carries `Published`
    // because P01 publishes all eight of its 1.3 declarations. Accounting
    // gains `Published` at the flip this row performs. Applying the version rule to the prefix question
    // would have left the name asserting "Accounting staged" about a host serving six live screens.
    // The prefix marks the FLIP, not wholeness: the journal detail is still staged here, which the
    // registration test pins as an exact set rather than a count.
    // Derived from the MERGE TREE's providers, not from either branch, and calibrated on BOTH halves
    // before the value was trusted: all eight of main's Organization 1.1 rows reproduce byte-for-byte
    // with Accounting staged (the old half), and the same ACC-V0c model placed back at Organization 1.0
    // reproduces this branch's own sha256:OOlEtnDx… (the new half, which no accepted constant covered).
    // ACC-V1: Accounting 1.1 adds the published normal-draft create, own-detail and approval routes.
    // ACC-REV-UI phase 1: Accounting 1.3 publishes the M-04 journal detail and M-06 trial balance and declares the
    // staged reversal propose control and approval route. This row and the Inventory row after it were re-derived by
    // ShellCapabilityRegistry.ComputeCandidateRevision over main c1837a44's providers with this change's Accounting 1.3.
    // DEC-SAL-003: re-derived the same way over main aaea20d0's providers with Sales 1.2 (SalesV11 -> SalesV12).
    // MFG-PLAN-EDIT: re-derived over main 0b6a68cd's providers with Manufacturing 1.4 (ManufacturingV13 -> V14).
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV12PublishedPurchasingV14AndPublishedAccountingV13 =
        "sha256:FXO3HWwrMUr6_FUEStu6vPlh8Jaid57r5SRDyBE6Poc";
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14AndPublishedAccountingV13 =
        "sha256:ogZHB2SN1LdJDjhsVrFBRZtrtVwNlhn9hXvHgWBwD4Y";
    // AT-021 + ACC-V0b: the same Sales 1.6 tuple continues through Accounting 1.3.
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14AndPublishedAccountingV13 =
        "sha256:liRJER5kPU40Jc8pqyMNxL-DqVC-XWrmQOI47aghe0g";

    // R14: Inventory 1.0 publishes one opening-stock workspace after the existing normal tuple.
    // The capability reuses the accepted company-wide permission; publication grants nothing.
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV11ProductV10PublishedSalesV11PublishedFinanceV12PublishedManufacturingV12PublishedLogisticsV10PublishedPurchasingV13PublishedAccountingV10AndPublishedInventoryV10 =
        "sha256:F_6PIbW9hd2rjxL51mSza2t_1V1p8ibbjj7PoN2LQUU";

    // ST-01 / S04 allocation: Inventory 1.2 publishes the stock-position read and its physical-allocation
    // child control beside the opening-stock workspace. The control preserves the parent's route/context/scope
    // and adds only the existing write permission; publication grants nothing.
    // R19: Inventory 1.3 adds the label split/merge control (inventory.label-transformation.record) under the stock
    // positions route. Re-derived by ShellCapabilityRegistry.ComputeCandidateRevision over main b3c45bf7's providers
    // with this change's Inventory 1.3 provider.
    // DEC-SAL-003: re-derived the same way over main aaea20d0's providers with Sales 1.2 (SalesV11 -> SalesV12).
    // AT-037: re-derived over main 9ebb9916's providers (Organization 1.3) with Inventory 1.4 (two stock-count
    // controls; InventoryV13 -> V14).
    // MFG-PLAN-EDIT: re-derived over main 0b6a68cd's providers with Manufacturing 1.4 (ManufacturingV13 -> V14).
    // Stock scrap: re-derived over main 4ea83fa4's providers with Inventory 1.5 (two ST-01 scrap controls;
    // InventoryV14 -> V15).
    // Label classification: independently re-derived over 5caae87f's providers with Inventory 1.6 (one ST-01
    // child control; InventoryV15 -> V16). Both derivations first reproduced the reviewed V15 value exactly.
    // AT-021: re-derived with the price-floor control in Sales 1.6 while retaining Inventory 1.6.
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV12PublishedPurchasingV14PublishedAccountingV13AndPublishedInventoryV16 =
        "sha256:NiwMBdtHa3SyUDQJAzI_ZDg8h3WIj7KeF_1piiD2ON8";
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14PublishedAccountingV13AndPublishedInventoryV16 =
        "sha256:k_hMHg7I4fVXYCl3RnpA1AG6iYVCmEp29nFswYuN9vw";
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14PublishedAccountingV13AndPublishedInventoryV16 =
        "sha256:odPyp9bzgEhmKfm1TE5y2A9YdrWrpabftJv85dQn7OY";
    // ST-01 Product display: Inventory 1.7 requires the existing Product operational-selection read on the route
    // and every child control. Re-derived from the same full normal-host provider tuple; publication grants nothing.
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14PublishedAccountingV13AndPublishedInventoryV17 =
        "sha256:paopfRcQvL3LCh5FuAUunnp6UnTnTBFTri9iotKcT_o";

    // SY-01/SY-02 (read-only users-and-permissions view): Identity 1.0 joins the normal host with two screens on
    // identity.assignments.read. Only this new full-host row is added; every earlier row keeps its provider set and value.
    // Derived by ShellCapabilityRegistry.ComputeCandidateRevision over main fec92de9's providers plus Identity 1.0.
    // Stock scrap: re-derived over main 4ea83fa4's providers with Inventory 1.5 (InventoryV14 -> V15).
    // Label classification: independently re-derived over 5caae87f's providers with Inventory 1.6, preserving
    // Identity 1.0 and every disposable authority unchanged.
    // AT-021: re-derived with the price-floor control in Sales 1.6 while retaining Inventory 1.6 and Identity 1.0.
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV12PublishedPurchasingV14PublishedAccountingV13PublishedInventoryV16AndPublishedIdentityV10 =
        "sha256:PoQhy7tk9sQ6GernEV-AdBlBwNx1nIrBG0CDHJMYOYQ";
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14PublishedAccountingV13PublishedInventoryV16AndPublishedIdentityV10 =
        "sha256:pBfEusbEirf3XEsRBzPKYpcv35kt3TAFRZyMv9v2mY8";
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14PublishedAccountingV13PublishedInventoryV16AndPublishedIdentityV10 =
        "sha256:vDQf2otcUHTdIPiUgtAVmPDZo7LJUXMvIsrJeIZCGec";
    internal const string CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14PublishedAccountingV13PublishedInventoryV17AndPublishedIdentityV10 =
        "sha256:YY5yXdgL2HOugsRXyKzjwTxdKcn37JJwnmWS5AyV9w8";

    // AT-018 / AT-021: final normal host adds PRODUCT-PRICE-LISTS 1.0 and Sales 1.6 beside Inventory 1.6.
    internal const string FullNormalHostWithProductPriceListsAndSalesFloorApproval =
        "sha256:JdTctfanO3Z1XN4ORmoSJsHdtWTqXvJDdWh4B6Z0M_4";

    internal const string CounterpartyV15PublishedListCreateAndDetail =
        "sha256:UBHnivzhe_dp5f7vU7dP5n7HG-P8XYBGhxvD_OjMSnw";

    internal const string C01DisposableListOnlyV1 =
        "sha256:SXjogFbPGJiI4BxV5pC3wL7ywOTKV1fdsWR-nb5kreg";

    // The disposable "cari-list-detail" profile (BROWSER-CI stage 2, C-03): Counterparty 1.5 with
    // list (as C-01) and detail available. Computed by reproducing ComputeRevision (which yields the
    // three accepted constants exactly) and pinned by a test against ComputeCandidateRevision.
    internal const string C01DisposableCariListDetailV1 =
        "sha256:LM81M4d4tKOqR9OaKvZM_uT5HQIcoffoTMEzMFH4atg";

    // The disposable "logistics-instructions" profile (log-staged, LOG-4b, sha256:G6ic5Cbh...) is
    // retired: Logistics is published on the normal host and l04 runs there.

    // The disposable "manufacturing-order-detail" profile (mfg-staged, MFG-UI D4 #323, sha256:eZi6K5tQ…) is
    // retired: Manufacturing is published on the normal host (MFG-UI-F) and the mfg suite runs there.

    // The disposable "finance-bank-accounts-and-drafts" profile (fin-staged, FIN-UI-1b #332,
    // sha256:EsjgjVRX…) is retired: Finance is published on the normal host (FIN-UI-1c) and fin01 runs there.

    // The disposable "accounting-periods-and-journals" profile (acc-staged, ACC-V0b, sha256:meLXKsKx…) is
    // retired: Accounting is published on the normal host (ACC-V0c) and acc01 runs there. Note what the
    // two hosts do NOT share — the envelope opened all SEVEN declarations, while the normal host publishes
    // six and leaves the journal detail staged, so this value was never the published host's and could not
    // have been reused for it.
}
