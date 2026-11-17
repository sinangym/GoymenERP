using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Accounting.Infrastructure;

/// <summary>
/// B-28 source-first route through the real approved Accounting package and Finance's authorized owner reads.
/// An expense item remains an expense item: no bank fee/interest/account classification is inferred from text.
/// A same-currency transfer is one source with two legs. Exchange differences are not invented by this adapter.
/// </summary>
public sealed class FinanceFinancialMovementNormalJournalSourceProviderV1(
    IApprovedNormalJournalSourceKindCatalogV1 kinds,
    FinanceManualReceiptNormalJournalSourceProviderV1 receipts,
    IExpensePaymentService expenses,
    ICashTransferService transfers,
    IApprovedNormalJournalSourceBindingCatalogV1 bindings,
    IFinancePermissionPort permissions,
    ICompanyContext company,
    ICurrentUser user,
    ICorrelationContext correlation) : INormalJournalDraftServerSourceProviderV1
{
    public async Task<NormalJournalDraftServerSourceV1?> ResolveAsync(Guid companyId, Guid sourceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || sourceId == Guid.Empty || !Current(companyId, out var actor) || !correlation.IsResolved) return null;
        var kind = await kinds.ResolveKindAsync(companyId, sourceId, cancellationToken);
        if (!Same(companyId, actor)) return null;
        if (kind == NormalJournalSourceKindsV1.RecordedManualReceipt)
            return await receipts.ResolveAsync(companyId, sourceId, cancellationToken);
        var context = new FinanceContext(companyId, actor, correlation.CorrelationId, correlation.CausationId);
        var fact = kind switch
        {
            NormalJournalSourceKindsV1.RecordedExpensePayment => Expense(await expenses.DetailAuthorizedAsync(context, sourceId, cancellationToken), companyId, sourceId),
            NormalJournalSourceKindsV1.RecordedCashTransfer => Transfer(await transfers.DetailAuthorizedAsync(context, sourceId, cancellationToken), companyId, sourceId),
            _ => null,
        };
        if (fact is null || !Same(companyId, actor)) return null;
        var rule = await bindings.ResolveAsync(companyId, sourceId, kind!, fact.Version, fact.Profile.ProfileId,
            fact.Profile.Version, fact.CurrencyId, cancellationToken);
        if (rule is null || rule.Mirror.CompanyId != companyId || !Same(companyId, actor)) return null;
        var permitted = await permissions.CheckAsync(context, fact.Action, sourceId, cancellationToken);
        return permitted == PermissionDecision.Allowed && Same(companyId, actor)
            ? new(rule, fact.CurrencyId, fact.Version, fact.Profile.ProfileId, fact.Profile.Version, fact.Amount) : null;
    }

    private static Fact? Expense(ReadResult<ExpensePaymentView> result, Guid company, Guid id)
    {
        if (result is not { Outcome: ReadOutcome.Found, Resource: { } view } || view.Id != id || view.CompanyId != company
            || view.Status != ExpensePaymentStatus.Recorded || !view.PaymentConfirmed || !Canonical(view.Version)
            || !Canonical(view.PreparedBy) || !Canonical(view.ApprovedBy) || view.ApprovedBy == view.PreparedBy
            || view.ApprovedAtUtc is null || !Canonical(view.RecordedBy) || view.RecordedAtUtc is null) return null;
        var source = view.Preparation;
        if (source is null || source.TreasuryAccountId == Guid.Empty || source.ExpenseItemId == Guid.Empty
            || !Enum.IsDefined(source.SourceKind) || source.OccurredOn == default || !Canonical(source.Description)
            || !ValidMoney(source.Money) || !ValidProfile(source.Profile)) return null;
        return new(source.Money.CurrencyId, view.Version, source.Profile, FinanceAction.ReadExpensePayments, source.Money.Amount);
    }

    private static Fact? Transfer(ReadResult<CashTransferView> result, Guid company, Guid id)
    {
        if (result is not { Outcome: ReadOutcome.Found, Resource: { } view } || view.Id != id || view.CompanyId != company
            || !view.TransferConfirmed || !Canonical(view.Version) || !Canonical(view.RecordedBy) || view.RecordedAtUtc == default) return null;
        var source = view.Preparation;
        if (source is null || source.Kind != CashTransferKind.SameCurrency || source.Rate is not null
            || !Enum.IsDefined(source.SourceKind) || !Enum.IsDefined(source.TargetKind) || source.SourceAccountId == Guid.Empty
            || source.TargetAccountId == Guid.Empty || source.SourceKind == source.TargetKind && source.SourceAccountId == source.TargetAccountId
            || source.OccurredOn == default || !ValidMoney(source.SourceMoney) || !ValidMoney(source.TargetMoney)
            || source.SourceMoney != source.TargetMoney || !ValidProfile(source.SourceProfile) || !ValidProfile(source.TargetProfile)) return null;
        // This exact immutable source version contains BOTH profile proofs; the approved binding names the real source-leg profile.
        return new(source.SourceMoney.CurrencyId, view.Version, source.SourceProfile, FinanceAction.ReadCashTransfers, source.SourceMoney.Amount);
    }

    private bool Current(Guid companyId, out string actor)
    {
        actor = user.UserId ?? string.Empty;
        return company.IsResolved && company.CompanyId == companyId && user.IsAuthenticated && user.CanAccessCompany(companyId)
            && Guid.TryParse(actor, out var id) && id != Guid.Empty;
    }
    private bool Same(Guid companyId, string actor) => Current(companyId, out var current) && current == actor;
    private static bool Canonical(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim();
    private static bool ValidProfile(ProfileReference? profile) => profile is not null && Canonical(profile.ProfileId) && Canonical(profile.Version);
    private static bool ValidMoney(MoneyText? money) => money is not null && money.CurrencyId != Guid.Empty
        && Canonical(money.Amount) && decimal.TryParse(money.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount > 0m;
    private sealed record Fact(Guid CurrencyId, string Version, ProfileReference Profile, FinanceAction Action, string Amount);
}
