using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.ArchitectureTests;

public sealed class SqlServerSearchCapabilityInventoryGuardTests
{
    private const string ExpectedNormalizedSqlSha256 =
        "9344df23a2188d5fd20289291fc726fcd72bf51b5d96dc5af2a33ed6c2a7e540";

    [Fact]
    public void Search_capability_inventory_matches_reviewed_read_only_sql()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sqlPath = Path.Combine(
            repositoryRoot,
            "tools",
            "erp000i",
            "sql_server_search_capability_inventory.sql");
        var normalizedSql = File.ReadAllText(sqlPath)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var actualDigest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSql)))
            .ToLowerInvariant();

        Assert.Equal(ExpectedNormalizedSqlSha256, actualDigest);
    }

    [Theory]
    [InlineData("Global Search PoC kabulü değildir")]
    [InlineData("veritabanı sağlayıcısı kabulü değildir")]
    [InlineData("collation kararı veya kabulü değildir")]
    [InlineData("performans/operasyon eşiği kabulü değildir")]
    [InlineData("BU ŞABLON KARAR KAPATMAZ")]
    [InlineData("SQLCMD ön işlemesi kapalı")]
    public void Evidence_template_keeps_decisions_open(string requiredBoundary)
    {
        var repositoryRoot = FindRepositoryRoot();
        var evidencePath = Path.Combine(
            repositoryRoot,
            "docs",
            "evidence",
            "erp-000i",
            "SQL_SERVER_SEARCH_CAPABILITY_INVENTORY_EVIDENCE_TEMPLATE.md");
        var evidence = File.ReadAllText(evidencePath);

        Assert.Contains(requiredBoundary, evidence, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("GoymenERP repository root was not found.");
    }
}
