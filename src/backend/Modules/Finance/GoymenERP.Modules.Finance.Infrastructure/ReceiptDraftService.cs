using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// The accepted Settings money operation for manual bank receipts, supplied by the host's
/// approved mapping (as for the draft read context, #261). Null keeps every save unavailable.
/// </summary>
public sealed record FinanceReceiptDraftBinding(string? MoneyOperation);

/// <summary>
/// The server-side ADR-009 audit envelope of the current request, built by the host from its
/// authenticated context; this module never makes one up. <see cref="PermissionCode"/> names the
/// action the permission port was asked; it is not a registered Identity grant.
/// </summary>
public sealed record FinanceAuditEnvelope(
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string SourceChannel,
    string? ClientIpAddress = null,
    string? UserAgent = null);

/// <summary>
/// FIN-T04a: persistent manual bank receipt DRAFTS behind the reviewed #258 contract. A draft is
/// preparation only: no money source, no balance, no allocation, no journal.
/// <list type="bullet">
/// <item><b>Authority:</b> server company/actor must be the request's; the <c>SaveDraft</c> action is
/// checked before anything is read and again after every owner await (the #261 fence), replay
/// included. Amount disclosure needs <c>ReadAmount</c> separately.</item>
/// <item><b>Owners:</b> bank account, party account and the Settings money profile are read through
/// the reviewed ports and validated by <see cref="PreparationValidator"/>. Each owner that is
/// unavailable fails closed with its own code; on main today every save is refused with
/// <c>bank_account_unavailable</c> or <c>settings_provider_unavailable</c>.</item>
/// <item><b>Idempotency (ADR-011):</b> (company, command, key hash) with the actor and the canonical
/// payload hash. The same key, payload and actor answers the stored result as a replay with its first
/// correlation; another payload or actor is an idempotency conflict (§2–§4). An authorized terminal
/// refusal — the draft is not the company's, or the expected version is stale — is recorded and
/// replayed too (§7), and the loser of a same-key race answers the winner's record (§8).</item>
/// <item><b>Concurrency:</b> <c>expectedVersion</c> against the stored version; a stale one changes
/// nothing. One save covers the draft, the command record and the audit event.</item>
/// </list>
/// List (FIN-T04b) pages the company's drafts newest first under <c>ReadSources</c>. Record and
/// Reverse are not part of this slice and answer <see cref="CommandOutcome.Unavailable"/>.
/// </summary>
public sealed class ReceiptDraftService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    IFinanceBankAccountReader banks,
    IFinancePartyAccountPort parties,
    ISettingsPrerequisiteProvider settings,
    IAuditEventCollector auditEvents,
    FinanceReceiptDraftBinding binding,
    FinanceAuditEnvelope envelope,
    TimeProvider timeProvider,
    FinanceAuditEnvelope? recordEnvelope = null,
    IFinanceRecordPeriodRule? periodRule = null) : IManualReceiptService
{
    public const string SaveDraftCommand = "finance.manual-receipt.draft.save";

    /// <summary>FIN-T05a: the Record command's name in the ADR-011 log.</summary>
    public const string RecordCommand = "finance.manual-receipt.record";

    private readonly IFinanceRecordPeriodRule _periodRule = periodRule ?? new UndecidedFinanceRecordPeriodRule();
    private const string EntityType = "Finance.ReceiptDraft";
    private const int IdentityMaximumLength = 128;
    private const int BankReferenceMaximumLength = 128;
    private const int DescriptionMaximumLength = 1000;
    internal const int ListPageSize = 50;

    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<CommandResult> DraftSaveAsync(FinanceContext context, CommandMetadata metadata,
        DraftSaveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        if (await FenceAsync(context, FinanceAction.SaveDraft, request.DraftId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var key = metadata.IdempotencyKey;
        if (!IsCanonical(key) || key.Length > IdentityMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));
        }

        if (context.ActorId.Length > IdentityMaximumLength || context.CorrelationId.Length > IdentityMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("context", "finance_value_does_not_fit"));
        }

        var payloadHash = PayloadHash(request);
        var keyHash = FinanceCommandKeys.Hash(key);
        if (await PriorAsync(context, SaveDraftCommand, FinanceAction.SaveDraft, keyHash, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (!PreparationValidator.IsValidDraftIdentity(request))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("draftId", "draft_identity_invalid"));
        }

        var preparation = request.Preparation;
        if (preparation is null || preparation.Money is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("preparation", "identity_required"));
        }

        if (FitIssues(preparation) is { Count: > 0 } fit)
        {
            return Refused(context, CommandOutcome.ValidationFailed, [.. fit]);
        }

        var operation = binding.MoneyOperation;
        if (string.IsNullOrWhiteSpace(operation))
        {
            return Refused(context, CommandOutcome.Unavailable, Issue("profile", "finance_operation_mapping_unavailable"));
        }

        ReceiptDraftRow? row = null;
        if (request.DraftId is { } draftId)
        {
            row = await dbContext.ReceiptDrafts.SingleOrDefaultAsync(candidate => candidate.Id == draftId, cancellationToken);
            if (row is null)
            {
                // Terminal (ADR-011 §7): the key keeps this answer; there is no draft to point at.
                return await TerminalAsync(context, SaveDraftCommand, FinanceAction.SaveDraft, keyHash, payloadHash, draftId: null,
                    Refused(context, CommandOutcome.ValidationFailed, Issue("draftId", "draft_not_found")), cancellationToken);
            }

            if (!string.Equals(VersionText(row.Version), request.ExpectedVersion, StringComparison.Ordinal))
            {
                // Terminal (§7): a retry of this key answers the same conflict even after the draft moves on.
                return await TerminalAsync(context, SaveDraftCommand, FinanceAction.SaveDraft, keyHash, payloadHash, row.Id,
                    Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                    cancellationToken);
            }

            if (row.Stage != ReceiptStages.Draft)
            {
                // FIN-T05a: a recorded source is never edited again; not terminal, the key stays free.
                return Refused(context, CommandOutcome.ValidationFailed, Issue("draftId", "source_not_draft"));
            }
        }

        // Owner reads, each followed by a fresh authority fence before its answer is used.
        var bankRead = await banks.GetAuthorizedAsync(context, preparation.TreasuryAccountId, cancellationToken);
        if (await FenceAsync(context, FinanceAction.SaveDraft, request.DraftId, cancellationToken) is { } afterBank)
        {
            return afterBank;
        }

        switch (bankRead.Outcome)
        {
            case ReadOutcome.Denied:
                return Refused(context, CommandOutcome.Denied);
            case ReadOutcome.NotFound:
                return Refused(context, CommandOutcome.ValidationFailed, Issue("treasuryAccountId", "bank_account_ineligible"));
            case ReadOutcome.Found when bankRead.Resource is not null:
                break;
            default:
                return Refused(context, CommandOutcome.Unavailable, Issue("treasuryAccountId", "bank_account_unavailable"));
        }

        var partyRead = await parties.GetAuthorizedAsync(context, preparation.PartyId, preparation.CompanyAccountId, cancellationToken);
        if (await FenceAsync(context, FinanceAction.SaveDraft, request.DraftId, cancellationToken) is { } afterParty)
        {
            return afterParty;
        }

        switch (partyRead.Outcome)
        {
            case ReadOutcome.Denied:
                return Refused(context, CommandOutcome.Denied);
            case ReadOutcome.NotFound:
                return Refused(context, CommandOutcome.ValidationFailed, Issue("companyAccountId", "party_account_ineligible"));
            case ReadOutcome.Found when partyRead.Resource is not null:
                break;
            default:
                return Refused(context, CommandOutcome.Unavailable, Issue("companyAccountId", "party_account_unavailable"));
        }

        IReadOnlyList<ValidationIssue> issues;
        try
        {
            issues = await new SettingsReceiptPrerequisites(settings).ValidatePreparationAsync(context,
                operation, preparation, bankRead.Resource, partyRead.Resource, cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, FinanceAction.SaveDraft, request.DraftId, cancellationToken) is { } fenced)
            {
                return fenced;
            }

            return exception.Code switch
            {
                "settings_access_denied" => Refused(context, CommandOutcome.Denied),
                // The chosen currency has no approved profile: an answer about the request, not an outage.
                SettingsPrerequisiteCodesV1.MoneyProfileNotFound =>
                    Refused(context, CommandOutcome.ValidationFailed, Issue("money.currencyId", exception.Code)),
                _ => Refused(context, CommandOutcome.Unavailable, Issue("profile", exception.Code)),
            };
        }

        if (await FenceAsync(context, FinanceAction.SaveDraft, request.DraftId, cancellationToken) is { } afterSettings)
        {
            return afterSettings;
        }

        if (issues.Count > 0)
        {
            return Refused(context, CommandOutcome.ValidationFailed, [.. issues]);
        }

        var now = timeProvider.GetUtcNow();
        long? versionBefore = row?.Version;
        var before = row is null ? "{}" : DraftAudit(row);
        if (row is null)
        {
            row = new ReceiptDraftRow
            {
                Id = Guid.NewGuid(),
                CompanyId = context.CompanyId,
                Version = 1,
                CreatedAtUtc = now,
                CreatedBy = context.ActorId,
            };
            Apply(row, preparation, now, context.ActorId);
            dbContext.ReceiptDrafts.Add(row);
        }
        else
        {
            Apply(row, preparation, now, context.ActorId);
            row.Version = checked(row.Version + 1);
        }

        var amountDecision = await permissions.CheckAsync(context, FinanceAction.ReadAmount, row.Id, cancellationToken);
        var result = new CommandResult(CommandOutcome.Succeeded, Detail(row, AmountDisclosure.Visible), IsReplay: false,
            context.CorrelationId, []);
        dbContext.Commands.Add(new FinanceCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = SaveDraftCommand,
            IdempotencyKeyHash = keyHash,
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            DraftId = row.Id,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });
        Audit(context, metadata, row.Id, versionBefore is null ? "finance.receipt-draft.created.v1" : "finance.receipt-draft.updated.v1",
            versionBefore is null ? "Create" : "Update", versionBefore, row.Version, before, DraftAudit(row));

        if (await SaveAsync(context, SaveDraftCommand, FinanceAction.SaveDraft, keyHash, payloadHash, cancellationToken) is { } lost)
        {
            return lost;
        }

        return Disclose(result, amountDecision);
    }

    public async Task<ReadResult<SourceDetail>> DetailAuthorizedAsync(FinanceContext context, SourceDetailRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return new ReadResult<SourceDetail>(ReadOutcome.Denied, null);
        }

        var read = await permissions.CheckAsync(context, FinanceAction.ReadSources, request.SourceId, cancellationToken);
        if (read != PermissionDecision.Allowed || !SameContext(context))
        {
            return new ReadResult<SourceDetail>(read == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied, null);
        }

        var row = await dbContext.ReceiptDrafts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == request.SourceId, cancellationToken);
        var amount = await permissions.CheckAsync(context, FinanceAction.ReadAmount, request.SourceId, cancellationToken);
        Guid? reversalSourceId = null;
        Guid? reversesSourceId = null;
        string? unallocatedText = null;
        if (row is not null)
        {
            // F-03: an allocated source's unallocated amount is its allocation state's, not the whole source amount.
            unallocatedText = await dbContext.SourceAllocationStates.AsNoTracking()
                .Where(state => state.SourceId == row.Id)
                .Select(state => state.UnallocatedAmountText)
                .SingleOrDefaultAsync(cancellationToken);
            reversalSourceId = await dbContext.ReversalProposals.AsNoTracking()
                .Where(proposal => proposal.Status == ReversalProposalStatuses.Approved
                    && proposal.OriginalSourceId == row.Id)
                .Select(proposal => proposal.ReversalSourceId)
                .SingleOrDefaultAsync(cancellationToken);
            reversesSourceId = await dbContext.ReversalProposals.AsNoTracking()
                .Where(proposal => proposal.Status == ReversalProposalStatuses.Approved
                    && proposal.ReversalSourceId == row.Id)
                .Select(proposal => (Guid?)proposal.OriginalSourceId)
                .SingleOrDefaultAsync(cancellationToken);
        }
        var again = await permissions.CheckAsync(context, FinanceAction.ReadSources, request.SourceId, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
        {
            return new ReadResult<SourceDetail>(again == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied, null);
        }

        return row is null
            ? new ReadResult<SourceDetail>(ReadOutcome.NotFound, null)
            : new ReadResult<SourceDetail>(ReadOutcome.Found,
                Detail(row, Disclosure(amount), reversalSourceId, reversesSourceId, unallocatedText));
    }

    /// <summary>
    /// The company's drafts, newest created first (<c>CreatedAtUtc desc, Id desc</c>): an edit never
    /// moves a draft between pages. The cursor is opaque and bound to the company and the ordering; a
    /// malformed or foreign cursor reads nothing. The page size is a fixed technical constant, not a
    /// policy. The amount is disclosed only with a fresh <c>ReadAmount</c>, as in the detail.
    /// </summary>
    public async Task<ReadResult<SourcePage>> ListAuthorizedAsync(FinanceContext context, SourceListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return new ReadResult<SourcePage>(ReadOutcome.Denied, null);
        }

        var read = await permissions.CheckAsync(context, FinanceAction.ReadSources, null, cancellationToken);
        if (read != PermissionDecision.Allowed || !SameContext(context))
        {
            return new ReadResult<SourcePage>(read == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied, null);
        }

        var query = dbContext.ReceiptDrafts.AsNoTracking();
        if (request.Cursor is not null)
        {
            if (DraftListCursor.Decode(request.Cursor, context.CompanyId) is not { } after)
            {
                return new ReadResult<SourcePage>(ReadOutcome.NotFound, null);
            }

            var afterCreatedAtUtc = after.CreatedAtUtc;
            var afterId = after.Id;
            query = query.Where(row => row.CreatedAtUtc < afterCreatedAtUtc
                || (row.CreatedAtUtc == afterCreatedAtUtc && row.Id.CompareTo(afterId) < 0));
        }

        var rows = await query
            .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id)
            .Take(ListPageSize + 1)
            .ToListAsync(cancellationToken);
        var amount = await permissions.CheckAsync(context, FinanceAction.ReadAmount, null, cancellationToken);
        var again = await permissions.CheckAsync(context, FinanceAction.ReadSources, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
        {
            return new ReadResult<SourcePage>(again == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied, null);
        }

        var page = rows.Take(ListPageSize).ToList();
        var next = rows.Count > ListPageSize
            ? DraftListCursor.Encode(context.CompanyId, page[^1].CreatedAtUtc, page[^1].Id)
            : null;
        return new ReadResult<SourcePage>(ReadOutcome.Found,
            new SourcePage(context.CompanyId, [.. page.Select(row => Detail(row, Disclosure(amount)).Source)], next));
    }

    /// <summary>
    /// FIN-T05a (card #66 5743678389): the Record of a realized manual bank receipt — ONE money event, exactly once.
    /// <list type="bullet">
    /// <item><b>Authority:</b> the narrow <c>Record</c> name (Ö4: one authorised operator, no second approval), measured
    /// fresh before anything is read, after every owner read and right before the one save; replay included.</item>
    /// <item><b>ADR-011:</b> its own command name in the log; the same key, request and actor replays; a missing source
    /// and a stale version are terminal (§7); a state refusal (not a draft) or an owner or period refusal is not, so a
    /// corrected retry is measured again; the loser of a race answers the winner's record (§8).</item>
    /// <item><b>At record time</b> the owners are read again: the bank account (approved, usable, same currency), the
    /// party account (active) and the Settings money profile (in effect, same profile, the stored amount within its
    /// scale — Ö2, never rounded).</item>
    /// <item><b>Period (Ö5):</b> <see cref="IFinanceRecordPeriodRule"/>. Which date decides was answered by Sinan's
    /// K1 (#66 <c>5748258299</c>, closing <c>5745134466</c>): <c>occurredOn</c>. The host registers the rule that reads
    /// Accounting's period port under the CALLER's own <c>accounting.periods.read</c>; this module does not see
    /// Accounting and does not map the date itself. A rule that could not decide — none registered, or the period
    /// owner unreadable — refuses with <c>finance_period_rule_unavailable</c>, which is NOT
    /// <c>finance_period_not_open</c>; either way nothing below runs.</item>
    /// <item><b>One save:</b> the source turns Recorded (its version moves once), ONE <c>MoneyEvents</c> row, the
    /// command record and the audit event. A second event for the same source is refused by the unique index even to a
    /// lost race. The bank inflow and the customer credit are reads of that one row; no allocation, journal or
    /// official document is made.</item>
    /// </list>
    /// </summary>
    public async Task<CommandResult> RecordAsync(FinanceContext context, CommandMetadata metadata, RecordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        if (await FenceAsync(context, FinanceAction.Record, request.DraftId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var key = metadata.IdempotencyKey;
        if (!IsCanonical(key) || key.Length > IdentityMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));
        }

        if (context.ActorId.Length > IdentityMaximumLength || context.CorrelationId.Length > IdentityMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("context", "finance_value_does_not_fit"));
        }

        var payloadHash = RecordPayloadHash(request);
        var keyHash = FinanceCommandKeys.Hash(key);
        if (await PriorAsync(context, RecordCommand, FinanceAction.Record, keyHash, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (request.DraftId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExpectedVersion))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("draftId", "draft_identity_invalid"));
        }

        var operation = binding.MoneyOperation;
        if (string.IsNullOrWhiteSpace(operation))
        {
            return Refused(context, CommandOutcome.Unavailable, Issue("profile", "finance_operation_mapping_unavailable"));
        }

        var row = await dbContext.ReceiptDrafts.SingleOrDefaultAsync(candidate => candidate.Id == request.DraftId, cancellationToken);
        if (row is null)
        {
            return await TerminalAsync(context, RecordCommand, FinanceAction.Record, keyHash, payloadHash, draftId: null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("draftId", "draft_not_found")), cancellationToken);
        }

        if (!string.Equals(VersionText(row.Version), request.ExpectedVersion, StringComparison.Ordinal))
        {
            return await TerminalAsync(context, RecordCommand, FinanceAction.Record, keyHash, payloadHash, row.Id,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")), cancellationToken);
        }

        if (row.Stage != ReceiptStages.Draft)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("draftId", "source_not_draft"));
        }

        // The stored preparation, exactly as saved, re-checked against the owners' CURRENT answers.
        var preparation = new ReceiptPreparation(row.PartyId, row.CompanyAccountId, row.TreasuryAccountId,
            new MoneyText(row.AmountText, row.CurrencyId), new ProfileReference(row.ProfileId, row.ProfileVersion),
            row.OccurredOn, row.ValueOn, row.BankReference, row.Description, PosInput(row));

        var bankRead = await banks.GetAuthorizedAsync(context, row.TreasuryAccountId, cancellationToken);
        if (await FenceAsync(context, FinanceAction.Record, row.Id, cancellationToken) is { } afterBank)
        {
            return afterBank;
        }

        switch (bankRead.Outcome)
        {
            case ReadOutcome.Denied:
                return Refused(context, CommandOutcome.Denied);
            case ReadOutcome.NotFound:
                return Refused(context, CommandOutcome.ValidationFailed, Issue("treasuryAccountId", "bank_account_ineligible"));
            case ReadOutcome.Found when bankRead.Resource is not null:
                break;
            default:
                return Refused(context, CommandOutcome.Unavailable, Issue("treasuryAccountId", "bank_account_unavailable"));
        }

        var partyRead = await parties.GetAuthorizedAsync(context, row.PartyId, row.CompanyAccountId, cancellationToken);
        if (await FenceAsync(context, FinanceAction.Record, row.Id, cancellationToken) is { } afterParty)
        {
            return afterParty;
        }

        switch (partyRead.Outcome)
        {
            case ReadOutcome.Denied:
                return Refused(context, CommandOutcome.Denied);
            case ReadOutcome.NotFound:
                return Refused(context, CommandOutcome.ValidationFailed, Issue("companyAccountId", "party_account_ineligible"));
            case ReadOutcome.Found when partyRead.Resource is not null:
                break;
            default:
                return Refused(context, CommandOutcome.Unavailable, Issue("companyAccountId", "party_account_unavailable"));
        }

        IReadOnlyList<ValidationIssue> issues;
        try
        {
            issues = await new SettingsReceiptPrerequisites(settings).ValidatePreparationAsync(context,
                operation, preparation, bankRead.Resource, partyRead.Resource, cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, FinanceAction.Record, row.Id, cancellationToken) is { } fenced)
            {
                return fenced;
            }

            return exception.Code switch
            {
                "settings_access_denied" => Refused(context, CommandOutcome.Denied),
                SettingsPrerequisiteCodesV1.MoneyProfileNotFound =>
                    Refused(context, CommandOutcome.ValidationFailed, Issue("money.currencyId", exception.Code)),
                _ => Refused(context, CommandOutcome.Unavailable, Issue("profile", exception.Code)),
            };
        }

        if (await FenceAsync(context, FinanceAction.Record, row.Id, cancellationToken) is { } afterSettings)
        {
            return afterSettings;
        }

        if (issues.Count > 0)
        {
            return Refused(context, CommandOutcome.ValidationFailed, [.. issues]);
        }

        // Ö5: recorded only in an OPEN period. Which date decides is Sinan's K1 (#66 5748258299): occurredOn. Both
        // dates are handed over because the mapping belongs to the rule, not here; a refusal lands before any write.
        var now = timeProvider.GetUtcNow();
        var period = await _periodRule.CheckAsync(context, row.OccurredOn, row.ValueOn, now, cancellationToken);
        if (await FenceAsync(context, FinanceAction.Record, row.Id, cancellationToken) is { } afterPeriod)
        {
            return afterPeriod;
        }

        switch (period.Outcome)
        {
            case RecordPeriodOutcome.Open:
                break;
            case RecordPeriodOutcome.NotOpen:
                return Refused(context, CommandOutcome.ValidationFailed,
                    Issue("occurredOn", period.Code ?? RecordPeriodAnswer.PeriodNotOpenCode));
            default:
                // A definite refusal the user can read (422 with its code), not an outage: a 503 would put the screen into
                // "result unknown, resend" for a rule that is simply not decided. Not terminal: the key stays free.
                return Refused(context, CommandOutcome.ValidationFailed,
                    Issue("period", period.Code ?? RecordPeriodAnswer.RuleUnavailableCode));
        }

        var versionBefore = row.Version;
        var before = DraftAudit(row);
        row.Stage = ReceiptStages.Recorded;
        row.RecordedAtUtc = now;
        row.RecordedBy = context.ActorId;
        row.Version = checked(row.Version + 1);
        dbContext.MoneyEvents.Add(new MoneyEventRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            SourceId = row.Id,
            Kind = MoneyEventKinds.ManualBankReceiptRecorded,
            PartyId = row.PartyId,
            CompanyAccountId = row.CompanyAccountId,
            TreasuryAccountId = row.TreasuryAccountId,
            CurrencyId = row.CurrencyId,
            AmountText = row.AmountText,
            ProfileId = row.ProfileId,
            ProfileVersion = row.ProfileVersion,
            OccurredOn = row.OccurredOn,
            ValueOn = row.ValueOn,
            RecordedAtUtc = now,
            RecordedBy = context.ActorId,
            CorrelationId = context.CorrelationId,
            // DEC-FIN-014: the same one event carries the POS facts; the customer credit stays the gross AmountText.
            PosCommissionAmountText = row.PosCommissionAmountText,
            PosBankNetAmountText = row.PosBankNetAmountText,
            PosCommissionBearer = row.PosCommissionBearer,
        });

        var amountDecision = await permissions.CheckAsync(context, FinanceAction.ReadAmount, row.Id, cancellationToken);
        var result = new CommandResult(CommandOutcome.Succeeded, Detail(row, AmountDisclosure.Visible), IsReplay: false,
            context.CorrelationId, []);
        dbContext.Commands.Add(new FinanceCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = RecordCommand,
            IdempotencyKeyHash = keyHash,
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            DraftId = row.Id,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });
        Audit(context, metadata, row.Id, "finance.manual-receipt.recorded.v1", "Record", versionBefore, row.Version, before,
            DraftAudit(row), recordEnvelope ?? envelope, row.PosCommissionBearer is null
                ? "Manuel banka tahsilatı kesin kayda alındı (tek para olayı)"
                : "Müşteri POS tahsilatı kesin kayda alındı (tek para olayı; müşteri brüt tutarla alacaklanır, banka neti ve komisyon ayrı)");

        // The last fence that can still prevent the write; none after the commit.
        if (await FenceAsync(context, FinanceAction.Record, row.Id, cancellationToken) is { } beforeSave)
        {
            dbContext.ChangeTracker.Clear();
            return beforeSave;
        }

        if (await SaveAsync(context, RecordCommand, FinanceAction.Record, keyHash, payloadHash, cancellationToken) is { } lost)
        {
            return lost;
        }

        return Disclose(result, amountDecision);
    }

    public Task<CommandResult> ReverseAsync(FinanceContext context, CommandMetadata metadata, ReverseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Refused(context, SameContext(context) ? CommandOutcome.Unavailable : CommandOutcome.Denied));
    }

    /// <summary>The canonical content of a save request: the exact contract JSON of the whole request.</summary>
    internal static string PayloadHash(DraftSaveRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json)))).ToLowerInvariant();

    /// <summary>The canonical content of a record request: the exact contract JSON of the whole request.</summary>
    internal static string RecordPayloadHash(RecordRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json)))).ToLowerInvariant();

    /// <summary>The lossless version text the contract carries.</summary>
    internal static string VersionText(long version) => version.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// ADR-011: the key's stored result — a success or a recorded terminal refusal — answered as a replay
    /// under a fresh fence; another payload or actor under the same key is a conflict that discloses
    /// nothing of the earlier result (§3). Null when the key holds nothing.
    /// </summary>
    private async Task<CommandResult?> PriorAsync(FinanceContext context, string commandName, FinanceAction action, string keyHash,
        string payloadHash, CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (prior is null)
        {
            return null;
        }

        return prior.PayloadHash == payloadHash && string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal)
            ? await ReplayAsync(context, action, prior, cancellationToken)
            : Refused(context, CommandOutcome.IdempotencyConflict);
    }

    /// <summary>
    /// One save; null when it committed. If it loses a race (a unique key or the row version), nothing
    /// of it stays tracked and the key is read afresh: another caller of the same key that won is this
    /// answer too (ADR-011 §8); a race that left this key empty is a <c>concurrent_change</c>.
    /// </summary>
    private async Task<CommandResult?> SaveAsync(FinanceContext context, string commandName, FinanceAction action, string keyHash,
        string payloadHash, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            return await PriorAsync(context, commandName, action, keyHash, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }
    }

    /// <summary>
    /// ADR-011 §7: an authorized terminal refusal is the key's result for good. It is recorded alone
    /// (no business change, no audit event) and answered; a retry of the key replays it.
    /// </summary>
    private async Task<CommandResult> TerminalAsync(FinanceContext context, string commandName, FinanceAction action, string keyHash,
        string payloadHash, Guid? draftId, CommandResult refusal, CancellationToken cancellationToken)
    {
        dbContext.Commands.Add(new FinanceCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            DraftId = draftId,
            ResultJson = JsonSerializer.Serialize(refusal, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = timeProvider.GetUtcNow(),
        });
        return await SaveAsync(context, commandName, action, keyHash, payloadHash, cancellationToken) ?? refusal;
    }

    private async Task<CommandResult> ReplayAsync(FinanceContext context, FinanceAction action, FinanceCommandRow prior,
        CancellationToken cancellationToken)
    {
        var stored = JsonSerializer.Deserialize<CommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı komut sonucu okunamadı.");
        var amount = await permissions.CheckAsync(context, FinanceAction.ReadAmount, prior.DraftId, cancellationToken);
        if (await FenceAsync(context, action, prior.DraftId, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        return Disclose(stored with { IsReplay = true }, amount);
    }

    /// <summary>Company/actor still the request's, then a fresh decision on the action.</summary>
    private async Task<CommandResult?> FenceAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        var decision = await permissions.CheckAsync(context, action, sourceId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("permission", "finance_permission_mapping_unavailable"));
        }

        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private static void Apply(ReceiptDraftRow row, ReceiptPreparation preparation, DateTimeOffset now, string actor)
    {
        row.PartyId = preparation.PartyId;
        row.CompanyAccountId = preparation.CompanyAccountId;
        row.TreasuryAccountId = preparation.TreasuryAccountId;
        row.CurrencyId = preparation.Money.CurrencyId;
        row.AmountText = preparation.Money.Amount;
        row.ProfileId = preparation.Profile.ProfileId;
        row.ProfileVersion = preparation.Profile.Version;
        row.OccurredOn = preparation.OccurredOn;
        row.ValueOn = preparation.ValueOn;
        row.BankReference = preparation.BankReference;
        row.Description = preparation.Description;
        // DEC-FIN-014: the validator already refused a commission that is not below the gross, so the net exists.
        if (preparation.Pos is { } pos && PosCollectionRules.TryBankNet(preparation.Money.Amount, pos.CommissionAmount, out var net))
        {
            row.PosCommissionAmountText = pos.CommissionAmount;
            row.PosBankNetAmountText = net;
            row.PosCommissionBearer = (int)pos.CommissionBearer;
        }
        else
        {
            row.PosCommissionAmountText = null;
            row.PosBankNetAmountText = null;
            row.PosCommissionBearer = null;
        }

        row.UpdatedAtUtc = now;
        row.UpdatedBy = actor;
    }

    /// <summary>The stored POS facts as the preparation input they came from (the net is always re-derived).</summary>
    private static PosCollectionInput? PosInput(ReceiptDraftRow row) =>
        row.PosCommissionBearer is { } bearer && row.PosCommissionAmountText is { } commission
            ? new PosCollectionInput(commission, (PosCommissionBearer)bearer)
            : null;

    /// <summary>Storage limits only, refused and never cut; not a money or document policy.</summary>
    private static List<ValidationIssue> FitIssues(ReceiptPreparation preparation)
    {
        var issues = new List<ValidationIssue>();
        if (preparation.Profile is not null
            && ((preparation.Profile.ProfileId?.Length ?? 0) > 36 || (preparation.Profile.Version?.Length ?? 0) > 20))
        {
            issues.Add(Issue("profile", "finance_value_does_not_fit"));
        }

        if ((preparation.BankReference?.Length ?? 0) > BankReferenceMaximumLength)
        {
            issues.Add(Issue("bankReference", "finance_value_does_not_fit"));
        }

        if ((preparation.Description?.Length ?? 0) > DescriptionMaximumLength)
        {
            issues.Add(Issue("description", "finance_value_does_not_fit"));
        }

        return issues;
    }

    private static SourceDetail Detail(ReceiptDraftRow row, AmountDisclosure disclosure,
        Guid? reversalSourceId = null, Guid? reversesSourceId = null, string? unallocatedText = null)
    {
        AmountView Shown(string text) => disclosure == AmountDisclosure.Visible
            ? new AmountView(AmountDisclosure.Visible, new MoneyText(text, row.CurrencyId))
            : new AmountView(disclosure, null);
        var amount = Shown(row.AmountText);
        var recorded = row.Stage == ReceiptStages.Recorded;
        var summary = new SourceSummary(row.Id, row.CompanyId, row.Id.ToString("D"), VersionText(row.Version),
            reversalSourceId is not null ? SourceStage.Reversed : recorded ? SourceStage.Recorded : SourceStage.Draft,
            row.PartyId, row.CompanyAccountId, row.TreasuryAccountId, row.CurrencyId, row.OccurredOn, row.ValueOn, amount);
        // A draft has no allocation fact: unknown, never 0. A recorded source's unallocated amount is exact: its F-03
        // allocation state when one exists, otherwise the source amount - 0 active allocations (card #66 5743678389 §2).
        // For a POS collection that amount is the GROSS (DEC-FIN-014): the customer's debt closes at it.
        var unallocated = recorded
            ? new UnallocatedView(AllocationKnowledge.Known, unallocatedText is null ? amount : Shown(unallocatedText))
            : new UnallocatedView(AllocationKnowledge.Unavailable, new AmountView(AmountDisclosure.Unavailable, null));
        var pos = row.PosCommissionBearer is { } bearer
            ? new PosCollectionView((PosCommissionBearer)bearer, Shown(row.PosCommissionAmountText!),
                Shown(row.PosBankNetAmountText!))
            : null;
        return new SourceDetail(summary, new ProfileReference(row.ProfileId, row.ProfileVersion), row.BankReference,
            row.Description, RecordedAtUtc: row.RecordedAtUtc, ReversalSourceId: reversalSourceId,
            ReversesSourceId: reversesSourceId, unallocated, pos);
    }

    /// <summary>Applies a FRESH amount decision to a result, so a replay never discloses more than now allowed.</summary>
    private static CommandResult Disclose(CommandResult result, PermissionDecision amount)
    {
        if (result.Resource is not { } detail || amount == PermissionDecision.Allowed)
        {
            return result;
        }

        var hidden = new AmountView(Disclosure(amount), null);
        return result with
        {
            Resource = detail with
            {
                Source = detail.Source with { Amount = hidden },
                Pos = detail.Pos is { } pos ? pos with { Commission = hidden, BankNet = hidden } : null,
            },
        };
    }

    private static AmountDisclosure Disclosure(PermissionDecision decision) => decision switch
    {
        PermissionDecision.Allowed => AmountDisclosure.Visible,
        PermissionDecision.Denied => AmountDisclosure.Redacted,
        _ => AmountDisclosure.Unavailable,
    };

    private void Audit(FinanceContext context, CommandMetadata metadata, Guid draftId, string eventType, string operation,
        long? versionBefore, long? versionAfter, string before, string after)
        => Audit(context, metadata, draftId, eventType, operation, versionBefore, versionAfter, before, after, envelope,
            "Manuel banka tahsilatı taslağı (para etkisi yok)");

    private void Audit(FinanceContext context, CommandMetadata metadata, Guid draftId, string eventType, string operation,
        long? versionBefore, long? versionAfter, string before, string after, FinanceAuditEnvelope audit, string reason)
    {
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId,
            eventType,
            EntityType,
            draftId.ToString("D"),
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            context.ActorId,
            audit.PermissionCode,
            audit.RoleCodes,
            before,
            after,
            reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            audit.SourceChannel,
            audit.ClientIpAddress,
            audit.UserAgent,
            context.CorrelationId,
            context.CausationId,
            metadata.IdempotencyKey));
    }

    /// <summary>The audit payload names the draft's identities and dates; the amount is not copied into audit JSON.</summary>
    private static string DraftAudit(ReceiptDraftRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        version = VersionText(row.Version),
        stage = row.Stage == ReceiptStages.Recorded ? "Recorded" : "Draft",
        row.RecordedAtUtc,
        row.PartyId,
        row.CompanyAccountId,
        row.TreasuryAccountId,
        row.CurrencyId,
        row.OccurredOn,
        row.ValueOn,
        profile = new { row.ProfileId, row.ProfileVersion },
        // DEC-FIN-014: which kind of collection and who bears the commission; the amounts stay out of audit JSON.
        channel = row.PosCommissionBearer is null ? "BankTransfer" : "Pos",
        posCommissionBearer = row.PosCommissionBearer is { } bearer ? ((PosCommissionBearer)bearer).ToString() : null,
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static CommandResult Refused(FinanceContext context, CommandOutcome outcome, params ValidationIssue[] issues) =>
        new CommandResult(outcome, null, IsReplay: false, context.CorrelationId, issues);
}
