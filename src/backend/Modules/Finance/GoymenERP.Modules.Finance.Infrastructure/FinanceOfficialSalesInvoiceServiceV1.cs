using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed record FinanceOfficialSalesInvoiceAuditEnvelopesV1(
    FinanceAuditEnvelope Record,
    FinanceAuditEnvelope Approve);

/// <summary>
/// F-005 D1 (FNS-D17…FNS-D21, FNS-D26): the manual official sales invoice record. One actor reserves the record id,
/// uploads the PDF to that id in the Documents vault and records every mandatory field with it (#66 6860586175: PDF
/// first, one "Kaydet" step); a different actor approves it (maker≠approver, also enforced by the database check).
/// Only an approved record is a fact other modules may read (D3). There is no edit, cancel or withdraw command
/// (FNS-D20).
/// </summary>
public sealed class FinanceOfficialSalesInvoiceServiceV1(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinanceOfficialSalesInvoiceAuthorityV1 authority,
    IFinanceOfficialSalesInvoiceDocumentPortV1 documents,
    ISettingsMoneyCandidateReader currencies,
    IAuditEventCollector auditEvents,
    FinanceOfficialSalesInvoiceAuditEnvelopesV1 envelopes,
    TimeProvider timeProvider)
    : IFinanceOfficialSalesInvoiceServiceV1
{
    public const string ReserveCommand = "finance.official-sales-invoices.reserve";
    public const string RecordCommand = "finance.official-sales-invoices.record";
    public const string ApproveCommand = "finance.official-sales-invoices.approve";
    private const string EntityType = "Finance.OfficialSalesInvoice";
    private const string ReservationEntityType = "Finance.OfficialSalesInvoiceReservation";
    private const int IdentityMaximumLength = 128;
    private const int PageSize = 50;
    private const int MaximumOffset = 1_000_000;
    private const long PendingVersion = 1;
    private const long ApprovedVersion = 2;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    /// <summary>
    /// #66 6860586175: pre-allocates the record id so the PDF can be uploaded to its owner before the record exists.
    /// It needs Record authority, has no money effect, no expiry and is never deleted; the same key replays the first
    /// reservation. The id is generated here for the context company, never taken from the client.
    /// </summary>
    public async Task<FinanceOfficialSalesInvoiceReservationResultV1> ReserveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOfficialSalesInvoiceReservationRequestV1 request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // The host refuses Record authority for an empty id, so the fence carries this candidate id; a replay answers
        // the first reservation's id instead and the candidate is never stored.
        var action = FinanceOfficialSalesInvoiceAuthorityActionV1.Record;
        var invoiceId = Guid.NewGuid();
        if (await FenceAsync(context, action, invoiceId, cancellationToken) is { } denied)
        {
            return Reservation(denied);
        }
        if (!ValidIdentity(metadata.IdempotencyKey) || !ValidIdentity(context.ActorId)
            || !ValidIdentity(context.CorrelationId))
        {
            return Reservation(Refused(context, CommandOutcome.ValidationFailed,
                Issue("request", "finance_value_does_not_fit")));
        }

        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payloadHash = Hash(JsonSerializer.Serialize(request, Json));
        if (await PriorReservationAsync(context, action, invoiceId, keyHash, payloadHash, cancellationToken)
            is { } prior)
        {
            return prior;
        }

        var now = timeProvider.GetUtcNow();
        var row = new FinanceOfficialSalesInvoiceReservationRow
        {
            Id = invoiceId,
            CompanyId = context.CompanyId,
            ReservedBy = context.ActorId,
            ReservedAtUtc = now,
            CorrelationId = context.CorrelationId,
            CausationId = context.CausationId,
        };
        var resource = new FinanceOfficialSalesInvoiceReservationV1(row.Id, row.CompanyId, row.ReservedBy,
            row.ReservedAtUtc);
        var result = new FinanceOfficialSalesInvoiceReservationResultV1(CommandOutcome.Succeeded, resource, false,
            context.CorrelationId, []);
        dbContext.OfficialSalesInvoiceReservations.Add(row);
        AddCommand(context, ReserveCommand, keyHash, payloadHash, row.Id, result, now);

        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } beforeSave)
        {
            dbContext.ChangeTracker.Clear();
            return Reservation(beforeSave);
        }

        Audit(context, metadata, ReservationEntityType, row.Id, "finance.official-sales-invoice.reserved.v1",
            "Reserve", null, null, "{}", JsonSerializer.Serialize(resource, Json), envelopes.Record,
            "Resmî satış faturası kaydı için kimlik ayrıldı; para etkisi yok");
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (IsLostRace(exception))
        {
            // §8: the same key raced; the winner's reservation is the answer.
            dbContext.ChangeTracker.Clear();
            return await PriorReservationAsync(context, action, invoiceId, keyHash, payloadHash, cancellationToken)
                ?? Reservation(Refused(context, CommandOutcome.VersionConflict,
                    Issue("request", "concurrent_change")));
        }
    }

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

        var issues = Validate(request, out var ettn, out var amounts);
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
        if (await ConflictRefusalAsync(context, action, request.InvoiceId, officialNo, ettn, pdfSha256: null,
                cancellationToken) is { } conflict)
        {
            return conflict;
        }
        if (await CurrencyRefusalAsync(context, action, request.InvoiceId, request.CurrencyId, cancellationToken)
            is { } currencyRefused)
        {
            return currencyRefused;
        }

        // #66 6860586175: the PDF must already belong to this record id (uploaded after the reservation); its hash
        // comes only from the vault's metadata.
        var pdf = await documents.CheckAsync(context, request.PdfDocumentId, request.InvoiceId, null,
            cancellationToken);
        if (PdfRefusal(context, pdf, expectedVersionNo: null, expectedSha256: null) is { } pdfRefused)
        {
            return pdfRefused;
        }
        if (await ConflictRefusalAsync(context, action, request.InvoiceId, officialNo, ettn, pdf!.Sha256,
                cancellationToken) is { } pdfConflict)
        {
            return pdfConflict;
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
            // Stored normalised, so the text kept is the value validated (the payload hash stays the request's).
            PayableAmountText = amounts.Payable,
            PayableFunctionalAmountText = amounts.PayableFunctional,
            TaxBaseFunctionalAmountText = amounts.TaxBaseFunctional,
            VatFunctionalAmountText = amounts.VatFunctional,
            WithholdingFunctionalAmountText = amounts.WithholdingFunctional,
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

        Audit(context, metadata, EntityType, row.Id, "finance.official-sales-invoice.recorded.v1", "Record", null,
            PendingVersion, "{}", AuditJson(row), envelopes.Record,
            "Resmî satış faturası elle kaydedildi; ikinci kişinin onayını bekliyor");
        return await SaveAsync(context, RecordCommand, action, row.Id, keyHash, payloadHash,
                () => ConflictRefusalAsync(context, action, row.Id, officialNo, ettn, row.PdfSha256,
                    cancellationToken), cancellationToken)
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
            // Another company's record is not visible either: the same answer, nothing leaks. Terminal (ADR-011 §7):
            // the key keeps this answer even if a record with this id appears later.
            return await TerminalAsync(context, ApproveCommand, action, request.InvoiceId, keyHash, payloadHash,
                Refused(context, CommandOutcome.ValidationFailed, Issue("invoiceId", "invoice_not_found")),
                cancellationToken);
        }
        if (row.Status != (int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval
            || !string.Equals(request.ExpectedVersion, VersionText(row.Version), StringComparison.Ordinal))
        {
            // Terminal (§7): a retry of this key answers the same conflict even after the record moves on.
            return await TerminalAsync(context, ApproveCommand, action, row.Id, keyHash, payloadHash,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken);
        }
        if (string.Equals(row.RecordedBy, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("approver", "approver_must_differ_from_recorder"));
        }
        if (await CurrencyRefusalAsync(context, action, row.Id, row.CurrencyId, cancellationToken)
            is { } currencyRefused)
        {
            dbContext.ChangeTracker.Clear();
            return currencyRefused;
        }

        var pdf = await documents.CheckAsync(context, row.PdfDocumentId, row.Id, row.PdfDocumentVersionNo,
            cancellationToken);
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

        Audit(context, metadata, EntityType, row.Id, "finance.official-sales-invoice.approved.v1", "Approve",
            PendingVersion, ApprovedVersion, before, AuditJson(row), envelopes.Approve,
            "Kaydedenden farklı yetkili finans kullanıcısı resmî satış faturasını onayladı");
        return await SaveAsync(context, ApproveCommand, action, row.Id, keyHash, payloadHash, conflicts: null,
                cancellationToken)
            ?? result;
    }

    public async Task<ReadResult<FinanceOfficialSalesInvoiceViewV1>> GetAsync(
        FinanceContext context,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync<FinanceOfficialSalesInvoiceViewV1>(context, invoiceId, cancellationToken)
            is { } refused)
        {
            return refused;
        }
        if (invoiceId == Guid.Empty)
        {
            return new(ReadOutcome.NotFound, null);
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
        if (await ReadFenceAsync<FinanceOfficialSalesInvoicePageV1>(context, Guid.Empty, cancellationToken)
            is { } refused)
        {
            return refused;
        }
        if (!TryCursor(context.CompanyId, pendingOnly, cursor, out var offset))
        {
            return new(ReadOutcome.NotFound, null);
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

    private static List<ValidationIssue> Validate(FinanceOfficialSalesInvoiceRecordRequestV1 request, out string ettn,
        out NormalizedAmounts amounts)
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
        amounts = new NormalizedAmounts(
            Amount(issues, "payableAmount", request.PayableAmount, positive: true),
            Amount(issues, "payableFunctionalAmount", request.PayableFunctionalAmount, positive: true),
            Amount(issues, "taxBaseFunctionalAmount", request.TaxBaseFunctionalAmount, positive: false),
            Amount(issues, "vatFunctionalAmount", request.VatFunctionalAmount, positive: false),
            Amount(issues, "withholdingFunctionalAmount", request.WithholdingFunctionalAmount, positive: false));
        if (request.PdfDocumentId == Guid.Empty) issues.Add(Issue("pdfDocumentId", "required"));
        return issues;
    }

    /// <summary>
    /// Exact decimal text (DEC-FIN-014). It must also fit <see cref="decimal"/> without rounding because the Sales
    /// read port (D3) carries decimals; a value that would be rounded is refused, never silently changed. Answers the
    /// normalised text (no leading zeros, the scale kept), or an empty text when the value is refused.
    /// </summary>
    private static string Amount(List<ValidationIssue> issues, string field, string? text, bool positive)
    {
        if (string.IsNullOrEmpty(text))
        {
            issues.Add(Issue(field, "required"));
            return string.Empty;
        }
        var exact = positive
            ? FinanceExactDecimalText.TryParsePositive(text, out var parsed)
            : FinanceExactDecimalText.TryParseNonNegative(text, out parsed);
        if (!exact || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var value)
            || !FinanceExactDecimalText.Equal(value.ToString(CultureInfo.InvariantCulture), parsed.Text))
        {
            issues.Add(Issue(field, "invalid"));
            return string.Empty;
        }
        return parsed.Text;
    }

    private readonly record struct NormalizedAmounts(
        string Payable,
        string PayableFunctional,
        string TaxBaseFunctional,
        string VatFunctional,
        string WithholdingFunctional);

    private static IEnumerable<ValidationIssue> DuplicateIssues(bool sameNo, bool sameEttn)
    {
        if (sameNo) yield return Issue("officialNo", "official_no_already_recorded");
        if (sameEttn) yield return Issue("ettn", "ettn_already_recorded");
    }

    /// <summary>
    /// The record's conflicts, read through the company filter: the id already recorded (409), no reservation of this
    /// id by this actor (422 <c>reservation_not_found</c>), a duplicate official number or ETTN (422) and, once the
    /// vault has answered the hash, the same PDF content in another record (422 <c>pdf_already_recorded</c> carrying
    /// that record's official number in <c>ExistingOfficialNo</c>; #66 6860586175). It runs before the PDF check,
    /// after it, and after a save that lost a race to a unique index, so the race answers what the check would have.
    /// None of these is terminal: nothing is recorded.
    /// </summary>
    private async Task<FinanceOfficialSalesInvoiceResultV1?> ConflictRefusalAsync(
        FinanceContext context, FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId, string officialNo,
        string ettn, string? pdfSha256, CancellationToken cancellationToken)
    {
        var companyId = context.CompanyId;
        var sameId = await dbContext.OfficialSalesInvoices.AsNoTracking()
            .AnyAsync(row => row.Id == invoiceId, cancellationToken);
        var reservedBy = await dbContext.OfficialSalesInvoiceReservations.AsNoTracking()
            .Where(row => row.Id == invoiceId && row.CompanyId == companyId)
            .Select(row => row.ReservedBy)
            .SingleOrDefaultAsync(cancellationToken);
        var sameNo = await dbContext.OfficialSalesInvoices.AsNoTracking()
            .AnyAsync(row => row.OfficialNo == officialNo, cancellationToken);
        var sameEttn = await dbContext.OfficialSalesInvoices.AsNoTracking()
            .AnyAsync(row => row.Ettn == ettn, cancellationToken);
        var pdfHolderOfficialNo = pdfSha256 is null
            ? null
            : await dbContext.OfficialSalesInvoices.AsNoTracking()
                .Where(row => row.PdfSha256 == pdfSha256 && row.CompanyId == companyId)
                .Select(row => row.OfficialNo)
                .SingleOrDefaultAsync(cancellationToken);
        if (await FenceAsync(context, action, invoiceId, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }
        if (sameId)
        {
            return Refused(context, CommandOutcome.IdempotencyConflict, Issue("invoiceId", "invoice_already_exists"));
        }
        // Fail closed: only the reserving actor consumes a reservation (another company's id is not visible either).
        if (!string.Equals(reservedBy, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("invoiceId", "reservation_not_found"));
        }

        List<ValidationIssue> issues = [.. DuplicateIssues(sameNo, sameEttn)];
        if (pdfHolderOfficialNo is not null)
        {
            issues.Add(Issue("pdfDocumentId", "pdf_already_recorded"));
        }
        return issues.Count == 0
            ? null
            : new FinanceOfficialSalesInvoiceResultV1(CommandOutcome.ValidationFailed, null, false,
                context.CorrelationId, issues, pdfHolderOfficialNo);
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

    /// <summary>
    /// The stored answer of a key, after the fence: none, a refusal (another actor or payload under the key), or the
    /// first answer's JSON for the caller to replay.
    /// </summary>
    private async Task<(string? ResultJson, FinanceOfficialSalesInvoiceResultV1? Refusal)> PriorJsonAsync(
        FinanceContext context, string command, FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId,
        string keyHash, string payloadHash, CancellationToken cancellationToken)
    {
        var prior = await dbContext.OfficialSalesInvoiceCommands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (await FenceAsync(context, action, invoiceId, cancellationToken) is { } denied)
        {
            return (null, denied);
        }
        if (prior is null) return (null, null);
        if (!string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal)
            || !string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            return (null, Refused(context, CommandOutcome.IdempotencyConflict));
        }
        return (prior.ResultJson, null);
    }

    private async Task<FinanceOfficialSalesInvoiceResultV1?> PriorAsync(
        FinanceContext context, string command, FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId,
        string keyHash, string payloadHash, CancellationToken cancellationToken)
    {
        var (json, refusal) = await PriorJsonAsync(context, command, action, invoiceId, keyHash, payloadHash,
            cancellationToken);
        if (refusal is not null || json is null) return refusal;
        var stored = JsonSerializer.Deserialize<FinanceOfficialSalesInvoiceResultV1>(json, Json)
            ?? throw new InvalidOperationException("Kayıtlı resmî satış faturası komut sonucu okunamadı.");
        return stored with { IsReplay = true };
    }

    private async Task<FinanceOfficialSalesInvoiceReservationResultV1?> PriorReservationAsync(
        FinanceContext context, FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId, string keyHash,
        string payloadHash, CancellationToken cancellationToken)
    {
        var (json, refusal) = await PriorJsonAsync(context, ReserveCommand, action, invoiceId, keyHash, payloadHash,
            cancellationToken);
        if (refusal is not null) return Reservation(refusal);
        if (json is null) return null;
        var stored = JsonSerializer.Deserialize<FinanceOfficialSalesInvoiceReservationResultV1>(json, Json)
            ?? throw new InvalidOperationException("Kayıtlı resmî satış faturası kimlik ayırma sonucu okunamadı.");
        return stored with { IsReplay = true };
    }

    /// <summary>
    /// One save. A lost race answers the winner's record for the same key (§8), else what
    /// <paramref name="conflicts"/> finds now (a unique index the pre-check could not see yet), else
    /// <c>concurrent_change</c>.
    /// </summary>
    private async Task<FinanceOfficialSalesInvoiceResultV1?> SaveAsync(
        FinanceContext context, string command, FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId,
        string keyHash, string payloadHash, Func<Task<FinanceOfficialSalesInvoiceResultV1?>>? conflicts,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (IsLostRace(exception))
        {
            dbContext.ChangeTracker.Clear();
            return await PriorAsync(context, command, action, invoiceId, keyHash, payloadHash, cancellationToken)
                ?? (conflicts is null ? null : await conflicts())
                ?? Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }
    }

    private static bool IsLostRace(DbUpdateException exception) => exception is DbUpdateConcurrencyException
        || exception.InnerException is SqlException { Number: 2601 or 2627 };

    /// <summary>
    /// ADR-011 §7: an authorized terminal refusal is the key's result for good. It is recorded alone (no business
    /// change, no audit event) and replayed with its first correlation; a lost race answers the winner's record (§8).
    /// </summary>
    private async Task<FinanceOfficialSalesInvoiceResultV1> TerminalAsync(
        FinanceContext context, string command, FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId,
        string keyHash, string payloadHash, FinanceOfficialSalesInvoiceResultV1 refusal,
        CancellationToken cancellationToken)
    {
        AddCommand(context, command, keyHash, payloadHash, invoiceId, refusal, timeProvider.GetUtcNow());
        if (await FenceAsync(context, action, invoiceId, cancellationToken) is { } fenced)
        {
            dbContext.ChangeTracker.Clear();
            return fenced;
        }
        return await SaveAsync(context, command, action, invoiceId, keyHash, payloadHash, conflicts: null,
                cancellationToken)
            ?? refusal;
    }

    /// <summary>
    /// FinanceBankAccountService's rule (#66 6860586175): the currency must be one of Settings' money candidates now.
    /// An unreachable or malformed source is Unavailable with the Settings code, a forbidden one is Denied; nothing is
    /// defaulted. A currency outside the candidates is a refusal on the merits, so it is not recorded.
    /// </summary>
    private async Task<FinanceOfficialSalesInvoiceResultV1?> CurrencyRefusalAsync(
        FinanceContext context, FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId, Guid currencyId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(
                await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken));
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, action, invoiceId, cancellationToken) is { } fenced)
            {
                return fenced;
            }
            return exception.Code == "settings_access_denied"
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("currencyId", exception.Code));
        }

        if (await FenceAsync(context, action, invoiceId, cancellationToken) is { } afterSettings)
        {
            return afterSettings;
        }
        return candidates.Any(candidate => candidate.CurrencyId == currencyId)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("currencyId", "currency_not_a_candidate"));
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

    private void AddCommand<TResult>(FinanceContext context, string command, string keyHash, string payloadHash,
        Guid invoiceId, TResult result, DateTimeOffset now) =>
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

    private void Audit(FinanceContext context, CommandMetadata metadata, string entityType, Guid entityId,
        string eventType, string operation, long? beforeVersion, long? afterVersion, string before, string after,
        FinanceAuditEnvelope envelope, string reason)
        => auditEvents.Enqueue(new AuditEventDraft(context.CompanyId, eventType, entityType, entityId.ToString("D"),
            operation, beforeVersion, afterVersion, AuditActorTypes.User, context.ActorId, envelope.PermissionCode,
            envelope.RoleCodes, before, after, reason, null, AuditAiInvolvement.None, null, envelope.SourceChannel,
            envelope.ClientIpAddress, envelope.UserAgent, context.CorrelationId, context.CausationId,
            metadata.IdempotencyKey));

    private bool SameContext(FinanceContext context) => SameContext(currentUser, companyContext, context);

    /// <summary>The live user and company are still the captured actor and company (shared with the owner read).</summary>
    internal static bool SameContext(ICurrentUser user, ICompanyContext company, FinanceContext context) =>
        user.IsAuthenticated
        && !string.IsNullOrWhiteSpace(user.UserId)
        && string.Equals(user.UserId, context.ActorId, StringComparison.Ordinal)
        && company.IsResolved && company.CompanyId == context.CompanyId
        && context.CompanyId != Guid.Empty && user.CanAccessCompany(context.CompanyId);

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
        context.CorrelationId, [], null);

    private static FinanceOfficialSalesInvoiceResultV1 Refused(FinanceContext context, CommandOutcome outcome,
        params ValidationIssue[] issues) => new(outcome, null, false, context.CorrelationId, issues, null);

    private static FinanceOfficialSalesInvoiceReservationResultV1 Reservation(
        FinanceOfficialSalesInvoiceResultV1 refusal) => new FinanceOfficialSalesInvoiceReservationResultV1(
        refusal.Outcome, null, refusal.IsReplay, refusal.OriginalCorrelationId, refusal.Issues);
}

