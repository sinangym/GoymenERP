using System.Collections.Immutable;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// Pure, generic policy-version lifecycle. It does not authorize an actor, persist a command,
/// choose a family schema or activate a provider.
/// </summary>
public static class SettingsPolicyVersionLifecyclePolicyV1
{
    private const int IdentifierMaximumLength = 200;
    private const int ActorMaximumLength = 200;
    private const int IdempotencyMaximumLength = 128;
    private const int ReasonMaximumLength = 500;
    private const int EntryKeyMaximumLength = 200;
    private const int EntryValueMaximumLength = 8192;

    public static SettingsPolicyVersionLifecycleV1 Prepare(PrepareSettingsPolicyVersionV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateContext(request.Context);
        if (request.Context.ExpectedStateVersion != 0)
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.Invalid);
        }

        var snapshot = Capture(request.Snapshot);
        return Draft(
            request.Context.CompanyId,
            request.Context.PolicyId,
            request.Context.PolicyVersionId,
            snapshot.FamilyId,
            snapshot.SchemaId,
            snapshot.ScopeIdentity,
            snapshot.ExactScope,
            policyVersion: 1,
            snapshot.ValueSnapshotId,
            snapshot.Values,
            InitialSnapshotHistory(snapshot.ValueSnapshotId, snapshot.Values),
            snapshot.EffectiveFrom,
            request.Context);
    }

    public static SettingsPolicyVersionLifecycleV1 Revise(
        SettingsPolicyVersionLifecycleV1 current,
        ReviseSettingsPolicyVersionV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(current, request.Context);
        RequireStatus(current, SettingsPolicyVersionStatusV1.Draft);
        var revision = Capture(request.Revision);
        RequireConsistentSnapshotIdentity(current, revision);
        var snapshotHistory = AppendSnapshotHistory(current.ValueSnapshotHistory, revision);

        return new SettingsPolicyVersionLifecycleV1(
            current.CompanyId,
            current.PolicyId,
            current.PolicyVersionId,
            current.FamilyId,
            current.SchemaId,
            current.ScopeIdentity,
            current.ExactScope,
            current.PolicyVersion,
            Next(current.StateVersion),
            SettingsPolicyVersionStatusV1.Draft,
            revision.ValueSnapshotId,
            revision.Values,
            snapshotHistory,
            revision.EffectiveFrom,
            request.Context.ActorId,
            current.MakerHistory.Add(request.Context.ActorId),
            request.Context.OccurredAtUtc,
            request.Context.BusinessDateEuropeIstanbul,
            request.Context.Reason,
            approvedBy: null,
            approvedAtUtc: null,
            approvedBusinessDate: null,
            approvalReason: null,
            publishedBy: null,
            publishedAtUtc: null,
            publishedBusinessDate: null,
            publicationReason: null);
    }

    public static SettingsPolicyVersionLifecycleV1 Approve(
        SettingsPolicyVersionLifecycleV1 current,
        ApproveSettingsPolicyVersionV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(current, request.Context);
        RequireStatus(current, SettingsPolicyVersionStatusV1.Draft);
        RequireNotMaker(current, request.Context);
        return Copy(
            current,
            Next(current.StateVersion),
            SettingsPolicyVersionStatusV1.Approved,
            approvedBy: request.Context.ActorId,
            approvedAtUtc: request.Context.OccurredAtUtc,
            approvedBusinessDate: request.Context.BusinessDateEuropeIstanbul,
            approvalReason: request.Context.Reason,
            publishedBy: null,
            publishedAtUtc: null,
            publishedBusinessDate: null,
            publicationReason: null);
    }

    public static SettingsPolicyVersionLifecycleV1 Publish(
        SettingsPolicyVersionLifecycleV1 current,
        PublishSettingsPolicyVersionV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(current, request.Context);
        RequireStatus(current, SettingsPolicyVersionStatusV1.Approved);
        RequireNotMaker(current, request.Context);
        if (current.EffectiveFrom < request.Context.BusinessDateEuropeIstanbul)
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.EffectiveDateInPast);
        }

        return Copy(
            current,
            Next(current.StateVersion),
            SettingsPolicyVersionStatusV1.Published,
            current.ApprovedBy,
            current.ApprovedAtUtc,
            current.ApprovedBusinessDate,
            current.ApprovalReason,
            request.Context.ActorId,
            request.Context.OccurredAtUtc,
            request.Context.BusinessDateEuropeIstanbul,
            request.Context.Reason);
    }

    public static SettingsPolicyVersionLifecycleV1 PrepareNextVersion(
        SettingsPolicyVersionLifecycleV1 current,
        PrepareNextSettingsPolicyVersionV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(current, request.Context);
        RequireStatus(current, SettingsPolicyVersionStatusV1.Published);
        if (request.PolicyVersionId == Guid.Empty || request.PolicyVersionId == current.PolicyVersionId)
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.Invalid);
        }

        var revision = Capture(request.Revision);
        RequireConsistentSnapshotIdentity(current, revision);
        var snapshotHistory = AppendSnapshotHistory(current.ValueSnapshotHistory, revision);
        return Draft(
            current.CompanyId,
            current.PolicyId,
            request.PolicyVersionId,
            current.FamilyId,
            current.SchemaId,
            current.ScopeIdentity,
            current.ExactScope,
            Next(current.PolicyVersion),
            revision.ValueSnapshotId,
            revision.Values,
            snapshotHistory,
            revision.EffectiveFrom,
            request.Context);
    }

    private static SettingsPolicyVersionLifecycleV1 Draft(
        Guid companyId,
        Guid policyId,
        Guid policyVersionId,
        string familyId,
        string schemaId,
        string scopeIdentity,
        ImmutableArray<SettingsPolicySnapshotEntryV1> exactScope,
        long policyVersion,
        Guid valueSnapshotId,
        ImmutableArray<SettingsPolicySnapshotEntryV1> values,
        ImmutableArray<SettingsPolicyValueSnapshotV1> valueSnapshotHistory,
        DateOnly effectiveFrom,
        SettingsPolicyCommandContextV1 context) =>
        new(
            companyId,
            policyId,
            policyVersionId,
            familyId,
            schemaId,
            scopeIdentity,
            exactScope,
            policyVersion,
            stateVersion: 1,
            SettingsPolicyVersionStatusV1.Draft,
            valueSnapshotId,
            values,
            valueSnapshotHistory,
            effectiveFrom,
            context.ActorId,
            [context.ActorId],
            context.OccurredAtUtc,
            context.BusinessDateEuropeIstanbul,
            context.Reason,
            approvedBy: null,
            approvedAtUtc: null,
            approvedBusinessDate: null,
            approvalReason: null,
            publishedBy: null,
            publishedAtUtc: null,
            publishedBusinessDate: null,
            publicationReason: null);

    private static SettingsPolicyVersionLifecycleV1 Copy(
        SettingsPolicyVersionLifecycleV1 current,
        long stateVersion,
        SettingsPolicyVersionStatusV1 status,
        string? approvedBy,
        DateTimeOffset? approvedAtUtc,
        DateOnly? approvedBusinessDate,
        string? approvalReason,
        string? publishedBy,
        DateTimeOffset? publishedAtUtc,
        DateOnly? publishedBusinessDate,
        string? publicationReason) =>
        new(
            current.CompanyId,
            current.PolicyId,
            current.PolicyVersionId,
            current.FamilyId,
            current.SchemaId,
            current.ScopeIdentity,
            current.ExactScope,
            current.PolicyVersion,
            stateVersion,
            status,
            current.ValueSnapshotId,
            current.Values,
            current.ValueSnapshotHistory,
            current.EffectiveFrom,
            current.PreparedBy,
            current.MakerHistory,
            current.PreparedAtUtc,
            current.PreparedBusinessDate,
            current.PreparationReason,
            approvedBy,
            approvedAtUtc,
            approvedBusinessDate,
            approvalReason,
            publishedBy,
            publishedAtUtc,
            publishedBusinessDate,
            publicationReason);

    private static void Validate(
        SettingsPolicyVersionLifecycleV1 current,
        SettingsPolicyCommandContextV1 context)
    {
        ArgumentNullException.ThrowIfNull(current);
        ValidateState(current);
        ValidateContext(context);
        if (current.CompanyId != context.CompanyId
            || current.PolicyId != context.PolicyId
            || current.PolicyVersionId != context.PolicyVersionId)
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.Invalid);
        }

        if (current.StateVersion != context.ExpectedStateVersion)
        {
            throw new SettingsPolicyVersionLifecycleException(
                SettingsPolicyVersionLifecycleCodesV1.VersionConflict,
                context.ExpectedStateVersion,
                current.StateVersion);
        }
    }

    private static void ValidateState(SettingsPolicyVersionLifecycleV1 current)
    {
        var baseValid = current.CompanyId != Guid.Empty
            && current.PolicyId != Guid.Empty
            && current.PolicyVersionId != Guid.Empty
            && ValidIdentifier(current.FamilyId)
            && ValidIdentifier(current.SchemaId)
            && ValidIdentifier(current.ScopeIdentity)
            && !current.ExactScope.IsDefault
            && ValidEntries(current.ExactScope, requireValue: false)
            && current.PolicyVersion > 0
            && current.StateVersion > 0
            && Enum.IsDefined(current.Status)
            && current.ValueSnapshotId != Guid.Empty
            && !current.Values.IsDefaultOrEmpty
            && ValidEntries(current.Values, requireValue: true)
            && ValidSnapshotHistory(
                current.ValueSnapshotHistory,
                current.ValueSnapshotId,
                current.Values)
            && current.EffectiveFrom != default
            && ValidText(current.PreparedBy, ActorMaximumLength)
            && ValidMakerHistory(current.MakerHistory, current.PreparedBy)
            && Utc(current.PreparedAtUtc)
            && current.PreparedBusinessDate != default
            && ValidText(current.PreparationReason, ReasonMaximumLength);
        var statusValid = current.Status switch
        {
            SettingsPolicyVersionStatusV1.Draft =>
                current.ApprovedBy is null && current.ApprovedAtUtc is null
                && current.ApprovedBusinessDate is null && current.ApprovalReason is null
                && current.PublishedBy is null && current.PublishedAtUtc is null
                && current.PublishedBusinessDate is null && current.PublicationReason is null,
            SettingsPolicyVersionStatusV1.Approved =>
                ValidText(current.ApprovedBy, ActorMaximumLength)
                && !IsMaker(current, current.ApprovedBy)
                && Utc(current.ApprovedAtUtc)
                && current.ApprovedBusinessDate is not null
                && ValidText(current.ApprovalReason, ReasonMaximumLength)
                && current.PublishedBy is null && current.PublishedAtUtc is null
                && current.PublishedBusinessDate is null && current.PublicationReason is null,
            SettingsPolicyVersionStatusV1.Published =>
                ValidText(current.ApprovedBy, ActorMaximumLength)
                && !IsMaker(current, current.ApprovedBy)
                && Utc(current.ApprovedAtUtc)
                && current.ApprovedBusinessDate is not null
                && ValidText(current.ApprovalReason, ReasonMaximumLength)
                && ValidText(current.PublishedBy, ActorMaximumLength)
                && !IsMaker(current, current.PublishedBy)
                && Utc(current.PublishedAtUtc)
                && current.PublishedBusinessDate is not null
                && current.EffectiveFrom >= current.PublishedBusinessDate.Value
                && ValidText(current.PublicationReason, ReasonMaximumLength),
            _ => false,
        };
        if (!baseValid || !statusValid)
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.Invalid);
        }
    }

    private static void ValidateContext(SettingsPolicyCommandContextV1 context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CompanyId == Guid.Empty
            || context.PolicyId == Guid.Empty
            || context.PolicyVersionId == Guid.Empty
            || !ValidText(context.ActorId, ActorMaximumLength)
            || context.ExpectedStateVersion < 0
            || !ValidText(context.IdempotencyKey, IdempotencyMaximumLength)
            || !ValidText(context.Reason, ReasonMaximumLength)
            || !Utc(context.OccurredAtUtc)
            || context.BusinessDateEuropeIstanbul == default)
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.Invalid);
        }
    }

    private static CapturedSnapshot Capture(SettingsPolicySnapshotInputV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ValidIdentifier(snapshot.FamilyId)
            || !ValidIdentifier(snapshot.SchemaId)
            || !ValidIdentifier(snapshot.ScopeIdentity)
            || snapshot.ValueSnapshotId == Guid.Empty
            || snapshot.EffectiveFrom == default)
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.Invalid);
        }

        return new CapturedSnapshot(
            snapshot.FamilyId,
            snapshot.SchemaId,
            snapshot.ScopeIdentity,
            CaptureEntries(snapshot.ExactScope, requireValue: false),
            snapshot.ValueSnapshotId,
            CaptureEntries(snapshot.Values, requireValue: true),
            snapshot.EffectiveFrom);
    }

    private static CapturedRevision Capture(SettingsPolicyValueRevisionV1 revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.ValueSnapshotId == Guid.Empty || revision.EffectiveFrom == default)
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.Invalid);
        }

        return new CapturedRevision(
            revision.ValueSnapshotId,
            CaptureEntries(revision.Values, requireValue: true),
            revision.EffectiveFrom);
    }

    private static ImmutableArray<SettingsPolicySnapshotEntryV1> CaptureEntries(
        IReadOnlyCollection<SettingsPolicySnapshotEntryV1> entries,
        bool requireValue)
    {
        if (entries is null || (requireValue && entries.Count == 0))
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.Invalid);
        }

        var captured = entries.Select(entry =>
            entry is null
                ? throw Failure(SettingsPolicyVersionLifecycleCodesV1.Invalid)
                : new SettingsPolicySnapshotEntryV1(entry.Key, entry.Value)).ToImmutableArray();
        if (!ValidEntries(captured, requireValue))
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.Invalid);
        }

        return captured;
    }

    private static bool ValidEntries(
        IEnumerable<SettingsPolicySnapshotEntryV1> entries,
        bool requireValue)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var entry in entries)
        {
            count++;
            if (entry is null
                || !ValidText(entry.Key, EntryKeyMaximumLength)
                || entry.Value is null
                || entry.Value.Length > EntryValueMaximumLength
                || !seen.Add(entry.Key))
            {
                return false;
            }
        }

        return !requireValue || count > 0;
    }

    private static void RequireStatus(
        SettingsPolicyVersionLifecycleV1 current,
        SettingsPolicyVersionStatusV1 expected)
    {
        if (current.Status != expected)
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.InvalidTransition);
        }
    }

    private static void RequireNotMaker(
        SettingsPolicyVersionLifecycleV1 current,
        SettingsPolicyCommandContextV1 context)
    {
        if (IsMaker(current, context.ActorId))
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.MakerSeparationRequired);
        }
    }

    private static bool ValidMakerHistory(ImmutableArray<string> makerHistory, string preparedBy) =>
        !makerHistory.IsDefaultOrEmpty
        && makerHistory.All(actor => ValidText(actor, ActorMaximumLength))
        && SameActor(makerHistory[^1], preparedBy);

    private static bool IsMaker(SettingsPolicyVersionLifecycleV1 current, string? actor) =>
        !current.MakerHistory.IsDefaultOrEmpty
        && current.MakerHistory.Any(maker => SameActor(maker, actor));

    private static void RequireConsistentSnapshotIdentity(
        SettingsPolicyVersionLifecycleV1 current,
        CapturedRevision revision)
    {
        if (current.ValueSnapshotHistory.Any(snapshot =>
                snapshot.ValueSnapshotId == revision.ValueSnapshotId
                && !snapshot.Values.SequenceEqual(revision.Values)))
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.Invalid);
        }
    }

    private static ImmutableArray<SettingsPolicyValueSnapshotV1> InitialSnapshotHistory(
        Guid valueSnapshotId,
        ImmutableArray<SettingsPolicySnapshotEntryV1> values) =>
        [new SettingsPolicyValueSnapshotV1(valueSnapshotId, values)];

    private static ImmutableArray<SettingsPolicyValueSnapshotV1> AppendSnapshotHistory(
        ImmutableArray<SettingsPolicyValueSnapshotV1> history,
        CapturedRevision revision) =>
        history.Add(new SettingsPolicyValueSnapshotV1(revision.ValueSnapshotId, revision.Values));

    private static bool ValidSnapshotHistory(
        ImmutableArray<SettingsPolicyValueSnapshotV1> history,
        Guid currentSnapshotId,
        ImmutableArray<SettingsPolicySnapshotEntryV1> currentValues)
    {
        if (history.IsDefaultOrEmpty)
        {
            return false;
        }

        var known = new Dictionary<Guid, ImmutableArray<SettingsPolicySnapshotEntryV1>>();
        foreach (var snapshot in history)
        {
            if (snapshot is null
                || snapshot.ValueSnapshotId == Guid.Empty
                || snapshot.Values.IsDefaultOrEmpty
                || !ValidEntries(snapshot.Values, requireValue: true))
            {
                return false;
            }

            if (known.TryGetValue(snapshot.ValueSnapshotId, out var knownValues))
            {
                if (!knownValues.SequenceEqual(snapshot.Values))
                {
                    return false;
                }
            }
            else
            {
                known.Add(snapshot.ValueSnapshotId, snapshot.Values);
            }
        }

        var latest = history[^1];
        return latest.ValueSnapshotId == currentSnapshotId
            && latest.Values.SequenceEqual(currentValues);
    }

    private static bool SameActor(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static bool ValidIdentifier(string? value) => ValidText(value, IdentifierMaximumLength);

    private static bool ValidText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    private static bool Utc(DateTimeOffset? value) =>
        value is { Offset: { } offset } && offset == TimeSpan.Zero;

    private static long Next(long value)
    {
        try
        {
            return checked(value + 1);
        }
        catch (OverflowException)
        {
            throw Failure(SettingsPolicyVersionLifecycleCodesV1.Invalid);
        }
    }

    private static SettingsPolicyVersionLifecycleException Failure(string code) => new(code);

    private sealed record CapturedSnapshot(
        string FamilyId,
        string SchemaId,
        string ScopeIdentity,
        ImmutableArray<SettingsPolicySnapshotEntryV1> ExactScope,
        Guid ValueSnapshotId,
        ImmutableArray<SettingsPolicySnapshotEntryV1> Values,
        DateOnly EffectiveFrom);

    private sealed record CapturedRevision(
        Guid ValueSnapshotId,
        ImmutableArray<SettingsPolicySnapshotEntryV1> Values,
        DateOnly EffectiveFrom);
}
