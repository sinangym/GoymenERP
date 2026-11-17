using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingInventoryCostProposalContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_legacy_proposal_without_a_current_posted_journal_remains_readable_and_serializes_without_a_new_null_field()
    {
        var proposal = Source();
        var encoded = JsonSerializer.Serialize(proposal, Json);
        using var document = JsonDocument.Parse(encoded);
        Assert.False(document.RootElement.TryGetProperty("currentPostedJournalId", out _));
        Assert.False(document.RootElement.GetProperty("component").TryGetProperty("recordedRounding", out _));
        Assert.Equal("100.01", document.RootElement.GetProperty("exactTlAmount").GetString());
        var restored = Assert.IsType<AccountingInventoryCostProposalV1>(JsonSerializer.Deserialize<AccountingInventoryCostProposalV1>(encoded, Json));
        Assert.Null(restored.CurrentPostedJournalId);
        Assert.Equal(proposal.ProposalId, restored.ProposalId);
        Assert.Equal(proposal.Component, restored.Component);
    }

    [Fact]
    public void A_current_correction_journal_is_additive_proof_and_never_replaces_the_original_posted_journal_identity()
    {
        var originalJournal = Guid.NewGuid(); var correctedJournal = Guid.NewGuid();
        var proposal = Source() with { JournalId = originalJournal, CurrentPostedJournalId = correctedJournal };
        var encoded = JsonSerializer.Serialize(proposal, Json);
        using var document = JsonDocument.Parse(encoded);
        Assert.Equal(originalJournal, document.RootElement.GetProperty("journalId").GetGuid());
        Assert.Equal(correctedJournal, document.RootElement.GetProperty("currentPostedJournalId").GetGuid());
        var restored = Assert.IsType<AccountingInventoryCostProposalV1>(JsonSerializer.Deserialize<AccountingInventoryCostProposalV1>(encoded, Json));
        Assert.Equal(originalJournal, restored.JournalId);
        Assert.Equal(correctedJournal, restored.CurrentPostedJournalId);
    }

    private static AccountingInventoryCostProposalV1 Source()
    {
        var line = Guid.NewGuid();
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AccountingInventoryCostKindV1.SalesInvoicedShipment,
            Guid.NewGuid(), 3, Guid.NewGuid(), new(2026, 11, 17), "SENTETIK-COST-CONTRACT",
            new(AccountingInventoryCostTraceV1.SalesComponent(line), Guid.NewGuid(), line, 1, null,
                new("10001", "100"), "{\"syntheticOwnerProof\":true}", new string('a', 64)),
            1, "AwaitingPreparation", new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
    }
}
