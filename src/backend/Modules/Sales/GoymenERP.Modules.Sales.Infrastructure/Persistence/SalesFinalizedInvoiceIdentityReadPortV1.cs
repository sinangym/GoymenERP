using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>Retained native receipt plus its same-save finalized outbox; never reads a current header/match.</summary>
public sealed class SalesFinalizedInvoiceIdentityReadPortV1(SalesDbContext db, ICompanyContext company)
    : ISalesFinalizedInvoiceIdentityReadPortV1
{
    public const int MaximumInvoices = 128;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SalesFinalizedInvoiceIdentityBatchV1> ReadAsync(Guid companyId,
        IReadOnlyCollection<SalesFinalizedInvoiceIdentityRequestV1> invoices,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        SalesFinalizedInvoiceIdentityBatchV1 Unavailable() => new(companyId, false, [], "finalization_identity_unavailable");
        if (!Current() || freshConsumerFence is null || invoices is null || invoices.Count > MaximumInvoices
            || invoices.Any(x => x.InvoiceId == Guid.Empty || x.OriginalFinalizedVersion < 2)
            || invoices.Select(x => x.InvoiceId).Distinct().Count() != invoices.Count) return Unavailable();
        var requested = invoices.OrderBy(x => x.InvoiceId).ToArray();
        var ids = requested.Select(x => x.InvoiceId).ToArray();
        try
        {
            await Fence();
            var first = await Snapshot();
            var second = await Snapshot();
            if (JsonSerializer.Serialize(first, Json) != JsonSerializer.Serialize(second, Json)) return Unavailable();
            var answer = Project(companyId, requested, first);
            await Fence();
            return answer;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Unavailable(); }

        bool Current() => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId;
        async Task Fence()
        {
            if (!Current()) throw new UnauthorizedAccessException();
            await freshConsumerFence(cancellationToken);
            if (!Current()) throw new UnauthorizedAccessException();
        }
        async Task<T[]> Rows<T>(IQueryable<T> query)
        {
            await Fence();
            var rows = await query.Take(MaximumInvoices + 1).ToArrayAsync(cancellationToken);
            await Fence();
            if (rows.Length > MaximumInvoices) throw new InvalidOperationException("Finalization identity read truncated.");
            return rows;
        }
        async Task<IdentitySnapshot> Snapshot()
        {
            if (ids.Length == 0) return new([], [], [], []);
            var finals = await Rows(db.InvoiceFinalizations.AsNoTracking()
                .Where(x => x.CompanyId == companyId && ids.Contains(x.InvoiceId)).OrderBy(x => x.InvoiceId)
                .Select(x => new Finalization(x.CompanyId, x.InvoiceId, x.AdmissionId, x.FinalizedVersion,
                    x.FinalizedBy, x.FinalizedAtUtc, x.CorrelationId)));
            var attempts = await Rows(db.SubcontractInvoiceCommandAttempts.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.InvoiceId.HasValue && ids.Contains(x.InvoiceId.Value)
                    && x.CommandName == SalesSubcontractInvoiceLifecycleCommandsV1.Finalize && x.RefusalStatus == null)
                .OrderBy(x => x.InvoiceId));
            var sequences = attempts.Where(x => x.LifecycleSequence.HasValue).Select(x => x.LifecycleSequence!.Value).Distinct().ToArray();
            var history = await Rows(db.SubcontractInvoiceLifecycles.AsNoTracking()
                .Where(x => x.CompanyId == companyId && ids.Contains(x.InvoiceId) && sequences.Contains(x.Sequence))
                .OrderBy(x => x.InvoiceId).ThenBy(x => x.Sequence));
            var events = await Rows(db.OutboxMessages.AsNoTracking()
                .Where(x => x.CompanyId == companyId && ids.Contains(x.AggregateId)
                    && x.EventType == SalesStore.InvoiceFinalizedEventType).OrderBy(x => x.AggregateId).ThenBy(x => x.Id));
            return new(finals, attempts, history, events);
        }
    }

    internal static SalesFinalizedInvoiceIdentityBatchV1 Project(Guid companyId,
        IReadOnlyList<SalesFinalizedInvoiceIdentityRequestV1> requested, IdentitySnapshot snapshot)
    {
        try
        {
            var items = requested.Select(request => ProjectOne(companyId, request, snapshot)).ToArray();
            return new(companyId, true, items);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or FormatException)
        { return new(companyId, false, [], "finalization_identity_proof_invalid"); }
    }

    private static SalesFinalizedInvoiceIdentityV1 ProjectOne(Guid companyId,
        SalesFinalizedInvoiceIdentityRequestV1 request, IdentitySnapshot snapshot)
    {
        var envelope = snapshot.Events.Single(x => x.AggregateId == request.InvoiceId);
        if (envelope.CompanyId != companyId || envelope.Id == Guid.Empty || envelope.SchemaVersion != 1 || envelope.Sequence != 1
            || envelope.AggregateVersion != request.OriginalFinalizedVersion || envelope.EventType != SalesStore.InvoiceFinalizedEventType
            || envelope.OccurredAtUtc == default || envelope.OccurredAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(envelope.CorrelationId)) throw Invalid();
        using var document = JsonDocument.Parse(envelope.PayloadJson);
        SalesFinalizedShipmentClaimReadPortV1.RequireUniqueProperties(document.RootElement);
        var payload = JsonSerializer.Deserialize<Payload>(envelope.PayloadJson, Json) ?? throw Invalid();
        if (payload.CompanyId != companyId || payload.InvoiceId != request.InvoiceId || payload.Version != request.OriginalFinalizedVersion
            || payload.AdmissionId == Guid.Empty || payload.PartyId == Guid.Empty || payload.CompanyAccountId == Guid.Empty
            || string.IsNullOrWhiteSpace(payload.FinalizedBy) || payload.Match is not { } match
            || string.IsNullOrWhiteSpace(match.OfficialNo) || match.OfficialNo.Trim() != match.OfficialNo
            || match.OfficialNo.Any(char.IsControl) || !Guid.TryParseExact(match.Ettn, "D", out var ettn) || ettn == Guid.Empty
            || match.Ettn != ettn.ToString("D") || payload.OfficialInvoiceNo != match.OfficialNo
            || match.InvoiceDate is null || match.InvoiceDate == default(DateOnly)
            || match.SourceProof?.IsValidFor(companyId) != true) throw Invalid();
        var finals = snapshot.Finalizations.Where(x => x.InvoiceId == request.InvoiceId).ToArray();
        var attempts = snapshot.Attempts.Where(x => x.InvoiceId == request.InvoiceId).ToArray();
        if (envelope.AggregateType == "Sales.Invoice")
        {
            if (attempts.Length != 0 || finals is not [var final] || final.CompanyId != companyId
                || final.AdmissionId != payload.AdmissionId || final.Version != payload.Version || final.Actor != payload.FinalizedBy
                || final.AtUtc != envelope.OccurredAtUtc || final.CorrelationId != envelope.CorrelationId) throw Invalid();
        }
        else if (envelope.AggregateType == "Sales.SubcontractServiceInvoice")
        {
            if (finals.Length != 0 || attempts is not [var attempt] || attempt.CompanyId != companyId
                || attempt.LifecycleSequence is not { } sequence || attempt.RefusalStatus != null
                || attempt.ActorId != payload.FinalizedBy || attempt.CorrelationId != envelope.CorrelationId
                || attempt.RecordedAtUtc != envelope.OccurredAtUtc || string.IsNullOrWhiteSpace(attempt.RequestHash)
                || string.IsNullOrWhiteSpace(attempt.IdempotencyKeyHash)) throw Invalid();
            var history = snapshot.History.Single(x => x.InvoiceId == request.InvoiceId && x.Sequence == sequence);
            using var factsDocument = JsonDocument.Parse(history.FactsJson);
            SalesFinalizedShipmentClaimReadPortV1.RequireUniqueProperties(factsDocument.RootElement);
            var facts = JsonSerializer.Deserialize<SalesSubcontractInvoiceLifecycleFacts>(history.FactsJson, Json) ?? throw Invalid();
            if (history.CompanyId != companyId || history.InvoiceVersion != payload.Version
                || history.State != (int)SalesSubcontractServiceInvoiceState.Finalized || history.CorrelationId != envelope.CorrelationId
                || history.RecordedAtUtc != envelope.OccurredAtUtc || facts.Match is not { } storedMatch
                || storedMatch.OfficialNo != match.OfficialNo || storedMatch.Ettn != match.Ettn || !storedMatch.HasCompleteSourceProof(companyId)
                || storedMatch.SourceProof != match.SourceProof || storedMatch.InvoiceDate != match.InvoiceDate
                || facts.Finalization is not { } final
                || final.AdmissionId != payload.AdmissionId || final.FinalizedVersion != payload.Version
                || final.FinalizedBy != payload.FinalizedBy || final.FinalizedAtUtc != envelope.OccurredAtUtc) throw Invalid();
        }
        else throw Invalid();
        return new(companyId, payload.InvoiceId, payload.Version, payload.AdmissionId, payload.PartyId,
            payload.CompanyAccountId, match.OfficialNo, match.Ettn, envelope.Id,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { envelope.Id,
                envelope.PayloadJson, envelope.CorrelationId, envelope.OccurredAtUtc }, Json)))));
    }

    private static InvalidOperationException Invalid() => new("Immutable finalization identity is incomplete.");
    private sealed record Payload(Guid CompanyId, Guid InvoiceId, long Version, Guid AdmissionId, Guid PartyId,
        Guid CompanyAccountId, string FinalizedBy, string OfficialInvoiceNo, IdentityMatch? Match);
    private sealed record IdentityMatch(string OfficialNo, string Ettn, DateOnly? InvoiceDate, SalesOfficialInvoiceSourceProofV1? SourceProof);
    internal sealed record Finalization(Guid CompanyId, Guid InvoiceId, Guid AdmissionId, long Version,
        string Actor, DateTimeOffset AtUtc, string CorrelationId);
    internal sealed record IdentitySnapshot(Finalization[] Finalizations, SubcontractInvoiceCommandAttemptRow[] Attempts,
        SubcontractInvoiceLifecycleRow[] History, SalesOutboxRow[] Events);
}
