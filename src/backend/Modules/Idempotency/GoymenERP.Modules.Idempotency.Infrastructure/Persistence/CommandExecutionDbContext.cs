using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Domain.Retention;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Persistence;

public sealed class CommandExecutionDbContext(
    DbContextOptions<CommandExecutionDbContext> options,
    ICompanyContext companyContext)
    : DbContext(options)
{
    public DbSet<CommandExecution> CommandExecutions => Set<CommandExecution>();

    public DbSet<IdempotencyRetentionPurgeRecord> RetentionPurgeRecords => Set<IdempotencyRetentionPurgeRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ConfigureCommandExecution(excludeFromMigrations: false);
        modelBuilder.Entity<CommandExecution>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        modelBuilder.ConfigureIdempotencyRetentionPurgeRecord();
        modelBuilder.Entity<IdempotencyRetentionPurgeRecord>()
            .HasQueryFilter(record => record.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges()
    {
        throw ReadOnlyContextException();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        throw ReadOnlyContextException();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        throw ReadOnlyContextException();
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        throw ReadOnlyContextException();
    }

    private static InvalidOperationException ReadOnlyContextException()
    {
        return new InvalidOperationException(
            "Idempotency kayıtları yalnız iş komutunun atomik transaction'ı içinde oluşturulabilir.");
    }
}
