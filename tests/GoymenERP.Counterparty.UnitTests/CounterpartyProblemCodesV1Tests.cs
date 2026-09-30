using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyProblemCodesV1Tests
{
    [Fact]
    public void Registry_ContainsUniqueStableMachineCodes()
    {
        var codes = new[]
        {
            CounterpartyProblemCodesV1.ValidationFailed,
            CounterpartyProblemCodesV1.CommandMetadataInvalid,
            CounterpartyProblemCodesV1.InvalidCursor,
            CounterpartyProblemCodesV1.NotFound,
            CounterpartyProblemCodesV1.AlreadyLinked,
            CounterpartyProblemCodesV1.VersionConflict,
            CounterpartyProblemCodesV1.IdempotencyConflict,
            CounterpartyProblemCodesV1.TaxIdentityInvalid,
            CounterpartyProblemCodesV1.TaxIdentityChangeRequiresApproval,
            CounterpartyProblemCodesV1.TaxIdentityNotSet,
            CounterpartyProblemCodesV1.TaxIdentityUnchanged,
            CounterpartyProblemCodesV1.TaxIdentityChangePending,
            CounterpartyProblemCodesV1.TaxIdentityChangeNotPending,
            CounterpartyProblemCodesV1.TaxIdentityChangeStale,
            CounterpartyProblemCodesV1.TaxIdentitySelfDecision,
        };

        Assert.Equal(15, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches("^[a-z]+(?:_[a-z]+)*$", code));
    }

    [Theory]
    [InlineData(CounterpartyCommandResult.NotFound, CounterpartyProblemCodesV1.NotFound)]
    [InlineData(
        CounterpartyCommandResult.VersionConflict,
        CounterpartyProblemCodesV1.VersionConflict)]
    [InlineData(
        CounterpartyCommandResult.IdempotencyConflict,
        CounterpartyProblemCodesV1.IdempotencyConflict)]
    [InlineData(
        CounterpartyCommandResult.AlreadyLinked,
        CounterpartyProblemCodesV1.AlreadyLinked)]
    [InlineData(
        CounterpartyCommandResult.TaxIdentityChangeRequiresApproval,
        CounterpartyProblemCodesV1.TaxIdentityChangeRequiresApproval)]
    [InlineData(CounterpartyCommandResult.TaxIdentityNotSet, CounterpartyProblemCodesV1.TaxIdentityNotSet)]
    [InlineData(CounterpartyCommandResult.TaxIdentityUnchanged, CounterpartyProblemCodesV1.TaxIdentityUnchanged)]
    [InlineData(CounterpartyCommandResult.TaxIdentityChangePending, CounterpartyProblemCodesV1.TaxIdentityChangePending)]
    [InlineData(
        CounterpartyCommandResult.TaxIdentityChangeNotPending,
        CounterpartyProblemCodesV1.TaxIdentityChangeNotPending)]
    [InlineData(CounterpartyCommandResult.TaxIdentityChangeStale, CounterpartyProblemCodesV1.TaxIdentityChangeStale)]
    [InlineData(CounterpartyCommandResult.TaxIdentitySelfDecision, CounterpartyProblemCodesV1.TaxIdentitySelfDecision)]
    public void RequiredForProblem_MapsEveryPersistedTerminalFailure(
        CounterpartyCommandResult result,
        string expectedCode)
    {
        Assert.Equal(expectedCode, CounterpartyProblemCodesV1.RequiredForProblem(result));
    }

    [Theory]
    [InlineData(CounterpartyCommandResult.Created)]
    [InlineData(CounterpartyCommandResult.Succeeded)]
    [InlineData((CounterpartyCommandResult)999)]
    public void RequiredForProblem_RejectsSuccessAndUnknownResults(
        CounterpartyCommandResult result)
    {
        Assert.Throws<InvalidOperationException>(
            () => CounterpartyProblemCodesV1.RequiredForProblem(result));
    }
}
