using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// Test shorthand for realization facts that do not care about their own identity: each call
/// makes a new fact id and a strictly later time, so the cases read as the plant would record
/// them. Tests about identity or time call the order's methods directly.
/// </summary>
internal static class OrderSteps
{
    private static long _tick;

    public static DateTimeOffset Next() =>
        DateTimeOffset.UnixEpoch.AddSeconds(Interlocked.Increment(ref _tick));

    public static InputIssue Issue(this ProductionOrder order, Guid parentRollId, Kilograms mass) =>
        order.IssueInput(Guid.NewGuid(), parentRollId, mass, Next());

    public static InputReturn Return(this ProductionOrder order, Guid parentRollId, Kilograms mass) =>
        order.ReturnUnusedInput(Guid.NewGuid(), parentRollId, mass, Next());

    public static OutputDeclaration Declare(
        this ProductionOrder order, Guid parentRollId, OutputClass outputClass, Kilograms mass) =>
        order.DeclareOutput(Guid.NewGuid(), parentRollId, outputClass, mass, Next());

    public static WipMeasurement MeasureWip(this ProductionOrder order, Guid parentRollId, Kilograms mass) =>
        order.RecordWorkInProgress(Guid.NewGuid(), parentRollId, mass, Next());
}
