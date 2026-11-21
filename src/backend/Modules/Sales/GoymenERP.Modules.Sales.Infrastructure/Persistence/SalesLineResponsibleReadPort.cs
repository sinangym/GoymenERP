using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>Read-only Sales owner seam for existing per-line notifications; no assignment, permission or audit mutation.</summary>
internal sealed class SalesLineResponsibleReadPort(SalesDbContext database, ICompanyContext company) : ISalesLineResponsibleReadPortV1
{
    public async Task<SalesLineResponsibleAnswerV1> ReadAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        SalesLineResponsibleAnswerV1 Answer(SalesLineResponsibleOutcomeV1 outcome, Guid? user = null, long? version = null) =>
            new SalesLineResponsibleAnswerV1(outcome, companyId, orderId, lineId, user, version);
        if (companyId == Guid.Empty) return Answer(SalesLineResponsibleOutcomeV1.Unavailable);
        if (orderId == Guid.Empty || lineId == Guid.Empty) return Answer(SalesLineResponsibleOutcomeV1.NotFound);
        try
        {
            var found = await (from header in database.Drafts.AsNoTracking()
                join line in database.DraftLines.AsNoTracking()
                    on new { header.CompanyId, Id = header.Id } equals new { line.CompanyId, Id = line.DraftId }
                where header.CompanyId == companyId && header.Id == orderId && line.LineId == lineId
                select new { header.CompanyId, header.Version, header.SalesOwnerUserId }).SingleOrDefaultAsync(cancellationToken);
            if (!company.IsResolved || company.CompanyId != companyId) return Answer(SalesLineResponsibleOutcomeV1.Unavailable);
            if (found is null) return Answer(SalesLineResponsibleOutcomeV1.NotFound);
            if (found.CompanyId != companyId || found.Version < 1 || found.SalesOwnerUserId == Guid.Empty)
                return Answer(SalesLineResponsibleOutcomeV1.Unavailable);
            return found.SalesOwnerUserId is { } user
                ? Answer(SalesLineResponsibleOutcomeV1.Found, user, found.Version)
                : Answer(SalesLineResponsibleOutcomeV1.Unassigned, version: found.Version);
        }
        // K3 review pending: DbException is only an error category; this owner opens no SQL connection and emits no SQL text.
        catch (DbException) { return Answer(SalesLineResponsibleOutcomeV1.Unavailable); }
        catch (InvalidOperationException) { return Answer(SalesLineResponsibleOutcomeV1.Unavailable); }
    }
}
