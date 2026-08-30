using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyProblemCodesV1Tests
{
    [Fact]
    public void Registry_ContainsSevenUniqueStableMachineCodes()
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
        };

        Assert.Equal(7, codes.Distinct(StringComparer.Ordinal).Count());
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
    public void TerminalCommandResults_MapToStableCodes(
        CounterpartyCommandResult result,
        string expectedCode)
    {
        Assert.Equal(expectedCode, CounterpartyProblemCodesV1.For(result));
    }

    [Theory]
    [InlineData(CounterpartyCommandResult.Created)]
    [InlineData(CounterpartyCommandResult.Succeeded)]
    public void SuccessfulCommandResults_DoNotMapToProblemCodes(
        CounterpartyCommandResult result)
    {
        Assert.Null(CounterpartyProblemCodesV1.For(result));
    }
}
