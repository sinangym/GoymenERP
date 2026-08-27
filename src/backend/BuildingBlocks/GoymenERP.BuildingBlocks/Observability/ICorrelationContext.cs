namespace GoymenERP.BuildingBlocks.Observability;

public interface ICorrelationContext
{
    bool IsResolved { get; }

    string CorrelationId { get; }

    string? CausationId { get; }

    string SourceChannel { get; }

    string? ClientIpAddress { get; }

    string? UserAgent { get; }

    void Set(
        string correlationId,
        string? causationId,
        string sourceChannel,
        string? clientIpAddress,
        string? userAgent);
}
