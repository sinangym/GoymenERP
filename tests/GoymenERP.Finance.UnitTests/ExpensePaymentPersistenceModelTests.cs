using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FNS-D25 persistence shape without a database. SQL constraints and transaction behavior remain real-SQL gates.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class ExpensePaymentPersistenceModelTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void Payment_and_command_rows_are_company_filtered_finance_rows()
    {
        using var context = Context();
        foreach (var type in new[] { typeof(ExpensePaymentRow), typeof(ExpensePaymentCommandRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(FinanceDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.NotNull(entity.FindProperty("CompanyId"));
        }
    }

    [Fact]
    public void Payment_keeps_money_lossless_the_item_by_identity_and_a_mandatory_description()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ExpensePaymentRow))!;

        Assert.Equal(typeof(string), design.FindProperty(nameof(ExpensePaymentRow.AmountText))!.ClrType);
        Assert.DoesNotContain(design.GetProperties(), property => property.ClrType == typeof(decimal));
        Assert.Equal(typeof(Guid), design.FindProperty(nameof(ExpensePaymentRow.ExpenseItemId))!.ClrType);
        Assert.True(design.FindProperty(nameof(ExpensePaymentRow.Version))!.IsConcurrencyToken);
        Assert.False(design.FindProperty(nameof(ExpensePaymentRow.Description))!.IsNullable);
        Assert.Equal(1000, design.FindProperty(nameof(ExpensePaymentRow.Description))!.GetMaxLength());

        var text = Assert.Single(design.GetCheckConstraints(), check => check.Name == "CK_ExpensePayments_Text");
        Assert.Contains("LEN(LTRIM(RTRIM([Description]))) > 0", text.Sql, StringComparison.Ordinal);
        var identity = Assert.Single(design.GetCheckConstraints(), check => check.Name == "CK_ExpensePayments_Identity");
        Assert.Contains("[ExpenseItemId] <> '00000000-0000-0000-0000-000000000000'", identity.Sql,
            StringComparison.Ordinal);
        var lifecycle = Assert.Single(design.GetCheckConstraints(), check => check.Name == "CK_ExpensePayments_Status");
        Assert.Equal(FinanceDbContext.ExpensePaymentStatusCheck, lifecycle.Sql);
        Assert.Contains("[ApprovedBy] <> [PreparedBy]", lifecycle.Sql, StringComparison.Ordinal);
        Assert.Contains("[PaymentConfirmed] = 1", lifecycle.Sql, StringComparison.Ordinal);
        Assert.Equal("Latin1_General_100_BIN2",
            design.FindProperty(nameof(ExpensePaymentRow.PreparedBy))!.GetCollation());
        Assert.Equal("Latin1_General_100_BIN2",
            design.FindProperty(nameof(ExpensePaymentRow.ApprovedBy))!.GetCollation());
    }

    [Fact]
    public void Command_keys_are_unique_per_company_and_command_and_point_to_the_payment()
    {
        using var context = Context();
        var commands = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ExpensePaymentCommandRow))!;

        var unique = Assert.Single(commands.GetIndexes(), index => index.IsUnique);
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"],
            unique.Properties.Select(property => property.Name));
        Assert.Contains(commands.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(ExpensePaymentRow)
            && key.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ExpensePaymentCommandRow.CompanyId), nameof(ExpensePaymentCommandRow.PaymentId)]));
    }

    [Fact]
    public void A_command_receipt_is_append_only()
    {
        using var context = Context();
        var row = new ExpensePaymentCommandRow
        {
            Id = Guid.NewGuid(), CompanyId = Company, CommandName = "c", IdempotencyKeyHash = new string('a', 64),
            ActorId = "SENTETIK-maker", PayloadHash = new string('b', 64), ResultJson = "{}", CorrelationId = "corr",
        };
        context.ExpensePaymentCommands.Attach(row);
        row.ResultJson = "{\"changed\":true}";

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_preparer_cannot_approve_their_own_draft_at_the_save_guard()
    {
        using var context = Context();
        var row = Draft();
        context.ExpensePayments.Attach(row);
        row.Status = 1;
        row.ApprovedBy = row.PreparedBy;
        row.ApprovedAtUtc = DateTimeOffset.UnixEpoch;
        row.Version = 2;

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void A_draft_cannot_jump_to_recorded_at_the_save_guard()
    {
        using var context = Context();
        var row = Draft();
        context.ExpensePayments.Attach(row);
        row.Status = 2;
        row.RecordedBy = "SENTETIK-checker";
        row.RecordedAtUtc = DateTimeOffset.UnixEpoch;
        row.PaymentConfirmed = true;
        row.Version = 2;

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void Expense_payment_migration_is_discoverable_and_snapshot_matches_the_model()
    {
        using var context = Context();

        Assert.Contains(context.Database.GetMigrations(), migration =>
            migration.EndsWith("_AddExpensePayments", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_expense_payment_migration_is_pure_ddl_up_and_its_rollback_refuses_first_while_rows_exist()
    {
        var migration = new AddExpensePayments();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51296", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[ExpensePayments]", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[ExpensePaymentCommands]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }

    private static ExpensePaymentRow Draft() => new()
    {
        Id = Guid.NewGuid(), CompanyId = Company, Version = 1, SourceKind = 1, TreasuryAccountId = Guid.NewGuid(),
        ExpenseItemId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(), AmountText = "10", ProfileId = "p",
        ProfileVersion = "1", OccurredOn = new DateOnly(2026, 11, 10), Description = "SENTETIK gider",
        Status = 0, PreparedBy = "SENTETIK-maker", PreparedAtUtc = DateTimeOffset.UnixEpoch,
    };

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
