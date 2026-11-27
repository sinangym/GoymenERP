using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FIN-S5 / FNS-D19 (D5) identity claim persistence shape without a database. The unique generation index and the
/// check constraints remain real-SQL gates. SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinanceOfficialInvoiceIdentityClaimPersistenceModelTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void A_claim_is_a_company_filtered_finance_row()
    {
        using var context = Context();
        var entity = context.Model.FindEntityType(typeof(FinanceOfficialInvoiceIdentityClaimRow))!;

        Assert.Equal(FinanceDbContext.Schema, entity.GetSchema());
        Assert.Equal("OfficialInvoiceIdentityClaims", entity.GetTableName());
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.NotNull(entity.FindProperty("CompanyId"));
    }

    [Fact]
    public void Each_identity_generation_is_unique_per_company_and_the_holder_is_a_Finance_admission_receipt()
    {
        using var context = Context();
        var claims = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(FinanceOfficialInvoiceIdentityClaimRow))!;

        var unique = Assert.Single(claims.GetIndexes(), index => index.IsUnique);
        Assert.Equal("UX_OfficialInvoiceIdentityClaims_Generation", unique.GetDatabaseName());
        Assert.Equal(["CompanyId", "IdentityKind", "IdentityValue", "Generation"],
            unique.Properties.Select(property => property.Name));
        Assert.Contains(claims.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(FinanceOpenItemSourceBatchRow)
            && key.DeleteBehavior == DeleteBehavior.Restrict
            && key.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(FinanceOfficialInvoiceIdentityClaimRow.CompanyId),
                    nameof(FinanceOfficialInvoiceIdentityClaimRow.HolderBatchId)]));

        var value = Assert.Single(claims.GetCheckConstraints(),
            check => check.Name == "CK_OfficialInvoiceIdentityClaims_Value");
        Assert.Equal(FinanceDbContext.OfficialInvoiceIdentityClaimValueCheck, value.Sql);
        Assert.Contains($"'{FinanceOfficialInvoiceIdentityKinds.OfficialNo}'", value.Sql, StringComparison.Ordinal);
        Assert.Contains($"'{FinanceOfficialInvoiceIdentityKinds.Ettn}'", value.Sql, StringComparison.Ordinal);
        var content = Assert.Single(claims.GetCheckConstraints(),
            check => check.Name == "CK_OfficialInvoiceIdentityClaims_Content");
        Assert.Contains("[Generation] >= 1", content.Sql, StringComparison.Ordinal);
        Assert.Equal(FinanceDbContext.OfficialSalesInvoiceMaximumOfficialNoLength,
            claims.FindProperty(nameof(FinanceOfficialInvoiceIdentityClaimRow.IdentityValue))!.GetMaxLength());
        Assert.Equal("Latin1_General_100_BIN2",
            claims.FindProperty(nameof(FinanceOfficialInvoiceIdentityClaimRow.IdentityValue))!.GetCollation());
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("delete")]
    public void A_claim_is_append_only(string fault)
    {
        using var context = Context();
        var row = new FinanceOfficialInvoiceIdentityClaimRow
        {
            Id = Guid.NewGuid(), CompanyId = Company, IdentityKind = FinanceOfficialInvoiceIdentityKinds.OfficialNo,
            IdentityValue = "SNT1", Generation = 1, HolderBatchId = Guid.NewGuid(),
            HolderSourceDocumentId = Guid.NewGuid(), ClaimedBy = "SENTETIK-checker",
            ClaimedAtUtc = DateTimeOffset.UnixEpoch, CorrelationId = "corr",
        };
        context.OfficialInvoiceIdentityClaims.Attach(row);
        if (fault == "edit")
        {
            row.Generation = 2;
        }
        else
        {
            context.OfficialInvoiceIdentityClaims.Remove(row);
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void Identity_claim_migration_is_discoverable_and_snapshot_matches_the_model()
    {
        using var context = Context();

        // The newest Finance migration is pinned by name in FinanceBankAccountTests; here only presence is checked.
        Assert.Contains(context.Database.GetMigrations(), migration =>
            migration.EndsWith("_AddOfficialInvoiceIdentityClaims", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_identity_claim_migration_is_pure_ddl_up_and_its_rollback_refuses_first_while_claims_exist()
    {
        var migration = new AddOfficialInvoiceIdentityClaims();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51372", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[OfficialInvoiceIdentityClaims]", guard, StringComparison.Ordinal);
        Assert.Contains("forward fix", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }

    [Fact]
    public void Admission_receipt_FK_migration_refuses_orphans_and_has_a_native_holder_forward_fix_Down()
    {
        using var context = Context();
        Assert.Equal("20261127071245_BindOfficialInvoiceClaimsToAdmissionReceipts", context.Database.GetMigrations().Last());
        Assert.False(context.Database.HasPendingModelChanges());
        var migration = new BindOfficialInvoiceClaimsToAdmissionReceipts();
        var up = Assert.IsType<SqlOperation>(migration.UpOperations[0]).Sql;
        var down = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51373", up);
        Assert.Contains("[finance].[OpenItemSourceBatches]", up);
        Assert.Contains("without backfill", up);
        Assert.Contains("THROW 51374", down);
        Assert.Contains("[finance].[OfficialInvoiceImportBatches]", down);
        Assert.Contains("forward fix", down);
        Assert.Single(migration.UpOperations.OfType<SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        var foreignKey = Assert.Single(migration.UpOperations.OfType<AddForeignKeyOperation>());
        Assert.Equal("OpenItemSourceBatches", foreignKey.PrincipalTable);
        Assert.Equal(new[] { "CompanyId", "HolderBatchId" }, foreignKey.Columns);
        Assert.Equal(new[] { "CompanyId", "Id" }, foreignKey.PrincipalColumns);
    }

    private static FinanceDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(Company);
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new FinanceDbContext(options, company);
    }
}
