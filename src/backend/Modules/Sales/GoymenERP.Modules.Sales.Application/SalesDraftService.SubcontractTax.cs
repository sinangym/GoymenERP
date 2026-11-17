using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>SAT-D381/D105 and Issue #1189 6947126872: tax-only writes use Sales approval, without conferring price-write authority.</summary>
public sealed partial class SalesDraftService : ISalesSubcontractTaxServiceV1
{
    private async Task<Scope> WithSubcontractTaxAuthorityAsync(Scope scope, CancellationToken token)
    {
        await RequireStillAsync(scope, bindings.ApprovePermission, token);
        return scope with { RequiresSubcontractTaxAuthority = true };
    }
    public async Task<SalesSubcontractTaxOptionsV1> GetSubcontractTaxOptionsAsync(Guid draftId, CancellationToken token = default)
    {
        var scope = await RequireAsync(bindings.ApprovePermission, token);
        var draft = await LoadDraftAsync(scope, bindings.ApprovePermission, draftId, token);
        if (!draft.IsSubcontractService) throw new SalesDraftServiceException(422, "validation_failed");
        var options = await ReadTaxOptionsAsync(scope, bindings.ApprovePermission, draft.Header.BusinessDate, token);
        await RequireStillAsync(scope, bindings.ApprovePermission, token);
        return new(draft.Version.ToString(CultureInfo.InvariantCulture), draft.Header.BusinessDate,
            draft.Header.BuyerTax?.Situation ?? StartBuyerSituation, options);
    }
    public async Task<SalesOrderDraftResponse> ReviseSubcontractTaxAsync(Guid draftId, SalesSubcontractTaxRevisionV1 request,
        SalesCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        var permission = bindings.ApprovePermission;
        var scope = await RequireAsync(permission, token);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected < 1
            || request.LineId == Guid.Empty || request.Tax is null || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 4000)
            throw new SalesDraftServiceException(400, "validation_failed");
        const string command = SalesDraftCommands.ReviseSubcontractTaxV1;
        var hash = SalesDraftRequestHash.Compute(command, draftId, request); var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, hash, command, token);
        if (replay is not null) return await ReplayedAsync(scope, replay, token);
        var draft = await LoadDraftAsync(scope, permission, draftId, token);
        if (!draft.IsSubcontractService || draft.Lines.All(x => x.Id != request.LineId)) throw new SalesDraftServiceException(422, "validation_failed");
        if (draft.Version != expected) throw await TerminalAsync(scope, permission, envelope, hash,
            new(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", SalesRuleCodes.VersionConflict)]), token, command, draftId);
        var buyer = draft.Header.BuyerTax ?? SalesBuyerTax.Create(StartBuyerSituation);
        var choice = request.Tax; SalesLineTaxOverride? change = null;
        if (choice.Mode == SalesDraftLineTaxModesV1.Default)
        {
            if (choice.BuyerSituation is not null || choice.Reason is not null || choice.ItemType is not null || choice.ExemptionCode is not null
                || choice.VatRateCode is not null || choice.WithholdingRatioCode is not null) throw new SalesDraftServiceException(400, "validation_failed");
        }
        else if (choice.Mode == SalesDraftLineTaxModesV1.Override)
            change = await RequireLineTaxChangeAsync(scope, permission, choice.ItemType, choice.BuyerSituation!, choice.ExemptionCode,
                choice.Reason!, new(draft.Header.BusinessDate), "tax", timeProvider.GetUtcNow(), token, choice.VatRateCode, choice.WithholdingRatioCode);
        else throw new SalesDraftServiceException(400, "validation_failed");
        var line = draft.Lines.Single(x => x.Id == request.LineId);
        var tax = await ResolveLineTaxAsync(scope, permission, line.Product.ProductId, buyer, change, draft.Header.BusinessDate, token,
            field: "tax", subcontract: true);
        var taxes = draft.Lines.ToDictionary(x => x.Id, x => x.Id == line.Id ? tax : x.Tax);
        var inputs = draft.Lines.Select(x => new LineCommercialInput(x.Id, x.Commercial)).ToArray();
        try
        {
            var result = await store.ReviseDraftCommercialAsync(envelope, hash, command, draftId,
                (current, now) => current.ReviseCommercial(expected, inputs, scope.ActorId, now, taxes: taxes),
                new("sales.order-draft.subcontract-tax-revised.v1", "ReviseSubcontractTax", request.Reason.Trim()),
                async ct => { await RequireStillAsync(scope, permission, ct); }, token);
            if (result is null) throw new SalesDraftServiceException(404, "not_found");
            return Response(scope, result.Draft, result.Outcome == SalesDraftWriteOutcome.Replayed, result.OriginalCorrelationId, await DiscloseAsync(scope, token));
        }
        catch (SalesVersionConflictException exception)
        { throw await TerminalAsync(scope, permission, envelope, hash, new(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]), token, command, draftId); }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        { throw await TerminalAsync(scope, permission, envelope, hash, new(409, SalesDraftProblemCodesV1.StateConflict, [new("tax", exception.Code)]), token, command, draftId); }
        catch (SalesRuleException exception) { throw new SalesDraftServiceException(422, "validation_failed", [new("tax", exception.Code)]); }
        catch (SalesDraftStoreException exception) { throw Store(exception); }
    }
}
