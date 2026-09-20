namespace GoymenERP.Api.Shell;

internal static class ShellCapabilityRegistryAuthorities
{
    internal const string CounterpartyV15AndOrganizationV10 =
        "sha256:RH0MyBCLUsF7pYSmDBPtIe5Kj8HmrZN9dZUxo3pM7JI";

    // Counterparty 1.5 + Organization 1.0 + Product 1.0 (#316): the normal host's set once Product
    // publishes its four screens.
    internal const string CounterpartyV15OrganizationV10AndProductV10 =
        "sha256:vJnKkOnDipYXkO3hllw5Nc_rBrJ11OYnd6H51NOzFPM";

    // Counterparty 1.5 + Organization 1.0 + Product 1.0 + Sales 1.0 (#321): the normal host while Sales'
    // draft screens were staged. Superseded by the published set below (#328); no host serves it now.
    internal const string CounterpartyV15OrganizationV10ProductV10AndSalesV10 =
        "sha256:b6YPCP_FsrRvvQ-V6nM8bH6q96qEvqHghZEu40Ihsps";

    // Counterparty 1.5 + Organization 1.0 + Product 1.0 + Sales 1.0 PUBLISHED (#328, S03-c): the three Sales
    // draft screens available. Computed by the ComputeRevision reproduction (which re-derives every accepted
    // constant) and, independently, by ORG (5735275571) and SİPARİŞ; pinned by test.
    // Re-measured for SALES-A1 (the Sales approve control on S-04): ComputeRevision reproduction, and
    // independently re-checked (ORG DATA ONLY); the control keeps its parent's context; pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10AndPublishedSalesV10 =
        "sha256:-dDuvuDtzdcHDKQC8JvJhuOnrJ8YVu39xEyWr7eE4iY";

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

    // FIN-UI-1c: Counterparty 1.5 + Organization 1.0 + Product 1.0 + Sales 1.0 PUBLISHED (with the SALES-A1
    // control) + Finance 1.0 PUBLISHED: the six Finance declarations flip only their availability after the
    // FIN-01 acceptance (#332, run 35444049175). Computed by the ComputeRevision reproduction and, independently,
    // by ORG (DATA ONLY); pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10AndPublishedFinanceV10 =
        "sha256:TrLsCbRsjzE9MZ5jZEzSH0vZ7GVU3iwnvtrZeLt2o8Y";

    // FIN-UI-1c: ... + Manufacturing 1.0 staged: the normal host (C+O+P+S(published)+F(published)+M(staged)).
    // Computed by the ComputeRevision reproduction and, independently, by ORG (DATA ONLY); pinned by test.
    // Superseded when Manufacturing was published (MFG-UI-F, below); no host serves it now.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10PublishedFinanceV10AndManufacturingV10 =
        "sha256:90oJBLWkzXFLflvbeE_AAOrVoe9ZowFEqClCaq_tel4";

    // MFG-UI-F: ... + Manufacturing 1.0 PUBLISHED (the order detail and its facts control flip only their
    // availability): the normal host (C+O+P+S(published)+F(published)+M(published)). Computed by the
    // ComputeRevision reproduction and, independently, by ORG and SİPARİŞ (DATA ONLY); pinned by test.
    // Re-measured for MFG-ORD (the MFG-02 order list route and the close control of the order screen:
    // four Manufacturing declarations, the control keeping its parent's route, screen, precedence,
    // context and scope): ComputeRevision reproduction, calibrated against the three constants this
    // file already served, and independently re-checked (DATA ONLY); pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10PublishedFinanceV10AndPublishedManufacturingV10 =
        "sha256:tc_PnhgkBI6_EfkP2xl3JKc64Wrd7YYESEg4HnLHbPA";

    // LOG-4a: ... + Logistics 1.0 STAGED (the loading-instruction list L-02, new L-03 and detail L-02-DETAIL,
    // master v3 §27.8): the normal host (C+O+P+S(published)+F(published)+M(published)+L(staged)). Computed by
    // the ComputeRevision reproduction and, independently, by ORG (DATA ONLY); pinned by test.
    // Re-measured for MFG-ORD (the MFG-02 order list route and the close control of the order screen:
    // four Manufacturing declarations, the control keeping its parent's route, screen, precedence,
    // context and scope): ComputeRevision reproduction, calibrated against the three constants this
    // file already served, and independently re-checked (DATA ONLY); pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10PublishedFinanceV10PublishedManufacturingV10AndLogisticsV10 =
        "sha256:LlNH5-bouF79CKxrcrdWZQEQMEVRma5z1rtR0G452C4";

