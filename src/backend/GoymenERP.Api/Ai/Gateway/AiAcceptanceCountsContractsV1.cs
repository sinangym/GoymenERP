using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Ai.Gateway;

/// <summary>
/// WP-A5 (AI-20, DALGA_PLANI_v2 A.5): one feature's acceptance counts of a business month — how often a user accepted,
/// corrected, rejected or later reverted what the assistant proposed. Counts only, never content.
/// </summary>
public sealed record AiFeatureAcceptanceCountV1(AiFeatureV1 Feature, int Accepted, int Corrected, int Rejected, int Reverted);

/// <summary>
/// WP-A5: the read port of the acceptance counts. The learning record that owns them arrives with WP-C9 (C.9, "kabul,
/// düzeltme, ret, sonradan geri alma"); until a source is registered the management screen says "not measured yet" instead
/// of showing zeros. The company is always the server-resolved one.
/// </summary>
public interface IAiAcceptanceCountSourceV1
{
    Task<IReadOnlyList<AiFeatureAcceptanceCountV1>> GetMonthAsync(Guid companyId, int budgetMonth,
        CancellationToken cancellationToken = default);
}

public sealed record AiFeatureAcceptanceViewV1(string Feature, int Accepted, int Corrected, int Rejected, int Reverted);

/// <summary>
/// WP-A5: <c>GET /api/v1/settings/company-policies/ai-gateway/usage</c>. <see cref="AcceptanceMeasured"/> false means no
/// acceptance source is registered yet (WP-C9) and <see cref="Acceptance"/> is empty — not "nobody accepted anything".
/// </summary>
public sealed record AiGatewayUsageResponseV1(
    Guid CompanyId,
    int BudgetMonth,
    long CommittedUnits,
    int EntryCount,
    IReadOnlyList<AiFeatureUsageViewV1> Features,
    bool AcceptanceMeasured,
    IReadOnlyList<AiFeatureAcceptanceViewV1> Acceptance)
{
    /// <summary>Joins the ledger view with the optional acceptance counts; a malformed count line is a
    /// provider failure (503), never shown.</summary>
    public static AiGatewayUsageResponseV1 From(AiGatewayUsageViewV1 usage, IReadOnlyList<AiFeatureAcceptanceCountV1>? acceptance)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (acceptance is null)
            return new(usage.CompanyId, usage.BudgetMonth, usage.CommittedUnits, usage.EntryCount, usage.Features, false, []);

        var lines = new List<AiFeatureAcceptanceViewV1>(acceptance.Count);
        foreach (var line in acceptance.OrderBy(item => item?.Feature))
        {
            if (line is null || !Enum.IsDefined(line.Feature) || line.Accepted < 0 || line.Corrected < 0 || line.Rejected < 0
                || line.Reverted < 0 || lines.Any(existing => existing.Feature == line.Feature.ToString()))
                throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
            lines.Add(new(line.Feature.ToString(), line.Accepted, line.Corrected, line.Rejected, line.Reverted));
        }

        return new(usage.CompanyId, usage.BudgetMonth, usage.CommittedUnits, usage.EntryCount, usage.Features, true, lines);
    }
}
