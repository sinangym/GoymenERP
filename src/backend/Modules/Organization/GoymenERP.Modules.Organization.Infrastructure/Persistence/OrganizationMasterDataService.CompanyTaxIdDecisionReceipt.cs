using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

internal sealed partial class OrganizationMasterDataService
{
    internal async Task<OrganizationCompanyTaxIdDecisionReceiptV1?> ReadTaxIdDecisionReceiptAsync(Guid id,
        DecideCompanyTaxIdChange command, string key, CommandExecutionResult original,
        Func<CancellationToken, Task> consumer, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(consumer); ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(original);
        var companyId = companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
        var actor = currentUser.UserId;
        await TaxIdPurposeFence(companyId, actor, consumer, token);
        if (id == Guid.Empty || command.ExpectedVersion < 1 || !OrganizationCompanyTaxIdRules.IsDecision(command.Decision)
            || !OrganizationCompanyTaxIdRules.IsValidNote(command.Note) || string.IsNullOrWhiteSpace(key) || key != key.Trim()
            || original.Response is null || original.Result != CommandResult.Succeeded || original.Response != CommandResponses.For(CommandResult.Succeeded)
            || string.IsNullOrWhiteSpace(original.OriginalCorrelationId)) return null;
        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        // Reuse the exact existing native fingerprint; neither current company version nor VKN participates.
        var metadata = new CommandMetadata(companyId, actor!, original.OriginalCorrelationId, null, "Web", null, null, key);
        var request = CreateRequest(DecideCompanyTaxIdChangeCommandName, metadata,
            new DecideCompanyTaxIdChangeFingerprint(companyId, id, command.Decision, command.ExpectedVersion, note));
        var execution = await dbContext.CommandExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId
            && x.CommandName == request.CommandName && x.IdempotencyKeyHash == request.IdempotencyKeyHash, token);
        await TaxIdPurposeFence(companyId, actor, consumer, token);
        if (execution is null || execution.SchemaVersion != CommandExecutionSchemaVersion || execution.ActorId != actor || execution.RequestHash != request.RequestHash
            || execution.Outcome != CommandResult.Succeeded.ToString() || execution.HttpStatusCode != 204
            || execution.OriginalCorrelationId != original.OriginalCorrelationId) return null;
        try
        {
            if (!JsonNode.DeepEquals(JsonNode.Parse(execution.ResponseJson), JsonSerializer.SerializeToNode(original.Response))) return null;
        }
        catch (JsonException) { return null; }
        catch (ArgumentException) { return null; } // Duplicate JSON property names cannot be certified either.
        var row = await dbContext.CompanyTaxIdChangeRequests.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, token);
        await TaxIdPurposeFence(companyId, actor, consumer, token);
        var approve = command.Decision == OrganizationCompanyTaxIdRules.ApproveDecision;
        if (row is null || !ValidTaxIdPurposeRow(row) || row.IsPending || row.DecidedBy != actor || row.DecisionNote != note
            || row.Status != (approve ? CompanyTaxIdChangeStatus.Approved : CompanyTaxIdChangeStatus.Rejected)
            || approve && row.CompanyVersionAtRequest != command.ExpectedVersion) return null;
        return new(companyId, id, row.CompanyVersionAtRequest, command.ExpectedVersion, row.Version, command.Decision, note,
            row.RequestedBy, row.RequestedAtUtc, row.DecidedBy!, row.DecidedAtUtc!.Value, request.CommandName,
            request.IdempotencyKeyHash, request.RequestHash, execution.ActorId, execution.OriginalCorrelationId,
            execution.CompletedAtUtc, TaxIdSourceDigest(row), TaxIdPurposeDigest(original.Response));
    }
}
