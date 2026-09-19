namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>
/// A location's own state as another module needs it for its own command. It carries exactly what
/// Organization owns: the location's own active flag and whether it is an external (supplier) depot
/// (SINAN_KARARLARI_2026-08-31 §3). Its site's state is not part of it: no source decides that an
/// inactive site makes its locations unusable, so Organization does not answer that question.
/// </summary>
public sealed record OrganizationLocationStatusV1(Guid LocationId, bool IsActive, bool IsExternal);

/// <summary>
/// The Organization module read for other modules (not a user read). It is company-scoped by the
/// server-resolved <c>ICompanyContext</c> and applies no user permission or row scope: the calling
/// module has already authorized its own command, and a caller's Organization read grant must not
/// decide that command. <see cref="IOrganizationService.GetLocationAsync"/> stays the user read.
/// <para>
/// A location of another company, or an unknown id, is null (not found); nothing about another
/// company's location is returned. <paramref name="companyId"/> must be the resolved company: an
/// unresolved or different company context throws
/// <see cref="OrganizationCompanyContextMismatchException"/>, never a guessed answer. A failure of the
/// source is not caught: the caller answers it as unavailable.
/// </para>
/// </summary>
public interface IOrganizationLocationStatusReaderV1
{
    Task<OrganizationLocationStatusV1?> ReadAsync(
        Guid companyId,
        Guid locationId,
        CancellationToken cancellationToken = default);
}

/// <summary>The company given is not the server-resolved company context (or none is resolved).</summary>
public sealed class OrganizationCompanyContextMismatchException : InvalidOperationException
{
    public const string Code = "organization_company_context_mismatch";

    public OrganizationCompanyContextMismatchException()
        : base("Organizasyon okuması çözümlenmiş şirket bağlamı dışında istendi.")
    {
    }
}
