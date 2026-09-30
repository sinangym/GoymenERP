namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// The loading-instruction use cases (LOG-4a): create (Draft with lines), issue (Draft → Issued),
/// record a load share, list and detail. Implementations obtain company, actor and session from the
/// authenticated server scope and re-measure the Logistics permission at every fence (#276), before
/// the replay is read (ADR-011 §5) and right before the single save — never after the commit (S03).
/// Definite shipment and delivery are NOT here (LOG-5, after the Inventory seam; D3).
/// </summary>
public interface ILogisticsInstructionService
{
    Task<LoadingInstructionResponse> CreateInstructionAsync(CreateLoadingInstructionRequest request, LogisticsCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<LoadingInstructionResponse> IssueInstructionAsync(Guid instructionId, IssueLoadingInstructionRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<LoadingInstructionResponse> RecordLoadAsync(Guid instructionId, Guid lineId, RecordLoadRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>AT-053: the vehicle's manual weighbridge record on an issued instruction (one per instruction; facts, no tolerance verdict).</summary>
    Task<LoadingInstructionResponse> RecordWeighingAsync(Guid instructionId, RecordWeighingRequest request, LogisticsCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>AT-055: a dispatch-note draft from an issued instruction's definite shipments (a shipment is linked to at most one).</summary>
    Task<LoadingInstructionResponse> CreateDispatchNoteAsync(Guid instructionId, CreateDispatchNoteRequest request, LogisticsCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>AT-055 (user decision 2026-09-30): approve a draft; the company's next system number is assigned in the same save.</summary>
    Task<LoadingInstructionResponse> ApproveDispatchNoteAsync(Guid instructionId, Guid dispatchNoteId, ApproveDispatchNoteRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>AT-055 (user decision 2026-09-30): cancel a draft or an approved note with a reason; its shipments are released.</summary>
    Task<LoadingInstructionResponse> CancelDispatchNoteAsync(Guid instructionId, Guid dispatchNoteId, CancelDispatchNoteRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>AT-055 (user decision 2026-09-30): record the official e-İrsaliye number of an approved note, once.</summary>
    Task<LoadingInstructionResponse> SetEDispatchNumberAsync(Guid instructionId, Guid dispatchNoteId, SetEDispatchNumberRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>AT-055: the dispatch notes documenting shipments of one Sales order (the S-04 link), under the instruction read.</summary>
    Task<SalesOrderDispatchNotesView> GetSalesOrderDispatchNotesAsync(Guid salesOrderId, CancellationToken cancellationToken = default);

    Task<LoadingInstructionDetailView> GetInstructionDetailAsync(Guid instructionId, CancellationToken cancellationToken = default);

    Task<LoadingInstructionListResponse> GetInstructionListAsync(LoadingInstructionListQuery query, CancellationToken cancellationToken = default);

    /// <summary>LOG-4b: the APPROVED sales order lines offered for loading, price-free, under the create name (Sinan §4).</summary>
    Task<LogisticsApprovedOrderLinePage> GetApprovedOrderLinesAsync(LogisticsApprovedOrderLinesQuery query, CancellationToken cancellationToken = default);
}
