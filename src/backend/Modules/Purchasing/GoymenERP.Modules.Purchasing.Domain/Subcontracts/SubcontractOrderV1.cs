namespace GoymenERP.Modules.Purchasing.Domain.Subcontracts;

public enum SubcontractOrderStatusV1 { Draft = 1, Admitted = 2 }

public sealed record SubcontractOrderLineV1(Guid LineId, Guid ServiceId, string ServiceVersion,
    Guid ServiceUnitId, string ServiceQuantity, Guid SourcePhysicalRequestId, string SourcePhysicalRequestVersion);

public sealed class SubcontractOrderV1
{
    private readonly IReadOnlyList<SubcontractOrderLineV1> lines;
    private SubcontractOrderV1(Guid id, Guid companyId, Guid supplierPartyId, string supplierPartyVersion,
        Guid companyAccountId, string companyAccountVersion, SubcontractOrderStatusV1 status, long version,
        DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc, IReadOnlyList<SubcontractOrderLineV1> lines)
    {
        Id = RequireId(id, nameof(id)); CompanyId = RequireId(companyId, nameof(companyId));
        SupplierPartyId = RequireId(supplierPartyId, nameof(supplierPartyId));
        SupplierPartyVersion = Token(supplierPartyVersion, nameof(supplierPartyVersion));
        CompanyAccountId = RequireId(companyAccountId, nameof(companyAccountId));
        CompanyAccountVersion = Token(companyAccountVersion, nameof(companyAccountVersion));
        if (!Enum.IsDefined(status) || version < 1 || createdAtUtc == default || updatedAtUtc < createdAtUtc)
            throw new ArgumentException("Invalid subcontract order state.");
        if (lines is null || lines.Count is < 1 or > 500 || lines.Select(x => x.LineId).Distinct().Count() != lines.Count)
            throw new ArgumentException("A subcontract order requires unique lines.", nameof(lines));
        foreach (var line in lines) Validate(line);
        Status = status; Version = version; CreatedAtUtc = createdAtUtc; UpdatedAtUtc = updatedAtUtc;
        this.lines = [.. lines];
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public Guid SupplierPartyId { get; }
    public string SupplierPartyVersion { get; }
    public Guid CompanyAccountId { get; }
    public string CompanyAccountVersion { get; }
    public SubcontractOrderStatusV1 Status { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyList<SubcontractOrderLineV1> Lines => lines;

    public static SubcontractOrderV1 Create(Guid id, Guid companyId, Guid supplierPartyId,
        string supplierPartyVersion, Guid companyAccountId, string companyAccountVersion,
        IReadOnlyList<SubcontractOrderLineV1> lines, DateTimeOffset now) =>
        new(id, companyId, supplierPartyId, supplierPartyVersion, companyAccountId, companyAccountVersion,
            SubcontractOrderStatusV1.Draft, 1, now, now, lines);

    public static SubcontractOrderV1 Restore(Guid id, Guid companyId, Guid supplierPartyId,
        string supplierPartyVersion, Guid companyAccountId, string companyAccountVersion,
        SubcontractOrderStatusV1 status, long version, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc,
        IReadOnlyList<SubcontractOrderLineV1> lines) => new(id, companyId, supplierPartyId,
            supplierPartyVersion, companyAccountId, companyAccountVersion, status, version, createdAtUtc, updatedAtUtc, lines);

    public void Admit(long expectedVersion, DateTimeOffset now)
    {
        if (expectedVersion != Version) throw new SubcontractOrderVersionConflictV1();
        if (Status != SubcontractOrderStatusV1.Draft) throw new SubcontractOrderStateConflictV1();
        Status = SubcontractOrderStatusV1.Admitted; Version = checked(Version + 1); UpdatedAtUtc = now;
    }

    private static void Validate(SubcontractOrderLineV1 line)
    {
        ArgumentNullException.ThrowIfNull(line);
        RequireId(line.LineId, nameof(line.LineId)); RequireId(line.ServiceId, nameof(line.ServiceId));
        RequireId(line.ServiceUnitId, nameof(line.ServiceUnitId)); RequireId(line.SourcePhysicalRequestId, nameof(line.SourcePhysicalRequestId));
        Token(line.ServiceVersion, nameof(line.ServiceVersion)); Token(line.SourcePhysicalRequestVersion, nameof(line.SourcePhysicalRequestVersion));
        if (string.IsNullOrWhiteSpace(line.ServiceQuantity) || line.ServiceQuantity.Length > 80
            || !string.Equals(line.ServiceQuantity, line.ServiceQuantity.Trim(), StringComparison.Ordinal)
            || line.ServiceQuantity.Any(char.IsControl))
            throw new ArgumentException("Canonical service quantity text is required.", nameof(line.ServiceQuantity));
    }

    private static Guid RequireId(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException("Identity is required.", name) : value;
    private static string Token(string? value, string name) => string.IsNullOrWhiteSpace(value) || value.Length > 64
        || !string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Any(char.IsControl)
        ? throw new ArgumentException("Canonical version is required.", name) : value;
}

public sealed class SubcontractOrderVersionConflictV1 : InvalidOperationException { }
public sealed class SubcontractOrderStateConflictV1 : InvalidOperationException { }
