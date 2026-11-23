using System.Globalization;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// WP-A1 (AI-D22, AI-D24, AI-D30, AI-D34 d; DALGA_PLANI_v2 A.1): the company's versioned AI gateway policy — the
/// "task kind × data class" model table, the monthly usage ceiling, the per-request reservation and the per-feature
/// kill switches. Nothing is seeded: a company without a recorded version has every feature closed, no route and no
/// budget (fail-closed). The monthly budget value is a human gate (S5); the code never chooses one.
/// </summary>
public static class SettingsAiGatewayPermissionsV1
{
    /// <summary>
    /// Reads and records the AI gateway policy versions. Registered, not granted; company-wide only; not Critical. Giving it
    /// to a role is a human gate.
    /// </summary>
    public const string Manage = "settings.ai-gateway.manage";
}

/// <summary>Input bounds only; the decided values live in recorded versions.</summary>
public static class SettingsAiGatewayLimitsV1
{
    /// <summary>One route per task kind × data class cell at most.</summary>
    public const int MaximumRoutes = 24;

    public const int ModelDeploymentMaximumLength = 128;

    /// <summary>The largest ceiling the store accepts (a bound against typing errors, not a budget).</summary>
    public const long MaximumMonthlyUsageCeiling = 1_000_000_000_000;

    /// <summary>The largest per-request reservation the store accepts (a bound, not a budget).</summary>
    public const int MaximumUnitsPerRequest = 10_000_000;

    /// <summary>The business month of the budget is the company's calendar month in this zone (same zone as Finance).</summary>
    public const string BudgetTimeZoneId = "Europe/Istanbul";
}

public sealed record SettingsAiModelRouteV1(
    AiTaskKindV1 TaskKind,
    AiDataClassV1 DataClass,
    AiModelPlacementV1 Placement,
    string ModelDeployment);

/// <summary>
/// The policy in force. <see cref="Version"/> 0 means nothing is recorded: no feature, no route, no budget.
/// <see cref="MonthlyUsageCeiling"/> or <see cref="MaxUnitsPerRequest"/> null keeps the budget closed.
/// </summary>
public sealed record SettingsAiGatewayPolicyV1(
    Guid CompanyId,
    long Version,
    long? MonthlyUsageCeiling,
    int? MaxUnitsPerRequest,
    IReadOnlySet<AiFeatureV1> EnabledFeatures,
    IReadOnlyList<SettingsAiModelRouteV1> Routes)
{
    public string VersionText => $"ai.gateway-policy:{Version.ToString(CultureInfo.InvariantCulture)}";

    public static SettingsAiGatewayPolicyV1 NotRecorded(Guid companyId) =>
        new(companyId, 0, null, null, new HashSet<AiFeatureV1>(), []);
}

/// <summary>The gateway's read port. The company must be the server-resolved one; no permission is measured here.</summary>
public interface ISettingsAiGatewayPolicyProviderV1
{
    Task<SettingsAiGatewayPolicyV1> GetAiGatewayPolicyAsync(Guid companyId, CancellationToken cancellationToken = default);
}

public enum SettingsAiBudgetReservationOutcomeV1
{
    Reserved = 1,

    /// <summary>The reservation would pass the month's ceiling; nothing was recorded.</summary>
    CeilingReached = 2,

    /// <summary>No ceiling or no per-request reservation is recorded: the budget is closed.</summary>
    BudgetClosed = 3,

    /// <summary>The policy version the caller routed with is no longer the one in force.</summary>
    PolicyChanged = 4,
}

/// <summary>
/// One reservation against the month's ceiling. The ledger re-reads the policy version in force: the units reserved are
/// that version's per-request reservation and the ceiling is that version's ceiling, never a caller value.
/// </summary>
public sealed record SettingsAiBudgetReservationRequestV1(
    Guid CompanyId,
    long PolicyVersion,
    AiFeatureV1 Feature,
    AiTaskKindV1 TaskKind,
    AiModelPlacementV1 Placement,
    string ModelDeployment,
    string ActorId,
    string CorrelationId);

public sealed record SettingsAiBudgetReservationV1(
    SettingsAiBudgetReservationOutcomeV1 Outcome,
    Guid? EntryId,
    int BudgetMonth,
    int ReservedUnits);

public sealed record SettingsAiBudgetMonthUsageV1(Guid CompanyId, int BudgetMonth, long CommittedUnits, int EntryCount);

