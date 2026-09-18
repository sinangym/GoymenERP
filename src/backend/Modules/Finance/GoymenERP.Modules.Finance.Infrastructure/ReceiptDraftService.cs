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
/// correlation; another payload or actor is an idempotency conflict.</item>
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
    TimeProvider timeProvider) : IManualReceiptService
{
    public const string SaveDraftCommand = "finance.manual-receipt.draft.save";
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
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == SaveDraftCommand && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (prior is not null)
        {
            // ADR-011 §3: another payload or another actor under the same key is a conflict, and the
            // earlier result is not disclosed.
            return prior.PayloadHash == payloadHash && string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal)
                ? await ReplayAsync(context, prior, cancellationToken)
                : Refused(context, CommandOutcome.IdempotencyConflict);
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
                return Refused(context, CommandOutcome.ValidationFailed, Issue("draftId", "draft_not_found"));
            }

            if (!string.Equals(VersionText(row.Version), request.ExpectedVersion, StringComparison.Ordinal))
            {
                return Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict"));
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

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
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
        var again = await permissions.CheckAsync(context, FinanceAction.ReadSources, request.SourceId, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
        {
            return new ReadResult<SourceDetail>(again == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied, null);
        }

        return row is null
            ? new ReadResult<SourceDetail>(ReadOutcome.NotFound, null)
            : new ReadResult<SourceDetail>(ReadOutcome.Found, Detail(row, Disclosure(amount)));
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

            query = query.Where(row => row.CreatedAtUtc < after.CreatedAtUtc
                || (row.CreatedAtUtc == after.CreatedAtUtc && row.Id.CompareTo(after.Id) < 0));
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

    public Task<CommandResult> RecordAsync(FinanceContext context, CommandMetadata metadata, RecordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        // Record is T05: money, period, FX and OPEN-009 authority are not part of a draft.
        return Task.FromResult(Refused(context, SameContext(context) ? CommandOutcome.Unavailable : CommandOutcome.Denied));
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

    /// <summary>The lossless version text the contract carries.</summary>
    internal static string VersionText(long version) => version.ToString(CultureInfo.InvariantCulture);

    private async Task<CommandResult> ReplayAsync(FinanceContext context, FinanceCommandRow prior, CancellationToken cancellationToken)
    {
        var stored = JsonSerializer.Deserialize<CommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı komut sonucu okunamadı.");
        var amount = await permissions.CheckAsync(context, FinanceAction.ReadAmount, prior.DraftId, cancellationToken);
        if (await FenceAsync(context, FinanceAction.SaveDraft, prior.DraftId, cancellationToken) is { } fenced)
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
        row.UpdatedAtUtc = now;
        row.UpdatedBy = actor;
    }

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

    private static SourceDetail Detail(ReceiptDraftRow row, AmountDisclosure disclosure)
    {
        var amount = disclosure == AmountDisclosure.Visible
            ? new AmountView(AmountDisclosure.Visible, new MoneyText(row.AmountText, row.CurrencyId))
            : new AmountView(disclosure, null);
        var summary = new SourceSummary(row.Id, row.CompanyId, row.Id.ToString("D"), VersionText(row.Version), SourceStage.Draft,
            row.PartyId, row.CompanyAccountId, row.TreasuryAccountId, row.CurrencyId, row.OccurredOn, row.ValueOn, amount);
        // A draft has no allocation fact: unknown, never 0.
        return new SourceDetail(summary, new ProfileReference(row.ProfileId, row.ProfileVersion), row.BankReference,
            row.Description, RecordedAtUtc: null, ReversalSourceId: null, ReversesSourceId: null,
            new UnallocatedView(AllocationKnowledge.Unavailable, new AmountView(AmountDisclosure.Unavailable, null)));
    }

    /// <summary>Applies a FRESH amount decision to a result, so a replay never discloses more than now allowed.</summary>
    private static CommandResult Disclose(CommandResult result, PermissionDecision amount)
    {
        if (result.Resource is not { } detail || amount == PermissionDecision.Allowed)
        {
            return result;
        }

        var hidden = new AmountView(Disclosure(amount), null);
        return result with { Resource = detail with { Source = detail.Source with { Amount = hidden } } };
    }

    private static AmountDisclosure Disclosure(PermissionDecision decision) => decision switch
    {
        PermissionDecision.Allowed => AmountDisclosure.Visible,
        PermissionDecision.Denied => AmountDisclosure.Redacted,
        _ => AmountDisclosure.Unavailable,
    };

    private void Audit(FinanceContext context, CommandMetadata metadata, Guid draftId, string eventType, string operation,
        long? versionBefore, long? versionAfter, string before, string after)
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
            envelope.PermissionCode,
            envelope.RoleCodes,
            before,
            after,
            "Manuel banka tahsilatı taslağı (para etkisi yok)",
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            context.CorrelationId,
            context.CausationId,
            metadata.IdempotencyKey));
    }

    /// <summary>The audit payload names the draft's identities and dates; the amount is not copied into audit JSON.</summary>
    private static string DraftAudit(ReceiptDraftRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        version = VersionText(row.Version),
        stage = "Draft",
        row.PartyId,
        row.CompanyAccountId,
        row.TreasuryAccountId,
        row.CurrencyId,
        row.OccurredOn,
        row.ValueOn,
        profile = new { row.ProfileId, row.ProfileVersion },
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static CommandResult Refused(FinanceContext context, CommandOutcome outcome, params ValidationIssue[] issues) =>
        new CommandResult(outcome, null, IsReplay: false, context.CorrelationId, issues);
}
