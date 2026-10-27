using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// SAT-D57 permission names, the Counterparty owner's spelling. The central catalogue (ErpPermissions) registers them;
/// registering grants nothing — no role, seed or user receives them (human gate). Company-wide only: the group list and
/// the account's group belong to the server-resolved company, which has no narrower dimension. Not Critical.
/// </summary>
public static class CounterpartyCustomerGroupPermissionsV1
{
    /// <summary>Create, rename, deactivate and reactivate the company's customer groups.</summary>
    public const string Manage = "counterparty.customer-groups.manage";

    /// <summary>Assign (or clear) the customer group of a Cari card's company account.</summary>
    public const string Assign = "counterparty.accounts.customer-group.assign";
}

/// <summary>
/// SAT-D57 (Sinan, 2026-10-27): the customer (price) group of a Cari card, owned by Counterparty and held on the
/// company's account (<c>CompanyPartyAccount</c>) — never a party field shared between companies. The list a group is
/// chosen from is the company's own; nothing is seeded. Every command is the Counterparty command shape: company-wide
/// permission first, one Idempotency-Key (stored terminal answers replay), the record's version as decimal text, and
/// one audit event per real change.
/// </summary>
public interface ICounterpartyCustomerGroupService
{
    /// <summary>The active company's groups (passive ones included, marked), and whether this reader may manage them.</summary>
    Task<CounterpartyCustomerGroupList> GetCustomerGroupsAsync(CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> CreateCustomerGroupAsync(
        CreateCounterpartyCustomerGroup command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> RenameCustomerGroupAsync(
        Guid customerGroupId,
        RenameCounterpartyCustomerGroup command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> SetCustomerGroupActiveStateAsync(
        Guid customerGroupId,
        SetCounterpartyCustomerGroupActiveState command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);

    /// <summary>The active company's account of the party with its group; null when the company holds no account.</summary>
    Task<CounterpartyAccountCustomerGroupView?> GetAccountCustomerGroupAsync(
        Guid partyId,
        CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> AssignAccountCustomerGroupAsync(
        Guid partyId,
        AssignCounterpartyCustomerGroup command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);
}

/// <summary>A new group. The caller chooses the UUID, so an unanswered create is retried with the same key and id.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCounterpartyCustomerGroup(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string Name);

/// <summary><c>expectedVersion</c> is the GROUP version (decimal text) from the read the form opened on.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RenameCounterpartyCustomerGroup(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] string Name);

/// <summary>Deactivate (passive) or reactivate a group; a group is never deleted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetCounterpartyCustomerGroupActiveState(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] bool IsActive);

/// <summary>
/// <c>expectedVersion</c> is the company ACCOUNT version (decimal text). <c>customerGroupId</c> must be present: a
/// group of the active company, or an explicit null to clear the account's group.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssignCounterpartyCustomerGroup(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] Guid? CustomerGroupId);

public sealed record CounterpartyCustomerGroupView(
    Guid Id,
    string Name,
    bool IsActive,
    string Version);

/// <summary>What the reader may do here, measured by the server for this actor and company at read time.</summary>
public sealed record CounterpartyCustomerGroupViewer(bool CanManage);

public sealed record CounterpartyCustomerGroupList(
    Guid CompanyId,
    IReadOnlyList<CounterpartyCustomerGroupView> Items,
    CounterpartyCustomerGroupViewer Viewer);

public sealed record CounterpartyAccountCustomerGroupViewer(bool CanAssign);

/// <summary>
/// The active company's account of a Cari card and its customer group (null = no group: the price chain's group tier
/// is skipped for this account). <c>version</c> is the ACCOUNT version the assign command is fenced on.
/// </summary>
public sealed record CounterpartyAccountCustomerGroupView(
    Guid PartyId,
    Guid AccountId,
    Guid CompanyId,
    string Version,
    bool AccountIsActive,
    CounterpartyCustomerGroupView? CustomerGroup,
    CounterpartyAccountCustomerGroupViewer Viewer);
