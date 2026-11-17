using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Inventory.UnitTests;

// SQL Server model and owner write admission only. A save-pipeline tripwire prevents any persistence;
// actual lock ordering, approved-count history and rollback races need separate real SQL evidence.
public sealed class InventoryNegativeStockBoundaryModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("82500000-0000-4000-8000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("82500000-0000-4000-8000-000000000002");
    private static readonly Guid Product = Guid.Parse("82500000-0000-4000-8000-000000000003");
    private static readonly Guid Location = Guid.Parse("82500000-0000-4000-8000-000000000004");
    private static readonly Guid Count = Guid.Parse("82500000-0000-4000-8000-000000000005");

    [Fact]
    public void The_technical_mutex_has_one_company_key_and_a_positive_concurrency_version()
    {
        using var context = Context(CompanyA);
        var guard = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(NegativeStockValidationGuardRow))!;
        Assert.Equal(("inventory", "NegativeStockValidationGuards"), (guard.GetSchema(), guard.GetTableName()));
        Assert.Equal(["CompanyId"], Names(guard.FindPrimaryKey()!.Properties));
        var version = guard.FindProperty("Version")!;
        Assert.Equal(typeof(long), version.ClrType);
        Assert.False(version.IsNullable);
        Assert.True(version.IsConcurrencyToken);
        Assert.Contains(guard.GetCheckConstraints(), check => check.Name == "CK_InventoryNegativeStockValidationGuards_Version"
            && check.Sql == "[Version] >= 1");
    }

    [Fact]
    public void A_count_boundary_is_ordered_per_product_and_location_and_has_one_source_per_count()
    {
        using var context = Context(CompanyA);
        var boundary = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(NegativeStockCountBoundaryRow))!;
        Assert.Equal(("inventory", "NegativeStockCountBoundaries"), (boundary.GetSchema(), boundary.GetTableName()));
        Assert.Equal(["CompanyId", "ProductId", "LocationId", "Version"], Names(boundary.FindPrimaryKey()!.Properties));
        Assert.Contains(boundary.GetKeys(), key => Names(key.Properties).SequenceEqual(["CompanyId", "StockCountId", "Version"]));
        Assert.Contains(boundary.GetIndexes(), index => index.IsUnique
            && Names(index.Properties).SequenceEqual(["CompanyId", "StockCountId"]));
        Assert.Contains(boundary.GetCheckConstraints(), check => check.Name == "CK_InventoryNegativeStockCountBoundaries_Version"
            && check.Sql == "[Version] >= 1");
        Assert.False(boundary.FindProperty("Version")!.IsConcurrencyToken);
        Assert.Equal(128, boundary.FindProperty("DecidedBy")!.GetMaxLength());
        Assert.False(boundary.FindProperty("DecidedBy")!.IsNullable);
        Assert.False(boundary.FindProperty("DecidedAtUtc")!.IsNullable);

        var count = Assert.Single(boundary.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(StockCountRow));
        Assert.Equal(["CompanyId", "StockCountId"], Names(count.Properties));
        Assert.Equal(["CompanyId", "Id"], Names(count.PrincipalKey.Properties));
        var decision = Assert.Single(boundary.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(StockCountDecisionRow));
        Assert.Equal(["CompanyId", "StockCountId"], Names(decision.Properties));
        Assert.Equal(["CompanyId", "CountId"], Names(decision.PrincipalKey.Properties));
        Assert.Equal(DeleteBehavior.Restrict, count.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, decision.DeleteBehavior);
        // Approval kind and exact product/location are additionally checked by the owner against these source rows.
    }

    [Fact]
    public void Legacy_versions_remain_nullable_and_new_resolution_proof_names_its_company_count_and_boundary_version()
    {
        using var context = Context(CompanyA);
        var model = context.GetService<IDesignTimeModel>().Model;
        var attempt = model.FindEntityType(typeof(NegativeStockAttemptRow))!;
        var resolution = model.FindEntityType(typeof(NegativeStockAttemptResolutionRow))!;
        Assert.True(attempt.FindProperty("ObservedCountBoundaryVersion")!.IsNullable);
        Assert.True(resolution.FindProperty("CountBoundaryVersion")!.IsNullable);
        Assert.Null(new NegativeStockAttemptRow().ObservedCountBoundaryVersion);
        Assert.Null(new NegativeStockAttemptResolutionRow().CountBoundaryVersion);
        Assert.Contains(attempt.GetCheckConstraints(), check => check.Name == "CK_InventoryNegativeStockAttempts_Boundary"
            && check.Sql == "[ObservedCountBoundaryVersion] IS NULL OR [ObservedCountBoundaryVersion] >= 0");
        Assert.Contains(resolution.GetCheckConstraints(), check => check.Name == "CK_InventoryNegativeStockAttemptResolutions_Boundary"
            && check.Sql == "[CountBoundaryVersion] IS NULL OR [CountBoundaryVersion] >= 1");

        var source = Assert.Single(resolution.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(NegativeStockCountBoundaryRow));
        Assert.Equal(["CompanyId", "StockCountId", "CountBoundaryVersion"], Names(source.Properties));
        Assert.Equal(["CompanyId", "StockCountId", "Version"], Names(source.PrincipalKey.Properties));
        Assert.False(source.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, source.DeleteBehavior);
    }

    [Fact]
    public void Guard_and_boundary_queries_remain_scoped_to_each_context_company_with_a_cached_model()
    {
        using var first = Context(CompanyA);
        using var second = Context(CompanyB);
        foreach (var type in new[] { typeof(NegativeStockValidationGuardRow), typeof(NegativeStockCountBoundaryRow) })
            Assert.Single(first.Model.FindEntityType(type)!.GetDeclaredQueryFilters());
        foreach (var sql in new[] { first.NegativeStockValidationGuards.ToQueryString(), first.NegativeStockCountBoundaries.ToQueryString() })
        {
            Assert.Contains(CompanyA.ToString("D"), sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(CompanyB.ToString("D"), sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[CompanyId] =", sql, StringComparison.Ordinal);
        }
        foreach (var sql in new[] { second.NegativeStockValidationGuards.ToQueryString(), second.NegativeStockCountBoundaries.ToQueryString() })
        {
            Assert.Contains(CompanyB.ToString("D"), sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(CompanyA.ToString("D"), sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_the_technical_guard_version_edit_passes_owner_admission_before_the_blocked_save_pipeline(bool asynchronous)
    {
        var barrier = new StopAtSavePipeline();
        await using var context = Context(CompanyA, barrier);
        var guard = new NegativeStockValidationGuardRow { CompanyId = CompanyA, Version = 3 };
        context.Attach(guard);
        guard.Version = 4;
        if (asynchronous)
            await Assert.ThrowsAsync<SavePipelineReachedException>(() => context.SaveChangesAsync());
        else
            Assert.Throws<SavePipelineReachedException>(() => context.SaveChanges());
        Assert.Equal(1, barrier.Calls);
    }

    [Theory]
    [InlineData("decider", false)]
    [InlineData("decider", true)]
    [InlineData("time", false)]
    [InlineData("time", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    public async Task Count_boundary_decision_evidence_cannot_be_rewritten_or_deleted(string edit, bool asynchronous)
    {
        var barrier = new StopAtSavePipeline();
        await using var context = Context(CompanyA, barrier);
        var boundary = Boundary(CompanyA);
        context.Attach(boundary);
        if (edit == "delete") context.Remove(boundary);
        else if (edit == "time") boundary.DecidedAtUtc = boundary.DecidedAtUtc.AddMinutes(1);
        else boundary.DecidedBy = "synthetic-other-checker";

        if (asynchronous)
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        else
            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Equal(0, barrier.Calls);
    }

    [Theory]
    [InlineData("guard", "foreign")]
    [InlineData("guard", "empty")]
    [InlineData("guard", "unresolved")]
    [InlineData("boundary", "foreign")]
    [InlineData("boundary", "empty")]
    [InlineData("boundary", "unresolved")]
    public async Task Both_new_row_types_require_a_nonempty_matching_resolved_company_before_the_save_pipeline(string kind, string fault)
    {
        var barrier = new StopAtSavePipeline();
        await using var context = Context(fault == "unresolved" ? null : CompanyA, barrier);
        var companyId = fault == "empty" ? Guid.Empty : fault == "foreign" ? CompanyB : CompanyA;
        if (kind == "guard") context.Add(new NegativeStockValidationGuardRow { CompanyId = companyId, Version = 1 });
        else context.Add(Boundary(companyId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Equal(0, barrier.Calls);
    }

    [Theory]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    [InlineData("company", false)]
    [InlineData("company", true)]
    public async Task A_technical_guard_cannot_be_removed_or_moved_to_another_company(string edit, bool asynchronous)
    {
        var barrier = new StopAtSavePipeline();
        await using var context = Context(CompanyA, barrier);
        var guard = new NegativeStockValidationGuardRow { CompanyId = CompanyA, Version = 3 };
        context.Attach(guard);
        if (edit == "delete") context.Remove(guard);
        else guard.CompanyId = CompanyB;
        if (asynchronous)
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        else
            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Equal(0, barrier.Calls);
    }

    private static NegativeStockCountBoundaryRow Boundary(Guid companyId) => new()
    {
        CompanyId = companyId, ProductId = Product, LocationId = Location, Version = 2, StockCountId = Count,
        DecidedAtUtc = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero), DecidedBy = "synthetic-checker",
    };

    private static IEnumerable<string> Names(IEnumerable<IReadOnlyProperty> properties) => properties.Select(property => property.Name);

    private static InventoryDbContext Context(Guid? selectedCompany, StopAtSavePipeline? barrier = null)
    {
        var company = new CompanyContext();
        if (selectedCompany is { } companyId) company.Set(companyId);
        return new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .AddInterceptors(barrier ?? new StopAtSavePipeline()).Options, company);
    }

    private sealed class SavePipelineReachedException : Exception;

    private sealed class StopAtSavePipeline : SaveChangesInterceptor
    {
        public int Calls { get; private set; }
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Calls++;
            throw new SavePipelineReachedException();
        }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new SavePipelineReachedException();
        }
    }
}
