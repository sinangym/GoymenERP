using System.Text.Json;

namespace GoymenERP.Modules.Audit.Domain.Events;

public enum FounderRecoveryAuditOperation { Reactivate = 1, ResetPassword = 2 }
public sealed record FounderRecoveryOperatorDeclaration(string OperatingSystemUser, string MachineName);
public sealed record FounderRecoveryAuditData(Guid EventId, Guid CompanyId, Guid FounderUserId,
    FounderRecoveryAuditOperation Operation, FounderRecoveryOperatorDeclaration Operator,
    IReadOnlyList<Guid> PreconditionCompanyIds, IReadOnlyList<Guid> RestoredAssignmentIds,
    long UserVersionBefore, long UserVersionAfter, DateTimeOffset OccurredAtUtc, string CorrelationId,
    string? CausationId, string IdempotencyKeyHash);

public sealed partial class AuditEvent
{
    public const string FounderRecoveryActor = "identity.recover-founder";
    public const string FounderRecoveredEvent = "identity.founder.recovered";
    public const string FounderPasswordResetEvent = "identity.founder.password_reset";
    public const string FounderRecoveryDeclarationKind = "IdentityFounderRecoveryV1";

    /// <summary>The sole company System variant without a permission; arbitrary draft/envelope construction still rejects null.</summary>
    public static AuditEvent CreateFounderRecovery(FounderRecoveryAuditData data)
    {
        ArgumentNullException.ThrowIfNull(data); ArgumentNullException.ThrowIfNull(data.Operator);
        if (!Enum.IsDefined(data.Operation) || data.FounderUserId == Guid.Empty || data.UserVersionBefore < 1 || data.UserVersionAfter < data.UserVersionBefore)
            throw new ArgumentException("A known founder recovery operation and versioned target are required.");
        var osUser = RequireText(data.Operator.OperatingSystemUser, nameof(data.Operator.OperatingSystemUser), 256);
        var machine = RequireText(data.Operator.MachineName, nameof(data.Operator.MachineName), 256);
        ArgumentNullException.ThrowIfNull(data.PreconditionCompanyIds); ArgumentNullException.ThrowIfNull(data.RestoredAssignmentIds);
        if (data.PreconditionCompanyIds.Any(id => id == Guid.Empty) || data.RestoredAssignmentIds.Any(id => id == Guid.Empty)
            || data.PreconditionCompanyIds.Distinct().Count() != data.PreconditionCompanyIds.Count
            || data.RestoredAssignmentIds.Distinct().Count() != data.RestoredAssignmentIds.Count)
            throw new ArgumentException("Recovery evidence contains invalid identities.");
        if (data.Operation == FounderRecoveryAuditOperation.ResetPassword && data.RestoredAssignmentIds.Count != 0)
            throw new ArgumentException("Password reset cannot claim restored assignments.");
        var envelope = new AuditEventEnvelope(data.EventId, data.CompanyId, data.OccurredAtUtc,
            data.Operation == FounderRecoveryAuditOperation.Reactivate ? FounderRecoveredEvent : FounderPasswordResetEvent,
            "Identity.User", data.FounderUserId.ToString("D"), data.Operation == FounderRecoveryAuditOperation.Reactivate ? "RecoverFounder" : "ResetFounderPassword",
            data.UserVersionBefore, data.UserVersionAfter, "System", FounderRecoveryActor, null, "[]", "{}",
            JsonSerializer.Serialize(new { founderUserId = data.FounderUserId, preconditionCompanyIds = data.PreconditionCompanyIds.Order().ToArray(), restoredAssignmentIds = data.RestoredAssignmentIds.Order().ToArray() }),
            "Etkileşimli kurucu kurtarma işlemi tamamlandı",
            JsonSerializer.Serialize(new { kind = FounderRecoveryDeclarationKind, operatingSystemUser = osUser, machineName = machine }),
            "None", null, "System", null, null, data.CorrelationId, data.CausationId, data.IdempotencyKeyHash, "Succeeded", 1);
        return new(new AuditEventData(envelope, AuditEventIntegrity.Compute(envelope)), founderRecovery: true);
    }
}
