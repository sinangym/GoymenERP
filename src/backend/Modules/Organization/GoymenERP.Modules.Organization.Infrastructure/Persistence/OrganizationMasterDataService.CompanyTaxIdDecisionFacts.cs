using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

internal sealed partial class OrganizationMasterDataService
{
    // Reuse the existing native fingerprint type and hash, including its original property order.
    internal static string TaxIdDecisionPayloadHash(Guid companyId, Guid requestId, DecideCompanyTaxIdChange command) =>
        IdempotencyHash.ComputePayload(new DecideCompanyTaxIdChangeFingerprint(companyId, requestId, command.Decision,
            command.ExpectedVersion, string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim()));
    internal static string TaxIdOriginalResultDigest(CommandResponseSnapshot response) => TaxIdPurposeDigest(response);
}
