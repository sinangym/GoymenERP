namespace GoymenERP.BuildingBlocks.Observability;

public sealed class CorrelationContext : ICorrelationContext
{
    private string? _correlationId;

    public bool IsResolved => _correlationId is not null;

    public string CorrelationId => _correlationId
        ?? throw new InvalidOperationException("Korelasyon bağlamı henüz belirlenmedi.");

    public string? CausationId { get; private set; }

    public string SourceChannel { get; private set; } = string.Empty;

    public string? ClientIpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public void Set(
        string correlationId,
        string? causationId,
        string sourceChannel,
        string? clientIpAddress,
        string? userAgent)
    {
        var normalizedCorrelationId = RequireText(
            correlationId,
            nameof(correlationId),
            128);
        var normalizedSourceChannel = RequireText(
            sourceChannel,
            nameof(sourceChannel),
            32);

        if (_correlationId is not null
            && (!string.Equals(
                    _correlationId,
                    normalizedCorrelationId,
                    StringComparison.Ordinal)
                || !string.Equals(CausationId, causationId, StringComparison.Ordinal)
                || !string.Equals(
                    SourceChannel,
                    normalizedSourceChannel,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Bir istek içinde korelasyon bağlamı değiştirilemez.");
        }

        _correlationId = normalizedCorrelationId;
        CausationId = NormalizeOptional(causationId, nameof(causationId), 128);
        SourceChannel = normalizedSourceChannel;
        ClientIpAddress = NormalizeOptional(
            clientIpAddress,
            nameof(clientIpAddress),
            64);
        UserAgent = NormalizeOptional(userAgent, nameof(userAgent), 512);
    }

    private static string RequireText(
        string value,
        string parameterName,
        int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return normalized.Length > maximumLength
            ? throw new ArgumentOutOfRangeException(
                parameterName,
                $"Değer {maximumLength} karakterden uzun olamaz.")
            : normalized;
    }

    private static string? NormalizeOptional(
        string? value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return RequireText(value, parameterName, maximumLength);
    }
}
