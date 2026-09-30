using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Documents.Domain;

/// <summary>
/// The owner record a document is linked to (DEC-DOC-001's "Kayıt Bağlantısı"): a link kind the owning module declared
/// ("counterparty.party") and the owner record's id. The vault never reads the owner's tables; whether the record
/// exists in the context company and whether the actor may read it is the owner's answer through its declared port.
/// </summary>
public sealed partial record DocumentOwnerLink
{
    public const int MaximumKindLength = 64;

    private DocumentOwnerLink(string kind, Guid ownerId)
    {
        Kind = kind;
        OwnerId = ownerId;
    }

    public string Kind { get; }

    public Guid OwnerId { get; }

    public static DocumentOwnerLink? TryCreate(string? kind, Guid ownerId) =>
        kind is not null && kind.Length <= MaximumKindLength && KindPattern().IsMatch(kind) && ownerId != Guid.Empty
            ? new DocumentOwnerLink(kind, ownerId)
            : null;

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex KindPattern();
}
