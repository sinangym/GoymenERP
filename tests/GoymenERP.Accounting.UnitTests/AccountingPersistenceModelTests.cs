using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// The persistent model without a database: schema, tenant filters, the R4 unique identities,
/// the snapshot and the write guard, which refuses before any connection is opened. What SQL
/// Server itself enforces is proven by the relational acceptance tests, not here.
/// </summary>
public sealed class AccountingPersistenceModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private static readonly Type[] TenantRows =
    [
        typeof(PeriodCalendarRow), typeof(PeriodRow), typeof(JournalRow), typeof(JournalLineRow), typeof(DeliveryRow),
        typeof(AccountingPeriodClosureRow), typeof(AccountingPeriodReopeningRow),
        typeof(PostingExceptionRow), typeof(PendingValuationRow), typeof(AccountingCommandRow),
        typeof(NormalJournalDraftRow), typeof(NormalJournalDraftLineRow),
        typeof(AccountingPostingAccountMappingVersionRow), typeof(AccountingPostingAccountMappingEntryRow),
        typeof(AccountingSourceDocumentAdmissionRow), typeof(AccountingSourceDocumentVoucherDraftRow),
        typeof(AccountingSourceDocumentVoucherDraftLineRow),
        typeof(AccountingSourceInvoiceCancellationRow),
        typeof(ManufacturingCostDifferenceRow), typeof(CreditMovementProposalRow),
        typeof(InvoiceLineCogsProposalRow), typeof(InvoiceLineCogsRevisionRow),
        typeof(ManufacturingCostAttributionRow), typeof(ManufacturingCostAttributionShareRow), typeof(InvoiceCogsEconomicClaimRow),
        typeof(InvoiceCogsEconomicClaimSuccessionRow),
        typeof(PurchaseCostAttributionSourceRow), typeof(PurchaseCostAttributionCandidateRow), typeof(PurchaseCostAttributionApprovalRow),
    ];

    [Fact]
    public void Purchase_cost_candidates_keep_real_receipt_roots_and_one_approval_per_actual_invoice_line_source()
    {
        using var context = Context();
        var candidates = context.Model.FindEntityType(typeof(PurchaseCostAttributionCandidateRow))!;
        Assert.Equal(["CompanyId", "SourceEventId", "InvoiceLineId", "AcceptanceId", "MovementId", "MovementLineNumber"],
            candidates.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.False(Assert.Single(candidates.GetIndexes(), x => x.Properties.Select(p => p.Name)
            .SequenceEqual(["CompanyId", "MovementId", "MovementLineNumber"])).IsUnique);
        var approval = context.Model.FindEntityType(typeof(PurchaseCostAttributionApprovalRow))!;
        Assert.Equal(["CompanyId", "SourceEventId", "InvoiceLineId"], approval.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.True(approval.FindProperty("JournalId")!.IsNullable);
        Assert.All(approval.GetForeignKeys(), x => Assert.Equal(DeleteBehavior.Restrict, x.DeleteBehavior));
    }

    [Fact]
    public void Posted_cost_shares_allow_two_output_roots_on_one_physical_line_but_the_initial_economic_claim_is_unique()
    {
        using var context = Context();
        var shares = context.Model.FindEntityType(typeof(ManufacturingCostAttributionShareRow))!;
        Assert.Equal(["CompanyId", "SourceEventId", "OutputId", "MovementId", "MovementLineNumber"],
            shares.FindPrimaryKey()!.Properties.Select(x => x.Name));
        var lookup = Assert.Single(shares.GetIndexes(), x => x.Properties.Select(p => p.Name)
            .SequenceEqual(["CompanyId", "MovementId", "MovementLineNumber"]));
        Assert.False(lookup.IsUnique);
        var claims = context.Model.FindEntityType(typeof(InvoiceCogsEconomicClaimRow))!;
        Assert.Equal(["CompanyId", "MovementId", "MovementLineNumber"], claims.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.All(new[] { typeof(ManufacturingCostAttributionRow), typeof(ManufacturingCostAttributionShareRow), typeof(InvoiceCogsEconomicClaimRow) }, type =>
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.All(entity.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
            Assert.All(entity.GetIndexes().Where(x => x.IsUnique), index => Assert.Equal("CompanyId", index.Properties[0].Name));
        });
    }

    [Fact]
    public void Purchase_cost_attribution_migration_is_append_only_and_refuses_rollback_before_any_evidence_is_dropped()
    {
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddPostedPurchaseCostAttribution();
        Assert.Equal(3, migration.UpOperations.OfType<CreateTableOperation>().Count());
        var triggers = migration.UpOperations.OfType<SqlOperation>().ToArray();
        Assert.Equal(3, triggers.Length);
        Assert.All(triggers, sql =>
        {
            Assert.Contains("AFTER UPDATE, DELETE", sql.Sql);
            Assert.Contains("THROW 51805", sql.Sql);
            Assert.Contains("THROW 51806", sql.Sql);
        });
        var down = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51804", down.Sql);
        Assert.Contains("PurchaseCostAttributionSources", down.Sql);
        Assert.Contains("PurchaseCostAttributionCandidates", down.Sql);
        Assert.Contains("PurchaseCostAttributionApprovals", down.Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Equal(3, migration.DownOperations.OfType<DropTableOperation>().Count());
    }

    [Fact]
    public void Every_accounting_table_is_in_its_schema_and_filtered_by_the_active_company()
    {
        using var context = Context();
        foreach (var type in TenantRows)
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(AccountingDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.NotNull(entity.FindProperty("CompanyId"));
        }

        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
        Assert.Single(audit.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_r4_identities_are_unique_keys_of_the_company()
    {
        using var context = Context();
        var deliveries = context.Model.FindEntityType(typeof(DeliveryRow))!;
        Assert.Equal(["CompanyId", "Consumer", "MessageId"],
            deliveries.FindPrimaryKey()!.Properties.Select(property => property.Name));

        var journals = context.Model.FindEntityType(typeof(JournalRow))!;
        var original = Assert.Single(journals.GetIndexes(), index => index.GetDatabaseName() == "UX_Journals_Original");
        var reversal = Assert.Single(journals.GetIndexes(), index => index.GetDatabaseName() == "UX_Journals_Reversal");
        var correction = Assert.Single(journals.GetIndexes(), index => index.GetDatabaseName() == "UX_Journals_Correction");
        Assert.True(original.IsUnique && reversal.IsUnique && correction.IsUnique);
        Assert.Equal(["CompanyId", "SourceId", "PostingPurpose"], original.Properties.Select(property => property.Name));
        Assert.Equal("[Kind] = 1", original.GetFilter());
        Assert.Equal(["CompanyId", "LinkedJournalId"], reversal.Properties.Select(property => property.Name));
        Assert.Equal("[Kind] = 2", reversal.GetFilter());
        Assert.Equal(["CompanyId", "CorrectionId"], correction.Properties.Select(property => property.Name));
        Assert.Equal("[CorrectionId] IS NOT NULL", correction.GetFilter());
        // No group-wide identity: every unique identity starts with the company.
        Assert.All(journals.GetIndexes().Where(index => index.IsUnique),
            index => Assert.Equal("CompanyId", index.Properties[0].Name));
    }

    [Fact]
    public void Identity_texts_compare_exactly_and_the_period_is_a_concurrency_token()
    {
        using var context = Context();
        // Collation is design-time configuration; the read-optimized runtime model does not keep it.
        var designModel = context.GetService<IDesignTimeModel>().Model;
        foreach (var (type, property) in new[]
                 {
                     (typeof(JournalRow), nameof(JournalRow.PostingPurpose)),
                     (typeof(DeliveryRow), nameof(DeliveryRow.Consumer)),
                     (typeof(DeliveryRow), nameof(DeliveryRow.MessageId)),
                     (typeof(PostingExceptionRow), nameof(PostingExceptionRow.PostingPurpose)),
                     (typeof(PendingValuationRow), nameof(PendingValuationRow.PostingPurpose)),
                     (typeof(NormalJournalDraftRow), nameof(NormalJournalDraftRow.Consumer)),
                     (typeof(NormalJournalDraftRow), nameof(NormalJournalDraftRow.MessageId)),
                     (typeof(NormalJournalDraftRow), nameof(NormalJournalDraftRow.PostingPurpose)),
                     (typeof(NormalJournalDraftLineRow), nameof(NormalJournalDraftLineRow.MappingKey)),
                 })
        {
            Assert.Equal(AccountingDbContext.IdentityCollation,
                designModel.FindEntityType(type)!.FindProperty(property)!.GetCollation());
        }

        var periods = context.Model.FindEntityType(typeof(PeriodRow))!;
        Assert.True(periods.FindProperty(nameof(PeriodRow.Version))!.IsConcurrencyToken);
        Assert.True(periods.FindProperty(nameof(PeriodRow.RowVersion))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(PeriodCalendarRow))!.FindProperty(nameof(PeriodCalendarRow.Version))!
            .IsConcurrencyToken);
        var drafts = context.Model.FindEntityType(typeof(NormalJournalDraftRow))!;
        Assert.True(drafts.FindProperty(nameof(NormalJournalDraftRow.Version))!.IsConcurrencyToken);
        Assert.True(drafts.FindProperty(nameof(NormalJournalDraftRow.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void Journals_reference_their_period_and_linked_journal_by_company_and_id()
    {
        using var context = Context();
        var journals = context.Model.FindEntityType(typeof(JournalRow))!;
        Assert.Contains(journals.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PeriodRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PeriodId"]));
        Assert.Contains(journals.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(JournalRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LinkedJournalId"]));
        var lines = context.Model.FindEntityType(typeof(JournalLineRow))!;
        Assert.Contains(lines.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(JournalRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "JournalId"]));
        var drafts = context.Model.FindEntityType(typeof(NormalJournalDraftRow))!;
        Assert.Contains(drafts.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(JournalRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PostedJournalId"]));
        var draftLines = context.Model.FindEntityType(typeof(NormalJournalDraftLineRow))!;
        Assert.Contains(draftLines.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(NormalJournalDraftRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "DraftId"]));
    }

    [Fact]
    public void The_migration_is_discoverable_and_the_snapshot_matches_the_model()
    {
        using var context = Context();
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_InitialAccounting", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddDeliveryRequestHash", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddPeriodApproval", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddNormalJournalDrafts", StringComparison.Ordinal));
        Assert.Equal(["20261117000534_AddManufacturingCostDifferenceProposals", "20261117005516_AddCreditMovementProposals",
                "20261117095844_AddInvoiceLineCogsCorrections", "20261117102540_AddFinanceJournalSources",
                "20261117123611_AddSourceVoucherLifecycle", "20261117154311_AddPeriodClosureCarryAndReopening",
                "20261117171929_WidenPostingMappingsToCogs", "20261117175214_AddInventoryCostProposals",
                "20261117190022_AddPostedManufacturingCostAttribution", "20261117194231_AddInventoryCostComponentRounding",
                "20261117205436_AddPostedPurchaseCostAttribution", "20261117213704_AllowCashExchangeJournalSources",
                "20261117223234_AddInventoryCostComponentCancellations", "20261117232712_AddInvoiceCogsClaimSuccessions"],
            context.Database.GetMigrations().TakeLast(14));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Cost_claim_successions_keep_the_original_anchor_and_append_one_company_scoped_generation_with_forward_only_guards()
    {
        using var context = Context();
        var row = context.Model.FindEntityType(typeof(InvoiceCogsEconomicClaimSuccessionRow))!;
        Assert.Equal(["CompanyId", "MovementId", "MovementLineNumber", "Generation"], row.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Contains(row.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual(["CompanyId", "SalesEventId", "MovementId", "MovementLineNumber"]));
        Assert.All(row.GetForeignKeys(), x => Assert.Equal(DeleteBehavior.Restrict, x.DeleteBehavior));
        Assert.Contains(row.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(InvoiceCogsEconomicClaimRow));
        Assert.False(row.FindProperty("SuccessionJson")!.IsNullable); Assert.False(row.FindProperty("PreviousClaimSha256")!.IsNullable);
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddInvoiceCogsClaimSuccessions();
        Assert.Contains("THROW 51840", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        var guard = Assert.Single(migration.UpOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51842", guard.Sql); Assert.Contains("THROW 51843", guard.Sql);
        Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
    }

    [Fact]
    public void Posted_manufacturing_attribution_is_append_only_and_its_Down_protects_economic_claims_before_any_drop()
    {
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddPostedManufacturingCostAttribution();
        var down = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51800", down.Sql);
        Assert.Contains("[InventoryCostProposals] WHERE [Status] = 5", down.Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        var guards = migration.UpOperations.OfType<SqlOperation>().ToArray();
        Assert.Equal(3, guards.Length);
        Assert.All(guards, guard => { Assert.Contains("THROW 51801", guard.Sql); Assert.Contains("THROW 51802", guard.Sql); });
    }

    [Fact]
    public void Inventory_cost_rounding_widens_only_the_approved_zero_state_and_protects_exact_evidence_before_rollback()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var row = model.FindEntityType(typeof(AccountingInventoryCostProposalRow))!;
        Assert.Equal(AccountingDbContext.InventoryCostProposalStateCheck,
            row.GetCheckConstraints().Single(x => x.Name == "CK_InventoryCostProposals_State").Sql);
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddInventoryCostComponentRounding();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Collection(migration.UpOperations,
            op => Assert.IsType<DropCheckConstraintOperation>(op), op => Assert.IsType<AddCheckConstraintOperation>(op));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51803", guard.Sql);
        Assert.Contains("$.preparation.rounding", guard.Sql);
        Assert.Contains("$.component.recordedRounding", guard.Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    [Fact]
    public void Posting_account_mappings_keep_one_active_version_maker_checker_and_a_forward_fix_down()
    {
        using var context = Context();
        var versions = context.Model.FindEntityType(typeof(AccountingPostingAccountMappingVersionRow))!;
        Assert.Equal(["CompanyId", "MappingVersion"], versions.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var active = Assert.Single(versions.GetIndexes(),
            index => index.GetDatabaseName() == "UX_PostingAccountMappingVersions_Active");
        Assert.True(active.IsUnique);
        Assert.Equal(["CompanyId"], active.Properties.Select(property => property.Name));
        Assert.Equal("[Status] = 2", active.GetFilter());
        Assert.True(versions.FindProperty(nameof(AccountingPostingAccountMappingVersionRow.Version))!.IsConcurrencyToken);
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var checks = designModel.FindEntityType(typeof(AccountingPostingAccountMappingVersionRow))!.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal(AccountingDbContext.PostingAccountMappingVersionStatusCheck,
            checks["CK_PostingAccountMappingVersions_Status"]);
        Assert.Equal(AccountingDbContext.PostingAccountMappingVersionMakerCheckerCheck,
            checks["CK_PostingAccountMappingVersions_MakerChecker"]);

        var rows = context.Model.FindEntityType(typeof(AccountingPostingAccountMappingEntryRow))!;
        var selector = Assert.Single(rows.GetIndexes(),
            index => index.GetDatabaseName() == "UX_PostingAccountMappingRows_Selector");
        Assert.True(selector.IsUnique);
        Assert.Null(selector.GetFilter());
        Assert.Equal(["CompanyId", "MappingVersion", "Purpose", "MappingKey", "ItemType", "LinePurpose", "IsExport", "VatRateCode"],
            selector.Properties.Select(property => property.Name));
        var rowChecks = designModel.FindEntityType(typeof(AccountingPostingAccountMappingEntryRow))!.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal(AccountingDbContext.PostingAccountMappingRowKeyCheck, rowChecks["CK_PostingAccountMappingRows_Key"]);
        Assert.Equal(AccountingDbContext.PostingAccountMappingRowVatRateCheck, rowChecks["CK_PostingAccountMappingRows_VatRate"]);
        foreach (var property in new[] { "ItemType", "LinePurpose", "IsExport", "VatRateCode" })
        {
            Assert.True(rows.FindProperty(property)!.IsNullable);
        }

        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddPostingAccountMappings();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51212", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }

    [Fact]
    public void Inventory_cost_sources_and_proposal_versions_have_company_foreign_keys_unique_economic_identity_and_append_only_guards()
    {
        using var context = Context();
        var sources = context.Model.FindEntityType(typeof(AccountingInventoryCostSourceRow))!;
        var proposals = context.Model.FindEntityType(typeof(AccountingInventoryCostProposalRow))!;
        Assert.Equal(["CompanyId", "EventId"], sources.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Equal(["CompanyId", "Id", "Version"], proposals.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Contains(sources.GetIndexes(), x => x.IsUnique && x.Properties.Select(y => y.Name)
            .SequenceEqual(["CompanyId", "Kind", "InvoiceId", "InvoiceVersion"]));
        Assert.Contains(proposals.GetIndexes(), x => x.IsUnique && x.Properties.Select(y => y.Name)
            .SequenceEqual(["CompanyId", "EventId", "ComponentKey", "Version"]));
        Assert.All(proposals.GetForeignKeys(), fk => Assert.Equal("CompanyId", fk.Properties[0].Name));
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddInventoryCostProposals();
        Assert.Equal(2, migration.UpOperations.OfType<CreateTableOperation>().Count());
        var triggers = migration.UpOperations.OfType<SqlOperation>().ToArray();
        Assert.Equal(2, triggers.Length);
        Assert.Contains("THROW 51931", triggers[0].Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51932", triggers[1].Sql, StringComparison.Ordinal);
        Assert.All(triggers, x => Assert.Contains("AFTER UPDATE, DELETE", x.Sql, StringComparison.Ordinal));
        Assert.Contains("THROW 51930", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, x => x is SqlOperation);
    }

    [Fact]
    public void Cogs_mapping_constraint_only_adds_explicit_class_keys_and_refuses_an_unsafe_rollback()
    {
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.WidenPostingMappingsToCogs();
        Assert.Equal(2, migration.UpOperations.Count);
        Assert.IsType<DropCheckConstraintOperation>(migration.UpOperations[0]);
        var added = Assert.IsType<AddCheckConstraintOperation>(migration.UpOperations[1]);
        Assert.Equal("CK_PostingAccountMappingRows_Key", added.Name);
        Assert.Equal(AccountingDbContext.PostingAccountMappingRowKeyCheck, added.Sql);
        Assert.Contains("N'cost-of-goods-sold'", added.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51929", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [Purpose] = N'cost-of-goods-sold'", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.DoesNotContain("cost-of-goods-sold", Assert.IsType<AddCheckConstraintOperation>(migration.DownOperations[2]).Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Source_document_admissions_keep_one_row_per_key_and_document_and_a_forward_fix_down()
    {
        using var context = Context();
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var admissions = context.Model.FindEntityType(typeof(AccountingSourceDocumentAdmissionRow))!;
        Assert.Equal(["CompanyId", "SourceDocumentId"], admissions.FindPrimaryKey()!.Properties.Select(property => property.Name));
        foreach (var name in new[] { "UX_SourceDocumentAdmissions_Key", "UX_SourceDocumentAdmissions_Document" })
        {
            Assert.True(Assert.Single(admissions.GetIndexes(), index => index.GetDatabaseName() == name).IsUnique);
        }

        Assert.True(admissions.FindProperty(nameof(AccountingSourceDocumentAdmissionRow.Version))!.IsConcurrencyToken);
        var checks = designModel.FindEntityType(typeof(AccountingSourceDocumentAdmissionRow))!.GetCheckConstraints()
            .ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Contains(AccountingDbContext.SourceDocumentAdmissionStatusCheck, checks.Values);

        var drafts = context.Model.FindEntityType(typeof(AccountingSourceDocumentVoucherDraftRow))!;
        Assert.Equal(["CompanyId", "Id"], drafts.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var lines = context.Model.FindEntityType(typeof(AccountingSourceDocumentVoucherDraftLineRow))!;
        Assert.Equal(["CompanyId", "DraftId", "LineNumber"], lines.FindPrimaryKey()!.Properties.Select(property => property.Name));

        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddSourceDocumentAdmissions();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51240", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }

    [Fact]
    public void Source_voucher_lifecycle_preserves_company_keys_frozen_source_and_real_journal_maker_checker()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var vouchers = model.FindEntityType(typeof(AccountingSourceDocumentVoucherDraftRow))!;
        Assert.Contains(vouchers.GetCheckConstraints(), check => check.Name == "CK_SourceDocumentVoucherDrafts_Status"
            && check.Sql == AccountingDbContext.SourceVoucherLifecycleStatusCheck);
        Assert.Contains(vouchers.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(JournalRow)
            && fk.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "JournalId"]));
        Assert.Contains(vouchers.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(PeriodRow)
            && fk.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "PeriodId"]));
        Assert.True(vouchers.FindProperty(nameof(AccountingSourceDocumentVoucherDraftRow.OfficialInvoiceNo))!.IsNullable);
        var cancellations = model.FindEntityType(typeof(AccountingSourceInvoiceCancellationRow))!;
        Assert.Equal(["CompanyId", "CancellationId"], cancellations.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Contains(cancellations.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "SourceDocumentId"]));
        Assert.Contains(cancellations.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "DocumentKind", "InvoiceId", "FinalizedVersion"]));
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddSourceVoucherLifecycle();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51938", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[OfficialInvoiceNo] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }

    [Theory]
    [InlineData("edit-cancellation")]
    [InlineData("edit-voucher-amount")]
    [InlineData("rewrite-posted-voucher")]
    [InlineData("change-posted-approval")]
    [InlineData("change-posted-journal")]
    [InlineData("skip-posted-version")]
    public void Source_voucher_financial_facts_and_cancellation_receipts_are_immutable_before_database_access(string fault)
    {
        using var context = Context();
        if (fault == "edit-cancellation")
        {
            var row = new AccountingSourceInvoiceCancellationRow { CompanyId = CompanyA, CancellationId = Guid.NewGuid(), Reason = "SENTETIK" };
            context.Attach(row); row.Reason = "SENTETIK changed";
        }
        else
        {
            var row = new AccountingSourceDocumentVoucherDraftRow { CompanyId = CompanyA, Id = Guid.NewGuid(),
                SourceActorId = Guid.NewGuid(), Status = fault == "edit-voucher-amount" ? 1 : 2, Version = 1 };
            context.Attach(row); row.Version = 2;
            if (fault == "edit-voucher-amount") { row.Status = 3; row.FunctionalPayable = 100; }
            else if (fault == "change-posted-approval") row.ApprovedBy = Guid.NewGuid();
            else if (fault == "change-posted-journal") row.JournalId = Guid.NewGuid();
            else if (fault == "skip-posted-version") row.Version = 3;
            else row.Status = 3;
        }
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void Every_delivery_keeps_a_required_64_hex_content_hash()
    {
        using var context = Context();
        // Check constraints and unicode are design-time configuration, like collation.
        var deliveries = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(DeliveryRow))!;
        var hash = deliveries.FindProperty(nameof(DeliveryRow.RequestHash))!;
        // Required: no delivery row may replay without a content check (#307 review).
        Assert.False(hash.IsNullable);
        Assert.Equal(64, hash.GetMaxLength());
        Assert.False(hash.IsUnicode());
        Assert.Contains(deliveries.GetCheckConstraints(), check => check.Name == "CK_Deliveries_RequestHash");
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("no-company-context")]
    [InlineData("empty-company")]
    public void A_row_of_another_company_or_without_a_company_is_refused_before_the_database(string fault)
    {
        using var context = Context(resolved: fault != "no-company-context");
        context.Periods.Add(new PeriodRow
        {
            Id = Guid.NewGuid(),
            CompanyId = fault switch { "other-company" => CompanyB, "empty-company" => Guid.Empty, _ => CompanyA },
            StartsOn = new DateOnly(2026, 9, 1),
            EndsOn = new DateOnly(2026, 9, 30),
            Status = 1,
            Version = 1,
            CreatedAtUtc = At,
            UpdatedAtUtc = At,
        });

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Theory]
    [InlineData("delete-journal")]
    [InlineData("edit-journal")]
    [InlineData("edit-line")]
    [InlineData("edit-delivery")]
    [InlineData("edit-period-dates")]
    [InlineData("edit-period-preparer")]
    [InlineData("edit-command")]
    [InlineData("delete-command")]
    [InlineData("edit-normal-draft-source")]
    [InlineData("edit-normal-draft-line")]
    [InlineData("delete-normal-draft")]
    [InlineData("move-exception-to-another-company")]
    public void Stored_accounting_is_never_deleted_and_only_its_allowed_fields_change(string fault)
    {
        using var context = Context();
        switch (fault)
        {
            case "delete-journal":
                context.Journals.Remove(Attach(context, Journal()));
                break;
            case "edit-journal":
                Attach(context, Journal()).PostingRuleVersion = "synthetic-rule-changed";
                break;
            case "edit-line":
                Attach(context, Line()).Debit = 2m;
                break;
            case "edit-delivery":
                Attach(context, Delivery()).OutcomeKind = 3;
                break;
            case "edit-period-dates":
                Attach(context, Period()).EndsOn = new DateOnly(2026, 10, 31);
                break;
            case "edit-period-preparer":
                // Ö4: who prepared a period is its record; only the approval and the close are added later.
                Attach(context, Period()).PreparedByUserId = Guid.NewGuid();
                break;
            case "edit-command":
                Attach(context, Command()).ResultJson = "{\"outcome\":\"Refused\"}";
                break;
            case "delete-command":
                context.Commands.Remove(Attach(context, Command()));
                break;
            case "edit-normal-draft-source":
                Attach(context, NormalDraft()).SourceId = Guid.NewGuid();
                break;
            case "edit-normal-draft-line":
                Attach(context, NormalDraftLine()).Amount = 2m;
                break;
            case "delete-normal-draft":
                context.NormalJournalDrafts.Remove(Attach(context, NormalDraft()));
                break;
            default:
                Attach(context, QueueEntry()).CompanyId = CompanyB;
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    private static T Attach<T>(AccountingDbContext context, T row) where T : class
    {
        context.Attach(row);
        return row;
    }

    private static PeriodRow Period() => new PeriodRow
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, StartsOn = new DateOnly(2026, 9, 1), EndsOn = new DateOnly(2026, 9, 30),
        Status = 1, PreparedByUserId = Guid.NewGuid(), PreparedAtUtc = At, ApprovedByUserId = Guid.NewGuid(), ApprovedAtUtc = At,
        Version = 1, CreatedAtUtc = At, UpdatedAtUtc = At,
    };

    private static AccountingCommandRow Command() => new AccountingCommandRow
    {
        CompanyId = CompanyA, CommandName = "accounting.period.propose.v1", IdempotencyKeyHash = new string('a', 64),
        ActorId = "synthetic-actor", PayloadHash = new string('b', 64),
        ResultJson = "{\"outcome\":\"Recorded\"}", CorrelationId = "synthetic-correlation", RecordedAtUtc = At,
    };

    private static JournalRow Journal() => new JournalRow
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, Kind = 1, SourceId = Guid.NewGuid(), PostingPurpose = "synthetic-purpose",
        PostingRuleVersion = "synthetic-rule-1", CurrencyId = Guid.NewGuid(), TransactionDate = new DateOnly(2026, 9, 14),
        AccountingDate = new DateOnly(2026, 9, 15), RecordedAtUtc = At, PeriodId = Guid.NewGuid(), StoredAtUtc = At,
        StoredBy = "synthetic-actor", CorrelationId = "synthetic-correlation",
    };

    private static JournalLineRow Line() => new JournalLineRow
    {
        CompanyId = CompanyA, JournalId = Guid.NewGuid(), LineNumber = 1, AccountId = Guid.NewGuid(), Debit = 1m, Credit = 0m,
    };

    private static DeliveryRow Delivery() => new DeliveryRow
    {
        CompanyId = CompanyA, Consumer = "synthetic-consumer", MessageId = "m-1", OutcomeKind = 1, JournalId = Guid.NewGuid(),
        RecordedAtUtc = At, CorrelationId = "synthetic-correlation",
    };

    private static PostingExceptionRow QueueEntry() => new PostingExceptionRow
    {
        CompanyId = CompanyA, SourceId = Guid.NewGuid(), PostingPurpose = "synthetic-purpose", Status = 1,
        PostingRuleVersion = "synthetic-rule-1", MissingMappingKeysJson = "[\"k\"]", FirstMessageId = "m-1", Version = 1,
        UpdatedAtUtc = At,
    };

    private static NormalJournalDraftRow NormalDraft() => new NormalJournalDraftRow
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, Status = 1, Version = 1, ContentHash = new string('a', 64),
        PreparedByUserId = Guid.NewGuid(), PreparedAtUtc = At, Consumer = "synthetic-consumer", MessageId = "draft-1",
        SourceId = Guid.NewGuid(), PostingPurpose = "synthetic-purpose", PostingRuleVersion = "synthetic-rule-1",
        CurrencyId = Guid.NewGuid(), TransactionDate = new DateOnly(2026, 9, 14), AccountingDate = new DateOnly(2026, 9, 15),
        RecordedAtUtc = At, AccountPlanSourceVersion = "synthetic-plan-1", AccountPlanSourceSha256 = new string('b', 64),
        AccountPlanApprovedByUserId = Guid.NewGuid(), AccountPlanApprovedAtUtc = At,
        PostingRuleApprovedByUserId = Guid.NewGuid(), PostingRuleApprovedAtUtc = At, CreatedAtUtc = At, UpdatedAtUtc = At,
    };

    private static NormalJournalDraftLineRow NormalDraftLine() => new NormalJournalDraftLineRow
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), LineNumber = 1, MappingKey = "synthetic-debit",
        Side = 1, AccountId = Guid.NewGuid(), Amount = 1m,
    };

    /// <summary>A context of company A, or of no resolved company at all.</summary>
    private static AccountingDbContext Context(bool resolved = true)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new AccountingDbContext(options, company);
    }
}
