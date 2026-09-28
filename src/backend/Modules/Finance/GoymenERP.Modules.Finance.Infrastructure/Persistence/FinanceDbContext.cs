using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// Finance's own tables in the <c>finance</c> schema (FIN-T04a, ADR-002).
/// <list type="bullet">
/// <item>Every row is a tenant row: a global query filter on the server-resolved company, and a
/// write guard that refuses any row of another company, any company change and any write without a
/// resolved company.</item>
/// <item>A receipt draft changes only its preparation fields, version and update trace; nothing is
/// deleted. A command record is append-only.</item>
/// <item>A money event (FIN-T05a) is one per (company, source, kind) and append-only; a recorded source never
/// changes again.</item>
/// <item>A bank account (FS0) changes only by its one approval; the database itself refuses an
/// approval by the proposer (<c>CK_BankAccounts_Approval</c>, Ö4).</item>
/// <item>Idempotency (ADR-011): (company, command, key hash) is the command record's key, so one key
/// is one attempt per command and company under a race; the actor is kept and must match.</item>
/// </list>
/// The audit sidecar is Audit's table, written in the same save and filtered the same way.
/// </summary>
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "finance";

    /// <summary>Binary collation: an actor id compares exactly (no case folding).</summary>
    public const string IdentityCollation = "Latin1_General_100_BIN2";

    internal DbSet<ReceiptDraftRow> ReceiptDrafts => Set<ReceiptDraftRow>();

    internal DbSet<FinanceCommandRow> Commands => Set<FinanceCommandRow>();

    internal DbSet<BankAccountRow> BankAccounts => Set<BankAccountRow>();

    internal DbSet<MoneyEventRow> MoneyEvents => Set<MoneyEventRow>();

    internal DbSet<FinanceOpenItemRow> OpenItems => Set<FinanceOpenItemRow>();
    internal DbSet<FinanceOpenItemSourceBatchRow> OpenItemSourceBatches => Set<FinanceOpenItemSourceBatchRow>();

    internal DbSet<FinanceSourceAllocationStateRow> SourceAllocationStates => Set<FinanceSourceAllocationStateRow>();

    internal DbSet<FinanceAllocationRow> Allocations => Set<FinanceAllocationRow>();

    internal DbSet<ReversalProposalRow> ReversalProposals => Set<ReversalProposalRow>();

    internal DbSet<ManualBankPaymentRow> ManualBankPayments => Set<ManualBankPaymentRow>();

    internal DbSet<OutgoingMoneyEventRow> OutgoingMoneyEvents => Set<OutgoingMoneyEventRow>();

    internal DbSet<CustomerInstrumentRow> CustomerInstruments => Set<CustomerInstrumentRow>();

    internal DbSet<CustomerInstrumentMovementRow> CustomerInstrumentMovements => Set<CustomerInstrumentMovementRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        ConfigureReceiptDrafts(modelBuilder);
        ConfigureBankAccounts(modelBuilder);
        ConfigureCommands(modelBuilder);
        ConfigureMoneyEvents(modelBuilder);
        ConfigureOpenItemSourceBatches(modelBuilder);
        ConfigureOpenItems(modelBuilder);
        ConfigureSourceAllocationStates(modelBuilder);
        ConfigureAllocations(modelBuilder);
        ConfigureReversalProposals(modelBuilder);
        ConfigureManualBankPayments(modelBuilder);
        ConfigureOutgoingMoneyEvents(modelBuilder);
        ConfigureCustomerInstruments(modelBuilder);
        ConfigureCustomerInstrumentMovements(modelBuilder);
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

    private void ConfigureReceiptDrafts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReceiptDraftRow>();
        entity.ToTable("ReceiptDrafts", Schema, table =>
        {
            table.HasCheckConstraint("CK_ReceiptDrafts_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PartyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [TreasuryAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_ReceiptDrafts_Text",
                "LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 "
                + "AND LEN([CreatedBy]) > 0 AND LEN([UpdatedBy]) > 0");
            table.HasCheckConstraint("CK_ReceiptDrafts_Version", "[Version] >= 1");
            // FIN-T05a: a draft carries no record trace; a recorded source carries who and when.
            table.HasCheckConstraint("CK_ReceiptDrafts_Stage", StageCheck);
            // DEC-FIN-014: a bank transfer carries no POS fact; a POS collection carries all three.
            table.HasCheckConstraint("CK_ReceiptDrafts_Pos", PosCheck);
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        // No local digit limit: the shared ExactPositiveDecimalV1 contract sets none (Finance examples README).
        entity.Property(x => x.AmountText).HasColumnType("varchar(max)").IsUnicode(false).IsRequired();
        entity.Property(x => x.ProfileId).HasMaxLength(36).IsUnicode(false).IsRequired();
        entity.Property(x => x.ProfileVersion).HasMaxLength(20).IsUnicode(false).IsRequired();
        entity.Property(x => x.OccurredOn).HasColumnType("date");
        entity.Property(x => x.ValueOn).HasColumnType("date");
        entity.Property(x => x.BankReference).HasMaxLength(128);
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.UpdatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RecordedBy).HasMaxLength(128);
        // DEC-FIN-014: exact text like the amount; no database decimal is a money scale.
        entity.Property(x => x.PosCommissionAmountText).HasColumnType("varchar(max)").IsUnicode(false);
        entity.Property(x => x.PosBankNetAmountText).HasColumnType("varchar(max)").IsUnicode(false);
        entity.HasIndex(x => new { x.CompanyId, x.UpdatedAtUtc, x.Id });
        // The list's stable order (FIN-T04b): newest created first; an edit never moves a draft between pages.
        entity.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc, x.Id });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureBankAccounts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<BankAccountRow>();
        entity.ToTable("BankAccounts", Schema, table =>
        {
            table.HasCheckConstraint("CK_BankAccounts_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_BankAccounts_Text", "LEN([DisplayName]) > 0 AND LEN([ProposedBy]) > 0");
            table.HasCheckConstraint("CK_BankAccounts_Version", "[Version] >= 1");
            // Ö4: proposed carries no approval; approved carries one, by someone other than the proposer.
            table.HasCheckConstraint("CK_BankAccounts_Approval",
                "([Status] = 1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR "
                + "([Status] = 2 AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND LEN([ApprovedBy]) > 0 "
                + "AND [ApprovedBy] <> [ProposedBy])");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();

        // K2's card. Every column is NULLABLE, including the two K2 calls mandatory: the rows that
        // existed before K2 have no value for them, and making the column required would force this
        // slice to decide what those rows mean. B3 (#66 5750700247) has now decided it — UNDECLARED,
        // refused on the money path — and the answer does NOT change these columns: it adds a
        // declaration beside them rather than reinterpreting them. The mandatory pair is still
        // enforced where a card is WRITTEN, so new cards cannot omit it.
        entity.Property(x => x.BankName).HasMaxLength(200);
        entity.Property(x => x.AccountType).HasMaxLength(64);
        entity.Property(x => x.BranchName).HasMaxLength(200);
        entity.Property(x => x.BranchCode).HasMaxLength(64);
        // ISO 13616's maximum is 34 characters; stored normalised, so the column never holds spacing.
        entity.Property(x => x.Iban).HasMaxLength(34);
        entity.Property(x => x.AccountNumber).HasMaxLength(64);

        // B3's declaration. Nullable ONLY to carry the pre-declaration rows; NULL is read as UNDECLARED
        // and never as false, so the two can be told apart and shown apart.
        entity.Property(x => x.MoneyMovementEligible);

        // D4's pending IBAN change, beside the row and never in [Iban] until approved. The check keeps
        // the four members all-null or all-set, so a half-written pending change cannot exist.
        entity.Property(x => x.PendingIban).HasMaxLength(34);
        entity.Property(x => x.PendingIbanProposedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        entity.Property(x => x.PendingIbanReason).HasMaxLength(500);
        entity.ToTable(table => table.HasCheckConstraint("CK_BankAccounts_PendingIbanChange", PendingIbanCheck));

        entity.Property(x => x.ProposedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.ApprovedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        entity.HasIndex(x => new { x.CompanyId, x.Status });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// D4: a pending IBAN change is all four members or none of them (the migration that introduced it
    /// uses the same text). A half-written pending change would be a value nobody proposed — the
    /// approver would see an IBAN with no proposer to be different from, and Ö4 would have nothing to
    /// compare against.
    /// </summary>
    internal const string PendingIbanCheck =
        "([PendingIban] IS NULL AND [PendingIbanProposedBy] IS NULL AND [PendingIbanProposedAtUtc] IS NULL "
        + "AND [PendingIbanReason] IS NULL) OR "
        + "([PendingIban] IS NOT NULL AND [PendingIbanProposedBy] IS NOT NULL AND LEN([PendingIbanProposedBy]) > 0 "
        + "AND [PendingIbanProposedAtUtc] IS NOT NULL AND [PendingIbanReason] IS NOT NULL AND LEN([PendingIbanReason]) > 0)";

    /// <summary>The stage rule of a receipt source (FIN-T05a); the migration that introduced it uses the same text.</summary>
    internal const string StageCheck =
        "([Stage] = 0 AND [RecordedAtUtc] IS NULL AND [RecordedBy] IS NULL) OR "
        + "([Stage] = 1 AND [RecordedAtUtc] IS NOT NULL AND [RecordedBy] IS NOT NULL AND LEN([RecordedBy]) > 0)";

    /// <summary>
    /// DEC-FIN-014: the POS facts of a receipt source and of its money event are all NULL (a bank transfer) or all set,
    /// with a known bearer (1 company, 2 customer). The migration that introduced it uses the same text.
    /// </summary>
    internal const string PosCheck =
        "([PosCommissionAmountText] IS NULL AND [PosBankNetAmountText] IS NULL AND [PosCommissionBearer] IS NULL) OR "
        + "([PosCommissionAmountText] IS NOT NULL AND LEN([PosCommissionAmountText]) > 0 "
        + "AND [PosBankNetAmountText] IS NOT NULL AND LEN([PosBankNetAmountText]) > 0 "
        + "AND [PosCommissionBearer] IN (1, 2))";

    /// <summary>
    /// FIN-T05a: one money event per (company, source, kind). Its source is the company's receipt source, and every
    /// fact is non-empty; the row is append-only (the write guard).
    /// </summary>
    private void ConfigureMoneyEvents(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MoneyEventRow>();
        entity.ToTable("MoneyEvents", Schema, table =>
        {
            table.HasCheckConstraint("CK_MoneyEvents_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PartyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [TreasuryAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
            // DEC-FIN-016: 3 is a customer instrument's collection (a kind of its own; FIN-T06 does not reverse it).
            table.HasCheckConstraint("CK_MoneyEvents_Kind", MoneyEventKindCheck);
            table.HasCheckConstraint("CK_MoneyEvents_Text",
                "LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 "
                + "AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_MoneyEvents_Pos", PosCheck);
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.AmountText).HasColumnType("varchar(max)").IsUnicode(false).IsRequired();
        entity.Property(x => x.ProfileId).HasMaxLength(36).IsUnicode(false).IsRequired();
        entity.Property(x => x.ProfileVersion).HasMaxLength(20).IsUnicode(false).IsRequired();
        entity.Property(x => x.OccurredOn).HasColumnType("date");
        entity.Property(x => x.ValueOn).HasColumnType("date");
        entity.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.PosCommissionAmountText).HasColumnType("varchar(max)").IsUnicode(false);
        entity.Property(x => x.PosBankNetAmountText).HasColumnType("varchar(max)").IsUnicode(false);
        // Exactly once: a second event of the same kind for the same source cannot be stored, whoever races.
        entity.HasIndex(x => new { x.CompanyId, x.SourceId, x.Kind }).IsUnique();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ReceiptDraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureReversalProposals(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReversalProposalRow>();
        entity.ToTable("ReversalProposals", Schema, table =>
        {
            table.HasCheckConstraint("CK_ReversalProposals_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[OriginalSourceId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_ReversalProposals_Text",
                "LEN([Reason]) > 0 AND LEN([ProposedBy]) > 0");
            table.HasCheckConstraint("CK_ReversalProposals_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_ReversalProposals_Decision", ReversalProposalDecisionCheck);
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.OccurredOn).HasColumnType("date");
        entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.ProposedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.DecidedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        entity.Property(x => x.DecisionReason).HasMaxLength(1000);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasIndex(x => new { x.CompanyId, x.OriginalSourceId }, "UX_ReversalProposals_PendingOriginal")
            .HasFilter("[Status] = 0").IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.OriginalSourceId }, "UX_ReversalProposals_ApprovedOriginal")
            .HasFilter("[Status] = 1").IsUnique();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ReceiptDraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OriginalSourceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ReceiptDraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReversalSourceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureOpenItemSourceBatches(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FinanceOpenItemSourceBatchRow>();
        entity.ToTable("OpenItemSourceBatches", Schema, table =>
        {
            table.HasCheckConstraint("CK_OpenItemSourceBatches_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_OpenItemSourceBatches_Text",
                "LEN([SourceKind]) > 0 AND LEN([SourceVersion]) > 0 AND LEN([SourceChecksum]) = 64 AND "
                + "LEN([IdempotencyKeyHash]) = 64 AND LEN([PayloadHash]) = 64 AND LEN([ActorId]) > 0 AND "
                + "LEN([CorrelationId]) > 0 AND ISJSON([ResultJson]) = 1");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.SourceKind).HasMaxLength(128).IsUnicode(false).IsRequired();
        entity.Property(x => x.SourceVersion).HasMaxLength(128).IsUnicode(false).IsRequired();
        entity.Property(x => x.SourceChecksum).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
        entity.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.ResultJson).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.CausationId).HasMaxLength(128).UseCollation(IdentityCollation);
        entity.HasIndex(x => new { x.CompanyId, x.IdempotencyKeyHash }).IsUnique();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureOpenItems(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FinanceOpenItemRow>();
        entity.ToTable("OpenItems", Schema, table =>
        {
            table.HasCheckConstraint("CK_OpenItems_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[SourceId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[SourceLineId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[SourceBatchId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[PartyId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_OpenItems_Text",
                "LEN([SourceKind]) > 0 AND LEN([SourceVersion]) > 0 AND "
                + "LEN([SourceBatchVersion]) > 0 AND LEN([SourceBatchChecksum]) = 64 AND "
                + "LEN([OriginalAmountText]) > 0 AND LEN([OutstandingAmountText]) > 0 AND "
                + "LEN([CreatedBy]) > 0 AND LEN([UpdatedBy]) > 0");
            table.HasCheckConstraint("CK_OpenItems_Status", "[Status] IN (1, 2)");
            table.HasCheckConstraint("CK_OpenItems_Version", "[Version] >= 1");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.SourceKind).HasMaxLength(128).IsUnicode(false).IsRequired();
        entity.Property(x => x.SourceVersion).HasMaxLength(128).IsUnicode(false).IsRequired();
        entity.Property(x => x.SourceBatchVersion).HasMaxLength(128).IsUnicode(false).IsRequired();
        entity.Property(x => x.SourceBatchChecksum).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
        entity.Property(x => x.OriginalAmountText).HasColumnType("varchar(max)").IsUnicode(false).IsRequired();
        entity.Property(x => x.OutstandingAmountText).HasColumnType("varchar(max)").IsUnicode(false).IsRequired();
        entity.Property(x => x.DueOn).HasColumnType("date");
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.CreatedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.UpdatedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.SourceKind, x.SourceId, x.SourceLineId }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.CompanyAccountId, x.CurrencyId, x.Status, x.DueOn });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<FinanceOpenItemSourceBatchRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceBatchId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureAllocations(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FinanceAllocationRow>();
        entity.ToTable("Allocations", Schema, table =>
        {
            table.HasCheckConstraint("CK_Allocations_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[SourceId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[OpenItemId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Allocations_Text",
                "LEN([AmountText]) > 0 AND LEN([Reason]) > 0 AND LEN([CreatedBy]) > 0");
            table.HasCheckConstraint("CK_Allocations_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_Allocations_Status", AllocationStatusCheck);
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.AmountText).HasColumnType("varchar(max)").IsUnicode(false).IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.CreatedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.ReleasedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        entity.Property(x => x.ReleaseReason).HasMaxLength(1000);
        entity.HasIndex(x => new { x.CompanyId, x.SourceId, x.Status });
        entity.HasIndex(x => new { x.CompanyId, x.OpenItemId, x.Status });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ReceiptDraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<FinanceOpenItemRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OpenItemId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureSourceAllocationStates(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FinanceSourceAllocationStateRow>();
        entity.ToTable("SourceAllocationStates", Schema, table =>
        {
            table.HasCheckConstraint("CK_SourceAllocationStates_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[SourceId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_SourceAllocationStates_Text",
                "LEN([OriginalAmountText]) > 0 AND LEN([UnallocatedAmountText]) > 0 AND LEN([UpdatedBy]) > 0");
            table.HasCheckConstraint("CK_SourceAllocationStates_Version", "[Version] >= 1");
        });
        entity.HasKey(x => new { x.CompanyId, x.SourceId });
        entity.Property(x => x.OriginalAmountText).HasColumnType("varchar(max)").IsUnicode(false).IsRequired();
        entity.Property(x => x.UnallocatedAmountText).HasColumnType("varchar(max)").IsUnicode(false).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.UpdatedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ReceiptDraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    internal const string AllocationStatusCheck =
        "([Status] = 1 AND [ReleasedBy] IS NULL AND [ReleasedAtUtc] IS NULL AND [ReleaseReason] IS NULL) OR "
        + "([Status] = 2 AND [ReleasedBy] IS NOT NULL AND LEN([ReleasedBy]) > 0 "
        + "AND [ReleasedAtUtc] IS NOT NULL AND [ReleaseReason] IS NOT NULL AND LEN([ReleaseReason]) > 0)";

    internal const string ReversalProposalDecisionCheck =
        "([Status] = 0 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [ReversalSourceId] IS NULL) OR "
        + "([Status] = 1 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL "
        + "AND [DecisionReason] IS NULL AND [ReversalSourceId] IS NOT NULL AND [ReversalSourceId] <> [OriginalSourceId] "
        + "AND [ProposedBy] <> [DecidedBy]) OR "
        + "([Status] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL "
        + "AND [DecisionReason] IS NOT NULL AND LEN([DecisionReason]) > 0 AND [ReversalSourceId] IS NULL "
        + "AND [ProposedBy] <> [DecidedBy])";

    internal const string ManualBankPaymentStatusCheck =
        "([Status] = 0 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [RecordedBy] IS NULL "
        + "AND [RecordedAtUtc] IS NULL AND [BankResultConfirmed] = 0) OR "
        + "([Status] = 1 AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL "
        + "AND [ApprovedBy] <> [PreparedBy] AND [RecordedBy] IS NULL AND [RecordedAtUtc] IS NULL "
        + "AND [BankResultConfirmed] = 0) OR "
        + "([Status] = 2 AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL "
        + "AND [ApprovedBy] <> [PreparedBy] AND [RecordedBy] IS NOT NULL AND LEN([RecordedBy]) > 0 "
        + "AND [RecordedAtUtc] IS NOT NULL AND [BankResultConfirmed] = 1)";

    private void ConfigureManualBankPayments(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ManualBankPaymentRow>();
        entity.ToTable("ManualBankPayments", Schema, table =>
        {
            table.HasCheckConstraint("CK_ManualBankPayments_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [TreasuryAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_ManualBankPayments_Text",
                "LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 AND LEN([PreparedBy]) > 0 "
                + "AND (([LinkedDocumentId] IS NULL AND [LinkedDocumentVersion] IS NULL) OR "
                + "([LinkedDocumentId] IS NOT NULL AND [LinkedDocumentId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [LinkedDocumentVersion] IS NOT NULL AND LEN([LinkedDocumentVersion]) > 0))");
            table.HasCheckConstraint("CK_ManualBankPayments_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_ManualBankPayments_Status", ManualBankPaymentStatusCheck);
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.AmountText).HasColumnType("varchar(max)").IsUnicode(false).IsRequired();
        entity.Property(x => x.ProfileId).HasMaxLength(36).IsUnicode(false).IsRequired();
        entity.Property(x => x.ProfileVersion).HasMaxLength(20).IsUnicode(false).IsRequired();
        entity.Property(x => x.OccurredOn).HasColumnType("date");
        entity.Property(x => x.ValueOn).HasColumnType("date");
        entity.Property(x => x.BankReference).HasMaxLength(128);
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.LinkedDocumentVersion).HasMaxLength(128).UseCollation(IdentityCollation);
        entity.Property(x => x.PreparedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.ApprovedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        entity.Property(x => x.RecordedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        entity.HasIndex(x => new { x.CompanyId, x.PreparedAtUtc, x.Id });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureOutgoingMoneyEvents(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OutgoingMoneyEventRow>();
        entity.ToTable("OutgoingMoneyEvents", Schema, table =>
        {
            table.HasCheckConstraint("CK_OutgoingMoneyEvents_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PaymentId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [TreasuryAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_OutgoingMoneyEvents_Text",
                "LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.AmountText).HasColumnType("varchar(max)").IsUnicode(false).IsRequired();
        entity.Property(x => x.ProfileId).HasMaxLength(36).IsUnicode(false).IsRequired();
        entity.Property(x => x.ProfileVersion).HasMaxLength(20).IsUnicode(false).IsRequired();
        entity.Property(x => x.OccurredOn).HasColumnType("date");
        entity.Property(x => x.ValueOn).HasColumnType("date");
        entity.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.PaymentId }).IsUnique();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ManualBankPaymentRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PaymentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// The command record's target rule; the migration that last changed it (<c>AddCustomerInstruments</c>, DEC-FIN-016:
    /// a customer instrument is the sixth target) uses the same text.
    /// </summary>
    internal const string TargetCheck =
        "([DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL AND [CustomerInstrumentId] IS NULL) OR "
        + "([BankAccountId] IS NOT NULL AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL AND [CustomerInstrumentId] IS NULL) OR "
        + "([ReversalProposalId] IS NOT NULL AND [ReversalProposalId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL AND [CustomerInstrumentId] IS NULL) OR "
        + "([ManualBankPaymentId] IS NOT NULL AND [ManualBankPaymentId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [FinanceAllocationId] IS NULL AND [CustomerInstrumentId] IS NULL) OR "
        + "([FinanceAllocationId] IS NOT NULL AND [FinanceAllocationId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [CustomerInstrumentId] IS NULL) OR "
        + "([CustomerInstrumentId] IS NOT NULL AND [CustomerInstrumentId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL) OR "
        + "([DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL AND [CustomerInstrumentId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')";

    /// <summary>The money event kinds (1 recorded receipt, 2 its reversal, 3 a customer instrument's collection).</summary>
    internal const string MoneyEventKindCheck = "[Kind] IN (1, 2, 3)";

    /// <summary>
    /// DEC-FIN-016: a customer instrument's status and its bank / collection facts agree. The bank account is held
    /// only at the bank and after collection; only a collection names its receipt source; a cheque is dishonoured as
    /// Bounced (4), a note as Protested (5). The migration that introduced it uses the same text.
    /// </summary>
    internal const string CustomerInstrumentStateCheck =
        "([Status] IN (1, 6) AND [TreasuryAccountId] IS NULL AND [CollectionSourceId] IS NULL) OR "
        + "([Status] = 2 AND [TreasuryAccountId] IS NOT NULL AND [CollectionSourceId] IS NULL) OR "
        + "([Status] = 3 AND [TreasuryAccountId] IS NOT NULL AND [CollectionSourceId] IS NOT NULL) OR "
        + "([Status] = 4 AND [Kind] = 1 AND [TreasuryAccountId] IS NULL AND [CollectionSourceId] IS NULL) OR "
        + "([Status] = 5 AND [Kind] = 2 AND [TreasuryAccountId] IS NULL AND [CollectionSourceId] IS NULL)";

    /// <summary>DEC-FIN-016: a cheque names the bank it is drawn on; a note has none required.</summary>
    internal const string CustomerInstrumentBankCheck =
        "[Kind] = 2 OR ([BankName] IS NOT NULL AND LEN([BankName]) > 0)";

    private void ConfigureCustomerInstruments(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CustomerInstrumentRow>();
        entity.ToTable("CustomerInstruments", Schema, table =>
        {
            table.HasCheckConstraint("CK_CustomerInstruments_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PartyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_CustomerInstruments_Text",
                "LEN([SerialNumber]) > 0 AND LEN([Drawer]) > 0 AND LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 "
                + "AND LEN([ProfileVersion]) > 0 AND LEN([ReceivedBy]) > 0 AND LEN([UpdatedBy]) > 0");
            table.HasCheckConstraint("CK_CustomerInstruments_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_CustomerInstruments_Kind", "[Kind] IN (1, 2)");
            table.HasCheckConstraint("CK_CustomerInstruments_Bank", CustomerInstrumentBankCheck);
            table.HasCheckConstraint("CK_CustomerInstruments_State", CustomerInstrumentStateCheck);
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.SerialNumber).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Drawer).HasMaxLength(200).IsRequired();
        entity.Property(x => x.BankName).HasMaxLength(200);
        entity.Property(x => x.BranchName).HasMaxLength(200);
        entity.Property(x => x.DueOn).HasColumnType("date");
        entity.Property(x => x.ReceivedOn).HasColumnType("date");
        entity.Property(x => x.StatusChangedOn).HasColumnType("date");
        // No local digit limit: the shared ExactPositiveDecimalV1 contract sets none.
        entity.Property(x => x.AmountText).HasColumnType("varchar(max)").IsUnicode(false).IsRequired();
        entity.Property(x => x.ProfileId).HasMaxLength(36).IsUnicode(false).IsRequired();
        entity.Property(x => x.ProfileVersion).HasMaxLength(20).IsUnicode(false).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.ReceivedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.UpdatedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        // The workspace's "araç no mükerrer değil": one instrument per kind, serial, drawer and bank in a company.
        // Covers every row on purpose: a note has no bank, and SQL Server's unique index treats the NULLs as one value,
        // so a note is covered too. The filter is on the non-nullable CompanyId, i.e. true for every row: it exists only
        // because Finance's hand-written migrations carry no target model, and without an explicit filter EF's SQL
        // Server generator adds the legacy "[BankName] IS NOT NULL" filter, which would exempt every note (#501 SQL).
        entity.HasIndex(x => new { x.CompanyId, x.Kind, x.SerialNumber, x.Drawer, x.BankName },
            "UX_CustomerInstruments_Identity").IsUnique().HasFilter("[CompanyId] IS NOT NULL");
        // Exactly once: a receipt source is the collection of at most one instrument.
        entity.HasIndex(x => new { x.CompanyId, x.CollectionSourceId }, "UX_CustomerInstruments_CollectionSource")
            .HasFilter("[CollectionSourceId] IS NOT NULL").IsUnique();
        // The portfolio list's stable order: newest received first.
        entity.HasIndex(x => new { x.CompanyId, x.ReceivedAtUtc, x.Id });
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.DueOn });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ReceiptDraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CollectionSourceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureCustomerInstrumentMovements(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CustomerInstrumentMovementRow>();
        entity.ToTable("CustomerInstrumentMovements", Schema, table =>
        {
            table.HasCheckConstraint("CK_CustomerInstrumentMovements_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [InstrumentId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_CustomerInstrumentMovements_Status",
                "[ToStatus] IN (1, 2, 3, 4, 5, 6) AND ([FromStatus] IS NULL OR [FromStatus] IN (1, 2, 3, 4, 5, 6))");
            table.HasCheckConstraint("CK_CustomerInstrumentMovements_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_CustomerInstrumentMovements_Text",
                "LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.OccurredOn).HasColumnType("date");
        entity.Property(x => x.Note).HasMaxLength(500);
        entity.Property(x => x.RecordedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        // One step per instrument version, whoever races.
        entity.HasIndex(x => new { x.CompanyId, x.InstrumentId, x.Version }).IsUnique();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<CustomerInstrumentRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.InstrumentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureCommands(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FinanceCommandRow>();
        entity.ToTable("Commands", Schema, table =>
        {
            table.HasCheckConstraint("CK_Commands_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([ActorId]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_Commands_KeyHash", "LEN([IdempotencyKeyHash]) = 64");
            // At most one target: a receipt draft or a bank account (FS0). No target only for a recorded
            // terminal refusal whose target does not exist (ADR-011 §7, FIN-UI-1), never for a success.
            table.HasCheckConstraint("CK_Commands_Target", TargetCheck);
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
        entity.HasIndex(x => new { x.CompanyId, x.DraftId });
        entity.HasIndex(x => new { x.CompanyId, x.BankAccountId });
        entity.HasIndex(x => new { x.CompanyId, x.ReversalProposalId });
        entity.HasIndex(x => new { x.CompanyId, x.ManualBankPaymentId });
        entity.HasIndex(x => new { x.CompanyId, x.FinanceAllocationId });
        entity.HasIndex(x => new { x.CompanyId, x.CustomerInstrumentId });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ReceiptDraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<BankAccountRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BankAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ReversalProposalRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReversalProposalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ManualBankPaymentRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ManualBankPaymentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<FinanceAllocationRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FinanceAllocationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<CustomerInstrumentRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CustomerInstrumentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ApplyWriteGuards()
    {
        FinanceAuditEventTenantGuard.Validate(this, companyContext);

        foreach (var entry in ChangeTracker.Entries<IFinanceTenantRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Finans kayıtları silinmez.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Finans kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Finans kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.");
            }

            if (entry.State == EntityState.Modified
                && entry.Entity is ReceiptDraftRow
                && (int)entry.OriginalValues[nameof(ReceiptDraftRow.Stage)]! != ReceiptStages.Draft)
            {
                // FIN-T05a: a recorded source never changes again; its correction is a reversal (FIN-T06).
                throw new InvalidOperationException("Kesin kayda alınmış finans kaynağı değişmez.");
            }

            if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is ManualBankPaymentRow payment
                    && !IsAllowedManualBankPaymentTransition(entry, payment))
                {
                    throw new InvalidOperationException(
                        "Manuel banka ödemesi yalnız taslak, onay ve kesin kayıt geçişleriyle değişir.");
                }

                if (entry.Entity is BankAccountRow bankAccount
                    && !IsAllowedBankAccountTransition(entry, bankAccount))
                {
                    throw new InvalidOperationException(
                        "Banka hesabı yalnız ilk onay veya bekleyen IBAN öneri/onay geçişiyle değişir.");
                }

                if (entry.Entity is FinanceAllocationRow allocation
                    && !IsAllowedAllocationRelease(entry, allocation))
                {
                    throw new InvalidOperationException(
                        "Finans dağıtımı yalnız aktif durumdan sürümlü serbest bırakma ile değişir.");
                }

                if (entry.Entity is CustomerInstrumentRow instrument
                    && !IsAllowedCustomerInstrumentChange(entry, instrument))
                {
                    throw new InvalidOperationException(
                        "Müşteri çeki/senedi yalnız izinli bir durum adımıyla, sürümü bir artarak değişir.");
                }

                string[] allowed = entry.Entity switch
                {
                    ReceiptDraftRow =>
                    [
                        nameof(ReceiptDraftRow.PartyId), nameof(ReceiptDraftRow.CompanyAccountId),
                        nameof(ReceiptDraftRow.TreasuryAccountId), nameof(ReceiptDraftRow.CurrencyId),
                        nameof(ReceiptDraftRow.AmountText), nameof(ReceiptDraftRow.ProfileId),
                        nameof(ReceiptDraftRow.ProfileVersion), nameof(ReceiptDraftRow.OccurredOn),
                        nameof(ReceiptDraftRow.ValueOn), nameof(ReceiptDraftRow.BankReference),
                        nameof(ReceiptDraftRow.Description), nameof(ReceiptDraftRow.UpdatedAtUtc),
                        nameof(ReceiptDraftRow.UpdatedBy), nameof(ReceiptDraftRow.Stage),
                        nameof(ReceiptDraftRow.RecordedAtUtc), nameof(ReceiptDraftRow.RecordedBy),
                        nameof(ReceiptDraftRow.PosCommissionAmountText), nameof(ReceiptDraftRow.PosBankNetAmountText),
                        nameof(ReceiptDraftRow.PosCommissionBearer),
                    ],
                    // Initial approval and D4's two-step IBAN transition; the state-aware guard above
                    // prevents these columns from becoming general-purpose mutable fields.
                    BankAccountRow =>
                    [
                        nameof(BankAccountRow.Status), nameof(BankAccountRow.ApprovedBy),
                        nameof(BankAccountRow.ApprovedAtUtc), nameof(BankAccountRow.UpdatedAtUtc),
                        nameof(BankAccountRow.Iban), nameof(BankAccountRow.PendingIban),
                        nameof(BankAccountRow.PendingIbanProposedBy),
                        nameof(BankAccountRow.PendingIbanProposedAtUtc),
                        nameof(BankAccountRow.PendingIbanReason),
                    ],
                    ReversalProposalRow =>
                    [
                        nameof(ReversalProposalRow.Status), nameof(ReversalProposalRow.DecidedBy),
                        nameof(ReversalProposalRow.DecidedAtUtc), nameof(ReversalProposalRow.DecisionReason),
                        nameof(ReversalProposalRow.ReversalSourceId),
                    ],
                    ManualBankPaymentRow =>
                    [
                        nameof(ManualBankPaymentRow.PartyId), nameof(ManualBankPaymentRow.CompanyAccountId),
                        nameof(ManualBankPaymentRow.TreasuryAccountId), nameof(ManualBankPaymentRow.CurrencyId),
                        nameof(ManualBankPaymentRow.AmountText), nameof(ManualBankPaymentRow.ProfileId),
                        nameof(ManualBankPaymentRow.ProfileVersion), nameof(ManualBankPaymentRow.OccurredOn),
                        nameof(ManualBankPaymentRow.ValueOn), nameof(ManualBankPaymentRow.BankReference),
                        nameof(ManualBankPaymentRow.Description), nameof(ManualBankPaymentRow.LinkedDocumentId),
                        nameof(ManualBankPaymentRow.LinkedDocumentVersion), nameof(ManualBankPaymentRow.Status),
                        nameof(ManualBankPaymentRow.PreparedBy), nameof(ManualBankPaymentRow.PreparedAtUtc),
                        nameof(ManualBankPaymentRow.ApprovedBy), nameof(ManualBankPaymentRow.ApprovedAtUtc),
                        nameof(ManualBankPaymentRow.RecordedBy), nameof(ManualBankPaymentRow.RecordedAtUtc),
                        nameof(ManualBankPaymentRow.BankResultConfirmed),
                    ],
                    FinanceAllocationRow =>
                    [
                        nameof(FinanceAllocationRow.Status), nameof(FinanceAllocationRow.ReleasedBy),
                        nameof(FinanceAllocationRow.ReleasedAtUtc), nameof(FinanceAllocationRow.ReleaseReason),
                    ],
                    FinanceOpenItemRow =>
                    [
                        nameof(FinanceOpenItemRow.OutstandingAmountText), nameof(FinanceOpenItemRow.Status),
                        nameof(FinanceOpenItemRow.UpdatedBy), nameof(FinanceOpenItemRow.UpdatedAtUtc),
                    ],
                    FinanceSourceAllocationStateRow =>
                    [
                        nameof(FinanceSourceAllocationStateRow.UnallocatedAmountText),
                        nameof(FinanceSourceAllocationStateRow.UpdatedBy),
                        nameof(FinanceSourceAllocationStateRow.UpdatedAtUtc),
                    ],
                    // DEC-FIN-016: the facts never change; only the status step and its bank / collection trace. The
                    // state-aware guard above admits only an allowed step. A movement is append-only (no entry here).
                    CustomerInstrumentRow =>
                    [
                        nameof(CustomerInstrumentRow.Status), nameof(CustomerInstrumentRow.TreasuryAccountId),
                        nameof(CustomerInstrumentRow.CollectionSourceId), nameof(CustomerInstrumentRow.StatusChangedOn),
                        nameof(CustomerInstrumentRow.UpdatedBy), nameof(CustomerInstrumentRow.UpdatedAtUtc),
                    ],
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
                        "Finans kaydının yalnız izinli alanı değişir; komut kayıtları değişmez.");
                }
            }
        }
    }

    private static bool IsAllowedManualBankPaymentTransition(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IFinanceTenantRow> entry,
        ManualBankPaymentRow current)
    {
        var originalStatus = (int)entry.OriginalValues[nameof(ManualBankPaymentRow.Status)]!;
        var modified = entry.Properties
            .Where(property => property.IsModified && !property.Metadata.IsConcurrencyToken)
            .Select(property => property.Metadata.Name)
            .ToHashSet(StringComparer.Ordinal);
        bool Only(params string[] names) => modified.All(names.Contains);

        if (originalStatus == ManualBankPaymentStatuses.Draft && current.Status == ManualBankPaymentStatuses.Draft)
        {
            return Only(nameof(ManualBankPaymentRow.PartyId), nameof(ManualBankPaymentRow.CompanyAccountId),
                nameof(ManualBankPaymentRow.TreasuryAccountId), nameof(ManualBankPaymentRow.CurrencyId),
                nameof(ManualBankPaymentRow.AmountText), nameof(ManualBankPaymentRow.ProfileId),
                nameof(ManualBankPaymentRow.ProfileVersion), nameof(ManualBankPaymentRow.OccurredOn),
                nameof(ManualBankPaymentRow.ValueOn), nameof(ManualBankPaymentRow.BankReference),
                nameof(ManualBankPaymentRow.Description), nameof(ManualBankPaymentRow.LinkedDocumentId),
                nameof(ManualBankPaymentRow.LinkedDocumentVersion), nameof(ManualBankPaymentRow.PreparedBy),
                nameof(ManualBankPaymentRow.PreparedAtUtc));
        }

        if (originalStatus == ManualBankPaymentStatuses.Draft && current.Status == ManualBankPaymentStatuses.Approved)
        {
            return Only(nameof(ManualBankPaymentRow.Status), nameof(ManualBankPaymentRow.ApprovedBy),
                       nameof(ManualBankPaymentRow.ApprovedAtUtc))
                   && !string.IsNullOrWhiteSpace(current.ApprovedBy)
                   && !string.Equals(current.PreparedBy, current.ApprovedBy, StringComparison.Ordinal)
                   && current.ApprovedAtUtc is not null;
        }

        return originalStatus == ManualBankPaymentStatuses.Approved
               && current.Status == ManualBankPaymentStatuses.Recorded
               && Only(nameof(ManualBankPaymentRow.Status), nameof(ManualBankPaymentRow.RecordedBy),
                   nameof(ManualBankPaymentRow.RecordedAtUtc), nameof(ManualBankPaymentRow.BankResultConfirmed))
               && !string.IsNullOrWhiteSpace(current.RecordedBy)
               && current.RecordedAtUtc is not null
               && current.BankResultConfirmed;
    }

    /// <summary>
    /// DEC-FIN-016: one allowed status step (<see cref="CustomerInstrumentRules"/>) per save, the version moving by
    /// exactly one; a collected instrument (its collection source set) never changes again.
    /// </summary>
    private static bool IsAllowedCustomerInstrumentChange(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IFinanceTenantRow> entry,
        CustomerInstrumentRow current)
    {
        var originalStatus = (int)entry.OriginalValues[nameof(CustomerInstrumentRow.Status)]!;
        var originalVersion = (long)entry.OriginalValues[nameof(CustomerInstrumentRow.Version)]!;
        var originalCollection = (Guid?)entry.OriginalValues[nameof(CustomerInstrumentRow.CollectionSourceId)];
        var kind = (CustomerInstrumentKind)current.Kind;
        var from = (CustomerInstrumentStatus)originalStatus;
        var to = (CustomerInstrumentStatus)current.Status;
        return originalCollection is null
            && current.Version == checked(originalVersion + 1)
            && Enum.IsDefined(kind) && Enum.IsDefined(from) && Enum.IsDefined(to)
            && CustomerInstrumentRules.IsAllowedStatusChange(kind, from, to);
    }

    private static bool IsAllowedAllocationRelease(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IFinanceTenantRow> entry,
        FinanceAllocationRow current)
    {
        var originalStatus = (int)entry.OriginalValues[nameof(FinanceAllocationRow.Status)]!;
        var originalVersion = (long)entry.OriginalValues[nameof(FinanceAllocationRow.Version)]!;
        return originalStatus == FinanceAllocationStatuses.Active
            && current.Status == FinanceAllocationStatuses.Released
            && current.Version == checked(originalVersion + 1)
            && entry.OriginalValues[nameof(FinanceAllocationRow.ReleasedBy)] is null
            && entry.OriginalValues[nameof(FinanceAllocationRow.ReleasedAtUtc)] is null
            && entry.OriginalValues[nameof(FinanceAllocationRow.ReleaseReason)] is null
            && !string.IsNullOrWhiteSpace(current.ReleasedBy)
            && current.ReleasedAtUtc is not null
            && !string.IsNullOrWhiteSpace(current.ReleaseReason);
    }

    private static bool IsAllowedBankAccountTransition(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IFinanceTenantRow> entry,
        BankAccountRow current)
    {
        const int proposed = 1;
        const int approved = 2;
        var originalStatus = (int)entry.OriginalValues[nameof(BankAccountRow.Status)]!;
        var originalIban = (string?)entry.OriginalValues[nameof(BankAccountRow.Iban)];
        var originalApprovedBy = (string?)entry.OriginalValues[nameof(BankAccountRow.ApprovedBy)];
        var originalApprovedAt = (DateTimeOffset?)entry.OriginalValues[nameof(BankAccountRow.ApprovedAtUtc)];
        var originalPendingIban = (string?)entry.OriginalValues[nameof(BankAccountRow.PendingIban)];
        var originalPendingBy = (string?)entry.OriginalValues[nameof(BankAccountRow.PendingIbanProposedBy)];
        var originalPendingAt = (DateTimeOffset?)entry.OriginalValues[nameof(BankAccountRow.PendingIbanProposedAtUtc)];
        var originalPendingReason = (string?)entry.OriginalValues[nameof(BankAccountRow.PendingIbanReason)];

        var originalPendingEmpty = originalPendingIban is null && originalPendingBy is null
            && originalPendingAt is null && originalPendingReason is null;
        var originalPendingComplete = originalPendingIban is not null && !string.IsNullOrWhiteSpace(originalPendingBy)
            && originalPendingAt is not null && !string.IsNullOrWhiteSpace(originalPendingReason);
        var currentPendingEmpty = current.PendingIban is null && current.PendingIbanProposedBy is null
            && current.PendingIbanProposedAtUtc is null && current.PendingIbanReason is null;
        var currentPendingComplete = current.PendingIban is not null
            && !string.IsNullOrWhiteSpace(current.PendingIbanProposedBy)
            && current.PendingIbanProposedAtUtc is not null && !string.IsNullOrWhiteSpace(current.PendingIbanReason);

        var initialApproval = originalStatus == proposed && current.Status == approved
            && originalPendingEmpty && currentPendingEmpty
            && string.Equals(current.Iban, originalIban, StringComparison.Ordinal)
            && originalApprovedBy is null && originalApprovedAt is null
            && !string.IsNullOrWhiteSpace(current.ApprovedBy) && current.ApprovedAtUtc is not null;

        var proposesIban = originalStatus == approved && current.Status == approved
            && originalPendingEmpty && currentPendingComplete
            && string.Equals(current.Iban, originalIban, StringComparison.Ordinal)
            && string.Equals(current.ApprovedBy, originalApprovedBy, StringComparison.Ordinal)
            && current.ApprovedAtUtc == originalApprovedAt;

        // D4 explicitly keeps one waiting slot whose newest proposal replaces the previous waiting
        // proposal. Keep that approved service semantic without turning the pending columns into
        // generally mutable fields: both sides must be complete, the approved value/approval stay
        // untouched, and at least one pending-proposal fact must actually change.
        var replacesPendingIban = originalStatus == approved && current.Status == approved
            && originalPendingComplete && currentPendingComplete
            && string.Equals(current.Iban, originalIban, StringComparison.Ordinal)
            && string.Equals(current.ApprovedBy, originalApprovedBy, StringComparison.Ordinal)
            && current.ApprovedAtUtc == originalApprovedAt
            && (!string.Equals(current.PendingIban, originalPendingIban, StringComparison.Ordinal)
                || !string.Equals(current.PendingIbanProposedBy, originalPendingBy, StringComparison.Ordinal)
                || current.PendingIbanProposedAtUtc != originalPendingAt
                || !string.Equals(current.PendingIbanReason, originalPendingReason, StringComparison.Ordinal));

        var approvesIban = originalStatus == approved && current.Status == approved
            && originalPendingComplete && currentPendingEmpty
            && string.Equals(current.Iban, originalPendingIban, StringComparison.Ordinal)
            && string.Equals(current.ApprovedBy, originalApprovedBy, StringComparison.Ordinal)
            && current.ApprovedAtUtc == originalApprovedAt;

        return initialApproval || proposesIban || replacesPendingIban || approvesIban;
    }
}
