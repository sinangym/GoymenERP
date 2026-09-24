using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// F-03 allocation command. Product/account/open-item facts are server-owned; the caller supplies
/// identities, expected versions, the exact positive amount and an audit reason only.
/// </summary>
public sealed record CreateFinanceAllocationRequest(
    Guid AllocationId,
    Guid SourceId,
    string ExpectedSourceVersion,
    Guid OpenItemId,
    string ExpectedOpenItemVersion,
    string Amount,
    string Reason);

public sealed record ReleaseFinanceAllocationRequest(
    Guid AllocationId,
    string ExpectedAllocationVersion,
    string ExpectedOpenItemVersion,
    string Reason);

[JsonConverter(typeof(JsonStringEnumConverter<FinanceAllocationStatus>))]
public enum FinanceAllocationStatus
{
    Active = 1,
    Released = 2,
}

public sealed record FinanceAllocationView(
    Guid Id,
    Guid CompanyId,
    Guid SourceId,
    Guid OpenItemId,
    Guid CurrencyId,
    string Amount,
    FinanceAllocationStatus Status,
    string Version,
    string Reason,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? ReleasedBy,
    DateTimeOffset? ReleasedAtUtc,
    string? ReleaseReason);

public sealed record FinanceAllocationCommandResult(
    CommandOutcome Outcome,
    FinanceAllocationView? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// Finance-owned allocation boundary. Runtime publication requires its own narrow permission and
/// central host/OpenAPI work; declaring this interface grants nothing and exposes no route.
/// </summary>
public interface IFinanceAllocationService
{
    Task<FinanceAllocationCommandResult> CreateAsync(
        FinanceContext context,
        CommandMetadata metadata,
        CreateFinanceAllocationRequest request,
        CancellationToken cancellationToken);

    Task<FinanceAllocationCommandResult> ReleaseAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ReleaseFinanceAllocationRequest request,
        CancellationToken cancellationToken);
}
