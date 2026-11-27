using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidCounterpartyIdentityProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind is not (CounterpartyTaxIdentity or CounterpartyLegalName))
            return snapshot.CounterpartyTaxIdentityChange is null && snapshot.CounterpartyLegalNameChange is null;
        var parties = snapshot.Item.References.Where(reference => reference.Role == "party").ToArray();
        if (parties.Length != 1 || parties[0].RecordId is not { } partyId || partyId == Guid.Empty) return false;
        static bool Reason(string? value, int maximumLength) => !string.IsNullOrWhiteSpace(value) && value.Trim() == value
            && value.Length <= maximumLength;
        static bool Name(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim() == value
            && value.Length <= Party.LegalNameMaximumLength;
        if (kind == CounterpartyTaxIdentity)
        {
            return snapshot.CounterpartyLegalNameChange is null && snapshot.CounterpartyTaxIdentityChange is { } tax
                && tax.PartyId == partyId && tax.ChangeRequestId == snapshot.Item.ItemId
                && Reason(tax.Reason, PartyTaxIdentityChangeRequest.ReasonMaximumLength)
                // A checksum-invalid historical current value can be the very reason for the approved correction.
                && tax.CurrentTaxIdentityNumber is { Length: 10 or 11 } && tax.CurrentTaxIdentityNumber.All(char.IsAsciiDigit)
                && TaxIdentityNumber.TryParse(tax.ProposedTaxIdentityNumber, out var proposed, out _)
                && proposed.Value == tax.ProposedTaxIdentityNumber && tax.CurrentTaxIdentityNumber != proposed.Value
                && tax.ProposedKind == (proposed.Kind == TaxIdentityKind.Vkn ? CounterpartyTaxIdentityKindsV1.Vkn : CounterpartyTaxIdentityKindsV1.Tckn);
        }
        return snapshot.CounterpartyTaxIdentityChange is null && snapshot.CounterpartyLegalNameChange is { } legal
            && legal.PartyId == partyId && legal.ChangeRequestId == snapshot.Item.ItemId && Reason(legal.Reason, PartyLegalNameChangeRequest.ReasonMaximumLength)
            && Name(legal.CurrentLegalName) && Name(legal.ProposedLegalName)
            && legal.CurrentLegalName != legal.ProposedLegalName;
    }

    private async Task<WorkDecisionResultV1> DecideCounterpartyIdentityAsync(string kind, Guid requestId,
        WorkDecisionRequestV1 request, string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor,
        CancellationToken token)
    {
        // Only the company/account-fenced native source supplies the global party identity. It is
        // never parsed from a URL or accepted from the generic command body.
        var parties = snapshot.Item.References.Where(reference => reference.Role == "party").ToArray();
        if (parties.Length != 1 || parties[0].RecordId is not { } partyId || partyId == Guid.Empty)
            throw new WorkDecisionException(503);
        if (kind == CounterpartyTaxIdentity && counterpartyTaxIdentity is null
            || kind == CounterpartyLegalName && counterpartyLegalName is null)
            throw new WorkDecisionException(503);
        var metadata = new CounterpartyCommandMetadata(snapshot.CompanyId, actor, correlation.CorrelationId,
            correlation.CausationId, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent, key);
        CounterpartyCommandExecution? answer;
        try
        {
            // Party expected-version semantics belong to the native owner: approve increments it;
            // reject does not. An old expected version/key is forwarded unchanged for immutable replay.
            answer = kind == CounterpartyTaxIdentity
                ? await counterpartyTaxIdentity!.DecideTaxIdentityChangeAsync(partyId,
                    new(request.ExpectedVersion, requestId, request.Approve ? "approve" : "reject", request.Reason), metadata, token)
                : await counterpartyLegalName!.DecideLegalNameChangeAsync(partyId,
                    new(request.ExpectedVersion, requestId, request.Approve ? "approve" : "reject", request.Reason), metadata, token);
        }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch (ArgumentException) { throw new WorkDecisionException(400, "work_decision_invalid"); }
        catch (InvalidOperationException) { throw new WorkDecisionException(503); }
        if (answer?.Response is not { } response || !Enum.IsDefined(response.Result)
            || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId) || answer.OriginalCorrelationId.Length > 128
            || answer.OriginalCorrelationId.Trim() != answer.OriginalCorrelationId
            || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId)
            throw new WorkDecisionException(503);
        if (response.Result != CounterpartyCommandResult.Succeeded)
        {
            var knownRefusal = response.Result is CounterpartyCommandResult.NotFound
                or CounterpartyCommandResult.VersionConflict or CounterpartyCommandResult.IdempotencyConflict
                || kind == CounterpartyTaxIdentity && response.Result is (CounterpartyCommandResult.TaxIdentityChangeNotPending
                    or CounterpartyCommandResult.TaxIdentitySelfDecision or CounterpartyCommandResult.TaxIdentityChangeStale)
                || kind == CounterpartyLegalName && response.Result is (CounterpartyCommandResult.LegalNameChangeNotPending
                    or CounterpartyCommandResult.LegalNameSelfDecision or CounterpartyCommandResult.LegalNameChangeStale);
            if (!knownRefusal) throw new WorkDecisionException(503);
            var nativeStatus = response.Result == CounterpartyCommandResult.NotFound ? 404 : 409;
            if (response.StatusCode != nativeStatus || response.Resource is not null)
                throw new WorkDecisionException(503);
            throw new WorkDecisionException(nativeStatus, replayed: answer.IsReplay, original: answer.OriginalCorrelationId);
        }
        // The hardened native decision boundary returns success only after its own durable receipt,
        // or the exact immutable native replay. It is intentionally 204 with NO current Party view.
        if (response.StatusCode != 204 || response.Resource is not null || response.Conflict is not null
            || response.Title is not null || response.Detail is not null)
            throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, kind, requestId, request.Approve ? "Approved" : "Rejected",
            answer.IsReplay, answer.OriginalCorrelationId);
    }
}
