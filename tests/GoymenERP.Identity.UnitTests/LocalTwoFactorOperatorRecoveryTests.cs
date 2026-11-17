using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Identity.UnitTests;

/// <summary>SENTETIK. No in-memory transaction is used as proof of SQL atomicity.</summary>
public sealed class LocalTwoFactorOperatorRecoveryTests
{
    [Fact]
    public void A_global_immutable_attempt_binds_one_key_to_target_operator_and_confirmed_factor_versions_without_company_or_secrets()
    {
        using var owner = Owner();
        var entity = owner.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(LocalTwoFactorOperatorRecoveryAttempt))!;
        Assert.Equal(("identity", "LocalTwoFactorOperatorRecoveryAttempts"), (entity.GetSchema(), entity.GetTableName()));
        Assert.Null(entity.FindProperty("CompanyId")); Assert.Null(entity.GetDeclaredQueryFilters().SingleOrDefault());
        var key = Assert.Single(entity.GetIndexes(), index => index.GetDatabaseName() == "UX_LocalTwoFactorOperatorRecoveryAttempts_KeyHash");
        Assert.True(key.IsUnique); Assert.Equal(["KeyHash"], key.Properties.Select(property => property.Name));
        Assert.Equal(typeof(LocalCredential), Assert.Single(entity.GetForeignKeys()).PrincipalEntityType.ClrType);
        Assert.Equal(IdentityDbContext.OperatorTwoFactorRecoveryCheck, Assert.Single(entity.GetCheckConstraints()).Sql);
        Assert.DoesNotContain(entity.GetProperties(), property => property.Name.Contains("Password") || property.Name.Contains("Stamp") || property.Name == "ProtectedSecret");
    }

    [Fact]
    public async Task Modified_or_deleted_attempts_are_refused_before_opening_a_connection()
    {
        foreach (var state in new[] { EntityState.Modified, EntityState.Deleted })
        {
            await using var owner = Owner(); var attempt = Attempt();
            owner.Attach(attempt); owner.Entry(attempt).State = state;
            await Assert.ThrowsAsync<InvalidOperationException>(() => owner.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(2, 2)] [InlineData(2, 4)]
    public void A_success_cannot_claim_an_unconfirmed_or_unchanged_factor_generation(long secretBefore, long secretAfter)
    {
        Assert.Throws<ArgumentException>(() => new LocalTwoFactorOperatorRecoveryAttempt(Guid.NewGuid(), Guid.NewGuid(), Hex, Hex, Hex, Hex,
            "SENTETIK-reason", 1, 2, secretBefore, true, LocalTwoFactorOperatorRecoveryOutcome.Recovered, 3, secretAfter, DateTimeOffset.UtcNow, "SENTETIK-correlation"));
    }

    [Theory]
    [InlineData(false, true, LocalTwoFactorOperatorRecoveryStatusV1.InvalidInvocation)]
    [InlineData(true, false, LocalTwoFactorOperatorRecoveryStatusV1.NotInteractive)]
    public async Task Invalid_argv_and_noninteractive_use_record_a_closed_refusal_before_any_target_or_database_read(bool valid, bool interactive,
        LocalTwoFactorOperatorRecoveryStatusV1 expected)
    {
        var writer = new Refusals(); var console = new UnreadConsole(interactive);
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-correlation", null, SourceChannels.System, null, null);
        var service = new LocalTwoFactorOperatorRecoveryServiceV1(OwnerOptions(), new NeverAudit(), new NeverSecurity(), writer, correlation, TimeProvider.System);
        Assert.Equal(expected, (await service.RunAsync(valid, console)).Status);
        Assert.Equal(0, console.Reads);
        var signal = Assert.Single(writer.Events);
        Assert.Equal(SecurityEventTypesV1.OperatorTwoFactorRecoveryRefused, signal.EventType);
        Assert.Null(signal.CompanyId); Assert.Null(signal.PermissionCode); Assert.Null(signal.RequestedCompanyId);
        Assert.Equal(LocalTwoFactorOperatorRecoverySecurityV1.Actor, signal.ActorId);
    }
    [Fact]
    public void Recovery_debug_values_do_not_include_operator_or_confirmation_contents()
    {
        var op = new LocalTwoFactorRecoveryOperatorDeclarationV1("SENTETIK-user", "SENTETIK-machine");
        Assert.Equal(nameof(LocalTwoFactorRecoveryOperatorDeclarationV1), op.ToString());
        Assert.Equal(nameof(LocalTwoFactorOperatorRecoveryAttempt), Attempt().ToString());
    }
    [Fact]
    public void The_EF_migration_creates_one_global_journal_and_only_the_reviewed_immutable_trigger_with_a_first_Down_guard()
    {
        var migration = new AddOperatorTwoFactorRecoveryAttempts();
        Assert.Equal("LocalTwoFactorOperatorRecoveryAttempts", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        Assert.Contains("AFTER UPDATE, DELETE", Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51925", Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51922", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
    private const string Hex = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static LocalTwoFactorOperatorRecoveryAttempt Attempt() => new(Guid.NewGuid(), Guid.NewGuid(), Hex, Hex, Hex, Hex,
        "SENTETIK-reason", 1, 2, 1, true, LocalTwoFactorOperatorRecoveryOutcome.Recovered, 3, 2, DateTimeOffset.UtcNow, "SENTETIK-correlation");
    private static DbContextOptions<IdentityDbContext> OwnerOptions() => new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer("Server=model-only;Database=model-only;Integrated Security=True;TrustServerCertificate=True").Options;
    private static IdentityDbContext Owner() => new(OwnerOptions());
    private sealed class UnreadConsole(bool interactive) : ILocalTwoFactorOperatorRecoveryConsoleV1
    {
        public bool IsInteractive => interactive; public int Reads { get; private set; }
        public string? ReadTargetUserName() { Reads++; throw new InvalidOperationException(); }
        public string? ReadIdempotencyKey() => throw new InvalidOperationException();
        public string? ReadReason() => throw new InvalidOperationException();
        public LocalTwoFactorRecoveryOperatorDeclarationV1? ReadOperatorDeclaration() => throw new InvalidOperationException();
        public bool Confirm(LocalTwoFactorOperatorRecoveryConfirmationV1 confirmation) => throw new InvalidOperationException();
    }
    private sealed class NeverAudit : ILocalTwoFactorOperatorRecoveryAuditV1
    { public Task AppendAsync(System.Data.IDbTransaction tx, LocalTwoFactorOperatorRecoveryAuditV1 record, CancellationToken ct = default) => throw new InvalidOperationException(); }
    private sealed class NeverSecurity : ILocalTwoFactorOperatorRecoverySecurityWriterV1
    { public Task AppendAsync(System.Data.IDbTransaction tx, LocalTwoFactorOperatorRecoverySecurityV1 record, CancellationToken ct = default) => throw new InvalidOperationException(); }
    private sealed class Refusals : ISecurityEventWriterV1
    {
        public List<SecurityEventV1> Events { get; } = [];
        public Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken ct = default)
        { Events.Add(securityEvent); return Task.FromResult(SecurityEventWriteResultV1.Written(SecurityEventWriteFailureMode.FailOpenWithAlarm)); }
    }
}
