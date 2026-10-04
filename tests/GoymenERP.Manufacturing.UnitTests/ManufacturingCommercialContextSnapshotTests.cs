using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class ManufacturingCommercialContextSnapshotTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid Party = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid Account = Guid.Parse("40000000-0000-0000-0000-000000000004");

    [Fact]
    public void Snapshot_keeps_all_four_values_and_opaque_versions_exactly()
    {
        var snapshot = new PlanCommercialContextSnapshotV1(Party, Account, " 01 ", "account:v2/007");

        Assert.Equal((Party, Account, " 01 ", "account:v2/007"),
            (snapshot.PartyId, snapshot.CompanyPartyAccountId, snapshot.PartyVersion, snapshot.AccountVersion));
    }

    [Fact]
    public void Snapshot_refuses_empty_identity_or_version_without_reinterpreting_versions()
    {
        Assert.Throws<ArgumentException>(() => Snapshot(partyId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Snapshot(accountId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Snapshot(partyVersion: ""));
        Assert.Throws<ArgumentNullException>(() => Snapshot(partyVersion: null!));
        Assert.Throws<ArgumentException>(() => Snapshot(accountVersion: ""));
        Assert.Throws<ArgumentNullException>(() => Snapshot(accountVersion: null!));
    }

    [Fact]
    public void Legacy_null_context_remains_optional_and_is_not_classified_as_company_owned()
    {
        var legacy = new PlannedInput(Guid.NewGuid(), new Kilograms(100m));
        var commercial = Input(Guid.NewGuid(), Snapshot());

        var plan = Plan([legacy, commercial]);

        Assert.Null(plan.Inputs[0].CommercialContext);
        Assert.Same(commercial.CommercialContext, plan.Inputs[1].CommercialContext);
    }

    [Fact]
    public void One_plan_accepts_multiple_inputs_with_the_same_exact_commercial_context()
    {
        var context = Snapshot();

        var plan = Plan([Input(Guid.NewGuid(), context), Input(Guid.NewGuid(), Snapshot())]);

        Assert.All(plan.Inputs, input => Assert.Equal(context, input.CommercialContext));
    }

    public static IEnumerable<object[]> DifferentCommercialContexts()
    {
        yield return [Snapshot(partyId: Guid.Parse("30000000-0000-0000-0000-000000000099"))];
        yield return [Snapshot(accountId: Guid.Parse("40000000-0000-0000-0000-000000000099"))];
        yield return [Snapshot(partyVersion: "party-v2")];
        yield return [Snapshot(accountVersion: "account-v2")];
    }

    [Theory]
    [MemberData(nameof(DifferentCommercialContexts))]
    public void One_plan_refuses_a_different_identity_or_version(
        PlanCommercialContextSnapshotV1 differentContext)
    {
        var first = Input(Guid.NewGuid(), Snapshot());
        var second = Input(Guid.NewGuid(), differentContext);

        Assert.Throws<ArgumentException>(() => Plan([first, second]));
    }

    [Fact]
    public void A_refused_revision_applies_nothing()
    {
        var originalContext = Snapshot();
        var originalInput = Input(Guid.NewGuid(), originalContext);
        var plan = Plan([originalInput]);
        var differentContext = Snapshot(accountVersion: "account-v2");

        Assert.Throws<ArgumentException>(() => plan.Revise(plan.Version, Guid.NewGuid(),
            ManufacturingProcess.Slitting,
            [Input(Guid.NewGuid(), originalContext), Input(Guid.NewGuid(), differentContext)], target: null));

        Assert.Equal((1L, 0, WorkCenter), (plan.Version, plan.RevisionCount, plan.WorkCenterId));
        Assert.Same(originalInput, Assert.Single(plan.Inputs));
    }

    private static ProductionPlan Plan(IReadOnlyList<PlannedInput> inputs) =>
        new(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting, inputs);

    private static PlannedInput Input(Guid parentRollId, PlanCommercialContextSnapshotV1? commercialContext) =>
        new(parentRollId, new Kilograms(100m), expectedLabelVersion: 1,
            technicalSnapshot: null, commercialContext: commercialContext);

    private static PlanCommercialContextSnapshotV1 Snapshot(
        Guid? partyId = null,
        Guid? accountId = null,
        string partyVersion = "party-v1",
        string accountVersion = "account-v1") =>
        new(partyId ?? Party, accountId ?? Account, partyVersion, accountVersion);
}
