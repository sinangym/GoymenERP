using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceFinancialMovementOutboxTests
{
    private static readonly Guid Company = Guid.Parse("c0280000-0000-4000-8000-000000000001");
    private static readonly Guid Currency = Guid.Parse("c0280000-0000-4000-8000-000000000002");
    private static readonly Guid SourceAccount = Guid.Parse("c0280000-0000-4000-8000-000000000003");
    private static readonly Guid TargetAccount = Guid.Parse("c0280000-0000-4000-8000-000000000004");
    private static readonly DateTimeOffset RecordedAt = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly FinanceContext Context = new(Company, "SENTETIK-recorder", "SENTETIK-correlation", "SENTETIK-cause");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Expense_event_preserves_exact_owner_money_profile_approval_and_causation_without_a_second_movement()
    {
        using var db = Database();
        var source = Expense();
        FinanceFinancialMovementOutboxV1.Expense(db, Context, source);
        var row = Assert.Single(db.OutboxMessages.Local);
        var payload = JsonSerializer.Deserialize<FinanceExpensePaymentRecordedV1>(row.PayloadJson, Json)!;
        Assert.Equal(row.Id, payload.EventId);
        Assert.Equal(source, payload.Source);
        Assert.Equal((source.Id, 3L, Company, RecordedAt), (row.AggregateId, row.AggregateVersion, row.CompanyId, row.OccurredAtUtc));
        Assert.Equal((Context.CorrelationId, Context.CausationId), (row.CorrelationId, row.CausationId));
        Assert.Equal(FinanceFinancialMovementEventsV1.ExpensePaymentRecorded, row.EventType);
        Assert.Equal(1, row.SchemaVersion);
        Assert.Equal("1250.50", payload.Source.Preparation.Money.Amount);
        using var json = JsonDocument.Parse(row.PayloadJson);
        Assert.Equal(new[] { "eventId", "source" }, json.RootElement.EnumerateObject().Select(x => x.Name));
        var wire = json.RootElement.GetProperty("source");
        Assert.Equal(new[] { "id", "companyId", "status", "preparation", "preparedBy", "preparedAtUtc",
            "approvedBy", "approvedAtUtc", "recordedBy", "recordedAtUtc", "paymentConfirmed", "version" },
            wire.EnumerateObject().Select(x => x.Name));
        Assert.Equal("Recorded", wire.GetProperty("status").GetString());
        Assert.Equal("3", wire.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.String, wire.GetProperty("preparation").GetProperty("money").GetProperty("amount").ValueKind);
        Assert.Single(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Transfer_event_keeps_both_legs_on_the_one_canonical_source_and_does_not_publish_currency_exchange()
    {
        using var db = Database();
        var source = Transfer();
        FinanceFinancialMovementOutboxV1.Transfer(db, Context, source);
        var row = Assert.Single(db.OutboxMessages.Local);
        var payload = JsonSerializer.Deserialize<FinanceCashTransferRecordedV1>(row.PayloadJson, Json)!;
        Assert.Equal(source, payload.Source);
        Assert.Equal(row.Id, payload.EventId);
        Assert.Equal(FinanceFinancialMovementEventsV1.CashTransferRecorded, row.EventType);
        Assert.Equal(SourceAccount, payload.Source.Preparation.SourceAccountId);
        Assert.Equal(TargetAccount, payload.Source.Preparation.TargetAccountId);
        using var json = JsonDocument.Parse(row.PayloadJson);
        var preparation = json.RootElement.GetProperty("source").GetProperty("preparation");
        Assert.Equal("SameCurrency", preparation.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, preparation.GetProperty("rate").ValueKind);
        Assert.Equal("1250.50", preparation.GetProperty("sourceMoney").GetProperty("amount").GetString());
        Assert.Equal("1250.50", preparation.GetProperty("targetMoney").GetProperty("amount").GetString());
        db.ChangeTracker.Clear();
        var exchange = source with { Preparation = source.Preparation with { Kind = CashTransferKind.CurrencyExchange, Rate = "2" } };
        Assert.Throws<InvalidOperationException>(() => FinanceFinancialMovementOutboxV1.Transfer(db, Context, exchange));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Unrecorded_unapproved_other_company_and_unconfirmed_sources_cannot_stage_an_event()
    {
        using var db = Database();
        foreach (var source in new[] { Expense() with { Status = ExpensePaymentStatus.Approved },
                     Expense() with { ApprovedBy = "SENTETIK-maker" }, Expense() with { RecordedAtUtc = null },
                     Expense() with { CompanyId = Guid.NewGuid() }, Expense() with { PaymentConfirmed = false } })
            Assert.Throws<InvalidOperationException>(() => FinanceFinancialMovementOutboxV1.Expense(db, Context, source));
        Assert.Throws<InvalidOperationException>(() => FinanceFinancialMovementOutboxV1.Transfer(db, Context,
            Transfer() with { TransferConfirmed = false }));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static ExpensePaymentView Expense() => new(Guid.NewGuid(), Company, ExpensePaymentStatus.Recorded,
        new(ExpensePaymentSourceKind.Bank, SourceAccount, Guid.Parse("c0280000-0000-4000-8000-000000000005"),
            new("1250.50", Currency), new("SENTETIK-profile", "7"), new(2026, 11, 17), null, "SENTETIK-reference", "SENTETIK masraf"),
        "SENTETIK-maker", RecordedAt.AddHours(-2), "SENTETIK-checker", RecordedAt.AddHours(-1), Context.ActorId, RecordedAt, true, "3");
    private static CashTransferView Transfer() => new(Guid.NewGuid(), Company,
        new(CashTransferKind.SameCurrency, CashTransferAccountKind.Bank, SourceAccount, new("1250.50", Currency),
            new("SENTETIK-source-profile", "7"), CashTransferAccountKind.Cash, TargetAccount, new("1250.50", Currency),
            new("SENTETIK-target-profile", "9"), null, new(2026, 11, 17), null, null, "SENTETIK virman"),
        Context.ActorId, RecordedAt, true, "1");
    private static FinanceDbContext Database()
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options, company);
    }
}
