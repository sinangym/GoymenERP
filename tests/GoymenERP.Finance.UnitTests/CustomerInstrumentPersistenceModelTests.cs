using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// DEC-FIN-016 persistence without a database: the two tables are company-filtered in the finance schema, the face
/// value is text, one instrument per identity and per collection source, one movement per version, the command record
/// names the instrument, and the write guard admits only an allowed status step (facts frozen, movements append-only).
/// What SQL Server enforces is proven by <c>FinanceCustomerInstrumentSqlServerRelationalAcceptanceTests</c>. SENTETIK.
/// </summary>
public sealed class CustomerInstrumentPersistenceModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At = new(2026, 9, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_portfolio_tables_are_company_filtered_text_money_with_their_uniqueness()
    {
        using var context = SqlModelContext();
        var design = context.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(CustomerInstrumentRow), typeof(CustomerInstrumentMovementRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(FinanceDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
        }

        var instruments = design.FindEntityType(typeof(CustomerInstrumentRow))!;
        Assert.Equal(typeof(string), instruments.FindProperty(nameof(CustomerInstrumentRow.AmountText))!.ClrType);
        Assert.DoesNotContain(instruments.GetProperties(), property => property.ClrType == typeof(decimal));
        Assert.True(instruments.FindProperty(nameof(CustomerInstrumentRow.Version))!.IsConcurrencyToken);
        var identity = Assert.Single(instruments.GetIndexes(), index => index.GetDatabaseName() == "UX_CustomerInstruments_Identity");
        Assert.True(identity.IsUnique);
        // Unfiltered, so a note (no bank) is covered as well.
        Assert.Null(identity.GetFilter());
        var collection = Assert.Single(instruments.GetIndexes(),
            index => index.GetDatabaseName() == "UX_CustomerInstruments_CollectionSource");
        Assert.True(collection.IsUnique);
        Assert.Contains(instruments.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(ReceiptDraftRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "CollectionSourceId"]));
        Assert.Contains(instruments.GetCheckConstraints(), check => check.Name == "CK_CustomerInstruments_State"
            && check.Sql == FinanceDbContext.CustomerInstrumentStateCheck);

        var movements = design.FindEntityType(typeof(CustomerInstrumentMovementRow))!;
        Assert.Contains(movements.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "InstrumentId", "Version"]));

        var commands = design.FindEntityType(typeof(FinanceCommandRow))!;
        Assert.Contains(commands.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(CustomerInstrumentRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "CustomerInstrumentId"]));
        Assert.Contains("[CustomerInstrumentId] IS NOT NULL", FinanceDbContext.TargetCheck, StringComparison.Ordinal);
        Assert.Contains(design.FindEntityType(typeof(MoneyEventRow))!.GetCheckConstraints(),
            check => check.Name == "CK_MoneyEvents_Kind" && check.Sql == "[Kind] IN (1, 2, 3)");
    }

    [Theory]
    [InlineData("deposit")]
    [InlineData("collect")]
    [InlineData("bounce-from-portfolio")]
    public void An_allowed_step_with_its_version_passes_the_write_guard(string step)
    {
        using var context = MemoryContext();
        var row = Instrument();
        context.CustomerInstruments.Add(row);
        context.SaveChanges();

        row.Version = 2;
        row.UpdatedAtUtc = At.AddMinutes(1);
        switch (step)
        {
            case "deposit":
                row.Status = (int)CustomerInstrumentStatus.AtBankForCollection;
                row.TreasuryAccountId = Guid.NewGuid();
                break;
            case "collect":
                row.Status = (int)CustomerInstrumentStatus.Collected;
                row.TreasuryAccountId = Guid.NewGuid();
                row.CollectionSourceId = Guid.NewGuid();
                break;
            default:
                row.Status = (int)CustomerInstrumentStatus.Bounced;
                break;
        }

        context.SaveChanges();
    }

    [Theory]
    [InlineData("edit-amount")]
    [InlineData("edit-serial")]
    [InlineData("edit-party")]
    [InlineData("skip-version")]
    [InlineData("reopen-collected")]
    [InlineData("note-bounces")]
    [InlineData("return-from-bank")]
    [InlineData("edit-movement")]
    [InlineData("delete-instrument")]
    [InlineData("delete-movement")]
    public void The_facts_are_frozen_and_only_an_allowed_step_changes_an_instrument(string fault)
    {
        using var context = MemoryContext();
        var row = Instrument();
        if (fault == "reopen-collected")
        {
            row.Status = (int)CustomerInstrumentStatus.Collected;
            row.TreasuryAccountId = Guid.NewGuid();
            row.CollectionSourceId = Guid.NewGuid();
        }
        else if (fault == "return-from-bank")
        {
            row.Status = (int)CustomerInstrumentStatus.AtBankForCollection;
            row.TreasuryAccountId = Guid.NewGuid();
        }
        else if (fault == "note-bounces")
        {
            row.Kind = (int)CustomerInstrumentKind.PromissoryNote;
            row.BankName = null;
        }

        var movement = new CustomerInstrumentMovementRow
        {
            Id = Guid.NewGuid(), CompanyId = CompanyA, InstrumentId = row.Id, Version = 1,
            ToStatus = row.Status, OccurredOn = row.ReceivedOn, RecordedBy = "synthetic-actor", RecordedAtUtc = At,
            CorrelationId = "synthetic-correlation",
        };
        context.CustomerInstruments.Add(row);
        context.CustomerInstrumentMovements.Add(movement);
        context.SaveChanges();

        switch (fault)
        {
            case "edit-amount":
                row.AmountText = "1";
                row.Version = 2;
                break;
            case "edit-serial":
                row.SerialNumber = "SENTETIK-OTHER";
                row.Version = 2;
                break;
            case "edit-party":
                row.PartyId = Guid.NewGuid();
                row.Version = 2;
                break;
            case "skip-version":
                row.Status = (int)CustomerInstrumentStatus.Bounced;
                row.Version = 3;
                break;
            case "reopen-collected":
                row.Status = (int)CustomerInstrumentStatus.InPortfolio;
                row.TreasuryAccountId = null;
                row.CollectionSourceId = null;
                row.Version = 2;
                break;
            case "note-bounces":
                row.Status = (int)CustomerInstrumentStatus.Bounced;
                row.Version = 2;
                break;
            case "return-from-bank":
                row.Status = (int)CustomerInstrumentStatus.ReturnedToCustomer;
                row.TreasuryAccountId = null;
                row.Version = 2;
                break;
            case "edit-movement":
                movement.Note = "changed";
                break;
            case "delete-movement":
                context.CustomerInstrumentMovements.Remove(movement);
                break;
            default:
                // Only the instrument tracked, so the guard (not EF's relationship fix-up) answers.
                context.ChangeTracker.Clear();
                context.CustomerInstruments.Remove(context.CustomerInstruments.Attach(row).Entity);
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    private static CustomerInstrumentRow Instrument() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, Version = 1, Kind = (int)CustomerInstrumentKind.Cheque,
        Status = (int)CustomerInstrumentStatus.InPortfolio, PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(),
        SerialNumber = "SENTETIK-CEK-1", Drawer = "SENTETIK Keşideci", BankName = "SENTETIK Banka",
        DueOn = new DateOnly(2026, 10, 15), ReceivedOn = new DateOnly(2026, 9, 20), CurrencyId = Guid.NewGuid(),
        AmountText = "1000.00", ProfileId = Guid.NewGuid().ToString("D"), ProfileVersion = "1",
        StatusChangedOn = new DateOnly(2026, 9, 20), ReceivedBy = "synthetic-actor", ReceivedAtUtc = At,
        UpdatedBy = "synthetic-actor", UpdatedAtUtc = At,
    };

    private static FinanceDbContext MemoryContext()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"instrument-model-{Guid.NewGuid():N}").Options, company);
    }

    private static FinanceDbContext SqlModelContext()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}
