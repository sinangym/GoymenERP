using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// KIM-S23 (KIM-D28, DEC-ORG-005): the active company's VKN and its maker-checker change flow. A preparer opens a
/// change request (the company's VKN does not move), another person approves or rejects it; only the approval writes
/// the VKN, in the same transaction that closes the request. Both commands follow the master edit's protocol (fresh
/// authority, key looked up first, every terminal answer stored with the key, one audit event per applied command in
/// the same transaction). Both permissions are company-wide only; the names are registered, nobody is granted them
/// here (grant/role is a human gate).
/// </summary>
internal sealed partial class OrganizationMasterDataService
{
    internal const string RequestCompanyTaxIdChangeCommandName = "organization.company.tax-id.change-request.v1";
    internal const string DecideCompanyTaxIdChangeCommandName = "organization.company.tax-id.change-decision.v1";
    internal const string CompanyTaxIdChangeRequestedEventType = "organization.company.tax-id.change-requested.v1";
    internal const string CompanyTaxIdChangeApprovedEventType = "organization.company.tax-id.change-approved.v1";
    internal const string CompanyTaxIdChangeRejectedEventType = "organization.company.tax-id.change-rejected.v1";
    private const string CompanyEntityType = "Organization.Company";
    private const string CompanyTaxIdChangeRequestEntityType = "Organization.CompanyTaxIdChangeRequest";

    public async Task<CompanyTaxIdView?> GetCompanyTaxIdAsync(CancellationToken cancellationToken)
    {
        if (!companyContext.IsResolved)
        {
            return null;
        }

        var canRequest = IsCompanyWideGrant(
            await ReadScopeAsync(OrganizationCompanyTaxIdPermissionsV1.Request, cancellationToken));
        var canApprove = IsCompanyWideGrant(
            await ReadScopeAsync(OrganizationCompanyTaxIdPermissionsV1.Approve, cancellationToken));
        if (!canRequest && !canApprove)
        {
            // The VKN is not part of the plain organization read: without either tax-id permission the screen is closed.
            return null;
        }

        var companyId = companyContext.CompanyId;
        var company = await dbContext.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);
        if (company is null)
        {
            return null;
        }

        var pending = await dbContext.CompanyTaxIdChangeRequests
            .AsNoTracking()
            .Where(x => x.Status == CompanyTaxIdChangeStatus.Pending)
            .SingleOrDefaultAsync(cancellationToken);
        var lastDecided = await dbContext.CompanyTaxIdChangeRequests
            .AsNoTracking()
            .Where(x => x.Status != CompanyTaxIdChangeStatus.Pending)
            .OrderByDescending(x => x.DecidedAtUtc)
            .ThenByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var actorId = currentUser.UserId;

