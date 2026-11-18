using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// F018 plan §L3 (FNS-D27 (2), FNS-D31 (4), FNS-D58): the read behind the Finance panel of the Cari card. Untracked; it
/// creates nothing (no attempt, audit or version effect). Every figure comes from the calculators the Sales risk port
/// (<see cref="FinanceSalesPartyRiskReadPortV1"/>) and the collateral master (<see cref="FinancePartyCollateralFigures"/>)
/// already use, so the panel and the risk input never disagree; nothing is computed on the screen.
/// <list type="bullet">
/// <item>The panel opens under the credit limit read OR the collateral read authority; with neither it is Denied (or
/// Unavailable when an authority cannot be measured). The party must be an account of the server-resolved company the
/// caller can see (<see cref="IFinancePartyAccountPort"/>).</item>
/// <item>Credit limit: under the credit limit read. Open receivables: under the open item read and its amount read.
/// Held and weighted collateral: under the collateral read. Documentary risk: under the collateral read and the
/// customer instrument read and amount read (as on the collateral account). Utilization: under the open receivables'
/// and the collateral's authorities. A figure the caller may not read is Withheld and discloses no amount.</item>
/// <item>Gross risk and limit headroom are NOT answered: they are the Sales risk calculator's (F018 §L3).</item>
/// </list>
/// </summary>
public sealed class FinancePartyCreditPanelService : IFinancePartyCreditPanelReader
{
    public const string CreditLimitWithheld = "finance_credit_limit_withheld";
    public const string OpenReceivablesWithheld = "finance_open_receivables_withheld";
    public const string CollateralWithheld = "finance_collateral_withheld";
    public const string UtilizationWithheld = "finance_collateral_utilization_withheld";

    private readonly FinanceDbContext dbContext;
    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly IFinancePermissionPort permissions;
    private readonly IFinancePartyAccountPort parties;
    private readonly IFinanceCollateralTypeCatalogReader collateralTypes;
    private readonly TimeProvider timeProvider;
    private readonly IFinanceCollateralPolicyReaderV1? collateralPolicies;
    private readonly FinanceSalesPartyRiskReadPortV1 risk;

    public FinancePartyCreditPanelService(FinanceDbContext dbContext, ICompanyContext companyContext,
        ICurrentUser currentUser, IFinancePermissionPort permissions, IFinancePartyAccountPort parties,
        IFinanceCollateralTypeCatalogReader collateralTypes, TimeProvider timeProvider, IFinanceCollateralPolicyReaderV1? collateralPolicies = null)
    {
        this.dbContext = dbContext;
        this.companyContext = companyContext;
        this.currentUser = currentUser;
        this.permissions = permissions;
        this.parties = parties;
        this.collateralTypes = collateralTypes;
        this.timeProvider = timeProvider;
        this.collateralPolicies = collateralPolicies;
        risk = new FinanceSalesPartyRiskReadPortV1(dbContext, companyContext, collateralTypes, timeProvider, collateralPolicies);
    }

    public async Task<ReadResult<PartyCreditPanelView>> GetAsync(FinanceContext context, Guid partyId,
        Guid companyAccountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await PanelFenceAsync(context, cancellationToken) is { } outcome)
        {
            return new ReadResult<PartyCreditPanelView>(outcome, null);
        }

        if (partyId == Guid.Empty || companyAccountId == Guid.Empty)
        {
            return new ReadResult<PartyCreditPanelView>(ReadOutcome.NotFound, null);
        }

        var party = await parties.GetAuthorizedAsync(context, partyId, companyAccountId, cancellationToken);
        if (party.Outcome != ReadOutcome.Found || party.Resource is not { } account)
        {
            return new ReadResult<PartyCreditPanelView>(
                party.Outcome is ReadOutcome.Denied or ReadOutcome.NotFound ? party.Outcome : ReadOutcome.Unavailable, null);
        }

        if (account.CompanyId != context.CompanyId || account.PartyId != partyId
            || account.CompanyAccountId != companyAccountId)
        {
            return new ReadResult<PartyCreditPanelView>(ReadOutcome.NotFound, null);
        }

