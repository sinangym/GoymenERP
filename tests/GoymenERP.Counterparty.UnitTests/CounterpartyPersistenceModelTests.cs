using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
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
    public void Customer_e_document_profile_is_group_wide_VKN_keyed_with_an_explicit_status_and_a_shaped_override()
    {
        using var dbContext = CreateContext(ResolvedCompanyContext(Guid.NewGuid()));
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var profile = model.FindEntityType(typeof(CustomerEDocumentProfile))!;
        Assert.Equal("counterparty", profile.GetSchema());
        Assert.Equal("CustomerEDocumentProfiles", profile.GetTableName());

        // SAT-D119: a GROUP-LEVEL record — no CompanyId property and no query filter (the same shape as Party).
        Assert.Null(profile.FindProperty("CompanyId"));
        Assert.Empty(profile.GetDeclaredQueryFilters());

        // One profile per taxpayer: the VKN/TCKN is unique across the group.
        var unique = Assert.Single(profile.GetIndexes(), index => index.IsUnique);
        Assert.Equal([nameof(CustomerEDocumentProfile.TaxIdentityNumber)], unique.Properties.Select(property => property.Name));
        Assert.False(profile.FindProperty(nameof(CustomerEDocumentProfile.TaxIdentityNumber))!.IsUnicode());

        // SAT-D102: Unknown (0) carries no check; a known status carries both the time and the checker.
        var status = Assert.Single(profile.GetCheckConstraints(), check => check.Name == "CK_CustomerEDocumentProfiles_Status");
        Assert.Contains("[EDocumentStatus] = 0 AND [EDocumentStatusCheckedAtUtc] IS NULL", status.Sql, StringComparison.Ordinal);
        Assert.Contains("[EDocumentStatus] IN (1, 2)", status.Sql, StringComparison.Ordinal);
        var code = Assert.Single(profile.GetCheckConstraints(),
            check => check.Name == "CK_CustomerEDocumentProfiles_ScenarioOverrideCode");
        Assert.Contains("NOT LIKE '%[^A-Z0-9_]%'", code.Sql, StringComparison.Ordinal);
        Assert.False(profile.FindProperty(nameof(CustomerEDocumentProfile.EDocumentStatus))!.IsNullable);
        Assert.Equal(32, profile.FindProperty(nameof(CustomerEDocumentProfile.EInvoiceScenarioOverrideCode))!.GetMaxLength());
        Assert.Equal(128, profile.FindProperty(nameof(CustomerEDocumentProfile.EDocumentStatusCheckedByUserId))!.GetMaxLength());

        // The checking company decides only who sees the checker; it is a real company (FK), not a tenant key.
        Assert.Contains("[EDocumentStatusCheckedInCompanyId] IS NULL", status.Sql, StringComparison.Ordinal);
        var checkedIn = Assert.Single(profile.GetForeignKeys());
        Assert.Equal([nameof(CustomerEDocumentProfile.EDocumentStatusCheckedInCompanyId)],
            checkedIn.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, checkedIn.DeleteBehavior);
        Assert.False(checkedIn.IsRequired);
    }

    [Fact]
    public void SaveChanges_refuses_an_e_document_profile_delete_before_the_database_call()
    {
        using var dbContext = CreateContext(ResolvedCompanyContext(Guid.NewGuid()));
        var profile = new CustomerEDocumentProfile(Guid.NewGuid(), "1234567890", DateTimeOffset.UnixEpoch);
        dbContext.CustomerEDocumentProfiles.Attach(profile);
        dbContext.CustomerEDocumentProfiles.Remove(profile);
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
    }

    [Fact]
    public void SaveChanges_refuses_an_e_document_check_recorded_for_another_company_before_the_database_call()
    {
        var active = Guid.NewGuid();
        var other = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(active));

        // A new profile whose check names another company is refused by the guard.
        var created = new CustomerEDocumentProfile(Guid.NewGuid(), "1234567890", DateTimeOffset.UnixEpoch);
        created.RecordStatus(EDocumentStatus.EInvoiceTaxpayer, "checker", other, DateTimeOffset.UnixEpoch);
        dbContext.CustomerEDocumentProfiles.Add(created);
        var refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("yalnız aktif şirkette", refused.Message, StringComparison.Ordinal);

        // So is re-recording an existing profile's check for another company.
        dbContext.ChangeTracker.Clear();
        var existing = new CustomerEDocumentProfile(Guid.NewGuid(), "9876543217", DateTimeOffset.UnixEpoch);
        dbContext.CustomerEDocumentProfiles.Attach(existing);
        existing.RecordStatus(EDocumentStatus.EArchive, "checker", other, DateTimeOffset.UnixEpoch);
        refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("yalnız aktif şirkette", refused.Message, StringComparison.Ordinal);

        // A check of the active company passes the guard (the absent database is what fails next).
        dbContext.ChangeTracker.Clear();
        var own = new CustomerEDocumentProfile(Guid.NewGuid(), "1234567890", DateTimeOffset.UnixEpoch);
        dbContext.CustomerEDocumentProfiles.Attach(own);
        own.RecordStatus(EDocumentStatus.EArchive, "checker", active, DateTimeOffset.UnixEpoch);
        var passed = Assert.ThrowsAny<Exception>(() => dbContext.SaveChanges());
        Assert.DoesNotContain("yalnız aktif şirkette", passed.Message, StringComparison.Ordinal);
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
        // A legal-name-only change has its own command now (ALS-D95); any other Party column is still refused.
        dbContext.Entry(other).Property(value => value.IsActive).CurrentValue = false;
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
    public void SaveChanges_AllowsATradeNameOnlyPartyChangeAndRejectsALegalNameRiddenAlong()
    {
        var companyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));
        var party = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000011"), PartyKind.LegalEntity, "Göymen Test Cari");
        dbContext.Parties.Attach(party);
        Assert.True(party.ChangeTradeName("Göymen", DateTimeOffset.UtcNow));
        dbContext.ChangeTracker.DetectChanges();
        Assert.Equal(EntityState.Modified, dbContext.Entry(party).State);
        // The guard lets this through; the (absent) database is what fails next, not the guard.
        var passed = Assert.ThrowsAny<Exception>(() => dbContext.SaveChanges());
        Assert.DoesNotContain("doğrudan değiştirilemez", passed.Message, StringComparison.Ordinal);

        // The legal name has its own command and permission (ALS-D95), so it may not ride along with a trade name change.
        dbContext.ChangeTracker.Clear();
        var mixed = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000012"), PartyKind.LegalEntity, "Karışık Cari");
        dbContext.Parties.Attach(mixed);
        mixed.ChangeTradeName("Karışık", DateTimeOffset.UtcNow);
        dbContext.Entry(mixed).Property(value => value.LegalName).CurrentValue = "Gizli Unvan Değişikliği";
        var refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("doğrudan değiştirilemez", refused.Message, StringComparison.Ordinal);

        // Nor may a tax identity change and a trade name change share one save.
        dbContext.ChangeTracker.Clear();
        var both = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000013"), PartyKind.LegalEntity, "İkili Cari");
        dbContext.Parties.Attach(both);
        both.ChangeTradeName("İkili", DateTimeOffset.UtcNow);
        both.EnterTaxIdentityNumber(TaxIdentityNumber.Parse("1234567890"), DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
    }

    [Fact]
    public void SaveChanges_AllowsALegalNameOnlyPartyChangeAndRejectsAnythingRiddenAlong()
    {
        // ALS-D95 / 19.22: the legal name change is its own fenced, audited command; nothing else shares the save.
        var companyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));
        var party = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000021"), PartyKind.LegalEntity, "Eski Unvan A.Ş.");
        dbContext.Parties.Attach(party);
        Assert.True(party.ChangeLegalName("Yeni Unvan A.Ş.", DateTimeOffset.UtcNow));
        dbContext.ChangeTracker.DetectChanges();
        Assert.Equal(EntityState.Modified, dbContext.Entry(party).State);
        // The guard lets this through; the (absent) database is what fails next, not the guard.
        var passed = Assert.ThrowsAny<Exception>(() => dbContext.SaveChanges());
        Assert.DoesNotContain("doğrudan değiştirilemez", passed.Message, StringComparison.Ordinal);

        // A trade name change may not ride along: each name is changed under its own permission.
        dbContext.ChangeTracker.Clear();
        var mixed = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000022"), PartyKind.LegalEntity, "Karışık Unvan");
        dbContext.Parties.Attach(mixed);
        mixed.ChangeLegalName("Karışık Yeni Unvan", DateTimeOffset.UtcNow);
        dbContext.Entry(mixed).Property(value => value.TradeName).CurrentValue = "Gizli Ticari Ad";
        var refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("doğrudan değiştirilemez", refused.Message, StringComparison.Ordinal);

        // Nor may a tax identity change (maker-checker) share the save.
        dbContext.ChangeTracker.Clear();
        var both = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000023"), PartyKind.LegalEntity, "İkili Unvan");
        dbContext.Parties.Attach(both);
        both.ChangeLegalName("İkili Yeni Unvan", DateTimeOffset.UtcNow);
        both.EnterTaxIdentityNumber(TaxIdentityNumber.Parse("1234567890"), DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());

        // Nor any other Party column.
        dbContext.ChangeTracker.Clear();
        var other = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000024"), PartyKind.LegalEntity, "Pasif Unvan");
        dbContext.Parties.Attach(other);
        other.ChangeLegalName("Pasif Yeni Unvan", DateTimeOffset.UtcNow);
        dbContext.Entry(other).Property(value => value.IsActive).CurrentValue = false;
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
        Assert.Contains(
            "20260930150000_AddPartyTaxIdentity",
            dbContext.Database.GetMigrations());
        Assert.Contains(
            "20261027202534_AddCounterpartyCustomerGroups",
            dbContext.Database.GetMigrations());
        Assert.Contains(
            "20261031143822_AddCustomerEDocumentProfiles",
            dbContext.Database.GetMigrations());
        Assert.Contains(
            "20261115132003_AddCounterpartyOutbox",
            dbContext.Database.GetMigrations());
        Assert.Contains(
            "20261115160755_AddCounterpartyIdentityHistoryAndSuccession",
            dbContext.Database.GetMigrations());
        Assert.Contains(
            "20261115165158_AddCounterpartyPartyMerge",
            dbContext.Database.GetMigrations());
        Assert.EndsWith(
            "_AddCounterpartyLegalNameChangeRequests",
            dbContext.Database.GetMigrations().Last(),
            StringComparison.Ordinal);
        Assert.False(dbContext.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Customer_group_is_company_scoped_and_the_account_group_is_a_same_company_composite_key()
    {
        var companyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));
        var group = dbContext.Model.FindEntityType(typeof(CustomerGroup))!;
        Assert.Equal("counterparty", group.GetSchema());
        Assert.Equal("CustomerGroups", group.GetTableName());
        Assert.NotNull(group.GetDeclaredQueryFilters().SingleOrDefault());
        var unique = Assert.Single(group.GetIndexes(), index => index.IsUnique);
        Assert.Equal(["CompanyId", "Name"], unique.Properties.Select(property => property.Name));

        var account = dbContext.Model.FindEntityType(typeof(CompanyPartyAccount))!;
        var foreignKey = Assert.Single(account.GetForeignKeys(), key => key.PrincipalEntityType == group);
        Assert.Equal(["CompanyId", "CustomerGroupId"], foreignKey.Properties.Select(property => property.Name));
        Assert.Equal(["CompanyId", "Id"], foreignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.False(foreignKey.IsRequired);
    }

    [Fact]
    public void SaveChanges_refuses_a_group_delete_and_a_group_of_another_company_before_the_database_call()
    {
        var companyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));
        var own = new CustomerGroup(Guid.NewGuid(), companyId, "Toptancı");
        dbContext.CustomerGroups.Attach(own);
        dbContext.CustomerGroups.Remove(own);
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());

        dbContext.ChangeTracker.Clear();
        dbContext.CustomerGroups.Add(new CustomerGroup(Guid.NewGuid(), Guid.NewGuid(), "Perakendeci"));
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
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

    [Fact]
    public void Outbox_is_company_scoped_append_only_and_keyed_by_aggregate_sequence()
    {
        using var dbContext = CreateContext(ResolvedCompanyContext(Guid.NewGuid()));
        var outbox = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CounterpartyOutboxRow))!;
        Assert.Equal("counterparty", outbox.GetSchema());
        Assert.Equal("OutboxMessages", outbox.GetTableName());
        Assert.NotNull(outbox.GetDeclaredQueryFilters().SingleOrDefault());
        Assert.Equal(
            ["CK_OutboxMessages_Envelope", "CK_OutboxMessages_Identity"],
            outbox.GetCheckConstraints().Select(check => check.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            CounterpartyDbContext.OutboxIdentityCheck,
            outbox.GetCheckConstraints().Single(check => check.Name == "CK_OutboxMessages_Identity").Sql);
        Assert.Equal(
            CounterpartyDbContext.OutboxEnvelopeCheck,
            outbox.GetCheckConstraints().Single(check => check.Name == "CK_OutboxMessages_Envelope").Sql);

        var unique = Assert.Single(outbox.GetIndexes(), index => index.IsUnique);
        Assert.Equal("UX_OutboxMessages_AggregateSequence", unique.GetDatabaseName());
        Assert.Equal(
            ["CompanyId", "EventType", "AggregateId", "Sequence"],
            unique.Properties.Select(property => property.Name));
        Assert.Equal(ValueGenerated.Never, outbox.FindPrimaryKey()!.Properties.Single().ValueGenerated);
        Assert.False(outbox.FindProperty(nameof(CounterpartyOutboxRow.CorrelationId))!.IsNullable);
        Assert.True(outbox.FindProperty(nameof(CounterpartyOutboxRow.CausationId))!.IsNullable);

        var company = Assert.Single(outbox.GetForeignKeys());
        Assert.Equal(["CompanyId"], company.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, company.DeleteBehavior);
    }

    [Fact]
    public void SaveChanges_refuses_an_outbox_update_delete_and_another_company_row_before_the_database_call()
    {
        var companyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));
        var own = OutboxRow(companyId);
        dbContext.OutboxMessages.Attach(own);
        own.PayloadJson = "{}";
        var refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("olay kutusu", refused.Message, StringComparison.Ordinal);

        dbContext.ChangeTracker.Clear();
        var deleted = OutboxRow(companyId);
        dbContext.OutboxMessages.Attach(deleted);
        dbContext.OutboxMessages.Remove(deleted);
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());

        dbContext.ChangeTracker.Clear();
        dbContext.OutboxMessages.Add(OutboxRow(Guid.NewGuid()));
        refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("şirket", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveChanges_refuses_an_outbox_row_without_a_resolved_company_before_the_database_call()
    {
        using var dbContext = CreateContext();
        dbContext.OutboxMessages.Add(OutboxRow(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
    }

    [Fact]
    public void Outbox_migration_Up_is_pure_DDL_and_its_Down_first_refuses_while_events_exist()
    {
        var migration = new GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations.AddCounterpartyOutbox();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51441", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[counterparty].[OutboxMessages]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains(
            migration.DownOperations,
            operation => operation is DropTableOperation { Name: "OutboxMessages", Schema: "counterparty" });
    }

    [Fact]
    public void Party_merge_is_a_group_level_append_only_record_and_a_merged_party_points_at_another_party()
    {
        // F-027 / EK-18 #76-#77: Party is global, so the merge record carries no company query filter; it names the
        // company the approval was given in. A duplicate is merged at most once; nothing cascades.
        using var dbContext = CreateContext(ResolvedCompanyContext(Guid.NewGuid()));
        var party = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Party))!;
        Assert.Equal(
            CounterpartyDbContext.PartyMergedIntoCheck,
            party.GetCheckConstraints().Single(check => check.Name == "CK_Parties_MergedInto").Sql);
        Assert.True(party.FindProperty(nameof(Party.MergedIntoPartyId))!.IsNullable);
        var selfLink = Assert.Single(
            party.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Party.MergedIntoPartyId)]));
        Assert.Same(party, selfLink.PrincipalEntityType);
        Assert.Equal(DeleteBehavior.Restrict, selfLink.DeleteBehavior);
        var mergedIndex = Assert.Single(
            party.GetIndexes(),
            index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(Party.MergedIntoPartyId)]));
        Assert.False(mergedIndex.IsUnique);
        Assert.Equal("[MergedIntoPartyId] IS NOT NULL", mergedIndex.GetFilter());

        var merge = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PartyMerge))!;
        Assert.Equal("counterparty", merge.GetSchema());
        Assert.Equal("PartyMerges", merge.GetTableName());
        Assert.Empty(merge.GetDeclaredQueryFilters());
        Assert.Equal(ValueGenerated.Never, merge.FindPrimaryKey()!.Properties.Single().ValueGenerated);
        Assert.Equal(
            ["CK_PartyMerges_Identity", "CK_PartyMerges_Text", "CK_PartyMerges_Version"],
            merge.GetCheckConstraints().Select(check => check.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            CounterpartyDbContext.PartyMergeIdentityCheck,
            merge.GetCheckConstraints().Single(check => check.Name == "CK_PartyMerges_Identity").Sql);
        Assert.Equal(
            CounterpartyDbContext.PartyMergeVersionCheck,
            merge.GetCheckConstraints().Single(check => check.Name == "CK_PartyMerges_Version").Sql);
        Assert.Equal(
            CounterpartyDbContext.PartyMergeTextCheck,
            merge.GetCheckConstraints().Single(check => check.Name == "CK_PartyMerges_Text").Sql);
        Assert.Equal(PartyMerge.ReasonMaximumLength, merge.FindProperty(nameof(PartyMerge.Reason))!.GetMaxLength());
        Assert.Equal(PartyMerge.ActorMaximumLength, merge.FindProperty(nameof(PartyMerge.ApprovedBy))!.GetMaxLength());
        Assert.Equal(
            "Latin1_General_100_BIN2",
            merge.FindProperty(nameof(PartyMerge.CorrelationId))!.GetCollation());

        var unique = Assert.Single(merge.GetIndexes(), index => index.IsUnique);
        Assert.Equal("UX_PartyMerges_MergedParty", unique.GetDatabaseName());
        Assert.Equal([nameof(PartyMerge.MergedPartyId)], unique.Properties.Select(property => property.Name));
        Assert.Equal(3, merge.GetForeignKeys().Count());
        Assert.All(merge.GetForeignKeys(), foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
        Assert.Contains(
            merge.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(PartyMerge.ApprovalCompanyId)])
                && foreignKey.PrincipalEntityType.GetTableName() == "Companies"
                && foreignKey.PrincipalEntityType.GetSchema() == "organization");
    }

    [Fact]
    public void SaveChanges_admits_a_merge_only_with_its_record_and_the_other_companies_notifications()
    {
        // The guard lets the whole merge through; the (absent) database is what fails next, not the guard.
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));
        var (survivor, duplicate) = AttachedPair(dbContext, 31);
        var record = MergeRecord(companyId, survivor, duplicate);
        duplicate.MergeInto(survivor, DateTimeOffset.UtcNow);
        dbContext.Entry(survivor).Property(value => value.MergedIntoPartyId).IsModified = true;
        dbContext.PartyMerges.Add(record);
        dbContext.OutboxMessages.Add(MergeNotification(companyId, record.Id));
        dbContext.OutboxMessages.Add(MergeNotification(otherCompanyId, record.Id));
        var passed = Assert.ThrowsAny<Exception>(() => dbContext.SaveChanges());
        Assert.DoesNotContain("doğrudan değiştirilemez", passed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("birleştir", passed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("olay kutusu", passed.Message, StringComparison.Ordinal);

        // Without the record naming this pair, the duplicate's merge columns may not move.
        dbContext.ChangeTracker.Clear();
        (survivor, duplicate) = AttachedPair(dbContext, 33);
        duplicate.MergeInto(survivor, DateTimeOffset.UtcNow);
        var refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("doğrudan değiştirilemez", refused.Message, StringComparison.Ordinal);

        // A record naming another survivor does not admit it either.
        dbContext.ChangeTracker.Clear();
        (survivor, duplicate) = AttachedPair(dbContext, 35);
        var stranger = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000037"), PartyKind.LegalEntity, "Başka Cari");
        dbContext.PartyMerges.Add(MergeRecord(companyId, stranger, duplicate));
        duplicate.MergeInto(survivor, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());

        // A notification for another company with no merge record beside it is refused.
        dbContext.ChangeTracker.Clear();
        dbContext.OutboxMessages.Add(MergeNotification(otherCompanyId, Guid.NewGuid()));
        refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("olay kutusu", refused.Message, StringComparison.Ordinal);

        // Nor may a passive flag or a name ride along with the merge.
        dbContext.ChangeTracker.Clear();
        (survivor, duplicate) = AttachedPair(dbContext, 38);
        dbContext.PartyMerges.Add(MergeRecord(companyId, survivor, duplicate));
        duplicate.MergeInto(survivor, DateTimeOffset.UtcNow);
        dbContext.Entry(duplicate).Property(value => value.LegalName).CurrentValue = "Gizli Unvan";
        refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("doğrudan değiştirilemez", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveChanges_refuses_an_unmerge_a_redirect_a_merged_new_party_and_a_merge_record_edit()
    {
        var companyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));

        // EK-18 #77: no unmerge, no reactivation, no redirect in place.
        var survivor = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000041"), PartyKind.LegalEntity, "Kalan Cari");
        var merged = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000042"), PartyKind.LegalEntity, "Mükerrer Cari");
        merged.MergeInto(survivor, DateTimeOffset.UtcNow);
        dbContext.Parties.Attach(merged);
        dbContext.Entry(merged).Property(value => value.MergedIntoPartyId).CurrentValue = null;
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());

        dbContext.ChangeTracker.Clear();
        dbContext.Parties.Attach(merged);
        dbContext.Entry(merged).Property(value => value.IsActive).CurrentValue = true;
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());

        dbContext.ChangeTracker.Clear();
        dbContext.Parties.Attach(merged);
        var redirect = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000043"), PartyKind.LegalEntity, "Yeni Hedef");
        dbContext.PartyMerges.Add(MergeRecord(companyId, redirect, merged));
        dbContext.Entry(merged).Property(value => value.MergedIntoPartyId).CurrentValue = redirect.Id;
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());

        // A party is never created already merged.
        dbContext.ChangeTracker.Clear();
        var born = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000044"), PartyKind.LegalEntity, "Doğuştan Birleşik");
        born.MergeInto(survivor, DateTimeOffset.UtcNow);
        dbContext.Parties.Add(born);
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());

        // The merge record is append-only and is recorded in the active company only.
        dbContext.ChangeTracker.Clear();
        var record = MergeRecord(companyId, survivor, merged);
        dbContext.PartyMerges.Attach(record);
        dbContext.Entry(record).Property(value => value.Reason).CurrentValue = "Değiştirilmiş neden";
        var refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("birleştirme kaydı", refused.Message, StringComparison.Ordinal);

        dbContext.ChangeTracker.Clear();
        dbContext.PartyMerges.Attach(record);
        dbContext.PartyMerges.Remove(record);
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());

        dbContext.ChangeTracker.Clear();
        dbContext.PartyMerges.Add(MergeRecord(Guid.NewGuid(), survivor, merged));
        refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("aktif şirket", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveChanges_refuses_a_merge_record_without_a_resolved_company_before_the_database_call()
    {
        using var dbContext = CreateContext();
        var survivor = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000045"), PartyKind.LegalEntity, "Kalan");
        var merged = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000046"), PartyKind.LegalEntity, "Mükerrer");
        dbContext.PartyMerges.Add(MergeRecord(Guid.NewGuid(), survivor, merged));
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
    }

    [Fact]
    public void Party_merge_migration_Up_is_pure_DDL_and_its_Down_first_refuses_while_merges_exist()
    {
        var migration = new GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations.AddCounterpartyPartyMerge();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51453", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[counterparty].[PartyMerges]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[MergedIntoPartyId] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains(
            migration.DownOperations,
            operation => operation is DropTableOperation { Name: "PartyMerges", Schema: "counterparty" });
        Assert.Contains(
            migration.DownOperations,
            operation => operation is DropColumnOperation { Name: "MergedIntoPartyId", Table: "Parties" });
    }

    [Fact]
    public void Legal_name_change_request_is_company_scoped_with_one_pending_slot_and_the_maker_checker_checks()
    {
        using var dbContext = CreateContext(ResolvedCompanyContext(Guid.NewGuid()));
        var request = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PartyLegalNameChangeRequest))!;
        Assert.Equal("counterparty", request.GetSchema());
        Assert.Equal("PartyLegalNameChangeRequests", request.GetTableName());
        Assert.NotNull(request.GetDeclaredQueryFilters().SingleOrDefault());
        var pending = Assert.Single(request.GetIndexes(), index => index.GetDatabaseName() == "UX_PartyLegalNameChangeRequests_Pending");
        Assert.True(pending.IsUnique);
        Assert.Equal("[Status] = N'Pending'", pending.GetFilter());
        Assert.Equal(
            new[] { nameof(PartyLegalNameChangeRequest.CompanyId), nameof(PartyLegalNameChangeRequest.PartyId) },
            pending.Properties.Select(property => property.Name));
        var checks = request.GetCheckConstraints().Select(check => check.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("CK_PartyLegalNameChangeRequests_Status", checks);
        Assert.Contains("CK_PartyLegalNameChangeRequests_Names", checks);
        Assert.Contains("CK_PartyLegalNameChangeRequests_Decision", checks);
        Assert.Contains("CK_PartyLegalNameChangeRequests_Text", checks);
        Assert.Contains(
            "<> [RequestedBy]",
            request.GetCheckConstraints().Single(check => check.Name == "CK_PartyLegalNameChangeRequests_Decision").Sql,
            StringComparison.Ordinal);
        Assert.True(request.FindProperty(nameof(PartyLegalNameChangeRequest.Version))!.IsConcurrencyToken);
        Assert.Equal(Party.LegalNameMaximumLength, request.FindProperty(nameof(PartyLegalNameChangeRequest.ProposedLegalName))!.GetMaxLength());
    }

    [Fact]
    public void SaveChanges_refuses_a_legal_name_change_request_of_another_company_before_the_database_call()
    {
        var companyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));
        dbContext.PartyLegalNameChangeRequests.Add(new PartyLegalNameChangeRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Eski Unvan", "Yeni Unvan", 1, "Sicil değişikliği",
            "unit-maker", DateTimeOffset.UtcNow));
        var refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("aktif şirket", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveChanges_refuses_to_delete_or_rewrite_a_legal_name_change_request()
    {
        var companyId = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(companyId));
        var request = new PartyLegalNameChangeRequest(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Eski Unvan", "Yeni Unvan", 1, "Sicil değişikliği",
            "unit-maker", DateTimeOffset.UtcNow);
        dbContext.PartyLegalNameChangeRequests.Attach(request);
        dbContext.Entry(request).Property(x => x.ProposedLegalName).CurrentValue = "Başka Unvan";
        Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());

        dbContext.Entry(request).State = EntityState.Deleted;
        var deleted = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("silinemez", deleted.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Legal_name_change_migration_Up_is_pure_DDL_and_its_Down_first_refuses_while_requests_exist()
    {
        var migration = new GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations.AddCounterpartyLegalNameChangeRequests();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51847", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[counterparty].[PartyLegalNameChangeRequests]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains(
            migration.DownOperations,
            operation => operation is DropTableOperation { Name: "PartyLegalNameChangeRequests", Schema: "counterparty" });
    }

    private static (Party Survivor, Party Duplicate) AttachedPair(CounterpartyDbContext dbContext, int sequence)
    {
        var survivor = new Party(
            Guid.NewGuid(), PartyCode.FromSequence(sequence), PartyKind.LegalEntity, "Kalan Cari A.Ş.");
        var duplicate = new Party(
            Guid.NewGuid(), PartyCode.FromSequence(sequence + 1), PartyKind.LegalEntity, "Mükerrer Cari A.Ş.");
        dbContext.Parties.Attach(survivor);
        dbContext.Parties.Attach(duplicate);
        return (survivor, duplicate);
    }

    private static PartyMerge MergeRecord(Guid companyId, Party survivor, Party duplicate) =>
        new(
            Guid.NewGuid(),
            companyId,
            survivor.Id,
            survivor.Version,
            duplicate.Id,
            duplicate.Version + 1,
            "Aynı firma iki kez açılmış.",
            "unit-approver",
            DateTimeOffset.UtcNow,
            "unit-correlation");

    private static CounterpartyOutboxRow MergeNotification(Guid companyId, Guid mergeId)
    {
        var row = OutboxRow(companyId);
        row.EventType = GoymenERP.Modules.Counterparty.Application.Contracts.CounterpartyPartyMergedOutboxV1.EventType;
        row.AggregateId = mergeId;
        return row;
    }

    [Fact]
    public void Identity_history_migration_Up_is_pure_DDL_and_its_Down_first_refuses_while_history_or_a_succession_exists()
    {
        var migration = new GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations.AddCounterpartyIdentityHistoryAndSuccession();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51530", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[counterparty].[PartyIdentityRevisions]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[counterparty].[CustomerEDocumentStatusRevisions]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[counterparty].[PartySuccessions]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        foreach (var table in new[] { "PartyIdentityRevisions", "CustomerEDocumentStatusRevisions", "PartySuccessions" })
        {
            Assert.Contains(
                migration.DownOperations,
                operation => operation is DropTableOperation { Schema: "counterparty" } drop && drop.Name == table);
        }
    }

    [Fact]
    public void Identity_revisions_are_group_level_append_only_and_dated_by_kind()
    {
        using var dbContext = CreateContext(ResolvedCompanyContext(Guid.NewGuid()));
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var revision = model.FindEntityType(typeof(PartyIdentityRevision))!;
        Assert.Equal("counterparty", revision.GetSchema());
        Assert.Equal("PartyIdentityRevisions", revision.GetTableName());

        // KLN-D38: group-level like Party (DEC-CAR-001); the recording company is provenance only.
        Assert.Null(revision.FindProperty("CompanyId"));
        Assert.Empty(revision.GetDeclaredQueryFilters());
        var unique = Assert.Single(revision.GetIndexes(), index => index.IsUnique);
        Assert.Equal(
            [nameof(PartyIdentityRevision.PartyId), nameof(PartyIdentityRevision.Attribute), nameof(PartyIdentityRevision.Sequence)],
            unique.Properties.Select(property => property.Name));
        Assert.True(revision.FindProperty(nameof(PartyIdentityRevision.EffectiveFrom))!.IsNullable);
        Assert.Equal(PartyIdentityRevision.ValueMaximumLength,
            revision.FindProperty(nameof(PartyIdentityRevision.Value))!.GetMaxLength());

        var dated = Assert.Single(revision.GetCheckConstraints(), check => check.Name == "CK_PartyIdentityRevisions_EffectiveFrom");
        Assert.Contains("[Kind] IN (1, 2) AND [EffectiveFrom] IS NULL", dated.Sql, StringComparison.Ordinal);
        Assert.Contains("[Kind] IN (3, 4) AND [EffectiveFrom] IS NOT NULL", dated.Sql, StringComparison.Ordinal);
        var correction = Assert.Single(revision.GetCheckConstraints(), check => check.Name == "CK_PartyIdentityRevisions_Correction");
        Assert.Contains("[Kind] = 3 AND [Attribute] = 1 AND [SourceChangeRequestId] IS NOT NULL", correction.Sql, StringComparison.Ordinal);
        Assert.Contains("[Kind] <> 3 AND [SourceChangeRequestId] IS NULL", correction.Sql, StringComparison.Ordinal);
        var valueProperty = revision.FindProperty(nameof(PartyIdentityRevision.Value))!;
        Assert.Equal($"nvarchar({PartyIdentityRevision.ValueMaximumLength})", valueProperty.GetColumnType());
        var value = Assert.Single(revision.GetCheckConstraints(), check => check.Name == "CK_PartyIdentityRevisions_Value");
        Assert.Contains("[Attribute] <> 1 OR (LEN([Value]) IN (10, 11)", value.Sql, StringComparison.Ordinal);
        Assert.Contains("DATALENGTH([Value]) = 2 * LEN([Value])", value.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DATALENGTH([Value]) = LEN([Value])", value.Sql, StringComparison.Ordinal);
        Assert.Contains("[Value] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'", value.Sql, StringComparison.Ordinal);
        var partyValue = Assert.Single(model.FindEntityType(typeof(Party))!.GetCheckConstraints(),
            check => check.Name == "CK_Parties_TaxIdentityNumber");
        Assert.Contains("DATALENGTH([TaxIdentityNumber]) = LEN([TaxIdentityNumber])", partyValue.Sql, StringComparison.Ordinal);
        Assert.All(revision.GetForeignKeys(), foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
        Assert.Contains(revision.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(PartyTaxIdentityChangeRequest));

        var status = model.FindEntityType(typeof(CustomerEDocumentStatusRevision))!;
        Assert.Equal("CustomerEDocumentStatusRevisions", status.GetTableName());
        Assert.Null(status.FindProperty("CompanyId"));
        Assert.Empty(status.GetDeclaredQueryFilters());
        Assert.False(status.FindProperty(nameof(CustomerEDocumentStatusRevision.EffectiveFrom))!.IsNullable);
        var statusUnique = Assert.Single(status.GetIndexes(), index => index.IsUnique);
        Assert.Equal(
            [nameof(CustomerEDocumentStatusRevision.ProfileId), nameof(CustomerEDocumentStatusRevision.Sequence)],
            statusUnique.Properties.Select(property => property.Name));
    }

    [Fact]
    public void Party_succession_is_company_scoped_with_one_successor_per_predecessor_and_no_balance_column()
    {
        using var dbContext = CreateContext(ResolvedCompanyContext(Guid.NewGuid()));
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var succession = model.FindEntityType(typeof(PartySuccession))!;
        Assert.Equal("counterparty", succession.GetSchema());
        Assert.Equal("PartySuccessions", succession.GetTableName());
        Assert.NotNull(succession.FindProperty(nameof(PartySuccession.CompanyId)));
        Assert.Single(succession.GetDeclaredQueryFilters());

        var unique = Assert.Single(succession.GetIndexes(), index => index.IsUnique);
        Assert.Equal("UX_PartySuccessions_Predecessor", unique.GetDatabaseName());
        Assert.Equal(
            [nameof(PartySuccession.CompanyId), nameof(PartySuccession.PredecessorPartyId)],
            unique.Properties.Select(property => property.Name));
        var parties = Assert.Single(succession.GetCheckConstraints(), check => check.Name == "CK_PartySuccessions_Parties");
        Assert.Equal("[PredecessorPartyId] <> [SuccessorPartyId]", parties.Sql);
        Assert.Equal(PartySuccession.LegalBasisMaximumLength,
            succession.FindProperty(nameof(PartySuccession.LegalBasis))!.GetMaxLength());
        Assert.False(succession.FindProperty(nameof(PartySuccession.LegalBasis))!.IsNullable);

        // KLN-D38 (2): the link never carries a balance; the balance moves only through a Finance transfer voucher.
        var propertyNames = succession.GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(propertyNames, name => name.Contains("Amount", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Balance", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Currency", StringComparison.OrdinalIgnoreCase));
        Assert.All(succession.GetForeignKeys(), foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
        Assert.Equal(2, succession.GetForeignKeys().Count(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Party)));
    }

    [Fact]
    public void SaveChanges_refuses_an_identity_revision_edit_or_delete_before_the_database_call()
    {
        var active = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(active));
        var revision = PartyIdentityRevision.Baseline(
            Guid.NewGuid(), Guid.NewGuid(), PartyIdentityAttribute.LegalName, 1, "Örnek Ticaret", DateTimeOffset.UnixEpoch);
        dbContext.PartyIdentityRevisions.Attach(revision);
        dbContext.PartyIdentityRevisions.Remove(revision);
        var refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("değiştirilemez veya silinemez", refused.Message, StringComparison.Ordinal);

        dbContext.ChangeTracker.Clear();
        var status = new CustomerEDocumentStatusRevision(
            Guid.NewGuid(), Guid.NewGuid(), 1, EDocumentStatus.EArchive, new DateOnly(2026, 11, 1),
            CustomerEDocumentStatusRevisionKind.Recorded, "checker", active, DateTimeOffset.UnixEpoch);
        dbContext.CustomerEDocumentStatusRevisions.Attach(status);
        dbContext.Entry(status).State = EntityState.Modified;
        refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("değiştirilemez veya silinemez", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveChanges_refuses_a_history_recorded_for_another_company_or_a_succession_of_another_company()
    {
        var active = Guid.NewGuid();
        var other = Guid.NewGuid();
        using var dbContext = CreateContext(ResolvedCompanyContext(active));

        dbContext.PartyIdentityRevisions.Add(new PartyIdentityRevision(
            Guid.NewGuid(), Guid.NewGuid(), PartyIdentityAttribute.LegalName, 2, "Yeni Unvan", new DateOnly(2026, 11, 1),
            PartyIdentityRevisionKind.DatedChange, null, null, "maker", other, DateTimeOffset.UnixEpoch));
        var refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("yalnız aktif şirket", refused.Message, StringComparison.Ordinal);

        dbContext.ChangeTracker.Clear();
        dbContext.PartySuccessions.Add(new PartySuccession(
            Guid.NewGuid(), other, Guid.NewGuid(), Guid.NewGuid(), PartySuccessionKind.Merger, "Ticaret sicili ilanı",
            new DateOnly(2026, 11, 1), "maker", DateTimeOffset.UnixEpoch));
        refused = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());
        Assert.Contains("aktif şirket bağlamıyla", refused.Message, StringComparison.Ordinal);

        // A succession of the active company passes the guard (the absent database is what fails next).
        dbContext.ChangeTracker.Clear();
        dbContext.PartySuccessions.Add(new PartySuccession(
            Guid.NewGuid(), active, Guid.NewGuid(), Guid.NewGuid(), PartySuccessionKind.Merger, "Ticaret sicili ilanı",
            new DateOnly(2026, 11, 1), "maker", DateTimeOffset.UnixEpoch));
        var passed = Assert.ThrowsAny<Exception>(() => dbContext.SaveChanges());
        Assert.DoesNotContain("aktif şirket bağlamıyla", passed.Message, StringComparison.Ordinal);
    }

    private static CounterpartyOutboxRow OutboxRow(Guid companyId)
    {
        var occurredAt = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
        return new CounterpartyOutboxRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            EventType = "counterparty.party.deactivated.v1",
            SchemaVersion = 1,
            SourceOperation = "counterparty.company-account.set-active-state.v1",
            AggregateType = "Counterparty.CompanyPartyAccount",
            AggregateId = Guid.NewGuid(),
            AggregateVersion = 2,
            Sequence = 2,
            PayloadJson = "{\"reason\":\"Çalışma sona erdi\"}",
            OccurredAtUtc = occurredAt,
            CorrelationId = "unit-correlation",
            RecordedAtUtc = occurredAt,
        };
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
