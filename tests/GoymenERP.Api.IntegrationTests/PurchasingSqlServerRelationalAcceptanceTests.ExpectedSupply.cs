using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Expected_supply_read_freshens_accepted_quantity_without_creating_a_second_effect()
    {
        await using Run run = await Run.StartAsync();
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        long approvedVersion;

        await using (StoreScope scope = run.Store(CompanyA))
        {
            PurchaseOrder order = PurchaseOrder.Create(orderId, CompanyA, AcceptanceSupplier, AcceptanceTime);
            order.AddLine(
                order.Version,
                lineId,
                AcceptanceProduct,
                AcceptanceUnit,
                Quantity.Of(100m, QuantityUnit.Kilogram),
                sourceRequestLineId: null,
                AcceptanceTime);
            string createKey = $"expected-supply-create-{orderId:N}";
            StoredPurchaseOrder stored = (await scope.Store.StoreOrderAsync(
                Envelope(createKey, $"correlation-{createKey}"),
                AcceptanceHash(createKey),
                AcceptanceAccount,
                DateOnly.FromDateTime(AcceptanceTime.UtcDateTime),
                () => order,
                new Dictionary<Guid, string> { [lineId] = "product-v1" })).Order;
            string approveKey = $"expected-supply-approve-{orderId:N}";
            PurchasingOrderStoreResult? approved = await scope.Store.MutateOrderAsync(
                Envelope(approveKey, $"correlation-{approveKey}"),
                AcceptanceHash(approveKey),
                PurchasingCommands.OrderApproveV1,
                orderId,
                (candidate, now) => candidate.Approve(candidate.Version, now),
                Approved);
            Assert.NotNull(approved);
            Assert.Equal(PurchaseOrderStatus.Approved, approved!.Order.Order.Status);
            approvedVersion = approved.Order.Order.Version;
            Assert.Equal(stored.Order.Version + 1, approvedVersion);
        }

        ExpectedSupplyState beforeInitialRead = await ExpectedSupplyStateAsync(run, orderId);
        await using (StoreScope scope = run.Store(CompanyA))
        {
            PurchaseSupplyReferenceAnswerV1 answer = await new PurchaseSupplyReferencePort(scope.Store)
                .GetApprovedLineAsync(orderId, lineId);
            Assert.Equal(PurchaseSupplyReferenceOutcomeV1.Approved, answer.Outcome);
            Assert.Null(answer.Status);
            PurchaseSupplyLineReferenceV1 line = Assert.IsType<PurchaseSupplyLineReferenceV1>(answer.Line);
            Assert.Equal(
                (CompanyA, orderId, lineId, 1, AcceptanceSupplier, AcceptanceAccount, AcceptanceProduct, "product-v1", AcceptanceUnit,
                    "General", "100", "0", "100", approvedVersion),
                (line.CompanyId, line.OrderId, line.LineId, line.LineNo, line.SupplierPartyId, line.CompanyAccountId, line.ProductId,
                    line.ProductVersion, line.UnitId, line.Kind, line.CommittedText, line.AcceptedText, line.RemainingText, line.Version));
            Assert.Null(line.SourceRequestId);
            Assert.Null(line.SourceRequestLineId);
        }
        Assert.Equal(beforeInitialRead, await ExpectedSupplyStateAsync(run, orderId));

        var seed = new AcceptanceSeed(orderId, lineId, AcceptanceUnit, approvedVersion);
        PurchaseAcceptanceStageResultV1 accepted = await StageAndCommitAsync(
            run,
            CompanyA,
            AcceptanceRequest(seed, Guid.NewGuid(), "40"),
            Envelope($"expected-supply-accept-{orderId:N}", $"correlation-expected-supply-accept-{orderId:N}"));
        Assert.False(accepted.IsReplay);
        Assert.Equal(("4", "60"), (accepted.PurchaseOrderVersion, accepted.RemainingQuantity));

        ExpectedSupplyState beforeFreshReads = await ExpectedSupplyStateAsync(run, orderId);
        Assert.Equal((4L, 40m, 1, 1),
            (beforeFreshReads.OrderVersion, beforeFreshReads.Accepted, beforeFreshReads.AcceptanceShares, beforeFreshReads.AcceptanceAudits));

        await using (StoreScope scope = run.Store(CompanyA))
        {
            PurchaseSupplyReferencePort port = new PurchaseSupplyReferencePort(scope.Store);
            PurchaseSupplyReferenceAnswerV1 answer = await port.GetApprovedLineAsync(orderId, lineId);
            PurchaseSupplyLineReferenceV1 line = Assert.IsType<PurchaseSupplyLineReferenceV1>(answer.Line);
            Assert.Equal(PurchaseSupplyReferenceOutcomeV1.Approved, answer.Outcome);
            Assert.Equal(("100", "40", "60", 4L),
                (line.CommittedText, line.AcceptedText, line.RemainingText, line.Version));
            Assert.Equal(PurchaseSupplyReferenceOutcomeV1.NotFound,
                (await port.GetApprovedLineAsync(orderId, Guid.NewGuid())).Outcome);
        }

        await using (StoreScope foreignScope = run.Store(CompanyB))
        {
            PurchaseSupplyReferenceAnswerV1 foreign = await new PurchaseSupplyReferencePort(foreignScope.Store)
                .GetApprovedLineAsync(orderId, lineId);
            Assert.Equal(PurchaseSupplyReferenceOutcomeV1.NotFound, foreign.Outcome);
            Assert.Null(foreign.Line);
        }

        Assert.Equal(beforeFreshReads, await ExpectedSupplyStateAsync(run, orderId));

        string[] properties = [.. typeof(PurchaseSupplyLineReferenceV1).GetProperties().Select(property => property.Name)];
        Assert.Equal(
            ["CompanyId", "OrderId", "LineId", "LineNo", "SupplierPartyId", "CompanyAccountId", "SourceRequestId", "SourceRequestLineId",
                "ProductId", "ProductVersion", "UnitId", "Kind", "CommittedText", "AcceptedText", "RemainingText", "Version"],
            properties);
        string[] forbidden = ["Due", "Date", "Schedule", "Slice", "Price", "Discount", "Currency", "Tax", "Payment", "Money", "Stock", "Invoice", "Consumed"];
        Assert.DoesNotContain(properties, property => forbidden.Any(word => property.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<ExpectedSupplyState> ExpectedSupplyStateAsync(Run run, Guid orderId) => new(
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CompanyId = @company AND OrderId = @order",
            ("@company", CompanyA), ("@order", orderId)),
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @company AND EntityId = @entity",
            ("@company", CompanyA), ("@entity", orderId.ToString("D"))),
        await run.ScalarAsync<long>(
            "SELECT Version FROM purchasing.PurchaseOrders WHERE CompanyId = @company AND Id = @order",
            ("@company", CompanyA), ("@order", orderId)),
        await run.ScalarAsync<decimal>(
            "SELECT Accepted FROM purchasing.PurchaseOrderLines WHERE CompanyId = @company AND OrderId = @order AND LineId = @line",
            ("@company", CompanyA), ("@order", orderId), ("@line", await ExpectedSupplyLineIdAsync(run, orderId))),
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE CompanyId = @company AND OrderId = @order",
            ("@company", CompanyA), ("@order", orderId)),
        await ExactAcceptanceAuditCountAsync(run, orderId),
        await SchemaRowCountAsync(run, "inventory"),
        await SchemaRowCountAsync(run, "logistics"),
        await SchemaRowCountAsync(run, "finance"));

    private static Task<Guid> ExpectedSupplyLineIdAsync(Run run, Guid orderId) => run.ScalarAsync<Guid>(
        "SELECT LineId FROM purchasing.PurchaseOrderLines WHERE CompanyId = @company AND OrderId = @order",
        ("@company", CompanyA), ("@order", orderId));

    private static Task<long> SchemaRowCountAsync(Run run, string schema) => run.ScalarAsync<long>(
        """
        DECLARE @statement nvarchar(max);
        SELECT @statement = N'SELECT @total = ' + STRING_AGG(
            CONVERT(nvarchar(max), N'(SELECT COUNT_BIG(*) FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N')'),
            N' + ')
        FROM sys.tables AS t
        INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        WHERE s.name = @schema;

        IF @statement IS NULL
        BEGIN
            SELECT CAST(0 AS bigint);
            RETURN;
        END;

        DECLARE @total bigint;
        EXEC sys.sp_executesql @statement, N'@total bigint OUTPUT', @total OUTPUT;
        SELECT @total;
        """,
        ("@schema", schema));

    private sealed record ExpectedSupplyState(
        int PurchaseAttempts,
        int AuditEvents,
        long OrderVersion,
        decimal Accepted,
        int AcceptanceShares,
        int AcceptanceAudits,
        long InventoryRows,
        long LogisticsRows,
        long FinanceRows);
}
