using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiGatewayTests
{
    [Fact]
    public async Task Existing_fake_and_legacy_composers_do_not_claim_general_chat_support_or_spend_budget()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var fake = AiGatewayTestDoubles.Gateway(ReadyOptions(), store);
        Assert.Equal(AiGatewayStatusV1.Unavailable, (await fake.ComposeAsync(Request([], AiFeatureV1.Chat, AiTaskKindV1.Converse))).Status);
        var composer = new CountingComposer(AiModelPlacementV1.Cloud) { Text = "SENTETIK unsupported answer" };
        Assert.Equal(AiGatewayStatusV1.Unavailable, (await AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [composer])
            .ComposeAsync(Request([], AiFeatureV1.Chat, AiTaskKindV1.Converse))).Status);
        Assert.Empty(store.Reservations); Assert.Equal(0, composer.Calls);
    }

    [Theory]
    [InlineData(AiFeatureV1.Panel)] [InlineData(AiFeatureV1.Summary)] [InlineData(AiFeatureV1.Document)]
    [InlineData(AiFeatureV1.Query)] [InlineData(AiFeatureV1.Write)] [InlineData(AiFeatureV1.Scheduled)]
    [InlineData(AiFeatureV1.Memory)] [InlineData(AiFeatureV1.Email)]
    public async Task Web_declaring_composer_is_never_admitted_to_ERP_even_with_valid_grounding_and_budget(AiFeatureV1 feature)
    {
        var store = new InMemoryAiGatewayStore(Company);
        var composer = new CountingComposer(AiModelPlacementV1.OnPremises) { UsesWeb = true };
        var result = await AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [composer])
            .ComposeAsync(Request([Item("{\"count\":42}")], feature: feature));
        Assert.Equal(AiGatewayStatusV1.Unavailable, result.Status); Assert.Equal(0, composer.Calls); Assert.Empty(store.Reservations);
    }

    [Fact]
    public async Task General_chat_cannot_claim_an_ERP_citation_and_used_units_are_still_settled()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var composer = new CountingComposer(AiModelPlacementV1.Cloud)
            { SupportsGeneralChat = true, Text = "[1] SENTETIK unsupported source claim", Cited = [1], Units = 7 };
        var result = await AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [composer])
            .ComposeAsync(Request([], AiFeatureV1.Chat, AiTaskKindV1.Converse));
        Assert.Equal(AiGatewayStatusV1.Unavailable, result.Status); Assert.Null(result.Answer);
        Assert.Equal(7, Assert.Single(store.Entries.Values).Used);
    }
}
