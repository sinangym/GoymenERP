using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// DEC-FIN-016 first release (customer cheque / note portfolio) against a REAL SQL Server: receive into the portfolio
/// with no money effect, reopen, deposit, collect ONCE — one recorded receipt source and one money event at the face
/// value, read again after the scope is closed — replay, stale retry, a race of two collections, company isolation,
/// the database's own state / identity / collection-source rules, F-03 distribution to the customer's open item, the
/// FIN-T06 refusal of the collection source, and the 51051 rollback refusal. Owner ports, the open period and the
/// permissions are SENTETIK test inputs (the real permission port is the HTTP case's); no journal is made.
/// </summary>
public sealed class FinanceCustomerInstrumentSqlServerRelationalAcceptanceTests
{
    private const string Operation = "SENTETIK-operation";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a91");
    private static readonly Guid ProfileId = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a92");

    [SqlServerFact]
    public async Task A_customer_cheque_is_received_reopened_deposited_and_collected_once_at_face_value()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);

        CustomerInstrumentCommandResult received;
        await using (var a = run.Instruments(CompanyA, owners))
        {
            received = await a.Service.ReceiveAsync(a.Context, Key("receive"), Receive(owners), CancellationToken.None);
        }

        Assert.Equal((CommandOutcome.Succeeded, CustomerInstrumentStatus.InPortfolio), (received.Outcome, received.Resource!.Status));
        var id = received.Resource.Id;
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));

        // Close and reopen: a new scope reads the stored instrument; company B reads nothing.
        await using (var reader = run.Instruments(CompanyA, owners, correlation: "SENTETIK-instrument-reopen"))
        {
            var detail = await reader.Service.DetailAuthorizedAsync(reader.Context, id, CancellationToken.None);
            Assert.Equal((ReadOutcome.Found, "1000.00", "SENTETIK-CEK-SQL-1"), (detail.Outcome,
                detail.Resource!.Instrument.Amount.Money!.Amount, detail.Resource.Instrument.SerialNumber));
            Assert.Contains(CustomerInstrumentAction.Collect, detail.Resource.AvailableActions);
        }

        await using (var b = run.Instruments(CompanyB, owners))
        {
            Assert.Equal(ReadOutcome.NotFound, (await b.Service.DetailAuthorizedAsync(b.Context, id, CancellationToken.None)).Outcome);
            Assert.Empty((await b.Service.ListAuthorizedAsync(b.Context, null, CancellationToken.None)).Resource!.Items);
        }

        await using (var a = run.Instruments(CompanyA, owners))
        {
            var deposited = await a.Service.ChangeStatusAsync(a.Context, Key("deposit"),
                new CustomerInstrumentStatusChangeRequest(id, "1", CustomerInstrumentTransition.DepositForCollection,
                    new DateOnly(2026, 9, 25), owners.Bank, null), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, "2"), (deposited.Outcome, deposited.Resource!.Version));
        }

        var collect = new CustomerInstrumentCollectRequest(id, "2", owners.Bank, new DateOnly(2026, 10, 16), null);
        CustomerInstrumentCommandResult collected;
        await using (var a = run.Instruments(CompanyA, owners))
        {
            collected = await a.Service.CollectAsync(a.Context, Key("collect"), collect, CancellationToken.None);
        }

        Assert.Equal((CommandOutcome.Succeeded, CustomerInstrumentStatus.Collected), (collected.Outcome, collected.Resource!.Status));
        var sourceId = collected.Resource.CollectionSourceId!.Value;
        await using (var a = run.Instruments(CompanyA, owners, correlation: "SENTETIK-instrument-retry"))
        {
            var replay = await a.Service.CollectAsync(a.Context, Key("collect"), collect, CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, run.Correlation), (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId));
            Assert.Equal(CommandOutcome.VersionConflict,
                (await a.Service.CollectAsync(a.Context, Key("collect-stale"), collect, CancellationToken.None)).Outcome);
        }

        // ONE money event, of the collection's own kind, at the face value, for the customer's account and the bank.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal((3, "1000.00"), (
            await run.ScalarAsync<int>("SELECT Kind FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", sourceId)),
            await run.ScalarAsync<string>("SELECT AmountText FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", sourceId))));
        Assert.Equal((owners.Account, owners.Bank), (
            await run.ScalarAsync<Guid>("SELECT CompanyAccountId FROM finance.MoneyEvents"),
            await run.ScalarAsync<Guid>("SELECT TreasuryAccountId FROM finance.MoneyEvents")));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Stage FROM finance.ReceiptDrafts WHERE Id = @s", ("@s", sourceId)));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerInstrumentMovements WHERE InstrumentId = @i", ("@i", id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.customer-instrument.collected.v1' "
            + "AND EntityId = @i AND SourceChannel = N'Web'", ("@i", id.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.manual-receipt.recorded.v1' AND EntityId = @s "
            + "AND AfterJson LIKE N'%\"channel\":\"CustomerInstrument\"%'", ("@s", sourceId.ToString("D"))));

        // The database keeps the rules whatever writes it: a collected cheque names its source, a source is collected
        // once, one instrument per identity (a note, without a bank, included).
        var noSource = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE finance.CustomerInstruments SET CollectionSourceId = NULL WHERE Id = @i; SELECT 1", ("@i", id)));
        Assert.Contains("CK_CustomerInstruments_State", noSource.Message, StringComparison.Ordinal);
        var note = await run.ReceiveAsync(owners, Receive(owners) with
        {
            Kind = CustomerInstrumentKind.PromissoryNote, SerialNumber = "SENTETIK-SNT-SQL-1", BankName = null, BranchName = null,
        });
        var secondSource = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE finance.CustomerInstruments SET Status = 3, TreasuryAccountId = @b, CollectionSourceId = @s, Version = 2 "
            + "WHERE Id = @n; SELECT 1", ("@b", owners.Bank), ("@s", sourceId), ("@n", note.Id)));
        Assert.Contains("UX_CustomerInstruments_CollectionSource", secondSource.Message, StringComparison.Ordinal);
        var duplicateNote = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "INSERT INTO finance.CustomerInstruments (Id, CompanyId, Version, Kind, Status, PartyId, CompanyAccountId, "
            + "SerialNumber, Drawer, BankName, DueOn, ReceivedOn, CurrencyId, AmountText, ProfileId, ProfileVersion, "
            + "StatusChangedOn, ReceivedBy, ReceivedAtUtc, UpdatedBy, UpdatedAtUtc) "
            + "SELECT NEWID(), CompanyId, 1, Kind, 1, PartyId, CompanyAccountId, SerialNumber, Drawer, BankName, DueOn, "
            + "ReceivedOn, CurrencyId, AmountText, ProfileId, ProfileVersion, StatusChangedOn, ReceivedBy, ReceivedAtUtc, "
            + "UpdatedBy, UpdatedAtUtc FROM finance.CustomerInstruments WHERE Id = @n; SELECT 1", ("@n", note.Id)));
        Assert.Contains("UX_CustomerInstruments_Identity", duplicateNote.Message, StringComparison.Ordinal);

        // The next step: the collection is distributed (F-03) at its face value to the customer's open item, which closes.
        var openItem = await run.SeedOpenItemAsync(CompanyA, owners.Account, "1000.00");
        await using (var allocation = run.Allocations(CompanyA))
        {
            var allocated = await allocation.Service.CreateAsync(allocation.Context, Key("allocate"),
                new CreateFinanceAllocationRequest(Guid.NewGuid(), sourceId, "1", openItem.Id, openItem.Version, "1000.00",
                    "SENTETIK çek tahsili kapama"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, "1000.00"), (allocated.Outcome, allocated.Resource!.Amount));
        }

        Assert.Equal(("0.00", 2), (
            await run.ScalarAsync<string>("SELECT OutstandingAmountText FROM finance.OpenItems WHERE Id = @i", ("@i", openItem.Id)),
            await run.ScalarAsync<int>("SELECT Status FROM finance.OpenItems WHERE Id = @i", ("@i", openItem.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));

        // The reverse flow after collection is not decided: FIN-T06 refuses the collection source and writes nothing.
        await using (var maker = run.Reversals(CompanyA))
        {
            var proposal = await maker.Service.ProposeAsync(maker.Context, Key("reverse"),
                new ProposeReversalRequest(sourceId, "1", new DateOnly(2026, 10, 17), "SENTETIK ters kayıt"), CancellationToken.None);
            Assert.Equal("finance_reversal_source_not_original_receipt", proposal.Issues.Single().Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReversalProposals"));
    }

    [SqlServerFact]
    public async Task Two_collections_of_one_instrument_at_once_make_one_money_event()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var instrument = await run.ReceiveAsync(owners, Receive(owners));

        async Task<CustomerInstrumentCommandResult> CollectAsync(string key, string actor)
        {
            await using var scope = run.Instruments(CompanyA, owners, actor: actor);
            return await scope.Service.CollectAsync(scope.Context, Key(key),
                new CustomerInstrumentCollectRequest(instrument.Id, "1", owners.Bank, new DateOnly(2026, 10, 16), null),
                CancellationToken.None);
        }

        var results = await Task.WhenAll(
            Task.Run(() => CollectAsync("race-a", "SENTETIK-collector-a")),
            Task.Run(() => CollectAsync("race-b", "SENTETIK-collector-b")));

        Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded && !result.IsReplay);
        Assert.Single(results, result => result.Outcome is CommandOutcome.VersionConflict or CommandOutcome.ValidationFailed);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.CustomerInstruments"));
    }

    /// <summary>#497 P2-1: without the amount grant, no answer — command, replay, list, detail — carries the face value.</summary>
    [SqlServerFact]
    public async Task Without_the_amount_grant_no_portfolio_answer_carries_an_amount()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var answers = new List<string>();
        await using (var blind = run.Instruments(CompanyA, owners, permissions: new DenyAmount()))
        {
            var received = await blind.Service.ReceiveAsync(blind.Context, Key("receive"), Receive(owners), CancellationToken.None);
            answers.Add(Serialize(received));
            answers.Add(Serialize(await blind.Service.ReceiveAsync(blind.Context, Key("receive"), Receive(owners), CancellationToken.None)));
            var collect = new CustomerInstrumentCollectRequest(received.Resource!.Id, "1", owners.Bank, new DateOnly(2026, 10, 16), null);
            answers.Add(Serialize(await blind.Service.CollectAsync(blind.Context, Key("collect"), collect, CancellationToken.None)));
            answers.Add(Serialize(await blind.Service.CollectAsync(blind.Context, Key("collect"), collect, CancellationToken.None)));
            answers.Add(Serialize((await blind.Service.ListAuthorizedAsync(blind.Context, null, CancellationToken.None)).Resource!));
            answers.Add(Serialize((await blind.Service.DetailAuthorizedAsync(blind.Context, received.Resource.Id,
                CancellationToken.None)).Resource!));
        }

        string[] stored = ["1000.00", "1000", "1000.0"];
        Assert.All(answers, answer =>
        {
            using var document = JsonDocument.Parse(answer);
            var views = AmountViews(document.RootElement).ToList();
            Assert.NotEmpty(views);
            Assert.All(views, view =>
            {
                Assert.Equal("Redacted", view.GetProperty("disclosure").GetString());
                Assert.Equal(JsonValueKind.Null, view.GetProperty("money").ValueKind);
            });
            Assert.DoesNotContain(ScalarTokens(document.RootElement), token => stored.Contains(token, StringComparer.Ordinal));
        });
        Assert.Equal("1000.00", await run.ScalarAsync<string>("SELECT AmountText FROM finance.MoneyEvents"));
    }

    /// <summary>
    /// DEC-FIN-016 risk tracking (user decision 2026-09-27) on a real SQL Server: the company-filtered point-in-time
    /// query (instruments received on or before the day, each with its last movement dated on or before it — the
    /// correlated NOT EXISTS runs in SQL) is bucketed by state and due date per customer account and currency, at the
    /// currency scale; on a PAST day an instrument received later is absent and one dishonoured, collected or returned
    /// later is still open exposure; company B reads none of it; without the amount grant every amount is withheld.
    /// </summary>
    [SqlServerFact]
    public async Task The_risk_view_buckets_a_company_s_instruments_by_state_and_due_date_and_redacts_every_amount()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var other = owners with { Party = Guid.NewGuid(), Account = Guid.NewGuid() };
        var notDue = await run.ReceiveAsync(owners, Receive(owners) with { SerialNumber = "SENTETIK-RISK-1" });
        await run.ReceiveAsync(owners, Receive(owners) with
        {
            SerialNumber = "SENTETIK-RISK-2", DueOn = new DateOnly(2026, 9, 25), Money = new MoneyText("250.50", Currency),
        });
        var bounced = await run.ReceiveAsync(owners, Receive(owners) with
        {
            SerialNumber = "SENTETIK-RISK-3", Money = new MoneyText("300.00", Currency),
        });
        await run.ReceiveAsync(other, Receive(other) with { SerialNumber = "SENTETIK-RISK-4" });
        // Received after every day asked below: never counted.
        await run.ReceiveAsync(owners, Receive(owners) with
        {
            SerialNumber = "SENTETIK-RISK-5", ReceivedOn = new DateOnly(2026, 10, 2), Money = new MoneyText("50.00", Currency),
        });
        var returned = await run.ReceiveAsync(owners, Receive(owners) with
        {
            SerialNumber = "SENTETIK-RISK-6", Money = new MoneyText("75.00", Currency),
        });
        await using (var a = run.Instruments(CompanyA, owners))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await a.Service.ChangeStatusAsync(a.Context, Key("risk-bounce"),
                new CustomerInstrumentStatusChangeRequest(bounced.Id, "1", CustomerInstrumentTransition.Dishonour,
                    new DateOnly(2026, 9, 26), null, null), CancellationToken.None)).Outcome);
            Assert.Equal(CommandOutcome.Succeeded, (await a.Service.CollectAsync(a.Context, Key("risk-collect"),
                new CustomerInstrumentCollectRequest(notDue.Id, "1", owners.Bank, new DateOnly(2026, 9, 27), null),
                CancellationToken.None)).Outcome);
            Assert.Equal(CommandOutcome.Succeeded, (await a.Service.ChangeStatusAsync(a.Context, Key("risk-return"),
                new CustomerInstrumentStatusChangeRequest(returned.Id, "1", CustomerInstrumentTransition.ReturnToCustomer,
                    new DateOnly(2026, 9, 30), null, null), CancellationToken.None)).Outcome);
        }

        var asOf = new DateOnly(2026, 10, 1);
        CustomerInstrumentRiskView company;
        CustomerInstrumentRiskView customer;
        CustomerInstrumentRiskView past;
        await using (var reader = run.Instruments(CompanyA, owners))
        {
            company = (await reader.Service.RiskAuthorizedAsync(reader.Context,
                new CustomerInstrumentRiskRequest(asOf, null, null), CancellationToken.None)).Resource!;
            customer = (await reader.Service.RiskAuthorizedAsync(reader.Context,
                new CustomerInstrumentRiskRequest(asOf, owners.Party, owners.Account), CancellationToken.None)).Resource!;
            past = (await reader.Service.RiskAuthorizedAsync(reader.Context,
                new CustomerInstrumentRiskRequest(new DateOnly(2026, 9, 25), owners.Party, owners.Account),
                CancellationToken.None)).Resource!;
        }

        // K3 5332370492 P2-1, on 2026-09-25: RISK-5 (received 2026-10-02) is absent; RISK-1 (collected 09-27), RISK-3
        // (dishonoured 09-26) and RISK-6 (returned 09-30) were all still in the portfolio; RISK-2 is due that very
        // day, so not yet overdue.
        var then = Assert.Single(past.Customers).Figures;
        Assert.Equal((4, "1625.50"), (then.InPortfolioNotDue.Count, then.InPortfolioNotDue.Amount.Money!.Amount));
        Assert.Equal((0, "0.00"), (then.InPortfolioOverdue.Count, then.InPortfolioOverdue.Amount.Money!.Amount));
        Assert.Equal((0, "0.00"), (then.Dishonoured.Count, then.Dishonoured.Amount.Money!.Amount));
        Assert.Equal((0, "0.00"), (then.Collected.Count, then.Collected.Amount.Money!.Amount));
        Assert.Equal((0, "0.00"), (then.ReturnedToCustomer.Count, then.ReturnedToCustomer.Amount.Money!.Amount));
        Assert.Equal((4, "1625.50"), (then.OpenExposure.Count, then.OpenExposure.Amount.Money!.Amount));

        Assert.Equal(2, company.Customers.Count);
        var mine = Assert.Single(customer.Customers).Figures;
        Assert.Equal((1, "250.50"), (mine.InPortfolioOverdue.Count, mine.InPortfolioOverdue.Amount.Money!.Amount));
        Assert.Equal((0, "0.00"), (mine.InPortfolioNotDue.Count, mine.InPortfolioNotDue.Amount.Money!.Amount));
        Assert.Equal((1, "75.00"), (mine.ReturnedToCustomer.Count, mine.ReturnedToCustomer.Amount.Money!.Amount));
        Assert.Equal((1, "300.00"), (mine.Dishonoured.Count, mine.Dishonoured.Amount.Money!.Amount));
        Assert.Equal((1, "1000.00"), (mine.Collected.Count, mine.Collected.Amount.Money!.Amount));
        Assert.Equal((2, "550.50"), (mine.OpenExposure.Count, mine.OpenExposure.Amount.Money!.Amount));
        var total = Assert.Single(company.Totals).Figures;
        Assert.Equal((3, "1550.50"), (total.OpenExposure.Count, total.OpenExposure.Amount.Money!.Amount));

        await using (var b = run.Instruments(CompanyB, owners))
        {
            var foreign = (await b.Service.RiskAuthorizedAsync(b.Context, new CustomerInstrumentRiskRequest(asOf, null, null),
                CancellationToken.None)).Resource!;
            Assert.Empty(foreign.Customers);
            Assert.Empty(foreign.Totals);
        }

        await using (var blind = run.Instruments(CompanyA, owners, permissions: new DenyAmount()))
        {
            var redacted = await blind.Service.RiskAuthorizedAsync(blind.Context, new CustomerInstrumentRiskRequest(asOf, null, null),
                CancellationToken.None);
            using var document = JsonDocument.Parse(Serialize(redacted.Resource!));
            var views = AmountViews(document.RootElement).ToList();
            Assert.Equal(3 * 7, views.Count);
            Assert.All(views, view =>
            {
                Assert.Equal("Redacted", view.GetProperty("disclosure").GetString());
                Assert.Equal(JsonValueKind.Null, view.GetProperty("money").ValueKind);
            });
            string[] stored = ["1000.00", "250.50", "300.00", "75.00", "550.50", "1550.50"];
            Assert.DoesNotContain(ScalarTokens(document.RootElement), token => stored.Contains(token, StringComparer.Ordinal));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    /// <summary>DEC-FIN-016: a rollback never drops a customer's cheque, its history or its collection (Finance's 51051).</summary>
    [SqlServerFact]
    public async Task The_customer_instrument_migration_rolls_back_only_while_no_instrument_is_stored()
    {
        await using var run = await Run.StartAsync();
        const string beforeInstruments = "20260927120000_AddPosCollectionFacts";

        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(beforeInstruments);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'CustomerInstruments' AND [schema_id] = SCHEMA_ID(N'finance')"));
            await context.Database.MigrateAsync();
        }

        var owners = Owners.For(CompanyA);
        await run.ReceiveAsync(owners, Receive(owners));

        await using (var context = run.Context(CompanyA))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(beforeInstruments));
            Assert.Equal(51051, error.Number);
            Assert.EndsWith("_AddCustomerInstruments", (await context.Database.GetAppliedMigrationsAsync()).Last(),
                StringComparison.Ordinal);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerInstruments"));
    }

    private static CommandMetadata Key(string key) => new(key);

    private static CustomerInstrumentReceiveRequest Receive(Owners owners) => new(
        CustomerInstrumentKind.Cheque, owners.Party, owners.Account, "SENTETIK-CEK-SQL-1", "SENTETIK Keşideci A.Ş.",
        "SENTETIK Banka", "SENTETIK Şube", new DateOnly(2026, 10, 15), new DateOnly(2026, 9, 20),
        new MoneyText("1000.00", Currency), null);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, FinanceJson.CreateOptions());

    private static IEnumerable<JsonElement> AmountViews(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("disclosure", out _) && element.TryGetProperty("money", out _))
            {
                yield return element;
            }

            foreach (var property in element.EnumerateObject())
            {
                foreach (var nested in AmountViews(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var nested in element.EnumerateArray().SelectMany(AmountViews))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Every string and number value, whole tokens only (no substring matching: ids contain digits too).</summary>
    private static IEnumerable<string> ScalarTokens(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => ScalarTokens(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(ScalarTokens),
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Number => [element.GetRawText()],
        _ => [],
    };

    private sealed record Owners(Guid Company, Guid Bank, Guid Party, Guid Account)
    {
        public static Owners For(Guid company) => new(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }

    private sealed record SavedOpenItem(Guid Id, string Version);

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = Guid.NewGuid().ToString("D");
        public string Correlation { get; } = $"dec-fin-016-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context(CompanyA);
            await context.Database.MigrateAsync();
            return run;
        }

        public FinanceDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(Options(companyContext, null), companyContext);
        }

        public Scope<ICustomerInstrumentService> Instruments(Guid company, Owners owners, string? actor = null,
            string? correlation = null, IFinancePermissionPort? permissions = null)
        {
            var (dbContext, companyContext, collector) = Tracked(company);
            var user = new FixedCurrentUser(actor ?? Actor, company);
            var service = new CustomerInstrumentService(dbContext, companyContext, user,
                permissions ?? new SyntheticPermissions(), new SyntheticBank(owners), new SyntheticParty(owners),
                new SyntheticSettings(company), new SyntheticOpenPeriodRule(), collector,
                new FinanceReceiptDraftBinding(Operation),
                new CustomerInstrumentAuditEnvelopes(
                    new FinanceAuditEnvelope(FinancePermissionNames.CustomerInstrumentReceive, ["SENTETIK-FINANCE"], "Web"),
                    new FinanceAuditEnvelope(FinancePermissionNames.CustomerInstrumentStatusChange, ["SENTETIK-FINANCE"], "Web"),
                    new FinanceAuditEnvelope(FinancePermissionNames.CustomerInstrumentCollect, ["SENTETIK-FINANCE"], "Web")),
                TimeProvider.System);
            return new Scope<ICustomerInstrumentService>(dbContext, service,
                new FinanceContext(company, actor ?? Actor, correlation ?? Correlation, null));
        }

        public async Task<CustomerInstrumentView> ReceiveAsync(Owners owners, CustomerInstrumentReceiveRequest request)
        {
            await using var scope = Instruments(owners.Company, owners, correlation: $"receive-{Guid.NewGuid():N}");
            var result = await scope.Service.ReceiveAsync(scope.Context, Key($"receive-{Guid.NewGuid():N}"), request,
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            return result.Resource!;
        }

        public Scope<IFinanceAllocationService> Allocations(Guid company)
        {
            var (dbContext, companyContext, collector) = Tracked(company);
            var service = new FinanceAllocationService(dbContext, companyContext, new FixedCurrentUser(Actor, company),
                new SyntheticPermissions(), collector,
                new FinanceAuditEnvelope(FinanceAllocationService.CreateCommandName, ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            return new Scope<IFinanceAllocationService>(dbContext, service,
                new FinanceContext(company, Actor, $"dec-fin-016-f03-{Guid.NewGuid():N}", null));
        }

        public Scope<IReversalProposalService> Reversals(Guid company)
        {
            var (dbContext, companyContext, collector) = Tracked(company);
            var user = new FixedCurrentUser(Actor, company);
            var service = new ReversalProposalService(dbContext, companyContext, user, new SyntheticPermissions(),
                new SyntheticOpenPeriodRule(), new FinanceAllocationReversalGate(dbContext, companyContext, user), collector,
                new FinanceReversalEnvelopes(
                    new FinanceAuditEnvelope(ReversalProposalService.ProposeCommand, ["SENTETIK-FINANCE"], "Web"),
                    new FinanceAuditEnvelope(ReversalProposalService.ApproveCommand, ["SENTETIK-FINANCE"], "Web")),
                TimeProvider.System);
            return new Scope<IReversalProposalService>(dbContext, service,
                new FinanceContext(company, Actor, $"dec-fin-016-t06-{Guid.NewGuid():N}", null));
        }

        public async Task<SavedOpenItem> SeedOpenItemAsync(Guid company, Guid companyAccountId, string amount)
        {
            await using var dbContext = Context(company);
            var batchId = Guid.NewGuid();
            dbContext.OpenItemSourceBatches.Add(new FinanceOpenItemSourceBatchRow
            {
                Id = batchId, CompanyId = company, SourceKind = "synthetic.receivable.v1", SourceVersion = "synthetic-v1",
                SourceChecksum = new string('f', 64),
                IdempotencyKeyHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                PayloadHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                ActorId = "synthetic-finance-open-item-fixture", ResultJson = "{}",
                CorrelationId = "synthetic-finance-open-item-fixture", AdmittedAtUtc = DateTimeOffset.UtcNow,
            });
            var row = new FinanceOpenItemRow
            {
                Id = Guid.NewGuid(), CompanyId = company, SourceKind = "synthetic.receivable.v1",
                SourceId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceVersion = "1",
                SourceBatchId = batchId, SourceBatchVersion = "synthetic-v1",
                SourceBatchChecksum = new string('f', 64), PartyId = Guid.NewGuid(),
                CompanyAccountId = companyAccountId, CurrencyId = Currency,
                OriginalAmountText = amount, OutstandingAmountText = amount,
                Status = FinanceOpenItemStatuses.Open, Version = 1,
                CreatedBy = "synthetic-finance-open-item-fixture", CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = "synthetic-finance-open-item-fixture", UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            dbContext.OpenItems.Add(row);
            await dbContext.SaveChangesAsync();
            return new SavedOpenItem(row.Id, "1");
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

    /// <summary>TEST INPUT: answers "open" for any date; not a period policy (K1's product rule is FIN-T05's case).</summary>
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

    /// <summary>SENTETIK: every action except the portfolio's amount read.</summary>
    private sealed class DenyAmount : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(action == FinanceAction.ReadCustomerInstrumentAmount
            ? PermissionDecision.Denied
            : PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: one usable bank account of the company in the test currency.</summary>
    private sealed class SyntheticBank(Owners owners) : IFinanceBankAccountReader
    {
        public Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(accountId == owners.Bank && context.CompanyId == owners.Company
                ? new ReadResult<BankAccountView>(ReadOutcome.Found,
                    new BankAccountView(owners.Bank, owners.Company, Currency, "SENTETIK çek bankası", true, true, "1"))
                : new ReadResult<BankAccountView>(ReadOutcome.NotFound, null));
    }

    /// <summary>SENTETIK: one active party account of the company.</summary>
    private sealed class SyntheticParty(Owners owners) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(partyId == owners.Party && companyAccountId == owners.Account && context.CompanyId == owners.Company
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(owners.Party, owners.Account, owners.Company, true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    /// <summary>SENTETIK: a money profile of scale 2 for the test currency — a test input, not a production scale.</summary>
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
