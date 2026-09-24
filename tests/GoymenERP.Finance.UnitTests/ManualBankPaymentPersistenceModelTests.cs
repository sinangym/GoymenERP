using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// R47 persistence shape without a database. SQL constraints and transaction behavior remain real-SQL gates.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class ManualBankPaymentPersistenceModelTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void Payment_and_outgoing_event_are_company_filtered_finance_rows()
    {
        using var context = Context();
        foreach (var type in new[] { typeof(ManualBankPaymentRow), typeof(OutgoingMoneyEventRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(FinanceDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.NotNull(entity.FindProperty("CompanyId"));
        }
    }

    [Fact]
    public void Payment_keeps_money_lossless_and_uses_optimistic_concurrency()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ManualBankPaymentRow))!;

        Assert.Equal(typeof(string), design.FindProperty(nameof(ManualBankPaymentRow.AmountText))!.ClrType);
        Assert.DoesNotContain(design.GetProperties(), property => property.ClrType == typeof(decimal));
        Assert.True(design.FindProperty(nameof(ManualBankPaymentRow.Version))!.IsConcurrencyToken);
        var lifecycle = Assert.Single(design.GetCheckConstraints(), check =>
            check.Name == "CK_ManualBankPayments_Status");
        Assert.Contains("[ApprovedBy] <> [PreparedBy]", lifecycle.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] = 1", lifecycle.Sql, StringComparison.Ordinal);
        Assert.Contains("[BankResultConfirmed] = 0", lifecycle.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] = 2", lifecycle.Sql, StringComparison.Ordinal);
        Assert.Contains("[RecordedBy] IS NOT NULL", lifecycle.Sql, StringComparison.Ordinal);
        Assert.Contains("[BankResultConfirmed] = 1", lifecycle.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Exactly_one_outgoing_event_is_bound_to_each_payment_and_command_results_can_point_to_it()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;
        var events = design.FindEntityType(typeof(OutgoingMoneyEventRow))!;
        var unique = Assert.Single(events.GetIndexes(), index => index.IsUnique);
        Assert.Equal(["CompanyId", "PaymentId"],
            unique.Properties.Select(property => property.Name));
        Assert.Equal(typeof(string), events.FindProperty(nameof(OutgoingMoneyEventRow.AmountText))!.ClrType);

        var source = Assert.Single(events.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(ManualBankPaymentRow));
        Assert.Equal(["CompanyId", "PaymentId"], source.Properties.Select(property => property.Name));

        var commands = design.FindEntityType(typeof(FinanceCommandRow))!;
        Assert.NotNull(commands.FindProperty(nameof(FinanceCommandRow.ManualBankPaymentId)));
        Assert.Contains(commands.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(ManualBankPaymentRow)
            && key.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(FinanceCommandRow.CompanyId), nameof(FinanceCommandRow.ManualBankPaymentId)]));
    }

    [Fact]
    public void Manual_bank_payment_migration_is_discoverable_and_snapshot_matches_the_model()
    {
        using var context = Context();

        Assert.Contains(context.Database.GetMigrations(), migration =>
            migration.EndsWith("_AddManualBankPayments", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
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
