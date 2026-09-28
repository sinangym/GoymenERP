using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Scraps;
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
/// ST-02 scrap write-off without a database: the scrap and its one Scrap movement, the ledger effect (free or
/// blocked), the fenced record/decide command, the fenced read and the persistent model. The stored flow (replay,
/// stale, maker≠checker, allocation, company fence, rollback refusal) is proven by the relational acceptance tests.
/// </summary>
public sealed class InventoryStockScrapTests
{
    private static readonly Guid ScrapId = Guid.Parse("61000000-0000-0000-0000-000000000001");

    [Theory]
    [InlineData(100, 2.5, 97.5)]
    [InlineData(100, 100, 0)]
    [InlineData(0.002, 0.001, 0.001)]
    public void A_scrap_posts_one_out_line_of_exactly_the_scrap_and_leaves_book_minus_scrap(
        decimal book, decimal scrapKg, decimal remaining)
    {
        var scrap = Scrap(book, scrapKg);

        Assert.Equal(remaining, scrap.Remaining.Value);
        var movementId = Guid.NewGuid();
        var movement = scrap.WriteOff(movementId, At.AddHours(1));
        Assert.Equal((movementId, StockMovementKind.Scrap, At.AddHours(1)), (movement.Id, movement.Kind, movement.OccurredAtUtc));
        Assert.Equal((StockScrap.SourceKind, ScrapId, (Guid?)null), (movement.Source.Kind, movement.Source.Id, movement.Source.LineId));
        var line = Assert.Single(movement.Lines);
        Assert.Equal((scrap.Position, StockDirection.Out, scrapKg), (line.Position, line.Direction, line.Quantity.Value));
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("long-reason")]
    [InlineData("offset")]
    [InlineData("company")]
    [InlineData("zero")]
    [InlineData("above-book")]
    public void A_scrap_needs_a_reason_UTC_time_its_own_companys_position_and_a_positive_amount_within_the_book(string fault)
    {
        var position = Position();
        var exception = Assert.Throws<InventoryRuleException>(() => new StockScrap(ScrapId,
            fault == "company" ? CompanyB : CompanyA, position, Kilograms.Positive(10m),
            fault switch { "zero" => Kilograms.Zero, "above-book" => Kilograms.Positive(10.001m), _ => Kilograms.Positive(1m) },
            fault == "offset" ? new DateTimeOffset(2026, 9, 28, 11, 0, 0, TimeSpan.FromHours(3)) : At,
            fault switch { "reason" => "   ", "long-reason" => new string('x', 1001), _ => "Hurda" }));

        Assert.Equal(InventoryRuleCodes.InvalidStockScrap, exception.Code);
    }

    [Fact]
    public void The_approved_scrap_leaves_free_or_blocked_stock_without_a_release()
    {
        foreach (var block in new[] { BlockState.Free, BlockState.Blocked })
        {
            var position = Position(block: block);
            var ledger = StockLedger.FromStoredBalances([new StockBalance(position, Kilograms.Of(100m))]);
            var scrap = new StockScrap(ScrapId, CompanyA, position, Kilograms.Positive(100m), Kilograms.Positive(30.5m), At, "Hasar");

            ledger.Apply(scrap.WriteOff(Guid.NewGuid(), At));

            Assert.Equal(69.5m, ledger.QuantityAt(position).Value);
        }
    }

