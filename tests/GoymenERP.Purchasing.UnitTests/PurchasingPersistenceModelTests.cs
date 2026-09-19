using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Shell;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// PUR-4a persistent model without a database: schema, tenant filters, the ADR-011 identities (one create
/// attempt per stored request and per stored order), the share check, the write guard (refuses before any
/// connection) and the staged shell declarations. What SQL Server enforces is proven by the relational
/// acceptance tests. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchasingPersistenceModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void Every_purchasing_table_is_in_its_schema_and_filtered_by_the_active_company()
    {
        using PurchasingDbContext context = Context();
        foreach (var type in new[] { typeof(PurchaseRequestRow), typeof(PurchaseRequestLineRow), typeof(PurchaseOrderRow), typeof(PurchaseOrderLineRow), typeof(PurchaseAttemptRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(PurchasingDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Contains(entity.GetForeignKeys(), key => key.Properties.Any(property => property.Name == "CompanyId"));
        }

        Assert.Equal(["PurchaseAttempts", "PurchaseOrderLines", "PurchaseOrders", "PurchaseRequestLines", "PurchaseRequests"],
            new[] { typeof(PurchaseAttemptRow), typeof(PurchaseOrderLineRow), typeof(PurchaseOrderRow), typeof(PurchaseRequestLineRow), typeof(PurchaseRequestRow) }
                .Select(type => context.Model.FindEntityType(type)!.GetTableName()).ToArray());
        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
        Assert.Equal(("organization", "Companies"), (context.Model.FindEntityType(typeof(OrganizationCompanyReference))!.GetSchema(),
            context.Model.FindEntityType(typeof(OrganizationCompanyReference))!.GetTableName()));
    }

    [Fact]
    public void The_identities_are_the_attempt_key_the_line_ids_and_one_create_attempt_per_request_and_per_order()
    {
        using PurchasingDbContext context = Context();
        var attempts = context.Model.FindEntityType(typeof(PurchaseAttemptRow))!;
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempts.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.Equal(PurchasingDbContext.IdentityCollation, attempts.FindProperty(nameof(PurchaseAttemptRow.IdempotencyKeyHash))!.GetCollation());
        // The Sales #338 lesson: one CREATE attempt per stored aggregate; the transition commands' attempts share the aggregate id.
        var requestCreates = attempts.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PurchaseAttempts_RequestId");
        Assert.True(requestCreates.IsUnique);
        Assert.Equal("[RequestId] IS NOT NULL AND [CommandName] = N'purchasing.purchase-request.create.v1'", requestCreates.GetFilter());
        var orderCreates = attempts.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PurchaseAttempts_OrderId");
        Assert.True(orderCreates.IsUnique);
        Assert.Equal("[OrderId] IS NOT NULL AND [CommandName] = N'purchasing.purchase-order.create.v1'", orderCreates.GetFilter());
        Assert.Equal((PurchasingCommands.RequestCreateV1, PurchasingCommands.OrderCreateV1),
            ("purchasing.purchase-request.create.v1", "purchasing.purchase-order.create.v1"));

        var orderLines = context.Model.FindEntityType(typeof(PurchaseOrderLineRow))!;
        Assert.True(orderLines.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PurchaseOrderLines_LineId").IsUnique);
        Assert.Contains(orderLines.GetIndexes(), index => index.GetDatabaseName() == "IX_PurchaseOrderLines_SourceRequestLine");
        Assert.Contains(orderLines.GetCheckConstraints(), check => check.Name == "CK_PurchaseOrderLines_Shares"
            && check.Sql.Contains("[Technical] + [Accepted] <= [Ordered]", StringComparison.Ordinal));
        Assert.Equal((3, 3, 3), (orderLines.FindProperty(nameof(PurchaseOrderLineRow.Ordered))!.GetScale(),
            orderLines.FindProperty(nameof(PurchaseOrderLineRow.Technical))!.GetScale(), orderLines.FindProperty(nameof(PurchaseOrderLineRow.Accepted))!.GetScale()));
        Assert.Equal(64, orderLines.FindProperty(nameof(PurchaseOrderLineRow.ProductVersion))!.GetMaxLength());
        var requestLines = context.Model.FindEntityType(typeof(PurchaseRequestLineRow))!;
        Assert.True(requestLines.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PurchaseRequestLines_LineId").IsUnique);
        Assert.Equal(3, requestLines.FindProperty(nameof(PurchaseRequestLineRow.Quantity))!.GetScale());

        var requests = context.Model.FindEntityType(typeof(PurchaseRequestRow))!;
        Assert.True(requests.FindProperty(nameof(PurchaseRequestRow.Version))!.IsConcurrencyToken);
        Assert.True(requests.FindProperty(nameof(PurchaseRequestRow.RowVersion))!.IsConcurrencyToken);
        var orders = context.Model.FindEntityType(typeof(PurchaseOrderRow))!;
        Assert.True(orders.FindProperty(nameof(PurchaseOrderRow.Version))!.IsConcurrencyToken);
        // A sourced order references its request in the same company; the reference is optional.
        Assert.Contains(orders.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PurchaseRequestRow)
            && !key.IsRequired && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "SourceRequestId"]));
    }

    [Fact]
    public async Task The_write_guard_refuses_another_company_a_delete_and_a_moved_line_identity_before_any_connection()
    {
        await using (PurchasingDbContext context = Context())
        {
            context.Orders.Add(Order(Guid.NewGuid()));
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("aktif şirket", refusal.Message, StringComparison.Ordinal);
        }

        await using (PurchasingDbContext context = Context(resolved: false))
        {
            context.Orders.Add(Order(CompanyA));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (PurchasingDbContext context = Context())
        {
            PurchaseOrderRow row = Order(CompanyA);
            context.Orders.Attach(row);
            context.Entry(row).State = EntityState.Deleted;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("silinmez", refusal.Message, StringComparison.Ordinal);
        }

        await using (PurchasingDbContext context = Context())
        {
            PurchaseOrderLineRow line = new PurchaseOrderLineRow
            {
                CompanyId = CompanyA, OrderId = Guid.NewGuid(), LineNo = 1, LineId = Guid.NewGuid(), Kind = 1, ProductId = Guid.NewGuid(),
                ProductVersion = "7", UnitId = Guid.NewGuid(), UnitIsKilogram = true, Ordered = 1m,
            };
            context.OrderLines.Attach(line);
            line.Accepted = 1m; // allowed (PUR-4b's share)
            line.ProductId = Guid.NewGuid(); // not allowed: identity never moves
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
        }

        await using (PurchasingDbContext context = Context())
        {
            // A request line is immutable after the first commit: no column may move.
            PurchaseRequestLineRow line = new PurchaseRequestLineRow
            {
                CompanyId = CompanyA, RequestId = Guid.NewGuid(), LineNo = 1, LineId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductVersion = "7",
                UnitId = Guid.NewGuid(), UnitIsKilogram = true, Quantity = 1m,
            };
            context.RequestLines.Attach(line);
            line.Quantity = 2m;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public void The_shell_declarations_are_staged_and_bound_to_the_manifest_names()
    {
        var declarations = new PurchasingShellCapabilityProvider().GetDeclarations();
        Assert.Equal(3, declarations.Count);
        Assert.All(declarations, declaration => Assert.False(declaration.ImplementationAvailable));
        Assert.Equal(
            [
                ("purchasing.orders.create", "/satinalma/siparisler/yeni", PurchasingPermissionManifestV1.OrdersCreate),
                ("purchasing.orders.detail", "/satinalma/siparisler/:orderId", PurchasingPermissionManifestV1.OrdersRead),
                ("purchasing.orders.list", "/satinalma/siparisler", PurchasingPermissionManifestV1.OrdersRead),
            ],
            declarations.Select(declaration => (declaration.CapabilityId, declaration.RoutePath, declaration.RequiredPermissions.Single()))
                .OrderBy(item => item.CapabilityId, StringComparer.Ordinal).ToArray());
        Assert.Equal(3, PurchasingPermissionManifestV1.Names.Count);
        // Master v3 §27.3: P-03 is the order list, P-04 the new/edit form; the detail is a view of one P-03 row (the F-02-DETAIL precedent).
        Assert.Equal(["P-04", "P-03-DETAIL", "P-03"],
            declarations.OrderBy(declaration => declaration.CapabilityId, StringComparer.Ordinal).Select(declaration => declaration.ScreenId).ToArray());
    }

    private static PurchaseOrderRow Order(Guid company) => new PurchaseOrderRow
    {
        Id = Guid.NewGuid(), CompanyId = company, SupplierPartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), SourceRequestId = null, Status = 1, Version = 1,
        EffectiveOn = new DateOnly(2026, 9, 19), CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = "SENTETIK", CorrelationId = "SENTETIK",
    };

    private static PurchasingDbContext Context(bool resolved = true)
    {
        CompanyContext company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        return new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}
