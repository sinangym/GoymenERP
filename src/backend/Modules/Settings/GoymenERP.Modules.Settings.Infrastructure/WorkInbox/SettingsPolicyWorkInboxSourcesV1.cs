using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.WorkInbox;

/// <summary>
/// WP7 (AI-D31, AI-D32, YOL-D18): the versioned Ayarlar policies that wait for publication, as Settings lists them for the Work
/// inbox — the finance approval matrix, the cash count difference policy, the bank institution catalog and the collateral rate
/// policy (No 133, URN-D3), and the Manufacturing parameter and tolerance profiles (URN-D6). A version waits while it is a
/// Draft (or, for a Manufacturing profile, left Approved under the retired D-MFG-PROFILE-AUTH-01=A chain) — exactly what the
/// family's publish command accepts; it is published on the family's own Ayarlar screen (the inbox publishes nothing).
/// <para>
/// URN-D3/URN-D6 and YOL-D38 (E-65, SINAN_KARARLARI_2026-11-22_EK_SORULAR): the manager holding the publish permission may
/// prepare and publish alone; K15's "yayımlayan ≠ hazırlayan" does not apply to these policies; no second approver is sought. The owner's rule
/// therefore names no maker and refuses no one: <see cref="WorkInboxItemV1.PreparedBy"/> is null and the preparer sees their
/// own draft. The caller must hold every permission the publish command measures (company-wide, as it stands now, before and
/// after the read); the kind's <see cref="IWorkInboxSourceV1.ApproverPermission"/> is the decisive one of them. A draft waits
/// from its preparation time. Settings stores no threshold on a publication, so the rules carry no numbers.
/// </para>
/// </summary>
internal abstract class SettingsPolicyWorkInboxSourceV1(
    SettingsDbContext database,
    SettingsWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "settings";

    /// <summary>The prepared version waits for publication by an authorized manager (URN-D3, no second approval).</summary>
    public const string DraftAwaitingPublicationRule = "settings.policy-draft-awaiting-publication";

    /// <summary>A Manufacturing profile version approved under the retired chain still waits for publication (URN-D6).</summary>
    public const string ApprovedAwaitingPublicationRule = "settings.profile-approved-awaiting-publication";

    /// <summary>The version replaces an earlier published version of the same family scope.</summary>
    public const string NextVersionRule = "settings.policy-next-version";

    protected const string Draft = "Draft";
    protected const string Approved = "Approved";

    public string Module => ModuleName;

    public abstract string Kind { get; }

    public abstract string ApproverPermission { get; }

    /// <summary>Every permission the family's publish command measures (the approver permission included).</summary>
    protected abstract IReadOnlyList<string> PublishPermissions { get; }

    /// <summary>The family's Ayarlar screen (relative application path).</summary>
    protected abstract string ScreenPath { get; }

    protected SettingsDbContext Database => database;

    /// <summary>The waiting versions of the caller company, any order; the base orders and limits them.</summary>
    protected abstract Task<IReadOnlyList<PendingPolicyV1>> ReadPendingAsync(Guid companyId, CancellationToken cancellationToken);

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, PublishPermissions, cancellationToken) is { } refused)
        {
            return refused;
        }

        List<PendingPolicyV1> pending;
        try
        {
            pending = (await ReadPendingAsync(context.CompanyId, cancellationToken))
                .OrderBy(item => item.PreparedAtUtc).ThenBy(item => item.PolicyId)
                .ToList();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.CheckAsync(context, PublishPermissions, cancellationToken) ?? WorkInboxSourceResultV1.Unavailable();
        }

        if (await fence.CheckAsync(context, PublishPermissions, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var items = new List<WorkInboxItemV1>(Math.Min(pending.Count, maxCount));
        foreach (var entry in pending.Take(maxCount))
        {
            if (!TryItem(context, entry, out var item))
            {
                // A version Settings cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, pending.Count > maxCount);
    }

    private bool TryItem(WorkInboxSourceContextV1 context, PendingPolicyV1 entry, out WorkInboxItemV1 item)
    {
        item = null!;
        if (entry.PolicyId == Guid.Empty || entry.CompanyId != context.CompanyId || entry.Version <= 0
            || entry.State is not (Draft or Approved) || entry.PreparedAtUtc == default
            || string.IsNullOrWhiteSpace(entry.Label) || entry.Label.Length > WorkInboxRulesV1.MaxDocumentLabelLength)
        {
            return false;
        }

        var rules = new List<WorkApprovalRuleResultV1>(2)
        {
            new(entry.State == Draft ? DraftAwaitingPublicationRule : ApprovedAwaitingPublicationRule, null, null, null, null),
        };
        if (entry.Version > 1)
        {
            rules.Add(new(NextVersionRule, null, null, null, null));
        }

        var references = entry.References ?? [];
        item = new WorkInboxItemV1(ModuleName, Kind, entry.PolicyId, entry.Label, entry.PreparedAtUtc.ToUniversalTime(), ScreenPath,
            PreparedBy: null, rules, references);
        return true;
    }

    protected static string VersionLabel(long version) => "v" + version.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One waiting policy version as a family reads it.</summary>
internal sealed record PendingPolicyV1(
    Guid CompanyId,
    Guid PolicyId,
    long Version,
    string State,
    DateTimeOffset PreparedAtUtc,
    string Label,
    IReadOnlyList<WorkApprovalReferenceV1>? References = null);

/// <summary>
/// WP7: the fence every Settings Work inbox source applies before and after its read. The server-resolved company and the
/// authenticated user must be the context's, the user must still reach the company, and every permission the family's publish
/// command measures must be granted company-wide as it stands now. Anything else is a refusal without items; an authority answer
/// that cannot be read is unavailable, never a grant.
/// </summary>
internal sealed class SettingsWorkInboxFence(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
{
    public async Task<WorkInboxSourceResultV1?> CheckAsync(
        WorkInboxSourceContextV1 context,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context) || permissions.Count == 0)
        {
            return WorkInboxSourceResultV1.Denied();
        }

        try
        {
            foreach (var permission in permissions)
            {
                var scope = await authority.RevalidatePermissionScopeAsync(permission, context.CompanyId, cancellationToken);
                if (scope is not { IsGranted: true, IsCompanyWide: true })
                {
                    return WorkInboxSourceResultV1.Denied();
                }
            }

            return SameContext(context) ? null : WorkInboxSourceResultV1.Denied();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WorkInboxSourceResultV1.Unavailable();
        }
    }

    private bool SameContext(WorkInboxSourceContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);
}

/// <summary>WP7: the finance approval matrix waiting for publication (No 133; publish-critical with prepare and publish).</summary>
internal sealed class SettingsFinanceApprovalPolicyWorkInboxSourceV1(SettingsDbContext database, SettingsWorkInboxFence fence)
    : SettingsPolicyWorkInboxSourceV1(database, fence)
{
    public const string KindName = "settings.finance-approval-policy.v1";

    public override string Kind => KindName;

    public override string ApproverPermission => SettingsFinancePolicyPermissionsV1.PublishCritical;

    protected override IReadOnlyList<string> PublishPermissions { get; } =
        [SettingsFinancePolicyPermissionsV1.Prepare, SettingsFinancePolicyPermissionsV1.Publish, SettingsFinancePolicyPermissionsV1.PublishCritical];

    /// <summary>The finance approval policy screen (<c>SETTINGS_ROUTE financePolicies</c>).</summary>
    protected override string ScreenPath => "/ayarlar/finans-onay-politikalari";

    protected override async Task<IReadOnlyList<PendingPolicyV1>> ReadPendingAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await Database.FinanceApprovalPolicies.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.State == Draft)
            .Select(row => new { row.CompanyId, row.PolicyId, row.Version, row.State, row.PreparedAtUtc })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new PendingPolicyV1(row.CompanyId, row.PolicyId, row.Version, row.State, row.PreparedAtUtc,
                VersionLabel(row.Version)))
            .ToArray();
    }
}

