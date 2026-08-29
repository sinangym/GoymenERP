using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Audit.UnitTests;

public sealed class AuditSaveChangesInterceptorRecoveryTests
{
    [Fact]
    public async Task MismatchedCollectorDraft_IsClearedAndSameScopeRetryPersistsOnce()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(CompanyA);
        var collector = new AuditEventCollector();
        var interceptor = new AuditSaveChangesInterceptor(collector, companyContext);
        await using var dbContext = CreateContext(interceptor);
        var probe = new ProbeEntity(Guid.NewGuid(), "mismatch-retry");
        dbContext.Probes.Add(probe);
        collector.Enqueue(CreateDraft(CompanyB, "audit-mismatch"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kaydı aktif şirket bağlamıyla eşleşmelidir.",
            exception.Message);
        Assert.Empty(dbContext.ChangeTracker.Entries<AuditEvent>());
        Assert.Equal(EntityState.Added, dbContext.Entry(probe).State);

        collector.Enqueue(CreateDraft(CompanyA, "audit-retry"));
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, await dbContext.Probes.CountAsync());
        var persistedAudit = Assert.Single(await dbContext.AuditEvents.ToListAsync());
        Assert.Equal(CompanyA, persistedAudit.CompanyId);
        Assert.Equal("audit-retry", persistedAudit.CorrelationId);
    }

    [Fact]
    public async Task UnresolvedContext_IsClearedAndRetryAfterResolutionPersistsOnce()
    {
        var companyContext = new CompanyContext();
        var collector = new AuditEventCollector();
        var interceptor = new AuditSaveChangesInterceptor(collector, companyContext);
        await using var dbContext = CreateContext(interceptor);
        var probe = new ProbeEntity(Guid.NewGuid(), "unresolved-retry");
        dbContext.Probes.Add(probe);
        collector.Enqueue(CreateDraft(CompanyA, "audit-unresolved"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kaydı yazmak için aktif şirket bağlamı zorunludur.",
            exception.Message);
        Assert.Empty(dbContext.ChangeTracker.Entries<AuditEvent>());
        Assert.Equal(EntityState.Added, dbContext.Entry(probe).State);

        companyContext.Set(CompanyA);
        collector.Enqueue(CreateDraft(CompanyA, "audit-resolved-retry"));
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, await dbContext.Probes.CountAsync());
        var persistedAudit = Assert.Single(await dbContext.AuditEvents.ToListAsync());
        Assert.Equal("audit-resolved-retry", persistedAudit.CorrelationId);
    }

    [Fact]
    public async Task FactoryFailure_DetachesPartiallyAttachedEventsAndPreservesBusinessRetry()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(CompanyA);
        var collector = new AuditEventCollector();
        var interceptor = new AuditSaveChangesInterceptor(collector, companyContext);
        await using var dbContext = CreateContext(interceptor);
        var probe = new ProbeEntity(Guid.NewGuid(), "factory-retry");
        dbContext.Probes.Add(probe);
        collector.Enqueue(CreateDraft(CompanyA, "audit-valid-before-failure"));
        collector.Enqueue(
            CreateDraft(CompanyA, "audit-invalid-json") with
            {
                BeforeJson = "not-json",
            });

        await Assert.ThrowsAsync<ArgumentException>(() => dbContext.SaveChangesAsync());

        Assert.Empty(dbContext.ChangeTracker.Entries<AuditEvent>());
        Assert.Equal(EntityState.Added, dbContext.Entry(probe).State);

        collector.Enqueue(CreateDraft(CompanyA, "audit-after-factory-retry"));
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, await dbContext.Probes.CountAsync());
        var persistedAudit = Assert.Single(await dbContext.AuditEvents.ToListAsync());
        Assert.Equal("audit-after-factory-retry", persistedAudit.CorrelationId);
    }

    [Fact]
    public async Task DirectAddedAuditEvent_WithMismatchedCompany_IsRejectedBeforeWrite()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(CompanyA);
        var collector = new AuditEventCollector();
        var interceptor = new AuditSaveChangesInterceptor(collector, companyContext);
        await using var dbContext = CreateContext(interceptor);
        var mismatched = AuditEventFactory.Create(
            CreateDraft(CompanyB, "audit-direct-mismatch"));
        dbContext.AuditEvents.Add(mismatched);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kaydı aktif şirket bağlamıyla eşleşmelidir.",
            exception.Message);
        Assert.Equal(EntityState.Added, dbContext.Entry(mismatched).State);
        Assert.Empty(await dbContext.AuditEvents.AsNoTracking().ToListAsync());
    }

    private static ProbeDbContext CreateContext(AuditSaveChangesInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseInMemoryDatabase($"audit-interceptor-recovery-{Guid.NewGuid():N}")
            .AddInterceptors(interceptor)
            .Options;
        return new ProbeDbContext(options);
    }

    private static AuditEventDraft CreateDraft(Guid companyId, string correlationId)
    {
        return new AuditEventDraft(
            companyId,
            "audit.interceptor-recovery.probe.v1",
            "AuditInterceptorRecoveryProbe",
            Guid.NewGuid().ToString("N"),
            "Probe",
            null,
            null,
            AuditActorTypes.System,
            "audit-interceptor-recovery-test",
            "audit.events.read",
            ["SYSTEM_ADMINISTRATOR"],
            "{}",
            "{}",
            "Audit interceptor recovery unit acceptance",
            null,
            AuditAiInvolvement.None,
            null,
            SourceChannels.System,
            null,
            null,
            correlationId,
            null,
            $"idem-{correlationId}");
    }

    private static readonly Guid CompanyA = Guid.Parse(
        "10000000-0000-0000-0000-000000000001");

    private static readonly Guid CompanyB = Guid.Parse(
        "10000000-0000-0000-0000-000000000002");

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options)
        : DbContext(options)
    {
        public DbSet<ProbeEntity> Probes => Set<ProbeEntity>();

        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProbeEntity>().HasKey(x => x.Id);
            modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        }
    }

    private sealed class ProbeEntity(Guid id, string name)
    {
        public Guid Id { get; private set; } = id;

        public string Name { get; private set; } = name;
    }
}
