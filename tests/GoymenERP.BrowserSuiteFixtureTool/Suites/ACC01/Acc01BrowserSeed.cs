using System.Text.Json;
using GoymenERP.Modules.Accounting.Infrastructure.Fixtures;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.ACC01;

/// <summary>
/// The `acc01` seed (FİNANS, Accounting owner; ACC-V0b, ACC-V1 reversal UI phase 1). Every period the suite acts on is
/// proposed, approved and closed by the browser suite through the M-07 screen and the real API. The ONE thing no
/// Accounting HTTP route can produce is a posted journal, which ACC01-09 opens on the M-04 detail and totals on M-06:
/// so the seed installs Accounting's fixed SENTETIK posted journal through its owner fixture port behind the runner's
/// gate (<see cref="Acc01PostedJournalFixtureWriter"/>). That port first ensures the fixed fin01 Open period
/// (September 2026) of GDC and then posts through Accounting's own posting path; no SQL, no grant, no caller-chosen row.
/// <para>
/// FIXTURE SCOPE. The seed then proves the stack is the one the suite expects, through the real host: the M-07 period
/// list as each suite actor in the companies its grants name (preparer and approver in GDC: exactly the fixture period,
/// open; the preparer in GSP: nothing), and the M-04 journal list and detail as the preparer (exactly the fixture
/// journal). The run fails here — not in the browser — if a grant, a route, the gated port or the Accounting module is
/// missing, or if the disposable database already held anything else. Those reads are not audited.
/// </para>
/// </summary>
public sealed class Acc01BrowserSeed : IBrowserSuiteFixture
{
    public const string PreparerActor = "acc01-preparer";
    public const string ApproverActor = "acc01-approver";

    /// <summary>GDC, the company of the built-in organization seed where both actors hold their Accounting grants.</summary>
    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");

    /// <summary>GSP, the second seeded company: the preparer reads periods there too, to show company isolation.</summary>
    public static readonly Guid CompanyGsp = Guid.Parse("10000000-0000-0000-0000-000000000002");

    private const string PeriodsPath = "/api/v1/accounting/periods";
    private const string JournalsPath = "/api/v1/accounting/journals";
    private const string FinanceSourcesPath = "/api/v1/finance/manual-receipts";
    private const string NormalDraftSourcesPath = "/api/v1/accounting/normal-journal-draft-sources";

    public string Suite => "acc01";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        if (AccountingPostedJournalFixture.CompanyId != CompanyGdc)
        {
            throw new InvalidOperationException("The acc01 posted-journal fixture is not GDC's.");
        }

        await new Acc01PostedJournalFixtureWriter(context.Services).EnsureAsync(cancellationToken);
        await new Acc01NormalJournalSourceFixtureWriter(context.Services).EnsureAsync(cancellationToken);

        foreach (var (actor, company) in new[]
                 {
                     (PreparerActor, CompanyGdc), (ApproverActor, CompanyGdc), (PreparerActor, CompanyGsp),
                 })
        {
            var periods = Items(await context.GetJsonAsync(actor, company, PeriodsPath, cancellationToken),
                $"period list of {actor} in {company:D}");
            if (company == CompanyGsp)
            {
                if (periods.GetArrayLength() != 0)
                {
                    throw new InvalidOperationException($"The disposable database already holds an Accounting period in {company:D}.");
                }

                continue;
            }

            if (periods.GetArrayLength() != 1
                || periods[0].GetProperty("periodId").GetGuid() != AccountingPostedJournalFixture.PeriodId
                || periods[0].GetProperty("companyId").GetGuid() != CompanyGdc
                || periods[0].GetProperty("startsOn").GetString() != "2026-09-01"
                || periods[0].GetProperty("endsOn").GetString() != "2026-09-30"
                || periods[0].GetProperty("status").GetString() != "Open")
            {
                throw new InvalidOperationException(
                    $"The real period list did not answer {actor} exactly the fixture's open September period in GDC.");
            }
        }

