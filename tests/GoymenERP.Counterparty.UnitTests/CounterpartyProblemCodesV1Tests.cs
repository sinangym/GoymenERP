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
            CounterpartyProblemCodesV1.CustomerGroupExists,
            CounterpartyProblemCodesV1.CustomerGroupNameTaken,
            CounterpartyProblemCodesV1.CustomerGroupNotFound,
            CounterpartyProblemCodesV1.CustomerGroupInactive,
            CounterpartyProblemCodesV1.EInvoiceScenarioCodeListMissing,
            CounterpartyProblemCodesV1.EInvoiceScenarioNotListed,
            CounterpartyProblemCodesV1.EDocumentTaxIdentityRequired,
            CounterpartyProblemCodesV1.EDocumentAccountInactive,
            CounterpartyProblemCodesV1.PartyAlreadyMerged,
            CounterpartyProblemCodesV1.PartyMergeTargetMerged,
            CounterpartyProblemCodesV1.PartyMergeTaxIdentityMismatch,
            CounterpartyProblemCodesV1.SuccessionSameParty,
            CounterpartyProblemCodesV1.SuccessionSameTaxIdentity,
            CounterpartyProblemCodesV1.SuccessionSuccessorInactive,
            CounterpartyProblemCodesV1.SuccessionCycle,
            CounterpartyProblemCodesV1.SuccessionAlreadyRecorded,
        };

        Assert.Equal(31, codes.Distinct(StringComparer.Ordinal).Count());
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
    [InlineData(CounterpartyCommandResult.CustomerGroupExists, CounterpartyProblemCodesV1.CustomerGroupExists)]
    [InlineData(CounterpartyCommandResult.CustomerGroupNameTaken, CounterpartyProblemCodesV1.CustomerGroupNameTaken)]
    [InlineData(CounterpartyCommandResult.CustomerGroupNotFound, CounterpartyProblemCodesV1.CustomerGroupNotFound)]
    [InlineData(CounterpartyCommandResult.CustomerGroupInactive, CounterpartyProblemCodesV1.CustomerGroupInactive)]
    [InlineData(
        CounterpartyCommandResult.EInvoiceScenarioCodeListMissing,
        CounterpartyProblemCodesV1.EInvoiceScenarioCodeListMissing)]
    [InlineData(CounterpartyCommandResult.EInvoiceScenarioNotListed, CounterpartyProblemCodesV1.EInvoiceScenarioNotListed)]
    [InlineData(
        CounterpartyCommandResult.EDocumentTaxIdentityRequired,
        CounterpartyProblemCodesV1.EDocumentTaxIdentityRequired)]
    [InlineData(CounterpartyCommandResult.EDocumentAccountInactive, CounterpartyProblemCodesV1.EDocumentAccountInactive)]
    [InlineData(CounterpartyCommandResult.PartyAlreadyMerged, CounterpartyProblemCodesV1.PartyAlreadyMerged)]
    [InlineData(CounterpartyCommandResult.PartyMergeTargetMerged, CounterpartyProblemCodesV1.PartyMergeTargetMerged)]
    [InlineData(
        CounterpartyCommandResult.PartyMergeTaxIdentityMismatch,
        CounterpartyProblemCodesV1.PartyMergeTaxIdentityMismatch)]
    [InlineData(CounterpartyCommandResult.SuccessionSameParty, CounterpartyProblemCodesV1.SuccessionSameParty)]
    [InlineData(
        CounterpartyCommandResult.SuccessionSameTaxIdentity,
        CounterpartyProblemCodesV1.SuccessionSameTaxIdentity)]
    [InlineData(
        CounterpartyCommandResult.SuccessionSuccessorInactive,
        CounterpartyProblemCodesV1.SuccessionSuccessorInactive)]
    [InlineData(CounterpartyCommandResult.SuccessionCycle, CounterpartyProblemCodesV1.SuccessionCycle)]
    [InlineData(
        CounterpartyCommandResult.SuccessionAlreadyRecorded,
        CounterpartyProblemCodesV1.SuccessionAlreadyRecorded)]
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
