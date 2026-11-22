using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Audit.UnitTests;

/// <summary>K3-B finding 3: the founder bootstrap writes a typed company System audit row (spec ID-2). SENTETIK.</summary>
public sealed class FounderBootstrapSystemAuditTests
{
    [Fact]
    public void A_typed_founder_bootstrap_has_no_invented_permission_and_keeps_operator_evidence_and_canonical_integrity()
    {
        var data = Data();
        var audit = AuditEvent.CreateFounderBootstrap(data);

        Assert.Null(audit.PermissionCode);
        Assert.Equal(("System", AuditEvent.FounderBootstrapActor, "Identity.User", AuditEvent.FounderBootstrapOperation, "Succeeded", "System"),
            (audit.ActorType, audit.ActorId, audit.EntityType, audit.Operation, audit.Outcome, audit.SourceChannel));
        Assert.Equal(AuditEvent.FounderBootstrappedEvent, audit.EventType);
        Assert.Equal((data.CompanyId, data.FounderUserId.ToString("D")), (audit.CompanyId, audit.EntityId));
        Assert.Equal(((long?)null, (long?)1), (audit.EntityVersionBefore, audit.EntityVersionAfter));
        Assert.Contains("SENTETIK-os", audit.ApprovalContextJson!);
        Assert.Contains("SENTETIK-machine", audit.ApprovalContextJson!);
        Assert.Contains(AuditEvent.FounderBootstrapDeclarationKind, audit.ApprovalContextJson!);
        Assert.Contains(data.AdminAssignmentId.ToString("D"), audit.AfterJson);
        Assert.Equal(audit.IntegrityHash, AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope()));
        // The general factory never admits the null-permission variant.
        Assert.Throws<ArgumentException>(() => AuditEvent.Create(new(audit.ToIntegrityEnvelope(), audit.IntegrityHash)));
    }

    [Fact]
    public void A_founder_bootstrap_row_refuses_missing_operator_founder_assignment_or_a_company_outside_the_run()
    {
        var valid = Data();
        Assert.Throws<ArgumentException>(() => AuditEvent.CreateFounderBootstrap(valid with { Operator = new(" ", "SENTETIK-machine") }));
        Assert.Throws<ArgumentException>(() => AuditEvent.CreateFounderBootstrap(valid with { Operator = new("SENTETIK-os", "") }));
        Assert.Throws<ArgumentException>(() => AuditEvent.CreateFounderBootstrap(valid with { FounderUserId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => AuditEvent.CreateFounderBootstrap(valid with { AdminAssignmentId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => AuditEvent.CreateFounderBootstrap(valid with { CompanyIds = [Guid.NewGuid()] }));
        Assert.Throws<ArgumentException>(() => AuditEvent.CreateFounderBootstrap(valid with { CompanyIds = [] }));
    }

    [Fact]
    public void The_closed_check_admits_the_bootstrap_variant_next_to_the_existing_typed_variants()
    {
        Assert.EndsWith(" OR " + AuditEventModelBuilderExtensions.FounderBootstrapPermissionCheck, AuditEventModelBuilderExtensions.TypedPermissionCheck,
            StringComparison.Ordinal);
        Assert.StartsWith(AuditEventModelBuilderExtensions.FounderRecoveryPermissionCheck + " OR ", AuditEventModelBuilderExtensions.TypedPermissionCheck,
            StringComparison.Ordinal);
        Assert.Contains("N'IdentityFounderBootstrapV1'", AuditEventModelBuilderExtensions.FounderBootstrapPermissionCheck, StringComparison.Ordinal);
        Assert.Contains("[EntityVersionAfter] = 1", AuditEventModelBuilderExtensions.FounderBootstrapPermissionCheck, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_writer_refuses_a_missing_transaction_and_an_incomplete_or_mixed_batch_before_any_write()
    {
        var writer = new FounderBootstrapSystemAuditV1(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var row = new FounderBootstrapCompanyAuditV1(a, Guid.NewGuid(), Guid.NewGuid(), new("SENTETIK-os", "SENTETIK-machine"), [a, b],
            new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero), "SENTETIK-correlation", null, "SENTETIK-key");

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.AppendAsync(null!, [row]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => writer.AppendAsync(null!, null!));
    }

    private static FounderBootstrapAuditData Data()
    {
        var company = Guid.NewGuid();
        return new(Guid.NewGuid(), company, Guid.NewGuid(), new("SENTETIK-os", "SENTETIK-machine"), Guid.NewGuid(), [company, Guid.NewGuid()],
            new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero), "SENTETIK-correlation", null, new string('A', 64));
    }
}
