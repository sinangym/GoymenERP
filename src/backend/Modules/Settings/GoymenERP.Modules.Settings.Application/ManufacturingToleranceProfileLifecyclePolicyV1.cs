using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// Pure lifecycle policy selected by D-MFG-PROFILE-AUTH-01=A. It does not authorize an actor,
/// persist a command, choose a tolerance value or activate a profile.
/// </summary>
public static class ManufacturingToleranceProfileLifecyclePolicyV1
{
    private const int ActorMaximumLength = 200;
    private const int IdempotencyMaximumLength = 128;
    private const int ReasonMaximumLength = 500;

    public static ManufacturingToleranceProfileLifecycleV1 Prepare(
        PrepareManufacturingToleranceProfileV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateContext(request.Context);
        if (request.ProfileId == Guid.Empty
            || !SettingsManufacturingProcessesV1.IsCanonical(request.Process)
            || request.ProfileVersion <= 0
            || request.Context.ExpectedStateVersion != 0)
        {
            throw Failure(ManufacturingToleranceProfileLifecycleCodesV1.Invalid);
        }

        return Draft(
            request.Context.CompanyId,
            request.ProfileId,
            request.Process,
            request.ProfileVersion,
            request.Context);
    }

    public static ManufacturingToleranceProfileLifecycleV1 Revise(
        ManufacturingToleranceProfileLifecycleV1 current,
        ManufacturingToleranceProfileCommandContextV1 context)
    {
        Validate(current, context);
        RequireStatus(current, ManufacturingToleranceProfileStatusV1.Draft);
        return current with
        {
            StateVersion = Next(current.StateVersion),
            PreparedBy = context.ActorId,
            PreparedAtUtc = context.OccurredAtUtc,
            PreparationReason = context.Reason,
        };
    }

    public static ManufacturingToleranceProfileLifecycleV1 Approve(
        ManufacturingToleranceProfileLifecycleV1 current,
        ManufacturingToleranceProfileCommandContextV1 context)
    {
        Validate(current, context);
        RequireStatus(current, ManufacturingToleranceProfileStatusV1.Draft);
        RequireNotMaker(current, context);
        return current with
        {
            StateVersion = Next(current.StateVersion),
            Status = ManufacturingToleranceProfileStatusV1.Approved,
            ApprovedBy = context.ActorId,
            ApprovedAtUtc = context.OccurredAtUtc,
            ApprovalReason = context.Reason,
        };
    }

    public static ManufacturingToleranceProfileLifecycleV1 Publish(
        ManufacturingToleranceProfileLifecycleV1 current,
        ManufacturingToleranceProfileCommandContextV1 context)
    {
        Validate(current, context);
        RequireStatus(current, ManufacturingToleranceProfileStatusV1.Approved);
        RequireNotMaker(current, context);
        return current with
        {
            StateVersion = Next(current.StateVersion),
            Status = ManufacturingToleranceProfileStatusV1.Published,
            PublishedBy = context.ActorId,
            PublishedAtUtc = context.OccurredAtUtc,
            PublicationReason = context.Reason,
        };
    }

    public static ManufacturingToleranceProfileLifecycleV1 PrepareNextVersion(
        ManufacturingToleranceProfileLifecycleV1 current,
        PrepareNextManufacturingToleranceProfileVersionV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(current, request.Context);
        RequireStatus(current, ManufacturingToleranceProfileStatusV1.Published);
        if (request.ProfileId == Guid.Empty || request.ProfileId == current.ProfileId)
        {
            throw Failure(ManufacturingToleranceProfileLifecycleCodesV1.Invalid);
        }

        return Draft(
            current.CompanyId,
            request.ProfileId,
            current.Process,
            Next(current.ProfileVersion),
            request.Context);
    }

    private static ManufacturingToleranceProfileLifecycleV1 Draft(
        Guid companyId,
        Guid profileId,
        string process,
        long profileVersion,
        ManufacturingToleranceProfileCommandContextV1 context) =>
        new(
            companyId,
            profileId,
            process,
            profileVersion,
            StateVersion: 1,
            ManufacturingToleranceProfileStatusV1.Draft,
            context.ActorId,
            context.OccurredAtUtc,
            context.Reason,
            ApprovedBy: null,
            ApprovedAtUtc: null,
            ApprovalReason: null,
            PublishedBy: null,
            PublishedAtUtc: null,
            PublicationReason: null);

