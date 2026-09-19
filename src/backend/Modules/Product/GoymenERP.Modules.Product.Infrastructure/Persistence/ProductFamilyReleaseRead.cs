using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// The released family definition for the reads that must not fail on it (the Draft reference catalogue and
/// the options read): the well-formed release, or null — fail-closed, as before. A missing release
/// (<c>product_family_policy_unavailable</c>) is the normal state until Q6 and is not logged. Any other refusal —
/// above all <c>product_family_policy_invalid</c>, an operator fault in a loaded release — is still null here, but
/// is logged with its code (#334 P3-1), so a malformed release does not look like "no release" until an
/// eligibility call happens to reveal it. Only the code is logged: no release content, no user data.
/// </summary>
internal static class ProductFamilyReleaseRead
{
    public static async Task<ProductFamilyDefinitionV2?> WellFormedAsync(IProductFamilyDefinitionSourceV2 source,
        ILogger? logger, string reader, CancellationToken cancellationToken)
    {
        try
        {
            var definition = await source.GetAsync(cancellationToken);
            ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);
            return definition;
        }
        catch (ProductOperationException exception)
        {
            if (!string.Equals(exception.Code, UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode, StringComparison.Ordinal))
            {
                logger?.LogWarning(
                    "Product family release refused; the read continues without a release. Code={Code} Reader={Reader}",
                    exception.Code,
                    reader);
            }

            return null;
        }
    }
}