/// <summary>WP7: the cash count difference policy (per cash account and currency) waiting for publication (No 133).</summary>
internal sealed class SettingsCashCountDifferencePolicyWorkInboxSourceV1(SettingsDbContext database, SettingsWorkInboxFence fence)
    : SettingsPolicyWorkInboxSourceV1(database, fence)
{
    public const string KindName = "settings.cash-count-difference-policy.v1";

    public override string Kind => KindName;

    public override string ApproverPermission => SettingsFinancePolicyPermissionsV1.Publish;

    protected override IReadOnlyList<string> PublishPermissions { get; } =
        [SettingsFinancePolicyPermissionsV1.Prepare, SettingsFinancePolicyPermissionsV1.Publish];

    /// <summary>The cash count difference policy screen (<c>SETTINGS_ROUTE cashDifferencePolicies</c>).</summary>
    protected override string ScreenPath => "/ayarlar/kasa-sayim-fark-politikalari";

    protected override async Task<IReadOnlyList<PendingPolicyV1>> ReadPendingAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await Database.CashCountDifferencePolicies.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.State == Draft)
            .Select(row => new { row.CompanyId, row.PolicyId, row.Version, row.State, row.PreparedAtUtc, row.CashAccountId })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new PendingPolicyV1(row.CompanyId, row.PolicyId, row.Version, row.State, row.PreparedAtUtc,
                VersionLabel(row.Version),
                [new WorkApprovalReferenceV1("cash-account", ShortReference(row.CashAccountId), row.CashAccountId, null)]))
            .ToArray();
    }

    private static string ShortReference(Guid id) => "…" + id.ToString("D", CultureInfo.InvariantCulture)[^8..];
}

