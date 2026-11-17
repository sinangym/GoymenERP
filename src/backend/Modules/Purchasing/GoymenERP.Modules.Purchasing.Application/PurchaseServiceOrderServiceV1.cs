using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>ALC-S7 / ALS-D29/D40, KIM-D49. All owner reads are fenced; no grant, physical receipt or assumed service catalog.</summary>
public sealed class PurchaseServiceOrderServiceV1(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IPurchaseServiceOrderStoreV1 store, IPurchasingPartyAccountPortV2 parties,
    ISettingsReferenceListReaderV1 referenceLists, ISettingsPrerequisiteProvider profiles, ISettingsUnitIdentityCatalogV1 unitIdentities, TimeProvider clock,
    IPurchaseServiceOrderReferenceProofV1? physicalReferences = null,
    ISettingsSubcontractTaxDefaultProviderV1? subcontractTax = null) : IPurchaseServiceOrderServiceV1
{
    private sealed record Scope(Guid Company, string Actor, IReadOnlyCollection<string> Roles);
    public async Task<PurchaseServiceOrderReferencesV1> ReferencesAsync(CancellationToken token = default)
    {
        var scope = await RequireAsync(PurchasingPermissionManifestV1.OrdersCreate, token);
        var list = await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersCreate,
            () => referenceLists.ListAsync(scope.Company, SettingsReferenceListKindV1.ServiceType, token), token);
        if (list.CompanyId != scope.Company || list.Kind != SettingsReferenceListKindV1.ServiceType || !list.Enabled || list.Entries is null)
            throw Failure(503);
        var units = await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersCreate, () => unitIdentities.ListAsync(token), token);
        if (units.Any(x => x.UnitId == Guid.Empty || string.IsNullOrWhiteSpace(x.Code)) || units.Select(x => x.UnitId).Distinct().Count() != units.Count)
            throw Failure(503);
        return new(scope.Company, list.Entries.Where(x => x.Active).Select(x => new PurchaseServiceTypeOptionV1(x.Id, Text(x.Version), x.Code, x.Name)).ToArray(),
            units.Select(x => new PurchaseServiceUnitOptionV1(x.UnitId, x.Code, x.IsKilogram)).ToArray());
    }
    public async Task<IReadOnlyList<PurchaseServiceOrderViewV1>> ListAsync(CancellationToken token = default)
    {
        var scope = await RequireAsync(PurchasingPermissionManifestV1.OrdersRead, token);
        var rows = await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersRead, () => store.ListAsync(token), token);
        var disclose = await DiscloseAsync(scope, token);
        return rows.Select(x => View(x, disclose)).ToArray();
    }
    public async Task<PurchaseServiceOrderViewV1> ReadAsync(Guid orderId, CancellationToken token = default)
    {
        var scope = await RequireAsync(PurchasingPermissionManifestV1.OrdersRead, token);
        var row = await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersRead, () => store.ReadAsync(orderId, token), token) ?? throw Failure(404);
        return View(row, await DiscloseAsync(scope, token));
    }
    public Task<PurchaseServiceOrderResponseV1> CreateAsync(CreatePurchaseServiceOrderV1 request, PurchasingCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CommandAsync(request.OrderId, PurchaseServiceOrderCommandsV1.Create, PurchasingPermissionManifestV1.OrdersCreate,
            request, request.Reason, metadata, async (scope, old, ct) =>
            {
                if (request.ExpectedVersion != "0" || old is not null) throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
                if (request.Lines is not { Count: > 0 and <= 100 } || request.Lines.Any(x => x is null)
                    || request.Materials is null || request.Materials.Any(x => x is null) || request.ExpectedOutputs is null || request.ExpectedOutputs.Any(x => x is null)
                    || !Enum.IsDefined(request.Kind)) throw new ArgumentException();
                var supplier = await SupplierAsync(scope, request.SupplierPartyId, request.CompanyAccountId, request.Kind, PurchasingPermissionManifestV1.OrdersCreate, ct);
                var profile = await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersCreate,
                    () => profiles.GetMoneyProfileAsync(request.CurrencyId, PurchasingMoneyOperationsV1.OrderV1, request.BusinessDate, ct), ct);
                if (profile.CompanyId != scope.Company || profile.CurrencyId != request.CurrencyId || !profile.IsActive
                    || profile.Operation != PurchasingMoneyOperationsV1.OrderV1 || profile.EffectiveFrom > request.BusinessDate
                    || profile.EffectiveThrough is { } until && until < request.BusinessDate) throw Failure(503);
                var money = new PurchaseServiceMoney(profile.CurrencyId, profile.CurrencyCode, profile.Profile.ProfileId,
                    profile.Profile.Version, profile.Scale, profile.UnitPriceScale);
                var lines = new List<PurchaseServiceLine>();
                foreach (var line in request.Lines)
                {
                    var type = await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersCreate,
                        () => referenceLists.GetAsync(scope.Company, SettingsReferenceListKindV1.ServiceType, line.ServiceTypeId, ct), ct);
                    if (type.Outcome == SettingsReferenceListLookupOutcomeV1.Unavailable) throw Failure(503);
                    if (type.Outcome != SettingsReferenceListLookupOutcomeV1.Found || type.Entry is not { Active: true } entry || entry.Id != line.ServiceTypeId)
                        throw new ArgumentException("Hizmet tipi etkin değil.");
                    if (Text(entry.Version) != line.ExpectedServiceTypeVersion) throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
                    var unit = await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersCreate,
                        () => profiles.GetUnitProfileAsync(line.UnitId, request.BusinessDate, ct), ct);
                    if (unit.CompanyId != scope.Company || unit.UnitId != line.UnitId || !unit.IsActive || unit.Scale is < 0 or > 6
                        || unit.EffectiveFrom > request.BusinessDate || unit.EffectiveThrough is { } end && end < request.BusinessDate)
                        throw Failure(503);
                    if (request.Kind == PurchaseServiceOrderKind.OwnMaterialSubcontract && !unit.IsKilogram)
                        throw new ArgumentException("Fason hizmet birim fiyatı işlenecek kg başına girilir (ALS-D106).");
                    var quantity = Decimal(line.Quantity);
                    if (unit.IsKilogram && decimal.Round(quantity, 3) != quantity) throw new ArgumentException("Kg ölçeği geçersiz.");
                    lines.Add(new(line.LineId, entry.Id, entry.Version, entry.Name, unit.UnitId, unit.UnitCode,
                        unit.Profile.ProfileId, unit.Profile.Version, unit.IsKilogram ? Math.Min(3, unit.Scale) : unit.Scale, quantity,
                        line.UnitPrice is null ? null : Decimal(line.UnitPrice, zero: true), 0));
                }
                if (request.Kind == PurchaseServiceOrderKind.OwnMaterialSubcontract)
                {
                    var tax = await TaxAsync(scope, PurchasingPermissionManifestV1.OrdersCreate, ct);
                    lines = lines.Select(line => line with { Tax = tax }).ToList();
                }
                var materials = request.Materials.Select(x => new PurchaseServiceMaterial(x.LabelId, x.LabelVersion, x.ProductId, x.ProductVersion, Decimal(x.QuantityKg))).ToArray();
                var outputs = request.ExpectedOutputs.Select(x => new PurchaseServiceOutput(x.ProductId, x.ProductVersion, Decimal(x.ExpectedQuantityKg))).ToArray();
                await VerifyPhysicalAsync(scope, PurchasingPermissionManifestV1.OrdersCreate, request.BusinessDate, materials, outputs, ct);
                var now = clock.GetUtcNow();
                return PurchaseServiceOrder.Create(new(scope.Company, request.OrderId, 1, request.Kind, PurchaseServiceOrderStatus.Draft,
                    supplier.PartyId, supplier.CompanyAccountId, supplier.PartyVersion, supplier.AccountVersion, request.BusinessDate,
                    scope.Actor, null, now, now, money, lines, materials, outputs, []));
            }, token);
    }
    public Task<PurchaseServiceOrderResponseV1> ApproveAsync(Guid orderId, ApprovePurchaseServiceOrderV1 request,
        PurchasingCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CommandAsync(orderId, PurchaseServiceOrderCommandsV1.Approve, PurchasingPermissionManifestV1.OrdersApprove,
            request, request.Reason, metadata, async (scope, old, ct) =>
            {
                var value = old ?? throw Failure(404);
                var supplier = await SupplierAsync(scope, value.SupplierPartyId, value.CompanyAccountId, value.Kind, PurchasingPermissionManifestV1.OrdersApprove, ct);
                foreach (var line in value.PriceRevision == 0 ? value.Lines : [])
                {
                    var type = await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersApprove,
                        () => referenceLists.GetAsync(scope.Company, SettingsReferenceListKindV1.ServiceType, line.ServiceTypeId, ct), ct);
                    if (type.Outcome == SettingsReferenceListLookupOutcomeV1.Unavailable) throw Failure(503);
                    if (type.Outcome != SettingsReferenceListLookupOutcomeV1.Found || type.Entry?.Active != true || type.Entry.Id != line.ServiceTypeId)
                        throw new PurchaseServiceOrderException(PurchaseServiceOrder.StateConflict);
                    if (type.Entry.Version != line.ServiceTypeVersion) throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
                }
                // A price-only revision retains the original approved material identities. A later stock movement
                // cannot invalidate already performed service or require the same label to be physically sent again.
                if (value.PriceRevision == 0)
                {
                    if (value.Kind == PurchaseServiceOrderKind.OwnMaterialSubcontract)
                    {
                        var tax = await TaxAsync(scope, PurchasingPermissionManifestV1.OrdersApprove, ct);
                        if (value.Lines.Any(line => line.Tax != tax)) throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
                    }
                    await VerifyPhysicalAsync(scope, PurchasingPermissionManifestV1.OrdersApprove, value.BusinessDate, value.Materials, value.ExpectedOutputs, ct);
                }
                return PurchaseServiceOrder.Approve(value with { PartyVersion = supplier.PartyVersion, AccountVersion = supplier.AccountVersion },
                    Version(request.ExpectedVersion), scope.Actor, clock.GetUtcNow());
            }, token);
    }
    public Task<PurchaseServiceOrderResponseV1> AcceptAsync(Guid orderId, AcceptPurchaseServiceV1 request,
        PurchasingCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CommandAsync(orderId, PurchaseServiceOrderCommandsV1.Accept, PurchasingPermissionManifestV1.GoodsReceiptsRecord,
            request, request.Reason, metadata, (scope, old, _) => Task.FromResult(PurchaseServiceOrder.Accept(old ?? throw Failure(404),
                Version(request.ExpectedVersion), request.AcceptanceId, request.LineId, Decimal(request.Quantity), request.AcceptedOn,
                scope.Actor, request.Reason, clock.GetUtcNow())), token);
    }
    public Task<PurchaseServiceOrderResponseV1> RevisePricesAsync(Guid orderId, RevisePurchaseServiceOrderPricesV1 request,
        PurchasingCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CommandAsync(orderId, PurchaseServiceOrderCommandsV1.RevisePrices, PurchasingPermissionManifestV1.OrdersRevise,
            request, request.Reason, metadata, (scope, old, _) =>
            {
                if (request.Lines is not { Count: > 0 and <= 100 } || request.Lines.Any(x => x is null)
                    || request.Lines.Select(x => x.LineId).Distinct().Count() != request.Lines.Count) throw new ArgumentException();
                return Task.FromResult(PurchaseServiceOrder.RevisePrices(old ?? throw Failure(404), Version(request.ExpectedVersion),
                    request.Lines.ToDictionary(x => x.LineId, x => Decimal(x.UnitPrice, zero: true)), scope.Actor, request.Reason, clock.GetUtcNow()));
            }, token);
    }
    private async Task<PurchaseServiceOrderResponseV1> CommandAsync<T>(Guid id, string command, string permission, T request, string reason,
        PurchasingCommandMetadata metadata, Func<Scope, PurchaseServiceOrderState?, CancellationToken, Task<PurchaseServiceOrderState>> mutate, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(metadata); token.ThrowIfCancellationRequested();
        var scope = await RequireAsync(permission, token);
        if (id == Guid.Empty || metadata.RequestedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || metadata.IdempotencyKey.Length > 200 || metadata.IdempotencyKey != metadata.IdempotencyKey.Trim()
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || metadata.CorrelationId.Length > 128) throw Failure(400);
        var hash = PurchasingRequestHash.Compute(command, new { orderId = id, request });
        var envelope = new PurchasingWriteEnvelope(scope.Actor, permission, scope.Roles, metadata.CorrelationId, metadata.CausationId,
            metadata.IdempotencyKey, "api");
        try
        {
            var cleanReason = PurchaseServiceOrder.RequireReason(reason);
            var result = await ReadAsync(scope, permission, () => store.ReplayAsync(command, envelope, hash, token), token);
            var disclosure = await DiscloseAsync(scope, token);
            result ??= await store.WriteAsync(id, command, envelope, hash, cleanReason,
                (old, ct) => mutate(scope, old, ct), async ct => { await StillAsync(scope, permission, ct); disclosure = await DiscloseAsync(scope, ct); }, token) ?? throw Failure(404);
            if (result.Refusal is { } refusal) throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors,
                result.IsReplay, result.OriginalCorrelationId);
            return new(View(result.Order ?? throw Failure(503), disclosure), result.IsReplay, result.OriginalCorrelationId);
        }
        catch (PurchaseServiceOrderException exception) { throw new PurchasingServiceException(409, exception.Code); }
        catch (SettingsPrerequisiteException) { throw Failure(503); }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.SameKeyPayloadMismatch)
        { throw new PurchasingServiceException(409, PurchasingProblemCodesV1.SameKeyPayloadMismatch); }
        catch (ArgumentException) { throw Failure(422); }
        catch (OverflowException) { throw Failure(422); }
    }
    private async Task<PurchasingPartyAccountV2> SupplierAsync(Scope scope, Guid party, Guid account, PurchaseServiceOrderKind kind, string permission, CancellationToken token)
    {
        var reply = await ReadAsync(scope, permission, () => parties.GetAuthorizedAsync(party, account, token), token);
        if (reply.Outcome == PurchasingPartyAccountOutcomeV1.Denied) throw Failure(403);
        if (reply.Outcome == PurchasingPartyAccountOutcomeV1.Unavailable) throw Failure(503);
        if (reply.Outcome != PurchasingPartyAccountOutcomeV1.Found || reply.Account is not { } value || value.CompanyId != scope.Company
            || value.PartyId != party || value.CompanyAccountId != account) throw Failure(422);
        if (!value.PartyIsActive || !value.AccountIsActive || !value.AccountHasSupplierRole
            || kind == PurchaseServiceOrderKind.OwnMaterialSubcontract && !value.AccountHasTollingRole) throw Failure(422);
        return value;
    }
    private async Task VerifyPhysicalAsync(Scope scope, string permission, DateOnly date, IReadOnlyList<PurchaseServiceMaterial> materials,
        IReadOnlyList<PurchaseServiceOutput> outputs, CancellationToken token)
    {
        if (materials.Count == 0 && outputs.Count == 0) return;
        var owner = physicalReferences ?? throw Failure(503);
        await ReadAsync(scope, permission, async () => { await owner.ValidateAsync(scope.Company, date, materials, outputs, token); return true; }, token);
    }
    private async Task<PurchaseServiceTax> TaxAsync(Scope scope, string permission, CancellationToken token)
    {
        var provider = subcontractTax ?? throw Failure(503);
        var answer = await ReadAsync(scope, permission,
            () => provider.GetSubcontractTaxDefaultAsync(scope.Company, SubcontractTaxSideV1.Purchasing, token), token);
        if (answer.CompanyId != scope.Company || answer.Side != SubcontractTaxSideV1.Purchasing
            || answer.Version < 1 || answer.Default is not { } value) throw Failure(503);
        return new(value.VatApplies, value.VatPercent, value.ExemptionCode, value.WithholdingApplies,
            value.WithholdingPercent, value.WithholdingCode, answer.Version);
    }
    private async Task<Scope> RequireAsync(string permission, CancellationToken token)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw Failure(401);
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw Failure(403);
        if (!ErpPermissions.IsKnown(permission)) throw Failure(503);
        var id = company.CompanyId; var actor = user.UserId;
        var grant = await authority.RevalidatePermissionScopeAsync(permission, id, token);
        if (!Current(id, actor) || !grant.IsGranted || !grant.IsCompanyWide) throw Failure(403);
        return new(id, actor, grant.RoleCodes.Order(StringComparer.Ordinal).ToArray());
    }
    private async Task StillAsync(Scope scope, string permission, CancellationToken token)
    { var fresh = await RequireAsync(permission, token); if (fresh.Company != scope.Company || fresh.Actor != scope.Actor) throw Failure(403); }
    private async Task<T> ReadAsync<T>(Scope scope, string permission, Func<Task<T>> read, CancellationToken token)
    { var value = await read(); await StillAsync(scope, permission, token); return value; }
    private async Task<bool> DiscloseAsync(Scope scope, CancellationToken token)
    {
        var grant = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.OrdersPriceRead, scope.Company, token);
        if (!Current(scope.Company, scope.Actor)) throw Failure(403);
        return grant.IsGranted && grant.IsCompanyWide;
    }
    private bool Current(Guid id, string actor) => user.IsAuthenticated && user.UserId == actor && company.IsResolved
        && company.CompanyId == id && user.CanAccessCompany(id);
    private static PurchaseServiceOrderViewV1 View(PurchaseServiceOrderState value, bool prices)
    {
        PurchaseServiceOrder.Validate(value); var money = value.Money.Value();
        return new(value.CompanyId, value.OrderId, value.DocumentNumber ?? throw Failure(503), Text(value.Version), value.Kind.ToString(), value.Status.ToString(),
            value.SupplierPartyId, value.CompanyAccountId, value.BusinessDate, value.Money.CurrencyId, value.Money.CurrencyCode,
            value.IsOpen, prices ? "Visible" : "Withheld", value.Lines.Select(line => new PurchaseServiceLineViewV1(line.LineId,
                line.ServiceTypeId, Text(line.ServiceTypeVersion), line.ServiceName, line.UnitId, line.UnitCode, Text(line.Quantity),
                Text(line.AcceptedQuantity), prices && line.UnitPrice is { } p ? Text(p) : null,
                prices && line.UnitPrice is { } unit ? Text(new PurchaseOrderLinePrice(unit, line.UnitId).LineAmount(line.Quantity, money)) : null,
                line.Tax is not { } tax ? null : new(tax.VatApplies, tax.VatPercent is { } vat ? Text(vat) : null, tax.ExemptionCode,
                    tax.WithholdingApplies, tax.WithholdingPercent is { } withheld ? Text(withheld) : null, tax.WithholdingCode, Text(tax.SettingsVersion)))).ToArray(),
            value.Materials.Select(x => new PurchaseServiceMaterialInputV1(x.LabelId, x.LabelVersion, x.ProductId, x.ProductVersion, Text(x.QuantityKg))).ToArray(),
            value.ExpectedOutputs.Select(x => new PurchaseServiceOutputInputV1(x.ProductId, x.ProductVersion, Text(x.ExpectedQuantityKg))).ToArray(),
            value.Acceptances.Select(x => new PurchaseServiceAcceptanceViewV1(x.AcceptanceId, x.LineId, Text(x.Quantity), x.AcceptedOn, x.RecordedBy, x.Reason, x.RecordedAtUtc)).ToArray());
    }
    private static decimal Decimal(string text, bool zero = false) => text is not null && text.Length <= 40
        && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
        && Text(number) == text && (zero ? number >= 0 : number > 0) ? number : throw new ArgumentException("Kanonik miktar gerekli.");
    private static long Version(string text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
        && n > 0 && Text(n) == text ? n : throw new ArgumentException("Sürüm geçersiz.");
    private static string Text(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static PurchasingServiceException Failure(int status) => new(status, status == 503 ? "service_unavailable" : status == 403 ? "forbidden" : status == 401 ? "unauthenticated" : status == 404 ? "not_found" : "validation_failed");
}
