using GoymenERP.Api.Ai.Gateway;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Ai.Feedback;

/// <summary>AI-D52: answers created in the business month, one last feedback category at its fixed cutoff.</summary>
public sealed class AiFeedbackAcceptanceCountSourceV1(IAiFeedbackPersistenceV1? persistence, ICompanyContext company,
    ICurrentUser user, IErpAuthorityRevalidationService authority, IActorSessionBindingResolver sessions, TimeProvider clock)
    : IAiAcceptanceCountSourceV1
{
    public async Task<IReadOnlyList<AiFeatureAcceptanceCountV1>> GetMonthAsync(Guid companyId, int budgetMonth, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(user.UserId, out var actorId) || !Current(companyId, actorId)) throw new AiFeedbackAuthorityLostException();
        var session = await sessions.ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(session)) throw new AiFeedbackAuthorityLostException();
        await Fence(companyId, actorId, session, cancellationToken);
        if (persistence is null) throw new InvalidOperationException("Feedback measurement owner is unavailable.");
        var window = Window(budgetMonth, clock.GetUtcNow());
        var counts = await persistence.GetMonthCountsAsync(companyId, actorId, window, ct => Fence(companyId, actorId, session, ct), cancellationToken);
        await Fence(companyId, actorId, session, cancellationToken);
        if (counts.Any(row => row is null || row.AsOfUtc != window.AsOfUtc || row.Accepted < 0 || row.Corrected < 0
            || row.Rejected < 0 || row.Reverted < 0 || row.Unreviewed < 0)
            || counts.Select(row => row.Feature).Distinct(StringComparer.Ordinal).Count() != counts.Count)
            throw new InvalidOperationException("Invalid feedback measurement.");
        return counts.Select(row => {
            if (!Enum.TryParse<AiFeatureV1>(row.Feature, out var feature) || !Enum.IsDefined(feature) || feature.ToString() != row.Feature)
                throw new InvalidOperationException("Invalid feedback feature.");
            return new AiFeatureAcceptanceCountV1(feature, row.Accepted, row.Corrected, row.Rejected, row.Reverted, row.Unreviewed);
        }).ToArray();
    }
    public static AiFeedbackCountWindowV1 Window(int month, DateTimeOffset now)
    {
        if (month is < 200001 or > 210012 || month % 100 is < 1 or > 12 || now.Offset != TimeSpan.Zero
            || month > SettingsAiBudgetMonthV1.Of(now)) throw new InvalidOperationException("Invalid feedback business month.");
        var localStart = new DateTime(month / 100, month % 100, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(SettingsAiGatewayLimitsV1.BudgetTimeZoneId);
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone));
        var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart.AddMonths(1), zone));
        if (start < now.AddDays(-AiAnswerReceiptRulesV1.RetentionDays)) throw new InvalidOperationException("Feedback cohort is outside retention.");
        return new(start, end, now < end ? now : end);
    }
    private bool Current(Guid companyId, Guid actorId) => companyId != Guid.Empty && actorId != Guid.Empty && company.IsResolved
        && company.CompanyId == companyId && user.IsAuthenticated && Guid.TryParse(user.UserId, out var actor) && actor == actorId && user.CanAccessCompany(companyId);
    private async Task Fence(Guid companyId, Guid actorId, string session, CancellationToken token)
    {
        if (!Current(companyId, actorId)) throw new AiFeedbackAuthorityLostException();
        var scope = await authority.RevalidatePermissionScopeAsync(SettingsAiGatewayPermissionsV1.Manage, companyId, token);
        if (scope is not { IsGranted: true, IsCompanyWide: true } || !Current(companyId, actorId)) throw new AiFeedbackAuthorityLostException();
        if (await sessions.ResolveAsync(token) != session || !Current(companyId, actorId)) throw new AiFeedbackAuthorityLostException();
    }
}
