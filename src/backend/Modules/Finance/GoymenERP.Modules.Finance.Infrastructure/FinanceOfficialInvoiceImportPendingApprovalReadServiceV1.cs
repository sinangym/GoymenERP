using System.Globalization;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed class FinanceOfficialInvoiceImportPendingApprovalReadServiceV1(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinanceOfficialInvoiceImportParserV1 parser,
    IFinanceOfficialInvoiceImportApprovalAuthorityV1 authority)
    : IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1
{
    private const int PageSize = 50;
    private const int MaximumOffset = 1_000_000;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<ReadResult<FinanceOfficialInvoiceImportPendingBatchPageV1>> ListAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCursor(context.CompanyId, cursor, out var offset))
        {
            return new(ReadOutcome.NotFound, null);
        }

        if (await FenceAsync<FinanceOfficialInvoiceImportPendingBatchPageV1>(context, Guid.Empty, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        List<FinanceOfficialInvoiceImportBatchRow> rows;
        try
        {
            rows = await dbContext.OfficialInvoiceImportBatches.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId
                    && row.UploadedBy != context.ActorId
                    && !dbContext.OfficialInvoiceImportApprovals.Any(approval => approval.BatchId == row.Id))
                .OrderByDescending(row => row.UploadedAtUtc)
                .ThenBy(row => row.Id)
                .Skip(offset)
                .Take(PageSize + 1)
                .ToListAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await FailureAfterReadAsync<FinanceOfficialInvoiceImportPendingBatchPageV1>(
                context, Guid.Empty, cancellationToken);
        }

        if (await FenceAsync<FinanceOfficialInvoiceImportPendingBatchPageV1>(context, Guid.Empty, cancellationToken)
            is { } afterRead)
        {
            return afterRead;
        }

        var visible = rows.Take(PageSize).ToArray();
        if (visible.Any(row => Reparse(context, row) is null) || !SameContext(context))
        {
            return new(ReadOutcome.Unavailable, null);
        }

        var hasMore = rows.Count > PageSize;
        return new(ReadOutcome.Found, new FinanceOfficialInvoiceImportPendingBatchPageV1(
            context.CompanyId,
            visible.Select(View).ToArray(),
            hasMore ? EncodeCursor(context.CompanyId, checked(offset + PageSize)) : null));
    }

    public async Task<ReadResult<FinanceOfficialInvoiceImportPendingBatchDetailV1>> GetAsync(
        FinanceContext context,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (batchId == Guid.Empty)
        {
            return new(ReadOutcome.NotFound, null);
        }

        if (await FenceAsync<FinanceOfficialInvoiceImportPendingBatchDetailV1>(context, batchId, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        FinanceOfficialInvoiceImportBatchRow? row;
        try
        {
            row = await dbContext.OfficialInvoiceImportBatches.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == batchId
                    && candidate.CompanyId == context.CompanyId
                    && candidate.UploadedBy != context.ActorId
                    && !dbContext.OfficialInvoiceImportApprovals.Any(approval => approval.BatchId == candidate.Id),
                    cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await FailureAfterReadAsync<FinanceOfficialInvoiceImportPendingBatchDetailV1>(
                context, batchId, cancellationToken);
        }

        if (await FenceAsync<FinanceOfficialInvoiceImportPendingBatchDetailV1>(context, batchId, cancellationToken)
            is { } afterRead)
        {
            return afterRead;
        }
        if (row is null)
        {
            return new(ReadOutcome.NotFound, null);
        }

        var preview = Reparse(context, row);
        return preview is null || !SameContext(context)
            ? new(ReadOutcome.Unavailable, null)
            : new(ReadOutcome.Found, new FinanceOfficialInvoiceImportPendingBatchDetailV1(View(row), preview));
    }

    private async Task<ReadResult<T>?> FenceAsync<T>(
        FinanceContext context,
        Guid batchId,
        CancellationToken cancellationToken) where T : class
    {
        if (!SameContext(context))
        {
            return new(ReadOutcome.Denied, null);
        }

        var decision = await authority.CheckAsync(
            context, FinanceOfficialInvoiceImportAuthorityActionV1.Approve, batchId, cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context))
        {
            return decision == PermissionDecision.Unavailable
                ? new(ReadOutcome.Unavailable, null)
                : new(ReadOutcome.Denied, null);
        }
        return null;
    }

    private async Task<ReadResult<T>> FailureAfterReadAsync<T>(
        FinanceContext context,
        Guid batchId,
        CancellationToken cancellationToken) where T : class =>
        await FenceAsync<T>(context, batchId, cancellationToken) ?? new(ReadOutcome.Unavailable, null);

    private FinanceOfficialInvoiceImportPreviewV1? Reparse(
        FinanceContext context,
        FinanceOfficialInvoiceImportBatchRow row)
    {
        try
        {
            var preview = parser.Preview(context,
                new FinanceOfficialInvoiceImportPreviewRequestV1(row.Id, row.BatchVersion, row.FileBytes));
            if (!preview.IsValid || preview.Batch is null
                || preview.Batch.BatchId != row.Id
                || preview.Batch.CompanyId != row.CompanyId
                || !string.Equals(preview.Batch.BatchVersion, row.BatchVersion, StringComparison.Ordinal)
                || !string.Equals(preview.Batch.BatchChecksum, row.BatchChecksum, StringComparison.Ordinal)
                || !string.Equals(preview.SourceSha256, row.BatchChecksum, StringComparison.Ordinal)
                || !string.Equals(preview.TemplateVersion, row.TemplateVersion, StringComparison.Ordinal)
                || preview.Batch.Rows.Count != row.RowCount
                || !string.Equals(JsonSerializer.Serialize(preview.Batch.Rows, Json), row.SourceRowsJson,
                    StringComparison.Ordinal))
            {
                return null;
            }
            return preview;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool SameContext(FinanceContext context) =>
        companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && context.CompanyId != Guid.Empty
        && currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    private static FinanceOfficialInvoiceImportBatchViewV1 View(FinanceOfficialInvoiceImportBatchRow row) => new(
        row.Id, row.CompanyId, row.BatchVersion, row.BatchChecksum, row.TemplateVersion,
        FinanceOfficialInvoiceImportApprovalStatusV1.Pending, "1", row.RowCount,
        row.UploadedBy, row.UploadedAtUtc, null, null);

    private static string EncodeCursor(Guid companyId, int offset)
    {
        var bytes = Encoding.UTF8.GetBytes($"{companyId:N}:{offset.ToString(CultureInfo.InvariantCulture)}");
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryCursor(Guid companyId, string? cursor, out int offset)
    {
        offset = 0;
        if (cursor is null)
        {
            return true;
        }
        if (cursor.Length is 0 or > 128)
        {
            return false;
        }

        try
        {
            var value = cursor.Replace('-', '+').Replace('_', '/');
            value = value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            var separator = decoded.IndexOf(':');
            if (separator != 32
                || !Guid.TryParseExact(decoded[..separator], "N", out var cursorCompany)
                || cursorCompany != companyId
                || !int.TryParse(decoded[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out offset)
                || offset < 0 || offset > MaximumOffset)
            {
                return false;
            }
            return string.Equals(cursor, EncodeCursor(companyId, offset), StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
