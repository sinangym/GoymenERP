using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
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

    internal DbSet<ReversalProposalRow> ReversalProposals => Set<ReversalProposalRow>();

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
        ConfigureReversalProposals(modelBuilder);
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
            table.HasCheckConstraint("CK_MoneyEvents_Kind", "[Kind] IN (1, 2)");
            table.HasCheckConstraint("CK_MoneyEvents_Text",
                "LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 "
                + "AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
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

    internal const string ReversalProposalDecisionCheck =
        "([Status] = 0 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [ReversalSourceId] IS NULL) OR "
        + "([Status] = 1 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL "
        + "AND [DecisionReason] IS NULL AND [ReversalSourceId] IS NOT NULL AND [ReversalSourceId] <> [OriginalSourceId] "
        + "AND [ProposedBy] <> [DecidedBy]) OR "
        + "([Status] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL "
        + "AND [DecisionReason] IS NOT NULL AND LEN([DecisionReason]) > 0 AND [ReversalSourceId] IS NULL "
        + "AND [ProposedBy] <> [DecidedBy])";

    /// <summary>The command record's target rule; the migration that introduced it uses the same text.</summary>
    internal const string TargetCheck =
        "([DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL) OR "
        + "([BankAccountId] IS NOT NULL AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [ReversalProposalId] IS NULL) OR "
        + "([ReversalProposalId] IS NOT NULL AND [ReversalProposalId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL) OR "
        + "([DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')";

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
                    ],
                    // The one approval; name and currency never change.
                    BankAccountRow =>
                    [
                        nameof(BankAccountRow.Status), nameof(BankAccountRow.ApprovedBy),
                        nameof(BankAccountRow.ApprovedAtUtc), nameof(BankAccountRow.UpdatedAtUtc),
                    ],
                    ReversalProposalRow =>
                    [
                        nameof(ReversalProposalRow.Status), nameof(ReversalProposalRow.DecidedBy),
                        nameof(ReversalProposalRow.DecidedAtUtc), nameof(ReversalProposalRow.DecisionReason),
                        nameof(ReversalProposalRow.ReversalSourceId),
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
}
