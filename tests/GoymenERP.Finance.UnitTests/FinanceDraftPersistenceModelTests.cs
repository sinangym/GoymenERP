using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FIN-T04a persistent model without a database: schema, tenant filters, the idempotency key, the
/// snapshot, the write guard (refuses before any connection) and the canonical payload hash. What
/// SQL Server enforces is proven by the relational acceptance tests, not here. SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinanceDraftPersistenceModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_finance_table_is_in_its_schema_and_filtered_by_the_active_company()
    {
        using var context = Context();
        foreach (var type in new[] { typeof(ReceiptDraftRow), typeof(FinanceCommandRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(FinanceDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.NotNull(entity.FindProperty("CompanyId"));
        }

        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
        Assert.Single(audit.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_command_record_follows_adr_011_company_command_and_key_hash_with_the_actor_kept()
    {
        using var context = Context();
        var commands = context.Model.FindEntityType(typeof(FinanceCommandRow))!;
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"],
            commands.FindPrimaryKey()!.Properties.Select(property => property.Name));
        // §2: the raw key is not a column at all.
        Assert.DoesNotContain(commands.GetProperties(), property => property.Name == "IdempotencyKey");
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(FinanceCommandRow))!;
        Assert.Equal(FinanceDbContext.IdentityCollation, design.FindProperty(nameof(FinanceCommandRow.ActorId))!.GetCollation());
        Assert.False(design.FindProperty(nameof(FinanceCommandRow.ActorId))!.IsNullable);
        Assert.Contains(design.GetCheckConstraints(), check => check.Name == "CK_Commands_KeyHash");
        // The key hash is the trimmed key's upper-case SHA-256 (ADR-011 §2).
        Assert.Equal(64, FinanceCommandKeys.Hash("k-1").Length);
        Assert.Equal(FinanceCommandKeys.Hash("k-1"), FinanceCommandKeys.Hash("k-1").ToUpperInvariant());
        Assert.Equal(FinanceCommandKeys.Hash("k-1"), FinanceCommandKeys.Hash(" k-1 "));
        Assert.NotEqual(FinanceCommandKeys.Hash("k-1"), FinanceCommandKeys.Hash("K-1"));
        var drafts = context.Model.FindEntityType(typeof(ReceiptDraftRow))!;
        Assert.True(drafts.FindProperty(nameof(ReceiptDraftRow.Version))!.IsConcurrencyToken);
        // No money is stored as a database decimal: the amount is the exact text that passed the profile.
        Assert.Equal(typeof(string), drafts.FindProperty(nameof(ReceiptDraftRow.AmountText))!.ClrType);
        Assert.DoesNotContain(drafts.GetProperties(), property => property.ClrType == typeof(decimal));
    }

    [Fact]
    public void The_migration_is_discoverable_and_the_snapshot_matches_the_model()
    {
        using var context = Context();
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_InitialFinanceDrafts", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AlignCommandsWithAdr011", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("no-company-context")]
    [InlineData("empty-company")]
    public void A_row_of_another_company_or_without_a_company_is_refused_before_the_database(string fault)
    {
        using var context = Context(resolved: fault != "no-company-context");
        var row = Draft();
        row.CompanyId = fault switch { "other-company" => CompanyB, "empty-company" => Guid.Empty, _ => CompanyA };
        context.ReceiptDrafts.Add(row);

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Theory]
    [InlineData("delete-draft")]
    [InlineData("edit-draft-creator")]
    [InlineData("edit-command")]
    public void Drafts_are_never_deleted_and_command_records_never_change(string fault)
    {
        using var context = Context();
        switch (fault)
        {
            case "delete-draft":
                context.ReceiptDrafts.Remove(Attach(context, Draft()));
                break;
            case "edit-draft-creator":
                Attach(context, Draft()).CreatedBy = "someone-else";
                break;
            default:
                Attach(context, Command()).PayloadHash = new string('1', 64);
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_payload_hash_is_stable_and_names_every_field()
    {
        var request = Request("1000");
        Assert.Equal(64, ReceiptDraftService.PayloadHash(request).Length);
        Assert.Equal(ReceiptDraftService.PayloadHash(request), ReceiptDraftService.PayloadHash(Request("1000")));
        Assert.NotEqual(ReceiptDraftService.PayloadHash(request), ReceiptDraftService.PayloadHash(Request("1000.0")));
        Assert.NotEqual(ReceiptDraftService.PayloadHash(request),
            ReceiptDraftService.PayloadHash(request with { Preparation = request.Preparation with { Description = "x" } }));
        Assert.NotEqual(ReceiptDraftService.PayloadHash(request),
            ReceiptDraftService.PayloadHash(request with { DraftId = Guid.NewGuid(), ExpectedVersion = "1" }));
        Assert.Equal("9007199254740993", ReceiptDraftService.VersionText(9007199254740993L));
    }

    private static DraftSaveRequest Request(string amount) => new DraftSaveRequest(null, null, new ReceiptPreparation(
        Guid.Parse("44444444-4444-4444-4444-444444444444"), Guid.Parse("55555555-5555-5555-5555-555555555555"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        new MoneyText(amount, Guid.Parse("22222222-2222-2222-2222-222222222222")),
        new ProfileReference("77777777-7777-7777-7777-777777777777", "1"), new DateOnly(2026, 9, 13), null, null, null));

    private static T Attach<T>(FinanceDbContext context, T row) where T : class
    {
        context.Attach(row);
        return row;
    }

    private static ReceiptDraftRow Draft() => new ReceiptDraftRow
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, Version = 1, PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(),
        TreasuryAccountId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(), AmountText = "1000", ProfileId = Guid.NewGuid().ToString("D"),
        ProfileVersion = "1", OccurredOn = new DateOnly(2026, 9, 13), CreatedAtUtc = At, CreatedBy = "synthetic-actor",
        UpdatedAtUtc = At, UpdatedBy = "synthetic-actor",
    };

    private static FinanceCommandRow Command() => new FinanceCommandRow
    {
        CompanyId = CompanyA, CommandName = ReceiptDraftService.SaveDraftCommand, IdempotencyKeyHash = FinanceCommandKeys.Hash("k-1"), ActorId = "synthetic-actor",
        PayloadHash = new string('0', 64), DraftId = Guid.NewGuid(), ResultJson = "{}", CorrelationId = "synthetic-correlation",
        RecordedAtUtc = At,
    };

    /// <summary>A context of company A, or of no resolved company at all.</summary>
    private static FinanceDbContext Context(bool resolved = true)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new FinanceDbContext(options, company);
    }
}
