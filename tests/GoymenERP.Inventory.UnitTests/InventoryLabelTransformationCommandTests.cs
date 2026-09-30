using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryLabelTransformationCommandTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Source = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Granted_actor_reaches_the_writer_once_with_metadata_derived_from_the_measured_scope()
    {
        var world = new World();
        world.Authority.Roles = ["stok.b", "stok.a"];

        var result = await world.Service().RecordAsync(Request(), Transport(world.User.UserId!));

        Assert.Same(world.Writer.Result, result);
        var (request, metadata) = Assert.Single(world.Writer.Calls);
        Assert.Equal(Source, Assert.Single(request.Sources).LabelId);
        Assert.Equal(
            (world.User.UserId!, InventoryPermissionManifestV1.LabelTransformationRecord, "corr-1", "key-1", "http"),
            (metadata.ActorId, metadata.PermissionCode, metadata.CorrelationId, metadata.IdempotencyKey, metadata.SourceChannel));
        Assert.Equal(new[] { "stok.a", "stok.b" }, metadata.RoleCodes);
        Assert.Equal(
            new[] { InventoryPermissionManifestV1.LabelTransformationRecord, InventoryPermissionManifestV1.LabelTransformationRecord },
            world.Authority.Measured);
    }

    [Fact]
    public async Task Caller_supplied_actor_permission_and_roles_are_never_trusted()
    {
        var world = new World();
        var transport = Transport(world.User.UserId!) with
        {
            PermissionCode = InventoryPermissionManifestV1.OpeningStockRecord,
            RoleCodes = ["system.admin"],
        };

        await world.Service().RecordAsync(Request(), transport);

        var metadata = Assert.Single(world.Writer.Calls).Metadata;
        Assert.Equal(InventoryPermissionManifestV1.LabelTransformationRecord, metadata.PermissionCode);
        Assert.Empty(metadata.RoleCodes);

        await Refused(world.Service().RecordAsync(Request(), Transport("someone-else")), 403,
            InventoryLabelTransformationCommandCodesV1.Forbidden);
        Assert.Single(world.Writer.Calls);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("site-scoped")]
    public async Task Denied_or_site_scoped_authority_never_reaches_the_writer(string denial)
    {
        var world = new World();
        world.Authority.Granted = denial != "denied";
        world.Authority.CompanyWide = denial != "site-scoped";

        await Refused(world.Service().RecordAsync(Request(), Transport(world.User.UserId!)), 403,
            InventoryLabelTransformationCommandCodesV1.Forbidden);

        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("membership")]
    [InlineData("authentication")]
    public async Task Authority_lost_between_the_two_measurements_never_reaches_the_writer(string change)
    {
        var world = new World();
        world.Authority.AfterFirst = () =>
        {
            switch (change)
            {
                case "permission": world.Authority.Granted = false; break;
                case "company": world.Context.Set(Guid.NewGuid()); break;
                case "actor": world.User.UserId = Guid.NewGuid().ToString(); break;
                case "membership": world.User.Access = false; break;
                case "authentication": world.User.IsAuthenticated = false; break;
            }
        };

        await Refused(world.Service().RecordAsync(Request(), Transport(world.User.UserId!)), 403,
            InventoryLabelTransformationCommandCodesV1.Forbidden);

        Assert.Empty(world.Writer.Calls);
    }

    [Fact]
    public async Task Missing_identity_or_company_is_refused_before_any_measurement()
    {
        var world = new World();
        world.User.IsAuthenticated = false;
        await Refused(world.Service().RecordAsync(Request(), Transport("x")), 401,
            InventoryLabelTransformationCommandCodesV1.Unauthenticated);

        world = new World();
        world.Context.Set(Guid.Empty);
        await Refused(world.Service().RecordAsync(Request(), Transport(world.User.UserId!)), 403,
            InventoryLabelTransformationCommandCodesV1.Forbidden);

        world = new World();
        world.User.Access = false;
        await Refused(world.Service().RecordAsync(Request(), Transport(world.User.UserId!)), 403,
            InventoryLabelTransformationCommandCodesV1.Forbidden);

        Assert.Empty(world.Authority.Measured);
        Assert.Empty(world.Writer.Calls);
    }

    [Fact]
    public async Task Unregistered_permission_name_is_a_setup_gap_and_never_a_grant()
    {
        var world = new World { Known = false };

        await Refused(world.Service().RecordAsync(Request(), Transport(world.User.UserId!)), 503,
            InventoryLabelTransformationCommandCodesV1.PermissionUnregistered);

        Assert.Empty(world.Authority.Measured);
        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData(409, InventoryLabelGenealogyCodesV1.ActiveAllocationConflict)]
    [InlineData(409, InventoryLabelGenealogyCodesV1.LabelVersionConflict)]
    [InlineData(404, InventoryLabelGenealogyCodesV1.LabelNotFound)]
    [InlineData(400, InventoryLabelGenealogyCodesV1.ValidationFailed)]
    [InlineData(400, InventoryScaleCodes.ScaleExceeded)]
    public async Task Writer_refusals_pass_through_unchanged(int status, string code)
    {
        var world = new World();
        world.Writer.Failure = new InventoryLabelGenealogyException(status, code);

        await Refused(world.Service().RecordAsync(Request(), Transport(world.User.UserId!)), status, code);
    }

    [Theory]
    [InlineData(InventoryScaleCodes.ProfileUnusable)]
    [InlineData("settings_provider_unavailable")]
    public async Task Kilogram_profile_provider_states_are_answered_as_unavailable(string code)
    {
        var world = new World();
        world.Writer.Failure = new InventoryLabelGenealogyException(400, code);

        await Refused(world.Service().RecordAsync(Request(), Transport(world.User.UserId!)), 503, code);
    }

    [Fact]
    public void The_command_name_is_its_own_and_distinct_from_every_other_inventory_permission()
    {
        Assert.Equal("inventory.label-transformation.record", InventoryPermissionManifestV1.LabelTransformationRecord);
        Assert.DoesNotContain(InventoryPermissionManifestV1.LabelTransformationRecord, new[]
        {
            InventoryPermissionManifestV1.OpeningStockRecord,
            InventoryPermissionManifestV1.PhysicalAllocationWrite,
            ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead,
        });
    }

    [Fact]
    public void Http_contract_is_pinned_and_status_code_sets_are_disjoint()
    {
        Assert.Equal("/api/v1/inventory/label-transformations", InventoryLabelTransformationApiV1.Path);
        Assert.Equal("RecordInventoryLabelTransformationV1", InventoryLabelTransformationApiV1.RecordOperationId);
        var sets = new[]
        {
            InventoryLabelTransformationHttpCodesV1.Status400Codes,
            InventoryLabelTransformationHttpCodesV1.Status404Codes,
            InventoryLabelTransformationHttpCodesV1.Status409Codes,
            InventoryLabelTransformationHttpCodesV1.Status503Codes,
        };
        var all = sets.SelectMany(set => set).ToArray();
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(InventoryLabelGenealogyCodesV1.ActiveAllocationConflict, InventoryLabelTransformationHttpCodesV1.Status409Codes);
        Assert.Contains(InventoryLabelGenealogyCodesV1.CorruptState, InventoryLabelTransformationHttpCodesV1.Status503Codes);
    }

    [Fact]
    public void Kind_travels_as_its_name_on_the_wire()
    {
        var json = """
            {"transformationId":"30000000-0000-0000-0000-000000000001","kind":"Merge",
             "sources":[{"labelId":"20000000-0000-0000-0000-000000000001","expectedLabelVersion":"2"}],
             "outputs":[{"labelId":"40000000-0000-0000-0000-000000000001","code":"LBL-9","quantityKg":"10.5"}],
             "occurredAtUtc":"2026-09-26T08:00:00Z","reason":"Paket birleştirme"}
            """;
        var request = JsonSerializer.Deserialize<TransformInventoryLabelsV1>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(InventoryLabelTransformationKindV1.Merge, request!.Kind);
        Assert.Contains("\"kind\":\"Split\"",
            JsonSerializer.Serialize(Request(), new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.Ordinal);
    }

    [Fact]
    public void Owner_registration_adds_the_fenced_command_beside_the_writer()
    {
        var services = new ServiceCollection().AddInventoryLabelGenealogy();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryLabelTransformationCommandV1)
            && descriptor.ImplementationType == typeof(InventoryLabelTransformationCommandService)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private static TransformInventoryLabelsV1 Request() => new(
        Guid.Parse("30000000-0000-0000-0000-000000000001"),
        InventoryLabelTransformationKindV1.Split,
        [new InventoryLabelTransformationSourceV1(Source, "2")],
        [
            new InventoryLabelTransformationOutputV1(Guid.Parse("40000000-0000-0000-0000-000000000001"), "LBL-1A", "4"),
            new InventoryLabelTransformationOutputV1(Guid.Parse("40000000-0000-0000-0000-000000000002"), "LBL-1B", "6.5"),
        ],
        new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero),
        "Paket bölme");

    private static InventoryCommandMetadata Transport(string actorId) =>
        new(actorId, string.Empty, [], "corr-1", null, "key-1", "http");

    private static async Task Refused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryLabelGenealogyException>(() => task);
        Assert.Equal((status, code), (error.Status, error.Code));
    }

    private sealed class World
    {
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Authority Authority { get; } = new();
        public Writer Writer { get; } = new();
        public bool Known { get; init; } = true;

        public InventoryLabelTransformationCommandService Service() =>
            new(Context, User, Authority, Writer, permission =>
                Known && permission == InventoryPermissionManifestV1.LabelTransformationRecord);
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
        public bool Access { get; set; } = true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => Access && AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
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
            var answer = new PermissionScope(Granted, CompanyWide, Roles.ToHashSet(StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
            if (Measured.Count == 1)
            {
                AfterFirst?.Invoke();
            }

            return answer;
        }
    }

    private sealed class Writer : IInventoryLabelGenealogyWriterV1
    {
        public List<(TransformInventoryLabelsV1 Request, InventoryCommandMetadata Metadata)> Calls { get; } = [];
        public InventoryLabelGenealogyException? Failure { get; set; }
        public InventoryLabelTransformationResultV1 Result { get; } = new(
            new InventoryLabelTransformationViewV1(Guid.NewGuid(), Company, "Split",
                new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero), "Paket bölme", [Source], [Guid.NewGuid()]),
            false,
            "corr-1");

        public Task<InventoryLabelTransformationResultV1> TransformAsync(
            TransformInventoryLabelsV1 request,
            InventoryCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((request, metadata));
            return Failure is null ? Task.FromResult(Result) : Task.FromException<InventoryLabelTransformationResultV1>(Failure);
        }
    }
}
