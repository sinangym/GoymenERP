using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Actual global owner HTTP + SQL transactions. No production grants; existing isolated synthetic authority fixture. NOT_RUN.</summary>
public sealed partial class ProductGlobalRuntimeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Cogs_classification_is_explicit_global_and_replays_the_original_result_after_another_change()=>
        WithDatabaseAsync(async(factory,_)=>
        {
            using var client=factory.CreateClientFor("group-bootstrap-admin",CompanyA);
            var product=await CreateCogsCardAsync(client,"cogs-card");
            var unset=(await client.GetFromJsonAsync<ProductCogsClassificationViewV1>(CogsPath(product)))!;
            Assert.Null(unset.Classification);Assert.Equal("0",unset.ClassificationVersion);
            var intent=new SetProductCogsClassificationV1("1","0",ProductCogsClassificationKindV1.TradeGoods,"SENTETIK explicit trade goods");
            using var first=await SetCogsAsync(client,product,intent,"cogs-first");Assert.Equal(HttpStatusCode.OK,first.StatusCode);
            var original=(await first.Content.ReadFromJsonAsync<ProductCogsClassificationMutationV1>())!;
            Assert.Equal(("1","1"),(original.Classification.ProductVersion,original.Classification.ClassificationVersion));
            using var changed=await SetCogsAsync(client,product,intent with {ExpectedClassificationVersion="1",Classification=ProductCogsClassificationKindV1.Manufactured},"cogs-next");
            Assert.Equal(HttpStatusCode.OK,changed.StatusCode);
            using var replay=await SetCogsAsync(client,product,intent,"cogs-first");Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
            var repeated=(await replay.Content.ReadFromJsonAsync<ProductCogsClassificationMutationV1>())!;
            Assert.True(repeated.IsReplay);Assert.Equal(original.Classification,repeated.Classification);
            Assert.Equal(original.OriginalCorrelationId,repeated.OriginalCorrelationId);
            Assert.Equal("true",Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal((2,2,2),await CogsCountsAsync(factory.DatabaseConnectionString,product));
            using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            Assert.Equal(1,(await db.Products.SingleAsync(x=>x.Id==product)).Version);
        });
    [SqlServerFact]
    public Task Cogs_same_value_noop_and_terminal_stale_refusal_do_not_create_an_extra_card_revision()=>
        WithDatabaseAsync(async(factory,_)=>
        {
            using var client=factory.CreateClientFor("group-bootstrap-admin",CompanyA);var product=await CreateCogsCardAsync(client,"cogs-noop-card");
            var intent=new SetProductCogsClassificationV1("1","0",ProductCogsClassificationKindV1.TradeGoods,"SENTETIK class");
            using var first=await SetCogsAsync(client,product,intent,"cogs-noop-first");Assert.Equal(HttpStatusCode.OK,first.StatusCode);
            using var noop=await SetCogsAsync(client,product,intent with {ExpectedClassificationVersion="1"},"cogs-noop");Assert.Equal(HttpStatusCode.OK,noop.StatusCode);
            using var stale=await SetCogsAsync(client,product,intent,"cogs-stale");Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
            using var refusalReplay=await SetCogsAsync(client,product,intent,"cogs-stale");Assert.Equal(HttpStatusCode.Conflict,refusalReplay.StatusCode);
            Assert.Equal("true",Assert.Single(refusalReplay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal((1,1,3),await CogsCountsAsync(factory.DatabaseConnectionString,product));
        });
    [SqlServerFact]
    public Task Cogs_audit_failure_and_fresh_revocation_roll_back_product_audit_and_global_terminal_together()=>
        WithDatabaseAsync(async(factory,observer)=>
        {
            using var client=factory.CreateClientFor("group-bootstrap-admin",CompanyA);var product=await CreateCogsCardAsync(client,"cogs-rollback-card");
            var intent=new SetProductCogsClassificationV1("1","0",ProductCogsClassificationKindV1.TradeGoods,"SENTETIK class");
            observer.Callback=(stage,_,_)=>stage==ProductCommandAttemptStage.AuditFlushed?Task.FromException(new InjectedProductFailureException()):Task.CompletedTask;
            using var failure=await SetCogsAsync(client,product,intent,"cogs-audit-failure");Assert.Equal(HttpStatusCode.ServiceUnavailable,failure.StatusCode);
            Assert.Equal((0,0,0),await CogsCountsAsync(factory.DatabaseConnectionString,product));
            observer.Callback=async(stage,_,token)=>
            {
                if(stage!=ProductCommandAttemptStage.BeforeCommit)return;
                await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var grant=await db.Set<ProductMasterAssignment>().SingleAsync(x=>x.UserId==ActorId&&x.Operation==ProductMasterOperation.SetCogsClassification,token);
                grant.Revoke(Guid.NewGuid(),"SENTETIK isolated revocation",DateTimeOffset.UtcNow);await db.SaveChangesAsync(token);
            };
            using var denied=await SetCogsAsync(client,product,intent,"cogs-late-revocation");Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            Assert.Equal((0,0,0),await CogsCountsAsync(factory.DatabaseConnectionString,product));
        });
    [SqlServerFact]
    public Task Cogs_global_key_never_partitions_by_origin_company_and_company_role_never_substitutes_for_op7()=>
        WithDatabaseAsync(async(factory,_)=>
        {
            using var client=factory.CreateClientFor("group-bootstrap-admin",CompanyA);var product=await CreateCogsCardAsync(client,"cogs-origin-card");
            var intent=new SetProductCogsClassificationV1("1","0",ProductCogsClassificationKindV1.TradeGoods,"SENTETIK class");
            using var first=await SetCogsAsync(client,product,intent,"cogs-global-key");Assert.Equal(HttpStatusCode.OK,first.StatusCode);
            using var other=factory.CreateClientFor("group-bootstrap-admin",CompanyB);
            using var conflict=await SetCogsAsync(other,product,intent,"cogs-global-key");Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
            using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var grant=await db.Set<ProductMasterAssignment>().SingleAsync(x=>x.UserId==ActorId&&x.Operation==ProductMasterOperation.SetCogsClassification);
            grant.Revoke(Guid.NewGuid(),"SENTETIK remove only op7",DateTimeOffset.UtcNow);await db.SaveChangesAsync();
            using var denied=await SetCogsAsync(client,product,intent,"cogs-global-key");Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            using var read=await client.GetAsync(CogsPath(product));Assert.Equal(HttpStatusCode.OK,read.StatusCode);
            Assert.Equal((1,1,1),await CogsCountsAsync(factory.DatabaseConnectionString,product));
        });
    [SqlServerFact]
    public Task Cogs_concurrent_same_key_winner_is_reloaded_only_after_full_loser_rollback()=>
        WithDatabaseAsync(async(factory,observer)=>
        {
            using var client=factory.CreateClientFor("group-bootstrap-admin",CompanyA);var product=await CreateCogsCardAsync(client,"cogs-race-card");
            var staged=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var pauses=0;
            observer.Callback=async(stage,_,_)=>
            {
                if(stage==ProductCommandAttemptStage.ProductFlushed&&Interlocked.Increment(ref pauses)==1)
                {staged.TrySetResult();await release.Task.WaitAsync(TimeSpan.FromSeconds(30));}
            };
            var intent=new SetProductCogsClassificationV1("1","0",ProductCogsClassificationKindV1.Manufactured,"SENTETIK concurrent class");
            var winner=SetCogsAsync(client,product,intent,"cogs-concurrent");await staged.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using var second=factory.CreateClientFor("group-bootstrap-admin",CompanyA);
            var loser=SetCogsAsync(second,product,intent,"cogs-concurrent");release.TrySetResult();
            using var first=await winner;using var replay=await loser;
            Assert.Equal(HttpStatusCode.OK,first.StatusCode);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
            Assert.True((await replay.Content.ReadFromJsonAsync<ProductCogsClassificationMutationV1>())!.IsReplay);
            Assert.Equal((1,1,1),await CogsCountsAsync(factory.DatabaseConnectionString,product));
        });
    [SqlServerFact]
    public Task Cogs_Down_guard_allows_empty_store_and_refuses_to_erase_an_explicit_classification()=>
        WithDatabaseAsync(async(factory,_)=>
        {
            var guard=Assert.IsType<SqlOperation>(new AddProductCogsClassifications().DownOperations[0]).Sql;
            await using var sql=new SqlConnection(factory.DatabaseConnectionString);await sql.OpenAsync();
            await using var command=sql.CreateCommand();command.CommandText=guard;await command.ExecuteNonQueryAsync();
            using var client=factory.CreateClientFor("group-bootstrap-admin",CompanyA);var product=await CreateCogsCardAsync(client,"cogs-down-card");
            using var result=await SetCogsAsync(client,product,new("1","0",ProductCogsClassificationKindV1.TradeGoods,"SENTETIK class"),"cogs-down");
            Assert.Equal(HttpStatusCode.OK,result.StatusCode);
            var failure=await Assert.ThrowsAsync<SqlException>(()=>command.ExecuteNonQueryAsync());Assert.Equal(51978,failure.Number);
            Assert.Equal((1,1,1),await CogsCountsAsync(factory.DatabaseConnectionString,product));
        });
    [SqlServerFact]
    public Task Cogs_same_timestamp_owner_page_uses_classification_revision_before_cutting_the_tied_set()=>
        WithDatabaseAsync(async(factory,_)=>
        {
            var product=Guid.NewGuid();var at=new DateTimeOffset(2026,11,17,0,0,0,TimeSpan.Zero);
            await using(var sql=new SqlConnection(factory.DatabaseConnectionString))
            {
                await sql.OpenAsync();
                var ids=await OrderIdsDescendingAsync(sql,[Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()]);
                for(var version=1;version<=3;version++)
                {
                    ProductCogsClassificationViewV1 Snapshot(int revision)=>new(CompanyA,product,"1",revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        revision==0?null:revision%2==1?ProductCogsClassificationKindV1.TradeGoods:ProductCogsClassificationKindV1.Manufactured,
                        revision==0?null:CompanyA,revision==0?null:at,ProductCogsClassificationContractsV1.SourceVersion);
                    var web=new JsonSerializerOptions(JsonSerializerDefaults.Web);
                    var envelope=new GroupSharedAuditEventEnvelope(ids[version-1],new(product,CompanyA,"synthetic-tie-revision"),at,
                        "product.card.cogs-classification.changed.v1","Product.Product",product.ToString("D"),"SetCogsClassification",1,1,
                        "User",ActorId.ToString("D"),ProductCogsClassificationContractsV1.Manage,"[\"SYSTEM_ADMINISTRATOR\"]",
                        JsonSerializer.Serialize(Snapshot(version-1),web),JsonSerializer.Serialize(Snapshot(version),web),"SENTETIK same tick class", "None",null,
                        "System",null,null,"synthetic-tie-"+version,null,new string('A',64),"Succeeded",2);
                    await InsertGroupSharedAuditAsync(sql,GroupSharedAuditEventData.Create(envelope));
                }
            }
            await using var scope=factory.Services.CreateAsyncScope();var accessor=scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var prior=accessor.HttpContext;
            try
            {
                accessor.HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(GoymenClaimTypes.UserId,ActorId.ToString("D")),new Claim(GoymenClaimTypes.CompanyAccess,CompanyA.ToString("D"))],"IsolatedSqlFixture"))};
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
                var records=await scope.ServiceProvider.GetRequiredService<GroupSharedProductAuditReader>().ReadAsync(product,2);
                Assert.Equal(new[]{"3","2"},records.Select(x=>JsonDocument.Parse(x.Envelope.AfterJson).RootElement.GetProperty("classificationVersion").GetString()));
            }
            finally{accessor.HttpContext=prior;}
        });
    private static string CogsPath(Guid product)=>"/api/v1/product-cogs-classifications/"+product.ToString("D");
    private static async Task<Guid> CreateCogsCardAsync(HttpClient client,string key)
    {
        using var response=await CreateAsync(client,key,"SENTETIK explicit COGS classification");Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product.Id;
    }
    private static Task<HttpResponseMessage> SetCogsAsync(HttpClient client,Guid product,SetProductCogsClassificationV1 body,string key)
    {
        var request=new HttpRequestMessage(HttpMethod.Put,CogsPath(product)){Content=JsonContent.Create(body)};
        request.Headers.Add("Idempotency-Key",key);return client.SendAsync(request);
    }
    private static async Task<(int Revisions,int GlobalAudits,int Terminals)> CogsCountsAsync(string connectionString,Guid product)
    {
        await using var sql=new SqlConnection(connectionString);await sql.OpenAsync();await using var command=sql.CreateCommand();
        command.CommandText="""
            SELECT (SELECT COUNT(*) FROM product.CogsClassifications WHERE ProductId=@product),
              (SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'product.card.cogs-classification.changed.v1'),
              (SELECT COUNT(*) FROM idempotency.GroupSharedCommandTerminals WHERE CommandName=N'product.card.cogs-classification.set.v1');
            """;
        command.Parameters.AddWithValue("@product",product);command.Parameters.AddWithValue("@id",product.ToString("D"));
        await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
        return(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2));
    }
}
