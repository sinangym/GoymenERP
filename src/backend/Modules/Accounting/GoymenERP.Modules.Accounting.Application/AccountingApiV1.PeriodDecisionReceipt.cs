using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingApiService
{
    public static string PeriodApprovalKeyHash(string key) => Sha256(key.Trim());
    public static string PeriodOpeningApprovalPayloadHash(Guid periodId, long expectedVersion) =>
        Sha256(AccountingCommandNames.ApprovePeriod + "|" + JsonSerializer.Serialize(new { periodId, expectedVersion }, Json));
    public static string PeriodReopeningApprovalPayloadHash(Guid periodId, Guid proposalId, ApproveAccountingPeriodReopeningV1 request) =>
        Sha256("accounting.period.reopening.approve.v1|" + JsonSerializer.Serialize(new { periodId, proposalId, request }, Json));

    private async Task<AccountingApiResult<T>> AttachPeriodApprovalReceiptAsync<T>(AccountingApiResult<T> answer,
        string commandName, object canonicalRequest, string key, AccessScope scope, string permission, CancellationToken token) where T : class
    {
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is null || periods is not IAccountingPeriodDecisionStoreV1 reader) return answer;
        var canonical = JsonSerializer.SerializeToElement(canonicalRequest, Json);
        if (!canonical.TryGetProperty("periodId", out var periodValue) || !periodValue.TryGetGuid(out var periodId)) return answer;
        await ReceiptFence(token);
        var command = await commands.FindCommandAsync(commandName, PeriodApprovalKeyHash(key), token);
        await ReceiptFence(token);
        if (answer is AccountingApiResult<PeriodViewV1> opening && commandName == AccountingCommandNames.ApprovePeriod
            && canonical.TryGetProperty("expectedVersion", out var version) && version.TryGetInt64(out var expected))
        {
            var source = await reader.ReadOpeningDecisionAsync(periodId, ReceiptFence, token); await ReceiptFence(token);
            var proof = source is null || !ValidPeriodSource(source, scope.CompanyId, periodId) ? null : BindPeriodOpeningApprovalReceipt(command, source, opening, expected, key, scope.ActorGuid);
            return (AccountingApiResult<T>)(object)(opening with { Resource = opening.Resource! with { ApprovalReceipt = proof } });
        }
        if (answer is AccountingApiResult<AccountingPeriodReopeningViewV1> reopening && commandName == "accounting.period.reopening.approve.v1"
            && canonical.TryGetProperty("proposalId", out var proposalValue) && proposalValue.TryGetGuid(out var proposalId)
            && canonical.TryGetProperty("request", out var requestValue))
        {
            var request = requestValue.Deserialize<ApproveAccountingPeriodReopeningV1>(Json);
            if (request is null) return answer;
            var source = await reader.ReadReopeningDecisionAsync(proposalId, ReceiptFence, token); await ReceiptFence(token);
            var proof = source is null || !ValidReopeningSource(source, scope.CompanyId, proposalId) || source.Reopening.PeriodId != periodId ? null
                : BindPeriodReopeningApprovalReceipt(command, source, reopening, request, key, scope.ActorGuid);
            return (AccountingApiResult<T>)(object)(reopening with { Resource = reopening.Resource! with { ApprovalReceipt = proof } });
        }
        return answer;
        async Task ReceiptFence(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await RequireStillAsync(scope, permission, cancellationToken); }
            catch (FreshAuthorityDeniedException) { throw new NormalJournalAuthorityDeniedException(); }
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public static AccountingPeriodApprovalReceiptV1? BindPeriodOpeningApprovalReceipt(AccountingCommandRecord? command,
        AccountingPeriodRecord source, AccountingApiResult<PeriodViewV1> answer, long expectedVersion, string key, Guid actor)
    {
        if (!ValidPeriodSource(source, source.Period.CompanyId, source.Period.Id) || expectedVersion < 1 || expectedVersion == long.MaxValue
            || actor == Guid.Empty || source.Period.PreparedByUserId == actor || string.IsNullOrWhiteSpace(key)
            || !ValidPeriodCommand(command, AccountingCommandNames.ApprovePeriod, PeriodOpeningApprovalPayloadHash(source.Period.Id, expectedVersion), key, actor)) return null;
        var stored = ExactPeriodAnswer<PeriodViewV1>(command!);
        if (!ValidPeriodAnswer(stored, answer, command!) || stored!.Resource is not { } original
            || original.PeriodId != source.Period.Id || original.CompanyId != source.Period.CompanyId
            || original.Version != expectedVersion + 1 || source.Version < original.Version || original.Status != "Open"
            || original.IsOwnProposal || original.ClosedByUserId is not null || original.ClosedAtUtc is not null
            || original.ApprovedByUserId != actor || original.ApprovedAtUtc is not { } approved
            || original.ApprovedByUserId != source.Period.ApprovedByUserId || original.ApprovedAtUtc != source.Period.ApprovedAtUtc
            || PeriodOpeningSourceFactsDigest(original) != PeriodOpeningSourceFactsDigest(PeriodView(source, actor))) return null;
        return new(source.Period.CompanyId, source.Period.Id, null, command!.CommandName, command.IdempotencyKeyHash,
            command.RequestHash, actor, expectedVersion, null, original.Version, command.OriginalCorrelationId, approved,
            PeriodOpeningSourceFactsDigest(original), Sha256(command.ResultJson));
    }

    public static AccountingPeriodApprovalReceiptV1? BindPeriodReopeningApprovalReceipt(AccountingCommandRecord? command,
        AccountingPeriodReopeningDecisionSourceV1 source, AccountingApiResult<AccountingPeriodReopeningViewV1> answer,
        ApproveAccountingPeriodReopeningV1 request, string key, Guid actor)
    {
        var proposal = source.Reopening;
        if (!ValidReopeningSource(source, proposal.CompanyId, proposal.Id) || request.ExpectedVersion < 1 || request.ExpectedVersion == long.MaxValue
            || request.ExpectedPeriodVersion < 1 || request.ExpectedPeriodVersion == long.MaxValue || actor == Guid.Empty
            || proposal.ProposedBy == actor || string.IsNullOrWhiteSpace(key)
            || request.ExpectedPeriodVersion != proposal.ClosedPeriodVersion
            || !ValidPeriodCommand(command, "accounting.period.reopening.approve.v1",
                PeriodReopeningApprovalPayloadHash(proposal.PeriodId, proposal.Id, request), key, actor)) return null;
        var stored = ExactPeriodAnswer<AccountingPeriodReopeningViewV1>(command!);
        if (!ValidPeriodAnswer(stored, answer, command!) || stored!.Resource is not { } original
            || original.Version != request.ExpectedVersion + 1 || original.ApprovedBy != actor || original.ApprovedAtUtc is not { } approved
            || JsonSerializer.Serialize(original, Json) != JsonSerializer.Serialize(proposal, Json)
            || source.Period.Version < request.ExpectedPeriodVersion + 1
            || source.Closure.ReopeningId != proposal.Id || source.Closure.ReopenedBy != actor || source.Closure.ReopenedAtUtc != approved) return null;
        return new(proposal.CompanyId, proposal.PeriodId, proposal.Id, command!.CommandName, command.IdempotencyKeyHash,
            command.RequestHash, actor, request.ExpectedVersion, request.ExpectedPeriodVersion, original.Version,
            command.OriginalCorrelationId, approved, PeriodReopeningSourceFactsDigest(source), Sha256(command.ResultJson));
    }
    private static bool ValidPeriodCommand(AccountingCommandRecord? command, string name, string payload, string key, Guid actor) =>
        command is not null && command.CommandName == name && command.IdempotencyKeyHash == PeriodApprovalKeyHash(key)
        && command.RequestHash == payload && Guid.TryParse(command.ActorId, out var originalActor) && originalActor == actor
        && !string.IsNullOrWhiteSpace(command.OriginalCorrelationId);
    private static AccountingApiResult<T>? ExactPeriodAnswer<T>(AccountingCommandRecord command) where T : class
    {
        try
        {
            var stored = JsonSerializer.Deserialize<AccountingApiResult<T>>(command.ResultJson, Json);
            return JsonNode.DeepEquals(JsonNode.Parse(command.ResultJson), JsonSerializer.SerializeToNode(stored, Json)) ? stored : null;
        }
        catch (JsonException) { return null; }
    }
    private static bool ValidPeriodAnswer<T>(AccountingApiResult<T>? stored, AccountingApiResult<T> answer, AccountingCommandRecord command) where T : class =>
        stored is { Status: AccountingApiStatus.Ok, Resource: not null, IsReplay: false, Code: null, Field: null,
            ExpectedVersion: null, CurrentVersion: null, OriginalCorrelationId: null }
        && answer is { Status: AccountingApiStatus.Ok, Resource: not null, Code: null, Field: null, ExpectedVersion: null, CurrentVersion: null }
        && (answer.IsReplay ? answer.OriginalCorrelationId == command.OriginalCorrelationId : answer.OriginalCorrelationId is null)
        && JsonSerializer.Serialize(stored.Resource, Json) == JsonSerializer.Serialize(answer.Resource, Json);
}
