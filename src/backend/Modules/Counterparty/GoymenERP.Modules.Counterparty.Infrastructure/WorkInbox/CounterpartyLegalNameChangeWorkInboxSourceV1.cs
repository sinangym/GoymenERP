using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.WorkInbox;

/// <summary>
/// WP7-2 (AI-D31, AI-D32, YOL-D18): the requests to change a party's official legal name waiting for the checker, as
/// Counterparty lists them for the Work inbox. A request waits while it is Pending — exactly what
/// <c>DecideCounterpartyLegalNameChangeV1</c> accepts; it is decided on the party's own screen by a holder of
/// <c>counterparties.legal_name.approve</c> (with <c>counterparty.parties.read</c>), both company-wide — the two names the
/// decision command measures — never by its proposer (<see cref="PartyLegalNameChangeRequest.IsRequestedBy"/>; one approval is
/// enough). A request prepared against a party version that has moved since is stale and its rule says so. The label is the
/// proposed legal name; the party's current name is the reference.
/// </summary>
internal sealed class CounterpartyLegalNameChangeWorkInboxSourceV1(
    CounterpartyDbContext database,
    WorkInboxCompanyWideFenceV1 fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "counterparty";
    public const string KindName = "counterparty.legal-name-change.v1";

    /// <summary>A party's legal name changes only when someone other than the proposer approves it.</summary>
    public const string ApprovalRequiredRule = "counterparty.legal-name-change-approval-required";

    /// <summary>The party changed after the request was prepared.</summary>
    public const string StaleRule = "counterparty.legal-name-change-stale";

    /// <summary>The party screen (<c>CAPABILITY.cariPartiesDetail</c>, <c>/cari/:partyId</c>).</summary>
    internal const string PartyScreenPath = "/cari";

    private static readonly IReadOnlyList<string> Permissions =
        [CounterpartyLegalNamePermissionsV1.Approve, ErpPermissions.CounterpartyPartiesRead];

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => CounterpartyLegalNamePermissionsV1.Approve;

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, Permissions, cancellationToken) is { } refused)
        {
            return refused;
        }

        List<PartyLegalNameChangeRequest> rows;
        Dictionary<Guid, (string LegalName, long Version)> parties;
        int total;
        try
        {
            var pending = database.PartyLegalNameChangeRequests.AsNoTracking()
                .Where(request => request.CompanyId == context.CompanyId && request.Status == PartyLegalNameChangeStatus.Pending);
            total = await pending.CountAsync(cancellationToken);
            rows = await pending.OrderBy(request => request.RequestedAtUtc).ThenBy(request => request.Id)
                .Take(maxCount).ToListAsync(cancellationToken);
            var partyIds = rows.Select(row => row.PartyId).Distinct().ToArray();
            parties = (await database.Parties.AsNoTracking()
                    .Where(party => partyIds.Contains(party.Id))
                    .Select(party => new { party.Id, party.LegalName, party.Version })
                    .ToListAsync(cancellationToken))
                .ToDictionary(party => party.Id, party => (party.LegalName, party.Version));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.CheckAsync(context, Permissions, cancellationToken) ?? WorkInboxSourceResultV1.Unavailable();
        }

        if (await fence.CheckAsync(context, Permissions, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var items = new List<WorkInboxItemV1>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Id == Guid.Empty || string.IsNullOrWhiteSpace(row.RequestedBy) || !parties.TryGetValue(row.PartyId, out var party)
                || string.IsNullOrWhiteSpace(party.LegalName) || party.LegalName.Length > WorkInboxRulesV1.MaxDocumentLabelLength)
            {
                return WorkInboxSourceResultV1.Unavailable();
            }

            var rules = new List<WorkApprovalRuleResultV1>(2) { new(ApprovalRequiredRule, null, null, null, null) };
            if (party.Version != row.PartyVersionAtRequest)
            {
                rules.Add(new(StaleRule, null, null, null, null));
            }

            var link = WorkInboxCompanyWideFenceV1.Link(PartyScreenPath, row.PartyId);
            if (string.IsNullOrWhiteSpace(row.ProposedLegalName) || row.ProposedLegalName.Length > WorkInboxRulesV1.MaxDocumentLabelLength)
            {
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(new WorkInboxItemV1(ModuleName, KindName, row.Id, row.ProposedLegalName, row.RequestedAtUtc.ToUniversalTime(), link,
                row.RequestedBy, rules, [new("party", party.LegalName, row.PartyId, link)]));
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, total > rows.Count);
    }
}