/// <summary>WP7: the bank institution (name/EFT code) catalog waiting for publication (No 133).</summary>
internal sealed class SettingsBankInstitutionPolicyWorkInboxSourceV1(SettingsDbContext database, SettingsWorkInboxFence fence)
    : SettingsPolicyWorkInboxSourceV1(database, fence)
{
    public const string KindName = "settings.bank-institution-policy.v1";

    public override string Kind => KindName;

    public override string ApproverPermission => SettingsFinancePolicyPermissionsV1.Publish;

    protected override IReadOnlyList<string> PublishPermissions { get; } =
        [SettingsFinancePolicyPermissionsV1.Prepare, SettingsFinancePolicyPermissionsV1.Publish];

    /// <summary>The bank institution screen (<c>SETTINGS_ROUTE bankInstitutions</c>).</summary>
    protected override string ScreenPath => "/ayarlar/banka-kurumlari";

    protected override async Task<IReadOnlyList<PendingPolicyV1>> ReadPendingAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await Database.BankInstitutionPolicies.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.State == Draft)
            .Select(row => new { row.CompanyId, row.PolicyId, row.Version, row.State, row.PreparedAtUtc })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new PendingPolicyV1(row.CompanyId, row.PolicyId, row.Version, row.State, row.PreparedAtUtc,
                VersionLabel(row.Version)))
            .ToArray();
    }
}

/// <summary>WP7: a scoped collateral rate policy waiting for publication (No 133, K6/K7).</summary>
internal sealed class SettingsCollateralRatePolicyWorkInboxSourceV1(SettingsDbContext database, SettingsWorkInboxFence fence)
    : SettingsPolicyWorkInboxSourceV1(database, fence)
{
    public const string KindName = "settings.collateral-rate-policy.v1";

    public override string Kind => KindName;

    public override string ApproverPermission => SettingsFinancePolicyPermissionsV1.Publish;

    protected override IReadOnlyList<string> PublishPermissions { get; } =
        [SettingsFinancePolicyPermissionsV1.Prepare, SettingsFinancePolicyPermissionsV1.Publish];

    /// <summary>The collateral rate policy screen (<c>SETTINGS_ROUTE collateralRatePolicies</c>).</summary>
    protected override string ScreenPath => "/ayarlar/teminat-oran-politikalari";

    protected override async Task<IReadOnlyList<PendingPolicyV1>> ReadPendingAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await Database.CollateralRatePolicies.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.State == Draft)
            .Select(row => new { row.CompanyId, row.PolicyId, row.Version, row.State, row.PreparedAtUtc })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new PendingPolicyV1(row.CompanyId, row.PolicyId, row.Version, row.State, row.PreparedAtUtc,
                VersionLabel(row.Version)))
            .ToArray();
    }
}

