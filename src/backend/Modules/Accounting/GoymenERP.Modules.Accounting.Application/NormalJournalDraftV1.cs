using System.Collections.ObjectModel;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>An explicit entry in an approved posting rule; its side/account are not inferred.</summary>
public sealed record NormalJournalMappingV1(string MappingKey, PostingSide Side, AccountRef Account);

/// <summary>
/// Trusted, user-approved rule snapshot tied to one exact mirror and purpose. The real mapping data
/// and its approval are external prerequisites, not supplied by this package. Never construct this
/// object directly from an untrusted command body or infer rules from account-code prefixes.
/// </summary>
public sealed class NormalJournalPostingRuleV1
{
    private readonly ReadOnlyCollection<NormalJournalMappingV1> _mappings;
    private readonly Dictionary<(string Key, PostingSide Side), AccountRef> _accounts;

    public NormalJournalPostingRuleV1(AccountPlanMirrorV1 mirror, string postingPurpose, string version,
        Guid approvedByUserId, DateTimeOffset approvedAtUtc, IEnumerable<NormalJournalMappingV1> mappings)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(mappings);
        AccountPlanMirrorV1.RequireText(postingPurpose, nameof(postingPurpose));
        AccountPlanMirrorV1.RequireText(version, nameof(version));
        if (approvedByUserId == Guid.Empty || approvedAtUtc == default || approvedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                "The rule must carry explicit UTC user-approval provenance.");
        }

        var copy = mappings.ToArray();
        _accounts = new Dictionary<(string Key, PostingSide Side), AccountRef>();
        foreach (var entry in copy)
        {
            if (entry is null || !Enum.IsDefined(entry.Side) || mirror.Find(entry.Account) is null)
            {
                throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                    "Each rule entry needs a known side and an exact account from the approved mirror.");
            }

            AccountPlanMirrorV1.RequireText(entry.MappingKey, nameof(entry.MappingKey));
            if (!_accounts.TryAdd((entry.MappingKey, entry.Side), entry.Account))
            {
                throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                    "A rule cannot contain ambiguous duplicate mapping-key/side entries.");
            }
        }

        Mirror = mirror;
        PostingPurpose = postingPurpose;
        Version = version;
        ApprovedByUserId = approvedByUserId;
        ApprovedAtUtc = approvedAtUtc;
        _mappings = Array.AsReadOnly(copy);
    }

    public AccountPlanMirrorV1 Mirror { get; }
    public string PostingPurpose { get; }
    public string Version { get; }
    public Guid ApprovedByUserId { get; }
    public DateTimeOffset ApprovedAtUtc { get; }
    public IReadOnlyList<NormalJournalMappingV1> Mappings => _mappings;

    public AccountRef? Find(string mappingKey, PostingSide side) =>
        _accounts.TryGetValue((mappingKey, side), out var account) ? account : null;
}

/// <summary>
/// The result of pure preparation, not of a posted command. Exception/Pending are existing ledger
/// payloads for the future atomic handler to queue; this class does NOT claim a durable queue write.
/// Exactly one of Draft, Exception or Pending is populated by Prepare.
/// </summary>
public sealed class NormalJournalPreparationV1
{
    internal NormalJournalPreparationV1(NormalJournalDraftV1? draft = null, PostingException? exception = null,
        PendingValuation? pending = null)
    {
        if ((draft is null ? 0 : 1) + (exception is null ? 0 : 1) + (pending is null ? 0 : 1) != 1)
        {
            throw new ArgumentException("Preparation has exactly one outcome.");
        }

        Draft = draft;
        Exception = exception;
        Pending = pending;
    }

    public NormalJournalDraftV1? Draft { get; }
    public PostingException? Exception { get; }
    public PendingValuation? Pending { get; }
}

/// <summary>
/// A balanced NORMAL draft, not a posted Journal. No identity/version/time/amount/account is
/// invented, rounded, persisted or approved. Journal.Restore is deliberately not used to manufacture
/// a posted journal. The existing ledger/store remain the ONLY posting path.
/// </summary>
public sealed class NormalJournalDraftV1
{
    private NormalJournalDraftV1(PostingRequest request, long version, Guid preparedByUserId,
        NormalJournalPostingRuleV1 rule, JournalLine[] lines)
    {
        Request = request with { Lines = Array.AsReadOnly(request.Lines.ToArray()) };
        Version = version;
        PreparedByUserId = preparedByUserId;
        Rule = rule;
        Lines = Array.AsReadOnly(lines);
    }

