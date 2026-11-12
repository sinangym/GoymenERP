using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed record FinanceOfficialSalesInvoiceAuditEnvelopesV1(
    FinanceAuditEnvelope Record,
    FinanceAuditEnvelope Approve);

/// <summary>
/// F-005 D1 (FNS-D17…FNS-D21, FNS-D26): the manual official sales invoice record. One actor records every mandatory field
/// with a clean PDF from the Documents vault; a different actor approves it (maker≠approver, also enforced by the
/// database check). Only an approved record is a fact other modules may read (D3). There is no edit, cancel or
/// withdraw command (FNS-D20).
/// </summary>
public sealed class FinanceOfficialSalesInvoiceServiceV1(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinanceOfficialSalesInvoiceAuthorityV1 authority,
    IFinanceOfficialSalesInvoiceDocumentPortV1 documents,
    IAuditEventCollector auditEvents,
    FinanceOfficialSalesInvoiceAuditEnvelopesV1 envelopes,
    TimeProvider timeProvider)
    : IFinanceOfficialSalesInvoiceServiceV1
{
    public const string RecordCommand = "finance.official-sales-invoices.record";
    public const string ApproveCommand = "finance.official-sales-invoices.approve";
    private const string EntityType = "Finance.OfficialSalesInvoice";
    private const int IdentityMaximumLength = 128;
    private const int PageSize = 50;
    private const int MaximumOffset = 1_000_000;
    private const long PendingVersion = 1;
    private const long ApprovedVersion = 2;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<FinanceOfficialSalesInvoiceResultV1> RecordAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOfficialSalesInvoiceRecordRequestV1 request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var action = FinanceOfficialSalesInvoiceAuthorityActionV1.Record;
        if (await FenceAsync(context, action, request.InvoiceId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!ValidIdentity(metadata.IdempotencyKey) || !ValidIdentity(context.ActorId)
            || !ValidIdentity(context.CorrelationId))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("request", "finance_value_does_not_fit"));
        }

        var issues = Validate(request, out var ettn);
        if (issues.Count > 0)
        {
            return Refused(context, CommandOutcome.ValidationFailed, [.. issues]);
        }

        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payloadHash = Hash(JsonSerializer.Serialize(request, Json));
        if (await PriorAsync(context, RecordCommand, action, request.InvoiceId, keyHash, payloadHash,
                cancellationToken) is { } prior)
        {
            return prior;
        }

        var officialNo = request.OfficialNo;
        var sameId = await dbContext.OfficialSalesInvoices.AsNoTracking()
            .AnyAsync(row => row.Id == request.InvoiceId, cancellationToken);
        var sameNo = await dbContext.OfficialSalesInvoices.AsNoTracking()
            .AnyAsync(row => row.OfficialNo == officialNo, cancellationToken);
        var sameEttn = await dbContext.OfficialSalesInvoices.AsNoTracking()
            .AnyAsync(row => row.Ettn == ettn, cancellationToken);
        if (await FenceAsync(context, action, request.InvoiceId, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }
        if (sameId)
        {
            return Refused(context, CommandOutcome.IdempotencyConflict, Issue("invoiceId", "invoice_already_exists"));
        }
        if (sameNo || sameEttn)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                [.. DuplicateIssues(sameNo, sameEttn)]);
        }

        var pdf = await documents.CheckAsync(context, request.PdfDocumentId, request.CustomerPartyId, null,
            cancellationToken);
        if (PdfRefusal(context, pdf, expectedVersionNo: null, expectedSha256: null) is { } pdfRefused)
        {
            return pdfRefused;
        }

        var now = timeProvider.GetUtcNow();
        var row = new FinanceOfficialSalesInvoiceRow
        {
            Id = request.InvoiceId,
            CompanyId = context.CompanyId,
            OfficialNo = officialNo,
            Ettn = ettn,
            CustomerPartyId = request.CustomerPartyId,
            CurrencyId = request.CurrencyId,
            PayableAmountText = request.PayableAmount,
            PayableFunctionalAmountText = request.PayableFunctionalAmount,
            TaxBaseFunctionalAmountText = request.TaxBaseFunctionalAmount,
            VatFunctionalAmountText = request.VatFunctionalAmount,
            WithholdingFunctionalAmountText = request.WithholdingFunctionalAmount,
            PdfDocumentId = request.PdfDocumentId,
            PdfDocumentVersionNo = pdf.VersionNo!.Value,
            PdfSha256 = pdf.Sha256!,
            Status = (int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval,
            Version = PendingVersion,
            RecordedBy = context.ActorId,
            RecordedAtUtc = now,
            CorrelationId = context.CorrelationId,
            CausationId = context.CausationId,
        };
        var result = Succeeded(context, View(row));
        dbContext.OfficialSalesInvoices.Add(row);
        AddCommand(context, RecordCommand, keyHash, payloadHash, row.Id, result, now);

        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } beforeSave)
        {
            dbContext.ChangeTracker.Clear();
            return beforeSave;
        }

        Audit(context, metadata, row.Id, "finance.official-sales-invoice.recorded.v1", "Record", null,
            PendingVersion, "{}", AuditJson(row), envelopes.Record,
            "Resmî satış faturası elle kaydedildi; ikinci kişinin onayını bekliyor");
        return await SaveAsync(context, RecordCommand, action, row.Id, keyHash, payloadHash, cancellationToken)
            ?? result;
    }

    public async Task<FinanceOfficialSalesInvoiceResultV1> ApproveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOfficialSalesInvoiceApproveRequestV1 request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var action = FinanceOfficialSalesInvoiceAuthorityActionV1.Approve;
        if (await FenceAsync(context, action, request.InvoiceId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!ValidIdentity(metadata.IdempotencyKey) || !ValidIdentity(request.ExpectedVersion)
            || !ValidIdentity(context.ActorId) || !ValidIdentity(context.CorrelationId))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("request", "finance_value_does_not_fit"));
        }
        if (request.InvoiceId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("invoiceId", "identity_required"));
        }

        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payloadHash = Hash(JsonSerializer.Serialize(request, Json));
        if (await PriorAsync(context, ApproveCommand, action, request.InvoiceId, keyHash, payloadHash,
                cancellationToken) is { } prior)
        {
            return prior;
        }

        var row = await dbContext.OfficialSalesInvoices
            .SingleOrDefaultAsync(candidate => candidate.Id == request.InvoiceId, cancellationToken);
        if (await FenceAsync(context, action, request.InvoiceId, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }
        if (row is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("invoiceId", "invoice_not_found"));
        }
        if (row.Status != (int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval
            || !string.Equals(request.ExpectedVersion, VersionText(row.Version), StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict"));
        }
        if (string.Equals(row.RecordedBy, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("approver", "approver_must_differ_from_recorder"));
        }

        var pdf = await documents.CheckAsync(context, row.PdfDocumentId, row.CustomerPartyId,
            row.PdfDocumentVersionNo, cancellationToken);
        if (PdfRefusal(context, pdf, row.PdfDocumentVersionNo, row.PdfSha256) is { } pdfRefused)
        {
            dbContext.ChangeTracker.Clear();
            return pdfRefused;
        }

        var before = AuditJson(row);
        var now = timeProvider.GetUtcNow();
        row.Status = (int)FinanceOfficialSalesInvoiceStatusV1.Approved;
        row.Version = ApprovedVersion;
        row.ApprovedBy = context.ActorId;
        row.ApprovedAtUtc = now;
        var result = Succeeded(context, View(row));
        AddCommand(context, ApproveCommand, keyHash, payloadHash, row.Id, result, now);

        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } beforeSave)
        {
            dbContext.ChangeTracker.Clear();
            return beforeSave;
        }

        Audit(context, metadata, row.Id, "finance.official-sales-invoice.approved.v1", "Approve", PendingVersion,
            ApprovedVersion, before, AuditJson(row), envelopes.Approve,
            "Kaydedenden farklı yetkili finans kullanıcısı resmî satış faturasını onayladı");
        return await SaveAsync(context, ApproveCommand, action, row.Id, keyHash, payloadHash, cancellationToken)
            ?? result;
    }

    public async Task<ReadResult<FinanceOfficialSalesInvoiceViewV1>> GetAsync(
        FinanceContext context,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (invoiceId == Guid.Empty)
        {
            return new(ReadOutcome.NotFound, null);
        }
        if (await ReadFenceAsync<FinanceOfficialSalesInvoiceViewV1>(context, invoiceId, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        FinanceOfficialSalesInvoiceRow? row;
        try
        {
            row = await dbContext.OfficialSalesInvoices.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == invoiceId
                    && candidate.CompanyId == context.CompanyId, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await FailureAfterReadAsync<FinanceOfficialSalesInvoiceViewV1>(context, invoiceId,
                cancellationToken);
        }

        if (await ReadFenceAsync<FinanceOfficialSalesInvoiceViewV1>(context, invoiceId, cancellationToken)
            is { } afterRead)
        {
            return afterRead;
        }
        return row is null ? new(ReadOutcome.NotFound, null) : new(ReadOutcome.Found, View(row));
    }

    public async Task<ReadResult<FinanceOfficialSalesInvoicePageV1>> ListAsync(
        FinanceContext context,
        bool pendingOnly,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCursor(context.CompanyId, pendingOnly, cursor, out var offset))
        {
            return new(ReadOutcome.NotFound, null);
        }
        if (await ReadFenceAsync<FinanceOfficialSalesInvoicePageV1>(context, Guid.Empty, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        List<FinanceOfficialSalesInvoiceRow> rows;
        try
        {
            var query = dbContext.OfficialSalesInvoices.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId);
            if (pendingOnly)
            {
                var pending = (int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval;
                query = query.Where(row => row.Status == pending);
            }
            rows = await query
                .OrderByDescending(row => row.RecordedAtUtc)
                .ThenBy(row => row.Id)
                .Skip(offset)
                .Take(PageSize + 1)
                .ToListAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await FailureAfterReadAsync<FinanceOfficialSalesInvoicePageV1>(context, Guid.Empty,
                cancellationToken);
        }

        if (await ReadFenceAsync<FinanceOfficialSalesInvoicePageV1>(context, Guid.Empty, cancellationToken)
            is { } afterRead)
        {
            return afterRead;
        }

        var hasMore = rows.Count > PageSize;
        return new(ReadOutcome.Found, new FinanceOfficialSalesInvoicePageV1(
            context.CompanyId,
            [.. rows.Take(PageSize).Select(View)],
            hasMore ? EncodeCursor(context.CompanyId, pendingOnly, checked(offset + PageSize)) : null));
    }

    private static List<ValidationIssue> Validate(FinanceOfficialSalesInvoiceRecordRequestV1 request, out string ettn)
    {
        var issues = new List<ValidationIssue>();
        ettn = string.Empty;
        if (request.InvoiceId == Guid.Empty) issues.Add(Issue("invoiceId", "identity_required"));

        if (string.IsNullOrWhiteSpace(request.OfficialNo))
        {
            issues.Add(Issue("officialNo", "required"));
        }
        else if (request.OfficialNo.Length > FinanceDbContext.OfficialSalesInvoiceMaximumOfficialNoLength
            || !string.Equals(request.OfficialNo, request.OfficialNo.Trim(), StringComparison.Ordinal))
        {
            issues.Add(Issue("officialNo", "invalid"));
        }

        if (string.IsNullOrWhiteSpace(request.Ettn))
        {
            issues.Add(Issue("ettn", "required"));
        }
        else if (request.Ettn.Length != FinanceDbContext.OfficialSalesInvoiceEttnLength
            || !Guid.TryParseExact(request.Ettn, "D", out var parsedEttn) || parsedEttn == Guid.Empty)
        {
            issues.Add(Issue("ettn", "invalid"));
        }
        else
        {
            ettn = parsedEttn.ToString("D");
        }

        if (request.CustomerPartyId == Guid.Empty) issues.Add(Issue("customerPartyId", "required"));
        if (request.CurrencyId == Guid.Empty) issues.Add(Issue("currencyId", "required"));
        Amount(issues, "payableAmount", request.PayableAmount, positive: true);
        Amount(issues, "payableFunctionalAmount", request.PayableFunctionalAmount, positive: true);
        Amount(issues, "taxBaseFunctionalAmount", request.TaxBaseFunctionalAmount, positive: false);
        Amount(issues, "vatFunctionalAmount", request.VatFunctionalAmount, positive: false);
        Amount(issues, "withholdingFunctionalAmount", request.WithholdingFunctionalAmount, positive: false);
        if (request.PdfDocumentId == Guid.Empty) issues.Add(Issue("pdfDocumentId", "required"));
        return issues;
    }

    /// <summary>
    /// Exact decimal text (DEC-FIN-014). It must also fit <see cref="decimal"/> without rounding because the Sales
    /// read port (D3) carries decimals; a value that would be rounded is refused, never silently changed.
    /// </summary>
    private static void Amount(List<ValidationIssue> issues, string field, string? text, bool positive)
    {
        if (string.IsNullOrEmpty(text))
        {
            issues.Add(Issue(field, "required"));
            return;
        }
        var exact = positive
            ? FinanceExactDecimalText.TryParsePositive(text, out var parsed)
            : FinanceExactDecimalText.TryParseNonNegative(text, out parsed);
        if (!exact || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var value)
            || !FinanceExactDecimalText.Equal(value.ToString(CultureInfo.InvariantCulture), parsed.Text))
        {
            issues.Add(Issue(field, "invalid"));
        }
    }

    private static IEnumerable<ValidationIssue> DuplicateIssues(bool sameNo, bool sameEttn)
    {
        if (sameNo) yield return Issue("officialNo", "official_no_already_recorded");
        if (sameEttn) yield return Issue("ettn", "ettn_already_recorded");
    }

    private static FinanceOfficialSalesInvoiceResultV1? PdfRefusal(
        FinanceContext context,
        FinanceOfficialSalesInvoicePdfCheckV1? pdf,
        int? expectedVersionNo,
        string? expectedSha256)
    {
        if (pdf is null)
        {
            return Refused(context, CommandOutcome.Unavailable, Issue("pdfDocumentId", "document_unavailable"));
        }
        return pdf.Outcome switch
        {
            FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted when pdf.VersionNo is >= 1
                && pdf.Sha256 is { Length: 64 } sha && IsLowerHex(sha)
                && (expectedVersionNo is null || pdf.VersionNo == expectedVersionNo)
                && (expectedSha256 is null || string.Equals(sha, expectedSha256, StringComparison.Ordinal)) => null,
            FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted when expectedVersionNo is not null =>
                Refused(context, CommandOutcome.ValidationFailed, Issue("pdfDocumentId", "document_changed")),
            FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted =>
                Refused(context, CommandOutcome.Unavailable, Issue("pdfDocumentId", "document_unavailable")),
            FinanceOfficialSalesInvoicePdfOutcomeV1.NotFound =>
                Refused(context, CommandOutcome.ValidationFailed, Issue("pdfDocumentId", "document_not_found")),
            FinanceOfficialSalesInvoicePdfOutcomeV1.NotPdf =>
                Refused(context, CommandOutcome.ValidationFailed, Issue("pdfDocumentId", "document_not_pdf")),
            FinanceOfficialSalesInvoicePdfOutcomeV1.OwnerMismatch =>
                Refused(context, CommandOutcome.ValidationFailed, Issue("pdfDocumentId", "document_owner_mismatch")),
            FinanceOfficialSalesInvoicePdfOutcomeV1.NotDownloadable =>
                Refused(context, CommandOutcome.ValidationFailed, Issue("pdfDocumentId", "document_not_clean")),
            FinanceOfficialSalesInvoicePdfOutcomeV1.Denied =>
                Refused(context, CommandOutcome.Denied, Issue("pdfDocumentId", "document_access_denied")),
            _ => Refused(context, CommandOutcome.Unavailable, Issue("pdfDocumentId", "document_unavailable")),
        };
    }

    private async Task<FinanceOfficialSalesInvoiceResultV1?> PriorAsync(
        FinanceContext context, string command, FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId,
        string keyHash, string payloadHash, CancellationToken cancellationToken)
    {
        var prior = await dbContext.OfficialSalesInvoiceCommands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (await FenceAsync(context, action, invoiceId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (prior is null) return null;
        if (!string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal)
            || !string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.IdempotencyConflict);
        }
        var stored = JsonSerializer.Deserialize<FinanceOfficialSalesInvoiceResultV1>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı resmî satış faturası komut sonucu okunamadı.");
        return stored with { IsReplay = true };
    }

    private async Task<FinanceOfficialSalesInvoiceResultV1?> SaveAsync(
        FinanceContext context, string command, FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId,
        string keyHash, string payloadHash, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            return await PriorAsync(context, command, action, invoiceId, keyHash, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }
    }

    private async Task<FinanceOfficialSalesInvoiceResultV1?> FenceAsync(
        FinanceContext context, FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        var decision = await authority.CheckAsync(context, action, invoiceId, cancellationToken);
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        if (decision == PermissionDecision.Allowed) return null;
        return decision == PermissionDecision.Denied
            ? Refused(context, CommandOutcome.Denied)
            : Refused(context, CommandOutcome.Unavailable,
                Issue("permission", "finance_permission_mapping_unavailable"));
    }

    private async Task<ReadResult<T>?> ReadFenceAsync<T>(
        FinanceContext context, Guid invoiceId, CancellationToken cancellationToken) where T : class
    {
        if (!SameContext(context)) return new(ReadOutcome.Denied, null);
        var decision = await authority.CheckAsync(context, FinanceOfficialSalesInvoiceAuthorityActionV1.Read,
            invoiceId, cancellationToken);
        if (!SameContext(context)) return new(ReadOutcome.Denied, null);
        return decision switch
        {
            PermissionDecision.Allowed => null,
            PermissionDecision.Unavailable => new(ReadOutcome.Unavailable, null),
            _ => new(ReadOutcome.Denied, null),
        };
    }

    private async Task<ReadResult<T>> FailureAfterReadAsync<T>(
        FinanceContext context, Guid invoiceId, CancellationToken cancellationToken) where T : class =>
        await ReadFenceAsync<T>(context, invoiceId, cancellationToken) ?? new(ReadOutcome.Unavailable, null);

    private void AddCommand(FinanceContext context, string command, string keyHash, string payloadHash,
        Guid invoiceId, FinanceOfficialSalesInvoiceResultV1 result, DateTimeOffset now) =>
        dbContext.OfficialSalesInvoiceCommands.Add(new FinanceOfficialSalesInvoiceCommandRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = keyHash,
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            InvoiceId = invoiceId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    private void Audit(FinanceContext context, CommandMetadata metadata, Guid invoiceId, string eventType,
        string operation, long? beforeVersion, long afterVersion, string before, string after,
        FinanceAuditEnvelope envelope, string reason)
        => auditEvents.Enqueue(new AuditEventDraft(context.CompanyId, eventType, EntityType, invoiceId.ToString("D"),
            operation, beforeVersion, afterVersion, AuditActorTypes.User, context.ActorId, envelope.PermissionCode,
            envelope.RoleCodes, before, after, reason, null, AuditAiInvolvement.None, null, envelope.SourceChannel,
            envelope.ClientIpAddress, envelope.UserAgent, context.CorrelationId, context.CausationId,
            metadata.IdempotencyKey));

    private bool SameContext(FinanceContext context) => currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == context.CompanyId
        && context.CompanyId != Guid.Empty && currentUser.CanAccessCompany(context.CompanyId);

    private static FinanceOfficialSalesInvoiceViewV1 View(FinanceOfficialSalesInvoiceRow row) => new(
        row.Id, row.CompanyId, row.OfficialNo, row.Ettn, row.CustomerPartyId, row.CurrencyId,
        row.PayableAmountText, row.PayableFunctionalAmountText, row.TaxBaseFunctionalAmountText,
        row.VatFunctionalAmountText,
        row.WithholdingFunctionalAmountText, row.PdfDocumentId, row.PdfDocumentVersionNo, row.PdfSha256,
        (FinanceOfficialSalesInvoiceStatusV1)row.Status, VersionText(row.Version), row.RecordedBy, row.RecordedAtUtc,
        row.ApprovedBy, row.ApprovedAtUtc);

    private static string AuditJson(FinanceOfficialSalesInvoiceRow row) => JsonSerializer.Serialize(View(row), Json);

    private static string VersionText(long version) => version.ToString(CultureInfo.InvariantCulture);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool IsLowerHex(string value) => value.All(character => char.IsAsciiDigit(character)
        || character is >= 'a' and <= 'f');

    private static bool ValidIdentity(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= IdentityMaximumLength && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static string EncodeCursor(Guid companyId, bool pendingOnly, int offset)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"{companyId:N}:{(pendingOnly ? 'p' : 'a')}:{offset.ToString(CultureInfo.InvariantCulture)}");
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryCursor(Guid companyId, bool pendingOnly, string? cursor, out int offset)
    {
        offset = 0;
        if (cursor is null) return true;
        if (cursor.Length is 0 or > 128) return false;
        try
        {
            var value = cursor.Replace('-', '+').Replace('_', '/');
            value = value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(value)).Split(':');
            if (parts.Length != 3
                || !Guid.TryParseExact(parts[0], "N", out var cursorCompany) || cursorCompany != companyId
                || !string.Equals(parts[1], pendingOnly ? "p" : "a", StringComparison.Ordinal)
                || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out offset)
                || offset < 0 || offset > MaximumOffset)
            {
                return false;
            }
            return string.Equals(cursor, EncodeCursor(companyId, pendingOnly, offset), StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static ValidationIssue Issue(string field, string code) => new(field, code);

    private static FinanceOfficialSalesInvoiceResultV1 Succeeded(FinanceContext context,
        FinanceOfficialSalesInvoiceViewV1 resource) => new(CommandOutcome.Succeeded, resource, false,
        context.CorrelationId, []);

    private static FinanceOfficialSalesInvoiceResultV1 Refused(FinanceContext context, CommandOutcome outcome,
        params ValidationIssue[] issues) => new(outcome, null, false, context.CorrelationId, issues);
}
