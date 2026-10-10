using System.Net;
using System.Data.Common;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly DateTimeOffset AcceptanceHistoryTime = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Order_detail_reopens_the_same_order_with_ordered_immutable_acceptance_history_and_no_read_effect()
    {
        await using var run = await Run.StartAsync();
        await GrantOrdersReadAsync(run, TestUsers.CompanyA, CompanyA);
        await GrantOrdersReadAsync(run, TestUsers.CompanyB, CompanyB);
        var owners = Owners.For(CompanyA);
        await SeedCounterpartyOwnerAsync(run, owners);
        StoredPurchaseOrder approved;
        await using (var scope = run.Store(CompanyA))
        {
            PurchaseOrder order = Order(owners, null, null, 100m);
            var stored = await scope.Store.StoreOrderAsync(
                run.Envelope("history-order-create"), Hash("history-order-create"), owners.Account, Day,
                () => order, VersionsOf(order));
            var transitioned = await scope.Store.MutateOrderAsync(
                run.Envelope("history-order-approve"), Hash("history-order-approve"), PurchasingCommands.OrderApproveV1,
                order.Id, (candidate, now) => candidate.Approve(2, now), Approved);
            approved = Assert.IsType<StoredPurchaseOrder>(transitioned?.Order);
        }

        Guid orderId = approved.Order.Id;
        Guid lineId = Assert.Single(approved.Order.Lines).Id;
        Guid firstId = Guid.Parse("64000000-0000-0000-0000-000000000001");
        Guid secondId = Guid.Parse("64000000-0000-0000-0000-000000000002");

        await CommitAcceptanceAsync(run, new PurchaseAcceptanceStageRequestV1(
            CompanyA, firstId, orderId, lineId, "3", Product, "7", KgUnit, "40", AcceptanceHistoryTime),
            "history-accept-40");

        var beforeFirstRead = await AcceptanceHistoryStateAsync(run, orderId);
        PurchaseOrderDetailView firstDetail = await NativeDetailAsync(run, orderId);
        Assert.Equal(("4", "40", "60"),
            (firstDetail.Order.Version, Assert.Single(firstDetail.Lines).Accepted, Assert.Single(firstDetail.Lines).Remaining));
        var acceptance = Assert.Single(firstDetail.Acceptances);
        Assert.Equal((firstId, lineId, "40", AcceptanceHistoryTime),
            (acceptance.AcceptanceId, acceptance.LineId, acceptance.Quantity, acceptance.RecordedAtUtc));
        Assert.Equal(beforeFirstRead, await AcceptanceHistoryStateAsync(run, orderId));

        await CommitAcceptanceAsync(run, new PurchaseAcceptanceStageRequestV1(
            CompanyA, secondId, orderId, lineId, "4", Product, "7", KgUnit, "25", AcceptanceHistoryTime.AddMinutes(1)),
            "history-accept-25");

        var beforeReads = await AcceptanceHistoryStateAsync(run, orderId);
        PurchaseOrderDetailView detail = await NativeDetailAsync(run, orderId);
        Assert.Equal(("5", "65", "35"),
            (detail.Order.Version, Assert.Single(detail.Lines).Accepted, Assert.Single(detail.Lines).Remaining));
        Assert.Equal([firstId, secondId], detail.Acceptances.Select(item => item.AcceptanceId).ToArray());
        Assert.Equal([lineId, lineId], detail.Acceptances.Select(item => item.LineId).ToArray());
        Assert.Equal(["40", "25"], detail.Acceptances.Select(item => item.Quantity).ToArray());
        Assert.Equal([AcceptanceHistoryTime, AcceptanceHistoryTime.AddMinutes(1)],
            detail.Acceptances.Select(item => item.RecordedAtUtc).ToArray());

        _ = await NativeDetailAsync(run, orderId);
        await AssertNativeFailureAsync(run, TestUsers.CompanyA, CompanyA, $"{PurchasingApiV1.OrdersPath}/{Guid.NewGuid():D}", HttpStatusCode.NotFound, orderId, firstId);
        await AssertNativeFailureAsync(run, TestUsers.CompanyB, CompanyB, $"{PurchasingApiV1.OrdersPath}/{orderId:D}", HttpStatusCode.NotFound, orderId, firstId);
        await AssertNativeFailureAsync(run, TestUsers.NoAssignment, CompanyA, $"{PurchasingApiV1.OrdersPath}/{orderId:D}", HttpStatusCode.Forbidden, orderId, firstId);
        await AssertNativeFailureAsync(run, TestUsers.CompanyA, CompanyA, $"{PurchasingApiV1.OrdersPath}/not-a-guid", HttpStatusCode.NotFound, orderId, firstId);

        Assert.Equal(beforeReads, await AcceptanceHistoryStateAsync(run, orderId));
        Assert.Equal(
            ["AcceptanceId", "LineId", "Quantity", "RecordedAtUtc"],
            typeof(PurchaseOrderAcceptanceHistoryView).GetProperties().Select(property => property.Name).ToArray());
    }

    private static async Task CommitAcceptanceAsync(Run run, PurchaseAcceptanceStageRequestV1 request, string key)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using (var participant = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyA))
        {
            PurchaseAcceptanceStageResultV1 result = await participant.StageAsync(request, run.Envelope(key));
            Assert.False(result.IsReplay);
        }
        await transaction.CommitAsync();
    }

    private static Task SeedCounterpartyOwnerAsync(Run run, Owners owners) => run.ExecuteAsync(
        """
        INSERT INTO counterparty.Parties
            (Id, Code, Kind, LegalName, TradeName, IsActive, Version, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@party, N'CAR-909661', N'LegalEntity', N'SENTETIK Tarihsel Tedarikci', NULL, 1, 1, @now, @now);

        INSERT INTO counterparty.CompanyPartyAccounts
            (Id, CompanyId, PartyId, Roles, ExternalIntegrationCode, IsActive, Version, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@account, @company, @party, 0, NULL, 1, 1, @now, @now);
        """,
        ("@party", owners.Party),
        ("@account", owners.Account),
        ("@company", owners.Company),
        ("@now", AcceptanceHistoryTime));

    private static async Task GrantOrdersReadAsync(Run run, string developmentUser, Guid company)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var link = await dbContext.ExternalIdentities.SingleAsync(value =>
            value.Issuer == BootstrapIdentityOptions.Issuer && value.Subject == developmentUser);
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"PUR-HISTORY-{roleId:N}", "Synthetic purchasing history reader", true);
        role.AddPermission(PurchasingPermissionManifestV1.OrdersRead);
        var assignment = new RoleAssignment(
            Guid.NewGuid(), link.UserId, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, company);
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<PurchaseOrderDetailView> NativeDetailAsync(Run run, Guid orderId)
    {
        using var client = run.Factory.CreateClientFor(TestUsers.CompanyA, CompanyA);
        using var response = await client.GetAsync($"{PurchasingApiV1.OrdersPath}/{orderId:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        string payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(["order", "lines", "acceptances"],
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        return JsonSerializer.Deserialize<PurchaseOrderDetailView>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Native purchase order detail payload could not be decoded.");
    }

    private static async Task AssertNativeFailureAsync(
        Run run,
        string actor,
        Guid company,
        string path,
        HttpStatusCode expectedStatus,
        params Guid[] protectedIds)
    {
        using var client = run.Factory.CreateClientFor(actor, company);
        using var response = await client.GetAsync(path);
        Assert.Equal(expectedStatus, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("acceptances", payload, StringComparison.OrdinalIgnoreCase);
        foreach (Guid protectedId in protectedIds)
        {
            Assert.DoesNotContain(protectedId.ToString("D"), payload, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<AcceptanceHistoryState> AcceptanceHistoryStateAsync(Run run, Guid orderId) => new(
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE OrderId = @id", ("@id", orderId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", ("@id", orderId.ToString("D"))),
        await SchemaRowsAsync(run, "purchasing"),
        await SchemaRowsAsync(run, "inventory"),
        await SchemaRowsAsync(run, "finance"),
        await SchemaRowsAsync(run, "logistics"));

    private static Task<long> SchemaRowsAsync(Run run, string schema) => run.ScalarAsync<long>(
        "SELECT COALESCE(SUM(partition.rows), 0) FROM sys.tables tableRow "
        + "JOIN sys.schemas schemaRow ON schemaRow.schema_id = tableRow.schema_id "
        + "JOIN sys.partitions partition ON partition.object_id = tableRow.object_id AND partition.index_id IN (0, 1) "
        + "WHERE schemaRow.name = @schema",
        ("@schema", schema));

    private sealed record AcceptanceHistoryState(
        int Orders, int Lines, int Acceptances, int Attempts, int Audits,
        long PurchasingRows, long InventoryRows, long FinanceRows, long LogisticsRows);
}