    // PUR-4a: ... + Purchasing 1.0 STAGED (the purchase-order list P-03, new P-04 and detail P-03-DETAIL,
    // master v3 §27.3): the normal host (C+O+P+S(pub)+F(pub)+M(pub)+L(staged)+Pu(staged)). Computed by the
    // ComputeRevision reproduction and, independently, by ORG (DATA ONLY); pinned by test.
    // Re-measured for MFG-ORD (the MFG-02 order list route and the close control of the order screen:
    // four Manufacturing declarations, the control keeping its parent's route, screen, precedence,
    // context and scope): ComputeRevision reproduction, calibrated against the three constants this
    // file already served, and independently re-checked (DATA ONLY); pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10PublishedFinanceV10PublishedManufacturingV10LogisticsV10AndPurchasingV10 =
        "sha256:FTHNU_KsDs6eQMkaYavYF6Jgu0z5kNxq9SZNZPMQkC0";

    // ACC-V0b: ... + Accounting 1.0 STAGED (the period list M-07 and its propose screen, the approve and
    // close controls of that list, the journal list M-04 and its detail, the posting queues M-01): the normal
    // host (C+O+P+S(pub)+F(pub)+M(pub)+L(staged)+Pu(staged)+Acc(staged)). Computed by the ComputeRevision
    // reproduction, independently re-checked (DATA ONLY); pinned by test.
    // RE-MEASURED for ACC-V0c: six of Accounting's seven declarations become available (the period
    // list and its propose screen, the journal list, the posting queues — and the approve and close
    // CONTROLS, which are `periods with { … }` and therefore inherit the list's availability, so the
    // count is six even though nothing says available: true on their own lines). M-04-DETAIL stays
    // staged: acc01 never opened a journal, so that route has no acceptance to stand on. The earlier
    // value (sha256:oQPU6TLY…) is SUPERSEDED — it describes a host that no longer exists.
    // Two BLIND derivations from the source declarations, CARİ and ORG, neither having seen the
    // other's value, both calibrated first against the constants this tree already serves.
    // RE-MEASURED after MFG-ORD (#360) landed: Manufacturing now declares FOUR capabilities, not two, so the
    // earlier value of this row (sha256:kxDfAaAN…) is SUPERSEDED and describes a host that no longer exists.
    // Its earlier independent check is superseded with it — a match against the old value is not evidence.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10PublishedFinanceV10PublishedManufacturingV10LogisticsV10PurchasingV10AndAccountingV10 =
        "sha256:OOlEtnDx45fVDzs98US4T6h2k8vR_BaJ9rrRQMMFvv0";

    internal const string CounterpartyListCreateLinkAndDetailV15 =
        "sha256:3GiwcHV6aVuN1Y-wBzElxLJLCok2bblGc6pRruhqTx4";

    internal const string C01DisposableListOnlyV1 =
        "sha256:SXjogFbPGJiI4BxV5pC3wL7ywOTKV1fdsWR-nb5kreg";

    // The disposable "cari-list-detail" profile (BROWSER-CI stage 2, C-03): Counterparty 1.5 with
    // list (as C-01) and detail available. Computed by reproducing ComputeRevision (which yields the
    // three accepted constants exactly) and pinned by a test against ComputeCandidateRevision.
    internal const string C01DisposableCariListDetailV1 =
        "sha256:LM81M4d4tKOqR9OaKvZM_uT5HQIcoffoTMEzMFH4atg";

    // The disposable "accounting-periods-and-journals" profile (BROWSER-CI acc-staged, ACC-V0b): the C-01
    // list-only Counterparty provider plus Accounting's own seven declarations with only their availability
    // opened (M-07 list and propose, its approve and close controls, M-04 list and detail, M-01). Computed by
    // the ComputeRevision reproduction, independently re-checked (DATA ONLY); pinned by a test against
    // ComputeCandidateRevision.
    internal const string C01DisposableAccountingPeriodsAndJournalsV1 =
        "sha256:meLXKsKx_dZtvOZvMdkrHM6Ih-U8TeadJumWY4vfcuY";
    // The disposable "logistics-instructions" profile (BROWSER-CI log-staged, LOG-4b): the C-01 list-only Counterparty
    // provider plus Logistics's own three declarations with only their availability opened (L-02 list, L-03 new,
    // L-02-DETAIL). Computed by the ComputeRevision reproduction; pinned by a test against ComputeCandidateRevision.
    internal const string C01DisposableLogisticsInstructionsV1 =
        "sha256:G6ic5Cbh7NH1-kw430fr2eeOFViX0QGiv7w1qosZpbI";

    // The disposable "manufacturing-order-detail" profile (mfg-staged, MFG-UI D4 #323, sha256:eZi6K5tQ…) is
    // retired: Manufacturing is published on the normal host (MFG-UI-F) and the mfg suite runs there.

    // The disposable "finance-bank-accounts-and-drafts" profile (fin-staged, FIN-UI-1b #332,
    // sha256:EsjgjVRX…) is retired: Finance is published on the normal host (FIN-UI-1c) and fin01 runs there.
}
