using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Search;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Search;

/// <summary>
/// AT-002 / ERP-000I: the Sales-owned global search source — the context company's sales orders whose document number
/// contains the term (the number is the only searchable text Sales itself holds; the customer's name is the Cari owner's).
/// <list type="bullet">
/// <item><b>Authority</b>: the Sales draft read (<see cref="SalesDraftServiceBindings.ReadPermission"/>, the same name the
/// order list measures) is measured afresh, company-wide, before the read and again before the answer is released; not held
/// is <see cref="GlobalSearchSourceAnswerV1.NotAuthorized"/> — no hit, no count, no "more". An unregistered or unbound name
/// is a setup gap and throws (the composer's 503), never a grant.</item>
/// <item><b>Company fence</b>: Sales's own tenant-filtered rows, and every hit carries its company for the composer to
/// check; another company's order is never read.</item>
/// <item><b>Price-free</b>: document number, state and business date only — no line, price, discount or amount.</item>
/// <item>Reading creates nothing: untracked, no attempt, audit or version effect.</item>
/// </list>
/// Newest order first (created time, then id), at most the request's limit, with "more" when another row matched.
/// </summary>
internal sealed class SalesOrderGlobalSearchSource(
    SalesDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    SalesDraftServiceBindings bindings) : IGlobalSearchSourceV1
{
    public string Kind => GlobalSearchKindsV1.SalesOrder;

    public async Task<GlobalSearchSourceAnswerV1> SearchAsync(GlobalSearchRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Term) || request.Limit < 1)
        {
            throw new ArgumentException("Arama isteği geçersiz.", nameof(request));
        }

        var permission = bindings.ReadPermission;
        if (string.IsNullOrWhiteSpace(permission) || !ErpPermissions.IsKnown(permission))
        {
            throw new InvalidOperationException("Satış okuma yetkisi bağlı değil.");
        }

        if (!await HoldsAsync(permission, cancellationToken))
        {
            return GlobalSearchSourceAnswerV1.NotAuthorized;
        }

        var companyId = companyContext.CompanyId;
        var rows = await Query(dbContext, Normalize(request.Term)).Take(request.Limit + 1).ToListAsync(cancellationToken);

        // Released only under the authority that still holds after the read (the owner reads' fence).
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HoldsAsync(permission, cancellationToken) || companyContext.CompanyId != companyId)
        {
            return GlobalSearchSourceAnswerV1.NotAuthorized;
        }

        var hits = new List<GlobalSearchHitV1>(Math.Min(rows.Count, request.Limit));
        foreach (var row in rows.Take(request.Limit))
        {
            if (row.CompanyId != companyId || string.IsNullOrWhiteSpace(row.DocumentNo))
            {
                // Fail closed: a row the tenant filter should never have answered is not guessed onto the wire.
                throw new InvalidOperationException("Kayıtlı satış siparişi okunamadı.");
            }

            hits.Add(new GlobalSearchHitV1(row.Id, row.DocumentNo, null, null, StateName(row.State, row.ApprovalState), row.BusinessDate,
                row.CompanyId));
        }

        return GlobalSearchSourceAnswerV1.Answered(hits, rows.Count > request.Limit);
    }

    /// <summary>
    /// The read, newest first. Document numbers are invariant upper-case ASCII (<c>SO-yyyy-nnnnnn</c>), so the term is compared
    /// upper-cased; SQL Server's LIKE is escaped by the provider for a parameterized <c>Contains</c>.
    /// </summary>
    internal static IQueryable<SalesOrderSearchRow> Query(SalesDbContext context, string normalizedTerm) =>
        context.Drafts.AsNoTracking()
            .Where(draft => draft.DocumentNo.Contains(normalizedTerm))
            .OrderByDescending(draft => draft.CreatedAtUtc)
            .ThenByDescending(draft => draft.Id)
            .Select(draft => new SalesOrderSearchRow(draft.Id, draft.CompanyId, draft.DocumentNo, draft.State, draft.ApprovalState,
                draft.BusinessDate));

    internal static string Normalize(string term) => term.Trim().ToUpperInvariant();

    /// <summary>The lifecycle wins when terminal (DEC-SAL-005 Cancelled); otherwise the internal approval state's name.</summary>
    internal static string StateName(int state, int approvalState)
    {
        if (state == (int)SalesDraftState.Cancelled)
        {
            return nameof(SalesDraftState.Cancelled);
        }

        if (state != (int)SalesDraftState.Draft || !Enum.IsDefined(typeof(InternalApprovalState), approvalState))
        {
            throw new InvalidOperationException("Kayıtlı satış siparişi durumu okunamadı.");
        }

        return ((InternalApprovalState)approvalState).ToString();
    }

    private async Task<bool> HoldsAsync(string permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId) || !companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return false;
        }

        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyContext.CompanyId, cancellationToken);
        return scope.IsGranted && scope.IsCompanyWide;
    }
}

internal sealed record SalesOrderSearchRow(Guid Id, Guid CompanyId, string DocumentNo, int State, int ApprovalState, DateOnly BusinessDate);
