using System.Reflection;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Domain.Events;
using static GoymenERP.SecurityEvents.UnitTests.SecurityEventFixtures;

namespace GoymenERP.SecurityEvents.UnitTests;

public sealed class SecurityEventContractTests
{
    public static TheoryData<string> ClosedListNames => new()
    {
        "types", "phases", "outcomes", "severities", "scope_kinds", "actor_types", "reason_codes", "retention_classes",
    };

    [Theory]
    [MemberData(nameof(ClosedListNames))]
    public void Closed_code_lists_are_non_empty_and_unique(string listName)
    {
        var values = ClosedList(listName);

        Assert.NotEmpty(values);
        Assert.Equal(values.Count, values.Distinct(StringComparer.Ordinal).Count());
        Assert.All(values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Fact]
    public void Failure_mode_map_is_total_over_event_types_and_has_unique_pairs()
    {
        var pairs = SecurityEventWriteFailureModesV1.Pairs;

        Assert.Equal(pairs.Count, pairs.Distinct().Count());
        Assert.Equal(
            SecurityEventTypesV1.All.Order(StringComparer.Ordinal),
            pairs.Select(pair => pair.EventType).Distinct().Order(StringComparer.Ordinal));
        Assert.All(pairs, pair => Assert.True(SecurityEventPhasesV1.IsDefined(pair.Phase)));
    }

    [Theory]
    [InlineData(SecurityEventTypesV1.LoginSucceeded, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailOpenWithAlarm)]
    [InlineData(SecurityEventTypesV1.LoginFailed, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailOpenWithAlarm)]
    [InlineData(SecurityEventTypesV1.LoginRateLimited, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailOpenWithAlarm)]
    [InlineData(SecurityEventTypesV1.AuthorizationDenied, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailOpenWithAlarm)]
    [InlineData(SecurityEventTypesV1.AiAuthorizationDenied, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailOpenWithAlarm)]
    [InlineData(SecurityEventTypesV1.AiRequestRefused, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailOpenWithAlarm)]
    [InlineData(SecurityEventTypesV1.PermissionChanged, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailClosed)]
    [InlineData(SecurityEventTypesV1.PolicyPublished, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailClosed)]
    [InlineData(SecurityEventTypesV1.ExportPerformed, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailClosed)]
    [InlineData(SecurityEventTypesV1.RetentionPurgeRequested, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailClosed)]
    [InlineData(SecurityEventTypesV1.SecurityEventsRead, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailClosed)]
    [InlineData(SecurityEventTypesV1.AiToolCalled, SecurityEventPhasesV1.Started, SecurityEventWriteFailureMode.FailClosed)]
    [InlineData(SecurityEventTypesV1.AiToolCalled, SecurityEventPhasesV1.Completed, SecurityEventWriteFailureMode.FailOpenWithAlarm)]
    [InlineData(SecurityEventTypesV1.FounderRecovered, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailClosed)]
    [InlineData(SecurityEventTypesV1.FounderPasswordReset, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailClosed)]
    [InlineData(SecurityEventTypesV1.FounderRecoveryRefused, SecurityEventPhasesV1.Single, SecurityEventWriteFailureMode.FailOpenWithAlarm)]
    public void Failure_mode_map_follows_decision_E1(string eventType, string phase, SecurityEventWriteFailureMode expected)
    {
        Assert.Equal(expected, SecurityEventWriteFailureModesV1.Resolve(eventType, phase));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("unknown.type", SecurityEventPhasesV1.Single)]
    [InlineData(SecurityEventTypesV1.AiToolCalled, SecurityEventPhasesV1.Single)]
    [InlineData(SecurityEventTypesV1.LoginFailed, SecurityEventPhasesV1.Completed)]
    public void Unknown_type_phase_pair_resolves_to_fail_closed(string? eventType, string? phase)
    {
        Assert.Equal(SecurityEventWriteFailureMode.FailClosed, SecurityEventWriteFailureModesV1.Resolve(eventType, phase));
        Assert.False(SecurityEventWriteFailureModesV1.IsValidPhase(eventType, phase));
    }

    [Fact]
    public void Aggregation_eligibility_covers_exactly_the_denial_types_and_never_fail_closed()
    {
        var eligible = SecurityEventAggregationEligibilityV1.Pairs;

        Assert.Equal(
            new[]
            {
                SecurityEventTypesV1.AiAuthorizationDenied, SecurityEventTypesV1.AiRequestRefused,
                SecurityEventTypesV1.LoginFailed, SecurityEventTypesV1.AuthorizationDenied,
            }.Order(StringComparer.Ordinal),
            eligible.Select(pair => pair.EventType).Order(StringComparer.Ordinal));
        Assert.All(eligible, pair => Assert.Equal(SecurityEventPhasesV1.Single, pair.Phase));
        Assert.Equal(
            SecurityEventTypesV1.DenialTypes.Order(StringComparer.Ordinal),
            eligible.Select(pair => pair.EventType).Order(StringComparer.Ordinal));

        foreach (var (eventType, phase) in SecurityEventWriteFailureModesV1.Pairs)
        {
            var failClosed = SecurityEventWriteFailureModesV1.Resolve(eventType, phase) == SecurityEventWriteFailureMode.FailClosed;
            if (failClosed)
            {
                Assert.False(SecurityEventAggregationEligibilityV1.IsEligible(eventType, phase));
            }

            Assert.Equal(
                eligible.Contains((eventType, phase)),
                SecurityEventAggregationEligibilityV1.IsEligible(eventType, phase));
        }
    }

    [Theory]
    [InlineData(SecurityEventTypesV1.LoginSucceeded, SecurityEventPhasesV1.Single)]
    [InlineData(SecurityEventTypesV1.AiToolCalled, SecurityEventPhasesV1.Completed)]
    [InlineData(SecurityEventTypesV1.AiToolCalled, SecurityEventPhasesV1.Started)]
    [InlineData(SecurityEventTypesV1.PermissionChanged, SecurityEventPhasesV1.Single)]
    [InlineData(null, null)]
    public void Non_denial_or_unknown_pairs_are_not_aggregation_eligible(string? eventType, string? phase)
    {
        Assert.False(SecurityEventAggregationEligibilityV1.IsEligible(eventType, phase));
    }

    [Theory]
    [InlineData(SecurityEventTypesV1.AiAuthorizationDenied, SecurityEventRetentionClassesV1.AiAuthorizationDenied)]
    [InlineData(SecurityEventTypesV1.AiToolCalled, SecurityEventRetentionClassesV1.No157)]
    [InlineData(SecurityEventTypesV1.AiRequestRefused, SecurityEventRetentionClassesV1.No157)]
    [InlineData(SecurityEventTypesV1.AuthorizationDenied, SecurityEventRetentionClassesV1.No134)]
    [InlineData(SecurityEventTypesV1.SecurityEventsRead, SecurityEventRetentionClassesV1.No134)]
    [InlineData(SecurityEventTypesV1.LoginRateLimited, SecurityEventRetentionClassesV1.No134)]
    [InlineData(SecurityEventTypesV1.FounderRecovered, SecurityEventRetentionClassesV1.No134)]
    [InlineData(SecurityEventTypesV1.FounderPasswordReset, SecurityEventRetentionClassesV1.No134)]
    [InlineData(SecurityEventTypesV1.FounderRecoveryRefused, SecurityEventRetentionClassesV1.No134)]
    public void Retention_class_map_follows_No134_No157_and_E9(string eventType, string expected)
    {
        Assert.Equal(expected, SecurityEventRetentionClassesV1.ForEventType(eventType));
    }

    [Fact]
    public void Every_event_type_has_a_retention_class()
    {
        Assert.All(SecurityEventTypesV1.All, eventType => Assert.NotNull(SecurityEventRetentionClassesV1.ForEventType(eventType)));
        Assert.Null(SecurityEventRetentionClassesV1.ForEventType("unknown.type"));
    }

    [Theory]
    [InlineData(typeof(SecurityEventV1))]
    [InlineData(typeof(SecurityEventRecord))]
    public void Envelope_and_row_carry_no_ip_user_agent_username_email_or_identifier_hash(Type type)
    {
        string[] forbiddenFragments = ["IPADDRESS", "CLIENTIP", "REMOTEIP", "USERAGENT", "USERNAME", "EMAIL", "MAIL", "DISPLAYNAME", "LOGINNAME"];
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name.ToUpperInvariant()).ToArray();

        Assert.DoesNotContain(properties, p => p == "IP");
        Assert.DoesNotContain(properties, p => forbiddenFragments.Any(fragment => p.Contains(fragment, StringComparison.Ordinal)));
        Assert.DoesNotContain(properties, p => p.Contains("HASH", StringComparison.Ordinal) && p != "INTEGRITYHASH");
    }

