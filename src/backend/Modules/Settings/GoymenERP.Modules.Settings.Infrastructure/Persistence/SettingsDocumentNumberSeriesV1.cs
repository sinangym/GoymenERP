using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>Settings owns the annual counter and immutable number/cancellation history. The document owner owns commit/rollback.</summary>
internal sealed class SettingsDocumentNumberSeriesV1(SettingsDbContext database, ICompanyContext companyContext,
    ICurrentUser user, IErpAuthorityRevalidationService authority, IServiceScopeFactory scopes,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, TimeProvider time)
    : ISettingsDocumentNumberSeriesV1, ISettingsDocumentNumberSeriesAdministrationV1
{
    private const string FormatCommand = "settings.document-number-format.record.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<SettingsDocumentNumberFormatV1> GetPolicyAsync(Guid companyId, SettingsDocumentSeriesV1 series, CancellationToken cancellationToken = default)
    {
        var actor = RequireIdentity(companyId);
        await RequireAsync(companyId, actor, Permission(series, cancel: false), cancellationToken);
        var row = await database.DocumentNumberFormats.AsNoTracking().Where(row => row.Series == (int)series)
            .OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
        await RequireAsync(companyId, actor, Permission(series, cancel: false), cancellationToken);
        return row is null ? throw Missing() : new(companyId, series, row.Prefix, row.SequenceDigits, Text(row.Version));
    }
    public Task<SettingsDocumentNumberReservationV1> ReservePurchaseOrderNumberAsync(IDbTransaction transaction, Guid companyId,
        Guid documentId, DateOnly businessDate, string expectedFormatVersion, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default) => ReserveAsync(transaction, companyId, SettingsDocumentSeriesV1.PurchaseOrder,
            documentId, businessDate, expectedFormatVersion, metadata, cancellationToken);

    public async Task<SettingsDocumentNumberReservationV1> ReserveAsync(IDbTransaction transaction, Guid companyId,
        SettingsDocumentSeriesV1 series, Guid documentId, DateOnly businessDate, string expectedFormatVersion,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ValidateMetadata(companyId, metadata);
        var permission = Permission(series, cancel: false);
        await RequireAsync(companyId, metadata.ActorId, permission, cancellationToken);
        var sql = RequireTransaction(transaction);
        if (documentId == Guid.Empty || businessDate == default || !ParseVersion(expectedFormatVersion, out var expected) || expected < 1)
            throw new ArgumentException("Belge, iş tarihi ve kayıtlı biçim sürümü gereklidir.");
        var key = ReservationKey(series, metadata.IdempotencyKey);
        var hash = IdempotencyHash.ComputePayload(new { Series = series, DocumentId = documentId, BusinessDate = businessDate, ExpectedFormatVersion = expected });
        await using var scope = scopes.CreateAsyncScope();
        var owner = await EnlistAsync(scope.ServiceProvider, companyId, sql, cancellationToken);
        var previous = await owner.DocumentNumberReservations.AsNoTracking().SingleOrDefaultAsync(row => row.KeyHash == key, cancellationToken);
        if (previous is not null)
        {
            await RequireAsync(companyId, metadata.ActorId, permission, cancellationToken);
            if (previous.Series != (int)series || previous.DocumentId != documentId || previous.RequestHash != hash || previous.RecordedBy != metadata.ActorId)
                throw new SettingsPrerequisiteException(SettingsDocumentNumberSeriesProblemCodesV1.IdempotencyConflict);
            return View(previous, replayed: true);
        }
        if (await owner.DocumentNumberReservations.AnyAsync(row => row.Series == (int)series && row.DocumentId == documentId, cancellationToken))
        {
            await RequireAsync(companyId, metadata.ActorId, permission, cancellationToken);
            throw new SettingsPrerequisiteException(SettingsDocumentNumberSeriesProblemCodesV1.DocumentAlreadyNumbered);
        }
        var format = await owner.DocumentNumberFormats.AsNoTracking().Where(row => row.Series == (int)series).OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
        if (format is null) { await RequireAsync(companyId, metadata.ActorId, permission, cancellationToken); throw Missing(); }
        if (format.Version != expected) { await RequireAsync(companyId, metadata.ActorId, permission, cancellationToken); throw new SettingsPrerequisiteException(SettingsDocumentNumberSeriesProblemCodesV1.VersionConflict); }
        var year = businessDate.Year;
        var counter = await owner.DocumentNumberCounters.SingleOrDefaultAsync(row => row.Series == (int)series && row.Year == year, cancellationToken);
        var next = checked((counter?.LastSequence ?? 0) + 1);
        if (counter is null) owner.DocumentNumberCounters.Add(counter = new() { CompanyId = companyId, Series = (int)series, Year = year, LastSequence = next });
        else counter.LastSequence = next;
        var number = string.Create(CultureInfo.InvariantCulture, $"{format.Prefix}-{year:D4}-{next.ToString(CultureInfo.InvariantCulture).PadLeft(format.SequenceDigits, '0')}");
        var row = new DocumentNumberReservationRow { CompanyId = companyId, Series = (int)series, DocumentId = documentId,
            BusinessDate = businessDate, Year = year, Sequence = next, FormatVersion = expected, Number = number,
            KeyHash = key, RequestHash = hash, RecordedBy = metadata.ActorId, RecordedAtUtc = time.GetUtcNow() };
        owner.DocumentNumberReservations.Add(row);
        var grant = await RequireAsync(companyId, metadata.ActorId, permission, cancellationToken);
        await owner.SaveChangesAsync(cancellationToken);
        Enqueue("settings.document-number.reserved.v1", "ReserveNumber", documentId, permission, grant, metadata,
            new { Series = series, BusinessDate = businessDate, Number = number, Sequence = next, FormatVersion = expected }, null, 1, series);
        await auditWriter.WritePendingAsync(sql, cancellationToken);
        await RequireAsync(companyId, metadata.ActorId, permission, cancellationToken);
        return View(row, replayed: false);
    }
    public async Task CancelAsync(IDbTransaction transaction, Guid companyId, SettingsDocumentSeriesV1 series, Guid documentId,
        string reason, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ValidateMetadata(companyId, metadata);
        var permission = Permission(series, cancel: true);
        await RequireAsync(companyId, metadata.ActorId, permission, cancellationToken);
        var sql = RequireTransaction(transaction);
        if (documentId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 4000) throw new ArgumentException("Belge ve gerekçe gereklidir.");
        reason = reason.Trim();
        var key = ReservationKey(series, metadata.IdempotencyKey);
        var hash = IdempotencyHash.ComputePayload(new { Series = series, DocumentId = documentId, Reason = reason });
        await using var scope = scopes.CreateAsyncScope();
        var owner = await EnlistAsync(scope.ServiceProvider, companyId, sql, cancellationToken);
        var previous = await owner.DocumentNumberCancellations.AsNoTracking().SingleOrDefaultAsync(row => row.KeyHash == key || row.Series == (int)series && row.DocumentId == documentId, cancellationToken);
        if (previous is not null)
        {
            await RequireAsync(companyId, metadata.ActorId, permission, cancellationToken);
            if (previous.KeyHash != key || previous.Series != (int)series || previous.DocumentId != documentId || previous.RequestHash != hash || previous.RecordedBy != metadata.ActorId)
                throw new SettingsPrerequisiteException(SettingsDocumentNumberSeriesProblemCodesV1.IdempotencyConflict);
            return;
        }
        var reservation = await owner.DocumentNumberReservations.AsNoTracking().SingleOrDefaultAsync(row => row.Series == (int)series && row.DocumentId == documentId, cancellationToken);
        if (reservation is null) { await RequireAsync(companyId, metadata.ActorId, permission, cancellationToken); throw new SettingsPrerequisiteException(SettingsDocumentNumberSeriesProblemCodesV1.Unavailable); }
        owner.DocumentNumberCancellations.Add(new() { CompanyId = companyId, Series = (int)series, DocumentId = documentId,
            RecordedBy = metadata.ActorId, RecordedAtUtc = time.GetUtcNow(), Reason = reason, KeyHash = key, RequestHash = hash });
        var grant = await RequireAsync(companyId, metadata.ActorId, permission, cancellationToken);
        await owner.SaveChangesAsync(cancellationToken);
        Enqueue("settings.document-number.cancelled.v1", "CancelNumber", documentId, permission, grant, metadata,
            new { Series = series, reservation.Number, reservation.Sequence, Reason = reason }, null, 1, series);
        await auditWriter.WritePendingAsync(sql, cancellationToken);
        await RequireAsync(companyId, metadata.ActorId, permission, cancellationToken);
    }
    public async Task<SettingsDocumentNumberFormatViewV1> GetFormatHistoryAsync(SettingsDocumentSeriesV1 series, CancellationToken cancellationToken = default)
    {
        ValidateSeries(series);
        var company = RequireCompany(); var actor = RequireIdentity(company);
        await RequireAsync(company, actor, SettingsDocumentNumberSeriesPermissionsV1.Manage, cancellationToken);
        var rows = await database.DocumentNumberFormats.AsNoTracking().Where(row => row.Series == (int)series).OrderByDescending(row => row.Version)
            .Select(row => new SettingsDocumentNumberFormatVersionV1(Text(row.Version), row.Prefix, row.SequenceDigits, row.RecordedBy, row.RecordedAtUtc, row.Reason)).ToArrayAsync(cancellationToken);
        await RequireAsync(company, actor, SettingsDocumentNumberSeriesPermissionsV1.Manage, cancellationToken);
        return new(company, series, rows);
    }
    public async Task<IReadOnlyList<SettingsDocumentNumberGapV1>> GetGapsAsync(SettingsDocumentSeriesV1 series, int year, CancellationToken cancellationToken = default)
    {
        ValidateSeries(series);
        if (year is < 1 or > 9999) throw new ArgumentException("Yıl geçersiz.");
        var company = RequireCompany(); var actor = RequireIdentity(company);
        await RequireAsync(company, actor, SettingsDocumentNumberSeriesPermissionsV1.Manage, cancellationToken);
        var rows = await (from cancelled in database.DocumentNumberCancellations.AsNoTracking()
            join number in database.DocumentNumberReservations.AsNoTracking() on new { cancelled.CompanyId, cancelled.Series, cancelled.DocumentId } equals new { number.CompanyId, number.Series, number.DocumentId }
            where number.Series == (int)series && number.Year == year orderby number.Sequence
            select new SettingsDocumentNumberGapV1(series, year, Text(number.Sequence), number.Number, number.DocumentId, cancelled.RecordedAtUtc, cancelled.Reason)).ToArrayAsync(cancellationToken);
        await RequireAsync(company, actor, SettingsDocumentNumberSeriesPermissionsV1.Manage, cancellationToken);
        return rows;
    }
    public async Task<CompanyPolicyCommandResultV1> RecordFormatAsync(RecordSettingsDocumentNumberFormatV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ValidateSeries(request.Series); ValidateMetadata(metadata.CompanyId, metadata);
        const string permission = SettingsDocumentNumberSeriesPermissionsV1.Manage;
        await RequireAsync(metadata.CompanyId, metadata.ActorId, permission, cancellationToken);
        if (!ParseVersion(request.ExpectedVersion, out var expected) || request.Prefix is not { Length: >= 1 and <= 16 } prefix
            || prefix.Any(character => !char.IsAsciiLetterUpper(character) && !char.IsAsciiDigit(character))
            || request.SequenceDigits is not (>= 1 and <= 18) || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 4000)
            return Result(400, CompanyPolicyProblemCodesV1.RequestInvalid, metadata);
        var key = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var hash = IdempotencyHash.ComputePayload(request with { Reason = request.Reason.Trim() });
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var previous = await database.CommandExecutions.AsNoTracking().SingleOrDefaultAsync(row => row.CommandName == FormatCommand && row.IdempotencyKeyHash == key, cancellationToken);
            if (previous is not null)
            {
                await RequireAsync(metadata.CompanyId, metadata.ActorId, permission, cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                return Replay(previous, hash, metadata);
            }
            var current = await database.DocumentNumberFormats.AsNoTracking().Where(row => row.Series == (int)request.Series).OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
            var result = (current?.Version ?? 0) == expected ? Result(204, null, metadata) : Result(409, SettingsDocumentNumberSeriesProblemCodesV1.VersionConflict, metadata);
            if (result.Answer.StatusCode == 204)
            {
                database.DocumentNumberFormats.Add(new() { CompanyId = metadata.CompanyId, Series = (int)request.Series, Version = checked(expected + 1),
                    Prefix = prefix, SequenceDigits = request.SequenceDigits.Value, RecordedBy = metadata.ActorId, RecordedAtUtc = time.GetUtcNow(), Reason = request.Reason.Trim() });
            }
            database.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(Guid.NewGuid(), metadata.CompanyId,
                FormatCommand, key, hash, metadata.ActorId, result.Answer.StatusCode == 204 ? "Succeeded" : "Refused", result.Answer.StatusCode,
                JsonSerializer.Serialize(result.Answer, Json), metadata.CorrelationId, time.GetUtcNow(), 1)));
            var grant = await RequireAsync(metadata.CompanyId, metadata.ActorId, permission, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            if (result.Answer.StatusCode == 204)
            {
                Enqueue("settings.document-number-format.recorded.v1", "RecordFormat", Guid.Empty, permission, grant, metadata,
                    new { request.Series, Prefix = prefix, request.SequenceDigits, Version = checked(expected + 1), Reason = request.Reason.Trim() }, expected == 0 ? null : expected, checked(expected + 1), request.Series);
                await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
            }
            await RequireAsync(metadata.CompanyId, metadata.ActorId, permission, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            database.ChangeTracker.Clear(); auditWriter.DiscardPending();
            var previous = await database.CommandExecutions.AsNoTracking().SingleOrDefaultAsync(row => row.CommandName == FormatCommand && row.IdempotencyKeyHash == key, cancellationToken);
            await RequireAsync(metadata.CompanyId, metadata.ActorId, permission, cancellationToken);
            return previous is null ? throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable) : Replay(previous, hash, metadata);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            database.ChangeTracker.Clear(); auditWriter.DiscardPending(); throw;
        }
        finally { database.ChangeTracker.Clear(); }
    }
    private static async Task<SettingsDbContext> EnlistAsync(IServiceProvider services, Guid company, DbTransaction transaction, CancellationToken ct)
    {
        services.GetRequiredService<ICompanyContext>().Set(company);
        var owner = services.GetRequiredService<SettingsDbContext>();
        if (owner.ChangeTracker.Entries().Any() || owner.Database.CurrentTransaction is not null) throw new InvalidOperationException("A fresh Settings owner context is required.");
        owner.Database.SetDbConnection(transaction.Connection!, contextOwnsConnection: false);
        await owner.Database.UseTransactionAsync(transaction, ct);
        return owner;
    }
    private static DbTransaction RequireTransaction(IDbTransaction transaction) => transaction is DbTransaction { Connection.State: ConnectionState.Open, IsolationLevel: IsolationLevel.Serializable } sql
        ? sql : throw new InvalidOperationException("Document numbering requires the owner's open Serializable SQL transaction.");
    private Guid RequireCompany() => companyContext.IsResolved && companyContext.CompanyId != Guid.Empty ? companyContext.CompanyId : throw new UnauthorizedAccessException();
    private string RequireIdentity(Guid company)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId) || RequireCompany() != company || !user.CanAccessCompany(company)) throw new UnauthorizedAccessException();
        return user.UserId;
    }
    private async Task<PermissionScope> RequireAsync(Guid company, string actor, string permission, CancellationToken ct)
    {
        if (!string.Equals(RequireIdentity(company), actor, StringComparison.Ordinal)) throw new UnauthorizedAccessException();
        var grant = await authority.RevalidatePermissionScopeAsync(permission, company, ct);
        if (!string.Equals(RequireIdentity(company), actor, StringComparison.Ordinal) || grant is not { IsGranted: true, IsCompanyWide: true }) throw new UnauthorizedAccessException();
        return grant;
    }
    private void ValidateMetadata(Guid company, SettingsCommandMetadataV1 metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (company != metadata.CompanyId || RequireIdentity(company) != metadata.ActorId || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.SourceChannel)) throw new InvalidOperationException("Server-derived number-series metadata is required.");
    }
    private static void ValidateSeries(SettingsDocumentSeriesV1 series) { if (!Enum.IsDefined(series)) throw new ArgumentOutOfRangeException(nameof(series)); }
    private static string Permission(SettingsDocumentSeriesV1 series, bool cancel) => (series, cancel) switch
    {
        (SettingsDocumentSeriesV1.SalesOrder, false) => ErpPermissions.SalesDraftsCreate,
        (SettingsDocumentSeriesV1.SalesOrder, true) => ErpPermissions.SalesOrdersCancel,
        (SettingsDocumentSeriesV1.SalesInvoice, _) => ErpPermissions.SalesInvoicesWrite,
        (SettingsDocumentSeriesV1.DispatchNote, false) => ErpPermissions.LogisticsDispatchNotesCreate,
        (SettingsDocumentSeriesV1.DispatchNote, true) => ErpPermissions.LogisticsDispatchNotesCancel,
        (SettingsDocumentSeriesV1.PurchaseOrder, false) => ErpPermissions.PurchasingOrdersCreate,
        (SettingsDocumentSeriesV1.PurchaseOrder, true) => ErpPermissions.PurchasingOrdersCancel,
        _ => throw new ArgumentOutOfRangeException(nameof(series)),
    };
    private void Enqueue(string eventType, string operation, Guid document, string permission, PermissionScope grant, SettingsCommandMetadataV1 metadata,
        object after, long? beforeVersion, long afterVersion, SettingsDocumentSeriesV1 series) => audit.Enqueue(new(
        metadata.CompanyId, eventType, operation == "RecordFormat" ? "Settings.DocumentNumberSeries" : operation == "CancelNumber" ? "Settings.DocumentNumberCancellation" : "Settings.DocumentNumberReservation",
        document == Guid.Empty ? string.Create(CultureInfo.InvariantCulture, $"{metadata.CompanyId:D}:{(int)series}") : document.ToString("D"), operation,
        beforeVersion, afterVersion, AuditActorTypes.User, metadata.ActorId, permission, grant.RoleCodes, "{}", JsonSerializer.Serialize(after, Json),
        "Belge numara serisi işlemi kaydedildi", null, AuditAiInvolvement.None, null, metadata.SourceChannel,
        metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
    private static SettingsDocumentNumberReservationV1 View(DocumentNumberReservationRow row, bool replayed) => new(row.CompanyId, (SettingsDocumentSeriesV1)row.Series,
        row.DocumentId, row.BusinessDate, row.Number, Text(row.FormatVersion), row.Sequence, replayed);
    private static SettingsPrerequisiteException Missing() => new(SettingsDocumentNumberSeriesProblemCodesV1.FormatMissing);
    // A Sales create and a Purchasing create are distinct owner commands. Within one series the key cannot move to another document.
    private static string ReservationKey(SettingsDocumentSeriesV1 series, string key) =>
        IdempotencyHash.ComputeKey(string.Create(CultureInfo.InvariantCulture, $"{(int)series}:{key}"));
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static bool ParseVersion(string? text, out long version)
    {
        version = 0;
        return text is { Length: >= 1 and <= 19 } && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0') && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }
    private static CompanyPolicyCommandResultV1 Result(int status, string? code, SettingsCommandMetadataV1 metadata) => new(new(status, code,
        status == 204 ? "Kaydedildi." : "İşlem tamamlanamadı.", status == 204 ? "Biçim sürümü kaydedildi." : "İstek veya biçim sürümü geçersiz."), false, metadata.CorrelationId);
    private static CompanyPolicyCommandResultV1 Replay(CommandExecution previous, string hash, SettingsCommandMetadataV1 metadata)
    {
        if (previous.ActorId != metadata.ActorId || previous.RequestHash != hash) return Result(409, SettingsDocumentNumberSeriesProblemCodesV1.IdempotencyConflict, metadata);
        var answer = JsonSerializer.Deserialize<CompanyPolicyCommandAnswerV1>(previous.ResponseJson, Json);
        if (answer is null || answer.StatusCode != previous.HttpStatusCode || answer.StatusCode is not (204 or 409)) throw new InvalidOperationException("Stored number-format result is invalid.");
        return new(answer, true, previous.OriginalCorrelationId);
    }
}
