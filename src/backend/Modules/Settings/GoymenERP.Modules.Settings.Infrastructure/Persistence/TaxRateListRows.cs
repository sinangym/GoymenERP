using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>A rate list of one company (SAT-D181; the edit fence of its versions). Created with its first version.</summary>
internal sealed class TaxRateListRow
{
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public long Version { get; set; }
    public long LastListVersion { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>One immutable, whole rate list version; its entries are <see cref="TaxRateListEntryRow"/>.</summary>
internal sealed class TaxRateListVersionRow
{
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public long ListVersion { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>One rate of a list version: its code, name, exact percent and (withholding ratio only) its GİB withholding code.</summary>
internal sealed class TaxRateListEntryRow
{
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public long ListVersion { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Percent { get; set; }
    public string? TaxCode { get; set; }
}

/// <summary>The withdrawal of a rate list version before it took effect (insert-only).</summary>
internal sealed class TaxRateListWithdrawalRow
{
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public long ListVersion { get; set; }
    public long? ReplacedByListVersion { get; set; }
    public DateTimeOffset WithdrawnAtUtc { get; set; }
    public string WithdrawnBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>The stored rate list rows of one company, read under the company filter.</summary>
internal sealed record TaxRateListRows(
    IReadOnlyList<TaxRateListRow> Lists,
    IReadOnlyList<TaxRateListVersionRow> Versions,
    IReadOnlyList<TaxRateListEntryRow> Entries,
    IReadOnlyList<TaxRateListWithdrawalRow> Withdrawals)
{
    public static async Task<TaxRateListRows> ReadAsync(SettingsDbContext database, bool track,
        CancellationToken cancellationToken)
    {
        var lists = track
            ? await database.TaxRateLists.ToListAsync(cancellationToken)
            : await database.TaxRateLists.AsNoTracking().ToListAsync(cancellationToken);
        var versions = await database.TaxRateListVersions.AsNoTracking().ToListAsync(cancellationToken);
        var entries = await database.TaxRateListEntries.AsNoTracking().ToListAsync(cancellationToken);
        var withdrawals = await database.TaxRateListWithdrawals.AsNoTracking().ToListAsync(cancellationToken);
        return new TaxRateListRows(lists, versions, entries, withdrawals);
    }

    /// <summary>The company's versions, the reviewed (empty) SAT-D181 catalog of <paramref name="companyId"/> included.</summary>
    public IReadOnlyList<TaxRateListVersionDataV1> ToVersions(Guid companyId)
    {
        var withdrawn = Withdrawals.Select(withdrawal => (withdrawal.Kind, withdrawal.ListVersion)).ToHashSet();
        var entries = Entries
            .GroupBy(entry => (entry.Kind, entry.ListVersion))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TaxRateListEntryViewV1>)group
                .OrderBy(entry => entry.Code, StringComparer.Ordinal)
                .Select(entry => new TaxRateListEntryViewV1(entry.Code, entry.Name, entry.Percent, entry.TaxCode))
                .ToArray());
        return ApprovedTaxRateListCatalog.For(companyId)
            .Concat(Versions.Select(row => new TaxRateListVersionDataV1(
                row.Kind,
                row.ListVersion,
                row.EffectiveFrom,
                entries.GetValueOrDefault((row.Kind, row.ListVersion)) ?? [],
                withdrawn.Contains((row.Kind, row.ListVersion)))))
            .ToArray();
    }
}
