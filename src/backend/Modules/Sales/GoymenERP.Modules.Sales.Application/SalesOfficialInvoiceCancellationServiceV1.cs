using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>SAT-D96 / #1189 6957924089: records a proved existing cancellation; never cancels an external invoice.
/// Existing finalize authority only. Price-free receipts avoid silently requiring a new price/read grant.</summary>
public sealed class SalesOfficialInvoiceCancellationServiceV1(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, ICorrelationContext correlation,
    ISalesOfficialInvoiceCancellationStoreV1 store, ISalesOfficialCancellationEvidenceReadPortV1 evidence,
    ISalesFinanceCancellationPortV1? finance = null) : ISalesOfficialInvoiceCancellationServiceV1
{
    private const string Permission = SalesPermissionManifestV1.InvoicesFinalize;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SalesOfficialCancellationCandidatesV1> ListCandidatesAsync(SalesOfficialInvoiceKindV1 kind, string? cursor,
        CancellationToken cancellationToken = default)
    {
        var scope = await Require(cancellationToken); Guid? after = null;
        if (!Enum.IsDefined(kind)) throw Error(400, "validation_failed");
        if (cursor is not null)
        {
            if (!Guid.TryParseExact(cursor, "D", out var parsed) || parsed == Guid.Empty || parsed.ToString("D") != cursor)
                throw Error(400, "invalid_cursor");
            after = parsed;
        }
        return await Read(() => store.ListCandidatesAsync(kind, after, t => Still(scope, t), cancellationToken), scope, cancellationToken);
    }

    public async Task<SalesOfficialInvoiceCancellationResultV1> RecordAsync(SalesOfficialInvoiceKindV1 kind, Guid invoiceId,
        RecordSalesOfficialInvoiceCancellationV1 request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        var scope = await Require(cancellationToken);
        ArgumentNullException.ThrowIfNull(request);
        Validate(metadata);
        if (!Enum.IsDefined(kind) || invoiceId == Guid.Empty || !Version(request.ExpectedVersion, out var version)
            || !Text(request.Reason, 1000)) throw Error(400, "validation_failed");
        var command = SalesOfficialInvoiceCancellationCommandsV1.Record;
        var hash = Hash(new { kind, invoiceId, request });
        var envelope = Envelope(scope, metadata);
        Task Fence(CancellationToken token) => Still(scope, token);
        var replay = await Read(() => store.FindReplayAsync(envelope, hash, command, Fence, cancellationToken), scope, cancellationToken);
        if (replay is not null) return replay;
        SalesOfficialCancellationTargetV1 target;
        try { target = await Read(() => store.ReadTargetAsync(kind, invoiceId, Fence, cancellationToken), scope, cancellationToken)
            ?? throw Error(404, "not_found"); }
        catch (SalesDraftServiceException ex) when (ex.Status == 409 && ex.Code == SalesDraftProblemCodesV1.StateConflict)
        { return await Terminal(scope, envelope, hash, command, new(409, ex.Code, [new("state", ex.Code)]), cancellationToken); }
        if (target.CompanyId != scope.CompanyId || target.InvoiceId != invoiceId || target.Kind != kind)
            throw Error(503, "official_cancellation_source_unavailable");
        if (target.Version != version)
            return await Terminal(scope, envelope, hash, command, new(409, SalesDraftProblemCodesV1.VersionConflict,
                [new("expectedVersion", "version_conflict")]), cancellationToken);
        SalesOfficialCancellationEvidenceAnswerV1? proof;
        try { proof = await evidence.ReadAsync(scope.CompanyId, kind, invoiceId, target.OfficialInvoiceNo, target.Ettn, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { await Fence(cancellationToken); throw Error(503, "official_cancellation_evidence_unavailable"); }
        await Fence(cancellationToken);
        if (proof?.Outcome == SalesOfficialCancellationEvidenceOutcomeV1.NotFound && proof.Evidence is null)
            throw Error(409, "official_cancellation_not_confirmed"); // Data may arrive: the key remains unused.
        if (proof?.Outcome != SalesOfficialCancellationEvidenceOutcomeV1.Available || proof.Evidence is null
            || !proof.Evidence.IsValidFor(scope.CompanyId, invoiceId, target.OfficialInvoiceNo, target.Ettn)
            || proof.Evidence.OfficiallyCancelledAtUtc > DateTimeOffset.UtcNow)
            throw Error(503, "official_cancellation_evidence_unavailable");
        try
        {
            return await store.RecordAsync(envelope, hash, kind, invoiceId, version, request.Reason, proof.Evidence,
                Fence, cancellationToken);
        }
        catch (SalesVersionConflictException)
        {
            await Fence(cancellationToken);
            return await Terminal(scope, envelope, hash, command, new(409, SalesDraftProblemCodesV1.VersionConflict,
                [new("expectedVersion", "version_conflict")]), cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            await Fence(cancellationToken);
            return await Terminal(scope, envelope, hash, command, new(409, SalesDraftProblemCodesV1.StateConflict,
                [new("state", SalesRuleCodes.StateConflict)]), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            await Fence(cancellationToken);
            if (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
            {
                var winner = await Read(() => store.FindReplayAsync(envelope, hash, command, Fence, cancellationToken), scope, cancellationToken);
                if (winner is not null) return winner;
            }
            throw Map(exception);
        }
        catch (SalesDraftServiceException) { await Fence(cancellationToken); throw; }
        catch (OperationCanceledException) { throw; }
        catch { await Fence(cancellationToken); throw Error(503, "official_cancellation_source_unavailable"); }
    }

    public async Task<SalesOfficialInvoiceCancellationViewV1> GetAsync(Guid cancellationId, CancellationToken cancellationToken = default)
    {
        var scope = await Require(cancellationToken);
        var value = await Read(() => store.ReadDeliveryAsync(cancellationId, token => Still(scope, token), cancellationToken), scope, cancellationToken)
            ?? throw Error(404, "not_found");
        await Still(scope, cancellationToken);
        return value.Cancellation;
    }

    public async Task<SalesOfficialInvoiceCancellationViewV1> GetForInvoiceAsync(SalesOfficialInvoiceKindV1 kind, Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var scope = await Require(cancellationToken);
        var value = await Read(() => store.ReadForInvoiceAsync(kind, invoiceId, t => Still(scope, t), cancellationToken), scope, cancellationToken)
            ?? throw Error(404, "not_found");
        return value.Cancellation;
    }

    public async Task<SalesOfficialInvoiceCancellationResultV1> RetryFinanceAsync(Guid cancellationId,
        RetrySalesOfficialInvoiceCancellationFinanceV1 request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        var scope = await Require(cancellationToken);
        ArgumentNullException.ThrowIfNull(request);
        Validate(metadata);
        if (cancellationId == Guid.Empty || request.ExpectedDeliveryVersion < 0) throw Error(400, "validation_failed");
        var command = SalesOfficialInvoiceCancellationCommandsV1.RetryFinance;
        var hash = Hash(new { cancellationId, request });
        var envelope = Envelope(scope, metadata);
        Task Fence(CancellationToken token) => Still(scope, token);
        var replay = await Read(() => store.FindReplayAsync(envelope, hash, command, Fence, cancellationToken), scope, cancellationToken);
        if (replay is not null) return replay;
        var source = await Read(() => store.ReadDeliveryAsync(cancellationId, Fence, cancellationToken), scope, cancellationToken)
            ?? throw Error(404, "not_found");
        if (source.Cancellation.CompanyId != scope.CompanyId || source.Cancellation.CancellationId != cancellationId)
            throw Error(503, "official_cancellation_source_unavailable");
        if (source.Cancellation.DeliveryVersion != request.ExpectedDeliveryVersion)
            return await Terminal(scope, envelope, hash, command, new(409, SalesDraftProblemCodesV1.VersionConflict,
                [new("expectedDeliveryVersion", "version_conflict")]), cancellationToken);
        SalesFinanceCancellationAnswerV1 answer;
        if (source.Cancellation.FinanceStatus == "Cancelled") answer = new("Cancelled");
        else
        {
            if (finance is null) throw Error(503, "official_cancellation_finance_unavailable");
            try
            {
                answer = await finance.CancelAsync(new(scope.CompanyId, scope.ActorId, metadata.CorrelationId,
                    metadata.CausationId, metadata.IdempotencyKey, cancellationId, source.Cancellation.Reason,
                    source.Admission, request.ReleaseActiveAllocations), cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch { await Fence(cancellationToken); throw Error(503, "official_cancellation_finance_unavailable"); }
            await Fence(cancellationToken);
            if (answer is null || answer.Status is not ("Cancelled" or "Pending" or "AllocationConfirmationRequired")
                || answer.Reason is not null && !Text(answer.Reason, 128))
                throw Error(503, "official_cancellation_finance_unavailable");
        }
        try { return await store.RecordDeliveryAsync(envelope, hash, cancellationId, request.ExpectedDeliveryVersion,
            request.ReleaseActiveAllocations, answer, Fence, cancellationToken); }
        catch (SalesDraftStoreException exception)
        {
            await Fence(cancellationToken);
            if (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
            {
                var winner = await Read(() => store.FindReplayAsync(envelope, hash, command, Fence, cancellationToken), scope, cancellationToken);
                if (winner is not null) return winner;
            }
            throw Map(exception);
        }
        catch (SalesDraftServiceException) { await Fence(cancellationToken); throw; }
        catch (OperationCanceledException) { throw; }
        catch { await Fence(cancellationToken); throw Error(503, "official_cancellation_source_unavailable"); }
    }

    private async Task<SalesOfficialInvoiceCancellationResultV1> Terminal(Scope scope, SalesDraftWriteEnvelope envelope,
        string hash, string command, SalesDraftTerminalRefusal refusal, CancellationToken ct)
    {
        await Still(scope, ct);
        try { return await store.RecordRefusalAsync(envelope, hash, command, refusal, t => Still(scope, t), ct); }
        catch (SalesDraftServiceException) { await Still(scope, ct); throw; }
        catch (SalesDraftStoreException ex)
        {
            await Still(scope, ct);
            if (ex.Code == SalesDraftStoreCodes.ConcurrentChange)
            {
                var winner = await Read(() => store.FindReplayAsync(envelope, hash, command, t => Still(scope, t), ct), scope, ct);
                if (winner is not null) return winner;
            }
            throw Map(ex);
        }
        catch (OperationCanceledException) { throw; }
        catch { await Still(scope, ct); throw Error(503, "official_cancellation_source_unavailable"); }
    }

    private async Task<T> Read<T>(Func<Task<T>> operation, Scope scope, CancellationToken token)
    {
        try { var value = await operation(); await Still(scope, token); return value; }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException) { await Still(scope, token); throw; }
        catch (SalesRuleException ex) when (ex.Code == SalesRuleCodes.StateConflict) { await Still(scope, token); throw Error(409, SalesDraftProblemCodesV1.StateConflict); }
        catch (SalesDraftStoreException exception) { await Still(scope, token); throw Map(exception); }
        catch { await Still(scope, token); throw Error(503, "official_cancellation_source_unavailable"); }
    }
    private async Task<Scope> Require(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw Error(401, "unauthenticated");
        var companyId = company.CompanyId; var actor = user.UserId;
        if (!Same(companyId, actor)) throw Error(403, "forbidden");
        if (!ErpPermissions.IsKnown(Permission)) throw Error(503, SalesDraftProblemCodesV1.PermissionUnregistered);
        var measured = await authority.RevalidatePermissionScopeAsync(Permission, companyId, token);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Same(companyId, actor)) throw Error(403, "forbidden");
        return new(companyId, actor, [.. measured.RoleCodes.Order(StringComparer.Ordinal)]);
    }
    private async Task Still(Scope scope, CancellationToken token)
    { var actual = await Require(token); if (actual.CompanyId != scope.CompanyId || actual.ActorId != scope.ActorId) throw Error(403, "forbidden"); }
    private bool Same(Guid id, string actor) => id != Guid.Empty && company.IsResolved && company.CompanyId == id
        && user.IsAuthenticated && user.UserId == actor && user.CanAccessCompany(id);
    private SalesDraftWriteEnvelope Envelope(Scope scope, SalesCommandMetadata metadata) => new(scope.ActorId, Permission,
        scope.Roles, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel,
        correlation.ClientIpAddress, correlation.UserAgent);
    private static bool Version(string value, out long version) => long.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out version) && version > 0 && value == version.ToString(CultureInfo.InvariantCulture);
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim()
        && value.Length <= maximum && !value.Any(char.IsControl);
    private static void Validate(SalesCommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!Text(metadata.IdempotencyKey, 200) || !Text(metadata.CorrelationId, 128)
            || metadata.CausationId is not null && !Text(metadata.CausationId, 128)) throw Error(400, SalesDraftProblemCodesV1.CommandMetadataInvalid);
    }
    // The store accepts only a 64-character lowercase sha256 (SalesStore.RequireHash); K3-B P1.
    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json)))).ToLowerInvariant();
    private static SalesDraftServiceException Map(SalesDraftStoreException exception) => exception.Code switch
    {
        SalesDraftStoreCodes.SameKeyPayloadMismatch => Error(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch),
        SalesDraftStoreCodes.ConcurrentChange => Error(409, SalesDraftProblemCodesV1.ConcurrentChange),
        SalesDraftStoreCodes.CompanyContextMismatch => Error(403, "forbidden"),
        _ => Error(503, "official_cancellation_source_unavailable"),
    };
    private static SalesDraftServiceException Error(int status, string code) => new(status, code);
    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> Roles);
}
