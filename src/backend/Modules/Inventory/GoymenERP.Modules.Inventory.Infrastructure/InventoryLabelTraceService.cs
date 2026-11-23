using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>One stored movement line of a traced label, as the company-filtered reader answers it (raw enum values).</summary>
internal sealed record InventoryLabelTraceMovementLine(
    Guid MovementId,
    int LineNumber,
    Guid LabelId,
    int Kind,
    DateTimeOffset OccurredAtUtc,
    string SourceKind,
    Guid SourceId,
    Guid? SourceLineId,
    int Direction,
    decimal QuantityKg);

internal sealed record InventoryLabelTraceLabel(Guid LabelId, string Code);

internal interface IInventoryLabelTraceReader
{
    /// <summary>The company's movement lines of the labels whose source kind is a trace step, oldest first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<InventoryLabelTraceMovementLine>> ReadMovementLinesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> labelIds,
        IReadOnlyCollection<string> sourceKinds,
        int take,
        CancellationToken cancellationToken);

    /// <summary>The company's labels whose code starts with <paramref name="prefix"/>, ordered by code, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<InventoryLabelTraceLabel>> SearchLabelsAsync(
        Guid companyId,
        string prefix,
        int take,
        CancellationToken cancellationToken);

    /// <summary>The company's labels among <paramref name="labelIds"/>; another company's label is simply absent.</summary>
    Task<IReadOnlyList<InventoryLabelTraceLabel>> FindLabelsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> labelIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// STK-S33 (KLN-D44, KLN-D41): the fenced owner read behind the label trace. The label-detail permissions are measured fresh
