using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Domain.CompanySetup;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Organization.UnitTests;

public sealed class CompanySetupLifecycleTests
{
    [Fact]
    public void An_opened_company_waits_for_setup_without_changing_the_operational_active_flag()
    {
        var actor = Guid.NewGuid();
        var company = Pending(actor);
        Assert.True(company.IsActive);
        Assert.False(company.InvoiceCapable);
        Assert.Equal("SetupPending", company.LifecycleState);
        Assert.Equal(actor, company.OpenedByUserId);
        Assert.True(company.CompleteSetup(DateTimeOffset.UnixEpoch.AddMinutes(1)));
        Assert.False(company.CompleteSetup(DateTimeOffset.UnixEpoch.AddMinutes(2)));
        Assert.Equal(("Active", 2L), (company.LifecycleState, company.Version));
        Assert.Throws<ArgumentException>(() => Company.OpenPending(Guid.NewGuid(), "SYN", "Sentetik", "4100046586", Guid.Empty, DateTimeOffset.UnixEpoch));
    }
    [Fact]
    public async Task The_real_directory_includes_pending_and_active_companies_and_does_not_derive_membership()
    {
        var context = new CompanyContext();
        var bootstrap = new OrganizationBootstrapWriteScope();
        await using var db = Db(context, bootstrap);
        var pending = Pending(Guid.NewGuid());
        var existing = new Company(Guid.NewGuid(), "OLD", "Sentetik eski şirket", "1234567890", false, false);
        using (bootstrap.BeginBootstrapWrite())
        {
            db.Companies.AddRange(pending, existing);
            await db.SaveChangesAsync();
        }
        var reader = new OrganizationCompanyLifecycleReader(db);
        Assert.Equal(new[] { pending.Id, existing.Id }.Order(), await reader.ListCompanyIdsAsync(default));
        Assert.Equal(CompanyLifecycleStateV1.SetupPending, (await reader.ReadAsync(pending.Id))!.State);
        Assert.Equal(CompanyLifecycleStateV1.Active, (await reader.ReadAsync(existing.Id))!.State);
        Assert.Null(await reader.ReadAsync(Guid.NewGuid()));
        Assert.Null(await reader.ReadAsync(Guid.Empty));
    }
    [Fact]
    public async Task Owner_inbox_writes_require_the_resolved_company_and_receipts_cannot_change_or_be_deleted()
    {
        var company = Guid.NewGuid();
        var context = new CompanyContext(); context.Set(company);
        await using var db = Db(context, new());
        var receipt = new CompanySetupInboxReceipt(company, Guid.NewGuid(), new string('A', 64), DateTimeOffset.UnixEpoch);
        db.CompanySetupInboxReceipts.Add(receipt);
        await db.SaveChangesAsync();
        db.Entry(receipt).Property(x => x.PayloadHash).CurrentValue = new string('B', 64);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.CompanySetupInboxReceipts.Add(new(Guid.NewGuid(), Guid.NewGuid(), new string('A', 64), DateTimeOffset.UnixEpoch));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear(); db.CompanySetupInboxReceipts.Remove(receipt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public void The_lifecycle_owner_maps_append_only_inbox_outbox_and_a_rowversion_cursor()
    {
        using var db = Db(new CompanyContext(), new());
        var model = db.GetService<IDesignTimeModel>().Model;
        var receipt = model.FindEntityType(typeof(CompanySetupInboxReceipt))!;
        Assert.Equal("organization", receipt.GetSchema());
        Assert.Equal(new[] { "CompanyId", "EventId" }, receipt.FindPrimaryKey()!.Properties.Select(x => x.Name));
        var message = model.FindEntityType(typeof(OrganizationCompanySetupOutboxMessage))!;
        Assert.True(message.FindProperty("Position")!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, message.FindProperty("Position")!.ValueGenerated);
        Assert.Equal("Active", model.FindEntityType(typeof(Company))!.FindProperty("LifecycleState")!.GetDefaultValue());
        Assert.Equal(OrganizationDbContext.CompanyLifecycleCheck, model.FindEntityType(typeof(Company))!.GetCheckConstraints().Single(x => x.Name == "CK_Companies_Lifecycle").Sql);
        Assert.Equal(OrganizationDbContext.CompanySetupReceiptIdentityCheck, Assert.Single(receipt.GetCheckConstraints()).Sql);
    }
    [Fact]
    public void The_first_admin_contract_rejects_foreign_company_wrong_source_bad_version_and_unreadable_payload()
    {
        var company = Guid.NewGuid(); var context = new CompanyContext(); context.Set(company);
        var payload = new IdentityCompanyFirstAdminAssignedV1(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        var message = new WorkInboxEventV1(IdentityCompanyFirstAdminAssignedV1.SourceName, IdentityCompanyFirstAdminAssignedV1.EventId(company),
            company, IdentityCompanyFirstAdminAssignedV1.EventType, 1, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            DateTimeOffset.UnixEpoch, "synthetic", null);
        Assert.Equal(payload, OrganizationCompanySetupInboxConsumer.Read(message, context));
        Assert.Throws<OutboxPermanentFailureException>(() => OrganizationCompanySetupInboxConsumer.Read(message with { CompanyId = Guid.NewGuid() }, context));
        Assert.Throws<OutboxPermanentFailureException>(() => OrganizationCompanySetupInboxConsumer.Read(message with { SourceName = "foreign" }, context));
        Assert.Throws<OutboxPermanentFailureException>(() => OrganizationCompanySetupInboxConsumer.Read(message with { Version = 2 }, context));
        Assert.Throws<OutboxPermanentFailureException>(() => OrganizationCompanySetupInboxConsumer.Read(message with { PayloadJson = "{}" }, context));
    }
    [Fact]
    public void The_setup_migration_is_generated_DDL_preserves_existing_Active_companies_and_refuses_to_lose_new_lifecycle_history()
    {
        using var db = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused").Options, new CompanyContext(), new());
        Assert.Equal(["20261116185536_AddCompanySetupLifecycleAndMessaging", "20261116205304_AddAuthorizedCompanyOpening"], db.Database.GetMigrations().TakeLast(2));
        Assert.False(db.Database.HasPendingModelChanges());
        var migration = new AddCompanySetupLifecycleAndMessaging();
        Assert.DoesNotContain(migration.UpOperations, x => x is SqlOperation);
        Assert.Equal("Active", Assert.Single(migration.UpOperations.OfType<AddColumnOperation>(), x => x.Name == "LifecycleState").DefaultValue);
        Assert.Contains("THROW 51945", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
    private static Company Pending(Guid actor) => Company.OpenPending(Guid.NewGuid(), "NEW", "Sentetik yeni şirket", "4100046586", actor, DateTimeOffset.UnixEpoch);
    private static OrganizationDbContext Db(ICompanyContext context, OrganizationBootstrapWriteScope scope) => new(
        new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, context, scope);
}
