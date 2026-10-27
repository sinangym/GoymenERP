namespace GoymenERP.Modules.Product.Domain.PriceLists;

/// <summary>The SAT-D9 tiers in chain order: the first tier that prices the line wins.</summary>
public enum PriceTier
{
    Agreement = 1,
    Customer = 2,
    CustomerGroup = 3,
    Company = 4,
}

/// <summary>What the chain found at one tier.</summary>
public enum PriceTierStatus
{
    /// <summary>Exactly one list of the tier prices the line: the chain stops here.</summary>
    Matched = 1,

    /// <summary>Several lists of the tier price the line: the chain stops here and no list is chosen for the person.</summary>
    Ambiguous = 2,

    /// <summary>The tier was asked and no list of it prices the line: the chain goes on.</summary>
    NoMatch = 3,

    /// <summary>The tier needs an input the caller did not give (no customer): the chain goes on.</summary>
    NotRequested = 4,

    /// <summary>The tier's owner master does not exist yet, so no list can carry it: the chain goes on.</summary>
    Unavailable = 5,

    /// <summary>A higher tier already answered.</summary>
    NotReached = 6,
}

public enum PriceTierChainOutcome
{
    /// <summary>One list of the highest answering tier prices the line.</summary>
    Resolved = 1,

    /// <summary>Several lists of the highest answering tier price the line; the candidates are returned, none is picked.</summary>
    Ambiguous = 2,

    /// <summary>No tier prices the line. No price is implied (never zero, never an older version).</summary>
    NoPrice = 3,
}

/// <summary>One list that prices the line on the date: its tier, the list, the version that prices the date and the line.</summary>
public sealed record PriceTierCandidate(PriceTier Tier, PriceList List, PriceListVersion Version, PriceListLine Line);

public sealed record PriceTierEvaluation(PriceTier Tier, PriceTierStatus Status, string? Reason);

/// <summary>The customer a line is priced for: the Cari party and its company account in the list's company.</summary>
public sealed record PriceTierCustomer(Guid PartyId, Guid CompanyAccountId);

public sealed record PriceTierChainResult(PriceTierChainOutcome Outcome, DateOnly On, PriceTier? MatchedTier,
    IReadOnlyList<PriceTierCandidate> Candidates, IReadOnlyList<PriceTierEvaluation> Tiers);

/// <summary>
/// SAT-D9 (user decision 2026-10-25, replaces ASSUMPTION-PRL-4): the list price of a line is found automatically along
/// agreement &gt; customer-specific &gt; customer group &gt; company. Rules:
/// <list type="bullet">
/// <item>Only the context company's Sales-channel, VAT-excluded lists in the exact currency count; a list answers through
/// the one published version that prices the business date (<see cref="PriceList.ResolveOn"/>) and its exact quantity tier.</item>
/// <item>A customer-specific list answers only for its own customer (party AND company account); another customer's list
/// never answers, at any tier.</item>
/// <item>The first tier with an answer stops the chain. Several lists in that tier are <see cref="PriceTierChainOutcome.Ambiguous"/>:
/// which one wins is not decided, so the candidates are returned and none is picked (the person chooses, as before).</item>
/// <item>The agreement and customer-group tiers have no owner master yet: they are reported
/// <see cref="PriceTierStatus.Unavailable"/> and the chain goes on — no list can carry them, so nothing is skipped over.</item>
/// <item>No answer is <see cref="PriceTierChainOutcome.NoPrice"/>: no zero, no adjacent tier, no older version.</item>
/// </list>
/// </summary>
public static class PriceTierChain
{
    public const string AgreementOwnerMissing = "agreement_owner_missing";
    public const string CustomerGroupOwnerMissing = "customer_group_owner_missing";
    public const string CustomerNotGiven = "customer_not_given";

    public static PriceTierChainResult Resolve(IEnumerable<PriceList> lists, Guid companyId, Guid productId, Guid priceUnitId,
        decimal quantity, string currencyCode, DateOnly on, PriceTierCustomer? customer)
    {
        ArgumentNullException.ThrowIfNull(lists);
        var answers = new List<PriceTierCandidate>();
        foreach (var list in lists.OrderBy(list => list.Name, StringComparer.Ordinal).ThenBy(list => list.Id))
        {
            if (companyId == Guid.Empty || list.CompanyId != companyId || list.Channel != PriceListChannel.Sales
                || list.VatBasis != PriceListVatBasis.ExcludingVat || !string.Equals(list.CurrencyCode, currencyCode, StringComparison.Ordinal)
                || TierOf(list, customer) is not { } tier
                || list.ResolveOn(on) is not { } version
                || version.LineFor(productId, priceUnitId, quantity) is not { } line)
            {
                continue;
            }

            answers.Add(new PriceTierCandidate(tier, list, version, line));
        }

        var evaluations = new List<PriceTierEvaluation>();
        PriceTier? matched = null;
        IReadOnlyList<PriceTierCandidate> candidates = [];
        foreach (var tier in (PriceTier[])[PriceTier.Agreement, PriceTier.Customer, PriceTier.CustomerGroup, PriceTier.Company])
        {
            if (matched is not null)
            {
                evaluations.Add(new(tier, PriceTierStatus.NotReached, null));
                continue;
            }

            switch (tier)
            {
                case PriceTier.Agreement:
                    evaluations.Add(new(tier, PriceTierStatus.Unavailable, AgreementOwnerMissing));
                    continue;
                case PriceTier.CustomerGroup:
                    evaluations.Add(new(tier, PriceTierStatus.Unavailable, CustomerGroupOwnerMissing));
                    continue;
                case PriceTier.Customer when customer is null:
                    evaluations.Add(new(tier, PriceTierStatus.NotRequested, CustomerNotGiven));
                    continue;
            }

            var found = answers.Where(answer => answer.Tier == tier).ToList();
            if (found.Count == 0)
            {
                evaluations.Add(new(tier, PriceTierStatus.NoMatch, null));
                continue;
            }

            matched = tier;
            candidates = found;
            evaluations.Add(new(tier, found.Count == 1 ? PriceTierStatus.Matched : PriceTierStatus.Ambiguous, null));
        }

        var outcome = candidates.Count switch
        {
            0 => PriceTierChainOutcome.NoPrice,
            1 => PriceTierChainOutcome.Resolved,
            _ => PriceTierChainOutcome.Ambiguous,
        };
        return new PriceTierChainResult(outcome, on, matched, candidates, evaluations);
    }

    private static PriceTier? TierOf(PriceList list, PriceTierCustomer? customer) => list.Scope.Kind switch
    {
        PriceListScopeKind.Company => PriceTier.Company,
        PriceListScopeKind.Customer when customer is not null && list.Scope.IsCustomer(customer.PartyId, customer.CompanyAccountId) =>
            PriceTier.Customer,
        _ => null,
    };
}