/// <summary>WP7: a Manufacturing parameter profile version waiting for publication (URN-D6; Draft or left Approved).</summary>
internal sealed class SettingsManufacturingParameterProfileWorkInboxSourceV1(SettingsDbContext database, SettingsWorkInboxFence fence)
    : SettingsPolicyWorkInboxSourceV1(database, fence)
{
    public const string KindName = "settings.manufacturing-parameter-profile.v1";

    public override string Kind => KindName;

    public override string ApproverPermission => ManufacturingParameterProfileActionsV1.Publish;

    protected override IReadOnlyList<string> PublishPermissions { get; } = [ManufacturingParameterProfileActionsV1.Publish];

    /// <summary>The Manufacturing parameter profile screen (<c>SETTINGS_ROUTE manufacturingParameters</c>).</summary>
    protected override string ScreenPath => "/ayarlar/uretim-parametre-profilleri";

    protected override async Task<IReadOnlyList<PendingPolicyV1>> ReadPendingAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await Database.ManufacturingParameterProfiles.AsNoTracking()
            .Where(row => row.CompanyId == companyId && (row.Status == Draft || row.Status == Approved))
            .Select(row => new { row.CompanyId, row.ProfileId, row.ProfileVersion, row.Status, row.PreparedAtUtc, row.Process, row.WorkCenterId })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new PendingPolicyV1(row.CompanyId, row.ProfileId, row.ProfileVersion, row.Status, row.PreparedAtUtc,
                ProfileLabel(row.Process, row.ProfileVersion), WorkCenter(row.WorkCenterId)))
            .ToArray();
    }

    internal static string ProfileLabel(string process, long version) => process + " " + VersionLabel(version);

    internal static IReadOnlyList<WorkApprovalReferenceV1>? WorkCenter(Guid? workCenterId) =>
        workCenterId is { } id
            ? [new WorkApprovalReferenceV1("work-center", "…" + id.ToString("D", CultureInfo.InvariantCulture)[^8..], id, null)]
            : null;
}

/// <summary>WP7: a Manufacturing tolerance profile version waiting for publication (URN-D6; Draft or left Approved).</summary>
internal sealed class SettingsManufacturingToleranceProfileWorkInboxSourceV1(SettingsDbContext database, SettingsWorkInboxFence fence)
    : SettingsPolicyWorkInboxSourceV1(database, fence)
{
    public const string KindName = "settings.manufacturing-tolerance-profile.v1";

    public override string Kind => KindName;

    public override string ApproverPermission => ManufacturingToleranceProfileActionsV1.Publish;

    protected override IReadOnlyList<string> PublishPermissions { get; } = [ManufacturingToleranceProfileActionsV1.Publish];

    /// <summary>The Manufacturing tolerance profile screen (<c>SETTINGS_ROUTE manufacturingTolerances</c>).</summary>
    protected override string ScreenPath => "/ayarlar/uretim-tolerans-profilleri";

    protected override async Task<IReadOnlyList<PendingPolicyV1>> ReadPendingAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await Database.ManufacturingToleranceProfileVersions.AsNoTracking()
            .Where(row => row.CompanyId == companyId && (row.Status == Draft || row.Status == Approved))
            .Select(row => new { row.CompanyId, row.ProfileId, row.ProfileVersion, row.Status, row.PreparedAtUtc, row.Process, row.WorkCenterId })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new PendingPolicyV1(row.CompanyId, row.ProfileId, row.ProfileVersion, row.Status, row.PreparedAtUtc,
                SettingsManufacturingParameterProfileWorkInboxSourceV1.ProfileLabel(row.Process, row.ProfileVersion),
                SettingsManufacturingParameterProfileWorkInboxSourceV1.WorkCenter(row.WorkCenterId)))
            .ToArray();
    }
}
