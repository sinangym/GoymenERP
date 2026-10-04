using System.Security.Cryptography;
using System.Text;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesDraftCommercialRevisionHashTests
{
    private static readonly Guid DraftId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid LineId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid UnitId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    [Fact]
    public void A_pre_extension_commercial_request_keeps_its_exact_canonical_hash()
    {
        var request = new SalesDraftCommercialRevisionRequest("7",
            [new SalesDraftLineCommercialV1(LineId, "12.50", UnitId, null)]);
        var predecessorBytes = $$"""{"command":"{{SalesDraftCommands.ReviseCommercialV1}}","draftId":"{{DraftId:D}}","expectedVersion":"7","lines":[{"lineId":"{{LineId:D}}","unitPrice":"12.50","priceUnitId":"{{UnitId:D}}","discountPercent":null}]}""";
        var predecessorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(predecessorBytes))).ToLowerInvariant();

        Assert.Equal(predecessorHash,
            SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseCommercialV1, DraftId, request));
    }

    [Fact]
    public void Every_populated_extension_identity_changes_the_predecessor_hash()
    {
        var predecessor = new SalesDraftCommercialRevisionRequest("7",
            [new SalesDraftLineCommercialV1(LineId, "12.50", UnitId, null)]);
        var baseline = SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseCommercialV1, DraftId, predecessor);
        var reference = new SalesDraftPriceReferenceRequestV1(Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"), 2);

        Assert.NotEqual(baseline, SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseCommercialV1, DraftId,
            predecessor with { Lines = [predecessor.Lines[0] with { PriceReference = reference }] }));
        Assert.NotEqual(baseline, SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseCommercialV1, DraftId,
            predecessor with { Lines = [predecessor.Lines[0] with { DiscountAmount = "1" }] }));
        Assert.NotEqual(baseline, SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseCommercialV1, DraftId,
            predecessor with { Lines = [predecessor.Lines[0] with { SurchargePercent = "2" }] }));
        Assert.NotEqual(baseline, SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseCommercialV1, DraftId,
            predecessor with { Lines = [predecessor.Lines[0] with { SurchargeAmount = "3" }] }));
    }
}
