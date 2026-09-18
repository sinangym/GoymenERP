using System.Globalization;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Manufacturing.Application;

// MFG-API (#313, card approved 2026-09-18): the authorized Manufacturing command surface. Kilograms
// travel as lossless invariant text (Ö2), as Inventory's reads do; a JSON number would lose digits in
// a browser. Every id is the caller's (R3): the same id with the same content is a replay.

public sealed record SavePlanRequestV1(
    Guid PlanId, Guid WorkCenterId, IReadOnlyList<PlannedInputRequestV1>? Inputs, TargetRequestV1? Target);

public sealed record PlannedInputRequestV1(Guid ParentRollId, string? PlannedKg);

/// <summary><see cref="Basis"/> is <c>InputMass</c> or <c>GoodOutputMass</c>, the plan's declared basis.</summary>
public sealed record TargetRequestV1(string? Basis, string? Kg);

public sealed record ApprovePlanRequestV1(long ExpectedVersion);

public sealed record CreateOrderRequestV1(Guid OrderId, Guid PlanId);

/// <summary>An input issue, an unused-input return or a WIP reading of one parent roll, at its own time (R6).</summary>
public sealed record RecordFactRequestV1(
    Guid FactId, long ExpectedVersion, Guid ParentRollId, string? Kg, DateTimeOffset? OccurredAtUtc);

/// <summary><see cref="OutputClass"/> is one of the five <c>OutputClass</c> names.</summary>
public sealed record DeclareOutputRequestV1(
    Guid FactId, long ExpectedVersion, Guid ParentRollId, string? OutputClass, string? Kg, DateTimeOffset? OccurredAtUtc);

public sealed record PlannedInputViewV1(Guid ParentRollId, string PlannedKg);

public sealed record TargetViewV1(string Basis, string Kg);

public sealed record PlanViewV1(
    Guid PlanId, Guid CompanyId, Guid WorkCenterId, string Process, string Status, long Version,
    IReadOnlyList<PlannedInputViewV1> Inputs, TargetViewV1? Target, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAtUtc);

/// <summary>
/// One parent roll's balance from the stored facts: issued − returned = outputs + WIP + unexplained.
/// The unexplained difference keeps its sign and is never folded into a class.
/// </summary>
public sealed record RollBalanceViewV1(
    Guid ParentRollId, string PlannedKg, string IssuedKg, string ReturnedKg, string NetIssuedKg,
    IReadOnlyDictionary<string, string> OutputsKg, string WorkInProgressKg, string UnexplainedKg, bool IsFullyAccounted);

/// <summary>Only in the basis the plan declared; otherwise <c>Basis</c> is null (unavailable, never 0).</summary>
public sealed record RemainingTargetViewV1(string? Basis, string? TargetKg, string? AchievedKg, string? RemainingKg);

/// <summary>Cost is a status only in this slice: every output is <c>NotCalculated</c>.</summary>
public sealed record OrderViewV1(
    Guid OrderId, Guid CompanyId, Guid PlanId, Guid WorkCenterId, string Process, string Status, long Version,
    TargetViewV1? Target, RemainingTargetViewV1 RemainingTarget, IReadOnlyList<RollBalanceViewV1> Rolls,
    string CostStatus, Guid? ClosedByUserId, DateTimeOffset? ClosedAtUtc);

/// <summary>How a command ended, before HTTP: the endpoint maps it and adds nothing of its own.</summary>
public enum ManufacturingApiStatus
{
    Created = 1,
    Ok = 2,
    Invalid = 3,
    Denied = 4,
    NotFound = 5,
    Conflict = 6,
    Unprocessable = 7,
    Unavailable = 8,
}

public sealed record ManufacturingApiResult<T>(
    ManufacturingApiStatus Status, T? Resource, bool IsReplay, string? Code, string? Field = null,
    long? ExpectedVersion = null, long? CurrentVersion = null)
    where T : class;

/// <summary>Codes the command surface adds; everything else is the domain's, the store's or Settings'.</summary>
public static class ManufacturingApiCodes
{
    /// <summary>The request is malformed: a missing id, list or time, or a value outside its grammar.</summary>
    public const string RequestInvalid = "manufacturing_request_invalid";

