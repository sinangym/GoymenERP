using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryLabelClassificationTests
{
    private const string Permission = "inventory.label-classification.write";

    [Fact]
    public async Task Command_measures_the_configured_company_wide_permission_twice_and_derives_metadata()
    {
        var world = new World();
        world.Authority.Roles = ["quality.b", "quality.a"];

        var result = await world.Service().ClassifyAsync(Label1,
            new("7", InventoryLabelClassificationV1.SecondQuality), Transport(world.User.UserId!));

        Assert.Same(world.Writer.Result, result);
        var call = Assert.Single(world.Writer.Calls);
        Assert.Equal((Label1, Permission, world.User.UserId!),
            (call.LabelId, call.Metadata.PermissionCode, call.Metadata.ActorId));
        Assert.Equal(new[] { "quality.a", "quality.b" }, call.Metadata.RoleCodes);
        Assert.Equal(new[] { Permission, Permission }, world.Authority.Measured);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("site-scoped")]
    [InlineData("other-actor")]
    [InlineData("lost-between")]
    [InlineData("company-changed")]
    public async Task Denied_scoped_foreign_or_lost_authority_never_reaches_the_writer(string fault)
    {
        var world = new World();
        var actor = world.User.UserId!;
        switch (fault)
        {
            case "denied": world.Authority.Granted = false; break;
            case "site-scoped": world.Authority.CompanyWide = false; break;
            case "other-actor": actor = "someone-else"; break;
            case "lost-between": world.Authority.AfterFirst = () => world.Authority.Granted = false; break;
            case "company-changed": world.Authority.AfterFirst = () => world.Context.Set(CompanyB); break;
        }

        await Refused(world.Service().ClassifyAsync(Label1,
            new("7", InventoryLabelClassificationV1.Standard), Transport(actor)),
            403, InventoryLabelClassificationCodesV1.Forbidden);
        Assert.Empty(world.Writer.Calls);
    }

    [Fact]
    public async Task Missing_identity_and_unregistered_permission_fail_closed()
    {
        var world = new World();
        world.User.IsAuthenticated = false;
        await Refused(world.Service().ClassifyAsync(Label1,
            new("7", InventoryLabelClassificationV1.Standard), Transport("x")),
            401, InventoryLabelClassificationCodesV1.Unauthenticated);

        world = new World { Known = false };
        await Refused(world.Service().ClassifyAsync(Label1,
            new("7", InventoryLabelClassificationV1.Standard), Transport(world.User.UserId!)),
            503, InventoryLabelClassificationCodesV1.PermissionUnregistered);
        Assert.Empty(world.Authority.Measured);
        Assert.Empty(world.Writer.Calls);
    }

    private static InventoryCommandMetadata Transport(string actor) =>
        new(actor, string.Empty, [], "corr-1", null, "key-1", "Web");

    private static async Task Refused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryLabelClassificationException>(() => task);
        Assert.Equal((status, code), (error.Status, error.Code));
    }

    private sealed class World
    {
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Authority Authority { get; } = new();
        public Writer Writer { get; } = new();
        public bool Known { get; init; } = true;

        public InventoryLabelClassificationCommandService Service() => new(
            Context, User, Authority, Writer, new(Permission), _ => Known);
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
        public string? UserId { get; set; } = "classification-operator";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
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

    private sealed class Writer : IInventoryLabelClassificationWriterV1
    {
        public List<(Guid LabelId, InventoryCommandMetadata Metadata)> Calls { get; } = [];
        public InventoryLabelClassificationResultV1 Result { get; } = new(
            new(Label1, "8", "SecondQuality", At), false, "corr-1");

        public Task<InventoryLabelClassificationResultV1> ClassifyAsync(
            Guid labelId,
            ClassifyInventoryLabelV1 request,
            InventoryCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((labelId, metadata));
            return Task.FromResult(Result);
        }
    }
}
