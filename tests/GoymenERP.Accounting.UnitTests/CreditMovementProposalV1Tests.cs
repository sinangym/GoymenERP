using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;

namespace GoymenERP.Accounting.UnitTests;

public sealed class CreditMovementProposalV1Tests
{
    private static readonly Guid Company = Guid.Parse("c0710000-0000-4000-8000-000000000001");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public void Actual_source_keeps_bank_and_components_distinct_and_unknown_functional_amount_is_not_zero()
    {
        var value = Source();
        var source = CreditMovementProposalInboxConsumerV1.Read(Envelope(value), Context());
        Assert.Null(source.Components[0].FunctionalAmount);
        Assert.Equal("100.00", source.Components[0].Amount);
        Assert.Equal("98.50", source.BankEffect!.Amount);
    }
    [Fact]
    public void Reversal_is_explicit_and_other_company_or_event_forgery_cannot_enter_the_inbox()
    {
        var source = Source() with { Kind = "OpeningBalance", BankEffect = null };
        source = source with { ReversesMovementId = source.MovementId };
        Assert.Equal(source.ReversesMovementId, CreditMovementProposalInboxConsumerV1.Read(Envelope(source), Context()).ReversesMovementId);
        Assert.Throws<OutboxPermanentFailureException>(() => CreditMovementProposalInboxConsumerV1.Read(Envelope(source) with { EventId = Guid.NewGuid() }, Context()));
        Assert.Throws<OutboxPermanentFailureException>(() => CreditMovementProposalInboxConsumerV1.Read(Envelope(source) with { CompanyId = Guid.NewGuid() }, Context()));
        Assert.Throws<OutboxPermanentFailureException>(() => CreditMovementProposalInboxConsumerV1.Read(Envelope(source with { ReversesMovementId = Guid.NewGuid() }), Context()));
    }
    [Fact]
    public void Numeric_JSON_money_unknown_component_and_missing_source_proof_are_refused()
    {
        var source = Source();
        Assert.Throws<OutboxPermanentFailureException>(() => CreditMovementProposalInboxConsumerV1.Read(Envelope(source) with
            { PayloadJson = JsonSerializer.Serialize(source, Json).Replace("\"amount\":\"100.00\"", "\"amount\":100.00", StringComparison.Ordinal) }, Context()));
        Assert.Throws<OutboxPermanentFailureException>(() => CreditMovementProposalInboxConsumerV1.Read(Envelope(source with { Components = [new("computed-tax", "100.00", null)] }), Context()));
        Assert.Throws<OutboxPermanentFailureException>(() => CreditMovementProposalInboxConsumerV1.Read(Envelope(source with { SourceDocument = source.SourceDocument with { Reference = "" } }), Context()));
    }
    private static CreditMovementSourceV1 Source() => new(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Drawdown",
        new("SENTETIK bank source", new(2026, 11, 17), "1"), new(2026, 11, 17), new(2026, 11, 17), [new("principal", "100.00", null)],
        new(Guid.NewGuid(), "98.50", "SENTETIK bank credit"), null, "SENTETIK checker", "SENTETIK document");
    private static WorkInboxEventV1 Envelope(CreditMovementSourceV1 value) => new(CreditMovementProposalInboxV1.SourceName, value.EventId,
        Company, CreditMovementProposalInboxV1.EventType, 1, JsonSerializer.Serialize(value, Json), new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero), "SENTETIK", null);
    private static CompanyContext Context() { var company = new CompanyContext(); company.Set(Company); return company; }
}
