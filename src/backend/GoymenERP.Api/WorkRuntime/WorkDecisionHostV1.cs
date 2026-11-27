using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

public sealed record WorkDecisionRequestV1(string ExpectedVersion, bool Approve, string? Reason, Guid? DelegationId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkManufacturingPlanReapprovalSelectionV1? ManufacturingPlanReapproval = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkInventoryTransferLossSelectionV1? InventoryTransferLoss = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkLogisticsPlanExcessSelectionV1? LogisticsPlanExcess = null);
public sealed record WorkLogisticsPlanExcessSelectionV1(string ExpectedInstructionVersion);
public sealed record WorkInventoryTransferLossSelectionV1(int RequestNumber, DateTimeOffset DecidedAtUtc);
/// <summary>The human's explicit selection. Only the Manufacturing reapproval command accepts it.</summary>
public sealed record WorkManufacturingPlanReapprovalSelectionV1(IReadOnlyList<Guid>? SelectedLabelIds = null);
public sealed record WorkDecisionPreviewV1(Guid CompanyId, string Kind, Guid ItemId, string DocumentLabel,
    string SourceVersion, bool Pending, bool ApprovalReasonRequired, IReadOnlyList<WorkApprovalReferenceV1> References,
    IReadOnlyList<WorkApprovalRuleResultV1> Rules, bool RejectionSupported = true,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkManufacturingPlanReapprovalOptionsV1? ManufacturingPlanReapproval = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkInventoryTransferLossOptionsV1? InventoryTransferLoss = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkCounterpartyTaxIdentityChangePreviewV1? CounterpartyTaxIdentityChange = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkCounterpartyLegalNameChangePreviewV1? CounterpartyLegalNameChange = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkLogisticsPlanExcessPreviewV1? LogisticsPlanExcess = null);
public sealed record WorkDecisionResultV1(Guid CompanyId, string Kind, Guid ItemId, string State, bool Replayed, string OriginalCorrelationId);
public sealed class WorkDecisionException(int status, string? code = null, bool replayed = false, string? original = null) : Exception(code)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
    public bool Replayed { get; } = replayed;
    public string? OriginalCorrelationId { get; } = original;
}

/// <summary>Request-local additional fence, never a grant. Re-entrant Identity reads still use the real authority.
/// AsyncLocal confines the recursion guard to the current verification flow, not unrelated concurrent callers.</summary>
public sealed class WorkDecisionExecutionFenceV1
{
    private Func<CancellationToken, Task<bool>>? check;
    private readonly AsyncLocal<int> depth = new();
    public IDisposable Enter(Func<CancellationToken, Task<bool>> verify)
    {
        if (Interlocked.CompareExchange(ref check, verify, null) is not null) throw new WorkDecisionException(503);
        return new Lease(() => Interlocked.Exchange(ref check, null));
    }
    public async Task<bool> VerifyAsync(CancellationToken token)
    {
        if (check is not { } verify || depth.Value != 0) return true;
        depth.Value++;
        try { return await verify(token); }
        finally { depth.Value--; }
    }
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? pending = release;
        public void Dispose() => Interlocked.Exchange(ref pending, null)?.Invoke();
    }
}

public sealed class WorkDecisionAuthorityV1(IErpAuthorityRevalidationService inner, WorkDecisionExecutionFenceV1 fence)
    : IErpAuthorityRevalidationService
{
    public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
    {
        var scope = await inner.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        return scope.IsGranted && !await fence.VerifyAsync(cancellationToken) ? PermissionScope.Denied : scope;
    }
}

public static class WorkDecisionRegistrationV1
{
    public static IServiceCollection AddWorkDecisionHostV1(this IServiceCollection services)
    {
        // Decorate the existing lifecycle-aware authority factory; no second authorization engine or cache.
        var original = services.Last(item => item.ServiceType == typeof(IErpAuthorityRevalidationService));
        if (original.Lifetime != ServiceLifetime.Scoped || original.ImplementationFactory is null)
            throw new InvalidOperationException("The lifecycle-aware scoped authority factory is required.");
        services.Remove(original);
        services.AddScoped<WorkDecisionExecutionFenceV1>();
        services.AddScoped<IErpAuthorityRevalidationService>(provider => new WorkDecisionAuthorityV1(
            (IErpAuthorityRevalidationService)original.ImplementationFactory(provider), provider.GetRequiredService<WorkDecisionExecutionFenceV1>()));
        services.AddScoped<WorkDecisionHostV1>();
        return services;
    }
}

