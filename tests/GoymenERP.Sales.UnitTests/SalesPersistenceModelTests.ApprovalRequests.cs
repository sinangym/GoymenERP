using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesPersistenceModelTests
{
    /// <summary>
    /// WP4 (SAT-D475 / SAT-D476): the approval request tables carry the company, the checks the domain restores by, the pending
    /// unique indexes and the company filter; Up is pure DDL and Down refuses with THROW 52130 first (forward fix).
    /// </summary>
    [Fact]
    public void The_approval_request_tables_carry_their_checks_and_pending_indexes_and_the_migration_down_refuses_with_52130_first()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var request = model.FindEntityType(typeof(ApprovalRequestRow))!;
        Assert.Equal(("sales", "ApprovalRequests"), (request.GetSchema(), request.GetTableName()));
        var checks = request.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal(SalesDbContext.ApprovalRequestFactsConstraint, checks["CK_ApprovalRequests_Facts"]);
        Assert.Equal(SalesDbContext.ApprovalRequestDecisionConstraint, checks["CK_ApprovalRequests_Decision"]);
        Assert.Equal(SalesDbContext.ApprovalRequestMakerCheckerConstraint, checks["CK_ApprovalRequests_MakerChecker"]);
        Assert.Contains("COLLATE Latin1_General_100_BIN2", checks["CK_ApprovalRequests_MakerChecker"], StringComparison.Ordinal);
        var indexes = request.GetIndexes().ToDictionary(index => index.GetDatabaseName()!);
        Assert.True(indexes["UX_ApprovalRequests_No"].IsUnique);
        Assert.Equal(SalesDbContext.PendingQuoteConversionRequestFilter, indexes["UX_ApprovalRequests_PendingQuoteRevision"].GetFilter());
        Assert.Equal(SalesDbContext.PendingOverDeliveryRequestFilter, indexes["UX_ApprovalRequests_PendingOrderLine"].GetFilter());
        Assert.True(indexes["UX_ApprovalRequests_PendingOrderLine"].IsUnique);
        Assert.NotEmpty(request.GetDeclaredQueryFilters());
        Assert.True(request.FindProperty(nameof(ApprovalRequestRow.Version))!.IsConcurrencyToken);

        var attempt = model.FindEntityType(typeof(ApprovalRequestAttemptRow))!;
        var attemptChecks = attempt.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal(SalesDbContext.ApprovalRequestAttemptCommandConstraint, attemptChecks["CK_ApprovalRequestAttempts_Command"]);
        Assert.Equal(SalesDbContext.ApprovalRequestAttemptOutcomeConstraint, attemptChecks["CK_ApprovalRequestAttempts_Outcome"]);
        Assert.NotEmpty(attempt.GetDeclaredQueryFilters());

        var migration = new AddSalesApprovalRequests();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 52130,", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[sales].[ApprovalRequests]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[sales].[ApprovalRequestAttempts]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
}
