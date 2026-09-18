using GoymenERP.BrowserSuiteFixtureTool.Shared;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.Fin01;

/// <summary>
/// The `fin01` seed (FİNANS, card FIN-UI-1 §9): the one thing the Finance screens cannot create — the
/// customer. A synthetic party and its company account in GDC are opened through the real counterparty
/// endpoint (CARİ's shared seed) as the canonical <c>group-bootstrap-admin</c>, whose seeded
/// SYSTEM_ADMINISTRATOR role creates parties there. No SQL, no gated writer, no Finance row: the bank
/// account is proposed and approved, and the draft saved, by the browser suite through the screens.
/// <para>
/// The seed then reads the F-04 master list through the real host as both suite actors, so the run
/// fails here — not in the browser — if their grants or the Finance routes are missing. Those reads
/// are not audited; the Cari create is (one audited business command, through the API).
/// </para>
/// </summary>
public sealed class Fin01BrowserSeed : IBrowserSuiteFixture
{
    public const string CariActor = "group-bootstrap-admin";
    public const string ProposerActor = "fin01-proposer";
    public const string ApproverActor = "fin01-approver";

    /// <summary>The GDC company of the built-in organization seed, whose approved TRY money profile Settings answers.</summary>
    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");

    private const string BankAccountMasterPath = "/api/v1/finance/bank-accounts/master";
    private const int CustomerRole = 1;

    public string Suite => "fin01";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        var customer = await CariBrowserSeed.OpenPartyWithAccountAsync(
            context, CariActor, CompanyGdc, "customer", "Tahsilat müşterisi", CustomerRole, cancellationToken);

        // Both actors reach the real F-04 read with their own grants; a fresh run has no account yet.
        foreach (var actor in new[] { ProposerActor, ApproverActor })
        {
            var master = await context.GetJsonAsync(actor, CompanyGdc, BankAccountMasterPath, cancellationToken);
            if (master.GetProperty("companyId").GetGuid() != CompanyGdc)
            {
                throw new InvalidOperationException($"The bank-account master answered {actor} for another company.");
            }
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["companyId"] = CompanyGdc.ToString("D"),
            ["partyId"] = customer.PartyId.ToString("D"),
            ["companyAccountId"] = customer.AccountId.ToString("D"),
            ["proposerActor"] = ProposerActor,
            ["approverActor"] = ApproverActor,
        };
    }
}
