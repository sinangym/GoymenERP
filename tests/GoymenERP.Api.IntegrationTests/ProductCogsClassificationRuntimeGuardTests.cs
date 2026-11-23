using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Controlled source guards, not real SQL or live authority acceptance.</summary>
public sealed class ProductCogsClassificationRuntimeGuardTests
{
    private static readonly Guid Company=Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Actor=Guid.Parse("22222222-2222-4222-8222-222222222222");
    [Fact]
    public void A_refused_attempt_is_audited_with_the_role_the_global_authority_requires_never_a_role_name_typed_in_the_host()
    {
        // K3-A P3 (AGENTS.md: no role names): the refusal audit reads Identity's required system role — the one the SQL authority
        // provider grants op 7 on — so the audited value stays exactly what it was while its source is the owner's single constant.
        Assert.Equal(new[]{ProductMasterAssignment.RequiredRoleCode},ProductCogsClassificationRuntime.RefusalAuditRoleCodes);
        Assert.Throws<NotSupportedException>(()=>((IList<string>)ProductCogsClassificationRuntime.RefusalAuditRoleCodes).Add("SENTETIK"));
        var source=File.ReadAllText(Path.Combine(RepositoryRoot(),"src","backend","GoymenERP.Api","ProductRuntime",
            "ProductCogsClassificationRuntime.cs"));
        Assert.DoesNotContain("\""+ProductMasterAssignment.RequiredRoleCode+"\"",source,StringComparison.Ordinal);
    }
    private static string RepositoryRoot()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"AGENTS.md"))) return directory.FullName;
        throw new InvalidOperationException("SENTETIK repository root not found.");
    }
    [Theory]
    [InlineData("denied")] [InlineData("read")] [InlineData("draft")]
    [InlineData("actor")] [InlineData("company")]
    public async Task Only_explicit_current_global_op7_reaches_the_terminal_or_Product_owner(string change)
    {
        using var services=new ServiceCollection().BuildServiceProvider();
        using var db=Context();var user=new User();var company=new CompanySource();
        var source=new Authority(operation=>change switch
        {
            "denied"=>new(false,GroupSharedMasterKind.Product,operation,Actor,Company,"revision-1"),
            "read"=>new(true,GroupSharedMasterKind.Product,GroupSharedMasterOperation.Read,Actor,Company,"revision-1"),
            "draft"=>new(true,GroupSharedMasterKind.Product,GroupSharedMasterOperation.DraftBasicEdit,Actor,Company,"revision-1"),
            "actor"=>new(true,GroupSharedMasterKind.Product,operation,Guid.NewGuid(),Company,"revision-1"),
            _=>new(true,GroupSharedMasterKind.Product,operation,Actor,Guid.NewGuid(),"revision-1"),
        });
        var runtime=Runtime(db,services,source,company,user);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>runtime.SetAsync(Guid.NewGuid(),Request(),Metadata()));
        Assert.Equal(new[]{GroupSharedMasterOperation.SetCogsClassification},source.Operations);
        Assert.Empty(db.ChangeTracker.Entries());
    }
    [Theory]
    [InlineData("product-version")] [InlineData("classification-version")] [InlineData("kind")]
    [InlineData("reason")] [InlineData("actor")] [InlineData("company")]
    [InlineData("key")] [InlineData("correlation")]
    public async Task Invalid_bound_intent_is_reauthorized_and_refused_before_replay_or_database(string change)
    {
        using var services=new ServiceCollection().BuildServiceProvider();using var db=Context();
        var source=new Authority(op=>new(true,GroupSharedMasterKind.Product,op,Actor,Company,"revision-1"));
        var request=Request();var metadata=Metadata();
        request=change switch
        {
            "product-version"=>request with{ExpectedProductVersion="01"},
            "classification-version"=>request with{ExpectedClassificationVersion="-1"},
            "kind"=>request with{Classification=(ProductCogsClassificationKindV1)0},
            "reason"=>request with{Reason=" "},_=>request,
        };
        metadata=change switch
        {
            "actor"=>metadata with{ActorId=Guid.NewGuid().ToString("D")},"company"=>metadata with{CompanyId=Guid.NewGuid()},
            "key"=>metadata with{IdempotencyKey=" bad "},"correlation"=>metadata with{CorrelationId="other"},_=>metadata,
        };
        var error=await Assert.ThrowsAsync<ProductCogsClassificationException>(()=>Runtime(db,services,source,new CompanySource(),new User())
            .SetAsync(Guid.NewGuid(),request,metadata));
        Assert.Equal((400,ProductCogsClassificationCodesV1.InvalidRequest),(error.StatusCode,error.Code));
        Assert.Equal(2,source.Operations.Count);Assert.Empty(db.ChangeTracker.Entries());
    }
    [Fact]
    public async Task Unconfigured_operational_reader_returns_typed_unavailable_without_a_default_class()
    {
        using var services=new ServiceCollection().BuildServiceProvider();using var db=Context();
        var source=new Authority(_=>throw new InvalidOperationException("No authority source may be fabricated."));
        IProductCogsClassificationReadPortV1 runtime=Runtime(db,services,source,new CompanySource(),new User(),false);
        var result=await runtime.GetAsync(Guid.NewGuid());
        Assert.Equal(ProductCogsClassificationReadOutcomeV1.Unavailable,result.Outcome);Assert.Null(result.Snapshot);
        Assert.Empty(source.Operations);Assert.Empty(db.ChangeTracker.Entries());
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Stored_next_revision_must_name_its_actual_origin_while_an_unchanged_global_class_may_have_a_foreign_origin(bool nextRevision)
    {
        using var services=new ServiceCollection().BuildServiceProvider();using var db=Context();
        var source=new Authority(op=>new(true,GroupSharedMasterKind.Product,op,Actor,Company,"revision-1"));
        var product=Guid.NewGuid();var request=Request() with{ExpectedClassificationVersion=nextRevision?"0":"1"};
        var metadata=Metadata();var context=GroupSharedMasterCommandContext.FromServerDecision(
            new(true,GroupSharedMasterKind.Product,GroupSharedMasterOperation.SetCogsClassification,Actor,Company,"revision-1"),GroupSharedMasterOperation.SetCogsClassification);
        var web=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var requestHash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{productId=product,request.ExpectedProductVersion,
            request.ExpectedClassificationVersion,request.Classification,request.Reason},web)));
        var keyHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metadata.IdempotencyKey)));
        var view=new ProductCogsClassificationViewV1(Company,product,"1","1",request.Classification,Guid.NewGuid(),
            new DateTimeOffset(2026,11,17,0,0,0,TimeSpan.Zero),ProductCogsClassificationContractsV1.SourceVersion);
        var terminal=GroupSharedCommandTerminal.Create(GroupSharedCommandBinding.Create(context,keyHash,requestHash),200,
            JsonSerializer.Serialize(new{statusCode=200,code=(string?)null,view},web),"synthetic-original",1);
        var resolver=new GroupSharedCommandReplayResolver(source,new StoredTerminal(terminal));
        var runtime=Runtime(db,services,source,new CompanySource(),new User(),replay:resolver);
        if(nextRevision)
        {
            var failure=await Assert.ThrowsAsync<ProductCogsClassificationException>(()=>runtime.SetAsync(product,request,metadata));
            Assert.Equal(503,failure.StatusCode);
        }
        else
        {
            var result=await runtime.SetAsync(product,request,metadata);Assert.True(result.IsReplay);
            Assert.Equal(view,result.Classification);Assert.Equal("synthetic-original",result.OriginalCorrelationId);
        }
        Assert.Empty(db.ChangeTracker.Entries());
    }
    private sealed class StoredTerminal(GroupSharedCommandTerminal terminal):IGroupSharedCommandTerminalReader
    {
        public Task<GroupSharedCommandTerminal?> ReadAsync(GroupSharedCommandKey key,CancellationToken token=default)
        {Assert.Equal(terminal.Binding.Key,key);return Task.FromResult<GroupSharedCommandTerminal?>(terminal);}
    }
    private static ProductCogsClassificationRuntime Runtime(ProductDbContext db,ServiceProvider services,Authority authority,
        ICompanyContext company,ICurrentUser user,bool enabled=true,IGroupSharedCommandReplayResolver? replay=null)
    {
        var correlation=new CorrelationContext();correlation.Set("synthetic-correlation",null,"Web",null,null);
        var options=new ProductRuntimeOptions("Server=localhost;Database=GuardOnly;Integrated Security=true;TrustServerCertificate=true",
            enabled?new ProductPilotIssuerBinding(Actor,Guid.NewGuid()):null,null);
        return new(options,db,company,user,authority,replay??new NoReplay(),services.GetRequiredService<IServiceScopeFactory>(),correlation,new NullProductCommandAttemptObserver());
    }
    private static ProductDbContext Context()=>new(new DbContextOptionsBuilder<ProductDbContext>()
        .UseSqlServer("Server=localhost;Database=GuardOnly;Integrated Security=true;TrustServerCertificate=true").Options);
    private static SetProductCogsClassificationV1 Request()=>new("1","0",ProductCogsClassificationKindV1.TradeGoods,"SENTETIK class");
    private static ProductCogsClassificationCommandMetadataV1 Metadata()=>new(Company,Actor.ToString("D"),"synthetic-correlation",null,"synthetic-key","Web");
    private sealed class User:ICurrentUser
    {
        public bool IsAuthenticated=>true;public string? UserId=>Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds=>new HashSet<Guid>{Company};public bool CanAccessCompany(Guid company)=>company==Company;
    }
    private sealed class CompanySource:ICompanyContext
    { public bool IsResolved=>true;public Guid CompanyId=>Company;public void Set(Guid company)=>throw new InvalidOperationException(); }
    private sealed class Authority(Func<GroupSharedMasterOperation,GroupSharedMasterAuthorizationSnapshot> source):IGroupSharedMasterAuthorizationService
    {
        public List<GroupSharedMasterOperation> Operations{get;}=[];
        public Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(GroupSharedMasterKind subject,GroupSharedMasterOperation operation,CancellationToken token=default)
        { Operations.Add(operation);return Task.FromResult(source(operation)); }
    }
    private sealed class NoReplay:IGroupSharedCommandReplayResolver
    {
        public Task<GroupSharedCommandReplayResult> ResolveAsync(GroupSharedMasterOperation operation,string keyHash,string requestHash,CancellationToken token=default)=>
            throw new InvalidOperationException("Replay cannot precede explicit authority and valid intent.");
    }
}
