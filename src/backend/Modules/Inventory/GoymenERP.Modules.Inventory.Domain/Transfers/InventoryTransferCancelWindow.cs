namespace GoymenERP.Modules.Inventory.Domain.Transfers;

/// <summary>
/// KLN-D25 (7) / LOJ-D10: the source warehouse "kantardan geçer" setting as Inventory sees it. An undefined setting is
/// <see cref="Off"/>; a setting that cannot be read is <see cref="Unreadable"/> (a read error, never a policy value).
/// </summary>
public enum InventoryTransferSourceScaleSetting
{
    Off = 1,
    On = 2,
    Unreadable = 3,
}

/// <summary>KLN-D25 (6): whether the dispatch is tied to an e-İrsaliye. Unreadable is a read error.</summary>
public enum InventoryTransferEDispatchLink
{
    NotLinked = 1,
    Linked = 2,
    Unreadable = 3,
}

/// <summary>A departure-side fact (exit weighing, vehicle exit, GİB send) as its owner reports it now.</summary>
public enum InventoryTransferDepartureFactStatus
{
    NotOccurred = 1,
    Occurred = 2,
    Unreadable = 3,
}

/// <summary>The persisted reason the cancellation window closed (KLN-D27: moment and basis are kept).</summary>
public enum InventoryTransferCancelClosureBasis
{
    /// <summary>The source warehouse does not pass through the scale and no e-İrsaliye is linked: closes at dispatch.</summary>
    DispatchRecorded = 1,

    ExitWeighing = 2,
    VehicleExit = 3,
    EDispatchSentToGib = 4,
}

public enum InventoryTransferCancelWindowState
{
    Open = 1,
    Closed = 2,
    Unreadable = 3,
}

public sealed record InventoryTransferDepartureFact(
    InventoryTransferDepartureFactStatus Status,
    DateTimeOffset? OccurredAtUtc)
{
    public static InventoryTransferDepartureFact NotOccurred { get; } = new(InventoryTransferDepartureFactStatus.NotOccurred, null);
    public static InventoryTransferDepartureFact Unreadable { get; } = new(InventoryTransferDepartureFactStatus.Unreadable, null);
    public static InventoryTransferDepartureFact At(DateTimeOffset occurredAtUtc) =>
        new(InventoryTransferDepartureFactStatus.Occurred, occurredAtUtc);

    /// <summary>A malformed fact (occurred without a UTC moment, or a moment on a non-occurred fact) is unreadable.</summary>
    internal bool IsWellFormed => Status switch
    {
        InventoryTransferDepartureFactStatus.Occurred => OccurredAtUtc is { Offset.Ticks: 0 },
        InventoryTransferDepartureFactStatus.NotOccurred or InventoryTransferDepartureFactStatus.Unreadable =>
            OccurredAtUtc is null,
        _ => false,
    };
}

/// <summary>The owner facts read for one dispatch at one moment (Logistics/e-İrsaliye side).</summary>
public sealed record InventoryTransferDepartureFacts(
    InventoryTransferEDispatchLink EDispatchLink,
    DateTimeOffset? EDispatchLinkedAtUtc,
    InventoryTransferDepartureFact EDispatchSentToGib,
    InventoryTransferDepartureFact ExitWeighing,
    InventoryTransferDepartureFact VehicleExit);

public sealed record InventoryTransferCancelWindowDecision(
    InventoryTransferCancelWindowState State,
    InventoryTransferCancelClosureBasis? ClosureBasis,
    DateTimeOffset? ClosedAtUtc,
    bool RequiresLogisticsNoExitConfirmation,
    bool RequiresEDispatchCancelConfirmation)
{
    public static InventoryTransferCancelWindowDecision Unreadable { get; } =
        new(InventoryTransferCancelWindowState.Unreadable, null, null, false, false);

    public static InventoryTransferCancelWindowDecision Closed(InventoryTransferCancelClosureBasis basis, DateTimeOffset at) =>
        new(InventoryTransferCancelWindowState.Closed, basis, at, false, false);

    public static InventoryTransferCancelWindowDecision Open(bool requiresLogistics, bool requiresEDispatch) =>
        new(InventoryTransferCancelWindowState.Open, null, null, requiresLogistics, requiresEDispatch);
}

