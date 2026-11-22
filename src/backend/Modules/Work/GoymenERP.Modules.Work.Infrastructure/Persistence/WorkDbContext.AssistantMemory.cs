using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

/// <summary>WP-C6 personal assistant memory: entries, per-person closures and overseer view evidence (AI-D30).</summary>
public sealed partial class WorkDbContext
{
    internal const string AssistantMemoryEntryIdentityCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid + " AND [OwnerUserId] <> " + EmptyGuid
        + " AND [Kind] IN (1, 2) AND LEN([Body]) > 0 AND [Version] >= 1 AND LEN([CorrelationId]) > 0"
        + " AND [UpdatedAtUtc] >= [CreatedAtUtc] AND [ExpiresAtUtc] > [UpdatedAtUtc]";
    internal const string AssistantMemoryEntryStateCheck =
        "([State] = 1 AND [EndedAtUtc] IS NULL AND [EndedByUserId] IS NULL AND [CloseReason] IS NULL)"
        + " OR ([State] = 2 AND [EndedAtUtc] IS NOT NULL AND [EndedByUserId] = [OwnerUserId] AND [CloseReason] IS NULL AND [Version] >= 2)"
        + " OR ([State] = 3 AND [EndedAtUtc] IS NOT NULL AND [EndedByUserId] IS NOT NULL AND [EndedByUserId] <> " + EmptyGuid
        + " AND [CloseReason] IS NOT NULL AND LEN([CloseReason]) > 0 AND [Version] >= 2)";
    internal const string AssistantMemoryUserClosureCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [SubjectUserId] <> " + EmptyGuid + " AND [ChangedByUserId] <> " + EmptyGuid
        + " AND [Version] >= 1 AND LEN([Reason]) > 0 AND LEN([CorrelationId]) > 0";
    internal const string AssistantMemoryOversightViewCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid + " AND [ViewerUserId] <> " + EmptyGuid
        + " AND [SubjectUserId] <> " + EmptyGuid + " AND [EntryCount] >= 0 AND LEN([CorrelationId]) > 0";

    internal DbSet<WorkAssistantMemoryEntryRow> AssistantMemoryEntries => Set<WorkAssistantMemoryEntryRow>();
    internal DbSet<WorkAssistantMemoryUserClosureRow> AssistantMemoryUserClosures => Set<WorkAssistantMemoryUserClosureRow>();
    internal DbSet<WorkAssistantMemoryOversightViewRow> AssistantMemoryOversightViews => Set<WorkAssistantMemoryOversightViewRow>();

    private void ConfigureAssistantMemory(ModelBuilder model)
    {
        var entry = model.Entity<WorkAssistantMemoryEntryRow>();
        entry.ToTable("AssistantMemoryEntries", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkAssistantMemoryEntries_Identity", AssistantMemoryEntryIdentityCheck);
            table.HasCheckConstraint("CK_WorkAssistantMemoryEntries_State", AssistantMemoryEntryStateCheck);
        });
        entry.HasKey(row => new { row.CompanyId, row.Id });
        entry.Property(row => row.Body).HasMaxLength(500).IsRequired();
        entry.Property(row => row.CloseReason).HasMaxLength(500);
        entry.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        entry.Property(row => row.CausationId).HasMaxLength(128);
        entry.Property(row => row.Version).IsConcurrencyToken();
        entry.Property(row => row.RowVersion).IsRowVersion();
        entry.HasIndex(row => new { row.CompanyId, row.OwnerUserId, row.State, row.UpdatedAtUtc, row.Id });
        entry.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entry.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var closure = model.Entity<WorkAssistantMemoryUserClosureRow>();
        closure.ToTable("AssistantMemoryUserClosures", Schema,
            table => table.HasCheckConstraint("CK_WorkAssistantMemoryUserClosures_Identity", AssistantMemoryUserClosureCheck));
        closure.HasKey(row => new { row.CompanyId, row.SubjectUserId });
        closure.Property(row => row.Reason).HasMaxLength(500).IsRequired();
        closure.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        closure.Property(row => row.Version).IsConcurrencyToken();
        closure.Property(row => row.RowVersion).IsRowVersion();
        closure.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        closure.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var view = model.Entity<WorkAssistantMemoryOversightViewRow>();
        view.ToTable("AssistantMemoryOversightViews", Schema,
            table => table.HasCheckConstraint("CK_WorkAssistantMemoryOversightViews_Identity", AssistantMemoryOversightViewCheck));
        view.HasKey(row => new { row.CompanyId, row.Id });
        view.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        view.HasIndex(row => new { row.CompanyId, row.SubjectUserId, row.ViewedAtUtc, row.Id });
        view.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        view.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// Entry: identity and creation time never change; only an active entry changes, by exactly one version; a revision keeps
    /// it active, while deletion or closure keeps its text, kind and times. Closure row: same person, one version step. View
    /// rows are append-only.
    /// </summary>
    private static void GuardAssistantMemoryWrite(EntityEntry<IWorkTenantRow> entry)
    {
        if (entry.Entity is WorkAssistantMemoryOversightViewRow && entry.State != EntityState.Added)
        {
            throw new InvalidOperationException("Work assistant memory views are append-only.");
        }

        if (entry.Entity is WorkAssistantMemoryEntryRow memory && entry.State == EntityState.Modified)
        {
            var original = entry.OriginalValues;
            var ended = memory.State != 1;
            if (original.GetValue<Guid>(nameof(memory.Id)) != memory.Id
                || original.GetValue<Guid>(nameof(memory.OwnerUserId)) != memory.OwnerUserId
                || original.GetValue<DateTimeOffset>(nameof(memory.CreatedAtUtc)) != memory.CreatedAtUtc
                || original.GetValue<int>(nameof(memory.State)) != 1
                || memory.State is not (1 or 2 or 3)
                || memory.Version != checked(original.GetValue<long>(nameof(memory.Version)) + 1)
                || (ended && (original.GetValue<string>(nameof(memory.Body)) != memory.Body
                    || original.GetValue<int>(nameof(memory.Kind)) != memory.Kind
                    || original.GetValue<DateTimeOffset>(nameof(memory.UpdatedAtUtc)) != memory.UpdatedAtUtc
                    || original.GetValue<DateTimeOffset>(nameof(memory.ExpiresAtUtc)) != memory.ExpiresAtUtc)))
            {
                throw new InvalidOperationException("A work assistant memory entry changes only while active, by one version step, and keeps its identity.");
            }
        }

        if (entry.Entity is WorkAssistantMemoryUserClosureRow closure && entry.State == EntityState.Modified
            && (entry.OriginalValues.GetValue<Guid>(nameof(closure.SubjectUserId)) != closure.SubjectUserId
                || closure.Version != checked(entry.OriginalValues.GetValue<long>(nameof(closure.Version)) + 1)))
        {
            throw new InvalidOperationException("A work assistant memory closure moves by one version step for the same person.");
        }
    }
}
