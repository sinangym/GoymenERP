using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

public sealed partial class FinanceDbContext
{
    internal DbSet<CashExchangeBookValueProposalRow> CashExchangeBookValueProposals => Set<CashExchangeBookValueProposalRow>();
    internal DbSet<CashExchangeBookValueDecisionRow> CashExchangeBookValueDecisions => Set<CashExchangeBookValueDecisionRow>();
    internal DbSet<CashExchangeBookValueCommandRow> CashExchangeBookValueCommands => Set<CashExchangeBookValueCommandRow>();

    private void ConfigureCashExchangeBookValues(ModelBuilder model)
    {
        var proposal = model.Entity<CashExchangeBookValueProposalRow>();
        proposal.ToTable("CashExchangeBookValueProposals", Schema, table => {
            table.HasTrigger("TR_CashExchangeBookValueProposals_Immutable");
            table.HasCheckConstraint("CK_CashExchangeBookValueProposals_Proof",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND ISJSON([TransferSourceJson]) = 1 AND LEN([TransferSourceHash]) = 64 AND ISJSON([PreparationJson]) = 1 AND LEN([PreparedBy]) > 0");
        });
        proposal.HasKey(x => new { x.CompanyId, x.Id });
        proposal.HasAlternateKey(x => new { x.CompanyId, x.Id, x.TransferId, x.PreparedBy });
        proposal.Property(x => x.Id).ValueGeneratedNever();
        proposal.Property(x => x.TransferSourceHash).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        proposal.Property(x => x.PreparedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        proposal.HasIndex(x => new { x.CompanyId, x.TransferId });
        proposal.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        proposal.HasOne<CashTransferRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.TransferId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        var decision = model.Entity<CashExchangeBookValueDecisionRow>();
        decision.ToTable("CashExchangeBookValueDecisions", Schema, table => {
            table.HasTrigger("TR_CashExchangeBookValueDecisions_Immutable");
            table.HasCheckConstraint("CK_CashExchangeBookValueDecisions_Checker",
                "LEN([PreparedBy]) > 0 AND LEN([DecidedBy]) > 0 AND [PreparedBy] <> [DecidedBy] AND (([Approved] = 1 AND [Reason] IS NULL) OR ([Approved] = 0 AND LEN([Reason]) > 0))");
        });
        decision.HasKey(x => new { x.CompanyId, x.ProposalId });
        decision.Property(x => x.PreparedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        decision.Property(x => x.DecidedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        decision.Property(x => x.Reason).HasMaxLength(1000);
        decision.HasIndex(x => new { x.CompanyId, x.TransferId }).IsUnique().HasFilter("[Approved] = 1");
        decision.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        decision.HasOne<CashExchangeBookValueProposalRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ProposalId, x.TransferId, x.PreparedBy })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id, x.TransferId, x.PreparedBy }).OnDelete(DeleteBehavior.Restrict);

        var command = model.Entity<CashExchangeBookValueCommandRow>();
        command.ToTable("CashExchangeBookValueCommands", Schema, table => {
            table.HasTrigger("TR_CashExchangeBookValueCommands_Immutable");
            table.HasCheckConstraint("CK_CashExchangeBookValueCommands_Receipt",
                "LEN([KeyHash]) = 64 AND LEN([PayloadHash]) = 64 AND LEN([ActorId]) > 0 AND ISJSON([ResultJson]) = 1");
        });
        command.HasKey(x => new { x.CompanyId, x.Command, x.KeyHash });
        command.Property(x => x.Command).HasMaxLength(64).IsUnicode(false);
        command.Property(x => x.KeyHash).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        command.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        command.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        command.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        command.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }

    private void GuardCashExchangeBookValues()
    {
        if (ChangeTracker.Entries<CashExchangeBookValueProposalRow>().Any(x => x.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<CashExchangeBookValueDecisionRow>().Any(x => x.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<CashExchangeBookValueCommandRow>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Döviz defter değeri kaynağı, ayrı karar ve makbuzlar değişmez kayıtlardır.");
    }
}
