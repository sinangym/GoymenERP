using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Requests;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// A1-1 (plan API-01; ALS-D2, ALS-D16, No.161 §1): the purchase manager's decision on a submitted request, under its own
/// name <c>purchasing.requests.approve</c>, on the same fences as every Purchasing command (#276: before the replay, after
/// every awaited read, right before the single save; none after the commit), ADR-011 replay first, and the terminal
/// refusals (§7) recorded under the key: a stale version, a state the decision does not start from, the maker deciding
/// their own request (hazırlayan ≠ onaylayan), an unknown maker (fail-closed) and — for a rejection — an APPROVED order
/// bound to the request, which the store measures inside the same save. A missing or too long reason is a 422 (not terminal).
/// <para>
/// A1-1b (ALS-D46, ALS-D47): the preparer's or submitter's withdrawal of their Submitted request runs on the same path
/// under its own name <c>purchasing.requests.withdraw</c>; its terminal refusals are a state other than Submitted, no
/// recorded maker at all (fail-closed), an actor who is neither recorded maker and a bound APPROVED order.
/// </para>
/// </summary>
public sealed partial class PurchasingService
{
    public Task<PurchaseRequestResponse> ApproveRequestAsync(Guid requestId, ApprovePurchaseRequestRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideRequestAsync(requestId, request, request.ExpectedVersion, metadata, PurchasingCommands.RequestApproveV1, bindings.RequestApprovePermission,
            (target, _, actor, version, now) => target.Approve(version, actor, now),
            new PurchasingMutationAudit("purchasing.purchase-request.approved.v1", "Approve", "Satınalma talebi onaylandı"), cancellationToken);
    }

    public Task<PurchaseRequestResponse> RejectRequestAsync(Guid requestId, RejectPurchaseRequestRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideRequestAsync(requestId, request, request.ExpectedVersion, metadata, PurchasingCommands.RequestRejectV1, bindings.RequestApprovePermission,
            (target, facts, actor, version, now) => target.Reject(version, actor, request.Reason, facts.HasApprovedOrder, now),
            new PurchasingMutationAudit("purchasing.purchase-request.rejected.v1", "Reject", "Satınalma talebi reddedildi"), cancellationToken);
    }

    public Task<PurchaseRequestResponse> WithdrawRequestAsync(Guid requestId, WithdrawPurchaseRequestRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideRequestAsync(requestId, request, request.ExpectedVersion, metadata, PurchasingCommands.RequestWithdrawV1, bindings.RequestWithdrawPermission,
            (target, facts, actor, version, now) => target.Withdraw(version, actor, request.Reason, facts.HasApprovedOrder, now),
            new PurchasingMutationAudit("purchasing.purchase-request.withdrawn.v1", "Withdraw", "Satınalma talebi oluşturan veya gönderen tarafından geri çekildi"),
            cancellationToken);
    }

    private async Task<PurchaseRequestResponse> DecideRequestAsync(Guid requestId, object wire, string expectedVersionText, PurchasingCommandMetadata metadata,
        string commandName, string? permission, Action<PurchaseRequest, PurchaseRequestBindingFacts, string, long, DateTimeOffset> decide,
        PurchasingMutationAudit audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(permission, cancellationToken);
        var expectedVersion = ParseVersion(expectedVersionText);
        var requestHash = PurchasingRequestHash.Compute(commandName, requestId, wire);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return ReplayedRequest(replay);
        }

        if (requestId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        // The last fence that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, permission, cancellationToken);
        var actor = scope.ActorId.Trim();
        PurchasingRequestStoreResult? result;
        try
        {
            result = await store.MutateRequestAsync(envelope, requestHash, commandName, requestId,
                (target, facts, now) => decide(target, facts, actor, expectedVersion, now), audit, cancellationToken);
        }
        catch (PurchasingVersionConflictException)
        {
            return await DecisionTerminalAsync(scope, permission, envelope, requestHash, commandName,
                PurchasingProblemCodesV1.VersionConflict, "expectedVersion", "version_conflict", cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code is PurchasingRuleCodes.RequestNotSubmitted or PurchasingRuleCodes.RequestNotRejectable
                                                             or PurchasingRuleCodes.RequestNotWithdrawable)
        {
            return await DecisionTerminalAsync(scope, permission, envelope, requestHash, commandName,
                PurchasingProblemCodesV1.RequestDecisionState, "status", exception.Code, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.RequestSameActor)
        {
            return await DecisionTerminalAsync(scope, permission, envelope, requestHash, commandName,
                PurchasingProblemCodesV1.RequestSameActor, "actor", exception.Code, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.RequestNotPreparer)
        {
            // ALS-D47: only the preparer or the submitter withdraws; terminal for this key.
            return await DecisionTerminalAsync(scope, permission, envelope, requestHash, commandName,
                PurchasingProblemCodesV1.RequestNotPreparer, "actor", exception.Code, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.RequestMakerUnknown)
        {
            return await DecisionTerminalAsync(scope, permission, envelope, requestHash, commandName,
                PurchasingProblemCodesV1.RequestMakerUnknown, "actor", exception.Code, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.RequestHasApprovedOrder)
        {
            // No.161 §1 (fail-closed): the bound approved order is cancelled first; terminal for this key.
            return await DecisionTerminalAsync(scope, permission, envelope, requestHash, commandName,
                PurchasingProblemCodesV1.RequestHasApprovedOrder, "status", exception.Code, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code is PurchasingRuleCodes.RequestRejectionReasonRequired
                                                             or PurchasingRuleCodes.RequestRejectionReasonTooLong
                                                             or PurchasingRuleCodes.RequestWithdrawalReasonRequired
                                                             or PurchasingRuleCodes.RequestWithdrawalReasonTooLong)
        {
            throw new PurchasingServiceException(422, exception.Code, [new("reason", exception.Code)]);
        }
        catch (PurchasingRuleException exception)
        {
            throw new PurchasingServiceException(422, "validation_failed", [new("request", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new PurchasingServiceException(400, "validation_failed");
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : ReplayedRequest(won);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new PurchasingServiceException(404, "not_found")
            : RequestResponse(result.Request, result.Outcome == PurchasingWriteOutcome.Replayed, result.OriginalCorrelationId);
    }

    private Task<PurchaseRequestResponse> DecisionTerminalAsync(Scope scope, string? permission, PurchasingWriteEnvelope envelope, string requestHash,
        string commandName, string code, string field, string fieldCode, CancellationToken cancellationToken) =>
        TerminalAsync(scope, permission, envelope, requestHash, commandName,
            new PurchasingTerminalRefusal(409, code, [new(field, fieldCode)]), ReplayedRequest, cancellationToken);
}
