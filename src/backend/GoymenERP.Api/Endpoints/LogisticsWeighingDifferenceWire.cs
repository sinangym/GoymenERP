using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

// Decimal text preserves all decimal(18,3) kilograms across the browser boundary.
public sealed record DispatchScaleWire(Guid ProductId, string ScaleKg);
public sealed record DispatchDeclaredLineWire(int LineNo, Guid ShipmentId, Guid ProductId, string Kilograms);
public sealed record DispatchDocumentWire(Guid Ettn, string Kind, Guid DocumentId, IReadOnlyList<DispatchDeclaredLineWire> Lines,
    int ExpectedDocumentVersion, string ExpectedDocumentSha256);
public sealed record RecordDispatchKilogramsWire(Guid RecordId, Guid DispatchNoteId,
    IReadOnlyList<DispatchScaleWire> Products, DispatchDocumentWire? FinalizedDocument);
public sealed record DispatchKilogramLineWire(int LineNo, Guid ShipmentId, Guid ProductId, string TheoreticalKg, string Kilograms);
public sealed record DispatchKilogramEvidenceWire(Guid Ettn, string Kind, Guid DocumentId, int DocumentVersion, string Sha256);
public sealed record DispatchKilogramAllocationWire(IReadOnlyList<DispatchKilogramLineWire> Lines, DispatchKilogramEvidenceWire? FinalizedDocument);
public sealed record DispatchProductKilogramsWire(Guid ProductId, string NoteKg, string ScaleKg, string DifferenceKg,
    string RemainingPhysicalKg, string PhysicallyCoveredKg, string ExcessKg, int? TargetLineNo);
public sealed record DispatchKilogramsWire(Guid CompanyId, Guid RecordId, Guid DispatchNoteId, Guid InstructionId,
    string TheoreticalKg, string ScaleKg, string DifferenceKg, string BandLevel, string? WarningBandPercent,
    string? ApprovalBandPercent, string? BlockBandPercent, string TotalExcessKg, string? ExcessRatioPercent,
    string? ApprovalRatePercent, string Decision, string Status, int Version, IReadOnlyList<DispatchProductKilogramsWire> Products,
    IReadOnlyList<string> EnteredBy, bool EnteredByComplete, string RecordedBy, DateTimeOffset RecordedAtUtc,
    string? ApprovedBy, DateTimeOffset? ApprovedAtUtc, DispatchKilogramAllocationWire? KilogramAllocation);
public sealed record DispatchKilogramsResponseWire(DispatchKilogramsWire Record, bool IsReplay, string OriginalCorrelationId);

internal static partial class LogisticsWeighingDifferenceWire
{
    [GeneratedRegex(@"^(0|[1-9][0-9]{0,14})(\.[0-9]{1,3})?$", RegexOptions.CultureInvariant)]
    private static partial Regex KilogramsPattern();
    private static decimal Kg(string? value) => value is not null && KilogramsPattern().IsMatch(value)
        && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result)
        ? result : throw new JsonException("Invalid kilogram text.");
    private static string Text(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static string? Text(decimal? value) => value.HasValue ? Text(value.Value) : null;
    public static RecordWeighingDifferenceV1 Command(RecordDispatchKilogramsWire wire)
    {
        if (wire.Products is null || wire.Products.Any(x => x is null)
            || wire.FinalizedDocument is { } doc && (doc.Lines is null || doc.Lines.Any(x => x is null))) throw new JsonException();
        return new(wire.RecordId, wire.DispatchNoteId, wire.Products.Select(x => new WeighingDifferenceScaleEntryV1(x.ProductId, Kg(x.ScaleKg))).ToArray(),
            wire.FinalizedDocument is not { } document ? null : new(document.Ettn, document.Kind, document.DocumentId,
                document.Lines.Select(x => new GoymenERP.Modules.Logistics.Domain.WeighingDifferences.DispatchKilogramDeclaredLine(x.LineNo, x.ShipmentId, x.ProductId, Kg(x.Kilograms))).ToArray(),
                document.ExpectedDocumentVersion, document.ExpectedDocumentSha256));
    }
    public static DispatchKilogramsWire View(WeighingDifferenceViewV1 value) => new(value.CompanyId, value.RecordId, value.DispatchNoteId, value.InstructionId,
        Text(value.TheoreticalKg), Text(value.ScaleKg), Text(value.DifferenceKg), value.BandLevel, Text(value.WarningBandPercent), Text(value.ApprovalBandPercent),
        Text(value.BlockBandPercent), Text(value.TotalExcessKg), Text(value.ExcessRatioPercent), Text(value.ApprovalRatePercent), value.Decision, value.Status, value.Version,
        value.Products.Select(x => new DispatchProductKilogramsWire(x.ProductId, Text(x.NoteKg), Text(x.ScaleKg), Text(x.DifferenceKg), Text(x.RemainingPhysicalKg),
            Text(x.PhysicallyCoveredKg), Text(x.ExcessKg), x.TargetLineNo)).ToArray(), value.EnteredBy, value.EnteredByComplete, value.RecordedBy, value.RecordedAtUtc,
        value.ApprovedBy, value.ApprovedAtUtc, value.KilogramAllocation is not { } allocation ? null : new(
            allocation.Lines.Select(x => new DispatchKilogramLineWire(x.LineNo, x.ShipmentId, x.ProductId, Text(x.TheoreticalKg), Text(x.Kilograms))).ToArray(),
            allocation.FinalizedDocument is not { } evidence ? null : new(evidence.Ettn, evidence.Kind, evidence.DocumentId, evidence.DocumentVersion, evidence.Sha256)));
    public static DispatchKilogramsResponseWire Response(WeighingDifferenceResponseV1 value) => new(View(value.Record), value.IsReplay, value.OriginalCorrelationId);
}
