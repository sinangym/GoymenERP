using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryLabelClassificationTests
{
    private const string Permission = "inventory.label-classification.write";

    [Theory]
    [InlineData("Standard", InventoryLabelClassificationV1.Standard)]
    [InlineData("SecondQuality", InventoryLabelClassificationV1.SecondQuality)]
    [InlineData("Scrap", InventoryLabelClassificationV1.Scrap)]
    public void Published_classification_names_are_the_request_wire_contract(
        string name,
        InventoryLabelClassificationV1 expected)
    {
        var request = JsonSerializer.Deserialize<ClassifyInventoryLabelV1>(
            $$"""{"expectedLabelVersion":"7","classification":"{{name}}"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        Assert.Equal(("7", expected), (request.ExpectedLabelVersion, request.Classification));
        Assert.Equal(
            $$"""{"expectedLabelVersion":"7","classification":"{{name}}"}""",
            JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("\"Undefined\"")]
    public void Numeric_or_undefined_classification_tokens_are_refused(string token)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClassifyInventoryLabelV1>(
            $$"""{"expectedLabelVersion":"7","classification":{{token}}}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task Command_derives_metadata_and_keeps_a_fresh_fence_inside_the_writer()
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
        Assert.Equal(new[] { Permission, Permission, Permission }, world.Authority.Measured);
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

    [Fact]
    public async Task Authority_revoked_after_the_writer_stages_the_command_never_commits_an_effect()
    {
        var world = new World();
        world.Writer.OnStaged = () => world.Authority.Granted = false;

        await Refused(world.Service().ClassifyAsync(Label1,
            new("7", InventoryLabelClassificationV1.Scrap), Transport(world.User.UserId!)),
            403, InventoryLabelClassificationCodesV1.Forbidden);

        Assert.Single(world.Writer.Calls);
        Assert.Equal(1, world.Writer.StagedEffects);
        Assert.Equal(0, world.Writer.CommittedEffects);
        Assert.Equal(new[] { Permission, Permission, Permission }, world.Authority.Measured);
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
        public Action? OnStaged { get; set; }
        public int StagedEffects { get; private set; }
        public int CommittedEffects { get; private set; }
        public InventoryLabelClassificationResultV1 Result { get; } = new(
            new(Label1, "8", "SecondQuality", At), false, "corr-1");

        public async Task<InventoryLabelClassificationResultV1> ClassifyAsync(
            Guid labelId,
            ClassifyInventoryLabelV1 request,
            InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((labelId, metadata));
            StagedEffects++;
            OnStaged?.Invoke();
            await freshAuthorityFence(cancellationToken);
            CommittedEffects++;
            return Result;
        }
    }
}
