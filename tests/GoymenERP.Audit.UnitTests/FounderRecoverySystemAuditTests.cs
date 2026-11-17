using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Audit.UnitTests;

public sealed class FounderRecoverySystemAuditTests
{
    [Fact]
    public void Only_the_Audit_owner_maps_nullable_permission_with_the_closed_System_check_and_a_first_forward_fix_guard()
    {
        using var owner = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True").Options, new CompanyContext());
        var entity = owner.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AuditEvent))!;
        Assert.True(entity.FindProperty(nameof(AuditEvent.PermissionCode))!.IsNullable);
        Assert.Equal(AuditEventModelBuilderExtensions.TypedPermissionCheck,
            Assert.Single(entity.GetCheckConstraints(), check => check.Name == "CK_AuditEvents_PermissionOrFounderRecovery").Sql);
        var migration = new AddTypedFounderRecoverySystemAudit();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 51952", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
    [Fact]
    public void A_typed_founder_recovery_has_no_invented_permission_and_retains_canonical_integrity_and_operator_evidence()
    {
        var data = Data(); var audit = AuditEvent.CreateFounderRecovery(data);
        Assert.Null(audit.PermissionCode);
        Assert.Equal(("System", AuditEvent.FounderRecoveryActor, "Identity.User", "RecoverFounder", "Succeeded"),
            (audit.ActorType, audit.ActorId, audit.EntityType, audit.Operation, audit.Outcome));
        Assert.Equal(data.CompanyId, audit.CompanyId);
        Assert.Contains("SENTETIK-os", audit.ApprovalContextJson!);
        Assert.Contains("SENTETIK-machine", audit.ApprovalContextJson!);
        Assert.Contains(data.RestoredAssignmentIds[0].ToString("D"), audit.AfterJson);
        Assert.Equal(audit.IntegrityHash, AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope()));
        Assert.Throws<ArgumentException>(() => AuditEvent.Create(new(audit.ToIntegrityEnvelope(), audit.IntegrityHash)));
    }
    [Theory]
    [InlineData("User")]
    [InlineData("Ai")]
    [InlineData("Integration")]
    [InlineData("System")]
    public void General_audit_drafts_still_require_a_permission_for_every_actor_kind(string actor)
    {
        var draft = new AuditEventDraft(Guid.NewGuid(), "SENTETIK.event.v1", "SENTETIK.Entity", "SENTETIK-id", "Update", 1, 2,
            actor, "SENTETIK-actor", null!, [], "{}", "{}", "SENTETIK", null, "None", null, "System", null, null, "SENTETIK-correlation", null, "SENTETIK-key");
        Assert.ThrowsAny<ArgumentException>(() => AuditEventFactory.Create(draft));
    }
    [Fact]
    public void Typed_system_audit_refuses_unknown_operation_missing_operator_and_false_restoration_on_password_reset()
    {
        var valid = Data();
        Assert.Throws<ArgumentException>(() => AuditEvent.CreateFounderRecovery(valid with { Operation = (FounderRecoveryAuditOperation)99 }));
        Assert.Throws<ArgumentException>(() => AuditEvent.CreateFounderRecovery(valid with { Operator = new(" ", "SENTETIK-machine") }));
        Assert.Throws<ArgumentException>(() => AuditEvent.CreateFounderRecovery(valid with { Operation = FounderRecoveryAuditOperation.ResetPassword }));
    }
    private static FounderRecoveryAuditData Data() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), FounderRecoveryAuditOperation.Reactivate,
        new("SENTETIK-os", "SENTETIK-machine"), [Guid.NewGuid()], [Guid.NewGuid()], 1, 2,
        new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero), "SENTETIK-correlation", null, new string('A', 64));
}
