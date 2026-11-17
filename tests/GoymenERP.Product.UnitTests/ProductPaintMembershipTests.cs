using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Product.UnitTests;

/// <summary>BS-10 source guards without a database. Synthetic / NOT_ENV; SQL and browser evidence are separate.</summary>
public sealed class ProductPaintMembershipTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("55555555-5555-4555-8555-555555555555");
    [Fact]
    public void The_narrow_membership_permission_is_registered_once_company_wide_without_grant_or_critical_classification()
    {
        var name = ProductPaintMembershipPermissionsV1.Manage;
        Assert.Equal("product.paint-memberships.manage", name);
        Assert.Equal(ErpPermissions.ProductPaintMembershipsManage, name);
        Assert.Single(ErpPermissions.All, x => x == name);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(name));
        Assert.False(ErpPermissions.IsCritical(name));
        Assert.Equal(new[] { name }, ProductPaintMembershipPermissionManifestV1.Names);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GoymenERP.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        Assert.DoesNotContain(name, File.ReadAllText(Path.Combine(root.FullName, "src/backend/GoymenERP.Api/Seed/identity-seed.json")), StringComparison.Ordinal);
    }
    [Fact]
    public void The_append_only_tenant_model_maps_original_product_version_without_owning_the_product_table()
    {
        using var database = Context();
        var model = database.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(ProductPaintMembershipVersionRow), typeof(ProductPaintMembershipLineRow), typeof(ProductPaintMembershipAttemptRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("product", entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        }
        Assert.Equal(ProductPriceListDbContext.PaintMembershipVersionCheck, model.FindEntityType(typeof(ProductPaintMembershipVersionRow))!.GetCheckConstraints().Single().Sql);
        Assert.Equal(ProductPriceListDbContext.PaintMembershipLineCheck, model.FindEntityType(typeof(ProductPaintMembershipLineRow))!.GetCheckConstraints().Single().Sql);
        Assert.Equal(ProductPriceListDbContext.PaintMembershipAttemptCheck, model.FindEntityType(typeof(ProductPaintMembershipAttemptRow))!.GetCheckConstraints().Single().Sql);
        var product = model.FindEntityType(typeof(PriceListProductReference))!;
        Assert.NotNull(product.FindProperty("Version"));
        Assert.True(product.IsTableExcludedFromMigrations());
    }
    [Fact]
    public void The_generated_paint_migration_is_last_matches_the_model_and_refuses_Down_before_every_drop()
    {
        using var database = Context();
        Assert.Equal("AddProductPaintMemberships", database.Database.GetMigrations().Last().Split('_', 2)[1]);
        Assert.False(database.Database.HasPendingModelChanges());
        var migration = new AddProductPaintMemberships();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(new[] { "PaintMembershipAttempts", "PaintMembershipVersions", "PaintMembershipLines" }, migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name));
        Assert.DoesNotContain(migration.UpOperations, operation => operation is AddColumnOperation or AlterColumnOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51975", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[PaintMembershipVersions]", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[PaintMembershipLines]", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[PaintMembershipAttempts]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }
    [Theory]
    [InlineData("header", true)] [InlineData("line", true)] [InlineData("attempt", true)]
    [InlineData("header", false)] [InlineData("line", false)] [InlineData("attempt", false)]
    public void A_membership_history_or_attempt_cannot_be_modified_or_deleted(string kind, bool delete)
    {
        using var database = Context();
        object row = kind switch
        {
            "header" => new ProductPaintMembershipVersionRow { CompanyId = CompanyA, Version = 1 },
            "line" => new ProductPaintMembershipLineRow { CompanyId = CompanyA, Version = 1, ProductId = Guid.NewGuid(), MembershipVersion = 1 },
            _ => new ProductPaintMembershipAttemptRow { CompanyId = CompanyA, CommandName = "SENTETIK", IdempotencyKeyHash = "SENTETIK" },
        };
        database.Attach(row);
        database.Entry(row).State = delete ? EntityState.Deleted : EntityState.Modified;
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
    }
    [Theory]
    [InlineData("header")] [InlineData("line")] [InlineData("attempt")]
    public void A_membership_insert_for_another_company_is_refused_before_a_database_call(string kind)
    {
        using var database = Context();
        object row = kind switch
        {
            "header" => new ProductPaintMembershipVersionRow { CompanyId = CompanyB, Version = 1 },
            "line" => new ProductPaintMembershipLineRow { CompanyId = CompanyB, Version = 1, ProductId = Guid.NewGuid(), MembershipVersion = 1 },
            _ => new ProductPaintMembershipAttemptRow { CompanyId = CompanyB, CommandName = "SENTETIK", IdempotencyKeyHash = "SENTETIK" },
        };
        database.Add(row);
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
    }
    [Theory]
    [InlineData("0", true)] [InlineData("1", true)] [InlineData("9223372036854775807", true)]
    [InlineData(null, false)] [InlineData("01", false)] [InlineData("-1", false)] [InlineData("1.0", false)]
    [InlineData(" 1", false)] [InlineData("+1", false)] [InlineData("9223372036854775808", false)]
    public void Expected_versions_are_canonical_int64_text_without_coercion(string? text, bool valid) => Assert.Equal(valid, ProductPaintMembershipService.Version(text, out _));
    [Theory]
    [InlineData("../paint.xlsx", "AA==")] [InlineData("paint.xlsm", "AA==")]
    [InlineData("C:\\paint.xlsx", "AA==")] [InlineData("paint.xlsx", "bad")] [InlineData("paint.xlsx", "")]
    public void Unsafe_file_envelopes_are_refused_without_opening_a_database(string name, string content)
    {
        var refusal = Assert.Throws<ProductPaintMembershipException>(() => ProductPaintMembershipService.Parse(name, content, null));
        Assert.Equal((422, ProductPaintMembershipCodesV1.InvalidFile), (refusal.StatusCode, refusal.Code));
    }
    [Fact]
    public void Unknown_json_fields_and_a_missing_membership_boolean_are_not_silently_interpreted_as_a_removal()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SetProductPaintMembershipV1>("{\"expectedVersion\":\"1\",\"expectedProductVersion\":\"1\",\"expectedMembershipVersion\":\"1\",\"reason\":\"SENTETIK\"}", json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ValidateProductPaintMembershipImportV1>("{\"fileName\":\"a.xlsx\",\"contentBase64\":\"AA==\",\"companyId\":\"injected\"}", json));
    }
    [Theory]
    [InlineData("actor")] [InlineData("company")] [InlineData("permission")] [InlineData("partial_scope")]
    public async Task Authority_refresh_cannot_replace_the_original_actor_or_company_or_broaden_a_partial_scope(string drift)
    {
        var company = new MutableCompany(CompanyA);
        var user = new MutableUser();
        using var database = Context(company);
        var authority = new Authority(() =>
        {
            if (drift == "actor") user.UserId = "SENTETIK-other";
            if (drift == "company") company.Set(CompanyB);
            return new PermissionScope(drift != "permission", drift != "partial_scope", new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        });
        var service = new ProductPaintMembershipService(database, company, user, authority, new NoAudit(), new NoWriter(), new NoRefused(), TimeProvider.System,
            new ProductPaintMembershipReader(database, company, user));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetCurrentAsync());
        Assert.Equal(new[] { ProductPaintMembershipPermissionsV1.Manage }, authority.Asked);
    }
    [Fact]
    public async Task The_consumer_source_is_unavailable_without_an_authenticated_company_member_instead_of_false_NoSource()
    {
        var company = new MutableCompany(CompanyA);
        var user = new MutableUser { UserId = null };
        using var database = Context(company);
        var reader = new ProductPaintMembershipReader(database, company, user);
        Assert.Equal(ProductPaintMembershipReadOutcomeV1.Unavailable, (await reader.GetAsync(Guid.NewGuid())).Outcome);
        Assert.Equal(ProductPaintMembershipCatalogOutcomeV1.Unavailable, (await reader.ListAsync()).Outcome);
    }
    private static ProductPriceListDbContext Context(ICompanyContext? company = null)
    {
        company ??= new MutableCompany(CompanyA);
        return new(new DbContextOptionsBuilder<ProductPriceListDbContext>().UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
    private sealed class MutableCompany(Guid id) : ICompanyContext { public bool IsResolved => true; public Guid CompanyId { get; private set; } = id; public void Set(Guid value) => CompanyId = value; }
    private sealed class MutableUser : ICurrentUser
    {
        public string? UserId { get; set; } = "SENTETIK-actor";
        public bool IsAuthenticated => UserId is not null;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, CompanyB };
        public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id);
    }
    private sealed class Authority(Func<PermissionScope> answer) : IErpAuthorityRevalidationService
    {
        public List<string> Asked { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken ct = default)
        { Asked.Add(permission); return Task.FromResult(answer()); }
    }
    private sealed class NoAudit : IAuditEventCollector { public void Enqueue(AuditEventDraft draft) => throw new InvalidOperationException("Unexpected audit before the authority fence."); }
    private sealed class NoWriter : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => false;
        public Task WritePendingAsync(IDbTransaction tx, CancellationToken ct = default) => throw new InvalidOperationException("Unexpected audit writer.");
        public void DiscardPending() { }
    }
    private sealed class NoRefused : IRefusedAttemptRecorderV1
    {
        public void Record(RefusedAttemptV1 attempt) => throw new InvalidOperationException("Unexpected refused attempt.");
        public Task<RefusedAttemptRecordResultV1> RecordStandaloneAsync(RefusedAttemptV1 attempt, CancellationToken ct = default) => throw new InvalidOperationException();
        public void NoteReplayedRefusal(string refusalClass) => throw new InvalidOperationException();
    }
}
