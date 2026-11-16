namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// STK-S33 (KLN-D44, KLN-D41): the cross-module trace read of one label. Forward follows the label into what was made and
/// shipped from it (production output, customer shipment); backward follows it back to where it came from (production inputs,
/// supplier receipt). It is read under the label-detail permissions only (KLN-D44: no separate trace permission, no grant) and
/// creates nothing: no attempt, audit, version, outbox or stock effect.
/// </summary>
public static class InventoryLabelTraceApiV1
{
    public const string Path = "/api/v1/inventory/labels/{labelId:guid}/trace";
    public const string GetOperationId = "GetInventoryLabelTraceV1";
    public const string SearchPath = "/api/v1/inventory/label-trace/search";
    public const string SearchOperationId = "SearchInventoryLabelTraceV1";
    public const string DirectionQueryKey = "direction";
    public const string TextQueryKey = "text";
    public const string Forward = "forward";
    public const string Backward = "backward";

    /// <summary>Technical read bound: the most labels one directed walk returns; a longer chain sets the window flag.</summary>
    public const int MaximumLabels = 200;

    /// <summary>The genealogy read window (the genealogy reader's own upper bound).</summary>
    public const int GenealogyWindow = 100;

    /// <summary>Technical read bound: the most movement lines one trace returns; more sets the window flag.</summary>
    public const int MaximumEvents = 200;

    public const int MinimumSearchLength = 2;
    public const int MaximumSearchLength = 64;

    /// <summary>Technical read bound: the most labels one search returns; more sets the window flag.</summary>
    public const int MaximumSearchResults = 25;
}

public enum InventoryLabelTraceDirectionV1
{
    Forward = 1,
    Backward = 2,
}

public interface IInventoryLabelTraceV1
{
    /// <summary>The directed trace of a label of the context company; another company's label is the same 404 as a missing one.</summary>
    Task<InventoryLabelTraceV1> ReadAsync(
        Guid labelId,
        InventoryLabelTraceDirectionV1 direction,
        CancellationToken cancellationToken = default);

    /// <summary>KLN-D41: the context company's labels whose own code or the supplier's original number starts with the text.</summary>
    Task<InventoryLabelTraceSearchV1> SearchAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>One label reached by the walk; the root is depth 0, each transformation step adds one.</summary>
public sealed record InventoryLabelTraceNodeV1(Guid LabelId, string Code, int Depth);

/// <summary>
/// One genealogy edge on the walked path (split, merge or manufacturing). For a manufacturing edge the production order and the
/// input's consumed kg and share are Manufacturing's own answer (URT-D62/URT-D63) passed through as text, never recomputed here;
/// they are null while the production owner did not answer.
/// </summary>
public sealed record InventoryLabelTraceLinkV1(
    Guid TransformationId,
    string Kind,
    DateTimeOffset OccurredAtUtc,
    Guid SourceLabelId,
    Guid OutputLabelId,
    string SourceQuantityKg,
    string OutputQuantityKg,
    Guid? ProductionOrderId,
    string? ConsumedKg,
    string? ConsumedShare,
    string? ShareStatus);

/// <summary>
/// One stock movement line of a traced label in a trace step (supplier receipt, production issue/output, shipment, returns).
/// <see cref="PartyId"/> is the supplier of a receipt or the customer of a shipment as their owners answered it; null when unknown.
/// </summary>
public sealed record InventoryLabelTraceEventV1(
    Guid MovementId,
    int LineNumber,
    Guid LabelId,
    string Step,
    string MovementKind,
    DateTimeOffset OccurredAtUtc,
    string SourceKind,
    Guid SourceId,
    Guid? SourceLineId,
    string Direction,
    string QuantityKg,
    Guid? PartyId,
    string? SupplierLabelNumber);

public sealed record InventoryLabelTracePartyV1(
    Guid PartyId,
    string Role,
    string Code,
    string LegalName,
    string? TradeName);

public sealed record InventoryLabelTraceV1(
    Guid LabelId,
    string LabelCode,
    string Direction,
    IReadOnlyList<InventoryLabelTraceNodeV1> Labels,
    IReadOnlyList<InventoryLabelTraceLinkV1> Links,
    IReadOnlyList<InventoryLabelTraceEventV1> Events,
    IReadOnlyList<InventoryLabelTracePartyV1> Parties,
    string ProductionStatus,
    string PartyStatus,
    string PartyNameStatus,
    bool WindowFull);

public sealed record InventoryLabelTraceSearchItemV1(
    Guid LabelId,
    string Code,
    string? SupplierLabelNumber,
    string MatchedBy);

public sealed record InventoryLabelTraceSearchV1(
    IReadOnlyList<InventoryLabelTraceSearchItemV1> Items,
    bool WindowFull,
    string SupplierSearchStatus);

public static class InventoryLabelTraceStepsV1
{
    public const string SupplierReceipt = "SupplierReceipt";
    public const string SupplierReturn = "SupplierReturn";
    public const string ProductionIssue = "ProductionIssue";
    public const string ProductionReturn = "ProductionReturn";
    public const string ProductionOutput = "ProductionOutput";
    public const string ProductionOutputReversal = "ProductionOutputReversal";
    public const string Shipment = "Shipment";
    public const string CustomerReturn = "CustomerReturn";

