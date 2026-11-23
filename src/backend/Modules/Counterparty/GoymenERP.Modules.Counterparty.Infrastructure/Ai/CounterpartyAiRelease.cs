using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Ai;

/// <summary>
/// WP-A3a: what the Cari READ tools release about a party. A natural person's legal and trade names are personal data
/// (AI-D34 d, AI-D36): they are masked with <see cref="PersonMask"/> in the party outputs (the title stays Internal, AI-D42);
/// a legal entity's names are released. Roles are released as their owner names in a fixed order.
/// </summary>
internal static class CounterpartyAiRelease
{
    /// <summary>
    /// AI-D42 (SINAN_KARARLARI_2026-11-22_EK_SORULAR.md): the data class of every field a Cari READ tool may release. The
    /// authorised person's name, phone, e-mail and TCKN are Confidential (in-house model only); the party's title, codes,
    /// roles, balances and amounts are Internal. A tool's effective class is the highest class of the fields it returned.
    /// TCKN, addresses and the website are not released by any Cari tool.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, AiDataClassV1> FieldClasses = new Dictionary<string, AiDataClassV1>(StringComparer.Ordinal)
    {
        ["code"] = AiDataClassV1.Internal,
        ["kind"] = AiDataClassV1.Internal,
        ["legalName"] = AiDataClassV1.Internal,
        ["tradeName"] = AiDataClassV1.Internal,
        ["roles"] = AiDataClassV1.Internal,
        ["isActive"] = AiDataClassV1.Internal,
        ["count"] = AiDataClassV1.Internal,
        ["contactName"] = AiDataClassV1.Confidential,
        ["contactJobTitle"] = AiDataClassV1.Internal,
        ["phone"] = AiDataClassV1.Confidential,
        ["email"] = AiDataClassV1.Confidential,
        // FNS-D121 / AI-D42: a cari IBAN is Confidential personal data; no Cari tool releases it.
        ["iban"] = AiDataClassV1.Confidential,
    };

    public const string FieldPolicy = "AI-D42";

    /// <summary>The highest class of <paramref name="releasedFields"/> (Internal when none is released).</summary>
    public static AiDataClassV1 EffectiveClass(IEnumerable<string> releasedFields) =>
        releasedFields.Select(field => FieldClasses[field]).DefaultIfEmpty(AiDataClassV1.Internal).Max();

    /// <summary>The released fields with their classes, in ordinal order, for the tool output.</summary>
    public static IReadOnlyDictionary<string, string> Classes(IEnumerable<string> releasedFields) =>
        new SortedDictionary<string, string>(releasedFields.Distinct(StringComparer.Ordinal)
            .ToDictionary(field => field, field => FieldClasses[field].ToString(), StringComparer.Ordinal), StringComparer.Ordinal);

    /// <summary>The fields every party output releases (all Internal under AI-D42).</summary>
    public static readonly IReadOnlyList<string> PartyFields = ["code", "isActive", "kind", "legalName", "roles", "tradeName"];

    public const string PersonMask = "[KİŞİ]";
    public const string Masking = "AI-D36 dogal kisi adi maskeli";

    public static readonly IReadOnlyList<(PartyRole Role, string Name)> Roles =
    [
        (PartyRole.Customer, nameof(PartyRole.Customer)),
        (PartyRole.Supplier, nameof(PartyRole.Supplier)),
        (PartyRole.Tolling, nameof(PartyRole.Tolling)),
        (PartyRole.Service, nameof(PartyRole.Service)),
    ];

    public static bool IsKnownKind(PartyKind kind) => kind is PartyKind.LegalEntity or PartyKind.NaturalPerson;

    public static bool IsKnownRoles(PartyRole roles) =>
        (roles & ~(PartyRole.Customer | PartyRole.Supplier | PartyRole.Tolling | PartyRole.Service)) == PartyRole.None;

    public static string? Name(PartyKind kind, string? name) =>
        name is null ? null : kind == PartyKind.NaturalPerson ? PersonMask : name;

    public static IReadOnlyList<string> RoleNames(PartyRole roles) =>
        [.. Roles.Where(entry => roles.HasFlag(entry.Role)).Select(entry => entry.Name)];
}

/// <summary>One party as a Cari READ tool releases it: names masked for a natural person; only the context company's account.</summary>
public sealed record CounterpartyAiPartyV1(
    Guid PartyId,
    string Code,
    string Kind,
    string? LegalName,
    string? TradeName,
    bool NameMasked,
    bool IsActive,
    string Version,
    CounterpartyAiAccountV1 Account);

public sealed record CounterpartyAiAccountV1(Guid AccountId, IReadOnlyList<string> Roles, bool IsActive, string Version);
