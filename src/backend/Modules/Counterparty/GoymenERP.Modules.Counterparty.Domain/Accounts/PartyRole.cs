namespace GoymenERP.Modules.Counterparty.Domain.Accounts;

[Flags]
public enum PartyRole
{
    None = 0,
    Customer = 1,
    Supplier = 2,
    Tolling = 4,
    Service = 8,
}