    /// <summary>The definite-shipment Issue's source kind (Logistics owns the shipment, the source line is its instruction line).</summary>
    public const string DefiniteShipmentSourceKind = "logistics.definite-shipment";

    public static IReadOnlyList<string> SourceKinds { get; } =
    [
        InventoryGoodsReceiptContractV1.SourceKind,
        InventorySupplierReturnExitContractV1.SourceKind,
        InventoryManufacturingRealizationContractV1.InputIssueSourceKind,
        InventoryManufacturingRealizationContractV1.InputReturnSourceKind,
        InventoryManufacturingRealizationContractV1.OutputSourceKind,
        InventoryManufacturingOutputReversalContractV1.OutputReversalSourceKind,
        DefiniteShipmentSourceKind,
        InventoryCustomerReturnContractV1.SourceKind,
    ];

    /// <summary>The trace step of a movement source kind, or null for a kind that is not a trace step.</summary>
    public static string? StepOf(string? sourceKind) => sourceKind switch
    {
        InventoryGoodsReceiptContractV1.SourceKind => SupplierReceipt,
        InventorySupplierReturnExitContractV1.SourceKind => SupplierReturn,
        InventoryManufacturingRealizationContractV1.InputIssueSourceKind => ProductionIssue,
        InventoryManufacturingRealizationContractV1.InputReturnSourceKind => ProductionReturn,
        InventoryManufacturingRealizationContractV1.OutputSourceKind => ProductionOutput,
        InventoryManufacturingOutputReversalContractV1.OutputReversalSourceKind => ProductionOutputReversal,
        DefiniteShipmentSourceKind => Shipment,
        InventoryCustomerReturnContractV1.SourceKind => CustomerReturn,
        _ => null,
    };
}

public static class InventoryLabelTracePartyRolesV1
{
    public const string Supplier = "Supplier";
    public const string Customer = "Customer";
}

public static class InventoryLabelTraceMatchesV1
{
    public const string LabelCode = "LabelCode";
    public const string SupplierLabelNumber = "SupplierLabelNumber";
}

/// <summary>
/// The answer state of one enrichment owner. <see cref="NotAnswered"/>: the owner's read is not wired; <see cref="Partial"/>: some
/// requested items stayed unknown; <see cref="Denied"/>: the owner refused the display; <see cref="Unavailable"/>: the owner could
/// not answer reliably. The trace itself is still answered; nothing is guessed in place of a missing answer.
/// </summary>
public static class InventoryLabelTraceStatusesV1
{
    public const string Answered = "Answered";
    public const string NotAnswered = "NotAnswered";
    public const string Partial = "Partial";
    public const string Denied = "Denied";
    public const string Unavailable = "Unavailable";
}

public static class InventoryLabelTraceCodesV1
{
    public const string Unauthenticated = "inventory_label_trace_unauthenticated";
    public const string Forbidden = "inventory_label_trace_forbidden";
    public const string InvalidQuery = "inventory_label_trace_invalid_query";
    public const string NotFound = "inventory_label_trace_not_found";
    public const string Unavailable = "inventory_label_trace_unavailable";
}

public sealed class InventoryLabelTraceException(int statusCode, string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

/// <summary>One input of a manufacturing output as Manufacturing answered it (URT-D62/URT-D63).</summary>
public sealed record InventoryLabelTraceProductionShareV1(
    Guid OutputLabelId,
    Guid InputLabelId,
    Guid OrderId,
    Guid TransformationId,
    decimal ConsumedKg,
    decimal? Share,
    string ShareStatus);

public sealed record InventoryLabelTraceProductionAnswerV1(
    bool Answered,
    IReadOnlyList<InventoryLabelTraceProductionShareV1> Items);

/// <summary>
/// The production owner's lineage of the given output labels of <c>companyId</c> (Inventory never references Manufacturing;
/// the host adapts). Carries no authority; the trace service measured the label-detail permissions around the call.
/// </summary>
public interface IInventoryLabelTraceProductionSourceV1
{
    Task<InventoryLabelTraceProductionAnswerV1> ReadSharesAsync(
        Guid companyId,
        IReadOnlyList<Guid> outputLabelIds,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryLabelTracePartySourceV1(
    string SourceKind,
    Guid SourceId,
    Guid? SourceLineId,
    Guid LabelId,
    string LabelCode);

public sealed record InventoryLabelTracePartyRequestV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyList<InventoryLabelTracePartySourceV1> Sources);

/// <summary>The party of one requested source: the supplier of a receipt (with the supplier's own label number) or the customer of a shipment.</summary>
public sealed record InventoryLabelTracePartyMatchV1(
    string SourceKind,
    Guid SourceId,
    Guid? SourceLineId,
    Guid LabelId,
    string Role,
    Guid? PartyId,
    string? SupplierLabelNumber);

public sealed record InventoryLabelTracePartyAnswerV1(
    bool Answered,
    IReadOnlyList<InventoryLabelTracePartyMatchV1> Items);

/// <summary>
/// The owners' party of receipt and shipment sources (Purchasing for the supplier, Logistics for the customer); the host adapts.
/// Carries no authority; a source the owners do not know stays unanswered, never guessed.
/// </summary>
public interface IInventoryLabelTracePartySourceV1
{
    Task<InventoryLabelTracePartyAnswerV1> ResolveAsync(
        InventoryLabelTracePartyRequestV1 request,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryLabelTraceSupplierMatchV1(Guid LabelId, string LabelCode, string? SupplierLabelNumber);

public sealed record InventoryLabelTraceSupplierSearchAnswerV1(
    bool Answered,
    IReadOnlyList<InventoryLabelTraceSupplierMatchV1> Items);

/// <summary>KLN-D41: the receipt owner's labels whose own code or the supplier's original number starts with the text.</summary>
public interface IInventoryLabelTraceSupplierSearchSourceV1
{
    Task<InventoryLabelTraceSupplierSearchAnswerV1> SearchAsync(
        Guid companyId,
        string text,
        int take,
        CancellationToken cancellationToken = default);
}

/// <summary>Default until the host wires the production owner: the trace answers without shares and says so.</summary>
public sealed class InventoryNotAnsweredLabelTraceProductionSourceV1 : IInventoryLabelTraceProductionSourceV1
{
    public Task<InventoryLabelTraceProductionAnswerV1> ReadSharesAsync(
        Guid companyId,
        IReadOnlyList<Guid> outputLabelIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new InventoryLabelTraceProductionAnswerV1(false, []));
}

/// <summary>Default until the host wires the party owners: the trace answers without parties and says so.</summary>
public sealed class InventoryNotAnsweredLabelTracePartySourceV1 : IInventoryLabelTracePartySourceV1
{
    public Task<InventoryLabelTracePartyAnswerV1> ResolveAsync(
        InventoryLabelTracePartyRequestV1 request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new InventoryLabelTracePartyAnswerV1(false, []));
}

/// <summary>Default until the host wires the receipt owner: the search answers own codes only and says so.</summary>
public sealed class InventoryNotAnsweredLabelTraceSupplierSearchSourceV1 : IInventoryLabelTraceSupplierSearchSourceV1
{
    public Task<InventoryLabelTraceSupplierSearchAnswerV1> SearchAsync(
        Guid companyId,
        string text,
        int take,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new InventoryLabelTraceSupplierSearchAnswerV1(false, []));
}