        var mayReadLimit = await AllowedAsync(context, cancellationToken, FinanceAction.ReadCreditLimits);
        var mayReadCollateral = await AllowedAsync(context, cancellationToken, FinanceAction.ReadCollaterals);
        var mayReadReceivables = await AllowedAsync(context, cancellationToken, FinanceAction.ReadOpenItems,
            FinanceAction.ReadOpenItemAmount);
        var mayReadInstruments = mayReadCollateral && await AllowedAsync(context, cancellationToken,
            FinanceAction.ReadCustomerInstruments, FinanceAction.ReadCustomerInstrumentAmount);

        var creditLimit = mayReadLimit
            ? LimitView(await risk.CreditLimitAsync(context.CompanyId, partyId, companyAccountId, cancellationToken))
            : Withheld(CreditLimitWithheld);
        var receivablesFigure = mayReadReceivables
            ? ReceivablesFigure(await risk.OpenReceivablesAsync(context.CompanyId, partyId, companyAccountId,
                cancellationToken))
            : null;

        // FNS-D75: the panel is dated on the company business day; without it nothing is dated by a UTC guess.
        if (FinancePartyCollateralService.ReadDate(timeProvider) is not { } asOf)
        {
            return new ReadResult<PartyCreditPanelView>(ReadOutcome.Unavailable, null);
        }

        FinancePartyCollateralFigures.Figure? held = null;
        FinancePartyCollateralFigures.Figure? weighted = null;
        if (mayReadCollateral)
        {
            (held, weighted) = await CollateralAsync(context.CompanyId, partyId, companyAccountId, asOf, cancellationToken);
        }

        var documentary = mayReadInstruments
            ? View(FinancePartyCollateralFigures.DocumentaryRisk(await FinancePartyCollateralService.PortfolioAsync(
                dbContext, context.CompanyId, companyAccountId, cancellationToken)))
            : Withheld(FinancePartyCollateralFigures.DocumentaryRiskWithheld);

        var utilization = receivablesFigure is { } receivables && weighted is { } counted
            ? View(FinancePartyCollateralFigures.Utilization(receivables, counted))
            : Withheld(UtilizationWithheld);

        if (await PanelFenceAsync(context, cancellationToken) is { } again)
        {
            return new ReadResult<PartyCreditPanelView>(again, null);
        }