    public Guid Id => Request.JournalId;
    public Guid CompanyId => Request.Source.CompanyId;
    public JournalKind Kind => JournalKind.Original;
    public long Version { get; }
    public Guid PreparedByUserId { get; }
    public PostingRequest Request { get; }
    public NormalJournalPostingRuleV1 Rule { get; }
    public IReadOnlyList<JournalLine> Lines { get; }
    public decimal TotalDebit => Lines.Sum(line => line.Debit);
    public decimal TotalCredit => Lines.Sum(line => line.Credit);

    public static NormalJournalPreparationV1 Prepare(JournalKind kind, PostingRequest request,
        long version, Guid preparedByUserId, AccountPlanMirrorV1 mirror, NormalJournalPostingRuleV1? rule)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentNullException.ThrowIfNull(request.Dates);
        ArgumentNullException.ThrowIfNull(request.Lines);
        ArgumentNullException.ThrowIfNull(mirror);
        if (kind != JournalKind.Original || request.JournalId == Guid.Empty || request.CurrencyId == Guid.Empty
            || version <= 0 || preparedByUserId == Guid.Empty)
        {
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                "Only an identified, versioned original journal draft is accepted here.");
        }

        AccountPlanMirrorV1.RequireText(request.Consumer, nameof(request.Consumer));
        AccountPlanMirrorV1.RequireText(request.MessageId, nameof(request.MessageId));
        AccountPlanMirrorV1.RequireText(request.PostingRuleVersion, nameof(request.PostingRuleVersion));
        if (request.Source.CompanyId != mirror.CompanyId)
        {
            throw new AccountingDomainException(AccountingErrorCodes.CompanyMismatch,
                "The source and the approved mirror must belong to the same company.");
        }

        // Snapshot caller-owned arrays BEFORE checking or retaining them.
        var proposed = request.Lines.ToArray();
        request = request with { Lines = Array.AsReadOnly(proposed) };
        if (proposed.Length < 2)
        {
            throw new AccountingDomainException(AccountingErrorCodes.JournalTooFewLines,
                "A normal draft needs at least two explicit proposed lines.");
        }

        foreach (var line in proposed)
        {
            if (line is null || !Enum.IsDefined(line.Side) || line.Amount is <= 0m || line.Account == default(AccountRef))
            {
                throw new AccountingDomainException(AccountingErrorCodes.JournalLineInvalid,
                    "A proposed line must have a valid side and, when known, a positive amount/account.");
            }

            AccountPlanMirrorV1.RequireText(line.MappingKey, nameof(line.MappingKey));
            if (line.Account is { } account && account.CompanyId != mirror.CompanyId)
            {
                throw new AccountingDomainException(AccountingErrorCodes.CompanyMismatch,
                    "A proposed account cannot belong to another company.");
            }
        }

        if (rule is not null && (!rule.Mirror.Matches(mirror)
            || !string.Equals(rule.PostingPurpose, request.Source.PostingPurpose, StringComparison.Ordinal)
            || !string.Equals(rule.Version, request.PostingRuleVersion, StringComparison.Ordinal)))
        {
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                "The approved rule must bind the exact mirror snapshot, purpose and rule version.");
        }

        var missing = new List<string>();
        foreach (var line in proposed)
        {
            var mapped = rule?.Find(line.MappingKey, line.Side);
            if (mapped is null || line.Account is null)
            {
                missing.Add(line.MappingKey);
            }
            else if (line.Account.Value != mapped.Value || mirror.Find(mapped.Value) is null)
            {
                throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                    "A caller-selected account cannot replace the approved mapping.");
            }
        }

        if (missing.Count > 0)
        {
            return new NormalJournalPreparationV1(exception: new PostingException(request.Source,
                request.PostingRuleVersion,
                Array.AsReadOnly(missing.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
                request.MessageId));
        }

        var awaitingAmount = proposed.Count(line => line.Amount is null);
        if (awaitingAmount > 0)
        {
            return new NormalJournalPreparationV1(pending: new PendingValuation(request.Source,
                request.PostingRuleVersion, awaitingAmount, request.MessageId));
        }

        var lines = proposed.Select(line => new JournalLine(line.Account!.Value,
            line.Side == PostingSide.Debit ? line.Amount!.Value : 0m,
            line.Side == PostingSide.Credit ? line.Amount!.Value : 0m)).ToArray();
        if (lines.Sum(line => line.Debit) != lines.Sum(line => line.Credit))
        {
            throw new AccountingDomainException(AccountingErrorCodes.JournalUnbalanced,
                "A normal draft must balance exactly; no balancing line is manufactured.");
        }

        return new NormalJournalPreparationV1(draft: new NormalJournalDraftV1(request, version,
            preparedByUserId, rule!, lines));
    }
}

