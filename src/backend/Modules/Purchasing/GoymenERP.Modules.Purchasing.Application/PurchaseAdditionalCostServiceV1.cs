using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed class PurchaseAdditionalCostServiceV1(ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority,
    IPurchaseAdditionalCostStoreV1 store, IPurchasingPartyAccountPortV2 parties, ISettingsReferenceListReaderV1 types,
    ISettingsPrerequisiteProvider profiles, ISettingsTaxRateListReaderV1 taxRates, TimeProvider clock,
    PurchaseAdditionalCostWriteAdmissionV1? writeAdmission = null) : IPurchaseAdditionalCostServiceV1
{
    private sealed record Scope(Guid Company, string Actor, string Permission, IReadOnlyCollection<string> Roles);
    public async Task<PurchaseAdditionalCostSourceViewV1> SourceAsync(PurchaseAdditionalCostSourceKind kind, Guid orderId, CancellationToken token = default)
    {
        var scope = await RequireAsync(PurchasingPermissionManifestV1.OrdersRead, token);
        if (!Enum.IsDefined(kind) || orderId == Guid.Empty) throw Failure(404);
        var value = await ReadAsync(scope, () => store.ReadSourceAsync(kind, orderId, null, token), token) ?? throw Failure(404);
        if (value.CompanyId != scope.Company || value.OrderId != orderId || value.Kind != kind || value.OrderVersion < 1
            || value.Acceptances is null || value.Acceptances.Any(x => x.AcceptanceId == Guid.Empty || x.LineId == Guid.Empty)
            || value.Acceptances.Select(x => x.AcceptanceId).Distinct().Count() != value.Acceptances.Count) throw Failure(503);
        return new(value.CompanyId, orderId, Text(value.OrderVersion), value.OrderNumber, kind.ToString(), value.Money.CurrencyId, value.Money.CurrencyCode, value.Acceptances);
    }
    public async Task<PurchaseAdditionalCostReferencesV1> ReferencesAsync(DateOnly on, CancellationToken token = default)
    {
        var scope = await RequireAsync(PurchasingPermissionManifestV1.OrdersRead, token);
        if (on == default) throw Failure(400);
        var entries = await TypesAsync(scope, token); var rates = await RatesAsync(scope, on, token);
        return new(scope.Company, entries.Count > 0, entries.Select(x => new PurchaseServiceTypeOptionV1(x.Id, Text(x.Version), x.Code, x.Name)).ToArray(),
            on, Text(rates.ListVersion!.Value), rates.Entries.Select(x => new PurchaseAdditionalCostVatOptionV1(x.Code, x.Name, Text(x.Percent))).ToArray());
    }
    public async Task<IReadOnlyList<PurchaseAdditionalCostViewV1>> ListAsync(PurchaseAdditionalCostSourceKind kind, Guid orderId, CancellationToken token = default)
    {
        var scope = await RequireAsync(PurchasingPermissionManifestV1.OrdersRead, token);
        if (orderId == Guid.Empty || !Enum.IsDefined(kind)) throw Failure(404);
        var rows = await ReadAsync(scope, () => store.ListAsync(kind, orderId, token), token);
        if (rows.Any(x => x.SourceKind != kind)) throw Failure(503);
        return rows.Select(x => View(x, scope.Company, orderId)).ToArray();
    }
    public async Task<PurchaseAdditionalCostResponseV1> RecordAsync(RecordPurchaseAdditionalCostV1 request, PurchasingCommandMetadata metadata,
        CancellationToken token = default)
    {
        // ALS-D23/D27/D80/D107 plus the explicit ALC-S8 answer (6946921285). Composition never grants authority.
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw Failure(401);
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw Failure(403);
        var admission = writeAdmission ?? throw Failure(503);
        if (admission.Permission != PurchasingPermissionManifestV1.OrdersRevise || string.IsNullOrWhiteSpace(admission.DecisionReference)) throw Failure(503);
        var scope = await RequireAsync(admission.Permission, token);
        if (request is null || metadata is null || request.CostId == Guid.Empty || request.OrderId == Guid.Empty || request.AcceptanceId == Guid.Empty
            || request.ExpectedVersion != "0" || !Enum.IsDefined(request.SourceKind) || request.BusinessDate == default
            || metadata.RequestedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 200
            || metadata.IdempotencyKey.Trim() != metadata.IdempotencyKey || string.IsNullOrWhiteSpace(metadata.CorrelationId) || metadata.CorrelationId.Length > 128)
            throw Failure(400);
        var envelope = new PurchasingWriteEnvelope(scope.Actor, scope.Permission, scope.Roles, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey, "api");
        var hash = PurchasingRequestHash.Compute(PurchaseAdditionalCostCommandsV1.Record, request);
        try
        {
            var result = await ReadAsync(scope, () => store.ReplayAsync(envelope, hash, token), token);
            result ??= await store.RecordAsync(request.CostId, envelope, hash, async ct =>
            {
                var source = await ReadAsync(scope, () => store.ReadSourceAsync(request.SourceKind, request.OrderId, request.AcceptanceId, ct), ct) ?? throw Failure(404);
                if (source.CompanyId != scope.Company || source.OrderId != request.OrderId || source.Kind != request.SourceKind
                    || source.AcceptanceId != request.AcceptanceId) throw Failure(503);
                if (Text(source.OrderVersion) != request.ExpectedOrderVersion) throw new PurchaseAdditionalCostException("purchasing_additional_cost_source_version_conflict");
                var account = await ReadAsync(scope, () => parties.GetAuthorizedAsync(request.SupplierPartyId, request.CompanyAccountId, ct), ct);
                if (account.Outcome == PurchasingPartyAccountOutcomeV1.Denied) throw Failure(403);
                if (account.Outcome == PurchasingPartyAccountOutcomeV1.Unavailable) throw Failure(503);
                if (account.Outcome != PurchasingPartyAccountOutcomeV1.Found || account.Account is not { } supplier
                    || supplier.CompanyId != scope.Company || supplier.PartyId != request.SupplierPartyId || supplier.CompanyAccountId != request.CompanyAccountId
                    || !supplier.PartyIsActive || !supplier.AccountIsActive || !supplier.AccountHasSupplierRole) throw Failure(422);
                var catalog = await TypesAsync(scope, ct);
                SettingsReferenceListEntryV1? selected = null;
                if (request.ExpenseTypeId is { } id)
                {
                    selected = catalog.SingleOrDefault(x => x.Id == id) ?? throw Failure(422);
                    if (Text(selected.Version) != request.ExpectedExpenseTypeVersion) throw new PurchaseAdditionalCostException("purchasing_additional_cost_type_version_conflict");
                }
                else if (catalog.Count > 0 || request.ExpectedExpenseTypeVersion is not null) throw Failure(422);
                var taxList = await RatesAsync(scope, request.BusinessDate, ct);
                if (Text(taxList.ListVersion!.Value) != request.ExpectedVatListVersion)
                    throw new PurchaseAdditionalCostException("purchasing_additional_cost_tax_version_conflict");
                var vat = taxList.Entries.SingleOrDefault(x => x.Code == request.VatRateCode) ?? throw Failure(422);
                var money = await ReadAsync(scope, () => profiles.GetMoneyProfileAsync(source.Money.CurrencyId, PurchasingMoneyOperationsV1.OrderV1, request.BusinessDate, ct), ct);
                if (money.CompanyId != scope.Company || money.CurrencyId != source.Money.CurrencyId || money.Operation != PurchasingMoneyOperationsV1.OrderV1
                    || !money.IsActive || money.EffectiveFrom > request.BusinessDate || money.EffectiveThrough is { } until && until < request.BusinessDate) throw Failure(503);
                var amount = Decimal(request.NetAmount); var totals = PurchaseAdditionalCost.Amounts(request.CostId, amount, vat.Percent, money.Scale);
                return PurchaseAdditionalCost.Record(new(scope.Company, request.CostId, source.OrderId, source.OrderVersion, source.OrderNumber,
                    source.Kind, source.AcceptanceId, source.AcceptanceLineId, supplier.PartyId, supplier.CompanyAccountId, supplier.PartyVersion, supplier.AccountVersion,
                    request.BusinessDate, new(money.CurrencyId, money.CurrencyCode, money.Profile.ProfileId, money.Profile.Version, money.Scale, money.UnitPriceScale),
                    request.Description?.Trim() ?? "", selected is null ? null : new(selected.Id, selected.Version, selected.Code, selected.Name),
                    PurchasingRequestHash.Compute("purchasing.additional-cost.type-source.v1", new { scope.Company, Entries = catalog.OrderBy(x => x.Id).ToArray() }),
                    amount, new(vat.Code, taxList.ListVersion!.Value, request.BusinessDate, vat.Percent), totals.Vat, totals.Gross,
                    scope.Actor, request.Reason, clock.GetUtcNow()));
            }, ct => StillAsync(scope, ct), token);
            await StillAsync(scope, token);
            if (result.Refusal is { } refusal) throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors, result.IsReplay, result.OriginalCorrelationId);
            return new(View(result.Cost ?? throw Failure(503), scope.Company, request.OrderId), result.IsReplay, result.OriginalCorrelationId);
        }
        catch (PurchaseAdditionalCostException exception) { throw new PurchasingServiceException(409, exception.Code); }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.SameKeyPayloadMismatch)
        { throw new PurchasingServiceException(409, PurchasingProblemCodesV1.SameKeyPayloadMismatch); }
        catch (SettingsPrerequisiteException exception) { throw Failure(exception.Code == "settings_access_denied" ? 403 : 503); }
        catch (ArgumentException) { throw Failure(422); }
        catch (OverflowException) { throw Failure(422); }
    }
    private async Task<IReadOnlyList<SettingsReferenceListEntryV1>> TypesAsync(Scope scope, CancellationToken token)
    {
        var value = await ReadAsync(scope, () => types.ListAsync(scope.Company, SettingsReferenceListKindV1.ExpenseType, token), token);
        if (value.CompanyId != scope.Company || value.Kind != SettingsReferenceListKindV1.ExpenseType || !value.Enabled || value.Entries is null
            || value.Entries.Any(x => x is null || x.Id == Guid.Empty || x.Version < 1 || string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name))
            || value.Entries.Select(x => x.Id).Distinct().Count() != value.Entries.Count) throw Failure(503);
        return value.Entries.Where(x => x.Active).ToArray();
    }
    private async Task<SettingsTaxRateListResolutionV1> RatesAsync(Scope scope, DateOnly day, CancellationToken token)
    {
        var value = await ReadAsync(scope, () => taxRates.GetRateListAsync(TaxRateListKindsV1.VatRate, day, token), token);
        if (value.CompanyId != scope.Company || value.Kind != TaxRateListKindsV1.VatRate || value.On != day
            || value.State != TaxResolutionStatesV1.Resolved || value.ListVersion is not > 0 || value.Entries is null
            || value.Entries.Any(x => x is null || string.IsNullOrWhiteSpace(x.Code) || x.Percent is < 0 or > 100)
            || value.Entries.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != value.Entries.Count) throw Failure(503);
        return value;
    }
    private async Task<Scope> RequireAsync(string permission, CancellationToken token)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw Failure(401);
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw Failure(403);
        var id = company.CompanyId; var actor = user.UserId; IReadOnlyCollection<string> roles = [];
        foreach (var name in new[] { permission, PurchasingPermissionManifestV1.OrdersPriceRead }.Distinct(StringComparer.Ordinal))
        {
            var grant = await authority.RevalidatePermissionScopeAsync(name, id, token);
            if (!Current(id, actor) || !grant.IsGranted || !grant.IsCompanyWide) throw Failure(403);
            if (name == permission) roles = grant.RoleCodes.Order(StringComparer.Ordinal).ToArray();
        }
        return new(id, actor, permission, roles);
    }
    private async Task StillAsync(Scope scope, CancellationToken token)
    { var fresh = await RequireAsync(scope.Permission, token); if (fresh.Company != scope.Company || fresh.Actor != scope.Actor) throw Failure(403); }
    private async Task<T> ReadAsync<T>(Scope scope, Func<Task<T>> read, CancellationToken token)
    { try { var value = await read(); await StillAsync(scope, token); return value; } catch { await StillAsync(scope, token); throw; } }
    private bool Current(Guid id, string actor) => user.IsAuthenticated && user.UserId == actor && company.IsResolved && company.CompanyId == id && user.CanAccessCompany(id);
    private static PurchaseAdditionalCostViewV1 View(PurchaseAdditionalCostState value, Guid company, Guid order)
    {
        PurchaseAdditionalCost.Validate(value); if (value.CompanyId != company || value.OrderId != order) throw Failure(503);
        return new(value.CompanyId, value.CostId, "1", value.OrderId, Text(value.SourceOrderVersion), value.SourceOrderNumber, value.SourceKind.ToString(),
            value.AcceptanceId, value.SupplierPartyId, value.CompanyAccountId, value.BusinessDate, value.Money.CurrencyId, value.Money.CurrencyCode,
            value.Description, value.ExpenseType?.Id, value.ExpenseType is { } type ? Text(type.Version) : null, value.ExpenseType?.Name,
            Text(value.NetAmount), Text(value.Tax.VatPercent), Text(value.VatAmount), Text(value.GrossAmount), value.RecordedBy, value.Reason, value.RecordedAtUtc);
    }
    private static decimal Decimal(string text) => text is not null && text.Length <= 40
        && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value >= 0 && Text(value) == text
        ? value : throw new ArgumentException("Kanonik tutar gerekli.");
    private static string Text(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static PurchasingServiceException Failure(int status) => new(status, status == 503 ? "service_unavailable" : status == 401 ? "unauthenticated"
        : status == 403 ? "forbidden" : status == 404 ? "not_found" : "validation_failed");
}
public sealed class PurchaseAdditionalCostException(string code) : Exception(code) { public string Code { get; } = code; }
