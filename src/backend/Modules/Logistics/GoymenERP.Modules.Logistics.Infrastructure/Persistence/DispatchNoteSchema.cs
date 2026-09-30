namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// AT-055 (user decision 2026-09-30): the SQL Server shape of the dispatch-note lifecycle, one text shared by the model and the
/// hand-written migration so the two cannot drift. Every group check spells out IS NOT NULL for each column it needs: a CHECK
/// passes on UNKNOWN, so a bare comparison with a NULL would let a half-written group through.
/// </summary>
internal static class DispatchNoteSchema
{
    public const string StatusCheck = "[Status] IN (1, 2, 3)";

    public const string OrderCheck = "[NoteNo] >= 1 AND [SharesBefore] >= 1 AND [CreatedStep] >= 1";

    /// <summary>Approved (2) or cancelled after approval (3): the approver, the time, the step and the system number together, or none of them.</summary>
    public const string ApprovalCheck =
        "([ApprovedStep] IS NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [SeriesCode] IS NULL AND [SequenceNo] IS NULL "
        + "AND [Status] IN (1, 3)) "
        + "OR ([ApprovedStep] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [SeriesCode] IS NOT NULL "
        + "AND [SequenceNo] IS NOT NULL AND [ApprovedStep] > [CreatedStep] AND LEN([ApprovedBy]) > 0 AND LEN([SeriesCode]) > 0 "
        + "AND [SequenceNo] >= 1 AND [Status] IN (2, 3))";

    /// <summary>Cancelled (3) exactly when the cancellation group is written, after the creation (and the approval, if any).</summary>
    public const string CancellationCheck =
        "([CancelledStep] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancelReason] IS NULL AND [Status] IN (1, 2)) "
        + "OR ([CancelledStep] IS NOT NULL AND [CancelledBy] IS NOT NULL AND [CancelledAtUtc] IS NOT NULL AND [CancelReason] IS NOT NULL "
        + "AND [CancelledStep] > [CreatedStep] AND ([ApprovedStep] IS NULL OR [CancelledStep] > [ApprovedStep]) "
        + "AND LEN([CancelledBy]) > 0 AND LEN([CancelReason]) > 0 AND [Status] = 3)";

    /// <summary>The e-İrsaliye number only on an approved note, after its approval and before any cancellation; 16 characters.</summary>
    public const string EDispatchNumberCheck =
        "([EDispatchNumberStep] IS NULL AND [EDispatchNumber] IS NULL AND [EDispatchNumberSetBy] IS NULL AND [EDispatchNumberSetAtUtc] IS NULL) "
        + "OR ([EDispatchNumberStep] IS NOT NULL AND [EDispatchNumber] IS NOT NULL AND [EDispatchNumberSetBy] IS NOT NULL "
        + "AND [EDispatchNumberSetAtUtc] IS NOT NULL AND [ApprovedStep] IS NOT NULL AND [EDispatchNumberStep] > [ApprovedStep] "
        + "AND ([CancelledStep] IS NULL OR [EDispatchNumberStep] < [CancelledStep]) AND LEN([EDispatchNumber]) = 16 "
        + "AND LEN([EDispatchNumberSetBy]) > 0)";

    public const string SeriesCheck = "LEN([SeriesCode]) > 0 AND [LastSequenceNo] >= 1";

    /// <summary>The system number is unique per company over the notes that carry one (cancelled ones keep theirs).</summary>
    public const string NumberFilter = "[SeriesCode] IS NOT NULL AND [SequenceNo] IS NOT NULL";

    /// <summary>The e-İrsaliye number is unique per company when present.</summary>
    public const string EDispatchNumberFilter = "[EDispatchNumber] IS NOT NULL";

    /// <summary>The single-link key covers the lines of non-cancelled notes only.</summary>
    public const string ActiveLineFilter = "[Released] = 0";

    /// <summary>The columns a lifecycle step writes once, from empty.</summary>
    public static readonly string[] SetOnceColumns =
    [
        nameof(DispatchNoteRow.SeriesCode), nameof(DispatchNoteRow.SequenceNo), nameof(DispatchNoteRow.ApprovedBy),
        nameof(DispatchNoteRow.ApprovedAtUtc), nameof(DispatchNoteRow.ApprovedStep), nameof(DispatchNoteRow.CancelledBy),
        nameof(DispatchNoteRow.CancelledAtUtc), nameof(DispatchNoteRow.CancelReason), nameof(DispatchNoteRow.CancelledStep),
        nameof(DispatchNoteRow.EDispatchNumber), nameof(DispatchNoteRow.EDispatchNumberSetBy),
        nameof(DispatchNoteRow.EDispatchNumberSetAtUtc), nameof(DispatchNoteRow.EDispatchNumberStep),
    ];
}
