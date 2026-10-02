using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Labels;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryStore
{
    internal const string ClassifyLabelCommand = "inventory.label-classification.classify.v1";

    public async Task<InventoryLabelClassificationResultV1> ClassifyAsync(
        Guid labelId,
        ClassifyInventoryLabelV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireMetadata(metadata);
        RequireResolvedCompany();
        cancellationToken.ThrowIfCancellationRequested();

        if (labelId == Guid.Empty
            || !long.TryParse(request.ExpectedLabelVersion, out var expectedVersion)
            || expectedVersion < 1
            || !Enum.IsDefined(request.Classification)
            || !string.Equals(metadata.IdempotencyKey, metadata.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || metadata.IdempotencyKey.Length > 128 || metadata.IdempotencyKey.Any(char.IsControl)
            || !string.Equals(metadata.CorrelationId, metadata.CorrelationId.Trim(), StringComparison.Ordinal)
            || metadata.CorrelationId.Length > 128)
        {
            throw Error(400, InventoryLabelClassificationCodesV1.ValidationFailed);
        }

        var companyId = companyContext.CompanyId;
        var keyHash = Hash(metadata.IdempotencyKey);
        var payloadHash = PayloadHash(new
        {
            labelId,
            expectedLabelVersion = expectedVersion,
            classification = (int)request.Classification,
        });
        if (await FindClassificationReplayAsync(keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } replay)
        {
            return replay;
        }

        var row = await dbContext.Labels.SingleOrDefaultAsync(candidate => candidate.Id == labelId, cancellationToken);
        await RequireClassificationFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (row is null)
        {
            throw Error(404, InventoryLabelClassificationCodesV1.LabelNotFound);
        }

        if (row.Version != expectedVersion)
        {
            throw Error(409, InventoryLabelClassificationCodesV1.LabelVersionConflict);
        }

        var classification = (LabelClassification)request.Classification;
        if (row.Classification == (int)classification)
        {
            throw Error(409, InventoryLabelClassificationCodesV1.ClassificationUnchanged);
        }

        var beforeVersion = row.Version;
        var beforeClassification = row.Classification is null
            ? null
            : ((LabelClassification)row.Classification.Value).ToString();
        var classifiedAtUtc = timeProvider.GetUtcNow();
        row.Classification = (int)classification;
        row.Version++;
        row.UpdatedAtUtc = classifiedAtUtc;

        var view = new InventoryLabelClassificationViewV1(
            row.Id,
            row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            classification.ToString(),
            classifiedAtUtc);
        dbContext.Commands.Add(new InventoryCommandRow
        {
            CompanyId = companyId,
            CommandName = ClassifyLabelCommand,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId.Trim(),
            PayloadHash = payloadHash,
            TargetId = labelId,
            ResultJson = JsonSerializer.Serialize(new ClassificationCommandResult(view), Json),
            CorrelationId = metadata.CorrelationId.Trim(),
            RecordedAtUtc = classifiedAtUtc,
        });
        Audit(metadata, companyId, LabelEntityType, labelId, "inventory.label.classified.v1", "Classify",
            beforeVersion, row.Version,
            JsonSerializer.Serialize(new { classification = beforeClassification }, Json),
            JsonSerializer.Serialize(new { classification = classification.ToString() }, Json),
            "Etiket kalite sınıflandırması");

        await RequireClassificationFreshAuthorityAsync(freshAuthorityFence, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || IsUniqueConstraintViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            if (await FindClassificationReplayAsync(keyHash, payloadHash, metadata.ActorId,
                    freshAuthorityFence, cancellationToken) is { } winner)
            {
                return winner;
            }

            throw Error(409, InventoryLabelClassificationCodesV1.ConcurrentChange, exception);
        }

        return new(view, false, metadata.CorrelationId.Trim());
    }

    private async Task<InventoryLabelClassificationResultV1?> FindClassificationReplayAsync(
        string keyHash,
        string payloadHash,
        string actorId,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CommandName == ClassifyLabelCommand && row.IdempotencyKeyHash == keyHash, cancellationToken);
        await RequireClassificationFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, actorId.Trim(), StringComparison.Ordinal))
        {
            throw Error(409, InventoryLabelClassificationCodesV1.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<ClassificationCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı etiket sınıflandırma sonucu okunamadı.");
        return new(stored.Classification, true, prior.CorrelationId);
    }

    private async Task RequireClassificationFreshAuthorityAsync(
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        try
        {
            await freshAuthorityFence(cancellationToken);
        }
        catch
        {
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string PayloadHash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));

    private static InventoryLabelClassificationException Error(int status, string code, Exception? inner = null) =>
        new(status, code, inner);

    private sealed record ClassificationCommandResult(InventoryLabelClassificationViewV1 Classification);
}
