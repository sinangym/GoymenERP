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
    // independently ORG (DATA ONLY cross-check, 3/3 match); pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10AndPublishedSalesV10 =
        "sha256:FL0hMs5FRAn4JQmXnstK_KxK-7Du9OaXt4QVmRmj35E";

    // ... + Finance 1.0 (#325, FIN-UI-1a) beside the STAGED Sales screens. Superseded when Sales was published
    // (#328, below); no host serves it now. Computed by the ComputeRevision reproduction and,
    // independently, by ORG (#325 5735351391, row C+O(1.0)+P+S(staged)+F); pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10SalesV10AndFinanceV10 =
        "sha256:lF5_r5EE4pCtayLb0jU4EpudtyzrbgqoRmuQP0KPsbM";

    // Counterparty 1.5 + Organization 1.0 + Product 1.0 + Sales 1.0 PUBLISHED + Finance 1.0 staged (#328 on #325):
    // the normal host once both have landed. Computed by the ComputeRevision reproduction and, independently,
    // by SİPARİŞ and ORG (#325 5735351391, row C+O(1.0)+P+S(available)+F); pinned by test.
    // Re-measured for SALES-A1 (the Sales approve control on S-04): ComputeRevision reproduction, and
    // independently ORG (DATA ONLY cross-check, 3/3 match); pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10AndFinanceV10 =
        "sha256:6rr0tooncL-nY_O5oiEVZebeitrwCRuOga92xes4o28";

    // ... + Manufacturing 1.0 staged (#323, MFG-UI) on top of #328/#325: the normal host once Manufacturing
    // registers its (staged) order detail and facts control beside published Sales and staged Finance.
    // Computed by the ComputeRevision reproduction and, independently, by SİPARİŞ and ORG; pinned by test.
    // Re-measured for SALES-A1 (the Sales approve control on S-04): ComputeRevision reproduction, and
    // independently ORG (DATA ONLY cross-check, 3/3 match); pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10PublishedSalesV10FinanceV10AndManufacturingV10 =
        "sha256:d2eqDZOVE7Im0synYyLwqRlnS1PAIAeDLqAgUhNJMkU";

    internal const string CounterpartyListCreateLinkAndDetailV15 =
        "sha256:3GiwcHV6aVuN1Y-wBzElxLJLCok2bblGc6pRruhqTx4";

    internal const string C01DisposableListOnlyV1 =
        "sha256:SXjogFbPGJiI4BxV5pC3wL7ywOTKV1fdsWR-nb5kreg";

    // The disposable "cari-list-detail" profile (BROWSER-CI stage 2, C-03): Counterparty 1.5 with
    // list (as C-01) and detail available. Computed by reproducing ComputeRevision (which yields the
    // three accepted constants exactly) and pinned by a test against ComputeCandidateRevision.
    internal const string C01DisposableCariListDetailV1 =
        "sha256:LM81M4d4tKOqR9OaKvZM_uT5HQIcoffoTMEzMFH4atg";

    // The disposable "manufacturing-order-detail" profile (BROWSER-CI mfg-staged, MFG-UI D4): the C-01
    // list-only Counterparty provider plus Manufacturing's own declarations with only their availability
    // opened (order detail and facts control). Computed by the ComputeRevision reproduction; pinned by a
    // test against ComputeCandidateRevision.
    internal const string C01DisposableManufacturingOrderDetailV1 =
        "sha256:eZi6K5tQlsm6-dSQ2XZNou1GFj1I9JOuygIJ_mQ9Lw0";
}
