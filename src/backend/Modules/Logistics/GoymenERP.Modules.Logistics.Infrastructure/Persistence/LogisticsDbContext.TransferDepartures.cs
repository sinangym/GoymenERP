using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

internal sealed class TransferDepartureStateRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid TransferId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid SourceLocationId { get; set; }
    public int Association { get; set; }
    public Guid? OfficialDocumentId { get; set; }
    public long? InitialOfficialDocumentVersion { get; set; }
    public DateTimeOffset? OfficialLinkedAtUtc { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset? ExitWeighingAtUtc { get; set; }
    public DateTimeOffset? VehicleExitAtUtc { get; set; }
    public long? NoFurtherExitVersion { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class TransferDepartureEventRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid TransferId { get; set; }
    public long Version { get; set; }
    public string Kind { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public int? WeighingDirection { get; set; }
    public decimal? FirstWeighingKg { get; set; }
    public decimal? SecondWeighingKg { get; set; }
    public decimal? NetWeighingKg { get; set; }
}

internal sealed class TransferDepartureAttemptRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public Guid TransferId { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed partial class LogisticsDbContext
{
    internal const string TransferDepartureStatesIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [TransferId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [SourceLocationId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0";
    internal const string TransferDepartureStatesAssociationCheck = "[Association] IN (0,1,2)";
    internal const string TransferDepartureStatesOfficialBindingCheck = "([Association] = 2 AND [OfficialDocumentId] IS NOT NULL AND [OfficialDocumentId] <> '00000000-0000-0000-0000-000000000000' AND [InitialOfficialDocumentVersion] IS NOT NULL AND [InitialOfficialDocumentVersion] > 0 AND [OfficialLinkedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET,[OfficialLinkedAtUtc]) = 0) OR ([Association] <> 2 AND [OfficialDocumentId] IS NULL AND [InitialOfficialDocumentVersion] IS NULL AND [OfficialLinkedAtUtc] IS NULL)";
    internal const string TransferDepartureStatesFenceCheck = "[NoFurtherExitVersion] IS NULL OR ([NoFurtherExitVersion] > 0 AND [NoFurtherExitVersion] <= [Version] AND [ExitWeighingAtUtc] IS NULL AND [VehicleExitAtUtc] IS NULL)";
    internal const string TransferDepartureStatesUtcCheck = "DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND ([ExitWeighingAtUtc] IS NULL OR DATEPART(TZOFFSET,[ExitWeighingAtUtc]) = 0) AND ([VehicleExitAtUtc] IS NULL OR DATEPART(TZOFFSET,[VehicleExitAtUtc]) = 0)";
    internal const string TransferDepartureEventsIdentityCheck = "[Version] > 0 AND [Kind] IN ('AssociationRecorded','ExitWeighing','VehicleExit','NoFurtherExit')";
    internal const string TransferDepartureEventsAuditCheck = "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0 AND DATEPART(TZOFFSET,[OccurredAtUtc]) = 0";
    internal const string TransferDepartureEventsWeighingCheck = "([Kind] = 'ExitWeighing' AND [WeighingDirection] IS NOT NULL AND [FirstWeighingKg] IS NOT NULL AND [SecondWeighingKg] IS NOT NULL AND [NetWeighingKg] IS NOT NULL AND [WeighingDirection] IN (1,2) AND [FirstWeighingKg] > 0 AND [SecondWeighingKg] > 0 AND [NetWeighingKg] > 0 AND (([WeighingDirection] = 1 AND [SecondWeighingKg] > [FirstWeighingKg]) OR ([WeighingDirection] = 2 AND [FirstWeighingKg] > [SecondWeighingKg])) AND [NetWeighingKg] = ABS([SecondWeighingKg] - [FirstWeighingKg])) OR ([Kind] <> 'ExitWeighing' AND [WeighingDirection] IS NULL AND [FirstWeighingKg] IS NULL AND [SecondWeighingKg] IS NULL AND [NetWeighingKg] IS NULL)";
    internal const string TransferDepartureAttemptsJsonCheck = "ISJSON([ResultJson]) = 1";
    internal const string TransferDepartureAttemptsCommandCheck = "[CommandName] IN ('logistics.transfer-departure.record.v1','logistics.transfer-departure.confirm-no-exit.v1')";
    internal DbSet<TransferDepartureStateRow> TransferDepartureStates => Set<TransferDepartureStateRow>();
    internal DbSet<TransferDepartureEventRow> TransferDepartureEvents => Set<TransferDepartureEventRow>();
    internal DbSet<TransferDepartureAttemptRow> TransferDepartureAttempts => Set<TransferDepartureAttemptRow>();
    private void ConfigureTransferDepartures(ModelBuilder modelBuilder)
    {
        var state = modelBuilder.Entity<TransferDepartureStateRow>();
        state.ToTable("TransferDepartureStates", Schema, table =>
        {
            table.HasCheckConstraint("CK_TransferDepartureStates_Identity", TransferDepartureStatesIdentityCheck);
            table.HasCheckConstraint("CK_TransferDepartureStates_Association", TransferDepartureStatesAssociationCheck);
            table.HasCheckConstraint("CK_TransferDepartureStates_OfficialBinding", TransferDepartureStatesOfficialBindingCheck);
            table.HasCheckConstraint("CK_TransferDepartureStates_Fence", TransferDepartureStatesFenceCheck);
            table.HasCheckConstraint("CK_TransferDepartureStates_Utc", TransferDepartureStatesUtcCheck);
        });
        state.HasKey(x => new { x.CompanyId, x.TransferId });
        state.HasIndex(x => new { x.CompanyId, x.DocumentId }).IsUnique();
        state.HasOne<TransferOutgoingDocumentRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.DocumentId }).OnDelete(DeleteBehavior.Restrict);
        state.Property(x => x.Version).IsConcurrencyToken();
        state.Property(x => x.RowVersion).IsRowVersion();
        state.Property(x => x.CreatedBy).HasMaxLength(128);
        state.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var item = modelBuilder.Entity<TransferDepartureEventRow>();
        item.ToTable("TransferDepartureEvents", Schema, table =>
        {
            table.HasCheckConstraint("CK_TransferDepartureEvents_Identity", TransferDepartureEventsIdentityCheck);
            table.HasCheckConstraint("CK_TransferDepartureEvents_Audit", TransferDepartureEventsAuditCheck);
            table.HasCheckConstraint("CK_TransferDepartureEvents_Weighing", TransferDepartureEventsWeighingCheck);
        });
        item.HasKey(x => new { x.CompanyId, x.TransferId, x.Version });
        item.HasOne<TransferDepartureStateRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.TransferId }).OnDelete(DeleteBehavior.Restrict);
        item.Property(x => x.Kind).HasMaxLength(32).IsUnicode(false);
        item.Property(x => x.RecordedBy).HasMaxLength(128);
        item.Property(x => x.Reason).HasMaxLength(4000);
        item.Property(x => x.CorrelationId).HasMaxLength(128);
        item.Property(x => x.FirstWeighingKg).HasPrecision(18, 3);
        item.Property(x => x.SecondWeighingKg).HasPrecision(18, 3);
        item.Property(x => x.NetWeighingKg).HasPrecision(18, 3);
        item.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<TransferDepartureAttemptRow>();
        attempt.ToTable("TransferDepartureAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_TransferDepartureAttempts_Json", TransferDepartureAttemptsJsonCheck);
            table.HasCheckConstraint("CK_TransferDepartureAttempts_Command", TransferDepartureAttemptsCommandCheck);
        });
        attempt.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        attempt.HasOne<TransferDepartureStateRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.TransferId }).OnDelete(DeleteBehavior.Restrict);
        attempt.Property(x => x.CommandName).HasMaxLength(96).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        attempt.Property(x => x.CorrelationId).HasMaxLength(128);
        attempt.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }

    private void GuardTransferDepartureChange(EntityEntry<ILogisticsTenantRow> entry)
    {
        if (entry.Entity is not TransferDepartureStateRow row) return;
        if (entry.State == EntityState.Added)
        {
            var initial = ChangeTracker.Entries<TransferDepartureEventRow>().Where(item => item.State == EntityState.Added
                && item.Entity.CompanyId == row.CompanyId && item.Entity.TransferId == row.TransferId).ToArray();
            if (row.Version != 1 || row.Association is not (1 or 2) || row.NoFurtherExitVersion is not null
                || row.ExitWeighingAtUtc is not null || row.VehicleExitAtUtc is not null || initial.Length != 1
                || initial[0].Entity.Version != 1 || initial[0].Entity.Kind != "AssociationRecorded"
                || initial[0].Entity.OccurredAtUtc != row.CreatedAtUtc)
                throw new InvalidOperationException("New departure state requires its one explicit association event.");
            return;
        }
        var oldVersion = entry.Property(nameof(TransferDepartureStateRow.Version)).OriginalValue;
        var events = ChangeTracker.Entries<TransferDepartureEventRow>().Where(item => item.State == EntityState.Added
            && item.Entity.CompanyId == row.CompanyId && item.Entity.TransferId == row.TransferId && item.Entity.Version == row.Version).ToArray();
        if (oldVersion is not long previous || previous == long.MaxValue || row.Version != previous + 1 || events.Length != 1)
            throw new InvalidOperationException("Transfer departure changes require one next immutable owner event.");
        var expected = events[0].Entity.Kind switch
        {
            "ExitWeighing" => nameof(TransferDepartureStateRow.ExitWeighingAtUtc),
            "VehicleExit" => nameof(TransferDepartureStateRow.VehicleExitAtUtc),
            "NoFurtherExit" => nameof(TransferDepartureStateRow.NoFurtherExitVersion),
            _ => throw new InvalidOperationException("Association events may only initialize a new departure state."),
        };
        var changedFacts = new[] { nameof(TransferDepartureStateRow.ExitWeighingAtUtc), nameof(TransferDepartureStateRow.VehicleExitAtUtc),
            nameof(TransferDepartureStateRow.NoFurtherExitVersion) }.Where(name => entry.Property(name).IsModified).ToArray();
        if (changedFacts.Length != 1 || changedFacts[0] != expected
            || expected == nameof(TransferDepartureStateRow.NoFurtherExitVersion) && row.NoFurtherExitVersion != row.Version
            || expected != nameof(TransferDepartureStateRow.NoFurtherExitVersion)
                && entry.Property(expected).CurrentValue is DateTimeOffset actual && actual != events[0].Entity.OccurredAtUtc)
            throw new InvalidOperationException("Each departure fact must match its one immutable event kind and time.");
        foreach (var name in new[] { nameof(TransferDepartureStateRow.ExitWeighingAtUtc), nameof(TransferDepartureStateRow.VehicleExitAtUtc),
                     nameof(TransferDepartureStateRow.NoFurtherExitVersion) })
        {
            var property = entry.Property(name);
            if (property.IsModified && (property.OriginalValue is not null || property.CurrentValue is null))
                throw new InvalidOperationException("Transfer departure and no-exit facts are set once and never reopen.");
        }
    }
}
