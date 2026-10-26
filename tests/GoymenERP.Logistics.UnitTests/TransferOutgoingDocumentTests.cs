using GoymenERP.Modules.Logistics.Domain.Transfers;

namespace GoymenERP.Logistics.UnitTests;

public sealed class TransferOutgoingDocumentTests
{
    private static readonly Guid Company = Guid.Parse("98000000-0000-4000-8000-000000000001");
    private static readonly Guid Document = Guid.Parse("98000000-0000-4000-8000-000000000002");
    private static readonly Guid Transfer = Guid.Parse("98000000-0000-4000-8000-000000000003");
    private static readonly Guid FirstLine = Guid.Parse("98000000-0000-4000-8000-000000000004");
    private static readonly Guid SecondLine = Guid.Parse("98000000-0000-4000-8000-000000000005");
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Draft_preserves_the_complete_ordered_binding_and_has_no_effective_authority()
    {
        var source = new[] { SecondLine, FirstLine };
        var document = TransferOutgoingDocument.Create(Company, Document, Transfer, source,
            " GIB-REF-1 ", " creator ", Now);
        source[0] = Guid.NewGuid();

        Assert.Equal(TransferOutgoingDocumentStatus.Draft, document.Status);
        Assert.Equal(1, document.Version);
        Assert.Equal([SecondLine, FirstLine], document.OrderedTransferLineIds);
        Assert.Equal("GIB-REF-1", document.EDispatchNumber);
        Assert.Equal("creator", document.CreatedBy);
        Assert.Null(document.EffectiveAtUtc);
    }

    [Fact]
    public void Finalize_is_one_expected_version_transition_and_keeps_identity_and_lines_immutable()
    {
        var document = Draft();

        document.Finalize(1, "approver", Now.AddMinutes(1));

        Assert.Equal((Company, Document, Transfer), (document.CompanyId, document.DocumentId, document.TransferId));
        Assert.Equal([FirstLine, SecondLine], document.OrderedTransferLineIds);
        Assert.Equal(TransferOutgoingDocumentStatus.Effective, document.Status);
        Assert.Equal(2, document.Version);
        Assert.Equal("approver", document.EffectiveBy);
        Assert.Throws<TransferOutgoingDocumentRuleException>(() =>
            document.Finalize(1, "other", Now.AddMinutes(2)));
    }

    [Fact]
    public void Cancellation_of_an_effective_document_revokes_it_with_a_new_version()
    {
        var document = Draft();
        document.Finalize(1, "approver", Now.AddMinutes(1));

        document.Cancel(2, "canceller", "wrong destination", Now.AddMinutes(2));

        Assert.Equal(TransferOutgoingDocumentStatus.Cancelled, document.Status);
        Assert.Equal(3, document.Version);
        Assert.Equal("canceller", document.CancelledBy);
        Assert.Equal("wrong destination", document.CancellationReason);
    }

    [Fact]
    public void Draft_can_be_cancelled_without_ever_becoming_effective()
    {
        var document = Draft();

        document.Cancel(1, "canceller", "cancel before release", Now.AddMinutes(1));

        var restored = TransferOutgoingDocument.Restore(
            document.CompanyId, document.DocumentId, document.TransferId, document.OrderedTransferLineIds,
            document.EDispatchNumber, document.Status, document.Version, document.CreatedBy,
            document.CreatedAtUtc, document.EffectiveBy, document.EffectiveAtUtc, document.CancelledBy,
            document.CancelledAtUtc, document.CancellationReason);

        Assert.Equal(TransferOutgoingDocumentStatus.Cancelled, restored.Status);
        Assert.Equal(2, restored.Version);
        Assert.Null(restored.EffectiveBy);
    }

    [Fact]
    public void Complete_ordered_line_binding_rejects_empty_duplicate_and_over_limit_inputs()
    {
        Assert.Throws<ArgumentException>(() => TransferOutgoingDocument.Create(
            Company, Document, Transfer, [], null, "actor", Now));
        Assert.Throws<ArgumentException>(() => TransferOutgoingDocument.Create(
            Company, Document, Transfer, [FirstLine, FirstLine], null, "actor", Now));
        Assert.Throws<ArgumentException>(() => TransferOutgoingDocument.Create(
            Company, Document, Transfer,
            Enumerable.Range(0, TransferOutgoingDocument.MaximumLines + 1).Select(_ => Guid.NewGuid()),
            null, "actor", Now));
    }

    private static TransferOutgoingDocument Draft() => TransferOutgoingDocument.Create(
        Company, Document, Transfer, [FirstLine, SecondLine], null, "creator", Now);
}
