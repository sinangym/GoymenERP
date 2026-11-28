using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

public sealed partial class IdentityAccessAssignmentServiceTests
{
    [Fact]
    public async Task Original_reject_receipt_is_bound_to_real_command_and_retained_decided_request_without_inventing_impact()
    {
        var (h, id) = await RejectedReceiptHarnessAsync();
        var receipt = await h.Service().ReadDecisionReceiptAsync(id, false, "1", "reason_code", h.Metadata(), _ => Task.CompletedTask);
        Assert.NotNull(receipt);
        Assert.Equal("2", receipt.ResultVersion);
        Assert.Equal(Harness.CorrelationId, receipt.OriginalCorrelationId);
        Assert.Equal(200, receipt.OriginalAnswer.StatusCode);
        Assert.Null(receipt.AppliedImpactDigest);
        Assert.Null(receipt.AppliedPermissionCount);
        Assert.Empty(h.Audits.Enqueued);
    }

    [Theory]
    [InlineData("unknown-root")]
    [InlineData("unknown-result")]
    [InlineData("wrong-actor")]
    [InlineData("wrong-key")]
    [InlineData("wrong-version")]
    [InlineData("wrong-reason")]
    [InlineData("wrong-company")]
    public async Task Altered_original_command_or_selector_cannot_certify_receipt(string fault)
    {
        var (h, id) = await RejectedReceiptHarnessAsync(); var metadata = h.Metadata();
        if (fault.StartsWith("unknown", StringComparison.Ordinal))
        {
            var old = await h.Database.CommandExecutions.SingleAsync();
            var json = JsonNode.Parse(old.ResponseJson)!.AsObject();
            if (fault == "unknown-root") json["approvalReceipt"] = true;
            else json["result"]!.AsObject()["unexpected"] = true;
            h.Database.CommandExecutions.Remove(old); await h.Database.SaveChangesAsync();
            h.Database.CommandExecutions.Add(CommandExecution.Complete(new(Guid.NewGuid(), old.CompanyId, old.CommandName,
                old.IdempotencyKeyHash, old.RequestHash, old.ActorId, old.Outcome, old.HttpStatusCode, json.ToJsonString(),
                old.OriginalCorrelationId, old.CompletedAtUtc, old.SchemaVersion)));
            await h.Database.SaveChangesAsync(); h.Database.ChangeTracker.Clear();
        }
        if (fault == "wrong-actor") metadata = metadata with { ActorId = Guid.NewGuid().ToString("D") };
        if (fault == "wrong-key") metadata = metadata with { IdempotencyKey = "SENTETIK-other-key" };
        if (fault == "wrong-company") metadata = metadata with { CompanyId = CompanyB };
        var read = () => h.Service().ReadDecisionReceiptAsync(id, false, fault == "wrong-version" ? "2" : "1",
            fault == "wrong-reason" ? "other_code" : "reason_code", metadata, _ => Task.CompletedTask);
        if (fault is "wrong-actor" or "wrong-company") await Assert.ThrowsAsync<UnauthorizedAccessException>(read);
        else Assert.Null(await read());
        Assert.Empty(h.Audits.Enqueued);
    }

    private static async Task<(Harness Harness, Guid Id)> RejectedReceiptHarnessAsync()
    {
        var h = new Harness(); var seed = h.SeedQueue();
        var row = await h.Database.AccessGrantRequests.SingleAsync(x => x.Id == seed.Older);
        row.Reject(Guid.Parse(h.Metadata().ActorId), "reason_code", Now.AddMinutes(-5));
        await h.Database.SaveChangesAsync(); h.Database.ChangeTracker.Clear();
        h.StoreExecution(IdentityAccessCommandsV1.Reject, row.Id.ToString("D"), new RejectAccessGrantRequestV1("1", "reason_code"),
            new(200, null, "Kaydedildi.", "Değişiklik kaydedildi.", new(row.Id, "2", "Rejected", null)), "Succeeded");
        return (h, row.Id);
    }
}
