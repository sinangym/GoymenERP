namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>
/// KIM-D35: whether the company's VKN has been entered. A company whose VKN is not known yet (the existing placeholder
/// record) carries no VKN and is marked "VKN girilmemiş"; a stand-in value such as ten repeated digits is never a VKN.
/// The mark is lifted only when a real VKN is approved through the maker-checker change flow (KIM-D28, KIM-S23).
/// </summary>
public enum OrganizationCompanyTaxIdStatusV1
{
    /// <summary>The company carries an approved VKN.</summary>
    Entered = 1,

    /// <summary>"VKN girilmemiş": no VKN is entered for the company yet.</summary>
    NotEntered = 2,
}

/// <summary>
/// The company's VKN status as a module that issues a VKN-bearing official document needs it. <paramref name="Vkn"/> is
/// the approved VKN when <paramref name="Status"/> is <see cref="OrganizationCompanyTaxIdStatusV1.Entered"/> and null
/// otherwise. The VKN is the company's own fiscal identity: callers print it on the document and never log it.
/// </summary>
public sealed record OrganizationCompanyTaxIdStateV1(
    Guid CompanyId,
    OrganizationCompanyTaxIdStatusV1 Status,
    string? Vkn)
{
    public bool IsTaxIdEntered => Status == OrganizationCompanyTaxIdStatusV1.Entered;
}

/// <summary>
/// KIM-D35 problem code for the modules that issue a VKN-bearing official document (invoice, e-document, dispatch note
/// and the like). While the company is marked "VKN girilmemiş" the attempt is refused with this code, before anything is
/// written; the caller answers it as a conflict (409) under its own problem details.
/// </summary>
public static class OrganizationCompanyTaxIdProblemCodesV1
{
    public const string TaxIdNotEntered = "organization_company_tax_id_not_entered";
}

/// <summary>
/// The Organization module read for other modules (not a user read): the resolved company's VKN status. It is
/// company-scoped by the server-resolved <c>ICompanyContext</c> and applies no user permission: the calling module has
/// already authorized its own command, and a caller's Organization grant must not decide that command. The tax-id screen
/// (<see cref="IOrganizationCompanyTaxIdService"/>) stays the user read.
/// <para>
/// An unknown company is null (not found). <paramref name="companyId"/> must be the resolved company: an unresolved or
/// different company context throws <see cref="OrganizationCompanyContextMismatchException"/>, never a guessed answer. A
/// failure of the source is not caught: the caller answers it as unavailable and issues nothing (fail closed).
/// </para>
/// </summary>
public interface IOrganizationCompanyTaxIdStatusReaderV1
{
    Task<OrganizationCompanyTaxIdStateV1?> ReadAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);
}
