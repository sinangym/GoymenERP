using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.IntegrationTests;

public sealed class AccountingCostProposalStaleConsumerTests
{
    [Fact]
    public async Task Closure_notifies_only_the_immutable_preparer_without_money_or_commercial_targets()
    {
        var delivery = new Delivery(); var message = Envelope();
        await new AccountingCostProposalStaleNotificationConsumerV1(delivery).HandleAsync(message, default);
        var command = Assert.Single(delivery.Commands);
        Assert.Equal(WorkNotificationKindV1.AccountingCostProposalStale, command.Kind);
        var payload = JsonSerializer.Deserialize<AccountingCostProposalStalePayloadV1>(message.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(payload.PreparedBy, command.PreparedByUserId);
        Assert.Equal(payload.ProposalId, command.SourceEntityId);
        Assert.Equal("3", command.SourceVersion); Assert.Empty(command.Targets); Assert.Null(command.PurchaseTarget);
        Assert.Equal(WorkInboxNotificationClassV1.Digest, WorkInboxNotificationClassesV1.Of(WorkInboxNotificationKindV1.AccountingCostProposalStale));
    }

    [Fact]
    public async Task Closure_refuses_foreign_company_missing_preparer_duplicate_fields_and_unknown_members()
    {
        var delivery = new Delivery(); var consumer = new AccountingCostProposalStaleNotificationConsumerV1(delivery);
        var message = Envelope();
        foreach (var bad in new[] { message with { CompanyId = Guid.NewGuid() }, message with { SourceName = "foreign" },
            message with { Version = 2 }, message with { OccurredAtUtc = message.OccurredAtUtc.AddSeconds(1) },
            message with { PayloadJson = message.PayloadJson.Replace("\"preparedBy\":", "\"unknown\":") },
            message with { PayloadJson = message.PayloadJson[..^1] + ",\"preparedBy\":\"" + Guid.NewGuid() + "\"}" },
            message with { PayloadJson = "{}" } })
            Assert.Equal("notification_stale_cost_contract_invalid", (await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => consumer.HandleAsync(bad, default))).Code);
        Assert.Empty(delivery.Commands);
    }
    internal static WorkInboxEventV1 Envelope()
    {
        var at = DateTimeOffset.Parse("2026-11-27T00:00:00Z");
        var value = new AccountingCostProposalStalePayloadV1(TestCompanyIds.CompanyA, Guid.NewGuid(), 3, Guid.NewGuid(),
            AccountingCostProposalStaleEventV1.ReasonCode, AccountingCostProposalStaleEventV1.ReasonText, at);
        return new(AccountingCostProposalStaleEventV1.SourceName, Guid.NewGuid(), value.CompanyId, AccountingCostProposalStaleEventV1.EventType,
            1, JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), at, "SENTETIK-stale-cost", null);
    }
    private sealed class Delivery : IWorkNotificationDeliveryV1
    {
        public List<DeliverWorkNotificationV1> Commands { get; } = [];
        public Task DeliverAsync(DeliverWorkNotificationV1 command, CancellationToken cancellationToken = default)
        { Commands.Add(command); return Task.CompletedTask; }
    }
}
