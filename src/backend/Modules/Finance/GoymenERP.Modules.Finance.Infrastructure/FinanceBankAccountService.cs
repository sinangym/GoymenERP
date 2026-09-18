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
/// FS0 (card #66 5731563923): the Finance bank account master and its authorized reader, the
/// real source behind <see cref="IFinanceBankAccountReader"/>. It holds no balance and moves no money.
/// <list type="bullet">
/// <item><b>Fields:</b> exactly <see cref="BankAccountView"/>'s. Bank name, branch, IBAN, account
/// number, type, a currency change and closing are not decided and not built.</item>
/// <item><b>Lifecycle (Ö4, #66 5722637822):</b> a proposal is neither active nor usable; one approval
/// by an authorized user OTHER than the proposer makes it both. The database refuses an approval by
/// the proposer too (<c>CK_BankAccounts_Approval</c>).</item>
/// <item><b>Currency:</b> must be one of Settings' money currency candidates of the server-resolved
/// company, checked on propose and again on approve. Being listed proves existence only; money
/// validity is still the profile's, at draft save. An unavailable Settings source refuses, fail-closed.</item>
/// <item><b>Authority:</b> server company/actor must be the request's; the action is checked before
/// anything is read and again after every owner await, replay included.</item>
/// <item><b>Idempotency and concurrency:</b> the shared (company, actor, key) command record with the
/// canonical payload hash; the same key and payload replays, another payload or command conflicts.
/// Approve needs the expected version. One save covers the account, the command and the audit event.</item>
/// </list>
/// </summary>
public sealed class FinanceBankAccountService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    ISettingsMoneyCandidateReader currencies,
    IAuditEventCollector auditEvents,
    FinanceAuditEnvelope envelope,
    TimeProvider timeProvider) : IFinanceBankAccountMaster, IFinanceBankAccountReader
{
    public const string ProposeCommand = "finance.bank-account.propose";
    public const string ApproveCommand = "finance.bank-account.approve";
    private const string EntityType = "Finance.BankAccount";
    private const int Proposed = 1;
    private const int Approved = 2;
    private const int IdentityMaximumLength = 128;
    private const int DisplayNameMaximumLength = 200;

    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<BankAccountCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountProposeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (await StartAsync(context, metadata, FinanceAction.ProposeBankAccount, null, cancellationToken) is { } refused)
        {
            return refused;
        }

        // A null text cannot be the contract's JSON; refused before it is hashed.
        if (request.DisplayName is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("displayName", "display_name_invalid"));
        }

        var payloadHash = PayloadHash(ProposeCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ProposeBankAccount, ProposeCommand, payloadHash, cancellationToken)
            is { } prior)
        {
            return prior;
        }

        if (!IsCanonical(request.DisplayName))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("displayName", "display_name_invalid"));
        }

        if (request.DisplayName.Length > DisplayNameMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("displayName", "finance_value_does_not_fit"));
        }

        if (request.CurrencyId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("currencyId", "identity_required"));
        }

        if (await CurrencyRefusalAsync(context, FinanceAction.ProposeBankAccount, null, request.CurrencyId, cancellationToken)
            is { } currencyRefused)
        {
            return currencyRefused;
        }

        var now = timeProvider.GetUtcNow();
        var row = new BankAccountRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            CurrencyId = request.CurrencyId,
            DisplayName = request.DisplayName,
            Status = Proposed,
            Version = 1,
            ProposedBy = context.ActorId,
            ProposedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.BankAccounts.Add(row);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, ProposeCommand, payloadHash, row.Id, result, now);
        Audit(context, metadata, row.Id, "finance.bank-account.proposed.v1", "Propose", versionBefore: null, row.Version,
            "{}", AccountAudit(row), approvalContextJson: null, "Finans banka hesabı önerisi (para etkisi yok)");
        return await SaveAsync(context, result, cancellationToken);
    }

    public async Task<BankAccountCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountApproveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (await StartAsync(context, metadata, FinanceAction.ApproveBankAccount, request.AccountId, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        if (request.ExpectedVersion is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
        }

        var payloadHash = PayloadHash(ApproveCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ApproveBankAccount, ApproveCommand, payloadHash, cancellationToken)
            is { } prior)
        {
            return prior;
        }

        if (request.AccountId == Guid.Empty || !IsCanonical(request.ExpectedVersion))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "identity_required"));
        }

        var row = await dbContext.BankAccounts.SingleOrDefaultAsync(candidate => candidate.Id == request.AccountId, cancellationToken);
        if (row is null)
        {
            // Another company's account is not visible either: the same answer, nothing leaks.
            return Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "bank_account_not_found"));
        }

        if (!string.Equals(VersionText(row.Version), request.ExpectedVersion, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict"));
        }

        if (row.Status != Proposed)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "bank_account_not_proposed"));
        }

        // Ö4: a single authorized person other than the one who proposed it.
        if (string.Equals(row.ProposedBy, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.Denied, Issue("approval", "finance_approver_is_proposer"));
        }

        if (await CurrencyRefusalAsync(context, FinanceAction.ApproveBankAccount, row.Id, row.CurrencyId, cancellationToken)
            is { } currencyRefused)
        {
            return currencyRefused;
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = row.Version;
        var before = AccountAudit(row);
        row.Status = Approved;
        row.ApprovedBy = context.ActorId;
        row.ApprovedAtUtc = now;
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, ApproveCommand, payloadHash, row.Id, result, now);
        var approval = JsonSerializer.Serialize(new { rule = "O4", proposedBy = row.ProposedBy, approvedBy = context.ActorId },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Audit(context, metadata, row.Id, "finance.bank-account.approved.v1", "Approve", versionBefore, row.Version, before,
            AccountAudit(row), approval, "Finans banka hesabı onayı (hazırlayandan farklı tek onay; para etkisi yok)");
        return await SaveAsync(context, result, cancellationToken);
    }

    public async Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync(context, null, cancellationToken) is { } outcome)
        {
            return new ReadResult<BankAccountPage>(outcome, null);
        }

        var rows = await dbContext.BankAccounts.AsNoTracking()
            .OrderBy(row => row.DisplayName)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        if (await ReadFenceAsync(context, null, cancellationToken) is { } again)
        {
            return new ReadResult<BankAccountPage>(again, null);
        }

        return new ReadResult<BankAccountPage>(ReadOutcome.Found,
            new BankAccountPage(context.CompanyId, [.. rows.Select(View)]));
    }

    public async Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync(context, accountId, cancellationToken) is { } outcome)
        {
            return new ReadResult<BankAccountView>(outcome, null);
        }

        var row = await dbContext.BankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == accountId, cancellationToken);
        if (await ReadFenceAsync(context, accountId, cancellationToken) is { } again)
        {
            return new ReadResult<BankAccountView>(again, null);
        }

        return row is null
            ? new ReadResult<BankAccountView>(ReadOutcome.NotFound, null)
            : new ReadResult<BankAccountView>(ReadOutcome.Found, View(row));
    }

    /// <summary>The canonical content of a command: its name and the exact contract JSON of the request.</summary>
    internal static string PayloadHash<T>(string command, T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command + "|" + JsonSerializer.Serialize(request, Json))))
            .ToLowerInvariant();

    internal static string VersionText(long version) => version.ToString(CultureInfo.InvariantCulture);

    /// <summary>Approved is the only active and usable state; a proposal is never selectable.</summary>
    private static BankAccountView View(BankAccountRow row) =>
        new BankAccountView(row.Id, row.CompanyId, row.CurrencyId, row.DisplayName,
            IsActive: row.Status == Approved, IsUsableForReceipt: row.Status == Approved, VersionText(row.Version));

    /// <summary>Context, the action's fresh authority, and the key and context storage limits.</summary>
    private async Task<BankAccountCommandResult?> StartAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, Guid? accountId, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, action, accountId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var key = metadata.IdempotencyKey;
        if (!IsCanonical(key) || key.Length > IdentityMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));
        }

        return context.ActorId.Length > IdentityMaximumLength || context.CorrelationId.Length > IdentityMaximumLength
            ? Refused(context, CommandOutcome.ValidationFailed, Issue("context", "finance_value_does_not_fit"))
            : null;
    }

    /// <summary>The same key again: its stored answer as a replay, or a conflict for another payload or command.</summary>
    private async Task<BankAccountCommandResult?> PriorAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, CancellationToken cancellationToken)
    {
        var key = metadata.IdempotencyKey;
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ActorId == context.ActorId && row.IdempotencyKey == key, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (prior.CommandName != command || prior.PayloadHash != payloadHash)
        {
            return Refused(context, CommandOutcome.IdempotencyConflict);
        }

        if (await FenceAsync(context, action, prior.BankAccountId, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        var stored = JsonSerializer.Deserialize<BankAccountCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı komut sonucu okunamadı.");
        return stored with { IsReplay = true };
    }

    /// <summary>The currency must be one of Settings' candidates now; an unavailable source refuses.</summary>
    private async Task<BankAccountCommandResult?> CurrencyRefusalAsync(FinanceContext context, FinanceAction action,
        Guid? accountId, Guid currencyId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(
                await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken));
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, action, accountId, cancellationToken) is { } fenced)
            {
                return fenced;
            }

            return exception.Code == "settings_access_denied"
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("currencyId", exception.Code));
        }

        if (await FenceAsync(context, action, accountId, cancellationToken) is { } afterSettings)
        {
            return afterSettings;
        }

        return candidates.Any(candidate => candidate.CurrencyId == currencyId)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("currencyId", "currency_not_a_candidate"));
    }

    /// <summary>Company/actor still the request's, then a fresh decision on the action.</summary>
    private async Task<BankAccountCommandResult?> FenceAsync(FinanceContext context, FinanceAction action, Guid? accountId,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        var decision = await permissions.CheckAsync(context, action, accountId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("permission", "finance_permission_mapping_unavailable"));
        }

        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    private async Task<ReadOutcome?> ReadFenceAsync(FinanceContext context, Guid? accountId, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return ReadOutcome.Denied;
        }

        var decision = await permissions.CheckAsync(context, FinanceAction.ReadBankAccounts, accountId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable;
        }

        return SameContext(context) ? null : ReadOutcome.Denied;
    }

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private void AddCommand(FinanceContext context, CommandMetadata metadata, string command, string payloadHash,
        Guid accountId, BankAccountCommandResult result, DateTimeOffset now) =>
        dbContext.Commands.Add(new FinanceCommandRow
        {
            CompanyId = context.CompanyId,
            ActorId = context.ActorId,
            IdempotencyKey = metadata.IdempotencyKey,
            CommandName = command,
            PayloadHash = payloadHash,
            BankAccountId = accountId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    private async Task<BankAccountCommandResult> SaveAsync(FinanceContext context, BankAccountCommandResult result,
        CancellationToken cancellationToken)
    {
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

        return result;
    }

    private void Audit(FinanceContext context, CommandMetadata metadata, Guid accountId, string eventType, string operation,
        long? versionBefore, long? versionAfter, string before, string after, string? approvalContextJson, string reason)
    {
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId,
            eventType,
            EntityType,
            accountId.ToString("D"),
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            context.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            before,
            after,
            reason,
            approvalContextJson,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            context.CorrelationId,
            context.CausationId,
            metadata.IdempotencyKey));
    }

    private static string AccountAudit(BankAccountRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        version = VersionText(row.Version),
        status = row.Status == Approved ? "Approved" : "Proposed",
        row.CurrencyId,
        row.DisplayName,
        row.ProposedBy,
        row.ApprovedBy,
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static BankAccountCommandResult Succeeded(FinanceContext context, BankAccountRow row) =>
        new BankAccountCommandResult(CommandOutcome.Succeeded, View(row), IsReplay: false, context.CorrelationId, []);

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static BankAccountCommandResult Refused(FinanceContext context, CommandOutcome outcome, params ValidationIssue[] issues) =>
        new BankAccountCommandResult(outcome, null, IsReplay: false, context.CorrelationId, issues);
}
