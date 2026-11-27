using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    // Same isolated currency as the existing native Finance receipt/allocation fixture. The decision compares in this
    // currency; a missing TL read/rate stays unavailable information (SAT-D80), never a fabricated conversion.
    private static readonly Guid SecondRiskCurrency = Guid.Parse("6e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a91");

    [SqlServerFact]
    public async Task Second_native_shipment_uses_real_Sales_and_Finance_owners_without_double_count_before_and_after_full_allocation()
    {
        foreach (var settle in new[] { false, true })
        {
            await using var run = await Run.StartAsync();
            var fixture = await SeedHostedShipmentAsync(run, $"SYN-SECOND-RISK-{settle}");
            await SeedSecondRiskOrderAsync(run, fixture);
            await SetSecondRiskLimitAsync(run, fixture);
            var grant = await GrantRealizationAsync(run, $"SYN-SECOND-RISK-{Guid.NewGuid():N}");
            await GrantSecondRiskReadAsync(run, grant);
            var observed = new SecondRiskObservations();
            await using var host = SecondRiskHost(run, fixture, grant, observed);
            using var client = HostedClientFor(host, grant.Subject, CompanyA);
            var first = CreateHostedShipmentRequest(fixture, "1", "second-risk-first");
            using (var result = await client.SendAsync(first.Message)) Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.True(observed.GateReads > 0); Assert.Empty(observed.Answers.Last().ProcessedSalesInvoices.ProcessedSalesInvoiceIds);

            var (next, invoice) = await FinalizeFirstSecondRiskShipmentAsync(run, fixture, first.ShipmentId);
            var admission = await AdmitSecondRiskInvoiceAsync(run, fixture, invoice);
            if (settle)
                await FinanceRecordSqlServerRelationalAcceptanceTests.AllocateSecondShipmentInvoiceAsync(run.Factory,
                    CompanyA, fixture.Owners.Party, fixture.Owners.Account, admission);

            // Real Sales read, not an expected calculator result substituted by the test. A duplicated invoice would produce
            // 960 (240 AR + 240 shipped + 480 planned) and exceed the native 800 limit. Correct total is 720 or 480.
            using (var risk = await client.GetAsync($"{SalesEndpoints.DraftsPath}/{fixture.Owners.Order:D}/risk-check"))
            {
                Assert.Equal(HttpStatusCode.OK, risk.StatusCode);
                var body = await risk.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                Assert.Equal("Within", body.GetProperty("result").GetString());
                Assert.Equal(settle ? "480.00" : "720.00", body.GetProperty("inLimitCurrency").GetProperty("grossRisk").GetString());
                var components = body.GetProperty("components").EnumerateArray().ToDictionary(x => x.GetProperty("kind").GetString()!);
                Assert.Equal(0, components["ShippedNotInvoiced"].GetProperty("amounts").GetArrayLength());
                Assert.Equal(0, components["InvoicedAwaitingFinance"].GetProperty("amounts").GetArrayLength());
                Assert.Equal("480.00", components["ApprovedUnshippedRemainder"].GetProperty("amounts")[0].GetProperty("amount").GetString());
                var ar = components["OpenReceivables"].GetProperty("amounts");
                if (settle) Assert.Equal(0, ar.GetArrayLength());
                else Assert.Equal("240.00", ar[0].GetProperty("amount").GetString());
            }
            var ownerAnswer = observed.Answers.Last();
            Assert.Equal([invoice.Id], ownerAnswer.ProcessedSalesInvoices.ProcessedSalesInvoiceIds);
            Assert.Equal(SalesPartyRiskProcessedSetStateV2.Available, ownerAnswer.ProcessedSalesInvoices.State);
            Assert.All(observed.Queries.Where(x => x.SalesInvoiceIds.Count > 0), x => Assert.Equal([invoice.Id], x.SalesInvoiceIds));

            var second = SecondRiskShipment(next, "second-risk-second");
            var reads = observed.GateReads;
            using (var result = await client.SendAsync(second.Message)) Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(reads + 1, observed.GateReads);
            Assert.Equal([invoice.Id], observed.Answers.Last().ProcessedSalesInvoices.ProcessedSalesInvoiceIds);
            Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE InstructionId=@id AND Kind=2", ("@id", fixture.InstructionId)));
            Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id IN (@first,@second)", ("@first", first.MovementId), ("@second", second.MovementId)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId=@id", ("@id", invoice.Id)));
            if (settle)
                Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId=@id AND Status=2 AND OutstandingAmountText='0.00'", ("@id", invoice.Id)));

            var after = await HostedEffectsAsync(run, next, second);
            var replay = SecondRiskShipment(next, "second-risk-second", second.ShipmentId, second.MovementId, "SYN-second-risk-new-replay-correlation");
            reads = observed.GateReads;
            using (var result = await client.SendAsync(replay.Message))
            {
                Assert.Equal(HttpStatusCode.OK, result.StatusCode);
                Assert.Equal("true", Assert.Single(result.Headers.GetValues("Idempotency-Replayed")));
                Assert.Equal(second.CorrelationId, Assert.Single(result.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            }
            Assert.Equal(reads, observed.GateReads); Assert.Equal(after, await HostedEffectsAsync(run, next, second));
        }
    }

    [SqlServerFact]
    public async Task Second_native_shipment_fresh_authority_loss_after_real_Finance_read_writes_no_owner_effect()
    {
        await using var run = await Run.StartAsync(); var fixture = await SeedHostedShipmentAsync(run, "SYN-SECOND-RISK-REVOKE");
        await SeedSecondRiskOrderAsync(run, fixture); await SetSecondRiskLimitAsync(run, fixture);
        var grant = await GrantRealizationAsync(run, $"SYN-SECOND-RISK-REVOKE-{Guid.NewGuid():N}");
        var observed = new SecondRiskObservations();
        await using var host = SecondRiskHost(run, fixture, grant, observed); using var client = HostedClientFor(host, grant.Subject, CompanyA);
        var first = CreateHostedShipmentRequest(fixture, "1", "second-risk-revoke-first");
        using (var result = await client.SendAsync(first.Message)) Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var (next, invoice) = await FinalizeFirstSecondRiskShipmentAsync(run, fixture, first.ShipmentId);
        await AdmitSecondRiskInvoiceAsync(run, fixture, invoice);
        var revoke = new InventoryWriteRevoker(run.Factory, grant.RoleId);
        observed.AfterFinanceRead = revoke.RevokeAsync;
        var second = SecondRiskShipment(next, "second-risk-revoked"); var before = await HostedEffectsAsync(run, next, second);
        using (var result = await client.SendAsync(second.Message)) Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.True(revoke.Revoked); Assert.Equal([invoice.Id], observed.Answers.Last().ProcessedSalesInvoices.ProcessedSalesInvoiceIds);
        Assert.Equal(before, await HostedEffectsAsync(run, next, second));
    }

    [SqlServerFact]
    public async Task Second_native_shipment_and_real_Finance_membership_never_cross_the_company_boundary()
    {
        await using var run = await Run.StartAsync(); var fixture = await SeedHostedShipmentAsync(run, "SYN-SECOND-RISK-FOREIGN");
        await SeedSecondRiskOrderAsync(run, fixture); await SetSecondRiskLimitAsync(run, fixture);
        var grant = await GrantRealizationAsync(run, $"SYN-SECOND-RISK-FOREIGN-{Guid.NewGuid():N}");
        var observed = new SecondRiskObservations(); await using var host = SecondRiskHost(run, fixture, grant, observed);
        using var client = HostedClientFor(host, grant.Subject, CompanyA);
        var first = CreateHostedShipmentRequest(fixture, "1", "second-risk-foreign-first");
        using (var result = await client.SendAsync(first.Message)) Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var (next, invoice) = await FinalizeFirstSecondRiskShipmentAsync(run, fixture, first.ShipmentId);
        await AdmitSecondRiskInvoiceAsync(run, fixture, invoice);
        var second = SecondRiskShipment(next, "second-risk-foreign-second"); var before = await HostedEffectsAsync(run, next, second);
        var reads = observed.GateReads;
        using (var foreign = HostedClientFor(host, grant.Subject, CompanyB))
        using (var result = await foreign.SendAsync(second.Message)) Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Equal(reads, observed.GateReads); Assert.Equal(before, await HostedEffectsAsync(run, next, second));
        var company = new CompanyContext(); company.Set(CompanyB);
        await using var db = SecondRiskFinanceContext(run, company);
        var answer = await new FinanceSalesPartyRiskReadPortV1(db, company).ReadPartyRiskAsync(new(fixture.Owners.Party, fixture.Owners.Account, [invoice.Id]));
        Assert.Equal(CompanyB, answer.CompanyId); Assert.Equal(SalesPartyRiskProcessedSetStateV2.Available, answer.ProcessedSalesInvoices.State);
        Assert.Empty(answer.ProcessedSalesInvoices.ProcessedSalesInvoiceIds); Assert.Empty(answer.OpenReceivables.Amounts);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> SecondRiskHost(Run run,
        HostedShipmentFixture fixture, RealizationGrant grant, SecondRiskObservations observed) =>
        HostedShipmentHost(run, fixture, new HostedAllocationReads(fixture), grant.Subject, configure: services =>
        {
            // TEST DI only: real owner readers replace the hosted fixture's fake risk and stale fixed allocation read.
            // Its synthetic Sales-line and over-delivery references remain; this is not an acceptance of those other gates.
            services.RemoveAll<ISalesShipmentRiskGateReadPortV1>();
            services.AddScoped<ISalesShipmentRiskGateReadPortV1>(provider => new SalesShipmentRiskGateReadPort(
                new SecondRiskSource(provider.GetRequiredService<SalesDraftService>(), observed),
                provider.GetRequiredService<ISalesShipmentRiskExceptionReadPortV1>(), provider.GetRequiredService<ICompanyContext>()));
            services.RemoveAll<ISalesPartyRiskReadPortV2>();
            services.AddScoped<ISalesPartyRiskReadPortV2>(provider => new SecondRiskFinanceReader(
                provider.GetRequiredService<FinanceSalesPartyRiskReadPortV1>(), observed));
            services.RemoveAll<GoymenERP.Modules.Inventory.Application.IInventoryPhysicalAllocationReadsV1>();
            services.AddInventoryPhysicalAllocationReads();
        });

    private sealed class SecondRiskObservations
    {
        public int GateReads; public readonly List<SalesPartyRiskReadQueryV2> Queries = [];
        public readonly List<SalesPartyRiskReadAnswerV2> Answers = []; public Func<Task>? AfterFinanceRead;
    }
    private sealed class SecondRiskSource(ISalesShipmentRiskGateSource owner, SecondRiskObservations observed) : ISalesShipmentRiskGateSource
    {
        public async Task<SalesShipmentRiskGateDecisionV1> EvaluateShipmentRiskGateAsync(Guid id, CancellationToken token = default)
        { observed.GateReads++; return await owner.EvaluateShipmentRiskGateAsync(id, token); }
    }
    private sealed class SecondRiskFinanceReader(ISalesPartyRiskReadPortV2 owner, SecondRiskObservations observed) : ISalesPartyRiskReadPortV2
    {
        public async Task<SalesPartyRiskReadAnswerV2> ReadPartyRiskAsync(SalesPartyRiskReadQueryV2 query, CancellationToken token = default)
        {
            var result = await owner.ReadPartyRiskAsync(query, token); observed.Queries.Add(query); observed.Answers.Add(result);
            if (observed.AfterFinanceRead is { } after) await after(); return result;
        }
    }
    private static async Task GrantSecondRiskReadAsync(Run run, RealizationGrant grant)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var role = await db.Roles.Include(x => x.Permissions).SingleAsync(x => x.Id == grant.RoleId);
        role.AddPermission(SalesPermissionManifestV1.DraftsRead); role.AddPermission(SalesPermissionManifestV1.OrdersPriceRead);
        await db.SaveChangesAsync();
    }
    private static HostedShipmentRequest SecondRiskShipment(HostedShipmentFixture fixture, string key, Guid? shipmentId = null,
        Guid? movementId = null, string? correlationId = null)
    {
        var shipment = shipmentId ?? Guid.NewGuid(); var movement = movementId ?? Guid.NewGuid(); var correlation = correlationId ?? "SYN-" + key;
        var request = new HttpRequestMessage(HttpMethod.Post, $"{LogisticsEndpoints.InstructionsPath}/{fixture.InstructionId:D}/lines/{fixture.LineId:D}/shipments")
        { Content = JsonContent.Create(new RecordShipmentRequest(fixture.InstructionVersion.ToString(CultureInfo.InvariantCulture),
            shipment, fixture.AllocationId, "2", "2", movement, "2", T0.AddMinutes(10))) };
        request.Headers.Add("Idempotency-Key", key); request.Headers.Add("X-Correlation-Id", correlation);
        return new(request, shipment, movement, correlation);
    }
    private static SalesDraftWriteEnvelope SecondRiskEnvelope(Run run, string key, string? actor = null) =>
        new(actor ?? run.Actor, SalesPermissionManifestV1.InvoicesWrite, [], "SYN-" + key, null, key, "Web", null, null);
    private sealed class SecondRiskSalesScope(SalesDbContext db, SalesStore owner) : IAsyncDisposable
    { public SalesStore Owner => owner; public ValueTask DisposeAsync() => db.DisposeAsync(); }
    private static SecondRiskSalesScope SecondRiskSales(Run run)
    {
        var company = new CompanyContext(); company.Set(CompanyA); var audit = new AuditEventCollector();
        var db = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
        return new(db, new SalesStore(db, company, audit, TimeProvider.System));
    }
    private static SalesLineTaxSnapshot SecondRiskTax(Guid product) => new(product, Day, "Normal", "Steel", "Resolved", null,
        "Record", Guid.NewGuid(), 1, "Resolved", 20m, "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);
    private static async Task SeedSecondRiskOrderAsync(Run run, HostedShipmentFixture fixture)
    {
        await using var scope = SecondRiskSales(run); var profile = Guid.NewGuid(); var unitProfile = Guid.NewGuid();
        var unit = new UnitProfileSnapshot(CompanyA, KgUnit, "kg", "weight", unitProfile, "1", true, 3, 3, true, Day.AddDays(-1), null);
        var binding = new UnitBindingSnapshot(CompanyA, KgUnit, unitProfile, "1");
        var product = new ProductSnapshot(fixture.ProductId, "7", "SYN-SECOND-RISK", "Sentetik risk ürünü", Day, binding, binding, binding);
        var stored = await scope.Owner.StoreDraftAsync(SecondRiskEnvelope(run, "second-risk-order"), Hash("second-risk-order"), number =>
        {
            var order = SalesOrderDraft.Create(fixture.Owners.Order, CompanyA, number,
                new(fixture.Owners.Party, fixture.Owners.Account, SecondRiskCurrency, Day, DueTerms: TestDueTerms.PointDue30),
                new(CompanyA, SecondRiskCurrency, SalesOrderDraft.MoneyOperation, profile, "1", Day, true, Day.AddDays(-1), null, 2), T0);
            order.AddLine(order.Version, new(fixture.OrderLineId, product, unit, "7", 7m, Commercial:
                SalesOrderDraft.WithDiscountLimitCheck(new("100", KgUnit, null), SalesDiscountLimit.Undefined, run.Actor, T0),
                Tax: SecondRiskTax(fixture.ProductId)), T0); return order;
        });
        await scope.Owner.ReviseDraftCommercialAsync(SecondRiskEnvelope(run, "second-risk-discount", "SYN-manager"), Hash("second-risk-discount"),
            SalesDraftCommands.ApproveDiscountExceptionsV1, stored.Draft.Id, (order, now) =>
            { order.ApproveDiscountExceptions(order.Version, [new(fixture.OrderLineId, "SENTETIK fiyat")], "SYN-manager", now); return null; },
            new("sales.order-draft.discount-exceptions-approved.v1", "ApproveDiscountExceptions", "SENTETIK"), _ => Task.CompletedTask,
            default, SalesRiskCheckSqlServerRelationalAcceptanceTests.SecondShipmentReplayProjection);
        await scope.Owner.UpdateDraftStateAsync(SecondRiskEnvelope(run, "second-risk-submit"), Hash("second-risk-submit"), SalesDraftCommands.SubmitV1,
            stored.Draft.Id, (order, now) => order.Submit(3, run.Actor, now), new("sales.order-draft.submitted.v1", "Submit", "SENTETIK"));
        await scope.Owner.UpdateDraftStateAsync(SecondRiskEnvelope(run, "second-risk-approve", "SYN-order-checker"), Hash("second-risk-approve"), SalesDraftCommands.ApproveV1,
            stored.Draft.Id, (order, now) => order.Approve(4, "SYN-order-checker", now), new("sales.order-draft.approved.v1", "Approve", "SENTETIK"));
        // The reused hosted instruction retains Rev.1. Produce that revision through native commands, not a history patch.
        await scope.Owner.ReviseDraftQuantityAsync(SecondRiskEnvelope(run, "second-risk-revise"), Hash("second-risk-revise"),
            SalesDraftCommands.ReviseQuantityV1, stored.Draft.Id,
            (order, now) => order.ReviseQuantities(5, [new(fixture.OrderLineId, "6", 6m)], run.Actor, "SENTETIK altı kg sipariş", now),
            new("sales.order-draft.quantity-revised.v1", "ReviseQuantity", "SENTETIK"), (_, _) => Task.CompletedTask);
        await scope.Owner.UpdateDraftStateAsync(SecondRiskEnvelope(run, "second-risk-resubmit"), Hash("second-risk-resubmit"), SalesDraftCommands.SubmitV1,
            stored.Draft.Id, (order, now) => order.Submit(6, run.Actor, now), new("sales.order-draft.submitted.v1", "Submit", "SENTETIK"));
        var approved = await scope.Owner.UpdateDraftStateAsync(SecondRiskEnvelope(run, "second-risk-reapprove", "SYN-order-checker"), Hash("second-risk-reapprove"), SalesDraftCommands.ApproveV1,
            stored.Draft.Id, (order, now) => order.Approve(7, "SYN-order-checker", now), new("sales.order-draft.approved.v1", "Approve", "SENTETIK"));
        Assert.Equal(1, approved!.Draft.Revision);
    }
    private static async Task<(HostedShipmentFixture Fixture, SalesInvoice Invoice)> FinalizeFirstSecondRiskShipmentAsync(
        Run run, HostedShipmentFixture fixture, Guid shipment)
    {
        var noteId = Guid.NewGuid(); long version;
        await using (var logistics = run.Store(CompanyA))
        {
            var current = (await logistics.Store.FindInstructionAsync(fixture.InstructionId))!;
            var drafted = (await logistics.Store.MutateInstructionAsync(run.Envelope("second-risk-note"), Hash("second-risk-note"),
                LogisticsCommands.CreateDispatchNoteV1, fixture.InstructionId,
                (value, now) => value.CreateDispatchNoteDraft(current.Instruction.Version, noteId, [shipment], run.Actor, now), Drafted))!;
            version = drafted.Instruction.Instruction.Version;
        }
        // The hardened native approval owner writes the note receipt; no raw mutation bypass.
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var native = (ILogisticsDispatchNoteApprovalStoreV1)scope.ServiceProvider.GetRequiredService<LogisticsStore>();
            var approved = await native.ApproveDispatchNoteOwnedAsync(run.Envelope("second-risk-note-approve", actor: "SYN-note-checker"),
                Hash("second-risk-note-approve"), fixture.InstructionId, noteId, version, Approved, _ => Task.CompletedTask);
            version = approved!.Instruction.Instruction.Version;
        }
        string number;
        await using (var reader = run.Store(CompanyA)) number = (await reader.Store.FindApprovedDispatchInvoiceSourceAsync(noteId))!.Number!.Text;
        await using var sales = SecondRiskSales(run); var id = Guid.NewGuid();
        var terms = SalesDueTerms.Create([new(1, SalesDueBasis.OrderDate, 30, "100")]);
        // Explicit synthetic retained invoice rate, not a tested production FX source or a risk-side conversion fallback.
        await sales.Owner.StoreInvoiceAsync(SecondRiskEnvelope(run, "second-risk-invoice"), Hash("second-risk-invoice"), invoiceNumber =>
            SalesInvoice.Create(id, CompanyA, invoiceNumber,
                new(fixture.Owners.Party, fixture.Owners.Account, SecondRiskCurrency, Guid.NewGuid(), "1", 2, Day,
                    new(1m, Day, Day, SettingsCurrencyIdentitiesV1.TurkishLira, 2)),
                [new(Guid.NewGuid(), shipment, noteId, number, 1, Day, fixture.Owners.Order, fixture.OrderLineId, 1,
                    fixture.ProductId, KgUnit, 2m, "2", new("100", KgUnit, null), SecondRiskTax(fixture.ProductId))],
                SalesDueDateCalculator.Compute(terms, Day, Day).DueDates, terms, run.Actor, T0));
        await sales.Owner.DecideInvoiceAsync(SecondRiskEnvelope(run, "second-risk-invoice-submit"), Hash("second-risk-invoice-submit"),
            SalesInvoiceCommands.SubmitV1, id, (value, now) => value.Submit(1, run.Actor, now), new("sales.invoice.submitted.v1", "Submit", "SENTETIK"));
        var approvedInvoice = (await sales.Owner.DecideInvoiceAsync(SecondRiskEnvelope(run, "second-risk-invoice-approve", "SYN-invoice-checker"), Hash("second-risk-invoice-approve"),
            SalesInvoiceCommands.ApproveV1, id, (value, now) => value.Approve(2, "SYN-invoice-checker", now), new("sales.invoice.approved.v1", "Approve", "SENTETIK")))!.Invoice;
        var official = new SalesOfficialInvoiceFacts("GIB2026000000001", Guid.NewGuid().ToString("D"), fixture.Owners.Party,
            SecondRiskCurrency, approvedInvoice.PayableFunctionalAmount, approvedInvoice.PayableFunctionalAmount,
            approvedInvoice.VatFunctionalAmount, approvedInvoice.WithholdingFunctionalAmount, Day,
            new(CompanyA, Guid.NewGuid(), "manual", "1", T0, Guid.NewGuid(), 1, Hash("second-risk-official"), null, null, false));
        await sales.Owner.MatchInvoiceAsync(SecondRiskEnvelope(run, "second-risk-match", "SYN-matcher"), Hash("second-risk-match"), id,
            (value, now) => value.RecordMatch(3, "SYN-matcher", official, 0m, false, null, now), new("sales.invoice.matched.v1", "Match", "SENTETIK"));
        var final = (await sales.Owner.FinalizeInvoiceAsync(SecondRiskEnvelope(run, "second-risk-final", "SYN-finalizer"), Hash("second-risk-final"), id,
            (value, now) => value.Finalize(3, "SYN-finalizer", Guid.NewGuid, now), new("sales.invoice.finalized.v1", "Finalize", "SENTETIK")))!.Invoice;
        return (fixture with { InstructionVersion = version }, final);
    }
    private static FinanceDbContext SecondRiskFinanceContext(Run run, ICompanyContext company, AuditEventCollector? audit = null)
    {
        var builder = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
        if (audit is not null) builder.AddInterceptors(new AuditSaveChangesInterceptor(audit, company));
        return new(builder.Options, company);
    }
    private sealed class SecondRiskFinanceInputs(HostedShipmentFixture fixture, string actor)
        : ICurrentUser, IFinancePartyAccountPort, IFinanceInvoiceOpenItemAdmissionAuthorityV1, IFinancePermissionPort, ISettingsMoneyCandidateReader
    {
        public bool IsAuthenticated => true; public string UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA };
        public bool CanAccessCompany(Guid id) => id == CompanyA;
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid party, Guid account, CancellationToken token) =>
            Task.FromResult(context.CompanyId == CompanyA && party == fixture.Owners.Party && account == fixture.Owners.Account
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found, new(party, account, CompanyA, true, IsPartyActive: true)) : new(ReadOutcome.NotFound, null));
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer, Guid invoice, CancellationToken token) =>
            Task.FromResult(context.CompanyId == CompanyA ? PermissionDecision.Allowed : PermissionDecision.Denied);
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? source, CancellationToken token) =>
            Task.FromResult(context.CompanyId == CompanyA ? PermissionDecision.Allowed : PermissionDecision.Denied);
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new(SecondRiskCurrency, "SYN", "Sentetik para")]);
    }
    private static async Task SetSecondRiskLimitAsync(Run run, HostedShipmentFixture fixture)
    {
        var company = new CompanyContext(); company.Set(CompanyA); var audit = new AuditEventCollector();
        var inputs = new SecondRiskFinanceInputs(fixture, run.Actor); await using var db = SecondRiskFinanceContext(run, company, audit);
        var owner = new FinancePartyCreditLimitService(db, company, inputs, inputs, inputs, inputs, audit,
            new(new("synthetic.limit.propose", [], "Web"), new("synthetic.limit.approve", [], "Web")), TimeProvider.System);
        var result = await owner.SetAsync(new(CompanyA, run.Actor, "SYN-limit", null), new("second-risk-limit"),
            new(fixture.Owners.Party, fixture.Owners.Account, SecondRiskCurrency, "800.00", "0"), default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
    }
    private static async Task<FinanceInvoiceOpenItemAdmissionV1> AdmitSecondRiskInvoiceAsync(Run run, HostedShipmentFixture fixture, SalesInvoice invoice)
    {
        var final = invoice.Finalization!;
        var source = new FinanceInvoiceOpenItemAdmissionV1(final.AdmissionId, FinanceInvoiceProducerV1.Sales, invoice.Id,
            final.FinalizedVersion.ToString(CultureInfo.InvariantCulture), fixture.Owners.Party, fixture.Owners.Account, SecondRiskCurrency,
            "1", final.Plan.Select(x => new FinanceInvoiceInstallmentV1(x.OpenItemId, x.InstallmentId, x.SequenceNo, x.DueDate,
                x.Amount.ToString("F2", CultureInfo.InvariantCulture), x.FunctionalAmount?.ToString("F2", CultureInfo.InvariantCulture))).ToArray(),
            invoice.Match!.OfficialNo, invoice.Match.Ettn);
        var company = new CompanyContext(); company.Set(CompanyA); var audit = new AuditEventCollector();
        var inputs = new SecondRiskFinanceInputs(fixture, run.Actor); await using var db = SecondRiskFinanceContext(run, company, audit);
        var owner = new FinanceInvoiceOpenItemAdmissionService(db, company, inputs, inputs, inputs, audit,
            new("synthetic.invoice.admit", [], "Web"), TimeProvider.System);
        var result = await owner.AdmitAsync(new(CompanyA, run.Actor, "SYN-admit", null), new("second-risk-admit"), source, default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome); Assert.Equal(source.Installments.Select(x => x.OpenItemId).Order(), result.OpenItemIds.Order());
        return source;
    }
}

