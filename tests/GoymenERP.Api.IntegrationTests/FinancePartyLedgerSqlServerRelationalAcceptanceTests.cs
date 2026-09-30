using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The first-release party ledger (cari hesap ekstresi) against a REAL SQL Server. The facts are written by the real
/// receipt, reversal, portfolio and allocation services (a Record replayed, a reversal approved by another actor, a
/// cheque collected and distributed by F-03), plus an admitted open item and a recorded payment's outgoing event; the
/// ledger's own company-filtered EF queries (the date range, the UTC admission window, the reversal and collection
/// links) run in SQL. Owner ports, the open period and the service-level permissions are SENTETIK test inputs; the
/// HTTP case goes through the real host and the real permission port (which needs the two
/// <c>finance.party-ledger.*</c> names registered centrally) with synthetic roles in this test's own database.
/// </summary>
public sealed class FinancePartyLedgerSqlServerRelationalAcceptanceTests
{
    private const string Operation = "SENTETIK-operation";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Lira = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b01");
    private static readonly Guid Dollar = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b02");
    private static readonly Guid LiraBank = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b03");
    private static readonly Guid DollarBank = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b04");
    private static readonly Guid ProfileId = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b05");
    private static readonly Guid Party = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b06");
    private static readonly Guid Account = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b07");
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);

    [SqlServerFact]
    public async Task The_ledger_reads_each_recorded_fact_once_per_currency_from_zero_fenced_to_the_company_and_redacted()
    {
        await using var run = await Run.StartAsync();
        var story = await run.SeedStoryAsync();

        PartyLedgerView ledger;
        await using (var reader = run.Ledger(CompanyA))
        {
            var result = await reader.Service.ReadAuthorizedAsync(reader.Context, new PartyLedgerRequest(Party, Account, From, To),
                CancellationToken.None);
            Assert.Equal(PartyLedgerOutcome.Found, result.Outcome);
            ledger = result.Ledger!;
        }

        Assert.Equal(PartyLedgerCarryForward.NoneOpen018, ledger.CarryForward);
        var tl = ledger.Currencies.Single(currency => currency.CurrencyId == Lira);
        Assert.Equal(
            [
                (PartyLedgerEntryKind.OpenItem, "1000.00", PartyLedgerBalanceSide.Debit, "1000.00", story.OpenItem),
                (PartyLedgerEntryKind.ManualReceipt, "400.00", PartyLedgerBalanceSide.Debit, "600.00", story.Receipt),
                (PartyLedgerEntryKind.ManualReceiptReversal, "400.00", PartyLedgerBalanceSide.Debit, "1000.00", story.Reversal),
                (PartyLedgerEntryKind.ManualBankPayment, "50.00", PartyLedgerBalanceSide.Debit, "1050.00", story.Payment),
                (PartyLedgerEntryKind.CustomerInstrumentCollection, "1200.00", PartyLedgerBalanceSide.Credit, "150.00", story.Collection),
            ],
            tl.Entries.Select(entry => (entry.Kind, entry.Amount.Money!.Amount, entry.RunningBalance.Side!.Value,
                entry.RunningBalance.Amount.Money!.Amount, entry.SourceId)).ToArray());
        Assert.Equal(new DateOnly(2026, 9, 10), tl.Entries[0].OccurredOn);
        Assert.Equal(story.Receipt, tl.Entries[2].ReversesSourceId);
        Assert.Equal(story.Instrument, tl.Entries[4].InstrumentId);
        Assert.Equal(("1450.00", "1600.00", PartyLedgerBalanceSide.Credit, "150.00"), (tl.DebitTotal.Money!.Amount,
            tl.CreditTotal.Money!.Amount, tl.ClosingBalance.Side!.Value, tl.ClosingBalance.Amount.Money!.Amount));
        var usd = ledger.Currencies.Single(currency => currency.CurrencyId == Dollar);
        Assert.Equal((PartyLedgerBalanceSide.Credit, "75.50"),
            (usd.ClosingBalance.Side!.Value, usd.ClosingBalance.Amount.Money!.Amount));
        Assert.Single(usd.Entries);

        // The facts behind it, as stored: one money event per recorded fact (the Record replay added none), the draft
        // none, the allocation is its own row and no event.
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.MoneyEvents WHERE CompanyId = @c AND PartyId = @p", ("@c", CompanyA), ("@p", Party)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Allocations WHERE CompanyId = @c", ("@c", CompanyA)));

        // Close and reopen, and a narrower range: nothing before it is carried in (OPEN-018).
        await using (var again = run.Ledger(CompanyA))
        {
            var reopened = (await again.Service.ReadAuthorizedAsync(again.Context, new PartyLedgerRequest(Party, Account, From, To),
                CancellationToken.None)).Ledger!;
            Assert.Equal(Serialize(ledger), Serialize(reopened));
            var narrow = (await again.Service.ReadAuthorizedAsync(again.Context,
                new PartyLedgerRequest(Party, Account, new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 24)),
                CancellationToken.None)).Ledger!;
            var only = Assert.Single(narrow.Currencies);
            Assert.Equal([PartyLedgerEntryKind.ManualReceiptReversal, PartyLedgerEntryKind.ManualBankPayment],
                only.Entries.Select(entry => entry.Kind));
            Assert.Equal((PartyLedgerBalanceSide.Debit, "450.00"),
                (only.ClosingBalance.Side!.Value, only.ClosingBalance.Amount.Money!.Amount));
        }

        // Company B reads only its own fact for the same ids, never company A's.
        await using (var b = run.Ledger(CompanyB))
        {
            var foreign = (await b.Service.ReadAuthorizedAsync(b.Context, new PartyLedgerRequest(Party, Account, From, To),
                CancellationToken.None)).Ledger!;
            Assert.Equal(CompanyB, foreign.CompanyId);
            Assert.Equal("999.00", Assert.Single(Assert.Single(foreign.Currencies).Entries).Amount.Money!.Amount);
        }

        // Without the amount grant: every amount, total and balance (and a balance's side) is withheld.
        await using (var blind = run.Ledger(CompanyA, new DenyAmount()))
        {
            var redacted = (await blind.Service.ReadAuthorizedAsync(blind.Context, new PartyLedgerRequest(Party, Account, From, To),
                CancellationToken.None)).Ledger!;
            AssertRedacted(Serialize(redacted), expectedAmountViews: 5 * 2 + 3 + 1 * 2 + 3);
        }
    }

    [SqlServerFact]
    public async Task Over_HTTP_the_ledger_answers_only_with_fresh_ledger_grants_and_redacts_without_the_amount_grant()
    {
        await using var run = await Run.StartAsync();
        await run.SeedStoryAsync();
        await run.GrantAsync(TestUsers.CompanyA, CompanyA, FinancePermissionNames.PartyLedgerRead,
            FinancePermissionNames.PartyLedgerAmountRead);
        await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, FinancePermissionNames.PartyLedgerRead);
        using var reader = run.Factory.CreateClientFor(TestUsers.CompanyA, CompanyA);
        using var blind = run.Factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA);
        using var ungranted = run.Factory.CreateClientFor(TestUsers.CompanyB, CompanyB);
        var path = Path(From, To);

        using (var answer = await reader.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            Assert.True(answer.Headers.CacheControl is { Private: true, NoStore: true });
            var body = await answer.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("NoneOpen018", body.GetProperty("carryForward").GetString());
            var lira = body.GetProperty("currencies").EnumerateArray()
                .Single(currency => currency.GetProperty("currencyId").GetGuid() == Lira);
            Assert.Equal(5, lira.GetProperty("entries").GetArrayLength());
            Assert.Equal(("Credit", "150.00"), (lira.GetProperty("closingBalance").GetProperty("side").GetString(),
                lira.GetProperty("closingBalance").GetProperty("amount").GetProperty("money").GetProperty("amount").GetString()));
            Assert.Equal("ManualReceiptReversal", lira.GetProperty("entries")[2].GetProperty("kind").GetString());
        }

        using (var redacted = await blind.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, redacted.StatusCode);
            AssertRedacted(await redacted.Content.ReadAsStringAsync(), expectedAmountViews: 5 * 2 + 3 + 1 * 2 + 3);
        }

        using (var refused = await ungranted.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        foreach (var malformed in new[]
                 {
                     $"{FinanceEndpoints.PartyLedgerPath}?partyId={Party:D}&companyAccountId={Account:D}&from=2026-9-1&to=2026-09-30",
                     $"{FinanceEndpoints.PartyLedgerPath}?partyId={Party:D}&companyAccountId={Account:D}&from=2026-09-30&to=2026-09-01",
                     $"{FinanceEndpoints.PartyLedgerPath}?partyId={Party:D}&from=2026-09-01&to=2026-09-30",
                 })
        {
            using var bad = await reader.GetAsync(malformed);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
    }

    private static string Path(DateOnly from, DateOnly to) =>
        $"{FinanceEndpoints.PartyLedgerPath}?partyId={Party:D}&companyAccountId={Account:D}"
        + $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, FinanceJson.CreateOptions());

    private static void AssertRedacted(string json, int expectedAmountViews)
    {
        using var document = JsonDocument.Parse(json);
        var objects = Walk(document.RootElement).ToList();
        var views = objects.Where(item => item.TryGetProperty("disclosure", out _) && item.TryGetProperty("money", out _)).ToList();
        Assert.Equal(expectedAmountViews, views.Count);
        Assert.All(views, view =>
        {
            Assert.Equal("Redacted", view.GetProperty("disclosure").GetString());
            Assert.Equal(JsonValueKind.Null, view.GetProperty("money").ValueKind);
        });
        var balances = objects.Where(item => item.TryGetProperty("side", out _) && item.TryGetProperty("amount", out _)
            && !item.TryGetProperty("kind", out _)).ToList();
        Assert.NotEmpty(balances);
        Assert.All(balances, balance => Assert.Equal(JsonValueKind.Null, balance.GetProperty("side").ValueKind));
        string[] stored = ["1000.00", "400.00", "50.00", "1200.00", "600.00", "1050.00", "150.00", "1450.00", "1600.00", "75.50"];
        Assert.DoesNotContain(Scalars(document.RootElement), token => stored.Contains(token, StringComparer.Ordinal));
    }

    private static IEnumerable<JsonElement> Walk(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var nested in element.EnumerateObject().SelectMany(property => Walk(property.Value)))
            {
                yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var nested in element.EnumerateArray().SelectMany(Walk))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<string> Scalars(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Scalars(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Scalars),
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Number => [element.GetRawText()],
        _ => [],
    };

    private static CommandMetadata Key(string key) => new(key);

    private sealed record Story(Guid OpenItem, Guid Receipt, Guid Reversal, Guid Payment, Guid Collection, Guid Instrument);

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context(CompanyA, null);
            await context.Database.MigrateAsync();
            return run;
        }

        public Scope<IPartyLedgerService> Ledger(Guid company, IFinancePermissionPort? permissions = null)
        {
            var (dbContext, companyContext, _) = Tracked(company);
            var service = new PartyLedgerService(dbContext, companyContext, new FixedCurrentUser("SENTETIK-reader", company),
                permissions ?? new SyntheticPermissions());
            return new Scope<IPartyLedgerService>(dbContext, service,
                new FinanceContext(company, "SENTETIK-reader", $"ledger-{Guid.NewGuid():N}", null));
        }

        /// <summary>
        /// Company A, party account (Party, Account): an admitted open item (UTC 2026-09-10 23:30, 1000.00); a receipt
        /// recorded 09-15 (400.00) and its Record replayed; a draft never recorded; its reversal dated 09-20 approved by
        /// another actor; a recorded payment's outgoing event 09-22 (50.00); a cheque collected 09-25 (1200.00) and
        /// distributed to the open item; a USD receipt 09-12 (75.50); another party's receipt. Company B: its own
        /// receipt for the same ids (999.00).
        /// </summary>
        public async Task<Story> SeedStoryAsync()
        {
            var openItem = await SeedOpenItemAsync();
            var receipt = await RecordReceiptAsync(CompanyA, Party, Account, LiraBank, Lira, "400.00", new DateOnly(2026, 9, 15),
                replay: true);
            await RecordReceiptAsync(CompanyA, Party, Account, DollarBank, Dollar, "75.50", new DateOnly(2026, 9, 12));
            await RecordReceiptAsync(CompanyA, Guid.NewGuid(), Guid.NewGuid(), LiraBank, Lira, "888.00", new DateOnly(2026, 9, 12));
            await RecordReceiptAsync(CompanyB, Party, Account, LiraBank, Lira, "999.00", new DateOnly(2026, 9, 12));
            await using (var clerk = Receipts(CompanyA, Party, Account, "SENTETIK-clerk"))
            {
                Assert.Equal(CommandOutcome.Succeeded, (await clerk.Service.DraftSaveAsync(clerk.Context, Key("draft-only"),
                    Save(Party, Account, LiraBank, Lira, "333.00", new DateOnly(2026, 9, 16)), CancellationToken.None)).Outcome);
            }

            Guid reversal;
            await using (var maker = Reversals("SENTETIK-maker"))
            {
                var proposed = await maker.Service.ProposeAsync(maker.Context, Key("propose"),
                    new ProposeReversalRequest(receipt, "2", new DateOnly(2026, 9, 20), "SENTETIK ters kayıt"),
                    CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
                await using var checker = Reversals("SENTETIK-checker");
                var approved = await checker.Service.ApproveAsync(checker.Context, Key("approve"),
                    new ApproveReversalRequest(proposed.Resource!.Id, "1"), CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
                reversal = approved.Resource!.ReversalSourceId!.Value;
            }

            Guid instrument;
            Guid collection;
            await using (var clerk = Instruments("SENTETIK-clerk"))
            {
                var received = await clerk.Service.ReceiveAsync(clerk.Context, Key("receive"),
                    new CustomerInstrumentReceiveRequest(CustomerInstrumentKind.Cheque, Party, Account, "SENTETIK-EKSTRE-SQL-1",
                        "SENTETIK Keşideci", "SENTETIK Banka", null, new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 21),
                        new MoneyText("1200.00", Lira), null), CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, received.Outcome);
                instrument = received.Resource!.Id;
                var collected = await clerk.Service.CollectAsync(clerk.Context, Key("collect"),
                    new CustomerInstrumentCollectRequest(instrument, "1", LiraBank, new DateOnly(2026, 9, 25), null),
                    CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, collected.Outcome);
                collection = collected.Resource!.CollectionSourceId!.Value;
            }

            await using (var allocations = Allocations())
            {
                var allocated = await allocations.Service.CreateAsync(allocations.Context, Key("allocate"),
                    new CreateFinanceAllocationRequest(Guid.NewGuid(), collection, "1", openItem, "1", "1000.00",
                        "SENTETIK ekstre kapama"), CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, allocated.Outcome);
            }

            var payment = await SeedRecordedPaymentAsync();
            return new Story(openItem, receipt, reversal, payment, collection, instrument);
        }

        public async Task GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Finance ledger user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor,
                    BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"FIN-LEDGER-{roleId:N}", "Synthetic Finance ledger role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
                DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private async Task<Guid> RecordReceiptAsync(Guid company, Guid party, Guid account, Guid bank, Guid currency,
            string amount, DateOnly occurredOn, bool replay = false)
        {
            await using var clerk = Receipts(company, party, account, "SENTETIK-clerk");
            var saved = await clerk.Service.DraftSaveAsync(clerk.Context, Key($"save-{Guid.NewGuid():N}"),
                Save(party, account, bank, currency, amount, occurredOn), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
            var record = new RecordRequest(saved.Resource!.Source.Id, saved.Resource.Source.Version);
            var key = Key($"record-{Guid.NewGuid():N}");
            Assert.Equal(CommandOutcome.Succeeded, (await clerk.Service.RecordAsync(clerk.Context, key, record,
                CancellationToken.None)).Outcome);
            if (replay)
            {
                Assert.True((await clerk.Service.RecordAsync(clerk.Context, key, record, CancellationToken.None)).IsReplay);
            }

            return saved.Resource.Source.Id;
        }

        private async Task<Guid> SeedOpenItemAsync()
        {
            await using var dbContext = Context(CompanyA, null);
            var batchId = Guid.NewGuid();
            var admitted = new DateTimeOffset(2026, 9, 10, 23, 30, 0, TimeSpan.Zero);
            dbContext.OpenItemSourceBatches.Add(new FinanceOpenItemSourceBatchRow
            {
                Id = batchId, CompanyId = CompanyA, SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
                SourceVersion = "synthetic-v1", SourceChecksum = new string('f', 64),
                IdempotencyKeyHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                PayloadHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                ActorId = "synthetic-finance-open-item-fixture", ResultJson = "{}",
                CorrelationId = "synthetic-finance-open-item-fixture", AdmittedAtUtc = admitted,
            });
            var row = new FinanceOpenItemRow
            {
                Id = Guid.NewGuid(), CompanyId = CompanyA, SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
                SourceId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceVersion = "1",
                SourceBatchId = batchId, SourceBatchVersion = "synthetic-v1", SourceBatchChecksum = new string('f', 64),
                PartyId = Party, CompanyAccountId = Account, CurrencyId = Lira,
                OriginalAmountText = "1000.00", OutstandingAmountText = "1000.00", DueOn = new DateOnly(2026, 10, 10),
                Status = FinanceOpenItemStatuses.Open, Version = 1,
                CreatedBy = "synthetic-finance-open-item-fixture", CreatedAtUtc = admitted,
                UpdatedBy = "synthetic-finance-open-item-fixture", UpdatedAtUtc = admitted,
            };
            dbContext.OpenItems.Add(row);
            await dbContext.SaveChangesAsync();
            return row.Id;
        }

        /// <summary>A recorded R47 payment (maker ≠ checker, bank result confirmed) and its one outgoing money event.</summary>
        private async Task<Guid> SeedRecordedPaymentAsync()
        {
            await using var dbContext = Context(CompanyA, null);
            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            dbContext.ManualBankPayments.Add(new ManualBankPaymentRow
            {
                Id = id, CompanyId = CompanyA, Version = 3, PartyId = Party, CompanyAccountId = Account,
                TreasuryAccountId = LiraBank, CurrencyId = Lira, AmountText = "50.00", ProfileId = ProfileId.ToString("D"),
                ProfileVersion = "1", OccurredOn = new DateOnly(2026, 9, 22), Status = ManualBankPaymentStatuses.Recorded,
                PreparedBy = "SENTETIK-maker", PreparedAtUtc = now, ApprovedBy = "SENTETIK-checker", ApprovedAtUtc = now,
                RecordedBy = "SENTETIK-checker", RecordedAtUtc = now, BankResultConfirmed = true,
            });
            dbContext.OutgoingMoneyEvents.Add(new OutgoingMoneyEventRow
            {
                Id = Guid.NewGuid(), CompanyId = CompanyA, PaymentId = id, PartyId = Party, CompanyAccountId = Account,
                TreasuryAccountId = LiraBank, CurrencyId = Lira, AmountText = "50.00", ProfileId = ProfileId.ToString("D"),
                ProfileVersion = "1", OccurredOn = new DateOnly(2026, 9, 22), RecordedAtUtc = now,
                RecordedBy = "SENTETIK-checker", CorrelationId = "SENTETIK-payment",
            });
            await dbContext.SaveChangesAsync();
            return id;
        }

        private Scope<IManualReceiptService> Receipts(Guid company, Guid party, Guid account, string actor)
        {
            var (dbContext, companyContext, collector) = Tracked(company);
            var service = new ReceiptDraftService(dbContext, companyContext, new FixedCurrentUser(actor, company),
                new SyntheticPermissions(), new SyntheticBanks(company), new SyntheticParty(company, party, account),
                new SyntheticSettings(company), collector, new FinanceReceiptDraftBinding(Operation), Envelope("save"),
                TimeProvider.System, Envelope("record"), new SyntheticOpenPeriodRule());
            return new Scope<IManualReceiptService>(dbContext, service,
                new FinanceContext(company, actor, $"ledger-receipt-{Guid.NewGuid():N}", null));
        }

        private Scope<IReversalProposalService> Reversals(string actor)
        {
            var (dbContext, companyContext, collector) = Tracked(CompanyA);
            var user = new FixedCurrentUser(actor, CompanyA);
            var service = new ReversalProposalService(dbContext, companyContext, user, new SyntheticPermissions(),
                new SyntheticOpenPeriodRule(), new FinanceAllocationReversalGate(dbContext, companyContext, user), collector,
                new FinanceReversalEnvelopes(Envelope(ReversalProposalService.ProposeCommand),
                    Envelope(ReversalProposalService.ApproveCommand)), TimeProvider.System);
            return new Scope<IReversalProposalService>(dbContext, service,
                new FinanceContext(CompanyA, actor, $"ledger-reversal-{Guid.NewGuid():N}", null));
        }

        private Scope<ICustomerInstrumentService> Instruments(string actor)
        {
            var (dbContext, companyContext, collector) = Tracked(CompanyA);
            var service = new CustomerInstrumentService(dbContext, companyContext, new FixedCurrentUser(actor, CompanyA),
                new SyntheticPermissions(), new SyntheticBanks(CompanyA), new SyntheticParty(CompanyA, Party, Account),
                new SyntheticSettings(CompanyA), new SyntheticOpenPeriodRule(), collector,
                new FinanceReceiptDraftBinding(Operation),
                new CustomerInstrumentAuditEnvelopes(Envelope(FinancePermissionNames.CustomerInstrumentReceive),
                    Envelope(FinancePermissionNames.CustomerInstrumentStatusChange),
                    Envelope(FinancePermissionNames.CustomerInstrumentCollect)),
                TimeProvider.System);
            return new Scope<ICustomerInstrumentService>(dbContext, service,
                new FinanceContext(CompanyA, actor, $"ledger-instrument-{Guid.NewGuid():N}", null));
        }

        private Scope<IFinanceAllocationService> Allocations()
        {
            var (dbContext, companyContext, collector) = Tracked(CompanyA);
            var service = new FinanceAllocationService(dbContext, companyContext, new FixedCurrentUser("SENTETIK-allocator", CompanyA),
                new SyntheticPermissions(), collector, Envelope(FinanceAllocationService.CreateCommandName), TimeProvider.System);
            return new Scope<IFinanceAllocationService>(dbContext, service,
                new FinanceContext(CompanyA, "SENTETIK-allocator", $"ledger-f03-{Guid.NewGuid():N}", null));
        }

        private static DraftSaveRequest Save(Guid party, Guid account, Guid bank, Guid currency, string amount,
            DateOnly occurredOn) =>
            new DraftSaveRequest(null, null, new ReceiptPreparation(party, account, bank, new MoneyText(amount, currency),
                new ProfileReference(ProfileId.ToString("D"), "1"), occurredOn, null, "SENTETIK-EKSTRE-SQL", null));

        private static FinanceAuditEnvelope Envelope(string permission) =>
            new FinanceAuditEnvelope(permission, ["SENTETIK-FINANCE"], "Web");

        private FinanceDbContext Context(Guid company, AuditEventCollector? collector)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(Options(companyContext, collector), companyContext);
        }

        private (FinanceDbContext Context, CompanyContext Company, AuditEventCollector Collector) Tracked(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            return (new FinanceDbContext(Options(companyContext, collector), companyContext), companyContext, collector);
        }

        private DbContextOptions<FinanceDbContext> Options(CompanyContext companyContext, AuditEventCollector? collector)
        {
            var builder = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            if (collector is not null)
            {
                builder.AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext));
            }

            return builder.Options;
        }
    }

    private sealed class Scope<T>(FinanceDbContext dbContext, T service, FinanceContext context) : IAsyncDisposable
    {
        public T Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>TEST INPUT: answers "open" for any date; not a period policy.</summary>
    private sealed class SyntheticOpenPeriodRule : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant.</summary>
    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: every action except the ledger's amount read.</summary>
    private sealed class DenyAmount : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(action == FinanceAction.ReadPartyLedgerAmount
            ? PermissionDecision.Denied
            : PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: a usable TRY and a usable USD bank account of the company.</summary>
    private sealed class SyntheticBanks(Guid company) : IFinanceBankAccountReader
    {
        public Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult((accountId == LiraBank || accountId == DollarBank) && context.CompanyId == company
                ? new ReadResult<BankAccountView>(ReadOutcome.Found, new BankAccountView(accountId, company,
                    accountId == LiraBank ? Lira : Dollar, "SENTETIK ekstre bankası", true, true, "1"))
                : new ReadResult<BankAccountView>(ReadOutcome.NotFound, null));
    }

    /// <summary>SENTETIK: one active party account of the company.</summary>
    private sealed class SyntheticParty(Guid company, Guid party, Guid account) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(partyId == party && companyAccountId == account && context.CompanyId == company
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(party, account, company, true, IsPartyActive: true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    /// <summary>SENTETIK: a scale-2 money profile for any currency — a test input, not a production scale.</summary>
    private sealed class SyntheticSettings(Guid company) : ISettingsPrerequisiteProvider
    {
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MoneyProfileV1(company, currencyId, "SENTETIK", operation,
                new ProfileVersionV1(ProfileId, 1), 2, true, new DateOnly(2026, 1, 1), null));

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedCurrentUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