    [Fact]
    public void Valid_fixtures_pass_validation()
    {
        Assert.Empty(SecurityEventV1Validator.Validate(Denial()));
        Assert.Empty(SecurityEventV1Validator.Validate(PermissionChange()));
        Assert.Empty(SecurityEventV1Validator.Validate(AiToolCall(SecurityEventPhasesV1.Started)));
        Assert.Empty(SecurityEventV1Validator.Validate(AiToolCall(SecurityEventPhasesV1.Completed)));
        Assert.Empty(SecurityEventV1Validator.Validate(RateLimited()));
    }

    [Fact]
    public void Login_rate_limited_type_name_is_pinned_and_is_not_re_aggregated()
    {
        Assert.Equal("auth.login.rate_limited", SecurityEventTypesV1.LoginRateLimited);
        Assert.Contains(SecurityEventTypesV1.LoginRateLimited, SecurityEventTypesV1.All);
        // The summary is already a count over its own window; the writer never folds it into another row.
        Assert.DoesNotContain(SecurityEventTypesV1.LoginRateLimited, SecurityEventTypesV1.DenialTypes);
        Assert.False(SecurityEventAggregationEligibilityV1.IsEligible(SecurityEventTypesV1.LoginRateLimited, SecurityEventPhasesV1.Single));
    }

