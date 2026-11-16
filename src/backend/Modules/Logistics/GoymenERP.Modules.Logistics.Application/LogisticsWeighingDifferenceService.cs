using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

namespace GoymenERP.Modules.Logistics.Application;

/// <summary>
/// STK-S24: authority-fenced owner service of the sales weighing-difference record (SAT-D319..D325, SAT-D332, SAT-D340..D344).
/// It records what the single backend calculator decided for one draft dispatch note and lets a different person approve a
/// record that waits. It never moves stock and never approves or blocks the dispatch note itself: the "kantar farkı" stock entry
/// at average cost and the D323 pending value belong to Inventory (separate slice); Sales reads <see cref="LogisticsWeighingDifferenceReadV1.SaleMayProceed"/> through
/// <see cref="ILogisticsWeighingDifferenceReadPortV1"/>. The SAT-D332 order tolerance is a separate check and is not read here.
/// No HTTP route yet: the central host owns route exposure.
/// </summary>
public sealed class LogisticsWeighingDifferenceService(
    ILogisticsWeighingDifferenceScopeV1 scopes,
    ILogisticsWeighingDifferenceStoreV1 store,
    LogisticsWeighingDifferenceBindingsV1 bindings,
    ILogisticsWeighingDifferencePolicyReadPortV1 policies,
    ILogisticsWeighingDifferenceStockReadPortV1 stock,
    ICorrelationContext correlation,
    TimeProvider timeProvider) : ILogisticsWeighingDifferenceServiceV1
{
    public async Task<WeighingDifferenceResponseV1> RecordAsync(RecordWeighingDifferenceV1 request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        if (request.RecordId == Guid.Empty || request.DispatchNoteId == Guid.Empty || request.Products is null
            || request.Products.Count == 0 || request.Products.Count > WeighingDifferenceCalculator.MaximumLines
            || request.Products.Any(item => item is null))
            throw Validation();
        var scope = await scopes.RequireAsync(bindings.RecordPermission, cancellationToken);
        var envelope = Envelope(scope, bindings.RecordPermission, metadata);
        try
        {
            var hash = LogisticsRequestHash.Compute(LogisticsWeighingDifferenceCommandsV1.Record, request.RecordId, request);
            var replay = await store.FindReplayAsync(envelope, LogisticsWeighingDifferenceCommandsV1.Record, hash,
                cancellationToken);
            if (replay is not null) return Response(replay);

            var source = await store.ReadSourceAsync(request.DispatchNoteId, cancellationToken)
                ?? throw new LogisticsWeighingDifferenceServiceException(404,
                    LogisticsWeighingDifferenceCodesV1.DispatchNoteNotFound);
            if (!source.IsDraft)
                throw new LogisticsWeighingDifferenceServiceException(409,
                    LogisticsWeighingDifferenceCodesV1.DispatchNoteNotDraft);
            if (source.Lines.Count == 0 || source.Lines.Any(line => !line.IsKilogram))
                throw new LogisticsWeighingDifferenceServiceException(409, WeighingDifferenceRuleCodes.UnitNotKilogram);

            var productIds = source.Lines.Select(line => line.ProductId).Distinct().ToArray();
            var policy = Policy(await policies.ReadAsync(scope.CompanyId, cancellationToken));
            var free = await stock.ReadAsync(scope.CompanyId, productIds, cancellationToken);
            var remaining = RemainingPhysical(free, productIds);

            var noteLines = source.Lines.Select(line => new WeighingDifferenceNoteLine(line.LineNo, line.ShipmentId,
                line.ProductId, Quantity.Of(line.Quantity, QuantityUnit.Kilogram))).ToArray();
            var measurements = request.Products.Select(item => new WeighingDifferenceProductMeasurement(item.ProductId,
                Quantity.Of(item.ScaleKg, QuantityUnit.Kilogram),
                remaining.TryGetValue(item.ProductId, out var physical) ? physical : Quantity.Zero(QuantityUnit.Kilogram)))
                .ToArray();
            var evaluation = WeighingDifferenceCalculator.Evaluate(noteLines, measurements, policy);
            var record = WeighingDifferenceRecord.Create(scope.CompanyId, request.RecordId, source.DispatchNoteId,
                source.InstructionId, evaluation, source.EnteredBy, source.EnteredByComplete, scope.ActorId,
                timeProvider.GetUtcNow());
            var result = await store.StoreAsync(envelope, hash, record, source,
                token => scopes.RequireStillAsync(scope, bindings.RecordPermission, token), cancellationToken);
            return Response(result);
        }
        catch (WeighingDifferenceRuleException exception)
        {
            throw new LogisticsWeighingDifferenceServiceException(409, exception.Code);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }
        catch (ArgumentException)
        {
            throw Validation();
        }
    }

    public async Task<WeighingDifferenceResponseV1> ApproveAsync(Guid recordId, ApproveWeighingDifferenceV1 request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        if (recordId == Guid.Empty || request.ExpectedVersion < 1) throw Validation();
        var scope = await scopes.RequireAsync(bindings.ApprovePermission, cancellationToken);
        var envelope = Envelope(scope, bindings.ApprovePermission, metadata);
        try
        {
            var hash = LogisticsRequestHash.Compute(LogisticsWeighingDifferenceCommandsV1.Approve, recordId, request);
            var result = await store.MutateAsync(envelope, hash, LogisticsWeighingDifferenceCommandsV1.Approve, recordId,
                (record, now) => record.Approve(request.ExpectedVersion, scope.ActorId, now),
                token => scopes.RequireStillAsync(scope, bindings.ApprovePermission, token), cancellationToken);
            return Response(result);
        }
        catch (WeighingDifferenceRuleException exception)
        {
            throw new LogisticsWeighingDifferenceServiceException(409, exception.Code);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }
        catch (ArgumentException)
        {
            throw Validation();
        }
    }

    public async Task<WeighingDifferenceViewV1> GetByDispatchNoteAsync(Guid dispatchNoteId,
        CancellationToken cancellationToken = default)
    {
        if (dispatchNoteId == Guid.Empty) throw Validation();
        await scopes.RequireAsync(bindings.ReadPermission, cancellationToken);
        try
        {
            var record = await store.FindByDispatchNoteAsync(dispatchNoteId, cancellationToken)
                ?? throw new LogisticsWeighingDifferenceServiceException(404, LogisticsWeighingDifferenceCodesV1.NotFound);
            return View(record);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }
    }

    /// <summary>
    /// Fail-closed reading of the Settings values: a rate or band outside its range, or with more decimals than are stored, is
    /// treated as unreadable (rate: the record waits for approval; bands: not evaluated). Nothing is rounded or guessed.
    /// </summary>
    private static WeighingDifferencePolicy Policy(LogisticsWeighingDifferencePolicyReadV1? read)
    {
        if (read is null) return WeighingDifferencePolicy.Unreadable;
        var rate = ValidPercent(read.ExcessApprovalRatePercent) ? read.ExcessApprovalRatePercent : null;
        WeighingDifferenceBands? bands = null;
        if (ValidPercent(read.WarningBandPercent) && ValidPercent(read.ApprovalBandPercent) && ValidPercent(read.BlockBandPercent)
            && read.WarningBandPercent < read.ApprovalBandPercent && read.ApprovalBandPercent < read.BlockBandPercent)
            bands = new WeighingDifferenceBands(read.WarningBandPercent!.Value, read.ApprovalBandPercent!.Value,
                read.BlockBandPercent!.Value);
        return new WeighingDifferencePolicy(rate, bands);
    }

    private static bool ValidPercent(decimal? value) =>
        value is { } percent && percent > 0m && percent <= WeighingDifferenceCalculator.PercentScale
        && Quantity.SignificantDecimals(percent) <= WeighingDifferenceCalculator.PercentDecimals;

    /// <summary>
    /// SAT-D322 (1): the free physical kg per note product. An unreadable read, a missing product or a value that is not a valid
    /// kilogram quantity refuses the record (503): the physical cover is never assumed to be zero or plenty.
    /// </summary>
    private static Dictionary<Guid, Quantity> RemainingPhysical(LogisticsWeighingDifferenceStockReadV1? read,
        IReadOnlyCollection<Guid> productIds)
    {
        if (read is null || !read.IsReadable || read.FreeKgByProduct is null)
            throw new LogisticsWeighingDifferenceServiceException(503, LogisticsWeighingDifferenceCodesV1.StockUnavailable);
        var result = new Dictionary<Guid, Quantity>();
        foreach (var productId in productIds)
        {
            if (!read.FreeKgByProduct.TryGetValue(productId, out var value) || value < 0m
                || Quantity.SignificantDecimals(value) > Quantity.KilogramScale)
                throw new LogisticsWeighingDifferenceServiceException(503, LogisticsWeighingDifferenceCodesV1.StockUnavailable);
            result[productId] = Quantity.Of(value, QuantityUnit.Kilogram);
        }

        return result;
    }

    private LogisticsWriteEnvelope Envelope(LogisticsWeighingDifferenceScopeV1 scope, string permission,
        LogisticsCommandMetadata metadata) => new(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId,
        metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel, correlation.ClientIpAddress,
        correlation.UserAgent);

    private static WeighingDifferenceResponseV1 Response(WeighingDifferenceStoreResultV1 result) =>
        new(View(result.Record), result.Outcome == LogisticsWriteOutcome.Replayed, result.OriginalCorrelationId);

    private static WeighingDifferenceViewV1 View(WeighingDifferenceRecord item)
    {
        var evaluation = item.Evaluation;
        return new WeighingDifferenceViewV1(item.CompanyId, item.RecordId, item.DispatchNoteId, item.InstructionId,
            evaluation.TheoreticalKg.Value, evaluation.ScaleKg.Value, evaluation.DifferenceKg, evaluation.BandLevel.ToString(),
            evaluation.Bands?.WarningPercent, evaluation.Bands?.ApprovalPercent, evaluation.Bands?.BlockPercent,
            evaluation.TotalExcessKg.Value,
            WeighingDifferenceCalculator.ExcessRatioPercent(evaluation.TotalExcessKg.Value, evaluation.TheoreticalKg.Value),
            evaluation.ApprovalRatePercent, evaluation.Decision.ToString(), item.Status.ToString(), item.Version,
            [.. item.Products.Select(product => new WeighingDifferenceProductViewV1(product.ProductId, product.NoteKg.Value,
                product.ScaleKg.Value, product.DifferenceKg, product.RemainingPhysicalKg.Value,
                product.PhysicallyCoveredKg.Value, product.ExcessKg.Value, product.TargetLineNo))],
            item.EnteredBy, item.EnteredByComplete, item.RecordedBy, item.RecordedAtUtc, item.ApprovedBy, item.ApprovedAtUtc);
    }

    private static LogisticsWeighingDifferenceServiceException Validation() =>
        new(422, LogisticsWeighingDifferenceCodesV1.ValidationFailed);

    private static LogisticsWeighingDifferenceServiceException Store(LogisticsStoreException exception) =>
        exception.Code switch
        {
            LogisticsStoreCodes.SameKeyPayloadMismatch => new(409, LogisticsStoreCodes.SameKeyPayloadMismatch),
            LogisticsStoreCodes.ConcurrentChange => new(409, LogisticsWeighingDifferenceCodesV1.VersionConflict),
            LogisticsWeighingDifferenceCodesV1.NotFound => new(404, LogisticsWeighingDifferenceCodesV1.NotFound),
            LogisticsWeighingDifferenceCodesV1.AlreadyRecorded => new(409, LogisticsWeighingDifferenceCodesV1.AlreadyRecorded),
            _ => new(503, LogisticsWeighingDifferenceCodesV1.Unavailable),
        };
}