        return new CompanyTaxIdView(
            company.Id,
            company.Code,
            company.LegalName,
            company.Vkn,
            company.IsPlaceholder,
            company.Version,
            pending is null ? null : ToChangeView(pending),
            lastDecided is null ? null : ToChangeView(lastDecided),
            new CompanyTaxIdViewer(
                canRequest,
                canApprove,
                pending is not null && actorId is not null && pending.IsRequestedBy(actorId)));
    }

    public async Task<CommandExecutionResult> RequestCompanyTaxIdChangeAsync(
        RequestCompanyTaxIdChange command,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        if (!OrganizationCompanyTaxIdRules.IsValidVkn(command.ProposedVkn))
        {
            throw new ArgumentException("Önerilen VKN tam olarak 10 ASCII rakamdan oluşmalıdır.", nameof(command));
        }

        RequireExpectedVersion(command.ExpectedVersion);
        var reason = RequireReason(command.Reason);
        var proposedVkn = command.ProposedVkn.Trim();
        const string permission = OrganizationCompanyTaxIdPermissionsV1.Request;
        var scope = await authorityRevalidationService.RevalidatePermissionScopeAsync(
            permission, metadata.CompanyId, cancellationToken);
        var request = CreateRequest(
            RequestCompanyTaxIdChangeCommandName,
            metadata,
            new RequestCompanyTaxIdChangeFingerprint(
                metadata.CompanyId,
                proposedVkn,
                command.ExpectedVersion,
                reason));

        var existing = await FindCommandExecutionAsync(request, cancellationToken);
        if (existing is not null)
        {
            return IsCompanyWideGrant(scope)
                ? ResolveExistingExecution(existing, request, metadata)
                : NotRecordedNotFound(metadata);
        }

        if (!IsCompanyWideGrant(scope))
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.NotFound), cancellationToken);
        }

        var companyId = metadata.CompanyId;
        var company = await dbContext.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);
        if (company is null)
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.NotFound), cancellationToken);
        }

        if (!OrganizationCompanyTaxIdRules.HasValidCheckDigit(proposedVkn))
        {
            // KIM-D40: a well-formed VKN that fails the official last-digit check is refused at entry, before any
            // request exists to approve; the refusal is recorded with the key like every other terminal answer.
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.For(CommandResult.TaxIdCheckDigitInvalid),
                cancellationToken);
        }

        if (company.Version != command.ExpectedVersion)
        {
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.VersionConflict(command.ExpectedVersion, company.Version),
                cancellationToken);
        }

        if (await HasPendingCompanyTaxIdChangeAsync(cancellationToken))
        {
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.For(CommandResult.TaxIdChangePending),
                cancellationToken);
        }

        if (string.Equals(company.Vkn, proposedVkn, StringComparison.Ordinal))
        {
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.For(CommandResult.TaxIdUnchanged),
                cancellationToken);
        }

        if (await IsCompanyTaxIdInUseAsync(proposedVkn, companyId, cancellationToken))
        {
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.For(CommandResult.TaxIdInUse),
                cancellationToken);
        }

        var freshScope = await ReadFreshCompanyTaxIdMutationScopeAsync(metadata, permission, cancellationToken);
        if (freshScope is null) return NotRecordedNotFound(metadata);

        var changeRequest = new CompanyTaxIdChangeRequest(
            Guid.NewGuid(),
            companyId,
            company.Vkn,
            proposedVkn,
            company.Version,
            reason,
            metadata.ActorId,
            DateTimeOffset.UtcNow);
        dbContext.CompanyTaxIdChangeRequests.Add(changeRequest);
        EnqueueAudit(
            metadata,
            freshScope,
            permission,
            CompanyTaxIdChangeRequestedEventType,
            CompanyTaxIdChangeRequestEntityType,
            changeRequest.Id,
            "Create",
            versionBefore: null,
            changeRequest.Version,
            beforeJson: "{}",
            SerializeCompanyTaxIdChangeRequest(changeRequest),
            reason);

        var expectedVersion = command.ExpectedVersion;
        return await CompleteMutationAsync(
            request,
            metadata,
            CommandResponses.Created(changeRequest.Id, changeRequest.Version),
            async (_, token) =>
            {
                // The only races a request can lose: the company moved, or another request became pending first (the
                // filtered unique index allows one pending request per company).
                var currentVersion = await ReadCompanyVersionAsync(companyId, token);
                if (currentVersion is null)
                {
                    return CommandResponses.For(CommandResult.NotFound);
                }

                if (currentVersion.Value != expectedVersion)
                {
                    return CommandResponses.VersionConflict(expectedVersion, currentVersion.Value);
                }

                return await HasPendingCompanyTaxIdChangeAsync(token)
                    ? CommandResponses.For(CommandResult.TaxIdChangePending)
                    : null;
            },
            changeRequest.Id,
            cancellationToken);
    }

    public async Task<CommandExecutionResult> DecideCompanyTaxIdChangeAsync(
        Guid requestId,
        DecideCompanyTaxIdChange command,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("VKN değişiklik talebi kimliği boş olamaz.", nameof(requestId));
        }

        if (!OrganizationCompanyTaxIdRules.IsDecision(command.Decision))
        {
            throw new ArgumentException("Karar 'approve' veya 'reject' olmalıdır.", nameof(command));
        }

        if (!OrganizationCompanyTaxIdRules.IsValidNote(command.Note))
        {
            throw new ArgumentException("Karar notu tanımlı sınırlar içinde olmalıdır.", nameof(command));
        }

        RequireExpectedVersion(command.ExpectedVersion);
        var approve = string.Equals(
            command.Decision,
            OrganizationCompanyTaxIdRules.ApproveDecision,
            StringComparison.Ordinal);
        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        const string permission = OrganizationCompanyTaxIdPermissionsV1.Approve;
        var scope = await authorityRevalidationService.RevalidatePermissionScopeAsync(
            permission, metadata.CompanyId, cancellationToken);
        var request = CreateRequest(
            DecideCompanyTaxIdChangeCommandName,
            metadata,
            new DecideCompanyTaxIdChangeFingerprint(
                metadata.CompanyId,
                requestId,
                command.Decision,
                command.ExpectedVersion,
                note));

        var existing = await FindCommandExecutionAsync(request, cancellationToken);
        if (existing is not null)
        {
            return IsCompanyWideGrant(scope)
                ? ResolveExistingExecution(existing, request, metadata)
                : NotRecordedNotFound(metadata);
        }

        if (!IsCompanyWideGrant(scope))
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.NotFound), cancellationToken);
        }

        // Tenant query filter: a request id of another company is simply not found.
        var changeRequest = await dbContext.CompanyTaxIdChangeRequests
            .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);
        var companyId = metadata.CompanyId;
        var company = await dbContext.Companies
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);
        if (changeRequest is null || company is null)
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.NotFound), cancellationToken);
        }

        if (!changeRequest.IsPending)
        {
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.For(CommandResult.TaxIdChangeNotPending),
                cancellationToken);
        }

        if (changeRequest.IsRequestedBy(metadata.ActorId))
        {
            // Hazırlayan ≠ onaylayan: the preparer can neither approve nor reject their own request (KIM-D28).
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.For(CommandResult.TaxIdSelfDecision),
                cancellationToken);
        }

        if (company.Version != command.ExpectedVersion)
        {
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.VersionConflict(command.ExpectedVersion, company.Version),
                cancellationToken);
        }

        if (approve)
        {
            if (!changeRequest.IsCurrentFor(company))
            {
                return await CompleteAsync(
                    request,
                    metadata,
                    CommandResponses.For(CommandResult.TaxIdChangeStale),
                    cancellationToken);
            }

            if (await IsCompanyTaxIdInUseAsync(changeRequest.ProposedVkn, companyId, cancellationToken))
            {
                return await CompleteAsync(
                    request,
                    metadata,
                    CommandResponses.For(CommandResult.TaxIdInUse),
                    cancellationToken);
            }
        }

        var freshScope = await ReadFreshCompanyTaxIdMutationScopeAsync(metadata, permission, cancellationToken);
        if (freshScope is null) return NotRecordedNotFound(metadata);

        var now = DateTimeOffset.UtcNow;
        Guid auditEntityId;
        if (approve)
        {
            var companyVersionBefore = company.Version;
            var companyBeforeJson = SerializeCompanyTaxId(company, companyVersionBefore, changeRequest);
            changeRequest.Approve(company, metadata.ActorId, note, now);
            EnqueueAudit(
                metadata,
                freshScope,
                permission,
                CompanyTaxIdChangeApprovedEventType,
                CompanyEntityType,
                company.Id,
                "Update",
                companyVersionBefore,
                company.Version,
                companyBeforeJson,
                SerializeCompanyTaxId(company, company.Version, changeRequest),
                changeRequest.Reason);
            auditEntityId = company.Id;
        }
        else
        {
            var requestVersionBefore = changeRequest.Version;
            var requestBeforeJson = SerializeCompanyTaxIdChangeRequest(changeRequest);
            changeRequest.Reject(metadata.ActorId, note, now);
            EnqueueAudit(
                metadata,
                freshScope,
                permission,
                CompanyTaxIdChangeRejectedEventType,
                CompanyTaxIdChangeRequestEntityType,
                changeRequest.Id,
                "Reject",
                requestVersionBefore,
                changeRequest.Version,
                requestBeforeJson,
                SerializeCompanyTaxIdChangeRequest(changeRequest),
                changeRequest.Reason);
            auditEntityId = changeRequest.Id;
        }

        var expectedVersion = command.ExpectedVersion;
        var proposedVkn = changeRequest.ProposedVkn;
        return await CompleteMutationAsync(
            request,
            metadata,
            CommandResponses.For(CommandResult.Succeeded),
            async (exception, token) =>
            {
                var status = await dbContext.CompanyTaxIdChangeRequests
                    .AsNoTracking()
                    .Where(x => x.Id == requestId)
                    .Select(x => (CompanyTaxIdChangeStatus?)x.Status)
                    .SingleOrDefaultAsync(token);
                if (status is null)
                {
                    return CommandResponses.For(CommandResult.NotFound);
                }

                if (status != CompanyTaxIdChangeStatus.Pending)
                {
                    return CommandResponses.For(CommandResult.TaxIdChangeNotPending);
                }

                // The unique VKN index decides a race between two companies approving the same VKN.
                if (approve && await IsCompanyTaxIdInUseAsync(proposedVkn, companyId, token))
                {
                    return CommandResponses.For(CommandResult.TaxIdInUse);
                }

                var currentVersion = await ReadCompanyVersionAsync(companyId, token);
                if (currentVersion is null)
                {
                    return CommandResponses.For(CommandResult.NotFound);
                }

                return exception is DbUpdateConcurrencyException || currentVersion.Value != expectedVersion
                    ? CommandResponses.VersionConflict(expectedVersion, currentVersion.Value)
                    : null;
            },
            auditEntityId,
            cancellationToken);
    }

    /// <summary>
    /// The last awaited read before staging a VKN mutation and its success audit/receipt. Losing authority leaves
    /// nothing staged; callers go directly from this fence to the single SaveChanges, with no intervening async reads.
    /// </summary>
    private async Task<PermissionScope?> ReadFreshCompanyTaxIdMutationScopeAsync(
        CommandMetadata metadata, string permission, CancellationToken cancellationToken)
    {
        bool SameActorAndCompany() => currentUser.IsAuthenticated
            && string.Equals(currentUser.UserId, metadata.ActorId, StringComparison.Ordinal)
            && companyContext.IsResolved && companyContext.CompanyId == metadata.CompanyId
            && currentUser.CanAccessCompany(metadata.CompanyId);

        if (SameActorAndCompany())
        {
            var fresh = await authorityRevalidationService.RevalidatePermissionScopeAsync(
                permission, metadata.CompanyId, cancellationToken);
            if (IsCompanyWideGrant(fresh) && SameActorAndCompany()) return fresh;
        }

        dbContext.ChangeTracker.Clear();
        return null;
    }

    private static bool IsCompanyWideGrant(PermissionScope scope) => scope.IsGranted && scope.IsCompanyWide;

    private Task<bool> HasPendingCompanyTaxIdChangeAsync(CancellationToken cancellationToken) =>
        dbContext.CompanyTaxIdChangeRequests
            .AsNoTracking()
            .AnyAsync(x => x.Status == CompanyTaxIdChangeStatus.Pending, cancellationToken);

    /// <summary>
    /// DEC-ORG-005: the VKN is unique across the group, so this deliberately looks at every company (Companies carry no
    /// tenant filter). The answer is a boolean and names no company.
    /// </summary>
    private Task<bool> IsCompanyTaxIdInUseAsync(string vkn, Guid exceptCompanyId, CancellationToken cancellationToken) =>
        dbContext.Companies
            .AsNoTracking()
            .AnyAsync(x => x.Vkn == vkn && x.Id != exceptCompanyId, cancellationToken);

    private Task<long?> ReadCompanyVersionAsync(Guid companyId, CancellationToken cancellationToken) =>
        dbContext.Companies
            .AsNoTracking()
            .Where(x => x.Id == companyId)
            .Select(x => (long?)x.Version)
            .SingleOrDefaultAsync(cancellationToken);

    private static CompanyTaxIdChangeView ToChangeView(CompanyTaxIdChangeRequest request) =>
        new(
            request.Id,
            request.CurrentVkn,
            request.ProposedVkn,
            request.Reason,
            ToStatusToken(request.Status),
            request.RequestedBy,
            request.RequestedAtUtc,
            request.DecidedBy,
            request.DecidedAtUtc,
            request.DecisionNote);

    internal static string ToStatusToken(CompanyTaxIdChangeStatus status) => status switch
    {
        CompanyTaxIdChangeStatus.Pending => "pending",
        CompanyTaxIdChangeStatus.Approved => "approved",
        CompanyTaxIdChangeStatus.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static string SerializeCompanyTaxId(
        Company company,
        long version,
        CompanyTaxIdChangeRequest changeRequest) =>
        JsonSerializer.Serialize(new
        {
            company.Id,
            company.Code,
            company.Vkn,
            company.IsPlaceholder,
            Version = version,
            ChangeRequestId = changeRequest.Id,
            ChangeRequestStatus = changeRequest.Status.ToString(),
            changeRequest.RequestedBy,
            changeRequest.DecidedBy,
            changeRequest.DecisionNote,
        });

    private static string SerializeCompanyTaxIdChangeRequest(CompanyTaxIdChangeRequest changeRequest) =>
        JsonSerializer.Serialize(new
        {
            changeRequest.Id,
            changeRequest.CompanyId,
            changeRequest.CurrentVkn,
            changeRequest.ProposedVkn,
            changeRequest.CompanyVersionAtRequest,
            Status = changeRequest.Status.ToString(),
            changeRequest.RequestedBy,
            changeRequest.RequestedAtUtc,
            changeRequest.DecidedBy,
            changeRequest.DecidedAtUtc,
            changeRequest.DecisionNote,
            changeRequest.Version,
        });

    private sealed record RequestCompanyTaxIdChangeFingerprint(
        Guid CompanyId,
        string ProposedVkn,
        long ExpectedVersion,
        string Reason);

    private sealed record DecideCompanyTaxIdChangeFingerprint(
        Guid CompanyId,
        Guid RequestId,
        string Decision,
        long ExpectedVersion,
        string? Note);
}
