using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Quotes;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Quote_original_decision_keeps_raw_hash_body_and_private_selected_target_without_old_HTTP_fabrication()
    {
        var f=await QuoteReceiptFixture();var proof=Assert.IsType<SalesQuoteOriginalDecisionReceiptV1>(RestoreQuoteReceipt(f));
        Assert.Equal((f.Command.Target,"01","2","Approved","quote-original"),(proof.Target,proof.ExpectedVersion,proof.ResultVersion,proof.OriginalState,proof.OriginalCorrelationId));
        var replay=f.Response with {IsReplay=true};
        Assert.Equal(proof,RestoreQuoteReceipt(f,replay));
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(replay with{OriginalDecisionReceipt=proof},SalesDraftJson.Options));
        Assert.Equal(3,json.RootElement.EnumerateObject().Count());Assert.False(json.RootElement.TryGetProperty("originalDecisionReceipt",out _));
        Assert.Null(f.Attempt.ResultJson);
        // A later current detail is separately bound; it never supplies original version/state/decision facts.
        var current=replay with {Detail=replay.Detail with {Quote=replay.Detail.Quote with {Version="9007199254740993"}}};
        var retained=Assert.IsType<SalesQuoteOriginalDecisionReceiptV1>(RestoreQuoteReceipt(f,current));
        Assert.Equal(proof.OriginalDecisionDigest,retained.OriginalDecisionDigest);Assert.Equal("2",retained.ResultVersion);
        Assert.NotEqual(proof.CurrentResponseDigest,retained.CurrentResponseDigest);
    }

    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("legacy-trace")] [InlineData("different-selected-request")]
    [InlineData("request-version")] [InlineData("request-number")] [InlineData("request-revision")]
    [InlineData("actor")] [InlineData("company")] [InlineData("key")] [InlineData("hash")] [InlineData("correlation")]
    [InlineData("permission")] [InlineData("timestamp")] [InlineData("before-version")] [InlineData("malformed")]
    [InlineData("result-json")] [InlineData("trace-extra")] [InlineData("note-body")]
    public async Task Quote_original_decision_requires_exact_selected_trace_attempt_audit_and_actual_raw_native_body(string fault)
    {
        var f=await QuoteReceiptFixture();var audit=f.Audit;var command=f.Command;
        if(fault=="legacy-trace")audit=audit with {ApprovalContextJson=null};
        if(fault=="different-selected-request")command=command with {Target=command.Target with {RequestId=Guid.NewGuid()}};
        if(fault=="request-version")command=command with {Target=command.Target with {RequestVersion="2"}};
        if(fault=="request-number")command=command with {Target=command.Target with {RequestNo=2}};
        if(fault=="request-revision")command=command with {Target=command.Target with {SubjectRevision=1}};
        if(fault=="actor")f.Attempt.ActorId="SENTETIK-other";
        if(fault=="company")f.Attempt.CompanyId=Guid.NewGuid();
        if(fault=="key")f.Attempt.IdempotencyKeyHash=new('a',64);
        if(fault=="hash")f.Attempt.RequestHash=new('b',64);
        if(fault=="correlation")f.Attempt.CorrelationId="other";
        if(fault=="permission")audit=audit with {PermissionCode=SalesPermissionManifestV1.QuotesRead};
        if(fault=="timestamp")f.Attempt.RecordedAtUtc=f.Attempt.RecordedAtUtc.AddTicks(1);
        if(fault=="before-version")audit=audit with {EntityVersionBefore=2};
        if(fault=="malformed")audit=audit with {AfterJson="{}"};
        if(fault=="result-json")f.Attempt.ResultJson="{}";
        if(fault=="trace-extra")audit=audit with {ApprovalContextJson=audit.ApprovalContextJson![..^1]+",\"extra\":1}"};
        if(fault=="note-body")command=command with {NoteOrReason="changed"};
        var stored=AuditEvent.Create(new(audit,AuditEventIntegrity.Compute(audit)));
        IReadOnlyList<AuditEvent> candidates=fault=="missing"?[]:fault=="duplicate"?[stored,stored]:[stored];
        Assert.Null(SalesStore.RestoreQuoteDecisionReceipt(command,f.Envelope,f.Hash,f.Attempt,null,candidates,f.Response));
    }

    private static SalesQuoteOriginalDecisionReceiptV1? RestoreQuoteReceipt(QuoteOriginalReceiptFixture f,SalesQuoteResponse? response=null)=>
        SalesStore.RestoreQuoteDecisionReceipt(f.Command,f.Envelope,f.Hash,f.Attempt,null,
            [AuditEvent.Create(new(f.Audit,AuditEventIntegrity.Compute(f.Audit)))],response??f.Response);
    private static async Task<QuoteOriginalReceiptFixture> QuoteReceiptFixture()
    {
        var (world,source)=await QuoteHumanSource("Pending");var before=QuoteReceiptAuditState(source.Quote);
        var target=new SalesQuoteConversionTargetV1(source.Request.Id,source.Quote.Id,1,0,"1");
        var body=new SalesQuoteExpiryApprovalRequest("01","  SENTETIK note  ");
        var response=await world.Service().ApproveExpiredQuoteConversionAsync(source.Quote.Id,body,Metadata("quote-receipt",correlation:"quote-original"));
        var hash=SalesQuoteHash.Compute(target.QuoteId,body);var key=SalesStore.IdempotencyKeyHash("quote-receipt");
        var envelope=new SalesDraftWriteEnvelope(Actor,SalesPermissionManifestV1.QuotesExpiredConversionApprove,[],"quote-original",null,"quote-receipt","Web");
        var attempt=new QuoteAttemptRow{CompanyId=Company,QuoteId=target.QuoteId,CommandName=SalesQuoteCommands.ApproveExpiredConversionV1,
            IdempotencyKeyHash=key,RequestHash=hash,ActorId=Actor,Outcome=1,CorrelationId="quote-original",RecordedAtUtc=source.Quote.ExpiryApprovals[^1].ApprovedAtUtc};
        var audit=new AuditEventEnvelope(Guid.NewGuid(),Company,attempt.RecordedAtUtc,"sales.quote.expired-conversion-approved.v1","Sales.Quote",
            target.QuoteId.ToString("D"),"ApproveExpiredConversion",1,2,AuditActorTypes.User,Actor,SalesPermissionManifestV1.QuotesExpiredConversionApprove,
            "[]",before,QuoteReceiptAuditState(source.Quote),"SENTETIK karar",JsonSerializer.Serialize(target,SalesDraftJson.Options),
            AuditAiInvolvement.None,null,"Web",null,null,"quote-original",null,key,AuditOutcomes.Succeeded,1);
        return new(new(target,body.ExpectedVersion,body.Note),response,attempt,envelope,hash,audit);
    }
    private static string QuoteReceiptAuditState(SalesQuote quote)=>JsonSerializer.Serialize(new
    {quote.Id,quote.DocumentNo,state=quote.State.ToString(),revision=quote.Revision,version=quote.Version,quote.Header.PartyId,quote.Header.CompanyAccountId,
        quote.Header.CurrencyId,quote.Header.BusinessDate,requests=quote.RequestIds,terms=quote.Terms,loss=quote.Loss,conversion=quote.Conversion,
        expiryApprovals=quote.ExpiryApprovals,lines=(object?)null},SalesDraftJson.Options);
    private sealed record QuoteOriginalReceiptFixture(SalesQuoteDecisionCommandV1 Command,SalesQuoteResponse Response,QuoteAttemptRow Attempt,
        SalesDraftWriteEnvelope Envelope,string Hash,AuditEventEnvelope Audit);
}
