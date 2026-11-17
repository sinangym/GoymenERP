using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence;

/// <summary>KLN-D33/D73/D109. Dedicated owner context per attempt; no tracker clearing or mutation of a published row.</summary>
public sealed class DocumentBrandingService(DbContextOptions<DocumentsDbContext> options, ICompanyContext company,
    ICurrentUser user, IErpAuthorityRevalidationService authority, ICorrelationContext correlation,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, IRefusedAttemptRecorderV1 refusals,
    DocumentsServiceBindings bindings, DocumentBrandingFilePolicy filePolicy, TimeProvider clock, IDocumentObjectStoreV1? objects = null,
    IDocumentBrandingAssetRenderValidatorV1? renderValidator = null)
    : ICompanyDocumentBrandingServiceV1, IDocumentPublishedTemplateReadPortV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private readonly DocumentBrandingAssetReader assets = new(company, objects, bindings);

    public async Task<CompanyDocumentBrandingCurrentV1> GetBrandingAsync(CancellationToken cancellationToken = default)
    {
        var frame = await RequireAsync(DocumentBrandingPermissionsV1.Manage, cancellationToken);
        await using var db = NewContext();
        var row = await db.BrandingRevisions.AsNoTracking().OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
        var value = row is null ? null : await BrandingAsync(db, row, cancellationToken);
        var upload = await authority.RevalidatePermissionScopeAsync(DocumentsPermissionManifestV1.FilesUpload, frame.CompanyId, cancellationToken);
        await FenceAsync(frame, cancellationToken);
        return new(frame.CompanyId, value, new(DocumentContentPolicy.InfosOf(RequireFilePolicy())
            .Select(x => x.ContentType).ToArray(), bindings.MaximumUploadBytes, upload.IsGranted && upload.IsCompanyWide));
    }

    public async Task<DocumentTemplateCurrentV1> GetTemplateAsync(DocumentTemplateModuleV1 module, CancellationToken cancellationToken = default)
    {
        var code = DocumentBrandingContractV1.TemplateCode(module) ?? throw Invalid();
        var requestedCompany = company.IsResolved ? company.CompanyId : Guid.Empty;
        var requestedActor = user.UserId;
        Frame frame;
        try { frame = await RequireAsync(DocumentBrandingPermissionsV1.Prepare(module)!, cancellationToken); }
        catch (DocumentBrandingServiceException ex) when (ex.Status == 403)
        {
            if (!MatchesCompany(requestedCompany) || user.UserId != requestedActor) throw;
            frame = await RequireAsync(DocumentBrandingPermissionsV1.Publish(module)!, cancellationToken);
        }
        await using var db = NewContext();
        // One serializable read transaction: the displayed draft/publication belongs to one consistent owner state.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var draft = await LatestDraftAsync(db, module, code, cancellationToken);
        var publication = await LatestPublicationAsync(db, module, code, cancellationToken);
        var branding = await db.BrandingRevisions.AsNoTracking().OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
        var brandingView = branding is null ? null : await BrandingAsync(db, branding, cancellationToken);
        var draftView = draft is null ? null : await DraftAsync(db, draft, cancellationToken);
        var publishedView = publication is null ? null : await PublicationAsync(db, publication, cancellationToken);
        await FenceAsync(frame, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new(frame.CompanyId, module, code, draftView, publishedView, brandingView);
    }

    public async Task<CompanyDocumentBrandingMutationV1> SetBrandingAsync(SetCompanyDocumentBrandingV1 request,
        DocumentCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answer = await ExecuteAsync(DocumentBrandingCommandsV1.Set, DocumentBrandingPermissionsV1.Manage, request, metadata,
            async (db, frame, ct) =>
            {
                var expected = Version(request.ExpectedVersion, allowZero: true);
                var reason = Reason(request.Reason);
                var previous = await db.BrandingRevisions.AsNoTracking().OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
                if ((previous?.Version ?? 0) != expected) throw Conflict();
                await assets.ReadAsync(db, frame.CompanyId, request.Logo?.DocumentId, request.Logo?.VersionNo, ct);
                await assets.ReadAsync(db, frame.CompanyId, request.Letterhead?.DocumentId, request.Letterhead?.VersionNo, ct);
                await FenceAsync(frame, ct);
                var row = new CompanyBrandingRevisionRow { CompanyId = frame.CompanyId, Version = checked(expected + 1),
                    LogoDocumentId = request.Logo?.DocumentId, LogoVersionNo = request.Logo?.VersionNo,
                    LetterheadDocumentId = request.Letterhead?.DocumentId, LetterheadVersionNo = request.Letterhead?.VersionNo,
                    PreparedBy = frame.ActorId, PreparedAtUtc = clock.GetUtcNow(), Reason = reason };
                var view = await BrandingAsync(db, row, ct);
                db.BrandingRevisions.Add(row);
                return new Mutation<CompanyDocumentBrandingV1>(view, expected, row.Version, reason);
            }, cancellationToken);
        return new(answer.Value, answer.IsReplay, answer.CorrelationId);
    }

    public async Task<DocumentTemplateDraftMutationV1> PrepareAsync(PrepareDocumentTemplateV1 request,
        DocumentCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Known(request.Module, request.TemplateCode);
        var answer = await ExecuteAsync(DocumentBrandingCommandsV1.Prepare, DocumentBrandingPermissionsV1.Prepare(request.Module)!, request, metadata,
            async (db, frame, ct) =>
            {
                var expected = Version(request.ExpectedDraftVersion, true);
                var brandingVersion = Version(request.ExpectedBrandingVersion, false);
                var reason = Reason(request.Reason);
                var previous = await LatestDraftAsync(db, request.Module, request.TemplateCode, ct);
                if ((previous?.Version ?? 0) != expected) throw Conflict();
                var branding = await db.BrandingRevisions.AsNoTracking().OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
                if (branding is null || branding.Version != brandingVersion) throw Conflict();
                var row = new DocumentTemplateDraftRow { CompanyId = frame.CompanyId, Module = (int)request.Module,
                    TemplateCode = request.TemplateCode, Version = checked(expected + 1), BrandingVersion = brandingVersion,
                    PreparedBy = frame.ActorId, PreparedAtUtc = clock.GetUtcNow(), Reason = reason };
                var view = await DraftAsync(db, row, ct);
                await ValidateRenderAsync(db, frame.CompanyId, view.Logo, view.Letterhead, ct);
                db.TemplateDrafts.Add(row);
                return new Mutation<DocumentTemplateDraftV1>(view, expected, row.Version, reason);
            }, cancellationToken);
        return new(answer.Value, answer.IsReplay, answer.CorrelationId);
    }

    public async Task<DocumentTemplatePublicationMutationV1> PublishAsync(PublishDocumentTemplateV1 request,
        DocumentCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Known(request.Module, request.TemplateCode);
        var answer = await ExecuteAsync(DocumentBrandingCommandsV1.Publish, DocumentBrandingPermissionsV1.Publish(request.Module)!, request, metadata,
            async (db, frame, ct) =>
            {
                var expectedDraft = Version(request.ExpectedDraftVersion, false);
                var expectedPublication = Version(request.ExpectedPublicationVersion, true);
                var reason = Reason(request.Reason);
                var draft = await LatestDraftAsync(db, request.Module, request.TemplateCode, ct);
                var previous = await LatestPublicationAsync(db, request.Module, request.TemplateCode, ct);
                if (draft is null || draft.Version != expectedDraft || (previous?.Version ?? 0) != expectedPublication) throw Conflict();
                if (string.Equals(draft.PreparedBy, frame.ActorId, StringComparison.Ordinal))
                    throw new DocumentBrandingServiceException(409, "document_template_maker_cannot_publish");
                if (previous?.DraftVersion == draft.Version) throw new DocumentBrandingServiceException(409, "document_template_already_published");
                var row = new DocumentTemplatePublicationRow { CompanyId = frame.CompanyId, Module = (int)request.Module,
                    TemplateCode = request.TemplateCode, Version = checked(expectedPublication + 1), Id = Guid.NewGuid(),
                    DraftVersion = draft.Version, BrandingVersion = draft.BrandingVersion, PreparedBy = draft.PreparedBy,
                    PublishedBy = frame.ActorId, PublishedAtUtc = clock.GetUtcNow(), Reason = reason };
                var view = await PublicationAsync(db, row, ct);
                if (view.Logo is null && view.Letterhead is null)
                    throw new DocumentBrandingServiceException(409, "document_template_assets_required");
                await ValidateRenderAsync(db, frame.CompanyId, view.Logo, view.Letterhead, ct);
                db.TemplatePublications.Add(row);
                return new Mutation<DocumentTemplatePublicationV1>(view, expectedPublication, row.Version, reason);
            }, cancellationToken);
        return new(answer.Value, answer.IsReplay, answer.CorrelationId);
    }

    public Task<DocumentPublishedTemplateReadV1> ReadAsync(Guid companyId, DocumentTemplateModuleV1 module, string templateCode,
        CancellationToken cancellationToken = default) => ReadPublicationCoreAsync(companyId, module, templateCode, null, null, cancellationToken);

    public Task<DocumentPublishedTemplateReadV1> ReadPublicationAsync(Guid companyId, DocumentTemplateModuleV1 module,
        string templateCode, Guid publicationId, string publicationVersion, CancellationToken cancellationToken = default) =>
        ReadPublicationCoreAsync(companyId, module, templateCode, publicationId, publicationVersion, cancellationToken);

    private async Task<DocumentPublishedTemplateReadV1> ReadPublicationCoreAsync(Guid companyId, DocumentTemplateModuleV1 module,
        string templateCode, Guid? publicationId, string? publicationVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DocumentPublishedTemplateReadV1 Closed(string why) => new(DocumentPublishedTemplateOutcomeV1.Unavailable, companyId,
            module, templateCode, null, null, null, why);
        if (!DocumentBrandingContractV1.IsKnown(module, templateCode) || !MatchesCompany(companyId)) return Closed("source_frame_invalid");
        try
        {
            RequireFilePolicy();
            await using var db = NewContext();
            if (publicationId is not null && (publicationId == Guid.Empty || !PositiveVersion(publicationVersion!)))
                return Closed("publication_selection_invalid");
            var row = publicationId is null ? await LatestPublicationAsync(db, module, templateCode, cancellationToken)
                : await db.TemplatePublications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == publicationId
                    && x.Module == (int)module && x.TemplateCode == templateCode, cancellationToken);
            RequireFilePolicy();
            if (!MatchesCompany(companyId)) return Closed("company_changed");
            if (publicationId is not null && (row is null || Text(row.Version) != publicationVersion)) return Closed("publication_selection_missing");
            if (row is null) return new(DocumentPublishedTemplateOutcomeV1.NoSource, companyId, module, templateCode, null, null, null);
            var branding = await db.BrandingRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.Version == row.BrandingVersion, cancellationToken)
                ?? throw Unavailable();
            var logo = await assets.ReadAsync(db, companyId, branding.LogoDocumentId, branding.LogoVersionNo, cancellationToken);
            var letterhead = await assets.ReadAsync(db, companyId, branding.LetterheadDocumentId, branding.LetterheadVersionNo, cancellationToken);
            RequireFilePolicy();
            if (!MatchesCompany(companyId) || logo is null && letterhead is null) return Closed("publication_invalid");
            return new(DocumentPublishedTemplateOutcomeV1.Available, companyId, module, templateCode,
                Publication(row, logo?.Proof, letterhead?.Proof), logo, letterhead);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is DocumentBrandingServiceException or DbUpdateException or System.Data.Common.DbException
            or InvalidOperationException or ArgumentException) { return Closed("publication_unavailable"); }
    }

    private async Task<Stored<T>> ExecuteAsync<T>(string command, string permission, object request, DocumentCommandMetadata metadata,
        Func<DocumentsDbContext, Frame, CancellationToken, Task<Mutation<T>>> mutate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ct.ThrowIfCancellationRequested();
        var frame = await RequireAsync(permission, ct);
        ValidateMetadata(metadata);
        var hash = Hash(JsonSerializer.Serialize(new { command, request }, Json));
        var key = Hash(metadata.IdempotencyKey);
        if (auditWriter.HasPendingDrafts) throw Unavailable();
        try
        {
            await using var db = NewContext();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await AttemptAsync(db, command, key, ct);
            if (previous is not null)
            {
                await FenceAsync(frame, ct);
                return Replay<T>(previous, frame, hash);
            }
            var changed = await mutate(db, frame, ct);
            await FenceAsync(frame, ct);
            RequireMutationKinds(changed.Value);
            db.BrandingAttempts.Add(Attempt(frame, command, key, hash, metadata.CorrelationId, 200, null, JsonSerializer.Serialize(changed.Value, Json)));
            await db.SaveChangesAsync(ct);
            await FenceAsync(frame, ct);
            audit.Enqueue(new AuditEventDraft(frame.CompanyId, command + ".recorded", "DocumentBranding", frame.CompanyId.ToString("D"),
                command, changed.BeforeVersion, changed.AfterVersion, AuditActorTypes.User, frame.ActorId, permission, frame.Roles,
                JsonSerializer.Serialize(new { version = Text(changed.BeforeVersion) }, Json), JsonSerializer.Serialize(changed.Value, Json),
                changed.Reason, null, AuditAiInvolvement.None, null, correlation.SourceChannel, correlation.ClientIpAddress,
                correlation.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
            await auditWriter.WritePendingAsync(tx.GetDbTransaction(), ct);
            await FenceAsync(frame, ct);
            RequireMutationKinds(changed.Value);
            await tx.CommitAsync(ct);
            return new(changed.Value, false, metadata.CorrelationId);
        }
        catch (DocumentBrandingServiceException ex) when (ex.Status == 409 && !ex.IsReplay && ex.Code != "same_key_payload_mismatch")
        {
            // The failed mutation's context/transaction has been disposed before this fresh terminal write.
            auditWriter.DiscardPending();
            return await RecordTerminalAsync<T>(frame, command, key, hash, metadata, ex.Code, ct);
        }
        catch (OperationCanceledException) { auditWriter.DiscardPending(); throw; }
        catch (DocumentBrandingServiceException) { auditWriter.DiscardPending(); throw; }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException or InvalidOperationException or JsonException or OverflowException)
        { auditWriter.DiscardPending(); throw Unavailable(); }
    }

    private async Task<Stored<T>> RecordTerminalAsync<T>(Frame frame, string command, string key, string hash,
        DocumentCommandMetadata metadata, string code, CancellationToken ct)
    {
        await FenceAsync(frame, ct);
        try
        {
            await using var db = NewContext();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await AttemptAsync(db, command, key, ct);
            if (previous is not null) { await FenceAsync(frame, ct); return Replay<T>(previous, frame, hash); }
            db.BrandingAttempts.Add(Attempt(frame, command, key, hash, metadata.CorrelationId, 409, code, null));
            await FenceAsync(frame, ct);
            await db.SaveChangesAsync(ct);
            await FenceAsync(frame, ct);
            refusals.Record(new RefusedAttemptV1 { CompanyId = frame.CompanyId, CommandName = command, Operation = command,
                TargetType = "DocumentBranding", TargetId = frame.CompanyId.ToString("D"), ActorType = AuditActorTypes.User,
                ActorId = frame.ActorId, PermissionCode = frame.Permission, RoleCodes = frame.Roles, HttpStatus = 409,
                ProblemCode = code, RefusalClass = code == "document_branding_version_conflict" ? RefusedAttemptClassesV1.Concurrency : RefusedAttemptClassesV1.BusinessRule,
                CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId, SourceChannel = correlation.SourceChannel,
                IdempotencyKey = metadata.IdempotencyKey });
            await auditWriter.WritePendingAsync(tx.GetDbTransaction(), ct);
            await FenceAsync(frame, ct);
            await tx.CommitAsync(ct);
        }
        catch (OperationCanceledException) { auditWriter.DiscardPending(); throw; }
        catch (DocumentBrandingServiceException) { auditWriter.DiscardPending(); throw; }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException or InvalidOperationException)
        { auditWriter.DiscardPending(); throw Unavailable(); }
        throw new DocumentBrandingServiceException(409, code, originalCorrelationId: metadata.CorrelationId);
    }

    private Stored<T> Replay<T>(DocumentBrandingAttemptRow row, Frame frame, string hash)
    {
        if (row.CompanyId != frame.CompanyId || row.ActorId != frame.ActorId || row.RequestHash != hash)
            throw new DocumentBrandingServiceException(409, "same_key_payload_mismatch");
        if (!DocumentBrandingCommandsV1.IsKnown(row.CommandName) || string.IsNullOrWhiteSpace(row.CorrelationId)) throw Unavailable();
        if (row.Status == 409 && row.Code is { Length: > 0 } code && row.ResultJson is null)
        { refusals.NoteReplayedRefusal(RefusedAttemptClassesV1.BusinessRule); throw new DocumentBrandingServiceException(409, code, true, row.CorrelationId); }
        if (row.Status != 200 || row.Code is not null || row.ResultJson is null) throw Unavailable();
        var value = JsonSerializer.Deserialize<T>(row.ResultJson, Json) ?? throw Unavailable();
        var valid = value switch
        {
            CompanyDocumentBrandingV1 branding => row.CommandName == DocumentBrandingCommandsV1.Set
                && branding.CompanyId == frame.CompanyId && branding.PreparedBy == frame.ActorId && PositiveVersion(branding.Version)
                && ValidProof(branding.Logo, frame.CompanyId) && ValidProof(branding.Letterhead, frame.CompanyId),
            DocumentTemplateDraftV1 draft => row.CommandName == DocumentBrandingCommandsV1.Prepare
                && draft.CompanyId == frame.CompanyId && draft.PreparedBy == frame.ActorId && PositiveVersion(draft.Version)
                && PositiveVersion(draft.BrandingVersion) && DocumentBrandingContractV1.IsKnown(draft.Module, draft.TemplateCode)
                && DocumentBrandingPermissionsV1.Prepare(draft.Module) == frame.Permission
                && ValidProof(draft.Logo, frame.CompanyId) && ValidProof(draft.Letterhead, frame.CompanyId),
            DocumentTemplatePublicationV1 publication => row.CommandName == DocumentBrandingCommandsV1.Publish
                && publication.CompanyId == frame.CompanyId && publication.PublishedBy == frame.ActorId && publication.PreparedBy != frame.ActorId
                && publication.PublicationId != Guid.Empty && PositiveVersion(publication.Version) && PositiveVersion(publication.DraftVersion)
                && PositiveVersion(publication.BrandingVersion) && publication.TemplateVersion == "pub." + publication.Version
                && DocumentBrandingContractV1.IsKnown(publication.Module, publication.TemplateCode)
                && DocumentBrandingPermissionsV1.Publish(publication.Module) == frame.Permission
                && (publication.Logo is not null || publication.Letterhead is not null)
                && ValidProof(publication.Logo, frame.CompanyId) && ValidProof(publication.Letterhead, frame.CompanyId),
            _ => false,
        };
        return valid ? new(value, true, row.CorrelationId) : throw Unavailable();
    }

    private async Task<CompanyDocumentBrandingV1> BrandingAsync(DocumentsDbContext db, CompanyBrandingRevisionRow row, CancellationToken ct)
    {
        var logo = await assets.ReadAsync(db, row.CompanyId, row.LogoDocumentId, row.LogoVersionNo, ct);
        var letterhead = await assets.ReadAsync(db, row.CompanyId, row.LetterheadDocumentId, row.LetterheadVersionNo, ct);
        return new(row.CompanyId, Text(row.Version), logo?.Proof, letterhead?.Proof, row.PreparedBy, row.PreparedAtUtc, row.Reason);
    }
    private async Task<DocumentTemplateDraftV1> DraftAsync(DocumentsDbContext db, DocumentTemplateDraftRow row, CancellationToken ct)
    {
        var branding = await db.BrandingRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.Version == row.BrandingVersion, ct) ?? throw Unavailable();
        var view = await BrandingAsync(db, branding, ct);
        return new(row.CompanyId, (DocumentTemplateModuleV1)row.Module, row.TemplateCode, Text(row.Version), Text(row.BrandingVersion),
            view.Logo, view.Letterhead, row.PreparedBy, row.PreparedAtUtc, row.Reason);
    }
    private async Task<DocumentTemplatePublicationV1> PublicationAsync(DocumentsDbContext db, DocumentTemplatePublicationRow row, CancellationToken ct)
    {
        var branding = await db.BrandingRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.Version == row.BrandingVersion, ct) ?? throw Unavailable();
        var view = await BrandingAsync(db, branding, ct);
        return Publication(row, view.Logo, view.Letterhead);
    }
    private static DocumentTemplatePublicationV1 Publication(DocumentTemplatePublicationRow row, DocumentBrandingAssetProofV1? logo,
        DocumentBrandingAssetProofV1? letterhead) => new(row.Id, row.CompanyId, (DocumentTemplateModuleV1)row.Module, row.TemplateCode,
            Text(row.Version), "pub." + Text(row.Version), Text(row.DraftVersion), Text(row.BrandingVersion), logo, letterhead,
            row.PreparedBy, row.PublishedBy, row.PublishedAtUtc, row.Reason);
    private async Task ValidateRenderAsync(DocumentsDbContext db, Guid companyId, DocumentBrandingAssetProofV1? logo,
        DocumentBrandingAssetProofV1? letterhead, CancellationToken ct)
    {
        RequireProofKinds([logo, letterhead]);
        foreach (var (proof, slot) in new[] { (logo, DocumentBrandingAssetSlotV1.Logo), (letterhead, DocumentBrandingAssetSlotV1.Letterhead) })
        {
            if (proof is null) continue;
            if (renderValidator is null) throw Unavailable();
            var asset = await assets.ReadAsync(db, companyId, proof.DocumentId, proof.VersionNo, ct) ?? throw Unavailable();
            DocumentBrandingAssetRenderOutcomeV1 result;
            try { result = await renderValidator.ValidateAsync(asset, slot, ct); }
            catch (OperationCanceledException) { throw; }
            catch { throw Unavailable(); }
            if (result == DocumentBrandingAssetRenderOutcomeV1.Invalid)
                throw new DocumentBrandingServiceException(422, "document_branding_asset_render_invalid");
            if (result != DocumentBrandingAssetRenderOutcomeV1.Available) throw Unavailable();
        }
    }
    private static Task<DocumentTemplateDraftRow?> LatestDraftAsync(DocumentsDbContext db, DocumentTemplateModuleV1 module, string code, CancellationToken ct) =>
        db.TemplateDrafts.AsNoTracking().Where(x => x.Module == (int)module && x.TemplateCode == code).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    private static Task<DocumentTemplatePublicationRow?> LatestPublicationAsync(DocumentsDbContext db, DocumentTemplateModuleV1 module, string code, CancellationToken ct) =>
        db.TemplatePublications.AsNoTracking().Where(x => x.Module == (int)module && x.TemplateCode == code).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    private static Task<DocumentBrandingAttemptRow?> AttemptAsync(DocumentsDbContext db, string command, string key, CancellationToken ct) =>
        db.BrandingAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.CommandName == command && x.IdempotencyKeyHash == key, ct);
    private static DocumentBrandingAttemptRow Attempt(Frame frame, string command, string key, string hash, string correlationId, int status, string? code, string? result) =>
        new() { CompanyId = frame.CompanyId, CommandName = command, IdempotencyKeyHash = key, RequestHash = hash,
            ActorId = frame.ActorId, CorrelationId = correlationId, Status = status, Code = code, ResultJson = result };
    private DocumentsDbContext NewContext() => new(options, company);
    private async Task<Frame> RequireAsync(string permission, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw new DocumentBrandingServiceException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw new DocumentBrandingServiceException(403, "forbidden");
        if (!ErpPermissions.IsKnown(permission)) throw new DocumentBrandingServiceException(503, "permission_unregistered");
        var companyId = company.CompanyId;
        var actor = user.UserId;
        var decision = await authority.RevalidatePermissionScopeAsync(permission, companyId, ct);
        if (!decision.IsGranted || !decision.IsCompanyWide || !MatchesCompany(companyId) || !user.IsAuthenticated || user.UserId != actor
            || !user.CanAccessCompany(companyId))
            throw new DocumentBrandingServiceException(403, "forbidden");
        RequireFilePolicy();
        return new(companyId, actor, permission, [.. decision.RoleCodes.Order(StringComparer.Ordinal)]);
    }
    private IReadOnlyList<DocumentFileKind> RequireFilePolicy() => filePolicy.Read() is { Available: true } value ? value.Kinds : throw Unavailable();
    private void RequireMutationKinds<T>(T value)
    {
        var proofs = value switch
        {
            CompanyDocumentBrandingV1 branding => new[] { branding.Logo, branding.Letterhead },
            DocumentTemplateDraftV1 draft => new[] { draft.Logo, draft.Letterhead },
            DocumentTemplatePublicationV1 publication => new[] { publication.Logo, publication.Letterhead },
            _ => throw Unavailable(),
        };
        RequireProofKinds(proofs);
    }
    private void RequireProofKinds(IReadOnlyList<DocumentBrandingAssetProofV1?> proofs)
    {
        var allowed = DocumentContentPolicy.InfosOf(RequireFilePolicy());
        if (proofs.Any(proof => proof is not null && !allowed.Any(info => info.ContentType == proof.ContentType)))
            throw new DocumentBrandingServiceException(422, "document_branding_asset_type_not_allowed");
    }
    private async Task FenceAsync(Frame frame, CancellationToken ct)
    {
        var actual = await RequireAsync(frame.Permission, ct);
        if (actual.CompanyId != frame.CompanyId || actual.ActorId != frame.ActorId) throw new DocumentBrandingServiceException(403, "forbidden");
    }
    private bool MatchesCompany(Guid companyId) => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId;
    private static long Version(string text, bool allowZero) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value >= (allowZero ? 0 : 1) && Text(value) == text ? value : throw Invalid();
    private static string Reason(string text) => !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= DocumentBrandingContractV1.MaximumReasonLength
        && !text.Any(char.IsControl) ? text.Trim() : throw Invalid();
    private static void Known(DocumentTemplateModuleV1 module, string code) { if (!DocumentBrandingContractV1.IsKnown(module, code)) throw Invalid(); }
    private static void ValidateMetadata(DocumentCommandMetadata value)
    {
        if (string.IsNullOrWhiteSpace(value.IdempotencyKey) || value.IdempotencyKey.Length > 128 || value.IdempotencyKey.Trim() != value.IdempotencyKey
            || value.IdempotencyKey.Any(c => char.IsControl(c) || c == ',') || string.IsNullOrWhiteSpace(value.CorrelationId)
            || value.CorrelationId.Length > 128 || value.CorrelationId.Any(char.IsControl) || value.CausationId?.Length > 128) throw Invalid();
    }
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static bool PositiveVersion(string text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value > 0 && Text(value) == text;
    private static bool ValidProof(DocumentBrandingAssetProofV1? proof, Guid companyId) => proof is null
        || proof.CompanyId == companyId && proof.DocumentId != Guid.Empty && proof.VersionNo > 0 && proof.SizeBytes > 0
        && DocumentContentHash.IsWellFormed(proof.Sha256) && proof.ScannerId is { Length: > 0 and <= 64 }
        && proof.UploadedBy is { Length: > 0 and <= 128 } && proof.ContentType is "image/png" or "image/jpeg" or "application/pdf";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static DocumentBrandingServiceException Invalid() => new(400, "validation_failed");
    private static DocumentBrandingServiceException Conflict() => new(409, "document_branding_version_conflict");
    private static DocumentBrandingServiceException Unavailable() => new(503, "service_unavailable");
    private sealed record Frame(Guid CompanyId, string ActorId, string Permission, IReadOnlyList<string> Roles);
    private sealed record Mutation<T>(T Value, long BeforeVersion, long AfterVersion, string Reason);
    private sealed record Stored<T>(T Value, bool IsReplay, string CorrelationId);
}
