using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-05 / M-03 (Muhasebe &gt; Cari Hesaplar) against a REAL SQL Server, through the real host, its DI and the real
/// permission port: the list sums each party account's still-open items per currency in exact text, an item closed by a
/// real F-03 allocation drops out, another company's rows never appear, and the account view lists the open items by due
/// date. Without the amount grant the list is refused and the view's open items are redacted; without the ledger grant
/// both are refused. Roles are synthetic, in this test's own database.
/// </summary>
public sealed partial class FinancePartyLedgerSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Over_HTTP_the_party_accounts_sum_only_open_items_per_currency_and_need_the_ledger_and_amount_grants()
    {
        await using var run = await Run.StartAsync();
        // The story's 1000.00 receivable is closed by the collected cheque's allocation; two items stay open.
        var story = await run.SeedStoryAsync();
        var receivable = await run.SeedOpenItemAsync(FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
            FinanceOpenItemDirectionCodes.Receivable, "1000.10", new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero));
        var payable = await run.SeedOpenItemAsync(FinanceOpenItemSourceKinds.ApprovedOfficialIncomingInvoiceInstallment,
            FinanceOpenItemDirectionCodes.Payable, "400.00", new DateTimeOffset(2026, 9, 11, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(FinanceOpenItemStatuses.Closed, await run.ScalarAsync<int>(
            "SELECT Status FROM finance.OpenItems WHERE Id = @id", ("@id", story.OpenItem)));

        await run.GrantAsync(TestUsers.CompanyA, CompanyA, FinancePermissionNames.PartyLedgerRead,
            FinancePermissionNames.PartyLedgerAmountRead);
        await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, FinancePermissionNames.PartyLedgerRead);
        using var reader = run.Factory.CreateClientFor(TestUsers.CompanyA, CompanyA);
        using var blind = run.Factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA);
        using var ungranted = run.Factory.CreateClientFor(TestUsers.CompanyB, CompanyB);
        var list = FinanceOpenItemReadEndpoints.PartyAccountsPath;
        var account = $"{FinanceOpenItemReadEndpoints.PartyAccountsPath}/{Party:D}/{Account:D}";

        using (var answer = await reader.GetAsync(list))
        {
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            var body = await answer.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(CompanyA, body.GetProperty("companyId").GetGuid());
            var only = Assert.Single(body.GetProperty("accounts").EnumerateArray());
            Assert.Equal((Party, Account), (only.GetProperty("partyId").GetGuid(),
                only.GetProperty("companyAccountId").GetGuid()));
            var lira = Assert.Single(only.GetProperty("currencies").EnumerateArray());
            Assert.Equal((Lira, 1, "1000.10", 1, "400.00"), (lira.GetProperty("currencyId").GetGuid(),
                lira.GetProperty("receivableCount").GetInt32(),
                lira.GetProperty("openReceivable").GetProperty("amount").GetString(),
                lira.GetProperty("payableCount").GetInt32(),
                lira.GetProperty("openPayable").GetProperty("amount").GetString()));
        }

        using (var answer = await reader.GetAsync(account))
        {
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            var body = await answer.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Visible", body.GetProperty("openItemsDisclosure").GetString());
            var items = body.GetProperty("openItems").EnumerateArray().ToList();
            // Both fall due on the same day, so the earlier admitted payable comes first.
            Assert.Equal([payable, receivable], items.Select(item => item.GetProperty("openItemId").GetGuid()).ToArray());
            Assert.Equal(["Payable", "Receivable"],
                items.Select(item => item.GetProperty("direction").GetString()!).ToArray());
            Assert.Equal("1000.10", items[1].GetProperty("outstandingAmount").GetProperty("amount").GetString());
            var tl = body.GetProperty("currencies").EnumerateArray()
                .Single(currency => currency.GetProperty("currencyId").GetGuid() == Lira);
            Assert.Equal(("1000.10", "400.00"),
                (tl.GetProperty("openReceivable").GetProperty("money").GetProperty("amount").GetString(),
                    tl.GetProperty("openPayable").GetProperty("money").GetProperty("amount").GetString()));
        }

        // Only the ledger grant: the list (which exists to show open amounts) is refused, the view hides its items.
        using (var refused = await blind.GetAsync(list))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using (var redacted = await blind.GetAsync(account))
        {
            Assert.Equal(HttpStatusCode.OK, redacted.StatusCode);
            var text = await redacted.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(text);
            Assert.Equal("Redacted", document.RootElement.GetProperty("openItemsDisclosure").GetString());
            Assert.Equal(0, document.RootElement.GetProperty("openItems").GetArrayLength());
            Assert.DoesNotContain("1000.10", text, StringComparison.Ordinal);
        }

        foreach (var path in new[] { list, account })
        {
            using var refused = await ungranted.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        foreach (var malformed in new[] { $"{list}?partyId={Party:D}", $"{account}?from=2026-09-01" })
        {
            using var bad = await reader.GetAsync(malformed);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
    }
}
