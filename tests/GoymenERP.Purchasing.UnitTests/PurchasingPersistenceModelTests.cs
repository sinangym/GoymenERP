using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Shell;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

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
                .Select(type => context.Model.FindEntityType(type)!.GetTableName()!).ToArray());
        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
        Assert.Equal(("organization", "Companies"), (context.Model.FindEntityType(typeof(OrganizationCompanyReference))!.GetSchema(),
            context.Model.FindEntityType(typeof(OrganizationCompanyReference))!.GetTableName()));
    }

    [Fact]
    public void The_identities_are_the_attempt_key_the_line_ids_and_one_create_attempt_per_request_and_per_order()
    {
        using PurchasingDbContext context = Context();
        // Check constraints, collations and index filters are design-time configuration, not in the read-optimized runtime model.
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var attempts = designModel.FindEntityType(typeof(PurchaseAttemptRow))!;
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempts.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        var design = designModel.FindEntityType(typeof(PurchaseAttemptRow))!;
        Assert.Equal(PurchasingDbContext.IdentityCollation, design.FindProperty(nameof(PurchaseAttemptRow.IdempotencyKeyHash))!.GetCollation());
        Assert.Equal(PurchasingDbContext.IdentityCollation, design.FindProperty(nameof(PurchaseAttemptRow.CommandName))!.GetCollation());
        // The Sales #338 lesson: one CREATE attempt per stored aggregate; the transition commands' attempts share the aggregate id.
        var requestCreates = design.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PurchaseAttempts_RequestId");
        Assert.True(requestCreates.IsUnique);
        Assert.Equal("[RequestId] IS NOT NULL AND [CommandName] = N'purchasing.purchase-request.create.v1'", requestCreates.GetFilter());
        var orderCreates = design.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PurchaseAttempts_OrderId");
        Assert.True(orderCreates.IsUnique);
        Assert.Equal("[OrderId] IS NOT NULL AND [CommandName] = N'purchasing.purchase-order.create.v1'", orderCreates.GetFilter());
        Assert.Equal((PurchasingCommands.RequestCreateV1, PurchasingCommands.OrderCreateV1),
            ("purchasing.purchase-request.create.v1", "purchasing.purchase-order.create.v1"));

        var orderLines = designModel.FindEntityType(typeof(PurchaseOrderLineRow))!;
        Assert.True(orderLines.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PurchaseOrderLines_LineId").IsUnique);
        Assert.Contains(orderLines.GetIndexes(), index => index.GetDatabaseName() == "IX_PurchaseOrderLines_SourceRequestLine");
        // A1-3 (master INV-006): the cancelled remainder is inside the shares bound.
        Assert.Contains(orderLines.GetCheckConstraints(), check => check.Name == "CK_PurchaseOrderLines_Shares"
            && check.Sql.Contains("[Technical] + [Accepted] + [Cancelled] <= [Ordered]", StringComparison.Ordinal)
            && check.Sql.Contains("[Cancelled] >= 0", StringComparison.Ordinal));
        Assert.Equal((3, 3, 3, 3), (orderLines.FindProperty(nameof(PurchaseOrderLineRow.Ordered))!.GetScale(),
            orderLines.FindProperty(nameof(PurchaseOrderLineRow.Technical))!.GetScale(), orderLines.FindProperty(nameof(PurchaseOrderLineRow.Accepted))!.GetScale(),
            orderLines.FindProperty(nameof(PurchaseOrderLineRow.Cancelled))!.GetScale()));
        Assert.Equal(64, orderLines.FindProperty(nameof(PurchaseOrderLineRow.ProductVersion))!.GetMaxLength());
        var requestLines = designModel.FindEntityType(typeof(PurchaseRequestLineRow))!;
        Assert.True(requestLines.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PurchaseRequestLines_LineId").IsUnique);
        Assert.Equal(3, requestLines.FindProperty(nameof(PurchaseRequestLineRow.Quantity))!.GetScale());

        var requests = designModel.FindEntityType(typeof(PurchaseRequestRow))!;
        Assert.True(requests.FindProperty(nameof(PurchaseRequestRow.Version))!.IsConcurrencyToken);
        Assert.True(requests.FindProperty(nameof(PurchaseRequestRow.RowVersion))!.IsConcurrencyToken);
        var orders = designModel.FindEntityType(typeof(PurchaseOrderRow))!;
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
    public void The_request_and_order_screens_and_their_controls_are_published()
    {
        var declarations = new PurchasingShellCapabilityProvider().GetDeclarations();
        Assert.Equal("1.7", PurchasingShellCapabilityProvider.OwnerContractVersion);
        Assert.Equal(12, declarations.Count);
        Assert.All(declarations, declaration => Assert.True(declaration.ImplementationAvailable));
        var byId = declarations.ToDictionary(declaration => declaration.CapabilityId, StringComparer.Ordinal);
        Assert.Equal(PurchasingShellCapabilityProvider.RequestListRoutePath,
            byId[PurchasingShellCapabilityProvider.RequestListCapabilityId].RoutePath);
        Assert.Equal([PurchasingPermissionManifestV1.OrdersRead],
            byId[PurchasingShellCapabilityProvider.RequestListCapabilityId].RequiredPermissions);
        Assert.Equal(PurchasingShellCapabilityProvider.RequestCreateRoutePath,
            byId[PurchasingShellCapabilityProvider.RequestCreateCapabilityId].RoutePath);
        Assert.Equal([PurchasingPermissionManifestV1.OrdersCreate],
            byId[PurchasingShellCapabilityProvider.RequestCreateCapabilityId].RequiredPermissions);
        Assert.Equal(PurchasingShellCapabilityProvider.RequestDetailRoutePath,
            byId[PurchasingShellCapabilityProvider.RequestDetailCapabilityId].RoutePath);
        Assert.Equal([PurchasingPermissionManifestV1.OrdersRead],
            byId[PurchasingShellCapabilityProvider.RequestDetailCapabilityId].RequiredPermissions);
        Assert.Equal([PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.OrdersCreate],
            byId[PurchasingShellCapabilityProvider.RequestSubmitCapabilityId].RequiredPermissions);
        Assert.Equal(PurchasingShellCapabilityProvider.RequestDetailCapabilityId,
            byId[PurchasingShellCapabilityProvider.RequestSubmitCapabilityId].ParentCapabilityId);
        // A1-1: the request decision control is a child of the request detail, measured with its own name.
        var requestApprove = byId[PurchasingShellCapabilityProvider.RequestApproveCapabilityId];
        Assert.Equal("purchasing.requests.approve", requestApprove.CapabilityId);
        Assert.Equal(
            (PurchasingShellCapabilityProvider.RequestDetailRoutePath, PurchasingShellCapabilityProvider.RequestDetailScreenId,
                PurchasingShellCapabilityProvider.RequestDetailCapabilityId),
            (requestApprove.RoutePath, requestApprove.ScreenId, requestApprove.ParentCapabilityId));
        Assert.Equal([PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.RequestsApprove], requestApprove.RequiredPermissions);
        // A1-1b (ALS-D46): the withdrawal control is another child of the request detail, measured with its own name.
        var requestWithdraw = byId[PurchasingShellCapabilityProvider.RequestWithdrawCapabilityId];
        Assert.Equal("purchasing.requests.withdraw", requestWithdraw.CapabilityId);
        Assert.Equal(
            (PurchasingShellCapabilityProvider.RequestDetailRoutePath, PurchasingShellCapabilityProvider.RequestDetailScreenId,
                PurchasingShellCapabilityProvider.RequestDetailCapabilityId),
            (requestWithdraw.RoutePath, requestWithdraw.ScreenId, requestWithdraw.ParentCapabilityId));
        Assert.Equal([PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.RequestsWithdraw], requestWithdraw.RequiredPermissions);
        Assert.Equal(PurchasingShellCapabilityProvider.CreateRoutePath, byId[PurchasingShellCapabilityProvider.CreateCapabilityId].RoutePath);
        Assert.Equal([PurchasingPermissionManifestV1.OrdersCreate], byId[PurchasingShellCapabilityProvider.CreateCapabilityId].RequiredPermissions);
        Assert.Equal(PurchasingShellCapabilityProvider.DetailRoutePath, byId[PurchasingShellCapabilityProvider.ApproveCapabilityId].RoutePath);
        Assert.Equal([PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.OrdersApprove],
            byId[PurchasingShellCapabilityProvider.ApproveCapabilityId].RequiredPermissions);
        Assert.Equal(PurchasingShellCapabilityProvider.DetailRoutePath, byId[PurchasingShellCapabilityProvider.DetailCapabilityId].RoutePath);
        Assert.Equal([PurchasingPermissionManifestV1.OrdersRead], byId[PurchasingShellCapabilityProvider.DetailCapabilityId].RequiredPermissions);
        Assert.Equal(PurchasingShellCapabilityProvider.ListRoutePath, byId[PurchasingShellCapabilityProvider.ListCapabilityId].RoutePath);
        Assert.Equal([PurchasingPermissionManifestV1.OrdersRead], byId[PurchasingShellCapabilityProvider.ListCapabilityId].RequiredPermissions);
        Assert.Equal(PurchasingShellCapabilityProvider.DetailCapabilityId,
            declarations.Single(item => item.CapabilityId == PurchasingShellCapabilityProvider.ApproveCapabilityId).ParentCapabilityId);
        var goodsReceipt = byId[PurchasingShellCapabilityProvider.GoodsReceiptCapabilityId];
        Assert.Equal(
            (PurchasingShellCapabilityProvider.DetailRoutePath, PurchasingShellCapabilityProvider.DetailScreenId,
                PurchasingShellCapabilityProvider.DetailCapabilityId),
            (goodsReceipt.RoutePath, goodsReceipt.ScreenId, goodsReceipt.ParentCapabilityId));
        Assert.Equal([PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.GoodsReceiptsRecord],
            goodsReceipt.RequiredPermissions);
        Assert.Equal(byId[PurchasingShellCapabilityProvider.DetailCapabilityId].ContextRequirement,
            byId[PurchasingShellCapabilityProvider.ApproveCapabilityId].ContextRequirement);
        Assert.All(declarations, declaration =>
            Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement));
        Assert.Equal(8, PurchasingPermissionManifestV1.Names.Count);
        // A2a-1 (PRS-02): the price read governs disclosure only; no shell control or route is measured with it.
        Assert.Contains("purchasing.orders.price.read", PurchasingPermissionManifestV1.Names);
        Assert.DoesNotContain(declarations, declaration => declaration.RequiredPermissions.Contains(PurchasingPermissionManifestV1.OrdersPriceRead));
        Assert.Contains("purchasing.requests.approve", PurchasingPermissionManifestV1.Names);
        Assert.Contains("purchasing.requests.withdraw", PurchasingPermissionManifestV1.Names);
        Assert.Contains("purchasing.orders.cancel", PurchasingPermissionManifestV1.Names);
        // A1-3 (plan API-10/UI-05): the remainder cancellation control is a child of the order detail, measured with its own name.
        var cancel = byId[PurchasingShellCapabilityProvider.CancelCapabilityId];
        Assert.Equal("purchasing.orders.cancel", cancel.CapabilityId);
        Assert.Equal(
            (PurchasingShellCapabilityProvider.DetailRoutePath, PurchasingShellCapabilityProvider.DetailScreenId,
                PurchasingShellCapabilityProvider.DetailCapabilityId),
            (cancel.RoutePath, cancel.ScreenId, cancel.ParentCapabilityId));
        Assert.Equal([PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.OrdersCancel], cancel.RequiredPermissions);
        // Master v3 §27.3: P-03 is the order list, P-04 the new/edit form; the detail is a view of one P-03 row (the F-02-DETAIL precedent).
        Assert.Equal(PurchasingShellCapabilityProvider.ListScreenId, byId[PurchasingShellCapabilityProvider.ListCapabilityId].ScreenId);
        Assert.Equal(PurchasingShellCapabilityProvider.CreateScreenId, byId[PurchasingShellCapabilityProvider.CreateCapabilityId].ScreenId);
        Assert.Equal(PurchasingShellCapabilityProvider.DetailScreenId, byId[PurchasingShellCapabilityProvider.DetailCapabilityId].ScreenId);
        Assert.Equal(PurchasingShellCapabilityProvider.DetailScreenId, byId[PurchasingShellCapabilityProvider.ApproveCapabilityId].ScreenId);
        Assert.Equal(PurchasingShellCapabilityProvider.RequestListScreenId,
            byId[PurchasingShellCapabilityProvider.RequestListCapabilityId].ScreenId);
        Assert.Equal(PurchasingShellCapabilityProvider.RequestCreateScreenId,
            byId[PurchasingShellCapabilityProvider.RequestCreateCapabilityId].ScreenId);
        Assert.Equal(PurchasingShellCapabilityProvider.RequestDetailScreenId,
            byId[PurchasingShellCapabilityProvider.RequestDetailCapabilityId].ScreenId);
        Assert.Equal(PurchasingShellCapabilityProvider.RequestDetailScreenId,
            byId[PurchasingShellCapabilityProvider.RequestSubmitCapabilityId].ScreenId);
    }

    [Fact]
    public void The_A1_1_A1_1b_A1_2_A1_3_A2a_1_A2a_1c_A2a_2_A2a_5a_and_A3_API_30_migrations_are_the_last_ones_and_the_snapshot_matches_the_model()
    {
        using PurchasingDbContext context = Context();
        Assert.Equal(["20261102125710_AddPurchaseRequestDecisions", "20261102161658_AddPurchaseRequestWithdrawal",
                "20261103012142_AddPurchaseRequestLineSource", "20261103053923_AddPurchaseOrderCancellation",
                "20261107063820_AddPurchaseOrderLinePrice", "20261107131321_AddPurchaseOrderUnitPriceScale",
                "20261107151838_AddPurchaseOrderLineDeferral", "20261111182351_AddPurchaseOrderApproval",
                "20261112014446_AddPurchaseOrderLineDeliverySchedule"],
            context.Database.GetMigrations().TakeLast(9));
        Assert.False(context.Database.HasPendingModelChanges());

        var requests = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseRequestRow))!;
        Assert.Contains(requests.GetCheckConstraints(), check => check.Name == "CK_PurchaseRequests_Status" && check.Sql == "[Status] IN (1, 2, 3, 4, 5)");
        Assert.Contains(requests.GetCheckConstraints(), check => check.Name == "CK_PurchaseRequests_Decision"
            && check.Sql.Contains("[Status] = 5 AND ([SubmittedBy] IS NULL OR LEN([SubmittedBy]) > 0)", StringComparison.Ordinal));
        // #900 review P3: maker ≠ checker compares binary, the domain's ordinal rule.
        Assert.Contains(requests.GetCheckConstraints(), check => check.Name == "CK_PurchaseRequests_MakerChecker"
            && check.Sql.Contains("[ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [CreatedBy] COLLATE Latin1_General_100_BIN2", StringComparison.Ordinal)
            && check.Sql.Contains("[RejectedBy] COLLATE Latin1_General_100_BIN2 <> [SubmittedBy] COLLATE Latin1_General_100_BIN2", StringComparison.Ordinal));
        // A1-1b (ALS-D46, ALS-D47): only the preparer or the submitter withdraws (binary), with a reason; no other status
        // carries withdrawal facts.
        Assert.Contains(requests.GetCheckConstraints(), check => check.Name == "CK_PurchaseRequests_Withdrawal"
            && check.Sql.Contains("[WithdrawnBy] COLLATE Latin1_General_100_BIN2 = [CreatedBy] COLLATE Latin1_General_100_BIN2", StringComparison.Ordinal)
            && check.Sql.Contains("[WithdrawnBy] COLLATE Latin1_General_100_BIN2 = [SubmittedBy] COLLATE Latin1_General_100_BIN2", StringComparison.Ordinal)
            && check.Sql.Contains("[Status] <> 5 AND [WithdrawnBy] IS NULL", StringComparison.Ordinal));
        Assert.Equal((128, 128, 128, 4000), (requests.FindProperty(nameof(PurchaseRequestRow.SubmittedBy))!.GetMaxLength(),
            requests.FindProperty(nameof(PurchaseRequestRow.ApprovedBy))!.GetMaxLength(), requests.FindProperty(nameof(PurchaseRequestRow.RejectedBy))!.GetMaxLength(),
            requests.FindProperty(nameof(PurchaseRequestRow.RejectionReason))!.GetMaxLength()));
        Assert.Equal((128, 4000), (requests.FindProperty(nameof(PurchaseRequestRow.WithdrawnBy))!.GetMaxLength(),
            requests.FindProperty(nameof(PurchaseRequestRow.WithdrawalReason))!.GetMaxLength()));

        // A1-1b Down refuses while a withdrawn request exists (forward fix) before any drop.
        var withdrawal = new GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations.AddPurchaseRequestWithdrawal();
        var withdrawalGuard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(withdrawal.DownOperations[0]);
        Assert.Contains("THROW 51165", withdrawalGuard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] = 5", withdrawalGuard.Sql, StringComparison.Ordinal);
        // ... and refuses clearly (51166) when the restored case-insensitive maker-checker check would fail, before any drop.
        var collationGuard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(withdrawal.DownOperations[1]);
        Assert.Contains("THROW 51166", collationGuard.Sql, StringComparison.Ordinal);
        Assert.Contains("[ApprovedBy] = [CreatedBy]", collationGuard.Sql, StringComparison.Ordinal);
        Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropCheckConstraintOperation>(withdrawal.DownOperations[2]);
        Assert.Empty(withdrawal.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());

        // Down refuses while a decided request exists (forward fix) before any drop; Up backfills the submitter from its own attempt.
        var migration = new GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations.AddPurchaseRequestDecisions();
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51164", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] IN (3, 4)", guard.Sql, StringComparison.Ordinal);
        var backfill = Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Contains("purchasing.purchase-request.submit.v1", backfill.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_A2a_1_price_columns_have_their_checks_precisions_and_a_Down_that_refuses_while_a_price_exists()
    {
        using PurchasingDbContext context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var orders = model.FindEntityType(typeof(PurchaseOrderRow))!;
        var money = Assert.Single(orders.GetCheckConstraints(), check => check.Name == "CK_PurchaseOrders_Money");
        Assert.Equal(PurchasingDbContext.OrderMoneyCheck, money.Sql);
        Assert.Contains("[CurrencyId] IS NULL AND [CurrencyCode] IS NULL AND [MoneyProfileId] IS NULL AND [MoneyProfileVersion] IS NULL AND [MoneyScale] IS NULL",
            money.Sql, StringComparison.Ordinal);
        Assert.Contains("[MoneyProfileVersion] >= 1 AND [MoneyScale] IS NOT NULL AND [MoneyScale] >= 0 AND [MoneyScale] <= 28", money.Sql,
            StringComparison.Ordinal);
        Assert.Equal(16, orders.FindProperty(nameof(PurchaseOrderRow.CurrencyCode))!.GetMaxLength());
        foreach (var column in new[] { nameof(PurchaseOrderRow.CurrencyId), nameof(PurchaseOrderRow.CurrencyCode), nameof(PurchaseOrderRow.MoneyProfileId),
                     nameof(PurchaseOrderRow.MoneyProfileVersion), nameof(PurchaseOrderRow.MoneyScale) })
        {
            Assert.True(orders.FindProperty(column)!.IsNullable);
        }

        var lines = model.FindEntityType(typeof(PurchaseOrderLineRow))!;
        var price = Assert.Single(lines.GetCheckConstraints(), check => check.Name == "CK_PurchaseOrderLines_Price");
        Assert.Equal(PurchasingDbContext.OrderLinePriceCheck, price.Sql);
        Assert.Contains("[UnitPrice] IS NULL AND [PriceUnitId] IS NULL AND [DiscountPercent] IS NULL AND [DiscountAmount] IS NULL", price.Sql, StringComparison.Ordinal);
        Assert.Contains("[Kind] = 1 AND [UnitPrice] IS NOT NULL AND [UnitPrice] >= 0", price.Sql, StringComparison.Ordinal);
        Assert.Contains("([DiscountPercent] IS NULL OR [DiscountAmount] IS NULL)", price.Sql, StringComparison.Ordinal);
        Assert.Contains("[DiscountPercent] >= 0 AND [DiscountPercent] <= 100", price.Sql, StringComparison.Ordinal);
        Assert.Contains("[DiscountAmount] >= 0 AND [DiscountAmount] <= [UnitPrice]", price.Sql, StringComparison.Ordinal);
        Assert.Equal((24, 6), (lines.FindProperty(nameof(PurchaseOrderLineRow.UnitPrice))!.GetPrecision(),
            lines.FindProperty(nameof(PurchaseOrderLineRow.UnitPrice))!.GetScale()));
        Assert.Equal((24, 6), (lines.FindProperty(nameof(PurchaseOrderLineRow.DiscountAmount))!.GetPrecision(),
            lines.FindProperty(nameof(PurchaseOrderLineRow.DiscountAmount))!.GetScale()));
        Assert.Equal((9, 6), (lines.FindProperty(nameof(PurchaseOrderLineRow.DiscountPercent))!.GetPrecision(),
            lines.FindProperty(nameof(PurchaseOrderLineRow.DiscountPercent))!.GetScale()));

        // Up only adds nullable columns and the two checks (no SQL, no backfill); Down refuses (51169) before any drop while a
        // currency or a price exists.
        var migration = new GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations.AddPurchaseOrderLinePrice();
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.All(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>(),
            column => Assert.True(column.IsNullable));
        Assert.Equal(9, migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>().Count());
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51169", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[CurrencyId] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[UnitPrice] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void The_A2a_1c_unit_price_scale_is_a_nullable_column_beside_the_money_snapshot_with_a_Down_that_refuses_while_one_exists()
    {
        using PurchasingDbContext context = Context();
        var orders = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseOrderRow))!;
        Assert.True(orders.FindProperty(nameof(PurchaseOrderRow.UnitPriceScale))!.IsNullable);
        var check = Assert.Single(orders.GetCheckConstraints(), candidate => candidate.Name == "CK_PurchaseOrders_UnitPriceScale");
        Assert.Equal(PurchasingDbContext.OrderUnitPriceScaleCheck, check.Sql);
        Assert.Equal("[UnitPriceScale] IS NULL OR ([CurrencyId] IS NOT NULL AND [UnitPriceScale] >= 0 AND [UnitPriceScale] <= 28)", check.Sql);
        // The A2a-1 money check is unchanged: a legacy priced order has no unit-price scale.
        Assert.DoesNotContain("UnitPriceScale", PurchasingDbContext.OrderMoneyCheck, StringComparison.Ordinal);

        // Up adds one nullable column and its check (no SQL, no backfill); Down refuses (51170) before any drop while a scale exists.
        var migration = new GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations.AddPurchaseOrderUnitPriceScale();
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        var column = Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>());
        Assert.Equal((nameof(PurchaseOrderRow.UnitPriceScale), true), (column.Name, column.IsNullable));
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51170", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[UnitPriceScale] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void The_A2a_5a_approval_is_two_nullable_facts_never_the_preparer_with_a_Down_that_refuses_while_an_approver_exists()
    {
        using PurchasingDbContext context = Context();
        var orders = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseOrderRow))!;
        Assert.Equal((128, true), (orders.FindProperty(nameof(PurchaseOrderRow.ApprovedBy))!.GetMaxLength(),
            orders.FindProperty(nameof(PurchaseOrderRow.ApprovedBy))!.IsNullable));
        Assert.True(orders.FindProperty(nameof(PurchaseOrderRow.ApprovedAtUtc))!.IsNullable);
        var check = Assert.Single(orders.GetCheckConstraints(), candidate => candidate.Name == "CK_PurchaseOrders_Approval");
        Assert.Equal(PurchasingDbContext.OrderApprovalCheck, check.Sql);
        Assert.Contains("([ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL)", check.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] IN (2, 3)", check.Sql, StringComparison.Ordinal);
        // ALS-D3: maker ≠ checker compares binary, the domain's ordinal rule (the #900 P3 lesson).
        Assert.Contains("[ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [CreatedBy] COLLATE Latin1_General_100_BIN2", check.Sql,
            StringComparison.Ordinal);

        // Up adds two nullable columns and the check (no SQL, no backfill); Down refuses (51172) before any drop while an approver exists.
        var migration = new GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations.AddPurchaseOrderApproval();
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        var columns = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>().ToArray();
        Assert.Equal([nameof(PurchaseOrderRow.ApprovedAtUtc), nameof(PurchaseOrderRow.ApprovedBy)], columns.Select(column => column.Name).Order(StringComparer.Ordinal));
        Assert.All(columns, column => Assert.True(column.IsNullable));
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51172", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[ApprovedBy] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void The_A3_API_30_delivery_slices_are_an_append_only_tenant_table_bound_to_the_line_with_a_Down_that_refuses_while_a_slice_exists()
    {
        using PurchasingDbContext context = Context();
        var slices = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseOrderLineDeliverySliceRow))!;
        Assert.Equal(("PurchaseOrderLineDeliverySlices", PurchasingDbContext.Schema), (slices.GetTableName(), slices.GetSchema()));
        Assert.Equal([nameof(PurchaseOrderLineDeliverySliceRow.CompanyId), nameof(PurchaseOrderLineDeliverySliceRow.OrderId),
                nameof(PurchaseOrderLineDeliverySliceRow.OrderVersionAfter), nameof(PurchaseOrderLineDeliverySliceRow.SliceNo)],
            slices.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal((18, 3), (slices.FindProperty(nameof(PurchaseOrderLineDeliverySliceRow.Quantity))!.GetPrecision(),
            slices.FindProperty(nameof(PurchaseOrderLineDeliverySliceRow.Quantity))!.GetScale()));
        Assert.Equal("date", slices.FindProperty(nameof(PurchaseOrderLineDeliverySliceRow.DeliveryDate))!.GetColumnType());
        var checks = slices.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal("[Quantity] > 0", checks["CK_PurchaseOrderLineDeliverySlices_Quantity"]);
        Assert.Equal("[SliceNo] >= 1 AND [SliceNo] <= 500", checks["CK_PurchaseOrderLineDeliverySlices_SliceNo"]);
        Assert.Equal("[OrderVersionAfter] >= 2", checks["CK_PurchaseOrderLineDeliverySlices_Version"]);
        Assert.Contains("[SliceId] <> '00000000-0000-0000-0000-000000000000'", checks["CK_PurchaseOrderLineDeliverySlices_Identity"], StringComparison.Ordinal);
        Assert.Contains("LEN([RecordedBy]) > 0", checks["CK_PurchaseOrderLineDeliverySlices_Text"], StringComparison.Ordinal);
        // Bound to the order and to the line of the same company; the slice id is unique within one revision.
        Assert.Contains(slices.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PurchaseOrderLineRow)
            && key.Properties.Select(property => property.Name).SequenceEqual([nameof(PurchaseOrderLineDeliverySliceRow.CompanyId),
                nameof(PurchaseOrderLineDeliverySliceRow.OrderId), nameof(PurchaseOrderLineDeliverySliceRow.LineId)]));
        Assert.Contains(slices.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PurchaseOrderRow));
        Assert.All(slices.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        Assert.Contains(slices.GetIndexes(), index => index.IsUnique && index.GetDatabaseName() == "UX_PurchaseOrderLineDeliverySlices_SliceId");

        // Up creates the table (no SQL, nothing backfilled); Down refuses (51173) before the drop while a slice exists.
        var migration = new GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations.AddPurchaseOrderLineDeliverySchedule();
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>());
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51173", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[purchasing].[PurchaseOrderLineDeliverySlices]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>(migration.DownOperations[^1]);
    }

    [Fact]
    public void The_A2a_2_surcharge_columns_have_their_check_precisions_and_a_Down_that_refuses_while_a_surcharge_exists()
    {
        using PurchasingDbContext context = Context();
        var lines = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseOrderLineRow))!;
        Assert.Equal((9, 6, true), (lines.FindProperty(nameof(PurchaseOrderLineRow.DeferralPercent))!.GetPrecision(),
            lines.FindProperty(nameof(PurchaseOrderLineRow.DeferralPercent))!.GetScale(), lines.FindProperty(nameof(PurchaseOrderLineRow.DeferralPercent))!.IsNullable));
        Assert.Equal((24, 6, true), (lines.FindProperty(nameof(PurchaseOrderLineRow.DeferralAmount))!.GetPrecision(),
            lines.FindProperty(nameof(PurchaseOrderLineRow.DeferralAmount))!.GetScale(), lines.FindProperty(nameof(PurchaseOrderLineRow.DeferralAmount))!.IsNullable));
        var check = Assert.Single(lines.GetCheckConstraints(), candidate => candidate.Name == "CK_PurchaseOrderLines_Deferral");
        Assert.Equal(PurchasingDbContext.OrderLineDeferralCheck, check.Sql);
        Assert.Contains("[UnitPrice] IS NOT NULL AND [UnitPrice] > 0", check.Sql, StringComparison.Ordinal);
        Assert.Contains("([DeferralPercent] IS NULL OR [DeferralAmount] IS NULL)", check.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Deferral", PurchasingDbContext.OrderLinePriceCheck, StringComparison.Ordinal);

        var migration = new GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations.AddPurchaseOrderLineDeferral();
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Equal(2, migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>().Count());
        Assert.All(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>(),
            column => Assert.True(column.IsNullable));
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51171", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[DeferralPercent] IS NOT NULL OR [DeferralAmount] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void The_A1_3_cancellation_is_a_third_status_with_its_facts_a_cancelled_share_and_a_Down_that_refuses_while_one_exists()
    {
        using PurchasingDbContext context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var orders = model.FindEntityType(typeof(PurchaseOrderRow))!;
        Assert.Contains(orders.GetCheckConstraints(), check => check.Name == "CK_PurchaseOrders_Status" && check.Sql == "[Status] IN (1, 2, 3)");
        var cancellation = Assert.Single(orders.GetCheckConstraints(), check => check.Name == "CK_PurchaseOrders_Cancellation");
        Assert.Equal(PurchasingDbContext.OrderCancellationCheck, cancellation.Sql);
        Assert.Contains("[Status] = 3 AND [CancelledBy] IS NOT NULL AND LEN([CancelledBy]) > 0", cancellation.Sql, StringComparison.Ordinal);
        Assert.Contains("[CancellationReason] IS NOT NULL AND LEN([CancellationReason]) > 0", cancellation.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] <> 3 AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL", cancellation.Sql,
            StringComparison.Ordinal);
        Assert.Equal((128, 4000), (orders.FindProperty(nameof(PurchaseOrderRow.CancelledBy))!.GetMaxLength(),
            orders.FindProperty(nameof(PurchaseOrderRow.CancellationReason))!.GetMaxLength()));
        var lines = model.FindEntityType(typeof(PurchaseOrderLineRow))!;
        Assert.False(lines.FindProperty(nameof(PurchaseOrderLineRow.Cancelled))!.IsNullable);
        Assert.Equal(PurchasingDbContext.OrderLineSharesCheck,
            Assert.Single(lines.GetCheckConstraints(), check => check.Name == "CK_PurchaseOrderLines_Shares").Sql);

        // Up adds the columns (the line share NOT NULL with 0 for every existing line) and the checks, no SQL; Down refuses (51168)
        // before any drop while a cancelled order or a cancelled share exists.
        var migration = new GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations.AddPurchaseOrderCancellation();
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        var share = Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>(),
            column => column.Name == "Cancelled");
        Assert.Equal((false, 0m), (share.IsNullable, (decimal)share.DefaultValue!));
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51168", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] = 3", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Cancelled] <> 0", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        // The restored checks are InitialPurchasing's exactly.
        Assert.Contains(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>(),
            check => check.Name == "CK_PurchaseOrders_Status" && check.Sql == "[Status] IN (1, 2)");
        Assert.Contains(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>(),
            check => check.Name == "CK_PurchaseOrderLines_Shares"
                && check.Sql == "[Ordered] > 0 AND [Technical] >= 0 AND [Accepted] >= 0 AND [Technical] + [Accepted] <= [Ordered]");
    }

    [Fact]
    public async Task The_write_guard_lets_the_cancellation_facts_and_the_cancelled_share_move_but_not_another_field()
    {
        await using (PurchasingDbContext context = Context())
        {
            var order = new PurchaseOrderRow
            {
                Id = Guid.NewGuid(), CompanyId = CompanyA, SupplierPartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), Status = 2, Version = 3,
                EffectiveOn = new DateOnly(2026, 11, 3), CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
                CreatedBy = "SENTETIK", CorrelationId = "SENTETIK",
            };
            context.Orders.Attach(order);
            order.Status = 3;
            order.CancelledBy = "SENTETIK-manager";
            order.CancelledAtUtc = DateTimeOffset.UtcNow;
            order.CancellationReason = "SENTETIK";
            // The cancellation facts alone are on the allow-list: the guard passes them (only the database is unreachable here).
            var onlyFacts = await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
            Assert.DoesNotContain("izinli alan", onlyFacts.Message, StringComparison.Ordinal);
            Assert.IsAssignableFrom<System.Data.Common.DbException>(onlyFacts.InnerException);
            order.SupplierPartyId = Guid.NewGuid(); // not allowed: the supplier never moves
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
        }

        await using (PurchasingDbContext context = Context())
        {
            var line = new PurchaseOrderLineRow
            {
                CompanyId = CompanyA, OrderId = Guid.NewGuid(), LineNo = 1, LineId = Guid.NewGuid(), Kind = 1, ProductId = Guid.NewGuid(),
                ProductVersion = "7", UnitId = Guid.NewGuid(), UnitIsKilogram = true, Ordered = 6m,
            };
            context.OrderLines.Attach(line);
            line.Cancelled = 6m;
            var onlyShare = await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
            Assert.DoesNotContain("izinli alan", onlyShare.Message, StringComparison.Ordinal);
            Assert.IsAssignableFrom<System.Data.Common.DbException>(onlyShare.InnerException);
            line.Ordered = 7m; // not allowed: the first commitment never moves
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_write_guard_lets_the_A2a_5a_approval_facts_move_but_not_the_preparer()
    {
        await using PurchasingDbContext context = Context();
        var order = new PurchaseOrderRow
        {
            Id = Guid.NewGuid(), CompanyId = CompanyA, SupplierPartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), Status = 1, Version = 2,
            EffectiveOn = new DateOnly(2026, 11, 11), CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = "SENTETIK-preparer", CorrelationId = "SENTETIK",
        };
        context.Orders.Attach(order);
        order.Status = 2;
        order.ApprovedBy = "SENTETIK-approver";
        order.ApprovedAtUtc = DateTimeOffset.UtcNow;
        // The approval facts are on the allow-list: the guard passes them (only the database is unreachable here).
        var onlyFacts = await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
        Assert.DoesNotContain("izinli alan", onlyFacts.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<System.Data.Common.DbException>(onlyFacts.InnerException);
        order.CreatedBy = "SENTETIK-approver"; // not allowed: the preparer never moves (it would launder maker ≠ checker)
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_write_guard_never_lets_a_recorded_price_or_currency_move()
    {
        // A2a-1: a price is written once with the line and the currency once with the order; a later price is a revision (A2b).
        await using (PurchasingDbContext context = Context())
        {
            var line = new PurchaseOrderLineRow
            {
                CompanyId = CompanyA, OrderId = Guid.NewGuid(), LineNo = 1, LineId = Guid.NewGuid(), Kind = 1, ProductId = Guid.NewGuid(),
                ProductVersion = "7", UnitId = Guid.NewGuid(), UnitIsKilogram = true, Ordered = 6m, UnitPrice = 10m,
            };
            context.OrderLines.Attach(line);
            line.UnitPrice = 9m;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
        }

        await using (PurchasingDbContext context = Context())
        {
            var order = new PurchaseOrderRow
            {
                Id = Guid.NewGuid(), CompanyId = CompanyA, SupplierPartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), Status = 1, Version = 1,
                EffectiveOn = new DateOnly(2026, 11, 7), CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
                CreatedBy = "SENTETIK", CorrelationId = "SENTETIK",
            };
            context.Orders.Attach(order);
            order.CurrencyId = Guid.NewGuid();
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_write_guard_lets_only_the_decision_facts_move_on_a_request()
    {
        await using PurchasingDbContext context = Context();
        var row = new PurchaseRequestRow
        {
            Id = Guid.NewGuid(), CompanyId = CompanyA, Status = 2, Version = 3, EffectiveOn = new DateOnly(2026, 11, 2),
            CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = "SENTETIK", CorrelationId = "SENTETIK",
        };
        context.Requests.Attach(row);
        row.Status = 3;
        row.ApprovedBy = "SENTETIK-manager";
        row.ApprovedAtUtc = DateTimeOffset.UtcNow;
        row.CreatedBy = "SENTETIK-other"; // not allowed: the preparer never moves
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_A1_2_line_source_is_three_nullable_columns_kept_together_by_a_check_and_its_Down_refuses_while_a_source_exists()
    {
        using PurchasingDbContext context = Context();
        var lines = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseRequestLineRow))!;
        Assert.True(lines.FindProperty(nameof(PurchaseRequestLineRow.SourceKind))!.IsNullable);
        Assert.True(lines.FindProperty(nameof(PurchaseRequestLineRow.SourceDocumentId))!.IsNullable);
        Assert.True(lines.FindProperty(nameof(PurchaseRequestLineRow.SourceDocumentLineId))!.IsNullable);
        var check = Assert.Single(lines.GetCheckConstraints(), candidate => candidate.Name == "CK_PurchaseRequestLines_Source");
        Assert.Equal(PurchasingDbContext.RequestLineSourceCheck, check.Sql);
        Assert.Contains("[SourceKind] IS NULL AND [SourceDocumentId] IS NULL AND [SourceDocumentLineId] IS NULL", check.Sql, StringComparison.Ordinal);
        Assert.Contains("[SourceKind] IN (1, 2, 3)", check.Sql, StringComparison.Ordinal);
        Assert.Contains("[SourceDocumentLineId] <> '00000000-0000-0000-0000-000000000000'", check.Sql, StringComparison.Ordinal);
        var index = Assert.Single(lines.GetIndexes(), candidate => candidate.GetDatabaseName() == "IX_PurchaseRequestLines_Source");
        Assert.Equal([nameof(PurchaseRequestLineRow.CompanyId), nameof(PurchaseRequestLineRow.SourceKind), nameof(PurchaseRequestLineRow.SourceDocumentId)],
            index.Properties.Select(property => property.Name));
        Assert.Equal("[SourceKind] IS NOT NULL", index.GetFilter());

        // Up only adds nullable columns, the index and the check; Down refuses (51167) before any drop while a line has a source.
        var migration = new GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations.AddPurchaseRequestLineSource();
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.All(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>(),
            column => Assert.True(column.IsNullable));
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51167", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[SourceKind] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public async Task The_write_guard_never_lets_a_stored_line_source_move()
    {
        await using PurchasingDbContext context = Context();
        var row = new PurchaseRequestLineRow
        {
            CompanyId = CompanyA, RequestId = Guid.NewGuid(), LineNo = 1, LineId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductVersion = "7",
            UnitId = Guid.NewGuid(), UnitIsKilogram = true, Quantity = 6m,
        };
        context.RequestLines.Attach(row);
        row.SourceKind = 2;
        row.SourceDocumentId = Guid.NewGuid();
        row.SourceDocumentLineId = Guid.NewGuid();
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_write_guard_lets_the_withdrawal_facts_move_but_not_another_field()
    {
        await using PurchasingDbContext context = Context();
        var row = new PurchaseRequestRow
        {
            Id = Guid.NewGuid(), CompanyId = CompanyA, Status = 2, Version = 3, EffectiveOn = new DateOnly(2026, 11, 2),
            CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = "SENTETIK", CorrelationId = "SENTETIK",
            SubmittedBy = "SENTETIK",
        };
        context.Requests.Attach(row);
        row.Status = 5;
        row.WithdrawnBy = "SENTETIK";
        row.WithdrawnAtUtc = DateTimeOffset.UtcNow;
        row.WithdrawalReason = "SENTETIK";
        // The withdrawal facts alone are on the allow-list: the guard passes them (only the database is unreachable here).
        var onlyFacts = await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
        Assert.DoesNotContain("izinli alan", onlyFacts.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<System.Data.Common.DbException>(onlyFacts.InnerException);

        row.EffectiveOn = new DateOnly(2026, 11, 3); // not allowed: the business date never moves
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
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