    [Fact]
    public void Founder_recovery_type_names_are_pinned_and_are_not_aggregated()
    {
        Assert.Equal("identity.founder.recovered", SecurityEventTypesV1.FounderRecovered);
        Assert.Equal("identity.founder.password_reset", SecurityEventTypesV1.FounderPasswordReset);
        Assert.Equal("identity.founder.recovery_refused", SecurityEventTypesV1.FounderRecoveryRefused);
        Assert.All(SecurityEventTypesV1.FounderRecoveryTypes, type =>
        {
            Assert.Contains(type, SecurityEventTypesV1.All);
            // Every recovery run is its own row; a refused run is never folded into a denial aggregate.
            Assert.DoesNotContain(type, SecurityEventTypesV1.DenialTypes);
            Assert.False(SecurityEventAggregationEligibilityV1.IsEligible(type, SecurityEventPhasesV1.Single));
        });
    }

    [Fact]
    public void Valid_founder_recovery_fixtures_pass_validation()
    {
        Assert.Empty(SecurityEventV1Validator.Validate(FounderRecovery(SecurityEventTypesV1.FounderRecovered)));
        Assert.Empty(SecurityEventV1Validator.Validate(FounderRecovery(SecurityEventTypesV1.FounderPasswordReset)));
        Assert.Empty(SecurityEventV1Validator.Validate(FounderRecovery(SecurityEventTypesV1.FounderRecoveryRefused)));
    }

    [Fact]
    public void Founder_recovery_events_require_platform_scope_without_company()
    {
        var withCompany = FounderRecovery(SecurityEventTypesV1.FounderRecovered) with
        {
            ScopeKind = SecurityEventScopeKindsV1.Company,
            CompanyId = Guid.NewGuid(),
        };
        var withRequestedCompany = FounderRecovery(SecurityEventTypesV1.FounderPasswordReset) with
        {
            RequestedCompanyId = Guid.NewGuid(),
        };

        Assert.Contains("founder_recovery_requires_platform_scope_without_company", SecurityEventV1Validator.Validate(withCompany));
        Assert.Contains(
            "founder_recovery_requires_platform_scope_without_company",
            SecurityEventV1Validator.Validate(withRequestedCompany));
    }

