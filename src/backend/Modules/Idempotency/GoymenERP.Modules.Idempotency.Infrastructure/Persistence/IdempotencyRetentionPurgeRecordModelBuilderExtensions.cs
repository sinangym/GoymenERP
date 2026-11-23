using GoymenERP.Modules.Idempotency.Domain.Retention;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Persistence;

/// <summary>
/// The append-only evidence table <c>idempotency.RetentionPurgeRecords</c> (OPEN-021 / NS-12). Rows are written only by
/// the retention purge store in the delete transaction; the migration adds a trigger that refuses UPDATE and DELETE.
/// </summary>
public static class IdempotencyRetentionPurgeRecordModelBuilderExtensions
{
    public const string TableName = "RetentionPurgeRecords";
    public const string Schema = "idempotency";

    public const string CompanyIdNotEmptyCheckName = "CK_RetentionPurgeRecords_CompanyId_NotEmpty";
    public const string CompanyIdNotEmptyCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000'";

    public const string IdentifiersNotEmptyCheckName = "CK_RetentionPurgeRecords_Identifiers_NotEmpty";
    public const string IdentifiersNotEmptyCheck =
        "[Id] <> '00000000-0000-0000-0000-000000000000' AND [RunId] <> '00000000-0000-0000-0000-000000000000'";

    public const string DeletedCountPositiveCheckName = "CK_RetentionPurgeRecords_DeletedCount_Positive";
    public const string DeletedCountPositiveCheck = "[DeletedCount] > 0";

    public const string UtcInstantsCheckName = "CK_RetentionPurgeRecords_Utc_Instants";
    public const string UtcInstantsCheck =
        "DATEPART(TZOFFSET, [CutoffUtc]) = 0 AND DATEPART(TZOFFSET, [RecordedAtUtc]) = 0 AND [CutoffUtc] < [RecordedAtUtc]";

    public const string RetentionPeriodFormatCheckName = "CK_RetentionPurgeRecords_RetentionPeriod_Format";
    public const string RetentionPeriodFormatCheck =
        "[RetentionPeriod] LIKE 'P%' AND LEN([RetentionPeriod]) > 1 AND [RetentionPeriod] NOT LIKE '%[^0-9PYMD]%'";

    public const string PolicyRefNotBlankCheckName = "CK_RetentionPurgeRecords_PolicyRef_NotBlank";
    public const string PolicyRefNotBlankCheck = "LEN(LTRIM(RTRIM([PolicyRef]))) > 0";

    public static void ConfigureIdempotencyRetentionPurgeRecord(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var entity = modelBuilder.Entity<IdempotencyRetentionPurgeRecord>();
        entity.ToTable(
            TableName,
            Schema,
            table =>
            {
                table.HasCheckConstraint(CompanyIdNotEmptyCheckName, CompanyIdNotEmptyCheck);
                table.HasCheckConstraint(IdentifiersNotEmptyCheckName, IdentifiersNotEmptyCheck);
                table.HasCheckConstraint(DeletedCountPositiveCheckName, DeletedCountPositiveCheck);
                table.HasCheckConstraint(UtcInstantsCheckName, UtcInstantsCheck);
                table.HasCheckConstraint(RetentionPeriodFormatCheckName, RetentionPeriodFormatCheck);
                table.HasCheckConstraint(PolicyRefNotBlankCheckName, PolicyRefNotBlankCheck);
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.RetentionPeriod)
            .HasMaxLength(IdempotencyRetentionPeriod.MaxTextLength)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.PolicyRef)
            .HasMaxLength(IdempotencyRetentionPurgeRecord.PolicyRefMaxLength)
            .IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.RecordedAtUtc, x.Id });
        entity.HasIndex(x => x.RunId);
    }
}
