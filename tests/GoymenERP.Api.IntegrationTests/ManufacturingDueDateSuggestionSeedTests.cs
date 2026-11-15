using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-D91 (SINAN-CEVAP 34.24, completing URT-D89): the termin safety margin starts at 0 and no ready value (seed) is
/// written; the work center's input capacity (URT-D89) is likewise entered in Settings, never shipped. These facts read
/// the places a value could reach a fresh company without a person entering it: the shipped seed files carry neither
/// the termin capacity key nor a safety margin, and no Settings migration or approved manufacturing profile catalog
/// entry inserts the capacity key. (A migration may still create the margin's own table: only its value is not
/// seeded.) Today's termin behavior (URT-S10) does not change.
/// </summary>
public sealed class ManufacturingDueDateSuggestionSeedTests
{
    private const string SeedDirectory = "src/backend/GoymenERP.Api/Seed";
    private const string SettingsMigrationsDirectory =
        "src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations";
    private const string ApprovedProfileCatalogPath =
        "src/backend/Modules/Settings/GoymenERP.Modules.Settings.Application/ApprovedManufacturingProfileCatalog.cs";
    private const string SafetyMarginToken = "safetymargin";

    [Fact]
    public void No_shipped_seed_carries_the_termin_capacity_or_a_safety_margin()
    {
        var seeds = Directory.GetFiles(Path.Combine(FindRepositoryRoot(), SeedDirectory), "*.json");
        Assert.NotEmpty(seeds);
        foreach (var seed in seeds)
        {
            var text = File.ReadAllText(seed);
            AssertCarriesNoCapacityKey(text);
            Assert.DoesNotContain(SafetyMarginToken, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void No_settings_migration_inserts_the_termin_capacity()
    {
        var migrations = Directory.GetFiles(
            Path.Combine(FindRepositoryRoot(), SettingsMigrationsDirectory), "*.cs");
        Assert.NotEmpty(migrations);
        foreach (var migration in migrations)
        {
            AssertCarriesNoCapacityKey(File.ReadAllText(migration));
        }
    }

    [Fact]
    public void The_approved_profile_catalog_ships_no_termin_capacity()
    {
        var catalog = Path.Combine(FindRepositoryRoot(), ApprovedProfileCatalogPath);
        Assert.True(File.Exists(catalog), catalog);
        AssertCarriesNoCapacityKey(File.ReadAllText(catalog));
    }

    private static void AssertCarriesNoCapacityKey(string text) =>
        Assert.DoesNotContain(ManufacturingDueDateSuggestionV1.CapacityParameterKey, text, StringComparison.Ordinal);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
