using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

public sealed partial class FinanceDbContext
{
    internal DbSet<FinancePartyBalanceTransferRow> PartyBalanceTransfers => Set<FinancePartyBalanceTransferRow>();
    internal DbSet<FinancePartyBalanceAssignmentRow> PartyBalanceAssignments => Set<FinancePartyBalanceAssignmentRow>();
    internal DbSet<FinancePartyBalanceTransferCommandRow> PartyBalanceTransferCommands => Set<FinancePartyBalanceTransferCommandRow>();

    internal const string PartyBalanceTransferIdentityCheck = "[Id] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [PredecessorPartyId] <> [SuccessorPartyId] AND [PredecessorAccountId] <> [SuccessorAccountId] "
        + "AND [PredecessorPartyId] <> '00000000-0000-0000-0000-000000000000' AND [SuccessorPartyId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [PredecessorAccountId] <> '00000000-0000-0000-0000-000000000000' AND [SuccessorAccountId] <> '00000000-0000-0000-0000-000000000000'";
    internal const string PartyBalanceTransferEvidenceCheck = "ISJSON([OwnerProofJson]) = 1 AND ISJSON([EvidenceJson]) = 1 AND ISJSON([CurrenciesJson]) = 1 "
        + "AND ISJSON([AssignmentSnapshotJson]) = 1 AND LEN([SnapshotVersion]) = 64 AND LEN([Reason]) > 0 AND LEN([RecordedBy]) > 0";

    private void ConfigurePartyBalanceTransfers(ModelBuilder model)
    {
        var transfer = model.Entity<FinancePartyBalanceTransferRow>();
        transfer.ToTable("PartyBalanceTransfers", Schema, table =>
        {
            table.HasCheckConstraint("CK_PartyBalanceTransfers_Identity", PartyBalanceTransferIdentityCheck);
            table.HasCheckConstraint("CK_PartyBalanceTransfers_Evidence", PartyBalanceTransferEvidenceCheck);
        });
        transfer.HasKey(x => new { x.CompanyId, x.Id });
        transfer.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        transfer.Property(x => x.EvidenceJson).HasColumnType("nvarchar(max)");
        transfer.Property(x => x.OwnerProofJson).HasColumnType("nvarchar(max)");
        transfer.Property(x => x.CurrenciesJson).HasColumnType("nvarchar(max)");
        transfer.Property(x => x.AssignmentSnapshotJson).HasColumnType("nvarchar(max)");
        transfer.Property(x => x.SnapshotVersion).HasMaxLength(64).IsUnicode(false);
        transfer.Property(x => x.OccurredOn).HasColumnType("date");
        transfer.Property(x => x.Reason).HasMaxLength(1000);
        transfer.Property(x => x.RecordedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        transfer.HasIndex(x => new { x.CompanyId, x.PredecessorAccountId, x.OccurredOn });
        transfer.HasIndex(x => new { x.CompanyId, x.SuccessorAccountId, x.OccurredOn });

        var assignment = model.Entity<FinancePartyBalanceAssignmentRow>();
        assignment.ToTable("PartyBalanceAssignments", Schema, table =>
            table.HasCheckConstraint("CK_PartyBalanceAssignments_Identity", "[Kind] IN (1,2,3) AND [Version] >= 1 "
                + "AND [SourceId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OriginalPartyId] <> '00000000-0000-0000-0000-000000000000' AND [OriginalAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrentPartyId] <> '00000000-0000-0000-0000-000000000000' AND [CurrentAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrentAccountId] <> [OriginalAccountId] AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([AvailableAmountText]) > 0 AND [AvailableAmountText] NOT LIKE '%[^0-9.]%' "
                + "AND [AvailableAmountText] NOT LIKE '%.%.%' AND [AvailableAmountText] NOT LIKE '.%' AND [AvailableAmountText] NOT LIKE '%.'"));
        assignment.HasKey(x => new { x.CompanyId, x.Kind, x.SourceId, x.CurrentAccountId });
        assignment.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        assignment.Property(x => x.Version).IsConcurrencyToken();
        assignment.Property(x => x.AvailableAmountText).HasColumnType("varchar(max)");
        assignment.HasIndex(x => new { x.CompanyId, x.CurrentAccountId, x.Kind });
        assignment.HasOne<FinancePartyBalanceTransferRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.LastTransferId })
            .OnDelete(DeleteBehavior.Restrict);

        const string allocationPair = "([AllocationPartyId] IS NULL AND [AllocationAccountId] IS NULL) OR "
            + "([AllocationPartyId] IS NOT NULL AND [AllocationAccountId] IS NOT NULL "
            + "AND [AllocationPartyId] <> '00000000-0000-0000-0000-000000000000' "
            + "AND [AllocationAccountId] <> '00000000-0000-0000-0000-000000000000')";
        model.Entity<FinanceAllocationRow>().ToTable("Allocations", Schema,
            table => table.HasCheckConstraint("CK_Allocations_Beneficiary", allocationPair));
        model.Entity<FinancePaymentAllocationRow>().ToTable("PaymentAllocations", Schema,
            table => table.HasCheckConstraint("CK_PaymentAllocations_Beneficiary", allocationPair));

        var command = model.Entity<FinancePartyBalanceTransferCommandRow>();
        command.ToTable("PartyBalanceTransferCommands", Schema, table =>
            table.HasCheckConstraint("CK_PartyBalanceTransferCommands_Evidence", "LEN([KeyHash]) = 64 AND LEN([PayloadHash]) = 64 "
                + "AND LEN([ActorId]) > 0 AND ISJSON([ResultJson]) = 1"));
        command.HasKey(x => new { x.CompanyId, x.KeyHash });
        command.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        command.Property(x => x.KeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        command.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false);
        command.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        command.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
    }
}
