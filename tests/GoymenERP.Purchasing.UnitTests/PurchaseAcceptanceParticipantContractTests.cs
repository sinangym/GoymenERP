using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseAcceptanceParticipantContractTests
{
    [Fact]
    public void Contract_is_single_use_host_transaction_participant_shape()
    {
        Assert.Contains(typeof(IAsyncDisposable), typeof(IPurchaseAcceptanceParticipantV1).GetInterfaces());
        var stage = Assert.Single(typeof(IPurchaseAcceptanceParticipantV1).GetMethods());
        Assert.Equal(nameof(IPurchaseAcceptanceParticipantV1.StageAsync), stage.Name);
        Assert.Equal(typeof(Task<PurchaseAcceptanceStageResultV1>), stage.ReturnType);
        Assert.Equal(
            [typeof(PurchaseAcceptanceStageRequestV1), typeof(PurchasingWriteEnvelope), typeof(CancellationToken)],
            stage.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    [Fact]
    public void Validator_accepts_canonical_positive_share_and_exact_versions()
    {
        var validated = PurchaseAcceptanceParticipantContractV1.Validate(ValidRequest());

        Assert.Equal(7, validated.ExpectedPurchaseOrderVersion);
        Assert.Equal(12.125m, validated.AcceptedQuantity);
    }

    [Theory]
    [InlineData("07", "12.125", PurchaseAcceptanceParticipantCodes.InvalidVersion)]
    [InlineData("7", "12.1250", PurchaseAcceptanceParticipantCodes.InvalidQuantity)]
    [InlineData("7", "0", PurchaseAcceptanceParticipantCodes.InvalidQuantity)]
    [InlineData("7", "-1", PurchaseAcceptanceParticipantCodes.InvalidQuantity)]
    public void Validator_refuses_noncanonical_or_nonpositive_values(string version, string quantity, string code)
    {
        var request = ValidRequest() with { ExpectedPurchaseOrderVersion = version, AcceptedQuantity = quantity };

        var exception = Assert.Throws<PurchaseAcceptanceParticipantException>(() =>
            PurchaseAcceptanceParticipantContractV1.Validate(request));

        Assert.Equal(code, exception.Code);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void Validator_refuses_non_utc_occurrence()
    {
        var request = ValidRequest() with { OccurredAtUtc = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.FromHours(3)) };

        var exception = Assert.Throws<PurchaseAcceptanceParticipantException>(() =>
            PurchaseAcceptanceParticipantContractV1.Validate(request));

        Assert.Equal(PurchaseAcceptanceParticipantCodes.InvalidRequest, exception.Code);
    }

    private static PurchaseAcceptanceStageRequestV1 ValidRequest() => new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000001"),
        Guid.Parse("30000000-0000-0000-0000-000000000001"),
        Guid.Parse("40000000-0000-0000-0000-000000000001"),
        "7",
        Guid.Parse("50000000-0000-0000-0000-000000000001"),
        "product-v3",
        Guid.Parse("60000000-0000-0000-0000-000000000001"),
        "12.125",
        new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero));
}
