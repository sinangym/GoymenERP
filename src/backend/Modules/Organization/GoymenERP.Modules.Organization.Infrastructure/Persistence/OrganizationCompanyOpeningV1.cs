using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Domain.CompanySetup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

public sealed class OrganizationCompanyOpeningV1(OrganizationDbContext database, ICurrentUser currentUser,
    ICompanyContext companyContext, ICompanySetupGroupAuthorityV1 authority, IAuditEventCollector auditEvents,
    IAuditTransactionWriterV1 auditWriter, IRefusedAttemptRecorderV1 refusedAttempts, TimeProvider clock) : IOrganizationCompanyOpeningV1
{
    private const string CommandName = "organization.company.open.v1";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public async Task<CompanySetupCapabilitiesV1> ReadCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var actor = Actor();
        var open = await authority.ReadAsync(CompanySetupGroupOperationV1.OpenCompany, cancellationToken);
        var assign = await authority.ReadAsync(CompanySetupGroupOperationV1.AssignFirstAdmin, cancellationToken);
        if (Actor() != actor || open.IsGranted && open.ActorUserId != actor || assign.IsGranted && assign.ActorUserId != actor)
            throw new UnauthorizedAccessException();
        if (open.IsGranted) await StillAsync(open, null, cancellationToken);
        if (assign.IsGranted) await StillAsync(assign, null, cancellationToken);
        return new CompanySetupCapabilitiesV1(open.IsGranted, assign.IsGranted);
    }

    public async Task<IReadOnlyList<CompanySetupCompanyV1>> ReadPendingAsync(CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(CompanySetupGroupOperationV1.AssignFirstAdmin, null, cancellationToken);
        var rows = await database.Companies.AsNoTracking().Where(company => company.LifecycleState == "SetupPending")
            .OrderBy(company => company.Code).ThenBy(company => company.Id)
            .Select(company => new CompanySetupCompanyV1(company.Id, company.Code, company.LegalName,
                company.LifecycleState, company.Version)).ToListAsync(cancellationToken);
        await StillAsync(scope, null, cancellationToken);
        return rows;
    }

    public async Task<CompanyOpeningResultV1> OpenAsync(OpenCompanyV1 request, CompanyOpeningMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(CompanySetupGroupOperationV1.OpenCompany, null, cancellationToken);
        if (!Guid.TryParse(metadata.ActorId, out var actor) || actor != scope.ActorUserId)
            throw new UnauthorizedAccessException();
        if (request.CompanyId == Guid.Empty || request.ExpectedVersion != "0" || string.IsNullOrWhiteSpace(request.Code)
            || string.IsNullOrWhiteSpace(request.LegalName) || string.IsNullOrWhiteSpace(request.Vkn)
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500 || request.Reason.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 128
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || metadata.CorrelationId.Length > 128
            || string.IsNullOrWhiteSpace(metadata.SourceChannel) || metadata.CausationId?.Length > 128)
            throw new ArgumentException("Company opening request or metadata is invalid.");
        // Validate existing KIM-D28/KIM-D40 domain rules; never enable invoice issuing during setup.
        var now = clock.GetUtcNow().ToUniversalTime();
        var proposed = Company.OpenPending(request.CompanyId, request.Code, request.LegalName, request.Vkn, actor, now);
        if (!OrganizationCompanyTaxIdRules.HasValidCheckDigit(proposed.Vkn))
            throw new ArgumentException("Company tax identity check digit is invalid.");
        var keyHash = Hash(metadata.IdempotencyKey);
        var requestHash = Hash(JsonSerializer.Serialize(request, Json));
        if (companyContext.IsResolved && companyContext.CompanyId != request.CompanyId) throw new UnauthorizedAccessException();
        companyContext.Set(request.CompanyId);
        IDisposable? openingProof = null;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await StillAsync(scope, request.CompanyId, cancellationToken);
            var previous = await FindAsync(keyHash, cancellationToken);
            await StillAsync(scope, request.CompanyId, cancellationToken);
            if (previous is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await StillAsync(scope, request.CompanyId, cancellationToken);
                return await CompleteReplayAsync(previous, request, requestHash, metadata, actor, scope, cancellationToken);
            }
            CompanyOpeningAnswerV1 answer;
            if (await database.Companies.AnyAsync(company => company.Id == request.CompanyId, cancellationToken))
                answer = new CompanyOpeningAnswerV1(409, "organization.company.already_exists", null);
            else if (await database.Companies.AnyAsync(company => company.Code == proposed.Code, cancellationToken))
                answer = new CompanyOpeningAnswerV1(409, "organization.company.code_in_use", null);
            else if (await database.Companies.AnyAsync(company => company.Vkn == proposed.Vkn, cancellationToken))
                answer = new CompanyOpeningAnswerV1(409, "organization.company.tax_id_in_use", null);
            else
                answer = new CompanyOpeningAnswerV1(201, null, new CompanySetupCompanyV1(proposed.Id, proposed.Code, proposed.LegalName, proposed.LifecycleState, proposed.Version));
            await StillAsync(scope, request.CompanyId, cancellationToken);
            openingProof = database.BeginCompanyOpeningWrite(request.CompanyId, scope);
            if (answer.StatusCode == 201)
            {
                database.Companies.Add(proposed);
                var payload = new OrganizationCompanyOpenedV1(proposed.Id, actor, now);
                database.CompanySetupOutboxMessages.Add(new OrganizationCompanySetupOutboxMessage(Guid.NewGuid(), proposed.Id, JsonSerializer.Serialize(payload, Json),
                    now, metadata.CorrelationId, metadata.CausationId));
            }
            database.CompanyOpeningExecutions.Add(new CompanyOpeningExecution(keyHash, request.CompanyId, actor, requestHash, answer.StatusCode,
                JsonSerializer.Serialize(answer, Json), metadata.CorrelationId, now));
            if (answer.StatusCode == 201)
                auditEvents.Enqueue(new AuditEventDraft(request.CompanyId, "organization.company.opened.v1", "Organization.Company",
                    request.CompanyId.ToString("D"), "Create", null, 1, AuditActorTypes.User, metadata.ActorId,
                    ErpPermissions.OrganizationCompaniesOpen, [], "{}",
                    JsonSerializer.Serialize(new { companyId = proposed.Id, proposed.Code, proposed.LegalName, proposed.Vkn,
                        proposed.LifecycleState, proposed.OpenedByUserId, proposed.InvoiceCapable, proposed.IsPlaceholder, proposed.Version }, Json),
                    request.Reason.Trim(), JsonSerializer.Serialize(new { groupAuthorityRevision = scope.Revision }, Json),
                    AuditAiInvolvement.None, null, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
                    metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
            else
                refusedAttempts.Record(Refused(request.CompanyId, metadata, answer.Code!, RefusedAttemptClassesV1.BusinessRule));
            // Drain before Organization's legacy audit interceptor: the Audit owner joins this exact transaction.
            await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await StillAsync(scope, request.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CompanyOpeningResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            // The await-using transaction is disposed/rolled back before a winner can be disclosed.
            database.ChangeTracker.Clear();
            auditWriter.DiscardPending();
            openingProof?.Dispose(); openingProof = null;
            await StillAsync(scope, request.CompanyId, cancellationToken);
            var winner = await FindAsync(keyHash, cancellationToken);
            await StillAsync(scope, request.CompanyId, cancellationToken);
            if (winner is null) throw;
            return await CompleteReplayAsync(winner, request, requestHash, metadata, actor, scope, cancellationToken);
        }
        finally { openingProof?.Dispose(); database.ChangeTracker.Clear(); auditWriter.DiscardPending(); }
    }

    private Task<CompanyOpeningExecution?> FindAsync(string keyHash, CancellationToken cancellationToken) =>
        database.CompanyOpeningExecutions.AsNoTracking().SingleOrDefaultAsync(row => row.IdempotencyKeyHash == keyHash, cancellationToken);

    private async Task<CompanyOpeningResultV1> CompleteReplayAsync(CompanyOpeningExecution previous, OpenCompanyV1 request,
        string requestHash, CompanyOpeningMetadataV1 metadata, Guid actor, CompanySetupGroupAuthoritySnapshotV1 scope,
        CancellationToken cancellationToken)
    {
        var result = Replay(previous, request, requestHash, metadata, actor);
        if (!result.IsReplay)
            await refusedAttempts.RecordStandaloneAsync(Refused(request.CompanyId, metadata, result.Answer.Code!,
                RefusedAttemptClassesV1.Idempotency), cancellationToken);
        else if (result.Answer.StatusCode == 409)
            refusedAttempts.NoteReplayedRefusal(RefusedAttemptClassesV1.BusinessRule);
        await StillAsync(scope, request.CompanyId, cancellationToken);
        return result;
    }
    private static RefusedAttemptV1 Refused(Guid companyId, CompanyOpeningMetadataV1 metadata, string code, string refusalClass) => new RefusedAttemptV1()
    {
        CompanyId = companyId, CommandName = CommandName, Operation = "Create", TargetType = "Organization.Company",
        TargetId = companyId.ToString("D"), ExpectedVersion = 0, ActorType = AuditActorTypes.User, ActorId = metadata.ActorId,
        PermissionCode = ErpPermissions.OrganizationCompaniesOpen, RoleCodes = [], HttpStatus = 409, ProblemCode = code,
        RefusalClass = refusalClass, CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId,
        SourceChannel = metadata.SourceChannel, IdempotencyKey = metadata.IdempotencyKey,
    };

    private static CompanyOpeningResultV1 Replay(CompanyOpeningExecution previous, OpenCompanyV1 request,
        string requestHash, CompanyOpeningMetadataV1 metadata, Guid actor)
    {
        if (previous.ActorUserId != actor || previous.CompanyId != request.CompanyId || previous.RequestHash != requestHash)
            return new CompanyOpeningResultV1(new CompanyOpeningAnswerV1(409, "organization.company.idempotency_conflict", null), false, metadata.CorrelationId);
        var answer = JsonSerializer.Deserialize<CompanyOpeningAnswerV1>(previous.ResponseJson, Json)
            ?? throw new InvalidOperationException("Company-opening terminal is unreadable.");
        if (answer.StatusCode != previous.StatusCode || answer.StatusCode is not (201 or 409)
            || answer.StatusCode == 201 && (answer.Code is not null || answer.Company is not { LifecycleState: "SetupPending", Version: 1 } company
                || company.CompanyId != request.CompanyId)
            || answer.StatusCode == 409 && (answer.Company is not null || answer.Code is not
                ("organization.company.already_exists" or "organization.company.code_in_use" or "organization.company.tax_id_in_use")))
            throw new InvalidOperationException("Company-opening terminal is inconsistent.");
        return new CompanyOpeningResultV1(answer, true, previous.OriginalCorrelationId);
    }
    private Guid Actor() => currentUser.IsAuthenticated && Guid.TryParse(currentUser.UserId, out var actor) && actor != Guid.Empty
        ? actor : throw new UnauthorizedAccessException();
    private async Task<CompanySetupGroupAuthoritySnapshotV1> RequireAsync(CompanySetupGroupOperationV1 operation,
        Guid? companyId, CancellationToken cancellationToken)
    {
        var actor = Actor();
        if (companyId.HasValue && (!companyContext.IsResolved || companyContext.CompanyId != companyId)) throw new UnauthorizedAccessException();
        var scope = await authority.ReadAsync(operation, cancellationToken);
        if (!scope.IsGranted || scope.Operation != operation || scope.ActorUserId != actor || string.IsNullOrWhiteSpace(scope.Revision)
            || Actor() != actor || companyId.HasValue && (!companyContext.IsResolved || companyContext.CompanyId != companyId))
            throw new UnauthorizedAccessException();
        return scope;
    }
    private async Task StillAsync(CompanySetupGroupAuthoritySnapshotV1 original, Guid? companyId, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(original.Operation, companyId, cancellationToken);
        if (current.ActorUserId != original.ActorUserId || current.Revision != original.Revision) throw new UnauthorizedAccessException();
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
