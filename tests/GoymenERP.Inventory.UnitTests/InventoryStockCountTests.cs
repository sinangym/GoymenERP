using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Counts;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// ST-05 / AT-037 (DEC-STK-007) without a database: the count's difference and its one adjustment movement, the
/// ledger effect, the fenced record/decide command, the fenced read and the persistent model. The stored flow
/// (replay, stale, maker≠checker, allocation, company fence) is proven by the relational acceptance tests.
/// </summary>
public sealed class InventoryStockCountTests
{
    private static readonly Guid CountId = Guid.Parse("60000000-0000-0000-0000-000000000001");

    [Theory]
    [InlineData("checker", null)]
    [InlineData(" checker ", null)]
    [InlineData("maker", InventoryStockCountCodesV1.SelfApproval)]
    [InlineData("third", InventoryStockCountCodesV1.AlreadyDecided)]
    public void A_decision_is_replayed_only_to_its_decider(string actor, string? refusal)
    {
        // The stored decision was made by "checker" on what "maker" recorded; the same content from anyone else
        // is not their replay (#499/#512 review P3).
        Assert.Equal(refusal, InventoryStore.DecisionReplayRefusal("checker", "maker", actor,
            InventoryStockCountCodesV1.SelfApproval, InventoryStockCountCodesV1.AlreadyDecided));
    }

    [Theory]
    [InlineData(100, 97.5, "-2.5", StockDirection.Out)]
    [InlineData(100, 100.25, "0.25", StockDirection.In)]
    [InlineData(0.001, 0, "-0.001", StockDirection.Out)]
    public void A_difference_is_counted_minus_book_and_posts_one_line_of_exactly_that_size(
        decimal book, decimal counted, string difference, StockDirection direction)
    {
        var count = Count(book, counted);

        Assert.True(count.HasDifference);
        Assert.Equal(difference, InventoryStore.SignedKilograms(count.Difference));
        var movementId = Guid.NewGuid();
        var movement = count.Adjustment(movementId, At.AddHours(1));
        Assert.Equal((movementId, StockMovementKind.CountAdjustment, At.AddHours(1)), (movement.Id, movement.Kind, movement.OccurredAtUtc));
        Assert.Equal((StockCount.SourceKind, CountId, (Guid?)null), (movement.Source.Kind, movement.Source.Id, movement.Source.LineId));
        var line = Assert.Single(movement.Lines);
        Assert.Equal((count.Position, direction, Math.Abs(count.Difference)), (line.Position, line.Direction, line.Quantity.Value));
    }

    [Fact]
    public void A_count_without_a_difference_has_no_adjustment()
    {
        var count = Count(100m, 100m);

        Assert.False(count.HasDifference);
        Assert.Equal("0", InventoryStore.SignedKilograms(count.Difference));
        Assert.Equal(InventoryRuleCodes.InvalidStockCount,
            Assert.Throws<InventoryRuleException>(() => count.Adjustment(Guid.NewGuid(), At)).Code);
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("long-reason")]
    [InlineData("offset")]
    [InlineData("company")]
    public void A_count_needs_a_reason_UTC_time_and_its_own_companys_position(string fault)
    {
        var position = Position();
        var exception = Assert.Throws<InventoryRuleException>(() => new StockCount(CountId,
            fault == "company" ? CompanyB : CompanyA, position, Kilograms.Positive(10m), Kilograms.Of(9m),
            fault == "offset" ? new DateTimeOffset(2026, 9, 27, 11, 0, 0, TimeSpan.FromHours(3)) : At,
            fault switch { "reason" => "   ", "long-reason" => new string('x', 1001), _ => "Sayım farkı" }));

        Assert.Equal(InventoryRuleCodes.InvalidStockCount, exception.Code);
    }

    [Fact]
    public void The_approved_adjustment_moves_the_book_to_the_counted_kilograms_free_or_blocked()
    {
        foreach (var block in new[] { BlockState.Free, BlockState.Blocked })
        {
            var position = Position(block: block);
            var ledger = StockLedger.FromStoredBalances([new StockBalance(position, Kilograms.Of(100m))]);
            var shortage = new StockCount(CountId, CompanyA, position, Kilograms.Positive(100m), Kilograms.Of(96.5m), At, "Eksik");

            ledger.Apply(shortage.Adjustment(Guid.NewGuid(), At));
            Assert.Equal(96.5m, ledger.QuantityAt(position).Value);

            var surplus = new StockCount(Guid.NewGuid(), CompanyA, position, Kilograms.Positive(96.5m), Kilograms.Of(97m), At, "Fazla");
            ledger.Apply(surplus.Adjustment(Guid.NewGuid(), At));
            Assert.Equal(97m, ledger.QuantityAt(position).Value);
        }
    }

