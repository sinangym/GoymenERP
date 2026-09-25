using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using ProductContracts = GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// Host-owned translation from Purchasing's command-time consumer port to Product's owner contract.
/// It grants nothing and never turns an owner failure into a positive selection.
/// </summary>
public sealed class ProductPurchaseEligibilityPort(ProductContracts.IProductPurchaseEligibilityV1 products)
    : IProductPurchaseEligibilityPortV1
{
    public async Task<ProductPurchaseEligibilityAnswerV1> EvaluateAsync(
        ProductPurchaseSelectionV1 selection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var answer = await products.EvaluateAsync(
                new ProductContracts.ProductPurchaseSelectionV1(
                    selection.ProductId,
                    selection.ExpectedProductVersion,
                    selection.UnitId,
                    selection.EffectiveOn),
                cancellationToken);

            if (answer.Snapshot is { } snapshot
                && answer.Errors.Count == 0
                && !string.IsNullOrWhiteSpace(snapshot.ProductVersion))
            {
                return new(ProductPurchaseEligibilityOutcomeV1.Eligible, snapshot.ProductVersion, []);
            }

            if (answer.Snapshot is null && answer.Errors.Count > 0)
            {
                return new(
                    ProductPurchaseEligibilityOutcomeV1.Refused,
                    null,
                    answer.Errors.Select(error => new PurchasingFieldError(error.Field, error.Code)).ToArray());
            }

            return Unavailable();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProductContracts.ProductOperationException exception) when (
            exception.StatusCode == StatusCodes.Status403Forbidden)
        {
            return Denied();
        }
        catch (SettingsPrerequisiteException exception) when (
            string.Equals(exception.Code, "settings_access_denied", StringComparison.Ordinal))
        {
            return Denied();
        }
        catch (Exception)
        {
            return Unavailable();
        }
    }

    private static ProductPurchaseEligibilityAnswerV1 Denied() =>
        new(ProductPurchaseEligibilityOutcomeV1.Denied, null, []);

    private static ProductPurchaseEligibilityAnswerV1 Unavailable() =>
        new(ProductPurchaseEligibilityOutcomeV1.Unavailable, null, []);
}
