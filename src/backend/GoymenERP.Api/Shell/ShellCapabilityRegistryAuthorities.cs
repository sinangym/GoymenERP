namespace GoymenERP.Api.Shell;

internal static class ShellCapabilityRegistryAuthorities
{
    /// <summary>
    /// Counterparty 1.5 plus Organization 1.1 — the ORG-01/ORG-02 routes, their two
    /// controls and the ORG-03 work-center list route. Recomputed, not edited: the
    /// value is <see cref="ShellCapabilityRegistry.Revision"/> over that exact
    /// provider pair, and the registry rejects the host at startup if the two differ.
    /// </summary>
    internal const string CounterpartyV15AndOrganizationV11 =
        "sha256:JGyoGF1wX_PNfnq4vY17VltFXS_pTDueuQdFr_gtOds";

    internal const string CounterpartyListCreateLinkAndDetailV15 =
        "sha256:3GiwcHV6aVuN1Y-wBzElxLJLCok2bblGc6pRruhqTx4";

    internal const string C01DisposableListOnlyV1 =
        "sha256:SXjogFbPGJiI4BxV5pC3wL7ywOTKV1fdsWR-nb5kreg";
}
