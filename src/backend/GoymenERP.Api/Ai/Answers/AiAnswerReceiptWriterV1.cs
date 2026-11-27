using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.AiKnowledge.Domain;

namespace GoymenERP.Api.Ai.Answers;

public interface IAiAnswerReceiptWriterV1
{
    Task<Guid?> RecordAsync(Guid companyId, string actorId, string correlationId, AiGatewayAnswerV1 answer,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}

/// <summary>Successful grounded answer only; canonical fingerprints contain no persisted plaintext or raw source identifiers.</summary>
public sealed class AiAnswerReceiptWriterV1(IAiAnswerReceiptPersistenceV1 persistence, TimeProvider clock) : IAiAnswerReceiptWriterV1
{
    public async Task<Guid?> RecordAsync(Guid companyId, string actorId, string correlationId, AiGatewayAnswerV1 answer,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        if (!answer.Grounded || string.IsNullOrWhiteSpace(answer.Text) || answer.Citations is not { Count: > 0 }
            || !AiAnswerReceiptRulesV1.Token(answer.ModelDeployment) || !AiAnswerReceiptRulesV1.Token(answer.PolicyVersion)
            || answer.Citations.Any(x => x is null || x.TargetId == Guid.Empty || string.IsNullOrWhiteSpace(x.ToolId)
                || string.IsNullOrWhiteSpace(x.ToolVersion) || string.IsNullOrWhiteSpace(x.TargetKind)
                || string.IsNullOrWhiteSpace(x.Title) || string.IsNullOrWhiteSpace(x.SourceVersion)
                || x.AsOfUtc == default || x.AsOfUtc.Offset != TimeSpan.Zero)) return null;
        var now = clock.GetUtcNow();
        if (now > DateTimeOffset.MaxValue.AddDays(-AiAnswerReceiptRulesV1.RetentionDays)) return null;
        var citations = answer.Citations.Select(x => AiCanonicalJson.Canonicalize(JsonSerializer.Serialize(x, Json))).ToArray();
        var citationsHash = Hash(JsonSerializer.Serialize(citations, Json));
        var answerHash = Hash(JsonSerializer.Serialize(new { companyId, answer.Text, answer.ModelDeployment, answer.Placement, answer.PolicyVersion, citationsHash }, Json));
        var receipt = new AiAnswerReceiptV1(companyId, Guid.NewGuid(), answerHash, citationsHash, "Panel", answer.ModelDeployment,
            answer.PolicyVersion, now, now.AddDays(AiAnswerReceiptRulesV1.RetentionDays));
        var identity = new AiAnswerReceiptIdentityV1(companyId, receipt.AnswerId, actorId, correlationId);
        return await persistence.TryAddAsync(receipt, identity, freshConsumerFence, cancellationToken) ? receipt.AnswerId : null;
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AiCanonicalJson.Canonicalize(value))));
}
