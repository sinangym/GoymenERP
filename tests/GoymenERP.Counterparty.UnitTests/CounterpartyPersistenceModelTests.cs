using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyPersistenceModelTests
{
    [Fact]
    public void Model_UsesExpectedSchemaAndTenantSafeIndexes()
    {
        using var dbContext = CreateContext();
        var partyEntity = dbContext.Model.FindEntityType(typeof(Party));
        var accountEntity = dbContext.Model.FindEntityType(typeof(CompanyPartyAccount));
        var auditEntity = dbContext.Model.FindEntityType(typeof(AuditEvent));

        Assert.NotNull(partyEntity);
        Assert.NotNull(accountEntity);
        Assert.NotNull(auditEntity);
        Assert.Equal("counterparty", partyEntity!.GetSchema());
        Assert.Equal("Parties", partyEntity.GetTableName());
        Assert.Equal("counterparty", accountEntity!.GetSchema());
        Assert.Equal("CompanyPartyAccounts", accountEntity.GetTableName());
        Assert.Equal("audit", auditEntity!.GetSchema());
        Assert.Equal("AuditEvents", auditEntity.GetTableName());
        Assert.NotEmpty(auditEntity.GetDeclaredQueryFilters());

        Assert.Contains(
            partyEntity.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(Party.Code)]));
        Assert.Contains(
            accountEntity.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name)
                    .SequenceEqual([
                        nameof(CompanyPartyAccount.CompanyId),
                        nameof(CompanyPartyAccount.PartyId),
                    ]));

        Assert.Contains(
            accountEntity.GetForeignKeys(),
            foreignKey => foreignKey.Properties
                    .Select(property => property.Name)
                    .SequenceEqual([nameof(CompanyPartyAccount.PartyId)])
                && foreignKey.PrincipalEntityType.GetTableName() == "Parties"
                && foreignKey.PrincipalEntityType.GetSchema() == "counterparty");
        Assert.Contains(
            accountEntity.GetForeignKeys(),
            foreignKey => foreignKey.Properties
                    .Select(property => property.Name)
                    .SequenceEqual([nameof(CompanyPartyAccount.CompanyId)])
                && foreignKey.PrincipalEntityType.GetTableName() == "Companies"
                && foreignKey.PrincipalEntityType.GetSchema() == "organization");
    }

    [Fact]
    public void Model_DoesNotIntroduceDeferredSensitiveIdentifiers()
    {
        using var dbContext = CreateContext();
        var propertyNames = dbContext.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("Vkn", propertyNames);
        Assert.DoesNotContain("Tckn", propertyNames);
        Assert.DoesNotContain("Mersis", propertyNames);
        Assert.DoesNotContain("Iban", propertyNames);
    }

    [Fact]
    public void Migration_IsDiscoverableAndSnapshotMatchesRuntimeModel()
    {
        using var dbContext = CreateContext();
        Assert.Contains(
            "20260828123500_InitialCounterparty",
            dbContext.Database.GetMigrations());
        Assert.False(dbContext.Database.HasPendingModelChanges());
    }

    [Fact]
    public void SaveChanges_RejectsPhysicalPartyDeleteBeforeDatabaseCall()
    {
        using var dbContext = CreateContext();
        var party = new Party(
            Guid.NewGuid(),
            PartyCode.Parse("CAR-000001"),
            PartyKind.LegalEntity,
            "Göymen Test Cari");

        dbContext.Parties.Attach(party);
        dbContext.Parties.Remove(party);

        var exception = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("fiziksel", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveChanges_RejectsPhysicalCompanyAccountDeleteBeforeDatabaseCall()
    {
        using var dbContext = CreateContext();
        var account = new CompanyPartyAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PartyRole.Customer);

        dbContext.CompanyPartyAccounts.Attach(account);
        dbContext.CompanyPartyAccounts.Remove(account);

        var exception = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("fiziksel", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CounterpartyDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CounterpartyDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpCounterpartyTests;Trusted_Connection=True;")
            .Options;
        return new CounterpartyDbContext(options, new UnresolvedCompanyContext());
    }

    private sealed class UnresolvedCompanyContext : ICompanyContext
    {
        public bool IsResolved => false;

        public Guid CompanyId => Guid.Empty;

        public void Set(Guid companyId) =>
            throw new InvalidOperationException("Persistence model testi company context çözmez.");
    }
}
