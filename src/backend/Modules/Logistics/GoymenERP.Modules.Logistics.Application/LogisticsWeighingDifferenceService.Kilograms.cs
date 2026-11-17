using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

namespace GoymenERP.Modules.Logistics.Application;

public sealed partial class LogisticsWeighingDifferenceService
{
    private async Task<DispatchKilogramSnapshot> PrepareKilogramsAsync(LogisticsWeighingDifferenceScopeV1 scope,
        WeighingDifferenceSourceV1 source, WeighingDifferenceEvaluation evaluation,
        DispatchKilogramFinalizedDocumentV1? finalized, CancellationToken cancellationToken)
    {
        var lines = source.Lines.Select(line => new DispatchKilogramSourceLine(line.LineNo, line.ShipmentId, line.ProductId, line.Quantity)).ToArray();
        var totals = evaluation.Products.Select(product => new DispatchKilogramProductTotal(product.ProductId, product.ScaleKg.Value)).ToArray();
        if (finalized is null) return DispatchKilogramSnapshot.Theoretical(lines, totals);
        var proof = await ReadDocumentAsync(scope, source.DispatchNoteId, finalized.DocumentId, finalized.ExpectedDocumentVersion, bindings.RecordPermission, cancellationToken);
        if (!string.Equals(proof.Sha256, finalized.ExpectedDocumentSha256, StringComparison.OrdinalIgnoreCase)) throw UnreadableDocument();
        return DispatchKilogramSnapshot.Finalized(lines, totals, finalized.Lines,
            new(finalized.Ettn, finalized.Kind, proof.DocumentId, proof.Version, proof.Sha256));
    }

    private async Task RequireEvidenceAndAuthorityAsync(LogisticsWeighingDifferenceScopeV1 scope, Guid noteId,
        DispatchKilogramDocument? document, string permission, CancellationToken cancellationToken)
    {
        if (document is not null)
        {
            var proof = await ReadDocumentAsync(scope, noteId, document.DocumentId, document.DocumentVersion, permission, cancellationToken);
            if (!string.Equals(proof.Sha256, document.Sha256, StringComparison.Ordinal)) throw UnreadableDocument();
        }
        await scopes.RequireStillAsync(scope, permission, cancellationToken);
    }

    private async Task<DispatchKilogramDocumentReadV1> ReadDocumentAsync(LogisticsWeighingDifferenceScopeV1 scope,
        Guid noteId, Guid documentId, int? version, string permission, CancellationToken cancellationToken)
    {
        if (documents is null || documentId == Guid.Empty) throw UnreadableDocument();
        DispatchKilogramDocumentReadV1? proof;
        try { proof = await documents.ReadAsync(scope.CompanyId, noteId, documentId, version, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await scopes.RequireStillAsync(scope, permission, cancellationToken);
            throw UnreadableDocument();
        }
        await scopes.RequireStillAsync(scope, permission, cancellationToken);
        if (proof is null || proof.CompanyId != scope.CompanyId || proof.DispatchNoteId != noteId || proof.DocumentId != documentId
            || proof.Version < 1 || version is { } expected && proof.Version != expected
            || proof.Sha256 is not { Length: 64 } || !proof.Sha256.All(Uri.IsHexDigit)) throw UnreadableDocument();
        return proof;
    }

    private static LogisticsWeighingDifferenceServiceException UnreadableDocument() =>
        new(503, "logistics_dispatch_kg_document_unavailable");
}