        var journals = Items(await context.GetJsonAsync(PreparerActor, CompanyGdc, JournalsPath, cancellationToken),
            "journal list of the preparer in GDC");
        if (journals.GetArrayLength() != 1
            || journals[0].GetProperty("journalId").GetGuid() != AccountingPostedJournalFixture.JournalId
            || journals[0].GetProperty("kind").GetString() != "Original"
            || journals[0].GetProperty("periodId").GetGuid() != AccountingPostedJournalFixture.PeriodId
            || journals[0].GetProperty("lineCount").GetInt32() != 2
            || journals[0].GetProperty("totalDebit").GetString() != journals[0].GetProperty("totalCredit").GetString())
        {
            throw new InvalidOperationException("The real journal list did not answer exactly the fixture's posted journal.");
        }

        var detail = await context.GetJsonAsync(PreparerActor, CompanyGdc,
            $"{JournalsPath}/{AccountingPostedJournalFixture.JournalId:D}", cancellationToken);
        if (detail.GetProperty("journal").GetProperty("journalId").GetGuid() != AccountingPostedJournalFixture.JournalId
            || detail.GetProperty("lines").GetArrayLength() != 2)
        {
            throw new InvalidOperationException("The real journal read did not answer the fixture's two posted lines.");
        }

        // ACC01-10 consumes one separately gated synthetic Finance receipt, created through Finance's owner service.
        // Both reads below are real authorized HTTP and fail closed if that prerequisite/authority is absent.
        var financeRead = await context.GetJsonAsync(PreparerActor, CompanyGdc, FinanceSourcesPath, cancellationToken);
        if (financeRead.GetProperty("outcome").GetString() != "Found")
        {
            throw new InvalidOperationException("The real Finance source list did not return a Found result for acc01.");
        }
        var sourcePage = financeRead.GetProperty("resource");
        if (sourcePage.GetProperty("companyId").GetGuid() != CompanyGdc)
        {
            throw new InvalidOperationException("The acc01 Finance source list answered another company.");
        }
        var recorded = sourcePage.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("stage").GetString() == "Recorded")
            .ToArray();
        if (recorded.Length != 1)
        {
            throw new InvalidOperationException("The acc01 prerequisite must expose exactly one recorded Finance source.");
        }
        var source = recorded[0];
        var sourceId = source.GetProperty("id").GetGuid();
        var sourceNumber = source.GetProperty("sourceNumber").GetString();
        var occurredOn = source.GetProperty("occurredOn").GetString();
        var amount = source.GetProperty("amount").GetProperty("money").GetProperty("amount").GetString();
        if (sourceId == Guid.Empty || string.IsNullOrWhiteSpace(sourceNumber)
            || string.IsNullOrWhiteSpace(occurredOn) || string.IsNullOrWhiteSpace(amount))
        {
            throw new InvalidOperationException("The acc01 recorded Finance source has incomplete visible facts.");
        }

        var preparation = await context.GetJsonAsync(PreparerActor, CompanyGdc,
            $"{NormalDraftSourcesPath}/{sourceId:D}", cancellationToken);
        if (preparation.GetProperty("companyId").GetGuid() != CompanyGdc
            || preparation.GetProperty("sourceId").GetGuid() != sourceId)
        {
            throw new InvalidOperationException("The normal-journal preparation is not bound to the acc01 Finance source.");
        }
        var mappings = preparation.GetProperty("mappings").EnumerateArray().ToArray();
        var debit = mappings.Single(mapping => mapping.GetProperty("side").GetString() == "Debit");
        var credit = mappings.Single(mapping => mapping.GetProperty("side").GetString() == "Credit");
        static string MappingLabel(JsonElement mapping, string side) =>
            $"{mapping.GetProperty("accountCode").GetString()} - {mapping.GetProperty("accountName").GetString()} ({side})";

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["companyId"] = CompanyGdc.ToString("D"),
            ["companyBId"] = CompanyGsp.ToString("D"),
            ["preparerActor"] = PreparerActor,
            ["approverActor"] = ApproverActor,
            ["normalSourceLabel"] = $"{sourceNumber} - {occurredOn}",
            ["normalOccurredOn"] = occurredOn,
            ["normalAmount"] = amount,
            ["normalDebitLabel"] = MappingLabel(debit, "Borç"),
            ["normalCreditLabel"] = MappingLabel(credit, "Alacak"),
            ["openPeriodId"] = AccountingPostedJournalFixture.PeriodId.ToString("D"),
        };
    }

    private static JsonElement Items(JsonElement page, string what)
    {
        if (page.ValueKind != JsonValueKind.Object
            || !page.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"The Accounting {what} did not answer a page.");
        }

        return items;
    }
}
