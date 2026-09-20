using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// Every <see cref="FinanceAction"/> that HAS a permission name must have one the central registry
/// knows. The defect this closes is not "Record was unregistered" — it is that a money command can go
/// live before its permission name is registered centrally, and nothing stops it.
///
/// <para>
/// WHAT IT COSTS WHEN IT HAPPENS. <c>FinancePermissionPort.CheckAsync</c> answers
/// <c>Unavailable</c> both when an action has no name and when the name is not
/// <c>ErpPermissions.IsKnown</c>. Unavailable is a 503: the screen tells the person the outcome is
/// UNKNOWN and offers a retry — and that retry can never succeed, because nothing about a missing
/// registration changes between attempts. So the user-visible failure of this defect is an honest-
/// looking outage that is actually permanent.
/// </para>
///
/// <para>
/// THIS WAS NOT HYPOTHETICAL, AND THE GUARD IS NOT DECORATIVE. Measured on <c>main</c> at
/// <c>0507ba53</c>, the commit before the registration landed: (1) <c>NameOf(FinanceAction.Record)</c>
/// returned <c>finance.manual-receipt.record</c>; (2) that string did not appear in
/// <c>ErpPermissions</c>; (3) its route was mounted at <c>FinanceEndpoints.cs:160</c>
/// (<c>POST /api/v1/finance/manual-receipts/{sourceId}/record</c>). So main carried a money command with
/// a live route whose permission name the registry did not know. **This test, placed on that commit,
/// goes red on <c>FinanceAction.Record</c>.** It is green here because <c>cd00fdc6</c> registered the
/// name — the guard makes that closure permanent rather than preventing a hypothetical.
/// </para>
///
/// <para>
/// NO EXEMPTION LIST, DELIBERATELY. The rule is "nameless OR registered", so <c>Reverse</c> passes
/// because it genuinely has no name yet (FIN-T06; its authority is the half of OPEN-009 nobody has
/// answered) — not because someone listed it. The day Reverse gets a name, this test demands its
/// registration with nobody having edited anything here. An exemption list would be a guard carrying
/// its own silencer: the next person meeting a red adds their action to the list and the guard stops
/// guarding.
/// </para>
///
/// <para>
/// BUT THE LEFT BRANCH HAS A HOLE, AND IT IS PINNED RATHER THAN IGNORED. <c>NameOf</c> ends in
/// <c>_ => null</c>, so namelessness is the DEFAULT: a new <see cref="FinanceAction"/> whose author
/// forgot to add a mapping arm is indistinguishable, to the rule above, from one that deliberately has
/// no name — and it would pass while being a command that answers Unavailable forever. So the set of
/// nameless actions is pinned to exactly the actions whose authority is an OPEN DECISION (today:
/// <c>Reverse</c>). That is not the exemption list refused above: an exemption list excuses an action
/// from the registration rule, whereas this converts silence into a signal — a new nameless action
/// turns this red, and the only honest repairs are to give it a registered name or to record why its
/// authority is still undecided. The tension is real and stated plainly: someone could satisfy the red
/// by adding a member instead of thinking. What makes that visible is that membership here carries a
/// REASON (FIN-T06 / OPEN-009), so a member without one is a review finding, not a passing test.
/// </para>
///
/// <para>
/// THE NEGATIVE, WITH ITS METHOD, BECAUSE A NEGATIVE IS ONLY AS GOOD AS THE SEARCH BEHIND IT (the
/// failure that ran five rounds on #373 was a negative stated without one). CLAIM: the only path from a
/// <see cref="FinanceAction"/> to an authority measurement is
/// <c>NameOf</c> → <c>CheckAsync</c> → <c>IErpAuthorityRevalidationService.RevalidatePermissionScopeAsync</c>.
/// METHOD: searched the symbol <c>NameOf(</c> across <c>src/</c> and <c>tests/</c> — one declaration and
/// exactly one production call site (<c>FinancePermissionPort.cs:60</c>), every other hit a test; then
/// searched <c>RevalidatePermissionScopeAsync</c> across the Finance module and the host — one Finance
/// call site (<c>FinancePermissionPort.cs:67</c>), the only other being Accounting's period port, which
/// passes an Accounting manifest name.
/// WHAT THE METHOD CANNOT SEE: a permission name reaching the authority as a literal, an interpolation
/// or a value built at runtime, none of which a symbol search finds. And a trap in the other direction —
/// the SAME strings exist as ADR-011 command names (<c>ReceiptDraftService.RecordCommand</c>,
/// <c>FinanceBankAccountService.ProposeCommand</c>), so anyone auditing by literal string will find
/// command-log constants and can mistake them for permission use, or mistake permission use for a
/// command name. The two concepts are spelled identically on purpose and are not the same thing.
/// </para>
///
/// <para>
/// OUT OF SCOPE, NAMED RATHER THAN SILENTLY ABSENT: an action with a ROUTE but NO NAME passes this
/// test, because the rule's left branch admits namelessness. That is a different defect — a route with
/// no permission at all — and catching it needs route enumeration, which this test cannot do. It is not
/// chased here. (<c>EndpointAuthorizationPolicyRegistrationTests</c> guards the neighbouring direction:
/// no route may demand a policy the registry never built.)
/// </para>
/// </summary>
public sealed class FinanceActionPermissionNameRegistrationTests
{
    /// <summary>Nameless, or registered centrally. No third state, and no exceptions.</summary>
    private static bool NamelessOrRegistered(FinanceAction action) =>
        FinancePermissionPort.NameOf(action) is not { } name || ErpPermissions.IsKnown(name);

