using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// ALS-D100 permission name, the Counterparty owner's spelling. The central catalogue (ErpPermissions) registers it;
/// registering grants nothing — the decision names the Genel Müdür and Admin personas and excludes the Muhasebe
/// Müdürü, and giving it to anyone is a human gate. Company-wide only; checked in the origin company and again in every
/// target company. Not Critical. It is NOT <c>counterparty.accounts.status.update</c>: the per-company deactivation keeps
/// its own name, so existing per-company grants are not widened to the group-wide command.
/// </summary>
public static class CounterpartyGroupDeactivationPermissionsV1
{
    public const string GroupDeactivate = "counterparty.parties.group-deactivate";
}

/// <summary>
/// ALS-D100 (Sinan, 2026-11-15): one command deactivates a party's account in every Göymen company the actor names.
/// Each company runs as its own command (own expected version, own derived idempotency key, own audit, own
/// <c>counterparty.party.deactivated.v1</c> event, own transaction); a failing company is reported, never skipped, and
/// never rolls back the companies that already succeeded. The ALS-D99 warning and reason apply per company.
/// </summary>
public interface ICounterpartyGroupDeactivationService
{
    /// <summary>
    /// Every account of the party in the actor's active authorized companies, with each company's ALS-D99 warning.
    /// Companies the actor cannot reach are never disclosed, not even as a count (tenant boundary); the screen says the
    /// list covers only the companies the actor can access. Null when no reachable company holds an account.
    /// </summary>
    Task<CounterpartyGroupDeactivationPreviewView?> PreviewAsync(
        Guid partyId,
        CancellationToken cancellationToken);

    Task<CounterpartyGroupDeactivationResultView> DeactivateAsync(
        Guid partyId,
        GroupDeactivateCounterparty command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);
}

/// <summary>
/// <c>targets</c> lists each company to deactivate with the account version the actor saw (no duplicates).
/// <c>reason</c> is required (1-1000 characters) and is recorded in every company. <c>exposureWarningAcknowledged</c>
/// confirms the ALS-D99 warning was shown; a company whose warning needs it and lacks it is refused (reported).
/// </summary>
public sealed record GroupDeactivateCounterparty(
    [property: JsonRequired] IReadOnlyList<CounterpartyGroupDeactivationTarget> Targets,
    string Reason,
    bool ExposureWarningAcknowledged = false);

public sealed record CounterpartyGroupDeactivationTarget(
    [property: JsonRequired] Guid CompanyId,
    [property: JsonRequired] long ExpectedVersion);

public sealed record CounterpartyGroupDeactivationPreviewView(
    Guid PartyId,
    IReadOnlyList<CounterpartyGroupDeactivationPreviewCompanyView> Companies);

/// <summary>
/// <c>GroupPermissionGranted</c> says whether the actor holds the group permission in that company; a company without
/// it is shown and will be refused, never silently dropped.
/// </summary>
public sealed record CounterpartyGroupDeactivationPreviewCompanyView(
    Guid CompanyId,
    bool GroupPermissionGranted,
    CounterpartyDeactivationExposureView Exposure);

/// <summary>Per-company outcome spellings of the group command (the frontend decoder accepts exactly these).</summary>
public static class CounterpartyGroupDeactivationOutcomesV1
{
    /// <summary>The account was active and is now passive (audit + event written in that company).</summary>
    public const string Deactivated = "deactivated";

    /// <summary>The account was already passive; recorded as a no-op, no event.</summary>
    public const string AlreadyPassive = "already-passive";

    /// <summary>The company's account moved since the preview; nothing changed there.</summary>
    public const string VersionConflict = "version-conflict";

    /// <summary>The company holds no account for the party.</summary>
    public const string NotFound = "not-found";

    /// <summary>The actor lacks the group permission (or the company) there; nothing changed there.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>The company's ALS-D99 warning needs acknowledgement and the command did not carry it.</summary>
    public const string WarningAcknowledgementRequired = "warning-acknowledgement-required";

    /// <summary>The derived idempotency key was already used for a different request in that company.</summary>
    public const string IdempotencyConflict = "idempotency-conflict";

    /// <summary>The company's command failed unexpectedly; nothing changed there and the failure is logged.</summary>
    public const string Failed = "failed";

    /// <summary>The actor can reach the company and its account is active, but the command did not name it.</summary>
    public const string NotRequested = "not-requested";
}

/// <summary>
/// <c>Replayed</c> is true when the company's stored answer was returned for the same key (no second write); a replayed
/// success reads <c>deactivated</c> because the stored answer does not say whether the first run changed the state.
/// <c>CurrentVersion</c> carries the account version on a version conflict (lossless decimal string), null otherwise.
/// </summary>
public sealed record CounterpartyGroupDeactivationCompanyOutcomeView(
    Guid CompanyId,
    string Outcome,
    bool Replayed,
    string? CurrentVersion);

/// <summary>
/// One outcome per requested company plus <c>not-requested</c> for every reachable company whose account is still active
/// and was not named. Companies the actor cannot reach are not reported (tenant boundary).
/// </summary>
public sealed record CounterpartyGroupDeactivationResultView(
    Guid PartyId,
    IReadOnlyList<CounterpartyGroupDeactivationCompanyOutcomeView> Companies);
