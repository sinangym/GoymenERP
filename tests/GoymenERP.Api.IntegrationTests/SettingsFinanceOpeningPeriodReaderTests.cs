using GoymenERP.Api.Shell;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KIM-S19: the host adapter answering Finance's opening period reader from the Settings company policy registry. It never
/// assumes the period open: nothing recorded, a refused or failing read, another company's answer or a non-positive version
/// is <see cref="FinanceOpeningPeriodAnswer.NotAvailable"/>; open and closed carry a version text that changes per version.
/// </summary>
public sealed class SettingsFinanceOpeningPeriodReaderTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Nothing_recorded_is_not_available()
    {
        var reader = new SettingsFinanceOpeningPeriodReader(
            new FakePolicies(_ => new SettingsFinancePolicyV1(CompanyId, FinanceOpeningPeriodStateV1.NotRecorded, 0)));

        Assert.Equal(FinanceOpeningPeriodAnswer.NotAvailable, await reader.ReadAsync(CompanyId, CancellationToken.None));
    }

    [Fact]
    public async Task Open_and_closed_carry_a_version_text_that_changes_on_every_open_or_close()
    {
        var open = await new SettingsFinanceOpeningPeriodReader(
                new FakePolicies(_ => new SettingsFinancePolicyV1(CompanyId, FinanceOpeningPeriodStateV1.Open, 1)))
            .ReadAsync(CompanyId, CancellationToken.None);
        var closed = await new SettingsFinanceOpeningPeriodReader(
                new FakePolicies(_ => new SettingsFinancePolicyV1(CompanyId, FinanceOpeningPeriodStateV1.Closed, 2)))
            .ReadAsync(CompanyId, CancellationToken.None);
        var reopened = await new SettingsFinanceOpeningPeriodReader(
                new FakePolicies(_ => new SettingsFinancePolicyV1(CompanyId, FinanceOpeningPeriodStateV1.Open, 3)))
            .ReadAsync(CompanyId, CancellationToken.None);

        Assert.Equal(OpeningPeriodState.Open, open.State);
        Assert.Equal(OpeningPeriodState.Closed, closed.State);
        Assert.Equal(OpeningPeriodState.Open, reopened.State);
        Assert.Equal(3, new[] { open.Version, closed.Version, reopened.Version }.Distinct(StringComparer.Ordinal).Count());
        Assert.All(new[] { open.Version, closed.Version, reopened.Version }, version => Assert.False(string.IsNullOrEmpty(version)));
    }

    [Theory]
    [InlineData(CompanyPolicyProblemCodesV1.AccessDenied)]
    [InlineData(CompanyPolicyProblemCodesV1.ProviderUnavailable)]
    [InlineData(CompanyPolicyProblemCodesV1.ProfileInvalid)]
    [InlineData(null)]
    public async Task A_refused_or_failing_read_is_not_available(string? prerequisiteCode)
    {
        var reader = new SettingsFinanceOpeningPeriodReader(new FakePolicies(_ => throw (prerequisiteCode is null
            ? (Exception)new InvalidOperationException("synthetic failure")
            : new SettingsPrerequisiteException(prerequisiteCode))));

        Assert.Equal(FinanceOpeningPeriodAnswer.NotAvailable, await reader.ReadAsync(CompanyId, CancellationToken.None));
    }

    [Fact]
    public async Task Another_companys_answer_a_non_positive_version_or_an_empty_company_is_not_available()
    {
        var otherCompany = new SettingsFinanceOpeningPeriodReader(
            new FakePolicies(_ => new SettingsFinancePolicyV1(Guid.NewGuid(), FinanceOpeningPeriodStateV1.Open, 1)));
        var zeroVersion = new SettingsFinanceOpeningPeriodReader(
            new FakePolicies(_ => new SettingsFinancePolicyV1(CompanyId, FinanceOpeningPeriodStateV1.Open, 0)));
        var called = false;
        var emptyCompany = new SettingsFinanceOpeningPeriodReader(new FakePolicies(_ =>
        {
            called = true;
            return new SettingsFinancePolicyV1(Guid.Empty, FinanceOpeningPeriodStateV1.Open, 1);
        }));

        Assert.Equal(FinanceOpeningPeriodAnswer.NotAvailable, await otherCompany.ReadAsync(CompanyId, CancellationToken.None));
        Assert.Equal(FinanceOpeningPeriodAnswer.NotAvailable, await zeroVersion.ReadAsync(CompanyId, CancellationToken.None));
        Assert.Equal(FinanceOpeningPeriodAnswer.NotAvailable, await emptyCompany.ReadAsync(Guid.Empty, CancellationToken.None));
        Assert.False(called);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var reader = new SettingsFinanceOpeningPeriodReader(new FakePolicies(token =>
        {
            token.ThrowIfCancellationRequested();
            return new SettingsFinancePolicyV1(CompanyId, FinanceOpeningPeriodStateV1.Open, 1);
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(CompanyId, cancellation.Token));
    }

    private sealed class FakePolicies(Func<CancellationToken, SettingsFinancePolicyV1> answer) : ISettingsFinancePolicyProviderV1
    {
        public Task<SettingsFinancePolicyV1> GetFinancePolicyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(answer(cancellationToken));
    }
}
