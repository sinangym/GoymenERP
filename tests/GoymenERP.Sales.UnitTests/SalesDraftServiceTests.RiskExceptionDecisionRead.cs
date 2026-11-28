using System.Text.Json;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    private static SalesRiskExceptionDecisionSourceV1 RiskDecisionSource() => new(Company,"SENTETIK SO-5",4,"Draft","Approved",false,
        new(Guid.Parse("f5050000-0000-4000-8000-000000000001"),Guid.Parse("f5050000-0000-4000-8000-000000000002"),1,RiskShipment,
            "Requested",1,"  SENTETIK risk gerekçesi  ","SENTETIK-requester",new(2026,9,18,6,0,0,TimeSpan.Zero),null,null,null,null),null);
    [Theory]
    [InlineData("Draft",true)] [InlineData("Closed",false)] [InlineData("Cancelled",false)]
    public async Task Risk_selected_read_is_price_free_and_reject_does_not_require_an_eligible_order(string state,bool approve)
    {
        var world=new World();var source=RiskDecisionSource() with{OrderState=state};var store=new FakeRiskExceptionStore{RiskDecisionSourceValue=source};
        var answer=await RiskService(world,store).ReadRiskExceptionDecisionAsync(source.Recorded.Id,_=>Task.CompletedTask);
        Assert.Equal(SalesRiskExceptionDecisionReadStateV1.Found,answer.State);Assert.NotNull(answer.Detail);
        Assert.Equal((approve,true),(answer.Detail.ApprovalSupported,answer.Detail.RejectionSupported));
        Assert.Equal(source.Recorded.Reason,answer.Detail.Exception.Reason);Assert.True(SalesRiskExceptionService.MatchesRiskDecisionDetail(answer.Detail));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(answer.Detail, SalesDraftJson.Options));
        Assert.Equal(12, json.RootElement.EnumerateObject().Count());
        Assert.False(json.RootElement.TryGetProperty("source", out _)); Assert.False(json.RootElement.TryGetProperty("observedBusinessDate", out _));
        Assert.True(json.RootElement.TryGetProperty("exception", out var exception)); Assert.Equal(13, exception.EnumerateObject().Count());
        Assert.All(world.Authority.Measured,p=>Assert.Equal(RiskApprovePermission,p));Assert.Empty(store.Writes);Assert.Empty(store.Refusals);
    }
    [Fact]
    public async Task Risk_selected_versions_are_lossless_text_and_a_nonincrementable_request_has_no_supported_decision()
    {
        var world = new World(); var s = RiskDecisionSource();
        var source = s with { OrderVersion = 9007199254740993L, Recorded = s.Recorded with { Version = long.MaxValue } };
        var store = new FakeRiskExceptionStore { RiskDecisionSourceValue = source };
        var answer = await RiskService(world, store).ReadRiskExceptionDecisionAsync(source.Recorded.Id, _ => Task.CompletedTask);
        Assert.Equal(SalesRiskExceptionDecisionReadStateV1.Found, answer.State); Assert.NotNull(answer.Detail);
        Assert.Equal(("9007199254740993", "9223372036854775807"), (answer.Detail.OrderVersion, answer.Detail.Exception.Version));
        Assert.False(answer.Detail.ApprovalSupported); Assert.False(answer.Detail.RejectionSupported);
        Assert.Empty(store.Writes); Assert.Empty(store.Refusals);
    }
    [Fact]
    public async Task Risk_selected_read_requester_denial_and_missing_source_do_not_release_human_facts()
    {
        var world=new World();var source=RiskDecisionSource();var store=new FakeRiskExceptionStore{RiskDecisionSourceValue=source};world.User.Id=source.Recorded.RequestedBy;
        var denied=await RiskService(world,store).ReadRiskExceptionDecisionAsync(source.Recorded.Id,_=>Task.CompletedTask);
        Assert.Equal(SalesRiskExceptionDecisionReadStateV1.Denied,denied.State);Assert.Null(denied.Detail);
        world.User.Id=Actor;store.RiskDecisionSourceValue=null;
        var missing=await RiskService(world,store).ReadRiskExceptionDecisionAsync(source.Recorded.Id,_=>Task.CompletedTask);
        Assert.Equal(SalesRiskExceptionDecisionReadStateV1.NotFound,missing.State);Assert.Null(missing.Detail);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task Risk_selected_read_every_completed_consumer_cancellation_releases_no_private_facts(int phase)
    {
        var world=new World();var source=RiskDecisionSource();var store=new FakeRiskExceptionStore{RiskDecisionSourceValue=source};using var cancel=new CancellationTokenSource();var calls=0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>RiskService(world,store).ReadRiskExceptionDecisionAsync(source.Recorded.Id,
            _=>{if(++calls==phase)cancel.Cancel();return Task.CompletedTask;},cancel.Token));
        Assert.Equal(phase,calls);Assert.Empty(store.Writes);Assert.Empty(store.Refusals);
    }
    [Fact]
    public async Task Risk_selected_final_completed_consumer_permission_loss_is_denied_without_facts()
    {
        var world=new World();var source=RiskDecisionSource();var store=new FakeRiskExceptionStore{RiskDecisionSourceValue=source};var calls=0;
        var result=await RiskService(world,store).ReadRiskExceptionDecisionAsync(source.Recorded.Id,_=>{if(++calls==5)world.Authority.Denied.Add(RiskApprovePermission);return Task.CompletedTask;});
        Assert.Equal(5,calls);Assert.Equal(SalesRiskExceptionDecisionReadStateV1.Denied,result.State);Assert.Null(result.Detail);
    }
    [Theory]
    [InlineData("foreign")] [InlineData("wrong-id")] [InlineData("null-record")] [InlineData("bad-version")] [InlineData("bad-state")]
    public async Task Risk_selected_owner_refuses_malformed_provider_source(string fault)
    {
        var world=new World();var s=RiskDecisionSource();var source=fault switch{"foreign"=>s with{CompanyId=Guid.NewGuid()},"wrong-id"=>s with{Recorded=s.Recorded with{Id=Guid.NewGuid()}},
            "null-record"=>s with{Recorded=null!},"bad-version"=>s with{Recorded=s.Recorded with{Version=0}},_=>s with{Recorded=s.Recorded with{State="5"}}};
        var answer=await RiskService(world,new FakeRiskExceptionStore{RiskDecisionSourceValue=source}).ReadRiskExceptionDecisionAsync(s.Recorded.Id,_=>Task.CompletedTask);
        Assert.Equal(SalesRiskExceptionDecisionReadStateV1.Unavailable,answer.State);Assert.Null(answer.Detail);
    }
    private sealed partial class FakeRiskExceptionStore : ISalesRiskExceptionDecisionReadStoreV1
    {
        internal SalesRiskExceptionDecisionSourceV1? RiskDecisionSourceValue;
        public async Task<SalesRiskExceptionDecisionSourceV1?> ReadRiskExceptionDecisionSourceAsync(Guid id,Func<CancellationToken,Task> fence,CancellationToken ct=default)
        {await fence(ct);await fence(ct);return RiskDecisionSourceValue;}
        public Task<SalesRiskExceptionOriginalDecisionReceiptV1?> ReadRiskExceptionDecisionReceiptAsync(SalesDraftWriteEnvelope e,string hash,string command,SalesRiskExceptionCommandResult response,Func<CancellationToken,Task> fence,CancellationToken ct)=>Task.FromResult<SalesRiskExceptionOriginalDecisionReceiptV1?>(null);
        public Task<SalesRiskExceptionOriginalVersionReadResultV1> ReadRiskExceptionOriginalVersionAsync(Guid id,long expected,string actor,Func<CancellationToken,Task> fence,CancellationToken ct)=>Task.FromResult(new SalesRiskExceptionOriginalVersionReadResultV1(SalesRiskExceptionDecisionReadStateV1.NotFound,null){CurrentVersion=RiskDecisionSourceValue?.Recorded.Version});
    }
}
