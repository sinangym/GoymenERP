using Microsoft.EntityFrameworkCore;
using GoymenERP.Modules.AiKnowledge.Domain;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;

public sealed partial class AiKnowledgeDbContext
{
    internal DbSet<AiAnswerReceiptRow> AnswerReceipts => Set<AiAnswerReceiptRow>();
    internal DbSet<AiAnswerReceiptIdentityRow> AnswerReceiptIdentities => Set<AiAnswerReceiptIdentityRow>();
    internal const string AnswerReceiptCheck = "[CompanyId] <> " + EmptyGuid + " AND [AnswerId] <> " + EmptyGuid
        + " AND (" + "DATALENGTH([AnswerSha256]) = 64 AND [AnswerSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'"
        + ") AND (DATALENGTH([CitationsSha256]) = 64 AND [CitationsSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')"
        + " AND [Feature] = 'Panel' AND LEN(LTRIM(RTRIM([ModelDeployment]))) > 0 AND LEN(LTRIM(RTRIM([PolicyVersion]))) > 0"
        + " AND DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET,[RetainUntilUtc]) = 0"
        + " AND [RetainUntilUtc] = DATEADD(day,90,[CreatedAtUtc])";
    internal const string AnswerIdentityCheck = "[CompanyId] <> " + EmptyGuid + " AND [AnswerId] <> " + EmptyGuid
        + " AND LEN(LTRIM(RTRIM([ActorId]))) > 0 AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0";

    private void ConfigureAnswerReceipts(ModelBuilder modelBuilder)
    {
        var receipt = modelBuilder.Entity<AiAnswerReceiptRow>();
        receipt.ToTable("AnswerReceipts", CommandSchema, table => table.HasCheckConstraint("CK_AiAnswerReceipts_Evidence", AnswerReceiptCheck));
        receipt.HasKey(row => new { row.CompanyId, row.AnswerId });
        receipt.Property(row => row.AnswerSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength().IsRequired();
        receipt.Property(row => row.CitationsSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength().IsRequired();
        receipt.Property(row => row.Feature).HasMaxLength(AiAnswerReceiptRulesV1.MetadataMaximumLength).IsRequired();
        receipt.Property(row => row.ModelDeployment).HasMaxLength(AiAnswerReceiptRulesV1.MetadataMaximumLength).IsRequired();
        receipt.Property(row => row.PolicyVersion).HasMaxLength(AiAnswerReceiptRulesV1.MetadataMaximumLength).IsRequired();
        receipt.HasIndex(row => new { row.CompanyId, row.RetainUntilUtc, row.AnswerId });
        receipt.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        receipt.HasOne<AiKnowledgeCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        var identity = modelBuilder.Entity<AiAnswerReceiptIdentityRow>();
        identity.ToTable("AnswerReceiptIdentities", CommandSchema, table => table.HasCheckConstraint("CK_AiAnswerReceiptIdentities_Identity", AnswerIdentityCheck));
        identity.HasKey(row => new { row.CompanyId, row.AnswerId });
        identity.Property(row => row.ActorId).HasMaxLength(AiAnswerReceiptRulesV1.MetadataMaximumLength).IsRequired();
        identity.Property(row => row.CorrelationId).HasMaxLength(AiAnswerReceiptRulesV1.MetadataMaximumLength).IsRequired();
        identity.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        identity.HasOne<AiAnswerReceiptRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.AnswerId }).OnDelete(DeleteBehavior.Restrict);
    }
}
