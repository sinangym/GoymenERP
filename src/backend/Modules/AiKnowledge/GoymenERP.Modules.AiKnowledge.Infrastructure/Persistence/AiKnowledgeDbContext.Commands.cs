using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;

public sealed partial class AiKnowledgeDbContext
{
    public const string CommandSchema = "ai";
    internal const string CommandRecordCheck = "[CompanyId] <> " + EmptyGuid + " AND [PreviewId] <> " + EmptyGuid
        + " AND LEN(LTRIM(RTRIM([ActorId]))) > 0 AND [State] BETWEEN 1 AND 9 AND [Version] > 0 AND ISJSON([RecordJson]) = 1"
        + " AND DATALENGTH([BindingSha256]) = 64 AND [BindingSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'";
    internal const string CommandEventCheck = "[CompanyId] <> " + EmptyGuid + " AND [EventId] <> " + EmptyGuid
        + " AND LEN(LTRIM(RTRIM([ActorId]))) > 0 AND [Kind] BETWEEN 1 AND 10 AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0"
        + " AND ([PreviewId] IS NULL OR [PreviewId] <> " + EmptyGuid + ")"
        + " AND ([PreviewHash] IS NULL OR (" + "DATALENGTH([PreviewHash]) = 64 AND [PreviewHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'" + "))";

    internal DbSet<AiCommandRecordRow> CommandRecords => Set<AiCommandRecordRow>();
    internal DbSet<AiCommandEventRow> CommandEvents => Set<AiCommandEventRow>();

    private void ConfigureCommands(ModelBuilder modelBuilder)
    {
        var record = modelBuilder.Entity<AiCommandRecordRow>();
        record.ToTable("CommandRecords", CommandSchema, table => table.HasCheckConstraint("CK_AiCommandRecords_Identity", CommandRecordCheck));
        record.HasKey(row => new { row.CompanyId, row.PreviewId });
        record.Property(row => row.ActorId).HasMaxLength(128).IsRequired();
        record.Property(row => row.RecordJson).IsRequired();
        record.Property(row => row.BindingSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength().IsRequired();
        record.Property(row => row.Version).IsConcurrencyToken();
        record.Property(row => row.RowVersion).IsRowVersion();
        record.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        record.HasOne<AiKnowledgeCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);

        var auditEvent = modelBuilder.Entity<AiCommandEventRow>();
        auditEvent.ToTable("CommandEvents", CommandSchema, table => table.HasCheckConstraint("CK_AiCommandEvents_Identity", CommandEventCheck));
        auditEvent.HasKey(row => new { row.CompanyId, row.EventId });
        auditEvent.Property(row => row.ActorId).HasMaxLength(128).IsRequired();
        auditEvent.Property(row => row.ToolId).HasMaxLength(128);
        auditEvent.Property(row => row.ToolVersion).HasMaxLength(64);
        auditEvent.Property(row => row.PreviewHash).HasColumnType("char(64)").IsUnicode(false).IsFixedLength();
        auditEvent.Property(row => row.ReasonCode).HasMaxLength(128);
        auditEvent.Property(row => row.ResultVersion).HasMaxLength(256);
        auditEvent.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        auditEvent.Property(row => row.CausationId).HasMaxLength(128);
        auditEvent.HasIndex(row => new { row.CompanyId, row.PreviewId, row.OccurredAtUtc, row.EventId });
        auditEvent.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        auditEvent.HasOne<AiKnowledgeCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        auditEvent.HasOne<AiCommandRecordRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.PreviewId }).OnDelete(DeleteBehavior.Restrict);
    }
}
