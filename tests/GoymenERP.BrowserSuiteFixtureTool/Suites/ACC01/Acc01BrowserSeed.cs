namespace GoymenERP.BrowserSuiteFixtureTool.Suites.ACC01;

/// <summary>
/// The `acc01` seed (FİNANS, Accounting owner; ACC-V0b): the suite needs NO business data. Every period is
/// proposed, approved and closed by the browser suite through the M-07 screen, and the journal and queue reads
/// (M-04, M-01) are read-only views that Accounting's HTTP surface cannot fill. So this seed writes nothing:
/// no SQL, no gated writer, no Accounting row, no audit event.
/// <para>
/// FIXTURE SCOPE. The seed only proves the stack is the one the suite expects, through the real host: it reads the
/// M-07 period list as each suite actor in the companies its grants name (preparer and approver in GDC, the
/// preparer also in GSP), so the run fails here — not in the browser — if a grant, a route or the Accounting
/// module is missing, or if a fresh database already holds a period. Those reads are not audited.
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

    public string Suite => "acc01";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        foreach (var (actor, company) in new[]
                 {
                     (PreparerActor, CompanyGdc), (ApproverActor, CompanyGdc), (PreparerActor, CompanyGsp),
                 })
        {
            var page = await context.GetJsonAsync(actor, company, PeriodsPath, cancellationToken);
            if (page.ValueKind != System.Text.Json.JsonValueKind.Object
                || !page.TryGetProperty("items", out var items) || items.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                throw new InvalidOperationException($"The Accounting period list did not answer {actor} in {company:D}.");
            }

            if (items.GetArrayLength() != 0)
            {
                throw new InvalidOperationException(
                    $"The disposable database already holds an Accounting period in {company:D}; acc01 needs a fresh one.");
            }
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["companyId"] = CompanyGdc.ToString("D"),
            ["companyBId"] = CompanyGsp.ToString("D"),
            ["preparerActor"] = PreparerActor,
            ["approverActor"] = ApproverActor,
        };
    }
}