    [Fact]
    public void Every_finance_action_is_nameless_or_its_name_is_registered_centrally()
    {
        var actions = Enum.GetValues<FinanceAction>();

        var unregistered = actions
            .Where(action => !NamelessOrRegistered(action))
            .Select(action => $"{action} -> {FinancePermissionPort.NameOf(action)}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unregistered.Count == 0,
            "These Finance actions carry a permission name the central registry does not know, so the port "
            + "answers Unavailable (503) for them and the retry a screen offers can never succeed. Register "
            + "the name in ErpPermissions, or remove the mapping if the action should have no name yet:\n"
            + string.Join("\n", unregistered));
    }

    /// <summary>
    /// The rule above is enumerated from the enum itself, so a new action joins it without anyone
    /// remembering — but only if the enumeration really carries the actions, and only if "registered"
    /// is a claim that can be false. Both are pinned here, because a rule that filtered nothing, or one
    /// whose predicate cannot say no, would pass while measuring nothing.
    /// </summary>
    [Fact]
    public void The_rule_has_input_and_its_predicate_can_say_no()
    {
        var actions = Enum.GetValues<FinanceAction>();
        Assert.NotEmpty(actions);

        // Not vacuous by namelessness: most actions DO carry a name, so the right branch is exercised.
        var named = actions.Where(action => FinancePermissionPort.NameOf(action) is not null).ToArray();
        Assert.NotEmpty(named);
        Assert.All(named, action => Assert.True(ErpPermissions.IsKnown(FinancePermissionPort.NameOf(action)!)));

        // And the registry really can reject: the predicate's right branch is not a constant true.
        Assert.False(ErpPermissions.IsKnown("finance.manual-receipt.not-a-registered-name"));
    }

    /// <summary>
    /// Reverse is the left branch's live example, and its presence is what makes the no-exemption-list
    /// design honest: it passes on a FACT about the system (it has no name), not on a listing. If FIN-T06
    /// gives it a route and a name, the case above starts demanding that name's registration — which is
    /// the whole point, and is why this case asserts the fact rather than asserting "Reverse is exempt".
    /// </summary>
    [Fact]
    public void Reverse_passes_because_it_is_genuinely_nameless_not_because_it_is_excused()
    {
        Assert.Null(FinancePermissionPort.NameOf(FinanceAction.Reverse));
        Assert.True(NamelessOrRegistered(FinanceAction.Reverse));
    }

    /// <summary>
    /// Closes the left branch's hole. Because <c>NameOf</c> ends in <c>_ => null</c>, an action added
    /// without a mapping arm is silently "nameless" and the rule above waves it through — while the
    /// product answers Unavailable for it forever. So the nameless set is pinned, and the reason each
    /// member is nameless is named here rather than left as bare membership: <c>Reverse</c> is FIN-T06,
    /// whose authority is the unanswered half of OPEN-009.
    /// <para>
    /// A new action arriving here turns this red. The honest repairs are to give it a name that the
    /// central registry knows, or to state why its authority is still an open human decision. Adding a
    /// member without that reason satisfies the compiler and not the review.
    /// </para>
    /// </summary>
    [Fact]
    public void The_only_nameless_action_is_the_one_whose_authority_is_still_an_open_decision()
    {
        var nameless = Enum.GetValues<FinanceAction>()
            .Where(action => FinancePermissionPort.NameOf(action) is null)
            .Order()
            .ToArray();

        Assert.Equal(new[] { FinanceAction.Reverse }, nameless);
    }
}
