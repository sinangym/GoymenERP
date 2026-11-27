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
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

public sealed record WorkDecisionRequestV1(string ExpectedVersion, bool Approve, string? Reason, Guid? DelegationId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkManufacturingPlanReapprovalSelectionV1? ManufacturingPlanReapproval = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkInventoryTransferLossSelectionV1? InventoryTransferLoss = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkLogisticsPlanExcessSelectionV1? LogisticsPlanExcess = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkInventoryTransferOverageSelectionV1? InventoryTransferOverage = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkSupplierReturnCostExceptionSelectionV1? SupplierReturnCostException = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkLogisticsCustomerReturnSelectionV1? LogisticsCustomerReturn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkProductPackageStandardSelectionV1? ProductPackageStandard = null);
public sealed record WorkProductPackageStandardSelectionV1(string EffectiveFrom);
public sealed record WorkLogisticsCustomerReturnSelectionV1(Guid InventoryMovementId);
public sealed record WorkSupplierReturnCostExceptionSelectionV1([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpectedPurchaseOrderVersion = null);
public sealed record WorkInventoryTransferOverageSelectionV1(int RequestNumber, DateTimeOffset DecidedAtUtc);
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
    WorkLogisticsPlanExcessPreviewV1? LogisticsPlanExcess = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkInventoryTransferOveragePreviewV1? InventoryTransferOverage = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkLogisticsWeighingDifferencePreviewV1? LogisticsWeighingDifference = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkSupplierReturnCostExceptionPreviewV1? SupplierReturnCostException = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkInventoryStockCountPreviewV1? InventoryStockCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkBankAccountPreviewV1? BankAccount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkInventoryStockScrapPreviewV1? InventoryStockScrap = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkLogisticsDispatchNotePreviewV1? LogisticsDispatchNote = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkLogisticsCustomerReturnPreviewV1? LogisticsCustomerReturn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ApprovalSupported = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkBankAccountIbanChangePreviewV1? BankAccountIbanChange = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkProductPackageStandardPreviewV1? ProductPackageStandard = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkProductPriceListPreviewV1? ProductPriceList = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkPartyCreditLimitPreviewV1? PartyCreditLimit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FinanceOfficialInvoiceImportPendingBatchDetailV1? OfficialInvoiceImport = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FinanceOfficialSalesInvoiceViewV1? OfficialSalesInvoice = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PaymentReversalDecisionPreviewV1? ManualBankPaymentReversal = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReceiptReversalDecisionDetailV1? ManualReceiptReversal = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ExpensePaymentDecisionDetailV1? ExpensePayment = null);
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
    ISalesDraftService? salesOrders = null, ISalesInvoiceService? salesInvoices = null, IPurchasingService? purchasing = null, ISalesSubcontractInvoiceLifecycleServiceV1? subcontractInvoices = null, IFinanceCreditAccountService? credits = null, IPurchaseServiceOrderServiceV1? purchasingServiceOrders = null, IPurchaseIncomingInvoiceWorkflowV1? purchaseInvoiceWorkflow = null, IPurchaseGoodsReceiptBatchServiceV1? goodsReceiptBatches = null, ManufacturingCommandService? manufacturingPlans = null, IInventoryTransferReceiptCommandV1? inventoryTransferReceipts = null, ICounterpartyTaxIdentityService? counterpartyTaxIdentity = null, ICounterpartyLegalNameService? counterpartyLegalName = null, ILogisticsPlanExcessServiceV1? logisticsPlanExcess = null, ILogisticsWeighingDifferenceServiceV1? logisticsWeighingDifferences = null, IPurchaseSupplierReturnServiceV1? supplierReturns = null, IInventoryStockCountCommandV1? stockCounts = null, IFinanceBankAccountMaster? bankAccounts = null, IInventoryStockScrapCommandV1? stockScraps = null, ILogisticsInstructionService? logisticsInstructions = null, ILogisticsCustomerReturnServiceV1? logisticsCustomerReturns = null, IManualBankPaymentService? manualBankPayments = null, GoymenERP.Modules.Organization.Application.Contracts.IOrganizationService? bankAccountCompanies = null, IProductPackageStandardServiceV3? productPackageStandards = null, IProductPriceListServiceV1? productPriceLists = null, IFinancePartyCreditLimitMaster? partyCreditLimits = null, IFinanceOfficialInvoiceImportApprovalServiceV1? officialInvoiceImports = null, IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1? officialInvoiceImportReads = null, IFinanceOfficialSalesInvoiceServiceV1? officialSalesInvoices = null, IPaymentReversalProposalService? manualBankPaymentReversals = null, IReversalProposalService? manualReceiptReversals = null, IExpensePaymentService? expensePayments = null)
{
    public const string ExpensePayment = "finance.expense-payment.v1";
    public const string ManualBankPaymentReversal = "finance.manual-bank-payment-reversal.v1";
    public const string ManualReceiptReversal = "finance.manual-receipt-reversal.v1";
    public const string ManualBankPayment = "finance.manual-bank-payment.v1";
    public const string LogisticsCustomerReturn = "logistics.customer-return.v1";
    public const string LogisticsDispatchNote = "logistics.dispatch-note.v1";
    public const string InventoryStockScrap = "inventory.stock-scrap.v1";
    public const string InventoryStockCount = "inventory.stock-count.v1";
    public const string BankAccount = "finance.bank-account.v1";
    public const string PartyCreditLimit = "finance.party-credit-limit.v1";
    public const string OfficialSalesInvoice = "finance.official-sales-invoice.v1";
    public const string OfficialInvoiceImport = "finance.official-invoice-import.v1";
    public const string ProductPriceList = "product.price-list-version.v1";
    public const string ProductPackageStandard = "product.package-standard-version.v1";
    public const string BankAccountIbanChange = "finance.bank-account-iban-change.v1";
    public const string SupplierReturnCostException = "purchasing.supplier-return-cost-exception.v1";
    public const string LogisticsWeighingDifference = "logistics.weighing-difference.v1";
    public const string LogisticsPlanExcess = "logistics.plan-excess.v1";
    public const string CounterpartyTaxIdentity = "counterparty.tax-identity-change.v1";
    public const string CounterpartyLegalName = "counterparty.legal-name-change.v1";
    public const string ManufacturingPlan = "manufacturing.plan.v1";
    public const string InventoryTransferOverage = "inventory.transfer-overage.v1";
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
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>([ExpensePayment, ManualBankPaymentReversal, ManualReceiptReversal, OfficialSalesInvoice, OfficialInvoiceImport, PartyCreditLimit, ProductPriceList, ProductPackageStandard, BankAccountIbanChange, ManualBankPayment, LogisticsCustomerReturn, LogisticsDispatchNote, InventoryStockScrap, InventoryStockCount, BankAccount, SupplierReturnCostException, LogisticsWeighingDifference, InventoryTransferOverage, LogisticsPlanExcess, CounterpartyTaxIdentity, CounterpartyLegalName, InventoryTransferLoss, OpeningCost, "finance.cash-account.v1",
        "finance.cash-account-passivation.v1", "finance.cash-account-reactivation.v1", SalesOrder, SalesInvoice, SalesSubcontractInvoice, PurchaseRequest, PurchaseOrder, PurchaseServiceOrder, PurchaseInvoiceVariance, PurchaseGoodsReceiptBatch, ManufacturingPlan, CreditAccount], StringComparer.Ordinal);

    private async Task RequireDecisionAuthorityAsync(string kind,string permission,Guid companyId,string actor,CancellationToken token)
    {
        foreach(var required in kind is CounterpartyTaxIdentity or CounterpartyLegalName ? new[]{kind == CounterpartyTaxIdentity ? CounterpartyTaxIdentityPermissionsV1.Approve : CounterpartyLegalNamePermissionsV1.Approve, ErpPermissions.CounterpartyPartiesRead}
            : kind == ProductPriceList ? new[]{ProductPriceListPermissionsV1.Read,ProductPriceListPermissionsV1.Publish}
            : kind == PurchaseInvoiceVariance ? new[]{PurchasingPermissionManifestV1.OrdersPriceRead,permission}
            : (kind is SalesInvoice or SalesSubcontractInvoice)?new[]{permission,SalesPermissionManifestV1.OrdersPriceRead}:new[]{permission})
        {
            var scope=await authority.RevalidatePermissionScopeAsync(required,companyId,token);
            if(!scope.IsGranted||!scope.IsCompanyWide||!user.IsAuthenticated||user.UserId!=actor
                ||!company.IsResolved||company.CompanyId!=companyId||!user.CanAccessCompany(companyId))throw new WorkDecisionException(403);
        }
    }

    private async Task<WorkApprovalDecisionSnapshotV1> SnapshotAsync(string kind, Guid id, CancellationToken token, int? selectedLossRequestNumber = null, int? selectedOverageRequestNumber = null, string? selectedOwnerVersion = null)
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
        try
        {
            snapshot = (kind is BankAccountIbanChange or ProductPriceList) && selectedOwnerVersion is not null
                ? source is IWorkApprovalDecisionVersionSourceV1 versionSource
                    ? await versionSource.ReadDecisionVersionAsync(context, id, selectedOwnerVersion, token)
                    : throw new InvalidOperationException("The owner cannot select an original native version.")
                : await source.ReadDecisionAsync(context, id, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) when (kind is OfficialInvoiceImport or OfficialSalesInvoice or ManualBankPaymentReversal or ManualReceiptReversal or ExpensePayment) { await Fence(); throw new WorkDecisionException(403); }
        catch { await Fence(); throw new WorkDecisionException(503); }
        await Fence();
        if (snapshot is null) throw new WorkDecisionException(404);
        if (kind == ExpensePayment && (source.Module != "finance" || source.ApproverPermission != FinancePermissionNames.ExpensePaymentApprove)
            || kind == ManualBankPaymentReversal && (source.Module != "finance" || source.ApproverPermission != FinancePermissionNames.PaymentReverseApprove)
            || kind == ManualReceiptReversal && (source.Module != "finance" || source.ApproverPermission != FinancePermissionNames.ReverseApprove)
            || kind == OfficialSalesInvoice && (source.Module != "finance" || source.ApproverPermission != FinanceOfficialSalesInvoicePermissionNamesV1.Approve)
            || kind == OfficialInvoiceImport && (source.Module != "finance" || source.ApproverPermission != FinanceOfficialInvoiceImportPermissionNamesV1.Approve)
            || kind == PartyCreditLimit && (source.Module != "finance" || source.ApproverPermission != ErpPermissions.FinanceCreditLimitsApprove)
            || kind == ManualBankPayment && (source.Module != "finance" || source.ApproverPermission != FinancePermissionNames.ManualBankPaymentApprove)
            || kind == LogisticsCustomerReturn && (source.Module != "logistics" || source.ApproverPermission != LogisticsPermissionManifestV1.CustomerReturnsDecision)
            || kind == LogisticsDispatchNote && (source.Module != "logistics" || source.ApproverPermission != LogisticsPermissionManifestV1.DispatchNotesApprove)
            || kind == InventoryStockScrap && (source.Module != "inventory" || source.ApproverPermission != InventoryPermissionManifestV1.StockScrapApprove)
            || kind == InventoryStockCount && (source.Module != "inventory" || source.ApproverPermission != InventoryPermissionManifestV1.StockCountApprove)
            || kind == BankAccount && (source.Module != "finance" || source.ApproverPermission != ErpPermissions.FinanceBankAccountApprove)
            || kind == ProductPriceList && (source.Module != "product" || source.ApproverPermission != ProductPriceListPermissionsV1.Publish)
            || kind == ProductPackageStandard && (source.Module != "product" || source.ApproverPermission != ProductPackageStandardPermissionsV3.Approve)
            || kind == BankAccountIbanChange && (source.Module != "finance" || source.ApproverPermission != ErpPermissions.FinanceBankAccountApprove)
            || kind == SupplierReturnCostException && (source.Module != "purchasing" || source.ApproverPermission != PurchasingPermissionManifestV1.OrdersApprove)
            || kind is (CounterpartyTaxIdentity or CounterpartyLegalName) && source.Module != "counterparty"
            || kind == InventoryTransferOverage && (source.Module != "inventory" || source.ApproverPermission != InventoryPermissionManifestV1.StockTransferOverageApprove)
            || kind == LogisticsWeighingDifference && (source.Module != "logistics" || source.ApproverPermission != LogisticsPermissionManifestV1.WeighingDifferencesApprove)
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
            || !ValidInventoryStockScrapProof(kind, snapshot)
            || !ValidInventoryStockCountProof(kind, snapshot)
            || !ValidBankAccountProof(kind, snapshot)
            || !ValidBankIbanProof(kind, snapshot)
            || kind == ManualBankPayment && !ValidManualBankPaymentSnapshot(snapshot)
            || !ValidPartyCreditLimitProof(kind, snapshot)
            || !ValidOfficialImportProof(kind, snapshot)
            || !ValidOfficialSalesProof(kind, snapshot)
            || !ValidManualBankPaymentReversalProof(kind, snapshot)
            || !ValidManualReceiptReversalProof(kind, snapshot)
            || !ValidExpensePaymentProof(kind, snapshot)
            || !ValidProductPriceListProof(kind, snapshot)
            || !ValidProductPackageProof(kind, snapshot)
            || !ValidSupplierReturnProof(kind, snapshot)
            || !ValidCounterpartyIdentityProof(kind, snapshot)
            || !ValidLogisticsPlanExcessProof(kind, snapshot)
            || !ValidLogisticsWeighingProof(kind, snapshot)
            || !ValidLogisticsDispatchNoteProof(kind, snapshot)
            || !ValidLogisticsCustomerReturnProof(kind, snapshot)
            || (kind == InventoryTransferOverage ? !ValidInventoryTransferOverageProof(snapshot) : snapshot.InventoryTransferOverageProof is not null)
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
            && !(kind == InventoryTransferLoss && MayReachHistoricalLossReceipt(snapshot, selectedLossRequestNumber, actor))
            && !(kind == InventoryTransferOverage && MayReachHistoricalOverageReceipt(snapshot, selectedOverageRequestNumber, actor)))
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
        if (kind == SupplierReturnCostException)
        {
            var disclosed = false;
            try
            {
                var prices = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.OrdersPriceRead, snapshot.CompanyId, token);
                disclosed = prices.IsGranted && prices.IsCompanyWide;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { /* The owner's price-read failure withholds costs; it never grants or blocks a decision. */ }
            if (!disclosed)
                rules = rules.Select(rule => rule.RuleCode == "purchasing.supplier-return-manual-cost" ? rule with { Actual = null, Unit = null } : rule).ToArray();
            snapshot = snapshot with { Item = snapshot.Item with { Rules = rules } };
        }
        await RequireDecisionAuthorityAsync(kind,sources.Single(source => source.Kind == kind).ApproverPermission,snapshot.CompanyId,actor!,token);
        // SnapshotAsync validated the immutable binding; only this authorized human GET exposes the proposal.
        var logisticsPreview = snapshot.LogisticsPlanExcessProof is { } proposal && snapshot.LogisticsPlanExcess is { } logisticsOptions
            ? new WorkLogisticsPlanExcessPreviewV1(logisticsOptions.InstructionVersion, proposal.InstructionId, proposal.LineId,
                proposal.Quantity, proposal.Reason, proposal.ProposedBy, proposal.ProposedAtUtc)
            : null;
        var priceListPreview = kind == ProductPriceList ? await ProductPriceListHumanPreviewAsync(snapshot, actor!, token) : null;
        var ibanPreview = kind == BankAccountIbanChange ? await BankIbanHumanPreviewAsync(snapshot, actor!, token) : null;
        var officialSalesPreview = kind == OfficialSalesInvoice ? await OfficialSalesHumanPreviewAsync(snapshot, actor!, token) : null;
        var importPreview = kind == OfficialInvoiceImport ? await OfficialImportHumanPreviewAsync(snapshot, actor!, token) : null;
        var paymentReversalPreview = kind == ManualBankPaymentReversal ? await ManualBankPaymentReversalHumanPreviewAsync(snapshot, actor!, token) : null;
        var receiptReversalPreview = kind == ManualReceiptReversal ? await ManualReceiptReversalHumanPreviewAsync(snapshot, actor!, token) : null;
        var expensePreview = kind == ExpensePayment ? await ExpensePaymentHumanPreviewAsync(snapshot, actor!, token) : null;
        return new(snapshot.CompanyId, kind, id, snapshot.Item.DocumentLabel, snapshot.SourceVersion, snapshot.Pending,
            kind is ProductPackageStandard or InventoryStockScrap or InventoryStockCount or OpeningCost or PurchaseServiceOrder or PurchaseInvoiceVariance or InventoryTransferLoss or InventoryTransferOverage, snapshot.Item.References, rules, kind is not (ExpensePayment or OfficialInvoiceImport or PurchaseOrder or PurchaseServiceOrder or PurchaseInvoiceVariance or LogisticsPlanExcess or LogisticsWeighingDifference or LogisticsDispatchNote or BankAccount or BankAccountIbanChange or ManualBankPayment or ProductPriceList)
                && !(kind == ManufacturingPlan && rules.Any(rule => rule.RuleCode == "manufacturing.plan-reapproval-after-order-cancel")),
            snapshot.ManufacturingPlanReapproval, snapshot.InventoryTransferLoss,
            snapshot.CounterpartyTaxIdentityChange, snapshot.CounterpartyLegalNameChange, logisticsPreview,
            kind == InventoryTransferOverage ? OveragePreview(snapshot) : null,
            kind == LogisticsWeighingDifference ? WeighingHumanPreview(snapshot.LogisticsWeighingDifferenceProof!) : null,
            kind == SupplierReturnCostException ? SupplierReturnHumanPreview(snapshot) : null,
            kind == InventoryStockCount ? snapshot.InventoryStockCountProof!.Count : null,
            kind == BankAccount ? BankAccountHumanPreview(snapshot) : null,
            kind == InventoryStockScrap ? snapshot.InventoryStockScrapProof!.Scrap : null,
            kind == LogisticsDispatchNote ? snapshot.LogisticsDispatchNoteProof!.Note : null,
            kind == LogisticsCustomerReturn ? snapshot.LogisticsCustomerReturnProof!.Return : null,
            kind == LogisticsCustomerReturn ? snapshot.LogisticsCustomerReturnProof!.Return.QuantityUnit == "Kilogram" || snapshot.LogisticsCustomerReturnProof.Return.Weighing is not null : null,
            ibanPreview, kind == ProductPackageStandard ? snapshot.ProductPackageStandardProof!.Table : null, priceListPreview,
            kind == PartyCreditLimit ? snapshot.PartyCreditLimitProof!.Proposal : null, importPreview, officialSalesPreview, paymentReversalPreview, receiptReversalPreview, expensePreview);
    }

    public async Task<WorkDecisionResultV1> DecideAsync(string kind, Guid id, WorkDecisionRequestV1 request, string key, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (kind == ExpensePayment && (!request.Approve || request.Reason is not null || !ExpensePaymentVersion(request.ExpectedVersion, out _, increment: true)))
            throw new WorkDecisionException(400, "work_decision_invalid");
        if (kind is ManualBankPaymentReversal or ManualReceiptReversal && !ValidNativeReversalRequest(request))
            throw new WorkDecisionException(400, "work_decision_invalid");
        if (kind == OfficialSalesInvoice && !ValidOfficialSalesRequest(request))
            throw new WorkDecisionException(400, "work_decision_invalid");
        if (kind == OfficialInvoiceImport && (!request.Approve || request.Reason is not null || request.ExpectedVersion != "1"))
            throw new WorkDecisionException(400, "work_decision_invalid");
        if (kind == ProductPriceList && (!request.Approve || request.Reason is not null || !PriceListHeaderVersion(request.ExpectedVersion, out _)))
            throw new WorkDecisionException(400, "work_decision_invalid");
        if (kind == BankAccountIbanChange && (!request.Approve || request.Reason is not null || !BankAccountVersion(request.ExpectedVersion)))
            throw new WorkDecisionException(400, "work_decision_invalid");
        var actor = user.UserId;
        var snapshot = await SnapshotAsync(kind, id, token, request.InventoryTransferLoss?.RequestNumber, request.InventoryTransferOverage?.RequestNumber,
            kind is BankAccountIbanChange or ProductPriceList ? request.ExpectedVersion : null);
        if (user.UserId != actor) throw new WorkDecisionException(403);
        if (kind == PartyCreditLimit && !ValidPartyCreditLimitRequest(request)
            || request.ProductPackageStandard is not null && kind != ProductPackageStandard
            || kind == ProductPackageStandard && !ValidProductPackageRequest(request)
            || request.LogisticsCustomerReturn is not null && kind != LogisticsCustomerReturn
            || kind == LogisticsCustomerReturn && !ValidLogisticsCustomerReturnRequest(request)
            || kind == LogisticsDispatchNote && (!request.Approve || request.Reason is not null || !DispatchVersion(request.ExpectedVersion, increment: true))
            || kind == ManualBankPayment && (!request.Approve || request.Reason is not null || !BankAccountVersion(request.ExpectedVersion))
            || kind == BankAccount && (!request.Approve || request.Reason is not null || !BankAccountVersion(request.ExpectedVersion))
            || request.SupplierReturnCostException is not null && kind != SupplierReturnCostException
            || kind == SupplierReturnCostException && !ValidSupplierReturnRequest(snapshot, request)
            || kind == LogisticsWeighingDifference && (!request.Approve || request.Reason is not null || !PositiveWeighingVersion(request.ExpectedVersion))
            || request.InventoryTransferOverage is not null && kind != InventoryTransferOverage
            || kind == InventoryTransferOverage && request.InventoryTransferOverage is null
            || request.LogisticsPlanExcess is not null && kind != LogisticsPlanExcess
            || kind == LogisticsPlanExcess && (request.LogisticsPlanExcess is null || !request.Approve || request.Reason is not null
                || !PositiveLogisticsPlanVersion(request.ExpectedVersion) || !PositiveLogisticsPlanVersion(request.LogisticsPlanExcess.ExpectedInstructionVersion))
            || kind == InventoryTransferOverage && InventoryTransferReceiptApiV1.NormalizeText(request.Reason) is null
            || request.InventoryTransferLoss is not null && kind != InventoryTransferLoss
            || kind == InventoryTransferLoss && (request.InventoryTransferLoss is null || request.ManufacturingPlanReapproval is not null)
            || request.ManufacturingPlanReapproval is not null && (kind != ManufacturingPlan || !request.Approve)
            || kind is (PurchaseOrder or PurchaseServiceOrder or PurchaseInvoiceVariance) && !request.Approve
            || string.IsNullOrWhiteSpace(request.ExpectedVersion) || request.ExpectedVersion.Length > 128
            || (kind == OpeningCost && request.ExpectedVersion != snapshot.SourceVersion)
            || ((kind is OpeningCost or InventoryTransferLoss || !request.Approve && kind is not (ManualBankPaymentReversal or ManualReceiptReversal or OfficialSalesInvoice or SalesOrder or SalesInvoice or SalesSubcontractInvoice or PurchaseRequest or PurchaseGoodsReceiptBatch or ManufacturingPlan or ProductPackageStandard or LogisticsCustomerReturn or CounterpartyTaxIdentity or CounterpartyLegalName or InventoryTransferOverage or InventoryStockCount or InventoryStockScrap)) && (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500))
            || (kind is not (ProductPackageStandard or InventoryStockScrap or InventoryStockCount or OpeningCost or SupplierReturnCostException or PurchaseServiceOrder or PurchaseInvoiceVariance or PurchaseGoodsReceiptBatch or InventoryTransferLoss or InventoryTransferOverage or LogisticsCustomerReturn or CounterpartyTaxIdentity or CounterpartyLegalName) && request.Approve && request.Reason is not null)) throw new WorkDecisionException(400, "work_decision_invalid");
        if (kind is InventoryStockCount or InventoryStockScrap && (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000))
            throw new WorkDecisionException(400, "work_decision_invalid");
        if (kind is InventoryStockCount or InventoryStockScrap && request.ExpectedVersion != "1")
            throw new WorkDecisionException(409, "work_decision_version_conflict");
        if (kind is CounterpartyTaxIdentity or CounterpartyLegalName && request.Reason?.Trim().Length > 1000)
            throw new WorkDecisionException(400, "work_decision_invalid");
        // A retained receipt targets the old request's maker, not the maker of a later request
        // on the same hashed inbox item. All other item scope/exclusions and live delegation checks remain unchanged.
        var verificationItem = kind == InventoryTransferLoss && MayReachHistoricalLossReceipt(snapshot, request.InventoryTransferLoss?.RequestNumber, actor!)
            ? snapshot.Item with { PreparedBy = snapshot.InventoryTransferLossProof!.Requests.Single(row => row.RequestNumber == request.InventoryTransferLoss!.RequestNumber).PreparedBy }
            : kind == InventoryTransferOverage && MayReachHistoricalOverageReceipt(snapshot, request.InventoryTransferOverage?.RequestNumber, actor!)
                ? snapshot.Item with { PreparedBy = snapshot.InventoryTransferOverageProof!.Requests.Single(row => row.RequestNumber == request.InventoryTransferOverage!.RequestNumber).PreparedBy }
                : snapshot.Item;
        async Task<bool> Verify(CancellationToken ct)
        {
            // Preserve this purpose's existing human Read+Publish boundary at the native owned commit fences too.
            // The existing AsyncLocal execution-fence guard permits these real authority reads without recursion.
            if (kind == ProductPriceList)
            {
                try { await RequireDecisionAuthorityAsync(kind, ProductPriceListPermissionsV1.Publish, snapshot.CompanyId, actor!, ct); }
                catch (WorkDecisionException error) when (error.Status is 401 or 403) { return false; }
            }
            return request.DelegationId is not { } delegation
                || await delegations.FindForActorAsync(delegation, verificationItem, ct) is not null;
        }
        if (!await Verify(token)) throw new WorkDecisionException(403);
        using var lease = executionFence.Enter(Verify);
        if (kind == ExpensePayment)
            return await DecideExpensePaymentAsync(id, request, key, snapshot, actor!, token);
        if (kind == ManualBankPaymentReversal)
            return await DecideManualBankPaymentReversalAsync(id, request, key, snapshot, actor!, token);
        if (kind == ManualReceiptReversal)
            return await DecideManualReceiptReversalAsync(id, request, key, snapshot, actor!, token);
        if (kind == OfficialSalesInvoice)
            return await DecideOfficialSalesAsync(id, request, key, snapshot, actor!, token);
        if (kind == OfficialInvoiceImport)
            return await DecideOfficialImportAsync(id, request, key, snapshot, actor!, token);
        if (kind == PartyCreditLimit)
            return await DecidePartyCreditLimitAsync(id, request, key, snapshot, actor!, token);
        if (kind == ProductPriceList)
            return await DecideProductPriceListAsync(id, request, key, snapshot, actor!, token);
        if (kind == ProductPackageStandard)
            return await DecideProductPackageStandardAsync(id, request, key, snapshot, actor!, token);
        if (kind == LogisticsCustomerReturn)
            return await DecideLogisticsCustomerReturnAsync(id, request, key, snapshot, actor!, token);
        if (kind == LogisticsDispatchNote)
            return await DecideLogisticsDispatchNoteAsync(id, request, key, snapshot, actor!, token);
        if (kind == InventoryStockScrap)
            return await DecideInventoryStockScrapAsync(id, request, key, snapshot, actor!, token);
        if (kind == InventoryStockCount)
            return await DecideInventoryStockCountAsync(id, request, key, snapshot, actor!, token);
        if (kind == ManualBankPayment)
            return await DecideManualBankPaymentAsync(id, request, key, snapshot, actor!, token);
        if (kind == BankAccount)
            return await DecideBankAccountAsync(id, request, key, snapshot, actor!, token);
        if (kind == BankAccountIbanChange)
            return await DecideBankIbanAsync(id, request, key, snapshot, actor!, token);
        if (kind == SupplierReturnCostException)
            return await DecideSupplierReturnAsync(id, request, key, snapshot, actor!, token);
        if (kind == LogisticsWeighingDifference)
            return await DecideLogisticsWeighingDifferenceAsync(id, request, key, snapshot, actor!, token);
        if (kind == LogisticsPlanExcess)
            return await DecideLogisticsPlanExcessAsync(id, request, key, snapshot, actor!, token);
        if (kind is CounterpartyTaxIdentity or CounterpartyLegalName)
            return await DecideCounterpartyIdentityAsync(kind, id, request, key, snapshot, actor!, token);
        if (kind == InventoryTransferOverage)
            return await DecideInventoryTransferOverageAsync(id, request, key, snapshot, actor!, token);
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