    [Fact]
    public void Founder_recovery_events_require_the_system_actor_and_no_permission_code()
    {
        var userActor = FounderRecovery(SecurityEventTypesV1.FounderRecovered) with
        {
            ActorType = SecurityEventActorTypesV1.User,
            ActorId = Guid.NewGuid().ToString("D"),
        };
        var withPermission = FounderRecovery(SecurityEventTypesV1.FounderRecovered) with
        {
            PermissionCode = "identity.users.manage",
        };

        Assert.Contains("founder_recovery_requires_system_actor", SecurityEventV1Validator.Validate(userActor));
        Assert.Contains("founder_recovery_forbids_permission_code", SecurityEventV1Validator.Validate(withPermission));
    }

    [Fact]
    public void Founder_recovery_outcomes_are_fixed_per_type_and_a_refusal_carries_only_policy_refused()
    {
        var deniedSuccess = FounderRecovery(SecurityEventTypesV1.FounderPasswordReset) with
        {
            Outcome = SecurityEventOutcomesV1.Denied,
            ReasonCode = SecurityEventReasonCodesV1.PolicyRefused,
        };
        var allowedRefusal = FounderRecovery(SecurityEventTypesV1.FounderRecoveryRefused) with
        {
            Outcome = SecurityEventOutcomesV1.Allowed,
            ReasonCode = null,
        };
        var otherReason = FounderRecovery(SecurityEventTypesV1.FounderRecoveryRefused) with
        {
            ReasonCode = SecurityEventReasonCodesV1.CredentialsInvalid,
        };

        Assert.NotEmpty(SecurityEventV1Validator.Validate(deniedSuccess));
        Assert.NotEmpty(SecurityEventV1Validator.Validate(allowedRefusal));
        Assert.Contains("founder_recovery_reason_code_not_policy_refused", SecurityEventV1Validator.Validate(otherReason));
    }

