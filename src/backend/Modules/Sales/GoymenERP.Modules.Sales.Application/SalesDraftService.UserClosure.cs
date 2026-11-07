using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D32 (EK3) — "Tüm eksenler tamamlansa da sipariş kendiliğinden kapanmaz; 'kapanmaya hazır' işareti çıkar ve kullanıcı
/// kapatır" — under its own name (SAT-D168, <c>sales.orders.close</c>) and without a reason (SAT-D215). The force-closure's
/// ordering and refusal semantics, with these differences:
/// <list type="bullet">
/// <item><b>The precondition is the status read's <c>readyToClose</c></b>, recomputed INSIDE the save from the same calculation
/// (<see cref="OrderStatusAsync"/>: the Logistics shipped totals, the Settings tolerance, the customer acceptance and the
/// owner-less axes) — never a flag the client carried. Not ready is the key's terminal 409
/// <see cref="SalesDraftProblemCodesV1.OrderNotReadyToClose"/>. While the source (MOD-05), invoicing (S5) and financial-closure
/// (MOD-04) owners do not exist those axes are NoSource and every user closure is that 409 (fail-closed).</item>
/// <item><b>No impact preview, no remainder:</b> a ready order is complete; nothing is cut, released or reversed. The line
/// quantities do not move; no stock or money event is produced; nothing of another module is written.</item>
/// <item><b>Ordering</b> (the force-closure's): fence → version shape → the key's replay → the store reads the draft → fence →
/// the domain's own preconditions → the readiness from the owners (fence after each) → last fence → ONE save. A stale version,
/// a wrong state and a not-ready order are the key's TERMINAL 409s.</item>
/// <item><b>Same lifecycle state as a force-closure</b> (<c>Closed</c>), its own record (reversible implementation detail, EK37
/// "Sınırlar"): the line port answers the lines as not approved ("Closed") exactly as after a force-closure.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftResponse> CloseDraftAsync(Guid draftId, SalesDraftCloseRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.ClosePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        const string commandName = SalesDraftCommands.CloseV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            // The same key, content and actor: the stored result again — the owners are not asked, nothing is closed twice.
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var audit = new SalesDraftStateAudit("sales.order-draft.closed.v1", "Close",
            "Kapanmaya hazır satış siparişi kullanıcı tarafından kapatıldı (SAT-D32, SAT-D168; gerekçe istenmez SAT-D215; geçmiş silinmez)");

        SalesDraftStateResult? result;
        try
        {
            result = await store.CloseDraftAsync(envelope, requestHash, commandName, draftId,
                (draft, now) => draft.CloseByUser(expectedVersion, scope.ActorId, now), audit,
                async (draft, writeCancellationToken) =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    // The domain's own preconditions first: a stale, terminal, unapproved or remainder-pending order asks no owner.
                    draft.RequireUserClosable(expectedVersion);
                    var status = await OrderStatusAsync(scope, permission, draft, withReturns: false, writeCancellationToken);
                    if (!status.ReadyToClose)
                    {
                        throw new NotReadyToClose();
                    }

                    // The last fence that can still prevent the write; none after the commit (ORG #321 P3-1).
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                }, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict,
                [new("expectedVersion", exception.Code)]), cancellationToken, commandName, draftId);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict,
                [new("state", exception.Code)]), cancellationToken, commandName, draftId);
        }
        catch (NotReadyToClose)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.OrderNotReadyToClose,
                [new("readyToClose", SalesDraftProblemCodesV1.OrderNotReadyToClose)]), cancellationToken, commandName, draftId);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("draft", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            if (won is null)
            {
                throw Store(exception);
            }

            return await ReplayedAsync(scope, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (result is null)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        return Response(scope, result.Draft, result.Outcome == SalesDraftWriteOutcome.Replayed, result.OriginalCorrelationId,
            await DiscloseAsync(scope, cancellationToken));
    }

    /// <summary>SAT-D32: the recorded user closure (or null) under the draft read; another company's draft is 404.</summary>
    public async Task<SalesOrderDraftUserClosureResponse> GetDraftUserClosureAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        SalesOrderDraft? draft;
        try
        {
            draft = await GuardedAsync(scope, bindings.ReadPermission, () => store.FindDraftAsync(draftId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (draft is null)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        return new SalesOrderDraftUserClosureResponse(draft.UserClosure is not { } facts
            ? null
            : new SalesDraftUserClosureV1(SalesDraftApiV1.RevisionText(facts.Revision), facts.ClosedBy, facts.ClosedAtUtc));
    }

    /// <summary>The recomputed readiness is false: the key's terminal 409 (recorded, replayed).</summary>
    private sealed class NotReadyToClose : Exception;
}
