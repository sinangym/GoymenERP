using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// NINV-SRC-01 on real SQL Server: the Logistics-owned native-invoice source is a company-filtered,
/// no-tracking read of one approved and non-cancelled dispatch note. It preserves every line's own
/// Sales order identity and creates no Logistics command/audit or Inventory stock effect. SENTETIK.
/// </summary>
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Approved_dispatch_invoice_source_translates_on_SQL_preserves_multi_order_lines_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var secondOrder = Guid.NewGuid();
        var firstOrderLine = Guid.NewGuid();
        var secondOrderLine = Guid.NewGuid();
        var firstProduct = Guid.NewGuid();
        var secondProduct = Guid.NewGuid();
        var firstInstructionLine = Guid.NewGuid();
        var secondInstructionLine = Guid.NewGuid();
        var firstShipment = Guid.NewGuid();
        var secondShipment = Guid.NewGuid();
        var noteId = Guid.NewGuid();

        var instruction = LoadingInstruction.Create(Guid.NewGuid(), CompanyA, owners.Party, owners.Account, T0);
        instruction.AddLine(instruction.Version, firstInstructionLine,
            new SourceOrderLineRef(owners.Order, firstOrderLine, 0), firstProduct, KgUnit, null, Kg(6m), T0);
        instruction.AddLine(instruction.Version, secondInstructionLine,
            new SourceOrderLineRef(secondOrder, secondOrderLine, 0), secondProduct, KgUnit, null, Kg(6m), T0);

        StoredLoadingInstruction stored;
        await using (var writer = run.Store(CompanyA))
        {
            stored = (await writer.Store.StoreInstructionAsync(run.Envelope("invoice-source-create"), Hash("invoice-source-create"), Day,
                () => instruction)).Instruction;
            stored = (await writer.Store.MutateInstructionAsync(run.Envelope("invoice-source-issue"), Hash("invoice-source-issue"),
                LogisticsCommands.IssueV1, instruction.Id,
                (value, now) => value.Issue(stored.Instruction.Version, now), Issued))!.Instruction;
            stored = (await writer.Store.MutateInstructionAsync(run.Envelope("invoice-source-load-a"), Hash("invoice-source-load-a"),
                LogisticsCommands.RecordLoadV1, instruction.Id,
                (value, now) => value.RecordLoad(stored.Instruction.Version, Guid.NewGuid(), firstInstructionLine, Kg(3m), MeasurementKind.Measured, now),
                Loaded))!.Instruction;
            stored = (await writer.Store.MutateInstructionAsync(run.Envelope("invoice-source-load-b"), Hash("invoice-source-load-b"),
                LogisticsCommands.RecordLoadV1, instruction.Id,
                (value, now) => value.RecordLoad(stored.Instruction.Version, Guid.NewGuid(), secondInstructionLine, Kg(4.5m), MeasurementKind.Measured, now),
                Loaded))!.Instruction;
            stored = (await writer.Store.MutateInstructionAsync(run.Envelope("invoice-source-ship-a"), Hash("invoice-source-ship-a"),
                LogisticsCommands.RecordShipmentV1, instruction.Id,
                (value, now) => value.MarkShipped(stored.Instruction.Version, firstShipment, firstInstructionLine, Kg(3m), now), Shipped))!.Instruction;
            stored = (await writer.Store.MutateInstructionAsync(run.Envelope("invoice-source-ship-b"), Hash("invoice-source-ship-b"),
                LogisticsCommands.RecordShipmentV1, instruction.Id,
                (value, now) => value.MarkShipped(stored.Instruction.Version, secondShipment, secondInstructionLine, Kg(4.5m), now), Shipped))!.Instruction;
            stored = (await writer.Store.MutateInstructionAsync(run.Envelope("invoice-source-draft"), Hash("invoice-source-draft"),
                LogisticsCommands.CreateDispatchNoteV1, instruction.Id,
                (value, now) => value.CreateDispatchNoteDraft(stored.Instruction.Version, noteId, [firstShipment, secondShipment], run.Actor, now),
                Drafted))!.Instruction;
        }

        // Draft, another company's row and an unknown id are all unavailable through the real translated query.
        await using (var draftReader = run.Store(CompanyA))
        {
            Assert.Null(await draftReader.Store.FindApprovedDispatchInvoiceSourceAsync(noteId));
            Assert.Null(await draftReader.Store.FindApprovedDispatchInvoiceSourceAsync(Guid.NewGuid()));
        }
        await using (var foreignReader = run.Store(CompanyB))
        {
            Assert.Null(await foreignReader.Store.FindApprovedDispatchInvoiceSourceAsync(noteId));
        }

        await using (var approver = run.Store(CompanyA))
        {
            stored = (await approver.Store.MutateInstructionAsync(run.Envelope("invoice-source-approve"), Hash("invoice-source-approve"),
                LogisticsCommands.ApproveDispatchNoteV1, instruction.Id,
                (value, now) => value.ApproveDispatchNote(stored.Instruction.Version, noteId, run.Actor, now), Approved))!.Instruction;
        }

        var logisticsBefore = await run.CountsAsync(instruction.Id);
        var inventoryBefore = await InventorySnapshotAsync();
        StoredApprovedDispatchInvoiceSource source;
        await using (var freshReader = run.Store(CompanyA))
        {
            source = Assert.IsType<StoredApprovedDispatchInvoiceSource>(
                await freshReader.Store.FindApprovedDispatchInvoiceSourceAsync(noteId));
        }
        await using (var approvedForeignReader = run.Store(CompanyB))
        {
            Assert.Null(await approvedForeignReader.Store.FindApprovedDispatchInvoiceSourceAsync(noteId));
        }

        Assert.Equal((CompanyA, noteId, instruction.Id, stored.Instruction.Version, owners.Party, owners.Account, "IRS-000001", (string?)null),
            (source.CompanyId, source.DispatchNoteId, source.InstructionId, source.InstructionVersion,
                source.CustomerPartyId, source.CompanyAccountId, source.Number!.Text, source.EDispatchNumber));
        Assert.NotNull(source.ApprovedAtUtc);
        Assert.Equal([
                (owners.Order, firstOrderLine, firstShipment, firstProduct, KgUnit, 3m),
                (secondOrder, secondOrderLine, secondShipment, secondProduct, KgUnit, 4.5m),
            ],
            source.Lines.Select(line => (line.SourceOrderId, line.SourceOrderLineId, line.ShipmentId,
                line.ProductId, line.UnitId, line.Quantity)).ToArray());
        Assert.Equal(logisticsBefore, await run.CountsAsync(instruction.Id));
        Assert.Equal(inventoryBefore, await InventorySnapshotAsync());

        await using (var canceller = run.Store(CompanyA))
        {
            stored = (await canceller.Store.MutateInstructionAsync(run.Envelope("invoice-source-cancel"), Hash("invoice-source-cancel"),
                LogisticsCommands.CancelDispatchNoteV1, instruction.Id,
                (value, now) => value.CancelDispatchNote(stored.Instruction.Version, noteId, run.Actor, "SENTETIK yanlış araç", now), Cancelled))!.Instruction;
        }
        await using (var cancelledReader = run.Store(CompanyA))
        {
            Assert.Null(await cancelledReader.Store.FindApprovedDispatchInvoiceSourceAsync(noteId));
        }

        async Task<(int Balances, int Movements, int Commands, int Outbox)> InventorySnapshotAsync() =>
            (await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Balances"),
                await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"),
                await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"),
                await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages"));
    }
}
