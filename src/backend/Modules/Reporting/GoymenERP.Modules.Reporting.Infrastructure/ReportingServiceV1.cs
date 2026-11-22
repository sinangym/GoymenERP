using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Reporting.Domain;
using GoymenERP.Modules.Reporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Reporting.Infrastructure;

/// <summary>
/// WP14 report centre (DALGA_PLANI_v2 1.15, YOL-D13, ROL-D13, ROL-D15). Every number comes from an OWNER read port through a
/// host adapter and is computed here, never on the screen; Reporting reads no other module's table. Every read names the
/// server-resolved company and is fenced: each required permission is measured fresh (company-wide) before the first read and
/// again before anything is released. Exports and auditor packages write one append-only export record and its audit event in
/// one transaction before the bytes leave. Saved views follow the critical command skeleton (idempotency key per actor,
/// request-hash replay, expected version, durable refusals, correlation/causation, audit, one SaveChanges).
/// </summary>
public sealed class ReportingServiceV1(
    ReportingDbContext database,
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IReportingStockAgingSourceV1 stockAging,
    IReportingOtifSourceV1 otif,
    IReportingRecallSourceV1 recall,
    IReportingIntercompanySourceV1 intercompany,
    IReportingTrialBalanceSourceV1 trialBalance,
    IAuditEventCollector audit,
    ICorrelationContext correlation,
    TimeProvider time) : IReportingServiceV1
{
    internal const string BusinessTimeZoneId = "Europe/Istanbul";
    internal const int AuditorStockPageLimit = 20;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- Catalogue -----------------------------------------------------------------------------------------------------

    public async Task<ReportingCatalogResponseV1> ListReportsAsync(CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        var held = new Dictionary<string, bool>(StringComparer.Ordinal);
        async Task<bool> Holds(string permission)
        {
            if (!held.TryGetValue(permission, out var granted))
            {
                granted = await HoldsAsync(actor, permission, cancellationToken);
                held[permission] = granted;
            }

            return granted;
        }

        var canExport = await Holds(ReportingPermissionManifestV1.Export);
        var reports = new List<ReportingReportViewV1>();
        foreach (var report in ReportingCatalogV1.Reports)
        {
            var visible = true;
            foreach (var permission in report.RequiredPermissions) visible &= await Holds(permission);
            if (visible) reports.Add(View(report, canExport));
        }

        var canPackage = await Holds(ReportingPermissionManifestV1.AuditorPackageExport);
        RequireStill(actor);
        return new ReportingCatalogResponseV1(actor.CompanyId, reports, canPackage);
    }

    private static ReportingReportViewV1 View(ReportingReportDefinitionV1 report, bool canExport) => new(report.Id, report.OwnerModule,
        report.Parameters.Select(parameter => new ReportingParameterViewV1(parameter.Id, Kebab(parameter.Kind.ToString()), parameter.Required,
            parameter.Choices, parameter.Minimum?.ToString(CultureInfo.InvariantCulture), parameter.Maximum?.ToString(CultureInfo.InvariantCulture))).ToArray(),
        report.Columns.Select(column => new ReportingColumnViewV1(column.Id, Kebab(column.Kind.ToString()))).ToArray(), canExport);

    private static string Kebab(string pascal) => string.Concat(pascal.Select((character, index) =>
        char.IsUpper(character) ? (index == 0 ? "" : "-") + char.ToLowerInvariant(character) : character.ToString()));

    // ---- Run -----------------------------------------------------------------------------------------------------------

    public async Task<ReportingTableV1> RunAsync(string reportId, IReadOnlyDictionary<string, string?> parameters, int? page, int? pageSize,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        var report = ReportingCatalogV1.Find(reportId) ?? throw new ReportingException(404, "not_found");
        var scope = await RequireAllAsync(actor, report.RequiredPermissions, cancellationToken);
        var (pageNumber, size) = Page(page, pageSize, ReportingCatalogV1.MaximumPageSize);
        var set = ReportingParameterSetV1.Parse(report, parameters);
        var table = await BuildAsync(actor, report, set, pageNumber, size, cancellationToken);
        await RequireAllAgainAsync(scope, report.RequiredPermissions, cancellationToken);
        return table;
    }

    private static (int Page, int PageSize) Page(int? page, int? pageSize, int maximum)
    {
        var number = page ?? 1;
        var size = pageSize ?? ReportingCatalogV1.DefaultPageSize;
        if (number < 1 || number > 100_000 || size < 1 || size > maximum) throw new ReportingException(400, ReportingProblemCodesV1.PageInvalid);
        return (number, size);
    }

    private async Task<ReportingTableV1> BuildAsync(Actor actor, ReportingReportDefinitionV1 report, ReportingParameterSetV1 set, int page,
        int pageSize, CancellationToken cancellationToken) => report.Id switch
        {
            ReportingCatalogV1.StockAging => await StockAgingAsync(actor, report, set, page, pageSize, cancellationToken),
            ReportingCatalogV1.Otif => await OtifAsync(actor, report, set, page, pageSize, cancellationToken),
            ReportingCatalogV1.Recall => await RecallAsync(actor, report, set, page, pageSize, cancellationToken),
            ReportingCatalogV1.Intercompany => await IntercompanyAsync(actor, report, set, page, pageSize, cancellationToken),
            _ => throw new ReportingException(404, "not_found"),
        };

    private static void RequireFound(ReportingSourceOutcomeV1 outcome, Guid answeredCompany, Actor actor)
    {
        switch (outcome)
        {
            case ReportingSourceOutcomeV1.Found when answeredCompany == actor.CompanyId:
                return;
            case ReportingSourceOutcomeV1.Found:
                throw new ReportingException(503, ReportingProblemCodesV1.SourceUnavailable);
            case ReportingSourceOutcomeV1.Denied:
                throw new ReportingException(403, "forbidden");
            case ReportingSourceOutcomeV1.NotConnected:
                throw new ReportingException(503, ReportingProblemCodesV1.SourceNotConnected);
            case ReportingSourceOutcomeV1.NotFound:
                throw new ReportingException(404, "not_found");
            default:
                throw new ReportingException(503, ReportingProblemCodesV1.SourceUnavailable);
        }
    }

    private static TimeZoneInfo BusinessZone() =>
        TimeZoneInfo.TryFindSystemTimeZoneById(BusinessTimeZoneId, out var zone)
            ? zone
            : throw new ReportingException(503, ReportingProblemCodesV1.TimeZoneUnavailable);

    internal static DateOnly BusinessDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    private static string Number(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static decimal OwnerNumber(string text) =>
        ReportingFileWritersV1.IsInvariantNumber(text) && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ReportingException(503, ReportingProblemCodesV1.SourceUnavailable);

    private async Task<ReportingTableV1> StockAgingAsync(Actor actor, ReportingReportDefinitionV1 report, ReportingParameterSetV1 set, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        var zone = BusinessZone();
        var asOf = set.Date("asOf");
        var basis = set.Choice("ageBasis");
        var boundaries = set.WholeNumbers("bucketDays");
        var answer = await stockAging.ReadAsync(actor.CompanyId, page, pageSize, set.OptionalGuid("productId"), set.OptionalGuid("locationId"),
            cancellationToken);
        RequireStill(actor);
        RequireFound(answer.Outcome, answer.CompanyId, actor);
        if (answer.Items.Count > pageSize) throw new ReportingException(503, ReportingProblemCodesV1.SourceUnavailable);
        var bucketKg = ReportingStockAgingCalculatorV1.Buckets(boundaries).ToDictionary(name => name, _ => 0m, StringComparer.Ordinal);
        decimal unknownKg = 0, totalKg = 0;
        var rows = new List<IReadOnlyList<string?>>(answer.Items.Count);
        foreach (var item in answer.Items)
        {
            var kg = OwnerNumber(item.PhysicalKg);
            if (kg <= 0) throw new ReportingException(503, ReportingProblemCodesV1.SourceUnavailable);
            totalKg += kg;
            DateTimeOffset? entry = basis == ReportingCatalogV1.AgeBasisLabelCreated ? item.LabelCreatedAtUtc : item.FirstIncomingAtUtc;
            string? entryText = null, ageText = null, bucket = null;
            if (entry is { } instant)
            {
                var entryDate = BusinessDate(instant, zone);
                var age = ReportingStockAgingCalculatorV1.AgeDays(entryDate, asOf);
                bucket = ReportingStockAgingCalculatorV1.Bucket(age, boundaries);
                entryText = DateText(entryDate);
                ageText = age.ToString(CultureInfo.InvariantCulture);
                bucketKg[bucket] += kg;
            }
            else
            {
                // No incoming movement: the age on this basis is unknown, never guessed from another date.
                unknownKg += kg;
            }

            rows.Add([item.LabelCode, item.LabelId.ToString("D"), item.ProductId.ToString("D"), item.LocationId.ToString("D"), item.HeatNumber,
                item.CoilNumber, item.PhysicalKg, entryText, ageText, bucket]);
        }

        var summary = new List<ReportingSummaryItemV1> { new("pageTotalKg", Number(totalKg)), new("unknownAgeKg", Number(unknownKg)) };
        summary.AddRange(bucketKg.Select(pair => new ReportingSummaryItemV1("bucketKg:" + pair.Key, Number(pair.Value))));
        return Table(actor, report, set, rows, page, pageSize, answer.HasMore, summary, []);
    }

    private async Task<ReportingTableV1> OtifAsync(Actor actor, ReportingReportDefinitionV1 report, ReportingParameterSetV1 set, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        var from = set.Date("from");
        var to = set.Date("to");
        var toleranceDays = set.WholeNumber("onTimeToleranceDays");
        var tolerancePercent = set.Decimal("inFullTolerancePercent");
        var answer = await otif.ReadAsync(actor.CompanyId, from, to, page, pageSize, cancellationToken);
        RequireStill(actor);
        RequireFound(answer.Outcome, answer.CompanyId, actor);
        if (answer.Lines.Count > pageSize) throw new ReportingException(503, ReportingProblemCodesV1.SourceUnavailable);
        int onTime = 0, inFull = 0, both = 0;
        var rows = new List<IReadOnlyList<string?>>(answer.Lines.Count);
        foreach (var line in answer.Lines)
        {
            if (line.OrderedQuantity <= 0 || line.RequestedDate < from || line.RequestedDate > to
                || line.Shipments.Any(shipment => shipment.Quantity <= 0))
                throw new ReportingException(503, ReportingProblemCodesV1.SourceUnavailable);
            var result = ReportingOtifCalculatorV1.Evaluate(line.RequestedDate, line.OrderedQuantity, line.Shipments, toleranceDays, tolerancePercent);
            onTime += result.OnTime ? 1 : 0;
            inFull += result.InFull ? 1 : 0;
            both += result.Otif ? 1 : 0;
            rows.Add([line.OrderNumber, line.OrderId.ToString("D"), line.LineNumber.ToString(CultureInfo.InvariantCulture),
                line.CustomerId.ToString("D"), line.UnitCode, DateText(line.RequestedDate), Number(line.OrderedQuantity),
                Number(result.ShippedQuantity), result.InFullDate is { } day ? DateText(day) : null, Flag(result.OnTime), Flag(result.InFull),
                Flag(result.Otif)]);
        }

        var summary = new List<ReportingSummaryItemV1>
        {
            new("pageLines", rows.Count.ToString(CultureInfo.InvariantCulture)),
            new("onTimeLines", onTime.ToString(CultureInfo.InvariantCulture)),
            new("inFullLines", inFull.ToString(CultureInfo.InvariantCulture)),
            new("otifLines", both.ToString(CultureInfo.InvariantCulture)),
        };
        if (ReportingOtifCalculatorV1.Percent(both, rows.Count) is { } percent) summary.Add(new("otifPercent", percent));
        return Table(actor, report, set, rows, page, pageSize, answer.HasMore, summary, []);
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private async Task<ReportingTableV1> RecallAsync(Actor actor, ReportingReportDefinitionV1 report, ReportingParameterSetV1 set, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        var answer = await recall.ReadAsync(actor.CompanyId, set.Guid("labelId"), set.Choice("direction") == ReportingCatalogV1.DirectionForward,
            cancellationToken);
        RequireStill(actor);
        RequireFound(answer.Outcome, answer.CompanyId, actor);
        var all = answer.Rows.OrderBy(row => row.OccurredAtUtc).ThenBy(row => row.LabelCode, StringComparer.Ordinal).ToArray();
        foreach (var row in all) _ = OwnerNumber(row.QuantityKg);
        var skip = (long)(page - 1) * pageSize;
        var rows = all.Skip((int)Math.Min(skip, int.MaxValue)).Take(pageSize).Select(row => (IReadOnlyList<string?>)
        [
            row.LabelCode, row.LabelId.ToString("D"), row.Step, row.OccurredAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            row.QuantityKg, row.PartyRole, row.PartyCode, row.PartyName, row.SourceKind, row.SourceId.ToString("D"),
        ]).ToArray();
        var customers = all.Where(row => row.PartyRole == "Customer" && row.PartyCode is not null).Select(row => row.PartyCode!).Distinct().Count();
        var summary = new List<ReportingSummaryItemV1>
        {
            new("events", all.Length.ToString(CultureInfo.InvariantCulture)),
            new("labels", all.Select(row => row.LabelId).Distinct().Count().ToString(CultureInfo.InvariantCulture)),
            new("customers", customers.ToString(CultureInfo.InvariantCulture)),
        };
        var statuses = answer.Statuses.Select(pair => new ReportingSummaryItemV1(pair.Key, pair.Value)).ToList();
        statuses.Add(new("windowFull", Flag(answer.WindowFull)));
        return Table(actor, report, set, rows, page, pageSize, skip + rows.Length < all.Length, summary, statuses);
    }

    private async Task<ReportingTableV1> IntercompanyAsync(Actor actor, ReportingReportDefinitionV1 report, ReportingParameterSetV1 set,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var answer = await intercompany.ReadAsync(actor.CompanyId, set.Date("from"), set.Date("to"), page, pageSize, cancellationToken);
        RequireStill(actor);
        RequireFound(answer.Outcome, answer.CompanyId, actor);
        if (answer.Rows.Count > pageSize || answer.Rows.Any(row => row.CounterpartyCompanyId == actor.CompanyId))
            throw new ReportingException(503, ReportingProblemCodesV1.SourceUnavailable);
        var rows = answer.Rows.Select(row => (IReadOnlyList<string?>)
        [
            DateText(row.DocumentDate), row.DocumentKind, row.DocumentNumber, row.CounterpartyCompanyId.ToString("D"), row.CurrencyCode,
            Number(OwnerNumber(row.NetAmount)),
        ]).ToArray();
        return Table(actor, report, set, rows, page, pageSize, answer.HasMore,
            [new("pageRows", rows.Length.ToString(CultureInfo.InvariantCulture))], []);
    }

    private ReportingTableV1 Table(Actor actor, ReportingReportDefinitionV1 report, ReportingParameterSetV1 set,
        IReadOnlyList<IReadOnlyList<string?>> rows, int page, int pageSize, bool hasMore, IReadOnlyList<ReportingSummaryItemV1> summary,
        IReadOnlyList<ReportingSummaryItemV1> statuses) =>
        new(actor.CompanyId, report.Id, set.Canonical, report.Columns.Select(column => column.Id).ToArray(), rows, page, pageSize, hasMore,
            summary, statuses, time.GetUtcNow());

    // ---- Export --------------------------------------------------------------------------------------------------------

    public async Task<ReportingFileV1> ExportAsync(string reportId, ReportingExportRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = RequireActor();
        var report = ReportingCatalogV1.Find(reportId) ?? throw new ReportingException(404, "not_found");
        var permissions = report.RequiredPermissions.Append(ReportingPermissionManifestV1.Export).Distinct(StringComparer.Ordinal).ToArray();
        var scope = await RequireAllAsync(actor, permissions, cancellationToken, auditPermission: ReportingPermissionManifestV1.Export);
        var format = request.Format switch
        {
            ReportingApiV1.FormatCsv => ReportingApiV1.FormatCsv,
            ReportingApiV1.FormatXlsx => ReportingApiV1.FormatXlsx,
            _ => throw new ReportingException(400, ReportingProblemCodesV1.FormatInvalid),
        };
        var (page, size) = Page(request.Page, request.PageSize, ReportingCatalogV1.MaximumExportPageSize);
        var set = ReportingParameterSetV1.Parse(report, request.Parameters);
        var columns = Columns(report, request.Columns, 400, ReportingProblemCodesV1.ParameterInvalid);
        var table = await BuildAsync(actor, report, set, page, size, cancellationToken);
        var indexes = columns.Select(column => table.Columns.ToList().IndexOf(column.Id)).ToArray();
        var rows = table.Rows.Select(row => (IReadOnlyList<string?>)indexes.Select(index => row[index]).ToArray()).ToArray();
        var content = format == ReportingApiV1.FormatCsv
            ? ReportingFileWritersV1.Csv(columns, rows)
            : ReportingFileWritersV1.Xlsx(report.Id, columns, rows);
        await RequireAllAgainAsync(scope, permissions, cancellationToken);
        var fileName = string.Create(CultureInfo.InvariantCulture,
            $"{report.Id}-{time.GetUtcNow():yyyyMMdd'T'HHmmss'Z'}-p{page}.{format}");
        var exportId = await RecordExportAsync(scope, ReportingExportKindsV1.ReportPage, report.Id, format, set.Canonical,
            columns.Select(column => column.Id).ToArray(), page, size, rows.Length, content, cancellationToken);
        return new ReportingFileV1(exportId, fileName, format == ReportingApiV1.FormatCsv
                ? "text/csv; charset=utf-8"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content, ReportingFileWritersV1.Sha256(content), rows.Length);
    }

    private static IReadOnlyList<ReportingSheetColumnV1> Columns(ReportingReportDefinitionV1 report, IReadOnlyList<string>? requested, int status,
        string code)
    {
        if (requested is null || requested.Count == 0)
            return report.Columns.Select(column => new ReportingSheetColumnV1(column.Id, column.Kind)).ToArray();
        if (requested.Distinct(StringComparer.Ordinal).Count() != requested.Count) throw new ReportingException(status, code, "columns");
        return requested.Select(id => report.Columns.FirstOrDefault(column => string.Equals(column.Id, id, StringComparison.Ordinal))
                is { } column ? new ReportingSheetColumnV1(column.Id, column.Kind) : throw new ReportingException(status, code, "columns"))
            .ToArray();
    }

    private async Task<Guid> RecordExportAsync(Scope scope, int kind, string reportId, string format, IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string> columns, int? page, int? pageSize, int rowCount, byte[] content, CancellationToken cancellationToken)
    {
        var correlationId = correlation.IsResolved ? correlation.CorrelationId : Guid.NewGuid().ToString("N");
        var causationId = correlation.IsResolved ? correlation.CausationId : null;
        var now = time.GetUtcNow();
        var row = new ReportingExportRow
        {
            CompanyId = scope.CompanyId, Id = Guid.NewGuid(), ActorUserId = scope.ActorUserId, Kind = kind, ReportId = reportId,
            Format = format, ParametersJson = JsonSerializer.Serialize(parameters, Json), ColumnsJson = JsonSerializer.Serialize(columns, Json),
            Page = page, PageSize = pageSize, RowCount = rowCount, ByteLength = content.Length,
            ContentSha256 = ReportingFileWritersV1.Sha256(content), RecordedAtUtc = now, CorrelationId = correlationId,
            CausationId = causationId,
        };
        database.Exports.Add(row);
        var after = JsonSerializer.Serialize(new
        {
            reportId, format, parameters, columns, page, pageSize, rowCount, byteLength = content.Length, contentSha256 = row.ContentSha256,
        }, Json);
        audit.Enqueue(new AuditEventDraft(scope.CompanyId, kind == ReportingExportKindsV1.AuditorPackage
                ? "reporting.auditor-package.exported.v1"
                : "reporting.report.exported.v1", "Reporting.Export", row.Id.ToString("D"), "Export", null, 1, AuditActorTypes.User,
            scope.ActorId, scope.AuditPermission, scope.RoleCodes, "{}", after,
            kind == ReportingExportKindsV1.AuditorPackage ? "Denetçi paketi dışa aktarıldı." : "Rapor dışa aktarıldı.", null,
            AuditAiInvolvement.None, null, correlation.IsResolved ? correlation.SourceChannel : "api",
            correlation.IsResolved ? correlation.ClientIpAddress : null, correlation.IsResolved ? correlation.UserAgent : null,
            correlationId, causationId, "reporting-export:" + row.Id.ToString("D")));
        RequireStill(scope);
        try
        {
            if (database.Database.IsRelational())
            {
                await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
                RequireStill(scope);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await database.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateException)
        {
            // Without its record nothing leaves the system.
            throw new ReportingException(503, ReportingProblemCodesV1.SourceUnavailable);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }

        return row.Id;
    }

    // ---- Auditor package (YOL-D13, ROL-D13) ------------------------------------------------------------------------------

    private sealed record Section(string Id, string Status, int RowCount, string? FileName, string? Sha256, string? SourceVersion, bool Complete);

    public async Task<ReportingFileV1> BuildAuditorPackageAsync(ReportingAuditorPackageRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = RequireActor();
        var scope = await RequireAllAsync(actor, [ReportingPermissionManifestV1.AuditorPackageExport], cancellationToken);
        var period = ReportingParameterSetV1.Parse(PeriodDefinition, new Dictionary<string, string?> { ["from"] = request.From, ["to"] = request.To });
        var from = period.Date("from");
        var to = period.Date("to");
        var sections = new List<Section>();
        var files = new List<(string Name, byte[] Content)>();

        // Mizan: the Accounting owner's trial balance, under Accounting's own read authority as well.
        if (await HoldsAsync(actor, ErpPermissions.AccountingJournalsRead, cancellationToken))
        {
            var answer = await trialBalance.ReadAsync(actor.CompanyId, from, to, cancellationToken);
            RequireStill(actor);
            if (answer.Outcome == ReportingSourceOutcomeV1.Found && answer.CompanyId == actor.CompanyId)
            {
                var columns = new[] { "accountId", "currencyId", "debit", "credit", "balanceSide", "balance" }
                    .Select(id => new ReportingSheetColumnV1(id, id is "debit" or "credit" or "balance" ? ReportingColumnKindV1.Number : ReportingColumnKindV1.Text))
                    .ToArray();
                var rows = answer.Rows.Select(row => (IReadOnlyList<string?>)[row.AccountId.ToString("D"), row.CurrencyId.ToString("D"),
                    row.Debit, row.Credit, row.BalanceSide, row.Balance]).ToArray();
                AddSection(sections, files, "trial-balance", columns, rows, answer.SourceVersion, true);
            }
            else
            {
                sections.Add(new Section("trial-balance", Status(answer.Outcome), 0, null, null, null, false));
            }
        }
        else
        {
            sections.Add(new Section("trial-balance", "forbidden", 0, null, null, null, false));
        }

        // Stock: current physical positions with the label's entry date (no bucket or threshold is chosen for the auditor).
        if (await HoldsAsync(actor, ErpPermissions.InventoryLabelsRead, cancellationToken)
            && await HoldsAsync(actor, ErpPermissions.InventoryBalancesRead, cancellationToken))
        {
            var zone = BusinessZone();
            var rows = new List<IReadOnlyList<string?>>();
            var status = "included";
            var complete = false;
            for (var page = 1; page <= AuditorStockPageLimit; page++)
            {
                var answer = await stockAging.ReadAsync(actor.CompanyId, page, ReportingCatalogV1.MaximumPageSize, null, null, cancellationToken);
                RequireStill(actor);
                if (answer.Outcome != ReportingSourceOutcomeV1.Found || answer.CompanyId != actor.CompanyId)
                {
                    status = Status(answer.Outcome);
                    rows.Clear();
                    break;
                }

                rows.AddRange(answer.Items.Select(item => (IReadOnlyList<string?>)[item.LabelCode, item.LabelId.ToString("D"),
                    item.ProductId.ToString("D"), item.LocationId.ToString("D"), item.HeatNumber, item.CoilNumber, item.PhysicalKg,
                    DateText(BusinessDate(item.LabelCreatedAtUtc, zone))]));
                if (!answer.HasMore)
                {
                    complete = true;
                    break;
                }
            }

            if (status == "included")
            {
                var columns = new[] { "labelCode", "labelId", "productId", "locationId", "heatNumber", "coilNumber", "physicalKg", "entryDate" }
                    .Select(id => new ReportingSheetColumnV1(id, id == "physicalKg" ? ReportingColumnKindV1.Number : ReportingColumnKindV1.Text)).ToArray();
                // A cut list is never presented as the whole stock: the manifest says complete=false.
                AddSection(sections, files, "stock-positions", columns, rows, null, complete);
            }
            else
            {
                sections.Add(new Section("stock-positions", status, 0, null, null, null, false));
            }
        }
        else
        {
            sections.Add(new Section("stock-positions", "forbidden", 0, null, null, null, false));
        }

        // Intercompany legs (ROL-D15): only for a holder of the separate permission.
        if (await HoldsAsync(actor, ReportingPermissionManifestV1.IntercompanyRead, cancellationToken))
        {
            var answer = await intercompany.ReadAsync(actor.CompanyId, from, to, 1, ReportingCatalogV1.MaximumExportPageSize, cancellationToken);
            RequireStill(actor);
            if (answer.Outcome == ReportingSourceOutcomeV1.Found && answer.CompanyId == actor.CompanyId)
            {
                var report = ReportingCatalogV1.Find(ReportingCatalogV1.Intercompany)!;
                var rows = answer.Rows.Select(row => (IReadOnlyList<string?>)[DateText(row.DocumentDate), row.DocumentKind, row.DocumentNumber,
                    row.CounterpartyCompanyId.ToString("D"), row.CurrencyCode, row.NetAmount]).ToArray();
                AddSection(sections, files, "intercompany", report.Columns.Select(column => new ReportingSheetColumnV1(column.Id, column.Kind)).ToArray(),
                    rows, null, !answer.HasMore);
            }
            else
            {
                sections.Add(new Section("intercompany", Status(answer.Outcome), 0, null, null, null, false));
            }
        }
        else
        {
            sections.Add(new Section("intercompany", "forbidden", 0, null, null, null, false));
        }

        // YOL-D13 names these too; no owner port is connected to the centre yet, so they are listed, never fabricated.
        foreach (var pending in new[] { "counterparty-balances", "cost", "document-trail" })
            sections.Add(new Section(pending, "not-connected", 0, null, null, null, false));

        await RequireAllAgainAsync(scope, [ReportingPermissionManifestV1.AuditorPackageExport], cancellationToken);
        var generatedAt = time.GetUtcNow();
        var manifest = JsonSerializer.Serialize(new
        {
            schema = "goymen-erp.auditor-package.v1",
            companyId = actor.CompanyId,
            from = DateText(from),
            to = DateText(to),
            generatedAtUtc = generatedAt,
            generatedBy = actor.ActorId,
            accessNotice = "Salt okuma dışa aktarım. Denetçiye sistem erişimi verilmez (ROL-D13).",
            sections = sections.Select(section => new
            {
                id = section.Id, status = section.Status, rowCount = section.RowCount, fileName = section.FileName, sha256 = section.Sha256,
                sourceVersion = section.SourceVersion, complete = section.Complete,
            }),
        }, new JsonSerializerOptions(Json) { WriteIndented = true });
        var content = ReportingFileWritersV1.ZipBytes([("manifest.json", Encoding.UTF8.GetBytes(manifest)), .. files]);
        var fileName = string.Create(CultureInfo.InvariantCulture, $"denetci-paketi-{DateText(from)}-{DateText(to)}.zip");
        var exportId = await RecordExportAsync(scope, ReportingExportKindsV1.AuditorPackage, ReportingExportKindsV1.AuditorPackageReportId, "zip",
            period.Canonical, sections.Select(section => section.Id + ":" + section.Status).ToArray(), null, null,
            sections.Sum(section => section.RowCount), content, cancellationToken);
        return new ReportingFileV1(exportId, fileName, "application/zip", content, ReportingFileWritersV1.Sha256(content),
            sections.Sum(section => section.RowCount));
    }

    private static readonly ReportingReportDefinitionV1 PeriodDefinition = new("auditor-package", "reporting", [],
        [new("from", ReportingParameterKindV1.Date, true, [], null, null), new("to", ReportingParameterKindV1.Date, true, [], null, null)], []);

    private static string Status(ReportingSourceOutcomeV1 outcome) => outcome switch
    {
        ReportingSourceOutcomeV1.NotConnected => "not-connected",
        ReportingSourceOutcomeV1.Denied => "forbidden",
        _ => "unavailable",
    };

    private static void AddSection(List<Section> sections, List<(string Name, byte[] Content)> files, string id,
        IReadOnlyList<ReportingSheetColumnV1> columns, IReadOnlyList<IReadOnlyList<string?>> rows, string? sourceVersion, bool complete)
    {
        var bytes = ReportingFileWritersV1.Csv(columns, rows);
        var fileName = "sections/" + id + ".csv";
        files.Add((fileName, bytes));
        sections.Add(new Section(id, "included", rows.Count, fileName, ReportingFileWritersV1.Sha256(bytes), sourceVersion, complete));
    }

    // ---- Saved views ---------------------------------------------------------------------------------------------------

    internal static class Commands
    {
        public const string Create = "reporting.saved-view.create";
        public const string Update = "reporting.saved-view.update";
        public const string Passivate = "reporting.saved-view.passivate";
    }

    public async Task<ReportingSavedViewPageV1> ListSavedViewsAsync(string reportId, CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        var report = ReportingCatalogV1.Find(reportId) ?? throw new ReportingException(404, "not_found");
        var scope = await RequireAllAsync(actor, report.RequiredPermissions, cancellationToken);
        var rows = await database.SavedViews.AsNoTracking()
            .Where(row => row.CompanyId == actor.CompanyId && row.OwnerUserId == actor.ActorUserId && row.ReportId == report.Id && row.IsActive)
            .OrderBy(row => row.Name).ThenBy(row => row.Id)
            .Take(ReportingSavedViewLimitsV1.MaximumActivePerReport).ToArrayAsync(cancellationToken);
        await RequireAllAgainAsync(scope, report.RequiredPermissions, cancellationToken);
        return new ReportingSavedViewPageV1(actor.CompanyId, report.Id, rows.Select(ViewOf).ToArray());
    }

    public async Task<ReportingSavedViewResponseV1> CreateSavedViewAsync(ReportingSavedViewRequestV1 request, ReportingCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var actor = RequireActor();
        var report = ReportingCatalogV1.Find(request.ReportId) ?? throw new ReportingException(404, "not_found");
        var scope = await RequireAllAsync(actor, report.RequiredPermissions, cancellationToken);
        var requestHash = Hash(Commands.Create, report.Id, request.Name, Sorted(request.Parameters), request.Columns);
        var replay = await FindReplayAsync(scope, metadata, Commands.Create, requestHash, cancellationToken);
        if (replay is not null) return new(await SavedViewAsync(scope, replay.TargetId, cancellationToken), true, replay.CorrelationId);
        var (name, parameters, columns) = ValidateView(report, request);
        var activeCount = await database.SavedViews.AsNoTracking().CountAsync(row => row.CompanyId == scope.CompanyId
            && row.OwnerUserId == scope.ActorUserId && row.ReportId == report.Id && row.IsActive, cancellationToken);
        if (activeCount >= ReportingSavedViewLimitsV1.MaximumActivePerReport) throw new ReportingException(409, ReportingProblemCodesV1.SavedViewLimit);
        if (await NameTakenAsync(scope, report.Id, name, null, cancellationToken))
            throw new ReportingException(409, ReportingProblemCodesV1.SavedViewNameTaken, "name");
        await RequireAllAgainAsync(scope, report.RequiredPermissions, cancellationToken);
        var now = time.GetUtcNow();
        var row = new ReportingSavedViewRow
        {
            CompanyId = scope.CompanyId, Id = Guid.NewGuid(), OwnerUserId = scope.ActorUserId, ReportId = report.Id, Name = name,
            ParametersJson = JsonSerializer.Serialize(parameters, Json), ColumnsJson = JsonSerializer.Serialize(columns, Json), Version = 1,
            IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        database.SavedViews.Add(row);
        AddAttempt(scope, metadata, Commands.Create, requestHash, row.Id, 201, null, now);
        AuditView(scope, metadata, "reporting.saved-view.created.v1", row.Id, "CreateSavedView", null, 1, "{}", ViewAudit(row), "Kayıtlı görünüm oluşturuldu.");
        var saved = await SaveAsync(scope, metadata, Commands.Create, requestHash, cancellationToken);
        if (saved is not null) return new(await SavedViewAsync(scope, saved.TargetId, cancellationToken), true, saved.CorrelationId);
        return new(await SavedViewAsync(scope, row.Id, cancellationToken), false, metadata.CorrelationId);
    }

    public async Task<ReportingSavedViewResponseV1> UpdateSavedViewAsync(Guid viewId, ReportingSavedViewRequestV1 request,
        ReportingCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var actor = RequireActor();
        var requestHash = Hash(Commands.Update, viewId, request.ReportId, request.Name, Sorted(request.Parameters), request.Columns, request.ExpectedVersion);
        return await MutateAsync(actor, viewId, request.ExpectedVersion, Commands.Update, requestHash, metadata, (scope, report, row) =>
        {
            if (!string.Equals(request.ReportId, row.ReportId, StringComparison.Ordinal))
                throw new ReportingException(422, ReportingProblemCodesV1.SavedViewInvalid, "reportId");
            var (name, parameters, columns) = ValidateView(report, request);
            return (row2 =>
            {
                row2.Name = name;
                row2.ParametersJson = JsonSerializer.Serialize(parameters, Json);
                row2.ColumnsJson = JsonSerializer.Serialize(columns, Json);
            }, name, "reporting.saved-view.updated.v1", "UpdateSavedView", "Kayıtlı görünüm güncellendi.");
        }, cancellationToken);
    }

    public async Task<ReportingSavedViewResponseV1> PassivateSavedViewAsync(Guid viewId, ReportingSavedViewPassivateRequestV1 request,
        ReportingCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var actor = RequireActor();
        var requestHash = Hash(Commands.Passivate, viewId, request.ExpectedVersion);
        return await MutateAsync(actor, viewId, request.ExpectedVersion, Commands.Passivate, requestHash, metadata, (_, _, _) =>
            (row => row.IsActive = false, null, "reporting.saved-view.passivated.v1", "PassivateSavedView", "Kayıtlı görünüm pasifleştirildi."),
            cancellationToken);
    }

    private delegate (Action<ReportingSavedViewRow> Apply, string? NewName, string EventType, string Operation, string Reason) Mutation(
        Scope scope, ReportingReportDefinitionV1 report, ReportingSavedViewRow row);

    private async Task<ReportingSavedViewResponseV1> MutateAsync(Actor actor, Guid viewId, string? expectedVersion, string command, string requestHash,
        ReportingCommandMetadataV1 metadata, Mutation mutation, CancellationToken cancellationToken)
    {
        // The view's own report decides the permissions; an unknown or foreign view answers like no view at all.
        var owned = viewId == Guid.Empty ? null : await database.SavedViews.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == actor.CompanyId && row.Id == viewId && row.OwnerUserId == actor.ActorUserId, cancellationToken);
        RequireStill(actor);
        var report = owned is null ? null : ReportingCatalogV1.Find(owned.ReportId);
        if (owned is null || report is null) throw new ReportingException(404, "not_found");
        var scope = await RequireAllAsync(actor, report.RequiredPermissions, cancellationToken);
        var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
        if (replay is not null) return new(await SavedViewAsync(scope, replay.TargetId, cancellationToken), true, replay.CorrelationId);
        var version = ParseVersion(expectedVersion);
        var row = await database.SavedViews.SingleOrDefaultAsync(item => item.CompanyId == scope.CompanyId && item.Id == viewId
            && item.OwnerUserId == scope.ActorUserId, cancellationToken) ?? throw new ReportingException(404, "not_found");
        if (!row.IsActive) throw await TerminalAsync(scope, metadata, command, requestHash, viewId, ReportingProblemCodesV1.SavedViewPassive, cancellationToken);
        if (row.Version != version)
            throw await TerminalAsync(scope, metadata, command, requestHash, viewId, ReportingProblemCodesV1.VersionMismatch, cancellationToken);
        var (apply, newName, eventType, operation, reason) = mutation(scope, report, row);
        if (newName is not null && await NameTakenAsync(scope, row.ReportId, newName, row.Id, cancellationToken))
            throw new ReportingException(409, ReportingProblemCodesV1.SavedViewNameTaken, "name");
        await RequireAllAgainAsync(scope, report.RequiredPermissions, cancellationToken);
        var before = ViewAudit(row);
        apply(row);
        row.Version = checked(row.Version + 1);
        row.UpdatedAtUtc = time.GetUtcNow();
        AddAttempt(scope, metadata, command, requestHash, row.Id, 200, null, row.UpdatedAtUtc);
        AuditView(scope, metadata, eventType, row.Id, operation, row.Version - 1, row.Version, before, ViewAudit(row), reason);
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        if (saved is not null) return new(await SavedViewAsync(scope, saved.TargetId, cancellationToken), true, saved.CorrelationId);
        return new(await SavedViewAsync(scope, viewId, cancellationToken), false, metadata.CorrelationId);
    }

    private static (string Name, IReadOnlyDictionary<string, string> Parameters, IReadOnlyList<string> Columns) ValidateView(
        ReportingReportDefinitionV1 report, ReportingSavedViewRequestV1 request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > ReportingSavedViewLimitsV1.NameMaximumLength || name.Any(char.IsControl))
            throw new ReportingException(422, ReportingProblemCodesV1.SavedViewInvalid, "name");
        ReportingParameterSetV1 parameters;
        try
        {
            parameters = ReportingParameterSetV1.ParsePartial(report, request.Parameters);
        }
        catch (ReportingException exception)
        {
            throw new ReportingException(422, ReportingProblemCodesV1.SavedViewInvalid, exception.Field);
        }

        if (request.Columns is null || request.Columns.Count == 0) throw new ReportingException(422, ReportingProblemCodesV1.SavedViewInvalid, "columns");
        var columns = Columns(report, request.Columns, 422, ReportingProblemCodesV1.SavedViewInvalid).Select(column => column.Id).ToArray();
        return (name, parameters.Canonical, columns);
    }

    private Task<bool> NameTakenAsync(Scope scope, string reportId, string name, Guid? except, CancellationToken cancellationToken) =>
        database.SavedViews.AsNoTracking().AnyAsync(row => row.CompanyId == scope.CompanyId && row.OwnerUserId == scope.ActorUserId
            && row.ReportId == reportId && row.IsActive && row.Name == name && (except == null || row.Id != except), cancellationToken);

    private static ReportingSavedViewV1 ViewOf(ReportingSavedViewRow row) => new(row.Id, row.CompanyId, row.ReportId, row.Name,
        JsonSerializer.Deserialize<Dictionary<string, string>>(row.ParametersJson, Json) ?? new Dictionary<string, string>(),
        JsonSerializer.Deserialize<string[]>(row.ColumnsJson, Json) ?? [], row.Version.ToString(CultureInfo.InvariantCulture), row.IsActive,
        row.CreatedAtUtc, row.UpdatedAtUtc);

    private async Task<ReportingSavedViewV1> SavedViewAsync(Scope scope, Guid viewId, CancellationToken cancellationToken)
    {
        var row = await database.SavedViews.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == scope.CompanyId && item.Id == viewId
            && item.OwnerUserId == scope.ActorUserId, cancellationToken) ?? throw new ReportingException(503, ReportingProblemCodesV1.SourceUnavailable);
        return ViewOf(row);
    }

    private static string ViewAudit(ReportingSavedViewRow row) => JsonSerializer.Serialize(new
    {
        row.Id, row.ReportId, nameLength = row.Name.Length, parameters = row.ParametersJson, columns = row.ColumnsJson, row.Version, row.IsActive,
    }, Json);

    private void AuditView(Scope scope, ReportingCommandMetadataV1 metadata, string eventType, Guid viewId, string operation, long? before, long? after,
        string beforeJson, string afterJson, string reason) =>
        audit.Enqueue(new AuditEventDraft(scope.CompanyId, eventType, "Reporting.SavedView", viewId.ToString("D"), operation, before, after,
            AuditActorTypes.User, scope.ActorId, scope.AuditPermission, scope.RoleCodes, beforeJson, afterJson, reason, null, AuditAiInvolvement.None,
            null, correlation.IsResolved ? correlation.SourceChannel : "api", correlation.IsResolved ? correlation.ClientIpAddress : null,
            correlation.IsResolved ? correlation.UserAgent : null, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

    private static SortedDictionary<string, string?>? Sorted(IReadOnlyDictionary<string, string?>? values) =>
        values is null ? null : new SortedDictionary<string, string?>(values.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal);

    private static long ParseVersion(string? value) =>
        value is not null && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version >= 1
            && value == version.ToString(CultureInfo.InvariantCulture)
            ? version
            : throw new ReportingException(400, ReportingProblemCodesV1.SavedViewInvalid, "expectedVersion");

    private static string Hash(params object?[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts, Json))));

    private static string KeyHash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private sealed record Replay(Guid TargetId, string CorrelationId);

    private async Task<Replay?> FindReplayAsync(Scope scope, ReportingCommandMetadataV1 metadata, string command, string requestHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 128
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || metadata.CorrelationId.Length > 128 || metadata.CausationId?.Length > 128)
            throw new ReportingException(400, ReportingProblemCodesV1.CommandMetadataInvalid);
        var keyHash = KeyHash(metadata.IdempotencyKey);
        var attempt = await database.Attempts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == scope.CompanyId && row.ActorUserId == scope.ActorUserId && row.IdempotencyKeySha256 == keyHash, cancellationToken);
        RequireStill(scope);
        if (attempt is null) return null;
        if (attempt.Command != command || attempt.RequestSha256 != requestHash)
            throw new ReportingException(409, ReportingProblemCodesV1.SameKeyPayloadMismatch);
        if (attempt.Status == 409) throw new ReportingException(409, attempt.RefusalCode!, isReplay: true, originalCorrelationId: attempt.CorrelationId);
        return new Replay(attempt.TargetId, attempt.CorrelationId);
    }

    private void AddAttempt(Scope scope, ReportingCommandMetadataV1 metadata, string command, string requestHash, Guid targetId, int status,
        string? refusalCode, DateTimeOffset now) => database.Attempts.Add(new ReportingAttemptRow
        {
            CompanyId = scope.CompanyId, ActorUserId = scope.ActorUserId, IdempotencyKeySha256 = KeyHash(metadata.IdempotencyKey), Command = command,
            RequestSha256 = requestHash, TargetId = targetId, Status = status, RefusalCode = refusalCode, CorrelationId = metadata.CorrelationId,
            RecordedAtUtc = now,
        });

    /// <summary>A refusal of state is durable: recorded with its key, then thrown; a lost race answers with the winner.</summary>
    private async Task<ReportingException> TerminalAsync(Scope scope, ReportingCommandMetadataV1 metadata, string command, string requestHash,
        Guid targetId, string code, CancellationToken cancellationToken)
    {
        RequireStill(scope);
        database.ChangeTracker.Clear();
        AddAttempt(scope, metadata, command, requestHash, targetId, 409, code, time.GetUtcNow());
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        return saved is null ? new ReportingException(409, code) : new ReportingException(409, ReportingProblemCodesV1.ConcurrentChange);
    }

    private async Task<Replay?> SaveAsync(Scope scope, ReportingCommandMetadataV1 metadata, string command, string requestHash,
        CancellationToken cancellationToken)
    {
        RequireStill(scope);
        try
        {
            if (database.Database.IsRelational())
            {
                await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
                RequireStill(scope);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await database.SaveChangesAsync(cancellationToken);
            }

            return null;
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
            return replay ?? throw new ReportingException(409, ReportingProblemCodesV1.ConcurrentChange);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    // ---- Authority fence -----------------------------------------------------------------------------------------------

    private sealed record Actor(Guid CompanyId, string ActorId, Guid ActorUserId);

    private sealed record Scope(Guid CompanyId, string ActorId, Guid ActorUserId, string AuditPermission, IReadOnlyList<string> RoleCodes)
    {
        public static implicit operator Actor(Scope scope) => new(scope.CompanyId, scope.ActorId, scope.ActorUserId);
    }

    private Actor RequireActor()
    {
        if (!user.IsAuthenticated || !Guid.TryParse(user.UserId, out var actorUserId) || actorUserId == Guid.Empty)
            throw new ReportingException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
            throw new ReportingException(403, "forbidden");
        return new Actor(company.CompanyId, user.UserId!, actorUserId);
    }

    private void RequireStill(Actor actor)
    {
        if (!user.IsAuthenticated || user.UserId != actor.ActorId || !company.IsResolved || company.CompanyId != actor.CompanyId
            || !user.CanAccessCompany(actor.CompanyId))
            throw new ReportingException(403, "forbidden");
    }

    private async Task<bool> HoldsAsync(Actor actor, string permission, CancellationToken cancellationToken)
    {
        if (!ErpPermissions.IsKnown(permission)) throw new ReportingException(503, ReportingProblemCodesV1.PermissionUnregistered);
        var granted = await authority.RevalidatePermissionScopeAsync(permission, actor.CompanyId, cancellationToken);
        RequireStill(actor);
        return granted is { IsGranted: true, IsCompanyWide: true };
    }

    private async Task<Scope> RequireAllAsync(Actor actor, IReadOnlyList<string> permissions, CancellationToken cancellationToken,
        string? auditPermission = null)
    {
        var roles = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var permission in permissions)
        {
            if (!ErpPermissions.IsKnown(permission)) throw new ReportingException(503, ReportingProblemCodesV1.PermissionUnregistered);
            var granted = await authority.RevalidatePermissionScopeAsync(permission, actor.CompanyId, cancellationToken);
            RequireStill(actor);
            if (granted is not { IsGranted: true, IsCompanyWide: true }) throw new ReportingException(403, "forbidden");
            roles.UnionWith(granted.RoleCodes);
        }

        return new Scope(actor.CompanyId, actor.ActorId, actor.ActorUserId, auditPermission ?? permissions[0], roles.ToArray());
    }

    private async Task RequireAllAgainAsync(Scope scope, IReadOnlyList<string> permissions, CancellationToken cancellationToken)
    {
        RequireStill(scope);
        foreach (var permission in permissions)
        {
            var granted = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
            RequireStill(scope);
            if (granted is not { IsGranted: true, IsCompanyWide: true }) throw new ReportingException(403, "forbidden");
        }
    }
}
