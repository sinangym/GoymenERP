using GoymenERP.Modules.Quality.Application;

namespace GoymenERP.Quality.UnitTests;

public sealed class QualityInspectionDispositionContractTests
{
    [Theory]
    [InlineData(QualityDispositionKindsV1.Release)]
    [InlineData(QualityDispositionKindsV1.Reject)]
    public void Disposition_preserves_exact_owner_binding_and_separate_actors(string kind)
    {
        var value = Valid(kind, evidenceVersion: "mtc-evidence-v7");

        Assert.Equal(Company, value.CompanyId);
        Assert.Equal(Label, value.LabelId);
        Assert.Equal(("5", "3"), (value.ExpectedLabelVersion, value.ExpectedHoldVersion));
        Assert.Equal(("quality-preparer", "quality-decider"),
            (value.PreparedByActorId, value.DecidedByActorId));
        Assert.Equal(kind, value.Kind);
        Assert.Equal("mtc-evidence-v7", value.EvidenceVersion);
    }

    [Fact]
    public void Preparer_cannot_decide_their_own_disposition()
    {
        Assert.Throws<ArgumentException>(() => Valid(decidedBy: "quality-preparer"));
    }

    [Fact]
    public void Evidence_is_optional_and_does_not_choose_a_disposition()
    {
        var withoutEvidence = Valid(evidenceVersion: null);

        Assert.Null(withoutEvidence.EvidenceVersion);
        Assert.Equal(QualityDispositionKindsV1.Release, withoutEvidence.Kind);
        Assert.False(QualityDispositionKindsV1.IsKnown("MtcAttached"));
        Assert.False(QualityDispositionKindsV1.IsKnown("TransferAccepted"));
    }

    [Fact]
    public void Unknown_or_incomplete_bindings_fail_closed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid(kind: "MtcAttached"));
        Assert.Throws<ArgumentException>(() => Valid(companyId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Valid(labelId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Valid(expectedLabelVersion: " "));
        Assert.Throws<ArgumentException>(() => Valid(expectedHoldVersion: ""));
        Assert.Throws<ArgumentException>(() => Valid(expectedLabelVersion: "0"));
        Assert.Throws<ArgumentException>(() => Valid(expectedLabelVersion: "01"));
        Assert.Throws<ArgumentException>(() => Valid(expectedHoldVersion: "-1"));
        Assert.Throws<ArgumentException>(() => Valid(reason: ""));
    }

    private static readonly Guid Company = Guid.Parse("81000000-0000-4000-8000-000000000001");
    private static readonly Guid Inspection = Guid.Parse("81000000-0000-4000-8000-000000000002");
    private static readonly Guid Label = Guid.Parse("81000000-0000-4000-8000-000000000003");
    private static readonly Guid Hold = Guid.Parse("81000000-0000-4000-8000-000000000004");
    private static readonly Guid Disposition = Guid.Parse("81000000-0000-4000-8000-000000000005");

    private static QualityInspectionDispositionV1 Valid(
        string kind = QualityDispositionKindsV1.Release,
        Guid? companyId = null,
        Guid? labelId = null,
        string expectedLabelVersion = "5",
        string expectedHoldVersion = "3",
        string preparedBy = "quality-preparer",
        string decidedBy = "quality-decider",
        string reason = "SENTETIK disposition reason",
        string? evidenceVersion = "evidence-v1") =>
        new(Disposition, companyId ?? Company, Inspection, labelId ?? Label, expectedLabelVersion,
            Hold, expectedHoldVersion, preparedBy, decidedBy, kind, reason, evidenceVersion);
}
