namespace GoymenERP.Modules.Organization.Infrastructure.Seeding;

public sealed class OrganizationSeedOptions
{
    public const string SectionName = "OrganizationSeed";

    public List<SeedCompany> Companies { get; init; } = [];

    public List<SeedSiteTemplate> SiteTemplates { get; init; } = [];

    public List<SeedWorkCenter> WorkCenters { get; init; } = [];
}

public sealed class SeedCompany
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string LegalName { get; init; } = string.Empty;

    public string? Vkn { get; init; }

    public bool IsPlaceholder { get; init; }

    public bool InvoiceCapable { get; init; }

    public bool ApplyTopologyTemplates { get; init; }
}

public sealed class SeedSiteTemplate
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public List<SeedLocation> Locations { get; init; } = [];
}

public sealed class SeedLocation
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}

public sealed class SeedWorkCenter
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string LocationCode { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;
}
