using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingPostingAccountMappingSqlServerRelationalAcceptanceTests
{
    // Real migrated SQL and SAME scoped actual Audit collector/writer. Authority/failures are controlled test seams.
    // Sources only: no actual SQL execution or runtime acceptance is claimed here.
    [SqlServerFact]
    public async Task Mapping_activation_purpose_read_and_original_command_proof_survive_mapping_and_active_plan_replacement()
    {
        await using var run = await Run.StartAsync(); var maker = await MappingSafetyPrepare(run);
        await using var scope = new MappingActivationScope(run);
        var read = await scope.Service.ReadDecisionAsync(1, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, read.Status); var pending = Assert.IsType<AccountingPostingAccountMappingDecisionDetailV1>(read.Resource);
        Assert.Equal(1L, pending.Mapping.MappingVersion); Assert.Equal(1L, pending.ExpectedVersion); Assert.Equal(0L, pending.ExpectedActiveMappingVersion);
        Assert.Equal(3, pending.Mapping.Rows.Count); Assert.Equal(5, pending.CurrentActiveAccountPlan.Accounts.Count);
        Assert.Equal(maker, pending.Mapping.PreparedByUserId);
        const string key = "mapping-original-receipt";
        var original = await scope.Service.ActivateAsync(1, new(pending.ExpectedVersion, pending.ExpectedActiveMappingVersion), key);
        Assert.Equal(AccountingApiStatus.Ok, original.Status);
        var proof = Assert.IsType<AccountingPostingAccountMappingActivationReceiptV1>(original.Resource?.ActivationReceipt);
        Assert.Equal(CompanyA, proof.CompanyId); Assert.Equal(1L, proof.MappingVersion); Assert.Equal(MappingSafetyActor, proof.ActorId);
        Assert.Equal(1L, proof.ExpectedVersion); Assert.Equal(0L, proof.ExpectedActiveMappingVersion); Assert.Equal(2L, proof.ResultVersion);
        Assert.Equal(scope.CorrelationId, proof.OriginalCorrelationId); Assert.Equal(pending.SourceFactsDigest, proof.SourceFactsDigest);
        Assert.Equal(AccountingPostingAccountMappingServiceV1.ActivationKeyHash(key), proof.IdempotencyKeyHash);
        Assert.Equal(AccountingPostingAccountMappingServiceV1.ActivationPayloadHash(1, 1, 0), proof.PayloadHash);
        var stored = await run.ScalarAsync<string>("SELECT ResultJson FROM accounting.Commands WHERE CompanyId=@c AND CommandName=@n AND IdempotencyKeyHash=@k",
            ("@c", CompanyA), ("@n", AccountingPostingAccountMappingCommandNamesV1.Activate), ("@k", proof.IdempotencyKeyHash));
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(JsonSerializer.Serialize(original with { IsReplay = false, OriginalCorrelationId = null }, json), stored);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stored))), proof.OriginalResultDigest);
        Assert.Equal(AccountingPostingAccountMappingServiceV1.ActivationOriginalResultDigest(original), proof.OriginalResultDigest);
        await MappingSafetyPrepare(run, 1);
        Assert.Equal(AccountingApiStatus.Ok, (await scope.Service.ActivateAsync(2, new(1, 1), "mapping-replacement")).Status);
        await ReplaceMappingDecisionActivePlan(scope);
        var counts = await run.CountsAsync();
        var current = (await scope.Service.ReadDecisionAsync(1, _ => Task.CompletedTask)).Resource!;
        Assert.Equal("Superseded", current.Mapping.Status); Assert.Equal(3L, current.ExpectedVersion); Assert.Equal(2L, current.ExpectedActiveMappingVersion);
        Assert.Equal(pending.PreparedAccountPlan, current.PreparedAccountPlan);
        Assert.Equal("SENTETIK-replacement-plan", current.CurrentActiveAccountPlan.Reference.SourceVersion);
        Assert.NotEqual(current.PreparedAccountPlan, current.CurrentActiveAccountPlan.Reference); Assert.Equal(2, current.CurrentActiveAccountPlan.Accounts.Count);
        Assert.Equal(pending.SourceFactsDigest, current.SourceFactsDigest); Assert.NotEqual(pending.PreviewDigest, current.PreviewDigest);
        Assert.Equal(JsonSerializer.Serialize(pending.Mapping.Rows, json), JsonSerializer.Serialize(current.Mapping.Rows, json));
        var replay = await scope.Activate(1, key);
        Assert.True(replay.IsReplay); Assert.Equal("Active", replay.Resource?.Status); Assert.Equal(2L, replay.Resource?.Version);
        Assert.Equal(scope.CorrelationId, replay.OriginalCorrelationId); Assert.Equal(proof, replay.Resource?.ActivationReceipt);
        Assert.Equal(JsonSerializer.Serialize(original.Resource, json), JsonSerializer.Serialize(replay.Resource, json));
        Assert.Equal(counts, await run.CountsAsync()); scope.AssertClean();
        var wire = JsonSerializer.SerializeToElement(replay, json); Assert.Equal(8, wire.EnumerateObject().Count());
        Assert.Equal(12, wire.GetProperty("resource").EnumerateObject().Count()); Assert.False(wire.GetProperty("resource").TryGetProperty("activationReceipt", out _));
        var changed = await scope.Service.ActivateAsync(1, new(1, 1), key);
        Assert.Equal(AccountingApiCodes.IdempotencyConflict, changed.Code); Assert.Null(changed.Resource);
        scope.User.Actor = Guid.NewGuid(); var otherActor = await scope.Activate(1, key);
        Assert.Equal(AccountingApiCodes.IdempotencyConflict, otherActor.Code); Assert.Null(otherActor.Resource); Assert.Equal(counts, await run.CountsAsync());
    }

    [SqlServerFact]
    public async Task Mapping_activation_original_receipt_read_fences_abort_all_native_effects_before_same_key_retry()
    {
        foreach (var phase in new[] { 5, 7, 8, 9, 11 }) // Save1/store2/lifecycle3/entry4/command5/source-entry6/row7/rows8/release9/owner10/precommit11.
        {
            await using var run = await Run.StartAsync(); await MappingSafetyPrepare(run); var before = await run.CountsAsync();
            var save = new MappingActivationSave(1); await using var scope = new MappingActivationScope(run, save); var phases = 0; var deny = true;
            scope.Authority.Read = () => !save.Saved || ++phases != phase || !deny;
            var refused = await scope.Activate(1, "mapping-receipt-fence");
            Assert.Equal(AccountingApiStatus.Denied, refused.Status); Assert.Null(refused.Resource); Assert.Equal(phase, phases);
            scope.AssertClean(); Assert.Equal(before, await run.CountsAsync()); Assert.Equal(1L, (await run.VersionAsync(1)).Version);
            deny = false;
            var approved = await scope.Activate(1, "mapping-receipt-fence"); var replay = await scope.Activate(1, "mapping-receipt-fence");
            Assert.Equal(AccountingApiStatus.Ok, approved.Status); Assert.NotNull(approved.Resource?.ActivationReceipt); Assert.True(replay.IsReplay);
            Assert.Equal(approved.Resource?.ActivationReceipt, replay.Resource?.ActivationReceipt); Assert.Equal(2, save.Attempts);
            Assert.Equal(new Counts(1, 3, before.Commands + 1, before.Audits + 1), await run.CountsAsync()); scope.AssertClean();
        }
    }

    [SqlServerFact]
    public async Task Mapping_activation_purpose_final_consumer_and_foreign_scope_never_release_source_facts_or_create_commands()
    {
        foreach (var fault in new[] { "permission", "company", "actor", "member", "cancel" })
        {
            await using var run = await Run.StartAsync(); var maker = await MappingSafetyPrepare(run); var before = await run.CountsAsync();
            await using var scope = new MappingActivationScope(run); var callbacks = 0; var allowed = true; using var cancellation = new CancellationTokenSource();
            scope.Authority.Read = () => allowed;
            Task Consumer(CancellationToken _)
            {
                // Twelve callbacks reach the fully assembled detail's final release fence.
                if (++callbacks == 12)
                {
                    if (fault == "permission") allowed = false; if (fault == "company") scope.Company.Set(CompanyB);
                    if (fault == "actor") scope.User.Actor = maker; if (fault == "member") scope.User.Member = false;
                    if (fault == "cancel") cancellation.Cancel();
                }
                return Task.CompletedTask;
            }
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Service.ReadDecisionAsync(1, Consumer, cancellation.Token));
            else { var denied = await scope.Service.ReadDecisionAsync(1, Consumer); Assert.Equal(AccountingApiStatus.Denied, denied.Status); Assert.Null(denied.Resource); }
            Assert.Equal(12, callbacks); Assert.Equal(before, await run.CountsAsync()); scope.AssertClean();
            scope.Company.Set(CompanyA); scope.User.Actor = maker; scope.User.Member = true; allowed = true;
            var own = await scope.Service.ReadDecisionAsync(1, _ => Task.CompletedTask);
            Assert.Equal(AccountingPostingAccountMappingCodesV1.SelfActivation, own.Code); Assert.Empty(scope.Db.ChangeTracker.Entries()); Assert.Equal(before, await run.CountsAsync());
            scope.Company.Set(CompanyB); scope.User.Actor = MappingSafetyActor;
            var native = await scope.Service.ReadDecisionAsync(1, _ => Task.CompletedTask);
            Assert.Equal(AccountingApiStatus.Denied, native.Status); Assert.Null(native.Resource);
            Assert.Null(await scope.Store.ReadActivationReceiptSourceAsync(1, _ => Task.CompletedTask));
            Assert.Equal(before, await run.CountsAsync()); scope.AssertClean();
        }
    }

    private static async Task ReplaceMappingDecisionActivePlan(MappingActivationScope scope)
    {
        // Existing native catalog command, not a direct table mutation or a second acceptance/approval engine.
        var accounts = Encoding.UTF8.GetBytes(ApprovedAccountingSourcePackageTemplateV1.AccountsHeader + "\n"
            + $"{Receivable120:D},120.02,SENTETIK current receivable\n{Revenue600:D},600.02,SENTETIK current revenue\n");
        var source = Guid.NewGuid(); var currency = Guid.NewGuid();
        var bindings = Encoding.UTF8.GetBytes(ApprovedAccountingSourcePackageTemplateV1.MappingsHeader + "\n"
            + $"{source:D},{NormalJournalSourceKindsV1.RecordedManualReceipt},v1,profile,profile-v1,{currency:D},receipt,rule-v1,debit,Debit,{Receivable120:D}\n"
            + $"{source:D},{NormalJournalSourceKindsV1.RecordedManualReceipt},v1,profile,profile-v1,{currency:D},receipt,rule-v1,credit,Credit,{Revenue600:D}\n");
        var store = new ApprovedAccountingSourceCatalogStoreV1(scope.Db, scope.Company, scope.Collector, TimeProvider.System, scope.Core);
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-replacement-plan", null, "Web", null, null);
        var service = new ApprovedAccountingSourceCatalogServiceV1(new ApprovedAccountingSourcePackageParserV1(), store, scope.Core, scope.Authority,
            scope.Company, scope.User, correlation, TimeProvider.System); var package = Guid.NewGuid();
        var prepared = await service.PrepareAsync(new(package, "SENTETIK-replacement-plan", accounts, bindings,
            Convert.ToHexString(SHA256.HashData(accounts)), Convert.ToHexString(SHA256.HashData(bindings))), "mapping-current-plan-prepare");
        Assert.Equal(AccountingApiStatus.Created, prepared.Status); scope.AssertClean();
        Assert.Equal(AccountingApiStatus.Ok, (await service.ActivateAsync(package, new(1, 1), "mapping-current-plan-activate")).Status); scope.AssertClean();
    }
}