/// before and after every awaited read; the genealogy and the stock movements are Inventory's own and fail closed (503), while the
/// production shares, the parties and their names are other owners' answers that only enrich the trace: when one of them cannot
/// answer, the trace still answers and says so in its status, never with a guessed value.
/// </summary>
internal sealed class InventoryLabelTraceService(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IInventoryLabelGenealogyReaderV1? genealogy,
    IInventoryLabelTraceReader? reader,
    IInventoryLabelTraceProductionSourceV1? production,
    IInventoryLabelTracePartySourceV1? parties,
    IInventoryStockOwnerDisplaySourceV1? partyNames,
    IInventoryLabelTraceSupplierSearchSourceV1? supplierSearch) : IInventoryLabelTraceV1
{
    /// <summary>The party display owner's largest batch.</summary>
    internal const int PartyNameBatchSize = 100;

    public async Task<InventoryLabelTraceV1> ReadAsync(
        Guid labelId,
        InventoryLabelTraceDirectionV1 direction,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (labelId == Guid.Empty || !Enum.IsDefined(direction))
        {
            throw Error(400, InventoryLabelTraceCodesV1.InvalidQuery);
        }

        IInventoryLabelGenealogyReaderV1 genealogyReader = genealogy ?? throw Error(503, InventoryLabelTraceCodesV1.Unavailable);
        IInventoryLabelTraceReader movementReader = reader ?? throw Error(503, InventoryLabelTraceCodesV1.Unavailable);
        var chain = await FencedAsync(
            () => genealogyReader.ReadAsync(labelId, InventoryLabelTraceApiV1.GenealogyWindow, cancellationToken),
            scope,
            cancellationToken);
        if (chain is null)
        {
            throw Error(404, InventoryLabelTraceCodesV1.NotFound);
        }
        ValidateGenealogy(chain, labelId);

        var walk = Walk(chain, direction);
        var lines = await FencedAsync(
            () => movementReader.ReadMovementLinesAsync(scope.CompanyId, [.. walk.Nodes.Select(node => node.LabelId)],
                InventoryLabelTraceStepsV1.SourceKinds, InventoryLabelTraceApiV1.MaximumEvents + 1, cancellationToken),
            scope,
            cancellationToken);
        var events = Events(lines, walk.Nodes);
        var windowFull = walk.WindowFull || lines.Count > InventoryLabelTraceApiV1.MaximumEvents;

        var (links, productionStatus) = await EnrichProductionAsync(walk.Links, scope, cancellationToken);
        var (tracedEvents, partyStatus) = await EnrichPartiesAsync(events, walk.Nodes, scope, cancellationToken);
        var (partyList, partyNameStatus) = await ResolvePartyNamesAsync(tracedEvents, scope, cancellationToken);

        await RequireAsync(scope, cancellationToken);
        return new InventoryLabelTraceV1(
            chain.LabelId,
            chain.LabelCode,
            direction == InventoryLabelTraceDirectionV1.Forward
                ? InventoryLabelTraceApiV1.Forward
                : InventoryLabelTraceApiV1.Backward,
            walk.Nodes,
            links,
            tracedEvents,
            partyList,
            productionStatus,
            partyStatus,
            partyNameStatus,
            windowFull);
    }

    public async Task<InventoryLabelTraceSearchV1> SearchAsync(string text, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length is < InventoryLabelTraceApiV1.MinimumSearchLength or > InventoryLabelTraceApiV1.MaximumSearchLength
            || trimmed.Any(char.IsControl))
        {
            throw Error(400, InventoryLabelTraceCodesV1.InvalidQuery);
        }

        IInventoryLabelTraceReader labels = reader ?? throw Error(503, InventoryLabelTraceCodesV1.Unavailable);
        var take = InventoryLabelTraceApiV1.MaximumSearchResults + 1;
        var own = await FencedAsync(
            () => labels.SearchLabelsAsync(scope.CompanyId, trimmed, take, cancellationToken),
            scope,
            cancellationToken);
        if (own is null || own.Count > take || own.Any(label => !ValidLabel(label)))
        {
            throw Error(503, InventoryLabelTraceCodesV1.Unavailable);
        }

        var merged = new Dictionary<Guid, InventoryLabelTraceSearchItemV1>();
        foreach (var label in own)
        {
            if (!merged.TryAdd(label.LabelId,
                    new InventoryLabelTraceSearchItemV1(label.LabelId, label.Code, null, InventoryLabelTraceMatchesV1.LabelCode)))
            {
                throw Error(503, InventoryLabelTraceCodesV1.Unavailable);
            }
        }

        var supplierStatus = InventoryLabelTraceStatusesV1.NotAnswered;
        var supplierWindowFull = false;
        var supplierAnswer = new SoftAnswer<InventoryLabelTraceSupplierSearchAnswerV1>(null, false);
        if (supplierSearch is not null)
        {
            IInventoryLabelTraceSupplierSearchSourceV1 supplierSource = supplierSearch;
            supplierAnswer = await SoftAsync(
                () => supplierSource.SearchAsync(scope.CompanyId, trimmed, take, cancellationToken),
                scope,
                cancellationToken);
            if (supplierAnswer.Value is null)
            {
                supplierAnswer = supplierAnswer with { Failed = true };
            }
        }

        if (supplierAnswer.Failed)
        {
            supplierStatus = InventoryLabelTraceStatusesV1.Unavailable;
        }
        else if (supplierAnswer.Value is { Answered: true } answered)
        {
            if (answered.Items is null || answered.Items.Count > take || answered.Items.Any(item => item is null
                    || item.LabelId == Guid.Empty || !ValidText(item.LabelCode) || !ValidOptionalText(item.SupplierLabelNumber)))
            {
                supplierStatus = InventoryLabelTraceStatusesV1.Unavailable;
            }
            else
            {
                supplierStatus = InventoryLabelTraceStatusesV1.Answered;
                supplierWindowFull = answered.Items.Count > InventoryLabelTraceApiV1.MaximumSearchResults;
                var supplierIds = answered.Items.Select(item => item.LabelId).Distinct().ToArray();
                IReadOnlyList<InventoryLabelTraceLabel>? known = supplierIds.Length == 0
                    ? Array.Empty<InventoryLabelTraceLabel>()
                    : await FencedAsync(
                        () => labels.FindLabelsAsync(scope.CompanyId, supplierIds, cancellationToken),
                        scope,
                        cancellationToken);
                if (known is null || known.Any(label => !ValidLabel(label) || !supplierIds.Contains(label.LabelId)))
                {
                    throw Error(503, InventoryLabelTraceCodesV1.Unavailable);
                }

                var codes = known.GroupBy(label => label.LabelId).ToDictionary(group => group.Key, group => group.First().Code);
                foreach (var item in answered.Items)
                {
                    if (!codes.TryGetValue(item.LabelId, out var code))
                    {
                        // Purchasing knows a label Inventory does not hold (yet): it cannot be traced, so it is not offered.
                        continue;
                    }

                    var number = item.SupplierLabelNumber?.Trim();
                    if (merged.TryGetValue(item.LabelId, out var existing))
                    {
                        merged[item.LabelId] = existing with { SupplierLabelNumber = existing.SupplierLabelNumber ?? number };
                        continue;
                    }

                    merged[item.LabelId] = new InventoryLabelTraceSearchItemV1(item.LabelId, code, number,
                        code.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
                            ? InventoryLabelTraceMatchesV1.LabelCode
                            : InventoryLabelTraceMatchesV1.SupplierLabelNumber);
                }
            }
        }

        await RequireAsync(scope, cancellationToken);
        var ordered = merged.Values.OrderBy(item => item.Code, StringComparer.Ordinal).ThenBy(item => item.LabelId).ToArray();
        return new InventoryLabelTraceSearchV1(
            [.. ordered.Take(InventoryLabelTraceApiV1.MaximumSearchResults)],
            own.Count > InventoryLabelTraceApiV1.MaximumSearchResults || supplierWindowFull
                || ordered.Length > InventoryLabelTraceApiV1.MaximumSearchResults,
            supplierStatus);
    }

    internal sealed record TraceWalk(
        IReadOnlyList<InventoryLabelTraceNodeV1> Nodes,
        IReadOnlyList<InventoryLabelGenealogyEdgeV1> Links,
        bool WindowFull);

    /// <summary>
    /// The directed walk over the undirected genealogy window: forward follows source → output, backward output → source, breadth
    /// first so each label carries its shortest step count. A label is reached once; the walk stops at
    /// <see cref="InventoryLabelTraceApiV1.MaximumLabels"/> and then, like a full genealogy window, sets the window flag.
    /// </summary>
    internal static TraceWalk Walk(InventoryLabelGenealogyV1 chain, InventoryLabelTraceDirectionV1 direction)
    {
        var forward = direction == InventoryLabelTraceDirectionV1.Forward;
        var codes = new Dictionary<Guid, string> { [chain.LabelId] = chain.LabelCode };
        foreach (var edge in chain.Edges)
        {
            codes.TryAdd(edge.SourceLabelId, edge.SourceCode);
            codes.TryAdd(edge.OutputLabelId, edge.OutputCode);
        }

        var nodes = new List<InventoryLabelTraceNodeV1> { new(chain.LabelId, chain.LabelCode, 0) };
        var depths = new Dictionary<Guid, int> { [chain.LabelId] = 0 };
        var links = new List<InventoryLabelGenealogyEdgeV1>();
        var linkKeys = new HashSet<(Guid, Guid, Guid)>();
        var windowFull = chain.WindowFull;
        var frontier = new List<Guid> { chain.LabelId };
        while (frontier.Count > 0)
        {
            var next = new List<Guid>();
            foreach (var from in frontier)
            {
                var depth = depths[from];
                var outgoing = chain.Edges
                    .Where(edge => (forward ? edge.SourceLabelId : edge.OutputLabelId) == from)
                    .OrderBy(edge => edge.OccurredAtUtc).ThenBy(edge => edge.TransformationId)
                    .ThenBy(edge => forward ? edge.OutputCode : edge.SourceCode, StringComparer.Ordinal);
                foreach (var edge in outgoing)
                {
                    var to = forward ? edge.OutputLabelId : edge.SourceLabelId;
                    if (!depths.ContainsKey(to))
                    {
                        if (nodes.Count >= InventoryLabelTraceApiV1.MaximumLabels)
                        {
                            windowFull = true;
                            continue;
                        }

                        depths[to] = depth + 1;
                        nodes.Add(new InventoryLabelTraceNodeV1(to, codes[to], depth + 1));
                        next.Add(to);
                    }

                    if (linkKeys.Add((edge.TransformationId, edge.SourceLabelId, edge.OutputLabelId)))
                    {
                        links.Add(edge);
                    }
                }
            }
            frontier = next;
        }

        return new TraceWalk(nodes, links, windowFull);
    }

    private static void ValidateGenealogy(InventoryLabelGenealogyV1 chain, Guid labelId)
    {
        if (chain.LabelId != labelId || !ValidText(chain.LabelCode) || chain.Edges is null)
        {
            throw Error(503, InventoryLabelTraceCodesV1.Unavailable);
        }

        var codes = new Dictionary<Guid, string> { [chain.LabelId] = chain.LabelCode };
        foreach (var edge in chain.Edges)
        {
            if (edge is null || edge.TransformationId == Guid.Empty || edge.OccurredAtUtc.Offset != TimeSpan.Zero
                || !Enum.TryParse<InventoryLabelTransformationKind>(edge.Kind, out var kind) || !Enum.IsDefined(kind)
                || !string.Equals(kind.ToString(), edge.Kind, StringComparison.Ordinal)
                || edge.SourceLabelId == Guid.Empty || edge.OutputLabelId == Guid.Empty
                || edge.SourceLabelId == edge.OutputLabelId
                || !ValidText(edge.SourceCode) || !ValidText(edge.OutputCode)
                || !CanonicalQuantity(edge.SourceQuantityKg) || !CanonicalQuantity(edge.OutputQuantityKg)
                || !codes.TryAdd(edge.SourceLabelId, edge.SourceCode) && codes[edge.SourceLabelId] != edge.SourceCode
                || !codes.TryAdd(edge.OutputLabelId, edge.OutputCode) && codes[edge.OutputLabelId] != edge.OutputCode)
            {
                throw Error(503, InventoryLabelTraceCodesV1.Unavailable);
            }
        }
    }

    private static IReadOnlyList<InventoryLabelTraceEventV1> Events(
        IReadOnlyList<InventoryLabelTraceMovementLine>? lines,
        IReadOnlyList<InventoryLabelTraceNodeV1> nodes)
    {
        if (lines is null || lines.Count > InventoryLabelTraceApiV1.MaximumEvents + 1)
        {
            throw Error(503, InventoryLabelTraceCodesV1.Unavailable);
        }

        var labelIds = nodes.Select(node => node.LabelId).ToHashSet();
        var keys = new HashSet<(Guid, int)>();
        var events = new List<InventoryLabelTraceEventV1>();
        foreach (var line in lines.Take(InventoryLabelTraceApiV1.MaximumEvents))
        {
            var step = InventoryLabelTraceStepsV1.StepOf(line?.SourceKind);
            if (line is null || step is null || line.MovementId == Guid.Empty || line.LineNumber <= 0
                || !keys.Add((line.MovementId, line.LineNumber)) || !labelIds.Contains(line.LabelId)
                || line.SourceId == Guid.Empty || line.SourceLineId == Guid.Empty
                || !Enum.IsDefined(typeof(StockMovementKind), line.Kind)
                || !Enum.IsDefined(typeof(StockDirection), line.Direction)
                || !TryKilograms(line.QuantityKg, out var quantity))
            {
                throw Error(503, InventoryLabelTraceCodesV1.Unavailable);
            }

            events.Add(new InventoryLabelTraceEventV1(
                line.MovementId,
                line.LineNumber,
                line.LabelId,
                step,
                Enum.GetName(typeof(StockMovementKind), line.Kind)!,
                line.OccurredAtUtc,
                line.SourceKind,
                line.SourceId,
                line.SourceLineId,
                Enum.GetName(typeof(StockDirection), line.Direction)!,
                quantity,
                null,
                null));
        }

        return events;
    }

    private async Task<(IReadOnlyList<InventoryLabelTraceLinkV1>, string)> EnrichProductionAsync(
        IReadOnlyList<InventoryLabelGenealogyEdgeV1> edges,
        Scope scope,
        CancellationToken cancellationToken)
    {
        var manufacturing = nameof(InventoryLabelTransformationKind.Manufacturing);
        var outputs = edges.Where(edge => edge.Kind == manufacturing).Select(edge => edge.OutputLabelId).Distinct().ToArray();
        IReadOnlyList<InventoryLabelTraceProductionShareV1> shares = [];
        string status;
        if (outputs.Length == 0)
        {
            status = InventoryLabelTraceStatusesV1.Answered;
        }
        else if (production is null)
        {
            status = InventoryLabelTraceStatusesV1.NotAnswered;
        }
        else
        {
            IInventoryLabelTraceProductionSourceV1 productionSource = production;
            var answer = await SoftAsync(
                () => productionSource.ReadSharesAsync(scope.CompanyId, outputs, cancellationToken),
                scope,
                cancellationToken);
            if (answer.Failed || answer.Value is null)
            {
                status = InventoryLabelTraceStatusesV1.Unavailable;
            }
            else if (!answer.Value.Answered)
            {
                status = InventoryLabelTraceStatusesV1.NotAnswered;
            }
            else if (answer.Value.Items is null || answer.Value.Items.Any(item => item is null
                         || !outputs.Contains(item.OutputLabelId) || item.InputLabelId == Guid.Empty
                         || item.OrderId == Guid.Empty || item.TransformationId == Guid.Empty || item.ConsumedKg < 0m
                         || item.Share is < 0m or > 1m || !ValidText(item.ShareStatus)))
            {
                status = InventoryLabelTraceStatusesV1.Unavailable;
            }
            else
            {
                shares = answer.Value.Items;
                status = InventoryLabelTraceStatusesV1.Answered;
            }
        }

        var links = new List<InventoryLabelTraceLinkV1>(edges.Count);
        var missing = false;
        foreach (var edge in edges)
        {
            InventoryLabelTraceProductionShareV1? share = null;
            if (edge.Kind == manufacturing)
            {
                share = shares.FirstOrDefault(item => item.OutputLabelId == edge.OutputLabelId
                    && item.InputLabelId == edge.SourceLabelId && item.TransformationId == edge.TransformationId);
                missing |= share is null;
            }

            links.Add(new InventoryLabelTraceLinkV1(
                edge.TransformationId,
                edge.Kind,
                edge.OccurredAtUtc,
                edge.SourceLabelId,
                edge.OutputLabelId,
                edge.SourceQuantityKg,
                edge.OutputQuantityKg,
                share?.OrderId,
                share is null ? null : Plain(share.ConsumedKg),
                share?.Share is { } ratio ? Plain(ratio) : null,
                share?.ShareStatus));
        }

        if (status == InventoryLabelTraceStatusesV1.Answered && missing)
        {
            status = InventoryLabelTraceStatusesV1.Partial;
        }

        return (links, status);
    }

    private async Task<(IReadOnlyList<InventoryLabelTraceEventV1>, string)> EnrichPartiesAsync(
        IReadOnlyList<InventoryLabelTraceEventV1> events,
        IReadOnlyList<InventoryLabelTraceNodeV1> nodes,
        Scope scope,
        CancellationToken cancellationToken)
    {
        var codes = nodes.ToDictionary(node => node.LabelId, node => node.Code);
        var sources = events
            .Where(item => RoleOf(item.Step) is not null)
            .Select(item => new InventoryLabelTracePartySourceV1(item.SourceKind, item.SourceId, item.SourceLineId,
                item.LabelId, codes[item.LabelId]))
            .Distinct()
            .ToArray();
        if (sources.Length == 0)
        {
            return (events, InventoryLabelTraceStatusesV1.Answered);
        }
        if (parties is null)
        {
            return (events, InventoryLabelTraceStatusesV1.NotAnswered);
        }

        IInventoryLabelTracePartySourceV1 partySource = parties;
        var answer = await SoftAsync(
            () => partySource.ResolveAsync(new InventoryLabelTracePartyRequestV1(scope.CompanyId, scope.ActorId, sources),
                cancellationToken),
            scope,
            cancellationToken);
        if (answer.Failed || answer.Value is null)
        {
            return (events, InventoryLabelTraceStatusesV1.Unavailable);
        }
        if (!answer.Value.Answered)
        {
            return (events, InventoryLabelTraceStatusesV1.NotAnswered);
        }

        var requested = sources.Select(Key).ToHashSet();
        if (answer.Value.Items is null)
        {
            return (events, InventoryLabelTraceStatusesV1.Unavailable);
        }

        var matches = new Dictionary<(string, Guid, Guid?, Guid), InventoryLabelTracePartyMatchV1>();
        foreach (var match in answer.Value.Items)
        {
            if (match is null || !requested.Contains((match.SourceKind, match.SourceId, match.SourceLineId, match.LabelId))
                || match.Role != RoleOf(InventoryLabelTraceStepsV1.StepOf(match.SourceKind))
                || match.PartyId == Guid.Empty || !ValidOptionalText(match.SupplierLabelNumber)
                || match.Role != InventoryLabelTracePartyRolesV1.Supplier && match.SupplierLabelNumber is not null
                || !matches.TryAdd((match.SourceKind, match.SourceId, match.SourceLineId, match.LabelId), match))
            {
                return (events, InventoryLabelTraceStatusesV1.Unavailable);
            }
        }

        var resolved = events.Select(item => matches.TryGetValue(
                (item.SourceKind, item.SourceId, item.SourceLineId, item.LabelId), out var match)
            ? item with { PartyId = match.PartyId, SupplierLabelNumber = match.SupplierLabelNumber?.Trim() }
            : item).ToArray();
        var complete = sources.All(source => matches.TryGetValue(Key(source), out var match) && match.PartyId is not null);
        return (resolved, complete ? InventoryLabelTraceStatusesV1.Answered : InventoryLabelTraceStatusesV1.Partial);

        static (string, Guid, Guid?, Guid) Key(InventoryLabelTracePartySourceV1 source) =>
            (source.SourceKind, source.SourceId, source.SourceLineId, source.LabelId);
    }

    private async Task<(IReadOnlyList<InventoryLabelTracePartyV1>, string)> ResolvePartyNamesAsync(
        IReadOnlyList<InventoryLabelTraceEventV1> events,
        Scope scope,
        CancellationToken cancellationToken)
    {
        var roles = events
            .Where(item => item.PartyId is not null && RoleOf(item.Step) is not null)
            .Select(item => (PartyId: item.PartyId!.Value, Role: RoleOf(item.Step)!))
            .Distinct()
            .OrderBy(item => item.Role, StringComparer.Ordinal).ThenBy(item => item.PartyId)
            .ToArray();
        if (roles.Length == 0)
        {
            return ([], InventoryLabelTraceStatusesV1.Answered);
        }
        if (partyNames is null)
        {
            return ([], InventoryLabelTraceStatusesV1.NotAnswered);
        }

        IInventoryStockOwnerDisplaySourceV1 nameSource = partyNames;
        var partyIds = roles.Select(item => item.PartyId).Distinct().ToArray();
        var names = new Dictionary<Guid, InventoryStockOwnerDisplayV1>();
        foreach (var batch in partyIds.Chunk(PartyNameBatchSize))
        {
            var answer = await SoftAsync(
                () => nameSource.GetAuthorizedBatchAsync(
                    new InventoryStockOwnerDisplayBatchRequestV1(scope.CompanyId, scope.ActorId, batch), cancellationToken),
                scope,
                cancellationToken);
            if (answer.Failed || answer.Value is null)
            {
                return ([], InventoryLabelTraceStatusesV1.Unavailable);
            }
            if (answer.Value.Outcome == InventoryStockOwnerDisplayBatchOutcomeV1.Denied)
            {
                return ([], InventoryLabelTraceStatusesV1.Denied);
            }
            if (answer.Value.Outcome != InventoryStockOwnerDisplayBatchOutcomeV1.Found || answer.Value.Items is null)
            {
                return ([], InventoryLabelTraceStatusesV1.Unavailable);
            }

            foreach (var item in answer.Value.Items)
            {
                if (item is null || !batch.Contains(item.PartyId) || !ValidText(item.Code) || !ValidText(item.LegalName)
                    || !ValidOptionalText(item.TradeName) || !names.TryAdd(item.PartyId, item))
                {
                    return ([], InventoryLabelTraceStatusesV1.Unavailable);
                }
            }
        }

        var list = roles.Where(item => names.ContainsKey(item.PartyId)).Select(item =>
        {
            var name = names[item.PartyId];
            return new InventoryLabelTracePartyV1(item.PartyId, item.Role, name.Code, name.LegalName, name.TradeName);
        }).ToArray();
        return (list, list.Length == roles.Length
            ? InventoryLabelTraceStatusesV1.Answered
            : InventoryLabelTraceStatusesV1.Partial);
    }

    private static string? RoleOf(string? step) => step switch
    {
        InventoryLabelTraceStepsV1.SupplierReceipt => InventoryLabelTracePartyRolesV1.Supplier,
        InventoryLabelTraceStepsV1.Shipment => InventoryLabelTracePartyRolesV1.Customer,
        _ => null,
    };

    /// <summary>The owner's exact decimal as invariant text without trailing zeros; formatting only, never a rounding.</summary>
    private static string Plain(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static bool ValidLabel(InventoryLabelTraceLabel? label) =>
        label is not null && label.LabelId != Guid.Empty && ValidText(label.Code)
        && label.Code.Length <= MaterialLabel.MaximumCodeLength;

    private static bool ValidText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    private static bool ValidOptionalText(string? value) => value is null || ValidText(value);

    private static bool TryKilograms(decimal value, out string text)
    {
        text = string.Empty;
        if (value <= 0m)
        {
            return false;
        }

        try
        {
            text = Kilograms.Positive(value).ToString();
            return true;
        }
        catch (InventoryRuleException)
        {
            return false;
        }
    }

    private static bool CanonicalQuantity(string? value) =>
        value is not null
        && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
        && TryKilograms(parsed, out var canonical)
        && string.Equals(canonical, value, StringComparison.Ordinal);

    private readonly record struct SoftAnswer<T>(T? Value, bool Failed)
        where T : class;

    /// <summary>An enrichment owner's read: a failure re-measures the scope and becomes a status, never the trace's 503.</summary>
    private async Task<SoftAnswer<T>> SoftAsync<T>(Func<Task<T>> read, Scope scope, CancellationToken cancellationToken)
        where T : class
    {
        T? result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await RequireAsync(scope, cancellationToken);
            return new SoftAnswer<T>(null, true);
        }

        await RequireAsync(scope, cancellationToken);
        return new SoftAnswer<T>(result, false);
    }

    private async Task<T> FencedAsync<T>(Func<Task<T>> read, Scope scope, CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RequireAsync(scope, cancellationToken);
            if (exception is InventoryLabelTraceException)
            {
                throw;
            }
            throw Error(503, InventoryLabelTraceCodesV1.Unavailable, exception);
        }

        await RequireAsync(scope, cancellationToken);
        return result;
    }

    private async Task<Scope> RequireAsync(Scope? expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
        {
            throw Error(expected is null ? 401 : 403, expected is null
                ? InventoryLabelTraceCodesV1.Unauthenticated
                : InventoryLabelTraceCodesV1.Forbidden);
        }
        if (!company.IsResolved)
        {
            throw Error(403, InventoryLabelTraceCodesV1.Forbidden);
        }

        var scope = new Scope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope)
        {
            throw Error(403, InventoryLabelTraceCodesV1.Forbidden);
        }

        // KLN-D44: the trace is shown to whoever may see the label detail; no separate permission exists.
        foreach (var permission in new[] { ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead })
        {
            RequireIdentity(scope);
            PermissionScope grant;
            try
            {
                grant = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                RequireIdentity(scope);
                throw Error(503, InventoryLabelTraceCodesV1.Unavailable, exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            RequireIdentity(scope);
            if (!grant.IsGranted || !grant.IsCompanyWide)
            {
                throw Error(403, InventoryLabelTraceCodesV1.Forbidden);
            }
        }
        return scope;
    }

    private void RequireIdentity(Scope scope)
    {
        if (!company.IsResolved || company.CompanyId != scope.CompanyId || scope.CompanyId == Guid.Empty
            || !user.IsAuthenticated || user.UserId != scope.ActorId || !user.CanAccessCompany(scope.CompanyId))
        {
            throw Error(403, InventoryLabelTraceCodesV1.Forbidden);
        }
    }

    private static InventoryLabelTraceException Error(int statusCode, string code, Exception? exception = null) =>
        new(statusCode, code, exception);

    private sealed record Scope(Guid CompanyId, string ActorId);
}
