using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Ai;

/// <summary>
/// WP-A3a: what the Cari READ tools release about a party. A natural person's legal and trade names are personal data
/// (AI-D34 d, AI-D36): they are masked with <see cref="PersonMask"/> in every tool output; a legal entity's names are
/// released. Roles are released as their owner names in a fixed order.
/// </summary>
internal static class CounterpartyAiRelease
{
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
