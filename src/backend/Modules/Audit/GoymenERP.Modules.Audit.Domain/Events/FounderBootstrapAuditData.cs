using System.Text.Json;

namespace GoymenERP.Modules.Audit.Domain.Events;

/// <summary>
/// One company row of the one-shot founder bootstrap (spec ID-2 "Kurucu bootstrap komutu": "tek transaction içinde ...
/// işaret + audit yazar"; K3-B finding 3). The actor is <c>System</c> plus the operator declaration (the same term as the
/// founder recovery command); there is no permission because no user exists before the command.
/// </summary>
public sealed record FounderBootstrapAuditData(Guid EventId, Guid CompanyId, Guid FounderUserId,
    FounderRecoveryOperatorDeclaration Operator, Guid AdminAssignmentId, IReadOnlyList<Guid> CompanyIds,
    DateTimeOffset OccurredAtUtc, string CorrelationId, string? CausationId, string IdempotencyKeyHash);

public sealed partial class AuditEvent
{
    public const string FounderBootstrapActor = "identity.bootstrap-founder";
    public const string FounderBootstrappedEvent = "identity.founder.bootstrapped";
    public const string FounderBootstrapOperation = "BootstrapFounder";
    public const string FounderBootstrapDeclarationKind = "IdentityFounderBootstrapV1";

    /// <summary>The typed company System variant of the founder bootstrap; the user is new (no before version, after 1).</summary>
    public static AuditEvent CreateFounderBootstrap(FounderBootstrapAuditData data)
    {
        ArgumentNullException.ThrowIfNull(data); ArgumentNullException.ThrowIfNull(data.Operator); ArgumentNullException.ThrowIfNull(data.CompanyIds);
        if (data.FounderUserId == Guid.Empty || data.AdminAssignmentId == Guid.Empty || data.CompanyIds.Count == 0
            || data.CompanyIds.Any(id => id == Guid.Empty) || data.CompanyIds.Distinct().Count() != data.CompanyIds.Count
            || !data.CompanyIds.Contains(data.CompanyId))
            throw new ArgumentException("A founder, its Admin assignment and the bootstrapped companies are required.");
        var osUser = RequireText(data.Operator.OperatingSystemUser, nameof(data.Operator.OperatingSystemUser), 256);
        var machine = RequireText(data.Operator.MachineName, nameof(data.Operator.MachineName), 256);
        var envelope = new AuditEventEnvelope(data.EventId, data.CompanyId, data.OccurredAtUtc, FounderBootstrappedEvent,
            "Identity.User", data.FounderUserId.ToString("D"), FounderBootstrapOperation, null, 1, "System", FounderBootstrapActor,
            null, "[]", "{}",
            JsonSerializer.Serialize(new { founderUserId = data.FounderUserId, adminAssignmentId = data.AdminAssignmentId,
                companyIds = data.CompanyIds.Order().ToArray(), mustChangePassword = true }),
            "Etkileşimli kurucu kurulumu tamamlandı",
            JsonSerializer.Serialize(new { kind = FounderBootstrapDeclarationKind, operatingSystemUser = osUser, machineName = machine }),
            "None", null, "System", null, null, data.CorrelationId, data.CausationId, data.IdempotencyKeyHash, "Succeeded", 1);
        return new(new AuditEventData(envelope, AuditEventIntegrity.Compute(envelope)), founderBootstrap: true);
    }
}
