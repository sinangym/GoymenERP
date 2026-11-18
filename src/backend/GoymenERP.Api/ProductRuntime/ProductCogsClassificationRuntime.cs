using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.ProductRuntime;

/// <summary>SAT-D422 global Product card extension. Only actual op7 assignments authorize writes.
/// Product, canonical global Audit and the existing global Idempotency terminal share one transaction.</summary>
public sealed class ProductCogsClassificationRuntime(ProductRuntimeOptions options, ProductDbContext db,
    ICompanyContext company, ICurrentUser user, IGroupSharedMasterAuthorizationService authority,
    IGroupSharedCommandReplayResolver replay, IServiceScopeFactory scopes, ICorrelationContext correlation,
    IProductCommandAttemptObserver observer) : IProductCogsClassificationServiceV1, IProductCogsClassificationReadPortV1
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    public async Task<ProductCogsClassificationViewV1> GetAsync(Guid productId, CancellationToken token = default)
    {
        RequireEnabled(); var identity = RequireIdentity();
        var before = GroupSharedMasterReadContext.FromServerDecision(await authority.GetSnapshotAsync(
            GroupSharedMasterKind.Product,GroupSharedMasterOperation.Read,token));
        RequireIdentity(identity); RequireReadIdentity(before,identity);
        ProductCogsClassificationViewV1? result;
        try { result = await ReadAsync(productId,identity.CompanyId,token); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { await ReauthorizeReadAsync(before,identity,token); throw Unavailable(); }
        await ReauthorizeReadAsync(before,identity,token);
        return result ?? throw new ProductCogsClassificationException(404,ProductCogsClassificationCodesV1.NotFound);
    }
    async Task<ProductCogsClassificationReadV1> IProductCogsClassificationReadPortV1.GetAsync(Guid productId, CancellationToken token)
    {
        var identity = RequireIdentity();
        if (!options.IsEnabled) return new ProductCogsClassificationReadV1(ProductCogsClassificationReadOutcomeV1.Unavailable,identity.CompanyId,productId,null,
            ProductCogsClassificationContractsV1.SourceVersion);
        try
        {
            var result = await ReadAsync(productId,identity.CompanyId,token); RequireIdentity(identity);
            return new ProductCogsClassificationReadV1(result is null ? ProductCogsClassificationReadOutcomeV1.NotFound
                : result.Classification is null ? ProductCogsClassificationReadOutcomeV1.NotSet : ProductCogsClassificationReadOutcomeV1.Available,
                identity.CompanyId,productId,result is null ? null : new ProductCogsClassificationSnapshotV1(result.ProductId,result.ProductVersion,result.ClassificationVersion,
                    result.Classification,result.ChangedInCompanyId,result.SourceVersion),ProductCogsClassificationContractsV1.SourceVersion);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch
        {
            RequireIdentity(identity);
            return new ProductCogsClassificationReadV1(ProductCogsClassificationReadOutcomeV1.Unavailable,identity.CompanyId,productId,null,
                ProductCogsClassificationContractsV1.SourceVersion);
        }
    }
    private async Task<ProductCogsClassificationViewV1?> ReadAsync(Guid productId,Guid companyId,CancellationToken token)
    {
        if (productId == Guid.Empty) throw new ProductCogsClassificationException(400,ProductCogsClassificationCodesV1.InvalidRequest);
        // One SQL statement reads the actual card version and its current owner classification; no default exists.
        var result = await (from product in db.Products.AsNoTracking() where product.Id == productId
            from classification in db.CogsClassifications.AsNoTracking().Where(x => x.ProductId == product.Id)
                .OrderByDescending(x => x.Version).Take(1).DefaultIfEmpty()
            select new { product.Id,product.Version,Classification = classification }).SingleOrDefaultAsync(token);
        return result is null ? null : ProductCogsClassificationParticipant.View(companyId,result.Id,result.Version,result.Classification);
    }
    public async Task<ProductCogsClassificationMutationV1> SetAsync(Guid productId,SetProductCogsClassificationV1 request,
        ProductCogsClassificationCommandMetadataV1 metadata,CancellationToken token = default)
    {
        RequireEnabled(); var identity = RequireIdentity();
        var initial = await CurrentAuthorityAsync(null,identity,token);
        if (request is null || metadata is null || productId == Guid.Empty || !TryVersion(request.ExpectedProductVersion,1,out var productVersion)
            || !TryVersion(request.ExpectedClassificationVersion,0,out var classificationVersion) || classificationVersion == long.MaxValue
            || request.Classification is not (ProductCogsClassificationKindV1.TradeGoods or ProductCogsClassificationKindV1.Manufactured)
            || !Canonical(request.Reason,ProductCogsClassificationContractsV1.MaximumReasonLength)
            || !ValidMetadata(metadata,identity))
        { await CurrentAuthorityAsync(initial,identity,token); throw new ProductCogsClassificationException(400,ProductCogsClassificationCodesV1.InvalidRequest); }
        var keyHash = Hash(Encoding.UTF8.GetBytes(metadata.IdempotencyKey));
        var requestHash = Hash(JsonSerializer.SerializeToUtf8Bytes(new { productId,request.ExpectedProductVersion,
            request.ExpectedClassificationVersion,request.Classification,request.Reason },Json));
        for (var attempt = 0; attempt <= options.MaximumAttempts; attempt++)
        {
            var resolved = await replay.ResolveAsync(GroupSharedMasterOperation.SetCogsClassification,keyHash,requestHash,token);
            RequireIdentity(identity); RequireSameAuthority(initial,resolved.Context);
            if (resolved.Kind == GroupSharedCommandReplayKind.Conflict)
                throw new ProductCogsClassificationException(409,ProductCogsClassificationCodesV1.IdempotencyConflict);
            if (resolved.Kind == GroupSharedCommandReplayKind.MatchingReplay)
            {
                var stored = Decode(resolved.Terminal!,identity.CompanyId,productId,request);
                await CurrentAuthorityAsync(initial,identity,token);
                if (stored.StatusCode >= 400)
                {
                    await using var noteScope = scopes.CreateAsyncScope();
                    noteScope.ServiceProvider.GetRequiredService<IRefusedAttemptRecorderV1>().NoteReplayedRefusal(
                        stored.StatusCode == 409 ? RefusedAttemptClassesV1.Concurrency : RefusedAttemptClassesV1.BusinessRule);
                }
                return Answer(stored,true,resolved.Terminal!.OriginalCorrelationId);
            }
            if (attempt >= options.MaximumAttempts) throw Unavailable();
            var current = await CurrentAuthorityAsync(initial,identity,token);
            await using var connection = new SqlConnection(options.ConnectionString); await connection.OpenAsync(token);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(identity.CompanyId);
            var commitStarted = false;
            try
            {
                await using var owner = await ProductCogsClassificationParticipant.EnlistAsync(connection,transaction,current,token);
                await using var audit = await GroupSharedAuditSession.EnlistAsync(scope.ServiceProvider.GetRequiredService<AuditDbContext>(),
                    connection,transaction,current,token);
                await using var terminal = await GroupSharedCommandTerminalSession.EnlistAsync(connection,transaction,current,keyHash,requestHash,token);
                var staged = await owner.StageAsync(productId,productVersion,classificationVersion,request.Classification,request.Reason,
                    metadata.CorrelationId,token);
                await observer.ObserveAsync(ProductCommandAttemptStage.ProductFlushed,current,token);
                if (staged.Changed)
                    audit.Enqueue(new GroupSharedAuditEventDraft(current,productId,ProductCogsClassificationParticipant.EventType,
                        productVersion,productVersion,staged.BeforeJson!,staged.AfterJson!,request.Reason,
                        metadata.SourceChannel,metadata.ClientIpAddress,metadata.UserAgent,metadata.CorrelationId,metadata.CausationId,
                        metadata.IdempotencyKey,AuditAiInvolvement.None,null));
                var response = new TerminalResponse(staged.StatusCode,staged.Code,staged.View);
                terminal.Stage(GroupSharedCommandTerminal.Create(GroupSharedCommandBinding.Create(current,keyHash,requestHash),
                    staged.StatusCode,JsonSerializer.Serialize(response,Json),metadata.CorrelationId,1));
                var fresh = await CurrentAuthorityAsync(current,identity,token);
                var refusalAuditFlushed = staged.StatusCode < 400;
                if (staged.Changed)
                {
                    await audit.FlushAsync(fresh,token);
                    await observer.ObserveAsync(ProductCommandAttemptStage.AuditFlushed,current,token);
                }
                else if (staged.StatusCode >= 400)
                {
                    var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
                    if (writer.HasPendingDrafts) throw new InvalidOperationException("Refusal audit requires a clean owner collector.");
                    scope.ServiceProvider.GetRequiredService<IRefusedAttemptRecorderV1>().Record(new RefusedAttemptV1
                    {
                        CompanyId = identity.CompanyId, CommandName = ProductCogsClassificationContractsV1.CommandName,
                        Operation = "SetCogsClassification", TargetType = "Product.Product", TargetId = productId.ToString("D"),
                        ExpectedVersion = classificationVersion, ActorType = AuditActorTypes.User, ActorId = metadata.ActorId,
                        PermissionCode = ProductCogsClassificationContractsV1.Manage, RoleCodes = ["SYSTEM_ADMINISTRATOR"],
                        HttpStatus = staged.StatusCode, ProblemCode = staged.Code!,
                        RefusalClass = staged.StatusCode == 409 ? RefusedAttemptClassesV1.Concurrency : RefusedAttemptClassesV1.BusinessRule,
                        CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId, SourceChannel = metadata.SourceChannel,
                        IdempotencyKey = metadata.IdempotencyKey,
                    });
                    await writer.WritePendingAsync(transaction,token); refusalAuditFlushed = true;
                    await observer.ObserveAsync(ProductCommandAttemptStage.AuditFlushed,current,token);
                }
                await terminal.FlushAsync(fresh,token);
                await observer.ObserveAsync(ProductCommandAttemptStage.TerminalFlushed,current,token);
                await observer.ObserveAsync(ProductCommandAttemptStage.BeforeCommit,current,token);
                await CurrentAuthorityAsync(current,identity,token);
                if (!owner.IsFlushed || staged.Changed && !audit.IsFlushed || !refusalAuditFlushed || !terminal.IsFlushed)
                    throw new InvalidOperationException("The global classification owners have not all flushed.");
                commitStarted = true; await transaction.CommitAsync(token);
                // Only an actual change emits a canonical global audit; no-op/refusal terminals do not manufacture a card change.
                return Answer(response,false,metadata.CorrelationId);
            }
            catch (ProductCogsClassificationException exception) when (commitStarted && exception.StatusCode is 404 or 409)
            { throw; } // A committed refusal is immutable, not a failed SQL attempt.
            catch (Exception exception)
            {
                var rolledBack = false;
                try { await transaction.RollbackAsync(CancellationToken.None); rolledBack = true; }
                catch (Exception cleanup) when (cleanup is SqlException or InvalidOperationException) { }
                if (exception is OperationCanceledException && token.IsCancellationRequested) throw;
                await CurrentAuthorityAsync(current,identity,token);
                if (rolledBack && !commitStarted && exception is GroupSharedCommandTerminalAlreadyExistsException) continue;
                throw Unavailable(); // No retry or success after an unknown commit outcome.
            }
        }
        throw Unavailable();
    }
    private bool ValidMetadata(ProductCogsClassificationCommandMetadataV1 metadata,Identity identity) =>
        metadata.CompanyId == identity.CompanyId && metadata.ActorId == identity.ActorId.ToString("D")
        && !string.IsNullOrEmpty(metadata.IdempotencyKey) && metadata.IdempotencyKey.Length <= 128
        && metadata.IdempotencyKey.All(c => c is >= (char)33 and <= (char)126 && c != ',')
        && Canonical(metadata.CorrelationId,128) && (metadata.CausationId is null || Canonical(metadata.CausationId,128))
        && metadata.SourceChannel is ("Web" or "Mobile" or "Import" or "Integration" or "AiAssisted" or "System")
        && (metadata.ClientIpAddress is null || Canonical(metadata.ClientIpAddress,64))
        && (metadata.UserAgent is null || Canonical(metadata.UserAgent,512))
        && correlation.IsResolved && metadata.CorrelationId == correlation.CorrelationId && metadata.CausationId == correlation.CausationId
        && metadata.SourceChannel == correlation.SourceChannel && metadata.ClientIpAddress == correlation.ClientIpAddress
        && metadata.UserAgent == correlation.UserAgent;
    private static TerminalResponse Decode(GroupSharedCommandTerminal terminal,Guid companyId,Guid productId,
        SetProductCogsClassificationV1 request)
    {
        if (terminal.SchemaVersion != 1 || terminal.StatusCode is not (200 or 404 or 409)) throw Unavailable();
        var result = JsonSerializer.Deserialize<TerminalResponse>(terminal.ResponseJson,Json) ?? throw Unavailable();
        if (result.StatusCode != terminal.StatusCode || result.StatusCode == 200 && (result.Code is not null || result.View is null)
            || result.StatusCode == 404 && (result.Code != ProductCogsClassificationCodesV1.NotFound || result.View is not null)
            || result.StatusCode == 409 && (result.Code != ProductCogsClassificationCodesV1.VersionConflict || result.View is not null)) throw Unavailable();
        if (result.View is { } view && (view.CompanyId != companyId || view.ProductId != productId || view.Classification != request.Classification
            || view.ProductVersion != request.ExpectedProductVersion || view.SourceVersion != ProductCogsClassificationContractsV1.SourceVersion
            || !TryVersion(view.ClassificationVersion,1,out var version) || !TryVersion(request.ExpectedClassificationVersion,0,out var expected)
            || version != expected && version != expected+1 || version == expected+1 && view.ChangedInCompanyId != companyId
            || view.ChangedInCompanyId == Guid.Empty || view.ChangedInCompanyId is null
            || view.UpdatedAtUtc is null || view.UpdatedAtUtc.Value.Offset != TimeSpan.Zero)) throw Unavailable();
        return result;
    }
    private static ProductCogsClassificationMutationV1 Answer(TerminalResponse result,bool isReplay,string originalCorrelation)
    {
        if (result.StatusCode != 200) throw new ProductCogsClassificationException(result.StatusCode,result.Code!,isReplay,originalCorrelation);
        return new ProductCogsClassificationMutationV1(result.View!,isReplay,originalCorrelation);
    }
    private async Task<GroupSharedMasterCommandContext> CurrentAuthorityAsync(GroupSharedMasterCommandContext? expected,Identity identity,CancellationToken token)
    {
        RequireIdentity(identity);
        var result = GroupSharedMasterCommandContext.FromServerDecision(await authority.GetSnapshotAsync(GroupSharedMasterKind.Product,
            GroupSharedMasterOperation.SetCogsClassification,token),GroupSharedMasterOperation.SetCogsClassification);
        RequireIdentity(identity);
        if (result.LocalActorId != identity.ActorId || result.OriginCompanyId != identity.CompanyId) throw new UnauthorizedAccessException();
        if (expected is not null) RequireSameAuthority(expected,result);
        return result;
    }
    private async Task ReauthorizeReadAsync(GroupSharedMasterReadContext expected,Identity identity,CancellationToken token)
    {
        var current = GroupSharedMasterReadContext.FromServerDecision(await authority.GetSnapshotAsync(GroupSharedMasterKind.Product,
            GroupSharedMasterOperation.Read,token));
        RequireIdentity(identity); RequireReadIdentity(current,identity);
        if (current.AuthorityRevision != expected.AuthorityRevision) throw new UnauthorizedAccessException();
    }
    private static void RequireReadIdentity(GroupSharedMasterReadContext context,Identity identity)
    { if (context.LocalActorId != identity.ActorId || context.OriginCompanyId != identity.CompanyId) throw new UnauthorizedAccessException(); }
    private static void RequireSameAuthority(GroupSharedMasterCommandContext expected,GroupSharedMasterCommandContext current)
    {
        if (expected.LocalActorId != current.LocalActorId || expected.OriginCompanyId != current.OriginCompanyId
            || expected.Operation != current.Operation || expected.AuthorityRevision != current.AuthorityRevision) throw new UnauthorizedAccessException();
    }
    private Identity RequireIdentity(Identity? expected = null)
    {
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.IsAuthenticated
            || !Guid.TryParseExact(user.UserId,"D",out var actor) || actor == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
            throw new UnauthorizedAccessException();
        var actual = new Identity(company.CompanyId,actor);
        if (expected is not null && actual != expected) throw new UnauthorizedAccessException();
        return actual;
    }
    private void RequireEnabled() { if (!options.IsEnabled) throw Unavailable(); }
    private static bool TryVersion(string? value,long minimum,out long version) => long.TryParse(value,NumberStyles.None,
        CultureInfo.InvariantCulture,out version) && version >= minimum && version.ToString(CultureInfo.InvariantCulture) == value;
    private static bool Canonical(string? value,int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum
        && value == value.Trim() && !value.Any(char.IsControl);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static ProductCogsClassificationException Unavailable() => new ProductCogsClassificationException(503,ProductCogsClassificationCodesV1.Unavailable);
    private sealed record Identity(Guid CompanyId,Guid ActorId);
    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    private sealed record TerminalResponse(int StatusCode,string? Code,ProductCogsClassificationViewV1? View);
}
