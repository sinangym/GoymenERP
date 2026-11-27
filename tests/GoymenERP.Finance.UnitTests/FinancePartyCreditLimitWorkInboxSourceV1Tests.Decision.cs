using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinancePartyCreditLimitWorkInboxSourceV1Tests
{
    [Fact]
    public async Task Decision_read_preserves_the_exact_native_amount_and_asks_only_company_wide_approve_not_browse_read()
    {
        var row = Proposal(Guid.NewGuid(), "00123.4500", Start);
        await using var run = await Run.CreateAsync(row); var authority = new FixedAuthority(true, true);
        var snapshot = await run.Source(authority: authority).ReadDecisionAsync(Context(), row.Id, default);
        Assert.NotNull(snapshot); Assert.True(snapshot.Pending); Assert.Equal("1", snapshot.SourceVersion);
        var p = snapshot.PartyCreditLimitProof!.Proposal;
        Assert.Equal((row.PartyId, row.CompanyAccountId, "Set", (Guid?)Try, row.AmountText, "0", "0", Proposer, Start.ToUniversalTime()),
            (p.PartyId, p.CompanyAccountId, p.Kind, p.CurrencyId, p.Amount, p.BaseRevision, p.CurrentRevision, p.ProposedBy, p.ProposedAtUtc));
        Assert.Equal(9, JsonSerializer.SerializeToElement(p, new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateObject().Count());
        Assert.All(authority.Asked, ask => Assert.Equal((ErpPermissions.FinanceCreditLimitsApprove, Company), ask));
        Assert.Equal(FinancePartyCreditLimitWorkInboxSourceV1.ScreenPath, snapshot.Item.Link);
        var wire = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("partyCreditLimitProof", wire); Assert.DoesNotContain("00123.4500", wire);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Remove_proposal_keeps_null_currency_amount_and_native_current_limit_rules()
    {
        var row = Proposal(Guid.NewGuid(), null, Start, kind: FinancePartyCreditLimitService.RemoveKind, baseRevision: 1);
        var effective = Record(row.CompanyAccountId, FinancePartyCreditLimitService.Approved, "987.50", Start.AddDays(-1), revision: 1);
        effective.PartyId = row.PartyId;
        await using var run = await Run.CreateAsync(row, effective);
        var snapshot = await run.Source().ReadDecisionAsync(Context(), row.Id, default);
        Assert.Equal("Remove", snapshot!.PartyCreditLimitProof!.Proposal.Kind);
        Assert.Null(snapshot.PartyCreditLimitProof.Proposal.CurrencyId); Assert.Null(snapshot.PartyCreditLimitProof.Proposal.Amount);
        Assert.Equal("1", snapshot.PartyCreditLimitProof.Proposal.CurrentRevision);
        Assert.Equal("987.5", Assert.Single(snapshot.Item.Rules).Threshold);
        Assert.Equal("987.50", (await run.Db.PartyCreditLimits.FindAsync(effective.Id))!.AmountText);
    }

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Historical_non_direct_proposal_metadata_remains_readable_without_reconstructing_a_decision(int status)
    {
        var row = Proposal(Guid.NewGuid(), "123", Start); row.Status = status; row.Version = status == 4 ? 3 : 2;
        row.DecidedBy = Caller; row.DecidedAtUtc = Start.AddMinutes(1);
        row.AccountRevision = status == 3 ? null : 1; row.SupersededAtUtc = status == 4 ? Start.AddMinutes(2) : null;
        await using var run = await Run.CreateAsync(row);
        var snapshot = await run.Source().ReadDecisionAsync(Context(), row.Id, default);
        Assert.NotNull(snapshot); Assert.False(snapshot.Pending); Assert.Equal(row.Version.ToString(), snapshot.SourceVersion);
        Assert.Equal((Proposer, row.AmountText), (snapshot.PartyCreditLimitProof!.Proposal.ProposedBy, snapshot.PartyCreditLimitProof.Proposal.Amount));
    }

    [Fact]
    public async Task Foreign_direct_and_unknown_targets_are_not_purpose_proposals_and_missing_approval_authority_is_denied()
    {
        var foreign = Proposal(Guid.NewGuid(), "12", Start, company: OtherCompany);
        var direct = Record(Guid.NewGuid(), FinancePartyCreditLimitService.Approved, "12", Start, revision: 1);
        await using var run = await Run.CreateAsync(foreign, direct);
        var source = run.Source();
        Assert.Null(await source.ReadDecisionAsync(Context(), foreign.Id, default));
        Assert.Null(await source.ReadDecisionAsync(Context(), direct.Id, default));
        Assert.Null(await source.ReadDecisionAsync(Context(), Guid.NewGuid(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => run.Source(authority: new(false, true)).ReadDecisionAsync(Context(), direct.Id, default));
    }

    [Fact]
    public void The_staged_standalone_purpose_does_not_grant_browse_read_or_change_the_existing_F18_control()
    {
        var declarations = new FinanceShellCapabilityProvider().GetDeclarations();
        var cap = Assert.Single(declarations, row => row.CapabilityId == FinanceShellCapabilityProvider.PartyCreditLimitWorkDecisionCapabilityId);
        Assert.False(cap.ImplementationAvailable); Assert.Null(cap.ParentCapabilityId);
        Assert.Equal("/onaylarim/finance.party-credit-limit.v1/:approvalId", cap.RoutePath);
        Assert.Equal([ErpPermissions.FinanceCreditLimitsApprove], cap.RequiredPermissions);
        var native = Assert.Single(declarations, row => row.CapabilityId == FinanceShellCapabilityProvider.CreditLimitApproveCapabilityId);
        Assert.Equal(FinanceShellCapabilityProvider.CreditLimitListCapabilityId, native.ParentCapabilityId);
        Assert.Equal([ErpPermissions.FinanceCreditLimitsRead, ErpPermissions.FinanceCreditLimitsApprove], native.RequiredPermissions);
    }
}
