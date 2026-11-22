using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesPersistenceModelTests
{
    /// <summary>
    /// WP13 (YOL-D11): the signed approval records and their attempts — company-filtered, company-bound to the order row, at most
    /// one active record per order (filtered unique index), the version a concurrency token, the state / document / command / outcome
    /// checks declared as the DbContext constants, both guard triggers declared; Up adds the facts-and-one-step guard (52261) and the
    /// append-only attempts guard (52262); Down is a forward fix (52260) first, then the two drops.
    /// </summary>
    [Fact]
    public void The_signed_approval_migration_adds_two_triggers_one_active_record_per_order_and_its_down_refuses_with_52260_first()
    {
        using var context = Context();
        var approval = context.Model.FindEntityType(typeof(OrderSignedApprovalRow))!;
        Assert.Equal(("sales", "OrderSignedApprovals"), (approval.GetSchema(), approval.GetTableName()));
        Assert.Single(approval.GetDeclaredQueryFilters());
        Assert.Equal(["Id"], approval.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(approval.FindProperty(nameof(OrderSignedApprovalRow.Version))!.IsConcurrencyToken);
        var active = approval.GetIndexes().Single(index => index.GetDatabaseName() == "UX_OrderSignedApprovals_Active");
        Assert.True(active.IsUnique);
        Assert.Equal(SalesDbContext.OrderSignedApprovalActiveFilter, active.GetFilter());
        Assert.Equal(["CompanyId", "OrderId"], active.Properties.Select(property => property.Name));
        Assert.Contains(approval.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(DraftRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "OrderId"]));
        var attempt = context.Model.FindEntityType(typeof(OrderSignedApprovalAttemptRow))!;
        Assert.Equal(("sales", "OrderSignedApprovalAttempts"), (attempt.GetSchema(), attempt.GetTableName()));
        Assert.Single(attempt.GetDeclaredQueryFilters());
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempt.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Null(attempt.FindProperty("IdempotencyKey"));

        var design = context.GetService<IDesignTimeModel>().Model;
        var checks = design.FindEntityType(typeof(OrderSignedApprovalRow))!.GetCheckConstraints().ToArray();
        Assert.Contains(checks, check => check.Name == "CK_OrderSignedApprovals_State" && check.Sql == SalesDbContext.OrderSignedApprovalStateConstraint);
        Assert.Contains(checks, check => check.Name == "CK_OrderSignedApprovals_Document" && check.Sql == SalesDbContext.OrderSignedApprovalDocumentConstraint);
        var attemptChecks = design.FindEntityType(typeof(OrderSignedApprovalAttemptRow))!.GetCheckConstraints().ToArray();
        Assert.Contains(attemptChecks, check => check.Name == "CK_OrderSignedApprovalAttempts_Command"
            && check.Sql == SalesDbContext.OrderSignedApprovalAttemptCommandConstraint);
        Assert.Contains(attemptChecks, check => check.Name == "CK_OrderSignedApprovalAttempts_Outcome"
            && check.Sql == SalesDbContext.OrderSignedApprovalAttemptOutcomeConstraint);
        Assert.Equal([SalesDbContext.OrderSignedApprovalGuardTrigger],
            design.FindEntityType(typeof(OrderSignedApprovalRow))!.GetDeclaredTriggers().Select(trigger => trigger.GetDatabaseName()));
        Assert.Equal([SalesDbContext.OrderSignedApprovalAttemptGuardTrigger],
            design.FindEntityType(typeof(OrderSignedApprovalAttemptRow))!.GetDeclaredTriggers().Select(trigger => trigger.GetDatabaseName()));

        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddSalesOrderSignedApprovals();
        var up = migration.UpOperations.OfType<SqlOperation>().ToArray();
        Assert.Equal(2, up.Length);
        Assert.Contains(up, op => op.Sql.Contains("TR_OrderSignedApprovals_Guard", StringComparison.Ordinal)
            && op.Sql.Contains("THROW 52261,", StringComparison.Ordinal));
        Assert.Contains(up, op => op.Sql.Contains("TR_OrderSignedApprovalAttempts_Guard", StringComparison.Ordinal)
            && op.Sql.Contains("THROW 52262,", StringComparison.Ordinal));
        var down = migration.DownOperations;
        var guard = Assert.IsType<SqlOperation>(down[0]);
        Assert.Contains("THROW 52260,", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[sales].[OrderSignedApprovals]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[sales].[OrderSignedApprovalAttempts]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(down.OfType<SqlOperation>());
        Assert.Equal(2, down.OfType<DropTableOperation>().Count());
    }

    /// <summary>WP13: the application write guard admits only Active → Passive with its deactivation facts; nothing else of a record moves.</summary>
    [Fact]
    public void The_signed_approval_write_guard_refuses_a_change_of_the_pinned_document_and_a_delete_and_admits_the_deactivation()
    {
        using var context = Context();
        var row = new OrderSignedApprovalRow
        {
            Id = Guid.NewGuid(), CompanyId = CompanyA, OrderId = Guid.NewGuid(), OrderRevision = 1, OrderVersion = 3, DocumentId = Guid.NewGuid(),
            DocumentVersionNo = 1, DocumentSha256 = new string('a', 64), State = 1, Version = 1, RecordedBy = "synthetic-seller",
            RecordedAtUtc = At, CorrelationId = "SENTETIK",
        };
        context.Attach(row);
        row.DocumentVersionNo = 2;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        row.DocumentVersionNo = 1;
        context.Attach(row);
        context.Remove(row);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        // The one admitted change: Active → Passive with the deactivation facts passes the guard (and reaches the provider).
        context.ChangeTracker.Clear();
        context.Attach(row);
        row.State = 2;
        row.Version = 2;
        row.DeactivationKind = 1;
        row.DeactivatedBy = "synthetic-seller";
        row.DeactivatedAtUtc = At;
        row.DeactivationReason = "SENTETIK";
        Assert.Throws<GuardPassedException>(() => context.SaveChanges());
    }
}
