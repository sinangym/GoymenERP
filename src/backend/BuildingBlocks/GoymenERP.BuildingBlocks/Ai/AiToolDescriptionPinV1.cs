using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoymenERP.BuildingBlocks.Ai;

/// <summary>
/// WP-A6 (DALGA_PLANI_v2 A.6 "araç açıklaması sürüm pin'i", AI-D34 c): the SHA-256 of a tool's model card — identity,
/// version, owner, risk, data class, description, required permissions and field allowlist — in one canonical JSON
/// form. The model plans from this card, so a card that changed after a human approved it (a reworded description, a
/// widened field list, a new permission) is a different tool to the model. The catalog admits a tool only while the
/// configured pin equals the pin of the registered card; a missing or stale pin keeps the tool out (default deny).
/// The pin is evidence of what was approved, not an authority: permission and data class checks still run per call.
/// </summary>
public static class AiToolDescriptionPinV1
{
    public const string Format = "goymen.ai.tool-card.v1";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The canonical model card the pin is computed over.</summary>
    public static string CanonicalCard(AiToolDescriptorV1 descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return AiCanonicalJson.Canonicalize(JsonSerializer.Serialize(new
        {
            format = Format,
            toolId = descriptor.ToolId,
            version = descriptor.Version,
            ownerModule = descriptor.OwnerModule,
            risk = descriptor.Risk.ToString(),
            dataClass = descriptor.DataClass.ToString(),
            description = descriptor.Description,
            requiredPermissions = descriptor.RequiredPermissions,
            fieldAllowlist = descriptor.FieldAllowlist,
        }, Web));
    }

    /// <summary>Lower-case hex SHA-256 of <see cref="CanonicalCard"/>.</summary>
    public static string Compute(AiToolDescriptorV1 descriptor) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalCard(descriptor))));

    /// <summary>True only for a well-formed pin that equals the registered card's pin (ordinal, lower-case hex).</summary>
    public static bool Matches(AiToolDescriptorV1 descriptor, string? approvedPin) =>
        IsWellFormed(approvedPin) && string.Equals(Compute(descriptor), approvedPin, StringComparison.Ordinal);

    public static bool IsWellFormed(string? pin) =>
        pin is { Length: 64 } && pin.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
