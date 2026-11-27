using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingPostingAccountMappingServiceV1
{
    public static string ActivationKeyHash(string key) => AccountingApiService.Sha256(key.Trim());
    public static string ActivationPayloadHash(long mappingVersion, long expectedVersion, long expectedActiveMappingVersion) =>
        AccountingApiService.Sha256(AccountingPostingAccountMappingCommandNamesV1.Activate + "|" + JsonSerializer.Serialize(new
        { mappingVersion, ExpectedVersion = expectedVersion, ExpectedActiveMappingVersion = expectedActiveMappingVersion }, Json));
    public static string ActivationOriginalResultDigest(AccountingApiResult<AccountingPostingAccountMappingViewV1> answer) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(answer with { IsReplay = false, OriginalCorrelationId = null }, Json));

    private async Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> AttachMappingActivationReceiptAsync(
        AccountingApiResult<AccountingPostingAccountMappingViewV1> answer, long mappingVersion,
        ActivateAccountingPostingAccountMappingRequestV1 request, string key, Guid company, Guid actor, CancellationToken token)
    {
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is null || mappings is not IAccountingPostingAccountMappingDecisionStoreV1 reader) return answer;
        try
        {
            await ReceiptFence(token);
            var command = await commands.FindCommandAsync(AccountingPostingAccountMappingCommandNamesV1.Activate, ActivationKeyHash(key), token);
            await ReceiptFence(token);
            // Historical original-key proof never reads or compares the CURRENT active plan.
            var source = await reader.ReadActivationReceiptSourceAsync(mappingVersion, ReceiptFence, token);
            if (source?.Rows is not null) source = FreezeMappingRecord(source);
            await ReceiptFence(token);
            var proof = source is null || source.CompanyId != company || source.MappingVersion != mappingVersion ? null
                : BindMappingActivationReceipt(command, source, answer, request, key, actor);
            return answer with { Resource = answer.Resource with { ActivationReceipt = proof } };
        }
        catch (AccountingStoreException error) when (error.Code == AccountingStoreCodes.ProviderUnavailable)
        { return new(AccountingApiStatus.Unavailable, null, false, null); }
        Task ReceiptFence(CancellationToken cancellationToken) => ActivationFenceAsync(company, actor, cancellationToken);
    }

    public static AccountingPostingAccountMappingActivationReceiptV1? BindMappingActivationReceipt(AccountingCommandRecord? command,
        AccountingPostingAccountMappingRecordV1 source, AccountingApiResult<AccountingPostingAccountMappingViewV1> answer,
        ActivateAccountingPostingAccountMappingRequestV1 request, string key, Guid actor)
    {
        if (command is null || request is null || string.IsNullOrWhiteSpace(command.ResultJson) || !ValidMappingDecisionRecord(source)
            || request.ExpectedVersion <= 0 || request.ExpectedVersion == long.MaxValue || request.ExpectedActiveMappingVersion < 0
            || actor == Guid.Empty || actor == source.PreparedByUserId || string.IsNullOrWhiteSpace(key)
            || command.CommandName != AccountingPostingAccountMappingCommandNamesV1.Activate || command.IdempotencyKeyHash != ActivationKeyHash(key)
            || command.ActorId != actor.ToString("D") || string.IsNullOrWhiteSpace(command.OriginalCorrelationId)
            || command.RequestHash != ActivationPayloadHash(source.MappingVersion, request.ExpectedVersion, request.ExpectedActiveMappingVersion)) return null;
        AccountingApiResult<AccountingPostingAccountMappingViewV1>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<AccountingApiResult<AccountingPostingAccountMappingViewV1>>(command.ResultJson, Json);
            if (!JsonNode.DeepEquals(JsonNode.Parse(command.ResultJson), JsonSerializer.SerializeToNode(stored, Json))) return null;
        }
        catch (JsonException) { return null; }
        catch (ArgumentException) { return null; }
        if (stored is not { Status: AccountingApiStatus.Ok, IsReplay: false, Code: null, Resource: { } original }
            || stored.OriginalCorrelationId is not null || stored.Field is not null || stored.ExpectedVersion is not null || stored.CurrentVersion is not null
            || answer is null || answer.Status != AccountingApiStatus.Ok || answer.Code is not null || answer.Field is not null || answer.ExpectedVersion is not null || answer.CurrentVersion is not null
            || answer.Resource is not { } actual || !answer.IsReplay && answer.OriginalCorrelationId is not null
            || answer.IsReplay && answer.OriginalCorrelationId != command.OriginalCorrelationId
            || source.Status == AccountingPostingAccountMappingStatusV1.Prepared || original.Status != "Active"
            || original.CompanyId != source.CompanyId || original.MappingVersion != source.MappingVersion
            || original.Version != request.ExpectedVersion + 1 || source.Version < original.Version
            || original.ActivatedByUserId != actor || source.ActivatedByUserId != actor || original.ActivatedAtUtc is not { } at
            || at == default || at.Offset != TimeSpan.Zero || source.ActivatedAtUtc != at
            || JsonSerializer.Serialize(original, Json) != JsonSerializer.Serialize(actual, Json)) return null;
        // Reconstruct only the terminal envelope from its original receipt facts; all prepared facts and rows come from retained storage.
        var retainedOriginal = View(source with { Status = AccountingPostingAccountMappingStatusV1.Active, Version = original.Version });
        if (JsonSerializer.Serialize(original, Json) != JsonSerializer.Serialize(retainedOriginal, Json)) return null;
        return new(source.CompanyId, source.MappingVersion, command.CommandName, command.IdempotencyKeyHash, command.RequestHash, actor,
            request.ExpectedVersion, request.ExpectedActiveMappingVersion, original.Version, command.OriginalCorrelationId, at,
            MappingSourceFactsDigest(source), AccountingApiService.Sha256(command.ResultJson));
    }
}
