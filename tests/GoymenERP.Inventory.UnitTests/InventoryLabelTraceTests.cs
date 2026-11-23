using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryLabelTraceTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset At = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
    private const string ManufacturingKind = "Manufacturing";
    private const string SplitKind = "Split";

    [Fact]
    public async Task Forward_trace_follows_receipt_through_production_to_shipment_with_owner_answers()
    {
        var world = new World();
        var trace = await world.Service().ReadAsync(world.Received, InventoryLabelTraceDirectionV1.Forward);

        Assert.Equal((world.Received, "LBL-R", InventoryLabelTraceApiV1.Forward), (trace.LabelId, trace.LabelCode, trace.Direction));
        Assert.Equal([(world.Received, 0), (world.Produced, 1), (world.Shipped, 2)],
            trace.Labels.Select(node => (node.LabelId, node.Depth)));
        Assert.Equal(2, trace.Links.Count);
        var production = trace.Links[0];
        Assert.Equal((ManufacturingKind, (Guid?)world.Order, "10", "1", "Exact"),
            (production.Kind, production.ProductionOrderId, production.ConsumedKg, production.ConsumedShare, production.ShareStatus));
        Assert.Null(trace.Links[1].ProductionOrderId);

        Assert.Equal([InventoryLabelTraceStepsV1.SupplierReceipt, InventoryLabelTraceStepsV1.Shipment],
            trace.Events.Select(item => item.Step));
        var receipt = trace.Events[0];
        Assert.Equal(("Receipt", "In", "10", (Guid?)world.Supplier, "SUP-77"),
            (receipt.MovementKind, receipt.Direction, receipt.QuantityKg, receipt.PartyId, receipt.SupplierLabelNumber));
        Assert.Equal(((Guid?)world.Customer, (string?)null), (trace.Events[1].PartyId, trace.Events[1].SupplierLabelNumber));
        Assert.Equal([InventoryLabelTracePartyRolesV1.Customer, InventoryLabelTracePartyRolesV1.Supplier],
            trace.Parties.Select(party => party.Role));
        Assert.Equal(
            (InventoryLabelTraceStatusesV1.Answered, InventoryLabelTraceStatusesV1.Answered, InventoryLabelTraceStatusesV1.Answered),
            (trace.ProductionStatus, trace.PartyStatus, trace.PartyNameStatus));
        Assert.False(trace.WindowFull);
        Assert.Equal(Company, world.Reader.CompanyAsked);
        Assert.Equal([world.Produced], world.Production.Asked);
        Assert.Equal(1, world.Genealogy.Reads);
    }

    [Fact]
    public async Task Backward_trace_follows_output_to_its_sources()
    {
        var world = new World();
        var trace = await world.Service().ReadAsync(world.Shipped, InventoryLabelTraceDirectionV1.Backward);

        Assert.Equal(InventoryLabelTraceApiV1.Backward, trace.Direction);
        Assert.Equal([(world.Shipped, 0), (world.Produced, 1), (world.Received, 2)],
            trace.Labels.Select(node => (node.LabelId, node.Depth)));

        var middle = await world.Service().ReadAsync(world.Produced, InventoryLabelTraceDirectionV1.Backward);
        Assert.Equal([world.Produced, world.Received], middle.Labels.Select(node => node.LabelId));
        Assert.Equal(InventoryLabelTraceStepsV1.SupplierReceipt, Assert.Single(middle.Events).Step);
    }

    [Fact]
    public void Walk_stops_at_the_label_bound_and_carries_the_genealogy_window_flag()
    {
        var root = Guid.NewGuid();
        var edges = Enumerable.Range(0, InventoryLabelTraceApiV1.MaximumLabels + 10)
            .Select(index => Edge(SplitKind, root, "ROOT", Guid.NewGuid(), $"OUT-{index:D4}", At.AddMinutes(index)))
            .ToArray();

        var capped = InventoryLabelTraceService.Walk(new(root, "ROOT", edges, false), InventoryLabelTraceDirectionV1.Forward);
        Assert.Equal(InventoryLabelTraceApiV1.MaximumLabels, capped.Nodes.Count);
        Assert.True(capped.WindowFull);

        var backward = InventoryLabelTraceService.Walk(new(root, "ROOT", edges, true), InventoryLabelTraceDirectionV1.Backward);
        Assert.Single(backward.Nodes);
        Assert.Empty(backward.Links);
        Assert.True(backward.WindowFull);
    }

    [Fact]
    public async Task Both_label_detail_permissions_are_company_wide_and_fresh_after_every_read()
    {
        var denied = new World();
        denied.Authority.Denied = ErpPermissions.InventoryBalancesRead;
        await Refused(denied.Service().ReadAsync(denied.Received, InventoryLabelTraceDirectionV1.Forward), 403,
            InventoryLabelTraceCodesV1.Forbidden);
        Assert.Equal(0, denied.Genealogy.Reads);

        var revoked = new World();
        revoked.Genealogy.During = () => revoked.Authority.Denied = ErpPermissions.InventoryLabelsRead;
        await Refused(revoked.Service().ReadAsync(revoked.Received, InventoryLabelTraceDirectionV1.Forward), 403,
            InventoryLabelTraceCodesV1.Forbidden);
        Assert.Equal(0, revoked.Reader.Reads);

        var scoped = new World();
        scoped.Authority.CompanyWide = false;
        await Refused(scoped.Service().SearchAsync("LBL"), 403, InventoryLabelTraceCodesV1.Forbidden);

        var counted = new World();
        await counted.Service().ReadAsync(counted.Received, InventoryLabelTraceDirectionV1.Forward);
        Assert.Equal([ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead],
            counted.Authority.Calls.Take(2));
        Assert.Equal(0, counted.Authority.Calls.Count % 2);
        Assert.DoesNotContain(counted.Authority.Calls,
            permission => permission is not (ErpPermissions.InventoryLabelsRead or ErpPermissions.InventoryBalancesRead));
    }

    [Fact]
    public async Task Anonymous_or_moved_scope_is_refused()
    {
        var anonymous = new World();
        anonymous.User.IsAuthenticated = false;
        await Refused(anonymous.Service().ReadAsync(anonymous.Received, InventoryLabelTraceDirectionV1.Forward), 401,
            InventoryLabelTraceCodesV1.Unauthenticated);

        var moved = new World();
        moved.Genealogy.During = () => moved.Context.Set(Guid.NewGuid());
        await Refused(moved.Service().ReadAsync(moved.Received, InventoryLabelTraceDirectionV1.Forward), 403,
            InventoryLabelTraceCodesV1.Forbidden);
    }

    [Fact]
    public async Task Empty_label_or_undefined_direction_is_invalid_and_reads_nothing()
    {
        var world = new World();
        await Refused(world.Service().ReadAsync(Guid.Empty, InventoryLabelTraceDirectionV1.Forward), 400,
            InventoryLabelTraceCodesV1.InvalidQuery);
        await Refused(world.Service().ReadAsync(world.Received, (InventoryLabelTraceDirectionV1)9), 400,
            InventoryLabelTraceCodesV1.InvalidQuery);
        Assert.Equal(0, world.Genealogy.Reads);
    }

    [Fact]
    public async Task Unknown_or_another_companys_label_is_not_found()
    {
        var world = new World();
        await Refused(world.Service().ReadAsync(Guid.NewGuid(), InventoryLabelTraceDirectionV1.Forward), 404,
            InventoryLabelTraceCodesV1.NotFound);
        Assert.Equal(0, world.Reader.Reads);
    }

    [Fact]
    public async Task Inventorys_own_chain_and_movements_fail_closed()
    {
        var world = new World();
        await Refused(world.Service(genealogy: false).ReadAsync(world.Received, InventoryLabelTraceDirectionV1.Forward), 503,
            InventoryLabelTraceCodesV1.Unavailable);
        await Refused(world.Service(reader: false).ReadAsync(world.Received, InventoryLabelTraceDirectionV1.Forward), 503,
            InventoryLabelTraceCodesV1.Unavailable);

        var lowerCase = new World();
        lowerCase.Genealogy.Edges[0] = lowerCase.Genealogy.Edges[0] with { Kind = "manufacturing" };
        await Refused(lowerCase.Service().ReadAsync(lowerCase.Received, InventoryLabelTraceDirectionV1.Forward), 503,
            InventoryLabelTraceCodesV1.Unavailable);

        var widened = new World();
        widened.Genealogy.Edges[0] = widened.Genealogy.Edges[0] with { SourceQuantityKg = "10.0" };
        await Refused(widened.Service().ReadAsync(widened.Received, InventoryLabelTraceDirectionV1.Forward), 503,
            InventoryLabelTraceCodesV1.Unavailable);

        var unknownKind = new World();
        unknownKind.Reader.Lines[0] = unknownKind.Reader.Lines[0] with { Kind = 99 };
        await Refused(unknownKind.Service().ReadAsync(unknownKind.Received, InventoryLabelTraceDirectionV1.Forward), 503,
            InventoryLabelTraceCodesV1.Unavailable);

        var failing = new World();
        failing.Reader.Error = new InvalidOperationException("reader failed");
        await Refused(failing.Service().ReadAsync(failing.Received, InventoryLabelTraceDirectionV1.Forward), 503,
            InventoryLabelTraceCodesV1.Unavailable);
    }

    [Fact]
    public async Task Production_owner_answers_only_enrich_and_say_what_is_missing()
    {
        var absent = new World();
        var notAnswered = await absent.Service(production: false).ReadAsync(absent.Received, InventoryLabelTraceDirectionV1.Forward);
        Assert.Equal(InventoryLabelTraceStatusesV1.NotAnswered, notAnswered.ProductionStatus);
        Assert.Null(notAnswered.Links[0].ProductionOrderId);
        Assert.Equal(3, notAnswered.Labels.Count);

        var failing = new World();
        failing.Production.Error = new InvalidOperationException("owner down");
        Assert.Equal(InventoryLabelTraceStatusesV1.Unavailable,
            (await failing.Service().ReadAsync(failing.Received, InventoryLabelTraceDirectionV1.Forward)).ProductionStatus);

        var declined = new World();
        declined.Production.Answer = new(false, []);
        Assert.Equal(InventoryLabelTraceStatusesV1.NotAnswered,
            (await declined.Service().ReadAsync(declined.Received, InventoryLabelTraceDirectionV1.Forward)).ProductionStatus);

        var missing = new World();
        missing.Production.Answer = new(true, []);
        Assert.Equal(InventoryLabelTraceStatusesV1.Partial,
            (await missing.Service().ReadAsync(missing.Received, InventoryLabelTraceDirectionV1.Forward)).ProductionStatus);

        var malformed = new World();
        malformed.Production.Answer = new(true, [malformed.Share with { Share = 1.5m }]);
        Assert.Equal(InventoryLabelTraceStatusesV1.Unavailable,
            (await malformed.Service().ReadAsync(malformed.Received, InventoryLabelTraceDirectionV1.Forward)).ProductionStatus);

        var noProduction = new World();
        var shipped = await noProduction.Service(production: false)
            .ReadAsync(noProduction.Shipped, InventoryLabelTraceDirectionV1.Forward);
        Assert.Equal(InventoryLabelTraceStatusesV1.Answered, shipped.ProductionStatus);
    }

    [Fact]
    public async Task Party_owner_answers_only_enrich_and_say_what_is_missing()
    {
        var absent = new World();
        var notAnswered = await absent.Service(parties: false).ReadAsync(absent.Received, InventoryLabelTraceDirectionV1.Forward);
        Assert.Equal(InventoryLabelTraceStatusesV1.NotAnswered, notAnswered.PartyStatus);
        Assert.All(notAnswered.Events, item => Assert.Null(item.PartyId));
        Assert.Empty(notAnswered.Parties);

        var unknown = new World();
        unknown.Parties.CustomerKnown = false;
        var partial = await unknown.Service().ReadAsync(unknown.Received, InventoryLabelTraceDirectionV1.Forward);
        Assert.Equal(InventoryLabelTraceStatusesV1.Partial, partial.PartyStatus);
        Assert.Equal(InventoryLabelTracePartyRolesV1.Supplier, Assert.Single(partial.Parties).Role);

        var foreign = new World();
        foreign.Parties.Extra = new(InventoryGoodsReceiptContractV1.SourceKind, Guid.NewGuid(), null, foreign.Received,
            InventoryLabelTracePartyRolesV1.Supplier, Guid.NewGuid(), null);
        var refused = await foreign.Service().ReadAsync(foreign.Received, InventoryLabelTraceDirectionV1.Forward);
        Assert.Equal(InventoryLabelTraceStatusesV1.Unavailable, refused.PartyStatus);
        Assert.Equal(2, refused.Events.Count);

        var failing = new World();
        failing.Parties.Error = new InvalidOperationException("owner down");
        Assert.Equal(InventoryLabelTraceStatusesV1.Unavailable,
            (await failing.Service().ReadAsync(failing.Received, InventoryLabelTraceDirectionV1.Forward)).PartyStatus);
    }

    [Fact]
    public async Task Party_names_follow_the_display_owners_own_decision()
    {
        var denied = new World();
        denied.Names.Outcome = InventoryStockOwnerDisplayBatchOutcomeV1.Denied;
        var trace = await denied.Service().ReadAsync(denied.Received, InventoryLabelTraceDirectionV1.Forward);
        Assert.Equal(InventoryLabelTraceStatusesV1.Denied, trace.PartyNameStatus);
        Assert.Empty(trace.Parties);
        Assert.Equal(denied.Supplier, trace.Events[0].PartyId);

        var absent = new World();
        Assert.Equal(InventoryLabelTraceStatusesV1.NotAnswered,
            (await absent.Service(names: false).ReadAsync(absent.Received, InventoryLabelTraceDirectionV1.Forward)).PartyNameStatus);

        var failing = new World();
        failing.Names.Error = new InvalidOperationException("owner down");
        Assert.Equal(InventoryLabelTraceStatusesV1.Unavailable,
            (await failing.Service().ReadAsync(failing.Received, InventoryLabelTraceDirectionV1.Forward)).PartyNameStatus);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("  B  ")]
    [InlineData("LBL\u0001")]
    public async Task Search_text_outside_the_bounds_is_invalid(string text)
    {
        var world = new World();
        await Refused(world.Service().SearchAsync(text), 400, InventoryLabelTraceCodesV1.InvalidQuery);
        Assert.Equal(0, world.Reader.Reads);
    }

    [Fact]
    public async Task Search_text_longer_than_the_bound_is_invalid()
    {
        var world = new World();
        await Refused(world.Service().SearchAsync(new string('L', InventoryLabelTraceApiV1.MaximumSearchLength + 1)), 400,
            InventoryLabelTraceCodesV1.InvalidQuery);
    }

    [Fact]
    public async Task Search_merges_our_code_and_the_suppliers_original_number_for_known_labels_only()
    {
        var world = new World();
        world.Reader.Own = [new(world.Received, "LBL-R"), new(world.Shipped, "LBL-S")];
        world.Search.Items =
        [
            new(world.Received, "LBL-R", "SUP-77"),
            new(world.Shipped, "LBL-S", "SUP-90"),
            new(Guid.NewGuid(), "LBL-UNKNOWN", "SUP-91"),
        ];

        var byNumber = await world.Service().SearchAsync(" SUP ");

        Assert.Equal(
            [(world.Received, "LBL-R", (string?)"SUP-77", InventoryLabelTraceMatchesV1.SupplierLabelNumber),
                (world.Shipped, "LBL-S", (string?)"SUP-90", InventoryLabelTraceMatchesV1.SupplierLabelNumber)],
            byNumber.Items.Select(item => (item.LabelId, item.Code, item.SupplierLabelNumber, item.MatchedBy)));
        Assert.Equal(InventoryLabelTraceStatusesV1.Answered, byNumber.SupplierSearchStatus);
        Assert.False(byNumber.WindowFull);
        Assert.Equal(("SUP", Company), (world.Search.Text, world.Search.CompanyAsked));

        var byCode = await world.Service().SearchAsync("LBL-R");

        var found = Assert.Single(byCode.Items);
        Assert.Equal((world.Received, "LBL-R", (string?)"SUP-77", InventoryLabelTraceMatchesV1.LabelCode),
            (found.LabelId, found.Code, found.SupplierLabelNumber, found.MatchedBy));
    }

    [Fact]
    public async Task Search_keeps_our_own_labels_when_the_supplier_owner_cannot_answer()
    {
        var failing = new World();
        failing.Reader.Own = [new(failing.Received, "LBL-R")];
        failing.Search.Error = new InvalidOperationException("owner down");
        var result = await failing.Service().SearchAsync("LBL");
        Assert.Equal(failing.Received, Assert.Single(result.Items).LabelId);
        Assert.Equal(InventoryLabelTraceStatusesV1.Unavailable, result.SupplierSearchStatus);

        var absent = new World();
        absent.Reader.Own = [new(absent.Received, "LBL-R")];
        Assert.Equal(InventoryLabelTraceStatusesV1.NotAnswered,
            (await absent.Service(search: false).SearchAsync("LBL")).SupplierSearchStatus);
    }

    [Fact]
    public async Task Search_is_bounded_and_says_when_more_exist()
    {
        var world = new World();
        world.Reader.Own = [.. Enumerable.Range(0, InventoryLabelTraceApiV1.MaximumSearchResults + 1)
            .Select(index => new InventoryLabelTraceLabel(Guid.NewGuid(), $"LBL-{index:D3}"))];

        var result = await world.Service().SearchAsync("LBL");

        Assert.Equal(InventoryLabelTraceApiV1.MaximumSearchResults, result.Items.Count);
        Assert.True(result.WindowFull);
        Assert.Equal("LBL-000", result.Items[0].Code);
    }

    private static async Task Refused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryLabelTraceException>(() => task);
        Assert.Equal((status, code), (error.StatusCode, error.Code));
    }

    private static InventoryLabelGenealogyEdgeV1 Edge(
        string kind,
        Guid source,
        string sourceCode,
        Guid output,
        string outputCode,
        DateTimeOffset occurredAt,
        Guid? transformationId = null,
        string sourceKg = "10",
        string outputKg = "10") =>
        new(transformationId ?? Guid.NewGuid(), kind, occurredAt, source, sourceCode, "1", sourceKg, output, outputCode, "1",
            outputKg);

    private sealed class World
    {
        public Guid Received { get; } = Guid.NewGuid();
        public Guid Produced { get; } = Guid.NewGuid();
        public Guid Shipped { get; } = Guid.NewGuid();
        public Guid Order { get; } = Guid.NewGuid();
        public Guid Supplier { get; } = Guid.NewGuid();
        public Guid Customer { get; } = Guid.NewGuid();
        public Guid Acceptance { get; } = Guid.NewGuid();
        public Guid ShipmentLine { get; } = Guid.NewGuid();
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Authority Authority { get; } = new();
        public GenealogyReader Genealogy { get; }
        public TraceReader Reader { get; }
        public ProductionSource Production { get; }
        public PartySource Parties { get; }
        public NameSource Names { get; }
        public SupplierSearch Search { get; } = new();
        public InventoryLabelTraceProductionShareV1 Share { get; }

        public World()
        {
            var manufacturing = Guid.NewGuid();
            Genealogy = new GenealogyReader(
            [
                Edge(ManufacturingKind, Received, "LBL-R", Produced, "LBL-P", At,
                    manufacturing),
                Edge(SplitKind, Produced, "LBL-P", Shipped, "LBL-S", At.AddHours(1),
                    outputKg: "9.5", sourceKg: "9.5"),
            ]);
            Reader = new TraceReader(
            [
                new(Guid.NewGuid(), 1, Received, 1, At.AddMinutes(-30), InventoryGoodsReceiptContractV1.SourceKind, Acceptance,
                    null, 1, 10m),
                new(Guid.NewGuid(), 1, Shipped, 2, At.AddHours(2), InventoryLabelTraceStepsV1.DefiniteShipmentSourceKind,
                    Guid.NewGuid(), ShipmentLine, 2, 9.5m),
            ],
            [new(Received, "LBL-R"), new(Produced, "LBL-P"), new(Shipped, "LBL-S")]);
            Share = new InventoryLabelTraceProductionShareV1(Produced, Received, Order, manufacturing, 10m, 1m, "Exact");
            Production = new ProductionSource(new(true, [Share]));
            Parties = new PartySource(this);
            Names = new NameSource();
        }

        public InventoryLabelTraceService Service(
            bool genealogy = true,
            bool reader = true,
            bool production = true,
            bool parties = true,
            bool names = true,
            bool search = true) =>
            new(Context, User, Authority,
                genealogy ? Genealogy : null,
                reader ? Reader : null,
                production ? Production : null,
                parties ? Parties : null,
                names ? Names : null,
                search ? Search : new InventoryNotAnsweredLabelTraceSupplierSearchSourceV1());
    }

    private sealed class Context : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = Company;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = "trace-reader";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public string? Denied { get; set; }
        public bool CompanyWide { get; set; } = true;
        public List<string> Calls { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(permission);
            return Task.FromResult(new PermissionScope(
                permission != Denied,
                CompanyWide,
                new HashSet<string>(),
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
        }
    }

    private sealed class GenealogyReader(InventoryLabelGenealogyEdgeV1[] edges) : IInventoryLabelGenealogyReaderV1
    {
        public InventoryLabelGenealogyEdgeV1[] Edges { get; } = edges;
        public Action? During { get; set; }
        public int Reads { get; private set; }

        public async Task<InventoryLabelGenealogyV1?> ReadAsync(
            Guid labelId,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            await Task.Yield();
            During?.Invoke();
            var code = Edges.Where(edge => edge.SourceLabelId == labelId).Select(edge => edge.SourceCode)
                .Concat(Edges.Where(edge => edge.OutputLabelId == labelId).Select(edge => edge.OutputCode))
                .FirstOrDefault();
            return code is null ? null : new InventoryLabelGenealogyV1(labelId, code, Edges, false);
        }
    }

    private sealed class TraceReader(InventoryLabelTraceMovementLine[] lines, InventoryLabelTraceLabel[] labels)
        : IInventoryLabelTraceReader
    {
        public InventoryLabelTraceMovementLine[] Lines { get; } = lines;
        public IReadOnlyList<InventoryLabelTraceLabel> Own { get; set; } = [];
        public Exception? Error { get; set; }
        public int Reads { get; private set; }
        public Guid CompanyAsked { get; private set; }

        public async Task<IReadOnlyList<InventoryLabelTraceMovementLine>> ReadMovementLinesAsync(
            Guid companyId,
            IReadOnlyCollection<Guid> labelIds,
            IReadOnlyCollection<string> sourceKinds,
            int take,
            CancellationToken cancellationToken)
        {
            Reads++;
            CompanyAsked = companyId;
            await Task.Yield();
            if (Error is not null) throw Error;
            return [.. Lines.Where(line => labelIds.Contains(line.LabelId) && sourceKinds.Contains(line.SourceKind)).Take(take)];
        }

        public Task<IReadOnlyList<InventoryLabelTraceLabel>> SearchLabelsAsync(
            Guid companyId,
            string prefix,
            int take,
            CancellationToken cancellationToken)
        {
            Reads++;
            CompanyAsked = companyId;
            return Task.FromResult<IReadOnlyList<InventoryLabelTraceLabel>>(
                [.. Own.Where(label => label.Code.StartsWith(prefix, StringComparison.Ordinal)).Take(take)]);
        }

        public Task<IReadOnlyList<InventoryLabelTraceLabel>> FindLabelsAsync(
            Guid companyId,
            IReadOnlyCollection<Guid> labelIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InventoryLabelTraceLabel>>(
                [.. labels.Where(label => labelIds.Contains(label.LabelId))]);
    }

    private sealed class ProductionSource(InventoryLabelTraceProductionAnswerV1 answer) : IInventoryLabelTraceProductionSourceV1
    {
        public InventoryLabelTraceProductionAnswerV1 Answer { get; set; } = answer;
        public Exception? Error { get; set; }
        public IReadOnlyList<Guid> Asked { get; private set; } = [];

        public Task<InventoryLabelTraceProductionAnswerV1> ReadSharesAsync(
            Guid companyId,
            IReadOnlyList<Guid> outputLabelIds,
            CancellationToken cancellationToken = default)
        {
            Asked = outputLabelIds;
            if (Error is not null) throw Error;
            return Task.FromResult(Answer);
        }
    }

    private sealed class PartySource(World world) : IInventoryLabelTracePartySourceV1
    {
        public bool CustomerKnown { get; set; } = true;
        public InventoryLabelTracePartyMatchV1? Extra { get; set; }
        public Exception? Error { get; set; }

        public Task<InventoryLabelTracePartyAnswerV1> ResolveAsync(
            InventoryLabelTracePartyRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            if (Error is not null) throw Error;
            var items = request.Sources.Select(source => source.SourceKind == InventoryGoodsReceiptContractV1.SourceKind
                    ? new InventoryLabelTracePartyMatchV1(source.SourceKind, source.SourceId, source.SourceLineId, source.LabelId,
                        InventoryLabelTracePartyRolesV1.Supplier, world.Supplier, "SUP-77")
                    : new InventoryLabelTracePartyMatchV1(source.SourceKind, source.SourceId, source.SourceLineId, source.LabelId,
                        InventoryLabelTracePartyRolesV1.Customer, CustomerKnown ? world.Customer : null, null))
                .ToList();
            if (Extra is not null) items.Add(Extra);
            return Task.FromResult(new InventoryLabelTracePartyAnswerV1(true, items));
        }
    }

    private sealed class NameSource : IInventoryStockOwnerDisplaySourceV1
    {
        public InventoryStockOwnerDisplayBatchOutcomeV1 Outcome { get; set; } = InventoryStockOwnerDisplayBatchOutcomeV1.Found;
        public Exception? Error { get; set; }

        public Task<InventoryStockOwnerDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
            InventoryStockOwnerDisplayBatchRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            if (Error is not null) throw Error;
            return Task.FromResult(new InventoryStockOwnerDisplayBatchAnswerV1(Outcome, Outcome == InventoryStockOwnerDisplayBatchOutcomeV1.Found
                ? [.. request.PartyIds.Select(id => new InventoryStockOwnerDisplayV1(id, $"C-{id:N}"[..8], "Unvan", null))]
                : []));
        }
    }

    private sealed class SupplierSearch : IInventoryLabelTraceSupplierSearchSourceV1
    {
        public IReadOnlyList<InventoryLabelTraceSupplierMatchV1> Items { get; set; } = [];
        public Exception? Error { get; set; }
        public string? Text { get; private set; }
        public Guid CompanyAsked { get; private set; }

        public Task<InventoryLabelTraceSupplierSearchAnswerV1> SearchAsync(
            Guid companyId,
            string text,
            int take,
            CancellationToken cancellationToken = default)
        {
            Text = text;
            CompanyAsked = companyId;
            if (Error is not null) throw Error;
            return Task.FromResult(new InventoryLabelTraceSupplierSearchAnswerV1(true,
                [.. Items.Where(item => item.LabelCode.StartsWith(text, StringComparison.OrdinalIgnoreCase)
                    || item.SupplierLabelNumber?.StartsWith(text, StringComparison.OrdinalIgnoreCase) == true).Take(take)]));
        }
    }
}
