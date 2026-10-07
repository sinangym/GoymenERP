using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Manufacturing.Application;

// MFG-API (#313, card approved 2026-09-18): the authorized Manufacturing command surface. Kilograms
// travel as lossless invariant text (Ö2), as Inventory's reads do; a JSON number would lose digits in
// a browser. Every id is the caller's (R3): the same id with the same content is a replay.

public sealed record SavePlanRequestV1(
    Guid PlanId, Guid WorkCenterId, IReadOnlyList<PlannedInputRequestV1>? Inputs, TargetRequestV1? Target);

public sealed record PlanCommercialContextRequestV1(
    Guid PartyId,
    Guid CompanyPartyAccountId,
    string? PartyVersion,
    string? AccountVersion);

public sealed record PlannedInputRequestV1(
    Guid ParentRollId,
    string? PlannedKg,
    string ExpectedLabelVersion,
    PlanCommercialContextRequestV1? CommercialContext = null);

/// <summary><see cref="Basis"/> is <c>InputMass</c> or <c>GoodOutputMass</c>, the plan's declared basis.</summary>
public sealed record TargetRequestV1(string? Basis, string? Kg);

public sealed record ApprovePlanRequestV1(long ExpectedVersion);

/// <summary>
/// MFG-PLAN-EDIT: the Draft plan's new content AS A WHOLE — work center, planned inputs (each with the Inventory label
/// version the caller read) and the optional target — at the version the caller read, with the user's reason. The
/// process is not in the request: it is the plan's own and never changes. The same grammar as the plan save.
/// </summary>
public sealed record RevisePlanRequestV1(
    long ExpectedVersion, Guid WorkCenterId, IReadOnlyList<PlannedInputRequestV1>? Inputs, TargetRequestV1? Target,
    string? Reason);

public sealed record CreateOrderRequestV1(Guid OrderId, Guid PlanId);

/// <summary>
/// The close of the order at the version the caller read. <see cref="ExceptionReason"/> is absent on the normal
/// attempt and may be supplied only after the server requires the separately authorized tolerance exception.
/// </summary>
public sealed record CloseOrderRequestV1(long ExpectedVersion, string? ExceptionReason = null);

/// <summary>An input issue, an unused-input return or a WIP reading of one parent roll, at its own time (R6).</summary>
public sealed record RecordFactRequestV1(
    Guid FactId, long ExpectedVersion, Guid ParentRollId, string? Kg, DateTimeOffset? OccurredAtUtc);

public sealed record IssueInventoryInputRequestV1(
    Guid IssueId, long ExpectedVersion, Guid ParentRollId, Guid MovementId, string? Kg,
    DateTimeOffset? OccurredAtUtc);

public sealed record ReturnInventoryInputRequestV1(
    Guid ReturnId, long ExpectedVersion, Guid ParentRollId, string? ExpectedParentLabelVersion,
    Guid MovementId, string? Kg, DateTimeOffset? OccurredAtUtc);

public sealed record DeclareInventoryOutputRequestV1(
    Guid OutputId, long ExpectedVersion, Guid ParentRollId, string? ExpectedParentLabelVersion,
    string? OutputClass, Guid ProductId, string? ExpectedProductVersion, Guid LotId, Guid LabelId,
    string? LabelCode, string? HeatNumber, string? CoilNumber, IReadOnlyList<string>? RequiredDocuments,
    string? OwnerKind, Guid OwnerId, string? CustodianKind, Guid CustodianId, Guid LocationId,
    string? InitialBlock, Guid MovementId, string? Kg, DateTimeOffset? OccurredAtUtc);

/// <summary><see cref="OutputClass"/> is one of the five <c>OutputClass</c> names.</summary>
public sealed record DeclareOutputRequestV1(
    Guid FactId, long ExpectedVersion, Guid ParentRollId, string? OutputClass, string? Kg, DateTimeOffset? OccurredAtUtc);

public sealed record CapturedPlanFamilyRevisionViewV1(Guid FamilyId, string FamilyCode, string SourceRevision);

public sealed record CapturedPlanTemplateRevisionViewV1(
    Guid TemplateId, string TemplateVersion, string TemplateCode, string SourceRevision);

public sealed record CapturedPlanKilogramUnitViewV1(
    Guid CompanyId, Guid UnitId, string UnitCode, Guid ProfileId, string ProfileVersion);

/// <summary>
/// Product/Settings facts captured for this exact planned Inventory label. Null on legacy plans;
/// legacy is unavailable and is never filled by reading the current Product card.
/// </summary>
public sealed record CapturedPlanTechnicalSnapshotViewV1(
    Guid ProductId,
    string ProductVersion,
    CapturedPlanFamilyRevisionViewV1 Family,
    CapturedPlanTemplateRevisionViewV1 Template,
    string ThicknessMm,
    string WidthMm,
    CapturedPlanKilogramUnitViewV1 UnitKg,
    DateOnly EffectiveOn);

/// <summary>
/// Exact Counterparty party/account provenance captured for a customer or tolling input. Null is legacy or
/// company-owned input; a read never replaces a captured version with the Counterparty record's current version.
/// </summary>
public sealed record CapturedPlanCommercialContextViewV1(
    Guid PartyId,
    Guid CompanyPartyAccountId,
    string PartyVersion,
    string AccountVersion);

public sealed record PlannedInputViewV1(
    Guid ParentRollId,
    string PlannedKg,
    CapturedPlanTechnicalSnapshotViewV1? TechnicalSnapshot = null,
    CapturedPlanCommercialContextViewV1? CommercialContext = null);

public sealed record TargetViewV1(string Basis, string Kg);

public sealed record PlanViewV1(
    Guid PlanId, Guid CompanyId, Guid WorkCenterId, string Process, string Status, long Version,
    IReadOnlyList<PlannedInputViewV1> Inputs, TargetViewV1? Target, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAtUtc,
    PlanPreviewViewV1? Calculation = null);

/// <summary>
/// MFG-PLAN-EDIT: one planned parent roll of a history entry, with the Inventory label version accepted for it
/// (null only for a plan stored before label versions were kept). The edit form sends it back for a kept roll, and
/// Inventory revalidates it; a roll whose label moved on is refused and must be selected again.
/// </summary>
public sealed record PlanRevisionInputViewV1(
    Guid ParentRollId,
    string PlannedKg,
    string? ExpectedLabelVersion,
    CapturedPlanCommercialContextViewV1? CommercialContext = null,
    CapturedPlanTechnicalSnapshotViewV1? TechnicalSnapshot = null);

/// <summary>
/// MFG-PLAN-EDIT: one entry of a plan's history. Revision 0 is the plan as created (no reason, no reviser); each later
/// revision is one Draft edit: the complete content it set, the plan version it produced, its reason, the actor id
/// that recorded it and when. The last entry is the plan's current content.
/// </summary>
public sealed record PlanRevisionViewV1(
    int Revision, long PlanVersion, Guid WorkCenterId, IReadOnlyList<PlanRevisionInputViewV1> Inputs,
    TargetViewV1? Target, string? Reason, string? RevisedBy, DateTimeOffset RecordedAtUtc);

/// <summary>MFG-PLAN-EDIT: the plan's history, oldest first.</summary>
public sealed record PlanRevisionListViewV1(Guid PlanId, IReadOnlyList<PlanRevisionViewV1> Revisions);

/// <summary>
/// One parent roll's balance from the stored facts: issued − returned = outputs + WIP + unexplained.
/// The unexplained difference keeps its sign and is never folded into a class.
/// </summary>
public sealed record RollBalanceViewV1(
    Guid ParentRollId, string PlannedKg, string IssuedKg, string ReturnedKg, string NetIssuedKg,
    IReadOnlyDictionary<string, string> OutputsKg, string WorkInProgressKg, string UnexplainedKg, bool IsFullyAccounted,
    CapturedPlanCommercialContextViewV1? CommercialContext = null);

/// <summary>Only in the basis the plan declared; otherwise <c>Basis</c> is null (unavailable, never 0).</summary>
public sealed record RemainingTargetViewV1(string? Basis, string? TargetKg, string? AchievedKg, string? RemainingKg);

/// <summary>Cost is a status only in this slice: every output is <c>NotCalculated</c>.</summary>
public sealed record OrderViewV1(
    Guid OrderId, Guid CompanyId, Guid PlanId, Guid WorkCenterId, string Process, string Status, long Version,
    TargetViewV1? Target, RemainingTargetViewV1 RemainingTarget, IReadOnlyList<RollBalanceViewV1> Rolls,
    string CostStatus, Guid? ClosedByUserId, DateTimeOffset? ClosedAtUtc);

