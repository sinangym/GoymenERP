using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class ApprovedAccountingSourceCatalogServiceV1
{
    public static string ActivationKeyHash(string key) => AccountingApiService.Sha256(key.Trim());
    public static string ActivationPayloadHash(Guid packageId, long expectedPackageVersion, long expectedActiveCatalogVersion) =>
        AccountingApiService.Sha256(ApprovedAccountingSourceCatalogCommandNamesV1.Activate + "|" + JsonSerializer.Serialize(new
        { packageId, ExpectedPackageVersion = expectedPackageVersion, ExpectedActiveCatalogVersion = expectedActiveCatalogVersion }, Json));
    public static string ActivationOriginalResultDigest(AccountingApiResult<ApprovedAccountingSourceCatalogViewV1> answer) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(answer with { IsReplay = false, OriginalCorrelationId = null }, Json));

    private async Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>> AttachCatalogActivationReceiptAsync(
        AccountingApiResult<ApprovedAccountingSourceCatalogViewV1> answer, Guid packageId,
        ActivateApprovedAccountingSourceCatalogRequestV1 request, string key, Guid company, Guid actor, CancellationToken token)
    {
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is null || catalogs is not IApprovedAccountingSourceCatalogDecisionStoreV1 reader) return answer;
        try
        {
            await ReceiptFence(token);
            var command = await commands.FindCommandAsync(ApprovedAccountingSourceCatalogCommandNamesV1.Activate, ActivationKeyHash(key), token);
            await ReceiptFence(token);
            var source = await reader.ReadActivationReceiptSourceAsync(packageId, ReceiptFence, token);
            await ReceiptFence(token);
            var proof = source is null || source.CompanyId != company || source.PackageId != packageId ? null
                : BindCatalogActivationReceipt(command, source, answer, request, key, actor);
            return answer with { Resource = answer.Resource with { ActivationReceipt = proof } };
        }
        catch (AccountingStoreException error) when (error.Code == AccountingStoreCodes.ProviderUnavailable)
        { return new(AccountingApiStatus.Unavailable, null, false, null); }
        Task ReceiptFence(CancellationToken cancellationToken) => ActivationFenceAsync(company, actor, cancellationToken);
    }

    public static ApprovedAccountingSourceCatalogActivationReceiptV1? BindCatalogActivationReceipt(AccountingCommandRecord? command,
        ApprovedAccountingSourceCatalogRecordV1 source, AccountingApiResult<ApprovedAccountingSourceCatalogViewV1> answer,
        ActivateApprovedAccountingSourceCatalogRequestV1 request, string key, Guid actor)
    {
        if (command is null || request is null || string.IsNullOrWhiteSpace(command.ResultJson) || !ValidCatalogDecisionRecord(source) || request.ExpectedPackageVersion <= 0 || request.ExpectedPackageVersion == long.MaxValue
            || request.ExpectedActiveCatalogVersion < 0 || request.ExpectedActiveCatalogVersion == long.MaxValue || actor == Guid.Empty || string.IsNullOrWhiteSpace(key)
            || command.CommandName != ApprovedAccountingSourceCatalogCommandNamesV1.Activate || command.IdempotencyKeyHash != ActivationKeyHash(key)
            || command.ActorId != actor.ToString("D") || string.IsNullOrWhiteSpace(command.OriginalCorrelationId)
            || command.RequestHash != ActivationPayloadHash(source.PackageId, request.ExpectedPackageVersion, request.ExpectedActiveCatalogVersion)) return null;
        AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>>(command.ResultJson, Json);
            if (!JsonNode.DeepEquals(JsonNode.Parse(command.ResultJson), JsonSerializer.SerializeToNode(stored, Json))) return null;
        }
        catch (JsonException) { return null; }
        catch (ArgumentException) { return null; }
        if (stored is not { Status: AccountingApiStatus.Ok, IsReplay: false, Code: null, Resource: { } original }
            || stored.OriginalCorrelationId is not null || stored.Field is not null || stored.ExpectedVersion is not null || stored.CurrentVersion is not null
            || answer.Status != AccountingApiStatus.Ok || answer.Code is not null || answer.Field is not null || answer.ExpectedVersion is not null || answer.CurrentVersion is not null
            || answer.Resource is not { } actual || !answer.IsReplay && answer.OriginalCorrelationId is not null
            || answer.IsReplay && answer.OriginalCorrelationId != command.OriginalCorrelationId
            || original.Status != "Active" || original.PackageId != source.PackageId || original.CompanyId != source.CompanyId
            || original.Version != request.ExpectedPackageVersion + 1 || source.Version < original.Version
            || original.CatalogVersion is not { } originalCatalogVersion || originalCatalogVersion != request.ExpectedActiveCatalogVersion + 1 || source.CatalogVersion != originalCatalogVersion
            || original.ActivatedByUserId != actor || source.ActivatedByUserId != actor || original.ActivatedAtUtc is not { } at
            || at == default || at.Offset != TimeSpan.Zero || source.ActivatedAtUtc != at
            || original.TemplateVersion != source.TemplateVersion || original.AccountPlanSourceVersion != source.AccountPlanSourceVersion
            || original.AccountsSourceSha256 != source.AccountsSourceSha256 || original.PostingMappingsSourceSha256 != source.PostingMappingsSourceSha256
            || original.PreparedByUserId != source.PreparedByUserId || original.PreparedAtUtc != source.PreparedAtUtc
            || original.AccountCount != source.Package.Mirror.Rows.Count || original.BindingCount != source.Package.Bindings.Count
            || JsonSerializer.Serialize(original, Json) != JsonSerializer.Serialize(actual, Json)) return null;
        return new(source.CompanyId, source.PackageId, command.CommandName, command.IdempotencyKeyHash, command.RequestHash, actor,
            request.ExpectedPackageVersion, request.ExpectedActiveCatalogVersion, original.Version, originalCatalogVersion,
            command.OriginalCorrelationId, at, CatalogSourceFactsDigest(source), AccountingApiService.Sha256(command.ResultJson));
    }
}
