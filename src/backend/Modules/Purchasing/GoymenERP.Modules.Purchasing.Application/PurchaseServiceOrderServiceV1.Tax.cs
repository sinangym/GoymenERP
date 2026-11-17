using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchaseServiceOrderServiceV1
{
    public async Task<SubcontractTaxOptionsV1> TaxOptionsAsync(Guid orderId, CancellationToken token = default)
    {
        var scope = await RequireAsync(PurchasingPermissionManifestV1.OrdersApprove, token);
        var value = await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersApprove, () => store.ReadAsync(orderId, token), token) ?? throw Failure(404);
        if (value.Kind != PurchaseServiceOrderKind.OwnMaterialSubcontract) throw Failure(422);
        return await TaxCatalogAsync(scope, value.BusinessDate, token);
    }
    public Task<PurchaseServiceOrderResponseV1> ReviseTaxAsync(Guid orderId, RevisePurchaseServiceOrderTaxV1 request,
        PurchasingCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CommandAsync(orderId, PurchaseServiceOrderCommandsV1.ReviseTax, PurchasingPermissionManifestV1.OrdersApprove,
            request, request.Reason, metadata, async (scope, old, ct) =>
            {
                var value = old ?? throw Failure(404);
                if (value.Kind != PurchaseServiceOrderKind.OwnMaterialSubcontract) throw Failure(422);
                var options = await TaxCatalogAsync(scope, value.BusinessDate, ct);
                if (request.ExpectedSourceVersion != options.SourceVersion || request.ExpectedDefaultVersion != options.DefaultVersion)
                    throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
                PurchaseServiceTax tax;
                if (request.UseDefault)
                {
                    if (request.VatRateCode is not null || request.ExemptionCode is not null || request.WithholdingRatioCode is not null) throw new ArgumentException();
                    tax = await TaxAsync(scope, PurchasingPermissionManifestV1.OrdersApprove, ct);
                    if (Text(tax.SettingsVersion) != options.DefaultVersion) throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
                }
                else tax = ListedTax(options, request.VatRateCode, request.ExemptionCode, request.WithholdingRatioCode,
                    scope.Actor, request.Reason, clock.GetUtcNow());
                return PurchaseServiceOrder.ReviseTax(value, Version(request.ExpectedVersion), request.LineId, tax, scope.Actor, request.Reason, clock.GetUtcNow());
            }, token);
    }
    private async Task<SubcontractTaxOptionsV1> TaxCatalogAsync(Scope scope, DateOnly on, CancellationToken token)
    {
        var owner = taxCatalog ?? throw Failure(503);
        var value = await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersApprove,
            () => owner.GetAsync(scope.Company, SubcontractTaxSideV1.Purchasing, on, token), token);
        if (value.CompanyId != scope.Company || value.Side != SubcontractTaxSideV1.Purchasing || value.On != on
            || string.IsNullOrWhiteSpace(value.SourceVersion) || value.VatRates is null || value.WithholdingRatios is null
            || value.ExemptionCodes is null || value.WithholdingCodes is null) throw Failure(503);
        return value;
    }
    private static PurchaseServiceTax ListedTax(SubcontractTaxOptionsV1 options, string? vatCode, string? exemption,
        string? ratioCode, string actor, string reason, DateTimeOffset now)
    {
        var vat = options.VatRates.SingleOrDefault(x => x.Code == vatCode) ?? throw new ArgumentException("KDV oranı gerçek listeden seçilir.");
        var vatPercent = Decimal(vat.Percent, zero: true);
        var ratio = ratioCode is null ? null : options.WithholdingRatios.SingleOrDefault(x => x.Code == ratioCode)
            ?? throw new ArgumentException("Tevkifat oranı gerçek listeden seçilir.");
        if (vatPercent == 0 && (exemption is null || !options.ExemptionCodes.Any(x => x.Code == exemption) || ratio is not null)
            || vatPercent > 0 && exemption is not null
            || ratio is not null && (ratio.TaxCode is null || !options.WithholdingCodes.Any(x => x.Code == ratio.TaxCode)))
            throw new ArgumentException("Vergi kodu gerçek listede bulunmalıdır.");
        return new(vatPercent > 0, vatPercent > 0 ? vatPercent : null, vatPercent == 0 ? exemption : null,
            ratio is not null, ratio is null ? null : Decimal(ratio.Percent), ratio?.TaxCode, Version(options.DefaultVersion),
            new(options.SourceVersion, vat.Code, ratio?.Code, actor, PurchaseServiceOrder.RequireReason(reason), now));
    }
    private async Task VerifyTaxSnapshotsAsync(Scope scope, PurchaseServiceOrderState order, CancellationToken token)
    {
        foreach (var line in order.Lines)
        {
            if (await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersApprove,
                () => store.HasApprovedTaxSnapshotAsync(order, line.LineId, token), token)) continue;
            if (line.Tax?.Change is { } change)
            {
                var options = await TaxCatalogAsync(scope, order.BusinessDate, token);
                var current = ListedTax(options, change.VatRateCode, line.Tax.ExemptionCode, change.WithholdingRatioCode,
                    change.ChangedBy, change.Reason, change.ChangedAtUtc);
                // A separate default revision does not rewrite an explicit historical line choice.
                if ((current with { SettingsVersion = line.Tax.SettingsVersion }) != line.Tax)
                    throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
            }
            else if (line.Tax != await TaxAsync(scope, PurchasingPermissionManifestV1.OrdersApprove, token))
                throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
        }
    }
}
