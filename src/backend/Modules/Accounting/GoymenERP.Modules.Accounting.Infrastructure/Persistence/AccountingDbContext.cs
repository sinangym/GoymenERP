using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>
/// Accounting's own tables in the <c>accounting</c> schema (plan §7.6, R4, ADR-002).
/// <list type="bullet">
/// <item>Every row is a tenant row: a global query filter on the server-resolved company, and a
/// write guard that refuses any row of another company, any company change and any write without
/// a resolved company.</item>
/// <item>R4 persistent identities: the inbox key (company, consumer, message id); one ORIGINAL
/// journal per (company, source, purpose); one reversal per journal; one journal per correction
/// id. Each is a unique key or filtered unique index, so it holds under a race.</item>
/// <item>A journal, its lines, a delivery and a command record are append-only; a period changes
/// only its approval and close traces and version; a queue entry only its current facts and version. Nothing is deleted.</item>
/// <item>Identity texts (posting purpose, consumer, message id) use a binary collation, the same
/// exact comparison the domain uses.</item>
/// </list>
/// The audit sidecar is Audit's table, written in the same save and filtered the same way.
/// </summary>
public sealed class AccountingDbContext(DbContextOptions<AccountingDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "accounting";

    /// <summary>Binary collation: equality is exact, as the domain's ordinal identity comparison.</summary>
    public const string IdentityCollation = "Latin1_General_100_BIN2";

    internal DbSet<PeriodCalendarRow> PeriodCalendars => Set<PeriodCalendarRow>();

    internal DbSet<PeriodRow> Periods => Set<PeriodRow>();

    internal DbSet<JournalRow> Journals => Set<JournalRow>();

    internal DbSet<JournalLineRow> JournalLines => Set<JournalLineRow>();

    internal DbSet<DeliveryRow> Deliveries => Set<DeliveryRow>();

    internal DbSet<PostingExceptionRow> PostingExceptions => Set<PostingExceptionRow>();

    internal DbSet<PendingValuationRow> PendingValuations => Set<PendingValuationRow>();

    internal DbSet<AccountingCommandRow> Commands => Set<AccountingCommandRow>();

    internal DbSet<NormalJournalDraftRow> NormalJournalDrafts => Set<NormalJournalDraftRow>();

    internal DbSet<NormalJournalDraftLineRow> NormalJournalDraftLines => Set<NormalJournalDraftLineRow>();

    internal DbSet<AccountingJournalReversalProposalRow> JournalReversalProposals =>
        Set<AccountingJournalReversalProposalRow>();

    internal DbSet<ApprovedAccountingSourceCatalogRow> ApprovedSourceCatalogs =>
        Set<ApprovedAccountingSourceCatalogRow>();

    internal DbSet<AccountingPostingAccountMappingVersionRow> PostingAccountMappingVersions =>
        Set<AccountingPostingAccountMappingVersionRow>();

    internal DbSet<AccountingPostingAccountMappingEntryRow> PostingAccountMappingRows =>
        Set<AccountingPostingAccountMappingEntryRow>();

    internal DbSet<AccountingSourceDocumentAdmissionRow> SourceDocumentAdmissions =>
        Set<AccountingSourceDocumentAdmissionRow>();

    internal DbSet<AccountingSourceDocumentVoucherDraftRow> SourceDocumentVoucherDrafts =>
        Set<AccountingSourceDocumentVoucherDraftRow>();

    internal DbSet<AccountingSourceDocumentVoucherDraftLineRow> SourceDocumentVoucherDraftLines =>
        Set<AccountingSourceDocumentVoucherDraftLineRow>();

    /// <summary>
    /// B-04b: the facts follow the status. Waiting (1) and refused (3) carry a reason and no draft; admitted (2) carries
    /// the draft and the reason "admitted".
    /// </summary>
    internal const string SourceDocumentAdmissionStatusCheck =
        "([Status] = 1 AND [VoucherDraftId] IS NULL AND LEN([ReasonCode]) > 0) "
        + "OR ([Status] = 2 AND [VoucherDraftId] IS NOT NULL AND [ReasonCode] = 'admitted') "
        + "OR ([Status] = 3 AND [VoucherDraftId] IS NULL AND LEN([ReasonCode]) > 0 AND [ReasonCode] <> 'admitted')";

    /// <summary>
    /// B-04a: the facts follow the status. Prepared (1) has no activation; Active (2) and Superseded (3) carry
    /// the activator and instant.
    /// </summary>
    internal const string PostingAccountMappingVersionStatusCheck =
        "([Status] = 1 AND [ActivatedByUserId] IS NULL AND [ActivatedAtUtc] IS NULL) OR "
        + "([Status] IN (2, 3) AND [ActivatedByUserId] IS NOT NULL "
        + "AND [ActivatedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [ActivatedAtUtc] IS NOT NULL)";

    /// <summary>B-04a maker-checker: the activator is never the preparer.</summary>
    internal const string PostingAccountMappingVersionMakerCheckerCheck =
        "[ActivatedByUserId] IS NULL OR [ActivatedByUserId] <> [PreparedByUserId]";

    /// <summary>B-04a (Sinan #66 6707864938): only a VAT key may name an optional VAT rate sub-account selector.</summary>
    internal const string PostingAccountMappingRowVatRateCheck =
        "[VatRateCode] IS NULL OR (LEN([VatRateCode]) > 0 AND [MappingKey] IN (N'vat-output', N'vat-input'))";

    /// <summary>B-04a: the purposes and the mapping keys each purpose may carry (no account number).</summary>
    internal const string PostingAccountMappingRowKeyCheck =
        "([Purpose] = N'sales-invoice' AND [MappingKey] IN (N'receivable', N'revenue', N'vat-output', N'withholding-receivable')) OR "
        + "([Purpose] = N'purchase-invoice' AND [MappingKey] IN "
        + "(N'payable', N'inventory', N'vat-input', N'withholding-payable', N'expense'))";

    /// <summary>
    /// ACC-V0 (Ö4): the facts follow the status exactly. Proposed (3): the preparer only; Open (1): the
    /// preparer and an approval by SOMEONE ELSE; Closed (2): both plus the close.
    /// </summary>
    internal const string PeriodStatusCheck =
        "([Status] = 3 AND [ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosedAtUtc] IS NULL) OR "
        + "([Status] = 1 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedByUserId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [ApprovedByUserId] <> [PreparedByUserId] AND [ApprovedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NULL AND [ClosedAtUtc] IS NULL) OR "
        + "([Status] = 2 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedByUserId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [ApprovedByUserId] <> [PreparedByUserId] AND [ApprovedAtUtc] IS NOT NULL "
        + "AND [ClosedByUserId] IS NOT NULL AND [ClosedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [ClosedAtUtc] IS NOT NULL)";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        ConfigurePeriodCalendars(modelBuilder);
        ConfigurePeriods(modelBuilder);
        ConfigureJournals(modelBuilder);
        ConfigureJournalLines(modelBuilder);
        ConfigureDeliveries(modelBuilder);
        ConfigurePostingExceptions(modelBuilder);
        ConfigurePendingValuations(modelBuilder);
        ConfigureCommands(modelBuilder);
        ConfigureNormalJournalDrafts(modelBuilder);
        ConfigureNormalJournalDraftLines(modelBuilder);
        ConfigureJournalReversalProposals(modelBuilder);
        ConfigureApprovedSourceCatalogs(modelBuilder);
        ConfigurePostingAccountMappingVersions(modelBuilder);
        ConfigurePostingAccountMappingRows(modelBuilder);
        ConfigureSourceDocumentAdmissions(modelBuilder);
        ConfigureSourceDocumentVoucherDrafts(modelBuilder);
        ConfigureSourceDocumentVoucherDraftLines(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ConfigurePeriodCalendars(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PeriodCalendarRow>();
        entity.ToTable("PeriodCalendars", Schema, table =>
        {
            table.HasCheckConstraint("CK_PeriodCalendars_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PeriodCalendars_Version", "[Version] >= 1");
        });
        entity.HasKey(x => x.CompanyId);
        entity.Property(x => x.CompanyId).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigurePeriods(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PeriodRow>();
        entity.ToTable("Periods", Schema, table =>
        {
            table.HasCheckConstraint("CK_Periods_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Periods_Dates", "[EndsOn] >= [StartsOn]");
            table.HasCheckConstraint("CK_Periods_Prepared", "[PreparedByUserId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Periods_Status", PeriodStatusCheck);
            table.HasCheckConstraint("CK_Periods_Version", "[Version] >= 1");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.StartsOn).HasColumnType("date");
        entity.Property(x => x.EndsOn).HasColumnType("date");
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.CompanyId, x.StartsOn, x.EndsOn });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureJournals(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<JournalRow>();
        entity.ToTable("Journals", Schema, table =>
        {
            table.HasCheckConstraint("CK_Journals_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PeriodId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Journals_Kind",
                "([Kind] = 1 AND [LinkedJournalId] IS NULL AND [CorrectionId] IS NULL) OR "
                + "([Kind] = 2 AND [LinkedJournalId] IS NOT NULL AND [CorrectionId] IS NULL) OR "
                + "([Kind] = 3 AND [LinkedJournalId] IS NOT NULL AND [CorrectionId] IS NOT NULL "
                + "AND [CorrectionId] <> '00000000-0000-0000-0000-000000000000')");
            table.HasCheckConstraint("CK_Journals_Text",
                "LEN([PostingPurpose]) > 0 AND LEN([PostingRuleVersion]) > 0 AND LEN([StoredBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.PostingPurpose).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.PostingRuleVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.TransactionDate).HasColumnType("date");
        entity.Property(x => x.AccountingDate).HasColumnType("date");
        entity.Property(x => x.ValueDate).HasColumnType("date");
        entity.Property(x => x.StoredBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.SourceId, x.PostingPurpose })
            .IsUnique()
            .HasFilter("[Kind] = 1")
            .HasDatabaseName("UX_Journals_Original");
        entity.HasIndex(x => new { x.CompanyId, x.LinkedJournalId })
            .IsUnique()
            .HasFilter("[Kind] = 2")
            .HasDatabaseName("UX_Journals_Reversal");
        entity.HasIndex(x => new { x.CompanyId, x.CorrectionId })
            .IsUnique()
            .HasFilter("[CorrectionId] IS NOT NULL")
            .HasDatabaseName("UX_Journals_Correction");
        entity.HasIndex(x => new { x.CompanyId, x.PeriodId });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PeriodRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<JournalRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LinkedJournalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureJournalLines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<JournalLineRow>();
        entity.ToTable("JournalLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_JournalLines_LineNumber", "[LineNumber] >= 1");
            table.HasCheckConstraint("CK_JournalLines_Account", "[AccountId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_JournalLines_OneSide",
                "([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)");
        });
        entity.HasKey(x => new { x.CompanyId, x.JournalId, x.LineNumber });
        entity.Property(x => x.Debit).HasPrecision(38, 18);
        entity.Property(x => x.Credit).HasPrecision(38, 18);
        entity.HasOne<JournalRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.JournalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureDeliveries(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DeliveryRow>();
        entity.ToTable("Deliveries", Schema, table =>
        {
            table.HasCheckConstraint("CK_Deliveries_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Consumer]) > 0 AND LEN([MessageId]) > 0 "
                + "AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_Deliveries_Outcome",
                "([OutcomeKind] IN (1, 3, 6) AND [JournalId] IS NOT NULL AND [QueueEntryJson] IS NULL) OR "
                + "([OutcomeKind] IN (4, 5) AND [JournalId] IS NULL AND [QueueEntryJson] IS NOT NULL AND ISJSON([QueueEntryJson]) = 1)");
            table.HasCheckConstraint("CK_Deliveries_RequestHash", "LEN([RequestHash]) = 64");
        });
        entity.HasKey(x => new { x.CompanyId, x.Consumer, x.MessageId });
        entity.Property(x => x.Consumer).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.MessageId).HasMaxLength(200).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.QueueEntryJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.JournalId });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<JournalRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.JournalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigurePostingExceptions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PostingExceptionRow>();
        entity.ToTable("PostingExceptions", Schema, table =>
        {
            table.HasCheckConstraint("CK_PostingExceptions_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([PostingPurpose]) > 0 AND LEN([PostingRuleVersion]) > 0 AND LEN([FirstMessageId]) > 0");
            table.HasCheckConstraint("CK_PostingExceptions_Status", "[Status] IN (1, 2)");
            table.HasCheckConstraint("CK_PostingExceptions_Keys", "ISJSON([MissingMappingKeysJson]) = 1");
            table.HasCheckConstraint("CK_PostingExceptions_Version", "[Version] >= 1");
        });
        entity.HasKey(x => new { x.CompanyId, x.SourceId, x.PostingPurpose });
        entity.Property(x => x.PostingPurpose).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.PostingRuleVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.MissingMappingKeysJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.FirstMessageId).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigurePendingValuations(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PendingValuationRow>();
        entity.ToTable("PendingValuations", Schema, table =>
        {
            table.HasCheckConstraint("CK_PendingValuations_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([PostingPurpose]) > 0 AND LEN([PostingRuleVersion]) > 0 AND LEN([FirstMessageId]) > 0");
            table.HasCheckConstraint("CK_PendingValuations_Status", "[Status] IN (1, 2)");
            table.HasCheckConstraint("CK_PendingValuations_Lines", "[LinesAwaitingAmount] >= 1");
            table.HasCheckConstraint("CK_PendingValuations_Version", "[Version] >= 1");
        });
        entity.HasKey(x => new { x.CompanyId, x.SourceId, x.PostingPurpose });
        entity.Property(x => x.PostingPurpose).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.PostingRuleVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.FirstMessageId).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureCommands(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AccountingCommandRow>();
        entity.ToTable("Commands", Schema, table =>
        {
            table.HasCheckConstraint("CK_Commands_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([ActorId]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_Commands_KeyHash", "LEN([IdempotencyKeyHash]) = 64");
            table.HasCheckConstraint("CK_Commands_PayloadHash", "LEN([PayloadHash]) = 64");
            table.HasCheckConstraint("CK_Commands_ResultJson", "ISJSON([ResultJson]) = 1");
        });
        entity.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        entity.Property(x => x.CommandName).HasMaxLength(64);
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false);
        entity.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ResultJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureNormalJournalDrafts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<NormalJournalDraftRow>();
        entity.ToTable("NormalJournalDrafts", Schema, table =>
        {
            table.HasCheckConstraint("CK_NormalJournalDrafts_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PreparedByUserId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [AccountPlanApprovedByUserId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PostingRuleApprovedByUserId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_NormalJournalDrafts_Text",
                "LEN([Consumer]) > 0 AND LEN([MessageId]) > 0 AND LEN([PostingPurpose]) > 0 "
                + "AND LEN([PostingRuleVersion]) > 0 AND LEN([AccountPlanSourceVersion]) > 0");
            table.HasCheckConstraint("CK_NormalJournalDrafts_Hashes",
                "LEN([ContentHash]) = 64 AND LEN([AccountPlanSourceSha256]) = 64");
            table.HasCheckConstraint("CK_NormalJournalDrafts_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_NormalJournalDrafts_Status",
                "([Status] = 1 AND [ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL AND [PeriodId] IS NULL AND [PostedJournalId] IS NULL) OR "
                + "([Status] = 2 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedByUserId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ApprovedAtUtc] IS NOT NULL AND [PeriodId] IS NOT NULL AND [PeriodId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PostedJournalId] = [Id])");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.ContentHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.Consumer).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.MessageId).HasMaxLength(200).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.PostingPurpose).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.PostingRuleVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.AccountPlanSourceVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.AccountPlanSourceSha256).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.TransactionDate).HasColumnType("date");
        entity.Property(x => x.AccountingDate).HasColumnType("date");
        entity.Property(x => x.ValueDate).HasColumnType("date");
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.AccountingDate });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<JournalRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PostedJournalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureNormalJournalDraftLines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<NormalJournalDraftLineRow>();
        entity.ToTable("NormalJournalDraftLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_NormalJournalDraftLines_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [AccountId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_NormalJournalDraftLines_Line", "[LineNumber] >= 1 AND LEN([MappingKey]) > 0");
            table.HasCheckConstraint("CK_NormalJournalDraftLines_Value", "[Side] IN (1, 2) AND [Amount] > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.DraftId, x.LineNumber });
        entity.Property(x => x.MappingKey).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.Amount).HasPrecision(38, 18);
        entity.HasOne<NormalJournalDraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureJournalReversalProposals(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AccountingJournalReversalProposalRow>();
        entity.ToTable("JournalReversalProposals", Schema, table =>
        {
            table.HasCheckConstraint("CK_JournalReversalProposals_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceJournalId] <> '00000000-0000-0000-0000-000000000000' AND [PeriodId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ProposedByUserId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_JournalReversalProposals_Text", "LEN([Reason]) > 0");
            table.HasCheckConstraint("CK_JournalReversalProposals_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_JournalReversalProposals_Status",
                "([Status] = 1 AND [ReversalJournalId] IS NULL AND [DecidedByUserId] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL) OR "
                + "([Status] = 2 AND [ReversalJournalId] IS NOT NULL AND [DecidedByUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NULL) OR "
                + "([Status] = 3 AND [ReversalJournalId] IS NULL AND [DecidedByUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecisionReason]) > 0)");
            table.HasCheckConstraint("CK_JournalReversalProposals_MakerChecker",
                "[DecidedByUserId] IS NULL OR [DecidedByUserId] <> [ProposedByUserId]");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.TransactionDate).HasColumnType("date");
        entity.Property(x => x.AccountingDate).HasColumnType("date");
        entity.Property(x => x.ValueDate).HasColumnType("date");
        entity.Property(x => x.Reason).HasMaxLength(1024).IsRequired();
        entity.Property(x => x.DecisionReason).HasMaxLength(1024);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.AccountingDate });
        entity.HasIndex(x => new { x.CompanyId, x.SourceJournalId });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<JournalRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceJournalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<JournalRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReversalJournalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureApprovedSourceCatalogs(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ApprovedAccountingSourceCatalogRow>();
        entity.ToTable("ApprovedSourceCatalogs", Schema, table =>
        {
            table.HasCheckConstraint("CK_ApprovedSourceCatalogs_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PreparedByUserId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_ApprovedSourceCatalogs_Status",
                "([Status] = 1 AND [CatalogVersion] IS NULL AND [ActivatedByUserId] IS NULL AND [ActivatedAtUtc] IS NULL) OR "
                + "([Status] IN (2, 3) AND [CatalogVersion] IS NOT NULL AND [CatalogVersion] >= 1 AND [ActivatedByUserId] IS NOT NULL "
                + "AND [ActivatedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [ActivatedAtUtc] IS NOT NULL)");
            table.HasCheckConstraint("CK_ApprovedSourceCatalogs_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_ApprovedSourceCatalogs_Hashes",
                "LEN([AccountsSourceSha256]) = 64 AND LEN([PostingMappingsSourceSha256]) = 64");
            table.HasCheckConstraint("CK_ApprovedSourceCatalogs_Text",
                "LEN([TemplateVersion]) > 0 AND LEN([AccountPlanSourceVersion]) > 0 AND ISJSON([PackageJson]) = 1");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.TemplateVersion).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.AccountPlanSourceVersion).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.AccountsSourceSha256).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.PostingMappingsSourceSha256).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.PackageJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.Status })
            .IsUnique()
            .HasFilter("[Status] = 2")
            .HasDatabaseName("UX_ApprovedSourceCatalogs_Active");
        entity.HasIndex(x => new { x.CompanyId, x.CatalogVersion })
            .IsUnique()
            .HasFilter("[CatalogVersion] IS NOT NULL")
            .HasDatabaseName("UX_ApprovedSourceCatalogs_Version");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigurePostingAccountMappingVersions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AccountingPostingAccountMappingVersionRow>();
        entity.ToTable("PostingAccountMappingVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_PostingAccountMappingVersions_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [MappingVersion] >= 1 "
                + "AND [PreparedByUserId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PostingAccountMappingVersions_Status", PostingAccountMappingVersionStatusCheck);
            table.HasCheckConstraint("CK_PostingAccountMappingVersions_MakerChecker",
                PostingAccountMappingVersionMakerCheckerCheck);
            table.HasCheckConstraint("CK_PostingAccountMappingVersions_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_PostingAccountMappingVersions_Plan",
                "LEN([AccountPlanSourceVersion]) > 0 AND LEN([AccountPlanSourceSha256]) = 64");
        });
        entity.HasKey(x => new { x.CompanyId, x.MappingVersion });
        entity.Property(x => x.MappingVersion).ValueGeneratedNever();
        entity.Property(x => x.EffectiveFrom).HasColumnType("date");
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.AccountPlanSourceVersion).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.AccountPlanSourceSha256).HasMaxLength(64).IsUnicode(false).IsRequired();
        // One active version per company, under any race.
        entity.HasIndex(x => x.CompanyId)
            .IsUnique()
            .HasFilter("[Status] = 2")
            .HasDatabaseName("UX_PostingAccountMappingVersions_Active");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigurePostingAccountMappingRows(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AccountingPostingAccountMappingEntryRow>();
        entity.ToTable("PostingAccountMappingRows", Schema, table =>
        {
            table.HasCheckConstraint("CK_PostingAccountMappingRows_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [MappingVersion] >= 1 AND [LineNumber] >= 1 "
                + "AND [AccountId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PostingAccountMappingRows_Key", PostingAccountMappingRowKeyCheck);
            table.HasCheckConstraint("CK_PostingAccountMappingRows_Text",
                "LEN([AccountCode]) > 0 AND ([ItemType] IS NULL OR LEN([ItemType]) > 0) "
                + "AND ([LinePurpose] IS NULL OR LEN([LinePurpose]) > 0)");
            table.HasCheckConstraint("CK_PostingAccountMappingRows_VatRate", PostingAccountMappingRowVatRateCheck);
        });
        entity.HasKey(x => new { x.CompanyId, x.MappingVersion, x.LineNumber });
        entity.Property(x => x.Purpose).HasMaxLength(32).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.MappingKey).HasMaxLength(32).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.ItemType).HasMaxLength(64).UseCollation(IdentityCollation);
        entity.Property(x => x.LinePurpose).HasMaxLength(64).UseCollation(IdentityCollation);
        entity.Property(x => x.AccountCode).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.VatRateCode).HasMaxLength(64).UseCollation(IdentityCollation);
        // Deliberately unfiltered: NULL selectors compare equal in a SQL Server unique index, so each
        // combination of named and unnamed selectors exists at most once per version.
        entity.HasIndex(x => new
            {
                x.CompanyId, x.MappingVersion, x.Purpose, x.MappingKey, x.ItemType, x.LinePurpose, x.IsExport, x.VatRateCode,
            })
            .IsUnique()
            .HasFilter(null)
            .HasDatabaseName("UX_PostingAccountMappingRows_Selector");
        entity.HasOne<AccountingPostingAccountMappingVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MappingVersion })
            .HasPrincipalKey(x => new { x.CompanyId, x.MappingVersion })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureSourceDocumentAdmissions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AccountingSourceDocumentAdmissionRow>();
        entity.ToTable("SourceDocumentAdmissions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SourceDocumentAdmissions_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceDocumentId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [SourceActorId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [FinalizedVersion] >= 1 AND LEN([DocumentKind]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_SourceDocumentAdmissions_Status", SourceDocumentAdmissionStatusCheck);
            table.HasCheckConstraint("CK_SourceDocumentAdmissions_Attempts", "[AttemptCount] >= 1 AND [Version] >= 1");
            table.HasCheckConstraint("CK_SourceDocumentAdmissions_Hashes",
                "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
        });
        entity.HasKey(x => new { x.CompanyId, x.SourceDocumentId });
        entity.Property(x => x.SourceDocumentId).ValueGeneratedNever();
        entity.Property(x => x.DocumentKind).HasMaxLength(32).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ReasonCode).HasMaxLength(64).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.CompanyId, x.IdempotencyKeyHash }).IsUnique()
            .HasDatabaseName("UX_SourceDocumentAdmissions_Key");
        entity.HasIndex(x => new { x.CompanyId, x.DocumentKind, x.InvoiceId, x.FinalizedVersion }).IsUnique()
            .HasDatabaseName("UX_SourceDocumentAdmissions_Document");
        entity.HasIndex(x => new { x.CompanyId, x.VoucherDraftId }).IsUnique()
            .HasFilter("[VoucherDraftId] IS NOT NULL")
            .HasDatabaseName("UX_SourceDocumentAdmissions_Draft");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<AccountingSourceDocumentVoucherDraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.VoucherDraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureSourceDocumentVoucherDrafts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AccountingSourceDocumentVoucherDraftRow>();
        entity.ToTable("SourceDocumentVoucherDrafts", Schema, table =>
        {
            table.HasCheckConstraint("CK_SourceDocumentVoucherDrafts_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceDocumentId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceActorId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] >= 1 AND [MappingVersion] >= 1");
            table.HasCheckConstraint("CK_SourceDocumentVoucherDrafts_Status", "[Status] = 1");
            table.HasCheckConstraint("CK_SourceDocumentVoucherDrafts_Amounts",
                "[DocumentNet] >= 0 AND [DocumentVat] >= 0 AND [DocumentWithholding] >= 0 AND [DocumentPayable] >= 0 "
                + "AND [FunctionalNet] >= 0 AND [FunctionalVat] >= 0 AND [FunctionalWithholding] >= 0 AND [FunctionalPayable] >= 0 "
                + "AND [FunctionalWithholding] <= [FunctionalVat] AND [FunctionalPayable] = [FunctionalNet] + [FunctionalVat] - [FunctionalWithholding]");
            table.HasCheckConstraint("CK_SourceDocumentVoucherDrafts_Version", "[Version] >= 1");
        });
        entity.HasKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.DocumentKind).HasMaxLength(32).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.ExchangeRate).HasMaxLength(40).IsUnicode(false);
        entity.Property(x => x.DocumentDate).HasColumnType("date");
        entity.Property(x => x.DueDate).HasColumnType("date");
        entity.Property(x => x.DocumentNet).HasPrecision(38, 18);
        entity.Property(x => x.DocumentVat).HasPrecision(38, 18);
        entity.Property(x => x.DocumentWithholding).HasPrecision(38, 18);
        entity.Property(x => x.DocumentPayable).HasPrecision(38, 18);
        entity.Property(x => x.FunctionalNet).HasPrecision(38, 18);
        entity.Property(x => x.FunctionalVat).HasPrecision(38, 18);
        entity.Property(x => x.FunctionalWithholding).HasPrecision(38, 18);
        entity.Property(x => x.FunctionalPayable).HasPrecision(38, 18);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.CompanyId, x.SourceDocumentId }).IsUnique()
            .HasDatabaseName("UX_SourceDocumentVoucherDrafts_Source");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureSourceDocumentVoucherDraftLines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AccountingSourceDocumentVoucherDraftLineRow>();
        entity.ToTable("SourceDocumentVoucherDraftLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_SourceDocumentVoucherDraftLines_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [AccountId] <> '00000000-0000-0000-0000-000000000000' AND [LineNumber] >= 1 "
                + "AND ([SourceLineNumber] IS NULL OR [SourceLineNumber] >= 1) AND LEN([MappingKey]) > 0");
            table.HasCheckConstraint("CK_SourceDocumentVoucherDraftLines_Side", "[Side] IN (1, 2)");
            table.HasCheckConstraint("CK_SourceDocumentVoucherDraftLines_Amount", "[Amount] > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.DraftId, x.LineNumber });
        entity.Property(x => x.MappingKey).HasMaxLength(32).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.VatRateCode).HasMaxLength(64).UseCollation(IdentityCollation);
        entity.Property(x => x.Amount).HasPrecision(38, 18);
        entity.HasOne<AccountingSourceDocumentVoucherDraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ApplyWriteGuards()
    {
        AccountingAuditEventTenantGuard.Validate(this, companyContext);

        foreach (var entry in ChangeTracker.Entries<IAccountingTenantRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Muhasebe kayıtları silinmez; düzeltme yeni bir fiştir.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Muhasebe kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Muhasebe kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.");
            }

            if (entry.State == EntityState.Modified)
            {
                string[] allowed = entry.Entity switch
                {
                    PeriodCalendarRow => [nameof(PeriodCalendarRow.UpdatedAtUtc)],
                    // Ö4: a proposal changes only by its approval, an open period only by its close.
                    PeriodRow => [nameof(PeriodRow.Status), nameof(PeriodRow.ApprovedByUserId), nameof(PeriodRow.ApprovedAtUtc),
                        nameof(PeriodRow.ClosedByUserId), nameof(PeriodRow.ClosedAtUtc), nameof(PeriodRow.UpdatedAtUtc)],
                    PostingExceptionRow => [nameof(PostingExceptionRow.Status), nameof(PostingExceptionRow.PostingRuleVersion),
                        nameof(PostingExceptionRow.MissingMappingKeysJson), nameof(PostingExceptionRow.FirstMessageId),
                        nameof(PostingExceptionRow.UpdatedAtUtc)],
                    PendingValuationRow => [nameof(PendingValuationRow.Status), nameof(PendingValuationRow.PostingRuleVersion),
                        nameof(PendingValuationRow.LinesAwaitingAmount), nameof(PendingValuationRow.FirstMessageId),
                        nameof(PendingValuationRow.UpdatedAtUtc)],
                    NormalJournalDraftRow => [nameof(NormalJournalDraftRow.Status), nameof(NormalJournalDraftRow.ApprovedByUserId),
                        nameof(NormalJournalDraftRow.ApprovedAtUtc), nameof(NormalJournalDraftRow.PeriodId),
                        nameof(NormalJournalDraftRow.PostedJournalId), nameof(NormalJournalDraftRow.UpdatedAtUtc)],
                    AccountingJournalReversalProposalRow => [nameof(AccountingJournalReversalProposalRow.Status),
                        nameof(AccountingJournalReversalProposalRow.ReversalJournalId),
                        nameof(AccountingJournalReversalProposalRow.DecidedByUserId),
                        nameof(AccountingJournalReversalProposalRow.DecidedAtUtc),
                        nameof(AccountingJournalReversalProposalRow.DecisionReason)],
                    ApprovedAccountingSourceCatalogRow => [nameof(ApprovedAccountingSourceCatalogRow.Status),
                        nameof(ApprovedAccountingSourceCatalogRow.CatalogVersion),
                        nameof(ApprovedAccountingSourceCatalogRow.ActivatedByUserId),
                        nameof(ApprovedAccountingSourceCatalogRow.ActivatedAtUtc),
                        nameof(ApprovedAccountingSourceCatalogRow.UpdatedAtUtc)],
                    // B-04a: a mapping version changes only by its activation or supersession; its rows never.
                    AccountingPostingAccountMappingVersionRow => [nameof(AccountingPostingAccountMappingVersionRow.Status),
                        nameof(AccountingPostingAccountMappingVersionRow.ActivatedByUserId),
                        nameof(AccountingPostingAccountMappingVersionRow.ActivatedAtUtc),
                        nameof(AccountingPostingAccountMappingVersionRow.UpdatedAtUtc)],
                    // B-04b: an admission changes only by its attempt facts and its final answer; the draft and its lines never.
                    AccountingSourceDocumentAdmissionRow => [nameof(AccountingSourceDocumentAdmissionRow.Status),
                        nameof(AccountingSourceDocumentAdmissionRow.ReasonCode),
                        nameof(AccountingSourceDocumentAdmissionRow.VoucherDraftId),
                        nameof(AccountingSourceDocumentAdmissionRow.AttemptCount),
                        nameof(AccountingSourceDocumentAdmissionRow.LastAttemptedAtUtc),
                        nameof(AccountingSourceDocumentAdmissionRow.CorrelationId),
                        nameof(AccountingSourceDocumentAdmissionRow.UpdatedAtUtc)],
                    _ => [],
                };
                var changed = entry.Properties
                    .Where(property => property.IsModified && !property.Metadata.IsConcurrencyToken)
                    .Select(property => property.Metadata.Name)
                    .Where(name => !allowed.Contains(name))
                    .ToArray();
                if (changed.Length > 0 || allowed.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Muhasebe kaydının yalnız izinli alanı değişir; fişler, satırlar ve teslimat kayıtları değişmez.");
                }
            }
        }
    }
}
