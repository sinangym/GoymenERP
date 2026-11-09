using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U1-a (API-01/02; URT-D6, D7, D9, D10) against a REAL SQL Server through the REAL host: the Manufacturing parameter
/// profiles (the reviewed version-0 catalog as the base, prepare → approve → publish with maker ≠ approver/publisher, a work
/// center's own profile, effective-date selection, replay/idempotency/version conflicts, company isolation, audit, guard
/// triggers, the rollback refusal 51183) and the tolerance registry write path (publication writes the immutable registry row
/// the tolerance provider reads, with URT-D6's mass-balance tolerance; the window overlap refusal). Every grant is a fresh
/// synthetic role; reasons are SENTETIK.
/// </summary>
public sealed class SettingsManufacturingProfileSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Parameters = SettingsManufacturingProfileEndpoints.ParameterProfilesPath;
    private const string Tolerances = SettingsManufacturingProfileEndpoints.ToleranceProfilesPath;
    private const string MigrationName = "_AddManufacturingParameterProfiles";
    private const string Paint = SettingsManufacturingProcessesV1.Paint;

    [SqlServerFact]
    public async Task Parameter_profiles_lifecycle_catalog_line_scope_dates_isolation_replay_and_audit_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var maker = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            ManufacturingParameterProfileActionsV1.Read, ManufacturingParameterProfileActionsV1.Prepare,
            ManufacturingParameterProfileActionsV1.Approve, ManufacturingParameterProfileActionsV1.Publish);
        var checker = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA,
            ManufacturingParameterProfileActionsV1.Read, ManufacturingParameterProfileActionsV1.Approve,
            ManufacturingParameterProfileActionsV1.Publish);
        var outsider = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, "sales.drafts.read");
        var makerB = await run.GrantAsync(TestUsers.CompanyB, CompanyB,
            ManufacturingParameterProfileActionsV1.Read, ManufacturingParameterProfileActionsV1.Approve);
        using var makerClient = run.Client(maker, CompanyA);
        using var checkerClient = run.Client(checker, CompanyA);
        using var outsiderClient = run.Client(outsider, CompanyA);
        using var clientB = run.Client(makerB, CompanyB);
        var today = run.Today;

        // Without the names: 403 on the read and on prepare; nothing is written.
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.GetAsync(Parameters)).StatusCode);
        using (var forbidden = await outsiderClient.SendAsync(Json(HttpMethod.Post, Parameters, "p-forbidden",
                   Prepare("0", Paint, null, ("filmMicrons", "90")))))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ManufacturingParameterProfiles]"));

        // URT-D9/D10: the reviewed catalog answers (version "0") until a Settings version replaces it.
        var initial = await run.JsonAsync(makerClient, Parameters);
        var catalog = initial.GetProperty("profiles").EnumerateArray().ToArray();
        Assert.Equal(4, catalog.Length);
        Assert.All(catalog, profile => Assert.Equal(("Catalog", "0", "Published"),
            (profile.GetProperty("source").GetString(), profile.GetProperty("profileVersion").GetString(),
                profile.GetProperty("status").GetString())));
        Assert.Contains(initial.GetProperty("keys").EnumerateArray(), key =>
            key.GetProperty("key").GetString() == "densityKgPerCubicMetre" && key.GetProperty("unit").GetString() == "kg/m3");

        await using (var port = run.ParameterPort(CompanyA))
        {
            var paint = await port.Value.GetManufacturingParametersAsync(Paint, null, today);
            Assert.Equal(("0", ManufacturingProfileSourcesV1.Catalog), (paint.ProfileVersion, paint.Source));
            Assert.Equal(20m, Number(paint, "lineSpeedMetresPerMinute"));
            Assert.Equal(80m, Number(paint, "filmMicrons"));
            Assert.Equal(0.55m, Number(paint, "solidsRatio"));
            Assert.Equal(1.10m, Number(paint, "scrapFactor"));
            Assert.Equal(3m, Number(paint, "simultaneousProfiles"));
            var cut = await port.Value.GetManufacturingParametersAsync(SettingsManufacturingProcessesV1.CutToLength, null, today);
            Assert.Equal((7850m, 0m), (Number(cut, "densityKgPerCubicMetre"), Number(cut, "kerfMm")));
            Assert.Equal(ManufacturingParameterProfileCodesV1.ProfileNotFound, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => port.Value.GetManufacturingParametersAsync(SettingsManufacturingProcessesV1.TubeProfile, null, today))).Code);
            Assert.Equal(ManufacturingParameterProfileCodesV1.RequestInvalid, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => port.Value.GetManufacturingParametersAsync("paint", null, today))).Code);
            Assert.Equal(ManufacturingParameterProfileCodesV1.RequestInvalid, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => port.Value.GetManufacturingParametersAsync(Paint, Guid.Empty, today))).Code);
        }

        await using (var unserved = run.ParameterPort(Guid.Parse("10000000-0000-0000-0000-0000000000ee")))
        {
            Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => unserved.Value.GetManufacturingParametersAsync(Paint, null, today))).Code);
        }

        // Validation and reason (unstored 400s).
        await run.ProblemAsync(makerClient, Parameters, "p-bad-key", Prepare("0", Paint, null, ("kerfMm", "0")),
            HttpStatusCode.BadRequest, ManufacturingProfileProblemCodesV1.RequestInvalid);
        await run.ProblemAsync(makerClient, Parameters, "p-bad-ratio", Prepare("0", Paint, null, ("solidsRatio", "1.5")),
            HttpStatusCode.BadRequest, ManufacturingProfileProblemCodesV1.RequestInvalid);
        await run.ProblemAsync(makerClient, Parameters, "p-no-reason",
            Prepare("0", Paint, null, ("filmMicrons", "90")) with { Reason = " " },
            HttpStatusCode.BadRequest, ManufacturingProfileProblemCodesV1.ReasonRequired);

        // Prepare v1 of the Paint process profile; a replay answers the same receipt; another body under the key is 409.
        var v1Body = Prepare("0", Paint, null, ("filmMicrons", "90"), ("lineSpeedMetresPerMinute", "25"), ("scrapFactor", "1.10"),
            ("simultaneousProfiles", "3"), ("solidsRatio", "0.55"));
        var v1 = await run.ReceiptAsync(makerClient, Parameters, "p-v1", v1Body);
        Assert.Equal(("1", "1", "Draft"), (v1.ProfileVersion, v1.StateVersion, v1.Status));
        var replay = await run.ReceiptAsync(makerClient, Parameters, "p-v1", v1Body, replayed: true);
        Assert.Equal(v1, replay);
        await run.ProblemAsync(makerClient, Parameters, "p-v1", v1Body with { Reason = "SENTETIK başka" },
            HttpStatusCode.Conflict, ManufacturingProfileProblemCodesV1.IdempotencyConflict);
        await run.ProblemAsync(makerClient, Parameters, "p-stale", v1Body, HttpStatusCode.Conflict,
            ManufacturingProfileProblemCodesV1.VersionConflict);
        await run.ProblemAsync(makerClient, Parameters, "p-past", v1Body with { ExpectedVersion = "1", EffectiveFrom = today.AddDays(-1) },
            HttpStatusCode.UnprocessableEntity, ManufacturingProfileProblemCodesV1.EffectiveFromPast);

        // A draft is never served.
        await using (var port = run.ParameterPort(CompanyA))
        {
            Assert.Equal("0", (await port.Value.GetManufacturingParametersAsync(Paint, null, today)).ProfileVersion);
        }

        // Maker ≠ approver; the expected state version fences; then approve and publish by the checker.
        var approval = $"{Parameters}/{v1.ProfileId}/approval";
        var publication = $"{Parameters}/{v1.ProfileId}/publication";
        await run.ProblemAsync(makerClient, approval, "p-self-approve", Step("1"), HttpStatusCode.UnprocessableEntity,
            ManufacturingProfileProblemCodesV1.MakerSeparationRequired);
        await run.ProblemAsync(checkerClient, publication, "p-early-publish", Step("1"), HttpStatusCode.Conflict,
            ManufacturingProfileProblemCodesV1.TransitionInvalid);
        await run.ProblemAsync(checkerClient, approval, "p-stale-approve", Step("2"), HttpStatusCode.Conflict,
            ManufacturingProfileProblemCodesV1.VersionConflict);
        Assert.Equal(("2", "Approved"), Pair(await run.ReceiptAsync(checkerClient, approval, "p-approve", Step("1"))));
        await run.ProblemAsync(makerClient, publication, "p-self-publish", Step("2"), HttpStatusCode.UnprocessableEntity,
            ManufacturingProfileProblemCodesV1.MakerSeparationRequired);
        Assert.Equal(("3", "Published"), Pair(await run.ReceiptAsync(checkerClient, publication, "p-publish", Step("2"))));
        await run.ProblemAsync(checkerClient, approval, "p-approve-again", Step("3"), HttpStatusCode.Conflict,
            ManufacturingProfileProblemCodesV1.TransitionInvalid);

        // A work center's own profile wins for that line; another line keeps the process profile.
        var line = Guid.NewGuid();
        var lineDraft = await run.ReceiptAsync(makerClient, Parameters, "p-line",
            Prepare("0", Paint, line, ("lineSpeedMetresPerMinute", "18")));
        Assert.Equal("1", lineDraft.ProfileVersion);
        await run.ReceiptAsync(checkerClient, $"{Parameters}/{lineDraft.ProfileId}/approval", "p-line-approve", Step("1"));
        await run.ReceiptAsync(checkerClient, $"{Parameters}/{lineDraft.ProfileId}/publication", "p-line-publish", Step("2"));

        // A later process version replaces v1 from its own date.
        var v2 = await run.ReceiptAsync(makerClient, Parameters, "p-v2",
            v1Body with { ExpectedVersion = "1", EffectiveFrom = today.AddDays(10), Values = [new("filmMicrons", "100")] });
        await run.ReceiptAsync(checkerClient, $"{Parameters}/{v2.ProfileId}/approval", "p-v2-approve", Step("1"));
        await run.ReceiptAsync(checkerClient, $"{Parameters}/{v2.ProfileId}/publication", "p-v2-publish", Step("2"));

        await using (var port = run.ParameterPort(CompanyA))
        {
            var process = await port.Value.GetManufacturingParametersAsync(Paint, null, today);
            Assert.Equal((v1.ProfileId, "1", ManufacturingProfileSourcesV1.Settings, today, (DateOnly?)null),
                (process.ProfileId, process.ProfileVersion, process.Source, process.EffectiveFrom, process.EffectiveThrough));
            Assert.Equal((25m, "m/min"), (Number(process, "lineSpeedMetresPerMinute"),
                process.Values.Single(value => value.Key == "lineSpeedMetresPerMinute").Unit));
            var own = await port.Value.GetManufacturingParametersAsync(Paint, line, today);
            Assert.Equal((lineDraft.ProfileId, line, 18m), (own.ProfileId, own.WorkCenterId, Number(own, "lineSpeedMetresPerMinute")));
            Assert.Equal(v1.ProfileId, (await port.Value.GetManufacturingParametersAsync(Paint, Guid.NewGuid(), today)).ProfileId);
            var later = await port.Value.GetManufacturingParametersAsync(Paint, null, today.AddDays(10));
            Assert.Equal((v2.ProfileId, "2", 100m), (later.ProfileId, later.ProfileVersion, Number(later, "filmMicrons")));
            Assert.Equal("0", (await port.Value.GetManufacturingParametersAsync(Paint, null, today.AddDays(-1))).ProfileVersion);
        }

        // Company B sees only its own catalog; A's profile is the same code-less 404 as an unknown one.
        var listB = await run.JsonAsync(clientB, Parameters);
        Assert.All(listB.GetProperty("profiles").EnumerateArray(), profile =>
            Assert.Equal("Catalog", profile.GetProperty("source").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"{Parameters}/{v1.ProfileId}")).StatusCode);
        using (var foreign = await clientB.SendAsync(Json(HttpMethod.Post, $"{Parameters}/{v2.ProfileId}/approval", "p-foreign",
                   Step("3"))))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        var detail = await run.JsonAsync(makerClient, $"{Parameters}/{v1.ProfileId}");
        Assert.Equal(("Published", "3", "90"), (detail.GetProperty("status").GetString(), detail.GetProperty("stateVersion").GetString(),
            detail.GetProperty("values").EnumerateArray().Single(value => value.GetProperty("key").GetString() == "filmMicrons")
                .GetProperty("value").GetString()));

        // One audit event per change, in the company, under the action's own name, with the person's reason.
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.manufacturing-parameter-profile.prepared.v1' "
            + "AND [CompanyId] = @c AND [PermissionCode] = N'settings.manufacturing-parameter-profiles.prepare'", ("@c", CompanyA)));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.manufacturing-parameter-profile.published.v1' "
            + "AND [PermissionCode] = N'settings.manufacturing-parameter-profiles.publish' "
            + "AND JSON_VALUE([AfterJson], '$.reason') = N'SENTETIK adım'"));

        // The guards hold in the database too: published rows and values never change, nothing is deleted.
        Assert.Equal(51181, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE [settings].[ManufacturingParameterProfiles] SET [EffectiveThrough] = '2099-01-01' WHERE [ProfileId] = @p",
            ("@p", v1.ProfileId)))).Number);
        Assert.Equal(51182, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "DELETE FROM [settings].[ManufacturingParameterProfileValues] WHERE [ProfileId] = @p", ("@p", v1.ProfileId)))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO [settings].[ManufacturingParameterProfiles] ([CompanyId], [ProfileId], [Process], [ProfileVersion], "
            + "[StateVersion], [Status], [EffectiveFrom], [PreparedBy], [PreparedAtUtc], [PreparationReason], [ApprovedBy], "
            + "[ApprovedAtUtc], [ApprovalReason]) VALUES (@c, NEWID(), 'Paint', 99, 2, 'Approved', '2099-01-01', N'x', "
            + "SYSUTCDATETIME(), N'SENTETIK', N'x', SYSUTCDATETIME(), N'SENTETIK')", ("@c", CompanyA)))).Number);

        // The rollback is refused while profiles exist (forward fix, 51183); nothing is dropped.
        await run.AssertRollbackRefusedAsync();
    }

    [SqlServerFact]
    public async Task Tolerance_profiles_publish_the_registry_row_the_provider_reads_with_overlap_refusal_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var maker = await run.GrantAsync(TestUsers.CompanyA, CompanyA, ManufacturingToleranceProfileReadActionV1.Read,
            ManufacturingToleranceProfileActionsV1.Prepare);
        var checker = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, ManufacturingToleranceProfileReadActionV1.Read,
            ManufacturingToleranceProfileActionsV1.Approve, ManufacturingToleranceProfileActionsV1.Publish);
        var readerB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, ManufacturingToleranceProfileReadActionV1.Read);
        using var makerClient = run.Client(maker, CompanyA);
        using var checkerClient = run.Client(checker, CompanyA);
        using var clientB = run.Client(readerB, CompanyB);
        var today = run.Today;
        const string slitting = SettingsManufacturingProcessesV1.Slitting;

        // URT-D6/D7 starting values are listed, not served: the registry is still empty (503 for the close path).
        var initial = await run.JsonAsync(makerClient, Tolerances);
        Assert.Empty(initial.GetProperty("profiles").EnumerateArray());
        var starting = initial.GetProperty("startingValues").EnumerateArray().ToArray();
        Assert.Equal(5, starting.Length);
        Assert.All(starting, value => Assert.Equal(("5", "2", "StartingValue"),
            (value.GetProperty("maximumFirePercent").GetString(),
                value.GetProperty("massBalanceDifferenceTolerancePercent").GetString(), value.GetProperty("status").GetString())));
        await using (var port = run.TolerancePort(CompanyA))
        {
            Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => port.Value.GetManufacturingToleranceAsync(slitting, today))).Code);
        }

        await run.ProblemAsync(makerClient, Tolerances, "t-bad", ToleranceBody("0", slitting, "101", "2", today, null),
            HttpStatusCode.BadRequest, ManufacturingProfileProblemCodesV1.RequestInvalid);
        var v1 = await run.ReceiptAsync(makerClient, Tolerances, "t-v1", ToleranceBody("0", slitting, "5", "2", today, today.AddDays(30)));
        await run.ProblemAsync(makerClient, $"{Tolerances}/{v1.ProfileId}/approval", "t-self", Step("1"),
            HttpStatusCode.Forbidden, null);
        await run.ReceiptAsync(checkerClient, $"{Tolerances}/{v1.ProfileId}/approval", "t-v1-approve", Step("1"));
        Assert.Equal(("3", "Published"),
            Pair(await run.ReceiptAsync(checkerClient, $"{Tolerances}/{v1.ProfileId}/publication", "t-v1-publish", Step("2"))));
        await run.ReceiptAsync(checkerClient, $"{Tolerances}/{v1.ProfileId}/publication", "t-v1-publish", Step("2"), replayed: true);

        await using (var port = run.TolerancePort(CompanyA))
        {
            var profile = await port.Value.GetManufacturingToleranceAsync(slitting, today);
            Assert.Equal((CompanyA, v1.ProfileId, 1L, 5m, (decimal?)2m, true, today, (DateOnly?)today.AddDays(30)),
                (profile.CompanyId, profile.Profile.ProfileId, profile.Profile.Version, profile.MaximumFirePercent,
                    profile.MassBalanceDifferenceTolerancePercent, profile.IsActive, profile.EffectiveFrom, profile.EffectiveThrough));
        }

        // An overlapping successor is prepared and approved but cannot be published (the registry never changes).
        var overlap = await run.ReceiptAsync(makerClient, Tolerances, "t-v2",
            ToleranceBody("1", slitting, "4", "1.5", today.AddDays(10), null));
        await run.ReceiptAsync(checkerClient, $"{Tolerances}/{overlap.ProfileId}/approval", "t-v2-approve", Step("1"));
        await run.ProblemAsync(checkerClient, $"{Tolerances}/{overlap.ProfileId}/publication", "t-v2-publish", Step("2"),
            HttpStatusCode.Conflict, ManufacturingProfileProblemCodesV1.WindowOverlap);
        var successor = await run.ReceiptAsync(makerClient, Tolerances, "t-v3",
            ToleranceBody("2", slitting, "4", "1.5", today.AddDays(31), null));
        Assert.Equal("3", successor.ProfileVersion);
        await run.ReceiptAsync(checkerClient, $"{Tolerances}/{successor.ProfileId}/approval", "t-v3-approve", Step("1"));
        await run.ReceiptAsync(checkerClient, $"{Tolerances}/{successor.ProfileId}/publication", "t-v3-publish", Step("2"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[ManufacturingTolerancePolicies] WHERE [CompanyId] = @c AND [Process] = 'Slitting' "
            + "AND [ApprovalStatus] = 'Approved' AND [MassBalanceDifferenceTolerancePercent] IS NOT NULL", ("@c", CompanyA)));
        await using (var port = run.TolerancePort(CompanyA))
        {
            var later = await port.Value.GetManufacturingToleranceAsync(slitting, today.AddDays(40));
            Assert.Equal((successor.ProfileId, 3L, 4m, (decimal?)1.5m),
                (later.Profile.ProfileId, later.Profile.Version, later.MaximumFirePercent, later.MassBalanceDifferenceTolerancePercent));
        }

        // Company isolation: B lists none of A's versions, and A's version is a code-less 404 for B.
        Assert.Empty((await run.JsonAsync(clientB, Tolerances)).GetProperty("profiles").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"{Tolerances}/{v1.ProfileId}")).StatusCode);
        Assert.Equal(51184, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE [settings].[ManufacturingToleranceProfileVersions] SET [MaximumFirePercent] = 9 WHERE [ProfileId] = @p",
            ("@p", v1.ProfileId)))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.manufacturing-tolerance-profile.published.v1' "
            + "AND [EntityId] = @p AND [PermissionCode] = N'settings.manufacturing-tolerance-profiles.publish'",
            ("@p", v1.ProfileId.ToString("D"))));

        // Outside the lifecycle publication the context refuses any registry write before SQL (the approved workflow only).
        await using (var direct = run.SettingsContext(CompanyA))
        {
            direct.ManufacturingTolerancePolicies.Add(new ManufacturingTolerancePolicyRow
            {
                CompanyId = CompanyA, ProfileId = Guid.NewGuid(), Process = SettingsManufacturingProcessesV1.Paint, Version = 1,
                MaximumFirePercent = 5m, IsActive = true, EffectiveFrom = today.AddDays(400), ApprovalStatus = "Approved",
                ApprovedBy = "SENTETIK", ApprovedAtUtc = DateTimeOffset.UtcNow, ApprovalReason = "SENTETIK",
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => direct.SaveChangesAsync());
        }

        await run.AssertRollbackRefusedAsync();
    }

    /// <summary>
    /// Üretim U1-b (URT-D6, URT-D23): the line-scoped tolerance read — the line's published version, else the process
    /// version, else the reviewed version-0 catalog — and the supersession of an open-ended published version: publishing a
    /// later successor of the same scope closes the predecessor's window on the day before, in the same save; any other
    /// change of a published registry row is still refused by the trigger (51002).
    /// </summary>
    [SqlServerFact]
    public async Task Tolerance_line_scope_catalog_fallback_and_open_ended_supersession_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var maker = await run.GrantAsync(TestUsers.CompanyA, CompanyA, ManufacturingToleranceProfileReadActionV1.Read,
            ManufacturingToleranceProfileActionsV1.Prepare);
        var checker = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, ManufacturingToleranceProfileReadActionV1.Read,
            ManufacturingToleranceProfileActionsV1.Approve, ManufacturingToleranceProfileActionsV1.Publish);
        using var makerClient = run.Client(maker, CompanyA);
        using var checkerClient = run.Client(checker, CompanyA);
        var today = run.Today;
        const string slitting = SettingsManufacturingProcessesV1.Slitting;
        var line = Guid.NewGuid();
        var otherLine = Guid.NewGuid();

        // Nothing published: the catalog version 0 answers for any line (URT-D23), the process-only read stays unavailable.
        await using (var port = run.TolerancePort(CompanyA))
        {
            var catalog = await port.Value.GetScopedManufacturingToleranceAsync(slitting, line, today);
            Assert.Equal((ManufacturingProfileSourcesV1.Catalog, 0L, 5m, (decimal?)2m, (Guid?)null, (DateOnly?)null),
                (catalog.Source, catalog.Profile.Version, catalog.MaximumFirePercent,
                    catalog.MassBalanceDifferenceTolerancePercent, catalog.WorkCenterId, catalog.EffectiveThrough));
            Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => port.Value.GetManufacturingToleranceAsync(slitting, today))).Code);
            Assert.Equal(ManufacturingParameterProfileCodesV1.RequestInvalid, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => port.Value.GetScopedManufacturingToleranceAsync(slitting, Guid.Empty, today))).Code);
        }

        await using (var portB = run.TolerancePort(CompanyB))
        {
            var catalogB = await portB.Value.GetScopedManufacturingToleranceAsync(slitting, null, today);
            Assert.Equal((CompanyB, ManufacturingProfileSourcesV1.Catalog), (catalogB.CompanyId, catalogB.Source));
        }

        async Task<ManufacturingProfileCommandReceiptV1> PublishAsync(string key, string expected, string fire, string massBalance,
            DateOnly from, Guid? workCenter)
        {
            var prepared = await run.ReceiptAsync(makerClient, Tolerances, $"{key}-prepare",
                ToleranceBody(expected, slitting, fire, massBalance, from, null) with { WorkCenterId = workCenter });
            await run.ReceiptAsync(checkerClient, $"{Tolerances}/{prepared.ProfileId}/approval", $"{key}-approve", Step("1"));
            await run.ReceiptAsync(checkerClient, $"{Tolerances}/{prepared.ProfileId}/publication", $"{key}-publish", Step("2"));
            return prepared;
        }

        var process = await PublishAsync("ls-process", "0", "4", "1.5", today, null);
        var own = await PublishAsync("ls-line", "1", "3", "1", today, line);
        await using (var port = run.TolerancePort(CompanyA))
        {
            var forLine = await port.Value.GetScopedManufacturingToleranceAsync(slitting, line, today);
            Assert.Equal((own.ProfileId, 2L, line, ManufacturingProfileSourcesV1.Settings),
                (forLine.Profile.ProfileId, forLine.Profile.Version, forLine.WorkCenterId, forLine.Source));
            var forOther = await port.Value.GetScopedManufacturingToleranceAsync(slitting, otherLine, today);
            Assert.Equal((process.ProfileId, (Guid?)null, (decimal?)1.5m),
                (forOther.Profile.ProfileId, forOther.WorkCenterId, forOther.MassBalanceDifferenceTolerancePercent));
            // The process-only read ignores line versions.
            Assert.Equal(process.ProfileId, (await port.Value.GetManufacturingToleranceAsync(slitting, today)).Profile.ProfileId);
        }

        // A later open-ended successor of the process supersedes the open-ended process version (not the line's).
        var successor = await PublishAsync("ls-successor", "2", "6", "2.5", today.AddDays(5), null);
        Assert.Equal(today.AddDays(4), DateOnly.FromDateTime(await run.ScalarAsync<DateTime>(
            "SELECT [EffectiveThrough] FROM [settings].[ManufacturingTolerancePolicies] WHERE [ProfileId] = @p",
            ("@p", process.ProfileId))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[ManufacturingTolerancePolicies] WHERE [ProfileId] = @p AND [EffectiveThrough] IS NULL",
            ("@p", own.ProfileId)));
        var listed = (await run.JsonAsync(makerClient, Tolerances)).GetProperty("profiles").EnumerateArray()
            .Single(profile => profile.GetProperty("profileId").GetGuid() == process.ProfileId);
        Assert.Equal(today.AddDays(4).ToString("yyyy-MM-dd"), listed.GetProperty("effectiveThrough").GetString());
        await using (var port = run.TolerancePort(CompanyA))
        {
            Assert.Equal(process.ProfileId,
                (await port.Value.GetScopedManufacturingToleranceAsync(slitting, otherLine, today)).Profile.ProfileId);
            Assert.Equal(successor.ProfileId,
                (await port.Value.GetScopedManufacturingToleranceAsync(slitting, otherLine, today.AddDays(5))).Profile.ProfileId);
            Assert.Equal(own.ProfileId,
                (await port.Value.GetScopedManufacturingToleranceAsync(slitting, line, today.AddDays(5))).Profile.ProfileId);
        }

        // An earlier-starting successor still overlaps (nothing is superseded backwards).
        var backwards = await run.ReceiptAsync(makerClient, Tolerances, "ls-backwards",
            ToleranceBody("3", slitting, "6", "2", today.AddDays(5), null));
        await run.ReceiptAsync(checkerClient, $"{Tolerances}/{backwards.ProfileId}/approval", "ls-backwards-approve", Step("1"));
        await run.ProblemAsync(checkerClient, $"{Tolerances}/{backwards.ProfileId}/publication", "ls-backwards-publish",
            Step("2"), HttpStatusCode.Conflict, ManufacturingProfileProblemCodesV1.WindowOverlap);

        // The trigger admits only the window close: any other change, a second close or a reversed window is refused.
        foreach (var sql in new[]
                 {
                     "UPDATE [settings].[ManufacturingTolerancePolicies] SET [MaximumFirePercent] = 9 WHERE [ProfileId] = @p",
                     "UPDATE [settings].[ManufacturingTolerancePolicies] SET [EffectiveThrough] = DATEADD(day, -1, [EffectiveFrom]) WHERE [ProfileId] = @p",
                 })
        {
            Assert.Equal(51002, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql,
                ("@p", successor.ProfileId)))).Number);
        }

        Assert.Equal(51002, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE [settings].[ManufacturingTolerancePolicies] SET [EffectiveThrough] = DATEADD(day, 10, [EffectiveThrough]) WHERE [ProfileId] = @p",
            ("@p", process.ProfileId)))).Number);
        await run.AssertRollbackRefusedAsync();
    }

    [SqlServerFact]
    public async Task Empty_profile_migration_rolls_back_and_reapplies_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        await using var context = run.SettingsContext(CompanyA);
        var migrations = context.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, name => name.EndsWith(MigrationName, StringComparison.Ordinal));
        Assert.True(index > 0);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[index - 1]);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] IN (N'ManufacturingParameterProfiles', "
            + "N'ManufacturingParameterProfileValues', N'ManufacturingToleranceProfileVersions')"));
        // Scoped to the Settings registry: Manufacturing's close decision carries a column of the same name (U1-b).
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE [name] = N'MassBalanceDifferenceTolerancePercent' "
            + "AND [object_id] = OBJECT_ID(N'[settings].[ManufacturingTolerancePolicies]')"));
        await migrator.MigrateAsync();
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] IN (N'ManufacturingParameterProfiles', "
            + "N'ManufacturingParameterProfileValues', N'ManufacturingToleranceProfileVersions')"));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.triggers WHERE [name] IN (N'TR_ManufacturingParameterProfiles_Guard', "
            + "N'TR_ManufacturingParameterProfileValues_InsertOnly', N'TR_ManufacturingToleranceProfileVersions_Guard', "
            + "N'TR_ManufacturingTolerancePolicies_NoOverlap')"));
    }

    private static PrepareManufacturingParameterProfileRequestV1 Prepare(string expected, string process, Guid? workCenter,
        params (string Key, string Value)[] values) =>
        new(expected, process, workCenter, values.Select(value => new ManufacturingParameterValueInputV1(value.Key, value.Value))
            .ToArray(), "SENTETIK hazırlık");

    private static PrepareManufacturingToleranceProfileRequestV1 ToleranceBody(string expected, string process, string fire,
        string massBalance, DateOnly from, DateOnly? through) =>
        new(expected, process, fire, massBalance, "SENTETIK tolerans", from, through);

    private static ManufacturingProfileLifecycleRequestV1 Step(string expected) => new(expected, "SENTETIK adım");

    private static (string, string) Pair(ManufacturingProfileCommandReceiptV1 receipt) => (receipt.StateVersion, receipt.Status);

    private static decimal Number(ManufacturingParameterProfileSnapshotV1 snapshot, string key) =>
        snapshot.Values.Single(value => value.Key == key).Number!.Value;

    private static HttpRequestMessage Json(HttpMethod method, string path, string key, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"mfg-profile-{key}");
        return request;
    }

    private sealed class FixedActor(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; } = userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid candidate) => AllowedCompanyIds.Contains(candidate);
    }

    private sealed class PortScope<T>(IAsyncDisposable owner, T value) : IAsyncDisposable
    {
        public T Value { get; } = value;

        public ValueTask DisposeAsync() => owner.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>The REAL host over a fresh database, with Identity grants for the HTTP routes.</summary>
    private sealed class HostRun : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private HostRun(string baseConnectionString, SqlServerRelationalApiFactory factory, TimeProvider clock)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
            Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
                TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);
        }

        public SqlServerRelationalApiFactory Factory { get; }

        /// <summary>
        /// The business date (Europe/Istanbul) of the profile administration's server clock, which is fixed at the start
        /// of the run: the server's "today" stays this date even when the run crosses Istanbul midnight (21:00 UTC).
        /// </summary>
        public DateOnly Today { get; }

        public static async Task<HostRun> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            // Only the profile administration (the one server reader of the business date here; both profile lifecycle
            // interfaces resolve this one registration) takes the fixed clock; Identity and the other modules keep the
            // system clock, so the synthetic grants stay in force.
            TimeProvider clock = new FixedClock(DateTimeOffset.UtcNow);
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, services =>
                services.Replace(ServiceDescriptor.Scoped(provider =>
                    ActivatorUtilities.CreateInstance<SettingsManufacturingProfileAdministration>(provider, clock))));
            var run = new HostRun(baseConnectionString, factory, clock);
            using var client = run.Factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public async Task<ManufacturingProfileCommandReceiptV1> ReceiptAsync(HttpClient client, string path, string key, object body,
            bool replayed = false)
        {
            using var response = await client.SendAsync(Json(HttpMethod.Post, path, key, body));
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {text}");
            Assert.Equal(replayed, response.Headers.Contains("Idempotency-Replayed"));
            return JsonSerializer.Deserialize<ManufacturingProfileCommandReceiptV1>(text, JsonSerializerOptions.Web)!;
        }

        public async Task ProblemAsync(HttpClient client, string path, string key, object body, HttpStatusCode status, string? code)
        {
            using var response = await client.SendAsync(Json(HttpMethod.Post, path, key, body));
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(status == response.StatusCode, $"{response.StatusCode}: {text}");
            if (code is not null)
            {
                Assert.Equal(code, JsonDocument.Parse(text).RootElement.GetProperty("code").GetString());
            }
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
            return body;
        }

        public SettingsDbContext SettingsContext(Guid company) => NewSettingsContext(company, out _);

        public PortScope<ISettingsManufacturingParameterProviderV1> ParameterPort(Guid company)
        {
            var database = NewSettingsContext(company, out var companyContext);
            return new PortScope<ISettingsManufacturingParameterProviderV1>(database,
                new SettingsManufacturingParameterProviderV1(database, companyContext, new FixedActor("SENTETIK-mfg", company)));
        }

        public PortScope<ISettingsManufacturingToleranceProviderV1> TolerancePort(Guid company)
        {
            var database = NewSettingsContext(company, out var companyContext);
            return new PortScope<ISettingsManufacturingToleranceProviderV1>(database,
                new SettingsManufacturingToleranceProviderV1(database, companyContext, new FixedActor("SENTETIK-mfg", company)));
        }

        public async Task AssertRollbackRefusedAsync()
        {
            await using var context = SettingsContext(CompanyA);
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var index = Array.FindIndex(applied, name => name.EndsWith(MigrationName, StringComparison.Ordinal));
            Assert.True(index > 0);
            var rollback = await Assert.ThrowsAsync<SqlException>(() =>
                context.GetService<IMigrator>().MigrateAsync(applied[index - 1]));
            Assert.Equal(51185, rollback.Number); // U1-b: the newest profile migration refuses first (forward fix)
            // EF reverts the later migrations first, each in its own transaction: an empty later Settings store (the exchange-rate
            // record, AddExchangeRateRecords) rolls back freely; the refusing migration and everything before it stay applied.
            var refusing = Array.FindIndex(applied, name => name.EndsWith("_AddManufacturingToleranceLineScope", StringComparison.Ordinal));
            Assert.True(refusing >= index);
            Assert.Equal(applied.Take(refusing + 1), await context.Database.GetAppliedMigrationsAsync());
        }

        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK üretim ayarcısı", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer,
                    developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"U1A-{roleId:N}", "SENTETIK üretim profili rolü", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
                approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return developmentUser;
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private SettingsDbContext NewSettingsContext(Guid company, out CompanyContext companyContext)
        {
            companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema))
                .Options, companyContext);
        }
    }
}
