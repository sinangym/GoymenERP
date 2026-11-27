using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

namespace GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;

internal sealed partial class LogisticsWeighingDifferenceWorkInboxSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        async Task Fence()
        {
            if (await fence.CheckAsync(context, ApproverPermission, token) is not null)
                throw new InvalidOperationException("Weighing decision source unavailable.");
        }
        await Fence();
        var company = context.CompanyId;
        // The note/instruction are identity/printed-number context only. Current business status never blocks immutable retry.
        var query = from record in rows.WeighingDifferences
                    join note in rows.DispatchNotes on new { record.CompanyId, record.InstructionId, record.DispatchNoteId }
                        equals new { note.CompanyId, note.InstructionId, note.DispatchNoteId }
                    join instruction in rows.Instructions on new { record.CompanyId, Id = record.InstructionId }
                        equals new { instruction.CompanyId, instruction.Id }
                    where record.CompanyId == company && record.RecordId == itemId
                    select new { Record = record, note.SeriesCode, note.SequenceNo };
        var found = await rows.ListAsync(query.Take(2), token);
        await Fence();
        if (found.Count == 0) return null;
        if (found.Count != 1) throw new InvalidOperationException("Ambiguous weighing record.");
        var row = found[0].Record; var version = row.Version; var status = row.Status;
        if (version < 1 || !Enum.IsDefined((WeighingDifferenceRecordStatus)status)
            || !Enum.IsDefined((WeighingDifferenceBandLevel)row.BandLevel) || !Enum.IsDefined((WeighingDifferenceDecision)row.Decision)
            || row.RecordedAtUtc == default || row.RecordedAtUtc.Offset != TimeSpan.Zero || !row.EnteredByComplete)
            throw new InvalidOperationException("Incomplete weighing decision proof.");
        var actors = await rows.ListAsync(rows.WeighingDifferenceEnteredBy.Where(actor => actor.CompanyId == company && actor.RecordId == itemId)
            .Select(actor => actor.ActorId).OrderBy(actor => actor).Take(WorkInboxRulesV1.MaxExcludedDecidersPerItem + 2), token);
        await Fence();
        if (actors.Count == 0 || actors.Count > WorkInboxRulesV1.MaxExcludedDecidersPerItem + 1
            || actors.Any(actor => string.IsNullOrWhiteSpace(actor) || actor != actor.Trim() || actor.Length > WeighingDifferenceRecord.MaximumActorLength)
            || actors.Distinct(StringComparer.Ordinal).Count() != actors.Count || !actors.Contains(row.RecordedBy, StringComparer.Ordinal))
            throw new InvalidOperationException("Incomplete weighing entered-by set.");
        var products = await rows.ListAsync(rows.WeighingDifferenceProducts.Where(product => product.CompanyId == company && product.RecordId == itemId)
            .OrderBy(product => product.Ordinal).Take(WeighingDifferenceCalculator.MaximumLines + 1), token);
        await Fence();
        if (products.Count == 0 || products.Count > WeighingDifferenceCalculator.MaximumLines
            || products.Where((product, index) => product.Ordinal != index + 1 || product.ProductId == Guid.Empty).Any()
            || products.Select(product => product.ProductId).Distinct().Count() != products.Count)
            throw new InvalidOperationException("Incomplete weighing product evaluation.");
        var final = await rows.ListAsync(query.Select(value => new { value.Record.Version, value.Record.Status }).Take(2), token);
        await Fence();
        if (final.Count != 1 || final[0].Version != version || final[0].Status != status)
            throw new InvalidOperationException("Weighing approval changed during purpose read.");
        if (!TryItem(new(row.RecordId, row.InstructionId, row.DispatchNoteId, row.TheoreticalKg, row.ScaleKg, row.DifferenceKg,
            row.TotalExcessKg, row.ApprovalRatePercent, row.EnteredByComplete, row.RecordedBy, row.RecordedAtUtc,
            found[0].SeriesCode, found[0].SequenceNo), actors, out var item))
            throw new InvalidOperationException("Malformed weighing purpose item.");
        return new(company, item, version.ToString(CultureInfo.InvariantCulture), status == (int)WeighingDifferenceRecordStatus.PendingApproval,
            LogisticsWeighingDifferenceProof: new(company, row.RecordId, row.DispatchNoteId, row.InstructionId,
                row.TheoreticalKg, row.ScaleKg, row.DifferenceKg, ((WeighingDifferenceBandLevel)row.BandLevel).ToString(),
                row.WarningBandPercent, row.ApprovalBandPercent, row.BlockBandPercent, row.TotalExcessKg, row.ApprovalRatePercent,
                ((WeighingDifferenceDecision)row.Decision).ToString(), products.Select(product => new WorkLogisticsWeighingDifferenceProductProofV1(
                    product.ProductId, product.NoteKg, product.ScaleKg, product.DifferenceKg, product.RemainingPhysicalKg,
                    product.PhysicallyCoveredKg, product.ExcessKg, product.TargetLineNo)).ToArray(), actors.Order(StringComparer.Ordinal).ToArray(),
                row.EnteredByComplete, row.RecordedBy, row.RecordedAtUtc, row.KilogramAllocationJson));
    }
}