/// <summary>
/// #66 6860586175: the Finance answer behind the Documents owner kind <c>finance.official-sales-invoice</c>. The
/// actor must hold Record or Approve company-wide (the host's Read answer) before and after the read; the id must be
/// a reservation of the context company (every record consumed one, so a record is found through it). It only reads,
/// through the tenant-filtered context, and answers Unavailable on any failure (fail closed).
/// </summary>
public sealed class FinanceOfficialSalesInvoiceOwnerReadV1(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinanceOfficialSalesInvoiceAuthorityV1 authority)
    : IFinanceOfficialSalesInvoiceOwnerReadV1
{
    public async Task<ReadOutcome> CheckReadableAsync(
        FinanceContext context,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await FenceAsync(context, invoiceId, cancellationToken) is { } refused)
        {
            return refused;
        }
        if (invoiceId == Guid.Empty)
        {
            return ReadOutcome.NotFound;
        }

        bool reserved;
        try
        {
            var companyId = context.CompanyId;
            reserved = await dbContext.OfficialSalesInvoiceReservations.AsNoTracking()
                .AnyAsync(row => row.Id == invoiceId && row.CompanyId == companyId, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await FenceAsync(context, invoiceId, cancellationToken) ?? ReadOutcome.Unavailable;
        }

        return await FenceAsync(context, invoiceId, cancellationToken)
            ?? (reserved ? ReadOutcome.Found : ReadOutcome.NotFound);
    }

    private async Task<ReadOutcome?> FenceAsync(FinanceContext context, Guid invoiceId,
        CancellationToken cancellationToken)
    {
        if (!FinanceOfficialSalesInvoiceServiceV1.SameContext(currentUser, companyContext, context))
        {
            return ReadOutcome.Denied;
        }
        var decision = await authority.CheckAsync(context, FinanceOfficialSalesInvoiceAuthorityActionV1.Read,
            invoiceId, cancellationToken);
        if (!FinanceOfficialSalesInvoiceServiceV1.SameContext(currentUser, companyContext, context))
        {
            return ReadOutcome.Denied;
        }
        return decision switch
        {
            PermissionDecision.Allowed => null,
            PermissionDecision.Unavailable => ReadOutcome.Unavailable,
            _ => ReadOutcome.Denied,
        };
    }
}
