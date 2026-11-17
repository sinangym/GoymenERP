using System.Globalization;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.InventoryRuntime;

/// <summary>SAT-D336: physical owner proof only; never exception messages or reservation-only advice.</summary>
internal static class InventoryPhysicalStockRefusalProblem
{
    private static readonly Regex DetailPattern = new(
        @"\ASistemde (-?(?:0|[1-9][0-9]{0,14})(?:\.[0-9]{0,2}[1-9])?) kg var, ((?:0|[1-9][0-9]{0,14})(?:\.[0-9]{0,2}[1-9])?) kg istendi\.\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));

    internal static string? Apply(int statusCode, Exception exception, IDictionary<string, object?> extensions,
        string? recordedDetail = null)
    {
        if (statusCode != StatusCodes.Status409Conflict) return null;
        var evidence = InventoryNegativeStockAttemptEvidenceException.Find(exception);
        if (evidence is null) return CanonicalDetailOrNull(recordedDetail);
        var shortfall = evidence.Shortfall;
        if (evidence.MovementId == Guid.Empty || shortfall.LineNumber is not >= 1) return null;
        var available = shortfall.Available.Value;
        var requested = shortfall.Requested.Value;
        var availableText = available.ToString("0.###", CultureInfo.InvariantCulture);
        var requestedText = requested.ToString("0.###", CultureInfo.InvariantCulture);
        var detail = CanonicalDetailOrNull($"Sistemde {availableText} kg var, {requestedText} kg istendi.");
        if (detail is null) return null;

        extensions["stockShortfall"] = new
        {
            movementId = evidence.MovementId,
            labelId = shortfall.Position.LabelId,
            productId = shortfall.Position.ProductId,
            locationId = shortfall.Position.LocationId,
            requestedKg = requestedText,
            availableKg = availableText,
            missingKg = (requested - available).ToString("0.###", CultureInfo.InvariantCulture),
        };
        return detail;
    }

    internal static string? CanonicalDetailOrNull(string? detail)
    {
        if (detail is null || detail.Length > 128) return null;
        var match = DetailPattern.Match(detail);
        return match.Success && match.Groups[1].Value != "-0" && match.Groups[2].Value != "0" ? detail : null;
    }
}
