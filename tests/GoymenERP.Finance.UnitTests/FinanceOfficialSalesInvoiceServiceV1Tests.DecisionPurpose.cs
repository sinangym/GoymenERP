using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceOfficialSalesInvoiceServiceV1Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Decision_receipt_restores_only_the_original_native_command_and_never_changes_the_six_field_wire(bool approve)
    {
        var fixture = Fixture.Create(); await Record(fixture); fixture.Db.ChangeTracker.Clear();
        var context = Context("checker"); var key = new CommandMetadata("purpose-original");
        var service = fixture.Service("checker", new Documents());
        var result = approve ? await service.ApproveAsync(context, key, Approve(), default)
            : await service.RejectAsync(context, key, new(Invoice, "1", Reason), default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        var command = approve ? FinanceOfficialSalesInvoiceServiceV1.ApproveCommand : FinanceOfficialSalesInvoiceServiceV1.RejectCommand;
        var row = await fixture.Db.OfficialSalesInvoiceCommands.AsNoTracking().SingleAsync(x => x.CommandName == command);
        var proof = FinanceOfficialSalesInvoiceServiceV1.RestoreDecisionReceipt(row, context, key, Invoice, command, result);
        Assert.NotNull(proof);
        Assert.Equal((Company, Invoice, command, "checker", "1", "2", result.OriginalCorrelationId),
            (proof.CompanyId, proof.InvoiceId, proof.Command, proof.ActorId, proof.ExpectedVersion, proof.ResultVersion, proof.OriginalCorrelationId));
        var replay = result with { IsReplay = true, DecisionReceipt = proof };
        Assert.Equal(proof, FinanceOfficialSalesInvoiceServiceV1.RestoreDecisionReceipt(row,
            context with { CorrelationId = "purpose-retry" }, key, Invoice, command, replay));
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(replay, FinanceJsonOptions()));
        Assert.Equal(6, wire.RootElement.EnumerateObject().Count());
        Assert.False(wire.RootElement.TryGetProperty("decisionReceipt", out _));
        Assert.Equal(JsonSerializer.Serialize(result, FinanceJsonOptions()), JsonSerializer.Serialize(result with { DecisionReceipt = proof }, FinanceJsonOptions()));
        Assert.Null(JsonSerializer.Deserialize<FinanceOfficialSalesInvoiceResultV1>(row.ResultJson, FinanceJsonOptions())!.DecisionReceipt);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("invoice")]
    [InlineData("actor")]
    [InlineData("key")]
    [InlineData("payload")]
    [InlineData("command")]
    [InlineData("time")]
    [InlineData("correlation")]
    [InlineData("version")]
    [InlineData("status")]
    [InlineData("unknown-field")]
    [InlineData("missing-resource")]
    public async Task Missing_or_mismatched_original_receipt_never_becomes_decision_provenance(string fault)
    {
        var fixture = Fixture.Create(); await Record(fixture); fixture.Db.ChangeTracker.Clear();
        var context = Context("checker"); var key = new CommandMetadata("purpose-original");
        var result = await fixture.Service("checker", new Documents()).ApproveAsync(context, key, Approve(), default);
        var command = FinanceOfficialSalesInvoiceServiceV1.ApproveCommand;
        var row = await fixture.Db.OfficialSalesInvoiceCommands.AsNoTracking().SingleAsync(x => x.CommandName == command);
        switch (fault)
        {
            case "company": row.CompanyId = ForeignCompany; break;
            case "invoice": row.InvoiceId = ForeignInvoice; break;
            case "actor": row.ActorId = "other"; break;
            case "key": row.IdempotencyKeyHash = new string('b', 64); break;
            case "payload": row.PayloadHash = new string('b', 64); break;
            case "command": row.CommandName = FinanceOfficialSalesInvoiceServiceV1.RejectCommand; break;
            case "time": row.RecordedAtUtc = row.RecordedAtUtc.AddSeconds(1); break;
            case "correlation": row.CorrelationId = "other"; break;
            case "version": result = result with { Resource = result.Resource! with { Version = "3" } }; break;
            case "status": result = result with { Resource = result.Resource! with { Status = FinanceOfficialSalesInvoiceStatusV1.Rejected } }; break;
            case "unknown-field":
                var node = JsonNode.Parse(row.ResultJson)!; node["untrusted"] = true; row.ResultJson = node.ToJsonString(); break;
            case "missing-resource": row.ResultJson = "{}"; break;
        }
        Assert.Null(FinanceOfficialSalesInvoiceServiceV1.RestoreDecisionReceipt(row, context, key, Invoice, command, result));
        Assert.Null(result.DecisionReceipt); // The native answer is not rewritten into a new refusal or fake success.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Purpose_read_reuses_complete_owner_facts_for_pending_and_terminal_but_excludes_the_original_recorder(bool terminal)
    {
        var fixture = Fixture.Create(); await Record(fixture); fixture.Db.ChangeTracker.Clear();
        var checker = fixture.Service("checker", new Documents());
        if (terminal) Assert.Equal(CommandOutcome.Succeeded, (await checker.RejectAsync(Context("checker"), new("purpose-reject"), new(Invoice, "1", Reason), default)).Outcome);
        var commands = await fixture.Db.OfficialSalesInvoiceCommands.CountAsync(); var audit = fixture.Audit.Events.Count;
        var calls = 0;
        var answer = await checker.ReadDecisionAsync(Context("checker"), Invoice, _ => { calls++; return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Found, answer.Outcome); Assert.Equal(4, calls);
        var general = await checker.GetAsync(Context("checker"), Invoice, default);
        Assert.Equal(JsonSerializer.Serialize(general.Resource, FinanceJsonOptions()), JsonSerializer.Serialize(answer.Resource, FinanceJsonOptions()));
        Assert.Equal(Request().PayableAmount, answer.Resource!.PayableAmount);
        Assert.Equal(Sha, answer.Resource.PdfSha256); Assert.Equal(general.Resource!.TaxWarning, answer.Resource.TaxWarning);
        Assert.Equal(terminal ? "2" : "1", answer.Resource.Version);
        Assert.Equal(ReadOutcome.Denied, (await fixture.Service("maker", new Documents()).ReadDecisionAsync(Context("maker"), Invoice, _ => Task.CompletedTask, default)).Outcome);
        Assert.Equal(commands, await fixture.Db.OfficialSalesInvoiceCommands.CountAsync()); Assert.Equal(audit, fixture.Audit.Events.Count);
        Assert.False(fixture.Db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("membership")]
    [InlineData("consumer")]
    public async Task Purpose_read_refuses_late_authority_or_consumer_context_loss_without_releasing_owner_facts(string fault)
    {
        var fixture = Fixture.Create(); await Record(fixture); fixture.Db.ChangeTracker.Clear();
        var company = new PurposeCompany(Company); var user = new PurposeUser();
        var authority = new PurposeAuthority(); var consumerCalls = 0;
        var service = PurposeService(fixture, company, user, authority);
        authority.OnRead = action =>
        {
            if (action == FinanceOfficialSalesInvoiceAuthorityActionV1.Approve && authority.Actions.Count == 4)
            {
                if (fault == "actor") user.Actor = "other";
                if (fault == "company") company.Set(ForeignCompany);
                if (fault == "membership") user.Member = false;
                if (fault == "permission") return PermissionDecision.Denied;
            }
            return PermissionDecision.Allowed;
        };
        Task Consumer(CancellationToken _) { consumerCalls++; if (fault == "consumer" && consumerCalls == 3) user.Member = false; return Task.CompletedTask; }
        var answer = await service.ReadDecisionAsync(Context("checker"), Invoice, Consumer, default);
        Assert.Equal(ReadOutcome.Denied, answer.Outcome); Assert.Null(answer.Resource);
        Assert.False(fixture.Db.ChangeTracker.HasChanges());
        Assert.Single(await fixture.Db.OfficialSalesInvoiceCommands.ToListAsync()); Assert.Single(fixture.Audit.Events);
    }

    [Fact]
    public async Task Record_only_general_reader_does_not_gain_the_purpose_read_and_foreign_identity_is_not_visible()
    {
        var fixture = Fixture.Create(); await Record(fixture); fixture.Db.ChangeTracker.Clear();
        var company = new PurposeCompany(Company); var user = new PurposeUser(); var authority = new PurposeAuthority();
        authority.OnRead = action => action == FinanceOfficialSalesInvoiceAuthorityActionV1.Read ? PermissionDecision.Allowed : PermissionDecision.Denied;
        var service = PurposeService(fixture, company, user, authority);
        Assert.Equal(ReadOutcome.Found, (await service.GetAsync(Context("checker"), Invoice, default)).Outcome);
        var denied = await service.ReadDecisionAsync(Context("checker"), Invoice, _ => Task.CompletedTask, default);
        Assert.Equal(ReadOutcome.Denied, denied.Outcome); Assert.Null(denied.Resource);
        authority.OnRead = _ => PermissionDecision.Allowed;
        Assert.Equal(ReadOutcome.NotFound, (await service.ReadDecisionAsync(Context("checker"), ForeignInvoice, _ => Task.CompletedTask, default)).Outcome);
        Assert.Equal(ReadOutcome.Denied, (await service.ReadDecisionAsync(Context("checker", ForeignCompany), Invoice, _ => Task.CompletedTask, default)).Outcome);
        Assert.IsAssignableFrom<IFinanceOfficialSalesInvoiceDecisionReadV1>(service.WithApprovalSafety(null));
    }

    private static JsonSerializerOptions FinanceJsonOptions() => FinanceJson.CreateOptions();
    private static FinanceOfficialSalesInvoiceServiceV1 PurposeService(Fixture fixture, ICompanyContext company,
        ICurrentUser user, IFinanceOfficialSalesInvoiceAuthorityV1 authority) => new(fixture.Db, company, user,
        authority, new Documents(), new Candidates([new MoneyCurrencyCandidateV1(Currency, "TRY", "SENTETIK Lira")]), fixture.Audit,
        new(new(FinanceOfficialSalesInvoicePermissionNamesV1.Record, [], "unit-test"),
            new(FinanceOfficialSalesInvoicePermissionNamesV1.Approve, [], "unit-test"),
            new(FinanceOfficialSalesInvoicePermissionNamesV1.Approve, [], "unit-test"),
            new(FinanceOfficialSalesInvoicePermissionNamesV1.Withdraw, [], "unit-test")), TimeProvider.System, new CustomerOwner());
    private sealed class PurposeCompany(Guid id) : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } = id; public void Set(Guid value) => CompanyId = value; }
    private sealed class PurposeUser : ICurrentUser
    { public string Actor = "checker"; public bool Member = true; public bool IsAuthenticated => true; public string? UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company, ForeignCompany }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class PurposeAuthority : IFinanceOfficialSalesInvoiceAuthorityV1
    { public List<FinanceOfficialSalesInvoiceAuthorityActionV1> Actions { get; } = []; public Func<FinanceOfficialSalesInvoiceAuthorityActionV1, PermissionDecision>? OnRead;
      public Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid id, CancellationToken t)
      { Actions.Add(action); return Task.FromResult(OnRead?.Invoke(action) ?? PermissionDecision.Allowed); } }
}