/// <summary>
/// Trusted server-side dependency, NOT an HTTP field, permission declaration or grant. The central
/// owner must supply its eventually registered narrow normal-draft create/approve names. Existing
/// ACC-V0 read/period names and a shared create/approve name are refused. Supplying a name alone
/// authorizes nothing: the current authority service must grant THAT approval name company-wide.
/// </summary>
public sealed class NormalJournalPermissionBindingV1
{
    public NormalJournalPermissionBindingV1(string createPermission, string approvePermission)
    {
        AccountPlanMirrorV1.RequireText(createPermission, nameof(createPermission));
        AccountPlanMirrorV1.RequireText(approvePermission, nameof(approvePermission));
        if (string.Equals(createPermission, approvePermission, StringComparison.Ordinal)
            || AccountingPermissionManifestV1.All.Contains(createPermission, StringComparer.Ordinal)
            || AccountingPermissionManifestV1.All.Contains(approvePermission, StringComparer.Ordinal))
        {
            throw new ArgumentException("Normal draft create and approve need distinct owner-supplied narrow permissions.");
        }

        CreatePermission = createPermission;
        ApprovePermission = approvePermission;
    }

    public string CreatePermission { get; }
    public string ApprovePermission { get; }
}

/// <summary>
/// Read-only eligibility check on a supplied owner snapshot, NOT an approval command or a durable
/// approval receipt. Normal self-approval is permitted ONLY with the actual narrow approval grant.
/// No period/reversal/money operation is accepted or delegated here.
/// The future handler must load the current draft, enforce its version atomically, validate the open
/// period and amount scale, revalidate authority after awaited dependencies, and use the existing
/// command-log/audit/ledger save. A successful check MUST NOT replace those write gates or be cached
/// as authority. This source-only package neither persists nor issues a new command name.
/// </summary>
public sealed class NormalJournalDraftApprovalPolicyV1(
    NormalJournalPermissionBindingV1 permissions,
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser)
{
    public async Task<AccountingApiResult<NormalJournalDraftV1>> CheckAsync(NormalJournalDraftV1? draft,
        long expectedVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.IsAuthenticated || !Guid.TryParse(currentUser.UserId, out var actor)
            || actor == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return Denied();
        }

        var company = companyContext.CompanyId;
        if (!await HasAuthorityAsync(company, actor, cancellationToken))
        {
            return Denied();
        }

        if (draft is null || draft.CompanyId != company)
        {
            return new AccountingApiResult<NormalJournalDraftV1>(AccountingApiStatus.NotFound, null, false, null);
        }

        if (expectedVersion <= 0)
        {
            return new AccountingApiResult<NormalJournalDraftV1>(AccountingApiStatus.Invalid, null, false,
                AccountingApiCodes.RequestInvalid, "expectedVersion");
        }

        if (draft.Version != expectedVersion)
        {
            return new AccountingApiResult<NormalJournalDraftV1>(AccountingApiStatus.Conflict, null, false,
                AccountingApiCodes.VersionConflict, ExpectedVersion: expectedVersion, CurrentVersion: draft.Version);
        }

        // Re-check at release. In particular, create authority is NEVER used as a substitute.
        if (!await HasAuthorityAsync(company, actor, cancellationToken))
        {
            return Denied();
        }

        // Deliberately no preparer != actor rule: Business permits normal self-approval with this grant.
        return new AccountingApiResult<NormalJournalDraftV1>(AccountingApiStatus.Ok, draft, false, null);
    }

    private async Task<bool> HasAuthorityAsync(Guid company, Guid actor, CancellationToken cancellationToken)
    {
        var scope = await authority.RevalidatePermissionScopeAsync(permissions.ApprovePermission, company,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return scope is { IsGranted: true, IsCompanyWide: true }
            && companyContext.IsResolved && companyContext.CompanyId == company
            && currentUser.IsAuthenticated && currentUser.CanAccessCompany(company)
            && Guid.TryParse(currentUser.UserId, out var currentActor) && currentActor == actor;
    }

    private static AccountingApiResult<NormalJournalDraftV1> Denied() =>
        new AccountingApiResult<NormalJournalDraftV1>(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);
}
