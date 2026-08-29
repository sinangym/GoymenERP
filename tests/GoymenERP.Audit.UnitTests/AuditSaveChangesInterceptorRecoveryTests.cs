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
    public async Task TrackedAddedTenantMismatch_IsRejectedBeforePersistence()
    {
        var companyA = Guid.NewGuid();
        var companyContext = new CompanyContext();
        companyContext.Set(companyA);
        var collector = new AuditEventCollector();
        await using var dbContext = CreateProbeContext(companyContext, collector);
        var auditEvent = AuditEventFactory.Create(
            CreateDraft(Guid.NewGuid(), "tracked-added-mismatch"));
        dbContext.AuditEvents.Add(auditEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kaydı aktif şirket bağlamıyla eşleşmelidir.",
            exception.Message);
        Assert.Equal(EntityState.Added, dbContext.Entry(auditEvent).State);
        Assert.Empty(await dbContext.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task TrackedAddedWithoutResolvedCompany_IsRejectedBeforePersistence()
    {
        var companyContext = new CompanyContext();
        var collector = new AuditEventCollector();
        await using var dbContext = CreateProbeContext(companyContext, collector);
        var auditEvent = AuditEventFactory.Create(
            CreateDraft(Guid.NewGuid(), "tracked-added-unresolved"));
        dbContext.AuditEvents.Add(auditEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kaydı yazmak için aktif şirket bağlamı zorunludur.",
            exception.Message);
        Assert.Equal(EntityState.Added, dbContext.Entry(auditEvent).State);
        Assert.Empty(await dbContext.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task TrackedModifiedAndDeletedAuditEvents_AreRejected()
    {
        var companyId = Guid.NewGuid();
        var companyContext = new CompanyContext();
        companyContext.Set(companyId);
        var collector = new AuditEventCollector();
        await using var dbContext = CreateProbeContext(companyContext, collector);
        var auditEvent = AuditEventFactory.Create(
            CreateDraft(companyId, "tracked-immutable"));
        dbContext.AuditEvents.Add(auditEvent);
        await dbContext.SaveChangesAsync();

        var reason = dbContext.Entry(auditEvent).Property(nameof(AuditEvent.Reason));
        reason.CurrentValue = "Değiştirilemez audit nedeni";
        reason.IsModified = true;

        var modifiedException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kayıtları değiştirilemez veya silinemez.",
            modifiedException.Message);

        dbContext.Entry(auditEvent).State = EntityState.Unchanged;
        dbContext.Remove(auditEvent);

        var deletedException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kayıtları değiştirilemez veya silinemez.",
            deletedException.Message);
    }

    [Fact]
    public async Task CollectorTenantMismatch_CleansAuditStateAndAllowsSameScopeRetry()
    {
        var companyA = Guid.NewGuid();
        var companyContext = new CompanyContext();
        companyContext.Set(companyA);
        var collector = new AuditEventCollector();
        await using var dbContext = CreateProbeContext(companyContext, collector);
        var probe = new ProbeEntity(Guid.NewGuid(), "tenant-mismatch");
        dbContext.Probes.Add(probe);
        collector.Enqueue(CreateDraft(Guid.NewGuid(), "mismatch"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kaydı aktif şirket bağlamıyla eşleşmelidir.",
            exception.Message);
        Assert.Empty(collector.Pending);
        Assert.Empty(dbContext.ChangeTracker.Entries<AuditEvent>());
        Assert.Equal(EntityState.Added, dbContext.Entry(probe).State);

        collector.Enqueue(CreateDraft(companyA, "valid-retry"));
        await dbContext.SaveChangesAsync();

        Assert.Empty(collector.Pending);
        Assert.Equal(EntityState.Unchanged, dbContext.Entry(probe).State);
        Assert.Single(await dbContext.AuditEvents.ToListAsync());
        Assert.Single(await dbContext.Probes.ToListAsync());
    }

    [Fact]
    public void CollectorTenantMismatch_SynchronousSaveAlsoCleansAuditState()
    {
        var companyA = Guid.NewGuid();
        var companyContext = new CompanyContext();
        companyContext.Set(companyA);
        var collector = new AuditEventCollector();
        using var dbContext = CreateProbeContext(companyContext, collector);
        collector.Enqueue(CreateDraft(Guid.NewGuid(), "sync-mismatch"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => dbContext.SaveChanges());

        Assert.Equal(
            "Audit kaydı aktif şirket bağlamıyla eşleşmelidir.",
            exception.Message);
        Assert.Empty(collector.Pending);
        Assert.Empty(dbContext.ChangeTracker.Entries<AuditEvent>());
    }

    [Fact]
    public async Task UnresolvedCompany_CleansAuditStateAndAllowsSameScopeRetry()
    {
        var companyId = Guid.NewGuid();
        var companyContext = new CompanyContext();
        var collector = new AuditEventCollector();
        await using var dbContext = CreateProbeContext(companyContext, collector);
        var probe = new ProbeEntity(Guid.NewGuid(), "unresolved-company");
        dbContext.Probes.Add(probe);
        collector.Enqueue(CreateDraft(companyId, "unresolved"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kaydı yazmak için aktif şirket bağlamı zorunludur.",
            exception.Message);
        Assert.Empty(collector.Pending);
        Assert.Empty(dbContext.ChangeTracker.Entries<AuditEvent>());
        Assert.Equal(EntityState.Added, dbContext.Entry(probe).State);

        companyContext.Set(companyId);
        collector.Enqueue(CreateDraft(companyId, "resolved-retry"));
        await dbContext.SaveChangesAsync();

        Assert.Empty(collector.Pending);
        Assert.Single(await dbContext.AuditEvents.ToListAsync());
        Assert.Single(await dbContext.Probes.ToListAsync());
    }

    [Fact]
    public async Task BatchFactoryFailure_AttachesNoEventsAndAllowsRetry()
    {
        var companyId = Guid.NewGuid();
        var companyContext = new CompanyContext();
        companyContext.Set(companyId);
        var collector = new AuditEventCollector();
        await using var dbContext = CreateProbeContext(companyContext, collector);
        var probe = new ProbeEntity(Guid.NewGuid(), "partial-factory-failure");
        dbContext.Probes.Add(probe);
        collector.Enqueue(CreateDraft(companyId, "valid-before-failure"));
        collector.Enqueue(CreateDraft(companyId, "invalid-json", beforeJson: "not-json"));

        await Assert.ThrowsAsync<ArgumentException>(() => dbContext.SaveChangesAsync());

        Assert.Empty(collector.Pending);
        Assert.Empty(dbContext.ChangeTracker.Entries<AuditEvent>());
        Assert.Equal(EntityState.Added, dbContext.Entry(probe).State);

        collector.Enqueue(CreateDraft(companyId, "valid-after-failure"));
        await dbContext.SaveChangesAsync();

        var auditEvent = Assert.Single(await dbContext.AuditEvents.ToListAsync());
        Assert.Equal("valid-after-failure", auditEvent.CorrelationId);
        Assert.Single(await dbContext.Probes.ToListAsync());
    }

    private static AuditEventDraft CreateDraft(
        Guid companyId,
        string correlationId,
        string beforeJson = "{}")
    {
        return new AuditEventDraft(
            companyId,
            "test.event.v1",
            "Test.Entity",
            Guid.NewGuid().ToString("D"),
            "Create",
            null,
            1,
            AuditActorTypes.System,
            "audit-lifecycle-test",
            "test.permission",
            ["TEST_ROLE"],
            beforeJson,
            "{}",
            "Audit lifecycle birim testi",
            null,
            AuditAiInvolvement.None,
            null,
            SourceChannels.System,
            null,
            null,
            correlationId,
            null,
            $"idempotency-{correlationId}");
    }

    private static AuditProbeDbContext CreateProbeContext(
        ICompanyContext companyContext,
        AuditEventCollector collector)
    {
        var interceptor = new AuditSaveChangesInterceptor(collector, companyContext);
        var options = new DbContextOptionsBuilder<AuditProbeDbContext>()
            .UseInMemoryDatabase($"audit-lifecycle-{Guid.NewGuid():N}")
            .AddInterceptors(interceptor)
            .Options;
        return new AuditProbeDbContext(options);
    }

    private sealed class AuditProbeDbContext(
        DbContextOptions<AuditProbeDbContext> options)
        : DbContext(options)
    {
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        public DbSet<ProbeEntity> Probes => Set<ProbeEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureAuditEvent(excludeFromMigrations: false);
            modelBuilder.Entity<ProbeEntity>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            });
        }
    }

    private sealed class ProbeEntity(Guid id, string name)
    {
        public Guid Id { get; private set; } = id;

        public string Name { get; private set; } = name;
    }
}