    /// <summary>The server's actor or authority for this company is not (or no longer) there.</summary>
    public const string AuthorityDenied = "manufacturing_authority_denied";
}

/// <summary>
/// MFG-API: plan / order / fact commands and the order read, for the server-resolved company.
/// <list type="bullet">
/// <item><b>Company:</b> only the company context; no request carries a company, and a record of
/// another company is the store's "not found".</item>
/// <item><b>Mass (Ö2):</b> every kilogram is checked against the company's kilogram profile
/// (<see cref="ManufacturingMassVerifier"/>) before anything is written: facts at their own date,
/// plan masses at the server's date. A Settings refusal keeps its own code.</item>
/// <item><b>Authority (#276):</b> the endpoint's policy answers first; after the Settings read, the
/// permission is measured again with <see cref="IErpAuthorityRevalidationService"/>, and only a fresh,
/// company-wide grant writes. Its role codes go into the audit envelope.</item>
/// <item><b>Replay and concurrency</b> are the store's (MFG-5): the caller's id plus the canonical
/// content, and the version the caller read.</item>
/// </list>
/// It decides no production, tolerance or permission policy.
/// </summary>
public sealed partial class ManufacturingCommandService(
    IManufacturingPlanStore plans,
    IManufacturingOrderStore orders,
    ManufacturingMassVerifier masses,
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    TimeProvider timeProvider)
{
    public async Task<ManufacturingApiResult<PlanViewV1>> SavePlanAsync(SavePlanRequestV1? request, string idempotencyKey,
        string permission, CancellationToken cancellationToken)
    {
        if (request is null || request.PlanId == Guid.Empty || request.WorkCenterId == Guid.Empty
            || request.Inputs is null || request.Inputs.Count == 0
            || request.Inputs.Any(input => input is null || input.ParentRollId == Guid.Empty))
        {
            return Invalid<PlanViewV1>("plan");
        }

        if (request.Target is { } target && ParseBasis(target.Basis) is null)
        {
            return Invalid<PlanViewV1>("target.basis");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var inputs = new List<(Guid ParentRollId, Kilograms Mass)>();
        foreach (var input in request.Inputs)
        {
            var mass = await VerifyAsync(input.PlannedKg, today, "inputs.plannedKg", cancellationToken);
            if (mass.Refusal is not null)
            {
                return Refusal<PlanViewV1>(mass.Refusal);
            }

            inputs.Add((input.ParentRollId, mass.Value));
        }

        Kilograms? targetMass = null;
        if (request.Target is { } declared)
        {
            var mass = await VerifyAsync(declared.Kg, today, "target.kg", cancellationToken);
            if (mass.Refusal is not null)
            {
                return Refusal<PlanViewV1>(mass.Refusal);
            }

            targetMass = mass.Value;
        }

        return await RunAsync(permission, idempotencyKey, async metadata =>
        {
            // Built behind the fence, so the domain's own refusals (a zero planned mass, a repeated
            // roll) map like every other refusal.
            var plan = new ProductionPlan(request.PlanId, companyContext.CompanyId, request.WorkCenterId,
                ManufacturingProcess.Slitting,
                inputs.Select(input => new PlannedInput(input.ParentRollId, input.Mass)),
                request.Target is { } target && targetMass is { } kg
                    ? new ProductionTarget(ParseBasis(target.Basis)!.Value, kg)
                    : null);
            var outcome = await plans.SavePlanAsync(plan, metadata, cancellationToken);
            var stored = await plans.FindPlanAsync(plan.Id, cancellationToken);
            return stored is null
                ? NotFoundResult<PlanViewV1>()
                : new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Created, PlanView(stored),
                    outcome == ManufacturingWriteOutcome.Replayed, null);
        }, cancellationToken);
    }

    public Task<ManufacturingApiResult<PlanViewV1>> ApprovePlanAsync(Guid planId, ApprovePlanRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken)
    {
        if (planId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<PlanViewV1>("expectedVersion"));
        }

        return RunAsync(permission, idempotencyKey, async metadata =>
        {
            var approved = await plans.ApprovePlanAsync(planId, request.ExpectedVersion, ActorGuid()!.Value,
                timeProvider.GetUtcNow(), metadata, cancellationToken);
            return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Ok, PlanView(approved), false, null);
        }, cancellationToken);
    }

    public Task<ManufacturingApiResult<OrderViewV1>> CreateOrderAsync(CreateOrderRequestV1? request, string idempotencyKey,
        string permission, CancellationToken cancellationToken)
    {
        if (request is null || request.OrderId == Guid.Empty || request.PlanId == Guid.Empty)
        {
            return Task.FromResult(Invalid<OrderViewV1>("order"));
        }

        return RunAsync(permission, idempotencyKey, async metadata =>
        {
            var outcome = await orders.CreateOrderAsync(request.OrderId, request.PlanId, metadata, cancellationToken);
            return await OrderResultAsync(request.OrderId, ManufacturingApiStatus.Created,
                outcome == ManufacturingWriteOutcome.Replayed, cancellationToken);
        }, cancellationToken);
    }

    public Task<ManufacturingApiResult<OrderViewV1>> IssueInputAsync(Guid orderId, RecordFactRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken) =>
        RecordAsync(orderId, request, idempotencyKey, permission,
            (fact, mass, at, metadata) => orders.IssueInputAsync(orderId, fact.ExpectedVersion, fact.FactId,
                fact.ParentRollId, mass, at, metadata, cancellationToken), cancellationToken);

    public Task<ManufacturingApiResult<OrderViewV1>> ReturnUnusedInputAsync(Guid orderId, RecordFactRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken) =>
        RecordAsync(orderId, request, idempotencyKey, permission,
            (fact, mass, at, metadata) => orders.ReturnUnusedInputAsync(orderId, fact.ExpectedVersion, fact.FactId,
                fact.ParentRollId, mass, at, metadata, cancellationToken), cancellationToken);

    public Task<ManufacturingApiResult<OrderViewV1>> RecordWorkInProgressAsync(Guid orderId, RecordFactRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken) =>
        RecordAsync(orderId, request, idempotencyKey, permission,
            (fact, mass, at, metadata) => orders.RecordWorkInProgressAsync(orderId, fact.ExpectedVersion, fact.FactId,
                fact.ParentRollId, mass, at, metadata, cancellationToken), cancellationToken);

    public Task<ManufacturingApiResult<OrderViewV1>> DeclareOutputAsync(Guid orderId, DeclareOutputRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken)
    {
        if (request is null || !Enum.TryParse<OutputClass>(request.OutputClass, ignoreCase: false, out var outputClass)
            || !Enum.IsDefined(outputClass) || !IsName(request.OutputClass))
        {
            return Task.FromResult(Invalid<OrderViewV1>("outputClass"));
        }

        var fact = new RecordFactRequestV1(request.FactId, request.ExpectedVersion, request.ParentRollId, request.Kg,
            request.OccurredAtUtc);
        return RecordAsync(orderId, fact, idempotencyKey, permission,
            (valid, mass, at, metadata) => orders.DeclareOutputAsync(orderId, valid.ExpectedVersion, valid.FactId,
                valid.ParentRollId, outputClass, mass, at, metadata, cancellationToken), cancellationToken);
    }

    /// <summary>The company's order rebuilt from its stored facts; another company's order is not found.</summary>
    public async Task<ManufacturingApiResult<OrderViewV1>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (!HasContext())
        {
            return Denied<OrderViewV1>();
        }

        return orderId == Guid.Empty
            ? NotFoundResult<OrderViewV1>()
            : await OrderResultAsync(orderId, ManufacturingApiStatus.Ok, isReplay: false, cancellationToken);
    }

    private async Task<ManufacturingApiResult<OrderViewV1>> RecordAsync(Guid orderId, RecordFactRequestV1? request,
        string idempotencyKey, string permission,
        Func<RecordFactRequestV1, Kilograms, DateTimeOffset, ManufacturingCommandMetadata, Task<ManufacturingWriteOutcome>> record,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || request is null || request.FactId == Guid.Empty || request.ParentRollId == Guid.Empty
            || request.ExpectedVersion <= 0)
        {
            return Invalid<OrderViewV1>("fact");
        }

        if (request.OccurredAtUtc is not { } occurredAt || occurredAt.Offset != TimeSpan.Zero)
        {
            return Invalid<OrderViewV1>("occurredAtUtc");
        }

        var mass = await VerifyAsync(request.Kg, DateOnly.FromDateTime(occurredAt.UtcDateTime), "kg", cancellationToken);
        if (mass.Refusal is not null)
        {
            return Refusal<OrderViewV1>(mass.Refusal);
        }

        return await RunAsync(permission, idempotencyKey, async metadata =>
        {
            var outcome = await record(request, mass.Value, occurredAt, metadata);
            return await OrderResultAsync(orderId, ManufacturingApiStatus.Ok,
                outcome == ManufacturingWriteOutcome.Replayed, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// The fresh authority fence, the envelope, and the mapping of every refusal to one status. Nothing
    /// is written before the fence and nothing after a refusal.
    /// </summary>
    private async Task<ManufacturingApiResult<T>> RunAsync<T>(string permission, string idempotencyKey,
        Func<ManufacturingCommandMetadata, Task<ManufacturingApiResult<T>>> write, CancellationToken cancellationToken)
        where T : class
    {
        if (!HasContext() || ActorGuid() is null)
        {
            return Denied<T>();
        }

        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyContext.CompanyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !HasContext())
        {
            return Denied<T>();
        }

        var metadata = new ManufacturingCommandMetadata(currentUser.UserId!, permission,
            [.. scope.RoleCodes.Order(StringComparer.Ordinal)], correlation.CorrelationId, correlation.CausationId,
            idempotencyKey, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);
        try
        {
            return await write(metadata);
        }
        catch (ManufacturingVersionConflictException exception)
        {
            return new ManufacturingApiResult<T>(ManufacturingApiStatus.Conflict, null, false,
                ManufacturingRuleCodes.VersionConflict, null, exception.ExpectedVersion, exception.CurrentVersion);
        }
        catch (ManufacturingRuleException exception)
        {
            return new ManufacturingApiResult<T>(ManufacturingApiStatus.Unprocessable, null, false, exception.Code);
        }
        catch (ManufacturingStoreException exception)
        {
            return new ManufacturingApiResult<T>(exception.Code switch
            {
                ManufacturingStoreCodes.PlanNotFound or ManufacturingStoreCodes.OrderNotFound => ManufacturingApiStatus.NotFound,
                ManufacturingStoreCodes.CompanyContextMismatch => ManufacturingApiStatus.Denied,
                ManufacturingStoreCodes.PlanIdConflict or ManufacturingStoreCodes.OrderIdConflict
                    or ManufacturingStoreCodes.FactIdConflict or ManufacturingStoreCodes.ConcurrentChange
                    => ManufacturingApiStatus.Conflict,
                _ => ManufacturingApiStatus.Unprocessable,
            }, null, false, exception.Code);
        }
        catch (ArgumentException exception)
        {
            // A caller error the domain names (for example a zero mass where a positive one is needed).
            return Invalid<T>(exception.ParamName ?? "request");
        }
    }

    private async Task<(Kilograms Value, ManufacturingApiResult<object>? Refusal)> VerifyAsync(string? text, DateOnly effectiveOn,
        string field, CancellationToken cancellationToken)
    {
        if (text is null || !KilogramText().IsMatch(text)
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return (default, new ManufacturingApiResult<object>(ManufacturingApiStatus.Invalid, null, false,
                ManufacturingApiCodes.RequestInvalid, field));
        }

        if (!HasContext())
        {
            return (default, new ManufacturingApiResult<object>(ManufacturingApiStatus.Denied, null, false,
                ManufacturingApiCodes.AuthorityDenied));
        }

        var verification = await masses.VerifyAsync(companyContext.CompanyId, value, effectiveOn, cancellationToken);
        if (verification.Mass is { } verified)
        {
            return (verified.Mass, null);
        }

        var status = verification.Code switch
        {
            "settings_access_denied" => ManufacturingApiStatus.Denied,
            "settings_provider_unavailable" or "settings_profile_invalid" => ManufacturingApiStatus.Unavailable,
            _ => ManufacturingApiStatus.Unprocessable,
        };
        return (default, new ManufacturingApiResult<object>(status, null, false, verification.Code, field));
    }

    private async Task<ManufacturingApiResult<OrderViewV1>> OrderResultAsync(Guid orderId, ManufacturingApiStatus status,
        bool isReplay, CancellationToken cancellationToken)
    {
        var order = await orders.FindOrderAsync(orderId, cancellationToken);
        return order is null
            ? NotFoundResult<OrderViewV1>()
            : new ManufacturingApiResult<OrderViewV1>(status, OrderView(order), isReplay, null);
    }

    private bool HasContext() =>
        companyContext.IsResolved && companyContext.CompanyId != Guid.Empty && currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId) && currentUser.CanAccessCompany(companyContext.CompanyId)
        && correlation.IsResolved;

    private Guid? ActorGuid() => Guid.TryParse(currentUser.UserId, out var actor) && actor != Guid.Empty ? actor : null;

    private static TargetBasis? ParseBasis(string? text) =>
        Enum.TryParse<TargetBasis>(text, ignoreCase: false, out var basis) && Enum.IsDefined(basis) && IsName(text)
            ? basis
            : null;

    /// <summary>Only the enum's own name: no number, no other spelling.</summary>
    private static bool IsName(string? text) => text is not null && EnumName().IsMatch(text);

    internal static PlanViewV1 PlanView(ProductionPlan plan) => new(
        plan.Id, plan.CompanyId, plan.WorkCenterId, plan.Process.ToString(), plan.Status.ToString(), plan.Version,
        [.. plan.Inputs.Select(input => new PlannedInputViewV1(input.ParentRollId, Text(input.PlannedMass.Value)))],
        plan.Target is { } target ? new TargetViewV1(target.Basis.ToString(), Text(target.Mass.Value)) : null,
        plan.ApprovedByUserId, plan.ApprovedAtUtc);

    internal static OrderViewV1 OrderView(ProductionOrder order)
    {
        var remaining = order.RemainingTargetOf();
        return new OrderViewV1(
            order.Id, order.CompanyId, order.PlanId, order.WorkCenterId, order.Process.ToString(), order.Status.ToString(),
            order.Version,
            order.Target is { } target ? new TargetViewV1(target.Basis.ToString(), Text(target.Mass.Value)) : null,
            remaining.IsAvailable
                ? new RemainingTargetViewV1(remaining.Basis!.Value.ToString(), Text(remaining.Target!.Value.Value),
                    Text(remaining.Achieved!.Value.Value), Text(remaining.Remaining!.Value))
                : new RemainingTargetViewV1(null, null, null, null),
            [.. order.PlannedInputs.Select(input =>
            {
                var balance = order.BalanceOf(input.ParentRollId);
                return new RollBalanceViewV1(input.ParentRollId, Text(input.PlannedMass.Value), Text(balance.Issued.Value),
                    Text(balance.Returned.Value), Text(balance.NetIssued.Value),
                    Enum.GetValues<OutputClass>().ToDictionary(outputClass => outputClass.ToString(),
                        outputClass => Text(balance.OutputOf(outputClass).Value), StringComparer.Ordinal),
                    Text(balance.WorkInProgress.Value), Text(balance.Unexplained), balance.IsFullyAccounted);
            })],
            nameof(CostStatus.NotCalculated), order.ClosedByUserId, order.ClosedAtUtc);
    }

    /// <summary>Lossless invariant text without trailing zeros: 5.00 and 5 are both "5".</summary>
    internal static string Text(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private static ManufacturingApiResult<T> Invalid<T>(string field) where T : class =>
        new(ManufacturingApiStatus.Invalid, null, false, ManufacturingApiCodes.RequestInvalid, field);

    private static ManufacturingApiResult<T> Denied<T>() where T : class =>
        new(ManufacturingApiStatus.Denied, null, false, ManufacturingApiCodes.AuthorityDenied);

    private static ManufacturingApiResult<T> NotFoundResult<T>() where T : class =>
        new(ManufacturingApiStatus.NotFound, null, false, null);

    private static ManufacturingApiResult<T> Refusal<T>(ManufacturingApiResult<object> refusal) where T : class =>
        new(refusal.Status, null, false, refusal.Code, refusal.Field);

    /// <summary>A non-negative decimal in plain notation: no sign, exponent, grouping or spaces.</summary>
    [GeneratedRegex(@"^(0|[1-9][0-9]{0,17})(\.[0-9]{1,20})?$", RegexOptions.CultureInvariant)]
    private static partial Regex KilogramText();

    [GeneratedRegex(@"^[A-Z][A-Za-z]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EnumName();
}
