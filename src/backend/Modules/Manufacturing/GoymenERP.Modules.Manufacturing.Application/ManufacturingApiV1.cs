using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Manufacturing.Application;

// MFG-API (#313, card approved 2026-09-18): the authorized Manufacturing command surface. Kilograms
// travel as lossless invariant text (Ö2), as Inventory's reads do; a JSON number would lose digits in
// a browser. Every id is the caller's (R3): the same id with the same content is a replay.

public sealed record SavePlanRequestV1(
    Guid PlanId, Guid WorkCenterId, IReadOnlyList<PlannedInputRequestV1>? Inputs, TargetRequestV1? Target);

public sealed record PlannedInputRequestV1(Guid ParentRollId, string? PlannedKg, string? ExpectedLabelVersion = null);

/// <summary><see cref="Basis"/> is <c>InputMass</c> or <c>GoodOutputMass</c>, the plan's declared basis.</summary>
public sealed record TargetRequestV1(string? Basis, string? Kg);

public sealed record ApprovePlanRequestV1(long ExpectedVersion);

public sealed record CreateOrderRequestV1(Guid OrderId, Guid PlanId);

/// <summary>The close of the order at the version the caller read; the close chooses nothing else (MFG-4).</summary>
public sealed record CloseOrderRequestV1(long ExpectedVersion);

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

/// <summary>
/// One order in the list: its stored facts only — the plan and work center it came from, its status and
/// version, how many parent rolls its snapshot plans and their planned mass together. No balance, no engine
/// answer and no roll label: a balance is the order read's answer roll by roll, and the label is Inventory's
/// read (ÜRÜN / T15), not this module's.
/// </summary>
public sealed record OrderSummaryViewV1(
    Guid OrderId, Guid PlanId, Guid WorkCenterId, string Process, string Status, long Version,
    int PlannedRollCount, string PlannedInputKg, TargetViewV1? Target, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc);

/// <summary>
/// One page of the company's orders, newest first. <see cref="NextCursor"/> null is the end; the cursor is
/// opaque and belongs to the company and ordering that produced it.
/// </summary>
public sealed record OrderListViewV1(IReadOnlyList<OrderSummaryViewV1> Orders, string? NextCursor);

/// <summary>
/// One GDC-BAL/1 answer as the engine gave it: <c>Computed</c> with its unrounded value as lossless text, or
/// <c>Unavailable</c> naming every missing input, or <c>Refused</c> with its code. Never a guessed number.
/// </summary>
public sealed record EngineAnswerViewV1(string Outcome, string? Value, IReadOnlyList<string> MissingParameters,
    string? RefusalCode, string? RefusedParameter);

/// <summary>BAL-003 for one parent roll: the actual input, what the categories account for, and the signed difference.</summary>
public sealed record MassDifferenceViewV1(string ActualInputKg, string AccountedKg, string DifferenceKg, bool IsFullyAccounted);

/// <summary>
/// One parent roll through GDC-BAL/1 (master v3 §28.9). <see cref="Difference"/> is BAL-003 from the order's own facts.
/// <see cref="ScrapPercent"/> (BAL-004) and <see cref="ActualUnitKg"/> (BAL-006) are the engine's answer to the inputs
/// the order can supply without a decision — none: which classes are "fire" and on which base is Business's open
/// question, and the order records no piece count — so they answer <c>Unavailable</c>, naming the inputs.
/// </summary>
public sealed record RollMassBalanceViewV1(Guid ParentRollId, MassDifferenceViewV1 Difference,
    EngineAnswerViewV1 ScrapPercent, EngineAnswerViewV1 ActualUnitKg);

/// <summary>The order's mass balance, roll by roll, through the one calculator the close uses; the order's version it was read at.</summary>
public sealed record MassBalanceViewV1(Guid OrderId, long Version, string FormulaVersion, IReadOnlyList<RollMassBalanceViewV1> Rolls);

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

/// <summary>
/// One command's answer. <see cref="IsReplay"/> is true when this is a stored answer; an idempotency
/// replay (ADR-011 §4) also carries the first attempt's <see cref="OriginalCorrelationId"/>.
/// </summary>
public sealed record ManufacturingApiResult<T>(
    ManufacturingApiStatus Status, T? Resource, bool IsReplay, string? Code, string? Field = null,
    long? ExpectedVersion = null, long? CurrentVersion = null, string? OriginalCorrelationId = null)
    where T : class;

/// <summary>Codes the command surface adds; everything else is the domain's, the store's or Settings'.</summary>
public static class ManufacturingApiCodes
{
    /// <summary>The request is malformed: a missing id, list or time, or a value outside its grammar.</summary>
    public const string RequestInvalid = "manufacturing_request_invalid";

    /// <summary>The server's actor or authority for this company is not (or no longer) there.</summary>
    public const string AuthorityDenied = "manufacturing_authority_denied";

    /// <summary>
    /// ADR-011 §3: the same <c>Idempotency-Key</c> for this command was used with another request or by
    /// another actor. The first attempt's answer is not disclosed.
    /// </summary>
    public const string IdempotencyConflict = "idempotency_conflict";
}

