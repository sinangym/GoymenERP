namespace GoymenERP.Api.Shell;

internal static class ShellCapabilityRegistryAuthorities
{
    internal const string CounterpartyV15AndOrganizationV10 =
        "sha256:RH0MyBCLUsF7pYSmDBPtIe5Kj8HmrZN9dZUxo3pM7JI";

    // Counterparty 1.5 + Organization 1.0 + Product 1.0 (#316): the normal host's set once Product
    // publishes its four screens.
    internal const string CounterpartyV15OrganizationV10AndProductV10 =
        "sha256:vJnKkOnDipYXkO3hllw5Nc_rBrJ11OYnd6H51NOzFPM";

    // Counterparty 1.5 + Organization 1.0 + Product 1.0 + Sales 1.0 (#321): the normal host once Sales
    // registers its (staged) draft screens. Computed by the ComputeRevision reproduction, pinned by test.
    internal const string CounterpartyV15OrganizationV10ProductV10AndSalesV10 =
        "sha256:b6YPCP_FsrRvvQ-V6nM8bH6q96qEvqHghZEu40Ihsps";

    internal const string CounterpartyListCreateLinkAndDetailV15 =
        "sha256:3GiwcHV6aVuN1Y-wBzElxLJLCok2bblGc6pRruhqTx4";

    internal const string C01DisposableListOnlyV1 =
        "sha256:SXjogFbPGJiI4BxV5pC3wL7ywOTKV1fdsWR-nb5kreg";

    // The disposable "cari-list-detail" profile (BROWSER-CI stage 2, C-03): Counterparty 1.5 with
    // list (as C-01) and detail available. Computed by reproducing ComputeRevision (which yields the
    // three accepted constants exactly) and pinned by a test against ComputeCandidateRevision.
    internal const string C01DisposableCariListDetailV1 =
        "sha256:LM81M4d4tKOqR9OaKvZM_uT5HQIcoffoTMEzMFH4atg";
}
