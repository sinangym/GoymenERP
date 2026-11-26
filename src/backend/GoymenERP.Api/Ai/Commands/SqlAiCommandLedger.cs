using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.AiKnowledge.Domain;

namespace GoymenERP.Api.Ai.Commands;

/// <summary>Typed pipeline adapter over the AI owner's persistence port; no direct table access or business writes.</summary>
public sealed class SqlAiCommandLedger(IAiCommandPersistenceV1 persistence) : IAiCommandLedgerV1
{
    private static readonly JsonSerializerOptions Json = CreateJson();
    public bool IsAvailable => persistence.IsAvailable;

    public Task<bool> TryAddAsync(AiCommandRecordV1 record, IReadOnlyList<AiCommandLedgerEventV1> events,
        CancellationToken cancellationToken = default) =>
        persistence.TryAddAsync(Store(record), events.Select(Store).ToArray(), cancellationToken);

    public async Task<AiCommandRecordV1?> GetAsync(Guid companyId, Guid previewId, CancellationToken cancellationToken = default)
    {
        var stored = await persistence.GetAsync(companyId, previewId, cancellationToken);
        if (stored is null) return null;
        try
        {
            var record = JsonSerializer.Deserialize<AiCommandRecordV1>(stored.RecordJson, Json)
                ?? throw new JsonException("Missing AI command record.");
            if (record.CompanyId != companyId || record.PreviewId != previewId || stored.CompanyId != companyId
                || stored.PreviewId != previewId || record.State != (AiCommandStateV1)stored.State
                || record.Version != stored.Version || record.Preview.Id != previewId
                || record.ExpiresAtUtc != record.Preview.ExpiresAtUtc
                || !string.Equals(record.ActorId, stored.ActorId, StringComparison.Ordinal)
                || !string.Equals(Binding(record), stored.BindingSha256, StringComparison.Ordinal))
                throw new JsonException("AI command stored identity does not match its state.");
            return record;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NullReferenceException)
        {
            throw new AiCommandPersistenceUnavailableException(exception);
        }
    }

    public Task<bool> TryReplaceAsync(AiCommandRecordV1 next, long expectedVersion, AiCommandLedgerEventV1 auditEvent,
        CancellationToken cancellationToken = default) =>
        persistence.TryReplaceAsync(Store(next), expectedVersion, Store(auditEvent), cancellationToken);

    public Task<bool> AppendAsync(AiCommandLedgerEventV1 auditEvent, CancellationToken cancellationToken = default) =>
        persistence.AppendAsync(Store(auditEvent), cancellationToken);

    private static AiCommandStoredRecordV1 Store(AiCommandRecordV1 record) =>
        new(record.CompanyId, record.PreviewId, record.ActorId, (int)record.State, record.Version,
            Binding(record), JsonSerializer.Serialize(record, Json));

    private static string Binding(AiCommandRecordV1 record) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { record.PreviewId, record.Context, record.ToolId, record.ToolVersion,
            record.CanonicalArgumentsJson, record.Preview, record.PreviewIdentityJson, record.PreviewHash, record.ExpiresAtUtc }, Json))));

    private static AiCommandStoredEventV1 Store(AiCommandLedgerEventV1 item) => new(item.EventId, item.CompanyId,
        item.ActorId, (int)item.Kind, item.PreviewId, item.ToolId, item.ToolVersion, item.PreviewHash, item.ApprovalId,
        item.ReasonCode, item.ResultVersion, item.CorrelationId, item.CausationId, item.OccurredAtUtc);

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new PreviewConverter());
        return options;
    }

    private sealed record PreviewPayload(Guid Id, string TargetKind, Guid TargetId, string TargetVersion,
        string CanonicalCommandJson, string DifferenceJson, IReadOnlyList<AiSourceV1> Sources,
        DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);

    private sealed class PreviewConverter : JsonConverter<AiPreviewV1>
    {
        public override AiPreviewV1 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var data = JsonSerializer.Deserialize<PreviewPayload>(ref reader, options) ?? throw new JsonException();
            return new(data.Id, data.TargetKind, data.TargetId, data.TargetVersion, data.CanonicalCommandJson,
                data.DifferenceJson, data.Sources, data.CreatedAtUtc, data.ExpiresAtUtc);
        }

        public override void Write(Utf8JsonWriter writer, AiPreviewV1 value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new PreviewPayload(value.Id, value.TargetKind, value.TargetId,
                value.TargetVersion, value.CanonicalCommandJson, value.DifferenceJson, value.Sources,
                value.CreatedAtUtc, value.ExpiresAtUtc), options);
    }
}
