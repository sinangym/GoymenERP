using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryTransferDocumentProofContractTests
{
    private static readonly Guid Company = Guid.Parse("91000000-0000-4000-8000-000000000001");
    private static readonly Guid Transfer = Guid.Parse("91000000-0000-4000-8000-000000000002");
    private static readonly Guid Document = Guid.Parse("91000000-0000-4000-8000-000000000003");
    private static readonly Guid FirstLine = Guid.Parse("91000000-0000-4000-8000-000000000004");
    private static readonly Guid SecondLine = Guid.Parse("91000000-0000-4000-8000-000000000005");

    [Fact]
    public void Outgoing_effective_proof_preserves_exact_identity_version_and_complete_ordered_lines()
    {
        var source = new[] { SecondLine, FirstLine };
        var query = new InventoryTransferOutgoingDocumentProofQueryV1(Company, Transfer, Document, 7, source);
        var ownerSnapshot = new InventoryTransferOutgoingDocumentOwnerSnapshotV1(
            Company, Transfer, Document, 7, [SecondLine, FirstLine]);
        source[0] = Guid.NewGuid();

        var result = InventoryTransferOutgoingDocumentProofV1.Match(query, ownerSnapshot);

        Assert.Equal(InventoryTransferDocumentProofOutcomeV1.CurrentEffective, result.Outcome);
        Assert.Same(ownerSnapshot, result.EffectiveBinding);
        Assert.Equal((Company, Transfer, Document, 7),
            (query.CompanyId, query.TransferId, query.DocumentId, query.ExpectedDocumentVersion));
        Assert.Equal([SecondLine, FirstLine], query.OrderedTransferLineIds);
    }

    [Fact]
    public void Target_effective_proof_preserves_exact_company_transfer_line_document_and_version()
    {
        var query = new InventoryTransferTargetAcceptanceDocumentProofQueryV1(
            Company, Transfer, FirstLine, Document, 11);
        var ownerSnapshot = new InventoryTransferTargetAcceptanceDocumentOwnerSnapshotV1(
            Company, Transfer, FirstLine, Document, 11);

        var result = InventoryTransferTargetAcceptanceDocumentProofV1.Match(query, ownerSnapshot);

        Assert.Equal(InventoryTransferDocumentProofOutcomeV1.CurrentEffective, result.Outcome);
        Assert.Same(ownerSnapshot, result.EffectiveBinding);
        Assert.Equal((Company, Transfer, FirstLine, Document, 11),
            (query.CompanyId, query.TransferId, query.TransferLineId, query.DocumentId,
                query.ExpectedDocumentVersion));
    }

    [Theory]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.Missing)]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.Stale)]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.Draft)]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.Cancelled)]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.Reversed)]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.BindingMismatch)]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.Unavailable)]
    public void Every_non_effective_outgoing_answer_is_fail_closed_and_carries_no_binding(
        InventoryTransferDocumentProofOutcomeV1 outcome)
    {
        var result = InventoryTransferOutgoingDocumentProofV1.Refused(outcome);

        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.EffectiveBinding);
    }

    [Theory]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.Missing)]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.Stale)]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.Draft)]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.Cancelled)]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.Reversed)]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.BindingMismatch)]
    [InlineData(InventoryTransferDocumentProofOutcomeV1.Unavailable)]
    public void Every_non_effective_target_answer_is_fail_closed_and_carries_no_binding(
        InventoryTransferDocumentProofOutcomeV1 outcome)
    {
        var result = InventoryTransferTargetAcceptanceDocumentProofV1.Refused(outcome);

        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.EffectiveBinding);
    }

    [Fact]
    public void Company_transfer_document_and_target_line_bindings_are_all_required()
    {
        Assert.Throws<ArgumentException>(() => Outgoing(companyId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Outgoing(transferId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Outgoing(documentId: Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => Outgoing(version: 0));

        Assert.Throws<ArgumentException>(() => Target(companyId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Target(transferId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Target(lineId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Target(documentId: Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => Target(version: 0));
    }

    [Fact]
    public void Outgoing_binding_requires_a_complete_unique_non_empty_bounded_line_set()
    {
        Assert.Throws<ArgumentException>(() => Outgoing(lines: []));
        Assert.Throws<ArgumentException>(() => Outgoing(lines: [Guid.Empty]));
        Assert.Throws<ArgumentException>(() => Outgoing(lines: [FirstLine, FirstLine]));
        Assert.Throws<ArgumentException>(() => Outgoing(lines:
            Enumerable.Range(0, InventoryTransferApiV1.MaximumLines + 1).Select(_ => Guid.NewGuid())));
        Assert.Throws<ArgumentNullException>(() => new InventoryTransferOutgoingDocumentProofQueryV1(
            Company, Transfer, Document, 1, null!));
    }

    [Fact]
    public void Effective_and_refused_factories_do_not_allow_an_ambiguous_result()
    {
        Assert.Throws<ArgumentNullException>(() =>
            InventoryTransferOutgoingDocumentProofV1.Match(null!, OutgoingSnapshot()));
        Assert.Throws<ArgumentNullException>(() =>
            InventoryTransferOutgoingDocumentProofV1.Match(Outgoing(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            InventoryTransferTargetAcceptanceDocumentProofV1.Match(null!, TargetSnapshot()));
        Assert.Throws<ArgumentNullException>(() =>
            InventoryTransferTargetAcceptanceDocumentProofV1.Match(Target(), null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryTransferOutgoingDocumentProofV1.Refused(
                InventoryTransferDocumentProofOutcomeV1.CurrentEffective));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryTransferTargetAcceptanceDocumentProofV1.Refused(
                InventoryTransferDocumentProofOutcomeV1.CurrentEffective));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryTransferOutgoingDocumentProofV1.Refused((InventoryTransferDocumentProofOutcomeV1)999));

        Assert.DoesNotContain(typeof(InventoryTransferOutgoingDocumentProofV1).GetMethods(),
            method => method.Name == "CurrentEffective");
        Assert.DoesNotContain(typeof(InventoryTransferTargetAcceptanceDocumentProofV1).GetMethods(),
            method => method.Name == "CurrentEffective");
    }

    [Theory]
    [InlineData("omitted")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("reordered")]
    public void Outgoing_proof_requires_the_owner_complete_ordered_line_set(string drift)
    {
        var requested = Outgoing(lines: [FirstLine, SecondLine]);
        IReadOnlyList<Guid> ownerLines = drift switch
        {
            "omitted" => [FirstLine],
            "extra" => [FirstLine, SecondLine, Guid.NewGuid()],
            "duplicate" => [FirstLine, SecondLine, SecondLine],
            "reordered" => [SecondLine, FirstLine],
            _ => throw new InvalidOperationException(),
        };

        var result = InventoryTransferOutgoingDocumentProofV1.Match(requested, OutgoingSnapshot(ownerLines));

        Assert.Equal(InventoryTransferDocumentProofOutcomeV1.BindingMismatch, result.Outcome);
        Assert.Null(result.EffectiveBinding);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("transfer")]
    [InlineData("document")]
    [InlineData("version")]
    public void Outgoing_proof_requires_every_owner_identity_and_version_to_match_exactly(string drift)
    {
        var requested = Outgoing(version: 5);
        var ownerSnapshot = drift switch
        {
            "company" => OutgoingSnapshot(companyId: Guid.NewGuid(), version: 5),
            "transfer" => OutgoingSnapshot(transferId: Guid.NewGuid(), version: 5),
            "document" => OutgoingSnapshot(documentId: Guid.NewGuid(), version: 5),
            "version" => OutgoingSnapshot(version: 6),
            _ => throw new InvalidOperationException(),
        };

        var result = InventoryTransferOutgoingDocumentProofV1.Match(requested, ownerSnapshot);

        Assert.Equal(InventoryTransferDocumentProofOutcomeV1.BindingMismatch, result.Outcome);
        Assert.Null(result.EffectiveBinding);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("transfer")]
    [InlineData("line")]
    [InlineData("document")]
    [InlineData("version")]
    public void Target_proof_requires_every_owner_binding_to_match_exactly(string drift)
    {
        var requested = Target(version: 5);
        var ownerSnapshot = drift switch
        {
            "company" => TargetSnapshot(companyId: Guid.NewGuid(), version: 5),
            "transfer" => TargetSnapshot(transferId: Guid.NewGuid(), version: 5),
            "line" => TargetSnapshot(lineId: Guid.NewGuid(), version: 5),
            "document" => TargetSnapshot(documentId: Guid.NewGuid(), version: 5),
            "version" => TargetSnapshot(version: 6),
            _ => throw new InvalidOperationException(),
        };

        var result = InventoryTransferTargetAcceptanceDocumentProofV1.Match(requested, ownerSnapshot);

        Assert.Equal(InventoryTransferDocumentProofOutcomeV1.BindingMismatch, result.Outcome);
        Assert.Null(result.EffectiveBinding);
    }

    [Fact]
    public void Typed_ports_are_read_only_and_expose_no_free_form_kind_quantity_or_command_envelope()
    {
        AssertReadOnlyPort<IInventoryTransferOutgoingDocumentProofPortV1,
            InventoryTransferOutgoingDocumentProofQueryV1,
            InventoryTransferOutgoingDocumentProofV1>();
        AssertReadOnlyPort<IInventoryTransferTargetAcceptanceDocumentProofPortV1,
            InventoryTransferTargetAcceptanceDocumentProofQueryV1,
            InventoryTransferTargetAcceptanceDocumentProofV1>();

        var queryProperties = typeof(InventoryTransferOutgoingDocumentProofQueryV1).GetProperties()
            .Concat(typeof(InventoryTransferTargetAcceptanceDocumentProofQueryV1).GetProperties())
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain(queryProperties, property =>
            property.Contains("Kind", StringComparison.Ordinal)
            || property.Contains("Quantity", StringComparison.Ordinal));
    }

    private static void AssertReadOnlyPort<TPort, TQuery, TResult>()
    {
        var method = Assert.Single(typeof(TPort).GetMethods());
        Assert.Equal("ReadAsync", method.Name);
        Assert.Equal(typeof(Task<TResult>), method.ReturnType);
        Assert.Equal(typeof(TQuery), method.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), method.GetParameters()[1].ParameterType);
        Assert.DoesNotContain(method.GetParameters(), parameter =>
            parameter.ParameterType == typeof(InventoryCommandMetadata));
    }

    private static InventoryTransferOutgoingDocumentProofQueryV1 Outgoing(
        Guid? companyId = null,
        Guid? transferId = null,
        Guid? documentId = null,
        int version = 1,
        IEnumerable<Guid>? lines = null) => new(
        companyId ?? Company,
        transferId ?? Transfer,
        documentId ?? Document,
        version,
        lines ?? [FirstLine, SecondLine]);

    private static InventoryTransferTargetAcceptanceDocumentProofQueryV1 Target(
        Guid? companyId = null,
        Guid? transferId = null,
        Guid? lineId = null,
        Guid? documentId = null,
        int version = 1) => new(
        companyId ?? Company,
        transferId ?? Transfer,
        lineId ?? FirstLine,
        documentId ?? Document,
        version);

    private static InventoryTransferOutgoingDocumentOwnerSnapshotV1 OutgoingSnapshot(
        IEnumerable<Guid>? lines = null,
        Guid? companyId = null,
        Guid? transferId = null,
        Guid? documentId = null,
        int version = 1) => new(
        companyId ?? Company,
        transferId ?? Transfer,
        documentId ?? Document,
        version,
        lines ?? [FirstLine, SecondLine]);

    private static InventoryTransferTargetAcceptanceDocumentOwnerSnapshotV1 TargetSnapshot(
        Guid? companyId = null,
        Guid? transferId = null,
        Guid? lineId = null,
        Guid? documentId = null,
        int version = 1) => new(
        companyId ?? Company,
        transferId ?? Transfer,
        lineId ?? FirstLine,
        documentId ?? Document,
        version);
}
