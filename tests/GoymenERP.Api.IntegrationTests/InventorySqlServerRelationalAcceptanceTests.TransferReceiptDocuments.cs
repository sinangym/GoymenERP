using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Settings.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

// Actual Inventory document/receipt and Audit SQL writes; authority/profile boundaries below are explicitly synthetic.
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Transfer_receipt_requires_its_own_actual_effective_document_and_preserves_immutable_draft_replay()
    {
        await using var run = await Run.StartAsync();
        var plan = await SeedDepartureTransferAsync(run, "SYN-INCOMING-DOCUMENT");
        await using var docs = ReceiptDocumentScope(run);
        var metadata = ReceiptDocumentMetadata(run, "incoming-create");
        var input = new CreateInventoryTransferReceiptDocumentV1(Guid.NewGuid(), plan.TransferId, "2", At.AddHours(2),
            [new(Guid.NewGuid(), plan.Lines[0].LineId, "5")]);
        var draft = await docs.Owner.CreateAsync(input, metadata);
        var line = Assert.Single(draft.Document.Lines);
        var receive = new ReceiveInventoryTransferLineV1("2", input.ReceivedAtUtc,
            new(InventoryTransferReceiptDocumentV1.Kind, input.DocumentId, line.DocumentLineId), "5", null, "2");
        await using (var stock = run.Store(CompanyA))
        {
            var denied = await Assert.ThrowsAsync<InventoryTransferException>(() => stock.Store.ReceiveLineAsync(plan.TransferId,
                line.TransferLineId, receive, Metadata(run), Authorized));
            Assert.Equal(InventoryTransferReceiptDocumentV1.NotEffective, denied.Code);
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferAcceptances WHERE TransferId=@id", ("@id", plan.TransferId)));
        var effective = await docs.Owner.FinalizeAsync(input.DocumentId, new("1", "2", "SENTETIK kontrol edildi"),
            ReceiptDocumentMetadata(run, "incoming-finalize"));
        var original = await docs.Owner.CreateAsync(input, metadata with { CorrelationId = "SENTETIK retry" });
        Assert.True(original.IsReplay); Assert.Equal("Draft", original.Document.Status);
        Assert.Equal(draft.OriginalCorrelationId, original.OriginalCorrelationId);
        await using (var stock = run.Store(CompanyA))
        {
            var received = await stock.Store.ReceiveLineAsync(plan.TransferId, line.TransferLineId,
                receive with { ExpectedIncomingDocumentVersion = effective.Document.Version }, Metadata(run), Authorized);
            Assert.Equal("Accepted", received.Transfer.Status);
        }
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferReceiptDocumentVersions WHERE DocumentId=@id", ("@id", input.DocumentId)));
    }

    [SqlServerFact]
    public async Task Transfer_receipt_document_mismatch_changes_no_physical_stock_and_another_company_cannot_read_it()
    {
        await using var run = await Run.StartAsync();
        var plan = await SeedDepartureTransferAsync(run, "SYN-INCOMING-BINDING");
        var receive = await RecordIncomingReceiptDocumentAsync(run, plan.TransferId, plan.Lines[0].LineId, "5", null);
        await using (var stock = run.Store(CompanyA))
        {
            foreach (var invalid in new[] { receive with { ReceivedKg = "4" }, receive with { ReceivedAtUtc = receive.ReceivedAtUtc.AddMinutes(1) },
                         receive with { IncomingDocument = receive.IncomingDocument with { LineId = Guid.NewGuid() } } })
            {
                var failure = await Assert.ThrowsAsync<InventoryTransferException>(() => stock.Store.ReceiveLineAsync(plan.TransferId,
                    plan.Lines[0].LineId, invalid, Metadata(run), Authorized));
                Assert.Equal(InventoryTransferReceiptDocumentV1.BindingConflict, failure.Code);
            }
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferAcceptances WHERE TransferId=@id", ("@id", plan.TransferId)));
        await using var foreign = ReceiptDocumentScope(run, CompanyB);
        var hidden = await Assert.ThrowsAsync<InventoryTransferException>(() => foreign.Owner.ListAsync(plan.TransferId));
        Assert.Equal(404, hidden.Status);
    }

    [SqlServerFact]
    public async Task Transfer_receipt_document_late_authority_loss_rolls_back_the_real_Audit_owner_and_history_key_together()
    {
        await using var run = await Run.StartAsync();
        var plan = await SeedDepartureTransferAsync(run, "SYN-INCOMING-ROLLBACK");
        var fault = new ReceiptDocumentAuthority();
        await using var docs = ReceiptDocumentScope(run, authority: fault);
        // Synthetic authority fault observes the genuine Audit write in the same borrowed transaction.
        fault.Probe = async token =>
        {
            var transaction = docs.Database.Database.CurrentTransaction;
            return transaction is not null && await docs.Database.Set<GoymenERP.Modules.Audit.Domain.Events.AuditEvent>()
                .AsNoTracking().AnyAsync(x => x.EntityType == "Inventory.TransferReceiptDocument", token);
        };
        var input = new CreateInventoryTransferReceiptDocumentV1(Guid.NewGuid(), plan.TransferId, "2", At.AddHours(2),
            [new(Guid.NewGuid(), plan.Lines[0].LineId, "5")]);
        var error = await Assert.ThrowsAsync<InventoryTransferException>(() => docs.Owner.CreateAsync(input,
            ReceiptDocumentMetadata(run, "incoming-permission-fault")));
        Assert.Equal(403, error.Status);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferReceiptDocumentVersions"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE CommandName=N'inventory.transfer.receipt-document.create.v1'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Inventory.TransferReceiptDocument'"));
    }

    [SqlServerFact]
    public async Task Transfer_receipt_document_keyset_continuation_reaches_every_actual_history_after_fifty_documents()
    {
        await using var run = await Run.StartAsync();
        var plan = await SeedDepartureTransferAsync(run, "SYN-INCOMING-PAGINATION");
        await using var docs = ReceiptDocumentScope(run);
        for (var i = 0; i < 51; ++i)
            await docs.Owner.CreateAsync(new(Guid.NewGuid(), plan.TransferId, "2", At.AddHours(2),
                [new(Guid.NewGuid(), plan.Lines[0].LineId, "5")]), ReceiptDocumentMetadata(run, "incoming-page-" + i));
        var first = await docs.Owner.ListAsync(plan.TransferId);
        Assert.Equal(50, first.Items.Count); Assert.True(first.SearchWindowFull); Assert.NotNull(first.NextCursor);
        var last = await docs.Owner.ListAsync(plan.TransferId, first.NextCursor);
        Assert.Single(last.Items); Assert.False(last.SearchWindowFull); Assert.Null(last.NextCursor);
        Assert.Equal(51, first.Items.Concat(last.Items).Select(x => x.DocumentId).Distinct().Count());
        await using var foreign = ReceiptDocumentScope(run, CompanyB);
        await Assert.ThrowsAsync<InventoryTransferException>(() => foreign.Owner.ListAsync(plan.TransferId, first.NextCursor));
    }

    [SqlServerFact]
    public async Task Transfer_receipt_document_history_forward_Down_refuses_before_losing_actual_immutable_versions()
    {
        await using var run = await Run.StartAsync();
        var plan = await SeedDepartureTransferAsync(run, "SYN-INCOMING-DOWN");
        _ = await RecordIncomingReceiptDocumentAsync(run, plan.TransferId, plan.Lines[0].LineId, "5", null);
        await using var database = run.Context(CompanyA);
        var migrations = database.Database.GetMigrations().ToArray();
        Assert.EndsWith("_AddInventoryTransferReceiptDocuments", migrations[^1], StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<SqlException>(() => database.GetService<IMigrator>().MigrateAsync(migrations[^2]));
        Assert.Equal(51999, error.Number);
        Assert.Equal(2, await database.TransferReceiptDocumentVersions.CountAsync());
        Assert.Equal(migrations[^1], (await database.Database.GetAppliedMigrationsAsync()).Last());
    }

    [SqlServerFact]
    public async Task Transfer_action_candidates_page_actual_pending_transfers_with_only_the_selected_command_permission()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedDepartureTransferAsync(run, "SYN-ACTION-PAGE-1");
        var second = await SeedDepartureTransferAsync(run, "SYN-ACTION-PAGE-2");
        var company = new CompanyContext(); company.Set(CompanyA);
        var authority = new ReceiptWorkAuthority(InventoryPermissionManifestV1.StockTransferWrite);
        await using var database = run.Context(CompanyA); await using var reads = run.Store(CompanyA);
        using var codec = new InventoryStockPositionCursorCodec(new(Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray())), TimeProvider.System);
        var owner = new InventoryTransferReceiptWork(company, new FixedCurrentUser(run.Actor, CompanyA), authority,
            reads.Transfers, (IInventoryTransferReceiptReaderV1)reads.Store, database, codec);
        var page = await owner.SearchAsync("receive", "SYN-ACTION-PAGE", limit: 1);
        Assert.Single(page.Items); Assert.True(page.SearchWindowFull); Assert.NotNull(page.NextCursor);
        var next = await owner.SearchAsync("receive", "SYN-ACTION-PAGE", page.NextCursor, 1);
        Assert.Single(next.Items); Assert.False(next.SearchWindowFull);
        Assert.Equal(new[] { first.TransferId, second.TransferId }.Order(), page.Items.Concat(next.Items).Select(x => x.Transfer.TransferId).Order());
        Assert.All(page.Items.Concat(next.Items), item => Assert.Contains(item.LabelCodes, code => code.StartsWith("SYN-ACTION-PAGE", StringComparison.Ordinal)));
        Assert.All(authority.Permissions, value => Assert.Equal(InventoryPermissionManifestV1.StockTransferWrite, value));
        Assert.Equal(400, (await Assert.ThrowsAsync<InventoryTransferException>(() => owner.SearchAsync("receive", "different", page.NextCursor, 1))).Status);
        authority.Deny = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryTransferException>(() => owner.SearchAsync("receive"))).Status);
    }

    [SqlServerFact]
    public async Task Transfer_overage_action_candidates_use_actual_undecided_receipt_evidence_without_report_or_write_grants()
    {
        await using var run = await Run.StartAsync();
        var plan = await SeedDepartureTransferAsync(run, "SYN-ACTION-OVERAGE");
        var receive = await RecordIncomingReceiptDocumentAsync(run, plan.TransferId, plan.Lines[0].LineId, "6", "SENTETIK fazla");
        await using (var stock = run.Store(CompanyA))
            _ = await stock.Store.ReceiveLineAsync(plan.TransferId, plan.Lines[0].LineId, receive, Metadata(run), Authorized);
        var company = new CompanyContext(); company.Set(CompanyA);
        var authority = new ReceiptWorkAuthority(InventoryPermissionManifestV1.StockTransferOverageApprove);
        await using var database = run.Context(CompanyA); await using var reads = run.Store(CompanyA);
        var owner = new InventoryTransferReceiptWork(company, new FixedCurrentUser(run.Actor, CompanyA), authority,
            reads.Transfers, (IInventoryTransferReceiptReaderV1)reads.Store, database);
        var page = await owner.SearchAsync("overage-decision", "SYN-ACTION-OVERAGE");
        Assert.Equal(plan.TransferId, Assert.Single(page.Items).Transfer.TransferId);
        var detail = await owner.ReadAsync(plan.TransferId, "overage-decision");
        Assert.Equal("Accepted", detail.Transfer.Status);
        Assert.All(authority.Permissions, value => Assert.Equal(InventoryPermissionManifestV1.StockTransferOverageApprove, value));
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryTransferException>(() => owner.SearchAsync("receive"))).Status);
        var foreignCompany = new CompanyContext(); foreignCompany.Set(CompanyB);
        var foreign = new InventoryTransferReceiptWork(foreignCompany, new FixedCurrentUser(run.Actor, CompanyA), authority,
            reads.Transfers, (IInventoryTransferReceiptReaderV1)reads.Store, database);
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryTransferException>(() => foreign.SearchAsync("overage-decision"))).Status);
    }

    private sealed class ReceiptWorkAuthority(string allowed) : IErpAuthorityRevalidationService
    {
        public List<string> Permissions { get; } = [];
        public bool Deny { get; set; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Permissions.Add(permission);
            return Task.FromResult(Deny || permission != allowed || companyId != CompanyA ? PermissionScope.Denied
                : new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal) { "SENTETIK" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private static async Task<ReceiveInventoryTransferLineV1> RecordIncomingReceiptDocumentAsync(Run run, Guid transferId,
        Guid lineId, string receivedKg, string? overageReason, DateTimeOffset? receivedAt = null)
    {
        await using var docs = ReceiptDocumentScope(run);
        var transfer = await docs.Transfers.ReadAsync(transferId) ?? throw new InvalidOperationException("SENTETIK transfer missing");
        var input = new CreateInventoryTransferReceiptDocumentV1(Guid.NewGuid(), transferId, transfer.Version, receivedAt ?? At.AddHours(2),
            [new(Guid.NewGuid(), lineId, receivedKg, overageReason)]);
        var created = await docs.Owner.CreateAsync(input, ReceiptDocumentMetadata(run, Guid.NewGuid().ToString("N")));
        var effective = await docs.Owner.FinalizeAsync(input.DocumentId, new(created.Document.Version, transfer.Version, "SENTETIK kontrol edildi"),
            ReceiptDocumentMetadata(run, Guid.NewGuid().ToString("N")));
        return new(transfer.Version, input.ReceivedAtUtc, new(InventoryTransferReceiptDocumentV1.Kind, input.DocumentId,
            Assert.Single(effective.Document.Lines).DocumentLineId), receivedKg, overageReason, effective.Document.Version);
    }
    private static async Task<AcceptInventoryTransferLineV1> RecordIncomingAcceptanceDocumentAsync(Run run, Guid transferId,
        Guid lineId, DateTimeOffset at)
    {
        await using var scope = run.Store(CompanyA);
        var transfer = await scope.Transfers.ReadAsync(transferId) ?? throw new InvalidOperationException("SENTETIK missing transfer");
        var line = transfer.Lines.Single(x => x.LineId == lineId);
        var request = await RecordIncomingReceiptDocumentAsync(run, transferId, lineId, line.ActualShippedKg!, null, at);
        return new(request.ExpectedTransferVersion, at, request.IncomingDocument, request.ExpectedIncomingDocumentVersion);
    }
    private static InventoryCommandMetadata ReceiptDocumentMetadata(Run run, string key) =>
        Metadata(run) with { IdempotencyKey = key, PermissionCode = InventoryPermissionManifestV1.StockTransferWrite };
    private static ReceiptDocumentScopeHandle ReceiptDocumentScope(Run run, Guid? companyId = null, ReceiptDocumentAuthority? authority = null)
    {
        var scope = run.Factory.Services.CreateAsyncScope(); var id = companyId ?? CompanyA;
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(id);
        var actor = new FixedCurrentUser(run.Actor, id); var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var transfers = scope.ServiceProvider.GetRequiredService<IInventoryTransferReaderV1>();
        var cursor = new InventoryStockPositionCursorCodec(new(Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray())), TimeProvider.System);
        return new(scope, database, transfers, new(database, company, actor, authority ?? new(), transfers,
            scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>(),
            new InventoryKilogramScale(new ApprovedSettingsPrerequisiteProvider(company, actor)), TimeProvider.System, cursor), cursor);
    }
    private sealed class ReceiptDocumentScopeHandle(AsyncServiceScope scope, InventoryDbContext database,
        IInventoryTransferReaderV1 transfers, InventoryTransferReceiptDocuments owner, IDisposable cursor) : IAsyncDisposable
    {
        public InventoryTransferReceiptDocuments Owner => owner;
        public InventoryDbContext Database => database;
        public IInventoryTransferReaderV1 Transfers => transfers;
        public async ValueTask DisposeAsync() { cursor.Dispose(); await scope.DisposeAsync(); }
    }
    private sealed class ReceiptDocumentAuthority : IErpAuthorityRevalidationService
    {
        public Func<CancellationToken, Task<bool>>? Probe { get; set; }
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Probe is not null && await Probe(cancellationToken) ? PermissionScope.Denied
                : new(true, true, new HashSet<string>(StringComparer.Ordinal) { "SENTETIK" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
    }
}
