using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;

public sealed partial class AiKnowledgeDbContext
{
    // Only the current scheduled-task command may save its explicitly registered entities in this context.
    internal HashSet<object>? ScheduledTaskWriteOwner { get; set; }
    internal bool ScheduledTaskSaveAuthorized { get; set; }
    internal void RequireScheduledTaskWriteOwnership()
    {
        if (ScheduledTaskWriteOwner is not { } owner) return;
        if (!ScheduledTaskSaveAuthorized || ChangeTracker.Entries().Any(entry => entry.State is not (EntityState.Unchanged or EntityState.Detached)
            && !owner.Contains(entry.Entity))) throw new InvalidOperationException("A scheduled-task command cannot save foreign tracked work.");
    }
    internal DbSet<AiScheduledTaskRow> ScheduledTasks => Set<AiScheduledTaskRow>();
    internal DbSet<AiScheduledTaskCommandRow> ScheduledTaskCommands => Set<AiScheduledTaskCommandRow>();
    internal DbSet<AiScheduledTaskRunRow> ScheduledTaskRuns => Set<AiScheduledTaskRunRow>();
    internal DbSet<AiScheduledTaskEventRow> ScheduledTaskEvents => Set<AiScheduledTaskEventRow>();

    private void ConfigureScheduledTasks(ModelBuilder model)
    {
        var task = model.Entity<AiScheduledTaskRow>();
        task.ToTable("ScheduledTasks", CommandSchema, table => table.HasCheckConstraint("CK_AiScheduledTasks_Identity",
            "[CompanyId] <> " + EmptyGuid + " AND [TaskId] <> " + EmptyGuid
            + " AND LEN([OwnerActorId]) > 0 AND [Purpose] = 'work.morning-summary.v1' AND [Effect] = 1"
            + " AND [State] BETWEEN 1 AND 4 AND [Version] > 0 AND LEN([ScheduleVersion]) > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]"));
        task.HasKey(row => new { row.CompanyId, row.TaskId });
        task.Property(row => row.OwnerActorId).HasMaxLength(128).IsRequired();
        task.Property(row => row.Purpose).HasMaxLength(128).IsRequired();
        task.Property(row => row.ScheduleVersion).HasMaxLength(256).IsRequired();
        task.Property(row => row.Version).IsConcurrencyToken(); task.Property(row => row.RowVersion).IsRowVersion();
        task.HasIndex(row => new { row.CompanyId, row.OwnerActorId, row.State, row.TaskId });
        task.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        task.HasOne<AiKnowledgeCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        var command = model.Entity<AiScheduledTaskCommandRow>();
        command.ToTable("ScheduledTaskCommands", CommandSchema, table => table.HasCheckConstraint("CK_AiScheduledTaskCommands_Identity",
            "[CompanyId] <> " + EmptyGuid + " AND [TaskId] <> " + EmptyGuid
            + " AND LEN([ActorId]) > 0 AND LEN([Command]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResponseJson]) = 1"
            + " AND DATALENGTH([KeyHash]) = 64 AND [KeyHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'"
            + " AND DATALENGTH([PayloadHash]) = 64 AND [PayloadHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'"));
        command.HasKey(row => new { row.CompanyId, row.ActorId, row.KeyHash });
        command.Property(row => row.ActorId).HasMaxLength(128).IsRequired();
        command.Property(row => row.KeyHash).HasColumnType("char(64)").IsUnicode(false).IsFixedLength();
        command.Property(row => row.PayloadHash).HasColumnType("char(64)").IsUnicode(false).IsFixedLength();
        command.Property(row => row.Command).HasMaxLength(64).IsRequired();
        command.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        command.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        command.HasOne<AiKnowledgeCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        var run = model.Entity<AiScheduledTaskRunRow>();
        run.ToTable("ScheduledTaskRuns", CommandSchema, table => table.HasCheckConstraint("CK_AiScheduledTaskRuns_Receipt",
            "[CompanyId] <> " + EmptyGuid + " AND [TaskId] <> " + EmptyGuid + " AND ISJSON([ReceiptJson]) = 1"));
        run.HasKey(row => new { row.CompanyId, row.TaskId, row.SlotUtc });
        run.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        run.HasOne<AiScheduledTaskRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.TaskId }).OnDelete(DeleteBehavior.Restrict);
        var evidence = model.Entity<AiScheduledTaskEventRow>();
        evidence.ToTable("ScheduledTaskEvents", CommandSchema, table => table.HasCheckConstraint("CK_AiScheduledTaskEvents_Identity",
            "[CompanyId] <> " + EmptyGuid + " AND [EventId] <> " + EmptyGuid + " AND [TaskId] <> " + EmptyGuid
            + " AND LEN([ActorId]) > 0 AND LEN([Kind]) > 0 AND LEN([CorrelationId]) > 0"));
        evidence.HasKey(row => new { row.CompanyId, row.EventId });
        evidence.Property(row => row.ActorId).HasMaxLength(128).IsRequired();
        evidence.Property(row => row.Kind).HasMaxLength(64).IsRequired();
        evidence.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        evidence.HasIndex(row => new { row.CompanyId, row.TaskId, row.RecordedAtUtc, row.EventId });
        evidence.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        evidence.HasOne<AiKnowledgeCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }

    private static Guid ScheduledTaskWriteCompany(EntityEntry entry)
    {
        if (entry.Entity is AiScheduledTaskRow task)
        {
            if (entry.State == EntityState.Added && task.Version != 1) throw new InvalidOperationException("A task starts at version one.");
            if (entry.State == EntityState.Modified &&
                (!Equals(entry.Property(nameof(task.OwnerActorId)).OriginalValue, task.OwnerActorId)
                 || !Equals(entry.Property(nameof(task.Purpose)).OriginalValue, task.Purpose)
                 || !Equals(entry.Property(nameof(task.Effect)).OriginalValue, task.Effect)
                 || !Equals(entry.Property(nameof(task.CreatedAtUtc)).OriginalValue, task.CreatedAtUtc)
                 || entry.Property(nameof(task.State)).OriginalValue is 3
                 || entry.Property(nameof(task.Version)).OriginalValue is not long version || version == long.MaxValue || task.Version != version + 1))
                throw new InvalidOperationException("Task replacement keeps its owner and purpose and advances one nonterminal version.");
            return task.CompanyId;
        }
        var companyId = entry.Entity switch
        {
            AiScheduledTaskCommandRow row => row.CompanyId,
            AiScheduledTaskRunRow row => row.CompanyId,
            AiScheduledTaskEventRow row => row.CompanyId,
            _ => throw new InvalidOperationException("Unknown AI knowledge record."),
        };
        if (entry.State != EntityState.Added) throw new InvalidOperationException("Scheduled task receipts and events are append-only.");
        return companyId;
    }
}