        return new ReadResult<PartyCreditPanelView>(ReadOutcome.Found, new PartyCreditPanelView(
            context.CompanyId, partyId, companyAccountId, asOf,
            creditLimit,
            receivablesFigure is { } open ? View(open) : Withheld(OpenReceivablesWithheld),
            held is { } gross ? View(gross) : Withheld(CollateralWithheld),
            weighted is { } count ? View(count) : Withheld(CollateralWithheld),
            utilization,
            documentary));
    }

    /// <summary>
    /// The held received collaterals of the account: at their amount, and at the Settings rate of their type read now. A
    /// collateral of another party on this account is never a guess: both figures are unavailable.
    /// </summary>
    private async Task<(FinancePartyCollateralFigures.Figure Held, FinancePartyCollateralFigures.Figure Weighted)>
        CollateralAsync(Guid companyId, Guid partyId, Guid companyAccountId, DateOnly asOf,
            CancellationToken cancellationToken)
    {
        var rows = await FinancePartyCollateralService.HeldReceivedAsync(dbContext, companyId, companyAccountId,
            cancellationToken);
        if (rows.Any(row => row.PartyId != partyId))
        {
            var unreadable = FinancePartyCollateralFigures.Figure.Unavailable(FinancePartyCollateralFigures.CollateralUnreadable);
            return (unreadable, unreadable);
        }

        var inputs = rows.Select(FinancePartyCollateralService.HeldInput).ToList();
        var catalog = inputs.Any(input => FinancePartyCollateralFigures.IsInForce(input.ValidFrom, input.ValidUntil, asOf))
            ? await collateralTypes.ListAsync(companyId, asOf, cancellationToken)
            : null;
        return (FinancePartyCollateralFigures.Gross(inputs, asOf),
            await FinanceCollateralPolicyFigures.WeighAsync(companyId, inputs, catalog, collateralPolicies, asOf, cancellationToken));
    }

    /// <summary>
    /// Company/actor still the request's, then the credit limit read or the collateral read. Both refused is Denied; an
    /// authority that cannot be measured (and no other allowing) is Unavailable.
    /// </summary>
    private async Task<ReadOutcome?> PanelFenceAsync(FinanceContext context, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return ReadOutcome.Denied;
        }

        var limit = await permissions.CheckAsync(context, FinanceAction.ReadCreditLimits, null, cancellationToken);
        var collateral = limit == PermissionDecision.Allowed
            ? PermissionDecision.Allowed
            : await permissions.CheckAsync(context, FinanceAction.ReadCollaterals, null, cancellationToken);
        if (limit != PermissionDecision.Allowed && collateral != PermissionDecision.Allowed)
        {
            return limit == PermissionDecision.Denied && collateral == PermissionDecision.Denied
                ? ReadOutcome.Denied
                : ReadOutcome.Unavailable;
        }

        return SameContext(context) ? null : ReadOutcome.Denied;
    }

    /// <summary>Every named action allowed now; anything else (denied or not measurable) withholds the figure.</summary>
    private async Task<bool> AllowedAsync(FinanceContext context, CancellationToken cancellationToken,
        params FinanceAction[] actions)
    {
        foreach (var action in actions)
        {
            if (await permissions.CheckAsync(context, action, null, cancellationToken) != PermissionDecision.Allowed)
            {
                return false;
            }
        }

        return true;
    }

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    /// <summary>
    /// The credit limit as the Sales risk port answers it: Available keeps its exact amount, NoSource (no approved
    /// limit, or an approved removal) is NotSet — never zero, never unlimited — and Unavailable keeps its reason.
    /// </summary>
    private static PartyCreditPanelFigureView LimitView(SalesPartyRiskFigureV1 figure) => figure.State switch
    {
        SalesPartyRiskFigureStateV1.Available => new PartyCreditPanelFigureView(PartyCreditPanelFigureState.Available,
            [.. figure.Amounts.Select(amount => new PartyCreditPanelAmountView(amount.CurrencyId,
                amount.Amount.ToString(CultureInfo.InvariantCulture)))], null),
        SalesPartyRiskFigureStateV1.NoSource => new PartyCreditPanelFigureView(PartyCreditPanelFigureState.NotSet, [],
            figure.ReasonCode ?? FinanceSalesPartyRiskReadPortV1.CreditLimitNotSet),
        _ => new PartyCreditPanelFigureView(PartyCreditPanelFigureState.Unavailable, [],
            figure.ReasonCode ?? FinanceSalesPartyRiskReadPortV1.CreditLimitUnreadable),
    };

    /// <summary>
    /// The open receivables as the calculator's figure (the utilization's input): Available keeps the exact amounts as
    /// invariant text; anything else is unavailable with its reason (open receivables have no "not set").
    /// </summary>
    private static FinancePartyCollateralFigures.Figure ReceivablesFigure(SalesPartyRiskFigureV1 figure) =>
        figure.State == SalesPartyRiskFigureStateV1.Available
            ? new FinancePartyCollateralFigures.Figure(true,
                [.. figure.Amounts.Select(amount => new FinancePartyCollateralFigures.CurrencyAmount(amount.CurrencyId,
                    amount.Amount.ToString(CultureInfo.InvariantCulture)))], null)
            : FinancePartyCollateralFigures.Figure.Unavailable(
                figure.ReasonCode ?? FinanceSalesPartyRiskReadPortV1.OpenItemUnreadable);

    private static PartyCreditPanelFigureView View(FinancePartyCollateralFigures.Figure figure) =>
        figure.IsAvailable
            ? new PartyCreditPanelFigureView(PartyCreditPanelFigureState.Available,
                [.. figure.Amounts.Select(amount => new PartyCreditPanelAmountView(amount.CurrencyId, amount.Amount))], null)
            : new PartyCreditPanelFigureView(PartyCreditPanelFigureState.Unavailable, [],
                figure.ReasonCode ?? FinancePartyCollateralFigures.CollateralUnreadable);

    private static PartyCreditPanelFigureView Withheld(string reasonCode) =>
        new PartyCreditPanelFigureView(PartyCreditPanelFigureState.Withheld, [], reasonCode);
}