/// <summary>
/// One plan in the plan list (MFG-PLAN-LIST): its stored row only — the work center and process it was bound to,
/// its status and version, how many parent rolls it plans and their planned mass together, its declared target and
/// its creation and approval times. No engine answer, no spec, no roll label and no order it may have opened: one
/// approved plan may open more than one order (#66 5728934213 is open), so the list claims no "converted" state.
/// </summary>
public sealed record PlanSummaryViewV1(
    Guid PlanId, Guid WorkCenterId, string Process, string Status, long Version, int PlannedRollCount,
    string PlannedInputKg, TargetViewV1? Target, DateTimeOffset CreatedAtUtc, DateTimeOffset? ApprovedAtUtc);

/// <summary>
/// One page of the company's plans, newest first. <see cref="NextCursor"/> null is the end; the cursor is opaque
/// and belongs to the company and to the plan list's ordering that produced it.
/// </summary>
public sealed record PlanListViewV1(IReadOnlyList<PlanSummaryViewV1> Plans, string? NextCursor);

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

    /// <summary>The selected party or company account changed after the user read its candidate version.</summary>
    public const string CommercialContextVersionConflict =
        "manufacturing_commercial_context_version_conflict";

    /// <summary>The exact party/account is inactive or has neither the customer nor tolling role.</summary>
    public const string CommercialContextIneligible =
        "manufacturing_commercial_context_ineligible";

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
    public const string RevisePlan = "manufacturing.plan.revise.v1";
    public const string CreateOrder = "manufacturing.order.create.v1";
    public const string IssueInput = "manufacturing.input.issue.v1";
    public const string ReturnInput = "manufacturing.input.return.v1";
    public const string DeclareOutput = "manufacturing.output.declare.v1";
    public const string DeclareProcessLoss = "manufacturing.process-loss.declare.v1";
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
    IInventoryParentRollValidationV1? parentRolls = null,
    IOrganizationWorkCenterPlanningReferenceV1? workCenters = null,
    IManufacturingInventoryInputIssuePortV1? inventoryInputIssues = null,
    IManufacturingInventoryReturnOutputPortV1? inventoryReturnsAndOutputs = null,
    IManufacturingPlanListStore? planList = null,
    IManufacturingPlanRevisionStore? planRevisions = null,
    ISettingsManufacturingToleranceProviderV1? manufacturingTolerances = null,
    IProductManufacturingTechnicalSnapshotV1? productTechnicalSnapshots = null,
    ICounterpartyManufacturingPartyAccountSourceV1? partyAccounts = null,
    IInventoryContextualParentRollReferencesV1? contextualParentRolls = null)
{
    // These owner-provider codes stay inside the application result. The public endpoint continues to
    // collapse every unavailable answer to service_unavailable; the disposable MFG fixture may map the
    // closed values to one fixed phase marker while diagnosing its own seed.
    private const string CounterpartyProviderUnavailable = "counterparty_provider_unavailable";
    private const string ProductTechnicalSnapshotUnavailable =
        "product_manufacturing_technical_snapshot_unavailable";

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

        var content = ParsePlanContent(request.Inputs, request.Target);
        if (content.InvalidField is { } invalidField)
        {
            return Task.FromResult(Invalid<PlanViewV1>(invalidField));
        }

        var inputs = content.Inputs!;
        var targetKg = content.TargetKg;
        var canonical = new
        {
            planId = request.PlanId,
            workCenterId = request.WorkCenterId,
            inputs = CanonicalPlanInputs(inputs),
            target = request.Target is null ? null : new { basis = request.Target.Basis, kg = Text(targetKg!.Value) },
        };

        return RunAsync<PlanViewV1>(permission, idempotencyKey, ManufacturingCommandNames.SavePlan, canonical,
            ManufacturingApiStatus.Created, async (metadata, actorId, companyId) =>
            {
                var process = await ResolveProcessAsync(
                    request.WorkCenterId, permission, companyId, actorId, cancellationToken);
                if (process.Refusal is not null)
                {
                    return Refusal<PlanViewV1>(process.Refusal);
                }

                var verified = await VerifyPlanContentAsync(
                    inputs, targetKg, permission, companyId, actorId, cancellationToken);
                if (verified.Refusal is not null)
                {
                    return Refusal<PlanViewV1>(verified.Refusal);
                }

                if (await FenceCapturedAsync<PlanViewV1>(permission, companyId, actorId, cancellationToken) is { } fenced)
                {
                    return fenced;
                }

                // Built behind the fence, so the domain's own refusals (a zero planned mass, a repeated
                // roll) map like every other refusal.
                var plan = new ProductionPlan(request.PlanId, companyId, request.WorkCenterId,
                    process.Value, PlannedInputs(verified.Inputs), Target(request.Target, verified.TargetMass));
                var outcome = await plans.SavePlanAsync(plan, metadata, cancellationToken);
                var stored = await plans.FindPlanAsync(plan.Id, cancellationToken);
                return stored is null
                    ? NotFoundResult<PlanViewV1>()
                    : new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Created, PlanView(stored),
                        outcome == ManufacturingWriteOutcome.Replayed, null);
            }, stored => PlanView((ProductionPlan)stored), cancellationToken);
    }

    /// <summary>
    /// MFG-PLAN-EDIT: the Draft edit of a stored plan (<c>POST /plans/{planId}/revision</c>). The new content is taken
    /// AS A WHOLE with the plan save's grammar and checks — the work center's process from Organization, every mass
    /// against the company's kilogram profile, every roll's exact label version revalidated by Inventory — plus a
    /// reason, at the version the caller read. The plan's own rules decide (<c>ProductionPlan.Revise</c>): only a
    /// Draft, never a plan with a slitting spec, never another process, never a change of nothing; a plan that already
    /// opened an order is refused as not a draft by the store. Fail-closed order: the stored answer for the key, then
    /// the stored plan's own state (not found, stale version, not a draft, spec) BEFORE any other owner is asked, then
    /// the owners, the fresh fence, and one save. ADR-011 as the approval: a success, a not-found and a stale version
    /// are the key's answer from then on; any other refusal is evaluated again on retry. It chooses no policy.
    /// </summary>
    public Task<ManufacturingApiResult<PlanViewV1>> RevisePlanAsync(Guid planId, RevisePlanRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken)
    {
        if (planId == Guid.Empty || request is null)
        {
            return Task.FromResult(Invalid<PlanViewV1>("plan"));
        }

        if (request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<PlanViewV1>("expectedVersion"));
        }

        if (request.WorkCenterId == Guid.Empty || request.Inputs is null || request.Inputs.Count == 0
            || request.Inputs.Any(input => input is null || input.ParentRollId == Guid.Empty))
        {
            return Task.FromResult(Invalid<PlanViewV1>("plan"));
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > ManufacturingStoreCodes.PlanRevisionReasonMaximumLength
            || reason.Any(char.IsControl))
        {
            return Task.FromResult(Invalid<PlanViewV1>("reason"));
        }

        var content = ParsePlanContent(request.Inputs, request.Target);
        if (content.InvalidField is { } invalidField)
        {
            return Task.FromResult(Invalid<PlanViewV1>(invalidField));
        }

        var inputs = content.Inputs!;
        var targetKg = content.TargetKg;
        var canonical = new
        {
            planId,
            expectedVersion = request.ExpectedVersion,
            workCenterId = request.WorkCenterId,
            inputs = CanonicalPlanInputs(inputs),
            target = request.Target is null ? null : new { basis = request.Target.Basis, kg = Text(targetKg!.Value) },
            reason,
        };

        return RunAsync<PlanViewV1>(permission, idempotencyKey, ManufacturingCommandNames.RevisePlan, canonical,
            ManufacturingApiStatus.Ok, async (metadata, actorId, companyId) =>
            {
                if (planRevisions is null)
                {
                    return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
                }

                // The stored plan answers first: an unknown plan, a stale version, an approved plan or a plan with a
                // slitting spec is refused before Organization, Settings or Inventory is asked.
                var current = await plans.FindPlanAsync(planId, cancellationToken);
                if (current is null)
                {
                    return NotFoundResult<PlanViewV1>();
                }

                current.RequireRevisable(request.ExpectedVersion);

                var process = await ResolveProcessAsync(
                    request.WorkCenterId, permission, companyId, actorId, cancellationToken);
                if (process.Refusal is not null)
                {
                    return Refusal<PlanViewV1>(process.Refusal);
                }

                var verified = await VerifyPlanContentAsync(
                    inputs, targetKg, permission, companyId, actorId, cancellationToken);
                if (verified.Refusal is not null)
                {
                    return Refusal<PlanViewV1>(verified.Refusal);
                }

                if (await FenceCapturedAsync<PlanViewV1>(permission, companyId, actorId, cancellationToken) is { } fenced)
                {
                    return fenced;
                }

                var revised = await planRevisions.RevisePlanAsync(planId, request.ExpectedVersion, request.WorkCenterId,
                    process.Value, [.. PlannedInputs(verified.Inputs)], Target(request.Target, verified.TargetMass),
                    reason, metadata, cancellationToken);
                return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Ok, PlanView(revised), false, null);
            }, stored => PlanView((ProductionPlan)stored), cancellationToken);
    }

    /// <summary>
    /// MFG-PLAN-EDIT: the plan's history under the plan read's grant: revision 0 as created, then every recorded Draft
    /// edit, oldest first. Another company's plan is the same code-less 404 as an unknown id; without the revision
    /// port the answer is a retryable 503, never an empty history. It computes nothing and stores nothing.
    /// </summary>
    public async Task<ManufacturingApiResult<PlanRevisionListViewV1>> ListPlanRevisionsAsync(Guid planId,
        CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<PlanRevisionListViewV1>();
        }

        if (planId == Guid.Empty)
        {
            return NotFoundResult<PlanRevisionListViewV1>();
        }

        if (planRevisions is null)
        {
            return new ManufacturingApiResult<PlanRevisionListViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
        }

        var companyId = companyContext.CompanyId;
        try
        {
            var revisions = await planRevisions.ListPlanRevisionsAsync(planId, cancellationToken);
            var scope = await authority.RevalidatePermissionScopeAsync(
                ErpPermissions.ManufacturingPlansRead, companyId, cancellationToken);
            if (!scope.IsGranted || !HasCapturedContext(companyId, actorId))
            {
                return Denied<PlanRevisionListViewV1>();
            }

            return revisions is null
                ? NotFoundResult<PlanRevisionListViewV1>()
                : new ManufacturingApiResult<PlanRevisionListViewV1>(ManufacturingApiStatus.Ok,
                    new PlanRevisionListViewV1(planId, [.. revisions.Select(PlanRevisionView)]), false, null);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            var scope = await authority.RevalidatePermissionScopeAsync(
                ErpPermissions.ManufacturingPlansRead, companyId, cancellationToken);
            if (!scope.IsGranted || !HasCapturedContext(companyId, actorId))
            {
                return Denied<PlanRevisionListViewV1>();
            }

            return new ManufacturingApiResult<PlanRevisionListViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
        }
    }

    /// <summary>
    /// The plan content's grammar, shared by the save and the revision in the save's order: the target basis, every
    /// planned kilogram, every roll's canonical label version, the target kilogram. The first invalid field is named.
    /// </summary>
    private sealed record ParsedPlanInput(
        Guid ParentRollId,
        string ExpectedLabelVersion,
        decimal Kg,
        PlanCommercialContextRequestV1? CommercialContext);

    private sealed record VerifiedPlanInput(
        Guid ParentRollId,
        Kilograms Mass,
        long ExpectedLabelVersion,
        PlanTechnicalParameterSnapshotV1 TechnicalSnapshot,
        PlanCommercialContextSnapshotV1? CommercialContext);

    /// <summary>
    /// ADR-011 compatibility: the published plan command predates commercial context. A null context therefore keeps
    /// the exact old per-input JSON object; only a supplied context adds the new member to the immutable request hash.
    /// </summary>
    private static IEnumerable<object> CanonicalPlanInputs(IEnumerable<ParsedPlanInput> inputs) =>
        inputs.Select(input => input.CommercialContext is null
            ? (object)new
            {
                rollId = input.ParentRollId,
                expectedLabelVersion = input.ExpectedLabelVersion,
                kg = Text(input.Kg),
            }
            : new
            {
                rollId = input.ParentRollId,
                expectedLabelVersion = input.ExpectedLabelVersion,
                kg = Text(input.Kg),
                commercialContext = input.CommercialContext,
            });

    private static (List<ParsedPlanInput>? Inputs, decimal? TargetKg,
        string? InvalidField) ParsePlanContent(IReadOnlyList<PlannedInputRequestV1> requested, TargetRequestV1? target)
    {
        if (target is not null && ParseBasis(target.Basis) is null)
        {
            return (null, null, "target.basis");
        }

        var inputs = new List<ParsedPlanInput>();
        for (var index = 0; index < requested.Count; index++)
        {
            var input = requested[index];
            if (ParseKg(input.PlannedKg) is not { } kg)
            {
                return (null, null, "inputs.plannedKg");
            }

            if (!IsPositiveCanonicalInt64(input.ExpectedLabelVersion))
            {
                return (null, null, $"inputs[{index}].expectedLabelVersion");
            }

            if (input.CommercialContext is { } commercial)
            {
                if (commercial.PartyId == Guid.Empty)
                {
                    return (null, null, $"inputs[{index}].commercialContext.partyId");
                }

                if (commercial.CompanyPartyAccountId == Guid.Empty)
                {
                    return (null, null, $"inputs[{index}].commercialContext.companyPartyAccountId");
                }

                if (string.IsNullOrEmpty(commercial.PartyVersion))
                {
                    return (null, null, $"inputs[{index}].commercialContext.partyVersion");
                }

                if (string.IsNullOrEmpty(commercial.AccountVersion))
                {
                    return (null, null, $"inputs[{index}].commercialContext.accountVersion");
                }
            }

            inputs.Add(new ParsedPlanInput(input.ParentRollId, input.ExpectedLabelVersion!, kg,
                input.CommercialContext));
        }

        decimal? targetKg = null;
        if (target is not null)
        {
            if (ParseKg(target.Kg) is not { } kg)
            {
                return (null, null, "target.kg");
            }

            targetKg = kg;
        }

        return (inputs, targetKg, null);
    }

    /// <summary>
    /// The owners' checks of a plan content, shared by the save and the revision in the save's order: each roll's mass
    /// against the company's kilogram profile (at the server's date) and its exact label version against Inventory,
    /// then the target mass. The first refusal is the answer; nothing is written.
    /// </summary>
    private async Task<(List<VerifiedPlanInput> Inputs, Kilograms? TargetMass,
        ManufacturingApiResult<object>? Refusal)> VerifyPlanContentAsync(
        List<ParsedPlanInput> inputs, decimal? targetKg, string permission, Guid companyId, Guid actorId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var planned = new List<VerifiedPlanInput>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            var mass = await VerifyAsync(input.Kg, today, "inputs.plannedKg", cancellationToken);
            if (mass.Refusal is not null)
            {
                return (planned, null, mass.Refusal);
            }

            var commercial = await ReadCommercialContextAsync(
                input.CommercialContext, index, permission, companyId, actorId, cancellationToken);
            if (commercial.Refusal is not null)
            {
                return (planned, null, commercial.Refusal);
            }

            var reference = await ValidateParentRollAsync(input.ParentRollId, input.ExpectedLabelVersion,
                $"inputs[{index}].expectedLabelVersion", commercial.Snapshot,
                permission, companyId, actorId, cancellationToken);
            if (reference.Refusal is not null)
            {
                return (planned, null, reference.Refusal);
            }

            var technical = await ReadTechnicalSnapshotAsync(reference.Reference!, today,
                $"inputs[{index}].parentRollId", permission, companyId, actorId, cancellationToken);
            if (technical.Refusal is not null)
            {
                return (planned, null, technical.Refusal);
            }

            planned.Add(new VerifiedPlanInput(input.ParentRollId, mass.Value, long.Parse(input.ExpectedLabelVersion,
                NumberStyles.None, CultureInfo.InvariantCulture), technical.Snapshot!, commercial.Snapshot));
        }

        Kilograms? targetMass = null;
        if (targetKg is { } kg)
        {
            var mass = await VerifyAsync(kg, today, "target.kg", cancellationToken);
            if (mass.Refusal is not null)
            {
                return (planned, null, mass.Refusal);
            }

            targetMass = mass.Value;
        }

        return (planned, targetMass, null);
    }

    private static IEnumerable<PlannedInput> PlannedInputs(
        IEnumerable<VerifiedPlanInput> verified) =>
        verified.Select(input => new PlannedInput(input.ParentRollId, input.Mass, input.ExpectedLabelVersion,
            input.TechnicalSnapshot, input.CommercialContext));

    private static ProductionTarget? Target(TargetRequestV1? requested, Kilograms? mass) =>
        requested is not null && mass is { } value ? new ProductionTarget(ParseBasis(requested.Basis)!.Value, value) : null;

    public Task<ManufacturingApiResult<PlanViewV1>> ApprovePlanAsync(Guid planId, ApprovePlanRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken)
    {
        if (planId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<PlanViewV1>("expectedVersion"));
        }

        return RunAsync<PlanViewV1>(permission, idempotencyKey, ManufacturingCommandNames.ApprovePlan,
            new { planId, expectedVersion = request.ExpectedVersion }, ManufacturingApiStatus.Ok,
            async (metadata, actor, _) =>
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
            new { orderId = request.OrderId, planId = request.PlanId }, ManufacturingApiStatus.Created,
            async (metadata, _, _) =>
            {
                var outcome = await orders.CreateOrderAsync(request.OrderId, request.PlanId, metadata, cancellationToken);
                return await OrderResultAsync(request.OrderId, ManufacturingApiStatus.Created,
                    outcome == ManufacturingWriteOutcome.Replayed, cancellationToken);
            }, stored => OrderView((ProductionOrder)stored), cancellationToken);
    }

    public Task<ManufacturingApiResult<OrderViewV1>> IssueInventoryInputAsync(
        Guid orderId,
        IssueInventoryInputRequestV1? request,
        string idempotencyKey,
        string permission,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || request is null || request.IssueId == Guid.Empty
            || request.ParentRollId == Guid.Empty || request.MovementId == Guid.Empty
            || request.MovementId == request.IssueId || request.ExpectedVersion <= 0)
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
            issueId = request.IssueId,
            expectedVersion = request.ExpectedVersion,
            parentLabelId = request.ParentRollId,
            movementId = request.MovementId,
            kg = Text(kg),
            occurredAtUtc = occurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        };

        return RunAsync<OrderViewV1>(permission, idempotencyKey, ManufacturingCommandNames.IssueInput,
            canonical, ManufacturingApiStatus.Ok, async (metadata, _, _) =>
            {
                if (inventoryInputIssues is null)
                {
                    return new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                        InventoryManufacturingRealizationCodesV1.Unavailable);
                }

                var mass = await VerifyAsync(kg, DateOnly.FromDateTime(occurredAt.UtcDateTime), "kg", cancellationToken);
                if (mass.Refusal is not null)
                {
                    return Refusal<OrderViewV1>(mass.Refusal);
                }

                if (await FenceAsync<OrderViewV1>(permission, cancellationToken) is { } fenced)
                {
                    return fenced;
                }

                var outcome = await inventoryInputIssues.IssueAsync(
                    new ManufacturingInventoryInputIssuePortCommandV1(
                        orderId, request.ExpectedVersion, request.IssueId, request.ParentRollId,
                        request.MovementId, mass.Value, occurredAt),
                    metadata, cancellationToken);
                return await OrderResultAsync(orderId, ManufacturingApiStatus.Ok, outcome.IsReplay, cancellationToken);
            }, stored => OrderView((ProductionOrder)stored), cancellationToken);
    }

    public Task<ManufacturingApiResult<OrderViewV1>> ReturnInventoryInputAsync(
        Guid orderId,
        ReturnInventoryInputRequestV1? request,
        string idempotencyKey,
        string permission,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || request is null || request.ReturnId == Guid.Empty
            || request.ParentRollId == Guid.Empty || request.MovementId == Guid.Empty
            || request.MovementId == request.ReturnId || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<OrderViewV1>("fact"));
        }

        if (!IsPositiveCanonicalInt64(request.ExpectedParentLabelVersion))
        {
            return Task.FromResult(Invalid<OrderViewV1>("expectedParentLabelVersion"));
        }

        if (ParseKg(request.Kg) is not { } kg)
        {
            return Task.FromResult(Invalid<OrderViewV1>("kg"));
        }

        if (request.OccurredAtUtc is not { } occurredAt || occurredAt.Offset != TimeSpan.Zero)
        {
            return Task.FromResult(Invalid<OrderViewV1>("occurredAtUtc"));
        }

        var expectedParentLabelVersion = long.Parse(
            request.ExpectedParentLabelVersion!, CultureInfo.InvariantCulture);
        var canonical = new
        {
            orderId,
            returnId = request.ReturnId,
            expectedVersion = request.ExpectedVersion,
            parentLabelId = request.ParentRollId,
            expectedParentLabelVersion,
            movementId = request.MovementId,
            kg = Text(kg),
            occurredAtUtc = occurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        };

        return RunAsync<OrderViewV1>(permission, idempotencyKey, ManufacturingCommandNames.ReturnInput,
            canonical, ManufacturingApiStatus.Ok, async (metadata, _, _) =>
            {
                if (inventoryReturnsAndOutputs is null)
                {
                    return new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                        InventoryManufacturingRealizationCodesV1.Unavailable);
                }

                var mass = await VerifyAsync(kg, DateOnly.FromDateTime(occurredAt.UtcDateTime), "kg", cancellationToken);
                if (mass.Refusal is not null)
                {
                    return Refusal<OrderViewV1>(mass.Refusal);
                }

                if (await FenceAsync<OrderViewV1>(permission, cancellationToken) is { } fenced)
                {
                    return fenced;
                }

                var outcome = await inventoryReturnsAndOutputs.ReturnAsync(
                    new ManufacturingInventoryInputReturnPortCommandV1(
                        orderId, request.ExpectedVersion, request.ReturnId, request.ParentRollId,
                        expectedParentLabelVersion, request.MovementId, mass.Value, occurredAt),
                    metadata, cancellationToken);
                return await OrderResultAsync(orderId, ManufacturingApiStatus.Ok, outcome.IsReplay, cancellationToken);
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

    public Task<ManufacturingApiResult<OrderViewV1>> DeclareInventoryOutputAsync(
        Guid orderId,
        DeclareInventoryOutputRequestV1? request,
        string idempotencyKey,
        string permission,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || request is null || request.OutputId == Guid.Empty
            || request.ParentRollId == Guid.Empty || request.ProductId == Guid.Empty
            || request.LotId == Guid.Empty || request.LabelId == Guid.Empty
            || request.OwnerId == Guid.Empty || request.CustodianId == Guid.Empty
            || request.LocationId == Guid.Empty || request.MovementId == Guid.Empty
            || request.MovementId == request.OutputId || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<OrderViewV1>("output"));
        }

        if (!IsPositiveCanonicalInt64(request.ExpectedParentLabelVersion))
        {
            return Task.FromResult(Invalid<OrderViewV1>("expectedParentLabelVersion"));
        }

        if (!IsPositiveCanonicalInt64(request.ExpectedProductVersion))
        {
            return Task.FromResult(Invalid<OrderViewV1>("expectedProductVersion"));
        }

        if (!Enum.TryParse<OutputClass>(request.OutputClass, ignoreCase: false, out var outputClass)
            || !Enum.IsDefined(outputClass) || !IsName(request.OutputClass))
        {
            return Task.FromResult(Invalid<OrderViewV1>("outputClass"));
        }

        // ProcessLoss is explicitly non-stock. It must never create an Inventory label or movement through this
        // stock-output command; RecordProcessLossAsync is the public fixed-class fact path.
        if (outputClass == OutputClass.ProcessLoss)
        {
            return Task.FromResult(Invalid<OrderViewV1>("outputClass"));
        }

        if (string.IsNullOrWhiteSpace(request.LabelCode))
        {
            return Task.FromResult(Invalid<OrderViewV1>("labelCode"));
        }

        if (request.RequiredDocuments is null
            || request.RequiredDocuments.Any(document => string.IsNullOrWhiteSpace(document)))
        {
            return Task.FromResult(Invalid<OrderViewV1>("requiredDocuments"));
        }

        if (request.OwnerKind is not ("Company" or "Counterparty"))
        {
            return Task.FromResult(Invalid<OrderViewV1>("ownerKind"));
        }

        if (request.CustodianKind is not ("Company" or "Counterparty"))
        {
            return Task.FromResult(Invalid<OrderViewV1>("custodianKind"));
        }

        if (request.InitialBlock is not ("Free" or "Blocked"))
        {
            return Task.FromResult(Invalid<OrderViewV1>("initialBlock"));
        }

        if (ParseKg(request.Kg) is not { } kg)
        {
            return Task.FromResult(Invalid<OrderViewV1>("kg"));
        }

        if (request.OccurredAtUtc is not { } occurredAt || occurredAt.Offset != TimeSpan.Zero)
        {
            return Task.FromResult(Invalid<OrderViewV1>("occurredAtUtc"));
        }

        var expectedParentLabelVersion = long.Parse(
            request.ExpectedParentLabelVersion!, CultureInfo.InvariantCulture);
        var canonical = new
        {
            orderId,
            outputId = request.OutputId,
            expectedVersion = request.ExpectedVersion,
            parentLabelId = request.ParentRollId,
            expectedParentLabelVersion,
            outputClass = outputClass.ToString(),
            productId = request.ProductId,
            expectedProductVersion = request.ExpectedProductVersion,
            lotId = request.LotId,
            labelId = request.LabelId,
            labelCode = request.LabelCode.Trim(),
            request.HeatNumber,
            request.CoilNumber,
            requiredDocuments = request.RequiredDocuments,
            request.OwnerKind,
            request.OwnerId,
            request.CustodianKind,
            request.CustodianId,
            request.LocationId,
            request.InitialBlock,
            request.MovementId,
            kg = Text(kg),
            occurredAtUtc = occurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        };

        return RunAsync<OrderViewV1>(permission, idempotencyKey, ManufacturingCommandNames.DeclareOutput,
            canonical, ManufacturingApiStatus.Ok, async (metadata, _, _) =>
            {
                if (inventoryReturnsAndOutputs is null)
                {
                    return new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                        InventoryManufacturingRealizationCodesV1.Unavailable);
                }

                var mass = await VerifyAsync(kg, DateOnly.FromDateTime(occurredAt.UtcDateTime), "kg", cancellationToken);
                if (mass.Refusal is not null)
                {
                    return Refusal<OrderViewV1>(mass.Refusal);
                }

                if (await FenceAsync<OrderViewV1>(permission, cancellationToken) is { } fenced)
                {
                    return fenced;
                }

                var outcome = await inventoryReturnsAndOutputs.DeclareOutputAsync(
                    new ManufacturingInventoryOutputPortCommandV1(
                        orderId, request.ExpectedVersion, request.OutputId, request.ParentRollId,
                        expectedParentLabelVersion, outputClass, request.ProductId, request.ExpectedProductVersion!,
                        request.LotId, request.LabelId, request.LabelCode.Trim(), request.HeatNumber,
                        request.CoilNumber, request.RequiredDocuments, request.OwnerKind, request.OwnerId,
                        request.CustodianKind, request.CustodianId, request.LocationId, request.InitialBlock,
                        request.MovementId, mass.Value, occurredAt),
                    metadata, cancellationToken);
                return await OrderResultAsync(orderId, ManufacturingApiStatus.Ok, outcome.IsReplay, cancellationToken);
            }, stored => OrderView((ProductionOrder)stored), cancellationToken);
    }

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
    /// Records declared non-stock process loss. The class is fixed by the server; the caller supplies only the same
    /// fact identity/version/roll/mass/time envelope as WIP. No Product, Inventory label, location or movement is
    /// accepted or created, and unexplained mass is never promoted into this fact.
    /// </summary>
    public Task<ManufacturingApiResult<OrderViewV1>> RecordProcessLossAsync(Guid orderId,
        RecordFactRequestV1? request, string idempotencyKey, string permission,
        CancellationToken cancellationToken) =>
        RecordAsync(orderId, request, OutputClass.ProcessLoss, idempotencyKey, permission,
            ManufacturingCommandNames.DeclareProcessLoss,
            (fact, mass, at, metadata) => orders.DeclareOutputAsync(orderId, fact.ExpectedVersion, fact.FactId,
                fact.ParentRollId, OutputClass.ProcessLoss, mass, at, metadata, cancellationToken), cancellationToken);

    /// <summary>
    /// Closes the order at the version the caller read. Settings supplies the exact company/process/date tolerance;
    /// Manufacturing persists it and derives fire (Scrap + ProcessLoss) and net issued mass from its own facts. A
    /// positive unexplained difference still requires an explicit fact. An out-of-tolerance result requires the
    /// separate permission passed by the host and a reason bound to company/order/actor/order version/policy version.
    /// A closed order refuses a second close; the same key replays the first answer (ADR-011 §4), and a stale version
    /// is a recorded terminal conflict (§7).
    /// </summary>
    public Task<ManufacturingApiResult<OrderViewV1>> CloseOrderAsync(Guid orderId, CloseOrderRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken) =>
        CloseOrderAsync(orderId, request, idempotencyKey, permission, exceptionApprovalPermission: null,
            cancellationToken);

    /// <summary>The host-bound overload that names the dedicated exception permission without importing host policy.</summary>
    public Task<ManufacturingApiResult<OrderViewV1>> CloseOrderAsync(Guid orderId, CloseOrderRequestV1? request,
        string idempotencyKey, string permission, string? exceptionApprovalPermission,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<OrderViewV1>("expectedVersion"));
        }

        var exceptionReason = request.ExceptionReason?.Trim();
        if (request.ExceptionReason is not null
            && (string.IsNullOrEmpty(exceptionReason)
                || exceptionReason.Length > ClosureExceptionApprovalV1.ReasonMaximumLength
                || exceptionReason.Any(char.IsControl)))
        {
            return Task.FromResult(Invalid<OrderViewV1>("exceptionReason"));
        }

        // The published normal-close command predates exception approval. Its canonical payload omitted the
        // optional member, so an absent reason must keep that exact hash for immutable ADR-011 replay.
        object canonicalRequest = exceptionReason is null
            ? new { orderId, expectedVersion = request.ExpectedVersion }
            : new { orderId, expectedVersion = request.ExpectedVersion, exceptionReason };

        return RunAsync<OrderViewV1>(permission, idempotencyKey, ManufacturingCommandNames.CloseOrder,
            canonicalRequest, ManufacturingApiStatus.Ok,
            async (metadata, actor, companyId) =>
            {
                var current = await orders.FindOrderAsync(orderId, cancellationToken);
                if (current is null)
                {
                    return NotFoundResult<OrderViewV1>();
                }

                // Stored state answers before another owner is asked: stale/closed never query Settings.
                current.RequireClosable(request.ExpectedVersion);
                if (manufacturingTolerances is null)
                {
                    return new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                        "settings_provider_unavailable");
                }

                var closedAt = timeProvider.GetUtcNow().ToUniversalTime();
                var selectedForDate = DateOnly.FromDateTime(closedAt.UtcDateTime);
                var requestedProcess = current.Process.ToString();
                ManufacturingToleranceProfileV1 profile;
                try
                {
                    profile = await manufacturingTolerances.GetManufacturingToleranceAsync(
                        requestedProcess, selectedForDate, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (SettingsPrerequisiteException exception)
                {
                    var status = exception.Code switch
                    {
                        "settings_access_denied" => ManufacturingApiStatus.Denied,
                        "settings_provider_unavailable" or "settings_profile_invalid" =>
                            ManufacturingApiStatus.Unavailable,
                        _ => ManufacturingApiStatus.Unprocessable,
                    };
                    return new ManufacturingApiResult<OrderViewV1>(status, null, false, exception.Code);
                }

                if (profile.CompanyId != current.CompanyId
                    || !string.Equals(profile.Process, requestedProcess, StringComparison.Ordinal))
                {
                    return new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                        ClosureAssessmentCodesV1.PolicyScopeMismatch);
                }

                var policy = new ClosureTolerancePolicySnapshotV1(
                    profile.CompanyId, profile.Profile.ProfileId, profile.Profile.Version, current.Process,
                    profile.MaximumFirePercent, profile.IsActive, profile.EffectiveFrom, profile.EffectiveThrough,
                    selectedForDate);
                var assessment = current.AssessClosure(policy);
                if (assessment.Disposition is not (ClosureDispositionV1.Balanced
                    or ClosureDispositionV1.ApprovalRequired))
                {
                    var status = assessment.Disposition == ClosureDispositionV1.Unavailable
                        ? ManufacturingApiStatus.Unavailable
                        : ManufacturingApiStatus.Unprocessable;
                    return new ManufacturingApiResult<OrderViewV1>(status, null, false,
                        assessment.Code ?? ClosureAssessmentCodesV1.PolicyInvalid, assessment.RefusedParameter);
                }

                ClosureExceptionApprovalV1? approval = null;
                if (assessment.Disposition == ClosureDispositionV1.ApprovalRequired)
                {
                    if (exceptionReason is null)
                    {
                        return new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Unprocessable, null,
                            false, ClosureAssessmentCodesV1.ApprovalRequired);
                    }

                    if (string.IsNullOrWhiteSpace(exceptionApprovalPermission)
                        || await FenceAsync<OrderViewV1>(exceptionApprovalPermission, cancellationToken) is not null)
                    {
                        return Denied<OrderViewV1>();
                    }

                    approval = ClosureExceptionApprovalV1.Create(
                        current.CompanyId, current.Id, actor, exceptionReason, current.Version,
                        policy.PolicyId, policy.PolicyVersion, closedAt);
                }
                else if (exceptionReason is not null)
                {
                    return new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Unprocessable, null, false,
                        ClosureAssessmentCodesV1.ApprovalUnexpected);
                }

                if (await FenceAsync<OrderViewV1>(permission, cancellationToken) is { } fenced)
                {
                    return fenced;
                }

                if (approval is not null
                    && await FenceAsync<OrderViewV1>(exceptionApprovalPermission!, cancellationToken) is { } approvalFenced)
                {
                    return approvalFenced;
                }

                var persistenceAuthority = new ManufacturingPersistenceAuthorityFenceV1(companyId, actor,
                    token => RevalidateClosePersistenceAuthorityAsync(companyId, actor, permission,
                        approval is null ? null : exceptionApprovalPermission, token));
                await orders.CloseOrderWithPolicyAsync(orderId, request.ExpectedVersion, actor, closedAt, policy,
                    approval, persistenceAuthority, metadata, cancellationToken);
                return await OrderResultAsync(orderId, ManufacturingApiStatus.Ok, isReplay: false, cancellationToken);
            }, stored => OrderView((ProductionOrder)stored), cancellationToken,
            storedAnswerPermission: exceptionReason is null ? null : exceptionApprovalPermission ?? string.Empty);
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
    /// One page of the company's plans, newest first (MFG-PLAN-LIST), the order list's rules: the stored rows only,
    /// another company's plan in no page, and the same read permission as the plan read (the endpoint's policy).
    /// <paramref name="status"/> is a <c>PlanStatus</c> name (<c>Draft</c> / <c>Approved</c>) or null (every status);
    /// <paramref name="cursor"/> is the previous page's, and a cursor of another company, another ordering (an
    /// order-list cursor included) or no grammar is a 400 on <c>cursor</c>, never a silent first page. Without the
    /// plan-list port the answer is a retryable 503, never an empty page. It computes nothing and stores nothing.
    /// </summary>
    public async Task<ManufacturingApiResult<PlanListViewV1>> ListPlansAsync(int? pageSize, string? cursor,
        string? status, CancellationToken cancellationToken)
    {
        if (!HasContext())
        {
            return Denied<PlanListViewV1>();
        }

        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaximumPageSize)
        {
            return Invalid<PlanListViewV1>("pageSize");
        }

        if (cursor is { Length: 0 or > MaximumCursorLength })
        {
            return Invalid<PlanListViewV1>("cursor");
        }

        PlanStatus? requested = null;
        if (status is not null)
        {
            if (ParsePlanStatus(status) is not { } parsed)
            {
                return Invalid<PlanListViewV1>("status");
            }

            requested = parsed;
        }

        if (planList is null)
        {
            return new ManufacturingApiResult<PlanListViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
        }

        try
        {
            var page = await planList.ListPlansAsync(size, cursor, requested, cancellationToken);
            return new ManufacturingApiResult<PlanListViewV1>(ManufacturingApiStatus.Ok,
                new PlanListViewV1([.. page.Plans.Select(PlanSummaryView)], page.NextCursor), false, null);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ListCursorInvalid)
        {
            return Invalid<PlanListViewV1>("cursor");
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            return new ManufacturingApiResult<PlanListViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
        }
    }

    /// <summary>
    /// The company's order rebuilt from its stored facts; another company's order is not found. A read
    /// that still meets concurrent commits after the store's own re-reads is a retryable consistency
    /// failure (503), not a command conflict: 409 belongs to commands (FİNANS #313 INFO-4).
    /// </summary>
    public async Task<ManufacturingApiResult<OrderViewV1>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<OrderViewV1>();
        }

        if (orderId == Guid.Empty)
        {
            return NotFoundResult<OrderViewV1>();
        }

        var companyId = companyContext.CompanyId;
        try
        {
            var order = await orders.FindOrderAsync(orderId, cancellationToken);
            var scope = await authority.RevalidatePermissionScopeAsync(
                ErpPermissions.ManufacturingOrdersRead, companyId, cancellationToken);
            if (!scope.IsGranted || !HasCapturedContext(companyId, actorId))
            {
                return Denied<OrderViewV1>();
            }

            return order is null
                ? NotFoundResult<OrderViewV1>()
                : new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Ok, OrderView(order), false, null);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            var scope = await authority.RevalidatePermissionScopeAsync(
                ErpPermissions.ManufacturingOrdersRead, companyId, cancellationToken);
            if (!scope.IsGranted || !HasCapturedContext(companyId, actorId))
            {
                return Denied<OrderViewV1>();
            }

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
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<PlanViewV1>();
        }

        if (planId == Guid.Empty)
        {
            return NotFoundResult<PlanViewV1>();
        }

        var companyId = companyContext.CompanyId;
        try
        {
            var plan = await plans.FindPlanAsync(planId, cancellationToken);
            var scope = await authority.RevalidatePermissionScopeAsync(
                ErpPermissions.ManufacturingPlansRead, companyId, cancellationToken);
            if (!scope.IsGranted || !HasCapturedContext(companyId, actorId))
            {
                return Denied<PlanViewV1>();
            }

            return plan is null
                ? NotFoundResult<PlanViewV1>()
                : new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Ok, PlanView(plan), false, null);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            var scope = await authority.RevalidatePermissionScopeAsync(
                ErpPermissions.ManufacturingPlansRead, companyId, cancellationToken);
            if (!scope.IsGranted || !HasCapturedContext(companyId, actorId))
            {
                return Denied<PlanViewV1>();
            }

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
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<MassBalanceViewV1>();
        }

        if (orderId == Guid.Empty)
        {
            return NotFoundResult<MassBalanceViewV1>();
        }

        var companyId = companyContext.CompanyId;
        try
        {
            var order = await orders.FindOrderAsync(orderId, cancellationToken);
            var scope = await authority.RevalidatePermissionScopeAsync(
                ErpPermissions.ManufacturingOrdersRead, companyId, cancellationToken);
            if (!scope.IsGranted || !HasCapturedContext(companyId, actorId))
            {
                return Denied<MassBalanceViewV1>();
            }

            return order is null
                ? NotFoundResult<MassBalanceViewV1>()
                : new ManufacturingApiResult<MassBalanceViewV1>(ManufacturingApiStatus.Ok, MassBalanceView(order), false, null);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            var scope = await authority.RevalidatePermissionScopeAsync(
                ErpPermissions.ManufacturingOrdersRead, companyId, cancellationToken);
            if (!scope.IsGranted || !HasCapturedContext(companyId, actorId))
            {
                return Denied<MassBalanceViewV1>();
            }

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
            async (metadata, _, _) =>
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
        Func<ManufacturingCommandMetadata, Guid, Guid, Task<ManufacturingApiResult<T>>> write, Func<object, T> view,
        CancellationToken cancellationToken, string? storedAnswerPermission = null)
        where T : class
    {
        if (!HasContext() || ActorGuid() is not { } actor)
        {
            return Denied<T>();
        }

        var companyId = companyContext.CompanyId;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !HasCapturedContext(companyId, actor))
        {
            return Denied<T>();
        }

        var actorId = currentUser.UserId!;
        var keyHash = Sha256(idempotencyKey.Trim());
        var requestHash = Sha256(commandName + "|" + JsonSerializer.Serialize(canonicalRequest, Json));

        async Task<ManufacturingApiResult<T>> AnswerStoredAsync(ManufacturingCommandRecord stored)
        {
            // The immutable answer may be released only while the original command grant and captured request
            // identity still hold. Every ordinary and concurrent-winner replay passes through this helper.
            if (await FenceCapturedAsync<T>(permission, companyId, actor, cancellationToken) is { } fenced)
            {
                return fenced;
            }

            // A reasoned close is still a privileged exception when its immutable answer is replayed or won by a
            // concurrent attempt. Re-measure that dedicated grant after finding the record, against the company and
            // actor captured above, in addition to the original command grant measured above.
            if (storedAnswerPermission is not null)
            {
                if (string.IsNullOrWhiteSpace(storedAnswerPermission) || !HasCapturedContext(companyId, actor))
                {
                    return Denied<T>();
                }

                var storedAnswerScope = await authority.RevalidatePermissionScopeAsync(
                    storedAnswerPermission, companyId, cancellationToken);
                if (!storedAnswerScope.IsGranted || !storedAnswerScope.IsCompanyWide
                    || !HasCapturedContext(companyId, actor))
                {
                    return Denied<T>();
                }
            }

            return Answer<T>(stored, actorId, requestHash);
        }

        // §4/§3: a stored answer for this command and key, after the fence and before any other read.
        if (await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } prior)
        {
            return await AnswerStoredAsync(prior);
        }

        if (await FenceCapturedAsync<T>(permission, companyId, actor, cancellationToken) is { } preWriteFenced)
        {
            return preWriteFenced;
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
            result = await write(metadata, actor, companyId);
        }
        catch (ManufacturingInventoryRealizationPortException exception)
        {
            result = exception.Status == ManufacturingApiStatus.NotFound
                ? NotFoundResult<T>()
                : new ManufacturingApiResult<T>(exception.Status, null, false, exception.Code,
                    exception.Field, exception.ExpectedVersion, exception.CurrentVersion);
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
            if (await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } winner)
            {
                return await AnswerStoredAsync(winner);
            }

            if (await FenceCapturedAsync<T>(permission, companyId, actor, cancellationToken) is { } noWinnerFenced)
            {
                return noWinnerFenced;
            }

            return new ManufacturingApiResult<T>(ManufacturingApiStatus.Conflict, null, false, exception.Code);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.AuthorityDenied)
        {
            result = Denied<T>();
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
            || (result.Status == ManufacturingApiStatus.Conflict
                && result.Code == InventoryManufacturingRealizationCodesV1.LabelVersionConflict)
            || result.Status == successStatus;
        if (!terminal)
        {
            // §8, one exit whatever the refusal: another attempt of this key may have committed while
            // this one ran (its rule, version or read then saw the winner's state). Its record is the
            // answer; only without one does this refusal stand.
            if (result.Status is ManufacturingApiStatus.Invalid or ManufacturingApiStatus.Denied
                or ManufacturingApiStatus.Unavailable)
            {
                return result;
            }

            if (await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } winner)
            {
                return await AnswerStoredAsync(winner);
            }

            if (await FenceCapturedAsync<T>(permission, companyId, actor, cancellationToken) is { } refusalFenced)
            {
                return refusalFenced;
            }

            return result;
        }

        try
        {
            await commands.RecordCommandAsync(Record(commandName, keyHash, actorId, requestHash, result), cancellationToken);
            return result;
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            if (await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } winner)
            {
                return await AnswerStoredAsync(winner);
            }

            if (await FenceCapturedAsync<T>(permission, companyId, actor, cancellationToken) is { } terminalFenced)
            {
                return terminalFenced;
            }

            return result;
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

    /// <summary>A fresh fence for the exact company and actor captured before an external owner await.</summary>
    private async Task<ManufacturingApiResult<T>?> FenceCapturedAsync<T>(string permission, Guid companyId,
        Guid actorId, CancellationToken cancellationToken)
        where T : class
    {
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        return scope.IsGranted && scope.IsCompanyWide && HasCapturedContext(companyId, actorId)
            ? null
            : Denied<T>();
    }

    /// <summary>
    /// Final policy-close authority measured for the company and actor captured by <see cref="RunAsync{T}"/>.
    /// Each await is followed by the same-context check; an exception close must retain both grants.
    /// </summary>
    private async Task<bool> RevalidateClosePersistenceAuthorityAsync(Guid companyId, Guid actorId,
        string closePermission, string? exceptionApprovalPermission, CancellationToken cancellationToken)
    {
        if (!HasCapturedContext(companyId, actorId))
        {
            return false;
        }

        var closeScope = await authority.RevalidatePermissionScopeAsync(
            closePermission, companyId, cancellationToken);
        if (!closeScope.IsGranted || !closeScope.IsCompanyWide || !HasCapturedContext(companyId, actorId))
        {
            return false;
        }

        if (exceptionApprovalPermission is not null)
        {
            var exceptionScope = await authority.RevalidatePermissionScopeAsync(
                exceptionApprovalPermission, companyId, cancellationToken);
            if (!exceptionScope.IsGranted || !exceptionScope.IsCompanyWide
                || !HasCapturedContext(companyId, actorId))
            {
                return false;
            }
        }

        return true;
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
    /// Resolves the exact authorized Counterparty party/account after the command's stored-answer lookup. The
    /// caller's opaque versions are compared ordinally with the owner answer and then captured unchanged. A
    /// malformed positive owner answer fails closed; Manufacturing neither repairs nor reinterprets it.
    /// </summary>
    private async Task<(PlanCommercialContextSnapshotV1? Snapshot, ManufacturingApiResult<object>? Refusal)>
        ReadCommercialContextAsync(PlanCommercialContextRequestV1? requested, int inputIndex, string permission,
            Guid companyId, Guid actorId, CancellationToken cancellationToken)
    {
        if (requested is null)
        {
            return (null, null);
        }

        if (partyAccounts is null)
        {
            return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                CounterpartyProviderUnavailable));
        }

        CounterpartyManufacturingPartyAccountAnswerV1 answer;
        try
        {
            answer = await partyAccounts.GetAuthorizedAsync(
                requested.PartyId, requested.CompanyPartyAccountId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
            {
                return (null, fenced);
            }

            return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                CounterpartyProviderUnavailable));
        }

        if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } ownerFenced)
        {
            return (null, ownerFenced);
        }

        if (answer.Outcome == CounterpartyManufacturingPartyAccountOutcomeV1.Denied)
        {
            return (null, Denied<object>());
        }

        if (answer.Outcome == CounterpartyManufacturingPartyAccountOutcomeV1.NotFound)
        {
            return (null, NotFoundResult<object>());
        }

        if (answer.Outcome != CounterpartyManufacturingPartyAccountOutcomeV1.Found
            || answer.Account is not { } account
            || account.PartyId != requested.PartyId
            || account.CompanyAccountId != requested.CompanyPartyAccountId
            || account.CompanyId != companyId
            || string.IsNullOrEmpty(account.PartyVersion)
            || string.IsNullOrEmpty(account.AccountVersion))
        {
            return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                CounterpartyProviderUnavailable));
        }

        if (!string.Equals(account.PartyVersion, requested.PartyVersion, StringComparison.Ordinal))
        {
            return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Conflict, null, false,
                ManufacturingApiCodes.CommercialContextVersionConflict,
                $"inputs[{inputIndex}].commercialContext.partyVersion"));
        }

        if (!string.Equals(account.AccountVersion, requested.AccountVersion, StringComparison.Ordinal))
        {
            return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Conflict, null, false,
                ManufacturingApiCodes.CommercialContextVersionConflict,
                $"inputs[{inputIndex}].commercialContext.accountVersion"));
        }

        if (!account.PartyIsActive || !account.AccountIsActive
            || (!account.HasCustomerRole && !account.HasTollingRole))
        {
            return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unprocessable, null, false,
                ManufacturingApiCodes.CommercialContextIneligible,
                $"inputs[{inputIndex}].commercialContext"));
        }

        return (new PlanCommercialContextSnapshotV1(
            account.PartyId,
            account.CompanyAccountId,
            account.PartyVersion,
            account.AccountVersion), null);
    }

    /// <summary>
    /// Revalidates Inventory's exact label/version after this command's stored-answer lookup. Company-owned input
    /// keeps the existing validator. A commercial input uses the already-authorized party as owner and this company
    /// as custodian; Inventory still owns the exact stored position and draft-selectability decision.
    /// </summary>
    private async Task<(InventoryParentRollReferenceV1? Reference, ManufacturingApiResult<object>? Refusal)>
        ValidateParentRollAsync(Guid labelId,
        string expectedLabelVersion, string expectedVersionField, PlanCommercialContextSnapshotV1? commercialContext,
        string permission, Guid companyId, Guid actorId, CancellationToken cancellationToken)
    {
        if ((commercialContext is null && parentRolls is null)
            || (commercialContext is not null && contextualParentRolls is null))
        {
            return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                InventoryParentRollCodesV1.Unavailable));
        }

        try
        {
            var reference = commercialContext is null
                ? await parentRolls!.ValidateAsync(
                    new InventoryParentRollSelectionV1(labelId, expectedLabelVersion), cancellationToken)
                : await contextualParentRolls!.ValidateAsync(
                    new InventoryContextualParentRollSelectionV1(
                        labelId,
                        expectedLabelVersion,
                        new InventoryParentRollPositionContextV1(
                            StockPartyKind.Counterparty,
                            commercialContext.PartyId,
                            StockPartyKind.Company,
                            companyId)),
                    cancellationToken);
            if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
            {
                return (null, fenced);
            }

            return reference.CompanyId == companyId
                   && reference.LabelId == labelId
                   && string.Equals(reference.LabelVersion, expectedLabelVersion, StringComparison.Ordinal)
                   && reference.ProductId != Guid.Empty
                   && !string.IsNullOrWhiteSpace(reference.ProductVersion)
                   && reference.DraftSelectable
                   && reference.DraftRefusalCode is null
                   && reference.ProductionUseRequiresRevalidation
                ? (reference, null)
                : (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                    InventoryParentRollCodesV1.Unavailable));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InventoryParentRollException exception)
        {
            if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
            {
                return (null, fenced);
            }

            return (null, exception switch
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
            });
        }
        catch (Exception)
        {
            if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
            {
                return (null, fenced);
            }

            return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                InventoryParentRollCodesV1.Unavailable));
        }
    }

    /// <summary>
    /// Copies Product's positive answer for the exact Inventory-bound Product version. Product field
    /// errors are the existing product-ineligible 422 on this parent-roll selection; denials disclose
    /// no owner detail, and absent or malformed positive answers fail closed as unavailable.
    /// </summary>
    private async Task<(PlanTechnicalParameterSnapshotV1? Snapshot, ManufacturingApiResult<object>? Refusal)>
        ReadTechnicalSnapshotAsync(InventoryParentRollReferenceV1 reference, DateOnly effectiveOn, string field,
            string permission, Guid companyId, Guid actorId, CancellationToken cancellationToken)
    {
        if (productTechnicalSnapshots is null)
        {
            return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                ProductTechnicalSnapshotUnavailable));
        }

        try
        {
            var answer = await productTechnicalSnapshots.ReadAsync(
                new ProductManufacturingTechnicalSnapshotSelectionV1(
                    reference.ProductId, reference.ProductVersion, effectiveOn), cancellationToken);
            if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
            {
                return (null, fenced);
            }

            if (answer.Errors.Count > 0)
            {
                return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unprocessable, null, false,
                    InventoryOpeningCodesV1.ProductIneligible, field));
            }

            if (answer.Snapshot is not { } snapshot
                || snapshot.ProductId != reference.ProductId
                || !string.Equals(snapshot.ProductVersion, reference.ProductVersion, StringComparison.Ordinal)
                || snapshot.UnitKg.CompanyId != companyId
                || snapshot.EffectiveOn != effectiveOn)
            {
                return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                    ProductTechnicalSnapshotUnavailable));
            }

            return (new PlanTechnicalParameterSnapshotV1(
                snapshot.ProductId,
                snapshot.ProductVersion,
                snapshot.Family.FamilyId,
                snapshot.Family.FamilyCode,
                snapshot.Family.SourceRevision,
                snapshot.Template.TemplateId,
                snapshot.Template.TemplateVersion,
                snapshot.Template.TemplateCode,
                snapshot.Template.SourceRevision,
                snapshot.UnitKg.CompanyId,
                snapshot.UnitKg.UnitId,
                snapshot.UnitKg.UnitCode,
                snapshot.UnitKg.ProfileId,
                snapshot.UnitKg.ProfileVersion,
                snapshot.EffectiveOn,
                snapshot.ThicknessMm,
                snapshot.WidthMm), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProductOperationException exception) when (exception.StatusCode is 401 or 403)
        {
            if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
            {
                return (null, fenced);
            }

            return (null, Denied<object>());
        }
        catch (ProductOperationException exception) when (exception.StatusCode == 503)
        {
            if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
            {
                return (null, fenced);
            }

            return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                exception.Code));
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
            {
                return (null, fenced);
            }

            return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                exception.Code));
        }
        catch (Exception)
        {
            if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
            {
                return (null, fenced);
            }

            return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                ProductTechnicalSnapshotUnavailable));
        }
    }

    /// <summary>
    /// Resolves the selected Organization work center after the stored-answer lookup. Only the five exact
    /// Organization kind names locked by DEC-PRDCT-001 enter Manufacturing's persisted process contract.
    /// No client process, active-state policy, tolerance or default is introduced. Missing/foreign is the
    /// existing work-center refusal, while an absent, failed, unsupported or malformed owner source is unavailable.
    /// </summary>
    private async Task<(ManufacturingProcess Value, ManufacturingApiResult<object>? Refusal)> ResolveProcessAsync(
        Guid workCenterId,
        string permission, Guid companyId, Guid actorId, CancellationToken cancellationToken)
    {
        if (workCenters is null)
        {
            return (default, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false, null));
        }

        try
        {
            var reference = await workCenters.ReadAsync(companyId, workCenterId, cancellationToken);
            if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
            {
                return (default, fenced);
            }

            if (reference is null)
            {
                return (default, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unprocessable, null, false,
                    ManufacturingStoreCodes.WorkCenterNotFound));
            }

            var process = reference.WorkCenterId == workCenterId
                ? reference.Kind switch
                {
                    "Slitting" => ManufacturingProcess.Slitting,
                    "ColdRolling" => ManufacturingProcess.ColdRolling,
                    "TubeProfile" => ManufacturingProcess.TubeProfile,
                    "Paint" => ManufacturingProcess.Paint,
                    "CutToLength" => ManufacturingProcess.CutToLength,
                    _ => (ManufacturingProcess?)null,
                }
                : null;
            return process is { } value
                ? (value, null)
                : (default, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false, null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
            {
                return (default, fenced);
            }

            return (default, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false, null));
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

    private bool HasCapturedContext(Guid companyId, Guid actorId) =>
        HasContext() && companyContext.CompanyId == companyId && ActorGuid() == actorId;

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

    private static PlanStatus? ParsePlanStatus(string? text) =>
        Enum.TryParse<PlanStatus>(text, ignoreCase: false, out var status) && Enum.IsDefined(status) && IsName(text)
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
        [.. plan.Inputs.Select(input => new PlannedInputViewV1(input.ParentRollId, Text(input.PlannedMass.Value),
            TechnicalSnapshotView(input.TechnicalSnapshot), CommercialContextView(input.CommercialContext)))],
        plan.Target is { } target ? new TargetViewV1(target.Basis.ToString(), Text(target.Mass.Value)) : null,
        plan.ApprovedByUserId, plan.ApprovedAtUtc,
        PlanPreviewView(plan.WorkCenterId, ProductionPlanCalculatorV1.Calculate(plan,
            PlanCalculationParameters.FromCapturedSnapshots(plan.Inputs))));

    private static CapturedPlanTechnicalSnapshotViewV1? TechnicalSnapshotView(
        PlanTechnicalParameterSnapshotV1? snapshot) => snapshot is null ? null : new(
        snapshot.ProductId,
        snapshot.ProductVersion,
        new(snapshot.FamilyId, snapshot.FamilyCode, snapshot.FamilySourceRevision),
        new(snapshot.TemplateId, snapshot.TemplateVersion, snapshot.TemplateCode, snapshot.TemplateSourceRevision),
        Text(snapshot.ThicknessMm),
        Text(snapshot.WidthMm),
        new(snapshot.KilogramUnitCompanyId, snapshot.KilogramUnitId, snapshot.KilogramUnitCode,
            snapshot.KilogramProfileId, snapshot.KilogramProfileVersion),
        snapshot.EffectiveOn);

    private static CapturedPlanCommercialContextViewV1? CommercialContextView(
        PlanCommercialContextSnapshotV1? snapshot) => snapshot is null ? null : new(
        snapshot.PartyId,
        snapshot.CompanyPartyAccountId,
        snapshot.PartyVersion,
        snapshot.AccountVersion);

    /// <summary>MFG-PLAN-EDIT: one history entry as text, with the same lossless kilogram text as the plan read.</summary>
    internal static PlanRevisionViewV1 PlanRevisionView(ManufacturingPlanRevision revision) => new(
        revision.Revision, revision.PlanVersion, revision.WorkCenterId,
        [.. revision.Inputs.Select(input => new PlanRevisionInputViewV1(input.ParentRollId, Text(input.PlannedMass.Value),
            input.ExpectedLabelVersion?.ToString(CultureInfo.InvariantCulture),
            CommercialContextView(input.CommercialContext), TechnicalSnapshotView(input.TechnicalSnapshot)))],
        revision.Target is { } target ? new TargetViewV1(target.Basis.ToString(), Text(target.Mass.Value)) : null,
        revision.Reason, revision.RevisedBy, revision.RecordedAtUtc);

    /// <summary>The stored plan summary as text: the same lossless kilogram text as every other mass here.</summary>
    internal static PlanSummaryViewV1 PlanSummaryView(ManufacturingPlanSummary summary) => new(
        summary.PlanId, summary.WorkCenterId, summary.Process.ToString(), summary.Status.ToString(), summary.Version,
        summary.PlannedRollCount, Text(summary.PlannedInputMass.Value),
        summary is { TargetBasis: { } basis, TargetMass: { } targetMass }
            ? new TargetViewV1(basis.ToString(), Text(targetMass.Value))
            : null,
        summary.CreatedAtUtc, summary.ApprovedAtUtc);

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
                    Text(balance.WorkInProgress.Value), Text(balance.Unexplained), balance.IsFullyAccounted,
                    CommercialContextView(input.CommercialContext));
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
