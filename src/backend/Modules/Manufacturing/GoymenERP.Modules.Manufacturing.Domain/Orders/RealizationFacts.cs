using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

// Plan v2 R6 ("OperationalFact = sourceEventId, …, zaman; source fact korunur") and §7.3
// (InputIssue and WipPosition are records of their own): every realization input is kept as the
// fact it is — its own id, its parent roll, its mass and when it happened — and the order's
// totals are derived from these facts, never kept instead of them. The same id applied twice is
// refused, so a retried or duplicated slip cannot double a mass silently. Every fact recorded in
// this slice is an actual measurement; estimated facts are not modelled here.

/// <summary>Input actually taken from a parent roll into production.</summary>
public sealed record InputIssue(Guid IssueId, Guid ParentRollId, Kilograms Mass, DateTimeOffset IssuedAtUtc);

/// <summary>Unused input sent back from production.</summary>
public sealed record InputReturn(Guid ReturnId, Guid ParentRollId, Kilograms Mass, DateTimeOffset ReturnedAtUtc);

/// <summary>
/// A measurement of the mass of one parent roll still in process. The latest measurement is the
/// roll's work in progress; earlier ones stay as history.
/// </summary>
public sealed record WipMeasurement(Guid MeasurementId, Guid ParentRollId, Kilograms Mass, DateTimeOffset MeasuredAtUtc);
