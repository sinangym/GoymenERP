using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// Actual SQL Server provider translation only: no connection is opened and no SQL acceptance
/// or authorization result is claimed. The complete owner query must remain server-translatable.
/// </summary>
public sealed class PartyLedgerSqlTranslationTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Account = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);

    [Fact]
    public void Actual_opening_ledger_query_keeps_date_order_and_limit_in_company_filtered_sql()
    {
        var company = new CompanyContext();
        company.Set(Company);
        using var db = Context(company);
        var service = new PartyLedgerService(db, company, new SyntheticUser(), new SyntheticPermissions());
        // Keep the production helper private; compose its actual expression without executing it.
        var method = typeof(PartyLedgerService).GetMethod("OpeningMovementsQuery",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(service, [Party, Account]));
        var sql = CompleteOpeningQuery(query).ToQueryString();

        Assert.Contains("[finance].[OpeningBalanceMovements]", sql, StringComparison.Ordinal);
        Assert.Contains("[CompanyId]", sql, StringComparison.Ordinal);
        Assert.Contains("[PartyId]", sql, StringComparison.Ordinal);
        Assert.Contains("[TargetId]", sql, StringComparison.Ordinal);
        Assert.Contains("[OccurredOn] >=", sql, StringComparison.Ordinal);
        Assert.Contains("[OccurredOn] <=", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("[Id]", sql, StringComparison.Ordinal);
        Assert.Contains("TOP(", sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    private static IQueryable CompleteOpeningQuery(IQueryable source)
    {
        // The production projection type is private. These are the same server-side operators
        // ReadCoreAsync appends to OpeningMovementsQuery: inclusive date range, stable order, cap.
        var row = Expression.Parameter(source.ElementType, "row");
        var occurredOn = Expression.Property(row, "OccurredOn");
        var within = Expression.AndAlso(
            Expression.GreaterThanOrEqual(occurredOn, Expression.Constant(From)),
            Expression.LessThanOrEqual(occurredOn, Expression.Constant(To)));
        var filtered = Expression.Call(typeof(Queryable), nameof(Queryable.Where), [source.ElementType],
            source.Expression, Expression.Quote(Expression.Lambda(within, row)));
        var ordered = Expression.Call(typeof(Queryable), nameof(Queryable.OrderBy), [source.ElementType, typeof(DateOnly)],
            filtered, Expression.Quote(Expression.Lambda(occurredOn, row)));
        var stable = Expression.Call(typeof(Queryable), nameof(Queryable.ThenBy), [source.ElementType, typeof(Guid)],
            ordered, Expression.Quote(Expression.Lambda(Expression.Property(row, "Id"), row)));
        var limited = Expression.Call(typeof(Queryable), nameof(Queryable.Take), [source.ElementType],
            stable, Expression.Constant(17));
        return source.Provider.CreateQuery(limited);
    }

    private static FinanceDbContext Context(CompanyContext company) => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);

    private sealed class SyntheticUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-query-translation";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? resourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }
}
