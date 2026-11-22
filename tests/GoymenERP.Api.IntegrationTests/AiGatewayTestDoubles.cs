using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP-A1 test doubles: an in-memory stand-in for the Settings-owned AI gateway policy port and usage ledger (same
/// reservation semantics: the version routed with must be in force, the per-request reservation and the ceiling come from
/// that version, committed + reservation ≤ ceiling), plus a builder for the real <see cref="AiGateway"/> over the Fake
/// composers. SENTETIK values only; no database, no network.
/// </summary>
internal sealed class InMemoryAiGatewayStore : ISettingsAiGatewayPolicyProviderV1, ISettingsAiBudgetLedgerV1
{
    public const string LocalDeployment = "sentetik-yerel-model";
    public const string CloudDeployment = "sentetik-bulut-model";

    public InMemoryAiGatewayStore(Guid companyId)
    {
        CompanyId = companyId;
        Policy = OpenPolicy(companyId);
    }

    public Guid CompanyId { get; }
    public SettingsAiGatewayPolicyV1 Policy { get; set; }
    public long CommittedUnits { get; set; }
    public bool FailPolicyRead { get; set; }
    public List<SettingsAiBudgetReservationRequestV1> Reservations { get; } = [];
    public Dictionary<Guid, (int Reserved, int? Used)> Entries { get; } = [];
    public int PolicyReads { get; private set; }

    /// <summary>Every feature on, a generous SENTETIK ceiling, Answer/Summarize routes: Public/Internal on cloud, Confidential local.</summary>
    public static SettingsAiGatewayPolicyV1 OpenPolicy(Guid companyId) => new(
        companyId,
        Version: 7,
        MonthlyUsageCeiling: 1_000_000,
        MaxUnitsPerRequest: 500,
        EnabledFeatures: Enum.GetValues<AiFeatureV1>().ToHashSet(),
        Routes:
        [
            new(AiTaskKindV1.Answer, AiDataClassV1.Public, AiModelPlacementV1.Cloud, CloudDeployment),
            new(AiTaskKindV1.Answer, AiDataClassV1.Internal, AiModelPlacementV1.Local, LocalDeployment),
            new(AiTaskKindV1.Answer, AiDataClassV1.Confidential, AiModelPlacementV1.Local, LocalDeployment),
            new(AiTaskKindV1.Converse, AiDataClassV1.Internal, AiModelPlacementV1.Cloud, CloudDeployment),
            new(AiTaskKindV1.Extract, AiDataClassV1.Internal, AiModelPlacementV1.Cloud, CloudDeployment),
        ]);

    public Task<SettingsAiGatewayPolicyV1> GetAiGatewayPolicyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        PolicyReads++;
        if (FailPolicyRead) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        if (companyId != CompanyId) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        return Task.FromResult(Policy);
    }

    public Task<SettingsAiBudgetReservationV1> ReserveAsync(SettingsAiBudgetReservationRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        Reservations.Add(request);
        const int month = 202611;
        if (request.CompanyId != CompanyId) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        if (request.PolicyVersion != Policy.Version)
            return Task.FromResult(new SettingsAiBudgetReservationV1(SettingsAiBudgetReservationOutcomeV1.PolicyChanged, null, month, 0));
        if (Policy.MonthlyUsageCeiling is not { } ceiling || Policy.MaxUnitsPerRequest is not { } units)
            return Task.FromResult(new SettingsAiBudgetReservationV1(SettingsAiBudgetReservationOutcomeV1.BudgetClosed, null, month, 0));
        if (CommittedUnits + units > ceiling)
            return Task.FromResult(new SettingsAiBudgetReservationV1(SettingsAiBudgetReservationOutcomeV1.CeilingReached, null, month, 0));
        CommittedUnits += units;
        var entryId = Guid.NewGuid();
        Entries[entryId] = (units, null);
        return Task.FromResult(new SettingsAiBudgetReservationV1(SettingsAiBudgetReservationOutcomeV1.Reserved, entryId, month, units));
    }

    public Task SettleAsync(Guid companyId, Guid entryId, int usedUnits, CancellationToken cancellationToken = default)
    {
        if (Entries.TryGetValue(entryId, out var entry) && entry.Used is null)
        {
            CommittedUnits += usedUnits - entry.Reserved;
            Entries[entryId] = (entry.Reserved, usedUnits);
        }

        return Task.CompletedTask;
    }

    public Task<SettingsAiBudgetMonthUsageV1> GetCurrentMonthUsageAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SettingsAiBudgetMonthUsageV1(companyId, 202611, CommittedUnits, Entries.Count));
}

internal static class AiGatewayTestDoubles
{
    public static AiGateway Gateway<TStore>(
        AiRuntimeOptions options,
        TStore store,
        IEnumerable<IAiAnswerComposerV1>? composers = null,
        string environmentName = "Development")
        where TStore : ISettingsAiGatewayPolicyProviderV1, ISettingsAiBudgetLedgerV1 =>
        new(
            Options.Create(options),
            new GatewayTestHostEnvironment(environmentName),
            new FakeAiModelProvider(),
            composers ?? [new FakeAiAnswerComposer(AiModelPlacementV1.Cloud), new FakeAiAnswerComposer(AiModelPlacementV1.Local)],
            store,
            store);

    private sealed class GatewayTestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
