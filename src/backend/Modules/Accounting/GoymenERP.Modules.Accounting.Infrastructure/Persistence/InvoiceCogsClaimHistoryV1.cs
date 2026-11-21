using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>One owner of the immutable anchor and every append-only successor. Callers retain their company/actor fence.</summary>
internal static class InvoiceCogsClaimHistoryV1
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    internal sealed record Entry(long Generation, InvoiceCogsEconomicClaimRow Claim, InventoryCostClaimSuccessionV1? Succession);

    internal static async Task<IReadOnlyList<Entry>> ReadAsync(AccountingDbContext db, Guid companyId, Guid movementId,
        int line, Action fence, CancellationToken token)
    {
        fence();
        var anchor = await db.InvoiceCogsEconomicClaims.AsNoTracking().SingleOrDefaultAsync(x => x.MovementId == movementId
            && x.MovementLineNumber == line, token); fence();
        var rows = await db.InvoiceCogsEconomicClaimSuccessions.AsNoTracking().Where(x => x.MovementId == movementId
            && x.MovementLineNumber == line).OrderBy(x => x.Generation).ToListAsync(token); fence();
        if (anchor is null) { if (rows.Count != 0) throw Invalid(); return []; }
        if (anchor.CompanyId != companyId || anchor.SourceHash != AccountingInventoryCostSourceValidationV1.Sha256(anchor.ViewJson)) throw Invalid();
        var result = new List<Entry> { new Entry(1, anchor, null) };
        foreach (var row in rows)
        {
            InventoryCostClaimSuccessionV1 proof;
            try { proof = JsonSerializer.Deserialize<InventoryCostClaimSuccessionV1>(row.SuccessionJson, Json) ?? throw Invalid(); }
            catch (JsonException) { throw Invalid(); }
            var previous = result[^1];
            if (proof.Claims is not { Count: > 0 } || proof.Claims.Any(x => x is null || x.Physical is null)
                || proof.Claims.Select(x => (x.Physical.MovementId, x.Physical.MovementLineNumber)).Distinct().Count() != proof.Claims.Count
                || proof.ReversedComponents is null || proof.CapturedCostFingerprints is null
                || proof.ReversedComponents.Any(x => x is null)
                || proof.ReversedComponents.Select(x => x.EconomicKey).Distinct(StringComparer.Ordinal).Count() != proof.ReversedComponents.Count)
                throw Invalid();
            var physical = proof.Claims.SingleOrDefault(x => x.Physical.MovementId == movementId && x.Physical.MovementLineNumber == line);
            if (row.CompanyId != companyId || row.Generation != previous.Generation + 1
                || row.PreviousClaimSha256 != previous.Claim.SourceHash || proof.PreviousEventId != previous.Claim.SalesEventId
                || proof.PreviousInvoiceId != previous.Claim.InvoiceId || proof.PreviousInvoiceLineId != previous.Claim.InvoiceLineId
                || proof.PreviousAdmissionId != previous.Claim.AdmissionId || proof.PreviousFinalizedVersion != previous.Claim.FinalizedVersion
                || proof.CancellationId != row.CancellationId || physical is null || physical.PreviousGeneration != previous.Generation
                || physical.PreviousClaimSha256 != previous.Claim.SourceHash || physical.Physical.ShipmentId != row.ShipmentId
                || row.SalesEventId == previous.Claim.SalesEventId || row.InvoiceId == previous.Claim.InvoiceId
                || row.SourceHash != AccountingInventoryCostSourceValidationV1.Sha256(row.ViewJson)
                || row.SuccessionSha256 != AccountingInventoryCostSourceValidationV1.Sha256(row.SuccessionJson)) throw Invalid();
            await VerifyRelease(db, companyId, proof, fence, token);
            var owner = await db.InventoryCostSources.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == row.SalesEventId, token); fence();
            if (owner is null || owner.CompanyId != companyId || owner.InvoiceId != row.InvoiceId
                || owner.InvoiceVersion != row.FinalizedVersion || owner.AdmissionId != row.AdmissionId) throw Invalid();
            AccountingInventoryCostSourceV1 source;
            try { source = JsonSerializer.Deserialize<AccountingInventoryCostSourceV1>(owner.SourceJson, Json) ?? throw Invalid(); }
            catch (JsonException) { throw Invalid(); }
            var matching = source.Components?.Where(x => x.InvoiceLineId == row.InvoiceLineId).ToArray();
            var component = matching is { Length: 1 } ? matching[0] : null;
            if (component?.ClaimSuccession is null || JsonSerializer.Serialize(component.ClaimSuccession, Json) != row.SuccessionJson
                || component.SalesPhysicalClaims is not { } actualPhysical
                || actualPhysical.Count(x => x == physical.Physical) != 1)
                throw Invalid();
            var expected = JsonSerializer.Serialize(new { source.CompanyId, source.EventId, source.InvoiceId, source.InvoiceVersion,
                source.AdmissionId, component.InvoiceLineId, Physical = physical.Physical, component.SourceProofSha256 }, Json);
            if (row.ViewJson != expected) throw Invalid();
            result.Add(new Entry(row.Generation, new InvoiceCogsEconomicClaimRow() { CompanyId = row.CompanyId, MovementId = row.MovementId,
                MovementLineNumber = row.MovementLineNumber, ShipmentId = row.ShipmentId, SalesEventId = row.SalesEventId,
                InvoiceId = row.InvoiceId, InvoiceLineId = row.InvoiceLineId, AdmissionId = row.AdmissionId,
                FinalizedVersion = row.FinalizedVersion, Origin = 1, SourceHash = row.SourceHash, ViewJson = row.ViewJson,
                RecordedAtUtc = row.RecordedAtUtc }, proof));
        }
        fence(); return result;
    }

    private static async Task VerifyRelease(AccountingDbContext db, Guid companyId, InventoryCostClaimSuccessionV1 proof,
        Action fence, CancellationToken token)
    {
        fence();
        var source = await db.InventoryCostSources.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == proof.PreviousEventId, token); fence();
        var proposal = await db.InventoryCostProposals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proof.PreviousProposalId
            && x.Version == proof.PreviousProposalVersion, token); fence();
        var cancellation = await db.SourceInvoiceCancellations.AsNoTracking().SingleOrDefaultAsync(x => x.SourceDocumentId == proof.PreviousAdmissionId, token); fence();
        if (source is null || proposal is null || cancellation is null || source.CompanyId != companyId || proposal.CompanyId != companyId
            || source.InvoiceId != proof.PreviousInvoiceId || source.InvoiceVersion != proof.PreviousFinalizedVersion
            || source.AdmissionId != proof.PreviousAdmissionId || proposal.EventId != source.EventId
            || proposal.ComponentKey != AccountingInventoryCostTraceV1.SalesComponent(proof.PreviousInvoiceLineId)
            || AccountingInventoryCostSourceValidationV1.Sha256(source.SourceJson) != proof.PreviousSourceSha256
            || cancellation.CompanyId != companyId || cancellation.DocumentKind != AccountingSourceDocumentKindsV1.SalesInvoice
            || cancellation.CancellationId != proof.CancellationId || cancellation.InvoiceId != proof.PreviousInvoiceId
            || cancellation.FinalizedVersion != proof.PreviousFinalizedVersion) throw Invalid();
        AccountingInventoryCostProposalV1 previous;
        try { previous = JsonSerializer.Deserialize<AccountingInventoryCostProposalV1>(proposal.ViewJson, Json) ?? throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
        if (previous.ProposalId != proposal.Id || previous.Version != proposal.Version || previous.EventId != source.EventId
            || previous.Component.InvoiceLineId != proof.PreviousInvoiceLineId || previous.CompanyId != companyId) throw Invalid();
        var expectedBase = previous.Component.UnpostedTlAmount ?? (previous.Component.HasPostedComponents
            ? AccountingInventoryCostAmountsV1.FromDecimal(0) : previous.Component.SignedTlAmount);
        if (expectedBase != proof.KnownBaseAmount || !AccountingInventoryCostAmountsV1.TryRead(proof.RestoredAmount, out var restored, out _)
            || restored.Sign < 0) throw Invalid();
        var expected = expectedBase;
        foreach (var component in proof.ReversedComponents)
        {
            var row = await db.InventoryCostCancellationClaims.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == proof.PreviousInvoiceId
                && x.InvoiceLineId == proof.PreviousInvoiceLineId && x.FinalizedVersion == proof.PreviousFinalizedVersion
                && x.EconomicKey == component.EconomicKey, token); fence();
            var approvedRow = row is null ? null : await db.InventoryCostCancellationVersions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.InventoryCostProposalId == row.InventoryCostProposalId && x.Version == row.CancellationVersion, token); fence();
            if (row is null || approvedRow is null || row.CompanyId != companyId || row.InventoryCostProposalId != proof.PreviousProposalId
                || row.ViewJson != JsonSerializer.Serialize(component, Json) || row.Sha256 != AccountingInventoryCostSourceValidationV1.Sha256(row.ViewJson)
                || row.SourceJournalId != component.SourceJournalId || row.ReversingJournalId != component.ReversingJournalId
                || approvedRow.CompanyId != companyId || approvedRow.Status != 2 || approvedRow.CancellationId != proof.CancellationId
                || approvedRow.ApprovedBy is null || approvedRow.ApprovedBy == approvedRow.PreparedBy
                || approvedRow.Sha256 != AccountingInventoryCostSourceValidationV1.Sha256(approvedRow.ViewJson)) throw Invalid();
            InventoryCostCancellationV1 approved;
            try { approved = JsonSerializer.Deserialize<InventoryCostCancellationV1>(approvedRow.ViewJson, Json) ?? throw Invalid(); }
            catch (JsonException) { throw Invalid(); }
            if (approved.Components?.Any(x => x == component) != true || approved.InvoiceLineRevision > proof.InvoiceLineRevision
                || component.SignedAmount != "0" && component.ReversingJournalId is null) throw Invalid();
            if (component.Origin != "Known")
            {
                if (!proof.CapturedCostFingerprints.Contains(component.EconomicKey, StringComparer.Ordinal)
                    || !decimal.TryParse(component.SignedAmount, System.Globalization.NumberStyles.AllowLeadingSign
                        | System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var signed)) throw Invalid();
                expected = AccountingInventoryCostAmountsV1.Add(expected, AccountingInventoryCostAmountsV1.FromDecimal(signed));
            }
        }
        if (expected != proof.RestoredAmount) throw Invalid();
        fence();
    }

    internal static async Task<IReadOnlyList<InvoiceCogsEconomicClaimRow>> CurrentForInvoiceAsync(AccountingDbContext db,
        Guid companyId, Guid invoiceId, Guid invoiceLineId, long version, Action fence, CancellationToken token)
    {
        fence();
        var anchors = await db.InvoiceCogsEconomicClaims.AsNoTracking().Where(x => x.InvoiceId == invoiceId
            && x.InvoiceLineId == invoiceLineId && x.FinalizedVersion == version).Select(x => new { x.MovementId, x.MovementLineNumber }).ToListAsync(token); fence();
        var later = await db.InvoiceCogsEconomicClaimSuccessions.AsNoTracking().Where(x => x.InvoiceId == invoiceId
            && x.InvoiceLineId == invoiceLineId && x.FinalizedVersion == version).Select(x => new { x.MovementId, x.MovementLineNumber }).ToListAsync(token); fence();
        var result = new List<InvoiceCogsEconomicClaimRow>();
        foreach (var key in anchors.Concat(later).Distinct())
        {
            var history = await ReadAsync(db, companyId, key.MovementId, key.MovementLineNumber, fence, token);
            var current = history.LastOrDefault()?.Claim;
            if (current is null || current.InvoiceId != invoiceId || current.InvoiceLineId != invoiceLineId || current.FinalizedVersion != version)
                throw Invalid();
            result.Add(current);
        }
        fence(); return result;
    }

    internal static AccountingStoreException Invalid() => new AccountingStoreException("inventory_cost_claim_generation_invalid", "Maliyet payının güncel sahiplik nesli doğrulanamadı.");
}