    private static void Validate(
        ManufacturingToleranceProfileLifecycleV1 current,
        ManufacturingToleranceProfileCommandContextV1 context)
    {
        ArgumentNullException.ThrowIfNull(current);
        ValidateState(current);
        ValidateContext(context);
        if (current.CompanyId != context.CompanyId)
        {
            throw Failure(ManufacturingToleranceProfileLifecycleCodesV1.Invalid);
        }

        if (current.StateVersion != context.ExpectedStateVersion)
        {
            throw new ManufacturingToleranceProfileLifecycleException(
                ManufacturingToleranceProfileLifecycleCodesV1.VersionConflict,
                context.ExpectedStateVersion,
                current.StateVersion);
        }
    }

    private static void ValidateState(ManufacturingToleranceProfileLifecycleV1 current)
    {
        var baseValid = current.CompanyId != Guid.Empty
            && current.ProfileId != Guid.Empty
            && SettingsManufacturingProcessesV1.IsCanonical(current.Process)
            && current.ProfileVersion > 0
            && current.StateVersion > 0
            && Enum.IsDefined(current.Status)
            && ValidText(current.PreparedBy, ActorMaximumLength)
            && Utc(current.PreparedAtUtc)
            && ValidText(current.PreparationReason, ReasonMaximumLength);
        var statusValid = current.Status switch
        {
            ManufacturingToleranceProfileStatusV1.Draft =>
                current.ApprovedBy is null && current.ApprovedAtUtc is null && current.ApprovalReason is null
                && current.PublishedBy is null && current.PublishedAtUtc is null && current.PublicationReason is null,
            ManufacturingToleranceProfileStatusV1.Approved =>
                ValidText(current.ApprovedBy, ActorMaximumLength)
                && current.ApprovedBy != current.PreparedBy
                && Utc(current.ApprovedAtUtc)
                && ValidText(current.ApprovalReason, ReasonMaximumLength)
                && current.PublishedBy is null && current.PublishedAtUtc is null && current.PublicationReason is null,
            ManufacturingToleranceProfileStatusV1.Published =>
                ValidText(current.ApprovedBy, ActorMaximumLength)
                && current.ApprovedBy != current.PreparedBy
                && Utc(current.ApprovedAtUtc)
                && ValidText(current.ApprovalReason, ReasonMaximumLength)
                && ValidText(current.PublishedBy, ActorMaximumLength)
                && current.PublishedBy != current.PreparedBy
                && Utc(current.PublishedAtUtc)
                && ValidText(current.PublicationReason, ReasonMaximumLength),
            _ => false,
        };
        if (!baseValid || !statusValid)
        {
            throw Failure(ManufacturingToleranceProfileLifecycleCodesV1.Invalid);
        }
    }

    private static void ValidateContext(ManufacturingToleranceProfileCommandContextV1 context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CompanyId == Guid.Empty
            || !ValidText(context.ActorId, ActorMaximumLength)
            || context.ExpectedStateVersion < 0
            || !ValidText(context.IdempotencyKey, IdempotencyMaximumLength)
            || !ValidText(context.Reason, ReasonMaximumLength)
            || !Utc(context.OccurredAtUtc))
        {
            throw Failure(ManufacturingToleranceProfileLifecycleCodesV1.Invalid);
        }
    }

    private static void RequireStatus(
        ManufacturingToleranceProfileLifecycleV1 current,
        ManufacturingToleranceProfileStatusV1 expected)
    {
        if (current.Status != expected)
        {
            throw Failure(ManufacturingToleranceProfileLifecycleCodesV1.InvalidTransition);
        }
    }

    private static void RequireNotMaker(
        ManufacturingToleranceProfileLifecycleV1 current,
        ManufacturingToleranceProfileCommandContextV1 context)
    {
        if (string.Equals(current.PreparedBy, context.ActorId, StringComparison.Ordinal))
        {
            throw Failure(ManufacturingToleranceProfileLifecycleCodesV1.MakerSeparationRequired);
        }
    }

    private static bool ValidText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    private static bool Utc(DateTimeOffset? value) => value is { Offset: { } offset } && offset == TimeSpan.Zero;

    private static long Next(long value)
    {
        try
        {
            return checked(value + 1);
        }
        catch (OverflowException)
        {
            throw Failure(ManufacturingToleranceProfileLifecycleCodesV1.Invalid);
        }
    }

    private static ManufacturingToleranceProfileLifecycleException Failure(string code) => new(code);
}
