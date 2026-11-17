using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Accounting.UnitTests;

public sealed class FinanceJournalSourceTests
{
    private static readonly Guid Company = Guid.Parse("b2890000-0000-4000-8000-000000000001");
    private static readonly Guid Currency = Guid.Parse("b2890000-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset At = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_actual_recorded_expense_and_same_currency_transfer_keep_the_exact_owner_amount_and_profile(bool transfer)
    {
        var envelope = Envelope(transfer); var company = new CompanyContext(); company.Set(Company);
        var fact = FinanceJournalSourceReaderV1.Read(envelope, company);
        Assert.Equal(Company, fact.CompanyId); Assert.Equal(Currency, fact.CurrencyId);
        Assert.Equal("100.01", fact.Amount); Assert.Equal("SENTETIK-profile", fact.ProfileId);
        Assert.Equal(envelope.EventType, fact.Kind); Assert.Equal("3", fact.Version);
        Assert.Equal(At, fact.RecordedAtUtc);
        Assert.Equal(2, FinanceJournalSourceInboxV1.Subscriptions.Count);
    }
    [Theory]
    [InlineData("eventId")]
    [InlineData("companyId")]
    [InlineData("amount")]
    [InlineData("status")]
    [InlineData("self-approval")]
    [InlineData("version")]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public void Broken_wire_or_owner_proof_is_not_an_accounting_source(string change)
    {
        var envelope = Envelope(false); var root = JsonNode.Parse(envelope.PayloadJson)!;
        var source = root["source"]!;
        switch (change)
        {
            case "eventId": root["eventId"] = Guid.NewGuid(); break;
            case "companyId": source["companyId"] = Guid.NewGuid(); break;
            case "amount": source["preparation"]!["money"]!["amount"] = "100.00000000000000000000000000001"; break;
            case "status": source["status"] = "Approved"; break;
            case "self-approval": source["approvedBy"] = "SENTETIK-maker"; break;
            case "version": source["version"] = "03"; break;
            case "unknown": root["invented"] = true; break;
            case "missing": root.AsObject().Remove("eventId"); break;
        }
        var json = root.ToJsonString();
        if (change == "duplicate") json = json.Insert(1, "\"eventId\":\"" + envelope.EventId + "\",");
        var company = new CompanyContext(); company.Set(Company);
        Assert.Throws<OutboxPermanentFailureException>(() => FinanceJournalSourceReaderV1.Read(envelope with { PayloadJson = json }, company));
    }
    [Fact]
    public void Foreign_company_wrong_schema_and_exchange_are_refused_without_inventing_an_fx_journal()
    {
        var company = new CompanyContext(); company.Set(Company); var envelope = Envelope(true);
        Assert.Throws<OutboxPermanentFailureException>(() => FinanceJournalSourceReaderV1.Read(envelope with { Version = 2 }, company));
        Assert.Throws<OutboxPermanentFailureException>(() => FinanceJournalSourceReaderV1.Read(envelope with { CompanyId = Guid.NewGuid() }, company));
        var root = JsonNode.Parse(envelope.PayloadJson)!;
        root["source"]!["preparation"]!["kind"] = "CurrencyExchange";
        root["source"]!["preparation"]!["rate"] = "2";
        Assert.Throws<OutboxPermanentFailureException>(() => FinanceJournalSourceReaderV1.Read(envelope with { PayloadJson = root.ToJsonString() }, company));
    }
    [Fact]
    public void Queue_model_has_company_filter_unique_source_identity_and_no_mutable_financial_fields()
    {
        var company = new CompanyContext(); company.Set(Company);
        using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options, company);
        var row = db.Model.FindEntityType(typeof(FinanceJournalSourceRow))!;
        Assert.NotEmpty(row.GetDeclaredQueryFilters());
        Assert.Equal(new[] { "CompanyId", "Id" }, row.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Contains(row.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "CompanyId", "SourceId", "SourceKind", "SourceVersion" }));
        var source = new FinanceJournalSourceRow { CompanyId = Company, Id = Guid.NewGuid(), SourceId = Guid.NewGuid(),
            SourceKind = NormalJournalSourceKindsV1.RecordedExpensePayment, SourceVersion = "3", CurrencyId = Currency,
            AmountText = "100.01", OwnerProfileId = "SENTETIK-profile", OwnerProfileVersion = "1", SourceHash = new('a', 64),
            SourceJson = "{}", OccurredOn = new(2026, 11, 17), RecordedAtUtc = At, ReceivedAtUtc = At };
        db.Attach(source); source.AmountText = "200.02";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear(); db.Attach(source); db.Remove(source);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }
    [Fact]
    public void Received_source_migration_is_ddl_only_and_refuses_to_drop_financial_provenance()
    {
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddFinanceJournalSources();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51989", guard.Sql); Assert.Contains("FinanceJournalSources", guard.Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        var company = new CompanyContext(); company.Set(Company);
        using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options, company);
        var model = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(FinanceJournalSourceRow))!;
        Assert.Equal(AccountingDbContext.FinanceJournalSourceIdentityCheck, model.GetCheckConstraints().Single(x => x.Name == "CK_FinanceJournalSources_Identity").Sql);
        Assert.Equal(AccountingDbContext.FinanceJournalSourcePayloadCheck, model.GetCheckConstraints().Single(x => x.Name == "CK_FinanceJournalSources_Payload").Sql);
        Assert.Equal(AccountingDbContext.FinanceJournalSourceKindCheck, model.GetCheckConstraints().Single(x => x.Name == "CK_FinanceJournalSources_Source").Sql);
    }
    internal static WorkInboxEventV1 Envelope(bool transfer)
    {
        var id = Guid.NewGuid(); var sourceId = Guid.NewGuid();
        var expense = new ExpensePaymentView(sourceId, Company, ExpensePaymentStatus.Recorded,
            new(ExpensePaymentSourceKind.Bank, Guid.NewGuid(), Guid.NewGuid(), new("100.01", Currency),
                new("SENTETIK-profile", "1"), new(2026, 11, 17), null, null, "SENTETIK expense"),
            "SENTETIK-maker", At.AddHours(-2), "SENTETIK-checker", At.AddHours(-1), "SENTETIK-recorder", At, true, "3");
        var cash = new CashTransferView(sourceId, Company,
            new(CashTransferKind.SameCurrency, CashTransferAccountKind.Bank, Guid.NewGuid(), new("100.01", Currency),
                new("SENTETIK-profile", "1"), CashTransferAccountKind.Cash, Guid.NewGuid(), new("100.01", Currency),
                new("SENTETIK-target", "2"), null, new(2026, 11, 17), null, null, null), "SENTETIK-recorder", At, true, "3");
        return new(FinanceJournalSourceInboxV1.SourceName, id, Company,
            transfer ? NormalJournalSourceKindsV1.RecordedCashTransfer : NormalJournalSourceKindsV1.RecordedExpensePayment, 1,
            transfer ? JsonSerializer.Serialize(new FinanceCashTransferRecordedV1(id, cash), FinanceJson.CreateOptions())
                : JsonSerializer.Serialize(new FinanceExpensePaymentRecordedV1(id, expense), FinanceJson.CreateOptions()), At, "SENTETIK-source", null);
    }
}
