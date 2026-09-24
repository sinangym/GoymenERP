using System.Collections.ObjectModel;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>
/// One exact, explicitly supplied Mikro account identity. Neither the code nor the opaque
/// Accounting account id is derived here. This contract contains no classification or fallback.
/// </summary>
public sealed class AccountPlanMirrorRowV1
{
    public AccountPlanMirrorRowV1(AccountRef account, string code, string name)
    {
        AccountPlanMirrorV1.RequireText(code, nameof(code));
        if (account == default || string.IsNullOrWhiteSpace(name))
        {
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                "A mirrored account needs an explicit identity, code and name.");
        }

        Account = account;
        Code = code;
        Name = name;
    }

    public AccountRef Account { get; }
    public string Code { get; }
    public string Name { get; }
}

/// <summary>
/// Immutable admission contract for the approved, versioned, read-only Mikro mirror
/// (Business #66/5811361336). It is NOT an import parser, approval writer or a second account master.
/// The eventual trusted import adapter must verify the checksum against the actual approved file
/// and verify the approval evidence before constructing this value. A well-formed digest alone
/// proves neither file availability nor approval. No source, identity or approval is generated here.
/// </summary>
public sealed class AccountPlanMirrorV1
{
    private readonly ReadOnlyCollection<AccountPlanMirrorRowV1> _rows;
    private readonly Dictionary<string, AccountPlanMirrorRowV1> _byCode;
    private readonly Dictionary<Guid, AccountPlanMirrorRowV1> _byId;

    public AccountPlanMirrorV1(Guid companyId, string sourceVersion, string sourceSha256,
        Guid approvedByUserId, DateTimeOffset approvedAtUtc, IEnumerable<AccountPlanMirrorRowV1> rows)
    {
        RequireText(sourceVersion, nameof(sourceVersion));
        if (companyId == Guid.Empty || approvedByUserId == Guid.Empty || approvedAtUtc == default
            || approvedAtUtc.Offset != TimeSpan.Zero || !IsSha256(sourceSha256))
        {
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                "A mirror needs company, explicit source version/SHA-256 and UTC approval provenance.");
        }

        ArgumentNullException.ThrowIfNull(rows);
        var copy = rows.ToArray();
        if (copy.Length == 0 || copy.Any(row => row is null))
        {
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                "An approved account mirror must contain explicit non-null account rows.");
        }

        _byCode = new Dictionary<string, AccountPlanMirrorRowV1>(StringComparer.Ordinal);
        _byId = new Dictionary<Guid, AccountPlanMirrorRowV1>();
        foreach (var row in copy)
        {
            if (row.Account.CompanyId != companyId)
            {
                throw new AccountingDomainException(AccountingErrorCodes.CompanyMismatch,
                    "Every mirrored account must belong to the mirror company.");
            }

            if (!_byCode.TryAdd(row.Code, row) || !_byId.TryAdd(row.Account.AccountId, row))
            {
                throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                    "A mirror cannot contain duplicate account codes or account identities.");
            }
        }

        CompanyId = companyId;
        SourceVersion = sourceVersion;
        SourceSha256 = sourceSha256;
        ApprovedByUserId = approvedByUserId;
        ApprovedAtUtc = approvedAtUtc;
        _rows = Array.AsReadOnly(copy);
    }

    public Guid CompanyId { get; }
    public string SourceVersion { get; }
    public string SourceSha256 { get; }
    public Guid ApprovedByUserId { get; }
    public DateTimeOffset ApprovedAtUtc { get; }
    public IReadOnlyList<AccountPlanMirrorRowV1> Rows => _rows;

    /// <summary>Exact ordinal lookup: never trims, parses, renames, creates or falls back.</summary>
    public AccountPlanMirrorRowV1? FindByCode(string? code) =>
        code is not null && _byCode.TryGetValue(code, out var row) ? row : null;

    public AccountPlanMirrorRowV1? Find(AccountRef account) =>
        account.CompanyId == CompanyId && _byId.TryGetValue(account.AccountId, out var row) ? row : null;

    // Compare values rather than object identity, so independently rehydrated snapshots work.
    internal bool Matches(AccountPlanMirrorV1 other) => CompanyId == other.CompanyId
        && string.Equals(SourceVersion, other.SourceVersion, StringComparison.Ordinal)
        && string.Equals(SourceSha256, other.SourceSha256, StringComparison.OrdinalIgnoreCase)
        && ApprovedByUserId == other.ApprovedByUserId && ApprovedAtUtc == other.ApprovedAtUtc
        && Rows.Count == other.Rows.Count
        && Rows.All(row => other.Find(row.Account) is { } match
            && string.Equals(row.Code, match.Code, StringComparison.Ordinal)
            && string.Equals(row.Name, match.Name, StringComparison.Ordinal));

    internal static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                $"{field} must be supplied without surrounding whitespace.");
        }
    }

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