/// <summary>
/// The append-only usage ledger (AI-D30: "the budget ends, the work stops"). A reservation is taken before the provider is
/// called and settled with the provider's reported units afterwards (zero when nothing was used). Entries are never
/// deleted or rewritten after settlement.
/// </summary>
public interface ISettingsAiBudgetLedgerV1
{
    Task<SettingsAiBudgetReservationV1> ReserveAsync(SettingsAiBudgetReservationRequestV1 request,
        CancellationToken cancellationToken = default);

    /// <summary>Settles a reservation once; a second settle of the same entry changes nothing.</summary>
    Task SettleAsync(Guid companyId, Guid entryId, int usedUnits, CancellationToken cancellationToken = default);

    Task<SettingsAiBudgetMonthUsageV1> GetCurrentMonthUsageAsync(Guid companyId, CancellationToken cancellationToken = default);
}

/// <summary>The business month (yyyymm) of an instant in <see cref="SettingsAiGatewayLimitsV1.BudgetTimeZoneId"/>.</summary>
public static class SettingsAiBudgetMonthV1
{
    public static int Of(DateTimeOffset instant)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(SettingsAiGatewayLimitsV1.BudgetTimeZoneId);
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return checked((local.Year * 100) + local.Month);
    }
}

public sealed record RecordAiModelRouteV1(string? TaskKind, string? DataClass, string? Placement, string? ModelDeployment);

public sealed record RecordAiGatewayPolicyRequestV1(
    string? ExpectedVersion,
    long? MonthlyUsageCeiling,
    int? MaxUnitsPerRequest,
    IReadOnlyList<string>? EnabledFeatures,
    IReadOnlyList<RecordAiModelRouteV1>? Routes,
    string? Reason);

public sealed record AiModelRouteViewV1(string TaskKind, string DataClass, string Placement, string ModelDeployment);

public sealed record AiGatewayPolicyVersionViewV1(
    string Version,
    long? MonthlyUsageCeiling,
    int? MaxUnitsPerRequest,
    int FeatureCount,
    int RouteCount,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

public sealed record AiBudgetMonthViewV1(int BudgetMonth, long CommittedUnits, int EntryCount);

public sealed record AiGatewayPolicyViewV1(
    Guid CompanyId,
    string Version,
    long? MonthlyUsageCeiling,
    int? MaxUnitsPerRequest,
    IReadOnlyList<string> EnabledFeatures,
    IReadOnlyList<AiModelRouteViewV1> Routes,
    AiBudgetMonthViewV1 CurrentMonth,
    IReadOnlyList<AiGatewayPolicyVersionViewV1> Versions);

/// <summary>
/// WP-A5 (AI-20, DALGA_PLANI_v2 A.5): one feature's line of the current business month, read from the append-only ledger.
/// <see cref="CommittedUnits"/> counts a settled entry with its used units and an open reservation with its reserved units
/// (the same conservative sum as the month fence). Counts only; no question text, actor or correlation leaves the ledger.
/// </summary>
public sealed record AiFeatureUsageViewV1(string Feature, int EntryCount, int OpenReservationCount, long CommittedUnits);

/// <summary>
/// WP-A5: the current business month's usage of the company, total and per feature (features without an entry are not
/// listed). The month is <see cref="SettingsAiBudgetMonthV1"/> of the server clock.
/// </summary>
public sealed record AiGatewayUsageViewV1(
    Guid CompanyId,
    int BudgetMonth,
    long CommittedUnits,
    int EntryCount,
    IReadOnlyList<AiFeatureUsageViewV1> Features);

/// <summary>
/// The K3 administration of the gateway policy on <see cref="SettingsAiGatewayPermissionsV1.Manage"/>: company-wide
/// permission measured first and fresh before the save, mandatory reason, one Idempotency-Key per command, expected
/// version, one audit event per real change, insert-only versions. A cloud route above
/// <see cref="AiGatewayPlacementRulesV1.CloudMaximumDataClass"/> is refused (AI-D3/AI-D4).
/// </summary>
public interface ISettingsAiGatewayAdministrationV1
{
    Task<AiGatewayPolicyViewV1> GetAiGatewayPolicyAsync(CancellationToken cancellationToken = default);

    Task<CompanyPolicyCommandResultV1> RecordAiGatewayPolicyAsync(RecordAiGatewayPolicyRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// WP-A5: the current month's ledger counts per feature, on the same company-wide
    /// <see cref="SettingsAiGatewayPermissionsV1.Manage"/> fence as the policy read (measured first and again after the read).
    /// </summary>
    Task<AiGatewayUsageViewV1> GetAiGatewayUsageAsync(CancellationToken cancellationToken = default);
}
