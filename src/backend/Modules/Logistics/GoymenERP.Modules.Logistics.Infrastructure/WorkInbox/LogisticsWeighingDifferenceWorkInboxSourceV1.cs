using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;

namespace GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the sales weighing-difference records (STK-S24, SAT-D319..D344) waiting for approval, as
/// Logistics lists them for the Work inbox. A record waits while its status is PendingApproval — the state
/// <c>ApproveLogisticsWeighingDifferenceV1</c> accepts — and is decided on the instruction detail screen (the dispatch note's
/// kilogram panel) by a holder of <c>logistics.weighing-differences.approve</c> who entered neither the weighing nor a shipment
/// (the inbox decides nothing). Every pending record of the company is returned, the recorder's own included: Work hides a
/// maker's own item.
/// <para>
/// The rule results are the record's stored evaluation (the single calculator's result written once at recording): the
/// approval rate Settings gave and the excess ratio (<see cref="WeighingDifferenceCalculator.ExcessRatioPercent"/>), the note
/// and scale kilograms with their signed difference, and the excess not covered by free stock. A record evaluated while the
/// rate was unreadable waits fail-closed and says so instead of a rate. The bands are record-and-report only (SAT-D324) and
/// never the reason for an approval, so they are not listed.
/// </para>
/// </summary>
internal sealed class LogisticsWeighingDifferenceWorkInboxSourceV1 : IWorkInboxSourceV1
{
    public const string ModuleName = "logistics";
    public const string KindName = "logistics.weighing-difference.v1";

    /// <summary>The uncovered excess is at or above the approval rate: threshold = the rate, actual = the excess ratio (%).</summary>
    public const string ExcessRateRule = "logistics.weighing-excess-rate";

    /// <summary>The approval rate was unreadable at recording, so the record waits (fail-closed); no numbers.</summary>
    public const string ExcessRateUnreadableRule = "logistics.weighing-excess-rate-unreadable";

    /// <summary>Note (theoretical) kg against scale kg: threshold = note kg, actual = scale kg, deviation = signed difference (kg).</summary>
    public const string ScaleDifferenceRule = "logistics.weighing-scale-difference";

    /// <summary>The kilograms above the note that free physical stock does not cover: actual = the excess (kg).</summary>
    public const string UncoveredExcessRule = "logistics.weighing-uncovered-excess";

    /// <summary>Who entered the weighing or a shipment is not fully known: approval is refused (SAT-D321 (2)).</summary>
    public const string EnteredByUnknownRule = "logistics.weighing-entered-by-unknown";

    private const string PercentUnit = "%";
    private const string KilogramUnit = "kg";

    private readonly LogisticsWorkInboxRows rows;
    private readonly LogisticsWorkInboxFence fence;

    public LogisticsWeighingDifferenceWorkInboxSourceV1(LogisticsDbContext dbContext, LogisticsWorkInboxFence fence)
        : this(new LogisticsWorkInboxDbRows(dbContext), fence)
    {
    }

    internal LogisticsWeighingDifferenceWorkInboxSourceV1(LogisticsWorkInboxRows rows, LogisticsWorkInboxFence fence)
    {
        this.rows = rows;
        this.fence = fence;
    }

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => LogisticsPermissionManifestV1.WeighingDifferencesApprove;

