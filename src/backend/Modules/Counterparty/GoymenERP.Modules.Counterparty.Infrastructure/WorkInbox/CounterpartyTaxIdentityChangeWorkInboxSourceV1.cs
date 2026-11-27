using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.WorkInbox;

/// <summary>
/// WP7-2 (AI-D31, AI-D32, YOL-D18; AT-009, CARI-DEC-004): the requests to replace a party's VKN/TCKN waiting for the checker,
/// as Counterparty lists them for the Work inbox. A request waits while it is Pending — exactly what
/// <c>DecideCounterpartyTaxIdentityChangeV1</c> accepts; it is decided on the party's own screen by a holder of
/// <c>counterparty.parties.tax-identity.approve</c> (with <c>counterparty.parties.read</c>), both company-wide — the two names
/// the decision command measures — never by its maker (<see cref="PartyTaxIdentityChangeRequest.IsRequestedBy"/>). A request
/// prepared against a party version that has moved since is stale and its rule says so. The label is the party's legal name;
/// the tax numbers themselves (a TCKN is personal data) are not put into the inbox.
/// </summary>
internal sealed class CounterpartyTaxIdentityChangeWorkInboxSourceV1(
    CounterpartyDbContext database,
    WorkInboxCompanyWideFenceV1 fence) : IWorkApprovalDecisionSourceV1
{
    public const string ModuleName = "counterparty";
    public const string KindName = "counterparty.tax-identity-change.v1";

    /// <summary>Replacing a party's VKN/TCKN needs another person's approval (AT-009 maker-checker).</summary>
    public const string ApprovalRequiredRule = "counterparty.tax-identity-change-approval-required";

    /// <summary>The party changed after the request was prepared.</summary>
    public const string StaleRule = "counterparty.tax-identity-change-stale";

    /// <summary>The party screen (<c>CAPABILITY.cariPartiesDetail</c>, <c>/cari/:partyId</c>).</summary>
    internal const string PartyScreenPath = "/cari";
    internal const string DecisionScreenPath = "/onaylarim/counterparty.tax-identity-change.v1";

    private static readonly IReadOnlyList<string> Permissions =
        [CounterpartyTaxIdentityPermissionsV1.Approve, ErpPermissions.CounterpartyPartiesRead];

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => CounterpartyTaxIdentityPermissionsV1.Approve;

    /// <summary>Native decision purpose: terminal requests remain readable for the owner's immutable replay.
    /// Party visibility uses the native company-account existence rule, including inactive accounts.
    /// Exact proposal values leave only through the non-serializable human proof, never the inbox item.</summary>
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (itemId == Guid.Empty || await fence.CheckAsync(context, Permissions, cancellationToken) is not null) return null;
        var row = await database.PartyTaxIdentityChangeRequests.AsNoTracking()
            .Where(request => request.CompanyId == context.CompanyId && request.Id == itemId)
            .Select(request => new { request.Id, request.CompanyId, request.PartyId, request.Version, request.Status,
                request.PartyVersionAtRequest, request.RequestedBy, request.RequestedAtUtc, request.Reason, request.CurrentTaxIdentityNumber, request.ProposedTaxIdentityNumber })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) { await fence.CheckAsync(context, Permissions, cancellationToken); return null; }
        var party = await database.Parties.AsNoTracking()
            .Where(party => party.Id == row.PartyId && database.CompanyPartyAccounts.Any(account =>
                account.CompanyId == context.CompanyId && account.PartyId == party.Id))
            .Select(party => new { party.Id, party.Code, party.LegalName, party.Version }).SingleOrDefaultAsync(cancellationToken);
        if (await fence.CheckAsync(context, Permissions, cancellationToken) is not null || party is null) return null;
        if (row.Id == Guid.Empty || row.PartyId == Guid.Empty || row.Version < 1 || row.PartyVersionAtRequest < 1
            || party.Version < 1 || !Enum.IsDefined(row.Status) || string.IsNullOrWhiteSpace(row.RequestedBy)
            || row.RequestedAtUtc == default || string.IsNullOrWhiteSpace(party.LegalName)
            || party.LegalName.Length > Party.LegalNameMaximumLength
            || string.IsNullOrWhiteSpace(row.Reason) || row.Reason.Trim() != row.Reason
            || row.Reason.Length > PartyTaxIdentityChangeRequest.ReasonMaximumLength
            || string.IsNullOrWhiteSpace(party.Code.Value) || party.Code.Value.Length > WorkInboxRulesV1.MaxReferenceLabelLength
            // Historical current values follow the stored 10/11 ASCII-digit contract, not a new checksum gate.
            || row.CurrentTaxIdentityNumber is not { Length: 10 or 11 } || row.CurrentTaxIdentityNumber.Any(character => !char.IsAsciiDigit(character))
            || !TaxIdentityNumber.TryParse(row.ProposedTaxIdentityNumber, out var proposed, out _)
            || proposed.Value != row.ProposedTaxIdentityNumber || row.CurrentTaxIdentityNumber == proposed.Value)
            throw new InvalidOperationException("The native identity decision source is malformed.");
        // Do not combine a newer request status/party version with an earlier projection. No retry or mutation.
        var finalRequest = await database.PartyTaxIdentityChangeRequests.AsNoTracking()
            .Where(request => request.CompanyId == context.CompanyId && request.Id == itemId)
            .Select(request => new { request.Version, request.Status }).SingleOrDefaultAsync(cancellationToken);
        var finalVersion = await database.Parties.AsNoTracking()
            .Where(candidate => candidate.Id == row.PartyId && database.CompanyPartyAccounts.Any(account =>
                account.CompanyId == context.CompanyId && account.PartyId == candidate.Id))
            .Select(candidate => (long?)candidate.Version).SingleOrDefaultAsync(cancellationToken);
        if (await fence.CheckAsync(context, Permissions, cancellationToken) is not null) return null;
        if (finalRequest is null || finalRequest.Version != row.Version || finalRequest.Status != row.Status
            || finalVersion != party.Version)
            throw new InvalidOperationException("The native identity decision source changed while it was read.");
        var rules = new List<WorkApprovalRuleResultV1> { new(ApprovalRequiredRule, null, null, null, null) };
        if (party.Version != row.PartyVersionAtRequest) rules.Add(new(StaleRule, null, null, null, null));
        var link = WorkInboxCompanyWideFenceV1.Link(PartyScreenPath, row.PartyId);
        var label = party.LegalName.Length <= WorkInboxRulesV1.MaxDocumentLabelLength ? party.LegalName : party.Code.Value;
        var referenceLabel = party.LegalName.Length <= WorkInboxRulesV1.MaxReferenceLabelLength ? party.LegalName : party.Code.Value;
        var item = new WorkInboxItemV1(ModuleName, KindName, row.Id, label,
            row.RequestedAtUtc.ToUniversalTime(), WorkInboxCompanyWideFenceV1.Link(DecisionScreenPath, row.Id), row.RequestedBy, rules,
            [new("party", referenceLabel, row.PartyId, link)]);
        return new(context.CompanyId, item, party.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            row.Status == PartyTaxIdentityChangeStatus.Pending, CounterpartyTaxIdentityChange: new(row.PartyId, row.Id, row.CurrentTaxIdentityNumber, row.ProposedTaxIdentityNumber,
                proposed.Kind == TaxIdentityKind.Vkn ? CounterpartyTaxIdentityKindsV1.Vkn : CounterpartyTaxIdentityKindsV1.Tckn, row.Reason));
    }

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

        List<PartyTaxIdentityChangeRequest> rows;
        Dictionary<Guid, (string Code, string LegalName, long Version)> parties;
        int total;
        try
        {
            var pending = database.PartyTaxIdentityChangeRequests.AsNoTracking()
                .Where(request => request.CompanyId == context.CompanyId && request.Status == PartyTaxIdentityChangeStatus.Pending);
            total = await pending.CountAsync(cancellationToken);
            rows = await pending.OrderBy(request => request.RequestedAtUtc).ThenBy(request => request.Id)
                .Take(maxCount).ToListAsync(cancellationToken);
            var partyIds = rows.Select(row => row.PartyId).Distinct().ToArray();
            parties = (await database.Parties.AsNoTracking()
                    .Where(party => partyIds.Contains(party.Id))
                    .Select(party => new { party.Id, party.Code, party.LegalName, party.Version })
                    .ToListAsync(cancellationToken))
                .ToDictionary(party => party.Id, party => (party.Code.Value, party.LegalName, party.Version));
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
                || string.IsNullOrWhiteSpace(party.LegalName) || party.LegalName.Length > Party.LegalNameMaximumLength
                || string.IsNullOrWhiteSpace(party.Code) || party.Code.Length > WorkInboxRulesV1.MaxReferenceLabelLength)
            {
                return WorkInboxSourceResultV1.Unavailable();
            }

            var rules = new List<WorkApprovalRuleResultV1>(2) { new(ApprovalRequiredRule, null, null, null, null) };
            if (party.Version != row.PartyVersionAtRequest)
            {
                rules.Add(new(StaleRule, null, null, null, null));
            }

            var link = WorkInboxCompanyWideFenceV1.Link(PartyScreenPath, row.PartyId);
            var label = party.LegalName.Length <= WorkInboxRulesV1.MaxDocumentLabelLength ? party.LegalName : party.Code;
            var referenceLabel = party.LegalName.Length <= WorkInboxRulesV1.MaxReferenceLabelLength ? party.LegalName : party.Code;
            items.Add(new WorkInboxItemV1(ModuleName, KindName, row.Id, label, row.RequestedAtUtc.ToUniversalTime(), WorkInboxCompanyWideFenceV1.Link(DecisionScreenPath, row.Id),
                row.RequestedBy, rules, [new("party", referenceLabel, row.PartyId, link)]));
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, total > rows.Count);
    }
}
