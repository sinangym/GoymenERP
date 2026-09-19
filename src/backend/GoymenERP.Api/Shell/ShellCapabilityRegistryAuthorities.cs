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
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10PublishedFinanceV10AndPublishedManufacturingV10 =
        "sha256:a5pdyL0p3KsMrsfEuwKWoXa8RtqJmZRAicrLmDAWxsU";

    // LOG-4a: ... + Logistics 1.0 STAGED (the loading-instruction list L-02, new L-03 and detail L-02-DETAIL,
    // master v3 §27.8): the normal host (C+O+P+S(published)+F(published)+M(published)+L(staged)). Computed by
    // the ComputeRevision reproduction and, independently, by ORG (DATA ONLY); pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10PublishedFinanceV10PublishedManufacturingV10AndLogisticsV10 =
        "sha256:qmTCFPlD-biVAyLgFvimrWzMq9aC-5F-0o2AALnEZH8";

    // PUR-4a: ... + Purchasing 1.0 STAGED (the purchase-order list P-03, new P-04 and detail P-03-DETAIL,
    // master v3 §27.3): the normal host (C+O+P+S(pub)+F(pub)+M(pub)+L(staged)+Pu(staged)). Computed by the
    // ComputeRevision reproduction and, independently, by ORG (DATA ONLY); pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10PublishedFinanceV10PublishedManufacturingV10LogisticsV10AndPurchasingV10 =
        "sha256:gv0JwWffycJQ08eCF4U7vpHSVIyVahDWM-1XSZPNQ3I";

    internal const string CounterpartyListCreateLinkAndDetailV15 =
        "sha256:3GiwcHV6aVuN1Y-wBzElxLJLCok2bblGc6pRruhqTx4";

    internal const string C01DisposableListOnlyV1 =
        "sha256:SXjogFbPGJiI4BxV5pC3wL7ywOTKV1fdsWR-nb5kreg";

    // The disposable "cari-list-detail" profile (BROWSER-CI stage 2, C-03): Counterparty 1.5 with
    // list (as C-01) and detail available. Computed by reproducing ComputeRevision (which yields the
    // three accepted constants exactly) and pinned by a test against ComputeCandidateRevision.
    internal const string C01DisposableCariListDetailV1 =
        "sha256:LM81M4d4tKOqR9OaKvZM_uT5HQIcoffoTMEzMFH4atg";

    // The disposable "manufacturing-order-detail" profile (mfg-staged, MFG-UI D4 #323, sha256:eZi6K5tQ…) is
    // retired: Manufacturing is published on the normal host (MFG-UI-F) and the mfg suite runs there.

    // The disposable "finance-bank-accounts-and-drafts" profile (fin-staged, FIN-UI-1b #332,
    // sha256:EsjgjVRX…) is retired: Finance is published on the normal host (FIN-UI-1c) and fin01 runs there.
}
