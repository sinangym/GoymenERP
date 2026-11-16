using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D367 on real SQL Server: the Logistics open-instruction quantity read counts only issued instructions, answers
/// instructed minus shipped per order line (loaded-but-unshipped still open, several issued instructions summed), leaves a
/// draft, a cancelled instruction and a fully shipped line out, answers only under the context company, and LOJ-D18: a
/// cancelled instruction keeps its line allocation id while a new instruction for the same order line names the same
/// allocation. SENTETIK.
/// </summary>
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_planned_quantity_read_sums_issued_instructed_minus_shipped_and_omits_drafts_cancelled_and_fully_shipped_lines()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var partlyShippedLine = Guid.NewGuid();
        var summedLine = Guid.NewGuid();
        var draftLine = Guid.NewGuid();
        var cancelledLine = Guid.NewGuid();
        var fullyShippedLine = Guid.NewGuid();
        var partlyShipped = InstructionForOrderLine(owners, partlyShippedLine, 6m);
        var summedFirst = InstructionForOrderLine(owners, summedLine, 3m);
        var summedSecond = InstructionForOrderLine(owners, summedLine, 4m);
        var draft = InstructionForOrderLine(owners, draftLine, 5m);
        var cancelled = InstructionForOrderLine(owners, cancelledLine, 7m);
        var fullyShipped = InstructionForOrderLine(owners, fullyShippedLine, 2m);
        await using (var a = run.Store(CompanyA))
        {
            foreach (var (instruction, key) in new[]
                     {
                         (partlyShipped, "partly"), (summedFirst, "sum-1"), (summedSecond, "sum-2"), (draft, "draft"),
                         (cancelled, "cancelled"), (fullyShipped, "full"),
                     })
            {
                await a.Store.StoreInstructionAsync(run.Envelope($"k-plan-{key}-create"), Hash($"plan-{key}-create"), Day, () => instruction);
                if (instruction != draft)
                {
                    await a.Store.MutateInstructionAsync(run.Envelope($"k-plan-{key}-issue"), Hash($"plan-{key}-issue"), LogisticsCommands.IssueV1,
                        instruction.Id, (stored, now) => stored.Issue(2, now), Issued);
                }
            }

            // 6 instructed, 4 loaded, 2.5 shipped: 3.5 stays open (loaded-but-unshipped quantity is still planned).
            var partlyLine = Assert.Single(partlyShipped.Lines).Id;
            await a.Store.MutateInstructionAsync(run.Envelope("k-plan-partly-load"), Hash("plan-partly-load"), LogisticsCommands.RecordLoadV1,
                partlyShipped.Id, (stored, now) => stored.RecordLoad(3, Guid.NewGuid(), partlyLine, Kg(4m), MeasurementKind.Measured, now), Loaded);
            await a.Store.MutateInstructionAsync(run.Envelope("k-plan-partly-ship"), Hash("plan-partly-ship"), LogisticsCommands.RecordShipmentV1,
                partlyShipped.Id, (stored, now) => stored.MarkShipped(4, Guid.NewGuid(), partlyLine, Kg(2.5m), now), Shipped);

            var fullLine = Assert.Single(fullyShipped.Lines).Id;
            await a.Store.MutateInstructionAsync(run.Envelope("k-plan-full-load"), Hash("plan-full-load"), LogisticsCommands.RecordLoadV1,
                fullyShipped.Id, (stored, now) => stored.RecordLoad(3, Guid.NewGuid(), fullLine, Kg(2m), MeasurementKind.Measured, now), Loaded);
            await a.Store.MutateInstructionAsync(run.Envelope("k-plan-full-ship"), Hash("plan-full-ship"), LogisticsCommands.RecordShipmentV1,
                fullyShipped.Id, (stored, now) => stored.MarkShipped(4, Guid.NewGuid(), fullLine, Kg(2m), now), Shipped);

            await a.Store.MutateInstructionAsync(run.Envelope("k-plan-cancelled-cancel"), Hash("plan-cancelled-cancel"),
                LogisticsCommands.CancelInstructionV1, cancelled.Id,
                (stored, now) => stored.CancelInstruction(3, run.Actor, "SENTETIK neden", now), InstructionCancelled);
        }

        var otherOrder = Guid.NewGuid();
        await using (var read = run.LogisticsContext(CompanyA))
        {
            var answer = await read.PlannedQuantity.ReadOpenInstructionQuantitiesAsync(CompanyA, [owners.Order, otherOrder, owners.Order]);

            Assert.Equal((LogisticsSalesOrderPlannedQuantityOutcomeV1.Found, (string?)null, CompanyA, false),
                (answer.Outcome, answer.ReasonCode, answer.CompanyId, answer.HasMore));
            Assert.Equal(new[] { owners.Order, otherOrder }.Order().ToArray(), answer.AnsweredOrderIds.ToArray());
            var expected = new[] { (partlyShippedLine, 3.5m), (summedLine, 7m) }.OrderBy(line => line.Item1).ToArray();
            Assert.Equal(expected, answer.Lines.Select(line => (line.OrderLineId, line.OpenInstructionQuantity)).ToArray());
            Assert.All(answer.Lines, line => Assert.Equal((owners.Order, KgUnit, QuantityUnit.Kilogram), (line.OrderId, line.UnitId, line.QuantityUnit)));
            Assert.DoesNotContain(answer.Lines, line => line.OrderLineId == draftLine || line.OrderLineId == cancelledLine
                || line.OrderLineId == fullyShippedLine);
        }

        // Company B never reads company A's plan: asked under A it is denied, asked under B the same order has no line.
        await using (var b = run.LogisticsContext(CompanyB))
        {
            var denied = await b.PlannedQuantity.ReadOpenInstructionQuantitiesAsync(CompanyA, [owners.Order]);
            Assert.Equal((LogisticsSalesOrderPlannedQuantityOutcomeV1.Denied, (string?)LogisticsSalesOrderPlannedQuantityReasonCodesV1.CompanyMismatch, 0),
                (denied.Outcome, denied.ReasonCode, denied.Lines.Count));
            var own = await b.PlannedQuantity.ReadOpenInstructionQuantitiesAsync(CompanyB, [owners.Order]);
            Assert.Equal((LogisticsSalesOrderPlannedQuantityOutcomeV1.Found, 0, owners.Order),
                (own.Outcome, own.Lines.Count, Assert.Single(own.AnsweredOrderIds)));
        }
    }

    [SqlServerFact]
    public async Task A_cancelled_instruction_keeps_its_line_allocation_id_and_a_new_instruction_names_the_same_allocation()
    {
        // LOJ-D18: the cancel does not release the allocation; it stays with the Sales order line for the next instruction.
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var orderLineId = Guid.NewGuid();
        var allocationId = Guid.NewGuid();
        var first = InstructionWithAllocation(owners, orderLineId, allocationId);
        var second = InstructionWithAllocation(owners, orderLineId, allocationId);
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.StoreInstructionAsync(run.Envelope("k-alloc-first"), Hash("alloc-first"), Day, () => first);
            await a.Store.MutateInstructionAsync(run.Envelope("k-alloc-first-issue"), Hash("alloc-first-issue"), LogisticsCommands.IssueV1,
                first.Id, (stored, now) => stored.Issue(2, now), Issued);
            await a.Store.MutateInstructionAsync(run.Envelope("k-alloc-first-cancel"), Hash("alloc-first-cancel"),
                LogisticsCommands.CancelInstructionV1, first.Id,
                (stored, now) => stored.CancelInstruction(3, run.Actor, "SENTETIK neden", now), InstructionCancelled);
            await a.Store.StoreInstructionAsync(run.Envelope("k-alloc-second"), Hash("alloc-second"), Day, () => second);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM logistics.LoadingInstructionLines WHERE InstructionId = @id AND AllocationId = @allocation",
            ("@id", first.Id), ("@allocation", allocationId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM logistics.LoadingInstructionLines WHERE InstructionId = @id AND AllocationId = @allocation",
            ("@id", second.Id), ("@allocation", allocationId)));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT [Status] FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", first.Id)));
        await using (var a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindInstructionAsync(first.Id))!.Instruction;
            Assert.Equal((LoadingInstructionStatus.Cancelled, (Guid?)allocationId), (rebuilt.Status, Assert.Single(rebuilt.Lines).AllocationId));
        }
    }

    private static LoadingInstruction InstructionWithAllocation(Owners owners, Guid orderLineId, Guid allocationId)
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), owners.Company, owners.Party, owners.Account, T0);
        instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(owners.Order, orderLineId, 0),
            Guid.NewGuid(), KgUnit, allocationId, Kg(6m), T0);
        return instruction;
    }
}
