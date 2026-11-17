using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Audit.UnitTests;

public sealed class LocalCredentialSelfServiceAuditTests
{
    [Theory]
    [InlineData(LocalCredentialSelfServiceOperation.EnableTwoFactor, AuditEvent.TwoFactorEnabledEvent)]
    [InlineData(LocalCredentialSelfServiceOperation.DisableTwoFactor, AuditEvent.TwoFactorDisabledEvent)]
    [InlineData(LocalCredentialSelfServiceOperation.RegenerateRecoveryCodes, AuditEvent.RecoveryCodesRegeneratedEvent)]
    public void Self_service_records_only_actor_target_version_and_method_without_inventing_an_admin_permission(
        LocalCredentialSelfServiceOperation operation, string eventType)
    {
        var source = Data() with { Operation = operation };
        var audit = AuditEvent.CreateLocalCredentialSelfService(source);
        Assert.Equal(("User", source.ActorUserId.ToString("D"), "Identity.LocalCredential", source.ActorUserId.ToString("D")),
            (audit.ActorType, audit.ActorId, audit.EntityType, audit.EntityId));
        Assert.Equal(eventType, audit.EventType); Assert.Null(audit.PermissionCode); Assert.Equal("[]", audit.RoleCodesJson);
        Assert.Equal("{\"credentialVersion\":0}", audit.BeforeJson); Assert.Equal("{\"credentialVersion\":1}", audit.AfterJson);
        Assert.Equal("{\"kind\":\"IdentityLocalCredentialSelfServiceV1\",\"method\":\"TOTP\"}", audit.ApprovalContextJson);
        Assert.Equal(audit.IntegrityHash, AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope()));
        Assert.Throws<ArgumentException>(() => AuditEvent.Create(new(audit.ToIntegrityEnvelope(), audit.IntegrityHash)));
    }

    [Fact]
    public void Self_service_refuses_another_target_unknown_method_invalid_operation_or_nonsequential_credential_version()
    {
        var source = Data();
        foreach (var invalid in new[] { source with { TargetUserId = Guid.NewGuid() }, source with { ActorUserId = Guid.Empty },
            source with { CompanyId = Guid.Empty }, source with { Method = "SMS" }, source with { Operation = (LocalCredentialSelfServiceOperation)99 },
            source with { CredentialVersionBefore = -1 }, source with { CredentialVersionAfter = 2 }, source with { Reason = " " },
            source with { OccurredAtUtc = source.OccurredAtUtc.ToOffset(TimeSpan.FromHours(3)) } })
            Assert.ThrowsAny<ArgumentException>(() => AuditEvent.CreateLocalCredentialSelfService(invalid));
    }

    [Fact]
    public void The_self_service_constraint_is_closed_and_the_Down_guard_precedes_constraint_restoration()
    {
        Assert.StartsWith(AuditEventModelBuilderExtensions.FounderRecoveryPermissionCheck + " OR ", AuditEventModelBuilderExtensions.TypedPermissionCheck);
        Assert.Contains("[ActorId] COLLATE Latin1_General_100_BIN2 = [EntityId] COLLATE Latin1_General_100_BIN2", AuditEventModelBuilderExtensions.LocalCredentialSelfServicePermissionCheck);
        Assert.Contains("[EntityVersionBefore] = [EntityVersionAfter] - 1", AuditEventModelBuilderExtensions.LocalCredentialSelfServicePermissionCheck);
        var migration = new AddLocalCredentialSelfServiceAudit();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51970", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    private static LocalCredentialSelfServiceAuditData Data()
    {
        var user = Guid.NewGuid();
        return new(Guid.NewGuid(), Guid.NewGuid(), user, user, LocalCredentialSelfServiceOperation.EnableTwoFactor, 0, 1, "TOTP",
            new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero), "SENTETIK kişisel güvenlik", "SENTETIK-correlation", null, new string('A', 64));
    }
}