    public WorkApprovalDurationClassV1 DurationClass => WorkApprovalDurationClassV1.Hours;

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } refused)
        {
            return refused;
        }

        var companyId = context.CompanyId;
        const int pendingApproval = (int)WeighingDifferenceRecordStatus.PendingApproval;
        List<Pending> pending;
        try
        {
            var read = await rows.ListAsync(
                (from record in rows.WeighingDifferences
                 join note in rows.DispatchNotes
                     on new { record.CompanyId, record.InstructionId, record.DispatchNoteId }
                     equals new { note.CompanyId, note.InstructionId, note.DispatchNoteId }
                 where record.CompanyId == companyId && note.CompanyId == companyId && record.Status == pendingApproval
                 orderby record.RecordedAtUtc, record.RecordId
                 select new
                 {
                     record.RecordId,
                     record.InstructionId,
                     record.DispatchNoteId,
                     record.TheoreticalKg,
                     record.ScaleKg,
                     record.DifferenceKg,
                     record.TotalExcessKg,
                     record.ApprovalRatePercent,
                     record.EnteredByComplete,
                     record.RecordedBy,
                     record.RecordedAtUtc,
                     note.SeriesCode,
                     note.SequenceNo,
                 })
                .Take(maxCount + 1),
                cancellationToken);
            pending = [.. read.Select(row => new Pending(row.RecordId, row.InstructionId, row.DispatchNoteId, row.TheoreticalKg,
                row.ScaleKg, row.DifferenceKg, row.TotalExcessKg, row.ApprovalRatePercent, row.EnteredByComplete, row.RecordedBy,
                row.RecordedAtUtc, row.SeriesCode, row.SequenceNo))];
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.UnavailableAsync(context, ApproverPermission, cancellationToken);
        }

        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var items = new List<WorkInboxItemV1>(Math.Min(pending.Count, maxCount));
        foreach (var row in pending.Take(maxCount))
        {
            if (!TryItem(row, out var item))
            {
                // A record Logistics cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(companyId, items, pending.Count > maxCount);
    }

    private static bool TryItem(Pending row, out WorkInboxItemV1 item)
    {
        item = null!;
        if (row.RecordId == Guid.Empty || row.InstructionId == Guid.Empty || row.DispatchNoteId == Guid.Empty
            || string.IsNullOrWhiteSpace(row.RecordedBy) || row.TheoreticalKg < 0m || row.ScaleKg < 0m || row.TotalExcessKg < 0m
            || (row.SeriesCode is null) != (row.SequenceNo is null))
        {
            return false;
        }

        // The note's printed number once it is approved, else its identity (a draft has no number yet).
        string noteLabel;
        try
        {
            noteLabel = row.SeriesCode is { } series && row.SequenceNo is { } sequence
                ? DispatchNoteNumber.Of(series, sequence).Text
                : LogisticsWorkInboxFence.Id(row.DispatchNoteId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var rules = new List<WorkApprovalRuleResultV1>(4);
        if (row.ApprovalRatePercent is { } rate)
        {
            var ratio = WeighingDifferenceCalculator.ExcessRatioPercent(row.TotalExcessKg, row.TheoreticalKg);
            rules.Add(new(ExcessRateRule, WorkInboxRulesV1.Decimal(rate),
                ratio is { } actual ? WorkInboxRulesV1.Decimal(actual) : null, null, PercentUnit));
        }
        else
        {
            rules.Add(new(ExcessRateUnreadableRule, null, null, null, null));
        }

        rules.Add(new(ScaleDifferenceRule, WorkInboxRulesV1.Decimal(row.TheoreticalKg), WorkInboxRulesV1.Decimal(row.ScaleKg),
            WorkInboxRulesV1.Decimal(row.DifferenceKg), KilogramUnit));
        rules.Add(new(UncoveredExcessRule, null, WorkInboxRulesV1.Decimal(row.TotalExcessKg), null, KilogramUnit));
        if (!row.EnteredByComplete)
        {
            rules.Add(new(EnteredByUnknownRule, null, null, null, null));
        }

        var link = LogisticsWorkInboxFence.InstructionLink(row.InstructionId);
        item = new WorkInboxItemV1(ModuleName, KindName, row.RecordId, noteLabel, row.RecordedAtUtc.ToUniversalTime(), link,
            row.RecordedBy, rules,
            [
                new WorkApprovalReferenceV1("dispatch-note", noteLabel, row.DispatchNoteId, link),
                LogisticsWorkInboxFence.InstructionReference(row.InstructionId),
            ]);
        return true;
    }

    private sealed record Pending(Guid RecordId, Guid InstructionId, Guid DispatchNoteId, decimal TheoreticalKg, decimal ScaleKg,
        decimal DifferenceKg, decimal TotalExcessKg, decimal? ApprovalRatePercent, bool EnteredByComplete, string RecordedBy,
        DateTimeOffset RecordedAtUtc, string? SeriesCode, long? SequenceNo);
}
