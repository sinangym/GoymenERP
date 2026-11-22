using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Tren-1 K3 fix on real SQL: CK_InvoiceLines_KilogramInformation as the migrations deployed it keeps the theoretical and actual
/// kilograms of a piece invoice line together. Under three-valued logic `[ActualKg] > 0` on NULL was UNKNOWN and half the fact
/// passed the CHECK; now it is refused (547), while both or neither still pass. SENTETIK.
/// </summary>
public sealed partial class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_deployed_invoice_line_kilogram_check_refuses_half_the_kilogram_fact_and_keeps_both_or_neither()
    {
        await using var run = await Run.StartAsync();
        const string columns = "[TheoreticalKg] decimal(18,3) NULL, [ActualKg] decimal(18,3) NULL";

        foreach (var half in new[] { "12.5, NULL", "NULL, 12.5" })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(KilogramCheckProbe(columns, half)));
            Assert.Equal(547, refused.Number);
        }

        await run.ExecuteAsync(KilogramCheckProbe(columns, "12.5, 12.75"));
        await run.ExecuteAsync(KilogramCheckProbe(columns, "NULL, NULL"));
    }

    /// <summary>The deployed definition (sys.check_constraints) on a scratch table, one row inserted; 547 on a refusal.</summary>
    private static string KilogramCheckProbe(string columns, string values) => $"""
        DECLARE @definition nvarchar(max) = (SELECT definition FROM sys.check_constraints WHERE name = N'CK_InvoiceLines_KilogramInformation');
        IF @definition IS NULL THROW 50000, N'CK_InvoiceLines_KilogramInformation is not deployed.', 1;
        DECLARE @sql nvarchar(max) = N'CREATE TABLE #probe ({columns}, CHECK ' + @definition
            + N'); INSERT INTO #probe VALUES ({values});';
        EXEC sp_executesql @sql;
        """;
}
