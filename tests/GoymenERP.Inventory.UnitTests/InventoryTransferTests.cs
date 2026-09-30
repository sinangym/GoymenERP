using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryTransferTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Owner = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Source = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid Destination = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid TargetCustodian = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid Product = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid Lot = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-30T12:00:00Z");

    [Fact]
    public void Dispatch_uses_every_stored_bucket_and_records_planned_vs_actual_without_changing_ownership()
    {
        var first = Planned("70000000-0000-0000-0000-000000000001", "80000000-0000-0000-0000-000000000001", 12m);
        var second = Planned("70000000-0000-0000-0000-000000000002", "80000000-0000-0000-0000-000000000002", 4m);
        var transfer = Transfer(first, second);

        var result = transfer.Dispatch(1, At.AddHours(1), Document("outgoing"),
        [
            Actual(first, Balance(first.LabelId, 7m), Balance(first.LabelId, 3m, BlockState.Blocked)),
            Actual(second, Balance(second.LabelId, 5m)),
        ]);

        Assert.Equal((2L, InventoryTransferStatus.InTransit), (transfer.Version, transfer.Status));
        Assert.Equal((-2m, 1m), (result.Lines[0].DifferenceKg, result.Lines[1].DifferenceKg));
        Assert.Equal(10m, result.Lines[0].ActualQuantity.Value);
        Assert.Equal(new[] { BlockState.Free, BlockState.Blocked }, result.Lines[0].Buckets.Select(bucket => bucket.Position.Block));
        Assert.All(result.Lines.SelectMany(line => line.Buckets), bucket =>
        {
            Assert.Equal(new StockOwner(StockPartyKind.Company, Owner), bucket.Position.Owner);
            Assert.Equal(new StockCustodian(StockPartyKind.Company, Company), bucket.Position.Custodian);
            Assert.Equal(Source, bucket.Position.LocationId);
        });
    }

    [Fact]
    public void Accepting_a_subset_of_whole_label_lines_is_partial_then_the_last_line_closes_the_transfer()
    {
        var first = Planned("70000000-0000-0000-0000-000000000001", "80000000-0000-0000-0000-000000000001", 10m);
        var second = Planned("70000000-0000-0000-0000-000000000002", "80000000-0000-0000-0000-000000000002", 5m);
        var transfer = Transfer(first, second);
        transfer.Dispatch(1, At.AddHours(1), Document("outgoing"),
        [
            Actual(first, Balance(first.LabelId, 7m), Balance(first.LabelId, 3m, BlockState.Blocked)),
            Actual(second, Balance(second.LabelId, 5m)),
        ]);

        var partial = transfer.AcceptLine(2, first.LineId, At.AddHours(2), Document("incoming-1"));

        Assert.Equal((3L, InventoryTransferStatus.PartiallyAccepted), (transfer.Version, transfer.Status));
        Assert.Equal(first.LineId, partial.Line.LineId);
        Assert.Equal(new[] { 7m, 3m }, partial.Line.Buckets.Select(bucket => bucket.Quantity.Value));
        Assert.All(partial.Line.Buckets, bucket =>
        {
            Assert.Equal(new StockOwner(StockPartyKind.Company, Owner), bucket.Position.Owner);
            Assert.Equal(new StockCustodian(StockPartyKind.Counterparty, TargetCustodian), bucket.Position.Custodian);
            Assert.Equal(Destination, bucket.Position.LocationId);
        });
        Assert.Equal(InventoryTransferLineStatus.Accepted,
            transfer.Lines.Single(line => line.LineId == first.LineId).Status);
        Assert.Equal(InventoryTransferLineStatus.InTransit,
            transfer.Lines.Single(line => line.LineId == second.LineId).Status);

        transfer.AcceptLine(3, second.LineId, At.AddHours(3), Document("incoming-2"));

        Assert.Equal((4L, InventoryTransferStatus.Accepted), (transfer.Version, transfer.Status));
        Assert.All(transfer.Lines, line => Assert.Equal(InventoryTransferLineStatus.Accepted, line.Status));
    }

    [Fact]
    public void Dispatch_and_acceptance_movements_move_the_same_buckets_without_negative_stock_or_mass_change()
    {
        var line = Planned("70000000-0000-0000-0000-000000000001", "80000000-0000-0000-0000-000000000001", 10m);
        var sourceFree = Balance(line.LabelId, 7m);
        var sourceBlocked = Balance(line.LabelId, 3m, BlockState.Blocked);
        var ledger = StockLedger.FromStoredBalances([sourceFree, sourceBlocked]);
        var transfer = Transfer(line);

        var dispatch = transfer.Dispatch(1, At.AddHours(1), Document("outgoing"),
            [Actual(line, sourceFree, sourceBlocked)]);
        var dispatchMovement = dispatch.Movement(Guid.NewGuid(), Company);
        ledger.Apply(dispatchMovement);

        Assert.Equal(StockMovementKind.TransferDispatch, dispatchMovement.Kind);
        Assert.All(dispatchMovement.Lines, movementLine => Assert.Equal(StockDirection.Out, movementLine.Direction));
        Assert.Empty(ledger.Balances);

        var acceptance = transfer.AcceptLine(2, line.LineId, At.AddHours(2), Document("incoming"));
        var acceptanceMovement = acceptance.Movement(Guid.NewGuid(), Company);
        ledger.Apply(acceptanceMovement);

        Assert.Equal(StockMovementKind.TransferAccept, acceptanceMovement.Kind);
        Assert.All(acceptanceMovement.Lines, movementLine => Assert.Equal(StockDirection.In, movementLine.Direction));
        Assert.Equal(10m, ledger.Balances.Sum(balance => balance.Quantity.Value));
        Assert.All(ledger.Balances, balance =>
        {
            Assert.Equal(Destination, balance.Position.LocationId);
            Assert.Equal(new StockOwner(StockPartyKind.Company, Owner), balance.Position.Owner);
        });
    }

    [Theory]
    [InlineData("missing-line")]
    [InlineData("duplicate-line")]
    [InlineData("stale-label")]
    [InlineData("wrong-company")]
    [InlineData("wrong-location")]
    [InlineData("wrong-custodian")]
    [InlineData("duplicate-block")]
    public void A_refused_dispatch_is_atomic_and_keeps_the_plan_unchanged(string fault)
    {
        var first = Planned("70000000-0000-0000-0000-000000000001", "80000000-0000-0000-0000-000000000001", 10m);
        var second = Planned("70000000-0000-0000-0000-000000000002", "80000000-0000-0000-0000-000000000002", 5m);
        var transfer = Transfer(first, second);
        var firstBalance = fault switch
        {
            "wrong-company" => Balance(first.LabelId, 10m, company: Guid.NewGuid()),
            "wrong-location" => Balance(first.LabelId, 10m, location: Guid.NewGuid()),
            "wrong-custodian" => Balance(first.LabelId, 10m, custodian: Guid.NewGuid()),
            _ => Balance(first.LabelId, 10m),
        };
        var firstActual = Actual(first, firstBalance);
        var secondActual = Actual(second, Balance(second.LabelId, 5m));
        IReadOnlyList<InventoryTransferDispatchLine> actual = fault switch
        {
            "missing-line" => [firstActual],
            "duplicate-line" => [firstActual, firstActual],
            "stale-label" => [firstActual with { LabelVersion = 6 }, secondActual],
            "duplicate-block" => [firstActual with { Buckets = [firstBalance, firstBalance] }, secondActual],
            _ => [firstActual, secondActual],
        };

        AssertInvalid(() => transfer.Dispatch(1, At.AddHours(1), Document("outgoing"), actual));

        Assert.Equal((1L, InventoryTransferStatus.Planned), (transfer.Version, transfer.Status));
        Assert.All(transfer.Lines, line =>
        {
            Assert.Equal(InventoryTransferLineStatus.Planned, line.Status);
            Assert.Null(line.ActualQuantity);
        });
    }

    [Fact]
    public void Acceptance_requires_the_current_transfer_version_and_an_in_transit_unaccepted_line()
    {
        var line = Planned("70000000-0000-0000-0000-000000000001", "80000000-0000-0000-0000-000000000001", 10m);
        var transfer = Transfer(line);

        AssertInvalid(() => transfer.AcceptLine(1, line.LineId, At, Document("incoming")));
        transfer.Dispatch(1, At.AddHours(1), Document("outgoing"), [Actual(line, Balance(line.LabelId, 10m))]);
        AssertInvalid(() => transfer.AcceptLine(1, line.LineId, At.AddHours(2), Document("incoming")));
        AssertInvalid(() => transfer.AcceptLine(2, line.LineId, At, Document("incoming")));
        transfer.AcceptLine(2, line.LineId, At.AddHours(2), Document("incoming"));
        AssertInvalid(() => transfer.AcceptLine(3, line.LineId, At.AddHours(3), Document("incoming")));
    }

    [Fact]
    public void A_plan_requires_a_real_physical_destination_and_unique_whole_labels()
    {
        var line = Planned("70000000-0000-0000-0000-000000000001", "80000000-0000-0000-0000-000000000001", 10m);
        AssertInvalid(() => new InventoryTransfer(Guid.NewGuid(), Company, Source,
            new StockCustodian(StockPartyKind.Company, Company), Source,
            new StockCustodian(StockPartyKind.Company, Company), At, [line]));
        AssertInvalid(() => Transfer(line, line with { LineId = Guid.NewGuid() }));
        AssertInvalid(() => Transfer(line, line with { LabelId = Guid.NewGuid() }));
    }

    [Fact]
    public void Public_requests_reject_unknown_fields_and_acceptance_exposes_no_partial_label_quantity()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = $$"""
            {"expectedTransferVersion":"2","acceptedAtUtc":"2026-09-30T14:00:00Z","incomingDocument":{"kind":"purchase.delivery","id":"{{Guid.NewGuid()}}","lineId":null},"quantityKg":"1"}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AcceptInventoryTransferLineV1>(json, web));
        Assert.DoesNotContain(typeof(AcceptInventoryTransferLineV1).GetProperties(),
            property => property.Name.Contains("Quantity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Plan_revalidates_transfer_authority_after_the_destination_owner_read_and_derives_metadata()
    {
        var world = new CommandWorld();
        world.Authority.Roles = ["transfer.b", "transfer.a"];

        await world.Service().PlanAsync(PlanRequest(), Transport(""));

        Assert.Equal([InventoryPermissionManifestV1.StockTransferWrite, InventoryPermissionManifestV1.StockTransferWrite,
                InventoryPermissionManifestV1.StockTransferWrite],
            world.Authority.Measured);
        Assert.Equal(1, world.Destination.Calls);
        var call = Assert.Single(world.Writer.Calls);
        Assert.Equal(("plan", "actor", InventoryPermissionManifestV1.StockTransferWrite),
            (call.Operation, call.Metadata.ActorId, call.Metadata.PermissionCode));
        Assert.Equal(["transfer.a", "transfer.b"], call.Metadata.RoleCodes);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("company")]
    [InlineData("actor")]
    public async Task Authority_or_context_change_during_the_destination_read_never_reaches_the_writer(string fault)
    {
        var world = new CommandWorld();
        world.Destination.During = fault switch
        {
            "revoked" => () => world.Authority.Granted = false,
            "company" => () => world.Context.CompanyId = Guid.NewGuid(),
            _ => () => world.User.UserId = "other",
        };

        await Refused(world.Service().PlanAsync(PlanRequest(), Transport("actor")), 403,
            InventoryTransferCodesV1.Forbidden);

        Assert.Empty(world.Writer.Calls);
    }

    [Fact]
    public async Task Acceptance_reloads_the_stored_target_and_revalidates_authority_after_its_owner_read()
    {
        var world = new CommandWorld();
        world.Reader.Result = TransferView(Company);
        world.Destination.During = () => world.Authority.Granted = false;

        await Refused(world.Service().AcceptLineAsync(world.Reader.Result.TransferId, Guid.NewGuid(),
            AcceptRequest(), Transport("actor")), 403, InventoryTransferCodesV1.Forbidden);

        Assert.Equal(1, world.Destination.Calls);
        Assert.Empty(world.Writer.Calls);
    }

    [Fact]
    public async Task Acceptance_revalidates_after_the_transfer_read_before_disclosing_not_found()
    {
        var world = new CommandWorld();
        world.Reader.During = () => world.Authority.Granted = false;

        await Refused(world.Service().AcceptLineAsync(Guid.NewGuid(), Guid.NewGuid(),
            AcceptRequest(), Transport("actor")), 403, InventoryTransferCodesV1.Forbidden);

        Assert.Equal(1, world.Reader.Calls);
        Assert.Equal(2, world.Authority.Measured.Count);
        Assert.Empty(world.Writer.Calls);
    }

    [Fact]
    public async Task Acceptance_uses_the_stored_target_then_writes_after_the_final_authority_fence()
    {
        var world = new CommandWorld();
        world.Reader.Result = TransferView(Company);

        await world.Service().AcceptLineAsync(world.Reader.Result.TransferId, Guid.NewGuid(),
            AcceptRequest(), Transport("actor"));

        Assert.Equal(1, world.Reader.Calls);
        Assert.Equal(1, world.Destination.Calls);
        Assert.Equal(4, world.Authority.Measured.Count);
        Assert.Equal("accept", Assert.Single(world.Writer.Calls).Operation);
    }

    [Fact]
    public async Task Revocation_at_the_writer_fence_refuses_without_recording_the_command()
    {
        var world = new CommandWorld();
        world.Writer.BeforeFence = () => world.Authority.Granted = false;

        await Refused(world.Service().DispatchAsync(Guid.NewGuid(), DispatchRequest(), Transport("actor")),
            403, InventoryTransferCodesV1.Forbidden);

        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData(InventoryTransferDestinationStatusV1.NotFound, InventoryTransferCodesV1.DestinationNotFound)]
    [InlineData(InventoryTransferDestinationStatusV1.Inactive, InventoryTransferCodesV1.DestinationInactive)]
    [InlineData(InventoryTransferDestinationStatusV1.ContextMismatch, InventoryTransferCodesV1.DestinationContextMismatch)]
    public async Task Refused_destination_context_is_distinct_and_never_written(
        InventoryTransferDestinationStatusV1 status,
        string code)
    {
        var world = new CommandWorld();
        world.Destination.Status = status;

        await Refused(world.Service().PlanAsync(PlanRequest(), Transport("actor")), 422, code);

        Assert.Empty(world.Writer.Calls);
    }

    [Fact]
    public async Task Unregistered_transfer_permission_is_a_setup_gap_and_not_a_grant_fallback()
    {
        var world = new CommandWorld { Known = false };

        await Refused(world.Service().DispatchAsync(Guid.NewGuid(), DispatchRequest(), Transport("actor")),
            503, InventoryTransferCodesV1.PermissionUnregistered);

        Assert.Empty(world.Authority.Measured);
        Assert.Empty(world.Writer.Calls);
    }

    [Fact]
    public async Task Transfer_read_is_fenced_before_and_after_and_refuses_a_foreign_owner_answer()
    {
        var world = new CommandWorld();
        world.Reader.Result = TransferView(companyId: Company);

        Assert.Same(world.Reader.Result, await world.Query().GetAsync(world.Reader.Result.TransferId));
        Assert.Equal([ErpPermissions.InventoryBalancesRead, ErpPermissions.InventoryBalancesRead], world.Authority.Measured);

        world = new CommandWorld();
        world.Reader.Result = TransferView(companyId: Guid.NewGuid());
        await Refused(world.Query().GetAsync(world.Reader.Result.TransferId), 503, InventoryTransferCodesV1.Unavailable);
    }

    [Fact]
    public void Owner_registration_adds_no_destination_adapter_or_grant()
    {
        var services = new ServiceCollection().AddInventoryTransfers();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferWriterV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferReaderV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferCommandV1)
            && descriptor.ImplementationType == typeof(InventoryTransferCommandService));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferDestinationPortV1));
        Assert.Equal("inventory.stock-transfer.write", InventoryPermissionManifestV1.StockTransferWrite);
    }

    [Fact]
    public void Relational_model_holds_company_scoped_transfer_lines_events_buckets_and_movement_links()
    {
        using var context = new InventoryDbContext(
            new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options,
            new TestContext());
        var model = context.GetService<IDesignTimeModel>().Model;
        var transfer = model.FindEntityType(typeof(InventoryTransferRow))!;
        Assert.Equal(["CompanyId", "Id"], transfer.GetKeys().Single(key => !key.IsPrimaryKey())
            .Properties.Select(property => property.Name));
        Assert.True(transfer.FindProperty(nameof(InventoryTransferRow.RowVersion))!.IsConcurrencyToken);

        var line = model.FindEntityType(typeof(InventoryTransferLineRow))!;
        Assert.Contains(line.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(InventoryTransferRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "TransferId"]));
        Assert.Contains(line.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(LabelRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LabelId"]));

        var dispatch = model.FindEntityType(typeof(InventoryTransferDispatchRow))!;
        var acceptance = model.FindEntityType(typeof(InventoryTransferAcceptanceRow))!;
        Assert.Contains(dispatch.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(MovementRow));
        Assert.Contains(acceptance.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(MovementRow));
        Assert.Equal(["CompanyId", "TransferId", "LineId"], acceptance.FindPrimaryKey()!.Properties
            .Select(property => property.Name));

        var movement = model.FindEntityType(typeof(MovementRow))!;
        Assert.Equal("[Kind] BETWEEN 1 AND 9",
            movement.GetCheckConstraints().Single(check => check.Name == "CK_Movements_Kind").Sql);
        Assert.Equal((8, 9), ((int)StockMovementKind.TransferDispatch, (int)StockMovementKind.TransferAccept));
    }

    private static InventoryTransfer Transfer(params InventoryTransferPlanLine[] lines) =>
        new(Guid.Parse("90000000-0000-0000-0000-000000000001"), Company, Source,
            new StockCustodian(StockPartyKind.Company, Company), Destination,
            new StockCustodian(StockPartyKind.Counterparty, TargetCustodian), At, lines);

    private static InventoryTransferPlanLine Planned(string lineId, string labelId, decimal quantity) =>
        new(Guid.Parse(lineId), Guid.Parse(labelId), 7, Kilograms.Positive(quantity));

    private static InventoryTransferDispatchLine Actual(
        InventoryTransferPlanLine line,
        params StockBalance[] balances) => new(line.LineId, line.ExpectedLabelVersion, balances);

    private static StockBalance Balance(
        Guid labelId,
        decimal quantity,
        BlockState block = BlockState.Free,
        Guid? company = null,
        Guid? location = null,
        Guid? custodian = null)
    {
        var companyId = company ?? Company;
        var label = new MaterialLabel(labelId, companyId, $"LBL-{labelId:N}", Product, Lot);
        var position = new StockPosition(companyId, new StockOwner(StockPartyKind.Company, Owner),
            new StockCustodian(StockPartyKind.Company, custodian ?? Company), location ?? Source, label, block);
        return new(position, Kilograms.Positive(quantity));
    }

    private static InventoryTransferDocumentReference Document(string suffix) =>
        new($"inventory.transfer.{suffix}", Guid.NewGuid());

    private static PlanInventoryTransferV1 PlanRequest() => new(Guid.NewGuid(), Source, "Company", Company,
        Destination, "Counterparty", TargetCustodian, At,
        [new(Guid.NewGuid(), Guid.NewGuid(), "7", "10")]);

    private static DispatchInventoryTransferV1 DispatchRequest() =>
        new("1", At.AddHours(1), new("inventory.transfer.outgoing", Guid.NewGuid(), null));

    private static AcceptInventoryTransferLineV1 AcceptRequest() =>
        new("2", At.AddHours(2), new("inventory.transfer.incoming", Guid.NewGuid(), null));

    private static InventoryCommandMetadata Transport(string actor) =>
        new(actor, "ignored", ["ignored"], "corr-1", null, "key-1", "Web");

    private static InventoryTransferViewV1 TransferView(Guid companyId) => new(Guid.NewGuid(), companyId, "1",
        "Planned", Source, "Company", Company, Destination, "Counterparty", TargetCustodian, At, null, []);

    private static async Task Refused(Task task, int status, string code)
    {
        var exception = await Assert.ThrowsAsync<InventoryTransferException>(() => task);
        Assert.Equal((status, code), (exception.Status, exception.Code));
    }

    private static void AssertInvalid(Action action)
    {
        var exception = Assert.Throws<InventoryRuleException>(action);
        Assert.Equal(InventoryRuleCodes.InvalidTransfer, exception.Code);
    }

    private sealed class CommandWorld
    {
        public TestContext Context { get; } = new();
        public TestUser User { get; } = new();
        public TestAuthority Authority { get; } = new();
        public TestDestination Destination { get; } = new();
        public TestWriter Writer { get; } = new();
        public TestReader Reader { get; } = new();
        public bool Known { get; init; } = true;

        public InventoryTransferCommandService Service() =>
            new(Context, User, Authority, Destination, Reader, Writer, _ => Known);

        public InventoryTransferQueryService Query() => new(Context, User, Authority, Reader);
    }

    private sealed class TestContext : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; set; } = Company;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class TestUser : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = "actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class TestAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public string[] Roles { get; set; } = [];
        public List<string> Measured { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Measured.Add(permission);
            return Task.FromResult(new PermissionScope(Granted, true, Roles.ToHashSet(StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class TestDestination : IInventoryTransferDestinationPortV1
    {
        public InventoryTransferDestinationStatusV1 Status { get; set; } = InventoryTransferDestinationStatusV1.Active;
        public int Calls { get; private set; }
        public Action? During { get; set; }

        public Task<InventoryTransferDestinationStatusV1> CheckAsync(
            Guid companyId,
            Guid locationId,
            string custodianKind,
            Guid custodianId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            During?.Invoke();
            return Task.FromResult(Status);
        }
    }

    private sealed class TestWriter : IInventoryTransferWriterV1
    {
        public List<(string Operation, InventoryCommandMetadata Metadata)> Calls { get; } = [];
        public Action? BeforeFence { get; set; }
        private readonly InventoryTransferResultV1 result = new(TransferView(Company), false, "corr-1");

        public Task<InventoryTransferResultV1> PlanAsync(PlanInventoryTransferV1 request,
            InventoryCommandMetadata metadata, Func<CancellationToken, Task> freshAuthorityFence,
            CancellationToken cancellationToken = default) =>
            Answer("plan", metadata, freshAuthorityFence, cancellationToken);

        public Task<InventoryTransferResultV1> DispatchAsync(Guid transferId, DispatchInventoryTransferV1 request,
            InventoryCommandMetadata metadata, Func<CancellationToken, Task> freshAuthorityFence,
            CancellationToken cancellationToken = default) =>
            Answer("dispatch", metadata, freshAuthorityFence, cancellationToken);

        public Task<InventoryTransferResultV1> AcceptLineAsync(Guid transferId, Guid lineId,
            AcceptInventoryTransferLineV1 request, InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence, CancellationToken cancellationToken = default) =>
            Answer("accept", metadata, freshAuthorityFence, cancellationToken);

        private async Task<InventoryTransferResultV1> Answer(string operation, InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence, CancellationToken cancellationToken)
        {
            BeforeFence?.Invoke();
            await freshAuthorityFence(cancellationToken);
            Calls.Add((operation, metadata));
            return result;
        }
    }

    private sealed class TestReader : IInventoryTransferReaderV1
    {
        public InventoryTransferViewV1? Result { get; set; }
        public int Calls { get; private set; }
        public Action? During { get; set; }

        public Task<InventoryTransferViewV1?> ReadAsync(Guid transferId, CancellationToken cancellationToken = default)
        {
            Calls++;
            During?.Invoke();
            return Task.FromResult(Result);
        }
    }
}
