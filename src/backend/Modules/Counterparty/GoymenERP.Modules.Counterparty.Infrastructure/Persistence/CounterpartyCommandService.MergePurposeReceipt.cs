using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed partial class CounterpartyCommandService
{
    private async Task<bool> MergePurposeReceiptAsync(Guid survivorId, ApproveCounterpartyPartyMerge request,
        CounterpartyCommandMetadata metadata, CancellationToken token)
    {
        var state = mergePurpose ?? throw new InvalidOperationException("Birleştirme purpose sınırı yok.");
        if (mergePurposeAudit is null) return false;
        var command = await FindCommandExecutionAsync(ApprovePartyMergeCommandName, state.KeyHash, token);
        await state.Fence(token);
        if (command is null || command.CompanyId != metadata.CompanyId || command.ActorId != metadata.ActorId
            || command.RequestHash != state.RequestHash || command.HttpStatusCode != 204
            || command.Outcome != nameof(CounterpartyCommandResult.Succeeded) || command.SchemaVersion != CommandExecutionSchemaVersion) return false;
        try
        {
            if (!JsonNode.DeepEquals(JsonNode.Parse(command.ResponseJson), JsonSerializer.SerializeToNode(Responses.Succeeded()))) return false;
        }
        catch (JsonException) { return false; }
        var records = await dbContext.PartyMerges.AsNoTracking().Where(x => x.ApprovalCompanyId == metadata.CompanyId
            && x.MergedPartyId == request.MergedPartyId).Take(2).ToListAsync(token);
        await state.Fence(token);
        var shape = MergeShape(survivorId, request);
        if (records.Count != 1 || shape.Merged == long.MaxValue) return false;
        var merge = records[0];
        if (merge.SurvivorPartyId != survivorId || merge.SurvivorPartyVersion != shape.Survivor
            || merge.MergedPartyVersion != shape.Merged + 1 || merge.Reason != shape.Reason
            || merge.ApprovedBy != command.ActorId || merge.CorrelationId != command.OriginalCorrelationId) return false;
        var artifact = await mergePurposeAudit.ReadAsync(new(metadata.CompanyId, request.MergedPartyId, metadata.ActorId,
            command.ActorId, command.OriginalCorrelationId, state.KeyHash), state.Fence,
            dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
        await state.Fence(token);
        if (artifact is null || artifact.VersionBefore != shape.Merged || artifact.VersionAfter != merge.MergedPartyVersion) return false;
        try
        {
            var json = JsonNode.Parse(artifact.AfterJson) as JsonObject;
            if (json is null || json["SeenImpactDigest"]?.GetValue<string>() != state.SeenImpactDigest
                || json["SeenImpact"] is not { } seen) return false;
            var impact = seen.Deserialize<CounterpartyPartyMergeImpactPreviewV1>(new JsonSerializerOptions
                { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
            if (impact is null || impact.CompanyId != metadata.CompanyId || impact.Survivor is null || impact.Merged is null
                || impact.Changes is null || impact.HoldingAccounts is null || impact.HoldingAccounts.Count == 0
                || impact.Survivor.PartyId != survivorId || impact.Merged.PartyId != request.MergedPartyId
                || impact.Survivor.Version != request.ExpectedVersion || impact.Merged.Version != request.MergedPartyExpectedVersion
                || impact.Survivor.MergedIntoPartyId is not null || impact.Merged.MergedIntoPartyId is not null
                || impact.Changes.PartyId != request.MergedPartyId || impact.Changes.MergedIntoPartyId != survivorId || impact.Changes.IsActive) return false;
            var accounts = impact.HoldingAccounts;
            if (accounts.Any(x => x is null || x.CompanyId == Guid.Empty || x.AccountId == Guid.Empty
                    || (x.PartyId != survivorId && x.PartyId != request.MergedPartyId)
                    || !long.TryParse(x.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                    || version < 1 || version.ToString(CultureInfo.InvariantCulture) != x.Version)
                || accounts.Select(x => x.AccountId).Distinct().Count() != accounts.Count
                || !accounts.SequenceEqual(accounts.OrderBy(x => x.CompanyId).ThenBy(x => x.PartyId).ThenBy(x => x.AccountId))
                || !accounts.Any(x => x.CompanyId == metadata.CompanyId && x.PartyId == survivorId)
                || !accounts.Any(x => x.PartyId == request.MergedPartyId) || MergeImpactDigest(impact) != state.SeenImpactDigest) return false;
            // The original Audit artifact, not today's account membership or party version, supplies this retained snapshot.
            var actual = JsonSerializer.SerializeToNode(new
            {
                PartyId = merge.MergedPartyId, merge.SurvivorPartyId, MergedIntoPartyId = (Guid?)merge.SurvivorPartyId,
                IsActive = false, Version = merge.MergedPartyVersion, MergeId = merge.Id, merge.SurvivorPartyVersion,
                NotifiedCompanyIds = accounts.Select(x => x.CompanyId).Distinct().Order().ToArray(),
            })!.AsObject();
            actual["SeenImpactDigest"] = state.SeenImpactDigest; actual["SeenImpact"] = JsonSerializer.SerializeToNode(impact);
            return JsonNode.DeepEquals(json, actual);
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