/// <summary>
/// The pure KLN-D25 (6)/(7) and KLN-D27 cancellation-window rule. It never chooses a policy value: the setting values
/// and owner facts are inputs, every unreadable or malformed input closes the decision as <c>Unreadable</c>
/// (fail closed), and a persisted closure is checked by the caller first because a closed window never reopens.
/// <list type="bullet">
/// <item>Not linked: the closed value of stored (at dispatch) and current settings wins; unreadable counts as off for
/// stored and as a read error for current. Off closes at dispatch. On stays open until exit weighing or vehicle exit
/// and needs a fresh Logistics "no exit and none later" confirmation.</item>
/// <item>Linked: open until the GİB send and needs an e-İrsaliye cancel confirmation. If either setting is on or
/// unreadable, KLN-D27 applies: the earliest of exit weighing, vehicle exit or GİB send closes it and both
/// confirmations are required.</item>
/// <item>A link created after dispatch never reopens the unlinked decision: the more closed of both readings wins.</item>
/// </list>
/// </summary>
public static class InventoryTransferCancelWindow
{
    public static InventoryTransferCancelWindowDecision Evaluate(
        InventoryTransferSourceScaleSetting storedAtDispatch,
        InventoryTransferSourceScaleSetting current,
        InventoryTransferDepartureFacts facts,
        DateTimeOffset dispatchedAtUtc)
    {
        if (!Enum.IsDefined(storedAtDispatch) || !Enum.IsDefined(current) || facts is null
            || facts.EDispatchSentToGib is null || facts.ExitWeighing is null || facts.VehicleExit is null
            || !facts.EDispatchSentToGib.IsWellFormed || !facts.ExitWeighing.IsWellFormed || !facts.VehicleExit.IsWellFormed
            || dispatchedAtUtc.Offset != TimeSpan.Zero)
        {
            return InventoryTransferCancelWindowDecision.Unreadable;
        }

        switch (facts.EDispatchLink)
        {
            case InventoryTransferEDispatchLink.NotLinked:
                return facts.EDispatchLinkedAtUtc is null
                    ? Unlinked(storedAtDispatch, current, facts, dispatchedAtUtc)
                    : InventoryTransferCancelWindowDecision.Unreadable;
            case InventoryTransferEDispatchLink.Linked:
                if (facts.EDispatchLinkedAtUtc is not { Offset.Ticks: 0 } linkedAtUtc)
                {
                    return InventoryTransferCancelWindowDecision.Unreadable;
                }

                var linked = Linked(storedAtDispatch, current, facts);
                return linkedAtUtc > dispatchedAtUtc
                    ? MoreClosed(Unlinked(storedAtDispatch, current, facts, dispatchedAtUtc), linked)
                    : linked;
            default:
                return InventoryTransferCancelWindowDecision.Unreadable;
        }
    }

    private static InventoryTransferCancelWindowDecision Unlinked(
        InventoryTransferSourceScaleSetting stored,
        InventoryTransferSourceScaleSetting current,
        InventoryTransferDepartureFacts facts,
        DateTimeOffset dispatchedAtUtc)
    {
        if (stored != InventoryTransferSourceScaleSetting.On || current == InventoryTransferSourceScaleSetting.Off)
        {
            return InventoryTransferCancelWindowDecision.Closed(
                InventoryTransferCancelClosureBasis.DispatchRecorded, dispatchedAtUtc);
        }

        if (current == InventoryTransferSourceScaleSetting.Unreadable)
        {
            return InventoryTransferCancelWindowDecision.Unreadable;
        }

        return FromFacts(
            [
                (InventoryTransferCancelClosureBasis.ExitWeighing, facts.ExitWeighing),
                (InventoryTransferCancelClosureBasis.VehicleExit, facts.VehicleExit),
            ],
            requiresLogistics: true,
            requiresEDispatch: false);
    }

    private static InventoryTransferCancelWindowDecision Linked(
        InventoryTransferSourceScaleSetting stored,
        InventoryTransferSourceScaleSetting current,
        InventoryTransferDepartureFacts facts)
    {
        var scalePath = stored != InventoryTransferSourceScaleSetting.Off || current != InventoryTransferSourceScaleSetting.Off;
        List<(InventoryTransferCancelClosureBasis, InventoryTransferDepartureFact)> relevant =
            [(InventoryTransferCancelClosureBasis.EDispatchSentToGib, facts.EDispatchSentToGib)];
        if (scalePath)
        {
            relevant.Add((InventoryTransferCancelClosureBasis.ExitWeighing, facts.ExitWeighing));
            relevant.Add((InventoryTransferCancelClosureBasis.VehicleExit, facts.VehicleExit));
        }

        return FromFacts(relevant, requiresLogistics: scalePath, requiresEDispatch: true);
    }

    private static InventoryTransferCancelWindowDecision FromFacts(
        IReadOnlyList<(InventoryTransferCancelClosureBasis Basis, InventoryTransferDepartureFact Fact)> relevant,
        bool requiresLogistics,
        bool requiresEDispatch)
    {
        // An occurred fact closes the window even when another fact is unreadable: the window is already shut, and a
        // closed window never reopens. Only when nothing closed it does an unreadable fact make the decision unknown.
        var closing = relevant
            .Where(item => item.Fact.Status == InventoryTransferDepartureFactStatus.Occurred)
            .OrderBy(item => item.Fact.OccurredAtUtc!.Value)
            .ThenBy(item => item.Basis)
            .ToArray();
        if (closing.Length != 0)
        {
            return InventoryTransferCancelWindowDecision.Closed(closing[0].Basis, closing[0].Fact.OccurredAtUtc!.Value);
        }

        return relevant.Any(item => item.Fact.Status != InventoryTransferDepartureFactStatus.NotOccurred)
            ? InventoryTransferCancelWindowDecision.Unreadable
            : InventoryTransferCancelWindowDecision.Open(requiresLogistics, requiresEDispatch);
    }

    private static InventoryTransferCancelWindowDecision MoreClosed(
        InventoryTransferCancelWindowDecision first,
        InventoryTransferCancelWindowDecision second)
    {
        if (first.State == InventoryTransferCancelWindowState.Closed
            && second.State == InventoryTransferCancelWindowState.Closed)
        {
            return first.ClosedAtUtc <= second.ClosedAtUtc ? first : second;
        }

        if (first.State == InventoryTransferCancelWindowState.Closed) return first;
        if (second.State == InventoryTransferCancelWindowState.Closed) return second;
        if (first.State == InventoryTransferCancelWindowState.Unreadable
            || second.State == InventoryTransferCancelWindowState.Unreadable)
        {
            return InventoryTransferCancelWindowDecision.Unreadable;
        }

        return InventoryTransferCancelWindowDecision.Open(
            first.RequiresLogisticsNoExitConfirmation || second.RequiresLogisticsNoExitConfirmation,
            first.RequiresEDispatchCancelConfirmation || second.RequiresEDispatchCancelConfirmation);
    }
}
