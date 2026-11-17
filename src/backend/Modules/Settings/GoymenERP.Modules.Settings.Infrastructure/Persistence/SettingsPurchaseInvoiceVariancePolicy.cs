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

/// <summary>
/// ALS-D8/D15/D63, DEC-SET-001: actual company registry, no synthetic zero or compiled active policy. Publication owns a
/// Serializable transaction containing the immutable version, Audit-owner append and terminal idempotency answer.
/// Borrowed/ambient transactions and another operation's pending state are refused without clearing the caller's state.
/// </summary>
public sealed class SettingsPurchaseInvoiceVariancePolicy(
    SettingsDbContext database, ICompanyContext companyContext, ICurrentUser currentUser,
    IErpAuthorizationService authorization, IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, TimeProvider clock,
    ILogger<SettingsPurchaseInvoiceVariancePolicy> logger)
    : ISettingsPurchaseInvoiceVariancePolicyProviderV1, ISettingsPurchaseInvoiceVariancePolicyAdministrationV1
{
    private const string Permission = SettingsPurchaseInvoiceVariancePolicyPermissionsV1.Manage;
    private const string Command = "settings.purchase-invoice-variance-policy.publish.v1";
    private const string EntityType = "Settings.PurchaseInvoiceVariancePolicy";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SettingsPurchaseInvoiceVariancePolicyV1> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var actor = RequireMember(companyId, cancellationToken);
        var row = await ReadStoreAsync(() => database.PurchaseInvoiceVariancePolicies.AsNoTracking()
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken));
        RequireSameMember(companyId, actor, cancellationToken);
        if (row is null) throw new SettingsPrerequisiteException(PurchaseInvoiceVariancePolicyCodesV1.NotPublished);
        Validate(row, companyId);
        return new(companyId, row.PolicyId, row.Version, row.AmountTolerance, row.RateTolerance);
    }

    public async Task<PurchaseInvoiceVariancePolicyViewV1> ReadAsync(CancellationToken cancellationToken = default)
    {
        var companyId = companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
        var actor = RequireMember(companyId, cancellationToken);
        await RequirePermissionAsync(companyId, actor, fresh: false, cancellationToken);
        var rows = await ReadStoreAsync(() => database.PurchaseInvoiceVariancePolicies.AsNoTracking()
            .OrderByDescending(x => x.Version).ToArrayAsync(cancellationToken));
        await RequirePermissionAsync(companyId, actor, fresh: true, cancellationToken);
        foreach (var row in rows) Validate(row, companyId);
        var versions = rows.Select(View).ToArray();
        return new(companyId, versions.FirstOrDefault()?.Version ?? "0", versions.FirstOrDefault(), versions);
    }

    public async Task<CompanyPolicyCommandResultV1> PublishAsync(PublishPurchaseInvoiceVariancePolicyRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var companyId = companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
        var actor = RequireMember(companyId, cancellationToken);
        await RequirePermissionAsync(companyId, actor, fresh: false, cancellationToken);
        if (metadata.CompanyId != companyId || !string.Equals(actor, metadata.ActorId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 128
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.SourceChannel))
            return Result(Invalid("Komut şirketi, aktörü ve işlem bilgileri geçerli olmalıdır."), metadata);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (!PurchaseInvoiceVariancePolicyRulesV1.TryVersion(request.ExpectedVersion, out var expected)
            || !PurchaseInvoiceVariancePolicyRulesV1.TryValue(request.AmountTolerance, out var amount)
            || !PurchaseInvoiceVariancePolicyRulesV1.TryValue(request.RateTolerance, out var rate)
            || reason.Length is 0 or > PurchaseInvoiceVariancePolicyRulesV1.MaximumReasonLength)
            return Result(Invalid("Beklenen sürüm, gerekçe ve iki negatif olmayan tolerans (en çok 18 tam ve 6 ondalık basamak) zorunludur."), metadata);

        // Refuse BEFORE acquiring/clearing anything owned by the caller, including a pending Audit batch.
        if (database.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || database.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts)
            throw new SettingsPrerequisiteException(PurchaseInvoiceVariancePolicyCodesV1.ProviderUnavailable);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { ExpectedVersion = expected,
            AmountTolerance = PurchaseInvoiceVariancePolicyRulesV1.Text(amount),
            RateTolerance = PurchaseInvoiceVariancePolicyRulesV1.Text(rate), Reason = reason });

        var operationStarted = false;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            operationStarted = true;
            if (await FindExecutionAsync(keyHash, cancellationToken) is { } existing)
            {
                await RequirePermissionAsync(companyId, actor, fresh: true, cancellationToken);
                var replay = Replay(existing, requestHash, metadata);
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            var latest = await database.PurchaseInvoiceVariancePolicies.AsNoTracking()
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
            if (latest is not null) Validate(latest, companyId);
            CompanyPolicyCommandAnswerV1 answer;
            if (expected != (latest?.Version ?? 0)) answer = Conflict();
            else
            {
                answer = new(204, null, "Yayımlandı.", "Fatura fark toleransı yayımlandı.");
                if (latest is null || latest.AmountTolerance != amount || latest.RateTolerance != rate)
                {
                    var row = new PurchaseInvoiceVariancePolicyRow { CompanyId = companyId,
                        PolicyId = Guid.NewGuid(), Version = checked(expected + 1), AmountTolerance = amount,
                        RateTolerance = rate, PublishedBy = actor, PublishedAtUtc = clock.GetUtcNow(), Reason = reason };
                    Validate(row, companyId);
                    database.PurchaseInvoiceVariancePolicies.Add(row);
                    audit.Enqueue(new AuditEventDraft(companyId, "settings.purchase-invoice-variance-policy.published.v1",
                        EntityType, row.PolicyId.ToString("D"), "PublishVersion", latest?.Version, row.Version,
                        AuditActorTypes.User, actor, Permission,
                        (await RequirePermissionAsync(companyId, actor, fresh: true, cancellationToken)).RoleCodes,
                        latest is null ? "{}" : JsonSerializer.Serialize(View(latest), Json),
                        JsonSerializer.Serialize(View(row), Json), "Alış fatura fark toleransı sürümü yayımlandı; gerekçe kayıt içeriğindedir",
                        null, AuditAiInvolvement.None, null, metadata.SourceChannel, metadata.ClientIpAddress,
                        metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
                    // The Audit owner drains the collector before Settings' legacy interceptor can see these drafts.
                    await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
                }
            }
            AddExecution(answer, keyHash, requestHash, metadata);
            await RequirePermissionAsync(companyId, actor, fresh: true, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await RequirePermissionAsync(companyId, actor, fresh: true, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Purchase invoice variance policy publication completed. Status={Status} CorrelationId={CorrelationId}",
                answer.StatusCode, metadata.CorrelationId);
            return Result(answer, metadata);
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            // The owned transaction has already been disposed/rolled back, including any Audit write. Never commit a
            // swallowed SaveChanges failure. A concurrently committed SAME key may replay; other races remain retryable.
            database.ChangeTracker.Clear();
            auditWriter.DiscardPending();
            var winner = await ReadStoreAsync(() => FindExecutionAsync(keyHash, cancellationToken));
            await RequirePermissionAsync(companyId, actor, fresh: true, cancellationToken);
            if (winner is not null) return Replay(winner, requestHash, metadata);
            throw new SettingsPrerequisiteException(PurchaseInvoiceVariancePolicyCodesV1.ProviderUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException
                                         and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(PurchaseInvoiceVariancePolicyCodesV1.ProviderUnavailable);
        }
        finally
        {
            if (operationStarted)
            {
                database.ChangeTracker.Clear();
                auditWriter.DiscardPending();
            }
        }
    }

    private string RequireMember(Guid companyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || !companyContext.IsResolved || companyContext.CompanyId != companyId
            || !currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !currentUser.CanAccessCompany(companyId))
            throw new SettingsPrerequisiteException(PurchaseInvoiceVariancePolicyCodesV1.AccessDenied);
        return currentUser.UserId;
    }

    private void RequireSameMember(Guid companyId, string actor, CancellationToken cancellationToken)
    {
        if (!string.Equals(RequireMember(companyId, cancellationToken), actor, StringComparison.Ordinal))
            throw new SettingsPrerequisiteException(PurchaseInvoiceVariancePolicyCodesV1.AccessDenied);
    }

    private async Task<PermissionScope> RequirePermissionAsync(Guid companyId, string actor, bool fresh, CancellationToken cancellationToken)
    {
        RequireSameMember(companyId, actor, cancellationToken);
        var scope = fresh
            ? await revalidation.RevalidatePermissionScopeAsync(Permission, companyId, cancellationToken)
            : await authorization.GetPermissionScopeAsync(Permission, companyId, cancellationToken);
        RequireSameMember(companyId, actor, cancellationToken);
        return scope is { IsGranted: true, IsCompanyWide: true } ? scope : throw new UnauthorizedAccessException();
    }

    private Task<CommandExecution?> FindExecutionAsync(string keyHash, CancellationToken cancellationToken) =>
        database.CommandExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.CommandName == Command
            && x.IdempotencyKeyHash == keyHash, cancellationToken);

    private void AddExecution(CompanyPolicyCommandAnswerV1 answer, string keyHash, string requestHash, SettingsCommandMetadataV1 metadata) =>
        database.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(Guid.NewGuid(), metadata.CompanyId,
            Command, keyHash, requestHash, metadata.ActorId, answer.Code ?? "Succeeded", answer.StatusCode,
            JsonSerializer.Serialize(answer, Json), metadata.CorrelationId, clock.GetUtcNow(), 1)));

    private static CompanyPolicyCommandResultV1 Replay(CommandExecution existing, string requestHash, SettingsCommandMetadataV1 metadata)
    {
        if (existing.ActorId != metadata.ActorId || existing.RequestHash != requestHash)
            return Result(new(409, PurchaseInvoiceVariancePolicyCodesV1.IdempotencyConflict, "İşlem anahtarı kullanılmış.",
                "Bu Idempotency-Key başka bir istekle kullanıldı."), metadata);
        var answer = JsonSerializer.Deserialize<CompanyPolicyCommandAnswerV1>(existing.ResponseJson, Json);
        if (answer is null || answer.StatusCode != existing.HttpStatusCode
            || (answer.Code ?? "Succeeded") != existing.Outcome
            || !(answer.StatusCode == 204 && answer.Code is null
                 || answer.StatusCode == 409 && answer.Code == PurchaseInvoiceVariancePolicyCodesV1.VersionConflict))
            throw new SettingsPrerequisiteException(PurchaseInvoiceVariancePolicyCodesV1.ProfileInvalid);
        return new(answer, true, existing.OriginalCorrelationId);
    }

    private static void Validate(PurchaseInvoiceVariancePolicyRow row, Guid companyId)
    {
        if (row.CompanyId != companyId || row.PolicyId == Guid.Empty || row.Version < 1
            || !PurchaseInvoiceVariancePolicyRulesV1.IsValue(row.AmountTolerance)
            || !PurchaseInvoiceVariancePolicyRulesV1.IsValue(row.RateTolerance)
            || string.IsNullOrWhiteSpace(row.PublishedBy) || row.PublishedBy.Length > 128
            || row.PublishedAtUtc.Offset != TimeSpan.Zero || row.PublishedAtUtc == default
            || string.IsNullOrWhiteSpace(row.Reason) || row.Reason.Length > PurchaseInvoiceVariancePolicyRulesV1.MaximumReasonLength)
            throw new SettingsPrerequisiteException(PurchaseInvoiceVariancePolicyCodesV1.ProfileInvalid);
    }

    private static PurchaseInvoiceVariancePolicyVersionViewV1 View(PurchaseInvoiceVariancePolicyRow row) =>
        new(row.PolicyId, PurchaseInvoiceVariancePolicyRulesV1.Text(row.Version),
            PurchaseInvoiceVariancePolicyRulesV1.Text(row.AmountTolerance), PurchaseInvoiceVariancePolicyRulesV1.Text(row.RateTolerance),
            row.PublishedBy, row.PublishedAtUtc, row.Reason);

    private static async Task<T> ReadStoreAsync<T>(Func<Task<T>> read)
    {
        try { return await read(); }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        { throw new SettingsPrerequisiteException(PurchaseInvoiceVariancePolicyCodesV1.ProviderUnavailable); }
    }

    private static CompanyPolicyCommandResultV1 Result(CompanyPolicyCommandAnswerV1 answer, SettingsCommandMetadataV1 metadata) =>
        new(answer, false, metadata.CorrelationId);
    private static CompanyPolicyCommandAnswerV1 Invalid(string detail) =>
        new(400, PurchaseInvoiceVariancePolicyCodesV1.RequestInvalid, "İstek geçersiz.", detail);
    private static CompanyPolicyCommandAnswerV1 Conflict() => new(409, PurchaseInvoiceVariancePolicyCodesV1.VersionConflict,
        "Kayıt değişmiş.", "Güncel tolerans sürümünü okuyup yeniden deneyin.");
}
