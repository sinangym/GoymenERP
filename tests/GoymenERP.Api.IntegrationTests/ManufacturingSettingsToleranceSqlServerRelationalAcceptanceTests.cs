using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingSettingsToleranceSqlServerRelationalAcceptanceTests
{
    private const string PredecessorMigration = "20261010095724_AddManufacturingTolerancePolicyRegistry";
    private const string SerializationMigration = "20261015150000_SerializeManufacturingTolerancePolicyWindows";
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private const string Maker = "20000000-0000-0000-0000-0000000000a1";
    private const string Checker = "20000000-0000-0000-0000-0000000000b2";

    [SqlServerFact]
    public async Task Lifecycle_commands_are_authorized_replay_safe_audited_and_do_not_activate_runtime_policy()
    {
        await InDisposableDatabaseAsync(async connectionString =>
        {
            await AssertLifecycleProtectionTriggerEnabledAsync(connectionString);
            var profileId = Guid.NewGuid();
            var prepare = Prepare(profileId, Maker, expectedVersion: 0, "settings-profile-prepare-0001");
            await using (var maker = Command(connectionString, CompanyA, Maker, new GrantingAuthority()))
            {
                var prepared = await maker.Service.PrepareAsync(prepare);
                Assert.False(prepared.IsReplay);
                Assert.Equal((1, ManufacturingToleranceProfileStatusV1.Draft),
                    (prepared.Profile.Lifecycle.StateVersion, prepared.Profile.Lifecycle.Status));

                var selfApproval = await Assert.ThrowsAsync<ManufacturingToleranceProfilePersistenceException>(() =>
                    maker.Service.ApproveAsync(Transition(profileId, Maker, 1,
                        "settings-profile-self-approve-0001", "maker cannot approve")));
                Assert.Equal(ManufacturingToleranceProfileLifecycleCodesV1.MakerSeparationRequired,
                    selfApproval.Code);
            }

            await using (var denied = Command(connectionString, CompanyA, Checker,
                new GrantingAuthority(ManufacturingToleranceProfileActionsV1.Prepare)))
            {
                var refusal = await Assert.ThrowsAsync<ManufacturingToleranceProfilePersistenceException>(() =>
                    denied.Service.ApproveAsync(Transition(profileId, Checker, 1,
                        "settings-profile-denied-approve-0001", "independent permission evidence")));
                Assert.Equal(ManufacturingToleranceProfilePersistenceCodesV1.Forbidden, refusal.Code);
            }

            await using (var checker = Command(connectionString, CompanyA, Checker, new GrantingAuthority()))
            {
                var approved = await checker.Service.ApproveAsync(Transition(profileId, Checker, 1,
                    "settings-profile-approve-0001", "checker approval evidence"));
                Assert.Equal((2, ManufacturingToleranceProfileStatusV1.Approved),
                    (approved.Profile.Lifecycle.StateVersion, approved.Profile.Lifecycle.Status));

                var published = await checker.Service.PublishAsync(Transition(profileId, Checker, 2,
                    "settings-profile-publish-0001", "checker publication evidence"));
                Assert.Equal((3, ManufacturingToleranceProfileStatusV1.Published),
                    (published.Profile.Lifecycle.StateVersion, published.Profile.Lifecycle.Status));
            }

            await using (var replay = Command(connectionString, CompanyA, Maker, new GrantingAuthority()))
            {
                var result = await replay.Service.PrepareAsync(prepare);
                Assert.True(result.IsReplay);
                Assert.Equal("correlation-settings-profile-prepare-0001", result.OriginalCorrelationId);
                Assert.Equal(ManufacturingToleranceProfileStatusV1.Draft, result.Profile.Lifecycle.Status);

                var changedPayload = prepare with { MaximumFirePercent = 1.500001m };
                Assert.Equal(ManufacturingToleranceProfilePersistenceCodesV1.IdempotencyConflict,
                    (await Assert.ThrowsAsync<ManufacturingToleranceProfilePersistenceException>(() =>
                        replay.Service.PrepareAsync(changedPayload))).Code);
            }

            await using (var changedActor = Command(connectionString, CompanyA, Checker, new GrantingAuthority()))
            {
                var sameKey = prepare with
                {
                    Context = prepare.Context with { ActorId = Checker },
                };
                Assert.Equal(ManufacturingToleranceProfilePersistenceCodesV1.IdempotencyConflict,
                    (await Assert.ThrowsAsync<ManufacturingToleranceProfilePersistenceException>(() =>
                        changedActor.Service.PrepareAsync(sameKey))).Code);
            }

            Assert.Equal(1, await CountTableAsync(connectionString, "settings", "ManufacturingToleranceProfileLifecycles"));
            Assert.Equal(3, await CountSettingsCommandsAsync(connectionString));
            Assert.Equal(3, await CountSettingsAuditsAsync(connectionString));
            var prepareAuditVersions = await AuditVersionsAsync(connectionString, profileId, "Prepare", 1);
            Assert.Null(prepareAuditVersions.Before);
            Assert.Equal(1, prepareAuditVersions.After);
            var evidence = await LifecycleEvidenceAsync(connectionString, CompanyA, profileId);
            Assert.Equal((Maker, "prepare reason", Checker, "checker approval evidence", Checker,
                "checker publication evidence", 3L), evidence);
            await AssertLifecycleProtectionTriggerEnabledAsync(connectionString);

            await using (var foreign = Command(connectionString, CompanyB, Checker, new GrantingAuthority()))
            {
                var missing = await Assert.ThrowsAsync<ManufacturingToleranceProfilePersistenceException>(() =>
                    foreign.Service.PublishAsync(Transition(profileId, Checker, 3,
                        "settings-profile-foreign-0001", "foreign company evidence", CompanyB)));
                Assert.Equal(ManufacturingToleranceProfilePersistenceCodesV1.NotFound, missing.Code);
            }

            await using (var provider = Provider(connectionString, CompanyA))
            {
                var unavailable = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => provider.Value
                    .GetManufacturingToleranceAsync(SettingsManufacturingProcessesV1.Slitting,
                        new DateOnly(2030, 6, 1)));
                Assert.Equal("settings_provider_unavailable", unavailable.Code);
            }

            await AssertCommandRejectedAsync(connectionString,
                "UPDATE [settings].[ManufacturingToleranceProfileLifecycles] SET [MaximumFirePercent] = 2 WHERE [CompanyId] = @company AND [ProfileId] = @profile",
                CompanyA, profileId, 51010);
            await AssertCommandRejectedAsync(connectionString,
                "DELETE FROM [settings].[ManufacturingToleranceProfileLifecycles] WHERE [CompanyId] = @company AND [ProfileId] = @profile",
                CompanyA, profileId, 51010);

            await using var migration = Context(connectionString, CompanyA);
            var downgrade = await Assert.ThrowsAsync<SqlException>(() => migration.GetService<IMigrator>()
                .MigrateAsync(SerializationMigration));
            Assert.Equal(51012, downgrade.Number);
            Assert.Equal(1, await CountTableAsync(connectionString, "settings", "ManufacturingToleranceProfileLifecycles"));
        });
    }

    [SqlServerFact]
    public async Task Initial_prepare_and_revision_audit_exact_versions()
    {
        await InDisposableDatabaseAsync(async connectionString =>
        {
            var profileId = Guid.NewGuid();
            await using (var maker = Command(connectionString, CompanyA, Maker, new GrantingAuthority()))
            {
                await maker.Service.PrepareAsync(Prepare(
                    profileId, Maker, 0, "settings-profile-audit-create"));
                await maker.Service.PrepareAsync(Prepare(
                    profileId, Maker, 1, "settings-profile-audit-revise", maximumFirePercent: 1.500000m));
            }

            var create = await AuditVersionsAsync(connectionString, profileId, "Prepare", 1);
            Assert.Null(create.Before);
            Assert.Equal(1, create.After);
            var revision = await AuditVersionsAsync(connectionString, profileId, "Prepare", 2);
            Assert.Equal(1, revision.Before);
            Assert.Equal(2, revision.After);
        });
    }

    [SqlServerFact]
    public async Task Concurrent_same_key_prepare_has_one_original_one_replay_and_one_atomic_effect()
    {
        await InDisposableDatabaseAsync(async connectionString =>
        {
            var profileId = Guid.NewGuid();
            var command = Prepare(
                profileId,
                Maker,
                0,
                "settings-profile-same-key-concurrent-prepare",
                profileVersion: 100,
                process: SettingsManufacturingProcessesV1.CutToLength);
            await using var firstMaker = Command(
                connectionString, CompanyA, Maker, new GrantingAuthority());
            await using var secondMaker = Command(
                connectionString, CompanyA, Maker, new GrantingAuthority());

            var outcomes = await Task.WhenAll(
                    firstMaker.Service.PrepareAsync(command),
                    secondMaker.Service.PrepareAsync(command))
                .WaitAsync(TimeSpan.FromSeconds(30));

            var original = Assert.Single(outcomes, outcome => !outcome.IsReplay);
            var replay = Assert.Single(outcomes, outcome => outcome.IsReplay);
            Assert.Equal(command.Context.CorrelationId, original.OriginalCorrelationId);
            Assert.Equal(original.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.All(outcomes, outcome => Assert.Equal(profileId, outcome.Profile.Lifecycle.ProfileId));
            Assert.Equal(1, await CountTableAsync(
                connectionString, "settings", "ManufacturingToleranceProfileLifecycles"));
            Assert.Equal(1, await CountSettingsCommandsAsync(connectionString));
            Assert.Equal(1, await CountSettingsAuditsAsync(connectionString));
        });
    }

    [SqlServerFact]
    public async Task Authority_revocation_before_save_or_replay_writes_nothing()
    {
        await InDisposableDatabaseAsync(async connectionString =>
        {
            var rejected = Prepare(Guid.NewGuid(), Maker, 0, "settings-profile-revoked-save-0001");
            await using (var revokedBeforeSave = Command(connectionString, CompanyA, Maker,
                new SequencedAuthority(true, false)))
            {
                var refusal = await Assert.ThrowsAsync<ManufacturingToleranceProfilePersistenceException>(() =>
                    revokedBeforeSave.Service.PrepareAsync(rejected));
                Assert.Equal(ManufacturingToleranceProfilePersistenceCodesV1.Forbidden, refusal.Code);
            }

            Assert.Equal(0, await CountTableAsync(connectionString, "settings", "ManufacturingToleranceProfileLifecycles"));
            Assert.Equal(0, await CountSettingsCommandsAsync(connectionString));
            Assert.Equal(0, await CountSettingsAuditsAsync(connectionString));

            var accepted = Prepare(Guid.NewGuid(), Maker, 0, "settings-profile-revoked-replay-0001");
            await using (var writer = Command(connectionString, CompanyA, Maker, new GrantingAuthority()))
            {
                await writer.Service.PrepareAsync(accepted);
            }

            await using (var revokedBeforeReplay = Command(connectionString, CompanyA, Maker,
                new SequencedAuthority(true, false)))
            {
                var refusal = await Assert.ThrowsAsync<ManufacturingToleranceProfilePersistenceException>(() =>
                    revokedBeforeReplay.Service.PrepareAsync(accepted));
                Assert.Equal(ManufacturingToleranceProfilePersistenceCodesV1.Forbidden, refusal.Code);
            }

            Assert.Equal(1, await CountTableAsync(connectionString, "settings", "ManufacturingToleranceProfileLifecycles"));
            Assert.Equal(1, await CountSettingsCommandsAsync(connectionString));
            Assert.Equal(1, await CountSettingsAuditsAsync(connectionString));
        });
    }

    [SqlServerFact]
    public async Task Mandatory_lifecycle_provenance_rejects_blank_direct_sql_values()
    {
        await InDisposableDatabaseAsync(async connectionString =>
        {
            var whitespaceOnlyValues = new[]
            {
                "   ",
                "\t\t",
                "\r\n\r\n",
                " \t\r\n\u00a0",
                "\v\f",
                "\u0085",
                "\u1680",
                "\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a",
                "\u2028\u2029",
                "\u202f",
                "\u205f",
                "\u3000",
            };
            Assert.All(whitespaceOnlyValues, value => Assert.True(string.IsNullOrWhiteSpace(value)));
            var preparationFields = new[]
            {
                "PreparedBy",
                "PreparationReason",
                "PreparationCorrelationId",
                "PreparationSourceChannel",
            };
            var approvalFields = new[]
            {
                "ApprovedBy",
                "ApprovalReason",
                "ApprovalCorrelationId",
                "ApprovalSourceChannel",
            };
            var publicationFields = new[]
            {
                "PublishedBy",
                "PublicationReason",
                "PublicationCorrelationId",
                "PublicationSourceChannel",
            };

            foreach (var whitespaceOnlyValue in whitespaceOnlyValues)
            {
                foreach (var field in preparationFields)
                {
                    await AssertBlankPreparationProvenanceRejectedAsync(
                        connectionString,
                        field,
                        whitespaceOnlyValue);
                }
            }

            Assert.Equal(0,
                await CountTableAsync(connectionString, "settings", "ManufacturingToleranceProfileLifecycles"));
            Assert.Equal(0, await CountSettingsAuditsAsync(connectionString));

            var profileId = Guid.NewGuid();
            await InsertLifecycleDraftAsync(connectionString, profileId);
            foreach (var whitespaceOnlyValue in whitespaceOnlyValues)
            {
                foreach (var field in approvalFields)
                {
                    await AssertBlankApprovalProvenanceRejectedAsync(
                        connectionString,
                        profileId,
                        field,
                        whitespaceOnlyValue);
                }
            }

            Assert.Equal((1L, ManufacturingToleranceProfileStatusV1.Draft),
                await LifecycleStateAsync(connectionString, profileId));
            Assert.Equal(0, await CountSettingsAuditsAsync(connectionString));

            await ApplyLifecycleApprovalAsync(connectionString, profileId);
            foreach (var whitespaceOnlyValue in whitespaceOnlyValues)
            {
                foreach (var field in publicationFields)
                {
                    await AssertBlankPublicationProvenanceRejectedAsync(
                        connectionString,
                        profileId,
                        field,
                        whitespaceOnlyValue);
                }
            }

            Assert.Equal((2L, ManufacturingToleranceProfileStatusV1.Approved),
                await LifecycleStateAsync(connectionString, profileId));
            Assert.Equal(0, await CountSettingsAuditsAsync(connectionString));

            await ApplyLifecyclePublicationAsync(connectionString, profileId);
            Assert.Equal((3L, ManufacturingToleranceProfileStatusV1.Published),
                await LifecycleStateAsync(connectionString, profileId));
            Assert.Equal(
                ("direct\u1680SQL valid preparation", "direct\u2003SQL valid approval",
                    "direct\u2028SQL\u2029valid publication"),
                await LifecycleReasonsAsync(connectionString, profileId));
            Assert.Equal(0, await CountSettingsAuditsAsync(connectionString));
        });
    }

    [SqlServerFact]
    public async Task Serializable_publication_and_state_version_races_have_one_winner_and_next_version_is_distinct()
    {
        await InDisposableDatabaseAsync(async connectionString =>
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            await PrepareApprovedAsync(connectionString, first, 1,
                new DateOnly(2031, 1, 1), new DateOnly(2031, 6, 30), "race-a");
            await PrepareApprovedAsync(connectionString, second, 2,
                new DateOnly(2031, 4, 1), new DateOnly(2031, 9, 30), "race-b");

            await using var firstPublisher = Command(connectionString, CompanyA, Checker, new GrantingAuthority());
            await using var secondPublisher = Command(connectionString, CompanyA, Checker, new GrantingAuthority());
            var firstPublication = PublishOutcomeAsync(firstPublisher.Service,
                Transition(first, Checker, 2, "settings-profile-race-publish-a", "race publish a"));
            var secondPublication = PublishOutcomeAsync(secondPublisher.Service,
                Transition(second, Checker, 2, "settings-profile-race-publish-b", "race publish b"));
            var publicationOutcomes = await Task.WhenAll(firstPublication, secondPublication)
                .WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Single(publicationOutcomes, outcome => outcome == "Succeeded");
            Assert.Single(publicationOutcomes,
                outcome => outcome == ManufacturingToleranceProfilePersistenceCodesV1.PublicationOverlap);
            Assert.Equal(1, await CountLifecycleStatusAsync(connectionString,
                ManufacturingToleranceProfileStatusV1.Published));

            var nonOverlapping = Guid.NewGuid();
            await PrepareApprovedAsync(connectionString, nonOverlapping, 10,
                new DateOnly(2031, 10, 1), new DateOnly(2031, 12, 31), "non-overlap");
            await using (var publisher = Command(connectionString, CompanyA, Checker, new GrantingAuthority()))
            {
                var result = await publisher.Service.PublishAsync(Transition(nonOverlapping, Checker, 2,
                    "settings-profile-non-overlap-publish", "non-overlap publish"));
                Assert.Equal(ManufacturingToleranceProfileStatusV1.Published, result.Profile.Lifecycle.Status);
            }
            Assert.Equal(2, await CountLifecycleStatusAsync(connectionString,
                ManufacturingToleranceProfileStatusV1.Published));

            var approvalRace = Guid.NewGuid();
            await using (var maker = Command(connectionString, CompanyA, Maker, new GrantingAuthority()))
            {
                await maker.Service.PrepareAsync(Prepare(approvalRace, Maker, 0,
                    "settings-profile-approval-race-prepare", profileVersion: 11,
                    from: new DateOnly(2032, 1, 1), through: new DateOnly(2032, 12, 31)));
            }

            await using var firstChecker = Command(connectionString, CompanyA, Checker, new GrantingAuthority());
            await using var secondChecker = Command(connectionString, CompanyA,
                "20000000-0000-0000-0000-0000000000c3", new GrantingAuthority());
            var firstDecision = ApproveOutcomeAsync(firstChecker.Service,
                Transition(approvalRace, Checker, 1, "settings-profile-approval-race-a", "approval race a"));
            var secondDecision = ApproveOutcomeAsync(secondChecker.Service,
                Transition(approvalRace, "20000000-0000-0000-0000-0000000000c3", 1,
                    "settings-profile-approval-race-b", "approval race b"));
            var decisionOutcomes = await Task.WhenAll(firstDecision, secondDecision)
                .WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Single(decisionOutcomes, outcome => outcome == "Succeeded");
            Assert.Single(decisionOutcomes,
                outcome => outcome == ManufacturingToleranceProfilePersistenceCodesV1.VersionConflict);

            var publishedPredecessor = Guid.NewGuid();
            await PrepareApprovedAsync(connectionString, publishedPredecessor, 1,
                new DateOnly(2033, 1, 1), new DateOnly(2033, 6, 30), "next-version",
                SettingsManufacturingProcessesV1.Paint);
            await using (var publisher = Command(connectionString, CompanyA, Checker, new GrantingAuthority()))
            {
                await publisher.Service.PublishAsync(Transition(publishedPredecessor, Checker, 2,
                    "settings-profile-next-publish", "next predecessor publish"));
            }

            var nextId = Guid.NewGuid();
            await using (var maker = Command(connectionString, CompanyA, Maker, new GrantingAuthority()))
            {
                var next = await maker.Service.PrepareNextVersionAsync(
                    new PrepareNextManufacturingToleranceProfileVersionCommandV1(
                        publishedPredecessor,
                        nextId,
                        1.375000m,
                        new DateOnly(2033, 7, 1),
                        new DateOnly(2033, 12, 31),
                        Context(CompanyA, Maker, 3, "settings-profile-next-prepare", "next version prepare")));
                Assert.Equal((nextId, 2L, 1L, ManufacturingToleranceProfileStatusV1.Draft),
                    (next.Profile.Lifecycle.ProfileId, next.Profile.Lifecycle.ProfileVersion,
                        next.Profile.Lifecycle.StateVersion, next.Profile.Lifecycle.Status));

                var sequentialConflict = await Assert.ThrowsAsync<ManufacturingToleranceProfilePersistenceException>(() =>
                    maker.Service.PrepareNextVersionAsync(
                        new PrepareNextManufacturingToleranceProfileVersionCommandV1(
                            publishedPredecessor,
                            Guid.NewGuid(),
                            1.500000m,
                            new DateOnly(2034, 1, 1),
                            new DateOnly(2034, 6, 30),
                            Context(CompanyA, Maker, 3, "settings-profile-next-sequential-conflict",
                                "second successor must conflict"))));
                Assert.Equal(ManufacturingToleranceProfilePersistenceCodesV1.VersionConflict,
                    sequentialConflict.Code);
                Assert.Equal(0, sequentialConflict.ExpectedStateVersion);
                Assert.Equal(1, sequentialConflict.CurrentStateVersion);

                var sameIdentity = await Assert.ThrowsAsync<ManufacturingToleranceProfilePersistenceException>(() =>
                    maker.Service.PrepareNextVersionAsync(
                        new PrepareNextManufacturingToleranceProfileVersionCommandV1(
                            publishedPredecessor,
                            publishedPredecessor,
                            1.375000m,
                            new DateOnly(2034, 1, 1),
                            null,
                            Context(CompanyA, Maker, 3, "settings-profile-next-same-id", "same id refusal"))));
                Assert.Equal(ManufacturingToleranceProfilePersistenceCodesV1.Invalid, sameIdentity.Code);
            }

            var concurrentPredecessor = Guid.NewGuid();
            await PrepareApprovedAsync(connectionString, concurrentPredecessor, 20,
                new DateOnly(2035, 1, 1), new DateOnly(2035, 6, 30), "next-version-race",
                SettingsManufacturingProcessesV1.Paint);
            await using (var publisher = Command(connectionString, CompanyA, Checker, new GrantingAuthority()))
            {
                await publisher.Service.PublishAsync(Transition(concurrentPredecessor, Checker, 2,
                    "settings-profile-next-race-publish", "next race predecessor publish"));
            }

            await using var firstMaker = Command(connectionString, CompanyA, Maker, new GrantingAuthority());
            await using var secondMaker = Command(connectionString, CompanyA, Maker, new GrantingAuthority());
            var firstSuccessor = PrepareNextOutcomeAsync(firstMaker.Service,
                new PrepareNextManufacturingToleranceProfileVersionCommandV1(
                    concurrentPredecessor,
                    Guid.NewGuid(),
                    1.625000m,
                    new DateOnly(2035, 7, 1),
                    new DateOnly(2035, 12, 31),
                    Context(CompanyA, Maker, 3, "settings-profile-next-race-a", "next race a")));
            var secondSuccessor = PrepareNextOutcomeAsync(secondMaker.Service,
                new PrepareNextManufacturingToleranceProfileVersionCommandV1(
                    concurrentPredecessor,
                    Guid.NewGuid(),
                    1.750000m,
                    new DateOnly(2035, 7, 1),
                    new DateOnly(2035, 12, 31),
                    Context(CompanyA, Maker, 3, "settings-profile-next-race-b", "next race b")));
            var successorOutcomes = await Task.WhenAll(firstSuccessor, secondSuccessor)
                .WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Single(successorOutcomes, outcome => outcome == "Succeeded");
            Assert.Single(successorOutcomes,
                outcome => outcome == ManufacturingToleranceProfilePersistenceCodesV1.VersionConflict);
            Assert.Equal(1, await CountLifecycleVersionAsync(
                connectionString, SettingsManufacturingProcessesV1.Paint, 21));
        });
    }

    [SqlServerFact]
    public async Task Approved_registry_is_company_scoped_versioned_audited_and_fail_closed()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);
        try
        {
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();

            await using var empty = Provider(factory.DatabaseConnectionString, CompanyA);
            Assert.Equal("settings_provider_unavailable",
                (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => empty.Value
                    .GetManufacturingToleranceAsync(SettingsManufacturingProcessesV1.Slitting,
                        new DateOnly(2026, 10, 10)))).Code);

            await InsertAsync(factory.DatabaseConnectionString, CompanyA, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.Paint, 1, 0.500000m, true,
                new DateOnly(2026, 1, 1), null, "synthetic-settings-sql-actor", "synthetic SQL acceptance only");
            await InsertAsync(factory.DatabaseConnectionString, CompanyB, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.Slitting, 1, 9.000000m, true,
                new DateOnly(2026, 1, 1), null, "synthetic-settings-sql-actor", "foreign company isolation");

            await using var missing = Provider(factory.DatabaseConnectionString, CompanyA);
            Assert.Equal(SettingsPrerequisiteCodesV1.ManufacturingToleranceProfileNotFound,
                (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => missing.Value
                    .GetManufacturingToleranceAsync(SettingsManufacturingProcessesV1.Slitting,
                        new DateOnly(2026, 10, 10)))).Code);

            var profileId = Guid.NewGuid();
            await InsertAsync(factory.DatabaseConnectionString, CompanyA, profileId,
                SettingsManufacturingProcessesV1.Slitting, 7, 1.234567m, false,
                new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
                "synthetic-settings-sql-approver", "future inactive profile evidence");

            await using var exact = Provider(factory.DatabaseConnectionString, CompanyA);
            var result = await exact.Value.GetManufacturingToleranceAsync(
                SettingsManufacturingProcessesV1.Slitting, new DateOnly(2026, 10, 10));
            Assert.Equal(CompanyA, result.CompanyId);
            Assert.Equal(profileId, result.Profile.ProfileId);
            Assert.Equal(7, result.Profile.Version);
            Assert.Equal(1.234567m, result.MaximumFirePercent);
            Assert.False(result.IsActive);
            Assert.Equal(new DateOnly(2027, 1, 1), result.EffectiveFrom);
            Assert.Equal(new DateOnly(2027, 12, 31), result.EffectiveThrough);

            await AssertInsertRejectedAsync(factory.DatabaseConnectionString, CompanyA, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.Slitting, 8, 2m, true,
                new DateOnly(2027, 6, 1), null, "synthetic-settings-sql-approver", "overlap", 51001);
            await AssertInsertRejectedAsync(factory.DatabaseConnectionString, CompanyA, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.ColdRolling, 1, 2m, true,
                new DateOnly(2026, 1, 1), null, "", "missing approval actor");

            var fixedVersionProfile = Guid.NewGuid();
            await InsertAsync(factory.DatabaseConnectionString, CompanyA, fixedVersionProfile,
                SettingsManufacturingProcessesV1.CutToLength, 1, 0.750000m, true,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31),
                "synthetic-settings-sql-approver", "version uniqueness evidence");
            await AssertInsertRejectedAsync(factory.DatabaseConnectionString, CompanyA, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.CutToLength, 1, 0.800000m, true,
                new DateOnly(2026, 2, 1), null,
                "synthetic-settings-sql-approver", "duplicate version must fail");

            await AssertIncomingBatchOverlapRejectedAsync(factory.DatabaseConnectionString);

            const int contentionRounds = 4;
            for (var round = 0; round < contentionRounds; round++)
            {
                var year = 2028 + round;
                await AssertContendedOverlapRejectedAsync(factory.DatabaseConnectionString,
                    (round * 2) + 1, (round * 2) + 2, year);
            }
            Assert.Equal(contentionRounds, await CountProcessAsync(factory.DatabaseConnectionString,
                SettingsManufacturingProcessesV1.ColdRolling));

            await AssertCommandRejectedAsync(factory.DatabaseConnectionString,
                "UPDATE [settings].[ManufacturingTolerancePolicies] SET [MaximumFirePercent] = 99 WHERE [CompanyId] = @company AND [ProfileId] = @profile",
                CompanyA, profileId, 51002);
            await AssertCommandRejectedAsync(factory.DatabaseConnectionString,
                "DELETE FROM [settings].[ManufacturingTolerancePolicies] WHERE [CompanyId] = @company AND [ProfileId] = @profile",
                CompanyA, profileId, 51002);

            Assert.Equal(4 + contentionRounds, await CountAsync(factory.DatabaseConnectionString));
            await using var rollback = Context(factory.DatabaseConnectionString, CompanyA);
            var rollbackException = await Assert.ThrowsAsync<SqlException>(() =>
                rollback.GetService<IMigrator>().MigrateAsync("0"));
            Assert.Equal(51003, rollbackException.Number);
            Assert.Equal(4 + contentionRounds, await CountAsync(factory.DatabaseConnectionString));
        }
        finally
        {
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(baseConnection, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task Serialization_migration_accepts_valid_predecessor_history_without_mutation()
    {
        await InDisposableDatabaseAsync(async connectionString =>
        {
            await using var context = Context(connectionString, CompanyA);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PredecessorMigration);

            await InsertAsync(connectionString, CompanyA, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.Slitting, 1, 0.625000m, true,
                new DateOnly(2037, 1, 1), new DateOnly(2037, 6, 30),
                "synthetic-settings-sql-approver", "valid predecessor history one");
            await InsertAsync(connectionString, CompanyA, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.Slitting, 2, 0.625000m, true,
                new DateOnly(2037, 7, 1), new DateOnly(2037, 12, 31),
                "synthetic-settings-sql-approver", "valid predecessor history two");

            var before = await HistoryRowsAsync(connectionString);
            var predecessorTrigger = await TriggerStateAsync(connectionString);
            Assert.False(predecessorTrigger.IsInsteadOf);
            Assert.False(predecessorTrigger.IsDisabled);
            Assert.Equal(0, await MigrationHistoryCountAsync(connectionString));

            await migrator.MigrateAsync(SerializationMigration);

            Assert.Equal(before, await HistoryRowsAsync(connectionString));
            Assert.Equal(1, await MigrationHistoryCountAsync(connectionString));
            var serializedTrigger = await TriggerStateAsync(connectionString);
            Assert.True(serializedTrigger.IsInsteadOf);
            Assert.False(serializedTrigger.IsDisabled);
        });
    }

    [SqlServerFact]
    public async Task Serialization_migration_rejects_overlapping_predecessor_history_atomically()
    {
        await InDisposableDatabaseAsync(async connectionString =>
        {
            await using var context = Context(connectionString, CompanyA);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PredecessorMigration);

            await SetTriggerEnabledAsync(connectionString, enabled: false);
            try
            {
                await InsertAsync(connectionString, CompanyA, Guid.NewGuid(),
                    SettingsManufacturingProcessesV1.Paint, 1, 0.625000m, true,
                    new DateOnly(2038, 1, 1), new DateOnly(2038, 12, 31),
                    "synthetic-settings-sql-approver", "explicit disposable legacy overlap one");
                await InsertAsync(connectionString, CompanyA, Guid.NewGuid(),
                    SettingsManufacturingProcessesV1.Paint, 2, 0.625000m, true,
                    new DateOnly(2038, 6, 1), new DateOnly(2038, 12, 31),
                    "synthetic-settings-sql-approver", "explicit disposable legacy overlap two");
            }
            finally
            {
                await SetTriggerEnabledAsync(connectionString, enabled: true);
            }

            var before = await HistoryRowsAsync(connectionString);
            var predecessorTrigger = await TriggerStateAsync(connectionString);
            Assert.False(predecessorTrigger.IsInsteadOf);
            Assert.False(predecessorTrigger.IsDisabled);
            Assert.Equal(2, await CountProcessAsync(connectionString, SettingsManufacturingProcessesV1.Paint));
            Assert.Equal(0, await MigrationHistoryCountAsync(connectionString));

            var error = await Assert.ThrowsAsync<SqlException>(() =>
                migrator.MigrateAsync(SerializationMigration));

            Assert.Equal(51004, error.Number);
            Assert.Contains("settings_manufacturing_tolerance_existing_history_overlap", error.Message,
                StringComparison.Ordinal);
            Assert.Equal(0, await MigrationHistoryCountAsync(connectionString));
            Assert.Equal(before, await HistoryRowsAsync(connectionString));
            Assert.Equal(predecessorTrigger, await TriggerStateAsync(connectionString));
        });
    }

    [SqlServerFact]
    public async Task Empty_registry_can_downgrade_to_predecessor_and_reapply_serialization()
    {
        await InDisposableDatabaseAsync(async connectionString =>
        {
            await using var context = Context(connectionString, CompanyA);
            var migrator = context.GetService<IMigrator>();
            Assert.Equal(1, await MigrationHistoryCountAsync(connectionString));
            Assert.True((await TriggerStateAsync(connectionString)).IsInsteadOf);

            await migrator.MigrateAsync(PredecessorMigration);

            Assert.Equal(0, await MigrationHistoryCountAsync(connectionString));
            var predecessorTrigger = await TriggerStateAsync(connectionString);
            Assert.False(predecessorTrigger.IsInsteadOf);
            Assert.False(predecessorTrigger.IsDisabled);
            Assert.Equal([], await HistoryRowsAsync(connectionString));

            await migrator.MigrateAsync(SerializationMigration);

            Assert.Equal(1, await MigrationHistoryCountAsync(connectionString));
            var serializedTrigger = await TriggerStateAsync(connectionString);
            Assert.True(serializedTrigger.IsInsteadOf);
            Assert.False(serializedTrigger.IsDisabled);
            Assert.Equal([], await HistoryRowsAsync(connectionString));
        });
    }

    private static PrepareManufacturingToleranceProfileCommandV1 Prepare(
        Guid profileId,
        string actor,
        long expectedVersion,
        string idempotencyKey,
        Guid? company = null,
        long profileVersion = 1,
        DateOnly? from = null,
        DateOnly? through = null,
        decimal maximumFirePercent = 1.250000m,
        string process = SettingsManufacturingProcessesV1.Slitting) => new(
        profileId,
        process,
        profileVersion,
        maximumFirePercent,
        from ?? new DateOnly(2030, 1, 1),
        through ?? new DateOnly(2030, 12, 31),
        Context(company ?? CompanyA, actor, expectedVersion, idempotencyKey, "prepare reason"));

    private static TransitionManufacturingToleranceProfileCommandV1 Transition(
        Guid profileId,
        string actor,
        long expectedVersion,
        string idempotencyKey,
        string reason,
        Guid? company = null) => new(
        profileId,
        Context(company ?? CompanyA, actor, expectedVersion, idempotencyKey, reason));

    private static ManufacturingToleranceProfilePersistenceContextV1 Context(
        Guid company,
        string actor,
        long expectedVersion,
        string idempotencyKey,
        string reason) => new(
        company,
        actor,
        expectedVersion,
        idempotencyKey,
        reason,
        new DateTimeOffset(2026, 10, 16, 12, (int)(expectedVersion % 60), 0, TimeSpan.Zero),
        $"correlation-{idempotencyKey}",
        $"causation-{idempotencyKey}",
        "Web",
        "127.0.0.1",
        "synthetic-settings-sql-agent");

    private static async Task PrepareApprovedAsync(
        string connectionString,
        Guid profileId,
        long profileVersion,
        DateOnly from,
        DateOnly through,
        string keySuffix,
        string process = SettingsManufacturingProcessesV1.Slitting)
    {
        await using (var maker = Command(connectionString, CompanyA, Maker, new GrantingAuthority()))
        {
            await maker.Service.PrepareAsync(Prepare(profileId, Maker, 0,
                $"settings-profile-{keySuffix}-prepare", profileVersion: profileVersion,
                from: from, through: through, process: process));
        }

        await using var checker = Command(connectionString, CompanyA, Checker, new GrantingAuthority());
        await checker.Service.ApproveAsync(Transition(profileId, Checker, 1,
            $"settings-profile-{keySuffix}-approve", $"{keySuffix} approval"));
    }

    private static async Task<string> PublishOutcomeAsync(
        SettingsManufacturingToleranceProfileCommandServiceV1 service,
        TransitionManufacturingToleranceProfileCommandV1 command)
    {
        try
        {
            await service.PublishAsync(command);
            return "Succeeded";
        }
        catch (ManufacturingToleranceProfilePersistenceException exception)
        {
            return exception.Code;
        }
    }

    private static async Task<string> ApproveOutcomeAsync(
        SettingsManufacturingToleranceProfileCommandServiceV1 service,
        TransitionManufacturingToleranceProfileCommandV1 command)
    {
        try
        {
            await service.ApproveAsync(command);
            return "Succeeded";
        }
        catch (ManufacturingToleranceProfilePersistenceException exception)
        {
            return exception.Code;
        }
    }

    private static async Task<string> PrepareNextOutcomeAsync(
        SettingsManufacturingToleranceProfileCommandServiceV1 service,
        PrepareNextManufacturingToleranceProfileVersionCommandV1 command)
    {
        try
        {
            await service.PrepareNextVersionAsync(command);
            return "Succeeded";
        }
        catch (ManufacturingToleranceProfilePersistenceException exception)
        {
            return exception.Code;
        }
    }

    private static CommandScope Command(
        string connectionString,
        Guid company,
        string actor,
        IErpAuthorityRevalidationService authority)
    {
        var companyContext = new FixedCompany(company);
        var collector = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer(connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
            .Options;
        var database = new SettingsDbContext(options, companyContext);
        var service = new SettingsManufacturingToleranceProfileCommandServiceV1(
            database,
            companyContext,
            new FixedActor(company, actor),
            authority,
            collector);
        return new CommandScope(database, service);
    }

    private static async Task<int> CountTableAsync(
        string connectionString,
        string schema,
        string table)
    {
        var allowed = (schema, table) is
            ("settings", "ManufacturingToleranceProfileLifecycles");
        if (!allowed)
        {
            throw new ArgumentOutOfRangeException(nameof(table));
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM [{schema}].[{table}];";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountSettingsCommandsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM [idempotency].[CommandExecutions]
            WHERE [CommandName] IN
                (N'settings.manufacturing-tolerance-profiles.prepare',
                 N'settings.manufacturing-tolerance-profiles.approve',
                 N'settings.manufacturing-tolerance-profiles.publish');
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountSettingsAuditsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM [audit].[AuditEvents]
            WHERE [EntityType] = N'Settings.ManufacturingToleranceProfileLifecycle';
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<(long? Before, long After)> AuditVersionsAsync(
        string connectionString,
        Guid profileId,
        string operation,
        long after)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [EntityVersionBefore], [EntityVersionAfter]
            FROM [audit].[AuditEvents]
            WHERE [EntityType] = N'Settings.ManufacturingToleranceProfileLifecycle'
              AND [EntityId] = @profile
              AND [Operation] = @operation
              AND [EntityVersionAfter] = @after;
            """;
        command.Parameters.AddWithValue("@profile", profileId.ToString("D"));
        command.Parameters.AddWithValue("@operation", operation);
        command.Parameters.AddWithValue("@after", after);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        long? before = reader.IsDBNull(0) ? null : reader.GetInt64(0);
        return (before, reader.GetInt64(1));
    }

    private static async Task<int> CountLifecycleStatusAsync(
        string connectionString,
        ManufacturingToleranceProfileStatusV1 status)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM [settings].[ManufacturingToleranceProfileLifecycles]
            WHERE [CompanyId] = @company AND [Status] = @status;
            """;
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@status", (int)status);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<(long StateVersion, ManufacturingToleranceProfileStatusV1 Status)>
        LifecycleStateAsync(string connectionString, Guid profileId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [StateVersion], [Status]
            FROM [settings].[ManufacturingToleranceProfileLifecycles]
            WHERE [CompanyId] = @company AND [ProfileId] = @profile;
            """;
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@profile", profileId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), (ManufacturingToleranceProfileStatusV1)reader.GetInt32(1));
    }

    private static async Task<(string PreparationReason, string ApprovalReason, string PublicationReason)>
        LifecycleReasonsAsync(string connectionString, Guid profileId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [PreparationReason], [ApprovalReason], [PublicationReason]
            FROM [settings].[ManufacturingToleranceProfileLifecycles]
            WHERE [CompanyId] = @company AND [ProfileId] = @profile;
            """;
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@profile", profileId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private static async Task<int> CountLifecycleVersionAsync(
        string connectionString,
        string process,
        long profileVersion)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM [settings].[ManufacturingToleranceProfileLifecycles]
            WHERE [CompanyId] = @company AND [Process] = @process AND [ProfileVersion] = @profileVersion;
            """;
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@process", process);
        command.Parameters.AddWithValue("@profileVersion", profileVersion);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<(string PreparedBy, string PreparationReason, string ApprovedBy,
        string ApprovalReason, string PublishedBy, string PublicationReason, long StateVersion)>
        LifecycleEvidenceAsync(string connectionString, Guid company, Guid profileId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [PreparedBy], [PreparationReason], [ApprovedBy], [ApprovalReason],
                   [PublishedBy], [PublicationReason], [StateVersion]
            FROM [settings].[ManufacturingToleranceProfileLifecycles]
            WHERE [CompanyId] = @company AND [ProfileId] = @profile;
            """;
        command.Parameters.AddWithValue("@company", company);
        command.Parameters.AddWithValue("@profile", profileId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt64(6));
    }

    private static ProviderScope Provider(string connectionString, Guid company)
    {
        var companyContext = new FixedCompany(company);
        var database = Context(connectionString, companyContext);
        return new ProviderScope(database,
            new SettingsManufacturingToleranceProviderV1(database, companyContext, new FixedActor(company)));
    }

    private static SettingsDbContext Context(string connectionString, Guid company) =>
        Context(connectionString, new FixedCompany(company));

    private static SettingsDbContext Context(string connectionString, ICompanyContext companyContext)
    {
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer(connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema))
            .Options;
        return new SettingsDbContext(options, companyContext);
    }

    private static async Task InsertAsync(string connectionString, Guid companyId, Guid profileId,
        string process, long version, decimal maximumFirePercent, bool isActive, DateOnly from,
        DateOnly? through, string approvedBy, string reason)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = InsertCommand(connection, companyId, profileId, process, version,
            maximumFirePercent, isActive, from, through, approvedBy, reason);
        await command.ExecuteNonQueryAsync();
    }

    private static SqlCommand InsertCommand(SqlConnection connection, Guid companyId, Guid profileId,
        string process, long version, decimal maximumFirePercent, bool isActive, DateOnly from,
        DateOnly? through, string approvedBy, string reason)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [settings].[ManufacturingTolerancePolicies]
                ([CompanyId],[ProfileId],[Process],[Version],[MaximumFirePercent],[IsActive],
                 [EffectiveFrom],[EffectiveThrough],[ApprovalStatus],[ApprovedBy],[ApprovedAtUtc],[ApprovalReason])
            VALUES (@company,@profile,@process,@version,@percent,@active,@from,@through,
                    'Approved',@approvedBy,@approvedAt,@reason);
            """;
        command.Parameters.AddWithValue("@company", companyId);
        command.Parameters.AddWithValue("@profile", profileId);
        command.Parameters.AddWithValue("@process", process);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@percent", maximumFirePercent);
        command.Parameters.AddWithValue("@active", isActive);
        command.Parameters.AddWithValue("@from", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@through", through is null ? DBNull.Value : through.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@approvedBy", approvedBy);
        command.Parameters.AddWithValue("@approvedAt", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        command.Parameters.AddWithValue("@reason", reason);
        return command;
    }

    private static async Task AssertInsertRejectedAsync(string connectionString, Guid companyId, Guid profileId,
        string process, long version, decimal maximumFirePercent, bool isActive, DateOnly from,
        DateOnly? through, string approvedBy, string reason, int? expectedNumber = null)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(() => InsertAsync(connectionString, companyId,
            profileId, process, version, maximumFirePercent, isActive, from, through, approvedBy, reason));
        if (expectedNumber is not null)
        {
            Assert.Equal(expectedNumber.Value, exception.Number);
        }
    }

    private static async Task AssertIncomingBatchOverlapRejectedAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [settings].[ManufacturingTolerancePolicies]
                ([CompanyId],[ProfileId],[Process],[Version],[MaximumFirePercent],[IsActive],
                 [EffectiveFrom],[EffectiveThrough],[ApprovalStatus],[ApprovedBy],[ApprovedAtUtc],[ApprovalReason])
            VALUES
                (@company,@firstProfile,@process,1,0.625000,1,'2036-01-01','2036-12-31',
                 'Approved',@approvedBy,@approvedAt,@reason),
                (@company,@secondProfile,@process,2,0.625000,1,'2036-06-01','2036-12-31',
                 'Approved',@approvedBy,@approvedAt,@reason);
            """;
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@firstProfile", Guid.NewGuid());
        command.Parameters.AddWithValue("@secondProfile", Guid.NewGuid());
        command.Parameters.AddWithValue("@process", SettingsManufacturingProcessesV1.TubeProfile);
        command.Parameters.AddWithValue("@approvedBy", "synthetic-settings-sql-approver");
        command.Parameters.AddWithValue("@approvedAt",
            new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        command.Parameters.AddWithValue("@reason", "incoming batch overlap evidence");

        var exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(51001, exception.Number);
        Assert.Equal(0, await CountProcessAsync(connectionString, SettingsManufacturingProcessesV1.TubeProfile));
    }

    private static async Task AssertContendedOverlapRejectedAsync(string connectionString,
        long firstVersion, long secondVersion, int year)
    {
        await using var firstConnection = new SqlConnection(connectionString);
        await firstConnection.OpenAsync();
        await using var firstTransaction = (SqlTransaction)await firstConnection.BeginTransactionAsync();
        await using var firstCommand = InsertCommand(firstConnection, CompanyA, Guid.NewGuid(),
            SettingsManufacturingProcessesV1.ColdRolling, firstVersion, 0.625000m, true,
            new DateOnly(year, 1, 1), new DateOnly(year, 12, 31),
            "synthetic-settings-sql-approver", "held contention winner evidence");
        firstCommand.Transaction = firstTransaction;
        await firstCommand.ExecuteNonQueryAsync();

        var contenderStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var contender = AttemptContendedInsertAsync(contenderStarted, connectionString, secondVersion,
            new DateOnly(year, 6, 1), new DateOnly(year, 12, 31));
        await contenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var completedBeforeCommit = await Task.WhenAny(contender, Task.Delay(TimeSpan.FromMilliseconds(500)))
            == contender;

        await firstTransaction.CommitAsync();
        var outcome = await contender.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(completedBeforeCommit);
        Assert.Equal(51001, outcome);
    }

    private static async Task<int?> AttemptContendedInsertAsync(TaskCompletionSource<bool> started,
        string connectionString, long version, DateOnly from, DateOnly through)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = InsertCommand(connection, CompanyA, Guid.NewGuid(),
            SettingsManufacturingProcessesV1.ColdRolling, version, 0.625000m, true, from, through,
            "synthetic-settings-sql-approver", "blocked contention loser evidence");
        started.TrySetResult(true);

        try
        {
            await command.ExecuteNonQueryAsync();
            return null;
        }
        catch (SqlException exception)
        {
            return exception.Number;
        }
    }

    private static async Task AssertBlankPreparationProvenanceRejectedAsync(
        string connectionString,
        string blankField,
        string blankValue)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "PreparedBy",
            "PreparationReason",
            "PreparationCorrelationId",
            "PreparationSourceChannel",
        };
        Assert.Contains(blankField, allowed);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [settings].[ManufacturingToleranceProfileLifecycles]
                ([CompanyId],[ProfileId],[Process],[ProfileVersion],[MaximumFirePercent],
                 [EffectiveFrom],[EffectiveThrough],[StateVersion],[Status],
                 [PreparedBy],[PreparedAtUtc],[PreparationReason],[PreparationCorrelationId],
                 [PreparationSourceChannel])
            VALUES
                (@company,@profile,@process,100,1.250000,'2040-01-01','2040-12-31',1,1,
                 @preparedBy,@preparedAt,@preparationReason,@preparationCorrelationId,
                 @preparationSourceChannel);
            """;
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@profile", Guid.NewGuid());
        command.Parameters.AddWithValue("@process", SettingsManufacturingProcessesV1.CutToLength);
        command.Parameters.AddWithValue("@preparedBy", Provenance(blankField, "PreparedBy", blankValue, Maker));
        command.Parameters.AddWithValue("@preparedAt",
            new DateTimeOffset(2026, 10, 16, 13, 0, 0, TimeSpan.Zero));
        command.Parameters.AddWithValue("@preparationReason",
            Provenance(blankField, "PreparationReason", blankValue, "direct SQL preparation"));
        command.Parameters.AddWithValue("@preparationCorrelationId",
            Provenance(blankField, "PreparationCorrelationId", blankValue, "direct-sql-preparation"));
        command.Parameters.AddWithValue("@preparationSourceChannel",
            Provenance(blankField, "PreparationSourceChannel", blankValue, "System"));
        await AssertCheckConstraintRejectedAsync(command);
    }

    private static async Task InsertLifecycleDraftAsync(string connectionString, Guid profileId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [settings].[ManufacturingToleranceProfileLifecycles]
                ([CompanyId],[ProfileId],[Process],[ProfileVersion],[MaximumFirePercent],
                 [EffectiveFrom],[EffectiveThrough],[StateVersion],[Status],
                 [PreparedBy],[PreparedAtUtc],[PreparationReason],[PreparationCorrelationId],
                 [PreparationSourceChannel])
            VALUES
                (@company,@profile,@process,200,1.250000,'2041-01-01','2041-12-31',1,1,
                 @preparedBy,@preparedAt,@preparationReason,@preparationCorrelationId,
                 @preparationSourceChannel);
            """;
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@profile", profileId);
        command.Parameters.AddWithValue("@process", SettingsManufacturingProcessesV1.CutToLength);
        command.Parameters.AddWithValue("@preparedBy", Maker);
        command.Parameters.AddWithValue("@preparedAt",
            new DateTimeOffset(2026, 10, 16, 13, 1, 0, TimeSpan.Zero));
        command.Parameters.AddWithValue("@preparationReason", "direct\u1680SQL valid preparation");
        command.Parameters.AddWithValue("@preparationCorrelationId", "direct-sql-valid-preparation");
        command.Parameters.AddWithValue("@preparationSourceChannel", "System");
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertBlankApprovalProvenanceRejectedAsync(
        string connectionString,
        Guid profileId,
        string blankField,
        string blankValue)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "ApprovedBy",
            "ApprovalReason",
            "ApprovalCorrelationId",
            "ApprovalSourceChannel",
        };
        Assert.Contains(blankField, allowed);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE [settings].[ManufacturingToleranceProfileLifecycles]
            SET [StateVersion] = 2, [Status] = 2,
                [ApprovedBy] = @approvedBy, [ApprovedAtUtc] = @approvedAt,
                [ApprovalReason] = @approvalReason,
                [ApprovalCorrelationId] = @approvalCorrelationId,
                [ApprovalSourceChannel] = @approvalSourceChannel
            WHERE [CompanyId] = @company AND [ProfileId] = @profile;
            """;
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@profile", profileId);
        command.Parameters.AddWithValue("@approvedBy", Provenance(blankField, "ApprovedBy", blankValue, Checker));
        command.Parameters.AddWithValue("@approvedAt",
            new DateTimeOffset(2026, 10, 16, 13, 2, 0, TimeSpan.Zero));
        command.Parameters.AddWithValue("@approvalReason",
            Provenance(blankField, "ApprovalReason", blankValue, "direct SQL approval"));
        command.Parameters.AddWithValue("@approvalCorrelationId",
            Provenance(blankField, "ApprovalCorrelationId", blankValue, "direct-sql-approval"));
        command.Parameters.AddWithValue("@approvalSourceChannel",
            Provenance(blankField, "ApprovalSourceChannel", blankValue, "System"));
        await AssertCheckConstraintRejectedAsync(command);
    }

    private static async Task ApplyLifecycleApprovalAsync(string connectionString, Guid profileId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE [settings].[ManufacturingToleranceProfileLifecycles]
            SET [StateVersion] = 2, [Status] = 2,
                [ApprovedBy] = @approvedBy, [ApprovedAtUtc] = @approvedAt,
                [ApprovalReason] = @approvalReason,
                [ApprovalCorrelationId] = @approvalCorrelationId,
                [ApprovalSourceChannel] = @approvalSourceChannel
            WHERE [CompanyId] = @company AND [ProfileId] = @profile;
            """;
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@profile", profileId);
        command.Parameters.AddWithValue("@approvedBy", Checker);
        command.Parameters.AddWithValue("@approvedAt",
            new DateTimeOffset(2026, 10, 16, 13, 3, 0, TimeSpan.Zero));
        command.Parameters.AddWithValue("@approvalReason", "direct\u2003SQL valid approval");
        command.Parameters.AddWithValue("@approvalCorrelationId", "direct-sql-valid-approval");
        command.Parameters.AddWithValue("@approvalSourceChannel", "System");
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertBlankPublicationProvenanceRejectedAsync(
        string connectionString,
        Guid profileId,
        string blankField,
        string blankValue)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "PublishedBy",
            "PublicationReason",
            "PublicationCorrelationId",
            "PublicationSourceChannel",
        };
        Assert.Contains(blankField, allowed);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE [settings].[ManufacturingToleranceProfileLifecycles]
            SET [StateVersion] = 3, [Status] = 3,
                [PublishedBy] = @publishedBy, [PublishedAtUtc] = @publishedAt,
                [PublicationReason] = @publicationReason,
                [PublicationCorrelationId] = @publicationCorrelationId,
                [PublicationSourceChannel] = @publicationSourceChannel
            WHERE [CompanyId] = @company AND [ProfileId] = @profile;
            """;
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@profile", profileId);
        command.Parameters.AddWithValue("@publishedBy", Provenance(blankField, "PublishedBy", blankValue, Checker));
        command.Parameters.AddWithValue("@publishedAt",
            new DateTimeOffset(2026, 10, 16, 13, 4, 0, TimeSpan.Zero));
        command.Parameters.AddWithValue("@publicationReason",
            Provenance(blankField, "PublicationReason", blankValue, "direct SQL publication"));
        command.Parameters.AddWithValue("@publicationCorrelationId",
            Provenance(blankField, "PublicationCorrelationId", blankValue, "direct-sql-publication"));
        command.Parameters.AddWithValue("@publicationSourceChannel",
            Provenance(blankField, "PublicationSourceChannel", blankValue, "System"));
        await AssertCheckConstraintRejectedAsync(command);
    }

    private static async Task ApplyLifecyclePublicationAsync(string connectionString, Guid profileId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE [settings].[ManufacturingToleranceProfileLifecycles]
            SET [StateVersion] = 3, [Status] = 3,
                [PublishedBy] = @publishedBy, [PublishedAtUtc] = @publishedAt,
                [PublicationReason] = @publicationReason,
                [PublicationCorrelationId] = @publicationCorrelationId,
                [PublicationSourceChannel] = @publicationSourceChannel
            WHERE [CompanyId] = @company AND [ProfileId] = @profile;
            """;
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@profile", profileId);
        command.Parameters.AddWithValue("@publishedBy", Checker);
        command.Parameters.AddWithValue("@publishedAt",
            new DateTimeOffset(2026, 10, 16, 13, 5, 0, TimeSpan.Zero));
        command.Parameters.AddWithValue("@publicationReason", "direct\u2028SQL\u2029valid publication");
        command.Parameters.AddWithValue("@publicationCorrelationId", "direct-sql-valid-publication");
        command.Parameters.AddWithValue("@publicationSourceChannel", "System");
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertCheckConstraintRejectedAsync(SqlCommand command)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(547, exception.Number);
    }

    private static string Provenance(
        string blankField,
        string currentField,
        string blankValue,
        string validValue) =>
        string.Equals(blankField, currentField, StringComparison.Ordinal) ? blankValue : validValue;

    private static async Task AssertCommandRejectedAsync(string connectionString, string commandText,
        Guid companyId, Guid profileId, int expectedNumber)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("@company", companyId);
        command.Parameters.AddWithValue("@profile", profileId);
        var exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(expectedNumber, exception.Number);
    }

    private static async Task<int> CountProcessAsync(string connectionString, string process)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [settings].[ManufacturingTolerancePolicies] WHERE [Process] = @process";
        command.Parameters.AddWithValue("@process", process);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [settings].[ManufacturingTolerancePolicies]";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> MigrationHistoryCountAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [settings].[__EFMigrationsHistory] WHERE [MigrationId] = @migration";
        command.Parameters.AddWithValue("@migration", SerializationMigration);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<IReadOnlyList<string>> HistoryRowsAsync(string connectionString)
    {
        var rows = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CONCAT(
                CONVERT(varchar(36), [CompanyId]), N'|', CONVERT(varchar(36), [ProfileId]), N'|',
                [Process], N'|', CONVERT(varchar(20), [Version]), N'|',
                CONVERT(varchar(32), [MaximumFirePercent]), N'|', CONVERT(varchar(1), [IsActive]), N'|',
                CONVERT(varchar(10), [EffectiveFrom], 23), N'|',
                COALESCE(CONVERT(varchar(10), [EffectiveThrough], 23), N'<null>'), N'|',
                [ApprovalStatus], N'|', [ApprovedBy], N'|', CONVERT(varchar(40), [ApprovedAtUtc], 127), N'|',
                [ApprovalReason])
            FROM [settings].[ManufacturingTolerancePolicies]
            ORDER BY [CompanyId], [Process], [EffectiveFrom], [ProfileId];
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }
        return rows;
    }

    private static async Task<TriggerState> TriggerStateAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OBJECT_DEFINITION([object_id]), [is_instead_of_trigger], [is_disabled]
            FROM sys.triggers
            WHERE [parent_id] = OBJECT_ID(N'[settings].[ManufacturingTolerancePolicies]')
              AND [name] = N'TR_ManufacturingTolerancePolicies_NoOverlap';
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new TriggerState(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    private static async Task AssertLifecycleProtectionTriggerEnabledAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OBJECT_DEFINITION([object_id]), [is_instead_of_trigger], [is_disabled]
            FROM sys.triggers
            WHERE [parent_id] = OBJECT_ID(N'[settings].[ManufacturingToleranceProfileLifecycles]')
              AND [name] = N'TR_ManufacturingToleranceProfileLifecycles_Protect';
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Contains("AFTER INSERT, UPDATE, DELETE", reader.GetString(0), StringComparison.OrdinalIgnoreCase);
        Assert.False(reader.GetBoolean(1));
        Assert.False(reader.GetBoolean(2));
    }

    private static async Task SetTriggerEnabledAsync(string connectionString, bool enabled)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {(enabled ? "ENABLE" : "DISABLE")} TRIGGER [settings].[TR_ManufacturingTolerancePolicies_NoOverlap]
            ON [settings].[ManufacturingTolerancePolicies];
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InDisposableDatabaseAsync(Func<string, Task> acceptance)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);
        try
        {
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await acceptance(factory.DatabaseConnectionString);
        }
        finally
        {
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(baseConnection, factory.DatabaseName);
        }
    }

    private static async Task DropDatabaseAsync(string baseConnectionString, string databaseName)
    {
        await using var connection = new SqlConnection(baseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class ProviderScope(SettingsDbContext database,
        SettingsManufacturingToleranceProviderV1 value) : IAsyncDisposable
    {
        public SettingsManufacturingToleranceProviderV1 Value { get; } = value;
        public ValueTask DisposeAsync() => database.DisposeAsync();
    }

    private sealed record TriggerState(string Definition, bool IsInsteadOf, bool IsDisabled);

    private sealed class FixedCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; } = companyId;
        public void Set(Guid id) => throw new NotSupportedException();
    }

    private sealed class FixedActor(Guid companyId, string actorId = "synthetic-settings-sql-actor") : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; } = actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid candidate) => AllowedCompanyIds.Contains(candidate);
    }

    private sealed class CommandScope(
        SettingsDbContext database,
        SettingsManufacturingToleranceProfileCommandServiceV1 service) : IAsyncDisposable
    {
        public SettingsManufacturingToleranceProfileCommandServiceV1 Service { get; } = service;
        public ValueTask DisposeAsync() => database.DisposeAsync();
    }

    private sealed class GrantingAuthority(params string[] permissions) : IErpAuthorityRevalidationService
    {
        private readonly IReadOnlySet<string> granted = permissions.Length == 0
            ? new HashSet<string>(
            [
                ManufacturingToleranceProfileActionsV1.Prepare,
                ManufacturingToleranceProfileActionsV1.Approve,
                ManufacturingToleranceProfileActionsV1.Publish,
            ], StringComparer.Ordinal)
            : new HashSet<string>(permissions, StringComparer.Ordinal);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(granted.Contains(permission)
                ? GrantedScope
                : PermissionScope.Denied);
    }

    private sealed class SequencedAuthority(params bool[] grants) : IErpAuthorityRevalidationService
    {
        private int call;

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref call) - 1;
            return Task.FromResult(index < grants.Length && grants[index]
                ? GrantedScope
                : PermissionScope.Denied);
        }
    }

    private static readonly PermissionScope GrantedScope = new(
        true,
        true,
        new HashSet<string>(["settings-profile-checker"], StringComparer.Ordinal),
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());
}
