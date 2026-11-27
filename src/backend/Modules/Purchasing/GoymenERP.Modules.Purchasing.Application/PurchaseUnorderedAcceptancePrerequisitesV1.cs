using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>Composition of existing master owners only. Missing document-line/date proof remains unavailable;
/// no order, admission, approval, stock or financial event is manufactured by validation.</summary>
public sealed class PurchaseUnorderedAcceptancePrerequisitesV1(ICompanyContext company,
    IPurchasingPartyAccountPortV1 parties, ISettingsPrerequisiteProvider profiles, ISettingsReferenceListReaderV1 references,
    PurchaseUnitVerifier units, IProductPurchaseEligibilityPortV1? products = null,
    IPurchaseUnorderedAcceptanceEvidenceReadV1? evidence = null) : IPurchaseUnorderedAcceptancePrerequisitesV1
{
    public async Task ValidateAsync(Guid companyId, RecordPurchaseUnorderedAcceptanceV1 request,
        Func<CancellationToken, Task> requireStill, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(requireStill);
        var frozen = PurchaseUnorderedAcceptanceOwnerContractV1.Freeze(request);
        await Fence();
        if (!PurchaseUnorderedAcceptanceOwnerContractV1.Valid(frozen)) throw Failure(400);
        if (evidence is null) throw Failure(503);
        var source = await Read(() => evidence.ReadAsync(companyId, frozen, _ => Fence(), token), answer =>
            answer?.Facts is not { } facts ? answer! : answer with { Facts = facts with
            { ControlEvidence = facts.ControlEvidence is null ? null! : Array.AsReadOnly(facts.ControlEvidence.ToArray()) } });
        if (source is null || !Enum.IsDefined(source.Outcome) || source.Outcome != PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Found && source.Facts is not null)
            throw Failure(503);
        if (source.Outcome != PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Found)
            throw Failure(source.Outcome == PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Denied ? 403
                : source.Outcome == PurchaseUnorderedAcceptanceEvidenceOutcomeV1.NotFound ? 404 : 503);
        if (source.Facts is not { BusinessDate: { } day } proof || proof.CompanyId != companyId
            || proof.AcceptanceId != frozen.AcceptanceId || proof.AcceptanceLineId != frozen.AcceptanceLineId
            || proof.SupplierPartyId != frozen.SupplierPartyId || proof.CompanyAccountId != frozen.CompanyAccountId
            || proof.Subject != frozen.Subject || proof.UnitId != frozen.UnitId || proof.AcceptedQuantity != frozen.AcceptedQuantity
            || proof.OccurredAtUtc != frozen.OccurredAtUtc || proof.OccurredAtUtc.Offset != TimeSpan.Zero
            || !SameEvidence(proof.Evidence, frozen.Evidence) || proof.ControlEvidence is null
            || proof.ControlEvidence.Count != frozen.ControlEvidence.Count
            || proof.ControlEvidence.Where((value, index) => !SameEvidence(value, frozen.ControlEvidence[index])).Any()) throw Failure(503);

        var supplier = await Read(() => parties.GetAuthorizedAsync(frozen.SupplierPartyId, frozen.CompanyAccountId, token));
        if (supplier is null || !Enum.IsDefined(supplier.Outcome)) throw Failure(503);
        if (supplier.Outcome != PurchasingPartyAccountOutcomeV1.Found)
            throw Failure(supplier.Outcome == PurchasingPartyAccountOutcomeV1.Denied ? 403
                : supplier.Outcome == PurchasingPartyAccountOutcomeV1.NotFound ? 404 : 503);
        if (supplier.Account is not { } account || account.CompanyId != companyId || account.PartyId != frozen.SupplierPartyId
            || account.CompanyAccountId != frozen.CompanyAccountId) throw Failure(503);
        if (!account.PartyIsActive || !account.AccountIsActive || !account.AccountHasSupplierRole) throw Failure(422);
        if (account.PartyVersion != frozen.ExpectedPartyVersion || account.AccountVersion != frozen.ExpectedAccountVersion) throw Failure(409);

        if (frozen.Subject is PurchaseUnorderedGoodsSubjectV1 goods)
        {
            if (products is null) throw Failure(503);
            var product = await Read(() => products.EvaluateAsync(new(goods.ProductId, goods.ProductVersion, frozen.UnitId, day), token),
                answer => answer is null ? null! : answer with { Errors = answer.Errors is null ? null! : Array.AsReadOnly(answer.Errors.ToArray()) });
            if (product is null || !Enum.IsDefined(product.Outcome)) throw Failure(503);
            if (product.Outcome == ProductPurchaseEligibilityOutcomeV1.Denied) throw Failure(403);
            if (product.Outcome == ProductPurchaseEligibilityOutcomeV1.Unavailable) throw Failure(503);
            if (product.Outcome == ProductPurchaseEligibilityOutcomeV1.Refused)
            {
                if (product.Errors is not { Count: > 0 } || product.Errors.Any(x => x is null || string.IsNullOrWhiteSpace(x.Field) || string.IsNullOrWhiteSpace(x.Code))) throw Failure(503);
                throw Failure(product.Errors.Any(x => x.Code == "version_conflict") ? 409 : 422);
            }
            if (product.ProductVersion != goods.ProductVersion || product.Errors is not { Count: 0 }) throw Failure(503);
            _ = PurchaseInvoiceAcceptanceSelectionContractV1.TryQuantity(frozen.AcceptedQuantity, false, out var quantity);
            // Keep the existing goods kg/piece policy and scale; never reinterpret another unit as kg.
            var verified = await Read(() => units.VerifyAsync(companyId, frozen.UnitId, quantity, day, token));
            if (verified?.Unit is not { } unit) throw Failure(verified?.Code == "settings_access_denied" ? 403
                : verified?.Code is "settings_provider_unavailable" or "settings_profile_invalid" ? 503 : 422);
            if (unit.ProfileId != frozen.UnitProfile.ProfileId || unit.ProfileVersion != Text(frozen.UnitProfile.Version)) throw Failure(409);
        }
        else if (frozen.Subject is PurchaseUnorderedServiceSubjectV1 service)
        {
            var lookup = await Read(() => references.GetAsync(companyId, SettingsReferenceListKindV1.ServiceType, service.ServiceTypeId, token));
            if (lookup is null || !Enum.IsDefined(lookup.Outcome) || lookup.Outcome == SettingsReferenceListLookupOutcomeV1.Unavailable) throw Failure(503);
            if (lookup.Outcome != SettingsReferenceListLookupOutcomeV1.Found || lookup.Entry is not { Active: true } entry) throw Failure(422);
            if (entry.Id != service.ServiceTypeId) throw Failure(503);
            if (Text(entry.Version) != service.ServiceTypeVersion) throw Failure(409);
            var unit = await Read(() => profiles.GetUnitProfileAsync(frozen.UnitId, day, token));
            if (unit is null || unit.CompanyId != companyId || unit.UnitId != frozen.UnitId || unit.Profile is not { ProfileId: var profileId, Version: > 0 }
                || profileId == Guid.Empty || unit.Scale is < 0 or > 6 || string.IsNullOrWhiteSpace(unit.UnitCode)
                || !unit.IsActive || unit.EffectiveFrom > day || unit.EffectiveThrough is { } through && through < day) throw Failure(503);
            if (unit.Profile != frozen.UnitProfile) throw Failure(409);
            _ = PurchaseInvoiceAcceptanceSelectionContractV1.TryQuantity(frozen.AcceptedQuantity, false, out var quantity);
            if (decimal.Round(quantity, unit.IsKilogram ? Math.Min(3, unit.Scale) : unit.Scale) != quantity) throw Failure(422);
        }
        else throw Failure(400);
        await Fence();

        async Task Fence()
        {
            token.ThrowIfCancellationRequested();
            if (!Current()) throw Failure(403);
            await requireStill(token); token.ThrowIfCancellationRequested();
            if (!Current()) throw Failure(403);
        }
        bool Current() => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId;
        async Task<T> Read<T>(Func<Task<T>> read, Func<T, T>? capture = null)
        {
            await Fence(); T result;
            try { result = await read(); if (capture is not null) result = capture(result); }
            catch (OperationCanceledException) { throw; }
            catch (SettingsPrerequisiteException error) { await Fence(); throw Failure(error.Code == "settings_access_denied" ? 403 : 503); }
            catch (PurchasingServiceException) { await Fence(); throw; }
            catch { await Fence(); throw Failure(503); }
            await Fence(); return result;
        }
    }

    private static bool SameEvidence(PurchaseAcceptanceDocumentEvidenceV1? left, PurchaseAcceptanceDocumentEvidenceV1 right) =>
        left is not null && left.DocumentId == right.DocumentId && left.DocumentVersion == right.DocumentVersion
        && string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase) && left.SourceLineId == right.SourceLineId;
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static PurchasingServiceException Failure(int status) => new(status,
        status == 403 ? "forbidden" : status == 404 ? "not_found" : status == 409 ? PurchasingProblemCodesV1.VersionConflict
            : status == 422 ? "validation_failed" : status == 400 ? "validation_failed" : "service_unavailable");
}