/// <summary>WP8 host composition only. Owner commands retain all writes, receipts, versions, maker checks and audit.
/// Four YOL-D41 screenless kinds first; an unregistered kind never becomes an arbitrary command/route.</summary>
public sealed partial class WorkDecisionHostV1(ICompanyContext company, ICurrentUser user, ICorrelationContext correlation,
    IErpAuthorityRevalidationService authority, IEnumerable<IWorkInboxSourceV1> sources,
    IWorkDelegationReaderV1 delegations, WorkDecisionExecutionFenceV1 executionFence,
    IFinanceCashAccountMaster cash, IInventoryOpeningCostCommandV1 openingCost,
    ISalesDraftService? salesOrders = null, ISalesInvoiceService? salesInvoices = null, IPurchasingService? purchasing = null, ISalesSubcontractInvoiceLifecycleServiceV1? subcontractInvoices = null, IFinanceCreditAccountService? credits = null, IPurchaseServiceOrderServiceV1? purchasingServiceOrders = null, IPurchaseIncomingInvoiceWorkflowV1? purchaseInvoiceWorkflow = null, IPurchaseGoodsReceiptBatchServiceV1? goodsReceiptBatches = null, ManufacturingCommandService? manufacturingPlans = null, IInventoryTransferReceiptCommandV1? inventoryTransferReceipts = null, ICounterpartyTaxIdentityService? counterpartyTaxIdentity = null, ICounterpartyLegalNameService? counterpartyLegalName = null, ILogisticsPlanExcessServiceV1? logisticsPlanExcess = null)
{
    public const string LogisticsPlanExcess = "logistics.plan-excess.v1";
    public const string CounterpartyTaxIdentity = "counterparty.tax-identity-change.v1";
    public const string CounterpartyLegalName = "counterparty.legal-name-change.v1";
    public const string ManufacturingPlan = "manufacturing.plan.v1";
    public const string InventoryTransferLoss = "inventory.transfer-loss.v1";
    public const string OpeningCost = "inventory.opening-cost.v1";
    public const string SalesOrder = "sales.order-draft.v1";
    public const string SalesInvoice = "sales.invoice.v1";
    public const string SalesSubcontractInvoice = "sales.subcontract-invoice.v1";
    public const string CreditAccount = "finance.credit-account.v1";
    public const string PurchaseGoodsReceiptBatch = "purchasing.goods-receipt-batch.v1";
    public const string PurchaseRequest = "purchasing.request.v1";
    public const string PurchaseInvoiceVariance = "purchasing.incoming-invoice-variance.v1";
    public const string PurchaseServiceOrder = "purchasing.service-order.v1";
    public const string PurchaseOrder = "purchasing.order.v1";
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>([LogisticsPlanExcess, CounterpartyTaxIdentity, CounterpartyLegalName, InventoryTransferLoss, OpeningCost, "finance.cash-account.v1",
        "finance.cash-account-passivation.v1", "finance.cash-account-reactivation.v1", SalesOrder, SalesInvoice, SalesSubcontractInvoice, PurchaseRequest, PurchaseOrder, PurchaseServiceOrder, PurchaseInvoiceVariance, PurchaseGoodsReceiptBatch, ManufacturingPlan, CreditAccount], StringComparer.Ordinal);

    private async Task RequireDecisionAuthorityAsync(string kind,string permission,Guid companyId,string actor,CancellationToken token)
    {
        foreach(var required in kind is CounterpartyTaxIdentity or CounterpartyLegalName ? new[]{kind == CounterpartyTaxIdentity ? CounterpartyTaxIdentityPermissionsV1.Approve : CounterpartyLegalNamePermissionsV1.Approve, ErpPermissions.CounterpartyPartiesRead}
            : kind == PurchaseInvoiceVariance ? new[]{PurchasingPermissionManifestV1.OrdersPriceRead,permission}
            : (kind is SalesInvoice or SalesSubcontractInvoice)?new[]{permission,SalesPermissionManifestV1.OrdersPriceRead}:new[]{permission})
        {
            var scope=await authority.RevalidatePermissionScopeAsync(required,companyId,token);
            if(!scope.IsGranted||!scope.IsCompanyWide||!user.IsAuthenticated||user.UserId!=actor
                ||!company.IsResolved||company.CompanyId!=companyId||!user.CanAccessCompany(companyId))throw new WorkDecisionException(403);
        }
    }

    private async Task<WorkApprovalDecisionSnapshotV1> SnapshotAsync(string kind, Guid id, CancellationToken token, int? selectedLossRequestNumber = null)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw new WorkDecisionException(401);
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw new WorkDecisionException(403);
        if (!correlation.IsResolved) throw new WorkDecisionException(503);
        if (!Kinds.Contains(kind) || id == Guid.Empty) throw new WorkDecisionException(404);
        var matches = sources.Where(source => source.Kind == kind).ToArray();
        if (matches.Length != 1 || matches[0] is not IWorkApprovalDecisionSourceV1 source
            || !ErpPermissions.IsKnown(source.ApproverPermission)) throw new WorkDecisionException(503);
        var companyId = company.CompanyId; var actor = user.UserId;
        async Task Fence()
        {
            await RequireDecisionAuthorityAsync(kind,source.ApproverPermission,companyId,actor,token);
        }
        await Fence();
        var context = new WorkInboxSourceContextV1(companyId, actor, correlation.CorrelationId, correlation.CausationId);
        WorkApprovalDecisionSnapshotV1? snapshot;
        try { snapshot = await source.ReadDecisionAsync(context, id, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(); throw new WorkDecisionException(503); }
        await Fence();
        if (snapshot is null) throw new WorkDecisionException(404);
        if (kind is (CounterpartyTaxIdentity or CounterpartyLegalName) && source.Module != "counterparty"
            || kind == LogisticsPlanExcess && (source.Module != "logistics" || source.ApproverPermission != LogisticsPlanExcessV1.ApprovePermission)
            || kind == CounterpartyTaxIdentity && source.ApproverPermission != CounterpartyTaxIdentityPermissionsV1.Approve
            || kind == CounterpartyLegalName && source.ApproverPermission != CounterpartyLegalNamePermissionsV1.Approve)
            throw new WorkDecisionException(503);
        if (snapshot.Item is null || snapshot.CompanyId != companyId || snapshot.Item.ItemId != id || snapshot.Item.Kind != kind
            || snapshot.Item.Module != source.Module || string.IsNullOrWhiteSpace(snapshot.SourceVersion)
            || (string.IsNullOrWhiteSpace(snapshot.Item.PreparedBy)
                && !(kind == ManufacturingPlan && snapshot.Item.Module == "manufacturing"
                    && source.ApproverPermission == ManufacturingPermissionManifestV1.PlansApprove && snapshot.Item.PreparedBy is null))
            || string.IsNullOrWhiteSpace(snapshot.Item.DocumentLabel)
            || snapshot.Item.References is null || snapshot.Item.References.Count > WorkInboxRulesV1.MaxReferencesPerItem
            || snapshot.Item.References.Any(item => !WorkInboxRulesV1.IsValidReference(item))
            || snapshot.Item.Rules is null || snapshot.Item.Rules.Count > WorkInboxRulesV1.MaxRulesPerItem
            || snapshot.Item.Rules.Any(rule => !WorkInboxRulesV1.IsValidRule(source.Module, rule))
            || !ValidCounterpartyIdentityProof(kind, snapshot)
            || !ValidLogisticsPlanExcessProof(kind, snapshot)
            || (kind == InventoryTransferLoss ? !ValidInventoryTransferLossProof(snapshot)
                : snapshot.InventoryTransferLoss is not null || snapshot.InventoryTransferLossProof is not null)
            || kind == ManufacturingPlan && snapshot.Pending
                && snapshot.Item.Rules.Any(rule => rule.RuleCode == "manufacturing.plan-reapproval-after-order-cancel")
                && snapshot.ManufacturingPlanReapproval is null
            || snapshot.ManufacturingPlanReapproval is { } options && (kind != ManufacturingPlan || !snapshot.Pending
                || !snapshot.Item.Rules.Any(rule => rule.RuleCode == "manufacturing.plan-reapproval-after-order-cancel")
                || options.ParentRollIds is not { Count: > 0 } || options.ParentRollIds.Contains(Guid.Empty)
                || options.ParentRollIds.Distinct().Count() != options.ParentRollIds.Count))
            throw new WorkDecisionException(503);
        if (snapshot.Item.Excludes(actor)
            && !(kind == InventoryTransferLoss && MayReachHistoricalLossReceipt(snapshot, selectedLossRequestNumber, actor)))
            throw new WorkDecisionException(403);
        return snapshot;
    }

    public async Task<WorkDecisionPreviewV1> PreviewAsync(string kind, Guid id, CancellationToken token)
    {
        var actor = user.UserId;
        var snapshot = await SnapshotAsync(kind, id, token);
        var rules = snapshot.Item.Rules;
        if (kind == OpeningCost && rules.Any(rule => rule.RuleCode == "inventory.opening-cost-unit-cost"))
        {
            var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.InventoryAverageCostRead, snapshot.CompanyId, token);
            if (!scope.IsGranted || !scope.IsCompanyWide) rules = rules.Where(rule => rule.RuleCode != "inventory.opening-cost-unit-cost").ToArray();
            if (!user.IsAuthenticated || user.UserId != actor || !company.IsResolved || company.CompanyId != snapshot.CompanyId || !user.CanAccessCompany(snapshot.CompanyId))
                throw new WorkDecisionException(403);
        }
        await RequireDecisionAuthorityAsync(kind,sources.Single(source => source.Kind == kind).ApproverPermission,snapshot.CompanyId,actor!,token);
        // SnapshotAsync validated the immutable binding; only this authorized human GET exposes the proposal.
        var logisticsPreview = snapshot.LogisticsPlanExcessProof is { } proposal && snapshot.LogisticsPlanExcess is { } logisticsOptions
            ? new WorkLogisticsPlanExcessPreviewV1(logisticsOptions.InstructionVersion, proposal.InstructionId, proposal.LineId,
                proposal.Quantity, proposal.Reason, proposal.ProposedBy, proposal.ProposedAtUtc)
            : null;
        return new(snapshot.CompanyId, kind, id, snapshot.Item.DocumentLabel, snapshot.SourceVersion, snapshot.Pending,
            kind is OpeningCost or PurchaseServiceOrder or PurchaseInvoiceVariance or InventoryTransferLoss, snapshot.Item.References, rules, kind is not (PurchaseOrder or PurchaseServiceOrder or PurchaseInvoiceVariance or LogisticsPlanExcess)
                && !(kind == ManufacturingPlan && rules.Any(rule => rule.RuleCode == "manufacturing.plan-reapproval-after-order-cancel")),
            snapshot.ManufacturingPlanReapproval, snapshot.InventoryTransferLoss,
            snapshot.CounterpartyTaxIdentityChange, snapshot.CounterpartyLegalNameChange, logisticsPreview);
    }

    public async Task<WorkDecisionResultV1> DecideAsync(string kind, Guid id, WorkDecisionRequestV1 request, string key, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = user.UserId;
        var snapshot = await SnapshotAsync(kind, id, token, request.InventoryTransferLoss?.RequestNumber);
        if (user.UserId != actor) throw new WorkDecisionException(403);
        if (request.LogisticsPlanExcess is not null && kind != LogisticsPlanExcess
            || kind == LogisticsPlanExcess && (request.LogisticsPlanExcess is null || !request.Approve || request.Reason is not null
                || !PositiveLogisticsPlanVersion(request.ExpectedVersion) || !PositiveLogisticsPlanVersion(request.LogisticsPlanExcess.ExpectedInstructionVersion))
            || request.InventoryTransferLoss is not null && kind != InventoryTransferLoss
            || kind == InventoryTransferLoss && (request.InventoryTransferLoss is null || request.ManufacturingPlanReapproval is not null)
            || request.ManufacturingPlanReapproval is not null && (kind != ManufacturingPlan || !request.Approve)
            || kind is (PurchaseOrder or PurchaseServiceOrder or PurchaseInvoiceVariance) && !request.Approve
            || string.IsNullOrWhiteSpace(request.ExpectedVersion) || request.ExpectedVersion.Length > 128
            || (kind == OpeningCost && request.ExpectedVersion != snapshot.SourceVersion)
            || ((kind is OpeningCost or InventoryTransferLoss || !request.Approve && kind is not (SalesOrder or SalesInvoice or SalesSubcontractInvoice or PurchaseRequest or PurchaseGoodsReceiptBatch or ManufacturingPlan or CounterpartyTaxIdentity or CounterpartyLegalName)) && (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500))
            || (kind is not (OpeningCost or PurchaseServiceOrder or PurchaseInvoiceVariance or PurchaseGoodsReceiptBatch or InventoryTransferLoss or CounterpartyTaxIdentity or CounterpartyLegalName) && request.Approve && request.Reason is not null)) throw new WorkDecisionException(400, "work_decision_invalid");
        if (kind is CounterpartyTaxIdentity or CounterpartyLegalName && request.Reason?.Trim().Length > 1000)
            throw new WorkDecisionException(400, "work_decision_invalid");
        // A retained receipt targets the old request's maker, not the maker of a later request
        // on the same hashed inbox item. All other item scope/exclusions and live delegation checks remain unchanged.
        var verificationItem = kind == InventoryTransferLoss && MayReachHistoricalLossReceipt(snapshot, request.InventoryTransferLoss?.RequestNumber, actor!)
            ? snapshot.Item with { PreparedBy = snapshot.InventoryTransferLossProof!.Requests.Single(row => row.RequestNumber == request.InventoryTransferLoss!.RequestNumber).PreparedBy }
            : snapshot.Item;
        async Task<bool> Verify(CancellationToken ct) => request.DelegationId is not { } delegation
            || await delegations.FindForActorAsync(delegation, verificationItem, ct) is not null;
        if (!await Verify(token)) throw new WorkDecisionException(403);
        using var lease = executionFence.Enter(Verify);
        if (kind == LogisticsPlanExcess)
            return await DecideLogisticsPlanExcessAsync(id, request, key, snapshot, actor!, token);
        if (kind is CounterpartyTaxIdentity or CounterpartyLegalName)
            return await DecideCounterpartyIdentityAsync(kind, id, request, key, snapshot, actor!, token);
        if (kind == InventoryTransferLoss)
            return await DecideInventoryTransferLossAsync(id, request, key, snapshot, actor, token);
        if (kind == ManufacturingPlan)
            return await DecideManufacturingPlanAsync(id, request, key, snapshot, actor, token);
        if (kind == PurchaseGoodsReceiptBatch)
        {
            if (goodsReceiptBatches is null) throw new WorkDecisionException(503);
            // The native batch command owns both identities. Never accept an order supplied by the caller
            // or infer it from a URL: only the company-bound owner's snapshot supplies this reference.
            var orders = snapshot.Item.References.Where(reference => reference.Role == "purchase-order").ToArray();
            if (orders.Length != 1 || orders[0].RecordId is not { } orderId || orderId == Guid.Empty)
                throw new WorkDecisionException(503);
            try
            {
                var body = new DecidePurchaseGoodsReceiptBatchV1(request.ExpectedVersion, request.Reason);
                var batchMetadata = new PurchasingCommandMetadata(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);
                var answer = request.Approve
                    ? await goodsReceiptBatches.ApproveGoodsReceiptBatchAsync(orderId, id, body, batchMetadata, token)
                    : await goodsReceiptBatches.RejectGoodsReceiptBatchAsync(orderId, id, body, batchMetadata, token);
                // Posted/Rejected decisions are immutable native outcomes, including a success replay.
                // A pending, foreign, contradictory or malformed owner answer is never Work success.
                if (answer?.Batch is not { } batch || batch.CompanyId != snapshot.CompanyId || batch.BatchId != id
                    || batch.PurchaseOrderId != orderId || batch.Status != (request.Approve ? "Posted" : "Rejected")
                    || !long.TryParse(batch.Version, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version)
                    || version < 2 || version.ToString(System.Globalization.CultureInfo.InvariantCulture) != batch.Version
                    || !long.TryParse(request.ExpectedVersion, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var expected)
                    || expected < 1 || expected == long.MaxValue || version != expected + 1
                    || batch.DecidedBy != actor || batch.DecidedAtUtc is null || batch.DecidedAtUtc == default(DateTimeOffset)
                    || batch.DecidedAtUtc.Value.Offset != TimeSpan.Zero || batch.PreparedBy == actor
                    || string.IsNullOrWhiteSpace(batch.PreparedBy) || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId)
                    || (request.Approve ? batch.DecisionReason is not null : string.IsNullOrWhiteSpace(batch.DecisionReason)
                        || !string.Equals(batch.DecisionReason, request.Reason?.Trim(), StringComparison.Ordinal)))
                    throw new WorkDecisionException(503);
                return new(snapshot.CompanyId, kind, id, batch.Status, answer.IsReplay, answer.OriginalCorrelationId);
            }
            catch (PurchasingServiceException error)
            { throw new WorkDecisionException(error.Status, error.Code, error.IsReplay, error.OriginalCorrelationId); }
        }
        if (kind == PurchaseInvoiceVariance)
        {
            if (purchaseInvoiceWorkflow is null) throw new WorkDecisionException(503);
            try
            {
                var answer = await purchaseInvoiceWorkflow.ApproveVarianceAsync(id, new(request.ExpectedVersion, request.Reason!),
                    new(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow), token);
                if (answer.Invoice.Id != id || answer.Invoice.CompanyId != snapshot.CompanyId
                    || answer.Invoice.Status != GoymenERP.Modules.Purchasing.Domain.Invoices.PurchaseIncomingInvoiceStatus.Matched
                    || answer.Invoice.VarianceDecision is null) throw new WorkDecisionException(503);
                return new(snapshot.CompanyId, kind, id, answer.Invoice.Status.ToString(), answer.IsReplay, answer.OriginalCorrelationId);
            }
            catch (PurchasingServiceException error)
            { throw new WorkDecisionException(error.Status, error.Code, error.IsReplay, error.OriginalCorrelationId); }
        }
        if (kind == PurchaseServiceOrder)
        {
            if (purchasingServiceOrders is null) throw new WorkDecisionException(503);
            try
            {
                var answer = await purchasingServiceOrders.ApproveAsync(id,
                    new(request.ExpectedVersion, request.Reason!),
                    new(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow), token);
                if (answer.Order.OrderId != id || answer.Order.CompanyId != snapshot.CompanyId) throw new WorkDecisionException(503);
                return new(snapshot.CompanyId, kind, id, answer.Order.Status, answer.IsReplay, answer.OriginalCorrelationId);
            }
            catch (PurchasingServiceException error)
            { throw new WorkDecisionException(error.Status, error.Code, error.IsReplay, error.OriginalCorrelationId); }
        }
        if (kind is PurchaseRequest or PurchaseOrder)
        {
            if (purchasing is null) throw new WorkDecisionException(503);
            var purchasingMetadata = new PurchasingCommandMetadata(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);
            try
            {
                if (kind == PurchaseOrder)
                {
                    // The owner has approval, not rejection. Cancellation remains a separate native workflow.
                    var order = await purchasing.ApproveOrderAsync(id, new(request.ExpectedVersion), purchasingMetadata, token);
                    if (order.Detail.Order.Id != id || order.Detail.Order.CompanyId != snapshot.CompanyId) throw new WorkDecisionException(503);
                    return new(snapshot.CompanyId, kind, id, order.Detail.Order.Status, order.IsReplay, order.OriginalCorrelationId);
                }
                // Native validation retains its 4000-character reason limit, terminal state/version refusals,
                // maker/submitter exclusion and original receipt. Never reinterpret cancellation as rejection.
                var answer = request.Approve
                    ? await purchasing.ApproveRequestAsync(id, new(request.ExpectedVersion), purchasingMetadata, token)
                    : await purchasing.RejectRequestAsync(id, new(request.ExpectedVersion, request.Reason!), purchasingMetadata, token);
                if (answer.Detail.Request.Id != id || answer.Detail.Request.CompanyId != snapshot.CompanyId) throw new WorkDecisionException(503);
                return new(snapshot.CompanyId, kind, id, answer.Detail.Request.Status, answer.IsReplay, answer.OriginalCorrelationId);
            }
            catch (PurchasingServiceException error)
            { throw new WorkDecisionException(error.Status, error.Code, error.IsReplay, error.OriginalCorrelationId); }
        }
        if(kind is SalesOrder or SalesInvoice or SalesSubcontractInvoice)
        {
            var salesMetadata=new SalesCommandMetadata(key,correlation.CorrelationId,correlation.CausationId,DateTimeOffset.UtcNow);
            var body=new SalesDraftStateCommandRequest(request.ExpectedVersion,request.Reason);
            try
            {
                if(kind==SalesOrder)
                {
                    if(salesOrders is null)throw new WorkDecisionException(503);
                    var answer=request.Approve?await salesOrders.ApproveDraftAsync(id,body,salesMetadata,token):await salesOrders.RejectDraftAsync(id,body,salesMetadata,token);
                    if(answer.Detail.Draft.Id!=id||answer.Detail.Draft.CompanyId!=snapshot.CompanyId)throw new WorkDecisionException(503);
                    return new(snapshot.CompanyId,kind,id,answer.Detail.Draft.ApprovalState,answer.IsReplay,answer.OriginalCorrelationId);
                }
                if(kind==SalesSubcontractInvoice)
                {
                    if(subcontractInvoices is null)throw new WorkDecisionException(503);
                    var answer=request.Approve?await subcontractInvoices.ApproveSubcontractInvoiceAsync(id,body,salesMetadata,token)
                        :await subcontractInvoices.RejectSubcontractInvoiceAsync(id,body,salesMetadata,token);
                    if(answer.Invoice.Id!=id)throw new WorkDecisionException(503);
                    return new(snapshot.CompanyId,kind,id,answer.Invoice.State,answer.IsReplay,answer.OriginalCorrelationId);
                }
                if(salesInvoices is null)throw new WorkDecisionException(503);
                var invoice=request.Approve?await salesInvoices.ApproveInvoiceAsync(id,body,salesMetadata,token):await salesInvoices.RejectInvoiceAsync(id,body,salesMetadata,token);
                if(invoice.Invoice.Id!=id)throw new WorkDecisionException(503);
                return new(snapshot.CompanyId,kind,id,invoice.Invoice.State,invoice.IsReplay,invoice.OriginalCorrelationId);
            }
            catch(SalesDraftServiceException error){throw new WorkDecisionException(error.Status,error.Code,error.IsReplay,error.OriginalCorrelationId);}
        }
        if (kind == OpeningCost)
        {
            var value = await openingCost.DecideAsync(id,
                new(request.Approve ? InventoryOpeningCostDecisionKindV1.Approve : InventoryOpeningCostDecisionKindV1.Reject, request.Reason!),
                new(actor!, "", [], correlation.CorrelationId, correlation.CausationId, key, correlation.SourceChannel,
                    correlation.ClientIpAddress, correlation.UserAgent), token);
            if (value.OpeningCost.CompanyId != snapshot.CompanyId || value.OpeningCost.OpeningCostId != id) throw new WorkDecisionException(503);
            return new(snapshot.CompanyId, kind, id, value.OpeningCost.Status, value.IsReplay, value.OriginalCorrelationId);
        }
        var context = new FinanceContext(snapshot.CompanyId, actor!, correlation.CorrelationId, correlation.CausationId);
        var metadata = new CommandMetadata(key);
        if (kind == CreditAccount)
        {
            if (credits is null) throw new WorkDecisionException(503);
            var answer = request.Approve
                ? await credits.ApproveAsync(context, metadata, new(id, request.ExpectedVersion), token)
                : await credits.RejectAsync(context, metadata, new(id, request.ExpectedVersion, request.Reason!), token);
            if (answer.Outcome != CommandOutcome.Succeeded)
                throw new WorkDecisionException(answer.Outcome switch { CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                    CommandOutcome.VersionConflict or CommandOutcome.IdempotencyConflict => 409, _ => 422 },
                    answer.Issues.FirstOrDefault()?.Code, answer.IsReplay, answer.OriginalCorrelationId);
            if (answer.Resource is not { } credit || credit.CompanyId != snapshot.CompanyId || credit.Id != id) throw new WorkDecisionException(503);
            return new(snapshot.CompanyId, kind, id, credit.Status.ToString(), answer.IsReplay, answer.OriginalCorrelationId);
        }
        CashAccountCommandResult result;
        if (kind == "finance.cash-account.v1")
            result = request.Approve ? await cash.ApproveAsync(context, metadata, new(id, request.ExpectedVersion), token)
                : await cash.RejectAsync(context, metadata, new(id, request.ExpectedVersion, request.Reason!), token);
        else
        {
            var change = kind == "finance.cash-account-passivation.v1" ? CashAccountStateChange.Passivation : CashAccountStateChange.Reactivation;
            result = request.Approve ? await cash.ApproveStateChangeAsync(context, metadata, change, new(id, request.ExpectedVersion), token)
                : await cash.RejectStateChangeAsync(context, metadata, change, new(id, request.ExpectedVersion, request.Reason!), token);
        }
        if (result.Outcome != CommandOutcome.Succeeded)
            throw new WorkDecisionException(result.Outcome switch { CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                CommandOutcome.VersionConflict or CommandOutcome.IdempotencyConflict => 409, _ => 422 },
                result.Issues.FirstOrDefault()?.Code, result.IsReplay, result.OriginalCorrelationId);
        if (result.Resource is not { } account || account.CompanyId != snapshot.CompanyId || account.Id != id) throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, kind, id, account.Status.ToString(), result.IsReplay, result.OriginalCorrelationId);
    }
}
