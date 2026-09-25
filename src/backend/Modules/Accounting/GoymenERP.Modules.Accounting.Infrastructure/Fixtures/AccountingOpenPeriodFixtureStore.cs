using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Accounting.Infrastructure.Fixtures;

/// <summary>Explicit test-only port. The host registers it only behind the existing complete browser fixture gate.</summary>
public interface IAccountingOpenPeriodFixtureStore
{
    Task EnsureAsync(CancellationToken cancellationToken);
}

public static class AccountingOpenPeriodFixtureStoreRegistration
{
    public static IServiceCollection AddAccountingOpenPeriodFixtureStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IAccountingOpenPeriodFixtureStore, AccountingOpenPeriodFixtureStore>();
        return services;
    }
}

/// <summary>
/// Fixed synthetic FIN-01 period. It is not a production period, grant or accounting command; two distinct
/// synthetic actors are retained so the stored Open state still satisfies the Accounting owner's O4 facts.
/// </summary>
public static class AccountingOpenPeriodFixture
{
    public static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid PeriodId = Guid.Parse("b7000000-0000-4000-8000-000000000501");
    public static readonly Guid PreparedByUserId = Guid.Parse("b2000000-0000-4000-8000-00000000f101");
    public static readonly Guid ApprovedByUserId = Guid.Parse("b2000000-0000-4000-8000-00000000f102");
    public static readonly DateOnly StartsOn = new DateOnly(2026, 9, 1);
    public static readonly DateOnly EndsOn = new DateOnly(2026, 9, 30);
    public static readonly DateTimeOffset PreparedAtUtc = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset ApprovedAtUtc = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
}

internal sealed class AccountingOpenPeriodFixtureStore(
    AccountingDbContext dbContext,
    ICompanyContext companyContext) : IAccountingOpenPeriodFixtureStore
{
    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (dbContext.ChangeTracker.Entries().Any())
        {
            throw new InvalidOperationException("An Accounting period fixture requires a clean context.");
        }

        if (!companyContext.IsResolved)
        {
            companyContext.Set(AccountingOpenPeriodFixture.CompanyId);
        }
        else if (companyContext.CompanyId != AccountingOpenPeriodFixture.CompanyId)
        {
            throw Conflict();
        }

        var periods = await dbContext.Periods.ToListAsync(cancellationToken);
        var calendars = await dbContext.PeriodCalendars.ToListAsync(cancellationToken);
        if (periods.Count == 0 && calendars.Count == 0)
        {
            dbContext.PeriodCalendars.Add(new PeriodCalendarRow
            {
                CompanyId = AccountingOpenPeriodFixture.CompanyId,
                Version = 1,
                UpdatedAtUtc = AccountingOpenPeriodFixture.ApprovedAtUtc,
            });
            dbContext.Periods.Add(new PeriodRow
            {
                Id = AccountingOpenPeriodFixture.PeriodId,
                CompanyId = AccountingOpenPeriodFixture.CompanyId,
                StartsOn = AccountingOpenPeriodFixture.StartsOn,
                EndsOn = AccountingOpenPeriodFixture.EndsOn,
                Status = (int)AccountingPeriodStatus.Open,
                PreparedByUserId = AccountingOpenPeriodFixture.PreparedByUserId,
                PreparedAtUtc = AccountingOpenPeriodFixture.PreparedAtUtc,
                ApprovedByUserId = AccountingOpenPeriodFixture.ApprovedByUserId,
                ApprovedAtUtc = AccountingOpenPeriodFixture.ApprovedAtUtc,
                Version = 2,
                CreatedAtUtc = AccountingOpenPeriodFixture.PreparedAtUtc,
                UpdatedAtUtc = AccountingOpenPeriodFixture.ApprovedAtUtc,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (periods.Count != 1 || calendars.Count != 1)
        {
            throw Conflict();
        }

        var period = periods[0];
        var calendar = calendars[0];
        if (period.Id != AccountingOpenPeriodFixture.PeriodId
            || period.CompanyId != AccountingOpenPeriodFixture.CompanyId
            || period.StartsOn != AccountingOpenPeriodFixture.StartsOn
            || period.EndsOn != AccountingOpenPeriodFixture.EndsOn
            || period.Status != (int)AccountingPeriodStatus.Open
            || period.PreparedByUserId != AccountingOpenPeriodFixture.PreparedByUserId
            || period.PreparedAtUtc != AccountingOpenPeriodFixture.PreparedAtUtc
            || period.ApprovedByUserId != AccountingOpenPeriodFixture.ApprovedByUserId
            || period.ApprovedAtUtc != AccountingOpenPeriodFixture.ApprovedAtUtc
            || period.ClosedByUserId is not null
            || period.ClosedAtUtc is not null
            || period.Version != 2
            || period.CreatedAtUtc != AccountingOpenPeriodFixture.PreparedAtUtc
            || period.UpdatedAtUtc != AccountingOpenPeriodFixture.ApprovedAtUtc
            || calendar.CompanyId != AccountingOpenPeriodFixture.CompanyId
            || calendar.Version != 1
            || calendar.UpdatedAtUtc != AccountingOpenPeriodFixture.ApprovedAtUtc)
        {
            throw Conflict();
        }
    }

    private static InvalidOperationException Conflict() =>
        new InvalidOperationException("The Accounting calendar is not the identical synthetic fin01 open-period fixture.");
}
