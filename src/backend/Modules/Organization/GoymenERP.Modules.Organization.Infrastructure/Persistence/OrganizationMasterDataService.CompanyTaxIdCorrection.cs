using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// KIM-D41 + KIM-D48: staged VKN corrections. A data fix never rewrites a company's VKN: it stages the old and the new
/// value with its source (for example the migration that found the fiscal seed's stand-in on the placeholder company),
/// and a person holding the existing approve permission applies the row. The apply command follows the tax-id flow's
/// protocol (fresh authority, key looked up first, every terminal answer stored with the key, the company's expected
/// version) and writes exactly one audit event per applied row in the same transaction: the actor is the SYSTEM actor
/// named by the correction's source (KIM-D41), the before/after JSON carry the old value, the new value and the source,
/// and the approval context carries the approver (KIM-D48). No new permission name is opened; nobody is granted here.
/// </summary>
internal sealed partial class OrganizationMasterDataService
{
    internal const string ApplyCompanyTaxIdCorrectionCommandName = "organization.company.tax-id.correction-apply.v1";
    internal const string CompanyTaxIdCorrectionAppliedEventType = "organization.company.tax-id.correction-applied.v1";
    private const string CompanyTaxIdCorrectionApplyOperation = "ApplyCorrection";

    public async Task<CompanyTaxIdCorrectionsView?> GetCompanyTaxIdCorrectionsAsync(CancellationToken cancellationToken)
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

        // The status is stored as its name, so the pending-first order is taken in memory (a company has a handful of
        // corrections at most: one pending, the applied trail).
        var corrections = await dbContext.CompanyTaxIdCorrections
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new CompanyTaxIdCorrectionsView(
            company.Id,
            company.Version,
            canApprove,
            corrections
                .OrderBy(x => x.IsPending ? 0 : 1)
                .ThenByDescending(x => x.StagedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => ToCorrectionView(x, company))
                .ToArray());
    }

    public async Task<CommandExecutionResult> ApplyCompanyTaxIdCorrectionAsync(
        Guid correctionId,
        ApplyCompanyTaxIdCorrection command,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        if (correctionId == Guid.Empty)
        {
            throw new ArgumentException("VKN düzeltmesi kimliği boş olamaz.", nameof(correctionId));
        }

        RequireExpectedVersion(command.ExpectedVersion);
        const string permission = OrganizationCompanyTaxIdPermissionsV1.Approve;
        var scope = await authorityRevalidationService.RevalidatePermissionScopeAsync(
            permission, metadata.CompanyId, cancellationToken);
        var request = CreateRequest(
            ApplyCompanyTaxIdCorrectionCommandName,
            metadata,
            new ApplyCompanyTaxIdCorrectionFingerprint(
                metadata.CompanyId,
                correctionId,
                command.ExpectedVersion));

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

        // Tenant query filter: a correction id of another company is simply not found.
        var correction = await dbContext.CompanyTaxIdCorrections
            .SingleOrDefaultAsync(x => x.Id == correctionId, cancellationToken);
        var companyId = metadata.CompanyId;
        var company = await dbContext.Companies
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);
        if (correction is null || company is null)
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.NotFound), cancellationToken);
        }

        if (!correction.IsPending)
        {
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.For(CommandResult.TaxIdCorrectionNotPending),
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

        if (!correction.IsCurrentFor(company)
            || (correction.CorrectedVkn is null && (!company.IsPlaceholder || company.InvoiceCapable)))
        {
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.For(CommandResult.TaxIdCorrectionStale),
                cancellationToken);
        }

        if (correction.CorrectedVkn is not null
            && await IsCompanyTaxIdInUseAsync(correction.CorrectedVkn, companyId, cancellationToken))
        {
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.For(CommandResult.TaxIdInUse),
                cancellationToken);
        }

        var freshScope = await ReadFreshCompanyTaxIdMutationScopeAsync(metadata, permission, cancellationToken);
        if (freshScope is null) return NotRecordedNotFound(metadata);

        var now = DateTimeOffset.UtcNow;
        var companyVersionBefore = company.Version;
        var beforeJson = SerializeCompanyTaxIdCorrection(company, companyVersionBefore, correction);
        correction.Apply(company, metadata.ActorId, now);
        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            CompanyTaxIdCorrectionAppliedEventType,
            CompanyEntityType,
            company.Id.ToString("D"),
            CompanyTaxIdCorrectionApplyOperation,
            companyVersionBefore,
            company.Version,
            AuditActorTypes.System,
            correction.Source,
            permission,
            freshScope.RoleCodes,
            beforeJson,
            SerializeCompanyTaxIdCorrection(company, company.Version, correction),
            correction.Reason,
            SerializeCompanyTaxIdCorrectionApproval(correction, permission),
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

        var expectedVersion = command.ExpectedVersion;
        var correctedVkn = correction.CorrectedVkn;
        return await CompleteMutationAsync(
            request,
            metadata,
            CommandResponses.For(CommandResult.Succeeded),
            async (exception, token) =>
            {
                var status = await dbContext.CompanyTaxIdCorrections
                    .AsNoTracking()
                    .Where(x => x.Id == correctionId)
                    .Select(x => (CompanyTaxIdCorrectionStatus?)x.Status)
                    .SingleOrDefaultAsync(token);
                if (status is null)
                {
                    return CommandResponses.For(CommandResult.NotFound);
                }

                if (status != CompanyTaxIdCorrectionStatus.Pending)
                {
                    return CommandResponses.For(CommandResult.TaxIdCorrectionNotPending);
                }

                // The unique VKN index decides a race between two companies taking the same VKN.
                if (correctedVkn is not null && await IsCompanyTaxIdInUseAsync(correctedVkn, companyId, token))
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
            company.Id,
            cancellationToken);
    }

    private static CompanyTaxIdCorrectionView ToCorrectionView(CompanyTaxIdCorrection correction, Company company) =>
        new(
            correction.Id,
            correction.Source,
            correction.StagedVkn,
            correction.CorrectedVkn,
            correction.Reason,
            ToCorrectionStatusToken(correction.Status),
            correction.StagedAtUtc,
            correction.AppliedBy,
            correction.AppliedAtUtc,
            correction.IsPending && correction.IsCurrentFor(company));

    internal static string ToCorrectionStatusToken(CompanyTaxIdCorrectionStatus status) => status switch
    {
        CompanyTaxIdCorrectionStatus.Pending => "pending",
        CompanyTaxIdCorrectionStatus.Applied => "applied",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static string SerializeCompanyTaxIdCorrection(
        Company company,
        long version,
        CompanyTaxIdCorrection correction) =>
        JsonSerializer.Serialize(new
        {
            company.Id,
            company.Code,
            company.Vkn,
            company.IsPlaceholder,
            Version = version,
            CorrectionId = correction.Id,
            CorrectionStatus = correction.Status.ToString(),
            correction.Source,
            OldVkn = correction.StagedVkn,
            NewVkn = correction.CorrectedVkn,
        });

    private static string SerializeCompanyTaxIdCorrectionApproval(CompanyTaxIdCorrection correction, string permission) =>
        JsonSerializer.Serialize(new
        {
            CorrectionId = correction.Id,
            ApprovedBy = correction.AppliedBy,
            ApprovedAtUtc = correction.AppliedAtUtc,
            Permission = permission,
            correction.Source,
            correction.StagedAtUtc,
        });

    private sealed record ApplyCompanyTaxIdCorrectionFingerprint(
        Guid CompanyId,
        Guid CorrectionId,
        long ExpectedVersion);
}
