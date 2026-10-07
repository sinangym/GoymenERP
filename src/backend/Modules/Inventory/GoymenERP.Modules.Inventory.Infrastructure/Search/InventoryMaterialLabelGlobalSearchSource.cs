using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Search;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Search;

/// <summary>
/// Inventory's AT-002 global-search answer. A label is found only by <c>Label.Code == term</c> in the
/// server-resolved company. Both existing Inventory reads are measured afresh before the read and again before
/// release; a refusal exposes no group, hit, count or existence signal.
/// </summary>
internal sealed class InventoryMaterialLabelGlobalSearchSource(
    IInventoryMaterialLabelSearchReaderV1 reader,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority) : IGlobalSearchSourceV1
{
    private static readonly string[] Permissions =
    [
        ErpPermissions.InventoryLabelsRead,
        ErpPermissions.InventoryBalancesRead,
    ];

    public string Kind => GlobalSearchKindsV1.InventoryMaterialLabel;

    public async Task<GlobalSearchSourceAnswerV1> SearchAsync(
        GlobalSearchRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Term) || request.Limit < 1)
        {
            throw new ArgumentException("Arama isteği geçersiz.", nameof(request));
        }

        var scope = CaptureScope();
        if (scope is null || !await HoldsBothAsync(scope, cancellationToken))
        {
            return GlobalSearchSourceAnswerV1.NotAuthorized;
        }

        InventoryMaterialLabelSearchRecordV1? record;
        try
        {
            record = await reader.ReadExactAsync(scope.CompanyId, request.Term, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // An owner outage may be released as 503 only while the same actor still holds both reads. If authority
            // disappeared during the failing read, the non-disclosing answer wins.
            if (!SameIdentity(scope) || !await HoldsBothAsync(scope, cancellationToken))
            {
                return GlobalSearchSourceAnswerV1.NotAuthorized;
            }

            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!SameIdentity(scope) || !await HoldsBothAsync(scope, cancellationToken))
        {
            return GlobalSearchSourceAnswerV1.NotAuthorized;
        }

        if (record is null)
        {
            return GlobalSearchSourceAnswerV1.Answered([], false);
        }

        if (record.Id == Guid.Empty || record.CompanyId != scope.CompanyId
            || !string.Equals(record.Code, request.Term, StringComparison.Ordinal)
            || !Enum.TryParse<LabelDocumentStatus>(record.DocumentStatus, out var status) || !Enum.IsDefined(status))
        {
            throw new InvalidOperationException("Kayıtlı stok etiketi okunamadı.");
        }

        return GlobalSearchSourceAnswerV1.Answered(
            [new GlobalSearchHitV1(record.Id, record.Code, null, null, record.DocumentStatus, null, record.CompanyId)],
            false);
    }

    private Scope? CaptureScope()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return null;
        }

        return new Scope(companyContext.CompanyId, currentUser.UserId);
    }

    private async Task<bool> HoldsBothAsync(Scope scope, CancellationToken cancellationToken)
    {
        foreach (var permission in Permissions)
        {
            if (!SameIdentity(scope))
            {
                return false;
            }

            var grant = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!SameIdentity(scope) || !grant.IsGranted || !grant.IsCompanyWide)
            {
                return false;
            }
        }

        return true;
    }

    private bool SameIdentity(Scope scope) =>
        companyContext.IsResolved && companyContext.CompanyId == scope.CompanyId
        && currentUser.IsAuthenticated && currentUser.UserId == scope.ActorId
        && currentUser.CanAccessCompany(scope.CompanyId);

    private sealed record Scope(Guid CompanyId, string ActorId);
}

internal interface IInventoryMaterialLabelSearchReaderV1
{
    Task<InventoryMaterialLabelSearchRecordV1?> ReadExactAsync(
        Guid companyId,
        string code,
        CancellationToken cancellationToken = default);
}

internal sealed record InventoryMaterialLabelSearchRecordV1(
    Guid Id,
    Guid CompanyId,
    string Code,
    string DocumentStatus);

/// <summary>Tenant-filtered exact-code reader. It writes nothing and never performs fuzzy or normalized matching.</summary>
internal sealed class SqlInventoryMaterialLabelSearchReaderV1(InventoryDbContext db) : IInventoryMaterialLabelSearchReaderV1
{
    private const string ExactCodeCollation = "Latin1_General_100_BIN2";

    public async Task<InventoryMaterialLabelSearchRecordV1?> ReadExactAsync(
        Guid companyId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Labels.AsNoTracking()
            .SingleOrDefaultAsync(
                label => label.CompanyId == companyId
                    && EF.Functions.Collate(label.Code, ExactCodeCollation) == code,
                cancellationToken);
        if (row is null)
        {
            return null;
        }

        var documents = await db.LabelDocuments.AsNoTracking()
            .Where(document => document.CompanyId == companyId && document.LabelId == row.Id)
            .OrderBy(document => document.Sequence)
            .ToListAsync(cancellationToken);
        var finalVersion = await db.Labels.AsNoTracking()
            .Where(label => label.CompanyId == companyId && label.Id == row.Id)
            .Select(label => (long?)label.Version)
            .SingleOrDefaultAsync(cancellationToken);
        if (finalVersion != row.Version)
        {
            throw new InvalidOperationException("Stok etiketi arama sırasında değişti.");
        }

        try
        {
            var label = MaterialLabel.Restore(
                row.Id,
                row.CompanyId,
                row.Code,
                row.ProductId,
                row.LotId,
                row.ProductVersion,
                JsonSerializer.Deserialize<string[]>(row.RequiredDocumentsJson)
                    ?? throw new InvalidOperationException("Kayıtlı etiket belge gereksinimi okunamadı."),
                row.HeatNumber,
                row.CoilNumber,
                [],
                documents.Select(document => new LabelDocumentAttachment(
                    document.Kind,
                    document.AttachedAtUtc,
                    document.AttachedBy)),
                row.Classification is null ? null : (LabelClassification)row.Classification.Value);
            return new InventoryMaterialLabelSearchRecordV1(label.Id, label.CompanyId, label.Code, label.DocumentStatus.ToString());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException("Kayıtlı stok etiketi okunamadı.", exception);
        }
    }
}
