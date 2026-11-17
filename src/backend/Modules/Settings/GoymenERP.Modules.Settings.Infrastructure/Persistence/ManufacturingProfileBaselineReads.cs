using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>Immutable version-0 snapshot. A transferred scope never consults the compiled catalog again.</summary>
internal sealed class ManufacturingProfileBaselineTransferRow
{
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid TransferId { get; set; }
    public long Version { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public string SnapshotHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public DateTimeOffset TransferredAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

internal static class ManufacturingProfileBaselineReads
{
    internal const string Parameters = "parameters";
    internal const string Tolerances = "tolerances";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    internal static async Task<(IReadOnlyList<ApprovedManufacturingParameterProfileEntry> Values, string Version)> ParametersAsync(
        SettingsDbContext database, Guid company, CancellationToken cancellationToken)
    {
        var row = await RowAsync(database, company, Parameters, cancellationToken);
        if (row is null) return (ApprovedManufacturingProfileCatalog.ParametersFor(company), "0");
        var values = Decode<ApprovedManufacturingParameterProfileEntry>(row);
        if (values.Any(value => value.CompanyId != company || value.ProfileId == Guid.Empty
                || !SettingsManufacturingProcessesV1.IsCanonical(value.Process) || value.Values is null || value.Values.Count == 0
                || value.Values.Any(item => !ManufacturingProfileValueRulesV1.IsWellFormed(value.Process, item)))
            || values.Select(value => value.ProfileId).Distinct().Count() != values.Length
            || values.Select(value => value.Process).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw Invalid();
        return (values, "1");
    }

    internal static async Task<(IReadOnlyList<ApprovedManufacturingToleranceStartingValue> Values, string Version)> TolerancesAsync(
        SettingsDbContext database, Guid company, CancellationToken cancellationToken)
    {
        var row = await RowAsync(database, company, Tolerances, cancellationToken);
        if (row is null) return (ApprovedManufacturingProfileCatalog.ToleranceFor(company), "0");
        var values = Decode<ApprovedManufacturingToleranceStartingValue>(row);
        if (values.Any(value => value.CompanyId != company || value.ProfileId == Guid.Empty
                || !SettingsManufacturingProcessesV1.IsCanonical(value.Process)
                || value.MaximumFirePercent is < 0 or > 100 || value.MassBalanceDifferenceTolerancePercent is < 0 or > 100)
            || values.Select(value => value.ProfileId).Distinct().Count() != values.Length
            || values.Select(value => value.Process).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw Invalid();
        return (values, "1");
    }

    private static Task<ManufacturingProfileBaselineTransferRow?> RowAsync(SettingsDbContext database, Guid company,
        string kind, CancellationToken cancellationToken) => database.ManufacturingProfileBaselineTransfers.AsNoTracking()
        .SingleOrDefaultAsync(row => row.CompanyId == company && row.Kind == kind, cancellationToken);

    private static T[] Decode<T>(ManufacturingProfileBaselineTransferRow row)
    {
        if (row.Version != 1 || row.TransferId == Guid.Empty || row.SnapshotHash != Hash(row.SnapshotJson)) throw Invalid();
        try
        {
            var values = JsonSerializer.Deserialize<T[]>(row.SnapshotJson, Json);
            return values is { Length: > 0 } && values.All(value => value is not null) ? values : throw Invalid();
        }
        catch (JsonException) { throw Invalid(); }
    }

    private static SettingsPrerequisiteException Invalid() => new("settings_profile_invalid");
}
