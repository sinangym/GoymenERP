using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;

public sealed partial class AiKnowledgeDbContext
{
    internal DbSet<AiFeedbackEventRow> FeedbackEvents => Set<AiFeedbackEventRow>();
    internal DbSet<AiFeedbackIdentityRow> FeedbackIdentities => Set<AiFeedbackIdentityRow>();
    internal const string FeedbackEventCheck = "[CompanyId] <> " + EmptyGuid + " AND [AnswerId] <> " + EmptyGuid
        + " AND [Version] > 0 AND (([Version] = 1 AND [PriorVersion] IS NULL) OR ([Version] > 1 AND [PriorVersion] IS NOT NULL AND [PriorVersion] = [Version] - 1))"
        + " AND [Kind] BETWEEN 1 AND 4 AND ([Kind] <> 4 OR [PriorVersion] IS NOT NULL) AND (([Kind] = 1 AND [ReasonCode] IS NULL) OR ([ReasonCode] IS NOT NULL AND DATALENGTH([ReasonCode]) = DATALENGTH(RTRIM([ReasonCode])) AND [ReasonCode] COLLATE Latin1_General_100_BIN2 IN"
        + " ('incorrect_information','incorrect_or_missing_source','incomplete_answer','outdated_information','other')))"
        + " AND DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET,[RetainUntilUtc]) = 0"
        + " AND [RetainUntilUtc] = DATEADD(day,90,[CreatedAtUtc])";
    internal const string FeedbackIdentityCheck = "[CompanyId] <> " + EmptyGuid + " AND [AnswerId] <> " + EmptyGuid
        + " AND [Version] > 0 AND [ActorId] <> " + EmptyGuid + " AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0"
        + " AND DATALENGTH([IdempotencyKeySha256]) = 64 AND [IdempotencyKeySha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'"
        + " AND DATALENGTH([RequestSha256]) = 64 AND [RequestSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'";
    private void ConfigureFeedback(ModelBuilder modelBuilder)
    {
        var feedback = modelBuilder.Entity<AiFeedbackEventRow>();
        feedback.ToTable("FeedbackEvents", CommandSchema, table => table.HasCheckConstraint("CK_AiFeedbackEvents_Event", FeedbackEventCheck));
        feedback.HasKey(row => new { row.CompanyId, row.AnswerId, row.Version });
        feedback.Property(row => row.ReasonCode).HasMaxLength(64);
        feedback.HasIndex(row => new { row.CompanyId, row.CreatedAtUtc, row.AnswerId, row.Version });
        feedback.HasIndex(row => new { row.CompanyId, row.RetainUntilUtc });
        feedback.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        feedback.HasOne<AiAnswerReceiptRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.AnswerId }).OnDelete(DeleteBehavior.Restrict);
        feedback.HasOne<AiFeedbackEventRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.AnswerId, row.PriorVersion })
            .HasPrincipalKey(row => new { row.CompanyId, row.AnswerId, row.Version }).OnDelete(DeleteBehavior.Restrict);
        var identity = modelBuilder.Entity<AiFeedbackIdentityRow>();
        identity.ToTable("FeedbackIdentities", CommandSchema, table => table.HasCheckConstraint("CK_AiFeedbackIdentities_Identity", FeedbackIdentityCheck));
        identity.HasKey(row => new { row.CompanyId, row.AnswerId, row.Version });
        identity.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        identity.Property(row => row.IdempotencyKeySha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength().IsRequired();
        identity.Property(row => row.RequestSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength().IsRequired();
        identity.HasIndex(row => new { row.CompanyId, row.ActorId, row.IdempotencyKeySha256 }).IsUnique();
        identity.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        identity.HasOne<AiFeedbackEventRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.AnswerId, row.Version }).OnDelete(DeleteBehavior.Restrict);
    }
}
