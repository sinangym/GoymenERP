using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Audit.UnitTests;

public sealed class LocalTwoFactorOperatorRecoveryAuditTests
{
    [Fact]
    public void Recovery_has_a_closed_global_subject_and_canonical_version_proof_without_invented_company_or_permission()
    {
        var data = Data(); var row = AuditEvent.CreateLocalTwoFactorOperatorRecovery(data);
        Assert.Equal(AuditEvent.PlatformCredentialRecoverySubject, row.SubjectKind); Assert.Equal(3, row.SchemaVersion);
        Assert.Null(row.CompanyId); Assert.Null(row.OriginCompanyId); Assert.Null(row.AuthorityRevision); Assert.Null(row.PermissionCode);
        Assert.Equal(data, row.ToLocalTwoFactorOperatorRecoveryIntegrityData());
        Assert.Equal(data.ComputeIntegrity(), row.IntegrityHash);
        Assert.Contains("SENTETIK-operator", row.ApprovalContextJson!);
        Assert.DoesNotContain("Stamp", row.BeforeJson, StringComparison.OrdinalIgnoreCase);
        typeof(AuditEvent).GetProperty(nameof(AuditEvent.ActorId))!.SetValue(row, "identity.recover-founder");
        Assert.Throws<InvalidOperationException>(() => row.ToLocalTwoFactorOperatorRecoveryIntegrityData());
    }
    [Fact]
    public void A_closed_system_event_does_not_admit_an_unknown_outcome_or_a_false_version_transition()
    {
        var data = Data();
        Assert.Throws<ArgumentException>(() => AuditEvent.CreateLocalTwoFactorOperatorRecovery(data with { Outcome = (LocalTwoFactorOperatorRecoveryAuditOutcome)99 }));
        Assert.Throws<ArgumentException>(() => AuditEvent.CreateLocalTwoFactorOperatorRecovery(data with { After = data.Before }));
        Assert.Throws<ArgumentException>(() => AuditEvent.CreateLocalTwoFactorOperatorRecovery(data with { Operator = new("", "SENTETIK-machine") }));
    }
    [Fact]
    public void The_subject_and_null_permission_checks_fix_the_exact_system_operation_and_keep_the_company_read_filter()
    {
        using var owner = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer(
            "Server=model-only;Database=model-only;Integrated Security=True;TrustServerCertificate=True").Options, new CompanyContext());
        var model = owner.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AuditEvent))!;
        var checks = model.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Contains(AuditEventModelBuilderExtensions.OperatorRecoveryPermissionCheck, checks["CK_AuditEvents_Subject"], StringComparison.Ordinal);
        Assert.Contains(AuditEventModelBuilderExtensions.OperatorRecoveryPermissionCheck, checks["CK_AuditEvents_PermissionOrFounderRecovery"], StringComparison.Ordinal);
        var expression = Assert.IsAssignableFrom<System.Linq.Expressions.LambdaExpression>(Assert.Single(model.GetDeclaredQueryFilters()).Expression);
        Assert.DoesNotContain(AuditEvent.PlatformCredentialRecoverySubject, expression.ToString(), StringComparison.Ordinal);
    }
    private static LocalTwoFactorOperatorRecoveryAuditData Data() => new(Guid.NewGuid(), Guid.NewGuid(), new("SENTETIK-operator", "SENTETIK-machine"),
        new(1, 2, 1, true), new(1, 2, 1, true), new(1, 3, 2, false), LocalTwoFactorOperatorRecoveryAuditOutcome.Recovered,
        "SENTETIK-reason", DateTimeOffset.UtcNow, "SENTETIK-correlation", null, new string('A', 64));
    [Fact]
    public void The_EF_migration_widens_only_the_closed_checks_and_refuses_Down_before_dropping_them()
    {
        var migration = new AddPlatformCredentialRecoveryAudit();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51923", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
}
