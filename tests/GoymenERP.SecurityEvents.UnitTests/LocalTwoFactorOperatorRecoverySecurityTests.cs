using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.SecurityEvents.UnitTests;

public sealed class LocalTwoFactorOperatorRecoverySecurityTests
{
    [Fact]
    public void The_EF_migration_is_pure_DDL_and_its_Down_refuses_existing_closed_operator_events_first()
    {
        var migration = new AddOperatorTwoFactorRecoverySecurityEvents();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51924", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
    [Theory]
    [InlineData(true, SecurityEventWriteFailureMode.FailClosed)]
    [InlineData(false, SecurityEventWriteFailureMode.FailOpenWithAlarm)]
    public void The_operator_recovery_signal_is_closed_global_not_aggregated_and_uses_existing_security_retention(bool success, SecurityEventWriteFailureMode failure)
    {
        var signal = new LocalTwoFactorOperatorRecoverySecurityV1(Guid.NewGuid(), success, DateTimeOffset.UtcNow, "SENTETIK-correlation", null).ToEvent();
        Assert.Empty(SecurityEventV1Validator.Validate(signal));
        Assert.Equal(failure, SecurityEventWriteFailureModesV1.Resolve(signal.EventType, signal.Phase));
        Assert.Equal(SecurityEventRetentionClassesV1.No134, SecurityEventRetentionClassesV1.ForEventType(signal.EventType));
        Assert.False(SecurityEventAggregationEligibilityV1.IsEligible(signal.EventType, signal.Phase));
        Assert.NotEmpty(SecurityEventV1Validator.Validate(signal with { CompanyId = Guid.NewGuid() }));
        Assert.NotEmpty(SecurityEventV1Validator.Validate(signal with { ActorId = "SENTETIK-other" }));
        Assert.NotEmpty(SecurityEventV1Validator.Validate(signal with { PermissionCode = "identity.roles.manage" }));
        Assert.NotEmpty(SecurityEventV1Validator.Validate(signal with { SourceChannel = "Web" }));
        Assert.NotEmpty(SecurityEventV1Validator.Validate(signal with { Outcome = success ? "Denied" : "Allowed" }));
    }
}
