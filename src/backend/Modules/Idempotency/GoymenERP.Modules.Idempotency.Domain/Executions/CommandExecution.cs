using System.Text.Json;

namespace GoymenERP.Modules.Idempotency.Domain.Executions;

public sealed class CommandExecution
{
    private CommandExecution()
    {
    }

    private CommandExecution(CommandExecutionData data)
    {
        Id = RequireId(data.Id, nameof(data.Id));
        CompanyId = RequireId(data.CompanyId, nameof(data.CompanyId));
        CommandName = RequireText(data.CommandName, nameof(data.CommandName), 160);
        IdempotencyKeyHash = RequireSha256(
            data.IdempotencyKeyHash,
            nameof(data.IdempotencyKeyHash));
        RequestHash = RequireSha256(data.RequestHash, nameof(data.RequestHash));
        ActorId = RequireText(data.ActorId, nameof(data.ActorId), 128);
        Outcome = RequireText(data.Outcome, nameof(data.Outcome), 64);
        HttpStatusCode = data.HttpStatusCode is >= 100 and <= 599
            ? data.HttpStatusCode
            : throw new ArgumentOutOfRangeException(nameof(data.HttpStatusCode));
        ResponseJson = RequireJson(data.ResponseJson, nameof(data.ResponseJson));
        OriginalCorrelationId = RequireText(
            data.OriginalCorrelationId,
            nameof(data.OriginalCorrelationId),
            128);
        CompletedAtUtc = data.CompletedAtUtc.ToUniversalTime();
        SchemaVersion = data.SchemaVersion > 0
            ? data.SchemaVersion
            : throw new ArgumentOutOfRangeException(nameof(data.SchemaVersion));
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string CommandName { get; private set; } = string.Empty;

    public string IdempotencyKeyHash { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public string ActorId { get; private set; } = string.Empty;

    public string Outcome { get; private set; } = string.Empty;

    public int HttpStatusCode { get; private set; }

    public string ResponseJson { get; private set; } = string.Empty;

    public string OriginalCorrelationId { get; private set; } = string.Empty;

    public DateTimeOffset CompletedAtUtc { get; private set; }

    public int SchemaVersion { get; private set; }

    public static CommandExecution Complete(CommandExecutionData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new CommandExecution(data);
    }

    private static Guid RequireId(Guid value, string parameterName)
    {
        return value == Guid.Empty
            ? throw new ArgumentException("Kimlik boş olamaz.", parameterName)
            : value;
    }

    private static string RequireText(
        string? value,
        string parameterName,
        int maximumLength)
    {
        var canonical = value?.Trim();
        if (string.IsNullOrWhiteSpace(canonical))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return canonical.Length > maximumLength
            ? throw new ArgumentOutOfRangeException(
                parameterName,
                $"Değer {maximumLength} karakterden uzun olamaz.")
            : canonical;
    }

    private static string RequireSha256(string value, string parameterName)
    {
        var canonical = RequireText(value, parameterName, 64);
        return canonical.Length == 64
            && canonical.All(character =>
                character is >= '0' and <= '9'
                    or >= 'A' and <= 'F')
            ? canonical
            : throw new ArgumentException(
                "Değer 64 karakterlik büyük harf SHA-256 özeti olmalıdır.",
                parameterName);
    }

    private static string RequireJson(string value, string parameterName)
    {
        var canonical = RequireText(value, parameterName, int.MaxValue);
        try
        {
            using var _ = JsonDocument.Parse(canonical);
            return canonical;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Geçerli JSON değeri zorunludur.", parameterName, exception);
        }
    }
}

public sealed record CommandExecutionData(
    Guid Id,
    Guid CompanyId,
    string CommandName,
    string IdempotencyKeyHash,
    string RequestHash,
    string ActorId,
    string Outcome,
    int HttpStatusCode,
    string ResponseJson,
    string OriginalCorrelationId,
    DateTimeOffset CompletedAtUtc,
    int SchemaVersion);
