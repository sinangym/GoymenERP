using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

// Produces canonical owner data only; does not grant authority, write or commit.
public static class GroupSharedAuditEventFactory
{
    public static GroupSharedAuditEventData Create(GroupSharedAuditEventDraft draft, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Context);
        var key = draft.IdempotencyKey;
        if (string.IsNullOrEmpty(key) || key.Length > 128
            || key.Any(c => c < 33 || c > 126 || c == ','))
            throw new ArgumentException("The canonical request idempotency key is required.", nameof(draft));
        return CreateWithKeyHash(draft, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))), clock);
    }

    private static GroupSharedAuditEventData CreateWithKeyHash(GroupSharedAuditEventDraft draft,
        string keyHash, TimeProvider? clock)
    {
        var (operation, permission) = draft.Context.Operation switch
        {
            GroupSharedMasterOperation.DraftCreate => ("DraftCreate", "product.draft.create"),
            GroupSharedMasterOperation.DraftBasicEdit => ("DraftBasicEdit", "product.draft.edit"),
            // #329: the card approval (its event type product.card.activated.v1 is the Product owner's).
            GroupSharedMasterOperation.Activate => ("Activate", "product.card.activate"),
            // The deactivation / reactivation (their event types are the Product owner's).
            GroupSharedMasterOperation.Deactivate => ("Deactivate", "product.card.deactivate"),
            GroupSharedMasterOperation.Reactivate => ("Reactivate", "product.card.reactivate"),
            _ => throw new ArgumentException("Unsupported Product audit command.", nameof(draft)),
        };
        var envelope = new GroupSharedAuditEventEnvelope(Guid.NewGuid(),
            new(draft.ProductId, draft.OriginCompanyId, draft.Context.AuthorityRevision),
            (clock ?? TimeProvider.System).GetUtcNow(), draft.EventType, draft.EntityType,
            draft.ProductId.ToString("D"), operation, draft.EntityVersionBefore, draft.EntityVersionAfter,
            AuditActorTypes.User, draft.LocalActorId.ToString("D"), permission, "[\"SYSTEM_ADMINISTRATOR\"]",
            draft.BeforeJson, draft.AfterJson, draft.Reason, draft.AiInvolvement, draft.AiContextJson,
            draft.SourceChannel, draft.ClientIpAddress, draft.UserAgent, draft.CorrelationId, draft.CausationId,
            keyHash, AuditOutcomes.Succeeded, 2);
        return GroupSharedAuditEventData.Create(envelope);
    }
}
