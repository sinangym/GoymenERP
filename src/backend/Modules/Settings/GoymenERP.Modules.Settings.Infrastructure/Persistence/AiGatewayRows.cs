namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// WP-A1 (AI-D24, AI-D30): one insert-only version of a company's AI gateway policy. Its features and routes are child rows
/// of the same version; a new version is a full new snapshot. A null ceiling or reservation keeps the budget closed.
/// </summary>
internal sealed class AiGatewayPolicyVersionRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public long? MonthlyUsageCeiling { get; set; }
    public int? MaxUnitsPerRequest { get; set; }
    public int FeatureCount { get; set; }
    public int RouteCount { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>An enabled feature of a policy version (<c>AiFeatureV1</c> value); absence means the feature is switched off.</summary>
internal sealed class AiGatewayFeatureRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public int Feature { get; set; }
}

/// <summary>One cell of the "task kind × data class" model table of a policy version.</summary>
internal sealed class AiGatewayModelRouteRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public int TaskKind { get; set; }
    public int DataClass { get; set; }
    public int Placement { get; set; }
    public string ModelDeployment { get; set; } = string.Empty;
}

/// <summary>The committed units (reserved and settled) of one company business month; the ceiling fence.</summary>
internal sealed class AiBudgetMonthTotalRow
{
    public Guid CompanyId { get; set; }
    public int BudgetMonth { get; set; }
    public long CommittedUnits { get; set; }
}

/// <summary>One append-only usage ledger entry: reserved before the provider call, settled once afterwards.</summary>
internal sealed class AiBudgetLedgerEntryRow
{
    public Guid CompanyId { get; set; }
    public Guid EntryId { get; set; }
    public int BudgetMonth { get; set; }
    public long PolicyVersion { get; set; }
    public int Feature { get; set; }
    public int TaskKind { get; set; }
    public int Placement { get; set; }
    public string ModelDeployment { get; set; } = string.Empty;
    public int ReservedUnits { get; set; }
    public int? UsedUnits { get; set; }

    /// <summary>1 reserved, 2 settled.</summary>
    public int State { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset ReservedAtUtc { get; set; }
    public DateTimeOffset? SettledAtUtc { get; set; }
}
