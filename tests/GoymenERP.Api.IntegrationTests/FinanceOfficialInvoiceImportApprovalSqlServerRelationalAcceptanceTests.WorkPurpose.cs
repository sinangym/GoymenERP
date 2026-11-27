using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceOfficialInvoiceImportApprovalSqlServerRelationalAcceptanceTests
{
    // Existing real SQL/Audit owner fixture; authority and delegation are controlled ports, not real Identity/HTTP/UAT.
    [SqlServerFact]
    public async Task Work_import_approval_reuses_native_receipt_and_terminal_old_key_replay_without_duplicate_writes()
    {
        await using var run = await Run.StartAsync(); await UploadForSafety(run);
        var permissions = new ImportPurposePermissions(); var execution = new WorkDecisionExecutionFenceV1();
        var authority = new WorkDecisionAuthorityV1(permissions, execution); var delegations = new ImportPurposeDelegations();
        var probe = new ApprovalSafetySaveProbe();
        var native = new ApprovalSafetyAuthority(async () => (await authority.RevalidatePermissionScopeAsync(
            FinanceOfficialInvoiceImportPermissionNamesV1.Approve, Company)).IsGranted ? PermissionDecision.Allowed : PermissionDecision.Denied);
        await using var checker = run.Service(Company, "SENTETIK-checker", native, probe);
        var host = ImportPurposeHost(checker, authority, execution, delegations);
        var before = await host.PreviewAsync(WorkDecisionHostV1.OfficialInvoiceImport, Batch, default);
        Assert.Equal("1", before.SourceVersion); Assert.True(before.Pending); Assert.False(before.RejectionSupported);
        Assert.Single(before.OfficialInvoiceImport!.Preview.Batch!.Rows);
        var first = await host.DecideAsync(WorkDecisionHostV1.OfficialInvoiceImport, Batch, new("1", true, null), "work-import-key", default);
        Assert.Equal("Approved", first.State); Assert.False(first.Replayed); await AssertOneApproval(run);
        var terminal = await host.PreviewAsync(WorkDecisionHostV1.OfficialInvoiceImport, Batch, default);
        Assert.Equal("2", terminal.SourceVersion); Assert.False(terminal.Pending);
        var replay = await host.DecideAsync(WorkDecisionHostV1.OfficialInvoiceImport, Batch, new("1", true, null), "work-import-key", default);
        Assert.True(replay.Replayed); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(1, probe.Attempts); await AssertOneApproval(run); AssertApprovalClean(checker);
        var nativeReplay = await checker.Service.ApproveAsync(new(Company, "SENTETIK-checker", "SENTETIK-later", null),
            new("work-import-key"), new(Batch, "1"), default);
        Assert.NotNull(nativeReplay.DecisionReceipt); Assert.Equal(first.OriginalCorrelationId, nativeReplay.DecisionReceipt.OriginalCorrelationId);
        Assert.DoesNotContain("decisionReceipt", JsonSerializer.Serialize(nativeReplay, FinanceJson.CreateOptions()));
        var storedJson = await run.ScalarAsync<string>("SELECT ResultJson FROM finance.OfficialInvoiceImportCommands WHERE CommandName=@command", ("@command", FinanceOfficialInvoiceImportApprovalServiceV1.ApproveCommand));
        Assert.DoesNotContain("decisionReceipt", storedJson); using var stored = JsonDocument.Parse(storedJson);
        Assert.Equal(5, stored.RootElement.EnumerateObject().Count());
    }

    [SqlServerFact]
    public async Task Work_import_delegation_revoked_after_native_save_rolls_back_actual_audit_and_allows_same_key_retry()
    {
        await using var run = await Run.StartAsync(); await UploadForSafety(run);
        var permissions = new ImportPurposePermissions(); var execution = new WorkDecisionExecutionFenceV1();
        var authority = new WorkDecisionAuthorityV1(permissions, execution); var delegations = new ImportPurposeDelegations();
        var probe = new ApprovalSafetySaveProbe { AfterSave = () => delegations.Live = false };
        var native = new ApprovalSafetyAuthority(async () => (await authority.RevalidatePermissionScopeAsync(
            FinanceOfficialInvoiceImportPermissionNamesV1.Approve, Company)).IsGranted ? PermissionDecision.Allowed : PermissionDecision.Denied);
        await using var checker = run.Service(Company, "SENTETIK-checker", native, probe);
        var host = ImportPurposeHost(checker, authority, execution, delegations);
        var body = new WorkDecisionRequestV1("1", true, null, Guid.NewGuid());
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.OfficialInvoiceImport, Batch, body, "work-delegation-key", default))).Status);
        AssertApprovalClean(checker); await AssertOnlyUpload(run);
        delegations.Live = true; probe.AfterSave = null;
        var accepted = await host.DecideAsync(WorkDecisionHostV1.OfficialInvoiceImport, Batch, body, "work-delegation-key", default);
        Assert.False(accepted.Replayed); Assert.Equal("Approved", accepted.State); await AssertOneApproval(run);
    }

    [SqlServerFact]
    public async Task Legacy_marker_null_native_approval_replay_stays_successful_but_cannot_fabricate_Work_provenance()
    {
        await using var run = await Run.StartAsync();
        var request = Request(); var now = DateTimeOffset.UtcNow;
        var parsed = new FinanceOfficialInvoiceImportParserV1().Preview(new(Company, "SENTETIK-maker", "legacy-upload", null), request);
        var batch = new FinanceOfficialInvoiceImportBatchRow { Id = Batch, CompanyId = Company, BatchVersion = request.BatchVersion,
            BatchChecksum = parsed.SourceSha256, TemplateVersion = parsed.TemplateVersion, FileBytes = request.Utf8Csv.ToArray(),
            SourceRowsJson = JsonSerializer.Serialize(parsed.Batch!.Rows, FinanceJson.CreateOptions()), RowCount = parsed.Batch.Rows.Count,
            UploadedBy = "SENTETIK-maker", UploadedAtUtc = now.AddHours(-1), CorrelationId = "SENTETIK-legacy-upload" };
        var approved = new FinanceOfficialInvoiceImportBatchViewV1(Batch, Company, batch.BatchVersion, batch.BatchChecksum, batch.TemplateVersion,
            FinanceOfficialInvoiceImportApprovalStatusV1.Approved, "2", batch.RowCount, batch.UploadedBy, batch.UploadedAtUtc, "SENTETIK-checker", now);
        var old = new FinanceOfficialInvoiceImportApprovalResultV1(CommandOutcome.Succeeded, approved, false, "SENTETIK-legacy-approval", []);
        // Initial historical fixture, not a rewrite of a current receipt or backfill of provenance.
        await using (var seed = run.Context(Company))
        {
            seed.OfficialInvoiceImportBatches.Add(batch);
            seed.OfficialInvoiceImportApprovals.Add(new() { Id = Guid.NewGuid(), CompanyId = Company, BatchId = Batch,
                UploadedBy = batch.UploadedBy, ApprovedBy = "SENTETIK-checker", ApprovedAtUtc = now, CorrelationId = old.OriginalCorrelationId });
            seed.OfficialInvoiceImportCommands.Add(new() { Id = Guid.NewGuid(), CompanyId = Company, BatchId = Batch,
                CommandName = FinanceOfficialInvoiceImportApprovalServiceV1.ApproveCommand, IdempotencyKeyHash = FinanceCommandKeys.Hash("legacy-key"),
                ActorId = "SENTETIK-checker", PayloadHash = FinanceOfficialInvoiceImportApprovalServiceV1.ApprovalPayloadHash(new(Batch, "1")),
                ProducerSchemaVersion = null, ResultJson = JsonSerializer.Serialize(old, FinanceJson.CreateOptions()),
                CorrelationId = old.OriginalCorrelationId, RecordedAtUtc = now });
            await seed.SaveChangesAsync();
        }
        await using var checker = run.Service(Company, "SENTETIK-checker", new AllowAuthority());
        var native = await checker.Service.ApproveAsync(checker.Context, new("legacy-key"), new(Batch, "1"), default);
        Assert.Equal(CommandOutcome.Succeeded, native.Outcome); Assert.True(native.IsReplay); Assert.Null(native.DecisionReceipt);
        var execution = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(new ImportPurposePermissions(), execution);
        var host = ImportPurposeHost(checker, authority, execution, new ImportPurposeDelegations());
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.OfficialInvoiceImport, Batch, new("1", true, null), "legacy-key", default))).Status);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportWorkDeliveries"));
        AssertApprovalClean(checker);
    }

    private static WorkDecisionHostV1 ImportPurposeHost(Scope scope, WorkDecisionAuthorityV1 authority,
        WorkDecisionExecutionFenceV1 execution, ImportPurposeDelegations delegations)
    {
        var company = new CompanyContext(); company.Set(Company); var user = new User("SENTETIK-checker", Company);
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-work-import", null, "Web", null, null);
        var reader = new FinanceOfficialInvoiceImportPendingApprovalReadServiceV1(scope.Database, company, user,
            new FinanceOfficialInvoiceImportParserV1(), new AllowAuthority());
        var source = new NativeApprovalWorkInboxSourceV1(new FinanceInvoiceImportPendingApprovalAdapterV1(reader));
        return new(company, user, correlation, authority, [source], delegations, execution, null!, null!,
            officialInvoiceImports: scope.Service, officialInvoiceImportReads: reader);
    }
    private sealed class ImportPurposePermissions : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { Assert.Equal(FinanceOfficialInvoiceImportPermissionNamesV1.Approve, permission); Assert.Equal(Company, company);
          return Task.FromResult(new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class ImportPurposeDelegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, FinanceOfficialInvoiceImportPermissionNamesV1.Approve,
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), DateTimeOffset.UtcNow, "SENTETIK", null, null, null) : null);
    }
}
