using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryStore
{
    internal const string TransformLabelsCommand = "inventory.label-transformation.record.v1";

    public async Task<InventoryLabelTransformationResultV1> TransformAsync(
        TransformInventoryLabelsV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMetadata(metadata);
        RequireGenealogyMetadata(metadata);
        RequireResolvedCompany();
        cancellationToken.ThrowIfCancellationRequested();

        var parsed = ParseTransformation(request);
        var companyId = companyContext.CompanyId;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        var payloadHash = GenealogyPayloadHash(new
        {
            request.TransformationId,
            kind = (int)request.Kind,
            sources = parsed.Sources.Select(source => new { source.LabelId, source.ExpectedVersion }),
            outputs = parsed.Outputs.Select(output => new { output.LabelId, output.Code, quantityKg = output.Quantity.ToString() }),
            request.OccurredAtUtc,
            reason = parsed.Reason,
        });
        if (await FindGenealogyReplayAsync(keyHash, payloadHash, metadata.ActorId, cancellationToken) is { } replay)
        {
            return replay;
        }

        if (await FindTransformationReplayAsync(request.TransformationId, payloadHash, cancellationToken) is { } identityReplay)
        {
            return identityReplay;
        }

        var sourceIds = parsed.Sources.Select(source => source.LabelId).ToArray();
        var sourceRows = await dbContext.Labels.Where(row => sourceIds.Contains(row.Id)).ToListAsync(cancellationToken);
        if (sourceRows.Count != sourceIds.Length)
        {
            throw GenealogyError(404, InventoryLabelGenealogyCodesV1.LabelNotFound);
        }

        foreach (var source in parsed.Sources)
        {
            var row = sourceRows.Single(candidate => candidate.Id == source.LabelId);
            if (row.Version != source.ExpectedVersion)
            {
                throw GenealogyError(409, InventoryLabelGenealogyCodesV1.LabelVersionConflict);
            }
        }

        var balances = await dbContext.Balances
            .Where(row => sourceIds.Contains(row.LabelId) && row.QuantityKg > 0m)
            .ToListAsync(cancellationToken);
        if (sourceIds.Any(id => balances.Count(row => row.LabelId == id) != 1))
        {
            throw GenealogyError(409, InventoryLabelGenealogyCodesV1.SourcePositionConflict);
        }

        if (await dbContext.Allocations.AsNoTracking().AnyAsync(row => sourceIds.Contains(row.LabelId)
                && row.Status == (int)StockAllocationStatus.Active && row.RemainingKg > 0m, cancellationToken))
        {
            throw GenealogyError(409, InventoryLabelGenealogyCodesV1.ActiveAllocationConflict);
        }

        var outputIds = parsed.Outputs.Select(output => output.LabelId).ToArray();
        var outputCodes = parsed.Outputs.Select(output => output.Code).ToArray();
        if (await dbContext.Labels.AsNoTracking().AnyAsync(row => outputIds.Contains(row.Id) || outputCodes.Contains(row.Code),
                cancellationToken))
        {
            throw GenealogyError(409, InventoryLabelGenealogyCodesV1.LabelCodeTaken);
        }

        var sourceModels = parsed.Sources.Select(source =>
        {
            var row = sourceRows.Single(candidate => candidate.Id == source.LabelId);
            var balance = balances.Single(candidate => candidate.LabelId == source.LabelId);
            if (balance.ProductId != row.ProductId)
            {
                throw GenealogyError(503, InventoryLabelGenealogyCodesV1.CorruptState);
            }

            var label = GenealogyLabel(row);
            var position = Position(balance.CompanyId, balance.OwnerKind, balance.OwnerId, balance.CustodianKind,
                balance.CustodianId, balance.LocationId, label, balance.Block);
            return new InventoryLabelTransformationSource(
                label, position, Kilograms.Positive(balance.QuantityKg), source.ExpectedVersion);
        }).ToArray();
        var baseline = sourceRows.Single(row => row.Id == parsed.Sources[0].LabelId);
        if (baseline.ProductVersion is null || sourceRows.Any(row => row.ProductId != baseline.ProductId
                || !string.Equals(row.ProductVersion, baseline.ProductVersion, StringComparison.Ordinal)))
        {
            throw GenealogyError(503, InventoryLabelGenealogyCodesV1.CorruptState);
        }

        var baselineBalance = balances.Single(row => row.LabelId == baseline.Id);
        var requiredDocuments = JsonSerializer.Deserialize<string[]>(baseline.RequiredDocumentsJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı etiketin belge gereksinimi okunamadı.");
        var outputModels = parsed.Outputs.Select(output =>
        {
            var label = new MaterialLabel(output.LabelId, companyId, output.Code, baseline.ProductId, baseline.LotId,
                baseline.ProductVersion, requiredDocuments, baseline.HeatNumber, baseline.CoilNumber);
            var position = Position(companyId, baselineBalance.OwnerKind, baselineBalance.OwnerId,
                baselineBalance.CustodianKind, baselineBalance.CustodianId, baselineBalance.LocationId,
                label, baselineBalance.Block);
            return new InventoryLabelTransformationOutput(label, position, output.Quantity);
        }).ToArray();

        InventoryLabelTransformation transformation;
        try
        {
            transformation = new InventoryLabelTransformation(request.TransformationId, companyId,
                (InventoryLabelTransformationKind)request.Kind, request.OccurredAtUtc,
                sourceModels, outputModels, parsed.Reason);
        }
        catch (InventoryRuleException exception)
        {
            throw GenealogyError(400, InventoryLabelGenealogyCodesV1.ValidationFailed, exception);
        }

        var scale = await kilogramScale.CheckAsync(companyId,
            DateOnly.FromDateTime(request.OccurredAtUtc.UtcDateTime),
            transformation.Sources.Select(source => source.Quantity)
                .Concat(transformation.Outputs.Select(output => output.Quantity)), cancellationToken);
        if (!scale.IsAccepted)
        {
            throw GenealogyError(400, scale.Code ?? InventoryLabelGenealogyCodesV1.ValidationFailed);
        }

        var now = timeProvider.GetUtcNow();
        var transformationRow = new LabelTransformationRow
        {
            Id = transformation.Id,
            CompanyId = companyId,
            Kind = (int)transformation.Kind,
            OccurredAtUtc = transformation.OccurredAtUtc,
            Reason = transformation.Reason,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        };
        dbContext.LabelTransformations.Add(transformationRow);

        var inputSequence = 0;
        foreach (var source in transformation.Sources)
        {
            var label = sourceRows.Single(row => row.Id == source.Label.Id);
            balances.Single(row => row.LabelId == source.Label.Id).QuantityKg = 0m;
            label.Version = checked(label.Version + 1);
            label.UpdatedAtUtc = now;
            dbContext.LabelTransformationInputs.Add(new LabelTransformationInputRow
            {
                CompanyId = companyId,
                TransformationId = transformation.Id,
                Sequence = ++inputSequence,
                LabelId = label.Id,
                LabelVersion = source.ExpectedVersion,
                QuantityKg = source.Quantity.Value,
            });
        }

        var outputSequence = 0;
        foreach (var output in transformation.Outputs)
        {
            dbContext.Labels.Add(new LabelRow
            {
                Id = output.Label.Id,
                CompanyId = companyId,
                Code = output.Label.Code,
                ProductId = output.Label.ProductId,
                ProductVersion = output.Label.ProductVersion,
                LotId = output.Label.LotId,
                HeatNumber = output.Label.HeatNumber,
                CoilNumber = output.Label.CoilNumber,
                RequiredDocumentsJson = JsonSerializer.Serialize(output.Label.RequiredDocuments.Order(StringComparer.Ordinal), Json),
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
            dbContext.Balances.Add(new BalanceRow
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                OwnerKind = (int)output.Position.Owner.Kind,
                OwnerId = output.Position.Owner.Id,
                CustodianKind = (int)output.Position.Custodian.Kind,
                CustodianId = output.Position.Custodian.Id,
                LocationId = output.Position.LocationId,
                LabelId = output.Label.Id,
                ProductId = output.Label.ProductId,
                Block = (int)output.Position.Block,
                QuantityKg = output.Quantity.Value,
            });
            dbContext.LabelTransformationOutputs.Add(new LabelTransformationOutputRow
            {
                CompanyId = companyId,
                TransformationId = transformation.Id,
                Sequence = ++outputSequence,
                LabelId = output.Label.Id,
                LabelVersion = 1,
                QuantityKg = output.Quantity.Value,
            });
        }

        var view = TransformationView(transformationRow, sourceIds, outputIds);
        dbContext.Commands.Add(GenealogyCommandRow(companyId, keyHash, payloadHash, metadata, transformation.Id, view));
        Audit(metadata, companyId, "Inventory.LabelTransformation", transformation.Id,
            "inventory.label-transformation.recorded.v1", transformation.Kind.ToString(), null, 1,
            "{}", JsonSerializer.Serialize(view, Json), transformation.Reason);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || IsUniqueConstraintViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            if (await FindGenealogyReplayAsync(keyHash, payloadHash, metadata.ActorId, cancellationToken) is { } winner)
            {
                return winner;
            }

            if (await FindTransformationReplayAsync(request.TransformationId, payloadHash, cancellationToken) is { } identityWinner)
            {
                return identityWinner;
            }

            if (await dbContext.Labels.AsNoTracking().AnyAsync(row => outputIds.Contains(row.Id)
                    || outputCodes.Contains(row.Code), cancellationToken))
            {
                throw GenealogyError(409, InventoryLabelGenealogyCodesV1.LabelCodeTaken, exception);
            }

            throw GenealogyError(409, InventoryLabelGenealogyCodesV1.ConcurrentChange, exception);
        }

        return new(view, false, metadata.CorrelationId.Trim());
    }

    public async Task<InventoryLabelGenealogyV1?> ReadAsync(
        Guid labelId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (labelId == Guid.Empty || limit is < 1 or > 100)
        {
            throw GenealogyError(400, InventoryLabelGenealogyCodesV1.ValidationFailed);
        }

        var root = await dbContext.Labels.AsNoTracking().SingleOrDefaultAsync(row => row.Id == labelId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        var seenLabels = new HashSet<Guid> { labelId };
        var frontier = new HashSet<Guid> { labelId };
        var transformationIds = new List<Guid>();
        var windowFull = false;
        while (frontier.Count > 0 && transformationIds.Count < limit)
        {
            var inputIds = await dbContext.LabelTransformationInputs.AsNoTracking()
                .Where(row => frontier.Contains(row.LabelId)).Select(row => row.TransformationId)
                .ToListAsync(cancellationToken);
            var outputIds = await dbContext.LabelTransformationOutputs.AsNoTracking()
                .Where(row => frontier.Contains(row.LabelId)).Select(row => row.TransformationId)
                .ToListAsync(cancellationToken);
            var candidates = inputIds.Concat(outputIds).Distinct().Except(transformationIds).ToArray();
            if (candidates.Length == 0)
            {
                break;
            }

            var remaining = limit - transformationIds.Count;
            var rows = await dbContext.LabelTransformations.AsNoTracking().Where(row => candidates.Contains(row.Id))
                .OrderBy(row => row.OccurredAtUtc).ThenBy(row => row.Id).Take(remaining + 1)
                .ToListAsync(cancellationToken);
            if (rows.Count > remaining)
            {
                windowFull = true;
                rows.RemoveAt(rows.Count - 1);
            }

            var accepted = rows.Select(row => row.Id).ToArray();
            transformationIds.AddRange(accepted);
            var connectedInputs = await dbContext.LabelTransformationInputs.AsNoTracking()
                .Where(row => accepted.Contains(row.TransformationId)).Select(row => row.LabelId)
                .ToListAsync(cancellationToken);
            var connectedOutputs = await dbContext.LabelTransformationOutputs.AsNoTracking()
                .Where(row => accepted.Contains(row.TransformationId)).Select(row => row.LabelId)
                .ToListAsync(cancellationToken);
            frontier = connectedInputs.Concat(connectedOutputs).Where(seenLabels.Add).ToHashSet();
        }

        if (transformationIds.Count == limit && frontier.Count > 0)
        {
            windowFull = true;
        }

        var transformations = await dbContext.LabelTransformations.AsNoTracking()
            .Where(row => transformationIds.Contains(row.Id))
            .OrderBy(row => row.OccurredAtUtc).ThenBy(row => row.Id).ToListAsync(cancellationToken);
        var inputs = await dbContext.LabelTransformationInputs.AsNoTracking()
            .Where(row => transformationIds.Contains(row.TransformationId)).ToListAsync(cancellationToken);
        var outputs = await dbContext.LabelTransformationOutputs.AsNoTracking()
            .Where(row => transformationIds.Contains(row.TransformationId)).ToListAsync(cancellationToken);
        var allLabelIds = inputs.Select(row => row.LabelId).Concat(outputs.Select(row => row.LabelId)).Distinct().ToArray();
        var codes = await dbContext.Labels.AsNoTracking().Where(row => allLabelIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.Code, cancellationToken);
        if (codes.Count != allLabelIds.Length)
        {
            throw GenealogyError(503, InventoryLabelGenealogyCodesV1.CorruptState);
        }

        var edges = transformations.SelectMany(transformation =>
            inputs.Where(row => row.TransformationId == transformation.Id).OrderBy(row => row.Sequence)
                .SelectMany(input => outputs.Where(row => row.TransformationId == transformation.Id).OrderBy(row => row.Sequence)
                    .Select(output => new InventoryLabelGenealogyEdgeV1(
                        transformation.Id,
                        ((InventoryLabelTransformationKind)transformation.Kind).ToString(),
                        transformation.OccurredAtUtc,
                        input.LabelId,
                        codes[input.LabelId],
                        input.LabelVersion.ToString(CultureInfo.InvariantCulture),
                        Kilograms.Positive(input.QuantityKg).ToString(),
                        output.LabelId,
                        codes[output.LabelId],
                        output.LabelVersion.ToString(CultureInfo.InvariantCulture),
                        Kilograms.Positive(output.QuantityKg).ToString())))).ToArray();
        return new InventoryLabelGenealogyV1(root.Id, root.Code, edges, windowFull);
    }

    private static ParsedTransformation ParseTransformation(TransformInventoryLabelsV1 request)
    {
        if (request.TransformationId == Guid.Empty || !Enum.IsDefined(request.Kind)
            || request.OccurredAtUtc.Offset != TimeSpan.Zero
            || request.Sources is null || request.Outputs is null
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
        {
            throw GenealogyError(400, InventoryLabelGenealogyCodesV1.ValidationFailed);
        }

        var sources = request.Sources.Select(source =>
        {
            if (source is null || source.LabelId == Guid.Empty || !TryVersion(source.ExpectedLabelVersion, out var version))
            {
                throw GenealogyError(400, InventoryLabelGenealogyCodesV1.ValidationFailed);
            }

            return new ParsedSource(source.LabelId, version);
        }).ToArray();
        var outputs = request.Outputs.Select(output =>
        {
            if (output is null || output.LabelId == Guid.Empty || string.IsNullOrWhiteSpace(output.Code)
                || output.Code.Trim().Length > MaterialLabel.MaximumCodeLength
                || !TryQuantity(output.QuantityKg, out var quantity))
            {
                throw GenealogyError(400, InventoryLabelGenealogyCodesV1.ValidationFailed);
            }

            return new ParsedOutput(output.LabelId, output.Code.Trim(), quantity);
        }).ToArray();
        if (sources.Select(source => source.LabelId).Distinct().Count() != sources.Length
            || outputs.Select(output => output.LabelId).Distinct().Count() != outputs.Length
            || outputs.Select(output => output.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != outputs.Length)
        {
            throw GenealogyError(400, InventoryLabelGenealogyCodesV1.ValidationFailed);
        }

        return new ParsedTransformation(sources, outputs, request.Reason.Trim());
    }

    private async Task<InventoryLabelTransformationResultV1?> FindGenealogyReplayAsync(
        string keyHash,
        string payloadHash,
        string actorId,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == TransformLabelsCommand
                && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, actorId, StringComparison.Ordinal))
        {
            throw GenealogyError(409, InventoryLabelGenealogyCodesV1.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<GenealogyCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı etiket dönüşüm sonucu okunamadı.");
        return new(stored.Transformation, true, prior.CorrelationId);
    }

    private async Task<InventoryLabelTransformationResultV1?> FindTransformationReplayAsync(
        Guid transformationId,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var stored = await dbContext.LabelTransformations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == transformationId, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        if (!string.Equals(stored.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            throw GenealogyError(409, InventoryLabelGenealogyCodesV1.TransformationConflict);
        }

        return new(await TransformationViewAsync(stored, cancellationToken), true, stored.CorrelationId);
    }

    private InventoryCommandRow GenealogyCommandRow(
        Guid companyId,
        string keyHash,
        string payloadHash,
        InventoryCommandMetadata metadata,
        Guid targetId,
        InventoryLabelTransformationViewV1 view) => new()
        {
            CompanyId = companyId,
            CommandName = TransformLabelsCommand,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = targetId,
            ResultJson = JsonSerializer.Serialize(new GenealogyCommandResult(view), Json),
            CorrelationId = metadata.CorrelationId.Trim(),
            RecordedAtUtc = timeProvider.GetUtcNow(),
        };

    private async Task<InventoryLabelTransformationViewV1> TransformationViewAsync(
        LabelTransformationRow row,
        CancellationToken cancellationToken)
    {
        var sourceIds = await dbContext.LabelTransformationInputs.AsNoTracking()
            .Where(edge => edge.TransformationId == row.Id).OrderBy(edge => edge.Sequence)
            .Select(edge => edge.LabelId).ToListAsync(cancellationToken);
        var outputIds = await dbContext.LabelTransformationOutputs.AsNoTracking()
            .Where(edge => edge.TransformationId == row.Id).OrderBy(edge => edge.Sequence)
            .Select(edge => edge.LabelId).ToListAsync(cancellationToken);
        return TransformationView(row, sourceIds, outputIds);
    }

    private static InventoryLabelTransformationViewV1 TransformationView(
        LabelTransformationRow row,
        IReadOnlyList<Guid> sourceIds,
        IReadOnlyList<Guid> outputIds) =>
        new(row.Id, row.CompanyId, ((InventoryLabelTransformationKind)row.Kind).ToString(), row.OccurredAtUtc,
            row.Reason, sourceIds, outputIds);

    private static MaterialLabel GenealogyLabel(LabelRow row)
    {
        var required = JsonSerializer.Deserialize<string[]>(row.RequiredDocumentsJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı etiketin belge gereksinimi okunamadı.");
        return new MaterialLabel(row.Id, row.CompanyId, row.Code, row.ProductId, row.LotId,
            row.ProductVersion,
            required, row.HeatNumber, row.CoilNumber);
    }

    private static void RequireGenealogyMetadata(InventoryCommandMetadata metadata)
    {
        if (!string.Equals(metadata.IdempotencyKey, metadata.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || metadata.IdempotencyKey.Length > 128 || metadata.IdempotencyKey.Any(char.IsControl)
            || !string.Equals(metadata.CorrelationId, metadata.CorrelationId.Trim(), StringComparison.Ordinal)
            || metadata.CorrelationId.Length > 128)
        {
            throw GenealogyError(400, InventoryLabelGenealogyCodesV1.ValidationFailed);
        }
    }

    private static string GenealogyHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string GenealogyPayloadHash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))))
            .ToLowerInvariant();

    private static InventoryLabelGenealogyException GenealogyError(int status, string code, Exception? inner = null) =>
        new(status, code, inner);

    private sealed record ParsedTransformation(ParsedSource[] Sources, ParsedOutput[] Outputs, string Reason);
    private sealed record ParsedSource(Guid LabelId, long ExpectedVersion);
    private sealed record ParsedOutput(Guid LabelId, string Code, Kilograms Quantity);
    private sealed record GenealogyCommandResult(InventoryLabelTransformationViewV1 Transformation);
}
