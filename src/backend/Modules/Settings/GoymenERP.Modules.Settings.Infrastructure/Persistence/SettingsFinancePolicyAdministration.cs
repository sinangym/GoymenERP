using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>Explicit company authoring. No seed, default threshold, role assignment or Finance activation.</summary>
public sealed partial class SettingsFinancePolicyAdministration(
    SettingsDbContext database, ICompanyContext company, ICurrentUser user,
    IErpAuthorizationService authorization, IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter,
    ISettingsFinancePolicyReferencesV1 references, ISettingsCollateralTypeCatalogV1 collateralTypes, TimeProvider clock,
    ILogger<SettingsFinancePolicyAdministration> logger) : ISettingsFinancePolicyAdministrationV1,
    ISettingsFinanceApprovalPolicyProviderV1, ISettingsCashCountDifferencePolicyProviderV1, ISettingsFinancePolicyPreviewV1,
    ISettingsBankInstitutionAdministrationV1, ISettingsBankInstitutionCatalogV1,
    ISettingsCollateralRateAdministrationV1, ISettingsCollateralRateProviderV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string ApprovalEntity = "Settings.FinanceApprovalPolicy";
    private const string CashEntity = "Settings.CashCountDifferencePolicy";

    private sealed record Scope(Guid CompanyId, string ActorId);
    private sealed record Change(SettingsFinancePolicyCommandAnswerV1 Answer, string? EntityType = null,
        Guid? Id = null, string? Event = null, long? BeforeVersion = null, long? AfterVersion = null,
        object? Before = null, object? After = null, string? Reason = null);

    public async Task<SettingsFinancePoliciesWorkspaceV1> GetAsync(CancellationToken cancellationToken = default)
    {
        var scope = Capture(cancellationToken);
        await RequireAsync(scope, [SettingsFinancePolicyPermissionsV1.Prepare], false, cancellationToken);
        var source = await ReferenceAsync(scope, cancellationToken);
        var approvals = await ReadAsync(() => database.FinanceApprovalPolicies.AsNoTracking()
            .OrderByDescending(x => x.Version).ToListAsync(cancellationToken));
        var rules = await ReadAsync(() => database.FinanceApprovalPolicyRules.AsNoTracking().ToListAsync(cancellationToken));
        var cash = await ReadAsync(() => database.CashCountDifferencePolicies.AsNoTracking()
            .OrderBy(x => x.CashAccountId).ThenByDescending(x => x.Version).ToListAsync(cancellationToken));
        await RequireAsync(scope, [SettingsFinancePolicyPermissionsV1.Prepare], true, cancellationToken);
        return new(scope.CompanyId, Today(), source,
            approvals.Select(x => ApprovalView(x, rules.Where(rule => rule.PolicyId == x.PolicyId).ToArray())).ToArray(),
            cash.Select(CashView).ToArray());
    }

    private async Task<SettingsFinancePolicyCommandResultV1> ExecuteAsync<T>(string command, Guid? targetId, T request,
        SettingsCommandMetadataV1 metadata, string[] permissions,
        Func<Scope, DateOnly, CancellationToken, Task<Change>> mutate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = Capture(cancellationToken);
        await RequireAsync(scope, permissions, false, cancellationToken);
        if (metadata.CompanyId != scope.CompanyId || metadata.ActorId != scope.ActorId
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 128
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || metadata.CorrelationId.Length > 128
            || string.IsNullOrWhiteSpace(metadata.SourceChannel)) return Result(Invalid(), metadata);
        // Refuse before touching caller-owned tracking, Audit or an ambient/borrowed transaction.
        if (database.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || database.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts) throw Unavailable();
        var key = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var hash = IdempotencyHash.ComputePayload(new { TargetId = targetId, Request = request });
        var operationStarted = false;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            operationStarted = true;
            if (await FindAsync(command, key, cancellationToken) is { } existing)
            {
                await RequireAsync(scope, permissions, true, cancellationToken);
                var replay = Replay(existing, hash, metadata);
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            var change = await mutate(scope, Today(), cancellationToken);
            if (change.Answer.StatusCode is not (204 or 409))
            {
                await RequireAsync(scope, permissions, true, cancellationToken);
                return Result(change.Answer, metadata); // No attempt; owned transaction rolls back.
            }
            var authority = await RequireAsync(scope, permissions, true, cancellationToken);
            await VerifyReferencesAsync(scope, change, cancellationToken);
            if (change.Event is not null)
            {
                audit.Enqueue(new AuditEventDraft(scope.CompanyId, change.Event, change.EntityType!, change.Id!.Value.ToString("D"),
                    command, change.BeforeVersion, change.AfterVersion, AuditActorTypes.User, scope.ActorId,
                    permissions[^1], authority.RoleCodes, JsonSerializer.Serialize(change.Before ?? new { }, Json),
                    JsonSerializer.Serialize(change.After, Json), change.Reason!, null, AuditAiInvolvement.None, null,
                    metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId,
                    metadata.CausationId, metadata.IdempotencyKey));
                await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
            }
            database.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(Guid.NewGuid(), scope.CompanyId,
                command, key, hash, scope.ActorId, change.Answer.Code ?? "Succeeded", change.Answer.StatusCode,
                JsonSerializer.Serialize(change.Answer, Json), metadata.CorrelationId, clock.GetUtcNow(), 1)));
            await RequireAsync(scope, permissions, true, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await VerifyReferencesAsync(scope, change, cancellationToken);
            await RequireAsync(scope, permissions, true, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Finance policy command completed. Command={Command} Status={Status}", command, change.Answer.StatusCode);
            return Result(change.Answer, metadata);
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear(); auditWriter.DiscardPending();
            await RequireAsync(scope, permissions, true, cancellationToken);
            var winner = await ReadAsync(() => FindAsync(command, key, cancellationToken));
            await RequireAsync(scope, permissions, true, cancellationToken);
            if (winner is not null) return Replay(winner, hash, metadata);
            throw Unavailable();
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            // The using transaction has rolled back, including Audit. A caught Save failure is never committed.
            database.ChangeTracker.Clear(); auditWriter.DiscardPending();
            await RequireAsync(scope, permissions, true, cancellationToken);
            var winner = await ReadAsync(() => FindAsync(command, key, cancellationToken));
            await RequireAsync(scope, permissions, true, cancellationToken);
            if (winner is not null) return Replay(winner, hash, metadata);
            throw Unavailable();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException
                                         and not SettingsPrerequisiteException)
        { throw Unavailable(); }
        finally
        {
            if (operationStarted) { database.ChangeTracker.Clear(); auditWriter.DiscardPending(); }
        }
    }

    private Scope Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = user.UserId;
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(actor) || !user.CanAccessCompany(company.CompanyId))
            throw new SettingsPrerequisiteException(SettingsFinancePolicyCodesV1.AccessDenied);
        return new(company.CompanyId, actor);
    }

    private void Same(Scope scope, CancellationToken cancellationToken)
    {
        if (Capture(cancellationToken) != scope) throw new SettingsPrerequisiteException(SettingsFinancePolicyCodesV1.AccessDenied);
    }

    private async Task<PermissionScope> RequireAsync(Scope scope, string[] permissions, bool fresh, CancellationToken cancellationToken)
    {
        Same(scope, cancellationToken);
        PermissionScope? answer = null;
        foreach (var permission in permissions)
        {
            if (!ErpPermissions.IsKnown(permission)) throw Unavailable();
            answer = fresh
                ? await revalidation.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken)
                : await authorization.GetPermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
            Same(scope, cancellationToken);
            if (!answer.IsGranted || !answer.IsCompanyWide) throw new UnauthorizedAccessException();
        }
        return answer ?? throw Unavailable();
    }

    private async Task<SettingsFinancePolicyReferencesV1> ReferenceAsync(Scope scope, CancellationToken cancellationToken)
    {
        var answer = await ReadAsync(() => references.GetAsync(scope.CompanyId, cancellationToken));
        Same(scope, cancellationToken);
        if (answer is null || answer.CompanyId != scope.CompanyId || answer.Operations is null || answer.Roles is null
            || answer.CashAccounts is null || answer.Currencies is null
            || answer.Operations.Any(x => x is null || !ErpPermissions.IsKnown(x.Permission) || !x.Permission.StartsWith("finance.", StringComparison.Ordinal))
            || answer.Roles.Any(x => x is null || x.RoleId == Guid.Empty || string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.SourceVersion))
            || answer.CashAccounts.Any(x => x is null || x.CashAccountId == Guid.Empty || x.CurrencyId == Guid.Empty || string.IsNullOrWhiteSpace(x.Version))
            || answer.Currencies.Any(x => x is null || x.CurrencyId == Guid.Empty || string.IsNullOrWhiteSpace(x.Code))
            || answer.Operations.Select(x => x.Permission).Distinct(StringComparer.Ordinal).Count() != answer.Operations.Count
            || answer.Roles.Select(x => x.RoleId).Distinct().Count() != answer.Roles.Count
            || answer.CashAccounts.Select(x => x.CashAccountId).Distinct().Count() != answer.CashAccounts.Count
            || answer.Currencies.Select(x => x.CurrencyId).Distinct().Count() != answer.Currencies.Count) throw Unavailable();
        return answer;
    }

    private async Task VerifyReferencesAsync(Scope scope, Change change, CancellationToken token)
    {
        if (change.Event is null || change.Event.EndsWith(".retired.v1", StringComparison.Ordinal)) return;
        if (change.After is SettingsBankInstitutionPolicyV1) return;
        if (change.After is SettingsCollateralRatePolicyV1 collateralRate)
        {
            if (!await CollateralReferencesMatchAsync(scope, collateralRate, token)) throw Unavailable();
            return;
        }
        var source = await ReferenceAsync(scope, token);
        var matches = change.After switch
        {
            SettingsFinanceApprovalPolicyV1 policy => policy.Rules.All(rule =>
                source.Operations.Any(x => x.Permission == rule.OperationPermission)
                && source.Currencies.Any(x => x.CurrencyId == rule.CurrencyId)
                && source.Roles.Any(x => x.RoleId == rule.ApproverRoleId && x.Code == rule.ApproverRoleCode && x.SourceVersion == rule.RoleSourceVersion)),
            SettingsCashCountDifferencePolicyV1 policy => source.CashAccounts.Any(x => x.CashAccountId == policy.CashAccountId
                && x.CurrencyId == policy.CurrencyId && x.Version == policy.CashAccountVersion),
            _ => false
        };
        // A late source change is a rolled-back attempt, never a partially saved policy or a fabricated available source.
        if (!matches) throw Unavailable();
    }

    private DateOnly Today()
    {
        try { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).Date); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException) { throw Unavailable(); }
    }

    private Task<CommandExecution?> FindAsync(string command, string key, CancellationToken cancellationToken) =>
        database.CommandExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.CommandName == command && x.IdempotencyKeyHash == key, cancellationToken);

    private static SettingsFinancePolicyCommandResultV1 Replay(CommandExecution stored, string hash, SettingsCommandMetadataV1 metadata)
    {
        if (stored.ActorId != metadata.ActorId || stored.RequestHash != hash)
            return Result(Problem(409, SettingsFinancePolicyCodesV1.IdempotencyConflict, "İşlem anahtarı farklı bir istek için kullanılmış."), metadata);
        var answer = JsonSerializer.Deserialize<SettingsFinancePolicyCommandAnswerV1>(stored.ResponseJson, Json);
        if (answer is null || answer.StatusCode != stored.HttpStatusCode || (answer.Code ?? "Succeeded") != stored.Outcome
            || answer.StatusCode is not (204 or 409)) throw Unavailable();
        return new(answer, true, stored.OriginalCorrelationId);
    }

    private static async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        try { return await read(); }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException
                                         and not UnauthorizedAccessException) { throw Unavailable(); }
    }

    private static bool Reason(string? input, out string value)
    { value = input?.Trim() ?? string.Empty; return value.Length is > 0 and <= FinancePolicyRulesV1.ReasonMaximumLength; }
    private static SettingsPrerequisiteException Unavailable() => new(SettingsFinancePolicyCodesV1.Unavailable);
    private static SettingsFinancePolicyCommandResultV1 Result(SettingsFinancePolicyCommandAnswerV1 answer, SettingsCommandMetadataV1 metadata) => new(answer, false, metadata.CorrelationId);
    private static SettingsFinancePolicyCommandAnswerV1 Invalid() => Problem(400, SettingsFinancePolicyCodesV1.Invalid, "Alanları ve kaynak sürümlerini kontrol edin.");
    private static SettingsFinancePolicyCommandAnswerV1 Conflict() => Problem(409, SettingsFinancePolicyCodesV1.VersionConflict, "Kayıt değişmiş; güncel sürümü okuyun.");
    private static SettingsFinancePolicyCommandAnswerV1 Problem(int status, string code, string detail) => new(status, code, "Finans politikası kaydedilemedi.", detail);
    private static SettingsFinancePolicyCommandAnswerV1 Saved(IFinancePolicyVersionRow row) => new(204, null, "Kaydedildi.", "Politika işlemi kaydedildi.", row.PolicyId, row.Version, row.StateVersion);
    private static SettingsFinancePolicyStateV1 State(IFinancePolicyVersionRow row) => Enum.TryParse<SettingsFinancePolicyStateV1>(row.State, out var state)
        && Enum.IsDefined(state) ? state : throw Unavailable();

    private static SettingsFinanceApprovalPolicyV1 ApprovalView(FinanceApprovalPolicyRow row, IReadOnlyList<FinanceApprovalPolicyRuleRow> rules) =>
        new(row.CompanyId, row.PolicyId, row.Version, row.StateVersion, State(row), row.ValidFrom, row.ValidThrough,
            row.RetiredFrom, row.PreparedBy, row.PreparedAtUtc, row.PublishedBy, row.PublishedAtUtc, row.Reason,
            rules.OrderBy(x => x.OperationPermission, StringComparer.Ordinal).ThenBy(x => x.CurrencyId).ThenBy(x => x.ApprovalStep)
                .Select(x => new SettingsFinanceApprovalRuleV1(x.RuleId, x.OperationPermission, x.CurrencyId,
                    x.ApproverRoleId, x.ApproverRoleCode, x.RoleSourceVersion, x.ApprovalStep, x.AmountThreshold)).ToArray());
    private static SettingsCashCountDifferencePolicyV1 CashView(CashCountDifferencePolicyRow row) =>
        new(row.CompanyId, row.PolicyId, row.Version, row.StateVersion, State(row), row.ValidFrom, row.ValidThrough,
            row.RetiredFrom, row.PreparedBy, row.PreparedAtUtc, row.PublishedBy, row.PublishedAtUtc, row.Reason,
            row.CashAccountId, row.CashAccountVersion, row.CurrencyId, row.AmountThreshold);
}
