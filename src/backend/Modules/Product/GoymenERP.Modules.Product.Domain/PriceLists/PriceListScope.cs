namespace GoymenERP.Modules.Product.Domain.PriceLists;

/// <summary>
/// SAT-D9 / DEC-PRD-011: whose price a list is. The four kinds are the price tier chain's vocabulary (agreement &gt;
/// customer-specific &gt; customer group &gt; company); the numbers are stored and never re-used.
/// <list type="bullet">
/// <item><see cref="Company"/>: the company-wide list (every list stored before MOD-02 is one).</item>
/// <item><see cref="Customer"/>: one customer's own list — the Cari party and its company account in the list's company
/// (DEC-CAR-002: the relationship lives on the company-scoped account).</item>
/// <item><see cref="CustomerGroup"/> and <see cref="Agreement"/>: no owner master exists yet (no Cari customer-group master,
/// no sales agreement record — SAT-D28 is a follow-up), so a list cannot be scoped to them: refused fail-closed, never
/// stored with an invented target.</item>
/// </list>
/// </summary>
public enum PriceListScopeKind
{
    Company = 1,
    CustomerGroup = 2,
    Customer = 3,
    Agreement = 4,
}

/// <summary>The list's scope, fixed when the list is created (a different scope is a different list).</summary>
public sealed record PriceListScope
{
    private PriceListScope(PriceListScopeKind kind, Guid? partyId, Guid? companyAccountId)
    {
        Kind = kind;
        PartyId = partyId;
        CompanyAccountId = companyAccountId;
    }

    public static PriceListScope Company { get; } = new(PriceListScopeKind.Company, null, null);

    public PriceListScopeKind Kind { get; }

    /// <summary>The Cari party of a <see cref="PriceListScopeKind.Customer"/> list; null otherwise.</summary>
    public Guid? PartyId { get; }

    /// <summary>The party's company account (in the list's company) of a <see cref="PriceListScopeKind.Customer"/> list; null otherwise.</summary>
    public Guid? CompanyAccountId { get; }

    public static PriceListScope Customer(Guid partyId, Guid companyAccountId)
    {
        if (partyId == Guid.Empty || companyAccountId == Guid.Empty)
        {
            throw new PriceListRuleException(PriceListRuleCodes.InvalidScope, "scope",
                "Cari özel liste bir cari ve o carinin şirket hesabını taşır.");
        }

        return new PriceListScope(PriceListScopeKind.Customer, partyId, companyAccountId);
    }

    /// <summary>
    /// The stored or requested scope, checked: Company carries no target; Customer carries both identities; the kinds whose
    /// owner master does not exist are refused (<see cref="PriceListRuleCodes.ScopeUnavailable"/>).
    /// </summary>
    public static PriceListScope From(PriceListScopeKind kind, Guid? partyId, Guid? companyAccountId) => kind switch
    {
        PriceListScopeKind.Company when partyId is null && companyAccountId is null => Company,
        PriceListScopeKind.Company => throw new PriceListRuleException(PriceListRuleCodes.InvalidScope, "scope",
            "Şirket geneli liste bir cari taşımaz."),
        PriceListScopeKind.Customer => Customer(partyId ?? Guid.Empty, companyAccountId ?? Guid.Empty),
        PriceListScopeKind.CustomerGroup or PriceListScopeKind.Agreement => throw new PriceListRuleException(
            PriceListRuleCodes.ScopeUnavailable, "scope.kind",
            "Bu kademenin sahibi kayıt (cari grubu / satış anlaşması) henüz yok; liste bu kapsamla açılmaz."),
        _ => throw new PriceListRuleException(PriceListRuleCodes.InvalidScope, "scope.kind", "Bilinmeyen liste kapsamı."),
    };

    /// <summary>Whether this list is the given customer's own list (both identities, exactly).</summary>
    public bool IsCustomer(Guid partyId, Guid companyAccountId) =>
        Kind == PriceListScopeKind.Customer && PartyId == partyId && CompanyAccountId == companyAccountId;
}