public sealed partial class FinanceRecordSqlServerRelationalAcceptanceTests
{
    // Borrow the disposable Logistics fixture database, not its lifetime. Reuse native Finance Save/Record/Allocation;
    // the existing synthetic period/settings/bank/permission references are test inputs, never ERP activation or policy.
    internal static async Task AllocateSecondShipmentInvoiceAsync(SqlServerRelationalApiFactory factory, Guid company,
        Guid party, Guid account, FinanceInvoiceOpenItemAdmissionV1 invoice)
    {
        Assert.Equal(Currency, invoice.CurrencyId);
        var run = Run.BorrowSecondShipmentFactory(factory); var owners = new Owners(company, Guid.NewGuid(), party, account);
        await run.SeedApprovedBankAccountAsync(owners);
        var draft = await run.SaveDraftAsync(owners, "240.00"); SavedDraft recorded;
        await using (var receipt = run.Service(company, owners))
        {
            var answer = await receipt.Service.RecordAsync(receipt.Context, Key("second-risk-payment"), new(draft.Id, draft.Version), default);
            Assert.Equal(CommandOutcome.Succeeded, answer.Outcome); recorded = new(draft.Id, answer.Resource!.Source.Version);
        }
        foreach (var item in invoice.Installments)
        {
            var version = (await run.ScalarAsync<long>("SELECT Version FROM finance.OpenItems WHERE Id=@id", ("@id", item.OpenItemId))).ToString(CultureInfo.InvariantCulture);
            await using var allocation = run.AllocationService(company, run.Actor, new SyntheticPermissions());
            var answer = await allocation.Service.CreateAsync(allocation.Context, Key("second-risk-allocation-" + item.Sequence),
                new(Guid.NewGuid(), recorded.Id, recorded.Version, item.OpenItemId, version, item.Amount, "SENTETIK complete settlement"), default);
            Assert.Equal(CommandOutcome.Succeeded, answer.Outcome);
        }
        Assert.Equal(invoice.Installments.Count, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId=@id AND Status=2 AND OutstandingAmountText='0.00'", ("@id", invoice.InvoiceId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts WHERE Id=@id AND Stage=1", ("@id", recorded.Id)));
    }

    private sealed partial class Run
    {
        // The existing constructor stays private. This borrowed fixture must never dispose the caller-owned factory.
        public static Run BorrowSecondShipmentFactory(SqlServerRelationalApiFactory factory) => new(string.Empty, factory);
    }
}

public sealed partial class SalesRiskCheckSqlServerRelationalAcceptanceTests
{
    // Reuse the existing native discount-command replay projection; do not fabricate a second result shape.
    internal static SalesDraftReplayResult SecondShipmentReplayProjection(SalesOrderDraft draft) => ReplayResult(draft);
}
