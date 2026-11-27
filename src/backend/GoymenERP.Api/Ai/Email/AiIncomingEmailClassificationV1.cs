using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai.Email;

public enum AiEmailReplyClassV1 { Unclassified = 1, Acknowledgement = 2, Question = 3, CorrectionRequested = 4 }
public sealed record AiEmailReplyClassificationV1(Guid MessageId, string ContentDigest, AiEmailReplyClassV1 Classification);
/// <summary>Future local-only extractor through the EXISTING gateway/data-class/budget and activation guards,
/// not a provider adapter or permission bypass. Unavailable until that owner is wired. No tool list, recipients,
/// amounts, accounts or commands; only a closed classification, never model-provided executable instructions.</summary>
public interface IAiIncomingEmailClassifierV1
{
    bool IsLocalAndAvailable { get; }
    Task<AiEmailReplyClassV1?> ClassifyDataAsync(AiQuarantinedTextV1 content, CancellationToken cancellationToken);
}
public sealed class AiIncomingEmailClassificationV1(IAiIncomingEmailClassifierV1 classifier, IAiEmailAuthorityV1 authority)
{
    public async Task<AiEmailReplyClassificationV1?> ClassifyAsync(AiEmailScopeV1 scope, Guid messageId,
        string text, CancellationToken cancellationToken = default)
    {
        if (messageId == Guid.Empty || string.IsNullOrWhiteSpace(text) || !classifier.IsLocalAndAvailable
            || !await authority.IsCurrentAsync(scope, cancellationToken)) return null;
        var data = AiExternalContentQuarantineV1.Admit(text, AiExternalContentSourceV1.Email);
        var result = await classifier.ClassifyDataAsync(data, cancellationToken);
        if (!await authority.IsCurrentAsync(scope, cancellationToken) || result is null || !Enum.IsDefined(result.Value)) return null;
        return new(messageId, data.Sha256, result.Value);
    }
}
