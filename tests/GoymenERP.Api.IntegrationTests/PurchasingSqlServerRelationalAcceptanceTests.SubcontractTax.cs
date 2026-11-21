using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Subcontract_tax_revision_preserves_actual_accepted_physical_history_and_reads_approval_proof_inside_the_owner_transaction()
    {
        await using var run = await Run.StartAsync(); var draft = SubcontractTaxDraft(run);
        await using var scope = ServiceScope(run, CompanyA);
        await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create, run.Envelope("tax-create"), Hash("tax-create"), "SENTETIK",
            (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default);
        await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Approve, run.Envelope("tax-approve"), Hash("tax-approve"), "SENTETIK",
            async (old, ct) => { Assert.False(await scope.Store.HasApprovedPhysicalReferencesAsync(old!, ct)); return PurchaseServiceOrder.Approve(old!, 1, "SENTETIK-checker", old!.CreatedAtUtc); },
            _ => Task.CompletedTask, default);
        await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Accept, run.Envelope("tax-accept"), Hash("tax-accept"), "SENTETIK",
            (old, _) => Task.FromResult(PurchaseServiceOrder.Accept(old!, 2, Guid.NewGuid(), draft.Lines[0].LineId, 4m, Day, run.Actor, "SENTETIK kabul", old!.CreatedAtUtc)),
            _ => Task.CompletedTask, default);
        var envelope = run.Envelope("tax-revision"); var tax = TaxChange(draft);
        var revised = await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.ReviseTax, envelope, Hash("tax-revision"), "SENTETIK",
            (old, _) => Task.FromResult(PurchaseServiceOrder.ReviseTax(old!, 3, draft.Lines[0].LineId, tax, run.Actor, "SENTETIK vergi değişikliği", old!.CreatedAtUtc)),
            _ => Task.CompletedTask, default);
        Assert.Equal(PurchaseServiceOrderStatus.Draft, revised!.Order!.Status); Assert.Single(revised.Order.Acceptances);
        Assert.Equal(4m, revised.Order.Lines[0].AcceptedQuantity); Assert.Equal(draft.Materials, revised.Order.Materials);
        Assert.True((await scope.Store.ReplayAsync(PurchaseServiceOrderCommandsV1.ReviseTax, envelope, Hash("tax-revision"), default))!.IsReplay);
        await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Approve, run.Envelope("tax-reapprove"), Hash("tax-reapprove"), "SENTETIK",
            async (old, ct) =>
            {
                Assert.True(await scope.Store.HasApprovedPhysicalReferencesAsync(old!, ct));
                Assert.False(await scope.Store.HasApprovedTaxSnapshotAsync(old!, draft.Lines[0].LineId, ct));
                Assert.False(await scope.Store.HasApprovedPhysicalReferencesAsync(old! with { Materials = [old!.Materials[0] with { LabelId = Guid.NewGuid() }] }, ct));
                return PurchaseServiceOrder.Approve(old!, 4, "SENTETIK-other-checker", old!.CreatedAtUtc);
            }, _ => Task.CompletedTask, default);
        Assert.Equal(5, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderRevisions"));
        Assert.Equal(5, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Purchasing.ServiceOrder'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        await using var foreign = ServiceScope(run, CompanyB);
        await Assert.ThrowsAsync<InvalidOperationException>(() => foreign.Store.HasApprovedPhysicalReferencesAsync(revised.Order, default));
    }
    [SqlServerFact]
    public async Task Late_authority_loss_rolls_back_subcontract_tax_revision_attempt_and_Audit_together()
    {
        await using var run = await Run.StartAsync(); var draft = SubcontractTaxDraft(run);
        await using var scope = ServiceScope(run, CompanyA);
        await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create, run.Envelope("tax-rollback-create"), Hash("tax-rollback-create"), "SENTETIK",
            (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default);
        var fences = 0;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.ReviseTax,
            run.Envelope("tax-rollback"), Hash("tax-rollback"), "SENTETIK",
            (old, _) => Task.FromResult(PurchaseServiceOrder.ReviseTax(old!, 1, draft.Lines[0].LineId, TaxChange(draft), run.Actor, "SENTETIK", old!.CreatedAtUtc)),
            _ => { if (++fences > 3) throw new UnauthorizedAccessException("SENTETIK fresh fence after Audit append"); return Task.CompletedTask; }, default));
        Assert.Equal(1, (await scope.Store.ReadAsync(draft.OrderId, default))!.Version);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderRevisions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Purchasing.ServiceOrder'"));
    }
    private static PurchaseServiceOrderState SubcontractTaxDraft(Run run)
    {
        var value = ServiceDraft(run);
        return PurchaseServiceOrder.Create(value with { Kind = PurchaseServiceOrderKind.OwnMaterialSubcontract,
            Lines = [value.Lines[0] with { Tax = new(true, 20m, null, false, null, null, 1) }],
            Materials = [new(Guid.NewGuid(), "1", Guid.NewGuid(), "1", 10m)], ExpectedOutputs = [new(Guid.NewGuid(), "1", 9m)] });
    }
    private static PurchaseServiceTax TaxChange(PurchaseServiceOrderState draft) => new(true, 10m, null, false, null, null, 1,
        new("SENTETIK-lists:2", "10", null, "SENTETIK-editor", "SENTETIK vergi değişikliği", draft.CreatedAtUtc));
}
