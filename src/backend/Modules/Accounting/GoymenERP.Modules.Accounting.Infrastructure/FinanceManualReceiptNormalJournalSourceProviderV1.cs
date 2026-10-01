using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Accounting.Infrastructure;

/// <summary>
/// Read-only cross-module adapter for the approved first normal-journal source. It consumes the
/// Finance owner's authorized contract and an exact approved Accounting binding; it writes neither
/// module and never infers an account, side, purpose or mapping.
/// </summary>
public sealed class FinanceManualReceiptNormalJournalSourceProviderV1(
    IManualReceiptService receipts,
    IFinancePermissionPort financePermissions,
    IApprovedNormalJournalSourceBindingCatalogV1 bindings,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlationContext)
    : INormalJournalDraftServerSourceProviderV1
{
    public async Task<NormalJournalDraftServerSourceV1?> ResolveAsync(
        Guid companyId, Guid sourceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || sourceId == Guid.Empty
            || !TryContext(companyId, out var actor)
            || !correlationContext.IsResolved)
        {
            return null;
        }

        var context = new FinanceContext(companyId, actor, correlationContext.CorrelationId,
            correlationContext.CausationId);
        var result = await receipts.DetailAuthorizedAsync(
            context, new SourceDetailRequest(sourceId), cancellationToken);
        if (result is not { Outcome: ReadOutcome.Found, Resource: { } detail }
            || !Eligible(detail, companyId, sourceId)
            || !TryContext(companyId, out var actorAfterRead)
            || !string.Equals(actor, actorAfterRead, StringComparison.Ordinal))
        {
            return null;
        }

        var source = detail.Source;
        var rule = await bindings.ResolveAsync(
            companyId,
            source.Id,
            NormalJournalSourceKindsV1.RecordedManualReceipt,
            source.Version,
            detail.Profile.ProfileId,
            detail.Profile.Version,
            source.CurrencyId,
            cancellationToken);
        if (rule is null || rule.Mirror.CompanyId != companyId
            || !TryContext(companyId, out var actorAfterBinding)
            || !string.Equals(actor, actorAfterBinding, StringComparison.Ordinal))
        {
            return null;
        }

        // Both owner permissions are re-read after the source and binding awaits. ReadSources is
        // deliberately last, leaving no data-owner await between the final fence and release.
        var amount = await financePermissions.CheckAsync(
            context, FinanceAction.ReadAmount, sourceId, cancellationToken);
        if (amount != PermissionDecision.Allowed || !SameContext(companyId, actor))
        {
            return null;
        }

        var read = await financePermissions.CheckAsync(
            context, FinanceAction.ReadSources, sourceId, cancellationToken);
        return read == PermissionDecision.Allowed && SameContext(companyId, actor)
            ? new NormalJournalDraftServerSourceV1(
                rule, source.CurrencyId, source.Version, detail.Profile.ProfileId, detail.Profile.Version)
            : null;
    }

    private bool TryContext(Guid companyId, out string actor)
    {
        actor = currentUser.UserId ?? string.Empty;
        return companyContext.IsResolved && companyContext.CompanyId == companyId
            && currentUser.IsAuthenticated && currentUser.CanAccessCompany(companyId)
            && Guid.TryParse(actor, out var actorId) && actorId != Guid.Empty;
    }

    private bool SameContext(Guid companyId, string actor) =>
        TryContext(companyId, out var currentActor)
        && string.Equals(actor, currentActor, StringComparison.Ordinal);

    private static bool Eligible(SourceDetail detail, Guid companyId, Guid sourceId)
    {
        var source = detail.Source;
        return source.Id == sourceId && source.CompanyId == companyId
            && source.Stage == SourceStage.Recorded && source.RecordedIdentityIsComplete(detail)
            && detail.ReversalSourceId is null && detail.ReversesSourceId is null
            && source.Amount is
            {
                Disclosure: AmountDisclosure.Visible,
                Money: { } money
            }
            && money.CurrencyId == source.CurrencyId
            && decimal.TryParse(money.Amount, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount)
            && amount > 0m;
    }
}

internal static class FinanceManualReceiptNormalJournalSourceChecksV1
{
    internal static bool RecordedIdentityIsComplete(this SourceSummary source, SourceDetail detail) =>
        source.CurrencyId != Guid.Empty && source.PartyId != Guid.Empty
        && source.CompanyAccountId != Guid.Empty && source.TreasuryAccountId != Guid.Empty
        && detail.RecordedAtUtc is not null
        && IsCanonical(source.Version)
        && IsCanonical(detail.Profile.ProfileId)
        && IsCanonical(detail.Profile.Version);

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);
}
