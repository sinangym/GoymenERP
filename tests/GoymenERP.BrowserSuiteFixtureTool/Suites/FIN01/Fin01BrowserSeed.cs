using GoymenERP.Api.Shell;
using GoymenERP.BrowserSuiteFixtureTool.Shared;
using GoymenERP.Modules.Accounting.Infrastructure.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.FIN01;

/// <summary>
/// The `fin01` seed (FİNANS, card FIN-UI-1 §9): the prerequisites the Finance screens cannot create.
/// A synthetic party and its company account in GDC are opened through the real counterparty endpoint
/// (CARİ's shared seed), and Accounting's fixed synthetic Open period is installed through its owner
/// fixture port behind the existing complete runner gate. No raw SQL, no Finance row and no grant: the
/// bank account, draft, definitive receipt and reversal are created by the browser suite through screens.
/// <para>
/// The seed then reads the F-04 master list through the real host as both command actors, and the
/// allocation list as the checker and read-only actor, so the run
/// fails here — not in the browser — if their grants or the Finance routes are missing. Those reads
/// are not audited; the Cari create is (one audited business command, through the API).
/// </para>
/// </summary>
public sealed class Fin01BrowserSeed : IBrowserSuiteFixture
{
    public const string CariActor = "group-bootstrap-admin";
    public const string ProposerActor = "fin01-proposer";
    public const string ApproverActor = "fin01-approver";
    public const string ReaderActor = "fin01-reader";

    /// <summary>The GDC company of the built-in organization seed, whose approved TRY money profile Settings answers.</summary>
    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");

    private const string BankAccountMasterPath = "/api/v1/finance/bank-accounts/master";
    private const string AccountingPeriodsPath = "/api/v1/accounting/periods";
    private const string AllocationsPath = "/api/v1/finance/allocations";
    private const int CustomerRole = 1;

    public string Suite => "fin01";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        await using (var scope = context.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<C01E2eAccountingOpenPeriodFixtureWriter>()
                .EnsureAsync(cancellationToken);
        }

        var periodPage = await context.GetJsonAsync(ProposerActor, CompanyGdc, AccountingPeriodsPath, cancellationToken);
        var periods = periodPage.GetProperty("items");
        if (periods.GetArrayLength() != 1
            || periods[0].GetProperty("periodId").GetGuid() != AccountingOpenPeriodFixture.PeriodId
            || periods[0].GetProperty("companyId").GetGuid() != CompanyGdc
            || periods[0].GetProperty("startsOn").GetString() != "2026-09-01"
            || periods[0].GetProperty("endsOn").GetString() != "2026-09-30"
            || periods[0].GetProperty("status").GetString() != "Open")
        {
            throw new InvalidOperationException("The real Accounting period list did not return the synthetic fin01 Open period.");
        }

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

        foreach (var actor in new[] { ApproverActor, ReaderActor })
        {
            var allocations = await context.GetJsonAsync(actor, CompanyGdc, AllocationsPath, cancellationToken);
            if (allocations.GetProperty("companyId").GetGuid() != CompanyGdc)
            {
                throw new InvalidOperationException($"The allocation list answered {actor} for another company.");
            }
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["companyId"] = CompanyGdc.ToString("D"),
            ["partyId"] = customer.PartyId.ToString("D"),
            ["companyAccountId"] = customer.AccountId.ToString("D"),
            ["proposerActor"] = ProposerActor,
            ["approverActor"] = ApproverActor,
            ["readerActor"] = ReaderActor,
        };
    }
}
