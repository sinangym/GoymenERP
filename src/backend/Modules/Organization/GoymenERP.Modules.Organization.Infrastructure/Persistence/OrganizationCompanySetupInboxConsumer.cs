using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.CompanySetup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>ID-3b: Identity's first-Admin fact changes only Organization's lifecycle and owner-local inbox.</summary>
public sealed class OrganizationCompanySetupInboxConsumer(OrganizationDbContext database, ICompanyContext companyContext,
    IAuditEventCollector auditEvents, IAuditTransactionWriterV1 auditWriter, TimeProvider timeProvider) : IWorkInboxConsumerV1
{
    public const string ConsumerName = "organization.company-first-admin";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static IdentityCompanyFirstAdminAssignedV1 Read(WorkInboxEventV1 message, ICompanyContext context)
    {
        if (message.SourceName != IdentityCompanyFirstAdminAssignedV1.SourceName || message.EventType != IdentityCompanyFirstAdminAssignedV1.EventType
            || message.Version != 1 || !context.IsResolved || context.CompanyId != message.CompanyId
            || message.EventId != IdentityCompanyFirstAdminAssignedV1.EventId(message.CompanyId)
            || string.IsNullOrWhiteSpace(message.CorrelationId) || message.CorrelationId.Length > 128 || message.CausationId?.Length > 128) throw Poison("invalid_company_setup_envelope");
        IdentityCompanyFirstAdminAssignedV1? payload;
        try { payload = JsonSerializer.Deserialize<IdentityCompanyFirstAdminAssignedV1>(message.PayloadJson, Json); }
        catch (JsonException) { throw Poison("invalid_company_setup_payload"); }
        if (payload is null || payload.CompanyId != message.CompanyId || payload.UserId == Guid.Empty
            || payload.RoleAssignmentId == Guid.Empty || payload.ActorUserId == Guid.Empty || payload.AssignedAtUtc == default || payload.AssignedAtUtc.Offset != TimeSpan.Zero
            || payload.AssignedAtUtc != message.OccurredAtUtc || message.OccurredAtUtc.Offset != TimeSpan.Zero)
            throw Poison("invalid_company_setup_payload");
        return payload;
    }
    public async Task HandleAsync(WorkInboxEventV1 message, CancellationToken cancellationToken)
    {
        var payload = Read(message, companyContext);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.PayloadJson)));
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var receipt = await database.CompanySetupInboxReceipts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == message.CompanyId && x.EventId == message.EventId, cancellationToken);
            if (receipt is not null)
            {
                if (receipt.PayloadHash != hash) throw Poison("company_setup_payload_mismatch");
                await transaction.RollbackAsync(cancellationToken);
                return;
            }
            var company = await database.Companies.SingleOrDefaultAsync(x => x.Id == message.CompanyId, cancellationToken)
                ?? throw Poison("company_setup_company_missing");
            var before = company.Version;
            var now = timeProvider.GetUtcNow().ToUniversalTime();
            if (company.CompleteSetup(now))
            {
                auditEvents.Enqueue(new AuditEventDraft(company.Id, "organization.company.setup-completed.v1", "Organization.Company",
                    company.Id.ToString("D"), "Update", before, company.Version, AuditActorTypes.User,
                    // The permission the actor held for the fact being completed: the explicit first-Admin assignment (KIM-D56).
                    payload.ActorUserId.ToString("D"), ErpPermissions.IdentityCompanyFirstAdminAssign, [],
                    "{\"lifecycleState\":\"SetupPending\"}", "{\"lifecycleState\":\"Active\"}",
                    "İlk Admin ataması Organization yaşam döngüsünü tamamladı.",
                    JsonSerializer.Serialize(new { sourceEventId = message.EventId, payload.RoleAssignmentId }, Json),
                    AuditAiInvolvement.None, null, "System", null, null, message.CorrelationId,
                    message.EventId.ToString("D"), message.EventId.ToString("D")));
            }
            database.CompanySetupInboxReceipts.Add(new CompanySetupInboxReceipt(company.Id, message.EventId, hash, now));
            // The owner writer drains the shared collector before the legacy Organization Save interceptor sees it.
            await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            database.ChangeTracker.Clear();
            auditWriter.DiscardPending();
        }
    }
    private static OutboxPermanentFailureException Poison(string code) => new OutboxPermanentFailureException(code, "Şirket kurulum olayı sözleşmeye uymuyor.");
}
