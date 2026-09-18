using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// The persistent model without a database: schema, tenant filters, the R4 unique identities,
/// the snapshot and the write guard, which refuses before any connection is opened. What SQL
/// Server itself enforces is proven by the relational acceptance tests, not here.
/// </summary>
public sealed class AccountingPersistenceModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private static readonly Type[] TenantRows =
    [
        typeof(PeriodCalendarRow), typeof(PeriodRow), typeof(JournalRow), typeof(JournalLineRow), typeof(DeliveryRow),
        typeof(PostingExceptionRow), typeof(PendingValuationRow),
    ];

    [Fact]
    public void Every_accounting_table_is_in_its_schema_and_filtered_by_the_active_company()
    {
        using var context = Context();
        foreach (var type in TenantRows)
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(AccountingDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.NotNull(entity.FindProperty("CompanyId"));
        }

        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
        Assert.Single(audit.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_r4_identities_are_unique_keys_of_the_company()
    {
        using var context = Context();
        var deliveries = context.Model.FindEntityType(typeof(DeliveryRow))!;
        Assert.Equal(["CompanyId", "Consumer", "MessageId"],
            deliveries.FindPrimaryKey()!.Properties.Select(property => property.Name));

        var journals = context.Model.FindEntityType(typeof(JournalRow))!;
        var original = Assert.Single(journals.GetIndexes(), index => index.GetDatabaseName() == "UX_Journals_Original");
        var reversal = Assert.Single(journals.GetIndexes(), index => index.GetDatabaseName() == "UX_Journals_Reversal");
        var correction = Assert.Single(journals.GetIndexes(), index => index.GetDatabaseName() == "UX_Journals_Correction");
        Assert.True(original.IsUnique && reversal.IsUnique && correction.IsUnique);
        Assert.Equal(["CompanyId", "SourceId", "PostingPurpose"], original.Properties.Select(property => property.Name));
        Assert.Equal("[Kind] = 1", original.GetFilter());
        Assert.Equal(["CompanyId", "LinkedJournalId"], reversal.Properties.Select(property => property.Name));
        Assert.Equal("[Kind] = 2", reversal.GetFilter());
        Assert.Equal(["CompanyId", "CorrectionId"], correction.Properties.Select(property => property.Name));
        Assert.Equal("[CorrectionId] IS NOT NULL", correction.GetFilter());
        // No group-wide identity: every unique identity starts with the company.
        Assert.All(journals.GetIndexes().Where(index => index.IsUnique),
            index => Assert.Equal("CompanyId", index.Properties[0].Name));
    }

    [Fact]
    public void Identity_texts_compare_exactly_and_the_period_is_a_concurrency_token()
    {
        using var context = Context();
        // Collation is design-time configuration; the read-optimized runtime model does not keep it.
        var designModel = context.GetService<IDesignTimeModel>().Model;
        foreach (var (type, property) in new[]
                 {
                     (typeof(JournalRow), nameof(JournalRow.PostingPurpose)),
                     (typeof(DeliveryRow), nameof(DeliveryRow.Consumer)),
                     (typeof(DeliveryRow), nameof(DeliveryRow.MessageId)),
                     (typeof(PostingExceptionRow), nameof(PostingExceptionRow.PostingPurpose)),
                     (typeof(PendingValuationRow), nameof(PendingValuationRow.PostingPurpose)),
                 })
        {
            Assert.Equal(AccountingDbContext.IdentityCollation,
                designModel.FindEntityType(type)!.FindProperty(property)!.GetCollation());
        }

        var periods = context.Model.FindEntityType(typeof(PeriodRow))!;
        Assert.True(periods.FindProperty(nameof(PeriodRow.Version))!.IsConcurrencyToken);
        Assert.True(periods.FindProperty(nameof(PeriodRow.RowVersion))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(PeriodCalendarRow))!.FindProperty(nameof(PeriodCalendarRow.Version))!
            .IsConcurrencyToken);
    }

    [Fact]
    public void Journals_reference_their_period_and_linked_journal_by_company_and_id()
    {
        using var context = Context();
        var journals = context.Model.FindEntityType(typeof(JournalRow))!;
        Assert.Contains(journals.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PeriodRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PeriodId"]));
        Assert.Contains(journals.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(JournalRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LinkedJournalId"]));
        var lines = context.Model.FindEntityType(typeof(JournalLineRow))!;
        Assert.Contains(lines.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(JournalRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "JournalId"]));
    }

    [Fact]
    public void The_migration_is_discoverable_and_the_snapshot_matches_the_model()
    {
        using var context = Context();
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_InitialAccounting", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("no-company-context")]
    [InlineData("empty-company")]
    public void A_row_of_another_company_or_without_a_company_is_refused_before_the_database(string fault)
    {
        using var context = Context(resolved: fault != "no-company-context");
        context.Periods.Add(new PeriodRow
        {
            Id = Guid.NewGuid(),
            CompanyId = fault switch { "other-company" => CompanyB, "empty-company" => Guid.Empty, _ => CompanyA },
            StartsOn = new DateOnly(2026, 9, 1),
            EndsOn = new DateOnly(2026, 9, 30),
            Status = 1,
            Version = 1,
            CreatedAtUtc = At,
            UpdatedAtUtc = At,
        });

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Theory]
    [InlineData("delete-journal")]
    [InlineData("edit-journal")]
    [InlineData("edit-line")]
    [InlineData("edit-delivery")]
    [InlineData("edit-period-dates")]
    [InlineData("move-exception-to-another-company")]
    public void Stored_accounting_is_never_deleted_and_only_its_allowed_fields_change(string fault)
    {
        using var context = Context();
        switch (fault)
        {
            case "delete-journal":
                context.Journals.Remove(Attach(context, Journal()));
                break;
            case "edit-journal":
                Attach(context, Journal()).PostingRuleVersion = "synthetic-rule-changed";
                break;
            case "edit-line":
                Attach(context, Line()).Debit = 2m;
                break;
            case "edit-delivery":
                Attach(context, Delivery()).OutcomeKind = 3;
                break;
            case "edit-period-dates":
                Attach(context, Period()).EndsOn = new DateOnly(2026, 10, 31);
                break;
            default:
                Attach(context, QueueEntry()).CompanyId = CompanyB;
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    private static T Attach<T>(AccountingDbContext context, T row) where T : class
    {
        context.Attach(row);
        return row;
    }

    private static PeriodRow Period() => new PeriodRow
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, StartsOn = new DateOnly(2026, 9, 1), EndsOn = new DateOnly(2026, 9, 30),
        Status = 1, Version = 1, CreatedAtUtc = At, UpdatedAtUtc = At,
    };

    private static JournalRow Journal() => new JournalRow
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, Kind = 1, SourceId = Guid.NewGuid(), PostingPurpose = "synthetic-purpose",
        PostingRuleVersion = "synthetic-rule-1", CurrencyId = Guid.NewGuid(), TransactionDate = new DateOnly(2026, 9, 14),
        AccountingDate = new DateOnly(2026, 9, 15), RecordedAtUtc = At, PeriodId = Guid.NewGuid(), StoredAtUtc = At,
        StoredBy = "synthetic-actor", CorrelationId = "synthetic-correlation",
    };

    private static JournalLineRow Line() => new JournalLineRow
    {
        CompanyId = CompanyA, JournalId = Guid.NewGuid(), LineNumber = 1, AccountId = Guid.NewGuid(), Debit = 1m, Credit = 0m,
    };

    private static DeliveryRow Delivery() => new DeliveryRow
    {
        CompanyId = CompanyA, Consumer = "synthetic-consumer", MessageId = "m-1", OutcomeKind = 1, JournalId = Guid.NewGuid(),
        RecordedAtUtc = At, CorrelationId = "synthetic-correlation",
    };

    private static PostingExceptionRow QueueEntry() => new PostingExceptionRow
    {
        CompanyId = CompanyA, SourceId = Guid.NewGuid(), PostingPurpose = "synthetic-purpose", Status = 1,
        PostingRuleVersion = "synthetic-rule-1", MissingMappingKeysJson = "[\"k\"]", FirstMessageId = "m-1", Version = 1,
        UpdatedAtUtc = At,
    };

    /// <summary>A context of company A, or of no resolved company at all.</summary>
    private static AccountingDbContext Context(bool resolved = true)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new AccountingDbContext(options, company);
    }
}
