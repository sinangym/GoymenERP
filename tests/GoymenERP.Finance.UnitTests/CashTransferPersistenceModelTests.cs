using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FNS-D24 persistence shape without a database. SQL constraints and transaction behavior remain real-SQL gates.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class CashTransferPersistenceModelTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void Transfer_and_command_rows_are_company_filtered_finance_rows()
    {
        using var context = Context();
        foreach (var type in new[] { typeof(CashTransferRow), typeof(CashTransferCommandRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(FinanceDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.NotNull(entity.FindProperty("CompanyId"));
        }
    }

    [Fact]
    public void Transfer_keeps_both_legs_and_the_rate_lossless_on_one_row()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CashTransferRow))!;

        Assert.DoesNotContain(design.GetProperties(), property => property.ClrType == typeof(decimal)
            || property.ClrType == typeof(double) || property.ClrType == typeof(float));
        Assert.Equal(typeof(string), design.FindProperty(nameof(CashTransferRow.SourceAmountText))!.ClrType);
        Assert.Equal(typeof(string), design.FindProperty(nameof(CashTransferRow.TargetAmountText))!.ClrType);
        Assert.True(design.FindProperty(nameof(CashTransferRow.RateText))!.IsNullable);
        Assert.True(design.FindProperty(nameof(CashTransferRow.ExchangeSnapshotJson))!.IsNullable);
        Assert.Equal("nvarchar(max)", design.FindProperty(nameof(CashTransferRow.ExchangeSnapshotJson))!.GetColumnType());
        Assert.True(design.FindProperty(nameof(CashTransferRow.Version))!.IsConcurrencyToken);
        Assert.Equal("Latin1_General_100_BIN2",
            design.FindProperty(nameof(CashTransferRow.RecordedBy))!.GetCollation());

        var kind = Assert.Single(design.GetCheckConstraints(), check => check.Name == "CK_CashTransfers_Kind");
        Assert.Contains("[Kind] = 0 AND [SourceCurrencyId] = [TargetCurrencyId] AND [SourceAmountText] = [TargetAmountText] "
            + "AND [RateText] IS NULL", kind.Sql, StringComparison.Ordinal);
        Assert.Contains("[Kind] = 1 AND [SourceCurrencyId] <> [TargetCurrencyId] AND [RateText] IS NOT NULL", kind.Sql,
            StringComparison.Ordinal);
        var distinct = Assert.Single(design.GetCheckConstraints(),
            check => check.Name == "CK_CashTransfers_DistinctAccounts");
        Assert.Contains("[SourceAccountId] = [TargetAccountId]", distinct.Sql, StringComparison.Ordinal);
        Assert.Contains(design.GetCheckConstraints(), check => check.Name == "CK_CashTransfers_Confirmed"
            && check.Sql == "[TransferConfirmed] = 1");
        Assert.Contains(design.GetCheckConstraints(), check => check.Name == "CK_CashTransfers_Version"
            && check.Sql == "[Version] = 1");
        Assert.Contains(design.GetCheckConstraints(), check => check.Name == "CK_CashTransfers_Dates");
        Assert.Contains(design.GetCheckConstraints(), check => check.Name == "CK_CashTransfers_ExchangeSnapshot"
            && check.Sql == "[ExchangeSnapshotJson] IS NULL OR ([Kind] = 1 AND ISJSON([ExchangeSnapshotJson]) = 1)");
    }

    [Fact]
    public void Command_keys_are_unique_per_company_and_command_and_point_to_the_transfer()
    {
        using var context = Context();
        var commands = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CashTransferCommandRow))!;

        var unique = Assert.Single(commands.GetIndexes(), index => index.IsUnique);
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"],
            unique.Properties.Select(property => property.Name));
        Assert.Contains(commands.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(CashTransferRow)
            && key.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(CashTransferCommandRow.CompanyId), nameof(CashTransferCommandRow.TransferId)]));
    }

    [Fact]
    public void A_committed_transfer_is_append_only()
    {
        using var context = Context();
        var row = Transfer();
        context.CashTransfers.Attach(row);
        row.TargetAmountText = "2";

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void A_committed_transfer_cannot_be_deleted()
    {
        using var context = Context();
        var row = Transfer();
        context.CashTransfers.Attach(row);
        context.CashTransfers.Remove(row);

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void A_transfer_command_receipt_is_append_only()
    {
        using var context = Context();
        var row = new CashTransferCommandRow
        {
            Id = Guid.NewGuid(), CompanyId = Company, CommandName = "c", IdempotencyKeyHash = new string('a', 64),
            ActorId = "SENTETIK-treasurer", PayloadHash = new string('b', 64), TransferId = Guid.NewGuid(),
            ResultJson = "{}", CorrelationId = "corr",
        };
        context.CashTransferCommands.Attach(row);
        row.ResultJson = "{\"changed\":true}";

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void Cash_transfer_migration_is_discoverable_and_snapshot_matches_the_model()
    {
        using var context = Context();

        Assert.Contains(context.Database.GetMigrations(), migration =>
            migration.EndsWith("_AddCashTransfers", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration =>
            migration.EndsWith("_AddCashTransferExchangeSnapshots", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_cash_transfer_migration_is_pure_ddl_up_and_its_rollback_refuses_first_while_rows_exist()
    {
        var migration = new AddCashTransfers();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51458", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[CashTransfers]", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[CashTransferCommands]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }

    [Fact]
    public void Exchange_snapshot_migration_adds_only_nullable_evidence_and_refuses_rollback_before_any_drop()
    {
        var migration = new AddCashTransferExchangeSnapshots();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var column = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        Assert.Equal(("finance", "CashTransfers", "ExchangeSnapshotJson", "nvarchar(max)", true),
            (column.Schema, column.Table, column.Name, column.ColumnType, column.IsNullable));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51933", guard, StringComparison.Ordinal);
        Assert.Contains("[ExchangeSnapshotJson] IS NOT NULL", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    private static CashTransferRow Transfer() => new()
    {
        Id = Guid.NewGuid(), CompanyId = Company, Version = 1, Kind = CashTransferKinds.SameCurrency,
        SourceKind = CashTransferAccountKinds.Bank, SourceAccountId = Guid.NewGuid(), SourceCurrencyId = Guid.NewGuid(),
        SourceAmountText = "1", SourceProfileId = "p", SourceProfileVersion = "1",
        TargetKind = CashTransferAccountKinds.Cash, TargetAccountId = Guid.NewGuid(), TargetCurrencyId = Guid.NewGuid(),
        TargetAmountText = "1", TargetProfileId = "p", TargetProfileVersion = "1",
        OccurredOn = new DateOnly(2026, 11, 10), TransferConfirmed = true, RecordedBy = "SENTETIK-treasurer",
        RecordedAtUtc = DateTimeOffset.UnixEpoch,
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
