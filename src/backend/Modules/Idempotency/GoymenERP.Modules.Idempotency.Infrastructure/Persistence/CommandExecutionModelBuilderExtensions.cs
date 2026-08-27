using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Persistence;

public static class CommandExecutionModelBuilderExtensions
{
    public static void ConfigureCommandExecution(
        this ModelBuilder modelBuilder,
        bool excludeFromMigrations)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var entity = modelBuilder.Entity<CommandExecution>();
        entity.ToTable(
            "CommandExecutions",
            "idempotency",
            table =>
            {
                table.ExcludeFromMigrations(excludeFromMigrations);
                table.HasCheckConstraint(
                    "CK_CommandExecutions_CompanyId_NotEmpty",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_CommandExecutions_Hash_Lengths",
                    "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                table.HasCheckConstraint(
                    "CK_CommandExecutions_ResponseJson_Valid",
                    "ISJSON([ResponseJson]) = 1");
                table.HasCheckConstraint(
                    "CK_CommandExecutions_HttpStatusCode_Valid",
                    "[HttpStatusCode] BETWEEN 100 AND 599");
                table.HasCheckConstraint(
                    "CK_CommandExecutions_SchemaVersion_Positive",
                    "[SchemaVersion] > 0");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.CommandName).HasMaxLength(160).IsRequired();
        entity.Property(x => x.IdempotencyKeyHash)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.RequestHash)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Outcome).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.OriginalCorrelationId).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new
        {
            x.CompanyId,
            x.CommandName,
            x.IdempotencyKeyHash,
        })
            .IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.CompletedAtUtc, x.Id });
    }
}
