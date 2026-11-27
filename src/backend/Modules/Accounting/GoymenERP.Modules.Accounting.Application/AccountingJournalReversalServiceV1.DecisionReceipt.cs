using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingJournalReversalServiceV1
{
    public static string JournalReversalApprovalKeyHash(string key) => AccountingApiService.Sha256(key.Trim());
    public static string JournalReversalApprovalPayloadHash(Guid proposalId, int expectedVersion) => AccountingApiService.Sha256(
        AccountingJournalReversalCommandNamesV1.Approve + "|" + JsonSerializer.Serialize(new { proposalId, expectedVersion }, Json));
    public static string JournalReversalOriginalResultDigest(AccountingApiResult<AccountingJournalReversalProposalViewV1> answer) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(answer with { IsReplay = false, OriginalCorrelationId = null }, Json));

    private async Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> AttachJournalReversalReceiptAsync(
        AccountingApiResult<AccountingJournalReversalProposalViewV1> answer, Guid id, int expected, string key,
        Guid company, Guid actor, string actorId, CancellationToken token)
    {
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is null || proposals is not IAccountingJournalReversalDecisionStoreV1 reader) return answer;
        await ReceiptFence(token);
        var command = await commands.FindCommandAsync(AccountingJournalReversalCommandNamesV1.Approve, JournalReversalApprovalKeyHash(key), token);
        await ReceiptFence(token);
        var source = await reader.ReadJournalReversalDecisionAsync(id, ReceiptFence, token);
        await ReceiptFence(token);
        var receipt = source is null || !ValidJournalReversalSource(source, company, id) ? null
            : BindJournalReversalApprovalReceipt(command, source, answer, expected, key, actor);
        return answer with { Resource = answer.Resource with { ApprovalReceipt = receipt } };
        Task ReceiptFence(CancellationToken cancellationToken) =>
            FenceAsync(AccountingJournalReversalPermissionNamesV1.Approve, company, actor, actorId, cancellationToken);
    }

    public static AccountingJournalReversalApprovalReceiptV1? BindJournalReversalApprovalReceipt(AccountingCommandRecord? command,
        AccountingJournalReversalDecisionSourceV1 source, AccountingApiResult<AccountingJournalReversalProposalViewV1> answer,
        int expectedVersion, string key, Guid actor)
    {
        var proposal = source.Proposal;
        if (expectedVersion < 1 || expectedVersion == int.MaxValue || string.IsNullOrWhiteSpace(key) || actor == Guid.Empty
            || proposal.ProposedBy == actor || !ValidJournalReversalSource(source, proposal.CompanyId, proposal.ProposalId)
            || !source.ReversalJournalBound || command is null || command.CommandName != AccountingJournalReversalCommandNamesV1.Approve
            || command.IdempotencyKeyHash != JournalReversalApprovalKeyHash(key)
            || command.RequestHash != JournalReversalApprovalPayloadHash(proposal.ProposalId, expectedVersion)
            || !Guid.TryParse(command.ActorId, out var commandActor) || commandActor != actor
            || string.IsNullOrWhiteSpace(command.OriginalCorrelationId) || command.OriginalCorrelationId != source.ReversalCorrelationId) return null;
        AccountingApiResult<AccountingJournalReversalProposalViewV1>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<AccountingApiResult<AccountingJournalReversalProposalViewV1>>(command.ResultJson, Json);
            if (!JsonNode.DeepEquals(JsonNode.Parse(command.ResultJson), JsonSerializer.SerializeToNode(stored, Json))) return null;
        }
        catch (JsonException) { return null; }
        if (stored is not { Status: AccountingApiStatus.Ok, Resource: { } original, IsReplay: false, Code: null, Field: null,
                ExpectedVersion: null, CurrentVersion: null, OriginalCorrelationId: null }
            || answer is not { Status: AccountingApiStatus.Ok, Resource: not null, Code: null, Field: null, ExpectedVersion: null, CurrentVersion: null }
            || (answer.IsReplay ? answer.OriginalCorrelationId != command.OriginalCorrelationId : answer.OriginalCorrelationId is not null)
            || original.Status != "Approved" || original.Version != expectedVersion + 1 || original.DecidedBy != actor
            || original.DecidedAtUtc is not { } approved || approved.Offset != TimeSpan.Zero || original.ReversalJournalId is not { } reversal
            || JsonSerializer.Serialize(original, Json) != JsonSerializer.Serialize(View(proposal), Json)
            || JsonSerializer.Serialize(original, Json) != JsonSerializer.Serialize(answer.Resource, Json)) return null;
        return new(proposal.CompanyId, proposal.ProposalId, proposal.SourceJournalId, reversal, proposal.PeriodId,
            command.CommandName, command.IdempotencyKeyHash, command.RequestHash, actor, expectedVersion, original.Version,
            command.OriginalCorrelationId, approved, JournalReversalSourceFactsDigest(original), AccountingApiService.Sha256(command.ResultJson))
        { CancellationSourceActorId = source.CancellationSourceActorId, CancellationSourceActorProofEstablished = true };
    }
}
