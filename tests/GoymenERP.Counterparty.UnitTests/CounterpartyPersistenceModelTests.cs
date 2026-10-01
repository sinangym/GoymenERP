using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyPersistenceModelTests
{
    [Fact]
    public void Model_UsesExpectedSchemaAndTenantSafeIndexes()
    {
        var companyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));
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
        var queryFilter = Assert.Single(auditEntity.GetDeclaredQueryFilters());
        var expression = Assert.IsAssignableFrom<Expression<Func<AuditEvent, bool>>>(
            queryFilter.Expression);
        AssertCompanyScopedAuditFilter(expression, typeof(CounterpartyDbContext));

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
    public void Model_StoresTheTaxIdentityWithAFilteredNonUniqueLookupAndOnePendingChangePerParty()
    {
        var companyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var partyEntity = model.FindEntityType(typeof(Party))!;
        var number = partyEntity.FindProperty(nameof(Party.TaxIdentityNumber))!;
        Assert.True(number.IsNullable);
        Assert.Equal(11, number.GetMaxLength());
        Assert.False(number.IsUnicode());
        // AT-009: a duplicate is a warning, so the lookup index is NOT unique; its filter is written out explicitly.
        var lookup = Assert.Single(partyEntity.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Party.TaxIdentityNumber)]));
        Assert.False(lookup.IsUnique);
        Assert.Equal("[TaxIdentityNumber] IS NOT NULL", lookup.GetFilter());
        Assert.Contains(partyEntity.GetCheckConstraints(), check => check.Name == "CK_Parties_TaxIdentityNumber");

        var requestEntity = model.FindEntityType(typeof(PartyTaxIdentityChangeRequest))!;
        Assert.Equal("counterparty", requestEntity.GetSchema());
        Assert.Equal("PartyTaxIdentityChangeRequests", requestEntity.GetTableName());
        var pending = Assert.Single(requestEntity.GetIndexes(), index => index.IsUnique);
        Assert.Equal(
            [nameof(PartyTaxIdentityChangeRequest.CompanyId), nameof(PartyTaxIdentityChangeRequest.PartyId)],
            pending.Properties.Select(property => property.Name));
        Assert.Equal("[Status] = N'Pending'", pending.GetFilter());
        Assert.Equal("UX_PartyTaxIdentityChangeRequests_Pending", pending.GetDatabaseName());
        Assert.Contains(requestEntity.GetCheckConstraints(),
            check => check.Name == "CK_PartyTaxIdentityChangeRequests_Decision"
                && check.Sql.Contains("[DecidedBy] <> [RequestedBy]", StringComparison.Ordinal));
        // Company-scoped like every other company row of this context.
        Assert.Single(requestEntity.GetDeclaredQueryFilters());
        Assert.Contains(requestEntity.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "Companies");
        Assert.Contains(requestEntity.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Party));
    }

    [Fact]
    public void SaveChanges_AllowsATaxIdentityOnlyPartyChangeAndStillRejectsAnyOtherPartyEdit()
    {
        var companyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));
        var party = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000001"), PartyKind.LegalEntity, "Göymen Test Cari");
        dbContext.Parties.Attach(party);
        party.EnterTaxIdentityNumber(TaxIdentityNumber.Parse("1234567890"), DateTimeOffset.UtcNow);
        dbContext.ChangeTracker.DetectChanges();
        Assert.Equal(EntityState.Modified, dbContext.Entry(party).State);
        // The guard lets this through; the (absent) database is what fails next, not the guard.
        var passed = Assert.ThrowsAny<Exception>(() => dbContext.SaveChanges());
        Assert.DoesNotContain("doğrudan değiştirilemez", passed.Message, StringComparison.Ordinal);

        dbContext.ChangeTracker.Clear();
        var other = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000002"), PartyKind.LegalEntity, "Başka Cari");
        dbContext.Parties.Attach(other);
        dbContext.Entry(other).Property(value => value.LegalName).CurrentValue = "Yeni Unvan";
        var refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("doğrudan değiştirilemez", refused.Message, StringComparison.Ordinal);

        dbContext.ChangeTracker.Clear();
        var mixed = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000003"), PartyKind.LegalEntity, "Karışık Cari");
        dbContext.Parties.Attach(mixed);
        mixed.EnterTaxIdentityNumber(TaxIdentityNumber.Parse("9876543217"), DateTimeOffset.UtcNow);
        dbContext.Entry(mixed).Property(value => value.LegalName).CurrentValue = "Gizli Unvan Değişikliği";
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
    }

    [Fact]
    public void SaveChanges_RejectsAChangeRequestOfAnotherCompanyAndItsDeletion()
    {
        var companyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));
        var foreign = new PartyTaxIdentityChangeRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1234567890",
            TaxIdentityNumber.Parse("9876543217"), 2, "Neden", "maker", DateTimeOffset.UtcNow);
        dbContext.PartyTaxIdentityChangeRequests.Add(foreign);
        var exception = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("şirket bağlamı", exception.Message, StringComparison.Ordinal);

        dbContext.ChangeTracker.Clear();
        var own = new PartyTaxIdentityChangeRequest(Guid.NewGuid(), companyId, Guid.NewGuid(), "1234567890",
            TaxIdentityNumber.Parse("9876543217"), 2, "Neden", "maker", DateTimeOffset.UtcNow);
        dbContext.PartyTaxIdentityChangeRequests.Attach(own);
        dbContext.PartyTaxIdentityChangeRequests.Remove(own);
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
    }

    [Fact]
    public void Model_DoesNotIntroduceDeferredSensitiveIdentifiers()
    {
        using var dbContext = CreateContext();
        var propertyNames = dbContext.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // AT-009 stores VKN/TCKN as the party's TaxIdentityNumber (asserted above); MERSİS and IBAN stay deferred.
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
        Assert.Contains(
            "20260910120000_AllowCounterpartyAccountNoRole",
            dbContext.Database.GetMigrations());
        Assert.Equal(
            "20260930150000_AddPartyTaxIdentity",
            dbContext.Database.GetMigrations().Last());
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

    private static CounterpartyDbContext CreateContext(ICompanyContext? companyContext = null)
    {
        var options = new DbContextOptionsBuilder<CounterpartyDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpCounterpartyTests;Trusted_Connection=True;")
            .Options;
        return new CounterpartyDbContext(
            options,
            companyContext ?? new UnresolvedCompanyContext());
    }

    private static void AssertCompanyScopedAuditFilter(
        Expression<Func<AuditEvent, bool>> expression,
        Type contextType)
    {
        var comparison = Assert.IsAssignableFrom<BinaryExpression>(expression.Body);
        Assert.Equal(ExpressionType.Equal, comparison.NodeType);

        var entityCompanyId = Assert.IsAssignableFrom<MemberExpression>(comparison.Left);
        Assert.Same(expression.Parameters[0], entityCompanyId.Expression);
        Assert.Equal(nameof(AuditEvent.CompanyId), entityCompanyId.Member.Name);

        var nullableCompanyId = Assert.IsAssignableFrom<UnaryExpression>(comparison.Right);
        Assert.Equal(ExpressionType.Convert, nullableCompanyId.NodeType);
        Assert.Equal(typeof(Guid?), nullableCompanyId.Type);
        Assert.Null(nullableCompanyId.Method);
        Assert.Equal(typeof(Guid), nullableCompanyId.Operand.Type);
        var contextCompanyId = Assert.IsAssignableFrom<MemberExpression>(nullableCompanyId.Operand);
        Assert.Equal(nameof(ICompanyContext.CompanyId), contextCompanyId.Member.Name);
        Assert.Equal(typeof(ICompanyContext), contextCompanyId.Member.DeclaringType);
        Assert.True(IsCapturedFromContext(contextCompanyId.Expression, contextType));
    }

    private static bool IsCapturedFromContext(Expression? expression, Type contextType)
    {
        while (expression is MemberExpression memberExpression)
        {
            expression = memberExpression.Expression;
        }

        return expression is ConstantExpression constantExpression
            && contextType.IsAssignableFrom(constantExpression.Type);
    }

    private static CompanyContext ResolvedCompanyContext(Guid companyId)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(companyId);
        return companyContext;
    }

    private sealed class UnresolvedCompanyContext : ICompanyContext
    {
        public bool IsResolved => false;

        public Guid CompanyId => Guid.Empty;

        public void Set(Guid companyId) =>
            throw new InvalidOperationException("Persistence model testi company context çözmez.");
    }
}
