using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Inventory.UnitTests;

// SQL Server model and pre-connection write guards only; these are not relational execution evidence.
public sealed class InventoryNegativeStockAttemptModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("82100000-0000-4000-8000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("82100000-0000-4000-8000-000000000002");

    [Fact]
    public void Refusals_have_a_company_bound_dedup_key_and_an_index_for_count_resolution()
    {
        using var context = Context(CompanyA);
        var attempt = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(NegativeStockAttemptRow))!;

        Assert.Equal(("inventory", "NegativeStockAttempts"), (attempt.GetSchema(), attempt.GetTableName()));
        Assert.Equal(["Id"], attempt.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(attempt.GetKeys(), key => Names(key.Properties).SequenceEqual(["CompanyId", "Id"]));
        Assert.Contains(attempt.GetIndexes(), index => index.IsUnique && Names(index.Properties)
            .SequenceEqual(["CompanyId", "MovementId", "ActorId", "KeyHash"]));
        Assert.Contains(attempt.GetIndexes(), index => Names(index.Properties)
            .SequenceEqual(["CompanyId", "ProductId", "LocationId", "Id"]));
        Assert.Contains(attempt.GetIndexes(), index => Names(index.Properties)
            .SequenceEqual(["CompanyId", "RefusedAtUtc", "Id"]));
        // Refused movements and labels can have rolled back, so their evidence must stand alone.
        Assert.Empty(attempt.GetForeignKeys());
    }

    [Fact]
    public void Kilograms_preserve_three_decimal_places_and_refusal_math_without_requiring_nonnegative_available_stock()
    {
        using var context = Context(CompanyA);
        var attempt = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(NegativeStockAttemptRow))!;

        foreach (var name in new[] { "RequestedKg", "AvailableKg", "MissingKg" })
        {
            var property = attempt.FindProperty(name)!;
            Assert.Equal((18, 3), (property.GetPrecision(), property.GetScale()));
            Assert.False(property.IsNullable);
        }

        var state = Assert.Single(attempt.GetCheckConstraints(), item => item.Name == "CK_InventoryNegativeStockAttempts_State");
        Assert.Equal("CK_InventoryNegativeStockAttempts_State", state.Name);
        Assert.Equal("[RequestedKg] > 0 AND [AvailableKg] < [RequestedKg] AND [MissingKg] = [RequestedKg] - [AvailableKg] AND [LineNumber] >= 1",
            state.Sql);
        foreach (var name in new[] { "ActorId", "PermissionCode", "CorrelationId", "SourceChannel", "LabelCode", "SourceKind" })
        {
            Assert.Equal(128, attempt.FindProperty(name)!.GetMaxLength());
            Assert.False(attempt.FindProperty(name)!.IsNullable);
        }
        Assert.Equal(64, attempt.FindProperty("KeyHash")!.GetMaxLength());
        Assert.Equal(64, attempt.FindProperty("PayloadHash")!.GetMaxLength());
    }

    [Fact]
    public void A_resolution_is_unique_per_company_attempt_and_cannot_reference_a_foreign_count_or_refusal()
    {
        using var context = Context(CompanyA);
        var resolution = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(NegativeStockAttemptResolutionRow))!;

        Assert.Equal(("inventory", "NegativeStockAttemptResolutions"), (resolution.GetSchema(), resolution.GetTableName()));
        Assert.Equal(["CompanyId", "AttemptId"], Names(resolution.FindPrimaryKey()!.Properties));
        var attempt = Assert.Single(resolution.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(NegativeStockAttemptRow));
        var count = Assert.Single(resolution.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(StockCountRow));
        Assert.Equal(["CompanyId", "AttemptId"], Names(attempt.Properties));
        Assert.Equal(["CompanyId", "StockCountId"], Names(count.Properties));
        foreach (var foreignKey in new[] { attempt, count })
        {
            Assert.Equal(["CompanyId", "Id"], Names(foreignKey.PrincipalKey.Properties));
            Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        }
        Assert.Equal(128, resolution.FindProperty("DecidedBy")!.GetMaxLength());
        Assert.False(resolution.FindProperty("DecidedBy")!.IsNullable);
    }

    [Fact]
    public void Both_query_filters_use_each_context_company_even_when_the_model_is_cached()
    {
        using var first = Context(CompanyA);
        using var second = Context(CompanyB);
        foreach (var type in new[] { typeof(NegativeStockAttemptRow), typeof(NegativeStockAttemptResolutionRow) })
            Assert.Single(first.Model.FindEntityType(type)!.GetDeclaredQueryFilters());

        var firstQueries = new[] { first.NegativeStockAttempts.ToQueryString(), first.NegativeStockAttemptResolutions.ToQueryString() };
        var secondQueries = new[] { second.NegativeStockAttempts.ToQueryString(), second.NegativeStockAttemptResolutions.ToQueryString() };
        foreach (var query in firstQueries)
        {
            Assert.Contains(CompanyA.ToString("D"), query, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(CompanyB.ToString("D"), query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[CompanyId] =", query, StringComparison.Ordinal);
        }
        foreach (var query in secondQueries)
        {
            Assert.Contains(CompanyB.ToString("D"), query, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(CompanyA.ToString("D"), query, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("attempt", "edit", false)]
    [InlineData("attempt", "edit", true)]
    [InlineData("attempt", "delete", false)]
    [InlineData("attempt", "delete", true)]
    [InlineData("resolution", "edit", false)]
    [InlineData("resolution", "edit", true)]
    [InlineData("resolution", "delete", false)]
    [InlineData("resolution", "delete", true)]
    public async Task Existing_attempts_and_resolutions_cannot_be_edited_or_deleted_before_any_connection_opens(
        string kind, string action, bool asynchronous)
    {
        var tripwire = new NoConnectionOpen();
        await using var context = Context(CompanyA, tripwire);
        var row = Row(kind, CompanyA);
        context.Attach(row);
        if (action == "delete") context.Remove(row);
        else if (row is NegativeStockAttemptRow attempt) attempt.AvailableKg = 0.5m;
        else ((NegativeStockAttemptResolutionRow)row).DecidedBy = "synthetic-other-checker";

        if (asynchronous)
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        else
            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Equal(0, tripwire.OpenAttempts);
    }

    [Theory]
    [InlineData("attempt", "foreign")]
    [InlineData("attempt", "empty")]
    [InlineData("attempt", "unresolved")]
    [InlineData("resolution", "foreign")]
    [InlineData("resolution", "empty")]
    [InlineData("resolution", "unresolved")]
    public async Task Added_evidence_requires_a_nonempty_company_matching_the_resolved_context_before_sql(string kind, string fault)
    {
        var tripwire = new NoConnectionOpen();
        await using var context = Context(fault == "unresolved" ? null : CompanyA, tripwire);
        context.Add(Row(kind, fault == "empty" ? Guid.Empty : fault == "foreign" ? CompanyB : CompanyA));

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Equal(0, tripwire.OpenAttempts);
    }

    private static IEnumerable<string> Names(IEnumerable<IReadOnlyProperty> properties) => properties.Select(property => property.Name);

    private static object Row(string kind, Guid companyId) => kind switch
    {
        "attempt" => new NegativeStockAttemptRow
        {
            Id = Guid.Parse("82100000-0000-4000-8000-000000000003"), CompanyId = companyId,
            MovementId = Guid.Parse("82100000-0000-4000-8000-000000000004"),
            ProductId = Guid.Parse("82100000-0000-4000-8000-000000000005"),
            LocationId = Guid.Parse("82100000-0000-4000-8000-000000000006"),
            LabelId = Guid.Parse("82100000-0000-4000-8000-000000000007"), LabelCode = "SYNTHETIC-REFUSAL",
            ActorId = "synthetic-maker", KeyHash = new string('a', 64), PayloadHash = new string('b', 64),
            PermissionCode = "inventory.movements.create", CorrelationId = "synthetic-refusal-correlation",
            SourceChannel = "synthetic-test", RefusedAtUtc = DateTimeOffset.Parse("2026-11-17T09:00:00+00:00"),
            MovementKind = 2, LineNumber = 1, SourceKind = "synthetic-physical-exit",
            SourceId = Guid.Parse("82100000-0000-4000-8000-000000000008"),
            RequestedKg = 2m, AvailableKg = 1m, MissingKg = 1m,
        },
        "resolution" => new NegativeStockAttemptResolutionRow
        {
            CompanyId = companyId, AttemptId = Guid.Parse("82100000-0000-4000-8000-000000000003"),
            StockCountId = Guid.Parse("82100000-0000-4000-8000-000000000009"),
            DecidedAtUtc = DateTimeOffset.Parse("2026-11-17T10:00:00+00:00"), DecidedBy = "synthetic-checker",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static InventoryDbContext Context(Guid? selectedCompany, NoConnectionOpen? tripwire = null)
    {
        var company = new CompanyContext();
        if (selectedCompany is { } companyId) company.Set(companyId);
        return new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .AddInterceptors(tripwire ?? new NoConnectionOpen()).Options, company);
    }

    private sealed class UnexpectedDatabaseAccessException : Exception;

    private sealed class NoConnectionOpen : DbConnectionInterceptor
    {
        public int OpenAttempts { get; private set; }

        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result)
        {
            OpenAttempts++;
            throw new UnexpectedDatabaseAccessException();
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            OpenAttempts++;
            throw new UnexpectedDatabaseAccessException();
        }
    }
}