    [Fact]
    public void Rate_limit_summary_carries_only_window_count_and_policy_limits()
    {
        var names = typeof(SecurityEventRateLimitSummaryV1)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["PermitLimit", "RejectedAttemptCount", "Window", "WindowEndUtc", "WindowSeconds", "WindowStartUtc"],
            names);
    }

    public static TheoryData<string, string> InvalidRateLimitedEnvelopes => new()
    {
        { "summary_on_other_type", "rate_limit_summary_forbidden_for_event_type" },
        { "company_scope", "rate_limited_requires_platform_scope_without_company" },
        { "requested_company", "rate_limited_requires_platform_scope_without_company" },
        { "system_actor", "rate_limited_requires_anonymous_actor" },
        { "permission_code", "rate_limited_forbids_permission_code" },
        { "no_policy", "rate_limited_requires_policy_reference" },
        { "other_reason", "rate_limited_reason_code_not_policy_refused" },
        { "allowed", "outcome_not_allowed_for_event_type" },
        { "no_summary", "rate_limit_summary_required" },
        { "local_window", "rate_limit_window_not_utc" },
        { "inverted_window", "rate_limit_window_invalid" },
        { "before_window_end", "rate_limit_summary_before_window_end" },
        { "zero_rejections", "rate_limit_rejected_count_invalid" },
        { "zero_permit_limit", "rate_limit_permit_limit_invalid" },
        { "zero_window", "rate_limit_window_length_invalid" },
        { "fractional_window", "rate_limit_window_length_invalid" },
    };

    [Theory]
    [MemberData(nameof(InvalidRateLimitedEnvelopes))]
    public void Invalid_rate_limited_envelope_reports_stable_error_code(string caseName, string expectedError)
    {
        var errors = SecurityEventV1Validator.Validate(InvalidRateLimited(caseName));

        Assert.Contains(expectedError, errors);
    }

    private static SecurityEventV1 InvalidRateLimited(string caseName)
    {
        var valid = RateLimited();
        var summary = valid.RateLimitSummary!;
        return caseName switch
        {
            "summary_on_other_type" => Denial() with { RateLimitSummary = summary },
            "company_scope" => valid with { ScopeKind = SecurityEventScopeKindsV1.Company, CompanyId = CompanyA },
            "requested_company" => valid with { RequestedCompanyId = CompanyA },
            "system_actor" => valid with { ActorType = SecurityEventActorTypesV1.System, ActorId = "system.scheduler" },
            "permission_code" => valid with { PermissionCode = "identity.login" },
            "no_policy" => valid with { PolicyId = null, PolicyVersion = null },
            "other_reason" => valid with { ReasonCode = SecurityEventReasonCodesV1.CredentialsInvalid },
            "allowed" => valid with { Outcome = SecurityEventOutcomesV1.Allowed, ReasonCode = null },
            "no_summary" => valid with { RateLimitSummary = null },
            "local_window" => valid with
            {
                RateLimitSummary = summary with { WindowStartUtc = summary.WindowStartUtc.ToOffset(TimeSpan.FromHours(3)) },
            },
            "inverted_window" => valid with { RateLimitSummary = summary with { WindowStartUtc = summary.WindowEndUtc } },
            "before_window_end" => valid with { OccurredAtUtc = summary.WindowEndUtc.AddSeconds(-1) },
            "zero_rejections" => valid with { RateLimitSummary = summary with { RejectedAttemptCount = 0 } },
            "zero_permit_limit" => valid with { RateLimitSummary = summary with { PermitLimit = 0 } },
            "zero_window" => valid with { RateLimitSummary = summary with { Window = TimeSpan.Zero } },
            "fractional_window" => valid with { RateLimitSummary = summary with { Window = TimeSpan.FromMilliseconds(1500) } },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
        };
    }

    public static TheoryData<string, string> InvalidEnvelopes => new()
    {
        { "event_id_empty", "event_id_empty" },
        { "schema_version", "schema_version_unsupported" },
        { "local_time", "occurred_at_not_utc" },
        { "company_scope_without_company", "company_scope_requires_company_id" },
        { "platform_scope_with_company", "platform_scope_forbids_company_id" },
        { "anonymous_with_actor", "anonymous_actor_forbids_actor_id" },
        { "actor_free_text", "actor_id_invalid" },
        { "actor_email", "actor_id_looks_like_email" },
        { "user_actor_username", "user_actor_id_not_internal_id" },
        { "user_actor_uppercase_guid", "user_actor_id_not_internal_id" },
        { "user_actor_compact_guid", "user_actor_id_not_internal_id" },
        { "user_actor_empty_guid", "user_actor_id_not_internal_id" },
        { "login_failed_with_user_actor", "login_failed_requires_anonymous_actor" },
        { "login_failed_with_system_actor", "login_failed_requires_anonymous_actor" },
        { "unknown_type", "event_type_unknown" },
        { "phase_on_single_type", "phase_not_allowed_for_event_type" },
        { "denial_allowed", "outcome_not_allowed_for_event_type" },
        { "unknown_reason", "reason_code_unknown" },
        { "denial_without_reason", "reason_code_required" },
        { "allowed_with_reason", "reason_code_forbidden_for_allowed" },
        { "permission_code_bad", "permission_code_invalid" },
        { "policy_half", "policy_reference_incomplete" },
        { "correlation_free_text", "correlation_id_invalid" },
        { "source_unknown", "source_channel_unknown" },
        { "ai_fields_on_non_ai", "ai_fields_forbidden_for_event_type" },
        { "tool_call_without_ai_fields", "ai_fields_required_for_tool_call" },
        { "ai_tool_id_bad", "ai_tool_id_invalid" },
    };

    [Theory]
    [MemberData(nameof(InvalidEnvelopes))]
    public void Invalid_envelope_reports_stable_error_code(string caseName, string expectedError)
    {
        var errors = SecurityEventV1Validator.Validate(Invalid(caseName));

        Assert.Contains(expectedError, errors);
    }

    [Fact]
    public void Failed_login_is_valid_only_with_an_anonymous_actor_and_no_actor_id()
    {
        var failedLogin = Denial(SecurityEventTypesV1.LoginFailed) with
        {
            ActorType = SecurityEventActorTypesV1.Anonymous,
            ActorId = null,
            PermissionCode = null,
            ReasonCode = SecurityEventReasonCodesV1.CredentialsInvalid,
        };

        Assert.Empty(SecurityEventV1Validator.Validate(failedLogin));
    }

    [Fact]
    public void User_actor_accepts_the_canonical_internal_user_id_and_system_actor_keeps_its_token()
    {
        Assert.Empty(SecurityEventV1Validator.Validate(Denial() with { ActorId = Guid.NewGuid().ToString("D") }));
        Assert.Empty(SecurityEventV1Validator.Validate(Denial() with
        {
            ActorType = SecurityEventActorTypesV1.System,
            ActorId = "system.scheduler",
        }));
    }

    [Fact]
    public void Missing_envelope_is_invalid()
    {
        Assert.Equal(["event_missing"], SecurityEventV1Validator.Validate(null));
    }

    private static SecurityEventV1 Invalid(string caseName) => caseName switch
    {
        "event_id_empty" => Denial() with { EventId = Guid.Empty },
        "schema_version" => Denial() with { SchemaVersion = 2 },
        "local_time" => Denial() with { OccurredAtUtc = new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.FromHours(3)) },
        "company_scope_without_company" => Denial() with { CompanyId = null },
        "platform_scope_with_company" => Denial() with { ScopeKind = SecurityEventScopeKindsV1.Platform },
        "anonymous_with_actor" => Denial() with { ActorType = SecurityEventActorTypesV1.Anonymous },
        "actor_free_text" => Denial() with { ActorId = "Ali Veli" },
        "actor_email" => Denial() with { ActorId = "ali@example.com" },
        "user_actor_username" => Denial() with { ActorId = "ali.veli" },
        "user_actor_uppercase_guid" => Denial() with { ActorId = UserActorId.ToUpperInvariant() },
        "user_actor_compact_guid" => Denial() with { ActorId = Guid.Parse(UserActorId).ToString("N") },
        "user_actor_empty_guid" => Denial() with { ActorId = Guid.Empty.ToString("D") },
        "login_failed_with_user_actor" => Denial(SecurityEventTypesV1.LoginFailed),
        "login_failed_with_system_actor" => Denial(SecurityEventTypesV1.LoginFailed) with
        {
            ActorType = SecurityEventActorTypesV1.System,
            ActorId = "system.scheduler",
        },
        "unknown_type" => Denial() with { EventType = "auth.unknown" },
        "phase_on_single_type" => Denial() with { Phase = SecurityEventPhasesV1.Started },
        "denial_allowed" => Denial() with { Outcome = SecurityEventOutcomesV1.Allowed, ReasonCode = null },
        "unknown_reason" => Denial() with { ReasonCode = "because" },
        "denial_without_reason" => Denial() with { ReasonCode = null },
        "allowed_with_reason" => PermissionChange() with { ReasonCode = SecurityEventReasonCodesV1.PermissionMissing },
        "permission_code_bad" => Denial() with { PermissionCode = "Not A Permission" },
        "policy_half" => Denial() with { PolicyId = "policy-1" },
        "correlation_free_text" => Denial() with { CorrelationId = "has spaces" },
        "source_unknown" => Denial() with { SourceChannel = "Fax" },
        "ai_fields_on_non_ai" => Denial() with { AiToolId = "purchasing.requests.read.v1" },
        "tool_call_without_ai_fields" => AiToolCall(SecurityEventPhasesV1.Started) with { AiToolId = null },
        "ai_tool_id_bad" => AiToolCall(SecurityEventPhasesV1.Started) with { AiToolId = "Bad Tool" },
        _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
    };

    private static IReadOnlyList<string> ClosedList(string listName) => listName switch
    {
        "types" => SecurityEventTypesV1.All,
        "phases" => SecurityEventPhasesV1.All,
        "outcomes" => SecurityEventOutcomesV1.All,
        "severities" => SecurityEventSeveritiesV1.All,
        "scope_kinds" => SecurityEventScopeKindsV1.All,
        "actor_types" => SecurityEventActorTypesV1.All,
        "reason_codes" => SecurityEventReasonCodesV1.All,
        "retention_classes" => SecurityEventRetentionClassesV1.All,
        _ => throw new ArgumentOutOfRangeException(nameof(listName)),
    };
}