    [Fact]
    public void The_same_scrap_posts_at_most_once_and_a_scrap_movement_is_one_out_line()
    {
        var position = Position();
        var ledger = StockLedger.FromStoredBalances([new StockBalance(position, Kilograms.Of(100m))]);
        var scrap = Scrap(100m, 10m);
        ledger.Apply(scrap.WriteOff(Guid.NewGuid(), At));

        Assert.Equal(InventoryRuleCodes.DuplicateSource,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(scrap.WriteOff(Guid.NewGuid(), At))).Code);
        Assert.Equal(90m, ledger.QuantityAt(position).Value);
        Assert.Equal(InventoryRuleCodes.InvalidMovement, Assert.Throws<InventoryRuleException>(() =>
            Movement(StockMovementKind.Scrap, In(position, 1m))).Code);
        Assert.Equal(InventoryRuleCodes.InvalidMovement, Assert.Throws<InventoryRuleException>(() =>
            Movement(StockMovementKind.Scrap, Out(position, 1m), Out(position, 2m))).Code);
    }

    [Fact]
    public void A_scrap_larger_than_the_stored_book_is_refused_whole()
    {
        var position = Position();
        var ledger = StockLedger.FromStoredBalances([new StockBalance(position, Kilograms.Of(5m))]);

        Assert.Equal(InventoryRuleCodes.NegativePhysicalStock, Assert.Throws<InventoryRuleException>(() =>
            ledger.Apply(Scrap(10m, 10m).WriteOff(Guid.NewGuid(), At))).Code);
        Assert.Equal(5m, ledger.QuantityAt(position).Value);
    }

    [Fact]
    public async Task Record_measures_the_record_permission_twice_and_derives_the_metadata()
    {
        var world = new World();
        world.Authority.Roles = ["stok.b", "stok.a"];
        var transport = Transport(world.User.UserId!) with
        {
            PermissionCode = InventoryPermissionManifestV1.StockScrapApprove,
            RoleCodes = ["system.admin"],
        };

        var result = await world.Service().RecordAsync(RecordRequest(), transport);

        Assert.Same(world.Writer.Result, result);
        var call = Assert.Single(world.Writer.Calls);
        Assert.Equal(("record", InventoryPermissionManifestV1.StockScrapRecord, world.User.UserId!, "Web"),
            (call.Operation, call.Metadata.PermissionCode, call.Metadata.ActorId, call.Metadata.SourceChannel));
        Assert.Equal(new[] { "stok.a", "stok.b" }, call.Metadata.RoleCodes);
        Assert.Equal(new[] { InventoryPermissionManifestV1.StockScrapRecord, InventoryPermissionManifestV1.StockScrapRecord },
            world.Authority.Measured);
    }

    [Fact]
    public async Task Decide_measures_the_approve_permission_never_the_record_one()
    {
        var world = new World();

        await world.Service().DecideAsync(ScrapId, DecideRequest(), Transport(world.User.UserId!));

        var call = Assert.Single(world.Writer.Calls);
        Assert.Equal(("decide", ScrapId, InventoryPermissionManifestV1.StockScrapApprove),
            (call.Operation, call.ScrapId, call.Metadata.PermissionCode));
        Assert.Equal(new[] { InventoryPermissionManifestV1.StockScrapApprove, InventoryPermissionManifestV1.StockScrapApprove },
            world.Authority.Measured);
    }

    [Fact]
    public async Task Holding_only_the_record_permission_never_decides_and_count_authority_is_not_scrap_authority()
    {
        var world = new World();
        world.Authority.GrantedPermissions = [InventoryPermissionManifestV1.StockScrapRecord];

        await world.Service().RecordAsync(RecordRequest(), Transport(world.User.UserId!));
        await Refused(world.Service().DecideAsync(ScrapId, DecideRequest(), Transport(world.User.UserId!)), 403,
            InventoryStockScrapCodesV1.Forbidden);
        Assert.Equal("record", Assert.Single(world.Writer.Calls).Operation);

        world = new World();
        world.Authority.GrantedPermissions =
            [InventoryPermissionManifestV1.StockCountRecord, InventoryPermissionManifestV1.StockCountApprove];
        await Refused(world.Service().RecordAsync(RecordRequest(), Transport(world.User.UserId!)), 403,
            InventoryStockScrapCodesV1.Forbidden);
        await Refused(world.Service().DecideAsync(ScrapId, DecideRequest(), Transport(world.User.UserId!)), 403,
            InventoryStockScrapCodesV1.Forbidden);
        Assert.Empty(world.Writer.Calls);
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
                    ? world.Service().DecideAsync(ScrapId, DecideRequest(), Transport(actor))
                    : world.Service().RecordAsync(RecordRequest(), Transport(actor)),
                403, InventoryStockScrapCodesV1.Forbidden);
            Assert.Empty(world.Writer.Calls);
        }
    }

    [Fact]
    public async Task No_identity_is_401_and_an_unregistered_name_is_a_setup_gap()
    {
        var world = new World();
        world.User.IsAuthenticated = false;
        await Refused(world.Service().RecordAsync(RecordRequest(), Transport("x")), 401, InventoryStockScrapCodesV1.Unauthenticated);

        world = new World { Known = false };
        await Refused(world.Service().DecideAsync(ScrapId, DecideRequest(), Transport(world.User.UserId!)), 503,
            InventoryStockScrapCodesV1.PermissionUnregistered);
        Assert.Empty(world.Authority.Measured);
        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData(409, InventoryStockScrapCodesV1.SelfApproval, 409)]
    [InlineData(409, InventoryStockScrapCodesV1.Stale, 409)]
    [InlineData(409, InventoryStockScrapCodesV1.AllocationConflict, 409)]
    [InlineData(404, InventoryStockScrapCodesV1.ScrapNotFound, 404)]
    [InlineData(400, InventoryScaleCodes.ScaleExceeded, 400)]
    [InlineData(400, InventoryScaleCodes.ProfileUnusable, 503)]
    [InlineData(400, "settings_provider_unavailable", 503)]
    public async Task Writer_refusals_pass_through_except_provider_states(int status, string code, int answered)
    {
        var world = new World();
        world.Writer.Failure = new InventoryStockScrapException(status, code);

        await Refused(world.Service().DecideAsync(ScrapId, DecideRequest(), Transport(world.User.UserId!)), answered, code);
    }

    [Fact]
    public async Task The_read_is_fenced_by_label_and_balance_reads_before_and_after_the_owner_read()
    {
        var world = new World();
        world.Reader.Result = new InventoryLabelStockScrapsV1(Label1, [View(Label1, CompanyA)], false);

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
        await Refused(world.Query().GetAsync(Label1), 404, InventoryStockScrapCodesV1.LabelNotFound);

        world = new World();
        world.Reader.Result = new InventoryLabelStockScrapsV1(Label1, [], false);
        world.Reader.During = () => world.Authority.GrantedPermissions = [];
        await Refused(world.Query().GetAsync(Label1), 403, InventoryStockScrapCodesV1.Forbidden);

        world = new World();
        world.Reader.Result = new InventoryLabelStockScrapsV1(Label1, [View(Label1, CompanyB)], false);
        await Refused(world.Query().GetAsync(Label1), 503, InventoryStockScrapCodesV1.Unavailable);
    }

    [Fact]
    public void Http_contract_is_pinned_and_each_routes_codes_are_disjoint_by_status()
    {
        Assert.Equal("/api/v1/inventory/stock-scraps", InventoryStockScrapApiV1.Path);
        Assert.Equal("/api/v1/inventory/stock-scraps/{scrapId}/decision", InventoryStockScrapApiV1.DecisionPathTemplate);
        Assert.Equal("/api/v1/inventory/labels/{labelId}/stock-scraps", InventoryStockScrapApiV1.LabelScrapsPathTemplate);
        Assert.Equal(("RecordInventoryStockScrapV1", "DecideInventoryStockScrapV1", "GetInventoryLabelStockScrapsV1"),
            (InventoryStockScrapApiV1.RecordOperationId, InventoryStockScrapApiV1.DecideOperationId,
                InventoryStockScrapApiV1.LabelScrapsOperationId));
        foreach (var route in new[]
                 {
                     new[] { InventoryStockScrapHttpCodesV1.Status400Codes, InventoryStockScrapHttpCodesV1.RecordStatus404Codes,
                         InventoryStockScrapHttpCodesV1.RecordStatus409Codes, InventoryStockScrapHttpCodesV1.Status503Codes },
                     new[] { InventoryStockScrapHttpCodesV1.Status400Codes, InventoryStockScrapHttpCodesV1.DecideStatus404Codes,
                         InventoryStockScrapHttpCodesV1.DecideStatus409Codes, InventoryStockScrapHttpCodesV1.Status503Codes },
                 })
        {
            var all = route.SelectMany(set => set).ToArray();
            Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        }

        Assert.Contains(InventoryStockScrapCodesV1.SelfApproval, InventoryStockScrapHttpCodesV1.DecideStatus409Codes);
        Assert.DoesNotContain(InventoryStockScrapCodesV1.SelfApproval, InventoryStockScrapHttpCodesV1.RecordStatus409Codes);
        Assert.Contains(InventoryStockScrapCodesV1.AllocationConflict, InventoryStockScrapHttpCodesV1.RecordStatus409Codes);
        Assert.Contains(InventoryStockScrapCodesV1.AllocationConflict, InventoryStockScrapHttpCodesV1.DecideStatus409Codes);
    }

    [Fact]
    public void Permission_names_are_the_owners_own_and_distinct()
    {
        Assert.Equal(("inventory.stock-scrap.record", "inventory.stock-scrap.approve"),
            (InventoryPermissionManifestV1.StockScrapRecord, InventoryPermissionManifestV1.StockScrapApprove));
        var names = new[]
        {
            InventoryPermissionManifestV1.StockScrapRecord, InventoryPermissionManifestV1.StockScrapApprove,
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
        var decision = JsonSerializer.Deserialize<DecideInventoryStockScrapV1>("""{"decision":"Reject","reason":"Kullanılabilir"}""", web);
        Assert.Equal(InventoryStockScrapDecisionKindV1.Reject, decision!.Decision);
        Assert.Contains("\"decision\":\"Approve\"", JsonSerializer.Serialize(DecideRequest(), web), StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RecordInventoryStockScrapV1>(
            """{"scrapId":"61000000-0000-0000-0000-000000000001","labelId":"40000000-0000-0000-0000-000000000001","expectedLabelVersion":"1","scrapKg":"1","scrappedAtUtc":"2026-09-28T08:00:00Z","reason":"x","bookKg":"5"}""",
            web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DecideInventoryStockScrapV1>("""{"decision":"Approve"}""", web));
    }

    [Fact]
    public void Owner_registration_adds_the_writer_reader_command_and_fenced_read()
    {
        var services = new ServiceCollection().AddInventoryStockScraps();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryStockScrapCommandV1)
            && descriptor.ImplementationType == typeof(InventoryStockScrapCommandService)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryStockScrapQueryV1)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryStockScrapWriterV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryStockScrapReaderV1));
    }

    [Fact]
    public void Scraps_and_decisions_are_company_scoped_bounded_by_the_book_and_an_approval_names_its_movement()
    {
        using var context = DbContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var scrap = model.FindEntityType(typeof(StockScrapRow))!;
        Assert.Contains(scrap.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(LabelRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LabelId"]));
        Assert.Contains(scrap.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(OrganizationLocationReference)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LocationId"]));
        Assert.Contains("[ScrapKg] > 0 AND [ScrapKg] <= [BookKg]",
            scrap.GetCheckConstraints().Single(check => check.Name == "CK_StockScraps_State").Sql, StringComparison.Ordinal);

        var decision = model.FindEntityType(typeof(StockScrapDecisionRow))!;
        Assert.Equal(["CompanyId", "ScrapId"], decision.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(decision.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(StockScrapRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "ScrapId"])
            && key.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Id"]));
        Assert.Contains(decision.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(MovementRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "MovementId"])
            && key.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Equal("[MovementId] IS NOT NULL", decision.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "MovementId"]))
            .GetFilter());
        Assert.Contains(decision.GetCheckConstraints(), check => check.Name == "CK_StockScrapDecisions_Decision");
        Assert.Equal(7, (int)StockMovementKind.Scrap);
    }

    private static StockScrap Scrap(decimal book, decimal scrapKg) =>
        new(ScrapId, CompanyA, Position(), Kilograms.Positive(book), Kilograms.Positive(scrapKg), At, "Hurda");

    private static RecordInventoryStockScrapV1 RecordRequest() =>
        new(ScrapId, Label1, "1", "2.5", At, "Kenar hasarı");

    private static DecideInventoryStockScrapV1 DecideRequest() => new(InventoryStockScrapDecisionKindV1.Approve, "Uygun");

    private static InventoryCommandMetadata Transport(string actorId) =>
        new(actorId, string.Empty, [], "corr-1", null, "key-1", "Web");

    private static InventoryStockScrapViewV1 View(Guid labelId, Guid companyId) => new(
        ScrapId, companyId, labelId, "1", "Company", CompanyA, "Company", CompanyA, Warehouse1, BandProduct, "Free",
        "100", "2.5", "97.5", At, "Kenar hasarı", At, InventoryStockScrapStatusesV1.PendingApproval, null);

    private static async Task Refused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryStockScrapException>(() => task);
        Assert.Equal((status, code), (error.Status, error.Code));
    }

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

        public InventoryStockScrapCommandService Service() =>
            new(Context, User, Authority, Writer, permission => Known
                && permission is InventoryPermissionManifestV1.StockScrapRecord or InventoryPermissionManifestV1.StockScrapApprove);

        public InventoryStockScrapQueryService Query() => new(Context, User, Authority, Reader);
    }

    private sealed class Context : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = CompanyA;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = Guid.NewGuid().ToString();
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public string[] GrantedPermissions { get; set; } =
        [
            InventoryPermissionManifestV1.StockScrapRecord, InventoryPermissionManifestV1.StockScrapApprove,
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

    private sealed class Writer : IInventoryStockScrapWriterV1
    {
        public List<(string Operation, Guid ScrapId, InventoryCommandMetadata Metadata)> Calls { get; } = [];
        public InventoryStockScrapException? Failure { get; set; }
        public InventoryStockScrapResultV1 Result { get; } = new(View(Label1, CompanyA), false, "corr-1");

        public Task<InventoryStockScrapResultV1> RecordStockScrapAsync(
            RecordInventoryStockScrapV1 request,
            InventoryCommandMetadata metadata,
            CancellationToken cancellationToken = default) => Answer("record", request.ScrapId, metadata);

        public Task<InventoryStockScrapResultV1> DecideStockScrapAsync(
            Guid scrapId,
            DecideInventoryStockScrapV1 request,
            InventoryCommandMetadata metadata,
            CancellationToken cancellationToken = default) => Answer("decide", scrapId, metadata);

        private Task<InventoryStockScrapResultV1> Answer(string operation, Guid scrapId, InventoryCommandMetadata metadata)
        {
            Calls.Add((operation, scrapId, metadata));
            return Failure is null ? Task.FromResult(Result) : Task.FromException<InventoryStockScrapResultV1>(Failure);
        }
    }

    private sealed class Reader : IInventoryStockScrapReaderV1
    {
        public InventoryLabelStockScrapsV1? Result { get; set; }
        public Action? During { get; set; }

        public Task<InventoryLabelStockScrapsV1?> ReadStockScrapsAsync(
            Guid labelId,
            int limit = InventoryStockScrapApiV1.LabelScrapsLimit,
            CancellationToken cancellationToken = default)
        {
            During?.Invoke();
            return Task.FromResult(Result);
        }
    }
}