    [Fact]
    public void The_same_count_posts_at_most_once_and_a_count_adjustment_is_one_line()
    {
        var position = Position();
        var ledger = StockLedger.FromStoredBalances([new StockBalance(position, Kilograms.Of(100m))]);
        var count = Count(100m, 90m);
        ledger.Apply(count.Adjustment(Guid.NewGuid(), At));

        Assert.Equal(InventoryRuleCodes.DuplicateSource,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(count.Adjustment(Guid.NewGuid(), At))).Code);
        Assert.Equal(InventoryRuleCodes.InvalidMovement, Assert.Throws<InventoryRuleException>(() =>
            Movement(StockMovementKind.CountAdjustment, In(position, 1m), In(position, 2m))).Code);
    }

    [Fact]
    public void A_shortage_larger_than_the_stored_book_is_refused_whole()
    {
        var position = Position();
        var ledger = StockLedger.FromStoredBalances([new StockBalance(position, Kilograms.Of(5m))]);

        Assert.Equal(InventoryRuleCodes.NegativePhysicalStock, Assert.Throws<InventoryRuleException>(() =>
            ledger.Apply(Count(10m, 0m).Adjustment(Guid.NewGuid(), At))).Code);
        Assert.Equal(5m, ledger.QuantityAt(position).Value);
    }

    [Fact]
    public async Task Record_measures_the_record_permission_twice_and_derives_the_metadata()
    {
        var world = new World();
        world.Authority.Roles = ["stok.b", "stok.a"];
        var transport = Transport(world.User.UserId!) with
        {
            PermissionCode = InventoryPermissionManifestV1.StockCountApprove,
            RoleCodes = ["system.admin"],
        };

        var result = await world.Service().RecordAsync(RecordRequest(), transport);

        Assert.Same(world.Writer.Result, result);
        var call = Assert.Single(world.Writer.Calls);
        Assert.Equal(("record", InventoryPermissionManifestV1.StockCountRecord, world.User.UserId!, "Web"),
            (call.Operation, call.Metadata.PermissionCode, call.Metadata.ActorId, call.Metadata.SourceChannel));
        Assert.Equal(new[] { "stok.a", "stok.b" }, call.Metadata.RoleCodes);
        Assert.Equal(new[] { InventoryPermissionManifestV1.StockCountRecord, InventoryPermissionManifestV1.StockCountRecord },
            world.Authority.Measured);
    }

    [Fact]
    public async Task Decide_measures_the_approve_permission_never_the_record_one()
    {
        var world = new World();

        await world.Service().DecideAsync(CountId, DecideRequest(), Transport(world.User.UserId!));

        var call = Assert.Single(world.Writer.Calls);
        Assert.Equal(("decide", CountId, InventoryPermissionManifestV1.StockCountApprove),
            (call.Operation, call.CountId, call.Metadata.PermissionCode));
        Assert.Equal(new[] { InventoryPermissionManifestV1.StockCountApprove, InventoryPermissionManifestV1.StockCountApprove },
            world.Authority.Measured);
    }

    [Fact]
    public async Task Holding_only_the_record_permission_never_decides()
    {
        var world = new World();
        world.Authority.GrantedPermissions = [InventoryPermissionManifestV1.StockCountRecord];

        await world.Service().RecordAsync(RecordRequest(), Transport(world.User.UserId!));
        await Refused(world.Service().DecideAsync(CountId, DecideRequest(), Transport(world.User.UserId!)), 403,
            InventoryStockCountCodesV1.Forbidden);

        Assert.Equal("record", Assert.Single(world.Writer.Calls).Operation);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("site-scoped")]
    [InlineData("other-actor")]
    [InlineData("lost-between")]
    [InlineData("company-changed")]
    public async Task Denied_scoped_foreign_or_lost_authority_never_reaches_the_writer(string fault)
    {
        foreach (var decide in new[] { false, true })
        {
            var world = new World();
            var actor = world.User.UserId!;
            switch (fault)
            {
                case "denied": world.Authority.GrantedPermissions = []; break;
                case "site-scoped": world.Authority.CompanyWide = false; break;
                case "other-actor": actor = "someone-else"; break;
                case "lost-between": world.Authority.AfterFirst = () => world.Authority.GrantedPermissions = []; break;
                case "company-changed": world.Authority.AfterFirst = () => world.Context.Set(CompanyB); break;
            }

            await Refused(decide
                    ? world.Service().DecideAsync(CountId, DecideRequest(), Transport(actor))
                    : world.Service().RecordAsync(RecordRequest(), Transport(actor)),
                403, InventoryStockCountCodesV1.Forbidden);
            Assert.Empty(world.Writer.Calls);
        }
    }

    [Fact]
    public async Task No_identity_is_401_and_an_unregistered_name_is_a_setup_gap()
    {
        var world = new World();
        world.User.IsAuthenticated = false;
        await Refused(world.Service().RecordAsync(RecordRequest(), Transport("x")), 401, InventoryStockCountCodesV1.Unauthenticated);

        world = new World { Known = false };
        await Refused(world.Service().DecideAsync(CountId, DecideRequest(), Transport(world.User.UserId!)), 503,
            InventoryStockCountCodesV1.PermissionUnregistered);
        Assert.Empty(world.Authority.Measured);
        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData(409, InventoryStockCountCodesV1.SelfApproval, 409)]
    [InlineData(409, InventoryStockCountCodesV1.Stale, 409)]
    [InlineData(404, InventoryStockCountCodesV1.CountNotFound, 404)]
    [InlineData(400, InventoryScaleCodes.ScaleExceeded, 400)]
    [InlineData(400, InventoryScaleCodes.ProfileUnusable, 503)]
    [InlineData(400, "settings_provider_unavailable", 503)]
    public async Task Writer_refusals_pass_through_except_provider_states(int status, string code, int answered)
    {
        var world = new World();
        world.Writer.Failure = new InventoryStockCountException(status, code);

        await Refused(world.Service().DecideAsync(CountId, DecideRequest(), Transport(world.User.UserId!)), answered, code);
    }

    [Fact]
    public async Task The_read_is_fenced_by_label_and_balance_reads_before_and_after_the_owner_read()
    {
        var world = new World();
        world.Reader.Result = new InventoryLabelStockCountsV1(Label1, [View(Label1, Company)], false);

        var result = await world.Query().GetAsync(Label1);

        Assert.Same(world.Reader.Result, result);
        Assert.Equal(new[]
        {
            ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead,
            ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead,
        }, world.Authority.Measured);
    }

    [Fact]
    public async Task The_read_answers_404_for_an_unknown_label_403_when_authority_is_lost_and_503_for_foreign_rows()
    {
        var world = new World();
        await RefusedAsync(world.Query().GetAsync(Label1), 404, InventoryStockCountCodesV1.LabelNotFound);

        world = new World();
        world.Reader.Result = new InventoryLabelStockCountsV1(Label1, [], false);
        world.Reader.During = () => world.Authority.GrantedPermissions = [];
        await RefusedAsync(world.Query().GetAsync(Label1), 403, InventoryStockCountCodesV1.Forbidden);

        world = new World();
        world.Reader.Result = new InventoryLabelStockCountsV1(Label1, [View(Label1, CompanyB)], false);
        await RefusedAsync(world.Query().GetAsync(Label1), 503, InventoryStockCountCodesV1.Unavailable);
    }

    [Fact]
    public void Http_contract_is_pinned_and_each_routes_codes_are_disjoint_by_status()
    {
        Assert.Equal("/api/v1/inventory/stock-counts", InventoryStockCountApiV1.Path);
        Assert.Equal("/api/v1/inventory/stock-counts/{countId}/decision", InventoryStockCountApiV1.DecisionPathTemplate);
        Assert.Equal("/api/v1/inventory/labels/{labelId}/stock-counts", InventoryStockCountApiV1.LabelCountsPathTemplate);
        Assert.Equal(("RecordInventoryStockCountV1", "DecideInventoryStockCountV1", "GetInventoryLabelStockCountsV1"),
            (InventoryStockCountApiV1.RecordOperationId, InventoryStockCountApiV1.DecideOperationId,
                InventoryStockCountApiV1.LabelCountsOperationId));
        foreach (var route in new[]
                 {
                     new[] { InventoryStockCountHttpCodesV1.Status400Codes, InventoryStockCountHttpCodesV1.RecordStatus404Codes,
                         InventoryStockCountHttpCodesV1.RecordStatus409Codes, InventoryStockCountHttpCodesV1.Status503Codes },
                     new[] { InventoryStockCountHttpCodesV1.Status400Codes, InventoryStockCountHttpCodesV1.DecideStatus404Codes,
                         InventoryStockCountHttpCodesV1.DecideStatus409Codes, InventoryStockCountHttpCodesV1.Status503Codes },
                 })
        {
            var all = route.SelectMany(set => set).ToArray();
            Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        }

        Assert.Contains(InventoryStockCountCodesV1.SelfApproval, InventoryStockCountHttpCodesV1.DecideStatus409Codes);
        Assert.DoesNotContain(InventoryStockCountCodesV1.SelfApproval, InventoryStockCountHttpCodesV1.RecordStatus409Codes);
    }

    [Fact]
    public void Permission_names_are_the_owners_own_and_distinct()
    {
        Assert.Equal(("inventory.stock-count.record", "inventory.stock-count.approve"),
            (InventoryPermissionManifestV1.StockCountRecord, InventoryPermissionManifestV1.StockCountApprove));
        var names = new[]
        {
            InventoryPermissionManifestV1.StockCountRecord, InventoryPermissionManifestV1.StockCountApprove,
            InventoryPermissionManifestV1.OpeningStockRecord, InventoryPermissionManifestV1.PhysicalAllocationWrite,
            InventoryPermissionManifestV1.LabelTransformationRecord, ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead,
        };
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Requests_refuse_unknown_members_and_the_decision_travels_as_its_name()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var decision = JsonSerializer.Deserialize<DecideInventoryStockCountV1>("""{"decision":"Reject","reason":"Yeniden say"}""", web);
        Assert.Equal(InventoryStockCountDecisionKindV1.Reject, decision!.Decision);
        Assert.Contains("\"decision\":\"Approve\"", JsonSerializer.Serialize(DecideRequest(), web), StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RecordInventoryStockCountV1>(
            """{"countId":"60000000-0000-0000-0000-000000000001","labelId":"40000000-0000-0000-0000-000000000001","expectedLabelVersion":"1","countedKg":"1","countedAtUtc":"2026-09-27T08:00:00Z","reason":"x","bookKg":"5"}""",
            web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DecideInventoryStockCountV1>("""{"decision":"Approve"}""", web));
    }

    [Fact]
    public void Owner_registration_adds_the_writer_reader_command_and_fenced_read()
    {
        var services = new ServiceCollection().AddInventoryStockCounts();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryStockCountCommandV1)
            && descriptor.ImplementationType == typeof(InventoryStockCountCommandService)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryStockCountQueryV1)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryStockCountWriterV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryStockCountReaderV1));
    }

    [Fact]
    public void Counts_and_decisions_are_company_scoped_and_an_approval_names_its_movement()
    {
        using var context = DbContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var count = model.FindEntityType(typeof(StockCountRow))!;
        Assert.Contains(count.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(LabelRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LabelId"]));
        Assert.Contains(count.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(OrganizationLocationReference)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LocationId"]));

        var decision = model.FindEntityType(typeof(StockCountDecisionRow))!;
        Assert.Equal(["CompanyId", "CountId"], decision.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(decision.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(StockCountRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "CountId"])
            && key.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Id"]));
        Assert.Contains(decision.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(MovementRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "MovementId"])
            && key.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(decision.GetCheckConstraints(), check => check.Name == "CK_StockCountDecisions_Decision");

        var movements = model.FindEntityType(typeof(MovementRow))!;
        Assert.Equal("[Kind] BETWEEN 1 AND 7",
            movements.GetCheckConstraints().Single(check => check.Name == "CK_Movements_Kind").Sql);
        Assert.Equal(6, (int)StockMovementKind.CountAdjustment);
    }

    private static StockCount Count(decimal book, decimal counted) =>
        new(CountId, CompanyA, Position(), Kilograms.Positive(book), Kilograms.Of(counted), At, "Sayım farkı");

    private static readonly Guid Company = CompanyA;

    private static RecordInventoryStockCountV1 RecordRequest() =>
        new(CountId, Label1, "1", "97.5", At, "Rulo tartıldı");

    private static DecideInventoryStockCountV1 DecideRequest() => new(InventoryStockCountDecisionKindV1.Approve, "Uygun");

    private static InventoryCommandMetadata Transport(string actorId) =>
        new(actorId, string.Empty, [], "corr-1", null, "key-1", "Web");

    private static InventoryStockCountViewV1 View(Guid labelId, Guid companyId) => new(
        CountId, companyId, labelId, "1", "Company", CompanyA, "Company", CompanyA, Warehouse1, BandProduct, "Free",
        "100", "97.5", "-2.5", At, "Sayım farkı", At, InventoryStockCountStatusesV1.PendingApproval, null);

    private static async Task Refused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryStockCountException>(() => task);
        Assert.Equal((status, code), (error.Status, error.Code));
    }

    private static Task RefusedAsync(Task task, int status, string code) => Refused(task, status, code);

    private static InventoryDbContext DbContext()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new InventoryDbContext(options, company);
    }

    private sealed class World
    {
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Authority Authority { get; } = new();
        public Writer Writer { get; } = new();
        public Reader Reader { get; } = new();
        public bool Known { get; init; } = true;

        public InventoryStockCountCommandService Service() =>
            new(Context, User, Authority, Writer, permission => Known
                && permission is InventoryPermissionManifestV1.StockCountRecord or InventoryPermissionManifestV1.StockCountApprove);

        public InventoryStockCountQueryService Query() => new(Context, User, Authority, Reader);
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
        public string? UserId { get; set; } = Guid.NewGuid().ToString();
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company, CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public string[] GrantedPermissions { get; set; } =
        [
            InventoryPermissionManifestV1.StockCountRecord, InventoryPermissionManifestV1.StockCountApprove,
            ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead,
        ];

        public bool CompanyWide { get; set; } = true;
        public string[] Roles { get; set; } = [];
        public Action? AfterFirst { get; set; }
        public List<string> Measured { get; } = [];

        public async Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Measured.Add(permission);
            await Task.Yield();
            var answer = new PermissionScope(GrantedPermissions.Contains(permission), CompanyWide,
                Roles.ToHashSet(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
            if (Measured.Count == 1)
            {
                AfterFirst?.Invoke();
            }

            return answer;
        }
    }

    private sealed class Writer : IInventoryStockCountWriterV1
    {
        public List<(string Operation, Guid CountId, InventoryCommandMetadata Metadata)> Calls { get; } = [];
        public InventoryStockCountException? Failure { get; set; }
        public InventoryStockCountResultV1 Result { get; } = new(View(Label1, CompanyA), false, "corr-1");

        public Task<InventoryStockCountResultV1> RecordStockCountAsync(
            RecordInventoryStockCountV1 request,
            InventoryCommandMetadata metadata,
            CancellationToken cancellationToken = default) => Answer("record", request.CountId, metadata);

        public Task<InventoryStockCountResultV1> DecideStockCountAsync(
            Guid countId,
            DecideInventoryStockCountV1 request,
            InventoryCommandMetadata metadata,
            CancellationToken cancellationToken = default) => Answer("decide", countId, metadata);

        private Task<InventoryStockCountResultV1> Answer(string operation, Guid countId, InventoryCommandMetadata metadata)
        {
            Calls.Add((operation, countId, metadata));
            return Failure is null ? Task.FromResult(Result) : Task.FromException<InventoryStockCountResultV1>(Failure);
        }
    }

    private sealed class Reader : IInventoryStockCountReaderV1
    {
        public InventoryLabelStockCountsV1? Result { get; set; }
        public Action? During { get; set; }

        public Task<InventoryLabelStockCountsV1?> ReadStockCountsAsync(
            Guid labelId,
            int limit = InventoryStockCountApiV1.LabelCountsLimit,
            CancellationToken cancellationToken = default)
        {
            During?.Invoke();
            return Task.FromResult(Result);
        }
    }
}
