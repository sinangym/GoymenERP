using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

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
        Assert.Equal(128, design.FindProperty(nameof(ManualBankPaymentRow.LinkedDocumentVersion))!.GetMaxLength());
        var lifecycle = Assert.Single(design.GetCheckConstraints(), check =>
            check.Name == "CK_ManualBankPayments_Status");
        Assert.Contains("[ApprovedBy] <> [PreparedBy]", lifecycle.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] = 1", lifecycle.Sql, StringComparison.Ordinal);
        Assert.Contains("[BankResultConfirmed] = 0", lifecycle.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] = 2", lifecycle.Sql, StringComparison.Ordinal);
        Assert.Contains("[RecordedBy] IS NOT NULL", lifecycle.Sql, StringComparison.Ordinal);
        Assert.Contains("[BankResultConfirmed] = 1", lifecycle.Sql, StringComparison.Ordinal);
        var text = Assert.Single(design.GetCheckConstraints(), check => check.Name == "CK_ManualBankPayments_Text");
        Assert.Contains("[LinkedDocumentId] IS NULL AND [LinkedDocumentVersion] IS NULL", text.Sql,
            StringComparison.Ordinal);
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

    [Fact]
    public void Payment_reversal_rows_are_company_filtered_and_the_decision_check_is_the_reviewed_text()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(PaymentReversalProposalRow), typeof(OutgoingMoneyEventReversalRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(FinanceDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
        }

        var proposals = design.FindEntityType(typeof(PaymentReversalProposalRow))!;
        var decision = Assert.Single(proposals.GetCheckConstraints(), check =>
            check.Name == "CK_PaymentReversalProposals_Decision");
        Assert.Equal(FinanceDbContext.PaymentReversalProposalDecisionCheck, decision.Sql);
        Assert.Contains("[ProposedBy] <> [DecidedBy]", decision.Sql, StringComparison.Ordinal);
        Assert.Equal("Latin1_General_100_BIN2",
            proposals.FindProperty(nameof(PaymentReversalProposalRow.ProposedBy))!.GetCollation());
        Assert.Equal("Latin1_General_100_BIN2",
            proposals.FindProperty(nameof(PaymentReversalProposalRow.DecidedBy))!.GetCollation());
        Assert.True(proposals.FindProperty(nameof(PaymentReversalProposalRow.Version))!.IsConcurrencyToken);
        Assert.Equal(["[Status] = 0", "[Status] = 1"], proposals.GetIndexes()
            .Where(index => index.IsUnique && index.GetFilter() is not null)
            .Select(index => index.GetFilter()!).Order(StringComparer.Ordinal));

        var reversals = design.FindEntityType(typeof(OutgoingMoneyEventReversalRow))!;
        Assert.Equal(
            ["CompanyId,OriginalEventId", "CompanyId,PaymentId", "CompanyId,ProposalId"],
            reversals.GetIndexes().Where(index => index.IsUnique && index.GetFilter() is null)
                .Select(index => string.Join(',', index.Properties.Select(property => property.Name)))
                .Order(StringComparer.Ordinal));
        Assert.Equal(typeof(string), reversals.FindProperty(nameof(OutgoingMoneyEventReversalRow.AmountText))!.ClrType);
        Assert.DoesNotContain(reversals.GetProperties(), property => property.ClrType == typeof(decimal));
    }

    [Fact]
    public void A_recorded_reversal_event_is_append_only()
    {
        using var context = Context();
        var row = new OutgoingMoneyEventReversalRow
        {
            Id = Guid.NewGuid(), CompanyId = Company, PaymentId = Guid.NewGuid(), OriginalEventId = Guid.NewGuid(),
            ProposalId = Guid.NewGuid(), PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(),
            TreasuryAccountId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(), AmountText = "10", ProfileId = "p",
            ProfileVersion = "1", OccurredOn = new DateOnly(2026, 11, 11), RecordedBy = "checker",
            CorrelationId = "corr",
        };
        context.OutgoingMoneyEventReversals.Attach(row);
        row.AmountText = "11";

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_payment_reversal_migration_is_pure_ddl_up_and_its_rollback_refuses_first_while_a_reversal_exists()
    {
        var migration = new AddPaymentReversals();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51051", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[PaymentReversalProposals]", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[OutgoingMoneyEventReversals]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
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
