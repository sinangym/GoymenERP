using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SALES-P2 persistent model without a database: schema, tenant filters, the R3 identities, the
/// snapshot, the write guard (refuses before any connection), the store's argument guards, the
/// technical document number and the opaque list cursor. What SQL Server enforces is proven by the
/// relational acceptance tests, not here. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesPersistenceModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset At = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_sales_table_is_in_its_schema_and_filtered_by_the_active_company()
    {
        using var context = Context();
        foreach (var type in new[]
                 {
                     typeof(DraftNumberRow), typeof(DraftRow), typeof(DraftLineRow), typeof(DraftDeliverySliceRow), typeof(DraftAttemptRow),
                     typeof(DraftRevisionRow), typeof(DraftRevisionLineRow),
                     typeof(DraftDeliveryScheduleRow), typeof(DraftDeliveryScheduleDateRow), typeof(DraftCancellationRow),
                 })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(SalesDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.NotNull(entity.FindProperty("CompanyId"));
            Assert.Contains(entity.GetForeignKeys(), key => key.Properties.Any(property => property.Name == "CompanyId"));
        }

        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
        Assert.Single(audit.GetDeclaredQueryFilters());
        var companies = context.Model.FindEntityType(typeof(OrganizationCompanyReference))!;
        Assert.Equal(("organization", "Companies"), (companies.GetSchema(), companies.GetTableName()));
    }

    [Fact]
    public void The_r3_identities_are_the_attempt_key_the_document_number_and_the_client_line_and_slice_ids()
    {
        using var context = Context();
        var attempts = context.Model.FindEntityType(typeof(DraftAttemptRow))!;
        // ADR-011 §2: (company, command name, key hash); no raw key column exists.
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempts.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Null(attempts.FindProperty("IdempotencyKey"));
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(DraftAttemptRow))!;
        Assert.Equal(SalesDbContext.IdentityCollation, design.FindProperty(nameof(DraftAttemptRow.CommandName))!.GetCollation());
        Assert.Equal(SalesDbContext.IdentityCollation, design.FindProperty(nameof(DraftAttemptRow.IdempotencyKeyHash))!.GetCollation());
        Assert.Equal((160, 64, 128), (attempts.FindProperty(nameof(DraftAttemptRow.CommandName))!.GetMaxLength(),
            attempts.FindProperty(nameof(DraftAttemptRow.IdempotencyKeyHash))!.GetMaxLength(),
            attempts.FindProperty(nameof(DraftAttemptRow.ActorId))!.GetMaxLength()));
        Assert.Equal((64, false), (attempts.FindProperty(nameof(DraftAttemptRow.RequestHash))!.GetMaxLength(),
            attempts.FindProperty(nameof(DraftAttemptRow.RequestHash))!.IsUnicode()));
        Assert.True(UniqueIndex(attempts, "UX_DraftAttempts_DraftId"));
        // ADR-011 §7: an attempt is a stored draft or a recorded terminal refusal; the draft index covers stored drafts only.
        // SALES-A1: it covers the CREATE attempt only (a draft is born from exactly one); the state commands' attempts
        // share the draft id, one per key, and stay unique by the primary key.
        Assert.Equal("[DraftId] IS NOT NULL AND [CommandName] = N'sales.order-draft.create.v1'",
            attempts.GetIndexes().Single(index => index.GetDatabaseName() == "UX_DraftAttempts_DraftId").GetFilter());
        Assert.Equal(SalesDbContext.CreateAttemptFilter,
            attempts.GetIndexes().Single(index => index.GetDatabaseName() == "UX_DraftAttempts_DraftId").GetFilter());
        Assert.True(attempts.FindProperty(nameof(DraftAttemptRow.DraftId))!.IsNullable);
        Assert.NotNull(attempts.FindProperty(nameof(DraftAttemptRow.Outcome)));
        Assert.NotNull(attempts.FindProperty(nameof(DraftAttemptRow.RefusalJson)));
        Assert.Equal(SalesDbContext.AttemptOutcomeConstraint,
            design.GetCheckConstraints().Single(constraint => constraint.Name == "CK_DraftAttempts_Outcome").Sql);

        var drafts = context.Model.FindEntityType(typeof(DraftRow))!;
        Assert.Equal(["Id"], drafts.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(drafts.GetKeys(), key => !key.IsPrimaryKey() && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Id"]));
        Assert.True(UniqueIndex(drafts, "UX_Drafts_DocumentNo"));
        Assert.True(drafts.FindProperty(nameof(DraftRow.Version))!.IsConcurrencyToken);
        var rowVersion = drafts.FindProperty(nameof(DraftRow.RowVersion))!;
        Assert.Equal((true, ValueGenerated.OnAddOrUpdate), (rowVersion.IsConcurrencyToken, rowVersion.ValueGenerated));

        var lines = context.Model.FindEntityType(typeof(DraftLineRow))!;
        Assert.Equal(["CompanyId", "DraftId", "LineNo"], lines.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(UniqueIndex(lines, "UX_DraftLines_LineId"));
        // Ö2: the stored quantity is decimal(18,3), the text beside it is what the client sent.
        var quantity = lines.FindProperty(nameof(DraftLineRow.Quantity))!;
        Assert.Equal(18, quantity.GetPrecision());
        Assert.Equal(3, quantity.GetScale());

        var slices = context.Model.FindEntityType(typeof(DraftDeliverySliceRow))!;
        Assert.Equal(["CompanyId", "DraftId", "LineNo", "SliceNo"], slices.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(UniqueIndex(slices, "UX_DraftDeliverySlices_SliceId"));

        var numbers = context.Model.FindEntityType(typeof(DraftNumberRow))!;
        Assert.Equal(["CompanyId"], numbers.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(numbers.FindProperty(nameof(DraftNumberRow.Version))!.IsConcurrencyToken);
    }

    [Fact]
    public void The_migration_is_discoverable_and_the_snapshot_matches_the_model()
    {
        using var context = Context();
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_InitialSales", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddAttemptOutcome", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddDeliverySchedules", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddDraftCancellations", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddRevisionReasons", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddLinePricing", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddSalesPriceFloorApprovalReplayResults", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddDraftForceClosures", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddSalesRequests", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddSalesQuotes", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddSalesDiscountExceptionApprovalReplayResults", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddSalesQuoteExpiryApprovalsAndLossReasons", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddDraftHeaderRevisions", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddDraftOrderTax", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_BackfillDraftBuyerTax", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddDraftSubcontractService", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddSalesInvoices", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddSalesInvoiceApproval", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddSalesInvoiceOfficialMatch", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddSalesInvoiceFinalization", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddQuoteDueTerms", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddDraftLineIdentityForeignKeys", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddDraftSourcePlanLines", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddInvoiceMatchTaxComparison", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddShipmentRiskExceptions", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddCustomerReturns", StringComparison.Ordinal));
        Assert.Equal("20261110215452_AddCustomerReturns", context.Database.GetMigrations().Last());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    /// <summary>API-07 (SAT-D153…D162): the risk exception tables; Up adds the decision-once guard (51252) and the append-only
    /// attempts guard (51253); Down is a forward fix (51251) first.</summary>
    [Fact]
    public void The_risk_exception_migration_adds_two_triggers_and_its_down_refuses_with_51251_first()
    {
        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddShipmentRiskExceptions();
        var up = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().ToArray();
        Assert.Equal(2, up.Length);
        Assert.Contains(up, op => op.Sql.Contains("TR_ShipmentRiskExceptions_Guard", StringComparison.Ordinal) && op.Sql.Contains("51252", StringComparison.Ordinal));
        Assert.Contains(up, op => op.Sql.Contains("TR_ShipmentRiskExceptionAttempts_Guard", StringComparison.Ordinal) && op.Sql.Contains("51253", StringComparison.Ordinal));
        var down = migration.DownOperations;
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("THROW 51251,", guard.Sql, StringComparison.Ordinal);
        Assert.Single(down.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void The_risk_exception_model_is_numbered_per_order_with_one_pending_per_shipment_and_the_maker_checker_check()
    {
        using var context = Context();
        var exception = context.Model.FindEntityType(typeof(ShipmentRiskExceptionRow))!;
        Assert.Equal(["Id"], exception.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(UniqueIndex(exception, "UX_ShipmentRiskExceptions_No"));
        Assert.True(UniqueIndex(exception, "UX_ShipmentRiskExceptions_PendingShipment"));
        Assert.Equal(SalesDbContext.PendingShipmentRiskExceptionFilter,
            exception.GetIndexes().Single(index => index.GetDatabaseName() == "UX_ShipmentRiskExceptions_PendingShipment").GetFilter());
        Assert.True(exception.FindProperty(nameof(ShipmentRiskExceptionRow.RowVersion))!.IsConcurrencyToken);
        var attempt = context.Model.FindEntityType(typeof(ShipmentRiskExceptionAttemptRow))!;
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempt.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var design = context.GetService<IDesignTimeModel>().Model;
        var checks = design.FindEntityType(typeof(ShipmentRiskExceptionRow))!.GetCheckConstraints().ToArray();
        Assert.Contains(checks, check => check.Name == "CK_ShipmentRiskExceptions_MakerChecker" && check.Sql == SalesDbContext.ShipmentRiskExceptionMakerCheckerConstraint);
        Assert.Contains(checks, check => check.Name == "CK_ShipmentRiskExceptions_Decision" && check.Sql == SalesDbContext.ShipmentRiskExceptionDecisionConstraint);
        Assert.Contains(design.FindEntityType(typeof(ShipmentRiskExceptionAttemptRow))!.GetCheckConstraints(),
            check => check.Name == "CK_ShipmentRiskExceptionAttempts_Command" && check.Sql == SalesDbContext.ShipmentRiskExceptionAttemptCommandConstraint);
        // Both guard triggers are declared, so EF Core never sends OUTPUT without INTO (refused on a table with an enabled trigger).
        Assert.Equal([SalesDbContext.ShipmentRiskExceptionGuardTrigger],
            design.FindEntityType(typeof(ShipmentRiskExceptionRow))!.GetDeclaredTriggers().Select(trigger => trigger.GetDatabaseName()));
        Assert.Equal([SalesDbContext.ShipmentRiskExceptionAttemptGuardTrigger],
            design.FindEntityType(typeof(ShipmentRiskExceptionAttemptRow))!.GetDeclaredTriggers().Select(trigger => trigger.GetDatabaseName()));
    }

    [Theory]
    [InlineData("approve", true)]
    [InlineData("reject", true)]
    [InlineData("approved-to-rejected", false)]
    [InlineData("edit-reason", false)]
    [InlineData("edit-shipment", false)]
    [InlineData("edit-requester", false)]
    [InlineData("decision-without-state", false)]
    [InlineData("delete-exception", false)]
    [InlineData("edit-attempt", false)]
    [InlineData("delete-attempt", false)]
    public void A_risk_exception_moves_once_along_its_decision_and_its_request_facts_and_attempts_never_change(string change, bool passes)
    {
        using var context = Context();
        var row = Attach(context, RiskExceptionRow(change == "approved-to-rejected" ? 2 : 1));
        switch (change)
        {
            case "approve":
                (row.State, row.DecidedBy, row.DecidedAtUtc, row.ValidOn, row.Version) = (2, "synthetic-manager", At, new DateOnly(2026, 9, 18), 2);
                break;
            case "reject":
                (row.State, row.DecidedBy, row.DecidedAtUtc, row.RejectionReason, row.Version) = (3, "synthetic-manager", At, "SENTETIK", 2);
                break;
            case "approved-to-rejected":
                (row.State, row.RejectionReason, row.ValidOn, row.Version) = (3, "SENTETIK", null, 3);
                break;
            case "edit-reason":
                row.Reason = "SENTETIK başka";
                break;
            case "edit-shipment":
                row.LoadingInstructionId = Guid.NewGuid();
                break;
            case "edit-requester":
                row.RequestedBy = "synthetic-other";
                break;
            case "decision-without-state":
                row.DecidedBy = "synthetic-manager";
                break;
            case "delete-exception":
                context.Remove(row);
                break;
            case "edit-attempt":
                Attach(context, RiskAttemptRow(row.Id)).ActorId = "synthetic-other";
                break;
            case "delete-attempt":
                context.Remove(Attach(context, RiskAttemptRow(row.Id)));
                break;
        }

        if (passes)
        {
            Assert.Throws<GuardPassedException>(() => context.SaveChanges());
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        }
    }

    private static ShipmentRiskExceptionRow RiskExceptionRow(int state) => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, OrderId = Guid.NewGuid(), ExceptionNo = 1, LoadingInstructionId = Guid.NewGuid(),
        Reason = "SENTETIK ödeme sözü", State = state, RequestedBy = "synthetic-shipper", RequestedAtUtc = At,
        DecidedBy = state == 1 ? null : "synthetic-manager", DecidedAtUtc = state == 1 ? null : At,
        ValidOn = state == 2 ? new DateOnly(2026, 9, 18) : null, Version = state == 1 ? 1 : 2, RowVersion = [1, 2, 3, 4, 5, 6, 7, 8],
        UpdatedAtUtc = At, CorrelationId = "synthetic-correlation",
    };

    private static ShipmentRiskExceptionAttemptRow RiskAttemptRow(Guid exceptionId) => new()
    {
        CompanyId = CompanyA, CommandName = GoymenERP.Modules.Sales.Application.Contracts.SalesRiskExceptionCommands.RequestV1,
        IdempotencyKeyHash = new string('A', 64), RequestHash = new string('B', 64), ActorId = "synthetic-shipper", Outcome = 1,
        OrderId = Guid.NewGuid(), ExceptionId = exceptionId, CorrelationId = "synthetic-correlation", RecordedAtUtc = At,
    };

    /// <summary>API-40 (SAT-D288..SAT-D294): the customer return tables; Up adds the one-step-forward guard (51255) and the
    /// append-only attempts guard (51256); Down is a forward fix (51254) first.</summary>
    [Fact]
    public void The_customer_return_migration_adds_two_triggers_and_its_down_refuses_with_51254_first()
    {
        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddCustomerReturns();
        var up = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().ToArray();
        Assert.Equal(2, up.Length);
        Assert.Contains(up, op => op.Sql.Contains("TR_CustomerReturns_Guard", StringComparison.Ordinal) && op.Sql.Contains("THROW 51255,", StringComparison.Ordinal));
        Assert.Contains(up, op => op.Sql.Contains("TR_CustomerReturnAttempts_Guard", StringComparison.Ordinal) && op.Sql.Contains("THROW 51256,", StringComparison.Ordinal));
        var down = migration.DownOperations;
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("THROW 51254,", guard.Sql, StringComparison.Ordinal);
        Assert.Single(down.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void The_customer_return_model_keys_the_customer_document_once_per_party_and_carries_the_maker_checker_check()
    {
        using var context = Context();
        var customerReturn = context.Model.FindEntityType(typeof(CustomerReturnRow))!;
        Assert.Equal(["Id"], customerReturn.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(UniqueIndex(customerReturn, "UX_CustomerReturns_CustomerDocument"));
        Assert.True(UniqueIndex(customerReturn, "UX_CustomerReturns_FinalizationSequence"));
        Assert.Equal(SalesDbContext.CustomerReturnFinalizationSequenceFilter,
            customerReturn.GetIndexes().Single(index => index.GetDatabaseName() == "UX_CustomerReturns_FinalizationSequence").GetFilter());
        Assert.True(customerReturn.FindProperty(nameof(CustomerReturnRow.Version))!.IsConcurrencyToken);
        var attempt = context.Model.FindEntityType(typeof(CustomerReturnAttemptRow))!;
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempt.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var design = context.GetService<IDesignTimeModel>().Model;
        var checks = design.FindEntityType(typeof(CustomerReturnRow))!.GetCheckConstraints().ToArray();
        Assert.Contains(checks, check => check.Name == "CK_CustomerReturns_Facts" && check.Sql == SalesDbContext.CustomerReturnFactsConstraint);
        Assert.Contains(checks, check => check.Name == "CK_CustomerReturns_MakerChecker" && check.Sql == SalesDbContext.CustomerReturnMakerCheckerConstraint);
        Assert.Contains(checks, check => check.Name == "CK_CustomerReturns_Rate" && check.Sql == SalesDbContext.CustomerReturnRateConstraint);
        var attemptChecks = design.FindEntityType(typeof(CustomerReturnAttemptRow))!.GetCheckConstraints().ToArray();
        Assert.Contains(attemptChecks, check => check.Name == "CK_CustomerReturnAttempts_Command" && check.Sql == SalesDbContext.CustomerReturnAttemptCommandConstraint);
        Assert.Contains(attemptChecks, check => check.Name == "CK_CustomerReturnAttempts_Outcome" && check.Sql == SalesDbContext.CustomerReturnAttemptOutcomeConstraint);
        // Both guard triggers are declared, so EF Core never sends OUTPUT without INTO (refused on a table with an enabled trigger).
        Assert.Equal([SalesDbContext.CustomerReturnGuardTrigger],
            design.FindEntityType(typeof(CustomerReturnRow))!.GetDeclaredTriggers().Select(trigger => trigger.GetDatabaseName()));
        Assert.Equal([SalesDbContext.CustomerReturnAttemptGuardTrigger],
            design.FindEntityType(typeof(CustomerReturnAttemptRow))!.GetDeclaredTriggers().Select(trigger => trigger.GetDatabaseName()));
    }

    [Theory]
    [InlineData("submit", true)]
    [InlineData("approve", true)]
    [InlineData("finalize", true)]
    [InlineData("skip-a-step", false)]
    [InlineData("step-back", false)]
    [InlineData("edit-document", false)]
    [InlineData("edit-totals", false)]
    [InlineData("edit-lines", false)]
    [InlineData("edit-shipment", false)]
    [InlineData("facts-without-state", false)]
    [InlineData("delete-return", false)]
    [InlineData("edit-attempt", false)]
    [InlineData("delete-attempt", false)]
    public void A_customer_return_moves_one_step_forward_and_its_document_lines_totals_and_attempts_never_change(string change, bool passes)
    {
        using var context = Context();
        var row = Attach(context, NewCustomerReturnRow(change switch { "approve" => 2, "finalize" => 3, "step-back" => 2, _ => 1 }));
        switch (change)
        {
            case "submit":
                (row.State, row.SubmittedBy, row.SubmittedAtUtc, row.Version) = (2, "synthetic-preparer", At, 2);
                break;
            case "approve":
                (row.State, row.ApprovedBy, row.ApprovedAtUtc, row.Version) = (3, "synthetic-approver", At, 3);
                break;
            case "finalize":
                (row.State, row.FinalizedBy, row.FinalizedAtUtc, row.FinalizationSequence, row.Version) = (4, "synthetic-finance", At, 1, 4);
                break;
            case "skip-a-step":
                (row.State, row.Version) = (3, 2);
                break;
            case "step-back":
                (row.State, row.SubmittedBy, row.SubmittedAtUtc, row.Version) = (1, null, null, 3);
                break;
            case "edit-document":
                row.CustomerDocumentNo = "SENTETIK-IADE-2";
                break;
            case "edit-totals":
                row.NetAmount += 1m;
                break;
            case "edit-lines":
                row.LinesJson = "[{}]";
                break;
            case "edit-shipment":
                row.ShipmentId = Guid.NewGuid();
                break;
            case "facts-without-state":
                row.SubmittedBy = "synthetic-preparer";
                break;
            case "delete-return":
                context.Remove(row);
                break;
            case "edit-attempt":
                Attach(context, NewCustomerReturnAttemptRow(row.Id)).ActorId = "synthetic-other";
                break;
            case "delete-attempt":
                context.Remove(Attach(context, NewCustomerReturnAttemptRow(row.Id)));
                break;
        }

        if (passes)
        {
            Assert.Throws<GuardPassedException>(() => context.SaveChanges());
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        }
    }

    private static CustomerReturnRow NewCustomerReturnRow(int state) => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, OriginalInvoiceId = Guid.NewGuid(), OriginalDocumentNo = "SF-SENTETIK-1",
        PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(), AmountScale = 2,
        CustomerDocumentNo = "SENTETIK-IADE-1", CustomerDocumentDate = new DateOnly(2026, 11, 10), State = state,
        CreatedBy = "synthetic-preparer", CreatedAtUtc = At,
        SubmittedBy = state >= 2 ? "synthetic-preparer" : null, SubmittedAtUtc = state >= 2 ? At : null,
        ApprovedBy = state >= 3 ? "synthetic-approver" : null, ApprovedAtUtc = state >= 3 ? At : null,
        NetAmount = 100m, VatAmount = 20m, WithholdingAmount = 0m, LineTotalAmount = 120m, LineCount = 1, LinesJson = "[]",
        Version = state, RowVersion = [1, 2, 3, 4, 5, 6, 7, 8], UpdatedAtUtc = At, CorrelationId = "synthetic-correlation",
    };

    private static CustomerReturnAttemptRow NewCustomerReturnAttemptRow(Guid returnId) => new()
    {
        CompanyId = CompanyA, CommandName = SalesCustomerReturnCommands.CreateV1,
        IdempotencyKeyHash = new string('A', 64), RequestHash = new string('B', 64), ActorId = "synthetic-preparer", Outcome = 1,
        CustomerReturnId = returnId, CorrelationId = "synthetic-correlation", RecordedAtUtc = At,
    };

    /// <summary>API-33 PR2: approval columns, widened state/command CKs and the two immutability triggers; Down is a forward fix (51222).</summary>
    [Fact]
    public void The_invoice_approval_migration_adds_two_triggers_and_its_down_refuses_with_51222_first()
    {
        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddSalesInvoiceApproval();
        var up = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().ToArray();
        Assert.Equal(2, up.Length);
        Assert.Contains(up, op => op.Sql.Contains("TR_Invoices_Guard", StringComparison.Ordinal) && op.Sql.Contains("51223", StringComparison.Ordinal));
        Assert.Contains(up, op => op.Sql.Contains("TR_InvoiceLines_Guard", StringComparison.Ordinal) && op.Sql.Contains("51224", StringComparison.Ordinal));
        var down = migration.DownOperations;
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("THROW 51222,", guard.Sql, StringComparison.Ordinal);
        Assert.Single(down.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    /// <summary>API-35 PR3: the append-only match table; Up adds one immutability trigger (51226), Down is a forward fix (51225).</summary>
    [Fact]
    public void The_invoice_match_migration_adds_one_trigger_and_its_down_refuses_with_51225_first()
    {
        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddSalesInvoiceOfficialMatch();
        var up = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().ToArray();
        var trigger = Assert.Single(up);
        Assert.Contains("TR_InvoiceMatches_Guard", trigger.Sql, StringComparison.Ordinal);
        Assert.Contains("51226", trigger.Sql, StringComparison.Ordinal);
        var down = migration.DownOperations;
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("THROW 51225,", guard.Sql, StringComparison.Ordinal);
        Assert.Single(down.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void The_invoice_match_model_is_keyed_per_invoice_with_unique_official_identity_and_the_widened_command_check()
    {
        using var context = Context();
        var match = context.Model.FindEntityType(typeof(InvoiceMatchRow))!;
        Assert.Equal(["CompanyId", "InvoiceId"], match.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(UniqueIndex(match, "UX_InvoiceMatches_OfficialNo"));
        Assert.True(UniqueIndex(match, "UX_InvoiceMatches_Ettn"));
        var design = context.GetService<IDesignTimeModel>().Model;
        Assert.Contains(design.FindEntityType(typeof(InvoiceMatchRow))!.GetCheckConstraints(), check => check.Name == "CK_InvoiceMatches_Amounts" && check.Sql == SalesDbContext.InvoiceMatchAmountsConstraint);
        Assert.Contains(design.FindEntityType(typeof(InvoiceAttemptRow))!.GetCheckConstraints(), check => check.Name == "CK_InvoiceAttempts_Command" && check.Sql.Contains(GoymenERP.Modules.Sales.Application.Contracts.SalesInvoiceCommands.MatchV1, StringComparison.Ordinal));
    }

    /// <summary>SAT-D241 (EK48): six nullable tax comparison columns, the widened amounts check and the all-or-nothing tax check;
    /// Down is a forward fix (51242) first.</summary>
    [Fact]
    public void The_invoice_match_tax_comparison_migration_adds_the_checks_and_its_down_refuses_with_51242_first_SAT_D241()
    {
        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddInvoiceMatchTaxComparison();
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        var down = migration.DownOperations;
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("THROW 51242,", guard.Sql, StringComparison.Ordinal);
        Assert.Single(down.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());

        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(InvoiceMatchRow))!;
        Assert.Contains(design.GetCheckConstraints(), check => check.Name == "CK_InvoiceMatches_TaxAmounts"
            && check.Sql == SalesDbContext.InvoiceMatchTaxAmountsConstraint);
        Assert.Contains("ISNULL([VatDifference], 0)", SalesDbContext.InvoiceMatchAmountsConstraint, StringComparison.Ordinal);
        Assert.Contains("ISNULL([WithholdingDifference], 0)", SalesDbContext.InvoiceMatchAmountsConstraint, StringComparison.Ordinal);
        foreach (var name in new[] { "InvoiceVatAmount", "OfficialVatAmount", "VatDifference", "InvoiceWithholdingAmount",
            "OfficialWithholdingAmount", "WithholdingDifference" })
        {
            var property = design.FindProperty(name)!;
            Assert.True(property.IsNullable);
            Assert.Equal((28, 8), (property.GetPrecision(), property.GetScale()));
        }
    }

    /// <summary>API-36 PR4: the invoice guard is altered, two triggers are added (51232, 51233); Down is a forward fix (51231) first.</summary>
    [Fact]
    public void The_invoice_finalization_migration_alters_one_trigger_adds_two_and_its_down_refuses_with_51231_first()
    {
        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddSalesInvoiceFinalization();
        var up = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().ToArray();
        Assert.Equal(3, up.Length);
        Assert.Contains(up, op => op.Sql.Contains("ALTER TRIGGER [sales].[TR_Invoices_Guard]", StringComparison.Ordinal) && op.Sql.Contains("51223", StringComparison.Ordinal));
        Assert.Contains(up, op => op.Sql.Contains("TR_InvoiceFinalizations_Guard", StringComparison.Ordinal) && op.Sql.Contains("51232", StringComparison.Ordinal));
        Assert.Contains(up, op => op.Sql.Contains("TR_OutboxMessages_Guard", StringComparison.Ordinal) && op.Sql.Contains("51233", StringComparison.Ordinal));
        var down = migration.DownOperations;
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("THROW 51231,", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(3, down.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Count());
        Assert.DoesNotContain("DROP TRIGGER", guard.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_invoice_finalization_model_keeps_one_ledger_per_invoice_and_widens_the_state_and_command_checks()
    {
        using var context = Context();
        var ledger = context.Model.FindEntityType(typeof(InvoiceFinalizationRow))!;
        Assert.Equal(["CompanyId", "InvoiceId"], ledger.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(UniqueIndex(ledger, "UX_InvoiceFinalizations_AdmissionId"));
        var design = context.GetService<IDesignTimeModel>().Model;
        Assert.Contains(design.FindEntityType(typeof(InvoiceAttemptRow))!.GetCheckConstraints(), check => check.Name == "CK_InvoiceAttempts_Command"
            && check.Sql.Contains(GoymenERP.Modules.Sales.Application.Contracts.SalesInvoiceCommands.FinalizeV1, StringComparison.Ordinal)
            && check.Sql.Contains(GoymenERP.Modules.Sales.Application.Contracts.SalesInvoiceCommands.ResendFinanceV1, StringComparison.Ordinal));
        Assert.Contains(design.FindEntityType(typeof(InvoiceRow))!.GetCheckConstraints(), check => check.Name == "CK_Invoices_State" && check.Sql.Contains('6'));
    }

    /// <summary>DEC-SAL-002: the reason column's rollback refuses with Sales' own 51057 BEFORE anything is dropped (forward fix).</summary>
    [Fact]
    public void The_revision_reason_migration_down_refuses_with_51057_before_it_drops_anything()
    {
        var down = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddRevisionReasons().DownOperations;
        Assert.Equal(3, down.Count);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [sales].[DraftRevisions] WHERE [Reason] IS NOT NULL)", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51057,", guard.Sql, StringComparison.Ordinal);
        Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropCheckConstraintOperation>(down[1]);
        var drop = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropColumnOperation>(down[2]);
        Assert.Equal(("sales", "DraftRevisions", "Reason"), (drop.Schema, drop.Table, drop.Name));
        var up = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddRevisionReasons().UpOperations;
        var column = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>(up[0]);
        Assert.Equal(("Reason", true, 4000), (column.Name, column.IsNullable, column.MaxLength));
    }

    /// <summary>
    /// SAT-D2 / API-12: the discount-exception replay migration only widens CK_DraftAttempts_Result to the new command; its
    /// rollback refuses with Sales' own 51069 BEFORE the constraint is narrowed while such a replay result exists (forward fix).
    /// </summary>
    [Fact]
    public void The_discount_exception_replay_migration_widens_the_result_constraint_and_its_down_refuses_with_51069_first()
    {
        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddSalesDiscountExceptionApprovalReplayResults();
        var up = migration.UpOperations;
        Assert.Equal(2, up.Count);
        Assert.Equal("CK_DraftAttempts_Result", Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropCheckConstraintOperation>(up[0]).Name);
        var widened = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>(up[1]);
        Assert.Equal(("CK_DraftAttempts_Result", SalesDbContext.AttemptResultConstraint), (widened.Name, widened.Sql));
        Assert.Contains("N'sales.order-draft.approve-discount-exceptions.v1'", widened.Sql, StringComparison.Ordinal);

        var down = migration.DownOperations;
        Assert.Equal(3, down.Count);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("[CommandName] = N'sales.order-draft.approve-discount-exceptions.v1' AND [ResultJson] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51069,", guard.Sql, StringComparison.Ordinal);
        Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropCheckConstraintOperation>(down[1]);
        var narrowed = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>(down[2]);
        Assert.Equal(SalesDbContext.AttemptResultConstraintBeforeDiscountExceptions, narrowed.Sql);
        Assert.DoesNotContain("discount", narrowed.Sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("no-company-context")]
    [InlineData("empty-company")]
    public void A_row_of_another_company_or_without_a_company_is_refused_before_the_database(string fault)
    {
        using var context = Context(resolved: fault != "no-company-context");
        var row = Draft();
        row.CompanyId = fault switch { "other-company" => CompanyB, "empty-company" => Guid.Empty, _ => CompanyA };
        context.Drafts.Add(row);

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Theory]
    [InlineData("delete-draft")]
    [InlineData("edit-draft-header")]
    [InlineData("edit-draft-note")]
    [InlineData("edit-draft-responsible")]
    [InlineData("edit-revision-header")]
    [InlineData("delete-revision-header")]
    [InlineData("edit-draft-company")]
    [InlineData("edit-line")]
    [InlineData("edit-line-unit")]
    [InlineData("edit-revision-reason")]
    [InlineData("edit-slice")]
    [InlineData("edit-attempt")]
    [InlineData("edit-counter-company")]
    [InlineData("edit-revision")]
    [InlineData("delete-revision")]
    [InlineData("edit-revision-line")]
    [InlineData("edit-schedule")]
    [InlineData("delete-schedule")]
    [InlineData("edit-schedule-date")]
    [InlineData("delete-schedule-date")]
    [InlineData("edit-cancellation")]
    [InlineData("delete-cancellation")]
    [InlineData("uncancel-draft")]
    [InlineData("draft-to-unknown-state")]
    [InlineData("edit-force-closure")]
    [InlineData("delete-force-closure")]
    [InlineData("edit-user-closure")]
    [InlineData("delete-user-closure")]
    [InlineData("edit-over-delivery-approval")]
    [InlineData("delete-over-delivery-approval")]
    [InlineData("edit-line-closure")]
    [InlineData("delete-line-closure")]
    [InlineData("unclose-draft")]
    [InlineData("closed-to-cancelled")]
    [InlineData("cancelled-to-closed")]
    [InlineData("edit-customer-acceptance-required")]
    [InlineData("acceptance-nonapproved")]
    [InlineData("acceptance-wrong-revision")]
    public void Drafts_lines_slices_and_attempts_never_change_and_nothing_is_deleted(string fault)
    {
        using var context = Context();
        switch (fault)
        {
            case "delete-draft":
                context.Drafts.Remove(Attach(context, Draft()));
                break;
            case "edit-draft-header":
                // API-04 admits the customer, currency, delivery references, sales owner and due terms; the business date stays.
                Attach(context, Draft()).BusinessDate = new DateOnly(2026, 9, 19);
                break;
            case "edit-draft-note":
                Attach(context, Draft()).Note = "başka";
                break;
            case "edit-draft-responsible":
                Attach(context, Draft()).ResponsibleId = Guid.NewGuid();
                break;
            // API-04: an archived revision's header is append-only — no column of it may change, nothing is deleted.
            case "edit-revision-header":
                Attach(context, RevisionHeader()).PartyId = Guid.NewGuid();
                break;
            case "delete-revision-header":
                context.DraftRevisionHeaders.Remove(Attach(context, RevisionHeader()));
                break;
            case "edit-draft-company":
                Attach(context, Draft()).CompanyId = CompanyB;
                break;
            case "edit-line":
                // DEC-SAL-002 admits the ordered quantity under a quantity revision; the product binding stays immutable.
                Attach(context, Line()).ProductVersion = "8";
                break;
            case "edit-line-unit":
                Attach(context, Line()).OrderUnitId = Guid.NewGuid();
                break;
            case "edit-revision-reason":
                Attach(context, Revision()).Reason = "sonradan yazılan gerekçe";
                break;
            case "edit-slice":
                Attach(context, Slice()).RequestedDeliveryDate = new DateOnly(2026, 10, 1);
                break;
            case "edit-attempt":
                Attach(context, Attempt()).RequestHash = new string('1', 64);
                break;
            case "edit-revision":
                Attach(context, Revision()).ApprovedBy = "someone-else";
                break;
            case "delete-revision":
                context.DraftRevisions.Remove(Attach(context, Revision()));
                break;
            case "edit-revision-line":
                Attach(context, RevisionLine()).UnitPrice = "1";
                break;
            // DEC-SAL-003: a recorded delivery schedule is append-only — no column of it may change, nothing is deleted.
            case "edit-schedule":
                Attach(context, Schedule()).RequestedDeliveryDate = new DateOnly(2026, 11, 1);
                break;
            case "delete-schedule":
                context.DraftDeliverySchedules.Remove(Attach(context, Schedule()));
                break;
            case "edit-schedule-date":
                Attach(context, ScheduleDate()).RequestedDeliveryDate = new DateOnly(2026, 11, 1);
                break;
            case "delete-schedule-date":
                context.DraftDeliveryScheduleDates.Remove(Attach(context, ScheduleDate()));
                break;
            // DEC-SAL-005 / AT-007: a recorded cancellation is append-only, and the lifecycle only ever moves Draft → Cancelled.
            case "edit-cancellation":
                Attach(context, Cancellation()).Reason = "another reason";
                break;
            case "delete-cancellation":
                context.DraftCancellations.Remove(Attach(context, Cancellation()));
                break;
            case "uncancel-draft":
                var cancelled = Draft();
                cancelled.State = 2;
                cancelled.ApprovalState = 3;
                Attach(context, cancelled).State = 1;
                break;
            case "draft-to-unknown-state":
                // No 30: Closed (3) is a lifecycle state now; the first value after it is not.
                Attach(context, Draft()).State = 4;
                break;
            // No 30 / SAT-D12: a recorded force-closure is append-only, and the lifecycle moves only Draft → Closed (never back,
            // never across to Cancelled, never from Cancelled).
            case "edit-force-closure":
                Attach(context, ForceClosure()).LinesJson = "[{}]";
                break;
            case "delete-force-closure":
                context.DraftForceClosures.Remove(Attach(context, ForceClosure()));
                break;
            // SAT-D32: a recorded user closure is append-only too (no column may change, no row may go).
            case "edit-user-closure":
                Attach(context, UserClosure()).ClosedBy = "another-closer";
                break;
            case "delete-user-closure":
                context.DraftUserClosures.Remove(Attach(context, UserClosure()));
                break;
            // SAT-D30 / SAT-D213: a recorded over-delivery approval is append-only.
            case "edit-over-delivery-approval":
                Attach(context, OverDeliveryApproval()).ApprovedShipped = "999";
                break;
            case "delete-over-delivery-approval":
                context.DraftOverDeliveryApprovals.Remove(Attach(context, OverDeliveryApproval()));
                break;
            // SAT-D17: a recorded line closure is append-only.
            case "edit-line-closure":
                Attach(context, LineClosure()).CancelledRemainder = "999";
                break;
            case "delete-line-closure":
                context.DraftLineClosures.Remove(Attach(context, LineClosure()));
                break;
            case "unclose-draft":
                var closed = Draft();
                closed.State = 3;
                closed.ApprovalState = 3;
                Attach(context, closed).State = 1;
                break;
            case "closed-to-cancelled":
                var closedOrder = Draft();
                closedOrder.State = 3;
                closedOrder.ApprovalState = 3;
                Attach(context, closedOrder).State = 2;
                break;
            case "cancelled-to-closed":
                var cancelledOrder = Draft();
                cancelledOrder.State = 2;
                cancelledOrder.ApprovalState = 3;
                Attach(context, cancelledOrder).State = 3;
                break;
            case "edit-customer-acceptance-required":
                Attach(context, Draft()).CustomerAcceptanceRequired = true;
                break;
            case "acceptance-nonapproved":
            case "acceptance-wrong-revision":
                var acceptance = Draft();
                acceptance.CustomerAcceptanceRequired = true;
                acceptance.ApprovalState = fault == "acceptance-nonapproved" ? 2 : 3;
                acceptance.Revision = fault == "acceptance-wrong-revision" ? 1 : 0;
                var trackedAcceptance = Attach(context, acceptance);
                trackedAcceptance.CustomerAcceptedRevision = 0;
                trackedAcceptance.CustomerAcceptedBy = "synthetic-customer";
                trackedAcceptance.CustomerAcceptedAtUtc = At;
                break;
            default:
                Attach(context, new DraftNumberRow { CompanyId = CompanyA, Next = 2, Version = 1, UpdatedAtUtc = At }).CompanyId = CompanyB;
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    /// <summary>
    /// K3 Head B — the DELIBERATE narrowing of "lines never change": ONLY the three commercial columns of a line
    /// and the header's revision number pass the write guard (a commercial revision changes them); the guard is what
    /// would have refused them before this head, so the cases above (quantity, header, slices, attempts, history)
    /// stay refused and these reach the provider pipeline. "Reached" is MEASURED by the context's sentinel interceptor
    /// (<see cref="GuardPassedException"/>): it fires only after the guard let the save through and before any
    /// connection. The provider itself is never a witness here — its connection failure differs by platform (a plain
    /// SqlException on Windows, EF's transient-failure InvalidOperationException on Linux), which is exactly what a
    /// type-based "anything but the guard's exception" assertion could not tell apart.
    /// </summary>
    [Theory]
    [InlineData("line-price")]
    [InlineData("line-discount")]
    [InlineData("line-quantity")]
    [InlineData("header-revision")]
    [InlineData("customer-acceptance")]
    public void Only_a_lines_commercial_columns_and_the_headers_revision_pass_the_write_guard(string change)
    {
        using var context = Context();
        switch (change)
        {
            case "line-price":
                var line = Attach(context, Line());
                line.UnitPrice = "12.50";
                line.PriceUnitId = Guid.NewGuid();
                break;
            case "line-discount":
                Attach(context, Line()).DiscountPercent = "5";
                break;
            case "line-quantity":
                // DEC-SAL-002: the quantity revision moves the ordered quantity and its verbatim text, nothing else.
                var revised = Attach(context, Line());
                revised.Quantity = 2m;
                revised.QuantityText = "2";
                break;
            case "header-revision":
                Attach(context, Draft()).Revision = 1;
                break;
            default:
                var acceptance = Draft();
                acceptance.CustomerAcceptanceRequired = true;
                acceptance.ApprovalState = 3;
                var trackedAcceptance = Attach(context, acceptance);
                trackedAcceptance.CustomerAcceptedRevision = 0;
                trackedAcceptance.CustomerAcceptedBy = "synthetic-customer";
                trackedAcceptance.CustomerAcceptedAtUtc = At;
                break;
        }

        Assert.Throws<GuardPassedException>(() => context.SaveChanges());
    }

    [Fact]
    public async Task The_store_refuses_a_non_canonical_key_a_malformed_hash_and_an_unresolved_company_before_any_read()
    {
        using var context = Context();
        var store = Store(context);
        var hash = new string('a', 64);

        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreDraftAsync(Envelope(" k-1"), hash, _ => throw new InvalidOperationException("not reached")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreDraftAsync(Envelope("k-1 "), hash, _ => throw new InvalidOperationException("not reached")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreDraftAsync(Envelope(""), hash, _ => throw new InvalidOperationException("not reached")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreDraftAsync(Envelope("k-1"), new string('A', 64), _ => throw new InvalidOperationException("not reached")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreDraftAsync(Envelope("k-1"), "abc", _ => throw new InvalidOperationException("not reached")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreDraftAsync(Envelope("k-1") with { ActorId = " " }, hash, _ => throw new InvalidOperationException("not reached")));
        Assert.Equal(SalesDraftStoreCodes.ValueDoesNotFit, (await Assert.ThrowsAsync<SalesDraftStoreException>(
            () => store.StoreDraftAsync(Envelope(new string('k', 201)), hash, _ => throw new InvalidOperationException("not reached")))).Code);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ListDraftsAsync(0, null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ListDraftsAsync(101, null));

        using var unresolved = Context(resolved: false);
        var unresolvedStore = Store(unresolved);
        Assert.Equal(SalesDraftStoreCodes.CompanyContextMismatch, (await Assert.ThrowsAsync<SalesDraftStoreException>(
            () => unresolvedStore.StoreDraftAsync(Envelope("k-1"), hash, _ => throw new InvalidOperationException("not reached")))).Code);
        Assert.Equal(SalesDraftStoreCodes.CompanyContextMismatch, (await Assert.ThrowsAsync<SalesDraftStoreException>(
            () => unresolvedStore.FindDraftAsync(Guid.NewGuid()))).Code);
        Assert.Equal(SalesDraftStoreCodes.CompanyContextMismatch, (await Assert.ThrowsAsync<SalesDraftStoreException>(
            () => unresolvedStore.ListDraftsAsync(10, null))).Code);
    }

    [Fact]
    public void The_idempotency_key_is_stored_as_the_uppercase_sha256_of_its_trimmed_text()
    {
        // ADR-011 §2: no raw key; trimmed, then SHA-256, uppercase hex. Case in the key itself still counts.
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("k-1")));
        Assert.Equal(expected, SalesStore.IdempotencyKeyHash("k-1"));
        Assert.Equal(64, expected.Length);
        Assert.Equal(expected, expected.ToUpperInvariant());
        Assert.Equal(expected, SalesStore.IdempotencyKeyHash(" k-1 "));
        Assert.NotEqual(expected, SalesStore.IdempotencyKeyHash("K-1"));
        Assert.Equal("sales.order-draft.create.v1", SalesDraftCommands.CreateV1);
    }

    [Fact]
    public void The_document_number_is_technical_company_text_from_the_counter_and_the_utc_year()
    {
        Assert.Equal("SO-2026-000007", SalesStore.DocumentNo(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.FromHours(3)), 7));
        // The year is UTC's: an Istanbul new-year hour is still the old year's number text.
        Assert.Equal("SO-2026-000008", SalesStore.DocumentNo(new DateTimeOffset(2027, 1, 1, 1, 0, 0, TimeSpan.FromHours(3)), 8));
        Assert.Equal("SO-2026-1000000", SalesStore.DocumentNo(At, 1_000_000));
        Assert.True(SalesStore.DocumentNo(At, long.MaxValue).Length <= SalesOrderDraft.MaximumDocumentNoLength);
    }

    [Fact]
    public void The_list_cursor_round_trips_and_is_bound_to_the_company_and_the_ordering()
    {
        var id = Guid.NewGuid();
        var cursor = SalesStore.ListCursor.Encode(CompanyA, At, id);
        Assert.DoesNotContain(cursor, character => character is '+' or '/' or '=');
        Assert.Equal((At, id), SalesStore.ListCursor.Decode(cursor, CompanyA));

        Assert.Equal(SalesDraftStoreCodes.InvalidCursor, CodeOf(() => SalesStore.ListCursor.Decode(cursor, CompanyB)));
        Assert.Equal(SalesDraftStoreCodes.InvalidCursor, CodeOf(() => SalesStore.ListCursor.Decode("not a cursor!", CompanyA)));
        Assert.Equal(SalesDraftStoreCodes.InvalidCursor, CodeOf(() => SalesStore.ListCursor.Decode(Base64Url("{\"x\":1}"), CompanyA)));
        var otherOrdering = Base64Url("{\"company\":\"" + CompanyA + "\",\"ordering\":\"id asc\",\"createdAtUtc\":\"2026-09-18T09:00:00.0000000Z\",\"id\":\"" + id + "\"}");
        Assert.Equal(SalesDraftStoreCodes.InvalidCursor, CodeOf(() => SalesStore.ListCursor.Decode(otherOrdering, CompanyA)));
    }

    private static string CodeOf(Action action) => Assert.Throws<SalesDraftStoreException>(action).Code;

    private static string Base64Url(string json) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool UniqueIndex(IEntityType entity, string name) =>
        entity.GetIndexes().Single(index => index.GetDatabaseName() == name).IsUnique;

    private static SalesDraftWriteEnvelope Envelope(string key) =>
        new("synthetic-actor", "SENTETIK-sales-permission", ["SENTETIK-SALES"], "synthetic-correlation", null, key, "Web");

    /// <summary>The store over a context and the company context that context was built with.</summary>
    private static SalesStore Store(SalesDbContext context) =>
        new(context, Companies[context], new AuditEventCollector(), TimeProvider.System);

    private static readonly Dictionary<SalesDbContext, ICompanyContext> Companies = [];

    private static T Attach<T>(SalesDbContext context, T row) where T : class
    {
        context.Attach(row);
        return row;
    }

    private static DraftRow Draft() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, DocumentNo = "SO-2026-000001", Kind = 1, State = 1, Revision = 0, Version = 1,
        RowVersion = [1, 2, 3, 4, 5, 6, 7, 8], PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(),
        BusinessDate = new DateOnly(2026, 9, 18), MoneyOperation = SalesOrderDraft.MoneyOperation, MoneyProfileId = Guid.NewGuid(),
        MoneyProfileVersion = "3", MoneyEffectiveOn = new DateOnly(2026, 9, 18), MoneyIsActive = true, MoneyEffectiveFrom = new DateOnly(2026, 9, 1),
        MoneyScale = 2, CreatedAtUtc = At, UpdatedAtUtc = At, CreatedBy = "synthetic-actor", CorrelationId = "synthetic-correlation",
    };

    private static DraftLineRow Line() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), LineNo = 1, LineId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductVersion = "7",
        ProductCode = "GDC-001", ProductName = "Sentetik bant", ProductEffectiveOn = new DateOnly(2026, 9, 18), OrderUnitCode = "kg",
        OrderUnitDimensionCode = "weight", OrderUnitProfileVersion = "4", Quantity = 1m, QuantityText = "1",
    };

    private static DraftDeliverySliceRow Slice() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), LineNo = 1, SliceNo = 1, SliceId = Guid.NewGuid(), OrderUnitId = Guid.NewGuid(),
        Quantity = 1m, QuantityText = "1", RequestedDeliveryDate = new DateOnly(2026, 9, 25),
    };

    private static DraftRevisionRow Revision() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), Revision = 0, SubmittedBy = "prep", SubmittedAtUtc = At, ApprovedBy = "mgr", ApprovedAtUtc = At,
        MoneyProfileId = Guid.NewGuid(), MoneyProfileVersion = "3", MoneyScale = 2, ArchivedAtUtc = At, ArchivedBy = "pricer", CorrelationId = "synthetic-correlation",
    };

    private static DraftRevisionHeaderRow RevisionHeader() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), Revision = 0, PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(),
        CurrencyId = Guid.NewGuid(),
    };

    private static DraftRevisionLineRow RevisionLine() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), Revision = 0, LineNo = 1, LineId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductVersion = "7",
        OrderUnitId = Guid.NewGuid(), QuantityText = "1", UnitPrice = "12.50", PriceUnitId = Guid.NewGuid(), DiscountPercent = null,
    };

    /// <summary>
    /// DEC-SAL-003: a delivery-schedule change APPENDS a schedule and its dates and moves only the header's version and
    /// UpdatedAtUtc (both admitted before this head); the existing line and slice rows are never rewritten, so the save
    /// reaches the provider pipeline without widening what the guard admits.
    /// </summary>
    [Fact]
    public void A_schedule_change_passes_the_write_guard_by_appending_rows_and_moving_only_the_headers_version()
    {
        using var context = Context();
        var header = Attach(context, Draft());
        header.Version = 2;
        header.UpdatedAtUtc = At.AddMinutes(1);
        context.DraftDeliverySchedules.Add(Schedule());
        context.DraftDeliveryScheduleDates.Add(ScheduleDate());

        Assert.Throws<GuardPassedException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_schedule_tables_are_keyed_by_draft_schedule_line_and_slice_and_bound_to_the_draft_and_its_lines()
    {
        using var context = Context();
        var schedules = context.Model.FindEntityType(typeof(DraftDeliveryScheduleRow))!;
        Assert.Equal(["CompanyId", "DraftId", "ScheduleNo"], schedules.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal("DraftDeliverySchedules", schedules.GetTableName());
        var dates = context.Model.FindEntityType(typeof(DraftDeliveryScheduleDateRow))!;
        Assert.Equal(["CompanyId", "DraftId", "ScheduleNo", "LineNo", "SliceNo"], dates.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(dates.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(DraftDeliveryScheduleRow));
        Assert.Contains(dates.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(DraftLineRow));
        Assert.Contains(schedules.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(DraftRow));
        var design = context.GetService<IDesignTimeModel>().Model;
        Assert.Contains(design.FindEntityType(typeof(DraftDeliveryScheduleDateRow))!.GetCheckConstraints(),
            constraint => constraint.Name == "CK_DraftDeliveryScheduleDates_Slice");
    }

    /// <summary>
    /// DEC-SAL-005: a cancellation APPENDS its row and moves only the header's lifecycle state (Draft → Cancelled), its version
    /// and UpdatedAtUtc; no line, slice, revision, schedule or approval column is rewritten.
    /// </summary>
    [Fact]
    public void A_cancellation_passes_the_write_guard_by_appending_its_row_and_moving_only_the_headers_state_and_version()
    {
        using var context = Context();
        var header = Draft();
        header.ApprovalState = 3;
        Attach(context, header);
        header.State = 2;
        header.Version = 5;
        header.UpdatedAtUtc = At.AddMinutes(1);
        context.DraftCancellations.Add(Cancellation());

        Assert.Throws<GuardPassedException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_cancellation_table_is_keyed_by_the_draft_bound_to_it_and_the_header_state_admits_cancelled_only_beside_approved()
    {
        using var context = Context();
        var cancellations = context.Model.FindEntityType(typeof(DraftCancellationRow))!;
        Assert.Equal(["CompanyId", "DraftId"], cancellations.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal("DraftCancellations", cancellations.GetTableName());
        Assert.Contains(cancellations.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(DraftRow));
        Assert.Equal((SalesOrderDraft.MaximumCancellationReasonLength, 128),
            (cancellations.FindProperty(nameof(DraftCancellationRow.Reason))!.GetMaxLength(), cancellations.FindProperty(nameof(DraftCancellationRow.CancelledBy))!.GetMaxLength()));
        var design = context.GetService<IDesignTimeModel>().Model;
        Assert.Equal(["CK_DraftCancellations_Identity", "CK_DraftCancellations_Impact"],
            design.FindEntityType(typeof(DraftCancellationRow))!.GetCheckConstraints().Select(constraint => constraint.Name).Order(StringComparer.Ordinal));
        // No 30 widened the lifecycle by Closed (3); cancelled and closed alike stand only beside an approved order.
        Assert.Equal("[Kind] = 1 AND [State] IN (1, 2, 3) AND [Revision] >= 0 AND ([State] = 1 OR [ApprovalState] = 3)",
            design.FindEntityType(typeof(DraftRow))!.GetCheckConstraints().Single(constraint => constraint.Name == "CK_Drafts_State").Sql);
    }

    /// <summary>
    /// No 30 / SAT-D12: the force-closure table mirrors the cancellation's — keyed by (company, draft), bound to the draft, its
    /// own reason bound, ISJSON on the impact AND on the closed remainders — and the header's state now admits Closed (3), still
    /// only beside an approved order.
    /// </summary>
    [Fact]
    public void The_force_closure_table_is_keyed_by_the_draft_bound_to_it_and_the_header_state_admits_closed_only_beside_approved()
    {
        using var context = Context();
        var closures = context.Model.FindEntityType(typeof(DraftForceClosureRow))!;
        Assert.Equal(["CompanyId", "DraftId"], closures.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal("DraftForceClosures", closures.GetTableName());
        Assert.Equal("sales", closures.GetSchema());
        var foreignKey = Assert.Single(closures.GetForeignKeys());
        Assert.Equal(typeof(DraftRow), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(["CompanyId", "Id"], foreignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Equal((SalesOrderDraft.MaximumForceCloseReasonLength, 128),
            (closures.FindProperty(nameof(DraftForceClosureRow.Reason))!.GetMaxLength(), closures.FindProperty(nameof(DraftForceClosureRow.ClosedBy))!.GetMaxLength()));
        Assert.NotEmpty(closures.GetDeclaredQueryFilters());
        var design = context.GetService<IDesignTimeModel>().Model;
        var constraints = design.FindEntityType(typeof(DraftForceClosureRow))!.GetCheckConstraints().ToDictionary(constraint => constraint.Name!, constraint => constraint.Sql);
        Assert.Equal(["CK_DraftForceClosures_Identity", "CK_DraftForceClosures_Impact", "CK_DraftForceClosures_Lines"],
            constraints.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("ISJSON([ImpactJson]) = 1", constraints["CK_DraftForceClosures_Impact"]);
        Assert.Equal("ISJSON([LinesJson]) = 1", constraints["CK_DraftForceClosures_Lines"]);
        Assert.Contains("[DraftVersion] >= 5", constraints["CK_DraftForceClosures_Identity"], StringComparison.Ordinal);
        Assert.Equal("[Kind] = 1 AND [State] IN (1, 2, 3) AND [Revision] >= 0 AND ([State] = 1 OR [ApprovalState] = 3)",
            design.FindEntityType(typeof(DraftRow))!.GetCheckConstraints().Single(constraint => constraint.Name == "CK_Drafts_State").Sql);
    }

    /// <summary>No 30: the force-closure rollback refuses with Sales' own 51061 BEFORE anything is dropped, and restores the two-state CK.</summary>
    [Fact]
    public void The_force_closure_migration_down_refuses_with_51061_before_it_drops_anything()
    {
        var down = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddDraftForceClosures().DownOperations;
        Assert.Equal(4, down.Count);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [sales].[DraftForceClosures]) OR EXISTS (SELECT 1 FROM [sales].[Drafts] WHERE [State] = 3)",
            guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51061,", guard.Sql, StringComparison.Ordinal);
        var drop = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>(down[1]);
        Assert.Equal(("sales", "DraftForceClosures"), (drop.Schema, drop.Name));
        Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropCheckConstraintOperation>(down[2]);
        var restored = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>(down[3]);
        Assert.Equal("[Kind] = 1 AND [State] IN (1, 2) AND [Revision] >= 0 AND ([State] = 1 OR [ApprovalState] = 3)", restored.Sql);
    }

    /// <summary>
    /// SAT-D32: the user-closure table — keyed by (company, draft), bound to the draft, no reason and no remainder column; the
    /// header's state reuses Closed (3), so CK_Drafts_State does not change.
    /// </summary>
    [Fact]
    public void The_user_closure_table_is_keyed_by_the_draft_bound_to_it_and_carries_no_reason_or_remainder()
    {
        using var context = Context();
        var closures = context.Model.FindEntityType(typeof(DraftUserClosureRow))!;
        Assert.Equal(["CompanyId", "DraftId"], closures.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(("DraftUserClosures", "sales"), (closures.GetTableName(), closures.GetSchema()));
        var foreignKey = Assert.Single(closures.GetForeignKeys());
        Assert.Equal(typeof(DraftRow), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(["CompanyId", "Id"], foreignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Equal(["ClosedAtUtc", "ClosedBy", "CompanyId", "CorrelationId", "DraftId", "DraftVersion", "Revision"],
            closures.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal((128, 128), (closures.FindProperty(nameof(DraftUserClosureRow.ClosedBy))!.GetMaxLength(),
            closures.FindProperty(nameof(DraftUserClosureRow.CorrelationId))!.GetMaxLength()));
        Assert.NotEmpty(closures.GetDeclaredQueryFilters());
        var design = context.GetService<IDesignTimeModel>().Model;
        var constraint = Assert.Single(design.FindEntityType(typeof(DraftUserClosureRow))!.GetCheckConstraints());
        Assert.Equal(("CK_DraftUserClosures_Identity", SalesDbContext.UserClosureIdentityConstraint), (constraint.Name, constraint.Sql));
        Assert.Contains("[DraftVersion] >= 5", constraint.Sql, StringComparison.Ordinal);
        Assert.Equal("[Kind] = 1 AND [State] IN (1, 2, 3) AND [Revision] >= 0 AND ([State] = 1 OR [ApprovalState] = 3)",
            design.FindEntityType(typeof(DraftRow))!.GetCheckConstraints().Single(item => item.Name == "CK_Drafts_State").Sql);
    }

    /// <summary>SAT-D32: the user-closure rollback refuses with Sales' own 51212 BEFORE the table is dropped (forward fix).</summary>
    [Fact]
    public void The_user_closure_migration_down_refuses_with_51212_before_it_drops_anything()
    {
        var down = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddDraftUserClosures().DownOperations;
        Assert.Equal(2, down.Count);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [sales].[DraftUserClosures])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[CommandName] = N'sales.order-draft.close.v1' AND [Outcome] = 1", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51212,", guard.Sql, StringComparison.Ordinal);
        var drop = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>(down[1]);
        Assert.Equal(("sales", "DraftUserClosures"), (drop.Schema, drop.Name));
    }

    private static DraftUserClosureRow UserClosure() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), Revision = 0, ClosedBy = "synthetic-closer", ClosedAtUtc = At,
        DraftVersion = 5, CorrelationId = "synthetic-correlation",
    };

    /// <summary>
    /// SAT-D30 / SAT-D213: the over-delivery approval table — numbered per line under (company, draft, line, number), one row per
    /// version step, bound to the draft and to its line, tenant-filtered, with the identity check; Up is pure DDL and Down refuses
    /// with Sales' own 51213 BEFORE it drops the table (forward fix).
    /// </summary>
    [Fact]
    public void The_over_delivery_approval_table_is_numbered_per_line_bound_to_its_line_and_its_down_refuses_with_51213()
    {
        using var context = Context();
        var approvals = context.Model.FindEntityType(typeof(DraftOverDeliveryApprovalRow))!;
        Assert.Equal(["CompanyId", "DraftId", "LineId", "ApprovalNo"], approvals.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(("sales", "DraftOverDeliveryApprovals"), (approvals.GetSchema(), approvals.GetTableName()));
        Assert.True(UniqueIndex(approvals, "UX_DraftOverDeliveryApprovals_DraftVersion"));
        // The position-only line key and the line-identity key (AddDraftLineIdentityForeignKeys) both point at the line.
        Assert.Equal([typeof(DraftLineRow), typeof(DraftLineRow), typeof(DraftRow)],
            approvals.GetForeignKeys().Select(key => key.PrincipalEntityType.ClrType).OrderBy(type => type.Name, StringComparer.Ordinal));
        Assert.All(approvals.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        Assert.Equal(SalesOrderDraft.MaximumActorLength, approvals.FindProperty(nameof(DraftOverDeliveryApprovalRow.ApprovedBy))!.GetMaxLength());
        Assert.NotEmpty(approvals.GetDeclaredQueryFilters());
        var design = context.GetService<IDesignTimeModel>().Model;
        var check = Assert.Single(design.FindEntityType(typeof(DraftOverDeliveryApprovalRow))!.GetCheckConstraints());
        Assert.Equal(("CK_DraftOverDeliveryApprovals_Identity", SalesDbContext.OverDeliveryApprovalIdentityConstraint), (check.Name, check.Sql));

        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddDraftOverDeliveryApprovals();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation);
        Assert.Equal(2, migration.DownOperations.Count);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [sales].[DraftOverDeliveryApprovals])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51213,", guard.Sql, StringComparison.Ordinal);
        var drop = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>(migration.DownOperations[1]);
        Assert.Equal(("sales", "DraftOverDeliveryApprovals"), (drop.Schema, drop.Name));
    }

    /// <summary>
    /// SAT-D17 / SAT-D168: the line-closure table - one row per line under (company, draft, line), one row per version step, bound
    /// to the draft and to its line, tenant-filtered, with the identity check; Up is pure DDL and Down refuses with Sales' own 51228
    /// BEFORE it drops the table (forward fix).
    /// </summary>
    [Fact]
    public void The_line_closure_table_is_keyed_per_line_bound_to_its_line_and_its_down_refuses_with_51228()
    {
        using var context = Context();
        var closures = context.Model.FindEntityType(typeof(DraftLineClosureRow))!;
        Assert.Equal(["CompanyId", "DraftId", "LineId"], closures.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(("sales", "DraftLineClosures"), (closures.GetSchema(), closures.GetTableName()));
        Assert.True(UniqueIndex(closures, "UX_DraftLineClosures_DraftVersion"));
        // The position-only line key and the line-identity key (AddDraftLineIdentityForeignKeys) both point at the line.
        Assert.Equal([typeof(DraftLineRow), typeof(DraftLineRow), typeof(DraftRow)],
            closures.GetForeignKeys().Select(key => key.PrincipalEntityType.ClrType).OrderBy(type => type.Name, StringComparer.Ordinal));
        Assert.All(closures.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        Assert.Equal(SalesOrderDraft.MaximumActorLength, closures.FindProperty(nameof(DraftLineClosureRow.ClosedBy))!.GetMaxLength());
        Assert.NotEmpty(closures.GetDeclaredQueryFilters());
        var design = context.GetService<IDesignTimeModel>().Model;
        var check = Assert.Single(design.FindEntityType(typeof(DraftLineClosureRow))!.GetCheckConstraints());
        Assert.Equal(("CK_DraftLineClosures_Identity", SalesDbContext.LineClosureIdentityConstraint), (check.Name, check.Sql));

        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddDraftLineClosures();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation);
        Assert.Equal(2, migration.DownOperations.Count);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [sales].[DraftLineClosures])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51228,", guard.Sql, StringComparison.Ordinal);
        var drop = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>(migration.DownOperations[1]);
        Assert.Equal(("sales", "DraftLineClosures"), (drop.Schema, drop.Name));
    }

    /// <summary>
    /// SAT-D17: the stored band of a line closure is never the block band. The domain's restore accepts any defined band, so the
    /// table's identity check is what keeps a block-band row out: it admits Within (1) to Approval (3) only, and Block is 4.
    /// </summary>
    [Fact]
    public void The_line_closure_identity_check_admits_within_to_approval_only_and_keeps_the_block_band_out()
    {
        Assert.Equal((1, 2, 3, 4), ((int)DeliveryToleranceBand.Within, (int)DeliveryToleranceBand.Warn,
            (int)DeliveryToleranceBand.Approval, (int)DeliveryToleranceBand.Block));
        Assert.Contains("AND [Band] BETWEEN 1 AND 3 AND", SalesDbContext.LineClosureIdentityConstraint, StringComparison.Ordinal);
        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddDraftLineClosures();
        var table = Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>(),
            operation => operation.Name == "DraftLineClosures");
        Assert.Equal(SalesDbContext.LineClosureIdentityConstraint, Assert.Single(table.CheckConstraints).Sql);
    }

    /// <summary>
    /// Review finding: the three line-bound history tables that store a LineId bind (CompanyId, DraftId, LineNo, LineId) to the
    /// draft line's alternate key, so the stored identity is always the line at that position; the delete behaviour stays
    /// Restrict and the position-only key stays in place. The two line-bound tables without a LineId keep their single line key.
    /// </summary>
    [Fact]
    public void The_line_bound_history_rows_bind_their_line_identity_to_the_line_at_their_position()
    {
        using var context = Context();
        var lines = context.Model.FindEntityType(typeof(DraftLineRow))!;
        string[] identity = ["CompanyId", "DraftId", "LineNo", "LineId"];
        var key = Assert.Single(lines.GetKeys(), candidate => !candidate.IsPrimaryKey());
        Assert.Equal(identity, key.Properties.Select(property => property.Name));
        Assert.Equal(SalesDbContext.DraftLineIdentityKey, key.GetName());

        foreach (var type in new[] { typeof(DraftOverDeliveryApprovalRow), typeof(DraftLineClosureRow), typeof(DraftRemainderChangeRow) })
        {
            var toLine = context.Model.FindEntityType(type)!.GetForeignKeys()
                .Where(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(DraftLineRow)).ToArray();
            Assert.Equal(2, toLine.Length);
            var position = Assert.Single(toLine, foreignKey => foreignKey.Properties.Count == 3);
            Assert.Equal(["CompanyId", "DraftId", "LineNo"], position.Properties.Select(property => property.Name));
            Assert.True(position.PrincipalKey.IsPrimaryKey());
            var bound = Assert.Single(toLine, foreignKey => foreignKey.Properties.Count == 4);
            Assert.Equal(identity, bound.Properties.Select(property => property.Name));
            Assert.Same(key, bound.PrincipalKey);
            Assert.All(toLine, foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
        }

        foreach (var type in new[] { typeof(DraftDeliverySliceRow), typeof(DraftDeliveryScheduleDateRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Null(entity.FindProperty("LineId"));
            Assert.True(Assert.Single(entity.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(DraftLineRow))
                .PrincipalKey.IsPrimaryKey());
        }
    }

    /// <summary>
    /// Review finding: Up refuses with 51234 (its own number) BEFORE any DDL while an existing row names another line's identity,
    /// and repairs nothing; Down is pure DDL (constraints and indexes only, no row loses meaning) and needs no guard.
    /// </summary>
    [Fact]
    public void The_line_identity_migration_guards_its_up_with_51234_first_and_its_down_is_pure_ddl()
    {
        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddDraftLineIdentityForeignKeys();
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.UpOperations[0]);
        Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Contains("THROW 51234,", guard.Sql, StringComparison.Ordinal);
        foreach (var table in new[] { "DraftOverDeliveryApprovals", "DraftLineClosures", "DraftRemainderChanges" })
        {
            Assert.Contains($"FROM [sales].[{table}] AS [c] WHERE NOT EXISTS", guard.Sql, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("UPDATE", guard.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", guard.Sql, StringComparison.OrdinalIgnoreCase);
        var unique = Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddUniqueConstraintOperation>());
        Assert.Equal((SalesDbContext.DraftLineIdentityKey, "DraftLines"), (unique.Name, unique.Table));
        var added = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddForeignKeyOperation>().ToArray();
        Assert.Equal(3, added.Length);
        Assert.All(added, foreignKey =>
        {
            Assert.Equal(["CompanyId", "DraftId", "LineNo", "LineId"], foreignKey.Columns);
            Assert.Equal(["CompanyId", "DraftId", "LineNo", "LineId"], foreignKey.PrincipalColumns ?? []);
            Assert.Equal("DraftLines", foreignKey.PrincipalTable);
            Assert.Equal(Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict, foreignKey.OnDelete);
        });
        Assert.DoesNotContain(migration.UpOperations, operation => operation is Microsoft.EntityFrameworkCore.Migrations.Operations.DropForeignKeyOperation);

        Assert.DoesNotContain(migration.DownOperations, operation => operation is Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation);
        Assert.Equal(3, migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropForeignKeyOperation>().Count());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropUniqueConstraintOperation>());
    }

    /// <summary>
    /// SAT-D252..SAT-D255 / API-11: the append-only source plan table carries the ONE equality as a check; Up has no SQL; Down is a
    /// forward fix that refuses with 51241 (its own number) FIRST while a recorded plan exists.
    /// </summary>
    [Fact]
    public void The_source_plan_migration_checks_the_one_equality_and_its_down_refuses_with_51241_first()
    {
        using var context = Context();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(DraftSourcePlanLineRow))!;
        Assert.Equal(("sales", "DraftSourcePlanLines"), (entity.GetSchema(), entity.GetTableName()));
        Assert.Equal(["CompanyId", "DraftId", "DraftVersion", "LineId"], entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var check = Assert.Single(entity.GetCheckConstraints());
        Assert.Equal(("CK_DraftSourcePlanLines_Identity", SalesDbContext.SourcePlanLineIdentityConstraint), (check.Name, check.Sql));
        Assert.Contains("[LockedQuantity] + [StockQuantity] + [ProductionQuantity] + [PurchasingQuantity] + [SubcontractQuantity] = [LineQuantity]",
            check.Sql, StringComparison.Ordinal);
        Assert.Contains("[LockedQuantity] < [LineQuantity]", check.Sql, StringComparison.Ordinal);

        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddDraftSourcePlanLines();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation);
        var table = Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>());
        Assert.Equal(SalesDbContext.SourcePlanLineIdentityConstraint, Assert.Single(table.CheckConstraints).Sql);
        Assert.All(table.ForeignKeys, foreignKey => Assert.Equal(Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict, foreignKey.OnDelete));
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Contains("IF EXISTS (SELECT 1 FROM [sales].[DraftSourcePlanLines])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51241,", guard.Sql, StringComparison.Ordinal);
        Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>(Assert.Single(migration.DownOperations.Skip(1)));
    }

    private static DraftLineClosureRow LineClosure() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), LineId = Guid.NewGuid(), LineNo = 1, BasisRevision = 0,
        Ordered = "100", ShippedAtClosure = "90", CancelledRemainder = "10", Band = 2, ClosedBy = "synthetic-closer",
        ClosedAtUtc = At, DraftVersion = 5, CorrelationId = "synthetic-correlation",
    };

    private static DraftOverDeliveryApprovalRow OverDeliveryApproval() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), LineId = Guid.NewGuid(), LineNo = 1, ApprovalNo = 1, BasisRevision = 0,
        Ordered = "100", ApprovedShipped = "102.5", ApprovedBy = "synthetic-approver", ApprovedAtUtc = At, DraftVersion = 5,
        CorrelationId = "synthetic-correlation",
    };

    private static DraftForceClosureRow ForceClosure() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), Revision = 0, ClosedBy = "synthetic-closer", ClosedAtUtc = At,
        Reason = "SENTETIK kalan kapatıldı", DraftVersion = 5, ImpactToken = new string('d', 64),
        ImpactJson = "{\"token\":\"x\",\"sources\":[]}", LinesJson = "[]", CorrelationId = "synthetic-correlation",
    };

    private static DraftCancellationRow Cancellation() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), Revision = 0, CancelledBy = "synthetic-canceller", CancelledAtUtc = At,
        Reason = "SENTETIK müşteri vazgeçti", DraftVersion = 5, ImpactToken = new string('b', 64),
        ImpactJson = "{\"token\":\"x\",\"sources\":[]}", CorrelationId = "synthetic-correlation",
    };

    private static DraftDeliveryScheduleRow Schedule() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), ScheduleNo = 1, RequestedDeliveryDate = new DateOnly(2026, 10, 1),
        ChangedBy = "synthetic-planner", ChangedAtUtc = At, DraftVersion = 2, CorrelationId = "synthetic-correlation",
    };

    private static DraftDeliveryScheduleDateRow ScheduleDate() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), ScheduleNo = 1, LineNo = 1, SliceNo = 1, SliceId = Guid.NewGuid(),
        RequestedDeliveryDate = new DateOnly(2026, 10, 2),
    };

    private static DraftAttemptRow Attempt() => new()
    {
        CompanyId = CompanyA, CommandName = SalesDraftCommands.CreateV1, IdempotencyKeyHash = new string('A', 64),
        RequestHash = new string('0', 64), ActorId = "synthetic-actor", Outcome = 1, DraftId = Guid.NewGuid(),
        CorrelationId = "synthetic-correlation", RecordedAtUtc = At,
    };

    /// <summary>A context of company A, or of no resolved company at all.</summary>
    private static SalesDbContext Context(bool resolved = true)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        var options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .AddInterceptors(new GuardPassedInterceptor())
            .Options;
        var context = new SalesDbContext(options, company);
        Companies[context] = company;
        return context;
    }

    /// <summary>
    /// Thrown the moment a save reaches the provider pipeline — AFTER the context's write guard passed (it runs before
    /// <c>base.SaveChanges</c>) and BEFORE any connection is opened. A guard refusal (InvalidOperationException) never
    /// reaches it, so the two theories above are told apart by type on every platform: a refusal that leaked through
    /// surfaces as this sentinel, never as the refusal's own type.
    /// </summary>
    private sealed class GuardPassedException() : Exception("the Sales write guard passed; the provider pipeline was reached");

    private sealed class GuardPassedInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => throw new GuardPassedException();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => throw new GuardPassedException();
    }
}
