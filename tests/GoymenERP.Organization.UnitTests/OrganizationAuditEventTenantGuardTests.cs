using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Organization.UnitTests;

public sealed class OrganizationAuditEventTenantGuardTests
{
    [Fact]
    public void Model_HasCompanyScopedAuditEventQueryFilter()
    {
        using var dbContext = CreateDbContext(ResolvedCompanyContext(Guid.NewGuid()));
        var auditEntity = dbContext.Model.FindEntityType(typeof(AuditEvent));

        Assert.NotNull(auditEntity);
        Assert.NotEmpty(auditEntity!.GetDeclaredQueryFilters());
    }

    [Fact]
    public async Task DirectAddedAuditEvent_WithEmptyCompany_IsRejected()
    {
        var companyContext = ResolvedCompanyContext(Guid.NewGuid());
        await using var dbContext = CreateDbContext(companyContext);
        var auditEvent = Assert.IsType<AuditEvent>(
            Activator.CreateInstance(typeof(AuditEvent), nonPublic: true));
        dbContext.Set<AuditEvent>().Add(auditEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal("Audit kaydı CompanyId olmadan yazılamaz.", exception.Message);
    }

    [Fact]
    public async Task DirectAddedAuditEvent_WithoutCompanyContext_IsRejected()
    {
        await using var dbContext = CreateDbContext(new CompanyContext());
        dbContext.Set<AuditEvent>().Add(CreateAuditEvent(Guid.NewGuid()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kaydı yazmak için aktif şirket bağlamı zorunludur.",
            exception.Message);
    }

    [Fact]
    public async Task DirectAddedAuditEvent_ForDifferentCompany_IsRejected()
    {
        await using var dbContext = CreateDbContext(
            ResolvedCompanyContext(Guid.NewGuid()));
        dbContext.Set<AuditEvent>().Add(CreateAuditEvent(Guid.NewGuid()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kaydı aktif şirket bağlamıyla eşleşmelidir.",
            exception.Message);
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public void ExistingAuditEvent_CannotBeModifiedOrDeleted(EntityState state)
    {
        var companyId = Guid.NewGuid();
        using var dbContext = CreateDbContext(ResolvedCompanyContext(companyId));
        dbContext.Attach(CreateAuditEvent(companyId)).State = state;

        var exception = Assert.Throws<InvalidOperationException>(
            () => dbContext.SaveChanges());

        Assert.Equal(
            "Audit kayıtları değiştirilemez veya silinemez.",
            exception.Message);
    }

    private static OrganizationDbContext CreateDbContext(ICompanyContext companyContext)
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"organization-audit-guard-{Guid.NewGuid():N}")
            .Options;
        return new OrganizationDbContext(
            options,
            companyContext,
            new OrganizationBootstrapWriteScope());
    }

    private static CompanyContext ResolvedCompanyContext(Guid companyId)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(companyId);
        return companyContext;
    }

    private static AuditEvent CreateAuditEvent(Guid companyId)
    {
        var envelope = new AuditEventEnvelope(
            Guid.NewGuid(),
            companyId,
            DateTimeOffset.UtcNow,
            "organization.audit-tenant-guard.unit.v1",
            "OrganizationAuditTenantGuardProbe",
            Guid.NewGuid().ToString("N"),
            "Probe",
            null,
            null,
            AuditActorTypes.System,
            "organization-audit-tenant-guard-unit-test",
            "organization.sites.read",
            "[\"SYSTEM_ADMINISTRATOR\"]",
            "{}",
            "{}",
            "Organization AuditEvent tenant guard birim testi",
            null,
            AuditAiInvolvement.None,
            null,
            SourceChannels.System,
            null,
            null,
            $"organization-audit-unit-{Guid.NewGuid():N}",
            null,
            new string('A', 64),
            AuditOutcomes.Succeeded,
            1);

        return AuditEvent.Create(
            new AuditEventData(envelope, AuditEventIntegrity.Compute(envelope)));
    }
}
