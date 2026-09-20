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

    Task<LoadingInstructionDetailView> GetInstructionDetailAsync(Guid instructionId, CancellationToken cancellationToken = default);

    Task<LoadingInstructionListResponse> GetInstructionListAsync(LoadingInstructionListQuery query, CancellationToken cancellationToken = default);

    /// <summary>LOG-4b: the APPROVED sales order lines offered for loading, price-free, under the create name (Sinan §4).</summary>
    Task<LogisticsApprovedOrderLinePage> GetApprovedOrderLinesAsync(LogisticsApprovedOrderLinesQuery query, CancellationToken cancellationToken = default);
}
