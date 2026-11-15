using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>
/// F-027 / EK-18 #76-#77: the append-only merge approval record. It names the approval company, both parties with their
/// versions, the reason and the approver; it has no public mutation, so a merge is never edited or undone.
/// </summary>
public sealed class PartyMergeTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Survivor = Guid.NewGuid();
    private static readonly Guid Duplicate = Guid.NewGuid();
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(5);

    private static PartyMerge Create(
        Guid? id = null,
        Guid? company = null,
        Guid? survivor = null,
        long survivorVersion = 3,
        Guid? duplicate = null,
        long duplicateVersion = 6,
        string reason = "  Aynı firma iki kez açılmış.  ",
        string approvedBy = "approver-a",
        string correlationId = "corr-merge-1") =>
        new(
            id ?? Guid.NewGuid(),
            company ?? Company,
            survivor ?? Survivor,
            survivorVersion,
            duplicate ?? Duplicate,
            duplicateVersion,
            reason,
            approvedBy,
            At,
            correlationId);

    [Fact]
    public void A_valid_approval_keeps_both_versions_and_the_trimmed_reason()
    {
        var merge = Create();

        Assert.Equal(Company, merge.ApprovalCompanyId);
        Assert.Equal(Survivor, merge.SurvivorPartyId);
        Assert.Equal(3, merge.SurvivorPartyVersion);
        Assert.Equal(Duplicate, merge.MergedPartyId);
        Assert.Equal(6, merge.MergedPartyVersion);
        Assert.Equal("Aynı firma iki kez açılmış.", merge.Reason);
        Assert.Equal("approver-a", merge.ApprovedBy);
        Assert.Equal(At, merge.ApprovedAtUtc);
        Assert.Equal("corr-merge-1", merge.CorrelationId);
    }

    [Fact]
    public void The_record_has_no_public_mutation()
    {
        var publicMethods = typeof(PartyMerge)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(PartyMerge) && !method.IsStatic && !method.IsSpecialName)
            .ToArray();

        Assert.Empty(publicMethods);
        Assert.All(
            typeof(PartyMerge).GetProperties(),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }

    [Fact]
    public void Empty_ids_and_a_self_merge_are_refused()
    {
        Assert.Throws<ArgumentException>(() => Create(id: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Create(company: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Create(survivor: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Create(duplicate: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Create(survivor: Survivor, duplicate: Survivor));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void A_version_below_one_is_refused(long survivorVersion, long duplicateVersion) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(survivorVersion: survivorVersion, duplicateVersion: duplicateVersion));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_reason_actor_or_correlation_is_refused(string blank)
    {
        Assert.Throws<ArgumentException>(() => Create(reason: blank));
        Assert.Throws<ArgumentException>(() => Create(approvedBy: blank));
        Assert.Throws<ArgumentException>(() => Create(correlationId: blank));
    }

    [Fact]
    public void Text_past_its_limit_is_refused_and_text_at_the_limit_is_kept()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(reason: new string('x', PartyMerge.ReasonMaximumLength + 1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(approvedBy: new string('a', PartyMerge.ActorMaximumLength + 1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(correlationId: new string('c', PartyMerge.CorrelationIdMaximumLength + 1)));
        Assert.Equal(
            PartyMerge.ReasonMaximumLength,
            Create(reason: new string('x', PartyMerge.ReasonMaximumLength)).Reason.Length);
    }
}
