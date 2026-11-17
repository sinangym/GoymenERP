using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

public sealed class CashExchangeBookValueProofTests
{
    private static readonly Guid Company = Guid.Parse("f1400000-0000-4000-8000-000000000001");
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();
    private static readonly DateTimeOffset At = new(2026, 11, 18, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Recorded_FX_event_preserves_actual_two_legs_and_TCMB_reference_without_valued_success()
    {
        using var db = Database();
        var source = Source();
        FinanceFinancialMovementOutboxV1.Exchange(db, new(Company, source.RecordedBy, "SENTETIK-record", null), source);
        var row = Assert.Single(db.OutboxMessages.Local);
        var payload = JsonSerializer.Deserialize<FinanceCashExchangeRecordedV1>(row.PayloadJson, Json)!;
        Assert.Equal(source, payload.Source);
        Assert.Equal((source.Id, 1L, FinanceFinancialMovementEventsV1.CashExchangeRecorded), (row.AggregateId, row.AggregateVersion, row.EventType));
        Assert.Equal("100", payload.Source.Preparation.SourceMoney.Amount);
        Assert.Equal("3300", payload.Source.Preparation.TargetMoney.Amount);
        Assert.Equal("3400", payload.Source.Exchange!.SourceReferenceTl!.Amount);
        Assert.DoesNotContain("sourceBookValueTl", row.PayloadJson);
        Assert.Single(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Pending_projection_preserves_exact_document_and_original_source_then_requires_a_real_distinct_checker()
    {
        var row = Proposal();
        var pending = CashExchangeBookValueServiceV1.View(row, null);
        Assert.Equal(("3500.00", "1", CashExchangeBookValueStatusV1.Pending), (pending.SourceBookValueTl, pending.Version, pending.Status));
        Assert.Equal("3300.00", pending.TargetFunctionalValueTl);
        var decision = Decision(row);
        var approved = CashExchangeBookValueServiceV1.View(row, decision);
        Assert.Equal((pending.SourceBookValueTl, pending.Evidence, "2", CashExchangeBookValueStatusV1.Approved),
            (approved.SourceBookValueTl, approved.Evidence, approved.Version, approved.Status));
        decision.DecidedBy = row.PreparedBy;
        Assert.Throws<JsonException>(() => CashExchangeBookValueServiceV1.View(row, decision));
        decision = Decision(row); decision.CompanyId = Guid.NewGuid();
        Assert.Throws<JsonException>(() => CashExchangeBookValueServiceV1.View(row, decision));
        row.TransferSourceHash = new string('a', 64);
        Assert.Throws<JsonException>(() => CashExchangeBookValueServiceV1.View(row, null));
    }

    [Fact]
    public void Approval_publishes_only_the_document_backed_book_proof_and_does_not_stage_a_second_transfer()
    {
        using var db = Database();
        var proposal = Proposal();
        var value = CashExchangeBookValueServiceV1.View(proposal, Decision(proposal));
        FinanceFinancialMovementOutboxV1.ExchangeBookValue(db, new(Company, value.DecidedBy!, "SENTETIK-approve", null), Source(), value);
        var row = Assert.Single(db.OutboxMessages.Local);
        var payload = JsonSerializer.Deserialize<FinanceCashExchangeBookValueApprovedV1>(row.PayloadJson, Json)!;
        Assert.Equal((value.Id, 2L, value.DecidedAtUtc), (row.AggregateId, row.AggregateVersion, (DateTimeOffset?)row.OccurredAtUtc));
        Assert.Equal("3500.00", payload.BookValue.SourceBookValueTl);
        Assert.Equal("3300.00", payload.BookValue.TargetFunctionalValueTl);
        Assert.Equal("3400", payload.Source.Exchange!.SourceReferenceTl!.Amount);
        Assert.Empty(db.CashTransfers.Local); Assert.Empty(db.OutgoingMoneyEvents.Local);
    }

    [Fact]
    public void Two_foreign_legs_preserve_the_documented_target_TL_value_independently_of_TCMB()
    {
        using var db = Database();
        var proposal = Proposal(foreignTarget: true);
        var value = CashExchangeBookValueServiceV1.View(proposal, Decision(proposal));
        Assert.Equal("3600.00", value.TargetFunctionalValueTl);
        var source = Source(foreignTarget: true);
        Assert.Equal("3240", source.Exchange!.TargetReferenceTl!.Amount);
        FinanceFinancialMovementOutboxV1.ExchangeBookValue(db, new(Company, value.DecidedBy!, "SENTETIK-two-FX", null), source, value);
        var payload = JsonSerializer.Deserialize<FinanceCashExchangeBookValueApprovedV1>(Assert.Single(db.OutboxMessages.Local).PayloadJson, Json)!;
        Assert.Equal(("3500.00", "3600.00", "3240"), (payload.BookValue.SourceBookValueTl, payload.BookValue.TargetFunctionalValueTl, payload.Source.Exchange!.TargetReferenceTl!.Amount));
        Assert.Empty(db.CashTransfers.Local); Assert.Empty(db.OutgoingMoneyEvents.Local);
    }

    [Theory]
    [InlineData("3300", true)]
    [InlineData("3300.00", true)]
    [InlineData("3300.0000000000000000000000000001", false)]
    [InlineData("3400", false)]
    [InlineData("0", false)]
    [InlineData("-3300", false)]
    [InlineData("3.3e3", false)]
    public void Target_TRY_admission_uses_exact_numeric_identity_and_never_the_benchmark(string target, bool matches)
        => Assert.Equal(matches, CashExchangeBookValueServiceV1.TargetMatchesSource(Source(), target));

    [Fact]
    public void Legacy_null_target_roundtrips_without_changing_hash_but_cannot_publish_a_new_two_value_approval()
    {
        using var db = Database();
        var row = Proposal(legacy: true);
        var oldJson = row.PreparationJson;
        Assert.DoesNotContain("targetFunctionalValueTl", oldJson);
        var request = JsonSerializer.Deserialize<ProposeCashExchangeBookValueV1>(oldJson, Json)!;
        Assert.True(CashExchangeBookValueServiceV1.Valid(request));
        Assert.False(CashExchangeBookValueServiceV1.ValidNew(request));
        Assert.Equal(oldJson, JsonSerializer.Serialize(request, Json));
        Assert.Equal(CashExchangeBookValueServiceV1.Hash(oldJson), CashExchangeBookValueServiceV1.Hash(JsonSerializer.Serialize(request, Json)));
        var pending = CashExchangeBookValueServiceV1.View(row, null);
        var approved = CashExchangeBookValueServiceV1.View(row, Decision(row));
        Assert.Null(pending.TargetFunctionalValueTl); Assert.Null(approved.TargetFunctionalValueTl);
        Assert.DoesNotContain("targetFunctionalValueTl", JsonSerializer.Serialize(approved, Json));
        Assert.Throws<InvalidOperationException>(() => FinanceFinancialMovementOutboxV1.ExchangeBookValue(db,
            new(Company, approved.DecidedBy!, "SENTETIK-legacy", null), Source(), approved));
        Assert.Empty(db.OutboxMessages.Local);
    }

    [Fact]
    public void Book_proof_rows_and_receipts_cannot_be_updated_deleted_or_written_under_another_company()
    {
        using var db = Database();
        var row = Proposal();
        db.Attach(row); row.PreparationJson = "{}";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear(); row = Proposal(); db.Remove(row);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear(); row = Proposal(); row.CompanyId = Guid.NewGuid(); db.Add(row);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        var decision = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CashExchangeBookValueDecisionRow))!;
        Assert.Contains(decision.GetIndexes(), x => x.IsUnique && x.GetFilter() == "[Approved] = 1");
        Assert.Contains(decision.GetForeignKeys(), x => x.Properties.Any(p => p.Name == nameof(CashExchangeBookValueDecisionRow.PreparedBy)));
    }

    [Fact]
    public void Book_proof_migration_matches_model_and_keeps_all_immutable_evidence_before_any_rollback_drop()
    {
        using var db = Database();
        Assert.Contains(db.Database.GetMigrations(), x => x.EndsWith("_AddCashExchangeBookValues", StringComparison.Ordinal));
        Assert.False(db.Database.HasPendingModelChanges());
        var migration = new AddCashExchangeBookValues();
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(3, tables.Length);
        Assert.All(tables, x => Assert.Equal("finance", x.Schema));
        var scripts = migration.UpOperations.OfType<SqlOperation>().Select(x => x.Sql).ToArray();
        Assert.Equal(3, scripts.Length);
        Assert.Contains(scripts, x => x.Contains("THROW 51912", StringComparison.Ordinal));
        Assert.Contains(scripts, x => x.Contains("THROW 51913", StringComparison.Ordinal));
        Assert.Contains(scripts, x => x.Contains("THROW 51914", StringComparison.Ordinal));
        Assert.All(scripts, x => Assert.Contains("AFTER UPDATE, DELETE", x, StringComparison.Ordinal));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51915", guard, StringComparison.Ordinal);
        Assert.All(tables, x => Assert.Contains($"[finance].[{x.Name}]", guard, StringComparison.Ordinal));
        Assert.Equal(3, migration.DownOperations.OfType<DropTableOperation>().Count());
    }

    internal static CashTransferView Source(bool foreignTarget = false)
    {
        var source = new MoneyText("100", SettingsCurrencyIdentitiesV1.UnitedStatesDollar);
        var target = new MoneyText(foreignTarget ? "90" : "3300", foreignTarget
            ? Guid.Parse("f1400000-0000-4000-8000-000000000009") : SettingsCurrencyIdentitiesV1.TurkishLira);
        var profile = new ProfileReference("f1400000-0000-4000-8000-000000000002", "1");
        var comparison = new CashTransferComparisonSourceV1(source.CurrencyId, "34", new(2026, 11, 18), "Tcmb", Guid.Parse("f1400000-0000-4000-8000-000000000004"), "1", new(2026, 11, 18), "SENTETIK-source");
        var functional = foreignTarget
            ? new CashTransferComparisonSourceV1(target.CurrencyId, "36", new(2026, 11, 18), "Tcmb", Guid.Parse("f1400000-0000-4000-8000-000000000010"), "1", new(2026, 11, 18), "SENTETIK-target")
            : new CashTransferComparisonSourceV1(target.CurrencyId, "1", new(2026, 11, 18), "FunctionalCurrency", null, null, null, null);
        var snapshot = CashTransferExchangeCalculatorV1.Calculate(Company, new(2026, 11, 18), source, target, profile, profile, comparison, functional);
        return new(Guid.Parse("f1400000-0000-4000-8000-000000000003"), Company,
            new(CashTransferKind.CurrencyExchange, CashTransferAccountKind.Bank, Guid.Parse("f1400000-0000-4000-8000-000000000005"), source, profile,
                CashTransferAccountKind.Bank, Guid.Parse("f1400000-0000-4000-8000-000000000006"), target, profile, snapshot.TransactionRate, new(2026, 11, 18), null, null, null),
            "SENTETIK-treasurer", At, true, "1", snapshot);
    }
    private static CashExchangeBookValueProposalRow Proposal(bool legacy = false, bool foreignTarget = false)
    {
        var source = Source(foreignTarget); var json = JsonSerializer.Serialize(source, Json);
        var request = new ProposeCashExchangeBookValueV1(source.Id, "1", "3500.00", new(Guid.NewGuid(), 1, new string('b', 64)), "SENTETIK gerçek kayıt",
            legacy ? null : foreignTarget ? "3600.00" : "3300.00");
        return new() { Id = Guid.NewGuid(), CompanyId = Company, TransferId = source.Id, TransferSourceJson = json,
            TransferSourceHash = CashExchangeBookValueServiceV1.Hash(json), PreparationJson = JsonSerializer.Serialize(request, Json),
            PreparedBy = "SENTETIK-accountant-maker", PreparedAtUtc = At };
    }
    private static CashExchangeBookValueDecisionRow Decision(CashExchangeBookValueProposalRow row) => new()
    { CompanyId = Company, ProposalId = row.Id, TransferId = row.TransferId, Approved = true,
        PreparedBy = row.PreparedBy, DecidedBy = "SENTETIK-accountant-checker", DecidedAtUtc = At.AddMinutes(1) };
    private static FinanceDbContext Database()
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options, company);
    }
}
