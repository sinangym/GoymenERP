using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// MFG-API command surface without a database or HTTP: request grammar, the Settings mass check,
/// the fresh authority fence and the envelope it builds, and the mapping of every refusal to one
/// status. What SQL and the host do is proven by the relational HTTP acceptance tests.
/// </summary>
public sealed class ManufacturingCommandServiceTests
{
    private const string Permission = "manufacturing.facts.record";
    private const string ClosePermission = "manufacturing.orders.close";
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Actor = Guid.Parse("a1000000-0000-0000-0000-00000000000a");
    private static readonly Guid WorkCenter = Guid.Parse("c1000000-0000-0000-0000-00000000000c");
    private static readonly Guid Roll = Guid.Parse("b1000000-0000-0000-0000-00000000000b");
    private static readonly DateTimeOffset At = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("1e3")]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData(" 10")]
    [InlineData("10.")]
    [InlineData("+10")]
    [InlineData("0010")]
    [InlineData("")]
    public async Task A_kilogram_outside_plain_decimal_text_is_invalid_and_nothing_is_asked_or_stored(string text)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        var result = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, text), "key-1", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, "kg"),
            (result.Status, result.Code, result.Field));
        Assert.Equal(0, fixture.Settings.Asked);
        Assert.Empty(fixture.Store.Writes);
    }

    [Theory]
    [InlineData("settings_access_denied", ManufacturingApiStatus.Denied)]
    [InlineData("settings_provider_unavailable", ManufacturingApiStatus.Unavailable)]
    [InlineData("settings_profile_invalid", ManufacturingApiStatus.Unavailable)]
    [InlineData(SettingsPrerequisiteCodesV1.UnitProfileNotFound, ManufacturingApiStatus.Unprocessable)]
    public async Task A_settings_refusal_keeps_its_meaning_and_nothing_is_stored(string code, ManufacturingApiStatus expected)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        fixture.Settings.Refusal = code;

        var result = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10"), "key-1", Permission,
            CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal(code, result.Code);
        Assert.Empty(fixture.Store.Writes);
        // Asked once, before the stored-answer lookup; never again for a write that did not happen.
        Assert.Equal(1, fixture.Authority.Asked);
        // A Settings refusal is not this key's answer: nothing is recorded, so a retry is re-evaluated.
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task More_decimals_than_the_profile_allows_is_refused_never_rounded()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        var result = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10.0001"), "key-1", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingMassCodes.ScaleExceeded),
            (result.Status, result.Code));
        Assert.Empty(fixture.Store.Writes);
    }

    // FİNANS #350 P2-1 sweep: decimal.TryParse rounds past a decimal's digits, so these texts would become whole
    // numbers (10¹⁴) and pass Ö2. The text is measured first: more significant digits than a decimal holds
    // exactly is invalid text, never rounded, and Settings is not asked.
    [Theory]
    [InlineData("100000000000000.000000000000001")]
    [InlineData("99999999999999.999999999999999999")]
    public async Task A_kilogram_text_a_decimal_cannot_hold_exactly_is_invalid_never_rounded(string text)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        var result = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, text), "key-1", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, "kg"),
            (result.Status, result.Code, result.Field));
        Assert.Equal(0, fixture.Settings.Asked);
        Assert.Empty(fixture.Store.Writes);
    }

    // Within the bound the text converts exactly and keeps its scale: twenty decimals, or trailing zeros past the
    // bound, still reach the Settings check unchanged.
    [Fact]
    public async Task A_kilogram_text_within_a_decimals_exact_digits_keeps_its_scale_for_settings()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        // Twenty decimals: exact, so Settings sees all of them and refuses the scale (never rounded to 0.123).
        var twenty = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "0.12345678901234567891"), "key-1",
            Permission, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingMassCodes.ScaleExceeded), (twenty.Status, twenty.Code));

        // Thirty-five characters, but twenty of them trailing zeros: fifteen significant digits, so it is valid text.
        var zeros = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "123456789012345.00000000000000000000"),
            "key-2", Permission, CancellationToken.None);
        Assert.NotEqual(ManufacturingApiStatus.Invalid, zeros.Status);

        Assert.Equal(2, fixture.Settings.Asked);
    }

    // Ö2 on the text, as Settings' ExactPositiveDecimalV1 counts it (FİNANS #353 INFO-1, coordinator ruling): every
    // written decimal counts, trailing zeros included. With the kilogram profile's scale 3, "10.500" is three
    // decimals and is recorded; "10.5000" claims a fourth and is refused, after Settings answered, never trimmed.
    [Fact]
    public async Task Trailing_zeros_in_the_text_count_as_decimals_as_the_settings_text_contract_counts_them()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        var padded = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10.5000"), "key-1", Permission,
            CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingMassCodes.ScaleExceeded, "kg"),
            (padded.Status, padded.Code, padded.Field));
        Assert.Equal(1, fixture.Settings.Asked);
        Assert.Empty(fixture.Store.Writes);

        var three = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10.500"), "key-2", Permission,
            CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, three.Status);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("site-scoped")]
    [InlineData("company-lost-access")]
    public async Task Only_a_fresh_company_wide_grant_writes(string fault)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        switch (fault)
        {
            case "revoked":
                fixture.Authority.Scope = PermissionScope.Denied;
                break;
            case "site-scoped":
                fixture.Authority.Scope = Scope(companyWide: false);
                break;
            default:
                fixture.User.Companies.Clear();
                break;
        }

        var result = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10"), "key-1", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied), (result.Status, result.Code));
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task The_envelope_is_the_servers_and_the_authority_is_measured_after_the_settings_read()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        fixture.Authority.Scope = Scope(companyWide: true, "PRODUCTION_OPERATOR", "AUDITOR");

        var result = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10.500"), " key-1 ".Trim(),
            Permission, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal(new[] { "authority", "settings", "authority" }, fixture.Calls);
        var metadata = Assert.Single(fixture.Store.Writes);
        Assert.Equal((Actor.ToString("D"), Permission, "corr-1", "key-1", "Web"),
            (metadata.ActorId, metadata.PermissionCode, metadata.CorrelationId, metadata.IdempotencyKey, metadata.SourceChannel));
        Assert.Equal(new[] { "AUDITOR", "PRODUCTION_OPERATOR" }, metadata.RoleCodes);
        Assert.Equal((Permission, Company), fixture.Authority.LastAsked);

        // The view: lossless text, no trailing zeros, and the roll's balance from the facts.
        var roll = Assert.Single(result.Resource!.Rolls);
        Assert.Equal(("10.5", "10.5", "0", "10.5"), (roll.IssuedKg, roll.NetIssuedKg, roll.WorkInProgressKg, roll.UnexplainedKg));
        Assert.False(roll.IsFullyAccounted);
        Assert.Equal("NotCalculated", result.Resource.CostStatus);
    }

    [Fact]
    public async Task A_fact_time_must_be_utc()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var local = new RecordFactRequestV1(Guid.NewGuid(), order.Version, Roll, "10", new DateTimeOffset(2026, 9, 18, 13, 0, 0,
            TimeSpan.FromHours(3)));

        var result = await fixture.Service.IssueInputAsync(order.Id, local, "key-1", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "occurredAtUtc"), (result.Status, result.Field));
        Assert.Empty(fixture.Store.Writes);
    }

    [Theory]
    [InlineData("scrap")]
    [InlineData("4")]
    [InlineData("GoodProduct ")]
    [InlineData(null)]
    public async Task An_output_class_is_only_one_of_the_five_names(string? outputClass)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        var result = await fixture.Service.DeclareOutputAsync(order.Id,
            new DeclareOutputRequestV1(Guid.NewGuid(), order.Version, Roll, outputClass, "1", At), "key-1", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "outputClass"), (result.Status, result.Field));
    }

    [Fact]
    public async Task Refusals_map_to_one_status_each()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        // A stale version: 409 with both versions.
        var stale = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version + 5, "10"), "k1", Permission,
            CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingRuleCodes.VersionConflict, order.Version + 5, order.Version),
            (stale.Status, stale.Code, stale.ExpectedVersion, stale.CurrentVersion));

        // A domain rule: 422 with its code (a roll the order does not have).
        var foreignRoll = new RecordFactRequestV1(Guid.NewGuid(), order.Version, Guid.NewGuid(), "10", At);
        var rule = await fixture.Service.IssueInputAsync(order.Id, foreignRoll, "k2", Permission, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.RollNotInOrder), (rule.Status, rule.Code));

        // A store refusal: another company's or an unknown order is 404, never a hint.
        var unknown = await fixture.Service.IssueInputAsync(Guid.NewGuid(), Fact(1, "10"), "k3", Permission,
            CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.NotFound, unknown.Status);

        fixture.Store.Fail = ManufacturingStoreCodes.FactIdConflict;
        var conflict = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10"), "k4", Permission,
            CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingStoreCodes.FactIdConflict), (conflict.Status, conflict.Code));

        fixture.Store.Fail = ManufacturingStoreCodes.ValueDoesNotFit;
        var tooLarge = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10"), "k5", Permission,
            CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingStoreCodes.ValueDoesNotFit),
            (tooLarge.Status, tooLarge.Code));
    }

    [Fact]
    public async Task A_plan_is_saved_for_the_context_company_at_the_servers_date_with_its_declared_target()
    {
        var fixture = new Fixture();
        var request = new SavePlanRequestV1(Guid.NewGuid(), WorkCenter,
            [new PlannedInputRequestV1(Roll, "1000.250")], new TargetRequestV1("GoodOutputMass", "950"));

        var result = await fixture.Service.SavePlanAsync(request, "key-1", "manufacturing.plans.write", CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Created, result.Status);
        Assert.Equal((Company, WorkCenter, "Slitting", "Draft", 1L), (result.Resource!.CompanyId, result.Resource.WorkCenterId,
            result.Resource.Process, result.Resource.Status, result.Resource.Version));
        Assert.Equal("1000.25", Assert.Single(result.Resource.Inputs).PlannedKg);
        Assert.Equal(("GoodOutputMass", "950"), (result.Resource.Target!.Basis, result.Resource.Target.Kg));
        Assert.Equal(new[] { DateOnly.FromDateTime(At.UtcDateTime), DateOnly.FromDateTime(At.UtcDateTime) }, fixture.Settings.Days);
    }

    [Theory]
    [InlineData("inputMass")]
    [InlineData("1")]
    [InlineData("Unknown")]
    public async Task A_target_basis_is_only_its_own_name(string basis)
    {
        var fixture = new Fixture();
        var request = new SavePlanRequestV1(Guid.NewGuid(), WorkCenter, [new PlannedInputRequestV1(Roll, "10")],
            new TargetRequestV1(basis, "5"));

        var result = await fixture.Service.SavePlanAsync(request, "key-1", "manufacturing.plans.write", CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "target.basis"), (result.Status, result.Field));
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task A_zero_planned_mass_is_the_domains_caller_error_behind_the_fence()
    {
        var fixture = new Fixture();
        var request = new SavePlanRequestV1(Guid.NewGuid(), WorkCenter, [new PlannedInputRequestV1(Roll, "0")], null);

        var result = await fixture.Service.SavePlanAsync(request, "key-1", "manufacturing.plans.write", CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Invalid, result.Status);
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task A_read_without_the_company_context_is_denied()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        fixture.User.Companies.Clear();

        Assert.Equal(ManufacturingApiStatus.Denied, (await fixture.Service.GetOrderAsync(order.Id, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task The_mass_balance_read_is_bal003_from_the_facts_and_bal004_bal006_are_the_engines_unavailable_answers()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var issued = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10.5"), "key-1", Permission,
            CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, issued.Status);

        var result = await fixture.Service.GetMassBalanceAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, false, (string?)null), (result.Status, result.IsReplay, result.Code));
        var view = result.Resource!;
        Assert.Equal((order.Id, issued.Resource!.Version, "GDC-BAL/1 (master v3 §28.9)"), (view.OrderId, view.Version, view.FormulaVersion));
        var roll = Assert.Single(view.Rolls);
        Assert.Equal(Roll, roll.ParentRollId);
        // BAL-003: 10.5 issued, nothing declared or measured → 10.5 unaccounted, the same number the order read shows.
        Assert.Equal(new MassDifferenceViewV1("10.5", "0", "10.5", false), roll.Difference);
        Assert.Equal(issued.Resource.Rolls.Single().UnexplainedKg, roll.Difference.DifferenceKg);
        // BAL-004 / BAL-006: the order supplies no input without a decision, so the engine names them; no number is guessed.
        Assert.Equal(("Unavailable", (string?)null), (roll.ScrapPercent.Outcome, roll.ScrapPercent.Value));
        Assert.Equal(["fireKg", "rawMaterialKg"], roll.ScrapPercent.MissingParameters);
        Assert.Equal(("Unavailable", (string?)null), (roll.ActualUnitKg.Outcome, roll.ActualUnitKg.Value));
        Assert.Equal(["actualKg", "actualCount"], roll.ActualUnitKg.MissingParameters);
        // A read: nothing is written by it.
        Assert.Single(fixture.Store.Writes);
    }

    [Fact]
    public async Task The_mass_balance_read_answers_like_the_order_read_denied_not_found_and_retryable()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        Assert.Equal(ManufacturingApiStatus.NotFound, (await fixture.Service.GetMassBalanceAsync(Guid.NewGuid(), CancellationToken.None)).Status);
        Assert.Equal(ManufacturingApiStatus.NotFound, (await fixture.Service.GetMassBalanceAsync(Guid.Empty, CancellationToken.None)).Status);

        fixture.Store.ReadFault = ManufacturingStoreCodes.ConcurrentChange;
        var busy = await fixture.Service.GetMassBalanceAsync(order.Id, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Unavailable, (string?)null), (busy.Status, busy.Code));
        Assert.Null(busy.Resource);
        fixture.Store.ReadFault = null;

        fixture.User.Companies.Clear();
        Assert.Equal(ManufacturingApiStatus.Denied, (await fixture.Service.GetMassBalanceAsync(order.Id, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_read_that_keeps_meeting_concurrent_commits_is_unavailable_not_a_conflict()
    {
        // FİNANS #313 INFO-4: the store has already re-read; what is left is retryable (503), and
        // 409 stays the commands' answer. The result names no resource and no code of its own.
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        fixture.Store.ReadFault = ManufacturingStoreCodes.ConcurrentChange;

        var result = await fixture.Service.GetOrderAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, false), (result.Status, result.IsReplay));
        Assert.Null(result.Resource);
        Assert.Null(result.Code);
    }

    [Fact]
    public async Task Any_other_read_refusal_is_not_hidden_behind_the_retryable_answer()
    {
        // Only the concurrent-change refusal is retryable; any other store refusal keeps its own path.
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        fixture.Store.ReadFault = ManufacturingStoreCodes.CompanyContextMismatch;

        var exception = await Assert.ThrowsAsync<ManufacturingStoreException>(
            () => fixture.Service.GetOrderAsync(order.Id, CancellationToken.None));
        Assert.Equal(ManufacturingStoreCodes.CompanyContextMismatch, exception.Code);
    }

    [Fact]
    public async Task The_same_key_and_request_replays_the_stored_answer_with_the_first_correlation()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var fact = Fact(order.Version, "10");

        var first = await fixture.Service.IssueInputAsync(order.Id, fact, "key-1", Permission, CancellationToken.None);
        // A request's correlation cannot change inside it; the stored record's first correlation is
        // what the replay names (the real SQL tests replay it as a separate HTTP request).
        var again = await fixture.Service.IssueInputAsync(order.Id, fact with { Kg = "10.000" }, "key-1", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, false, (string?)null), (first.Status, first.IsReplay, first.OriginalCorrelationId));
        // 10.000 and 10 are one request: the kilograms are normalized before hashing.
        Assert.Equal((ManufacturingApiStatus.Ok, true, "corr-1"), (again.Status, again.IsReplay, again.OriginalCorrelationId));
        Assert.Equal(first.Resource!.Version, again.Resource!.Version);
        // One business write; the key's record was made in it (the store's Completion), not after it.
        Assert.Single(fixture.Store.Writes);
        var record = Assert.Single(fixture.Store.Commands.Values);
        Assert.Equal((ManufacturingCommandNames.IssueInput, "corr-1"), (record.CommandName, record.OriginalCorrelationId));
        // ADR-011 §2 itself, not the production helper: the uppercase SHA-256 of the trimmed key.
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("key-1"))),
            record.IdempotencyKeyHash);
        Assert.Matches("^[0-9A-F]{64}$", record.IdempotencyKeyHash);
        Assert.DoesNotContain("key-1", record.ResultJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("other-request")]
    [InlineData("other-actor")]
    public async Task The_same_key_with_another_request_or_actor_is_a_conflict_that_discloses_nothing(string fault)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var fact = Fact(order.Version, "10");
        await fixture.Service.IssueInputAsync(order.Id, fact, "key-1", Permission, CancellationToken.None);
        var stored = Assert.Single(fixture.Store.Commands.Values);
        if (fault == "other-actor")
        {
            // The same key and request, first used by another actor of the company.
            fixture.Store.Commands[(stored.CommandName, stored.IdempotencyKeyHash)] =
                stored with { ActorId = Guid.NewGuid().ToString("D") };
        }

        var request = fault == "other-actor" ? fact : fact with { Kg = "11" };
        var result = await fixture.Service.IssueInputAsync(order.Id, request, "key-1", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingApiCodes.IdempotencyConflict, false),
            (result.Status, result.Code, result.IsReplay));
        Assert.Null(result.Resource);
        Assert.Null(result.OriginalCorrelationId);
        Assert.Single(fixture.Store.Writes);
    }

    [Fact]
    public async Task A_terminal_version_conflict_is_the_keys_answer()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var stale = Fact(order.Version + 5, "10");

        var first = await fixture.Service.IssueInputAsync(order.Id, stale, "key-1", Permission, CancellationToken.None);
        var again = await fixture.Service.IssueInputAsync(order.Id, stale, "key-1", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingRuleCodes.VersionConflict, false),
            (first.Status, first.Code, first.IsReplay));
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingRuleCodes.VersionConflict, true, order.Version + 5, order.Version),
            (again.Status, again.Code, again.IsReplay, again.ExpectedVersion, again.CurrentVersion));
        Assert.Single(fixture.Store.Commands);
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task A_terminal_not_found_is_the_keys_answer_and_names_no_code()
    {
        var fixture = new Fixture();
        var fact = Fact(1, "10");
        var unknown = Guid.NewGuid();

        var first = await fixture.Service.IssueInputAsync(unknown, fact, "key-1", Permission, CancellationToken.None);
        var again = await fixture.Service.IssueInputAsync(unknown, fact, "key-1", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.NotFound, (string?)null, false), (first.Status, first.Code, first.IsReplay));
        Assert.Equal((ManufacturingApiStatus.NotFound, (string?)null, true), (again.Status, again.Code, again.IsReplay));
        Assert.Single(fixture.Store.Commands);
    }

    [Fact]
    public async Task The_loser_of_a_race_on_one_key_answers_from_the_winners_record()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var fact = Fact(order.Version, "10");
        // The winner is the same request under the same key, committed first by another attempt.
        fixture.Store.RaceWinner = (metadata, result) => metadata.Completion!(result) with { OriginalCorrelationId = "corr-winner" };

        var loser = await fixture.Service.IssueInputAsync(order.Id, fact, "key-1", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, true, "corr-winner"), (loser.Status, loser.IsReplay, loser.OriginalCorrelationId));
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task A_rule_refusal_after_the_winner_committed_answers_from_the_winners_record()
    {
        // §8, one exit: the other attempt of this key commits while this one runs; this one's rule then
        // refuses against the winner's state (here: a roll the order does not have, as the state it
        // read). The winner's record is the answer, not the rule's 422.
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var fact = new RecordFactRequestV1(Guid.NewGuid(), order.Version, Guid.NewGuid(), "10", At);
        fixture.Store.OnWrite = metadata =>
        {
            var record = metadata.Completion!(order) with { OriginalCorrelationId = "corr-winner" };
            fixture.Store.Commands[(record.CommandName, record.IdempotencyKeyHash)] = record;
        };

        var result = await fixture.Service.IssueInputAsync(order.Id, fact, "key-1", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, true, "corr-winner"), (result.Status, result.IsReplay, result.OriginalCorrelationId));
        Assert.Empty(fixture.Store.Writes);
    }

    // ---- MFG-ORD: the close command ----------------------------------------------------------------

    [Fact]
    public async Task The_close_is_recorded_once_and_the_same_key_answers_from_that_record()
    {
        var fixture = new Fixture();
        var order = await fixture.BalancedOrderAsync();

        var closed = await fixture.Service.CloseOrderAsync(order.Id, new CloseOrderRequestV1(order.Version), "close-1",
            ClosePermission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, false, null), (closed.Status, closed.IsReplay, closed.Code));
        Assert.Equal("Closed", closed.Resource!.Status);
        Assert.Equal(ClosePermission, Assert.Single(fixture.Store.Writes).PermissionCode);
        Assert.Single(fixture.Store.Commands);

        // The same key again is the stored answer, not a second close (which the domain would refuse).
        var again = await fixture.Service.CloseOrderAsync(order.Id, new CloseOrderRequestV1(order.Version), "close-1",
            ClosePermission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, true, "corr-1"), (again.Status, again.IsReplay, again.OriginalCorrelationId));
        Assert.Single(fixture.Store.Writes);
    }

    [Fact]
    public async Task An_unexplained_difference_refuses_the_close_and_that_refusal_is_not_this_keys_answer()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(100m), At);
        // 60 declared of 100 issued: the difference stays unexplained, and MFG-4 refuses the close.
        order.DeclareOutput(order.Version, Guid.NewGuid(), Roll, OutputClass.GoodProduct, new Kilograms(60m), At);

        var result = await fixture.Service.CloseOrderAsync(order.Id, new CloseOrderRequestV1(order.Version), "close-1",
            ClosePermission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.UnexplainedDifference),
            (result.Status, result.Code));
        Assert.Equal(OrderStatus.Open, order.Status);
        Assert.Empty(fixture.Store.Writes);
        // Not a terminal: the same key may be sent again once the missing realization is recorded.
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task A_stale_version_is_a_recorded_terminal_and_the_same_key_answers_from_it()
    {
        var fixture = new Fixture();
        var order = await fixture.BalancedOrderAsync();
        var stale = order.Version - 1;

        var conflict = await fixture.Service.CloseOrderAsync(order.Id, new CloseOrderRequestV1(stale), "close-1",
            ClosePermission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingRuleCodes.VersionConflict, stale, order.Version),
            (conflict.Status, conflict.Code, conflict.ExpectedVersion, conflict.CurrentVersion));
        Assert.Equal(OrderStatus.Open, order.Status);
        Assert.Empty(fixture.Store.Writes);
        Assert.Single(fixture.Store.Commands);

        var again = await fixture.Service.CloseOrderAsync(order.Id, new CloseOrderRequestV1(stale), "close-1",
            ClosePermission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingRuleCodes.VersionConflict, true),
            (again.Status, again.Code, again.IsReplay));
        Assert.Empty(fixture.Store.Writes);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task A_close_without_the_version_it_read_is_invalid_and_no_authority_is_asked(long version)
    {
        var fixture = new Fixture();
        var order = await fixture.BalancedOrderAsync();

        var result = await fixture.Service.CloseOrderAsync(order.Id, new CloseOrderRequestV1(version), "close-1",
            ClosePermission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, "expectedVersion"),
            (result.Status, result.Code, result.Field));
        Assert.Equal(0, fixture.Authority.Asked);
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task A_grant_that_is_not_company_wide_does_not_close_an_order()
    {
        var fixture = new Fixture();
        var order = await fixture.BalancedOrderAsync();
        fixture.Authority.Scope = Scope(companyWide: false, "PRODUCTION_OPERATOR");

        var result = await fixture.Service.CloseOrderAsync(order.Id, new CloseOrderRequestV1(order.Version), "close-1",
            ClosePermission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied), (result.Status, result.Code));
        Assert.Equal(OrderStatus.Open, order.Status);
        Assert.Empty(fixture.Store.Writes);
        Assert.Empty(fixture.Store.Commands);
    }

    // ---- MFG-ORD: the order list read --------------------------------------------------------------

    [Fact]
    public async Task The_list_answers_the_stored_summaries_as_text_with_the_page_the_store_gave()
    {
        var fixture = new Fixture();
        fixture.Store.Summaries.Add(Summary(OrderStatus.Open, new Kilograms(1500.250m), new Kilograms(1200m)));
        fixture.Store.Summaries.Add(Summary(OrderStatus.Closed, new Kilograms(800m), target: null));
        fixture.Store.NextCursor = "cursor-2";

        var result = await fixture.Service.ListOrdersAsync(null, null, null, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var page = result.Resource!;
        Assert.Equal("cursor-2", page.NextCursor);
        Assert.Equal(["Open", "Closed"], page.Orders.Select(summary => summary.Status));
        // Kilograms keep the server's lossless text; a trailing zero is not a digit.
        Assert.Equal("1500.25", page.Orders[0].PlannedInputKg);
        Assert.Equal(new TargetViewV1("InputMass", "1200"), page.Orders[0].Target);
        Assert.Null(page.Orders[1].Target);
        // The default page and no filter: the store was asked exactly that.
        Assert.Equal((50, (string?)null, (OrderStatus?)null), Assert.Single(fixture.Store.ListCalls));
    }

    [Fact]
    public async Task The_list_passes_the_status_filter_and_the_cursor_it_was_given()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ListOrdersAsync(10, "cursor-1", "Closed", CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal((10, "cursor-1", (OrderStatus?)OrderStatus.Closed), Assert.Single(fixture.Store.ListCalls));
    }

    [Theory]
    [InlineData(0, null, "pageSize")]
    [InlineData(101, null, "pageSize")]
    [InlineData(null, "open", "status")]
    [InlineData(null, "1", "status")]
    [InlineData(null, "Cancelled", "status")]
    public async Task A_list_request_outside_the_contract_is_invalid_and_the_store_is_not_asked(int? pageSize,
        string? status, string field)
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ListOrdersAsync(pageSize, null, status, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, field),
            (result.Status, result.Code, result.Field));
        Assert.Empty(fixture.Store.ListCalls);
    }

    [Fact]
    public async Task A_cursor_the_store_refuses_is_answered_on_the_cursor_never_as_a_first_page()
    {
        var fixture = new Fixture();
        fixture.Store.Summaries.Add(Summary(OrderStatus.Open, new Kilograms(10m), target: null));
        fixture.Store.ListFault = ManufacturingStoreCodes.ListCursorInvalid;

        var result = await fixture.Service.ListOrdersAsync(null, "not-this-company", null, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, "cursor"),
            (result.Status, result.Code, result.Field));
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task A_read_that_keeps_meeting_commits_is_retryable_for_the_list_too()
    {
        var fixture = new Fixture();
        fixture.Store.ListFault = ManufacturingStoreCodes.ConcurrentChange;

        var result = await fixture.Service.ListOrdersAsync(null, null, null, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Unavailable, result.Status);
        Assert.Null(result.Resource);
    }

    private static ManufacturingOrderSummary Summary(OrderStatus status, Kilograms plannedInput, Kilograms? target) =>
        new(Guid.NewGuid(), Company, Guid.NewGuid(), WorkCenter, ManufacturingProcess.Slitting, status, 1, 2,
            plannedInput, target is null ? null : TargetBasis.InputMass, target, At,
            status == OrderStatus.Closed ? Actor : null, status == OrderStatus.Closed ? At : null);

    private static RecordFactRequestV1 Fact(long version, string kg) => new(Guid.NewGuid(), version, Roll, kg, At);

    private static PermissionScope Scope(bool companyWide, params string[] roles) => new(true, companyWide,
        new HashSet<string>(roles, StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private sealed class Fixture
    {
        public Fixture()
        {
            var company = new CompanyContext();
            company.Set(Company);
            var correlation = new CorrelationContext();
            correlation.Set("corr-1", null, "Web", null, null);
            Settings = new SettingsPort(Calls);
            Authority = new AuthorityPort(Calls) { Scope = Scope(companyWide: true, "PRODUCTION_OPERATOR") };
            Correlation = correlation;
            Service = new ManufacturingCommandService(Store, Store, Store, new ManufacturingMassVerifier(Settings), Authority,
                company, User, correlation, new FixedTime());
        }

        public List<string> Calls { get; } = [];

        public CorrelationContext Correlation { get; }

        public FakeStore Store { get; } = new();

        public SettingsPort Settings { get; }

        public AuthorityPort Authority { get; }

        public User User { get; } = new();

        public ManufacturingCommandService Service { get; }

        /// <summary>An order whose one roll accounts for everything issued: the close's own condition (MFG-4).</summary>
        public async Task<ProductionOrder> BalancedOrderAsync()
        {
            var order = await ApprovedOrderAsync();
            order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(100m), At);
            order.DeclareOutput(order.Version, Guid.NewGuid(), Roll, OutputClass.GoodProduct, new Kilograms(100m), At);
            return order;
        }

        public async Task<ProductionOrder> ApprovedOrderAsync()
        {
            var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(Roll, new Kilograms(1000m))]);
            plan.Approve(plan.Version, Actor, At);
            var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
            Store.Orders[order.Id] = order;
            await Task.CompletedTask;
            return order;
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At;
    }

    private sealed class User : ICurrentUser
    {
        public HashSet<Guid> Companies { get; } = [Company];

        public bool IsAuthenticated => true;

        public string? UserId => Actor.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds => Companies;

        public bool CanAccessCompany(Guid companyId) => Companies.Contains(companyId);
    }

    private sealed class AuthorityPort(List<string> calls) : IErpAuthorityRevalidationService
    {
        public PermissionScope Scope { get; set; } = PermissionScope.Denied;

        public int Asked { get; private set; }

        public (string Permission, Guid Company) LastAsked { get; private set; }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Asked++;
            calls.Add("authority");
            LastAsked = (permission, companyId);
            return Task.FromResult(Scope);
        }
    }

    private sealed class SettingsPort(List<string> calls) : ISettingsPrerequisiteProvider
    {
        public string? Refusal { get; set; }

        public int Asked { get; private set; }

        public List<DateOnly> Days { get; } = [];

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Asked++;
            Days.Add(effectiveOn);
            if (!calls.Contains("settings"))
            {
                calls.Add("settings");
            }

            if (Refusal is { } code)
            {
                throw new SettingsPrerequisiteException(code);
            }

            return Task.FromResult(new UnitProfileV1(Company, SettingsUnitIdentitiesV1.Kilogram, "kg", "mass", true,
                new ProfileVersionV1(Guid.Parse("4a790fdc-2a6b-5126-835f-d3a13d28b71c"), 1), 3, true,
                new DateOnly(2026, 9, 18), null));
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>An in-memory store with the store's contract: the company's records, the domain's rules.</summary>
    private sealed class FakeStore : IManufacturingPlanStore, IManufacturingOrderStore, IManufacturingCommandLog
    {
        public Dictionary<(string Command, string KeyHash), ManufacturingCommandRecord> Commands { get; } = [];

        /// <summary>Runs when a write starts: another attempt of the same key committing meanwhile.</summary>
        public Action<ManufacturingCommandMetadata>? OnWrite { get; set; }

        /// <summary>Another attempt of the same key that commits first (ADR-011 §8).</summary>
        public Func<ManufacturingCommandMetadata, object, ManufacturingCommandRecord>? RaceWinner { get; set; }

        public Task<ManufacturingCommandRecord?> FindCommandAsync(string commandName, string idempotencyKeyHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Commands.GetValueOrDefault((commandName, idempotencyKeyHash)));

        public Task RecordCommandAsync(ManufacturingCommandRecord record, CancellationToken cancellationToken = default)
        {
            if (!Commands.TryAdd((record.CommandName, record.IdempotencyKeyHash), record))
            {
                throw new ManufacturingStoreException(ManufacturingStoreCodes.ConcurrentChange, "yarış");
            }

            return Task.CompletedTask;
        }

        /// <summary>The store's contract: the completion's record joins the business write, or a race loses it.</summary>
        private void Complete(ManufacturingCommandMetadata metadata, object result)
        {
            if (RaceWinner is { } winner)
            {
                var record = winner(metadata, result);
                Commands[(record.CommandName, record.IdempotencyKeyHash)] = record;
                throw new ManufacturingStoreException(ManufacturingStoreCodes.ConcurrentChange, "yarış");
            }

            if (metadata.Completion is { } completion)
            {
                var record = completion(result);
                Commands.Add((record.CommandName, record.IdempotencyKeyHash), record);
            }
        }

        public Dictionary<Guid, ProductionPlan> Plans { get; } = [];

        public Dictionary<Guid, ProductionOrder> Orders { get; } = [];

        public List<ManufacturingCommandMetadata> Writes { get; } = [];

        public string? Fail { get; set; }

        public Task<ManufacturingWriteOutcome> SavePlanAsync(ProductionPlan plan, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Guard();
            Complete(metadata, plan);
            Writes.Add(metadata);
            Plans[plan.Id] = plan;
            return Task.FromResult(ManufacturingWriteOutcome.Recorded);
        }

        public Task<ProductionPlan> ApprovePlanAsync(Guid planId, long expectedVersion, Guid approvedByUserId,
            DateTimeOffset approvedAt, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProductionPlan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Plans.GetValueOrDefault(planId));

        public Task<ManufacturingWriteOutcome> CreateOrderAsync(Guid orderId, Guid planId, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ManufacturingWriteOutcome> IssueInputAsync(Guid orderId, long expectedVersion, Guid issueId,
            Guid parentRollId, Kilograms mass, DateTimeOffset issuedAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Guard();
            OnWrite?.Invoke(metadata);
            var order = Orders.GetValueOrDefault(orderId)
                ?? throw new ManufacturingStoreException(ManufacturingStoreCodes.OrderNotFound, "yok");
            order.IssueInput(expectedVersion, issueId, parentRollId, mass, issuedAt);
            Complete(metadata, order);
            Writes.Add(metadata);
            return Task.FromResult(ManufacturingWriteOutcome.Recorded);
        }

        public Task<ManufacturingWriteOutcome> ReturnUnusedInputAsync(Guid orderId, long expectedVersion, Guid returnId,
            Guid parentRollId, Kilograms mass, DateTimeOffset returnedAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ManufacturingWriteOutcome> DeclareOutputAsync(Guid orderId, long expectedVersion, Guid outputId,
            Guid parentRollId, OutputClass outputClass, Kilograms mass, DateTimeOffset declaredAt,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManufacturingWriteOutcome> RecordWorkInProgressAsync(Guid orderId, long expectedVersion, Guid measurementId,
            Guid parentRollId, Kilograms mass, DateTimeOffset measuredAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CloseOrderAsync(Guid orderId, long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Guard();
            OnWrite?.Invoke(metadata);
            var order = Orders.GetValueOrDefault(orderId)
                ?? throw new ManufacturingStoreException(ManufacturingStoreCodes.OrderNotFound, "yok");
            // The domain decides, as the real store does; nothing below runs for a refused close.
            order.Close(expectedVersion, closedByUserId, closedAt);
            Complete(metadata, order);
            Writes.Add(metadata);
            return Task.CompletedTask;
        }

        /// <summary>What the list answers, and what it was asked.</summary>
        public List<ManufacturingOrderSummary> Summaries { get; } = [];

        public List<(int PageSize, string? Cursor, OrderStatus? Status)> ListCalls { get; } = [];

        public string? NextCursor { get; set; }

        public string? ListFault { get; set; }

        public Task<ManufacturingOrderPage> ListOrdersAsync(int pageSize, string? cursor, OrderStatus? status,
            CancellationToken cancellationToken = default)
        {
            ListCalls.Add((pageSize, cursor, status));
            if (ListFault is { } code)
            {
                throw new ManufacturingStoreException(code, "sentetik liste reddi");
            }

            var rows = Summaries.Where(summary => status is null || summary.Status == status).ToList();
            return Task.FromResult(new ManufacturingOrderPage(rows, NextCursor));
        }

        /// <summary>A read the store itself refuses (for example re-reads that keep meeting commits).</summary>
        public string? ReadFault { get; set; }

        public Task<ProductionOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            ReadFault is { } code
                ? throw new ManufacturingStoreException(code, "sentetik okuma reddi")
                : Task.FromResult(Orders.GetValueOrDefault(orderId));

        private void Guard()
        {
            if (Fail is { } code)
            {
                throw new ManufacturingStoreException(code, "sentetik ret");
            }
        }
    }
}