/// <summary>The versioned command names of the ADR-011 log: one key is one attempt per command.</summary>
public static class ManufacturingCommandNames
{
    public const string SavePlan = "manufacturing.plan.save.v1";
    public const string ApprovePlan = "manufacturing.plan.approve.v1";
    public const string CreateOrder = "manufacturing.order.create.v1";
    public const string IssueInput = "manufacturing.input.issue.v1";
    public const string ReturnInput = "manufacturing.input.return.v1";
    public const string DeclareOutput = "manufacturing.output.declare.v1";
    public const string RecordWorkInProgress = "manufacturing.wip.record.v1";
    public const string CloseOrder = "manufacturing.order.close.v1";
}

/// <summary>
/// MFG-API: plan / order / fact commands and the order read, for the server-resolved company.
/// <list type="bullet">
/// <item><b>Company:</b> only the company context; no request carries a company, and a record of
/// another company is the store's "not found".</item>
/// <item><b>Idempotency (ADR-011):</b> every write is one command attempt in the module's own log,
/// unique per (company, command name, uppercase SHA-256 of the trimmed key); the raw key is never
/// stored. The request hash is the canonical payload's (path id included, kilograms normalized).
/// After the fresh authority check: the same key, request and actor answers the stored result with
/// <c>Idempotency-Replayed</c> and the first correlation; another request or actor is 409
/// <see cref="ManufacturingApiCodes.IdempotencyConflict"/> without disclosing anything. A success is
/// recorded in the same save as the business change and its audit event; a terminal not-found or
/// version conflict is recorded on its own (§7). Two attempts racing on one key leave one record;
/// the loser answers from it (§8).</item>
/// <item><b>R3 underneath:</b> the store still recognises the same plan / order / fact id with the
/// same content as the same record, whatever the key. That is the ledger's own identity; the key
/// log is the HTTP attempt's.</item>
/// <item><b>Mass (Ö2):</b> every kilogram is checked against the company's kilogram profile
/// (<see cref="ManufacturingMassVerifier"/>) before anything is written: facts at their own date,
/// plan masses at the server's date. A Settings refusal keeps its own code.</item>
/// <item><b>Authority (#276):</b> the endpoint's policy answers first; the permission is measured
/// fresh with <see cref="IErpAuthorityRevalidationService"/> before any stored answer is read and
/// again after the Settings read, and only a company-wide grant writes. Its role codes go into the
/// audit envelope.</item>
/// </list>
/// It decides no production, tolerance or permission policy.
/// </summary>
public sealed partial class ManufacturingCommandService(
    IManufacturingPlanStore plans,
    IManufacturingOrderStore orders,
    IManufacturingCommandLog commands,
    ManufacturingMassVerifier masses,
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    TimeProvider timeProvider,
    IInventoryParentRollValidationV1? parentRolls = null)
{
    /// <summary>Any integer mantissa of this many digits fits a decimal's 96 bits, so such a text converts exactly.</summary>
    private const int ExactDecimalDigits = 28;

    /// <summary>The list page the caller gets without asking, and the one it cannot pass (the store's own bound).</summary>
    private const int DefaultPageSize = 50;

    private const int MaximumPageSize = 100;

    /// <summary>A cursor longer than any this surface produces is refused before it is decoded.</summary>
    private const int MaximumCursorLength = 512;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ManufacturingApiResult<PlanViewV1>> SavePlanAsync(SavePlanRequestV1? request, string idempotencyKey,
        string permission, CancellationToken cancellationToken)
    {
        if (request is null || request.PlanId == Guid.Empty || request.WorkCenterId == Guid.Empty
            || request.Inputs is null || request.Inputs.Count == 0
            || request.Inputs.Any(input => input is null || input.ParentRollId == Guid.Empty))
        {
            return Task.FromResult(Invalid<PlanViewV1>("plan"));
        }

        if (request.Target is { } target && ParseBasis(target.Basis) is null)
        {
            return Task.FromResult(Invalid<PlanViewV1>("target.basis"));
        }

        var inputs = new List<(Guid ParentRollId, string ExpectedLabelVersion, decimal Kg)>();
        for (var index = 0; index < request.Inputs.Count; index++)
        {
            var input = request.Inputs[index];
            if (ParseKg(input.PlannedKg) is not { } kg)
            {
                return Task.FromResult(Invalid<PlanViewV1>("inputs.plannedKg"));
            }

            if (!IsPositiveCanonicalInt64(input.ExpectedLabelVersion))
            {
                return Task.FromResult(Invalid<PlanViewV1>($"inputs[{index}].expectedLabelVersion"));
            }

            inputs.Add((input.ParentRollId, input.ExpectedLabelVersion!, kg));
        }

        decimal? targetKg = null;
        if (request.Target is { } declared)
        {
            if (ParseKg(declared.Kg) is not { } kg)
            {
                return Task.FromResult(Invalid<PlanViewV1>("target.kg"));
            }

            targetKg = kg;
        }

        var canonical = new
        {
            planId = request.PlanId,
            workCenterId = request.WorkCenterId,
            inputs = inputs.Select(input => new
            {
                rollId = input.ParentRollId,
                expectedLabelVersion = input.ExpectedLabelVersion,
                kg = Text(input.Kg),
            }),
            target = request.Target is null ? null : new { basis = request.Target.Basis, kg = Text(targetKg!.Value) },
        };

        return RunAsync<PlanViewV1>(permission, idempotencyKey, ManufacturingCommandNames.SavePlan, canonical,
            ManufacturingApiStatus.Created, async (metadata, _) =>
            {
                var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
                var planned = new List<(Guid ParentRollId, Kilograms Mass)>();
                for (var index = 0; index < inputs.Count; index++)
                {
                    var input = inputs[index];
                    var mass = await VerifyAsync(input.Kg, today, "inputs.plannedKg", cancellationToken);
                    if (mass.Refusal is not null)
                    {
                        return Refusal<PlanViewV1>(mass.Refusal);
                    }

                    var reference = await ValidateParentRollAsync(input.ParentRollId, input.ExpectedLabelVersion,
                        $"inputs[{index}].expectedLabelVersion", cancellationToken);
                    if (reference is not null)
                    {
                        return Refusal<PlanViewV1>(reference);
                    }

                    planned.Add((input.ParentRollId, mass.Value));
                }

                Kilograms? targetMass = null;
                if (targetKg is { } kg)
                {
                    var mass = await VerifyAsync(kg, today, "target.kg", cancellationToken);
                    if (mass.Refusal is not null)
                    {
                        return Refusal<PlanViewV1>(mass.Refusal);
                    }

                    targetMass = mass.Value;
                }

                if (await FenceAsync<PlanViewV1>(permission, cancellationToken) is { } fenced)
                {
                    return fenced;
                }

                // Built behind the fence, so the domain's own refusals (a zero planned mass, a repeated
                // roll) map like every other refusal.
                var plan = new ProductionPlan(request.PlanId, companyContext.CompanyId, request.WorkCenterId,
                    ManufacturingProcess.Slitting, planned.Select(input => new PlannedInput(input.ParentRollId, input.Mass)),
                    request.Target is { } targetRequest && targetMass is { } targetValue
                        ? new ProductionTarget(ParseBasis(targetRequest.Basis)!.Value, targetValue)
                        : null);
                var outcome = await plans.SavePlanAsync(plan, metadata, cancellationToken);
                var stored = await plans.FindPlanAsync(plan.Id, cancellationToken);
                return stored is null
                    ? NotFoundResult<PlanViewV1>()
                    : new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Created, PlanView(stored),
                        outcome == ManufacturingWriteOutcome.Replayed, null);
            }, stored => PlanView((ProductionPlan)stored), cancellationToken);
    }

    public Task<ManufacturingApiResult<PlanViewV1>> ApprovePlanAsync(Guid planId, ApprovePlanRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken)
    {
        if (planId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<PlanViewV1>("expectedVersion"));
        }

        return RunAsync<PlanViewV1>(permission, idempotencyKey, ManufacturingCommandNames.ApprovePlan,
            new { planId, expectedVersion = request.ExpectedVersion }, ManufacturingApiStatus.Ok, async (metadata, actor) =>
            {
                var approved = await plans.ApprovePlanAsync(planId, request.ExpectedVersion, actor,
                    timeProvider.GetUtcNow(), metadata, cancellationToken);
                return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Ok, PlanView(approved), false, null);
            }, stored => PlanView((ProductionPlan)stored), cancellationToken);
    }

    public Task<ManufacturingApiResult<OrderViewV1>> CreateOrderAsync(CreateOrderRequestV1? request, string idempotencyKey,
        string permission, CancellationToken cancellationToken)
    {
        if (request is null || request.OrderId == Guid.Empty || request.PlanId == Guid.Empty)
        {
            return Task.FromResult(Invalid<OrderViewV1>("order"));
        }

        return RunAsync<OrderViewV1>(permission, idempotencyKey, ManufacturingCommandNames.CreateOrder,
            new { orderId = request.OrderId, planId = request.PlanId }, ManufacturingApiStatus.Created, async (metadata, _) =>
            {
                var outcome = await orders.CreateOrderAsync(request.OrderId, request.PlanId, metadata, cancellationToken);
                return await OrderResultAsync(request.OrderId, ManufacturingApiStatus.Created,
                    outcome == ManufacturingWriteOutcome.Replayed, cancellationToken);
            }, stored => OrderView((ProductionOrder)stored), cancellationToken);
    }

    public Task<ManufacturingApiResult<OrderViewV1>> IssueInputAsync(Guid orderId, RecordFactRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken) =>
        RecordAsync(orderId, request, outputClass: null, idempotencyKey, permission, ManufacturingCommandNames.IssueInput,
            (fact, mass, at, metadata) => orders.IssueInputAsync(orderId, fact.ExpectedVersion, fact.FactId,
                fact.ParentRollId, mass, at, metadata, cancellationToken), cancellationToken);

    public Task<ManufacturingApiResult<OrderViewV1>> ReturnUnusedInputAsync(Guid orderId, RecordFactRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken) =>
        RecordAsync(orderId, request, outputClass: null, idempotencyKey, permission, ManufacturingCommandNames.ReturnInput,
            (fact, mass, at, metadata) => orders.ReturnUnusedInputAsync(orderId, fact.ExpectedVersion, fact.FactId,
                fact.ParentRollId, mass, at, metadata, cancellationToken), cancellationToken);

    public Task<ManufacturingApiResult<OrderViewV1>> RecordWorkInProgressAsync(Guid orderId, RecordFactRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken) =>
        RecordAsync(orderId, request, outputClass: null, idempotencyKey, permission,
            ManufacturingCommandNames.RecordWorkInProgress,
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
        return RecordAsync(orderId, fact, outputClass, idempotencyKey, permission, ManufacturingCommandNames.DeclareOutput,
            (valid, mass, at, metadata) => orders.DeclareOutputAsync(orderId, valid.ExpectedVersion, valid.FactId,
                valid.ParentRollId, outputClass, mass, at, metadata, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Closes the order at the version the caller read (MFG-4, Business 5728619644). The decision is the
    /// domain's and nothing here softens it: every planned roll must account for its own issued mass, a
    /// surplus on one roll never covers a gap on another, remaining WIP stays on the order as its own
    /// position, and an unexplained difference refuses the close with
    /// <see cref="ManufacturingRuleCodes.UnexplainedDifference"/> (422) — there is no tolerance and no
    /// write-off. A closed order refuses a second close with <see cref="ManufacturingRuleCodes.OrderClosed"/>;
    /// the same <c>Idempotency-Key</c> replays the first close's answer (ADR-011 §4), and a stale
    /// <see cref="CloseOrderRequestV1.ExpectedVersion"/> is a recorded terminal conflict (§7).
    /// </summary>
    public Task<ManufacturingApiResult<OrderViewV1>> CloseOrderAsync(Guid orderId, CloseOrderRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<OrderViewV1>("expectedVersion"));
        }

        return RunAsync<OrderViewV1>(permission, idempotencyKey, ManufacturingCommandNames.CloseOrder,
            new { orderId, expectedVersion = request.ExpectedVersion }, ManufacturingApiStatus.Ok,
            async (metadata, actor) =>
            {
                if (await FenceAsync<OrderViewV1>(permission, cancellationToken) is { } fenced)
                {
                    return fenced;
                }

                await orders.CloseOrderAsync(orderId, request.ExpectedVersion, actor, timeProvider.GetUtcNow(), metadata,
                    cancellationToken);
                return await OrderResultAsync(orderId, ManufacturingApiStatus.Ok, isReplay: false, cancellationToken);
            }, stored => OrderView((ProductionOrder)stored), cancellationToken);
    }

    /// <summary>
    /// One page of the company's orders, newest first, from the stored rows: another company's order is not
    /// in any page, and the same read permission as the order read is required. <paramref name="status"/> is
    /// an <c>OrderStatus</c> name or null (every status); <paramref name="cursor"/> is the previous page's,
    /// and a cursor of another company, another ordering or no grammar is a 400 on <c>cursor</c>, never a
    /// silent first page. It computes nothing and stores nothing.
    /// </summary>
    public async Task<ManufacturingApiResult<OrderListViewV1>> ListOrdersAsync(int? pageSize, string? cursor,
        string? status, CancellationToken cancellationToken)
    {
        if (!HasContext())
        {
            return Denied<OrderListViewV1>();
        }

        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaximumPageSize)
        {
            return Invalid<OrderListViewV1>("pageSize");
        }

        if (cursor is { Length: 0 or > MaximumCursorLength })
        {
            return Invalid<OrderListViewV1>("cursor");
        }

        OrderStatus? requested = null;
        if (status is not null)
        {
            if (ParseStatus(status) is not { } parsed)
            {
                return Invalid<OrderListViewV1>("status");
            }

            requested = parsed;
        }

        try
        {
            var page = await orders.ListOrdersAsync(size, cursor, requested, cancellationToken);
            return new ManufacturingApiResult<OrderListViewV1>(ManufacturingApiStatus.Ok,
                new OrderListViewV1([.. page.Orders.Select(OrderSummaryView)], page.NextCursor), false, null);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ListCursorInvalid)
        {
            return Invalid<OrderListViewV1>("cursor");
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            return new ManufacturingApiResult<OrderListViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
        }
    }

    /// <summary>
    /// The company's order rebuilt from its stored facts; another company's order is not found. A read
    /// that still meets concurrent commits after the store's own re-reads is a retryable consistency
    /// failure (503), not a command conflict: 409 belongs to commands (FİNANS #313 INFO-4).
    /// </summary>
    public async Task<ManufacturingApiResult<OrderViewV1>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (!HasContext())
        {
            return Denied<OrderViewV1>();
        }

        if (orderId == Guid.Empty)
        {
            return NotFoundResult<OrderViewV1>();
        }

        try
        {
            return await OrderResultAsync(orderId, ManufacturingApiStatus.Ok, isReplay: false, cancellationToken);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            return new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
        }
    }

    /// <summary>
    /// K4.4: one plan, read back. Until this, a plan could be CREATED and APPROVED but never read again —
    /// there was no plan read at any layer above the store, so "reopen a plan" was not a missing screen but
    /// a missing ANSWER. This is what makes a created plan reachable a second time.
    ///
    /// <para>
    /// It invents no reading: <c>FindPlanAsync</c> already existed on the plan store, and the projection is
    /// the SAME <see cref="PlanView"/> the create and approve answers return — so a reopened plan and a
    /// just-created one are identical in shape by construction, rather than by two projections kept in step
    /// by hand. Another company's plan is NOT FOUND, never forbidden: a 403 would confirm the row exists,
    /// which is the order read's rule here too.
    /// </para>
    /// <para>
    /// The 503-not-409 split is <see cref="GetOrderAsync"/>'s and deliberate: a read that still meets
    /// concurrent commits after the store's own re-reads is a retryable consistency failure, and 409 belongs
    /// to commands (FİNANS #313 INFO-4).
    /// </para>
    /// </summary>
    public async Task<ManufacturingApiResult<PlanViewV1>> GetPlanAsync(Guid planId, CancellationToken cancellationToken)
    {
        if (!HasContext())
        {
            return Denied<PlanViewV1>();
        }

        if (planId == Guid.Empty)
        {
            return NotFoundResult<PlanViewV1>();
        }

        try
        {
            var plan = await plans.FindPlanAsync(planId, cancellationToken);
            return plan is null
                ? NotFoundResult<PlanViewV1>()
                : new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Ok, PlanView(plan), false, null);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
        }
    }

    /// <summary>
    /// The order's GDC-BAL/1 read (BAL slice 2): the same company context, read permission and not-found /
    /// retryable-503 answers as <see cref="GetOrderAsync"/>, because it is the same order read through the one
    /// calculator the close uses. It computes; it stores nothing.
    /// </summary>
    public async Task<ManufacturingApiResult<MassBalanceViewV1>> GetMassBalanceAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (!HasContext())
        {
            return Denied<MassBalanceViewV1>();
        }

        if (orderId == Guid.Empty)
        {
            return NotFoundResult<MassBalanceViewV1>();
        }

        try
        {
            var order = await orders.FindOrderAsync(orderId, cancellationToken);
            return order is null
                ? NotFoundResult<MassBalanceViewV1>()
                : new ManufacturingApiResult<MassBalanceViewV1>(ManufacturingApiStatus.Ok, MassBalanceView(order), false, null);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            return new ManufacturingApiResult<MassBalanceViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
        }
    }

    private Task<ManufacturingApiResult<OrderViewV1>> RecordAsync(Guid orderId, RecordFactRequestV1? request,
        OutputClass? outputClass, string idempotencyKey, string permission, string commandName,
        Func<RecordFactRequestV1, Kilograms, DateTimeOffset, ManufacturingCommandMetadata, Task<ManufacturingWriteOutcome>> record,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || request is null || request.FactId == Guid.Empty || request.ParentRollId == Guid.Empty
            || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<OrderViewV1>("fact"));
        }

        if (ParseKg(request.Kg) is not { } kg)
        {
            return Task.FromResult(Invalid<OrderViewV1>("kg"));
        }

        if (request.OccurredAtUtc is not { } occurredAt || occurredAt.Offset != TimeSpan.Zero)
        {
            return Task.FromResult(Invalid<OrderViewV1>("occurredAtUtc"));
        }

        var canonical = new
        {
            orderId,
            factId = request.FactId,
            expectedVersion = request.ExpectedVersion,
            rollId = request.ParentRollId,
            kg = Text(kg),
            occurredAtUtc = occurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            outputClass = outputClass?.ToString(),
        };

        return RunAsync<OrderViewV1>(permission, idempotencyKey, commandName, canonical, ManufacturingApiStatus.Ok,
            async (metadata, _) =>
            {
                var mass = await VerifyAsync(kg, DateOnly.FromDateTime(occurredAt.UtcDateTime), "kg", cancellationToken);
                if (mass.Refusal is not null)
                {
                    return Refusal<OrderViewV1>(mass.Refusal);
                }

                if (await FenceAsync<OrderViewV1>(permission, cancellationToken) is { } fenced)
                {
                    return fenced;
                }

                var outcome = await record(request, mass.Value, occurredAt, metadata);
                return await OrderResultAsync(orderId, ManufacturingApiStatus.Ok,
                    outcome == ManufacturingWriteOutcome.Replayed, cancellationToken);
            }, stored => OrderView((ProductionOrder)stored), cancellationToken);
    }

    /// <summary>
    /// One ADR-011 command attempt. In order: the context and a fresh company-wide grant; the stored
    /// answer for this (command, key), if any; the write, whose success is recorded in its own save;
    /// the recording of a terminal refusal; and the race on one key. Nothing is written before the
    /// fence and nothing after a non-terminal refusal.
    /// </summary>
    private async Task<ManufacturingApiResult<T>> RunAsync<T>(string permission, string idempotencyKey, string commandName,
        object canonicalRequest, ManufacturingApiStatus successStatus,
        Func<ManufacturingCommandMetadata, Guid, Task<ManufacturingApiResult<T>>> write, Func<object, T> view,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!HasContext() || ActorGuid() is not { } actor)
        {
            return Denied<T>();
        }

        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyContext.CompanyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !HasContext())
        {
            return Denied<T>();
        }

        var actorId = currentUser.UserId!;
        var keyHash = Sha256(idempotencyKey.Trim());
        var requestHash = Sha256(commandName + "|" + JsonSerializer.Serialize(canonicalRequest, Json));

        // §4/§3: a stored answer for this command and key, after the fence and before any other read.
        if (await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } prior)
        {
            return Answer<T>(prior, actorId, requestHash);
        }

        ManufacturingApiResult<T>? completed = null;
        ManufacturingCommandRecord Complete(object stored)
        {
            completed = new ManufacturingApiResult<T>(successStatus, view(stored), false, null);
            return Record(commandName, keyHash, actorId, requestHash, completed);
        }

        var metadata = new ManufacturingCommandMetadata(actorId, permission,
            [.. scope.RoleCodes.Order(StringComparer.Ordinal)], correlation.CorrelationId, correlation.CausationId,
            idempotencyKey, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent, Complete);
        ManufacturingApiResult<T> result;
        try
        {
            result = await write(metadata, actor);
        }
        catch (ManufacturingVersionConflictException exception)
        {
            result = new ManufacturingApiResult<T>(ManufacturingApiStatus.Conflict, null, false,
                ManufacturingRuleCodes.VersionConflict, null, exception.ExpectedVersion, exception.CurrentVersion);
        }
        catch (ManufacturingRuleException exception)
        {
            result = new ManufacturingApiResult<T>(ManufacturingApiStatus.Unprocessable, null, false, exception.Code);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            // §8: the same key's other attempt may have won; then its record is the answer.
            return await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } winner
                ? Answer<T>(winner, actorId, requestHash)
                : new ManufacturingApiResult<T>(ManufacturingApiStatus.Conflict, null, false, exception.Code);
        }
        catch (ManufacturingStoreException exception)
        {
            result = new ManufacturingApiResult<T>(exception.Code switch
            {
                ManufacturingStoreCodes.PlanNotFound or ManufacturingStoreCodes.OrderNotFound => ManufacturingApiStatus.NotFound,
                ManufacturingStoreCodes.CompanyContextMismatch => ManufacturingApiStatus.Denied,
                ManufacturingStoreCodes.PlanIdConflict or ManufacturingStoreCodes.OrderIdConflict
                    or ManufacturingStoreCodes.FactIdConflict => ManufacturingApiStatus.Conflict,
                _ => ManufacturingApiStatus.Unprocessable,
            }, null, false, exception.Code);
            if (result.Status == ManufacturingApiStatus.NotFound)
            {
                // A not-found answer names no code: the same answer as for any unknown id.
                result = NotFoundResult<T>();
            }
        }
        catch (ArgumentException exception)
        {
            // A caller error the domain names (for example a zero mass where a positive one is needed).
            return Invalid<T>(exception.ParamName ?? "request");
        }

        if (completed is not null && result.Status == successStatus)
        {
            // The success was recorded in the business save (§6). A record built for a save that then
            // failed was never stored, so that attempt falls through like any other refusal.
            return result;
        }

        // §7: a terminal not-found or version conflict, and a success the ledger already held (R3: the
        // same id and content under a new key), are this key's answer from now on. Anything else
        // (validation, Settings, rules, a lost race without a winner) is not recorded.
        var terminal = result.Status == ManufacturingApiStatus.NotFound
            || (result.Status == ManufacturingApiStatus.Conflict && result.Code == ManufacturingRuleCodes.VersionConflict)
            || (result.Status == ManufacturingApiStatus.Conflict
                && result.Code == InventoryParentRollCodesV1.VersionConflict)
            || result.Status == successStatus;
        if (!terminal)
        {
            // §8, one exit whatever the refusal: another attempt of this key may have committed while
            // this one ran (its rule, version or read then saw the winner's state). Its record is the
            // answer; only without one does this refusal stand.
            return result.Status is ManufacturingApiStatus.Invalid or ManufacturingApiStatus.Denied
                    or ManufacturingApiStatus.Unavailable
                ? result
                : await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } winner
                    ? Answer<T>(winner, actorId, requestHash)
                    : result;
        }

        try
        {
            await commands.RecordCommandAsync(Record(commandName, keyHash, actorId, requestHash, result), cancellationToken);
            return result;
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            return await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } winner
                ? Answer<T>(winner, actorId, requestHash)
                : result;
        }
    }

    /// <summary>§3/§4: the stored answer for the same request and actor; otherwise a conflict that discloses nothing.</summary>
    private static ManufacturingApiResult<T> Answer<T>(ManufacturingCommandRecord prior, string actorId, string requestHash)
        where T : class
    {
        if (!string.Equals(prior.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new ManufacturingApiResult<T>(ManufacturingApiStatus.Conflict, null, false,
                ManufacturingApiCodes.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<ManufacturingApiResult<T>>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı üretim komutu sonucu okunamadı.");
        return stored with { IsReplay = true, OriginalCorrelationId = prior.OriginalCorrelationId };
    }

    private ManufacturingCommandRecord Record<T>(string commandName, string keyHash, string actorId, string requestHash,
        ManufacturingApiResult<T> result)
        where T : class =>
        new(commandName, keyHash, actorId, requestHash,
            JsonSerializer.Serialize(result with { IsReplay = false, OriginalCorrelationId = null }, Json),
            correlation.CorrelationId);

    /// <summary>The fresh authority fence after an owner's await (#276): still granted, company-wide, same context.</summary>
    private async Task<ManufacturingApiResult<T>?> FenceAsync<T>(string permission, CancellationToken cancellationToken)
        where T : class
    {
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyContext.CompanyId, cancellationToken);
        return scope.IsGranted && scope.IsCompanyWide && HasContext() ? null : Denied<T>();
    }

    private async Task<(Kilograms Value, ManufacturingApiResult<object>? Refusal)> VerifyAsync(decimal value, DateOnly effectiveOn,
        string field, CancellationToken cancellationToken)
    {
        if (!HasContext())
        {
            return (default, new ManufacturingApiResult<object>(ManufacturingApiStatus.Denied, null, false,
                ManufacturingApiCodes.AuthorityDenied));
        }

        var verification = await masses.VerifyAsync(companyContext.CompanyId, value, effectiveOn, cancellationToken);
        if (verification.Mass is { } verified)
        {
            // Ö2 on the TEXT, as Settings' ExactPositiveDecimalV1 counts it (FİNANS #353 INFO-1, coordinator ruling:
            // consistency with the platform contract, not a new policy): every written decimal is a decimal, trailing
            // zeros included, so "1.5000" claims a precision the profile does not allow and is refused. The value
            // came from ParseKg, which converts only texts a decimal holds exactly, so its Scale is the text's.
            // Settings still answers first (its refusals keep their precedence); zero stays a valid reading.
            return value.Scale > verified.Scale
                ? (default, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unprocessable, null, false,
                    ManufacturingMassCodes.ScaleExceeded, field))
                : (verified.Mass, null);
        }

        var status = verification.Code switch
        {
            "settings_access_denied" => ManufacturingApiStatus.Denied,
            "settings_provider_unavailable" or "settings_profile_invalid" => ManufacturingApiStatus.Unavailable,
            _ => ManufacturingApiStatus.Unprocessable,
        };
        return (default, new ManufacturingApiResult<object>(status, null, false, verification.Code, field));
    }

    /// <summary>
    /// Revalidates Inventory's exact label/version after this command's stored-answer lookup. The owner
    /// decides draft selectability; Manufacturing only closes the cross-module result into its existing
    /// status channel and verifies the successful answer still names this company and selection.
    /// </summary>
    private async Task<ManufacturingApiResult<object>?> ValidateParentRollAsync(Guid labelId,
        string expectedLabelVersion, string expectedVersionField, CancellationToken cancellationToken)
    {
        if (parentRolls is null)
        {
            return new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                InventoryParentRollCodesV1.Unavailable);
        }

        try
        {
            var reference = await parentRolls.ValidateAsync(
                new InventoryParentRollSelectionV1(labelId, expectedLabelVersion), cancellationToken);
            return reference.CompanyId == companyContext.CompanyId
                   && reference.LabelId == labelId
                   && string.Equals(reference.LabelVersion, expectedLabelVersion, StringComparison.Ordinal)
                   && reference.DraftSelectable
                   && reference.DraftRefusalCode is null
                   && reference.ProductionUseRequiresRevalidation
                ? null
                : new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                    InventoryParentRollCodesV1.Unavailable);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InventoryParentRollException exception)
        {
            return exception switch
            {
                { StatusCode: 400, Code: InventoryParentRollCodesV1.InvalidVersion } =>
                    Invalid<object>(expectedVersionField),
                { StatusCode: 401 or 403 } => Denied<object>(),
                { StatusCode: 404 } => NotFoundResult<object>(),
                { StatusCode: 409, Code: InventoryParentRollCodesV1.VersionConflict
                    or InventoryParentRollCodesV1.ReadConflict } =>
                    new ManufacturingApiResult<object>(ManufacturingApiStatus.Conflict, null, false, exception.Code),
                { StatusCode: 422, Code: InventoryParentRollCodesV1.ContextRequired
                    or InventoryParentRollCodesV1.NoPhysicalPosition } =>
                    new ManufacturingApiResult<object>(ManufacturingApiStatus.Unprocessable, null, false, exception.Code),
                { StatusCode: 503, Code: InventoryParentRollCodesV1.Unavailable } =>
                    new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false, exception.Code),
                _ => new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                    InventoryParentRollCodesV1.Unavailable),
            };
        }
        catch (Exception)
        {
            return new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                InventoryParentRollCodesV1.Unavailable);
        }
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

    /// <summary>
    /// Plain non-negative decimal text only; its scale is Settings' question, not the grammar's. The text is
    /// measured BEFORE it is converted: <see cref="decimal.TryParse(string, NumberStyles, IFormatProvider, out decimal)"/>
    /// rounds digits beyond what a decimal holds (28–29 significant), so a text such as
    /// <c>100000000000000.000000000000001</c> would silently become a whole number and pass Ö2. A text with more
    /// significant digits than <see cref="ExactDecimalDigits"/> (trailing fractional zeros are not digits) cannot
    /// be held exactly and is invalid, never rounded (FİNANS #350 P2-1 sweep). Every such text carries far more
    /// than Ö2's three decimals, so no valid mass is lost; every text within the bound converts exactly and keeps
    /// its scale for the Settings check.
    /// </summary>
    private static decimal? ParseKg(string? text) =>
        text is not null && KilogramText().IsMatch(text) && SignificantDigits(text) <= ExactDecimalDigits
        && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool IsPositiveCanonicalInt64(string? text) =>
        text is not null
        && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value > 0
        && string.Equals(value.ToString(CultureInfo.InvariantCulture), text, StringComparison.Ordinal);

    /// <summary>The integer part's digits (none for "0") plus the fraction's without its trailing zeros; the grammar is already checked.</summary>
    private static int SignificantDigits(string text)
    {
        var point = text.IndexOf('.', StringComparison.Ordinal);
        var integer = point < 0 ? text : text[..point];
        var fraction = point < 0 ? string.Empty : text[(point + 1)..].TrimEnd('0');
        return (integer == "0" ? 0 : integer.Length) + fraction.Length;
    }

    private static OrderStatus? ParseStatus(string? text) =>
        Enum.TryParse<OrderStatus>(text, ignoreCase: false, out var status) && Enum.IsDefined(status) && IsName(text)
            ? status
            : null;

    private static TargetBasis? ParseBasis(string? text) =>
        Enum.TryParse<TargetBasis>(text, ignoreCase: false, out var basis) && Enum.IsDefined(basis) && IsName(text)
            ? basis
            : null;

    /// <summary>Only the enum's own name: no number, no other spelling.</summary>
    private static bool IsName(string? text) => text is not null && EnumName().IsMatch(text);

    /// <summary>ADR-011 §2: the uppercase hex SHA-256 of the UTF-8 text.</summary>
    internal static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static PlanViewV1 PlanView(ProductionPlan plan) => new(
        plan.Id, plan.CompanyId, plan.WorkCenterId, plan.Process.ToString(), plan.Status.ToString(), plan.Version,
        [.. plan.Inputs.Select(input => new PlannedInputViewV1(input.ParentRollId, Text(input.PlannedMass.Value)))],
        plan.Target is { } target ? new TargetViewV1(target.Basis.ToString(), Text(target.Mass.Value)) : null,
        plan.ApprovedByUserId, plan.ApprovedAtUtc);

    /// <summary>The stored summary as text: the same lossless kilogram text as every other mass here.</summary>
    internal static OrderSummaryViewV1 OrderSummaryView(ManufacturingOrderSummary summary) => new(
        summary.OrderId, summary.PlanId, summary.WorkCenterId, summary.Process.ToString(), summary.Status.ToString(),
        summary.Version, summary.PlannedRollCount, Text(summary.PlannedInputMass.Value),
        summary is { TargetBasis: { } basis, TargetMass: { } targetMass }
            ? new TargetViewV1(basis.ToString(), Text(targetMass.Value))
            : null,
        summary.CreatedAtUtc, summary.ClosedAtUtc);

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

    /// <summary>
    /// GDC-BAL/1 per planned parent roll. BAL-003 is the roll's own <see cref="MassBalance.Reconciliation"/> (the
    /// close's calculation). BAL-004 and BAL-006 are asked with what the order can supply without a decision —
    /// nothing — so the engine answers <c>Unavailable</c> and names the inputs; no definition of "fire", base mass
    /// or piece count is chosen here (BAL-004: Business, #66 5744055923; BAL-006: the count-producing slices).
    /// </summary>
    internal static MassBalanceViewV1 MassBalanceView(ProductionOrder order) =>
        new(order.Id, order.Version, MassBalanceEngineV1.FormulaVersion,
        [
            .. order.PlannedInputs.Select(input =>
            {
                var balance = order.BalanceOf(input.ParentRollId);
                var difference = balance.Reconciliation;
                return new RollMassBalanceViewV1(input.ParentRollId,
                    new MassDifferenceViewV1(Text(balance.NetIssued.Value), Text(difference.AccountedKg),
                        Text(difference.DifferenceKg), difference.IsFullyAccounted),
                    Answer(MassBalanceEngineV1.ScrapPercent(new ScrapPercentInputV1(null, null)), result => result.FirePercent),
                    Answer(MassBalanceEngineV1.ActualUnitMass(new ActualUnitMassInputV1(null, null)), result => result.ActualUnitKg));
            }),
        ]);

    private static EngineAnswerViewV1 Answer<T>(EngineResult<T> result, Func<T, decimal> value)
        where T : class =>
        new(result.Outcome.ToString(), result.Value is { } computed ? Text(value(computed)) : null,
            result.MissingParameters, result.RefusalCode, result.RefusedParameter);

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
