using System.Net;
using System.Text.Json;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsManufacturingProfileSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Starting_values_transfer_rolls_back_a_successful_audit_owner_append_when_the_operation_fails_on_real_sql()
    {
        await using var run = await HostRun.StartAsync(services =>
        {
            var realOwner = services.Last(descriptor => descriptor.ServiceType == typeof(IAuditTransactionWriterV1));
            services.AddScoped<IAuditTransactionWriterV1>(provider => new BaselineAuditFault((IAuditTransactionWriterV1)
                (realOwner.ImplementationFactory is { } factory ? factory(provider)
                    : ActivatorUtilities.CreateInstance(provider, realOwner.ImplementationType!))));
        });
        var actor = await run.GrantAsync(TestUsers.CompanyA, CompanyA, ManufacturingParameterProfileActionsV1.Read,
            ManufacturingParameterProfileActionsV1.Publish);
        using var client = run.Client(actor, CompanyA);
        await run.ProblemAsync(client, Parameters + "/starting-values-transfer", "baseline-audit-fault",
            new ManufacturingProfileBaselineTransferRequestV1("0", "SENTETIK atomiklik"), HttpStatusCode.ServiceUnavailable, null);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ManufacturingProfileBaselineTransfers]"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = 'settings.manufacturing-profile.baseline-transferred.v1'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName] = 'settings.manufacturing-parameters.baseline-transfer.v1'"));
        Assert.Equal("0", (await run.JsonAsync(client, Parameters)).GetProperty("startingValuesTransferVersion").GetString());
    }

    private sealed class BaselineAuditFault(IAuditTransactionWriterV1 owner) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => owner.HasPendingDrafts;
        public async Task WritePendingAsync(System.Data.IDbTransaction transaction, CancellationToken cancellationToken = default)
        {
            await owner.WritePendingAsync(transaction, cancellationToken);
            throw new InvalidOperationException("SENTETIK failure after real Audit append.");
        }
        public void DiscardPending() => owner.DiscardPending();
    }

    [SqlServerFact]
    public async Task Starting_values_transfer_keeps_existing_draft_and_does_not_publish_or_replace_it_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var actor = await run.GrantAsync(TestUsers.CompanyA, CompanyA, ManufacturingParameterProfileActionsV1.Read,
            ManufacturingParameterProfileActionsV1.Prepare, ManufacturingParameterProfileActionsV1.Publish);
        using var client = run.Client(actor, CompanyA);
        var draft = await run.ReceiptAsync(client, Parameters, "baseline-existing-draft", Prepare("0", Paint, null,
            ("filmMicrons", "90"), ("lineSpeedMetresPerMinute", "25"), ("scrapFactor", "1.10"),
            ("simultaneousProfiles", "3"), ("solidsRatio", "0.55")));
        var before = (await run.JsonAsync(client, $"{Parameters}/{draft.ProfileId}")).GetRawText();
        await using var port = run.ParameterPort(CompanyA);
        var servedBefore = JsonSerializer.Serialize(await port.Value.GetManufacturingParametersAsync(Paint, null, run.Today));
        await run.ReceiptAsync(client, Parameters + "/starting-values-transfer", "baseline-with-draft",
            new ManufacturingProfileBaselineTransferRequestV1("0", "SENTETIK mevcut taslağı koru"));
        Assert.Equal(before, (await run.JsonAsync(client, $"{Parameters}/{draft.ProfileId}")).GetRawText());
        Assert.Equal(servedBefore, JsonSerializer.Serialize(await port.Value.GetManufacturingParametersAsync(Paint, null, run.Today)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ManufacturingParameterProfiles] WHERE [CompanyId] = @c AND [Status] = 'Draft'", ("@c", CompanyA)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ManufacturingParameterProfiles] WHERE [CompanyId] = @c AND [Status] = 'Published'", ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task Starting_values_transfer_preserves_exact_provider_answers_is_company_scoped_replays_and_refuses_down_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var publisher = await run.GrantAsync(TestUsers.CompanyA, CompanyA, ManufacturingParameterProfileActionsV1.Read,
            ManufacturingParameterProfileActionsV1.Publish, ManufacturingToleranceProfileReadActionV1.Read,
            ManufacturingToleranceProfileActionsV1.Publish);
        var preparer = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, ManufacturingParameterProfileActionsV1.Prepare);
        var publisherB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, ManufacturingParameterProfileActionsV1.Read,
            ManufacturingParameterProfileActionsV1.Publish);
        using var client = run.Client(publisher, CompanyA);
        using var denied = run.Client(preparer, CompanyA);
        using var clientB = run.Client(publisherB, CompanyB);
        const string transferSuffix = "/starting-values-transfer";
        var body = new ManufacturingProfileBaselineTransferRequestV1("0", "SENTETIK değişmeden aktarım");
        var guard = Assert.IsType<SqlOperation>(new AddManufacturingProfileBaselineTransfers().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        await run.ProblemAsync(denied, Parameters + transferSuffix, "baseline-denied", body, HttpStatusCode.Forbidden, null);
        await using var parameters = run.ParameterPort(CompanyA);
        await using var tolerances = run.TolerancePort(CompanyA);
        var parameterBefore = new Dictionary<string, string>();
        var toleranceBefore = new Dictionary<string, string>();
        foreach (var process in new[] { "Slitting", "ColdRolling", "CutToLength", "Paint" })
            parameterBefore.Add(process, JsonSerializer.Serialize(await parameters.Value.GetManufacturingParametersAsync(process, null, run.Today)));
        foreach (var process in new[] { "Slitting", "ColdRolling", "CutToLength", "Paint", "TubeProfile" })
            toleranceBefore.Add(process, JsonSerializer.Serialize(await tolerances.Value.GetScopedManufacturingToleranceAsync(process, null, run.Today)));

        var receipt = await run.ReceiptAsync(client, Parameters + transferSuffix, "baseline-parameters", body);
        Assert.Equal(("0", "1", "Published"), (receipt.ProfileVersion, receipt.StateVersion, receipt.Status));
        Assert.Equal(receipt, await run.ReceiptAsync(client, Parameters + transferSuffix, "baseline-parameters", body, replayed: true));
        await run.ProblemAsync(client, Parameters + transferSuffix, "baseline-parameters", body with { Reason = "SENTETIK farklı" },
            HttpStatusCode.Conflict, ManufacturingProfileProblemCodesV1.IdempotencyConflict);
        await run.ProblemAsync(client, Parameters + transferSuffix, "baseline-stale", body,
            HttpStatusCode.Conflict, ManufacturingProfileProblemCodesV1.VersionConflict);
        Assert.Equal("0", (await run.JsonAsync(clientB, Parameters)).GetProperty("startingValuesTransferVersion").GetString());
        await run.ReceiptAsync(client, Tolerances + transferSuffix, "baseline-tolerances", body);

        foreach (var before in parameterBefore)
            Assert.Equal(before.Value, JsonSerializer.Serialize(await parameters.Value.GetManufacturingParametersAsync(before.Key, null, run.Today)));
        foreach (var before in toleranceBefore)
            Assert.Equal(before.Value, JsonSerializer.Serialize(await tolerances.Value.GetScopedManufacturingToleranceAsync(before.Key, null, run.Today)));
        Assert.Equal("1", (await run.JsonAsync(client, Parameters)).GetProperty("startingValuesTransferVersion").GetString());
        Assert.Equal("1", (await run.JsonAsync(client, Tolerances)).GetProperty("startingValuesTransferVersion").GetString());
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ManufacturingProfileBaselineTransfers] WHERE [CompanyId] = @c", ("@c", CompanyA)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ManufacturingParameterProfiles] WHERE [CompanyId] = @c", ("@c", CompanyA)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @c AND [EventType] = 'settings.manufacturing-profile.baseline-transferred.v1'", ("@c", CompanyA)));
        var rollback = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51966, rollback.Number);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ManufacturingProfileBaselineTransfers]"));
        // The persisted source is authoritative: corrupt evidence fails closed, never silently reverts to the compiled values.
        await run.ExecuteAsync("UPDATE [settings].[ManufacturingProfileBaselineTransfers] SET [SnapshotHash] = REPLICATE('0',64) WHERE [CompanyId] = @c AND [Kind] = 'parameters'", ("@c", CompanyA));
        Assert.Equal("settings_profile_invalid", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => parameters.Value.GetManufacturingParametersAsync(Paint, null, run.Today))).Code);
    }
}
